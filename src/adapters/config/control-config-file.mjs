import { APP_ENV_KEYS } from './env-file-store.mjs';
import {
  buildControlConfigFromEnvValues
} from './control-config-mapper.mjs';
import {
  buildRuntimeEnvValuesFromSettingsSnapshot,
  ensureRuntimeSettings,
  readBootstrapConfig,
  writeRuntimeSettings
} from './runtime-settings-store.mjs';

export { APP_ENV_KEYS } from './env-file-store.mjs';

export async function readRuntimeConfigFromEnvFile({ cwd = process.cwd() } = {}) {
  return readBootstrapConfig({ cwd });
}

export async function readRuntimeSettingsFromDisk({ cwd = process.cwd() } = {}) {
  return ensureRuntimeSettings({ cwd });
}

export function buildRuntimeProcessEnv(envValues = {}, baseEnv = process.env) {
  const nextEnv = { ...baseEnv };

  for (const key of APP_ENV_KEYS) {
    delete nextEnv[key];
  }

  return {
    ...nextEnv,
    ...envValues
  };
}

export async function readControlConfig({ cwd = process.cwd() } = {}) {
  const result = await ensureRuntimeSettings({ cwd });

  return {
    envPath: result.settingsPath,
    bootstrapEnvPath: result.bootstrapEnvPath,
    config: buildControlConfigFromEnvValues(
      buildRuntimeEnvValuesFromSettingsSnapshot(result.runtimeSettingsSnapshot),
      result.runtimeConfig
    ),
    runtimeConfig: result.runtimeConfig,
    runtimeSettingsSnapshot: result.runtimeSettingsSnapshot
  };
}

export async function writeControlConfig({
  cwd = process.cwd(),
  config
}) {
  const result = await writeRuntimeSettings({
    cwd,
    config
  });

  return {
    envPath: result.settingsPath,
    bootstrapEnvPath: result.bootstrapEnvPath,
    config: buildControlConfigFromEnvValues(
      buildRuntimeEnvValuesFromSettingsSnapshot(result.runtimeSettingsSnapshot),
      result.runtimeConfig
    ),
    runtimeConfig: result.runtimeConfig,
    runtimeSettingsSnapshot: result.runtimeSettingsSnapshot
  };
}
