/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import fs, { mkdirSync, mkdtempSync, readdirSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import path from 'node:path';
import type { AbstractBlock, BlockProcessor, Reader, Registry } from '@asciidoctor/core';
import { Extensions, Logger } from '@asciidoctor/core';
import Hexo from 'hexo';
import { afterEach, describe, expect, it } from 'vitest';
import { convertAsciiDoc } from '../src/core/asciidoctor';
import renderer from '../src/core/renderer';
import registerRenderer from '../src/hexo/register';
import type { Hexo as HexoContract } from '../src/types';

const originalCwd = process.cwd();
const temporaryDirectories: string[] = [];
const SUPPORTED_EXTENSIONS = ['ad', 'adoc', 'asciidoc'] as const;

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

const HexoClass = Hexo as unknown as HexoConstructor;

const createTemporaryDirectory = (): string => {
  const directory = mkdtempSync(path.join(tmpdir(), 'hexo-renderer-asciidoc-runtime-'));
  temporaryDirectories.push(directory);
  return directory;
};

const createHexoWorkspace = (): string => {
  const directory = createTemporaryDirectory();
  writeFileSync(path.join(directory, '_config.yml'), 'title: hexo-renderer-asciidoc\n');
  return directory;
};

type LoopbackRequestRecord = {
  method: string;
  timestamp: string;
  url: string;
};

/**
 * Evidence record for a single include directive: paths extracted from Asciidoctor's
 * diagnostic stderr messages and the canonical filesystem path (from realpathSync).
 *
 * Asciidoctor v4 emits exact resolved paths in its error messages when includes are
 * denied or missing, providing direct evidence without inferring from output HTML.
 */
type IncludePathEvidence = {
  /**
   * The path Asciidoctor computed (after jail normalization) before the jail check.
   * Extracted from the stderr diagnostic message for denied/missing includes.
   */
  jailNormalizedPath: string | null;
  /** Whether the include was explicitly denied by the server-mode jail. */
  denied: boolean;
  /** Exact denial message from stderr (e.g. "illegal reference to ancestor of jail"). */
  denialMessage: string | null;
  /**
   * Canonical realpath from `realpathSync` at test-setup time.
   * For symlinks this is the target file's real path; null when the path does not exist.
   */
  canonicalPath: string | null;
  /** The path string the include directive was given (from test setup). */
  requestedPath: string;
};

/**
 * Parse the jail-normalized path from an Asciidoctor stderr diagnostic message.
 *
 * Asciidoctor v4 formats include diagnostics as:
 *   asciidoctor: ERROR: <stdin>: line N: include file not found: /abs/path
 *   asciidoctor: ERROR: <stdin>: line N: illegal reference to ancestor of jail: /abs/path
 *   asciidoctor: ERROR: <stdin>: line N: outside of jail: /abs/path
 */
const parseIncludePathFromStderr = (stderr: string): string | null => {
  const match = /(?:include file not found|illegal reference to ancestor of jail|outside of jail): (.+)/.exec(stderr);
  return match?.[1]?.trim() ?? null;
};

/**
 * Build an IncludePathEvidence record from the rendered result, captured stderr,
 * and the test-setup knowledge of what path was requested.
 */
const buildIncludePathEvidence = ({
  denied,
  requestedPath,
  stderr,
}: {
  denied: boolean;
  requestedPath: string;
  stderr: string;
}): IncludePathEvidence => {
  const denialMessages = ['include file not found', 'illegal reference to ancestor of jail', 'outside of jail'];
  const denialLine = stderr.split('\n').find((line) => denialMessages.some((msg) => line.includes(msg))) ?? null;
  const denialMessage = denialLine?.trim() ?? null;
  const jailNormalizedPath = parseIncludePathFromStderr(stderr);

  let canonicalPath: string | null = null;
  try {
    canonicalPath =
      typeof fs.realpathSync.native === 'function'
        ? fs.realpathSync.native(requestedPath)
        : fs.realpathSync(requestedPath);
  } catch {
    canonicalPath = null;
  }

  return {
    canonicalPath,
    denied,
    denialMessage,
    jailNormalizedPath,
    requestedPath,
  };
};

const drainPendingTasks = async (): Promise<void> => {
  await Promise.resolve();
  await new Promise<void>((resolve) => setImmediate(resolve));
};

const withCapturedStderr = async <T>(callback: () => Promise<T>): Promise<{ result: T; stderr: string }> => {
  const originalWrite = process.stderr.write.bind(process.stderr);
  let stderr = '';

  process.stderr.write = ((chunk: string | Uint8Array) => {
    stderr += typeof chunk === 'string' ? chunk : Buffer.from(chunk).toString('utf8');
    return true;
  }) as typeof process.stderr.write;

  try {
    return {
      result: await callback(),
      stderr,
    };
  } finally {
    process.stderr.write = originalWrite as typeof process.stderr.write;
  }
};

const formatLoopbackRequests = (requests: LoopbackRequestRecord[]): string =>
  [
    'unexpected loopback requests reached the include server:',
    ...requests.map((request) => `- ${request.timestamp} ${request.method} ${request.url}`),
  ].join('\n');

const withHexo = async <T>(baseDirectory: string, callback: (hexo: HexoTestInstance) => Promise<T>): Promise<T> => {
  const hexo = new HexoClass(baseDirectory, { silent: true, debug: false });
  registerRenderer(hexo);

  try {
    await hexo.init();
    return await callback(hexo);
  } finally {
    await drainPendingTasks();
    await hexo.exit();
    await drainPendingTasks();
  }
};

const createSourceDocument = (marker: string, language: string): string => `
== ${marker}

[source,${language}]
----
const ${marker.replaceAll('-', '_')} = { lang: '${language}', marker: '${marker}' };
----
`;

type OverlapRenderCase = {
  callId: string;
  contentSentinel: string;
  errorSentinel?: string;
  heading: string;
  language: string;
  loggerSentinel: string;
  optionSentinel: string;
};

type LoggerLike = {
  warn(message: string): void;
};

type ReaderWithLogger = Reader & {
  getLogger(): LoggerLike;
};

type DocumentWithAttributes = {
  getAttribute(name: string): unknown;
};

type ParentWithDocument = AbstractBlock & {
  getDocument(): DocumentWithAttributes;
};

type OverlapBarrierRegistration = {
  dispose: () => void;
  getActiveCount: () => number;
  getLoggers: () => ReadonlyMap<string, LoggerLike>;
  getMaxActiveCount: () => number;
};

const OVERLAP_BLOCK_NAME = 'unit2overlap';
let overlapExtensionSequence = 0;

const countMatches = (value: string, pattern: RegExp): number => value.match(pattern)?.length ?? 0;

const withObservedCoreLoggerWarnings = async <T>(
  callback: () => Promise<T>,
): Promise<{ result: T; warningsByLogger: ReadonlyMap<Logger, readonly string[]> }> => {
  const originalAdd = Logger.prototype.add;
  const warningsByLogger = new Map<Logger, string[]>();

  Logger.prototype.add = function observedLoggerAdd(...args: Parameters<Logger['add']>): boolean {
    const result = originalAdd.apply(this, args);
    const message = args[1];
    if (typeof message === 'string' && message.startsWith('UNIT2_LOG_SENTINEL:')) {
      const warnings = warningsByLogger.get(this) ?? [];
      warnings.push(message);
      warningsByLogger.set(this, warnings);
    }
    return result;
  };

  try {
    return {
      result: await callback(),
      warningsByLogger,
    };
  } finally {
    Logger.prototype.add = originalAdd;
  }
};

const createOverlapDocument = ({
  callId,
  contentSentinel,
  errorSentinel,
  heading,
  language,
  loggerSentinel,
  optionSentinel,
}: OverlapRenderCase): string =>
  [
    `:unit2-call-id: ${callId}`,
    `:unit2-option-sentinel: ${optionSentinel}`,
    `:unit2-logger-sentinel: ${loggerSentinel}`,
    ...(errorSentinel ? [`:unit2-error-sentinel: ${errorSentinel}`] : []),
    '',
    `== ${heading}`,
    '',
    `[${OVERLAP_BLOCK_NAME}]`,
    contentSentinel,
    '',
    `[source,${language}]`,
    '----',
    `const ${callId.replaceAll('-', '_')} = { content: '${contentSentinel}', option: '${optionSentinel}', logger: '${loggerSentinel}' };`,
    '----',
  ].join('\n');

const registerOverlapBarrier = (expectedCount: number): OverlapBarrierRegistration => {
  overlapExtensionSequence += 1;
  const extensionName = `unit2-overlap-barrier-${overlapExtensionSequence}`;
  let activeCount = 0;
  let maxActiveCount = 0;
  const loggers = new Map<string, LoggerLike>();
  let settleBarrier: ((error?: Error) => void) | undefined;
  const barrier = new Promise<void>((resolve, reject) => {
    settleBarrier = (error?: Error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    };
  });
  const barrierTimeout = setTimeout(() => {
    settleBarrier?.(new Error(`only ${activeCount} conversions reached the overlap barrier`));
  }, 5_000);
  const OverlapBlock = Extensions.createBlockProcessor('Unit2OverlapBarrier', {
    async process(this: BlockProcessor, parent: AbstractBlock, reader: Reader, attributes: Record<string, unknown>) {
      const document = (parent as ParentWithDocument).getDocument();
      const callId = String(document.getAttribute('unit2-call-id') ?? '');
      const optionSentinel = String(document.getAttribute('unit2-option-sentinel') ?? '');
      const loggerSentinel = String(document.getAttribute('unit2-logger-sentinel') ?? '');
      const errorSentinel = document.getAttribute('unit2-error-sentinel');
      const logger =
        typeof (reader as Partial<ReaderWithLogger>).getLogger === 'function'
          ? (reader as ReaderWithLogger).getLogger()
          : undefined;
      if (!logger) {
        throw new TypeError(`conversion ${callId} did not expose its core logger`);
      }
      loggers.set(callId, logger);
      const barrierText = (await reader.readLines()).join('\n');

      logger.warn(`UNIT2_LOG_SENTINEL:${loggerSentinel}`);
      activeCount += 1;
      maxActiveCount = Math.max(maxActiveCount, activeCount);
      if (activeCount === expectedCount) {
        clearTimeout(barrierTimeout);
        settleBarrier?.();
      }

      try {
        await barrier;
        if (typeof errorSentinel === 'string' && errorSentinel.length > 0) {
          throw new Error(`UNIT2_ERROR_SENTINEL:${errorSentinel}`);
        }

        return this.createParagraph(parent, `${barrierText}|OPTION:${optionSentinel}|CALL:${callId}`, attributes);
      } finally {
        activeCount -= 1;
      }
    },
  });
  OverlapBlock.option('name', OVERLAP_BLOCK_NAME);
  OverlapBlock.option('contexts', ['paragraph']);
  OverlapBlock.option('content_model', 'simple');
  Extensions.register(extensionName, function registerBarrier(this: Registry) {
    this.block(OverlapBlock);
  });

  return {
    dispose: () => {
      clearTimeout(barrierTimeout);
      settleBarrier?.(new Error('overlap barrier disposed before completion'));
      Extensions.unregister(extensionName);
    },
    getActiveCount: () => activeCount,
    getLoggers: () => loggers,
    getMaxActiveCount: () => maxActiveCount,
  };
};

const withRegisteredOverlapBarrier = async <T>(
  expectedCount: number,
  callback: (registration: OverlapBarrierRegistration) => Promise<T>,
): Promise<T> => {
  const registration = registerOverlapBarrier(expectedCount);
  try {
    return await callback(registration);
  } finally {
    registration.dispose();
  }
};

afterEach(() => {
  process.chdir(originalCwd);
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true });
  }
});

describe.sequential('Asciidoctor v4 runtime worker', () => {
  it('returns string output without writing files', async () => {
    const workingDirectory = createTemporaryDirectory();
    process.chdir(workingDirectory);

    const { result, stderr } = await withCapturedStderr(() => convertAsciiDoc('== Controlled to_file false =='));

    expect(result).toContain('<h2 id="_controlled_to_file_false">Controlled to_file false</h2>');
    expect(readdirSync(workingDirectory)).toEqual([]);
    expect(stderr).toBe('');
  });

  it('renders the production renderer in a fresh process for cold-start evidence', async () => {
    const marker = 'cold-start-production-renderer';
    const result = await renderer({ text: createSourceDocument(marker, 'javascript') });

    expect(result).toContain(`<h2 id="_${marker.replaceAll('-', '_')}">${marker}</h2>`);
    expect(result).toContain('<code class="highlight javascript">');
    expect(result).toContain(marker.replaceAll('-', '_'));
    expect(result).not.toContain('[object Promise]');
  });

  it('keeps include resolution tied to the active CWD while ignoring data.path and Hexo site root', async () => {
    const workingDirectory = createTemporaryDirectory();
    const firstSiteRoot = createHexoWorkspace();
    const secondSiteRoot = createHexoWorkspace();
    const text = 'include::included.adoc[]';

    writeFileSync(path.join(workingDirectory, 'included.adoc'), 'FROM_CWD');
    writeFileSync(path.join(firstSiteRoot, 'included.adoc'), 'FROM_FIRST_SITE_ROOT');
    writeFileSync(path.join(secondSiteRoot, 'included.adoc'), 'FROM_SECOND_SITE_ROOT');
    process.chdir(workingDirectory);

    // Evidence: the expected include path is `{workingDirectory}/included.adoc`.
    // The three other directories hold distinct sentinels so the output proves which
    // file was read without ambiguity.
    const expectedIncludePath = path.join(workingDirectory, 'included.adoc');
    expect(fs.realpathSync(expectedIncludePath)).toBe(expectedIncludePath);

    const directOutputs = await Promise.all([
      renderer({ text }),
      renderer({ text, path: null }),
      renderer({ text, path: 'nested/document.adoc' }),
      renderer({ text, path: path.join(workingDirectory, 'document.adoc') }),
    ]);

    expect(new Set(directOutputs).size).toBe(1);
    for (const output of directOutputs) {
      expect(output).toContain('FROM_CWD');
      expect(output).not.toContain('FROM_FIRST_SITE_ROOT');
      expect(output).not.toContain('FROM_SECOND_SITE_ROOT');
    }

    const [firstSiteOutput, secondSiteOutput] = await Promise.all([
      withHexo(firstSiteRoot, async (hexo) => await hexo.render.render({ text, engine: 'adoc' })),
      withHexo(secondSiteRoot, async (hexo) => await hexo.render.render({ text, engine: 'adoc' })),
    ]);

    expect(firstSiteOutput).toContain('FROM_CWD');
    expect(firstSiteOutput).not.toContain('FROM_FIRST_SITE_ROOT');
    expect(secondSiteOutput).toContain('FROM_CWD');
    expect(secondSiteOutput).not.toContain('FROM_SECOND_SITE_ROOT');
    expect(firstSiteOutput).toBe(secondSiteOutput);
  });

  it('changes relative include resolution when only the active CWD changes', async () => {
    const firstWorkingDirectory = createTemporaryDirectory();
    const secondWorkingDirectory = createTemporaryDirectory();
    writeFileSync(path.join(firstWorkingDirectory, 'included.adoc'), 'FIRST_CWD');
    writeFileSync(path.join(secondWorkingDirectory, 'included.adoc'), 'SECOND_CWD');

    process.chdir(firstWorkingDirectory);
    const first = await renderer({ text: 'include::included.adoc[]' });
    process.chdir(secondWorkingDirectory);
    const second = await renderer({ text: 'include::included.adoc[]' });

    expect(first).toContain('FIRST_CWD');
    expect(first).not.toContain('SECOND_CWD');
    expect(second).toContain('SECOND_CWD');
    expect(second).not.toContain('FIRST_CWD');
  });

  it('captures missing, traversal, absolute, and symlink include behavior', async () => {
    const root = createTemporaryDirectory();
    const workingDirectory = path.join(root, 'working');
    const outsideDirectory = path.join(root, 'outside');
    mkdirSync(workingDirectory);
    mkdirSync(outsideDirectory);

    const outsideFile = path.join(outsideDirectory, 'outside.adoc');
    writeFileSync(outsideFile, 'OUTSIDE_INCLUDE_SENTINEL');
    symlinkSync(outsideFile, path.join(workingDirectory, 'linked.adoc'));
    process.chdir(workingDirectory);

    // Pre-compute canonical paths from test-setup to document expected evidence.
    // For denied cases: Asciidoctor logs the exact resolved path in stderr.
    // For symlink: realpathSync exposes the real target.
    const symLinkPath = path.join(workingDirectory, 'linked.adoc');
    const symLinkRealPath = fs.realpathSync(symLinkPath);
    expect(symLinkRealPath).toBe(outsideFile);

    const includeCases = [
      {
        denied: false,
        requestedPath: path.join(workingDirectory, 'missing.adoc'),
        key: 'missing',
        text: 'include::missing.adoc[]',
      },
      {
        denied: true,
        requestedPath: path.join(workingDirectory, '../outside/outside.adoc'),
        key: 'traversal',
        text: 'include::../outside/outside.adoc[]',
      },
      {
        denied: true,
        requestedPath: outsideFile,
        key: 'absolute',
        text: `include::${outsideFile}[]`,
      },
      {
        denied: false,
        requestedPath: symLinkPath,
        key: 'symlink',
        text: 'include::linked.adoc[]',
      },
    ] as const;

    const stderrByCase: Record<(typeof includeCases)[number]['key'], string> = {
      absolute: '',
      missing: '',
      symlink: '',
      traversal: '',
    };
    const renderedByCase: Record<(typeof includeCases)[number]['key'], string> = {
      absolute: '',
      missing: '',
      symlink: '',
      traversal: '',
    };
    const evidenceByCase: Record<(typeof includeCases)[number]['key'], IncludePathEvidence> = {
      absolute: {
        canonicalPath: null,
        denied: true,
        denialMessage: null,
        jailNormalizedPath: null,
        requestedPath: outsideFile,
      },
      missing: {
        canonicalPath: null,
        denied: false,
        denialMessage: null,
        jailNormalizedPath: null,
        requestedPath: path.join(workingDirectory, 'missing.adoc'),
      },
      symlink: {
        canonicalPath: null,
        denied: false,
        denialMessage: null,
        jailNormalizedPath: null,
        requestedPath: symLinkPath,
      },
      traversal: {
        canonicalPath: null,
        denied: true,
        denialMessage: null,
        jailNormalizedPath: null,
        requestedPath: path.join(workingDirectory, '../outside/outside.adoc'),
      },
    };

    for (const includeCase of includeCases) {
      const { result: rendered, stderr } = await withCapturedStderr(
        async () => await renderer({ text: includeCase.text }),
      );

      stderrByCase[includeCase.key] = stderr;
      renderedByCase[includeCase.key] = rendered;
      evidenceByCase[includeCase.key] = buildIncludePathEvidence({
        denied: includeCase.denied,
        requestedPath: includeCase.requestedPath,
        stderr,
      });
    }

    // Output sentinel checks (retained separately from path evidence).
    expect(renderedByCase.missing).toContain('Unresolved directive');
    expect(renderedByCase.missing).not.toContain('OUTSIDE_INCLUDE_SENTINEL');
    expect(renderedByCase.traversal).toContain('Unresolved directive');
    expect(renderedByCase.traversal).not.toContain('OUTSIDE_INCLUDE_SENTINEL');
    expect(renderedByCase.absolute).toContain('Unresolved directive');
    expect(renderedByCase.absolute).not.toContain('OUTSIDE_INCLUDE_SENTINEL');
    expect(renderedByCase.symlink).toContain('OUTSIDE_INCLUDE_SENTINEL');

    // Exact path evidence from Asciidoctor's stderr diagnostics.
    expect(evidenceByCase.missing.jailNormalizedPath).toBe(path.join(workingDirectory, 'missing.adoc'));
    expect(evidenceByCase.missing.denied).toBe(false);
    expect(evidenceByCase.missing.canonicalPath).toBeNull();
    expect(evidenceByCase.missing.denialMessage).toContain('include file not found');

    expect(evidenceByCase.traversal.jailNormalizedPath).not.toBeNull();
    expect(evidenceByCase.traversal.denied).toBe(true);
    // The file exists outside the jail; realpathSync resolves to the real target.
    expect(evidenceByCase.traversal.canonicalPath).toBe(outsideFile);
    expect(evidenceByCase.traversal.denialMessage).toContain('illegal reference to ancestor of jail');

    expect(evidenceByCase.absolute.jailNormalizedPath).not.toBeNull();
    expect(evidenceByCase.absolute.denied).toBe(true);
    // The file exists outside the jail; realpathSync resolves to the real target.
    expect(evidenceByCase.absolute.canonicalPath).toBe(outsideFile);
    expect(evidenceByCase.absolute.denialMessage).toContain('outside of jail');

    // Symlink: no denial, canonical path resolves to the real target.
    expect(evidenceByCase.symlink.denied).toBe(false);
    expect(evidenceByCase.symlink.denialMessage).toBeNull();
    expect(evidenceByCase.symlink.canonicalPath).toBe(outsideFile);
    expect(evidenceByCase.symlink.requestedPath).toBe(symLinkPath);

    // Stderr diagnostic checks.
    expect(stderrByCase.missing).toContain('include file not found');
    expect(stderrByCase.traversal).toContain('illegal reference to ancestor of jail');
    expect(stderrByCase.absolute).toContain('outside of jail');
    expect(stderrByCase.symlink).not.toContain('linked.adoc');
  });

  it('never issues unexpected loopback URI requests even when allow-uri-read is declared', async () => {
    const requests: LoopbackRequestRecord[] = [];
    const server = createServer((request, response) => {
      requests.push({
        method: request.method ?? 'UNKNOWN',
        timestamp: new Date().toISOString(),
        url: request.url ?? '<empty>',
      });
      response.writeHead(403, { 'content-type': 'text/plain; charset=utf-8' });
      response.end('REMOTE_INCLUDE_REQUEST_FORBIDDEN');
    });

    await new Promise<void>((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', () => resolve());
    });

    const address = server.address();
    if (!address || typeof address === 'string') {
      await new Promise<void>((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
      throw new TypeError('expected an ephemeral loopback server address');
    }

    const withoutAllowUriReadUri = `http://127.0.0.1:${address.port}/case-no-allow/remote.adoc`;
    const withAllowUriReadUri = `http://127.0.0.1:${address.port}/case-with-allow/remote.adoc`;

    let result: { withoutAllowUriRead: string; withAllowUriRead: string };
    let stderr: string;
    try {
      ({ result, stderr } = await withCapturedStderr(async () => ({
        withoutAllowUriRead: await renderer({ text: `include::${withoutAllowUriReadUri}[]` }),
        withAllowUriRead: await renderer({ text: `:allow-uri-read:\n\ninclude::${withAllowUriReadUri}[]` }),
      })));
    } finally {
      await new Promise<void>((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
    }

    expect(result.withoutAllowUriRead).toContain(withoutAllowUriReadUri);
    expect(result.withAllowUriRead).toContain(withAllowUriReadUri);
    expect(result.withoutAllowUriRead).not.toContain('REMOTE_INCLUDE_REQUEST_FORBIDDEN');
    expect(result.withAllowUriRead).not.toContain('REMOTE_INCLUDE_REQUEST_FORBIDDEN');
    expect(stderr.match(/allow-uri-read attribute not enabled/g)?.length).toBe(2);
    if (requests.length > 0) {
      throw new Error(formatLoopbackRequests(requests));
    }
  });

  it('keeps multiple outstanding conversion and renderer promises isolated', async () => {
    const renderCases = [
      { marker: 'parallel-javascript', language: 'javascript' },
      { marker: 'parallel-ruby', language: 'ruby' },
      { marker: 'parallel-python', language: 'python' },
    ] as const;
    const extensionName = 'unit2-concurrency-barrier';
    let conversionsAtBarrier = 0;
    let maximumConversionsAtBarrier = 0;
    let releaseBarrier: (() => void) | undefined;
    let rejectBarrier: ((reason: Error) => void) | undefined;
    const barrier = new Promise<void>((resolve, reject) => {
      releaseBarrier = resolve;
      rejectBarrier = reject;
    });
    const barrierTimeout = setTimeout(() => {
      rejectBarrier?.(new Error(`only ${conversionsAtBarrier} conversions reached the concurrency barrier`));
    }, 5_000);
    const BarrierBlock = Extensions.createBlockProcessor('Unit2ConcurrencyBarrier', {
      async process(this: BlockProcessor, parent: AbstractBlock, reader: Reader, attributes: Record<string, unknown>) {
        conversionsAtBarrier += 1;
        maximumConversionsAtBarrier = Math.max(maximumConversionsAtBarrier, conversionsAtBarrier);
        if (conversionsAtBarrier === renderCases.length) {
          clearTimeout(barrierTimeout);
          releaseBarrier?.();
        }
        await barrier;
        conversionsAtBarrier -= 1;
        return this.createParagraph(parent, (await reader.readLines()).join('\n'), attributes);
      },
    });
    BarrierBlock.option('name', 'unit2barrier');
    BarrierBlock.option('contexts', ['paragraph']);
    BarrierBlock.option('content_model', 'simple');
    Extensions.register(extensionName, function registerBarrier(this: Registry) {
      this.block(BarrierBlock);
    });

    let renderOutputs: string[];
    try {
      renderOutputs = await Promise.all(
        renderCases.map(({ marker, language }) =>
          renderer({
            text: `[unit2barrier]\nBARRIER_${marker}\n\n${createSourceDocument(marker, language)}`,
          }),
        ),
      );
    } finally {
      clearTimeout(barrierTimeout);
      Extensions.unregister(extensionName);
    }

    expect(maximumConversionsAtBarrier).toBe(renderCases.length);
    expect(conversionsAtBarrier).toBe(0);

    for (const [index, output] of renderOutputs.entries()) {
      const renderCase = renderCases[index];
      if (!renderCase) {
        throw new RangeError(`missing render case at index ${index}`);
      }
      const { marker, language } = renderCase;
      expect(output).toContain(`<h2 id="_${marker.replaceAll('-', '_')}">${marker}</h2>`);
      expect(output).toContain(`<code class="highlight ${language}">`);
      expect(output).toContain(marker.replaceAll('-', '_'));
      expect(output).toContain(`BARRIER_${marker}`);
      expect(output).not.toContain('[object Promise]');
      for (const otherCase of renderCases) {
        if (otherCase !== renderCase) {
          expect(output).not.toContain(otherCase.marker);
        }
      }
    }
  });

  it('proves strict real-runtime overlap and isolation across a mixed batch with sentinels', async () => {
    const successCases = [
      {
        callId: 'unit2-batch-javascript',
        contentSentinel: 'CONTENT_SENTINEL_A',
        heading: 'Unit2 Batch Javascript',
        language: 'javascript',
        loggerSentinel: 'LOGGER_SENTINEL_A',
        optionSentinel: 'OPTION_SENTINEL_A',
      },
      {
        callId: 'unit2-batch-ruby',
        contentSentinel: 'CONTENT_SENTINEL_B',
        heading: 'Unit2 Batch Ruby',
        language: 'ruby',
        loggerSentinel: 'LOGGER_SENTINEL_B',
        optionSentinel: 'OPTION_SENTINEL_B',
      },
      {
        callId: 'unit2-batch-python',
        contentSentinel: 'CONTENT_SENTINEL_C',
        heading: 'Unit2 Batch Python',
        language: 'python',
        loggerSentinel: 'LOGGER_SENTINEL_C',
        optionSentinel: 'OPTION_SENTINEL_C',
      },
    ] satisfies [OverlapRenderCase, OverlapRenderCase, OverlapRenderCase];
    const failureCase: OverlapRenderCase = {
      callId: 'unit2-batch-failure',
      contentSentinel: 'CONTENT_SENTINEL_FAILURE',
      errorSentinel: 'ERROR_SENTINEL_FAILURE',
      heading: 'Unit2 Batch Failure',
      language: 'json',
      loggerSentinel: 'LOGGER_SENTINEL_FAILURE',
      optionSentinel: 'OPTION_SENTINEL_FAILURE',
    };
    const mixedBatchCases: OverlapRenderCase[] = [successCases[0], failureCase, successCases[1], successCases[2]];

    const expectedSuccessOutputs = new Map<string, string>();
    for (const overlapCase of successCases) {
      const { result } = await withCapturedStderr(
        async () =>
          await withRegisteredOverlapBarrier(1, async () => {
            return await renderer({ text: createOverlapDocument(overlapCase) });
          }),
      );
      expectedSuccessOutputs.set(overlapCase.callId, result);
    }
    const { result: expectedFailureReason } = await withCapturedStderr(
      async () =>
        await withRegisteredOverlapBarrier(1, async () => {
          try {
            await renderer({ text: createOverlapDocument(failureCase) });
          } catch (error) {
            return error as Error;
          }

          throw new TypeError('expected the overlap failure case to reject');
        }),
    );

    const {
      result: { result: mixedResults, stderr: mixedStderr },
      warningsByLogger,
    } = await withObservedCoreLoggerWarnings(
      async () =>
        await withCapturedStderr(
          async () =>
            await withRegisteredOverlapBarrier(mixedBatchCases.length, async (registration) => {
              const settled = await Promise.allSettled(
                mixedBatchCases.map((overlapCase) => renderer({ text: createOverlapDocument(overlapCase) })),
              );

              expect(registration.getMaxActiveCount()).toBe(mixedBatchCases.length);
              expect(registration.getActiveCount()).toBe(0);
              return { loggerByCall: new Map(registration.getLoggers()), settled };
            }),
        ),
    );

    expect(mixedResults.settled).toHaveLength(mixedBatchCases.length);

    for (const overlapCase of mixedBatchCases) {
      expect(countMatches(mixedStderr, new RegExp(`UNIT2_LOG_SENTINEL:${overlapCase.loggerSentinel}`, 'g'))).toBe(1);
    }

    expect(new Set(mixedResults.loggerByCall.values()).size).toBe(mixedBatchCases.length);
    for (const overlapCase of mixedBatchCases) {
      const logger = mixedResults.loggerByCall.get(overlapCase.callId);
      expect(logger).toBeInstanceOf(Logger);
      if (!(logger instanceof Logger)) {
        throw new TypeError(`missing core logger identity for ${overlapCase.callId}`);
      }
      expect(warningsByLogger.get(logger)).toEqual([`UNIT2_LOG_SENTINEL:${overlapCase.loggerSentinel}`]);
      for (const otherCase of mixedBatchCases) {
        if (otherCase !== overlapCase) {
          expect(warningsByLogger.get(logger)?.every((warning) => !warning.includes(otherCase.loggerSentinel))).toBe(
            true,
          );
        }
      }
    }

    for (const [index, mixedResult] of mixedResults.settled.entries()) {
      const overlapCase = mixedBatchCases[index];
      if (!overlapCase) {
        throw new RangeError(`missing overlap case at index ${index}`);
      }

      if (overlapCase.errorSentinel) {
        expect(mixedResult).toMatchObject({ status: 'rejected' });
        if (mixedResult.status !== 'rejected') {
          throw new TypeError('expected a rejected overlap result');
        }
        expect(mixedResult.reason).toBeInstanceOf(Error);
        expect(mixedResult.reason.name).toBe(expectedFailureReason.name);
        expect(mixedResult.reason.message).toBe(expectedFailureReason.message);
        expect(mixedResult.reason.message).toContain(`UNIT2_ERROR_SENTINEL:${overlapCase.errorSentinel}`);
        for (const otherCase of mixedBatchCases) {
          if (otherCase !== overlapCase) {
            expect(mixedResult.reason.message).not.toContain(otherCase.contentSentinel);
            expect(mixedResult.reason.message).not.toContain(otherCase.optionSentinel);
            expect(mixedResult.reason.message).not.toContain(otherCase.loggerSentinel);
            if (otherCase.errorSentinel) {
              expect(mixedResult.reason.message).not.toContain(otherCase.errorSentinel);
            }
          }
        }
        continue;
      }

      expect(mixedResult).toMatchObject({ status: 'fulfilled' });
      if (mixedResult.status !== 'fulfilled') {
        throw new TypeError('expected a fulfilled overlap result');
      }

      const expectedOutput = expectedSuccessOutputs.get(overlapCase.callId);
      if (!expectedOutput) {
        throw new RangeError(`missing expected overlap output for ${overlapCase.callId}`);
      }

      expect(mixedResult.value).toBe(expectedOutput);
      expect(mixedResult.value).toContain(
        `<h2 id="_${overlapCase.heading.toLowerCase().replaceAll(' ', '_')}">${overlapCase.heading}</h2>`,
      );
      expect(mixedResult.value).toContain(`<code class="highlight ${overlapCase.language}">`);
      expect(mixedResult.value).toContain(overlapCase.contentSentinel);
      expect(mixedResult.value).toContain(overlapCase.optionSentinel);
      expect(mixedResult.value).toContain(overlapCase.callId.replaceAll('-', '_'));
      expect(mixedResult.value).not.toContain('[object Promise]');
      for (const otherCase of mixedBatchCases) {
        if (otherCase === overlapCase) {
          continue;
        }

        expect(mixedResult.value).not.toContain(otherCase.contentSentinel);
        expect(mixedResult.value).not.toContain(otherCase.optionSentinel);
        expect(mixedResult.value).not.toContain(otherCase.loggerSentinel);
        if (otherCase.errorSentinel) {
          expect(mixedResult.value).not.toContain(otherCase.errorSentinel);
        }
      }
    }

    const recoveryCase: OverlapRenderCase = {
      callId: 'unit2-post-failure-success',
      contentSentinel: 'CONTENT_SENTINEL_RECOVERY',
      heading: 'Unit2 Post Failure Success',
      language: 'yaml',
      loggerSentinel: 'LOGGER_SENTINEL_RECOVERY',
      optionSentinel: 'OPTION_SENTINEL_RECOVERY',
    };
    const { result: recoveryOutput, stderr: recoveryStderr } = await withCapturedStderr(
      async () =>
        await withRegisteredOverlapBarrier(1, async () => {
          return await renderer({ text: createOverlapDocument(recoveryCase) });
        }),
    );

    expect(countMatches(recoveryStderr, /UNIT2_LOG_SENTINEL:LOGGER_SENTINEL_RECOVERY/g)).toBe(1);
    expect(recoveryOutput).toContain('<h2 id="_unit2_post_failure_success">Unit2 Post Failure Success</h2>');
    expect(recoveryOutput).toContain('<code class="highlight yaml">');
    expect(recoveryOutput).toContain(recoveryCase.contentSentinel);
    expect(recoveryOutput).toContain(recoveryCase.optionSentinel);
    expect(recoveryOutput).not.toContain('[object Promise]');
    for (const overlapCase of mixedBatchCases) {
      expect(recoveryOutput).not.toContain(overlapCase.contentSentinel);
      expect(recoveryOutput).not.toContain(overlapCase.optionSentinel);
      expect(recoveryOutput).not.toContain(overlapCase.loggerSentinel);
      if (overlapCase.errorSentinel) {
        expect(recoveryOutput).not.toContain(overlapCase.errorSentinel);
      }
    }
  });

  it('produces equivalent isolated output across repeated bounded parallel batches', async () => {
    const languages = ['javascript', 'ruby', 'python'] as const;
    for (let round = 1; round <= 3; round += 1) {
      const cases = SUPPORTED_EXTENSIONS.map((extension, index) => {
        const language = languages[index];
        if (!language) {
          throw new RangeError(`missing language at index ${index}`);
        }
        return {
          extension,
          marker: `round-${round}-${extension}-${index}`,
          language,
        };
      });

      const expected = [];
      for (const testCase of cases) {
        expected.push(await renderer({ text: createSourceDocument(testCase.marker, testCase.language) }));
      }

      const actual = await Promise.all(
        cases.map(
          async (testCase) => await renderer({ text: createSourceDocument(testCase.marker, testCase.language) }),
        ),
      );

      expect(actual).toEqual(expected);
      for (const [index, output] of actual.entries()) {
        const testCase = cases[index];
        if (!testCase) {
          throw new RangeError(`missing parallel case at index ${index}`);
        }
        expect(output).toContain(`<h2 id="_${testCase.marker.replaceAll('-', '_')}">${testCase.marker}</h2>`);
        expect(output).toContain(`<code class="highlight ${testCase.language}">`);
      }
    }
  });

  it('supports mixed concurrent success and failure and still succeeds afterwards', async () => {
    const mixedResults = await Promise.allSettled([
      renderer({ text: createSourceDocument('mixed-success-javascript', 'javascript') }),
      renderer({ text: 42 as unknown as string }),
      convertAsciiDoc('== convert-success =='),
      convertAsciiDoc({ invalid: true } as unknown as string),
      renderer({ text: createSourceDocument('mixed-success-ruby', 'ruby') }),
    ]);

    expect(mixedResults[0]).toMatchObject({ status: 'fulfilled' });
    expect(mixedResults[1]).toMatchObject({ status: 'rejected' });
    expect(mixedResults[2]).toMatchObject({ status: 'fulfilled' });
    expect(mixedResults[3]).toMatchObject({ status: 'rejected' });
    expect(mixedResults[4]).toMatchObject({ status: 'fulfilled' });

    const successfulRendererOutputs = [mixedResults[0], mixedResults[4]]
      .filter((result): result is PromiseFulfilledResult<string> => result.status === 'fulfilled')
      .map((result) => result.value);
    expect(successfulRendererOutputs[0]).toContain('mixed-success-javascript');
    expect(successfulRendererOutputs[0]).toContain('<code class="highlight javascript">');
    expect(successfulRendererOutputs[1]).toContain('mixed-success-ruby');
    expect(successfulRendererOutputs[1]).toContain('<code class="highlight ruby">');

    const failedReasons = [mixedResults[1], mixedResults[3]]
      .filter((result): result is PromiseRejectedResult => result.status === 'rejected')
      .map((result) => result.reason);
    expect(failedReasons).toEqual(expect.arrayContaining([expect.any(TypeError)]));

    const recoveryRender = await renderer({ text: createSourceDocument('post-failure-success', 'python') });
    const recoveryConvert = await convertAsciiDoc('== Post Failure Success ==');

    expect(recoveryRender).toContain('post-failure-success');
    expect(recoveryRender).toContain('<code class="highlight python">');
    expect(recoveryConvert).toContain('Post Failure Success');
  });
});
