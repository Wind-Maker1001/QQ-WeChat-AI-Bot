import fs from 'node:fs/promises';
import path from 'node:path';

import dotenv from 'dotenv';

import { loadRuntimeConfig } from './load-runtime-config.mjs';
import { validateRuntimeConfig } from '../../domain/runtime-config.mjs';

const ENV_FILE_NAME = '.env';
const ENV_EXAMPLE_FILE_NAME = '.env.example';

const KNOWN_KEY_ORDER = [
  'OPENAI_API_KEY',
  'OPENAI_DEFAULT_API_KEY',
  'OPENAI_ADVANCED_API_KEY',
  'OPENAI_MODEL',
  'OPENAI_DEFAULT_MODEL',
  'OPENAI_ADVANCED_MODEL',
  'OPENAI_BASE_URL',
  'OPENAI_DEFAULT_BASE_URL',
  'OPENAI_ADVANCED_BASE_URL',
  'OPENAI_DEFAULT_API_STYLE',
  'OPENAI_ADVANCED_API_STYLE',
  'OPENAI_ADVANCED_TRIGGER_PREFIXES',
  'NAPCAT_WS_URL',
  'NAPCAT_TOKEN',
  'BOT_PREFIX',
  'BOT_PERSONA',
  'MAX_OUTPUT_CHARS',
  'ALLOWED_GROUP_IDS',
  'ALLOWED_USER_IDS'
];

function encodeEnvValue(value) {
  return (value ?? '')
    .replace(/\\/g, '\\\\')
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .replace(/\n/g, '\\n');
}

function decodeEnvValue(value) {
  return (value ?? '').replace(/\\n/g, '\n').replace(/\\\\/g, '\\');
}

function unquote(value) {
  if (value.length >= 2) {
    const isDoubleQuoted = value.startsWith('"') && value.endsWith('"');
    const isSingleQuoted = value.startsWith("'") && value.endsWith("'");

    if (isDoubleQuoted || isSingleQuoted) {
      return value.slice(1, -1);
    }
  }

  return value;
}

function buildControlConfigFromEnvValues(envValues, runtimeConfig) {
  return {
    openAiApiKey:
      envValues.OPENAI_ADVANCED_API_KEY ??
      envValues.OPENAI_API_KEY ??
      runtimeConfig.openai.advancedRoute.apiKey ??
      '',
    openAiDefaultApiKey:
      envValues.OPENAI_DEFAULT_API_KEY ?? runtimeConfig.openai.defaultRoute.apiKey ?? '',
    openAiDefaultModel:
      envValues.OPENAI_DEFAULT_MODEL ?? runtimeConfig.openai.defaultRoute.model ?? '',
    openAiModel:
      envValues.OPENAI_ADVANCED_MODEL ??
      envValues.OPENAI_MODEL ??
      runtimeConfig.openai.advancedRoute.model ??
      '',
    openAiBaseUrl:
      envValues.OPENAI_ADVANCED_BASE_URL ??
      envValues.OPENAI_BASE_URL ??
      runtimeConfig.openai.advancedRoute.baseURL ??
      '',
    openAiDefaultBaseUrl:
      envValues.OPENAI_DEFAULT_BASE_URL ?? runtimeConfig.openai.defaultRoute.baseURL ?? '',
    openAiAdvancedTriggerPrefixes:
      envValues.OPENAI_ADVANCED_TRIGGER_PREFIXES ??
      runtimeConfig.openai.advancedTriggerPrefixes.join(','),
    napCatWsUrl: envValues.NAPCAT_WS_URL ?? runtimeConfig.napcat.wsUrl ?? '',
    napCatToken: envValues.NAPCAT_TOKEN ?? runtimeConfig.napcat.token ?? '',
    botPrefix: envValues.BOT_PREFIX ?? runtimeConfig.bot.prefix ?? '',
    botPersona: decodeEnvValue(envValues.BOT_PERSONA ?? runtimeConfig.bot.persona ?? ''),
    maxOutputChars:
      envValues.MAX_OUTPUT_CHARS ?? String(runtimeConfig.bot.maxOutputChars ?? 800),
    allowedGroupIds: envValues.ALLOWED_GROUP_IDS ?? runtimeConfig.access.allowedGroupIds.join(','),
    allowedUserIds: envValues.ALLOWED_USER_IDS ?? runtimeConfig.access.allowedUserIds.join(',')
  };
}

function normalizeControlConfigInput(config, fallbackConfig) {
  const source = config && typeof config === 'object' ? config : {};
  const fallback = buildControlConfigFromEnvValues({}, fallbackConfig);

  return {
    openAiApiKey: String(source.openAiApiKey ?? fallback.openAiApiKey ?? '').trim(),
    openAiDefaultApiKey: String(
      source.openAiDefaultApiKey ?? fallback.openAiDefaultApiKey ?? ''
    ).trim(),
    openAiDefaultModel: String(
      source.openAiDefaultModel ?? fallback.openAiDefaultModel ?? ''
    ).trim(),
    openAiModel: String(source.openAiModel ?? fallback.openAiModel ?? '').trim(),
    openAiBaseUrl: String(source.openAiBaseUrl ?? fallback.openAiBaseUrl ?? '').trim(),
    openAiDefaultBaseUrl: String(
      source.openAiDefaultBaseUrl ?? fallback.openAiDefaultBaseUrl ?? ''
    ).trim(),
    openAiAdvancedTriggerPrefixes: String(
      source.openAiAdvancedTriggerPrefixes ?? fallback.openAiAdvancedTriggerPrefixes ?? ''
    ).trim(),
    napCatWsUrl: String(source.napCatWsUrl ?? fallback.napCatWsUrl ?? '').trim(),
    napCatToken: String(source.napCatToken ?? fallback.napCatToken ?? '').trim(),
    botPrefix: String(source.botPrefix ?? fallback.botPrefix ?? '').trim(),
    botPersona: String(source.botPersona ?? fallback.botPersona ?? ''),
    maxOutputChars: String(source.maxOutputChars ?? fallback.maxOutputChars ?? '800').trim(),
    allowedGroupIds: String(source.allowedGroupIds ?? fallback.allowedGroupIds ?? '').trim(),
    allowedUserIds: String(source.allowedUserIds ?? fallback.allowedUserIds ?? '').trim()
  };
}

async function ensureEnvFile(cwd) {
  const envPath = path.join(cwd, ENV_FILE_NAME);
  const examplePath = path.join(cwd, ENV_EXAMPLE_FILE_NAME);

  try {
    await fs.access(envPath);
  } catch {
    try {
      await fs.copyFile(examplePath, envPath);
    } catch {
      await fs.writeFile(envPath, '', 'utf8');
    }
  }

  return envPath;
}

async function readEnvValues(cwd) {
  const envPath = await ensureEnvFile(cwd);
  const raw = await fs.readFile(envPath, 'utf8');
  const parsed = dotenv.parse(raw);
  return {
    envPath,
    values: Object.fromEntries(
      Object.entries(parsed).map(([key, value]) => [key, decodeEnvValue(unquote(value.trim()))])
    )
  };
}

export async function readRuntimeConfigFromEnvFile({ cwd = process.cwd() }) {
  const { envPath, values } = await readEnvValues(cwd);

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

export async function readControlConfig({ cwd = process.cwd(), runtimeConfig }) {
  const { envPath, values } = await readEnvValues(cwd);

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
  const { envPath, values } = await readEnvValues(cwd);
  const normalizedConfig = normalizeControlConfigInput(config, runtimeConfig);
  const nextValues = {
    ...values,
    OPENAI_API_KEY: normalizedConfig.openAiApiKey,
    OPENAI_DEFAULT_API_KEY: normalizedConfig.openAiDefaultApiKey,
    OPENAI_ADVANCED_API_KEY: normalizedConfig.openAiApiKey,
    OPENAI_MODEL: normalizedConfig.openAiModel,
    OPENAI_DEFAULT_MODEL: normalizedConfig.openAiDefaultModel,
    OPENAI_ADVANCED_MODEL: normalizedConfig.openAiModel,
    OPENAI_BASE_URL: normalizedConfig.openAiBaseUrl,
    OPENAI_DEFAULT_BASE_URL: normalizedConfig.openAiDefaultBaseUrl,
    OPENAI_ADVANCED_BASE_URL: normalizedConfig.openAiBaseUrl,
    OPENAI_DEFAULT_API_STYLE: values.OPENAI_DEFAULT_API_STYLE ?? 'chat_completions',
    OPENAI_ADVANCED_API_STYLE: values.OPENAI_ADVANCED_API_STYLE ?? 'responses',
    OPENAI_ADVANCED_TRIGGER_PREFIXES: normalizedConfig.openAiAdvancedTriggerPrefixes,
    NAPCAT_WS_URL: normalizedConfig.napCatWsUrl,
    NAPCAT_TOKEN: normalizedConfig.napCatToken,
    BOT_PREFIX: normalizedConfig.botPrefix,
    BOT_PERSONA: normalizedConfig.botPersona,
    MAX_OUTPUT_CHARS: normalizedConfig.maxOutputChars,
    ALLOWED_GROUP_IDS: normalizedConfig.allowedGroupIds,
    ALLOWED_USER_IDS: normalizedConfig.allowedUserIds
  };

  const extraKeys = Object.keys(nextValues)
    .filter((key) => !KNOWN_KEY_ORDER.includes(key))
    .sort((left, right) => left.localeCompare(right));
  const orderedKeys = [...KNOWN_KEY_ORDER, ...extraKeys];
  const lines = orderedKeys.map((key) => {
    const rawValue = nextValues[key] ?? '';
    const encodedValue = key === 'BOT_PERSONA' ? encodeEnvValue(rawValue) : rawValue;
    return `${key}=${encodedValue}`;
  });

  const nextRuntimeConfig = loadRuntimeConfig({
    cwd,
    env: nextValues,
    loadDotenv: false
  });
  validateRuntimeConfig(nextRuntimeConfig);

  await fs.writeFile(envPath, `${lines.join('\n')}\n`, 'utf8');

  return {
    envPath,
    config: normalizedConfig,
    envValues: nextValues
  };
}
