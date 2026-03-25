import { loadRuntimeConfig } from './load-runtime-config.mjs';
import {
  APP_ENV_KEYS,
  readEnvFileValues,
  updateEnvFileValues
} from './env-file-store.mjs';
import {
  buildControlConfigFromEnvValues,
  buildControlEnvValues
} from './control-config-mapper.mjs';
import { validateRuntimeConfig } from '../../domain/runtime-config.mjs';

export { APP_ENV_KEYS } from './env-file-store.mjs';

export async function readRuntimeConfigFromEnvFile({ cwd = process.cwd() } = {}) {
  const { envPath, values } = await readEnvFileValues({ cwd });

  return {
    envPath,
    envValues: values,
    runtimeConfig: loadRuntimeConfig({
      cwd,
      env: values,
      loadDotenv: false
    })
  };
}

export function buildRuntimeProcessEnv(envValues, baseEnv = process.env) {
  const nextEnv = { ...baseEnv };

  for (const key of APP_ENV_KEYS) {
    delete nextEnv[key];
  }

  return {
    ...nextEnv,
    ...envValues
  };
}

export async function readControlConfig({ cwd = process.cwd(), runtimeConfig }) {
  const { envPath, values } = await readEnvFileValues({ cwd });

  return {
    envPath,
    config: buildControlConfigFromEnvValues(values, runtimeConfig),
    envValues: values
  };
}

export async function writeControlConfig({
  cwd = process.cwd(),
  runtimeConfig,
  config
}) {
  let normalizedConfig = null;

  const result = await updateEnvFileValues({
    cwd,
    mapValues: (existingValues) => {
      const mappedValues = buildControlEnvValues({
        existingValues,
        runtimeConfig,
        config
      });

      normalizedConfig = mappedValues.normalizedConfig;

      const nextRuntimeConfig = loadRuntimeConfig({
        cwd,
        env: mappedValues.nextValues,
        loadDotenv: false
      });
      validateRuntimeConfig(nextRuntimeConfig, {
        validateWechatBridge: true
      });

      return mappedValues.nextValues;
    }
  });

  return {
    envPath: result.envPath,
    config: normalizedConfig,
    envValues: result.nextValues
  };
}
