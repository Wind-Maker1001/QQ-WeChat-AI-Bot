# Current Architecture

This document is the current-state calibration for the codebase in `D:\QQ AI Bot`.

Do not treat the project as "just a QQ bot" and do not infer the architecture from historical README text alone.
Use this document together with the code and tests.

## System Identity

The project is now a single-machine, multi-process AI runtime with a local operator console.

Primary runtime/control-plane parts:

- Node supervisor
- QQ runtime worker
- WeChat runtime worker
- local control API
- WPF desktop control plane
- config persistence and hot reload
- conversation/session persistence
- LLM routing and execution policy
- backend system tests and desktop regression tests

## Runtime Topology

High-level runtime shape:

```text
Desktop Shell (WPF tray app)
        |
        v
Control API / Supervisor
        |
        +-- QQ runtime worker
        |
        +-- WeChat runtime worker
```

Important entry points:

- `src/index.mjs`
- `src/app/runtime-worker.mjs`
- `src/app/wechat-runtime-worker.mjs`
- `src/app/control-api.mjs`

Supervisor/runtime coordination is already explicit and should stay explicit.

Key modules:

- `src/app/supervisor-runtime-controller.mjs`
- `src/app/supervisor-worker-slot.mjs`
- `src/app/process-runtime-message.mjs`
- `src/app/runtime-connection-manager.mjs`
- `src/app/runtime-config-reloader.mjs`
- `src/app/supervisor-runtime-state.mjs`

## Stable Boundaries

### Message Boundary

Application logic should not drift back toward channel-specific semantics.

Key files:

- `src/domain/channel-port.mjs`
- `src/domain/channel-message.mjs`
- `src/application/message-orchestrator.mjs`

### Runtime Public Layer

The runtime coordination layer is already decomposed into shared modules and should not be re-flattened.

Key files:

- `src/app/runtime-connection-manager.mjs`
- `src/app/runtime-config-reloader.mjs`
- `src/app/process-runtime-message.mjs`
- `src/app/supervisor-worker-slot.mjs`
- `src/app/supervisor-runtime-controller.mjs`
- `src/app/supervisor-runtime-state.mjs`

### Config Layer

Desktop is control-API first. Config file access is still present, but it is a fallback and persistence substrate, not the primary control path.

Key files:

- `src/adapters/config/env-file-store.mjs`
- `src/adapters/config/control-config-mapper.mjs`
- `src/adapters/config/control-config-file.mjs`
- `src/adapters/config/load-runtime-config.mjs`

### Desktop Control Plane

Desktop should behave like a control-plane shell over backend state, not like a direct `.env` editor with attached business logic.

Key files:

- `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs`
- `desktop/QQAIBot.Desktop/Services/BackendControlApiService.cs`
- `desktop/QQAIBot.Desktop/Services/LocalEnvConfigFallbackReader.cs`

## Strategy Chain

The current request/strategy chain is:

```text
incoming message
  -> route decision
  -> route request policy
  -> execution plan
  -> turn strategy assembly
  -> provider execution
  -> telemetry / session update / local projection
```

Key ownership:

- route decision:
  - `src/domain/route-decision.mjs`
- request policy:
  - `src/domain/llm-request-policy.mjs`
- execution plan:
  - `src/application/llm-execution-plan.mjs`
- turn spec / telemetry:
  - `src/application/message-turn-spec.mjs`
- turn strategy assembly:
  - `src/application/message-turn-strategy.mjs`
- provider/router boundary:
  - `src/adapters/llm/llm-router.mjs`
  - `src/adapters/llm/openai-provider.mjs`

`message-orchestrator.mjs` should remain an orchestrator, not re-grow into the strategy center.

## Strategy Execution Modes

Current execution modes:

- direct
- deliberation
- local capability reply

The strategy layer now carries explicit execution projection metadata:

- execution kind
- execution stage path
- completed stages
- failed stage
- degraded flag
- recovery tags

Primary files:

- `src/domain/execution-projection.mjs`
- `src/application/message-turn-strategy.mjs`
- `src/application/deliberation-executor.mjs`
- `src/application/message-turn-spec.mjs`

## Desktop Control-Plane Layers

The desktop side now has a clearer layering model.

### Projection Formatters

- `desktop/QQAIBot.Desktop/Services/BackendExecutionProjectionFormatter.cs`
- `desktop/QQAIBot.Desktop/Services/BackendLlmProjectionFormatter.cs`
- `desktop/QQAIBot.Desktop/Services/BackendActivityProjectionFormatter.cs`

### Activity Projection and View State

- `desktop/QQAIBot.Desktop/Services/BackendRecentActivityProjector.cs`
- `desktop/QQAIBot.Desktop/Services/BackendRecentActivityViewStateHelper.cs`
- `desktop/QQAIBot.Desktop/Services/BackendRecentActivityCoordinator.cs`

### Runtime Snapshot Projection

- `desktop/QQAIBot.Desktop/Models/BackendRuntimeSnapshotViewState.cs`
- `desktop/QQAIBot.Desktop/Services/BackendRuntimeSnapshotCoordinator.cs`
- `desktop/QQAIBot.Desktop/Services/BackendRuntimeSnapshotViewHelper.cs`

### Snapshot Presentation

- `desktop/QQAIBot.Desktop/Services/LocalStateSnapshotPresentationBuilder.cs`

Snapshot archive IO and diff generation should stay in `LocalStateSnapshotService`.
User-facing restore impact, safety, and post-restore action guidance should be derived in the presentation builder, not rebuilt inside `MainViewModel`.

### Control-Plane Coordination

- `desktop/QQAIBot.Desktop/Models/BackendControlApiPollState.cs`
- `desktop/QQAIBot.Desktop/Services/BackendControlApiStatusPollCoordinator.cs`
- `desktop/QQAIBot.Desktop/Services/BackendControlApiRecoveryCoordinator.cs`
- `desktop/QQAIBot.Desktop/Services/BackendControlPlaneCoordinator.cs`
- `desktop/QQAIBot.Desktop/Services/BackendRuntimeControlCoordinator.cs`
- `desktop/QQAIBot.Desktop/Services/BackendControlPlaneFacade.cs`
- `desktop/QQAIBot.Desktop/Services/DesktopControlPlaneFeedback.cs`

### Health Guidance

- `desktop/QQAIBot.Desktop/Services/DesktopHealthGuidanceBuilder.cs`

Health checks and overall readiness can still compose into one report, but latest-issue explanation and action-priority rules should stay in the guidance builder instead of drifting back into the report builder or `MainViewModel`.

### Health Checklist And Status

- `desktop/QQAIBot.Desktop/Services/DesktopHealthChecklistBuilder.cs`
- `desktop/QQAIBot.Desktop/Services/DesktopHealthStatusBuilder.cs`

Checklist item construction and checklist-summary text should stay in the checklist builder.
Overall readiness state, primary action, ready-now text, and runtime explanation should stay in the status builder.

### Guide Presentation

- `desktop/QQAIBot.Desktop/Services/DesktopGuideFlowBuilder.cs`

Homepage first-run guidance, daily-use guidance, and overall-readiness action selection should be derived in the guide builder instead of being rebuilt inline inside `MainViewModel`.

This is the current intended shape:

```text
MainViewModel
  -> control-plane facade / coordinators
  -> projection helpers
  -> UI shell state + commands
```

## Current Complexity Centers

The main complexity centers are now:

1. Runtime coordination
2. Strategy execution and execution projection
3. Desktop control-plane orchestration and recovery

Channel adapter complexity is no longer the main center.

## Gravity Wells To Resist

These files or layers are especially likely to become new system centers if future changes are not disciplined.

### `src/application/message-orchestrator.mjs`

This file should stay focused on orchestration:

- gather already-normalized inputs
- invoke strategy assembly / execution
- persist session state
- emit replies / telemetry

It should not re-absorb:

- route policy heuristics
- execution-mode branching rules
- provider-specific capability logic
- channel-specific semantics

### `src/adapters/llm/llm-router.mjs`

This layer should stay at the router/provider boundary.

It can own:

- route selection handoff
- provider construction
- request description at the route/provider edge

It should not become:

- the place where conversation/session policy lives
- the place where desktop-facing projection logic is built
- the place where orchestration mode decisions accumulate

### `desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs`

The ViewModel is now much thinner than before, but it is still the easiest place for new behavior to drift back into.

It should stay focused on:

- UI shell state
- commands
- applying results from facades/coordinators/helpers

It should resist taking back:

- control-plane state machines
- projection string assembly
- recent-activity projection rules
- backend recovery orchestration

### README

README should remain an entrypoint, not a second architecture source of truth.

Detailed behavior and architecture should continue to live in:

- `docs/current-architecture.md`
- `docs/operations.md`
- `docs/testing.md`
- `docs/review-checklist.md`
- the tests themselves

## Test Sources of Truth

When behavior is unclear, prefer the tests over historical prose.

Key backend tests:

- `tests/llm-request-policy.test.mjs`
- `tests/llm-execution-plan.test.mjs`
- `tests/message-turn-spec.test.mjs`
- `tests/message-orchestrator-local-reply.test.mjs`
- `tests/llm-router.test.mjs`
- `tests/openai-provider-config.test.mjs`
- `tests/supervisor.e2e.test.mjs`

Key desktop regression harness:

- `desktop/QQAIBot.Desktop.Tests/Program.cs`

## Rules For Future Changes

When changing the backend strategy path:

- do not move channel-specific logic back into the application layer
- do not let `message-orchestrator.mjs` absorb strategy policy again
- preserve explicit execution projection metadata
- prefer extending `src/domain/execution-projection.mjs` over scattering new execution tags
- if behavior changes the request/telemetry contract, update the relevant node tests and supervisor e2e coverage together

When changing the desktop shell:

- prefer adding logic to the existing facade/coordinator/helper layers
- avoid putting new control-plane state machines back into `MainViewModel`
- prefer projection helpers over inline string assembly in the ViewModel
- keep snapshot restore explanation and action-priority rules in `LocalStateSnapshotPresentationBuilder`
- keep health latest-issue and next-action prioritization in `DesktopHealthGuidanceBuilder`
- keep homepage first-run / daily-use guide synthesis in `DesktopGuideFlowBuilder`
- keep checklist-item construction in `DesktopHealthChecklistBuilder`
- keep overall readiness / ready-now / runtime explanation in `DesktopHealthStatusBuilder`
- prefer extending existing desktop coordinators before adding one-off private methods to the ViewModel
- if a new helper is introduced, add a direct regression test for that helper instead of relying only on smoke coverage

When changing control-plane behavior:

- update desktop regression tests together with the behavior change
- treat the regression harness as part of the architecture contract
- keep `MainViewModel` as the composition shell; put branching recovery/start-stop/load-save logic elsewhere
- preserve the current separation between:
  - poll-state evaluation
  - recovery execution
  - runtime control execution
  - projection / feedback application

When changing documentation:

- update `docs/current-architecture.md` if ownership or complexity centers move
- update `docs/operations.md` if operational commands or install/release flows change
- update `docs/testing.md` if test entrypoints or minimum validation guidance change
- update `docs/review-checklist.md` if review guidance or gravity-well warnings change
- do not let README regain detailed architecture prose that can drift independently
