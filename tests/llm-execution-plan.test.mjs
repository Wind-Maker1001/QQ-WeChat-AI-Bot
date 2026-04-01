import test from 'node:test';
import assert from 'node:assert/strict';

import { createRouteDecision } from '../src/domain/route-decision.mjs';
import { buildLlmExecutionPlan } from '../src/application/llm-execution-plan.mjs';

test('execution plan builds direct request with session context and images', () => {
  const executionPlan = buildLlmExecutionPlan({
    routeInfo: createRouteDecision({
      route: 'advanced',
      userText: 'Analyze this image.',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
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
          content: 'Earlier message'
        }
      ]
    },
    preparedImageInputs: [
      {
        imageUrl: 'data:image/png;base64,abc'
      }
    ]
  });

  assert.equal(executionPlan.mode, 'direct');
  assert.equal(executionPlan.route, 'advanced');
  assert.equal(executionPlan.userText, 'Analyze this image.');
  assert.equal(executionPlan.imageInputs.length, 1);
  assert.equal(executionPlan.sessionContext.previousResponseId, 'resp_prev');
  assert.deepEqual(executionPlan.sessionContext.sharedMessages, [
    {
      role: 'user',
      content: 'Earlier message'
    }
  ]);
  assert.deepEqual(executionPlan.directRequest, {
    route: 'advanced',
    userText: 'Analyze this image.',
    previousResponseId: 'resp_prev',
    sharedMessages: [
      {
        role: 'user',
        content: 'Earlier message'
      }
    ],
    imageInputs: [
      {
        imageUrl: 'data:image/png;base64,abc'
      }
    ],
    reasoningEffortOverride: '',
    textVerbosityOverride: '',
    enableWebSearchOverride: undefined,
    enableCodeInterpreterOverride: undefined,
    requestedTools: {
      requested: [],
      required: []
    }
  });
  assert.equal(executionPlan.deliberation, null);
});

test('execution plan formalizes deliberation stage requests for complex text turns', () => {
  const executionPlan = buildLlmExecutionPlan({
    routeInfo: createRouteDecision({
      route: 'default',
      userText: 'Compare two approaches.',
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
        capabilityReasons: ['complex'],
        upgradeReasons: []
      }
    }),
    routeState: {
      previousResponseId: 'resp_prev',
      messages: [
        {
          role: 'assistant',
          content: 'Existing context'
        }
      ]
    },
    preparedImageInputs: []
  });

  assert.equal(executionPlan.mode, 'deliberation');
  assert.equal(executionPlan.requestedCapabilities.reasoningEffort, 'high');
  assert.equal(executionPlan.directRequest.previousResponseId, 'resp_prev');
  assert.equal(executionPlan.deliberation.plannerRequest.previousResponseId, null);
  assert.equal(executionPlan.deliberation.plannerRequest.reasoningEffortOverride, 'high');
  assert.equal(executionPlan.deliberation.plannerRequest.textVerbosityOverride, 'low');
  assert.equal(executionPlan.deliberation.plannerRequest.enableWebSearchOverride, false);
  assert.equal(executionPlan.deliberation.plannerRequest.enableCodeInterpreterOverride, false);
  assert.equal(executionPlan.deliberation.plannerRequest.storeOverride, false);
  assert.deepEqual(executionPlan.deliberation.plannerRequest.requestedTools, {
    requested: [],
    required: []
  });
  assert.deepEqual(executionPlan.deliberation.plannerRequest.imageInputs, []);

  assert.equal(executionPlan.deliberation.draftRequest.previousResponseId, null);
  assert.equal(executionPlan.deliberation.draftRequest.reasoningEffortOverride, 'high');
  assert.equal(executionPlan.deliberation.draftRequest.textVerbosityOverride, 'high');
  assert.equal(executionPlan.deliberation.draftRequest.enableWebSearchOverride, true);
  assert.equal(executionPlan.deliberation.draftRequest.enableCodeInterpreterOverride, false);
  assert.equal(executionPlan.deliberation.draftRequest.storeOverride, false);
  assert.deepEqual(executionPlan.deliberation.draftRequest.requestedTools, {
    requested: ['web_search'],
    required: []
  });

  assert.equal(executionPlan.deliberation.rewriteRequest.previousResponseId, null);
  assert.equal(executionPlan.deliberation.rewriteRequest.reasoningEffortOverride, 'high');
  assert.equal(executionPlan.deliberation.rewriteRequest.textVerbosityOverride, 'high');
  assert.equal(executionPlan.deliberation.rewriteRequest.enableWebSearchOverride, true);
  assert.equal(executionPlan.deliberation.rewriteRequest.enableCodeInterpreterOverride, false);
  assert.equal(executionPlan.deliberation.rewriteRequest.storeOverride, false);
  assert.deepEqual(executionPlan.deliberation.rewriteRequest.requestedTools, {
    requested: ['web_search'],
    required: []
  });
});

test('execution plan merges route histories and clears stale response ids when routes diverge', () => {
  const executionPlan = buildLlmExecutionPlan({
    routeInfo: createRouteDecision({
      route: 'advanced',
      userText: 'Continue the earlier thread.',
      selectedRoute: {
        model: 'gpt-5.4',
        apiStyle: 'responses'
      },
      requestedCapabilities: {
        reasoningEffort: '',
        textVerbosity: '',
        enableWebSearch: undefined,
        enableCodeInterpreter: undefined,
        needsResponsesCapabilities: false
      }
    }),
    routeState: {
      previousResponseId: 'resp_advanced_prev',
      messages: [
        {
          role: 'user',
          content: 'Only advanced sees this turn'
        }
      ]
    },
    conversationState: {
      routes: {
        default: {
          previousResponseId: 'resp_default_prev',
          messages: [
            {
              role: 'user',
              content: 'Shared context'
            },
            {
              role: 'assistant',
              content: 'Shared answer'
            }
          ]
        },
        advanced: {
          previousResponseId: 'resp_advanced_prev',
          messages: [
            {
              role: 'user',
              content: 'Only advanced sees this turn'
            }
          ]
        }
      }
    },
    preparedImageInputs: []
  });

  assert.equal(executionPlan.sessionContext.previousResponseId, null);
  assert.deepEqual(executionPlan.sessionContext.sharedMessages, [
    {
      role: 'user',
      content: 'Shared context'
    },
    {
      role: 'assistant',
      content: 'Shared answer'
    }
  ]);
  assert.equal(executionPlan.directRequest.previousResponseId, null);
});
