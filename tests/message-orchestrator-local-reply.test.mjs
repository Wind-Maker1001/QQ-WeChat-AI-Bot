import test from 'node:test';
import assert from 'node:assert/strict';

import { orchestrateIncomingMessage } from '../src/application/message-orchestrator.mjs';
import {
  createExecutionProjection,
  EXECUTION_KIND_LOCAL_CAPABILITY_REPLY,
  EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY,
  getExecutionSummary
} from '../src/domain/execution-projection.mjs';
import { createRouteDecision } from '../src/domain/route-decision.mjs';

function createEmptyConversationState() {
  return {
    routes: {
      default: {
        previousResponseId: 'resp_default_prev',
        messages: []
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

test('message orchestrator answers current-turn tool introspection locally without calling the LLM', async () => {
  const sentMessages = [];
  const telemetryEntries = [];
  let savedConversationState = null;
  let generateReplyCallCount = 0;
  const conversationState = createEmptyConversationState();
  const questionText = '这轮对话可调用工具？';
  const routeInfo = createRouteDecision({
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
  });

  await orchestrateIncomingMessage({
    message: {
      chatId: 'chat_1',
      userId: 'user_1',
      text: questionText,
      trigger: 'mention',
      triggered: true,
      imageRefs: [],
      replyToMessageIds: []
    },
    sessionStore: {
      getConversation() {
        return conversationState;
      },
      async setConversation(_conversationKey, nextConversationState) {
        savedConversationState = nextConversationState;
      }
    },
    llmRouter: {
      resolveRoute() {
        return routeInfo;
      },
      describeRequest() {
        return {
          route: 'advanced',
          model: 'gpt-5.4',
          configuredApiStyle: 'responses',
          effectiveApiStyle: 'responses',
          configuredReasoningEffort: 'high',
          effectiveReasoningEffort: 'high',
          configuredTextVerbosity: 'high',
          effectiveTextVerbosity: 'high',
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
        return {
          text: 'unexpected'
        };
      }
    },
    channelPort: {
      channelId: 'qq',
      async sendText(chatId, text) {
        sentMessages.push({ chatId, text });
      },
      async readImage() {
        throw new Error('readImage should not be called.');
      },
      async getMessage() {
        throw new Error('getMessage should not be called.');
      }
    },
    logger: {
      info() {},
      error() {}
    },
    prepareImageInputs: async () => [],
    imageCacheDir: 'D:\\Temp\\qq-ai-bot-image-cache-test',
    maxOutputChars: 800,
    onReplyTelemetry(telemetry) {
      telemetryEntries.push(telemetry);
    }
  });

  assert.equal(generateReplyCallCount, 0);
  assert.equal(sentMessages.length, 1);
  assert.equal(sentMessages[0].chatId, 'chat_1');
  assert.match(sentMessages[0].text, /web_search \(Web Search\)/);
  assert.match(sentMessages[0].text, /code_interpreter \(Code Interpreter\)/);
  assert.equal(telemetryEntries.length, 1);
  assert.deepEqual(telemetryEntries[0].effectiveTools, ['web_search', 'code_interpreter']);
  assert.equal(telemetryEntries[0].executionKind, EXECUTION_KIND_LOCAL_CAPABILITY_REPLY);
  assert.equal(
    telemetryEntries[0].executionSummary,
    getExecutionSummary(EXECUTION_KIND_LOCAL_CAPABILITY_REPLY)
  );
  assert.deepEqual(
    telemetryEntries[0].executionProjection,
    createExecutionProjection({
      kind: EXECUTION_KIND_LOCAL_CAPABILITY_REPLY,
      completedStages: [EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY]
    })
  );
  assert.equal(savedConversationState.routes.default.previousResponseId, null);
  assert.deepEqual(savedConversationState.routes.default.messages, [
    {
      role: 'user',
      content: questionText
    },
    {
      role: 'assistant',
      content: sentMessages[0].text
    }
  ]);
  assert.equal(savedConversationState.routes.advanced.previousResponseId, null);
  assert.deepEqual(savedConversationState.routes.advanced.messages, [
    {
      role: 'user',
      content: questionText
    },
    {
      role: 'assistant',
      content: sentMessages[0].text
    }
  ]);
});
