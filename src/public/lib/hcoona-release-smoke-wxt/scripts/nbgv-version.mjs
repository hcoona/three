/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

// biome-ignore-all lint/suspicious/noConsole: this unofficial cli tool uses console output intentionally
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as nbgv from 'nerdbank-gitversioning';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(__dirname, '..');
const packageJsonPath = path.join(projectRoot, 'package.json');
const PLACEHOLDER_VERSION = '0.0.0';

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

async function spawnAsync(executablePath, args) {
  const { spawn } = await import('node:child_process');

  return new Promise((resolve, reject) => {
    const shouldUseShell = process.platform === 'win32' && /\.(cmd|bat)$/i.test(executablePath);

    const child = spawn(executablePath, args, {
      stdio: 'inherit',
      shell: shouldUseShell,
      cwd: projectRoot,
    });
    child.on('error', reject);
    child.on('exit', (code, signal) => {
      if (code === 0) {
        resolve();
      } else {
        const error = new Error(
          code != null ? `Command failed with exit code ${code}` : `Command terminated with signal ${signal}`,
        );
        error.exitCode = code ?? undefined;
        error.signal = signal ?? undefined;
        reject(error);
      }
    });
  });
}

async function resolveWxtCliEntry() {
  const { createRequire } = await import('node:module');

  const require = createRequire(import.meta.url);
  const wxtEntry = require.resolve('wxt', { paths: [projectRoot] });

  let current = path.dirname(wxtEntry);
  const { root } = path.parse(current);
  while (true) {
    const candidate = path.join(current, 'package.json');
    try {
      const pkgRaw = await readFile(candidate, 'utf8');
      const pkg = JSON.parse(pkgRaw);

      const bin = pkg?.bin;
      const binRel = typeof bin === 'string' ? bin : bin?.wxt;
      if (typeof binRel !== 'string' || binRel.length === 0) {
        throw new Error('Unable to resolve WXT CLI entry from wxt package.json (missing "bin").');
      }

      return path.resolve(path.dirname(candidate), binRel);
    } catch {
      // keep walking
    }

    if (current === root) {
      break;
    }
    current = path.dirname(current);
  }

  throw new Error('Unable to locate wxt package.json on disk in node_modules.');
}

async function resolveRunExecutable(toolName, toolArgs) {
  if (toolName === 'wxt') {
    try {
      const wxtCli = await resolveWxtCliEntry();
      return { executablePath: process.execPath, args: [wxtCli, ...toolArgs] };
    } catch {
      // Fall back to the local .bin executable if something about the package layout changed.
    }

    const binDir = path.join(projectRoot, 'node_modules', '.bin');
    const wxtExecutable = process.platform === 'win32' ? path.join(binDir, 'wxt.cmd') : path.join(binDir, 'wxt');
    try {
      await readFile(wxtExecutable);
    } catch {
      throw new Error(`Cannot find wxt executable at ${wxtExecutable}. Did you run pnpm install in this package?`);
    }
    return { executablePath: wxtExecutable, args: toolArgs };
  }

  throw new Error(`Unsupported tool "${toolName}". Only "wxt" is allowed for the run subcommand.`);
}

async function previewVersion() {
  process.chdir(projectRoot);
  const versionInfo = await nbgv.getVersion();
  console.log(
    JSON.stringify(
      {
        npmPackageVersion: versionInfo?.npmPackageVersion,
        simpleVersion: versionInfo?.simpleVersion,
        versionHeight: versionInfo?.versionHeight,
        gitCommitIdShort: versionInfo?.gitCommitIdShort,
      },
      null,
      2,
    ),
  );
}

async function runWithStampedVersion(command, args) {
  await stampVersion();
  try {
    await spawnAsync(command, args);
  } finally {
    await resetVersion();
  }
}

async function main() {
  const [command, ...rest] = process.argv.slice(2);

  if (command === 'stamp') {
    await stampVersion();
    return;
  }

  if (command === 'reset') {
    await resetVersion();
    return;
  }

  if (command === 'preview') {
    await previewVersion();
    return;
  }

  if (command === 'run') {
    if (rest.length === 0) {
      console.error('Usage: node ./scripts/nbgv-version.mjs run wxt [args...]');
      process.exitCode = 1;
      return;
    }

    let resolved;
    try {
      resolved = await resolveRunExecutable(rest[0], rest.slice(1));
    } catch (error) {
      process.exitCode = 1;
      if (error?.message) {
        console.error(error.message);
      }
      return;
    }

    try {
      await runWithStampedVersion(resolved.executablePath, resolved.args);
    } catch (error) {
      if (typeof error.exitCode === 'number') {
        process.exitCode = error.exitCode;
      } else {
        process.exitCode = 1;
      }
      if (error?.message) {
        console.error(error.message);
      }
    }
    return;
  }

  console.error('Usage: node ./scripts/nbgv-version.mjs <stamp|reset|preview|run wxt ...>');
  process.exitCode = 1;
}

await main();
