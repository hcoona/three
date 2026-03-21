# GitHub Workflows Design (v2.26)

This document describes the intended GitHub Actions design for the `three` monorepo.

> **Implementation prerequisite:** This design assumes the repository has already completed the per-project root migration so every releasable project resolves to one stable project root with its own checked-in release metadata. `AGENTS.md` currently says the repository has **not** completed that migration yet. Therefore this document is a target design, not an immediately implementable plan.

> **Scope constraint:** Each project maps to exactly one language ecosystem and exactly one checked-in build kind. Multi-language or multi-build-kind projects are out of scope and must be split into separate project directories with separate `release.json` files before implementation.

> **Release-unit constraint:** Each `buddy.yml` or `official.yml` run releases exactly one project.

> **Identity split:** Manual workflow interfaces expose `project-name`. It must equal the canonical repository-safe internal key used elsewhere in this document as `project-key`; there is no compatibility alias, lossy normalization, or separate external/internal naming layer in this target design. `packageIdentity` is the external package identifier actually published to the target ecosystem and may be scoped or case-sensitive. `packageManifestPath` is the explicit repo-relative manifest or project-file path for that package. Internal workflow logic must never derive `packageIdentity` by normalizing `project-key`.

The repository must also carry one checked-in machine-readable repository release contract, `.github/repository-release-contract.json`, as the single source of truth for repository-side release prerequisites, PR trust-model rules, protected-ref requirements, environment contracts, and target-auth contracts. `ci.yml` must validate in every PR that workflow code and checked-in docs have not drifted from that contract.

## 1. Architecture Overview

The externally exposed entry workflows remain exactly:

- `ci.yml`
- `buddy.yml`
- `official.yml`

No additional triggered top-level workflows are part of this design. In particular, health-monitor, readiness, governance, drift, or scheduled admission workflows are out of scope. If a capability is required, it must live in one of the three entry workflows or in checked-in repository state.

The shared execution layer is:

- reusable build/test workflows under `.github/workflows/_build-test-*.yml`
- reusable attestation workflows under `.github/workflows/_attest-build-*.yml`
- reviewed local composite actions under `.github/actions/**`
- reviewed helper scripts under `eng/scripts/**`

Security-sensitive publication stays in direct jobs inside `buddy.yml` or `official.yml`. Reusable publish workflows are intentionally out of scope because they are not a meaningful authorization boundary for the buddy path.

## 2. `ci.yml` — Pull Request Validation

Main responsibilities:

1. Run repository static analysis through HK.
2. Detect affected ecosystems and build kinds.
3. Build, test, and package only the affected ecosystem/build-kind suites when those steps are required by the resolved ecosystem/build-kind path, unless infrastructure changes require a broader run.
4. Validate that workflow and docs changes do not drift from `.github/repository-release-contract.json`.
5. Finish with one final gate job suitable for branch protection.

Design rules:

- HK is repository-wide, not project-specific.
- Infrastructure and shared control-plane changes must trigger all ecosystem/build-kind suites.
- Ecosystem build/test execution uses static reusable-workflow calls such as `_build-test-csharp.yml`, `_build-test-python.yml`, `_build-test-node-npm.yml`, `_build-test-node-wxt.yml`, and `_build-test-ruby.yml`.
- The reusable runner contract is part of the design: `csharp-pack` uses `windows-latest`, while `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` use `ubuntu-latest`.
- `ci.yml` must parse the checked-in repository release contract and fail the PR when workflow code or checked-in docs drift from that machine-readable source of truth.
- PR validation for untrusted code must use `pull_request`, not `pull_request_target`.
- If `pull_request_target` is ever used for a repository-maintenance task, it must not check out, execute, or source code from the PR head and must not mint publish, protected-ref-write, or other privileged release credentials.
- Fork PRs are always untrusted input. PR workflows, including fork PR workflows, must never receive repository secrets, environment-scoped publication credentials, or protected-ref-write credentials.
- Repository settings that would grant fork PRs secrets or privileged write tokens are out of scope for this design and must remain disabled.
- PR workflows must never publish artifacts to external registries, create releases, or mutate protected refs.
- The final gate job must succeed when the required build/test/package work succeeded, even if some ecosystem jobs were intentionally skipped.

## 3. `buddy.yml` — Unofficial Release

`buddy.yml` is the manual workflow for unofficial releases. It is independent of `official.yml`. The `workflow_dispatch` interface exposes only `project-name`; the branch selected in the UI supplies the single frozen buddy snapshot for that run. There is no separate `source-branch` input. After preflight freezes that selected branch to an immutable commit, every later buddy step must use that same snapshot for both workflow/control-plane files and release payload files.

### 3.1 Responsibilities

1. Resolve exactly one project from repository state.
2. Run bounded static analysis for that project plus buddy control-plane files.
3. Build, test, and package exactly one ecosystem/build-kind path when that path requires packaging.
4. Publish only unofficial targets.

### 3.2 Job outline

1. **`resolve-context`**
   - validates workflow input `project-name` against the repository-safe lowercase pattern and rejects any non-exact mapping to the canonical internal `project-key`
   - requires the selected `workflow_dispatch` ref to be a branch ref, not a tag ref
   - freezes immutable buddy `dispatchSha` from the `workflow_dispatch` event snapshot of the selected branch
   - resolves `project-path` from `.github/repository-release-contract.json` at `dispatchSha`, then resolves `packageIdentity`, `packageManifestPath`, `ecosystem`, `build-kind`, and version from that same frozen `dispatchSha` snapshot
   - strictly validates `<project-root>/release.json` from `dispatchSha`
   - validates project existence, uniqueness, ecosystem/build-kind shape, and target compatibility before any channel filtering
   - filters to the unofficial target set
   - fails if the filtered unofficial set is empty

2. **`static-analysis`**
   - runs after `resolve-context`
   - uses frozen `dispatchSha` for both workflow/control-plane files and every file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection
   - runs `hk check` over the resolved project path from `dispatchSha`, any shared/root build inputs from `dispatchSha`, plus the shared buddy control-plane surface from that same snapshot:
      - from `dispatchSha`: `.github/workflows/buddy.yml`, `.github/workflows/_build-test-*.yml`, `.github/actions/**`, `eng/scripts/**`, `hk.pkl`, and other pure control-plane rule code
      - from `dispatchSha`: the resolved project path plus any shared/root files actually consumed by the resolved ecosystem/build-kind path, including files such as `mise.toml`, `mise.lock`, root lockfiles, or root config when they affect the buddy build inputs
    - must not silently collapse to project-only analysis when shared buddy release files are in scope

3. **One static conditional build job**
    - exactly one of `build-csharp`, `build-python`, `build-node-npm`, `build-node-wxt`, or `build-ruby` runs
    - each uses the matching reusable build/test workflow selected by `(ecosystem, build-kind)`
    - the runner contract is fixed by `buildKind`: `csharp-pack` on `windows-latest`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-latest`
    - the selected path must run compile/build work, unit tests, and package/pack validation whenever that build kind requires packaging
    - workflow-owned local actions, helper scripts, and project build inputs all come from the same frozen selected-branch snapshot captured as `dispatchSha`
    - build artifacts are produced fresh inside the workflow run together with a digest manifest for every publishable file
    - direct publish jobs consume only those current-run artifacts and digest-manifest entries; they do not rebuild

4. **Direct publish jobs**
   - one direct job per supported buddy target
   - no same-repository reusable publish workflow is used as the authorization boundary
   - shared step logic may be implemented through reviewed local composite actions or scripts

### 3.3 Buddy authorization boundary

The authorization boundary for buddy publishing is the direct job itself plus the repository-side controls that scope buddy credentials to manual buddy runs:

- job-level `permissions`
- direct jobs whose workflow/control-plane code is rooted in the selected `workflow_dispatch` snapshot
- dedicated buddy publication environments and credentials that are available only to documented buddy jobs
- repository review on the direct job wiring and helper code at the frozen `dispatchSha`

This design does **not** treat "same-repository reusable workflow plus caller-supplied path input" as a real authorization boundary. Buddy is intentionally an unofficial channel that may run from development branches, so the manual dispatch-selected branch is the workflow snapshot for that run; there is no hidden second control branch or separate `source-branch` input.

### 3.4 Buddy targets

Buddy filters to this unofficial target set:

- `nuget:gpr`
- `npm:gpr`
- `rubygems:gpr`
- `github:release`

Python has no unofficial package-registry target. If a Python project needs a buddy preview, it must declare `github:release` and buddy publishes that preview as a GitHub prerelease. `pypi:testpypi` is not a supported target.

### 3.5 Buddy GitHub Release identity and auth

- Buddy GitHub Release identity is separate from the official release identity.
- The buddy tag format is `buddy/<project-key>/v<version>/<dispatchSha>`.
- Buddy `github:release` must always attach to that already-derived buddy tag; it must not reuse the official `release/<project-key>/v<version>` namespace.
- A buddy rerun is idempotent only when the existing buddy tag already points to the same frozen buddy `dispatchSha`, the existing release is attached to that same buddy tag, and the live release asset set exactly matches the current-run immutable artifact set plus digest manifest; otherwise it is a hard conflict.
- For buddy `github:release`, missing, extra, renamed, or digest-mismatched release assets are conflicts, not same-identity no-ops.
- Buddy registry targets use target-specific idempotent publish helpers that consume the current-run immutable artifact set plus digest manifest.
- A buddy registry publish may be treated as a same-identity no-op only when live remote state proves the already-present package version corresponds to the same frozen buddy identity and the same current-run artifact identity; version-only matches are insufficient.
- If a registry already contains the requested version but remote state cannot prove same-identity, or proves different bytes/metadata for that buddy identity, the workflow must hard-fail rather than overwrite or silently accept the conflict.
- Buddy publish jobs must enter pre-created buddy publication environments before any external mutation.
- Buddy publication credentials must be explicitly minted or injected inside those environments. Ambient credential availability outside the documented target auth contract is not the authorization boundary for buddy publishing.
- For `github:release`, use a dedicated GitHub Release credential such as a GitHub App installation token or an equivalently reviewed brokered credential with only the repository permissions required for release publication, and do not reuse the protected official ref-write actor.
- For GitHub Packages buddy targets (`nuget:gpr`, `npm:gpr`, and `rubygems:gpr`), use the documented GitHub Packages auth contract for that ecosystem, normally job-scoped `GITHUB_TOKEN` with `packages: write`; any stronger GitHub-native package credential is a repository hardening choice that must be documented target-by-target in the checked-in release contract rather than implied by the generic term "GitHub-native".
- Buddy publishing must not use long-lived repository secrets.

## 4. `official.yml` — Production Release

`official.yml` is the manual production release workflow. It is independent of `buddy.yml`.

The official release tag format is:

- `release/<project-key>/v<version>`

`official.yml` derives that tag internally. It is not a workflow input.

`official.yml` has one supported manual interface: workflow input `project-name` plus the protected branch selected in the `workflow_dispatch` UI. For a normal official release, that selected protected branch is the single trust root for the run: it supplies the trusted workflow/control-plane code, the checked-in release policy inputs, and the release payload source. The workflow must freeze that selected branch to an immutable snapshot before downstream work begins. There is no separate `source-branch` input and no hidden control-branch override.

### 4.1 Official repository prerequisites

Official release enablement for a project is allowed only after these repository-side controls already exist:

- every protected branch that may dispatch `official.yml` for that project is covered by a branch protection rule or ruleset that at minimum:
  - prevents force-push and deletion
  - requires the repository's official CI gate before merge
  - requires reviewed changes or an explicitly restricted bypass path
- the selected protected branch must itself be the authoritative release line for the resolved project version: `main` for the current mainline release line, or `release/<project-key>/v<release-line>` for a maintenance line
- the official tag namespace `refs/tags/release/<project-key>/v*` is covered by a tag-targeted ruleset
- the live official lock tag `refs/tags/official-lock/<project-key>` is covered by a tag-targeted ruleset
- `.github/repository-release-contract.json` and `.github/official-admission-state.json` exist, are reviewed on every protected official release branch that may dispatch `official.yml`, and are the checked-in policy inputs relied on by that branch's run snapshot
- every official environment that can grant approval or credentials is pre-created and configured so only runs dispatched from the allowed protected official release branches may enter it
- those tag-targeted rulesets explicitly allow only the documented official ref-write automation identity plus the documented break-glass actor to create, update, or delete refs in those two protected tag namespaces
- the official `github:release` publisher identity is a different GitHub App or automation actor from the protected-ref writer, and the protected tag rulesets do **not** allow the release-publisher actor

Official publish jobs must not rely on "protected branch" alone as the full trust-root prerequisite. Protected selected-branch workflow code, protected official tags, and the protected live-lock tag are separate requirements, and this design does not assume GitHub provides one generic "protected ref namespace" primitive outside branches and tags. In-workflow protected-branch checks validate the selected release line; they are not by themselves a trust boundary against branch-local workflow edits.

### 4.2 Official environment model

The authoritative human approval gate is the baseline project environment:

- `production-<project-key>`

That baseline environment is the only required human approval gate in this design.

Target-specific mechanics may still exist, but they are subordinate to the baseline gate rather than replacements for it. Examples:

- `production-nuget-<project-key>`
- `production-npm-<project-key>`
- `production-pypi-<project-key>`
- `production-rubygems-<project-key>`
- `production-github-<project-key>`
- `production-ref-write-<project-key>`
- `production-evidence-write-<project-key>`

Those subordinate environments are for narrowly scoped credentials or target-specific variables only. They must not become an alternate human approval model.

The baseline environment must encode a real approval boundary, not just a name. The minimum contract is:

- at least one required reviewer user or team
- `prevent self-review` enabled
- deployment-branch policy or equivalent repository-side restriction that allows entry only from the protected official release branches allowed by the checked-in release contract
- explicit documented admin-bypass policy for this environment; if any admin bypass is allowed, it is break-glass only and not part of the normal release path
- the baseline reviewer population should be administratively narrower than the routine workflow-dispatch caller population
- only the single `baseline-approval-and-audit` job may depend on reviewer-gated baseline approval; later jobs may consume its outputs but must not create a second reviewer-gated environment boundary

### 4.3 Control-plane trust root and preflight sequencing

For a normal official release, the branch selected in the `workflow_dispatch` UI is the workflow-code, checked-in-policy, and payload trust root. `preflight-validate` must distinguish the current branch snapshot that authorizes the run from the frozen release identity that later jobs actually publish:

- `policy-sha` is the immutable `workflow_dispatch` event snapshot commit for the selected protected branch
- `release-plan` is the immutable canonical release plan consumed by build, test, provenance, tag, and publish jobs
- `release-plan.planDigest` is the canonical digest of the frozen release plan used for lock comparison and recovery identity
- `release-plan.payloadSha` is the immutable source commit snapshot whose build outputs are published when the run needs to build; for a normal release it equals `policy-sha`, while for a reviewed recovery it remains the frozen blocked-plan payload snapshot

The frozen `release-plan` contains:

- `planDigest`
- `projectKey`
- `projectPath`
- `packageIdentity`
- `packageManifestPath`
- `ecosystem`
- `buildKind`
- `version`
- `authorizedBranch`
- filtered official target set
- target-to-artifact routing
- per-target auth contract
- official release tag name
- `payloadSha`

For a normal official release, `preflight-validate` derives the entire `release-plan` from the single frozen snapshot selected in `workflow_dispatch`. `release-plan.payloadSha` therefore equals `policy-sha` for that run.

For a reviewed recovery run, `preflight-validate` must load the entire frozen `release-plan`, the blocked-stage discriminator, and the original artifact identity when that identity already exists in the blocked checked-in admission state entry. `policy-sha` in recovery authorizes whether that blocked plan may be resumed from the currently selected protected branch, but it must not overwrite `project-path`, `packageIdentity`, `packageManifestPath`, `ecosystem`, `build-kind`, `version`, target selection, target-to-artifact routing, target auth contract, official tag identity, or `payloadSha` with newly re-derived values from the newer branch snapshot.

After `preflight-validate` freezes `policy-sha`, `release-plan`, the blocked-stage discriminator, and any persisted recovery artifact identity, no later job may re-resolve the selected branch HEAD. The selected branch name remains an audit and authorization input only after the frozen values are derived.

The frozen inputs therefore supply:

- current trusted control-plane code and checked-in authorization/evidence from `policy-sha`
- the immutable release identity from `release-plan`
- for a normal release, the selected protected branch snapshot as both policy and payload source
- for recovery, the original frozen blocked payload identity and, when present, the original durable artifact identity

This design intentionally anchors official dispatcher behavior, authorization checks, and protected-ref mutation logic to the protected branch selected in `workflow_dispatch` plus repository-side environment/credential restrictions that only admit supported protected-branch runs. An in-workflow "run must be from main" check is not part of this model and must not be used as a substitute for selecting the correct protected release branch in the dispatch UI.

All project canonicalization, release-plan validation, and baseline-environment safety checks happen before any environment with secrets is entered.

Job sequence:

1. **`preflight-validate`** — no environment
    - validates workflow input `project-name` and resolves it only to the exact canonical internal `project-key`
    - requires the selected `workflow_dispatch` ref to be a branch ref, not a tag ref
    - requires the selected branch to be a protected branch
    - freezes the immutable `policy-sha` from the `workflow_dispatch` event snapshot of that selected protected branch
    - reads the checked-in admission/recovery state file from the frozen `policy-sha` and fails closed if the file, schema, or selected project entry is missing or invalid
    - for a new release, reads `.github/repository-release-contract.json`, `release.json`, and `packageManifestPath` from that same frozen `policy-sha` snapshot to resolve the selected `project-key` to one `project-path`, one release-enabled baseline environment contract, protected-ref requirements, per-target auth contract, `packageIdentity`, `ecosystem`, `buildKind`, version, official-branch mode, optional `release-line`, targets, artifact catalog, and target-to-artifact routing, then constructs the canonical `release-plan` with `payloadSha = policy-sha`
    - for an approved recovery, loads the full frozen `release-plan`, the blocked-stage discriminator, any existing artifact identity, and machine-readable reviewed recovery authorization from the blocked admission entry; recovery must not rewrite any release-identity field from current checked-in project metadata
    - validates project existence, uniqueness, and single-ecosystem/single-build-kind shape for a new release plan from `policy-sha`; for recovery it validates that the requested `project-name` and selected protected dispatch branch match the frozen blocked plan being resumed
    - for a new release, strictly validates `release.json` from `policy-sha` and the constructed release plan before any channel filtering; for recovery it validates only the blocked-entry schema and current authorization facts, not a replacement identity from current `release.json`
    - validates ecosystem/build-kind compatibility and target-to-artifact routing completeness for the frozen plan that will actually be published
    - filters to the official target set only while constructing a new release plan
    - verifies that the selected protected dispatch branch itself matches the authoritative branch rule for the frozen plan:
       - `officialBranchMode = main` requires `main`
       - `officialBranchMode = release-line` requires `release/<project-key>/v<release-line>`
    - derives or verifies the official release tag name carried by the frozen plan and computes the canonical `planDigest`
    - checks that `.github/repository-release-contract.json` contains a complete, release-enabled entry for the selected `project-key`, then validates required environment names, live-lock requirements, ref-write requirements, target-auth completeness, artifact-routing completeness, and release-tag conflicts
    - performs a bounded GitHub-side non-mutating check of the protected live lock tag `refs/tags/official-lock/<project-key>` and fails closed on any conflicting open lock; a reviewed recovery run may proceed only when that existing annotated lock record carries the same frozen `planDigest`
    - performs a bounded GitHub-side non-mutating check that `production-<project-key>` already exists and matches the required protection policy before any environment entry
    - emits validated outputs including `policy-sha`, the frozen `release-plan`, the blocked-stage discriminator, any persisted blocked artifact identity, and release identity for downstream jobs

2. **`static-analysis`**
    - runs after `preflight-validate`
    - uses the frozen `policy-sha` only for workflow/control-plane files and the frozen `release-plan.payloadSha` for every file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection
    - runs `hk check` over the resolved project path from `release-plan.payloadSha`, any payload-scoped shared/root build inputs from `release-plan.payloadSha`, plus the official release control-plane surface from `policy-sha`:
      - from `policy-sha`: `.github/workflows/official.yml`, `.github/workflows/_build-test-*.yml`, `.github/workflows/_attest-build-*.yml`, `.github/actions/**`, `eng/scripts/**`, `hk.pkl`, and other pure control-plane rule code
      - from `release-plan.payloadSha`: the resolved project path plus any shared/root files actually consumed by the official ecosystem/build-kind path, including files such as `mise.toml`, `mise.lock`, root lockfiles, or root config when they affect the official build inputs
    - must not be reduced to project-path-only validation

3. **`baseline-approval-and-audit`** — `environment: production-<project-key>`
   - is the authoritative human approval gate
   - runs only after successful `preflight-validate` and `static-analysis`
   - consumes only validated outputs from `preflight-validate`
   - does **not** re-resolve the project, targets, version, or payload SHA after environment entry
   - the approval request or equivalent reviewed approval surface must display the frozen release identity: `payloadSha`, `packageIdentity`, `version`, `officialTag`, and `planDigest`
   - verifies that subordinate target/tag/evidence environments required by the validated plan exist and match policy
   - fails if any required subordinate environment is missing, because GitHub may auto-create unprotected environments on first reference
   - verifies that subordinate environments do not introduce an unintended second human-approval gate unless the design explicitly requires one
     - re-reads the current selected protected branch head immediately after approval and fails closed if it no longer equals the frozen `policy-sha`; a reviewed recovery still keeps `release-plan.payloadSha` pinned to the blocked plan rather than replacing it with the newer branch head
   - performs any approved live GitHub-side audit or provider-specific non-mutating checks using the already validated plan
   - emits audited environment facts for downstream jobs

The environment-backed job is therefore an audit-and-admission consumer, not a second resolver.

Every official job that executes repository-owned actions or scripts must use two fixed checkouts: `control-root/` at `policy-sha` and `payload-root/` at `release-plan.payloadSha`. Local composite actions under `.github/actions/**`, helper scripts under `eng/scripts/**`, and other workflow-authored control-plane code must execute only from `control-root/`. Project build/test/package commands operate on files under `payload-root/`. Any file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection—including shared/root toolchain files, lockfiles, and config consumed by the selected build kind—belongs to the payload side and must be read from `payload-root/`, not `control-root/`. `control-root/` is limited to workflow files, local actions, helper scripts, HK policy, and other pure control-plane rule code. The workflow must not place the payload checkout at the default path in a way that lets `uses: ./.github/actions/...` or `bash eng/scripts/...` resolve from payload content.

### 4.4 Official job outline after approval

The official flow has two distinct publish paths:

- **New release path:** build, test, and package from the frozen `release-plan.payloadSha`; generate fresh attestation subjects from that current-run immutable artifact set; create the live lock before any durable artifact-store write; persist one authoritative artifact identity; create or verify the official release tag carrying that canonical success identity; then publish.
- **Reviewed recovery path:** use the blocked entry's machine-readable `blockedStage` to select exactly one recovery mode for the already-frozen release plan. `pre-provenance` recovery rebuilds and retests from the frozen `release-plan.payloadSha` because no authoritative durable artifact identity existed yet. `post-provenance` recovery restores the previously persisted immutable bundle from `artifactLocator`, verifies the restored bytes, persisted subject digests, and persisted `attestationRef`, then republishes from that restored bundle. Recovery must not derive a new release identity from the newer dispatch branch snapshot.

4. **One static conditional build-test-package-preparation job**
    - exactly one preparation path runs for the resolved `(ecosystem, build-kind)`
    - the runner contract is fixed by `buildKind`: `csharp-pack` on `windows-latest`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-latest`
    - workflow-owned local actions and helper scripts execute from `control-root/`; project build inputs come only from `payload-root/`
    - for a new release, this job must execute the build-kind's required compile/build work, unit tests, and package/pack validation against the frozen `release-plan.payloadSha`, then emit one immutable release-artifact set plus a digest manifest for every file allowed to reach any publish destination
    - for a reviewed recovery with `blockedStage = pre-provenance`, this job reruns the documented build/test/package path from the frozen `release-plan.payloadSha` and emits a fresh immutable artifact set plus digest manifest for first-time provenance capture for that already-frozen plan
    - for a reviewed recovery with `blockedStage = post-provenance`, this job must restore the previously persisted immutable artifact bundle from the blocked entry's durable `artifactLocator`, must not rebuild or repackage, and must emit the restored digest manifest for downstream verification
    - self-hosted runners are out of scope

5. **One static conditional attestation/verification job**
   - exactly one attestation/verification workflow runs
   - for a new release, it generates attestation from the current run's build output only
   - for a reviewed recovery with `blockedStage = pre-provenance`, it generates attestation from that recovery run's rebuilt artifact set for the already-frozen plan because no persisted attestation exists yet
   - for a reviewed recovery with `blockedStage = post-provenance`, it reuses the previously recorded `attestationRef` and verifies that the restored digest manifest exactly matches the persisted blocked artifact identity
   - attestation subjects must be the digest-manifest entries for the immutable artifact set actually published; publish-only repackaging is forbidden

6. **`create-live-lock`**
    - runs only after successful build-test-package-preparation and attestation/verification
    - enters only the ref-write environment required for the selected project
    - must create or verify the protected live official lock tag `refs/tags/official-lock/<project-key>` for the same frozen `release-plan` before the first irreversible external mutation of the run, including any durable artifact-store write
    - that live lock must be an annotated tag whose annotation payload carries at minimum the frozen `planDigest`, `payloadSha`, `packageIdentity`, `version`, `officialTag`, and the timestamp/actor that established the lock
    - uses only the dedicated repository-ref-write credential defined in §4.8

7. **`require-provenance`**
    - runs only after successful `create-live-lock`
    - enters only the evidence-write environment required for the selected project
    - consumes validated outputs from preflight jobs instead of redoing canonicalization
    - verifies that the attestation subjects exactly match the immutable artifact digest manifest selected for publication
    - for a new release, writes the immutable artifact bundle, digest manifest, and attestation/provenance record to the durable artifact store using only the credential scoped to `production-evidence-write-<project-key>`
    - for a new release, that durable write must use create-if-absent / write-once semantics keyed by `planDigest` (or an equivalent same-plan uniqueness key). The store may return the already-recorded identity for the same frozen plan, but it must fail closed if the same `planDigest` would otherwise map to a different `artifactLocator`, `attestationRef`, or subject-digest set.
    - for a new release, emits durable `artifactLocator`, `attestationRef`, and artifact-subject outputs only after the immutable write succeeds; those outputs become the authoritative artifact identity for the frozen release plan before the official release tag or any publish job starts
    - for a blocked recovery with `blockedStage = pre-provenance`, performs the first durable write for that already-frozen plan using the same create-if-absent / write-once semantics keyed by `planDigest`, then emits the newly created `artifactLocator`, `attestationRef`, and artifact-subject map; reviewed recovery authorization must explicitly allow `rerun-plan` for this stage
    - for a blocked recovery with `blockedStage = post-provenance`, verifies that the selected publication artifact set exactly matches the persisted blocked artifact identity before any further external mutation starts, and re-emits the already-recorded `artifactLocator`, `attestationRef`, and artifact-subject map without replacing them in-run

8. **`create-release-tag`**
    - runs only after successful `require-provenance`
    - enters only the ref-write environment required for the selected project
    - creates or verifies the protected official release tag `refs/tags/release/<project-key>/v<version>`
    - the official release tag must be an annotated tag whose annotation payload durably records the canonical frozen success identity for that release, including at minimum the frozen `planDigest`, `payloadSha`, `packageIdentity`, `version`, `officialTag`, the canonical frozen `release-plan` JSON used to compute `planDigest`, `artifactLocator`, `attestationRef`, and the exact artifact subject-digest map
    - uses only the frozen `release-plan.payloadSha`
    - uses only the dedicated repository-ref-write credential defined in §4.8
    - must complete successfully before any official publish job starts

9. **Direct publish jobs**
   - one direct job per official target
   - each job mutates exactly one destination
   - each job depends on successful baseline approval, validated preflight outputs, successful build-test-package-preparation, provenance, and tag creation
   - each job consumes only the immutable artifact set and digest manifest selected and verified earlier in the run
   - publish jobs must not rebuild, repackage, or substitute files after attestation
   - `release-plan.payloadSha` is metadata for identity and audit only at publish time; the publish bytes come from the attested or restored immutable artifact set named by the frozen plan and artifact identity
   - shared step logic may be implemented through reviewed local composite actions or scripts

10. **`confirm-publish-state`**
    - confirms selected destinations from live remote state
    - uses bounded retries and explicit timeouts
    - records results for the current run only
    - does not scan unbounded historical run state

11. **`release-complete`**
    - final aggregation gate for the workflow
    - on successful confirmation of every selected destination, clears the protected live official lock tag for the selected project using the same dedicated repository-ref-write credential
    - lock-clear failure is a release failure because a stale lock must not silently remain after a completed release

### 4.5 Official targets

Official filters to this target set:

- `nuget:official`
- `npm:official`
- `pypi:official`
- `rubygems:official`
- `github:release`

`github:release` is the GitHub Release target for both buddy and official channels. The channel determines behavior:

- buddy uses it for unofficial preview/prerelease publication
- official uses it for the production release on the protected official release identity

There is no separate `github:official` target.

For the official channel, `github:release` must attach to the already-created official tag `release/<project-key>/v<version>`. It must fail closed if GitHub would need to auto-create or retarget that tag.
For official `github:release`, same-identity acceptance requires both the protected annotated official tag identity and an exact match between the live GitHub Release asset set and the authoritative artifact identity for the frozen plan. Tag-only equality is insufficient.

### 4.6 External-system checks

This design does not depend on extra scheduled readiness workflows or aging snapshot artifacts.

If a provider-specific readiness or authorization check is still required, it must be either:

- a checked-in policy fact consumed during `preflight-validate`, or
- a bounded same-run check performed in `baseline-approval-and-audit` after the baseline approval gate

No official admission decision may depend on scanning arbitrarily old workflow runs.

### 4.7 Baseline and subordinate environment requirements

- `production-<project-key>` must be pre-created before the workflow is enabled for that project.
- That environment must carry the expected protection rules; a missing or unprotected baseline environment is a hard failure.
- The minimum acceptable baseline protection policy is the same minimum contract defined in §4.2:
  - at least one required reviewer user or team
  - `prevent self-review` enabled
  - deployment-branch policy or equivalent repository-side restriction that allows entry only from the protected official release branches allowed by the checked-in release contract
  - explicit documented admin-bypass policy
  - the baseline reviewer population should be administratively narrower than the routine workflow-dispatch caller population
  - only the single `baseline-approval-and-audit` job may depend on reviewer-gated baseline approval; later jobs may consume its outputs but must not create a second reviewer-gated environment boundary
- The baseline environment should be used for approval and narrowly scoped audit facts only. It must not be treated as the default storage location for publication credentials.
- Any subordinate environment referenced by the validated plan must also be pre-created before workflow enablement.
- If the design intends `production-<project-key>` to be the only required human gate, subordinate environments must not independently require human reviewers.
- Referencing a missing environment is never an acceptable bootstrap path, because GitHub may auto-create it without the required protection semantics.

### 4.8 Protected repository-ref write contract

This design uses only concrete GitHub tag refs for protected official repository writes:

- `refs/tags/release/<project-key>/v<version>`
- `refs/tags/official-lock/<project-key>`

The repository-ref write contract is:

- only `create-live-lock`, `create-release-tag`, and `release-complete` may mutate those refs
- those jobs must enter `production-ref-write-<project-key>`
- the credential used there is a dedicated GitHub App installation token for this repository; by design `GITHUB_TOKEN` is not the protected-ref writer for official release/tag-lock operations
- that GitHub App must hold only the repository permissions required for reviewed protected-tag mutation, must be distinct from the actor used by official `github:release`, and the workflow must mint its installation token inside `production-ref-write-<project-key>` from either an environment-scoped App private key or an equivalently reviewed brokered issuance path
- ref-level restrictions are enforced by the corresponding tag-targeted rulesets, not by the token alone
- the corresponding tag-targeted rulesets must allow only that ref-write GitHub App actor plus the documented break-glass actor to create, update, or delete the protected release-tag and live-lock refs
- the official `github:release` publisher actor must not appear on the protected-tag bypass list; environment names alone are not a separation boundary if the same actor can be minted in both environments
- environment approval does not itself bypass protected tag rules; the credential and actor allowance must already be correct

### 4.9 Official target authentication contract

Official target authentication must be explicit and target-scoped. Repository-level long-lived publication secrets are out of scope.

| Target | Auth class | Required subordinate environment | Credential rule |
| --- | --- | --- | --- |
| `github:release` | GitHub-native API auth | `production-github-<project-key>` | Use a dedicated GitHub App installation token or equivalently reviewed brokered credential minted inside this environment. It must be a different actor from the protected ref-writer in §4.8, must not appear on the protected-tag bypass list, and long-lived secrets are forbidden. |
| `nuget:official` | External-registry API key | `production-nuget-<project-key>` | Use one narrowly scoped NuGet credential only in this environment. GitHub OIDC trusted publishing is not available here as a comparable official auth path in this design revision, so this provider remains the explicit credential-based exception. |
| `npm:official` | External-registry OIDC trusted publishing | `production-npm-<project-key>` | Use npm trusted publishing as the normal registry-auth path. Provider-side trust must pin the exact repository, exact official workflow identity, exact environment when supported, the narrowest supported ref/subject claims, and the minimal audience. If npm provenance/signing is also emitted, that is an additional concern layered on the same publish job rather than a substitute auth mechanism. Only the publish job may receive `id-token: write`. |
| `pypi:official` | External-registry OIDC trusted publishing | `production-pypi-<project-key>` | Use trusted publishing only. Provider-side trust must pin the exact repository, exact official workflow identity, exact environment when supported, the narrowest supported ref/subject claims, and the minimal audience. Only the publish job may receive `id-token: write`. |
| `rubygems:official` | External-registry OIDC trusted publishing | `production-rubygems-<project-key>` | Use trusted publishing only. Provider-side trust must pin the exact repository, exact official workflow identity, exact environment when supported, the narrowest supported ref/subject claims, and the minimal audience. Only the publish job may receive `id-token: write`. |

The validated official release plan must include `targetAuthContracts` keyed by target. Each entry contains at minimum the required environment name, auth class, allowed credential source, and any provider-side trust requirements that are part of the release contract for that target. Those contracts come from `.github/repository-release-contract.json`, not from ad-hoc workflow defaults. A target with no documented auth contract is not releaseable.

## 5. Release Configuration Contract

Each releasable project must define `<project-root>/release.json`.

### 5.1 Schema

```json
{
  "schemaVersion": 1,
  "packageIdentity": "@three/example-package",
  "packageManifestPath": "src/example-project/package.json",
  "buildKind": "node-npm",
  "officialBranchMode": "release-line",
  "releaseLine": "1.2",
  "targets": ["npm:gpr", "npm:official", "github:release"],
  "artifacts": {
    "package": { "kind": "npm-package" }
  },
  "targetArtifacts": {
    "npm:gpr": ["package"],
    "npm:official": ["package"],
    "github:release": ["package"]
  }
}
```

### 5.2 Fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `schemaVersion` | `number` | Yes | Must be `1`. |
| `packageIdentity` | `string` | Yes | Exact external package identifier published to the ecosystem. It is not normalized from `project-key` and may be scoped or case-sensitive when the target ecosystem allows it. |
| `packageManifestPath` | `string` | Yes | Explicit repo-relative path to the manifest or project file that defines `packageIdentity`, such as `package.json`, `pyproject.toml`, `.gemspec`, or the releasable `.csproj`. |
| `buildKind` | `string` | Yes | Checked-in build/publish routing discriminator. Supported values are `csharp-pack`, `python-package`, `node-npm`, `node-wxt`, and `ruby-gem`. |
| `officialBranchMode` | `string` | Yes | Closed set `{main, release-line}` defining which protected branch shape may authorize official releases for the current project version. |
| `releaseLine` | `string` | Conditionally | Required when `officialBranchMode = release-line`; checked-in branch discriminator used to form `release/<project-key>/v<releaseLine>`. |
| `targets` | `string[]` | Yes | Non-empty array of unique publish targets in `ecosystem:destination` format. |
| `artifacts` | `object` | Yes | Non-empty artifact catalog keyed by checked-in artifact alias. Each alias declares one canonical artifact `kind` produced by the selected `buildKind`. |
| `targetArtifacts` | `object` | Yes | Exact target-to-artifact routing map. Each declared target must map to a non-empty array of artifact aliases from `artifacts`. |
| `npmAccessHint` | `string` | No | Optional checked-in npm access hint for `node-npm` projects declaring `npm:*`; closed set `{public, restricted}`. |

### 5.3 Validation rules

- `release.json` must be valid JSON.
- `schemaVersion` must equal `1`.
- `packageIdentity` must be present and non-empty.
- `packageManifestPath` must be present, must point to exactly one repo-relative manifest or project file under the resolved project root, and that file must resolve the same `packageIdentity` observed by the ecosystem-specific resolver.
- `buildKind` must be one of the documented supported values.
- `officialBranchMode` must be either `main` or `release-line`.
- `releaseLine` is required only when `officialBranchMode = release-line`.
- `targets` must be a non-empty array of unique strings.
- `artifacts` must be a non-empty object keyed by safe lowercase aliases matching `[a-z0-9][a-z0-9._-]*`.
- Every `artifacts.<alias>` entry must be an object with exactly one supported `kind`.
- `targetArtifacts` must be an object whose key set exactly matches `targets`.
- Each `targetArtifacts.<target>` value must be a non-empty array of unique artifact aliases that all exist in `artifacts`.
- Unknown targets are hard failures.
- Target compatibility is validated against the resolved project ecosystem and `buildKind` before channel filtering.
- Artifact-kind compatibility is validated against the resolved project ecosystem, `buildKind`, and destination before any publish job is considered valid.
- A workflow may filter out valid opposite-channel targets only after strict validation succeeds.
- No top-level keys other than `schemaVersion`, `packageIdentity`, `packageManifestPath`, `buildKind`, `officialBranchMode`, optional `releaseLine`, `targets`, `artifacts`, `targetArtifacts`, and optional `npmAccessHint` are allowed.
- `npmAccessHint` is legal only for `node-npm` projects that declare at least one `npm:*` target.
- Because implementation has not started, removing or changing a target takes effect immediately; no backward-compatibility layer is preserved.

### 5.4 Supported targets

| Target | Channel use | Processed by | Description |
| --- | --- | --- | --- |
| `nuget:gpr` | Buddy only | `buddy.yml` | Publish `.nupkg` to GitHub Packages NuGet feed |
| `nuget:official` | Official only | `official.yml` | Publish `.nupkg` to NuGet.org |
| `npm:gpr` | Buddy only | `buddy.yml` | Publish npm tarball to GitHub Packages npm registry |
| `npm:official` | Official only | `official.yml` | Publish npm tarball to npmjs |
| `pypi:official` | Official only | `official.yml` | Publish wheel/sdist to PyPI |
| `rubygems:gpr` | Buddy only | `buddy.yml` | Publish gem to GitHub Packages RubyGems host |
| `rubygems:official` | Official only | `official.yml` | Publish gem to RubyGems.org |
| `github:release` | Buddy and official | `buddy.yml`, `official.yml` | Publish release assets to GitHub Releases; buddy uses preview/prerelease behavior, official uses the protected production release identity |

`pypi:testpypi` and `github:official` are not supported targets.

### 5.5 Ecosystem/build-kind target compatibility matrix

| Resolved ecosystem | `buildKind` | Allowed targets |
| --- | --- | --- |
| `csharp` | `csharp-pack` | `nuget:*`, `github:release` |
| `python` | `python-package` | `pypi:official`, `github:release` |
| `jsts` | `node-npm` | `npm:*`, `github:release` |
| `jsts` | `node-wxt` | `github:release` |
| `ruby` | `ruby-gem` | `rubygems:*`, `github:release` |

### 5.6 Version resolution and validator contract

Version validation is ecosystem-aware. The workflow must first resolve the project's canonical ecosystem/build-kind identity, then run exactly one validator family for that resolved release path:

| Resolved ecosystem | `buildKind` | Canonical version source | Required validator family |
| --- | --- | --- | --- |
| `csharp` | `csharp-pack` | The releasable package version resolved by the canonical .NET packaging toolchain for the releasable `.csproj` at the frozen SHA | NuGet package version validator |
| `python` | `python-package` | The releasable version resolved by the canonical Python packaging metadata/toolchain at the frozen SHA | PEP 440 public version validator |
| `jsts` | `node-npm` | The releasable version resolved by the canonical Node/npm release path at the frozen SHA | npm SemVer validator |
| `jsts` | `node-wxt` | The releasable version resolved by the canonical Node/WXT release path at the frozen SHA | npm SemVer validator |
| `ruby` | `ruby-gem` | The releasable gem version resolved by the canonical RubyGems release path at the frozen SHA | RubyGems/Gem::Version validator |

Additional rules:

- The version string used for official tags, admission state, lock identity, and duplicate-version checks is the normalized output of the single validator family for the resolved `(ecosystem, buildKind)`.
- No later cross-ecosystem normalizer may rewrite that validated version.
- A manifest literal such as `package.json.version`, `[project].version`, or a `.gemspec` literal is canonical only when that build kind's documented toolchain treats it as the authoritative release version at the frozen SHA.
- If a build path uses git-history-derived version computation such as NBGV, the workflow must fetch full history before resolving and validating the canonical version.

### 5.7 Artifact routing contract

The build workflow and release metadata must together define exactly which immutable files may reach which destinations.

Artifact catalog rules:

- `artifacts` is the checked-in per-project catalog of artifact aliases.
- Each alias declares one canonical artifact `kind`.
- The build workflow must emit digest-manifest entries keyed by those aliases only.
- A publish job may consume only the aliases listed for its target in `targetArtifacts`.

Supported artifact kinds by `buildKind`:

| `buildKind` | Supported artifact kinds |
| --- | --- |
| `csharp-pack` | `nuget-package`, `github-release-asset` |
| `python-package` | `wheel`, `sdist`, `github-release-asset` |
| `node-npm` | `npm-package`, `github-release-asset` |
| `node-wxt` | `github-release-asset` |
| `ruby-gem` | `ruby-gem`, `github-release-asset` |

Destination compatibility rules:

- `nuget:*` targets may reference only `nuget-package`.
- `npm:*` targets may reference only `npm-package`.
- `pypi:official` may reference only `wheel` and/or `sdist`.
- `rubygems:*` targets may reference only `ruby-gem`.
- `github:release` may reference any explicitly declared artifact aliases, but only those aliases.

### 5.8 Project resolution contract

- `project-key` must match `[a-z0-9][a-z0-9._-]*`, must be `1..100` characters, must reject `..`, trailing `.`, and `.lock` suffixes.
- Releasable `project-key` values are canonical ASCII lowercase repository-safe names.
- `.github/repository-release-contract.json` must map each release-enabled `project-key` to exactly one `projectPath`. `ci.yml` must fail if two entries claim the same `project-key`, if one `project-key` maps to multiple project roots, or if a release-enabled project root lacks a contract entry.
- `packageIdentity` is the external package identity and may differ from `project-key`; no workflow or helper may derive it by lowercasing, de-scoping, or otherwise normalizing `project-key`.
- Project resolution starts from the checked-in `project-key` entry in `.github/repository-release-contract.json`, then uses the checked-in ecosystem identity at that resolved project root; it must not depend on leaf-directory-name matching alone.
- C# projects resolve by the `PackageId` declared in the releasable `.csproj` at `packageManifestPath`. Every releasable C# project must set `PackageId` explicitly; directory names, solution names, and `AssemblyName` are not fallback identities.
- Python projects resolve by the `packageIdentity` declared in `pyproject.toml` `[project].name` at `packageManifestPath`.
- `jsts` projects resolve by the `packageIdentity` declared in `package.json` `name` at `packageManifestPath`.
- Ruby projects resolve by the exact `packageIdentity` declared by the `.gemspec` at `packageManifestPath`.
- A resolved project root must map to exactly one supported ecosystem in `{csharp, python, jsts, ruby}`.
- A resolved project root must map to exactly one supported `buildKind`.
- No match, ambiguous match, unsupported ecosystem, unsupported build kind, or multi-language/multi-build-kind match is a hard failure.
- `<project-root>/release.json` is required; there is no inheritance or upward fallback.

### 5.9 Repository release contract

The repository-wide release contract lives at `.github/repository-release-contract.json`. It is the single checked-in machine-readable source of truth for repository-side release readiness and privileged release wiring. `official.yml` must consume it from the frozen `policy-sha`, while `ci.yml` must consume it from the checked-out PR validation snapshot that the `pull_request` run is validating, such as the merge SHA or equivalent checked-out validation commit.

At minimum, that contract must declare for each release-enabled `project-key`:

- the canonical `projectPath`
- whether the project is release-enabled
- the baseline environment name plus every subordinate environment name that `official.yml` may reference
- the protected release-tag pattern and live-lock ref namespace for that project
- the dedicated protected-ref writer actor class and the distinct `github:release` publisher actor class
- target auth contracts keyed by target, including auth class, required environment, allowed credential source, provider-side trust requirements, and whether the target is an explicit credential-based exception such as `nuget:official`

The same contract must also declare the repository PR trust model relied on by `ci.yml`, including that untrusted PR validation uses `pull_request`, any permitted `pull_request_target` use is metadata-only, and settings that would expose fork PRs to secrets or privileged write tokens are not part of the normal design.

`ci.yml` must validate that every release-enabled project's checked-in `release.json` and manifest/package file are consistent with this repository contract, and `official.yml` must fail closed if the selected `project-name` cannot be resolved to a complete, enabled contract entry there.

## 6. Checked-in Admission and Recovery State

Official admission uses bounded checked-in state plus one bounded live lock per project instead of historical workflow-run scanning.

### 6.1 File and live lock

- `.github/official-admission-state.json`
- protected live lock tag `refs/tags/official-lock/<project-key>` as an annotated tag whose annotation payload carries the frozen lock identity

### 6.2 Purpose

The checked-in file on the selected protected official release branch is the authoritative reviewed per-project admission and recovery ledger for that `official.yml` run. `preflight-validate` reads its frozen snapshot at `policy-sha`.

The live lock is the immediate durable blocker for a project and must exist before the first irreversible external mutation of an official run, including any durable artifact-store write. The lock is a concrete protected GitHub annotated tag ref, not an abstract repository ref namespace. Its annotation payload is the minimal comparable lock record for the frozen release plan, headed by `planDigest`.

Together they record whether a project is currently release-eligible or blocked because of an unresolved release-state or control-plane issue. Both sources are intentionally bounded by current project count rather than repository age.

### 6.3 Example shape

```json
{
  "schemaVersion": 1,
  "projects": {
    "example-project": {
      "status": "ready",
      "updatedAt": "2026-03-01T00:00:00Z"
    },
    "blocked-project": {
      "status": "blocked",
      "blockedStage": "post-provenance",
      "frozenPlan": {
        "planDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "projectKey": "blocked-project",
        "projectPath": "src/blocked-project",
        "packageIdentity": "@three/blocked-project",
        "packageManifestPath": "src/blocked-project/package.json",
        "ecosystem": "jsts",
        "buildKind": "node-npm",
        "version": "1.2.3",
        "authorizedBranch": "refs/heads/release/blocked-project/v1.2",
        "payloadSha": "1111111111111111111111111111111111111111",
        "officialTag": "release/blocked-project/v1.2.3",
        "targets": ["npm:official", "github:release"],
        "targetArtifacts": {
          "npm:official": ["package"],
          "github:release": ["package"]
        },
        "targetAuthContracts": {
          "npm:official": {
            "requiredEnvironment": "production-npm-blocked-project",
            "authClass": "external-registry-oidc-trusted-publishing",
            "allowedCredentialSource": "github-oidc"
          },
          "github:release": {
            "requiredEnvironment": "production-github-blocked-project",
            "authClass": "github-app-installation-token",
            "allowedCredentialSource": "environment-issued-github-app-token"
          }
        }
      },
      "artifactIdentity": {
        "artifactLocator": "artifact-store://official/blocked-project/sha256-2222",
        "attestationRef": "attestation-store://official/blocked-project/sha256-2222",
        "subjects": {
          "package": {
            "sha256": "2222222222222222222222222222222222222222222222222222222222222222"
          }
        }
      },
      "reason": "open-partial-publish",
      "evidenceRef": "issue:1234",
      "recovery": {
        "approvalState": "approved",
        "allowedMode": "restore-bundle",
        "authorizationRef": "pr:1235",
        "authorizedAt": "2026-03-02T00:00:00Z"
      },
      "updatedAt": "2026-03-01T00:00:00Z"
    }
  }
}
```

Durability contract for `artifactIdentity`:

- `artifactLocator` must resolve to one immutable artifact bundle in a durable artifact store; replacement-in-place is forbidden.
- `attestationRef` must resolve to the attestation or provenance record for that same immutable artifact bundle.
- For a new release or an approved `pre-provenance` recovery, `create-live-lock` must succeed before `require-provenance` performs the durable write, because that durable write is an irreversible external mutation.
- `require-provenance` is the only job allowed to write those durable records. For both a new release and an approved `pre-provenance` recovery, that durable store write must use create-if-absent / write-once semantics keyed by `planDigest` (or an equivalent same-plan uniqueness key) so the same frozen plan cannot produce multiple authoritative artifact identities.
- The `artifactLocator`, `attestationRef`, and `subjects` emitted by that successful durable write become the authoritative artifact identity for the frozen release plan. Any later blocked-state entry or success record must carry those exact values rather than reconstructing them after the fact.
- The durable store must retain blocked-release bundles and attestations until the blocked entry is cleared by reviewed evidence, and never for less than one year from the blocked entry's `updatedAt`.
- Recovery jobs must have documented read access to that store, while delete permission must be administratively narrower than routine workflow-dispatch permission.
- A locator or attestation reference that depends only on default GitHub Actions artifact retention is insufficient for this contract unless the repository separately establishes and documents equivalent durability, immutability, and recovery-read guarantees.

A blocked entry must include a machine-readable reason and a reviewed evidence reference. The schema is a discriminated union so it can represent both the window after the live lock exists but before authoritative provenance is written, and the later window after durable artifact identity already exists. Example reasons include:

- `open-before-provenance`
- `open-partial-publish`
- `control-plane-discrepancy`

Validation rules:

- The file must exist at the documented path in the frozen official release snapshot.
- `schemaVersion` must equal `1`.
- `projects` must be an object keyed by canonical `project-key`.
- The selected project entry must exist; missing-entry is a hard failure.
- `status` must be exactly `ready` or `blocked`.
- A `blocked` entry must include `blockedStage`, `frozenPlan`, `reason`, `evidenceRef`, and `recovery`.
- `blockedStage` must be exactly `pre-provenance` or `post-provenance`.
- `frozenPlan` must include `planDigest`, `projectKey`, `projectPath`, `packageIdentity`, `packageManifestPath`, `ecosystem`, `buildKind`, `version`, `authorizedBranch`, `payloadSha`, `officialTag`, `targets`, `targetArtifacts`, and `targetAuthContracts`.
- When `blockedStage = pre-provenance`, `artifactIdentity` must be absent because the authoritative durable artifact identity does not exist yet.
- When `blockedStage = post-provenance`, `artifactIdentity` must include a durable `artifactLocator`, durable `attestationRef`, and the exact digest subject map keyed by artifact alias.
- `recovery.approvalState` must be exactly `not-approved` or `approved`.
- When `recovery.approvalState = approved` and `blockedStage = pre-provenance`, `recovery.allowedMode` must be exactly `rerun-plan`, and `recovery.authorizationRef` must identify the reviewed approval that authorized that mode.
- When `recovery.approvalState = approved` and `blockedStage = post-provenance`, `recovery.allowedMode` must be exactly `restore-bundle`, and `recovery.authorizationRef` must identify the reviewed approval that authorized that mode.
- When `recovery.approvalState = not-approved`, the blocked entry is not yet eligible for an official recovery run.
- Unknown top-level or per-project fields are hard failures until explicitly added to the schema.

`preflight-validate` reads only the selected project's current checked-in entry from `policy-sha` and performs one bounded live-lock check for `refs/tags/official-lock/<project-key>`. It does not scan old workflow runs to reconstruct admission state.

### 6.4 Update model

- Normal admission-state, blocked-state evidence, and recovery-authorization updates happen only through reviewed PRs to the same protected official release branch that is authoritative for the selected frozen plan.
- For every new official release and every approved `pre-provenance` recovery, `require-provenance` must persist the immutable artifact bundle, digest manifest, and attestation to the durable store after `create-live-lock` succeeds but before the official release tag or any external publish starts. Those emitted locator/reference values become the authoritative artifact identity for that frozen plan.
- The live lock tag must be created before the first irreversible external mutation of an official run, including any durable artifact-store write, must target the frozen `release-plan.payloadSha`, and its annotation payload must carry the frozen `planDigest`, `packageIdentity`, `version`, and `officialTag`.
- If an official run becomes partial, failed, or uncertain after the live lock exists, the checked-in blocked state must record whether the run is `pre-provenance` or `post-provenance` so the failure window is representable without inventing a nonexistent `artifactIdentity`.
- If an official run becomes partial, failed, or uncertain after any external mutation, the live lock remains blocking until checked-in state on the authoritative protected branch records the disposition and evidence for that same frozen release plan and, when available, the authoritative artifact identity.
- On a fully successful official release, `release-complete` clears the live lock tag in the same run after publish confirmation succeeds, but the annotated official release tag remains the durable success record for that frozen plan and its authoritative artifact identity.
- A blocked entry persists the exact frozen release plan, the `blockedStage` discriminator, and when available the original artifact identity so that a reviewed recovery change on the authoritative protected branch may authorize a new official recovery run without re-deriving the old release identity from the newer `policy-sha`.
- A reviewed recovery change must express approval machine-readably through the `recovery` object, including whether `rerun-plan` or `restore-bundle` recovery is approved for that blocked stage.
- Recovery that would derive a different release identity from a newer branch snapshot is intentionally out of scope; only the already-frozen plan may be resumed.
- A successful recovery run may clear the live lock in-run after publish confirmation succeeds, but the checked-in blocked entry remains authoritative until a reviewed PR on the authoritative protected branch transitions the project back to `ready`.
- Emergency updates may use the documented break-glass process, but the checked-in state must still become authoritative again afterward.
- The file must stay small, current, and project-scoped.

## 7. Release Serialization and Recovery Contract

### 7.1 Frozen policy, plan, and payload identity

- `preflight-validate` must emit immutable `policy-sha` plus one canonical frozen `release-plan`.
- For a normal official release, `policy-sha` is also the frozen payload snapshot. For a reviewed recovery, `release-plan.payloadSha` remains the frozen blocked payload snapshot carried by checked-in state.
- The authoritative official release identity is the frozen `release-plan`, not `policy-sha` alone.
- `release-plan` must include `planDigest`, `projectKey`, `projectPath`, `packageIdentity`, `packageManifestPath`, `ecosystem`, `buildKind`, `version`, `authorizedBranch`, `payloadSha`, `officialTag`, `targets`, `targetArtifacts`, and `targetAuthContracts`.
- `planDigest` is the `sha256` of the RFC 8785 / JCS canonical JSON serialization of `projectKey`, `projectPath`, `packageIdentity`, `packageManifestPath`, `ecosystem`, `buildKind`, `version`, `authorizedBranch`, `payloadSha`, `officialTag`, `targets`, `targetArtifacts`, and `targetAuthContracts`.
- Before serialization, `targets` must be lexicographically sorted and every `targetArtifacts.<target>` array must be lexicographically sorted by artifact alias.
- For a new official release, `release-plan.payloadSha` equals the frozen snapshot of the protected branch selected in `workflow_dispatch`.
- For a reviewed official recovery run, `release-plan.payloadSha` may differ from `policy-sha` only when the checked-in blocked state and the live lock explicitly carry forward that same frozen plan.
- `policy-sha` authorizes admission, workflow/control-plane behavior, branch-policy checks, and reviewed recovery evidence. It must never rewrite release-plan identity fields during recovery.
- The annotated official release tag is the durable success record for a completed frozen release plan; it must carry the canonical success identity consisting of the frozen release-plan identity plus `artifactLocator`, `attestationRef`, and exact subject digests. Clearing the live lock does not discard that success identity.
- After `preflight-validate`, downstream checkout, build, test, provenance, tag, and publish steps must consume only the frozen `release-plan` plus any persisted blocked artifact identity. The selected branch names remain audit inputs only after the frozen values are derived.

### 7.2 Concurrency model

- Buddy and official runs for the same `project-key` must share one per-project concurrency group, such as `release/<project-key>`, so the two channels cannot execute concurrently for that project.
- `cancel-in-progress: false` is used only to avoid overlapping execution and to avoid evicting an in-flight run; it must not be treated as a durable FIFO queue or admission ledger.
- Durable ordering, recovery, and unblock decisions come from the checked-in admission state plus the live lock, not from GitHub Actions pending-run behavior.
- Distinct projects may release in parallel.

### 7.3 Idempotency, rerun, and recovery

- `create-release-tag` is idempotent only when the existing annotated official tag already points to the same frozen `release-plan.payloadSha`, the tag annotation carries the same frozen `planDigest`, canonical frozen `release-plan` payload, `artifactLocator`, `attestationRef`, and exact subject digests, and the protected live lock, if present, carries the same frozen `planDigest`; otherwise it is a hard conflict.
- A publish target may be treated as a same-identity no-op only when live remote state proves that the already-present version or release artifact corresponds to the current frozen release plan and the authoritative artifact identity for that plan. For a new release and a `pre-provenance` recovery, that artifact identity is produced and durably persisted in the current run before any publish mutation; for a `post-provenance` recovery, it is the persisted `artifactIdentity` carried in checked-in state. If a target cannot prove same-identity from live remote state, the workflow must fail closed rather than silently accepting a version-only match.
- For `github:release`, same-identity proof requires the release to be attached to the expected tag and the full remote asset set to match the authoritative artifact identity exactly by name and digest. Missing, extra, or digest-mismatched assets are conflicts.
- If a run fails before any external mutation occurs, rerunning the same release identity is allowed and admission state remains unchanged.
- If any official external mutation succeeds but the overall release result is partial, failed, or uncertain, the project must remain blocked in both the live lock and `.github/official-admission-state.json` until a reviewed recovery change on the authoritative protected branch records the disposition and evidence for that same frozen release plan and, when available, its artifact identity.
- A blocked recovery uses exactly the machine-readable mode recorded for that blocked stage. `pre-provenance` recovery reruns the already-frozen plan from `release-plan.payloadSha` to create the first authoritative artifact identity for that plan. `post-provenance` recovery reuses the previously recorded immutable artifact bundle referenced by `artifactLocator`. Recovery never derives a replacement release identity from a newer branch snapshot.
- A successful recovery run may clear the live lock after `confirm-publish-state` succeeds, but the project is not release-ready again until reviewed checked-in state on the authoritative protected branch transitions the project entry back to `ready`.
- Clearing a blocked project requires reviewed evidence that every affected target, the authoritative artifact identity, the official tag, and the live lock are now in the intended terminal state, plus a checked-in transition back to `ready`.

## 8. Shared Workflow Rules

- Reusable workflows must not declare their own `permissions:` blocks.
- Build/test and attestation reusable workflows must be called with `secrets: {}`.
- Official publish jobs are direct jobs, not reusable-workflow hops.
- Build/test/package reusable workflow runner selection is fixed by `buildKind`: `csharp-pack` on `windows-latest`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-latest`.
- Shell steps must treat workflow inputs and derived values as untrusted: map through `env:` first, then reference quoted variables.
- Raw `${{ ... }}` interpolation inside shell scripts is forbidden. Workflow expressions must be mapped into environment variables or explicit action inputs before shell execution.
- `eval`, untrusted `bash -c`, and sourcing any shell content that comes from payload-controlled files, workflow inputs, or other untrusted data are forbidden.
- Writes to `GITHUB_OUTPUT` and `GITHUB_ENV` must use the documented file-append form with trusted keys, trusted here-doc delimiters, and delimiter values that cannot be influenced by untrusted content.
- Here-doc bodies in shell steps must use quoted delimiters when expansion is not required, and delimiter tokens must be chosen so payload-controlled content cannot terminate them early.
- Third-party actions must be pinned to full commit SHA.
- All jobs that rely on NBGV or other git-history-derived metadata must use full history.
- Permission grants default to `permissions: {}` at workflow level, with job-level least-privilege escalation.
- `id-token: write` must appear only on the publish job that actually needs GitHub OIDC for trusted publishing or provenance; it must not be granted at workflow scope or to build/test jobs.
- When a release workflow freezes distinct control-plane and payload SHAs, it must check them out into distinct fixed paths such as `control-root/` and `payload-root/`.
- Local composite actions, helper scripts, and other workflow-owned control-plane code must execute only from the control checkout.
- Project build/test/package commands may read payload files only from the payload checkout, and any file that influences project resolution, version resolution, dependency resolution, build, package, or artifact selection belongs to the payload checkout even when it lives at repository root. The payload checkout must not shadow local action or helper-script paths.
- Jobs must not re-resolve the selected protected dispatch branch into a new HEAD after `preflight-validate`; they must consume the emitted frozen values only.

## 9. Summary of Key Design Properties

- The only externally exposed workflows are `ci.yml`, `buddy.yml`, and `official.yml`.
- `.github/repository-release-contract.json` is the checked-in machine-readable source of truth for repository-side release prerequisites, PR trust-model rules, and target-auth contracts, and `ci.yml` validates PR drift against it.
- The release configuration contract includes `github:release` and does not include `pypi:testpypi`.
- Python buddy preview uses `github:release`.
- Buddy publish authorization stays in direct jobs rooted in the dispatch-selected snapshot plus dedicated buddy publication environments; it does not rely on same-repository reusable publish workflows or ambient credentials outside the documented target auth contract.
- Buddy GitHub Releases use a dedicated `buddy/<project-key>/v<version>/<dispatchSha>` tag namespace separate from official tags.
- For a normal official release, the protected branch selected in `workflow_dispatch` is the single trust root for workflow code, checked-in policy inputs, and payload, while the published release identity is the frozen release plan plus its authoritative artifact identity.
- Official canonicalization, existence, uniqueness, and compatibility checks happen before any environment with secrets is entered.
- Official static analysis happens before any environment entry.
- PR validation uses `pull_request` for untrusted code paths, treats fork PRs as untrusted, and never mints secrets or privileged publish/ref-write credentials for PR workflows.
- `production-<project-key>` is the authoritative human approval gate.
- `production-<project-key>` must be pre-created and protected before use, with required reviewers and prevent-self-review enabled.
- Target-specific official environments are subordinate credential scopes, not replacement approval gates.
- Protected selected-branch workflow code, protected official tags, and the protected live-lock tag are all required repository prerequisites.
- Protected official ref writes use a dedicated GitHub App installation token plus tag-targeted rulesets; environment approval is not itself a protected-ref bypass.
- Official GitHub-native publication credentials are issued explicitly inside protected environments rather than relying on ambient branch-local `GITHUB_TOKEN` write authority, and the GitHub Release publisher actor is distinct from the protected ref-writer actor.
- Official static analysis covers project files plus the official control-plane surface.
- Official build/test/package routing is determined by resolved ecosystem plus checked-in `buildKind`, explicit `packageIdentity`, explicit `packageManifestPath`, `artifacts`, and `targetArtifacts`.
- Recovery resumes only the already-frozen blocked release plan rather than recomputing release identity from a newer branch snapshot. `pre-provenance` recovery may rebuild that frozen plan to create the first authoritative artifact identity, while `post-provenance` recovery reuses the persisted immutable artifact bundle; both modes depend on an explicitly defined immutable artifact-store contract whose normal-path write is protected by a live lock created before that external mutation.
- Official downstream jobs use physically separate control and payload checkouts, consume the frozen release plan and attested immutable artifacts, and do not re-resolve branch heads or rebuild during publish.
- Admission is driven by bounded checked-in state on the authoritative protected official release branch plus one bounded live lock per project, not unbounded historical run scans or scheduled snapshot freshness.
- Same-project release runs use one shared per-project concurrency group only to avoid concurrent execution; durable ordering and recovery come from checked-in state plus the live lock.
- Official release tag and publish operations have explicit idempotency and recovery rules keyed by the frozen annotated-tag identity plus `planDigest`, including machine-readable recovery approval.
- Successful official releases keep a durable canonical success identity on the annotated official release tag even after the live lock is cleared.
- Implementation must wait until the repository completes the per-project root migration described in `AGENTS.md`.
