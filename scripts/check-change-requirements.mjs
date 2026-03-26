import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const STRATEGY_CHANGED_PATTERNS = [
  'src/application/message-orchestrator.mjs',
  'src/application/message-turn-strategy.mjs',
  'src/application/message-turn-spec.mjs',
  'src/application/llm-execution-plan.mjs',
  'src/application/deliberation-executor.mjs',
  'src/domain/route-decision.mjs',
  'src/domain/llm-request-policy.mjs',
  'src/domain/execution-projection.mjs',
  'src/adapters/llm/llm-router.mjs',
  'src/adapters/llm/openai-provider.mjs'
];

const STRATEGY_COVERAGE_PATTERNS = [
  'tests/llm-request-policy.test.mjs',
  'tests/route-decision.test.mjs',
  'tests/llm-execution-plan.test.mjs',
  'tests/message-turn-spec.test.mjs',
  'tests/message-turn-strategy.test.mjs',
  'tests/deliberation-executor.test.mjs',
  'tests/message-orchestrator-local-reply.test.mjs',
  'tests/llm-router.test.mjs',
  'tests/openai-provider-config.test.mjs',
  'tests/supervisor.e2e.test.mjs'
];

const CONTROL_CONTRACT_CHANGED_PATTERNS = [
  'src/app/control-api.mjs',
  'src/adapters/config/',
  'desktop/QQAIBot.Desktop/Models/Backend',
  'desktop/QQAIBot.Desktop/Services/BackendControlApiService.cs'
];

const CONTROL_CONTRACT_COVERAGE_PATTERNS = [
  'tests/control-config-contract.test.mjs',
  'tests/control-config-mapper.test.mjs',
  'tests/control-api.integration.test.mjs',
  'tests/env-file-store.test.mjs',
  'desktop/QQAIBot.Desktop.Tests/Program.cs'
];

const DESKTOP_CONTROL_CHANGED_PATTERNS = [
  'desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs',
  'desktop/QQAIBot.Desktop/Services/Backend',
  'desktop/QQAIBot.Desktop/Services/DesktopControlPlaneFeedback.cs'
];

const DESKTOP_CONTROL_COVERAGE_PATTERNS = [
  'desktop/QQAIBot.Desktop.Tests/Program.cs'
];

const CHANGE_RULES = [
  {
    id: 'strategy-coverage',
    changedPatterns: STRATEGY_CHANGED_PATTERNS,
    coveragePatterns: STRATEGY_COVERAGE_PATTERNS,
    message:
      'Strategy/runtime-execution changes must include related backend test updates when touching key strategy ownership files.'
  },
  {
    id: 'control-contract-coverage',
    changedPatterns: CONTROL_CONTRACT_CHANGED_PATTERNS,
    coveragePatterns: CONTROL_CONTRACT_COVERAGE_PATTERNS,
    message:
      'Control API/config contract changes must include contract-test or desktop regression updates.'
  },
  {
    id: 'desktop-control-coverage',
    changedPatterns: DESKTOP_CONTROL_CHANGED_PATTERNS,
    coveragePatterns: DESKTOP_CONTROL_COVERAGE_PATTERNS,
    message:
      'Desktop control-plane changes must include desktop regression updates.'
  }
];

function normalizePath(filePath) {
  return filePath.replace(/\\/g, '/').trim();
}

function matchesAnyPattern(filePath, patterns) {
  return patterns.some((pattern) => filePath === pattern || filePath.startsWith(pattern));
}

export function evaluateChangeRequirements(changedFiles) {
  const normalizedChangedFiles = [...new Set((changedFiles || []).map(normalizePath).filter(Boolean))];
  const failures = [];

  for (const rule of CHANGE_RULES) {
    const touchedRuleFiles = normalizedChangedFiles.filter((filePath) =>
      matchesAnyPattern(filePath, rule.changedPatterns)
    );

    if (touchedRuleFiles.length === 0) {
      continue;
    }

    const touchedCoverageFiles = normalizedChangedFiles.filter((filePath) =>
      matchesAnyPattern(filePath, rule.coveragePatterns)
    );

    if (touchedCoverageFiles.length === 0) {
      failures.push({
        id: rule.id,
        message: rule.message,
        touchedRuleFiles
      });
    }
  }

  return {
    changedFiles: normalizedChangedFiles,
    failures
  };
}

function runGit(args, { allowFailure = false } = {}) {
  try {
    return execFileSync('git', args, {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe']
    }).trim();
  } catch (error) {
    if (allowFailure) {
      return '';
    }

    throw error;
  }
}

function getChangedFilesFromGit() {
  const eventName = process.env.GITHUB_EVENT_NAME || '';
  const baseRef = process.env.GITHUB_BASE_REF || '';
  const eventBefore = process.env.GITHUB_EVENT_BEFORE || '';

  if (eventName === 'pull_request' && baseRef) {
    runGit(['fetch', '--no-tags', '--depth=1', 'origin', baseRef], {
      allowFailure: true
    });
    const mergeBase = runGit(['merge-base', 'HEAD', `origin/${baseRef}`], {
      allowFailure: true
    });

    if (mergeBase) {
      return runGit(['diff', '--name-only', `${mergeBase}...HEAD`], {
        allowFailure: true
      })
        .split(/\r?\n/)
        .filter(Boolean);
    }
  }

  if (eventBefore && !/^0+$/.test(eventBefore)) {
    const diffOutput = runGit(['diff', '--name-only', `${eventBefore}..HEAD`], {
      allowFailure: true
    });

    if (diffOutput) {
      return diffOutput.split(/\r?\n/).filter(Boolean);
    }
  }

  const headParentDiff = runGit(['diff', '--name-only', 'HEAD^', 'HEAD'], {
    allowFailure: true
  });
  if (headParentDiff) {
    return headParentDiff.split(/\r?\n/).filter(Boolean);
  }

  const statusOutput = runGit(['status', '--porcelain'], {
    allowFailure: true
  });
  if (!statusOutput) {
    return [];
  }

  return statusOutput
    .split(/\r?\n/)
    .filter(Boolean)
    .map((line) => normalizePath(line.slice(3)));
}

function main() {
  const changedFiles =
    process.argv.length > 2 ? process.argv.slice(2).map(normalizePath) : getChangedFilesFromGit();
  const result = evaluateChangeRequirements(changedFiles);

  if (result.failures.length === 0) {
    console.log('Change requirements check passed.');
    return;
  }

  console.error('Change requirements check failed.');

  for (const failure of result.failures) {
    console.error(`- [${failure.id}] ${failure.message}`);

    for (const filePath of failure.touchedRuleFiles) {
      console.error(`  touched: ${filePath}`);
    }
  }

  process.exit(1);
}

const currentFilePath = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]) === currentFilePath) {
  main();
}
