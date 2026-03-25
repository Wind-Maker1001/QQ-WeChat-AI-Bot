import fs from 'node:fs/promises';
import path from 'node:path';

import {
  buildLegacyConversationId,
  parseConversationId,
  CONVERSATION_ROUTE_NAMES,
  createEmptyConversationState,
  normalizeConversationMessages,
  normalizeConversationState,
  parseConversationStorageKey
} from './domain/conversation-state.mjs';
import { safeJsonParse } from './utils.mjs';

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

function mergeConversationMaps(targetConversations, sourceConversations) {
  for (const [conversationId, conversationState] of sourceConversations.entries()) {
    const existingConversation =
      targetConversations.get(conversationId) ?? createEmptyConversationState(conversationId);

    targetConversations.set(
      conversationId,
      mergeConversationState(existingConversation, conversationState)
    );
  }

  return targetConversations;
}

function hydrateConversationsFromParsed(parsed, conversations) {
  let migrationApplied = false;

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      conversations,
      migrationApplied
    };
  }

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

  return {
    conversations,
    migrationApplied
  };
}

async function loadConversationsFromDisk(sessionFilePath, invalidJson) {
  const conversations = new Map();
  let migrationApplied = false;

  try {
    const raw = await fs.readFile(sessionFilePath, 'utf8');
    const parsed = safeJsonParse(raw, invalidJson);

    if (parsed === invalidJson) {
      console.warn(`[session] Invalid JSON in ${sessionFilePath}; starting with empty store.`);
      return {
        conversations,
        migrationApplied
      };
    }

    const hydrationResult = hydrateConversationsFromParsed(parsed, conversations);
    migrationApplied = hydrationResult.migrationApplied;
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      console.error(`[session] Failed to read ${sessionFilePath}:`, error);
    }
  }

  return {
    conversations,
    migrationApplied
  };
}

const SESSION_LOCK_TIMEOUT_MS = 5000;
const STALE_SESSION_LOCK_AGE_MS = 15000;

function buildSessionLockPayload() {
  return JSON.stringify(
    {
      pid: process.pid,
      acquiredAt: new Date().toISOString()
    },
    null,
    2
  );
}

function parseSessionLockPayload(raw) {
  const parsed = safeJsonParse(raw, null);

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return {
      pid: null,
      acquiredAt: null
    };
  }

  const pid = Number.isInteger(parsed.pid) && parsed.pid > 0 ? parsed.pid : null;
  const acquiredAt =
    typeof parsed.acquiredAt === 'string' && parsed.acquiredAt.trim() ? parsed.acquiredAt : null;

  return {
    pid,
    acquiredAt
  };
}

function isProcessAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function withSessionFileLock(lockFilePath, task, timeoutMs = SESSION_LOCK_TIMEOUT_MS) {
  const startedAt = Date.now();

  while (true) {
    try {
      const lockHandle = await fs.open(lockFilePath, 'wx');

      try {
        await lockHandle.writeFile(`${buildSessionLockPayload()}\n`, 'utf8');
        return await task();
      } finally {
        await lockHandle.close();
        await fs.rm(lockFilePath, { force: true });
      }
    } catch (error) {
      if (error?.code !== 'EEXIST') {
        throw error;
      }

      try {
        const rawLockFile = await fs.readFile(lockFilePath, 'utf8');
        const stats = await fs.stat(lockFilePath);
        const lockAgeMs = Date.now() - stats.mtimeMs;
        const lockState = parseSessionLockPayload(rawLockFile);
        const ownerAlive = isProcessAlive(lockState.pid);

        if (lockAgeMs >= STALE_SESSION_LOCK_AGE_MS && !ownerAlive) {
          await fs.rm(lockFilePath, { force: true });
          console.warn(
            `[session] Removed stale lock file: ${lockFilePath} age_ms=${Math.round(lockAgeMs)} owner_pid=${lockState.pid ?? 'unknown'}`
          );
          continue;
        }
      } catch (statError) {
        if (statError?.code === 'ENOENT') {
          continue;
        }

        throw statError;
      }

      if (Date.now() - startedAt >= timeoutMs) {
        throw new Error(`Timed out waiting for session file lock: ${lockFilePath}`);
      }

      await new Promise((resolve) => setTimeout(resolve, 25));
    }
  }
}

function resolveConversationKeys(key) {
  const { conversationId } = parseConversationStorageKey(key);
  const normalizedKey = conversationId || key;
  const parsedConversationId = parseConversationId(normalizedKey);
  const legacyKey =
    parsedConversationId.channelId && parsedConversationId.chatId && parsedConversationId.userId
      ? buildLegacyConversationId({
          chatId: parsedConversationId.chatId,
          userId: parsedConversationId.userId
        })
      : '';

  return {
    normalizedKey,
    legacyKey: legacyKey && legacyKey !== normalizedKey ? legacyKey : ''
  };
}

export async function createSessionStore({
  cwd = process.cwd(),
  dataDirPath = path.resolve(cwd, 'data'),
  sessionFilePath = path.join(dataDirPath, 'sessions.json')
} = {}) {
  const conversations = new Map();
  const sessionLockFilePath = `${sessionFilePath}.lock`;
  const sessionTempFilePath = `${sessionFilePath}.tmp`;
  let saveQueue = Promise.resolve();
  const invalidJson = Symbol('invalid_json');
  let migrationApplied = false;

  await fs.mkdir(dataDirPath, { recursive: true });

  const loadResult = await loadConversationsFromDisk(sessionFilePath, invalidJson);
  mergeConversationMaps(conversations, loadResult.conversations);
  migrationApplied = loadResult.migrationApplied;

  if ((await fs.stat(sessionFilePath).catch(() => null)) === null) {
    await fs.writeFile(sessionFilePath, '{}\n', 'utf8');
  }

  async function saveConversations() {
    saveQueue = saveQueue
      .catch(() => {})
      .then(() =>
        withSessionFileLock(sessionLockFilePath, async () => {
          const diskResult = await loadConversationsFromDisk(sessionFilePath, invalidJson);
          const mergedConversations = mergeConversationMaps(
            diskResult.conversations,
            conversations
          );
          const payload = JSON.stringify(Object.fromEntries(mergedConversations), null, 2);

          conversations.clear();
          mergeConversationMaps(conversations, mergedConversations);

          await fs.writeFile(sessionTempFilePath, `${payload}\n`, 'utf8');
          await fs.rename(sessionTempFilePath, sessionFilePath);
        })
      );

    return saveQueue;
  }

  function getConversation(key) {
    const { normalizedKey, legacyKey } = resolveConversationKeys(key);
    const lookupKey =
      conversations.has(normalizedKey) || !legacyKey || !conversations.has(legacyKey)
        ? normalizedKey
        : legacyKey;

    return normalizeConversationState(
      conversations.get(lookupKey) ?? createEmptyConversationState(normalizedKey),
      normalizedKey
    );
  }

  async function setConversation(key, conversationState) {
    const { normalizedKey, legacyKey } = resolveConversationKeys(key);

    if (typeof normalizedKey !== 'string' || !normalizedKey) {
      throw new Error('Conversation key is required.');
    }

    const normalizedConversation = normalizeConversationState(conversationState, normalizedKey);
    normalizedConversation.updatedAt = new Date().toISOString();
    conversations.set(normalizedKey, normalizedConversation);

    if (legacyKey && conversations.has(legacyKey)) {
      conversations.delete(legacyKey);
    }

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
