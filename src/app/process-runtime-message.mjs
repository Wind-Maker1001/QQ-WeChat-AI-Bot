import { orchestrateIncomingMessage } from '../application/message-orchestrator.mjs';
import { buildConversationId } from '../domain/conversation-state.mjs';

export async function processRuntimeMessage({
  message,
  activeLocks,
  allowedChatIds,
  allowedUserIds,
  createChannelPort,
  sessionStore,
  llmRouter,
  logger,
  prepareImageInputs,
  imageCacheDir,
  maxOutputChars,
  reportStatus,
  onReplyTelemetry,
  onReplyFailureTelemetry,
  concurrentRequestLabel = 'request'
}) {
  if (!message?.triggered) {
    return false;
  }

  const chatId = message.chatId;
  const userId = message.userId;

  if (chatId === null || userId === null) {
    return false;
  }

  const chatIdText = String(chatId);
  const userIdText = String(userId);

  if (allowedChatIds.size > 0 && !allowedChatIds.has(chatIdText)) {
    logger.info(`[filter] Ignored chat not in allowlist: chat_id=${chatIdText}, user_id=${userIdText}`);
    return false;
  }

  if (allowedUserIds.size > 0 && !allowedUserIds.has(userIdText)) {
    logger.info(`[filter] Ignored user not in allowlist: chat_id=${chatIdText}, user_id=${userIdText}`);
    return false;
  }

  const lockKey = buildConversationId({
    channelId: message.channelId,
    chatId,
    userId
  });

  if (activeLocks.has(lockKey)) {
    logger.info(
      `[lock] Ignored concurrent ${concurrentRequestLabel}: chat_id=${chatId}, user_id=${userId}`
    );
    return false;
  }

  activeLocks.add(lockKey);
  reportStatus?.();

  try {
    const channelPort = createChannelPort();

    await orchestrateIncomingMessage({
      message,
      sessionStore,
      llmRouter,
      channelPort,
      logger,
      prepareImageInputs,
      imageCacheDir,
      maxOutputChars,
      onReplyTelemetry,
      onReplyFailureTelemetry
    });

    return true;
  } finally {
    activeLocks.delete(lockKey);
    reportStatus?.();
  }
}
