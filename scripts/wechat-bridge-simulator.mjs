import http from 'node:http';
import { randomUUID } from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';

import WebSocket, { WebSocketServer } from 'ws';

const host = process.env.WECHAT_BRIDGE_SIM_HOST || '127.0.0.1';
const port = Number.parseInt(process.env.WECHAT_BRIDGE_SIM_PORT || '3198', 10);
const bridgeToken = process.env.WECHAT_BRIDGE_TOKEN || '';
const actionLogPath = path.resolve(
  process.cwd(),
  process.env.WECHAT_BRIDGE_SIM_ACTION_LOG || 'wechat-actions.json'
);

const state = {
  clients: new Set(),
  messages: new Map(),
  actions: []
};

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

async function persistActionLog() {
  await fs.writeFile(
    actionLogPath,
    `${JSON.stringify(state.actions, null, 2)}\n`,
    'utf8'
  );
}

async function readJsonBody(req) {
  const chunks = [];

  for await (const chunk of req) {
    chunks.push(chunk);
  }

  const raw = Buffer.concat(chunks).toString('utf8').trim();

  if (!raw) {
    return {};
  }

  return JSON.parse(raw);
}

function normalizeString(value, fallback = '') {
  return typeof value === 'string' && value.trim() ? value.trim() : fallback;
}

function normalizeReplyIds(value) {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .filter((item) => typeof item === 'string' || typeof item === 'number')
    .map((item) => String(item).trim())
    .filter(Boolean);
}

function normalizeImages(images) {
  if (!Array.isArray(images)) {
    return [];
  }

  return images
    .filter((image) => image && typeof image === 'object')
    .map((image) => ({
      imageUrl: normalizeString(image.imageUrl),
      fileId: normalizeString(image.fileId)
    }))
    .filter((image) => image.imageUrl || image.fileId);
}

function buildTestMessage({
  chatId = 'wx_group_demo',
  userId = 'wx_user_demo',
  selfId = 'wx_self_demo',
  text = '/ai hello from wechat bridge simulator',
  rawText = text,
  mentioned = false,
  replyToMessageIds = [],
  images = [],
  rawPayload = {}
} = {}) {
  const normalizedText = normalizeString(text, '/ai hello from wechat bridge simulator');
  const messageId = `wx_msg_${randomUUID()}`;

  const packet = {
    type: 'group_message',
    chatId: normalizeString(chatId, 'wx_group_demo'),
    userId: normalizeString(userId, 'wx_user_demo'),
    selfId: normalizeString(selfId, 'wx_self_demo'),
    messageId,
    text: normalizedText,
    rawText: normalizeString(rawText, normalizedText),
    mentioned: mentioned === true,
    replyToMessageIds: normalizeReplyIds(replyToMessageIds),
    images: normalizeImages(images),
    timestamp: Date.now(),
    rawPayload: {
      source: 'wechat-bridge-simulator',
      ...rawPayload
    }
  };

  state.messages.set(messageId, packet);
  return packet;
}

function sendJson(ws, payload) {
  ws.send(JSON.stringify(payload));
}

function broadcast(payload) {
  for (const client of state.clients) {
    if (client.readyState === WebSocket.OPEN) {
      sendJson(client, payload);
    }
  }
}

function handleAction(ws, packet) {
  const requestId = typeof packet.requestId === 'string' ? packet.requestId : '';
  const action = typeof packet.action === 'string' ? packet.action : '';
  const params = packet.params && typeof packet.params === 'object' ? packet.params : {};
  state.actions.push({
    id: randomUUID(),
    requestId,
    action,
    params,
    timestamp: new Date().toISOString()
  });
  void persistActionLog();

  function ok(data = {}) {
    sendJson(ws, {
      type: 'action_result',
      requestId,
      ok: true,
      data
    });
  }

  function fail(error) {
    sendJson(ws, {
      type: 'action_result',
      requestId,
      ok: false,
      error
    });
  }

  if (!requestId || !action) {
    fail('invalid action packet');
    return;
  }

  switch (action) {
    case 'send_group_text': {
      logInfo(`[wechat-bridge-sim] send_group_text chatId=${params.chatId} text=${params.text}`);
      ok({
        messageId: `wx_reply_${randomUUID()}`
      });
      return;
    }
    case 'send_group_image': {
      logInfo(
        `[wechat-bridge-sim] send_group_image chatId=${params.chatId} imagePath=${params.imagePath}`
      );
      ok({
        messageId: `wx_reply_${randomUUID()}`
      });
      return;
    }
    case 'get_message': {
      const message = state.messages.get(String(params.messageId || ''));
      if (!message) {
        fail('message not found');
        return;
      }

      ok(message);
      return;
    }
    case 'download_image': {
      ok({
        dataUrl:
          'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9s5lMP8AAAAASUVORK5CYII='
      });
      return;
    }
    default:
      fail(`unsupported action: ${action}`);
  }
}

const server = http.createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/emit-test-message') {
    void (async () => {
      try {
        const payload = await readJsonBody(req);
        const packet = buildTestMessage(payload);
        broadcast(packet);
        res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(
          JSON.stringify({
            ok: true,
            messageId: packet.messageId,
            packet
          })
        );
      } catch (error) {
        res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(
          JSON.stringify({
            ok: false,
            error: error instanceof Error ? error.message : String(error)
          })
        );
      }
    })();
    return;
  }

  if (req.method === 'GET' && req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ ok: true, clients: state.clients.size }));
    return;
  }

  if (req.method === 'GET' && req.url === '/actions') {
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({ ok: true, actions: state.actions }));
    return;
  }

  res.writeHead(404, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify({ ok: false, error: 'not found' }));
});

const wss = new WebSocketServer({ server });

wss.on('connection', (ws, req) => {
  if (bridgeToken) {
    const authorization = req.headers.authorization || '';
    if (authorization !== `Bearer ${bridgeToken}`) {
      ws.close(1008, 'unauthorized');
      return;
    }
  }

  state.clients.add(ws);
  logInfo(`[wechat-bridge-sim] client connected. clients=${state.clients.size}`);

  ws.on('message', (data) => {
    let packet = null;
    try {
      packet = JSON.parse(data.toString('utf8'));
    } catch {
      sendJson(ws, {
        type: 'action_result',
        requestId: '',
        ok: false,
        error: 'invalid json'
      });
      return;
    }

    if (packet.type === 'action') {
      handleAction(ws, packet);
    }
  });

  ws.on('close', () => {
    state.clients.delete(ws);
    logInfo(`[wechat-bridge-sim] client disconnected. clients=${state.clients.size}`);
  });
});

await persistActionLog();

server.listen(port, host, () => {
  logInfo(`[wechat-bridge-sim] listening on ws://${host}:${port}`);
  logInfo(`[wechat-bridge-sim] http control available at http://${host}:${port}`);
  logInfo(`[wechat-bridge-sim] action log file: ${actionLogPath}`);
});

process.on('SIGINT', () => {
  server.close(() => process.exit(0));
});
process.on('SIGTERM', () => {
  server.close(() => process.exit(0));
});
