import { createToolSelection } from './tool-registry.mjs';

export function createToolExecutionRequest({
  toolKind = '',
  userText = '',
  routeInfo = null,
  requestDescriptor = null
} = {}) {
  return {
    toolKind: typeof toolKind === 'string' ? toolKind : '',
    userText: typeof userText === 'string' ? userText : '',
    routeInfo: routeInfo && typeof routeInfo === 'object' ? routeInfo : null,
    requestDescriptor:
      requestDescriptor && typeof requestDescriptor === 'object' ? requestDescriptor : null
  };
}

export function createToolExecutionResult({
  toolKind = '',
  text = ''
} = {}) {
  return {
    toolKind: typeof toolKind === 'string' ? toolKind : '',
    text: typeof text === 'string' ? text : ''
  };
}

export function createToolExecutorContract({
  effectiveTools = [],
  suppressedTools = []
} = {}) {
  return {
    effectiveTools: createToolSelection({
      requested: effectiveTools
    }).requested,
    suppressedTools: Array.isArray(suppressedTools) ? suppressedTools : []
  };
}
