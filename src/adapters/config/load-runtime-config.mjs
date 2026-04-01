import path from 'node:path';

import dotenv from 'dotenv';

import {
  DEFAULT_BOT_SYSTEM_PROMPT,
  DEFAULT_ADVANCED_MODEL,
  DEFAULT_DEFAULT_MODEL
} from '../llm/openai-provider.mjs';
import { DEFAULT_ADVANCED_TRIGGER_PREFIXES } from '../../domain/route-decision.mjs';
import { createRuntimeConfig } from '../../domain/runtime-config.mjs';
import { buildRouteToolPolicy } from '../../domain/tool-registry.mjs';
import { parseBoolean, parseCsvList, parsePositiveInt } from '../../utils.mjs';

const DEFAULT_NAPCAT_WS_URL = 'ws://127.0.0.1:3001';
const DEFAULT_BOT_PREFIX = '/ai';
const DEFAULT_MAX_OUTPUT_CHARS = 1600;
const DEFAULT_RECONNECT_DELAY_MS = 3000;
const DEFAULT_WECHAT_BOT_PREFIX = '/ai';
const DEFAULT_DEEPSEEK_MODEL = 'deepseek-chat';
const DEFAULT_DEEPSEEK_BASE_URL = 'https://api.deepseek.com/v1';

export function loadRuntimeConfig({
  cwd = process.cwd(),
  env = process.env,
  loadDotenv = env === process.env,
  dotenvPath,
  toolPolicies = {}
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
  const defaultOpenAiReasoningEffort = env.OPENAI_DEFAULT_REASONING_EFFORT || '';
  const defaultOpenAiTextVerbosity = env.OPENAI_DEFAULT_TEXT_VERBOSITY || '';
  const defaultOpenAiEnableWebSearch = parseBoolean(env.OPENAI_DEFAULT_ENABLE_WEB_SEARCH, false);
  const defaultOpenAiEnableCodeInterpreter = parseBoolean(
    env.OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER,
    false
  );
  const advancedOpenAiApiKey = env.OPENAI_ADVANCED_API_KEY || sharedOpenAiApiKey;
  const advancedOpenAiModel = env.OPENAI_ADVANCED_MODEL || sharedOpenAiModel || DEFAULT_ADVANCED_MODEL;
  const advancedOpenAiBaseUrl = env.OPENAI_ADVANCED_BASE_URL || sharedOpenAiBaseUrl;
  const advancedOpenAiApiStyle = env.OPENAI_ADVANCED_API_STYLE || '';
  const advancedOpenAiReasoningEffort = env.OPENAI_ADVANCED_REASONING_EFFORT || '';
  const advancedOpenAiTextVerbosity = env.OPENAI_ADVANCED_TEXT_VERBOSITY || '';
  const advancedOpenAiEnableWebSearch = parseBoolean(env.OPENAI_ADVANCED_ENABLE_WEB_SEARCH, false);
  const advancedOpenAiEnableCodeInterpreter = parseBoolean(
    env.OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER,
    false
  );
  const deepseekFallbackEnabled = parseBoolean(env.DEEPSEEK_FALLBACK_ENABLED, false);
  const deepseekApiKey = env.DEEPSEEK_API_KEY ?? '';
  const deepseekModel = env.DEEPSEEK_MODEL || DEFAULT_DEEPSEEK_MODEL;
  const deepseekBaseUrl = env.DEEPSEEK_BASE_URL || DEFAULT_DEEPSEEK_BASE_URL;
  const botSystemPrompt =
    typeof env.BOT_SYSTEM_PROMPT === 'string' && env.BOT_SYSTEM_PROMPT.trim()
      ? env.BOT_SYSTEM_PROMPT
      : DEFAULT_BOT_SYSTEM_PROMPT;
  const advancedTriggerPrefixes = parseCsvList(
    env.OPENAI_ADVANCED_TRIGGER_PREFIXES || DEFAULT_ADVANCED_TRIGGER_PREFIXES.join(',')
  );
  const defaultRouteToolPolicy = buildRouteToolPolicy(
    toolPolicies?.default && typeof toolPolicies.default === 'object'
      ? toolPolicies.default
      : {
          enabledTools: [
            ...(defaultOpenAiEnableWebSearch ? ['web_search'] : []),
            ...(defaultOpenAiEnableCodeInterpreter ? ['code_interpreter'] : [])
          ]
        }
  );
  const advancedRouteToolPolicy = buildRouteToolPolicy(
    toolPolicies?.advanced && typeof toolPolicies.advanced === 'object'
      ? toolPolicies.advanced
      : {
          enabledTools: [
            ...(advancedOpenAiEnableWebSearch ? ['web_search'] : []),
            ...(advancedOpenAiEnableCodeInterpreter ? ['code_interpreter'] : [])
          ]
        }
  );

  return createRuntimeConfig({
    openai: {
      defaultRoute: {
        apiKey: defaultOpenAiApiKey,
        model: defaultOpenAiModel,
        baseURL: defaultOpenAiBaseUrl,
        apiStyle: defaultOpenAiApiStyle,
        reasoningEffort: defaultOpenAiReasoningEffort,
        textVerbosity: defaultOpenAiTextVerbosity,
        enableWebSearch: defaultOpenAiEnableWebSearch,
        enableCodeInterpreter: defaultOpenAiEnableCodeInterpreter,
        toolPolicy: defaultRouteToolPolicy
      },
      advancedRoute: {
        apiKey: advancedOpenAiApiKey,
        model: advancedOpenAiModel,
        baseURL: advancedOpenAiBaseUrl,
        apiStyle: advancedOpenAiApiStyle,
        reasoningEffort: advancedOpenAiReasoningEffort,
        textVerbosity: advancedOpenAiTextVerbosity,
        enableWebSearch: advancedOpenAiEnableWebSearch,
        enableCodeInterpreter: advancedOpenAiEnableCodeInterpreter,
        toolPolicy: advancedRouteToolPolicy
      },
      advancedTriggerPrefixes
    },
    deepseek: {
      fallbackEnabled: deepseekFallbackEnabled,
      apiKey: deepseekApiKey,
      model: deepseekModel,
      baseURL: deepseekBaseUrl
    },
    napcat: {
      wsUrl: env.NAPCAT_WS_URL || DEFAULT_NAPCAT_WS_URL,
      token: env.NAPCAT_TOKEN ?? ''
    },
    wechat: {
      bridgeUrl: env.WECHAT_BRIDGE_URL ?? '',
      token: env.WECHAT_BRIDGE_TOKEN ?? '',
      botPrefix: env.WECHAT_BOT_PREFIX || DEFAULT_WECHAT_BOT_PREFIX
    },
    bot: {
      prefix: env.BOT_PREFIX || DEFAULT_BOT_PREFIX,
      systemPrompt: botSystemPrompt,
      persona: env.BOT_PERSONA ?? '',
      maxOutputChars: parsePositiveInt(env.MAX_OUTPUT_CHARS, DEFAULT_MAX_OUTPUT_CHARS)
    },
    access: {
      allowedChatIds: parseCsvList(env.ALLOWED_CHAT_IDS),
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
