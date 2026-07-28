/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { execFileSync } from 'node:child_process';
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readlinkSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

const sourceScript = path.resolve(import.meta.dirname, '../scripts/sync-licenses.mjs');
const backupSuffix = '.sync-licenses-backup';
const lexistsSync = (entryPath) => {
  try {
    lstatSync(entryPath);
    return true;
  } catch (error) {
    if (error?.code === 'ENOENT') {
      return false;
    }
    throw error;
  }
};

let fixtureRoot;
let packageRoot;

const run = (mode) => {
  try {
    execFileSync(process.execPath, ['scripts/sync-licenses.mjs', mode], {
      cwd: packageRoot,
      encoding: 'utf8',
      stdio: 'pipe',
    });
    return { succeeded: true, stderr: '' };
  } catch (error) {
    return { succeeded: false, stderr: String(error.stderr) };
  }
};

beforeEach(() => {
  fixtureRoot = mkdtempSync(path.join(tmpdir(), 'sync-licenses-test-'));
  packageRoot = path.join(fixtureRoot, 'package');
  mkdirSync(path.join(packageRoot, 'scripts'), { recursive: true });
  mkdirSync(path.join(fixtureRoot, 'LICENSES'), { recursive: true });
  writeFileSync(path.join(fixtureRoot, 'pnpm-workspace.yaml'), 'packages: []\n');
  writeFileSync(path.join(fixtureRoot, 'LICENSE'), 'root license\n');
  writeFileSync(path.join(fixtureRoot, 'COPYING'), 'root copying\n');
  writeFileSync(path.join(fixtureRoot, 'COPYING.LESSER'), 'root lesser\n');
  writeFileSync(path.join(fixtureRoot, 'LICENSES', 'LGPL-3.0-linking-exception.txt'), 'root exception\n');
  writeFileSync(path.join(packageRoot, 'LICENSE'), 'user package license\n');
  cpSync(sourceScript, path.join(packageRoot, 'scripts', 'sync-licenses.mjs'));
});

afterEach(() => {
  rmSync(fixtureRoot, { force: true, recursive: true });
});

describe('sync-licenses ownership and recovery', () => {
  it('copies a dangling source symlink exactly and restores the package target', () => {
    const source = path.join(fixtureRoot, 'LICENSE');
    const target = path.join(packageRoot, 'LICENSE');
    rmSync(source);
    symlinkSync('missing-root-license', source);

    expect(run('prepack').succeeded).toBe(true);
    expect(lstatSync(target).isSymbolicLink()).toBe(true);
    expect(readlinkSync(target)).toBe('missing-root-license');

    expect(run('postpack').succeeded).toBe(true);
    expect(lstatSync(target).isFile()).toBe(true);
    expect(readFileSync(target, 'utf8')).toBe('user package license\n');
  });

  it('restores a dangling destination symlink exactly', () => {
    const target = path.join(packageRoot, 'LICENSE');
    rmSync(target);
    symlinkSync('../missing-user-license', target);

    expect(run('prepack').succeeded).toBe(true);
    expect(lstatSync(target).isFile()).toBe(true);

    expect(run('postpack').succeeded).toBe(true);
    expect(lstatSync(target).isSymbolicLink()).toBe(true);
    expect(readlinkSync(target)).toBe('../missing-user-license');
  });

  it.each([
    ['state file', '.sync-licenses-state.json'],
    ['backup directory', '.sync-licenses-backups'],
    ['legacy backup', `COPYING${backupSuffix}`],
  ])('preflights and preserves a dangling %s', (_label, relativePath) => {
    const orphan = path.join(packageRoot, relativePath);
    symlinkSync('missing-user-data', orphan);

    expect(run('prepack').succeeded).toBe(false);
    expect(lexistsSync(orphan)).toBe(true);
    expect(lstatSync(orphan).isSymbolicLink()).toBe(true);
    expect(readlinkSync(orphan)).toBe('missing-user-data');
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
  });

  it('preflights an orphaned managed backup even when its target is absent', () => {
    const orphan = path.join(packageRoot, '.sync-licenses-backups', `COPYING${backupSuffix}`);
    mkdirSync(path.dirname(orphan), { recursive: true });
    writeFileSync(orphan, 'orphaned user data\n');

    const result = run('prepack');

    expect(result.succeeded).toBe(false);
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
    expect(readFileSync(orphan, 'utf8')).toBe('orphaned user data\n');
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(existsSync(path.join(packageRoot, 'COPYING'))).toBe(false);
  });

  it('preflights and preserves a legacy orphaned backup', () => {
    const legacyBackup = path.join(packageRoot, `COPYING${backupSuffix}`);
    writeFileSync(legacyBackup, 'legacy orphan\n');

    expect(run('prepack').succeeded).toBe(false);
    expect(readFileSync(legacyBackup, 'utf8')).toBe('legacy orphan\n');
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
  });

  it('preflights and preserves an existing state file and backup', () => {
    const state = path.join(packageRoot, '.sync-licenses-state.json');
    const orphan = path.join(packageRoot, '.sync-licenses-backups', 'orphan');
    mkdirSync(path.dirname(orphan), { recursive: true });
    writeFileSync(state, '{"user":"state"}\n');
    writeFileSync(orphan, 'backup bytes\n');

    expect(run('prepack').succeeded).toBe(false);
    expect(readFileSync(state, 'utf8')).toBe('{"user":"state"}\n');
    expect(readFileSync(orphan, 'utf8')).toBe('backup bytes\n');
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
  });

  it('preflights every source before moving a user target', () => {
    rmSync(path.join(fixtureRoot, 'COPYING'));

    expect(run('prepack').succeeded).toBe(false);
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
    expect(existsSync(path.join(packageRoot, '.sync-licenses-backups'))).toBe(false);
  });

  it('rejects a symlinked license parent without mutating its external target', () => {
    const external = path.join(fixtureRoot, 'external-licenses');
    const sentinel = path.join(external, 'LGPL-3.0-linking-exception.txt');
    mkdirSync(external);
    writeFileSync(sentinel, 'external sentinel\n');
    symlinkSync(external, path.join(packageRoot, 'LICENSES'));

    const result = run('prepack');

    expect(result.succeeded).toBe(false);
    expect(result.stderr).toContain('Refusing to follow symlinked package parent path LICENSES');
    expect(readFileSync(sentinel, 'utf8')).toBe('external sentinel\n');
    expect(lstatSync(path.join(packageRoot, 'LICENSES')).isSymbolicLink()).toBe(true);
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
  });

  it('refuses postpack cleanup through a replaced parent symlink without mutating external data', () => {
    expect(run('prepack').succeeded).toBe(true);
    const external = path.join(fixtureRoot, 'external-cleanup');
    const sentinel = path.join(external, 'LGPL-3.0-linking-exception.txt');
    mkdirSync(external);
    writeFileSync(sentinel, 'external cleanup sentinel\n');
    rmSync(path.join(packageRoot, 'LICENSES'), { recursive: true });
    symlinkSync(external, path.join(packageRoot, 'LICENSES'));

    const result = run('postpack');

    expect(result.succeeded).toBe(false);
    expect(result.stderr).toContain('Refusing to follow symlinked package parent path LICENSES');
    expect(readFileSync(sentinel, 'utf8')).toBe('external cleanup sentinel\n');
    expect(lstatSync(path.join(packageRoot, 'LICENSES')).isSymbolicLink()).toBe(true);
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(true);
  });

  it('postpack without valid state refuses to delete targets or orphaned backups', () => {
    const copying = path.join(packageRoot, 'COPYING');
    const orphan = path.join(packageRoot, '.sync-licenses-backups', 'unrecorded');
    mkdirSync(path.dirname(orphan), { recursive: true });
    writeFileSync(copying, 'user copying\n');
    writeFileSync(orphan, 'user backup\n');

    expect(run('postpack').succeeded).toBe(false);
    expect(readFileSync(copying, 'utf8')).toBe('user copying\n');
    expect(readFileSync(orphan, 'utf8')).toBe('user backup\n');
  });

  it.each([
    [
      'an unknown state version',
      {
        version: 999,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [{ target: 'LICENSE', backup: null }],
      },
    ],
    [
      'a missing root field',
      {
        version: 1,
        createdBackupDirectory: false,
        items: [{ target: 'LICENSE', backup: null }],
      },
    ],
    [
      'an extra root field',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [{ target: 'LICENSE', backup: null }],
        extra: true,
      },
    ],
    [
      'an extra item field',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [{ target: 'LICENSE', backup: null, extra: true }],
      },
    ],
    [
      'a traversal target',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [{ target: '../LICENSE', backup: null }],
      },
    ],
    [
      'a traversal backup',
      {
        version: 1,
        createdBackupDirectory: true,
        createdDirectories: [],
        items: [{ target: 'LICENSE', backup: '../LICENSE.sync-licenses-backup' }],
      },
    ],
    [
      'an absolute created directory',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: ['/tmp'],
        items: [{ target: 'LICENSE', backup: null }],
      },
    ],
    [
      'duplicate target records',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [
          { target: 'LICENSE', backup: null },
          { target: 'LICENSE', backup: null },
        ],
      },
    ],
    [
      'duplicate created directories',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: ['LICENSES', 'LICENSES'],
        items: [{ target: 'LICENSE', backup: null }],
      },
    ],
    [
      'inconsistent backup metadata',
      {
        version: 1,
        createdBackupDirectory: false,
        createdDirectories: [],
        items: [{ target: 'LICENSE', backup: `.sync-licenses-backups/LICENSE${backupSuffix}` }],
      },
    ],
  ])('rejects %s without changing targets, backups, or state', (_label, stateValue) => {
    const state = path.join(packageRoot, '.sync-licenses-state.json');
    const backup = path.join(packageRoot, '.sync-licenses-backups', `LICENSE${backupSuffix}`);
    mkdirSync(path.dirname(backup), { recursive: true });
    writeFileSync(backup, 'user backup bytes\n');
    const stateBytes = `${JSON.stringify(stateValue, null, 2)}\n`;
    writeFileSync(state, stateBytes);

    const result = run('postpack');

    expect(result.succeeded).toBe(false);
    expect(result.stderr).toContain('Refusing cleanup');
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(readFileSync(backup, 'utf8')).toBe('user backup bytes\n');
    expect(readFileSync(state, 'utf8')).toBe(stateBytes);
  });

  it('rejects malformed JSON without changing targets, backups, or state', () => {
    const state = path.join(packageRoot, '.sync-licenses-state.json');
    const backup = path.join(packageRoot, '.sync-licenses-backups', `LICENSE${backupSuffix}`);
    mkdirSync(path.dirname(backup), { recursive: true });
    writeFileSync(backup, 'user backup bytes\n');
    writeFileSync(state, '{"version": 1');

    const result = run('postpack');

    expect(result.succeeded).toBe(false);
    expect(result.stderr).toContain('not valid JSON');
    expect(result.stderr).toContain('Refusing cleanup');
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(readFileSync(backup, 'utf8')).toBe('user backup bytes\n');
    expect(readFileSync(state, 'utf8')).toBe('{"version": 1');
  });

  it('restores recorded data while preserving an unrecorded backup', () => {
    expect(run('prepack').succeeded).toBe(true);
    const orphan = path.join(packageRoot, '.sync-licenses-backups', 'unrecorded');
    writeFileSync(orphan, 'do not remove\n');

    expect(run('postpack').succeeded).toBe(true);
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('user package license\n');
    expect(existsSync(path.join(packageRoot, 'COPYING'))).toBe(false);
    expect(existsSync(path.join(packageRoot, 'COPYING.LESSER'))).toBe(false);
    expect(existsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
    expect(readFileSync(orphan, 'utf8')).toBe('do not remove\n');
  });

  it('attempts later recorded restorations after an earlier backup is missing', () => {
    const backupDirectory = path.join(packageRoot, '.sync-licenses-backups');
    const copyingBackup = path.join(backupDirectory, `COPYING${backupSuffix}`);
    mkdirSync(backupDirectory, { recursive: true });
    writeFileSync(path.join(packageRoot, 'COPYING'), 'temporary copied license\n');
    writeFileSync(copyingBackup, 'user copying restored\n');
    writeFileSync(
      path.join(packageRoot, '.sync-licenses-state.json'),
      JSON.stringify({
        version: 1,
        createdBackupDirectory: true,
        createdDirectories: [],
        items: [
          { target: 'LICENSE', backup: `.sync-licenses-backups/LICENSE${backupSuffix}` },
          { target: 'COPYING', backup: `.sync-licenses-backups/COPYING${backupSuffix}` },
        ],
      }),
    );

    expect(run('postpack').succeeded).toBe(false);
    expect(readFileSync(path.join(packageRoot, 'COPYING'), 'utf8')).toBe('user copying restored\n');
    const remainingState = JSON.parse(readFileSync(path.join(packageRoot, '.sync-licenses-state.json'), 'utf8'));
    expect(remainingState.items).toEqual([
      {
        target: 'LICENSE',
        backup: `.sync-licenses-backups/LICENSE${backupSuffix}`,
        completed: false,
      },
      {
        target: 'COPYING',
        backup: `.sync-licenses-backups/COPYING${backupSuffix}`,
        completed: true,
      },
    ]);
  });

  it('aggregates multiple cleanup failures, retains retry state, and succeeds after repair', () => {
    const backupDirectory = path.join(packageRoot, '.sync-licenses-backups');
    const statePath = path.join(packageRoot, '.sync-licenses-state.json');
    mkdirSync(backupDirectory);
    writeFileSync(path.join(packageRoot, 'COPYING'), 'temporary copying\n');
    writeFileSync(
      statePath,
      JSON.stringify({
        version: 2,
        createdBackupDirectory: true,
        createdDirectories: [],
        items: [
          {
            target: 'LICENSE',
            backup: `.sync-licenses-backups/LICENSE${backupSuffix}`,
            completed: false,
          },
          {
            target: 'COPYING',
            backup: `.sync-licenses-backups/COPYING${backupSuffix}`,
            completed: false,
          },
        ],
      }),
    );

    const failed = run('postpack');

    expect(failed.succeeded).toBe(false);
    expect(failed.stderr.match(/Recorded backup for/g)).toHaveLength(2);
    expect(existsSync(statePath)).toBe(true);

    mkdirSync(backupDirectory);
    writeFileSync(path.join(backupDirectory, `LICENSE${backupSuffix}`), 'restored license\n');
    writeFileSync(path.join(backupDirectory, `COPYING${backupSuffix}`), 'restored copying\n');

    expect(run('postpack').succeeded).toBe(true);
    expect(readFileSync(path.join(packageRoot, 'LICENSE'), 'utf8')).toBe('restored license\n');
    expect(readFileSync(path.join(packageRoot, 'COPYING'), 'utf8')).toBe('restored copying\n');
    expect(existsSync(statePath)).toBe(false);
    expect(existsSync(backupDirectory)).toBe(false);
  });

  it('restores a recorded dangling backup symlink exactly', () => {
    const backupDirectory = path.join(packageRoot, '.sync-licenses-backups');
    const copyingBackup = path.join(backupDirectory, `COPYING${backupSuffix}`);
    mkdirSync(backupDirectory, { recursive: true });
    writeFileSync(path.join(packageRoot, 'COPYING'), 'temporary copied license\n');
    symlinkSync('../missing-user-copying', copyingBackup);
    writeFileSync(
      path.join(packageRoot, '.sync-licenses-state.json'),
      JSON.stringify({
        version: 1,
        createdBackupDirectory: true,
        createdDirectories: [],
        items: [{ target: 'COPYING', backup: `.sync-licenses-backups/COPYING${backupSuffix}` }],
      }),
    );

    expect(run('postpack').succeeded).toBe(true);
    const restored = path.join(packageRoot, 'COPYING');
    expect(lexistsSync(restored)).toBe(true);
    expect(lstatSync(restored).isSymbolicLink()).toBe(true);
    expect(readlinkSync(restored)).toBe('../missing-user-copying');
    expect(lexistsSync(path.join(packageRoot, '.sync-licenses-state.json'))).toBe(false);
  });
});
