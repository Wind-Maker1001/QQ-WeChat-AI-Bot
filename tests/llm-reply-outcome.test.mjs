import test from 'node:test';
import assert from 'node:assert/strict';

import {
  appendConversationMessages,
  applyConversationDelta,
  createConversationDelta,
  createLlmReplyOutcome
} from '../src/domain/llm-reply-outcome.mjs';

test('reply outcome appends normalized conversation messages', () => {
  const messages = appendConversationMessages(
    [
      { role: 'user', content: ' earlier ' },
      { role: 'assistant', content: ' done ' },
      { role: 'user', content: '' }
    ],
    'new question',
    'new answer'
  );

  assert.deepEqual(messages, [
    { role: 'user', content: 'earlier' },
    { role: 'assistant', content: 'done' },
    { role: 'user', content: 'new question' },
    { role: 'assistant', content: 'new answer' }
  ]);
});

test('reply outcome creates structured conversation delta and reply outcome', () => {
  const outcome = createLlmReplyOutcome({
    text: 'ok',
    responseId: 'resp_1',
    conversationDelta: createConversationDelta({
      previousResponseId: 'resp_1',
      sharedMessages: [{ role: 'user', content: 'hello' }]
    })
  });

  assert.equal(outcome.text, 'ok');
  assert.equal(outcome.responseId, 'resp_1');
  assert.deepEqual(outcome.conversationDelta, {
    previousResponseId: 'resp_1',
    sharedMessages: [{ role: 'user', content: 'hello' }],
    clearPreviousResponseId: false
  });
});

test('reply outcome applies clearPreviousResponseId semantics during persistence', () => {
  const routeState = applyConversationDelta({
    previousResponseId: 'resp_prev',
    sharedMessages: [{ role: 'assistant', content: 'context' }],
    conversationDelta: {
      clearPreviousResponseId: true,
      previousResponseId: null,
      sharedMessages: [{ role: 'user', content: 'fresh turn' }]
    }
  });

  assert.equal(routeState.previousResponseId, null);
  assert.deepEqual(routeState.sharedMessages, [{ role: 'user', content: 'fresh turn' }]);
});
