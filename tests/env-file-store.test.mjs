import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { readEnvFileValues, updateEnvFileValues } from '../src/adapters/config/env-file-store.mjs';

async function createTempEnv(lines) {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-env-store-'));
  await fs.writeFile(path.join(cwd, '.env'), `${lines.join('\n')}\n`, 'utf8');
  return cwd;
}

test('readEnvFileValues migrates legacy allowed group ids and decodes env values', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'BOT_SYSTEM_PROMPT=base1\\nbase2',
    'BOT_PERSONA=line1\\nline2',
    'ALLOWED_GROUP_IDS=legacy-a,legacy-b'
  ]);

  const result = await readEnvFileValues({ cwd });
  const envText = await fs.readFile(path.join(cwd, '.env'), 'utf8');

  assert.equal(result.values.BOT_SYSTEM_PROMPT, 'base1\nbase2');
  assert.equal(result.values.BOT_PERSONA, 'line1\nline2');
  assert.equal(result.values.ALLOWED_CHAT_IDS, 'legacy-a,legacy-b');
  assert.match(envText, /ALLOWED_CHAT_IDS=legacy-a,legacy-b/);
  assert.doesNotMatch(envText, /ALLOWED_GROUP_IDS=/);
});

test('updateEnvFileValues preserves unknown keys and encodes multiline bot prompt fields', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'CUSTOM_FLAG=enabled'
  ]);

  const result = await updateEnvFileValues({
    cwd,
    mapValues: (existingValues) => ({
      ...existingValues,
      BOT_SYSTEM_PROMPT: 'base1\nbase2',
      BOT_PERSONA: 'line1\nline2',
      ALLOWED_CHAT_IDS: 'chat-a,chat-b'
    })
  });
  const envText = await fs.readFile(path.join(cwd, '.env'), 'utf8');

  assert.equal(result.nextValues.CUSTOM_FLAG, 'enabled');
  assert.equal(result.nextValues.BOT_SYSTEM_PROMPT, 'base1\nbase2');
  assert.equal(result.nextValues.BOT_PERSONA, 'line1\nline2');
  assert.match(envText, /CUSTOM_FLAG=enabled/);
  assert.match(envText, /BOT_SYSTEM_PROMPT=base1\\nbase2/);
  assert.match(envText, /BOT_PERSONA=line1\\nline2/);
  assert.match(envText, /ALLOWED_CHAT_IDS=chat-a,chat-b/);
});

test('readEnvFileValues re-reads under lock before writing legacy migration', async () => {
  const cwd = await createTempEnv([
    'OPENAI_API_KEY=test-key',
    'NAPCAT_TOKEN=test-token',
    'ALLOWED_GROUP_IDS=legacy-a,legacy-b'
  ]);
  const envPath = path.join(cwd, '.env');
  const lockPath = `${envPath}.lock`;

  await fs.writeFile(
    lockPath,
    JSON.stringify({
      pid: process.pid,
      acquiredAt: new Date().toISOString()
    }),
    'utf8'
  );

  const pendingRead = readEnvFileValues({ cwd });

  await new Promise((resolve) => setTimeout(resolve, 50));
  await fs.writeFile(
    envPath,
    [
      'OPENAI_API_KEY=test-key',
      'NAPCAT_TOKEN=test-token',
      'ALLOWED_CHAT_IDS=new-a,new-b',
      'CUSTOM_FLAG=preserved'
    ].join('\n') + '\n',
    'utf8'
  );
  await fs.rm(lockPath, { force: true });

  const result = await pendingRead;
  const envText = await fs.readFile(envPath, 'utf8');

  assert.equal(result.values.ALLOWED_CHAT_IDS, 'new-a,new-b');
  assert.equal(result.values.CUSTOM_FLAG, 'preserved');
  assert.match(envText, /ALLOWED_CHAT_IDS=new-a,new-b/);
  assert.match(envText, /CUSTOM_FLAG=preserved/);
  assert.doesNotMatch(envText, /ALLOWED_GROUP_IDS=/);
});
