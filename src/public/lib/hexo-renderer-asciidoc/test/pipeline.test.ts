/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const pipeline = vi.hoisted(() => ({
  calls: [] as string[],
  convert: vi.fn(),
  highlight: vi.fn(),
  escape: vi.fn(),
}));

vi.mock('../src/core/asciidoctor', () => ({
  convertAsciiDoc: pipeline.convert,
}));

vi.mock('../src/core/highlight', () => ({
  applyStaticHighlighting: pipeline.highlight,
}));

vi.mock('../src/core/sanitize', () => ({
  escapeCurlyBraces: pipeline.escape,
}));

import renderer from '../src/core/renderer';

beforeEach(() => {
  pipeline.calls.length = 0;
  pipeline.convert.mockReset().mockImplementation((text: string) => {
    pipeline.calls.push('convert');
    return `<converted>${text}</converted>`;
  });
  pipeline.highlight.mockReset().mockImplementation((html: string) => {
    pipeline.calls.push('highlight');
    return `<highlighted>${html}</highlighted>`;
  });
  pipeline.escape.mockReset().mockImplementation((html: string) => {
    pipeline.calls.push('escape');
    return `<escaped>${html}</escaped>`;
  });
});

describe('renderer pipeline', () => {
  it('runs conversion, highlighting, and brace escaping in order', () => {
    const result = renderer({ text: 'input' });

    expect(pipeline.calls).toEqual(['convert', 'highlight', 'escape']);
    expect(pipeline.convert).toHaveBeenCalledWith('input');
    expect(pipeline.highlight).toHaveBeenCalledWith('<converted>input</converted>');
    expect(pipeline.escape).toHaveBeenCalledWith('<highlighted><converted>input</converted></highlighted>');
    expect(result).toBe('<escaped><highlighted><converted>input</converted></highlighted></escaped>');
  });

  it('preserves conversion error identity and suppresses downstream stages', () => {
    const sentinel = new Error('conversion sentinel');
    pipeline.convert.mockImplementation(() => {
      pipeline.calls.push('convert');
      throw sentinel;
    });

    let caught: unknown;
    try {
      renderer({ text: 'input' });
    } catch (error) {
      caught = error;
    }

    expect(caught).toBe(sentinel);
    expect(pipeline.calls).toEqual(['convert']);
    expect(pipeline.highlight).not.toHaveBeenCalled();
    expect(pipeline.escape).not.toHaveBeenCalled();
  });

  it('preserves highlighting error identity and suppresses brace escaping', () => {
    const sentinel = new Error('highlighting sentinel');
    pipeline.highlight.mockImplementation(() => {
      pipeline.calls.push('highlight');
      throw sentinel;
    });

    let caught: unknown;
    try {
      renderer({ text: 'input' });
    } catch (error) {
      caught = error;
    }

    expect(caught).toBe(sentinel);
    expect(pipeline.calls).toEqual(['convert', 'highlight']);
    expect(pipeline.escape).not.toHaveBeenCalled();
  });

  it('characterizes conversion, highlighting, and escaping failures in an isolated subprocess', () => {
    const packageRoot = path.resolve(import.meta.dirname, '..');

    expect(() =>
      execFileSync('pnpm', ['exec', 'vitest', 'run', '--config', 'vitest.pipeline-failure.config.ts'], {
        cwd: packageRoot,
        env: { ...process.env, NODE_OPTIONS: '--unhandled-rejections=strict' },
        stdio: 'pipe',
        timeout: 30_000,
      }),
    ).not.toThrow();
  }, 60_000);
});
