import { createLlmRouter } from '../adapters/llm/llm-router.mjs';
import { DEFAULT_OPENAI_BASE_URL, prepareImageInputs } from '../adapters/llm/openai-provider.mjs';
import { buildRuntimeConfigFromSettingsSnapshot } from '../adapters/config/runtime-settings-store.mjs';
import { validateRuntimeConfig } from '../domain/runtime-config.mjs';
import { processRuntimeMessage } from './process-runtime-message.mjs';

function createLoggerPrefix(workerKind) {
  return workerKind === 'wechat' ? 'wechat-runtime' : 'runtime';
}

function logRouteSummary(workerKind, runtimeConfig, logInfo) {
  const prefix = createLoggerPrefix(workerKind);
  const channelPrefix =
    workerKind === 'wechat'
      ? runtimeConfig.wechat.botPrefix || '/ai'
      : runtimeConfig.bot.prefix;

  logInfo(
    `[${prefix}] Default model: ${runtimeConfig.openai.defaultRoute.model}, Base URL: ${runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}`
  );
  logInfo(
    `[${prefix}] Advanced model: ${runtimeConfig.openai.advancedRoute.model}, Base URL: ${runtimeConfig.openai.advancedRoute.baseURL || runtimeConfig.openai.defaultRoute.baseURL || DEFAULT_OPENAI_BASE_URL}, API: ${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
  );
  logInfo(
    `[${prefix}] DeepSeek fallback: ${runtimeConfig.deepseek.fallbackEnabled ? 'enabled' : 'disabled'}, model=${runtimeConfig.deepseek.model || 'none'}, base_url=${runtimeConfig.deepseek.baseURL || 'none'}`
  );

  if (workerKind === 'wechat') {
    logInfo(
      `[${prefix}] Config ready: prefix=${channelPrefix}, default=${runtimeConfig.openai.defaultRoute.model}/${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}, advanced=${runtimeConfig.openai.advancedRoute.model}/${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
    );
    return;
  }

  logInfo(
    `[${prefix}] LLM routes ready: default=${runtimeConfig.openai.defaultRoute.model}/${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}, advanced=${runtimeConfig.openai.advancedRoute.model}/${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}, prefix=${channelPrefix}, max_output=${runtimeConfig.bot.maxOutputChars}`
  );
}

function logConfigReload(workerKind, runtimeConfig, source, logInfo) {
  const prefix = createLoggerPrefix(workerKind);

  if (workerKind === 'wechat') {
    logInfo(
      `[${prefix}] Config reloaded from ${source}: prefix=${runtimeConfig.wechat.botPrefix || '/ai'}, default=${runtimeConfig.openai.defaultRoute.model}/${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}, advanced=${runtimeConfig.openai.advancedRoute.model}/${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}`
    );
    return;
  }

  logInfo(
    `[${prefix}] Config reloaded from ${source}: default=${runtimeConfig.openai.defaultRoute.model}/${runtimeConfig.openai.defaultRoute.apiStyle || 'auto'}, advanced=${runtimeConfig.openai.advancedRoute.model}/${runtimeConfig.openai.advancedRoute.apiStyle || 'auto'}, prefix=${runtimeConfig.bot.prefix}, max_output=${runtimeConfig.bot.maxOutputChars}`
  );
}

export function createRuntimeWorkerSession({
  workerKind,
  host,
  sessionStore,
  logger,
  sendStatus
}) {
  const activeLocks = new Set();
  let initialized = false;
  let shuttingDown = false;
  let runtimeSettingsSnapshot = null;
  let runtimeConfig = null;
  let allowedChatIds = new Set();
  let allowedUserIds = new Set();
  let llmRouter = null;
  let lastLlmRequest = null;
  let lastLlmFailure = null;

  function rebuildDerivedRuntimeState(nextRuntimeConfig) {
    runtimeConfig = nextRuntimeConfig;
    allowedChatIds = new Set(nextRuntimeConfig.access.allowedChatIds);
    allowedUserIds = new Set(nextRuntimeConfig.access.allowedUserIds);
    llmRouter = createLlmRouter({
      defaultRoute: nextRuntimeConfig.openai.defaultRoute,
      advancedRoute: nextRuntimeConfig.openai.advancedRoute,
      deepseekFallback: nextRuntimeConfig.deepseek,
      advancedTriggerPrefixes: nextRuntimeConfig.openai.advancedTriggerPrefixes,
      botSystemPrompt: nextRuntimeConfig.bot.systemPrompt,
      botPersona: nextRuntimeConfig.bot.persona
    });
  }

  function reportStatus() {
    const hostStatus = host.buildStatusPayload();
    sendStatus({
      runtimeActive: initialized && !shuttingDown,
      runtimeReady: hostStatus.runtimeReady === true,
      activeLockCount: activeLocks.size,
      lastLlmRequest,
      lastLlmFailure,
      ...hostStatus
    });
  }

  async function applyRuntimeSettingsSnapshot(snapshot, source = 'config-update') {
    const nextRuntimeConfig = buildRuntimeConfigFromSettingsSnapshot({
      cwd: process.cwd(),
      snapshot
    });
    validateRuntimeConfig(nextRuntimeConfig, {
      validateWechatBridge: workerKind === 'wechat'
    });

    const previousRuntimeConfig = runtimeConfig;
    runtimeSettingsSnapshot = snapshot;
    rebuildDerivedRuntimeState(nextRuntimeConfig);
    await host.applyConfig(nextRuntimeConfig, source, previousRuntimeConfig);

    if (!initialized) {
      initialized = true;
      logger.info(`[${createLoggerPrefix(workerKind)}] Worker started.`);
      logRouteSummary(workerKind, nextRuntimeConfig, logger.info);
      logger.info(`[session] Store ready: ${sessionStore.filePath}`);
      host.connect();
    } else {
      logConfigReload(workerKind, nextRuntimeConfig, source, logger.info);
    }

    reportStatus();
  }

  async function handleIncomingPacket(packet) {
    if (!initialized || !runtimeConfig || !llmRouter) {
      return false;
    }

    const message = host.normalizeIncomingEvent(packet);

    return processRuntimeMessage({
      message,
      activeLocks,
      allowedChatIds,
      allowedUserIds,
      createChannelPort: () => host.buildChannelPort(),
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
      concurrentRequestLabel: workerKind === 'wechat' ? 'wechat request' : 'request'
    });
  }

  async function shutdown(signal) {
    if (shuttingDown) {
      return;
    }

    shuttingDown = true;
    reportStatus();
    logger.info(`[${createLoggerPrefix(workerKind)}] Shutting down: signal=${signal}`);
    host.disconnect(`shutdown:${signal}`);
  }

  return Object.freeze({
    applyRuntimeSettingsSnapshot,
    handleIncomingPacket,
    shutdown,
    reportStatus,
    getRuntimeConfig: () => runtimeConfig,
    getRuntimeSettingsSnapshot: () => runtimeSettingsSnapshot,
    isShuttingDown: () => shuttingDown
  });
}
