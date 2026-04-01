import {
  TOOL_KIND_CODE_INTERPRETER,
  TOOL_KIND_WEB_SEARCH,
  buildRouteToolPolicy
} from './tool-registry.mjs';

export const API_STYLE_RESPONSES = 'responses';
export const API_STYLE_CHAT_COMPLETIONS = 'chat_completions';

function inferDefaultReasoningEffort(routeName, model, apiStyle) {
  if (apiStyle !== API_STYLE_RESPONSES) {
    return '';
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';

  if (!normalizedModel.startsWith('gpt-5')) {
    return '';
  }

  return 'high';
}

function normalizeReasoningEffort(value, routeName, model, apiStyle) {
  const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : '';

  if (
    normalizedValue === 'none' ||
    normalizedValue === 'low' ||
    normalizedValue === 'medium' ||
    normalizedValue === 'high'
  ) {
    return normalizedValue;
  }

  return inferDefaultReasoningEffort(routeName, model, apiStyle);
}

function inferDefaultTextVerbosity(routeName, model, apiStyle) {
  if (apiStyle !== API_STYLE_RESPONSES) {
    return '';
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';

  if (!normalizedModel.startsWith('gpt-5')) {
    return '';
  }

  return 'high';
}

function normalizeTextVerbosity(value, routeName, model, apiStyle) {
  const normalizedValue = typeof value === 'string' ? value.trim().toLowerCase() : '';

  if (normalizedValue === 'low' || normalizedValue === 'medium' || normalizedValue === 'high') {
    return normalizedValue;
  }

  return inferDefaultTextVerbosity(routeName, model, apiStyle);
}

function normalizeBooleanFlag(value, fallback = false) {
  if (value === true || value === false) {
    return value;
  }

  if (typeof value !== 'string') {
    return fallback;
  }

  const normalizedValue = value.trim().toLowerCase();

  if (['1', 'true', 'yes', 'on'].includes(normalizedValue)) {
    return true;
  }

  if (['0', 'false', 'no', 'off'].includes(normalizedValue)) {
    return false;
  }

  return fallback;
}

export function normalizeApiStyle(style, { routeName = '', model = '', baseURL = '' } = {}) {
  const normalizedStyle = typeof style === 'string' ? style.trim().toLowerCase() : '';

  if (normalizedStyle === 'responses' || normalizedStyle === 'response') {
    return API_STYLE_RESPONSES;
  }

  if (
    normalizedStyle === 'chat' ||
    normalizedStyle === 'chat_completions' ||
    normalizedStyle === 'chat-completions' ||
    normalizedStyle === 'chatcompletions'
  ) {
    return API_STYLE_CHAT_COMPLETIONS;
  }

  const normalizedModel = typeof model === 'string' ? model.trim().toLowerCase() : '';
  const normalizedBaseUrl = typeof baseURL === 'string' ? baseURL.toLowerCase() : '';

  if (
    routeName === 'default' &&
    (normalizedModel.startsWith('deepseek-') || normalizedBaseUrl.includes('api.deepseek.com'))
  ) {
    return API_STYLE_CHAT_COMPLETIONS;
  }

  return API_STYLE_RESPONSES;
}

export function resolveRouteRequestPolicy({
  routeName,
  model,
  baseURL,
  apiStyle,
  reasoningEffort,
  textVerbosity,
  enableWebSearch,
  enableCodeInterpreter,
  toolPolicy,
  fallback
}) {
  const resolvedModel = model || fallback?.model || '';
  const resolvedBaseURL = baseURL ?? fallback?.baseURL ?? '';
  const resolvedApiStyle = normalizeApiStyle(apiStyle ?? fallback?.apiStyle ?? '', {
    routeName,
    model: resolvedModel,
    baseURL: resolvedBaseURL
  });

  const explicitToolPolicy =
    toolPolicy && typeof toolPolicy === 'object'
      ? toolPolicy
      : {
          enabledTools: [
            ...(normalizeBooleanFlag(enableWebSearch ?? false, false) ? [TOOL_KIND_WEB_SEARCH] : []),
            ...(normalizeBooleanFlag(enableCodeInterpreter ?? false, false) ? [TOOL_KIND_CODE_INTERPRETER] : [])
          ]
        };
  const fallbackToolPolicy = fallback?.toolPolicy && typeof fallback.toolPolicy === 'object'
    ? fallback.toolPolicy
    : {
        enabledTools: [
          ...(normalizeBooleanFlag(fallback?.enableWebSearch ?? false, false) ? [TOOL_KIND_WEB_SEARCH] : []),
          ...(normalizeBooleanFlag(fallback?.enableCodeInterpreter ?? false, false) ? [TOOL_KIND_CODE_INTERPRETER] : [])
        ]
      };
  const resolvedToolPolicy = buildRouteToolPolicy(
    explicitToolPolicy.enabledTools.length > 0 ? explicitToolPolicy : fallbackToolPolicy
  );
  const resolvedEnableWebSearch = resolvedToolPolicy.enabledTools.includes(TOOL_KIND_WEB_SEARCH);
  const resolvedEnableCodeInterpreter = resolvedToolPolicy.enabledTools.includes(TOOL_KIND_CODE_INTERPRETER);

  return {
    routeName,
    model: resolvedModel,
    apiStyle: resolvedApiStyle,
    reasoningEffort: normalizeReasoningEffort(
      reasoningEffort ?? fallback?.reasoningEffort ?? '',
      routeName,
      resolvedModel,
      resolvedApiStyle
    ),
    textVerbosity: normalizeTextVerbosity(
      textVerbosity ?? fallback?.textVerbosity ?? '',
      routeName,
      resolvedModel,
      resolvedApiStyle
    ),
    enableWebSearch: resolvedEnableWebSearch,
    enableCodeInterpreter: resolvedEnableCodeInterpreter,
    toolPolicy: resolvedToolPolicy
  };
}

export function resolveEffectiveRequestPolicy({
  routePolicy,
  reasoningEffortOverride,
  textVerbosityOverride,
  enableWebSearchOverride,
  enableCodeInterpreterOverride
}) {
  return {
    apiStyle: routePolicy.apiStyle,
    reasoningEffort: normalizeReasoningEffort(
      reasoningEffortOverride ?? routePolicy.reasoningEffort,
      routePolicy.routeName,
      routePolicy.model,
      routePolicy.apiStyle
    ),
    textVerbosity: normalizeTextVerbosity(
      textVerbosityOverride ?? routePolicy.textVerbosity,
      routePolicy.routeName,
      routePolicy.model,
      routePolicy.apiStyle
    ),
    enableWebSearch:
      typeof enableWebSearchOverride === 'boolean'
        ? enableWebSearchOverride
        : routePolicy.enableWebSearch,
    enableCodeInterpreter:
      typeof enableCodeInterpreterOverride === 'boolean'
        ? enableCodeInterpreterOverride
        : routePolicy.enableCodeInterpreter
  };
}

export function buildEnabledToolKinds({
  enableWebSearch = false,
  enableCodeInterpreter = false
} = {}) {
  const toolKinds = [];

  if (enableWebSearch) {
    toolKinds.push('web_search');
  }

  if (enableCodeInterpreter) {
    toolKinds.push('code_interpreter');
  }

  return toolKinds;
}
