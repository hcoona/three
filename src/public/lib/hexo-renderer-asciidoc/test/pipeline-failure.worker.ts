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
type BoundaryName = '@asciidoctor/core.convert' | 'hexo-util.highlight' | 'sanitize.replace';

type FailureInjection = {
  sentinel: unknown;
  stage: StageName;
};

const pipeline = vi.hoisted(() => ({
  calls: [] as BoundaryName[],
  coreConvert: vi.fn(),
  escapeCurlyBraces: vi.fn(),
  failure: null as FailureInjection | null,
  hexoHighlight: vi.fn(),
  pendingConversions: 0,
}));

vi.mock('@asciidoctor/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@asciidoctor/core')>();

  pipeline.coreConvert.mockImplementation(async (...args: Parameters<typeof actual.convert>) => {
    pipeline.calls.push('@asciidoctor/core.convert');
    pipeline.pendingConversions += 1;

    try {
      const failure = pipeline.failure;
      if (failure?.stage === 'convertAsciiDoc') {
        pipeline.failure = null;
        throw failure.sentinel;
      }

      return await actual.convert(...args);
    } finally {
      pipeline.pendingConversions -= 1;
    }
  });

  return {
    ...actual,
    convert: pipeline.coreConvert,
  };
});

vi.mock('hexo-util', async (importOriginal) => {
  const actual = await importOriginal<typeof import('hexo-util')>();
  const actualDefault = (actual as typeof actual & { default: typeof actual }).default;

  pipeline.hexoHighlight.mockImplementation((...args: Parameters<typeof actual.highlight>) => {
    pipeline.calls.push('hexo-util.highlight');

    const failure = pipeline.failure;
    if (failure?.stage === 'applyStaticHighlighting') {
      pipeline.failure = null;
      throw failure.sentinel;
    }

    return actual.highlight(...args);
  });

  return {
    ...actual,
    default: new Proxy(actualDefault, {
      get(target, property, receiver) {
        return property === 'highlight' ? pipeline.hexoHighlight : Reflect.get(target, property, receiver);
      },
    }),
    highlight: pipeline.hexoHighlight,
  };
});

vi.mock('../src/core/sanitize', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../src/core/sanitize')>();

  pipeline.escapeCurlyBraces.mockImplementation((html: string) => {
    pipeline.calls.push('sanitize.replace');

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
const COMPLETE_TRACE: readonly BoundaryName[] = [
  '@asciidoctor/core.convert',
  'hexo-util.highlight',
  'sanitize.replace',
];
// This source block produces Asciidoctor's canonical listing-block chain, so static highlighting is mandatory.
const CANONICAL_SOURCE_TEXT = `
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
  pipeline.coreConvert.mockClear();
  pipeline.escapeCurlyBraces.mockClear();
  pipeline.hexoHighlight.mockClear();
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

const expectBoundaryTrace = (expected: readonly BoundaryName[]): void => {
  expect(pipeline.calls).toEqual(expected);
  expect(pipeline.coreConvert).toHaveBeenCalledTimes(expected.includes('@asciidoctor/core.convert') ? 1 : 0);
  expect(pipeline.hexoHighlight).toHaveBeenCalledTimes(expected.includes('hexo-util.highlight') ? 1 : 0);
  expect(pipeline.escapeCurlyBraces).toHaveBeenCalledTimes(expected.includes('sanitize.replace') ? 1 : 0);
};

const expectPendingTasksDrained = async (): Promise<void> => {
  await drainPendingTasks();
  expect(pipeline.pendingConversions).toBe(0);
};

class PipelineFailureSentinel extends Error {
  readonly identity: symbol;

  constructor(readonly stage: StageName) {
    super(`${stage} sentinel`);
    this.name = 'PipelineFailureSentinel';
    this.identity = Symbol(`${stage} sentinel`);
  }
}

const createSentinel = (stage: StageName): Readonly<PipelineFailureSentinel> =>
  Object.freeze(new PipelineFailureSentinel(stage));

const FAILURE_CASES = [
  {
    expectedTrace: ['@asciidoctor/core.convert'],
    extension: 'ad',
    stage: 'convertAsciiDoc',
  },
  {
    expectedTrace: ['@asciidoctor/core.convert', 'hexo-util.highlight'],
    extension: 'adoc',
    stage: 'applyStaticHighlighting',
  },
  {
    expectedTrace: COMPLETE_TRACE,
    extension: 'asciidoc',
    stage: 'escapeCurlyBraces',
  },
] as const satisfies readonly {
  expectedTrace: readonly BoundaryName[];
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
      await expectExactSentinelRejection(() => renderer({ text: CANONICAL_SOURCE_TEXT }), sentinel);
      expectBoundaryTrace(expectedTrace);
      expect(pipeline.failure).toBeNull();
      await expectPendingTasksDrained();

      resetObservations();
      await expect(renderer({ text: CANONICAL_SOURCE_TEXT })).resolves.toBe(PINNED_V4_EXPECTED_HTML);
      expectBoundaryTrace(COMPLETE_TRACE);
      await expectPendingTasksDrained();

      resetObservations();
      expect(hexoInstance.extend.renderer.get(extension)).toBe(renderer);
      armFailure(stage, sentinel);
      await expectExactSentinelRejection(
        () => hexoInstance.render.render({ text: CANONICAL_SOURCE_TEXT, engine: extension }),
        sentinel,
      );
      expectBoundaryTrace(expectedTrace);
      expect(pipeline.failure).toBeNull();
      await expectPendingTasksDrained();

      resetObservations();
      await expect(hexoInstance.render.render({ text: CANONICAL_SOURCE_TEXT, engine: extension })).resolves.toBe(
        PINNED_V4_EXPECTED_HTML,
      );
      expectBoundaryTrace(COMPLETE_TRACE);
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
      expectBoundaryTrace([]);
      expect(pipeline.failure).toBe(armedFailure);
      pipeline.failure = null;
    },
  );
});
