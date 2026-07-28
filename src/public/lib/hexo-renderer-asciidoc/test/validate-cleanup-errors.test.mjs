/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { spawnSync } from 'node:child_process';
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  readlinkSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  captureCleanupFailure,
  capturePathStates,
  createCommandRunner,
  createEvidenceRecorder,
  PACK_LIFECYCLE_PATHS,
  restorePathStates,
  runPlain,
  throwValidationFailures,
  verifyPackCleanup,
} from '../scripts/validation-utils.mjs';

const packageRoot = path.resolve(import.meta.dirname, '..');
const validationUtilsScript = path.join(packageRoot, 'scripts', 'validation-utils.mjs');
const releasePackScript = path.join(packageRoot, 'scripts', 'validate-release-pack.mjs');
const packedArtifactScript = path.join(packageRoot, 'scripts', 'validate-packed-artifact.mjs');
const linkedExampleScript = path.join(packageRoot, 'scripts', 'validate-linked-example.mjs');

const run = (command, args, cwd) => {
  const result = spawnSync(command, args, { cwd, encoding: 'utf8' });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr}`);
  }
  return result.stdout;
};

const createReleaseFixture = () => {
  const root = mkdtempSync(path.join(tmpdir(), 'release-pack-init-test-'));
  const fixturePackageRoot = path.join(root, 'src', 'public', 'lib', 'hexo-renderer-asciidoc');
  mkdirSync(path.join(fixturePackageRoot, 'scripts'), { recursive: true });
  cpSync(validationUtilsScript, path.join(fixturePackageRoot, 'scripts', 'validation-utils.mjs'));
  cpSync(releasePackScript, path.join(fixturePackageRoot, 'scripts', 'validate-release-pack.mjs'));
  writeFileSync(path.join(root, 'pnpm-workspace.yaml'), 'packages: []\n');
  writeFileSync(path.join(fixturePackageRoot, 'package.json'), '{"name":"fixture","version":"0.0.0-placeholder"}\n');
  writeFileSync(path.join(fixturePackageRoot, 'README.md'), 'contributor readme\n');
  writeFileSync(path.join(fixturePackageRoot, 'README.npm.md'), 'package readme\n');
  run('git', ['init', '--quiet'], root);
  run('git', ['config', 'user.email', 'test@example.invalid'], root);
  run('git', ['config', 'user.name', 'Validation Test'], root);
  run('git', ['add', '.'], root);
  run('git', ['commit', '--quiet', '-m', 'fixture'], root);
  return { fixturePackageRoot, root };
};

const listValidationTemps = (prefix) => new Set(readdirSync(tmpdir()).filter((entry) => entry.startsWith(prefix)));

describe('validator cleanup failure handling', () => {
  it.each([
    ['evidence command runner', () => createCommandRunner().runResult],
    ['plain command runner', () => runPlain],
  ])('rejects a signaled child in the %s', (_label, getRunner) => {
    const runner = getRunner();
    expect(() => runner(process.execPath, ['-e', 'process.kill(process.pid, "SIGTERM")'])).toThrow(
      /Command:.*Signal: SIGTERM.*Error: none.*Stderr: <empty>/s,
    );
  });

  it.each([
    ['evidence command runner', () => createCommandRunner().runResult],
    ['plain command runner', () => runPlain],
  ])('rejects a spawn error with null status in the %s', (_label, getRunner) => {
    const runner = getRunner();
    expect(() => runner('definitely-not-a-unit3-command', [])).toThrow(
      /Command: definitely-not-a-unit3-command.*Context: cwd=.*Status: null.*Signal: none.*Error: Error: spawnSync definitely-not-a-unit3-command E(?:NOENT|A[C]{2}ES).*Stderr: <empty>/s,
    );
  });

  it('rejects a null status even when no signal or spawn error is supplied', () => {
    const spawnWithNullStatus = () => ({
      error: undefined,
      signal: null,
      status: null,
      stderr: 'injected stderr',
      stdout: '',
    });

    expect(() => createCommandRunner(undefined, spawnWithNullStatus).runResult('injected', [])).toThrow(
      /Status: null.*Signal: none.*Error: none.*Stderr: injected stderr/s,
    );
    expect(() => runPlain('injected', [], { spawn: spawnWithNullStatus })).toThrow(
      /Status: null.*Signal: none.*Error: none.*Stderr: injected stderr/s,
    );
  });

  it('preserves the primary error identity when no cleanup fails', () => {
    const primary = new Error('primary details');
    expect(() => throwValidationFailures(primary, [], 'validation failed')).toThrow(primary);
  });

  it('aggregates primary and cleanup failures without replacing the primary', () => {
    const primary = new Error('primary details');
    const cleanup = new Error('cleanup details');

    let thrown;
    try {
      throwValidationFailures(primary, [cleanup], 'validation failed');
    } catch (error) {
      thrown = error;
    }

    expect(thrown).toBeInstanceOf(AggregateError);
    expect(thrown.errors).toEqual([primary, cleanup]);
    expect(thrown.cause).toBe(primary);
    expect(thrown.errors[0]).toBe(primary);
  });

  it('reports cleanup-only failure', () => {
    const cleanup = new Error('cleanup-only details');
    expect(() => throwValidationFailures(undefined, [cleanup], 'cleanup failed')).toThrow(AggregateError);
    try {
      throwValidationFailures(undefined, [cleanup], 'cleanup failed');
    } catch (error) {
      expect(error.errors).toEqual([cleanup]);
      expect(error.cause).toBeUndefined();
    }
  });

  it('captures every cleanup operation after earlier failures', () => {
    const errors = [];
    const attempted = [];
    for (const [name, fails] of [
      ['restore', true],
      ['remove-temp', false],
      ['verify', true],
    ]) {
      captureCleanupFailure(errors, () => {
        attempted.push(name);
        if (fails) {
          throw new Error(`${name} failed`);
        }
      });
    }
    expect(attempted).toEqual(['restore', 'remove-temp', 'verify']);
    expect(errors.map((error) => error.message)).toEqual(['restore failed', 'verify failed']);
  });

  it('attempts every path restoration when an earlier path fails', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'restore-path-states-test-'));
    try {
      expect(() =>
        restorePathStates(root, {
          broken: { type: 'file' },
          restored: { type: 'file', mode: 0o100644, contents: Buffer.from('restored\n') },
        }),
      ).toThrow(AggregateError);
      expect(existsSync(path.join(root, 'restored'))).toBe(true);
      expect(readFileSync(path.join(root, 'restored'), 'utf8')).toBe('restored\n');
    } finally {
      rmSync(root, { force: true, recursive: true });
    }
  });

  it('captures and restores dangling symlink path state without dereferencing it', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'restore-symlink-state-test-'));
    try {
      const target = path.join(root, 'dangling');
      symlinkSync('../missing-target', target);
      const state = capturePathStates(root, ['dangling']);
      rmSync(target);
      writeFileSync(target, 'replacement\n');

      restorePathStates(root, state);

      expect(lstatSync(target).isSymbolicLink()).toBe(true);
      expect(readlinkSync(target)).toBe('../missing-target');
    } finally {
      rmSync(root, { force: true, recursive: true });
    }
  });

  it('normalizes absolute paths before writing evidence', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'normalized-evidence-test-'));
    const repositoryRoot = path.join(root, 'repository');
    const sessionRoot = path.join(root, 'session');
    try {
      const recorder = createEvidenceRecorder(root, { repositoryRoot, sessionRoot });
      recorder.writeJson('result.json', {
        homePath: path.join(process.env.HOME, 'candidate'),
        repositoryPath: path.join(repositoryRoot, 'candidate'),
        sessionPath: path.join(sessionRoot, 'candidate'),
        temporaryPath: path.join(tmpdir(), 'candidate'),
      });

      const evidence = readFileSync(path.join(root, 'result.json'), 'utf8');
      expect(evidence).not.toContain(process.env.HOME);
      expect(evidence).not.toContain(repositoryRoot);
      expect(evidence).not.toContain(sessionRoot);
      expect(evidence).not.toContain(`${tmpdir()}/candidate`);
      expect(evidence).toContain('<home>/candidate');
      expect(evidence).toContain('<repo>/candidate');
      expect(evidence).toContain('<session>/candidate');
      expect(evidence).toContain('<tmp>/candidate');
    } finally {
      rmSync(root, { force: true, recursive: true });
    }
  });

  it('normalizes repository, session, home, and temporary paths in child logs', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'normalized-command-log-test-'));
    const repositoryRoot = path.join(root, 'repository');
    const sessionRoot = path.join(root, 'session');
    const output = [
      path.join(repositoryRoot, 'file'),
      path.join(sessionRoot, 'file'),
      path.join(process.env.HOME, 'file'),
      path.join(tmpdir(), 'file'),
    ].join('\n');
    const spawn = () => ({
      error: undefined,
      signal: null,
      status: 0,
      stderr: Buffer.from(output),
      stdout: Buffer.from(output),
    });
    try {
      createCommandRunner(root, spawn, { repositoryRoot, sessionRoot }).run('injected', [], {
        binary: true,
        label: 'path-output',
      });
      for (const stream of ['stdout', 'stderr']) {
        const log = readFileSync(path.join(root, 'logs', `path-output.${stream}.log`), 'utf8');
        expect(log).not.toContain(repositoryRoot);
        expect(log).not.toContain(sessionRoot);
        expect(log).not.toContain(process.env.HOME);
        expect(log).not.toContain(tmpdir());
        expect(log).toContain('<repo>/file');
        expect(log).toContain('<session>/file');
        expect(log).toContain('<home>/file');
        expect(log).toContain('<tmp>/file');
      }
    } finally {
      rmSync(root, { force: true, recursive: true });
    }
  });

  it.each([
    ['LICENSES', 'LICENSES', (root) => mkdirSync(path.join(root, 'LICENSES'))],
    [
      'sync state',
      '.sync-licenses-state.json',
      (root) => writeFileSync(path.join(root, '.sync-licenses-state.json'), '{}\n'),
    ],
    [
      'README backup',
      '.README.md.npm-backup',
      (root) => writeFileSync(path.join(root, '.README.md.npm-backup'), 'leak\n'),
    ],
  ])('reports leaked %s without deleting or repairing it', (_label, leakPath, createLeak) => {
    const root = mkdtempSync(path.join(tmpdir(), 'verify-pack-cleanup-test-'));
    try {
      writeFileSync(path.join(root, 'package.json'), '{"name":"fixture","version":"0.0.0-placeholder"}\n');
      writeFileSync(path.join(root, 'README.md'), 'contributor readme\n');
      writeFileSync(path.join(root, 'README.npm.md'), 'package readme\n');
      writeFileSync(path.join(root, 'LICENSE'), 'package license\n');
      const expectedLifecycleState = capturePathStates(root, PACK_LIFECYCLE_PATHS);
      const expectedPackageJson = readFileSync(path.join(root, 'package.json'));
      const expectedReadme = readFileSync(path.join(root, 'README.md'));
      const expectedReadmeNpm = readFileSync(path.join(root, 'README.npm.md'));
      createLeak(root);

      expect(() =>
        verifyPackCleanup({
          expectedLifecycleState,
          expectedPackageJson,
          expectedReadme,
          expectedReadmeNpm,
          packageRoot: root,
        }),
      ).toThrow();
      expect(existsSync(path.join(root, leakPath))).toBe(true);
      if (_label === 'sync state') {
        expect(readFileSync(path.join(root, '.sync-licenses-state.json'), 'utf8')).toBe('{}\n');
      }
      if (_label === 'README backup') {
        expect(readFileSync(path.join(root, '.README.md.npm-backup'), 'utf8')).toBe('leak\n');
      }
    } finally {
      rmSync(root, { force: true, recursive: true });
    }
  });

  it('aggregates an early release initialization fault with cleanup failure and removes its temp directory', () => {
    const fixture = createReleaseFixture();
    const prefix = 'hexo-renderer-asciidoc-release-pack-';
    const beforeTemps = listValidationTemps(prefix);
    try {
      const result = spawnSync(process.execPath, ['scripts/validate-release-pack.mjs'], {
        cwd: fixture.fixturePackageRoot,
        encoding: 'utf8',
        env: {
          ...process.env,
          HEXO_RENDERER_ASCIIDOC_VALIDATION_FAULTS: 'release-pack:after-temp-directory,release-pack:after-temp-cleanup',
        },
      });

      expect(result.status).not.toBe(0);
      expect(result.stderr).toContain('Injected validation fault: release-pack:after-temp-directory');
      expect(result.stderr).toContain('Injected validation fault: release-pack:after-temp-cleanup');
      expect(listValidationTemps(prefix)).toEqual(beforeTemps);
      expect(run('git', ['status', '--porcelain=v1'], fixture.root)).toBe('');
    } finally {
      rmSync(fixture.root, { force: true, recursive: true });
    }
  });

  it('aggregates an early packed-artifact initialization fault with cleanup failure', () => {
    const prefix = 'hexo-renderer-asciidoc-pack-';
    const beforeTemps = listValidationTemps(prefix);
    const result = spawnSync(process.execPath, [packedArtifactScript], {
      cwd: packageRoot,
      encoding: 'utf8',
      env: {
        ...process.env,
        HEXO_RENDERER_ASCIIDOC_VALIDATION_FAULTS:
          'packed-artifact:after-temp-directory,packed-artifact:after-temp-cleanup',
      },
    });

    expect(result.status).not.toBe(0);
    expect(result.stderr).toContain('Injected validation fault: packed-artifact:after-temp-directory');
    expect(result.stderr).toContain('Injected validation fault: packed-artifact:after-temp-cleanup');
    expect(listValidationTemps(prefix)).toEqual(beforeTemps);
  });

  it('aggregates an early linked-example state initialization fault with cleanup failure', () => {
    const result = spawnSync(process.execPath, [linkedExampleScript], {
      cwd: packageRoot,
      encoding: 'utf8',
      env: {
        ...process.env,
        HEXO_RENDERER_ASCIIDOC_VALIDATION_FAULTS:
          'linked-example:after-example-state,linked-example:after-example-state-cleanup',
      },
    });

    expect(result.status).not.toBe(0);
    expect(result.stderr).toContain('Injected validation fault: linked-example:after-example-state');
    expect(result.stderr).toContain('Injected validation fault: linked-example:after-example-state-cleanup');
  });

  it('refuses release validation from a dirty checkout before creating temporary state', () => {
    const fixture = createReleaseFixture();
    const prefix = 'hexo-renderer-asciidoc-release-pack-';
    const beforeTemps = listValidationTemps(prefix);
    try {
      writeFileSync(path.join(fixture.root, 'dirty.txt'), 'dirty\n');
      const result = spawnSync(process.execPath, ['scripts/validate-release-pack.mjs'], {
        cwd: fixture.fixturePackageRoot,
        encoding: 'utf8',
        env: process.env,
      });

      expect(result.status).not.toBe(0);
      expect(result.stderr).toContain('requires a clean worktree at start');
      expect(listValidationTemps(prefix)).toEqual(beforeTemps);
      expect(existsSync(path.join(fixture.root, 'dirty.txt'))).toBe(true);
    } finally {
      rmSync(fixture.root, { force: true, recursive: true });
    }
  });
});
