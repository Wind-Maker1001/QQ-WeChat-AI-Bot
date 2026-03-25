function normalizeChannelId(value) {
  if (typeof value !== 'string') {
    return 'unknown';
  }

  const trimmed = value.trim();
  return trimmed || 'unknown';
}

function normalizeIdentity(value) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === 'bigint') {
    return value;
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    return trimmed || null;
  }

  return null;
}

function normalizeText(value) {
  return typeof value === 'string' ? value : '';
}

function normalizeReplyIds(replyToMessageIds) {
  if (!Array.isArray(replyToMessageIds)) {
    return Object.freeze([]);
  }

  return Object.freeze(
    replyToMessageIds
      .filter((item) => typeof item === 'string' || typeof item === 'number' || typeof item === 'bigint')
      .map((item) => String(item).trim())
      .filter(Boolean)
  );
}

function normalizeImageRefs(imageRefs) {
  if (!Array.isArray(imageRefs)) {
    return Object.freeze([]);
  }

  return Object.freeze(
    imageRefs
      .filter((item) => item && typeof item === 'object')
      .map((item, index) =>
        Object.freeze({
          imageUrl: typeof item.imageUrl === 'string' ? item.imageUrl : '',
          fileRef:
            typeof item.fileRef === 'string'
              ? item.fileRef
              : typeof item.fileId === 'string'
                ? item.fileId
                : '',
          source: typeof item.source === 'string' ? item.source : 'unknown',
          index: Number.isInteger(item.index) ? item.index : index
        })
      )
      .filter((item) => item.imageUrl || item.fileRef)
  );
}

export function createChannelMessage({
  channelId = 'unknown',
  kind = 'group_message',
  messageId = null,
  chatId = null,
  userId = null,
  selfId = null,
  rawText = '',
  text = '',
  trigger = 'none',
  triggered = false,
  replyToMessageIds = [],
  imageRefs = []
} = {}) {
  const normalizedChannelId = normalizeChannelId(channelId);

  return Object.freeze({
    channelId: normalizedChannelId,
    source: normalizedChannelId,
    kind: typeof kind === 'string' ? kind : 'unknown',
    messageId: normalizeIdentity(messageId),
    chatId: normalizeIdentity(chatId),
    userId: normalizeIdentity(userId),
    selfId: normalizeIdentity(selfId),
    rawText: normalizeText(rawText),
    text: normalizeText(text),
    trigger:
      trigger === 'prefix' || trigger === 'mention' || trigger === 'none' ? trigger : 'none',
    triggered: triggered === true,
    replyToMessageIds: normalizeReplyIds(replyToMessageIds),
    imageRefs: normalizeImageRefs(imageRefs)
  });
}

export function hasReplyReferences(message) {
  return (
    Array.isArray(message?.replyToMessageIds) && message.replyToMessageIds.length > 0
  );
}
