import type {
  AcpPermissionOption,
  GeneralCommand,
  MessageSummary,
  ParsedApprovalCallback,
  SessionCommand,
  TelegramCallbackQuery,
  TelegramInlineKeyboardMarkup,
  TelegramMessage,
  TelegramUpdate,
  UpdateSummary,
} from './types.ts';

const TELEGRAM_OPTION_DASH_PATTERN = /^[\u2010-\u2015\u2212\uFF0D]+/u;
const TELEGRAM_MARKDOWN_SPECIAL_CHARACTERS = /(_|\*|\[|\]|\(|\)|~|`|>|#|\+|-|=|\||\{|\}|\.|!)/gu;
const TELEGRAM_FENCED_CODE_BLOCK_PATTERN = /```([A-Za-z0-9_+-]+)?\n([\s\S]*?)```/gu;
const TELEGRAM_INLINE_MARKDOWN_PATTERN =
  /(?<inlineCode>`[^`\n]+`)|(?<link>\[(?<label>[^\]]+)\]\((?<url>https?:\/\/[^\s)]+)\))|(?<bold>\*\*(?<boldText>[^*\n]+)\*\*)|(?<strike>~~(?<strikeText>[^~\n]+)~~)/gu;

interface InlineMarkdownMatchGroups {
  inlineCode?: string;
  link?: string;
  label?: string;
  url?: string;
  bold?: string;
  boldText?: string;
  strike?: string;
  strikeText?: string;
}

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
    forumTopicClosed: message.forum_topic_closed === true,
    forumTopicReopened: message.forum_topic_reopened === true,
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
      kind: determineMessageKind(update, message),
      chatId: summary.chatId,
      messageId: summary.messageId,
      fromId: summary.fromId,
      text: summary.text,
      callbackData: null,
      replyToMessageId: summary.replyToMessageId,
      messageThreadId: summary.messageThreadId,
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
    messageThreadId: null,
  };
}

export function createPermissionMarkup(
  approvalId: string,
  options: AcpPermissionOption[],
): TelegramInlineKeyboardMarkup {
  return {
    inline_keyboard: chunkButtons(
      options.map((option) => ({
        text: option.name,
        callback_data: `permission:${approvalId}:${option.optionId}`,
      })),
      2,
    ),
  };
}

export function parsePermissionCallbackData(data: string | undefined): ParsedApprovalCallback | null {
  if (!data) {
    return null;
  }

  const parts = data.split(':');
  if (parts.length !== 3 || parts[0] !== 'permission') {
    return null;
  }

  const approvalId = parts[1];
  const optionId = parts[2];
  if (!approvalId || !optionId) {
    return null;
  }

  return {
    approvalId,
    optionId,
  };
}

export function parseGeneralCommand(text: string | undefined): GeneralCommand | null {
  const normalized = text?.trim();
  if (!normalized) {
    return null;
  }

  const parts = normalized.split(/\s+/u);
  const head = normalizeCommandName(parts[0]);

  if (head === 'help') {
    return { command: 'help' };
  }

  if (head === 'list') {
    return { command: 'list' };
  }

  if (head === 'kill') {
    const target = parts[1];
    if (!target || target.length === 0) {
      return null;
    }

    return {
      command: 'kill',
      target,
    };
  }

  if (head !== 'new' && head !== 'takeover') {
    return null;
  }

  let acpSessionId: string | undefined;
  let workingDirectory: string | undefined;
  const promptTokens: string[] = [];

  for (let index = 1; index < parts.length; index += 1) {
    const part = parts[index];
    if (!part) {
      continue;
    }

    const normalizedOption = normalizeTelegramOptionToken(part);

    if (normalizedOption === '--cwd') {
      workingDirectory = parts[index + 1];
      index += 1;
      continue;
    }

    if (normalizedOption.startsWith('--cwd=')) {
      workingDirectory = normalizedOption.slice('--cwd='.length);
      continue;
    }

    if (normalizedOption === '--session-id') {
      acpSessionId = parts[index + 1];
      index += 1;
      continue;
    }

    if (normalizedOption.startsWith('--session-id=')) {
      acpSessionId = normalizedOption.slice('--session-id='.length);
      continue;
    }

    promptTokens.push(part);
  }

  if (!workingDirectory || workingDirectory.length === 0) {
    return null;
  }

  const prompt = promptTokens.length > 0 ? promptTokens.join(' ') : null;
  if (head === 'takeover') {
    if (!acpSessionId || acpSessionId.length === 0) {
      return null;
    }

    return {
      command: 'takeover',
      acpSessionId,
      workingDirectory,
      prompt,
    };
  }

  return {
    command: 'new',
    workingDirectory,
    prompt,
  };
}

export function renderTelegramMarkdownV2(text: string): {
  parse_mode: 'MarkdownV2';
  text: string;
} {
  return {
    parse_mode: 'MarkdownV2',
    text: renderMarkdownBlocks(text.replace(/\r\n/gu, '\n').trim()),
  };
}

export function parseSessionCommand(text: string | undefined): SessionCommand | null {
  const normalized = text?.trim();
  if (!normalized) {
    return null;
  }

  const parts = normalized.split(/\s+/u);
  const head = normalizeCommandName(parts[0]);

  if (head === 'stop') {
    return { command: 'stop' };
  }

  if (head === 'new') {
    return { command: 'new' };
  }

  if (head === 'resume') {
    return { command: 'resume' };
  }

  if (head === 'yolo' || head === 'allow-all') {
    const modeToken = parts[1]?.toLowerCase();
    return {
      command: 'yolo',
      mode: modeToken === 'off' ? 'disable' : modeToken === 'show' ? 'show' : 'enable',
    };
  }

  if (head === 'help') {
    return { command: 'help' };
  }

  if (head === 'approve' || head === 'deny') {
    return {
      command: head,
      approvalId: parts[1] ?? null,
    };
  }

  if (head === 'copilot') {
    const prompt = normalized.slice(parts[0]?.length ?? 0).trim();
    return {
      command: 'copilot',
      prompt: prompt.length > 0 ? prompt : null,
    };
  }

  return null;
}

function normalizeCommandName(token: string | undefined): string | null {
  if (!token || !token.startsWith('/')) {
    return null;
  }

  const withoutSlash = token.slice(1);
  const atIndex = withoutSlash.indexOf('@');
  return atIndex >= 0 ? withoutSlash.slice(0, atIndex) : withoutSlash;
}

function normalizeTelegramOptionToken(token: string): string {
  if (token.startsWith('--')) {
    return token;
  }

  if (!TELEGRAM_OPTION_DASH_PATTERN.test(token)) {
    return token;
  }

  return token.replace(TELEGRAM_OPTION_DASH_PATTERN, '--');
}

function renderMarkdownBlocks(text: string): string {
  let result = '';
  let lastIndex = 0;

  for (const match of text.matchAll(TELEGRAM_FENCED_CODE_BLOCK_PATTERN)) {
    const matchedText = match[0];
    if (matchedText === undefined || match.index === undefined) {
      continue;
    }

    result += renderMarkdownLines(text.slice(lastIndex, match.index));
    result += renderFencedCodeBlock(match[1], match[2] ?? '');
    lastIndex = match.index + matchedText.length;
  }

  result += renderMarkdownLines(text.slice(lastIndex));
  return result;
}

function renderMarkdownLines(text: string): string {
  return text
    .split('\n')
    .map((line) => renderMarkdownLine(line))
    .join('\n');
}

function renderMarkdownLine(line: string): string {
  const heading = line.match(/^#{1,6}\s+(.+)$/u);
  const headingText = heading?.[1];
  if (headingText) {
    return `*${escapeTelegramMarkdownV2(headingText)}*`;
  }

  return renderInlineMarkdown(line);
}

function renderInlineMarkdown(line: string): string {
  let result = '';
  let lastIndex = 0;

  for (const match of line.matchAll(TELEGRAM_INLINE_MARKDOWN_PATTERN)) {
    const matchedText = match[0];
    if (matchedText === undefined || match.index === undefined) {
      continue;
    }

    result += escapeTelegramMarkdownV2(line.slice(lastIndex, match.index));
    result += renderInlineToken(match);
    lastIndex = match.index + matchedText.length;
  }

  result += escapeTelegramMarkdownV2(line.slice(lastIndex));
  return result;
}

function renderInlineToken(match: RegExpMatchArray): string {
  const groups = match.groups as InlineMarkdownMatchGroups | undefined;
  const inlineCode = groups?.inlineCode;
  if (inlineCode) {
    const code = inlineCode.slice(1, -1);
    return `\`${escapeTelegramCode(code)}\``;
  }

  const link = groups?.link;
  const label = groups?.label;
  const url = groups?.url;
  if (link && label && url) {
    return `[${escapeTelegramMarkdownV2(label)}](${escapeTelegramUrl(url)})`;
  }

  const bold = groups?.bold;
  const boldText = groups?.boldText;
  if (bold && boldText) {
    return `*${escapeTelegramMarkdownV2(boldText)}*`;
  }

  const strike = groups?.strike;
  const strikeText = groups?.strikeText;
  if (strike && strikeText) {
    return `~${escapeTelegramMarkdownV2(strikeText)}~`;
  }

  return escapeTelegramMarkdownV2(match[0] ?? '');
}

function renderFencedCodeBlock(language: string | undefined, code: string): string {
  const languageSuffix = language && language.length > 0 ? language.replace(/[^A-Za-z0-9_+-]/gu, '') : '';
  const normalizedCode = code.replace(/\n$/u, '');

  return languageSuffix.length > 0
    ? `\`\`\`${languageSuffix}\n${escapeTelegramCode(normalizedCode)}\n\`\`\``
    : `\`\`\`\n${escapeTelegramCode(normalizedCode)}\n\`\`\``;
}

function escapeTelegramMarkdownV2(value: string): string {
  return value.replace(/\\/gu, '\\\\').replace(TELEGRAM_MARKDOWN_SPECIAL_CHARACTERS, '\\$1');
}

function escapeTelegramCode(value: string): string {
  return value.replace(/\\/gu, '\\\\').replace(/`/gu, '\\`');
}

function escapeTelegramUrl(value: string): string {
  return value.replace(/\\/gu, '\\\\').replace(/\)/gu, '\\)');
}

function chunkButtons<T>(items: T[], chunkSize: number): T[][] {
  const chunks: T[][] = [];
  for (let start = 0; start < items.length; start += chunkSize) {
    chunks.push(items.slice(start, start + chunkSize));
  }

  return chunks;
}

function determineMessageKind(update: TelegramUpdate, message: TelegramMessage): string {
  if (message.forum_topic_closed === true) {
    return 'forum_topic_closed';
  }

  if (message.forum_topic_reopened === true) {
    return 'forum_topic_reopened';
  }

  if (message.forum_topic_created) {
    return 'forum_topic_created';
  }

  return update.edited_message ? 'edited_message' : 'message';
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
    messageThreadId: callbackQuery.message?.message_thread_id ?? null,
  };
}
