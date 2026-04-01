function normalizeExecutionProjection(projection) {
  if (!projection || typeof projection !== 'object') {
    return null;
  }

  return {
    kind: typeof projection.kind === 'string' ? projection.kind : '',
    summary: typeof projection.summary === 'string' ? projection.summary : '',
    stages: Array.isArray(projection.stages)
      ? projection.stages.filter((stage) => typeof stage === 'string')
      : [],
    failedStage: typeof projection.failedStage === 'string' ? projection.failedStage : '',
    completedStages: Array.isArray(projection.completedStages)
      ? projection.completedStages.filter((stage) => typeof stage === 'string')
      : [],
    degraded: projection.degraded === true,
    recoveries: Array.isArray(projection.recoveries)
      ? projection.recoveries.filter((recovery) => typeof recovery === 'string')
      : []
  };
}

function normalizeDecisionSummary(summary) {
  if (!summary || typeof summary !== 'object') {
    return null;
  }

  const trigger =
    summary.trigger && typeof summary.trigger === 'object'
      ? {
          kind: typeof summary.trigger.kind === 'string' ? summary.trigger.kind : '',
          matchedPrefix:
            typeof summary.trigger.matchedPrefix === 'string' ? summary.trigger.matchedPrefix : ''
        }
      : null;
  const reasonGroups =
    summary.reasonGroups && typeof summary.reasonGroups === 'object'
      ? {
          triggerReasons: Array.isArray(summary.reasonGroups.triggerReasons)
            ? summary.reasonGroups.triggerReasons.filter((tag) => typeof tag === 'string')
            : [],
          capabilityReasons: Array.isArray(summary.reasonGroups.capabilityReasons)
            ? summary.reasonGroups.capabilityReasons.filter((tag) => typeof tag === 'string')
            : [],
          upgradeReasons: Array.isArray(summary.reasonGroups.upgradeReasons)
            ? summary.reasonGroups.upgradeReasons.filter((tag) => typeof tag === 'string')
            : []
        }
      : null;
  const requestedCapabilities =
    summary.requestedCapabilities && typeof summary.requestedCapabilities === 'object'
      ? {
          reasoningEffort:
            typeof summary.requestedCapabilities.reasoningEffort === 'string'
              ? summary.requestedCapabilities.reasoningEffort
              : '',
          textVerbosity:
            typeof summary.requestedCapabilities.textVerbosity === 'string'
              ? summary.requestedCapabilities.textVerbosity
              : '',
          enableWebSearch:
            typeof summary.requestedCapabilities.enableWebSearch === 'boolean'
              ? summary.requestedCapabilities.enableWebSearch
              : false,
          enableCodeInterpreter:
            typeof summary.requestedCapabilities.enableCodeInterpreter === 'boolean'
              ? summary.requestedCapabilities.enableCodeInterpreter
              : false,
          needsResponsesCapabilities:
            typeof summary.requestedCapabilities.needsResponsesCapabilities === 'boolean'
              ? summary.requestedCapabilities.needsResponsesCapabilities
              : false
        }
      : null;
  const requestedTools =
    summary.requestedTools && typeof summary.requestedTools === 'object'
      ? {
          requested: Array.isArray(summary.requestedTools.requested)
            ? summary.requestedTools.requested.filter((tool) => typeof tool === 'string')
            : [],
          required: Array.isArray(summary.requestedTools.required)
            ? summary.requestedTools.required.filter((tool) => typeof tool === 'string')
            : []
        }
      : null;
  const suppressedTools = Array.isArray(summary.suppressedTools)
    ? summary.suppressedTools
        .filter((suppressedTool) => suppressedTool && typeof suppressedTool === 'object')
        .map((suppressedTool) => ({
          toolKind:
            typeof suppressedTool.toolKind === 'string' ? suppressedTool.toolKind : '',
          reason: typeof suppressedTool.reason === 'string' ? suppressedTool.reason : ''
        }))
    : [];

  return {
    trigger,
    reasonTags: Array.isArray(summary.reasonTags)
      ? summary.reasonTags.filter((tag) => typeof tag === 'string')
      : [],
    reasonGroups,
    requestedCapabilities,
    requestedTools,
    suppressedTools,
    routeReason: typeof summary.routeReason === 'string' ? summary.routeReason : '',
    matchedPrefix: typeof summary.matchedPrefix === 'string' ? summary.matchedPrefix : ''
  };
}

export function normalizeLlmRequestStatus(status) {
  if (!status || typeof status !== 'object') {
    return null;
  }

  return {
    capturedAt: typeof status.capturedAt === 'string' ? status.capturedAt : '',
    channelId: typeof status.channelId === 'string' ? status.channelId : '',
    route: typeof status.route === 'string' ? status.route : '',
    routeReason: typeof status.routeReason === 'string' ? status.routeReason : '',
    matchedPrefix: typeof status.matchedPrefix === 'string' ? status.matchedPrefix : '',
    configuredModel: typeof status.configuredModel === 'string' ? status.configuredModel : '',
    model: typeof status.model === 'string' ? status.model : '',
    configuredApiStyle:
      typeof status.configuredApiStyle === 'string' ? status.configuredApiStyle : '',
    effectiveApiStyle: typeof status.effectiveApiStyle === 'string' ? status.effectiveApiStyle : '',
    configuredReasoningEffort:
      typeof status.configuredReasoningEffort === 'string' ? status.configuredReasoningEffort : '',
    effectiveReasoningEffort:
      typeof status.effectiveReasoningEffort === 'string' ? status.effectiveReasoningEffort : '',
    configuredTextVerbosity:
      typeof status.configuredTextVerbosity === 'string' ? status.configuredTextVerbosity : '',
    effectiveTextVerbosity:
      typeof status.effectiveTextVerbosity === 'string' ? status.effectiveTextVerbosity : '',
    configuredTools: Array.isArray(status.configuredTools)
      ? status.configuredTools.filter((tool) => typeof tool === 'string')
      : [],
    requestedTools:
      status.requestedTools && typeof status.requestedTools === 'object'
        ? {
            requested: Array.isArray(status.requestedTools.requested)
              ? status.requestedTools.requested.filter((tool) => typeof tool === 'string')
              : [],
            required: Array.isArray(status.requestedTools.required)
              ? status.requestedTools.required.filter((tool) => typeof tool === 'string')
              : []
          }
        : null,
    effectiveTools: Array.isArray(status.effectiveTools)
      ? status.effectiveTools.filter((tool) => typeof tool === 'string')
      : [],
    suppressedTools: Array.isArray(status.suppressedTools)
      ? status.suppressedTools
          .filter((suppressedTool) => suppressedTool && typeof suppressedTool === 'object')
          .map((suppressedTool) => ({
            toolKind:
              typeof suppressedTool.toolKind === 'string' ? suppressedTool.toolKind : '',
            reason: typeof suppressedTool.reason === 'string' ? suppressedTool.reason : ''
          }))
      : [],
    executionKind: typeof status.executionKind === 'string' ? status.executionKind : '',
    executionSummary: typeof status.executionSummary === 'string' ? status.executionSummary : '',
    executionProjection: normalizeExecutionProjection(status.executionProjection),
    imageCount: typeof status.imageCount === 'number' ? status.imageCount : 0,
    decisionSummary: normalizeDecisionSummary(status.decisionSummary),
    chatId: typeof status.chatId === 'string' ? status.chatId : '',
    userId: typeof status.userId === 'string' ? status.userId : '',
    responseId: typeof status.responseId === 'string' ? status.responseId : ''
  };
}

export function normalizeLlmFailureStatus(status) {
  if (!status || typeof status !== 'object') {
    return null;
  }

  return {
    capturedAt: typeof status.capturedAt === 'string' ? status.capturedAt : '',
    channelId: typeof status.channelId === 'string' ? status.channelId : '',
    route: typeof status.route === 'string' ? status.route : '',
    routeReason: typeof status.routeReason === 'string' ? status.routeReason : '',
    matchedPrefix: typeof status.matchedPrefix === 'string' ? status.matchedPrefix : '',
    decisionSummary: normalizeDecisionSummary(status.decisionSummary),
    requestedTools:
      status.requestedTools && typeof status.requestedTools === 'object'
        ? {
            requested: Array.isArray(status.requestedTools.requested)
              ? status.requestedTools.requested.filter((tool) => typeof tool === 'string')
              : [],
            required: Array.isArray(status.requestedTools.required)
              ? status.requestedTools.required.filter((tool) => typeof tool === 'string')
              : []
          }
        : null,
    suppressedTools: Array.isArray(status.suppressedTools)
      ? status.suppressedTools
          .filter((suppressedTool) => suppressedTool && typeof suppressedTool === 'object')
          .map((suppressedTool) => ({
            toolKind:
              typeof suppressedTool.toolKind === 'string' ? suppressedTool.toolKind : '',
            reason: typeof suppressedTool.reason === 'string' ? suppressedTool.reason : ''
          }))
      : [],
    executionKind: typeof status.executionKind === 'string' ? status.executionKind : '',
    executionSummary: typeof status.executionSummary === 'string' ? status.executionSummary : '',
    executionProjection: normalizeExecutionProjection(status.executionProjection),
    chatId: typeof status.chatId === 'string' ? status.chatId : '',
    userId: typeof status.userId === 'string' ? status.userId : '',
    error: typeof status.error === 'string' ? status.error : ''
  };
}
