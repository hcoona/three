/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { beforeEach, describe, expect, it, vi } from 'vitest';

const asciidoctorCore = vi.hoisted(() => ({
  convert: vi.fn(),
  Logger: vi.fn(
    class TestLogger {
      readonly identity = 'per-conversion-test-logger';
    },
  ),
}));

vi.mock('@asciidoctor/core', () => ({
  convert: asciidoctorCore.convert,
  Logger: asciidoctorCore.Logger,
}));

describe('convertAsciiDoc', () => {
  beforeEach(() => {
    asciidoctorCore.convert.mockReset();
    asciidoctorCore.Logger.mockClear();
    vi.resetModules();
  });

  it('passes the exact fixed input and controlled options to @asciidoctor/core', async () => {
    asciidoctorCore.convert.mockResolvedValue('<p>converted</p>');
    const { convertAsciiDoc } = await import('../src/core/asciidoctor');

    await expect(convertAsciiDoc('== Exact Input ==')).resolves.toBe('<p>converted</p>');
    expect(asciidoctorCore.convert).toHaveBeenCalledTimes(1);
    const logger = asciidoctorCore.Logger.mock.instances[0];
    expect(asciidoctorCore.convert).toHaveBeenCalledWith('== Exact Input ==', {
      doctype: 'article',
      logger,
      safe: 'server',
      to_file: false,
    });
    expect(asciidoctorCore.Logger).toHaveBeenCalledTimes(1);
  });

  it('rejects before calling @asciidoctor/core when input is not a string', async () => {
    const { convertAsciiDoc } = await import('../src/core/asciidoctor');

    await expect(convertAsciiDoc(42 as unknown as string)).rejects.toThrow(
      new TypeError('Asciidoctor conversion requires string input: number'),
    );
    expect(asciidoctorCore.convert).not.toHaveBeenCalled();
  });

  it('rejects with TypeError when Asciidoctor does not return a string', async () => {
    asciidoctorCore.convert.mockResolvedValue(42);
    const { convertAsciiDoc } = await import('../src/core/asciidoctor');

    await expect(convertAsciiDoc('== Non String ==')).rejects.toThrow(
      new TypeError('Asciidoctor conversion did not return a string: number'),
    );
  });
});
