#!/usr/bin/env node
/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

/**
 * Copies shared license artifacts from the monorepo root into the package root before packing
 * and removes them afterward so that they do not stay tracked in git.
 */

import { cp, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const moduleDir = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(moduleDir, '..');
const LICENSE_ENTRIES = ['COPYING', 'COPYING.LESSER', 'LICENSES'];

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
  const source = path.join(monorepoRoot, entry);
  const target = path.join(packageRoot, entry);
  const sourceStat = await stat(source).catch(() => null);
  if (!sourceStat) {
    throw new Error(`Cannot find "${entry}" in monorepo root (${monorepoRoot}).`);
  }

  await rm(target, { recursive: true, force: true });
  if (sourceStat.isDirectory()) {
    await cp(source, target, { recursive: true });
  } else {
    await cp(source, target);
  }
}

async function removeEntry(entry) {
  const target = path.join(packageRoot, entry);
  await rm(target, { recursive: true, force: true });
}

async function handlePrepack() {
  for (const entry of LICENSE_ENTRIES) {
    await copyEntry(entry);
  }
  log('Copied shared license artifacts into package root.');
}

async function handlePostpack() {
  for (const entry of LICENSE_ENTRIES) {
    await removeEntry(entry);
  }
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
