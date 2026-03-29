export type TelegramChatId = number | string;
export type TelegramParseMode = 'MarkdownV2';
export type PermissionMode = 'manual' | 'allow_all';
export type SessionStatus = 'connected' | 'killed' | 'topic_closed';
export type ApprovalStatus =
  | 'pending'
  | 'approved'
  | 'denied'
  | 'cancelled'
  | 'stale';

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

export interface TelegramForumTopicCreated {
  name: string;
  icon_color?: number;
  icon_custom_emoji_id?: string;
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
  forum_topic_created?: TelegramForumTopicCreated;
  forum_topic_closed?: true;
  forum_topic_reopened?: true;
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
  parameters?: {
    retry_after?: number;
  };
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
  offset?: number;
  timeout?: number;
  allowed_updates?: string[];
}

export interface SendMessageRequest {
  chat_id: TelegramChatId;
  text: string;
  parse_mode?: TelegramParseMode;
  message_thread_id?: number;
  reply_parameters?: TelegramReplyParameters;
  reply_markup?: TelegramInlineKeyboardMarkup;
}

export interface EditMessageTextRequest {
  chat_id: TelegramChatId;
  message_id: number;
  text: string;
  parse_mode?: TelegramParseMode;
}

export interface SendChatActionRequest {
  chat_id: TelegramChatId;
  action: string;
  message_thread_id?: number;
}

export interface AnswerCallbackQueryRequest {
  callback_query_id: string;
  text?: string;
}

export interface CreateForumTopicRequest {
  chat_id: TelegramChatId;
  name: string;
}

export interface ForumTopic {
  message_thread_id: number;
  name: string;
  icon_color?: number;
  icon_custom_emoji_id?: string;
}

export interface PersistedApproval {
  approvalId: string;
  gatewaySessionId: string;
  acpSessionId: string;
  topicThreadId: number;
  status: ApprovalStatus;
  title?: string;
  description?: string;
  toolCallId?: string;
  contextLines?: string[];
  options?: PersistedApprovalOption[];
  selectedOptionId?: string;
  selectedOptionName?: string;
  selectedOptionKind?: AcpPermissionOptionKind;
  promptMessageId?: number;
  createdAt: string;
  updatedAt: string;
}

export interface PersistedApprovalOption {
  optionId: string;
  kind: AcpPermissionOptionKind;
  name: string;
}

export interface PersistedSession {
  gatewaySessionId: string;
  acpSessionId: string;
  chatId: string;
  topicThreadId: number;
  topicName: string;
  workingDirectory: string;
  status: SessionStatus;
  createdAt: string;
  updatedAt: string;
  lastPromptAt?: string;
  lastStopReason?: string;
  latestApprovalId?: string;
  permissionMode?: PermissionMode;
}

export interface PersistedState {
  version: 1;
  apiBaseUrl: string;
  botToken: string;
  controlChatId: string;
  sessions: Record<string, PersistedSession>;
  approvals: Record<string, PersistedApproval>;
  configuredAt?: string;
  lastPollAt?: string;
  lastUpdateId?: number;
}

export interface SetupOptions {
  apiBaseUrl?: string;
  botToken: string;
  chatId: string;
}

export interface MonitorOptions {
  once: boolean;
  timeoutSeconds: number;
}

export interface BridgeOptions {
  copilotPath: string;
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
  forumTopicClosed: boolean;
  forumTopicReopened: boolean;
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
  messageThreadId: number | null;
}

export type GeneralCommand =
  | { command: 'help' }
  | { command: 'kill'; target: string }
  | { command: 'list' }
  | { command: 'new'; workingDirectory: string; prompt: string | null }
  | {
      command: 'takeover';
      acpSessionId: string;
      workingDirectory: string;
      prompt: string | null;
    };

export type SessionCommand =
  | { command: 'approve'; approvalId: string | null }
  | { command: 'copilot'; prompt: string | null }
  | { command: 'deny'; approvalId: string | null }
  | { command: 'help' }
  | { command: 'new' }
  | { command: 'resume' }
  | { command: 'yolo'; mode: 'enable' | 'disable' | 'show' }
  | { command: 'stop' };

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

export type AcpToolCallKind =
  | 'read'
  | 'edit'
  | 'delete'
  | 'move'
  | 'search'
  | 'execute'
  | 'think'
  | 'fetch'
  | 'other';

export type AcpToolCallStatus =
  | 'pending'
  | 'in_progress'
  | 'completed'
  | 'failed';

export interface AcpToolCallUpdate {
  sessionUpdate: 'tool_call' | 'tool_call_update';
  toolCallId: string;
  title?: string;
  kind?: AcpToolCallKind;
  status?: AcpToolCallStatus;
  content?: unknown[];
  rawInput?: Record<string, unknown>;
  rawOutput?: Record<string, unknown>;
}

export type AcpPermissionOptionKind =
  | 'allow_once'
  | 'allow_always'
  | 'reject_once'
  | 'reject_always';

export interface AcpPermissionOption {
  optionId: string;
  kind: AcpPermissionOptionKind;
  name: string;
}

export interface AcpPermissionRequestParams {
  sessionId: string;
  toolCallId?: string;
  title?: string;
  description?: string;
  toolCall?: {
    toolCallId?: string;
    title?: string;
    kind?: AcpToolCallKind;
    status?: AcpToolCallStatus;
    rawInput?: Record<string, unknown>;
    rawOutput?: Record<string, unknown>;
  };
  options?: AcpPermissionOption[];
  [key: string]: unknown;
}

export interface AcpPermissionResponse {
  outcome:
    | {
        outcome: 'cancelled';
      }
    | {
        outcome: 'selected';
        optionId: string;
      };
}

export interface ParsedApprovalCallback {
  approvalId: string;
  optionId: string;
}
