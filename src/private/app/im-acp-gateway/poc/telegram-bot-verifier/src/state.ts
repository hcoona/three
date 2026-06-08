import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import type { PersistedState } from './types.ts';

export const DEFAULT_STATE_DIR = path.join(os.homedir(), '.local', 'share', 'im-acp-gateway', 'telegram-bot-verifier');
const STATE_FILE_NAME = 'state.json';

export function resolveStateDirectory(input?: string): string {
  if (input && input.trim().length > 0) {
    return path.resolve(input);
  }

  return DEFAULT_STATE_DIR;
}

export function getStateFilePath(stateDirectory: string): string {
  return path.join(stateDirectory, STATE_FILE_NAME);
}

export async function readState(stateDirectory: string): Promise<PersistedState | null> {
  const stateFilePath = getStateFilePath(stateDirectory);

  try {
    const raw = await readFile(stateFilePath, 'utf8');
    const parsed = JSON.parse(raw) as Partial<PersistedState>;

    if (parsed.version !== 1) {
      throw new Error(`Unsupported state file version: ${String(parsed.version)}`);
    }

    if (typeof parsed.apiBaseUrl !== 'string' || typeof parsed.botToken !== 'string') {
      throw new Error('State file is missing required apiBaseUrl or botToken fields.');
    }

    const state: PersistedState = {
      version: 1,
      apiBaseUrl: parsed.apiBaseUrl,
      botToken: parsed.botToken,
    };

    if (typeof parsed.defaultChatId === 'string') {
      state.defaultChatId = parsed.defaultChatId;
    }

    if (typeof parsed.lastAcpSessionId === 'string') {
      state.lastAcpSessionId = parsed.lastAcpSessionId;
    }

    if (typeof parsed.lastAcpStopReason === 'string') {
      state.lastAcpStopReason = parsed.lastAcpStopReason;
    }

    if (typeof parsed.lastUpdateId === 'number') {
      state.lastUpdateId = parsed.lastUpdateId;
    }

    if (typeof parsed.configuredAt === 'string') {
      state.configuredAt = parsed.configuredAt;
    }

    if (typeof parsed.lastPollAt === 'string') {
      state.lastPollAt = parsed.lastPollAt;
    }

    if (typeof parsed.lastObservedChatId === 'string') {
      state.lastObservedChatId = parsed.lastObservedChatId;
    }

    if (typeof parsed.lastObservedMessageId === 'number') {
      state.lastObservedMessageId = parsed.lastObservedMessageId;
    }

    if (typeof parsed.lastObservedAt === 'string') {
      state.lastObservedAt = parsed.lastObservedAt;
    }

    if (typeof parsed.lastCallbackQueryId === 'string') {
      state.lastCallbackQueryId = parsed.lastCallbackQueryId;
    }

    if (typeof parsed.lastCallbackData === 'string') {
      state.lastCallbackData = parsed.lastCallbackData;
    }

    return state;
  } catch (error) {
    if (isFileNotFound(error)) {
      return null;
    }

    throw error;
  }
}

export async function writeState(stateDirectory: string, state: PersistedState): Promise<void> {
  await mkdir(stateDirectory, { recursive: true });

  const stateFilePath = getStateFilePath(stateDirectory);
  const temporaryFilePath = `${stateFilePath}.tmp`;
  const payload = JSON.stringify(state, null, 2);

  await writeFile(temporaryFilePath, `${payload}\n`, { encoding: 'utf8', mode: 0o600 });
  await rename(temporaryFilePath, stateFilePath);
}

function isFileNotFound(error: unknown): error is Error & { code: string } {
  return error instanceof Error && 'code' in error && error.code === 'ENOENT';
}
