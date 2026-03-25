import {
  appendConversationMessages,
  createConversationDelta
} from '../domain/llm-reply-outcome.mjs';
import { getRouteDecisionRequestedCapabilities } from '../domain/route-decision.mjs';

export function shouldUseDeliberationPipeline(routeInfo, preparedImageInputs) {
  if (!routeInfo || (Array.isArray(preparedImageInputs) && preparedImageInputs.length > 0)) {
    return false;
  }

  const requestedCapabilities = getRouteDecisionRequestedCapabilities(routeInfo);

  return (
    requestedCapabilities.reasoningEffort === 'high' ||
    requestedCapabilities.enableWebSearch === true ||
    requestedCapabilities.enableCodeInterpreter === true
  );
}

export function buildDeliberationConversationDelta({
  sharedMessages,
  userText,
  assistantText
}) {
  return createConversationDelta({
    clearPreviousResponseId: true,
    previousResponseId: null,
    sharedMessages: appendConversationMessages(sharedMessages, userText, assistantText)
  });
}
