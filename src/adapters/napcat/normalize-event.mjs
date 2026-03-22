import { createInboundMessage } from '../../domain/inbound-message.mjs';
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

  return createInboundMessage({
    source: 'napcat',
    kind: 'group_message',
    groupId: event.group_id,
    userId: event.user_id,
    selfId: event.self_id,
    rawText: triggerInfo.rawText || extractRawText(event),
    text: triggerInfo.userText,
    trigger: triggerInfo.trigger,
    triggered: triggerInfo.triggered,
    replyMessageIds: extractReplyMessageIds(event),
    imageInputs: extractImageInputs(event)
  });
}
