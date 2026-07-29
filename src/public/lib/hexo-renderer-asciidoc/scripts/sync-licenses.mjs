#!/usr/bin/env node

/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

/**
 * Copies shared license artifacts from the monorepo root into the package root before packing
 * and removes them afterward so that they do not stay tracked in git.
 */

import { randomUUID } from 'node:crypto';
import { cp, lstat, mkdir, readFile, readlink, rename, rm, rmdir, symlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const moduleDir = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(moduleDir, '..');

const BACKUP_SUFFIX = '.sync-licenses-backup';
const BACKUP_DIRECTORY = path.join(packageRoot, '.sync-licenses-backups');
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
    await lstat(entryPath);
    return true;
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return false;
    }
    throw error;
  }
}

async function tryLstat(entryPath) {
  try {
    return await lstat(entryPath);
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      return null;
    }
    throw error;
  }
}

async function ensureParentDir(filePath) {
  const parent = path.dirname(filePath);
  const relativeParent = path.relative(packageRoot, parent);
  let current = packageRoot;
  for (const segment of relativeParent.split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    const currentStat = await tryLstat(current);
    if (!currentStat) {
      await mkdir(current);
      continue;
    }
    if (currentStat.isSymbolicLink()) {
      throw new Error(`Refusing to follow symlinked package parent path ${path.relative(packageRoot, current)}.`);
    }
    if (!currentStat.isDirectory()) {
      throw new Error(`Package parent path ${path.relative(packageRoot, current)} is not a directory.`);
    }
  }
}

async function assertSafeParentPath(filePath) {
  const relativePath = path.relative(packageRoot, filePath);
  if (relativePath.startsWith('..') || path.isAbsolute(relativePath)) {
    throw new Error(`Refusing path outside package root: ${filePath}.`);
  }

  let current = packageRoot;
  for (const segment of path
    .dirname(relativePath)
    .split(path.sep)
    .filter((entry) => entry && entry !== '.')) {
    current = path.join(current, segment);
    const currentStat = await tryLstat(current);
    if (!currentStat) {
      return;
    }
    if (currentStat.isSymbolicLink()) {
      throw new Error(`Refusing to follow symlinked package parent path ${path.relative(packageRoot, current)}.`);
    }
    if (!currentStat.isDirectory()) {
      throw new Error(`Package parent path ${path.relative(packageRoot, current)} is not a directory.`);
    }
  }
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
  const sourceStat = await tryLstat(source);
  if (!sourceStat) {
    throw new Error(`Cannot find "${entry.source}" in monorepo root (${monorepoRoot}).`);
  }

  await ensureParentDir(target);
  await assertSafeParentPath(target);
  await rm(target, { recursive: true, force: true });
  if (sourceStat.isSymbolicLink()) {
    await symlink(await readlink(source), target);
    return;
  }
  if (sourceStat.isDirectory()) {
    await cp(source, target, { recursive: true, verbatimSymlinks: true });
    return;
  }

  await cp(source, target, { verbatimSymlinks: true });
}

async function preflightSource(entry) {
  const source = path.join(monorepoRoot, entry.source);
  const sourceStat = await tryLstat(source);
  if (!sourceStat) {
    throw new Error(`Cannot find "${entry.source}" in monorepo root (${monorepoRoot}).`);
  }
}

async function removeEntry(entry) {
  const target = path.join(packageRoot, entry);
  await assertSafeParentPath(target);
  await rm(target, { recursive: true, force: true });
}

async function planBackupIfNeeded(targetRelativePath) {
  const target = path.join(packageRoot, targetRelativePath);
  const targetStat = await tryLstat(target);
  if (!targetStat) {
    return null;
  }

  const backupName = `${targetRelativePath.replaceAll(path.sep, '__')}${BACKUP_SUFFIX}`;
  const backup = path.join(BACKUP_DIRECTORY, backupName);
  return path.relative(packageRoot, backup);
}

async function backupTarget(targetRelativePath, backupRelativePath) {
  if (!backupRelativePath) {
    return;
  }

  const target = path.join(packageRoot, targetRelativePath);
  const backup = path.join(packageRoot, backupRelativePath);
  await ensureParentDir(backup);
  await assertSafeParentPath(target);
  await assertSafeParentPath(backup);
  await rename(target, backup);
}

async function restoreBackupIfNeeded(targetRelativePath, backupRelativePath) {
  if (!backupRelativePath) {
    return false;
  }

  const target = path.join(packageRoot, targetRelativePath);
  const backup = path.join(packageRoot, backupRelativePath);
  await assertSafeParentPath(target);
  await assertSafeParentPath(backup);
  const backupStat = await tryLstat(backup);
  if (!backupStat) {
    return false;
  }

  await rm(target, { recursive: true, force: true });
  await ensureParentDir(target);
  await assertSafeParentPath(target);
  await cp(backup, target, { recursive: backupStat.isDirectory(), verbatimSymlinks: true });
  return true;
}

async function writeState(state) {
  const temporaryStateFile = path.join(packageRoot, `.sync-licenses-state-${randomUUID()}.tmp`);
  try {
    await writeFile(temporaryStateFile, JSON.stringify(state, null, 2), { encoding: 'utf8', flag: 'wx' });
    await rename(temporaryStateFile, STATE_FILE);
  } finally {
    await rm(temporaryStateFile, { force: true });
  }
}

async function readState() {
  const stateStat = await tryLstat(STATE_FILE);
  if (stateStat?.isSymbolicLink() || (stateStat && !stateStat.isFile())) {
    throw new Error(`State file (${path.relative(packageRoot, STATE_FILE)}) is not a regular file. Refusing cleanup.`);
  }
  let raw;
  try {
    raw = await readFile(STATE_FILE, 'utf8');
  } catch (error) {
    if (error && error.code === 'ENOENT') {
      throw new Error(`State file (${path.relative(packageRoot, STATE_FILE)}) is missing. Refusing cleanup.`, {
        cause: error,
      });
    }
    throw new Error(`State file (${path.relative(packageRoot, STATE_FILE)}) cannot be read. Refusing cleanup.`, {
      cause: error,
    });
  }
  try {
    return JSON.parse(raw);
  } catch (error) {
    throw new Error(`State file (${path.relative(packageRoot, STATE_FILE)}) is not valid JSON. Refusing cleanup.`, {
      cause: error,
    });
  }
}

function isPlainObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function assertExactKeys(value, expectedKeys, location) {
  const actualKeys = Object.keys(value).sort();
  const sortedExpectedKeys = [...expectedKeys].sort();
  if (
    actualKeys.length !== sortedExpectedKeys.length ||
    actualKeys.some((key, index) => key !== sortedExpectedKeys[index])
  ) {
    throw new Error(`State file ${location} must contain exactly these fields: ${sortedExpectedKeys.join(', ')}.`);
  }
}

function validateState(state) {
  const invalidState = (message) =>
    new Error(`State file (${path.relative(packageRoot, STATE_FILE)}) is invalid: ${message} Refusing cleanup.`);
  if (!isPlainObject(state)) {
    throw invalidState('the root value must be an object.');
  }
  try {
    assertExactKeys(state, ['version', 'createdBackupDirectory', 'createdDirectories', 'items'], 'root object');
  } catch (error) {
    throw invalidState(error.message);
  }
  if (![1, 2].includes(state.version)) {
    throw invalidState(`unsupported version ${JSON.stringify(state.version)}; expected version 1 or 2.`);
  }
  if (typeof state.createdBackupDirectory !== 'boolean') {
    throw invalidState('createdBackupDirectory must be a boolean.');
  }
  if (!Array.isArray(state.createdDirectories)) {
    throw invalidState('createdDirectories must be an array.');
  }
  if (!Array.isArray(state.items)) {
    throw invalidState('items must be an array.');
  }

  const allowedTargets = new Set(LICENSE_ITEMS.map((item) => item.target));
  const allowedCreatedDirectories = new Set(
    LICENSE_ITEMS.map((item) => path.dirname(item.target)).filter((directory) => directory !== '.'),
  );
  const seenCreatedDirectories = new Set();
  for (const createdDirectory of state.createdDirectories) {
    if (typeof createdDirectory !== 'string' || !allowedCreatedDirectories.has(createdDirectory)) {
      throw invalidState(`unexpected or unsafe created directory ${JSON.stringify(createdDirectory)}.`);
    }
    if (seenCreatedDirectories.has(createdDirectory)) {
      throw invalidState(`duplicate created directory ${JSON.stringify(createdDirectory)}.`);
    }
    seenCreatedDirectories.add(createdDirectory);
  }

  const seenTargets = new Set();
  for (const [index, item] of state.items.entries()) {
    if (!isPlainObject(item)) {
      throw invalidState(`items[${index}] must be an object.`);
    }
    try {
      assertExactKeys(
        item,
        state.version === 1 ? ['target', 'backup'] : ['target', 'backup', 'completed'],
        `items[${index}]`,
      );
    } catch (error) {
      throw invalidState(error.message);
    }
    if (typeof item.target !== 'string' || !allowedTargets.has(item.target)) {
      throw invalidState(`unexpected or unsafe target ${JSON.stringify(item.target)}.`);
    }
    if (seenTargets.has(item.target)) {
      throw invalidState(`duplicate target ${JSON.stringify(item.target)}.`);
    }
    seenTargets.add(item.target);

    const expectedCurrentBackup = path.relative(
      packageRoot,
      path.join(BACKUP_DIRECTORY, `${item.target.replaceAll(path.sep, '__')}${BACKUP_SUFFIX}`),
    );
    const expectedLegacyBackup = `${item.target}${BACKUP_SUFFIX}`;
    if (item.backup !== null && item.backup !== expectedCurrentBackup && item.backup !== expectedLegacyBackup) {
      throw invalidState(`unexpected or unsafe backup path ${JSON.stringify(item.backup)}.`);
    }
    if (item.backup === expectedCurrentBackup && state.createdBackupDirectory !== true) {
      throw invalidState('createdBackupDirectory must be true when an item records a managed backup.');
    }
    if (state.version === 2 && typeof item.completed !== 'boolean') {
      throw invalidState(`items[${index}].completed must be a boolean.`);
    }
  }
  if (state.version === 1) {
    return {
      ...state,
      version: 2,
      items: state.items.map((item) => ({ ...item, completed: false })),
    };
  }
  return state;
}

async function handlePrepack() {
  const existingState = await tryLstat(STATE_FILE);
  if (existingState) {
    throw new Error(
      `Found an existing state file (${path.relative(packageRoot, STATE_FILE)}). ` +
        'A previous run likely did not finish. Run "postpack" to restore, ' +
        'or delete the state file manually if you know what you are doing.',
    );
  }

  const existingBackupDirectory = await tryLstat(BACKUP_DIRECTORY);
  const legacyBackupPaths = LICENSE_ITEMS.map((item) => path.join(packageRoot, `${item.target}${BACKUP_SUFFIX}`));
  const existingLegacyBackup = (
    await Promise.all(legacyBackupPaths.map(async (backupPath) => ((await tryLstat(backupPath)) ? backupPath : null)))
  ).find(Boolean);
  if (existingBackupDirectory || existingLegacyBackup) {
    const backupPath = existingLegacyBackup ?? BACKUP_DIRECTORY;
    throw new Error(
      `Found existing backup data (${path.relative(packageRoot, backupPath)}). ` +
        'A previous run likely did not finish. Run "postpack" only when its state file is present; ' +
        'otherwise preserve or recover the backup manually.',
    );
  }

  // Finish the complete preflight before writing state or touching package data.
  await Promise.all(LICENSE_ITEMS.map((item) => preflightSource(item)));
  await Promise.all(LICENSE_ITEMS.map((item) => assertSafeParentPath(path.join(packageRoot, item.target))));
  const backupPlans = [];
  for (const item of LICENSE_ITEMS) {
    const backup = await planBackupIfNeeded(item.target);
    backupPlans.push(backup);
  }
  const createdDirectories = [];
  for (const item of LICENSE_ITEMS) {
    const parent = path.dirname(item.target);
    if (parent !== '.' && !(await tryLstat(path.join(packageRoot, parent)))) {
      createdDirectories.push(parent);
    }
  }

  // All preflight checks passed; now write the initial (empty) state file.
  const state = {
    version: 2,
    createdBackupDirectory: backupPlans.some(Boolean),
    createdDirectories: [...new Set(createdDirectories)],
    items: [],
  };
  await writeState(state);

  for (let index = 0; index < LICENSE_ITEMS.length; index += 1) {
    const item = LICENSE_ITEMS[index];
    const backup = backupPlans[index];
    state.items.push({ target: item.target, backup, completed: false });
    await writeState(state);
    await backupTarget(item.target, backup);
    await copyEntry(item);
  }
  log('Copied shared license artifacts into package root.');
}

async function handlePostpack() {
  // Validate the complete state before any path can be removed, replaced, or rewritten.
  const state = validateState(await readState());

  const cleanupErrors = [];
  for (const item of state.items) {
    if (item.completed) {
      continue;
    }
    try {
      if (!item.backup) {
        await removeEntry(item.target);
      } else {
        const restored = await restoreBackupIfNeeded(item.target, item.backup);
        if (!restored) {
          throw new Error(
            `Recorded backup for ${item.target} is missing; preserved the current target and state file.`,
          );
        }
      }
      item.completed = true;
      try {
        await writeState(state);
      } catch (error) {
        item.completed = false;
        cleanupErrors.push(error);
      }
    } catch (error) {
      cleanupErrors.push(error);
    }
  }

  for (const item of state.items) {
    if (!item.completed || !item.backup) {
      continue;
    }
    try {
      const backup = path.join(packageRoot, item.backup);
      await assertSafeParentPath(backup);
      await rm(backup, { recursive: true, force: true });
    } catch (error) {
      cleanupErrors.push(error);
    }
  }

  for (const createdDirectory of [...state.createdDirectories].reverse()) {
    try {
      await rmdir(path.join(packageRoot, createdDirectory));
    } catch (error) {
      if (!error || !['ENOENT', 'ENOTEMPTY', 'EEXIST'].includes(error.code)) {
        cleanupErrors.push(error);
      }
    }
  }
  if (state.createdBackupDirectory === true) {
    try {
      await rmdir(BACKUP_DIRECTORY);
    } catch (error) {
      if (!error || !['ENOENT', 'ENOTEMPTY', 'EEXIST'].includes(error.code)) {
        cleanupErrors.push(error);
      }
    }
  }
  if (cleanupErrors.length > 0) {
    throw new AggregateError(cleanupErrors, 'One or more recorded license cleanup operations failed.');
  }

  await rm(STATE_FILE, { force: true });
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
