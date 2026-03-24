import crypto from 'node:crypto';
import path from 'node:path';
import process from 'node:process';

import {
  createApprovalDemoMarkup,
  parseDemoCallbackData,
  summarizeUpdate,
} from './messages.ts';
import { readState, resolveStateDirectory, writeState } from './state.ts';
import { TelegramBotClient } from './telegram-client.ts';
import type {
  MonitorOptions,
  PersistedState,
  TelegramCallbackQuery,
  TelegramChatId,
  TelegramMessage,
  TelegramUpdate,
} from './types.ts';

export async function runSetupCommand(options: {
  force: boolean;
  setup: {
    apiBaseUrl?: string | undefined;
    botToken: string;
    chatId?: string | undefined;
  };
  stateDirectory?: string | undefined;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const existingState = await readState(stateDirectory);

  if (existingState && !options.force) {
    warn(
      `Existing bot state found at ${path.join(stateDirectory, 'state.json')}. Use --force to replace it.`,
    );
    return;
  }

  const client = new TelegramBotClient({
    apiBaseUrl: options.setup.apiBaseUrl,
    botToken: options.setup.botToken,
  });
  const bot = await client.getMe();

  const state: PersistedState = {
    version: 1,
    apiBaseUrl: client.apiBaseUrl,
    botToken: options.setup.botToken,
    configuredAt: new Date().toISOString(),
  };

  if (options.setup.chatId) {
    state.defaultChatId = options.setup.chatId;
  }

  await writeState(stateDirectory, state);

  warn(`Saved Telegram bot state to ${path.join(stateDirectory, 'state.json')}.`);
  warn(
    `Validated bot: ${bot.first_name}${bot.username ? ` (@${bot.username})` : ''} [${bot.id}]`,
  );
}

export async function runMonitorCommand(options: {
  stateDirectory?: string | undefined;
  monitor: MonitorOptions;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  let state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });

  warn(`Monitoring Telegram updates from ${client.apiBaseUrl}.`);
  warn(`State directory: ${stateDirectory}`);

  let offset =
    state.lastUpdateId === undefined ? undefined : state.lastUpdateId + 1;

  while (true) {
    const updates = await client.getUpdates({
      offset,
      timeout: options.monitor.timeoutSeconds,
      allowed_updates: ['message', 'edited_message', 'callback_query'],
    });

    if (updates.length > 0) {
      const maximumUpdateId = Math.max(...updates.map((update) => update.update_id));
      offset = maximumUpdateId + 1;
      state = {
        ...state,
        lastUpdateId: maximumUpdateId,
        lastPollAt: new Date().toISOString(),
      };
      await writeState(stateDirectory, state);
    }

    for (const update of updates) {
      const summary = summarizeUpdate(update);
      warn('Inbound update summary:');
      process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
      process.stdout.write(`${JSON.stringify(update, null, 2)}\n`);

      state = await updateObservedState(stateDirectory, state, update);

      if (update.callback_query && options.monitor.answerCallbacks) {
        await handleCallbackQuery(client, update.callback_query);
        continue;
      }

      const message = update.message;
      if (!message || !options.monitor.sendReplies) {
        continue;
      }

      await handleInboundMessage(client, message, options.monitor);
    }

    if (options.monitor.once) {
      return;
    }
  }
}

export async function runSendCommand(options: {
  chatId?: string | undefined;
  replyToMessageId?: number | undefined;
  stateDirectory?: string | undefined;
  text: string;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });
  const chatId = resolveChatId(options.chatId, state);

  const response = await client.sendMessage({
    chat_id: chatId,
    text: options.text,
    reply_parameters:
      options.replyToMessageId === undefined
        ? undefined
        : {
            message_id: options.replyToMessageId,
            allow_sending_without_reply: true,
          },
  });

  warn(`Sent message to chat ${String(chatId)}.`);
  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
}

export async function runEditCommand(options: {
  chatId?: string | undefined;
  messageId: number;
  stateDirectory?: string | undefined;
  text: string;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });
  const chatId = resolveChatId(options.chatId, state);

  const response = await client.editMessageText({
    chat_id: chatId,
    message_id: options.messageId,
    text: options.text,
  });

  warn(
    `Edited message ${options.messageId} in chat ${String(chatId)}.`,
  );
  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
}

export async function runSendChatActionCommand(options: {
  action: string;
  chatId?: string | undefined;
  stateDirectory?: string | undefined;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });
  const chatId = resolveChatId(options.chatId, state);

  const response = await client.sendChatAction({
    chat_id: chatId,
    action: options.action,
  });

  warn(`Sent chat action "${options.action}" to chat ${String(chatId)}.`);
  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
}

export async function runApprovalDemoCommand(options: {
  chatId?: string | undefined;
  stateDirectory?: string | undefined;
  text: string;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });
  const chatId = resolveChatId(options.chatId, state);
  const nonce = crypto.randomUUID().slice(0, 8);

  const response = await client.sendMessage({
    chat_id: chatId,
    text: `${options.text}\n\nNonce: ${nonce}`,
    reply_markup: createApprovalDemoMarkup(nonce),
  });

  warn(`Sent approval demo to chat ${String(chatId)}.`);
  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
}

export async function runShowStateCommand(options: {
  stateDirectory?: string | undefined;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const state = await readState(stateDirectory);

  if (!state) {
    warn(`No state file found at ${path.join(stateDirectory, 'state.json')}.`);
    return;
  }

  process.stdout.write(`${JSON.stringify(sanitizeStateForDisplay(state), null, 2)}\n`);
}

function sanitizeStateForDisplay(
  state: PersistedState,
): Record<string, string | number | undefined> {
  return {
    version: String(state.version),
    apiBaseUrl: state.apiBaseUrl,
    botTokenPreview: `${state.botToken.slice(0, 8)}...${state.botToken.slice(-4)}`,
    defaultChatId: state.defaultChatId,
    lastUpdateId: state.lastUpdateId,
    configuredAt: state.configuredAt,
    lastPollAt: state.lastPollAt,
    lastObservedChatId: state.lastObservedChatId,
    lastObservedMessageId: state.lastObservedMessageId,
    lastObservedAt: state.lastObservedAt,
    lastCallbackQueryId: state.lastCallbackQueryId,
    lastCallbackData: state.lastCallbackData,
  };
}

async function handleInboundMessage(
  client: TelegramBotClient,
  message: TelegramMessage,
  options: MonitorOptions,
): Promise<void> {
  if (message.from?.is_bot) {
    return;
  }

  const text = message.text?.trim();
  if (!text) {
    return;
  }

  if (text === '/approval-demo') {
    await client.sendMessage({
      chat_id: message.chat.id,
      text: 'Telegram approval demo. Choose an action below.',
      reply_parameters: {
        message_id: message.message_id,
        allow_sending_without_reply: true,
      },
      reply_markup: createApprovalDemoMarkup(
        crypto.randomUUID().slice(0, 8),
      ),
    });
    return;
  }

  if (text === '/approve' || text === '/deny' || text === '/stop') {
    await client.sendMessage({
      chat_id: message.chat.id,
      text: `Received fallback command ${text}.`,
      reply_parameters: {
        message_id: message.message_id,
        allow_sending_without_reply: true,
      },
    });
    return;
  }

  await client.sendMessage({
    chat_id: message.chat.id,
    text: `${options.replyPrefix}${text}`,
    reply_parameters: {
      message_id: message.message_id,
      allow_sending_without_reply: true,
    },
  });
}

async function handleCallbackQuery(
  client: TelegramBotClient,
  callbackQuery: TelegramCallbackQuery,
): Promise<void> {
  const parsed = parseDemoCallbackData(callbackQuery.data);
  const feedback = parsed
    ? `Recorded ${parsed.action}.`
    : 'Callback received.';

  await client.answerCallbackQuery({
    callback_query_id: callbackQuery.id,
    text: feedback,
  });

  if (!callbackQuery.message || !parsed) {
    return;
  }

  await client.editMessageText({
    chat_id: callbackQuery.message.chat.id,
    message_id: callbackQuery.message.message_id,
    text: `Demo action recorded: ${parsed.action.toUpperCase()} (${parsed.nonce})`,
  });
}

async function updateObservedState(
  stateDirectory: string,
  state: PersistedState,
  update: TelegramUpdate,
): Promise<PersistedState> {
  const nextState: PersistedState = { ...state };
  let changed = false;

  const observedMessage = update.message ?? update.edited_message;
  if (observedMessage) {
    nextState.lastObservedChatId = String(observedMessage.chat.id);
    nextState.lastObservedMessageId = observedMessage.message_id;
    nextState.lastObservedAt = new Date().toISOString();
    changed = true;
  }

  if (update.callback_query) {
    nextState.lastCallbackQueryId = update.callback_query.id;
    if (update.callback_query.data) {
      nextState.lastCallbackData = update.callback_query.data;
    }
    if (update.callback_query.message) {
      nextState.lastObservedChatId = String(update.callback_query.message.chat.id);
      nextState.lastObservedMessageId = update.callback_query.message.message_id;
      nextState.lastObservedAt = new Date().toISOString();
    }
    changed = true;
  }

  if (!changed) {
    return state;
  }

  await writeState(stateDirectory, nextState);
  return nextState;
}

async function readRequiredState(stateDirectory: string): Promise<PersistedState> {
  const state = await readState(stateDirectory);

  if (!state) {
    throw new Error(
      `No bot state found in ${stateDirectory}. Run the setup command first.`,
    );
  }

  return state;
}

function resolveChatId(
  explicitChatId: string | undefined,
  state: PersistedState,
): TelegramChatId {
  if (explicitChatId && explicitChatId.trim().length > 0) {
    return explicitChatId.trim();
  }

  if (state.defaultChatId && state.defaultChatId.trim().length > 0) {
    return state.defaultChatId;
  }

  if (state.lastObservedChatId && state.lastObservedChatId.trim().length > 0) {
    return state.lastObservedChatId;
  }

  throw new Error(
    'No chat id available. Pass --chat-id explicitly or run monitor after sending a message to the bot.',
  );
}

function warn(message: string): void {
  console.warn(message);
}
