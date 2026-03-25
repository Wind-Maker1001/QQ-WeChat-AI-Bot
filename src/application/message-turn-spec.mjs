import { buildLlmExecutionPlan } from './llm-execution-plan.mjs';
import { applyConversationDelta } from '../domain/llm-reply-outcome.mjs';
import {
  buildRouteDecisionSummary,
  formatRouteDecisionReason,
  getRouteDecisionReasonGroups,
  getRouteDecisionReasonTags,
  getRouteDecisionMatchedPrefix,
  getRouteDecisionTrigger,
  getRouteDecisionRequestedCapabilities
} from '../domain/route-decision.mjs';
import { formatError } from '../utils.mjs';

export function buildMessageTurnSpec({
  channelId,
  chatId,
  userId,
  routeInfo,
  routeState,
  preparedImageInputs = []
}) {
  return {
    channelId,
    chatId,
    userId,
    routeInfo,
    route: routeInfo.route,
    trigger: getRouteDecisionTrigger(routeInfo),
    reasonGroups: getRouteDecisionReasonGroups(routeInfo),
    reasonTags: getRouteDecisionReasonTags(routeInfo),
    routeReason: formatRouteDecisionReason(routeInfo),
    matchedPrefix: getRouteDecisionMatchedPrefix(routeInfo),
    userText: routeInfo.userText,
    imageCount: Array.isArray(preparedImageInputs) ? preparedImageInputs.length : 0,
    preparedImageInputs,
    requestedCapabilities: getRouteDecisionRequestedCapabilities(routeInfo),
    executionPlan: buildLlmExecutionPlan({
      routeInfo,
      routeState,
      preparedImageInputs
    })
  };
}

export function buildTurnReplyTelemetry({
  turnSpec,
  reply
}) {
  return {
    capturedAt: new Date().toISOString(),
    channelId: turnSpec.channelId,
    route: reply.route,
    routeReason: turnSpec.routeReason,
    matchedPrefix: turnSpec.matchedPrefix,
    model: reply.model,
    configuredApiStyle: reply.configuredApiStyle || reply.apiStyle || '',
    effectiveApiStyle: reply.effectiveApiStyle || reply.apiStyle || '',
    configuredReasoningEffort: reply.configuredReasoningEffort || '',
    effectiveReasoningEffort: reply.effectiveReasoningEffort || '',
    configuredTextVerbosity: reply.configuredTextVerbosity || '',
    effectiveTextVerbosity: reply.effectiveTextVerbosity || '',
    configuredTools: Array.isArray(reply.configuredTools) ? reply.configuredTools : [],
    effectiveTools: Array.isArray(reply.effectiveTools) ? reply.effectiveTools : [],
    imageCount: turnSpec.imageCount,
    decisionSummary: buildRouteDecisionSummary(turnSpec.routeInfo),
    chatId: String(turnSpec.chatId),
    userId: String(turnSpec.userId),
    responseId: reply.responseId || ''
  };
}

export function buildTurnFailureTelemetry({
  turnSpec = null,
  routeInfo = null,
  channelId,
  chatId,
  userId,
  error
}) {
  const effectiveRouteInfo = turnSpec?.routeInfo ?? routeInfo;

  return {
    capturedAt: new Date().toISOString(),
    channelId,
    route: effectiveRouteInfo?.route || '',
    routeReason: formatRouteDecisionReason(effectiveRouteInfo),
    matchedPrefix: getRouteDecisionMatchedPrefix(effectiveRouteInfo),
    decisionSummary: buildRouteDecisionSummary(effectiveRouteInfo),
    chatId: chatId === null || chatId === undefined ? '' : String(chatId),
    userId: userId === null || userId === undefined ? '' : String(userId),
    error: formatError(error)
  };
}

export function buildNextConversationState({
  conversationState,
  turnSpec,
  reply,
  cachedImageRefs
}) {
  const nextRouteState = applyConversationDelta({
    previousResponseId: turnSpec.executionPlan.sessionContext.previousResponseId,
    sharedMessages: turnSpec.executionPlan.sessionContext.sharedMessages,
    conversationDelta: reply.conversationDelta
  });

  return {
    ...conversationState,
    routes: {
      ...conversationState.routes,
      [turnSpec.route]: {
        previousResponseId: nextRouteState.previousResponseId,
        messages: nextRouteState.sharedMessages
      }
    },
    shared: {
      ...conversationState.shared,
      lastImageRefs: cachedImageRefs
    }
  };
}
