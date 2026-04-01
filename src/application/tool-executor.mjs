import fs from 'node:fs/promises';
import path from 'node:path';

import {
  createToolExecutionRequest,
  createToolExecutionResult
} from '../domain/tool-execution-contract.mjs';
import {
  TOOL_KIND_LOCAL_RUNTIME_STATE,
  TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
  formatSuppressedTools,
  formatToolLabels
} from '../domain/tool-registry.mjs';

async function listSnapshotArchives({ cwd = process.cwd() } = {}) {
  const snapshotRootPath = path.resolve(cwd, 'artifacts', 'state-snapshots');

  try {
    const entries = await fs.readdir(snapshotRootPath, { withFileTypes: true });
    const fileEntries = [];

    for (const entry of entries) {
      if (!entry.isFile() || !entry.name.toLowerCase().endsWith('.zip')) {
        continue;
      }

      const fullPath = path.join(snapshotRootPath, entry.name);
      const stats = await fs.stat(fullPath);
      fileEntries.push({
        fullPath,
        fileName: entry.name,
        mtimeMs: stats.mtimeMs
      });
    }

    return fileEntries.sort((left, right) => right.mtimeMs - left.mtimeMs);
  } catch {
    return [];
  }
}

function buildLocalRuntimeStateReply(executionRequest) {
  const requestDescriptor = executionRequest.requestDescriptor ?? {};
  const route = requestDescriptor.route || executionRequest.routeInfo?.route || 'default';
  const apiStyle = requestDescriptor.effectiveApiStyle || requestDescriptor.configuredApiStyle || 'unknown';
  const model = requestDescriptor.model || requestDescriptor.configuredModel || 'unknown-model';
  const effectiveTools = Array.isArray(requestDescriptor.effectiveTools)
    ? requestDescriptor.effectiveTools
    : [];
  const suppressedTools = Array.isArray(requestDescriptor.suppressedTools)
    ? requestDescriptor.suppressedTools
    : [];

  return createToolExecutionResult({
    toolKind: TOOL_KIND_LOCAL_RUNTIME_STATE,
    text: [
      `Current route: ${route}`,
      `Provider: ${apiStyle} / ${model}`,
      `Effective tools: ${formatToolLabels(effectiveTools)}`,
      `Suppressed tools: ${formatSuppressedTools(suppressedTools)}`
    ].join('\n')
  });
}

async function buildLocalSnapshotInspectReply(executionRequest, { cwd = process.cwd() } = {}) {
  const latestArchives = await listSnapshotArchives({ cwd });

  if (latestArchives.length === 0) {
    return createToolExecutionResult({
      toolKind: TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
      text: 'No local state snapshots were found under artifacts/state-snapshots.'
    });
  }

  const latestSummary = latestArchives
    .slice(0, 3)
    .map((archive, index) => `${index + 1}. ${archive.fileName}`)
    .join('\n');

  return createToolExecutionResult({
    toolKind: TOOL_KIND_LOCAL_SNAPSHOT_INSPECT,
    text: [
      'Latest local state snapshots on disk:',
      latestSummary,
      'Detailed restore diff/advice remains available through the desktop snapshot workflow.'
    ].join('\n')
  });
}

export function createToolExecutor({
  cwd = process.cwd()
} = {}) {
  async function executeTool(request) {
    const executionRequest = createToolExecutionRequest(request);

    if (executionRequest.toolKind === TOOL_KIND_LOCAL_RUNTIME_STATE) {
      return buildLocalRuntimeStateReply(executionRequest);
    }

    if (executionRequest.toolKind === TOOL_KIND_LOCAL_SNAPSHOT_INSPECT) {
      return buildLocalSnapshotInspectReply(executionRequest, { cwd });
    }

    return null;
  }

  async function tryExecuteLocalTools({
    userText = '',
    routeInfo = null,
    requestDescriptor = null
  } = {}) {
    const localToolKinds = Array.isArray(requestDescriptor?.effectiveLocalTools)
      ? requestDescriptor.effectiveLocalTools
      : [];

    if (localToolKinds.length === 0) {
      return null;
    }

    const results = [];

    for (const toolKind of localToolKinds) {
      const result = await executeTool({
        toolKind,
        userText,
        routeInfo,
        requestDescriptor
      });

      if (result?.text) {
        results.push(result);
      }
    }

    if (results.length === 0) {
      return null;
    }

    return {
      text: results.map((result) => result.text).join('\n\n'),
      toolKinds: results.map((result) => result.toolKind)
    };
  }

  return {
    executeTool,
    tryExecuteLocalTools
  };
}
