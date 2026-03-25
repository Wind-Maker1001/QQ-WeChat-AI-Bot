import { resolveRouteDecision, DEFAULT_ADVANCED_TRIGGER_PREFIXES, normalizeTriggerPrefixes } from '../../domain/route-decision.mjs';
import {
  createOpenAIProvider,
  DEFAULT_ADVANCED_MODEL,
  DEFAULT_DEFAULT_MODEL
} from './openai-provider.mjs';

export function createLlmRouter({
  defaultRoute,
  advancedRoute,
  advancedTriggerPrefixes = DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  botPersona = ''
}) {
  const defaultProvider = createOpenAIProvider({
    routeName: 'default',
    apiKey: defaultRoute?.apiKey,
    model: defaultRoute?.model || DEFAULT_DEFAULT_MODEL,
    baseURL: defaultRoute?.baseURL ?? null,
    apiStyle: defaultRoute?.apiStyle,
    reasoningEffort: defaultRoute?.reasoningEffort,
    textVerbosity: defaultRoute?.textVerbosity,
    enableWebSearch: defaultRoute?.enableWebSearch,
    enableCodeInterpreter: defaultRoute?.enableCodeInterpreter,
    botPersona
  });

  const advancedProvider = createOpenAIProvider({
    routeName: 'advanced',
    apiKey: advancedRoute?.apiKey || defaultRoute?.apiKey,
    model: advancedRoute?.model || DEFAULT_ADVANCED_MODEL,
    baseURL: advancedRoute?.baseURL ?? null,
    apiStyle: advancedRoute?.apiStyle,
    reasoningEffort: advancedRoute?.reasoningEffort,
    textVerbosity: advancedRoute?.textVerbosity,
    enableWebSearch: advancedRoute?.enableWebSearch,
    enableCodeInterpreter: advancedRoute?.enableCodeInterpreter,
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
    botPersona
  });

  const normalizedAdvancedTriggerPrefixes = normalizeTriggerPrefixes(advancedTriggerPrefixes);

  function resolveRoute({ userText, imageInputs = [] }) {
    return resolveRouteDecision({
      userText,
      imageInputs,
      advancedTriggerPrefixes: normalizedAdvancedTriggerPrefixes,
      defaultRoute: {
        model: defaultProvider.model,
        apiStyle: defaultProvider.apiStyle,
        reasoningEffort: defaultProvider.reasoningEffort,
        textVerbosity: defaultProvider.textVerbosity,
        enableWebSearch: defaultProvider.enableWebSearch,
        enableCodeInterpreter: defaultProvider.enableCodeInterpreter
      },
      advancedRoute: {
        model: advancedProvider.model,
        apiStyle: advancedProvider.apiStyle,
        reasoningEffort: advancedProvider.reasoningEffort,
        textVerbosity: advancedProvider.textVerbosity,
        enableWebSearch: advancedProvider.enableWebSearch,
        enableCodeInterpreter: advancedProvider.enableCodeInterpreter
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
    generateReply
  };
}
