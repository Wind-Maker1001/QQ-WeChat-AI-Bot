import { createChannelMessage, hasReplyReferences } from './channel-message.mjs';

export function createInboundMessage({
  source = 'unknown',
  channelId = source,
  kind = 'group_message',
  messageId = null,
  groupId = null,
  chatId = groupId,
  userId = null,
  selfId = null,
  rawText = '',
  text = '',
  trigger = 'none',
  triggered = false,
  replyMessageIds = [],
  replyToMessageIds = replyMessageIds,
  imageInputs = [],
  imageRefs = imageInputs
} = {}) {
  return createChannelMessage({
    channelId,
    kind,
    messageId,
    chatId,
    userId,
    selfId,
    rawText,
    text,
    trigger,
    triggered,
    replyToMessageIds,
    imageRefs
  });
}

export { hasReplyReferences };
