import { createChannelMessage } from '../../domain/channel-message.mjs';

function normalizeString(value) {
  return typeof value === 'string' ? value.trim() : '';
}

function normalizeReplyIds(replyIds) {
  if (!Array.isArray(replyIds)) {
    return [];
  }

  return replyIds
    .filter((item) => typeof item === 'string' || typeof item === 'number')
    .map((item) => String(item).trim())
    .filter(Boolean);
}

function normalizeImageRefs(images) {
  if (!Array.isArray(images)) {
    return [];
  }

  return images
    .filter((image) => image && typeof image === 'object')
    .map((image, index) => ({
      imageUrl: normalizeString(image.imageUrl),
      fileRef: normalizeString(image.fileId),
      source: 'wechat-bridge',
      index
    }))
    .filter((image) => image.imageUrl || image.fileRef);
}

function unwrapWechatBridgePayload(packet) {
  if (!packet || typeof packet !== 'object') {
    return null;
  }

  if (packet.type === 'group_message') {
    return packet;
  }

  if (packet.data && typeof packet.data === 'object' && packet.data.type === 'group_message') {
    return packet.data;
  }

  return null;
}

export function normalizeIncomingWechatBridgeEvent(packet, { prefix = '' } = {}) {
  const payload = unwrapWechatBridgePayload(packet);

  if (!payload) {
    return null;
  }

  const rawText = normalizeString(payload.rawText || payload.text);
  let text = normalizeString(payload.text);
  let trigger = 'none';
  let triggered = false;

  if (prefix && text.startsWith(prefix)) {
    trigger = 'prefix';
    triggered = true;
    text = text.slice(prefix.length).trim();
  } else if (payload.mentioned === true) {
    trigger = 'mention';
    triggered = true;
  }

  return createChannelMessage({
    channelId: 'wechat',
    kind: 'group_message',
    messageId: normalizeString(payload.messageId),
    chatId: normalizeString(payload.chatId),
    userId: normalizeString(payload.userId),
    selfId: normalizeString(payload.selfId),
    rawText,
    text: text || (triggered ? '\u8bf7\u7ee7\u7eed\u3002' : ''),
    trigger,
    triggered,
    replyToMessageIds: normalizeReplyIds(payload.replyToMessageIds),
    imageRefs: normalizeImageRefs(payload.images)
  });
}
