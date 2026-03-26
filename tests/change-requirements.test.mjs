import test from 'node:test';
import assert from 'node:assert/strict';

import { evaluateChangeRequirements } from '../scripts/check-change-requirements.mjs';

test('change requirements require strategy test coverage for strategy-center changes', () => {
  const result = evaluateChangeRequirements([
    'src/application/message-orchestrator.mjs'
  ]);

  assert.equal(result.failures.length, 1);
  assert.equal(result.failures[0].id, 'strategy-coverage');
});

test('change requirements accept strategy changes when related tests are updated', () => {
  const result = evaluateChangeRequirements([
    'src/application/message-turn-strategy.mjs',
    'tests/message-turn-strategy.test.mjs'
  ]);

  assert.equal(result.failures.length, 0);
});

test('change requirements require desktop regression updates for desktop control-plane changes', () => {
  const result = evaluateChangeRequirements([
    'desktop/QQAIBot.Desktop/ViewModels/MainViewModel.cs'
  ]);

  assert.equal(result.failures.length, 1);
  assert.equal(result.failures[0].id, 'desktop-control-coverage');
});

test('change requirements accept desktop control-plane changes when desktop regression is updated', () => {
  const result = evaluateChangeRequirements([
    'desktop/QQAIBot.Desktop/Services/BackendControlPlaneFacade.cs',
    'desktop/QQAIBot.Desktop.Tests/Program.cs'
  ]);

  assert.equal(result.failures.length, 0);
});

test('change requirements require contract coverage for control-api/config changes', () => {
  const result = evaluateChangeRequirements([
    'src/app/control-api.mjs'
  ]);

  assert.equal(result.failures.length, 1);
  assert.equal(result.failures[0].id, 'control-contract-coverage');
});
