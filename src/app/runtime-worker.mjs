import fs from 'node:fs';

import { createNapCatClient } from '../napcat.mjs';
import { createLlmRouter } from '../adapters/llm/llm-router.mjs';
import { DEFAULT_OPENAI_BASE_URL, prepareImageInputs } from '../adapters/llm/openai-provider.mjs';
import { loadRuntimeConfig } from '../adapters/config/load-runtime-config.mjs';
import { readControlConfig } from '../adapters/config/control-config-file.mjs';
import { normalizeIncomingNapCatEvent } from '../adapters/napcat/normalize-event.mjs';
import { handleIncomingMessage } from './handle-incoming-message.mjs';
import { validateRuntimeConfig } from '../domain/runtime-config.mjs';
import { createSessionStore } from '../session.mjs';
import { formatError } from '../utils.mjs';

let runtimeConfig = loadRuntimeConfig();
let allowedGroupIds = new Set(runtimeConfig.access.allowedGroupIds);
let allowedUserIds = new Set(runtimeConfig.access.allowedUserIds);
let llmRouter = createLlmRouter({
  defaultRoute: runtimeConfig.openai.defaultRoute,
  advancedRoute: runtimeConfig.openai.advancedRoute,
  advancedTriggerPrefixes: runtimeConfig.openai.advancedTriggerPrefixes,
  botPersona: runtimeConfig.bot.persona
});
let runtimeConfigSignature = JSON.stringify(runtimeConfig);

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

function logRuntimeConfigSummary(prefix, config) {
  logInfo(
    `${prefix} default=${config.openai.defaultRoute.model}/${config.openai.defaultRoute.apiStyle || 'auto'}, advanced=${config.openai.advancedRoute.model}/${config.openai.advancedRoute.apiStyle || 'auto'}, prefix=${config.bot.prefix}, max_output=${config.bot.maxOutputChars}`
  );
}

function serializeRuntimeConfig(config) {
  return JSON.stringify({
    openai: {
      defaultRoute: config.openai.defaultRoute,
      advancedRoute: config.openai.advancedRoute,
      advancedTriggerPrefixes: config.openai.advancedTriggerPrefixes
    },
    napcat: config.napcat,
    bot: config.bot,
    access: config.access,
    runtime: config.runtime,
    paths: config.paths
  });
}

function sendStatus(status) {
  if (typeof process.send === 'function') {
    process.send({
      type: 'status',
      data: status
    });
  }
}

function rebuildDerivedRuntimeState(nextRuntimeConfig) {
  const nextLlmRouter = createLlmRouter({
    defaultRoute: nextRuntimeConfig.openai.defaultRoute,
    advancedRoute: nextRuntimeConfig.openai.advancedRoute,
    advancedTriggerPrefixes: nextRuntimeConfig.openai.advancedTriggerPrefixes,
    botPersona: nextRuntimeConfig.bot.persona
  });

  runtimeConfig = nextRuntimeConfig;
  runtimeConfigSignature = serializeRuntimeConfig(nextRuntimeConfig);
  allowedGroupIds = new Set(runtimeConfig.access.allowedGroupIds);
  allowedUserIds = new Set(runtimeConfig.access.allowedUserIds);
  llmRouter = nextLlmRouter;
}

async function main() {
  validateRuntimeConfig(runtimeConfig);
  const sessionStore = await createSessionStore();
  const activeLocks = new Set();
  const logger = {
    info: logInfo,
    error: logError
  };

  let reconnectTimer = null;
  let napcatConnected = false;
  let napcat = null;
  let suppressedReconnectClient = null;
  let envWatcher = null;
  let envReloadTimer = null;
  let shuttingDown = false;

  function reportStatus() {
    sendStatus({
      runtimeActive: !shuttingDown,
      napcatConnected,
      activeLockCount: activeLocks.size
    });
  }

  logInfo('[runtime] Worker started.');
  logInfo(
    `[runtime] Default model: ${runtimeConfig.openai.defaultRoute.model}, Base URL: ${runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}`
  );
  logInfo(
    `[runtime] Advanced model: ${runtimeConfig.openai.advancedRoute.model}, Base URL: ${runtimeConfig.openai.advancedRoute.baseURL || runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
  );
  logRuntimeConfigSummary('[runtime] LLM routes ready:', runtimeConfig);
  logInfo(`[session] Store ready: ${sessionStore.filePath}`);
  reportStatus();

  function normalizeIncomingEvent(event) {
    return normalizeIncomingNapCatEvent(event, {
      prefix: runtimeConfig.bot.prefix
    });
  }

  function createNapcatClientForCurrentConfig() {
    const client = createNapCatClient({
      url: runtimeConfig.napcat.wsUrl,
      token: runtimeConfig.napcat.token,
      onEvent: (event) => {
        if (napcat !== client || shuttingDown) {
          return;
        }

        void handleEvent(event);
      },
      onOpen: () => {
        if (napcat !== client || shuttingDown) {
          return;
        }

        if (reconnectTimer) {
          clearTimeout(reconnectTimer);
          reconnectTimer = null;
        }

        napcatConnected = true;
        logInfo(`[napcat] Connected: ${runtimeConfig.napcat.wsUrl}`);
        reportStatus();
      },
      onClose: (code, reason) => {
        if (client === suppressedReconnectClient) {
          suppressedReconnectClient = null;
          return;
        }

        if (napcat !== client || shuttingDown) {
          return;
        }

        napcatConnected = false;
        logInfo(`[napcat] Closed: code=${code}, reason=${reason || 'none'}`);
        reportStatus();
        scheduleReconnect();
      },
      onError: (error) => {
        if (napcat !== client || shuttingDown) {
          return;
        }

        logError(`[napcat] WebSocket error: ${formatError(error)}`);
      }
    });

    return client;
  }

  function scheduleReconnect() {
    if (reconnectTimer || shuttingDown) {
      return;
    }

    logInfo(`[napcat] Reconnecting in ${runtimeConfig.runtime.reconnectDelayMs / 1000}s.`);
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      logInfo('[napcat] Reconnecting now...');
      napcat?.connect();
    }, runtimeConfig.runtime.reconnectDelayMs);
  }

  async function reconnectNapcatClient(reason) {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    const previousClient = napcat;
    napcat = createNapcatClientForCurrentConfig();
    napcatConnected = false;
    reportStatus();

    if (previousClient?.getSocket()) {
      suppressedReconnectClient = previousClient;
      previousClient.disconnect(1000, reason);
    }

    napcat.connect();
  }

  async function applyRuntimeConfig(nextRuntimeConfig, source) {
    validateRuntimeConfig(nextRuntimeConfig);
    const nextSignature = serializeRuntimeConfig(nextRuntimeConfig);

    if (nextSignature === runtimeConfigSignature) {
      logInfo(`[runtime] Config reload skipped from ${source}: no effective change.`);
      return false;
    }

    const previousConfig = runtimeConfig;
    const napcatChanged =
      previousConfig.napcat.wsUrl !== nextRuntimeConfig.napcat.wsUrl ||
      previousConfig.napcat.token !== nextRuntimeConfig.napcat.token;

    rebuildDerivedRuntimeState(nextRuntimeConfig);
    logRuntimeConfigSummary(`[runtime] Config reloaded from ${source}:`, runtimeConfig);

    if (napcatChanged) {
      logInfo('[runtime] NapCat connection config changed; reconnecting client.');
      await reconnectNapcatClient('runtime config reload');
    }

    return true;
  }

  async function reloadRuntimeConfigFromDisk(source) {
    const result = await readControlConfig({
      cwd: process.cwd(),
      runtimeConfig
    });
    const nextRuntimeConfig = loadRuntimeConfig({
      cwd: process.cwd(),
      env: result.envValues,
      loadDotenv: false
    });

    return applyRuntimeConfig(nextRuntimeConfig, source);
  }

  function scheduleEnvReload(source) {
    if (envReloadTimer) {
      clearTimeout(envReloadTimer);
    }

    envReloadTimer = setTimeout(() => {
      envReloadTimer = null;
      void reloadRuntimeConfigFromDisk(source).catch((error) => {
        logError(`[runtime] Config reload from ${source} failed: ${formatError(error)}`);
      });
    }, 250);
  }

  async function handleEvent(event) {
    const message = normalizeIncomingEvent(event);

    if (!message?.triggered) {
      return;
    }

    const groupId = message.groupId;
    const userId = message.userId;

    if (groupId === null || userId === null) {
      return;
    }

    const groupIdText = String(groupId);
    const userIdText = String(userId);

    if (allowedGroupIds.size > 0 && !allowedGroupIds.has(groupIdText)) {
      logInfo(`[filter] Ignored group not in allowlist: group_id=${groupIdText}, user_id=${userIdText}`);
      return;
    }

    if (allowedUserIds.size > 0 && !allowedUserIds.has(userIdText)) {
      logInfo(`[filter] Ignored user not in allowlist: group_id=${groupIdText}, user_id=${userIdText}`);
      return;
    }

    const lockKey = `${groupId}:${userId}`;

    if (activeLocks.has(lockKey)) {
      logInfo(`[lock] Ignored concurrent request: group_id=${groupId}, user_id=${userId}`);
      return;
    }

    activeLocks.add(lockKey);
    reportStatus();

    try {
      await handleIncomingMessage({
        message,
        sessionStore,
        llmRouter,
        napcat,
        logger,
        normalizeIncomingEvent,
        prepareImageInputs,
        imageCacheDir: runtimeConfig.paths.imageCacheDir,
        maxOutputChars: runtimeConfig.bot.maxOutputChars
      });
    } finally {
      activeLocks.delete(lockKey);
      reportStatus();
    }
  }

  const initialControlConfig = await readControlConfig({
    cwd: process.cwd(),
    runtimeConfig
  });
  envWatcher = fs.watch(initialControlConfig.envPath, () => {
    if (!shuttingDown) {
      scheduleEnvReload('env-watch');
    }
  });
  logInfo(`[runtime] Watching config file: ${initialControlConfig.envPath}`);

  napcat = createNapcatClientForCurrentConfig();
  napcat.connect();

  async function shutdown(signal) {
    if (shuttingDown) {
      return;
    }

    shuttingDown = true;
    napcatConnected = false;
    reportStatus();
    logInfo(`[runtime] Shutting down: signal=${signal}`);

    if (envReloadTimer) {
      clearTimeout(envReloadTimer);
      envReloadTimer = null;
    }

    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    envWatcher?.close();

    if (napcat) {
      suppressedReconnectClient = napcat;
      napcat.disconnect(1000, `shutdown:${signal}`);
    }

    setTimeout(() => {
      process.exit(0);
    }, 100);
  }

  process.on('SIGINT', () => {
    void shutdown('SIGINT');
  });
  process.on('SIGTERM', () => {
    void shutdown('SIGTERM');
  });
  process.on('disconnect', () => {
    void shutdown('disconnect');
  });
}

main().catch((error) => {
  logError(`[fatal] Runtime worker failed: ${formatError(error)}`);
  process.exit(1);
});
