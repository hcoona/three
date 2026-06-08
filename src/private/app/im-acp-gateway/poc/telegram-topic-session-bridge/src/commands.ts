import crypto from 'node:crypto';
import { access, stat } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

import { CopilotAcpClient } from './acp-client.ts';
import {
  createPermissionMarkup,
  parseGeneralCommand,
  parsePermissionCallbackData,
  parseSessionCommand,
  renderTelegramMarkdownV2,
  summarizeUpdate,
} from './messages.ts';
import { readState, resolveStateDirectory, writeState } from './state.ts';
import { TelegramBotClient } from './telegram-client.ts';
import type {
  AcpAgentMessageChunkUpdate,
  AcpPermissionOption,
  AcpPermissionRequestParams,
  AcpPermissionResponse,
  BridgeOptions,
  MonitorOptions,
  PermissionMode,
  PersistedApproval,
  PersistedApprovalOption,
  PersistedSession,
  PersistedState,
  SessionCommand,
  SessionStatus,
  SetupOptions,
  TelegramCallbackQuery,
  TelegramMessage,
} from './types.ts';

interface ActiveTurn {
  cancellationAcknowledged: boolean;
  gatewaySessionId: string;
  acpSessionId: string;
  chatId: string;
  topicThreadId: number;
  replyToMessageId: number;
  textChunks: string[];
}

interface PendingApprovalResolver {
  resolve: (result: AcpPermissionResponse) => void;
}

export async function runSetupCommand(options: {
  force: boolean;
  setup: SetupOptions;
  stateDirectory?: string;
}): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  const existingState = await readState(stateDirectory);

  if (existingState && !options.force) {
    warn(`Existing bot state found at ${path.join(stateDirectory, 'state.json')}. Use --force to replace it.`);
    return;
  }

  const client = new TelegramBotClient({
    botToken: options.setup.botToken,
    ...(options.setup.apiBaseUrl ? { apiBaseUrl: options.setup.apiBaseUrl } : {}),
  });
  const bot = await client.getMe();

  const state: PersistedState = {
    version: 1,
    apiBaseUrl: client.apiBaseUrl,
    botToken: options.setup.botToken,
    controlChatId: options.setup.chatId,
    sessions: {},
    approvals: {},
    configuredAt: new Date().toISOString(),
  };

  await writeState(stateDirectory, state);

  warn(`Saved Telegram bot state to ${path.join(stateDirectory, 'state.json')}.`);
  warn(`Validated bot: ${bot.first_name}${bot.username ? ` (@${bot.username})` : ''} [${bot.id}]`);
}

export async function runMonitorCommand(options: { stateDirectory?: string; monitor: MonitorOptions }): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  let state = await readRequiredState(stateDirectory);
  const client = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });

  warn(`Monitoring Telegram updates from ${client.apiBaseUrl}.`);
  warn(`State directory: ${stateDirectory}`);

  let offset = state.lastUpdateId === undefined ? undefined : state.lastUpdateId + 1;

  while (true) {
    const updates = await client.getUpdates({
      timeout: options.monitor.timeoutSeconds,
      allowed_updates: ['message', 'edited_message', 'callback_query'],
      ...(offset === undefined ? {} : { offset }),
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
      warn('Inbound update summary:');
      process.stdout.write(`${JSON.stringify(summarizeUpdate(update), null, 2)}\n`);
      process.stdout.write(`${JSON.stringify(update, null, 2)}\n`);
    }

    if (options.monitor.once) {
      return;
    }
  }
}

export async function runBridgeCommand(options: { bridge: BridgeOptions; stateDirectory?: string }): Promise<void> {
  const stateDirectory = resolveStateDirectory(options.stateDirectory);
  let state = await readRequiredState(stateDirectory);
  state = markPendingApprovalsStale(state);
  await writeState(stateDirectory, state);

  const telegramClient = new TelegramBotClient({
    apiBaseUrl: state.apiBaseUrl,
    botToken: state.botToken,
  });
  const activeTurnByGatewaySessionId = new Map<string, ActiveTurn>();
  const gatewaySessionIdByAcpSessionId = new Map<string, string>();
  const loadedAcpSessionIds = new Set<string>();
  const pendingApprovalResolvers = new Map<string, PendingApprovalResolver>();
  const connectedSessionIdByThreadKey = new Map<string, string>();

  hydrateConnectedSessionIndexes(state, connectedSessionIdByThreadKey, gatewaySessionIdByAcpSessionId);

  const acpClient = await CopilotAcpClient.start({
    copilotPath: options.bridge.copilotPath,
    ...(options.bridge.model ? { model: options.bridge.model } : {}),
    async onSessionUpdate(sessionId, update) {
      const gatewaySessionId = gatewaySessionIdByAcpSessionId.get(sessionId);
      if (!gatewaySessionId) {
        return;
      }

      const activeTurn = activeTurnByGatewaySessionId.get(gatewaySessionId);
      if (!activeTurn) {
        return;
      }

      if (isAcpAgentTextChunkUpdate(update)) {
        activeTurn.textChunks.push(update.content.text);
      }
    },
    async onPermissionRequest(params) {
      return handlePermissionRequest({
        activeTurnByGatewaySessionId,
        getState() {
          return state;
        },
        params,
        pendingApprovalResolvers,
        stateDirectory,
        telegramClient,
        updateState(nextState) {
          state = nextState;
        },
      });
    },
  });

  await restoreConnectedSessions({
    acpClient,
    getState() {
      return state;
    },
    loadedAcpSessionIds,
  });

  warn('Monitoring Telegram updates for topic-session routing.');
  warn(`State directory: ${stateDirectory}`);

  let offset = state.lastUpdateId === undefined ? undefined : state.lastUpdateId + 1;

  try {
    while (true) {
      const updates = await telegramClient.getUpdates({
        timeout: options.bridge.timeoutSeconds,
        allowed_updates: ['message', 'edited_message', 'callback_query'],
        ...(offset === undefined ? {} : { offset }),
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
        warn('Bridge inbound update summary:');
        process.stdout.write(`${JSON.stringify(summarizeUpdate(update), null, 2)}\n`);

        if (update.callback_query) {
          await handleBridgeCallbackQuery({
            callbackQuery: update.callback_query,
            getState() {
              return state;
            },
            pendingApprovalResolvers,
            stateDirectory,
            telegramClient,
            updateState(nextState) {
              state = nextState;
            },
          });
          continue;
        }

        const message = update.message;
        if (!message) {
          continue;
        }

        if (String(message.chat.id) !== state.controlChatId) {
          continue;
        }

        await handleBridgeMessage({
          acpClient,
          activeTurnByGatewaySessionId,
          connectedSessionIdByThreadKey,
          gatewaySessionIdByAcpSessionId,
          getState() {
            return state;
          },
          loadedAcpSessionIds,
          message,
          pendingApprovalResolvers,
          stateDirectory,
          telegramClient,
          updateState(nextState) {
            state = nextState;
          },
        });
      }
    }
  } finally {
    for (const resolver of pendingApprovalResolvers.values()) {
      resolver.resolve(cancelledPermissionResponse());
    }

    await acpClient.close();
  }
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

async function handleBridgeMessage(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  gatewaySessionIdByAcpSessionId: Map<string, string>;
  getState: () => PersistedState;
  loadedAcpSessionIds: Set<string>;
  message: TelegramMessage;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const threadId = options.message.message_thread_id;

  if (options.message.from?.is_bot) {
    return;
  }

  if (threadId === undefined) {
    await handleGeneralTopicMessage({
      acpClient: options.acpClient,
      activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
      connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
      gatewaySessionIdByAcpSessionId: options.gatewaySessionIdByAcpSessionId,
      getState: options.getState,
      loadedAcpSessionIds: options.loadedAcpSessionIds,
      message: options.message,
      pendingApprovalResolvers: options.pendingApprovalResolvers,
      stateDirectory: options.stateDirectory,
      telegramClient: options.telegramClient,
      updateState: options.updateState,
    });
    return;
  }

  if (options.message.forum_topic_closed === true) {
    if (threadId === undefined) {
      return;
    }
    await disconnectSessionForTopic({
      acpClient: options.acpClient,
      activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
      connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
      getState: options.getState,
      pendingApprovalResolvers: options.pendingApprovalResolvers,
      reason: 'topic_closed',
      stateDirectory: options.stateDirectory,
      threadId,
      updateState: options.updateState,
    });
    return;
  }

  const text = options.message.text?.trim();
  if (!text) {
    return;
  }

  await handleSessionTopicMessage({
    acpClient: options.acpClient,
    activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
    connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
    getState: options.getState,
    loadedAcpSessionIds: options.loadedAcpSessionIds,
    message: options.message,
    pendingApprovalResolvers: options.pendingApprovalResolvers,
    stateDirectory: options.stateDirectory,
    telegramClient: options.telegramClient,
    updateState: options.updateState,
  });
}

async function handleGeneralTopicMessage(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  gatewaySessionIdByAcpSessionId: Map<string, string>;
  getState: () => PersistedState;
  loadedAcpSessionIds: Set<string>;
  message: TelegramMessage;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const command = parseGeneralCommand(options.message.text);

  if (!command) {
    await replyInGeneralTopic(
      options.telegramClient,
      options.getState(),
      options.message.message_id,
      [
        'Unknown command.',
        'Use one of:',
        '/new --cwd /absolute/path optional prompt text',
        '/takeover --session-id ACP_SESSION_ID --cwd /absolute/path optional topic label',
        '/list',
        '/kill <gateway-session-id|acp-session-id|topic-thread-id>',
        '/help',
      ].join('\n'),
    );
    return;
  }

  switch (command.command) {
    case 'help': {
      await replyInGeneralTopic(
        options.telegramClient,
        options.getState(),
        options.message.message_id,
        [
          'General Topic commands:',
          '/new --cwd /absolute/path optional prompt text',
          '/takeover --session-id ACP_SESSION_ID --cwd /absolute/path optional topic label',
          '/list',
          '/kill <gateway-session-id|acp-session-id|topic-thread-id>',
          '/help',
        ].join('\n'),
      );
      return;
    }

    case 'list': {
      await replyInGeneralTopic(
        options.telegramClient,
        options.getState(),
        options.message.message_id,
        formatSessionList(options.getState()),
      );
      return;
    }

    case 'kill': {
      const result = await disconnectSessionByTarget({
        acpClient: options.acpClient,
        activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
        connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
        getState: options.getState,
        pendingApprovalResolvers: options.pendingApprovalResolvers,
        stateDirectory: options.stateDirectory,
        target: command.target,
        updateState: options.updateState,
      });
      await replyInGeneralTopic(options.telegramClient, options.getState(), options.message.message_id, result);
      return;
    }

    case 'new': {
      const validationError = await validateWorkingDirectory(command.workingDirectory);
      if (validationError) {
        await replyInGeneralTopic(
          options.telegramClient,
          options.getState(),
          options.message.message_id,
          validationError,
        );
        return;
      }

      const gatewaySessionId = crypto.randomUUID().slice(0, 8);
      const timestamp = new Date().toISOString();
      const workingDirectory = path.resolve(command.workingDirectory);
      const acpSession = await options.acpClient.newSession(workingDirectory);
      options.loadedAcpSessionIds.add(acpSession.sessionId);
      const topicName = buildTopicName(gatewaySessionId, command.prompt);

      const forumTopic = await options.telegramClient.createForumTopic({
        chat_id: options.getState().controlChatId,
        name: topicName,
      });

      const session: PersistedSession = {
        gatewaySessionId,
        acpSessionId: acpSession.sessionId,
        chatId: options.getState().controlChatId,
        topicThreadId: forumTopic.message_thread_id,
        topicName: forumTopic.name,
        workingDirectory,
        status: 'connected',
        permissionMode: 'manual',
        createdAt: timestamp,
        updatedAt: timestamp,
      };

      const nextState: PersistedState = {
        ...options.getState(),
        sessions: {
          ...options.getState().sessions,
          [gatewaySessionId]: session,
        },
      };
      await persistState(options.stateDirectory, nextState, options.updateState);
      options.connectedSessionIdByThreadKey.set(
        createThreadKey(session.chatId, session.topicThreadId),
        gatewaySessionId,
      );
      options.gatewaySessionIdByAcpSessionId.set(acpSession.sessionId, gatewaySessionId);

      const bootstrapMessage = await options.telegramClient.sendMessage({
        chat_id: session.chatId,
        message_thread_id: session.topicThreadId,
        ...renderTelegramMarkdownV2(
          [
            `Session ${gatewaySessionId} started.`,
            `ACP session: ${session.acpSessionId}`,
            `Working directory: ${session.workingDirectory}`,
            'Bridge permission mode: manual',
            command.prompt ? `Initial prompt: ${command.prompt}` : 'Send a message in this topic to continue.',
          ].join('\n'),
        ),
      });

      await replyInGeneralTopic(
        options.telegramClient,
        nextState,
        options.message.message_id,
        [
          `Session ${gatewaySessionId} created.`,
          `Topic: ${session.topicName}`,
          `Thread ID: ${session.topicThreadId}`,
          `Working directory: ${session.workingDirectory}`,
        ].join('\n'),
      );

      if (command.prompt) {
        await startCopilotTurn({
          acpClient: options.acpClient,
          activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
          getState() {
            return nextState;
          },
          gatewaySessionId,
          loadedAcpSessionIds: options.loadedAcpSessionIds,
          promptText: command.prompt,
          replyToMessageId: bootstrapMessage.message_id,
          stateDirectory: options.stateDirectory,
          telegramClient: options.telegramClient,
          updateState: options.updateState,
        });
      }
      return;
    }

    case 'takeover': {
      const validationError = await validateWorkingDirectory(command.workingDirectory);
      if (validationError) {
        await replyInGeneralTopic(
          options.telegramClient,
          options.getState(),
          options.message.message_id,
          validationError,
        );
        return;
      }

      const workingDirectory = path.resolve(command.workingDirectory);
      const existingSession = findSessionByAcpSessionId(options.getState(), command.acpSessionId);
      if (existingSession) {
        await replyInGeneralTopic(
          options.telegramClient,
          options.getState(),
          options.message.message_id,
          [
            `ACP session ${command.acpSessionId} is already known to the gateway.`,
            `Gateway session: ${existingSession.gatewaySessionId}`,
            `Status: ${existingSession.status}`,
            `Thread ID: ${existingSession.topicThreadId}`,
          ].join('\n'),
        );
        return;
      }

      try {
        await loadAcpSession({
          acpClient: options.acpClient,
          loadedAcpSessionIds: options.loadedAcpSessionIds,
          acpSessionId: command.acpSessionId,
          workingDirectory,
        });
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        await replyInGeneralTopic(
          options.telegramClient,
          options.getState(),
          options.message.message_id,
          `Unable to link ACP session: ${message}`,
        );
        return;
      }

      const gatewaySessionId = crypto.randomUUID().slice(0, 8);
      const timestamp = new Date().toISOString();
      const topicName = buildTopicName(
        gatewaySessionId,
        command.prompt ?? `resume ${command.acpSessionId.slice(0, 8)}`,
      );

      const forumTopic = await options.telegramClient.createForumTopic({
        chat_id: options.getState().controlChatId,
        name: topicName,
      });

      const session: PersistedSession = {
        gatewaySessionId,
        acpSessionId: command.acpSessionId,
        chatId: options.getState().controlChatId,
        topicThreadId: forumTopic.message_thread_id,
        topicName: forumTopic.name,
        workingDirectory,
        status: 'connected',
        permissionMode: 'manual',
        createdAt: timestamp,
        updatedAt: timestamp,
      };

      const nextState: PersistedState = {
        ...options.getState(),
        sessions: {
          ...options.getState().sessions,
          [gatewaySessionId]: session,
        },
      };
      await persistState(options.stateDirectory, nextState, options.updateState);
      options.connectedSessionIdByThreadKey.set(
        createThreadKey(session.chatId, session.topicThreadId),
        gatewaySessionId,
      );
      options.gatewaySessionIdByAcpSessionId.set(session.acpSessionId, gatewaySessionId);

      await options.telegramClient.sendMessage({
        chat_id: session.chatId,
        message_thread_id: session.topicThreadId,
        ...renderTelegramMarkdownV2(
          [
            `Existing ACP session linked.`,
            `Gateway session: ${gatewaySessionId}`,
            `ACP session: ${session.acpSessionId}`,
            `Working directory: ${session.workingDirectory}`,
            'Bridge permission mode: manual',
            'Send a message in this topic to continue.',
          ].join('\n'),
        ),
      });

      await replyInGeneralTopic(
        options.telegramClient,
        nextState,
        options.message.message_id,
        [
          `Session ${gatewaySessionId} linked.`,
          `ACP session: ${session.acpSessionId}`,
          `Topic: ${session.topicName}`,
          `Thread ID: ${session.topicThreadId}`,
          `Working directory: ${session.workingDirectory}`,
        ].join('\n'),
      );
      return;
    }
  }
}

async function handleSessionTopicMessage(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  getState: () => PersistedState;
  loadedAcpSessionIds: Set<string>;
  message: TelegramMessage;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const threadId = options.message.message_thread_id;
  if (threadId === undefined) {
    return;
  }

  const threadKey = createThreadKey(String(options.message.chat.id), threadId);
  const gatewaySessionId = options.connectedSessionIdByThreadKey.get(threadKey);
  const sessionCommand = parseSessionCommand(options.message.text);

  if (!gatewaySessionId) {
    await replyInSessionTopic(
      options.telegramClient,
      String(options.message.chat.id),
      threadId,
      options.message.message_id,
      'This topic is not connected to a live session. Start a new one from the General Topic with /new --cwd ...',
    );
    return;
  }

  if (sessionCommand) {
    await handleSessionCommand({
      acpClient: options.acpClient,
      activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
      command: sessionCommand,
      gatewaySessionId,
      getState: options.getState,
      loadedAcpSessionIds: options.loadedAcpSessionIds,
      message: options.message,
      pendingApprovalResolvers: options.pendingApprovalResolvers,
      stateDirectory: options.stateDirectory,
      telegramClient: options.telegramClient,
      updateState: options.updateState,
    });
    return;
  }

  const activeTurn = options.activeTurnByGatewaySessionId.get(gatewaySessionId);
  if (activeTurn) {
    await replyInSessionTopic(
      options.telegramClient,
      String(options.message.chat.id),
      threadId,
      options.message.message_id,
      'A Copilot turn is already running in this topic. Wait for it to finish or send /stop.',
    );
    return;
  }

  const promptText = options.message.text?.trim();
  if (!promptText) {
    return;
  }

  await startCopilotTurn({
    acpClient: options.acpClient,
    activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
    getState: options.getState,
    gatewaySessionId,
    loadedAcpSessionIds: options.loadedAcpSessionIds,
    promptText,
    replyToMessageId: options.message.message_id,
    stateDirectory: options.stateDirectory,
    telegramClient: options.telegramClient,
    updateState: options.updateState,
  });
}

async function handleSessionCommand(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  command: SessionCommand;
  gatewaySessionId: string;
  getState: () => PersistedState;
  loadedAcpSessionIds: Set<string>;
  message: TelegramMessage;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const session = options.getState().sessions[options.gatewaySessionId];
  if (!session) {
    return;
  }

  switch (options.command.command) {
    case 'help': {
      await replyInSessionTopic(
        options.telegramClient,
        session.chatId,
        session.topicThreadId,
        options.message.message_id,
        [
          'Session Topic commands:',
          '- plain text continues the Copilot session',
          '- `/yolo` enables bridge-managed allow-all approval mode for this topic',
          '- `/yolo off` disables bridge-managed allow-all approval mode',
          '- `/yolo show` displays the current permission mode',
          '- `/status` and other unknown slash commands are forwarded to Copilot',
          '- bare `/new` and `/resume` are reserved to avoid changing Copilot context without changing bridge mappings',
          '- use General Topic `/new --cwd ...` for a new managed topic',
          '- use General Topic `/takeover --session-id ... --cwd ...` to link an existing ACP session',
          '- `/copilot <text>` forces raw text to Copilot, for example `/copilot /yolo review this repo`',
          '- `/copilot /yolo` only forwards text to Copilot; it does not toggle bridge approval mode',
          '- `/stop` cancels the active turn',
          '- `/approve [approval-id]` approves a pending permission',
          '- `/deny [approval-id]` denies a pending permission',
        ].join('\n'),
      );
      return;
    }

    case 'yolo': {
      if (options.command.mode === 'show') {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.message.message_id,
          formatPermissionModeMessage(session.permissionMode ?? 'manual'),
        );
        return;
      }

      const permissionMode: PermissionMode = options.command.mode === 'disable' ? 'manual' : 'allow_all';
      const nextState: PersistedState = {
        ...options.getState(),
        sessions: {
          ...options.getState().sessions,
          [session.gatewaySessionId]: {
            ...session,
            permissionMode,
            updatedAt: new Date().toISOString(),
          },
        },
      };
      await persistState(options.stateDirectory, nextState, options.updateState);
      await replyInSessionTopic(
        options.telegramClient,
        session.chatId,
        session.topicThreadId,
        options.message.message_id,
        formatPermissionModeMessage(permissionMode),
      );
      return;
    }

    case 'new':
    case 'resume': {
      await replyInSessionTopic(
        options.telegramClient,
        session.chatId,
        session.topicThreadId,
        options.message.message_id,
        [
          `Bare \`/${options.command.command}\` is reserved in bridge-managed topics.`,
          'Use General Topic `/new --cwd ...` to create a new managed topic.',
          'Use General Topic `/takeover --session-id ... --cwd ...` to link an existing ACP session.',
          `If you intentionally want Copilot-only semantics, send \`/copilot /${options.command.command}\` instead.`,
        ].join('\n'),
      );
      return;
    }

    case 'copilot': {
      if (!options.command.prompt) {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.message.message_id,
          'Usage: `/copilot <text>`\nExample: `/copilot /yolo inspect this repo`',
        );
        return;
      }

      await startCopilotTurn({
        acpClient: options.acpClient,
        activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
        getState: options.getState,
        gatewaySessionId: options.gatewaySessionId,
        loadedAcpSessionIds: options.loadedAcpSessionIds,
        promptText: options.command.prompt,
        replyToMessageId: options.message.message_id,
        stateDirectory: options.stateDirectory,
        telegramClient: options.telegramClient,
        updateState: options.updateState,
      });
      return;
    }

    case 'stop': {
      const activeTurn = options.activeTurnByGatewaySessionId.get(options.gatewaySessionId);
      if (!activeTurn) {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.message.message_id,
          'There is no active Copilot turn to stop in this topic.',
        );
        return;
      }

      activeTurn.cancellationAcknowledged = true;
      await options.acpClient.cancel(session.acpSessionId);
      await replyInSessionTopic(
        options.telegramClient,
        session.chatId,
        session.topicThreadId,
        options.message.message_id,
        'Stop requested for the active Copilot turn.',
      );
      return;
    }

    case 'approve':
    case 'deny': {
      const approval = findApprovalForSession(options.getState(), options.gatewaySessionId, options.command.approvalId);

      if (!approval) {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.message.message_id,
          'No matching pending approval was found for this topic.',
        );
        return;
      }

      const decision = options.command.command === 'approve' ? 'approved' : 'denied';
      const selectedOption = chooseManualApprovalOption(approval, decision);
      if (!selectedOption) {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.message.message_id,
          'This approval does not expose a matching ACP option anymore.',
        );
        return;
      }
      await resolveApproval({
        approval,
        selectedOption,
        getState: options.getState,
        pendingApprovalResolvers: options.pendingApprovalResolvers,
        stateDirectory: options.stateDirectory,
        telegramClient: options.telegramClient,
        updateState: options.updateState,
      });
      await replyInSessionTopic(
        options.telegramClient,
        session.chatId,
        session.topicThreadId,
        options.message.message_id,
        `Recorded ${selectedOption.name} for approval ${approval.approvalId}.`,
      );
      return;
    }
  }
}

async function handleBridgeCallbackQuery(options: {
  callbackQuery: TelegramCallbackQuery;
  getState: () => PersistedState;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const parsed = parsePermissionCallbackData(options.callbackQuery.data);

  if (!parsed || !options.callbackQuery.message) {
    await options.telegramClient.answerCallbackQuery({
      callback_query_id: options.callbackQuery.id,
      text: 'Callback received.',
    });
    return;
  }

  const approval = options.getState().approvals[parsed.approvalId];
  if (!approval || approval.status !== 'pending') {
    await options.telegramClient.answerCallbackQuery({
      callback_query_id: options.callbackQuery.id,
      text: 'This approval is no longer pending.',
    });
    return;
  }

  const selectedOption = approval.options?.find((option) => option.optionId === parsed.optionId);
  if (!selectedOption) {
    await options.telegramClient.answerCallbackQuery({
      callback_query_id: options.callbackQuery.id,
      text: 'This approval option is no longer available.',
    });
    return;
  }

  await resolveApproval({
    approval,
    selectedOption,
    getState: options.getState,
    pendingApprovalResolvers: options.pendingApprovalResolvers,
    stateDirectory: options.stateDirectory,
    telegramClient: options.telegramClient,
    updateState: options.updateState,
  });

  await options.telegramClient.answerCallbackQuery({
    callback_query_id: options.callbackQuery.id,
    text: `Recorded ${selectedOption.name}.`,
  });
}

async function handlePermissionRequest(options: {
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  getState: () => PersistedState;
  params: AcpPermissionRequestParams;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<AcpPermissionResponse> {
  const session = findSessionByAcpSessionId(options.getState(), options.params.sessionId);
  if (!session || session.status !== 'connected') {
    return cancelledPermissionResponse();
  }

  const activeTurn = options.activeTurnByGatewaySessionId.get(session.gatewaySessionId);
  if (!activeTurn) {
    return cancelledPermissionResponse();
  }

  const requestOptions = normalizeApprovalOptions(options.params.options);
  if (requestOptions.length === 0) {
    await replyInSessionTopic(
      options.telegramClient,
      session.chatId,
      session.topicThreadId,
      activeTurn.replyToMessageId,
      'Copilot requested permission without any selectable ACP options, so the bridge cancelled it.',
    );
    return cancelledPermissionResponse();
  }

  const automaticOption =
    (session.permissionMode ?? 'manual') === 'allow_all' ? chooseAutomaticApprovalOption(requestOptions) : null;
  if (automaticOption) {
    await replyInSessionTopic(
      options.telegramClient,
      session.chatId,
      session.topicThreadId,
      activeTurn.replyToMessageId,
      [
        'Permission AUTO-APPROVED',
        `Selection: ${automaticOption.name}`,
        ...buildPermissionContextLines(options.params, session),
      ].join('\n'),
    );
    return selectedPermissionResponse(automaticOption.optionId);
  }

  const approvalId = crypto.randomUUID().slice(0, 8);
  const timestamp = new Date().toISOString();
  const approval: PersistedApproval = {
    approvalId,
    gatewaySessionId: session.gatewaySessionId,
    acpSessionId: session.acpSessionId,
    topicThreadId: session.topicThreadId,
    status: 'pending',
    ...(typeof options.params.title === 'string' ? { title: options.params.title } : {}),
    ...(typeof options.params.description === 'string' ? { description: options.params.description } : {}),
    ...(typeof options.params.toolCallId === 'string' ? { toolCallId: options.params.toolCallId } : {}),
    contextLines: buildPermissionContextLines(options.params, session),
    options: requestOptions,
    createdAt: timestamp,
    updatedAt: timestamp,
  };

  const promptMessage = await options.telegramClient.sendMessage({
    chat_id: session.chatId,
    message_thread_id: session.topicThreadId,
    ...renderTelegramMarkdownV2(formatPermissionMessage(approval, 'pending')),
    reply_markup: createPermissionMarkup(approvalId, requestOptions),
  });

  approval.promptMessageId = promptMessage.message_id;
  const nextState: PersistedState = {
    ...options.getState(),
    sessions: {
      ...options.getState().sessions,
      [session.gatewaySessionId]: {
        ...session,
        latestApprovalId: approvalId,
        updatedAt: timestamp,
      },
    },
    approvals: {
      ...options.getState().approvals,
      [approvalId]: approval,
    },
  };
  await persistState(options.stateDirectory, nextState, options.updateState);

  return await new Promise<AcpPermissionResponse>((resolve) => {
    options.pendingApprovalResolvers.set(approvalId, {
      resolve,
    });
  });
}

async function startCopilotTurn(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  getState: () => PersistedState;
  gatewaySessionId: string;
  loadedAcpSessionIds: Set<string>;
  promptText: string;
  replyToMessageId: number;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const session = options.getState().sessions[options.gatewaySessionId];
  if (!session) {
    return;
  }

  try {
    await ensureAcpSessionLoaded({
      acpClient: options.acpClient,
      loadedAcpSessionIds: options.loadedAcpSessionIds,
      session,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown ACP load error.';
    await replyInSessionTopic(
      options.telegramClient,
      session.chatId,
      session.topicThreadId,
      options.replyToMessageId,
      `Copilot ACP error: ${message}`,
    );
    return;
  }

  const turn: ActiveTurn = {
    cancellationAcknowledged: false,
    gatewaySessionId: session.gatewaySessionId,
    acpSessionId: session.acpSessionId,
    chatId: session.chatId,
    topicThreadId: session.topicThreadId,
    replyToMessageId: options.replyToMessageId,
    textChunks: [],
  };
  options.activeTurnByGatewaySessionId.set(session.gatewaySessionId, turn);

  const nextState: PersistedState = {
    ...options.getState(),
    sessions: {
      ...options.getState().sessions,
      [session.gatewaySessionId]: {
        ...session,
        lastPromptAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    },
  };
  await persistState(options.stateDirectory, nextState, options.updateState);

  await options.telegramClient.sendChatAction({
    chat_id: session.chatId,
    message_thread_id: session.topicThreadId,
    action: 'typing',
  });

  void runCopilotTurn({
    ...options,
    turn,
  }).finally(() => {
    options.activeTurnByGatewaySessionId.delete(session.gatewaySessionId);
  });
}

async function runCopilotTurn(options: {
  acpClient: CopilotAcpClient;
  getState: () => PersistedState;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  turn: ActiveTurn;
  promptText: string;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  try {
    const promptResult = await options.acpClient.prompt(options.turn.acpSessionId, options.promptText);
    const normalizedText = normalizeCopilotText(options.turn.textChunks.join(''));
    const session = options.getState().sessions[options.turn.gatewaySessionId];
    if (!session) {
      return;
    }

    const nextState: PersistedState = {
      ...options.getState(),
      sessions: {
        ...options.getState().sessions,
        [session.gatewaySessionId]: {
          ...session,
          lastStopReason: promptResult.stopReason,
          updatedAt: new Date().toISOString(),
        },
      },
    };
    await persistState(options.stateDirectory, nextState, options.updateState);

    if (promptResult.stopReason === 'cancelled') {
      if (!options.turn.cancellationAcknowledged) {
        await replyInSessionTopic(
          options.telegramClient,
          session.chatId,
          session.topicThreadId,
          options.turn.replyToMessageId,
          'Copilot cancelled the current turn.',
        );
      }
      return;
    }

    const replyText =
      normalizedText.length > 0 ? normalizedText : `Copilot finished with stopReason=${promptResult.stopReason}.`;
    const chunks = splitTextForTelegram(replyText);

    for (const [index, chunk] of chunks.entries()) {
      await options.telegramClient.sendMessage({
        chat_id: session.chatId,
        message_thread_id: session.topicThreadId,
        ...renderTelegramMarkdownV2(chunk),
        ...(index === 0
          ? {
              reply_parameters: {
                message_id: options.turn.replyToMessageId,
                allow_sending_without_reply: true,
              },
            }
          : {}),
      });
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown Copilot ACP error.';
    const session = options.getState().sessions[options.turn.gatewaySessionId];
    if (!session) {
      return;
    }

    await replyInSessionTopic(
      options.telegramClient,
      session.chatId,
      session.topicThreadId,
      options.turn.replyToMessageId,
      `Copilot ACP error: ${message}`,
    );
  }
}

async function disconnectSessionByTarget(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  getState: () => PersistedState;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  target: string;
  updateState: (state: PersistedState) => void;
}): Promise<string> {
  const session = findSessionByAnyTarget(options.getState(), options.target);
  if (!session) {
    return `No session matched target ${options.target}.`;
  }

  await disconnectSession({
    acpClient: options.acpClient,
    activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
    connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
    getState: options.getState,
    gatewaySessionId: session.gatewaySessionId,
    pendingApprovalResolvers: options.pendingApprovalResolvers,
    reason: 'killed',
    stateDirectory: options.stateDirectory,
    updateState: options.updateState,
  });

  return [
    `Killed session ${session.gatewaySessionId}.`,
    `ACP session: ${session.acpSessionId}`,
    `Thread ID: ${session.topicThreadId}`,
  ].join('\n');
}

async function disconnectSessionForTopic(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  getState: () => PersistedState;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  reason: SessionStatus;
  stateDirectory: string;
  threadId: number;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const threadKey = createThreadKey(options.getState().controlChatId, options.threadId);
  const gatewaySessionId = options.connectedSessionIdByThreadKey.get(threadKey);
  if (!gatewaySessionId) {
    return;
  }

  await disconnectSession({
    acpClient: options.acpClient,
    activeTurnByGatewaySessionId: options.activeTurnByGatewaySessionId,
    connectedSessionIdByThreadKey: options.connectedSessionIdByThreadKey,
    getState: options.getState,
    gatewaySessionId,
    pendingApprovalResolvers: options.pendingApprovalResolvers,
    reason: options.reason,
    stateDirectory: options.stateDirectory,
    updateState: options.updateState,
  });
}

async function disconnectSession(options: {
  acpClient: CopilotAcpClient;
  activeTurnByGatewaySessionId: Map<string, ActiveTurn>;
  connectedSessionIdByThreadKey: Map<string, string>;
  getState: () => PersistedState;
  gatewaySessionId: string;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  reason: SessionStatus;
  stateDirectory: string;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const session = options.getState().sessions[options.gatewaySessionId];
  if (!session) {
    return;
  }

  const activeTurn = options.activeTurnByGatewaySessionId.get(options.gatewaySessionId);
  if (activeTurn) {
    activeTurn.cancellationAcknowledged = true;
    await options.acpClient.cancel(session.acpSessionId);
  }

  const nextApprovals = { ...options.getState().approvals };
  for (const approval of Object.values(nextApprovals)) {
    if (approval.gatewaySessionId === options.gatewaySessionId && approval.status === 'pending') {
      nextApprovals[approval.approvalId] = {
        ...approval,
        status: 'cancelled',
        updatedAt: new Date().toISOString(),
      };
      const resolver = options.pendingApprovalResolvers.get(approval.approvalId);
      if (resolver) {
        resolver.resolve(cancelledPermissionResponse());
        options.pendingApprovalResolvers.delete(approval.approvalId);
      }
    }
  }

  const nextState: PersistedState = {
    ...options.getState(),
    sessions: {
      ...options.getState().sessions,
      [session.gatewaySessionId]: {
        ...session,
        status: options.reason,
        updatedAt: new Date().toISOString(),
      },
    },
    approvals: nextApprovals,
  };
  await persistState(options.stateDirectory, nextState, options.updateState);
  options.connectedSessionIdByThreadKey.delete(createThreadKey(session.chatId, session.topicThreadId));
}

async function resolveApproval(options: {
  approval: PersistedApproval;
  selectedOption: PersistedApprovalOption;
  getState: () => PersistedState;
  pendingApprovalResolvers: Map<string, PendingApprovalResolver>;
  stateDirectory: string;
  telegramClient: TelegramBotClient;
  updateState: (state: PersistedState) => void;
}): Promise<void> {
  const resolver = options.pendingApprovalResolvers.get(options.approval.approvalId);
  if (resolver) {
    resolver.resolve(selectedPermissionResponse(options.selectedOption.optionId));
    options.pendingApprovalResolvers.delete(options.approval.approvalId);
  }

  const nextState: PersistedState = {
    ...options.getState(),
    approvals: {
      ...options.getState().approvals,
      [options.approval.approvalId]: {
        ...options.approval,
        status: approvalStatusForOption(options.selectedOption.kind),
        selectedOptionId: options.selectedOption.optionId,
        selectedOptionKind: options.selectedOption.kind,
        selectedOptionName: options.selectedOption.name,
        updatedAt: new Date().toISOString(),
      },
    },
  };
  await persistState(options.stateDirectory, nextState, options.updateState);

  if (options.approval.promptMessageId !== undefined) {
    await options.telegramClient.editMessageText({
      chat_id: options.getState().controlChatId,
      message_id: options.approval.promptMessageId,
      ...renderTelegramMarkdownV2(
        formatPermissionMessage(
          {
            ...options.approval,
            selectedOptionName: options.selectedOption.name,
          },
          approvalStatusForOption(options.selectedOption.kind),
        ),
      ),
    });
  }
}

function formatSessionList(state: PersistedState): string {
  const sessions = Object.values(state.sessions).sort((left, right) => left.createdAt.localeCompare(right.createdAt));

  if (sessions.length === 0) {
    return 'No sessions are recorded yet.';
  }

  return sessions
    .map((session) =>
      [
        `${session.gatewaySessionId} [${session.status}]`,
        `  thread=${session.topicThreadId}`,
        `  acp=${session.acpSessionId}`,
        `  cwd=${session.workingDirectory}`,
        `  permission=${session.permissionMode ?? 'manual'}`,
      ].join('\n'),
    )
    .join('\n\n');
}

function sanitizeStateForDisplay(state: PersistedState): Record<string, unknown> {
  return {
    version: state.version,
    apiBaseUrl: state.apiBaseUrl,
    botTokenPreview: `${state.botToken.slice(0, 8)}...${state.botToken.slice(-4)}`,
    controlChatId: state.controlChatId,
    configuredAt: state.configuredAt,
    lastPollAt: state.lastPollAt,
    lastUpdateId: state.lastUpdateId,
    sessions: state.sessions,
    approvals: state.approvals,
  };
}

async function validateWorkingDirectory(workingDirectory: string): Promise<string | null> {
  if (!path.isAbsolute(workingDirectory)) {
    return 'workingDirectory must be an absolute path.';
  }

  try {
    const directoryStat = await stat(workingDirectory);
    if (!directoryStat.isDirectory()) {
      return `workingDirectory is not a directory: ${workingDirectory}`;
    }

    await access(workingDirectory);
    return null;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    return `workingDirectory is not accessible: ${workingDirectory} (${message})`;
  }
}

function buildTopicName(gatewaySessionId: string, prompt: string | null): string {
  const snippet = prompt?.replace(/\s+/gu, ' ').trim() ?? '';
  const base = snippet.length > 0 ? `session-${gatewaySessionId} ${snippet}` : `session-${gatewaySessionId}`;
  return base.slice(0, 120);
}

function createThreadKey(chatId: string, threadId: number): string {
  return `${chatId}:${threadId}`;
}

function hydrateConnectedSessionIndexes(
  state: PersistedState,
  connectedSessionIdByThreadKey: Map<string, string>,
  gatewaySessionIdByAcpSessionId: Map<string, string>,
): void {
  for (const session of Object.values(state.sessions)) {
    if (session.status !== 'connected') {
      continue;
    }

    connectedSessionIdByThreadKey.set(createThreadKey(session.chatId, session.topicThreadId), session.gatewaySessionId);
    gatewaySessionIdByAcpSessionId.set(session.acpSessionId, session.gatewaySessionId);
  }
}

async function restoreConnectedSessions(options: {
  acpClient: CopilotAcpClient;
  getState: () => PersistedState;
  loadedAcpSessionIds: Set<string>;
}): Promise<void> {
  for (const session of Object.values(options.getState().sessions)) {
    if (session.status !== 'connected') {
      continue;
    }

    try {
      await ensureAcpSessionLoaded({
        acpClient: options.acpClient,
        loadedAcpSessionIds: options.loadedAcpSessionIds,
        session,
      });
      warn(`Restored ACP session ${session.acpSessionId} for gateway session ${session.gatewaySessionId}.`);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      warn(
        `Unable to restore ACP session ${session.acpSessionId} for gateway session ${session.gatewaySessionId}: ${message}`,
      );
    }
  }
}

async function ensureAcpSessionLoaded(options: {
  acpClient: CopilotAcpClient;
  loadedAcpSessionIds: Set<string>;
  session: PersistedSession;
}): Promise<void> {
  await loadAcpSession({
    acpClient: options.acpClient,
    loadedAcpSessionIds: options.loadedAcpSessionIds,
    acpSessionId: options.session.acpSessionId,
    workingDirectory: options.session.workingDirectory,
  });
}

async function loadAcpSession(options: {
  acpClient: CopilotAcpClient;
  loadedAcpSessionIds: Set<string>;
  acpSessionId: string;
  workingDirectory: string;
}): Promise<void> {
  if (options.loadedAcpSessionIds.has(options.acpSessionId)) {
    return;
  }

  if (!options.acpClient.supportsLoadSession()) {
    throw new Error('This Copilot ACP agent does not support session restoration after restart.');
  }

  try {
    await options.acpClient.loadSession(options.acpSessionId, options.workingDirectory);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (message.includes('not found')) {
      throw new Error(
        `Unable to resume ACP session ${options.acpSessionId}. Copilot could not find persisted state for it. Start a new topic with /new if this session never completed a turn.`,
      );
    }

    throw error;
  }

  options.loadedAcpSessionIds.add(options.acpSessionId);
}

function markPendingApprovalsStale(state: PersistedState): PersistedState {
  const approvals: Record<string, PersistedApproval> = Object.fromEntries(
    Object.entries(state.approvals).map(([approvalId, approval]) => {
      if (approval.status !== 'pending') {
        return [approvalId, approval] satisfies [string, PersistedApproval];
      }

      return [
        approvalId,
        {
          ...approval,
          status: 'stale',
          updatedAt: new Date().toISOString(),
        },
      ] satisfies [string, PersistedApproval];
    }),
  );

  return {
    ...state,
    approvals,
  };
}

function findSessionByAcpSessionId(state: PersistedState, acpSessionId: string): PersistedSession | null {
  return Object.values(state.sessions).find((session) => session.acpSessionId === acpSessionId) ?? null;
}

function findSessionByAnyTarget(state: PersistedState, target: string): PersistedSession | null {
  return (
    Object.values(state.sessions).find(
      (session) =>
        session.gatewaySessionId === target ||
        session.acpSessionId === target ||
        String(session.topicThreadId) === target,
    ) ?? null
  );
}

function findApprovalForSession(
  state: PersistedState,
  gatewaySessionId: string,
  explicitApprovalId: string | null,
): PersistedApproval | null {
  if (explicitApprovalId) {
    const approval = state.approvals[explicitApprovalId];
    return approval && approval.gatewaySessionId === gatewaySessionId && approval.status === 'pending'
      ? approval
      : null;
  }

  const pending = Object.values(state.approvals)
    .filter((approval) => approval.gatewaySessionId === gatewaySessionId && approval.status === 'pending')
    .sort((left, right) => left.createdAt.localeCompare(right.createdAt));
  return pending.at(-1) ?? null;
}

function formatPermissionMessage(
  approval: Pick<
    PersistedApproval,
    'approvalId' | 'title' | 'description' | 'toolCallId' | 'contextLines' | 'options' | 'selectedOptionName'
  >,
  status: PersistedApproval['status'],
): string {
  return [
    `Permission ${status.toUpperCase()}`,
    `Approval ID: ${approval.approvalId}`,
    ...(approval.contextLines ?? []),
    approval.title ? `Title: ${approval.title}` : null,
    approval.description ? `Description: ${approval.description}` : null,
    approval.toolCallId ? `Tool call ID: ${approval.toolCallId}` : null,
    approval.options && approval.options.length > 0
      ? `Options: ${approval.options.map((option) => option.name).join(', ')}`
      : null,
    approval.selectedOptionName ? `Selected option: ${approval.selectedOptionName}` : null,
  ]
    .filter((line): line is string => line !== null)
    .join('\n');
}

function cancelledPermissionResponse(): AcpPermissionResponse {
  return {
    outcome: {
      outcome: 'cancelled',
    },
  };
}

function selectedPermissionResponse(optionId: string): AcpPermissionResponse {
  return {
    outcome: {
      outcome: 'selected',
      optionId,
    },
  };
}

function splitTextForTelegram(text: string): string[] {
  const maximumChunkLength = 3500;
  if (text.length <= maximumChunkLength) {
    return [text];
  }

  const chunks: string[] = [];
  for (let start = 0; start < text.length; start += maximumChunkLength) {
    chunks.push(text.slice(start, start + maximumChunkLength));
  }

  return chunks;
}

function normalizeCopilotText(value: string): string {
  return value.replace(/\r\n/gu, '\n').trim();
}

function buildPermissionContextLines(params: AcpPermissionRequestParams, session: PersistedSession): string[] {
  const contextLines = [
    `Session: ${session.gatewaySessionId}`,
    `ACP session: ${session.acpSessionId}`,
    `Working directory: ${session.workingDirectory}`,
  ];

  if (params.toolCall?.title) {
    contextLines.push(`Tool title: ${params.toolCall.title}`);
  }

  if (params.toolCall?.kind) {
    contextLines.push(`Tool kind: ${params.toolCall.kind}`);
  }

  const toolInputSummary = summarizeToolInput(params.toolCall?.rawInput as Record<string, unknown> | undefined);
  if (toolInputSummary) {
    contextLines.push(`Tool input: ${toolInputSummary}`);
  }

  if (params.options && params.options.length > 0) {
    contextLines.push(`Selectable options: ${params.options.map((option) => option.name).join(', ')}`);
  }

  const supplementalEntries = Object.entries(params)
    .filter(
      ([key, value]) =>
        !['sessionId', 'title', 'description', 'toolCallId', 'toolCall', 'options'].includes(key) &&
        value !== undefined,
    )
    .slice(0, 5)
    .map(([key, value]) => `${humanizePermissionFieldName(key)}: ${summarizePermissionFieldValue(value)}`);

  return [...contextLines, ...supplementalEntries];
}

function humanizePermissionFieldName(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/gu, '$1 $2')
    .replace(/[_-]+/gu, ' ')
    .replace(/^./u, (character) => character.toUpperCase());
}

function summarizePermissionFieldValue(value: unknown): string {
  if (typeof value === 'string') {
    return truncateSingleLine(value);
  }

  if (typeof value === 'number' || typeof value === 'boolean' || value === null) {
    return String(value);
  }

  try {
    return truncateSingleLine(JSON.stringify(value));
  } catch {
    return '[unserializable value]';
  }
}

function truncateSingleLine(value: string): string {
  const normalized = value.replace(/\s+/gu, ' ').trim();
  return normalized.length <= 180 ? normalized : `${normalized.slice(0, 177)}...`;
}

function normalizeApprovalOptions(options: AcpPermissionOption[] | undefined): PersistedApprovalOption[] {
  if (!options || options.length === 0) {
    return [];
  }

  return options
    .filter(
      (option) =>
        typeof option.optionId === 'string' &&
        option.optionId.length > 0 &&
        typeof option.name === 'string' &&
        option.name.length > 0 &&
        typeof option.kind === 'string',
    )
    .map((option) => ({
      optionId: option.optionId,
      kind: option.kind,
      name: option.name,
    }));
}

function chooseManualApprovalOption(
  approval: PersistedApproval,
  decision: 'approved' | 'denied',
): PersistedApprovalOption | null {
  const options = approval.options ?? [];
  const preferredKinds = decision === 'approved' ? ['allow_once', 'allow_always'] : ['reject_once', 'reject_always'];
  for (const kind of preferredKinds) {
    const option = options.find((entry) => entry.kind === kind);
    if (option) {
      return option;
    }
  }

  return null;
}

function chooseAutomaticApprovalOption(options: PersistedApprovalOption[]): PersistedApprovalOption | null {
  return (
    options.find((option) => option.kind === 'allow_always') ??
    options.find((option) => option.kind === 'allow_once') ??
    null
  );
}

function approvalStatusForOption(kind: PersistedApprovalOption['kind']): PersistedApproval['status'] {
  return kind.startsWith('allow_') ? 'approved' : 'denied';
}

function formatPermissionModeMessage(permissionMode: PermissionMode): string {
  return permissionMode === 'allow_all'
    ? [
        'Bridge permission mode: ALLOW_ALL',
        'The bridge will auto-select Copilot permission options for this topic.',
        'Preference order: `allow_always`, then `allow_once`.',
        'This is the bridge-side equivalent of Copilot CLI `/yolo` or `/allow-all`.',
      ].join('\n')
    : [
        'Bridge permission mode: MANUAL',
        'The bridge will pause on Copilot permission requests and wait for Telegram approval.',
      ].join('\n');
}

function summarizeToolInput(input: Record<string, unknown> | undefined): string | null {
  if (!input) {
    return null;
  }

  const command = input['command'];
  if (typeof command === 'string' && command.length > 0) {
    return truncateSingleLine(command);
  }

  const pathValue = input['path'];
  if (typeof pathValue === 'string' && pathValue.length > 0) {
    return truncateSingleLine(pathValue);
  }

  try {
    return truncateSingleLine(JSON.stringify(input));
  } catch {
    return null;
  }
}

function isAcpAgentTextChunkUpdate(value: unknown): value is AcpAgentMessageChunkUpdate {
  if (!value || typeof value !== 'object') {
    return false;
  }

  const candidate = value as Partial<AcpAgentMessageChunkUpdate>;
  return (
    candidate.sessionUpdate === 'agent_message_chunk' &&
    candidate.content?.type === 'text' &&
    typeof candidate.content.text === 'string'
  );
}

async function replyInGeneralTopic(
  telegramClient: TelegramBotClient,
  state: PersistedState,
  replyToMessageId: number,
  text: string,
): Promise<void> {
  await telegramClient.sendMessage({
    chat_id: state.controlChatId,
    ...renderTelegramMarkdownV2(text),
    reply_parameters: {
      message_id: replyToMessageId,
      allow_sending_without_reply: true,
    },
  });
}

async function replyInSessionTopic(
  telegramClient: TelegramBotClient,
  chatId: string,
  topicThreadId: number,
  replyToMessageId: number,
  text: string,
): Promise<void> {
  await telegramClient.sendMessage({
    chat_id: chatId,
    message_thread_id: topicThreadId,
    ...renderTelegramMarkdownV2(text),
    reply_parameters: {
      message_id: replyToMessageId,
      allow_sending_without_reply: true,
    },
  });
}

async function persistState(
  stateDirectory: string,
  nextState: PersistedState,
  updateState: (state: PersistedState) => void,
): Promise<void> {
  await writeState(stateDirectory, nextState);
  updateState(nextState);
}

async function readRequiredState(stateDirectory: string): Promise<PersistedState> {
  const state = await readState(stateDirectory);

  if (!state) {
    throw new Error(`No bot state found in ${stateDirectory}. Run the setup command first.`);
  }

  return state;
}

function warn(message: string): void {
  process.stderr.write(`[telegram-topic-session-bridge] ${message}\n`);
}
