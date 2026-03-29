import fs from 'node:fs/promises';
import path from 'node:path';

import { buildControlConfigFromEnvValues, normalizeControlConfigInput } from './control-config-mapper.mjs';
import { readEnvFileValues } from './env-file-store.mjs';
import { loadRuntimeConfig } from './load-runtime-config.mjs';
import { validateRuntimeConfig } from '../../domain/runtime-config.mjs';

export const RUNTIME_SETTINGS_FILE_NAME = 'runtime-settings.json';
export const RUNTIME_SETTINGS_VERSION = 1;

const SETTINGS_LOCK_TIMEOUT_MS = 5000;
const STALE_SETTINGS_LOCK_AGE_MS = 15000;
const EMPTY_CONTROL_CONFIG = Object.freeze({
  openAiApiKey: '',
  openAiDefaultApiKey: '',
  openAiDefaultModel: '',
  openAiModel: '',
  openAiBaseUrl: '',
  openAiDefaultBaseUrl: '',
  openAiDefaultReasoningEffort: '',
  openAiAdvancedReasoningEffort: '',
  openAiDefaultTextVerbosity: '',
  openAiAdvancedTextVerbosity: '',
  openAiDefaultEnableWebSearch: 'false',
  openAiAdvancedEnableWebSearch: 'false',
  openAiDefaultEnableCodeInterpreter: 'false',
  openAiAdvancedEnableCodeInterpreter: 'false',
  openAiAdvancedTriggerPrefixes: '',
  deepSeekFallbackEnabled: 'false',
  deepSeekApiKey: '',
  deepSeekModel: 'deepseek-chat',
  deepSeekBaseUrl: 'https://api.deepseek.com/v1',
  napCatWsUrl: '',
  napCatToken: '',
  wechatBridgeUrl: '',
  wechatBridgeToken: '',
  wechatBotPrefix: '',
  botPrefix: '',
  botSystemPrompt: '',
  botPersona: '',
  maxOutputChars: '800',
  allowedChatIds: '',
  allowedUserIds: ''
});

function buildRuntimeSettingsPath(cwd) {
  return path.resolve(cwd, 'data', RUNTIME_SETTINGS_FILE_NAME);
}

function buildSettingsLockPayload() {
  return JSON.stringify(
    {
      pid: process.pid,
      acquiredAt: new Date().toISOString()
    },
    null,
    2
  );
}

function safeJsonParse(raw, fallback) {
  try {
    return JSON.parse(raw);
  } catch {
    return fallback;
  }
}

function parseSettingsLockPayload(raw) {
  const parsed = safeJsonParse(raw, null);

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      pid: null
    };
  }

  return {
    pid: Number.isInteger(parsed.pid) && parsed.pid > 0 ? parsed.pid : null
  };
}

function isProcessAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function writeTextFileAtomically(filePath, content) {
  const tempPath = `${filePath}.tmp`;
  await fs.writeFile(tempPath, content, 'utf8');
  await fs.rename(tempPath, filePath);
}

async function withSettingsFileLock(settingsPath, task, timeoutMs = SETTINGS_LOCK_TIMEOUT_MS) {
  const lockPath = `${settingsPath}.lock`;
  const startedAt = Date.now();

  while (true) {
    try {
      const lockHandle = await fs.open(lockPath, 'wx');

      try {
        await lockHandle.writeFile(`${buildSettingsLockPayload()}\n`, 'utf8');
        return await task();
      } finally {
        await lockHandle.close();
        await fs.rm(lockPath, { force: true });
      }
    } catch (error) {
      if (error?.code !== 'EEXIST') {
        throw error;
      }

      try {
        const rawLockFile = await fs.readFile(lockPath, 'utf8');
        const stats = await fs.stat(lockPath);
        const lockAgeMs = Date.now() - stats.mtimeMs;
        const lockState = parseSettingsLockPayload(rawLockFile);

        if (lockAgeMs >= STALE_SETTINGS_LOCK_AGE_MS && !isProcessAlive(lockState.pid)) {
          await fs.rm(lockPath, { force: true });
          continue;
        }
      } catch (readError) {
        if (readError?.code === 'ENOENT') {
          continue;
        }

        throw readError;
      }

      if (Date.now() - startedAt >= timeoutMs) {
        throw new Error(`Timed out waiting for runtime settings lock: ${lockPath}`);
      }

      await new Promise((resolve) => setTimeout(resolve, 25));
    }
  }
}

function normalizeHiddenSettings(hiddenSettings, bootstrapEnvValues = {}) {
  const source = hiddenSettings && typeof hiddenSettings === 'object' ? hiddenSettings : {};

  return {
    openAiDefaultApiStyle:
      typeof source.openAiDefaultApiStyle === 'string' && source.openAiDefaultApiStyle.trim()
        ? source.openAiDefaultApiStyle.trim()
        : typeof bootstrapEnvValues.OPENAI_DEFAULT_API_STYLE === 'string' &&
            bootstrapEnvValues.OPENAI_DEFAULT_API_STYLE.trim()
          ? bootstrapEnvValues.OPENAI_DEFAULT_API_STYLE.trim()
          : 'responses',
    openAiAdvancedApiStyle:
      typeof source.openAiAdvancedApiStyle === 'string' && source.openAiAdvancedApiStyle.trim()
        ? source.openAiAdvancedApiStyle.trim()
        : typeof bootstrapEnvValues.OPENAI_ADVANCED_API_STYLE === 'string' &&
            bootstrapEnvValues.OPENAI_ADVANCED_API_STYLE.trim()
          ? bootstrapEnvValues.OPENAI_ADVANCED_API_STYLE.trim()
          : 'responses'
  };
}

export function buildRuntimeEnvValuesFromSettingsSnapshot(snapshot) {
  const settings = normalizeControlConfigInput(snapshot?.settings, EMPTY_CONTROL_CONFIG);
  const hiddenSettings = normalizeHiddenSettings(snapshot?.hiddenSettings);

  return {
    OPENAI_API_KEY: settings.openAiApiKey,
    OPENAI_DEFAULT_API_KEY: settings.openAiDefaultApiKey,
    OPENAI_ADVANCED_API_KEY: settings.openAiApiKey,
    OPENAI_MODEL: settings.openAiModel,
    OPENAI_DEFAULT_MODEL: settings.openAiDefaultModel,
    OPENAI_ADVANCED_MODEL: settings.openAiModel,
    OPENAI_BASE_URL: settings.openAiBaseUrl,
    OPENAI_DEFAULT_BASE_URL: settings.openAiDefaultBaseUrl,
    OPENAI_ADVANCED_BASE_URL: settings.openAiBaseUrl,
    OPENAI_DEFAULT_API_STYLE: hiddenSettings.openAiDefaultApiStyle,
    OPENAI_ADVANCED_API_STYLE: hiddenSettings.openAiAdvancedApiStyle,
    OPENAI_DEFAULT_REASONING_EFFORT: settings.openAiDefaultReasoningEffort,
    OPENAI_ADVANCED_REASONING_EFFORT: settings.openAiAdvancedReasoningEffort,
    OPENAI_DEFAULT_TEXT_VERBOSITY: settings.openAiDefaultTextVerbosity,
    OPENAI_ADVANCED_TEXT_VERBOSITY: settings.openAiAdvancedTextVerbosity,
    OPENAI_DEFAULT_ENABLE_WEB_SEARCH: settings.openAiDefaultEnableWebSearch,
    OPENAI_ADVANCED_ENABLE_WEB_SEARCH: settings.openAiAdvancedEnableWebSearch,
    OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER: settings.openAiDefaultEnableCodeInterpreter,
    OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER: settings.openAiAdvancedEnableCodeInterpreter,
    OPENAI_ADVANCED_TRIGGER_PREFIXES: settings.openAiAdvancedTriggerPrefixes,
    DEEPSEEK_FALLBACK_ENABLED: settings.deepSeekFallbackEnabled,
    DEEPSEEK_API_KEY: settings.deepSeekApiKey,
    DEEPSEEK_MODEL: settings.deepSeekModel,
    DEEPSEEK_BASE_URL: settings.deepSeekBaseUrl,
    NAPCAT_WS_URL: settings.napCatWsUrl,
    NAPCAT_TOKEN: settings.napCatToken,
    WECHAT_BRIDGE_URL: settings.wechatBridgeUrl,
    WECHAT_BRIDGE_TOKEN: settings.wechatBridgeToken,
    WECHAT_BOT_PREFIX: settings.wechatBotPrefix,
    BOT_PREFIX: settings.botPrefix,
    BOT_SYSTEM_PROMPT: settings.botSystemPrompt,
    BOT_PERSONA: settings.botPersona,
    MAX_OUTPUT_CHARS: settings.maxOutputChars,
    ALLOWED_CHAT_IDS: settings.allowedChatIds,
    ALLOWED_USER_IDS: settings.allowedUserIds
  };
}

export function buildRuntimeConfigFromSettingsSnapshot({ cwd = process.cwd(), snapshot }) {
  return loadRuntimeConfig({
    cwd,
    env: buildRuntimeEnvValuesFromSettingsSnapshot(snapshot),
    loadDotenv: false
  });
}

function normalizeRuntimeSettingsSnapshot(snapshot, bootstrapEnvValues, runtimeConfig) {
  const fallbackConfig = buildControlConfigFromEnvValues(
    bootstrapEnvValues,
    runtimeConfig
  );

  return {
    version: RUNTIME_SETTINGS_VERSION,
    savedAt:
      typeof snapshot?.savedAt === 'string' && snapshot.savedAt.trim()
        ? snapshot.savedAt.trim()
        : new Date().toISOString(),
    settings: normalizeControlConfigInput(snapshot?.settings, fallbackConfig),
    hiddenSettings: normalizeHiddenSettings(snapshot?.hiddenSettings, bootstrapEnvValues)
  };
}

async function readRuntimeSettingsFile(settingsPath) {
  const raw = await fs.readFile(settingsPath, 'utf8');
  const parsed = safeJsonParse(raw, null);

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`Runtime settings file is not a JSON object: ${settingsPath}`);
  }

  return parsed;
}

export async function readBootstrapConfig({ cwd = process.cwd() } = {}) {
  const { envPath, values } = await readEnvFileValues({ cwd });
  const runtimeConfig = loadRuntimeConfig({
    cwd,
    env: values,
    loadDotenv: false
  });

  return {
    envPath,
    envValues: values,
    runtimeConfig
  };
}

export async function ensureRuntimeSettings({ cwd = process.cwd() } = {}) {
  const settingsPath = buildRuntimeSettingsPath(cwd);
  await fs.mkdir(path.dirname(settingsPath), { recursive: true });
  const bootstrapConfig = await readBootstrapConfig({ cwd });

  try {
    const parsedSettings = await readRuntimeSettingsFile(settingsPath);
    const runtimeSettingsSnapshot = normalizeRuntimeSettingsSnapshot(
      parsedSettings,
      bootstrapConfig.envValues,
      bootstrapConfig.runtimeConfig
    );
    const runtimeConfig = buildRuntimeConfigFromSettingsSnapshot({
      cwd,
      snapshot: runtimeSettingsSnapshot
    });

    return {
      settingsPath,
      bootstrapEnvPath: bootstrapConfig.envPath,
      bootstrapEnvValues: bootstrapConfig.envValues,
      runtimeSettingsSnapshot,
      runtimeConfig
    };
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      throw error;
    }
  }

  const runtimeSettingsSnapshot = normalizeRuntimeSettingsSnapshot(
    {},
    bootstrapConfig.envValues,
    bootstrapConfig.runtimeConfig
  );
  runtimeSettingsSnapshot.savedAt = new Date().toISOString();
  await writeTextFileAtomically(settingsPath, `${JSON.stringify(runtimeSettingsSnapshot, null, 2)}\n`);

  return {
    settingsPath,
    bootstrapEnvPath: bootstrapConfig.envPath,
    bootstrapEnvValues: bootstrapConfig.envValues,
    runtimeSettingsSnapshot,
    runtimeConfig: buildRuntimeConfigFromSettingsSnapshot({
      cwd,
      snapshot: runtimeSettingsSnapshot
    })
  };
}

export async function writeRuntimeSettings({
  cwd = process.cwd(),
  config,
  existingSnapshot = null
} = {}) {
  const currentState = await ensureRuntimeSettings({ cwd });
  const baseSnapshot = existingSnapshot ?? currentState.runtimeSettingsSnapshot;
  const fallbackConfig = buildControlConfigFromEnvValues(
    buildRuntimeEnvValuesFromSettingsSnapshot(baseSnapshot),
    currentState.runtimeConfig
  );
  const nextSnapshot = {
    version: RUNTIME_SETTINGS_VERSION,
    savedAt: new Date().toISOString(),
    settings: normalizeControlConfigInput(config, fallbackConfig),
    hiddenSettings: normalizeHiddenSettings(baseSnapshot.hiddenSettings, currentState.bootstrapEnvValues)
  };
  const nextRuntimeConfig = buildRuntimeConfigFromSettingsSnapshot({
    cwd,
    snapshot: nextSnapshot
  });
  validateRuntimeConfig(nextRuntimeConfig, {
    validateWechatBridge: true
  });

  await withSettingsFileLock(currentState.settingsPath, async () => {
    await writeTextFileAtomically(
      currentState.settingsPath,
      `${JSON.stringify(nextSnapshot, null, 2)}\n`
    );
  });

  return {
    settingsPath: currentState.settingsPath,
    bootstrapEnvPath: currentState.bootstrapEnvPath,
    bootstrapEnvValues: currentState.bootstrapEnvValues,
    runtimeSettingsSnapshot: nextSnapshot,
    runtimeConfig: nextRuntimeConfig
  };
}
