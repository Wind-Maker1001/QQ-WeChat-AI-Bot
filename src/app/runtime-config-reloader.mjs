import fs from 'node:fs';
import path from 'node:path';

import { buildRuntimeConfigFromSettingsSnapshot } from '../adapters/config/runtime-settings-store.mjs';
import { readControlConfig } from '../adapters/config/control-config-file.mjs';
import { formatError } from '../utils.mjs';

export function createRuntimeConfigReloader({
  getRuntimeConfig,
  applyRuntimeConfig,
  logInfo,
  logError,
  logPrefix
}) {
  let configWatcher = null;
  let configReloadTimer = null;
  let lastConfigMtimeMs = 0;

  async function reloadRuntimeConfigFromDisk(source) {
    const result = await readControlConfig({
      cwd: process.cwd()
    });
    const nextRuntimeConfig = buildRuntimeConfigFromSettingsSnapshot({
      cwd: process.cwd(),
      snapshot: result.runtimeSettingsSnapshot
    });

    return applyRuntimeConfig(nextRuntimeConfig, source);
  }

  function scheduleConfigReload(source) {
    if (configReloadTimer) {
      clearTimeout(configReloadTimer);
    }

    configReloadTimer = setTimeout(() => {
      configReloadTimer = null;
      void reloadRuntimeConfigFromDisk(source).catch((error) => {
        logError(`[${logPrefix}] Config reload from ${source} failed: ${formatError(error)}`);
      });
    }, 250);
  }

  async function refreshConfigMtime(configFilePath) {
    try {
      const stats = await fs.promises.stat(configFilePath);
      return stats.mtimeMs;
    } catch {
      return 0;
    }
  }

  function watchRuntimeConfigFile(configFilePath) {
    const configDirPath = path.dirname(configFilePath);
    const configFileName = path.basename(configFilePath);

    return fs.watch(configDirPath, (_eventType, filename) => {
      const normalizedFilename =
        typeof filename === 'string'
          ? filename
          : Buffer.isBuffer(filename)
            ? filename.toString('utf8')
            : '';

      const isConfigRelatedEvent =
        !normalizedFilename ||
        normalizedFilename === configFileName ||
        normalizedFilename === `${configFileName}.tmp`;

      if (!isConfigRelatedEvent) {
        return;
      }

      void refreshConfigMtime(configFilePath).then((mtimeMs) => {
        if (!mtimeMs || mtimeMs === lastConfigMtimeMs) {
          return;
        }

        lastConfigMtimeMs = mtimeMs;
        scheduleConfigReload('config-watch');
      });
    });
  }

  async function startWatching() {
    const initialControlConfig = await readControlConfig({
      cwd: process.cwd()
    });
    lastConfigMtimeMs = await refreshConfigMtime(initialControlConfig.configPath);
    configWatcher = watchRuntimeConfigFile(initialControlConfig.configPath);
    logInfo(`[${logPrefix}] Watching runtime config file: ${initialControlConfig.configPath}`);
    return initialControlConfig.configPath;
  }

  function stopWatching() {
    if (configReloadTimer) {
      clearTimeout(configReloadTimer);
      configReloadTimer = null;
    }

    configWatcher?.close();
    configWatcher = null;
  }

  return Object.freeze({
    reloadRuntimeConfigFromDisk,
    startWatching,
    stopWatching
  });
}
