import test from 'node:test';
import assert from 'node:assert/strict';

import {
  API_STYLE_CHAT_COMPLETIONS,
  API_STYLE_RESPONSES,
  normalizeApiStyle,
  resolveEffectiveRequestPolicy,
  resolveRouteRequestPolicy
} from '../src/domain/llm-request-policy.mjs';

test('request policy infers DeepSeek default route as chat completions', () => {
  const routePolicy = resolveRouteRequestPolicy({
    routeName: 'default',
    model: 'deepseek-chat',
    baseURL: 'https://api.deepseek.com',
    apiStyle: ''
  });

  assert.equal(routePolicy.apiStyle, API_STYLE_CHAT_COMPLETIONS);
});

test('request policy applies GPT-5 defaults for responses routes', () => {
  const defaultPolicy = resolveRouteRequestPolicy({
    routeName: 'default',
    model: 'gpt-5.4',
    apiStyle: 'responses'
  });
  const advancedPolicy = resolveRouteRequestPolicy({
    routeName: 'advanced',
    model: 'gpt-5.4',
    apiStyle: 'responses'
  });

  assert.equal(defaultPolicy.reasoningEffort, 'medium');
  assert.equal(defaultPolicy.textVerbosity, 'medium');
  assert.equal(advancedPolicy.reasoningEffort, 'high');
  assert.equal(advancedPolicy.textVerbosity, 'high');
});

test('request policy resolves explicit tool and verbosity overrides per request', () => {
  const routePolicy = resolveRouteRequestPolicy({
    routeName: 'default',
    model: 'gpt-5.4',
    apiStyle: 'responses',
    reasoningEffort: 'medium',
    textVerbosity: 'medium',
    enableWebSearch: false,
    enableCodeInterpreter: false
  });

  const effectivePolicy = resolveEffectiveRequestPolicy({
    routePolicy,
    reasoningEffortOverride: 'high',
    textVerbosityOverride: 'high',
    enableWebSearchOverride: true,
    enableCodeInterpreterOverride: true
  });

  assert.equal(effectivePolicy.apiStyle, API_STYLE_RESPONSES);
  assert.equal(effectivePolicy.reasoningEffort, 'high');
  assert.equal(effectivePolicy.textVerbosity, 'high');
  assert.equal(effectivePolicy.enableWebSearch, true);
  assert.equal(effectivePolicy.enableCodeInterpreter, true);
});

test('request policy normalizes api style aliases', () => {
  assert.equal(normalizeApiStyle('response'), API_STYLE_RESPONSES);
  assert.equal(normalizeApiStyle('chat-completions'), API_STYLE_CHAT_COMPLETIONS);
});
