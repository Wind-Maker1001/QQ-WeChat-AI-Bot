import { normalizeApiStyle } from './llm-request-policy.mjs';
import {
  TOOL_KIND_CODE_INTERPRETER,
  TOOL_KIND_LOCAL_RUNTIME_STATE,
  TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
  TOOL_KIND_WEB_SEARCH,
  createToolSelection,
  deriveCompatibilityCapabilities
} from './tool-registry.mjs';

export const DEFAULT_ADVANCED_TRIGGER_PREFIXES = [
  '/5.4',
  '/gpt',
  '/think',
  '/analyze',
  '/分析',
  '/vision',
  '/高级',
  '/多模态',
  '/看图',
  '/图片分析'
];

const COMPLEX_REASONING_PATTERNS = [
  /\b(why|how|compare|comparison|analy[sz]e|analysis|trade[\s-]?off|root cause|debug|diagnose|architecture|design|plan|strategy|step by step|deep dive|reason(ing)?|extend thinking)\b/i,
  /(?:为什么|怎么|分析|比较|对比|推理|证明|架构|设计|方案|重构|调试|排查|步骤|详细|深入|思考)/
];

const WEB_SEARCH_PATTERNS = [
  /\b(latest|recent|current|today|news|official|documentation|docs|release notes|version|price|weather|status|look up|search)\b/i,
  /(?:最新|最近|今天|当前|官方|官网|文档|资料|新闻|价格|汇率|天气|状态|查一下|搜一下)/
];

const CODE_INTERPRETER_PATTERNS = [
  /\b(csv|excel|spreadsheet|dataset|data set|table|statistics|statistical|regression|simulate|simulation|plot|chart|calculate|computation|python)\b/i,
  /(?:csv|excel|表格|数据集|数据分析|统计|回归|拟合|仿真|模拟|绘图|图表|计算)/
];

const LOCAL_RUNTIME_STATE_PATTERNS = [
  /\b(current runtime|runtime status|control api|worker status|route config|available tools this turn|callable tools this turn|can you browse this turn|can you run code this turn)\b/i,
  /(?:当前(?:这轮|对话|回合)?|本轮|这轮).{0,16}(?:工具|能力|联网|搜索|运行代码|代码解释器|路由|配置|runtime|control api|worker)/
];

const LOCAL_SNAPSHOT_PATTERNS = [
  /\b(snapshot|snapshots|restore impact|restore advice|snapshot diff|state archive)\b/i,
  /(?:快照|状态快照|恢复影响|恢复建议|快照差异|归档)/
];

const STRONG_WEB_SEARCH_PATTERNS = [
  /\b(latest|official|documentation|docs|release notes|look up|search|news)\b/i,
  /(?:最新|官方|官网|文档|资料|搜索|搜一下|新闻)/
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

  return /^(hi|hello|hey|yo|你好|您好|在吗|早上好|中午好|晚上好|哈喽)[!,.?。！？]*$/i.test(
    normalizedText
  );
}

export function shouldBoostThinking(text) {
  if (typeof text !== 'string' || !text.trim() || looksLikeGreeting(text)) {
    return false;
  }

  const normalizedText = text.trim();
  const lineCount = normalizedText.split(/\r?\n/).filter(Boolean).length;
  const questionCount = (normalizedText.match(/[?？]/g) || []).length;
  const listLike = /(^|\n)\s*(?:\d+[.)、]|[-*])/m.test(normalizedText);
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

export function shouldUseWebSearch(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), WEB_SEARCH_PATTERNS);
}

export function shouldUseCodeInterpreter(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), CODE_INTERPRETER_PATTERNS);
}

export function shouldUseLocalRuntimeState(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), LOCAL_RUNTIME_STATE_PATTERNS);
}

export function shouldUseLocalSnapshotInspect(text) {
  if (typeof text !== 'string' || !text.trim()) {
    return false;
  }

  return hasAnyPattern(text.trim(), LOCAL_SNAPSHOT_PATTERNS);
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

export function createMessageIntentSignals({
  userText = '',
  normalizedText = '',
  imageCount = 0,
  routeHint = 'default',
  trigger = {},
  capabilityReasons = [],
  requestedCapabilities = {},
  requestedTools = {},
  capabilityUpgradeApplied = false
} = {}) {
  const normalizedCapabilityReasons = Array.isArray(capabilityReasons)
    ? capabilityReasons.filter((reason) => typeof reason === 'string' && reason)
    : [];
  const normalizedRequestedTools = createToolSelection(requestedTools);
  const compatibilityCapabilities = deriveCompatibilityCapabilities({
    requestedTools: normalizedRequestedTools,
    reasoningEffort: requestedCapabilities.reasoningEffort,
    textVerbosity: requestedCapabilities.textVerbosity,
    needsResponsesCapabilities: requestedCapabilities.needsResponsesCapabilities === true
  });

  return {
    userText: typeof userText === 'string' ? userText : '',
    normalizedText: typeof normalizedText === 'string' ? normalizedText : '',
    imageCount: Number.isInteger(imageCount) ? imageCount : 0,
    routeHint: routeHint === 'advanced' ? 'advanced' : 'default',
    trigger:
      trigger && typeof trigger === 'object'
        ? {
            kind: typeof trigger.kind === 'string' ? trigger.kind : imageCount > 0 ? 'image' : 'default',
            matchedPrefix:
              typeof trigger.matchedPrefix === 'string' ? trigger.matchedPrefix : ''
          }
        : {
            kind: imageCount > 0 ? 'image' : 'default',
            matchedPrefix: ''
          },
    capabilityReasons: normalizedCapabilityReasons,
    capabilityUpgradeApplied: capabilityUpgradeApplied === true,
    requestedTools: normalizedRequestedTools,
    requestedCapabilities: {
      ...compatibilityCapabilities,
      reasoningEffort:
        typeof requestedCapabilities.reasoningEffort === 'string'
          ? requestedCapabilities.reasoningEffort
          : compatibilityCapabilities.reasoningEffort,
      textVerbosity:
        typeof requestedCapabilities.textVerbosity === 'string'
          ? requestedCapabilities.textVerbosity
          : compatibilityCapabilities.textVerbosity,
      needsResponsesCapabilities:
        requestedCapabilities.needsResponsesCapabilities === true ||
        compatibilityCapabilities.needsResponsesCapabilities
    }
  };
}

export function analyzeMessageIntent({
  userText,
  imageInputs = [],
  advancedTriggerPrefixes = DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  defaultRoute,
  advancedRoute
}) {
  const hasImages = Array.isArray(imageInputs) && imageInputs.length > 0;
  const normalizedPrefixes = normalizeTriggerPrefixes(advancedTriggerPrefixes);
  let normalizedText = typeof userText === 'string' ? userText.trim() : '';
  let routeHint = 'default';
  let matchedPrefix = '';
  let capabilityUpgradeApplied = false;
  const capabilityReasons = [];
  const requestedToolKinds = [];
  const requiredToolKinds = [];

  if (hasImages) {
    routeHint = 'advanced';
  } else {
    matchedPrefix = normalizedPrefixes.find((prefix) => normalizedText.startsWith(prefix)) || '';

    if (matchedPrefix) {
      routeHint = 'advanced';
      normalizedText = normalizedText.slice(matchedPrefix.length).trim();
    }
  }

  if (!normalizedText) {
    normalizedText = hasImages ? '请分析这张图片。' : '请继续。';
  }

  const needsComplexThinking = shouldBoostThinking(normalizedText);
  const needsLocalRuntimeState = shouldUseLocalRuntimeState(normalizedText);
  const needsLocalSnapshotInspect = shouldUseLocalSnapshotInspect(normalizedText);
  const strongWebSearchSignal = hasAnyPattern(normalizedText, STRONG_WEB_SEARCH_PATTERNS);
  const needsWebSearch =
    shouldUseWebSearch(normalizedText) &&
    (!(needsLocalRuntimeState || needsLocalSnapshotInspect) || strongWebSearchSignal);
  const needsCodeInterpreter = shouldUseCodeInterpreter(normalizedText);

  if (needsComplexThinking) {
    capabilityReasons.push('complex');
  }

  if (needsWebSearch) {
    capabilityReasons.push('web_search');
    requestedToolKinds.push(TOOL_KIND_WEB_SEARCH);
  }

  if (needsCodeInterpreter) {
    capabilityReasons.push('code_interpreter');
    requestedToolKinds.push(TOOL_KIND_CODE_INTERPRETER);
  }

  if (needsLocalRuntimeState) {
    capabilityReasons.push('local_runtime_state');
    requestedToolKinds.push(TOOL_KIND_LOCAL_RUNTIME_STATE);
    requiredToolKinds.push(TOOL_KIND_LOCAL_RUNTIME_STATE);
  }

  if (needsLocalSnapshotInspect) {
    capabilityReasons.push('local_snapshot_inspect');
    requestedToolKinds.push(TOOL_KIND_LOCAL_SNAPSHOT_INSPECT);
    requiredToolKinds.push(TOOL_KIND_LOCAL_SNAPSHOT_INSPECT);
  }

  const requestedReasoningEffort =
    routeHint === 'default' &&
    (needsComplexThinking || needsWebSearch || needsCodeInterpreter)
      ? 'high'
      : '';
  const requestedTextVerbosity =
    routeHint === 'default' &&
    (needsComplexThinking || needsWebSearch || needsCodeInterpreter)
      ? 'high'
      : '';

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

  const requestedTools = createToolSelection({
    requested: requestedToolKinds,
    required: requiredToolKinds
  });
  const needsResponsesCapabilities = deriveCompatibilityCapabilities({
    requestedTools,
    reasoningEffort: requestedReasoningEffort,
    textVerbosity: requestedTextVerbosity
  }).needsResponsesCapabilities;

  if (
    routeHint === 'default' &&
    needsResponsesCapabilities &&
    !defaultSupportsResponses &&
    advancedSupportsResponses
  ) {
    routeHint = 'advanced';
    capabilityUpgradeApplied = true;
  }

  return createMessageIntentSignals({
    userText,
    normalizedText,
    imageCount: imageInputs.length,
    routeHint,
    trigger: {
      kind: hasImages ? 'image' : matchedPrefix ? 'directive' : 'default',
      matchedPrefix
    },
    capabilityReasons,
    capabilityUpgradeApplied,
    requestedTools,
    requestedCapabilities: {
      reasoningEffort: requestedReasoningEffort,
      textVerbosity: requestedTextVerbosity,
      needsResponsesCapabilities
    }
  });
}
