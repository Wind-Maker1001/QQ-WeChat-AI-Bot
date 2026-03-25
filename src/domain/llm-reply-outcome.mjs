import { normalizeConversationMessages } from './conversation-state.mjs';

const MAX_CONVERSATION_MESSAGES = 24;

function normalizePreviousResponseId(value) {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

export function appendConversationMessages(sharedMessages, userText, assistantText) {
  return [
    ...normalizeConversationMessages(sharedMessages),
    {
      role: 'user',
      content: typeof userText === 'string' ? userText : ''
    },
    ...(assistantText
      ? [
          {
            role: 'assistant',
            content: assistantText
          }
        ]
      : [])
  ].slice(-MAX_CONVERSATION_MESSAGES);
}

export function createConversationDelta({
  previousResponseId = null,
  sharedMessages = [],
  clearPreviousResponseId = false
} = {}) {
  return {
    previousResponseId: normalizePreviousResponseId(previousResponseId),
    sharedMessages: normalizeConversationMessages(sharedMessages).slice(-MAX_CONVERSATION_MESSAGES),
    clearPreviousResponseId: clearPreviousResponseId === true
  };
}

export function createLlmReplyOutcome({
  text = '',
  responseId = null,
  conversationDelta
} = {}) {
  return {
    text: typeof text === 'string' ? text : '',
    responseId: normalizePreviousResponseId(responseId),
    conversationDelta: createConversationDelta(conversationDelta)
  };
}

export function applyConversationDelta({
  previousResponseId = null,
  sharedMessages = [],
  conversationDelta
} = {}) {
  const normalizedDelta = createConversationDelta(conversationDelta);

  return {
    previousResponseId: normalizedDelta.clearPreviousResponseId
      ? null
      : normalizedDelta.previousResponseId ?? normalizePreviousResponseId(previousResponseId),
    sharedMessages:
      normalizedDelta.sharedMessages.length > 0
        ? normalizedDelta.sharedMessages
        : normalizeConversationMessages(sharedMessages).slice(-MAX_CONVERSATION_MESSAGES)
  };
}
