import { spawn, type ChildProcessByStdio } from 'node:child_process';
import readline from 'node:readline';
import type { Readable, Writable } from 'node:stream';

import type {
  AcpInitializeResult,
  AcpPermissionRequestParams,
  AcpPermissionResponse,
  AcpPromptResult,
  AcpSessionResult,
} from './types.ts';

type JsonRpcId = number;

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (error: Error) => void;
}

type SessionUpdateHandler = (
  sessionId: string,
  update: unknown,
) => void | Promise<void>;

type PermissionHandler = (
  params: AcpPermissionRequestParams,
) => Promise<AcpPermissionResponse>;

export class CopilotAcpClient {
  readonly process: ChildProcessByStdio<Writable, Readable, null>;

  private readonly lineReader: readline.Interface;
  private readonly pendingRequests = new Map<JsonRpcId, PendingRequest>();
  private nextId = 0;
  private sessionUpdateHandler: SessionUpdateHandler | undefined;
  private permissionHandler: PermissionHandler | undefined;

  private constructor(process: ChildProcessByStdio<Writable, Readable, null>) {
    this.process = process;
    this.lineReader = readline.createInterface({
      input: process.stdout,
      crlfDelay: Infinity,
    });

    this.lineReader.on('line', (line) => {
      void this.handleLine(line);
    });

    process.once('exit', (code, signal) => {
      this.rejectAllPending(
        new Error(
          `Copilot ACP process exited before all requests completed (code=${String(code)}, signal=${String(signal)}).`,
        ),
      );
    });
  }

  static async start(options: {
    copilotPath: string;
    cwd: string;
    model?: string;
    onSessionUpdate?: SessionUpdateHandler;
    onPermissionRequest?: PermissionHandler;
  }): Promise<CopilotAcpClient> {
    const argumentsList = [
      '--acp',
      '--stdio',
      '--add-dir',
      options.cwd,
    ];

    if (options.model) {
      argumentsList.push('--model', options.model);
    }

    const process = spawn(options.copilotPath, argumentsList, {
      cwd: options.cwd,
      stdio: ['pipe', 'pipe', 'inherit'],
    });

    if (!process.stdin || !process.stdout) {
      throw new Error('Failed to start Copilot ACP process with piped stdio.');
    }

    const client = new CopilotAcpClient(process);
    if (options.onSessionUpdate) {
      client.sessionUpdateHandler = options.onSessionUpdate;
    }

    if (options.onPermissionRequest) {
      client.permissionHandler = options.onPermissionRequest;
    }

    await client.initialize();
    return client;
  }

  async initialize(): Promise<AcpInitializeResult> {
    return (await this.request('initialize', {
      protocolVersion: 1,
      clientCapabilities: {},
      clientInfo: {
        name: 'telegram-bot-verifier',
        title: 'Telegram Bot Verifier',
        version: '0.0.0',
      },
    })) as AcpInitializeResult;
  }

  async newSession(cwd: string): Promise<AcpSessionResult> {
    return (await this.request('session/new', {
      cwd,
      mcpServers: [],
    })) as AcpSessionResult;
  }

  async prompt(sessionId: string, text: string): Promise<AcpPromptResult> {
    return (await this.request('session/prompt', {
      sessionId,
      prompt: [
        {
          type: 'text',
          text,
        },
      ],
    })) as AcpPromptResult;
  }

  async cancel(sessionId: string): Promise<void> {
    await this.notify('session/cancel', { sessionId });
  }

  async close(): Promise<void> {
    this.lineReader.close();
    this.process.stdin.end();
    this.process.kill('SIGTERM');
    await new Promise<void>((resolve) => {
      const timeout = setTimeout(() => {
        resolve();
      }, 2_000);
      this.process.once('exit', () => {
        clearTimeout(timeout);
        resolve();
      });
    });
  }

  private async request(method: string, params: object): Promise<unknown> {
    const id = this.nextId;
    this.nextId += 1;

    const promise = new Promise<unknown>((resolve, reject) => {
      this.pendingRequests.set(id, {
        resolve,
        reject,
      });
    });

    await this.writeMessage({
      jsonrpc: '2.0',
      id,
      method,
      params,
    });

    return promise;
  }

  private async notify(method: string, params: object): Promise<void> {
    await this.writeMessage({
      jsonrpc: '2.0',
      method,
      params,
    });
  }

  private async respond(id: JsonRpcId, result: unknown): Promise<void> {
    await this.writeMessage({
      jsonrpc: '2.0',
      id,
      result,
    });
  }

  private async writeMessage(message: object): Promise<void> {
    const payload = `${JSON.stringify(message)}\n`;
    await new Promise<void>((resolve, reject) => {
      this.process.stdin.write(payload, (error) => {
        if (error) {
          reject(error);
          return;
        }

        resolve();
      });
    });
  }

  private async handleLine(line: string): Promise<void> {
    if (line.trim().length === 0) {
      return;
    }

    const message = JSON.parse(line) as {
      id?: number;
      method?: string;
      params?: Record<string, unknown>;
      result?: unknown;
      error?: { code?: number; message?: string };
    };

    if (message.method) {
      const incomingMethodMessage = {
        method: message.method,
        ...(message.id === undefined ? {} : { id: message.id }),
        ...(message.params === undefined ? {} : { params: message.params }),
      };

      await this.handleIncomingMethod(incomingMethodMessage);
      return;
    }

    if (message.id === undefined) {
      return;
    }

    const pending = this.pendingRequests.get(message.id);
    if (!pending) {
      return;
    }

    this.pendingRequests.delete(message.id);

    if (message.error) {
      pending.reject(
        new Error(
          `ACP request failed: ${message.error.message ?? 'unknown error'} (${String(message.error.code ?? 'no-code')})`,
        ),
      );
      return;
    }

    pending.resolve(message.result);
  }

  private async handleIncomingMethod(message: {
    id?: number;
    method: string;
    params?: Record<string, unknown>;
  }): Promise<void> {
    const params = message.params as
      | { sessionId?: unknown; update?: unknown }
      | undefined;

    if (message.method === 'session/update') {
      const sessionId = asString(params?.sessionId);
      if (sessionId && this.sessionUpdateHandler) {
        await this.sessionUpdateHandler(sessionId, params?.update);
      }
      return;
    }

    if (
      message.method === 'session/request_permission' &&
      message.id !== undefined
    ) {
      const fallback: AcpPermissionResponse = {
        outcome: {
          outcome: 'cancelled',
        },
      };

      const response = this.permissionHandler
        ? await this.permissionHandler(
            (message.params ?? {}) as AcpPermissionRequestParams,
          )
        : fallback;

      await this.respond(message.id, response);
    }
  }

  private rejectAllPending(error: Error): void {
    for (const pending of this.pendingRequests.values()) {
      pending.reject(error);
    }

    this.pendingRequests.clear();
  }
}

function asString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}
