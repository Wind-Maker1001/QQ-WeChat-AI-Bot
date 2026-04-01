import test from 'node:test';
import assert from 'node:assert/strict';

import {
  TOOL_SUPPRESSION_REASON_CONTEXT_UNAVAILABLE,
  TOOL_SUPPRESSION_REASON_FALLBACK_UNSUPPORTED,
  TOOL_SUPPRESSION_REASON_IMAGE_UNSUPPORTED,
  TOOL_SUPPRESSION_REASON_RESPONSES_API_REQUIRED,
  createProviderToolSupport
} from '../src/domain/provider-capability-matrix.mjs';

test('provider capability matrix suppresses hosted tools when responses api is unavailable', () => {
  const support = createProviderToolSupport({
    providerName: 'default',
    apiStyle: 'chat_completions',
    routeToolPolicy: {
      enabledTools: ['web_search', 'code_interpreter']
    },
    requestedTools: {
      requested: ['web_search', 'code_interpreter'],
      required: []
    }
  });

  assert.deepEqual(support.effectiveTools, []);
  assert.deepEqual(
    support.suppressedTools.map((suppressedTool) => suppressedTool.reason),
    [TOOL_SUPPRESSION_REASON_RESPONSES_API_REQUIRED, TOOL_SUPPRESSION_REASON_RESPONSES_API_REQUIRED]
  );
});

test('provider capability matrix suppresses image-incompatible and fallback-incompatible tools', () => {
  const imageSupport = createProviderToolSupport({
    providerName: 'default',
    apiStyle: 'responses',
    routeToolPolicy: {
      enabledTools: ['web_search']
    },
    requestedTools: {
      requested: ['web_search'],
      required: []
    },
    imageCount: 1
  });
  const fallbackSupport = createProviderToolSupport({
    providerName: 'deepseek-fallback',
    apiStyle: 'chat_completions',
    routeToolPolicy: {
      enabledTools: ['web_search']
    },
    requestedTools: {
      requested: ['web_search'],
      required: []
    },
    isFallback: true
  });

  assert.equal(imageSupport.suppressedTools[0].reason, TOOL_SUPPRESSION_REASON_IMAGE_UNSUPPORTED);
  assert.equal(fallbackSupport.suppressedTools[0].reason, TOOL_SUPPRESSION_REASON_FALLBACK_UNSUPPORTED);
});

test('provider capability matrix allows local tools when context exists', () => {
  const support = createProviderToolSupport({
    providerName: 'default',
    apiStyle: 'responses',
    routeToolPolicy: {
      enabledTools: []
    },
    requestedTools: {
      requested: ['local_runtime_state', 'local_snapshot_inspect'],
      required: ['local_runtime_state']
    },
    snapshotInspectorAvailable: false
  });

  assert.deepEqual(support.effectiveTools, ['local_runtime_state']);
  assert.equal(support.effectiveLocalTools.includes('local_runtime_state'), true);
  assert.equal(support.suppressedTools[0].reason, TOOL_SUPPRESSION_REASON_CONTEXT_UNAVAILABLE);
});
