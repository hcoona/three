/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import {
  assert,
  captureCleanupFailure,
  capturePathStates,
  collectTarballLicenseChecks,
  createCommandRunner,
  createEvidenceRecorder,
  createTempDirectory,
  EXPECTED_ROOT_DIST_FILES,
  getPackEnvironment,
  injectValidationFault,
  lexistsSync,
  listDirectoryFiles,
  MONOREPO_ROOT,
  OPTIONAL_ROOT_DIST_FILES,
  PACK_LIFECYCLE_PATHS,
  PACKAGE_ROOT,
  PLACEHOLDER_VERSION,
  parseArgs,
  pathStatesEqual,
  ROOT_LICENSE_ITEMS,
  readJsonFile,
  readTarEntries,
  readTarEntryBuffer,
  readTarEntryText,
  removePath,
  requireExactPnpmVersion,
  restorePathStates,
  runPlain,
  sha256Buffer,
  sha256File,
  throwValidationFailures,
  verifyDistInventory,
  verifyExactTarInventory,
  verifyPackCleanup,
} from './validation-utils.mjs';

const PACKAGE_JSON_PATH = path.join(PACKAGE_ROOT, 'package.json');
const README_NPM_PATH = path.join(PACKAGE_ROOT, 'README.npm.md');
const DIST_DIRECTORY = path.join(PACKAGE_ROOT, 'dist');
const PREPARE_NPM_PUBLISH_SCRIPT = path.join(MONOREPO_ROOT, 'eng', 'scripts', 'prepare_npm_publish.py');
const EXPECTED_SCOPE = '@hcoona';

const readExpectedAsciidoctorCoreVersion = () => {
  const version = readJsonFile(PACKAGE_JSON_PATH).dependencies?.['@asciidoctor/core'];
  assert(typeof version === 'string', 'Source package must declare an exact @asciidoctor/core dependency.');
  return version;
};

const verifyTarballCommon = (tarballPath, expectedName, expectedVersion, expectedReadmeBytes) => {
  const entries = readTarEntries(tarballPath);
  const manifest = JSON.parse(readTarEntryText(tarballPath, 'package/package.json'));
  const expectedAsciidoctorCoreVersion = readExpectedAsciidoctorCoreVersion();
  assert(manifest.name === expectedName, `Unexpected packed name for ${path.basename(tarballPath)}: ${manifest.name}`);
  assert(
    manifest.version === expectedVersion,
    `Unexpected packed version for ${path.basename(tarballPath)}: ${manifest.version}; expected ${expectedVersion}.`,
  );
  assert(manifest.main === './dist/index.cjs', 'Packed main must stay on dist/index.cjs.');
  assert(
    manifest.dependencies?.['@asciidoctor/core'] === expectedAsciidoctorCoreVersion,
    `Packed dependency @asciidoctor/core must remain ${expectedAsciidoctorCoreVersion}.`,
  );
  assert(!('asciidoctor' in (manifest.dependencies ?? {})), 'Packed manifest must not depend on asciidoctor.');
  assert(!entries.includes('package/README.npm.md'), 'README.npm.md must not be published.');
  verifyExactTarInventory(
    entries,
    [
      'package/package.json',
      'package/README.md',
      'package/CHANGELOG.md',
      ...ROOT_LICENSE_ITEMS.map((licenseItem) => `package/${licenseItem}`),
      ...EXPECTED_ROOT_DIST_FILES.map((distFile) => `package/dist/${distFile}`),
    ],
    OPTIONAL_ROOT_DIST_FILES.map((distFile) => `package/dist/${distFile}`),
  );
  const distFiles = entries
    .filter((entry) => entry.startsWith('package/dist/'))
    .map((entry) => entry.slice('package/dist/'.length))
    .filter((entry) => entry.length > 0 && !entry.includes('/'))
    .sort();
  // Nested dist entries (paths containing '/') are forbidden.
  const nestedDistEntries = entries
    .filter((entry) => entry.startsWith('package/dist/'))
    .map((entry) => entry.slice('package/dist/'.length))
    .filter((entry) => entry.length > 0 && entry.includes('/'));
  assert(nestedDistEntries.length === 0, `Unexpected nested dist entries: ${nestedDistEntries.join(', ')}`);
  for (const distFile of EXPECTED_ROOT_DIST_FILES) {
    assert(entries.includes(`package/dist/${distFile}`), `Packed artifact is missing dist/${distFile}.`);
  }
  const unexpectedDistFiles = distFiles.filter(
    (file) => !EXPECTED_ROOT_DIST_FILES.includes(file) && !OPTIONAL_ROOT_DIST_FILES.includes(file),
  );
  assert(unexpectedDistFiles.length === 0, `Unexpected dist artifacts: ${unexpectedDistFiles.join(', ')}`);
  const forbiddenEntries = entries.filter(
    (entry) =>
      /^package\/(?:src|test|tests|node_modules)\//.test(entry) ||
      /(?:fixture|fixtures|tmp|temp)/.test(entry) ||
      /(?:sync-licenses-backup|npm-backup)/.test(entry),
  );
  assert(forbiddenEntries.length === 0, `Unexpected packaged entries: ${forbiddenEntries.join(', ')}`);
  const licenseChecks = collectTarballLicenseChecks(tarballPath, MONOREPO_ROOT);
  for (const licenseCheck of licenseChecks) {
    assert(entries.includes(`package/${licenseCheck.path}`), `Packed artifact is missing ${licenseCheck.path}.`);
    assert(licenseCheck.byteEqual, `Packed ${licenseCheck.path} does not byte-match the monorepo root.`);
  }
  assert(
    Buffer.compare(readTarEntryBuffer(tarballPath, 'package/README.md'), expectedReadmeBytes) === 0,
    'Packed README.md does not byte-match README.npm.md.',
  );
  assert(
    Buffer.compare(
      readTarEntryBuffer(tarballPath, 'package/CHANGELOG.md'),
      readFileSync(path.join(PACKAGE_ROOT, 'CHANGELOG.md')),
    ) === 0,
    'Packed CHANGELOG.md does not byte-match the package CHANGELOG.md.',
  );
  return {
    distFiles,
    entries,
    forbiddenEntries,
    licenseChecks,
    manifest,
    sha256: sha256File(tarballPath),
  };
};

const verifyEquivalentTarballContent = (gprTarballPath, npmjsTarballPath, gprSummary, npmjsSummary) => {
  assert(
    JSON.stringify(gprSummary.entries) === JSON.stringify(npmjsSummary.entries),
    'GPR and npmjs tarball inventories differ.',
  );
  const normalizedGprManifest = { ...gprSummary.manifest, name: npmjsSummary.manifest.name };
  assert(
    JSON.stringify(normalizedGprManifest) === JSON.stringify(npmjsSummary.manifest),
    'GPR and npmjs package manifests differ beyond the expected package name.',
  );
  for (const entry of gprSummary.entries) {
    if (entry === 'package/package.json') {
      continue;
    }
    assert(
      Buffer.compare(readTarEntryBuffer(gprTarballPath, entry), readTarEntryBuffer(npmjsTarballPath, entry)) === 0,
      `GPR and npmjs tarball content differs at ${entry}.`,
    );
  }
  return {
    identicalEntries: gprSummary.entries.length - 1,
    inventoryEqual: true,
    manifestsEqualExceptName: true,
  };
};

const main = () => {
  const args = parseArgs(process.argv.slice(2));
  const evidenceDirectory = args.get('--evidence-dir');
  const repositoryRoot = path.resolve(args.get('--repository-root') ?? MONOREPO_ROOT);
  const sessionRoot = path.resolve(args.get('--session-root') ?? evidenceDirectory ?? repositoryRoot);
  const evidencePathOptions = { repositoryRoot, sessionRoot };
  const candidateSha = args.get('--candidate-sha') ?? process.env.CANDIDATE_SHA ?? process.env.EXPECTED_CANDIDATE_SHA;
  let recorder;
  let runner;
  let initialHeadSha;
  let initialWorktreeStatus;
  let initialPackageJson;
  let initialReadme;
  let initialReadmeNpm;
  let initialLifecycleState;
  let initialDistState;
  let tempRoot;
  let artifactsDirectory;
  let stateFilePath;
  let prepackAttempted = false;
  let postpackCompleted = false;
  let normalPackCleanupVerified = false;
  let stampedVersion;
  let prepackCount = 0;
  let operationError;
  let summary;
  const cleanupErrors = [];

  try {
    requireExactPnpmVersion();
    initialHeadSha = runPlain('git', ['rev-parse', 'HEAD'], { cwd: MONOREPO_ROOT }).trim();
    const initialShallow = runPlain('git', ['rev-parse', '--is-shallow-repository'], {
      cwd: MONOREPO_ROOT,
    }).trim();
    assert(initialShallow === 'false', 'Release-pack validation requires a non-shallow checkout.');
    if (candidateSha) {
      assert(initialHeadSha === candidateSha, `Candidate SHA ${candidateSha} does not match HEAD ${initialHeadSha}.`);
    }
    initialWorktreeStatus = runPlain(
      'git',
      ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=none'],
      {
        binary: true,
        cwd: MONOREPO_ROOT,
      },
    );
    assert(
      initialWorktreeStatus.length === 0,
      'Release-pack validation requires a clean worktree at start; dirty-checkout evidence is invalid.',
    );
    if (evidenceDirectory) {
      const evidenceRelativePath = path.relative(MONOREPO_ROOT, path.resolve(evidenceDirectory));
      assert(
        evidenceRelativePath.startsWith('..') || path.isAbsolute(evidenceRelativePath),
        'Release-pack evidence must be written outside the candidate worktree.',
      );
    }
    recorder = createEvidenceRecorder(evidenceDirectory, evidencePathOptions);
    runner = createCommandRunner(evidenceDirectory, undefined, evidencePathOptions);
    initialPackageJson = readFileSync(PACKAGE_JSON_PATH);
    initialReadme = readFileSync(path.join(PACKAGE_ROOT, 'README.md'));
    initialReadmeNpm = readFileSync(README_NPM_PATH);
    initialLifecycleState = capturePathStates(PACKAGE_ROOT, PACK_LIFECYCLE_PATHS);
    initialDistState = capturePathStates(PACKAGE_ROOT, ['dist']);
    tempRoot = createTempDirectory('hexo-renderer-asciidoc-release-pack-');
    injectValidationFault('release-pack:after-temp-directory');
    artifactsDirectory = path.join(tempRoot, 'artifacts');
    stateFilePath = path.join(tempRoot, 'npm-publish-state.json');
    mkdirSync(artifactsDirectory, { recursive: true });
    const packEnvironment = getPackEnvironment();

    removePath(DIST_DIRECTORY);
    assert(!existsSync(DIST_DIRECTORY), 'dist/ must be absent before the fresh release build.');
    runner.run('pnpm', ['run', 'build'], {
      label: 'workflow-release-build-fresh',
      phase: 'existing-release-workflow',
    });
    const freshDistFiles = verifyDistInventory(DIST_DIRECTORY);

    prepackAttempted = true;
    runner.run('pnpm', ['run', 'prepack'], {
      label: 'workflow-single-prepack',
      phase: 'existing-release-workflow',
    });
    prepackCount += 1;
    stampedVersion = readJsonFile(PACKAGE_JSON_PATH).version;
    assert(
      typeof stampedVersion === 'string' && stampedVersion.length > 0 && stampedVersion !== PLACEHOLDER_VERSION,
      'The single prepack/NBGV operation did not stamp a non-placeholder package version.',
    );

    runner.run(
      'uv',
      [
        'run',
        '--script',
        PREPARE_NPM_PUBLISH_SCRIPT,
        '--package-dir',
        PACKAGE_ROOT,
        '--scope',
        EXPECTED_SCOPE.slice(1),
        '--state-file',
        stateFilePath,
      ],
      { cwd: MONOREPO_ROOT, label: 'workflow-prepare-gpr-name', phase: 'existing-release-workflow' },
    );
    runner.run('npm', ['pack', '--ignore-scripts', '--pack-destination', artifactsDirectory], {
      cwd: PACKAGE_ROOT,
      env: packEnvironment,
      label: 'workflow-pack-gpr-ignore-scripts',
      phase: 'existing-release-workflow',
    });

    runner.run(
      'uv',
      [
        'run',
        '--script',
        PREPARE_NPM_PUBLISH_SCRIPT,
        '--package-dir',
        PACKAGE_ROOT,
        '--state-file',
        stateFilePath,
        '--restore',
      ],
      { cwd: MONOREPO_ROOT, label: 'workflow-restore-public-name', phase: 'existing-release-workflow' },
    );
    runner.run(
      'node',
      [
        '-e',
        `const fs = require('fs');
const pkg = JSON.parse(fs.readFileSync('package.json', 'utf8'));
if (pkg.private) {
  console.error('Error: package.json has "private": true; refusing to produce an npmjs tarball for public publishing.');
  process.exit(1);
}`,
      ],
      {
        cwd: PACKAGE_ROOT,
        label: 'workflow-verify-package-not-private-npmjs',
        phase: 'existing-release-workflow',
      },
    );
    runner.run('npm', ['pack', '--ignore-scripts', '--pack-destination', artifactsDirectory], {
      cwd: PACKAGE_ROOT,
      env: packEnvironment,
      label: 'workflow-pack-npmjs-ignore-scripts',
      phase: 'existing-release-workflow',
    });
    const releaseWorkflowCommands = runner.commands.filter((command) => command.phase === 'existing-release-workflow');
    assert(
      releaseWorkflowCommands.at(-1)?.label === 'workflow-pack-npmjs-ignore-scripts',
      'The modeled existing release workflow must end after the second npm pack --ignore-scripts.',
    );

    const tarballs = listDirectoryFiles(artifactsDirectory)
      .filter((entry) => entry.endsWith('.tgz'))
      .sort();
    assert(tarballs.length === 2, `Expected two release tarballs, found ${tarballs.length}.`);
    const gprTarballPath = path.join(artifactsDirectory, tarballs.find((entry) => entry.startsWith('hcoona-')) ?? '');
    const npmjsTarballPath = path.join(
      artifactsDirectory,
      tarballs.find((entry) => entry.startsWith('hexo-renderer-asciidoc-')) ?? '',
    );
    assert(existsSync(gprTarballPath), 'GitHub Packages tarball was not produced.');
    assert(existsSync(npmjsTarballPath), 'npmjs tarball was not produced.');

    const gprSummary = verifyTarballCommon(
      gprTarballPath,
      `${EXPECTED_SCOPE}/hexo-renderer-asciidoc`,
      stampedVersion,
      initialReadmeNpm,
    );
    const npmjsSummary = verifyTarballCommon(
      npmjsTarballPath,
      'hexo-renderer-asciidoc',
      stampedVersion,
      initialReadmeNpm,
    );
    const contentIdentity = verifyEquivalentTarballContent(gprTarballPath, npmjsTarballPath, gprSummary, npmjsSummary);
    const npmjsProbeEvidenceDirectory = recorder.directory
      ? path.join(recorder.directory, 'npmjs-tarball-probe')
      : undefined;
    const npmjsProbeArgs = [
      './scripts/validate-packed-artifact.mjs',
      '--tarball-path',
      npmjsTarballPath,
      '--readme-source-path',
      path.join(PACKAGE_ROOT, '.README.npm.md.npm-backup'),
    ];
    if (npmjsProbeEvidenceDirectory) {
      npmjsProbeArgs.push('--evidence-dir', npmjsProbeEvidenceDirectory);
    }
    npmjsProbeArgs.push('--repository-root', repositoryRoot, '--session-root', sessionRoot);
    const npmjsProbeSummary = JSON.parse(
      runner.run('node', npmjsProbeArgs, {
        cwd: PACKAGE_ROOT,
        label: 'local-validation-artifact-probe-npmjs',
        phase: 'local-validation-artifact-validation',
      }),
    );
    assert(
      npmjsProbeSummary.packedArtifact.sha256 === npmjsSummary.sha256,
      'npmjs probe must execute against the exact npmjs dry-run tarball.',
    );
    assert(
      npmjsProbeSummary.packedArtifact.version === stampedVersion,
      'The npmjs consumer probe observed a version other than the single prepack-stamped version.',
    );

    runner.run('pnpm', ['run', 'postpack'], {
      label: 'local-validation-harness-cleanup-postpack',
      phase: 'local-validation-harness-cleanup',
    });
    postpackCompleted = true;
    rmSync(stateFilePath, { force: true });
    verifyPackCleanup({
      expectedLifecycleState: initialLifecycleState,
      expectedPackageJson: initialPackageJson,
      expectedReadme: initialReadme,
      expectedReadmeNpm: initialReadmeNpm,
    });
    normalPackCleanupVerified = true;

    summary = {
      build: {
        distFiles: freshDistFiles,
        fresh: true,
      },
      cleanup: undefined,
      commands: runner.commands,
      checkout: {
        candidateSha: candidateSha ?? null,
        headSha: initialHeadSha,
        nonShallow: true,
        initialWorktreeStatus: [],
        initialWorktreeStatusSha256: sha256Buffer(initialWorktreeStatus),
      },
      contentIdentity,
      expectedScope: EXPECTED_SCOPE,
      npmjsPrivateGuard: {
        label: 'workflow-verify-package-not-private-npmjs',
        outcome: 'passed',
      },
      prepackCount,
      releaseSequence: {
        artifactValidation: {
          contentIdentity: true,
          inventories: true,
          licenseBytes: true,
          npmjsConsumerProbe: true,
        },
        commands: releaseWorkflowCommands,
        endsAfterSecondIgnoreScriptsPack: true,
        label: 'exact-existing-release-workflow-sequence',
        postpackIncluded: false,
      },
      localValidationHarnessOnly: {
        cleanupCommandLabel: 'local-validation-harness-cleanup-postpack',
        label: 'local-validation-harness-only-cleanup-reset-after-release-sequence',
        postpackInvoked: true,
      },
      stampedVersion,
      tarballs: {
        gpr: {
          filename: path.basename(gprTarballPath),
          ...gprSummary,
        },
        npmjs: {
          filename: path.basename(npmjsTarballPath),
          ...npmjsSummary,
          probeSummary: {
            consumerLockSha256: npmjsProbeSummary.consumer.fixtureLockSha256,
            node: npmjsProbeSummary.node,
            packedArtifactSha256: npmjsProbeSummary.packedArtifact.sha256,
            publicPages: npmjsProbeSummary.probes.publicPages,
            runtime: {
              commonjs: {
                defaultEqualsNamed: npmjsProbeSummary.probes.commonjs.defaultEqualsNamed,
                keys: npmjsProbeSummary.probes.commonjs.keys,
              },
              esm: {
                defaultEqualsNamed: npmjsProbeSummary.probes.esm.defaultEqualsNamed,
                defaultType: npmjsProbeSummary.probes.esm.defaultType,
              },
            },
            typeProbes: npmjsProbeSummary.probes.typeProbes,
            typescriptVersion: npmjsProbeSummary.typescriptVersion,
          },
        },
      },
    };
  } catch (error) {
    operationError = error;
  } finally {
    let emergencyRestoreAttempted = false;
    if (operationError !== undefined) {
      if (stateFilePath && lexistsSync(stateFilePath) && runner) {
        emergencyRestoreAttempted = true;
        captureCleanupFailure(cleanupErrors, () => {
          runner.run(
            'uv',
            [
              'run',
              '--script',
              PREPARE_NPM_PUBLISH_SCRIPT,
              '--package-dir',
              PACKAGE_ROOT,
              '--state-file',
              stateFilePath,
              '--restore',
            ],
            {
              cwd: MONOREPO_ROOT,
              label: 'local-validation-emergency-restore-public-name',
              phase: 'local-validation-harness-cleanup',
            },
          );
        });
      }
      if (stateFilePath) {
        captureCleanupFailure(cleanupErrors, () => rmSync(stateFilePath, { force: true }));
      }
      if (prepackAttempted && !postpackCompleted && runner) {
        emergencyRestoreAttempted = true;
        captureCleanupFailure(cleanupErrors, () => {
          runner.run('pnpm', ['run', 'postpack'], {
            label: 'local-validation-emergency-postpack',
            phase: 'local-validation-harness-cleanup',
          });
          postpackCompleted = true;
        });
      }
      if (prepackAttempted && runner) {
        emergencyRestoreAttempted = true;
        captureCleanupFailure(cleanupErrors, () => {
          runner.run('pnpm', ['run', 'version:reset'], {
            label: 'local-validation-emergency-version-reset',
            phase: 'local-validation-harness-cleanup',
          });
        });
      }
      if (initialLifecycleState) {
        emergencyRestoreAttempted = true;
        captureCleanupFailure(cleanupErrors, () => restorePathStates(PACKAGE_ROOT, initialLifecycleState));
      }
    }
    if (initialDistState) {
      captureCleanupFailure(cleanupErrors, () => restorePathStates(PACKAGE_ROOT, initialDistState));
    }
    if (tempRoot) {
      captureCleanupFailure(cleanupErrors, () => {
        removePath(tempRoot);
        injectValidationFault('release-pack:after-temp-cleanup');
      });
    }

    let finalPackageJson;
    let finalReadme;
    let finalReadmeNpm;
    let finalLifecycleState;
    let finalDistState;
    let finalHeadSha;
    let finalShallow;
    let finalWorktreeStatus;
    if (initialPackageJson) {
      captureCleanupFailure(cleanupErrors, () => {
        finalPackageJson = readFileSync(PACKAGE_JSON_PATH);
      });
    }
    if (initialReadme) {
      captureCleanupFailure(cleanupErrors, () => {
        finalReadme = readFileSync(path.join(PACKAGE_ROOT, 'README.md'));
      });
    }
    if (initialReadmeNpm) {
      captureCleanupFailure(cleanupErrors, () => {
        finalReadmeNpm = readFileSync(README_NPM_PATH);
      });
    }
    if (initialLifecycleState) {
      captureCleanupFailure(cleanupErrors, () => {
        finalLifecycleState = capturePathStates(PACKAGE_ROOT, PACK_LIFECYCLE_PATHS);
      });
    }
    if (initialDistState) {
      captureCleanupFailure(cleanupErrors, () => {
        finalDistState = capturePathStates(PACKAGE_ROOT, ['dist']);
      });
    }
    if (initialHeadSha) {
      captureCleanupFailure(cleanupErrors, () => {
        finalHeadSha = runPlain('git', ['rev-parse', 'HEAD'], { cwd: MONOREPO_ROOT }).trim();
      });
      captureCleanupFailure(cleanupErrors, () => {
        finalShallow = runPlain('git', ['rev-parse', '--is-shallow-repository'], {
          cwd: MONOREPO_ROOT,
        }).trim();
      });
      captureCleanupFailure(cleanupErrors, () => {
        finalWorktreeStatus = runPlain(
          'git',
          ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=none'],
          {
            binary: true,
            cwd: MONOREPO_ROOT,
          },
        );
      });
    }

    let cleanupSummary;
    captureCleanupFailure(cleanupErrors, () => {
      cleanupSummary = {
        cleanAtEnd: finalWorktreeStatus ? finalWorktreeStatus.length === 0 : null,
        distStateRestored:
          initialDistState && finalDistState ? pathStatesEqual(initialDistState, finalDistState) : null,
        emergencyRestoreAttempted,
        finalWorktreeStatusSha256: finalWorktreeStatus ? sha256Buffer(finalWorktreeStatus) : null,
        headRestored: initialHeadSha && finalHeadSha ? finalHeadSha === initialHeadSha : null,
        lifecycleStateRestored:
          initialLifecycleState && finalLifecycleState
            ? pathStatesEqual(initialLifecycleState, finalLifecycleState)
            : null,
        nonShallow: finalShallow ? finalShallow === 'false' : null,
        normalPackCleanupVerified,
        packageJsonRestored:
          initialPackageJson && finalPackageJson ? Buffer.compare(initialPackageJson, finalPackageJson) === 0 : null,
        postpackCompleted,
        readmeNpmRestored:
          initialReadmeNpm && finalReadmeNpm ? Buffer.compare(initialReadmeNpm, finalReadmeNpm) === 0 : null,
        readmeRestored: initialReadme && finalReadme ? Buffer.compare(initialReadme, finalReadme) === 0 : null,
        stateFileRemoved: stateFilePath ? !lexistsSync(stateFilePath) : null,
        temporaryDirectoryRemoved: tempRoot ? !lexistsSync(tempRoot) : null,
      };
    });
    for (const [condition, message] of [
      [initialHeadSha ? cleanupSummary?.headRestored : true, 'Release-pack validation changed HEAD.'],
      [initialDistState ? cleanupSummary?.distStateRestored : true, 'Release-pack validation did not restore dist/.'],
      [initialHeadSha ? cleanupSummary?.nonShallow : true, 'Release-pack validation ended in a shallow checkout.'],
      [
        initialLifecycleState ? cleanupSummary?.lifecycleStateRestored : true,
        'Release-pack lifecycle paths were not restored exactly.',
      ],
      [initialPackageJson ? cleanupSummary?.packageJsonRestored : true, 'package.json was not restored byte-for-byte.'],
      [initialReadme ? cleanupSummary?.readmeRestored : true, 'README.md was not restored byte-for-byte.'],
      [initialReadmeNpm ? cleanupSummary?.readmeNpmRestored : true, 'README.npm.md was not restored byte-for-byte.'],
      [stateFilePath ? cleanupSummary?.stateFileRemoved : true, 'The npm publish state file was not removed.'],
      [tempRoot ? cleanupSummary?.temporaryDirectoryRemoved : true, 'The temporary directory was not removed.'],
      [
        initialWorktreeStatus ? cleanupSummary?.cleanAtEnd : true,
        'Release-pack validation requires a clean worktree at end.',
      ],
    ]) {
      captureCleanupFailure(cleanupErrors, () => assert(condition, message));
    }
    if (summary) {
      summary.cleanup = cleanupSummary;
      summary.commands = runner?.commands ?? [];
      summary.checkout.finalHeadSha = finalHeadSha;
      summary.checkout.finalWorktreeStatus = finalWorktreeStatus
        ? finalWorktreeStatus.toString('utf8').split('\0').filter(Boolean)
        : null;
      summary.checkout.finalWorktreeStatusSha256 = finalWorktreeStatus ? sha256Buffer(finalWorktreeStatus) : null;
      if (recorder) {
        captureCleanupFailure(cleanupErrors, () => recorder.writeJson('results/release-pack-summary.json', summary));
      }
    }
    if (recorder) {
      captureCleanupFailure(cleanupErrors, () => {
        recorder.writeJson('results/release-pack-cleanup.json', cleanupSummary);
      });
    }
    captureCleanupFailure(cleanupErrors, () => {
      process.stdout.write(
        `${JSON.stringify(
          {
            cleanup: cleanupSummary,
            commands: runner?.commands ?? [],
            packageVersion: readJsonFile(PACKAGE_JSON_PATH).version,
            prepackCount,
            stampedVersion: stampedVersion ?? null,
          },
          null,
          2,
        )}\n`,
      );
    });
  }
  throwValidationFailures(operationError, cleanupErrors, 'Release-pack validation or lifecycle cleanup failed.');
};

main();
