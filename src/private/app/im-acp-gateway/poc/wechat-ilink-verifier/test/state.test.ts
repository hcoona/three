import assert from 'node:assert/strict';
import { mkdtemp } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { clearState, getStateFilePath, readState, resolveStateDirectory, writeState } from '../src/state.ts';
import type { PersistedState } from '../src/types.ts';

test('resolveStateDirectory prefers explicit input', () => {
  const resolved = resolveStateDirectory('./tmp/example');
  assert.equal(resolved, path.resolve('./tmp/example'));
});

test('writeState and readState round-trip persisted data', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'wechat-ilink-verifier-'));
  const state: PersistedState = {
    version: 1,
    baseUrl: 'https://example.invalid',
    botToken: 'secret-token',
    cursor: 'cursor-1',
    loginConfirmedAt: '2026-03-24T00:00:00.000Z',
    lastQrCodeId: 'qr-1',
    lastQrStatus: 'confirmed',
    lastPollAt: '2026-03-24T00:10:00.000Z',
  };

  await writeState(directory, state);

  const loaded = await readState(directory);

  assert.deepEqual(loaded, state);
});

test('clearState removes the persisted file', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'wechat-ilink-verifier-'));
  const state: PersistedState = {
    version: 1,
    baseUrl: 'https://example.invalid',
    botToken: 'secret-token',
  };

  await writeState(directory, state);
  await clearState(directory);

  const loaded = await readState(directory);
  assert.equal(loaded, null);
  assert.equal(getStateFilePath(directory).endsWith('state.json'), true);
});
