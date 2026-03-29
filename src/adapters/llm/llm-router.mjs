import { resolveRouteDecision, DEFAULT_ADVANCED_TRIGGER_PREFIXES, normalizeTriggerPrefixes } from '../../domain/route-decision.mjs';
import { createConversationDelta } from '../../domain/llm-reply-outcome.mjs';
import {
  createExecutionProjection,
  EXECUTION_KIND_DIRECT,
  EXECUTION_RECOVERY_PROVIDER_FALLBACK_TO_DEEPSEEK,
  EXECUTION_STAGE_DIRECT
} from '../../domain/execution-projection.mjs';
import {
  API_STYLE_RESPONSES,
  buildEnabledToolKinds,
  resolveEffectiveRequestPolicy,
  resolveRouteRequestPolicy
} from '../../domain/llm-request-policy.mjs';
import {
  createOpenAIProvider,
  DEFAULT_ADVANCED_MODEL,
  DEFAULT_DEFAULT_MODEL
} from './openai-provider.mjs';

const DEFAULT_DEEPSEEK_MODEL = 'deepseek-chat';
const DEFAULT_DEEPSEEK_BASE_URL = 'https://api.deepseek.com/v1';
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

function isTransientProviderError(error) {
  const status = normalizeErrorStatus(error);

  if (Number.isInteger(status)) {
    return status >= 500 && status < 600;
  }

  const candidates = [
    error?.code,
    error?.errno,
    error?.cause?.code,
    error?.cause?.errno
  ]
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

function isImplicitGpt5ResponsesDefault(routePolicy) {
  const normalizedModel =
    typeof routePolicy?.model === 'string' ? routePolicy.model.trim().toLowerCase() : '';

  return (
    routePolicy?.apiStyle === API_STYLE_RESPONSES &&
    normalizedModel.startsWith('gpt-5') &&
    routePolicy?.reasoningEffort === 'high' &&
    routePolicy?.textVerbosity === 'high'
  );
}

function canUseDeepSeekFallback({ routePolicy, request, effectiveRequest, deepseekProvider }) {
  if (!deepseekProvider) {
    return false;
  }

  if (Array.isArray(request?.imageInputs) && request.imageInputs.length > 0) {
    return false;
  }

  if (effectiveRequest.enableWebSearch === true || effectiveRequest.enableCodeInterpreter === true) {
    return false;
  }

  const hasExplicitReasoningOverride =
    typeof request?.reasoningEffortOverride === 'string' && request.reasoningEffortOverride.trim();
  const hasExplicitVerbosityOverride =
    typeof request?.textVerbosityOverride === 'string' && request.textVerbosityOverride.trim();

  if (hasExplicitReasoningOverride || hasExplicitVerbosityOverride) {
    return false;
  }

  if (
    routePolicy.apiStyle === API_STYLE_RESPONSES &&
    !isImplicitGpt5ResponsesDefault(routePolicy) &&
    (routePolicy.reasoningEffort || routePolicy.textVerbosity)
  ) {
    return false;
  }

  return true;
}

function buildFallbackReply({
  primaryRoutePolicy,
  fallbackReply
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

export function createLlmRouter({
  defaultRoute,
  advancedRoute,
  deepseekFallback,
  advancedTriggerPrefixes = DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  botSystemPrompt = '',
  botPersona = ''
}) {
  const defaultRoutePolicy = resolveRouteRequestPolicy({
    routeName: 'default',
    model: defaultRoute?.model || DEFAULT_DEFAULT_MODEL,
    baseURL: defaultRoute?.baseURL ?? null,
    apiStyle: defaultRoute?.apiStyle,
    reasoningEffort: defaultRoute?.reasoningEffort,
    textVerbosity: defaultRoute?.textVerbosity,
    enableWebSearch: defaultRoute?.enableWebSearch,
    enableCodeInterpreter: defaultRoute?.enableCodeInterpreter
  });
  const advancedRoutePolicy = resolveRouteRequestPolicy({
    routeName: 'advanced',
    model: advancedRoute?.model || DEFAULT_ADVANCED_MODEL,
    baseURL: advancedRoute?.baseURL ?? defaultRoute?.baseURL ?? null,
    apiStyle: advancedRoute?.apiStyle,
    reasoningEffort: advancedRoute?.reasoningEffort,
    textVerbosity: advancedRoute?.textVerbosity,
    enableWebSearch: advancedRoute?.enableWebSearch,
    enableCodeInterpreter: advancedRoute?.enableCodeInterpreter,
    fallback: {
      model: DEFAULT_ADVANCED_MODEL,
      baseURL: advancedRoute?.baseURL ?? defaultRoute?.baseURL ?? null,
      apiStyle: 'responses',
      reasoningEffort: defaultRoute?.reasoningEffort,
      textVerbosity: defaultRoute?.textVerbosity,
      enableWebSearch: defaultRoute?.enableWebSearch,
      enableCodeInterpreter: defaultRoute?.enableCodeInterpreter
    }
  });

  const defaultProvider = createOpenAIProvider({
    routeName: 'default',
    apiKey: defaultRoute?.apiKey,
    model: defaultRoutePolicy.model,
    baseURL: defaultRoute?.baseURL ?? null,
    routePolicy: defaultRoutePolicy,
    botSystemPrompt,
    botPersona
  });

  const advancedProvider = createOpenAIProvider({
    routeName: 'advanced',
    apiKey: advancedRoute?.apiKey || defaultRoute?.apiKey,
    model: advancedRoutePolicy.model,
    baseURL: advancedRoute?.baseURL ?? null,
    routePolicy: advancedRoutePolicy,
    fallback: {
      apiKey: defaultRoute?.apiKey,
      model: DEFAULT_ADVANCED_MODEL,
      baseURL: advancedRoute?.baseURL ?? defaultRoute?.baseURL ?? null,
      apiStyle: 'responses',
      reasoningEffort: defaultRoute?.reasoningEffort,
      textVerbosity: defaultRoute?.textVerbosity,
      enableWebSearch: defaultRoute?.enableWebSearch,
      enableCodeInterpreter: defaultRoute?.enableCodeInterpreter
    },
    botSystemPrompt,
    botPersona
  });
  const deepseekProvider =
    deepseekFallback?.fallbackEnabled === true
      ? createOpenAIProvider({
          routeName: 'deepseek-fallback',
          apiKey: deepseekFallback.apiKey,
          model: deepseekFallback.model || DEFAULT_DEEPSEEK_MODEL,
          baseURL: deepseekFallback.baseURL || DEFAULT_DEEPSEEK_BASE_URL,
          apiStyle: 'chat_completions',
          botSystemPrompt,
          botPersona
        })
      : null;

  const normalizedAdvancedTriggerPrefixes = normalizeTriggerPrefixes(advancedTriggerPrefixes);

  function describeRequest({
    route = 'default',
    reasoningEffortOverride,
    textVerbosityOverride,
    enableWebSearchOverride,
    enableCodeInterpreterOverride
  } = {}) {
    const routePolicy = route === 'advanced' ? advancedRoutePolicy : defaultRoutePolicy;
    const effectiveRequest = resolveEffectiveRequestPolicy({
      routePolicy,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
    const supportsResponsesCapabilities = effectiveRequest.apiStyle === API_STYLE_RESPONSES;

    return {
      route: routePolicy.routeName,
      configuredModel: routePolicy.model,
      model: routePolicy.model,
      configuredApiStyle: routePolicy.apiStyle,
      effectiveApiStyle: effectiveRequest.apiStyle,
      configuredReasoningEffort:
        routePolicy.apiStyle === API_STYLE_RESPONSES ? routePolicy.reasoningEffort : '',
      effectiveReasoningEffort:
        supportsResponsesCapabilities ? effectiveRequest.reasoningEffort : '',
      configuredTextVerbosity:
        routePolicy.apiStyle === API_STYLE_RESPONSES ? routePolicy.textVerbosity : '',
      effectiveTextVerbosity:
        supportsResponsesCapabilities ? effectiveRequest.textVerbosity : '',
      configuredEnableWebSearch:
        routePolicy.apiStyle === API_STYLE_RESPONSES ? routePolicy.enableWebSearch : false,
      effectiveEnableWebSearch:
        supportsResponsesCapabilities ? effectiveRequest.enableWebSearch : false,
      configuredEnableCodeInterpreter:
        routePolicy.apiStyle === API_STYLE_RESPONSES ? routePolicy.enableCodeInterpreter : false,
      effectiveEnableCodeInterpreter:
        supportsResponsesCapabilities ? effectiveRequest.enableCodeInterpreter : false,
      configuredTools:
        routePolicy.apiStyle === API_STYLE_RESPONSES
          ? buildEnabledToolKinds({
              enableWebSearch: routePolicy.enableWebSearch,
              enableCodeInterpreter: routePolicy.enableCodeInterpreter
            })
          : [],
      effectiveTools:
        supportsResponsesCapabilities
          ? buildEnabledToolKinds({
              enableWebSearch: effectiveRequest.enableWebSearch,
              enableCodeInterpreter: effectiveRequest.enableCodeInterpreter
            })
          : []
    };
  }

  function resolveRoute({ userText, imageInputs = [] }) {
    return resolveRouteDecision({
      userText,
      imageInputs,
      advancedTriggerPrefixes: normalizedAdvancedTriggerPrefixes,
      defaultRoute: {
        model: defaultRoutePolicy.model,
        apiStyle: defaultRoutePolicy.apiStyle,
        reasoningEffort: defaultRoutePolicy.reasoningEffort,
        textVerbosity: defaultRoutePolicy.textVerbosity,
        enableWebSearch: defaultRoutePolicy.enableWebSearch,
        enableCodeInterpreter: defaultRoutePolicy.enableCodeInterpreter,
        baseURL: defaultRoute?.baseURL ?? null
      },
      advancedRoute: {
        model: advancedRoutePolicy.model,
        apiStyle: advancedRoutePolicy.apiStyle,
        reasoningEffort: advancedRoutePolicy.reasoningEffort,
        textVerbosity: advancedRoutePolicy.textVerbosity,
        enableWebSearch: advancedRoutePolicy.enableWebSearch,
        enableCodeInterpreter: advancedRoutePolicy.enableCodeInterpreter,
        baseURL: advancedRoute?.baseURL ?? defaultRoute?.baseURL ?? null
      }
    });
  }

  async function generateReply({
    route = 'default',
    userText,
    previousResponseId = null,
    sharedMessages = [],
    imageInputs = [],
    reasoningEffortOverride,
    textVerbosityOverride,
    enableWebSearchOverride,
    enableCodeInterpreterOverride,
    storeOverride
  }) {
    const provider = route === 'advanced' ? advancedProvider : defaultProvider;
    const routePolicy = route === 'advanced' ? advancedRoutePolicy : defaultRoutePolicy;
    const effectiveRequest = resolveEffectiveRequestPolicy({
      routePolicy,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
    const request = {
      userText,
      previousResponseId,
      sharedMessages,
      imageInputs,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride,
      storeOverride
    };

    try {
      return await provider.generateReply(request);
    } catch (error) {
      if (
        !isTransientProviderError(error) ||
        !canUseDeepSeekFallback({
          routePolicy,
          request,
          effectiveRequest,
          deepseekProvider
        })
      ) {
        throw error;
      }

      const fallbackReply = await deepseekProvider.generateReply({
        ...request,
        previousResponseId: null
      });

      return buildFallbackReply({
        primaryRoutePolicy: routePolicy,
        fallbackReply
      });
    }
  }

  return {
    defaultRoute: {
      model: defaultProvider.model,
      apiStyle: defaultProvider.apiStyle,
      baseURL: defaultProvider.baseURL
    },
    advancedRoute: {
      model: advancedProvider.model,
      apiStyle: advancedProvider.apiStyle,
      baseURL: advancedProvider.baseURL
    },
    deepseekFallback: deepseekProvider
      ? {
          enabled: true,
          model: deepseekProvider.model,
          apiStyle: deepseekProvider.apiStyle,
          baseURL: deepseekProvider.baseURL
        }
      : {
          enabled: false,
          model: '',
          apiStyle: '',
          baseURL: ''
        },
    advancedTriggerPrefixes: normalizedAdvancedTriggerPrefixes,
    resolveRoute,
    describeRequest,
    generateReply
  };
}
