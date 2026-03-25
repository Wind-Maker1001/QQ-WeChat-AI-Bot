export const DEFAULT_ADVANCED_TRIGGER_PREFIXES = [
  '/5.4',
  '/gpt',
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

function normalizeApiStyle(apiStyle) {
  return typeof apiStyle === 'string' ? apiStyle.trim().toLowerCase() : '';
}

function looksLikeGreeting(text) {
  if (typeof text !== 'string') {
    return false;
  }

  const normalizedText = text.trim().toLowerCase();

  if (!normalizedText || normalizedText.length > 24) {
    return false;
  }

  return /^(hi|hello|hey|yo|你好|您好|在吗|早上好|中午好|晚上好|哈喽)[!,.?？。]*$/i.test(
    normalizedText
  );
}

function shouldBoostThinking(text) {
  if (typeof text !== 'string' || !text.trim() || looksLikeGreeting(text)) {
    return false;
  }

  const normalizedText = text.trim();
  const lineCount = normalizedText.split(/\r?\n/).filter(Boolean).length;
  const questionCount = (normalizedText.match(/[?？]/g) || []).length;
  const listLike = /(^|\n)\s*(?:\d+[.)]|[-*•])/m.test(normalizedText);
  const punctuationCount = (normalizedText.match(/[,:：;；]/g) || []).length;

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

  const defaultSupportsResponses = normalizeApiStyle(defaultRoute?.apiStyle) === 'responses';
  const advancedSupportsResponses = normalizeApiStyle(advancedRoute?.apiStyle) === 'responses';
  const needsResponsesCapabilities = Boolean(
    requestedReasoningEffort ||
      requestedTextVerbosity ||
      requestedEnableWebSearch === true ||
      requestedEnableCodeInterpreter === true
  );

  if (route === 'default' && needsResponsesCapabilities && !defaultSupportsResponses && advancedSupportsResponses) {
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

  return {
    route,
    reason: reasonTags.length > 0 ? reasonTags.join('+') : 'default',
    matchedPrefix,
    userText: normalizedText,
    model: selectedRoute.model,
    apiStyle: selectedRoute.apiStyle,
    sessionSuffix: route,
    imageCount: imageInputs.length,
    requestedReasoningEffort,
    requestedTextVerbosity,
    requestedEnableWebSearch,
    requestedEnableCodeInterpreter
  };
}
