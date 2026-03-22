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

function normalizeReplyIds(replyMessageIds) {
  if (!Array.isArray(replyMessageIds)) {
    return Object.freeze([]);
  }

  return Object.freeze(
    replyMessageIds
      .filter((item) => typeof item === 'string' || typeof item === 'number' || typeof item === 'bigint')
      .map((item) => String(item).trim())
      .filter(Boolean)
  );
}

function normalizeImageInputs(imageInputs) {
  if (!Array.isArray(imageInputs)) {
    return Object.freeze([]);
  }

  return Object.freeze(
    imageInputs
      .filter((item) => item && typeof item === 'object')
      .map((item, index) =>
        Object.freeze({
          imageUrl: typeof item.imageUrl === 'string' ? item.imageUrl : '',
          fileId: typeof item.fileId === 'string' ? item.fileId : '',
          source: typeof item.source === 'string' ? item.source : 'unknown',
          index: Number.isInteger(item.index) ? item.index : index
        })
      )
      .filter((item) => item.imageUrl || item.fileId)
  );
}

export function createInboundMessage({
  source = 'napcat',
  kind = 'group_message',
  groupId = null,
  userId = null,
  selfId = null,
  rawText = '',
  text = '',
  trigger = 'none',
  triggered = false,
  replyMessageIds = [],
  imageInputs = []
} = {}) {
  return Object.freeze({
    source: typeof source === 'string' ? source : 'unknown',
    kind: typeof kind === 'string' ? kind : 'unknown',
    groupId: normalizeIdentity(groupId),
    userId: normalizeIdentity(userId),
    selfId: normalizeIdentity(selfId),
    rawText: normalizeText(rawText),
    text: normalizeText(text),
    trigger:
      trigger === 'prefix' || trigger === 'mention' || trigger === 'none' ? trigger : 'none',
    triggered: triggered === true,
    replyMessageIds: normalizeReplyIds(replyMessageIds),
    imageInputs: normalizeImageInputs(imageInputs)
  });
}

export function hasReplyReferences(message) {
  return Array.isArray(message?.replyMessageIds) && message.replyMessageIds.length > 0;
}
