/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { describe, expect, it } from 'vitest';
import { runVitestSubprocess } from './helpers/vitest-subprocess';

const CHILD_TIMEOUT_MS = 45_000;
const CONCURRENT_COLD_STARTS = 3;
const COLD_START_PROBE_TEST_NAME = 'renders the production renderer in a fresh process for cold-start evidence';

type ColdStartMeasurement = {
  elapsedMilliseconds: number;
  label: string;
};

type ColdStartEvidence = {
  runs: ColdStartMeasurement[];
  wallClockMilliseconds: number;
};

const writeTimingEvidence = (label: string, evidence: ColdStartEvidence): void => {
  process.stdout.write(`${label}: ${JSON.stringify(evidence)}\n`);
};

const runColdStartProbe = async (packageRoot: string, label: string): Promise<ColdStartMeasurement> => {
  const startedAt = performance.now();
  await runVitestSubprocess({
    args: ['--config', 'vitest.asciidoctor-runtime.config.ts', '-t', COLD_START_PROBE_TEST_NAME],
    cwd: packageRoot,
    timeout: CHILD_TIMEOUT_MS,
  });

  return {
    elapsedMilliseconds: performance.now() - startedAt,
    label,
  };
};

const assertColdStartEvidence = (evidence: ColdStartEvidence): void => {
  expect(evidence.runs).toHaveLength(CONCURRENT_COLD_STARTS);
  for (const run of evidence.runs) {
    expect(Number.isFinite(run.elapsedMilliseconds)).toBe(true);
    expect(run.elapsedMilliseconds).toBeGreaterThan(0);
  }
  expect(evidence.wallClockMilliseconds).toBeGreaterThan(0);
};

describe.sequential('Asciidoctor cold-start timing evidence', () => {
  it('captures sequential cold-start timings for the production renderer', async () => {
    const packageRoot = path.resolve(import.meta.dirname, '..');
    const startedAt = performance.now();
    const runs: ColdStartMeasurement[] = [];

    for (let index = 1; index <= CONCURRENT_COLD_STARTS; index += 1) {
      runs.push(await runColdStartProbe(packageRoot, `sequential-${index}`));
    }

    const evidence: ColdStartEvidence = {
      runs,
      wallClockMilliseconds: performance.now() - startedAt,
    };

    writeTimingEvidence('Sequential cold-start timing evidence', evidence);
    assertColdStartEvidence(evidence);
  }, 180_000);

  it('captures bounded-parallel cold-start timings for the production renderer', async () => {
    const packageRoot = path.resolve(import.meta.dirname, '..');
    const startedAt = performance.now();
    const runs = await Promise.all(
      Array.from({ length: CONCURRENT_COLD_STARTS }, async (_value, index) => {
        return await runColdStartProbe(packageRoot, `parallel-${index + 1}`);
      }),
    );

    const evidence: ColdStartEvidence = {
      runs,
      wallClockMilliseconds: performance.now() - startedAt,
    };

    writeTimingEvidence('Bounded-parallel cold-start timing evidence', evidence);
    assertColdStartEvidence(evidence);
  }, 180_000);
});
