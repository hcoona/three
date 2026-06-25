# Workflow Release Plan Shape

## Purpose

This page defines the exact authoritative planner output shape for the
planner/build/publish boundary architecture. It builds directly on the Group 1 descriptor and
shared target-instance catalog schema.

## Design Summary

- The authoritative planner artifact is a `release-plan` object with exactly
  four top-level keys: `api-version`, `kind`, `envelope`, and `graph`.
- `envelope` carries the resolved release request plus selected-project
  snapshots. It is not an executable graph.
- `graph` carries the normalized ID-addressable execution objects: `variants`,
  `artifacts`, `publish-nodes`, and `target-instance-snapshots`.
- Planner-assigned IDs are opaque business-wise but lexically
  deterministic from canonical normalized identity inputs; `envelope.plan-id`
  keys resolved request/selection scope rather than every emitted-plan
  difference. Immutable package-registry proof reuse therefore uses the
  planner-frozen (`publish-node-id`, `artifact-id`,
  `resolved-publish-identity.package-name`,
  `resolved-publish-identity.version`) member binding rather than `plan-id`
  alone.
- The plan is execution-authoritative for descriptor-owned, catalog-owned, and
  planner-derived resolved publish identity, desired publish state, and
  publish-disposition data.
  Execution may still read the
  checked-out repository sources and manifests, but it must not re-read
  `three.release.yml` files or `eng/release/target-instances.yml`.

## Authoritative Top-Level Shape

Most normalized collections are mappings keyed by plan IDs rather than arrays; the request project lists are the single-entry array exception.

```yaml
api-version: three.release.plan/v1alpha1
kind: release-plan

envelope:
    plan-id: ...
    profile: buddy
    commit-sha: ...
    request-flags:
        force: false
    requested-project-ids: [hjg-pngcs]
    selected-project-ids: [hjg-pngcs]
    authoring-inputs:
        descriptor-api-version: three.release/v1alpha1
        catalog-path: eng/release/target-instances.yml
    projects:
        hjg-pngcs:
            display-name: Hjg.Pngcs
            ecosystem: dotnet
            release-kind: lib
            descriptor-path: src/public/lib/Hjg.Pngcs/three.release.yml
            release-root: src/public/lib/Hjg.Pngcs
            source:
                primary-manifest-path: src/public/lib/Hjg.Pngcs/Hjg.Pngcs.csproj
                auxiliary-input-paths: []
                version-authority-kind: build-system-nbgv
            resolved-version: 1.2.3
            variant-ids: [variant/...]
            publish-node-ids: [publish-node/release]
graph:
    variants:
        variant/...:
            project-id: hjg-pngcs
            descriptor-handle: package
            dimensions: {}
            artifact-ids: [artifact/package, artifact/symbols]
    artifacts:
        artifact/package:
            project-id: hjg-pngcs
            variant-id: variant/...
            descriptor-handle: nuget
            role: primary-package
            kind-family: package
            concrete-kind: nuget
            produced-from-artifact-ids: []
        artifact/symbols:
            project-id: hjg-pngcs
            variant-id: variant/...
            descriptor-handle: snupkg
            role: symbols
            kind-family: package
            concrete-kind: snupkg
            produced-from-artifact-ids: []
    publish-nodes:
        publish-node/release:
            project-id: hjg-pngcs
            profile: buddy
            descriptor-target-index: 0
            target-instance-snapshot-id: github-release/public
            artifact-ids: [artifact/package, artifact/symbols]
            publish-disposition: publish
            publish-mode: create-only
            resolved-publish-identity:
                release-tag: release/hjg-pngcs/v1.2.3
            desired-publish-state:
                release-state: prerelease
            projection:
                asset-names-by-artifact-id:
                    artifact/package: IO.Github.Hcoona.Pngcs.1.2.3.nupkg
                    artifact/symbols: IO.Github.Hcoona.Pngcs.1.2.3.snupkg
                asset-labels-by-artifact-id: {}
            attestation:
                signer-workflow: hcoona/three/.github/workflows/release-orchestrate.yml
    target-instance-snapshots:
        github-release/public:
            family: github-release
            instance-id: public
            catalog-ref: github-release/public
            contract:
                id: github-release-assets
                allowed-artifact-tuples:
                    - role: primary-package
                      kind-family: package
                      concrete-kind: nuget
                    - role: symbols
                      kind-family: package
                      concrete-kind: snupkg
                    - role: primary-package
                      kind-family: package
                      concrete-kind: wheel
                    - role: primary-package
                      kind-family: package
                      concrete-kind: sdist
                    - role: primary-package
                      kind-family: package
                      concrete-kind: npm-package
                    - role: primary-package
                      kind-family: package
                      concrete-kind: browser-zip
                    - role: sources
                      kind-family: archive
                      concrete-kind: sources-zip
                    - role: primary-package
                      kind-family: package
                      concrete-kind: rubygem
                    - role: primary-binary
                      kind-family: binary
                      concrete-kind: executable
                    - role: installer
                      kind-family: installer
                      concrete-kind: inno-setup
                aggregate-rules:
                    min-artifact-count: 1
                    max-artifact-count: null
                    cross-variant-policy: allow
                    tuple-rules:
                        - role: primary-package
                          kind-family: package
                          concrete-kind: nuget
                          min-count: 0
                          max-count: null
                        - role: symbols
                          kind-family: package
                          concrete-kind: snupkg
                          min-count: 0
                          max-count: null
                        - role: primary-package
                          kind-family: package
                          concrete-kind: wheel
                          min-count: 0
                          max-count: null
                        - role: primary-package
                          kind-family: package
                          concrete-kind: sdist
                          min-count: 0
                          max-count: null
                        - role: primary-package
                          kind-family: package
                          concrete-kind: npm-package
                          min-count: 0
                          max-count: null
                        - role: primary-package
                          kind-family: package
                          concrete-kind: browser-zip
                          min-count: 0
                          max-count: null
                        - role: sources
                          kind-family: archive
                          concrete-kind: sources-zip
                          min-count: 0
                          max-count: null
                        - role: primary-package
                          kind-family: package
                          concrete-kind: rubygem
                          min-count: 0
                          max-count: null
                        - role: primary-binary
                          kind-family: binary
                          concrete-kind: executable
                          min-count: 0
                          max-count: null
                        - role: installer
                          kind-family: installer
                          concrete-kind: inno-setup
                          min-count: 0
                          max-count: null
            destination:
                host: github
                owner: hcoona
                repo: three
            capabilities:
                mutability: mutable-prerelease
                name-uniqueness-scope: release-tag
                version-uniqueness-rule: tag
                profile-coexistence-rule: not-applicable
                credential-posture: github-token
                publish-topology: github-token
```

## Envelope Contents vs Graph Contents

The envelope is the authoritative resolved request summary for one selected
profile and one selected commit. Its `plan-id` identifies that
request/selection scope, not every planner-authored difference in the emitted
plan. The envelope contains only data that stays at project or whole-plan
scope:

- `plan-id` as the deterministic request/selection identity, plus
  `profile`, `commit-sha`, and normalized `request-flags`;
- `requested-project-ids` and `selected-project-ids`, the single-entry project
  lists selected by the active `project` workflow input after
  descriptor/catalog resolution. Their plural shape is the implementation
  contract used by the planner and fixtures, not multi-project operator
  dispatch;
- `request-flags`, which in `v1alpha1` has the exact normalized shape
  `{ force: <bool> }`, so `false` is the canonical default when no `FORCE`
  behavior was requested;
- `authoring-inputs`, which identify the author-time schema version and shared
  catalog path used for planning;
- `projects`, keyed by descriptor-owned `project.id`.

If dry-run or validation-only selection is ever reintroduced, it stays in the
control-plane run envelope rather than in `request-flags`, so it does not
change `envelope.plan-id` or the whole-release rerun identity.

In current scope for manual `workflow_dispatch`, `envelope.commit-sha` is the
pinned release commit. When `target` is empty, that commit is the GitHub UI
dispatch ref/commit. When `target` is non-empty, the control plane resolves the
supplied branch, tag, ref, or 40-hex SHA exactly once and records the resulting
commit. Later control-plane and executor stages must remain pinned to that
release commit rather than following any source ref after dispatch; workflow code
itself continues to run from the trusted dispatch ref.

Each `envelope.projects[project-id]` snapshot freezes the selected project
fields that later control-plane and execution design will need without re-
reading descriptors:

- `display-name`, `ecosystem`, and `release-kind` from the descriptor `project`
  section;
- `descriptor-path` and `release-root` as repo-root-relative paths;
- `source.primary-manifest-path` and `source.auxiliary-input-paths` as fully
  resolved repo-root-relative file paths;
- `source.version-authority-kind`, copied from the descriptor or defaulted by
  the schema's current-scope rules;
- `resolved-version`, the planner-frozen project-scoped version identity for the
  selected run;
- `variant-ids` and `publish-node-ids` as backlinks into the normalized graph.

`project` remains an owning scope rather than a first-class graph entity. The
plan therefore keeps project snapshots in the envelope and keeps only reusable
execution objects in the normalized graph.

## Normalized Graph Shape

### `graph.variants`

Each variant object contains:

- `project-id`;
- `descriptor-handle`, copied from `variants[].id` for diagnostics only;
- `dimensions`, copied from the descriptor and therefore remaining the semantic
  variant identity;
- `artifact-ids`, the plan artifact objects owned by that variant.

### `graph.artifacts`

Each artifact object contains:

- `project-id` and `variant-id` ownership anchors;
- `descriptor-handle`, copied from `artifacts[].id` for diagnostics only;
- `role`, `kind-family`, and `concrete-kind`;
- `projection`, normalized from descriptor artifact-level projection when the
  artifact kind permits projection;
- `produced-from-artifact-ids`, normalized from descriptor-local artifact
  handles to plan artifact IDs.

In the frozen plan, `artifact-id` is the planner-defined fulfillment slot for
that semantic artifact obligation. It is not a filename, path, bundle location,
or executor command recipe. The planner therefore owns the exact `artifact-id`
set, tuple metadata, ownership, `produced-from-artifact-ids`, and publish-node
consumption edges, while execution later proves fulfillment of each slot.

### `graph.publish-nodes`

Each publish node contains:

- `project-id`;
- `profile`, repeated from the envelope so one node remains self-describing when
  inspected in isolation;
- `descriptor-target-index`, the zero-based ordinal of the source target usage
  inside the selected `profiles.<profile>.targets` list;
- `target-instance-snapshot-id`;
- `artifact-ids`, resolved from descriptor-local artifact handles to plan
  artifact IDs and kept in descriptor-declared order;
- `publish-disposition`, the planner-authoritative action for that publication
  intent, with current-scope values `publish` and `skip-satisfied`;
- `publish-mode`, present only when `publish-disposition` is `publish`, with
  current-scope values `create-only`, `overwrite-mutable`, and
  `replace-authoritative`;
- `resolved-publish-identity`, the planner-resolved external publication
  identity used for destination uniqueness and replay checks;
- `desired-publish-state`, present only for families that need planner-owned
  desired target-side state;
- `projection`, normalized to the selected target family;
- `attestation`, present and required only for `github-release` nodes.

Current-scope normalized `resolved-publish-identity` shapes are:

| Resolved family                                 | Plan `resolved-publish-identity` shape        |
| ----------------------------------------------- | --------------------------------------------- |
| `github-release`                                | `release-tag: <string>`                       |
| active `npm`, `pypi`, and `rubygems` registries | `package-name: <string>`; `version: <string>` |
| deferred NuGet registry support                 | `package-name: <string>`; `version: <string>` |

For package registries, the planner resolves the final `package-name` after any
descriptor-side projection override or fallback to manifest-owned intrinsic
package naming and resolves `version` from the selected project's
planner-frozen `resolved-version` for the selected run. In current scope, that
resolved project version comes from build-system-integrated NBGV for every
project except the single `nbgv-python` special-support path, which instead
uses the selected commit's checked-in `pyproject.toml` `[project].version`.
The active current-scope package-name resolution and identity-equivalence
contract is:

| Family     | Planned `package-name` source and serialization                                                                                                                                                                                                                                                                             | Name equivalence for remote lookup and same-identity classification                                                | Version equivalence for remote lookup and same-identity classification                                                                    |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `pypi`     | Serialize `[project].name` after PyPI / PEP 503 normalization: lowercase, then replace each maximal run of `.`, `-`, or `_` with one `-`.                                                                                                                                                                                   | Compare the PEP 503 normalized project name.                                                                       | Compare using normalized Python package version identity under the Python packaging version-specifier rules.                              |
| `npm`      | Serialize artifact-level `projection.package-name` when declared, otherwise target-level `projection.package-name` when declared as the single-artifact compatibility shorthand, otherwise serialize `package.json` `name`. The resolved value must be a valid publishable npm package name and lowercase in current scope. | Compare the serialized npm package name exactly after npm package-name validation; scoped names include the scope. | Compare the canonical `node-semver` package version identity for the serialized frozen version and the observed package metadata version. |
| `rubygems` | Serialize the evaluated `.gemspec` `Gem::Specification.name`; current-scope gem names must be lowercase.                                                                                                                                                                                                                    | Compare the serialized gem name exactly after RubyGems name validation.                                            | Compare through RubyGems `Gem::Version` equality for the frozen version and the observed gem metadata version.                            |

NuGet registry publication is deferred in the active catalog/routing. If a
reviewed NuGet registry path is added later, it must serialize the evaluated
primary `.csproj` `PackageId`, reject absent or empty `PackageId`, avoid
NuGet/MSBuild `AssemblyName` or directory-name fallbacks, compare package IDs
case-insensitively while preserving the serialized spelling, and compare versions
using NuGet normalized package-version identity.

Family-specific canonicalization is part of planner-owned publish identity and
remote-state classification. It must not be re-derived differently by executors
or by later workflow jobs. For GitHub Release, the planner resolves both the
final project-scoped `release-tag` and `desired-publish-state.release-state`
before serializing the plan. In current scope, GitHub Release tags use the
repositories existing shape `release/<project.id>/v<resolved-version>`, matching
observed tags such as `release/nbgv-python/v2.0.0`,
`release/steam-account-history-to-csv/v1.1.1`, and
`release/hexo-renderer-asciidoc/v3.1.0-beta.11.g3f78566`, plus the root
`version.json` allowance for `^refs/tags/release/.+/v.+$`. Different projects on
the same commit therefore serialize different `release-tag` values and map to
different GitHub Release objects when their `project.id` values differ.
Execution and later replay checks must treat serialized
`resolved-publish-identity` and `desired-publish-state` as authoritative for
that plan rather than re-deriving them from commit, profile, or manifest
inputs.

Current-scope normalized `desired-publish-state` shapes are:

| Resolved family                                 | Plan `desired-publish-state` shape     |
| ----------------------------------------------- | -------------------------------------- |
| `github-release`                                | `release-state: prerelease \| release` |
| active `npm`, `pypi`, and `rubygems` registries | omitted                                |
| deferred NuGet registry support                 | omitted                                |

GitHub Release `desired-publish-state.release-state` is planner-owned desired
target-side state. It must not be copied into `projection`.

Current-scope normalized `projection` shapes are:

| Plan object       | Scope                      | Shape                                                                                                                                                                                                                                                                                                                                            |
| ----------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `graph.artifacts` | npm package artifacts only | `projection.package-name?: <string>`                                                                                                                                                                                                                                                                                                             |
| `publish-nodes`   | `github-release`           | `asset-names-by-artifact-id: { <artifact-id>: <string> }`; `asset-labels-by-artifact-id: { <artifact-id>: <string> }`; compatibility-only `asset-sizes-by-artifact-id?` and `asset-sha256-by-artifact-id?` are not planner authority; current size/SHA authority comes from build-result/publish handoff and GitHub Release asset proof evidence |
| `publish-nodes`   | `npm`                      | `package-name?: <string>`; `final-distribution-filenames-by-artifact-id: { <artifact-id>: <string> }`; compatibility-only `final-distribution-digests-by-artifact-id?` is not planner authority; current digest authority comes from build-result/publish handoff and proof evidence                                                             |
| `publish-nodes`   | `pypi`                     | `final-distribution-filenames-by-artifact-id: { <artifact-id>: <string> }`; compatibility-only `final-distribution-sha256-by-artifact-id?` is not planner authority; current digest authority comes from build-result/publish handoff and proof evidence                                                                                         |
| `publish-nodes`   | `rubygems`                 | `final-distribution-filenames-by-artifact-id: { <artifact-id>: <string> }`; compatibility-only `final-distribution-sha256-by-artifact-id?` is not planner authority; current digest authority comes from build-result/publish handoff and proof evidence                                                                                         |

Current-scope normalized `attestation` shapes are:

| Resolved family                                 | Plan `attestation` shape           |
| ----------------------------------------------- | ---------------------------------- |
| `github-release`                                | `signer-workflow: <full-identity>` |
| active `npm`, `pypi`, and `rubygems` registries | omitted                            |
| deferred NuGet registry support                 | omitted                            |

For GitHub Release, descriptor-side `projection.asset-labels` keys are resolved
from descriptor-local `artifact.id` handles into plan artifact IDs before the
plan is serialized. The planner must also serialize
`projection.asset-names-by-artifact-id` with exactly one target-side asset
basename for every `artifact-id` in the node's full `artifact-ids` membership.
Those names are planner-owned remote-member matching keys and live upload names;
they are not bundle paths, executor output paths, or replacements for
`artifact-id`. Release-state remains outside `projection` and belongs only in
`desired-publish-state`.
Compatibility plans may include paired GitHub Release asset size and SHA-256
maps, but current planners do not derive authoritative release-asset bytes.
Current exactness and mutation gates must use producer-bound build-result,
publish handoff, and `github-release-asset-proof` evidence; size/SHA API
evidence alone is not authoritative exact proof. Missing proof evidence is
intentionally non-exact and must not degrade to name-only matching.

Current-scope GitHub Release publish nodes must also serialize
`attestation.signer-workflow`. The value is the full GitHub CLI signer workflow
identity used by `gh attestation verify --signer-workflow`, not a bare filename.
For the current `github-token` topology, the GitHub Release upload path and the
preceding `actions/attest-build-provenance` attestation jobs are hosted by
`.github/workflows/release-orchestrate.yml`, so the frozen value is
`hcoona/three/.github/workflows/release-orchestrate.yml`. If a
successor topology moves GitHub Release attestation to another workflow, that
topology must freeze its corresponding full signer workflow identity in the plan
before any proof lookup can be considered admissible.

Every serialized publish node carries an embedded `publish-node-id` equal to its
key in `graph.publish-nodes`. Publish request materialization must copy both the
top-level selected `publish-node-id` and the embedded node snapshot, and contract
validation rejects any mismatch so an executor cannot report one node id while
executing another node payload.

Current-scope GitHub Release asset-name derivation is closed and descriptor-
independent:

- package artifacts use the ecosystem package basename that the planner can
  compute for the selected project and resolved version:
    - `nuget` and `snupkg` use the same NuGet filename formulas as deferred
      NuGet registry support;
    - `wheel` and `sdist` use the same metadata-only PyPI filename projection as
      the PyPI projection;
    - `npm-package` uses the npm pack tarball basename for the resolved npm
      package name and resolved version;
    - `rubygem` uses `<gem-name>-<version>.gem` after RubyGems name and version
      resolution.
- `binary/executable` uses
  `<project-id>-<resolved-version>-<variant-token>` for non-Windows variants and
  `<project-id>-<resolved-version>-<variant-token>.exe` for Windows variants.
- `installer/inno-setup` uses
  `<project-id>-<resolved-version>-<variant-token>-setup.exe`.

`variant-token` is the hyphen-joined, lexicographically key-sorted
`dimensions` values for the owning variant; an empty dimensions map serializes
as `default`. The planner must reject a GitHub Release publish node if the
derived asset names are not unique within that node.

For npm, artifact-level `projection.package-name` is descriptor-owned artifact
projection data. The planner serializes it on `graph.artifacts[artifact-id]`
only when the descriptor artifact declared that override. Target-level npm
`projection.package-name` remains a compatibility shorthand for a selected
single-artifact npm target usage whose artifact omitted artifact-level
projection. When that shorthand is declared, the planner also serializes it as
the publish-node `projection.package-name` so the control plane can materialize
legacy target-level npm projection into build requests. The final external npm
package name always belongs in `resolved-publish-identity.package-name`, whether
it came from artifact-level projection, target-level shorthand, or the manifest
fallback.

The npm dual-artifact model represents two package names as two planned npm
artifacts. For each npm artifact, the planned package tarball's embedded
`package/package.json` `name` must equal the artifact's resolved package name.
Publishing one built npm tarball under a second package name remains invalid;
the build bundle must instead contain the separately built projected tarball for
each planned npm artifact, such as the dedicated
`hcoona-release-smoke-npm-dual` smoke project that exercises an unscoped
official/npmjs artifact and a scoped buddy/GitHub Packages artifact.

For active current-scope `npm`, `pypi`, and `rubygems`, the planner must
deterministically determine one final distribution filename per planned artifact
before any remote-state classification:

- for `npm`, using `resolved-publish-identity.package-name` and
  `resolved-publish-identity.version` with npm pack's tarball basename rules for
  scoped and unscoped packages;
- for `pypi`, by projecting the selected project's normalized package name and
  PEP 440 version into the final wheel/sdist basename without invoking a build
  backend or hashing generated outputs. This filename projection is separate
  from PyPI remote package identity canonicalization in
  `resolved-publish-identity.package-name`. Current scope still limits each
  `pypi-publish` node to exactly one wheel and zero or one sdist, all from the
  same variant. Multi-wheel, cross-variant, and platform-specific wheel layouts
  are deferred.
- for `rubygems`, using the evaluated gem name and resolved version with the
  RubyGems filename formula `<gem-name>-<version>.gem`.

Deferred NuGet registry nodes must use `resolved-publish-identity.package-name`,
`resolved-publish-identity.version`, and the artifact's `concrete-kind`, with
the exact formulas `<package-name>.<version>.nupkg` for `nuget` and
`<package-name>.<version>.snupkg` for `snupkg`, if reviewed NuGet catalog/routing
later makes those nodes active.

For current-scope immutable package registries that classify one or more remote
members under one resolved `{ package-name, version }` identity, the planner
must serialize `projection.final-distribution-filenames-by-artifact-id` as the
planner-frozen final distribution filename map. That map must contain exactly
one entry for every `artifact-id` in the node's full `artifact-ids`
membership, including singleton nodes, and the map values must be unique within
that publish node. These filenames are family-specific remote-member matching
keys only: they do not redefine `artifact-id`, which remains the planner-owned
fulfillment slot rather than a filename, path, or command recipe. For each
`artifact-id` a live publish node publishes, the actual target-side uploaded
member filename must equal that planner-derived final filename; the executor
must consume the frozen value rather than re-derive an alternate filename, and
may satisfy the upload rule by using a bundle file that already has that basename
or by staging/renaming to that exact filename before upload. Any mismatch must
fail closed.

For PyPI publish nodes, exact replay satisfaction compares the planned filename
slots against destination evidence and producer-bound build-result/publish proof
digests for the actual built distributions. Compatibility
`projection.final-distribution-sha256-by-artifact-id` data is not current
authority. Missing producer-bound digest evidence, missing observed digest
evidence, or digest conflict is never an exact replay satisfaction.

For npm publish nodes, exact replay satisfaction compares the planned filename
slots against destination evidence and producer-bound build-result/publish proof
digests for the actual built tarball. Compatibility
`projection.final-distribution-digests-by-artifact-id` data is not current
authority. Normal npm registry evidence is `dist.integrity` with SHA-512 SRI, so
exact replay requires comparable producer-bound and remote algorithms to match.
If no comparable producer-bound/remote algorithm is available, the existing
version is non-exact.

For RubyGems publish nodes, exact replay satisfaction compares the planned
filename slot against destination evidence and producer-bound build-result/
publish proof digests for the actual built gem. Compatibility
`projection.final-distribution-sha256-by-artifact-id` data is not current
authority. RubyGems exact replay with RubyGems `sha` evidence requires every
matching planned filename to have matching producer-bound SHA-256 evidence;
missing producer-bound SHA-256, missing remote `sha`, or a digest mismatch is
non-exact or conflicting according to the immutable registry classification
rules.

For npm publish nodes, the build result bundle must contain the projected
tarball for the referenced `artifact-id` under the planner-derived final
filename. The npm publish executor is identity-verification-only for package
projection: it verifies that the tarball basename and embedded
`package/package.json` `name` match the frozen plan identity, then publishes
that tarball. It must not rewrite `package.json`, repack, or synthesize a second
package identity during publication.

`publish-disposition: publish` means execution should attempt the publication
intent represented by that node. `publish-mode: create-only` means the executor
must attempt a normal non-overwrite publication for the node's full
`artifact-ids` set. `publish-mode:
overwrite-mutable` means the executor must perform the planner-authorized
overwrite behavior for a mutable current-scope buddy target rather than
inventing its own overwrite policy from raw inputs. `publish-mode:
replace-authoritative` means the executor must converge a same-tag GitHub
Release node to the planner-owned full official publish intent, including
`desired-publish-state.release-state`, `artifact-ids`, and
`projection.asset-names-by-artifact-id` plus
`projection.asset-labels-by-artifact-id` and producer-bound build-result/proof
size/SHA-256 evidence, rather than treating promotion as a state-only flip or
additive merge. The asset set must be converged before the executor promotes the
remote release state from prerelease to release.
`publish-disposition: skip-satisfied` means planner-time validation already
proved that the destination state satisfies that full publish intent for this
run, so the plan records a no-op publish node rather than reserializing raw
remote observations. `resolved-publish-identity` is the planner-frozen external
publish identity that those checks refer to.

Current-scope planner classification uses these exact remote-observation terms
before any row in the matrix below is applied:

- **absent**: no remote publication exists for the node's
  `resolved-publish-identity`;
- **exact-satisfied**: a remote publication exists for that same identity and
  exactly matches the node's full planner-owned publish intent;
- **partial**: a remote publication exists for that same identity, is not
  `exact-satisfied`, and the planner can still normalize it into a structured
  same-identity subset case rather than an irreducible conflict;
- **partial-authoritative**: a same-tag GitHub Release exists for the selected
  official publish identity, the remote object is still `prerelease`, the node's
  desired state is `release`, and the remote asset names, labels, or
  attestation-backed asset content proof do not yet match the producer-bound
  official intent. This is the only non-exact observation that can enter
  `replace-authoritative`;
- **conflicting**: a remote publication exists for that same identity, is not
  `exact-satisfied`, and current-scope target semantics allow no non-error
  replay outcome for that observed state regardless of request flags, so the
  planner must fail for human intervention.

These classes are mutually exclusive: every remote observation reduces to
exactly one of `absent`, `exact-satisfied`, `partial`,
`partial-authoritative`, or `conflicting`, and the replay matrix below must
consume that already-chosen class rather than reclassifying the observation per
row. Request flags such as `FORCE` are evaluated only after this structural
classification step and therefore do not change whether one same-identity
observation is `partial`, `partial-authoritative`, or `conflicting`, except that
the reviewed `official` `force_update_tag` gate may prevent a release-tag
commit mismatch alone from becoming a pre-classification conflict so `ensure-tags`
can retarget the tag before executing the serialized publish mode.

For package registries, that classification is publish-node-wide for one
resolved `{ package-name, version }` identity and the planner-owned member set
for that node rather than per uploaded file in isolation.

Remote-query failure policy for that classification step is:

- the planner may use bounded retry for transient publish-destination query or
  normalization failures while deriving the remote observation for one selected
  publish node;
- if retry is exhausted, or the planner still cannot normalize the destination
  state into one of `absent`, `exact-satisfied`, `partial`,
  `partial-authoritative`, or `conflicting`, the planner must fail closed rather
  than guessing or degrading into a replay class;
- this includes persistent transport, authentication, authorization, rate-limit,
  malformed-response, or family-adapter interpretation failures that leave the
  remote state unclassifiable for the frozen publish intent;
- diagnostics for that failure should identify the affected
  `publish-node-id`, target family or instance, resolved publish identity, and
  failing phase (`query`, `normalization`, or `classification`) so the operator
  has a concrete human-intervention path;
- plan emission remains whole-request atomic: if any selected publish node that
  needs remote-state-dependent planning cannot be classified, the planner emits
  no partial plan and the entire planning request fails.

For GitHub Release, `exact-satisfied` requires all of the following for the
same `resolved-publish-identity.release-tag`:

- remote release state exactly equals `desired-publish-state.release-state`;
- the remote release contains exactly the planned asset names from
  `projection.asset-names-by-artifact-id`;
- each required asset name carries the planned label state from
  `projection.asset-labels-by-artifact-id`, where a missing map entry means no
  planned label;
- each required remote asset has content-equivalence evidence for the planned
  artifact: the planner can download the remote asset and verify an admissible
  GitHub Artifact Attestation whose subject name, digest, signer workflow, source
  repository, source digest, and predicate type match the frozen publish node,
  including `attestation.signer-workflow`, and selected commit; the remote asset
  size must match the downloaded file and any unexpired matching
  `github-release-asset-proof.json` wrapper as corroboration;
- no extra remote assets remain on that release object.

If GitHub Release remote asset download, attestation verification, digest
evidence, or size evidence is unavailable, unparsable, or mismatched for any
planned asset, the same-tag state is not `exact-satisfied`. The planner must
classify it as `partial` or `conflicting` under the same-tag rules below, or fail
closed when it cannot safely reduce the observation to one class. A same-name
asset is never sufficient evidence by itself.

For current-scope same-tag GitHub Release observations that are not
`exact-satisfied`, classification remains structural:

- when the target capability is `mutable-prerelease`, the same-tag observation
  is `partial` unless one of the authoritative `conflicting` cases below
  applies;
- the authoritative same-tag `conflicting` cases are limited to:
    - the remote release is already `release` while the frozen intent wants
      `prerelease`;
    - the frozen intent wants `release`, the remote release is already
      `release`, and the authoritative asset set, asset labels, or
      attestation-backed asset content proof is non-exact.

That means `buddy FORCE` and non-`FORCE` mutable replay consume one
already-chosen `partial` GitHub Release observation, while same-tag `official`
prerelease-to-release promotion consumes the already-chosen
`partial-authoritative` observation that feeds `replace-authoritative`.

For immutable package registries, the planner must classify same-identity remote
state with these current-scope rules:

- `absent`: none of the planned members for that resolved
  `{ package-name, version }` identity exist remotely, so the node may publish
  with `create-only`;
- before evaluating immutable `exact-satisfied`, `partial`, or `conflicting`
  for a same-identity observation, the planner must first establish remote-
  member ↔ planned-artifact matching for the node's full planned member set; in
  current scope for active npm, PyPI, RubyGems, and any future reviewed NuGet
  registry nodes, nodes match by the
  planner-frozen final distribution filename
  `projection.final-distribution-filenames-by-artifact-id[artifact-id]`,
  including singleton nodes and current-scope RubyGems nodes;
- content-equivalence for an already-present member is planner-provable only
  when the planner has an admissible exactness source for the corresponding
  planned member. In current PyPI, npm, and RubyGems paths, the admissible
  source is producer-bound build-result or publish/proof digest evidence matched
  against destination-reported digest evidence for the same planned final filename. For remaining or future immutable registry paths without a
  producer-bound build-result or publish handoff digest evidence, the admissible source is
  a planner-available digest from the control-plane-owned prior build receipt
  lookup/index for the same current planner-frozen immutable-proof member
  binding (`publish-node-id`, `artifact-id`,
  `resolved-publish-identity.package-name`,
  `resolved-publish-identity.version`); matching `envelope.plan-id` alone is
  not sufficient. Including the immutable resolved `{ package-name, version }`
  identity in that binding keeps receipt lookup version-sensitive for those
  paths. The planner-frozen
  `projection.final-distribution-filenames-by-artifact-id` map still serves only
  remote-member matching and classification; it is not the proof-binding key.
  The prior-receipt seam must yield `build-result` receipts together with
  authoritative control-plane provenance proving that the producing live build
  unit successfully emitted the receipt, whether the run was live versus any
  future dry-run or validation-only mode, and the run identity/attempt; no other
  prior-receipt proof source is admissible for that seam, which keeps receipt
  proof bound to the same planner-frozen versioned output slot. For any given
  immutable-proof member binding, the admissible receipt set must collapse to
  one digest, and if multiple admissible receipts exist with differing digests
  then receipt digest proof is unavailable; proof from any future dry-run
  receipts or other immutable-proof bindings is invalid for immutable
  `exact-satisfied` / `partial` classification;
- if that digest proof is unavailable for any member needed to distinguish
  `exact-satisfied`, `partial`, or `conflicting`, the planner must fail closed
  rather than infer equivalence from unfrozen filenames, handles, or descriptor
  shape;
- `exact-satisfied`: after that member-key matching step, the remote member set
  for that identity exactly matches the full planner-owned member set with
  content-equivalent matched members and no extras, so the node becomes
  `skip-satisfied`;
- `partial`: after that member-key matching step, the remote member set is a
  proved additive non-empty proper subset: all already-present planned members
  are content-equivalent to the planned members, all still-missing planned
  members are truly absent, and there are no extra unplanned remote members for
  that same identity; current scope still treats this as planner-error rather
  than a live publish case;
- `conflicting`: any same-identity observation outside the `partial`
  proved-subset case, including remote members that cannot be matched
  one-to-one by that frozen member key, non-equivalent existing members, extra
  unplanned members, or remote state that cannot be normalized and proved into
  a safe additive subset.

Immutable same-identity handling remains conservative in current scope when the
planner has strong registry evidence. The planner may emit a live publish only
for `absent`; both immutable `partial` and immutable `conflicting` must fail
closed for human intervention. GitHub Packages npm/RubyGems version presence is
not strong enough planner evidence by itself: those nodes remain live and the
idempotent publisher performs exact digest verification or hard-fails on a
conflict.

In current scope, active public npm and RubyGems registries are single-member
families, so immutable `partial` is structurally unreachable there. Deferred
NuGet registry support can observe `partial` because one publish node may own
multiple remote members for the same resolved package identity, but that behavior
is conditional on adding reviewed NuGet catalog/routing. PyPI can also observe
`partial`, but only for the narrowed current-scope member set of exactly one
wheel and zero or one sdist under one resolved package identity. Those active
strong-evidence observations remain planner-error in current scope.

In current scope, `official-frozen` is a planner-time predicate over one
selected project and its resolved version identity. It becomes true only when
that same project has already succeeded at the `official` GitHub Release publish
intent for the same project-scoped `resolved-publish-identity.release-tag`.
Buddy prereleases, package-registry publication, or any alternate tag shape do
not make a version official-frozen, and no second freeze tag is introduced.

The planner must apply the following current-scope replay, authoritative-
replacement, and `FORCE` matrix before serializing publish nodes. Whole-request
planner-error rows below take precedence over all per-node outcomes even though
they appear later in the table. For each publish node, after reducing the
remote state to exactly one observation class, evaluate the per-node rows in
the table's listed order and stop at the first match. In the current ordering,
that means the `exact-satisfied` skip rows first, then the immutable fail-closed
rows, then the remaining per-node planner-error and live `publish-mode` rows.
An unclassifiable remote state does not enter this matrix;
it is a fail-closed planner error before any row evaluation.

| Condition                                                                                                                                                                                                                                                                                 | Planner outcome                                                                                                                                                                                                                                                                                                              |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A `github-release` target is `exact-satisfied` for the same `resolved-publish-identity.release-tag`, including exact release state, exact required asset set, exact asset labels, and content-equivalent remote assets for every planned artifact.                                        | Emit `publish-disposition: skip-satisfied`. Do not invoke a publish executor for that node on rerun.                                                                                                                                                                                                                         |
| An immutable target is `exact-satisfied` for the node's full publish intent.                                                                                                                                                                                                              | Emit `publish-disposition: skip-satisfied`. Do not invoke a publish executor for that node on rerun.                                                                                                                                                                                                                         |
| An immutable target is `partial` for the node's full publish intent, meaning the remote state is a proved additive non-empty proper subset with content-equivalent existing planned members, absent missing planned members, and no extra same-identity members.                          | Planner error. Current scope does not auto-complete immutable targets even when the same-identity subset relationship is proved.                                                                                                                                                                                             |
| An immutable target has a `conflicting` remote publication for the same immutable target identity.                                                                                                                                                                                        | Planner error. Do not emit a plan that asks executors to auto-complete, reconcile, or overwrite the immutable conflict.                                                                                                                                                                                                      |
| A `github-release` target has a `conflicting` same-tag remote publication because the observed same-tag remote state is `release` while the node desires `prerelease`, or it is already `release` with a non-exact official asset set, labels, or attestation-backed asset content proof. | Planner error. Current scope does not allow same-tag release-to-prerelease demotion or reinterpretation of an already authoritative official release.                                                                                                                                                                        |
| No existing publication is found for the node's `resolved-publish-identity`.                                                                                                                                                                                                              | Emit `publish-disposition: publish` with `publish-mode: create-only`.                                                                                                                                                                                                                                                        |
| A `github-release` target already contains the same `resolved-publish-identity.release-tag` and the observation is `partial-authoritative`.                                                                                                                                               | Emit `publish-disposition: publish` with `publish-mode: replace-authoritative`. This is the only current-scope same-tag official-promotion path. The planner must serialize this explicitly so execution converges the full official publish intent rather than treating promotion as a state-only change or additive merge. |
| A target whose capability `mutability` is `mutable-prerelease` already contains the same `resolved-publish-identity`, the observation is `partial`, and the node is in the planner-authorized buddy `FORCE` overwrite case.                                                               | Emit `publish-disposition: publish` with `publish-mode: overwrite-mutable`. The planner must serialize this explicitly rather than leaving mutable-target replay overwrite behavior for executors to infer from destination state.                                                                                           |
| A target whose capability `mutability` is `mutable-prerelease` already contains the same `resolved-publish-identity`, the observation is `partial`, and the planner-authorized buddy `FORCE` overwrite case does not apply.                                                               | Planner error. Current scope does not allow non-`FORCE` mutable replay to proceed as `create-only`; the planner must fail before execution rather than leaving existing publication handling ambiguous.                                                                                                                      |
| `profile: official` with `request-flags.force: true`.                                                                                                                                                                                                                                     | Valid only for the active reviewed `force_update_tag` release-tag retarget path. It does not authorize package-registry overwrite, immutable-target completion, or bypass of publish identity checks.                                                                                                                        |
| `profile: buddy` with `request-flags.force: true`, but any selected project resolves to an official-frozen project-scoped version identity.                                                                                                                                               | Planner error for the whole request. `buddy FORCE` is never valid for an official-frozen project or version identity.                                                                                                                                                                                                        |
| `request-flags.force: true` for a target whose capability `mutability` is not `mutable-prerelease`.                                                                                                                                                                                       | `FORCE` does not authorize overwrite for that node. Immutable targets still follow the skip-versus-error rules above; only mutable buddy targets may proceed with `publish-mode: overwrite-mutable`.                                                                                                                         |

### `graph.target-instance-snapshots`

Each target-instance snapshot contains:

- `family`, `instance-id`, and `catalog-ref`;
- `contract`, frozen inline rather than left as a catalog lookup;
- `destination`, copied from the shared catalog;
- `capabilities`, copied from the shared catalog, including
  `publish-topology`.

The normalized `contract` object uses this exact shape:

```yaml
contract:
    id: ...
    allowed-artifact-tuples:
        - role: ...
          kind-family: ...
          concrete-kind: ...
    aggregate-rules:
        min-artifact-count: <int>
        max-artifact-count: <int-or-null>
        cross-variant-policy: allow | forbid
        tuple-rules:
            - role: ...
              kind-family: ...
              concrete-kind: ...
              min-count: <int>
              max-count: <int-or-null>
```

This is the plan-time normalization of the Group 1 contract-compatibility table.
Execution therefore does not need to re-read the shared catalog or the author-
time compatibility rules to understand what one referenced target instance
means. The copied capability block also freezes the trusted-publisher topology
selector that the control plane uses to schedule live publish paths without
registry-specific inference after planning.

## Plan IDs, Ownership, and References

The plan uses four kinds of identifiers:

- `envelope.plan-id`: the top-level request/selection identity for the
  emitted plan, not a hash of every serialized plan field. It is serialized as
  `plan/<hex-sha256>`, where the digest input is the canonical JSON object
  `{ profile, commit-sha, selected-project-ids, request-flags }` after
  `selected-project-ids` has resolved to the active single project list and
  `request-flags` has been normalized to the exact current-scope key set. In
  `v1alpha1`, `plan-id` is the authoritative whole-release rerun identity rather
  than being paired with a separate request-id. Changing `request-flags.force`
  therefore changes `plan-id` even when the selected commit and project scope do
  not change. `plan-id` deliberately excludes actor, run id, approval state,
  timestamps, raw remote observations, and planner-authored outcomes such as
  `publish-disposition` or `publish-mode`. Two materially different emitted
  plans may therefore share the same `plan-id` when they came from the same
  resolved request/selection scope. `v1alpha1` defines no separate full-emitted-
  plan hash field.
- `project-id`: the descriptor-owned `project.id` key used by
  `envelope.projects`, `requested-project-ids`, `selected-project-ids`, and
  every project-scoped graph object.
- `variant-id`, `artifact-id`, and `publish-node-id`: planner-assigned opaque
  string IDs with deterministic lexical form. The planner must emit them as
  `variant/<hex-sha256>`, `artifact/<hex-sha256>`, and
  `publish-node/<hex-sha256>` from the canonical identity payloads below. Later
  layers must treat them as equality-only references rather than parseable
  business keys. In particular, `artifact-id` is a fulfillment slot reference,
  not an implied output filename, path, or command template.
- `target-instance-snapshot-id`: exactly the resolved catalog reference string
  `family/instance-id`, because one snapshot exists per referenced catalog
  target instance per plan.

Canonical ID generation rules:

- The planner must canonicalize each ID payload as UTF-8 JSON with object keys
  sorted lexicographically, no insignificant whitespace, repo-root-relative path
  strings already normalized, and arrays preserved only in their already-defined
  semantic order.
- For the same normalized identity payloads, the planner must emit the
  same lexical `plan-id`, `variant-id`, `artifact-id`, and `publish-node-id`
  values on every run. Other planner-authored outcomes may change without
  changing those IDs when they are outside the documented identity payloads.
- The canonical identity payloads are:
    - `variant-id`: `{ project-id, dimensions }`.
    - `artifact-id`:
      `{ project-id, variant-id, role, kind-family, concrete-kind }`.
    - `publish-node-id`:
      `{ project-id, profile, descriptor-target-index, target-instance-snapshot-id, artifact-ids, projection }`.
- Because `publish-node-id` includes normalized `projection` but excludes
  `resolved-publish-identity`, a target-level npm `projection.package-name`
  compatibility shorthand participates in the npm publish-node ID, while an
  artifact-level or manifest-derived npm package name participates through the
  planner-frozen final filename map. Current-scope GitHub Release nodes always
  include the frozen asset-name map in `projection`, and active current-scope
  npm, PyPI, or RubyGems nodes always include the frozen filename map in
  `projection`, including singleton nodes; each map covers every `artifact-id`
  in the node's full `artifact-ids` membership. Those version-sensitive
  current-scope GitHub Release, npm, PyPI, and RubyGems
  `publish-node-id` values still
  participate in proof and rerun seams, but the immutable-package admissible
  binding remains the planner-frozen member tuple (`publish-node-id`,
  `artifact-id`, `resolved-publish-identity.package-name`,
  `resolved-publish-identity.version`); `envelope.plan-id` remains only
  request/selection scope identity.
- `descriptor-handle`, `display-name`, approvals, timestamps, remote
  observation payloads, `resolved-publish-identity`, `desired-publish-state`,
  `publish-disposition`, and `publish-mode` do not participate in these
  identity payloads. They may change emitted-plan detail or planner outcomes
  without changing the stable lexical IDs defined for the underlying
  request/selection scope or publish-node slot.
- Within every mapping-valued serialized collection in the plan, entries must be
  emitted in lexicographic key order. The request project-list fields
  (`requested-project-ids` and `selected-project-ids`) are the current
  single-entry array exception and must preserve that one-project order;
  list-valued fields with declared semantic order, such as `artifact-ids` on
  publish nodes, must preserve that semantic order.

Ownership and reference rules are:

- every variant, artifact, and publish node carries `project-id`;
- every artifact carries `variant-id`;
- every publish node references one `target-instance-snapshot-id` and one or
  more `artifact-id` values;
- every project snapshot carries only the IDs of graph objects it owns;
- target-instance snapshots are shared plan objects and therefore have no single
  `project-id` owner.

## Target-Instance Snapshot Freeze Boundary

The planner freezes all catalog-owned execution-relevant data into
`graph.target-instance-snapshots`:

- target family and instance identity;
- resolved destination contract id plus normalized compatibility structure;
- destination locator data;
- static capability data, including credential posture and publish topology.

The planner does **not** freeze mutable or control-plane-owned state there,
including:

- approval outcomes;
- workflow run ids or concurrency decisions;
- live credentials or secrets;
- remote registry observations such as already-exists checks or rerun status.

Those remain planner-time validation inputs or later execution-state records,
not catalog snapshots. When planner-time validation uses such observations for
immutable-target replay checks, the plan serializes only the referencing
publish node's already-resolved `resolved-publish-identity`, any
`desired-publish-state`, and derived `publish-disposition`, not target-instance
snapshot data or raw observation payloads.

## Deterministic Mapping from Group 1 Inputs

| Group 1 construct                                                          | Plan location                                                      | Deterministic mapping rule                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| -------------------------------------------------------------------------- | ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Descriptor file path and release root                                      | `envelope.projects[project-id].descriptor-path` and `release-root` | Derived from the discovered `src/**/three.release.yml` location.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `project.display-name`, `project.ecosystem`, `project.release-kind`        | `envelope.projects[project-id]`                                    | Copied verbatim from the selected descriptor.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `source.primary-manifest` and `source.auxiliary-inputs[]`                  | `envelope.projects[project-id].source.*`                           | Resolved from release-root-relative authoring paths to repo-root-relative execution paths.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `source.version-authority`                                                 | `envelope.projects[project-id].source.version-authority-kind`      | Copied from the descriptor when present; otherwise defaults to `build-system-nbgv`. The only current-scope non-default value is `nbgv-python-pyproject-version`, valid only for `nbgv-python`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `variants[]` entry                                                         | `graph.variants[variant-id]`                                       | One plan variant per descriptor variant. `variants[].id` becomes `descriptor-handle`; `dimensions` is copied verbatim.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| `artifacts[]` entry                                                        | `graph.artifacts[artifact-id]`                                     | One plan artifact per descriptor artifact. `artifacts[].id` becomes `descriptor-handle`; semantic artifact data is copied verbatim. For npm package artifacts, artifact-level `projection.package-name` is normalized into the artifact projection when declared.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `artifacts[].produced-from[]`                                              | `graph.artifacts[artifact-id].produced-from-artifact-ids`          | Resolved from descriptor-local artifact handles to plan artifact IDs within the same variant.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| Selected `profiles.<profile>.targets[n]` entry                             | `graph.publish-nodes[publish-node-id]`                             | Exactly one publish node per target usage entry in the selected profile. The zero-based target-list ordinal becomes `descriptor-target-index`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Planner request-affecting flags                                            | `envelope.request-flags`                                           | Current-scope normalized request flags are planner-facing inputs, not raw control-plane runtime state. `v1alpha1` currently freezes only `force: <bool>` there.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Planner-time replay-satisfaction decision                                  | `graph.publish-nodes[publish-node-id].publish-disposition`         | Planner-time validation may consult remote state, but the plan serializes only the derived closed outcome: `publish` or `skip-satisfied`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| Planner-time live publish behavior                                         | `graph.publish-nodes[publish-node-id].publish-mode`                | For live publish nodes, the planner serializes the executor-visible behavior: `create-only`, `overwrite-mutable`, or `replace-authoritative`. Executors do not infer overwrite or authoritative replacement from raw dispatch flags.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Planner-time resolved external publish identity                            | `graph.publish-nodes[publish-node-id].resolved-publish-identity`   | The planner serializes the target-family-specific identity used for publication and replay checks: current-scope `release-tag` for GitHub Release or `package-name` plus `version` for package registries.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| Planner-time family-specific desired target-side state                     | `graph.publish-nodes[publish-node-id].desired-publish-state`       | For current-scope GitHub Release nodes, the planner serializes `release-state: prerelease \| release`. No other current-scope family defines `desired-publish-state`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `targets[n].artifacts[]`                                                   | `graph.publish-nodes[publish-node-id].artifact-ids`                | Resolved from descriptor-local artifact handles to plan artifact IDs, preserving target entry order.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `targets[n].uses`                                                          | `graph.publish-nodes[publish-node-id].target-instance-snapshot-id` | Resolved from `family/instance-id` to one shared target-instance snapshot in the same plan.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| Catalog target-instance `capabilities.publish-topology`                    | `graph.target-instance-snapshots[*].capabilities.publish-topology` | Copied from the catalog into the snapshot. The control plane partitions active publish nodes by this frozen value rather than by target family guesses or registry-specific rules after planning.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `targets[n].projection` plus planner-frozen immutable registry member keys | `graph.publish-nodes[publish-node-id].projection`                  | Copied into the family-specific plan shape, with any artifact-handle keys normalized to artifact IDs. For npm, target-level `projection.package-name` is accepted only as a single-artifact compatibility shorthand, resolves the selected node identity, and is serialized as publish-node `projection.package-name`; artifact-level package-name projection remains on `graph.artifacts`. For current-scope npm/PyPI/RubyGems nodes, the planner determines one final distribution filename per artifact before replay classification and always serializes `projection.final-distribution-filenames-by-artifact-id`; any future reviewed NuGet registry path must do the same for NuGet and snupkg artifacts. For current-scope PyPI, filename projection is metadata-only; build workflows invoke the backend and produce authoritative receipts from actual artifacts. The map covers every `artifact-id` in the node's full `artifact-ids` membership, including singleton nodes, and remains remote-member matching data only. For every artifact the executor publishes for a live node, it must upload under exactly the planner-derived final filename. |
| Catalog target instance                                                    | `graph.target-instance-snapshots[target-instance-snapshot-id]`     | One snapshot per referenced catalog entry. `contract`, `destination`, and `capabilities` are frozen inline.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |

Only the selected profile contributes publish nodes, and only the selected
project appears anywhere in the plan.

## What Stays Out of the Plan

The following explicitly stay outside `release-plan` in `v1alpha1`:

- the raw control-plane run envelope, including actor, run id, attempt id,
  approval or environment-gate state, concurrency groups, raw workflow input
  names, and historical or future-only control-plane flags such as dry-run;
- workflow or job layout, reusable-workflow boundaries, artifact transport, and
  executor invocation syntax;
- the raw text of descriptors or the shared target catalog;
- unselected projects and the unselected profile block of selected projects;
- manifest-owned intrinsic package metadata as free-form source inputs, except
  where the planner has already frozen their current-scope publication
  contribution into `graph.publish-nodes[*].resolved-publish-identity`,
  `desired-publish-state`, or `projection` (including final distribution
  filenames and matching keys);
- raw remote observations such as already-exists responses, registry query
  payloads, or rerun evidence, even when the planner used them to derive a
  publish-node `publish-disposition`;
- execution status, publish receipts, or other post-plan mutable results.
- the control-plane prior build-receipt lookup/index and its workflow-run
  provenance records, even when the planner used them to prove immutable
  same-identity content equivalence.

This boundary keeps the plan self-sufficient for descriptor-owned, catalog-
owned, and planner-resolved publication identity, desired target-side state,
projection, and disposition without turning it into a workflow runtime record.

Those control-plane concerns are now defined in
[Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md).

## Related Pages

- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release OIDC Publish Topology Research](./workflow-release-oidc-publish-topology.md)
