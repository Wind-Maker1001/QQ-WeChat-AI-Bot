import { createChannelMessage } from '../../domain/channel-message.mjs';
import {
  extractImageInputs,
  extractRawText,
  extractReplyMessageIds,
  extractTriggerInfo,
  isGroupMessageEvent
} from '../../napcat.mjs';

export function normalizeIncomingNapCatEvent(event, { prefix = '' } = {}) {
  if (!isGroupMessageEvent(event)) {
    return null;
  }

  const triggerInfo = extractTriggerInfo(event, prefix);

  return createChannelMessage({
    channelId: 'qq',
    kind: 'group_message',
    messageId: event.message_id,
    chatId: event.group_id,
    userId: event.user_id,
    selfId: event.self_id,
    rawText: triggerInfo.rawText || extractRawText(event),
    text: triggerInfo.userText,
    trigger: triggerInfo.trigger,
    triggered: triggerInfo.triggered,
    replyToMessageIds: extractReplyMessageIds(event),
    imageRefs: extractImageInputs(event)
  });
}
