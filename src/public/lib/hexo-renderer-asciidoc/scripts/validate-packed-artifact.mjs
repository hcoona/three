/**
 * Copyright 2015 Shuai Zhang
 * SPDX-License-Identifier: LGPL-3.0-or-later WITH LGPL-3.0-linking-exception
 */

import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import {
  assert,
  captureCleanupFailure,
  capturePathStates,
  collectTarballLicenseChecks,
  createCommandRunner,
  createEvidenceRecorder,
  createFixturePackageJson,
  createTempDirectory,
  EXPECTED_ROOT_DIST_FILES,
  findCommandRecord,
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
  readInstalledTypescriptVersion,
  readJsonFile,
  readTarEntries,
  readTarEntryBuffer,
  readTarEntryText,
  removePath,
  requireExactPnpmVersion,
  restorePathStates,
  sha256Buffer,
  sha256File,
  throwValidationFailures,
  verifyDeclarationMapContents,
  verifyDistInventory,
  verifyExactTarInventory,
  verifyPackCleanup,
  writeJsonFile,
  writeTextFile,
} from './validation-utils.mjs';

const DIST_DIRECTORY = path.join(PACKAGE_ROOT, 'dist');
const README_NPM_PATH = path.join(PACKAGE_ROOT, 'README.npm.md');
const PACKAGE_JSON_PATH = path.join(PACKAGE_ROOT, 'package.json');
const PROBE_DOCUMENT = `== Packed Artifact ==

This document proves the packed ESM and CommonJS entry points stay aligned.

Marker for Artifact.

[source,javascript]
----
const value = { nested: true };
----
`;

const createEsmRuntimeProbe =
  () => `import packageDefault, { registerRenderer, renderer } from 'hexo-renderer-asciidoc';
const sample = ${JSON.stringify(PROBE_DOCUMENT)};
const registrations = [];
const registerResult = registerRenderer({
  extend: {
    renderer: {
      register(...args) {
        registrations.push(args);
      },
    },
  },
});
const outputs = {
  default: await packageDefault({ text: sample }),
  named: await renderer({ text: sample }),
  registered: await Promise.all(
    registrations.map(async ([extension, outputFormat, registeredRenderer, sync]) => ({
      extension,
      html: await registeredRenderer({ text: sample }),
      outputFormat,
      rendererIsNamed: registeredRenderer === renderer,
      sync,
    })),
  ),
};
process.stdout.write(
  JSON.stringify({
    defaultEqualsNamed: packageDefault === renderer,
    defaultType: typeof packageDefault,
    registerRendererType: typeof registerRenderer,
    registerResultIsUndefined: registerResult === undefined,
    rendererType: typeof renderer,
    outputs,
  }),
);
`;

const createCjsRuntimeProbe = () => `const pkg = require('hexo-renderer-asciidoc');
const sample = ${JSON.stringify(PROBE_DOCUMENT)};
const registrations = [];
const registerResult = pkg.registerRenderer({
  extend: {
    renderer: {
      register(...args) {
        registrations.push(args);
      },
    },
  },
});
(async () => {
  process.stdout.write(
    JSON.stringify({
      defaultEqualsNamed: pkg.default === pkg.renderer,
      defaultType: typeof pkg.default,
      keys: Object.keys(pkg).sort(),
      registerRendererType: typeof pkg.registerRenderer,
      registerResultIsUndefined: registerResult === undefined,
      rendererType: typeof pkg.renderer,
      outputs: {
        default: await pkg.default({ text: sample }),
        named: await pkg.renderer({ text: sample }),
        registered: await Promise.all(
          registrations.map(async ([extension, outputFormat, registeredRenderer, sync]) => ({
            extension,
            html: await registeredRenderer({ text: sample }),
            outputFormat,
            rendererIsNamed: registeredRenderer === pkg.renderer,
            sync,
          })),
        ),
      },
    }),
  );
})();
`;

const createEsmTypesProbe = () => `import renderer, {
  registerRenderer,
  renderer as namedRenderer,
  type Hexo,
  type Renderer,
  type RendererData,
  type RendererLocals,
} from 'hexo-renderer-asciidoc';

type Equal<A, B> =
  (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2)
    ? (<T>() => T extends B ? 1 : 2) extends (<T>() => T extends A ? 1 : 2)
      ? true
      : false
    : false;
type Expect<T extends true> = T;

const data: RendererData = { text: '== Bundler ==' };
const locals: RendererLocals = {};
const promise = renderer(data, locals);
const namedPromise = namedRenderer(data, locals);

type _DefaultPromise = Expect<Equal<typeof promise, Promise<string>>>;
type _NamedPromise = Expect<Equal<typeof namedPromise, Promise<string>>>;
type _RegisterRendererReturn = Expect<Equal<ReturnType<typeof registerRenderer>, void>>;

const typedRenderer: Renderer = renderer;
declare const hexo: Hexo;
registerRenderer(hexo);
void typedRenderer;
void promise;
void namedPromise;
`;

const createCjsTypesProbe = () => `import plugin = require('hexo-renderer-asciidoc');
import type { Hexo, Renderer, RendererData, RendererLocals } from 'hexo-renderer-asciidoc';

type Equal<A, B> =
  (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2)
    ? (<T>() => T extends B ? 1 : 2) extends (<T>() => T extends A ? 1 : 2)
      ? true
      : false
    : false;
type Expect<T extends true> = T;

const data: RendererData = { text: '== CommonJS ==' };
const locals: RendererLocals = {};
const promise = plugin.default(data, locals);
const namedPromise = plugin.renderer(data, locals);

type _DefaultPromise = Expect<Equal<typeof promise, Promise<string>>>;
type _NamedPromise = Expect<Equal<typeof namedPromise, Promise<string>>>;
type _RegisterRendererReturn = Expect<Equal<ReturnType<typeof plugin.registerRenderer>, void>>;

const typedRenderer: Renderer = plugin.renderer;
declare const hexo: Hexo;
plugin.registerRenderer(hexo);
void typedRenderer;
void promise;
void namedPromise;
`;

const createTsconfig = (moduleKind, moduleResolution, includePath) =>
  `${JSON.stringify(
    {
      compilerOptions: {
        module: moduleKind,
        moduleResolution,
        noEmit: true,
        strict: true,
        target: 'ES2022',
        verbatimModuleSyntax: true,
      },
      include: [includePath],
    },
    null,
    2,
  )}\n`;

const createHexoConfig = () => `title: packed-artifact-fixture
url: https://example.test
root: /
theme: false
highlight:
  enable: true
  line_number: false
  wrap: false
`;

const createHexoSource = (extension) => `---
layout: false
---
== Packed ${extension} ==

Marker for ${extension}.

[source,javascript]
----
const value = { extension: '${extension}' };
----
`;

const createHexoLoadProbe = () => `import Hexo from 'hexo';
import { createRequire } from 'node:module';

const readRoute = (stream) =>
  new Promise((resolve, reject) => {
    const chunks = [];
    stream.on('data', (chunk) => {
      chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(String(chunk)));
    });
    stream.on('error', reject);
    stream.on('end', () => {
      resolve(Buffer.concat(chunks).toString('utf8'));
    });
  });

const require = createRequire(import.meta.url);
const pluginPath = require.resolve('hexo-renderer-asciidoc');
const hexo = new Hexo(process.cwd(), { debug: false, silent: true });
const errors = [];
hexo.log.error = (...args) => {
  errors.push(args.map((value) => String(value)).join(' '));
};
await hexo.init();
const renderable = {
  ad: hexo.render.isRenderable('packed-ad/index.ad'),
  adoc: hexo.render.isRenderable('packed-adoc/index.adoc'),
  asciidoc: hexo.render.isRenderable('packed-asciidoc/index.asciidoc'),
};
await hexo.load();
const renderableAfterLoad = {
  ad: hexo.render.isRenderable('packed-ad/index.ad'),
  adoc: hexo.render.isRenderable('packed-adoc/index.adoc'),
  asciidoc: hexo.render.isRenderable('packed-asciidoc/index.asciidoc'),
};
const routes = {};
for (const route of ['packed-ad/index.html', 'packed-adoc/index.html', 'packed-asciidoc/index.html']) {
  const stream = hexo.route.get(route);
  routes[route] = stream ? await readRoute(stream) : null;
}
await hexo.loadPlugin(pluginPath);
const renderableAfterManualLoad = {
  ad: hexo.render.isRenderable('packed-ad/index.ad'),
  adoc: hexo.render.isRenderable('packed-adoc/index.adoc'),
  asciidoc: hexo.render.isRenderable('packed-asciidoc/index.asciidoc'),
};
await hexo.exit();
process.stdout.write(JSON.stringify({ errors, pluginPath, renderable, renderableAfterLoad, renderableAfterManualLoad, routes }));
`;

const TYPE_PROBE_DEFINITIONS = [
  {
    key: 'modernEsmBundler',
    label: 'types-probe-esm',
    tsconfigPath: 'probes/tsconfig.esm.json',
    sourceFile: 'types-esm.ts',
    mode: 'modern-esm-bundler',
    module: 'ESNext',
    moduleResolution: 'Bundler',
    exactAssertions: [
      'default renderer(data, locals) infers Promise<string>',
      'named renderer(data, locals) infers Promise<string>',
      'registerRenderer(...) returns void',
      'Hexo, Renderer, RendererData, and RendererLocals import successfully',
    ],
  },
  {
    key: 'node16',
    label: 'types-probe-node16',
    tsconfigPath: 'probes/tsconfig.node16.json',
    sourceFile: 'types-cjs.cts',
    mode: 'commonjs-compatible',
    module: 'Node16',
    moduleResolution: 'Node16',
    exactAssertions: [
      'CommonJS default renderer(data, locals) infers Promise<string>',
      'CommonJS named renderer(data, locals) infers Promise<string>',
      'plugin.registerRenderer(...) returns void',
      'Hexo, Renderer, RendererData, and RendererLocals import successfully',
    ],
  },
  {
    key: 'nodeNext',
    label: 'types-probe-nodenext',
    tsconfigPath: 'probes/tsconfig.nodenext.json',
    sourceFile: 'types-cjs.cts',
    mode: 'commonjs-compatible',
    module: 'NodeNext',
    moduleResolution: 'NodeNext',
    exactAssertions: [
      'CommonJS default renderer(data, locals) infers Promise<string>',
      'CommonJS named renderer(data, locals) infers Promise<string>',
      'plugin.registerRenderer(...) returns void',
      'Hexo, Renderer, RendererData, and RendererLocals import successfully',
    ],
  },
];

const createLoadMarkerWrappers = (installedPackageRoot) => {
  const distDirectory = path.join(installedPackageRoot, 'dist');
  const markerLog = path.join(installedPackageRoot, '.load-markers.log');
  const esmPath = path.join(distDirectory, 'index.js');
  const esmOriginalPath = path.join(distDirectory, 'index.original.js');
  const cjsPath = path.join(distDirectory, 'index.cjs');
  const cjsOriginalPath = path.join(distDirectory, 'index.original.cjs');

  renameSync(esmPath, esmOriginalPath);
  renameSync(cjsPath, cjsOriginalPath);

  writeFileSync(
    esmPath,
    `import { appendFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const markerLog = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '.load-markers.log');
appendFileSync(markerLog, 'esm\\n');
export * from './index.original.js';
export { default } from './index.original.js';
`,
    'utf8',
  );
  writeFileSync(
    cjsPath,
    `const { appendFileSync } = require('node:fs');
const path = require('node:path');
appendFileSync(path.join(__dirname, '..', '.load-markers.log'), 'cjs\\n');
module.exports = require('./index.original.cjs');
`,
    'utf8',
  );
  return markerLog;
};

const validateRenderedHtml = (html, extension) => {
  assert(html.includes(`Packed ${extension}`), `Missing heading marker for ${extension}.`);
  assert(html.includes(`Marker for ${extension}.`), `Missing body marker for ${extension}.`);
  assert(html.includes('<code class="highlight javascript">'), `Missing highlighted code block for ${extension}.`);
  assert(html.includes('&#123;'), `Missing escaped opening brace for ${extension}.`);
  assert(html.includes('&#125;'), `Missing escaped closing brace for ${extension}.`);
  assert(!html.includes('[object Promise]'), `Rendered HTML leaked [object Promise] for ${extension}.`);
};

const main = () => {
  const args = parseArgs(process.argv.slice(2));
  const evidenceDirectory = args.get('--evidence-dir');
  const repositoryRoot = path.resolve(args.get('--repository-root') ?? MONOREPO_ROOT);
  const sessionRoot = path.resolve(args.get('--session-root') ?? evidenceDirectory ?? repositoryRoot);
  const evidencePathOptions = { repositoryRoot, sessionRoot };
  const providedTarballPath = args.get('--tarball-path');
  const providedReadmeSourcePath = args.get('--readme-source-path');
  let operationError;
  let summary;
  let recorder;
  let runner;
  let pnpmVersion;
  let typescriptVersion;
  let tempRoot;
  let artifactsDirectory;
  let consumerDirectory;
  let initialLifecycleState;
  let initialDistState;
  let initialPackageJson;
  let initialReadme;
  let initialReadmeNpm;
  let normalPackCleanupVerified = providedTarballPath !== undefined;
  const cleanupErrors = [];
  try {
    recorder = createEvidenceRecorder(evidenceDirectory, evidencePathOptions);
    runner = createCommandRunner(evidenceDirectory, undefined, evidencePathOptions);
    pnpmVersion = requireExactPnpmVersion();
    typescriptVersion = readInstalledTypescriptVersion();
    tempRoot = createTempDirectory('hexo-renderer-asciidoc-pack-');
    injectValidationFault('packed-artifact:after-temp-directory');
    artifactsDirectory = path.join(tempRoot, 'artifacts');
    consumerDirectory = path.join(tempRoot, 'consumer');
    initialLifecycleState = capturePathStates(PACKAGE_ROOT, PACK_LIFECYCLE_PATHS);
    initialDistState = capturePathStates(PACKAGE_ROOT, ['dist']);
    initialPackageJson = readFileSync(PACKAGE_JSON_PATH);
    initialReadme = readFileSync(path.join(PACKAGE_ROOT, 'README.md'));
    initialReadmeNpm = existsSync(README_NPM_PATH) ? readFileSync(README_NPM_PATH) : undefined;
    mkdirSync(artifactsDirectory, { recursive: true });
    mkdirSync(consumerDirectory, { recursive: true });

    let distFiles = [];
    let tarballPath = providedTarballPath ? path.resolve(providedTarballPath) : '';
    let performedFreshBuild = false;
    let packMode = 'provided-tarball';
    if (providedTarballPath) {
      assert(existsSync(tarballPath), `Provided tarball does not exist: ${tarballPath}`);
    } else {
      runner.run('pnpm', ['run', 'test'], { label: 'candidate-tests' });
      runner.run('pnpm', ['run', 'typecheck'], { label: 'candidate-typecheck' });
      removePath(DIST_DIRECTORY);
      assert(!existsSync(DIST_DIRECTORY), 'dist/ must be absent before the fresh validation build.');
      runner.run('pnpm', ['run', 'build'], { label: 'build-fresh' });
      distFiles = verifyDistInventory(DIST_DIRECTORY);
      performedFreshBuild = true;
      packMode = 'pnpm-pack';

      const packEnvironment = getPackEnvironment();
      runner.run('pnpm', ['pack', '--pack-destination', artifactsDirectory], {
        env: packEnvironment,
        label: 'pnpm-pack',
      });

      const tarballs = listDirectoryFiles(artifactsDirectory).filter((entry) => entry.endsWith('.tgz'));
      assert(tarballs.length === 1, `Expected one pnpm tarball, found ${tarballs.length}.`);
      tarballPath = path.join(artifactsDirectory, tarballs[0]);
      verifyPackCleanup({
        expectedLifecycleState: initialLifecycleState,
        expectedPackageJson: initialPackageJson,
        expectedReadme: initialReadme,
        expectedReadmeNpm: initialReadmeNpm,
      });
      normalPackCleanupVerified = true;
    }
    const tarballHash = sha256File(tarballPath);

    const tarEntries = readTarEntries(tarballPath);
    const packedManifest = JSON.parse(readTarEntryText(tarballPath, 'package/package.json'));
    const packedReadme = readTarEntryBuffer(tarballPath, 'package/README.md');
    const packedChangelog = readTarEntryBuffer(tarballPath, 'package/CHANGELOG.md');
    const repositoryReadme = readFileSync(
      providedReadmeSourcePath ? path.resolve(providedReadmeSourcePath) : README_NPM_PATH,
    );
    const repositoryChangelog = readFileSync(path.join(PACKAGE_ROOT, 'CHANGELOG.md'));
    const packedDistFiles = tarEntries
      .filter((entry) => entry.startsWith('package/dist/'))
      .map((entry) => entry.slice('package/dist/'.length))
      .filter((entry) => entry.length > 0 && !entry.includes('/'))
      .sort();
    if (!performedFreshBuild) {
      distFiles = packedDistFiles;
    }
    assert(Buffer.compare(packedReadme, repositoryReadme) === 0, 'Packed README.md does not byte-match README.npm.md.');
    assert(
      Buffer.compare(packedChangelog, repositoryChangelog) === 0,
      'Packed CHANGELOG.md does not byte-match the package CHANGELOG.md.',
    );
    assert(!tarEntries.includes('package/README.npm.md'), 'README.npm.md must not be published.');
    verifyExactTarInventory(
      tarEntries,
      [
        'package/package.json',
        'package/README.md',
        'package/CHANGELOG.md',
        ...ROOT_LICENSE_ITEMS.map((licenseItem) => `package/${licenseItem}`),
        ...EXPECTED_ROOT_DIST_FILES.map((distFile) => `package/dist/${distFile}`),
      ],
      OPTIONAL_ROOT_DIST_FILES.map((distFile) => `package/dist/${distFile}`),
    );
    for (const distFile of EXPECTED_ROOT_DIST_FILES) {
      assert(tarEntries.includes(`package/dist/${distFile}`), `Tarball is missing dist/${distFile}.`);
    }
    // Nested dist entries (paths containing '/') are forbidden.
    const nestedTarDistEntries = tarEntries
      .filter((entry) => entry.startsWith('package/dist/'))
      .map((entry) => entry.slice('package/dist/'.length))
      .filter((entry) => entry.length > 0 && entry.includes('/'));
    assert(
      nestedTarDistEntries.length === 0,
      `Unexpected nested dist entries in tarball: ${nestedTarDistEntries.join(', ')}`,
    );
    for (const optionalDistFile of OPTIONAL_ROOT_DIST_FILES) {
      if (distFiles.includes(optionalDistFile)) {
        assert(
          tarEntries.includes(`package/dist/${optionalDistFile}`),
          `Tarball is missing optional dist/${optionalDistFile}.`,
        );
        const declarationName = optionalDistFile.slice(0, -'.map'.length);
        verifyDeclarationMapContents(
          readTarEntryBuffer(tarballPath, `package/dist/${declarationName}`),
          readTarEntryBuffer(tarballPath, `package/dist/${optionalDistFile}`),
          declarationName,
          optionalDistFile,
        );
      }
    }
    const unexpectedPackedDistFiles = packedDistFiles.filter(
      (file) => !EXPECTED_ROOT_DIST_FILES.includes(file) && !OPTIONAL_ROOT_DIST_FILES.includes(file),
    );
    assert(
      unexpectedPackedDistFiles.length === 0,
      `Unexpected dist artifacts in tarball: ${unexpectedPackedDistFiles.join(', ')}`,
    );
    const licenseChecks = collectTarballLicenseChecks(tarballPath, MONOREPO_ROOT);
    for (const licenseCheck of licenseChecks) {
      assert(tarEntries.includes(`package/${licenseCheck.path}`), `Tarball is missing ${licenseCheck.path}.`);
      assert(licenseCheck.byteEqual, `Packed ${licenseCheck.path} does not byte-match monorepo root.`);
    }
    const forbiddenTarballEntries = tarEntries.filter(
      (entry) =>
        /^package\/(?:src|test|tests|node_modules)\//.test(entry) ||
        /(?:fixture|fixtures|tmp|temp)/.test(entry) ||
        /(?:sync-licenses-backup|npm-backup)/.test(entry),
    );
    assert(forbiddenTarballEntries.length === 0, `Unexpected tarball entries: ${forbiddenTarballEntries.join(', ')}`);

    assert(packedManifest.main === './dist/index.cjs', 'Packed main must stay on the CommonJS artifact.');
    assert(
      packedManifest.version !== PLACEHOLDER_VERSION,
      'Packed version must be the non-placeholder version stamped by prepack/NBGV.',
    );
    assert(packedManifest.types === './dist/index.d.ts', 'Packed types must point at the ESM declarations.');
    assert(packedManifest.engines?.node === '>=22', 'Packed engines.node must remain >=22.');
    assert(
      packedManifest.dependencies?.['@asciidoctor/core'] === '4.0.4',
      'Packed dependency @asciidoctor/core must be 4.0.4.',
    );
    assert(!('asciidoctor' in (packedManifest.dependencies ?? {})), 'Packed manifest must not depend on asciidoctor.');
    assert(
      packedManifest.exports?.['.']?.import?.default === './dist/index.js',
      'exports.import.default must point to dist/index.js.',
    );
    assert(
      packedManifest.exports?.['.']?.import?.types === './dist/index.d.ts',
      'exports.import.types must point to dist/index.d.ts.',
    );
    assert(
      packedManifest.exports?.['.']?.require?.default === './dist/index.cjs',
      'exports.require.default must point to dist/index.cjs.',
    );
    assert(
      packedManifest.exports?.['.']?.require?.types === './dist/index.d.cts',
      'exports.require.types must point to dist/index.d.cts.',
    );
    assert(
      packedManifest.exports?.['.']?.default === './dist/index.js',
      'exports.default must point to dist/index.js.',
    );

    const fixturePackageJson = createFixturePackageJson(
      providedTarballPath ? `file:${tarballPath}` : `file:../artifacts/${path.basename(tarballPath)}`,
      typescriptVersion,
    );
    writeJsonFile(path.join(consumerDirectory, 'package.json'), fixturePackageJson);
    recorder.writeJson('inputs/packed-consumer.package.json', fixturePackageJson);
    runner.run('pnpm', ['install', '--lockfile-only', '--ignore-scripts'], {
      cwd: consumerDirectory,
      label: 'consumer-install-lockfile-only',
    });
    const consumerLockPath = path.join(consumerDirectory, 'pnpm-lock.yaml');
    const consumerLock = readFileSync(consumerLockPath);
    recorder.writeBuffer('inputs/packed-consumer.pnpm-lock.yaml', consumerLock);
    runner.run('pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], {
      cwd: consumerDirectory,
      label: 'consumer-install-frozen',
    });
    const resolvedGraph = JSON.parse(
      runner.run('pnpm', ['list', '--json', '--depth', 'Infinity'], {
        cwd: consumerDirectory,
        label: 'consumer-resolved-graph',
      }),
    );

    const installedPackageRoot = path.join(consumerDirectory, 'node_modules', 'hexo-renderer-asciidoc');
    const probesDirectory = path.join(consumerDirectory, 'probes');
    mkdirSync(probesDirectory, { recursive: true });
    const probeFiles = {
      'runtime-esm.mjs': createEsmRuntimeProbe(),
      'runtime-cjs.cjs': createCjsRuntimeProbe(),
      'types-esm.ts': createEsmTypesProbe(),
      'types-cjs.cts': createCjsTypesProbe(),
      'hexo-load.mjs': createHexoLoadProbe(),
      'tsconfig.esm.json': createTsconfig('ESNext', 'Bundler', 'types-esm.ts'),
      'tsconfig.node16.json': createTsconfig('Node16', 'Node16', 'types-cjs.cts'),
      'tsconfig.nodenext.json': createTsconfig('NodeNext', 'NodeNext', 'types-cjs.cts'),
    };
    for (const [fileName, content] of Object.entries(probeFiles)) {
      const targetPath = path.join(probesDirectory, fileName);
      writeTextFile(targetPath, content);
      recorder.writeText(path.posix.join('inputs', 'probes', fileName), content);
    }

    for (const typeProbe of TYPE_PROBE_DEFINITIONS) {
      runner.run('pnpm', ['exec', 'tsc', '--project', typeProbe.tsconfigPath], {
        cwd: consumerDirectory,
        label: typeProbe.label,
      });
    }

    writeTextFile(path.join(consumerDirectory, '_config.yml'), createHexoConfig());
    for (const extension of ['ad', 'adoc', 'asciidoc']) {
      const sourceDirectory = path.join(consumerDirectory, 'source', `packed-${extension}`);
      mkdirSync(sourceDirectory, { recursive: true });
      writeTextFile(path.join(sourceDirectory, `index.${extension}`), createHexoSource(extension));
    }
    const hexoProbe = JSON.parse(
      runner.run('node', [path.join('probes', 'hexo-load.mjs')], {
        cwd: consumerDirectory,
        label: 'hexo-load',
      }),
    );

    const markerLogPath = createLoadMarkerWrappers(installedPackageRoot);
    removePath(markerLogPath);
    const esmProbe = JSON.parse(
      runner.run('node', [path.join('probes', 'runtime-esm.mjs')], {
        cwd: consumerDirectory,
        label: 'runtime-probe-esm',
      }),
    );
    const esmMarkers = existsSync(markerLogPath)
      ? readFileSync(markerLogPath, 'utf8').trim().split('\n').filter(Boolean)
      : [];
    removePath(markerLogPath);
    const cjsProbe = JSON.parse(
      runner.run('node', [path.join('probes', 'runtime-cjs.cjs')], {
        cwd: consumerDirectory,
        label: 'runtime-probe-cjs',
      }),
    );
    const cjsMarkers = existsSync(markerLogPath)
      ? readFileSync(markerLogPath, 'utf8').trim().split('\n').filter(Boolean)
      : [];

    assert(esmProbe.defaultType === 'function', 'ESM default import must be callable.');
    assert(esmProbe.rendererType === 'function', 'ESM named renderer must be callable.');
    assert(esmProbe.registerRendererType === 'function', 'ESM registerRenderer must be callable.');
    assert(esmProbe.defaultEqualsNamed === true, 'ESM default export must equal the named renderer.');
    assert(esmProbe.registerResultIsUndefined === true, 'registerRenderer must return void in ESM.');
    assert(
      JSON.stringify(esmMarkers) === JSON.stringify(['esm']),
      `ESM import delegated to CommonJS: ${esmMarkers.join(', ')}`,
    );
    assert(cjsProbe.defaultType === 'function', 'CommonJS default export must be callable.');
    assert(cjsProbe.rendererType === 'function', 'CommonJS named renderer must be callable.');
    assert(cjsProbe.registerRendererType === 'function', 'CommonJS registerRenderer must be callable.');
    assert(cjsProbe.defaultEqualsNamed === true, 'CommonJS default export must equal the named renderer.');
    assert(cjsProbe.registerResultIsUndefined === true, 'registerRenderer must return void in CommonJS.');
    assert(
      JSON.stringify(cjsProbe.keys) === JSON.stringify(['default', 'registerRenderer', 'renderer']),
      `Unexpected CommonJS export keys: ${cjsProbe.keys.join(', ')}`,
    );
    assert(
      JSON.stringify(cjsMarkers) === JSON.stringify(['cjs']),
      `CommonJS probe did not load index.cjs directly: ${cjsMarkers.join(', ')}`,
    );
    assert(esmProbe.outputs.default === esmProbe.outputs.named, 'ESM default and named outputs diverged.');
    assert(cjsProbe.outputs.default === cjsProbe.outputs.named, 'CommonJS default and named outputs diverged.');
    assert(esmProbe.outputs.default === cjsProbe.outputs.default, 'ESM and CommonJS outputs diverged.');
    for (const output of [
      esmProbe.outputs.default,
      esmProbe.outputs.named,
      cjsProbe.outputs.default,
      cjsProbe.outputs.named,
    ]) {
      assert(typeof output === 'string', 'Expected string HTML output.');
      validateRenderedHtml(output, 'Artifact');
    }
    for (const registration of [...esmProbe.outputs.registered, ...cjsProbe.outputs.registered]) {
      assert(registration.outputFormat === 'html', 'Registered renderer output format must be html.');
      assert(registration.rendererIsNamed === true, 'Registered renderer must reference the public renderer function.');
      assert(registration.sync === false, 'Registered renderer must be asynchronous.');
      validateRenderedHtml(registration.html, 'Artifact');
    }

    const publicPages = {};
    for (const extension of ['ad', 'adoc', 'asciidoc']) {
      assert(hexoProbe.renderable[extension] === true, `Hexo did not auto-discover .${extension} as renderable.`);
      const routePath = `packed-${extension}/index.html`;
      const html = hexoProbe.routes[routePath];
      assert(typeof html === 'string', `Hexo did not generate output for .${extension}.`);
      validateRenderedHtml(html, extension);
      publicPages[extension] = {
        path: routePath,
        sha256: sha256Buffer(Buffer.from(html, 'utf8')),
      };
    }
    const typeProbes = Object.fromEntries(
      TYPE_PROBE_DEFINITIONS.map((typeProbe) => {
        const commandRecord = findCommandRecord(runner.commands, typeProbe.label);
        return [
          typeProbe.key,
          {
            command: commandRecord.rendered,
            exactAssertions: typeProbe.exactAssertions,
            exitCode: commandRecord.exitCode,
            label: commandRecord.label,
            mode: typeProbe.mode,
            module: typeProbe.module,
            moduleResolution: typeProbe.moduleResolution,
            outcome: commandRecord.exitCode === 0 ? 'passed' : 'failed',
            sourceFile: typeProbe.sourceFile,
          },
        ];
      }),
    );

    summary = {
      cleanup: undefined,
      candidateChecks: {
        tests: providedTarballPath ? 'not-run-for-provided-tarball' : 'passed',
        typecheck: providedTarballPath ? 'not-run-for-provided-tarball' : 'passed',
      },
      commands: runner.commands,
      consumer: {
        fixtureLockSha256: sha256File(consumerLockPath),
        fixtureManifest: fixturePackageJson,
        resolvedGraph,
      },
      dist: {
        files: distFiles,
        freshBuildPerformed: performedFreshBuild,
        requiredFiles: EXPECTED_ROOT_DIST_FILES,
        optionalFiles: OPTIONAL_ROOT_DIST_FILES.filter((file) => distFiles.includes(file)),
      },
      node: process.version,
      packedArtifact: {
        dependencyVersion: packedManifest.dependencies?.['@asciidoctor/core'],
        engines: packedManifest.engines,
        exports: packedManifest.exports,
        inventory: tarEntries,
        licenseChecks,
        main: packedManifest.main,
        mode: packMode,
        name: packedManifest.name,
        sha256: tarballHash,
        version: packedManifest.version,
      },
      probes: {
        commonjs: cjsProbe,
        esm: esmProbe,
        hexo: hexoProbe,
        publicPages,
        typeProbes,
      },
      pnpmVersion,
      typescriptVersion,
      worktreePackageJsonVersion: readJsonFile(PACKAGE_JSON_PATH).version,
    };
  } catch (error) {
    operationError = error;
  } finally {
    let emergencyRestoreAttempted = false;
    if (operationError !== undefined && initialLifecycleState) {
      emergencyRestoreAttempted = true;
      captureCleanupFailure(cleanupErrors, () => restorePathStates(PACKAGE_ROOT, initialLifecycleState));
    }
    if (initialDistState) {
      captureCleanupFailure(cleanupErrors, () => restorePathStates(PACKAGE_ROOT, initialDistState));
    }
    let finalLifecycleState;
    let finalDistState;
    let finalPackageJsonBytes;
    let finalReadmeBytes;
    let finalReadmeNpmBytes;
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
    if (initialPackageJson) {
      captureCleanupFailure(cleanupErrors, () => {
        finalPackageJsonBytes = readFileSync(PACKAGE_JSON_PATH);
      });
    }
    if (initialReadme) {
      captureCleanupFailure(cleanupErrors, () => {
        finalReadmeBytes = readFileSync(path.join(PACKAGE_ROOT, 'README.md'));
      });
    }
    if (initialReadmeNpm) {
      captureCleanupFailure(cleanupErrors, () => {
        finalReadmeNpmBytes = readFileSync(README_NPM_PATH);
      });
    }
    if (tempRoot) {
      captureCleanupFailure(cleanupErrors, () => {
        removePath(tempRoot);
        injectValidationFault('packed-artifact:after-temp-cleanup');
      });
    }
    let cleanup;
    captureCleanupFailure(cleanupErrors, () => {
      cleanup = {
        distStateRestored:
          initialDistState && finalDistState ? pathStatesEqual(initialDistState, finalDistState) : null,
        emergencyRestoreAttempted,
        lifecycleStateRestored:
          initialLifecycleState && finalLifecycleState
            ? pathStatesEqual(initialLifecycleState, finalLifecycleState)
            : null,
        normalPackCleanupVerified,
        packageJsonRestored: finalPackageJsonBytes
          ? Buffer.compare(initialPackageJson, finalPackageJsonBytes) === 0
          : null,
        readmeNpmRestored: initialReadmeNpm
          ? finalReadmeNpmBytes
            ? Buffer.compare(initialReadmeNpm, finalReadmeNpmBytes) === 0
            : false
          : null,
        readmeRestored: finalReadmeBytes ? Buffer.compare(initialReadme, finalReadmeBytes) === 0 : null,
        temporaryDirectoryRemovedByHarness: tempRoot ? !lexistsSync(tempRoot) : null,
      };
    });
    if (initialLifecycleState) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(
          cleanup?.lifecycleStateRestored,
          'Packed-artifact validation did not restore the pack lifecycle state exactly.',
        ),
      );
    }
    if (initialDistState) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(cleanup?.distStateRestored, 'Packed-artifact validation did not restore dist/ exactly.'),
      );
    }
    if (initialPackageJson) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(cleanup?.packageJsonRestored, 'Packed-artifact validation did not restore package.json byte-for-byte.'),
      );
    }
    if (initialReadme) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(cleanup?.readmeRestored, 'Packed-artifact validation did not restore README.md byte-for-byte.'),
      );
    }
    if (initialReadmeNpm) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(cleanup?.readmeNpmRestored, 'Packed-artifact validation did not restore README.npm.md byte-for-byte.'),
      );
    }
    if (tempRoot) {
      captureCleanupFailure(cleanupErrors, () =>
        assert(cleanup?.temporaryDirectoryRemovedByHarness, 'Packed-artifact temporary directory was not removed.'),
      );
    }
    if (recorder) {
      captureCleanupFailure(cleanupErrors, () => recorder.writeJson('results/packed-artifact-cleanup.json', cleanup));
    }
    if (summary) {
      summary.cleanup = cleanup;
      summary.commands = runner?.commands ?? [];
      captureCleanupFailure(cleanupErrors, () => {
        summary.worktreePackageJsonVersion = readJsonFile(PACKAGE_JSON_PATH).version;
      });
      if (recorder) {
        captureCleanupFailure(cleanupErrors, () => recorder.writeJson('results/packed-artifact-summary.json', summary));
        captureCleanupFailure(cleanupErrors, () => recorder.writeJson('results/evidence-index.json', recorder.writes));
      }
      captureCleanupFailure(cleanupErrors, () => process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`));
    }
  }
  throwValidationFailures(operationError, cleanupErrors, 'Packed-artifact validation or lifecycle cleanup failed.');
};

main();
