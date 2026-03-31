import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const STRATEGY_CHANGED_PATTERNS = [
  'src/application/message-orchestrator.mjs',
  'src/application/message-turn-strategy.mjs',
  'src/application/message-turn-spec.mjs',
  'src/application/llm-execution-plan.mjs',
  'src/application/deliberation-executor.mjs',
  'src/domain/message-analysis-policy.mjs',
  'src/domain/provider-fallback-policy.mjs',
  'src/domain/route-decision.mjs',
  'src/domain/llm-request-policy.mjs',
  'src/domain/execution-projection.mjs',
  'src/app/supervisor-contract-normalizer.mjs',
  'src/adapters/llm/llm-router.mjs',
  'src/adapters/llm/openai-provider.mjs'
];

const STRATEGY_COVERAGE_PATTERNS = [
  'tests/message-analysis-policy.test.mjs',
  'tests/provider-fallback-policy.test.mjs',
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

const ARCHITECTURE_COVERAGE_PATTERNS = [
  'tests/architecture-boundaries.test.mjs'
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
  },
  {
    id: 'architecture-boundaries-coverage',
    changedPatterns: [
      'src/domain/',
      'desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs',
      'src/domain/message-analysis-policy.mjs',
      'src/domain/provider-fallback-policy.mjs',
      'src/app/supervisor-contract-normalizer.mjs'
    ],
    coveragePatterns: ARCHITECTURE_COVERAGE_PATTERNS,
    message:
      'Boundary-sensitive changes must include architecture boundary test updates.'
  }
];

const ALLOWED_POLICY_IMPORTERS = new Map([
  ['message-analysis-policy.mjs', new Set(['src/domain/route-decision.mjs', 'src/adapters/llm/llm-router.mjs'])],
  ['provider-fallback-policy.mjs', new Set(['src/adapters/llm/llm-router.mjs'])],
  ['supervisor-contract-normalizer.mjs', new Set(['src/index.mjs'])]
]);

const FORBIDDEN_MAIN_VIEWMODEL_PATTERNS = [
  /_backendControlApiService\.(?:Try\w+Async|SetAccessToken|LastFailure)\b/,
  /_botProcessService\.(?:Start|StopAsync|Detach|IsRunning)\b/,
  /_localConfigFallbackReader\.LoadAsync\b/,
  /_localBootstrapConfigStore\.SaveExtraValueAsync\b/,
  /_localStateSnapshotService\./,
  /_activityStateStore\.(?:Load|Save)\b/
];

function normalizePath(filePath) {
  return filePath.replace(/\\/g, '/').trim();
}

function matchesAnyPattern(filePath, patterns) {
  return patterns.some((pattern) => filePath === pattern || filePath.startsWith(pattern));
}

function walkFiles(rootDir) {
  const results = [];

  if (!fs.existsSync(rootDir)) {
    return results;
  }

  for (const entry of fs.readdirSync(rootDir, { withFileTypes: true })) {
    const nextPath = path.join(rootDir, entry.name);

    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === 'bin' || entry.name === 'obj' || entry.name === 'dist') {
        continue;
      }

      results.push(...walkFiles(nextPath));
      continue;
    }

    results.push(nextPath);
  }

  return results;
}

function readText(filePath) {
  return fs.readFileSync(filePath, 'utf8');
}

function findForbiddenDomainImports(cwd = process.cwd()) {
  const domainRoot = path.join(cwd, 'src', 'domain');
  const violations = [];
  const forbiddenImportPattern = /from\s+['"](?:\.\.\/)+(?:adapters|app)\//g;

  for (const filePath of walkFiles(domainRoot)) {
    if (!filePath.endsWith('.mjs')) {
      continue;
    }

    const source = readText(filePath);

    if (!forbiddenImportPattern.test(source)) {
      continue;
    }

    violations.push({
      id: 'domain-dependency-direction',
      filePath: normalizePath(path.relative(cwd, filePath)),
      message: 'Files under src/domain must not import src/adapters or src/app.'
    });
  }

  return violations;
}

function findMainViewModelForbiddenUsage(cwd = process.cwd()) {
  const filePath = path.join(cwd, 'desktop', 'QQAIBot.Desktop', 'ViewModels', 'MainViewModel.cs');

  if (!fs.existsSync(filePath)) {
    return [];
  }

  const source = readText(filePath);
  const hits = FORBIDDEN_MAIN_VIEWMODEL_PATTERNS.filter((pattern) => pattern.test(source));

  if (hits.length === 0) {
    return [];
  }

  return [
    {
      id: 'main-viewmodel-low-level-control',
      filePath: normalizePath(path.relative(cwd, filePath)),
      message:
        'MainViewModel must not directly absorb backend control, snapshot persistence, or local config persistence flows.'
    }
  ];
}

function findPolicyImportViolations(cwd = process.cwd()) {
  const srcRoot = path.join(cwd, 'src');
  const violations = [];
  const sourceFiles = walkFiles(srcRoot).filter((filePath) => filePath.endsWith('.mjs'));

  for (const [targetFileName, allowedImporters] of ALLOWED_POLICY_IMPORTERS.entries()) {
    for (const filePath of sourceFiles) {
      const relativePath = normalizePath(path.relative(cwd, filePath));
      const source = readText(filePath);

      if (!source.includes(targetFileName)) {
        continue;
      }

      if (allowedImporters.has(relativePath)) {
        continue;
      }

      violations.push({
        id: 'policy-entry-boundary',
        filePath: relativePath,
        message: `${targetFileName} may only be imported by its designated composition entrypoint.`
      });
    }
  }

  return violations;
}

export function evaluateArchitectureBoundaries({ cwd = process.cwd() } = {}) {
  return {
    failures: [
      ...findForbiddenDomainImports(cwd),
      ...findMainViewModelForbiddenUsage(cwd),
      ...findPolicyImportViolations(cwd)
    ]
  };
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

  const boundaryResult = evaluateArchitectureBoundaries();
  const shouldCheckDomainBoundaries = normalizedChangedFiles.some((filePath) => filePath.startsWith('src/domain/'));
  const shouldCheckMainViewModel = normalizedChangedFiles.includes(
    'desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs'
  );
  const shouldCheckPolicyEntries = normalizedChangedFiles.some((filePath) =>
    [
      'src/domain/message-analysis-policy.mjs',
      'src/domain/provider-fallback-policy.mjs',
      'src/app/supervisor-contract-normalizer.mjs',
      'src/adapters/llm/llm-router.mjs',
      'src/index.mjs',
      'src/domain/route-decision.mjs'
    ].includes(filePath)
  );

  for (const failure of boundaryResult.failures) {
    if (failure.id === 'domain-dependency-direction' && !shouldCheckDomainBoundaries) {
      continue;
    }

    if (failure.id === 'main-viewmodel-low-level-control' && !shouldCheckMainViewModel) {
      continue;
    }

    if (failure.id === 'policy-entry-boundary' && !shouldCheckPolicyEntries) {
      continue;
    }

    failures.push(failure);
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
