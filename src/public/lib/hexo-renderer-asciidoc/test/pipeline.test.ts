/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import path from 'node:path';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { runVitestSubprocess } from './helpers/vitest-subprocess';

type Deferred<T> = {
  promise: Promise<T>;
  reject: (reason?: unknown) => void;
  resolve: (value: T) => void;
};

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

const createDeferred = <T>(): Deferred<T> => {
  let reject: Deferred<T>['reject'] = () => undefined;
  let resolve: Deferred<T>['resolve'] = () => undefined;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });

  return { promise, reject, resolve };
};

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
  it('awaits conversion before highlighting and brace escaping', async () => {
    const deferredConversion = createDeferred<string>();
    pipeline.convert.mockReset().mockImplementation(() => {
      pipeline.calls.push('convert:called');
      return deferredConversion.promise.then((html) => {
        pipeline.calls.push('convert:resolved');
        return html;
      });
    });
    pipeline.highlight.mockReset().mockImplementation((html: string) => {
      pipeline.calls.push('highlight');
      return `<highlighted>${html}</highlighted>`;
    });
    pipeline.escape.mockReset().mockImplementation((html: string) => {
      pipeline.calls.push('escape');
      return `<escaped>${html}</escaped>`;
    });

    const renderPromise = renderer({ text: 'input' });

    await Promise.resolve();

    expect(pipeline.convert).toHaveBeenCalledWith('input');
    expect(pipeline.calls).toEqual(['convert:called']);
    expect(pipeline.highlight).not.toHaveBeenCalled();
    expect(pipeline.escape).not.toHaveBeenCalled();

    deferredConversion.resolve('<converted>input</converted>');

    await expect(renderPromise).resolves.toBe(
      '<escaped><highlighted><converted>input</converted></highlighted></escaped>',
    );
    expect(pipeline.calls).toEqual(['convert:called', 'convert:resolved', 'highlight', 'escape']);
    expect(pipeline.highlight).toHaveBeenCalledWith('<converted>input</converted>');
    expect(pipeline.escape).toHaveBeenCalledWith('<highlighted><converted>input</converted></highlighted>');
  });

  it('preserves conversion error identity and suppresses downstream stages', async () => {
    const sentinel = new Error('conversion sentinel');
    pipeline.convert.mockImplementation(() => {
      pipeline.calls.push('convert');
      throw sentinel;
    });

    await expect(renderer({ text: 'input' })).rejects.toBe(sentinel);
    expect(pipeline.calls).toEqual(['convert']);
    expect(pipeline.highlight).not.toHaveBeenCalled();
    expect(pipeline.escape).not.toHaveBeenCalled();
  });

  it('preserves highlighting error identity and suppresses brace escaping', async () => {
    const sentinel = new Error('highlighting sentinel');
    pipeline.highlight.mockImplementation(() => {
      pipeline.calls.push('highlight');
      throw sentinel;
    });

    await expect(renderer({ text: 'input' })).rejects.toBe(sentinel);
    expect(pipeline.calls).toEqual(['convert', 'highlight']);
    expect(pipeline.escape).not.toHaveBeenCalled();
  });

  it('characterizes conversion, highlighting, and escaping failures in an isolated subprocess', async () => {
    const packageRoot = path.resolve(import.meta.dirname, '..');

    await expect(
      runVitestSubprocess({
        args: ['--config', 'vitest.pipeline-failure.config.ts'],
        cwd: packageRoot,
        timeout: 30_000,
      }),
    ).resolves.toBeUndefined();
  }, 60_000);
});
