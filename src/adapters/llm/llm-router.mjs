import { createToolExecutor } from '../../application/tool-executor.mjs';
import {
  DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  normalizeTriggerPrefixes
} from '../../domain/message-analysis-policy.mjs';
import {
  buildProviderFallbackReply,
  createProviderFallbackDecision
} from '../../domain/provider-fallback-policy.mjs';
import { createProviderToolSupport } from '../../domain/provider-capability-matrix.mjs';
import {
  TOOL_KIND_CODE_INTERPRETER,
  TOOL_KIND_WEB_SEARCH,
  createToolSelection
} from '../../domain/tool-registry.mjs';
import {
  getRouteDecisionRequestedTools,
  resolveRouteDecision
} from '../../domain/route-decision.mjs';
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

function resolveRequestedTools({
  requestedTools,
  routePolicy,
  enableWebSearchOverride,
  enableCodeInterpreterOverride
}) {
  const explicitSelection = createToolSelection(requestedTools);

  if (explicitSelection.requested.length > 0 || explicitSelection.required.length > 0) {
    return explicitSelection;
  }

  const inferredToolKinds = [
    ...((typeof enableWebSearchOverride === 'boolean'
      ? enableWebSearchOverride
      : routePolicy.enableWebSearch)
      ? [TOOL_KIND_WEB_SEARCH]
      : []),
    ...((typeof enableCodeInterpreterOverride === 'boolean'
      ? enableCodeInterpreterOverride
      : routePolicy.enableCodeInterpreter)
      ? [TOOL_KIND_CODE_INTERPRETER]
      : [])
  ];

  return createToolSelection({
    requested: inferredToolKinds
  });
}

function buildToolSupport({
  providerName,
  apiStyle,
  routePolicy,
  request,
  isFallback = false
}) {
  return createProviderToolSupport({
    providerName,
    apiStyle,
    routeToolPolicy: routePolicy.toolPolicy,
    requestedTools: request.requestedTools,
    imageCount: Array.isArray(request.imageInputs) ? request.imageInputs.length : 0,
    allowPlannerStage: request.storeOverride !== false,
    isFallback,
    snapshotInspectorAvailable: true
  });
}

function attachToolMetadata({
  reply,
  routePolicy,
  toolSupport,
  effectiveRequest
}) {
  const effectiveTools = Array.isArray(toolSupport?.effectiveTools) ? toolSupport.effectiveTools : [];

  return {
    ...reply,
    configuredTools: Array.isArray(reply.configuredTools)
      ? reply.configuredTools
      : routePolicy.toolPolicy.enabledTools,
    requestedTools: toolSupport?.requestedTools ?? createToolSelection(),
    effectiveTools,
    suppressedTools: Array.isArray(toolSupport?.suppressedTools) ? toolSupport.suppressedTools : [],
    configuredEnableWebSearch: routePolicy.toolPolicy.enabledTools.includes(TOOL_KIND_WEB_SEARCH),
    effectiveEnableWebSearch: effectiveTools.includes(TOOL_KIND_WEB_SEARCH),
    configuredEnableCodeInterpreter: routePolicy.toolPolicy.enabledTools.includes(TOOL_KIND_CODE_INTERPRETER),
    effectiveEnableCodeInterpreter: effectiveTools.includes(TOOL_KIND_CODE_INTERPRETER),
    effectiveApiStyle: effectiveRequest.apiStyle
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
    enableCodeInterpreter: defaultRoute?.enableCodeInterpreter,
    toolPolicy: defaultRoute?.toolPolicy
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
    toolPolicy: advancedRoute?.toolPolicy,
    fallback: {
      model: DEFAULT_ADVANCED_MODEL,
      baseURL: advancedRoute?.baseURL ?? defaultRoute?.baseURL ?? null,
      apiStyle: 'responses',
      reasoningEffort: defaultRoute?.reasoningEffort,
      textVerbosity: defaultRoute?.textVerbosity,
      enableWebSearch: defaultRoute?.enableWebSearch,
      enableCodeInterpreter: defaultRoute?.enableCodeInterpreter,
      toolPolicy: defaultRoute?.toolPolicy
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
  const toolExecutor = createToolExecutor({
    cwd: process.cwd()
  });

  function describeRequest({
    route = 'default',
    requestedTools = createToolSelection(),
    reasoningEffortOverride,
    textVerbosityOverride,
    enableWebSearchOverride,
    enableCodeInterpreterOverride,
    imageInputs = [],
    storeOverride
  } = {}) {
    const routePolicy = route === 'advanced' ? advancedRoutePolicy : defaultRoutePolicy;
    const effectiveRequest = resolveEffectiveRequestPolicy({
      routePolicy,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
    const resolvedRequestedTools = resolveRequestedTools({
      requestedTools,
      routePolicy,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
    const toolSupport = buildToolSupport({
      providerName: routePolicy.routeName,
      apiStyle: effectiveRequest.apiStyle,
      routePolicy,
      request: {
        requestedTools: resolvedRequestedTools,
        imageInputs,
        storeOverride
      }
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
      configuredEnableWebSearch: routePolicy.toolPolicy.enabledTools.includes(TOOL_KIND_WEB_SEARCH),
      effectiveEnableWebSearch: toolSupport.effectiveTools.includes(TOOL_KIND_WEB_SEARCH),
      configuredEnableCodeInterpreter: routePolicy.toolPolicy.enabledTools.includes(TOOL_KIND_CODE_INTERPRETER),
      effectiveEnableCodeInterpreter: toolSupport.effectiveTools.includes(TOOL_KIND_CODE_INTERPRETER),
      configuredTools: routePolicy.toolPolicy.enabledTools,
      requestedTools: resolvedRequestedTools,
      effectiveTools: toolSupport.effectiveTools,
      effectiveHostedTools: toolSupport.effectiveHostedTools,
      effectiveLocalTools: toolSupport.effectiveLocalTools,
      suppressedTools: toolSupport.suppressedTools
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

  async function tryExecuteLocalToolRequest({
    userText = '',
    routeInfo = null,
    requestDescriptor = null
  } = {}) {
    return toolExecutor.tryExecuteLocalTools({
      userText,
      routeInfo,
      requestDescriptor
    });
  }

  async function generateReply({
    route = 'default',
    userText,
    previousResponseId = null,
    sharedMessages = [],
    imageInputs = [],
    requestedTools = createToolSelection(),
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
    const resolvedRequestedTools = resolveRequestedTools({
      requestedTools,
      routePolicy,
      enableWebSearchOverride,
      enableCodeInterpreterOverride
    });
    const request = {
      userText,
      previousResponseId,
      sharedMessages,
      imageInputs,
      requestedTools: resolvedRequestedTools,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride,
      storeOverride
    };
    const toolSupport = buildToolSupport({
      providerName: routePolicy.routeName,
      apiStyle: effectiveRequest.apiStyle,
      routePolicy,
      request
    });

    try {
      const hostedToolKinds = toolSupport.effectiveHostedTools;
      const reply = await provider.generateReply({
        ...request,
        enableWebSearchOverride: hostedToolKinds.includes(TOOL_KIND_WEB_SEARCH),
        enableCodeInterpreterOverride: hostedToolKinds.includes(TOOL_KIND_CODE_INTERPRETER)
      });

      return attachToolMetadata({
        reply,
        routePolicy,
        toolSupport,
        effectiveRequest
      });
    } catch (error) {
      const fallbackToolSupport = buildToolSupport({
        providerName: deepseekProvider?.routeName || '',
        apiStyle: deepseekProvider?.apiStyle || '',
        routePolicy,
        request,
        isFallback: true
      });
      const fallbackDecision = createProviderFallbackDecision({
        request,
        primaryToolSupport: toolSupport,
        fallbackToolSupport,
        deepseekProvider,
        primaryError: error
      });

      if (!fallbackDecision.shouldFallback) {
        throw error;
      }

      const fallbackReply = await deepseekProvider.generateReply({
        ...request,
        previousResponseId: null,
        enableWebSearchOverride: false,
        enableCodeInterpreterOverride: false
      });

      return buildProviderFallbackReply({
        primaryRoutePolicy: routePolicy,
        fallbackReply,
        fallbackToolSupport,
        requestedTools: request.requestedTools
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
    tryExecuteLocalToolRequest,
    generateReply
  };
}
