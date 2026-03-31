import test from 'node:test';
import assert from 'node:assert/strict';

import { evaluateArchitectureBoundaries } from '../scripts/check-change-requirements.mjs';

test('repository architecture boundaries hold for domain imports, policy entrypoints, and MainViewModel', () => {
  const result = evaluateArchitectureBoundaries();

  assert.deepEqual(result.failures, []);
});
