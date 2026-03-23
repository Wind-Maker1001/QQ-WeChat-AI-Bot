import { spawn } from 'node:child_process';
import path from 'node:path';

import { loadRuntimeConfig } from './adapters/config/load-runtime-config.mjs';
import {
  readRuntimeConfigFromEnvFile,
  readControlConfig,
  writeControlConfig
} from './adapters/config/control-config-file.mjs';
import {
  createControlApiServer,
  DEFAULT_CONTROL_API_HOST,
  DEFAULT_CONTROL_API_PORT
} from './app/control-api.mjs';
import { formatError } from './utils.mjs';

const WORKER_ENTRY = path.resolve(process.cwd(), 'src', 'app', 'runtime-worker.mjs');
const WORKER_RESTART_DELAY_MS = 1000;
const BOOT_FAILURE_WINDOW_MS = 5000;
const MAX_CONSECUTIVE_BOOT_FAILURES = 3;

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

async function main() {
  const startedAt = new Date().toISOString();
  let lastConfigSavedAt = null;
  let desiredRuntimeActive = true;
  let worker = null;
  let workerRestartTimer = null;
  let stoppingWorker = false;
  let consecutiveBootFailures = 0;
  let lastWorkerStartedAt = 0;
  let runtimeStatus = {
    runtimeActive: false,
    napcatConnected: false,
    activeLockCount: 0,
    workerProcessId: null,
    workerStartedAt: null
  };
  const logger = {
    info: logInfo,
    error: logError
  };

  function buildStatusPayload() {
    return {
      startedAt,
      processId: process.pid,
      runtimeActive: runtimeStatus.runtimeActive,
      napcatConnected: runtimeStatus.napcatConnected,
      activeLockCount: runtimeStatus.activeLockCount,
      configRestartRequired: false,
      lastConfigSavedAt,
      controlApiUrl: `http://${DEFAULT_CONTROL_API_HOST}:${DEFAULT_CONTROL_API_PORT}`,
      configPath: `${process.cwd()}\\.env`,
      workerProcessId: runtimeStatus.workerProcessId,
      workerStartedAt: runtimeStatus.workerStartedAt
    };
  }

  function clearWorkerRestartTimer() {
    if (workerRestartTimer) {
      clearTimeout(workerRestartTimer);
      workerRestartTimer = null;
    }
  }

  function scheduleWorkerRestart(reason) {
    clearWorkerRestartTimer();

    if (!desiredRuntimeActive) {
      return;
    }

    logInfo(`[supervisor] Worker restart scheduled in ${WORKER_RESTART_DELAY_MS}ms: ${reason}`);
    workerRestartTimer = setTimeout(() => {
      workerRestartTimer = null;
      void startWorker(`restart:${reason}`);
    }, WORKER_RESTART_DELAY_MS);
  }

  function attachWorker(child, reason) {
    lastWorkerStartedAt = Date.now();
    runtimeStatus = {
      runtimeActive: true,
      napcatConnected: false,
      activeLockCount: 0,
      workerProcessId: child.pid ?? null,
      workerStartedAt: new Date().toISOString()
    };
    worker = child;
    logInfo(`[supervisor] Worker started: pid=${child.pid}, reason=${reason}`);

    child.stdout?.on('data', (chunk) => {
      process.stdout.write(chunk);
    });

    child.stderr?.on('data', (chunk) => {
      process.stderr.write(chunk);
    });

    child.on('message', (message) => {
      if (!message || typeof message !== 'object') {
        return;
      }

      if (message.type === 'status' && message.data && typeof message.data === 'object') {
        runtimeStatus = {
          ...runtimeStatus,
          runtimeActive: message.data.runtimeActive === true,
          napcatConnected: message.data.napcatConnected === true,
          activeLockCount:
            typeof message.data.activeLockCount === 'number' ? message.data.activeLockCount : 0
        };
      }
    });

    child.on('exit', (code, signal) => {
      const exitedWorker = worker === child;
      if (exitedWorker) {
        worker = null;
      }

      runtimeStatus = {
        runtimeActive: false,
        napcatConnected: false,
        activeLockCount: 0,
        workerProcessId: null,
        workerStartedAt: null
      };

      logInfo(
        `[supervisor] Worker exited: pid=${child.pid}, code=${code ?? 'none'}, signal=${signal ?? 'none'}`
      );

      if (!stoppingWorker && desiredRuntimeActive) {
        const workerLifetimeMs = Date.now() - lastWorkerStartedAt;

        if (workerLifetimeMs < BOOT_FAILURE_WINDOW_MS) {
          consecutiveBootFailures += 1;
          logError(
            `[supervisor] Worker exited too quickly (${workerLifetimeMs}ms). consecutive_boot_failures=${consecutiveBootFailures}`
          );
        } else {
          consecutiveBootFailures = 0;
        }

        if (consecutiveBootFailures >= MAX_CONSECUTIVE_BOOT_FAILURES) {
          desiredRuntimeActive = false;
          logError(
            `[supervisor] Worker restart disabled after ${consecutiveBootFailures} consecutive boot failures. Fix config or runtime errors, then call /start again.`
          );
          return;
        }

        scheduleWorkerRestart('unexpected-exit');
      }
    });
  }

  async function startWorker(reason = 'manual-start') {
    if (worker) {
      return false;
    }

    clearWorkerRestartTimer();
    const child = spawn(process.execPath, [WORKER_ENTRY], {
      cwd: process.cwd(),
      stdio: ['ignore', 'pipe', 'pipe', 'ipc']
    });
    attachWorker(child, reason);
    return true;
  }

  async function stopWorker(reason = 'manual-stop') {
    clearWorkerRestartTimer();

    if (!worker) {
      runtimeStatus = {
        runtimeActive: false,
        napcatConnected: false,
        activeLockCount: 0,
        workerProcessId: null,
        workerStartedAt: null
      };
      return false;
    }

    stoppingWorker = true;
    const child = worker;

    await new Promise((resolve) => {
      const timeoutId = setTimeout(() => {
        if (!child.killed) {
          child.kill('SIGKILL');
        }
      }, 3000);

      child.once('exit', () => {
        clearTimeout(timeoutId);
        resolve();
      });

      child.kill('SIGTERM');
    });

    stoppingWorker = false;
    logInfo(`[supervisor] Worker stopped: reason=${reason}`);
    return true;
  }

  async function startRuntime(source = 'control-api') {
    desiredRuntimeActive = true;
    consecutiveBootFailures = 0;
    await startWorker(source);
    return buildStatusPayload();
  }

  async function stopRuntime(source = 'control-api') {
    desiredRuntimeActive = false;
    consecutiveBootFailures = 0;
    await stopWorker(source);
    return buildStatusPayload();
  }

  const controlApi = createControlApiServer({
    host: DEFAULT_CONTROL_API_HOST,
    port: DEFAULT_CONTROL_API_PORT,
    logger,
    getStatus: async () => buildStatusPayload(),
    getConfig: async () => {
      const { runtimeConfig } = await readRuntimeConfigFromEnvFile({
        cwd: process.cwd()
      });
      const result = await readControlConfig({
        cwd: process.cwd(),
        runtimeConfig
      });

      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false
      };
    },
    updateConfig: async (payload) => {
      const { runtimeConfig } = await readRuntimeConfigFromEnvFile({
        cwd: process.cwd()
      });
      const result = await writeControlConfig({
        cwd: process.cwd(),
        runtimeConfig,
        config: payload
      });

      lastConfigSavedAt = new Date().toISOString();

      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false,
        savedAt: lastConfigSavedAt
      };
    },
    startRuntime: async () => startRuntime(),
    stopRuntime: async () => stopRuntime()
  });

  try {
    await controlApi.start();
  } catch (error) {
    if (error && error.code === 'EADDRINUSE') {
      throw new Error(
        `Control API port ${DEFAULT_CONTROL_API_PORT} is already in use. Another supervisor may already be running.`
      );
    }

    throw error;
  }
  logInfo(`[control] Listening on http://${DEFAULT_CONTROL_API_HOST}:${DEFAULT_CONTROL_API_PORT}`);

  await startWorker('startup');

  async function shutdownSupervisor(signal) {
    logInfo(`[supervisor] Shutting down: signal=${signal}`);
    desiredRuntimeActive = false;
    clearWorkerRestartTimer();
    await stopWorker(`supervisor-${signal}`);
    process.exit(0);
  }

  process.on('SIGINT', () => {
    void shutdownSupervisor('SIGINT');
  });
  process.on('SIGTERM', () => {
    void shutdownSupervisor('SIGTERM');
  });
}

main().catch((error) => {
  logError(`[fatal] Supervisor startup failed: ${formatError(error)}`);
  process.exit(1);
});
