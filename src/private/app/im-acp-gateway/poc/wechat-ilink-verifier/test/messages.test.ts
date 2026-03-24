import test from 'node:test';
import assert from 'node:assert/strict';

import { extractTextContent, summarizeInboundMessage } from '../src/messages.ts';
import type { WeixinMessage } from '../src/types.ts';

test('summarizeInboundMessage captures reply metadata', () => {
  const message: WeixinMessage = {
    message_id: 12345,
    session_id: 'session-1',
    from_user_id: 'alice@im.wechat',
    to_user_id: 'bot@im.bot',
    context_token: 'ctx-current',
    item_list: [
      {
        type: 1,
        text_item: {
          text: 'hello',
        },
        ref_msg: {
          msg_id: 'quoted-1',
          context_token: 'ctx-quoted',
          item_list: [
            {
              type: 1,
              text_item: {
                text: 'older text',
              },
            },
          ],
        },
      },
    ],
  };

  const summary = summarizeInboundMessage(message);

  assert.deepEqual(summary, {
    messageId: 12345,
    sessionId: 'session-1',
    fromUserId: 'alice@im.wechat',
    toUserId: 'bot@im.bot',
    contextToken: 'ctx-current',
    textItems: ['hello', 'older text'],
    itemTypes: [1],
    quotedMessageIds: ['quoted-1'],
    quotedContextTokens: ['ctx-quoted'],
  });
});

test('extractTextContent ignores non-text items', () => {
  const message: WeixinMessage = {
    item_list: [
      {
        type: 2,
      },
      {
        type: 1,
        text_item: {
          text: 'plain text',
        },
      },
    ],
  };

  assert.deepEqual(extractTextContent(message), ['plain text']);
});
