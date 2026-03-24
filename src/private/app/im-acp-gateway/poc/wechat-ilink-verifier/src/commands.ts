import path from 'node:path';
import process from 'node:process';

import { extractTextContent, summarizeInboundMessage } from './messages.ts';
import { ILinkClient } from './ilink-client.ts';
import { saveQrCodeArtifact } from './qr.ts';
import { clearState, ensureStateDirectory, readState, resolveStateDirectory, writeState } from './state.ts';
import type { LoginOptions, MonitorOptions, PersistedState, SendTextRequest } from './types.ts';

const DEFAULT_BASE_URL = 'https://ilinkai.weixin.qq.com';

export async function runLoginCommand(options: {
  baseUrl?: string;
  force: boolean;
  stateDirectory?: string;
  login: LoginOptions;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);

  if (!options.force) {
    const existingState = await readState(stateDirectory);

    if (existingState) {
      warn(
        `Existing login state found at ${path.join(stateDirectory, 'state.json')}. Use --force to replace it.`,
      );
      return;
    }
  } else {
    await clearState(stateDirectory);
  }

  await ensureStateDirectory(stateDirectory);

  const client = new ILinkClient({
    baseUrl: options.baseUrl ?? DEFAULT_BASE_URL,
  });

  warn(`Requesting QR code from ${client.baseUrl} ...`);
  const qrCodeResponse = await client.getBotQrCode(options.login.botType);
  const qrCodeId = qrCodeResponse.qrcode;

  if (!qrCodeId) {
    throw new Error(`Login QR code response did not include a qrcode identifier: ${JSON.stringify(qrCodeResponse)}`);
  }

  const qrArtifact = await saveQrCodeArtifact(stateDirectory, qrCodeId, qrCodeResponse.qrcode_img_content);

  warn(`QR code id: ${qrCodeId}`);

  if (qrArtifact.filePath) {
    warn(`QR code artifact saved to: ${qrArtifact.filePath}`);
  }

  if (qrCodeResponse.url) {
    warn(`QR code URL: ${qrCodeResponse.url}`);
  }

  warn('Please scan the QR code and confirm the login on your phone.');

  const deadline = Date.now() + options.login.timeoutSeconds * 1_000;
  let lastStatus: string | undefined;

  while (Date.now() < deadline) {
    const statusResponse = await client.getQrCodeStatus(qrCodeId);
    const currentStatus = statusResponse.status ?? 'unknown';

    if (currentStatus !== lastStatus) {
      warn(`QR status: ${currentStatus}`);
      lastStatus = currentStatus;
    }

    if (currentStatus === 'confirmed') {
      if (!statusResponse.bot_token || !statusResponse.baseurl) {
        throw new Error(
          `Confirmed QR status did not provide bot_token/baseurl: ${JSON.stringify(statusResponse)}`,
        );
      }

      const state: PersistedState = {
        version: 1,
        baseUrl: statusResponse.baseurl,
        botToken: statusResponse.bot_token,
        loginConfirmedAt: new Date().toISOString(),
        lastQrCodeId: qrCodeId,
        lastQrStatus: currentStatus,
      };

      await writeState(stateDirectory, state);

      warn(`Login confirmed. State persisted to ${path.join(stateDirectory, 'state.json')}.`);
      return;
    }

    if (currentStatus === 'expired' || currentStatus === 'rejected') {
      throw new Error(`QR code login ended with status "${currentStatus}".`);
    }

    await sleep(options.login.pollIntervalMs);
  }

  throw new Error(`Timed out waiting for QR code confirmation after ${options.login.timeoutSeconds} seconds.`);
}

export async function runMonitorCommand(options: {
  stateDirectory?: string;
  monitor: MonitorOptions;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new ILinkClient({
    baseUrl: state.baseUrl,
    botToken: state.botToken,
  });

  warn(`Monitoring inbound messages with base URL ${state.baseUrl}.`);
  warn(`State directory: ${stateDirectory}`);

  let cursor = state.cursor ?? '';

  while (true) {
    const response = await client.getUpdates(cursor);

    if (response.ret !== undefined && response.ret !== 0) {
      throw new Error(`getupdates failed: ${JSON.stringify(response)}`);
    }

    const newCursor = response.get_updates_buf ?? cursor;
    const messages = response.msgs ?? [];

    if (newCursor !== cursor || messages.length > 0) {
      cursor = newCursor;
      const updatedState: PersistedState = {
        ...state,
        lastPollAt: new Date().toISOString(),
      };

      if (cursor.length > 0) {
        updatedState.cursor = cursor;
      }

      await writeState(stateDirectory, updatedState);
    }

    for (const message of messages) {
      const summary = summarizeInboundMessage(message);
      warn('Inbound message summary:');
      process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
      process.stdout.write(`${JSON.stringify(message, null, 2)}\n`);

      if (!options.monitor.sendReplies) {
        continue;
      }

      if (message.message_type !== 1) {
        continue;
      }

      const textParts = extractTextContent(message);
      if (textParts.length === 0) {
        warn('Skipping auto-reply because no text items were found.');
        continue;
      }

      if (!message.from_user_id || !message.context_token) {
        warn('Skipping auto-reply because from_user_id or context_token is missing.');
        continue;
      }

      const replyText = `${options.monitor.replyPrefix}${textParts.join('\n')}`;
      const sendRequest: SendTextRequest = {
        toUserId: message.from_user_id,
        contextToken: message.context_token,
        text: replyText,
      };
      const sendResponse = await client.sendTextMessage(sendRequest);

      warn(`Sent reply to ${message.from_user_id}: ${replyText}`);
      process.stdout.write(`${JSON.stringify(sendResponse, null, 2)}\n`);
    }

    if (options.monitor.once) {
      return;
    }
  }
}

export async function runSendCommand(options: {
  stateDirectory?: string;
  request: SendTextRequest;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new ILinkClient({
    baseUrl: state.baseUrl,
    botToken: state.botToken,
  });

  const response = await client.sendTextMessage(options.request);
  warn(`Sent message to ${options.request.toUserId}.`);
  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
}

export async function runShowStateCommand(options: { stateDirectory?: string }): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readState(stateDirectory);

  if (!state) {
    warn(`No state file found at ${path.join(stateDirectory, 'state.json')}.`);
    return;
  }

  process.stdout.write(`${JSON.stringify(sanitizeStateForDisplay(state), null, 2)}\n`);
}

function sanitizeStateForDisplay(state: PersistedState): Record<string, string | undefined> {
  return {
    version: String(state.version),
    baseUrl: state.baseUrl,
    botTokenPreview: `${state.botToken.slice(0, 6)}...${state.botToken.slice(-4)}`,
    cursor: state.cursor,
    loginConfirmedAt: state.loginConfirmedAt,
    lastQrCodeId: state.lastQrCodeId,
    lastQrStatus: state.lastQrStatus,
    lastPollAt: state.lastPollAt,
  };
}

async function readRequiredState(stateDirectory: string): Promise<PersistedState> {
  const state = await readState(stateDirectory);

  if (!state) {
    throw new Error(
      `No login state found in ${stateDirectory}. Run the login command first.`,
    );
  }

  return state;
}

function sleep(milliseconds: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, milliseconds);
  });
}

function warn(message: string): void {
  console.warn(message);
}
