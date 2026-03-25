import test from 'node:test';
import assert from 'node:assert/strict';

import { loadRuntimeConfig } from '../src/adapters/config/load-runtime-config.mjs';
import {
  buildControlConfigFromEnvValues,
  buildControlEnvValues
} from '../src/adapters/config/control-config-mapper.mjs';

function createRuntimeConfig() {
  return loadRuntimeConfig({
    cwd: process.cwd(),
    loadDotenv: false,
    env: {
      OPENAI_API_KEY: 'shared-key',
      OPENAI_DEFAULT_API_KEY: 'default-key',
      OPENAI_MODEL: 'advanced-model',
      OPENAI_DEFAULT_MODEL: 'default-model',
      OPENAI_BASE_URL: 'https://gateway.example/v1',
      OPENAI_DEFAULT_BASE_URL: 'https://default.example/v1',
      OPENAI_DEFAULT_REASONING_EFFORT: 'medium',
      OPENAI_ADVANCED_REASONING_EFFORT: 'high',
      OPENAI_DEFAULT_TEXT_VERBOSITY: 'low',
      OPENAI_ADVANCED_TEXT_VERBOSITY: 'high',
      OPENAI_DEFAULT_ENABLE_WEB_SEARCH: 'false',
      OPENAI_ADVANCED_ENABLE_WEB_SEARCH: 'true',
      OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER: 'false',
      OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER: 'true',
      OPENAI_ADVANCED_TRIGGER_PREFIXES: '/gpt,/vision',
      NAPCAT_WS_URL: 'ws://127.0.0.1:3001',
      NAPCAT_TOKEN: 'napcat-token',
      WECHAT_BRIDGE_URL: 'ws://127.0.0.1:3198',
      WECHAT_BRIDGE_TOKEN: 'wechat-token',
      WECHAT_BOT_PREFIX: '/wx',
      BOT_PREFIX: '/ai',
      BOT_PERSONA: 'persona',
      MAX_OUTPUT_CHARS: '1200',
      ALLOWED_CHAT_IDS: 'chat-a',
      ALLOWED_USER_IDS: 'user-a'
    }
  });
}

test('buildControlConfigFromEnvValues falls back to runtime config defaults', () => {
  const runtimeConfig = createRuntimeConfig();

  const config = buildControlConfigFromEnvValues(
    {
      OPENAI_API_KEY: 'shared-key',
      NAPCAT_TOKEN: 'napcat-token'
    },
    runtimeConfig
  );

  assert.equal(config.openAiDefaultApiKey, 'default-key');
  assert.equal(config.wechatBotPrefix, '/wx');
  assert.equal(config.allowedChatIds, 'chat-a');
  assert.equal(config.maxOutputChars, '1200');
});

test('buildControlEnvValues preserves explicit API styles and unknown keys', () => {
  const runtimeConfig = createRuntimeConfig();

  const { normalizedConfig, nextValues } = buildControlEnvValues({
    existingValues: {
      OPENAI_DEFAULT_API_STYLE: 'chat_completions',
      OPENAI_ADVANCED_API_STYLE: 'responses',
      CUSTOM_FLAG: 'enabled'
    },
    runtimeConfig,
    config: {
      openAiApiKey: ' advanced-key ',
      openAiDefaultApiKey: ' default-key ',
      openAiModel: ' advanced-model-next ',
      openAiDefaultModel: ' default-model-next ',
      openAiBaseUrl: ' https://advanced.example/v1 ',
      openAiDefaultBaseUrl: ' https://default-next.example/v1 ',
      wechatBotPrefix: ' /bot ',
      napCatToken: ' next-token ',
      allowedChatIds: ' chat-x,chat-y ',
      allowedUserIds: ' user-x '
    }
  });

  assert.equal(normalizedConfig.openAiApiKey, 'advanced-key');
  assert.equal(normalizedConfig.wechatBotPrefix, '/bot');
  assert.equal(nextValues.OPENAI_DEFAULT_API_STYLE, 'chat_completions');
  assert.equal(nextValues.OPENAI_ADVANCED_API_STYLE, 'responses');
  assert.equal(nextValues.CUSTOM_FLAG, 'enabled');
  assert.equal(nextValues.WECHAT_BOT_PREFIX, '/bot');
  assert.equal(nextValues.ALLOWED_CHAT_IDS, 'chat-x,chat-y');
});
