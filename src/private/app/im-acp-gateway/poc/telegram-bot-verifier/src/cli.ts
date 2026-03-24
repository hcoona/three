#!/usr/bin/env node

import process from 'node:process';

import {
  runApprovalDemoCommand,
  runEditCommand,
  runMonitorCommand,
  runSendChatActionCommand,
  runSendCommand,
  runSetupCommand,
  runShowStateCommand,
} from './commands.ts';
import type { MonitorOptions } from './types.ts';

const DEFAULT_MONITOR_OPTIONS: MonitorOptions = {
  once: false,
  sendReplies: true,
  replyPrefix: 'Echo: ',
  answerCallbacks: true,
  timeoutSeconds: 30,
};

interface CliArgs {
  action?: string;
  'api-base-url'?: string;
  'bot-token'?: string;
  'chat-id'?: string;
  force?: string;
  'message-id'?: string;
  'no-answer-callbacks'?: string;
  'no-reply'?: string;
  once?: string;
  'reply-prefix'?: string;
  'state-dir'?: string;
  text?: string;
  'timeout-seconds'?: string;
  [key: string]: string | undefined;
}

async function main(): Promise<void> {
  const normalizedArguments =
    process.argv[2] === '--' ? process.argv.slice(3) : process.argv.slice(2);
  const [command, ...restArguments] = normalizedArguments;

  switch (command) {
    case 'setup': {
      const args = parseArgs(restArguments);
      await runSetupCommand({
        force: args.force === 'true',
        setup: {
          apiBaseUrl: args['api-base-url'],
          botToken: requireStringArg(args['bot-token'], 'bot-token'),
          chatId: args['chat-id'],
        },
        stateDirectory: args['state-dir'],
      });
      break;
    }

    case 'monitor': {
      const args = parseArgs(restArguments);
      await runMonitorCommand({
        stateDirectory: args['state-dir'],
        monitor: {
          once: args.once === 'true',
          sendReplies: args['no-reply'] !== 'true',
          replyPrefix:
            args['reply-prefix'] ?? DEFAULT_MONITOR_OPTIONS.replyPrefix,
          answerCallbacks:
            args['no-answer-callbacks'] !== 'true',
          timeoutSeconds: parseIntegerArg(
            args['timeout-seconds'],
            DEFAULT_MONITOR_OPTIONS.timeoutSeconds,
            'timeout-seconds',
          ),
        },
      });
      break;
    }

    case 'send': {
      const args = parseArgs(restArguments);
      await runSendCommand({
        chatId: args['chat-id'],
        replyToMessageId: parseOptionalIntegerArg(
          args['message-id'],
          'message-id',
        ),
        stateDirectory: args['state-dir'],
        text: requireStringArg(args.text, 'text'),
      });
      break;
    }

    case 'edit': {
      const args = parseArgs(restArguments);
      await runEditCommand({
        chatId: args['chat-id'],
        messageId: requireIntegerArg(args['message-id'], 'message-id'),
        stateDirectory: args['state-dir'],
        text: requireStringArg(args.text, 'text'),
      });
      break;
    }

    case 'send-action': {
      const args = parseArgs(restArguments);
      await runSendChatActionCommand({
        action: requireStringArg(args.action, 'action'),
        chatId: args['chat-id'],
        stateDirectory: args['state-dir'],
      });
      break;
    }

    case 'approval-demo': {
      const args = parseArgs(restArguments);
      await runApprovalDemoCommand({
        chatId: args['chat-id'],
        stateDirectory: args['state-dir'],
        text:
          args.text ??
          'Telegram approval demo. Use the buttons below to validate callback handling.',
      });
      break;
    }

    case 'show-state': {
      const args = parseArgs(restArguments);
      await runShowStateCommand({
        stateDirectory: args['state-dir'],
      });
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
      throw new Error(
        `Unknown command "${command}". Run with "help" to see the available commands.`,
      );
  }
}

function parseArgs(argumentsList: string[]): CliArgs {
  const parsed: CliArgs = {};

  for (let index = 0; index < argumentsList.length; index += 1) {
    const current = argumentsList[index];

    if (!current?.startsWith('--')) {
      throw new Error(
        `Unexpected argument "${current}". All arguments must use --name or --name value.`,
      );
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

function parseIntegerArg(
  value: string | undefined,
  defaultValue: number,
  name: string,
): number {
  if (value === undefined) {
    return defaultValue;
  }

  return requireIntegerArg(value, name);
}

function parseOptionalIntegerArg(
  value: string | undefined,
  name: string,
): number | undefined {
  if (value === undefined) {
    return undefined;
  }

  return requireIntegerArg(value, name);
}

function requireIntegerArg(value: string | undefined, name: string): number {
  if (!value || value.trim().length === 0) {
    throw new Error(`--${name} is required.`);
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
    'telegram-bot-verifier',
    '',
    'POC CLI for validating the Telegram bot path.',
    '',
    'Commands:',
    '  setup --bot-token TOKEN [--chat-id ID] [--api-base-url URL] [--state-dir PATH] [--force]',
    '  monitor [--state-dir PATH] [--reply-prefix "Echo: "] [--no-reply] [--no-answer-callbacks] [--timeout-seconds 30] [--once]',
    '  send --text TEXT [--chat-id ID] [--message-id REPLY_TO] [--state-dir PATH]',
    '  edit --message-id ID --text TEXT [--chat-id ID] [--state-dir PATH]',
    '  send-action --action typing [--chat-id ID] [--state-dir PATH]',
    '  approval-demo [--chat-id ID] [--text TEXT] [--state-dir PATH]',
    '  show-state [--state-dir PATH]',
    '',
    'Notes:',
    '  - The setup command validates the token via getMe and persists it locally.',
    '  - The monitor command logs raw updates so you can inspect reply metadata, callback_query payloads, and topic fields.',
    '  - Auto-reply is enabled by default during monitor; use --no-reply to inspect only.',
    '  - If no --chat-id is passed to send/edit/send-action/approval-demo, the CLI falls back to the saved default chat id or the most recently observed chat id.',
  ];

  process.stdout.write(`${lines.join('\n')}\n`);
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`Error: ${message}`);
  process.exitCode = 1;
});
