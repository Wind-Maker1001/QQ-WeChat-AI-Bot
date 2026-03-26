import test from 'node:test';
import assert from 'node:assert/strict';

import { createLlmRouter } from '../src/adapters/llm/llm-router.mjs';

test('LLM router describes effective tools for responses requests', () => {
  const router = createLlmRouter({
    defaultRoute: {
      apiKey: 'default-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: false,
      enableCodeInterpreter: false
    },
    advancedRoute: {
      apiKey: 'advanced-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    }
  });

  const description = router.describeRequest({
    route: 'default',
    enableWebSearchOverride: true,
    enableCodeInterpreterOverride: false
  });

  assert.equal(description.route, 'default');
  assert.equal(description.effectiveApiStyle, 'responses');
  assert.deepEqual(description.configuredTools, []);
  assert.deepEqual(description.effectiveTools, ['web_search']);
  assert.equal(description.effectiveEnableWebSearch, true);
  assert.equal(description.effectiveEnableCodeInterpreter, false);
});

test('LLM router suppresses unavailable tools on chat-completions routes', () => {
  const router = createLlmRouter({
    defaultRoute: {
      apiKey: 'default-key',
      model: 'deepseek-chat',
      apiStyle: 'chat_completions',
      enableWebSearch: true,
      enableCodeInterpreter: true
    },
    advancedRoute: {
      apiKey: 'advanced-key',
      model: 'gpt-5.4',
      apiStyle: 'responses',
      enableWebSearch: true,
      enableCodeInterpreter: true
    }
  });

  const description = router.describeRequest({
    route: 'default'
  });

  assert.equal(description.configuredApiStyle, 'chat_completions');
  assert.equal(description.effectiveApiStyle, 'chat_completions');
  assert.deepEqual(description.configuredTools, []);
  assert.deepEqual(description.effectiveTools, []);
  assert.equal(description.effectiveEnableWebSearch, false);
  assert.equal(description.effectiveEnableCodeInterpreter, false);
});
