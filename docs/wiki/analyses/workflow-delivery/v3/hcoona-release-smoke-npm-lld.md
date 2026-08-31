# `hcoona-release-smoke-npm` Live Buddy Low-Level Design

## 1. Status and Authorization Boundary

**Status:** replacement low-level design, dated 2026-08-31.

**Runtime state:** merged and disabled by protected Governance with `live_enabled: false`.

This document replaces the former implementation and rollout chronology. It defines the target first-slice design; current runtime code is useful only for repository naming and tooling conventions when it differs from the normative v3 design.

This document does **not** authorize changes to workflows, Python, schemas, tests, descriptors, policy or Governance files, Environments, access, packages, versions, tags, runs, or external state. It does not authorize a commit, activation, dispatch, publication, remediation, or deletion of the legacy publication Environment.

Implementation must be delivered and validated while `live_enabled` remains `false`. Obsolete Environment cleanup, fresh native evidence, Governance refresh, activation, and the first proving dispatch are later and separately controlled.

**Known activation blocker:** no current GitHub Packages npm primitive is
admitted for the complete version-and-tag projection. Standard
`npm publish --tag` can overwrite a conflicting tag introduced after
Observation. Live must remain disabled until a reviewed supported primitive
passes the conditional non-overwrite race acceptance in section 18.

### 1.1 Normative precedence

The current v3 `requirements.md`, `high-level-design.md`, medium-level designs, `architecture-glossary.md`, and `migration-strategy.md` are normative. This LLD closes first-slice implementation detail without weakening them.

Unless changed here, preserve purpose-first routing; request-local same-revision Provider and Repository Model behavior; NBGV; Build Definition, Release Unit, qualification, Observation, Official, simulation, concurrency, and remediation contracts. Simulation retains its current run-attempt identity and rerun behavior.

## 2. Exact Slice

| Concern                                   | Exact value                                                                                             |
| ----------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Repository                                | `hcoona/three`                                                                                          |
| Product root                              | `src/public/lib/hcoona-release-smoke-npm`                                                               |
| Release Unit                              | `hcoona-release-smoke-npm`                                                                              |
| Package                                   | `@hcoona/hcoona-release-smoke-npm`                                                                      |
| Channel and purpose                       | Buddy; `live-release`                                                                                   |
| Build Definition and output               | `node/npm-package-v1`; `npm-tarball`                                                                    |
| Destination                               | `npm/github-packages-hcoona-three-v1`                                                                   |
| Registry                                  | `https://npm.pkg.github.com`                                                                            |
| Release policy                            | `eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml`                                        |
| Release Unit descriptor                   | `src/public/lib/hcoona-release-smoke-npm/workflow-delivery.release-unit.yml`                            |
| Quality descriptor                        | `src/public/lib/hcoona-release-smoke-npm/workflow-delivery.quality.yml`                                 |
| Governance repository/ref/path            | `hcoona/three`; `refs/heads/main`; `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` |
| Governance maximum age                    | 90 days                                                                                                 |
| Approval Environment                      | `workflow-delivery-v3-buddy-approval`                                                                   |
| Environment sentinel                      | `WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-buddy-approval/v1`                               |
| Sole accepted writer/publisher TCB member | `hcoona`                                                                                                |
| Package credential principal              | repository `hcoona/three`                                                                               |
| Artifact retention                        | 45 days                                                                                                 |
| Target-derived dist-tag                   | `buddy-sha-<40-lowercase-target-sha>`                                                                   |
| Admitted mutation primitive               | None; standard `npm publish --tag` is rejected pending conditional non-overwrite proof                  |

The desired Buddy coordinate is the exact package plus the frozen native NBGV `npmPackageVersion`. The Release policy's Official projection, `npm/npmjs-public-v1`, remains isolated and is not a Live capability of this design.

## 3. Current State, Target State, and Risk

### 3.1 Current versus target

At this design date, protected Governance is disabled, was inspected at `2026-08-14T17:19:12Z`, and expires at `2026-11-12T17:19:12Z`. Both `workflow-delivery-v3-buddy-approval` and the legacy `workflow-delivery-v3-buddy-github-packages` Environment exist.

The merged runtime still uses history-based admission, a post-approval bridge, the legacy publication Environment, run-attempt-bearing normal-Live records, group-oriented publication records, a separate Receipt artifact, and the superseded consumer-policy implementation.

The replacement target has:

- one authority-bearing Environment, `workflow-delivery-v3-buddy-approval`;
- no publisher Environment, history-derived authority, prior-Attempt reconstruction, or reviewer recovery;
- a complete Publication Authorization emitted by the approved job;
- mechanically zero or one Publication Action;
- at most one Publication Result, with one embedded Receipt only for `published`;
- independent `github.run_attempt == 1` job guards and no normal-Live run-attempt record binding; and
- the bounded static-reference policy in section 6.

The legacy publication Environment remains untouched until replacement runtime references are absent and deletion receives separate authorization. Its continued existence grants no target authority.

No target code may retain a fallback to any superseded mechanism.

### 3.2 Accepted writer and repository-principal risk

Normal Buddy accepts an arbitrary same-repository selected ref. GitHub's resolved exact SHA is both workflow/control revision and Release target. Protected `main` supplies Governance only; it must not substitute control code for the selected revision. The selected-revision eligibility parser must require exact schema `workflow-delivery/v3/normal-live-governance-attestation-v1`; an incompatible ref fails before Release Execution lookup, Attempt creation, or any Environment job.

`hcoona` is the sole accepted writer and publisher TCB member and may self-approve with `prevent_self_review: false`. Approval is operator confirmation, not independent review. A malicious accepted writer is not constrained by protected Governance, CODEOWNERS, static-reference validation, Environment approval, exact action checks, or permission declarations.

The GitHub Packages `GITHUB_TOKEN` principal is repository `hcoona/three`, not the smoke package. Known reach includes the real `hexo-renderer-asciidoc` package and disposable packages. Coordinate checks, Environment, CODEOWNERS, concurrency, and workflow permissions do not narrow that token. This wider blast radius is explicitly accepted for `hcoona`; Official npmjs credentials remain separate.

Controls retained for outsiders and mistakes include exact same-revision bindings, protected Governance, bounded static-reference validation, credential-free build and qualification, read-only Observation without publication capability, immutable reviewer context, package-write isolation, create-only publication, complete resource keys, a durable pre-mutation marker, and exact readback.

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
       0 actions -> exact-satisfied
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

### 6.3 Supported surface

The canonical policy has six path families:

| Family                                    | Bounded selector                                                                                                                                                |
| ----------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Manifests                                 | `package.json`, `pyproject.toml`, `setup.py`, `requirements*.txt`, `Directory.Packages.props`, `packages.config`, and `*.*proj` basenames                       |
| Lockfiles                                 | `pnpm-lock.yaml`, `package-lock.json`, `npm-shrinkwrap.json`, `yarn.lock`, `bun.lock`, `uv.lock`, `poetry.lock`, and `packages.lock.json`                       |
| Workflows                                 | `.github/workflows/**/*.yml` and `.github/workflows/**/*.yaml`                                                                                                  |
| Dependency configuration                  | `.npmrc`, `.pnpmfile.cjs`, `.yarnrc*`, `pnpm-workspace.yaml`, `bunfig.toml`, `renovate.json`, and `.github/dependabot.{yml,yaml}`                               |
| Composite actions                         | `.github/actions/**/action.{yml,yaml}`                                                                                                                          |
| Conventional install/bootstrap automation | Any-depth basename beginning `bootstrap`, `install`, `setup`, or `postinstall` with a shell, PowerShell, batch, Python, JavaScript, or TypeScript script suffix |

The selector is path-role based, not a scan of all repository text. Documentation, ordinary application source, external configuration, generated registry metadata, and novel layouts are outside the first slice.

Format-specific readers may handle JSON, YAML, TOML, XML, and line-oriented files, with bounded literal-token recognition for command surfaces. Invalid syntax, duplicate structured keys, invalid required UTF-8, or an unsupported authority-relevant candidate fails closed.

The implementation must not use Tree-sitter, a JavaScript/shell dataflow interpreter, an exhaustive command trigger catalog, fixed inventory counts, or whole-file digest exceptions.

### 6.4 Prohibited forms

A supported dependency, package-load, package-manager, action, or workflow-command context reports a finding for:

- direct `@hcoona/hcoona-release-smoke-npm`;
- `@hcoona/hcoona-release-smoke-npm@<selector>`;
- `@hcoona/hcoona-release-smoke-npm/<subpath>`;
- `npm:@hcoona/hcoona-release-smoke-npm` with or without a selector;
- a workspace form naming the producer package;
- a dependency key equal to the producer package, regardless of its value; or
- dependency-position `file:`, `link:`, or path-bearing `workspace:` values resolving to the producer root.

Path resolution uses repository-relative POSIX semantics, resolves `.` and `..`, rejects escape above repository root, and compares with `src/public/lib/hcoona-release-smoke-npm`.

Encoded package names, split/constructed strings, arbitrary runtime downloads, external files, and new layout conventions are non-goals. Normal structured-string decoding is syntax handling, not a dataflow guarantee.

### 6.5 Exact allowances

Only these allowances exist:

1. the top-level `name` value in the exact producer `package.json`; and
2. legitimate build/workspace references to the producer root outside dependency positions.

The second allowance covers a workspace member, lockfile importer, or workflow `working-directory`; it does not permit an install command, dependency value, alias, package token, or module load.

Fixtures create prohibited examples outside candidate paths or construct them in test code. No repository file receives a whole-file exception.

### 6.6 Result and integration

Each finding contains normalized path, family, semantic context, prohibited-form kind, stable location when available, and a sanitized matched identity. Result status is `clean`, `findings`, or `error`.

Candidate counts, per-file digests, aggregate inventory digests, and timing are diagnostics only. Live authority is exact target, exact policy ID/digest, successful parsing, and an empty finding set.

Root HK runs the lightweight policy whenever HK runs; the step is not skipped because the caller-selected file list lacks a candidate. The caller explicitly selects `index` for staged/pre-commit operation or `worktree` for manual filesystem checking. Omitted or invalid mode fails rather than guessing. HK output is feedback, never Live Evidence.

Live Eligibility reruns `git-target` itself against the exact selected commit. It does not adopt HK, CI, caller-provided, index, or worktree output.

## 7. Canonical Records and Artifact Binding

### 7.1 Representation

Authoritative records use strict UTF-8 JSON, duplicate/unknown-field rejection, RFC 8785 JCS, normalized POSIX paths, full lowercase SHAs, `sha256:<64-hex>`, `sha512:<128-hex>`, exact schemas, and sorted duplicate-free semantic sets. Python records remain frozen and slotted.

If present, `record-digest` is computed over the canonical document before adding that field. Consumers reconstruct and verify it.

Normal-Live producer bindings include repository, workflow path, logical job, selected control SHA, target, purpose, `workflow_run_id`, payload identity, and payload digest. They omit `github.run_attempt`; target parsers reject a normal-Live `run-attempt` field.

### 7.2 Execution and Attempt identity

Buddy Execution identity is canonical channel + Release Unit + target. Normal-Live Attempt identity is Execution identity + `workflow_run_id`. An admitted, non-coalesced new dispatch therefore creates a new Attempt even for the same target; the platform run attempt remains only a guard and diagnostic.

### 7.3 Record set

| Record                                       | Binding responsibility                                                                         |
| -------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Release Intent                               | Manual request, selected ref, repository, channel, Release Unit, purpose, actor, target        |
| Repository Model Snapshot                    | Selected revision, Providers, Project Nodes, Release Unit, descriptors, catalogs/control, NBGV |
| Buddy Execution / Release Attempt identities | Deterministic execution tuple; execution plus workflow run                                     |
| Bounded Static-Reference Result              | Policy, source kind, target for `git-target`, status, findings                                 |
| Live Eligibility Decision                    | Intent, Model, workflow run, policy Result, protected Governance, eligibility-main lineage     |
| Qualification Snapshot                       | Target, build/output, version, toolchain, obligations, desired projection basis                |
| Release Artifact                             | Tarball transport, SHA-256/SHA-512, manifest, witness, build identity                          |
| Qualification Evidence / Decision            | Obligation results and complete admission                                                      |
| Projection Observation                       | Desired-state basis and canonical remote facts; no future Snapshot reference                   |
| Publication Snapshot                         | Qualification, Observations, desired state, `actions` of length zero or one                    |
| Approval Bundle                              | Pre-wait summary and complete one-action closure                                               |
| Publication Authorization                    | Sole approved authority for the exact action                                                   |
| Mutation marker                              | Durable pre-mutation boundary                                                                  |
| Publication Result                           | At most one result; `published` embeds one Receipt                                             |
| Attempt Outcome                              | Read-only current-DAG terminal/incomplete disposition                                          |

New schemas are `workflow-delivery/v3/approval-bundle`, `workflow-delivery/v3/publication-authorization`, and `workflow-delivery/v3/publication-result`. The marker retains `workflow-delivery/v3/github-packages-mutation-may-have-started`. A successful Result embeds one `workflow-delivery/v3/receipt`; no standalone Receipt artifact exists.

### 7.4 Artifact transport

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
`workflow-delivery/v3/normal-live-governance-attestation-v1`, replacing the
currently merged `workflow-delivery/v3/governance-attestation`. The new parser
accepts only the replacement schema. This is an intentional compatibility
fence so superseded selected-ref control fails before any Environment or
publisher job.

It also retains normalized authenticated native readback/attestation for `workflow-delivery-v3-buddy-approval`:

- reviewed destination-primitive identity and the retained
  disposable-package race-acceptance inputs, results, capture time, and
  canonical evidence digest;
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
| `observe-destination`     | Read-only exact remote Observation                          |
| `materialize-publication` | Zero/one-action Snapshot; summary and Bundle for one action |
| `exact-satisfied`         | Read-only zero-action terminal path                         |
| `approve-publication`     | Sole Environment job and Authorization producer             |
| `publish-github-packages` | Sole step-running package writer                            |
| `finalize-attempt`        | Read-only best-effort current-DAG outcome                   |

Incidental batching/DAG detail is not frozen. Boundaries may not combine Environment wait with package write, qualification with a publication token, Observation with mutation, pre-wait materialization with Authorization, product/build execution with package write, or finalization with mutation.

### 10.3 Permissions

Workflow-level permissions are empty or read-only. Admission/model/planning/build/test/qualification jobs have no package permission. Observation may have `packages: read`. Materialization has none. Exact-satisfied has at most read. Approval has no package permission and may have `contents: read`. Publisher alone has `packages: write`, may have `contents: read`, and has no `id-token: write`. Finalizer has no destination permission.

The publisher receives only short-lived repository `GITHUB_TOKEN`: no PAT fallback and no OIDC. No authority job needs `actions: read`.

### 10.4 Publisher executable isolation

Publisher checks out the exact selected target with persisted checkout credentials disabled and runs only the selected-revision Workflow Delivery publisher control plus pinned setup actions. This target-revision control is the explicit accepted-writer TCB exception; it is not an independent boundary against `hcoona`.

Publisher loads publication inputs only from exact current-run immutable artifacts by ID. It executes no Release Unit script, lifecycle hook, build, test, installer, target `.npmrc`, or packed package code, and it uses isolated npm configuration with scripts disabled.

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

A retry fully rebuilds. Existing different bytes for the desired version enter reconciliation. Nondeterministic units are unsupported pending a future sealed-artifact publication-resume design.

## 12. Observation and Zero/One Action

### 12.1 Desired state and readback

After Qualification, the read-only Adapter derives destination, ownership, package, frozen version, local SHA-256/SHA-512, witness/target, target-derived tag, and required tag mapping. It uses trusted isolated configuration and no scripts.

Observation records package/version existence, ownership/destination, version metadata, downloaded tarball bytes when present, computed digests, embedded witness, tag mapping, response status, selected non-secret headers, and bounded diagnostics.

Exact state requires downloaded remote bytes and the exact in-package witness. A local sidecar, registry integrity field, or matching version string is insufficient.

### 12.2 Mechanical state machine

| State                                                         | Snapshot             | Disposition                   |
| ------------------------------------------------------------- | -------------------- | ----------------------------- |
| Exact ownership, version bytes, witness, and tag mapping      | `actions: []`        | `exact-satisfied`             |
| Version and tag absent; accepted conditional primitive proven | One compound action  | Approval                      |
| Version and tag absent; conditional primitive unproven        | No admissible action | Unsupported; activation block |
| Existing differing bytes/witness/target/ownership             | No admissible action | Reconciliation                |
| Exact version with absent/wrong tag                           | No admissible action | Reconciliation                |
| Absent version with conflicting/inconsistent tag              | No admissible action | Reconciliation                |
| Unknown, unreadable, incomplete, or unprovable                | No admissible action | Fail closed                   |

Publication Snapshot schema restricts `actions` to length zero or one. More is invalid.

The required normal action must conditionally create the immutable version and
assign the target-derived tag as one non-overwriting operation. No current
GitHub Packages invocation is admitted.

The standard command below is a rejected baseline, not an executable
normal-Live action:

```text
npm publish <qualified-tarball> \
  --registry https://npm.pkg.github.com \
  --tag buddy-sha-<40-lowercase-target-sha> \
  --ignore-scripts \
  --fetch-retries=0
```

It protects immutable version creation but can move a competing tag introduced
after Observation because the tag assignment has no expected-value condition.
Repository concurrency, a second read, and post-action exact readback do not
repair that overwrite race. A future supported primitive requires a reviewed
design update and the section 18 race acceptance before this row can
materialize one action.

No separate tag-only, delete, restore, overwrite, visibility, permission, or
administrator action exists. A destination conflict is not same-Attempt
success; a new dispatch may later observe exact state.

## 13. Approval Bundle, Environment, and Authorization

### 13.1 Pre-wait materialization

For one action, `materialize-publication` durably uploads the Snapshot, renders deterministic reviewer Markdown, uploads it and captures transport, optionally mirrors it to the job summary, forms an Approval Bundle binding the summary and full closure, and uploads the Bundle before Approval can wait.

The Approval deployment URL points to the immutable reviewer-summary artifact or authenticated artifact page. The uploaded Markdown, not its visual projection, is authoritative.

### 13.2 Reviewer summary

The summary contains repository/run, selected ref/target, Release Unit/destination, package/version/tag, artifact ID/URL/SHA-256/SHA-512/manifest/witness, exact lifecycle scripts or none, `--ignore-scripts`, Qualification, Governance identity/freshness, Observation/Snapshot closure, exact compound action, complete resource keys, conservative group, and the warning that repository token reach is not package-isolated. It contains no secret.

### 13.3 Approval Bundle

`workflow-delivery/v3/approval-bundle` binds Intent, Execution, Attempt, selected ref, target, run, Eligibility, Governance, Qualification/Evidence, artifact/manifest/lifecycle scripts, Observations, Snapshot, summary payload/transport, exact action, complete resources, conservative projection, intended Environment, and intended Approval job. It contains no approval fact.

### 13.4 Approval job

`approve-publication` is the only job referencing `workflow-delivery-v3-buddy-approval`. It has no package publication permission.

Its first declared executable step performs only a case-sensitive exact comparison of the resolved sentinel with `workflow-delivery-v3-buddy-approval/v1`. It precedes checkout, artifact download, control execution, and other authority-critical work; missing/empty/mismatched value fails with no `continue-on-error`.

That check proves only the resolved value under external native attestation. It cannot prove source scope, reviewers, self-review, bypass, deployment policy, secrets, or broader-variable absence.

After the wait and sentinel, Approval obtains only exact selected-revision control, executes no product/build hook, downloads artifacts by ID, repeats Governance ancestry/path/freshness checks, validates the complete Bundle/Snapshot/artifact/action/resource closure, and durably uploads the sole Authorization.

GitHub does not expose approver login in normal job context. No actor is recovered or invented. The approval fact is successful post-wait execution and Authorization production by logical job `approve-publication` under the literal Environment.

### 13.5 Publication Authorization

`workflow-delivery/v3/publication-authorization` binds Attempt/run, ref/target/control, Eligibility, eligibility and approval Governance proofs, Qualification, exact artifact, Snapshot, Approval Bundle, reviewer-summary artifact, exact action, complete resources, conservative projection, literal Environment, and logical Approval job.

It contains no credential, secret, approver/recovered actor, historical authority, prior-Attempt reference, or run attempt. There is no later post-approval bridge; publisher independently revalidates this Authorization.

## 14. Publisher, Marker, Result, and Receipt

### 14.1 Entry and preflight

Publisher is an ordinary success-dependent consumer of Approval, has no Environment, and holds the publication resource concurrency group with `cancel-in-progress: false`.

Before mutation it validates attempt one; all Intent/Execution/Attempt/run/ref/target/control bindings; Eligibility; Qualification/Evidence; artifact transport/bytes/digests/manifest/lifecycle/witness; Snapshot cardinality; Bundle/summary; Authorization and Approval identity; destination/package/version/tag; exact action; the design-versioned admitted destination primitive; complete resources/group; isolated tool configuration; and fresh Governance ancestry/path/blob/content/expiry/enablement.

Any mismatch prevents marker and mutation. Flag-off blocks a publisher before its final fresh check but cannot revoke one already beyond that check.

### 14.2 Isolated npm configuration

If a future admitted primitive invokes npm, Publisher creates runner-private
configuration for the exact `@hcoona` registry, supplies `GITHUB_TOKEN` without
artifact/log exposure, fixes the user-config path, prevents target/project npm
config loading, verifies effective registry and scripts-disabled behavior,
requires the highest-precedence CLI value `fetch-retries=0`, and sanitizes
diagnostics. This configuration does not make standard `npm publish --tag`
conditionally non-overwriting.

### 14.3 Mutation marker

Immediately before the first mutating command, publisher durably uploads `workflow-delivery/v3/github-packages-mutation-may-have-started`. It binds Attempt/run, Authorization, Snapshot/action, complete resources/group, artifact transport/digests, final Governance proof, and publisher identity.

Marker upload failure prevents publication. Once durable, mutation is conservatively possible until a durable Result proves a controlled outcome.

### 14.4 One invocation and readback

No action-bearing invocation is currently admitted. After a reviewed supported
primitive passes section 18, Publisher invokes that exact primitive once with
the admitted tarball, registry, version, target tag, and conditional
non-overwrite inputs. If it invokes npm, it also fixes `--ignore-scripts` and
`--fetch-retries=0`; the zero retry value prevents npm from automatically
resending a retryable mutating `PUT` within that process but does not supply tag
compare-and-swap semantics. Publisher runs no second publish, separate tag
command, implicit `latest`, overwrite, delete, restore, permission change, or
automatic mutation retry after ambiguity. Bounded read-only retries are
permitted.

It then freshly observes command classification, sanitized response identity, ownership, version, downloaded tarball digests/witness, tag mapping, and complete after-state. Command success without complete exact readback is not `published`.

### 14.5 Publication Result

There is at most one logical `workflow-delivery/v3/publication-result` for the invocation. It binds Authorization, the durable marker, action/resources, artifact, command classification, post-action Observation, mutation classification, diagnostics, and outcome `published` or `failed`.

Mutation classification is at least `not-mutated`, `possibly-mutated`, or `mutated`; ambiguity is `possibly-mutated`.

`published` requires durable marker, successful command, exact ownership/version/bytes/witness/tag readback, and embeds exactly one `workflow-delivery/v3/receipt`. The Receipt binds Authorization, action/resources, destination coordinate/tag, artifact digests, witness/target, before/after observations, sanitized response identity, and successful disposition.

A failure before the marker emits no Publication Result or Receipt. A controlled post-marker failure omits Receipt and uses `not-mutated` only with complete proof; otherwise `possibly-mutated`. Publish conflict remains failed even if later readback appears exact.

Marker without durable Result is unknown/possibly mutated. A Result transport failure is not repaired or synthesized.

### 14.6 Durable publication-state matrix

| Durable current-run facts                                      | Meaning                                         | Required next-step posture       |
| -------------------------------------------------------------- | ----------------------------------------------- | -------------------------------- |
| Publisher non-start proved; no marker, Result, or Receipt      | Finalizer may prove `failed-before-publication` | New dispatch                     |
| Publisher started or transport unresolved; no marker or Result | Incomplete; no Result may be synthesized        | New dispatch; fresh Observation  |
| Marker; failed `not-mutated` Result                            | Adapter proved no mutation                      | New dispatch still reobserves    |
| Marker; failed `possibly-mutated` Result                       | Mutation cannot be excluded                     | Fresh read-only reconciliation   |
| Marker; no durable Result                                      | Unknown/possibly mutated                        | Fresh read-only reconciliation   |
| Marker; `published` Result with one valid Receipt              | Exact publication proved                        | Finalizer may report `published` |
| Result without required Authorization/marker lineage           | Contract failure                                | Do not infer destination state   |

No row authorizes continuation inside the same Attempt after a failed or ambiguous publish. The only normal recovery boundary is a new manual dispatch and fresh Observation.

## 15. Exact-Satisfied, Finalization, and Retry

### 15.1 Exact-satisfied

A zero-action Snapshot takes no Environment, Approval Bundle, Authorization, publisher, write token, marker, Result, or Receipt. `exact-satisfied` validates complete exact state and may repeat read-only Observation. Immediately before success it independently repeats the section 9 ancestry, path-touch, blob/content, expiry, and `live_enabled` checks and persists the fresh no-op Governance proof. It may yield `success/exact-satisfied`; partial, stale, conflicting, unknown, or possibly mutated state cannot.

### 15.2 Read-only Finalizer

`finalize-attempt` is best effort, declares all relevant direct `needs`, and uses only current-DAG job results, current-run artifact IDs/digests, and canonical records. It validates available Intent/Model/Eligibility/Qualification/Observation/Snapshot and either the fresh no-op Governance proof or Approval/Authorization/marker/Result/Receipt lineage.

It never lists historical runs/jobs/deployments/artifacts, recovers a reviewer, queries destination state to invent a Result, infers publication from green status, adopts a prior Attempt, reruns quality, repairs missing lineage, or mutates.

Possible outcomes are validated `success/exact-satisfied`, `success/published`, proven `failed-before-publication`, durable controlled `failed`, or `incomplete/unknown-possibly-mutated`.

For an action-bearing Snapshot, publisher `skipped` due to unsatisfied ordinary success dependency can contribute to proof of non-start. `cancelled`, `failure`, or missing transport alone cannot. If publisher start/mutation cannot be excluded, preserve incomplete/unknown state.

Cancellation, runner loss, or artifact transport failure may leave no durable Outcome. No record is safer than a fabricated one.

### 15.3 Retry

Retry is a new manual dispatch and `workflow_run_id`. It resolves ref, recompiles Model, reruns Eligibility, rebuilds, requalifies, reobserves, rematerializes, and reapproves if one action remains. No prior Model, artifact, Evidence, Snapshot, approval, Authorization, marker, Result, or Outcome is authority.

Fresh Observation resolves uncertainty: exact becomes no-op, absent may form one action, and conflict/unprovable state enters reconciliation. GitHub rerun commands are not recovery.

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

All normal-Live authoritative artifacts use 45-day retention: Intent/Model transport, Eligibility/Governance proof, Qualification Snapshot, tarball/manifest, Evidence/Decision, Observations/Snapshot, reviewer summary/Bundle, Authorization, marker, Result, and Outcome.

Fresh preactivation and post-merge evidence must authenticate
`GET /repos/hcoona/three/actions/permissions/artifact-and-log-retention`,
capture endpoint identity, time, normalized response, and canonical response
digest, and require integer `days >= 45`. Requested `retention-days: 45` in a
workflow is not evidence that repository policy permits it.

Logs and job summaries are diagnostic projections. Diagnostics may include run attempt, job/step result, Git status, endpoint/status/non-secret headers, canonical response digests, parser timing and inventory counts/digests, npm exit classification, and sanitized errors.

Redact `GITHUB_TOKEN`, npm auth lines, authorization headers, credential-bearing URLs/config, secrets, and unbounded response bodies. Native Actions history may be linked diagnostically but is never authority.

## 18. Semantic Acceptance Plan

### 18.1 Records and transport

- Reject duplicate/unknown fields, noncanonical paths/SHAs/digests/sets/JSON, and wrong producer/run/target/purpose.
- Reject normal-Live `run-attempt`; prove Attempt identity changes with `workflow_run_id` while artifact witness excludes run/Attempt.
- Enforce Snapshot action cardinality zero/one, Authorization without actor/credential, and exactly one embedded Receipt only for `published`.
- Admit artifacts only by ID, service digest, payload identity, and canonical digest; reject name/latest/history fallback.

### 18.2 Static-reference policy

- Prove `git-target`, index, and worktree read their declared bytes when all three differ.
- Reject unmerged index candidates, candidate symlinks/submodules, missing objects, unreadable/invalid candidates, and non-`git-target` Live Results.
- Cover direct, versioned, aliased, workspace, subpath, dependency-key, and dependency-position `file:`/`link:`/`workspace:` producer-root forms in every supported family.
- Allow only exact producer top-level `name` and legitimate workspace/importer/working-directory producer-root references.
- Prove no whole-file exception, Tree-sitter/dataflow dependency, fixed inventory authority, or consumer claim remains.
- Prove root HK requires explicit `index`/`worktree`, runs independent of candidate file selection, and cannot supply Live Evidence.

### 18.3 Workflow contracts

- Parse YAML and prove purpose-first routing, selected control equals target, and every authoritative job has its own attempt-one guard.
- Prove reusable caller is `uses`-only; only caller ceiling/publisher declare package write; publisher is the only step-running writer.
- Prove only Observation or an explicit no-op reobserver may declare
  `packages: read`; build, qualification, materialization, Approval, and
  Finalizer receive no destination permission.
- Prove Approval is the only Environment job, has no package write, and publisher has ordinary success dependency with no Environment.
- Prove no authority history permission/query, publisher product/build/lifecycle script, legacy publication Environment authority, run-attempt artifact name, or name-based artifact retrieval; prove publisher checkout is exact-target and persists no credentials.
- Prove overwrite-disabled 45-day uploads and unchanged Official simulation rerun semantics.

### 18.4 Governance

- Admit unrelated main advance.
- Reject direct edit, edit/revert, delete/restore, rename round-trip, and path-touching side-branch merge.
- Reject non-descendant/force update, shallow/missing history, missing/non-blob path, expired/over-90-day attestation, writer/native-fact change, or `live_enabled: false`.
- Require authenticated repository artifact-retention readback with
  `days >= 45`; reject missing, stale, malformed, or lower values.
- Exercise Approval and publisher proofs independently.

### 18.5 Build and qualification

- Prove frozen NBGV target/version binding and absence of recomputation/fallback.
- Prove isolated staging, unchanged source manifest, exact witness, deterministic bytes for stable frozen inputs/toolchain, and one produced build rather than certification rebuild.
- Prove separate project-test, artifact-content, and install/import Evidence; scripts-disabled install/import; exact lifecycle extraction; and failure on missing/substituted Evidence.

### 18.6 Observation and Snapshot

- With an admitted primitive, cover absent version/tag to one action; cover
  complete exact state to zero actions independently.
- Route differing bytes/witness/target/ownership, missing/wrong tag, absent version with tag conflict, inaccessible tarball, and malformed/ambiguous metadata to reconciliation/failure.
- Prove remote byte/witness download is required and no tag-only action can form.
- After absent Observation for desired version `V` and tag `T`, create distinct
  version `W` with `T -> W` on a separately authorized disposable package,
  invoke the candidate primitive, and require failure with `V` still absent and
  `T -> W` unchanged. Standard `npm publish V --tag T` must fail this
  admission. Synthetic tests may reject a client mechanism but cannot admit
  GitHub Packages destination support.

### 18.7 Approval

- Prove Snapshot/summary/Bundle are durable before wait and Environment URL identifies immutable summary.
- Prove summary and Bundle carry all artifact, lifecycle, Governance, Observation, action, resource, concurrency, and blast-radius context.
- Prove sentinel comparison is first executable Approval step, exact/fail-closed, and runtime cannot claim source scope.
- Prove fresh Governance and complete closure precede Authorization; Authorization binds literal Environment/logical job and contains no approver.
- Validate native-attestation structure for reviewer, self-review, bypass, wait, deployment policy, secrets, sentinel scope, and broader-variable absence.

### 18.8 Publisher and Result

- Prove publisher cannot start without successful Authorization and all checks precede marker.
- Prove marker failure blocks the admitted primitive; durable marker precedes
  exactly one mutation command; isolated configuration ignores target config.
- Prove the exact registry/tarball/version/tag, no separate tag or mutation
  retry, and exact readback before `published`.
- For any future npm-based admitted primitive, prove its argv contains
  `--ignore-scripts` and `--fetch-retries=0` and a retryable synthetic response
  produces exactly one outbound mutating `PUT`; this is necessary but not
  sufficient for conditional tag safety.
- Prove every Result has marker lineage, successful Result has one Receipt,
  failed Result none, pre-marker failure emits no Result, conflict is not
  success, marker without Result is possibly mutated, and secrets never enter
  records/logs.

### 18.9 Finalization, retry, and concurrency

- Cover exact/no-op, published, publisher skipped, cancelled, publisher failure
  before marker with no Result, marker plus failed Result, marker without
  Result, green job without Result, contradictory lineage, and Finalizer
  transport loss.
- Prove only current-DAG non-start becomes `failed-before-publication`, no history/destination invention occurs, and new dispatch adopts no prior authority.
- Prove Execution identity/group and publisher resource/group inputs, both `cancel-in-progress: false`, complete version/tag keys, conservative same-package serialization, and block on incomplete closure.

### 18.10 Disabled integration gate

Before activation, targeted Python/workflow tests and root HK pass; protected
Governance remains false; authenticated repository retention readback proves
`days >= 45`; the section 18.6 conditional non-overwrite race passes for the
design-versioned destination primitive; no dispatch/registry/Environment/access
mutation occurs; and repository search proves replacement runtime has no
reference to `workflow-delivery-v3-buddy-github-packages`. Under current
standard npm semantics the destination-primitive gate is unsatisfied, so Live
remains disabled.

Live disposable-package testing, if later authorized, is separate and cannot incidentally mutate the dedicated smoke coordinate.

## 19. Implementation and Deployment Order

1. Merge normative design and this replacement LLD.
2. Implement records, static-reference policy, Governance proof, workflow
   topology, non-mutating paths, fail-closed action admission, and semantic
   tests. Standard `npm publish --tag` must not be installed as an admitted
   normal-Live primitive.
3. Validate and merge the implementation with `live_enabled: false`; the
   disabled Governance document already uses the replacement schema.
4. Prove no workflow, executable source, schema, policy, formatter, validator, or test treats `workflow-delivery-v3-buddy-github-packages` as an input or authority. Current-state and migration text may still name the resource solely to inventory and remove it safely. Prove every retained dispatchable ref either implements the one-Environment contract or rejects the replacement Governance schema before any Environment job or deployment; retain `origin/workflow-delivery-v3-platform-orphan-exception@4af8819bed7c19d3231570351b278a24b268dab8` as a negative compatibility fixture if that ref still exists.
5. Obtain separate authorization before deleting that obsolete Environment.
6. Before any activation work, merge a separately reviewed design and
   implementation for a documented destination primitive and pass the section
   18.6 conditional non-overwrite race. This gate is unsatisfied by standard
   `npm publish --tag`.
7. Perform fresh authenticated native readback of Approval Environment,
   broader variables, access, package-principal facts, and repository Actions
   artifact retention; require `days >= 45`.
8. Prepare the exact refreshed Governance attestation from that evidence
   without merging a separate preparation change.
9. Create one small Activation PR that applies the refreshed attestation and
   changes `live_enabled: false` to `true`.
10. Merge through protected review and perform authenticated post-merge
    readback, including repository retention.
11. Dispatch once from then-current protected `main`.

There is no Preparation PR, main freeze, preselected activation SHA, activation tag, implementation-time dispatch, or blind retry.

### 19.1 First proving dispatch

Use the REST workflow-dispatch API with `X-GitHub-Api-Version: 2026-03-10`, workflow `workflow-delivery-v3-buddy-smoke.yml`, and `ref: main`. Require HTTP `200` with schema-valid `workflow_run_id`, `run_url`, and `html_url`.

Read back that exact run and verify repository, workflow, actor `hcoona`, event `workflow_dispatch`, `refs/heads/main`, actual head SHA equal to the just-recorded protected-main SHA, workflow/control revision equal to it, and run attempt one.

A lost/malformed/ambiguous response triggers read-only reconciliation, never blind redispatch or custom history correlation as authority. Later normal Buddy runs may again select arbitrary same-repository refs whose selected-revision control strictly admits the active Governance schema.

## 20. Deferred and Non-Goals

Outside this slice are Official Live npmjs trust; additional destinations/actions; generic Environment profiles; independent publisher infrastructure; cryptographic separation from `hcoona`; package-specific repository-token narrowing; universal package-grant enumeration; nondeterministic sealed-artifact resume; cross-Attempt artifact reuse; rerun recovery; history-derived admission; approver recovery; encoded/split/runtime-download analysis; arbitrary external/novel layouts; Tree-sitter/dataflow interpretation; tag-only/delete/restore/visibility/permission/admin actions; remediation redesign; simulation rerun changes; finalization watchdogs; unauthorized obsolete-Environment deletion; activation/dispatch/package mutation through this document; and release pipelines for other projects.

These items are bounded unsupported capabilities, not unresolved first-slice decisions.
