import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { WebSocketServer } from 'ws';
import {
  createExecutionProjection,
  DELIBERATION_EXECUTION_STAGES,
  EXECUTION_KIND_DELIBERATION,
  EXECUTION_KIND_DIRECT,
  EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK,
  EXECUTION_STAGE_DIRECT,
  EXECUTION_STAGE_DRAFT,
  EXECUTION_STAGE_PLANNER,
  EXECUTION_STAGE_REWRITE,
  getExecutionSummary
} from '../src/domain/execution-projection.mjs';

const repoRoot = 'D:\\QQ AI Bot';
const supervisorEntry = path.join(repoRoot, 'src', 'index.mjs');
const PNG_DATA_URL =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9s5lMP8AAAAASUVORK5CYII=';
const JPEG_BASE64 =
  '/9j/4AAQSkZJRgABAQAAAQABAAD/2wCEAAkGBxAQEBAQEA8PDw8PDw8PDw8PDw8PDw8QFREWFhURFRUYHSggGBolGxUVITEhJSkrLi4uFx8zODMsNygtLisBCgoKDg0OGxAQGi0fHyUtLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLS0tLf/AABEIAAEAAgMBIgACEQEDEQH/xAAXAAEBAQEAAAAAAAAAAAAAAAAAAQID/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEAMQAAAB6A//xAAVEAEBAAAAAAAAAAAAAAAAAAAAEf/aAAgBAQABBQKf/8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAEP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAAAAAAAEP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAEP/aAAgBAQABPyF//9k=';

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function countLogOccurrences(logs, needle) {
  return logs.join('').split(needle).length - 1;
}

async function waitFor(predicate, { timeoutMs = 15000, intervalMs = 100, label = 'condition' } = {}) {
  const startedAt = Date.now();

  while (Date.now() - startedAt < timeoutMs) {
    const result = await predicate();

    if (result) {
      return result;
    }

    await delay(intervalMs);
  }

  throw new Error(`Timed out waiting for ${label}.`);
}

async function createTempWorkspace(lines) {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-supervisor-'));
  await fs.writeFile(path.join(cwd, '.env'), `${lines.join('\n')}\n`, 'utf8');
  return cwd;
}

async function listenServer(server) {
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject);
      resolve();
    });
  });

  const address = server.address();

  if (!address || typeof address === 'string') {
    throw new Error('Failed to resolve dynamic server port.');
  }

  return address.port;
}

async function closeServer(server) {
  await new Promise((resolve, reject) => {
    server.close((error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });
}

async function getFreePort() {
  const server = http.createServer();
  const port = await listenServer(server);
  await closeServer(server);
  return port;
}

async function createFakeOpenAiServer({ onRequest } = {}) {
  const requests = [];
  const server = http.createServer(async (req, res) => {
    if (
      req.method !== 'POST' ||
      (req.url !== '/v1/chat/completions' && req.url !== '/v1/responses')
    ) {
      res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
      res.end(JSON.stringify({ error: 'not found' }));
      return;
    }

    const chunks = [];

    for await (const chunk of req) {
      chunks.push(chunk);
    }

    const body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    requests.push({
      endpoint: req.url,
      body
    });

    const userText =
      req.url === '/v1/responses'
        ? extractResponsesUserText(body)
        : extractChatUserText(body);

    if (typeof onRequest === 'function') {
      const customResponse = await onRequest({
        endpoint: req.url,
        body,
        userText,
        requestCount: requests.length
      });

      if (customResponse) {
        if (customResponse.destroySocket === true) {
          req.socket.destroy();
          return;
        }

        res.writeHead(customResponse.statusCode ?? 200, {
          'Content-Type': 'application/json; charset=utf-8',
          ...(customResponse.headers ?? {})
        });
        res.end(
          customResponse.rawBody ??
            JSON.stringify(customResponse.payload ?? { ok: true })
        );
        return;
      }
    }

    let assistantText = `reply:${userText}`;

    if (userText.includes('[INTERNAL_PLANNER]')) {
      assistantText = 'planner-outline';
    } else if (userText.includes('[INTERNAL_REWRITE]')) {
      assistantText = 'rewritten-answer';
    } else if (userText.includes('[INTERNAL_DRAFT]')) {
      assistantText = 'draft-answer';
    }

    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });

    if (req.url === '/v1/responses') {
      res.end(
        JSON.stringify({
          id: `resp_${Date.now()}`,
          output_text: assistantText
        })
      );
      return;
    }

    res.end(
      JSON.stringify({
        id: `chatcmpl_${Date.now()}`,
        object: 'chat.completion',
        created: Math.floor(Date.now() / 1000),
        model: body.model || 'fake-model',
        choices: [
          {
            index: 0,
            finish_reason: 'stop',
            message: {
              role: 'assistant',
              content: assistantText
            }
          }
        ]
      })
    );
  });

  const port = await listenServer(server);

  return {
    port,
    requests,
    countRequestsWithImages() {
      return requests.filter(({ endpoint, body }) =>
        endpoint === '/v1/responses'
          ? Array.isArray(body?.input) &&
            body.input.some((item) =>
              Array.isArray(item?.content) &&
              item.content.some((contentItem) => contentItem?.type === 'input_image')
            )
          : Array.isArray(body?.messages) &&
            body.messages.some((message) =>
              Array.isArray(message?.content) &&
              message.content.some(
                (item) =>
                  item &&
                  typeof item === 'object' &&
                  (item.type === 'image_url' || item.type === 'input_image')
              )
            )
      ).length;
    },
    async close() {
      await closeServer(server);
    }
  };
}

function extractChatUserText(body) {
  const lastMessage = Array.isArray(body?.messages) ? body.messages.at(-1) : null;
  const rawContent = lastMessage?.content;

  return typeof rawContent === 'string'
    ? rawContent
    : Array.isArray(rawContent)
      ? rawContent
          .map((item) => (item && typeof item === 'object' && typeof item.text === 'string' ? item.text : ''))
          .join(' ')
          .trim()
      : '';
}

function extractResponsesUserText(body) {
  const inputItem = Array.isArray(body?.input) ? body.input.at(-1) : null;
  const content = inputItem?.content;

  if (!Array.isArray(content)) {
    return '';
  }

  return content
    .map((item) => (item && typeof item === 'object' && typeof item.text === 'string' ? item.text : ''))
    .join(' ')
    .trim();
}

async function createFakeNapCatServer({ token }) {
  const sentGroupMessages = [];
  const clients = new Set();
  const messages = new Map();
  const server = http.createServer((_, res) => {
    res.writeHead(404);
    res.end();
  });
  const wss = new WebSocketServer({ server });

  wss.on('connection', (socket, req) => {
    assert.equal(req.headers.authorization, `Bearer ${token}`);
    clients.add(socket);

    socket.on('message', (buffer) => {
      const packet = JSON.parse(buffer.toString('utf8'));

      if (packet.action === 'send_group_msg') {
        sentGroupMessages.push({
          groupId: packet.params?.group_id,
          message: packet.params?.message
        });
      }

      if (packet.action === 'get_msg') {
        socket.send(
          JSON.stringify({
            status: 'ok',
            retcode: 0,
            data: messages.get(String(packet.params?.message_id || '')) ?? null,
            echo: packet.echo
          })
        );
        return;
      }

      if (packet.action === 'download_file_image_stream') {
        socket.send(
          JSON.stringify({
            status: 'ok',
            retcode: 0,
            data: {
              type: 'stream',
              data_type: 'file_chunk',
              data: JPEG_BASE64,
              index: 0
            },
            echo: packet.echo
          })
        );
        socket.send(
          JSON.stringify({
            status: 'ok',
            retcode: 0,
            data: {
              type: 'response',
              data_type: 'file_complete'
            },
            echo: packet.echo
          })
        );
        return;
      }

      socket.send(
        JSON.stringify({
          status: 'ok',
          retcode: 0,
          data: {
            ok: true
          },
          echo: packet.echo
        })
      );
    });

    socket.on('close', () => {
      clients.delete(socket);
    });
  });

  const port = await listenServer(server);

  return {
    port,
    sentGroupMessages,
    seedMessage({
      messageId,
      groupId = 'qq_group_demo',
      userId = 'qq_user_demo',
      selfId = 'qq_self_demo',
      text = '',
      replyToMessageIds = [],
      images = []
    }) {
      const message = [];

      for (const replyMessageId of replyToMessageIds) {
        message.push({
          type: 'reply',
          data: {
            id: replyMessageId
          }
        });
      }

      if (text) {
        message.push({
          type: 'text',
          data: {
            text
          }
        });
      }

      for (const image of images) {
        message.push({
          type: 'image',
          data: {
            file: image.fileId,
            url: image.imageUrl
          }
        });
      }

      const event = {
        post_type: 'message',
        message_type: 'group',
        message_id: messageId,
        group_id: groupId,
        user_id: userId,
        self_id: selfId,
        raw_message: text,
        message
      };

      messages.set(String(messageId), event);
      return event;
    },
    emitGroupMessage({
      messageId = 'qq_msg_1',
      groupId = 'qq_group_demo',
      userId = 'qq_user_demo',
      selfId = 'qq_self_demo',
      text = '/ai hello qq',
      replyToMessageIds = [],
      images = []
    } = {}) {
      const event = this.seedMessage({
        messageId,
        groupId,
        userId,
        selfId,
        text,
        replyToMessageIds,
        images
      });

      for (const client of clients) {
        client.send(JSON.stringify(event));
      }
    },
    async close() {
      wss.close();
      await closeServer(server);
    }
  };
}

async function createFakeWechatBridgeServer({ token }) {
  const sentGroupTexts = [];
  const clients = new Set();
  const messages = new Map();
  const server = http.createServer((_, res) => {
    res.writeHead(404);
    res.end();
  });
  const wss = new WebSocketServer({ server });

  wss.on('connection', (socket, req) => {
    assert.equal(req.headers.authorization, `Bearer ${token}`);
    clients.add(socket);

    socket.on('message', (buffer) => {
      const packet = JSON.parse(buffer.toString('utf8'));

      if (packet.type !== 'action') {
        return;
      }

      if (packet.action === 'send_group_text') {
        sentGroupTexts.push({
          chatId: packet.params?.chatId,
          text: packet.params?.text
        });
      }

      if (packet.action === 'get_message') {
        socket.send(
          JSON.stringify({
            type: 'action_result',
            requestId: packet.requestId,
            ok: true,
            data: messages.get(String(packet.params?.messageId || '')) ?? null
          })
        );
        return;
      }

      if (packet.action === 'download_image') {
        socket.send(
          JSON.stringify({
            type: 'action_result',
            requestId: packet.requestId,
            ok: true,
            data: {
              dataUrl: PNG_DATA_URL
            }
          })
        );
        return;
      }

      socket.send(
        JSON.stringify({
          type: 'action_result',
          requestId: packet.requestId,
          ok: true,
          data: {
            ok: true
          }
        })
      );
    });

    socket.on('close', () => {
      clients.delete(socket);
    });
  });

  const port = await listenServer(server);

  return {
    port,
    sentGroupTexts,
    seedMessage({
      messageId,
      chatId = 'wx_group_demo',
      userId = 'wx_user_demo',
      selfId = 'wx_self_demo',
      text = '',
      replyToMessageIds = [],
      images = []
    }) {
      const packet = {
        type: 'group_message',
        messageId,
        chatId,
        userId,
        selfId,
        text,
        rawText: text,
        mentioned: false,
        replyToMessageIds,
        images
      };

      messages.set(String(messageId), packet);
      return packet;
    },
    emitGroupMessage({
      messageId = 'wx_msg_1',
      chatId = 'wx_group_demo',
      userId = 'wx_user_demo',
      selfId = 'wx_self_demo',
      text = '/ai hello wechat',
      replyToMessageIds = [],
      images = []
    } = {}) {
      const packet = this.seedMessage({
        messageId,
        chatId,
        userId,
        selfId,
        text,
        replyToMessageIds,
        images
      });

      for (const client of clients) {
        client.send(JSON.stringify(packet));
      }
    },
    disconnectClients(code = 1012, reason = 'test disconnect') {
      for (const client of clients) {
        client.close(code, reason);
      }
    },
    async close() {
      wss.close();
      await closeServer(server);
    }
  };
}

function spawnSupervisor({ cwd, controlApiPort, logs }) {
  const child = spawn(process.execPath, [supervisorEntry], {
    cwd,
    env: {
      ...process.env,
      QQ_AI_BOT_CONTROL_API_PORT: String(controlApiPort)
    },
    stdio: ['ignore', 'pipe', 'pipe']
  });

  child.stdout?.on('data', (chunk) => {
    logs.push(chunk.toString('utf8'));
  });
  child.stderr?.on('data', (chunk) => {
    logs.push(chunk.toString('utf8'));
  });

  return child;
}

async function stopChild(child) {
  if (child.exitCode !== null) {
    return;
  }

  child.kill('SIGTERM');

  await Promise.race([
    new Promise((resolve) => child.once('exit', resolve)),
    delay(5000).then(() => {
      child.kill('SIGKILL');
    })
  ]);
}

test('supervisor starts both workers and processes QQ + WeChat messages end to end', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `supervisor status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      text: '/ai hello qq'
    });
    wechatServer.emitGroupMessage({
      text: '/ai hello wechat'
    });

    await waitFor(
      () =>
        napcatServer.sentGroupMessages.length === 1 && wechatServer.sentGroupTexts.length === 1,
      {
        label: `outbound replies. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].groupId, 'qq_group_demo');
    assert.equal(napcatServer.sentGroupMessages[0].message, 'reply:hello qq');
    assert.equal(wechatServer.sentGroupTexts[0].chatId, 'wx_group_demo');
    assert.equal(wechatServer.sentGroupTexts[0].text, 'reply:hello wechat');

    const sessionsPath = path.join(workspace, 'data', 'sessions.json');
    const sessions = JSON.parse(await fs.readFile(sessionsPath, 'utf8'));
    assert.ok(sessions['channel=qq|chat=qq_group_demo|user=qq_user_demo']);
    assert.ok(sessions['channel=wechat|chat=wx_group_demo|user=wx_user_demo']);
    assert.ok(openAiServer.requests.length >= 2);

    const statusResponse = await fetch(`${controlApiUrl}/status`);
    assert.equal(statusResponse.status, 200);
    const statusPayload = await statusResponse.json();
    assert.equal(statusPayload.lastQqLlmRequest.route, 'default');
    assert.equal(statusPayload.lastQqLlmRequest.model, 'fake-default');
    assert.equal(statusPayload.lastQqLlmRequest.effectiveApiStyle, 'chat_completions');
    assert.equal(statusPayload.lastQqLlmRequest.executionKind, EXECUTION_KIND_DIRECT);
    assert.equal(
      statusPayload.lastQqLlmRequest.executionSummary,
      getExecutionSummary(EXECUTION_KIND_DIRECT)
    );
    assert.deepEqual(
      statusPayload.lastQqLlmRequest.executionProjection,
      createExecutionProjection({
        kind: EXECUTION_KIND_DIRECT,
        completedStages: [EXECUTION_STAGE_DIRECT]
      })
    );
    assert.equal(statusPayload.runtimeReady, true);
    assert.equal(statusPayload.lastWechatLlmRequest.route, 'default');
    assert.equal(statusPayload.lastWechatLlmRequest.model, 'fake-default');
    assert.equal(statusPayload.lastWechatLlmRequest.effectiveApiStyle, 'chat_completions');
    assert.equal(statusPayload.lastWechatLlmRequest.executionKind, EXECUTION_KIND_DIRECT);
    assert.equal(
      statusPayload.lastWechatLlmRequest.executionSummary,
      getExecutionSummary(EXECUTION_KIND_DIRECT)
    );
    assert.deepEqual(
      statusPayload.lastWechatLlmRequest.executionProjection,
      createExecutionProjection({
        kind: EXECUTION_KIND_DIRECT,
        completedStages: [EXECUTION_STAGE_DIRECT]
      })
    );
    assert.equal(statusPayload.wechatRuntimeReady, true);

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatRuntimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor falls back to DeepSeek after transient GPT upstream failure', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer({
    onRequest({ endpoint, userText }) {
      if (endpoint === '/v1/responses' && !userText.includes('[INTERNAL_')) {
        return {
          statusCode: 502,
          payload: {
            error: 'Upstream request failed'
          }
        };
      }

      return null;
    }
  });
  const deepseekServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-gpt',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=responses',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=responses',
    'DEEPSEEK_FALLBACK_ENABLED=true',
    'DEEPSEEK_API_KEY=deepseek-key',
    'DEEPSEEK_MODEL=deepseek-chat',
    `DEEPSEEK_BASE_URL=http://127.0.0.1:${deepseekServer.port}/v1`,
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `DeepSeek fallback initial status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_deepseek_fallback',
      text: '/ai hello fallback'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.some((message) => message.message === 'reply:hello fallback'),
      {
        label: `DeepSeek fallback outbound reply. logs:\n${logs.join('')}`
      }
    );

    const statusPayload = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.lastQqLlmRequest?.model === 'deepseek-chat' ? payload : false;
        } catch {
          return false;
        }
      },
      {
        label: `DeepSeek fallback status payload. logs:\n${logs.join('')}`
      }
    );

    assert.ok(openAiServer.requests.length >= 1);
    assert.equal(
      deepseekServer.requests.filter((request) => request.endpoint === '/v1/chat/completions').length,
      1
    );
    assert.equal(statusPayload.lastQqLlmRequest.route, 'default');
    assert.equal(statusPayload.lastQqLlmRequest.configuredModel, 'fake-gpt');
    assert.equal(statusPayload.lastQqLlmRequest.model, 'deepseek-chat');
    assert.equal(statusPayload.lastQqLlmRequest.configuredApiStyle, 'responses');
    assert.equal(statusPayload.lastQqLlmRequest.effectiveApiStyle, 'chat_completions');
    assert.equal(statusPayload.lastQqLlmRequest.executionKind, EXECUTION_KIND_DIRECT);
    assert.equal(statusPayload.lastQqLlmRequest.executionProjection.degraded, true);
    assert.deepEqual(
      statusPayload.lastQqLlmRequest.executionProjection.recoveries,
      [EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK]
    );
    assert.equal(statusPayload.lastQqLlmRequest.responseId, '');
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      deepseekServer.close(),
      napcatServer.close()
    ]);
  }
});

test('supervisor does not fall back to DeepSeek on rejected GPT requests', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer({
    onRequest({ endpoint, userText }) {
      if (endpoint === '/v1/responses' && !userText.includes('[INTERNAL_')) {
        return {
          statusCode: 401,
          payload: {
            error: 'unauthorized'
          }
        };
      }

      return null;
    }
  });
  const deepseekServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-gpt',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=responses',
    'DEEPSEEK_FALLBACK_ENABLED=true',
    'DEEPSEEK_API_KEY=deepseek-key',
    'DEEPSEEK_MODEL=deepseek-chat',
    `DEEPSEEK_BASE_URL=http://127.0.0.1:${deepseekServer.port}/v1`,
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `rejected-request fallback initial status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_no_fallback_on_401',
      text: '/ai hello unauthorized'
    });

    const statusPayload = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.lastQqLlmFailure?.route === 'default' ? payload : false;
        } catch {
          return false;
        }
      },
      {
        label: `rejected-request failure status. logs:\n${logs.join('')}`
      }
    );

    assert.equal(
      deepseekServer.requests.filter((request) => request.endpoint === '/v1/chat/completions').length,
      0
    );
    assert.match(statusPayload.lastQqLlmFailure.error, /401|unauthorized/i);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      deepseekServer.close(),
      napcatServer.close()
    ]);
  }
});

test('supervisor resolves replied images for QQ and WeChat end to end', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `supervisor image status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.seedMessage({
      messageId: 'qq_reply_img_1',
      text: 'image source',
      images: [
        {
          fileId: 'qq-image-1',
          imageUrl: ''
        }
      ]
    });
    wechatServer.seedMessage({
      messageId: 'wx_reply_img_1',
      text: 'image source',
      images: [
        {
          fileId: 'wx-image-1'
        }
      ]
    });

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_with_reply',
      text: '/ai analyze this reply image',
      replyToMessageIds: ['qq_reply_img_1']
    });
    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_with_reply',
      text: '/ai analyze this reply image',
      replyToMessageIds: ['wx_reply_img_1']
    });

    await waitFor(
      () =>
        napcatServer.sentGroupMessages.length === 1 &&
        wechatServer.sentGroupTexts.length === 1 &&
        openAiServer.countRequestsWithImages() >= 2,
      {
        label: `image replies. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].groupId, 'qq_group_demo');
    assert.equal(wechatServer.sentGroupTexts[0].chatId, 'wx_group_demo');
    assert.ok(napcatServer.sentGroupMessages[0].message.startsWith('reply:'));
    assert.ok(wechatServer.sentGroupTexts[0].text.startsWith('reply:'));
    assert.ok(openAiServer.countRequestsWithImages() >= 2);

    const sessionsPath = path.join(workspace, 'data', 'sessions.json');
    const sessions = JSON.parse(await fs.readFile(sessionsPath, 'utf8'));
    assert.equal(
      sessions['channel=qq|chat=qq_group_demo|user=qq_user_demo']?.shared?.lastImageRefs?.length,
      1
    );
    assert.equal(
      sessions['channel=wechat|chat=wx_group_demo|user=wx_user_demo']?.shared?.lastImageRefs?.length,
      1
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatRuntimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor resolves direct image messages for QQ and WeChat end to end', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `supervisor direct-image status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_direct_image',
      text: '/ai analyze this image',
      images: [
        {
          fileId: 'qq-image-direct'
        }
      ]
    });
    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_direct_image',
      text: '/ai analyze this image',
      images: [
        {
          fileId: 'wx-image-direct'
        }
      ]
    });

    await waitFor(
      () =>
        napcatServer.sentGroupMessages.length === 1 &&
        wechatServer.sentGroupTexts.length === 1 &&
        openAiServer.countRequestsWithImages() >= 2,
      {
        label: `direct image replies. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].groupId, 'qq_group_demo');
    assert.equal(wechatServer.sentGroupTexts[0].chatId, 'wx_group_demo');
    assert.ok(napcatServer.sentGroupMessages[0].message.startsWith('reply:'));
    assert.ok(wechatServer.sentGroupTexts[0].text.startsWith('reply:'));
    assert.ok(openAiServer.countRequestsWithImages() >= 2);

    const sessionsPath = path.join(workspace, 'data', 'sessions.json');
    const sessions = JSON.parse(await fs.readFile(sessionsPath, 'utf8'));
    assert.equal(
      sessions['channel=qq|chat=qq_group_demo|user=qq_user_demo']?.shared?.lastImageRefs?.length,
      1
    );
    assert.equal(
      sessions['channel=wechat|chat=wx_group_demo|user=wx_user_demo']?.shared?.lastImageRefs?.length,
      1
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatRuntimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor does not reuse cached images for generic reply text after an image turn', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `supervisor cached-image status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.seedMessage({
      messageId: 'qq_plain_reply_target',
      text: 'plain source text'
    });

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_prime_image',
      text: '/ai analyze this image',
      images: [
        {
          fileId: 'qq-image-direct'
        }
      ]
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 1 && openAiServer.countRequestsWithImages() === 1,
      {
        label: `initial image request. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_plain_reply_after_image',
      text: '/ai speak',
      replyToMessageIds: ['qq_plain_reply_target']
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 2 && openAiServer.requests.length >= 2,
      {
        label: `generic reply after image turn. logs:\n${logs.join('')}`
      }
    );

    assert.equal(openAiServer.countRequestsWithImages(), 1);
    assert.equal(napcatServer.sentGroupMessages[1].message, 'reply:speak');

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close()
    ]);
  }
});

test('supervisor uses planner-draft-rewrite pipeline for complex text requests', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=responses',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `supervisor planner status. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_complex_pipeline',
      text: '/ai Please analyze why this refactor causes boundary leakage and compare two alternative implementations step by step.'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 1 && openAiServer.requests.length === 3,
      {
        label: `planner pipeline requests. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].message, 'rewritten-answer');
    assert.equal(openAiServer.requests.length, 3);

    const sessionsPath = path.join(workspace, 'data', 'sessions.json');
    const sessions = JSON.parse(await fs.readFile(sessionsPath, 'utf8'));
    const storedMessages = sessions['channel=qq|chat=qq_group_demo|user=qq_user_demo']?.routes?.default?.messages ?? [];
    const storedPreviousResponseId = sessions['channel=qq|chat=qq_group_demo|user=qq_user_demo']?.routes?.default?.previousResponseId;
    const statusPayload = await (await fetch(`${controlApiUrl}/status`)).json();

    assert.equal(storedMessages.length, 2);
    assert.equal(storedMessages[0].role, 'user');
    assert.match(storedMessages[0].content, /boundary leakage/);
    assert.equal(storedMessages[1].role, 'assistant');
    assert.equal(storedMessages[1].content, 'rewritten-answer');
    assert.equal(
      typeof storedPreviousResponseId === 'string' && storedPreviousResponseId ? storedPreviousResponseId : null,
      null
    );
    assert.equal(statusPayload.lastQqLlmRequest.executionKind, EXECUTION_KIND_DELIBERATION);
    assert.equal(
      statusPayload.lastQqLlmRequest.executionSummary,
      getExecutionSummary(EXECUTION_KIND_DELIBERATION)
    );
    assert.deepEqual(
      statusPayload.lastQqLlmRequest.executionProjection,
      createExecutionProjection({
        kind: EXECUTION_KIND_DELIBERATION,
        stages: DELIBERATION_EXECUTION_STAGES,
        completedStages: [
          EXECUTION_STAGE_PLANNER,
          EXECUTION_STAGE_DRAFT,
          EXECUTION_STAGE_REWRITE
        ]
      })
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
  } finally {
    await stopChild(supervisor);
    await openAiServer.close();
    await napcatServer.close();
  }
});

test('supervisor restarts QQ worker after unexpected exit and continues processing messages', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    const initialStatus = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            typeof payload.workerProcessId === 'number'
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial QQ worker status. logs:\n${logs.join('')}`
      }
    );

    const initialWorkerPid = initialStatus.workerProcessId;
    assert.equal(typeof initialWorkerPid, 'number');

    process.kill(initialWorkerPid);

    const restartedStatus = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            typeof payload.workerProcessId === 'number' &&
            payload.workerProcessId !== initialWorkerPid
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        timeoutMs: 20000,
        label: `QQ worker restart. logs:\n${logs.join('')}`
      }
    );

    assert.notEqual(restartedStatus.workerProcessId, initialWorkerPid);

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_after_restart',
      text: '/ai hello after restart'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 1,
      {
        label: `QQ reply after restart. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].groupId, 'qq_group_demo');
    assert.equal(napcatServer.sentGroupMessages[0].message, 'reply:hello after restart');

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([openAiServer.close(), napcatServer.close()]);
  }
});

test('supervisor restarts WeChat worker after unexpected exit and continues processing messages', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    const initialStatus = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true &&
            typeof payload.wechatWorkerProcessId === 'number'
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial WeChat worker status. logs:\n${logs.join('')}`
      }
    );

    const initialWechatWorkerPid = initialStatus.wechatWorkerProcessId;
    assert.equal(typeof initialWechatWorkerPid, 'number');

    process.kill(initialWechatWorkerPid);

    const restartedStatus = await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true &&
            typeof payload.wechatWorkerProcessId === 'number' &&
            payload.wechatWorkerProcessId !== initialWechatWorkerPid
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        timeoutMs: 20000,
        label: `WeChat worker restart. logs:\n${logs.join('')}`
      }
    );

    assert.notEqual(restartedStatus.wechatWorkerProcessId, initialWechatWorkerPid);

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_restart',
      text: '/ai hello after wechat restart'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.length === 1,
      {
        label: `WeChat reply after restart. logs:\n${logs.join('')}`
      }
    );

    assert.equal(wechatServer.sentGroupTexts[0].chatId, 'wx_group_demo');
    assert.equal(wechatServer.sentGroupTexts[0].text, 'reply:hello after wechat restart');

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatRuntimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor disables QQ worker restarts after repeated boot failures', async () => {
  const logs = [];
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    'OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:1/v1',
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    'OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:1/v1',
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          return response.ok ? true : false;
        } catch {
          return false;
        }
      },
      {
        label: `control API startup during boot failure test. logs:\n${logs.join('')}`
      }
    );

    await waitFor(
      () =>
        logs.join('').includes('Worker restart disabled after 3 consecutive boot failures'),
      {
        timeoutMs: 20000,
        label: `QQ worker boot failure disablement. logs:\n${logs.join('')}`
      }
    );

    await delay(1500);

    const statusResponse = await fetch(`${controlApiUrl}/status`);
    assert.equal(statusResponse.status, 200);
    const statusPayload = await statusResponse.json();
    assert.equal(statusPayload.runtimeActive, false);
    assert.equal(statusPayload.workerProcessId, null);
    assert.equal(countLogOccurrences(logs, '[supervisor] Worker started:'), 3);
  } finally {
    await stopChild(supervisor);
  }
});

test('supervisor disables WeChat worker restarts after repeated boot failures while QQ stays healthy', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'WECHAT_BRIDGE_URL=http://127.0.0.1:1',
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `QQ healthy during WeChat boot failure. logs:\n${logs.join('')}`
      }
    );

    await waitFor(
      () =>
        logs.join('').includes('Wechat worker restart disabled after 3 consecutive boot failures.'),
      {
        timeoutMs: 20000,
        label: `WeChat worker boot failure disablement. logs:\n${logs.join('')}`
      }
    );

    await delay(1500);

    const statusResponse = await fetch(`${controlApiUrl}/status`);
    assert.equal(statusResponse.status, 200);
    const statusPayload = await statusResponse.json();
    assert.equal(statusPayload.runtimeActive, true);
    assert.equal(statusPayload.napcatConnected, true);
    assert.equal(statusPayload.wechatRuntimeActive, false);
    assert.equal(statusPayload.wechatBridgeConnected, false);
    assert.equal(countLogOccurrences(logs, '[supervisor] Wechat worker started:'), 3);

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_after_wechat_failure_disable',
      text: '/ai hello after wechat failure'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 1,
      {
        label: `QQ still processes messages after WeChat failure disablement. logs:\n${logs.join('')}`
      }
    );

    assert.equal(
      napcatServer.sentGroupMessages[0].message,
      'reply:hello after wechat failure'
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
  } finally {
    await stopChild(supervisor);
    await Promise.all([openAiServer.close(), napcatServer.close()]);
  }
});

test('supervisor can recover from QQ boot-failure disablement after config fix and /start', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          return response.ok ? true : false;
        } catch {
          return false;
        }
      },
      {
        label: `control API startup during recovery test. logs:\n${logs.join('')}`
      }
    );

    await waitFor(
      () =>
        logs.join('').includes('Worker restart disabled after 3 consecutive boot failures'),
      {
        timeoutMs: 20000,
        label: `QQ worker disablement before recovery. logs:\n${logs.join('')}`
      }
    );

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();
    assert.equal(configPayload.napCatToken, '');

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        napCatWsUrl: `ws://127.0.0.1:${napcatServer.port}`,
        napCatToken: 'napcat-test-token'
      })
    });
    assert.equal(updateResponse.status, 200);

    const startResponse = await fetch(`${controlApiUrl}/start`, {
      method: 'POST'
    });
    assert.equal(startResponse.status, 200);
    const startPayload = await startResponse.json();
    assert.equal(startPayload.runtimeActive, true);

    await waitFor(
      async () => {
        const response = await fetch(`${controlApiUrl}/status`);
        const payload = await response.json();
        return payload.runtimeActive === true && payload.napcatConnected === true
          ? payload
          : false;
      },
      {
        timeoutMs: 20000,
        label: `QQ worker recovery after /start. logs:\n${logs.join('')}`
      }
    );

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_after_recovery',
      text: '/ai hello after recovery'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.length === 1,
      {
        label: `QQ reply after recovery. logs:\n${logs.join('')}`
      }
    );

    assert.equal(napcatServer.sentGroupMessages[0].message, 'reply:hello after recovery');

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([openAiServer.close(), napcatServer.close()]);
  }
});

test('supervisor can recover from WeChat boot-failure disablement after config fix and /start', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'WECHAT_BRIDGE_URL=http://127.0.0.1:1',
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          return response.ok ? true : false;
        } catch {
          return false;
        }
      },
      {
        label: `control API startup during WeChat recovery test. logs:\n${logs.join('')}`
      }
    );

    await waitFor(
      () =>
        logs.join('').includes('Wechat worker restart disabled after 3 consecutive boot failures.'),
      {
        timeoutMs: 20000,
        label: `WeChat worker disablement before recovery. logs:\n${logs.join('')}`
      }
    );

    const statusBeforeRecovery = await fetch(`${controlApiUrl}/status`);
    assert.equal(statusBeforeRecovery.status, 200);
    const statusBeforeRecoveryPayload = await statusBeforeRecovery.json();
    assert.equal(statusBeforeRecoveryPayload.runtimeActive, true);
    assert.equal(statusBeforeRecoveryPayload.napcatConnected, true);
    assert.equal(statusBeforeRecoveryPayload.wechatRuntimeActive, false);

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        wechatBridgeUrl: `ws://127.0.0.1:${wechatServer.port}`,
        wechatBridgeToken: 'wechat-test-token'
      })
    });
    assert.equal(updateResponse.status, 200);

    const startResponse = await fetch(`${controlApiUrl}/start`, {
      method: 'POST'
    });
    assert.equal(startResponse.status, 200);
    const startPayload = await startResponse.json();
    assert.equal(startPayload.runtimeActive, true);

    await waitFor(
      async () => {
        const response = await fetch(`${controlApiUrl}/status`);
        const payload = await response.json();
        return payload.runtimeActive === true &&
          payload.napcatConnected === true &&
          payload.wechatRuntimeActive === true &&
          payload.wechatBridgeConnected === true
          ? payload
          : false;
      },
      {
        timeoutMs: 20000,
        label: `WeChat worker recovery after /start. logs:\n${logs.join('')}`
      }
    );

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_recovery',
      text: '/ai hello after wechat recovery'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.length === 1,
      {
        label: `WeChat reply after recovery. logs:\n${logs.join('')}`
      }
    );

    assert.equal(
      wechatServer.sentGroupTexts[0].text,
      'reply:hello after wechat recovery'
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatRuntimeActive, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor status reflects wechat config changes even while runtime is stopped', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'WECHAT_BRIDGE_URL=',
    'WECHAT_BRIDGE_TOKEN=',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatConfigured === false
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial stopped-config status. logs:\n${logs.join('')}`
      }
    );

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
    const stopPayload = await stopResponse.json();
    assert.equal(stopPayload.runtimeActive, false);
    assert.equal(stopPayload.wechatConfigured, false);

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        wechatBridgeUrl: `ws://127.0.0.1:${wechatServer.port}`,
        wechatBridgeToken: 'wechat-test-token'
      })
    });
    assert.equal(updateResponse.status, 200);

    const statusResponse = await fetch(`${controlApiUrl}/status`);
    assert.equal(statusResponse.status, 200);
    const statusPayload = await statusResponse.json();
    assert.equal(statusPayload.runtimeActive, false);
    assert.equal(statusPayload.wechatConfigured, true);
    assert.equal(statusPayload.wechatRuntimeActive, false);
    assert.equal(statusPayload.wechatBridgeConnected, false);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('wechat worker applies bot prefix updates through supervisor config push', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    'OPENAI_ADVANCED_API_KEY=test-key',
    'OPENAI_ADVANCED_MODEL=fake-advanced',
    `OPENAI_ADVANCED_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_ADVANCED_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial WeChat hot reload status. logs:\n${logs.join('')}`
      }
    );

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_before_prefix_reload',
      text: '/wx should stay ignored before reload'
    });
    await delay(500);
    assert.equal(wechatServer.sentGroupTexts.length, 0);

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        wechatBotPrefix: '/wx'
      })
    });
    assert.equal(updateResponse.status, 200);

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_prefix_reload',
      text: '/wx hello after reload'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.length === 1,
      {
        label: `WeChat reply after prefix reload. logs:\n${logs.join('')}`
      }
    );

    assert.equal(wechatServer.sentGroupTexts[0].chatId, 'wx_group_demo');
    assert.equal(wechatServer.sentGroupTexts[0].text, 'reply:hello after reload');

    const stopResponse = await fetch(`${controlApiUrl}/stop`, {
      method: 'POST'
    });
    assert.equal(stopResponse.status, 200);
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('control API tolerates repeated wechat config updates through supervisor config push', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial repeated WeChat config status. logs:\n${logs.join('')}`
      }
    );

    let configPayload = await (await fetch(`${controlApiUrl}/config`)).json();
    const prefixes = ['/wx', '/bot', '/go'];

    for (const prefix of prefixes) {
      const updateResponse = await fetch(`${controlApiUrl}/config`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          ...configPayload,
          wechatBotPrefix: prefix
        })
      });

      const updateText = await updateResponse.text();
      assert.equal(
        updateResponse.status,
        200,
        `Expected /config update for prefix ${prefix} to succeed. body=${updateText}`
      );
      configPayload = JSON.parse(updateText);

      assert.equal(configPayload.wechatBotPrefix, prefix);
    }

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_repeated_updates',
      text: '/go hello after repeated updates'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.some((message) => message.text === 'reply:hello after repeated updates'),
      {
        label: `WeChat reply after repeated config updates. logs:\n${logs.join('')}`
      }
    );
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor starts wechat worker when bridge config is enabled via control API update', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'WECHAT_BRIDGE_URL=',
    'WECHAT_BRIDGE_TOKEN=',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatRuntimeActive === false
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `initial supervisor status without wechat worker. logs:\n${logs.join('')}`
      }
    );

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        wechatBridgeUrl: `ws://127.0.0.1:${wechatServer.port}`,
        wechatBridgeToken: 'wechat-test-token'
      })
    });
    assert.equal(updateResponse.status, 200);

    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.wechatRuntimeActive === true && payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        timeoutMs: 10000,
        label: `wechat worker start after config update. logs:\n${logs.join('')}`
      }
    );

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_bridge_enable',
      text: '/ai hello after wechat enable'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.some((message) => message.text === 'reply:hello after wechat enable'),
      {
        label: `wechat reply after enabling bridge. logs:\n${logs.join('')}`
      }
    );
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('supervisor recovers disabled wechat worker after bridge config fix without manual start', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'WECHAT_BRIDGE_URL=http://127.0.0.1:1',
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      () =>
        logs.join('').includes('Wechat worker restart disabled after 3 consecutive boot failures.'),
      {
        timeoutMs: 20000,
        label: `WeChat disablement before config fix. logs:\n${logs.join('')}`
      }
    );

    const configResponse = await fetch(`${controlApiUrl}/config`);
    assert.equal(configResponse.status, 200);
    const configPayload = await configResponse.json();

    const updateResponse = await fetch(`${controlApiUrl}/config`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        ...configPayload,
        wechatBridgeUrl: `ws://127.0.0.1:${wechatServer.port}`,
        wechatBridgeToken: 'wechat-test-token'
      })
    });
    assert.equal(updateResponse.status, 200);

    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.wechatRuntimeActive === true && payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        timeoutMs: 15000,
        label: `WeChat recovery after config fix. logs:\n${logs.join('')}`
      }
    );

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_disablement_recovery',
      text: '/ai hello after disablement recovery'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.some((message) => message.text === 'reply:hello after disablement recovery'),
      {
        label: `WeChat reply after disablement recovery. logs:\n${logs.join('')}`
      }
    );
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('wechat worker reconnects to bridge after disconnect and continues processing messages', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const wechatServer = await createFakeWechatBridgeServer({
    token: 'wechat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    `WECHAT_BRIDGE_URL=ws://127.0.0.1:${wechatServer.port}`,
    'WECHAT_BRIDGE_TOKEN=wechat-test-token',
    'WECHAT_BOT_PREFIX=/ai',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true &&
            payload.napcatConnected === true &&
            payload.wechatRuntimeActive === true &&
            payload.wechatBridgeConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `wechat reconnect initial status. logs:\n${logs.join('')}`
      }
    );

    wechatServer.disconnectClients();

    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.wechatBridgeConnected === false ? payload : false;
        } catch {
          return false;
        }
      },
      {
        label: `wechat disconnect status. logs:\n${logs.join('')}`
      }
    );

    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);
          const payload = await response.json();
          return payload.wechatBridgeConnected === true ? payload : false;
        } catch {
          return false;
        }
      },
      {
        timeoutMs: 10000,
        label: `wechat reconnect status. logs:\n${logs.join('')}`
      }
    );

    wechatServer.emitGroupMessage({
      messageId: 'wx_msg_after_reconnect',
      text: '/ai hello after reconnect'
    });

    await waitFor(
      () => wechatServer.sentGroupTexts.some((message) => message.text === 'reply:hello after reconnect'),
      {
        label: `wechat reconnect reply. logs:\n${logs.join('')}`
      }
    );
  } finally {
    await stopChild(supervisor);
    await Promise.all([
      openAiServer.close(),
      napcatServer.close(),
      wechatServer.close()
    ]);
  }
});

test('runtime worker ignores bootstrap env timestamp changes after startup', async () => {
  const logs = [];
  const openAiServer = await createFakeOpenAiServer();
  const napcatServer = await createFakeNapCatServer({
    token: 'napcat-test-token'
  });
  const controlApiPort = await getFreePort();
  const workspace = await createTempWorkspace([
    'OPENAI_API_KEY=test-key',
    'OPENAI_DEFAULT_API_KEY=test-key',
    'OPENAI_DEFAULT_MODEL=fake-default',
    `OPENAI_DEFAULT_BASE_URL=http://127.0.0.1:${openAiServer.port}/v1`,
    'OPENAI_DEFAULT_API_STYLE=chat_completions',
    `NAPCAT_WS_URL=ws://127.0.0.1:${napcatServer.port}`,
    'NAPCAT_TOKEN=napcat-test-token',
    'BOT_PREFIX=/ai',
    'MAX_OUTPUT_CHARS=800'
  ]);
  const supervisor = spawnSupervisor({
    cwd: workspace,
    controlApiPort,
    logs
  });
  const controlApiUrl = `http://127.0.0.1:${controlApiPort}`;

  try {
    await waitFor(
      async () => {
        try {
          const response = await fetch(`${controlApiUrl}/status`);

          if (!response.ok) {
            return false;
          }

          const payload = await response.json();
          return payload.runtimeActive === true && payload.napcatConnected === true
            ? payload
            : false;
        } catch {
          return false;
        }
      },
      {
        label: `runtime worker initial status. logs:\n${logs.join('')}`
      }
    );

    const envPath = path.join(workspace, '.env');
    const logCountBeforeEnvTouch = logs.length;
    const bumpedTime = new Date(Date.now() + 2000);
    await fs.utimes(envPath, bumpedTime, bumpedTime);
    await delay(1000);

    const logsAfterEnvTouch = logs.slice(logCountBeforeEnvTouch).join('');
    assert.doesNotMatch(logsAfterEnvTouch, /Config reloaded from/);
    assert.doesNotMatch(logsAfterEnvTouch, /env-watch/);

    napcatServer.emitGroupMessage({
      messageId: 'qq_msg_after_env_touch',
      text: '/ai hello after env touch'
    });

    await waitFor(
      () => napcatServer.sentGroupMessages.some((message) => message.message === 'reply:hello after env touch'),
      {
        label: `runtime worker reply after env touch. logs:\n${logs.join('')}`
      }
    );
  } finally {
    await stopChild(supervisor);
    await Promise.all([openAiServer.close(), napcatServer.close()]);
  }
});
