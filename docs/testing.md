# Testing Guide

This guide explains where the current behavior contract lives and how to validate changes.

For architecture and ownership boundaries, see [current-architecture.md](current-architecture.md).
For setup and operational commands, see [operations.md](operations.md).

## Source of Truth

When README text, comments, and implementation seem inconsistent:

1. use backend tests in `tests/`
2. use desktop regression in `desktop/QQAIBot.Desktop.Tests/Program.cs`
3. then read the implementation

The test suite is part of the architecture contract, not just a safety net.

## Main Test Commands

Run everything:

```powershell
npm test
```

Run backend tests only:

```powershell
npm run test:node
```

Run desktop regression only:

```powershell
npm run test:desktop
```

Run desktop acceptance scripts:

```powershell
npm run desktop:acceptance
npm run desktop:acceptance:auto
```

## Backend Test Groups

### Strategy and Policy

Read these first when changing routing, capability upgrades, execution modes, or telemetry:

- `tests/llm-request-policy.test.mjs`
- `tests/route-decision.test.mjs`
- `tests/llm-execution-plan.test.mjs`
- `tests/message-turn-spec.test.mjs`
- `tests/message-turn-strategy.test.mjs`
- `tests/deliberation-executor.test.mjs`
- `tests/message-orchestrator-local-reply.test.mjs`

### Provider and Router

Read these when changing OpenAI provider behavior, request shape, or tool configuration:

- `tests/llm-router.test.mjs`
- `tests/openai-provider-config.test.mjs`

### Config and Control Contract

Read these when changing config mapping, env storage, or control API behavior:

- `tests/control-config-contract.test.mjs`
- `tests/control-config-mapper.test.mjs`
- `tests/control-api.integration.test.mjs`
- `tests/env-file-store.test.mjs`

### Runtime and Supervisor

Read these when changing worker lifecycle, reconnect/recovery, hot reload, or end-to-end status:

- `tests/supervisor.e2e.test.mjs`

## Desktop Regression

The desktop regression harness is:

- `desktop/QQAIBot.Desktop.Tests/Program.cs`

It is intentionally broad and acts as the current desktop behavior contract.

Important desktop coverage areas:

- control API client contract
- config load/save/recovery behavior
- desktop activity projection helpers
- recent activity projection and selection rules
- runtime snapshot coordination
- control-plane polling/recovery/start/stop coordinators
- tray/minimize/external activation behaviors

## Change Validation Guidance

### If You Change Strategy Logic

At minimum run:

```powershell
node --test tests/llm-request-policy.test.mjs tests/route-decision.test.mjs tests/llm-execution-plan.test.mjs tests/message-turn-spec.test.mjs tests/message-turn-strategy.test.mjs tests/deliberation-executor.test.mjs
```

If telemetry or runtime status is affected, also run:

```powershell
node --test tests/supervisor.e2e.test.mjs
```

### If You Change Desktop Control Plane

At minimum run:

```powershell
npm run test:desktop
```

If backend contract or runtime status shape is also touched, run full:

```powershell
npm test
```

### If You Change Config or Control API

At minimum run:

```powershell
node --test tests/control-config-contract.test.mjs tests/control-config-mapper.test.mjs tests/control-api.integration.test.mjs tests/env-file-store.test.mjs
```

And if desktop consumes the changed contract:

```powershell
npm run test:desktop
```

## Practical Rule

If a change alters:

- route selection
- execution projection
- control API payloads
- desktop projection text or selection behavior
- supervisor recovery behavior

do not stop at "related unit tests passed".
Run the broader suite that exercises the public contract around that change.
