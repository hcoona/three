import type { InboundMessageSummary, MessageItem, RefMessage, WeixinMessage } from './types.ts';

export function summarizeInboundMessage(message: WeixinMessage): InboundMessageSummary {
  const itemList = message.item_list ?? [];

  return {
    messageId: message.message_id ?? null,
    sessionId: message.session_id ?? null,
    fromUserId: message.from_user_id ?? null,
    toUserId: message.to_user_id ?? null,
    contextToken: message.context_token ?? null,
    textItems: itemList.flatMap(extractTextItems),
    itemTypes: itemList.map((item) => item.type),
    quotedMessageIds: itemList.flatMap((item) => {
      const refMessage = item.ref_msg;
      if (!refMessage) {
        return [];
      }

      const messageId = refMessage.msg_id ?? refMessage.message_id;
      return messageId === undefined ? [] : [messageId];
    }),
    quotedContextTokens: itemList.flatMap((item) => {
      const contextToken = item.ref_msg?.context_token;
      return contextToken ? [contextToken] : [];
    }),
  };
}

export function extractTextContent(message: WeixinMessage): string[] {
  return (message.item_list ?? []).flatMap(extractTextItems);
}

function extractTextItems(item: MessageItem): string[] {
  const items: string[] = [];

  const inlineText = item.text_item?.text;
  if (inlineText) {
    items.push(inlineText);
  }

  if (item.ref_msg) {
    items.push(...extractRefMessageText(item.ref_msg));
  }

  return items;
}

function extractRefMessageText(message: RefMessage): string[] {
  return (message.item_list ?? []).flatMap((item) => {
    const text = item.text_item?.text;
    return text ? [text] : [];
  });
}
