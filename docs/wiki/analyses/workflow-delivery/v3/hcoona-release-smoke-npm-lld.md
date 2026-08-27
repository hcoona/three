# Workflow Delivery v3 `hcoona-release-smoke-npm` First-Slice LLD

## Status

Architecture version: **v3**.

Review state: **Approved for implementation on 2026-08-06**.

This brief low-level design realizes the confirmed
[requirements](./requirements.md),
[HLD](./high-level-design.md), and MLDs for the first vertical slice. It defines
enough concrete structure to begin implementation without
turning the LLD into an implementation transcript.

**Development gate:** satisfied by explicit user approval on 2026-08-06.
Implementation must follow the dependency-ordered commits and activation gates
in this LLD.

## Scope and Boundaries

The slice contains exactly:

- one Node Project Node:
  `@hcoona/hcoona-release-smoke-npm`;
- one Release Unit: `hcoona-release-smoke-npm`;
- one artifact variant: `npm-package`;
- one output: one npm `.tgz`;
- shadow pull-request incremental CI and manually dispatched
  `slice-validation`;
- live Buddy publication to GitHub Packages; and
- Official npmjs release simulation only.

CI always requires root HK source-tree conformance. When the Project Node or
Release Unit is selected, CI separately requires project build, project tests,
and npm artifact build/pack. There is no initial advisory lane.

The current `hk.pkl` does not run tests for the not-yet-created v3 control
package. The implementation commit that creates that package must add an
HK-internal pytest step. It is path-triggered by the v3 package, catalogs,
policies, descriptors, workflows, and direct workspace/lock/HK inputs, and runs
unconditionally in manual `slice-validation`. It remains inside root
`SourceTreeConformance`, not a separate obligation, Evidence record, or job.

Both first-slice CI modes are non-authoritative during v1/v3 coexistence. Manual
slice validation covers the complete `hcoona-release-smoke-npm` slice only and
is never named or projected as canonical repository-wide full validation. The
pull-request check remains shadow-only and does not replace v1 required CI.
Canonical explicit or scheduled full validation is deferred until every active
Project Node, Release Unit, and repository obligation is modeled.

Before coexistence begins, the first implementation pull request uses the
bounded bootstrap projection defined by the CI MLD. The workflow captures the
unchanged `ci finalize` exit code. On success it returns success directly. On a
pull-request failure only, a separate `ci project-bootstrap-shadow` command
re-admits the canonical Plan, Decision, and summary; binds the exact event base,
head, tested merge, and request number; probes the exact base commit for the
canonical workflow path; and applies the closed bootstrap predicate. An
eligible Decision remains `failure` / `incomplete-model-plan` and receives an
explicit GitHub summary note while the enclosing non-authoritative check
concludes successfully. Any ineligible Decision or projection error preserves
the nonzero conclusion. Manual validation returns the Finalizer exit unchanged,
and the existing no-Decision contract-failure step remains terminal.

Live Buddy publishes exactly:

```text
@hcoona/hcoona-release-smoke-npm@<frozen npmPackageVersion>
```

to `https://npm.pkg.github.com`. The package is disposable and must not appear
in normal developer, CI, or production dependency graphs. A permanent
repository-wide HK dependency-policy gate enforces that boundary.

Official simulation targets `https://registry.npmjs.org`, may run from any
same-repository selected ref, and performs Repository Model compilation,
planning, build, qualification, observation, and hypothetical action reporting.
It creates no live Product Identity, Release Execution, Attempt, Authorization
Record, Capability, Receipt, or mutation.

### Non-Goals

This slice does not implement:

- live Official publication;
- Buddy publication outside the named GitHub Packages package;
- legacy Buddy compatibility or support for former v1 Buddy projects after
  cutover;
- GitHub Release, provenance publication, signing, or notarization;
- another ecosystem, Project Node, Release Unit, variant, or artifact;
- automatic initiation, promotion, rollback, deletion, restore, or admin
  operations;
- a permanent Release database, reservation ledger, binding index, tag witness,
  or application-level destination lock;
- `Re-run failed jobs` recovery;
- v2 projects, profiles, Plans, proofs, reports, or control-plane imports; or
- canonical repository-wide explicit or scheduled full CI validation or v3
  Ruleset required-check cutover.

### Assumptions and Unsupported Boundaries

- The selected workflow ref and `github.sha` are the target; no independent
  target input exists.
- NBGV is the sole canonical and published version authority.
- GitHub Packages must prove durable readback and atomic non-overwriting npm
  version creation through acceptance tests. Until then live Buddy is disabled.
- The initial GitHub Packages operation is create-only. It may be classified as
  atomic create-or-exact only if platform acceptance proves one mutation call
  can accept concurrently established exact bytes without overwrite.
- A 403, malformed response, unavailable tarball, ambiguous owner, or
  unverifiable digest is not absence.
- Deletion and restore remain Break-Glass reconciliation concerns outside the
  ordinary workflows.

## Repository Decomposition

The control implementation uses Python 3.13 because the repository already
uses UV workspace Python packages for workflow contracts, planners, build
execution, proof, and publication mechanisms. Node remains the product and npm
tooling language. This choice does not import v2 domain authority.

New v3 code is one initially cohesive UV workspace package:

```text
src/public/lib/three-workflow-delivery-v3/
  pyproject.toml
  src/three_workflow_delivery_v3/
    cli.py
    canonical.py
    diagnostics.py
    records/
      bindings.py
      artifacts.py
      ci.py
      release.py
    repository/
      descriptors.py
      node_provider.py
      nbgv_provider.py
      compiler.py
    ci/
      planner.py
      evidence.py
      finalizer.py
    release/
      identity.py
      eligibility.py
      history.py
      planner.py
      qualification.py
      observation.py
      authorization.py
      finalizer.py
    adapters/
      node_build.py
      node_quality.py
      npm_provenance.py
    destinations/
      github_packages_npm.py
      npmjs.py
    platform/
      actions_artifacts.py
      github_api.py
      github_history.py
      npm_registry.py
  tests/
    contracts/
    repository/
    ci/
    release/
    adapters/
    destinations/
    integration/
```

The package exposes one CLI, `three-workflow-delivery-v3`, with context-owned
subcommands such as `repository compile`, `ci plan`, `ci finalize`,
`release plan-qualification`, `release materialize-publication`,
`release finalize`, `npm observe`, and `npm publish`.

Static implementation registration lives in Python catalogs in the same
package. YAML authoring selects allowlisted logical IDs only; it cannot supply
commands, module paths, or executable packages.

No module imports `three_workflow_release_*` or reads `three.release.yml`.
Revalidated algorithms may be ported into the new namespace with v3 tests and
contracts. The retained tree contains neither the v2 control projects nor
legacy descriptors; v2 remains available only at its immutable archive commit.
In v1, Official and CI behavior remains unchanged, while legacy Buddy
workflows, Buddy-specific tests and matrices, and Buddy documentation are
explicitly excluded from that preservation and are retired or rewritten by the
direct cutover.

The product receives only the first-slice test and script changes later required
by the approved implementation:

```text
src/public/lib/hcoona-release-smoke-npm/
  workflow-delivery.release-unit.yml
  workflow-delivery.quality.yml
  test/smoke.test.js
```

### CODEOWNERS Implementation

The implementation extends `.github/CODEOWNERS` with final-match patterns owned
by `@hcoona` for:

```text
/src/public/lib/three-workflow-delivery-v3/**
/eng/workflow-delivery/v3/**
/src/**/workflow-delivery.release-unit.yml
/src/**/workflow-delivery.quality.yml
/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json
/hk.pkl
/src/private/lib/hk/**
/pyproject.toml
/uv.lock
```

Existing ownership for `/.github/workflows/**`, `/.github/actions/**`,
`/eng/scripts/**`, and `/.github/CODEOWNERS` remains. Contract tests evaluate
GitHub final-match semantics and require `@hcoona` for every governed file,
including the exact protected Governance document above, discover every current
and newly added Release Unit or quality descriptor, and fail on a missing or
later overridden pattern. These merge-time controls do not add CODEOWNERS
eligibility to arbitrary-ref first-slice Buddy runtime.

## Authoring and Static Catalogs

### Release Unit Descriptor

The fixed basename is `workflow-delivery.release-unit.yml`. The slice descriptor
is:

```yaml
schema: workflow-delivery/v3/release-unit
release-unit: hcoona-release-smoke-npm
builds:
    - id: npm-package
      definition: node/npm-package-v1
      entry-point: package.json
      outputs:
          - id: npm-tarball
            role: primary-package
            kind: npm-tarball
```

Project membership, package name, workspace dependencies, and version are
Provider facts, not duplicated authoring.

### Project Quality Selection

The cascading basename is `workflow-delivery.quality.yml`:

```yaml
schema: workflow-delivery/v3/quality-selection
ecosystems:
    node:
        preset: node/hcoona-release-smoke-npm-v1
```

The static preset expands to:

| Capability              | Disposition | Concrete target |
| ----------------------- | ----------- | --------------- |
| `node/project-build-v1` | required    | Project Node    |
| `node/project-test-v1`  | required    | Project Node    |

The CI Planner independently adds:

- required root `repository/source-tree-conformance-v1`; and
- required `node/npm-artifact-v1` for every selected Release Unit variant.

The artifact obligation is not deduplicated with project build even though both
may invoke related mechanics. The first slice has no advisory definition.

### Release Policy

The exact policy file is:

```text
eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml
```

Its initial content is:

```yaml
schema: workflow-delivery/v3/release-policy
release-unit: hcoona-release-smoke-npm
governance:
    attestation:
        repository: hcoona/three
        ref: refs/heads/main
        path: .github/workflow-delivery/governance/hcoona-release-smoke-npm.json
        max-age-days: 90
channels:
    buddy:
        quality:
            - node/project-test-v1
            - node/npm-artifact-contents-v1
            - node/npm-install-import-v1
        projections:
            - destination: npm/github-packages-hcoona-three-v1
              artifact: npm-tarball
              package: '@hcoona/hcoona-release-smoke-npm'
    official:
        quality:
            - node/project-test-v1
            - node/npm-artifact-contents-v1
            - node/npm-install-import-v1
        projections:
            - destination: npm/npmjs-public-v1
              artifact: npm-tarball
              package: '@hcoona/hcoona-release-smoke-npm'
```

This is channel policy, not a v2 `profile`. The static catalogs additionally
define the named Build, Quality, Destination, execution-class, and capability
contracts. The `governance` object is the immutable first-slice source contract;
schema validation requires these exact repository, fully qualified ref, path,
and maximum-age values. No repository variable participates in live
enablement.

### Governance TCB Attestation

The protected-ref, non-executable file is:

```text
.github/workflow-delivery/governance/hcoona-release-smoke-npm.json
```

The Release policy fixes repository `hcoona/three`, protected ref
`refs/heads/main`, and that exact path. Its canonical schema contains the
required top-level boolean field `live_enabled`, explicit accepted
Write/Maintain/Admin writer inventory, explicit
package/repository/Manage Actions access inventory or human-inspection evidence
digest, policy and package binding, issuer, inspection time, expiry no more than
90 days later, and acknowledged API and staleness limitations. It contains no
command, module, workflow, or executable policy. A new protected merge replaces
it after human re-attestation. The payload need not self-reference Git
provenance because eligibility and freshness records bind source provenance
externally. This protected document is the authoritative normal-flow
live-enable source. It grants no Capability by itself.

Commit 10 installs the canonical disabled attestation with issuer and sole
accepted Admin writer `hcoona`, repository access `admin`, package access
`write`, Manage Actions `allowed`, inspection time
`2026-08-14T17:19:12Z`, and expiry `2026-11-12T17:19:12Z`. Its limitations
state that GitHub APIs are not a complete universal authority enumeration and
that reviewer recovery may depend on retained platform data.

Post-activation re-attestation is always a protected human procedure:

1. promptly merge `live_enabled: false` after a relevant writer, role,
   repository/package grant, or Manage Actions change;
2. inspect the current accepted writers and access facts without claiming API
   completeness;
3. replace the canonical document with a fresh inspection time, expiry no more
   than 90 days later, exact inventories or approved evidence digest, and
   updated limitations;
4. keep live disabled until all normal activation gates are again satisfied;
   and
5. use a separate protected approval to restore `live_enabled: true`.

Optional reviewer inspection never substitutes for this procedure, grants
Capability, or enables live.

## Canonical Records and Bindings

Records are strict UTF-8 JSON with duplicate-key and unknown-field rejection.
Canonical bytes use RFC 8785 JSON Canonicalization Scheme; digests use
`sha256:<lowercase-hex>`. This is a shared mechanism, not a universal domain
record envelope.

Each context-owned schema embeds only the applicable common binding values:

| Binding set | Fields                                                                                                                      |
| ----------- | --------------------------------------------------------------------------------------------------------------------------- |
| Request     | repository, workflow path/ref/SHA, request ID, actor, run ID, run attempt                                                   |
| Purpose     | `ci-pr-slice-shadow`, `slice-validation`, `live-release`, `release-simulation`, or Governance-only `destination-acceptance` |
| Target      | full commit SHA and selected Git ref                                                                                        |
| Producer    | repository, workflow path/SHA, job ID/name, run ID, run attempt                                                             |
| Control     | exact selected target/control commit and catalog digest                                                                     |
| Subject     | context-specific candidate, Execution, Attempt, Simulation, obligation, projection, or action                               |
| Integrity   | schema ID, canonical payload digest, referenced record digests                                                              |

Artifact records additionally carry immutable Actions artifact ID, artifact
name as a non-authoritative index, byte size, SHA-256, SHA-512 for npm
tarballs, media kind, logical output role, producer, target, purpose, and
provenance digest.

Every physical Actions artifact name is deterministic and unique across the
complete workflow run with `overwrite: false`. The first slice uses
`wdv3-<purpose>-<logical-role>-ra<github.run_attempt>-<deterministic-digest>`;
an equivalent name is valid only when `github.run_attempt` is part of the
deterministic hash preimage. Uploads capture and pass artifact ID, digest, and
URL. Every consumer downloads only an explicit artifact ID and verifies
returned name metadata, producer, `github.run_id`, `github.run_attempt`, and
digest. A prior-attempt ID, lookup by name, name fallback, or latest-artifact
selection is rejected for current authority. History-only admission instead
binds only artifact ID/digest, source workflow run ID, head SHA, payload
integrity, and platform metadata exposed by the API, with job/run-attempt phase
facts queried separately.

### Identity Table

| Identity                        | Exact fields                                                                                                    |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| CI request                      | repository + workflow path + `github.run_id`                                                                    |
| CI candidate                    | event kind + tested candidate SHA + authoritative comparison identity                                           |
| Release request/Intent          | repository + workflow path + `github.run_id`; binds actor, channel, mode, Release Unit, selected ref, target    |
| Official Product Identity       | `official` + Release Unit + canonical NBGV version                                                              |
| Official Execution Identity     | Official Product Identity + target                                                                              |
| Buddy Execution Identity        | `buddy` + Release Unit + target                                                                                 |
| Release Attempt                 | Release Execution Identity + `github.run_id` + `github.run_attempt`                                             |
| Simulation Identity             | `release-simulation` + request ID + `github.run_id` + `github.run_attempt`                                      |
| External Package Coordinate     | channel + destination ID + package name + frozen native version                                                 |
| Buddy mutable-resource keys     | canonical keys for the exact External Package Coordinate and destination/package/target-specific dist-tag       |
| GitHub Packages lock projection | physical destination ID + normalized npm package name; excludes channel, version, target, tag, and Release Unit |

Request ID intentionally excludes `github.run_attempt`; **Re-run all jobs**
preserves the request and creates a new Attempt or simulation pass.

### Contract Inventory

| Record                               | Required identity and binding fields                                                                                                                                                                                                                                                                                                 |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Provider Request Manifest            | request, purpose, run/attempt, target, producer/control, catalog digest, exact Provider requests and digests                                                                                                                                                                                                                         |
| Fact Bundle                          | manifest/request entry, purpose, run/attempt, target, Provider implementation/toolchain, normalized result digest, producer/control                                                                                                                                                                                                  |
| Repository Model Snapshot            | request, purpose, run/attempt, target, producer/control, descriptors, Project Node/graph, Build Definitions, Release Unit/variant/output closure, canonical/native NBGV facts                                                                                                                                                        |
| CI Qualification Snapshot            | CI candidate, shadow or `slice-validation` purpose, Repository Model digest, root HK definition, slice scope, complete obligation DAG and expected Evidence                                                                                                                                                                          |
| CI Evidence                          | CI Snapshot, obligation ID/request digest, producer, raw result, artifact/provenance references, disposition outcome                                                                                                                                                                                                                 |
| CI Slice Decision                    | CI Snapshot, admitted Evidence digests, every slice obligation disposition, structured explanation, terminal result, and explicit `non-authoritative` authority marker                                                                                                                                                               |
| Release Intent                       | request, actor, selected ref, target, channel, live mode, Release Unit                                                                                                                                                                                                                                                               |
| Governance TCB Attestation           | fixed-source protected-ref non-executable schema, required boolean `live_enabled`, explicit accepted writer inventory, package/repository/Manage Actions access inventory or evidence digest, policy/package, issuer, inspection time, expiry no later than 90 days, limitations, canonical digest, provenance                       |
| Live Eligibility Decision            | live purpose/request/run/attempt/ref/SHA, Repository Model, producer/control, consumer-policy/catalog, scanned surfaces/exceptions, admitted `live_enabled` value, exact attestation source contract and resolved commit/path/blob OID/content SHA-256, result; immutable artifact ID/digest                                         |
| Historical Execution Record          | `execution-history` selected by caller; authoritative artifact ID/digest, source workflow run ID, head SHA, payload integrity and exposed platform metadata; separately queried Jobs/Run phase facts; payload producer/run-attempt/reusable-workflow claims diagnostic only                                                          |
| Execution History Admission Snapshot | current request/run/run attempt, Execution Identity, exhaustive REST/GraphQL query and pagination basis, sorted admitted artifact IDs/digests/source run IDs/head SHAs and separately queried phase facts; history-only authority marker                                                                                             |
| Release Attempt binding              | Intent/request, Execution Identity, run/attempt, request-local Repository Model digest, Live Eligibility Decision ID/digest, complete attestation provenance, Execution History Admission Snapshot ID/digest                                                                                                                         |
| Simulation binding                   | request, purpose, run/attempt, target/channel/Release Unit, Simulation Identity, simulation Repository Model digest                                                                                                                                                                                                                  |
| Qualification Snapshot               | Attempt or Simulation, Repository Model digest, target/channel/unit/version facts, build requests, quality DAG, destination projections/coordinates, Adapter/version bindings, potential action schema, capability policy, deterministic key derivation basis                                                                        |
| Build Request/Result                 | Snapshot, target, Build Definition/request digests, exact `npmPackageVersion`, frozen canonical Package Target Witness bytes/digest, inputs/toolchain, source intended-file allowlist, staged manifest allowlist, expected/actual output manifest, producer                                                                          |
| Package Target Witness               | frozen canonical input packed at exact tar entry `package/workflow-delivery/provenance.json`: target, Release Unit, canonical/native NBGV facts, Build Definition/catalog/control digests, purpose, schema; excludes run/Attempt IDs                                                                                                 |
| Qualification Evidence/Decision      | Snapshot, obligation, mechanical result and artifacts; Decision admits every required Evidence digest                                                                                                                                                                                                                                |
| Observation Record                   | Attempt or Simulation, logical projection, immutable desired-state basis, canonical request/response facts, owner/coordinate/tarball digests, exact dist-tag mapping, classification                                                                                                                                                 |
| Publication Snapshot                 | Attempt, Qualification Snapshot/Decision, exact artifact/provenance, desired and observed state, actual action DAG/inputs, complete key sets, conservative lock projections/groups, capabilities, Receipt contracts                                                                                                                  |
| Authorization Record                 | live Attempt, Publication Snapshot digest, immutable reviewer-summary artifact ID/digest, run/attempt, approval job and Environment, channel, approval completion                                                                                                                                                                    |
| Approval Outcome Evidence            | reserved generic schema requiring documented exact current-attempt/job/Snapshot denial proof; not emitted by this first-slice GitHub Environment flow                                                                                                                                                                                |
| Capability Admission Decision        | Attempt, Authorization, Snapshot, summary, actions/artifacts/resource keys/group manifest, Live Eligibility Decision, fresh attestation `live_enabled` and provenance/content/expiry via `contents: read`, producer/run, result and diagnostics; no credential                                                                       |
| Platform run/job conclusion          | platform-owned run/attempt conclusion plus retained approval/capability phase state; no separate uploaded record is required, and it proves no side effect only when no capability group started                                                                                                                                     |
| Publication Action                   | Publication Snapshot, projection, operation, exact artifact, prerequisites, complete key set, conservative GitHub lock projection/group, capability group, expected result/Receipt                                                                                                                                                   |
| Action Result                        | action, run/attempt, producer, enforced lock group, typed response, mutation disposition, diagnostic and Receipt reference                                                                                                                                                                                                           |
| Capability-Group Result Bundle       | Attempt, Publication Snapshot, run/attempt, group ID, exact planned action IDs, per-action outcome/response/Receipt and diagnostic references, completion state, producer/control                                                                                                                                                    |
| Receipt                              | compound action, complete frozen coordinate-plus-tag key set, enforced destination/package lock projection/group, artifact SHA-256/SHA-512, version create/exact-race result, dist-tag mapping, destination response identity, producer/run/attempt                                                                                  |
| Attempt Outcome                      | Attempt, exact Qualification Decision, optional Publication Snapshot and later records, uncertainty flag, terminal phase and next action; qualification or publication-preparation interruption may omit the Publication Snapshot, and the Outcome may be absent when platform termination prevents Finalizer execution              |
| Simulation Outcome                   | Simulation Identity, Snapshot/Decision, observations, hypothetical actions/keys/capabilities, terminal result; explicitly no live records                                                                                                                                                                                            |
| Governance Acceptance Evidence       | `destination-acceptance` purpose, protected workflow/ref/SHA, hard-bound target and fixed coordinate, confirmation digest, Environment/reviewer, every dependency result, available probe actions/responses/digests/diagnostics, mutation disposition including incomplete/unknown, producer/run, explicit no-Release-lineage marker |

The trusted caller selects `current-authority` or `execution-history`; payload
content cannot choose the mode. `current-authority` requires exact current
purpose, request, run, run attempt, Attempt, target, producer, control, artifact
ID, and digest and rejects prior attempts. `execution-history` is accepted only
inside pre-Attempt live `admit`. Its authoritative attribution binds artifact
ID/digest, source workflow run ID, head SHA, payload integrity, and platform
metadata actually exposed. Jobs/Run APIs separately establish run-attempt, job,
conclusion, and phase facts. Producer, exact run attempt, reusable-workflow,
purpose, and control claims inside history payloads are diagnostic
self-assertions. History cannot satisfy current Evidence, authorization,
artifacts, Receipts, or outcomes. Strict historical workflow/attempt provenance
is unsupported without separately approved Artifact Attestations or OIDC; this
slice adds no `id-token`.

## Provider and Adapter Boundaries

### Node and NBGV Provider

The unprivileged target-evaluation Provider:

1. checks out the exact target SHA with credentials disabled and
   `fetch-depth: 0`, or an equivalent full-history mechanism that fetches the
   complete ancestry and tags NBGV needs for version height;
2. verifies that `HEAD` remains the exact target and fails before NBGV when the
   repository is shallow, required ancestry or tags are incomplete, or the
   full-history guarantee cannot be proved;
3. runs `pnpm install --frozen-lockfile --ignore-scripts`;
4. obtains workspace/package facts through PNPM JSON metadata;
5. resolves the effective `version.json` lineage;
6. invokes NBGV once for target-bound canonical and native JSON facts;
7. requires `npmPackageVersion`; and
8. emits one Fact Bundle.

It does not run package lifecycle, build, test, pack, or publish scripts.
Repository Model compilation freezes the resulting canonical version and
`npmPackageVersion`.

The same `--ignore-scripts` rule applies to tool preparation. Purposeful project
build, test, and pack operations execute only through their closed invocations
in the target-execution zone.

### Node Build Adapter

The Build Adapter receives the frozen `npmPackageVersion`; it never invokes
NBGV or accepts a fallback version.

It:

1. creates an isolated staging tree outside the checkout;
2. copies only declared project/build inputs;
3. writes the frozen version into the staged `package.json`;
4. writes the frozen canonical Package Target Witness input to staged
   `workflow-delivery/provenance.json`, binding target commit, Release Unit,
   canonical/native NBGV facts, Build Definition, catalog/control digests,
   purpose, and schema, with no run or Attempt IDs;
5. deterministically updates and verifies the staged `package.json` `files`
   allowlist so it preserves every existing intended package entry and includes
   exact entry `workflow-delivery/provenance.json`, without mutating the source
   working-tree manifest;
6. invokes the deterministic build operation directly rather than the current
   NBGV-stamping package script;
7. runs `npm pack --ignore-scripts` into an empty output directory;
8. inspects the packed tarball and verifies tarball basename, exact entry
   allowlist, packed `package/package.json` name, frozen version, staged `files`
   allowlist, exact witness path
   `package/workflow-delivery/provenance.json`, byte-for-byte canonical witness
   equality with the frozen input, absence of undeclared outputs, and
   lifecycle-script manifest;
9. computes SHA-256 and SHA-512; and
10. emits one immutable tarball plus manifest and provenance.

It never mutates or restores the source checkout. `SOURCE_DATE_EPOCH`, locale,
timezone, Node, PNPM, npm, and Adapter versions are frozen inputs.

### Quality Adapters

- `node/project-build-v1` builds from isolated staged inputs.
- `node/project-test-v1` runs the new Node test suite without publication
  credentials.
- `node/npm-artifact-contents-v1` opens the packed tarball, validates its exact
  entry allowlist, packed manifest `files` allowlist, package identity, frozen
  version, and lifecycle scripts, and fails unless exact entry
  `package/workflow-delivery/provenance.json` contains canonical bytes identical
  to the frozen Package Target Witness input.
- `node/npm-install-import-v1` installs the tarball with scripts disabled into
  an empty consumer project, imports `smokeMessage`, and verifies the installed
  target witness against the same frozen canonical input.
- Root HK remains one opaque repository-defined invocation.

Live Buddy and Official simulation may batch the two tarball-dependent npm
Adapters into one physical `npm-artifact-qualification` job after
`build-tarball`. They remain distinct obligation identities and emit two
separate Evidence records. Qualification finalization requires both.

### GitHub Packages npm Destination Adapter

Observation uses minimal `packages: read` and:

1. requests the exact package/version metadata;
2. distinguishes authoritative not-found from denial or transport failure;
3. verifies package scope, owner/repository association where exposed, version,
   and tarball URL;
4. downloads the remote tarball bytes;
5. computes remote SHA-512 and compares it with the qualified tarball SHA-512;
6. extracts and validates `workflow-delivery/provenance.json` against the
   snapshot-bound target, Release Unit, NBGV, Build Definition, catalog/control,
   purpose, and schema facts; and
7. reads the exact dist-tag
   `buddy-sha-<40-lowercase-target-sha>` and records its version mapping; and
8. records `dist.integrity` as auxiliary corroboration only.

Exact state requires the complete coordinate, expected ownership, local
qualified witness matching the extracted remote in-package witness, a
byte-identical downloaded tarball, and the exact target-specific dist-tag mapped
to the frozen native version. The tag is routing, not provenance. A local
sidecar or matching `dist.integrity` without downloadable matching bytes and
witness is `unprovable`. A different target witness is `conflicting`, including
when package name/version or other bytes appear equal. Missing tag state is
`partial`, a mapping to another version is `conflicting`, and unreadable tag
state is `unknown` or `unprovable`.

Publication uses:

```text
npm publish <qualified-tarball> \
  --registry https://npm.pkg.github.com \
  --tag buddy-sha-<40-lowercase-target-sha> \
  --ignore-scripts
```

with only the job's short-lived `GITHUB_TOKEN`. It never relies on implicit
`latest` or a shared moving Buddy tag. Publication is one compound action that
binds the External Package Coordinate and the
destination/package/target-specific-tag mutable resources. Its Receipt records
the version-creation or exact-race result and the tag-to-version mapping. Normal
flow exposes no separate dist-tag mutation; an exact version with absent or
mismatched tag requires reconciliation. The Adapter generates temporary
registry configuration outside the checkout and never accepts PAT, OIDC, force,
unpublish, overwrite, or delete behavior.

Classification is:

- pre-observed `absent`: one create-only action;
- pre-observed `exact-satisfied`: no action and no package-write Capability;
- `partial`, `conflicting`, `unknown`, or `unprovable`: no action and
  reconciliation-required;
- create conflict after `absent`: failed action, then whole-release replay;
- atomic concurrently-created exact success: allowed only after acceptance
  proves the destination operation supplies that result without mutation.

### npmjs Observation Adapter

Official simulation uses the public npm registry endpoint, downloads any exact
version tarball, and applies the same SHA-512 comparison and classification. It
emits hypothetical actions and requirements only. It never creates npm
credentials, provenance, authorization, Receipt, or mutation.

For this first slice, the only admitted npmjs coordinate is exactly
`@hcoona/hcoona-release-smoke-npm` at the frozen native NBGV version. The exact
scoped coordinate is the expected ownership fact: the Adapter relies on npm's
scope namespace abstraction and does not require mutable `maintainers`
metadata. This rule is first-slice-specific and does not classify other scoped
or unscoped repository packages.

Official simulation closes observation results as follows:

- `absent` succeeds and reports one hypothetical create action;
- `exact-satisfied` succeeds and reports no action;
- `unknown` is incomplete and replayable with next action
  `rerun-simulation`;
- `unprovable` is incomplete with next action
  `fix-observation-capability-and-rerun`; and
- `partial` or `conflicting` fails with next action
  `reconcile-destination-state`.

## Workflow Topology

All workflows run control code from the exact selected revision. Every external
action, including GitHub-maintained `actions/*`, uses a Renovate-managed full
40-character commit pin with a version comment:
`uses: owner/action@<40-char-sha> # vX.Y.Z`. Names such as
`actions/upload-artifact` in this LLD identify the action API only. Renovate
selects the current Node-24-compatible major and full commit pin; this LLD fixes
neither a major tag nor a transient SHA. First-slice Release control, artifact,
and outcome artifacts use 45-day retention. CI shadow artifacts also use 45
days for one consistent initial setting.

### CI

File: `.github/workflows/workflow-delivery-v3-ci.yml`

Events:

- `pull_request`: shadow incremental mode against the GitHub tested merge
  candidate;
- `workflow_dispatch`: purpose `slice-validation`; no scope-selection input.

Workflow permissions are `contents: read`; no Actions-history, Environment,
`packages`, secret, or `id-token` permission exists.

Job DAG:

```text
request
  -> discover-node
  -> plan
      +-> root-hk
      +-> project-build
      +-> project-test
      +-> npm-artifact-build
  -> required-finalizer (always)
```

The stable non-authoritative shadow job name is:

```text
Workflow Delivery v3 / hcoona-release-smoke-npm (shadow)
```

PR concurrency is `wdv3-ci-pr-<pull-request-number>` with
`cancel-in-progress: true`. Manual slice concurrency is
`wdv3-ci-slice-validation-<selected-target-sha>` with
`cancel-in-progress: true`.

Static executor jobs always emit a lane result. If a lane has no planned
obligation, it emits `empty` with the Plan digest and produces no Evidence.
Empty affected-system lanes are valid; root HK is never empty. The Finalizer
runs with `always()`, admits only planned Evidence, marks missing selected work
incomplete, and writes an explicitly non-authoritative slice summary. Neither
event creates a Ruleset required check or canonical repository-wide CI Final
Decision.

Inside `root-hk`, the new `v3-control-pytest` HK step runs for:

- `src/public/lib/three-workflow-delivery-v3/**`;
- additions, deletions, renames, or modifications matching
  `src/**/workflow-delivery.release-unit.yml`;
- additions, deletions, renames, or modifications matching
  `src/**/workflow-delivery.quality.yml`;
- `eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml`;
- `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`;
- every v3 control, catalog, and test path;
- every governed v3 workflow, action, and directly invoked script;
- `.github/CODEOWNERS`;
- root `pyproject.toml`, `uv.lock`, and other direct Python workspace inputs;
  and
- `hk.pkl`, imported HK configuration modules, and directly invoked helpers such
  as `eng/scripts/hk_exec.py`.

Manual `slice-validation` passes the HK full/slice signal that runs this step
unconditionally. The step executes the package pytest suite but remains
internal to the one root-HK `SourceTreeConformance` result. Unrelated
first-slice product source alone does not trigger this control-test step.

Root HK also adds a permanent repository-wide
`hcoona-release-smoke-npm-consumer-policy` step. It scans dependency manifests,
lockfiles, workflows, package-manager/install scripts, and dependency
configuration for normal developer, CI, or production consumption of
`@hcoona/hcoona-release-smoke-npm`. Cataloged dependency-surface paths trigger
it, including manifests and lockfiles in every workspace, workflow files,
install/bootstrap scripts, package-manager configuration, and the HK policy
implementation itself. Manual `slice-validation` runs it unconditionally. Any
consumer fails root `SourceTreeConformance`, disables live use, and reopens the
accepted exception. Acceptance probes and the package's own manifest are
explicit narrowly validated fixtures, not normal consumers.

### Live Buddy

Files:

- caller:
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`;
- same-revision reusable Attempt:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`.

Event: `workflow_dispatch` with no target, version, destination, force, or
variant input. The GitHub-selected workflow ref is the target.

Job DAG:

```text
request
  -> discover-node
  -> compile-model
  -> evaluate-live-eligibility
  -> run-live-attempt [Release Execution concurrency-scoped caller]
       -> invoke same-revision reusable workflow
            -> admit
                -> discover retained same-Execution history
                -> snapshot admitted history IDs/digests
                -> bind current Attempt
            -> plan-qualification
                +-> build-tarball
                +-> project-test
            -> npm-artifact-qualification [after build-tarball; two Evidence records]
            -> qualification-finalizer
            -> observe-github-packages
            -> materialize-publication
            -> approval
            -> approval-finalizer (if the run continues)
            -> publish-github-packages (only authorized absent action)
            -> release-finalizer (read-only finalization if the platform runs it)
```

`run-live-attempt` uses concurrency
`wdv3-execution-<sha256(Buddy Execution Identity)>` and
`cancel-in-progress: false`. Compilation occurs before this coalescing point.
The caller passes the immutable request-local Repository Model artifact ID and
digest plus the immutable Live Eligibility Decision artifact ID/digest into the
reusable workflow, which validates same-revision target, purpose, request, run,
attempt, producer, control, consumer-policy, and complete attestation source
provenance bindings before admission.

`evaluate-live-eligibility` runs after exact target pinning and Repository Model
compilation and before Execution lookup, concurrency, history admission, or
Attempt creation. It:

1. scans the exact target's cataloged manifests, lockfiles, workflows,
   install/bootstrap scripts, package-manager configuration, and other
   dependency surfaces using Release-owned policy;
2. permits only explicit digest-bound exceptions for the package's own
   declaration and reviewed acceptance fixture;
3. validates the Release policy's exact attestation source fields as repository
   `hcoona/three`, ref `refs/heads/main`, and path
   `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`, verifies
   protection of that ref, and uses `contents: read` to freshly resolve it to a
   full commit SHA and read the fixed-path blob at that commit;
4. validates canonical schema/content, explicit accepted writer and
   package/repository/Manage Actions access inventory or evidence digest,
   policy/package binding, issuer, inspection time, expiry, acknowledged
   limitations, and required `live_enabled: true`; and
5. emits an immutable Live Eligibility Decision by Actions artifact ID/digest.

The job declares exactly `contents: read`. It has no `actions: read`,
`packages: read`, package-write, PAT, or OIDC permission.

The attestation expires no later than 90 days after inspection. The
Decision binds live purpose, request, current run/attempt, selected ref/SHA,
Repository Model digest, producer/control, policy/catalog digests, exact scanned
surfaces/content digests/exceptions, the `live_enabled` value, attestation
repository/ref/resolved commit/path/Git blob OID/canonical content SHA-256, and
pass/block result. Only current-attempt success may proceed, and the Attempt and
human summary retain the Decision and complete attestation provenance. CI HK,
Execution history, and prior Decisions cannot substitute.

Missing, unreadable, malformed, expired, provenance-mismatched, disabled, or
consumer-positive state blocks before the caller. Runtime does not enumerate
current repository writers or GitHub Packages grants: `GITHUB_TOKEN` cannot do
the former and GitHub Packages has no complete grants API. Relevant role, grant,
or Manage Actions changes require an authorized human to promptly commit
`live_enabled: false` to the protected source, then update and re-attest before
a later protected commit restores it to true. Protection, review, merge, and
fresh-read latency make this bounded operational response rather than
instantaneous platform disablement, and a capability job already past its final
check may complete. Expiry bounds normal-flow staleness. No repository variable,
PAT, GitHub App, service account, ledger, `id-token`, or additional token
permission is added. The protected document is not a security boundary against
trusted malicious writers.

The caller job holds the concurrency slot until the reusable workflow completes
finalization; the slot is not released after `admit` or planning. Queue-single
platform behavior retains at most the newest pending caller as representable.
A superseded pending caller never invokes the reusable workflow, is not
admitted, and creates no Attempt. Every surviving reusable invocation emits its
Attempt binding before later planning.

Within `admit`, the trusted command caller selects `execution-history`;
candidate payloads cannot select their admission mode. `actions: read` fully
paginates retained workflow runs for the exact caller and reusable workflow
identities, then each candidate run's artifacts and jobs. It enumerates artifact
IDs and downloads candidate records only by ID; name metadata never selects a
record. The current run ID is not categorically excluded. Same-run artifacts declaring
an earlier run attempt may be admitted as history-only diagnostics when
artifact ID/digest, payload integrity, head SHA, Execution/target correlation,
and separately queried existence of that prior run attempt validate.
Authoritative historical attribution validates only artifact
ID/digest, source workflow run ID, head SHA, payload integrity, and exposed
artifact/run metadata. Jobs/Run APIs separately provide run-attempt, job,
conclusion, and phase facts. Payload producer, exact run attempt,
reusable-workflow, purpose, and control claims remain diagnostic
self-assertions. Historical records are marked history-only and cannot satisfy
current Evidence, authorization, artifacts, Receipts, or outcomes. Same-run
admission never claims artifact-to-attempt or artifact-to-job provenance. Every later
current record uses caller-selected
`current-authority` admission and requires exact current purpose, request, run,
run attempt, Attempt, target, producer, control, artifact ID, and digest.

Before creating the current Attempt binding, `admit` writes one immutable
Execution History Admission Snapshot containing the current request/run/run
attempt, the exhaustive pagination/query basis, sorted admitted history record
artifact IDs/digests/source run IDs/head SHAs, separately queried Jobs/Run phase
facts, and an explicit history-only marker. Finalization and summaries bind that
Snapshot. A 403/404, rate-limit truncation, incomplete link/cursor traversal,
malformed or duplicate response, digest mismatch, or conflicting/cross-Execution
binding fails before Attempt creation. An artifact marked expired, or a run
older than retention with no binding artifact, is recorded as unavailable
history. A recent run missing an expected non-expired binding blocks. After
retained history expires, provably absent or exact destination state may
proceed; partial, conflicting, unknown, or unprovable state requires
reconciliation. No permanent ledger or service is introduced.

If strict historical workflow/attempt provenance becomes necessary, this
history capability is unsupported until Artifact Attestations or an OIDC-backed
mechanism is separately approved. The first slice does not enable
`id-token: write`.

The live dispatcher/caller workflow declares no workflow-wide permission:

```yaml
permissions: {}
```

Every caller job declares its exact minimum permissions rather than inheriting
them by omission. `evaluate-live-eligibility` declares only `contents: read`;
it has no Actions-history or package permission. The `run-live-attempt` caller
job alone declares:

```yaml
permissions:
    contents: read
    actions: read
    packages: write
```

It is a `uses`-only job with no steps and no direct token use. This declaration
is solely the reusable-workflow ceiling required because the called workflow
cannot elevate beyond its caller job. It does not grant package write to any
other caller or called job. No permission in this ceiling is treated as
enabling complete writer or package-grant enumeration. Fresh protected-ref
resolution and attestation reads use only `contents: read`; repository
variables are not read, and no additional token permission is introduced.

The called reusable workflow baseline is `contents: read`. Only `admit`
declares effective `actions: read` for exhaustive execution-history admission.
The observer alone declares `packages: read`; only
`publish-github-packages` declares `packages: write`. Unspecified permissions
are `none`; the `approval` job overrides to `permissions: {}` and receives no
token. `approval-finalizer` declares only `contents: read`. Every called job has
an explicit least-privilege permission contract; no job can receive
Actions-history or package permission by omission. No called job can elevate
above the `run-live-attempt` caller ceiling, and that ceiling does not make a
permission available to a job that does not explicitly declare it.

`publish-github-packages` uses concurrency
`wdv3-resource-<sha256(canonical GitHub Packages lock projection)>` and
`cancel-in-progress: false`. The projection contains only the physical
destination ID and normalized npm package name. It excludes channel, version,
target, routing tag, and Release Unit, so every action touching the same
destination/package receives the same group, including actions with different
target-derived tags.

GitHub concurrency supports equality groups, not acquisition of every member of
an arbitrary resource-key set or general set-overlap locking. The first slice
therefore intentionally over-serializes all mutations for one
destination/package. This conservative group is only the platform enforcement
projection. The authoritative complete key set still contains the exact Buddy
GitHub Packages External Package Coordinate and the
destination/package/`buddy-sha-<40-lowercase-target-sha>` mutable resource. The
Publication Snapshot, Publication Action, Capability Admission validation,
publisher validation, Action Result, Receipt, and any remediation binding all
preserve and verify that complete frozen set plus the enforced projection/group.
Future Adapters retain complete-set overlap semantics and are unsupported when
no safe platform projection can enforce them. This does not weaken
`WD-CON-004`.

`materialize-publication` canonicalizes the Publication Snapshot JSON and a
deterministic Markdown reviewer summary that embeds the Snapshot digest. It
uploads the canonical Snapshot as its own immutable, non-archived artifact and
uploads the reviewer payload as a separate immutable artifact, using the
Renovate-selected current Node-24-compatible `actions/upload-artifact` major,
full 40-character commit pin, and version comment. It binds the reviewer
artifact transport to the exact Snapshot and summary payloads, captures the
returned IDs, URL, and artifact digests, and, after successful binding, writes
the Markdown plus artifact link to its completed job summary. The `approval`
job receives the reviewer artifact URL through a `needs` output and assigns it
to `environment.url`.

Failure or cancellation before the Snapshot artifact is durably uploaded stops
approval and publication and is eligible for a
`publication-preparation` incomplete Outcome. If the Snapshot artifact was
uploaded before a later reviewer-artifact or binding failure, approval and
publication still stop, but the Release Finalizer retains the Snapshot and uses
the existing Snapshot-bound outcome path rather than claiming that publication
preparation never completed. During whole-workflow cancellation, an unstarted
publisher may be reported as `cancelled`; that result is admitted as
publication preparation only when cancellation is directly observed and no
Snapshot, authorization, capability, mutation, bundle, or Receipt lineage
exists.

GitHub's Environment approval dialog has no custom body. Reviewers follow the
deployment URL or completed `materialize-publication` job summary to inspect the
canonical Snapshot and digest-bound Markdown. The Authorization Record binds the
Publication Snapshot digest plus reviewer-summary artifact ID and digest.
Admission and publisher validation fail closed on any mismatch.

Environments:

- `workflow-delivery-v3-buddy-smoke-approval`: human reviewer, no credentials,
  used only by `approval`;
- `workflow-delivery-v3-buddy-smoke-github-packages`: capability boundary used
  only by the publisher job.

The approval Environment prevents self-review and administrator bypass where
the actual repository plan and GitHub tier expose those controls. Rollout must
record the real settings and any unavailable control; the LLD does not claim an
unavailable guarantee.

The approval job is the separate human gate and has no credentials. The
publisher Environment is the downstream capability boundary and need not
require a second reviewer. `approval-finalizer` is the credential-free
Capability Admission Gate between them. An additional destination reviewer is
optional.

Because GitHub jobs do not share a workspace, the credential-free approval job
obtains control code through an anonymous public Git fetch of the exact selected
40-character target SHA from `https://github.com/hcoona/three.git`. It verifies
the fetched commit and detached `HEAD` before executing the v3 Authorization
formatter directly from that checkout. It does not use `GITHUB_TOKEN`,
`actions/checkout`, Actions artifact download, a package registry, a moving ref,
or fallback revision. Failure to fetch or verify the exact SHA leaves the
Attempt replayable incomplete and emits no Authorization Record.

Within the called workflow, only the executing
`publish-github-packages` job declares:

```yaml
permissions:
    contents: read
    packages: write
```

It has no PAT and no `id-token: write`. Observation has `packages: read` only.
Planning, build, qualification, approval, and finalization have no package-write
permission.

`approval-finalizer` has only `contents: read`, no Actions-history or package
permission, no Environment credential, no PAT, and no OIDC permission.
After the current-attempt approval job successfully emits the Authorization
Record, `approval-finalizer` validates:

- Authorization Record and approval job/run-attempt binding;
- Publication Snapshot and reviewer-summary artifact;
- every exact planned action and artifact;
- every complete Adapter resource-key set;
- the capability-group manifest;
- the current-attempt Live Eligibility Decision and exact Release-policy
  Governance source fields;
- a newly resolved/read, `contents: read`-fetched
  `hcoona/three` + `refs/heads/main` +
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`
  attestation whose protected-ref status, schema, canonical content,
  policy/package bindings, current expiry, and `live_enabled: true` are valid
  and whose admitted boolean, resolved commit, blob OID, and content SHA-256
  exactly match the Live Eligibility Decision.

It emits the Capability Admission Decision only on exact success.
Disablement, expiry during the approval wait, changed source/provenance/content,
binding change, or other invalidation produces a blocking Decision. Re-enabling
live or merging a replacement valid attestation does not resume this Attempt; a
new Attempt must repeat eligibility, planning, build, qualification,
observation, and approval.

The slice creates no additional aggregate Publication Control Bundle artifact.
Its control closure is the exact set of separately retained Snapshot,
reviewer, Qualification, Adapter Context, Release Artifact, Live Eligibility,
Authorization, Capability Admission, and package artifacts plus their canonical
cross-bindings. `approval-finalizer` acquires the Snapshot, reviewer artifact,
and Live Eligibility Decision by explicit ID. The publisher then acquires its
exact eight required closure members in one comma-delimited,
`merge-multiple: true`, explicit-ID download. It never selects by name or
latest artifact, and the durable Publication Snapshot remains the lifecycle
boundary rather than any synthetic closure identity.

`publish-github-packages` has `needs: approval-finalizer` and a strict admitted
condition; GitHub may not schedule or start that package-write job before gate
success. This LLD elects to have the publisher repeat the
Authorization/Snapshot bindings and the same `contents: read` fixed-source
`live_enabled`/provenance/content/expiry checks immediately before the npm
mutation. Failure blocks mutation as defense in depth. This repeat adds no
credential or service and does not make the target-revision publisher a
malicious-writer boundary.

`release-finalizer` uses `if: always()` and directly declares
`observe-github-packages` and `materialize-publication` in `needs` in addition
to its existing authoritative inputs. Direct dependencies expose exact GitHub
job results and outputs; they do not continue approval or publication after a
failure. Its checkout, tool setup, and required and optional artifact
acquisition steps explicitly admit cancellation so retained inputs remain
available when GitHub schedules cancellation finalization. The workflow adapter
translates only the approved state combinations into
`--publication-preparation-interrupted`. It requires successful Qualification
and no durable Snapshot artifact. The publisher result must be `skipped`, or
may be `cancelled` only when whole-workflow cancellation is directly observed
and no downstream lineage exists. The cancellation-owned result is not also
translated as post-Snapshot platform termination. No Authorization, Capability
Admission Decision, mutation marker, result bundle, or Receipt may exist. Job
success without the required Snapshot, unexplained skips, failed Snapshot
admission, partial optional record transport, or downstream lineage without a
Snapshot are contract failures.

If Qualification is `failure` or `incomplete`, whole-workflow cancellation may
likewise report the unstarted publisher as `cancelled`. With Observation and
materialization skipped and no Snapshot or downstream lineage, this does not
become platform termination; the existing qualification-only Outcome is
retained. Contradictory lineage continues through the normal domain rejection.

The sole Release Finalizer then verifies the exact successful Qualification
Decision and record absence before emitting
`publication-preparation`/`incomplete` with uncertainty, no possible mutation,
and next action `new-attempt`. It appends the direct Observation and
materialization and publisher results, Snapshot presence, and capability-path
state to the retained Attempt summary and GitHub Step Summary. The workflow
uploads the Outcome and summary before propagating a failed release conclusion.
GitHub may still cancel the entire run before the Finalizer executes; no
watchdog is added.

GitHub Environment `DeploymentReview` cannot produce authoritative
current-attempt rejection Evidence because it lacks `run_attempt` and
approval-job binding and has no documented append-only/consistency contract for
safe review-ID delta inference. Rejection or denial therefore emits no
admissible Approval Outcome Evidence in this LLD. If `release-finalizer` runs,
the sole Release Finalizer records unknown approval-contract failure and a
replayable incomplete Attempt Outcome; otherwise the run remains replayable
incomplete without a context-owned outcome. Observable review information is a
non-authoritative human diagnostic only. No capability group starts.

Workflow Delivery adds no approval watchdog. If GitHub cancels or expires the
run while approval remains pending, `approval-finalizer` may not run. When no
capability group started, the platform run/job conclusion is sufficient
no-side-effect terminal evidence and leaves a replayable incomplete Attempt. If
a capability job may have started, the Attempt is incomplete and possibly
mutated and replay must reobserve.

The Environment is a mandatory normal-process gate, not a security boundary
against a malicious repository writer. Every Write/Maintain/Admin actor is
inside this slice's publisher TCB and can author another write-capable workflow.
If that assumption changes, live Buddy blocks until the actor loses those roles
or an independently enforced publisher boundary makes package-write Capability
and destination access unavailable to writer-authored workflows.

After activation, human Governance re-attests every Write/Maintain/Admin actor
and package/repository/Manage Actions access after relevant role, team, or
permission changes and at least every 90 days. An authorized human promptly
commits `live_enabled: false` to the policy-fixed protected document pending
inspection and explicit reacceptance, then updates and re-attests before a later
protected commit may restore it to true. This response is not instantaneous:
protected review, merge, and fresh-read latency remain, and a capability job
already past its final check may complete. Attestation expiry blocks stale
normal flows. The permanent HK consumer policy independently blocks any normal
dependency introduction and reopens the exception.

Every active capability group uploads exactly one immutable
`capability-group-result-bundle` artifact, even when the group has one action.
The strict bundle contains:

- Release Attempt, Publication Snapshot, `github.run_id`, and
  `github.run_attempt` bindings;
- group ID and exact planned action-ID set;
- for each action, outcome, destination response identity, Receipt reference
  when mutation completed, and diagnostic reference;
- group completion state; and
- producer job and same-revision control identity.

The Release Finalizer downloads it by immutable Actions artifact ID, verifies
its canonical digest and producer, and requires exact set equality with the
group manifest. Missing bundle, duplicate bundle, missing action, extra action,
mismatched action, or conflicting completion state is blocking. The active
one-action GitHub Packages group therefore requires one bundle covering exactly
that action.

### Official Simulation

File: `.github/workflows/workflow-delivery-v3-official-simulate.yml`

Event: `workflow_dispatch` with no target, version, or live-mode input. Any
same-repository selected ref is accepted.

Job DAG:

```text
request
  -> discover-node
  -> compile-simulation-model
  -> create-simulation-identity
  -> plan-simulation
      +-> build-tarball
      +-> project-test
  -> npm-artifact-qualification [after build-tarball; two Evidence records]
  -> qualification-finalizer
  -> observe-npmjs
  -> materialize-hypothetical-actions
  -> simulation-finalizer
```

The workflow has `contents: read` and no Actions-history, Environment,
`packages`, `id-token`, PAT, npm token, Authorization Record, Capability,
Receipt, or mutation. Concurrency is request-scoped:
`wdv3-simulation-<github.run_id>-<github.run_attempt>`.

## Deadlines, Failures, and Retention

Initial job deadlines are:

| Boundary                                                             | Deadline        |
| -------------------------------------------------------------------- | --------------- |
| request/discovery/planning job                                       | 10 minutes each |
| live eligibility scan and platform comparison                        | 10 minutes      |
| root HK, project build, project test, or npm build/qualification job | 15 minutes each |
| remote observation                                                   | 10 minutes      |
| package publication                                                  | 10 minutes      |
| finalizer                                                            | 5 minutes       |
| complete simulation pass                                             | 45 minutes      |

Approval has no Workflow Delivery watchdog. Completed approval-job results are
`approved` or `rejected`/denied. GitHub may instead terminate the pending run by
cancellation or platform Environment gate expiry, currently up to 30 days,
without a downstream context-owned outcome. The design does not require
distinguishing cancellation from expiry unless GitHub exposes it. Neither a
pending nor completed approval freezes Governance state: immediately before
Capability Admission, a fresh `contents: read` check must still observe
`live_enabled: true`, and the at-most-90-day attestation must remain unexpired
and provenance/content-identical to the pre-Attempt Decision.

The CI finalizer also records elapsed time against the 12-minute ordinary-PR
SLO; missing the SLO does not convert failure to success.

Top-level failure classes are:

| Class                                | Examples                                                                                                    | Result                                                           |
| ------------------------------------ | ----------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| invalid request/binding              | wrong target, purpose, run attempt, producer, digest                                                        | blocked/contract failure                                         |
| incomplete model/plan                | missing descriptor, graph, NBGV fact, variant, obligation, Adapter                                          | blocked; no authoritative partial execution                      |
| live eligibility blocked             | consumer found; `live_enabled: false`; missing, invalid, expired, or mismatched attestation                 | blocked before Execution/Attempt                                 |
| quality failure                      | build, test, tarball, install/import failure                                                                | failed Decision                                                  |
| publication preparation interruption | Observation, Snapshot materialization, or Snapshot upload fails/cancels before durable Snapshot persistence | incomplete; no possible mutation; new Attempt                    |
| destination partial/conflict         | missing or mismatched dist-tag; differing bytes, ownership, or target provenance                            | reconciliation-required                                          |
| destination unknown                  | denial, timeout, malformed response, unavailable tarball, unreadable tag                                    | unknown/unprovable; fail closed                                  |
| approval rejection unknown           | Environment rejection without exact attempt-bound denial proof                                              | incomplete/replayable; diagnostic only, no Capability            |
| approval-pending termination         | cancellation/expiry before any capability group starts                                                      | incomplete/replayable; platform conclusion proves no side effect |
| Governance freshness blocked         | `live_enabled: false`; attestation expired, changed, or invalidated after eligibility                       | blocked; new Attempt required after Governance restoration       |
| post-capability cancellation         | capability job may have started                                                                             | incomplete/possibly mutated; replay reobserves                   |
| approval contract failure            | running Finalizer has neither applicable authorization nor terminal Evidence                                | unknown; no Capability                                           |
| action failure                       | create conflict, transport failure, lost response                                                           | replayable unless observation proves reconciliation state        |
| Receipt failure                      | mutation may have occurred but Receipt was not persisted                                                    | incomplete; replay reobserves                                    |

All authoritative JSON, summaries, artifacts, Evidence, Snapshots, observations,
actions, approval records, control-closure artifacts, capability-group result
bundles, Receipts, and outcomes use 45-day Actions retention. This provides
margin over the platform approval-expiry window. Activation is blocked if
repository retention policy cannot provide 45 days. Registry state is durable
external state, not an Actions retention substitute. Expired lineage never
turns present unprovable state into exact. The retention margin does not extend
attestation expiry or defer the prompt protected commit setting
`live_enabled: false`.

Every Finalizer writes a human summary containing identity, target, mode,
version facts, Repository Model and Live Eligibility Decision IDs/digests,
complete attestation provenance, Plan/Snapshot digests, obligations,
artifacts, observations, approval state, actions, Receipts, failure class, and
allowed next action.
Buddy approval additionally shows selected ref/SHA, coordinate, SHA-256 and
SHA-512, tarball manifest, lifecycle scripts, and exact action summary.

## Replay and Concurrency Semantics

- **Re-run all jobs** preserves request ID and `github.run_id`, increments
  `github.run_attempt`, compiles a new purpose-bound Repository Model Snapshot,
  reruns live eligibility for live mode, and creates a new Attempt or simulation
  pass.
- Prior-attempt Snapshot, Fact Bundle, artifact, Evidence, observation,
  Live Eligibility Decision, approval, action, Receipt, or outcome admission
  fails.
- Governance-freshness failure after approval permanently blocks publication
  from that Attempt. After Governance is restored, only a new whole-release
  Attempt may proceed.
- `Re-run failed jobs` is unsupported and the Finalizer reports mixed-attempt
  contract failure.
- Separate admitted manual requests for one Buddy Execution Identity create
  separate Intents and Attempts in that Execution.
- No Intent or failed pre-mutation Attempt reserves the package coordinate.
- Pre-observed exact state still requires approval but creates no action,
  Capability, or Receipt.
- A lost publish response or Receipt is recovered only by a new complete
  Attempt that rebuilds, qualifies, observes, and receives new approval.
- GitHub execution and resource concurrency are serialization mechanisms, not
  destination correctness locks. The first-slice destination/package equality
  group intentionally over-serializes while preserving complete resource-key
  overlap semantics.

## Acceptance Plan

### Contract and Binding Tests

1. Golden canonicalization and digest fixtures.
2. Unknown, duplicate, malformed, wrong-purpose, wrong-target, wrong-producer,
   wrong-run, and prior-run-attempt rejection for every transported record.
3. Simulation records rejected by live admission and vice versa.
4. Artifact ID/name substitution, digest mismatch, extra output, and missing
   output rejection.
5. Generic Authorization and Approval Outcome Evidence are mutually exclusive;
   the first-slice GitHub path never admits denial Evidence, and diagnostic
   Deployment Review data cannot grant Capability.
6. Workflow fixtures reject tag/branch action references and require
   Renovate-managed full 40-character pins with version comments and a current
   Node-24-compatible `actions/upload-artifact` major.
7. CODEOWNERS tests discover every governed file and newly added descriptor,
   explicitly require final-match ownership by `@hcoona` for
   `/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
   resolve all other governed paths to `@hcoona`, and fail missing or overridden
   patterns. Arbitrary-ref Buddy eligibility remains unaffected.
8. Trigger fixtures prove `.github/CODEOWNERS`, every v3
   control/catalog/test, governed workflow/action/script, HK
   configuration/helper, root Python workspace/lock input, and every descriptor
   add/delete/rename/modify runs `v3-control-pytest`; `slice-validation` always
   runs it.
9. Normal live-workflow permission fixtures reject workflow-level
   `packages: write`, require the live caller baseline to remain empty or
   read-only, allow `packages: write` only on the `run-live-attempt` `uses`-only
   caller job and the called Environment-referencing publisher job, reject
   package-write inheritance or grants on every other live-workflow job, and
   prove the callee cannot elevate beyond the caller-job ceiling. They require
   `evaluate-live-eligibility` to declare exactly `contents: read`, allow
   effective `actions: read` only on `admit` apart from the tokenless
   reusable-workflow caller ceiling, allow explicit `packages: read` only on
   `observe-github-packages`, and reject Actions-history permission on CI,
   simulation, acceptance, and every other normal live job plus explicit
   package-read permission on every non-observer job.
10. Node/NBGV Provider fixtures require exact-target checkout with
    `fetch-depth: 0` or an equivalent complete ancestry/tag guarantee, prove
    that full-history fetch leaves `HEAD` pinned to the target SHA, and reject
    shallow, missing-tag, incomplete-ancestry, or target-mismatch state before
    NBGV facts are compiled.
11. Resource-lock fixtures prove that complete coordinate-plus-tag key sets
    remain bound in Publication Snapshots, actions, Receipts, admission, and
    remediation while the actual GitHub group hashes only canonical physical
    destination plus normalized npm package name. Same destination/package with
    different versions or target-derived tags must produce the same group;
    different destination/package projections remain independently groupable.
    A complete-set hash used as the actual group fails the fixture.

### CI Scenarios

1. Project source change selects root HK, project build, project tests, and npm
   artifact build.
2. Manual `slice-validation` selects the complete first-slice scope without a
   synthetic changed range and never claims repository-wide full coverage.
3. A new project test failure fails the stable shadow slice check.
4. Repository-only change runs root HK and valid empty affected-system lanes.
5. Missing comparison identity blocks incremental planning rather than falling
   back to full.
6. Neither first-slice CI event creates a Ruleset required check or a parallel
   authoritative Decision; v1 remains required.
7. A change only to
   `eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml` selects the
   root-HK v3 pytest step; unrelated product source alone does not.
8. Every cataloged dependency-surface change runs the permanent smoke-package
   consumer policy; manual `slice-validation` runs it unconditionally.
9. A normal manifest, lockfile, workflow, install script, or dependency
   configuration reference to `@hcoona/hcoona-release-smoke-npm` fails root HK
   and reopens the exception; approved acceptance fixtures do not.
10. The first implementation pull request, whose exact base commit lacks the
    canonical v3 CI workflow, preserves its canonical
    `incomplete-model-plan` Decision for exclusively unclassified paths but
    projects a successful bootstrap check conclusion. The same Decision fails
    after the workflow exists in the base. Manual runs, mixed diagnostics,
    superseded candidates, lane failures, and missing or malformed Decisions
    never receive the projection.

### Build and Artifact Scenarios

1. Adapter builds the same target twice in clean staging and compares tarball
   bytes for determinism.
2. Frozen `npmPackageVersion` appears in staged and packed `package.json`;
   ambient manifest fallback and NBGV recomputation are rejected.
3. Source `package.json` remains byte-identical. The staged and packed manifests
   preserve the existing intended `files` entries and add exact entry
   `workflow-delivery/provenance.json`; a missing, dropped, duplicate, or extra
   allowlist entry fails.
4. Qualification opens the packed tarball, proves it contains only declared
   package files, and requires exact tar entry
   `package/workflow-delivery/provenance.json`. The extracted bytes must be
   canonical and byte-identical to the frozen witness input with exact
   target/Release Unit/NBGV/Build Definition/catalog/control/purpose/schema
   facts and no run/Attempt IDs; altered, missing, misplaced, sidecar-only, or
   nondeterministic witnesses fail.
5. A clean consumer installs with scripts disabled, imports `smokeMessage`, and
   verifies the installed witness.
6. Live and simulation each run the tarball-dependent physical qualification
   job and emit separate admitted Evidence for
   `node/npm-artifact-contents-v1` and `node/npm-install-import-v1`; omission or
   substitution of either blocks the Qualification Finalizer.
7. Source checkout remains byte-clean after success and failure.

### Destination and Replay Scenarios

1. Absent GitHub Packages version plans one create-only action.
2. Exact downloaded tarball SHA-512 plans an approved no-op.
3. Same version with differing bytes, owner, or provenance conflicts.
4. 403, 5xx, malformed metadata, missing tarball, or digest-only evidence is
   unknown or unprovable, never absent.
5. Concurrent identical contenders: one creates; the other either receives a
   proven atomic exact result or fails and becomes exact on replay.
6. Concurrent differing contenders: one may create; the other fails without
   overwrite and later observes conflict.
7. Publish success with lost response or Receipt replays to exact state.
8. Deleted or restored package state never triggers ordinary delete, restore,
   overwrite, or recreation; it requires reconciliation/Break-Glass review.
9. Remote exact state requires coordinate, ownership, byte-identical tarball,
   and an extracted matching target witness. Missing/malformed witness, local
   sidecar-only provenance, or a different target witness is blocking conflict
   or unprovable state.
10. Publish always supplies exactly
    `--tag buddy-sha-<40-lowercase-target-sha>`; acceptance verifies npm tag
    syntax and length and rejects implicit `latest` or shared moving Buddy tags.
11. Exact observation requires that tag mapped to the frozen native version.
    Missing, mismatched, malformed, denied, and ambiguous tag responses classify
    as partial, conflict, unknown, or unprovable and never complete normally.
12. Compound publish probes cover absent creation, identical and differing
    races, Receipt capture of version and tag results, and exact subsequent
    observation. If GitHub Packages cannot make the combined behavior provable,
    live publication is unsupported.
13. Two actions for the same physical destination/package but different
    target-derived tags retain different complete key sets and the same
    conservative GitHub concurrency group. Tests also prove the group may
    serialize non-overlapping versions/tags without changing the abstract
    `WD-CON-004` overlap rule.

### Approval and Trust Scenarios

1. Approval success executes the current-attempt approval job and creates its
   bound Authorization Record. `approval-finalizer` then validates every
   capability input and emits Capability Admission Decision before the publisher
   can be scheduled.
2. Environment rejection/denial creates no admissible Approval Outcome Evidence.
   It is unknown approval-contract failure and a replayable incomplete Attempt;
   observable review data is diagnostic only and no capability group starts.
3. Cancellation or platform expiry while approval is pending may prevent a
   separate Evidence record and Finalizer outcome. With no capability group
   started, the platform conclusion proves no side effect and leaves a
   replayable incomplete Attempt. If capability may have started, the Attempt is
   possibly mutated and replay reobserves. Tests do not require distinguishing
   cancellation from expiry unless GitHub exposes it.
4. A running `approval-finalizer` without valid current-attempt Authorization
   Record produces approval-contract failure; no workflow watchdog fabricates
   another timeout.
5. The arbitrary same-repository feature-ref flow uses its exact complete
   target-revision v3 stack and reaches the dedicated approval Environment.
6. The `run-live-attempt` uses-only caller holds the permission ceiling without
   token-using steps while the caller workflow has no workflow-wide package
   write. `evaluate-live-eligibility` receives only `contents: read`. In the
   callee, `admit` alone receives effective `actions: read`, the observer alone
   declares `packages: read`, `approval-finalizer` has neither permission, the
   Environment-referencing publisher alone receives effective
   `packages: write`, and all other jobs fail explicit and inherited
   permission-negative probes. The callee cannot elevate beyond the caller-job
   ceiling. No job receives PAT or `id-token: write`.
7. Actual self-review and administrator-bypass settings are recorded truthfully.
8. The fixed-source human-inspected writer/access attestation is unexpired, and
   the permanent HK no-consumer dependency-policy gate passes.
9. The one-action capability group emits one exact result bundle; missing,
   duplicate, mismatched, or extra action coverage blocks finalization.
10. `materialize-publication` uploads the canonical Publication Snapshot as one
    immutable non-archived Actions artifact and uploads deterministic Markdown
    plus reviewer inputs as a separate immutable reviewer artifact through the
    Renovate-selected current Node-24-compatible action major and full-SHA pin.
    It exposes only the reviewer artifact URL through `environment.url` and
    writes the same link/summary to the completed job summary after successful
    reviewer transport binding. The reviewer artifact transport is bound to the
    exact Snapshot and summary payloads; the reviewer artifact ID/digest and
    Snapshot digest match the Authorization Record, and mismatches block
    publisher admission.
11. Inspection records token permissions and grants, proves no known Official or
    production reach, and safely probes only enumerated unrelated assets without
    claiming universal negative reach proof.
12. Deployment Review contents and review-ID deltas cannot be admitted as
    current-attempt denial Evidence. Deployment failure/error, ordinary job
    failure, missing pending deployment, 403/404, timeout, malformed data, and
    generic non-review denial remain unknown and grant no Capability.
13. GitHub cannot schedule or start `publish-github-packages` unless
    `approval-finalizer` succeeds. The gate uses `contents: read` to freshly
    resolve and read the exact fixed source immediately before admission, and
    the LLD's publisher repeat validation still blocks mutation as defense in
    depth.
14. Eligibility validates the Release-policy source fields as `hcoona/three`,
    `refs/heads/main`, and
    `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
    verifies ref protection, freshly resolves the ref, reads the fixed-path blob
    with `contents: read`, binds commit/blob/content provenance, validates
    schema/bindings/expiry and `live_enabled: true`, and combines it with the
    exact-target no-consumer scan before Execution lookup.
15. Missing, unreadable, expired, malformed, provenance-mismatched, disabled,
    prior-attempt, wrong-source, or consumer-positive eligibility input blocks
    before concurrency/history/Attempt. CI HK and history cannot satisfy it.
16. After eligibility but before Capability Admission, committing
    `live_enabled: false`, crossing attestation expiry during the up-to-30-day
    approval wait, changing the protected ref resolution/blob/content or policy
    bindings, or otherwise invalidating Governance blocks the Attempt. A
    replacement valid attestation or re-enablement requires a new Attempt and
    new approval.
17. Publisher-side freshness revalidation is optional architecture-wide, but
    this slice elects and requires it immediately before npm mutation. It uses
    only existing permissions, creates no credential/service, and is tested as
    defense in depth rather than a malicious-writer boundary.
18. Protected-document disablement is tested as an operator control with
    bounded review/merge/read latency, not as instantaneous platform enforcement
    or malicious-writer protection. Tests assert that no repository variable or
    additional token permission is used and that runtime performs no writer or
    GitHub Packages grant enumeration.

### Simulation Scenarios

1. Official simulation from an arbitrary selected ref freezes canonical and
   native NBGV facts, builds and qualifies, observes npmjs, and reports
   hypothetical actions.
2. It creates no live identity, approval deployment, Capability, Receipt, or
   registry mutation.
3. Permission-negative tests prove npm credentials, `packages: write`, and
   `id-token: write` are unavailable.

### Bootstrap and History Scenarios

1. The protected attestation remains `live_enabled: false` while protected
   fixed-coordinate acceptance proves absent/create/readback, exact handling,
   conflicting bytes, and lost response behavior.
2. Wrong target SHA, coordinate, confirmation, ref, Environment, or any normal
   Release-style input blocks before a probe job.
3. Successful probes produce only Governance evidence; removal verification
   proves no acceptance workflow, bypass, or Environment remains.
4. `capture-governance-evidence` runs on the first attempt even when a probe
   dependency fails, is skipped, or is canceled. It persists every dependency
   result, all available response/digest/diagnostic data, and an explicit
   mutation disposition. Missing proof after a possibly started mutation is
   incomplete/unknown and enters reconciliation.
5. Failed probes leave `live_enabled: false` and all Buddy publication
   disabled, remove the temporary path, retain diagnostics, and send
   created/ambiguous state to reconciliation.
6. Live `admit` completely paginates and admits same-Execution retained history
   into an Execution History Admission Snapshot before current Attempt binding.
7. Historical Evidence cannot satisfy current obligations. Duplicate,
   malformed, truncated, or digest-mismatched platform/payload facts block
   history admission; self-asserted purpose/Execution/control mismatches reject
   that candidate and remain diagnostic rather than provenance proof.
8. After history expiry, absent/exact observation proceeds while
   partial/conflicting/unknown/unprovable state requires reconciliation.
9. Every physical artifact name is deterministic and workflow-run-unique,
   using `github.run_attempt` directly or in the deterministic hash preimage,
   with `overwrite: false`. Conformance rejects a rerun name collision.
   Current-authority consumers still reject name lookup, latest selection,
   prior-attempt IDs, and metadata/run-attempt mismatch and admit only an
   explicit ID.
10. Caller-selected `current-authority` accepts only exact current bindings and
    rejects prior attempts; payload mode fields are rejected.
11. Caller-selected `execution-history` is accepted only during pre-Attempt
    admit, binds only artifact ID/digest, source workflow run ID, head SHA,
    payload integrity, and exposed platform metadata, with Jobs/Run phase facts
    queried separately, and cannot satisfy current authority.
12. Historical payload producer/run-attempt/reusable-workflow claims remain
    diagnostic and cannot upgrade authority; strict provenance remains
    unsupported without separately approved attestation/OIDC.
13. Re-run-all admits an artifact from an earlier attempt of the same run as
    history-only when platform facts prove that prior attempt exists and all
    artifact integrity/head-SHA/Execution/target correlations match. It never
    claims artifact-to-attempt/job provenance.
14. Every acceptance probe independently rejects `github.run_attempt != 1`.
    Evidence capture uses
    `if: ${{ always() && github.run_attempt == 1 }}` or an exact equivalent, so
    it runs after failed dependencies only on the first attempt and remains
    rejected on reruns. Partial reruns cannot reuse the earlier Environment
    review or fixed coordinate. Retry succeeds only through a new reviewed
    workflow invocation with a new fixed disposable coordinate/version.

## Temporary Destination-Acceptance Bootstrap

File, present only during controlled activation:
`.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml`.

Purpose is exactly `destination-acceptance`, distinct from `live-release`,
`release-simulation`, and normal Buddy dispatch. It runs only from the approved
protected activation ref while the policy-fixed protected attestation has
`live_enabled: false`.

`workflow_dispatch` exposes only:

- `target_sha`, which must equal the full SHA pinned in the reviewed acceptance
  plan and workflow constants;
- `package_coordinate`, which for the first bootstrap must equal
  `@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.1`; and
- `confirm`, which must equal
  `I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES`.

The workflow accepts no normal target/ref selection, channel, Release Unit,
version derivation, destination, force, replay, or action input. Fixed probe
versions in the same disposable package are Governance test fixtures, not NBGV
product versions or Release projections.

The bootstrap also uses fixed acceptance-only dist-tags and proves the combined
GitHub Packages publish/tag contract: accepted npm tag syntax and length,
explicit tag selection, absent create/readback, identical and differing races,
and exact version-plus-tag observation. It never relies on implicit `latest`.
Failure to prove the compound behavior classifies the normal Adapter as
unsupported.

The 40-character target SHA is inserted as a literal in the reviewed bootstrap
commit; it is not derived from the dispatch ref. A failed bootstrap retry uses a
new reviewed one-time workflow invocation and a new fixed disposable
coordinate/version rather than reusing or parameterizing the prior coordinate.

Topology:

```text
validate-fixed-inputs [protected ref; no write]
  -> acceptance-review [workflow-delivery-v3-buddy-smoke-acceptance Environment]
  -> probe-absent-create-readback [packages: write]
  -> probe-exact-and-conflict [packages: write]
  -> capture-governance-evidence [no package write]
```

`validate-fixed-inputs` rejects `github.run_attempt != 1` before review. Every
probe job also carries its own job-level guard:

```yaml
if: ${{ github.run_attempt == 1 }}
```

`capture-governance-evidence` is the terminal fan-in and carries:

```yaml
if: ${{ always() && github.run_attempt == 1 }}
```

It records `needs.<job>.result` for validation, review, and every probe;
available canonical suite records, exact scenario inventories, suite-record
digests, immutable artifact IDs/digests, pre/action/response/post facts, and an
explicit mutation disposition. Complete Evidence requires the exact five
scenario set and every non-placeholder binding. A failed, skipped, or canceled dependency never
silently suppresses this first-attempt evidence. If a probe may have started but
durable exact state or non-mutation cannot be proved, the evidence classifies
the bootstrap incomplete/unknown and requires reconciliation.

The original Commit 10 acceptance boundary was pinned to a disposable-package
request captured from Node 24.14.0/npm 11.9.0 against a bounded loopback
registry. Merged dependency update `d3114d77` (#568) advanced the current
acceptance boundary to the separately captured Node 24.19.0/npm 11.17.0
request. Retry 3 therefore installs and verifies Node 24.19.0 and npm 11.17.0
before either write-capable probe; the original capture remains historical
replay evidence rather than current execution authority. The proxy admits only
the exact validated CouchDB coordinate, version, routing tag, attachment bytes
and hashes, witness, path, framing, and dummy authorization; only then may it
replace authorization for the mocked upstream. The absent/create/readback suite
receives one shared 120-second deadline. The exact/race/lost-response suite
receives one shared 300-second deadline across all four scenarios; no scenario
resets that budget.
proof binds the validated raw request and tarball digests to selected upstream
response facts and response identity, with both credentials excluded. One
monotonic deadline supplies decreasing remaining budgets to every observation,
npm process, proxy, upstream, and cleanup boundary. Missing, partial,
wrong-typed, contradictory, or pre-validation runner facts remain incomplete.
Complete Governance Acceptance Evidence independently rejects zero target and
workflow SHAs; incomplete rejected-dispatch evidence retains its sentinel
semantics.

Therefore **Re-run failed jobs**, **Re-run all jobs**, or any other partial
rerun cannot reuse the prior Environment review or coordinate.

Only probe jobs declare `packages: write`; no PAT or `id-token: write` exists.
The workflow top-level permissions are `{}`. Validation and evidence-capture
jobs declare only `contents: read`; each probe job declares `contents: read`
plus `packages: write`. Unspecified permissions are none.
The dedicated acceptance Environment and reviewer configuration are pending
protected finalization; they are not asserted to exist at the commit-10
boundary. Once configured, it is not either normal Buddy Environment. The
workflow emits only Governance acceptance
evidence bound to workflow/run/target/fixed coordinate, dependency outcomes,
available probe results, and complete/incomplete/unknown mutation
classification. It cannot create Release Intent,
Product/Execution/Attempt/Simulation identity, Authorization Record, Receipt,
or live Release history.

The actual Environment reviewer login is unavailable inside the workflow job
context. Governance Acceptance Evidence therefore records `reviewer.login:
null` with source `unavailable-in-job-context` plus run, Environment, asserted
unique review job, and artifact recovery coordinates. This absence alone does
not downgrade otherwise complete Evidence and `github.actor` is never used as
a reviewer substitute. The optional on-demand CLI first resolves the exact workflow run `node_id`
through a bounded REST `GET`, then paginates the supported
`WorkflowRun.deploymentReviews` GraphQL connection with query-only POST
transport. Recovery scope is run plus Environment and is unique because only
`acceptance-review` declares that Environment. Nested Environment pagination
is continued per specific `DeploymentReview` node without advancing or
skipping other review edges. The CLI reports only
present/removed/unknown/human-required diagnostic state. It cannot grant
Capability, enable live, become mandatory acceptance, or prove universal
negatives. Recovery can become impossible after GitHub removes the relevant
deployment/review data, so the coordinates and 45-day immutable artifact are
retained promptly and the retention risk is explicit.

This bootstrap is temporary repository configuration, not a reusable bypass.
After evidence capture, Governance deletes the workflow, any temporary enable
bypass, and the acceptance Environment, then verifies through workflow/API and
Environment inspection that all are absent.

## Activation Gate

The live Buddy workflow remains disabled through the policy-fixed protected
attestation's `live_enabled: false` state until all of the following are
recorded:

- contract, Adapter, concurrency, replay, and permission-negative tests pass;
- disposable GitHub Packages absent/exact/conflict/unknown probes pass;
- tarball download and SHA-512 exact proof is demonstrated;
- atomic non-overwriting create behavior and explicit compound
  version-plus-dist-tag behavior are demonstrated, including tag syntax/length,
  identical/different races, Receipt capture, and exact observation;
- package and destination scope, no-consumer state, and ordinary-action catalog
  are inspected;
- the permanent HK dependency-policy gate covers manifests, lockfiles,
  workflows, install scripts, and dependency configuration, passes on the
  current tree, triggers on dependency-surface changes, and runs unconditionally
  in `slice-validation`;
- actual token permissions and package/repository grants are recorded; known
  Official and production reach is absent; safe denial probes pass for the
  enumerated unrelated assets;
- the dedicated approval and capability Environments and actual reviewer,
  self-review, and administrator-bypass behavior are inspected;
- repository policy permits 45-day Release control and artifact retention;
- every Write/Maintain/Admin actor and relevant package/repository/Manage
  Actions access is human-inspected and accepted in the publisher TCB; a
  canonical non-executable attestation at repository `hcoona/three`, protected
  ref `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` binds
  the required boolean `live_enabled`, explicit inventories or evidence digest,
  policy/package, issuer, inspection time, expiry no later than 90 days, and
  acknowledged limitations; the Release policy's exact source fields and
  fixed-source ref/commit/blob/content provenance are verified;
- Buddy dispatch is frozen; both legacy identities, `buddy.yml` and
  `release-buddy.yml`, are repository-wide disabled; every queued, waiting,
  approval-pending, or running execution is drained or canceled; old-ref
  dispatch rejection is verified; and no compatibility Buddy route exists;
- the temporary destination-acceptance workflow has completed from its protected
  ref on `github.run_attempt == 1` against fixed acceptance-only coordinates,
  every probe carried the independent first-attempt guard, terminal evidence
  capture used `always() && github.run_attempt == 1`, all dependency outcomes
  and ambiguous mutation evidence are retained, no incomplete/unknown state
  remains unreconciled, and the workflow, temporary bypass, and acceptance
  Environment are verified removed;
- a human explicitly accepts the bounded residual risk.

The implementation PR merge is the direct v1 Buddy-to-v3 Buddy cutover. The
merge lands the complete v3 code with `live_enabled: false`, removes both legacy
Buddy workflow files, and does not create `legacy-buddy.yml` or preserve
unrelated v1 Buddy routes. Immediately after merge, Governance freezes Buddy
dispatch, disables both old workflow identities, cancels or drains every
queued, waiting, approval-pending, and running execution, and verifies disabled
state, removal, and old-ref dispatch rejection before acceptance. It then runs
and captures the temporary protected acceptance probes, removes and verifies
removal of the acceptance workflow/bypass/Environment, and only then uses an
authorized protected commit to set `live_enabled: true`. v3 live supports only
the named smoke package. Former Buddy projects are unsupported and blocked until
explicitly migrated. v1 Official and CI assets remain unchanged. Legacy Buddy
workflows, Buddy-specific tests and matrices, and Buddy documentation are
excluded from that preservation and are retired or rewritten. The mandatory
sequence creates an intentional brief Buddy outage.

If acceptance fails before normal enablement, `live_enabled` remains false and
all Buddy publication remains disabled.
Governance removes the temporary workflow, bypass, and Environment, keeps both
legacy Buddy identities retired, retains failure evidence, and routes any
created or ambiguous package state to reconciliation or Break-Glass. A retry
requires a newly reviewed one-time workflow invocation and a new fixed
disposable coordinate/version; no reusable bypass remains. There is no
compatibility rollback. Restoring legacy Buddy requires a separate user-approved
rollback PR.

The protected attestation field is only a normal-flow rollout gate, not a
malicious-writer security boundary. Failure of any gate leaves live Buddy
blocked while CI and Official simulation may continue.

After activation, a relevant role, team, permission, package/repository grant,
or Manage Actions change requires an authorized human to promptly commit
`live_enabled: false` to the policy-fixed protected source. Human Governance
then re-inspects, updates, and re-attests before a later protected commit may
restore `live_enabled: true`. Protection, review, merge, and fresh-read latency
mean disablement is not instantaneous, and a capability job already past its
final check may complete. Expiration of the at-most-90-day attestation blocks
normal flows and bounds staleness. Every dispatch uses `contents: read` to
freshly resolve and read the fixed source and validates provenance, schema,
bindings, expiry, and `live_enabled: true` before Attempt creation. Immediately
before Capability Admission it repeats that fresh source read, requires
provenance/content identity with the admitted Decision, and blocks the current
Attempt on `live_enabled: false`, expiry, change, or invalidation. Governance
restoration requires a new Attempt. The LLD's publisher-side repetition is
defense in depth only. Runtime does not enumerate current writers or GitHub
Packages grants. No repository variable or additional token permission is
used. A dependency-policy violation also blocks live use and reopens the
exception.

## Dependency-Ordered Implementation Commits

Each commit must be independently reviewable. The implementation PR contains
the complete disabled v3 slice, acceptance bootstrap, and retirement of both
legacy Buddy entry files. Its merge begins the direct cutover and expected
outage; no legacy Buddy compatibility is preserved. v1 Official and CI remain
unchanged.

1. **Add v3 package skeleton, canonical record primitives, caller-selected
   admission modes, and the path-triggered root-HK v3 pytest step.**
2. **Add strict contracts, binding fixtures, and negative admission tests.**
3. **Add descriptors, static catalogs, Node/NBGV Provider, Repository Model
   compiler, exact-target full-history/tag checkout and shallow-history
   rejection controls, exact fixed Governance-source fields and attestation
   schema, and exact-target Live Eligibility Decision.**
4. **Add first-slice project tests, canonical in-tarball target witness, and
   isolated Node Build/Quality Adapters.**
5. **Add CI Planner, Evidence, Finalizer, shadow pull-request check, manual
   `slice-validation`, and the permanent repository-wide smoke-package
   consumer-policy gate; do not add a v3 Ruleset required check.**
6. **Add Release identities, two-snapshot planning, qualification, simulation,
   and the Official simulation workflow.**
7. **Add npm observation Adapters and SHA-512 exact-state acceptance tests.**
8. **Add Buddy approval, authorization/failure Evidence, publication Adapter,
   immutable reviewer-summary artifact, reusable-workflow permission ceilings,
   credential-free capability admission with immediate Governance-freshness
   revalidation and publisher-side defense in depth, diagnostic-only rejection
   handling, Execution history admission, platform-termination handling,
   capability-group result bundle, Receipt/finalization, caller-held Attempt
   concurrency, and permission-negative tests with live activation still
   disabled.**
9. **Add CODEOWNERS final-match coverage and tests for the v3 package,
   `eng/workflow-delivery/v3/**`, all descriptors, HK surfaces, root Python
   workspace inputs, workflows, actions, scripts, and the exact protected
   Governance document
   `/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`, all
   resolving finally to `@hcoona`.**
10. **Add the temporary protected acceptance bootstrap, the protected
    attestation `live_enabled` control, post-activation re-attestation
    procedures, independent `github.run_attempt == 1` guards on every probe,
    terminal evidence capture with
    `always() && github.run_attempt == 1`, incomplete/unknown reconciliation
    classification, and Governance inspection tooling with normal live
    disabled.**
11. **Retire both `buddy.yml` and `release-buddy.yml` entry files in the same
    implementation PR without adding `legacy-buddy.yml`; remove or rewrite
    Buddy-only acceptance rows, node IDs, and tests; split mixed Buddy/Official
    assertions while retaining Official coverage; add negative tests proving no
    legacy Buddy route exists; update active v1 topology/rollout documentation
    and `MEMORY.md`; preserve v2 and v1 Official/CI assets, including shared
    Official/CI tests, but explicitly exclude and retire or rewrite legacy Buddy
    workflows, Buddy-specific tests/matrices, and Buddy docs; and require root
    HK success. Merge starts the direct cutover and intentional Buddy outage.**
12. **After merge, freeze dispatch, repository-wide disable both old workflow
    identities, cancel or drain all old executions, and prove old-ref dispatch
    rejection.**
13. **Run and capture fixed-coordinate acceptance probes, remove the temporary
    workflow/bypass/Environment, and verify removal; reject every rerun attempt
    while retaining first-attempt dependency failures and ambiguous mutation
    evidence through the terminal always-run capture job, and require a newly
    reviewed invocation with a new disposable coordinate/version after failure;
    keep normal live and all Buddy publication disabled, legacy Buddy retired,
    and reconcile incomplete/unknown probe state.**
14. **After separate human approval, activate v3 live only for the named smoke
    package through a protected commit setting `live_enabled: true`; former
    Buddy projects remain unsupported, while v1 Official and CI assets remain
    unchanged.**

## Open LLD Decisions

No additional product or architecture decision is requested.
Platform acceptance must determine whether GitHub Packages supplies only
create-only conflict semantics or a provable atomic create-or-exact result. That
finding changes Adapter capability classification, not the confirmed domain
model.

The LLD approval gate is satisfied. Implementation and acceptance remain
subject to the dependency order and activation gates above.
