export function assertChannelRuntimeHost(host) {
  if (!host || typeof host !== 'object') {
    throw new Error('Runtime host is required.');
  }

  const requiredMethods = [
    'connect',
    'disconnect',
    'applyConfig',
    'buildChannelPort',
    'normalizeIncomingEvent',
    'buildStatusPayload'
  ];

  for (const methodName of requiredMethods) {
    if (typeof host[methodName] !== 'function') {
      throw new Error(`Runtime host is missing method: ${methodName}`);
    }
  }

  return host;
}
