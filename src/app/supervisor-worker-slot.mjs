import { spawn } from 'node:child_process';

export function createSupervisorWorkerSlot({
  entryPath,
  label,
  restartDelayMs,
  bootFailureWindowMs,
  maxConsecutiveBootFailures,
  buildSpawnEnv,
  shouldKeepAlive,
  logInfo,
  logError,
  onAttach,
  onMessage,
  onExit,
  onAlreadyStopped,
  onRestartDisabled
}) {
  let child = null;
  let restartTimer = null;
  let stopping = false;
  let consecutiveBootFailures = 0;
  let lastStartedAt = 0;
  let lastInitMessage = null;

  function clearRestartTimer() {
    if (restartTimer) {
      clearTimeout(restartTimer);
      restartTimer = null;
    }
  }

  function scheduleRestart(reason) {
    clearRestartTimer();

    if (!shouldKeepAlive()) {
      return;
    }

    logInfo(`[supervisor] ${label} restart scheduled in ${restartDelayMs}ms: ${reason}`);
    restartTimer = setTimeout(() => {
      restartTimer = null;
      void start(`restart:${reason}`);
    }, restartDelayMs);
  }

  function attach(nextChild, reason) {
    child = nextChild;
    lastStartedAt = Date.now();
    onAttach?.(nextChild, reason);
    logInfo(`[supervisor] ${label} started: pid=${nextChild.pid}, reason=${reason}`);

    nextChild.stdout?.on('data', (chunk) => {
      process.stdout.write(chunk);
    });

    nextChild.stderr?.on('data', (chunk) => {
      process.stderr.write(chunk);
    });

    nextChild.on('message', (message) => {
      onMessage?.(message, nextChild);
    });

    nextChild.on('exit', (code, signal) => {
      const exitedCurrentChild = child === nextChild;

      if (exitedCurrentChild) {
        child = null;
      }

      onExit?.(nextChild, {
        code,
        signal,
        exitedCurrentChild
      });

      logInfo(
        `[supervisor] ${label} exited: pid=${nextChild.pid}, code=${code ?? 'none'}, signal=${signal ?? 'none'}`
      );

      if (!stopping && shouldKeepAlive()) {
        const workerLifetimeMs = Date.now() - lastStartedAt;

        if (workerLifetimeMs < bootFailureWindowMs) {
          consecutiveBootFailures += 1;
          logError(
            `[supervisor] ${label} exited too quickly (${workerLifetimeMs}ms). consecutive_boot_failures=${consecutiveBootFailures}`
          );
        } else {
          consecutiveBootFailures = 0;
        }

        if (consecutiveBootFailures >= maxConsecutiveBootFailures) {
          onRestartDisabled?.(consecutiveBootFailures);
          return;
        }

        scheduleRestart('unexpected-exit');
      }
    });
  }

  function send(message) {
    if (!child?.connected || !message || typeof message !== 'object') {
      return false;
    }

    child.send(message);
    return true;
  }

  async function start(reason = 'manual-start', initMessage = lastInitMessage) {
    if (child) {
      return false;
    }

    clearRestartTimer();
    lastInitMessage = initMessage ?? lastInitMessage;
    const nextChild = spawn(process.execPath, [entryPath], {
      cwd: process.cwd(),
      stdio: ['ignore', 'pipe', 'pipe', 'ipc'],
      env: buildSpawnEnv()
    });
    attach(nextChild, reason);

    if (lastInitMessage) {
      nextChild.send(lastInitMessage);
    }

    return true;
  }

  async function stop(reason = 'manual-stop') {
    clearRestartTimer();

    if (!child) {
      onAlreadyStopped?.();
      return false;
    }

    stopping = true;
    const activeChild = child;

    await new Promise((resolve) => {
      const timeoutId = setTimeout(() => {
        if (!activeChild.killed) {
          activeChild.kill('SIGKILL');
        }
      }, 3000);

      activeChild.once('exit', () => {
        clearTimeout(timeoutId);
        resolve();
      });

      activeChild.kill('SIGTERM');
    });

    stopping = false;
    logInfo(`[supervisor] ${label} stopped: reason=${reason}`);
    return true;
  }

  function isRunning() {
    return child !== null;
  }

  function getChild() {
    return child;
  }

  function resetBootFailures() {
    consecutiveBootFailures = 0;
  }

  return Object.freeze({
    clearRestartTimer,
    start,
    stop,
    send,
    isRunning,
    getChild,
    resetBootFailures
  });
}
