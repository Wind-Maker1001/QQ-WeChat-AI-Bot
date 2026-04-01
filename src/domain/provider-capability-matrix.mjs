import { API_STYLE_RESPONSES } from './llm-request-policy.mjs';
import {
  TOOL_CATEGORY_HOSTED,
  TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
  createToolSuppression,
  createToolSelection,
  getToolDescriptor,
  normalizeToolKinds
} from './tool-registry.mjs';

export const TOOL_SUPPRESSION_REASON_RESPONSES_API_REQUIRED = 'requires_responses_api';
export const TOOL_SUPPRESSION_REASON_FALLBACK_UNSUPPORTED = 'fallback_unsupported';
export const TOOL_SUPPRESSION_REASON_IMAGE_UNSUPPORTED = 'image_unsupported';
export const TOOL_SUPPRESSION_REASON_NOT_ENABLED_FOR_ROUTE = 'not_enabled_for_route';
export const TOOL_SUPPRESSION_REASON_CONTEXT_UNAVAILABLE = 'context_unavailable';
export const TOOL_SUPPRESSION_REASON_PLANNER_UNSUPPORTED = 'planner_unsupported';

export function createProviderToolSupport({
  providerName = '',
  apiStyle = '',
  routeToolPolicy = {},
  requestedTools = {},
  imageCount = 0,
  allowPlannerStage = true,
  isFallback = false,
  snapshotInspectorAvailable = false
} = {}) {
  const toolSelection = createToolSelection(requestedTools);
  const enabledTools = normalizeToolKinds(routeToolPolicy?.enabledTools);
  const supportedTools = [];
  const effectiveTools = [];
  const effectiveHostedTools = [];
  const effectiveLocalTools = [];
  const suppressedTools = [];

  for (const toolKind of toolSelection.requested) {
    const descriptor = getToolDescriptor(toolKind);

    if (!descriptor) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_CONTEXT_UNAVAILABLE
        })
      );
      continue;
    }

    if (
      descriptor.category === TOOL_CATEGORY_HOSTED &&
      enabledTools.length > 0 &&
      !enabledTools.includes(toolKind)
    ) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_NOT_ENABLED_FOR_ROUTE
        })
      );
      continue;
    }

    if (descriptor.availableDuringFallback !== true && isFallback) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_FALLBACK_UNSUPPORTED
        })
      );
      continue;
    }

    if (descriptor.requiresResponsesApi && apiStyle !== API_STYLE_RESPONSES) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_RESPONSES_API_REQUIRED
        })
      );
      continue;
    }

    if (descriptor.supportsImages !== true && imageCount > 0) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_IMAGE_UNSUPPORTED
        })
      );
      continue;
    }

    if (descriptor.allowPlannerStage !== true && allowPlannerStage === false) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_PLANNER_UNSUPPORTED
        })
      );
      continue;
    }

    if (toolKind === TOOL_KIND_LOCAL_SNAPSHOT_INSPECT && snapshotInspectorAvailable !== true) {
      suppressedTools.push(
        createToolSuppression({
          toolKind,
          reason: TOOL_SUPPRESSION_REASON_CONTEXT_UNAVAILABLE
        })
      );
      continue;
    }

    supportedTools.push(toolKind);
    effectiveTools.push(toolKind);

    if (descriptor.category === TOOL_CATEGORY_HOSTED) {
      effectiveHostedTools.push(toolKind);
    } else {
      effectiveLocalTools.push(toolKind);
    }
  }

  return {
    providerName: typeof providerName === 'string' ? providerName : '',
    apiStyle: typeof apiStyle === 'string' ? apiStyle : '',
    requestedTools: toolSelection,
    routeToolPolicy: {
      enabledTools
    },
    supportedTools,
    effectiveTools,
    effectiveHostedTools,
    effectiveLocalTools,
    suppressedTools,
    hasSuppressedRequiredTools: toolSelection.required.some((requiredToolKind) =>
      suppressedTools.some((suppressedTool) => suppressedTool.toolKind === requiredToolKind)
    )
  };
}
