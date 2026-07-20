/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { mkdirSync, mkdtempSync, readdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { ASCIIDOCTOR_DEFAULT_OPTIONS, convertAsciiDoc } from '../src/core/asciidoctor';
import renderer from '../src/core/renderer';

const originalCwd = process.cwd();
const temporaryDirectories: string[] = [];

const createTemporaryDirectory = (): string => {
  const directory = mkdtempSync(path.join(tmpdir(), 'hexo-renderer-asciidoc-characterization-'));
  temporaryDirectories.push(directory);
  return directory;
};

afterEach(() => {
  process.chdir(originalCwd);
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true });
  }
});

describe.sequential('Asciidoctor v3 filesystem characterization', () => {
  it('uses the server safe mode and does not set a default base_dir', () => {
    expect(ASCIIDOCTOR_DEFAULT_OPTIONS).toMatchObject({
      attributes: ['source-highlighter=html-pipeline'],
      doctype: 'article',
      safe: 'server',
    });
    expect(ASCIIDOCTOR_DEFAULT_OPTIONS).not.toHaveProperty('base_dir');
    expect(ASCIIDOCTOR_DEFAULT_OPTIONS).not.toHaveProperty('to_file');
  });

  it('resolves a local include from the conversion-time CWD', () => {
    const workingDirectory = createTemporaryDirectory();
    writeFileSync(path.join(workingDirectory, 'included.adoc'), 'INCLUDED_FROM_CWD');
    process.chdir(workingDirectory);

    const result = renderer({ text: 'include::included.adoc[]' });

    expect(result).toContain('INCLUDED_FROM_CWD');
  });

  it('allows an explicit base_dir override only through the conversion seam', () => {
    const workingDirectory = createTemporaryDirectory();
    const baseDirectory = createTemporaryDirectory();
    writeFileSync(path.join(workingDirectory, 'included.adoc'), 'FROM_CWD');
    writeFileSync(path.join(baseDirectory, 'included.adoc'), 'FROM_BASE_DIR');
    process.chdir(workingDirectory);

    const fromCwd = convertAsciiDoc('include::included.adoc[]');
    const fromBaseDirectory = convertAsciiDoc('include::included.adoc[]', {
      base_dir: baseDirectory,
    });

    expect(fromCwd).toContain('FROM_CWD');
    expect(fromBaseDirectory).toContain('FROM_BASE_DIR');
    expect(fromBaseDirectory).not.toContain('FROM_CWD');
  });

  it('keeps the to_file false experiment separate and does not create output files for string input', () => {
    const workingDirectory = createTemporaryDirectory();
    process.chdir(workingDirectory);

    const html = convertAsciiDoc('== Controlled to_file false ==', { to_file: false });

    expect(html).toContain('<h2 id="_controlled_to_file_false">Controlled to_file false</h2>');
    expect(readdirSync(workingDirectory)).toEqual([]);
  });

  it('is invariant across absent, null, relative, and absolute data.path values', () => {
    const workingDirectory = createTemporaryDirectory();
    writeFileSync(path.join(workingDirectory, 'included.adoc'), 'PATH_INVARIANT_INCLUDE');
    process.chdir(workingDirectory);
    const text = 'include::included.adoc[]';

    const outputs = [
      renderer({ text }),
      renderer({ text, path: null }),
      renderer({ text, path: 'nested/document.adoc' }),
      renderer({ text, path: path.join(workingDirectory, 'document.adoc') }),
    ];

    expect(new Set(outputs).size).toBe(1);
    expect(outputs[0]).toContain('PATH_INVARIANT_INCLUDE');
  });

  it('changes relative include resolution when only the CWD changes', () => {
    const firstWorkingDirectory = createTemporaryDirectory();
    const secondWorkingDirectory = createTemporaryDirectory();
    writeFileSync(path.join(firstWorkingDirectory, 'included.adoc'), 'FIRST_CWD');
    writeFileSync(path.join(secondWorkingDirectory, 'included.adoc'), 'SECOND_CWD');

    process.chdir(firstWorkingDirectory);
    const first = renderer({ text: 'include::included.adoc[]' });
    process.chdir(secondWorkingDirectory);
    const second = renderer({ text: 'include::included.adoc[]' });

    expect(first).toContain('FIRST_CWD');
    expect(first).not.toContain('SECOND_CWD');
    expect(second).toContain('SECOND_CWD');
    expect(second).not.toContain('FIRST_CWD');
  });

  it('denies traversal and absolute paths while retaining symlink include behavior', () => {
    const root = createTemporaryDirectory();
    const workingDirectory = path.join(root, 'working');
    const outsideDirectory = path.join(root, 'outside');
    mkdirSync(workingDirectory);
    mkdirSync(outsideDirectory);
    const outsideFile = path.join(outsideDirectory, 'outside.adoc');
    writeFileSync(outsideFile, 'OUTSIDE_INCLUDE_SENTINEL');
    symlinkSync(outsideFile, path.join(workingDirectory, 'linked.adoc'));
    process.chdir(workingDirectory);

    const traversal = renderer({ text: 'include::../outside/outside.adoc[]' });
    const absolute = renderer({ text: `include::${outsideFile}[]` });
    const symlink = renderer({ text: 'include::linked.adoc[]' });

    expect(traversal).toContain('Unresolved directive');
    expect(traversal).not.toContain('OUTSIDE_INCLUDE_SENTINEL');
    expect(absolute).toContain('Unresolved directive');
    expect(absolute).not.toContain('OUTSIDE_INCLUDE_SENTINEL');
    expect(symlink).toContain('OUTSIDE_INCLUDE_SENTINEL');
  });
});
