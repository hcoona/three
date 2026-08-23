import assert from 'node:assert/strict';
import test from 'node:test';

import { getBrowserExtensionVersion } from './version-utils.mjs';

test('browser manifest version preserves NBGV version height', () => {
  const first = getBrowserExtensionVersion({
    simpleVersion: '1.2.0',
    versionHeight: 149,
  });
  const second = getBrowserExtensionVersion({
    simpleVersion: '1.2.0',
    versionHeight: 150,
  });

  assert.equal(first, '1.2.0.149');
  assert.equal(second, '1.2.0.150');
  assert.notEqual(first, second);
});
