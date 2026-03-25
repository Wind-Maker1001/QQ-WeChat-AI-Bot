import test from 'node:test';
import assert from 'node:assert/strict';

import { resolveRouteDecision } from '../src/domain/route-decision.mjs';

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
  assert.equal(decision.requestedReasoningEffort, 'high');
  assert.equal(decision.requestedTextVerbosity, 'high');
  assert.match(decision.reason, /complex/);
});

test('time-sensitive requests ask for web search automatically', () => {
  const decision = createDecision('帮我查一下 OpenAI 最新官方文档里对 Responses API 的建议。');

  assert.equal(decision.route, 'default');
  assert.equal(decision.requestedEnableWebSearch, true);
  assert.equal(decision.requestedReasoningEffort, 'high');
  assert.match(decision.reason, /web_search/);
});

test('data-analysis requests ask for code interpreter automatically', () => {
  const decision = createDecision('我有一个 CSV 表格，帮我做统计分析并画图。');

  assert.equal(decision.route, 'default');
  assert.equal(decision.requestedEnableCodeInterpreter, true);
  assert.equal(decision.requestedReasoningEffort, 'high');
  assert.match(decision.reason, /code_interpreter/);
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
  assert.equal(decision.requestedReasoningEffort, 'high');
  assert.equal(decision.requestedEnableWebSearch, false);
  assert.equal(decision.requestedEnableCodeInterpreter, false);
  assert.match(decision.reason, /capability_upgrade/);
});
