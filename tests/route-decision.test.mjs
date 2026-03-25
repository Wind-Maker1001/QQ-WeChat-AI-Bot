import test from 'node:test';
import assert from 'node:assert/strict';

import {
  formatRouteDecisionReason,
  getRouteDecisionReasonGroups,
  getRouteDecisionRequestedCapabilities,
  getRouteDecisionTrigger,
  resolveRouteDecision
} from '../src/domain/route-decision.mjs';

function createDecision(userText, overrides = {}) {
  return resolveRouteDecision({
    userText,
    imageInputs: [],
    defaultRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    advancedRoute: {
      model: 'gpt-5.4',
      apiStyle: 'responses'
    },
    ...overrides
  });
}

test('complex requests boost thinking without requiring a manual advanced prefix', () => {
  const decision = createDecision(
    '请详细分析一下这个重构方案为什么会造成边界泄漏，并分步骤比较两种替代实现的优缺点。'
  );

  assert.equal(decision.route, 'default');
  assert.equal(decision.trigger.kind, 'default');
  assert.equal(decision.requestedCapabilities.reasoningEffort, 'high');
  assert.equal(decision.requestedCapabilities.textVerbosity, 'high');
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: [],
    capabilityReasons: ['complex'],
    upgradeReasons: []
  });
  assert.deepEqual(decision.decisionMetadata.reasonTags, ['complex']);
  assert.equal(formatRouteDecisionReason(decision), 'complex');
});

test('time-sensitive requests ask for web search automatically', () => {
  const decision = createDecision('帮我查一下 OpenAI 最新官方文档里对 Responses API 的建议。');

  assert.equal(decision.route, 'default');
  assert.equal(getRouteDecisionRequestedCapabilities(decision).enableWebSearch, true);
  assert.equal(getRouteDecisionRequestedCapabilities(decision).reasoningEffort, 'high');
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: [],
    capabilityReasons: ['web_search'],
    upgradeReasons: []
  });
  assert.deepEqual(decision.decisionMetadata.reasonTags, ['web_search']);
  assert.equal(formatRouteDecisionReason(decision), 'web_search');
});

test('data-analysis requests ask for code interpreter automatically', () => {
  const decision = createDecision('我有一个 CSV 表格，帮我做统计分析并画图。');

  assert.equal(decision.route, 'default');
  assert.equal(getRouteDecisionRequestedCapabilities(decision).enableCodeInterpreter, true);
  assert.equal(getRouteDecisionRequestedCapabilities(decision).reasoningEffort, 'high');
  assert.ok(getRouteDecisionReasonGroups(decision).capabilityReasons.includes('code_interpreter'));
  assert.ok(decision.decisionMetadata.reasonTags.includes('code_interpreter'));
  assert.match(formatRouteDecisionReason(decision), /code_interpreter/);
});

test('capability upgrade routes to advanced when default route cannot satisfy responses features', () => {
  const decision = resolveRouteDecision({
    userText: '请详细分析这个架构设计的取舍，并给出分步骤方案。',
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

  assert.equal(decision.route, 'advanced');
  assert.equal(getRouteDecisionRequestedCapabilities(decision).reasoningEffort, 'high');
  assert.equal(getRouteDecisionRequestedCapabilities(decision).enableWebSearch, false);
  assert.equal(getRouteDecisionRequestedCapabilities(decision).enableCodeInterpreter, false);
  assert.deepEqual(getRouteDecisionReasonGroups(decision), {
    triggerReasons: [],
    capabilityReasons: ['complex'],
    upgradeReasons: ['capability_upgrade']
  });
  assert.deepEqual(decision.decisionMetadata.reasonTags, ['complex', 'capability_upgrade']);
  assert.equal(formatRouteDecisionReason(decision), 'complex+capability_upgrade');
});

test('directive and image triggers are exposed as structured trigger metadata', () => {
  const directiveDecision = createDecision('/vision analyze this');
  const imageDecision = resolveRouteDecision({
    userText: 'look at this',
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

  assert.deepEqual(getRouteDecisionTrigger(directiveDecision), {
    kind: 'directive',
    matchedPrefix: '/vision'
  });
  assert.deepEqual(getRouteDecisionReasonGroups(directiveDecision), {
    triggerReasons: ['directive:/vision'],
    capabilityReasons: ['complex'],
    upgradeReasons: []
  });
  assert.deepEqual(getRouteDecisionTrigger(imageDecision), {
    kind: 'image',
    matchedPrefix: ''
  });
  assert.deepEqual(getRouteDecisionReasonGroups(imageDecision), {
    triggerReasons: ['image'],
    capabilityReasons: [],
    upgradeReasons: []
  });
});
