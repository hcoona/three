import type {
  AnswerCallbackQueryRequest,
  EditMessageTextRequest,
  GetMeResponse,
  GetUpdatesOptions,
  SendChatActionRequest,
  SendMessageRequest,
  TelegramApiEnvelope,
  TelegramMessage,
  TelegramUpdate,
} from './types.ts';

const DEFAULT_API_BASE_URL = 'https://api.telegram.org';

export class TelegramBotClient {
  readonly apiBaseUrl: string;
  readonly botToken: string;

  constructor(options: { apiBaseUrl?: string | undefined; botToken: string }) {
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

  private async invoke<T>(method: string, payload: object): Promise<T> {
    const response = await fetch(this.buildUrl(method), {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(
        `${method} failed with HTTP ${response.status} ${response.statusText}`,
      );
    }

    const envelope = (await response.json()) as TelegramApiEnvelope<T>;

    if (!envelope.ok || envelope.result === undefined) {
      throw new Error(
        `${method} failed: ${String(envelope.error_code ?? '')} ${
          envelope.description ?? 'unknown Telegram API error'
        }`.trim(),
      );
    }

    return envelope.result;
  }

  private buildUrl(method: string): string {
    return `${this.apiBaseUrl}/bot${this.botToken}/${method}`;
  }
}
