export const DEFAULT_ADVANCED_TRIGGER_PREFIXES = [
  '/5.4',
  '/gpt',
  '/vision',
  '/\u9ad8\u7ea7',
  '/\u591a\u6a21\u6001',
  '/\u770b\u56fe',
  '/\u56fe\u7247\u5206\u6790'
];

export function normalizeTriggerPrefixes(prefixes) {
  if (!Array.isArray(prefixes)) {
    return [];
  }

  return prefixes
    .filter((item) => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean)
    .sort((left, right) => right.length - left.length);
}

export function resolveRouteDecision({
  userText,
  imageInputs = [],
  advancedTriggerPrefixes = DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  defaultRoute,
  advancedRoute
}) {
  const hasImages = Array.isArray(imageInputs) && imageInputs.length > 0;
  const normalizedPrefixes = normalizeTriggerPrefixes(advancedTriggerPrefixes);
  let normalizedText = typeof userText === 'string' ? userText.trim() : '';
  let route = 'default';
  let reason = 'default';
  let matchedPrefix = '';

  if (hasImages) {
    route = 'advanced';
    reason = 'image';
  } else {
    matchedPrefix = normalizedPrefixes.find((prefix) => normalizedText.startsWith(prefix)) || '';

    if (matchedPrefix) {
      route = 'advanced';
      reason = `directive:${matchedPrefix}`;
      normalizedText = normalizedText.slice(matchedPrefix.length).trim();
    }
  }

  if (!normalizedText) {
    normalizedText = hasImages ? '\u8bf7\u5206\u6790\u8fd9\u5f20\u56fe\u7247\u3002' : '\u8bf7\u7ee7\u7eed\u3002';
  }

  const selectedRoute = route === 'advanced' ? advancedRoute : defaultRoute;

  return {
    route,
    reason,
    matchedPrefix,
    userText: normalizedText,
    model: selectedRoute.model,
    apiStyle: selectedRoute.apiStyle,
    sessionSuffix: route,
    imageCount: imageInputs.length
  };
}
