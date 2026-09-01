import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const REQUEST_SCHEMA = 'workflow-delivery/v3/static-reference-node-authority-request';
const RESPONSE_SCHEMA = 'workflow-delivery/v3/static-reference-node-authority-response';
const REGISTRY = 'https://registry.npmjs.org/';
const DEFAULT_TAG = 'latest';
const require = createRequire(import.meta.url);
const loadedPackages = new Set();

class GraphFailure extends Error {
  constructor(kind) {
    super('static-reference authority graph failed');
    this.kind = kind;
  }
}

function unsupported() {
  throw new GraphFailure('unsupported-projection');
}

function authorityRejected() {
  throw new GraphFailure('authority-rejected');
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function exactString(value) {
  if (typeof value !== 'string') {
    unsupported();
  }
  return value;
}

function optionalString(value) {
  if (value === undefined || value === null) {
    return null;
  }
  return exactString(value);
}

function utf8Compare(left, right) {
  return Buffer.compare(Buffer.from(left, 'utf8'), Buffer.from(right, 'utf8'));
}

function orderedStringEntries(value, { absent = false } = {}) {
  if (value === undefined && absent) {
    return [];
  }
  if (!isRecord(value)) {
    unsupported();
  }
  return Object.keys(value)
    .sort(utf8Compare)
    .map((key) => [key, exactString(value[key])]);
}

function lexicalSnapshotPath(value, baseDirectory, snapshotRoot) {
  const rawPath = exactString(value);
  if (rawPath.startsWith('~')) {
    unsupported();
  }
  const absolute = path.resolve(baseDirectory, rawPath);
  const relative = path.relative(snapshotRoot, absolute);
  if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    unsupported();
  }
  return relative === '' ? '.' : relative.split(path.sep).join('/');
}

function fileReferencePath(value, baseDirectory, snapshotRoot) {
  const reference = exactString(value);
  if (!reference.startsWith('file:')) {
    unsupported();
  }
  return lexicalSnapshotPath(reference.slice('file:'.length), baseDirectory, snapshotRoot);
}

function officialCall(callback) {
  try {
    return callback();
  } catch {
    authorityRejected();
  }
}

async function officialCallAsync(callback) {
  try {
    return await callback();
  } catch {
    authorityRejected();
  }
}

async function importPackage(packageName) {
  const imported = await import(packageName);
  loadedPackages.add(packageName);
  return imported;
}

async function loadedPackageIdentity(packageName) {
  let current = path.dirname(require.resolve(packageName));
  while (true) {
    const manifestPath = path.join(current, 'package.json');
    try {
      const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
      if (manifest?.name === packageName && typeof manifest.version === 'string') {
        return `${packageName}@${manifest.version}`;
      }
    } catch (error) {
      if (error?.code !== 'ENOENT') {
        throw error;
      }
    }
    const parent = path.dirname(current);
    if (parent === current) {
      throw new Error('loaded package identity is unavailable');
    }
    current = parent;
  }
}

async function implementationIdentities() {
  const identities = [`node@${process.versions.node}`];
  for (const packageName of [...loadedPackages].sort(utf8Compare)) {
    identities.push(await loadedPackageIdentity(packageName));
  }
  return identities.sort(utf8Compare);
}

async function preflightUtf8(candidatePath) {
  const content = await readFile(candidatePath);
  try {
    new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(content);
  } catch {
    throw new GraphFailure('encoding-rejected');
  }
  return content;
}

function normalizeNpaResult(result, snapshotDirectory, snapshotRoot, { alias = true } = {}) {
  if (!isRecord(result)) {
    unsupported();
  }
  const resultType = exactString(result.type);
  if (!new Set(['alias', 'directory', 'file', 'git', 'range', 'remote', 'tag', 'version']).has(resultType)) {
    unsupported();
  }
  const name = exactString(result.name);
  const rawSpec = exactString(result.rawSpec);
  const saveSpec = optionalString(result.saveSpec);
  const fetchSpec = optionalString(result.fetchSpec);
  let localPath = null;
  if (resultType === 'directory' || resultType === 'file') {
    if (fetchSpec === null) {
      unsupported();
    }
    localPath = lexicalSnapshotPath(fetchSpec, snapshotDirectory, snapshotRoot);
  }

  let aliasTarget = null;
  if (resultType === 'alias') {
    if (!alias || !isRecord(result.subSpec) || result.subSpec.type === 'alias') {
      unsupported();
    }
    aliasTarget = normalizeNpaResult(result.subSpec, snapshotDirectory, snapshotRoot, { alias: false });
  }
  return {
    aliasTarget,
    fetchSpec,
    localPath,
    name,
    rawSpec,
    saveSpec,
    type: resultType,
  };
}

function resolveNpa(npa, name, specifier, snapshotDirectory, snapshotRoot) {
  const result = officialCall(() => npa.resolve(name, specifier, snapshotDirectory));
  return normalizeNpaResult(result, snapshotDirectory, snapshotRoot);
}

async function npmManifestFacts(request) {
  await preflightUtf8(request.candidatePath);
  const packageJsonModule = await importPackage('@npmcli/package-json');
  const npaModule = await importPackage('npm-package-arg');
  const PackageJson = packageJsonModule.default;
  const npa = npaModule.default;
  if (typeof PackageJson?.load !== 'function' || typeof npa?.resolve !== 'function') {
    unsupported();
  }

  const snapshotDirectory = path.dirname(request.candidatePath);
  const loaded = await officialCallAsync(() => PackageJson.load(snapshotDirectory));
  const content = loaded?.content;
  if (!isRecord(content)) {
    unsupported();
  }

  const facts = [];
  if (Object.hasOwn(content, 'name')) {
    const name = exactString(content.name);
    const parsedName = officialCall(() => npa.resolve(name, '*', snapshotDirectory));
    if (!isRecord(parsedName) || parsedName.name !== name) {
      unsupported();
    }
    facts.push({ context: 'name', kind: 'npm-package-name', name });
  }

  for (const section of ['dependencies', 'devDependencies', 'optionalDependencies', 'peerDependencies']) {
    if (!Object.hasOwn(content, section)) {
      continue;
    }
    for (const [dependencyKey, sourceSpec] of orderedStringEntries(content[section])) {
      facts.push({
        dependencyKey,
        kind: 'npm-reference',
        reference: resolveNpa(npa, dependencyKey, sourceSpec, snapshotDirectory, request.snapshotRoot),
        section,
        sourceSpec,
      });
    }
  }
  return facts;
}

function workspaceReference(
  rawSpecifier,
  dependencyKey,
  snapshotDirectory,
  snapshotRoot,
  WorkspaceSpec,
  workspacePrefToNpm,
  parseBareSpecifier,
  npa,
) {
  const workspaceSpec = officialCall(() => WorkspaceSpec.parse(rawSpecifier));
  if (workspaceSpec === null) {
    return {
      kind: 'npm',
      npm: resolveNpa(npa, dependencyKey, rawSpecifier, snapshotDirectory, snapshotRoot),
    };
  }
  const normalizedSpecifier = officialCall(() => workspacePrefToNpm(rawSpecifier));
  const registrySpec = officialCall(() =>
    parseBareSpecifier(normalizedSpecifier, dependencyKey, DEFAULT_TAG, REGISTRY),
  );
  if (!isRecord(registrySpec)) {
    unsupported();
  }
  return {
    kind: 'workspace',
    workspace: {
      fetchSpec: exactString(registrySpec.fetchSpec),
      name: exactString(registrySpec.name),
      selector: exactString(workspaceSpec.version),
      type: exactString(registrySpec.type),
    },
  };
}

function appendCatalogFacts(facts, catalog, { catalogKind, catalogName }, snapshotDirectory, request, authorities) {
  for (const [dependencyKey, sourceSpec] of orderedStringEntries(catalog)) {
    facts.push({
      catalogKind,
      catalogName,
      dependencyKey,
      kind: 'pnpm-workspace-reference',
      reference: workspaceReference(
        sourceSpec,
        dependencyKey,
        snapshotDirectory,
        request.snapshotRoot,
        authorities.WorkspaceSpec,
        authorities.workspacePrefToNpm,
        authorities.parseBareSpecifier,
        authorities.npa,
      ),
      sourceSpec,
    });
  }
}

async function pnpmWorkspaceFacts(request) {
  await preflightUtf8(request.candidatePath);
  const readerModule = await importPackage('@pnpm/workspace.workspace-manifest-reader');
  const workspaceModule = await importPackage('@pnpm/workspace.spec-parser');
  const resolverModule = await importPackage('@pnpm/resolving.npm-resolver');
  const npaModule = await importPackage('npm-package-arg');
  const authorities = {
    WorkspaceSpec: workspaceModule.WorkspaceSpec,
    npa: npaModule.default,
    parseBareSpecifier: resolverModule.parseBareSpecifier,
    workspacePrefToNpm: resolverModule.workspacePrefToNpm,
  };
  if (
    typeof readerModule.readWorkspaceManifest !== 'function' ||
    typeof authorities.WorkspaceSpec?.parse !== 'function' ||
    typeof authorities.parseBareSpecifier !== 'function' ||
    typeof authorities.workspacePrefToNpm !== 'function' ||
    typeof authorities.npa?.resolve !== 'function'
  ) {
    unsupported();
  }

  const snapshotDirectory = path.dirname(request.candidatePath);
  const manifest = await officialCallAsync(() => readerModule.readWorkspaceManifest(snapshotDirectory));
  if (manifest === undefined || manifest === null) {
    return [];
  }
  if (!isRecord(manifest)) {
    unsupported();
  }

  const facts = [];
  if (Array.isArray(manifest.packages)) {
    for (const [index, pattern] of manifest.packages.entries()) {
      facts.push({
        index,
        kind: 'pnpm-workspace-pattern',
        pattern: exactString(pattern),
      });
    }
  }
  if (manifest.catalog !== undefined && manifest.catalog !== null) {
    appendCatalogFacts(
      facts,
      manifest.catalog,
      { catalogKind: 'default', catalogName: null },
      snapshotDirectory,
      request,
      authorities,
    );
  }
  if (manifest.catalogs !== undefined && manifest.catalogs !== null) {
    if (!isRecord(manifest.catalogs)) {
      unsupported();
    }
    for (const catalogName of Object.keys(manifest.catalogs).sort(utf8Compare)) {
      appendCatalogFacts(
        facts,
        manifest.catalogs[catalogName],
        { catalogKind: 'named', catalogName },
        snapshotDirectory,
        request,
        authorities,
      );
    }
  }
  return facts;
}

function normalizePnpmResolution(resolution, lockfileDirectory, snapshotRoot) {
  if (!isRecord(resolution)) {
    unsupported();
  }
  if (resolution.type === 'directory') {
    return {
      kind: 'directory',
      localPath: lexicalSnapshotPath(resolution.directory, lockfileDirectory, snapshotRoot),
    };
  }
  if (resolution.type === 'git') {
    return {
      commit: exactString(resolution.commit),
      kind: 'git',
      path: optionalString(resolution.path),
      repo: exactString(resolution.repo),
    };
  }
  if (resolution.gitHosted === true) {
    return {
      kind: 'hosted-git',
      path: optionalString(resolution.path),
      tarball: exactString(resolution.tarball),
    };
  }
  if (resolution.type !== undefined) {
    unsupported();
  }
  const tarball = exactString(resolution.tarball);
  if (tarball.startsWith('file:')) {
    return {
      kind: 'file-tarball',
      localPath: fileReferencePath(tarball, lockfileDirectory, snapshotRoot),
    };
  }
  return { kind: 'registry' };
}

function snapshotDependencyEdges(snapshot) {
  const edges = [];
  for (const section of ['dependencies', 'optionalDependencies']) {
    for (const [dependencyKey, reference] of orderedStringEntries(snapshot[section], { absent: true })) {
      edges.push({ dependencyKey, reference, section });
    }
  }
  return edges;
}

function pnpmSnapshots(lockfile, request, authorities) {
  const packages = lockfile.packages === undefined ? {} : lockfile.packages;
  if (!isRecord(packages)) {
    unsupported();
  }
  const lockfileDirectory = path.dirname(request.candidatePath);
  const facts = [];
  const resolutionKinds = new Map();
  for (const dependencyPath of Object.keys(packages).sort(utf8Compare)) {
    const snapshot = packages[dependencyPath];
    if (!isRecord(snapshot)) {
      unsupported();
    }
    const identity = officialCall(() => authorities.nameVerFromPkgSnapshot(dependencyPath, snapshot));
    const resolution = officialCall(() =>
      authorities.pkgSnapshotToResolution(dependencyPath, snapshot, authorities.registryContext),
    );
    if (!isRecord(identity)) {
      unsupported();
    }
    const version = optionalString(identity.version);
    const nonSemverVersion = optionalString(identity.nonSemverVersion);
    if (version === null && nonSemverVersion === null) {
      unsupported();
    }
    const normalizedResolution = normalizePnpmResolution(resolution, lockfileDirectory, request.snapshotRoot);
    resolutionKinds.set(dependencyPath, normalizedResolution.kind);
    facts.push({
      dependencyPath,
      dependencies: snapshotDependencyEdges(snapshot),
      kind: 'pnpm-lock-snapshot',
      name: exactString(identity.name),
      nonSemverVersion,
      registryName: optionalString(identity.registryName),
      resolution: normalizedResolution,
      version,
    });
  }
  return { facts, packages, resolutionKinds };
}

function importerReferences(importer, importerId, packages, resolutionKinds, authorities) {
  if (!isRecord(importer) || !isRecord(importer.specifiers)) {
    unsupported();
  }
  const selectedSections = ['dependencies', 'devDependencies', 'optionalDependencies'];
  const entries = [];
  const referencedSpecifiers = new Set();
  for (const section of selectedSections) {
    for (const [dependencyKey, resolvedReference] of orderedStringEntries(importer[section], { absent: true })) {
      if (!Object.hasOwn(importer.specifiers, dependencyKey)) {
        unsupported();
      }
      const rawSpecifier = exactString(importer.specifiers[dependencyKey]);
      referencedSpecifiers.add(dependencyKey);
      const workspaceSpec = officialCall(() => authorities.WorkspaceSpec.parse(rawSpecifier));
      const normalizedSpecifier =
        workspaceSpec === null ? rawSpecifier : officialCall(() => authorities.workspacePrefToNpm(rawSpecifier));
      const registrySpec = officialCall(() =>
        authorities.parseBareSpecifier(normalizedSpecifier, dependencyKey, DEFAULT_TAG, REGISTRY),
      );
      const snapshotKey = officialCall(() => authorities.refToRelative(resolvedReference, dependencyKey));
      if (workspaceSpec !== null && !isRecord(registrySpec)) {
        unsupported();
      }
      if (snapshotKey === null && workspaceSpec === null) {
        unsupported();
      }
      if (snapshotKey !== null && !Object.hasOwn(packages, snapshotKey)) {
        unsupported();
      }
      if (
        registrySpec === null &&
        snapshotKey !== null &&
        !new Set(['directory', 'file-tarball', 'git', 'hosted-git']).has(resolutionKinds.get(snapshotKey))
      ) {
        unsupported();
      }
      entries.push({
        dependencyKey,
        importerId,
        kind: 'pnpm-lock-importer-reference',
        rawSpecifier,
        registrySpec:
          registrySpec === null
            ? null
            : {
                fetchSpec: exactString(registrySpec.fetchSpec),
                name: exactString(registrySpec.name),
                type: exactString(registrySpec.type),
              },
        resolvedReference,
        section,
        snapshotKey,
        workspaceSelector: workspaceSpec === null ? null : exactString(workspaceSpec.version),
      });
    }
  }
  validateImporterSpecifierMembership(importer.specifiers, referencedSpecifiers);
  return entries;
}

function validateImporterSpecifierMembership(specifiers, referencedSpecifiers) {
  for (const [specifierKey] of orderedStringEntries(specifiers)) {
    if (!referencedSpecifiers.has(specifierKey)) {
      unsupported();
    }
  }
}

function pnpmImporters(lockfile, packages, resolutionKinds, authorities) {
  if (!isRecord(lockfile.importers)) {
    unsupported();
  }
  const facts = [];
  for (const importerId of Object.keys(lockfile.importers).sort(utf8Compare)) {
    facts.push(
      ...importerReferences(lockfile.importers[importerId], importerId, packages, resolutionKinds, authorities),
    );
  }
  return facts;
}

async function pnpmLockFacts(request) {
  const originalBytes = await preflightUtf8(request.candidatePath);
  const lockfileModule = await importPackage('@pnpm/lockfile.fs');
  const lockfileUtils = await importPackage('@pnpm/lockfile.utils');
  const depsPath = await importPackage('@pnpm/deps.path');
  const workspaceModule = await importPackage('@pnpm/workspace.spec-parser');
  const resolverModule = await importPackage('@pnpm/resolving.npm-resolver');
  const authorities = {
    WorkspaceSpec: workspaceModule.WorkspaceSpec,
    nameVerFromPkgSnapshot: lockfileUtils.nameVerFromPkgSnapshot,
    parseBareSpecifier: resolverModule.parseBareSpecifier,
    pkgSnapshotToResolution: lockfileUtils.pkgSnapshotToResolution,
    refToRelative: depsPath.refToRelative,
    registryContext: { registriesByScope: { default: REGISTRY } },
    workspacePrefToNpm: resolverModule.workspacePrefToNpm,
  };
  if (
    typeof lockfileModule.extractMainDocument !== 'function' ||
    typeof lockfileModule.readWantedLockfileWithMergeInfo !== 'function' ||
    typeof authorities.nameVerFromPkgSnapshot !== 'function' ||
    typeof authorities.pkgSnapshotToResolution !== 'function' ||
    typeof authorities.refToRelative !== 'function' ||
    typeof authorities.WorkspaceSpec?.parse !== 'function' ||
    typeof authorities.workspacePrefToNpm !== 'function' ||
    typeof authorities.parseBareSpecifier !== 'function'
  ) {
    unsupported();
  }

  let comparisonView = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(originalBytes);
  if (comparisonView.startsWith('\uFEFF')) {
    comparisonView = comparisonView.slice(1);
  }
  comparisonView = comparisonView.replaceAll('\r\n', '\n');
  if (officialCall(() => lockfileModule.extractMainDocument(comparisonView)) !== comparisonView) {
    unsupported();
  }

  const lockfileDirectory = path.dirname(request.candidatePath);
  const readResult = await officialCallAsync(() =>
    lockfileModule.readWantedLockfileWithMergeInfo(lockfileDirectory, {
      autofixMergeConflicts: true,
      ignoreIncompatible: false,
      mergeGitBranchLockfiles: false,
      useGitBranchLockfile: false,
      wantedVersions: ['9.0'],
    }),
  );
  if (!isRecord(readResult) || !isRecord(readResult.lockfile) || readResult.lockfile.lockfileVersion !== '9.0') {
    authorityRejected();
  }
  if (readResult.hadConflicts !== false || readResult.preMergeImporters !== undefined) {
    unsupported();
  }
  const { facts, packages, resolutionKinds } = pnpmSnapshots(readResult.lockfile, request, authorities);
  facts.push(...pnpmImporters(readResult.lockfile, packages, resolutionKinds, authorities));
  return facts;
}

function validateRequest(value) {
  if (!isRecord(value)) {
    throw new Error('invalid node authority request');
  }
  const expectedFields = ['candidatePath', 'graph', 'logicalPath', 'schema', 'snapshotRoot'];
  if (Object.keys(value).sort().join('\0') !== expectedFields.sort().join('\0') || value.schema !== REQUEST_SCHEMA) {
    throw new Error('invalid node authority request');
  }
  const graph = exactString(value.graph);
  if (!new Set(['npm-manifest-v1', 'pnpm-lock-v1', 'pnpm-workspace-v1']).has(graph)) {
    throw new Error('invalid node authority graph');
  }
  const snapshotRoot = path.resolve(exactString(value.snapshotRoot));
  const candidatePath = path.resolve(exactString(value.candidatePath));
  const relative = path.relative(snapshotRoot, candidatePath);
  if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    throw new Error('invalid node authority snapshot');
  }
  const logicalPath = exactString(value.logicalPath);
  const relativeLogicalPath = relative.split(path.sep).join('/');
  if (relativeLogicalPath !== logicalPath) {
    throw new Error('invalid node authority snapshot');
  }
  return {
    candidatePath,
    graph,
    logicalPath,
    snapshotRoot,
  };
}

async function runGraph(request) {
  if (request.graph === 'npm-manifest-v1') {
    return npmManifestFacts(request);
  }
  if (request.graph === 'pnpm-workspace-v1') {
    return pnpmWorkspaceFacts(request);
  }
  return pnpmLockFacts(request);
}

async function readStandardInput() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

async function main() {
  let request;
  try {
    request = validateRequest(JSON.parse(await readStandardInput()));
    const facts = await runGraph(request);
    process.stdout.write(
      JSON.stringify({
        facts,
        graph: request.graph,
        implementationIdentities: await implementationIdentities(),
        result: 'facts',
        schema: RESPONSE_SCHEMA,
      }),
    );
  } catch (error) {
    let diagnosticError = error;
    if (error instanceof GraphFailure && request !== undefined) {
      try {
        process.stdout.write(
          JSON.stringify({
            errorKind: error.kind,
            graph: request.graph,
            implementationIdentities: await implementationIdentities(),
            result: 'error',
            schema: RESPONSE_SCHEMA,
          }),
        );
        return;
      } catch (identityError) {
        diagnosticError = identityError;
      }
    }
    const diagnostic =
      process.env.WDV3_STATIC_REFERENCE_DEBUG === '1' && diagnosticError instanceof Error
        ? `${diagnosticError.stack ?? diagnosticError.message}\n`
        : 'static-reference node authority execution failed\n';
    process.stderr.write(diagnostic);
    process.exitCode = 1;
  }
}

if (process.argv[1] !== undefined && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  await main();
}

export { validateImporterSpecifierMembership };
