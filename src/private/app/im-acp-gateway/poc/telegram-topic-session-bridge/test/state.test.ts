import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

import { readState, writeState } from '../src/state.ts';
import type { PersistedState } from '../src/types.ts';

test('writeState and readState round-trip topic bridge state', async () => {
  const temporaryDirectory = await mkdtemp(
    path.join(os.tmpdir(), 'telegram-topic-session-bridge-state-'),
  );

  try {
    const state: PersistedState = {
      version: 1,
      apiBaseUrl: 'https://api.telegram.org',
      botToken: '123456:secret',
      controlChatId: '-100123456',
      configuredAt: '2026-03-28T00:00:00.000Z',
      lastPollAt: '2026-03-28T00:01:00.000Z',
      lastUpdateId: 42,
      sessions: {
        'session-1': {
          gatewaySessionId: 'session-1',
          acpSessionId: 'acp-1',
          chatId: '-100123456',
          topicThreadId: 77,
          topicName: 'session-1 repo summary',
          workingDirectory: '/workspace/repo',
          status: 'connected',
          permissionMode: 'allow_all',
          createdAt: '2026-03-28T00:00:00.000Z',
          updatedAt: '2026-03-28T00:01:00.000Z',
          lastPromptAt: '2026-03-28T00:00:30.000Z',
          lastStopReason: 'end_turn',
          latestApprovalId: 'approval-1',
        },
      },
      approvals: {
        'approval-1': {
          approvalId: 'approval-1',
          gatewaySessionId: 'session-1',
          acpSessionId: 'acp-1',
          topicThreadId: 77,
          status: 'pending',
          title: 'Run shell command',
          description: 'pnpm test',
          toolCallId: 'tool-1',
          contextLines: [
            'Session: session-1',
            'ACP session: acp-1',
            'Working directory: /workspace/repo',
          ],
          options: [
            {
              optionId: 'allow_once',
              kind: 'allow_once',
              name: 'Allow once',
            },
            {
              optionId: 'allow_always',
              kind: 'allow_always',
              name: 'Always allow',
            },
          ],
          promptMessageId: 999,
          createdAt: '2026-03-28T00:00:40.000Z',
          updatedAt: '2026-03-28T00:00:40.000Z',
        },
      },
    };

    await writeState(temporaryDirectory, state);
    const loaded = await readState(temporaryDirectory);

    assert.deepStrictEqual(loaded, state);
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
});
