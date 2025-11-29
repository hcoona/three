/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as nbgv from 'nerdbank-gitversioning';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(__dirname, '..');
const packageJsonPath = path.join(projectRoot, 'package.json');
const PLACEHOLDER_VERSION = '0.0.0-placeholder';

async function loadPackageJson() {
  const content = await readFile(packageJsonPath, 'utf8');
  return JSON.parse(content);
}

async function savePackageJson(pkg) {
  await writeFile(packageJsonPath, `${JSON.stringify(pkg, null, 2)}\n`);
}

async function resetVersion() {
  const pkg = await loadPackageJson();
  if (pkg.version === PLACEHOLDER_VERSION) {
    return;
  }

  pkg.version = PLACEHOLDER_VERSION;
  await savePackageJson(pkg);
  console.log(`Reset package version to placeholder (${PLACEHOLDER_VERSION}).`);
}

async function stampVersion() {
  process.chdir(projectRoot);
  await nbgv.setPackageVersion();
  const versionInfo = await nbgv.getVersion();
  const stampedVersion =
    versionInfo?.npmPackageVersion?.semVer2 ??
    versionInfo?.semVer2 ??
    versionInfo?.semVer1 ??
    versionInfo?.simpleVersion ??
    versionInfo?.version;

  if (stampedVersion) {
    console.log(`Stamped package version to ${stampedVersion}.`);
  }
}

async function main() {
  const [command] = process.argv.slice(2);

  if (command === 'stamp') {
    await stampVersion();
    return;
  }

  if (command === 'reset') {
    await resetVersion();
    return;
  }

  console.error('Usage: node ./scripts/nbgv-version.mjs <stamp|reset>');
  process.exitCode = 1;
}

await main();
