#!/usr/bin/env node

import process from 'node:process';

import { runBridgeCommand, runMonitorCommand, runSetupCommand, runShowStateCommand } from './commands.ts';
import type { MonitorOptions } from './types.ts';

const DEFAULT_MONITOR_OPTIONS: MonitorOptions = {
  once: false,
  timeoutSeconds: 30,
};

interface CliArgs {
  action?: string;
  'api-base-url'?: string;
  'bot-token'?: string;
  'chat-id'?: string;
  force?: string;
  model?: string;
  once?: string;
  'copilot-path'?: string;
  'state-dir'?: string;
  'timeout-seconds'?: string;
  [key: string]: string | undefined;
}

async function main(): Promise<void> {
  const normalizedArguments = process.argv[2] === '--' ? process.argv.slice(3) : process.argv.slice(2);
  const [command, ...restArguments] = normalizedArguments;

  switch (command) {
    case 'setup': {
      const args = parseArgs(restArguments);
      await runSetupCommand({
        force: args.force === 'true',
        setup: {
          botToken: requireStringArg(args['bot-token'], 'bot-token'),
          chatId: requireStringArg(args['chat-id'], 'chat-id'),
          ...(args['api-base-url'] ? { apiBaseUrl: args['api-base-url'] } : {}),
        },
        ...(args['state-dir'] ? { stateDirectory: args['state-dir'] } : {}),
      });
      break;
    }

    case 'monitor': {
      const args = parseArgs(restArguments);
      await runMonitorCommand({
        ...(args['state-dir'] ? { stateDirectory: args['state-dir'] } : {}),
        monitor: {
          once: args.once === 'true',
          timeoutSeconds: parseIntegerArg(
            args['timeout-seconds'],
            DEFAULT_MONITOR_OPTIONS.timeoutSeconds,
            'timeout-seconds',
          ),
        },
      });
      break;
    }

    case 'bridge': {
      const args = parseArgs(restArguments);
      await runBridgeCommand({
        bridge: {
          copilotPath: args['copilot-path'] ?? 'copilot',
          timeoutSeconds: parseIntegerArg(
            args['timeout-seconds'],
            DEFAULT_MONITOR_OPTIONS.timeoutSeconds,
            'timeout-seconds',
          ),
          ...(args.model ? { model: args.model } : {}),
        },
        ...(args['state-dir'] ? { stateDirectory: args['state-dir'] } : {}),
      });
      break;
    }

    case 'show-state': {
      const args = parseArgs(restArguments);
      await runShowStateCommand({
        ...(args['state-dir'] ? { stateDirectory: args['state-dir'] } : {}),
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
    'telegram-topic-session-bridge',
    '',
    'POC CLI for a Telegram General Topic control plane and topic-scoped ACP sessions.',
    '',
    'Commands:',
    '  setup --bot-token TOKEN --chat-id ID [--api-base-url URL] [--state-dir PATH] [--force]',
    '  monitor [--state-dir PATH] [--timeout-seconds 30] [--once]',
    '  bridge [--copilot-path copilot] [--model MODEL] [--timeout-seconds 30] [--state-dir PATH]',
    '  show-state [--state-dir PATH]',
    '',
    'Notes:',
    '  - setup persists the bot token and the forum-enabled supergroup chat id.',
    '  - bridge keeps General Topic command replies threaded by replying to the command message.',
    '  - /new --cwd validates the directory before creating any session or topic.',
    '  - /list and /kill are handled from persisted gateway state, not ACP session listing.',
  ];

  process.stdout.write(`${lines.join('\n')}\n`);
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`Error: ${message}`);
  process.exitCode = 1;
});
