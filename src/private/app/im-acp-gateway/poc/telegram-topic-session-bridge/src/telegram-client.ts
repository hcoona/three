import type {
  AnswerCallbackQueryRequest,
  CreateForumTopicRequest,
  EditMessageTextRequest,
  ForumTopic,
  GetMeResponse,
  GetUpdatesOptions,
  SendChatActionRequest,
  SendMessageRequest,
  TelegramApiEnvelope,
  TelegramMessage,
  TelegramUpdate,
} from './types.ts';

const DEFAULT_API_BASE_URL = 'https://api.telegram.org';
const MAX_RATE_LIMIT_RETRIES = 3;

export class TelegramBotClient {
  readonly apiBaseUrl: string;
  readonly botToken: string;

  constructor(options: { apiBaseUrl?: string; botToken: string }) {
    this.apiBaseUrl =
      options.apiBaseUrl?.trim().replace(/\/+$/, '') ?? DEFAULT_API_BASE_URL;
    this.botToken = options.botToken;
  }

  async getMe(): Promise<GetMeResponse> {
    return this.invoke<GetMeResponse>('getMe', {});
  }

  async getUpdates(options: GetUpdatesOptions): Promise<TelegramUpdate[]> {
    return this.invoke<TelegramUpdate[]>('getUpdates', options);
  }

  async sendMessage(request: SendMessageRequest): Promise<TelegramMessage> {
    return this.invoke<TelegramMessage>('sendMessage', request);
  }

  async editMessageText(
    request: EditMessageTextRequest,
  ): Promise<TelegramMessage | true> {
    return this.invoke<TelegramMessage | true>('editMessageText', request);
  }

  async sendChatAction(request: SendChatActionRequest): Promise<true> {
    return this.invoke<true>('sendChatAction', request);
  }

  async answerCallbackQuery(
    request: AnswerCallbackQueryRequest,
  ): Promise<true> {
    return this.invoke<true>('answerCallbackQuery', request);
  }

  async createForumTopic(request: CreateForumTopicRequest): Promise<ForumTopic> {
    return this.invoke<ForumTopic>('createForumTopic', request);
  }

  private async invoke<T>(method: string, payload: object): Promise<T> {
    for (let attempt = 0; ; attempt += 1) {
      const response = await fetch(this.buildUrl(method), {
        method: 'POST',
        headers: {
          'content-type': 'application/json',
        },
        body: JSON.stringify(payload),
      });

      const envelope = await tryParseEnvelope<T>(response);

      if (!response.ok) {
        const retryAfterSeconds = resolveRetryAfterSeconds(response.status, envelope);
        if (retryAfterSeconds !== null && attempt < MAX_RATE_LIMIT_RETRIES) {
          await delay((retryAfterSeconds + 1) * 1_000);
          continue;
        }

        throw new Error(
          formatHttpFailureMessage(method, response.status, response.statusText, envelope),
        );
      }

      if (!envelope || !envelope.ok || envelope.result === undefined) {
        throw new Error(
          `${method} failed: ${String(envelope?.error_code ?? '')} ${
            envelope?.description ?? 'unknown Telegram API error'
          }`.trim(),
        );
      }

      return envelope.result;
    }
  }

  private buildUrl(method: string): string {
    return `${this.apiBaseUrl}/bot${this.botToken}/${method}`;
  }
}

async function tryParseEnvelope<T>(
  response: Response,
): Promise<TelegramApiEnvelope<T> | null> {
  try {
    return (await response.json()) as TelegramApiEnvelope<T>;
  } catch {
    return null;
  }
}

function resolveRetryAfterSeconds(
  status: number,
  envelope: TelegramApiEnvelope<unknown> | null,
): number | null {
  if (status !== 429) {
    return null;
  }

  return envelope?.parameters?.retry_after ?? 1;
}

function formatHttpFailureMessage(
  method: string,
  status: number,
  statusText: string,
  envelope: TelegramApiEnvelope<unknown> | null,
): string {
  const base = `${method} failed with HTTP ${status} ${statusText}`;
  const description = envelope?.description?.trim();
  return description ? `${base}: ${description}` : base;
}

async function delay(milliseconds: number): Promise<void> {
  await new Promise<void>((resolve) => {
    setTimeout(resolve, milliseconds);
  });
}
