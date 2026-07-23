/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import Hexo from 'hexo';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Hexo as HexoContract, Renderer } from '../src/types';

type StageName = 'convertAsciiDoc' | 'applyStaticHighlighting' | 'escapeCurlyBraces';

type FailureInjection = {
  sentinel: unknown;
  stage: StageName;
};

const pipeline = vi.hoisted(() => ({
  applyStaticHighlighting: vi.fn(),
  calls: [] as StageName[],
  convertAsciiDoc: vi.fn(),
  escapeCurlyBraces: vi.fn(),
  failure: null as FailureInjection | null,
  pendingConversions: 0,
}));

vi.mock('../src/core/asciidoctor', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/core/asciidoctor')>();

  pipeline.convertAsciiDoc.mockImplementation(async (text: string) => {
    pipeline.calls.push('convertAsciiDoc');
    pipeline.pendingConversions += 1;

    try {
      const failure = pipeline.failure;
      if (failure?.stage === 'convertAsciiDoc') {
        pipeline.failure = null;
        throw failure.sentinel;
      }

      return await actual.convertAsciiDoc(text);
    } finally {
      pipeline.pendingConversions -= 1;
    }
  });

  return {
    ...actual,
    convertAsciiDoc: pipeline.convertAsciiDoc,
  };
});

vi.mock('../src/core/highlight', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/core/highlight')>();

  pipeline.applyStaticHighlighting.mockImplementation((html: string) => {
    pipeline.calls.push('applyStaticHighlighting');

    const failure = pipeline.failure;
    if (failure?.stage === 'applyStaticHighlighting') {
      pipeline.failure = null;
      throw failure.sentinel;
    }

    return actual.applyStaticHighlighting(html);
  });

  return {
    ...actual,
    applyStaticHighlighting: pipeline.applyStaticHighlighting,
  };
});

vi.mock('../src/core/sanitize', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/core/sanitize')>();

  pipeline.escapeCurlyBraces.mockImplementation((html: string) => {
    pipeline.calls.push('escapeCurlyBraces');

    const failure = pipeline.failure;
    if (failure?.stage === 'escapeCurlyBraces') {
      pipeline.failure = null;

      const throwingHtml = {
        replace: () => {
          throw failure.sentinel;
        },
      } as unknown as string;

      // Run the production sanitizer so a catch-and-fallback mutation is observable.
      return actual.escapeCurlyBraces(throwingHtml);
    }

    return actual.escapeCurlyBraces(html);
  });

  return {
    ...actual,
    escapeCurlyBraces: pipeline.escapeCurlyBraces,
  };
});

import renderer from '../src/core/renderer';
import registerRenderer from '../src/hexo/register';

const SUPPORTED_EXTENSIONS = ['ad', 'adoc', 'asciidoc'] as const;
const COMPLETE_TRACE: readonly StageName[] = ['convertAsciiDoc', 'applyStaticHighlighting', 'escapeCurlyBraces'];
const SAMPLE_TEXT = `
== Pipeline Failure Probe ==

[source,javascript]
----
const value = { nested: true };
----
`;
// Independently reviewed v4 baseline; intentionally not derived from production stages.
const PINNED_V4_EXPECTED_HTML = `<div class="sect1">
<h2 id="_pipeline_failure_probe">Pipeline Failure Probe</h2>
<div class="sectionbody">
<div class="listingblock">
<div class="content">
<pre><code class="highlight javascript"><span class="keyword">const</span> value = &#123; <span class="attr">nested</span>: <span class="literal">true</span> &#125;;</code></pre>
</div>
</div>
</div>
</div>`;

type HexoRenderAPI = {
  render(data: Record<string, unknown>, locals?: Record<string, unknown>): Promise<string>;
  renderSync(data: Record<string, unknown>, locals?: Record<string, unknown>): string;
};

type HexoRendererRegistry = HexoContract['extend']['renderer'] & {
  get(name: string, sync?: boolean): Renderer | undefined;
};

type HexoTestInstance = HexoContract & {
  base_dir: string;
  extend: {
    renderer: HexoRendererRegistry;
  };
  render: HexoRenderAPI;
  init(): Promise<void>;
  exit(err?: unknown): Promise<void>;
};

type HexoConstructor = new (baseDir: string, options?: Record<string, unknown>) => HexoTestInstance;

const HexoClass = Hexo as unknown as HexoConstructor;

const createHexoWorkspace = (): string => {
  const tempDir = mkdtempSync(path.join(tmpdir(), 'hexo-renderer-asciidoc-pipeline-failure-'));
  writeFileSync(path.join(tempDir, '_config.yml'), 'title: hexo-renderer-asciidoc\n');
  return tempDir;
};

const drainPendingTasks = async (): Promise<void> => {
  await Promise.resolve();
  await new Promise<void>((resolve) => setImmediate(resolve));
};

const resetObservations = (): void => {
  pipeline.calls.length = 0;
  pipeline.convertAsciiDoc.mockClear();
  pipeline.applyStaticHighlighting.mockClear();
  pipeline.escapeCurlyBraces.mockClear();
};

const armFailure = (stage: StageName, sentinel: unknown): void => {
  if (pipeline.failure !== null) {
    throw new Error(`A ${pipeline.failure.stage} failure is already armed`);
  }

  pipeline.failure = { sentinel, stage };
};

const captureOutcome = async <T>(
  operation: () => Promise<T>,
): Promise<{ reason: unknown; status: 'rejected' } | { status: 'fulfilled'; value: T }> => {
  try {
    return { status: 'fulfilled', value: await operation() };
  } catch (reason) {
    return { reason, status: 'rejected' };
  }
};

const expectExactSentinelRejection = async (operation: () => Promise<unknown>, sentinel: unknown): Promise<void> => {
  const outcome = await captureOutcome(operation);

  expect(outcome.status).toBe('rejected');
  if (outcome.status === 'fulfilled') {
    throw new Error(`Expected rejection, but the renderer returned a ${typeof outcome.value} fallback`);
  }

  // Identity excludes wrapping, cause-only propagation, and stringification.
  expect(outcome.reason).toBe(sentinel);
};

const expectStageTrace = (expected: readonly StageName[]): void => {
  expect(pipeline.calls).toEqual(expected);
  expect(pipeline.convertAsciiDoc).toHaveBeenCalledTimes(expected.includes('convertAsciiDoc') ? 1 : 0);
  expect(pipeline.applyStaticHighlighting).toHaveBeenCalledTimes(expected.includes('applyStaticHighlighting') ? 1 : 0);
  expect(pipeline.escapeCurlyBraces).toHaveBeenCalledTimes(expected.includes('escapeCurlyBraces') ? 1 : 0);
};

const expectPendingTasksDrained = async (): Promise<void> => {
  await drainPendingTasks();
  expect(pipeline.pendingConversions).toBe(0);
};

const createSentinel = (stage: StageName): Readonly<{ identity: symbol; stage: StageName }> =>
  Object.freeze({
    identity: Symbol(`${stage} sentinel`),
    stage,
  });

const FAILURE_CASES = [
  {
    expectedTrace: ['convertAsciiDoc'],
    extension: 'ad',
    stage: 'convertAsciiDoc',
  },
  {
    expectedTrace: ['convertAsciiDoc', 'applyStaticHighlighting'],
    extension: 'adoc',
    stage: 'applyStaticHighlighting',
  },
  {
    expectedTrace: COMPLETE_TRACE,
    extension: 'asciidoc',
    stage: 'escapeCurlyBraces',
  },
] as const satisfies readonly {
  expectedTrace: readonly StageName[];
  extension: (typeof SUPPORTED_EXTENSIONS)[number];
  stage: StageName;
}[];

describe.sequential('production pipeline failure worker', () => {
  let hexoInstance: HexoTestInstance;
  let workspace: string;

  beforeAll(async () => {
    workspace = createHexoWorkspace();
    hexoInstance = new HexoClass(workspace, { silent: true, debug: false });
    registerRenderer(hexoInstance);
    await hexoInstance.init();
  });

  afterAll(async () => {
    try {
      await drainPendingTasks();
      await hexoInstance.exit();
      await drainPendingTasks();
    } finally {
      rmSync(workspace, { recursive: true, force: true });
    }
  });

  beforeEach(() => {
    pipeline.failure = null;
    resetObservations();
  });

  afterEach(async () => {
    pipeline.failure = null;
    await expectPendingTasksDrained();
  });

  it.each(FAILURE_CASES)(
    'preserves the exact $stage sentinel through the production renderer and Hexo .$extension rendering',
    async ({ expectedTrace, extension, stage }) => {
      const sentinel = createSentinel(stage);

      armFailure(stage, sentinel);
      await expectExactSentinelRejection(() => renderer({ text: SAMPLE_TEXT }), sentinel);
      expectStageTrace(expectedTrace);
      expect(pipeline.failure).toBeNull();
      await expectPendingTasksDrained();

      resetObservations();
      await expect(renderer({ text: SAMPLE_TEXT })).resolves.toBe(PINNED_V4_EXPECTED_HTML);
      expectStageTrace(COMPLETE_TRACE);
      await expectPendingTasksDrained();

      resetObservations();
      expect(hexoInstance.extend.renderer.get(extension)).toBe(renderer);
      armFailure(stage, sentinel);
      await expectExactSentinelRejection(
        () => hexoInstance.render.render({ text: SAMPLE_TEXT, engine: extension }),
        sentinel,
      );
      expectStageTrace(expectedTrace);
      expect(pipeline.failure).toBeNull();
      await expectPendingTasksDrained();

      resetObservations();
      await expect(hexoInstance.render.render({ text: SAMPLE_TEXT, engine: extension })).resolves.toBe(
        PINNED_V4_EXPECTED_HTML,
      );
      expectStageTrace(COMPLETE_TRACE);
      await expectPendingTasksDrained();
    },
  );

  it.each(SUPPORTED_EXTENSIONS)(
    'does not invoke the production renderer pipeline from Hexo renderSync for .$extension',
    (extension) => {
      const text = `== Sync Zero Invocation ${extension} ==`;
      const sentinel = createSentinel('convertAsciiDoc');

      expect(hexoInstance.extend.renderer.get(extension)).toBe(renderer);
      armFailure('convertAsciiDoc', sentinel);
      const armedFailure = pipeline.failure;

      const output = hexoInstance.render.renderSync({ text, engine: extension });

      expect(output).toBe(text);
      expect(output).not.toContain('[object Promise]');
      expectStageTrace([]);
      expect(pipeline.failure).toBe(armedFailure);
      pipeline.failure = null;
    },
  );
});
