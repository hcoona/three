/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import Hexo from 'hexo';
import { describe, expect, it } from 'vitest';
import { convertAsciiDoc } from '../src/core/asciidoctor';
import { applyStaticHighlighting } from '../src/core/highlight';
import { escapeCurlyBraces } from '../src/core/sanitize';
import type { Hexo as HexoContract, Renderer, RendererData } from '../src/types';

const HEXO_ENGINE = 'adoc';
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
};

type HexoTestInstance = HexoContract & {
  base_dir: string;
  render: HexoRenderAPI;
  init(): Promise<void>;
  exit(err?: unknown): Promise<void>;
};

type HexoConstructor = new (baseDir: string, options?: Record<string, unknown>) => HexoTestInstance;

type StageName = 'convert' | 'highlight' | 'escape';
type ConvertStage = (text: string) => string | Promise<string>;
type HtmlStage = (html: string) => string;

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

/**
 * Test-only seam: compose the real production v4 stages while allowing one stage
 * to be replaced with a throwing sentinel. This keeps product source unchanged.
 */
const createRendererProbe = ({
  convert = convertAsciiDoc,
  highlight = applyStaticHighlighting,
  escapeStage = escapeCurlyBraces,
}: {
  convert?: ConvertStage;
  highlight?: HtmlStage;
  escapeStage?: HtmlStage;
} = {}) => {
  const calls: StageName[] = [];

  const probeRenderer: Renderer = async (data: RendererData) => {
    calls.push('convert');
    const html = await convert(data.text);

    calls.push('highlight');
    const highlighted = highlight(html);

    calls.push('escape');
    return escapeStage(highlighted);
  };

  return {
    calls,
    renderer: probeRenderer,
    reset() {
      calls.length = 0;
    },
  };
};

const renderViaHexo = async (probeRenderer: Renderer, text: string = SAMPLE_TEXT): Promise<string> => {
  const workspace = createHexoWorkspace();
  const hexo = new HexoClass(workspace, { silent: true, debug: false });
  hexo.extend.renderer.register(HEXO_ENGINE, 'html', probeRenderer, false);

  try {
    await hexo.init();
    return await hexo.render.render({ text, engine: HEXO_ENGINE });
  } finally {
    try {
      await drainPendingTasks();
      await hexo.exit();
      await drainPendingTasks();
    } finally {
      rmSync(workspace, { recursive: true, force: true });
    }
  }
};

describe.sequential('pipeline failure probe worker', () => {
  it('preserves a conversion sentinel and stops before highlighting or escaping', async () => {
    const sentinel = new Error('conversion sentinel');
    const probe = createRendererProbe({
      convert: () => {
        throw sentinel;
      },
    });

    await expect(probe.renderer({ text: SAMPLE_TEXT })).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert']);

    probe.reset();
    await expect(renderViaHexo(probe.renderer)).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert']);
  });

  it('preserves a highlighting sentinel and stops before escaping', async () => {
    const sentinel = new Error('highlighting sentinel');
    const probe = createRendererProbe({
      highlight: () => {
        throw sentinel;
      },
    });

    await expect(probe.renderer({ text: SAMPLE_TEXT })).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert', 'highlight']);

    probe.reset();
    await expect(renderViaHexo(probe.renderer)).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert', 'highlight']);
  });

  it('preserves an escaping sentinel after conversion and highlighting', async () => {
    const sentinel = new Error('escape sentinel');
    const probe = createRendererProbe({
      escapeStage: () => {
        throw sentinel;
      },
    });

    await expect(probe.renderer({ text: SAMPLE_TEXT })).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert', 'highlight', 'escape']);

    probe.reset();
    await expect(renderViaHexo(probe.renderer)).rejects.toBe(sentinel);
    expect(probe.calls).toEqual(['convert', 'highlight', 'escape']);
  });

  it('still renders successfully after an injected escape failure', async () => {
    const sentinel = new Error('escape once sentinel');

    let directFailed = false;
    const directProbe = createRendererProbe({
      escapeStage: (html) => {
        if (!directFailed) {
          directFailed = true;
          throw sentinel;
        }

        return escapeCurlyBraces(html);
      },
    });

    await expect(directProbe.renderer({ text: SAMPLE_TEXT })).rejects.toBe(sentinel);
    expect(directProbe.calls).toEqual(['convert', 'highlight', 'escape']);

    directProbe.reset();
    await expect(directProbe.renderer({ text: SAMPLE_TEXT })).resolves.toBe(PINNED_V4_EXPECTED_HTML);
    expect(directProbe.calls).toEqual(['convert', 'highlight', 'escape']);

    let hexoFailed = false;
    const hexoProbe = createRendererProbe({
      escapeStage: (html) => {
        if (!hexoFailed) {
          hexoFailed = true;
          throw sentinel;
        }

        return escapeCurlyBraces(html);
      },
    });

    await expect(renderViaHexo(hexoProbe.renderer)).rejects.toBe(sentinel);
    expect(hexoProbe.calls).toEqual(['convert', 'highlight', 'escape']);

    hexoProbe.reset();
    await expect(renderViaHexo(hexoProbe.renderer)).resolves.toBe(PINNED_V4_EXPECTED_HTML);
    expect(hexoProbe.calls).toEqual(['convert', 'highlight', 'escape']);
  });
});
