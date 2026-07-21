/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import {
  assert,
  captureCleanupFailure,
  capturePathStates,
  createCommandRunner,
  createEvidenceRecorder,
  injectValidationFault,
  MONOREPO_ROOT,
  PACKAGE_ROOT,
  parseArgs,
  removePath,
  restorePathStates,
  sha256File,
  throwValidationFailures,
  verifyDistInventory,
} from './validation-utils.mjs';

const EXAMPLE_ROOT = path.join(PACKAGE_ROOT, 'examples', 'hexo-site');
const DIST_DIRECTORY = path.join(PACKAGE_ROOT, 'dist');
const README_PATH = path.join(EXAMPLE_ROOT, 'README.md');
const EXAMPLE_RESTORED_PATHS = ['package.json', 'pnpm-lock.yaml'];
const EXAMPLE_GENERATED_PATHS = ['node_modules', 'public', 'db.json', '.cache'];
const PACKAGE_RELATIVE_PATH = path.relative(MONOREPO_ROOT, PACKAGE_ROOT).replaceAll(path.sep, '/');
const EXAMPLE_RELATIVE_PATH = path.relative(MONOREPO_ROOT, EXAMPLE_ROOT).replaceAll(path.sep, '/');
const EXPECTED_DOCUMENTED_COMMANDS = [
  'mise trust',
  'mise exec -- pnpm install --frozen-lockfile',
  `mise exec -- pnpm --dir ${PACKAGE_RELATIVE_PATH} run build`,
  `mise exec -- pnpm --dir ${EXAMPLE_RELATIVE_PATH} install --frozen-lockfile`,
  `mise exec -- pnpm --dir ${EXAMPLE_RELATIVE_PATH} run generate`,
];
const DOCUMENTED_COMMAND_ARGUMENTS = [
  ['mise', ['trust']],
  ['mise', ['exec', '--', 'pnpm', 'install', '--frozen-lockfile']],
  ['mise', ['exec', '--', 'pnpm', '--dir', PACKAGE_RELATIVE_PATH, 'run', 'build']],
  ['mise', ['exec', '--', 'pnpm', '--dir', EXAMPLE_RELATIVE_PATH, 'install', '--frozen-lockfile']],
  ['mise', ['exec', '--', 'pnpm', '--dir', EXAMPLE_RELATIVE_PATH, 'run', 'generate']],
];
const PLUGIN_LOAD_FAILURE =
  /No renderer found for file:.*\.adoc|Plugin load failed:\s*hexo-renderer-asciidoc|(?:failed|unable) to load.*hexo-renderer-asciidoc|Cannot find module.*dist\/index/isu;
const RAW_ASCIIDOC = /= Meet `hexo-renderer-asciidoc`|= About this demo site|:toc: macro|== Goals/u;

const readDocumentedCommands = () => {
  const readme = readFileSync(README_PATH, 'utf8');
  const match =
    /<!-- linked-example-validation-sequence:start -->\s*```bash\s*([\s\S]*?)\s*```\s*<!-- linked-example-validation-sequence:end -->/u.exec(
      readme,
    );
  assert(match, 'Example README is missing the linked-example validation sequence.');
  return match[1]
    .split(/\r?\n/u)
    .map((line) => line.trim())
    .filter(Boolean);
};

const runDocumentedCommand = (runner, index, label) => {
  const [command, commandArgs] = DOCUMENTED_COMMAND_ARGUMENTS[index];
  return runner.runResult(command, commandArgs, {
    cwd: MONOREPO_ROOT,
    label,
  });
};

const removeExampleGeneratedPaths = () => {
  for (const generatedPath of EXAMPLE_GENERATED_PATHS) {
    removePath(path.join(EXAMPLE_ROOT, generatedPath));
  }
};

const readOutputIfPresent = (outputPath) => (existsSync(outputPath) ? readFileSync(outputPath, 'utf8') : '');

const main = () => {
  const args = parseArgs(process.argv.slice(2));
  const evidenceDirectory = args.get('--evidence-dir');
  const repositoryRoot = path.resolve(args.get('--repository-root') ?? MONOREPO_ROOT);
  const sessionRoot = path.resolve(args.get('--session-root') ?? evidenceDirectory ?? repositoryRoot);
  const evidencePathOptions = { repositoryRoot, sessionRoot };
  let recorder;
  let runner;
  let initialExampleState;
  let initialGeneratedState;
  let initialDistState;
  let operationError;
  const cleanupErrors = [];
  try {
    recorder = createEvidenceRecorder(evidenceDirectory, evidencePathOptions);
    runner = createCommandRunner(evidenceDirectory, undefined, evidencePathOptions);
    initialExampleState = capturePathStates(EXAMPLE_ROOT, EXAMPLE_RESTORED_PATHS);
    injectValidationFault('linked-example:after-example-state');
    initialGeneratedState = capturePathStates(EXAMPLE_ROOT, EXAMPLE_GENERATED_PATHS);
    initialDistState = capturePathStates(PACKAGE_ROOT, ['dist']);
    for (const generatedPath of EXAMPLE_GENERATED_PATHS) {
      assert(
        initialGeneratedState[generatedPath].type === 'missing',
        `Example ${generatedPath} must be absent before linked-example validation.`,
      );
    }

    const documentedCommands = readDocumentedCommands();
    assert(
      JSON.stringify(documentedCommands) === JSON.stringify(EXPECTED_DOCUMENTED_COMMANDS),
      `Example README command sequence differs from validation: ${JSON.stringify(documentedCommands)}.`,
    );

    removePath(DIST_DIRECTORY);
    assert(!existsSync(DIST_DIRECTORY), 'dist/ must be absent before the linked-example package build.');

    // Negative control: trust the clean checkout, then use the example commands with the parent build omitted.
    const negativeTrust = runner.runResult('mise', ['trust'], {
      cwd: MONOREPO_ROOT,
      label: 'negative-mise-trust',
    });
    assert(negativeTrust.exitCode === 0, 'Negative-control mise trust failed.');
    const negativeInstall = runner.runResult(
      'mise',
      ['exec', '--', 'pnpm', '--dir', EXAMPLE_RELATIVE_PATH, 'install', '--frozen-lockfile'],
      { cwd: MONOREPO_ROOT, label: 'negative-example-install-without-parent-build' },
    );
    assert(negativeInstall.exitCode === 0, 'Negative-control example install failed before Hexo could be tested.');
    const negativeGenerate = runner.runResult(
      'mise',
      ['exec', '--', 'pnpm', '--dir', EXAMPLE_RELATIVE_PATH, 'run', 'generate'],
      { cwd: MONOREPO_ROOT, label: 'negative-example-generate-without-parent-build' },
    );
    const negativeIndex = readOutputIfPresent(path.join(EXAMPLE_ROOT, 'public', 'index.html'));
    const negativeAbout = readOutputIfPresent(path.join(EXAMPLE_ROOT, 'public', 'about', 'index.html'));
    const negativeOutput = `${negativeGenerate.stdout}\n${negativeGenerate.stderr}`;
    const negativePluginLoadFailure = PLUGIN_LOAD_FAILURE.test(negativeOutput);
    const negativeRawAsciidoc = RAW_ASCIIDOC.test(`${negativeIndex}\n${negativeAbout}`);
    const negativeMissingRenderedOutput =
      !negativeIndex.includes('Meet hexo-renderer-asciidoc') ||
      !negativeAbout.includes('hexo-renderer-asciidoc integration');
    assert(
      negativeGenerate.exitCode !== 0 ||
        negativePluginLoadFailure ||
        negativeRawAsciidoc ||
        negativeMissingRenderedOutput,
      'Hexo unexpectedly rendered the linked AsciiDoc example without the documented parent-package build.',
    );
    assert(
      negativePluginLoadFailure || negativeRawAsciidoc,
      'Negative-control output did not expose a plugin-load failure or raw AsciiDoc.',
    );
    removeExampleGeneratedPaths();
    assert(!existsSync(DIST_DIRECTORY), 'Negative control unexpectedly built the parent package.');

    // Execute the README sequence verbatim and in order after restoring clean generated state.
    const trust = runDocumentedCommand(runner, 0, 'documented-mise-trust');
    assert(trust.exitCode === 0, `${documentedCommands[0]} failed with exit code ${trust.exitCode}.`);
    const rootInstall = runDocumentedCommand(runner, 1, 'documented-root-frozen-install');
    assert(rootInstall.exitCode === 0, `${documentedCommands[1]} failed with exit code ${rootInstall.exitCode}.`);
    const buildResult = runDocumentedCommand(runner, 2, 'documented-parent-package-build');
    assert(buildResult.exitCode === 0, `${documentedCommands[2]} failed with exit code ${buildResult.exitCode}.`);
    const distFiles = verifyDistInventory(DIST_DIRECTORY);

    const exampleInstall = runDocumentedCommand(runner, 3, 'documented-example-frozen-install');
    assert(exampleInstall.exitCode === 0, `${documentedCommands[3]} failed with exit code ${exampleInstall.exitCode}.`);
    const exampleGenerate = runDocumentedCommand(runner, 4, 'documented-example-generate');
    assert(
      exampleGenerate.exitCode === 0,
      `${documentedCommands[4]} failed with exit code ${exampleGenerate.exitCode}.`,
    );

    const expectedPages = [
      path.join(EXAMPLE_ROOT, 'public', 'index.html'),
      path.join(EXAMPLE_ROOT, 'public', 'about', 'index.html'),
    ];
    for (const page of expectedPages) {
      assert(existsSync(page), `Example output is missing ${path.relative(EXAMPLE_ROOT, page)}.`);
    }
    const indexHtml = readFileSync(expectedPages[0], 'utf8');
    const aboutHtml = readFileSync(expectedPages[1], 'utf8');
    assert(indexHtml.includes('AsciiDoc'), 'Example home page does not contain the expected AsciiDoc marker.');
    assert(aboutHtml.includes('hexo-renderer-asciidoc'), 'Example about page does not contain the package marker.');
    assert(!indexHtml.includes('[object Promise]'), 'Example home page leaked [object Promise].');
    assert(!aboutHtml.includes('[object Promise]'), 'Example about page leaked [object Promise].');
    assert(
      !PLUGIN_LOAD_FAILURE.test(`${exampleGenerate.stdout}\n${exampleGenerate.stderr}`),
      'Documented example generation reported a renderer plugin-load failure.',
    );
    assert(!RAW_ASCIIDOC.test(indexHtml), 'Example home page contains raw AsciiDoc.');
    assert(!RAW_ASCIIDOC.test(aboutHtml), 'Example about page contains raw AsciiDoc.');

    const summary = {
      commands: runner.commands,
      documentedCommands,
      negativeControl: {
        generateExitCode: negativeGenerate.exitCode,
        missingRenderedOutput: negativeMissingRenderedOutput,
        pluginLoadFailure: negativePluginLoadFailure,
        rawAsciidoc: negativeRawAsciidoc,
      },
      setup: {
        distAbsentBeforeBuild: true,
        distFiles,
        freshBuildCommand: documentedCommands[2],
      },
      outputs: expectedPages.map((page) => ({
        path: path.relative(EXAMPLE_ROOT, page),
        sha256: sha256File(page),
      })),
    };
    recorder.writeJson('results/linked-example-summary.json', summary);
    process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
  } catch (error) {
    operationError = error;
  } finally {
    if (initialGeneratedState) {
      captureCleanupFailure(cleanupErrors, () => restorePathStates(EXAMPLE_ROOT, initialGeneratedState));
    }
    if (initialExampleState) {
      captureCleanupFailure(cleanupErrors, () => {
        restorePathStates(EXAMPLE_ROOT, initialExampleState);
        injectValidationFault('linked-example:after-example-state-cleanup');
      });
    }
    if (initialDistState) {
      captureCleanupFailure(cleanupErrors, () => restorePathStates(PACKAGE_ROOT, initialDistState));
    }
  }
  throwValidationFailures(operationError, cleanupErrors, 'Linked-example validation or cleanup failed.');
};

main();
