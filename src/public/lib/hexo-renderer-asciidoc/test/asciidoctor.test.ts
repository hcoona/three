/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { runVitestSubprocess } from './helpers/vitest-subprocess';

const CHILD_TIMEOUT_MS = 30_000;
const CASES = [
  'returns string output without writing files',
  'keeps include resolution tied to the active CWD while ignoring data.path and Hexo site root',
  'changes relative include resolution when only the active CWD changes',
  'captures missing, traversal, absolute, and symlink include behavior',
  'never issues unexpected loopback URI requests even when allow-uri-read is declared',
  'keeps multiple outstanding conversion and renderer promises isolated',
  'proves strict real-runtime overlap and isolation across a mixed batch with sentinels',
  'produces equivalent isolated output across repeated bounded parallel batches',
  'supports mixed concurrent success and failure and still succeeds afterwards',
] as const;
const MATRIX_TIMEOUT_MS = CHILD_TIMEOUT_MS * CASES.length + 30_000;

describe('Asciidoctor v4 runtime characterization', () => {
  it(
    'covers each filesystem, URI, and concurrency case in a strict isolated subprocess',
    async () => {
      const packageRoot = path.resolve(import.meta.dirname, '..');

      for (const testName of CASES) {
        await expect(
          runVitestSubprocess({
            args: ['--config', 'vitest.asciidoctor-runtime.config.ts', '-t', testName],
            cwd: packageRoot,
            timeout: CHILD_TIMEOUT_MS,
          }),
        ).resolves.toBeUndefined();
      }
    },
    MATRIX_TIMEOUT_MS,
  );
});
