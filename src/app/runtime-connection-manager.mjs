import { formatError } from '../utils.mjs';

export function createRuntimeConnectionManager({
  connectionLabel,
  errorLabel = 'Error',
  getCurrentClient,
  setCurrentClient,
  setConnected,
  getReconnectDelayMs,
  getConnectionTarget,
  isShuttingDown,
  connectClient,
  disconnectClient,
  canDisconnectClient = (client) => Boolean(client),
  logInfo,
  logError,
  reportStatus
}) {
  let reconnectTimer = null;
  let suppressedClient = null;

  function isActiveClient(client) {
    return getCurrentClient() === client && !isShuttingDown();
  }

  function clearReconnectTimer() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
  }

  function scheduleReconnect() {
    if (reconnectTimer || isShuttingDown()) {
      return;
    }

    const reconnectDelayMs = getReconnectDelayMs();
    logInfo(`[${connectionLabel}] Reconnecting in ${reconnectDelayMs / 1000}s.`);
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      logInfo(`[${connectionLabel}] Reconnecting now...`);
      const currentClient = getCurrentClient();

      if (currentClient) {
        connectClient(currentClient);
      }
    }, reconnectDelayMs);
  }

  function handleOpen(client) {
    if (!isActiveClient(client)) {
      return;
    }

    clearReconnectTimer();
    setConnected(true);
    logInfo(`[${connectionLabel}] Connected: ${getConnectionTarget()}`);
    reportStatus();
  }

  function handleClose(client, code, reason) {
    if (client === suppressedClient) {
      suppressedClient = null;
      return;
    }

    if (!isActiveClient(client)) {
      return;
    }

    setConnected(false);
    logInfo(`[${connectionLabel}] Closed: code=${code}, reason=${reason || 'none'}`);
    reportStatus();
    scheduleReconnect();
  }

  function handleError(client, error) {
    if (!isActiveClient(client)) {
      return;
    }

    logError(`[${connectionLabel}] ${errorLabel}: ${formatError(error)}`);
  }

  function attachInitialClient(client) {
    setCurrentClient(client);
    connectClient(client);
  }

  function replaceClient(client, reason) {
    clearReconnectTimer();
    const previousClient = getCurrentClient();
    setCurrentClient(client);
    setConnected(false);
    reportStatus();

    if (previousClient && canDisconnectClient(previousClient)) {
      suppressedClient = previousClient;
      disconnectClient(previousClient, reason);
    }

    connectClient(client);
  }

  function shutdownCurrentClient(reason) {
    clearReconnectTimer();
    const currentClient = getCurrentClient();

    if (currentClient && canDisconnectClient(currentClient)) {
      suppressedClient = currentClient;
      disconnectClient(currentClient, reason);
    }
  }

  return Object.freeze({
    isActiveClient,
    clearReconnectTimer,
    handleOpen,
    handleClose,
    handleError,
    attachInitialClient,
    replaceClient,
    shutdownCurrentClient
  });
}
