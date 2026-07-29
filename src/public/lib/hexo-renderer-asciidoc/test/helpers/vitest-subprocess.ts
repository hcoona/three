/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

type ExecFileFailure = Error & {
  code?: number | string | null;
  signal?: NodeJS.Signals | null;
  stderr?: Buffer | string;
  stdout?: Buffer | string;
};

const STRICT_UNHANDLED_REJECTIONS = '--unhandled-rejections=strict';
const execFileAsync = promisify(execFile);

const formatOutput = (output: Buffer | string | undefined): string => {
  if (output === undefined) {
    return '<empty>';
  }

  const text = typeof output === 'string' ? output : output.toString('utf8');
  return text.length > 0 ? text : '<empty>';
};

const withStrictUnhandledRejections = (): string => {
  const { NODE_OPTIONS } = process.env;
  return [NODE_OPTIONS, STRICT_UNHANDLED_REJECTIONS].filter(Boolean).join(' ');
};

export const runVitestSubprocess = async ({
  args,
  cwd,
  timeout,
}: {
  args: string[];
  cwd: string;
  timeout: number;
}): Promise<void> => {
  try {
    await execFileAsync('pnpm', ['exec', 'vitest', 'run', ...args], {
      cwd,
      encoding: 'utf8',
      env: {
        ...process.env,
        NODE_OPTIONS: withStrictUnhandledRejections(),
      },
      maxBuffer: 10 * 1024 * 1024,
      timeout,
    });
  } catch (error) {
    const failure = error as ExecFileFailure;

    throw new Error(
      [
        `Vitest subprocess failed: pnpm exec vitest run ${args.join(' ')}`,
        `status: ${failure.code ?? 'unknown'}`,
        `signal: ${failure.signal ?? 'none'}`,
        '',
        `stdout:\n${formatOutput(failure.stdout)}`,
        '',
        `stderr:\n${formatOutput(failure.stderr)}`,
      ].join('\n'),
      { cause: error },
    );
  }
};
