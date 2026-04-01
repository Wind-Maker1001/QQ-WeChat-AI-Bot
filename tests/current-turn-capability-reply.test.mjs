import test from 'node:test';
import assert from 'node:assert/strict';

import {
  __test__,
  tryBuildCurrentTurnCapabilityReply
} from '../src/application/current-turn-capability-reply.mjs';

test('current-turn capability reply recognizes tool introspection questions', () => {
  assert.equal(__test__.shouldReplyLocally('what tools are available this turn'), true);
  assert.equal(__test__.shouldReplyLocally('can you run code this turn'), true);
  assert.equal(__test__.shouldReplyLocally('summarize today tasks'), false);
});

test('current-turn capability reply formats enabled and suppressed tools from the registry', () => {
  const reply = tryBuildCurrentTurnCapabilityReply({
    userText: 'what tools are available this turn',
    requestDescriptor: {
      route: 'advanced',
      effectiveApiStyle: 'responses',
      model: 'gpt-5.4',
      effectiveTools: ['web_search', 'code_interpreter'],
      suppressedTools: [
        {
          toolKind: 'local_snapshot_inspect',
          reason: 'context_unavailable'
        }
      ],
      effectiveEnableWebSearch: true,
      effectiveEnableCodeInterpreter: true
    }
  });

  assert.match(reply, /Current turn route: advanced/);
  assert.match(reply, /web_search \(Web Search\)/);
  assert.match(reply, /code_interpreter \(Code Interpreter\)/);
  assert.match(reply, /local_snapshot_inspect/);
  assert.match(reply, /Web search: enabled/);
  assert.match(reply, /Code interpreter: enabled/);
});

test('current-turn capability reply reports disabled tools explicitly', () => {
  const reply = tryBuildCurrentTurnCapabilityReply({
    userText: 'can you search this turn',
    requestDescriptor: {
      route: 'default',
      effectiveApiStyle: 'responses',
      model: 'gpt-5.4',
      effectiveTools: [],
      suppressedTools: [],
      effectiveEnableWebSearch: false,
      effectiveEnableCodeInterpreter: false
    }
  });

  assert.match(reply, /Current turn route: default/);
  assert.match(reply, /Callable tools: none/);
  assert.match(reply, /Suppressed tools: none/);
  assert.match(reply, /Web search: disabled/);
  assert.match(reply, /Code interpreter: disabled/);
});
