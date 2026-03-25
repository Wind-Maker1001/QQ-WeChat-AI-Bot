import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { createSessionStore } from '../src/session.mjs';
import { buildConversationId } from '../src/domain/conversation-state.mjs';

test('session store removes stale lock file before saving conversations', async () => {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-session-lock-'));
  const dataDirPath = path.join(cwd, 'data');
  const sessionFilePath = path.join(dataDirPath, 'sessions.json');
  const lockFilePath = `${sessionFilePath}.lock`;

  await fs.mkdir(dataDirPath, { recursive: true });
  await fs.writeFile(sessionFilePath, '{}\n', 'utf8');
  await fs.writeFile(lockFilePath, '', 'utf8');
  const staleDate = new Date(Date.now() - 60_000);
  await fs.utimes(lockFilePath, staleDate, staleDate);

  const store = await createSessionStore({
    cwd,
    dataDirPath,
    sessionFilePath
  });
  const conversationId = buildConversationId({
    channelId: 'qq',
    chatId: 'chat-demo',
    userId: 'user-demo'
  });
  const conversation = store.getConversation(conversationId);

  conversation.routes.default.messages = [
    {
      role: 'user',
      content: 'hello'
    }
  ];

  await store.setConversation(conversationId, conversation);

  const savedText = await fs.readFile(sessionFilePath, 'utf8');
  const savedPayload = JSON.parse(savedText);

  assert.ok(savedPayload[conversationId]);
  await assert.rejects(fs.access(lockFilePath));
});
