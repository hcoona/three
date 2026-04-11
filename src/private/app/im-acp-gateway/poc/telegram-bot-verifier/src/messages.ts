import type {
  MessageSummary,
  ParsedDemoCallback,
  TelegramCallbackQuery,
  TelegramInlineKeyboardMarkup,
  TelegramMessage,
  TelegramUpdate,
  UpdateSummary,
} from './types.ts';

export function summarizeMessage(message: TelegramMessage): MessageSummary {
  return {
    messageId: message.message_id,
    chatId: String(message.chat.id),
    chatType: message.chat.type,
    fromId: message.from?.id ?? null,
    text: message.text ?? null,
    replyToMessageId: message.reply_to_message?.message_id ?? null,
    messageThreadId: message.message_thread_id ?? null,
    isTopicMessage: message.is_topic_message ?? false,
  };
}

export function summarizeUpdate(update: TelegramUpdate): UpdateSummary {
  if (update.callback_query) {
    return summarizeCallbackQuery(update.update_id, update.callback_query);
  }

  const message = update.message ?? update.edited_message;
  if (message) {
    const summary = summarizeMessage(message);

    return {
      updateId: update.update_id,
      kind: update.edited_message ? 'edited_message' : 'message',
      chatId: summary.chatId,
      messageId: summary.messageId,
      fromId: summary.fromId,
      text: summary.text,
      callbackData: null,
      replyToMessageId: summary.replyToMessageId,
    };
  }

  return {
    updateId: update.update_id,
    kind: 'unknown',
    chatId: null,
    messageId: null,
    fromId: null,
    text: null,
    callbackData: null,
    replyToMessageId: null,
  };
}

export function createApprovalDemoMarkup(nonce: string): TelegramInlineKeyboardMarkup {
  return {
    inline_keyboard: [
      [
        {
          text: 'Approve',
          callback_data: `demo:approve:${nonce}`,
        },
        {
          text: 'Deny',
          callback_data: `demo:deny:${nonce}`,
        },
      ],
      [
        {
          text: 'Stop',
          callback_data: `demo:stop:${nonce}`,
        },
      ],
    ],
  };
}

export function parseDemoCallbackData(data: string | undefined): ParsedDemoCallback | null {
  if (!data) {
    return null;
  }

  const parts = data.split(':');
  if (parts.length !== 3 || parts[0] !== 'demo') {
    return null;
  }

  const action = parts[1];
  const nonce = parts[2];

  if (
    action === undefined ||
    nonce === undefined ||
    (action !== 'approve' && action !== 'deny' && action !== 'stop') ||
    nonce.length === 0
  ) {
    return null;
  }

  return {
    action,
    nonce,
  };
}

function summarizeCallbackQuery(updateId: number, callbackQuery: TelegramCallbackQuery): UpdateSummary {
  return {
    updateId,
    kind: 'callback_query',
    chatId: callbackQuery.message ? String(callbackQuery.message.chat.id) : null,
    messageId: callbackQuery.message?.message_id ?? null,
    fromId: callbackQuery.from.id,
    text: callbackQuery.message?.text ?? null,
    callbackData: callbackQuery.data ?? null,
    replyToMessageId: callbackQuery.message?.reply_to_message?.message_id ?? null,
  };
}
