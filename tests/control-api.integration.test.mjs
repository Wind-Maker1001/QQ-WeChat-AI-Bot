import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { createControlApiServer } from '../src/app/control-api.mjs';
import { loadRuntimeConfig } from '../src/adapters/config/load-runtime-config.mjs';
import { readControlConfig, writeControlConfig } from '../src/adapters/config/control-config-file.mjs';

async function createTempWorkspace(lines) {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-control-api-'));
  await fs.writeFile(path.join(cwd, '.env'), `${lines.join('\n')}\n`, 'utf8');
  return cwd;
}

test('control API serves allowedChatIds contract and updates config through HTTP', async () => {
  const cwd = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'BOT_SYSTEM_PROMPT=base prompt',
    'ALLOWED_CHAT_IDS=chat-a,chat-b',
    'ALLOWED_USER_IDS=user-a'
  ]);

  let runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });
  let runtimeActive = false;

  const server = createControlApiServer({
    host: '127.0.0.1',
    port: 0,
    accessToken: '',
    logger: {
      error() {}
    },
    getStatus: async () => ({
      runtimeActive,
      runtimeReady: runtimeActive,
      napcatConnected: false,
      activeLockCount: 0
    }),
    getConfig: async () => {
      const result = await readControlConfig({ cwd, runtimeConfig });
      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false
      };
    },
    updateConfig: async (payload) => {
      const result = await writeControlConfig({
        cwd,
        runtimeConfig,
        config: payload
      });
      runtimeConfig = loadRuntimeConfig({
        cwd,
        env: result.envValues,
        loadDotenv: false
      });

      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false
      };
    },
    startRuntime: async () => {
      runtimeActive = true;
      return {
        runtimeActive
      };
    },
    stopRuntime: async () => {
      runtimeActive = false;
      return {
        runtimeActive
      };
    }
  });

  await server.start();

  try {
    const address = server.getAddress();
    assert.ok(address);

    const baseUrl = `http://127.0.0.1:${address.port}`;

    const configResponse = await fetch(`${baseUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    assert.equal(configPayload.allowedChatIds, 'chat-a,chat-b');
    assert.equal(configPayload.allowedUserIds, 'user-a');
    assert.equal(configPayload.botSystemPrompt, 'base prompt');
    assert.equal('allowedGroupIds' in configPayload, false);

    const updateResponse = await fetch(`${baseUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        botSystemPrompt: 'updated base prompt',
        allowedChatIds: 'chat-x,chat-y'
      })
    });

    assert.equal(updateResponse.status, 200);
    const updatedPayload = await updateResponse.json();
    assert.equal(updatedPayload.allowedChatIds, 'chat-x,chat-y');
    assert.equal(updatedPayload.botSystemPrompt, 'updated base prompt');
    assert.equal('allowedGroupIds' in updatedPayload, false);

    const envText = await fs.readFile(path.join(cwd, '.env'), 'utf8');
    assert.match(envText, /BOT_SYSTEM_PROMPT=updated base prompt/);
    assert.match(envText, /ALLOWED_CHAT_IDS=chat-x,chat-y/);
    assert.doesNotMatch(envText, /ALLOWED_GROUP_IDS=/);

    const startResponse = await fetch(`${baseUrl}/start`, {
      method: 'POST'
    });
    assert.equal(startResponse.status, 200);
    assert.equal((await startResponse.json()).runtimeActive, true);

    const stopResponse = await fetch(`${baseUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    assert.equal((await stopResponse.json()).runtimeActive, false);
  } finally {
    await server.stop();
  }
});

test('control API returns 400 for rejected config updates', async () => {
  const cwd = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token'
  ]);

  let runtimeConfig = loadRuntimeConfig({ cwd, loadDotenv: true });

  const server = createControlApiServer({
    host: '127.0.0.1',
    port: 0,
    accessToken: '',
    logger: {
      error() {}
    },
    getStatus: async () => ({
      runtimeActive: false
    }),
    getConfig: async () => {
      const result = await readControlConfig({ cwd, runtimeConfig });
      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false
      };
    },
    updateConfig: async (payload) => {
      const result = await writeControlConfig({
        cwd,
        runtimeConfig,
        config: payload
      });
      runtimeConfig = loadRuntimeConfig({
        cwd,
        env: result.envValues,
        loadDotenv: false
      });

      return {
        ...result.config,
        envPath: result.envPath,
        restartRequired: false
      };
    },
    startRuntime: async () => ({
      runtimeActive: true
    }),
    stopRuntime: async () => ({
      runtimeActive: false
    })
  });

  await server.start();

  try {
    const address = server.getAddress();
    assert.ok(address);

    const response = await fetch(`http://127.0.0.1:${address.port}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        openAiApiKey: 'test-key',
        napCatToken: 'test-token',
        napCatWsUrl: 'ws://127.0.0.1:3001',
        wechatBridgeUrl: 'not-a-valid-wechat-url'
      })
    });

    assert.equal(response.status, 400);
    const payload = await response.json();
    assert.match(payload.error, /WECHAT_BRIDGE_URL must be a valid ws:\/\/ or wss:\/\/ URL/);
  } finally {
    await server.stop();
  }
});

test('control API returns 400 for invalid JSON request bodies', async () => {
  const server = createControlApiServer({
    host: '127.0.0.1',
    port: 0,
    accessToken: '',
    logger: {
      error() {}
    },
    getStatus: async () => ({}),
    getConfig: async () => ({}),
    updateConfig: async () => ({}),
    startRuntime: async () => ({}),
    stopRuntime: async () => ({})
  });

  await server.start();

  try {
    const address = server.getAddress();
    assert.ok(address);

    const response = await fetch(`http://127.0.0.1:${address.port}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: '{"invalidJson":'
    });

    assert.equal(response.status, 400);
    const payload = await response.json();
    assert.equal(payload.error, 'Request body must be valid JSON.');
  } finally {
    await server.stop();
  }
});

test('control API rejects unauthorized requests when bearer auth is configured', async () => {
  let getStatusCalls = 0;

  const server = createControlApiServer({
    host: '127.0.0.1',
    port: 0,
    accessToken: 'control-secret',
    logger: {
      error() {}
    },
    getStatus: async () => {
      getStatusCalls += 1;
      return {
        runtimeActive: false
      };
    },
    getConfig: async () => ({}),
    updateConfig: async () => ({}),
    startRuntime: async () => ({}),
    stopRuntime: async () => ({})
  });

  await server.start();

  try {
    const address = server.getAddress();
    assert.ok(address);

    const baseUrl = `http://127.0.0.1:${address.port}`;

    const unauthorizedResponse = await fetch(`${baseUrl}/status`);
    assert.equal(unauthorizedResponse.status, 401);
    assert.equal((await unauthorizedResponse.json()).error, 'Control API authentication failed.');
    assert.equal(getStatusCalls, 0);

    const wrongTokenResponse = await fetch(`${baseUrl}/status`, {
      headers: {
        Authorization: 'Bearer wrong-secret'
      }
    });
    assert.equal(wrongTokenResponse.status, 401);
    assert.equal(getStatusCalls, 0);

    const authorizedResponse = await fetch(`${baseUrl}/status`, {
      headers: {
        Authorization: 'Bearer control-secret'
      }
    });
    assert.equal(authorizedResponse.status, 200);
    assert.equal((await authorizedResponse.json()).runtimeActive, false);
    assert.equal(getStatusCalls, 1);
  } finally {
    await server.stop();
  }
});
