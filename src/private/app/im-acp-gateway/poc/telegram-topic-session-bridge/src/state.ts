import { chmod, mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

import type {
  PersistedApproval,
  PersistedSession,
  PersistedState,
} from './types.ts';

export const DEFAULT_STATE_DIR = path.join(
  os.homedir(),
  '.local',
  'share',
  'im-acp-gateway',
  'telegram-topic-session-bridge',
);
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

export async function readState(
  stateDirectory: string,
): Promise<PersistedState | null> {
  const stateFilePath = getStateFilePath(stateDirectory);

  try {
    const raw = await readFile(stateFilePath, 'utf8');
    const parsed = JSON.parse(raw) as Partial<PersistedState>;

    if (parsed.version !== 1) {
      throw new Error(
        `Unsupported state file version: ${String(parsed.version)}`,
      );
    }

    if (
      typeof parsed.apiBaseUrl !== 'string' ||
      typeof parsed.botToken !== 'string' ||
      typeof parsed.controlChatId !== 'string'
    ) {
      throw new Error(
        'State file is missing required Telegram routing configuration fields.',
      );
    }

    return {
      version: 1,
      apiBaseUrl: parsed.apiBaseUrl,
      botToken: parsed.botToken,
      controlChatId: parsed.controlChatId,
      sessions: readSessions(parsed.sessions),
      approvals: readApprovals(parsed.approvals),
      ...(typeof parsed.configuredAt === 'string'
        ? { configuredAt: parsed.configuredAt }
        : {}),
      ...(typeof parsed.lastPollAt === 'string'
        ? { lastPollAt: parsed.lastPollAt }
        : {}),
      ...(typeof parsed.lastUpdateId === 'number'
        ? { lastUpdateId: parsed.lastUpdateId }
        : {}),
    };
  } catch (error) {
    if (isFileNotFound(error)) {
      return null;
    }

    throw error;
  }
}

export async function writeState(
  stateDirectory: string,
  state: PersistedState,
): Promise<void> {
  await mkdir(stateDirectory, { recursive: true });

  const stateFilePath = getStateFilePath(stateDirectory);
  const temporaryFilePath = `${stateFilePath}.tmp`;
  const payload = JSON.stringify(state, null, 2);

  await writeFile(temporaryFilePath, `${payload}\n`, 'utf8');
  await rename(temporaryFilePath, stateFilePath);
  await chmod(stateFilePath, 0o600);
}

function readSessions(
  sessions: unknown,
): Record<string, PersistedSession> {
  if (!sessions || typeof sessions !== 'object') {
    return {};
  }

  const entries = Object.entries(sessions);
  return Object.fromEntries(
    entries.filter(([, value]) => isPersistedSession(value)),
  );
}

function readApprovals(
  approvals: unknown,
): Record<string, PersistedApproval> {
  if (!approvals || typeof approvals !== 'object') {
    return {};
  }

  const entries = Object.entries(approvals);
  return Object.fromEntries(
    entries.filter(([, value]) => isPersistedApproval(value)),
  );
}

function isPersistedSession(value: unknown): value is PersistedSession {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const session = value as Partial<PersistedSession>;
  return (
    typeof session.gatewaySessionId === 'string' &&
    typeof session.acpSessionId === 'string' &&
    typeof session.chatId === 'string' &&
    typeof session.topicThreadId === 'number' &&
    typeof session.topicName === 'string' &&
    typeof session.workingDirectory === 'string' &&
    typeof session.status === 'string' &&
    typeof session.createdAt === 'string' &&
    typeof session.updatedAt === 'string'
  );
}

function isPersistedApproval(value: unknown): value is PersistedApproval {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const approval = value as Partial<PersistedApproval>;
  return (
    typeof approval.approvalId === 'string' &&
    typeof approval.gatewaySessionId === 'string' &&
    typeof approval.acpSessionId === 'string' &&
    typeof approval.topicThreadId === 'number' &&
    typeof approval.status === 'string' &&
    typeof approval.createdAt === 'string' &&
    typeof approval.updatedAt === 'string'
  );
}

function isFileNotFound(error: unknown): error is Error & { code: string } {
  return error instanceof Error && 'code' in error && error.code === 'ENOENT';
}
