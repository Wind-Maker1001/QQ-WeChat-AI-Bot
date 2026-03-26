import { normalizeApiStyle } from './llm-request-policy.mjs';

export const DEFAULT_ADVANCED_TRIGGER_PREFIXES = [
  '/5.4',
  '/gpt',
  '/think',
  '/analyze',
  '/\u5206\u6790',
  '/vision',
  '/\u9ad8\u7ea7',
  '/\u591a\u6a21\u6001',
  '/\u770b\u56fe',
  '/\u56fe\u7247\u5206\u6790'
];

const COMPLEX_REASONING_PATTERNS = [
  /\b(why|how|compare|comparison|analy[sz]e|analysis|trade[\s-]?off|root cause|debug|diagnose|architecture|design|plan|strategy|step by step|deep dive|reason(ing)?|extend thinking)\b/i,
  /(?:\u4e3a\u4ec0\u4e48|\u600e\u4e48|\u5206\u6790|\u6bd4\u8f83|\u5bf9\u6bd4|\u63a8\u7406|\u8bc1\u660e|\u67b6\u6784|\u8bbe\u8ba1|\u65b9\u6848|\u91cd\u6784|\u8c03\u8bd5|\u6392\u67e5|\u6b65\u9aa4|\u8be6\u7ec6|\u6df1\u5165|\u601d\u8003)/
];

const WEB_SEARCH_PATTERNS = [
  /\b(latest|recent|current|today|news|official|documentation|docs|release notes|version|price|weather|status|look up|search)\b/i,
  /(?:\u6700\u65b0|\u6700\u8fd1|\u4eca\u5929|\u5f53\u524d|\u5b98\u65b9|\u5b98\u7f51|\u6587\u6863|\u8d44\u6599|\u65b0\u95fb|\u4ef7\u683c|\u6c47\u7387|\u5929\u6c14|\u72b6\u6001|\u67e5\u4e00\u4e0b|\u641c\u4e00\u4e0b)/
];

const CODE_INTERPRETER_PATTERNS = [
  /\b(csv|excel|spreadsheet|dataset|data set|table|statistics|statistical|regression|simulate|simulation|plot|chart|calculate|computation|python)\b/i,
  /(?:csv|excel|\u8868\u683c|\u6570\u636e\u96c6|\u6570\u636e\u5206\u6790|\u7edf\u8ba1|\u56de\u5f52|\u62df\u5408|\u4eff\u771f|\u6a21\u62df|\u7ed8\u56fe|\u56fe\u8868|\u8ba1\u7b97)/
];

function hasAnyPattern(text, patterns) {
  return patterns.some((pattern) => pattern.test(text));
}

function looksLikeGreeting(text) {
  if (typeof text !== 'string') {
    return false;
  }

  const normalizedText = text.trim().toLowerCase();

  if (!normalizedText || normalizedText.length > 24) {
    return false;
  }

  return /^(hi|hello|hey|yo|\u4f60\u597d|\u60a8\u597d|\u5728\u5417|\u65e9\u4e0a\u597d|\u4e2d\u5348\u597d|\u665a\u4e0a\u597d|\u54c8\u55bd)[!,.?\u3002\uFF01\uFF1F]*$/i.test(
    normalizedText
  );
}

function shouldBoostThinking(text) {
  if (typeof text !== 'string' || !text.trim() || looksLikeGreeting(text)) {
    return false;
  }

  const normalizedText = text.trim();
  const lineCount = normalizedText.split(/\r?\n/).filter(Boolean).length;
  const questionCount = (normalizedText.match(/[?\uFF1F]/g) || []).length;
  const listLike = /(^|\n)\s*(?:\d+[.)\u3001]|[-*])/m.test(normalizedText);
  const punctuationCount = (normalizedText.match(/[,:，：]/g) || []).length;

  if (hasAnyPattern(normalizedText, COMPLEX_REASONING_PATTERNS)) {
    return true;
  }

  if (lineCount >= 3 || listLike) {
    return true;
  }

  if (normalizedText.length >= 180) {
    return true;
  }

  if (normalizedText.length >= 120 && (questionCount >= 1 || punctuationCount >= 3)) {
    return true;
  }

  return questionCount >= 2 && normalizedText.length >= 80;
}

function shouldUseWebSearch(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), WEB_SEARCH_PATTERNS);
}

function shouldUseCodeInterpreter(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), CODE_INTERPRETER_PATTERNS);
}

function normalizeRequestedCapabilityBoolean(value) {
  if (value === true || value === false) {
    return value;
  }

  return undefined;
}

function normalizeRouteDecisionTrigger(trigger, { imageCount = 0 } = {}) {
  const kind =
    typeof trigger?.kind === 'string' && trigger.kind ? trigger.kind : imageCount > 0 ? 'image' : 'default';
  const matchedPrefix =
    typeof trigger?.matchedPrefix === 'string' && trigger.matchedPrefix ? trigger.matchedPrefix : '';

  if (kind === 'directive' && matchedPrefix) {
    return {
      kind,
      matchedPrefix
    };
  }

  if (kind === 'image') {
    return {
      kind: 'image',
      matchedPrefix: ''
    };
  }

  return {
    kind: kind === 'directive' ? 'default' : kind,
    matchedPrefix: ''
  };
}

function normalizeRouteDecisionRequestedCapabilities(requestedCapabilities) {
  const reasoningEffort =
    typeof requestedCapabilities?.reasoningEffort === 'string'
      ? requestedCapabilities.reasoningEffort
      : '';
  const textVerbosity =
    typeof requestedCapabilities?.textVerbosity === 'string'
      ? requestedCapabilities.textVerbosity
      : '';
  const enableWebSearch = normalizeRequestedCapabilityBoolean(requestedCapabilities?.enableWebSearch);
  const enableCodeInterpreter = normalizeRequestedCapabilityBoolean(
    requestedCapabilities?.enableCodeInterpreter
  );

  return {
    reasoningEffort,
    textVerbosity,
    enableWebSearch,
    enableCodeInterpreter,
    needsResponsesCapabilities:
      requestedCapabilities?.needsResponsesCapabilities === true ||
      Boolean(
        reasoningEffort ||
          textVerbosity ||
          enableWebSearch === true ||
          enableCodeInterpreter === true
      )
  };
}

function categorizeReasonTags(reasonTags) {
  const normalizedReasonTags = Array.isArray(reasonTags)
    ? reasonTags.filter((tag) => typeof tag === 'string' && tag)
    : [];
  const triggerReasons = [];
  const capabilityReasons = [];
  const upgradeReasons = [];

  for (const tag of normalizedReasonTags) {
    if (tag === 'image' || tag.startsWith('directive:')) {
      triggerReasons.push(tag);
      continue;
    }

    if (tag === 'capability_upgrade') {
      upgradeReasons.push(tag);
      continue;
    }

    if (tag !== 'default') {
      capabilityReasons.push(tag);
    }
  }

  return {
    triggerReasons,
    capabilityReasons,
    upgradeReasons
  };
}

function flattenReasonGroups(reasonGroups) {
  if (!reasonGroups || typeof reasonGroups !== 'object') {
    return [];
  }

  return [
    ...(Array.isArray(reasonGroups.triggerReasons) ? reasonGroups.triggerReasons : []),
    ...(Array.isArray(reasonGroups.capabilityReasons) ? reasonGroups.capabilityReasons : []),
    ...(Array.isArray(reasonGroups.upgradeReasons) ? reasonGroups.upgradeReasons : [])
  ].filter((tag) => typeof tag === 'string' && tag);
}

export function createRouteDecision({
  route = 'default',
  userText = '',
  selectedRoute = {},
  imageCount = 0,
  trigger = {},
  decisionMetadata = {},
  reasonGroups = {},
  requestedCapabilities = {}
} = {}) {
  const normalizedTrigger = normalizeRouteDecisionTrigger(trigger, { imageCount });
  const normalizedDecisionMetadata =
    decisionMetadata && typeof decisionMetadata === 'object' ? decisionMetadata : {};
  const inputReasonGroups = {
    ...(Array.isArray(normalizedDecisionMetadata.triggerReasons)
      ? { triggerReasons: normalizedDecisionMetadata.triggerReasons }
      : Array.isArray(reasonGroups.triggerReasons)
        ? { triggerReasons: reasonGroups.triggerReasons }
        : {}),
    ...(Array.isArray(normalizedDecisionMetadata.capabilityReasons)
      ? { capabilityReasons: normalizedDecisionMetadata.capabilityReasons }
      : Array.isArray(reasonGroups.capabilityReasons)
        ? { capabilityReasons: reasonGroups.capabilityReasons }
        : {}),
    ...(Array.isArray(normalizedDecisionMetadata.upgradeReasons)
      ? { upgradeReasons: normalizedDecisionMetadata.upgradeReasons }
      : Array.isArray(reasonGroups.upgradeReasons)
        ? { upgradeReasons: reasonGroups.upgradeReasons }
        : {})
  };
  const fallbackReasonTags = Array.isArray(normalizedDecisionMetadata.reasonTags)
    ? normalizedDecisionMetadata.reasonTags
    : flattenReasonGroups(inputReasonGroups);
  const normalizedReasonGroups = {
    ...categorizeReasonTags(fallbackReasonTags),
    ...inputReasonGroups
  };

  if (
    !Array.isArray(normalizedReasonGroups.triggerReasons) ||
    normalizedReasonGroups.triggerReasons.length === 0
  ) {
    if (normalizedTrigger.kind === 'image') {
      normalizedReasonGroups.triggerReasons = ['image'];
    } else if (normalizedTrigger.kind === 'directive' && normalizedTrigger.matchedPrefix) {
      normalizedReasonGroups.triggerReasons = [`directive:${normalizedTrigger.matchedPrefix}`];
    }
  }

  const normalizedRequestedCapabilities =
    normalizeRouteDecisionRequestedCapabilities(requestedCapabilities);
  const reasonTags = flattenReasonGroups(normalizedReasonGroups);
  const normalizedReasonTags = reasonTags.length > 0 ? reasonTags : ['default'];

  return {
    route: route === 'advanced' ? 'advanced' : 'default',
    userText: typeof userText === 'string' ? userText : '',
    model: typeof selectedRoute?.model === 'string' ? selectedRoute.model : '',
    apiStyle: typeof selectedRoute?.apiStyle === 'string' ? selectedRoute.apiStyle : '',
    sessionSuffix: route === 'advanced' ? 'advanced' : 'default',
    imageCount: Number.isInteger(imageCount) ? imageCount : 0,
    trigger: normalizedTrigger,
    decisionMetadata: {
      reasonTags: normalizedReasonTags,
      triggerReasons: Array.isArray(normalizedReasonGroups.triggerReasons)
        ? normalizedReasonGroups.triggerReasons
        : [],
      capabilityReasons: Array.isArray(normalizedReasonGroups.capabilityReasons)
        ? normalizedReasonGroups.capabilityReasons
        : [],
      upgradeReasons: Array.isArray(normalizedReasonGroups.upgradeReasons)
        ? normalizedReasonGroups.upgradeReasons
        : [],
      capabilityUpgradeApplied:
        Array.isArray(normalizedReasonGroups.upgradeReasons) &&
        normalizedReasonGroups.upgradeReasons.includes('capability_upgrade')
    },
    requestedCapabilities: normalizedRequestedCapabilities
  };
}

export function getRouteDecisionReasonGroups(routeInfo) {
  const decisionMetadata = routeInfo?.decisionMetadata;

  if (
    decisionMetadata &&
    typeof decisionMetadata === 'object' &&
    (Array.isArray(decisionMetadata.triggerReasons) ||
      Array.isArray(decisionMetadata.capabilityReasons) ||
      Array.isArray(decisionMetadata.upgradeReasons))
  ) {
    return {
      triggerReasons: Array.isArray(decisionMetadata.triggerReasons)
        ? decisionMetadata.triggerReasons.filter((tag) => typeof tag === 'string' && tag)
        : [],
      capabilityReasons: Array.isArray(decisionMetadata.capabilityReasons)
        ? decisionMetadata.capabilityReasons.filter((tag) => typeof tag === 'string' && tag)
        : [],
      upgradeReasons: Array.isArray(decisionMetadata.upgradeReasons)
        ? decisionMetadata.upgradeReasons.filter((tag) => typeof tag === 'string' && tag)
        : []
    };
  }

  const reasonGroups = categorizeReasonTags(
    Array.isArray(decisionMetadata?.reasonTags)
      ? decisionMetadata.reasonTags
      : getRouteDecisionReasonTags(routeInfo)
  );
  const trigger = getRouteDecisionTrigger(routeInfo);

  if (reasonGroups.triggerReasons.length === 0) {
    if (trigger.kind === 'image') {
      reasonGroups.triggerReasons.push('image');
    } else if (trigger.kind === 'directive' && trigger.matchedPrefix) {
      reasonGroups.triggerReasons.push(`directive:${trigger.matchedPrefix}`);
    }
  }

  return reasonGroups;
}

export function getRouteDecisionReasonTags(routeInfo) {
  if (Array.isArray(routeInfo?.decisionMetadata?.reasonTags)) {
    return routeInfo.decisionMetadata.reasonTags.filter((tag) => typeof tag === 'string' && tag);
  }

  const reasonGroups = routeInfo?.decisionMetadata ? getRouteDecisionReasonGroups(routeInfo) : null;
  const groupedReasonTags = flattenReasonGroups(reasonGroups);

  if (groupedReasonTags.length > 0) {
    return groupedReasonTags;
  }

  return ['default'];
}

export function formatRouteDecisionReason(routeInfo) {
  const reasonTags = getRouteDecisionReasonTags(routeInfo);
  return reasonTags.length > 0 ? reasonTags.join('+') : 'default';
}

export function buildRouteDecisionSummary(routeInfo) {
  if (!routeInfo || typeof routeInfo !== 'object') {
    return null;
  }

  return {
    trigger: getRouteDecisionTrigger(routeInfo),
    reasonTags: getRouteDecisionReasonTags(routeInfo),
    reasonGroups: getRouteDecisionReasonGroups(routeInfo),
    requestedCapabilities: getRouteDecisionRequestedCapabilities(routeInfo),
    routeReason: formatRouteDecisionReason(routeInfo),
    matchedPrefix: getRouteDecisionMatchedPrefix(routeInfo)
  };
}

export function getRouteDecisionTrigger(routeInfo) {
  return normalizeRouteDecisionTrigger(routeInfo?.trigger, {
    imageCount: routeInfo?.imageCount
  });
}

export function getRouteDecisionMatchedPrefix(routeInfo) {
  return getRouteDecisionTrigger(routeInfo).matchedPrefix;
}

export function getRouteDecisionRequestedCapabilities(routeInfo) {
  return normalizeRouteDecisionRequestedCapabilities(routeInfo?.requestedCapabilities);
}

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
  let matchedPrefix = '';
  const reasonTags = [];

  if (hasImages) {
    route = 'advanced';
    reasonTags.push('image');
  } else {
    matchedPrefix = normalizedPrefixes.find((prefix) => normalizedText.startsWith(prefix)) || '';

    if (matchedPrefix) {
      route = 'advanced';
      reasonTags.push(`directive:${matchedPrefix}`);
      normalizedText = normalizedText.slice(matchedPrefix.length).trim();
    }
  }

  if (!normalizedText) {
    normalizedText = hasImages ? '\u8bf7\u5206\u6790\u8fd9\u5f20\u56fe\u7247\u3002' : '\u8bf7\u7ee7\u7eed\u3002';
  }

  const needsWebSearch = shouldUseWebSearch(normalizedText);
  const needsCodeInterpreter = shouldUseCodeInterpreter(normalizedText);
  const needsComplexThinking = shouldBoostThinking(normalizedText);

  if (needsComplexThinking) {
    reasonTags.push('complex');
  }

  if (needsWebSearch) {
    reasonTags.push('web_search');
  }

  if (needsCodeInterpreter) {
    reasonTags.push('code_interpreter');
  }

  const requestedReasoningEffort =
    route === 'default' && (needsComplexThinking || needsWebSearch || needsCodeInterpreter)
      ? 'high'
      : '';
  const requestedTextVerbosity =
    route === 'default' && (needsComplexThinking || needsWebSearch || needsCodeInterpreter)
      ? 'high'
      : '';

  let requestedEnableWebSearch = needsWebSearch ? true : undefined;
  let requestedEnableCodeInterpreter = needsCodeInterpreter ? true : undefined;

  const defaultSupportsResponses =
    normalizeApiStyle(defaultRoute?.apiStyle, {
      routeName: 'default',
      model: defaultRoute?.model,
      baseURL: defaultRoute?.baseURL
    }) === 'responses';
  const advancedSupportsResponses =
    normalizeApiStyle(advancedRoute?.apiStyle, {
      routeName: 'advanced',
      model: advancedRoute?.model,
      baseURL: advancedRoute?.baseURL
    }) === 'responses';
  const needsResponsesCapabilities = Boolean(
    requestedReasoningEffort ||
      requestedTextVerbosity ||
      requestedEnableWebSearch === true ||
      requestedEnableCodeInterpreter === true
  );

  if (
    route === 'default' &&
    needsResponsesCapabilities &&
    !defaultSupportsResponses &&
    advancedSupportsResponses
  ) {
    route = 'advanced';
    reasonTags.push('capability_upgrade');

    if (!needsWebSearch) {
      requestedEnableWebSearch = false;
    }

    if (!needsCodeInterpreter) {
      requestedEnableCodeInterpreter = false;
    }
  }

  const selectedRoute = route === 'advanced' ? advancedRoute : defaultRoute;
  const normalizedReasonTags = reasonTags.length > 0 ? reasonTags : ['default'];
  const reasonGroups = categorizeReasonTags(normalizedReasonTags);
  const requestedCapabilities = {
    reasoningEffort: requestedReasoningEffort,
    textVerbosity: requestedTextVerbosity,
    enableWebSearch: normalizeRequestedCapabilityBoolean(requestedEnableWebSearch),
    enableCodeInterpreter: normalizeRequestedCapabilityBoolean(requestedEnableCodeInterpreter),
    needsResponsesCapabilities
  };

  return createRouteDecision({
    route,
    userText: normalizedText,
    imageCount: imageInputs.length,
    selectedRoute,
    trigger: {
      kind: hasImages ? 'image' : matchedPrefix ? 'directive' : 'default',
      matchedPrefix
    },
    reasonGroups,
    requestedCapabilities
  });
}
