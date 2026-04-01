export const TOOL_KIND_WEB_SEARCH = 'web_search';
export const TOOL_KIND_CODE_INTERPRETER = 'code_interpreter';
export const TOOL_KIND_LOCAL_RUNTIME_STATE = 'local_runtime_state';
export const TOOL_KIND_LOCAL_SNAPSHOT_INSPECT = 'local_snapshot_inspect';

export const TOOL_CATEGORY_HOSTED = 'hosted';
export const TOOL_CATEGORY_LOCAL = 'local';

const TOOL_DESCRIPTORS = Object.freeze({
  [TOOL_KIND_WEB_SEARCH]: Object.freeze({
    kind: TOOL_KIND_WEB_SEARCH,
    displayLabel: 'web_search (Web Search)',
    category: TOOL_CATEGORY_HOSTED,
    readOnly: true,
    requiresResponsesApi: true,
    availableDuringFallback: false,
    supportsImages: false,
    allowPlannerStage: false
  }),
  [TOOL_KIND_CODE_INTERPRETER]: Object.freeze({
    kind: TOOL_KIND_CODE_INTERPRETER,
    displayLabel: 'code_interpreter (Code Interpreter)',
    category: TOOL_CATEGORY_HOSTED,
    readOnly: true,
    requiresResponsesApi: true,
    availableDuringFallback: false,
    supportsImages: false,
    allowPlannerStage: false
  }),
  [TOOL_KIND_LOCAL_RUNTIME_STATE]: Object.freeze({
    kind: TOOL_KIND_LOCAL_RUNTIME_STATE,
    displayLabel: 'local_runtime_state (Local Runtime State)',
    category: TOOL_CATEGORY_LOCAL,
    readOnly: true,
    requiresResponsesApi: false,
    availableDuringFallback: true,
    supportsImages: true,
    allowPlannerStage: false
  }),
  [TOOL_KIND_LOCAL_SNAPSHOT_INSPECT]: Object.freeze({
    kind: TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
    displayLabel: 'local_snapshot_inspect (Local Snapshot Inspect)',
    category: TOOL_CATEGORY_LOCAL,
    readOnly: true,
    requiresResponsesApi: false,
    availableDuringFallback: true,
    supportsImages: false,
    allowPlannerStage: false
  })
});

function normalizeToolKind(kind) {
  return typeof kind === 'string' ? kind.trim() : '';
}

export function getToolDescriptor(kind) {
  const normalizedKind = normalizeToolKind(kind);
  return TOOL_DESCRIPTORS[normalizedKind] ?? null;
}

export function listToolDescriptors() {
  return Object.values(TOOL_DESCRIPTORS);
}

export function normalizeToolKinds(kinds = []) {
  if (!Array.isArray(kinds)) {
    return [];
  }

  return [...new Set(
    kinds
      .map(normalizeToolKind)
      .filter((kind) => getToolDescriptor(kind) !== null)
  )];
}

export function createToolSelection({
  requested = [],
  required = []
} = {}) {
  const normalizedRequested = normalizeToolKinds(requested);
  const normalizedRequired = normalizeToolKinds(required).filter((kind) =>
    normalizedRequested.includes(kind)
  );

  return {
    requested: normalizedRequested,
    required: normalizedRequired
  };
}

export function createToolSuppression({
  toolKind,
  reason = ''
} = {}) {
  const normalizedToolKind = normalizeToolKind(toolKind);

  if (!normalizedToolKind) {
    throw new Error('Tool suppression requires a tool kind.');
  }

  return {
    toolKind: normalizedToolKind,
    reason: typeof reason === 'string' ? reason : ''
  };
}

export function buildRouteToolPolicy({
  enabledTools = []
} = {}) {
  return Object.freeze({
    enabledTools: normalizeToolKinds(enabledTools)
  });
}

export function formatToolLabel(toolKind) {
  return getToolDescriptor(toolKind)?.displayLabel ?? toolKind;
}

export function formatToolLabels(toolKinds = []) {
  const normalizedKinds = normalizeToolKinds(toolKinds);
  return normalizedKinds.length > 0
    ? normalizedKinds.map(formatToolLabel).join(', ')
    : 'none';
}

export function formatSuppressedTools(suppressedTools = []) {
  if (!Array.isArray(suppressedTools) || suppressedTools.length === 0) {
    return 'none';
  }

  return suppressedTools
    .filter((suppressedTool) => suppressedTool && typeof suppressedTool === 'object')
    .map((suppressedTool) => {
      const label = formatToolLabel(suppressedTool.toolKind);
      const reason = typeof suppressedTool.reason === 'string' && suppressedTool.reason
        ? suppressedTool.reason
        : 'unknown';
      return `${label} (${reason})`;
    })
    .join(', ');
}

export function deriveCompatibilityCapabilities({
  requestedTools,
  reasoningEffort = '',
  textVerbosity = '',
  needsResponsesCapabilities = false
} = {}) {
  const toolSelection = createToolSelection(requestedTools);

  return {
    reasoningEffort: typeof reasoningEffort === 'string' ? reasoningEffort : '',
    textVerbosity: typeof textVerbosity === 'string' ? textVerbosity : '',
    enableWebSearch: toolSelection.requested.includes(TOOL_KIND_WEB_SEARCH) ? true : false,
    enableCodeInterpreter: toolSelection.requested.includes(TOOL_KIND_CODE_INTERPRETER)
      ? true
      : false,
    needsResponsesCapabilities:
      needsResponsesCapabilities === true ||
      Boolean(
        toolSelection.requested.some((toolKind) => getToolDescriptor(toolKind)?.requiresResponsesApi === true) ||
          reasoningEffort ||
          textVerbosity
      )
  };
}
