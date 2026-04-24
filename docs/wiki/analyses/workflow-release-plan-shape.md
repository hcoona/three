# Workflow Release Plan Shape

## Purpose

This page defines the exact authoritative planner output shape for the frozen
planner-centric architecture. It builds directly on the Group 1 descriptor and
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
  difference.
- The plan is execution-authoritative for descriptor-owned, catalog-owned, and
  planner-derived resolved publish identity, desired publish state, and
  publish-disposition data.
  Execution may still read the
  checked-out repository sources and manifests, but it must not re-read
  `three.release.yml` files or `eng/release/target-instances.yml`.

## Authoritative Top-Level Shape

All normalized collections are mappings keyed by plan IDs rather than arrays.

```yaml
api-version: three.release.plan/v1alpha1
kind: release-plan

envelope:
    plan-id: ...
    profile: buddy
    commit-sha: ...
    request-flags:
        force: false
    requested-project-ids: [...]
    selected-project-ids: [...]
    authoring-inputs:
        descriptor-api-version: three.release/v1alpha1
        catalog-path: eng/release/target-instances.yml
    projects:
        hjg-pngcs:
            display-name: Hjg.Pngcs
            ecosystem: dotnet
            release-kind: lib
            descriptor-path: src/public/dotnet/Hjg.Pngcs/three.release.yml
            release-root: src/public/dotnet/Hjg.Pngcs
            source:
                primary-manifest-path: src/public/dotnet/Hjg.Pngcs/Hjg.Pngcs.csproj
                auxiliary-input-paths: []
            variant-ids: [variant/...]
            publish-node-ids: [publish-node/release, publish-node/package]
graph:
    variants:
        variant/...:
            project-id: hjg-pngcs
            descriptor-handle: package
            dimensions: {}
            artifact-ids: [artifact/...]
    artifacts:
        artifact/...:
            project-id: hjg-pngcs
            variant-id: variant/...
            descriptor-handle: nuget
            role: primary-package
            kind-family: package
            concrete-kind: nuget
            produced-from-artifact-ids: []
    publish-nodes:
        publish-node/release:
            project-id: hjg-pngcs
            profile: buddy
            descriptor-target-index: 0
            target-instance-snapshot-id: github-release/public
            artifact-ids: [artifact/...]
            publish-disposition: publish
            publish-mode: create-only
            resolved-publish-identity:
                release-tag: release/hjg-pngcs/v1.2.3
            desired-publish-state:
                release-state: prerelease
            projection:
                asset-labels-by-artifact-id:
                    artifact/...: Hjg.Pngcs.1.2.3.nupkg
        publish-node/package:
            project-id: hjg-pngcs
            profile: buddy
            descriptor-target-index: 1
            target-instance-snapshot-id: nuget/github-packages
            artifact-ids: [artifact/...]
            publish-disposition: publish
            publish-mode: create-only
            resolved-publish-identity:
                package-name: Hjg.Pngcs
                version: 1.2.3
            projection: {}
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
                aggregate-rules:
                    min-artifact-count: 1
                    max-artifact-count: null
                    cross-variant-policy: allow
                    tuple-rules:
                        - role: primary-package
                          kind-family: package
                          concrete-kind: nuget
                          min-count: 1
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
        nuget/github-packages:
            family: nuget
            instance-id: github-packages
            catalog-ref: nuget/github-packages
            contract:
                id: nuget-publish
                allowed-artifact-tuples:
                    - role: primary-package
                      kind-family: package
                      concrete-kind: nuget
                    - role: symbols
                      kind-family: package
                      concrete-kind: snupkg
                aggregate-rules:
                    min-artifact-count: 1
                    max-artifact-count: 2
                    cross-variant-policy: forbid
                    tuple-rules:
                        - role: primary-package
                          kind-family: package
                          concrete-kind: nuget
                          min-count: 1
                          max-count: 1
                        - role: symbols
                          kind-family: package
                          concrete-kind: snupkg
                          min-count: 0
                          max-count: 1
            destination:
                host: nuget.pkg.github.com
                owner: hcoona
            capabilities:
                mutability: immutable
                name-uniqueness-scope: package-name-with-owner
                version-uniqueness-rule: package-name-plus-version
                profile-coexistence-rule: requires-distinct-name
                credential-posture: github-token
```

## Envelope Contents vs Graph Contents

The envelope is the authoritative resolved request summary for one selected
profile and one selected commit. Its `plan-id` identifies that
request/selection scope, not every planner-authored difference in the emitted
plan. The envelope contains only data that stays at project or whole-plan
scope:

- `plan-id` as the deterministic request/selection identity, plus
  `profile`, `commit-sha`, and normalized `request-flags`;
- `requested-project-ids`, where omitted or empty input is serialized as `[]`
  and means all in-scope releasable projects, while explicit non-empty input
  must resolve completely or planning fails;
- `selected-project-ids`, the resolved project set normalized to unique
  lexicographic order;
- `request-flags`, which in `v1alpha1` has the exact normalized shape
  `{ force: <bool> }`, so `false` is the canonical default when no `FORCE`
  behavior was requested;
- `authoring-inputs`, which identify the author-time schema version and shared
  catalog path used for planning;
- `projects`, keyed by descriptor-owned `project.id`.

Each `envelope.projects[project-id]` snapshot freezes the selected project
fields that later control-plane and execution design will need without re-
reading descriptors:

- `display-name`, `ecosystem`, and `release-kind` from the descriptor `project`
  section;
- `descriptor-path` and `release-root` as repo-root-relative paths;
- `source.primary-manifest-path` and `source.auxiliary-input-paths` as fully
  resolved repo-root-relative file paths;
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
- `produced-from-artifact-ids`, normalized from descriptor-local artifact
  handles to plan artifact IDs.

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
- `projection`, normalized to the selected target family.

Current-scope normalized `resolved-publish-identity` shapes are:

| Resolved family                    | Plan `resolved-publish-identity` shape        |
| ---------------------------------- | --------------------------------------------- |
| `github-release`                   | `release-tag: <string>`                       |
| `npm`, `nuget`, `pypi`, `rubygems` | `package-name: <string>`; `version: <string>` |

For package registries, the planner resolves the final `package-name` after any
descriptor-side projection override or fallback to manifest-owned intrinsic
package naming and resolves `version` from the selected project's NBGV version
identity for the selected run. For GitHub Release, the planner resolves both the
final project-scoped `release-tag` and `desired-publish-state.release-state`
before serializing the plan. In current scope, GitHub Release tags use the
repositories existing shape `release/<project.id>/v<nbgv-version>`, matching
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

| Resolved family                    | Plan `desired-publish-state` shape |
| ---------------------------------- | ---------------------------------- | -------- |
| `github-release`                   | `release-state: prerelease         | release` |
| `npm`, `nuget`, `pypi`, `rubygems` | omitted                            |

GitHub Release `desired-publish-state.release-state` is planner-owned desired
target-side state. It must not be copied into `projection`.

Current-scope normalized `projection` shapes are:

| Resolved family             | Plan `projection` shape                               |
| --------------------------- | ----------------------------------------------------- |
| `github-release`            | `asset-labels-by-artifact-id: { <artifact-id>: ... }` |
| `npm`                       | `package-name?: <string>`                             |
| `nuget`, `pypi`, `rubygems` | `{}`                                                  |

For GitHub Release, descriptor-side `projection.asset-labels` keys are resolved
from descriptor-local `artifact.id` handles into plan artifact IDs before the
plan is serialized. Release-state remains outside `projection` and belongs only
in `desired-publish-state`.

For npm, `projection.package-name` remains descriptor-owned override data only.
The planner must serialize that key only when the selected descriptor target
usage explicitly declared an npm `projection.package-name` override. When no
such override exists, the planner must leave npm `projection` as `{}` rather
than backfilling the manifest-derived `package.json.name`. The final external
npm package name always belongs in `resolved-publish-identity.package-name`,
whether it came from the descriptor override or the manifest fallback.

`publish-disposition: publish` means execution should attempt the publication
intent represented by that node. `publish-mode: create-only` means the executor
must attempt a normal non-overwrite publication. `publish-mode:
overwrite-mutable` means the executor must perform the planner-authorized
overwrite behavior for a mutable current-scope buddy target rather than
inventing its own overwrite policy from raw inputs. `publish-mode:
replace-authoritative` means the executor must converge a same-tag GitHub
Release node to the planner-owned full official publish intent, including
`desired-publish-state.release-state`, `artifact-ids`, and
`projection.asset-labels-by-artifact-id`, rather than treating promotion as a
state-only flip or additive merge.
`publish-disposition: skip-satisfied` means planner-time validation already
proved that the destination state satisfies that full publish intent for this
run, so the plan records a no-op publish node rather than reserializing raw
remote observations. `resolved-publish-identity` is the planner-frozen external
publish identity that those checks refer to.

In current scope, `official-frozen` is a planner-time predicate over one
selected project and its resolved version identity. It becomes true only when
that same project has already succeeded at the `official` GitHub Release publish
intent for the same project-scoped `resolved-publish-identity.release-tag`.
Buddy prereleases, package-registry publication, or any alternate tag shape do
not make a version official-frozen, and no second freeze tag is introduced.

The planner must apply the following current-scope replay, authoritative-
replacement, and `FORCE` matrix before serializing publish nodes. Whole-request
planner-error rows below take precedence over per-node publish or skip
outcomes, and already-satisfied full publish intent must be detected before any
live `publish-mode` is chosen.

| Condition                                                                                                                                                                                                                                 | Planner outcome                                                                                                                                                                                                                                                                                   |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| No existing publication is found for the node's `resolved-publish-identity`.                                                                                                                                                              | Emit `publish-disposition: publish` with `publish-mode: create-only`.                                                                                                                                                                                                                             |
| A `github-release` target already satisfies the node's full publish intent for the same `resolved-publish-identity.release-tag`, including `desired-publish-state.release-state`, `artifact-ids`, and asset labels.                       | Emit `publish-disposition: skip-satisfied`. Do not invoke a publish executor for that node on rerun.                                                                                                                                                                                              |
| A `github-release` target already contains the same `resolved-publish-identity.release-tag`, and the node is in the planner-authorized same-tag `official` promotion case.                                                                | Emit `publish-disposition: publish` with `publish-mode: replace-authoritative`. The planner must serialize this explicitly so execution converges the full official publish intent rather than treating the promotion as a state-only change.                                                     |
| A target whose capability `mutability` is `mutable-prerelease` already contains the same `resolved-publish-identity`, and the node is in the planner-authorized buddy `FORCE` overwrite case.                                             | Emit `publish-disposition: publish` with `publish-mode: overwrite-mutable`. The planner must serialize this explicitly rather than leaving mutable-target replay overwrite behavior for executors to infer from destination state.                                                                |
| A target whose capability `mutability` is `mutable-prerelease` already contains the same `resolved-publish-identity`, but neither the planner-authorized same-tag `official` promotion case nor the buddy `FORCE` overwrite case applies. | Emit `publish-disposition: publish` with `publish-mode: create-only`. The planner must serialize this explicitly even though the same mutable publish identity already exists, and executors must not upgrade that replay case into overwrite or authoritative replacement behavior on their own. |
| An immutable target already satisfies the full publish intent for the node.                                                                                                                                                               | Emit `publish-disposition: skip-satisfied`. Do not invoke a publish executor for that node on rerun.                                                                                                                                                                                              |
| An immutable target already contains a conflicting publication for the same immutable target identity.                                                                                                                                    | Planner error. Do not emit a plan that asks executors to reconcile or overwrite the conflict.                                                                                                                                                                                                     |
| `profile: official` with `request-flags.force: true`.                                                                                                                                                                                     | Planner error for the whole request. `FORCE` is not a valid official-profile planner input in current scope.                                                                                                                                                                                      |
| `profile: buddy` with `request-flags.force: true`, but any selected project resolves to an official-frozen project-scoped version identity.                                                                                               | Planner error for the whole request. `buddy FORCE` is never valid for an official-frozen project or version identity.                                                                                                                                                                             |
| `request-flags.force: true` for a target whose capability `mutability` is not `mutable-prerelease`.                                                                                                                                       | `FORCE` does not authorize overwrite for that node. Immutable targets still follow the skip-versus-error rules above; only mutable buddy targets may proceed with `publish-mode: overwrite-mutable`.                                                                                              |

### `graph.target-instance-snapshots`

Each target-instance snapshot contains:

- `family`, `instance-id`, and `catalog-ref`;
- `contract`, frozen inline rather than left as a catalog lookup;
- `destination`, copied from the shared catalog;
- `capabilities`, copied from the shared catalog.

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
means.

## Plan IDs, Ownership, and References

The plan uses four kinds of identifiers:

- `envelope.plan-id`: the top-level request/selection identity for the
  emitted plan, not a hash of every serialized plan field. It is serialized as
  `plan/<hex-sha256>`, where the digest input is the canonical JSON object
  `{ profile, commit-sha, selected-project-ids, request-flags }` after
  `selected-project-ids` has been normalized to unique lexicographic order and
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
  `envelope.projects`, `selected-project-ids`, and every project-scoped graph
  object.
- `variant-id`, `artifact-id`, and `publish-node-id`: planner-assigned opaque
  string IDs with deterministic lexical form. The planner must emit them as
  `variant/<hex-sha256>`, `artifact/<hex-sha256>`, and
  `publish-node/<hex-sha256>` from the canonical identity payloads below. Later
  layers must treat them as equality-only references rather than parseable
  business keys.
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
  `resolved-publish-identity`, an npm publish node with no descriptor-side
  package-name override hashes with `projection: {}`. A manifest-derived final
  package name therefore changes `resolved-publish-identity` but does not by
  itself create a different publish-node ID.
- `descriptor-handle`, `display-name`, approvals, timestamps, remote
  observation payloads, `resolved-publish-identity`, `desired-publish-state`,
  `publish-disposition`, and `publish-mode` do not participate in these
  identity payloads. They may change emitted-plan detail or planner outcomes
  without changing the stable lexical IDs defined for the underlying
  request/selection scope or publish-node slot.
- Within every mapping-valued serialized collection in the plan, entries must be
  emitted in lexicographic key order. List-valued fields that are sets, such as
  `requested-project-ids` and `selected-project-ids`, must be normalized to
  unique lexicographic order; list-valued fields with declared semantic order,
  such as `artifact-ids` on publish nodes, must preserve that semantic order.

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
- static capability data, including credential posture.

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

| Group 1 construct                                                   | Plan location                                                      | Deterministic mapping rule                                                                                                                                                                                                                                                              |
| ------------------------------------------------------------------- | ------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| Descriptor file path and release root                               | `envelope.projects[project-id].descriptor-path` and `release-root` | Derived from the discovered `src/**/three.release.yml` location.                                                                                                                                                                                                                        |
| `project.display-name`, `project.ecosystem`, `project.release-kind` | `envelope.projects[project-id]`                                    | Copied verbatim from the selected descriptor.                                                                                                                                                                                                                                           |
| `source.primary-manifest` and `source.auxiliary-inputs[]`           | `envelope.projects[project-id].source.*`                           | Resolved from release-root-relative authoring paths to repo-root-relative execution paths.                                                                                                                                                                                              |
| `variants[]` entry                                                  | `graph.variants[variant-id]`                                       | One plan variant per descriptor variant. `variants[].id` becomes `descriptor-handle`; `dimensions` is copied verbatim.                                                                                                                                                                  |
| `artifacts[]` entry                                                 | `graph.artifacts[artifact-id]`                                     | One plan artifact per descriptor artifact. `artifacts[].id` becomes `descriptor-handle`; semantic artifact data is copied verbatim.                                                                                                                                                     |
| `artifacts[].produced-from[]`                                       | `graph.artifacts[artifact-id].produced-from-artifact-ids`          | Resolved from descriptor-local artifact handles to plan artifact IDs within the same variant.                                                                                                                                                                                           |
| Selected `profiles.<profile>.targets[n]` entry                      | `graph.publish-nodes[publish-node-id]`                             | Exactly one publish node per target usage entry in the selected profile. The zero-based target-list ordinal becomes `descriptor-target-index`.                                                                                                                                          |
| Planner request-affecting flags                                     | `envelope.request-flags`                                           | Current-scope normalized request flags are planner-facing inputs, not raw control-plane runtime state. `v1alpha1` currently freezes only `force: <bool>` there.                                                                                                                         |
| Planner-time replay-satisfaction decision                           | `graph.publish-nodes[publish-node-id].publish-disposition`         | Planner-time validation may consult remote state, but the plan serializes only the derived closed outcome: `publish` or `skip-satisfied`.                                                                                                                                               |
| Planner-time live publish behavior                                  | `graph.publish-nodes[publish-node-id].publish-mode`                | For live publish nodes, the planner serializes the executor-visible behavior: `create-only`, `overwrite-mutable`, or `replace-authoritative`. Executors do not infer overwrite or authoritative replacement from raw dispatch flags.                                                    |
| Planner-time resolved external publish identity                     | `graph.publish-nodes[publish-node-id].resolved-publish-identity`   | The planner serializes the target-family-specific identity used for publication and replay checks: current-scope `release-tag` for GitHub Release or `package-name` plus `version` for package registries.                                                                              |
| Planner-time family-specific desired target-side state              | `graph.publish-nodes[publish-node-id].desired-publish-state`       | For current-scope GitHub Release nodes, the planner serializes `release-state: prerelease                                                                                                                                                                                               | release`. No other current-scope family defines `desired-publish-state`. |
| `targets[n].artifacts[]`                                            | `graph.publish-nodes[publish-node-id].artifact-ids`                | Resolved from descriptor-local artifact handles to plan artifact IDs, preserving target entry order.                                                                                                                                                                                    |
| `targets[n].uses`                                                   | `graph.publish-nodes[publish-node-id].target-instance-snapshot-id` | Resolved from `family/instance-id` to one shared target-instance snapshot in the same plan.                                                                                                                                                                                             |
| `targets[n].projection`                                             | `graph.publish-nodes[publish-node-id].projection`                  | Copied into the family-specific plan shape, with any artifact-handle keys normalized to artifact IDs. For npm, `projection.package-name` is serialized only when the descriptor declared that override; the planner must not copy the manifest-derived fallback name into `projection`. |
| Catalog target instance                                             | `graph.target-instance-snapshots[target-instance-snapshot-id]`     | One snapshot per referenced catalog entry. `contract`, `destination`, and `capabilities` are frozen inline.                                                                                                                                                                             |

Only the selected profile contributes publish nodes, and only the selected
projects appear anywhere in the plan.

## What Stays Out of the Plan

The following explicitly stay outside `release-plan` in `v1alpha1`:

- the raw control-plane run envelope, including actor, run id, attempt id,
  approval jobs, concurrency groups, raw workflow input names, and control-
  plane-only flags such as dry-run;
- workflow or job layout, reusable-workflow boundaries, artifact transport, and
  executor invocation syntax;
- the raw text of descriptors or the shared target catalog;
- unselected projects and the unselected profile block of selected projects;
- manifest-owned intrinsic package metadata as free-form source inputs, except
  where the planner has already frozen their publication-identity contribution
  into `graph.publish-nodes[*].resolved-publish-identity` or
  `desired-publish-state`;
- raw remote observations such as already-exists responses, registry query
  payloads, or rerun evidence, even when the planner used them to derive a
  publish-node `publish-disposition`;
- execution status, publish receipts, or other post-plan mutable results.

This boundary keeps the plan self-sufficient for descriptor-owned, catalog-
owned, and planner-resolved publication identity, desired target-side state,
and disposition without turning it into a workflow runtime record.

Those control-plane concerns are now defined in
[Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md).

## Related Pages

- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
