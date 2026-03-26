export const EXECUTION_KIND_DIRECT = 'direct';
export const EXECUTION_KIND_DELIBERATION = 'deliberation';
export const EXECUTION_KIND_LOCAL_CAPABILITY_REPLY = 'local-capability-reply';

export const EXECUTION_STAGE_DIRECT = 'direct';
export const EXECUTION_STAGE_PLANNER = 'planner';
export const EXECUTION_STAGE_DRAFT = 'draft';
export const EXECUTION_STAGE_REWRITE = 'rewrite';
export const EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY = 'local-capability-reply';

export const EXECUTION_RECOVERY_PLANNER_FAILED = 'planner-failed';
export const EXECUTION_RECOVERY_REWRITE_FALLBACK_TO_DRAFT = 'rewrite-fallback-to-draft';

export const DELIBERATION_EXECUTION_STAGES = Object.freeze([
  EXECUTION_STAGE_PLANNER,
  EXECUTION_STAGE_DRAFT,
  EXECUTION_STAGE_REWRITE
]);

function normalizeStringList(values) {
  return Array.isArray(values)
    ? values.filter((value) => typeof value === 'string' && value)
    : [];
}

export function getExecutionSummary(kind) {
  if (kind === EXECUTION_KIND_DELIBERATION) {
    return DELIBERATION_EXECUTION_STAGES.join('->');
  }

  if (kind === EXECUTION_KIND_LOCAL_CAPABILITY_REPLY) {
    return 'local capability reply';
  }

  return EXECUTION_KIND_DIRECT;
}

export function getExecutionStages(kind) {
  if (kind === EXECUTION_KIND_DELIBERATION) {
    return [...DELIBERATION_EXECUTION_STAGES];
  }

  if (kind === EXECUTION_KIND_LOCAL_CAPABILITY_REPLY) {
    return [EXECUTION_STAGE_LOCAL_CAPABILITY_REPLY];
  }

  return [EXECUTION_STAGE_DIRECT];
}

export function createExecutionProjection({
  kind = EXECUTION_KIND_DIRECT,
  summary = '',
  stages,
  failedStage = '',
  completedStages = [],
  degraded = false,
  recoveries = []
} = {}) {
  const normalizedKind =
    kind === EXECUTION_KIND_DELIBERATION ||
    kind === EXECUTION_KIND_LOCAL_CAPABILITY_REPLY
      ? kind
      : EXECUTION_KIND_DIRECT;
  const normalizedStages =
    normalizeStringList(stages).length > 0
      ? normalizeStringList(stages)
      : getExecutionStages(normalizedKind);
  const normalizedRecoveries = normalizeStringList(recoveries);

  return {
    kind: normalizedKind,
    summary:
      typeof summary === 'string' && summary ? summary : getExecutionSummary(normalizedKind),
    stages: normalizedStages,
    failedStage: typeof failedStage === 'string' ? failedStage : '',
    completedStages: normalizeStringList(completedStages),
    degraded: degraded === true || normalizedRecoveries.length > 0,
    recoveries: normalizedRecoveries
  };
}
