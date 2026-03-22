export const CONVERSATION_STATE_VERSION = 2;
export const CONVERSATION_ROUTE_NAMES = Object.freeze(['default', 'advanced']);

function normalizePreviousResponseId(value) {
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

export function normalizeConversationMessages(messages) {
  if (!Array.isArray(messages)) {
    return [];
  }

  return messages
    .filter((message) => message && typeof message === 'object')
    .map((message) => {
      const role = message.role === 'assistant' ? 'assistant' : 'user';
      const content = typeof message.content === 'string' ? message.content.trim() : '';

      if (!content) {
        return null;
      }

      return {
        role,
        content
      };
    })
    .filter(Boolean);
}

function normalizeRouteContext(routeContext) {
  if (!routeContext || typeof routeContext !== 'object' || Array.isArray(routeContext)) {
    return {
      previousResponseId: null,
      messages: []
    };
  }

  return {
    previousResponseId: normalizePreviousResponseId(routeContext.previousResponseId),
    messages: normalizeConversationMessages(routeContext.messages)
  };
}

function normalizeLastImageRefs(lastImageRefs) {
  if (!Array.isArray(lastImageRefs)) {
    return [];
  }

  return lastImageRefs
    .filter((item) => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean);
}

export function createEmptyConversationState(conversationId = '') {
  return {
    version: CONVERSATION_STATE_VERSION,
    conversationId,
    routes: {
      default: {
        previousResponseId: null,
        messages: []
      },
      advanced: {
        previousResponseId: null,
        messages: []
      }
    },
    shared: {
      lastImageRefs: []
    },
    updatedAt: null
  };
}

export function normalizeConversationState(value, conversationId = '') {
  const emptyState = createEmptyConversationState(conversationId);

  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return emptyState;
  }

  const normalizedConversationId =
    typeof value.conversationId === 'string' && value.conversationId.trim()
      ? value.conversationId.trim()
      : conversationId;

  return {
    version: CONVERSATION_STATE_VERSION,
    conversationId: normalizedConversationId,
    routes: {
      default: normalizeRouteContext(value.routes?.default),
      advanced: normalizeRouteContext(value.routes?.advanced)
    },
    shared: {
      lastImageRefs: normalizeLastImageRefs(value.shared?.lastImageRefs)
    },
    updatedAt:
      typeof value.updatedAt === 'string' && value.updatedAt.trim() ? value.updatedAt.trim() : null
  };
}

export function parseConversationStorageKey(key) {
  if (typeof key !== 'string' || !key.trim()) {
    return {
      conversationId: '',
      routeName: null
    };
  }

  const trimmed = key.trim();

  for (const routeName of CONVERSATION_ROUTE_NAMES) {
    const suffix = `:${routeName}`;

    if (trimmed.endsWith(suffix)) {
      return {
        conversationId: trimmed.slice(0, -suffix.length),
        routeName
      };
    }
  }

  return {
    conversationId: trimmed,
    routeName: null
  };
}
