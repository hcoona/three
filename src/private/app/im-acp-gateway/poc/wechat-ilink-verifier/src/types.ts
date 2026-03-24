export interface BotQrCodeResponse {
  qrcode?: string;
  qrcode_img_content?: string;
  url?: string;
  ret?: number;
  errcode?: number;
  errmsg?: string;
}

export interface QrCodeStatusResponse {
  status?: string;
  bot_token?: string;
  baseurl?: string;
  ret?: number;
  errcode?: number;
  errmsg?: string;
}

export interface RefMessage {
  msg_id?: string | number;
  message_id?: string | number;
  session_id?: string;
  context_token?: string;
  item_list?: MessageItem[];
  [key: string]: unknown;
}

export interface TextItem {
  text?: string;
}

export interface MessageItem {
  type: number;
  msg_id?: string | number;
  text_item?: TextItem;
  ref_msg?: RefMessage;
  [key: string]: unknown;
}

export interface WeixinMessage {
  seq?: number;
  message_id?: number | string;
  from_user_id?: string;
  to_user_id?: string;
  create_time_ms?: number;
  session_id?: string;
  message_type?: number;
  message_state?: number;
  context_token?: string;
  item_list?: MessageItem[];
  [key: string]: unknown;
}

export interface GetUpdatesResponse {
  ret?: number;
  errcode?: number;
  errmsg?: string;
  msgs?: WeixinMessage[];
  get_updates_buf?: string;
  longpolling_timeout_ms?: number;
}

export interface SendMessageResponse {
  ret?: number;
  errcode?: number;
  errmsg?: string;
  [key: string]: unknown;
}

export interface PersistedState {
  version: 1;
  baseUrl: string;
  botToken: string;
  cursor?: string;
  loginConfirmedAt?: string;
  lastQrCodeId?: string;
  lastQrStatus?: string;
  lastPollAt?: string;
}

export interface LoginOptions {
  botType: number;
  timeoutSeconds: number;
  pollIntervalMs: number;
}

export interface MonitorOptions {
  once: boolean;
  sendReplies: boolean;
  replyPrefix: string;
}

export interface SendTextRequest {
  toUserId: string;
  contextToken: string;
  text: string;
}

export interface InboundMessageSummary {
  messageId: number | string | null;
  sessionId: string | null;
  fromUserId: string | null;
  toUserId: string | null;
  contextToken: string | null;
  textItems: string[];
  itemTypes: number[];
  quotedMessageIds: Array<string | number>;
  quotedContextTokens: string[];
}
