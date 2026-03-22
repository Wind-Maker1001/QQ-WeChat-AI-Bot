import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
  CONVERSATION_ROUTE_NAMES,
  createEmptyConversationState,
  normalizeConversationMessages,
  normalizeConversationState,
  parseConversationStorageKey
} from './domain/conversation-state.mjs';
import { safeJsonParse } from './utils.mjs';

const currentFilePath = fileURLToPath(import.meta.url);
const currentDirPath = path.dirname(currentFilePath);
const dataDirPath = path.resolve(currentDirPath, '..', 'data');
const sessionFilePath = path.join(dataDirPath, 'sessions.json');

function normalizeLegacyRouteState(routeState) {
  if (!routeState || typeof routeState !== 'object' || Array.isArray(routeState)) {
    return {
      previousResponseId: null
    };
  }

  return {
    previousResponseId:
      typeof routeState.previousResponseId === 'string' && routeState.previousResponseId.trim()
        ? routeState.previousResponseId.trim()
        : null
  };
}

function normalizeLegacySessionState(value) {
  if (typeof value === 'string') {
    return {
      routeStates: {
        default: {
          previousResponseId: null
        },
        advanced: {
          previousResponseId: value.trim() || null
        }
      },
      messages: [],
      lastImageRefs: []
    };
  }

  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }

  return {
    routeStates: {
      default: normalizeLegacyRouteState(value.routeStates?.default),
      advanced:
        'routeStates' in value
          ? normalizeLegacyRouteState(value.routeStates?.advanced)
          : normalizeLegacyRouteState({
              previousResponseId: value.previousResponseId
            })
    },
    messages: normalizeConversationMessages(value.sharedMessages ?? value.chatMessages ?? []),
    lastImageRefs: Array.isArray(value.lastImageRefs)
      ? value.lastImageRefs.filter((item) => typeof item === 'string' && item.trim())
      : []
  };
}

function mergeMessages(currentMessages, incomingMessages) {
  if (!Array.isArray(incomingMessages) || incomingMessages.length === 0) {
    return currentMessages;
  }

  if (!Array.isArray(currentMessages) || currentMessages.length === 0) {
    return incomingMessages;
  }

  const currentSerialized = JSON.stringify(currentMessages);
  const incomingSerialized = JSON.stringify(incomingMessages);

  return currentSerialized === incomingSerialized ? currentMessages : incomingMessages;
}

function inferLegacyMessagesRoute(legacyState, routeHint, currentConversation) {
  if (routeHint) {
    return routeHint;
  }

  const hasDefaultResponseId = Boolean(legacyState.routeStates.default.previousResponseId);
  const hasAdvancedResponseId = Boolean(legacyState.routeStates.advanced.previousResponseId);

  if (hasDefaultResponseId && !hasAdvancedResponseId) {
    return 'default';
  }

  if (!hasDefaultResponseId && hasAdvancedResponseId) {
    return 'advanced';
  }

  if (currentConversation.routes.default.messages.length > 0 && currentConversation.routes.advanced.messages.length === 0) {
    return 'default';
  }

  if (currentConversation.routes.advanced.messages.length > 0 && currentConversation.routes.default.messages.length === 0) {
    return 'advanced';
  }

  return 'default';
}

function mergeConversationState(currentConversation, incomingConversation) {
  const nextConversation = normalizeConversationState(
    currentConversation,
    incomingConversation.conversationId
  );

  for (const routeName of CONVERSATION_ROUTE_NAMES) {
    const currentRoute = nextConversation.routes[routeName];
    const incomingRoute = incomingConversation.routes[routeName];

    nextConversation.routes[routeName] = {
      previousResponseId:
        incomingRoute.previousResponseId ?? currentRoute.previousResponseId ?? null,
      messages: mergeMessages(currentRoute.messages, incomingRoute.messages)
    };
  }

  if (incomingConversation.shared.lastImageRefs.length > 0) {
    nextConversation.shared.lastImageRefs = incomingConversation.shared.lastImageRefs;
  }

  nextConversation.updatedAt = incomingConversation.updatedAt ?? nextConversation.updatedAt ?? null;
  return nextConversation;
}

function mergeLegacyIntoConversation(currentConversation, legacyState, routeHint = null) {
  const nextConversation = normalizeConversationState(
    currentConversation,
    currentConversation.conversationId
  );

  for (const routeName of CONVERSATION_ROUTE_NAMES) {
    if (!legacyState.routeStates[routeName].previousResponseId) {
      continue;
    }

    if (routeHint === routeName || !nextConversation.routes[routeName].previousResponseId) {
      nextConversation.routes[routeName].previousResponseId =
        legacyState.routeStates[routeName].previousResponseId;
    }
  }

  if (legacyState.messages.length > 0) {
    const targetRoute = inferLegacyMessagesRoute(legacyState, routeHint, nextConversation);
    nextConversation.routes[targetRoute].messages = mergeMessages(
      nextConversation.routes[targetRoute].messages,
      legacyState.messages
    );
  }

  if (legacyState.lastImageRefs.length > 0) {
    nextConversation.shared.lastImageRefs = legacyState.lastImageRefs;
  }

  return nextConversation;
}

export async function createSessionStore() {
  const conversations = new Map();
  let saveQueue = Promise.resolve();
  const invalidJson = Symbol('invalid_json');
  let migrationApplied = false;

  await fs.mkdir(dataDirPath, { recursive: true });

  try {
    const raw = await fs.readFile(sessionFilePath, 'utf8');
    const parsed = safeJsonParse(raw, invalidJson);

    if (parsed === invalidJson) {
      console.warn(`[session] Invalid JSON in ${sessionFilePath}; starting with empty store.`);
    } else if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      for (const [storageKey, value] of Object.entries(parsed)) {
        const { conversationId, routeName } = parseConversationStorageKey(storageKey);

        if (!conversationId) {
          migrationApplied = true;
          continue;
        }

        if (routeName !== null) {
          migrationApplied = true;
        }

        if (value && typeof value === 'object' && !Array.isArray(value) && value.version === 2) {
          const normalizedConversation = normalizeConversationState(value, conversationId);
          const existingConversation =
            conversations.get(conversationId) ?? createEmptyConversationState(conversationId);
          conversations.set(
            conversationId,
            mergeConversationState(existingConversation, normalizedConversation)
          );
          continue;
        }

        const legacyState = normalizeLegacySessionState(value);

        if (!legacyState) {
          migrationApplied = true;
          continue;
        }

        migrationApplied = true;
        const existingConversation =
          conversations.get(conversationId) ?? createEmptyConversationState(conversationId);
        conversations.set(
          conversationId,
          mergeLegacyIntoConversation(existingConversation, legacyState, routeName)
        );
      }
    } else {
      console.warn(`[session] Unexpected content in ${sessionFilePath}; starting with empty store.`);
    }
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      await fs.writeFile(sessionFilePath, '{}\n', 'utf8');
    } else {
      console.error(`[session] Failed to read ${sessionFilePath}:`, error);
    }
  }

  async function saveConversations() {
    const payload = JSON.stringify(Object.fromEntries(conversations), null, 2);

    saveQueue = saveQueue
      .catch(() => {})
      .then(() => fs.writeFile(sessionFilePath, `${payload}\n`, 'utf8'));

    return saveQueue;
  }

  function getConversation(key) {
    const { conversationId } = parseConversationStorageKey(key);
    const normalizedKey = conversationId || key;

    return normalizeConversationState(
      conversations.get(normalizedKey) ?? createEmptyConversationState(normalizedKey),
      normalizedKey
    );
  }

  async function setConversation(key, conversationState) {
    const { conversationId } = parseConversationStorageKey(key);
    const normalizedKey = conversationId || key;

    if (typeof normalizedKey !== 'string' || !normalizedKey) {
      throw new Error('Conversation key is required.');
    }

    const normalizedConversation = normalizeConversationState(conversationState, normalizedKey);
    normalizedConversation.updatedAt = new Date().toISOString();
    conversations.set(normalizedKey, normalizedConversation);
    await saveConversations();
  }

  if (migrationApplied) {
    await saveConversations();
  }

  return {
    filePath: sessionFilePath,
    getConversation,
    setConversation,
    saveConversations
  };
}
