import fs from 'node:fs';
import path from 'node:path';

import { loadRuntimeConfig } from '../adapters/config/load-runtime-config.mjs';
import { readControlConfig } from '../adapters/config/control-config-file.mjs';
import { formatError } from '../utils.mjs';

export function createRuntimeConfigReloader({
  getRuntimeConfig,
  applyRuntimeConfig,
  logInfo,
  logError,
  logPrefix
}) {
  let envWatcher = null;
  let envReloadTimer = null;
  let lastEnvMtimeMs = 0;

  async function reloadRuntimeConfigFromDisk(source) {
    const runtimeConfig = getRuntimeConfig();
    const result = await readControlConfig({
      cwd: process.cwd(),
      runtimeConfig
    });
    const nextRuntimeConfig = loadRuntimeConfig({
      cwd: process.cwd(),
      env: result.envValues,
      loadDotenv: false
    });

    return applyRuntimeConfig(nextRuntimeConfig, source);
  }

  function scheduleEnvReload(source) {
    if (envReloadTimer) {
      clearTimeout(envReloadTimer);
    }

    envReloadTimer = setTimeout(() => {
      envReloadTimer = null;
      void reloadRuntimeConfigFromDisk(source).catch((error) => {
        logError(`[${logPrefix}] Config reload from ${source} failed: ${formatError(error)}`);
      });
    }, 250);
  }

  async function refreshEnvMtime(envFilePath) {
    try {
      const stats = await fs.promises.stat(envFilePath);
      return stats.mtimeMs;
    } catch {
      return 0;
    }
  }

  function watchRuntimeConfigFile(envFilePath) {
    const envDirPath = path.dirname(envFilePath);
    const envFileName = path.basename(envFilePath);

    return fs.watch(envDirPath, (_eventType, filename) => {
      const normalizedFilename =
        typeof filename === 'string'
          ? filename
          : Buffer.isBuffer(filename)
            ? filename.toString('utf8')
            : '';

      const isEnvRelatedEvent =
        !normalizedFilename ||
        normalizedFilename === envFileName ||
        normalizedFilename === `${envFileName}.tmp`;

      if (!isEnvRelatedEvent) {
        return;
      }

      void refreshEnvMtime(envFilePath).then((mtimeMs) => {
        if (!mtimeMs || mtimeMs === lastEnvMtimeMs) {
          return;
        }

        lastEnvMtimeMs = mtimeMs;
        scheduleEnvReload('env-watch');
      });
    });
  }

  async function startWatching() {
    const runtimeConfig = getRuntimeConfig();
    const initialControlConfig = await readControlConfig({
      cwd: process.cwd(),
      runtimeConfig
    });
    lastEnvMtimeMs = await refreshEnvMtime(initialControlConfig.envPath);
    envWatcher = watchRuntimeConfigFile(initialControlConfig.envPath);
    logInfo(`[${logPrefix}] Watching config file: ${initialControlConfig.envPath}`);
    return initialControlConfig.envPath;
  }

  function stopWatching() {
    if (envReloadTimer) {
      clearTimeout(envReloadTimer);
      envReloadTimer = null;
    }

    envWatcher?.close();
    envWatcher = null;
  }

  return Object.freeze({
    reloadRuntimeConfigFromDisk,
    startWatching,
    stopWatching
  });
}
