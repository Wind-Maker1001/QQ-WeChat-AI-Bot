import {
  createExecutionProjection,
  EXECUTION_KIND_DIRECT,
  EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK,
  EXECUTION_STAGE_DIRECT
} from './execution-projection.mjs';
import { createConversationDelta } from './llm-reply-outcome.mjs';
import { buildEnabledToolKinds } from './llm-request-policy.mjs';

const TRANSIENT_ERROR_CODES = new Set([
  'ECONNABORTED',
  'ECONNREFUSED',
  'ECONNRESET',
  'EHOSTUNREACH',
  'EPIPE',
  'ETIMEDOUT',
  'ENOTFOUND',
  'EAI_AGAIN',
  'UND_ERR_CONNECT_TIMEOUT',
  'UND_ERR_HEADERS_TIMEOUT',
  'UND_ERR_SOCKET'
]);

function normalizeErrorStatus(error) {
  if (!error || typeof error !== 'object') {
    return null;
  }

  const status = error.status ?? error.statusCode ?? error.cause?.status ?? error.cause?.statusCode;
  return Number.isInteger(status) ? status : null;
}

export function isTransientProviderError(error) {
  const status = normalizeErrorStatus(error);

  if (Number.isInteger(status)) {
    return status >= 500 && status < 600;
  }

  const candidates = [error?.code, error?.errno, error?.cause?.code, error?.cause?.errno]
    .filter((value) => typeof value === 'string' && value)
    .map((value) => value.toUpperCase());

  if (candidates.some((value) => TRANSIENT_ERROR_CODES.has(value))) {
    return true;
  }

  const name = typeof error?.name === 'string' ? error.name.toLowerCase() : '';
  const message =
    error instanceof Error
      ? error.message.toLowerCase()
      : typeof error?.message === 'string'
        ? error.message.toLowerCase()
        : '';

  if (
    name.includes('timeout') ||
    name.includes('connection') ||
    message.includes('timeout') ||
    message.includes('timed out') ||
    message.includes('socket hang up') ||
    message.includes('connection error') ||
    message.includes('fetch failed') ||
    message.includes('network error')
  ) {
    return true;
  }

  return error?.cause ? isTransientProviderError(error.cause) : false;
}

export function createProviderFallbackDecision({
  request,
  primaryToolSupport,
  fallbackToolSupport,
  deepseekProvider,
  primaryError
}) {
  if (!deepseekProvider) {
    return {
      shouldFallback: false,
      reason: 'fallback-disabled'
    };
  }

  if (!isTransientProviderError(primaryError)) {
    return {
      shouldFallback: false,
      reason: 'primary-error-not-transient'
    };
  }

  if (Array.isArray(request?.imageInputs) && request.imageInputs.length > 0) {
    return {
      shouldFallback: false,
      reason: 'image-inputs-not-supported'
    };
  }

  if (primaryToolSupport?.hasSuppressedRequiredTools === true) {
    return {
      shouldFallback: false,
      reason: 'required-tools-already-suppressed'
    };
  }

  if (
    Array.isArray(primaryToolSupport?.requestedTools?.required) &&
    primaryToolSupport.requestedTools.required.length > 0
  ) {
    const fallbackRequiredToolsSupported = primaryToolSupport.requestedTools.required.every((requiredToolKind) =>
      fallbackToolSupport?.effectiveTools?.includes(requiredToolKind)
    );

    if (!fallbackRequiredToolsSupported) {
      return {
        shouldFallback: false,
        reason: 'fallback-cannot-preserve-required-tools'
      };
    }
  }

  const hasHostedTools =
    Array.isArray(primaryToolSupport?.effectiveHostedTools) &&
    primaryToolSupport.effectiveHostedTools.length > 0;

  if (hasHostedTools) {
    return {
      shouldFallback: false,
      reason: 'hosted-tools-required'
    };
  }

  return {
    shouldFallback: true,
    reason: 'deepseek-fallback-eligible'
  };
}

export function buildProviderFallbackReply({
  primaryRoutePolicy,
  fallbackReply,
  fallbackToolSupport,
  requestedTools = { requested: [], required: [] }
}) {
  return {
    ...fallbackReply,
    route: primaryRoutePolicy.routeName,
    configuredModel: primaryRoutePolicy.model,
    apiStyle:
      primaryRoutePolicy.apiStyle === fallbackReply.effectiveApiStyle
        ? fallbackReply.effectiveApiStyle
        : `${primaryRoutePolicy.apiStyle}->${fallbackReply.effectiveApiStyle}`,
    configuredApiStyle: primaryRoutePolicy.apiStyle,
    configuredReasoningEffort: primaryRoutePolicy.reasoningEffort,
    configuredTextVerbosity: primaryRoutePolicy.textVerbosity,
    configuredEnableWebSearch: primaryRoutePolicy.enableWebSearch,
    configuredEnableCodeInterpreter: primaryRoutePolicy.enableCodeInterpreter,
    configuredTools: buildEnabledToolKinds({
      enableWebSearch: primaryRoutePolicy.enableWebSearch,
      enableCodeInterpreter: primaryRoutePolicy.enableCodeInterpreter
    }),
    requestedTools,
    effectiveTools: Array.isArray(fallbackToolSupport?.effectiveTools)
      ? fallbackToolSupport.effectiveTools
      : [],
    suppressedTools: Array.isArray(fallbackToolSupport?.suppressedTools)
      ? fallbackToolSupport.suppressedTools
      : [],
    conversationDelta: createConversationDelta({
      ...fallbackReply.conversationDelta,
      previousResponseId: null,
      clearPreviousResponseId: true
    }),
    executionProjection: createExecutionProjection({
      kind: EXECUTION_KIND_DIRECT,
      failedStage: '',
      completedStages: [EXECUTION_STAGE_DIRECT],
      degraded: true,
      recoveries: [EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK]
    })
  };
}
