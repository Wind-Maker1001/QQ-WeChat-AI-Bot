import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import { createToolExecutor } from '../src/application/tool-executor.mjs';

test('tool executor returns local runtime state summaries from request descriptors', async () => {
  const executor = createToolExecutor();
  const result = await executor.executeTool({
    toolKind: 'local_runtime_state',
    requestDescriptor: {
      route: 'advanced',
      effectiveApiStyle: 'responses',
      model: 'gpt-5.4',
      effectiveTools: ['web_search'],
      suppressedTools: [
        {
          toolKind: 'code_interpreter',
          reason: 'requires_responses_api'
        }
      ]
    }
  });

  assert.match(result.text, /Current route: advanced/);
  assert.match(result.text, /web_search/);
  assert.match(result.text, /code_interpreter/);
});

test('tool executor lists latest local snapshots on disk', async () => {
  const cwd = await fs.mkdtemp(path.join(os.tmpdir(), 'qq-ai-bot-tool-executor-'));
  const snapshotRoot = path.join(cwd, 'artifacts', 'state-snapshots');
  await fs.mkdir(snapshotRoot, { recursive: true });
  await fs.writeFile(path.join(snapshotRoot, 'latest.zip'), 'zip');

  const executor = createToolExecutor({ cwd });
  const result = await executor.executeTool({
    toolKind: 'local_snapshot_inspect'
  });

  assert.match(result.text, /latest.zip/);
  assert.match(result.text, /Latest local state snapshots on disk/);
});
