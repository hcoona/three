export type TelegramChatId = number | string;

export interface TelegramUser {
  id: number;
  is_bot: boolean;
  first_name: string;
  last_name?: string;
  username?: string;
  language_code?: string;
}

export interface TelegramChat {
  id: TelegramChatId;
  type: string;
  title?: string;
  username?: string;
  first_name?: string;
  last_name?: string;
  is_forum?: boolean;
}

export interface TelegramReplyParameters {
  message_id: number;
  allow_sending_without_reply?: boolean;
}

export interface TelegramInlineKeyboardButton {
  text: string;
  callback_data?: string;
}

export interface TelegramInlineKeyboardMarkup {
  inline_keyboard: TelegramInlineKeyboardButton[][];
}

export interface TelegramMessage {
  message_id: number;
  date: number;
  chat: TelegramChat;
  from?: TelegramUser;
  text?: string;
  message_thread_id?: number;
  is_topic_message?: boolean;
  reply_to_message?: TelegramMessageReply;
}

export interface TelegramMessageReply {
  message_id: number;
  date?: number;
  chat: TelegramChat;
  from?: TelegramUser;
  text?: string;
}

export interface TelegramCallbackQuery {
  id: string;
  from: TelegramUser;
  message?: TelegramMessage;
  data?: string;
}

export interface TelegramUpdate {
  update_id: number;
  message?: TelegramMessage;
  edited_message?: TelegramMessage;
  callback_query?: TelegramCallbackQuery;
}

export interface TelegramApiEnvelope<T> {
  ok: boolean;
  result?: T;
  description?: string;
  error_code?: number;
}

export interface GetMeResponse {
  id: number;
  is_bot: boolean;
  first_name: string;
  username?: string;
  can_join_groups?: boolean;
  can_read_all_group_messages?: boolean;
  supports_inline_queries?: boolean;
}

export interface GetUpdatesOptions {
  offset?: number | undefined;
  timeout?: number | undefined;
  allowed_updates?: string[] | undefined;
}

export interface SendMessageRequest {
  chat_id: TelegramChatId;
  text: string;
  reply_parameters?: TelegramReplyParameters | undefined;
  reply_markup?: TelegramInlineKeyboardMarkup | undefined;
}

export interface EditMessageTextRequest {
  chat_id: TelegramChatId;
  message_id: number;
  text: string;
}

export interface SendChatActionRequest {
  chat_id: TelegramChatId;
  action: string;
}

export interface AnswerCallbackQueryRequest {
  callback_query_id: string;
  text?: string | undefined;
}

export interface PersistedState {
  version: 1;
  apiBaseUrl: string;
  botToken: string;
  defaultChatId?: string;
  lastAcpSessionId?: string;
  lastAcpStopReason?: string;
  lastUpdateId?: number;
  configuredAt?: string;
  lastPollAt?: string;
  lastObservedChatId?: string;
  lastObservedMessageId?: number;
  lastObservedAt?: string;
  lastCallbackQueryId?: string;
  lastCallbackData?: string;
}

export interface SetupOptions {
  apiBaseUrl?: string;
  botToken: string;
  chatId?: string;
}

export interface MonitorOptions {
  once: boolean;
  sendReplies: boolean;
  replyPrefix: string;
  answerCallbacks: boolean;
  timeoutSeconds: number;
}

export interface BridgeOptions {
  copilotPath: string;
  cwd: string;
  model?: string;
  timeoutSeconds: number;
}

export interface MessageSummary {
  messageId: number;
  chatId: string;
  chatType: string;
  fromId: number | null;
  text: string | null;
  replyToMessageId: number | null;
  messageThreadId: number | null;
  isTopicMessage: boolean;
}

export interface UpdateSummary {
  updateId: number;
  kind: string;
  chatId: string | null;
  messageId: number | null;
  fromId: number | null;
  text: string | null;
  callbackData: string | null;
  replyToMessageId: number | null;
}

export interface ParsedDemoCallback {
  action: 'approve' | 'deny' | 'stop';
  nonce: string;
}

export interface AcpInitializeResult {
  protocolVersion: number;
  agentCapabilities?: {
    loadSession?: boolean;
    sessionCapabilities?: {
      list?: Record<string, never>;
    };
  };
  agentInfo?: {
    name?: string;
    title?: string;
    version?: string;
  };
}

export interface AcpSessionResult {
  sessionId: string;
}

export interface AcpPromptResult {
  stopReason: string;
}

export interface AcpTextContent {
  type: 'text';
  text: string;
}

export interface AcpAgentMessageChunkUpdate {
  sessionUpdate: 'agent_message_chunk';
  content: AcpTextContent;
}

export interface AcpPermissionRequestParams {
  sessionId: string;
  toolCallId?: string;
  title?: string;
  description?: string;
  [key: string]: unknown;
}

export interface AcpPermissionResponse {
  outcome: {
    outcome: 'cancelled';
  };
}
