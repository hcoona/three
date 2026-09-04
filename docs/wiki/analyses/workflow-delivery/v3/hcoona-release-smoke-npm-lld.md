# `hcoona-release-smoke-npm` Live Buddy Low-Level Design

## 1. Status and Authorization Boundary

**Status:** replacement low-level design, dated 2026-08-31.

**Runtime state:** merged and disabled by protected Governance with `live_enabled: false`.

This document replaces the former implementation and rollout chronology. It defines the target first-slice design; current runtime code is useful only for repository naming and tooling conventions when it differs from the normative v3 design.

This document does **not** authorize changes to workflows, Python, schemas, tests, descriptors, policy or Governance files, Environments, access, packages, versions, tags, runs, or external state. It does not authorize a commit, activation, dispatch, publication, remediation, or deletion of the legacy publication Environment.

Implementation must be delivered and validated while `live_enabled` remains `false`. Obsolete Environment cleanup, fresh native evidence, Governance refresh, activation, and the first proving dispatch are later and separately controlled.

**Known activation blocker:** the pinned standard
`npm publish --tag ... --fetch-retries=0` Destination Operation Profile has not
yet passed the separately authorized native acceptance suite in section 18.
That suite must prove exact-version non-overwrite, the bounded
non-authoritative tag race, and safe rejection of deleted/restorable
same-version state. Live remains disabled until protected Governance binds a
fresh passing generation.

### 1.1 Normative precedence

The current v3 `requirements.md`, `high-level-design.md`, medium-level designs, `architecture-glossary.md`, and `migration-strategy.md` are normative. This LLD closes first-slice implementation detail without weakening them.

Unless changed here, preserve purpose-first routing; request-local same-revision Provider and Repository Model behavior; NBGV; Build Definition, Release Unit, qualification, Observation, Official, simulation, concurrency, and remediation contracts. Simulation retains its current run-attempt identity and rerun behavior.

## 2. Exact Slice

| Concern                                   | Exact value                                                                                                    |
| ----------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| Repository                                | `hcoona/three`                                                                                                 |
| Product root                              | `src/public/lib/hcoona-release-smoke-npm`                                                                      |
| Release Unit                              | `hcoona-release-smoke-npm`                                                                                     |
| Package                                   | `@hcoona/hcoona-release-smoke-npm`                                                                             |
| Channel and purpose                       | Buddy; `live-release`                                                                                          |
| Build Definition and output               | `node/npm-package-v1`; `npm-tarball`                                                                           |
| Destination                               | `npm/github-packages-hcoona-three-v1`                                                                          |
| Registry                                  | `https://npm.pkg.github.com`                                                                                   |
| Release policy                            | `eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml`                                               |
| Release Unit descriptor                   | `src/public/lib/hcoona-release-smoke-npm/workflow-delivery.release-unit.yml`                                   |
| Quality descriptor                        | `src/public/lib/hcoona-release-smoke-npm/workflow-delivery.quality.yml`                                        |
| Governance repository/ref/path            | `hcoona/three`; `refs/heads/main`; `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`        |
| Governance maximum age                    | 90 days                                                                                                        |
| Approval Environment                      | `workflow-delivery-v3-buddy-approval`                                                                          |
| Environment sentinel                      | `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`                                      |
| Sole accepted writer/publisher TCB member | `hcoona`                                                                                                       |
| Package credential principal              | repository `hcoona/three`                                                                                      |
| Artifact retention                        | 45 days                                                                                                        |
| Target-derived dist-tag                   | `buddy-sha-<40-lowercase-target-sha>`                                                                          |
| Target mutation profile                   | Pinned standard `npm publish --tag ... --fetch-retries=0`; action admission requires passing native acceptance |

The desired Buddy coordinate is the exact package plus the frozen native NBGV `npmPackageVersion`. The Release policy's Official projection, `npm/npmjs-public-v1`, remains isolated and is not a Live capability of this design.

## 3. Current State, Target State, and Risk

### 3.1 Current versus target

At this design date, protected Governance is disabled, was inspected at `2026-08-14T17:19:12Z`, and expires at `2026-11-12T17:19:12Z`. Both `workflow-delivery-v3-buddy-approval` and the legacy `workflow-delivery-v3-buddy-github-packages` Environment exist.

The merged runtime already uses protected Governance v1, one Approval
Environment, direct Publication Authorization, current-DAG finalization,
normal-Live records without run-attempt identity, at-most-one-action
publication, and the bounded static-reference policy. It remains disabled,
retains the superseded Receipt/ActionResult publication and Outcome contracts,
and rejects the unsupported destination mutation primitive.

The replacement target has:

- one authority-bearing Environment, `workflow-delivery-v3-buddy-approval`;
- no publisher Environment, history-derived authority, prior-Attempt reconstruction, or reviewer recovery;
- a complete Publication Authorization emitted by the approved job;
- mechanically zero or one Publication Action;
- one nullable scalar publication terminal Artifact Reference, resolving only
  to a mutation marker or Publication Result, and no Receipt;
- immutable exact package-version bytes/digests and embedded witness as the
  authoritative destination state, with the target-derived tag retained only
  as an authorized non-authoritative routing side effect;
- independent `github.run_attempt == 1` job guards and no normal-Live run-attempt record binding; and
- the bounded static-reference policy in section 6.

The legacy publication Environment remains untouched until replacement runtime references are absent and deletion receives separate authorization. Its continued existence grants no target authority.

No target code may retain a fallback to any superseded mechanism.

### 3.2 Accepted writer and repository-principal risk

Normal Buddy accepts an arbitrary same-repository selected ref. GitHub's resolved exact SHA is both workflow/control revision and Release target. Protected `main` supplies Governance only; it must not substitute control code for the selected revision. The selected-revision eligibility parser must require exact schema `workflow-delivery/v3/normal-live-governance-attestation-v2`; v1 is not an admission alias, and an incompatible ref fails before Release Execution lookup, Attempt creation, or any Environment job.

`hcoona` is the sole accepted writer and publisher TCB member and may self-approve with `prevent_self_review: false`. Approval is operator confirmation, not independent review. A malicious accepted writer is not constrained by protected Governance, CODEOWNERS, static-reference validation, Environment approval, exact action checks, or permission declarations.

The GitHub Packages `GITHUB_TOKEN` principal is repository `hcoona/three`, not the smoke package. Known reach includes the real `hexo-renderer-asciidoc` package and disposable packages. Coordinate checks, Environment, CODEOWNERS, concurrency, and workflow permissions do not narrow that token. This wider blast radius is explicitly accepted for `hcoona`; Official npmjs credentials remain separate.

Controls retained for outsiders and mistakes include exact same-revision
bindings, protected Governance, bounded static-reference validation,
credential-free build and qualification, read-only Observation without
publication capability, immutable reviewer context, package-write isolation,
create-only authoritative version publication, complete resource keys, a
durable pre-mutation marker, and exact readback.

## 4. Target Lifecycle

```text
manual request
  -> purpose/platform admission
  -> request-local Provider discovery and Repository Model
  -> Live Eligibility
  -> caller-held Release Execution concurrency
  -> qualification plan
  -> build and project test
  -> artifact qualification and Qualification Decision
  -> destination Observation
  -> Publication Snapshot
       0 actions -> fresh exact-satisfied finalization proof
       1 action  -> Approval Bundle -> Environment wait
                 -> Publication Authorization
                 -> publisher resource concurrency
                 -> mutation marker -> one admitted destination operation
                 -> readback -> Publication Result
  -> read-only best-effort Attempt Outcome
```

Purpose branches before Live Eligibility or Model adoption. A simulation, CI, or other-purpose record cannot be reinterpreted as `live-release`.

There is no custom Actions-history discovery phase. Native history may aid diagnostics but cannot select artifacts, recover an approver, admit a rerun, or reconstruct prior authority.

## 5. Repository and File Decomposition

| Path or area                                                    | Target responsibility                                                                            |
| --------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`        | Manual request, discovery/model compilation, Live Eligibility, and Release Execution concurrency |
| `.github/workflows/workflow-delivery-v3-live-attempt.yml`       | Reusable normal-Live Attempt, Approval, publication, and finalization                            |
| `.github/workflows/workflow-delivery-v3-official-simulate.yml`  | Existing Official simulation, unchanged by this design                                           |
| `.github/workflows/workflow-delivery-v3-ci.yml`                 | CI and root HK integration; no Live authority                                                    |
| Protected Governance path                                       | Fresh access, Environment, principal, and enablement attestation                                 |
| Slice release policy and descriptors                            | Existing exact Release Unit, quality, and projection authoring                                   |
| `hk.pkl`                                                        | Root `index`/`worktree` static-reference gate                                                    |
| `eng/scripts/workflow_delivery_v3_static_reference.py`          | Thin source-kind-aware entry point                                                               |
| `three_workflow_delivery_v3/release/static_reference_policy.py` | Canonical policy, source readers, selectors, findings, and Result validation                     |
| `three_workflow_delivery_v3/repository/`                        | Same-revision Provider and Repository Model logic                                                |
| `three_workflow_delivery_v3/records/`                           | Strict records, canonicalization, and transport admission                                        |
| `three_workflow_delivery_v3/release/eligibility.py`             | Current-request eligibility and Governance admission                                             |
| `three_workflow_delivery_v3/release/qualification.py`           | Qualification planning, Evidence Admission, and Decision                                         |
| `three_workflow_delivery_v3/release/live.py`                    | Snapshot, Approval Bundle, Authorization, Result, and outcome semantics                          |
| `three_workflow_delivery_v3/release/finalizer.py`               | Current-DAG-only read-only finalization                                                          |
| `three_workflow_delivery_v3/adapters/node.py`                   | Deterministic tarball build and Node qualification                                               |
| `three_workflow_delivery_v3/adapters/github_packages.py`        | Observation, primitive admission, one compound action, and readback                              |
| `three_workflow_delivery_v3/cli.py`                             | Strict workflow-facing commands                                                                  |
| `three_workflow_delivery_v3/tests/`                             | Semantic unit, adapter, contract, and workflow acceptance tests                                  |

Implementation retires the old consumer-policy module, JavaScript dataflow analyzer, and script after callers migrate. Dependencies used only by that analyzer are removed from manifests and locks during implementation. Chronology-named tests should be replaced by semantic contracts.

## 6. Bounded Static-Reference Contract

### 6.1 Identity and claim

The new identities are:

```text
policy schema: workflow-delivery/v3/bounded-static-reference-policy
result schema: workflow-delivery/v3/bounded-static-reference-result
policy ID: release/hcoona-release-smoke-npm-bounded-static-reference-v1
producer package: @hcoona/hcoona-release-smoke-npm
```

A clean Result means only that no prohibited reference was found in the supported bounded surface under the declared source kind and exact policy revision. Findings are prohibited references, not proven consumers. The policy does not prove absence of every runtime use and does not constrain token reach.

### 6.2 Exact source kinds

| Source kind  | Enumeration and bytes                                                                          | Permitted use                 |
| ------------ | ---------------------------------------------------------------------------------------------- | ----------------------------- |
| `git-target` | Explicit full commit; exact tree entries and blob objects; never index/worktree bytes          | Only Live-admissible evidence |
| `index`      | Git index stage-0 entries and indexed blob objects; never worktree bytes                       | Staged/pre-commit HK feedback |
| `worktree`   | Present tracked files plus `git ls-files --others --exclude-standard` eligible untracked files | Manual HK feedback            |

`git-target` verifies the object is a commit, binds its full SHA, and reads selected regular blobs with Git plumbing. Missing objects, candidate symlinks, candidate submodule entries, or unreadable blobs fail; submodules are not recursively scanned.

`index` rejects candidate unmerged stages, missing objects, unsupported modes, and unreadable entries. `worktree` excludes ignored and absent tracked paths and rejects candidate symlinks or unreadable files.

Index and worktree Results have no target SHA and never label their bytes as `HEAD`. Live Eligibility accepts only a same-run `git-target` Result bound to the exact selected target.

After the invocation schema admits one source kind and its required parameters,
failure to enumerate, read, or minimally materialize the declared exact source
is `source-acquisition-failed`.

### 6.3 Authority pipeline and isolated source snapshot

The static policy has three non-competing authority layers:

```text
Git Source Authority
  -> isolated exact-source snapshot
  -> Ecosystem Authority Graph
  -> normalized ecosystem facts
  -> repository policy projection
  -> canonical Result
```

Git owns path enumeration and exact bytes. An Ecosystem Authority Graph may
compose authoritative source artifacts, official libraries or CLIs, and
published ecosystem standards. Together they own the manifest, lock,
descriptor, locator, workspace, action, or language model. Repository policy
owns only the producer identity, producer root, applicable prohibited-form
comparison, allowances, and canonical Result.

An authority that accepts bytes or text receives one candidate directly. A
library or CLI that requires a filesystem receives a Session-owned temporary
snapshot containing only the exact files declared for that authority graph.
The snapshot:

- preserves repository-relative paths and exact file bytes from one source
  kind;
- never copies an undeclared companion, follows a repository symlink, or falls
  back to the real worktree;
- records source kind, target SHA when applicable, logical path, source object
  identity when available, byte length, SHA-256 digest, BOM presence, input
  mode, and authority-graph ID in its materialization manifest;
- runs with controlled arguments and environment, including no ambient
  `GIT_INDEX_FILE`, user package-manager configuration, registry credentials,
  or writable cache outside the Session root; and
- is removed by exact resolved path when that invocation ends.

Materialization does not make the filesystem authoritative. The manifest and
source-specific Git acquisition remain authoritative; the temporary tree is
only an input shape required by an official file-oriented library.

Raw-byte APIs receive the original bytes. String-only APIs receive one strict
UTF-8 decode with no replacement characters, newline conversion, Unicode
normalization, or locale-dependent decoding. A decoded UTF-8 BOM remains the
leading `U+FEFF`; only the selected syntax or model authority may accept or
reject it. XML APIs receive the original byte stream so the official XML reader
owns encoding and BOM handling. The declared input/decoding mode and
BOM-handling rule are policy-digest inputs. Observed BOM presence is
source-evidence metadata and does not change policy identity.

Before materializing a JSON or YAML candidate for a file-oriented authority,
the adapter performs only a fatal UTF-8 byte-validity preflight over
the exact source bytes. It neither parses syntax nor alters the bytes copied to
the snapshot. The selected package JSON and pnpm readers own their documented
leading-BOM and newline behavior. The bound pnpm helper/YAML-loader stacks
cumulatively accept zero, one, or two leading UTF-8 BOMs; that exact behavior,
rather than a local one-BOM ceiling, is a graph-manifest input. Malformed UTF-8
is `encoding-rejected`; other syntax and format outcomes after valid decoding
remain authority-owned.

No authority graph may fetch, install, restore, resolve a registry, load a
plugin or preset outside the snapshot, expand ambient environment variables,
evaluate GitHub expressions or MSBuild properties, execute candidate code, or
write repository or external state. A CLI node may write only its declared
Session-owned cache or temporary output, both removed by exact path.
Finally-equivalent cleanup runs after success, rejection, process failure,
timeout, or cancellation. Failure to remove a required exact Session-owned
root produces `cleanup-failed` and prevents an admissible clean/findings Result;
an earlier sanitized failure remains diagnostic.

### 6.4 Exact Ecosystem Authority Graph

This LLD is the sole normative owner of the first-slice bounded
static-reference Result schema, policy identity, authority manifest and graph,
source enumeration, snapshot/input contracts, normalized facts, failure
taxonomy, and semantic scenarios. CI design references these contracts and
owns only gate integration and CI-local transport.

The checked-in authority manifest binds these graph nodes. Source artifact
format generations are part of the graph. Node packages are direct dependencies
resolved by `pnpm-lock.yaml`, including lockfile integrity. .NET packages are
centrally pinned and resolved by the adapter project's `packages.lock.json`.
CLI and runtime nodes bind their exact backend and version provenance in
`mise.lock`, plus an artifact checksum when that backend records one.

| Graph node          | Authoritative artifacts, models, and exact implementation                                                                                                                                                                   | Public API or command and input mode                                                                                                                                                                                                                                  | Normalized model owned by the graph                                                                                                                                                 |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm-manifest-v1`   | `package.json`; `@npmcli/package-json@8.0.0`; `npm-package-arg@14.0.0`                                                                                                                                                      | fatal UTF-8 byte preflight; `PackageJson.load(snapshotDirectory)`; `npa.resolve(name, spec, where)`; isolated snapshot                                                                                                                                                | npm manifest, package identity, dependency result type, fetch spec, save spec, and local path                                                                                       |
| `pnpm-lock-v1`      | `pnpm-lock.yaml` lockfile version exactly `9.0`; `@pnpm/lockfile.fs@1100.2.5`; `@pnpm/lockfile.utils@1102.1.0`; `@pnpm/deps.path@1101.0.1`; `@pnpm/workspace.spec-parser@1100.0.1`; `@pnpm/resolving.npm-resolver@1104.1.0` | fatal UTF-8 byte preflight; exact public `extractMainDocument`, `readWantedLockfileWithMergeInfo`, `WorkspaceSpec.parse`, `workspacePrefToNpm`, `parseBareSpecifier`, `refToRelative`, `nameVerFromPkgSnapshot`, and `pkgSnapshotToResolution` bounded sequence below | pnpm importers, package snapshots and identities, snapshot dependency edges, registry/alias specs, named or ranged workspace specs, and typed lock-owned Git or `file:` resolutions |
| `pnpm-workspace-v1` | `pnpm-workspace.yaml`; `@pnpm/workspace.workspace-manifest-reader@1100.1.8`; `@pnpm/workspace.spec-parser@1100.0.1`; `@pnpm/resolving.npm-resolver@1104.1.0`; `npm-package-arg@14.0.0`                                      | fatal UTF-8 byte preflight; exact `readWorkspaceManifest`, `WorkspaceSpec.parse`, `workspacePrefToNpm`, `parseBareSpecifier`, and `npa.resolve` bounded sequence below; isolated snapshot                                                                             | workspace package patterns and catalog dependency specifications                                                                                                                    |
| `nuget-lock-v1`     | `packages.lock.json` model version exactly `1`, `2`, or `3`; `packages.config` XML; NuGet lock/config models; `NuGet.ProjectModel@7.9.0`; `NuGet.Packaging@7.9.0`; exact sidecar `packages.lock.json` dependency closure    | for `packages.lock.json`, fatal UTF-8 byte preflight followed by `PackagesLockFileFormat.Read(Stream, NullLogger.Instance, repositoryLogicalPath)`; for `packages.config`, `new PackagesConfigReader(Stream, false).GetPackages(false)`                               | NuGet package identities, dependency groups, dependency edges, requested ranges, resolved versions, and package entries                                                             |

File-oriented graph nodes receive exactly this source snapshot closure:

| Graph node          | Exact source inputs preserving repository-relative paths |
| ------------------- | -------------------------------------------------------- |
| `npm-manifest-v1`   | the selected `package.json` only                         |
| `pnpm-lock-v1`      | the selected `pnpm-lock.yaml` only                       |
| `pnpm-workspace-v1` | the selected `pnpm-workspace.yaml` only                  |

The NuGet graph consumes the exact candidate byte stream.
`npm-manifest-v1` passes the selected manifest directory as `where`;
`pnpm-workspace-v1` does the same for catalog references. Each Node invocation
receives controlled Session-owned `HOME`, `USERPROFILE`, `HOMEDRIVE`, and
`HOMEPATH` values as applicable. Tilde references and normalized paths escaping
the snapshot repository root are `unsupported-projection`.

The npm manifest graph uses this exact projection:

1. Call `PackageJson.load(snapshotDirectory)` once and use only the returned
   instance's `.content`. Do not call `normalize`, `prepare`, or `fix`.
2. Require `.content` to be a non-null, non-array object. Inspect only own
   top-level `name`, `dependencies`, `devDependencies`,
   `optionalDependencies`, and `peerDependencies`; ignore all other fields.
3. An absent `name` emits no package-name-role fact. A present `name` must be a
   string and must satisfy `npa.resolve(name, "*", snapshotDirectory)` with the
   same returned name before emitting that role.
4. Process the four dependency sections in the fixed order above. A present
   section must be a non-null, non-array object and every own value must be a
   string. Process dependency keys in ascending unnormalized UTF-8 byte order;
   the same key in different sections emits distinct facts.
5. For each entry call
   `npa.resolve(dependencyKey, sourceSpec, snapshotDirectory)`. Require a name
   and documented result type; emit the section, key, source spec, name, type,
   raw/save/fetch specs, file/directory local path when present, and one-level
   alias `subSpec` identity/spec fields. Do not serialize hosted-provider or
   other unused model state.

Wrong selected-field shapes or missing required successful-result fields are
`unsupported-projection`; `PackageJson.load` or `npa.resolve` rejection is
`authority-rejected`. Parse and validate the complete candidate before
emitting any facts. The graph does not independently parse JSON or npm
specifier syntax.

The pnpm workspace graph uses this exact projection:

1. Call `readWorkspaceManifest(snapshotDirectory)` once with no filename
   override. `undefined` or `null` emits no facts.
2. If `manifest.packages` is an array, emit each exact string with its array
   index. Otherwise emit no package-pattern facts. Never normalize/expand the
   patterns or discover members.
3. Traverse direct `manifest.catalog` entries as the distinct default catalog,
   then each direct catalog under `manifest.catalogs`; do not merge the default
   catalog with a named catalog called `default`, recurse, or deduplicate.
   Process catalog names and dependency keys in ascending unnormalized UTF-8
   byte order.
4. For each direct string specifier, make these calls:

    ```text
    workspaceSpec = WorkspaceSpec.parse(rawSpecifier)
    if workspaceSpec != null:
      normalizedSpecifier = workspacePrefToNpm(rawSpecifier)
      registrySpec = parseBareSpecifier(
        normalizedSpecifier,
        dependencyKey,
        "latest",
        "https://registry.npmjs.org/"
      )
    else:
      npmResult = npa.resolve(
        dependencyKey,
        rawSpecifier,
        snapshotDirectory
      )
    ```

    A workspace result must yield a registry spec; emit its target identity plus
    the exact workspace selector. A non-workspace result emits the same bounded
    npm-result fields used by the npm manifest graph.

Unsupported workspace path forms, tilde/escaping local paths, or missing
required successful-result fields are `unsupported-projection`; reader or
parser rejection is `authority-rejected`. Parse and validate the complete
candidate before emitting facts. No member discovery, realpath, stat, registry,
Git, tarball, or installer call is allowed.

An authority graph may compose nodes for different semantic layers.
`pnpm-lock-v1` composes the conflict-aware snapshot reader with public pure
dependency-path, lockfile-resolution, workspace-specifier, and registry
specifier helpers. It never invokes a resolver that reads a local directory,
tarball, registry, or Git remote. The implementation must not run two competing
authorities over the same semantic layer, cross-validate an official model with
a local grammar, or reject syntax solely because a second implementation
disagrees.

Before the pnpm lock reader runs, the adapter uses the reader package's public
document selector over a comparison view produced by the reader's documented
removal of at most one leading BOM and CRLF-to-LF normalization. If
`extractMainDocument` does not return that complete view unchanged, the combined
environment/main or environment-only lock is `unsupported-projection`. The
exact original bytes remain unchanged in the snapshot. This admission check
owns only document selection; it does not parse YAML or interpret
environment-lock contents.

The NuGet graph uses these exact projections:

1. For `packages.lock.json`, perform only the fatal UTF-8 byte-validity
   preflight, then pass the unchanged original bytes through a non-writable
   `MemoryStream` to:

    ```text
    PackagesLockFileFormat.Read(
      stream,
      NullLogger.Instance,
      repositoryLogicalPath
    )
    ```

    `repositoryLogicalPath` is the normalized repository-relative candidate
    path rather than a materialization path. Require `model.Version` to be
    exactly `1`, `2`, or `3`; `int.MinValue` or any other value is
    `authority-rejected`. The adapter does not independently parse JSON,
    inspect raw version spelling, or cross-check the NuGet model.

2. Require `model.Targets` and selected child collections to be non-null.
   Process targets by `PackagesLockFileTarget.Name` with
   `StringComparer.Ordinal`. Within each target, process
   `LockFileDependency` entries by ID using `OrdinalIgnoreCase` with `Ordinal`
   as the total-order tie-breaker. Emit target name, dependency ID,
   `PackageDependencyType.ToString()`, optional requested range through
   `ToNormalizedString()`, and optional resolved version through
   `ToNormalizedString()`.
3. Process each lock dependency's direct `PackageDependency` edges by ID using
   the same case-insensitive-plus-ordinal order. Emit parent ID, edge ID, and
   optional normalized `VersionRange`. Do not inspect `JObject`, content hash,
   or other unselected model state.
4. For `packages.config`, pass the original bytes through a non-writable
   `MemoryStream` and make these exact calls:

    ```text
    reader = new PackagesConfigReader(stream, leaveStreamOpen: false)
    packages = reader.GetPackages(allowDuplicatePackageIds: false)
    ```

    Materialize the complete result and order it with
    `packages.OrderBy(p => p.PackageIdentity, PackageIdentity.Comparer)`. Emit
    only reader-returned package ID spelling and
    `PackageIdentity.Version.ToNormalizedString()`. Do not preparse XML or
    inspect target-framework or installation metadata.

NuGet reader or `GetPackages` rejection is `authority-rejected`; an admitted
model missing a required selected field is `unsupported-projection`. Parse and
validate the complete candidate before emitting facts. Original bytes and
their digest remain unchanged.

The pnpm lock graph then uses this exact bounded sequence:

1. `lockfileDir` is the directory containing the materialized exact
   `pnpm-lock.yaml`.
2. After the document-admission check above, call
   `readWantedLockfileWithMergeInfo(lockfileDir, options)` with
   `wantedVersions: ["9.0"]`, `ignoreIncompatible: false`,
   `useGitBranchLockfile: false`, `mergeGitBranchLockfiles: false`, and
   `autofixMergeConflicts: true`. Require a non-null lockfile,
   `hadConflicts: false`, absent `preMergeImporters`, and exact returned
   `lockfileVersion: "9.0"`.
3. Bind `defaultTag` to `latest`, `registry` to
   `https://registry.npmjs.org/`, and `registryContext.registriesByScope.default`
   to that same registry. No ambient npm or pnpm configuration supplies these
   values.
4. Process distinct package snapshots in ascending unnormalized UTF-8
   dependency-path order. For each snapshot, call
   `nameVerFromPkgSnapshot(dependencyPath, snapshot)` and
   `pkgSnapshotToResolution(dependencyPath, snapshot, registryContext)`. Consume
   only the returned identity and typed lock-owned Git or `file:` resolution;
   neither call may trigger a resolver. Then inspect only the loaded
   snapshot's own `dependencies` and `optionalDependencies`, in that fixed
   section order. Absence is empty. A present section must be a non-null,
   non-array object whose own values are strings. Process keys in ascending
   unnormalized UTF-8 byte order and emit the owning dependency path, section,
   dependency key, exact resolved reference, and canonical logical location.
   Do not resolve or recursively walk these edges.
5. Process the loaded `lockfile.importers` own entries in ascending
   unnormalized UTF-8 importer-ID byte order. Each importer must be a non-null,
   non-array `ProjectSnapshot`. Inspect only its `specifiers`, `dependencies`,
   `devDependencies`, and `optionalDependencies`. `specifiers` must be a
   non-null, non-array object whose own values are strings. Process the three
   dependency sections in the fixed order above; absence is empty, while a
   present section must be a non-null, non-array object whose own values are
   strings. Process each section's keys in ascending unnormalized UTF-8 byte
   order without merging the same key across sections. Every section key must
   have an own string-valued `specifiers` entry, and every own `specifiers` key
   must occur in at least one selected section. A violation is
   `unsupported-projection`.
6. For each ordered importer-section entry, bind `dependencyName` to the
   section key, `rawSpecifier` to the exact
   `ProjectSnapshot.specifiers[dependencyName]` value, and
   `resolvedReference` to the exact section-map value. Then make these calls:

    ```text
    workspaceSpec = WorkspaceSpec.parse(rawSpecifier)
    normalizedSpecifier =
      workspaceSpec == null
        ? rawSpecifier
        : workspacePrefToNpm(rawSpecifier)
    registrySpec = parseBareSpecifier(
      normalizedSpecifier,
      dependencyName,
      defaultTag,
      registry
    )
    snapshotKey = refToRelative(resolvedReference, dependencyName)
    ```

    A workspace input must yield a registry spec; stage its `W` fact from that
    official identity and selector. When its snapshot key is null, that `W` fact
    is complete and no snapshot lookup occurs. A null snapshot key for a
    non-workspace input, a required missing non-null snapshot, or a workspace
    path form is `unsupported-projection`. A null registry spec is otherwise
    admissible only when the matched snapshot emits an admitted typed Git or
    `file:` resolution. Normalize returned local paths relative to
    `lockfileDir`.

The graph makes exactly one explicit `WorkspaceSpec.parse` call per ordered
importer-section entry; `workspacePrefToNpm` owns its internal reparse. It does
not call `packageIdFromSnapshot`, `deps.path.parse`, any non-public subpath, or
any filesystem, registry, Git, tarball, or package resolver after the declared
lock read.

Changing an authoritative source schema, standard, package, CLI, runtime,
public API or command, input mode, admitted format generation, or normalized
fact contract changes the policy digest and requires semantic acceptance.
Version discovery at runtime is not authority: the adapter must report the
exact loaded implementation identity, and admission compares it with this
manifest.

### 6.5 Selector-to-fact and prohibited-form matrix

Selectors are disjoint. A path is in the first-slice claim only when exactly one
row selects it:

| Family                  | Disjoint selector and admitted format                                                                         | Ordered authority graph | Required emitted facts                                                                                                                                                                                              | Applicable prohibited forms   | Explicitly unsupported in this row                                                                                                                                                                                                            |
| ----------------------- | ------------------------------------------------------------------------------------------------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| npm manifest            | basename `package.json`; npm manifest format accepted by the authority graph                                  | `npm-manifest-v1`       | package-name role; dependency key and normalized reference for dependencies, dev, optional, and peer sections                                                                                                       | `dependency-key`, `D/V/A/L`   | `workspace:` values, scripts, arbitrary custom fields, and dynamically constructed values                                                                                                                                                     |
| NuGet packages config   | exact basename `packages.config`                                                                              | `nuget-lock-v1`         | normalized package ID and requested version                                                                                                                                                                         | `D/V`                         | package-manager behavior not represented by `PackagesConfigReader`                                                                                                                                                                            |
| pnpm lock               | exact basename `pnpm-lock.yaml`, excluding descendants of `.github/workflows`; lockfile version exactly `9.0` | `pnpm-lock-v1`          | normalized importer dependencies, snapshots, package identities, snapshot dependency/optional-dependency edges, registry/alias specs, named/ranged workspace specs, and typed lock-owned Git or `file:` resolutions | `dependency-key`, `D/V/A/W/L` | conflicted/branch-merged/incompatible generations; non-workspace `link:`, bare/path-local and path-form workspace references; unexplained null snapshot keys; missing required snapshots; registry/Git resolution; runtime installation state |
| pnpm workspace manifest | exact basename `pnpm-workspace.yaml`, excluding descendants of `.github/workflows`                            | `pnpm-workspace-v1`     | workspace package patterns and normalized catalog dependency keys/references                                                                                                                                        | `dependency-key`, `D/V/A/W/L` | resolving package patterns into member paths, executable hooks, and files outside the isolated snapshot                                                                                                                                       |
| NuGet lock              | exact basename `packages.lock.json`; NuGet model `Version` exactly `1`, `2`, or `3`                           | `nuget-lock-v1`         | normalized package ID, dependency group and type, dependency edge, and requested range or resolved version when supplied by the model                                                                               | `dependency-key`, `D/V`       | reader rejection or model version outside `1`-`3`; assets files; restore; target-framework selection beyond emitted groups                                                                                                                    |

The prohibited-form codes are:

- `D`: direct `@hcoona/hcoona-release-smoke-npm`;
- `V`: the producer identity with a version, range, tag, or other selector;
- `A`: an alias whose normalized target is the producer;
- `W`: a workspace reference whose normalized identity is the producer;
- `L`: a normalized local dependency path resolving to the producer root;
- `dependency-key`: a dependency key equal to the producer package regardless
  of value.

The authority graph, not policy code, determines npm, pnpm, or NuGet syntax and
normalization. Policy code compares emitted identities and paths.
Repository-relative path comparison uses POSIX semantics, resolves `.` and
`..`, rejects escape above repository root, and compares with
`src/public/lib/hcoona-release-smoke-npm`.

Documentation, ordinary application source, standalone `pyproject.toml`,
`setup.py`, `requirements*.txt`, `uv.lock`, `poetry.lock`, npm and Yarn
lockfiles, `Directory.Packages.props`, `.csproj`, `.vbproj`, `.fsproj`, Bun
files, `.npmrc`, `.yarnrc.yml`, Renovate, Dependabot, `.pnpmfile.cjs`, batch/Zsh
files, shell and PowerShell scripts, JavaScript, TypeScript, and Python
automation, GitHub workflow and composite-action files, Node import subpaths,
encoded identities, split/constructed strings, arbitrary runtime downloads,
external files, and novel layouts are outside this policy revision. Unsupported
surfaces are omitted rather than backed by a local compatibility grammar or
retained behind an authority that can inspect undeclared filesystem state.

### 6.6 Allowances, failures, Result, and integration

Only these allowances exist:

1. the top-level `name` value in the exact producer `package.json`; and
2. legitimate build/workspace references to the producer root outside
   dependency positions.

The second allowance covers a workspace member or lockfile importer; it does
not permit an admitted direct, versioned, or aliased dependency value, package
token, or module load. Runtime-relative local install arguments on standalone
script surfaces have no source-owned execution base and are outside this
revision's `L` projection. Fixtures create prohibited examples outside
candidate paths or construct them in test code. No repository file receives a
whole-file exception.

Every selected file must complete its exact authority graph. Failure to decode
the declared input is `encoding-rejected`; rejection by an official artifact
schema, library, CLI, or standard model is `authority-rejected`; inability to
start or complete an executable authority node is
`authority-execution-failed`; successful authority processing that cannot emit
a required fact is `unsupported-projection`; a loaded implementation, API,
command, or schema identity that differs from the authority manifest is
`authority-mismatch`; failure to remove required Session-owned materialization
or scratch roots is `cleanup-failed`. Together with
`source-acquisition-failed`, these are distinct fail-closed errors. An
explicitly unsupported selector or field is outside the bounded claim; it must
not be silently promoted into a supported fact by a fallback authority.

Each finding contains normalized path, family, semantic context,
prohibited-form kind, stable location when the authority supplies one, and a
sanitized matched identity. `result` is `clean`, `findings`, or `error`. Every
Result contains the sorted exact implementation identities actually loaded.
`error-kind` is required exactly when `result` is `error` and is one of
`source-acquisition-failed`, `encoding-rejected`, `authority-rejected`,
`authority-execution-failed`, `unsupported-projection`,
`authority-mismatch`, or `cleanup-failed`; it is forbidden for `clean` and
`findings`. If cleanup fails after an earlier failure, `cleanup-failed` is
authoritative and the earlier sanitized cause is diagnostic. The policy
document binds the expected authority manifest and graph; a Result binds the
resulting policy digest and observed implementation identities rather than
reproducing foreign authority models.

The invocation schema first admits exactly one source kind and its required
parameters. An omitted or unknown source kind or malformed required parameter
terminates with a nonzero exit and sanitized diagnostic before Result
construction and before allocating an authority root. After admission, source
enumeration validates and orders candidates by ascending normalized POSIX-path
UTF-8 bytes. The first inability to enumerate, read, or minimally materialize
the declared exact source returns an error Result with
`source-acquisition-failed` and terminates before graph execution. Within a
candidate, graph nodes run in declared order, arrays run by index, and selected
mappings run in their explicitly bound section and UTF-8-key order. The first
typed non-cleanup graph failure terminates further projection and becomes the
Result's `error-kind`. Finally-equivalent cleanup still runs, and
`cleanup-failed` overrides an earlier source or graph failure. This traversal
and failure-selection rule is a policy-digest input.

Candidate counts, per-file digests, aggregate inventory digests, snapshot
paths, and timing are diagnostics only. Live authority is exact target, exact
policy ID/digest, exact authority identities, successful projections, and an
empty finding set.

Root HK runs the lightweight policy whenever HK runs; the step is not skipped
because the caller-selected file list lacks a candidate. The caller explicitly
selects `index` for staged/pre-commit operation or `worktree` for manual
filesystem checking. An omitted or unknown mode is the pre-Result invocation
failure defined above; a recognized mode whose source cannot be acquired
returns `source-acquisition-failed`. HK output is feedback, never Live Evidence.

Live Eligibility reruns `git-target` itself against the exact selected commit.
It does not adopt HK, CI, caller-provided, index, or worktree output.

## 7. Canonical Records and Artifact Binding

### 7.1 Representation

Authoritative records use strict UTF-8 JSON, duplicate/unknown-field rejection, RFC 8785 JCS, normalized POSIX paths, full lowercase SHAs, `sha256:<64-hex>`, `sha512:<128-hex>`, exact schemas, and sorted duplicate-free semantic sets. Python records remain frozen and slotted.

If present, `record-digest` is computed over the canonical document before adding that field. Consumers reconstruct and verify it.

Normal-Live producer bindings include repository, workflow path, logical job, selected control SHA, target, purpose, `workflow_run_id`, payload identity, and payload digest. They omit `github.run_attempt`; target parsers reject a normal-Live `run-attempt` field.

### 7.2 Execution and Attempt identity

Buddy Execution identity is canonical channel + Release Unit + target. Normal-Live Attempt identity is Execution identity + `workflow_run_id`. An admitted, non-coalesced new dispatch therefore creates a new Attempt even for the same target; the platform run attempt remains only a guard and diagnostic.

### 7.3 Record set

| Record                                       | Binding responsibility                                                                                                       |
| -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| Release Intent                               | Manual request, selected ref, repository, channel, Release Unit, purpose, actor, target                                      |
| Repository Model Snapshot                    | Selected revision, Providers, Project Nodes, Release Unit, descriptors, catalogs/control, NBGV                               |
| Buddy Execution / Release Attempt identities | Deterministic execution tuple; execution plus workflow run                                                                   |
| Bounded Static-Reference Result              | Policy, source kind, target for `git-target`, loaded authority identities, result, error kind when applicable, findings      |
| Live Eligibility Decision                    | Intent, Model, workflow run, policy Result, protected Governance, eligibility-main lineage                                   |
| Qualification Snapshot                       | Target, build/output, version, toolchain, obligations, desired projection basis                                              |
| Release Artifact                             | Tarball transport, SHA-256/SHA-512, manifest, witness, build identity                                                        |
| Qualification Evidence / Decision            | Obligation results and complete admission                                                                                    |
| Remote-State Observation                     | Active registry projection, package-control facts, exact version readback, and tag diagnostics; no future Snapshot reference |
| Publication Snapshot                         | Qualification, Observations, desired state, and `actions` of length zero or one                                              |
| Exact-Satisfied Finalization Proof           | Zero-action Snapshot plus fresh Governance, package-control, and authoritative exact-version readback                        |
| Approval Bundle                              | Direct references to the action-bearing Snapshot and immutable reviewer summary                                              |
| Publication Authorization                    | Direct Approval Bundle reference plus approval-boundary and fresh-Governance evidence                                        |
| Package-Control Proof                        | Destination/package subject, supported endpoints, and normalized boundary-local package-control readback                     |
| Mutation marker                              | Authorization plus final Governance, package-control, and effective-profile-match evidence                                   |
| Publication Result                           | Marker plus command classification and newly observed post-action facts                                                      |
| Attempt Outcome                              | Disposition, `possibly_mutated`, and one tagged direct predecessor                                                           |

The only publication-result schema is
`workflow-delivery/v3/publication-result`; the former
`workflow-delivery/v3/action-result` and all Receipt schemas have no alias.
The exact-satisfied proof schema is
`workflow-delivery/v3/exact-satisfied-finalization-proof`. The marker retains
`workflow-delivery/v3/github-packages-mutation-may-have-started`.

### 7.4 Schema-specific payload ownership

Every standalone record includes the shared normal-Live producer/current-run
envelope and canonical payload digest defined in section 7.1. The table lists
only fields newly owned by that payload; a field named `*-reference` is one
strict Shared Foundation Artifact Reference plus the referenced canonical
payload digest.

| Payload                            | Required schema-specific fields                                                                                                                                                                                                                                                        |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Destination Operation Profile      | `profile-id`, `registry`, `access-mode`, `node-version`, `npm-version`, normalized `command-template` containing every fixed CLI option, typed `operand-slots`, configuration-precedence rules, request-generation rules, and mutation-retry prohibition                               |
| Publication Action                 | `action-id`, `destination-operation-profile-digest`, exact `package`, exact `version`, `tarball-reference`, explicit target-derived `tag`, complete `mutable-resource-keys`, and `serialization-projection`                                                                            |
| Remote-State Observation           | strict `qualification-decision-reference`, desired package/version/artifact/witness basis, `classification`, supported `package-control`, active-version readback or active absence, target-tag readback when available, selected response identity, and bounded diagnostics           |
| Approval Bundle                    | `publication-snapshot-reference` and `reviewer-summary-reference`                                                                                                                                                                                                                      |
| Publication Authorization          | `approval-bundle-reference`, exact `approval-boundary`, fresh protected `governance-proof`, and `completed-at`                                                                                                                                                                         |
| Package-Control Proof              | exact destination/normalized-package `subject`, `observed-at`, supported authoritative `endpoints`, normalized observed owner/repository-association/visibility/exposed-access `facts`, and canonical `response-digests`; the parent supplies applicable Governance and owns admission |
| Mutation marker                    | `publication-authorization-reference`, final `governance-proof`, final `package-control-proof`, and canonical `profile-match` evidence                                                                                                                                                 |
| Exact-Satisfied Finalization Proof | `publication-snapshot-reference`, fresh `governance-proof`, fresh `package-control-proof`, fresh authoritative `exact-version-readback`, and `proved-at`                                                                                                                               |
| Publication Result                 | `mutation-marker-reference`, `command-classification`, nullable `post-action-readback`, `result`, `mutation-classification`, nullable sanitized `response-identity`, and bounded `diagnostics`                                                                                         |
| Attempt Outcome                    | `disposition`, `possibly-mutated`, and one tagged `direct-predecessor` reference                                                                                                                                                                                                       |

Closed values are:

- Observation `classification`: `absent`, `exact-satisfied`, `partial`,
  `conflicting`, `unknown`, or `unprovable`; `absent` always means absent from
  the active registry projection;
- Result `command-classification`: `not-initiated`, `definitive-success`,
  `definitive-non-success`, or `ambiguous`;
- Result `result`: `published` or `failed`;
- Result `mutation-classification`: `not-mutated`, `possibly-mutated`, or
  `mutated`;
- Outcome `disposition`: `exact-satisfied`, `published`,
  `failed-before-publication`, `publication-failed`, or `unknown`; and
- Outcome predecessor tag: `publication-result`, `mutation-marker`,
  `exact-satisfied-finalization-proof`, `zero-action-publication-snapshot`,
  `publication-authorization`, `approval-bundle`,
  `action-bearing-publication-snapshot`, `blocking-observation`, or
  `qualification-decision`.

`not-initiated` requires direct local proof that the registry request did not
start and permits only `failed`/`not-mutated`. `published` requires
`definitive-success`/`mutated` plus successful authoritative exact-version
readback. `definitive-non-success` and `ambiguous` always remain `failed`;
`ambiguous` is at least `possibly-mutated`.

No record accepts deprecated aliases, copied ancestor fields, `terminal_phase`,
`next_action`, parallel lineage digests, or a Result-reference collection.
Observation admission resolves and validates the Qualification Decision,
Qualification Snapshot, Live Eligibility Decision, and protected-Governance
identity chain before interpreting its embedded Package-Control Proof; it does
not copy Governance into either value.

### 7.5 Artifact transport

Every authoritative upload uses overwrite disabled and `retention-days: 45`, captures artifact ID/service digest/URL, and records a canonical payload digest. Names omit run attempt and are non-authoritative indexes, for example:

```text
wdv3-live-<role>-<workflow-run-id>-<payload-digest-prefix>
```

Consumers download only current-run artifacts by immutable ID and validate service digest, payload path, schema, producer, run, target, purpose, payload identity, and canonical digest. Name fallback, latest selection, and history lookup are forbidden.

Downstream records bind the producer-returned transport tuple:

```text
artifact-id
artifact-digest
artifact-url
payload-path
payload-digest
```

A payload does not bind its own post-upload artifact ID. Job outputs carry only small transport facts and never replace the durable artifact.

## 8. Request, Model, and Live Eligibility

### 8.1 Request admission

The Buddy caller admits only repository `hcoona/three`, event `workflow_dispatch`, an exact same-repository selected ref, full resolved target SHA, selected workflow/control SHA equal to target, Release Unit `hcoona-release-smoke-npm`, Buddy channel, `live-release`, and platform run attempt one.

There is no independent target-SHA input. GitHub's selected-ref resolution supplies both control and target. The Release Intent is produced before any Model is adopted.

### 8.2 Request-local Model

Provider discovery and Repository Model compilation run at the selected revision and freeze Product root, descriptors, Release Unit, quality selection, release policy, Buddy projection, catalog/descriptor/control digests, and NBGV facts.

Canonical NBGV facts are `version`, `semVer1`, `semVer2`, `versionHeight`, `gitCommitId`, and `publicRelease`; native facts contain `npmPackageVersion`. `gitCommitId` must bind the target. No later phase recomputes or substitutes version facts.

A protected-main Model never replaces the request-local Model. Dispatching an older ref uses that ref's original control stack.

### 8.3 Eligibility

Live Eligibility validates exact platform, purpose, Intent, workflow-run, and Model bindings; slice package, Release Unit, Build Definition, quality, destination, and projection; a clean exact-target static Result; protected Governance; sole accepted writer; repository-principal risk; native Environment attestation; `live_enabled: true`; and a valid at-most-90-day interval. It runs before Release Execution concurrency and Attempt creation, so it cannot bind an Attempt.

It records the protected-main SHA resolved during eligibility as `eligibility-main-sha`, plus Governance repository/ref/path, Git object format, exact blob OID, canonical content digest, and admitted semantic fields. The lineage SHA supports later history proof; unrelated bytes in that main commit are not Governance identity.

Eligibility has no `actions: read` and performs no run-history search.

## 9. Governance and Path-History Proof

### 9.1 Target protected attestation

The refreshed strict Governance file retains package/policy identity, issuer `hcoona`, accepted writer set `{hcoona}`, access inventory, repository `hcoona/three` as package principal, intended coordinate and known wider reach, inspection/expiry, maximum 90-day validity, `live_enabled`, and limitations on complete package enumeration and revocation latency.

Its exact schema is
`workflow-delivery/v3/normal-live-governance-attestation-v2`, replacing the
currently merged but disabled
`workflow-delivery/v3/normal-live-governance-attestation-v1`. The new parser
accepts only v2. This is an intentional compatibility fence so superseded
selected-ref control fails before any Environment or publisher job.

The v2 `activation` object is exactly one of:

| State     | Exact closed fields                                                                         | Live-flag relation                                                                     |
| --------- | ------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `blocked` | `state: "blocked"`                                                                          | Requires `live_enabled: false`; carries no native evidence                             |
| `ready`   | `state: "ready"`, `approval_environment`, `artifact_retention`, and `destination_primitive` | Required by `live_enabled: true`; may remain with `live_enabled: false` for revocation |

Unknown states or fields, native evidence in `blocked`, `live_enabled: true`
with `blocked`, or incomplete/non-passing `ready` evidence fails strict parsing
or admission. The implementation migration uses blocked v2. The Activation PR
atomically installs complete `ready` evidence and sets `live_enabled: true`.

The ready variant's `DestinationPrimitiveAttestation` binds only:

- the canonical Destination Operation Profile digest;
- native-acceptance-suite version;
- approved disposable-package preconditions;
- GitHub API version and cited lower-layer contract revision;
- capture time; and
- the canonical evidence digest identifying the exact successful acceptance
  generation.

`activation.destination_primitive` in the ready variant is a closed object with
these exact fields:

| Field                                  | Type and constraint                                                                                                                      |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `destination_operation_profile_digest` | Lowercase `sha256:<64-hex>` digest of the canonical profile                                                                              |
| `native_acceptance_suite_version`      | Non-empty exact suite revision admitted by target code                                                                                   |
| `disposable_package_preconditions`     | Closed object containing exact `package`, `preexisting_container: true`, `operator_controlled: true`, and `production_dependency: false` |
| `github_api_version`                   | Exact GitHub API version used by the suite                                                                                               |
| `lower_layer_contract_revision`        | Exact revision of the cited npm/GitHub contract set                                                                                      |
| `captured_at`                          | UTC instant used for the 90-day action-bearing freshness check                                                                           |
| `evidence_digest`                      | Lowercase `sha256:<64-hex>` digest of the retained canonical acceptance evidence                                                         |

Unknown or missing fields, any false disposable-package precondition, a profile
digest not resolved exactly by target code, an unadmitted suite or contract
revision, or evidence older than 90 days blocks action-bearing admission.
Inclusion in the complete `ready` variant is the issuer's successful-acceptance
attestation. Governance does not carry scenario names, individual
race/tombstone results, deleted-version facts, or raw endpoint material.

The detailed acceptance inputs, active/deleted inventories, tombstone identity,
responses, semantic deltas, and restoration facts remain in the separately
authorized acceptance evidence. They and the acceptance-only package-admin
credentials never enter runtime Governance, Observation, Approval, or
publication.

Governance also retains normalized authenticated native readback/attestation
for `workflow-delivery-v3-buddy-approval`:

- required reviewer set exactly `{hcoona}`;
- `prevent_self_review: false`;
- administrator/bypass disabled;
- zero wait;
- no deployment branch/tag restriction;
- zero Environment secrets;
- exactly the Environment-scoped sentinel and value from section 2;
- no same-name repository variable;
- no same-name organization variable in the applicable owner scope;
- authenticated
  `GET /repos/hcoona/three/actions/permissions/artifact-and-log-retention`
  readback whose integer `days` value is at least 45; and
- authenticated endpoint identities, capture time, normalized returned facts, and canonical response digests.

If a native API does not expose a setting, Governance records an explicit issuer observation rather than an invented API result. Runtime `vars` lookup cannot prove source scope or broader-variable absence. The current attestation must be refreshed under this contract before activation even if its dates have not expired.

### 9.2 Eligibility read

Eligibility uses an isolated Git object database, fetches complete required history for protected `refs/heads/main`, resolves the exact path to one regular blob, reads it through Git, strictly parses it, and records exact blob/content identity. Shallow state, missing objects, unsupported modes, or ambiguous ref/path resolution fail.

### 9.3 Approval and publisher continuity

Approval and publisher independently use fresh isolated Git state and:

1. fetch current protected `main` with complete required history;
2. reject shallow, missing-object, replacement/graft-influenced, or inconclusive state;
3. resolve `current-main-sha`;
4. require `git merge-base --is-ancestor <eligibility-main-sha> <current-main-sha>`;
5. require empty output from the complete equivalent of:

    ```text
    git rev-list --full-history \
      <eligibility-main-sha>..<current-main-sha> \
      -- .github/workflow-delivery/governance/hcoona-release-smoke-npm.json
    ```

6. re-read the exact current blob and require the admitted blob OID/content digest; and
7. revalidate schema, writers, native facts, expiry, and `live_enabled: true`.

Any intervening path touch invalidates the Attempt, including edit/revert, delete/restore, rename away/back, or a path-touching side branch merged into main. Unrelated main commits are allowed. Force/non-descendant lineage, incomplete history, or failed ancestry/path proof fails closed. Actions history is not involved.

Approval additionally compares the action's profile digest with current
Governance and validates the action as a typed profile instantiation. It does
not read mutable package-control state and does not claim to verify the
publisher's effective runtime. Immediately before the marker, the publisher
repeats Governance continuity, reads supported package-control state into a
Package-Control Proof, resolves the profile without defaults, verifies the
actual pinned toolchain and effective command configuration, and binds those
fresh facts in the marker.

## 10. Workflow Topology, Guards, and Permissions

### 10.1 Outer caller

| Job                         | Responsibility                                  | Maximum permission                          |
| --------------------------- | ----------------------------------------------- | ------------------------------------------- |
| `request`                   | Platform, purpose, selected-ref, target, Intent | `contents: read`                            |
| `discover-node`             | Exact-target Node Provider discovery            | `contents: read`                            |
| `compile-model`             | Request-local Model and NBGV                    | `contents: read`                            |
| `evaluate-live-eligibility` | Static policy and protected Governance          | `contents: read`                            |
| `run-live-attempt`          | Reusable call and Release Execution concurrency | `contents: read`, `packages: write` ceiling |

`run-live-attempt` is `uses`-only: no `steps`, shell, action, or direct token use. Its package-write declaration is only the reusable-workflow ceiling.

### 10.2 Reusable Live workflow

| Logical job               | Authority boundary                                          |
| ------------------------- | ----------------------------------------------------------- |
| `admit-current-attempt`   | Current request/Attempt/Model only; no history              |
| `plan-qualification`      | Frozen Qualification Snapshot                               |
| `build-tarball`           | Credential-free deterministic artifact                      |
| `project-test`            | Independent project-test Evidence                           |
| `qualify-artifact`        | Content and install/import Evidence                         |
| `finalize-qualification`  | Complete Evidence Admission and Decision                    |
| `observe-destination`     | Read-only active registry and package-control Observation   |
| `materialize-publication` | Zero/one-action Snapshot; summary and Bundle for one action |
| `exact-satisfied`         | Fresh zero-action finalization proof                        |
| `approve-publication`     | Sole Environment job and Authorization producer             |
| `publish-github-packages` | Sole step-running package writer                            |
| `finalize-attempt`        | Read-only best-effort current-DAG outcome                   |

Incidental batching/DAG detail is not frozen. Boundaries may not combine Environment wait with package write, qualification with a publication token, Observation with mutation, pre-wait materialization with Authorization, product/build execution with package write, or finalization with mutation.

### 10.3 Permissions

Workflow-level permissions are empty or read-only.
Admission/model/planning/build/test/qualification jobs have no package
permission. Observation uses public APIs or at most `packages: read`; it has no
PAT, destination write, `id-token: write`, or Environment. Materialization has
none. Exact-satisfied has only the minimum read authority needed for fresh
Governance, package-control, and exact-version readback. Approval has no
package permission and may have `contents: read`. Publisher alone has
`packages: write`, may have `contents: read`, and has no `id-token: write`.
Finalizer has no destination permission.

The publisher receives only short-lived repository `GITHUB_TOKEN`: no PAT fallback and no OIDC. No authority job needs `actions: read`.

### 10.4 Publisher executable isolation

Publisher checks out the exact selected target with persisted checkout credentials disabled and runs only the selected-revision Workflow Delivery publisher control plus pinned setup actions. This target-revision control is the explicit accepted-writer TCB exception; it is not an independent boundary against `hcoona`.

Publisher loads publication inputs only from exact current-run immutable artifacts by ID. It executes no Release Unit script, lifecycle hook, build, test, installer, target `.npmrc`, or packed package code, and it uses isolated npm configuration with scripts disabled.

The one publication invocation is isolated in a declared workflow step.
Publisher exposes that step's exact platform-evaluated
`steps.<publication-step>.outcome` as a job output; no script may synthesize
the value. The Finalizer uses exact `skipped` only to prove that
mutation-capable execution never started after a publisher failure or
cancellation. Missing or malformed output proves nothing.

### 10.5 Attempt-one guard

Every authoritative normal-Live job independently has a job-level `github.run_attempt == 1` condition: all outer producers and every reusable admission, plan, producer, finalizer, observer, materializer, no-op, Approval, publisher, and read-only Finalizer. `finalize-attempt` combines it with `always()`; normal jobs combine it with ordinary success/branch conditions.

An entry-only guard is insufficient because partial reruns exist. GitHub rerun commands are unsupported. Normal-Live records/artifacts omit run attempt; simulation keeps its current semantics.

## 11. Build, Qualification, and Determinism

### 11.1 Frozen plan

The Qualification Snapshot freezes target/ref, Release Unit/Product root, Build Definition/output, complete NBGV facts and `npmPackageVersion`, selected-revision `mise.toml`/`mise.lock` and package lock identities, control/catalog digests, observed Node/pnpm/npm/NBGV tools, target-commit `SOURCE_DATE_EPOCH`, Buddy projection, and obligations:

- `node/project-test-v1`;
- `node/npm-artifact-contents-v1`; and
- `node/npm-install-import-v1`.

### 11.2 Build

The credential-free build stages declared inputs outside the source checkout, applies frozen `npmPackageVersion` without fallback, creates canonical `package/workflow-delivery/provenance.json`, adjusts only the staged `files` allowlist, runs the Build Definition, normalizes paths/modes/order/timestamps/tar/gzip metadata, invokes `npm pack --ignore-scripts`, and emits exactly one tarball with one SHA-256 and one SHA-512.

The witness binds schema, target, Release Unit, canonical/native NBGV facts, Build Definition, catalog/control digests, and purpose. It excludes run, Attempt, transport, approval, and wall-clock identity so identical frozen inputs/toolchain reproduce identical bytes.

### 11.3 Qualification

Project test independently satisfies `node/project-test-v1`. Tarball qualification may share a physical job but emits distinct Evidence for content and install/import.

Content qualification verifies basename, package/version, deterministic entry manifest, expected files, exact witness path/bytes/bindings, both digests, and lifecycle script names/values. Install/import installs the exact tarball into an isolated fixture with scripts disabled, imports its declared entry point, and validates the installed witness.

Evidence Admission checks obligation, producer, target, artifact, Attempt, and digest without rerunning quality. Qualification succeeds only with all three exact Evidence records.

### 11.4 Determinism

The artifact must be deterministic for the same target, frozen inputs, Build Definition, NBGV facts, toolchain, and normalization. The workflow records and validates one build digest; it does not duplicate-build to certify determinism.

A retry fully rebuilds. Existing different bytes for the desired version block
normal Live and require separately governed operator investigation and
remediation. Nondeterministic units are unsupported pending a future
sealed-artifact publication-resume design.

## 12. Observation and Zero/One Action

### 12.1 Desired state and readback

After Qualification, the read-only Adapter derives destination, package,
frozen version, local SHA-256/SHA-512, witness/target, target-derived tag, and
expected package-control facts. It uses trusted isolated configuration and no
scripts.

Observation records the active registry projection: package/version existence,
supported owner/repository-association/visibility/exposed-access facts, active
version metadata, downloaded tarball bytes when present, computed digests,
embedded witness, tag mapping when readable, response status, selected
non-secret headers, and bounded diagnostics. It uses public APIs or minimum
read-only authority and receives no PAT, package-admin credential, destination
write, Environment, or deleted-version facts.

Exact state requires downloaded remote bytes and the exact in-package witness.
A local sidecar, registry integrity field, or matching version string is
insufficient. Exact package-version state is authoritative regardless of
whether the target-derived tag is absent, points to that version, points
elsewhere, or cannot be read. Active absence does not prove the version was
never published, has no deleted/restorable tombstone, or can be created.

### 12.2 Mechanical state machine

| Active-state and package-control result                                                                         | Snapshot             | Downstream path                                                  |
| --------------------------------------------------------------------------------------------------------------- | -------------------- | ---------------------------------------------------------------- |
| Exact version bytes/digests/witness and package control; tag in any state or unreadable                         | `actions: []`        | Fresh exact-satisfied finalization proof                         |
| Version active-absent; tag observed absent; package control exact; Governance binds unexpired native acceptance | One compound action  | Approval                                                         |
| Version active-absent; tag present or unprovable                                                                | No admissible action | Blocking Observation; `failed-before-publication` if finalizable |
| Existing differing bytes, digests, witness, or target                                                           | No admissible action | Blocking Observation; fail closed                                |
| Package control mismatched or unprovable                                                                        | No admissible action | Blocking Observation; fail closed                                |
| Version/tag metadata partial, conflicting, unknown, or unreadable where required                                | No admissible action | Blocking Observation; fail closed                                |

Publication Snapshot schema restricts `actions` to length zero or one. More is invalid.

The target Destination Operation Profile owns this fixed request shape:

```text
npm publish <qualified-tarball> \
  --registry https://npm.pkg.github.com \
  --tag buddy-sha-<40-lowercase-target-sha> \
  --ignore-scripts \
  --fetch-retries=0
```

The Publication Action's profile-instantiation inputs are only the profile
digest plus exact tarball, package, version, and target-derived tag operands.
It also carries the `action-id`, complete `mutable-resource-keys`, and
`serialization-projection` required by the closed schema in section 7.4. The
profile owns registry, access mode, pinned Node/npm versions, normalized
command/options, highest-precedence configuration, and retry prohibition.
Approval and publisher resolve the profile without defaults and validate the
action as a typed instantiation.

The command provides non-overwriting creation for the authoritative immutable
version but may move the declared tag after a post-Observation race because
GitHub Packages exposes no expected-value condition for the compound request.
That routing side effect is accepted only for this dedicated smoke package and
sole-writer TCB after section 18 passes. It is not version-plus-tag CAS and
does not authorize tag repair. A hidden tombstone may instead make the command
fail definitively; that failure never becomes same-Attempt `published`.

No separate tag-only, delete, restore, overwrite, visibility, permission, or
administrator action exists. A destination conflict is not same-Attempt
success; a new dispatch may later observe exact active state.

## 13. Approval Bundle, Environment, and Authorization

### 13.1 Pre-wait materialization

For one action, `materialize-publication` durably uploads the Snapshot, renders
deterministic reviewer Markdown, uploads it and captures transport, optionally
mirrors it to the job summary, forms an Approval Bundle with direct references
to that Snapshot and reviewer summary, and uploads the Bundle before Approval
can wait.

The Approval deployment URL points to the immutable reviewer-summary artifact or authenticated artifact page. The uploaded Markdown, not its visual projection, is authoritative.

### 13.2 Reviewer summary

The summary contains repository/run, selected ref/target, Release
Unit/destination, package/version/tag, artifact
ID/URL/SHA-256/SHA-512/manifest/witness, exact lifecycle scripts or none,
Qualification, Governance identity/freshness, native-acceptance generation and
age, active-state Observation, exact profile digest and dynamic action
operands, complete resource keys, conservative group, and warnings that the
tag is non-authoritative and repository token reach is not package-isolated.
It contains no secret or acceptance-only tombstone fact.

### 13.3 Approval Bundle

`workflow-delivery/v3/approval-bundle` directly binds only the exact
action-bearing Publication Snapshot Artifact Reference and immutable reviewer-
summary Artifact Reference. The Snapshot reaches Intent, Execution, Attempt,
selected ref/target/run, Eligibility, Qualification/Evidence, artifact,
Observation, action, resources, and conservative projection. The Bundle
contains no copied ancestor fields, approval fact, credential, Environment, or
approver identity.

### 13.4 Approval job

`approve-publication` is the only job referencing `workflow-delivery-v3-buddy-approval`. It has no package publication permission.

Its first declared executable step performs only a case-sensitive exact comparison of the resolved sentinel with `workflow-delivery-v3-buddy-approval/v1`. It precedes checkout, artifact download, control execution, and other authority-critical work; missing/empty/mismatched value fails with no `continue-on-error`.

That check proves only the resolved value under external native attestation. It cannot prove source scope, reviewers, self-review, bypass, deployment policy, secrets, or broader-variable absence.

After the wait and sentinel, Approval obtains only exact selected-revision
control, executes no product/build hook, downloads artifacts by ID, repeats
Governance ancestry/path/freshness checks, verifies that action-bearing
acceptance remains within 90 days, validates the Bundle's complete transitive
Snapshot/action/resource closure, resolves the Destination Operation Profile,
and validates the action as its exact typed instantiation before durably
uploading the sole Authorization. Approval does not read mutable
package-control state and does not claim to verify the publisher's actual
runtime configuration.

GitHub does not expose approver login in normal job context. No actor is recovered or invented. The approval fact is successful post-wait execution and Authorization production by logical job `approve-publication` under the literal Environment.

### 13.5 Publication Authorization

`workflow-delivery/v3/publication-authorization` directly binds only the
Approval Bundle Artifact Reference plus canonical approval-boundary evidence
for literal Environment `workflow-delivery-v3-buddy-approval`, logical job
`approve-publication`, the exact sentinel result, and the fresh protected-
Governance continuity proof. It reaches Snapshot, summary, action, resources,
artifact, Attempt, and target transitively through the Bundle.

It contains no credential, secret, approver/recovered actor, historical authority, prior-Attempt reference, or run attempt. There is no later post-approval bridge; publisher independently revalidates this Authorization.

## 14. Publisher, Marker, and Result

### 14.1 Entry and preflight

Publisher is an ordinary success-dependent consumer of Approval, has no Environment, and holds the publication resource concurrency group with `cancel-in-progress: false`.

Before mutation it validates attempt one; the Authorization and its exact
transitive Bundle/Snapshot/summary/action/artifact/resource closure; producer,
run/ref/target/control and purpose bindings; action cardinality; the concrete
package/version/tarball/tag operands; and complete resource/group derivation.
It independently repeats protected-Governance ancestry/path/blob/content,
schema, expiry, enablement, profile-digest, and acceptance-age checks.

It then freshly reads supported package-control state and requires expected
owner `hcoona`, repository association `hcoona/three`, visibility, and exposed
access facts. Unexposed grant completeness remains the protected-Governance
limitation and is not replaced by an invented runtime proof. It resolves the
Destination Operation Profile without defaults, validates every typed operand,
and verifies the actual pinned toolchain and effective command configuration.

Any mismatch prevents marker and mutation. Flag-off blocks a publisher before its final fresh check but cannot revoke one already beyond that check.

### 14.2 Resolved profile and isolated npm configuration

The canonical Destination Operation Profile is the sole owner of its stable
profile identity, registry `https://npm.pkg.github.com`, access mode, exact
Node/npm versions, normalized command template containing every fixed CLI
option, typed operand rules, request-generation behavior, and retry
prohibition. Governance binds its digest; the Publication Action binds that
digest plus concrete package, version, tarball, and tag operands.

The first activation uses this closed canonical profile:

| Field                      | Exact value                                                                                                                                                                           |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `profile-id`               | `npm/github-packages-hcoona-three-standard-publish-v1`                                                                                                                                |
| `registry`                 | `https://npm.pkg.github.com`                                                                                                                                                          |
| `access-mode`              | `existing-public-package/no-access-mutation`                                                                                                                                          |
| `node-version`             | `24.19.0`                                                                                                                                                                             |
| `npm-version`              | `11.17.0`                                                                                                                                                                             |
| `command-template`         | `["npm","publish","{tarball-path}","--registry","https://npm.pkg.github.com","--tag","{tag}","--ignore-scripts","--fetch-retries=0"]`                                                 |
| `operand-slots`            | Exact package, version, tarball Artifact Reference/path, and target-derived tag; package and version must equal the tarball manifest                                                  |
| `configuration-precedence` | CLI owns registry, tag, script suppression, and fetch retry count; authentication comes only from the current repository `GITHUB_TOKEN` through generated temporary npm configuration |
| `request-generation`       | Standard request generation of the pinned npm client; no wrapper registry protocol or hand-built publish request                                                                      |
| `mutation-retry`           | None after request initiation, including conflict, non-success, timeout, or ambiguous response                                                                                        |

Profile admission verifies that the existing package container is public and
associated with `hcoona/three`; the publish command neither changes nor
requests visibility or access. Target code recomputes the canonical profile
digest and records the exact effective runtime/configuration match before the
marker. A different Node/npm patch, argv order, option source, registry,
credential source, or implicit mutation-retry policy is a different profile
and blocks this action.

Publisher creates runner-private configuration for the exact `@hcoona`
registry, supplies `GITHUB_TOKEN` without artifact/log exposure, fixes the
user-config path, prevents target/project npm configuration loading, verifies
effective registry and scripts-disabled behavior, requires the highest-
precedence CLI value `fetch-retries=0`, and sanitizes diagnostics. It
materializes the effective request only from the resolved profile and explicit
operands. This configuration does not create version-plus-tag CAS.

### 14.3 Mutation marker

Immediately before the first mutating command, publisher durably uploads
`workflow-delivery/v3/github-packages-mutation-may-have-started`. Its
normal-Live producer/current-run envelope identifies the publisher. The marker
directly binds only:

- the Publication Authorization Artifact Reference and payload digest;
- the final protected-Governance proof;
- the final Package-Control Proof; and
- canonical evidence that the actual toolchain and effective command
  configuration matched the resolved Destination Operation Profile.

Authorization remains the sole approved closure over Snapshot, action,
resources, artifact, Attempt, and target; the marker does not copy them.

Marker upload failure prevents publication. Once durable, mutation is conservatively possible until a durable Result proves a controlled outcome.

### 14.4 One invocation and readback

After section 18 passes and current Governance binds that acceptance
generation, Publisher invokes the resolved standard npm profile exactly once.
The request uses the action-bound tarball, package, version, and target tag plus
the profile-owned registry, scripts-disabled configuration, and
`--fetch-retries=0`. The zero retry value prevents npm from automatically
resending a retryable mutating request; it does not provide tag compare-and-
swap.

Publisher runs no second publish, separate tag command, implicit `latest`,
overwrite, delete, restore, permission change, compensation, or automatic
mutation retry after conflict, non-success, or ambiguity. Bounded read-only
readback retries are permitted.

It then records command classification and sanitized response identity and
freshly reads the authoritative exact package version by downloading its
tarball and validating bytes, both digests, and embedded witness. It also
records the observed target-tag mapping as non-authoritative diagnostics.
Command success without authoritative exact-version readback is not
`published`; tag state cannot create or defeat publication success.

### 14.5 Publication Result

For each controlled post-marker terminal state, Publisher forms one logical
`workflow-delivery/v3/publication-result` and initiates one logical persistence
operation. Transport may retry only the same immutable payload.

Publisher declares exactly these job outputs:

| Job output                       | Wire value                                                                                                                                                                     |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `publication-terminal-reference` | One scalar string containing exactly canonical JSON `null` or one Shared Foundation Artifact Reference object whose target schema is the mutation marker or Publication Result |
| `publication-step-outcome`       | The direct platform expression `steps.<publication-step>.outcome`                                                                                                              |

For every controlled Publisher completion, the terminal-reference output is
exactly canonical JSON `null` or one strictly validated current-Attempt
Artifact Reference. It is the Result reference when a Result was durably
persisted, otherwise the marker reference when the marker was durably
persisted, otherwise null. Result takes precedence. The target record's schema
is the discriminator; no wrapper record or collection is introduced. A marker
target may be exposed only after validated persistence and before mutation; a
Result target may be exposed only after durable Result persistence. The
implementation may choose its non-authoritative step choreography but cannot
weaken those observable ordering and transport invariants.

For a running Publisher, an empty or missing terminal-reference output is not
`null` and fails Finalizer admission. When the entire Publisher job is skipped,
the workflow boundary may map GitHub's absent output to domain `null` only
while also supplying Publisher conclusion `skipped`; any other mapping is
invalid. Arrays, multiple references, wrapper objects, non-canonical JSON,
unexpected fields, invalid transport metadata, unadmitted target schema, or
lineage mismatch fail admission. No component lists or searches for marker or
Result artifacts.

`publication-step-outcome` is never script-produced. The job-output declaration
must reference the platform-owned step context directly. Missing step outcome,
command exit status, shell booleans, Result presence, marker presence, and the
Publisher job conclusion are not substitutes.

The Result's normal-Live producer/current-run envelope identifies Publisher.
It directly binds only the durable marker plus command classification,
post-action readback when available, mutation classification
`not-mutated`/`possibly-mutated`/`mutated`, sanitized diagnostics, and
destination outcome. It does not repeat requested coordinate/tag, action,
resources, expected artifact digests or witness, pre-action Observation, or
other facts authoritative through
`Result -> Marker -> Authorization -> Approval Bundle -> Snapshot`.

A `published` Result requires definitive success from the current command,
`mutation-classification: mutated`, and authoritative exact-version readback.
Conflict, non-success, or ambiguous response remains a failed Result in this
Attempt even if readback is exact. A controlled failed Result may use
`not-mutated` only when complete supported evidence proves no mutation;
otherwise it is `possibly-mutated` or `mutated`. The target tag is diagnostic
and never determines success.

A failure before the marker emits no Publication Result. Marker without durable
Result is unknown and possibly mutated. Result persistence or transport failure
is not repaired or synthesized.

### 14.6 Durable publication-state matrix

| Durable current-run and direct platform facts                                                                                                                                                                                         | Outcome                     | `possibly_mutated` | Required posture                                                                                                    |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- | -----------------: | ------------------------------------------------------------------------------------------------------------------- |
| Sole blocking Observation; no valid zero-action Snapshot; null publication terminal reference; Publisher `skipped`, or Publisher `failure`/`cancelled` with exact platform publication-step outcome `skipped`                         | `failed-before-publication` |              false | New dispatch reobserves; governed operator investigation/remediation may be required before productive continuation |
| Exactly one other admitted pre-marker predecessor; no valid zero-action Snapshot; null publication terminal reference; Publisher `skipped`, or Publisher `failure`/`cancelled` with exact platform publication-step outcome `skipped` | `failed-before-publication` |              false | New dispatch                                                                                                        |
| Valid failed Result proving `not-mutated`                                                                                                                                                                                             | `publication-failed`        |              false | New dispatch reobserves                                                                                             |
| Valid failed Result classified `possibly-mutated` or `mutated`                                                                                                                                                                        | `publication-failed`        |               true | Read-only operator investigation before any later dispatch                                                          |
| Publication terminal reference resolves to marker                                                                                                                                                                                     | `unknown`                   |               true | Read-only operator investigation before any later dispatch                                                          |
| Null publication terminal reference and publication-step start cannot be excluded                                                                                                                                                     | `unknown`                   |               true | Read-only operator investigation before any later dispatch                                                          |
| Valid `published` Result with Publisher `success`/`failure`/`cancelled`                                                                                                                                                               | `published`                 |              false | Complete                                                                                                            |
| Malformed, misbound, or other-kind publication terminal reference, or contradictory lineage                                                                                                                                           | No authoritative Outcome    |                n/a | Fail closed                                                                                                         |

No row authorizes continuation inside the same Attempt after a failed or ambiguous publish. The only normal recovery boundary is a new manual dispatch and fresh Observation.

## 15. Exact-Satisfied, Finalization, and Retry

### 15.1 Exact-satisfied

A zero-action Snapshot takes no Environment, Approval Bundle, Authorization,
publisher, write token, marker, or Result. The `exact-satisfied` job requires
Publisher `skipped` and no action-bearing lineage. Immediately before success
it:

1. repeats section 9 protected-Governance ancestry, path-touch, blob/content,
   schema, expiry, and `live_enabled` checks;
2. freshly reads supported package-control state; and
3. freshly downloads the exact active version and verifies bytes, SHA-256,
   SHA-512, and embedded witness against the Snapshot.

It binds those three checks and the zero-action Snapshot in one
`workflow-delivery/v3/exact-satisfied-finalization-proof`. Native-acceptance
age does not block this zero-action path. Tag state remains irrelevant. Missing,
stale, conflicting, unknown, or partial proof yields no exact-satisfied
success.

### 15.2 Read-only Finalizer

`finalize-attempt` is best effort, declares all relevant direct `needs`, and
uses only current-DAG job results, exact platform-evaluated publication-step
outcome, one nullable scalar publication terminal Artifact Reference, other
explicit current-DAG Artifact References, and canonical records. It validates available
Intent/Model/Eligibility/Qualification/Observation/Snapshot and either the
exact-satisfied finalization proof or Approval/Authorization/marker/Result
lineage.

It never lists historical runs/jobs/deployments/artifacts, recovers a reviewer, queries destination state to invent a Result, infers publication from green status, adopts a prior Attempt, reruns quality, repairs missing lineage, or mutates.

The canonical Attempt Outcome contains only:

- `disposition`: `exact-satisfied`, `published`,
  `failed-before-publication`, `publication-failed`, or `unknown`;
- `possibly_mutated`; and
- exactly one tagged direct predecessor.

Only these terminal combinations are valid:

| Durable domain and direct platform facts                                                                                                 | Disposition                 | `possibly_mutated` |
| ---------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- | -----------------: |
| Zero-action Snapshot, valid exact-satisfied finalization proof, Publisher `skipped`, and no action-bearing lineage                       | `exact-satisfied`           |              false |
| Valid `published` Result; Publisher `success`, `failure`, or `cancelled`                                                                 | `published`                 |              false |
| Exactly one admitted pre-marker predecessor, no valid zero-action Snapshot, null publication terminal reference, and Publisher `skipped` | `failed-before-publication` |              false |
| Same record state and Publisher `failure`/`cancelled` with exact platform publication-step outcome `skipped`                             | `failed-before-publication` |              false |
| Valid failed Result proving `not-mutated`                                                                                                | `publication-failed`        |              false |
| Valid failed Result classified `possibly-mutated` or `mutated`                                                                           | `publication-failed`        |               true |
| No valid Result and mutation cannot be excluded                                                                                          | `unknown`                   |               true |
| Valid zero-action Snapshot without valid finalization proof, Publisher `skipped`, and no action-bearing lineage                          | `unknown`                   |              false |

A non-null publication terminal reference with Publisher `skipped`, a zero-action Snapshot with
non-skipped Publisher, malformed or conflicting lineage, or any unlisted tuple
is contradictory and produces no authoritative Outcome. Failed or incomplete
Qualification remains the terminal authoritative record and forms no Outcome.

The direct predecessor is, in priority order: Publication Result, marker,
exact-satisfied finalization proof, zero-action Snapshot, Publication
Authorization, Approval Bundle, action-bearing Snapshot, sole blocking
Observation, or exact successful Qualification Decision. Multiple candidates
at the selected tier, contradictory lineage, or malformed scalar transport
produce no authoritative Outcome.

The admitted pre-marker predecessor is one Authorization, Bundle,
action-bearing Snapshot, sole blocking Observation, or the exact successful
Qualification Decision only when interruption occurred before any Observation.
A non-blocking `exact-satisfied` or `absent` Observation followed by Snapshot
materialization or transport failure has no admitted predecessor and forms no
Outcome; Finalizer does not skip it and fall back to Qualification.

A valid Result controls `published` or `publication-failed` even when the
Publisher job later reports `failure` or `cancelled`. A green Publisher without
a valid Result is not publication evidence. Marker without Result is `unknown`
and possibly mutated. A zero-action Snapshot without a valid finalization proof
is `unknown` with `possibly_mutated: false`.

For an admitted pre-marker path, Publisher `skipped` proves publisher
non-start.
Publisher `failure` or `cancelled` may produce
`failed-before-publication` only when the exact platform-derived publication-
step outcome is `skipped`, Qualification was exact and successful, no valid
zero-action Snapshot applies, the publication terminal reference is null, and
no contradictory lineage exists. Publisher conclusion, missing output,
script-produced flags, or missing
transport alone cannot prove non-start. Missing or malformed terminal transport
remains inadmissible. With a valid null terminal reference, if publication-step
start cannot be excluded, Outcome is `unknown` with
`possibly_mutated: true`.

Cancellation, runner loss, or artifact transport failure may leave no durable Outcome. No record is safer than a fabricated one.

### 15.3 Retry

Retry is a new manual dispatch and `workflow_run_id`. It resolves ref, recompiles Model, reruns Eligibility, rebuilds, requalifies, reobserves, rematerializes, and reapproves if one action remains. No prior Model, artifact, Evidence, Snapshot, approval, Authorization, marker, Result, or Outcome is authority.

Fresh Observation resolves current active state: exact becomes the zero-action
path; active-absent may form one action only with absent tag, exact package
control, and unexpired Governance-bound acceptance; conflicting or unprovable
state fails closed. Unknown or possibly mutated prior state may require
read-only operator investigation before a new dispatch. The first slice does
not implement a formal Reconciliation workflow or record. GitHub rerun commands
are not recovery.

## 16. Concurrency

### 16.1 Release Execution

Outer `run-live-attempt` holds:

```text
wdv3-execution-<sha256(JCS(Buddy Execution Identity))>
```

Identity contains channel, Release Unit, and target. The caller-held group begins before the reusable Attempt, spans its terminal state including Finalizer when it runs, and has `cancel-in-progress: false`. Read-only request/model preparation occurs before it.

### 16.2 Publication resources

The compound action binds:

1. External Package Coordinate: channel + destination + normalized package + frozen version; and
2. dist-tag resource: destination + normalized package + `buddy-sha-<target>`.

Because GitHub has equality groups rather than set-overlap locks, the Adapter projects all actions for one physical destination and normalized package to:

```text
wdv3-resource-<sha256(JCS(physical destination + normalized package))>
```

This safely over-serializes versions/tags and has `cancel-in-progress: false`. It never replaces complete resource keys. Missing/conflicting/unenforceable keys block publication. Concurrency is not authorization, reservation, token isolation, or protection from external writers.

## 17. Retention and Diagnostics

All normal-Live authoritative artifacts use 45-day retention: Intent/Model
transport, Eligibility/Governance proof, Qualification Snapshot,
tarball/manifest, Evidence/Decision, Observations/Snapshot, reviewer
summary/Bundle, Authorization, marker, Result, exact-satisfied finalization
proof, and Outcome. Embedded Package-Control Proof bytes are retained only
inside their Observation, marker, or exact-satisfied finalization-proof parent.

Fresh preactivation and post-merge evidence must authenticate
`GET /repos/hcoona/three/actions/permissions/artifact-and-log-retention`,
capture endpoint identity, time, normalized response, and canonical response
digest, and require integer `days >= 45`. Requested `retention-days: 45` in a
workflow is not evidence that repository policy permits it.

Logs and job summaries are diagnostic projections. Diagnostics may include run attempt, job/step result, Git status, endpoint/status/non-secret headers, canonical response digests, authority timing and inventory counts/digests, npm exit classification, and sanitized errors.

Redact `GITHUB_TOKEN`, npm auth lines, authorization headers, credential-bearing URLs/config, secrets, and unbounded response bodies. Native Actions history may be linked diagnostically but is never authority.

## 18. Semantic Acceptance Plan

### 18.1 Records and transport

- Reject duplicate/unknown fields, noncanonical paths/SHAs/digests/sets/JSON, and wrong producer/run/target/purpose.
- Reject normal-Live `run-attempt`; prove Attempt identity changes with `workflow_run_id` while artifact witness excludes run/Attempt.
- Enforce Snapshot action cardinality zero/one; Bundle direct references only to
  Snapshot and reviewer summary; Authorization direct reference only to Bundle
  plus approval-boundary/fresh-Governance evidence; marker direct reference to
  Authorization plus final Governance, package-control, and profile-match
  proofs; and Result direct reference only to marker plus newly produced
  post-action facts.
- Reject every Receipt schema and the former `ActionResult` schema.
- Admit one nullable scalar publication terminal Artifact Reference resolving
  only to marker or Result; require Result precedence and Result-to-marker
  resolution; reject malformed, non-scalar, misbound, other-kind, name-based,
  latest, wrapper, collection, or history-derived transport.
- Prove `published` requires definitive current-command success,
  `mutation-classification: mutated`, and authoritative exact-version readback;
  a conflict, non-success, or ambiguous response remains failed even when
  readback is exact.

### 18.2 Static-reference policy

- Prove `git-target`, index, and worktree read their declared bytes when all three differ.
- Reject unmerged index candidates, candidate symlinks/submodules, missing
  objects, unreadable/invalid candidates, and other admitted exact-source
  acquisition failures as `source-acquisition-failed`; reject an omitted or
  unknown source kind and malformed required source parameters with a nonzero
  pre-Result invocation failure; and reject non-`git-target` Live Results.
- Cover the prohibited forms assigned to each retained surface in the
  selector-to-fact matrix; do not require a universal form-by-family cross
  product.
- For every selector row, prove one boundary near-miss and cover every
  explicitly enumerated positive path-selector alternative at least once,
  without requiring cross-products of independent alternatives. Every case
  selects exactly one row or no row. For every declared unsupported category,
  prove at least one adapter-level representative is excluded or fails closed
  without fallback. Do not reproduce exhaustive upstream grammar variants.
- Allow only exact producer top-level `name` and legitimate workspace/importer
  producer-root references.
- Prove pnpm reserved basenames below `.github/workflows` select no pnpm row;
  all retained selector rows remain pairwise disjoint.
- Prove npm manifest projection uses `.content`, only the exact top-level name
  and four declared dependency sections, fixed section/key ordering, string
  field shapes, one-level alias `subSpec`, source-owned `where`, and no
  normalize/prepare/fix or partial fact emission.
- Prove pnpm workspace projection preserves package-pattern index/value,
  traverses default and named catalogs without merging, branches once through
  `WorkspaceSpec.parse`, uses exact fixed parser arguments, and performs no
  member discovery or partial fact emission.
- Prove pnpm lockfile version is exactly `9.0`, conflicted input is rejected
  through merge information, branch-lock behavior is disabled, the official
  reader reads only the declared snapshot, and subsequent pure companion APIs
  emit only bounded facts.
- Prove loaded importer IDs, `specifiers`, `dependencies`,
  `devDependencies`, and `optionalDependencies` use the exact selected shapes
  and bound order; every section key maps to its own specifier, every specifier
  belongs to a selected section, and equal keys across sections remain distinct
  entries.
- Prove each loaded package snapshot emits identity and typed lock-owned
  resolution plus direct `dependencies` and `optionalDependencies` edge keys
  in the exact bound order. Include a transitive alias whose dependency key is
  the producer while its resolved target is another package.
- Prove combined environment/main and environment-only pnpm lock documents are
  `unsupported-projection` before the main reader can discard a document.
- Prove named/ranged workspace specs can emit `W` when their official
  `refToRelative` result is null, while non-workspace `link:`, bare/path-local
  and path-form workspace references, unexplained null snapshot keys, or
  required missing snapshots cannot produce a clean pnpm-lock Result. No path
  invokes the local, Git, tarball, or registry resolver.
- Prove exact artifact schemas, library/CLI/runtime identities and versions,
  public APIs or commands, lock/checksum provenance, source-snapshot closure,
  byte-input modes, and stable normalized fact projection.
- Prove npm manifests reject unsupported `workspace:` dependency values and npm
  lockfiles remain outside the selector.
- Prove every npm-package-arg call receives an explicit source-owned base and a
  controlled HOME; changing ambient cwd or home cannot change emitted facts,
  and tilde or repository-escaping paths are `unsupported-projection`.
- Prove NuGet model versions `1`, `2`, and `3` are admitted, including
  representative source values that the pinned NuGet model coerces to those
  versions. Missing or unconvertible versions and parser failures that produce
  `int.MinValue`, plus model versions below `1` or above `3`, are rejected
  before facts are emitted.
- Prove the exact NuGet stream/logger/logical-path overload, sole-model version
  admission, target/dependency/edge ordering, selected model fields,
  `PackagesConfigReader(Stream, false)`, duplicate-ID rejection, and
  `OrderBy(p => p.PackageIdentity, PackageIdentity.Comparer)` ordering without
  adapter-owned JSON or XML traversal.
- Before enabling the root gate, prove the tracked Hexo example manifest
  contains exact `file:../..`, its actual isolated pnpm v9 lock has the matching
  importer specifier/reference and typed file-directory snapshot for `../..`,
  and the static policy returns clean without an example-path selector
  exception.
- Prove the only tracked selected `package.json` whose top-level name equals
  the producer is the exact producer manifest. The npm publish request fixture
  source must have a non-candidate basename, materialize as
  `package/package.json` only below test-owned temporary storage, preserve its
  package/tarball assertions, and require no fixture-path policy exception.
- For each file-oriented JSON/YAML authority, prove malformed UTF-8 cannot be
  replacement-decoded into a fact and exact bound BOM behavior belongs to the
  selected reader. In particular, pnpm zero-, one-, and two-BOM inputs produce
  equivalent facts; additional-BOM outcomes match the exact stack; snapshot
  bytes and digests remain unchanged.
- Prove distinct `source-acquisition-failed`, `encoding-rejected`,
  `authority-rejected`, `authority-execution-failed`,
  `unsupported-projection`, `authority-mismatch`, and `cleanup-failed` Results.
- Prove source candidates, graph nodes, arrays, and selected mappings follow
  their bound deterministic order; the first source-acquisition failure becomes
  the sole `error-kind` before graph execution, otherwise the first typed
  non-cleanup graph failure becomes the sole `error-kind`, and cleanup failure
  overrides either.
- Prove exact snapshot/HOME/cache/temp/output cleanup after success, rejection,
  execution failure, timeout, and cancellation; injected exact-path cleanup
  failure must return `cleanup-failed`.
- Prove GitHub workflow/composite-action files, Node import subpaths, npm, uv,
  and Yarn locks, unevaluated MSBuild project/central manifests, standalone
  Python manifests, shell and PowerShell scripts, and other excluded surfaces
  remain outside the bounded projection.
- Prove no handwritten ecosystem grammar or schema, competing-authority
  hardening, whole-file exception, Tree-sitter or dataflow dependency, fixed
  inventory authority, or consumer claim remains.
- Prove root HK requires explicit `index`/`worktree`, runs independent of candidate file selection, and cannot supply Live Evidence.

### 18.3 Workflow contracts

- Parse YAML and prove purpose-first routing, selected control equals target, and every authoritative job has its own attempt-one guard.
- Prove reusable caller is `uses`-only; only caller ceiling/publisher declare package write; publisher is the only step-running writer.
- Prove only Observation or an explicit no-op reobserver may declare
  `packages: read`; neither receives a PAT, package-admin authority, or
  deleted-state facts. Build, qualification, materialization, Approval, and
  Finalizer receive no destination permission.
- Prove Approval is the only Environment job, has no package write, and publisher has ordinary success dependency with no Environment.
- Prove no authority history permission/query, publisher product/build/lifecycle script, legacy publication Environment authority, run-attempt artifact name, or name-based artifact retrieval; prove publisher checkout is exact-target and persists no credentials.
- Prove the isolated publication step exposes only its exact platform-evaluated
  `steps.<publication-step>.outcome`; no script or caller flag can claim
  non-start. The Finalizer receives one nullable scalar publication terminal
  reference through declared `needs`.
- Prove overwrite-disabled 45-day uploads and unchanged Official simulation rerun semantics.

### 18.4 Governance

- Admit only exact schema
  `workflow-delivery/v3/normal-live-governance-attestation-v2`; prove v1 fails
  before Release Execution, Attempt, or Environment creation.
- Admit blocked v2 only as `{"state":"blocked"}` with no native-evidence fields
  and `live_enabled: false`; reject blocked/true, blocked evidence, incomplete
  ready evidence, and ready/true with any invalid attestation. Prove
  ready/false remains a valid revocation document but cannot admit Live.
- Admit unrelated main advance.
- Reject direct edit, edit/revert, delete/restore, rename round-trip, and path-touching side-branch merge.
- Reject non-descendant/force update, shallow/missing history, missing/non-blob path, expired/over-90-day attestation, writer/native-fact change, or `live_enabled: false`.
- Require authenticated repository artifact-retention readback with
  `days >= 45`; reject missing, stale, malformed, or lower values.
- Validate the exact Destination Operation Profile digest,
  native-acceptance-suite version, disposable-package preconditions,
  API/contract revisions, capture time, and evidence digest identifying the
  successful acceptance generation. Reject copied detailed acceptance
  inputs/results or tombstone facts in runtime Governance.
- Prove action-bearing admission fails when acceptance is older than 90 days
  and zero-action exact-satisfied remains eligible under a fresh Governance
  attestation.
- Exercise Approval and publisher Governance proofs independently; Approval
  must not read package-control state or claim runtime-profile proof, while
  Publisher must bind both fresh proofs in the marker.

### 18.5 Build and qualification

- Prove frozen NBGV target/version binding and absence of recomputation/fallback.
- Prove isolated staging, unchanged source manifest, exact witness, deterministic bytes for stable frozen inputs/toolchain, and one produced build rather than certification rebuild.
- Prove separate project-test, artifact-content, and install/import Evidence; scripts-disabled install/import; exact lifecycle extraction; and failure on missing/substituted Evidence.

### 18.6 Observation and Snapshot

- Prove Observation's strict Qualification Decision Artifact Reference resolves
  the complete Snapshot/Live-Eligibility/protected-Governance lineage; reject a
  digest-only, missing, substituted, or misbound predecessor.
- Cover authoritative exact-version state to zero actions when the tag is
  absent, exact, mismatched, or unreadable. Require fresh exact-version
  bytes/digests/witness again in the exact-satisfied finalization proof; prove
  no tag repair can form.
- Cover active-absent version plus observed-absent tag, exact package control,
  and unexpired Governance-bound acceptance to exactly one action. Prove active
  absence does not claim never-published, no tombstone, or guaranteed creation.
- Route differing bytes/digests/witness/target, package-control mismatch,
  active-absent version with present or unprovable tag, inaccessible tarball,
  and partial/conflicting/unknown metadata to a blocking Observation with no
  action.
- The versioned Native Acceptance Suite owns one closed canonical comparison
  shape over complete active version inventory, complete tag mapping,
  scenario-version bytes/digests/witness, and supported package-control facts;
  raw responses and excluded volatile fields remain auditable evidence.
- Prove a fresh publish changes only the declared immutable version and target
  tag, leaves `latest`, unrelated versions/tags, and package control unchanged,
  and reads back exact bytes/digests/witness.
- Prove identical- and differing-byte duplicate publishes against an active
  version fail without changing the complete canonical shape.
- After active-absent Observation for desired version `V` and tag `T`, create
  distinct exact version `W` with `T -> W` on the separately authorized
  disposable package. The candidate may fail or may create exact `V` and move
  `T`; in either accepted result both immutable versions remain exact and no
  unrelated projected state changes. This is the bounded non-authoritative tag
  race, not CAS.
- For the deleted/restorable scenario, publish and verify a fresh unique
  disposable version, delete it with acceptance-only package-admin authority,
  prove active absence plus the complete deleted inventory, targeted tombstone
  identity, and continued restorability, then invoke identical- and
  differing-byte same-version publishes sequentially. Each must fail
  definitively and leave the complete active/deleted inventories, target
  tombstone, tag mapping, and package control unchanged; prove the first empty
  delta before the second invocation. Restore the original object and verify
  original bytes, digests, and witness. Any success, ambiguity, semantic delta,
  lost restorability, or restore/readback failure rejects the profile.
- Prove acceptance-only package-admin credentials and deleted-state facts never
  enter runtime workflow inputs, records, Governance, Observation, or
  publication. Synthetic tests alone cannot admit GitHub Packages destination
  support.

### 18.7 Approval

- Prove Snapshot/summary/Bundle are durable before wait and Environment URL identifies immutable summary.
- Prove the reviewer summary renders all artifact, lifecycle, Governance,
  active Observation, profile/action operand, resource, concurrency,
  non-authoritative-tag, and blast-radius context, while Bundle directly binds
  only Snapshot and summary Artifact References.
- Prove sentinel comparison is first executable Approval step, exact/fail-closed, and runtime cannot claim source scope.
- Prove fresh Governance, unexpired action-bearing acceptance, profile
  resolution, typed action instantiation, and complete transitive closure
  precede Authorization. Approval does not read mutable package control or
  claim actual runtime-profile validation.
- Prove Authorization directly binds only Bundle plus approval-boundary and
  fresh-Governance evidence, reaches all other facts transitively, binds the
  literal Environment/logical job, and contains no approver.
- Validate native-attestation structure for reviewer, self-review, bypass, wait, deployment policy, secrets, sentinel scope, and broader-variable absence.

### 18.8 Publisher and Result

- Prove Publisher cannot start without successful Authorization and resolves
  its complete transitive closure before mutation.
- Prove final Governance, supported package-control, and actual profile/runtime
  checks occur at the publisher boundary; the marker directly binds those
  proofs plus Authorization and does not copy Snapshot/action/resource facts.
  Package-Control Proof must bind the exact destination/normalized-package
  subject; the parent must bind applicable Governance and derive expectations
  from it. The proof must not copy expected values or the Governance digest.
- Prove marker failure blocks the operation; durable marker precedes exactly
  one isolated publication step; isolated configuration ignores target config.
- Prove Destination Operation Profile is sole owner of registry, access mode,
  exact Node/npm versions, normalized command/options, and retry prohibition;
  the Action's profile-instantiation inputs are only profile digest plus exact
  package/version/tarball/tag operands, while its required `action-id`,
  `mutable-resource-keys`, and `serialization-projection` remain present.
  Missing profile resolution, implicit defaults, or effective-runtime mismatch
  blocks before marker.
- Prove the one standard npm invocation contains `--ignore-scripts` and
  `--fetch-retries=0`; a retryable synthetic response produces exactly one
  outbound mutating request. Prove no separate tag action, `latest`, mutation
  retry, compensation, delete, or restore.
- Prove authoritative exact-version readback is required before `published`
  and tag readback is diagnostic only.
- Prove every Result directly binds Marker and only new command/readback/
  mutation/diagnostic facts; pre-marker failure emits no Result; conflict,
  non-success, and ambiguity remain failed even if exact readback appears;
  marker without Result is possibly mutated; and no Receipt or secret enters
  records/logs.

### 18.9 Finalization, retry, and concurrency

- Cover valid exact-satisfied proof, zero-action Snapshot missing fresh proof,
  published Result, failed `not-mutated` Result, failed
  `possibly-mutated`/`mutated` Result, marker with null Result, green Publisher
  without Result, malformed scalar transport, contradictory lineage, and
  Finalizer transport loss.
- Prove Publisher `skipped`, or Publisher `failure`/`cancelled` with exact
  platform-derived publication-step outcome `skipped`, can become
  `failed-before-publication` only with exactly one admitted pre-marker
  predecessor, no valid zero-action Snapshot, a null publication terminal
  reference, and no contradictory lineage. Prove non-blocking
  `exact-satisfied` or `absent`
  Observation followed by Snapshot materialization/transport failure forms no
  Outcome and cannot fall back to Qualification. Missing/script-produced facts
  remain `unknown`.
- Prove Outcome contains only disposition, `possibly_mutated`, and one tagged
  direct predecessor selected by the defined priority. A Result controls
  publication outcome despite later Publisher failure/cancellation; green job
  status never substitutes for Result.
- Prove Finalizer performs no history/destination invention, no formal
  Reconciliation workflow or record is required, and a new dispatch adopts no
  prior authority.
- Prove Execution identity/group and publisher resource/group inputs, both `cancel-in-progress: false`, complete version/tag keys, conservative same-package serialization, and block on incomplete closure.

### 18.10 Disabled integration gate

The implementation PR must pass targeted Python/workflow tests, the full v3
suite, and root HK while protected Governance remains `live_enabled: false`.
It performs no dispatch, registry mutation, Environment change, access change,
or acceptance-only operation. Repository search proves replacement runtime has
no reference to `workflow-delivery-v3-buddy-github-packages`.

Before activation, separately authorized native acceptance must pass every
section 18.6 scenario for the exact implemented profile, including the bounded
tag race and deleted/restorable same-version sequence. Its package-admin
credential may mutate only the pre-approved disposable package/version and must
restore the original tombstoned object; it cannot touch the dedicated smoke
coordinate. Authenticated repository retention readback must prove
`days >= 45`, and fresh protected Governance must bind the passing acceptance
generation while remaining disabled until the Activation PR.

## 19. Implementation and Deployment Order

1. Merge normative design and this replacement LLD.
2. Implement records, static-reference policy, strict Governance v2 parser,
   Destination Operation Profile, active-state Observation, zero/one-action
   Snapshot, Approval/Authorization/marker/Result lineage, current-DAG
   Finalizer, workflow topology, and semantic tests. Atomically migrate the
   protected document to disabled v2; v1 is never an admission alias.
3. Validate, review, and merge the implementation with
   `live_enabled: false`. Standard `npm publish --tag ... --fetch-retries=0`
   exists only as the exact profile that still requires native acceptance
   before any action-bearing admission.
4. Prove no workflow, executable source, schema, policy, formatter, validator, or test treats `workflow-delivery-v3-buddy-github-packages` as an input or authority. Current-state and migration text may still name the resource solely to inventory and remove it safely. Prove every retained dispatchable ref either implements the one-Environment contract or rejects the replacement Governance schema before any Environment job or deployment; retain `origin/workflow-delivery-v3-platform-orphan-exception@4af8819bed7c19d3231570351b278a24b268dab8` as a negative compatibility fixture if that ref still exists.
5. Obtain separate authorization before deleting that obsolete Environment.
6. Under separate authorization, execute the complete section 18.6 native
   acceptance suite against the pre-approved disposable package with the exact
   implemented profile. Capture the bounded tag race, sequential
   deleted/restorable same-version failures and empty deltas, restoration
   readback, API/contract revisions, suite version, profile digest, verdict,
   capture time, and canonical evidence digest.
7. Perform fresh authenticated native readback of Approval Environment,
   broader variables, access, package-principal facts, and repository Actions
   artifact retention; require `days >= 45`.
8. Prepare the exact refreshed Governance v2 attestation from that evidence
   without copying acceptance-only credentials or detailed tombstone facts and
   without merging a separate preparation change.
9. Create one small Activation PR that applies the refreshed attestation and
   changes `live_enabled: false` to `true`.
10. Merge through protected review and perform authenticated post-merge
    readback, including repository retention.
11. Dispatch exactly once from then-current protected `main`, then verify the
    returned run, current-run records and Outcome, and authoritative destination
    readback.

There is no Preparation PR, main freeze, preselected activation SHA, activation tag, implementation-time dispatch, or blind retry.

### 19.1 First proving dispatch

Use the REST workflow-dispatch API with `X-GitHub-Api-Version: 2026-03-10`,
workflow `workflow-delivery-v3-buddy-smoke.yml`, and JSON body
`{"ref":"main","return_run_details":true}`. Require HTTP `200` with
schema-valid `workflow_run_id`, `run_url`, and `html_url`.

Read back that exact run and verify repository, workflow, actor `hcoona`, event `workflow_dispatch`, `refs/heads/main`, actual head SHA equal to the just-recorded protected-main SHA, workflow/control revision equal to it, and run attempt one.

A lost, malformed, or ambiguous response triggers read-only operator
investigation and native run lookup, never blind redispatch. For the one
returned run, verify current-run artifact references and canonical records,
exactly one authoritative Attempt Outcome with disposition `exact-satisfied` or
`published`, `possibly_mutated: false`, and authoritative destination readback
matching the frozen exact version bytes, digests, and witness. Any unexplained
mutation uncertainty means the proving objective is not complete. Later normal
Buddy runs may again select arbitrary same-repository refs whose selected-
revision control strictly admits the active Governance schema.

## 20. Deferred and Non-Goals

Outside this slice are Official Live npmjs trust; additional
destinations/actions; generic Environment profiles; independent publisher
infrastructure; cryptographic separation from `hcoona`; package-specific
repository-token narrowing; universal package-grant enumeration;
nondeterministic sealed-artifact resume; cross-Attempt artifact reuse; rerun
recovery; history-derived admission; approver recovery; a first-slice formal
Reconciliation workflow or record; encoded/split/runtime-download analysis;
arbitrary external/novel layouts; Tree-sitter/dataflow interpretation;
normal-runtime tag-only/delete/restore/visibility/permission/admin actions;
remediation redesign; simulation rerun changes; finalization watchdogs;
unauthorized obsolete-Environment deletion; activation/dispatch/package
mutation through this document; and release pipelines for other projects.

The separately authorized native acceptance suite is the sole exception for
disposable-package delete/restore operations. It grants no runtime capability.

These items are bounded unsupported capabilities, not unresolved first-slice decisions.
