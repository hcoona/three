import assert from 'node:assert/strict';
import test from 'node:test';

import {
  createPermissionMarkup,
  parseGeneralCommand,
  parsePermissionCallbackData,
  parseSessionCommand,
  renderTelegramMarkdownV2,
  summarizeMessage,
  summarizeUpdate,
} from '../src/messages.ts';
import type { TelegramMessage, TelegramUpdate } from '../src/types.ts';

test('summarizeMessage extracts topic correlation fields', () => {
  const message: TelegramMessage = {
    message_id: 10,
    date: 1_742_790_000,
    chat: {
      id: -100123456,
      type: 'supergroup',
      is_forum: true,
    },
    from: {
      id: 999,
      is_bot: false,
      first_name: 'Tester',
    },
    text: 'hello',
    message_thread_id: 22,
    is_topic_message: true,
    reply_to_message: {
      message_id: 9,
      chat: {
        id: -100123456,
        type: 'supergroup',
      },
      text: 'bot message',
    },
  };

  assert.deepStrictEqual(summarizeMessage(message), {
    messageId: 10,
    chatId: '-100123456',
    chatType: 'supergroup',
    fromId: 999,
    text: 'hello',
    replyToMessageId: 9,
    messageThreadId: 22,
    isTopicMessage: true,
    forumTopicClosed: false,
    forumTopicReopened: false,
  });
});

test('summarizeUpdate recognizes topic close events', () => {
  const update: TelegramUpdate = {
    update_id: 55,
    message: {
      message_id: 10,
      date: 1_742_790_000,
      chat: {
        id: -100123456,
        type: 'supergroup',
      },
      message_thread_id: 77,
      forum_topic_closed: true,
    },
  };

  assert.deepStrictEqual(summarizeUpdate(update), {
    updateId: 55,
    kind: 'forum_topic_closed',
    chatId: '-100123456',
    messageId: 10,
    fromId: null,
    text: null,
    callbackData: null,
    replyToMessageId: null,
    messageThreadId: 77,
  });
});

test('permission markup and callback parsing use approval ids', () => {
  const markup = createPermissionMarkup('abc123', [
    {
      optionId: 'allow_once',
      kind: 'allow_once',
      name: 'Allow once',
    },
    {
      optionId: 'reject_once',
      kind: 'reject_once',
      name: 'Reject once',
    },
  ]);
  assert.deepStrictEqual(markup, {
    inline_keyboard: [
      [
        { text: 'Allow once', callback_data: 'permission:abc123:allow_once' },
        { text: 'Reject once', callback_data: 'permission:abc123:reject_once' },
      ],
    ],
  });

  assert.deepStrictEqual(parsePermissionCallbackData('permission:abc123:allow_once'), {
    approvalId: 'abc123',
    optionId: 'allow_once',
  });
  assert.equal(parsePermissionCallbackData('demo:approve:abc123'), null);
});

test('general and session commands are parsed from Telegram text', () => {
  assert.deepStrictEqual(
    parseGeneralCommand('/new --cwd /workspace/repo summarize current repo'),
    {
      command: 'new',
      workingDirectory: '/workspace/repo',
      prompt: 'summarize current repo',
    },
  );
  assert.deepStrictEqual(parseGeneralCommand('/list'), { command: 'list' });
  assert.deepStrictEqual(parseGeneralCommand('/kill session-123'), {
    command: 'kill',
    target: 'session-123',
  });
  assert.deepStrictEqual(
    parseGeneralCommand(
      '/takeover --session-id acp-123 --cwd /workspace/repo continue prior work',
    ),
    {
      command: 'takeover',
      acpSessionId: 'acp-123',
      workingDirectory: '/workspace/repo',
      prompt: 'continue prior work',
    },
  );
  assert.equal(parseGeneralCommand('/new summarize without cwd'), null);
  assert.deepStrictEqual(
    parseGeneralCommand('/new —cwd /workspace/repo summarize — current repo'),
    {
      command: 'new',
      workingDirectory: '/workspace/repo',
      prompt: 'summarize — current repo',
    },
  );

  assert.deepStrictEqual(parseSessionCommand('/stop'), { command: 'stop' });
  assert.deepStrictEqual(parseSessionCommand('/new'), { command: 'new' });
  assert.deepStrictEqual(parseSessionCommand('/resume last'), { command: 'resume' });
  assert.deepStrictEqual(parseSessionCommand('/yolo'), {
    command: 'yolo',
    mode: 'enable',
  });
  assert.deepStrictEqual(parseSessionCommand('/allow-all off'), {
    command: 'yolo',
    mode: 'disable',
  });
  assert.deepStrictEqual(parseSessionCommand('/yolo show'), {
    command: 'yolo',
    mode: 'show',
  });
  assert.deepStrictEqual(parseSessionCommand('/help'), { command: 'help' });
  assert.deepStrictEqual(parseSessionCommand('/approve approval-1'), {
    command: 'approve',
    approvalId: 'approval-1',
  });
  assert.deepStrictEqual(parseSessionCommand('/deny'), {
    command: 'deny',
    approvalId: null,
  });
  assert.deepStrictEqual(parseSessionCommand('/copilot /yolo inspect this repo'), {
    command: 'copilot',
    prompt: '/yolo inspect this repo',
  });
});

test('renderTelegramMarkdownV2 keeps basic markdown readable and Telegram-safe', () => {
  assert.deepStrictEqual(
    renderTelegramMarkdownV2(
      [
        '# Summary',
        '- **bold** item with `code`',
        'See [docs](https://example.com/docs).',
        '```ts',
        'console.log("hello");',
        '```',
      ].join('\n'),
    ),
    {
      parse_mode: 'MarkdownV2',
      text: [
        '*Summary*',
        '\\- *bold* item with `code`',
        'See [docs](https://example.com/docs)\\.',
        '```ts',
        'console.log("hello");',
        '```',
      ].join('\n'),
    },
  );
});
