import {
  readControlConfig,
  readRuntimeSettingsFromDisk,
  writeControlConfig
} from '../adapters/config/control-config-file.mjs';
import { setWechatConfigured, shouldRunWechatWorker } from './supervisor-runtime-state.mjs';

function buildWorkerConfigMessage(workerKind, runtimeSettingsSnapshot, source) {
  return {
    type: 'config:update',
    data: {
      workerKind,
      source,
      runtimeSettingsSnapshot
    }
  };
}

function buildWorkerInitPayload(workerKind, runtimeSettingsSnapshot, source) {
  return {
    type: 'config:init',
    data: {
      workerKind,
      source,
      runtimeSettingsSnapshot
    }
  };
}

export function createSupervisorRuntimeController({
  getRuntimeStatus,
  setRuntimeStatus,
  getDesiredRuntimeActive,
  setDesiredRuntimeActive,
  getRuntimeSettingsState,
  setRuntimeSettingsState,
  workerSlot,
  wechatWorkerSlot,
  buildStatusPayload
}) {
  let lastConfigSavedAt = getRuntimeSettingsState()?.runtimeSettingsSnapshot?.savedAt ?? null;

  async function refreshRuntimeSettingsState() {
    const runtimeSettingsState = await readRuntimeSettingsFromDisk({
      cwd: process.cwd()
    });
    setRuntimeSettingsState(runtimeSettingsState);
    return runtimeSettingsState;
  }

  function getStatus() {
    return buildStatusPayload(lastConfigSavedAt);
  }

  async function getConfig() {
    const result = await readControlConfig({
      cwd: process.cwd()
    });

    setRuntimeSettingsState({
      settingsPath: result.configPath,
      bootstrapEnvPath: result.bootstrapEnvPath,
      runtimeConfig: result.runtimeConfig,
      runtimeSettingsSnapshot: result.runtimeSettingsSnapshot
    });

    return {
      ...result.config,
      configPath: result.configPath,
      bootstrapEnvPath: result.bootstrapEnvPath,
      restartRequired: false
    };
  }

  async function startRuntime(source = 'control-api') {
    setDesiredRuntimeActive(true);
    workerSlot.resetBootFailures();
    wechatWorkerSlot.resetBootFailures();

    const runtimeSettingsState = await refreshRuntimeSettingsState();
    const shouldEnableWechatWorker = shouldRunWechatWorker(runtimeSettingsState.runtimeConfig);

    setRuntimeStatus(
      setWechatConfigured(getRuntimeStatus(), shouldEnableWechatWorker)
    );

    await workerSlot.start(
      source,
      buildWorkerInitPayload('qq', runtimeSettingsState.runtimeSettingsSnapshot, source)
    );

    if (shouldEnableWechatWorker) {
      await wechatWorkerSlot.start(
        source,
        buildWorkerInitPayload('wechat', runtimeSettingsState.runtimeSettingsSnapshot, source)
      );
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
    const result = await writeControlConfig({
      cwd: process.cwd(),
      config: payload
    });
    const runtimeSettingsState = {
      settingsPath: result.configPath,
      bootstrapEnvPath: result.bootstrapEnvPath,
      runtimeConfig: result.runtimeConfig,
      runtimeSettingsSnapshot: result.runtimeSettingsSnapshot
    };
    const shouldEnableWechatWorker = shouldRunWechatWorker(result.runtimeConfig);

    setRuntimeSettingsState(runtimeSettingsState);
    lastConfigSavedAt = result.runtimeSettingsSnapshot.savedAt;
    setRuntimeStatus(
      setWechatConfigured(getRuntimeStatus(), shouldEnableWechatWorker)
    );

    if (getDesiredRuntimeActive()) {
      if (workerSlot.isRunning()) {
        workerSlot.send(
          buildWorkerConfigMessage('qq', result.runtimeSettingsSnapshot, 'config-update')
        );
      } else {
        await workerSlot.start(
          'config-update',
          buildWorkerInitPayload('qq', result.runtimeSettingsSnapshot, 'config-update')
        );
      }

      if (shouldEnableWechatWorker) {
        if (wechatWorkerSlot.isRunning()) {
          wechatWorkerSlot.send(
            buildWorkerConfigMessage('wechat', result.runtimeSettingsSnapshot, 'config-update')
          );
        } else {
          wechatWorkerSlot.resetBootFailures();
          await wechatWorkerSlot.start(
            'config-update',
            buildWorkerInitPayload('wechat', result.runtimeSettingsSnapshot, 'config-update')
          );
        }
      }

      if (!shouldEnableWechatWorker && wechatWorkerSlot.isRunning()) {
        await wechatWorkerSlot.stop('config-update');
      }
    }

    return {
      ...result.config,
      configPath: result.configPath,
      bootstrapEnvPath: result.bootstrapEnvPath,
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
