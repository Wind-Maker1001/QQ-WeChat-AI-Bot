import { analyzeMessageIntent } from './message-analysis-policy.mjs';
import { createToolSelection } from './tool-registry.mjs';

export {
  DEFAULT_ADVANCED_TRIGGER_PREFIXES,
  normalizeTriggerPrefixes
} from './message-analysis-policy.mjs';

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

function normalizeRouteDecisionRequestedTools(requestedTools, requestedCapabilities = {}) {
  const explicitSelection = createToolSelection(requestedTools);

  if (explicitSelection.requested.length > 0 || explicitSelection.required.length > 0) {
    return explicitSelection;
  }

  return createToolSelection({
    requested: [
      ...(requestedCapabilities?.enableWebSearch === true ? ['web_search'] : []),
      ...(requestedCapabilities?.enableCodeInterpreter === true ? ['code_interpreter'] : [])
    ]
  });
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
  requestedCapabilities = {},
  requestedTools = {}
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
  const normalizedRequestedTools =
    normalizeRouteDecisionRequestedTools(requestedTools, normalizedRequestedCapabilities);
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
    requestedCapabilities: normalizedRequestedCapabilities,
    requestedTools: normalizedRequestedTools
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
    requestedTools: getRouteDecisionRequestedTools(routeInfo),
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

export function getRouteDecisionRequestedTools(routeInfo) {
  return normalizeRouteDecisionRequestedTools(
    routeInfo?.requestedTools,
    routeInfo?.requestedCapabilities
  );
}

export function resolveRouteDecision({
  userText,
  imageInputs = [],
  advancedTriggerPrefixes,
  defaultRoute,
  advancedRoute
}) {
  const intentSignals = analyzeMessageIntent({
    userText,
    imageInputs,
    advancedTriggerPrefixes,
    defaultRoute,
    advancedRoute
  });
  const route = intentSignals.routeHint;
  const selectedRoute = route === 'advanced' ? advancedRoute : defaultRoute;

  return createRouteDecision({
    route,
    userText: intentSignals.normalizedText,
    imageCount: intentSignals.imageCount,
    selectedRoute,
    trigger: intentSignals.trigger,
    reasonGroups: {
      triggerReasons:
        intentSignals.trigger.kind === 'image'
          ? ['image']
          : intentSignals.trigger.kind === 'directive' && intentSignals.trigger.matchedPrefix
            ? [`directive:${intentSignals.trigger.matchedPrefix}`]
            : [],
      capabilityReasons: intentSignals.capabilityReasons,
      upgradeReasons: intentSignals.capabilityUpgradeApplied ? ['capability_upgrade'] : []
    },
    requestedCapabilities: intentSignals.requestedCapabilities,
    requestedTools: intentSignals.requestedTools
  });
}
