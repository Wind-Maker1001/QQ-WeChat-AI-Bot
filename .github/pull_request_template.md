## Summary

-

## Why

-

## Architecture Checklist

- [ ] This change does not push channel-specific semantics back into the application layer.
- [ ] This change does not re-flatten shared runtime coordination into worker-specific logic.
- [ ] If strategy behavior changed, `message-orchestrator.mjs` is still only orchestration, not the new policy center.
- [ ] If execution projection changed, the change is centralized in `src/domain/execution-projection.mjs` or existing strategy modules.
- [ ] If desktop control-plane behavior changed, the logic was added to facade/coordinator/helper layers instead of re-growing `MainViewModel.cs`.

## Validation

- [ ] I ran the relevant targeted tests.
- [ ] I ran `npm run test:desktop` if desktop behavior or desktop-facing contract changed.
- [ ] I ran `npm test` if backend/desktop contract or e2e-visible behavior changed.

## Docs

- [ ] I updated docs if architecture, operations, or testing guidance changed.
- [ ] I checked [docs/review-checklist.md](docs/review-checklist.md) for architecture review guidance.

## Risks

- 
