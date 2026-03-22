import path from 'node:path';

import dotenv from 'dotenv';

import {
  DEFAULT_ADVANCED_MODEL,
  DEFAULT_DEFAULT_MODEL
} from '../llm/openai-provider.mjs';
import { DEFAULT_ADVANCED_TRIGGER_PREFIXES } from '../../domain/route-decision.mjs';
import { createRuntimeConfig } from '../../domain/runtime-config.mjs';
import { parseCsvList, parsePositiveInt } from '../../utils.mjs';

const DEFAULT_NAPCAT_WS_URL = 'ws://127.0.0.1:3001';
const DEFAULT_BOT_PREFIX = '/ai';
const DEFAULT_MAX_OUTPUT_CHARS = 800;
const DEFAULT_RECONNECT_DELAY_MS = 3000;

export function loadRuntimeConfig({
  cwd = process.cwd(),
  env = process.env,
  loadDotenv = env === process.env,
  dotenvPath
} = {}) {
  if (loadDotenv) {
    dotenv.config(dotenvPath ? { path: dotenvPath } : undefined);
  }

  const sharedOpenAiApiKey = env.OPENAI_API_KEY ?? '';
  const sharedOpenAiModel = env.OPENAI_MODEL || DEFAULT_ADVANCED_MODEL;
  const sharedOpenAiBaseUrl = env.OPENAI_BASE_URL ?? '';
  const defaultOpenAiApiKey = env.OPENAI_DEFAULT_API_KEY || sharedOpenAiApiKey;
  const defaultOpenAiModel = env.OPENAI_DEFAULT_MODEL || DEFAULT_DEFAULT_MODEL;
  const defaultOpenAiBaseUrl = env.OPENAI_DEFAULT_BASE_URL || sharedOpenAiBaseUrl;
  const defaultOpenAiApiStyle = env.OPENAI_DEFAULT_API_STYLE || '';
  const advancedOpenAiApiKey = env.OPENAI_ADVANCED_API_KEY || sharedOpenAiApiKey;
  const advancedOpenAiModel = env.OPENAI_ADVANCED_MODEL || sharedOpenAiModel || DEFAULT_ADVANCED_MODEL;
  const advancedOpenAiBaseUrl = env.OPENAI_ADVANCED_BASE_URL || sharedOpenAiBaseUrl;
  const advancedOpenAiApiStyle = env.OPENAI_ADVANCED_API_STYLE || '';
  const advancedTriggerPrefixes = parseCsvList(
    env.OPENAI_ADVANCED_TRIGGER_PREFIXES || DEFAULT_ADVANCED_TRIGGER_PREFIXES.join(',')
  );

  return createRuntimeConfig({
    openai: {
      defaultRoute: {
        apiKey: defaultOpenAiApiKey,
        model: defaultOpenAiModel,
        baseURL: defaultOpenAiBaseUrl,
        apiStyle: defaultOpenAiApiStyle
      },
      advancedRoute: {
        apiKey: advancedOpenAiApiKey,
        model: advancedOpenAiModel,
        baseURL: advancedOpenAiBaseUrl,
        apiStyle: advancedOpenAiApiStyle
      },
      advancedTriggerPrefixes
    },
    napcat: {
      wsUrl: env.NAPCAT_WS_URL || DEFAULT_NAPCAT_WS_URL,
      token: env.NAPCAT_TOKEN ?? ''
    },
    bot: {
      prefix: env.BOT_PREFIX || DEFAULT_BOT_PREFIX,
      persona: env.BOT_PERSONA ?? '',
      maxOutputChars: parsePositiveInt(env.MAX_OUTPUT_CHARS, DEFAULT_MAX_OUTPUT_CHARS)
    },
    access: {
      allowedGroupIds: parseCsvList(env.ALLOWED_GROUP_IDS),
      allowedUserIds: parseCsvList(env.ALLOWED_USER_IDS)
    },
    runtime: {
      reconnectDelayMs: DEFAULT_RECONNECT_DELAY_MS
    },
    paths: {
      imageCacheDir: path.resolve(cwd, 'data', 'image-cache')
    }
  });
}
