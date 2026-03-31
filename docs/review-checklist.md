# Review Checklist

Use this checklist when reviewing or preparing changes in `D:\QQ AI Bot`.

Read this together with [current-architecture.md](current-architecture.md).

## Architecture Checks

### Message Boundary

- Application logic still depends on channel-neutral message concepts, not channel-specific APIs.
- Channel-specific semantics have not leaked back into `src/application/`.

### Runtime Public Layer

- Worker lifecycle, reconnect, hot reload, and status aggregation still go through the shared runtime modules.
- Runtime coordination logic was not re-flattened into ad hoc worker-specific code.

### Strategy Path

- `src/application/message-orchestrator.mjs` is still acting as an orchestrator, not as the strategy center.
- Route policy, intent-analysis heuristics, fallback rules, and execution projection metadata remain in the dedicated strategy/policy modules.
- If execution tags or execution projection changed, `src/domain/execution-projection.mjs` was updated instead of scattering new string literals.
- `src/domain/message-analysis-policy.mjs` owns heuristics; `src/domain/route-decision.mjs` owns decision objects and summaries.
- `src/domain/provider-fallback-policy.mjs` owns fallback eligibility and degraded fallback wrapping.

### Desktop Control Plane

- New control-plane logic was added to facade/coordinator/helper layers before considering `MainViewModel`.
- `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs` did not absorb a new state machine, projection formatter, or recovery workflow.
- Desktop projection text was changed in helpers/formatters, not by inline string assembly in the ViewModel.
- Derived shell state still flows through `DesktopShellProjector.cs` rather than being rebuilt ad hoc inside `DesktopControlPlaneSession.cs` or `MainViewModel.cs`.

## Gravity-Well Checks

Watch these files closely:

- `src/application/message-orchestrator.mjs`
- `src/adapters/llm/llm-router.mjs`
- `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs`
- `desktop/QQAIBot.Desktop/Services/DesktopControlPlaneSession.cs`

For each touched gravity-well candidate, ask:

- Did this file become more central, or just stay a composition point?
- Could the new logic live in an existing helper/coordinator instead?
- If this file grew, did another file become more explicit and own the complexity?

## Contract Checks

- Backend contract changes are reflected in node tests.
- Desktop contract changes are reflected in desktop regression tests.
- If control API payloads changed, both backend and desktop consumers were updated together.

## Validation Checks

At minimum, reviewers should confirm that the author ran the right scope of validation:

- strategy/policy changes:
  - `node tests/message-analysis-policy.test.mjs`
  - `node tests/provider-fallback-policy.test.mjs`
  - relevant node tests
  - supervisor e2e if status or telemetry changed
- desktop control-plane changes:
  - `npm run test:desktop`
- cross-cutting contract changes:
  - `npm test`
- architecture boundary changes:
  - `node tests/architecture-boundaries.test.mjs`

Current automation note:

- CI also runs `npm run check:changes`
- that check is intentionally lightweight and only covers the highest-risk gravity-well / contract files
- passing it does not replace human architecture review

## Documentation Checks

- If ownership or boundaries changed, update [current-architecture.md](current-architecture.md).
- If operational commands or workflows changed, update [operations.md](operations.md).
- If validation guidance changed, update [testing.md](testing.md).
- Keep README as the entrypoint, not a second detailed architecture source.
