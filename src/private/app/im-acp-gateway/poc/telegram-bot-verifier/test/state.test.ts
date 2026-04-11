import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { readState, writeState } from '../src/state.ts';
import type { PersistedState } from '../src/types.ts';

test('writeState and readState round-trip persisted bot state', async () => {
  const temporaryDirectory = await mkdtemp(path.join(os.tmpdir(), 'telegram-bot-verifier-state-'));

  try {
    const state: PersistedState = {
      version: 1,
      apiBaseUrl: 'https://api.telegram.org',
      botToken: '123456:secret',
      defaultChatId: '123456789',
      lastAcpSessionId: 'session-123',
      lastAcpStopReason: 'end_turn',
      lastUpdateId: 42,
      configuredAt: '2026-03-24T00:00:00.000Z',
      lastPollAt: '2026-03-24T00:01:00.000Z',
      lastObservedChatId: '123456789',
      lastObservedMessageId: 7,
      lastObservedAt: '2026-03-24T00:02:00.000Z',
      lastCallbackQueryId: 'callback-1',
      lastCallbackData: 'demo:approve:nonce',
    };

    await writeState(temporaryDirectory, state);
    const loaded = await readState(temporaryDirectory);

    assert.deepStrictEqual(loaded, state);
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
});
