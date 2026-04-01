import test from 'node:test';
import assert from 'node:assert/strict';

import { createRouteDecision } from '../src/domain/route-decision.mjs';
import {
  createExecutionProjection,
  DELIBERATION_EXECUTION_STAGES,
  EXECUTION_KIND_DELIBERATION,
  EXECUTION_KIND_DIRECT,
  EXECUTION_STAGE_DRAFT,
  EXECUTION_STAGE_DIRECT,
  EXECUTION_STAGE_PLANNER,
  getExecutionSummary
} from '../src/domain/execution-projection.mjs';
import {
  buildMessageTurnSpec,
  buildNextConversationState,
  buildTurnFailureTelemetry,
  buildTurnReplyTelemetry
} from '../src/application/message-turn-spec.mjs';

test('turn spec builds reply telemetry around route decision and execution plan', () => {
  const turnSpec = buildMessageTurnSpec({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'default',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      decisionMetadata: {
        reasonTags: ['complex', 'web_search']
      },
      trigger: {
        kind: 'directive',
        matchedPrefix: '/ai'
      },
      userText: 'Analyze this.',
      requestedCapabilities: {
        reasoningEffort: 'high',
        textVerbosity: 'high',
        enableWebSearch: true,
        enableCodeInterpreter: false,
        needsResponsesCapabilities: true
      }
    }),
    routeState: {
      previousResponseId: 'resp_prev',
      messages: []
    },
    preparedImageInputs: [
      {
        imageUrl: 'data:image/png;base64,abc'
      }
    ]
  });

  const telemetry = buildTurnReplyTelemetry({
    turnSpec,
    reply: {
      route: 'default',
      model: 'gpt-5.4',
      configuredApiStyle: 'responses',
      effectiveApiStyle: 'responses',
      configuredReasoningEffort: 'medium',
      effectiveReasoningEffort: 'high',
      configuredTextVerbosity: 'medium',
      effectiveTextVerbosity: 'high',
      configuredTools: [],
      effectiveTools: ['web_search'],
      executionKind: EXECUTION_KIND_DIRECT,
      executionSummary: getExecutionSummary(EXECUTION_KIND_DIRECT),
      executionProjection: createExecutionProjection({
        kind: EXECUTION_KIND_DIRECT,
        completedStages: [EXECUTION_STAGE_DIRECT]
      }),
      responseId: 'resp_123'
    }
  });

  assert.equal(telemetry.channelId, 'qq');
  assert.deepEqual(turnSpec.reasonGroups, {
    triggerReasons: ['directive:/ai'],
    capabilityReasons: ['complex', 'web_search'],
    upgradeReasons: []
  });
  assert.deepEqual(turnSpec.reasonTags, ['directive:/ai', 'complex', 'web_search']);
  assert.equal(turnSpec.trigger.kind, 'directive');
  assert.equal(telemetry.routeReason, 'directive:/ai+complex+web_search');
  assert.equal(telemetry.matchedPrefix, '/ai');
  assert.deepEqual(telemetry.decisionSummary, {
    trigger: {
      kind: 'directive',
      matchedPrefix: '/ai'
    },
    reasonTags: ['directive:/ai', 'complex', 'web_search'],
    reasonGroups: {
      triggerReasons: ['directive:/ai'],
      capabilityReasons: ['complex', 'web_search'],
      upgradeReasons: []
    },
    requestedCapabilities: {
      reasoningEffort: 'high',
      textVerbosity: 'high',
      enableWebSearch: true,
      enableCodeInterpreter: false,
      needsResponsesCapabilities: true
    },
    requestedTools: {
      requested: ['web_search'],
      required: []
    },
    routeReason: 'directive:/ai+complex+web_search',
    matchedPrefix: '/ai'
  });
  assert.equal(telemetry.imageCount, 1);
  assert.equal(telemetry.executionKind, EXECUTION_KIND_DIRECT);
  assert.equal(telemetry.executionSummary, getExecutionSummary(EXECUTION_KIND_DIRECT));
  assert.deepEqual(
    telemetry.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_DIRECT,
      completedStages: [EXECUTION_STAGE_DIRECT]
    })
  );
  assert.equal(telemetry.chatId, 'chat_1');
  assert.equal(telemetry.userId, 'user_1');
  assert.equal(telemetry.responseId, 'resp_123');
});

test('turn spec builds next conversation state from execution plan session context', () => {
  const turnSpec = buildMessageTurnSpec({
    channelId: 'qq',
    chatId: 'chat_1',
    userId: 'user_1',
    routeInfo: createRouteDecision({
      route: 'advanced',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      decisionMetadata: {
        reasonTags: ['image']
      },
      trigger: {
        kind: 'image',
        matchedPrefix: ''
      },
      userText: 'Analyze image.',
      requestedCapabilities: {
        reasoningEffort: '',
        textVerbosity: '',
        enableWebSearch: undefined,
        enableCodeInterpreter: undefined,
        needsResponsesCapabilities: false
      }
    }),
    routeState: {
      previousResponseId: 'resp_prev',
      messages: [
        {
          role: 'user',
          content: 'Earlier context'
        }
      ]
    },
    preparedImageInputs: []
  });

  const nextState = buildNextConversationState({
    conversationState: {
      routes: {
        default: {
          previousResponseId: null,
          messages: []
        },
        advanced: {
          previousResponseId: 'resp_prev',
          messages: [
            {
              role: 'user',
              content: 'Earlier context'
            }
          ]
        }
      },
      shared: {
        lastImageRefs: ['old.png']
      }
    },
    turnSpec,
    reply: {
      conversationDelta: {
        previousResponseId: 'resp_next',
        sharedMessages: [
          {
            role: 'user',
            content: 'Analyze image.'
          },
          {
            role: 'assistant',
            content: 'done'
          }
        ]
      }
    },
    cachedImageRefs: ['new.png']
  });

  assert.equal(nextState.routes.advanced.previousResponseId, 'resp_next');
  assert.equal(nextState.routes.default.previousResponseId, null);
  assert.deepEqual(turnSpec.reasonGroups, {
    triggerReasons: ['image'],
    capabilityReasons: [],
    upgradeReasons: []
  });
  assert.deepEqual(nextState.routes.default.messages, [
    {
      role: 'user',
      content: 'Analyze image.'
    },
    {
      role: 'assistant',
      content: 'done'
    }
  ]);
  assert.deepEqual(nextState.routes.advanced.messages, [
    {
      role: 'user',
      content: 'Analyze image.'
    },
    {
      role: 'assistant',
      content: 'done'
    }
  ]);
  assert.deepEqual(nextState.shared.lastImageRefs, ['new.png']);
});

test('turn spec failure telemetry falls back to route info when turn spec is unavailable', () => {
  const telemetry = buildTurnFailureTelemetry({
    routeInfo: createRouteDecision({
      route: 'default',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      decisionMetadata: {
        reasonTags: ['default']
      },
      trigger: {
        kind: 'default',
        matchedPrefix: ''
      }
    }),
    channelId: 'wechat',
    chatId: 'chat_2',
    userId: 'user_2',
    executionKind: EXECUTION_KIND_DELIBERATION,
    executionSummary: getExecutionSummary(EXECUTION_KIND_DELIBERATION),
    executionProjection: createExecutionProjection({
      kind: EXECUTION_KIND_DELIBERATION,
      stages: DELIBERATION_EXECUTION_STAGES,
      failedStage: EXECUTION_STAGE_DRAFT,
      completedStages: [EXECUTION_STAGE_PLANNER]
    }),
    error: new Error('boom')
  });

  assert.equal(telemetry.channelId, 'wechat');
  assert.equal(telemetry.route, 'default');
  assert.equal(telemetry.routeReason, 'default');
  assert.deepEqual(telemetry.decisionSummary, {
    trigger: {
      kind: 'default',
      matchedPrefix: ''
    },
    reasonTags: ['default'],
    reasonGroups: {
      triggerReasons: [],
      capabilityReasons: [],
      upgradeReasons: []
    },
    requestedCapabilities: {
      reasoningEffort: '',
      textVerbosity: '',
      enableWebSearch: undefined,
      enableCodeInterpreter: undefined,
      needsResponsesCapabilities: false
    },
    requestedTools: {
      requested: [],
      required: []
    },
    routeReason: 'default',
    matchedPrefix: ''
  });
  assert.equal(telemetry.executionKind, EXECUTION_KIND_DELIBERATION);
  assert.equal(telemetry.executionSummary, getExecutionSummary(EXECUTION_KIND_DELIBERATION));
  assert.deepEqual(
    telemetry.executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_DELIBERATION,
      stages: DELIBERATION_EXECUTION_STAGES,
      failedStage: EXECUTION_STAGE_DRAFT,
      completedStages: [EXECUTION_STAGE_PLANNER]
    })
  );
  assert.equal(telemetry.chatId, 'chat_2');
  assert.equal(telemetry.userId, 'user_2');
  assert.match(telemetry.error, /boom/);
});
