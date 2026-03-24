import { chmod, mkdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import type { PersistedState } from './types.ts';

export const DEFAULT_STATE_DIR = path.join(os.homedir(), '.local', 'share', 'im-acp-gateway', 'wechat-ilink-verifier');
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

    if (typeof parsed.baseUrl !== 'string' || typeof parsed.botToken !== 'string') {
      throw new Error('State file is missing required baseUrl or botToken fields.');
    }

    const state: PersistedState = {
      version: 1,
      baseUrl: parsed.baseUrl,
      botToken: parsed.botToken,
    };

    if (typeof parsed.cursor === 'string') {
      state.cursor = parsed.cursor;
    }

    if (typeof parsed.loginConfirmedAt === 'string') {
      state.loginConfirmedAt = parsed.loginConfirmedAt;
    }

    if (typeof parsed.lastQrCodeId === 'string') {
      state.lastQrCodeId = parsed.lastQrCodeId;
    }

    if (typeof parsed.lastQrStatus === 'string') {
      state.lastQrStatus = parsed.lastQrStatus;
    }

    if (typeof parsed.lastPollAt === 'string') {
      state.lastPollAt = parsed.lastPollAt;
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

  await writeFile(temporaryFilePath, `${payload}\n`, 'utf8');
  await rename(temporaryFilePath, stateFilePath);
  await chmod(stateFilePath, 0o600);
}

export async function clearState(stateDirectory: string): Promise<void> {
  const stateFilePath = getStateFilePath(stateDirectory);
  await rm(stateFilePath, { force: true });
}

export async function ensureStateDirectory(stateDirectory: string): Promise<void> {
  await mkdir(stateDirectory, { recursive: true });
}

export async function stateExists(stateDirectory: string): Promise<boolean> {
  try {
    await stat(getStateFilePath(stateDirectory));
    return true;
  } catch (error) {
    if (isFileNotFound(error)) {
      return false;
    }

    throw error;
  }
}

function isFileNotFound(error: unknown): error is Error & { code: string } {
  return error instanceof Error && 'code' in error && error.code === 'ENOENT';
}
