import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createApprovalDemoMarkup,
  parseDemoCallbackData,
  summarizeMessage,
  summarizeUpdate,
} from '../src/messages.ts';
import type { TelegramMessage, TelegramUpdate } from '../src/types.ts';

test('summarizeMessage extracts reply-correlation fields', () => {
  const message: TelegramMessage = {
    message_id: 10,
    date: 1_742_790_000,
    chat: {
      id: 123456,
      type: 'private',
    },
    from: {
      id: 999,
      is_bot: false,
      first_name: 'Tester',
    },
    text: 'hello',
    reply_to_message: {
      message_id: 9,
      chat: {
        id: 123456,
        type: 'private',
      },
      text: 'bot message',
    },
  };

  assert.deepStrictEqual(summarizeMessage(message), {
    messageId: 10,
    chatId: '123456',
    chatType: 'private',
    fromId: 999,
    text: 'hello',
    replyToMessageId: 9,
    messageThreadId: null,
    isTopicMessage: false,
  });
});

test('summarizeUpdate extracts callback query fields', () => {
  const update: TelegramUpdate = {
    update_id: 55,
    callback_query: {
      id: 'callback-1',
      from: {
        id: 999,
        is_bot: false,
        first_name: 'Tester',
      },
      data: 'demo:approve:nonce',
      message: {
        message_id: 10,
        date: 1_742_790_000,
        chat: {
          id: 123456,
          type: 'private',
        },
        text: 'Choose an action',
      },
    },
  };

  assert.deepStrictEqual(summarizeUpdate(update), {
    updateId: 55,
    kind: 'callback_query',
    chatId: '123456',
    messageId: 10,
    fromId: 999,
    text: 'Choose an action',
    callbackData: 'demo:approve:nonce',
    replyToMessageId: null,
  });
});

test('approval demo markup contains the expected callback actions', () => {
  const markup = createApprovalDemoMarkup('nonce');

  assert.deepStrictEqual(markup, {
    inline_keyboard: [
      [
        { text: 'Approve', callback_data: 'demo:approve:nonce' },
        { text: 'Deny', callback_data: 'demo:deny:nonce' },
      ],
      [{ text: 'Stop', callback_data: 'demo:stop:nonce' }],
    ],
  });
});

test('parseDemoCallbackData recognizes valid demo callbacks only', () => {
  assert.deepStrictEqual(parseDemoCallbackData('demo:approve:abc123'), {
    action: 'approve',
    nonce: 'abc123',
  });
  assert.equal(parseDemoCallbackData('demo:unknown:abc123'), null);
  assert.equal(parseDemoCallbackData('other:approve:abc123'), null);
});
