import { createSessionStore } from '../session.mjs';
import { createChannelRuntimeHost } from './create-channel-runtime-host.mjs';
import { createRuntimeWorkerSession } from './runtime-worker-session.mjs';
import { formatError } from '../utils.mjs';

const workerKind = process.env.QQ_AI_BOT_WORKER_KIND === 'wechat' ? 'wechat' : 'qq';

function logInfo(message, ...args) {
  console.log(new Date().toISOString(), message, ...args);
}

function logError(message, ...args) {
  console.error(new Date().toISOString(), message, ...args);
}

function sendStatus(status) {
  if (typeof process.send === 'function') {
    process.send({
      type: 'status',
      data: status
    });
  }
}

async function main() {
  const sessionStore = await createSessionStore();
  const logger = {
    info: logInfo,
    error: logError
  };
  let runtimeWorkerSession = null;

  const host = createChannelRuntimeHost({
    workerKind,
    getRuntimeConfig: () => runtimeWorkerSession?.getRuntimeConfig?.() ?? null,
    isShuttingDown: () => runtimeWorkerSession?.isShuttingDown?.() ?? false,
    handleIncomingPacket: async (packet) => runtimeWorkerSession?.handleIncomingPacket(packet),
    logInfo,
    logError,
    reportStatus: () => runtimeWorkerSession?.reportStatus()
  });

  runtimeWorkerSession = createRuntimeWorkerSession({
    workerKind,
    host,
    sessionStore,
    logger,
    sendStatus
  });

  async function applyWorkerMessage(message) {
    if (!message || typeof message !== 'object') {
      return;
    }

    if (message.type !== 'config:init' && message.type !== 'config:update') {
      return;
    }

    const nextWorkerKind =
      message?.data?.workerKind === 'wechat' ? 'wechat' : 'qq';

    if (nextWorkerKind !== workerKind) {
      return;
    }

    await runtimeWorkerSession.applyRuntimeSettingsSnapshot(
      message.data.runtimeSettingsSnapshot,
      message.data.source || message.type
    );
  }

  process.on('message', (message) => {
    void applyWorkerMessage(message).catch((error) => {
      logError(`[${workerKind === 'wechat' ? 'wechat-runtime' : 'runtime'}] Failed to apply worker message: ${formatError(error)}`);
      process.exit(1);
    });
  });

  async function shutdown(signal) {
    await runtimeWorkerSession.shutdown(signal);

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

  sendStatus({
    runtimeActive: false,
    runtimeReady: false,
    activeLockCount: 0
  });
}

main().catch((error) => {
  logError(`[fatal] Runtime worker failed: ${formatError(error)}`);
  process.exit(1);
});
