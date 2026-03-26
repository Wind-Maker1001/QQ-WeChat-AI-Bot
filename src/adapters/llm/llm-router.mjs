import { resolveRouteDecision, DEFAULT_ADVANCED_TRIGGER_PREFIXES, normalizeTriggerPrefixes } from '../../domain/route-decision.mjs';
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

export function createLlmRouter({
  defaultRoute,
  advancedRoute,
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

    return provider.generateReply({
      userText,
      previousResponseId,
      sharedMessages,
      imageInputs,
      reasoningEffortOverride,
      textVerbosityOverride,
      enableWebSearchOverride,
      enableCodeInterpreterOverride,
      storeOverride
    });
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
    advancedTriggerPrefixes: normalizedAdvancedTriggerPrefixes,
    resolveRoute,
    describeRequest,
    generateReply
  };
}
