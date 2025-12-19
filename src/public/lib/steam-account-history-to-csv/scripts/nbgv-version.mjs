// biome-ignore-all lint/suspicious/noConsole: this unofficial cli tool uses console output intentionally
import { spawn } from 'node:child_process';
import { access, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import * as nbgv from 'nerdbank-gitversioning';
import { getBrowserExtensionVersion, getVersionInfo, projectRoot } from './version-utils.mjs';

const packageJsonPath = path.join(projectRoot, 'package.json');
const PLACEHOLDER_VERSION = '0.0.0-placeholder';

const BIN_DIR = path.join(projectRoot, 'node_modules', '.bin');
const WXT_EXECUTABLE = process.platform === 'win32' ? path.join(BIN_DIR, 'wxt.cmd') : path.join(BIN_DIR, 'wxt');

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
  const versionInfo = await getVersionInfo();
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

function spawnAsync(executablePath, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(executablePath, args, { stdio: 'inherit', shell: false, cwd: projectRoot });
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

async function resolveRunExecutable(toolName) {
  // CodeQL: Avoid executing an attacker-chosen program name from argv.
  // We only support running known tools from this repo's local toolchain.
  if (toolName === 'wxt') {
    try {
      await access(WXT_EXECUTABLE);
    } catch {
      throw new Error(`Cannot find wxt executable at ${WXT_EXECUTABLE}. Did you run pnpm install in this package?`);
    }
    return WXT_EXECUTABLE;
  }

  throw new Error(`Unsupported tool "${toolName}". Only "wxt" is allowed for the run subcommand.`);
}

async function previewVersion() {
  const versionInfo = await getVersionInfo();
  const browserExtensionVersion = getBrowserExtensionVersion(versionInfo);

  const result = {
    npmPackageVersion: versionInfo?.npmPackageVersion,
    browserExtensionVersion,
    simpleVersion: versionInfo?.simpleVersion,
    versionHeight: versionInfo?.versionHeight,
    gitCommitIdShort: versionInfo?.gitCommitIdShort,
  };

  console.log(JSON.stringify(result, null, 2));
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

    let executable;
    try {
      executable = await resolveRunExecutable(rest[0]);
    } catch (error) {
      process.exitCode = 1;
      if (error?.message) {
        console.error(error.message);
      }
      return;
    }

    try {
      await runWithStampedVersion(executable, rest.slice(1));
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
