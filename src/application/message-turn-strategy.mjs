import {
  appendConversationMessages,
  createLlmReplyOutcome
} from '../domain/llm-reply-outcome.mjs';
import {
  createExecutionProjection,
  EXECUTION_KIND_DIRECT
} from '../domain/execution-projection.mjs';
import { runDeliberationPipeline } from './deliberation-executor.mjs';
import { tryBuildCurrentTurnCapabilityReply } from './current-turn-capability-reply.mjs';
import { buildMessageTurnSpec } from './message-turn-spec.mjs';

function describeRequest(llmRouter, request) {
  if (typeof llmRouter?.describeRequest !== 'function') {
    return null;
  }

  return llmRouter.describeRequest(request);
}

function buildRequestStage(llmRouter, request) {
  return {
    request,
    descriptor: describeRequest(llmRouter, request)
  };
}

function buildExecutionAssembly(llmRouter, executionPlan) {
  return {
    mode: executionPlan.mode,
    direct: buildRequestStage(llmRouter, executionPlan.directRequest),
    deliberation: executionPlan.deliberation
      ? {
          planner: buildRequestStage(llmRouter, executionPlan.deliberation.plannerRequest),
          draft: buildRequestStage(llmRouter, executionPlan.deliberation.draftRequest),
          rewrite: buildRequestStage(llmRouter, executionPlan.deliberation.rewriteRequest)
        }
      : null
  };
}

function buildExecutionProjection(executionKind) {
  return createExecutionProjection({
    kind: executionKind
  });
}

function attachExecutionMetadata(reply, strategy) {
  const executionProjection =
    reply?.executionProjection && typeof reply.executionProjection === 'object'
      ? reply.executionProjection
      : createExecutionProjection({
          ...strategy.executionProjection,
          completedStages: normalizeProjectionStages(strategy.executionProjection?.stages)
        });

  return {
    ...reply,
    executionKind: executionProjection.kind || strategy.executionKind,
    executionSummary: executionProjection.summary || strategy.executionSummary,
    executionProjection
  };
}

function normalizeProjectionStages(stages) {
  return Array.isArray(stages) ? stages.filter((stage) => typeof stage === 'string' && stage) : [];
}

function buildExecutionFailureProjection(strategy, {
  failedStage = '',
  completedStages = [],
  degraded = false,
  recoveries = []
} = {}) {
  const baseProjection =
    strategy?.executionProjection && typeof strategy.executionProjection === 'object'
      ? strategy.executionProjection
      : buildExecutionProjection(strategy?.executionKind || EXECUTION_KIND_DIRECT);
  const normalizedFailedStage =
    typeof failedStage === 'string' && failedStage
      ? failedStage
      : baseProjection.stages[0] || baseProjection.kind || '';

  return createExecutionProjection({
    ...baseProjection,
    failedStage: normalizedFailedStage,
    completedStages: normalizeProjectionStages(completedStages),
    degraded,
    recoveries
  });
}

function attachExecutionFailureProjection(error, executionProjection) {
  const normalizedProjection =
    executionProjection && typeof executionProjection === 'object' ? executionProjection : null;

  if (error instanceof Error) {
    error.executionFailureProjection = normalizedProjection;
    return error;
  }

  const wrappedError = new Error(String(error));
  wrappedError.cause = error;
  wrappedError.executionFailureProjection = normalizedProjection;
  return wrappedError;
}

function buildLocalCapabilityReply({
  turnSpec,
  requestDescriptor,
  replyText
}) {
  const normalizedRequestDescriptor =
    requestDescriptor && typeof requestDescriptor === 'object' ? requestDescriptor : {};
  const replyOutcome = createLlmReplyOutcome({
    text: replyText,
    responseId: null,
    conversationDelta: {
      previousResponseId: null,
      clearPreviousResponseId: true,
      sharedMessages: appendConversationMessages(
        turnSpec.executionPlan.sessionContext.sharedMessages,
        turnSpec.userText,
        replyText
      )
    }
  });

  return {
    ...replyOutcome,
    route: normalizedRequestDescriptor.route || turnSpec.route,
    model: normalizedRequestDescriptor.model || '',
    apiStyle:
      normalizedRequestDescriptor.effectiveApiStyle ||
      normalizedRequestDescriptor.configuredApiStyle ||
      '',
    configuredApiStyle: normalizedRequestDescriptor.configuredApiStyle || '',
    effectiveApiStyle: normalizedRequestDescriptor.effectiveApiStyle || '',
    configuredReasoningEffort: normalizedRequestDescriptor.configuredReasoningEffort || '',
    effectiveReasoningEffort: normalizedRequestDescriptor.effectiveReasoningEffort || '',
    configuredTextVerbosity: normalizedRequestDescriptor.configuredTextVerbosity || '',
    effectiveTextVerbosity: normalizedRequestDescriptor.effectiveTextVerbosity || '',
    configuredEnableWebSearch: normalizedRequestDescriptor.configuredEnableWebSearch === true,
    effectiveEnableWebSearch: normalizedRequestDescriptor.effectiveEnableWebSearch === true,
    configuredEnableCodeInterpreter:
      normalizedRequestDescriptor.configuredEnableCodeInterpreter === true,
    effectiveEnableCodeInterpreter:
      normalizedRequestDescriptor.effectiveEnableCodeInterpreter === true,
    configuredTools: Array.isArray(normalizedRequestDescriptor.configuredTools)
      ? normalizedRequestDescriptor.configuredTools
      : [],
    effectiveTools: Array.isArray(normalizedRequestDescriptor.effectiveTools)
      ? normalizedRequestDescriptor.effectiveTools
      : []
  };
}

// Stable assembly point for explaining how a route decision becomes executable LLM work.
export function buildMessageTurnStrategy({
  channelId,
  chatId,
  userId,
  routeInfo,
  routeState,
  conversationState,
  preparedImageInputs = [],
  llmRouter
}) {
  const turnSpec = buildMessageTurnSpec({
    channelId,
    chatId,
    userId,
    routeInfo,
    routeState,
    conversationState,
    preparedImageInputs
  });
  const executionAssembly = buildExecutionAssembly(llmRouter, turnSpec.executionPlan);
  const localCapabilityReplyText = tryBuildCurrentTurnCapabilityReply({
    userText: turnSpec.userText,
    requestDescriptor: executionAssembly.direct.descriptor
  });

  return {
    turnSpec,
    executionAssembly,
    executionKind: localCapabilityReplyText ? 'local-capability-reply' : executionAssembly.mode,
    executionSummary: buildExecutionProjection(
      localCapabilityReplyText ? 'local-capability-reply' : executionAssembly.mode
    ).summary,
    executionProjection: buildExecutionProjection(
      localCapabilityReplyText ? 'local-capability-reply' : executionAssembly.mode
    ),
    localCapabilityReplyText
  };
}

export async function executeMessageTurnStrategy({
  strategy,
  llmRouter,
  logger
}) {
  if (!strategy || typeof strategy !== 'object') {
    throw new Error('Message turn strategy is required.');
  }

  try {
    if (strategy.executionKind === 'local-capability-reply') {
      return attachExecutionMetadata(
        buildLocalCapabilityReply({
          turnSpec: strategy.turnSpec,
          requestDescriptor: strategy.executionAssembly.direct.descriptor,
          replyText: strategy.localCapabilityReplyText
        }),
        strategy
      );
    }

    if (strategy.executionAssembly.mode === 'deliberation') {
      return attachExecutionMetadata(
        await runDeliberationPipeline({
          llmRouter,
          executionPlan: strategy.turnSpec.executionPlan,
          logger
        }),
        strategy
      );
    }

    return attachExecutionMetadata(
      await llmRouter.generateReply(strategy.executionAssembly.direct.request),
      strategy
    );
  } catch (error) {
    if (error?.executionFailureProjection) {
      throw error;
    }

    throw attachExecutionFailureProjection(
      error,
      buildExecutionFailureProjection(strategy)
    );
  }
}

export const __test__ = Object.freeze({
  buildExecutionProjection,
  buildExecutionFailureProjection
});
