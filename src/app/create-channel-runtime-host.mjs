import { createNapCatClient } from '../napcat.mjs';
import { createNapCatChannelPort } from '../adapters/napcat/channel-port.mjs';
import { normalizeIncomingNapCatEvent } from '../adapters/napcat/normalize-event.mjs';
import { createWechatBridgeClient } from '../adapters/wechat/bridge-client.mjs';
import { createWechatChannelPort } from '../adapters/wechat/channel-port.mjs';
import { normalizeIncomingWechatBridgeEvent } from '../adapters/wechat/normalize-event.mjs';
import { createRuntimeConnectionManager } from './runtime-connection-manager.mjs';
import { assertChannelRuntimeHost } from './channel-runtime-host.mjs';

function createQqRuntimeHost({
  getRuntimeConfig,
  isShuttingDown,
  handleIncomingPacket,
  logInfo,
  logError,
  reportStatus
}) {
  let napcatConnected = false;
  let napcatClient = null;

  const connectionManager = createRuntimeConnectionManager({
    connectionLabel: 'napcat',
    errorLabel: 'WebSocket error',
    getCurrentClient: () => napcatClient,
    setCurrentClient: (client) => {
      napcatClient = client;
    },
    setConnected: (connected) => {
      napcatConnected = connected;
    },
    getReconnectDelayMs: () => getRuntimeConfig()?.runtime?.reconnectDelayMs ?? 3000,
    getConnectionTarget: () => getRuntimeConfig()?.napcat?.wsUrl ?? '',
    isShuttingDown,
    connectClient: (client) => client?.connect(),
    disconnectClient: (client, reason) => client?.disconnect(1000, reason),
    canDisconnectClient: (client) => Boolean(client?.getSocket?.()),
    logInfo,
    logError,
    reportStatus
  });

  function createClient() {
    const runtimeConfig = getRuntimeConfig();

    return createNapCatClient({
      url: runtimeConfig.napcat.wsUrl,
      token: runtimeConfig.napcat.token,
      onEvent: (packet) => {
        if (!connectionManager.isActiveClient(napcatClient)) {
          return;
        }

        void handleIncomingPacket(packet);
      },
      onOpen: () => connectionManager.handleOpen(napcatClient),
      onClose: (code, reason) => connectionManager.handleClose(napcatClient, code, reason),
      onError: (error) => connectionManager.handleError(napcatClient, error)
    });
  }

  return assertChannelRuntimeHost({
    connect() {
      if (napcatClient) {
        connectionManager.attachInitialClient(napcatClient);
        return;
      }

      connectionManager.attachInitialClient(createClient());
    },
    disconnect(reason) {
      connectionManager.shutdownCurrentClient(reason);
      napcatConnected = false;
      reportStatus();
    },
    async applyConfig(runtimeConfig, source, previousRuntimeConfig = null) {
      const napcatChanged =
        previousRuntimeConfig?.napcat?.wsUrl !== runtimeConfig.napcat.wsUrl ||
        previousRuntimeConfig?.napcat?.token !== runtimeConfig.napcat.token;

      if (!napcatClient) {
        return;
      }

      if (napcatChanged) {
        logInfo('[runtime] NapCat connection config changed; reconnecting client.');
        connectionManager.replaceClient(createClient(), `runtime config reload:${source}`);
      }
    },
    buildChannelPort() {
      return createNapCatChannelPort({
        napcatClient,
        prefix: getRuntimeConfig()?.bot?.prefix ?? ''
      });
    },
    normalizeIncomingEvent(packet) {
      return normalizeIncomingNapCatEvent(packet, {
        prefix: getRuntimeConfig()?.bot?.prefix ?? ''
      });
    },
    buildStatusPayload() {
      return {
        runtimeReady: !isShuttingDown() && napcatConnected,
        napcatConnected
      };
    }
  });
}

function createWechatRuntimeHost({
  getRuntimeConfig,
  isShuttingDown,
  handleIncomingPacket,
  logInfo,
  logError,
  reportStatus
}) {
  let bridgeConnected = false;
  let bridgeClient = null;

  const connectionManager = createRuntimeConnectionManager({
    connectionLabel: 'wechat-bridge',
    getCurrentClient: () => bridgeClient,
    setCurrentClient: (client) => {
      bridgeClient = client;
    },
    setConnected: (connected) => {
      bridgeConnected = connected;
    },
    getReconnectDelayMs: () => getRuntimeConfig()?.runtime?.reconnectDelayMs ?? 3000,
    getConnectionTarget: () => getRuntimeConfig()?.wechat?.bridgeUrl ?? '',
    isShuttingDown,
    connectClient: (client) => client?.connect(),
    disconnectClient: (client, reason) => client?.disconnect(1000, reason),
    logInfo,
    logError,
    reportStatus
  });

  function createClient() {
    const runtimeConfig = getRuntimeConfig();

    return createWechatBridgeClient({
      url: runtimeConfig.wechat.bridgeUrl,
      token: runtimeConfig.wechat.token,
      onOpen: () => connectionManager.handleOpen(bridgeClient),
      onClose: (code, reason) => connectionManager.handleClose(bridgeClient, code, reason),
      onError: (error) => connectionManager.handleError(bridgeClient, error),
      onEvent: (packet) => {
        if (!connectionManager.isActiveClient(bridgeClient)) {
          return;
        }

        void handleIncomingPacket(packet);
      }
    });
  }

  return assertChannelRuntimeHost({
    connect() {
      if (bridgeClient) {
        connectionManager.attachInitialClient(bridgeClient);
        return;
      }

      connectionManager.attachInitialClient(createClient());
    },
    disconnect(reason) {
      connectionManager.shutdownCurrentClient(reason);
      bridgeConnected = false;
      reportStatus();
    },
    async applyConfig(runtimeConfig, source, previousRuntimeConfig = null) {
      const bridgeChanged =
        previousRuntimeConfig?.wechat?.bridgeUrl !== runtimeConfig.wechat.bridgeUrl ||
        previousRuntimeConfig?.wechat?.token !== runtimeConfig.wechat.token;

      if (!bridgeClient) {
        return;
      }

      if (bridgeChanged) {
        logInfo('[wechat-runtime] Bridge connection config changed; reconnecting client.');
        connectionManager.replaceClient(createClient(), `runtime config reload:${source}`);
      }
    },
    buildChannelPort() {
      return createWechatChannelPort({
        bridgeClient,
        prefix: getRuntimeConfig()?.wechat?.botPrefix ?? '/ai'
      });
    },
    normalizeIncomingEvent(packet) {
      return normalizeIncomingWechatBridgeEvent(packet, {
        prefix: getRuntimeConfig()?.wechat?.botPrefix ?? '/ai'
      });
    },
    buildStatusPayload() {
      return {
        runtimeReady: !isShuttingDown() && bridgeConnected,
        bridgeConnected
      };
    }
  });
}

export function createChannelRuntimeHost(options) {
  if (options?.workerKind === 'wechat') {
    return createWechatRuntimeHost(options);
  }

  return createQqRuntimeHost(options);
}
