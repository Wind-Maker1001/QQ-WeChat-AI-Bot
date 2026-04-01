import test from 'node:test';
import assert from 'node:assert/strict';

import {
  TOOL_KIND_CODE_INTERPRETER,
  TOOL_KIND_LOCAL_RUNTIME_STATE,
  TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
  TOOL_KIND_WEB_SEARCH,
  createToolSelection,
  formatSuppressedTools,
  formatToolLabel,
  getToolDescriptor,
  listToolDescriptors,
  normalizeToolKinds
} from '../src/domain/tool-registry.mjs';

test('tool registry exposes stable descriptors for all supported tools', () => {
  const kinds = listToolDescriptors().map((descriptor) => descriptor.kind);

  assert.deepEqual(kinds, [
    TOOL_KIND_WEB_SEARCH,
    TOOL_KIND_CODE_INTERPRETER,
    TOOL_KIND_LOCAL_RUNTIME_STATE,
    TOOL_KIND_LOCAL_SNAPSHOT_INSPECT
  ]);
  assert.equal(getToolDescriptor(TOOL_KIND_WEB_SEARCH)?.requiresResponsesApi, true);
  assert.equal(getToolDescriptor(TOOL_KIND_LOCAL_RUNTIME_STATE)?.availableDuringFallback, true);
});

test('tool registry normalizes tool selections and labels', () => {
  assert.deepEqual(normalizeToolKinds([' web_search ', 'web_search', 'missing']), ['web_search']);
  assert.deepEqual(
    createToolSelection({
      requested: ['web_search', 'code_interpreter'],
      required: ['code_interpreter', 'missing']
    }),
    {
      requested: ['web_search', 'code_interpreter'],
      required: ['code_interpreter']
    }
  );
  assert.equal(formatToolLabel('code_interpreter'), 'code_interpreter (Code Interpreter)');
  assert.equal(
    formatSuppressedTools([
      {
        toolKind: 'web_search',
        reason: 'requires_responses_api'
      }
    ]),
    'web_search (Web Search) (requires_responses_api)'
  );
});
