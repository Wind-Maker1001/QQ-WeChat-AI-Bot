import test from 'node:test';
import assert from 'node:assert/strict';

import { createRouteDecision } from '../src/domain/route-decision.mjs';
import {
  createExecutionProjection,
  DELIBERATION_EXECUTION_STAGES,
  EXECUTION_KIND_DELIBERATION,
  EXECUTION_KIND_DIRECT,
  EXECUTION_KIND_LOCAL_CAPABILITY_REPLY,
  EXECUTION_STAGE_DIRECT,
  EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY,
  getExecutionSummary
} from '../src/domain/execution-projection.mjs';
import {
  __test__,
  buildMessageTurnStrategy,
  executeMessageTurnStrategy
} from '../src/application/message-turn-strategy.mjs';

function createConversationState() {
  return {
    routes: {
      default: {
        previousResponseId: 'resp_default_prev',
        messages: [
          {
            role: 'assistant',
            content: 'Existing context'
          }
        ]
      },
      advanced: {
        previousResponseId: 'resp_advanced_prev',
        messages: []
      }
    },
    shared: {
      lastImageRefs: []
    }
  };
}

test('message turn strategy assembles deliberation stage descriptors from the execution plan', () => {
  const describeCalls = [];
  const strategy = buildMessageTurnStrategy({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'default',
      userText: 'Compare two implementations step by step.',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      requestedCapabilities: {
        reasoningEffort: 'high',
        textVerbosity: 'high',
        enableWebSearch: true,
        enableCodeInterpreter: false,
        needsResponsesCapabilities: true
      },
      reasonGroups: {
        triggerReasons: [],
        capabilityReasons: ['complex', 'web_search'],
        upgradeReasons: []
      }
    }),
    routeState: createConversationState().routes.default,
    conversationState: createConversationState(),
    llmRouter: {
      describeRequest(request) {
        describeCalls.push(request.userText);
        return {
          route: request.route,
          effectiveApiStyle: 'responses',
          effectiveTools: request.enableWebSearchOverride ? ['web_search'] : [],
          effectiveEnableWebSearch: request.enableWebSearchOverride === true,
          effectiveEnableCodeInterpreter: request.enableCodeInterpreterOverride === true
        };
      }
    }
  });

  assert.equal(strategy.executionKind, EXECUTION_KIND_DELIBERATION);
  assert.equal(strategy.executionSummary, getExecutionSummary(EXECUTION_KIND_DELIBERATION));
  assert.deepEqual(
    strategy.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_DELIBERATION,
      stages: DELIBERATION_EXECUTION_STAGES
    })
  );
  assert.equal(strategy.turnSpec.executionPlan.mode, 'deliberation');
  assert.equal(strategy.executionAssembly.mode, 'deliberation');
  assert.equal(strategy.executionAssembly.direct.descriptor.effectiveApiStyle, 'responses');
  assert.equal(strategy.executionAssembly.deliberation.planner.descriptor.effectiveEnableWebSearch, false);
  assert.equal(strategy.executionAssembly.deliberation.draft.descriptor.effectiveEnableWebSearch, true);
  assert.equal(strategy.executionAssembly.deliberation.rewrite.descriptor.effectiveEnableWebSearch, true);
  assert.equal(describeCalls.length, 4);
});

test('message turn strategy handles current-turn capability replies without calling the router', async () => {
  let generateReplyCallCount = 0;
  const questionText = 'what tools are available this turn';
  const strategy = buildMessageTurnStrategy({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'advanced',
      userText: questionText,
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      requestedCapabilities: {
        reasoningEffort: '',
        textVerbosity: '',
        enableWebSearch: true,
        enableCodeInterpreter: true,
        needsResponsesCapabilities: true
      }
    }),
    routeState: createConversationState().routes.advanced,
    conversationState: createConversationState(),
    llmRouter: {
      describeRequest() {
        return {
          route: 'advanced',
          model: 'gpt-5.4',
          configuredApiStyle: 'responses',
          effectiveApiStyle: 'responses',
          configuredEnableWebSearch: true,
          effectiveEnableWebSearch: true,
          configuredEnableCodeInterpreter: true,
          effectiveEnableCodeInterpreter: true,
          configuredTools: ['web_search', 'code_interpreter'],
          effectiveTools: ['web_search', 'code_interpreter']
        };
      },
      async generateReply() {
        generateReplyCallCount += 1;
        throw new Error('generateReply should not be called for local capability replies.');
      }
    }
  });

  const reply = await executeMessageTurnStrategy({
    strategy,
    llmRouter: {
      async generateReply() {
        generateReplyCallCount += 1;
        throw new Error('generateReply should not be called for local capability replies.');
      }
    },
    logger: {
      info() {},
      error() {}
    }
  });

  assert.equal(strategy.executionKind, EXECUTION_KIND_LOCAL_CAPABILITY_REPLY);
  assert.equal(strategy.executionSummary, getExecutionSummary(EXECUTION_KIND_LOCAL_CAPABILITY_REPLY));
  assert.deepEqual(
    strategy.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_LOCAL_CAPABILITY_REPLY
    })
  );
  assert.equal(generateReplyCallCount, 0);
  assert.ok(strategy.localCapabilityReplyText);
  assert.match(strategy.localCapabilityReplyText, /web_search/);
  assert.match(strategy.localCapabilityReplyText, /code_interpreter/);
  assert.deepEqual(reply.effectiveTools, ['web_search', 'code_interpreter']);
  assert.equal(reply.executionKind, EXECUTION_KIND_LOCAL_CAPABILITY_REPLY);
  assert.equal(reply.executionSummary, getExecutionSummary(EXECUTION_KIND_LOCAL_CAPABILITY_REPLY));
  assert.deepEqual(
    reply.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_LOCAL_CAPABILITY_REPLY,
      completedStages: [EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY]
    })
  );
  assert.equal(reply.text, strategy.localCapabilityReplyText);
  assert.equal(reply.route, 'advanced');
  assert.equal(reply.conversationDelta.clearPreviousResponseId, true);
});

test('message turn strategy executes local tool replies before calling the router', async () => {
  let generateReplyCallCount = 0;
  const strategy = buildMessageTurnStrategy({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'default',
      userText: 'Show the current runtime status.',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      requestedTools: {
        requested: ['local_runtime_state'],
        required: ['local_runtime_state']
      }
    }),
    routeState: createConversationState().routes.default,
    conversationState: createConversationState(),
    llmRouter: {
      describeRequest() {
        return {
          route: 'default',
          model: 'gpt-5.4',
          configuredApiStyle: 'responses',
          effectiveApiStyle: 'responses',
          requestedTools: {
            requested: ['local_runtime_state'],
            required: ['local_runtime_state']
          },
          effectiveTools: ['local_runtime_state'],
          effectiveLocalTools: ['local_runtime_state'],
          suppressedTools: []
        };
      },
      async tryExecuteLocalToolRequest() {
        return {
          text: 'Current route: default'
        };
      },
      async generateReply() {
        generateReplyCallCount += 1;
        throw new Error('generateReply should not be called when a required local tool handled the turn.');
      }
    }
  });

  const reply = await executeMessageTurnStrategy({
    strategy,
    llmRouter: {
      async tryExecuteLocalToolRequest() {
        return {
          text: 'Current route: default'
        };
      },
      async generateReply() {
        generateReplyCallCount += 1;
        throw new Error('generateReply should not be called when a required local tool handled the turn.');
      }
    },
    logger: {
      info() {},
      error() {}
    }
  });

  assert.equal(generateReplyCallCount, 0);
  assert.equal(reply.text, 'Current route: default');
  assert.deepEqual(reply.effectiveTools, ['local_runtime_state']);
  assert.equal(reply.executionKind, EXECUTION_KIND_LOCAL_CAPABILITY_REPLY);
});

test('message turn strategy derives direct failure projection for request failures', async () => {
  const strategy = buildMessageTurnStrategy({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'default',
      userText: 'hello',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      }
    }),
    routeState: createConversationState().routes.default,
    conversationState: createConversationState(),
    llmRouter: {
      describeRequest() {
        return {
          route: 'default',
          effectiveApiStyle: 'responses',
          effectiveTools: []
        };
      }
    }
  });

  await assert.rejects(
    () =>
      executeMessageTurnStrategy({
        strategy,
        llmRouter: {
          async generateReply() {
            throw new Error('direct failed');
          }
        },
        logger: {
          info() {},
          error() {}
        }
      }),
    (error) => {
      assert.match(error.message, /direct failed/);
      assert.deepEqual(
        error.executionFailureProjection,
        createExecutionProjection({
          kind: EXECUTION_KIND_DIRECT,
          failedStage: EXECUTION_STAGE_DIRECT
        })
      );
      return true;
    }
  );
});

test('message turn strategy exposes canonical direct failure projection shape', () => {
  assert.deepEqual(
    __test__.buildExecutionFailureProjection({
      executionProjection: {
        kind: EXECUTION_KIND_DIRECT,
        summary: getExecutionSummary(EXECUTION_KIND_DIRECT),
        stages: [EXECUTION_STAGE_DIRECT],
        failedStage: '',
        completedStages: [],
        degraded: false,
        recoveries: []
      }
    }),
    createExecutionProjection({
      kind: EXECUTION_KIND_DIRECT,
      failedStage: EXECUTION_STAGE_DIRECT
    })
  );
});
