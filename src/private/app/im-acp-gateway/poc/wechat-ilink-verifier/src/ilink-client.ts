import crypto from 'node:crypto';

import type {
  BotQrCodeResponse,
  GetUpdatesResponse,
  QrCodeStatusResponse,
  SendMessageResponse,
  SendTextRequest,
} from './types.ts';

const DEFAULT_BASE_URL = 'https://ilinkai.weixin.qq.com';

export class ILinkClient {
  readonly #baseUrl: string;
  readonly #botToken: string | null;

  constructor(options?: { baseUrl?: string; botToken?: string }) {
    this.#baseUrl = normalizeBaseUrl(options?.baseUrl ?? DEFAULT_BASE_URL);
    this.#botToken = options?.botToken ?? null;
  }

  get baseUrl(): string {
    return this.#baseUrl;
  }

  async getBotQrCode(botType: number): Promise<BotQrCodeResponse> {
    const url = new URL('/ilink/bot/get_bot_qrcode', this.#baseUrl);
    url.searchParams.set('bot_type', String(botType));
    return this.#getJson<BotQrCodeResponse>(url, false);
  }

  async getQrCodeStatus(qrCodeId: string): Promise<QrCodeStatusResponse> {
    const url = new URL('/ilink/bot/get_qrcode_status', this.#baseUrl);
    url.searchParams.set('qrcode', qrCodeId);
    return this.#getJson<QrCodeStatusResponse>(url, false);
  }

  async getUpdates(cursor: string): Promise<GetUpdatesResponse> {
    return this.#postJson<GetUpdatesResponse>('/ilink/bot/getupdates', {
      get_updates_buf: cursor,
      base_info: {
        channel_version: '1.0.2',
      },
    });
  }

  async sendTextMessage(request: SendTextRequest): Promise<SendMessageResponse> {
    return this.#postJson<SendMessageResponse>('/ilink/bot/sendmessage', {
      msg: {
        to_user_id: request.toUserId,
        message_type: 2,
        message_state: 2,
        context_token: request.contextToken,
        item_list: [
          {
            type: 1,
            text_item: {
              text: request.text,
            },
          },
        ],
      },
    });
  }

  async #getJson<T>(url: URL, requiresAuth: boolean): Promise<T> {
    const response = await fetch(url, {
      method: 'GET',
      headers: this.#createHeaders(requiresAuth),
    });

    return parseJsonResponse<T>(response);
  }

  async #postJson<T>(pathname: string, body: unknown): Promise<T> {
    const url = new URL(pathname, this.#baseUrl);
    const response = await fetch(url, {
      method: 'POST',
      headers: this.#createHeaders(true),
      body: JSON.stringify(body),
    });

    return parseJsonResponse<T>(response);
  }

  #createHeaders(requiresAuth: boolean): Headers {
    const headers = new Headers({
      'Content-Type': 'application/json',
      AuthorizationType: 'ilink_bot_token',
      'X-WECHAT-UIN': createWechatUinHeader(),
    });

    if (requiresAuth) {
      if (!this.#botToken) {
        throw new Error('This operation requires a bot token.');
      }

      headers.set('Authorization', `Bearer ${this.#botToken}`);
    }

    return headers;
  }
}

export function createWechatUinHeader(): string {
  const randomValue = crypto.randomInt(0, 2 ** 32);
  return Buffer.from(String(randomValue), 'utf8').toString('base64');
}

function normalizeBaseUrl(value: string): string {
  return value.endsWith('/') ? value.slice(0, -1) : value;
}

async function parseJsonResponse<T>(response: Response): Promise<T> {
  const rawBody = await response.text();
  const parsedBody = rawBody.length > 0 ? (JSON.parse(rawBody) as T) : ({} as T);

  if (!response.ok) {
    throw new Error(
      `HTTP ${response.status} ${response.statusText}: ${rawBody || '<empty response body>'}`,
    );
  }

  return parsedBody;
}
