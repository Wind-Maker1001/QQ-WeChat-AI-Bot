import {
  getRouteDecisionRequestedCapabilities
} from '../domain/route-decision.mjs';
import {
  buildConversationSharedMessages,
  normalizeConversationMessages
} from '../domain/conversation-state.mjs';
import { shouldUseDeliberationPipeline } from './deliberation-policy.mjs';

function createGenerateReplyRequest({
  routeInfo,
  previousResponseId,
  sharedMessages,
  imageInputs,
  reasoningEffortOverride,
  textVerbosityOverride,
  enableWebSearchOverride,
  enableCodeInterpreterOverride,
  storeOverride
}) {
  const requestedCapabilities = getRouteDecisionRequestedCapabilities(routeInfo);
  const request = {
    route: routeInfo.route,
    userText: routeInfo.userText,
    previousResponseId,
    sharedMessages,
    imageInputs,
    reasoningEffortOverride:
      reasoningEffortOverride === undefined
        ? requestedCapabilities.reasoningEffort
        : reasoningEffortOverride,
    textVerbosityOverride:
      textVerbosityOverride === undefined
        ? requestedCapabilities.textVerbosity
        : textVerbosityOverride,
    enableWebSearchOverride:
      enableWebSearchOverride === undefined
        ? requestedCapabilities.enableWebSearch
        : enableWebSearchOverride,
    enableCodeInterpreterOverride:
      enableCodeInterpreterOverride === undefined
        ? requestedCapabilities.enableCodeInterpreter
        : enableCodeInterpreterOverride
  };

  if (typeof storeOverride === 'boolean') {
    request.storeOverride = storeOverride;
  }

  return request;
}

export function buildLlmExecutionPlan({
  routeInfo,
  routeState,
  conversationState,
  preparedImageInputs = []
}) {
  const requestedCapabilities = getRouteDecisionRequestedCapabilities(routeInfo);
  const routeMessages = normalizeConversationMessages(routeState?.messages);
  const mergedConversationMessages = buildConversationSharedMessages(
    conversationState,
    routeInfo.route
  );
  const sharedMessages =
    mergedConversationMessages.length > 0 ? mergedConversationMessages : routeMessages;
  const routePreviousResponseId =
    typeof routeState?.previousResponseId === 'string' && routeState.previousResponseId
      ? routeState.previousResponseId
      : null;
  const previousResponseId =
    JSON.stringify(sharedMessages) === JSON.stringify(routeMessages) ? routePreviousResponseId : null;
  const deliberationEnabled = shouldUseDeliberationPipeline(routeInfo, preparedImageInputs);

  return {
    routeInfo,
    route: routeInfo.route,
    userText: routeInfo.userText,
    imageInputs: preparedImageInputs,
    requestedCapabilities,
    sessionContext: {
      previousResponseId,
      sharedMessages
    },
    mode: deliberationEnabled ? 'deliberation' : 'direct',
    directRequest: createGenerateReplyRequest({
      routeInfo,
      previousResponseId,
      sharedMessages,
      imageInputs: preparedImageInputs
    }),
    deliberation: deliberationEnabled
      ? {
          plannerRequest: createGenerateReplyRequest({
            routeInfo,
            previousResponseId: null,
            sharedMessages,
            imageInputs: [],
            reasoningEffortOverride: 'high',
            textVerbosityOverride: 'low',
            enableWebSearchOverride: false,
            enableCodeInterpreterOverride: false,
            storeOverride: false
          }),
          draftRequest: createGenerateReplyRequest({
            routeInfo,
            previousResponseId: null,
            sharedMessages,
            imageInputs: preparedImageInputs,
            reasoningEffortOverride: requestedCapabilities.reasoningEffort || 'high',
            textVerbosityOverride: requestedCapabilities.textVerbosity || 'high',
            enableWebSearchOverride: requestedCapabilities.enableWebSearch,
            enableCodeInterpreterOverride: requestedCapabilities.enableCodeInterpreter,
            storeOverride: false
          }),
          rewriteRequest: createGenerateReplyRequest({
            routeInfo,
            previousResponseId: null,
            sharedMessages,
            imageInputs: [],
            reasoningEffortOverride: 'high',
            textVerbosityOverride: requestedCapabilities.textVerbosity || 'high',
            enableWebSearchOverride: requestedCapabilities.enableWebSearch,
            enableCodeInterpreterOverride: requestedCapabilities.enableCodeInterpreter,
            storeOverride: false
          })
        }
      : null
  };
}
