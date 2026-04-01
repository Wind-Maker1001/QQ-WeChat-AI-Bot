import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { ensureRuntimeSettings } from '../src/adapters/config/runtime-settings-store.mjs';

async function createTempWorkspace() {
  return fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-runtime-settings-'));
}

test('ensureRuntimeSettings creates a v2 snapshot from env-only installs', async () => {
  const cwd = await createTempWorkspace();
  await fs.writeFile(
    path.join(cwd, '.env'),
    [
      'OPENAI_API_KEY=shared-key',
      'OPENAI_DEFAULT_MODEL=gpt-5.4',
      'OPENAI_DEFAULT_API_STYLE=responses',
      'OPENAI_ADVANCED_API_STYLE=chat_completions',
      'NAPCAT_TOKEN=test-token'
    ].join('\n') + '\n',
    'utf8'
  );

  const result = await ensureRuntimeSettings({ cwd });
  const snapshot = JSON.parse(await fs.readFile(path.join(cwd, 'data', 'runtime-settings.json'), 'utf8'));

  assert.equal(result.runtimeSettingsSnapshot.version, 2);
  assert.equal(snapshot.version, 2);
  assert.deepEqual(snapshot.apiStyles, {
    default: 'responses',
    advanced: 'chat_completions'
  });
  assert.deepEqual(snapshot.toolPolicies, {
    default: {
      enabledTools: []
    },
    advanced: {
      enabledTools: []
    }
  });
  assert.equal(snapshot.settings.openAiApiKey, 'shared-key');
  assert.equal(snapshot.settings.napCatToken, 'test-token');
});

test('ensureRuntimeSettings migrates hiddenSettings v1 snapshots to apiStyles', async () => {
  const cwd = await createTempWorkspace();
  await fs.writeFile(
    path.join(cwd, '.env'),
    'NAPCAT_TOKEN=test-token\n',
    'utf8'
  );
  await fs.mkdir(path.join(cwd, 'data'), { recursive: true });
  await fs.writeFile(
    path.join(cwd, 'data', 'runtime-settings.json'),
    JSON.stringify(
      {
        version: 1,
        savedAt: '2026-03-31T00:00:00.000Z',
        settings: {
          openAiApiKey: 'snapshot-key',
          napCatToken: 'snapshot-token'
        },
        hiddenSettings: {
          openAiDefaultApiStyle: 'chat_completions',
          openAiAdvancedApiStyle: 'responses'
        }
      },
      null,
      2
    ) + '\n',
    'utf8'
  );

  const result = await ensureRuntimeSettings({ cwd });
  const snapshot = JSON.parse(await fs.readFile(path.join(cwd, 'data', 'runtime-settings.json'), 'utf8'));

  assert.equal(result.runtimeSettingsSnapshot.version, 2);
  assert.deepEqual(snapshot.apiStyles, {
    default: 'chat_completions',
    advanced: 'responses'
  });
  assert.deepEqual(snapshot.toolPolicies, {
    default: {
      enabledTools: []
    },
    advanced: {
      enabledTools: []
    }
  });
  assert.equal('hiddenSettings' in snapshot, false);
  assert.equal(result.runtimeConfig.openai.defaultRoute.apiStyle, 'chat_completions');
  assert.equal(result.runtimeConfig.openai.advancedRoute.apiStyle, 'responses');
});

test('ensureRuntimeSettings ignores conflicting env runtime keys when v2 settings already exist', async () => {
  const cwd = await createTempWorkspace();
  await fs.writeFile(
    path.join(cwd, '.env'),
    [
      'OPENAI_API_KEY=env-key',
      'BOT_PREFIX=/env',
      'QQ_AI_BOT_CONTROL_API_TOKEN=local-token',
      'NAPCAT_TOKEN=env-napcat-token'
    ].join('\n') + '\n',
    'utf8'
  );
  await fs.mkdir(path.join(cwd, 'data'), { recursive: true });
  await fs.writeFile(
    path.join(cwd, 'data', 'runtime-settings.json'),
    JSON.stringify(
      {
        version: 2,
        savedAt: '2026-03-31T00:00:00.000Z',
        settings: {
          openAiApiKey: 'settings-key',
          napCatToken: 'settings-napcat-token',
          botPrefix: '/settings'
        },
        apiStyles: {
          default: 'responses',
          advanced: 'responses'
        }
      },
      null,
      2
    ) + '\n',
    'utf8'
  );

  const result = await ensureRuntimeSettings({ cwd });

  assert.equal(result.runtimeConfig.openai.advancedRoute.apiKey, 'settings-key');
  assert.equal(result.runtimeConfig.napcat.token, 'settings-napcat-token');
  assert.equal(result.runtimeConfig.bot.prefix, '/settings');
  assert.equal(result.bootstrapEnvValues.QQ_AI_BOT_CONTROL_API_TOKEN, 'local-token');
});
