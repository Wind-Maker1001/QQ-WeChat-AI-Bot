import test from 'node:test';
import assert from 'node:assert/strict';

import {
  __test__,
  tryBuildCurrentTurnCapabilityReply
} from '../src/application/current-turn-capability-reply.mjs';

test('current-turn capability reply recognizes tool introspection questions', () => {
  assert.equal(__test__.shouldReplyLocally('这轮对话可调用工具？'), true);
  assert.equal(__test__.shouldReplyLocally('本轮能联网吗？'), true);
  assert.equal(__test__.shouldReplyLocally('帮我总结一下今天的任务'), false);
});

test('current-turn capability reply formats enabled tools and route context', () => {
  const reply = tryBuildCurrentTurnCapabilityReply({
    userText: '这轮对话可调用工具？',
    requestDescriptor: {
      route: 'advanced',
      effectiveApiStyle: 'responses',
      model: 'gpt-5.4',
      effectiveTools: ['web_search', 'code_interpreter'],
      effectiveEnableWebSearch: true,
      effectiveEnableCodeInterpreter: true
    }
  });

  assert.match(reply, /advanced 路由/);
  assert.match(reply, /web_search（联网搜索）/);
  assert.match(reply, /code_interpreter（代码解释器）/);
  assert.match(reply, /联网搜索：开启；代码解释器：开启/);
});

test('current-turn capability reply reports disabled tools explicitly', () => {
  const reply = tryBuildCurrentTurnCapabilityReply({
    userText: '本轮能联网吗？',
    requestDescriptor: {
      route: 'default',
      effectiveApiStyle: 'responses',
      model: 'gpt-5.4',
      effectiveTools: [],
      effectiveEnableWebSearch: false,
      effectiveEnableCodeInterpreter: false
    }
  });

  assert.match(reply, /default 路由/);
  assert.match(reply, /可调用工具：无/);
  assert.match(reply, /联网搜索：关闭；代码解释器：关闭/);
});
