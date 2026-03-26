import { createLlmRouter } from '../adapters/llm/llm-router.mjs';
import { DEFAULT_OPENAI_BASE_URL, prepareImageInputs } from '../adapters/llm/openai-provider.mjs';
import { loadRuntimeConfig } from '../adapters/config/load-runtime-config.mjs';
import { createWechatBridgeClient } from '../adapters/wechat/bridge-client.mjs';
import { createWechatChannelPort } from '../adapters/wechat/channel-port.mjs';
import { normalizeIncomingWechatBridgeEvent } from '../adapters/wechat/normalize-event.mjs';
import { validateRuntimeConfig } from '../domain/runtime-config.mjs';
import { createSessionStore } from '../session.mjs';
import { createRuntimeConnectionManager } from './runtime-connection-manager.mjs';
import { createRuntimeConfigReloader } from './runtime-config-reloader.mjs';
import { processRuntimeMessage } from './process-runtime-message.mjs';

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

async function main() {
  let runtimeConfig = loadRuntimeConfig();
  validateRuntimeConfig(runtimeConfig, {
    validateWechatBridge: true
  });
  const sessionStore = await createSessionStore();
  const activeLocks = new Set();
  let allowedChatIds = new Set(runtimeConfig.access.allowedChatIds);
  let allowedUserIds = new Set(runtimeConfig.access.allowedUserIds);
  let bridgeConnected = false;
  let lastLlmRequest = null;
  let lastLlmFailure = null;
  let bridgeClient = null;
  let shuttingDown = false;
  const logger = {
    info: logInfo,
    error: logError
  };
let llmRouter = createLlmRouter({
  defaultRoute: runtimeConfig.openai.defaultRoute,
  advancedRoute: runtimeConfig.openai.advancedRoute,
  advancedTriggerPrefixes: runtimeConfig.openai.advancedTriggerPrefixes,
  botSystemPrompt: runtimeConfig.bot.systemPrompt,
  botPersona: runtimeConfig.bot.persona
});

  function serializeRuntimeConfig(config) {
    return JSON.stringify({
      openai: {
        defaultRoute: config.openai.defaultRoute,
        advancedRoute: config.openai.advancedRoute,
        advancedTriggerPrefixes: config.openai.advancedTriggerPrefixes
      },
      wechat: config.wechat,
      bot: config.bot,
      access: config.access,
      runtime: config.runtime,
      paths: config.paths
    });
  }

  let runtimeConfigSignature = serializeRuntimeConfig(runtimeConfig);
  const configReloader = createRuntimeConfigReloader({
    getRuntimeConfig: () => runtimeConfig,
    applyRuntimeConfig,
    logInfo,
    logError,
    logPrefix: 'wechat-runtime'
  });
  const connectionManager = createRuntimeConnectionManager({
    connectionLabel: 'wechat-bridge',
    getCurrentClient: () => bridgeClient,
    setCurrentClient: (client) => {
      bridgeClient = client;
    },
    setConnected: (connected) => {
      bridgeConnected = connected;
    },
    getReconnectDelayMs: () => runtimeConfig.runtime.reconnectDelayMs,
    getConnectionTarget: () => runtimeConfig.wechat.bridgeUrl,
    isShuttingDown: () => shuttingDown,
    connectClient: (client) => client?.connect(),
    disconnectClient: (client, reason) => client?.disconnect(1000, reason),
    logInfo,
    logError,
    reportStatus
  });

  function reportStatus() {
    if (typeof process.send === 'function') {
      process.send({
        type: 'status',
        data: {
          runtimeActive: !shuttingDown,
          runtimeReady: !shuttingDown && bridgeConnected,
          bridgeConnected,
          activeLockCount: activeLocks.size,
          lastLlmRequest,
          lastLlmFailure
        }
      });
    }
  }

  logInfo('[wechat-runtime] Worker started.');
  logInfo(
    `[wechat-runtime] Default model: ${runtimeConfig.openai.defaultRoute.model}, Base URL: ${runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}`
  );
  logInfo(
    `[wechat-runtime] Advanced model: ${runtimeConfig.openai.advancedRoute.model}, Base URL: ${runtimeConfig.openai.advancedRoute.baseURL || runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
  );

  if (!runtimeConfig.wechat.bridgeUrl) {
    logInfo('[wechat-runtime] WECHAT_BRIDGE_URL is not set; worker exits without starting.');
    return;
  }

  function normalizeIncomingEvent(packet) {
    return normalizeIncomingWechatBridgeEvent(packet, {
      prefix: runtimeConfig.wechat.botPrefix || '/ai'
    });
  }

  function rebuildDerivedRuntimeState(nextRuntimeConfig) {
  llmRouter = createLlmRouter({
    defaultRoute: nextRuntimeConfig.openai.defaultRoute,
    advancedRoute: nextRuntimeConfig.openai.advancedRoute,
    advancedTriggerPrefixes: nextRuntimeConfig.openai.advancedTriggerPrefixes,
    botSystemPrompt: nextRuntimeConfig.bot.systemPrompt,
    botPersona: nextRuntimeConfig.bot.persona
  });
    runtimeConfig = nextRuntimeConfig;
    runtimeConfigSignature = serializeRuntimeConfig(nextRuntimeConfig);
    allowedChatIds = new Set(nextRuntimeConfig.access.allowedChatIds);
    allowedUserIds = new Set(nextRuntimeConfig.access.allowedUserIds);
  }

  function createBridgeClientForCurrentConfig() {
    const client = createWechatBridgeClient({
      url: runtimeConfig.wechat.bridgeUrl,
      token: runtimeConfig.wechat.token,
      onOpen: () => connectionManager.handleOpen(client),
      onClose: (code, reason) => connectionManager.handleClose(client, code, reason),
      onError: (error) => connectionManager.handleError(client, error),
      onEvent: (packet) => {
        if (!connectionManager.isActiveClient(client)) {
          return;
        }

        const message = normalizeIncomingEvent(packet);

        void processRuntimeMessage({
          message,
          activeLocks,
          allowedChatIds,
          allowedUserIds,
          createChannelPort: () =>
            createWechatChannelPort({
              bridgeClient,
              prefix: runtimeConfig.wechat.botPrefix || '/ai'
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
          },
          concurrentRequestLabel: 'wechat request'
        });
      }
    });

    return client;
  }

  async function reconnectBridgeClient(reason) {
    connectionManager.replaceClient(createBridgeClientForCurrentConfig(), reason);
  }

  async function applyRuntimeConfig(nextRuntimeConfig, source) {
    validateRuntimeConfig(nextRuntimeConfig, {
      validateWechatBridge: true
    });
    const nextSignature = serializeRuntimeConfig(nextRuntimeConfig);

    if (nextSignature === runtimeConfigSignature) {
      logInfo(`[wechat-runtime] Config reload skipped from ${source}: no effective change.`);
      return false;
    }

    const previousConfig = runtimeConfig;
    const bridgeChanged =
      previousConfig.wechat.bridgeUrl !== nextRuntimeConfig.wechat.bridgeUrl ||
      previousConfig.wechat.token !== nextRuntimeConfig.wechat.token;

    rebuildDerivedRuntimeState(nextRuntimeConfig);
    logInfo(
      `[wechat-runtime] Config reloaded from ${source}: prefix=${runtimeConfig.wechat.botPrefix || '/ai'}, default=${runtimeConfig.openai.defaultRoute.model}/${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}, advanced=${runtimeConfig.openai.advancedRoute.model}/${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
    );

    if (bridgeChanged) {
      logInfo('[wechat-runtime] Bridge connection config changed; reconnecting client.');
      await reconnectBridgeClient('runtime config reload');
    }

    return true;
  }

  await configReloader.startWatching();
  connectionManager.attachInitialClient(createBridgeClientForCurrentConfig());
  reportStatus();

  async function shutdown(signal) {
    if (shuttingDown) {
      return;
    }

    shuttingDown = true;
    bridgeConnected = false;
    reportStatus();
    logInfo(`[wechat-runtime] Shutting down: signal=${signal}`);

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
  logError(`[wechat-runtime] Startup failed: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
});
