import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { readControlConfig, writeControlConfig } from '../src/adapters/config/control-config-file.mjs';
import { loadRuntimeConfig } from '../src/adapters/config/load-runtime-config.mjs';

async function createTempEnv(lines) {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-contract-'));
  await fs.writeFile(path.join(cwd, '.env'), `${lines.join('\n')}\n`, 'utf8');
  return cwd;
}

test('control config exposes allowedChatIds and hides allowedGroupIds', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'ALLOWED_CHAT_IDS=chat-a,chat-b',
    'OPENAI_ADVANCED_REASONING_EFFORT=high',
    'OPENAI_ADVANCED_TEXT_VERBOSITY=high'
  ]);
  const runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });

  const { config } = await readControlConfig({ cwd, runtimeConfig });

  assert.equal(config.allowedChatIds, 'chat-a,chat-b');
  assert.equal(config.openAiAdvancedReasoningEffort, 'high');
  assert.equal(config.openAiAdvancedTextVerbosity, 'high');
  assert.equal('allowedGroupIds' in config, false);
});

test('legacy ALLOWED_GROUP_IDS env is migrated to ALLOWED_CHAT_IDS on read', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'ALLOWED_GROUP_IDS=legacy-a,legacy-b'
  ]);
  const runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });

  const { config } = await readControlConfig({
    cwd,
    runtimeConfig
  });

  assert.equal(config.allowedChatIds, 'legacy-a,legacy-b');
  assert.equal('allowedGroupIds' in config, false);

  const envText = await fs.readFile(path.join(cwd, '.env'), 'utf8');
  assert.match(envText, /ALLOWED_CHAT_IDS=legacy-a,legacy-b/);
  assert.doesNotMatch(envText, /ALLOWED_GROUP_IDS=/);
});

test('default OpenAI route inherits shared key and base URL when default-specific values are blank', async () => {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-default-openai-'));
  const runtimeConfig = loadRuntimeConfig({
    cwd,
    loadDotenv: false,
    env: {
      OPENAI_API_KEY: 'shared-key',
      OPENAI_BASE_URL: 'https://gateway.example/v1',
      OPENAI_DEFAULT_API_KEY: '',
      OPENAI_DEFAULT_MODEL: 'gpt-5.4',
      OPENAI_DEFAULT_BASE_URL: '',
      OPENAI_DEFAULT_API_STYLE: 'responses',
      NAPCAT_TOKEN: 'test-token'
    }
  });

  assert.equal(runtimeConfig.openai.defaultRoute.apiKey, 'shared-key');
  assert.equal(runtimeConfig.openai.defaultRoute.baseURL, 'https://gateway.example/v1');
  assert.equal(runtimeConfig.openai.defaultRoute.model, 'gpt-5.4');
  assert.equal(runtimeConfig.openai.defaultRoute.apiStyle, 'responses');
  assert.match(runtimeConfig.bot.systemPrompt, /QQ 群助手/);
});

test('writeControlConfig removes stale env lock before saving', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'BOT_PREFIX=/ai'
  ]);
  const lockPath = path.join(cwd, '.env.lock');
  await fs.writeFile(
    lockPath,
    JSON.stringify({
      pid: 999999,
      acquiredAt: new Date(Date.now() - 60_000).toISOString()
    }),
    'utf8'
  );
  const staleDate = new Date(Date.now() - 60_000);
  await fs.utimes(lockPath, staleDate, staleDate);
  const runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });

  const result = await writeControlConfig({
    cwd,
    runtimeConfig,
    config: {
      openAiApiKey: 'test-key',
      napCatToken: 'test-token',
      botPrefix: '/bot'
    }
  });

  assert.equal(result.config.botPrefix, '/bot');
  await assert.rejects(fs.access(lockPath));
});

test('writeControlConfig rejects invalid wechat bridge websocket URLs', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'NAPCAT_WS_URL=ws://127.0.0.1:3001'
  ]);
  const runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });

  await assert.rejects(
    () =>
      writeControlConfig({
        cwd,
        runtimeConfig,
        config: {
          openAiApiKey: 'test-key',
          napCatToken: 'test-token',
          napCatWsUrl: 'ws://127.0.0.1:3001',
          wechatBridgeUrl: 'not-a-valid-wechat-url'
        }
      }),
    /WECHAT_BRIDGE_URL must be a valid ws:\/\/ or wss:\/\/ URL/
  );
});
