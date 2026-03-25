import { loadRuntimeConfig } from '../adapters/config/load-runtime-config.mjs';
import {
  readControlConfig,
  readRuntimeConfigFromEnvFile,
  writeControlConfig
} from '../adapters/config/control-config-file.mjs';
import { setWechatConfigured, shouldRunWechatWorker } from './supervisor-runtime-state.mjs';

export function createSupervisorRuntimeController({
  getRuntimeStatus,
  setRuntimeStatus,
  getDesiredRuntimeActive,
  setDesiredRuntimeActive,
  workerSlot,
  wechatWorkerSlot,
  buildStatusPayload
}) {
  let lastConfigSavedAt = null;

  function getStatus() {
    return buildStatusPayload(lastConfigSavedAt);
  }

  async function getConfig() {
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
  }

  async function startRuntime(source = 'control-api') {
    setDesiredRuntimeActive(true);
    workerSlot.resetBootFailures();
    wechatWorkerSlot.resetBootFailures();
    const runtimeSnapshot = await readRuntimeConfigFromEnvFile({
      cwd: process.cwd()
    });
    setRuntimeStatus(
      setWechatConfigured(
        getRuntimeStatus(),
        shouldRunWechatWorker(runtimeSnapshot.runtimeConfig)
      )
    );

    await workerSlot.start(source, runtimeSnapshot.envValues);

    if (shouldRunWechatWorker(runtimeSnapshot.runtimeConfig)) {
      await wechatWorkerSlot.start(source, runtimeSnapshot.envValues);
    }

    return getStatus();
  }

  async function stopRuntime(source = 'control-api') {
    setDesiredRuntimeActive(false);
    workerSlot.resetBootFailures();
    wechatWorkerSlot.resetBootFailures();
    await wechatWorkerSlot.stop(source);
    await workerSlot.stop(source);
    return getStatus();
  }

  async function updateConfig(payload) {
    const { runtimeConfig } = await readRuntimeConfigFromEnvFile({
      cwd: process.cwd()
    });
    const result = await writeControlConfig({
      cwd: process.cwd(),
      runtimeConfig,
      config: payload
    });
    const nextRuntimeConfig = loadRuntimeConfig({
      cwd: process.cwd(),
      env: result.envValues,
      loadDotenv: false
    });

    lastConfigSavedAt = new Date().toISOString();
    const shouldEnableWechatWorker = shouldRunWechatWorker(nextRuntimeConfig);

    setRuntimeStatus(
      setWechatConfigured(getRuntimeStatus(), shouldEnableWechatWorker)
    );

    if (getDesiredRuntimeActive()) {
      if (shouldEnableWechatWorker && !wechatWorkerSlot.isRunning()) {
        wechatWorkerSlot.resetBootFailures();
        await wechatWorkerSlot.start('config-update', result.envValues);
      }

      if (!shouldEnableWechatWorker && wechatWorkerSlot.isRunning()) {
        await wechatWorkerSlot.stop('config-update');
      }
    }

    return {
      ...result.config,
      envPath: result.envPath,
      restartRequired: false,
      savedAt: lastConfigSavedAt
    };
  }

  return Object.freeze({
    getStatus,
    getConfig,
    updateConfig,
    startRuntime,
    stopRuntime
  });
}
