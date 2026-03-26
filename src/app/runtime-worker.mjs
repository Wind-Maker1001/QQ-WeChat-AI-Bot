import { createNapCatClient } from '../napcat.mjs';
import { createLlmRouter } from '../adapters/llm/llm-router.mjs';
import { DEFAULT_OPENAI_BASE_URL, prepareImageInputs } from '../adapters/llm/openai-provider.mjs';
import { loadRuntimeConfig } from '../adapters/config/load-runtime-config.mjs';
import { createNapCatChannelPort } from '../adapters/napcat/channel-port.mjs';
import { normalizeIncomingNapCatEvent } from '../adapters/napcat/normalize-event.mjs';
import { validateRuntimeConfig } from '../domain/runtime-config.mjs';
import { createSessionStore } from '../session.mjs';
import { createRuntimeConnectionManager } from './runtime-connection-manager.mjs';
import { createRuntimeConfigReloader } from './runtime-config-reloader.mjs';
import { processRuntimeMessage } from './process-runtime-message.mjs';
import { formatError } from '../utils.mjs';

let runtimeConfig = loadRuntimeConfig();
let allowedChatIds = new Set(runtimeConfig.access.allowedChatIds);
let allowedUserIds = new Set(runtimeConfig.access.allowedUserIds);
let llmRouter = createLlmRouter({
  defaultRoute: runtimeConfig.openai.defaultRoute,
  advancedRoute: runtimeConfig.openai.advancedRoute,
  advancedTriggerPrefixes: runtimeConfig.openai.advancedTriggerPrefixes,
  botSystemPrompt: runtimeConfig.bot.systemPrompt,
  botPersona: runtimeConfig.bot.persona
});
let runtimeConfigSignature = '';

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
    botSystemPrompt: nextRuntimeConfig.bot.systemPrompt,
    botPersona: nextRuntimeConfig.bot.persona
  });

  runtimeConfig = nextRuntimeConfig;
  runtimeConfigSignature = serializeRuntimeConfig(nextRuntimeConfig);
  allowedChatIds = new Set(runtimeConfig.access.allowedChatIds);
  allowedUserIds = new Set(runtimeConfig.access.allowedUserIds);
  llmRouter = nextLlmRouter;
}

runtimeConfigSignature = serializeRuntimeConfig(runtimeConfig);

async function main() {
  validateRuntimeConfig(runtimeConfig);
  const sessionStore = await createSessionStore();
  const activeLocks = new Set();
  const logger = {
    info: logInfo,
    error: logError
  };

  let napcatConnected = false;
  let napcat = null;
  let lastLlmRequest = null;
  let lastLlmFailure = null;
  let shuttingDown = false;
  const configReloader = createRuntimeConfigReloader({
    getRuntimeConfig: () => runtimeConfig,
    applyRuntimeConfig,
    logInfo,
    logError,
    logPrefix: 'runtime'
  });
  const connectionManager = createRuntimeConnectionManager({
    connectionLabel: 'napcat',
    errorLabel: 'WebSocket error',
    getCurrentClient: () => napcat,
    setCurrentClient: (client) => {
      napcat = client;
    },
    setConnected: (connected) => {
      napcatConnected = connected;
    },
    getReconnectDelayMs: () => runtimeConfig.runtime.reconnectDelayMs,
    getConnectionTarget: () => runtimeConfig.napcat.wsUrl,
    isShuttingDown: () => shuttingDown,
    connectClient: (client) => client?.connect(),
    disconnectClient: (client, reason) => client?.disconnect(1000, reason),
    canDisconnectClient: (client) => Boolean(client?.getSocket?.()),
    logInfo,
    logError,
    reportStatus
  });

  function reportStatus() {
    sendStatus({
      runtimeActive: !shuttingDown,
      runtimeReady: !shuttingDown && napcatConnected,
      napcatConnected,
      activeLockCount: activeLocks.size,
      lastLlmRequest,
      lastLlmFailure
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
        if (!connectionManager.isActiveClient(client)) {
          return;
        }

        void handleEvent(event);
      },
      onOpen: () => connectionManager.handleOpen(client),
      onClose: (code, reason) => connectionManager.handleClose(client, code, reason),
      onError: (error) => connectionManager.handleError(client, error)
    });

    return client;
  }

  async function reconnectNapcatClient(reason) {
    connectionManager.replaceClient(createNapcatClientForCurrentConfig(), reason);
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

  async function handleEvent(event) {
    const message = normalizeIncomingEvent(event);
    await processRuntimeMessage({
      message,
      activeLocks,
      allowedChatIds,
      allowedUserIds,
      createChannelPort: () =>
        createNapCatChannelPort({
          napcatClient: napcat,
          prefix: runtimeConfig.bot.prefix
        }),
      sessionStore,
      llmRouter,
      logger,
      prepareImageInputs,
      imageCacheDir: runtimeConfig.paths.imageCacheDir,
      maxOutputChars: runtimeConfig.bot.maxOutputChars,
      reportStatus,
      onReplyTelemetry: (telemetry) => {
        lastLlmRequest = telemetry;
        lastLlmFailure = null;
        reportStatus();
      },
      onReplyFailureTelemetry: (telemetry) => {
        lastLlmFailure = telemetry;
        reportStatus();
      }
    });
  }

  await configReloader.startWatching();
  connectionManager.attachInitialClient(createNapcatClientForCurrentConfig());

  async function shutdown(signal) {
    if (shuttingDown) {
      return;
    }

    shuttingDown = true;
    napcatConnected = false;
    reportStatus();
    logInfo(`[runtime] Shutting down: signal=${signal}`);

    configReloader.stopWatching();
    connectionManager.shutdownCurrentClient(`shutdown:${signal}`);

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
