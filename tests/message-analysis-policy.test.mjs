import test from 'node:test';
import assert from 'node:assert/strict';

import {
  analyzeMessageIntent,
  normalizeTriggerPrefixes,
  shouldBoostThinking,
  shouldUseCodeInterpreter,
  shouldUseWebSearch
} from '../src/domain/message-analysis-policy.mjs';

test('message analysis detects complex reasoning requests', () => {
  assert.equal(
    shouldBoostThinking('Analyze the architecture trade-offs and explain the root cause step by step.'),
    true
  );
});

test('message analysis detects web search and code interpreter triggers', () => {
  assert.equal(shouldUseWebSearch('What is the latest official documentation?'), true);
  assert.equal(shouldUseCodeInterpreter('Use python to analyze this csv dataset.'), true);
});

test('message analysis normalizes trigger prefixes by trimming and sorting longest first', () => {
  assert.deepEqual(normalizeTriggerPrefixes([' /gpt ', '/vision', '/g']), ['/vision', '/gpt', '/g']);
});

test('message analysis produces directive and image triggers with normalized text', () => {
  const directiveIntent = analyzeMessageIntent({
    userText: '/vision analyze this image',
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
  const imageIntent = analyzeMessageIntent({
    userText: '',
    imageInputs: [{ imageUrl: 'data:image/png;base64,abc' }],
    defaultRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    advancedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    }
  });

  assert.deepEqual(directiveIntent.trigger, {
    kind: 'directive',
    matchedPrefix: '/vision'
  });
  assert.equal(directiveIntent.normalizedText, 'analyze this image');
  assert.deepEqual(imageIntent.trigger, {
    kind: 'image',
    matchedPrefix: ''
  });
  assert.equal(imageIntent.routeHint, 'advanced');
  assert.equal(imageIntent.normalizedText.length > 0, true);
});

test('message analysis upgrades to advanced when only the advanced route can satisfy responses features', () => {
  const intent = analyzeMessageIntent({
    userText: 'Compare the architecture trade-offs in detail.',
    imageInputs: [],
    defaultRoute: {
      model: 'legacy-model',
      apiStyle: 'chat_completions'
    },
    advancedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    }
  });

  assert.equal(intent.routeHint, 'advanced');
  assert.equal(intent.capabilityUpgradeApplied, true);
  assert.equal(intent.requestedCapabilities.reasoningEffort, 'high');
  assert.equal(intent.requestedCapabilities.enableWebSearch, false);
  assert.equal(intent.requestedCapabilities.enableCodeInterpreter, false);
});
