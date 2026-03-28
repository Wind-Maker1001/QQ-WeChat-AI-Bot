function normalizeProcessId(pid) {
  return typeof pid === 'number' && Number.isFinite(pid) ? pid : null;
}

export function createInitialRuntimeStatus() {
  return {
    runtimeActive: false,
    runtimeReady: false,
    napcatConnected: false,
    activeLockCount: 0,
    workerProcessId: null,
    workerStartedAt: null,
    lastQqLlmRequest: null,
    lastQqLlmFailure: null,
    wechatConfigured: false,
    wechatRuntimeActive: false,
    wechatRuntimeReady: false,
    wechatBridgeConnected: false,
    wechatWorkerProcessId: null,
    wechatWorkerStartedAt: null,
    lastWechatLlmRequest: null,
    lastWechatLlmFailure: null
  };
}

export function buildSupervisorStatusPayload({
  startedAt,
  lastConfigSavedAt,
  controlApiHost,
  controlApiPort,
  runtimeStatus,
  configPath = ''
}) {
  return {
    startedAt,
    processId: process.pid,
    runtimeActive: runtimeStatus.runtimeActive,
    runtimeReady: runtimeStatus.runtimeReady,
    napcatConnected: runtimeStatus.napcatConnected,
    activeLockCount: runtimeStatus.activeLockCount,
    configRestartRequired: false,
    lastConfigSavedAt,
    controlApiUrl: `http://${controlApiHost}:${controlApiPort}`,
    configPath,
    workerProcessId: runtimeStatus.workerProcessId,
    workerStartedAt: runtimeStatus.workerStartedAt,
    lastQqLlmRequest: runtimeStatus.lastQqLlmRequest,
    lastQqLlmFailure: runtimeStatus.lastQqLlmFailure,
    wechatConfigured: runtimeStatus.wechatConfigured,
    wechatRuntimeActive: runtimeStatus.wechatRuntimeActive,
    wechatRuntimeReady: runtimeStatus.wechatRuntimeReady,
    wechatBridgeConnected: runtimeStatus.wechatBridgeConnected,
    wechatWorkerProcessId: runtimeStatus.wechatWorkerProcessId,
    wechatWorkerStartedAt: runtimeStatus.wechatWorkerStartedAt,
    lastWechatLlmRequest: runtimeStatus.lastWechatLlmRequest,
    lastWechatLlmFailure: runtimeStatus.lastWechatLlmFailure
  };
}

export function setWechatConfigured(runtimeStatus, configured) {
  return {
    ...runtimeStatus,
    wechatConfigured: configured === true
  };
}

export function attachQqWorker(runtimeStatus, child) {
  return {
    ...runtimeStatus,
    runtimeActive: true,
    runtimeReady: false,
    napcatConnected: false,
    activeLockCount: 0,
    workerProcessId: normalizeProcessId(child?.pid),
    workerStartedAt: new Date().toISOString(),
    lastQqLlmRequest: null,
    lastQqLlmFailure: null
  };
}

export function applyQqWorkerMessage(
  runtimeStatus,
  messageData,
  normalizeLlmRequestStatus,
  normalizeLlmFailureStatus
) {
  return {
    ...runtimeStatus,
    runtimeActive: messageData?.runtimeActive === true,
    runtimeReady: messageData?.runtimeReady === true,
    napcatConnected: messageData?.napcatConnected === true,
    activeLockCount: typeof messageData?.activeLockCount === 'number' ? messageData.activeLockCount : 0,
    lastQqLlmRequest: normalizeLlmRequestStatus(messageData?.lastLlmRequest),
    lastQqLlmFailure: normalizeLlmFailureStatus(messageData?.lastLlmFailure)
  };
}

export function clearQqWorker(runtimeStatus) {
  return {
    ...runtimeStatus,
    runtimeActive: false,
    runtimeReady: false,
    napcatConnected: false,
    activeLockCount: 0,
    workerProcessId: null,
    workerStartedAt: null,
    lastQqLlmRequest: null,
    lastQqLlmFailure: null
  };
}

export function attachWechatWorker(runtimeStatus, child) {
  return {
    ...runtimeStatus,
    wechatRuntimeActive: true,
    wechatRuntimeReady: false,
    wechatConfigured: true,
    wechatBridgeConnected: false,
    wechatWorkerProcessId: normalizeProcessId(child?.pid),
    wechatWorkerStartedAt: new Date().toISOString(),
    lastWechatLlmRequest: null,
    lastWechatLlmFailure: null
  };
}

export function applyWechatWorkerMessage(
  runtimeStatus,
  messageData,
  child,
  normalizeLlmRequestStatus,
  normalizeLlmFailureStatus
) {
  return {
    ...runtimeStatus,
    wechatRuntimeActive: messageData?.runtimeActive === true,
    wechatRuntimeReady: messageData?.runtimeReady === true,
    wechatBridgeConnected: messageData?.bridgeConnected === true,
    wechatWorkerProcessId: normalizeProcessId(child?.pid),
    lastWechatLlmRequest: normalizeLlmRequestStatus(messageData?.lastLlmRequest),
    lastWechatLlmFailure: normalizeLlmFailureStatus(messageData?.lastLlmFailure)
  };
}

export function clearWechatWorker(runtimeStatus, { clearConfigured = true } = {}) {
  return {
    ...runtimeStatus,
    wechatConfigured: clearConfigured ? false : runtimeStatus.wechatConfigured,
    wechatRuntimeActive: false,
    wechatRuntimeReady: false,
    wechatBridgeConnected: false,
    wechatWorkerProcessId: null,
    wechatWorkerStartedAt: null,
    lastWechatLlmRequest: null,
    lastWechatLlmFailure: null
  };
}

export function shouldRunWechatWorker(runtimeConfig) {
  return Boolean(runtimeConfig?.wechat?.bridgeUrl);
}
