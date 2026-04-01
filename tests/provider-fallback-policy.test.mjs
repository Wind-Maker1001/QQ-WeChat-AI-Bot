import test from 'node:test';
import assert from 'node:assert/strict';

import {
  buildProviderFallbackReply,
  createProviderFallbackDecision,
  isTransientProviderError
} from '../src/domain/provider-fallback-policy.mjs';
import {
  createExecutionProjection,
  EXECUTION_KIND_DIRECT,
  EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK,
  EXECUTION_STAGE_DIRECT
} from '../src/domain/execution-projection.mjs';

test('provider fallback policy classifies transport failures as transient', () => {
  assert.equal(isTransientProviderError({ code: 'ECONNRESET' }), true);
  assert.equal(isTransientProviderError({ status: 502 }), true);
  assert.equal(isTransientProviderError({ status: 401 }), false);
});

test('provider fallback decision only allows plain text transient fallback requests', () => {
  const allowed = createProviderFallbackDecision({
    request: {
      userText: 'hello',
      imageInputs: []
    },
    primaryToolSupport: {
      requestedTools: {
        requested: [],
        required: []
      },
      effectiveHostedTools: [],
      hasSuppressedRequiredTools: false
    },
    fallbackToolSupport: {
      effectiveTools: []
    },
    deepseekProvider: {},
    primaryError: { status: 502 }
  });
  const blocked = createProviderFallbackDecision({
    request: {
      userText: 'hello',
      imageInputs: []
    },
    primaryToolSupport: {
      requestedTools: {
        requested: ['web_search'],
        required: ['web_search']
      },
      effectiveHostedTools: ['web_search'],
      hasSuppressedRequiredTools: false
    },
    fallbackToolSupport: {
      effectiveTools: []
    },
    deepseekProvider: {},
    primaryError: { status: 502 }
  });

  assert.deepEqual(allowed, {
    shouldFallback: true,
    reason: 'deepseek-fallback-eligible'
  });
  assert.deepEqual(blocked, {
    shouldFallback: false,
    reason: 'fallback-cannot-preserve-required-tools'
  });
});

test('provider fallback reply rewrites conversation continuity and marks degraded execution', () => {
  const reply = buildProviderFallbackReply({
    primaryRoutePolicy: {
      routeName: 'default',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      reasoningEffort: 'high',
      textVerbosity: 'high',
      enableWebSearch: false,
      enableCodeInterpreter: false
    },
    fallbackReply: {
      text: 'fallback reply',
      model: 'deepseek-chat',
      effectiveApiStyle: 'chat_completions',
      conversationDelta: {
        previousResponseId: 'resp_old',
        sharedMessages: [],
        clearPreviousResponseId: false
      }
    }
  });

  assert.equal(reply.route, 'default');
  assert.equal(reply.conversationDelta.previousResponseId, null);
  assert.equal(reply.conversationDelta.clearPreviousResponseId, true);
  assert.deepEqual(
    reply.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_DIRECT,
      completedStages: [EXECUTION_STAGE_DIRECT],
      degraded: true,
      recoveries: [EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK]
    })
  );
});
