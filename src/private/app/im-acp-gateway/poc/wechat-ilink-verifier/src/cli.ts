#!/usr/bin/env node

import process from 'node:process';

import { runLoginCommand, runMonitorCommand, runSendCommand, runShowStateCommand } from './commands.ts';
import type { LoginOptions, MonitorOptions } from './types.ts';

const DEFAULT_LOGIN_OPTIONS: LoginOptions = {
  botType: 3,
  timeoutSeconds: 180,
  pollIntervalMs: 1_000,
};

const DEFAULT_MONITOR_OPTIONS: MonitorOptions = {
  once: false,
  sendReplies: true,
  replyPrefix: 'Echo: ',
};

interface CliArgs {
  'base-url'?: string;
  'bot-type'?: string;
  'context-token'?: string;
  force?: string;
  'no-reply'?: string;
  once?: string;
  'poll-interval-ms'?: string;
  'reply-prefix'?: string;
  'state-dir'?: string;
  text?: string;
  'timeout-seconds'?: string;
  'to-user-id'?: string;
  [key: string]: string | undefined;
}

async function main(): Promise<void> {
  const [command, ...restArguments] = process.argv.slice(2);

  switch (command) {
    case 'login': {
      const args = parseArgs(restArguments);
      const loginCommandOptions: {
        baseUrl?: string;
        force: boolean;
        stateDirectory?: string;
        login: LoginOptions;
      } = {
        force: args.force === 'true',
        login: {
          botType: parseIntegerArg(args['bot-type'], DEFAULT_LOGIN_OPTIONS.botType, 'bot-type'),
          timeoutSeconds: parseIntegerArg(
            args['timeout-seconds'],
            DEFAULT_LOGIN_OPTIONS.timeoutSeconds,
            'timeout-seconds',
          ),
          pollIntervalMs: parseIntegerArg(
            args['poll-interval-ms'],
            DEFAULT_LOGIN_OPTIONS.pollIntervalMs,
            'poll-interval-ms',
          ),
        },
      };

      if (args['base-url']) {
        loginCommandOptions.baseUrl = args['base-url'];
      }

      if (args['state-dir']) {
        loginCommandOptions.stateDirectory = args['state-dir'];
      }

      await runLoginCommand(loginCommandOptions);
      break;
    }

    case 'monitor': {
      const args = parseArgs(restArguments);
      const monitorCommandOptions: {
        stateDirectory?: string;
        monitor: MonitorOptions;
      } = {
        monitor: {
          once: args.once === 'true',
          sendReplies: args['no-reply'] !== 'true',
          replyPrefix: args['reply-prefix'] ?? DEFAULT_MONITOR_OPTIONS.replyPrefix,
        },
      };

      if (args['state-dir']) {
        monitorCommandOptions.stateDirectory = args['state-dir'];
      }

      await runMonitorCommand(monitorCommandOptions);
      break;
    }

    case 'send': {
      const args = parseArgs(restArguments);
      const toUserId = requireStringArg(args['to-user-id'], 'to-user-id');
      const contextToken = requireStringArg(args['context-token'], 'context-token');
      const text = requireStringArg(args.text, 'text');

      const sendCommandOptions: {
        stateDirectory?: string;
        request: { toUserId: string; contextToken: string; text: string };
      } = {
        request: {
          toUserId,
          contextToken,
          text,
        },
      };

      if (args['state-dir']) {
        sendCommandOptions.stateDirectory = args['state-dir'];
      }

      await runSendCommand(sendCommandOptions);
      break;
    }

    case 'show-state': {
      const args = parseArgs(restArguments);
      if (args['state-dir']) {
        await runShowStateCommand({
          stateDirectory: args['state-dir'],
        });
      } else {
        await runShowStateCommand({});
      }
      break;
    }

    case 'help':
    case '--help':
    case '-h':
    case undefined: {
      printHelp();
      break;
    }

    default:
      throw new Error(`Unknown command "${command}". Run with "help" to see the available commands.`);
  }
}

function parseArgs(argumentsList: string[]): CliArgs {
  const parsed: CliArgs = {};

  for (let index = 0; index < argumentsList.length; index += 1) {
    const current = argumentsList[index];

    if (!current?.startsWith('--')) {
      throw new Error(`Unexpected argument "${current}". All arguments must use --name or --name value.`);
    }

    const normalized = current.slice(2);

    if (normalized.includes('=')) {
      const separatorIndex = normalized.indexOf('=');
      const key = normalized.slice(0, separatorIndex);
      const value = normalized.slice(separatorIndex + 1);
      parsed[key] = value;
      continue;
    }

    const nextValue = argumentsList[index + 1];

    if (!nextValue || nextValue.startsWith('--')) {
      parsed[normalized] = 'true';
      continue;
    }

    parsed[normalized] = nextValue;
    index += 1;
  }

  return parsed;
}

function parseIntegerArg(value: string | undefined, defaultValue: number, name: string): number {
  if (value === undefined) {
    return defaultValue;
  }

  const parsed = Number.parseInt(value, 10);

  if (Number.isNaN(parsed) || parsed <= 0) {
    throw new Error(`--${name} must be a positive integer.`);
  }

  return parsed;
}

function requireStringArg(value: string | undefined, name: string): string {
  if (!value || value.trim().length === 0) {
    throw new Error(`--${name} is required.`);
  }

  return value;
}

function printHelp(): void {
  const lines = [
    'wechat-ilink-verifier',
    '',
    'POC CLI for validating the Personal WeChat iLink path.',
    '',
    'Commands:',
    '  login [--base-url URL] [--bot-type 3] [--timeout-seconds 180] [--poll-interval-ms 1000] [--state-dir PATH] [--force]',
    '  monitor [--state-dir PATH] [--reply-prefix "Echo: "] [--no-reply] [--once]',
    '  send --to-user-id USER --context-token TOKEN --text TEXT [--state-dir PATH]',
    '  show-state [--state-dir PATH]',
    '',
    'Notes:',
    '  - Login persists bot_token and cursor state under ~/.local/share/im-acp-gateway/wechat-ilink-verifier by default.',
    '  - The monitor command logs raw inbound payloads so you can inspect context_token, message_id, session_id, and quoted-message metadata.',
    '  - Auto-reply is enabled by default during monitor; use --no-reply to inspect only.',
  ];

  process.stdout.write(`${lines.join('\n')}\n`);
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`Error: ${message}`);
  process.exitCode = 1;
});
