import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { buildRuntimeProcessEnv } from './adapters/config/control-config-file.mjs';
import { readRuntimeSettingsFromDisk } from './adapters/config/control-config-file.mjs';
import {
  createControlApiServer,
  DEFAULT_CONTROL_API_HOST,
  DEFAULT_CONTROL_API_PORT,
  resolveDefaultControlApiHost,
  resolveDefaultControlApiPort
} from './app/control-api.mjs';
import { createSupervisorRuntimeController } from './app/supervisor-runtime-controller.mjs';
import {
  applyQqWorkerMessage,
  applyWechatWorkerMessage,
  attachQqWorker,
  attachWechatWorker,
  buildSupervisorStatusPayload,
  clearQqWorker,
  clearWechatWorker,
  createInitialRuntimeStatus
} from './app/supervisor-runtime-state.mjs';
import { createSupervisorWorkerSlot } from './app/supervisor-worker-slot.mjs';
import { formatError } from './utils.mjs';

const currentFilePath = fileURLToPath(import.meta.url);
const currentDirPath = path.dirname(currentFilePath);
const WORKER_ENTRY = path.resolve(currentDirPath, 'app', 'runtime-worker.mjs');
const WORKER_RESTART_DELAY_MS = 1000;
const BOOT_FAILURE_WINDOW_MS = 5000;
const MAX_CONSECUTIVE_BOOT_FAILURES = 3;

function normalizeDecisionSummary(summary) {
  if (!summary || typeof summary !== 'object') {
    return null;
  }

  const trigger = summary.trigger && typeof summary.trigger === 'object'
    ? {
        kind: typeof summary.trigger.kind === 'string' ? summary.trigger.kind : '',
        matchedPrefix:
          typeof summary.trigger.matchedPrefix === 'string' ? summary.trigger.matchedPrefix : ''
      }
    : null;
  const reasonGroups = summary.reasonGroups && typeof summary.reasonGroups === 'object'
    ? {
        triggerReasons: Array.isArray(summary.reasonGroups.triggerReasons)
          ? summary.reasonGroups.triggerReasons.filter((tag) => typeof tag === 'string')
          : [],
        capabilityReasons: Array.isArray(summary.reasonGroups.capabilityReasons)
          ? summary.reasonGroups.capabilityReasons.filter((tag) => typeof tag === 'string')
          : [],
        upgradeReasons: Array.isArray(summary.reasonGroups.upgradeReasons)
          ? summary.reasonGroups.upgradeReasons.filter((tag) => typeof tag === 'string')
          : []
      }
    : null;
  const requestedCapabilities =
    summary.requestedCapabilities && typeof summary.requestedCapabilities === 'object'
      ? {
          reasoningEffort:
            typeof summary.requestedCapabilities.reasoningEffort === 'string'
              ? summary.requestedCapabilities.reasoningEffort
              : '',
          textVerbosity:
            typeof summary.requestedCapabilities.textVerbosity === 'string'
              ? summary.requestedCapabilities.textVerbosity
              : '',
          enableWebSearch:
            typeof summary.requestedCapabilities.enableWebSearch === 'boolean'
              ? summary.requestedCapabilities.enableWebSearch
              : false,
          enableCodeInterpreter:
            typeof summary.requestedCapabilities.enableCodeInterpreter === 'boolean'
              ? summary.requestedCapabilities.enableCodeInterpreter
              : false,
          needsResponsesCapabilities:
            typeof summary.requestedCapabilities.needsResponsesCapabilities === 'boolean'
              ? summary.requestedCapabilities.needsResponsesCapabilities
              : false
        }
      : null;

  return {
    trigger,
    reasonTags: Array.isArray(summary.reasonTags)
      ? summary.reasonTags.filter((tag) => typeof tag === 'string')
      : [],
    reasonGroups,
    requestedCapabilities,
    routeReason: typeof summary.routeReason === 'string' ? summary.routeReason : '',
    matchedPrefix: typeof summary.matchedPrefix === 'string' ? summary.matchedPrefix : ''
  };
}

function normalizeLlmRequestStatus(status) {
  if (!status || typeof status !== 'object') {
    return null;
  }

  return {
    capturedAt: typeof status.capturedAt === 'string' ? status.capturedAt : '',
    channelId: typeof status.channelId === 'string' ? status.channelId : '',
    route: typeof status.route === 'string' ? status.route : '',
    routeReason: typeof status.routeReason === 'string' ? status.routeReason : '',
    matchedPrefix: typeof status.matchedPrefix === 'string' ? status.matchedPrefix : '',
    configuredModel: typeof status.configuredModel === 'string' ? status.configuredModel : '',
    model: typeof status.model === 'string' ? status.model : '',
    configuredApiStyle:
      typeof status.configuredApiStyle === 'string' ? status.configuredApiStyle : '',
    effectiveApiStyle:
      typeof status.effectiveApiStyle === 'string' ? status.effectiveApiStyle : '',
    configuredReasoningEffort:
      typeof status.configuredReasoningEffort === 'string' ? status.configuredReasoningEffort : '',
    effectiveReasoningEffort:
      typeof status.effectiveReasoningEffort === 'string' ? status.effectiveReasoningEffort : '',
    configuredTextVerbosity:
      typeof status.configuredTextVerbosity === 'string' ? status.configuredTextVerbosity : '',
    effectiveTextVerbosity:
      typeof status.effectiveTextVerbosity === 'string' ? status.effectiveTextVerbosity : '',
    configuredTools: Array.isArray(status.configuredTools)
      ? status.configuredTools.filter((tool) => typeof tool === 'string')
      : [],
    effectiveTools: Array.isArray(status.effectiveTools)
      ? status.effectiveTools.filter((tool) => typeof tool === 'string')
      : [],
    executionKind: typeof status.executionKind === 'string' ? status.executionKind : '',
    executionSummary: typeof status.executionSummary === 'string' ? status.executionSummary : '',
    executionProjection: normalizeExecutionProjection(status.executionProjection),
    imageCount: typeof status.imageCount === 'number' ? status.imageCount : 0,
    decisionSummary: normalizeDecisionSummary(status.decisionSummary),
    chatId: typeof status.chatId === 'string' ? status.chatId : '',
    userId: typeof status.userId === 'string' ? status.userId : '',
    responseId: typeof status.responseId === 'string' ? status.responseId : ''
  };
}

function normalizeLlmFailureStatus(status) {
  if (!status || typeof status !== 'object') {
    return null;
  }

  return {
    capturedAt: typeof status.capturedAt === 'string' ? status.capturedAt : '',
    channelId: typeof status.channelId === 'string' ? status.channelId : '',
    route: typeof status.route === 'string' ? status.route : '',
    routeReason: typeof status.routeReason === 'string' ? status.routeReason : '',
    matchedPrefix: typeof status.matchedPrefix === 'string' ? status.matchedPrefix : '',
    decisionSummary: normalizeDecisionSummary(status.decisionSummary),
    executionKind: typeof status.executionKind === 'string' ? status.executionKind : '',
    executionSummary: typeof status.executionSummary === 'string' ? status.executionSummary : '',
    executionProjection: normalizeExecutionProjection(status.executionProjection),
    chatId: typeof status.chatId === 'string' ? status.chatId : '',
    userId: typeof status.userId === 'string' ? status.userId : '',
    error: typeof status.error === 'string' ? status.error : ''
  };
}

function normalizeExecutionProjection(projection) {
  if (!projection || typeof projection !== 'object') {
    return null;
  }

  return {
    kind: typeof projection.kind === 'string' ? projection.kind : '',
    summary: typeof projection.summary === 'string' ? projection.summary : '',
    stages: Array.isArray(projection.stages)
      ? projection.stages.filter((stage) => typeof stage === 'string')
      : [],
    failedStage: typeof projection.failedStage === 'string' ? projection.failedStage : '',
    completedStages: Array.isArray(projection.completedStages)
      ? projection.completedStages.filter((stage) => typeof stage === 'string')
      : [],
    degraded: projection.degraded === true,
    recoveries: Array.isArray(projection.recoveries)
      ? projection.recoveries.filter((recovery) => typeof recovery === 'string')
      : []
  };
}

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

async function main() {
  const controlApiHost = resolveDefaultControlApiHost();
  const controlApiPort = resolveDefaultControlApiPort();
  const startedAt = new Date().toISOString();
  let desiredRuntimeActive = true;
  let runtimeStatus = createInitialRuntimeStatus();
  let runtimeSettingsState = await readRuntimeSettingsFromDisk({
    cwd: process.cwd()
  });
  const logger = {
    info: logInfo,
    error: logError
  };

  const workerSlot = createSupervisorWorkerSlot({
    entryPath: WORKER_ENTRY,
    label: 'Worker',
    restartDelayMs: WORKER_RESTART_DELAY_MS,
    bootFailureWindowMs: BOOT_FAILURE_WINDOW_MS,
    maxConsecutiveBootFailures: MAX_CONSECUTIVE_BOOT_FAILURES,
    buildSpawnEnv: () =>
      buildRuntimeProcessEnv({
        QQ_AI_BOT_WORKER_KIND: 'qq'
      }),
    shouldKeepAlive: () => desiredRuntimeActive,
    logInfo,
    logError,
    onAttach: (child) => {
      runtimeStatus = attachQqWorker(runtimeStatus, child);
    },
    onMessage: (message) => {
      if (!message || typeof message !== 'object') {
        return;
      }

      if (message.type === 'status' && message.data && typeof message.data === 'object') {
        runtimeStatus = applyQqWorkerMessage(
          runtimeStatus,
          message.data,
          normalizeLlmRequestStatus,
          normalizeLlmFailureStatus
        );
      }
    },
    onExit: () => {
      runtimeStatus = clearQqWorker(runtimeStatus);
    },
    onAlreadyStopped: () => {
      runtimeStatus = clearQqWorker(runtimeStatus);
    },
    onRestartDisabled: (count) => {
      desiredRuntimeActive = false;
      logError(
        `[supervisor] Worker restart disabled after ${count} consecutive boot failures. Fix config or runtime errors, then call /start again.`
      );
    }
  });

  const wechatWorkerSlot = createSupervisorWorkerSlot({
    entryPath: WORKER_ENTRY,
    label: 'Wechat worker',
    restartDelayMs: WORKER_RESTART_DELAY_MS,
    bootFailureWindowMs: BOOT_FAILURE_WINDOW_MS,
    maxConsecutiveBootFailures: MAX_CONSECUTIVE_BOOT_FAILURES,
    buildSpawnEnv: () =>
      buildRuntimeProcessEnv({
        QQ_AI_BOT_WORKER_KIND: 'wechat'
      }),
    shouldKeepAlive: () => desiredRuntimeActive,
    logInfo,
    logError,
    onAttach: (child) => {
      runtimeStatus = attachWechatWorker(runtimeStatus, child);
    },
    onMessage: (message, child) => {
      if (!message || typeof message !== 'object') {
        return;
      }

      if (message.type === 'status' && message.data && typeof message.data === 'object') {
        runtimeStatus = applyWechatWorkerMessage(
          runtimeStatus,
          message.data,
          child,
          normalizeLlmRequestStatus,
          normalizeLlmFailureStatus
        );
      }
    },
    onExit: () => {
      runtimeStatus = clearWechatWorker(runtimeStatus);
    },
    onAlreadyStopped: () => {
      runtimeStatus = clearWechatWorker(runtimeStatus, {
        clearConfigured: false
      });
    },
    onRestartDisabled: (count) => {
      logError(
        `[supervisor] Wechat worker restart disabled after ${count} consecutive boot failures.`
      );
    }
  });

  function clearWorkerRestartTimer() {
    workerSlot.clearRestartTimer();
  }

  function clearWechatWorkerRestartTimer() {
    wechatWorkerSlot.clearRestartTimer();
  }
  const runtimeController = createSupervisorRuntimeController({
    getRuntimeStatus: () => runtimeStatus,
    setRuntimeStatus: (nextRuntimeStatus) => {
      runtimeStatus = nextRuntimeStatus;
    },
    getDesiredRuntimeActive: () => desiredRuntimeActive,
    setDesiredRuntimeActive: (nextDesiredRuntimeActive) => {
      desiredRuntimeActive = nextDesiredRuntimeActive;
    },
    getRuntimeSettingsState: () => runtimeSettingsState,
    setRuntimeSettingsState: (nextRuntimeSettingsState) => {
      runtimeSettingsState = nextRuntimeSettingsState;
    },
    workerSlot,
    wechatWorkerSlot,
    buildStatusPayload: (lastConfigSavedAt) =>
      buildSupervisorStatusPayload({
        startedAt,
        lastConfigSavedAt,
        controlApiHost,
        controlApiPort,
        runtimeStatus,
        configPath: runtimeSettingsState?.settingsPath ?? ''
      })
  });

  const controlApi = createControlApiServer({
    host: controlApiHost,
    port: controlApiPort,
    logger,
    getStatus: async () => runtimeController.getStatus(),
    getConfig: async () => runtimeController.getConfig(),
    updateConfig: async (payload) => runtimeController.updateConfig(payload),
    startRuntime: async () => runtimeController.startRuntime(),
    stopRuntime: async () => runtimeController.stopRuntime()
  });

  try {
    await controlApi.start();
  } catch (error) {
    if (error && error.code === 'EADDRINUSE') {
      throw new Error(
        `Control API port ${controlApiPort} is already in use. Another supervisor may already be running.`
      );
    }

    throw error;
  }
  logInfo(`[control] Listening on http://${controlApiHost}:${controlApiPort}`);

  await runtimeController.startRuntime('startup');

  async function shutdownSupervisor(signal) {
    logInfo(`[supervisor] Shutting down: signal=${signal}`);
    clearWorkerRestartTimer();
    clearWechatWorkerRestartTimer();
    await runtimeController.stopRuntime(`supervisor-${signal}`);
    process.exit(0);
  }

  process.on('SIGINT', () => {
    void shutdownSupervisor('SIGINT');
  });
  process.on('SIGTERM', () => {
    void shutdownSupervisor('SIGTERM');
  });
}

main().catch((error) => {
  logError(`[fatal] Supervisor startup failed: ${formatError(error)}`);
  process.exit(1);
});
