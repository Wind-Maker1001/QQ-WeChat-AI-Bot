import test from 'node:test';
import assert from 'node:assert/strict';

import { runDeliberationPipeline } from '../src/application/deliberation-executor.mjs';

function createLogger() {
  return {
    infos: [],
    errors: [],
    info(message) {
      this.infos.push(message);
    },
    error(message) {
      this.errors.push(message);
    }
  };
}

test('deliberation executor runs planner draft rewrite with explicit internal settings', async () => {
  const calls = [];
  const logger = createLogger();
  const llmRouter = {
    async generateReply(request) {
      calls.push(request);

      if (request.userText.includes('[INTERNAL_PLANNER]')) {
        return {
          route: 'default',
          model: 'gpt-5.4',
          text: 'planner-outline'
        };
      }

      if (request.userText.includes('[INTERNAL_DRAFT]')) {
        return {
          route: 'default',
          model: 'gpt-5.4',
          text: 'draft-answer'
        };
      }

      if (request.userText.includes('[INTERNAL_REWRITE]')) {
        return {
          route: 'default',
          model: 'gpt-5.4',
          text: 'rewritten-answer'
        };
      }

      throw new Error('Unexpected deliberation stage.');
    }
  };

  const reply = await runDeliberationPipeline({
    llmRouter,
    executionPlan: {
      route: 'default',
      userText: 'Analyze this deeply.',
      sessionContext: {
        sharedMessages: [
          {
            role: 'user',
            content: 'existing context'
          }
        ]
      },
      deliberation: {
        plannerRequest: {
          route: 'default',
          previousResponseId: null,
          sharedMessages: [
            {
              role: 'user',
              content: 'existing context'
            }
          ],
          imageInputs: [],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: 'low',
          enableWebSearchOverride: false,
          enableCodeInterpreterOverride: false,
          storeOverride: false
        },
        draftRequest: {
          route: 'default',
          previousResponseId: null,
          sharedMessages: [
            {
              role: 'user',
              content: 'existing context'
            }
          ],
          imageInputs: [
            {
              imageUrl: 'data:image/png;base64,abc'
            }
          ],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: 'high',
          enableWebSearchOverride: true,
          enableCodeInterpreterOverride: false,
          storeOverride: false
        },
        rewriteRequest: {
          route: 'default',
          previousResponseId: null,
          sharedMessages: [
            {
              role: 'user',
              content: 'existing context'
            }
          ],
          imageInputs: [],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: 'high',
          enableWebSearchOverride: true,
          enableCodeInterpreterOverride: false,
          storeOverride: false
        }
      }
    },
    logger
  });

  assert.equal(calls.length, 3);
  assert.match(calls[0].userText, /\[INTERNAL_PLANNER\]/);
  assert.equal(calls[0].reasoningEffortOverride, 'high');
  assert.equal(calls[0].textVerbosityOverride, 'low');
  assert.equal(calls[0].enableWebSearchOverride, false);
  assert.equal(calls[0].enableCodeInterpreterOverride, false);
  assert.equal(calls[0].storeOverride, false);
  assert.deepEqual(calls[0].imageInputs, []);

  assert.match(calls[1].userText, /\[INTERNAL_DRAFT\]/);
  assert.equal(calls[1].reasoningEffortOverride, 'high');
  assert.equal(calls[1].textVerbosityOverride, 'high');
  assert.equal(calls[1].enableWebSearchOverride, true);
  assert.equal(calls[1].enableCodeInterpreterOverride, false);
  assert.equal(calls[1].storeOverride, false);
  assert.equal(calls[1].imageInputs.length, 1);

  assert.match(calls[2].userText, /\[INTERNAL_REWRITE\]/);
  assert.equal(calls[2].reasoningEffortOverride, 'high');
  assert.equal(calls[2].textVerbosityOverride, 'high');
  assert.equal(calls[2].enableWebSearchOverride, true);
  assert.equal(calls[2].enableCodeInterpreterOverride, false);
  assert.equal(calls[2].storeOverride, false);
  assert.deepEqual(calls[2].imageInputs, []);

  assert.equal(reply.text, 'rewritten-answer');
  assert.equal(reply.conversationDelta.clearPreviousResponseId, true);
  assert.equal(reply.conversationDelta.previousResponseId, null);
  assert.deepEqual(reply.conversationDelta.sharedMessages, [
    {
      role: 'user',
      content: 'existing context'
    },
    {
      role: 'user',
      content: 'Analyze this deeply.'
    },
    {
      role: 'assistant',
      content: 'rewritten-answer'
    }
  ]);
  assert.equal(logger.errors.length, 0);
  assert.equal(logger.infos.length, 1);
});

test('deliberation executor falls back to draft when rewrite fails and tolerates planner failure', async () => {
  const calls = [];
  const logger = createLogger();
  const llmRouter = {
    async generateReply(request) {
      calls.push(request);

      if (request.userText.includes('[INTERNAL_PLANNER]')) {
        throw new Error('planner failed');
      }

      if (request.userText.includes('[INTERNAL_DRAFT]')) {
        return {
          route: 'advanced',
          model: 'gpt-5.4',
          text: 'draft-answer'
        };
      }

      if (request.userText.includes('[INTERNAL_REWRITE]')) {
        throw new Error('rewrite failed');
      }

      throw new Error('Unexpected deliberation stage.');
    }
  };

  const reply = await runDeliberationPipeline({
    llmRouter,
    executionPlan: {
      route: 'advanced',
      userText: 'Compute this.',
      sessionContext: {
        sharedMessages: []
      },
      deliberation: {
        plannerRequest: {
          route: 'advanced',
          previousResponseId: null,
          sharedMessages: [],
          imageInputs: [],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: 'low',
          enableWebSearchOverride: false,
          enableCodeInterpreterOverride: false,
          storeOverride: false
        },
        draftRequest: {
          route: 'advanced',
          previousResponseId: null,
          sharedMessages: [],
          imageInputs: [],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: '',
          enableWebSearchOverride: false,
          enableCodeInterpreterOverride: true,
          storeOverride: false
        },
        rewriteRequest: {
          route: 'advanced',
          previousResponseId: null,
          sharedMessages: [],
          imageInputs: [],
          reasoningEffortOverride: 'high',
          textVerbosityOverride: 'medium',
          enableWebSearchOverride: false,
          enableCodeInterpreterOverride: true,
          storeOverride: false
        }
      }
    },
    logger
  });

  assert.equal(calls.length, 3);
  assert.equal(calls[1].reasoningEffortOverride, 'high');
  assert.equal(calls[1].textVerbosityOverride, '');
  assert.equal(calls[1].enableWebSearchOverride, false);
  assert.equal(calls[1].enableCodeInterpreterOverride, true);
  assert.equal(calls[2].textVerbosityOverride, 'medium');

  assert.equal(reply.text, 'draft-answer');
  assert.deepEqual(reply.conversationDelta.sharedMessages, [
    {
      role: 'user',
      content: 'Compute this.'
    },
    {
      role: 'assistant',
      content: 'draft-answer'
    }
  ]);
  assert.equal(logger.infos.length, 0);
  assert.equal(logger.errors.length, 2);
  assert.match(logger.errors[0], /Planner failed/);
  assert.match(logger.errors[1], /Final rewrite failed/);
});
