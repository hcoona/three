/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  chmodSync,
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
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';

export const PACKAGE_ROOT = path.resolve(
  process.env.HEXO_RENDERER_ASCIIDOC_PACKAGE_ROOT ?? path.resolve(import.meta.dirname, '..'),
);
export const EXPECTED_HEXO_VERSION = '8.1.2';
export const EXPECTED_PNPM_VERSION = '10.34.5';
export const PLACEHOLDER_VERSION = '0.0.0-placeholder';
export const EXPECTED_ROOT_DIST_FILES = ['index.js', 'index.cjs', 'index.d.ts', 'index.d.cts'];
export const OPTIONAL_ROOT_DIST_FILES = ['index.d.ts.map', 'index.d.cts.map'];
export const PACK_LIFECYCLE_PATHS = [
  'package.json',
  'README.md',
  'README.npm.md',
  '.README.md.npm-backup',
  '.README.npm.md.npm-backup',
  'LICENSE',
  'COPYING',
  'COPYING.LESSER',
  'LICENSES',
  '.sync-licenses-state.json',
  '.sync-licenses-backups',
];
export const ROOT_LICENSE_ITEMS = [
  'LICENSE',
  'COPYING',
  'COPYING.LESSER',
  path.join('LICENSES', 'LGPL-3.0-linking-exception.txt'),
];

const MONOREPO_MARKERS = ['pnpm-workspace.yaml', '.git'];

export const assert = (condition, message) => {
  if (!condition) {
    throw new Error(message);
  }
};

export const lexistsSync = (entryPath) => {
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

export const injectValidationFault = (point) => {
  const requestedFaults = (process.env.HEXO_RENDERER_ASCIIDOC_VALIDATION_FAULTS ?? '')
    .split(',')
    .map((fault) => fault.trim())
    .filter(Boolean);
  if (requestedFaults.includes(point)) {
    throw new Error(`Injected validation fault: ${point}`);
  }
};

export const captureCleanupFailure = (cleanupErrors, operation) => {
  try {
    return operation();
  } catch (error) {
    cleanupErrors.push(error);
    return undefined;
  }
};

export const throwValidationFailures = (operationError, cleanupErrors, message) => {
  if (operationError !== undefined && cleanupErrors.length === 0) {
    throw operationError;
  }
  if (operationError !== undefined || cleanupErrors.length > 0) {
    throw new AggregateError(
      operationError === undefined ? cleanupErrors : [operationError, ...cleanupErrors],
      message,
      operationError === undefined ? undefined : { cause: operationError },
    );
  }
};

export const ensureDir = (directoryPath) => {
  mkdirSync(directoryPath, { recursive: true });
};

export const parseArgs = (argv) => {
  const args = new Map();
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith('--')) {
      continue;
    }
    const [key, inlineValue] = token.split('=', 2);
    if (inlineValue !== undefined) {
      args.set(key, inlineValue);
      continue;
    }
    const next = argv[index + 1];
    if (!next || next.startsWith('--')) {
      args.set(key, 'true');
      continue;
    }
    args.set(key, next);
    index += 1;
  }
  return args;
};

export const findMonorepoRoot = (startDirectory) => {
  let current = startDirectory;
  const { root } = path.parse(current);
  while (true) {
    if (MONOREPO_MARKERS.some((marker) => existsSync(path.join(current, marker)))) {
      return current;
    }
    if (current === root) {
      break;
    }
    current = path.dirname(current);
  }
  throw new Error(`Unable to locate monorepo root above ${startDirectory}.`);
};

export const MONOREPO_ROOT = path.resolve(
  process.env.HEXO_RENDERER_ASCIIDOC_REPOSITORY_ROOT ?? findMonorepoRoot(PACKAGE_ROOT),
);

export const createEvidenceNormalizer = ({
  repositoryRoot = MONOREPO_ROOT,
  sessionRoot = process.env.HEXO_RENDERER_ASCIIDOC_SESSION_ROOT,
  additionalRoots = [],
} = {}) => {
  const replacements = [
    [repositoryRoot, '<repo>'],
    [sessionRoot, '<session>'],
    [tmpdir(), '<tmp>'],
    [homedir(), '<home>'],
    [process.env.HOME, '<home>'],
    ...additionalRoots,
  ]
    .filter(([absolutePath]) => absolutePath && path.isAbsolute(absolutePath))
    .map(([absolutePath, replacement]) => [path.resolve(absolutePath), replacement])
    .sort(([left], [right]) => right.length - left.length);

  return (value) => {
    let normalized = String(value);
    for (const [absolutePath, replacement] of replacements) {
      normalized = normalized.replaceAll(absolutePath, replacement);
    }
    return normalized;
  };
};

const defaultEvidenceNormalizer = createEvidenceNormalizer();
export const normalizeEvidenceText = (value) => defaultEvidenceNormalizer(value);

export const readJsonFile = (filePath) => JSON.parse(readFileSync(filePath, 'utf8'));

export const writeJsonFile = (filePath, data) => {
  ensureDir(path.dirname(filePath));
  writeFileSync(filePath, `${JSON.stringify(data, null, 2)}\n`, 'utf8');
};

export const writeTextFile = (filePath, text) => {
  ensureDir(path.dirname(filePath));
  writeFileSync(filePath, text, 'utf8');
};

export const createTempDirectory = (prefix) => mkdtempSync(path.join(tmpdir(), prefix));

export const sha256Buffer = (value) => createHash('sha256').update(value).digest('hex');

export const sha256File = (filePath) => sha256Buffer(readFileSync(filePath));

const capturePathState = (entryPath) => {
  if (!lexistsSync(entryPath)) {
    return { type: 'missing' };
  }
  const stat = lstatSync(entryPath);
  if (stat.isSymbolicLink()) {
    return {
      type: 'symlink',
      target: readlinkSync(entryPath),
    };
  }
  if (stat.isDirectory()) {
    return {
      type: 'directory',
      mode: stat.mode,
      entries: Object.fromEntries(
        readdirSync(entryPath)
          .sort()
          .map((entry) => [entry, capturePathState(path.join(entryPath, entry))]),
      ),
    };
  }
  return {
    type: 'file',
    mode: stat.mode,
    contents: readFileSync(entryPath),
  };
};

const restorePathState = (entryPath, state) => {
  rmSync(entryPath, { force: true, recursive: true });
  if (state.type === 'missing') {
    return;
  }
  if (state.type === 'directory') {
    mkdirSync(entryPath, { recursive: true });
    for (const [entry, childState] of Object.entries(state.entries)) {
      restorePathState(path.join(entryPath, entry), childState);
    }
    chmodSync(entryPath, state.mode);
    return;
  }
  if (state.type === 'symlink') {
    ensureDir(path.dirname(entryPath));
    symlinkSync(state.target, entryPath);
    return;
  }
  ensureDir(path.dirname(entryPath));
  writeFileSync(entryPath, state.contents);
  chmodSync(entryPath, state.mode);
};

const serializePathState = (state) => {
  if (state.type === 'file') {
    return { ...state, contents: state.contents.toString('base64') };
  }
  if (state.type === 'directory') {
    return {
      ...state,
      entries: Object.fromEntries(
        Object.entries(state.entries).map(([entry, childState]) => [entry, serializePathState(childState)]),
      ),
    };
  }
  return state;
};

export const capturePathStates = (rootDirectory, relativePaths) =>
  Object.fromEntries(
    relativePaths.map((relativePath) => [relativePath, capturePathState(path.join(rootDirectory, relativePath))]),
  );

export const restorePathStates = (rootDirectory, states) => {
  const errors = [];
  for (const [relativePath, state] of Object.entries(states)) {
    try {
      restorePathState(path.join(rootDirectory, relativePath), state);
    } catch (error) {
      errors.push(new Error(`Failed to restore ${relativePath}.`, { cause: error }));
    }
  }
  if (errors.length > 0) {
    throw new AggregateError(errors, `Failed to restore ${errors.length} path state(s).`);
  }
};

export const pathStatesEqual = (left, right) =>
  JSON.stringify(serializePathState(left)) === JSON.stringify(serializePathState(right));

export const collectTarballLicenseChecks = (tarballPath, rootDirectory = MONOREPO_ROOT) =>
  ROOT_LICENSE_ITEMS.map((licenseItem) => {
    const packedBytes = readTarEntryBuffer(tarballPath, `package/${licenseItem}`);
    const rootBytes = readFileSync(path.join(rootDirectory, licenseItem));
    return {
      path: licenseItem,
      packedSha256: sha256Buffer(packedBytes),
      rootSha256: sha256Buffer(rootBytes),
      byteEqual: Buffer.compare(packedBytes, rootBytes) === 0,
    };
  });

export const createEvidenceRecorder = (evidenceDirectory, pathOptions = {}) => {
  if (!evidenceDirectory) {
    return {
      directory: undefined,
      writes: [],
      writeText: () => undefined,
      writeJson: () => undefined,
      writeBuffer: () => undefined,
    };
  }

  ensureDir(evidenceDirectory);
  const writes = [];
  const normalize = createEvidenceNormalizer({
    repositoryRoot: pathOptions.repositoryRoot,
    sessionRoot: pathOptions.sessionRoot ?? evidenceDirectory,
    additionalRoots: pathOptions.additionalRoots,
  });
  const record = (relativePath, bytes) => {
    const absolutePath = path.join(evidenceDirectory, relativePath);
    const normalizedBytes = Buffer.from(normalize(bytes.toString('utf8')), 'utf8');
    ensureDir(path.dirname(absolutePath));
    writeFileSync(absolutePath, normalizedBytes);
    writes.push({
      path: relativePath,
      sha256: sha256Buffer(normalizedBytes),
      size: normalizedBytes.byteLength,
    });
    return absolutePath;
  };

  return {
    directory: evidenceDirectory,
    writes,
    writeText(relativePath, text) {
      return record(relativePath, Buffer.from(text, 'utf8'));
    },
    writeJson(relativePath, data) {
      return record(relativePath, Buffer.from(`${JSON.stringify(data, null, 2)}\n`, 'utf8'));
    },
    writeBuffer(relativePath, bytes) {
      return record(relativePath, bytes);
    },
  };
};

const formatCommandFailure = ({ command, args, cwd, result, stderr }) =>
  [
    `Command: ${[command, ...args].join(' ')}`,
    `Context: cwd=${cwd}`,
    `Status: ${String(result.status)}`,
    `Signal: ${result.signal ?? 'none'}`,
    `Error: ${result.error ? `${result.error.name}: ${result.error.message}` : 'none'}`,
    `Stderr: ${String(stderr).trim() || '<empty>'}`,
  ].join('\n');

const isSpawnFailure = (result) => result.error !== undefined || result.signal !== null || result.status === null;

export const createCommandRunner = (evidenceDirectory, spawn = spawnSync, pathOptions = {}) => {
  const logsDirectory = evidenceDirectory ? path.join(evidenceDirectory, 'logs') : undefined;
  const commands = [];
  const normalize = createEvidenceNormalizer({
    repositoryRoot: pathOptions.repositoryRoot,
    sessionRoot: pathOptions.sessionRoot ?? evidenceDirectory,
    additionalRoots: pathOptions.additionalRoots,
  });
  if (logsDirectory) {
    ensureDir(logsDirectory);
  }

  const execute = (command, args, options = {}) => {
    const cwd = options.cwd ?? PACKAGE_ROOT;
    const env = { ...process.env, ...options.env };
    const encoding = options.binary ? undefined : 'utf8';
    const rendered = [command, ...args].join(' ');
    const result = spawn(command, args, {
      cwd,
      env,
      encoding,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const stdout = result.stdout ?? (options.binary ? Buffer.alloc(0) : '');
    const stderr = result.stderr ?? (options.binary ? Buffer.alloc(0) : '');
    const label = options.label ?? `command-${String(commands.length + 1).padStart(2, '0')}`;
    const commandRecord = {
      label,
      cwd: normalize(cwd),
      command,
      args: args.map((argument) => normalize(argument)),
      rendered: normalize(rendered),
      exitCode: result.status,
      signal: result.signal ?? null,
      error: result.error ? normalize(`${result.error.name}: ${result.error.message}`) : null,
    };
    if (options.phase) {
      commandRecord.phase = options.phase;
    }

    if (logsDirectory) {
      const stdoutName = `${label}.stdout.log`;
      const stderrName = `${label}.stderr.log`;
      writeFileSync(
        path.join(logsDirectory, stdoutName),
        normalize(Buffer.isBuffer(stdout) ? stdout.toString('utf8') : stdout),
      );
      writeFileSync(
        path.join(logsDirectory, stderrName),
        normalize(Buffer.isBuffer(stderr) ? stderr.toString('utf8') : stderr),
      );
      commandRecord.stdout = path.posix.join('logs', stdoutName);
      commandRecord.stderr = path.posix.join('logs', stderrName);
    }
    commands.push(commandRecord);
    if (isSpawnFailure(result) || (result.status !== 0 && options.allowFailure !== true)) {
      throw new Error(formatCommandFailure({ command, args, cwd, result, stderr }));
    }
    return {
      exitCode: result.status,
      stderr,
      stdout,
    };
  };

  return {
    commands,
    run(command, args, options = {}) {
      return execute(command, args, options).stdout;
    },
    runResult(command, args, options = {}) {
      return execute(command, args, { ...options, allowFailure: true });
    },
  };
};

export const findCommandRecord = (commands, label) => {
  const command = commands.find((entry) => entry.label === label);
  assert(command, `Unable to find recorded command ${label}.`);
  return command;
};

export const getPackEnvironment = () => ({
  LC_ALL: 'C.UTF-8',
  SOURCE_DATE_EPOCH: runPlain('git', ['show', '-s', '--format=%ct', 'HEAD'], { cwd: MONOREPO_ROOT }).trim(),
  TZ: 'UTC',
});

export const runPlain = (command, args, options = {}) => {
  const encoding = options.binary ? undefined : 'utf8';
  const result = (options.spawn ?? spawnSync)(command, args, {
    cwd: options.cwd ?? PACKAGE_ROOT,
    env: { ...process.env, ...options.env },
    encoding,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  const stdout = result.stdout ?? (options.binary ? Buffer.alloc(0) : '');
  if (isSpawnFailure(result) || result.status !== 0) {
    const stderr = result.stderr ?? (options.binary ? Buffer.alloc(0) : '');
    throw new Error(
      formatCommandFailure({
        command,
        args,
        cwd: options.cwd ?? PACKAGE_ROOT,
        result,
        stderr,
      }),
    );
  }
  return stdout;
};

export const readTarEntries = (tarballPath) => {
  const output = runPlain('tar', ['-tzf', tarballPath], { cwd: PACKAGE_ROOT }).trim();
  return output.length === 0 ? [] : output.split('\n');
};

export const readTarEntryBuffer = (tarballPath, entryPath) =>
  runPlain('tar', ['-xOzf', tarballPath, entryPath], { binary: true, cwd: PACKAGE_ROOT });

export const readTarEntryText = (tarballPath, entryPath) => readTarEntryBuffer(tarballPath, entryPath).toString('utf8');

export const verifyExactTarEntries = (entries, requiredEntries) => {
  for (const requiredEntry of requiredEntries) {
    const count = entries.filter((entry) => entry === requiredEntry).length;
    assert(count === 1, `Expected exactly one ${requiredEntry} entry, found ${count}.`);
  }
};

export const verifyExactTarInventory = (entries, requiredEntries, optionalEntries = []) => {
  verifyExactTarEntries(entries, requiredEntries);
  const allowedEntries = new Set([...requiredEntries, ...optionalEntries]);
  const unexpectedEntries = entries.filter((entry) => !allowedEntries.has(entry));
  assert(unexpectedEntries.length === 0, `Unexpected tarball entries: ${unexpectedEntries.join(', ')}`);
  for (const optionalEntry of optionalEntries) {
    const count = entries.filter((entry) => entry === optionalEntry).length;
    assert(count <= 1, `Expected at most one ${optionalEntry} entry, found ${count}.`);
  }
};

export const listDirectoryFiles = (directoryPath) =>
  readdirSync(directoryPath, { withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name)
    .sort();

export const removePath = (targetPath) => {
  rmSync(targetPath, { force: true, recursive: true });
};

export const requireExactPnpmVersion = () => {
  const version = runPlain('pnpm', ['--version']).trim();
  assert(
    version === EXPECTED_PNPM_VERSION,
    `Expected pnpm ${EXPECTED_PNPM_VERSION}, received ${version}. Run this validation through mise.`,
  );
  return version;
};

export const readInstalledTypescriptVersion = () =>
  readJsonFile(path.join(PACKAGE_ROOT, 'node_modules', 'typescript', 'package.json')).version;

export const listDistEntries = (directoryPath, relativeTo = directoryPath) => {
  const entries = [];
  const items = readdirSync(directoryPath, { withFileTypes: true });
  for (const item of items) {
    const relPath = path.relative(relativeTo, path.join(directoryPath, item.name)).replace(/\\/g, '/');
    if (item.isDirectory()) {
      entries.push(`${relPath}/`);
      entries.push(...listDistEntries(path.join(directoryPath, item.name), relativeTo));
    } else {
      entries.push(relPath);
    }
  }
  return entries;
};

export const verifyDistInventory = (distDirectoryPath) => {
  // Recursively enumerate all entries in the dist tree to catch unexpected nested artifacts.
  const allDistEntries = listDistEntries(distDirectoryPath);
  const FULL_ALLOWLIST = [...EXPECTED_ROOT_DIST_FILES, ...OPTIONAL_ROOT_DIST_FILES];

  for (const requiredFile of EXPECTED_ROOT_DIST_FILES) {
    assert(allDistEntries.includes(requiredFile), `Missing required dist artifact: ${requiredFile}`);
  }

  const unexpectedEntries = allDistEntries.filter((entry) => !FULL_ALLOWLIST.includes(entry));
  assert(
    unexpectedEntries.length === 0,
    `Unexpected dist entries (nested artifacts not permitted): ${unexpectedEntries.join(', ')}`,
  );

  // Return only the flat file names for callers that need the list.
  const distFiles = allDistEntries.filter((entry) => !entry.endsWith('/'));

  for (const declarationName of ['index.d.ts', 'index.d.cts']) {
    const mapName = `${declarationName}.map`;
    if (distFiles.includes(mapName)) {
      verifyDeclarationMapContents(
        readFileSync(path.join(distDirectoryPath, declarationName)),
        readFileSync(path.join(distDirectoryPath, mapName)),
        declarationName,
        mapName,
      );
    }
  }
  return distFiles;
};

export const verifyDeclarationMapContents = (declarationBytes, mapBytes, declarationName, mapName) => {
  const declaration = declarationBytes.toString('utf8');
  const sourceMap = JSON.parse(mapBytes.toString('utf8'));
  assert(sourceMap.file === declarationName, `${mapName} must target ${declarationName}, received ${sourceMap.file}.`);
  assert(declaration.includes(`//# sourceMappingURL=${mapName}`), `${declarationName} must reference ${mapName}.`);
};

export const verifyPackCleanup = ({
  expectedLifecycleState,
  expectedPackageJson,
  expectedReadme,
  expectedReadmeNpm,
  packageRoot = PACKAGE_ROOT,
} = {}) => {
  const packageJsonPath = path.join(packageRoot, 'package.json');
  const readmePath = path.join(packageRoot, 'README.md');
  const readmeNpmPath = path.join(packageRoot, 'README.npm.md');
  const packageJsonBytes = readFileSync(packageJsonPath);
  const packageJson = JSON.parse(packageJsonBytes.toString('utf8'));
  assert(packageJson.version === PLACEHOLDER_VERSION, 'package.json version was not reset after packing.');
  assert(lexistsSync(readmePath), 'README.md is missing after packing.');
  assert(lexistsSync(readmeNpmPath), 'README.npm.md is missing after packing.');
  if (expectedPackageJson) {
    assert(
      Buffer.compare(packageJsonBytes, expectedPackageJson) === 0,
      'package.json was not restored byte-for-byte after packing.',
    );
  }
  if (expectedReadme) {
    assert(
      Buffer.compare(readFileSync(readmePath), expectedReadme) === 0,
      'README.md was not restored byte-for-byte after packing.',
    );
  }
  if (expectedReadmeNpm) {
    assert(
      Buffer.compare(readFileSync(readmeNpmPath), expectedReadmeNpm) === 0,
      'README.npm.md was not restored byte-for-byte after packing.',
    );
  }
  assert(!lexistsSync(path.join(packageRoot, '.README.md.npm-backup')), 'README backup was not cleaned up.');
  assert(
    !lexistsSync(path.join(packageRoot, '.README.npm.md.npm-backup')),
    'Hidden npm README backup was not cleaned up.',
  );
  assert(
    !lexistsSync(path.join(packageRoot, '.sync-licenses-state.json')),
    'sync-licenses state file was not cleaned up.',
  );
  assert(
    !lexistsSync(path.join(packageRoot, '.sync-licenses-backups')),
    'sync-licenses backup directory was not cleaned up.',
  );
  const currentLifecycleState = capturePathStates(packageRoot, PACK_LIFECYCLE_PATHS);
  if (expectedLifecycleState) {
    assert(
      pathStatesEqual(expectedLifecycleState, currentLifecycleState),
      'Pack lifecycle paths were not restored byte-for-byte after packing.',
    );
  }
  const expectedState = expectedLifecycleState ?? {};
  if (!expectedLifecycleState || expectedState.COPYING?.type === 'missing') {
    assert(!lexistsSync(path.join(packageRoot, 'COPYING')), 'Temporary COPYING file remained in package root.');
  }
  assert(
    (expectedLifecycleState && expectedState['COPYING.LESSER']?.type !== 'missing') ||
      !lexistsSync(path.join(packageRoot, 'COPYING.LESSER')),
    'Temporary COPYING.LESSER file remained in package root.',
  );
  assert(
    (expectedLifecycleState && expectedState.LICENSES?.type !== 'missing') ||
      !lexistsSync(path.join(packageRoot, 'LICENSES')),
    'Temporary LICENSES path remained in package root.',
  );
  return {
    lifecycleState: currentLifecycleState,
    packageJson: packageJsonBytes,
    readme: readFileSync(readmePath),
    readmeNpm: readFileSync(readmeNpmPath),
  };
};

export const createFixturePackageJson = (tarballRelativePath, typescriptVersion) => ({
  name: 'hexo-renderer-asciidoc-packed-consumer',
  private: true,
  packageManager: `pnpm@${EXPECTED_PNPM_VERSION}`,
  type: 'module',
  dependencies: {
    hexo: EXPECTED_HEXO_VERSION,
    'hexo-renderer-asciidoc': tarballRelativePath,
  },
  devDependencies: {
    typescript: typescriptVersion,
  },
  hexo: {
    version: EXPECTED_HEXO_VERSION,
  },
});

export const statPath = (entryPath) => (lexistsSync(entryPath) ? lstatSync(entryPath) : undefined);
