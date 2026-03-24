import crypto from 'node:crypto';
import path from 'node:path';
import process from 'node:process';

import { CopilotAcpClient } from './acp-client.ts';
import {
  createApprovalDemoMarkup,
  parseDemoCallbackData,
  summarizeUpdate,
} from './messages.ts';
import { readState, resolveStateDirectory, writeState } from './state.ts';
import { TelegramBotClient } from './telegram-client.ts';
import type {
  AcpAgentMessageChunkUpdate,
  AcpPermissionRequestParams,
  BridgeOptions,
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

export async function runBridgeCommand(options: {
  bridge: BridgeOptions;
  stateDirectory?: string | undefined;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  let state = await readRequiredState(stateDirectory);
  const telegramClient = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });

  const sessionByChat = new Map<string, string>();
  const sessionByBotMessage = new Map<string, string>();
  const activeTurnByChat = new Map<string, ActiveTurn>();

  const acpClient = await CopilotAcpClient.start({
    copilotPath: options.bridge.copilotPath,
    cwd: options.bridge.cwd,
    ...(options.bridge.model ? { model: options.bridge.model } : {}),
    onSessionUpdate(sessionId, update) {
      const activeTurn = findActiveTurnBySessionId(activeTurnByChat, sessionId);
      if (!activeTurn) {
        return;
      }

      if (isAcpAgentTextChunkUpdate(update)) {
        activeTurn.textChunks.push(update.content.text);
      }
    },
    async onPermissionRequest(params) {
      await notifyPermissionCancellation(
        telegramClient,
        activeTurnByChat,
        params,
      );

      return {
        outcome: {
          outcome: 'cancelled',
        },
      };
    },
  });

  warn(
    `Monitoring Telegram updates and routing messages to Copilot ACP in ${options.bridge.cwd}.`,
  );
  warn(`State directory: ${stateDirectory}`);

  let offset =
    state.lastUpdateId === undefined ? undefined : state.lastUpdateId + 1;

  try {
    while (true) {
      const updates = await telegramClient.getUpdates({
        offset,
        timeout: options.bridge.timeoutSeconds,
        allowed_updates: ['message', 'edited_message', 'callback_query'],
      });

      if (updates.length > 0) {
        const maximumUpdateId = Math.max(
          ...updates.map((update) => update.update_id),
        );
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
        warn('Bridge inbound update summary:');
        process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);

        state = await updateObservedState(stateDirectory, state, update);

        if (update.callback_query) {
          await handleBridgeCallbackQuery(
            telegramClient,
            acpClient,
            update.callback_query,
            activeTurnByChat,
          );
          continue;
        }

        const message = update.message;
        if (!message) {
          continue;
        }

        await handleBridgeMessage({
          activeTurnByChat,
          acpClient,
          bridge: options.bridge,
          getState() {
            return state;
          },
          message,
          sessionByBotMessage,
          sessionByChat,
          stateDirectory,
          telegramClient,
          updateState(nextState) {
            state = nextState;
          },
        });
      }
    }
  } finally {
    await acpClient.close();
  }
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
    lastAcpSessionId: state.lastAcpSessionId,
    lastAcpStopReason: state.lastAcpStopReason,
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

async function handleBridgeCallbackQuery(
  telegramClient: TelegramBotClient,
  acpClient: CopilotAcpClient,
  callbackQuery: TelegramCallbackQuery,
  activeTurnByChat: Map<string, ActiveTurn>,
): Promise<void> {
  const parsed = parseDemoCallbackData(callbackQuery.data);

  if (!callbackQuery.message || !parsed) {
    await handleCallbackQuery(telegramClient, callbackQuery);
    return;
  }

  if (parsed.action !== 'stop') {
    await handleCallbackQuery(telegramClient, callbackQuery);
    return;
  }

  const chatKey = String(callbackQuery.message.chat.id);
  const activeTurn = activeTurnByChat.get(chatKey);

  if (!activeTurn) {
    await telegramClient.answerCallbackQuery({
      callback_query_id: callbackQuery.id,
      text: 'No active Copilot turn to stop.',
    });
    return;
  }

  activeTurn.cancellationAcknowledged = true;
  await telegramClient.answerCallbackQuery({
    callback_query_id: callbackQuery.id,
    text: 'Stop requested.',
  });
  await acpClient.cancel(activeTurn.sessionId);
  await telegramClient.editMessageText({
    chat_id: callbackQuery.message.chat.id,
    message_id: callbackQuery.message.message_id,
    text: `Stop requested for Copilot turn (${parsed.nonce}).`,
  });
}

async function handleBridgeMessage(options: {
  activeTurnByChat: Map<string, ActiveTurn>;
  acpClient: CopilotAcpClient;
  bridge: BridgeOptions;
  getState: () => PersistedState;
  message: TelegramMessage;
  sessionByBotMessage: Map<string, string>;
  sessionByChat: Map<string, string>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const text = options.message.text?.trim();
  if (!text || options.message.from?.is_bot) {
    return;
  }

  const chatKey = String(options.message.chat.id);
  const activeTurn = options.activeTurnByChat.get(chatKey);

  if (text === '/new') {
    options.sessionByChat.delete(chatKey);
    await options.telegramClient.sendMessage({
      chat_id: options.message.chat.id,
      text: 'Okay. The next non-command message will start a new Copilot session.',
      reply_parameters: {
        message_id: options.message.message_id,
        allow_sending_without_reply: true,
      },
    });
    return;
  }

  if (text === '/stop') {
    if (!activeTurn) {
      await options.telegramClient.sendMessage({
        chat_id: options.message.chat.id,
        text: 'There is no active Copilot turn to stop.',
        reply_parameters: {
          message_id: options.message.message_id,
          allow_sending_without_reply: true,
        },
      });
      return;
    }

    activeTurn.cancellationAcknowledged = true;
    await options.acpClient.cancel(activeTurn.sessionId);
    await options.telegramClient.sendMessage({
      chat_id: options.message.chat.id,
      text: 'Stop requested for the active Copilot turn.',
      reply_parameters: {
        message_id: options.message.message_id,
        allow_sending_without_reply: true,
      },
    });
    return;
  }

  if (activeTurn) {
    await options.telegramClient.sendMessage({
      chat_id: options.message.chat.id,
      text: 'A Copilot turn is already running for this chat. Wait for it to finish or send /stop.',
      reply_parameters: {
        message_id: options.message.message_id,
        allow_sending_without_reply: true,
      },
    });
    return;
  }

  const replyToMessageId = options.message.reply_to_message?.message_id;
  let sessionId =
    resolveSessionIdFromReply(
      options.message.chat.id,
      replyToMessageId,
      options.sessionByBotMessage,
    ) ?? options.sessionByChat.get(chatKey);

  if (!sessionId) {
    const newSessionResult = await options.acpClient.newSession(options.bridge.cwd);
    sessionId = newSessionResult.sessionId;
    options.sessionByChat.set(chatKey, sessionId);
    await persistAcpState(options, {
      ...options.getState(),
      lastAcpSessionId: sessionId,
    });
  }

  const turn: ActiveTurn = {
    cancellationAcknowledged: false,
    chatId: String(options.message.chat.id),
    replyToMessageId: options.message.message_id,
    sessionId,
    textChunks: [],
  };
  options.activeTurnByChat.set(chatKey, turn);

  await options.telegramClient.sendChatAction({
    chat_id: options.message.chat.id,
    action: 'typing',
  });

  void runCopilotTurn(options, turn, text).finally(() => {
    options.activeTurnByChat.delete(chatKey);
  });
}

async function runCopilotTurn(
  options: {
    acpClient: CopilotAcpClient;
    getState: () => PersistedState;
    sessionByBotMessage: Map<string, string>;
    sessionByChat: Map<string, string>;
    stateDirectory: string;
    telegramClient: TelegramBotClient;
    updateState: (state: PersistedState) => void;
  },
  turn: ActiveTurn,
  promptText: string,
): Promise<void> {
  try {
    const promptResult = await options.acpClient.prompt(turn.sessionId, promptText);
    const normalizedText = normalizeCopilotText(turn.textChunks.join(''));

    await persistAcpState(options, {
      ...options.getState(),
      lastAcpSessionId: turn.sessionId,
      lastAcpStopReason: promptResult.stopReason,
    });

    if (promptResult.stopReason === 'cancelled') {
      if (!turn.cancellationAcknowledged) {
        await options.telegramClient.sendMessage({
          chat_id: turn.chatId,
          text: 'Copilot cancelled the current turn.',
          reply_parameters: {
            message_id: turn.replyToMessageId,
            allow_sending_without_reply: true,
          },
        });
      }
      return;
    }

    const replyText =
      normalizedText.length > 0
        ? normalizedText
        : `Copilot finished with stopReason=${promptResult.stopReason}.`;

    const chunks = splitTextForTelegram(replyText);

    for (const [index, chunk] of chunks.entries()) {
      const response = await options.telegramClient.sendMessage({
        chat_id: turn.chatId,
        text: chunk,
        reply_parameters:
          index === 0
            ? {
                message_id: turn.replyToMessageId,
                allow_sending_without_reply: true,
              }
            : undefined,
      });

      options.sessionByBotMessage.set(
        createMessageSessionKey(turn.chatId, response.message_id),
        turn.sessionId,
      );
      options.sessionByChat.set(turn.chatId, turn.sessionId);
    }
  } catch (error) {
    const message =
      error instanceof Error ? error.message : 'Unknown Copilot ACP error.';

    await options.telegramClient.sendMessage({
      chat_id: turn.chatId,
      text: `Copilot ACP error: ${message}`,
      reply_parameters: {
        message_id: turn.replyToMessageId,
        allow_sending_without_reply: true,
      },
    });
  }
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

async function persistAcpState(
  options: {
    stateDirectory: string;
    updateState: (state: PersistedState) => void;
  },
  nextState: PersistedState,
): Promise<void> {
  await writeState(options.stateDirectory, nextState);
  options.updateState(nextState);
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

function createMessageSessionKey(chatId: TelegramChatId, messageId: number): string {
  return `${String(chatId)}:${messageId}`;
}

function resolveSessionIdFromReply(
  chatId: TelegramChatId,
  replyToMessageId: number | undefined,
  sessionByBotMessage: Map<string, string>,
): string | null {
  if (replyToMessageId === undefined) {
    return null;
  }

  return (
    sessionByBotMessage.get(
      createMessageSessionKey(chatId, replyToMessageId),
    ) ?? null
  );
}

function findActiveTurnBySessionId(
  activeTurnByChat: Map<string, ActiveTurn>,
  sessionId: string,
): ActiveTurn | null {
  for (const activeTurn of activeTurnByChat.values()) {
    if (activeTurn.sessionId === sessionId) {
      return activeTurn;
    }
  }

  return null;
}

async function notifyPermissionCancellation(
  telegramClient: TelegramBotClient,
  activeTurnByChat: Map<string, ActiveTurn>,
  params: AcpPermissionRequestParams,
): Promise<void> {
  const activeTurn = findActiveTurnBySessionId(activeTurnByChat, params.sessionId);
  if (!activeTurn || activeTurn.permissionNoticeSent) {
    return;
  }

  activeTurn.permissionNoticeSent = true;

  await telegramClient.sendMessage({
    chat_id: activeTurn.chatId,
    text: 'Copilot requested a permission during this thin spike. The current bridge cancels permission requests automatically, so please retry with a simpler prompt or start a new session.',
    reply_parameters: {
      message_id: activeTurn.replyToMessageId,
      allow_sending_without_reply: true,
    },
  });
}

function isAcpAgentTextChunkUpdate(
  update: unknown,
): update is AcpAgentMessageChunkUpdate {
  if (!update || typeof update !== 'object') {
    return false;
  }

  const candidate = update as Partial<AcpAgentMessageChunkUpdate>;
  return (
    candidate.sessionUpdate === 'agent_message_chunk' &&
    candidate.content?.type === 'text' &&
    typeof candidate.content.text === 'string'
  );
}

function normalizeCopilotText(text: string): string {
  return text.replace(/\r\n/g, '\n').trim();
}

function splitTextForTelegram(text: string): string[] {
  const maximumLength = 3_500;
  if (text.length <= maximumLength) {
    return [text];
  }

  const chunks: string[] = [];
  let remaining = text;

  while (remaining.length > maximumLength) {
    const splitIndex = Math.max(
      remaining.lastIndexOf('\n\n', maximumLength),
      remaining.lastIndexOf('\n', maximumLength),
      remaining.lastIndexOf(' ', maximumLength),
    );

    const effectiveIndex =
      splitIndex > Math.floor(maximumLength / 2) ? splitIndex : maximumLength;
    chunks.push(remaining.slice(0, effectiveIndex).trim());
    remaining = remaining.slice(effectiveIndex).trim();
  }

  if (remaining.length > 0) {
    chunks.push(remaining);
  }

  return chunks;
}

interface ActiveTurn {
  cancellationAcknowledged: boolean;
  chatId: string;
  permissionNoticeSent?: boolean;
  replyToMessageId: number;
  sessionId: string;
  textChunks: string[];
}
