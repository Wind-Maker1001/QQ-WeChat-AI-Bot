function normalizeChannelId(value) {
  if (typeof value !== 'string') {
    return 'unknown';
  }

  const trimmed = value.trim();
  return trimmed || 'unknown';
}

function ensureFunction(name, value) {
  if (typeof value !== 'function') {
    throw new Error(`ChannelPort requires function: ${name}`);
  }

  return value;
}

export function assertChannelPort(channelPort) {
  if (!channelPort || typeof channelPort !== 'object') {
    throw new Error('ChannelPort is required.');
  }

  if (typeof channelPort.channelId !== 'string' || !channelPort.channelId.trim()) {
    throw new Error('ChannelPort requires channelId.');
  }

  ensureFunction('sendText', channelPort.sendText);
  ensureFunction('getMessage', channelPort.getMessage);
  ensureFunction('readImage', channelPort.readImage);

  return channelPort;
}

export function createChannelPort({
  channelId,
  sendText,
  getMessage,
  readImage
}) {
  const port = {
    channelId: normalizeChannelId(channelId),
    sendText: ensureFunction('sendText', sendText),
    getMessage: ensureFunction('getMessage', getMessage),
    readImage: ensureFunction('readImage', readImage)
  };

  return Object.freeze(assertChannelPort(port));
}
