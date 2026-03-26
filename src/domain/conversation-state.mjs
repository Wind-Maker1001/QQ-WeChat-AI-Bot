export const CONVERSATION_STATE_VERSION = 2;
export const CONVERSATION_ROUTE_NAMES = Object.freeze(['default', 'advanced']);
const CONVERSATION_ID_PATTERN =
  /^channel=([^|]+)\|chat=([^|]+)\|user=([^|]+)$/;

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

function serializeConversationMessages(messages) {
  return JSON.stringify(normalizeConversationMessages(messages));
}

function conversationMessagesEqual(left, right) {
  return serializeConversationMessages(left) === serializeConversationMessages(right);
}

function findConversationMessageOverlap(left, right) {
  const maxOverlap = Math.min(left.length, right.length);

  for (let overlap = maxOverlap; overlap > 0; overlap -= 1) {
    let matches = true;

    for (let index = 0; index < overlap; index += 1) {
      const leftMessage = left[left.length - overlap + index];
      const rightMessage = right[index];

      if (
        !leftMessage ||
        !rightMessage ||
        leftMessage.role !== rightMessage.role ||
        leftMessage.content !== rightMessage.content
      ) {
        matches = false;
        break;
      }
    }

    if (matches) {
      return overlap;
    }
  }

  return 0;
}

export function mergeConversationMessages(leftMessages, rightMessages, { prefer = 'left' } = {}) {
  const left = normalizeConversationMessages(leftMessages);
  const right = normalizeConversationMessages(rightMessages);

  if (left.length === 0) {
    return right;
  }

  if (right.length === 0) {
    return left;
  }

  if (conversationMessagesEqual(left, right)) {
    return left;
  }

  const leftToRightOverlap = findConversationMessageOverlap(left, right);
  const rightToLeftOverlap = findConversationMessageOverlap(right, left);
  const leftThenRight = leftToRightOverlap > 0 ? [...left, ...right.slice(leftToRightOverlap)] : null;
  const rightThenLeft = rightToLeftOverlap > 0 ? [...right, ...left.slice(rightToLeftOverlap)] : null;

  if (leftThenRight && rightThenLeft) {
    if (leftThenRight.length < rightThenLeft.length) {
      return leftThenRight;
    }

    if (rightThenLeft.length < leftThenRight.length) {
      return rightThenLeft;
    }

    return prefer === 'right' ? leftThenRight : rightThenLeft;
  }

  if (leftThenRight) {
    return leftThenRight;
  }

  if (rightThenLeft) {
    return rightThenLeft;
  }

  if (left.length === right.length) {
    return prefer === 'right' ? right : left;
  }

  return left.length > right.length ? left : right;
}

export function buildConversationSharedMessages(conversationState, preferredRoute = 'default') {
  const defaultMessages = normalizeConversationMessages(conversationState?.routes?.default?.messages);
  const advancedMessages = normalizeConversationMessages(conversationState?.routes?.advanced?.messages);

  if (preferredRoute === 'advanced') {
    return mergeConversationMessages(advancedMessages, defaultMessages, { prefer: 'left' });
  }

  return mergeConversationMessages(defaultMessages, advancedMessages, { prefer: 'left' });
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

function normalizeConversationKeyPart(value) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return String(value);
  }

  if (typeof value === 'bigint') {
    return String(value);
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    return trimmed || '';
  }

  return '';
}

function encodeConversationKeyPart(value) {
  return encodeURIComponent(normalizeConversationKeyPart(value));
}

function decodeConversationKeyPart(value) {
  if (typeof value !== 'string' || !value) {
    return '';
  }

  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export function buildConversationId({ channelId, chatId, userId } = {}) {
  const normalizedChannelId = normalizeConversationKeyPart(channelId);
  const normalizedChatId = normalizeConversationKeyPart(chatId);
  const normalizedUserId = normalizeConversationKeyPart(userId);

  if (!normalizedChannelId || !normalizedChatId || !normalizedUserId) {
    return '';
  }

  return `channel=${encodeConversationKeyPart(normalizedChannelId)}|chat=${encodeConversationKeyPart(normalizedChatId)}|user=${encodeConversationKeyPart(normalizedUserId)}`;
}

export function parseConversationId(conversationId) {
  if (typeof conversationId !== 'string' || !conversationId.trim()) {
    return {
      channelId: '',
      chatId: '',
      userId: ''
    };
  }

  const match = conversationId.trim().match(CONVERSATION_ID_PATTERN);

  if (!match) {
    return {
      channelId: '',
      chatId: '',
      userId: ''
    };
  }

  return {
    channelId: decodeConversationKeyPart(match[1]),
    chatId: decodeConversationKeyPart(match[2]),
    userId: decodeConversationKeyPart(match[3])
  };
}

export function buildLegacyConversationId({ chatId, userId } = {}) {
  const normalizedChatId = normalizeConversationKeyPart(chatId);
  const normalizedUserId = normalizeConversationKeyPart(userId);

  if (!normalizedChatId || !normalizedUserId) {
    return '';
  }

  return `${normalizedChatId}:${normalizedUserId}`;
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
