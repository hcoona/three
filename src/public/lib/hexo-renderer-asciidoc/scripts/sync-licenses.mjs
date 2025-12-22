#!/usr/bin/env node
/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

/**
 * Copies shared license artifacts from the monorepo root into the package root before packing
 * and removes them afterward so that they do not stay tracked in git.
 */

import { cp, mkdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const moduleDir = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(moduleDir, '..');

const BACKUP_SUFFIX = '.sync-licenses-backup';
const STATE_FILE = path.join(packageRoot, '.sync-licenses-state.json');

/**
 * Explicit list of license artifacts to copy from the monorepo root into the package root.
 * We keep this list tight so the package only ships what it needs.
 */
const LICENSE_ITEMS = [
  { source: 'LICENSE', target: 'LICENSE' },
  { source: 'COPYING', target: 'COPYING' },
  { source: 'COPYING.LESSER', target: 'COPYING.LESSER' },
  {
    source: path.join('LICENSES', 'LGPL-3.0-linking-exception.txt'),
    target: path.join('LICENSES', 'LGPL-3.0-linking-exception.txt'),
  },
];

async function pathExists(entryPath) {
  try {
    await stat(entryPath);
    return true;
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return false;
    }
    throw error;
  }
}

async function tryStat(entryPath) {
  try {
    return await stat(entryPath);
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return null;
    }
    throw error;
  }
}

async function ensureParentDir(filePath) {
  const parent = path.dirname(filePath);
  await mkdir(parent, { recursive: true });
}

async function findMonorepoRoot(startDir) {
  let current = startDir;
  const { root } = path.parse(current);
  while (true) {
    for (const marker of ['pnpm-workspace.yaml', '.git']) {
      if (await pathExists(path.join(current, marker))) {
        return current;
      }
    }
    if (current === root) {
      break;
    }
    current = path.dirname(current);
  }
  throw new Error(`Unable to locate pnpm workspace root when starting from ${startDir}.`);
}

const monorepoRoot = await findMonorepoRoot(packageRoot);

function log(message) {
  process.stdout.write(`[sync-licenses] ${message}\n`);
}

function logError(message) {
  process.stderr.write(`[sync-licenses] ERROR: ${message}\n`);
}

async function copyEntry(entry) {
  const source = path.join(monorepoRoot, entry.source);
  const target = path.join(packageRoot, entry.target);
  const sourceStat = await tryStat(source);
  if (!sourceStat) {
    throw new Error(`Cannot find "${entry.source}" in monorepo root (${monorepoRoot}).`);
  }

  await ensureParentDir(target);
  await rm(target, { recursive: true, force: true });
  if (sourceStat.isDirectory()) {
    await cp(source, target, { recursive: true });
    return;
  }

  await cp(source, target);
}

async function removeEntry(entry) {
  const target = path.join(packageRoot, entry);
  await rm(target, { recursive: true, force: true });
}

async function backupTargetIfNeeded(targetRelativePath) {
  const target = path.join(packageRoot, targetRelativePath);
  const targetStat = await tryStat(target);
  if (!targetStat) {
    return null;
  }

  const backup = `${target}${BACKUP_SUFFIX}`;
  const backupStat = await tryStat(backup);
  if (backupStat) {
    throw new Error(
      `Found an existing backup (${path.relative(packageRoot, backup)}). ` +
        'A previous run likely did not finish. Run "postpack" to restore, ' +
        'or delete the backup manually if you know what you are doing.',
    );
  }
  await ensureParentDir(backup);
  await rename(target, backup);
  return path.relative(packageRoot, backup);
}

async function restoreBackupIfNeeded(targetRelativePath, backupRelativePath) {
  if (!backupRelativePath) {
    return;
  }

  const target = path.join(packageRoot, targetRelativePath);
  const backup = path.join(packageRoot, backupRelativePath);
  const backupStat = await tryStat(backup);
  if (!backupStat) {
    return;
  }

  await rm(target, { recursive: true, force: true });
  await ensureParentDir(target);
  await rename(backup, target);
}

async function writeState(state) {
  await writeFile(STATE_FILE, JSON.stringify(state, null, 2), 'utf8');
}

async function readState() {
  const raw = await readFile(STATE_FILE, 'utf8').catch(() => null);
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

async function handlePrepack() {
  const existingState = await tryStat(STATE_FILE);
  if (existingState) {
    throw new Error(
      `Found an existing state file (${path.relative(packageRoot, STATE_FILE)}). ` +
        'A previous run likely did not finish. Run "postpack" to restore, ' +
        'or delete the state file manually if you know what you are doing.',
    );
  }

  const state = {
    version: 1,
    items: [],
  };

  for (const item of LICENSE_ITEMS) {
    const backup = await backupTargetIfNeeded(item.target);
    await copyEntry(item);
    state.items.push({ target: item.target, backup });
    await writeState(state);
  }
  log('Copied shared license artifacts into package root.');
}

async function handlePostpack() {
  const state = await readState();

  // Best-effort cleanup even if the state file is missing.
  if (!state || !Array.isArray(state.items)) {
    logError(
      `State file (${path.relative(packageRoot, STATE_FILE)}) is missing or invalid. ` +
        'Performing best-effort cleanup without touching the package LICENSE.',
    );

    for (const item of LICENSE_ITEMS) {
      if (item.target === 'LICENSE') {
        continue;
      }
      await removeEntry(item.target);
    }

    // Remove the LICENSES directory if we created it and it is empty.
    await rm(path.join(packageRoot, 'LICENSES'), { force: true, recursive: false }).catch(() => {});
    log('Removed temporary shared license artifacts after packaging.');
    return;
  }

  for (const item of state.items) {
    await removeEntry(item.target);
    await restoreBackupIfNeeded(item.target, item.backup);
  }

  await rm(STATE_FILE, { force: true });
  await rm(path.join(packageRoot, 'LICENSES'), { force: true, recursive: false }).catch(() => {});
  log('Removed temporary shared license artifacts after packaging.');
}

async function main() {
  const mode = process.argv[2];
  if (mode === 'prepack') {
    await handlePrepack();
    return;
  }

  if (mode === 'postpack') {
    await handlePostpack();
    return;
  }

  logError('Expected mode argument: prepack | postpack');
  process.exitCode = 1;
}

await main();
