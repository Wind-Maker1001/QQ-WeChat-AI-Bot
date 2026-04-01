import test from 'node:test';
import assert from 'node:assert/strict';

import { analyzeMessageIntent } from '../src/domain/message-analysis-policy.mjs';
import { getRouteDecisionRequestedTools, resolveRouteDecision } from '../src/domain/route-decision.mjs';

test('tool selection policy derives hosted tools from message analysis', () => {
  const intent = analyzeMessageIntent({
    userText: 'Use python to analyze this csv dataset and search the latest official documentation.',
    imageInputs: [],
    defaultRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    advancedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    }
  });

  assert.deepEqual(intent.requestedTools, {
    requested: ['web_search', 'code_interpreter'],
    required: []
  });
});

test('tool selection policy marks local runtime state and snapshot inspect as required local tools', () => {
  const routeDecision = resolveRouteDecision({
    userText: 'Show the current runtime status and snapshot restore impact.',
    imageInputs: [],
    defaultRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    advancedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    }
  });

  assert.deepEqual(getRouteDecisionRequestedTools(routeDecision), {
    requested: ['local_runtime_state', 'local_snapshot_inspect'],
    required: ['local_runtime_state', 'local_snapshot_inspect']
  });
});
