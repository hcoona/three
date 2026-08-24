import assert from 'node:assert/strict';
import test from 'node:test';

import { smokeMessage } from '../src/index.js';

test('smokeMessage returns the stable package identity', () => {
  assert.equal(smokeMessage(), 'hcoona-release-smoke-npm');
});
