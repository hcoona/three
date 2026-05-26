# GitHub Workflows Design (v2.50)

This document describes the target GitHub Actions release design for the `three` monorepo.

> **Current repository layout:** Active projects now use the canonical monorepo roots under `src/`, `src/lab/`, and `tests/`; the former `OneDotNet/` subtree has been migrated into those canonical roots. Release pipelines are still not set up.

> **Scope constraint:** Each releaseable project maps to exactly one language ecosystem and exactly one checked-in `buildKind`. Multi-language or multi-build-kind projects are out of scope.

> **Release-unit constraint:** Each `buddy.yml` or `official.yml` run releases exactly one project.

> **Identity split:** The workflow input is `project-key`, which must be the canonical internal `project-key` exactly as recorded in `.github/repository-release-contract.json`. `packageIdentity` is the external package identifier and must never be derived by normalizing `project-key`.

The repository must carry one checked-in machine-readable repository release contract, `.github/repository-release-contract.json`, as the single source of truth for repository-side release prerequisites, PR trust-model rules, environment contracts, durable artifact-store contracts, and target-auth contracts. The repository must also carry one checked-in machine-readable external control-plane integrity manifest, `.github/external-control-plane-commitments.json`, that pins the cryptographic commitments for the external credential broker and the external release monitor required by this design.

Before any release-workflow implementation begins, the repository must first land the Day 0 reviewed helper/tooling set reserved by this design: `eng/scripts/compute-bootstrap-hash`, `eng/scripts/jcs-canonicalize`, `eng/tests/jcs-fixtures/`, `eng/scripts/create-blocked-entry`, `eng/scripts/release-status`, and `eng/scripts/compute-build-time-p95`. The required bootstrap order is: `eng/scripts/jcs-canonicalize` plus `eng/tests/jcs-fixtures/` first, then `eng/scripts/compute-bootstrap-hash`, then `eng/scripts/create-blocked-entry`, then `eng/scripts/release-status`, then `eng/scripts/compute-build-time-p95`, and only then workflow integration. There is no pre-tool temporary or ad hoc authoritative path.

Because implementation has not started yet, this document keeps only the current target design. Superseded alternatives are removed instead of being preserved as compatibility paths.

## 1. Architecture Overview

The externally exposed release and release-authority validation entry workflows are:

- `ci.yml`
- `buddy.yml`
- `official.yml`

No additional triggered top-level workflows are release entry points in this design. `.github/workflows/codeql.yml` is allowed as a triggered top-level non-release security analysis workflow only when it has no release authority, cannot call release mutation workers, and does not mint publish credentials or protected-ref bypass credentials. A scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflow such as `renovate.yml` is allowed only when it has no release authority, uses explicit least-privilege job permissions from a workflow-level `permissions: {}` baseline, cannot call release mutation workers, and does not mint publish credentials or protected-ref bypass credentials. It may use a dedicated GitHub App installation token to create dependency branches and pull requests. Automerge is allowed only for the configured Renovate major-update rule after required CI passes; platform automerge remains disabled, and branch protection/rulesets must continue to prevent the dependency-maintenance token from mutating or bypassing protected branches and release refs directly.

The shared execution layer is:

- reusable build/test workflows under `.github/workflows/_build-test-*.yml`
- reusable attestation workflows under `.github/workflows/_attest-build-*.yml`
- the buddy-only internal mutation worker `.github/workflows/_buddy-mutation-worker.yml`
- reviewed local composite actions under `.github/actions/**`
- reviewed helper scripts under `eng/scripts/**`

Security-sensitive publication stays in direct jobs inside `buddy.yml` or `official.yml`. The only reusable publication-adjacent worker in scope is the repository-owned buddy phase wrapper `.github/workflows/_buddy-mutation-worker.yml`, and it is allowed only for concurrency management plus post-audit rebinding; it is not an independent release entry point or authorization boundary, must accept calls only from the documented top-level workflows, and must never expose `workflow_dispatch`. Because reusable-workflow runtime context identifies the called worker rather than a documented caller-workflow path, caller validation must be designed as repository-owned allowlisted call sites under CODEOWNERS/bootstrap-hash review plus explicit dispatcher-to-worker binding checks rather than as a trust decision on `github.workflow_ref` alone. That call-site allowlist is a reviewed repository-governance constraint, not a standalone runtime proof of caller identity. Reusable publish workflows remain out of scope as an authorization boundary for the buddy path.

## 2. `ci.yml` — Pull Request Validation

Main responsibilities:

1. Run repository static analysis through HK.
2. Detect affected ecosystems and build kinds.
3. Build, test, and package only the affected ecosystem/build-kind suites when required.
4. Validate that workflows and docs do not drift from `.github/repository-release-contract.json`.
5. Finish with one final gate job suitable for branch protection.

Design rules:

- HK is repository-wide, not project-specific.
- Infrastructure and shared control-plane changes must trigger all ecosystem/build-kind suites.
- Ecosystem build/test execution uses static reusable-workflow calls such as `_build-test-csharp.yml`, `_build-test-python.yml`, `_build-test-node-npm.yml`, `_build-test-node-wxt.yml`, and `_build-test-ruby.yml`.
- The reusable runner contract is fixed by `buildKind`: `csharp-pack` uses `windows-2022`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` use `ubuntu-24.04`.
- `ci.yml` must parse the checked-in repository release contract and fail the PR when workflow code or checked-in docs drift from that machine-readable source of truth.
- `ci.yml` must parse `.github/repository-release-contract.json`, every `.github/official-admission-state/<project-key>.json`, and every `release.json` with duplicate-key rejection enabled before any schema validation or digest computation. Because Python's default `json.loads()` silently accepts duplicate keys and keeps the last value, the Day 0 helpers and validators must use `object_pairs_hook` or an equivalent parser path that turns duplicate keys into hard failures.
- The `ci.yml` bootstrap-governance surface is `.github/CODEOWNERS`, `.github/workflows/ci.yml`, `.github/workflows/buddy.yml`, `.github/workflows/_buddy-mutation-worker.yml`, `.github/workflows/official.yml`, every checked-in file under `.github/workflows/_build-test-*.yml`, every checked-in file under `.github/workflows/_attest-build-*.yml`, `.github/workflows/docs/DESIGN.v2.md`, `.github/repository-release-contract.json`, `.github/external-control-plane-commitments.json`, every checked-in file under `.github/actions/**`, and every checked-in file under `eng/scripts/**`. That surface must be protected by dedicated CODEOWNERS review from the repository’s release-governance owners, and repository protection/rulesets must require code-owner review for that surface.
- `ci.yml` must recompute a checked-in `prTrustModel.bootstrapTrustedFilesSha256` integrity hash over the canonical bootstrap manifest `(path, sha256)` list for that bootstrap-governance surface and fail closed if the recomputed value differs. `ci.yml` drift validation is therefore not trusted on bootstrap changes unless both the integrity hash and the dedicated CODEOWNERS review path also match the checked-in trust model.
- `ci.yml` must use event-specific concurrency groups with `cancel-in-progress: true`: PR-triggered runs use `ci/pr/<pull-request-number>`, while push-triggered runs use `ci/push/<full-ref>`. Bare `ci/<head-ref>` is forbidden because different forks can reuse the same branch name and would otherwise collide, and bare `ci/` is forbidden because push-triggered runs must not all share one slot. There is no grandfathered exception for any pre-existing branch-name-based `ci.yml` concurrency group.
- `ci.yml` drift validation must parse every checked-in workflow `on:` block and fail when a `pull_request_target` workflow checks out, executes, or otherwise sources PR-head code or PR-head refs. Official release enablement is forbidden until every repository `pull_request_target` workflow satisfies the metadata-only rule from this section; there is no grandfathered exception list in this design.
- `ci.yml` drift validation must parse every workflow file and fail when any non-local third-party action reference outside the GitHub-maintained `actions/` organization is not pinned to a full 40-character commit SHA. First-party `actions/*` references may use reviewed version tags and do not require SHA pinning.
- `ci.yml` drift validation must parse every checked-in workflow file and fail when the workflow-level top-level `permissions:` mapping is missing or is not exactly `permissions: {}`. Job-level least-privilege escalation is the only approved permission-expansion path in this design, and no workflow is exempt from the explicit top-level declaration requirement.
- `ci.yml` drift validation must parse every checked-in `officialTargetConfirmationPolicies` entry and fail the PR when any `confirmTimeoutSeconds` value violates the §4.4 / §5.12 retry-budget inequality; release-time validation is the second line of defense, not the first time the formula is checked.
- `ci.yml` drift validation must, for every release-enabled project, validate the cross-file target-key contract on the same frozen branch snapshot: buddy targets present in `<project-root>/release.json.targets` must exactly match `buddyEnvironments` and `buddyTargetAuthContracts`, while official targets present in `<project-root>/release.json.targets` must exactly match `officialEnvironments.targets`, `officialTargetAuthContracts`, and `officialTargetConfirmationPolicies`. Channel filtering is allowed only after this cross-file key-set validation succeeds.
- `ci.yml` drift validation must fail the PR when any project's `approvalWaitMaxSeconds` is less than `baselineWaitTimerMinutes * 60 + 300`; an official run must not be cancellable for approval timeout before the checked-in baseline wait timer has elapsed and at least one full external-monitor poll interval of approval action time remains.
- `ci.yml` drift validation must validate every non-null `providerConfigReviewedAt` against the validating workflow runner's current UTC clock, fail future-dated values, and fail any record older than 365 days. Targets that also fall under the stricter `workflow-only` / `providerSupportsReadOnlyInspection = false` freshness rules in §4.9 and §5.11 still use those tighter bounds.
- `ci.yml` drift validation must fail if `.github/external-control-plane-commitments.json` is missing, violates its closed schema, fails signature verification against the pinned verifier set, or omits either the external credential broker or the external release monitor commitment required by this design.
- `ci.yml` drift validation must fail if any repository-owned internal reusable workflow other than the documented build/test or attestation helpers declares `workflow_dispatch`. `.github/workflows/_buddy-mutation-worker.yml` is not exempt: it must declare only `workflow_call`, and drift validation must fail if it declares any other trigger. `ci.yml` must also fail if any top-level workflow other than `buddy.yml` or `official.yml` is introduced as a release entry point; scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflows are permitted only when they remain outside release authority and satisfy the no-release-authority constraints in §1.
- `ci.yml` drift validation must also parse every checked-in workflow file and fail unless every `uses:` reference to `.github/workflows/_buddy-mutation-worker.yml` originates from `buddy.yml` only. No other workflow may call that worker through a relative path, an equivalent same-repository reusable-workflow reference, or an alternate wrapper path.
- `ci.yml` drift validation must fail if any workflow other than `official.yml` or `.github/workflows/_buddy-mutation-worker.yml` declares a workflow-level or job-level concurrency group whose literal prefix is `release/`; that prefix is reserved for the shared buddy/official mutation slot only.
- `ci.yml` drift validation must fail if any workflow other than `official.yml`, `buddy.yml`, or `.github/workflows/_buddy-mutation-worker.yml` references a reserved release environment name from `.github/repository-release-contract.json`, including `production-<project-key>`, any `production-*-<project-key>` subordinate environment, and any `buddy-*-<project-key>` publish environment.
- `ci.yml` drift validation must parse every `actions/checkout` invocation in `buddy.yml`, `official.yml`, `.github/workflows/_build-test-*.yml`, `.github/workflows/_attest-build-*.yml`, and every checked-in composite action under `.github/actions/**`, and fail when any checkout omits `persist-credentials: false`.
- `ci.yml` drift validation must fail when `id-token: write` appears anywhere other than the exact publish job that needs GitHub OIDC for trusted publishing or provenance.
- `ci.yml` drift validation must fail when build/test or attestation reusable-workflow calls omit the explicit `secrets: {}` mapping.
- The shell-safety rules from §8 apply equally to `ci.yml`. Even apparently low-risk values such as `${{ github.sha }}`, `${{ github.ref }}`, or `${{ github.event.pull_request.number }}` must be mapped through `env:` or explicit action inputs before shell execution rather than interpolated directly inside shell source.
- PR validation for untrusted code must use `pull_request`, not `pull_request_target`.
- If `pull_request_target` is ever used for repository-maintenance work, it must be metadata-only: it must not check out, execute, or source PR-head code and must not mint publish, protected-ref-write, or other privileged release credentials.
- Fork PRs are always untrusted input. PR workflows, including fork PR workflows, must never receive repository secrets, environment-scoped publication credentials, or protected-ref-write credentials.
- Repository settings that would grant fork PRs secrets or privileged write tokens are out of scope for this design and must remain disabled as an explicit repository prerequisite.
- PR workflows must never publish artifacts to external registries, create releases, or mutate protected refs.
- The final gate job must succeed when the required build/test/package work succeeded, even if some ecosystem jobs were intentionally skipped.

### 2.1 Bootstrap integrity hash computation and maintenance

- Unless this document explicitly says otherwise, every SHA-256 value stored in checked-in JSON, tag payloads, or reviewed manifests uses the canonical text form `sha256:<64 lowercase hex>`.
- The canonical bootstrap manifest is the UTF-8 RFC 8785 / JCS serialization of one JSON array of objects, where each object has exactly `path` and `sha256` keys.
- Each `path` is the exact repository-relative slash-separated path from the repository root.
- For every bootstrap-governance file except `.github/repository-release-contract.json`, the manifest `sha256` is the canonical `sha256:<64 lowercase hex>` digest of that file’s UTF-8 text bytes after line-ending normalization to LF. The helper must reject non-UTF-8 bootstrap files rather than hashing platform-specific raw bytes.
- For `.github/repository-release-contract.json`, the manifest `sha256` is computed from the same LF-normalized UTF-8 text **after** replacing the JSON string value of `prTrustModel.bootstrapTrustedFilesSha256` with the literal placeholder `sha256:0000000000000000000000000000000000000000000000000000000000000000`, every `providerConfigReviewedAt` value with the literal placeholder `1970-01-01T00:00:00Z`, and every non-null `providerConfigReviewRef` object with a placeholder object that preserves the original closed-schema `kind` value while replacing only `locator` with `artifact://provider-review/placeholder` and `evidenceSha256` with `sha256:0000000000000000000000000000000000000000000000000000000000000000`. `null` `providerConfigReviewRef` values remain `null`. The helper must hard-fail if `prTrustModel.bootstrapTrustedFilesSha256` is missing, duplicated, not a string, or appears anywhere other than `prTrustModel.bootstrapTrustedFilesSha256`, and it must also hard-fail when any non-null `providerConfigReviewRef` is duplicated or violates the closed schema before normalization. This placeholder normalization is the authoritative bootstrap-hash contract and replaces any impossible self-hash/fixed-point interpretation; preserving `providerConfigReviewRef.kind` is part of that contract because different evidence-capture assurance levels are semantically distinct even when the evidence bytes themselves are normalized out of bootstrap hashing.
- The manifest entries are sorted lexicographically by `path`. Duplicate paths are forbidden.
- The bootstrap-governance surface is exact, not heuristic: `.github/CODEOWNERS`, `.github/workflows/ci.yml`, `.github/workflows/buddy.yml`, `.github/workflows/_buddy-mutation-worker.yml`, `.github/workflows/official.yml`, every checked-in file under `.github/workflows/_build-test-*.yml`, every checked-in file under `.github/workflows/_attest-build-*.yml`, `.github/workflows/docs/DESIGN.v2.md`, `.github/repository-release-contract.json`, `.github/external-control-plane-commitments.json`, every checked-in file under `.github/actions/**`, and every checked-in file under `eng/scripts/**`. Membership in that surface does not depend on whether `ci.yml` currently invokes a given helper.
- `.github/CODEOWNERS` itself is a mandatory bootstrap prerequisite. Official release enablement is forbidden until that file exists, covers the bootstrap-governance surface, and repository protection/rulesets require code-owner review for that surface.
- `ci.yml` must parse `.github/CODEOWNERS` and fail closed if any bootstrap-governance path lacks the dedicated release-governance owner coverage required by this design. The bootstrap hash is therefore not self-authenticating; CODEOWNERS coverage drift is part of the same bootstrap validation surface.
- Repository protection/ruleset enforcement remains a separate repository prerequisite. Official release enablement is forbidden until repository protection/rulesets require code-owner review for the bootstrap-governance surface, and §4.1 treats that prerequisite as an explicit enablement checklist item rather than an operator memory task.
- Any identity that can bypass that CODEOWNERS or protection/ruleset enforcement surface, including a repository administrator using an allowed bypass path, is inside the bootstrap trust root by definition. Such a bypass is treated as compromise of bootstrap governance rather than as an ordinary reviewed change.
- The design requires one reviewed helper command, `eng/scripts/compute-bootstrap-hash`, that recomputes the manifest and final `prTrustModel.bootstrapTrustedFilesSha256` value from repository contents using the placeholder-normalization rule above for `.github/repository-release-contract.json`. It is a Day 0 implementation prerequisite for any release-workflow integration. Because Python's default `json.loads()` accepts duplicate keys, this helper must use duplicate-key-rejecting parsing such as `object_pairs_hook` rather than the default parser path. Until implementation exists, this command name is reserved by the design and no alternate ad hoc computation path is authoritative.
- `eng/scripts/compute-bootstrap-hash` is a required cross-platform helper with this minimum interface contract:
  - implementation baseline: Python `3.12+` with only checked-in repository code and explicitly reviewed dependencies; Linux and Windows CI entrypoints may be thin wrappers, but the authoritative logic is repository-owned and deterministic
  - invocation contract: `eng/scripts/compute-bootstrap-hash --repo-root <path> [--format json|hash] [--manifest-out <path>]`
  - default stdout contract (`--format json`): one JSON object with exactly `bootstrapTrustedFilesSha256` and `manifest`, where `manifest` is the exact canonical `(path, sha256)` list sorted as required by this section and already reflects the placeholder-normalized digest rule for `.github/repository-release-contract.json`
  - `--format hash` stdout contract: only the canonical `sha256:<64 lowercase hex>` value followed by `\n`
  - exit codes: `0` success; `2` invalid invocation; `3` bootstrap-surface resolution failure (including duplicate paths); `4` file-read or digest-computation failure
  - minimum tests: golden fixtures under `eng/tests/jcs-fixtures/` for path ordering, explicit LF normalization from both CRLF and LF inputs, duplicate-path rejection, placeholder normalization for `.github/repository-release-contract.json`, and a fixture proving that the emitted manifest and checked-in placeholder-normalized contract bytes recompute back to the same `bootstrapTrustedFilesSha256`
  - the placeholder-normalization fixture set must explicitly cover the exact literal `sha256:0000000000000000000000000000000000000000000000000000000000000000`, the exact timestamp placeholder `1970-01-01T00:00:00Z`, one non-null `providerConfigReviewRef` case for every supported `kind`, and a `null` `providerConfigReviewRef` case so cross-language implementations cannot silently drift on omitted prefixes, null handling, or placeholder object shape
- Any PR that adds, removes, renames, or changes a file in that bootstrap-governance surface must update the surface itself and `prTrustModel.bootstrapTrustedFilesSha256` in the same PR. There is no deferred or compatibility-preserving update path.
- A PR that changes only `providerConfigReviewedAt` and/or `providerConfigReviewRef` for unchanged target-auth bindings does not require a bootstrap-hash update because those operational freshness fields are placeholder-normalized out of the bootstrap manifest digest. It still requires ordinary reviewed contract changes on `.github/repository-release-contract.json`, must not be used to hide changes to workflow path, environment, actor, audience, or allowed-ref bindings, and must keep the old and new evidence locators/digests reviewable side by side in the PR description or linked review record. This is a deliberate residual-risk tradeoff: bootstrap integrity no longer authenticates the freshness-evidence bytes or locator/digest details inside `providerConfigReviewRef`, so drift validation is mandatory. `ci.yml`, `preflight-validate`, and the external provider-freshness monitor from §7.6 must treat those fields as operational evidence by verifying that every referenced locator remains reachable when the relevant surface is available and that the fetched bytes still hash to the recorded `evidenceSha256`, and that the evidence still asserts the same normalized trust tuple (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerAudience`, `providerRefClaimMode`, `providerTrustCapabilities`, and `allowedRefClaims`) rather than drifting to a weaker or differently-scoped conclusion. An unreachable, mismatched, or semantically divergent evidence record is configuration-invalid rather than a warning.
- The Day 0 helper exit-code tables in this document are tool-local contracts, not a repository-wide shared numeric taxonomy. Implementations and runbooks must not infer cross-tool meaning from the same numeric exit code unless this document explicitly says so.
- `ci.yml` must fail closed with both the checked-in hash and the recomputed hash, and it must print the canonical manifest diff or equivalent per-path digest mismatch list so reviewers can see exactly which bootstrap file changed.
- The dedicated CODEOWNERS protection for the bootstrap-governance surface is required even when the hash matches; the hash proves exact content identity, while CODEOWNERS review proves reviewed authority to change that surface.

## 3. `buddy.yml` — Unofficial Release

`buddy.yml` is the manual workflow for unofficial releases. It is independent of `official.yml`. The `workflow_dispatch` interface exposes only `project-key`; the branch selected in the UI supplies the single frozen buddy snapshot for that run. After preflight freezes that selected branch to an immutable commit, every later buddy step must use that same snapshot for both workflow/control-plane files and payload files.

Buddy is unofficial, but it is **not** an arbitrary-branch publication path. Each buddy-enabled project must declare exact buddy-authorized branch refs in `.github/repository-release-contract.json`, and the corresponding buddy environments must enforce the same allowlist through deployment-branch restrictions. Wildcard catch-alls such as `refs/heads/*` are forbidden.

Because GitHub approval/review history is documented per `run_id` rather than per `run_attempt`, buddy publication is single-attempt only. `buddy.yml` must hard-fail when `github.run_attempt != 1`, and any retry after approval, partial publish, or stale reviewer state requires a fresh manual dispatch rather than a GitHub rerun attempt.

### 3.1 Responsibilities

1. Resolve exactly one project from repository state.
2. Reject buddy publication unless the selected branch is one of the checked-in buddy-authorized refs for that project.
3. Refuse to run when the same project is blocked by official admission state or a live official lock.
4. Run bounded static analysis for that project plus buddy control-plane files.
5. Build, test, and package exactly one ecosystem/build-kind path when that path requires packaging.
6. Publish only unofficial targets.

### 3.2 Job outline

1. **`resolve-context`**
   - validates workflow input `project-key` against the repository-safe lowercase pattern and rejects any non-exact canonical key
   - requires the selected `workflow_dispatch` ref to be a branch ref, not a tag ref
   - freezes immutable buddy `dispatchSha` from the `workflow_dispatch` event snapshot of the selected branch
   - resolves `project-path` from `.github/repository-release-contract.json` at `dispatchSha`, then resolves `packageIdentity`, `packageManifestPath`, `ecosystem`, `buildKind`, and version from that same frozen snapshot
   - strictly validates `<project-root>/release.json` from `dispatchSha`
   - validates project existence, uniqueness, ecosystem/build-kind shape, and target compatibility before any channel filtering
   - filters to the unofficial target set and fails if the filtered unofficial set is empty
   - verifies that the selected branch exactly matches one of the checked-in buddy-authorized refs for that project
   - verifies through the GitHub API that every declared buddy-authorized ref is a protected branch and that the selected branch currently retains at least the documented minimum protection contract; inability to confirm protection is a hard failure
   - computes the authoritative official branch for the resolved project version and performs read-only checks of both `.github/official-admission-state/<project-key>.json` on that branch and `refs/tags/official-lock/<project-key>`
   - if that authoritative official branch does not yet exist, buddy treats the missing branch as `ready` with no checked-in blocked entry for that release line, but a live official lock or already-existing official tag for the same version is still a hard failure
   - fails immediately when the official admission entry is `blocked`, when a live official lock exists, or when an orphaned official lock exists without a matching blocked entry; the failure output must include the blocked reason or lock payload plus the next documented operator steps
   - performs bounded GitHub-side non-mutating checks that every required buddy environment already exists and matches the minimum buddy protection contract before any environment entry

2. **`static-analysis`**
   - runs after `resolve-context`
   - uses frozen `dispatchSha` for both workflow/control-plane files and every file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection
   - runs `hk check` over the resolved project path from `dispatchSha`, any shared/root build inputs from `dispatchSha`, plus the shared buddy control-plane surface from that same snapshot:
      - from `dispatchSha`: `.github/workflows/buddy.yml`, `.github/workflows/_build-test-*.yml`, `.github/actions/**`, `eng/scripts/**`, `hk.pkl`, and other pure control-plane rule code
      - from `dispatchSha`: the resolved project path plus any shared/root files actually consumed by the resolved ecosystem/build-kind path, including files such as `mise.toml`, `mise.lock`, root lockfiles, or root config when they affect the buddy build inputs
   - must not silently collapse to project-only analysis when shared buddy release files are in scope

3. **One static conditional build job**
   - exactly one of `build-csharp`, `build-python`, `build-node-npm`, `build-node-wxt`, or `build-ruby` runs
   - each uses the matching reusable build/test workflow selected by `(ecosystem, buildKind)`
   - the runner contract is fixed by `buildKind`: `csharp-pack` on `windows-2022`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-24.04`
   - the selected path must run compile/build work, unit tests, and package/pack validation whenever that build kind requires packaging
   - workflow-owned local actions, helper scripts, and project build inputs all come from the same frozen selected-branch snapshot captured as `dispatchSha`
   - build artifacts are produced fresh inside the workflow run together with a digest manifest for every publishable file
   - the build job must emit the canonical digest-manifest SHA-256 as a job output in addition to uploading the manifest as an artifact
   - direct publish jobs consume only those current-run artifacts and digest-manifest entries; they do not rebuild
   - before any external mutation, every buddy publish job must verify that the downloaded digest manifest exactly matches the build job’s emitted manifest hash; mismatch is a hard failure
4. **`buddy-audit`**
   - runs after the selected build job and before any buddy publication environment entry
   - consumes only frozen outputs from `resolve-context`
   - is the last pre-mutation buddy job and must not itself enter a buddy publication environment, mint publication credentials, or acquire the shared `release/<project-key>` mutation concurrency group
   - must run with explicit job-level least-privilege permissions. Its minimum required set is `contents: read`, `deployments: write`, and `pull-requests: write`; artifact upload itself does not justify broader repository write scope. If an implementation also reads GitHub Actions run metadata beyond the default artifact service path, it may add `actions: read`, but no wider permission grant is allowed for this job
   - constructs one canonical closed `buddy-review-payload` JSON object per buddy target containing exactly `dispatchSha`, `dispatchShaPrefix` (minimum 12 hex), `projectKey`, `packageIdentity`, `version`, `target`, `requiredEnvironment`, `workflowPath`, `runId`, `runAttempt`, `artifactAliases`, and `buddyTag`; extra fields are forbidden
   - applies the RFC 8785 / JCS null-handling rules from §7.1 to `buddy-review-payload` too: required nullable fields stay present as explicit `null`, and no implementation may silently omit `buddyTag` for non-`github:release` targets
   - serializes that payload with the RFC 8785 / JCS rules from §7.1, hashes it to `buddyReviewDigest`, uploads the JSON as an immutable run artifact named `buddy-review-<target>`, creates one GitHub Deployment audit record for that target/environment pair whose `description` is intentionally short (roughly `dispatchShaPrefix + packageIdentity + version + target`, and always kept below 100 characters), stores the full canonical payload as the deployment `payload`, and creates or updates a linked reviewer-facing comment/run-summary block instructing the reviewer to confirm the exact `dispatchShaPrefix`
   - each direct buddy publish job must consume the matching deployment identifier, `buddyReviewDigest`, target name, and artifact locator from `buddy-audit`, recompute the canonical payload from the frozen `dispatchSha` identity before any external mutation, and fail closed unless the recomputed digest, uploaded artifact bytes, deployment payload, and audited target/environment binding all match exactly
   - if payload generation, JCS serialization, digest computation, artifact upload, deployment creation, reviewer-surface rendering, or downstream digest re-binding fails for any target, that target must not enter its environment and the run fails closed

#### Canonical `buddy-review-payload` schema

`buddy-review-payload` is a closed object. It contains exactly these fields and no others:

| Field | Type | Notes |
| --- | --- | --- |
| `dispatchSha` | `string` | 40-character lowercase git commit SHA frozen from the selected branch. |
| `dispatchShaPrefix` | `string` | Lowercase hex prefix derived from `dispatchSha`; minimum length `12`. |
| `projectKey` | `string` | Canonical checked-in project key. |
| `packageIdentity` | `string` | Exact external package identity for the selected project. |
| `version` | `string` | Canonical validated buddy version. |
| `target` | `string` | One enabled buddy target from `{github:release, npm:gpr, nuget:gpr, rubygems:gpr}`. |
| `requiredEnvironment` | `string` | Exact buddy environment name for that target. |
| `workflowPath` | `string` | Exact path `.github/workflows/buddy.yml`. |
| `runId` | `integer` | GitHub Actions run id for this buddy dispatch. |
| `runAttempt` | `integer` | Must be `1`; GitHub rerun attempts are forbidden for buddy publication. |
| `artifactAliases` | `string[]` | Lexicographically sorted exact artifact aliases routed to this target. |
| `buddyTag` | `string \| null` | Exact buddy release tag for `github:release`; otherwise `null`. |

5. **Direct publish jobs**
   - one direct job per supported buddy target
   - no same-repository reusable publish workflow is used as the authorization boundary
   - shared step logic may be implemented through reviewed local composite actions or scripts
   - the publish phase must run in one repository-owned internal mutation worker invocation that acquires workflow-level concurrency `release/<project-key>` only after `buddy-audit` succeeds; that worker is a phase wrapper, not a separate publish authorization model
   - before any buddy publish job enters a buddy environment, mints publication credentials, or performs any other privileged action inside that mutation worker, the worker must re-read the authoritative official admission entry and `refs/tags/official-lock/<project-key>` after it has acquired the shared `release/<project-key>` concurrency slot; discovery of a blocked official admission entry, a live official lock, or an orphan official lock is a hard failure
   - each publish job in that mutation worker must consume the corresponding `buddy-audit` output and enter exactly one pre-created buddy environment declared in the checked-in contract before any external mutation

### 3.3 Buddy equivalent reviewed approval surface

GitHub environment approval remains the credential gate for buddy publish jobs, but GitHub does **not** natively render arbitrary workflow outputs in that approval UI. Buddy therefore requires one equivalent reviewed approval surface per target, and that surface is part of the authorization model:

- `buddy-audit` must create exactly one GitHub Deployment audit record per target/environment pair before the publish job becomes eligible for environment approval.
- The deployment `description` must be a short human-readable summary only, showing at least `dispatchShaPrefix`, `packageIdentity`, `version`, and target name, and it should stay comfortably below any undocumented platform limit by remaining under roughly 100 characters.
- The deployment `payload` must carry the full canonical serialized `buddy-review-payload`, including the full `dispatchSha`, computed buddy tag or `null`, exact artifact aliases, target/environment binding, and the current run tuple. `buddyReviewDigest` is derived from that payload and compared separately; it is **not** a self-embedded field inside the canonical payload object.
- `buddy-audit` must also emit a linked human-readable comment or run-summary block that repeats the same summary and instructs the reviewer to place one exact machine-readable confirmation line in the environment approval comment for that target: `buddy-approve target=<target> dispatch-sha=<dispatchShaPrefix> run-id=<runId> run-attempt=1 digest=<buddyReviewDigestPrefix>`. `buddyReviewDigestPrefix` must be exactly 16 lowercase hex characters derived from the first 16 characters of the full canonical digest. That prefix is a reviewer-usable binding hint, not the authoritative integrity value and not a substitute for collision-resistant comparison of the full digest; runtime verification must always compare the full canonical `buddyReviewDigest`, and the 16-hex prefix length is a deliberate usability-versus-human-transcription tradeoff recorded by this design rather than an implicit security claim. Approval-related summary text must be derived only from already-validated frozen outputs; it must not interpolate unchecked workflow inputs or payload-controlled strings directly into `GITHUB_STEP_SUMMARY`, and every rendered data value must be Markdown-escaped or code-fenced so package names, versions, refs, and similar fields cannot create headings, tables, links, images, checkboxes, or raw HTML.
- `buddy-audit` itself must stay outside every buddy publication environment. It only creates the deployment audit record and reviewer-facing approval surface; it is not allowed to mint publication credentials or request `id-token: write`.
- A buddy publish job becomes environment-eligible only after the matching `buddy-audit` record exists. As the literal first step after environment approval, before any checkout, setup action, local composite action use, external API call, package upload, GitHub App token minting, or OIDC token request, that publish job must re-read the deployment audit record and reviewer confirmation and fail closed unless both are bound to the same frozen `dispatchSha`, `packageIdentity`, `version`, computed buddy tag, target/environment pair, `runId`, and `runAttempt`.
- Environment approval remains the GitHub credential gate, so environment-scoped secrets or OIDC availability may already exist for that job when it starts. The design therefore treats the deployment-payload re-read and reviewer-confirmation check as a mandatory first action before any checkout, tool setup, external API call, package upload, release mutation, or credential minting/use beyond reading the audit record itself. Any later step that mints a publish credential must be conditionally gated on the verification step’s explicit success output (for example `if: steps.verify.outputs.verified == 'true'`).
- A failed post-approval re-check ends the credentialed job immediately. No later job may inherit those credentials, and there is no fallback path that allows a buddy publish to continue after a stale or mismatched approval record.
- Missing, stale, mismatched, unparsable, or format-incomplete reviewer confirmation invalidates approval. The workflow must reject any buddy approval comment whose `digest` value is not exactly 16 lowercase hex characters or does not exactly match the first 16 characters of the current run's full `buddyReviewDigest`. If the environment was approved but the comment omits the exact `dispatchShaPrefix`, `runId`, or digest prefix required above, the target must fail closed and require a fresh manual dispatch; there is no downgrade path that allows a buddy publish without the bound audit surface.

### 3.4 Buddy authorization boundary and minimum environment contract

The authorization boundary for buddy publishing is the direct publish job plus the repository-side controls that scope buddy credentials to manual buddy runs:

- job-level `permissions`
- direct jobs whose workflow/control-plane code is rooted in the selected `workflow_dispatch` snapshot
- repository review on the direct job wiring and helper code at the frozen `dispatchSha`
- pre-created buddy publication environments and credentials that are available only to documented buddy jobs from documented buddy-authorized protected branches

This design does **not** treat “same-repository reusable workflow plus caller-supplied path input” as a real authorization boundary. Any internal buddy mutation worker is only a concurrency wrapper: it must be `workflow_call`-only, must validate that the call came from the documented `buddy.yml` dispatcher through the repository-owned call-site allowlist enforced by reviewed workflow files under CODEOWNERS/bootstrap-hash protection, must keep a worker-internal hardcoded allowlist of permitted dispatcher workflow paths, and must reject any call whose `project-key` is not the canonical dispatcher-emitted key satisfying the full §5.8 project-resolution contract. Caller-emitted binding data is allowed only as a consistency check layered on top of that hardcoded allowlist; it is not authoritative proof by itself. That allowlist is a repository-governance constraint, not a standalone runtime caller-identity primitive. It also does **not** permit a buddy job to bootstrap itself into a new environment. The minimum buddy environment contract is:

- every buddy environment name is deterministic and checked in as `buddy-<surface>-<project-key>`, where `<surface>` is one of `github`, `npm`, `nuget`, or `rubygems`
- every buddy environment is pre-created before the project is buddy-enabled
- every buddy-authorized ref is itself a protected branch and must at minimum prevent force-push and deletion; environment deployment restrictions are additive and do not replace branch protection
- every buddy environment has at least one required reviewer user or team
- `prevent self-review` is enabled on every buddy environment
- every buddy environment has a deployment-branch policy that allows only the exact buddy-authorized refs declared for that project in `.github/repository-release-contract.json`; because that policy is repository-external GitHub state, the external monitor from §7.6 must audit it for drift and open an incident on any mismatch
- every buddy environment must have no wait timer; buddy uses reviewer intent plus the bound audit payload, not a delayed secondary hold, and a wait timer would become an undocumented second approval phase
- referencing a missing buddy environment is a hard failure because GitHub may auto-create unprotected environments
- buddy environments must not rely on repository-level long-lived publication credentials

### 3.5 Buddy targets

Buddy filters to this unofficial target set:

- `nuget:gpr`
- `npm:gpr`
- `rubygems:gpr`
- `github:release`

Python has no unofficial package-registry target. If a Python project needs a buddy preview, it must declare `github:release` and buddy publishes that preview as a GitHub prerelease. `pypi:testpypi` is not a supported target.

### 3.5.1 Python buddy preview rationale

- `pypi:testpypi` is intentionally excluded from this design. TestPyPI is a separate registry surface with separate credentials, separate environment wiring, separate cleanup behavior, and separate partial-publish recovery concerns.
- The buddy channel stays intentionally smaller than the official channel. For Python preview distribution, GitHub prereleases already provide a repository-local surface that avoids introducing a second unofficial Python registry trust domain.
- A Python project that needs buddy preview installation testing must distribute wheels or sdists through `github:release` assets and use repository-owned test instructions that consume those exact preview assets.
- Those repository-owned instructions must document at minimum: the canonical preview asset naming convention; a `pip install --no-index --find-links <release-assets-url> <packageIdentity>==<version>` path and, when a direct asset URL is used, the exact command form including `--hash=sha256:<digest>` for every referenced wheel or sdist; the fact that GitHub Release preview assets are not PyPI and are not a supported package index; the preview asset support lifetime/SLA; and the cleanup policy for superseded preview assets.
- Preview-asset cleanup must be explicit: when a buddy preview is abandoned or superseded, operators must either remove the prerelease or mark it unsupported in the release notes and repository-owned instructions so consumers do not mistake it for the official channel.
- Adding `pypi:testpypi` in the future would require an explicit design amendment covering its target auth contract, confirmation rules, partial-publish cleanup runbook, and why that additional unofficial registry surface is worth the added operational complexity. Until then, it remains unsupported.

### 3.6 Buddy GitHub Release identity and auth

- Buddy GitHub Release identity is separate from the official release identity.
- The buddy tag format is `buddy/<project-key>/v<version>/<dispatchSha>`.
- Because that tag format contains a second `/` segment after `v<version>`, the required protecting ruleset pattern is `refs/tags/buddy/<project-key>/v**`; `v*` is insufficient because GitHub tag-pattern `*` does not cross `/`.
- Buddy GitHub Release tags must be annotated tags, must match the exact `buddyTagPattern` declared for that project in `.github/repository-release-contract.json`, and the workflow must hard-fail if the computed buddy tag would fall outside that namespace or collide with the official tag namespace.
- Buddy `github:release` must always attach to that already-derived buddy tag; it must not reuse the official `release/<project-key>/v<version>` namespace.
- A fresh buddy redispatch of the same frozen buddy identity is idempotent only when the existing buddy tag already points to the same frozen buddy `dispatchSha`, the existing release is attached to that same buddy tag, and the live release asset set exactly matches the current-run immutable artifact set plus digest manifest; otherwise it is a hard conflict.
- For buddy `github:release`, missing, extra, renamed, or digest-mismatched release assets are conflicts, not same-identity no-ops.
- Buddy registry targets use target-specific idempotent publish helpers that consume the current-run immutable artifact set plus digest manifest.
- A buddy registry publish may be treated as a same-identity no-op only when live remote state proves the already-present package version corresponds to the same frozen buddy identity and the same current-run artifact identity; version-only matches are insufficient.
- If a registry already contains the requested version but remote state cannot prove same-identity, or proves different bytes/metadata for that buddy identity, the workflow must hard-fail rather than overwrite or silently accept the conflict.
- Buddy publication credentials must be explicitly minted only after the job has entered the pre-created buddy environment and completed the documented post-approval rebinding checks. Ambient credential availability outside the documented target auth contract is not the authorization boundary for buddy publishing.
- For `github:release`, the job must request a short-lived GitHub App installation token only through the reviewed external credential broker from §7.6.1 after entering the buddy GitHub environment. The buddy GitHub Release actor must be distinct from the protected official ref-writer, the official `github:release` publisher, and the durable-store marker writer, and the buddy and official `github:release` paths must use different GitHub App identities rather than one shared App with different storage or issuance paths. The buddy environment gates broker access only; it must not store the long-lived GitHub App private key in the normal path.
- For GitHub Packages buddy targets (`nuget:gpr`, `npm:gpr`, and `rubygems:gpr`), use the documented GitHub Packages auth contract for that ecosystem, normally job-scoped `GITHUB_TOKEN` with `packages: write`; that permission is broader than the single package being published, and workflow permissions alone do not express package-scoped narrowing. Any narrower repository/package-level access control is external GitHub configuration that must be reviewed separately and recorded as residual risk in the checked-in contract; any stronger GitHub-native package credential is a repository hardening choice that must be documented target-by-target in the checked-in release contract rather than implied by the generic term “GitHub-native”.
- Buddy publishing must not use long-lived publication credentials or normal-path private-key material. Any emergency use of long-lived bootstrap material is break-glass only under §7.5.

### 3.7 Buddy failure and partial publish behavior

- Buddy does not use the official checked-in blocked-state ledger. Its recovery surface is intentionally smaller and is limited to bounded fresh manual redispatches of the same frozen buddy identity plus manual cleanup when same-identity proof fails.
- The expected recovery path for a partial buddy publish is a fresh manual dispatch that resolves to the same frozen `dispatchSha`. GitHub rerun attempts are forbidden. A new manual dispatch that freezes a different `dispatchSha` is a new buddy release identity, not a continuation of the partial one.
- On a fresh redispatch, targets that already prove same-identity must be treated as no-op success, while targets that were not yet mutated or remain uncertain must continue through the normal bounded confirmation logic.
- If any already-published buddy target cannot prove same-identity to the redispatched artifact set, the workflow must fail hard and require manual cleanup instead of overwriting or silently accepting drift.
- Manual cleanup is mandatory runbook content, not an operator improvisation. For every buddy target (`npm:gpr`, `nuget:gpr`, `rubygems:gpr`, and `github:release`), the runbook must define this sequence: detect partial publication; capture evidence for the frozen `dispatchSha`, artifact names, and digests actually published; verify whether the remote bytes match the redispatched artifact identity; perform the target-specific delete/deprecate/yank cleanup if the platform allows it; decide whether the version must be permanently burned when deletion is impossible or identity proof failed; record the cleanup result and disposition; and decide whether a same-identity fresh redispatch is still allowed.
- For `github:release`, the cleanup runbook must explicitly order operations as release-asset evidence capture → release-asset cleanup or release deletion → buddy tag cleanup when needed, so tag identity and asset evidence are not lost prematurely.
- The runbook must state who signs each step: the release engineer opens and tracks the incident, the package owner performs registry-specific cleanup, and the approver on duty records the retry-versus-abandon decision with evidence references.
- If platform rules make the buddy version non-reusable after a partial publish, the runbook must say so explicitly and require the version to be burned rather than retried with changed bytes.

## 4. `official.yml` — Production Release

`official.yml` is the manual production release workflow. It is independent of `buddy.yml`.

The official release tag format is:

- `release/<project-key>/v<version>`

`official.yml` derives that tag internally. It is not a workflow input.

`official.yml` has one supported normal release interface: workflow input `project-key` plus the protected branch selected in the `workflow_dispatch` UI. The input must already be the exact canonical `project-key`; alias resolution, package-name lookup, or fuzzy matching are forbidden. For a normal official release, that selected protected branch is the single trust root for the run: it supplies the trusted workflow/control-plane code, the checked-in release policy inputs, and the release payload source. Recovery and lock-maintenance operations reuse the same workflow file, but they operate only against a previously frozen plan recorded in checked-in admission state or on the authoritative official tag.

### 4.1 Official repository prerequisites

Official release enablement for a project is allowed only after these repository-side controls already exist:

Readiness review is ordered. Repositories must clear these gates in sequence:

| Gate | Priority | Purpose | Minimum examples |
| --- | --- | --- | --- |
| Governance gate | P0 | Authorize the control plane | protected official branches, bootstrap hash + CODEOWNERS coverage, protected official/buddy/live-lock refs, no privileged fork-PR path, no tag-push trigger |
| Protected mutation gate | P1 | Constrain credentialed mutation surfaces | pre-created baseline/subordinate environments, actor separation, durable artifact store configured, commit-marker namespace protection when tag-backed markers are used |
| Operational readiness gate | P2 | Make failure and recovery operable | measured approval-delay budget, runbooks, drill cadence, runner pinning, key rotation and alerting |

- every protected branch that may dispatch `official.yml` for that project is covered by a branch protection rule or ruleset that at minimum:
  - prevents force-push and deletion
  - requires the repository’s official CI gate before merge
  - requires reviewed changes or an explicitly restricted bypass path
- the selected protected branch must itself be the authoritative release line for the resolved project version: `main` for the current mainline release line, or `release/<project-key>/v<release-line>` for a maintenance line
- `.github/CODEOWNERS` must already exist before the first official release is enabled, must cover the bootstrap-governance surface from §2.1, and repository protection/rulesets must require code-owner review for that surface
- official release enablement must include an explicit repository-readiness check that `.github/CODEOWNERS` still covers every bootstrap-governance path from §2.1 and that repository protection/rulesets still require code-owner review for that same surface; bootstrap governance is not considered healthy when either half of that pair is missing
- a project may have different official release lines over time, but the admission file and live lock remain per-project rather than per-release-line; an active live lock on any line therefore blocks all other official lines for that same project until the frozen plan is either completed, returned to `ready`, or explicitly aborted
- repository readiness must include a reviewed confirmation that the repository's current GitHub plan/visibility exposes repository rulesets for tag namespaces; unless re-verified otherwise for that exact repository type, readiness must conservatively assume GitHub Team or GitHub Enterprise Cloud (or an equivalent plan tier that explicitly includes repository rulesets for this repository visibility)
- the official tag namespace `refs/tags/release/<project-key>/v*` is covered by a tag-targeted ruleset
- the buddy tag namespace `refs/tags/buddy/<project-key>/v**` is covered by a tag-targeted ruleset because the actual buddy tag format appends `/<dispatchSha>` after `v<version>`
- the live official lock tag `refs/tags/official-lock/<project-key>` is covered by a tag-targeted ruleset
- when `artifactStore.backendClass` is `oci-registry` or `github-packages`, the commit-marker Git tag namespace `refs/tags/<commitMarkerTagPrefix>*` is covered by a tag-targeted ruleset, and that namespace must not overlap the official, buddy, or live-lock namespaces
- `.github/repository-release-contract.json` and the per-project checked-in admission-state files under `.github/official-admission-state/` exist, are reviewed on every protected official release branch that may dispatch `official.yml`, and are the checked-in policy inputs relied on by that branch’s run snapshot
- before a project’s first official dispatch on a protected branch, that branch already contains `.github/official-admission-state/<project-key>.json` with the exact minimal `status: ready` shape defined in §6.3.1
- every official environment that can grant approval or credentials is pre-created and configured so only runs dispatched from the allowed protected official release branches may enter it. `preflight-validate` must fail closed when it cannot confirm the live GitHub environment policy for the selected run, and the external monitor from §7.6 must continuously audit those policies for drift because they are repository-external state rather than bootstrap-hashed files
- the durable artifact store contract in §4.10 is fully configured before official release is enabled for the project, including write credentials, read credentials for recovery, immutable retention policy, and the declared storage backend type
- every build-affecting toolchain and dependency-resolution input for the enabled release path is already pinned in checked-in files before official release is enabled, including `mise.lock` when used, ecosystem lockfiles when applicable, and any restore/install inputs that would otherwise float across reruns
- every official-release project must record a reviewed measured expectation for the bounded post-approval window that begins at `postApprovalValidatedAt` in `baseline-approval-and-audit` and ends at the final `create-live-lock` revalidation. That measured expectation includes `build-test-package-preparation` plus same-run startup/scheduling jitter before `create-live-lock`, and it must size `approvalToLiveLockMaxDelaySeconds` from that measured expectation rather than from the raw job timeout ceiling. The measured expectation must use a documented process: repository-owned rehearsal or prior successful run timings on the authoritative branch, P95 or stricter runtime selection, explicit inclusion of runner-startup and queue jitter inside the official run, a `600` second safety buffer, a named measurement owner, and mandatory re-measurement after any material build-path change or at least once per quarter. If that measured expectation trends close to the 60 minute `build-test-package-preparation` timeout, official release is not yet ready until the timeout or release path is redesigned
- every official-release project must declare `approvalWaitMaxSeconds`, the maximum wall-clock time one official run may remain waiting for baseline approval while occupying the shared `release/<project-key>` concurrency slot. The external monitor from §7.6 is the authoritative canceller for that wait budget: when a run exceeds the checked-in limit, it must cancel the run, open or update the incident, and record that the slot was abandoned due to approval timeout rather than workflow failure
- `approvalWaitMaxSeconds` sizing must leave a real approval-action window after the baseline environment wait timer elapses. Because the external monitor polls at most every 5 minutes, the checked-in minimum is `baselineWaitTimerMinutes * 60 + 300`, and the normal readiness recommendation is to budget at least `baselineWaitTimerMinutes * 60 + 1800` unless the reviewed readiness record referenced by `readinessEvidenceRef` justifies a smaller buffer
- `approvalToLiveLockMaxDelaySeconds` is a post-approval bound, not a substitute for indefinite approval waiting. `assuranceProfile = high-assurance` projects must keep `approvalToLiveLockMaxDelaySeconds <= 900`. `standard` projects may exceed `900` only with explicit reviewed justification plus reviewed measurement evidence recorded in the checked-in readiness record referenced by `readinessEvidenceRef`
- the repository must keep checked-in release-operations runbooks at stable reviewed paths referenced from `.github/repository-release-contract.json`, including at minimum the break-glass runbook index, the per-target cleanup matrix, the schema-migration runbook, the blocked-entry PR procedure, and the cross-release-line contention / hotfix-preemption procedure required by §7.2; official release enablement is forbidden until those runbooks exist on every protected branch that may authorize the release
- official release enablement is forbidden until the repository has completed and recorded the minimum exercise cadence from §7.5 for the project’s checked-in `assuranceProfile`
- GitHub-hosted runner labels used by the design must be concrete versioned labels rather than `*-latest`; the default runner contract is `windows-2022` for `csharp-pack` and `ubuntu-24.04` for every other current `buildKind`. Repositories that need stronger reproducibility may replace those with self-hosted immutable images, but floating `*-latest` labels are not allowed for official release
- the official tag and live-lock rulesets explicitly allow only the documented official ref-write automation identity plus the documented break-glass actor to create, update, or delete refs in those namespaces
- the buddy tag ruleset explicitly allows only the documented buddy `github:release` publisher identity plus the documented break-glass actor to create, update, or delete buddy tags
- when a tag-backed commit-marker namespace is used, that ruleset explicitly allows only the documented durable-store marker writer identity plus the documented break-glass actor to create, update, or delete commit-marker refs
- the official `github:release` publisher identity is a different GitHub App or automation actor from the protected-ref writer, and the protected tag rulesets do **not** allow the release-publisher actor
- any GitHub App private key stored for release operations is long-lived bootstrap credential material and therefore requires a documented rotation policy of at most 90 days plus alerting before the rotation deadline
- §4.1 minimum startup-set guidance is capability-scoped rather than “provision every App on day one”: the protected-ref writer identity is mandatory for every first official release; the official `github:release` publisher identity is required only when `github:release` is enabled for the official target set; the buddy `github:release` publisher identity is required only when buddy `github:release` is enabled; and the `artifactStoreMarkerWriterActorClass` identity is required only when the selected durable-store backend uses Git commit-marker tags (`oci-registry` or `github-packages`). When a path is not enabled, its GitHub App identity should not be provisioned yet
- `.github/external-control-plane-commitments.json` must already exist before the first official release is enabled, must cover both the external credential broker and the external release monitor, and must pin for each one the signed policy commitment digest, the verifier key reference, the runtime commitment endpoint or equivalent attestation surface, and the reviewed scope of the protected behavior covered by that commitment
- repository settings that would expose fork PR runs to secrets or privileged write tokens remain disabled as an explicit out-of-workflow prerequisite
- `official.yml` must not retain or reintroduce any `on: push: tags:` trigger once this design is enabled; coexistence of tag-push triggering with the reviewed `workflow_dispatch` path is forbidden
- official release enablement is forbidden until every checked-in workflow already satisfies the SHA-pinning rules from §2; there is no grandfathered pre-design workflow exception during rollout

Official publish jobs must not rely on “protected branch” alone as the full trust-root prerequisite. Protected selected-branch workflow code, protected official tags, the protected live-lock tag, and the checked-in durable evidence-store contract are separate requirements.

#### 4.1.1 Build-time measurement helper contract

- `eng/scripts/compute-build-time-p95` is a Day 0 helper reserved by this design so `approvalToLiveLockMaxDelaySeconds` can be justified by one repository-owned measurement path rather than ad hoc manual calculations.
- minimum invocation contract: `eng/scripts/compute-build-time-p95 --project-key <key> --branch <protected-ref> [--sample-size <n>] [--percentile 95] [--format text|json]`
- read contract: it may read GitHub Actions timing metadata, checked-in readiness evidence, and runbook-linked measurement records; it must not mutate repository state or external release state
- measurement contract: it must measure the bounded post-approval window from `postApprovalValidatedAt` through the final `create-live-lock` revalidation using reviewed rehearsal data or prior successful official runs on the authoritative branch, compute P95 or a stricter requested percentile over the sampled durations, and add the required `600` second safety buffer before printing the recommended `approvalToLiveLockMaxDelaySeconds`. When enough data exists on the authoritative branch, the helper must default to at least 30 samples. The output must state how many samples came from prior successful official runs versus rehearsals. Fewer than 10 total samples is insufficient unless the readiness record referenced by `readinessEvidenceRef` carries an explicit waiver; any sub-10 case must include at least 3 recent rehearsal samples when available and must name the risk owner who accepted the smaller data set
- `--format text` must print sample count, branch, percentile used, raw measured percentile seconds, safety buffer, recommended bound, and the oldest/newest sampled run or rehearsal identifiers
- `--format json` must print one closed object with exactly `projectKey`, `branch`, `sampleSize`, `percentile`, `rawPercentileSeconds`, `safetyBufferSeconds`, `recommendedApprovalToLiveLockMaxDelaySeconds`, `measurementSource`, `oldestSampleRef`, `newestSampleRef`, and `measuredAt`
- exit codes: `0` success; `2` invalid invocation; `3` no usable measurement data; `4` external-read failure
- official release enablement remains forbidden until the project's checked-in readiness record referenced by `readinessEvidenceRef` records the measurement owner, source window, and output from this helper or an equivalently reviewed repository-owned wrapper around it

#### 4.1.2 Migration path from legacy repository state

Because implementation has not started, migration targets the repository’s legacy pre-design state rather than preserving any in-place compatibility. The repository must migrate in one reviewed release-freeze sequence:

1. **Prepare prerequisites off the release path.**
   - land the Day 0 helper/tooling set from §1
   - create `.github/repository-release-contract.json`, every required `.github/official-admission-state/<project-key>.json`, and the checked-in runbooks
   - pre-create only the exact baseline, subordinate, and buddy environments required by each enabled project/target set
   - create the protected tag rulesets and durable-store backend wiring
   - provision the external credential broker and external release monitor required by §4.8, §4.9, and §7.6, together with the signed runtime commitments pinned by `.github/external-control-plane-commitments.json`
2. **Freeze release traffic.**
   - stop new buddy and official dispatches
   - confirm no live official lock exists and no blocked-entry PR is mid-review
   - if any project is already mid-release under legacy tooling, finish or abort it before continuing
   - complete the Day -1 legacy-automation clearance checklist on the branch being migrated:
     - no `official.yml` or other replaced legacy workflow still retains `on: push: tags:`
     - no same-repository reusable workflow still acts as an official publish authorization boundary
     - no extra top-level release entry workflow remains beyond `ci.yml`, `buddy.yml`, and `official.yml`
     - every remaining `pull_request_target` workflow that can affect release
       authorization is metadata-only; dependency-maintenance automation may
       exist only as non-release-authority Renovate-style maintenance with
       least privilege, no release mutation worker calls, and no publish or
       protected-ref bypass credentials
     - the branch already contains the checked-in bootstrap prerequisites from §1 and §4.1, so migration does not pause halfway through an enablement sequence
3. **Land one enabling change set per protected branch.**
   - merge the workflow files, checked-in contract, admission-state files, CODEOWNERS/ruleset changes, and documentation updates together
   - remove or disable every legacy trigger, reusable publish boundary, or release path that is replaced by this design in that same reviewed change set
   - mixed states where legacy release automation and this design both have authority for the same project are forbidden
4. **Validate branch-by-branch in deterministic order.**
   - migrate `main` first, then each maintenance branch in documented order
   - after each branch update, run `ci.yml` validation for the bootstrap surface, release contract, and migration-specific drift checks before proceeding
   - `ci.yml` validates only the branch snapshot under test. It must **not** claim repository-wide proof that every other protected branch is already on the same migration/schema epoch, because a workflow run on one ref cannot authoritatively inspect unmerged branch snapshots for enforcement decisions
   - the cross-branch “no mixed schema / no mixed authority” invariant is therefore enforced by the release freeze plus one reviewed repository-owned migration coordinator record referenced from the schema-migration runbook. That coordinator must list every protected official branch, the required migration epoch for this design revision, and whether that branch has completed the reviewed cutover. Official release traffic must remain frozen until that coordinator says every protected branch for the project set is on the same epoch
5. **Enable project release traffic only after readiness passes.**
   - each project remains disabled until its exact target set, environments, durable store, broker path, and monitor coverage are all verified on the authoritative branch
   - phased adoption is still allowed, but only by capability tier: repositories may land `ci.yml` governance and `buddy.yml` first while official release remains disabled; official release still requires the broker before enablement, and there is no degraded broker mode for protected-ref or official GitHub mutation
   - `assuranceProfile = standard` projects may use a time-boxed monitor-bootstrap mode for at most 30 days after broker readiness, but only if the repository records the degraded state in the reviewed readiness record referenced by `readinessEvidenceRef`, runs hourly manual release-state checks by the on-call owner, and lets the §7.6 external monitor take over before the window expires. `high-assurance` projects have no such degraded-monitor bootstrap mode
   - if migration validation fails on any protected branch, restore every already-migrated branch to one consistent pre-enable state before reopening releases

There is no supported partial rollout where `official.yml` is direct-job based on one branch while another branch still relies on legacy release orchestration, tag-push triggering, missing admission-state files, or a different migration/schema epoch for the same project. The migration coordinator record, not `ci.yml` alone, is the authoritative repository-wide cutover checklist for that invariant.

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

For `workflow-only` OIDC targets, the subordinate target environment is branch-scoped rather than fixed per project. Its exact naming rule is defined in §5.9 as `production-<surface>-<project-key>-<branchScopeKey>`.

GitHub Environments are a ref-scoped credential gate, not a workflow-file identity boundary. GitHub does not provide a native checked-in rule that says only `.github/workflows/official.yml` may enter `production-<project-key>` or its subordinate environments. Any other workflow on an allowed protected branch that can target the same environment name could reach that environment unless reviewed repository governance prevents it.

Therefore the design distinguishes two layers: the environment gates credential minting, while reviewed workflow files, bootstrap governance, protected refs, and actor separation constrain which jobs are supposed to request those credentials. In the target design, high-privilege official GitHub mutations (`production-ref-write-<project-key>` and `production-github-<project-key>`) must use an external short-lived credential broker and treat the GitHub environment only as the gate to that broker. Long-lived GitHub App private keys for protected-ref mutation or official GitHub Release publication must not be stored directly in branch-scoped subordinate environments in the normal path.

The baseline environment must encode a real approval boundary. The minimum contract is:

- at least one required reviewer user or team
- `prevent self-review` enabled
- deployment-branch policy or equivalent repository-side restriction that allows entry only from the protected official release branches allowed by the checked-in release contract
- explicit documented admin-bypass policy; if any admin bypass is allowed, it is break-glass only and not part of the normal release path
- the baseline reviewer population should be administratively narrower than the routine workflow-dispatch caller population
- only the single `baseline-approval-and-audit` job may depend on reviewer-gated baseline approval; later jobs may consume its outputs but must not create a second reviewer-gated environment boundary
- a required wait timer expressed explicitly in `.github/repository-release-contract.json`; omission is invalid, `60` minutes is the recommended default, the inclusive maximum is `1440` minutes, and values above `240` minutes require the checked-in machine-readable `baselineWaitTimerJustification`
- a required `approvalToLiveLockMaxDelaySeconds` bound expressed explicitly in `.github/repository-release-contract.json`; omission is invalid, the value must be sized for the project’s measured post-approval window from `postApprovalValidatedAt` through the final stabilized `create-live-lock` revalidation, including approved pre-mutation work (`build-test-package-preparation`), same-run startup/scheduling jitter, and the full bounded live-lock stabilization / retry budget from §4.4 and §4.8 rather than for `create-live-lock` alone. The recommended sizing rule is measured P95 post-approval runtime plus a `600` second buffer and at least the maximum configured live-lock stabilization allowance. `csharp-pack` projects must size this from measured expected runtime, not from the 60 minute build timeout ceiling. Values above `900` seconds require the checked-in machine-readable `approvalToLiveLockDelayJustification`

The post-approval revalidation in §4.3 exists to reject approvals that no longer match the frozen reviewed branch snapshot. In this revision, `official.yml` intentionally keeps one workflow-level shared `release/<project-key>` concurrency slot for the whole official run so every official publish-capable job stays a direct job in `official.yml` and therefore preserves the expected OIDC workflow identity. That is a deliberate correctness-over-throughput tradeoff. Because GitHub’s documented approval history is not attempt-scoped and does not expose one authoritative approval-grant timestamp, the bounded delay is measured from `postApprovalValidatedAt` inside the approved run rather than from an opaque platform `approved_at` field. The required wait timer and the measured `approvalToLiveLockMaxDelaySeconds` process therefore bound normal healthy-monitor occupancy before irreversible mutation starts, but they are not the only abandonment control: §7.6 also requires a degraded-mode cancellation backstop for already-waiting baseline-approval runs when the external monitor is unavailable.

The design makes that occupancy cost explicit. Before the first irreversible mutation, one official run may hold the shared per-project slot for up to:

- `baselineWaitTimerMinutes * 60` seconds of baseline approval wait, plus
- the measured `approvalToLiveLockMaxDelaySeconds` bound from `postApprovalValidatedAt` to the final stabilized `create-live-lock` revalidation, including the bounded live-lock retry / stabilization allowance.

During monitor outage or acknowledged degraded mode, however, any official run that is still pending baseline approval must be cancelled within the §7.6 degraded-mode deadline unless one explicit break-glass exception names that run. After the lock exists, the run may continue through the selected official job timeouts, recovery, or confirmation logic. Buddy preview traffic for the same project therefore inherits that queueing delay. If a newer buddy preview is operationally more urgent and the official run is still only pending or still waiting in `baseline-approval-and-audit`, operators may cancel that not-yet-mutating official run and re-dispatch it later; once `create-live-lock` succeeds, the only legal unblock paths are the normal recovery/clear sequence or the documented break-glass path.

### 4.3 Control-plane trust root and preflight sequencing

For a normal official release, the branch selected in the `workflow_dispatch` UI is the workflow-code, checked-in-policy, and payload trust root. `preflight-validate` must distinguish the current branch snapshot that authorizes the run from the frozen release identity that later jobs actually publish:

- `policy-sha` is the immutable `workflow_dispatch` event snapshot commit for the selected protected branch
- `release-plan` is the immutable canonical release plan consumed by build, test, provenance, tag, and publish jobs
- `release-plan.planDigest` is the canonical digest of the frozen release plan used for lock comparison and recovery identity
- `release-plan.payloadSha` is the immutable source commit snapshot whose build outputs are published when the run needs to build; for a normal release it equals `policy-sha`, while for a reviewed recovery it remains the frozen blocked-plan payload snapshot

The frozen `release-plan` is the exact closed-schema object defined in §5.10. Its `environmentBindings`, `artifactStoreBinding`, and `targetAuthContracts` entries are the exact closed-schema objects defined in §5.10 and §5.11. The separately emitted `targetConfirmationPolicies` are the exact closed-schema objects defined in §5.12; they are validated operational controls rather than release-identity fields and therefore are not part of `planDigest`. Extra keys are forbidden in all three structures.

`official.yml` is modeled as three execution segments inside one direct workflow run: an approval segment (`preflight-validate`, `official-review-surface`, and `baseline-approval-and-audit`, plus `static-analysis` only for the `new-release` path) that freezes the plan and collects approval; an approved pre-mutation segment (`build-test-package-preparation` only); and a mutation segment beginning at `create-live-lock` and continuing through attestation, durable evidence persistence, tag creation, publication, confirmation, and lock clear. The workflow-level shared `release/<project-key>` concurrency group is held for the entire official run so the later publish-capable jobs remain direct `official.yml` jobs rather than reusable-workflow hops. `approvalToLiveLockMaxDelaySeconds` must therefore cover the whole approved pre-mutation segment measured from `postApprovalValidatedAt`, not merely the time spent in `create-live-lock`, and it explicitly includes the bounded live-lock stabilization / retry window required after the create request is submitted. It does **not** include any later internal worker-queue delay because this design no longer uses an official reusable mutation worker. That full-run concurrency choice does **not** make the external monitor the only abandonment mechanism: §7.6 also requires a degraded-mode backstop that cancels already-waiting baseline-approval runs during monitor outage so they cannot hold `release/<project-key>` indefinitely. When the external control-plane suspension record from §7.6 says new official approvals are suspended, `baseline-approval-and-audit` must fail closed even if a human already clicked approval in the GitHub UI.

After `preflight-validate` freezes `policy-sha`, `release-plan`, the blocked-stage discriminator, and any persisted recovery artifact identity, no later job may re-resolve the selected branch HEAD for planning or release identity. `baseline-approval-and-audit` must use GitHub’s documented workflow-run review surfaces only to prove that the current run received the required baseline environment approval and to bind that approval to the frozen review payload. The current documented surfaces are `GET /repos/{owner}/{repo}/actions/runs/{run_id}/approvals` for workflow-run review history and `GET /repos/{owner}/{repo}/actions/runs/{run_id}/pending_deployments` for still-pending review state. Because those APIs are scoped by `run_id` rather than `run_attempt`, and because the documented approvals surface does not expose one authoritative approval-grant timestamp, official publication is single-attempt only: `official.yml` must hard-fail when `github.run_attempt != 1`, and any retry after approval or mutation must use a fresh manual dispatch driven by checked-in state rather than a GitHub rerun attempt. `baseline-approval-and-audit` must, as its first post-approval action, read the current run’s approval history from the documented approvals endpoint, fail closed unless the required baseline environment is unambiguously recorded as approved for this run, immediately re-read the selected protected branch head, and then record `postApprovalValidatedAt`. The pending-deployments API may be used while the job is still waiting for approval, but it is not authoritative historical approval evidence after approval completes. `create-live-lock` must revalidate the same branch head again, must re-read the current checked-in admission-state file from that branch snapshot, and must fail closed unless the selected protected branch still resolves to the frozen `policy-sha`, the admission-state file still authorizes the same frozen plan/recovery path, and the elapsed wall-clock time since `postApprovalValidatedAt` remains less than or equal to the checked-in `approvalToLiveLockMaxDelaySeconds`.

#### 4.3.1 Required attestation/provenance profile

This design chooses one concrete attestation format so every implementation and consumer verifies the same trust chain:

- the authoritative format is GitHub Artifact Attestations backed by GitHub's Sigstore trust root, carrying a DSSE-wrapped in-toto statement whose predicate type is SLSA provenance for the immutable artifact set published by the run
- `attestation-verification` must verify, before any publish-capable continuation, that the attestation subjects exactly equal the canonical filename-and-digest bindings from the digest manifest, that the attesting workflow path is `.github/workflows/official.yml` or the documented internal attestation reusable workflow invoked by it, and that the attestation binds to the same repository and current run identity that produced the immutable artifact set
- the canonical durable `attestationRef` string format is `github-attestation://<owner>/<repo>/runs/<run-id>/attestations/<attestation-id>`; the identifier must resolve to one GitHub Artifact Attestation record whose verified subjects exactly match the stored subject map
- `artifactLocator`, `attestationRef`, and the exact subject filename-and-digest map together form the canonical publishable artifact identity recorded in the durable bundle, official tag annotation, and blocked-entry evidence
- alternate provenance systems, alternate predicate formats, or opaque provider-specific attestation blobs are out of scope unless this document is explicitly revised first

Job sequence:

1. **`preflight-validate`** — no environment
   - validates workflow input `project-key` as the exact canonical internal `project-key`
   - requires the selected `workflow_dispatch` ref to be a branch ref, not a tag ref
   - requires the selected branch to be a protected branch
   - freezes immutable `policy-sha` from the `workflow_dispatch` event snapshot of that selected protected branch
   - reads the checked-in per-project admission/recovery state file from the frozen `policy-sha` and fails closed if the file or schema is missing or invalid
   - for a new release, reads `.github/repository-release-contract.json`, `release.json`, and `packageManifestPath` from that same frozen snapshot to resolve the selected `project-key` to one `projectPath`, one release-enabled baseline environment contract, protected-ref requirements, per-target auth contract, `packageIdentity`, `ecosystem`, `buildKind`, version, official-branch mode, optional `releaseLine`, targets, artifact catalog, and target-to-artifact routing, validates exact manifest/package identity equivalence before version finalization or tag derivation, then constructs the canonical `release-plan` with `payloadSha = policy-sha`
    - for an approved recovery, loads the full frozen `release-plan`, the persisted `lockIdentity`, the blocked-stage discriminator, any existing artifact identity, and machine-readable reviewed recovery authorization from the blocked admission entry; recovery must not rewrite any release-identity field from current checked-in project metadata
   - validates project existence, uniqueness, and single-ecosystem/single-build-kind shape for a new release plan; for recovery it validates that the requested `project-key` and selected protected dispatch branch match the frozen blocked plan being resumed
   - strictly validates `release.json`, target compatibility, target-to-artifact routing completeness, durable-store contract completeness, and target-auth completeness before any environment entry
   - verifies that the selected protected dispatch branch itself matches the authoritative branch rule for the frozen plan:
      - `officialBranchMode = main` requires `main`
      - `officialBranchMode = release-line` requires `release/<project-key>/v<release-line>`
    - derives or verifies the full official release tag ref carried by the frozen plan and computes the canonical `planDigest` only after manifest/package identity equivalence and canonical version normalization both succeed
    - validates the closed schemas in §5.10, §5.11, and §5.12, including that OIDC-backed targets declare exact workflow-enforced `allowedRefClaims`, that those refs contain no wildcard patterns, that each OIDC target records `providerRefClaimSupport`, `providerRefClaimMode`, `providerRefClaimModeRationale` when required, `providerConfigReviewedAt`, machine-readable `providerConfigReviewRef`, and `providerTrustCapabilities`, that `providerConfigReviewedAt` is not later than the current UTC time, not older than 365 days, and that each allowed-ref set is coherent with the project’s `officialBranchMode` and `releaseLine`
   - loads and validates the checked-in per-target `targetConfirmationPolicies` for the selected official targets; those policies are emitted separately from `release-plan` so operators may tune confirmation timing without burning the frozen release identity
   - checks that `.github/repository-release-contract.json` contains a complete, release-enabled entry for the selected `project-key`, then validates required environment names, live-lock requirements, ref-write requirements, evidence-store completeness, artifact-routing completeness, target-auth completeness, confirmation-policy completeness, and release-tag conflicts
   - performs bounded GitHub-side non-mutating checks of the protected live lock tag `refs/tags/official-lock/<project-key>` and the authoritative official tag `refs/tags/release/<project-key>/v<version>`
    - if a live lock exists while the checked-in per-project admission file still says `ready`, fails closed with a dedicated orphan-lock diagnostic that prints the full lock annotation payload, proposes a blocked-entry JSON template, and applies the authoritative boundary rules from §7.4 using the frozen `planDigest` read from the lock payload itself rather than from any new dispatch input: first query the durable artifact store by that frozen `planDigest`; if no authoritative bundle exists use `blockedStage = pre-provenance`; if authoritative durable state cannot yet prove one complete immutable bundle identity use `blockedStage = provenance-uncertain`; if a bundle exists and no publish-confirmation evidence exists use `blockedStage = post-provenance`; otherwise use `blockedStage = post-confirmation`
   - performs bounded GitHub-side non-mutating checks that `production-<project-key>` and every subordinate environment named by the frozen plan already exist and match the required protection policy before any environment entry
    - emits validated outputs including `policy-sha`, the frozen `release-plan`, any persisted `lockIdentity`, validated `targetConfirmationPolicies`, `publishExpectationByTarget`, the blocked-stage discriminator, any persisted blocked artifact identity, and release identity for downstream jobs

2. **`static-analysis`** — no environment; `new-release` only
   - runs after `preflight-validate` only when `recoveryMode = new-release`
   - every blocked recovery mode skips this job entirely; a reviewed recovery must not become permanently impossible only because newer HK rules or control-plane policy changes would reject the same already-frozen payload snapshot today
   - uses the frozen `policy-sha` only for workflow/control-plane files and the frozen `release-plan.payloadSha` for every file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection
   - runs `hk check` over the resolved project path from `release-plan.payloadSha`, any payload-scoped shared/root build inputs from `release-plan.payloadSha`, plus the official release control-plane surface from `policy-sha`:
      - from `policy-sha`: `.github/workflows/official.yml`, `.github/workflows/_build-test-*.yml`, `.github/workflows/_attest-build-*.yml`, `.github/actions/**`, `eng/scripts/**`, `hk.pkl`, and other pure control-plane rule code
      - from `release-plan.payloadSha`: the resolved project path plus any shared/root files actually consumed by the official ecosystem/build-kind path, including files such as `mise.toml`, `mise.lock`, root lockfiles, or root config when they affect the official build inputs
   - must not be reduced to project-path-only validation

3. **`official-review-surface`** — no environment
   - runs only after successful `preflight-validate` and, when the selected path requires it, successful `static-analysis`
   - consumes only validated outputs from `preflight-validate`
   - constructs one canonical closed `official-review-payload` JSON object containing exactly `projectKey`, `policySha`, `payloadSha`, `packageIdentity`, `version`, `officialTag`, `releaseLine`, `planDigest`, `targets`, `requiredBaselineEnvironment`, `requiredTargetEnvironments`, `blockedStage`, `preProvenanceWarning`, `recoveryContext`, `workflowPath`, `runId`, and `runAttempt`; extra fields are forbidden
   - applies the RFC 8785 / JCS null-handling rules from §7.1 to `official-review-payload` too: every required nullable field (`releaseLine`, `blockedStage`, `preProvenanceWarning`, `recoveryContext`, and nested `artifactIdentitySummary`) must remain present as explicit `null` when absent
   - serializes that payload with the RFC 8785 / JCS rules from §7.1, hashes it to `officialReviewDigest`, uploads the JSON as an immutable run artifact, and renders the same fields plus `officialReviewDigest` in the job summary or equivalent repository-owned reviewed surface that approvers can inspect before environment approval. Any approval-related `GITHUB_STEP_SUMMARY` content must be derived only from already-validated frozen outputs. Every scalar data value rendered into that Markdown must be either inline-code fenced (for single-line values) or placed inside a fenced code block (for multi-line or structured values); implementations must not emit raw Markdown-significant text for reviewed fields. If a value cannot be rendered entirely inside code formatting, the workflow must additionally escape Markdown metacharacters at minimum for backtick, backslash, pipe, asterisk, underscore, hash, angle-bracket, bracket, parenthesis, exclamation-mark, and hyphen-plus-space/task-list sequences so reviewed fields cannot create headings, tables, links, images, checkboxes, or raw HTML. When `blockedStage` is non-null, that reviewed surface must display the recovery mode, evidence reference, authorization reference/timestamp, and the persisted artifact identity summary (when present) so approval is bound to concrete recovery bytes and authorization, not only to the frozen plan digest.
   - when `blockedStage = pre-provenance`, the summary must display a high-visibility banner stating that rebuilds are not byte-stable, the version may need to be burned, and operator review is authorizing a last-resort evidence-capture path rather than a routine retry
   - emits the immutable review payload locator plus `officialReviewDigest` for the approval gate

#### Canonical `official-review-payload` schema

`official-review-payload` is a closed object. It contains exactly these fields and no others:

| Field | Type | Notes |
| --- | --- | --- |
| `projectKey` | `string` | Canonical checked-in project key. |
| `policySha` | `string` | 40-character lowercase git commit SHA of the frozen control-plane snapshot. |
| `payloadSha` | `string` | 40-character lowercase git commit SHA of the frozen build/publish payload snapshot. |
| `packageIdentity` | `string` | Exact external package identity. |
| `version` | `string` | Canonical validated release version. |
| `officialTag` | `string` | Exact full official tag ref. |
| `releaseLine` | `string \| null` | Frozen release line, or `null` for `main`-authorized releases. |
| `planDigest` | `string` | Canonical digest of the frozen `release-plan`. |
| `targets` | `string[]` | Lexicographically sorted exact official target list. |
| `requiredBaselineEnvironment` | `string` | Exact human-approval environment `production-<project-key>`. |
| `requiredTargetEnvironments` | `object` | Closed object keyed exactly by `targets`, with each value equal to that target’s subordinate environment name. |
| `blockedStage` | `string \| null` | `null` for a new release; otherwise one of `{pre-provenance, provenance-uncertain, post-provenance, post-confirmation}`. |
| `preProvenanceWarning` | `string \| null` | Non-null only when `blockedStage = pre-provenance`; otherwise `null`. |
| `recoveryContext` | `object \| null` | `null` for a new release. For recovery, closed object containing `allowedMode`, `evidenceRef`, `authorizationRef`, `authorizedAt`, and `artifactIdentitySummary`. |
| `workflowPath` | `string` | Exact path `.github/workflows/official.yml`. |
| `runId` | `integer` | GitHub Actions run id for this official dispatch. |
| `runAttempt` | `integer` | Must be `1`; GitHub rerun attempts are forbidden for official publication. |

All integer-valued fields in `official-review-payload`, `release-plan`, checked-in admission state, and confirmation records must be representable exactly as IEEE 754 safe integers in addition to satisfying any narrower range listed in this document; non-integer numbers and out-of-range integers are invalid because the canonical JSON / cross-language interoperability contract depends on exact numeric round-tripping.

When `recoveryContext` is non-null, it is a closed object with exactly these fields:

| Field | Type | Notes |
| --- | --- | --- |
| `allowedMode` | `string` | Exact reviewed recovery mode from checked-in state. |
| `evidenceRef` | `string` | Exact blocked-entry evidence reference being approved. |
| `authorizationRef` | `string` | Exact reviewed recovery authorization reference. |
| `authorizedAt` | `string` | RFC 3339 UTC timestamp of that checked-in authorization. |
| `artifactIdentitySummary` | `object \| null` | `null` only when no persisted artifact identity exists yet. Otherwise closed object with exactly `artifactLocator`, `attestationRef`, and `subjects`. |

4. **`baseline-approval-and-audit`** — `environment: production-<project-key>`
   - is the authoritative human approval gate
   - runs only after successful `preflight-validate` and `official-review-surface`, plus successful `static-analysis` only when `recoveryMode = new-release`; blocked recovery modes must accept the intentional `static-analysis` skip
   - consumes only validated outputs from `preflight-validate` plus the immutable review payload metadata emitted by `official-review-surface`
   - must declare explicit job-level permissions. `actions: read` is mandatory because this job reads the documented workflow-run approvals and pending-deployments APIs; any additional job-level permissions must be separately justified by another step in this job rather than inherited implicitly
   - does **not** re-resolve the project, targets, version, or payload SHA after environment entry
   - treats the already-rendered `official-review-surface` artifact/summary as the reviewer-facing approval surface because GitHub environment approval UI does not natively render arbitrary workflow outputs before approval
   - re-loads the immutable `official-review-payload` and `officialReviewDigest` produced by `official-review-surface`, recomputes the digest, and fails closed if the reviewed payload no longer matches the frozen validated plan
   - verifies that subordinate target/tag/evidence environments required by the validated plan exist and match policy
   - fails if any required subordinate environment is missing, because GitHub may auto-create unprotected environments on first reference
    - verifies that subordinate environments do not introduce an unintended second human-approval gate; subordinate environments must not require reviewers or wait timers in the normal design
    - reads the current run’s review history from the documented GitHub approvals endpoint, fails closed unless the required baseline environment is unambiguously recorded as approved for this run, fails closed if the observed approval-wait age already exceeds the checked-in `approvalWaitMaxSeconds`, then immediately re-reads the current selected protected branch head and records `postApprovalValidatedAt`; missing, ambiguous, rejected, stale, or unparsable approval data is a hard failure
   - performs any approved live GitHub-side audit or provider-specific non-mutating checks using the already validated plan, including provider-side trust drift checks for OIDC targets when the checked-in `providerSupportsReadOnlyInspection` flag is `true`
   - emits audited environment facts for downstream jobs, including `officialReviewDigest` and `postApprovalValidatedAt`

Every official job that executes repository-owned actions or scripts must use two fixed checkouts: `control-root/` at `policy-sha` and `payload-root/` at `release-plan.payloadSha`. Local composite actions under `.github/actions/**`, helper scripts under `eng/scripts/**`, and other workflow-authored control-plane code must execute only from `control-root/`.

For every post-approval official job that enters a credentialed environment, the required first verification step must run before any checkout, setup action, or local composite action. The job may check out `control-root/` and `payload-root/` only after that verification step succeeds.

### 4.4 Official job outline after approval

This section covers the whole post-approval execution graph. It still has two design phases:

- **Approved pre-mutation phase:** `build-test-package-preparation` executes after approval succeeds and before the first irreversible mutation.
- **Mutation phase:** `create-live-lock` through `release-complete` perform the lock, attestation creation/verification, durable-write, tag, publish, confirmation, and lock-clear work.

Unlike the earlier design revision, both phases remain inside one direct `official.yml` workflow run that already holds the shared `release/<project-key>` concurrency slot. That tradeoff exists specifically so every official publish-capable job remains a direct job in `.github/workflows/official.yml` and therefore presents the expected workflow identity to OIDC-backed trusted-publishing providers.

For every post-approval official job that enters a credentialed environment (`production-ref-write-<project-key>`, `production-evidence-write-<project-key>`, or any official target environment), the first repository-controlled step must perform that job's required lock/identity revalidation before any checkout, setup action, local composite action use, external API call, GitHub App token minting, or OIDC token request. Environment approval is the GitHub credential gate, not proof that the reviewed release identity is still current.

The official flow has four publish-relevant paths:

- **New release path:** build, test, and package from the frozen `release-plan.payloadSha`; create the live lock before any persistent external write; generate fresh attestation subjects from that current-run immutable artifact set only after the lock exists; persist one authoritative artifact identity; create or verify the official release tag carrying that canonical release-identity anchor; then publish.
- **Reviewed recovery path:** use the blocked entry’s machine-readable `blockedStage` to select exactly one recovery mode for the already-frozen release plan. `pre-provenance` recovery is a last-resort rebuild path: it rebuilds and retests from the frozen `release-plan.payloadSha`, writes the first authoritative durable artifact identity for operator review, and then stops without tagging or publishing so operators can confirm the new subject filenames and digests before any publish-capable continuation. When no prior-run digest baseline exists, that path establishes only a newly observed authoritative artifact identity for reviewed continuation; it does **not** prove equivalence to any earlier failed run. If those rebuilt bytes differ from any known prior-run digest evidence for that same frozen plan, the workflow must stop in a blocked state, record `digestChangeReason`, and require the §7.5 break-glass abort path rather than any publish-capable continuation. `post-provenance` recovery restores the previously persisted immutable bundle from `artifactLocator`, using the frozen `artifactStoreBinding` and `environmentBindings.evidenceWrite` carried inside `release-plan` rather than re-resolving current branch contracts, verifies the restored bytes, persisted subject filename-and-digest bindings, and persisted `attestationRef`, then republishes from that restored bundle.
- **Lightweight lock-clear path:** `post-confirmation` recovery verifies that the official tag already carries the expected canonical release-identity anchor and that every selected target is already published, then clears only the residual live lock without rebuilding or republishing. Both `create-release-tag` and `confirm-publish-state` are allowed to detect tamper/mismatch conditions in this verification-only mode, and either one must be able to route the plan directly back to a structured `post-confirmation` blocked entry without depending on the other job to run.
- **Reconciliation-only path:** `provenance-uncertain` recovery performs no publish mutation. It queries the durable artifact store by `planDigest`, reconstructs authoritative `artifactIdentity` when possible, and then stops so operators can submit a new reviewed blocked-entry PR that advances the project either to `post-provenance`, `post-confirmation`, or an explicit abort decision.

Execution contract by path:

| Job | New release | `pre-provenance` | `provenance-uncertain` | `post-provenance` | `post-confirmation` |
| --- | --- | --- | --- | --- | --- |
| build-test-package-preparation | run in approved pre-mutation phase | rebuild in approved pre-mutation phase | load blocked facts only in approved pre-mutation phase | restore bundle in approved pre-mutation phase | verify-only, no bytes restored, in approved pre-mutation phase |
| `create-live-lock` | create at mutation boundary | verify existing lock only at mutation boundary | verify existing lock only at mutation boundary | verify existing lock only at mutation boundary | verify existing lock only at mutation boundary |
| `attestation-verification` | create attestation after lock | create attestation after lock | skip | verify restored identity after lock | verify tag-carried identity after lock |
| `require-provenance` | first durable write | first durable write then stop for reviewed digest confirmation | query-only reconciliation | verify persisted identity only | skip |
| `create-release-tag` | create or verify | skip | skip | create or verify | verify existing tag only |
| direct publish jobs | publish | skip | skip | republish from restored bundle | skip |
| `confirm-publish-state` | confirm all targets | skip | skip | confirm all targets | verify all targets only |
| `release-complete` | clear lock | skip | skip | clear lock | clear lock |

GitHub Actions job conditions must be written so expected skipped upstream jobs do **not** cascade into skipped downstream recovery jobs. In particular:

If the approved pre-mutation phase (`build-test-package-preparation`) fails before `create-live-lock` starts, the run must end without creating a live lock or checked-in blocked entry. That failure consumes the current approval, and any retry is a fresh official dispatch that must obtain fresh approval.

- `preflight-validate` must emit one explicit `recoveryMode` output from the closed set `{new-release, pre-provenance, provenance-uncertain, post-provenance, post-confirmation}`. Every later recovery-sensitive job condition must compare against that explicit mode instead of inferring mode only from skipped upstream jobs.
- `preflight-validate` must also emit `publishExpectationByTargetJson`, a closed JSON object keyed by every selected official target with values from the closed set `{must-run, verify-only}`. The mapping is closed and centralized: `new-release` → every target `must-run`; `pre-provenance` → every target `must-run` for the frozen continuation contract even though the current run stops before publish; `provenance-uncertain` → every target `verify-only`; `post-provenance` → every target `must-run`; `post-confirmation` → every target `verify-only`. Prose references to `publishExpectationByTarget` mean the parsed form `fromJson(needs.preflight-validate.outputs.publishExpectationByTargetJson)`.
- `official-review-surface` must use an explicit condition such as `if: ${{ always() && needs.preflight-validate.result == 'success' && (needs.static-analysis.result == 'success' || (needs.preflight-validate.outputs.recoveryMode != 'new-release' && needs.static-analysis.result == 'skipped')) }}` so recovery paths do not inherit the default skip cascade from the intentionally skipped `static-analysis` job.
- `baseline-approval-and-audit` must use the same `recoveryMode`-aware gating pattern: `static-analysis` success is required only for `new-release`, while every blocked recovery mode must treat an intentionally skipped `static-analysis` job as the expected state rather than as a permanent blocker.
- `attestation-verification` must use an explicit condition such as `if: ${{ always() && needs.create-live-lock.result == 'success' && contains(fromJson('["new-release","pre-provenance","post-provenance","post-confirmation"]'), needs.preflight-validate.outputs.recoveryMode) && needs.build-test-package-preparation.result == 'success' }}` so it runs in every attestation-relevant path, stays skipped in `provenance-uncertain`, and never treats cancelled or failed upstream work as an expected skip.
- `create-release-tag` must use an explicit condition such as `if: ${{ always() && needs.create-live-lock.result == 'success' && contains(fromJson('[\"new-release\",\"post-provenance\",\"post-confirmation\"]'), needs.preflight-validate.outputs.recoveryMode) && (needs.require-provenance.result == 'success' || (needs.preflight-validate.outputs.recoveryMode == 'post-confirmation' && needs.require-provenance.result == 'skipped')) }}`; cancellation must never be treated as equivalent to an expected skip.
- `require-provenance` must use an explicit condition such as `if: ${{ always() && needs.create-live-lock.result == 'success' && contains(fromJson('[\"new-release\",\"pre-provenance\",\"provenance-uncertain\",\"post-provenance\"]'), needs.preflight-validate.outputs.recoveryMode) && (needs.attestation-verification.result == 'success' || (needs.preflight-validate.outputs.recoveryMode == 'provenance-uncertain' && needs.attestation-verification.result == 'skipped')) && needs.build-test-package-preparation.result == 'success' }}` so it runs only in the modes that actually require provenance write or reconciliation work, remains skipped in `post-confirmation`, and never infers success from cancellation or unrelated upstream skips.
- every direct official publish job must use an explicit condition such as `if: ${{ always() && needs.create-live-lock.result == 'success' && contains(fromJson('[\"new-release\",\"post-provenance\"]'), needs.preflight-validate.outputs.recoveryMode) && needs.require-provenance.result == 'success' && needs.create-release-tag.result == 'success' }}` together with an in-job hard check that `fromJson(needs.preflight-validate.outputs.publishExpectationByTargetJson)[target] == 'must-run'`; publish-capable jobs must never infer authority from upstream skip patterns alone.
- `confirm-publish-state` must use an explicit condition such as `if: ${{ always() && needs.create-live-lock.result == 'success' && contains(fromJson('[\"new-release\",\"post-provenance\",\"post-confirmation\"]'), needs.preflight-validate.outputs.recoveryMode) && (needs.create-release-tag.result == 'success' || needs.create-release-tag.result == 'skipped') && (needs.publish-github-release.result == 'success' || needs.publish-github-release.result == 'skipped') && (needs.publish-npm-official.result == 'success' || needs.publish-npm-official.result == 'skipped') && (needs.publish-pypi-official.result == 'success' || needs.publish-pypi-official.result == 'skipped') && (needs.publish-rubygems-official.result == 'success' || needs.publish-rubygems-official.result == 'skipped') && (needs.publish-nuget-official.result == 'success' || needs.publish-nuget-official.result == 'skipped') }}` so it still runs in `post-confirmation` after publish jobs are intentionally skipped, never runs in `provenance-uncertain`, and does not continue after cancelled upstream work or a failed/missing lock acquisition. Inside that job, every target whose expectation is `must-run` must have an actual upstream result of `success`, every target whose expectation is `verify-only` must have an actual upstream result of `skipped`, and unexpected `skipped`, `cancelled`, or `failure` outcomes are hard failures.
- `release-complete` must use an explicit condition such as `if: ${{ always() && contains(fromJson('[\"new-release\",\"post-provenance\",\"post-confirmation\"]'), needs.preflight-validate.outputs.recoveryMode) && needs.confirm-publish-state.result == 'success' }}` so lock clear still runs after the verification-only `post-confirmation` path and stays skipped in `pre-provenance` and `provenance-uncertain`.

4. **One static conditional build-test-package-preparation job**
   - exactly one preparation path runs for the resolved `(ecosystem, buildKind)`
   - the runner contract is fixed by `buildKind`: `csharp-pack` on `windows-2022`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-24.04`
   - workflow-owned local actions and helper scripts execute from `control-root/`; project build inputs come only from `payload-root/`
   - for a new release, this job must execute the build-kind’s required compile/build work, unit tests, and package/pack validation against the frozen `release-plan.payloadSha`, then emit one immutable release-artifact set plus a digest manifest for every file allowed to reach any publish destination
   - every digest-manifest entry must record both the canonical asset filename and the canonical SHA-256 digest for that artifact alias so later GitHub Release verification can prove name-and-digest identity
   - for a reviewed recovery with `blockedStage = pre-provenance`, this job reruns the documented build/test/package path from the frozen `release-plan.payloadSha` and emits a fresh immutable artifact set plus digest manifest for first-time provenance capture for that already-frozen plan
   - for a reviewed recovery with `blockedStage = provenance-uncertain`, this job must not rebuild or restore bytes; it only loads the blocked entry, the live-lock payload, and the frozen `planDigest` required for read-only durable-store reconciliation
   - for a reviewed recovery with `blockedStage = post-provenance`, this job must restore the previously persisted immutable artifact bundle from the blocked entry’s durable `artifactLocator`, must not rebuild or repackage, and must emit the restored digest manifest for downstream verification
   - for a reviewed recovery with `blockedStage = post-provenance`, if the durable store read fails with unavailability, timeout, or digest mismatch after the stage has already been reached, the run must stop and record `blockedStage = post-provenance` with the corresponding artifact-store reason rather than misclassifying the failure as a pre-provenance event
   - for a reviewed recovery with `blockedStage = post-confirmation`, this job must not rebuild, repackage, or restore bytes; it only loads the recorded artifact identity needed to verify that the canonical release-identity anchor already exists
   - this job must emit the digest-manifest hash or restored manifest hash needed by downstream jobs to prove that the downloaded manifest was not replaced in-run

5. **`create-live-lock`**
   - runs only after successful build-test-package-preparation when the selected path still needs an active live lock
   - enters only the ref-write environment required for the selected project
   - before any lock mutation or verification, must re-read the selected protected branch head, re-read the current checked-in admission-state file from that branch, and fail closed unless the branch still equals the frozen `policy-sha` and the admission state still authorizes the same run path; for new releases it must also fail if the elapsed time since `postApprovalValidatedAt` exceeds the checked-in `approvalToLiveLockMaxDelaySeconds`
   - for a new release, creates the protected live official lock tag `refs/tags/official-lock/<project-key>` for the same frozen `release-plan` before the first irreversible external mutation of the run, including GitHub Artifact Attestation creation and any durable artifact-store write
   - because GitHub does not document linearizable refs/tags create+read semantics, a successful lock create or verify is authoritative only after a bounded same-API-surface stabilization protocol observes the expected ref and annotation payload unchanged at least 3 times over at most 60 seconds; `403`, `429`, abuse-throttle responses, and other rate-limit surfaces are uncertain observations that must honor `Retry-After` or reset metadata rather than being treated as absence
   - if a new-release run finds that the live lock already exists for a different frozen `planDigest`, it must classify the result before treating it as a security incident: when the observed lock payload matches another same-repository reviewed run that is already authoritative for that `project-key` (for example an in-flight mutation-stage run or an existing reviewed blocked entry), the current run stops with `LOCK_HELD_BY_CONCURRENT_RUN` and does **not** raise `LOCK_STOLEN`; only a lock payload that cannot be reconciled to another legitimate repository-authorized run is treated as `LOCK_STOLEN`
   - if a new-release run finds that the live lock already exists for the **same** frozen `planDigest`, it must not silently proceed. It must verify whether the existing lock payload names the same `runId`, `runAttempt`, and authoritative blocked-entry or success record already known to the repository. If the lock belongs to the current run attempt, treat it as idempotent lock verification; if it belongs to an earlier same-plan run with no authoritative blocked or success record yet, fail closed with `LOCK_REUSE_REQUIRES_REVIEW` and require operators to create or update checked-in blocked state before any continuation.
   - this lock is a best-effort interruption boundary, not an atomic stop boundary: removing it after a downstream job’s final revalidation may still allow one or more already-started external requests inside the current target job to complete
   - for `pre-provenance`, `provenance-uncertain`, `post-provenance`, and `post-confirmation`, verifies that the existing live lock is still present and still names the same frozen `planDigest` **and** the same frozen `lockInstanceToken`; it must not silently recreate a missing recovery lock
   - the live lock must be an annotated tag whose annotation payload carries at minimum the frozen `planDigest`, an unpredictable unique `lockInstanceToken`, `payloadSha`, `packageIdentity`, `version`, `officialTag`, `runId`, `runAttempt`, the selected `blockedStage` when applicable, the timestamp/actor that established the lock, and when issue creation later fails the latest structured blocked-entry draft or a durable pointer to it
   - every later mutation-stage job must compare that same `lockInstanceToken` rather than only `planDigest`; if the lock was deleted and recreated for the same plan, or if the observed token differs from the frozen one, the job must fail with `LOCK_STOLEN` or the more specific classified result and must not continue to external mutation
   - uses only the dedicated repository-ref-write credential defined in §4.8

6. **One static conditional `attestation-verification` job**
   - exactly one `attestation-verification` job runs
   - runs only after successful `create-live-lock` when the selected path still needs attestation generation or verification
   - for a new release, it generates attestation from the current run’s build output only
   - for a reviewed recovery with `blockedStage = pre-provenance`, it generates attestation from that recovery run’s rebuilt artifact set for the already-frozen plan because no persisted attestation exists yet
   - for a reviewed recovery with `blockedStage = provenance-uncertain`, it skips attestation generation entirely; `require-provenance` consumes the reconciliation inputs already emitted by `build-test-package-preparation` plus preflight outputs
   - for a reviewed recovery with `blockedStage = post-provenance`, it reuses the previously recorded `attestationRef` and verifies that the restored digest manifest exactly matches the persisted blocked artifact identity
   - for a reviewed recovery with `blockedStage = post-confirmation`, it only verifies that the canonical release-identity anchor already recorded on the official tag still matches the checked-in blocked entry
   - attestation subjects must be the digest-manifest entries for the immutable artifact set actually published; publish-only repackaging is forbidden
   - GitHub Artifact Attestation creation is treated as a persistent GitHub-hosted evidence write in this design, so attestation generation must occur only after the live lock already exists
   - if attestation generation, persistence, or verification fails after the live lock exists and before one authoritative durable bundle is established, the workflow must route the project to `blockedStage = pre-provenance` with `reason = attestation-generation-failed`
   - if a `post-provenance` restore or verification run finds that the restored attestation subjects, attesting workflow identity, or persisted `attestationRef` no longer match the already-recorded blocked `artifactIdentity`, the workflow must preserve `blockedStage = post-provenance` and record `reason = attestation-verification-failed`; this is a recorded-identity mismatch, not a regression to pre-provenance
   - the attestation format is the concrete GitHub Artifact Attestations profile defined in §4.3.1, and `attestation-verification` must fail closed if the generated or restored attestation cannot be verified against that profile before any publish-capable job continues

7. **`require-provenance`**
   - runs only after successful `create-live-lock` and, when required for the selected path, successful `attestation-verification`
   - enters only the evidence-write environment required for the selected project
   - consumes validated outputs from preflight jobs instead of redoing canonicalization
   - before any durable-store write or reconciliation decision, must re-read `refs/tags/official-lock/<project-key>` and hard-fail with `LOCK_MISSING` or `LOCK_STOLEN` unless the live lock still exists and its annotation payload carries the same frozen `planDigest` and `lockInstanceToken`
   - verifies that the attestation subjects exactly match the immutable artifact digest manifest selected for publication, including canonical filename-to-digest bindings
   - for a new release, writes the immutable artifact bundle, digest manifest, and attestation/provenance record to the durable artifact store using only the credential scoped to `production-evidence-write-<project-key>`
   - for a new release, that durable write must use the `create-if-absent`, `get-by-planDigest`, and `verify-digest` operations defined in §4.10 with write-once semantics keyed by `planDigest`
   - every successful write must perform mandatory read-back verification before the workflow may emit `artifactLocator` or `attestationRef`
   - for a blocked recovery with `blockedStage = pre-provenance`, this job must verify the pre-existing live lock only; if the lock is missing or mismatched, the run must stop for operator diagnosis instead of silently recreating it
   - for a blocked recovery with `blockedStage = pre-provenance`, performs the first durable write for that already-frozen plan using the same write-once semantics keyed by `planDigest`, then emits the newly created `artifactLocator`, `attestationRef`, and artifact-subject map, records those subject filename-and-digest bindings for reviewed blocked-state follow-up, automatically generates the structured blocked-entry draft for the intentional stop described in §6.4, and stops the run before tag creation or publication; reviewed recovery authorization must explicitly allow `rerun-plan` for this stage and must acknowledge that this is the last-resort non-byte-stable recovery path
   - for a blocked recovery with `blockedStage = pre-provenance`, when no prior-run subject digest baseline exists for the same frozen plan, that job must immediately persist the machine-readable `no-prior-digest-baseline` diagnostic as a `riskFlags` entry in the workflow-generated blocked-entry draft, event-evidence payload, and any live-lock-carried fallback draft instead of waiting for a later manual PR edit
   - if a `pre-provenance` durable write receives `create-if-absent.status = already-exists`, the run must fail closed, persist structured evidence of the returned bundle metadata, and require the next checked-in blocked-state transition to move to `blockedStage = provenance-uncertain` with `reason = existing-bundle-ownership-ambiguous`; it must not silently treat that response as same-run success
   - if that `pre-provenance` rebuild produces bytes whose digest manifest differs from any known prior-run digest evidence for the same frozen plan, the workflow must fail closed, emit structured digest-drift evidence, require the blocked entry to record `digestChangeReason`, and prohibit any publish-capable continuation for that version without the §7.5 break-glass abort decision
   - for new releases and `pre-provenance` recovery, no discretionary wait, second approval, or unrelated mutation step is allowed between `create-live-lock`, `attestation-verification`, and `require-provenance`; minimizing that interval is part of reducing the byte-drift risk acknowledged in §7.3
   - for a blocked recovery with `blockedStage = provenance-uncertain`, performs a read-only `get-by-planDigest` reconciliation, verifies any discovered bundle, and either reconstructs authoritative `artifactIdentity` for operator review or fails with a structured irreconcilable-state diagnostic; reviewed recovery authorization must explicitly allow `reconcile-store` for this stage
   - if `provenance-uncertain` reconciliation reaches a reviewed conclusion that the durable artifact store is permanently unavailable, corrupted beyond repair, or otherwise cannot produce one authoritative immutable bundle identity for the frozen `planDigest`, the next required checked-in state transition is `recovery.approvalState = aborted`; the design must then route the plan to the §7.5 break-glass abort path and record whether the version is burned
   - for a blocked recovery with `blockedStage = post-provenance`, verifies that the selected publication artifact set exactly matches the persisted blocked artifact identity before any further external mutation starts, and re-emits the already-recorded `artifactLocator`, `attestationRef`, and artifact-subject map without replacing them in-run
   - `blockedStage = post-confirmation` skips this job entirely because the authoritative artifact identity already exists

8. **`create-release-tag`**
   - runs only after successful `require-provenance` when the selected path still needs official tag creation or verification, using an explicit `if:` condition that allows expected skipped upstream jobs
   - enters only the ref-write environment required for the selected project
   - before any tag mutation or verification decision, must re-read `refs/tags/official-lock/<project-key>` and hard-fail with `LOCK_MISSING` or `LOCK_STOLEN` unless the live lock still exists and still carries the same frozen `planDigest` and `lockInstanceToken`
   - for new release and `post-provenance` paths, creates or verifies the protected official release tag `refs/tags/release/<project-key>/v<version>`
   - because GitHub does not document linearizable tag create+read semantics, `create-release-tag` must use the same bounded same-API-surface stabilization protocol as `create-live-lock` before treating tag presence, annotation contents, or deletion as authoritative
   - any tag creation, update, compare-read, or tag-ruleset failure that is not a same-identity tag conflict must route the project to `blockedStage = post-provenance` with `reason = tag-write-failure`; true same-name different-identity conflicts use `reason = tag-conflict`; for `post-confirmation` verification-only runs, however, a failed compare-read or anchor mismatch is a tamper-sensitive verification failure that must preserve `blockedStage = post-confirmation` with `reason = post-confirmation-verification-failed` and must trigger the structured blocked-entry / incident path directly from `create-release-tag` even if `confirm-publish-state` does not later run
   - for `post-confirmation`, verifies that the already-existing protected official release tag still carries the canonical frozen release-identity anchor; it must not create, retarget, or rewrite that tag in this mode. Any mismatch, unreadable anchor, or stabilization failure in this verification-only path is itself authoritative evidence for `blockedStage = post-confirmation` with `reason = post-confirmation-verification-failed`; `confirm-publish-state` is not the only job allowed to emit that blocked outcome
   - the official release tag must be an annotated tag whose annotation payload durably records the canonical frozen release-identity anchor for that release, including at minimum the frozen `planDigest`, `payloadSha`, `packageIdentity`, `version`, `officialTag`, the canonical frozen `release-plan` JSON used to compute `planDigest`, `artifactLocator`, `attestationRef`, and the exact artifact subject filename-and-digest map
   - official tag existence alone never proves that every target publish succeeded; success is established only by `confirm-publish-state` plus `release-complete` lock clear, or by an equivalent reviewed recovery path that reaches the same state
   - uses only the frozen `release-plan.payloadSha`
   - uses only the dedicated repository-ref-write credential defined in §4.8
   - must complete successfully before any official publish job starts

9. **Direct publish jobs**
   - one direct job per official target
   - each job mutates exactly one destination
   - each job depends on successful baseline approval, validated preflight outputs, successful build-test-package-preparation, provenance, and tag creation when those stages are required for the selected recovery mode
   - when `publishExpectationByTarget[target] = must-run`, that target's publish job must execute and succeed; an unexpected `skipped` result is a hard failure recorded as `reason = publish-job-failure` rather than being silently treated as a verification-only path
   - each job consumes only the immutable artifact set and digest manifest selected and verified earlier in the run
   - publish jobs must not rebuild, repackage, or substitute files after attestation
   - before any external mutation, each direct publish job must independently re-read `refs/tags/official-lock/<project-key>` and hard-fail with `LOCK_MISSING` or `LOCK_STOLEN` unless the live lock still exists and still carries the same frozen `planDigest` and `lockInstanceToken`
   - within each credentialed publish job, that lock revalidation and audit-payload revalidation must be the first repository-controlled step before any local composite action use
   - the target-specific publish credential or OIDC token must be minted or requested only after that final lock revalidation succeeds so the race window between validation and mutation is minimized
   - `release-plan.payloadSha` is metadata for identity and audit only at publish time; the publish bytes come from the attested or restored immutable artifact set named by the frozen plan and artifact identity
   - `blockedStage = post-confirmation` and `blockedStage = provenance-uncertain` must skip publish jobs entirely because those modes are verification-only or reconciliation-only
   - a direct publish job that cannot obtain or validate its brokered or OIDC credential must fail closed as `publish-job-failure`; it must not fall back to a different credential path or silently downgrade to verification-only behavior

10. **`confirm-publish-state`**
    - confirms selected destinations serially in lexicographic target order inside one direct job; the timeout contract in §5.12 and the default timeout table below are defined on that serial execution model, not on parallel target probing
    - confirms selected destinations from live remote state
    - uses bounded retries, target-specific attempt budgets, and explicit target-specific confirmation policies from the closed confirmation-policy schema in §5.12
    - records in-run results for the current run **and** persists immutable per-target confirmation evidence to the durable artifact store contract in §4.10 before reporting a target as confirmed
    - if live remote state appears successful but `put-confirmation(planDigest, target, record)` cannot durably persist the immutable confirmation record, the job must fail closed, keep the project in `blockedStage = post-provenance`, and refuse to clear the live lock; in-memory observation alone is not enough to upgrade recovery state
    - does not scan unbounded historical run state
    - before confirming any target, checks `publishExpectationByTarget`: `must-run` targets require an upstream publish result of `success`, while `verify-only` targets require an upstream publish result of `skipped`; unexpected `skipped`, `cancelled`, or `failure` states are hard failures
    - uses an explicit `if:` condition that permits verification to run after intentionally skipped publish jobs in `post-confirmation`
    - for `post-confirmation`, it performs verify-only confirmation against every target named by the frozen plan and does not require any publish job in the current run
    - for `post-confirmation`, any mismatch in the protected official tag anchor, the persisted confirmation records, or the live remote target identity is a hard stop that records `blockedStage = post-confirmation` with `reason = post-confirmation-verification-failed`; the workflow must not regress to rebuild/re-publish recovery and must not clear the live lock until reviewed incident handling has repaired or explicitly aborted the plan
    - confirmation logic must classify remote outcomes: `404`/not-yet-visible, transient `429`, and transient `5xx` failures are retryable; deterministic conflicts such as same-version different-content evidence or explicit provider conflict responses are terminal and must stop immediately
    - retries must use truncated exponential backoff with full jitter and must account for API latency, token issuance time, and response parsing through `perAttemptBudgetSeconds`. They must also account for provider-mandated wait windows such as `Retry-After` through the separate `providerDelayBudgetSeconds` field from §5.12; provider-enforced sleep is budgeted wall clock, not free time outside the target timeout
    - attempt `1` starts immediately. After each failed attempt `k` where `k < confirmMaxAttempts`, confirmation sleeps one jittered delay sampled within `[0, confirmIntervalSeconds * 2^(k-1)]`; jitter must stay inside that per-gap ceiling and must not add extra unbudgeted wall-clock beyond it
    - `confirmTimeoutSeconds` is the total wall-clock budget for the whole target confirmation loop. Under the exact retry model above, the required minimum timeout is `confirmIntervalSeconds * (2^(confirmMaxAttempts - 1) - 1) + confirmMaxAttempts * perAttemptBudgetSeconds + providerDelayBudgetSeconds`, where `providerDelayBudgetSeconds` is the maximum cumulative wall-clock allowance reserved for provider-mandated waits such as `Retry-After`. Every selected target confirmation policy must satisfy that inequality, and `confirm-publish-state` must fail contract validation before any publish starts when that relation does not hold. If cumulative provider-mandated wait exceeds either `providerDelayBudgetSeconds` or the remaining `confirmTimeoutSeconds` budget, confirmation must stop deterministically and record the target as timed out rather than silently overrunning the contract
    - each persisted confirmation record must be a closed immutable object containing at minimum `recordDigest`, `target`, `planDigest`, `version`, `outcome`, `confirmedAt`, provider or API status/response classification, request or correlation identifiers when available, `recordedDuringRecovery` when the monitor backfills a missed event after degraded mode, and the strongest remote identity proof available for that target (for example asset name-and-digest, tarball digest, distribution filename set plus digests, gem digest proof when exposed, or an explicit `digest-proof-unavailable` marker). `recordDigest` is the canonical digest used by §4.10 idempotency checks.
    - if all publish jobs completed but `confirm-publish-state` itself fails or times out, operators must treat the project as `blockedStage = post-provenance` unless and until reviewed persisted confirmation records plus external evidence prove every selected target succeeded; once that proof exists, the blocked entry may advance to `post-confirmation` and only the `clear-lock-only` path remains legal
    - recommended defaults for currently enableable official targets are:

| Target | `confirmMaxAttempts` | `confirmIntervalSeconds` | `perAttemptBudgetSeconds` | `providerDelayBudgetSeconds` | `confirmTimeoutSeconds` |
| --- | ---: | ---: | ---: | ---: | ---: |
| `github:release` | 3 | 10 | 10 | 0 | 110 |
| `npm:official` | 5 | 30 | 15 | 300 | 1020 |
| `pypi:official` | 5 | 30 | 15 | 300 | 1020 |
| `rubygems:official` | 5 | 30 | 15 | 300 | 1020 |

    - `nuget:official` has no default confirmation profile in this revision because the target itself remains `BLOCKED: pending-provider-review`.

11. **`release-complete`**
    - final aggregation gate for the workflow
    - uses an explicit `if:` condition that permits lock clear after verification-only `post-confirmation` runs
    - on successful confirmation of every selected destination, performs a compare-delete check by re-reading `refs/tags/official-lock/<project-key>` and failing closed with `LOCK_MISSING`, `LOCK_STOLEN`, or `lock-integrity-failure` unless the still-live lock matches the frozen `planDigest` and the current authorized run or recovery authority before deleting anything
    - only after that compare-delete check succeeds may it clear the protected live official lock tag for the selected project using the same dedicated repository-ref-write credential
    - the lock-clear operation must use bounded retry with exponential backoff for at most 60 seconds total
    - if lock clear still fails after bounded retry, the run outcome is `published-with-lock-residue`, not a generic re-publish failure; the workflow must emit `lock-clear-failed: true`, keep the lock in place, and route the project to the lightweight `clear-lock-only` recovery mode rather than to full rebuild/re-publish recovery
    - `published-with-lock-residue` is a blocked release outcome, not a warning-only success; it must trigger the §6.4 blocked-entry/issue-creation path with `blockedStage = post-confirmation` and `recovery.allowedMode = clear-lock-only`

Reference timeout contract for `official.yml` (overrideable per project in `.github/repository-release-contract.json`):

| Job | Default `timeout-minutes` |
| --- | ---: |
| `preflight-validate` | 10 |
| `static-analysis` | 15 |
| `official-review-surface` | 10 |
| `baseline-approval-and-audit` | `ceil(max(baselineWaitTimerMinutes * 60, approvalWaitMaxSeconds) / 60) + 20` |
| `build-test-package-preparation` | 60 for `csharp-pack`, 30 for all other build kinds |
| `create-live-lock` | 5 |
| `attestation-verification` | 15 |
| `require-provenance` | 10 |
| `create-release-tag` | 5 |
| `publish-github-release` | 15 |
| `publish-npm-official` | 15 |
| `publish-pypi-official` | 15 |
| `publish-rubygems-official` | 15 |
| `publish-nuget-official` | 15 |
| `confirm-publish-state` | `ceil(sum(selectedTargetConfirmationPolicies.confirmTimeoutSeconds) / 60) + 5` |
| `release-complete` | 5 |

### 4.5 Official targets

Official filters to this target set:

- `nuget:official`
- `npm:official`
- `pypi:official`
- `rubygems:official`
- `github:release`

There is no separate `github:official` target.

`nuget:official` remains a reserved official target name but is **BLOCKED: pending-provider-review** in this revision. Repositories must not enable it in checked-in project config until a later reviewed design revision closes the NuGet audience contract described in §4.9 and §5.11.

For the official channel, `github:release` must attach to the already-created official tag `release/<project-key>/v<version>`. It must fail closed if GitHub would need to auto-create or retarget that tag. Same-identity acceptance requires both the protected annotated official tag identity and an exact match between the live GitHub Release asset set and the authoritative artifact identity for the frozen plan by both canonical asset name and digest. Tag-only equality is insufficient.

### 4.6 External-system checks

This design does not depend on extra scheduled readiness workflows or aging snapshot artifacts.

If a provider-specific readiness or authorization check is still required, it must be either:

- a checked-in policy fact consumed during `preflight-validate`, or
- a bounded same-run check performed in `baseline-approval-and-audit` after the baseline approval gate

No official admission decision may depend on scanning arbitrarily old workflow runs.

### 4.7 Baseline and subordinate environment requirements

- `production-<project-key>` must be pre-created before the workflow is enabled for that project.
- That environment must carry the expected protection rules; a missing or unprotected baseline environment is a hard failure.
- The minimum acceptable baseline protection policy is the same minimum contract defined in §4.2.
- The baseline environment should be used for approval and narrowly scoped audit facts only. It must not be treated as the default storage location for publication credentials.
- Any subordinate environment referenced by the validated plan must also be pre-created before workflow enablement.
- Subordinate environments must not independently require human reviewers in the normal design. Any exception must be documented explicitly in this design and in `.github/repository-release-contract.json` together with its interaction with the baseline approval job.
- Subordinate environments must not define wait timers in the normal design. The baseline environment is the only approved wait-timer boundary; subordinate wait timers would create an undocumented second approval phase and distort the measured `approvalToLiveLockMaxDelaySeconds` budget.
- Referencing a missing environment is never an acceptable bootstrap path, because GitHub may auto-create it without the required protection semantics.
- Subordinate environments are not a workflow-path isolation primitive. Their native GitHub protection is ref-scoped, so the design must treat repository governance and reviewed workflow wiring as the controls that keep other allowed-branch workflows from targeting the same environment name.
- Post-approval changes to checked-in admission state do not retroactively cancel an in-flight run by themselves; the documented cancellation signal for already-approved runs is live-lock removal or mismatch, which downstream jobs must revalidate before irreversible mutation.

### 4.8 Protected repository-ref write contract

This design uses only concrete GitHub tag refs for protected official repository writes:

- `refs/tags/release/<project-key>/v<version>`
- `refs/tags/official-lock/<project-key>`

Commit-marker tags used by the §4.10 durable artifact store for `oci-registry` and `github-packages` are a separate namespace. They are not mutated by the normal `production-ref-write-<project-key>` path and instead use the dedicated `artifactStoreMarkerWriterActorClass` under the evidence-write contract.

The repository-ref write contract is:

- only `create-live-lock`, `create-release-tag`, `release-complete`, and the documented `clear-lock-only` maintenance path may mutate those refs
- those jobs must enter `production-ref-write-<project-key>`
- the credential used there is a dedicated GitHub App installation token for this repository; by design `GITHUB_TOKEN` is not the protected-ref writer for official release/tag-lock operations
- that GitHub App must hold only the repository permissions required for reviewed protected-tag mutation, must be distinct from the actor used by official `github:release`, and the workflow must mint its installation token inside `production-ref-write-<project-key>` through the reviewed external credential broker described in §7.6.1. Because GitHub Environments do not natively bind credentials to one workflow file path, directly storing the long-lived App private key in that branch-scoped environment is no longer part of the normal design. The broker request contract in §7.6.1 is authoritative and the broker must validate at minimum repository, workflow path, job name, run id, run attempt, project key, required environment name, and requested actor class before minting the short-lived installation token
- ref-level restrictions are enforced by the corresponding tag-targeted rulesets, not by the token alone
- the corresponding tag-targeted rulesets must allow only that ref-write GitHub App actor plus the documented break-glass actor to create, update, or delete the protected release-tag and live-lock refs; commit-marker tag namespaces are protected separately and must allow only the dedicated `artifactStoreMarkerWriterActorClass` plus break-glass
- the official `github:release` publisher actor must not appear on the protected-tag bypass list; environment names alone are not a separation boundary if the same actor can be minted in both environments
- the official `github:release` publisher, the buddy `github:release` publisher, and the protected-ref writer must map to separate GitHub App identities in the broker policy and in repository governance; sharing one GitHub App identity across those paths is forbidden because the approval surfaces, key-custody rules, and failure domains are intentionally different
- environment approval does not itself bypass protected tag rules; the credential and actor allowance must already be correct
- GitHub does not document linearizable create/read/delete semantics for refs or tags. This design therefore treats protected-ref state as authoritative only after the operation-specific bounded read-back and stabilization rules succeed on the same GitHub API surface; a one-off `201`, `200`, or `404` by itself never proves durable lock creation, absence, or deletion.
- All protected-ref reads and writes must classify `403`, `429`, abuse-throttle responses, and exhausted `x-ratelimit-*` budgets as retryable or uncertain based on `Retry-After` or reset metadata. Lock protocols must use truncated exponential backoff with full jitter, must never interpret throttling as proof that a lock is absent, and must fail closed if the state cannot be stabilized within the documented wall-clock budget. The bounded stabilization allowance assumed elsewhere in this design is 60 seconds, and any project-specific `approvalToLiveLockMaxDelaySeconds` sizing must include that full allowance rather than assuming the older 30-second heuristic.

### 4.9 Official target authentication contract

Official target authentication must be explicit and target-scoped. Repository-level long-lived publication credentials are out of scope.

| Target | Auth class | Required subordinate environment | Current exact-ref support record | Credential rule |
| --- | --- | --- | --- | --- |
| `github:release` | GitHub-native API auth | `production-github-<project-key>` | not applicable | Use a dedicated GitHub App installation token minted through the reviewed external credential broker from §7.6.1 after entry to this environment. It must be a different actor from the protected ref-writer in §4.8, must not appear on the protected-tag bypass list, and long-lived publication credentials are forbidden. The broker request contract from §7.6.1 is authoritative: repository, workflow path, job name, run id, run attempt, project key, required environment name, and requested actor class are all mandatory bound inputs. |
| `nuget:official` | External-registry OIDC trusted publishing | reserved | `providerRefClaimSupport` = reserved | **BLOCKED: pending-provider-review.** `nuget:official` is not enableable in this revision because the repository still lacks one approved closed NuGet audience contract. It is a reserved target name only, not a release-ready default. The checked-in project config must not include `nuget:official` in `officialEnvironments.targets`, `officialTargetAuthContracts`, or `officialTargetConfirmationPolicies`, and no official run may request `id-token: write` for NuGet.org until a later reviewed design revision removes this block. |
| `npm:official` | External-registry OIDC trusted publishing | `production-npm-<project-key>` when `providerRefClaimMode != workflow-only`; otherwise `production-npm-<project-key>-<branchScopeKey>` | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`, must always bind the exact repository and exact official workflow path, must always record the non-null checked-in `providerEnvironment`, and must use audience `npm:registry.npmjs.org`. Prefer `provider-enforced`; `workflow-only` is legal only when the checked-in support record says exact ref enforcement is `unsupported` or `unknown` for that provider/target pair. Only the publish job may receive `id-token: write`. If the provider-side trusted-publishing capability cannot satisfy the recorded contract for the project, `npm:official` is not enabled. |
| `pypi:official` | External-registry OIDC trusted publishing | `production-pypi-<project-key>` when `providerRefClaimMode != workflow-only`; otherwise `production-pypi-<project-key>-<branchScopeKey>` | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`, must always bind the exact repository and exact official workflow path, must always record the non-null checked-in `providerEnvironment`, and must use audience `pypi`. Day 0 enablement must still re-confirm that exact value against then-current first-party PyPI trusted-publishing documentation before `pypi:official` is enabled. Prefer `provider-enforced`; `workflow-only` is legal only when the checked-in support record says exact ref enforcement is `unsupported` or `unknown` for that provider/target pair. Only the publish job may receive `id-token: write`. If the provider-side trusted-publishing capability cannot satisfy the recorded contract for the project, `pypi:official` is not enabled. |
| `rubygems:official` | External-registry OIDC trusted publishing | `production-rubygems-<project-key>` when `providerRefClaimMode != workflow-only`; otherwise `production-rubygems-<project-key>-<branchScopeKey>` | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`, must always bind the exact repository and exact official workflow path, must always record the non-null checked-in `providerEnvironment`, and must use audience `rubygems.org`. Until reviewed provider evidence proves exact ref-claim enforcement, the default contract is `providerRefClaimMode = workflow-only` with capabilities at least `repository`, `workflow-path`, and `environment`. Only the publish job may receive `id-token: write`. If the provider-side trusted-publishing capability cannot satisfy the recorded contract for the project, `rubygems:official` is not enabled. |

The validated official release plan must include `targetAuthContracts` keyed by target. Each entry contains exactly the closed-schema fields in §5.11, including the required environment name, auth class, allowed credential source, exact workflow-enforced `allowedRefClaims`, provider trust summary, `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, `providerRefClaimModeRationale`, `providerConfigReviewedAt`, and machine-readable `providerConfigReviewRef`. Confirm-publish retry parameters live in the separately validated `targetConfirmationPolicies` from §5.12 and are intentionally excluded from the frozen release identity. Wildcard ref claims are forbidden. A target with no documented auth contract is not releaseable.

Workflow-side branch enforcement is mandatory for every OIDC-backed target: `preflight-validate` must verify that the selected protected branch is one of the checked-in `allowedRefClaims` for the frozen plan before any publish job may request `id-token: write`.

For every OIDC-backed target, `providerEnvironment` and `providerKey` must both be non-empty exact checked-in values. The checked-in `providerTrustCapabilities` set records which provider-side claims are actually enforced from the closed set `{repository, workflow-path, environment, ref}`, while `providerRefClaimSupport` and `providerSupportsReadOnlyInspection` record per-target/provider support facts consumed by validation and audit.

Provider-side exact ref-claim pinning is preferred and is required whenever the checked-in support record says exact ref claims are `supported` for that target. In that case `providerRefClaimMode` must be `provider-enforced`. `workflow-only` is a lower-assurance compensating-control mode, not a peer security level to `provider-enforced`: it is legal only when `providerRefClaimSupport` is `unsupported` or `unknown`, `providerRefClaimModeRationale` is a non-null machine-readable reason, and the provider-side capability set contains at least `{repository, workflow-path, environment}`.

Provider-side trusted-publishing configuration is repository-external state and therefore part of release readiness, not an implementation detail. `.github/repository-release-contract.json` must store the expected provider-side trust summary and support record for each official target, and any change to official workflow path, environment naming, or allowed refs must update both the checked-in contract and the provider-side configuration. `preflight-validate` checks internal coherence of those fields, including `providerConfigReviewedAt <= now()` in UTC. `baseline-approval-and-audit` performs a bounded read-only provider-side drift check whenever the checked-in `providerSupportsReadOnlyInspection` flag is `true`.

When `providerSupportsReadOnlyInspection = false`, the workflow has no independent runtime proof that the provider-side configuration still matches the checked-in contract. In that mode, release readiness additionally requires a repository-reviewed manual verification record carried by `providerConfigReviewedAt` and machine-readable `providerConfigReviewRef`, and any official target that uses `workflow-only` ref enforcement must refresh that manual verification at least every 7 days for `standard` projects and at least every 24 hours for `high-assurance` projects. `providerConfigReviewRef` is not a free-form opaque string: it must point to one machine-readable evidence record whose schema is defined in §5.11. A stale, future-dated, missing, or >365-day-old verification record is a hard failure before publication, and §7.6 requires pre-expiry alerting plus best-effort external provider-drift probes rather than waiting for release-time failure. Those reviews, probes, and alerts are compensating controls for provider-side drift; they are not a native provider guarantee. Because `workflow-only` omits provider-enforced exact-ref binding, any target in that mode is explicitly lower assurance than `provider-enforced` even when every compensating control is healthy; repositories should therefore use `workflow-only` only as a reviewed exception path, not as the preferred steady-state posture. `baseline-approval-and-audit` must additionally fail closed unless the latest external provider-freshness monitor result is still within the expected cadence for the project (`<= 24 hours` for `standard`, `<= 1 hour` for `high-assurance`) and is not `inspection-error` or `inspection-unavailable`. When `providerRefClaimMode = workflow-only`, each branch snapshot that enables that target must bind exactly one allowed official branch ref and one branch-scoped subordinate environment whose deployment-branch policy admits only that same branch, and that environment must use the deterministic branch-scoped naming rule from §5.9. Sharing one workflow-only OIDC environment across multiple official release lines for the same project is forbidden. For those `workflow-only` targets, the external monitor cadence is assurance-sensitive: `high-assurance` projects require best-effort drift probes at least once per hour, while `standard` projects may use a period up to 24 hours.

### 4.10 Durable artifact store contract

The durable artifact store is a first-class part of the official release design because recovery depends on it. Placeholder URIs such as `artifact-store://...` are descriptive only; an enabled project must bind them to one of these concrete backend classes in `.github/repository-release-contract.json`:

- `oci-registry`
- `azure-blob-storage`
- `github-packages`

For new projects, `oci-registry` or `azure-blob-storage` is preferred over `github-packages` when the repository already operates those backends, but every backend must still satisfy the same authoritative-visibility contract documented below.

The required store interface is:

- `create-if-absent(planDigest, bundle)`
- `get-by-planDigest(planDigest)`
- `verify-digest(locator, expectedDigestManifest)`
- `put-confirmation(planDigest, target, record)`

Store contract rules:

- the stored bundle format is a single immutable archive containing the publishable artifacts, the digest manifest, the canonical frozen `release-plan` JSON, and by default the §4.3.1 GitHub Artifact Attestation record itself; storing only a durable pointer is allowed only when the pointed-to system offers retention, immutability, availability, backup, and disaster-recovery guarantees at least equal to the bundle backend, and that equivalence is documented in the checked-in repository contract
- `create-if-absent` must be atomic only at the **authoritative visibility** boundary of the storage contract: `get-by-planDigest(planDigest)` must observe either no authoritative bundle or one complete verified bundle, never a partially visible bundle. This design does **not** claim that every underlying multi-system write (for example OCI upload plus commit-marker Git tag creation) is one native storage transaction. Authoritative reads and writes must use one explicitly documented backend-consistency surface; non-authoritative replica or secondary reads are forbidden for lock, recovery, and confirmation decisions.
- `create-if-absent` must return one closed response object with exactly `status`, `bundleIdentity`, and `conflictClass`. `status` is the closed enum `{created, already-exists, conflict}`. `bundleIdentity` is required for `created` and `already-exists`, forbidden for `conflict`, and is itself the exact closed tuple `{artifactLocator, bundleSha256, attestationRef, subjectsSha256, bundleFormatVersion}` where `bundleSha256` is the digest of the immutable stored bundle bytes and `subjectsSha256` is the canonical digest of the exact subject filename-and-digest map carried by that bundle. `conflictClass` is required only for `conflict` from the closed set `{bundle-metadata-mismatch, incomplete-authoritative-state, marker-divergence, corruption}`.
- concurrent `create-if-absent(planDigest, bundle)` callers must converge deterministically: a losing caller returns `already-exists` only when the already-present authoritative record proves exact equality of `(planDigest, artifactLocator, bundleSha256, attestationRef, subjectsSha256, bundleFormatVersion)` with the candidate bundle. If any field differs, is incomplete, or cannot be proved equal, the losing caller returns `conflict`
- if the underlying backend cannot provide native atomic create visibility, the repository contract must require an equivalent commit-marker design so `get-by-planDigest(planDigest)` returns empty until the bundle, digest manifest, release-plan copy, and attestation pointer are all durably committed together
- if bundle upload or read-back verification succeeds but the commit-marker create step fails, times out, or remains ambiguous, the overall `create-if-absent` operation is not successful: publication must stop, the workflow must emit an orphan-upload diagnostic, and recovery must classify the plan from stable marker visibility rather than assuming the uploaded bytes are authoritative
- if the new-release path receives `create-if-absent.status = already-exists` before the current run has established one authoritative bundle identity for its own lock instance, the workflow must fail closed, preserve the live lock, capture the returned bundle metadata, and route the project to `blockedStage = provenance-uncertain` with `reason = existing-bundle-ownership-ambiguous`; it must not silently treat the earlier authoritative bundle as same-run success
- for `backendClass = oci-registry`, `create-if-absent` must upload the immutable bundle as digest-addressed OCI content inside the configured repository, perform mandatory read-back verification, and then create exactly one commit-marker Git tag named `refs/tags/<commitMarkerTagPrefix><planDigest-hex>` only after that verification succeeds, where `planDigest-hex` is the 64 lowercase hexadecimal suffix of `planDigest` with the `sha256:` prefix removed. That commit-marker tag write is performed by the dedicated `artifactStoreMarkerWriterActorClass`, not by `actors.refWriterActorClass`, and the workflow must mint that actor only inside `production-evidence-write-<project-key>` through the reviewed broker path. `get-by-planDigest` must resolve the authoritative bundle exclusively through that marker tag, and a missing marker means the bundle is absent even if uncommitted OCI blobs or manifests were uploaded. A marker tag that resolves to different verified bundle metadata is storage corruption and a hard failure. The runbook and external monitor must define OCI orphan-upload detection as reconciliation between visible uploaded bundle/manifests and visible commit-marker tags: any upload that remains visible without a corresponding marker beyond the explicit backend grace period documented in the checked-in runbook (which must be finite and no longer than 48 hours) is an orphan candidate that must be incident-tracked and cleaned up through the backend-specific procedure. That procedure must define the scan cadence, the evidence recorded before cleanup, and the exact delete/unlist/burn decision path for the backend.
- for `backendClass = azure-blob-storage`, every authoritative `create-if-absent`, `get-by-planDigest`, `verify-digest`, and `put-confirmation` operation must use the storage account's primary endpoint only. RA-GRS or other secondary reads may be used for non-authoritative diagnostics only and must be labeled stale/non-authoritative. Both the immutable bundle blob and the commit-marker blob must use the block-blob type; append blobs and page blobs are forbidden. `create-if-absent` must write the immutable bundle under the configured `blobPrefix`, verify the uploaded bytes, and only then create exactly one commit-marker block blob named `<commitMarkerBlobPrefix><planDigest-hex>.json` on the primary endpoint using an atomic create-if-absent operation equivalent to `If-None-Match: *`. `get-by-planDigest` must resolve the authoritative bundle exclusively through that marker blob, and a missing marker means the bundle is absent even if payload blobs or staged blocks were uploaded earlier. When the bundle upload uses staged blocks (`Put Block` + `Put Block List`), the workflow must stage them under one unique payload blob name per attempted write; uncommitted blocks, abandoned staged blocks, or failed staging blobs are not authoritative state and must be treated as orphan candidates by the backend-specific runbook until they are cleaned up or expire. The marker blob must contain at minimum the authoritative bundle locator, the frozen `planDigest`, the persisted digest-manifest digest, and the attestation/provenance reference so recovery can rehydrate one complete verified bundle identity without historical run scans.
- for `backendClass = github-packages`, only the container-backed GitHub Packages/ghcr surface is supported. `create-if-absent` must upload the immutable bundle as a digest-addressed package version, then create exactly one commit-marker Git tag named `refs/tags/<commitMarkerTagPrefix><planDigest-hex>` only after read-back verification succeeds, where `planDigest-hex` is the 64 lowercase hexadecimal suffix of `planDigest` with the `sha256:` prefix removed. That commit-marker tag write is performed by the dedicated `artifactStoreMarkerWriterActorClass`, not by `actors.refWriterActorClass`, and the workflow must mint that actor only inside `production-evidence-write-<project-key>` through the reviewed broker path. `get-by-planDigest` must resolve the authoritative bundle exclusively through that marker tag, and a missing marker means the bundle is absent even if uncommitted package bytes were uploaded. A marker tag that resolves to different bundle metadata than the verified bundle is storage corruption and a hard failure. The runbook and external monitor must define GitHub Packages orphan-upload detection with the same marker-vs-visible-upload reconciliation model used for OCI: any package version or manifest that remains visible without a corresponding commit-marker tag beyond the explicit backend grace period documented in the checked-in runbook (which must be finite and no longer than 48 hours) is an orphan candidate that must be incident-tracked and cleaned up through the backend-specific procedure. That procedure must define the scan cadence, the evidence recorded before cleanup, and the exact delete/unlist/burn decision path for the backend
- `get-by-planDigest` must resolve the authoritative `artifactLocator`, the exact `github-attestation://...` `attestationRef`, the subject filename-and-digest map, and every immutable per-target confirmation record previously persisted with `put-confirmation(planDigest, target, record)` without requiring historical workflow-run scans
- `verify-digest` must prove that the fetched bundle still matches the expected subject filename-and-digest bindings before any restored bytes are published
- `put-confirmation` must persist one immutable per-target confirmation record under the same frozen `planDigest`. Each record carries a canonical `recordDigest` over the full closed record. Retrying the exact same `recordDigest` is allowed and must be idempotent; attempting to replace a different record for the same target/outcome boundary is a hard conflict. After any ambiguous timeout or connection loss, the workflow must resolve the result by reading the existing record and comparing `recordDigest` before retrying. For weak-proof outcomes such as `digest-proof-unavailable`, the workflow may persist at most one conservative uncertain record for that target in that run and must stop in blocked state rather than oscillating between competing records. Later recovery runs must not keep rewriting fresh uncertain records for the same target/plan; they may either discover stronger proof and persist that exact stronger record, or stop after one reviewed verification pass and require an explicit reviewed terminal disposition (`post-confirmation` under a target policy that accepts the available proof, or `recovery.approvalState = aborted`). Indefinite weak-proof retry loops are forbidden. Those records are authoritative recovery evidence for advancing a blocked release from `post-provenance` to `post-confirmation`
- every write attempt uses at most 3 attempts with exponential backoff, 60 seconds maximum per attempt, and 180 seconds maximum wall-clock time for the whole operation
- every successful write must be followed by mandatory read-back verification before the workflow may emit `artifactLocator` or `attestationRef`
- if the store is unavailable or verification fails, the workflow must fail fast, emit a structured error code such as `ARTIFACT_STORE_UNAVAILABLE`, `ARTIFACT_STORE_DIGEST_MISMATCH`, or `ARTIFACT_STORE_TIMEOUT`, keep the live lock in place, and require checked-in blocked-state evidence rather than silently degrading to ephemeral GitHub Actions artifacts
- write credentials are available only in `production-evidence-write-<project-key>` and must be short-lived OIDC-issued or equivalently brokered credentials; long-lived publication credentials are forbidden for normal operation, but long-lived key material used only to mint short-lived credentials may be stored in the protected environment when documented in the checked-in contract. When the backend uses commit-marker Git tags, the broker must be able to mint the distinct `artifactStoreMarkerWriterActorClass` for that namespace without reusing the protected release-tag writer actor
- recovery reads must use a read-only credential administratively narrower than the write credential
- blocked-release bundles, attestation records, and persisted confirmation records must be retained until the blocked entry is cleared, and never less than one year from the blocked entry’s `updatedAt`
- successful-release bundles, attestation records, and persisted confirmation records must be retained for at least two years from official tag creation, or longer when the checked-in repository release contract declares a longer retention period
- every enabled backend class must have documented capacity/quota monitoring, orphan-upload cleanup procedures, credential-rotation procedures, retention/immutability verification, and a backend-specific disaster-recovery runbook before official release is enabled
- the checked-in repository release contract must also declare the durable-store resilience strategy for each enabled project: either a second independent immutable copy or a reviewed backup/replication plan with explicit RTO/RPO values. A project is not release-ready until that resilience strategy exists and the latest required restore drill from §7.5 has passed
- when `backendClass = azure-blob-storage` and the declared resilience target is at least `RPO <= 15 minutes` / `RTO <= 60 minutes`, the reviewed strategy must include at minimum: region-loss-tolerant storage or a second independent immutable copy outside the primary failure domain; blob versioning or an equivalent immutable-history mechanism for commit markers and confirmation records; a documented backup/export cadence no worse than 15 minutes for whichever metadata would otherwise be lost on regional failover; and a rehearsed operator failover/restore procedure that re-establishes authoritative primary-endpoint reads within 60 minutes. If the repository cannot currently prove all four properties, it must declare weaker RPO/RTO targets instead of implying the stronger pair
- for `backendClass = github-packages`, the runbook must cover orphan uploaded package versions whose commit-marker tag was never written, marker/tag divergence, and cleanup of uncommitted versions after failed writes

## 5. Release Configuration Contract

Each releasable project must define `<project-root>/release.json`.

### 5.1 Schema

```json
{
  "schemaVersion": 1,
  "packageIdentity": "@three/example-project",
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
  },
  "npmAccessHint": "public"
}
```

### 5.2 Fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `schemaVersion` | `number` | Yes | Must be `1`. |
| `packageIdentity` | `string` | Yes | Exact external package identifier published to the ecosystem. |
| `packageManifestPath` | `string` | Yes | Explicit repo-relative path to the manifest or project file that defines `packageIdentity`; the workflow validates exact identity equivalence from this file before `planDigest` is computed. |
| `buildKind` | `string` | Yes | Closed set `{csharp-pack, python-package, node-npm, node-wxt, ruby-gem}`. |
| `officialBranchMode` | `string` | Yes | Closed set `{main, release-line}` defining which protected branch shape may authorize official releases. |
| `releaseLine` | `string` | Conditionally | Required when `officialBranchMode = release-line`; forbidden when `officialBranchMode = main`; must match `(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)` when present. |
| `targets` | `string[]` | Yes | Non-empty array of unique publish targets in `ecosystem:destination` format. |
| `artifacts` | `object` | Yes | Non-empty artifact catalog keyed by checked-in artifact alias. |
| `targetArtifacts` | `object` | Yes | Exact target-to-artifact routing map. |
| `npmAccessHint` | `string` | No | Optional checked-in npm access hint for `node-npm` projects declaring `npm:*`; closed set `{public, restricted}`. |

### 5.3 Validation rules

- `release.json` must be valid JSON.
- `schemaVersion` must equal `1`.
- `packageIdentity` must be present and non-empty.
- `packageManifestPath` must be present, must normalize to exactly one repo-relative manifest or project file under the resolved project root after path normalization and symlink resolution, and that exact file must resolve the same `packageIdentity` observed by the ecosystem-specific resolver.
- `preflight-validate` must perform the `packageManifestPath` identity-equivalence check before target filtering, before version/tag derivation, and before `planDigest` computation. Any mismatch between `release.json.packageIdentity`, the manifest-resolved identity, and the repository-contract-resolved project identity is a hard failure.
- `buildKind` must be one of the documented supported values.
- the resolved project `ecosystem` is derived only from `buildKind` using the authoritative total mapping in §5.5: `csharp-pack -> csharp`, `python-package -> python`, `node-npm -> jsts`, `node-wxt -> jsts`, and `ruby-gem -> ruby`
- `officialBranchMode` must be either `main` or `release-line`.
- `releaseLine` is required only when `officialBranchMode = release-line`.
- When `officialBranchMode = main`, `releaseLine` must be absent.
- When present, `releaseLine` must match `(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)`, must not be empty, and `release/<project-key>/v<releaseLine>` must be a valid Git ref name.
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

| Target | Channel use | Processed by | Description | Conservative version-burn behavior |
| --- | --- | --- | --- | --- |
| `nuget:gpr` | Buddy only | `buddy.yml` | Publish `.nupkg` to GitHub Packages NuGet feed | Repository-owned delete-capable package surface, but reuse is allowed only after explicit cleanup or same-identity proof. |
| `nuget:official` | Official only | `official.yml` | Reserved official target name for future NuGet.org trusted publishing; **BLOCKED in this revision pending reviewed audience closure** | Treat uncertain or differing same-version publication as burned for automatic reuse; operator cleanup may unlist but must not assume safe republish. |
| `npm:gpr` | Buddy only | `buddy.yml` | Publish npm tarball to GitHub Packages npm registry | Repository-owned delete-capable package surface, but reuse is allowed only after explicit cleanup or same-identity proof. |
| `npm:official` | Official only | `official.yml` | Publish npm tarball to npmjs | Treat the version as effectively burned once same-identity cannot be proved; deprecate is available, while delete/unpublish is not a dependable normal-path recovery tool. |
| `pypi:official` | Official only | `official.yml` | Publish wheel/sdist to PyPI | Treat published version/file identities as burned for reuse on uncertainty; operator cleanup may yank, but the design must not rely on deletion-and-republish as the routine recovery path. |
| `rubygems:gpr` | Buddy only | `buddy.yml` | Publish gem to GitHub Packages RubyGems host | Repository-owned delete-capable package surface, but reuse is allowed only after explicit cleanup or same-identity proof. |
| `rubygems:official` | Official only | `official.yml` | Publish gem to RubyGems.org | Treat uncertain or differing same-version publication as burned for automatic reuse; operator cleanup may yank, but rerun with changed bytes is forbidden. |
| `github:release` | Buddy and official | `buddy.yml`, `official.yml` | Publish release assets to GitHub Releases | Delete-capable release surface; reuse is allowed only after exact release/tag cleanup or same-identity proof. |

`pypi:testpypi` and `github:official` are not supported targets. `nuget:official` remains reserved but configuration-invalid until the pending provider-review block from §4.9 / §5.11 is removed in a later design revision.

### 5.5 Ecosystem/build-kind target compatibility matrix

The mapping below is authoritative and total for v1. `ecosystem` is derived from `buildKind`; it is not an independently configurable field anywhere in the checked-in release metadata.

| Resolved ecosystem | `buildKind` | Allowed targets |
| --- | --- | --- |
| `csharp` | `csharp-pack` | `nuget:gpr`, `github:release` (`nuget:official` remains reserved but blocked by §4.9 / §5.11 in this revision) |
| `python` | `python-package` | `pypi:official`, `github:release` |
| `jsts` | `node-npm` | `npm:*`, `github:release` |
| `jsts` | `node-wxt` | `github:release` |
| `ruby` | `ruby-gem` | `rubygems:*`, `github:release` |

### 5.6 Version resolution and validator contract

Version validation is ecosystem-aware. The workflow must first resolve the project’s canonical ecosystem/build-kind identity, then run exactly one validator family for that resolved release path:

| Resolved ecosystem | `buildKind` | Canonical version source | Required validator family |
| --- | --- | --- | --- |
| `csharp` | `csharp-pack` | The releasable normalized NuGet package version resolved by the canonical .NET packaging toolchain for the releasable `.csproj` at the frozen SHA | NuGet package version validator |
| `python` | `python-package` | The releasable version resolved by the canonical Python packaging metadata/toolchain at the frozen SHA | PEP 440 public version validator |
| `jsts` | `node-npm` | The releasable version resolved by the canonical Node/npm release path at the frozen SHA | npm SemVer validator |
| `jsts` | `node-wxt` | The releasable version resolved by the canonical Node/WXT release path at the frozen SHA | npm SemVer validator |
| `ruby` | `ruby-gem` | The releasable gem version resolved by the canonical RubyGems release path at the frozen SHA | RubyGems/Gem::Version validator |

For `csharp-pack`, the canonical version is the exact normalized NuGet public package version string that the pack/push toolchain would publish. The official tag `release/<project-key>/v<version>` must use that normalized form exactly; a raw project-file literal that normalizes differently is not a distinct release identity and must never appear in the official tag namespace.

For `node-wxt`, the canonical releasable version and package identity both come from the exact `package.json` located at `packageManifestPath`. `node-wxt` is the release path for built web-extension artifacts, not for npm-registry publication. Its build contract produces redistributable release assets for `github:release` only; a project that needs npm publication must use `node-npm` instead of `node-wxt`.

If a `node-wxt` project also needs distribution through Chrome Web Store or another browser-store surface outside this design, that store may impose an additional manifest-version format such as Chrome’s four-integer requirement. This design does not derive or validate any browser-store-specific version mapping; it freezes only the repository’s canonical `package.json.version` value used for the GitHub Release path, and any separate store-specific version translation must be reviewed as a distinct out-of-scope release surface.

- Valid `csharp-pack` frozen `version` examples: `1.2.3`, `1.2.3-rc.1`.
- Invalid `csharp-pack` frozen `version` examples: `v1.2.3` (leading tag prefix belongs only to the Git ref), `1.2.3+build.5` (NuGet build metadata is not part of the canonical published version identity).

### 5.7 Artifact routing contract

The build workflow and release metadata must together define exactly which immutable files may reach which destinations.

- `artifacts` is the checked-in per-project catalog of artifact aliases.
- Each alias declares one canonical artifact `kind`.
- The build workflow must emit digest-manifest entries keyed by those aliases only. Each entry must include the canonical output filename used for publication and the canonical `sha256:<64 lowercase hex>` digest of that file.
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
- `github:release` may reference any explicitly declared artifact aliases, but only those aliases. Recovery and confirmation logic must prove exact GitHub Release asset identity by both canonical filename and digest using the persisted artifact identity.

### 5.8 Project resolution contract

- `project-key` must match `[a-z0-9](?:[a-z0-9._-]*[a-z0-9_-])?`, must be `1..100` characters, must not contain the substring `..`, and must not end with the suffix `.lock`.
- Releasable `project-key` values are canonical ASCII lowercase repository-safe names.
- `.github/repository-release-contract.json` must map each release-enabled `project-key` to exactly one `projectPath`.
- `packageIdentity` is the external package identity and may differ from `project-key`; no workflow or helper may derive it by lowercasing, de-scoping, or otherwise normalizing `project-key`.
- Project resolution starts from the checked-in `project-key` entry in `.github/repository-release-contract.json`, then uses the checked-in ecosystem identity at that resolved project root.
- `packageManifestPath` identity equivalence is part of project resolution itself: `preflight-validate` must resolve the canonical manifest file, read package identity and version from that exact file, and prove exact string equality before it constructs the frozen `release-plan` or computes `planDigest`.
- C# projects resolve by the `PackageId` declared in the releasable `.csproj` at `packageManifestPath`. Recommended resolver: `dotnet msbuild <path> -getProperty:PackageId`.
- Python projects resolve by `[project].name` at `packageManifestPath`. Recommended resolver: `python - <<'PY'` using `tomllib` to read `pyproject.toml`.
- `jsts` projects resolve by `package.json.name` at `packageManifestPath`. Recommended resolver: `node -p "require('./package.json').name"` executed from the manifest directory.
- `node-wxt` uses that same `package.json` identity source at `packageManifestPath`, and its canonical releasable version is `package.json.version` validated with npm SemVer rules.
- `node-wxt` build outputs must be releasable extension artifacts routed only as `github-release-asset` entries. The design does not treat `node-wxt` as an npm-registry publish path.
- Ruby projects resolve by the exact `packageIdentity` declared by the `.gemspec` at `packageManifestPath`. Recommended resolver: `ruby -e 'spec = Gem::Specification.load(ARGV[0]); abort unless spec; puts spec.name' <path>`.
- A resolved project root must map to exactly one supported ecosystem and exactly one supported `buildKind`.
- No match, ambiguous match, unsupported ecosystem, unsupported build kind, or multi-language/multi-build-kind match is a hard failure.
- `<project-root>/release.json` is required; there is no inheritance or upward fallback.

### 5.9 Repository release contract

The repository-wide release contract lives at `.github/repository-release-contract.json`. It is the single checked-in machine-readable source of truth for repository-side release readiness and privileged release wiring. The file is a closed JSON object: unknown keys at any level are hard failures until explicitly added to this design.

Top-level schema:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `schemaVersion` | `number` | Yes | Must equal `1`. |
| `projects` | `object` | Yes | Object keyed by canonical `project-key`. Each value uses the closed per-project schema below. |
| `prTrustModel` | `object` | Yes | Closed object recording the repository PR trust rules consumed by `ci.yml`. |

Per-project schema (`projects.<project-key>`) is also closed and contains exactly these fields:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `projectPath` | `string` | Yes | Canonical repo-relative project root. |
| `assuranceProfile` | `string` | Yes | Closed set `{standard, high-assurance}`. `standard` is the default profile sized for normal open-source / small-team operation; `high-assurance` opts into the stricter drill cadence and offline custody expectations from §7.5. |
| `releaseEnabled` | `boolean` | Yes | `true` enables official release consideration. |
| `buddyAuthorizedRefs` | `string[]` | Yes | Exact buddy-authorized branch refs; lexicographically sorted; wildcard refs forbidden; must be non-empty when any buddy target is enabled and must otherwise be the empty array. |
| `buddyEnvironments` | `object` | Yes | Closed object keyed by enabled buddy target names (`github:release`, `npm:gpr`, `nuget:gpr`, `rubygems:gpr`) to deterministic environment names `buddy-<surface>-<project-key>`. |
| `officialEnvironments` | `object` | Yes | Closed object containing `baseline`, `refWrite`, `evidenceWrite`, and `targets`. `targets` is a closed object keyed by official target name to the exact subordinate environment name for the current branch snapshot; `workflow-only` targets must use the branch-scoped naming rule defined below. |
| `officialJobTimeoutMinutes` | `object` | No | Closed object of positive integer minute overrides keyed only by the formal timeout keys defined below. |
| `baselineWaitTimerMinutes` | `number` | Yes | Explicit reviewer wait timer in integer minutes. Must be in the inclusive range `1..1440`. `60` is the recommended default; values above `240` require the checked-in machine-readable `baselineWaitTimerJustification`. |
| `baselineWaitTimerJustification` | `string \| null` | Yes | Machine-readable kebab-case justification for `baselineWaitTimerMinutes > 240`. Must be `null` when the wait timer is `<= 240`. Recommended values include `change-freeze-window`, `cross-time-zone-review`, `regulated-release-window`, `release-train-coordination`, `security-incident-response`, and `on-call-capacity-constraint`. |
| `approvalWaitMaxSeconds` | `number` | Yes | Maximum wall-clock time an official run may remain waiting for baseline approval while holding the shared `release/<project-key>` concurrency slot. The external monitor from §7.6 cancels and annotates runs that exceed this bound. The configured value must leave at least one full monitor poll interval of approval action time after `baselineWaitTimerMinutes` elapses. |
| `approvalToLiveLockMaxDelaySeconds` | `number` | Yes | Maximum tolerated wall-clock delay from `postApprovalValidatedAt` through the final stabilized `create-live-lock` revalidation. Must cover approved pre-mutation build/test/package work, runner/job-start jitter inside the same official run, and the bounded live-lock stabilization / retry allowance from §4.8; it is not a substitute for cross-run queue policy or an approximation of a hidden platform `approved_at` timestamp. |
| `approvalToLiveLockDelayJustification` | `string \| null` | Yes | Machine-readable kebab-case justification required when `approvalToLiveLockMaxDelaySeconds > 900`; otherwise `null`. |
| `readinessEvidenceRef` | `string` | Yes | Non-empty reviewed repository-relative path or durable locator naming the project’s authoritative readiness record. That record carries the measured approval-delay evidence, any sub-10-sample waiver for §4.1.1, the normal or exceptional `approvalWaitMaxSeconds` justification when smaller than the recommended `+1800` buffer, any `approvalToLiveLockMaxDelayJustification` support package, the latest required exercise evidence, and any temporary monitor-bootstrap exception allowed by §4.1.2. |
| `protectedRefs` | `object` | Yes | Closed object containing `officialTagPattern`, `buddyTagPattern`, and `liveLockRef`. |
| `actors` | `object` | Yes | Closed object containing `refWriterActorClass`, `artifactStoreMarkerWriterActorClass`, `githubReleasePublisherActorClass`, `buddyGithubReleasePublisherActorClass`, and `breakGlassActorClass`. |
| `artifactStore` | `object` | Yes | Closed object using the §4.10 durable-store discriminated-union schema below. |
| `buddyTargetAuthContracts` | `object` | Yes | Closed object keyed by buddy targets only. Each value uses the §5.11 closed target-auth schema. |
| `officialTargetAuthContracts` | `object` | Yes | Closed object keyed by official targets only. Each value uses the §5.11 closed target-auth schema. |
| `officialTargetConfirmationPolicies` | `object` | Yes | Closed object keyed by official targets only. Each value uses the §5.12 closed confirmation-policy schema. These operational settings are not copied into `release-plan`. |
| `breakGlass` | `object` | Yes | Closed object naming the required two-person execution mechanism, the mandatory split-control custody path, the checked-in runbook reference, and the incident-record requirements from §7.5. |

`artifactStore` is a closed discriminated union keyed by `backendClass`.

`branchScopeKey` is the deterministic branch-scope suffix derived from the current branch snapshot's `release.json`: it is `main` when `officialBranchMode = main`, and `rl-<releaseLine>` when `officialBranchMode = release-line`. For official target environments, fixed names are allowed only for targets that do not use `workflow-only` ref enforcement. When the selected target uses `providerRefClaimMode = workflow-only`, both `requiredEnvironment` and `providerEnvironment` must exactly equal `production-<surface>-<project-key>-<branchScopeKey>` on that branch snapshot.

Common required fields for every backend:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `backendClass` | `string` | Yes | Closed set `{oci-registry, azure-blob-storage, github-packages}`. |
| `bundleFormatVersion` | `number` | Yes | Must equal `1`. |
| `writeEnvironment` | `string` | Yes | Exact `production-evidence-write-<project-key>` environment name. |
| `readCredentialScope` | `string` | Yes | Non-empty machine-readable read-only credential scope name. |
| `blockedRetentionDays` | `number` | Yes | Integer `>= 365`. |
| `successfulRetentionDays` | `number` | Yes | Integer `>= 730`. |

Backend-specific required fields:

| `backendClass` value | Additional required fields |
| --- | --- |
| `oci-registry` | `repository: string` — exact immutable OCI repository used for bundles; `commitMarkerTagPrefix: string` — prepended to the 64-hex `planDigest` suffix when forming the authoritative commit-marker Git tag ref `refs/tags/<commitMarkerTagPrefix><planDigest-hex>` |
| `azure-blob-storage` | `accountUrl: string`, `container: string`, `blobPrefix: string`, `commitMarkerBlobPrefix: string` |
| `github-packages` | `packageType: string` with closed set `{container}`, `packageName: string`, `commitMarkerTagPrefix: string` (prepended to the 64-hex `planDigest` suffix when forming the authoritative commit-marker Git tag ref `refs/tags/<commitMarkerTagPrefix><planDigest-hex>`) |

`officialJobTimeoutMinutes` is a closed object with only these keys:

| Key | Type | Default source |
| --- | --- | --- |
| `preflight-validate` | positive integer minutes | §4.4 timeout table |
| `static-analysis` | positive integer minutes | §4.4 timeout table |
| `official-review-surface` | positive integer minutes | §4.4 timeout table |
| `baseline-approval-and-audit` | positive integer minutes | §4.4 timeout table |
| `build-test-package-preparation` | positive integer minutes | §4.4 timeout table |
| `attestation-verification` | positive integer minutes | §4.4 timeout table |
| `create-live-lock` | positive integer minutes | §4.4 timeout table |
| `require-provenance` | positive integer minutes | §4.4 timeout table |
| `create-release-tag` | positive integer minutes | §4.4 timeout table |
| `publish-github-release` | positive integer minutes | §4.4 timeout table |
| `publish-npm-official` | positive integer minutes | §4.4 timeout table |
| `publish-pypi-official` | positive integer minutes | §4.4 timeout table |
| `publish-rubygems-official` | positive integer minutes | §4.4 timeout table |
| `publish-nuget-official` | positive integer minutes | §4.4 timeout table |
| `confirm-publish-state` | positive integer minutes | §4.4 timeout table |
| `release-complete` | positive integer minutes | §4.4 timeout table |

Missing keys use the documented defaults. Unknown keys are hard failures.

`breakGlass` is a closed object with exactly these fields:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `executionMechanism` | `string` | Yes | Closed set `{github-environment-single-approval-plus-offline-split-control}`. The GitHub environment portion is an additional in-platform gate only; the actual two-person control comes from the mandatory offline split-control path. |
| `incidentTicketRequired` | `boolean` | Yes | Must be `true`. |
| `actorClassRef` | `string` | Yes | Must exactly equal `actors.breakGlassActorClass`. |
| `runbookRef` | `string` | Yes | Exact reviewed repository-relative path or approved URL for the break-glass and cleanup runbook index used for this project. |
| `offlineCustodyMechanism` | `string` | Yes | Closed set `{sealed-secret-split-control, hsm-split-control, password-manager-split-control}` describing the out-of-band fallback path used when GitHub control-plane approval or workflow execution is unavailable. |
| `offlineControlledMaterial` | `string` | Yes | Non-empty machine-readable name of the exact secret/key package placed under split control, such as the break-glass GitHub App private key, broker signing key, or encrypted recovery package. |
| `offlineCustodians` | `string[]` | Yes | Closed non-empty list of named repository administrators or security contacts who jointly control the out-of-band path; at least two distinct custodians are required. |
| `offlineEvidenceRequirements` | `string[]` | Yes | Closed non-empty subset of `{incident-ticket, control-plane-outage-evidence, requested-action, before-after-state, operator-identity, approver-identity}`. |

`prTrustModel` is a closed object with exactly these fields:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `untrustedPullRequestEvent` | `string` | Yes | Must equal `pull_request`. |
| `allowPullRequestTargetMetadataOnly` | `boolean` | Yes | Records whether metadata-only `pull_request_target` usage is allowed. |
| `forkSecretsEnabled` | `boolean` | Yes | Must be `false`. |
| `forkPrivilegedWriteTokensEnabled` | `boolean` | Yes | Must be `false`. |
| `bootstrapCodeOwnerReviewRequired` | `boolean` | Yes | Must be `true`; records that the bootstrap-governance surface uses a dedicated CODEOWNERS or equivalent special-review path. |
| `bootstrapTrustedFilesSha256` | `string` | Yes | `sha256:<64 lowercase hex>` over the canonical bootstrap-governance manifest `(path, sha256)` list consumed by `ci.yml`, using the placeholder-normalized `.github/repository-release-contract.json` digest rule from §2.1. |

Example shape:

```json
{
  "schemaVersion": 1,
  "projects": {
    "example-project": {
      "projectPath": "src/example-project",
      "assuranceProfile": "standard",
      "releaseEnabled": true,
      "buddyAuthorizedRefs": [
        "refs/heads/main",
        "refs/heads/release/example-project/v1.2"
      ],
      "buddyEnvironments": {
        "github:release": "buddy-github-example-project",
        "npm:gpr": "buddy-npm-example-project"
      },
      "officialEnvironments": {
        "baseline": "production-example-project",
        "refWrite": "production-ref-write-example-project",
        "evidenceWrite": "production-evidence-write-example-project",
        "targets": {
          "github:release": "production-github-example-project",
          "npm:official": "production-npm-example-project-rl-1.2"
        }
      },
        "officialJobTimeoutMinutes": {
          "baseline-approval-and-audit": 110,
          "publish-github-release": 15,
          "confirm-publish-state": 24
        },
      "baselineWaitTimerMinutes": 60,
      "baselineWaitTimerJustification": null,
      "approvalWaitMaxSeconds": 5400,
      "approvalToLiveLockMaxDelaySeconds": 1800,
      "approvalToLiveLockDelayJustification": "large-windows-build",
      "readinessEvidenceRef": ".github/release-readiness/example-project.json",
      "protectedRefs": {
        "officialTagPattern": "refs/tags/release/example-project/v*",
        "buddyTagPattern": "refs/tags/buddy/example-project/v**",
        "liveLockRef": "refs/tags/official-lock/example-project"
      },
      "actors": {
        "refWriterActorClass": "official-ref-writer",
        "artifactStoreMarkerWriterActorClass": "artifact-store-marker-writer",
        "githubReleasePublisherActorClass": "github-release-publisher",
        "buddyGithubReleasePublisherActorClass": "buddy-github-release-publisher",
        "breakGlassActorClass": "release-break-glass"
      },
        "artifactStore": {
          "backendClass": "oci-registry",
          "repository": "ghcr.io/three/example-project-release-bundles",
          "commitMarkerTagPrefix": "plan-",
          "bundleFormatVersion": 1,
          "writeEnvironment": "production-evidence-write-example-project",
          "readCredentialScope": "artifact-store-readonly",
        "blockedRetentionDays": 365,
        "successfulRetentionDays": 730
      },
      "buddyTargetAuthContracts": {
        "github:release": {
          "requiredEnvironment": "buddy-github-example-project",
          "authClass": "github-app-installation-token",
          "allowedCredentialSource": "environment-gated-external-broker",
          "actorClass": "buddy-github-release-publisher",
          "providerWorkflowPath": null,
          "providerEnvironment": null,
          "providerKey": null,
          "providerTrustCapabilities": null,
          "providerRefClaimSupport": null,
          "providerSupportsReadOnlyInspection": null,
          "providerRefClaimMode": null,
          "providerRefClaimModeRationale": null,
          "providerConfigReviewedAt": null,
          "providerConfigReviewRef": null,
          "allowedRefClaims": [],
          "providerAudience": null
        },
        "npm:gpr": {
          "requiredEnvironment": "buddy-npm-example-project",
          "authClass": "github-packages-github-token",
          "allowedCredentialSource": "github-token",
          "actorClass": null,
          "providerWorkflowPath": null,
          "providerEnvironment": null,
          "providerKey": null,
          "providerTrustCapabilities": null,
          "providerRefClaimSupport": null,
          "providerSupportsReadOnlyInspection": null,
          "providerRefClaimMode": null,
          "providerRefClaimModeRationale": null,
          "providerConfigReviewedAt": null,
          "providerConfigReviewRef": null,
          "allowedRefClaims": [],
          "providerAudience": null
        }
      },
      "officialTargetAuthContracts": {
        "github:release": {
          "requiredEnvironment": "production-github-example-project",
          "authClass": "github-app-installation-token",
          "allowedCredentialSource": "environment-gated-external-broker",
          "actorClass": "github-release-publisher",
          "providerWorkflowPath": null,
          "providerEnvironment": null,
          "providerKey": null,
          "providerTrustCapabilities": null,
          "providerRefClaimSupport": null,
          "providerSupportsReadOnlyInspection": null,
          "providerRefClaimMode": null,
          "providerRefClaimModeRationale": null,
          "providerConfigReviewedAt": null,
          "providerConfigReviewRef": null,
          "allowedRefClaims": [],
          "providerAudience": null
        },
        "npm:official": {
          "requiredEnvironment": "production-npm-example-project-rl-1.2",
          "authClass": "external-registry-oidc-trusted-publishing",
          "allowedCredentialSource": "github-oidc",
          "actorClass": null,
          "providerWorkflowPath": ".github/workflows/official.yml",
          "providerEnvironment": "production-npm-example-project-rl-1.2",
          "providerKey": "npmjs",
          "providerTrustCapabilities": ["environment", "repository", "workflow-path"],
          "providerRefClaimSupport": "unsupported",
          "providerSupportsReadOnlyInspection": false,
          "providerRefClaimMode": "workflow-only",
          "providerRefClaimModeRationale": "provider-does-not-support-exact-ref-claims",
          "providerConfigReviewedAt": "2026-02-15T00:00:00Z",
          "providerConfigReviewRef": {
            "kind": "api-snapshot",
            "locator": "artifact://provider-reviews/npmjs/example-project/2026-02-15.json",
            "evidenceSha256": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
          },
          "allowedRefClaims": ["refs/heads/release/example-project/v1.2"],
          "providerAudience": "npm:registry.npmjs.org"
        }
      },
      "officialTargetConfirmationPolicies": {
        "github:release": {
          "confirmIntervalSeconds": 10,
          "confirmMaxAttempts": 3,
          "perAttemptBudgetSeconds": 10,
          "providerDelayBudgetSeconds": 0,
          "confirmTimeoutSeconds": 110
        },
        "npm:official": {
          "confirmIntervalSeconds": 30,
          "confirmMaxAttempts": 5,
          "perAttemptBudgetSeconds": 15,
          "providerDelayBudgetSeconds": 300,
          "confirmTimeoutSeconds": 1020
        }
      },
      "breakGlass": {
        "executionMechanism": "github-environment-single-approval-plus-offline-split-control",
        "incidentTicketRequired": true,
        "actorClassRef": "release-break-glass",
        "runbookRef": "docs/runbooks/release-break-glass.md",
        "offlineCustodyMechanism": "hsm-split-control",
        "offlineControlledMaterial": "break-glass-github-app-private-key",
        "offlineCustodians": ["repo-admin-oncall", "security-oncall"],
        "offlineEvidenceRequirements": [
          "incident-ticket",
          "control-plane-outage-evidence",
          "requested-action",
          "before-after-state",
          "operator-identity",
          "approver-identity"
        ]
      }
    }
  },
  "prTrustModel": {
    "untrustedPullRequestEvent": "pull_request",
    "allowPullRequestTargetMetadataOnly": true,
    "forkSecretsEnabled": false,
    "forkPrivilegedWriteTokensEnabled": false,
    "bootstrapCodeOwnerReviewRequired": true,
    "bootstrapTrustedFilesSha256": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
  }
}
```

Validation rules:

- `schemaVersion` must equal `1`.
- `projects` must be a non-empty object keyed by canonical `project-key`.
- Each `projects.<project-key>.projectPath` must be unique across the file.
- `projects.<project-key>.assuranceProfile` must be either `standard` or `high-assurance`.
- Each `projects.<project-key>.buddyAuthorizedRefs` array must contain unique values. It must be non-empty when either `buddyEnvironments` or `buddyTargetAuthContracts` is non-empty, and it must be the empty array otherwise.
- When validated together with `<project-root>/release.json` from the same branch snapshot, the buddy target subset in `release.json.targets` must exactly match `buddyEnvironments` and `buddyTargetAuthContracts`, while the official target subset must exactly match `officialEnvironments.targets`, `officialTargetAuthContracts`, and `officialTargetConfirmationPolicies`.
- Until the pending provider-review block from §4.9 / §5.11 is explicitly removed in a later design revision, `nuget:official` must not appear in `officialEnvironments.targets`, `officialTargetAuthContracts`, or `officialTargetConfirmationPolicies`.
- `protectedRefs.officialTagPattern` must exactly equal `refs/tags/release/<project-key>/v*`, `protectedRefs.buddyTagPattern` must exactly equal `refs/tags/buddy/<project-key>/v**`, and `protectedRefs.liveLockRef` must exactly equal `refs/tags/official-lock/<project-key>`.
- `buddyTargetAuthContracts` and `officialTargetAuthContracts` are separate namespaces. The same bare target name, such as `github:release`, may appear in both without collision because each channel stores an independent closed auth object.
- `officialEnvironments.targets` key sets must exactly match `officialTargetAuthContracts` and `officialTargetConfirmationPolicies`; `buddyEnvironments` and `buddyTargetAuthContracts` must use the same full buddy-target keys and those key sets must exactly match. Their values must also match exactly: `buddyEnvironments[t] == buddyTargetAuthContracts[t].requiredEnvironment` for every buddy target, `officialEnvironments.targets[t] == officialTargetAuthContracts[t].requiredEnvironment` for every official target, and `officialEnvironments.evidenceWrite == artifactStore.writeEnvironment`.
- `officialEnvironments.baseline` must equal `production-<project-key>`, `officialEnvironments.refWrite` must equal `production-ref-write-<project-key>`, and `officialEnvironments.evidenceWrite` must equal `production-evidence-write-<project-key>`.
- `officialEnvironments.targets.github:release`, when present, must equal `production-github-<project-key>`. For OIDC-backed official targets that do **not** use `workflow-only`, the required environment names are the fixed forms `production-npm-<project-key>`, `production-pypi-<project-key>`, `production-rubygems-<project-key>`, and `production-nuget-<project-key>`. For OIDC-backed official targets whose `providerRefClaimMode = workflow-only`, both `officialEnvironments.targets.<target>` and `officialTargetAuthContracts.<target>.providerEnvironment` must instead equal the deterministic branch-scoped form `production-<surface>-<project-key>-<branchScopeKey>` for that branch snapshot.
- `buddyEnvironments.github:release`, when present, must equal `buddy-github-<project-key>`; `buddyEnvironments.npm:gpr` must equal `buddy-npm-<project-key>`; `buddyEnvironments.rubygems:gpr` must equal `buddy-rubygems-<project-key>`; and `buddyEnvironments.nuget:gpr` must equal `buddy-nuget-<project-key>`.
- `artifactStore` must satisfy the closed discriminated-union schema for its selected `backendClass`.
- when `artifactStore.backendClass` is `oci-registry` or `github-packages`, `commitMarkerTagPrefix` must be non-empty, must form a repository Git tag namespace under `refs/tags/` that does not overlap `protectedRefs.officialTagPattern`, `protectedRefs.buddyTagPattern`, or `protectedRefs.liveLockRef`, and must be protected as required by §4.1.
- when `artifactStore.backendClass = azure-blob-storage`, `blobPrefix` and `commitMarkerBlobPrefix` must both be non-empty normalized prefixes, must not be equal, and neither may be a prefix of the other. Payload uploads must use a unique blob name per attempted write under `blobPrefix`; overwriting an earlier payload blob path is forbidden even when no commit-marker blob was created.
- `officialJobTimeoutMinutes` may contain only the documented formal keys, and every present value must be a positive integer minute count. Any override for `baseline-approval-and-audit` must be greater than or equal to `ceil(max(baselineWaitTimerMinutes * 60, approvalWaitMaxSeconds) / 60) + 20`. Any override for `confirm-publish-state` must be greater than or equal to `ceil(sum(selectedTargetConfirmationPolicies.confirmTimeoutSeconds) / 60) + 5` for the project’s enabled official targets.
- `breakGlass.executionMechanism` must document the split-control-plus-GitHub-gate mechanism used by §7.5.
- `breakGlass.incidentTicketRequired` must be `true`.
- `breakGlass.actorClassRef` must exactly equal `actors.breakGlassActorClass`.
- `breakGlass.runbookRef` must be a non-empty reviewed repository-relative path or approved URL reachable by the operators named in the project runbook.
- `breakGlass.offlineCustodyMechanism` and `breakGlass.offlineCustodians` must document a pre-established out-of-band split-control path usable when GitHub control-plane approval or workflow execution is degraded or unavailable.
- `breakGlass.offlineControlledMaterial` must be a non-empty kebab-case or dotted machine-readable identifier naming the exact material under split control; values such as `break-glass-github-app-private-key`, `broker-break-glass-signing-key`, or `encrypted-recovery-package` are illustrative.
- `breakGlass.offlineEvidenceRequirements` must be non-empty and must include at least `incident-ticket`, `requested-action`, `before-after-state`, `operator-identity`, and `approver-identity`.
- `baselineWaitTimerMinutes` must be an integer in the inclusive range `1..1440`.
- `baselineWaitTimerJustification` must be `null` when `baselineWaitTimerMinutes <= 240`, and must be a non-null kebab-case string matching `[a-z0-9]+(-[a-z0-9]+)*` when `baselineWaitTimerMinutes > 240`.
- `approvalWaitMaxSeconds` must be an integer in the inclusive range `300..86700`.
- `approvalWaitMaxSeconds` must be greater than or equal to `baselineWaitTimerMinutes * 60 + 300`. The extra `300` seconds is the minimum required buffer for the external monitor’s maximum 5-minute poll interval so approvers have a non-zero action window after the baseline wait timer elapses. Repositories should normally budget at least `+1800` seconds unless the reviewed readiness record referenced by `readinessEvidenceRef` justifies a smaller buffer.
- `approvalToLiveLockMaxDelaySeconds` must be an integer in the inclusive range `30..7200`.
- `approvalToLiveLockDelayJustification` must be `null` when `approvalToLiveLockMaxDelaySeconds <= 900`, and must be a non-null kebab-case string matching `[a-z0-9]+(-[a-z0-9]+)*` when `approvalToLiveLockMaxDelaySeconds > 900`. `assuranceProfile = high-assurance` must additionally keep `approvalToLiveLockMaxDelaySeconds <= 900`.
- `readinessEvidenceRef` must be a non-empty reviewed repository-relative path or durable locator, and that named readiness record is the authoritative place for the measurement owner, source window, helper output, any sub-10-sample waiver, exercise evidence, and any temporary monitor-bootstrap exception that this design permits.
- `offlineCustodians`, `offlineEvidenceRequirements`, `buddyAuthorizedRefs`, `allowedRefClaims`, and every non-null `providerTrustCapabilities` array must contain unique elements.
- `actors.refWriterActorClass`, `actors.artifactStoreMarkerWriterActorClass`, `actors.githubReleasePublisherActorClass`, `actors.buddyGithubReleasePublisherActorClass`, and `actors.breakGlassActorClass` must each be non-empty machine-readable identifiers, and the ref writer, artifact-store marker writer, official GitHub Release publisher, and buddy GitHub Release publisher must all be pairwise distinct; the protected-ref writer, commit-marker writer, official GitHub Release publisher, and buddy GitHub Release publisher are separate actor classes by design. For the two `github:release` paths, those pairwise-distinct actor classes must also map to distinct GitHub App identities in the broker policy and key-custody records; one shared GitHub App with multiple environment paths is forbidden.
- `officialTargetAuthContracts.github:release`, when present, must use `allowedCredentialSource = environment-gated-external-broker`; storing the long-lived GitHub App key directly in a branch-scoped official subordinate environment is not part of the normal target design.
- `officialTargetAuthContracts.github:release`, when present, must use `actorClass = actors.githubReleasePublisherActorClass`.
- `buddyTargetAuthContracts.github:release`, when present, must also use `allowedCredentialSource = environment-gated-external-broker`, and its `actorClass` must exactly equal `actors.buddyGithubReleasePublisherActorClass`; the normal design does not allow a branch-scoped buddy environment to hold the long-lived GitHub App private key, and it must not reuse the same GitHub App identity as `officialTargetAuthContracts.github:release`.
- `officialTargetAuthContracts.github:release`, when present, must use `authClass = github-app-installation-token`; `officialTargetAuthContracts.{npm:official,pypi:official,rubygems:official,nuget:official}`, when present, must use `authClass = external-registry-oidc-trusted-publishing`; `buddyTargetAuthContracts.github:release` must use `authClass = github-app-installation-token`; and `buddyTargetAuthContracts.{nuget:gpr,npm:gpr,rubygems:gpr}` must use `authClass = github-packages-github-token`.
- `prTrustModel.bootstrapCodeOwnerReviewRequired` must be `true`, and `prTrustModel.bootstrapTrustedFilesSha256` must match `sha256:<64 lowercase hex>`.
- `prTrustModel` must reject any configuration that would expose fork PRs to secrets or privileged write tokens.
- Any PR that changes provider-side trust inputs (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, `providerRefClaimModeRationale`, `allowedRefClaims`, or `providerAudience`) for an official target must update the corresponding `providerConfigReviewedAt` and `providerConfigReviewRef` fields in the same change set.
- Example and default confirmation budgets should leave timing slack above the computed minimum. This design uses a `+10` second recommendation for boundary-sized targets such as `github:release` and `npm:gpr` rather than configuring those examples exactly at the formula floor.
- Unknown fields at the top level, in any project entry, or in `prTrustModel` are hard failures.

### 5.10 Canonical `release-plan` schema

`release-plan` is a closed object. It contains exactly these fields and no others:

| Field | Type | Notes |
| --- | --- | --- |
| `schemaVersion` | `number` | Must equal `1`. Version of the frozen `release-plan` schema itself; independent from the admission-file `schemaVersion`. |
| `planDigest` | `string` | `sha256:<64 lowercase hex>` computed from the canonical serialization of the entire `release-plan` object except `planDigest` itself, after placeholder-normalizing every `targetAuthContracts.*.providerConfigReviewedAt` / `providerConfigReviewRef` freshness-only field pair exactly as defined in §7.1. `schemaVersion` is included in that digest. |
| `projectKey` | `string` | Canonical checked-in project key. |
| `projectPath` | `string` | Canonical project root from the repository contract. |
| `packageIdentity` | `string` | Exact external package identity. |
| `packageManifestPath` | `string` | Exact repo-relative manifest/project file path. |
| `ecosystem` | `string` | Closed set `{csharp, python, jsts, ruby}` derived solely from `buildKind` by the authoritative §5.5 mapping. |
| `buildKind` | `string` | Closed set `{csharp-pack, python-package, node-npm, node-wxt, ruby-gem}`. |
| `version` | `string` | Canonical validated release version. |
| `releaseLine` | `string \| null` | `null` when the frozen plan authorizes from `main`; exact checked-in release-line string when it authorizes from `release/<project-key>/v<releaseLine>`. |
| `authorizedBranch` | `string` | Exact protected branch ref that may authorize the frozen plan. |
| `officialTag` | `string` | Exact full tag ref `refs/tags/release/<project-key>/v<version>`. |
| `environmentBindings` | `object` | Closed object containing exactly `baseline`, `refWrite`, `evidenceWrite`, and `targets`, freezing the checked-in environment contract needed by this plan. `targets` is keyed exactly by `targets`. |
| `artifactStoreBinding` | `object` | Closed object freezing the exact durable artifact-store contract selected for this plan, using the same schema as `artifactStore` in §5.9. |
| `payloadSha` | `string` | 40-character lowercase git commit SHA. |
| `artifacts` | `object` | Closed artifact catalog filtered from `release.json` to exactly the aliases referenced by the selected channel’s `targetArtifacts`; each key is an artifact alias and each value is a closed object containing exactly `kind`. Buddy-only aliases must not appear in an official frozen plan. |
| `targets` | `string[]` | Lexicographically sorted exact target list. |
| `targetArtifacts` | `object` | Object keyed exactly by `targets`; each value is a lexicographically sorted array of artifact aliases. |
| `targetAuthContracts` | `object` | Object keyed exactly by `targets`; each value is the closed target-auth object from §5.11 for the selected channel only. The frozen copy carries the dispatch-time auth contract for audit, but `planDigest` placeholder-normalizes `providerConfigReviewedAt` and `providerConfigReviewRef`, and recovery-time freshness checks also treat those two fields as operational evidence fields rather than frozen equality fields. |
| `npmAccessHint` | `string \| null` | `public`, `restricted`, or `null` when not applicable. |

Validation rules:

- `release-plan.schemaVersion` must equal `1`.
- `release-plan` remains a closed object; `lockInstanceToken` and other live-lock-instance fields are intentionally excluded and persist separately in blocked-entry `lockIdentity`.
- `release-plan.artifacts` key set must exactly equal the union of every artifact alias named anywhere in `release-plan.targetArtifacts`. Extra aliases and omitted aliases are both hard failures.
- `release-plan.environmentBindings.baseline`, `.refWrite`, and `.evidenceWrite` must exactly equal the checked-in contract values that authorized the run, and `release-plan.environmentBindings.targets` key/value pairs must exactly match the selected target set and required target environments.
- `release-plan.artifactStoreBinding` must be the exact frozen durable-store contract that the plan is authorized to use for write, read-back, restore, and confirmation evidence; blocked recovery must not silently substitute a later branch version of that contract.

### 5.11 Canonical `targetAuthContracts` schema

Each `targetAuthContracts.<target>` entry is also a closed object. It contains exactly these fields and no others:

| Field | Type | Notes |
| --- | --- | --- |
| `requiredEnvironment` | `string` | Exact subordinate environment name used by the target. |
| `authClass` | `string` | Closed set `{github-app-installation-token, github-packages-github-token, external-registry-oidc-trusted-publishing}`. |
| `allowedCredentialSource` | `string` | Closed set `{environment-gated-external-broker, github-token, github-oidc}`. |
| `actorClass` | `string \| null` | Required for GitHub App actor-based auth, otherwise `null`. |
| `providerWorkflowPath` | `string \| null` | Exact workflow path expected by the provider-side trust configuration when applicable, otherwise `null`. |
| `providerEnvironment` | `string \| null` | For OIDC-backed targets, the non-empty exact environment name bound to the publish job; otherwise `null`. |
| `providerKey` | `string \| null` | For OIDC-backed targets, exact external provider identifier from the closed set `{npmjs, nuget.org, pypi, rubygems.org}`; otherwise `null`. |
| `providerTrustCapabilities` | `string[] \| null` | For OIDC-backed targets, lexicographically sorted closed subset of `{repository, workflow-path, environment, ref}` describing provider-side claim enforcement; `null` for GitHub-native targets. |
| `providerRefClaimSupport` | `string \| null` | For OIDC-backed targets, closed set `{supported, unsupported, unknown}` recording whether this provider/target pair can enforce exact ref claims; otherwise `null`. |
| `providerSupportsReadOnlyInspection` | `boolean \| null` | For OIDC-backed targets, records whether the provider exposes a documented read-only drift-inspection path; otherwise `null`. |
| `providerRefClaimMode` | `string \| null` | For OIDC-backed targets, closed set `{provider-enforced, workflow-only}`. `provider-enforced` is the preferred/higher-assurance mode; `workflow-only` is the reviewed lower-assurance exception mode that relies on repository-side compensating controls. `null` for GitHub-native targets. |
| `providerRefClaimModeRationale` | `string \| null` | Required closed set `{provider-does-not-support-exact-ref-claims, provider-ref-claims-not-available-for-this-target, provider-ref-claims-cannot-pin-required-branch-shape, provider-support-status-unknown}` when `providerRefClaimMode = workflow-only`; otherwise `null`. |
| `providerConfigReviewedAt` | `string \| null` | For OIDC-backed targets, RFC 3339 UTC timestamp of the most recent repository-reviewed provider-side trust-configuration verification; otherwise `null`. The authoritative validation clock is the validating workflow runner's current UTC time. It must never be later than that current UTC value and must never be older than 365 days; some targets use stricter freshness rules. This is operational freshness evidence, not a release-identity equality field for blocked recovery. |
| `providerConfigReviewRef` | `object \| null` | For OIDC-backed targets, closed machine-readable evidence object for that provider-side trust review; otherwise `null`. It contains exactly `kind`, `locator`, and `evidenceSha256`. This is operational freshness evidence, not a release-identity equality field for blocked recovery. Its referenced evidence must still assert the same normalized trust tuple recorded in the checked-in contract rather than merely linking to some raw screenshot or payload. |
| `allowedRefClaims` | `string[]` | Exact workflow-enforced ref claims; lexicographically sorted; wildcard patterns are forbidden. Empty only for non-OIDC GitHub-native targets. |
| `providerAudience` | `string \| null` | Exact OIDC audience when applicable, otherwise `null`. |

`providerConfigReviewRef`, when non-null, is a closed object with exactly these fields:

| Field | Type | Notes |
| --- | --- | --- |
| `kind` | `string` | Closed set `{api-snapshot, reviewed-console-export, signed-review-record}`. |
| `locator` | `string` | Non-empty durable locator for the machine-readable provider review evidence. The locator must remain readable for the lifetime of the reviewed target configuration and any blocked entry that still depends on that configuration. |
| `evidenceSha256` | `string` | Canonical `sha256:<64 lowercase hex>` digest of the evidence bytes referenced by `locator`. A locator whose fetched bytes do not match this digest is invalid. |

When `providerConfigReviewRef.kind = api-snapshot`, the referenced evidence must itself record at minimum the exact first-party source reviewed, the retrieval or capture timestamp, the normalized audience/trust-shape conclusion, the normalized trust tuple it supports (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerAudience`, `providerRefClaimMode`, `providerTrustCapabilities`, and `allowedRefClaims`), and the raw or normalized machine-readable payload used for that review. Evidence that omits the reviewed conclusion tuple, or whose conclusion no longer matches the checked-in contract even though the bytes are still readable, is semantically invalid.

Class-specific validation rules:

- `authClass = github-app-installation-token` is for GitHub API publication such as `github:release`. It requires `allowedCredentialSource = environment-gated-external-broker`, a non-null `actorClass`, empty `allowedRefClaims`, and `null` for every provider-side field.
- `authClass = github-packages-github-token` is allowed only for buddy GitHub Packages targets `{nuget:gpr, npm:gpr, rubygems:gpr}`. It requires `allowedCredentialSource = github-token`, `actorClass = null`, empty `allowedRefClaims`, and `null` for every provider-side field.
- `authClass = external-registry-oidc-trusted-publishing` requires `allowedCredentialSource = github-oidc`, `actorClass = null`, non-empty `providerWorkflowPath`, non-empty `providerEnvironment`, non-empty `providerKey`, non-empty `providerTrustCapabilities`, non-empty `allowedRefClaims`, non-empty `providerAudience`, non-empty `providerRefClaimSupport`, a non-null read-only-inspection support flag, non-empty `providerConfigReviewedAt`, and non-null `providerConfigReviewRef`. Provider-specific checked-in defaults are part of this schema contract where current first-party documentation is stable: `providerKey = npmjs` must use `providerAudience = npm:registry.npmjs.org`; `providerKey = pypi` must use `providerAudience = pypi`; `providerKey = rubygems.org` must use `providerAudience = rubygems.org`. `providerKey = nuget.org` is reserved but configuration-invalid in this revision because the repository still lacks one approved closed audience contract for `nuget:official`; `api://NuGet` is not an approved default.

For OIDC-backed targets, `allowedRefClaims` must be non-empty and must exactly enumerate the workflow-authorized branch refs for the project’s active release lines. `preflight-validate` always enforces those refs in-workflow. `providerTrustCapabilities` must always include `repository` and `workflow-path`; if it includes `environment`, the value must match the checked-in `providerEnvironment`; if `providerRefClaimMode = provider-enforced`, it must also include `ref` and `providerRefClaimSupport` must be `supported`; if `providerRefClaimMode = workflow-only`, then `providerRefClaimSupport` must be `unsupported` or `unknown`, `providerRefClaimModeRationale` must be non-null, the capability set must include `environment` and therefore contain at least `{repository, workflow-path, environment}`, the capability set must **not** include `ref`, and the branch snapshot enabling that target must use exactly one `allowedRefClaims` entry together with a branch-scoped `providerEnvironment`/`requiredEnvironment` whose deployment policy admits only that same branch. `workflow-only` is therefore an explicitly lower-assurance exception mode rather than a peer alternative to provider-enforced exact-ref binding. For `workflow-only`, those environment names must use the deterministic `production-<surface>-<project-key>-<branchScopeKey>` form from §5.9; a fixed per-project target environment name is invalid in that mode. `providerConfigReviewedAt` and `providerConfigReviewRef` record the last repository-reviewed verification of the provider-side trust configuration. The authoritative clock for both `ci.yml` and release-time freshness checks is the validating workflow runner's current UTC time, not an author workstation clock. Every non-null `providerConfigReviewedAt` must therefore be `<= now()` and no older than 365 days. When the relevant evidence surface is available, `ci.yml`, `preflight-validate`, and the §7.6 provider-freshness monitor must also verify that `providerConfigReviewRef.locator` remains readable and that its bytes still hash to `evidenceSha256`; missing or mismatched evidence is a hard failure, not a soft warning. If `providerSupportsReadOnlyInspection = false`, `baseline-approval-and-audit` cannot independently prove provider-side drift at runtime; it must instead verify that the **current checked-in** review fields are present, that the recorded review is not older than 7 days for `standard` projects or 24 hours for `high-assurance` projects when `providerRefClaimMode = workflow-only`, that it is not future-dated, that the checked-in contract still exactly matches the workflow path, environment, audience, and allowed refs being used by the run, and that the latest external drift-probe result is still within the expected cadence. Refreshing `providerConfigReviewedAt` is therefore evidence-recency tracking, not proof that provider configuration did not drift; the review update must carry the independent evidence referenced by `providerConfigReviewRef`. For `providerKey = pypi`, Day 0 enablement must still re-confirm the checked-in `pypi` audience against then-current first-party PyPI documentation. For `providerKey = nuget.org`, no checked-in configuration is valid in this revision: `nuget:official` remains `BLOCKED: pending-provider-review` until a later reviewed design revision approves one exact audience contract and removes the block. For blocked recovery, `providerConfigReviewedAt` and `providerConfigReviewRef` may be refreshed on the authoritative branch without changing the frozen plan or `planDigest` so long as every other field in the current checked-in `targetAuthContracts` entry still exactly matches the frozen entry. §7.6 requires assurance-sensitive pre-expiry alerting tied to the actual limit in force: 30 days / 7 days before the 365-day outer bound, 48 hours / 24 hours before the 7-day `standard workflow-only` bound, and 8 hours / 2 hours before the 24-hour `high-assurance workflow-only` bound. It also requires repository-owned best-effort provider-drift probes for `workflow-only` targets that record one closed outcome from `{match, drift-detected, inspection-unsupported, inspection-unavailable, inspection-error}`; those probes must run at least hourly for `high-assurance` projects and at least daily for `standard` projects. For GitHub-native targets that do not use provider-side OIDC trust, `allowedRefClaims` is the empty array, `providerRefClaimMode` is `null`, and provider-specific fields are `null`. Because implementation has not started, the checked-in audience value remains part of the reviewed contract immediately; however, only the stable reviewed defaults above may be assumed without extra provider evidence, and `nuget:official` is explicitly excluded from that set until the provider-review block is lifted. If later provider documentation changes, the repository must update the checked-in contract and this design together before enablement.

### 5.12 Canonical `officialTargetConfirmationPolicies` schema

Each `officialTargetConfirmationPolicies.<target>` entry is also a closed object. It contains exactly these fields and no others:

| Field | Type | Notes |
| --- | --- | --- |
| `confirmMaxAttempts` | `integer` | Integer in the inclusive range `1..8`. |
| `confirmIntervalSeconds` | `integer` | Integer `>= 1`. |
| `perAttemptBudgetSeconds` | `integer` | Integer `>= 1`; per-attempt allowance for API latency, token issuance, and response parsing. |
| `providerDelayBudgetSeconds` | `integer` | Integer `>= 0`; cumulative wall-clock allowance reserved for provider-mandated waits such as `Retry-After` across the whole target-confirmation loop. Use `0` only when the target has no such provider-managed delay path in the reviewed confirmation surface. |
| `confirmTimeoutSeconds` | `integer` | Integer `>= 1` and `>= confirmIntervalSeconds * (2^(confirmMaxAttempts - 1) - 1) + confirmMaxAttempts * perAttemptBudgetSeconds + providerDelayBudgetSeconds` under the exact truncated-exponential full-jitter retry model from §4.4. Repository guidance is to add at least 10 seconds of slack above that computed minimum. |

Validation rules:

- `officialTargetConfirmationPolicies` key sets must exactly match `officialTargetAuthContracts` for the same project.
- These confirmation policies are operational controls, not release-identity fields. `preflight-validate` must validate and emit them for the current run, but they are intentionally excluded from `release-plan`, blocked-entry `frozenPlan`, and `planDigest` so operators may tune confirmation behavior without burning the frozen version.
- `ci.yml` must statically validate the same `confirmTimeoutSeconds` inequality during PR validation so invalid retry budgets fail before merge rather than during a release run.
- Retryable visibility/rate-limit/provider faults use the exact retry model from §4.4: attempt `1` is immediate, each later gap sleeps a jittered duration within that gap’s exponential ceiling, and deterministic conflicts are terminal.

### 5.13 Schema evolution policy

- All checked-in release-control JSON surfaces in this design are closed schemas. In v1, adding a field is a breaking change, not a silent compatible extension.
- `schemaVersion` is repository-policy versioning, not a consumer-negotiated API. The workflows must hard-fail on any version they do not explicitly implement.
- Because implementation has not started, there is no backward-compatibility requirement. A schema change must migrate every affected checked-in file in one reviewed change set, and `ci.yml` must reject mixed-version repository states.
- A future `schemaVersion` increment is required for any field addition, removal, rename, type change, enum-set change, or semantic reinterpretation across `release.json`, `.github/repository-release-contract.json`, `.github/official-admission-state/<project-key>.json`, `release-plan`, `targetAuthContracts`, or `officialTargetConfirmationPolicies`.
- The repository-wide `schemaVersion` remains mandatory for identity-bearing and authorization-bearing surfaces. However, this design now explicitly allows future reviewed subordinate `structureVersion` evolution for non-authoritative diagnostic or evidence helper outputs when—and only when—the affected structure is not part of `planDigest`, blocked-stage selection, target-auth equality, or bootstrap integrity. Any such exception must be called out explicitly in this document rather than inferred from silence.
- In repositories with multiple protected release branches, schema migration is an operational procedure, not just a file edit. The runbook must define the supported branch order (normally `main` first, then maintenance branches), a temporary release freeze window while mixed-schema branches are being updated, the conversion procedure for already-blocked admission entries, the rollback procedure if migration tooling fails, and the rule that any recovery needed during the freeze must first restore all relevant branches to one consistent schema version.
- The schema-migration runbook must define an explicit freeze-window upper bound not exceeding 24 hours. If migration cannot complete within that bound, operators must either roll back to the last consistent schema version or declare a management-visible incident that keeps release traffic frozen under one named owner.
- The schema-migration runbook must include one concrete coordination checklist and example timeline. Minimum checklist items are: identify every protected branch affected; name one release owner per branch; announce the freeze window and rollback owner; pause new `official.yml` dispatches; confirm that no official run is still active on any affected branch; use `eng/scripts/release-status` or an equivalently reviewed repository-owned report to prove that every affected project is either `ready` with no live lock or already `blocked` with an explicit reviewed conversion plan for its current entry; confirm that no live lock remains active on any affected project before the freeze actually starts; pause or withdraw every in-flight recovery-authorization PR during the freeze; convert any already-blocked admission entries before reopening release traffic; verify `ci.yml` schema validation on each branch after migration; and record the exact condition for lifting the freeze.
- The example migration timeline must at minimum show: `T-5d` branch-owner notice and branch inventory; `T-1d` freeze reminder plus reviewed migration PR preparation; `T0` freeze starts only after active official runs are drained and live locks are verified absent, with any already-blocked entries either pre-converted or explicitly deferred under the reviewed conversion procedure, before `main` migrates first; `T0+n` each maintenance branch migrates in documented order; `T0+verify` all branches pass schema validation and blocked-entry conversions; `T0+lift` release freeze ends only after every protected branch is back on one schema version. During the freeze there are only two legal paths for a blocked release: re-submit the recovery authorization after every affected branch has been migrated to the new schema, or fully roll back the migration and execute recovery on the restored old schema. Mixing an old-schema recovery PR with an in-progress schema migration is forbidden.
- A schema-version bump must ship with: updated examples in this design document, updated duplicate-key-rejecting validators in workflow code, reviewed migration tooling under `eng/scripts/`, and explicit operator runbook steps for updating multi-project repositories atomically.

## 6. Checked-in Admission and Recovery State

Official admission uses bounded checked-in state plus one bounded live lock per project instead of historical workflow-run scanning.

### 6.1 File and live lock

- `.github/official-admission-state/<project-key>.json`
- protected live lock tag `refs/tags/official-lock/<project-key>` as an annotated tag whose annotation payload carries the frozen lock identity

### 6.2 Purpose

The checked-in file for the selected `project-key` on the selected protected official release branch is the authoritative reviewed admission and recovery ledger for that project and that `official.yml` run. `preflight-validate` reads its frozen snapshot at `policy-sha`.

The live lock is the immediate durable blocker for a project and must exist before the first irreversible external mutation of an official run, including GitHub-hosted attestation creation and any durable artifact-store write. Blocked admission entries must persist the reviewed `lockIdentity` separately from `frozenPlan` so recovery can validate the original live-lock instance even though `release-plan` remains a closed publication schema. Together the per-project checked-in file and the live lock record whether that project is currently release-eligible or blocked because of an unresolved release-state or control-plane issue.

### 6.3 Example shapes

#### 6.3.1 Initial ready-state example

```json
{
  "schemaVersion": 1,
  "projectKey": "example-project",
  "status": "ready",
  "updatedAt": "2026-03-01T00:00:00Z"
}
```

Before any official release is dispatched for a project from a protected branch, that branch must already contain the exact minimal `ready` entry for `.github/official-admission-state/<project-key>.json`. Project enablement is incomplete until that file exists on every protected branch that may authorize official releases for that project.

#### 6.3.2 Blocked-state example

```json
{
  "schemaVersion": 1,
  "projectKey": "blocked-project",
  "status": "blocked",
  "blockedStage": "post-confirmation",
  "entryVersion": 4,
  "digestChangeReason": null,
  "riskFlags": [],
  "targetResults": {
    "github:release": {
      "state": "confirmed",
      "evidenceRef": "artifact://official-confirmation/github-release/blocked-project/sha256-aaaa.json"
    },
    "npm:official": {
      "state": "confirmed",
      "evidenceRef": "artifact://official-confirmation/npm-official/blocked-project/sha256-bbbb.json"
    }
  },
  "lockIdentity": {
    "planDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "lockInstanceToken": "9f4c9f1b-8b44-4e2e-8d2f-4fd7a9ec0d1a",
    "lockRef": "refs/tags/official-lock/blocked-project",
    "lockedAt": "2026-03-01T00:00:00Z",
    "runId": 1234567890,
    "runAttempt": 1
  },
  "frozenPlan": {
    "schemaVersion": 1,
    "planDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "projectKey": "blocked-project",
    "projectPath": "src/blocked-project",
    "packageIdentity": "@three/blocked-project",
    "packageManifestPath": "src/blocked-project/package.json",
    "ecosystem": "jsts",
    "buildKind": "node-npm",
    "version": "1.2.3",
    "releaseLine": "1.2",
    "authorizedBranch": "refs/heads/release/blocked-project/v1.2",
    "officialTag": "refs/tags/release/blocked-project/v1.2.3",
    "environmentBindings": {
      "baseline": "production-blocked-project",
      "refWrite": "production-ref-write-blocked-project",
      "evidenceWrite": "production-evidence-write-blocked-project",
      "targets": {
        "github:release": "production-github-blocked-project",
        "npm:official": "production-npm-blocked-project-rl-1.2"
      }
    },
    "artifactStoreBinding": {
      "backendClass": "oci-registry",
      "repository": "ghcr.io/three/blocked-project-release-bundles",
      "commitMarkerTagPrefix": "plan-",
      "bundleFormatVersion": 1,
      "writeEnvironment": "production-evidence-write-blocked-project",
      "readCredentialScope": "artifact-store-readonly",
      "blockedRetentionDays": 365,
      "successfulRetentionDays": 730
    },
    "payloadSha": "1111111111111111111111111111111111111111",
    "artifacts": {
      "package": {
        "kind": "npm-package"
      }
    },
    "targets": ["github:release", "npm:official"],
    "targetArtifacts": {
      "github:release": ["package"],
      "npm:official": ["package"]
    },
    "targetAuthContracts": {
      "github:release": {
        "requiredEnvironment": "production-github-blocked-project",
        "authClass": "github-app-installation-token",
        "allowedCredentialSource": "environment-gated-external-broker",
        "actorClass": "github-release-publisher",
        "providerWorkflowPath": null,
        "providerEnvironment": null,
        "providerKey": null,
        "providerTrustCapabilities": null,
        "providerRefClaimSupport": null,
        "providerSupportsReadOnlyInspection": null,
        "providerRefClaimMode": null,
        "providerRefClaimModeRationale": null,
        "providerConfigReviewedAt": null,
        "providerConfigReviewRef": null,
        "allowedRefClaims": [],
        "providerAudience": null
      },
      "npm:official": {
        "requiredEnvironment": "production-npm-blocked-project-rl-1.2",
        "authClass": "external-registry-oidc-trusted-publishing",
        "allowedCredentialSource": "github-oidc",
        "actorClass": null,
        "providerWorkflowPath": ".github/workflows/official.yml",
        "providerEnvironment": "production-npm-blocked-project-rl-1.2",
        "providerKey": "npmjs",
        "providerTrustCapabilities": ["environment", "repository", "workflow-path"],
        "providerRefClaimSupport": "unsupported",
        "providerSupportsReadOnlyInspection": false,
        "providerRefClaimMode": "workflow-only",
        "providerRefClaimModeRationale": "provider-does-not-support-exact-ref-claims",
        "providerConfigReviewedAt": "2026-02-15T00:00:00Z",
        "providerConfigReviewRef": {
          "kind": "api-snapshot",
          "locator": "artifact://provider-reviews/npmjs/blocked-project/2026-02-15.json",
          "evidenceSha256": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
        },
        "allowedRefClaims": ["refs/heads/release/blocked-project/v1.2"],
        "providerAudience": "npm:registry.npmjs.org"
      }
    },
    "npmAccessHint": "public"
  },
  "artifactIdentity": {
    "artifactLocator": "artifact-store://official/blocked-project/sha256-2222",
    "attestationRef": "github-attestation://example-org/three/runs/1234567890/attestations/987654321",
    "subjects": {
      "package": {
        "filename": "three-blocked-project-1.2.3.tgz",
        "sha256": "sha256:2222222222222222222222222222222222222222222222222222222222222222"
      }
    }
  },
  "reason": "published-with-lock-residue",
  "evidenceRef": "issue:1234",
  "recovery": {
    "approvalState": "approved",
    "allowedMode": "clear-lock-only",
    "authorizationRef": "pr:1235",
    "authorizedAt": "2026-03-01T12:00:00Z",
    "approvedForEntryVersion": 4,
    "approvedForPlanDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "approvedForBlockedStage": "post-confirmation"
  },
  "updatedAt": "2026-03-01T00:00:00Z"
}
```

Reference-field schema (`evidenceRef`, `authorizationRef`) is the closed string format `{kind}:{identifier}` where `kind` is one of `issue`, `pr`, `url`, or `ticket`, and `identifier` is a non-empty target-specific opaque string without surrounding whitespace.

Blocked entries must always carry `digestChangeReason`. The only non-null value is the machine-readable string `{rebuilt-bytes-differ-from-prior-run}`. When no digest drift is known, the field must be present with explicit value `null` rather than omitted. A non-null value is legal only when `blockedStage = pre-provenance` and operators have evidence that a `pre-provenance` rebuild no longer matches prior-run digest evidence for the same frozen plan. When `digestChangeReason` is non-null, the design requires a recorded abort disposition before the project may return to `ready`; publish-capable recovery remains forbidden.

`riskFlags` is a closed lexicographically sorted array of machine-readable review-risk markers. In v1 the only allowed value is `no-prior-digest-baseline`. Blocked entries must carry `riskFlags` explicitly, using `[]` when no additional risk marker applies. When that flag is present for a frozen plan, later blocked-state updates for the same plan must preserve it until the project returns to `ready`.

`targetResults` is a closed object keyed exactly by `frozenPlan.targets`. Each value is a closed object with exactly `state` and `evidenceRef`. `state` is the closed set `{not-started, publish-succeeded-unconfirmed, confirmed, failed, unknown, verification-mismatch}`. `evidenceRef` is either `null` or one durable evidence locator / record reference that justifies the current per-target state. This field exists so blocked entries can represent mixed multi-target states without overloading the top-level `reason`.

`lockIdentity` is a closed object with exactly these fields: `planDigest`, `lockInstanceToken`, `lockRef`, `lockedAt`, `runId`, and `runAttempt`. It persists the reviewed live-lock instance independently from `frozenPlan` and must be copied from the authoritative lock payload rather than recomputed from current branch state.

`blockedStage × reason` validity matrix:

| `blockedStage` | Allowed `reason` values |
| --- | --- |
| `pre-provenance` | `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `attestation-generation-failed`, `lock-integrity-failure`, `operator-aborted` |
| `provenance-uncertain` | `provenance-reconciliation-failed`, `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `existing-bundle-ownership-ambiguous`, `lock-integrity-failure`, `operator-aborted` |
| `post-provenance` | `pre-provenance-write-completed-awaiting-review`, `tag-conflict`, `tag-write-failure`, `attestation-verification-failed`, `publish-job-failure`, `publish-confirmation-failed`, `publish-confirmation-timeout`, `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `lock-integrity-failure`, `operator-aborted` |
| `post-confirmation` | `published-with-lock-residue`, `post-confirmation-verification-failed`, `lock-integrity-failure`, `operator-aborted` |

Artifact-identity schema (`artifactIdentity`) is a closed object with these fields:

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `artifactLocator` | `string` | Yes | Non-empty durable locator returned by the §4.10 store. |
| `attestationRef` | `string` | Yes | Non-empty durable attestation/provenance locator. |
| `subjects` | `object` | Yes | Closed object keyed exactly by the union of every alias named anywhere in `frozenPlan.targetArtifacts`. |

Each `artifactIdentity.subjects.<alias>` entry is also closed and contains exactly these fields: `filename`, the canonical publication filename or GitHub Release asset name for that alias; and `sha256`, which must be `sha256:<64 lowercase hex>`. No other digest algorithms are allowed in v1.

This stage/presence matrix is the single authoritative source for when `artifactIdentity` may appear:

| Admission state | `artifactIdentity` rule |
| --- | --- |
| `ready` | absent |
| `blockedStage = pre-provenance` | absent |
| `blockedStage = provenance-uncertain` | either absent, or the full closed object once read-only reconciliation has reconstructed authoritative identity |
| `blockedStage = post-provenance` | required and must be the full closed object |
| `blockedStage = post-confirmation` | required and must be the full closed object |

`recovery` is also a closed object. When present, it contains exactly these fields:

| Field | Type | Required presence | Notes |
| --- | --- | --- | --- |
| `approvalState` | `string` | Always | Closed set `{not-approved, approved, aborted}`. |
| `allowedMode` | `string` | Conditional | Closed set `{rerun-plan, reconcile-store, restore-bundle, clear-lock-only}`. Required only when `approvalState = approved`; optional advisory recommendation when `approvalState = not-approved`; forbidden when `approvalState = aborted`. |
| `authorizationRef` | `string` | Conditional | Closed `{kind}:{identifier}` reference for the approving or aborting record. Required for `approved` and `aborted`; forbidden for `not-approved`. |
| `authorizedAt` | `string` | Conditional | RFC 3339 UTC timestamp for the approving or aborting record. Required for `approved` and `aborted`; forbidden for `not-approved`. |
| `approvedForEntryVersion` | `integer` | Conditional | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal the current top-level `entryVersion` whenever present. |
| `approvedForPlanDigest` | `string` | Conditional | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal `frozenPlan.planDigest` whenever present. |
| `approvedForBlockedStage` | `string` | Conditional | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal the current `blockedStage` whenever present. |

Validation rules:

- `schemaVersion` must equal `1`.
- `projectKey` must exactly equal the canonical `project-key` encoded by the file path `.github/official-admission-state/<project-key>.json`.
- `status` must be exactly `ready` or `blocked`.
- `updatedAt` is required for every entry and must be an RFC 3339 UTC timestamp.
- A `ready` entry must not contain `blockedStage`, `entryVersion`, `digestChangeReason`, `riskFlags`, `targetResults`, `lockIdentity`, `frozenPlan`, `artifactIdentity`, `reason`, `evidenceRef`, or `recovery`.
- A `blocked` entry must include `blockedStage`, `entryVersion`, `digestChangeReason`, `riskFlags`, `targetResults`, `lockIdentity`, `frozenPlan`, `reason`, `evidenceRef`, and `recovery`.
- `blockedStage` must be exactly `pre-provenance`, `provenance-uncertain`, `post-provenance`, or `post-confirmation`.
- `entryVersion` must be an integer `>= 1` and is the versioned review/approval binding for the current blocked facts.
- `lockIdentity.planDigest` must exactly equal `frozenPlan.planDigest`, `lockIdentity.lockRef` must exactly equal `refs/tags/official-lock/<project-key>`, `lockIdentity.runAttempt` must equal `1`, and `lockIdentity.lockedAt` must be an RFC 3339 UTC timestamp copied from the authoritative live-lock payload.
- `frozenPlan` must be the exact closed `release-plan` object from §5.10.
- `reason` must be a non-empty kebab-case string from the closed set `{artifact-store-unavailable, artifact-store-digest-mismatch, artifact-store-timeout, attestation-generation-failed, attestation-verification-failed, provenance-reconciliation-failed, existing-bundle-ownership-ambiguous, pre-provenance-write-completed-awaiting-review, tag-conflict, tag-write-failure, publish-job-failure, publish-confirmation-failed, publish-confirmation-timeout, published-with-lock-residue, post-confirmation-verification-failed, lock-integrity-failure, operator-aborted}` and must satisfy the authoritative `blockedStage × reason` matrix above.
- `digestChangeReason` must be present on every blocked entry. When non-null, it must be `rebuilt-bytes-differ-from-prior-run`. When it is non-null, `status` must be `blocked`, `blockedStage` must be `pre-provenance`, and publish-capable recovery authorization must remain absent until the §7.5 abort decision is recorded.
- `riskFlags` must be present on every blocked entry, must contain unique values, and may contain only `no-prior-digest-baseline`.
- `targetResults` must be present on every blocked entry, its keys must exactly equal `frozenPlan.targets`, every `state` must come from the closed enum above, and `evidenceRef` must be explicit `null` when the repository has no durable evidence reference for that per-target observation.
- `evidenceRef` and `recovery.authorizationRef` must use the closed `{kind}:{identifier}` format; cross-repository or external references are allowed only when their `kind` is explicitly encoded.
- `artifactIdentity` must never be `null`; it is either absent when not yet authoritative or present as the full closed object.
- When `blockedStage = pre-provenance`, `artifactIdentity` must be absent.
- When `blockedStage = provenance-uncertain`, `artifactIdentity` may be absent while operators reconcile the store, or it may be the fully reconstructed closed object once reconciliation succeeds. Partial `artifactIdentity` objects are forbidden.
- When `blockedStage = post-provenance` or `blockedStage = post-confirmation`, `artifactIdentity` must include a durable `artifactLocator`, durable `attestationRef`, and the exact filename-and-digest subject map keyed by the union of aliases referenced by `frozenPlan.targetArtifacts`.
- `artifactIdentity.subjects` key sets must exactly equal the union of every alias named anywhere in `frozenPlan.targetArtifacts`.
- `recovery.approvalState` must be exactly `not-approved`, `approved`, or `aborted`.
- When `recovery.approvalState = approved`, `recovery.allowedMode` must be `rerun-plan` for `pre-provenance`, `reconcile-store` for `provenance-uncertain`, `restore-bundle` for `post-provenance`, and `clear-lock-only` for `post-confirmation`; `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` are all required. `rerun-plan` means a fresh reviewed `official.yml` dispatch for the frozen blocked plan, never a GitHub rerun attempt under the same `run_id`.
- When `recovery.approvalState = not-approved`, `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` must all be absent. `allowedMode` may be absent, or it may appear only as the single stage-valid machine-generated recommendation (`rerun-plan` for `pre-provenance`, `reconcile-store` for `provenance-uncertain`, `restore-bundle` for `post-provenance`, `clear-lock-only` for `post-confirmation`). A present `allowedMode` in `not-approved` state is advisory only and becomes binding only after `approvalState = approved`.
- When `recovery.approvalState = aborted`, `allowedMode` must be absent and `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` are all required so the checked-in state distinguishes an explicit abort decision from an unreviewed blocked entry.
- When `digestChangeReason` is non-null, `recovery.approvalState` may be `not-approved` or `aborted`, but never `approved`.
- When any recovery binding field is present, `approvedForEntryVersion` must exactly equal the current top-level `entryVersion`, `approvedForPlanDigest` must exactly equal `frozenPlan.planDigest`, and `approvedForBlockedStage` must exactly equal the current `blockedStage`.
- `recovery` is a closed object; unknown nested fields are hard failures.
- Unknown top-level fields are hard failures until explicitly added to the schema.

### 6.4 Update model

- Normal admission-state, blocked-state evidence, and recovery-authorization updates happen only through reviewed PRs to the same protected official release branch that is authoritative for the selected frozen plan.
- A single reviewed PR may update multiple files under `.github/official-admission-state/`; bulk unblock and bulk return-to-ready updates are explicitly supported without cross-project JSON merge conflicts.
- If an official run becomes partial, failed, or uncertain after the live lock exists, the checked-in blocked state must record whether the run is `pre-provenance`, `provenance-uncertain`, `post-provenance`, or `post-confirmation` so the failure window is representable without inventing nonexistent state and without forcing a successful publish back through rebuild/re-publish recovery.
- While `status = blocked`, `frozenPlan` is immutable. Operators must not replace `frozenPlan`, `frozenPlan.planDigest`, `payloadSha`, target sets, or any other release-identity field in-place. A different frozen plan requires returning the project to `ready` and then creating a new blocked entry for that new plan.
- Refreshing only the current checked-in `providerConfigReviewedAt` and/or `providerConfigReviewRef` for unchanged target-auth bindings is not a `frozenPlan` change, does not require a blocked-entry stage change, and does not require `entryVersion` increment by itself. Any change to the remaining auth-binding fields still requires the normal immutable-frozen-plan rules.
- For one frozen `planDigest`, blocked-stage transitions are monotonic. The only allowed non-ready transitions are `pre-provenance -> provenance-uncertain`, `pre-provenance -> post-provenance`, `provenance-uncertain -> post-provenance`, and `post-provenance -> post-confirmation`; any blocked stage may also remain unchanged while evidence or recovery fields are updated. Regressions and direct jumps to `post-confirmation` from earlier stages are forbidden.
- Return from any blocked stage to `ready` is legal only when the same frozen `planDigest` has reached a reviewed terminal disposition: either successful completion with the live lock absent, or an approved abort/cleanup decision recorded with the required evidence for that plan.
- Any PR that changes blocked facts (`blockedStage`, `reason`, `digestChangeReason`, `riskFlags`, `artifactIdentity`, or any lock-classification evidence that changes operator understanding of the same blocked plan) must increment `entryVersion`. A pure metadata refresh that changes only `updatedAt` does not increment `entryVersion`.
- Recovery approval is bound to the tuple `(entryVersion, frozenPlan.planDigest, blockedStage)`, not merely to a file path. A previously approved recovery becomes stale as soon as any later PR increments `entryVersion` without simultaneously recording a fresh approval for the new version.
- For one frozen `planDigest`, `recovery.approvalState` monotonicity is version-scoped rather than file-lifetime-scoped: within one `entryVersion`, `not-approved -> approved` or `not-approved -> aborted` are legal; `approved -> aborted` is legal only when the same PR both increments `entryVersion` and records the superseding abort decision; `aborted -> approved` is forbidden for the same `entryVersion`.
- The authoritative `recovery.approvalState` transition table is:
- Only the transitions listed below are legal. In particular, `approved -> not-approved`, `aborted -> not-approved`, and `aborted -> approved` are hard validation failures; recovery authorization is irreversible within one `entryVersion`.

| Current context | Allowed next state without `entryVersion` increment | Allowed next state only with `entryVersion` increment |
| --- | --- | --- |
| `status = ready` (no `recovery` object) | none | first blocked entry starts at `not-approved` |
| `not-approved` | `not-approved`, `approved`, `aborted` | `not-approved`, `approved`, `aborted` |
| `approved` | `approved` | `approved`, `aborted` |
| `aborted` | `aborted` | `aborted` |

- A return from any blocked state to `ready` removes the `recovery` object entirely and is legal only through the reviewed terminal-disposition rule in this section; `ready -> approved` and `aborted -> approved` are never direct transitions.
- After baseline approval, checked-in admission-state changes do not by themselves retroactively cancel an already-running release. Live-lock removal is only a best-effort interruption signal: it can stop later irreversible mutations only if a downstream job has not already completed its final lock revalidation. The runbook must explicitly warn that one or more additional external requests inside the current publish job may still complete after break-glass lock removal, and any post-provenance or post-confirmation cleanup must account for that possibility.
- The workflow itself must create or update a GitHub issue when it fails after lock creation, when it intentionally stops after the first `pre-provenance` durable write, or when it ends in `published-with-lock-residue`, containing a structured blocked-entry JSON draft, required evidence fields, and the recommended `blockedStage`; this issue-creation duty applies to verification-only failures too, so a `post-confirmation` anchor mismatch detected in `create-release-tag` must emit the same structured blocked-entry path even if `confirm-publish-state` is skipped; it must also emit a machine-generated `event-evidence` JSON payload so operators are not expected to hand-author incident evidence under pressure. If issue creation fails, the run log must print the same structured payload verbatim **and** the workflow must persist that same structured draft into the live-lock annotation payload or the durable artifact store pointer referenced by that payload. For the intentional `pre-provenance` stop, that draft must use `blockedStage = post-provenance`, `reason = pre-provenance-write-completed-awaiting-review`, and the advisory `recovery.allowedMode = restore-bundle` while keeping `recovery.approvalState = not-approved`. For `published-with-lock-residue`, that draft must use `blockedStage = post-confirmation` and the advisory `recovery.allowedMode = clear-lock-only` while keeping `recovery.approvalState = not-approved`. Every machine-generated blocked-entry draft after lock creation must also populate `targetResults` from the best available in-run evidence so mixed per-target states are preserved rather than collapsed into one top-level reason.
- The reviewed helper `eng/scripts/create-blocked-entry` is a Day 0 implementation prerequisite for official release enablement. It must accept the frozen lock payload plus structured event evidence and emit a schema-valid blocked-entry JSON draft so operators do not hand-author closed-schema recovery objects during an incident or an intentional recovery stop.
- `eng/scripts/create-blocked-entry` must meet this minimum Day 0 interface contract:
  - implementation baseline: Python `3.12+` authoritative helper with repository-owned logic and no mutation permissions
  - invocation contract: `eng/scripts/create-blocked-entry --lock-payload <path|-> (--event-evidence <path|-> | --event-evidence-artifact <path|->) --blocked-stage <stage> [--artifact-identity <path>] [--reason <reason>] [--recovery-mode <mode>] [--target-results <path>] [--format json]`
  - input contract: `--lock-payload` is the exact parsed live-lock annotation payload; the normal path is a machine-generated `--event-evidence-artifact` emitted by the workflow, while `--event-evidence` remains the escape hatch for manually assembled closed JSON describing either a failed step or an intentional stop, run metadata, observed remote state, and evidence references; `--artifact-identity` is required only for `post-provenance` and `post-confirmation`; `--target-results` is required for every blocked draft after lock creation and carries the closed per-target result map used for `targetResults`; when the emitted draft keeps `recovery.approvalState = not-approved`, any provided `--recovery-mode` is an advisory machine recommendation rather than an approved authorization binding
  - stdout contract: prints exactly one schema-valid blocked-entry JSON object and nothing else
  - exit codes: `0` success; `2` invalid invocation; `3` schema/input validation failure; `4` required evidence missing or internally inconsistent
  - minimum fixtures: `pre-provenance`, intentional-stop `post-provenance`, irreconcilable `provenance-uncertain`, `post-provenance`, and `published-with-lock-residue` examples, plus a negative fixture proving unknown top-level fields are rejected
- The blocked-entry PR execution path is part of the runbook, not operator folklore: the standard procedure is generate draft JSON with `eng/scripts/create-blocked-entry`, commit it on a repository-owned branch named `blocked-entry/<project-key>/<planDigest-prefix>`, open or update exactly one PR for that branch, and reuse that same PR until the blocked stage changes.
- The operational responsibility chain is bound to named roles: the dispatching release engineer owns opening the blocked-entry PR if automation did not; the release approver on duty owns review/approval; the release-duty incident commander owns the SLA clock and escalation annotations; and a break-glass operator is paged only when CI or repository controls block the normal PR path.
- Stuck blocked entries and live locks use the severity-tiered escalation matrix from §7.6 rather than one universal timer. Customer-visible or externally mutated states page materially faster than pre-mutation-only states.
- When a `pre-provenance` recovery proceeds without any known prior-run subject digest evidence, the workflow-generated `event-evidence` payload and blocked-entry draft for that recovery must already carry the machine-readable `no-prior-digest-baseline` diagnostic as a `riskFlags` entry, and the next reviewed blocked-entry update must preserve that flag so later operators know the rebuild had no earlier digest baseline for comparison. That flag records missing equivalence evidence, not same-identity proof. Any later publish-capable continuation for that frozen plan is a high-risk recovery path and requires the additional independent review rules from §6.4.1 before it may be authorized.
- When a recovery run fails, operators may directly re-dispatch it only when the observable blocked stage and authorized recovery mode are unchanged. If the failed recovery changed the boundary state—for example, it completed the first durable write and then failed before tag creation—the workflow must stop, operators must update the blocked entry to the new stage, and a new reviewed PR must authorize the next recovery mode.
- When a recovery or `clear-lock-only` run succeeds, the workflow should auto-open a ready-transition PR draft for the affected project set using the recorded terminal evidence, so reviewers approve the state transition instead of manually creating a second PR from scratch. That automation must use a dedicated repository-maintenance credential with `contents: write` and `pull-requests: write`, must create or update exactly one deterministic branch named `ready-transition/<project-key>/<planDigest-prefix>`, and must reuse an existing open PR for that branch instead of opening duplicates.
- If CI infrastructure is unavailable, or if a higher-priority hotfix is blocked behind a lower-priority plan and the documented contention-decision deadline from §7.2 cannot be met through the normal reviewed PR path, the documented break-glass path may push an admission-state-only change directly to the authoritative protected branch, but only for the exact fields required to authorize, abort, or clear a blocked entry.

### 6.4.1 Recovery PR review requirements

- Any PR that creates or changes a blocked entry, sets `recovery.approvalState = approved` or `aborted`, changes `recovery.allowedMode`, records `digestChangeReason`, or returns a project from `blocked` to `ready` must be protected by dedicated CODEOWNERS on `.github/official-admission-state/**`.
- That CODEOWNERS path must be owned by the repository’s release-governance owners, and that reviewer population must be equal to or narrower than the baseline official approval population for `production-<project-key>`. Generic contributor review is not sufficient.
- A recovery authorization PR must not merge until at least one release-governance owner from that dedicated population has approved it. Repositories may require more approvals, but they must not require fewer than the protected baseline official approval model.
- If the blocked entry for the same frozen plan carries `riskFlags` containing `no-prior-digest-baseline` and the PR would authorize any publish-capable continuation after that last-resort rebuild, the PR is a high-risk recovery authorization and must receive one additional independent approval from a second release-governance owner who is distinct from the first required approver. That extra approval is in addition to the normal reviewed blocked-entry process; baseline environment approval alone is not sufficient for this path.
- A PR that approves `rerun-plan`, `reconcile-store`, `restore-bundle`, or `clear-lock-only` is itself a release-governance act. Branch protection and CODEOWNERS for `.github/official-admission-state/**` are therefore part of the authorization boundary, not a documentation-only aid.
- The recovery authorization template must display and reviewers must explicitly check the bound tuple `(entryVersion, blockedStage, planDigest)`; a PR that approves recovery for one tuple must not be reused after later evidence increments `entryVersion` or changes `blockedStage`.
- Recovery PR templates and reviewer checklists must display a high-visibility warning banner whenever `blockedStage = pre-provenance`, stating that the rebuild is not byte-stable, the version may need to be burned, and approval is for a last-resort recovery path rather than a routine rerun.
- When `digestChangeReason` is present or a break-glass abort is being prepared, the approving recovery PR must reference the exact evidence for the prior digest, the rebuilt digest, and the disposition decision before it may merge.
- Repositories that need different recovery approver sets by project should use explicit per-file or per-project CODEOWNERS entries rather than relying on one broad wildcard. Illustrative pattern:

```text
.github/official-admission-state/project-a.json @org/release-governance-a
.github/official-admission-state/project-b.json @org/release-governance-b
.github/official-admission-state/*            @org/release-governance-core
```

## 7. Release Serialization and Recovery Contract

### 7.1 Frozen policy, plan, and payload identity

- `preflight-validate` must emit immutable `policy-sha` plus one canonical frozen `release-plan`.
- For a normal official release, `policy-sha` is also the frozen payload snapshot. For a reviewed recovery, `release-plan.payloadSha` remains the frozen blocked payload snapshot carried by checked-in state.
- The authoritative official release identity is the frozen `release-plan`, not `policy-sha` alone.
- `release-plan` must be exactly the closed object defined in §5.10.
- `planDigest` is the canonical `sha256:<64 lowercase hex>` digest of the RFC 8785 / JCS canonical JSON serialization of every `release-plan` field except `planDigest` itself, using the closed `targetAuthContracts` objects from §5.11 after placeholder-normalizing the two freshness-only provider-review fields for every target: `providerConfigReviewedAt` is replaced with the literal timestamp `1970-01-01T00:00:00Z`, and each non-null `providerConfigReviewRef` is replaced with a placeholder object that preserves `kind` while replacing `locator` with `artifact://provider-review/placeholder` and `evidenceSha256` with `sha256:0000000000000000000000000000000000000000000000000000000000000000`; `null` `providerConfigReviewRef` remains `null`. `release-plan.schemaVersion` is part of that frozen identity. The separately validated `officialTargetConfirmationPolicies` from §5.12 are intentionally excluded because they are operational timing controls, not release-identity fields. Recovery-time provider-review freshness checks remain the deliberate exception to strict frozen-equality matching: implementations must compare the current checked-in `targetAuthContracts` against the frozen copy after ignoring only `providerConfigReviewedAt` and `providerConfigReviewRef`, and they must source freshness from the current checked-in values rather than from the frozen copy.
- Implementations must use an RFC 8785 / JCS conformant serializer that emits UTF-8 bytes, preserves the exact closed-schema field set, and does not pretty-print, reorder outside the documented sort rules, or coerce strings or path separators. All numeric fields in those canonicalized surfaces must remain exact IEEE 754 safe integers as noted above; implementations must reject values that cannot round-trip identically across the supported languages.
- This design intentionally uses the full RFC 8785 / JCS contract even though today’s checked-in release objects are mostly ASCII, string-heavy, and schema-constrained. The full spec avoids later interoperability breaks when fields expand, keeps one canonicalizer across `release-plan`, review payloads, and checked-in state, and removes any temptation to invent a repository-local “almost JSON” digest scheme.
- The checked-in reference implementation for canonicalization is reserved as `eng/scripts/jcs-canonicalize` and the checked-in fixture suite is reserved under `eng/tests/jcs-fixtures/`. Both are Day 0 implementation prerequisites for any release-workflow integration. Until implementation exists, those paths are design-reserved names and no alternate ad hoc canonicalizer is authoritative.
- `eng/scripts/jcs-canonicalize` and `eng/tests/jcs-fixtures/` must meet this minimum Day 0 interface contract:
  - implementation baseline: Python `3.12+` authoritative reference implementation, deterministic on both `ubuntu-24.04` and `windows-2022`, with any wrappers treated as non-authoritative launchers. Because Python's default `json.loads()` silently accepts duplicate keys, implementations must use `object_pairs_hook` or an equivalent duplicate-key-rejecting parser path instead of the default loader
  - invocation contract: `eng/scripts/jcs-canonicalize --input <path|-> --mode canonicalize|digest --schema-surface release-plan --reject-duplicates [--debug-out <path>]` and `eng/scripts/jcs-canonicalize --verify-fixtures --fixtures-root eng/tests/jcs-fixtures/`
  - `--mode canonicalize` stdout contract: exactly the UTF-8 RFC 8785 / JCS serialization bytes for the supplied JSON value, with no trailing explanatory text
  - `--mode digest` stdout contract: only `sha256:<64 lowercase hex>` followed by `\n`, computed from those canonical bytes
  - `--debug-out` writes a structured machine-readable trace containing the validated schema surface, canonical key ordering, canonical-byte digest, and any normalization decisions needed for operator debugging without changing the stdout contract
  - failure contract: duplicate keys, non-UTF-8 input, unsupported numeric forms, or schema-shape violations are hard failures with non-zero exit; exit codes are `0` success, `2` invalid invocation or invalid JSON input, `3` fixture mismatch or duplicate-key rejection, and `4` internal canonicalization failure
  - fixture-suite minimum coverage: object-key ordering, nested arrays/objects, required explicit `null` fields, duplicate-key rejection, non-BMP Unicode, lone-surrogate rejection, UTF-8 BOM rejection, `-0` normalization coverage, escape normalization, representative IEEE 754 boundary values that are valid JSON numbers, and golden `(input, canonical-bytes, digest)` vectors consumed identically on Linux and Windows
- The repository must keep a checked-in JCS fixture suite and golden digests. Cross-language implementations must validate themselves against those same fixtures before they are trusted for release. That fixture suite must include at minimum non-BMP Unicode code points, lone-surrogate rejection cases, UTF-8 BOM rejection cases, `\uXXXX` escape normalization cases, `-0` coverage, IEEE 754 boundary-number cases actually representable in JSON, duplicate-key rejection fixtures, and required-null-field fixtures.
- Before schema validation or serialization, implementations must parse `release.json`, `.github/repository-release-contract.json`, and `.github/official-admission-state/<project-key>.json` in strict duplicate-key-rejecting mode; duplicate keys are hard failures and must never be normalized away before `planDigest` computation. Python implementations must not rely on the default `json.loads()` behavior because it silently overwrites earlier duplicate keys.
- Before serialization, `targets`, every `targetArtifacts.<target>` array, every `targetAuthContracts.<target>.allowedRefClaims` array, every non-null `targetAuthContracts.<target>.providerTrustCapabilities` array, and the `artifacts`, `targetArtifacts`, and `targetAuthContracts` object keys must all be lexicographically sorted.
- Nullable schema fields remain part of the canonical object. Implementations must serialize required nullable fields explicitly as `null` rather than omitting them, and all cooperating implementations must agree on that exact null-field handling.
- Until a repository-reviewed native implementation passes the shared fixture suite, C# and Ruby helpers must delegate `planDigest` computation to the checked-in reference implementation rather than using ad hoc native serializers. There is no temporary authoritative digest path before the Day 0 reference implementation and fixtures exist.
- Extra fields are forbidden in both `release-plan` and `targetAuthContracts`; implementations must reject them rather than ignoring them. `release-plan.schemaVersion` must equal `1` in this document revision.
- `artifact://` is a repository-owned durable-evidence locator scheme. Its canonical URI shape is `artifact://<collection>/<path>` with no query string or fragment. `collection` names the reviewed storage namespace, the remaining slash-separated path is an opaque repository-owned locator inside that namespace, and the paired digest field such as `evidenceSha256` remains the authoritative byte-identity check for the referenced object.
- `providerConfigReviewRef.locator` may use `artifact://` only for machine-readable provider-review evidence retained in repository-controlled durable storage; human-only screenshots or transient console state are insufficient.
- Release tooling and runbooks must treat unknown `artifact://` collections as unsupported rather than guessing a storage backend.
- For a reviewed official recovery run, `release-plan.payloadSha` may differ from `policy-sha` only when the checked-in blocked state and the live lock explicitly carry forward that same frozen plan.
- `release-plan.releaseLine` must be `null` when the frozen plan authorizes from `main`; it must be the exact non-empty checked-in release line when the frozen plan authorizes from `release/<project-key>/v<releaseLine>`.
- For `node-npm` plans, `npmAccessHint` must be frozen into `release-plan`; for all other build kinds it must be `null`.
- The annotated official release tag is the durable release-identity anchor for a frozen release plan; it is created before external publication and therefore must **not** be treated as proof that publication succeeded. It must carry the canonical frozen release identity consisting of the frozen release-plan identity plus `artifactLocator`, `attestationRef`, and exact subject filename-and-digest bindings. A release becomes successful only when `confirm-publish-state` records success for every selected target and `release-complete` compare-clears the live lock, or when a reviewed recovery path reaches that same terminal condition. Persisted confirmation records are also canonical JCS objects: each `recordDigest` is the `sha256:<64 lowercase hex>` digest of the RFC 8785 / JCS UTF-8 serialization of the confirmation record with `recordDigest` itself omitted, and all producers/consumers must use that exact contract for idempotency and interop.

Canonicalization example for `planDigest`:

The JSON below is the full digest-input form of the example fixture: it is the complete `release-plan` object with only `planDigest` itself omitted, exactly as required by this section. The actual `planDigest` is computed from the RFC 8785 / JCS canonical UTF-8 serialization of that full object after the §7.1 placeholder normalization of `providerConfigReviewedAt` and `providerConfigReviewRef`, not from the pretty-printed text shown here.

```json
{
  "artifacts": {
    "package": {
      "kind": "npm-package"
    }
  },
  "authorizedBranch": "refs/heads/release/example-project/v1.2",
  "buildKind": "node-npm",
  "ecosystem": "jsts",
  "environmentBindings": {
    "baseline": "production-example-project",
    "evidenceWrite": "production-evidence-write-example-project",
    "refWrite": "production-ref-write-example-project",
    "targets": {
      "github:release": "production-github-example-project",
      "npm:official": "production-npm-example-project-rl-1.2"
    }
  },
  "artifactStoreBinding": {
    "backendClass": "oci-registry",
    "blockedRetentionDays": 365,
    "bundleFormatVersion": 1,
    "commitMarkerTagPrefix": "plan-",
    "readCredentialScope": "artifact-store-readonly",
    "repository": "ghcr.io/three/example-project-release-bundles",
    "successfulRetentionDays": 730,
    "writeEnvironment": "production-evidence-write-example-project"
  },
  "npmAccessHint": "public",
  "officialTag": "refs/tags/release/example-project/v1.2.3",
  "packageIdentity": "@three/example-project",
  "packageManifestPath": "src/example-project/package.json",
  "payloadSha": "1111111111111111111111111111111111111111",
  "projectKey": "example-project",
  "projectPath": "src/example-project",
  "releaseLine": "1.2",
  "schemaVersion": 1,
  "targetArtifacts": {
    "github:release": ["package"],
    "npm:official": ["package"]
  },
  "targetAuthContracts": {
    "github:release": {
      "actorClass": "github-release-publisher",
      "allowedCredentialSource": "environment-gated-external-broker",
      "allowedRefClaims": [],
      "authClass": "github-app-installation-token",
      "providerAudience": null,
      "providerConfigReviewRef": null,
      "providerConfigReviewedAt": null,
      "providerEnvironment": null,
      "providerKey": null,
      "providerRefClaimMode": null,
      "providerRefClaimModeRationale": null,
      "providerRefClaimSupport": null,
      "providerSupportsReadOnlyInspection": null,
      "providerTrustCapabilities": null,
      "providerWorkflowPath": null,
      "requiredEnvironment": "production-github-example-project"
    },
    "npm:official": {
      "actorClass": null,
      "allowedCredentialSource": "github-oidc",
      "allowedRefClaims": ["refs/heads/release/example-project/v1.2"],
      "authClass": "external-registry-oidc-trusted-publishing",
      "providerAudience": "npm:registry.npmjs.org",
      "providerConfigReviewRef": {
        "evidenceSha256": "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
        "kind": "api-snapshot",
        "locator": "artifact://provider-reviews/npmjs/example-project/2026-02-15.json"
      },
      "providerConfigReviewedAt": "2026-02-15T00:00:00Z",
      "providerEnvironment": "production-npm-example-project-rl-1.2",
      "providerKey": "npmjs",
      "providerRefClaimMode": "workflow-only",
      "providerRefClaimModeRationale": "provider-does-not-support-exact-ref-claims",
      "providerRefClaimSupport": "unsupported",
      "providerSupportsReadOnlyInspection": false,
      "providerTrustCapabilities": ["environment", "repository", "workflow-path"],
      "providerWorkflowPath": ".github/workflows/official.yml",
      "requiredEnvironment": "production-npm-example-project-rl-1.2"
    }
  },
  "targets": ["github:release", "npm:official"],
  "version": "1.2.3"
}
```

The example above is a canonicalization test vector, not the recommended production provider-review evidence profile from §4.9 and §5.11.

The RFC 8785 / JCS canonical serialization of the full object above, after the required provider-review placeholder normalization from this section, hashes to:

- `sha256:2faa3e6164a999d99ce226bc505736402a807ddfbe749c1bd28a4c8d3f6ae59b`

This exact full example object and digest pair must also be preserved as one checked-in golden fixture under `eng/tests/jcs-fixtures/` so future canonicalization changes cannot silently drift the documented test vector. The checked-in golden fixture is authoritative only if it includes the required `environmentBindings` and `artifactStoreBinding` fields shown above; a partial object is not a valid `release-plan` test vector for `planDigest`.

### 7.2 Concurrency model

- Buddy publish runs and the entire `official.yml` run must share one per-project concurrency group, such as `release/<project-key>`, so the two channels cannot execute irreversible mutations concurrently for that project.
- `official.yml` intentionally occupies that shared concurrency group for the full official run. That is the reviewed tradeoff that keeps every official publish-capable job inside direct `.github/workflows/official.yml` job identity instead of a reusable-workflow hop.
- An official run that has not yet passed the baseline approval gate must not be allowed to occupy `release/<project-key>` indefinitely during monitor outage. The degraded-mode rules in §7.6 are mandatory: once suspension for monitor outage is in effect, a surviving monitor/standby or the documented off-hours responder must cancel any still-waiting baseline-approval run within 15 minutes unless one explicit break-glass exception records that run id.
- A project’s checked-in admission file and live lock are still per-project, not per-release-line. Mainline and maintenance-line official releases for the same project therefore serialize through the same live lock even when they authorize from different protected branches. This is an explicit correctness-over-throughput tradeoff: one blocked maintenance-line plan can delay a mainline hotfix for the same project until operators resolve, abort, or clear that blocked state.
- `cancel-in-progress: false` is used only to avoid overlapping execution and to avoid evicting an in-flight mutation phase; it must not be treated as a durable FIFO queue or admission ledger.
- Because GitHub concurrency ordering is not a durable queue, durable ordering, recovery, and unblock decisions come from the checked-in admission state plus the live lock, not from GitHub Actions pending-run behavior. Monitoring and runbooks must therefore treat stuck pending/running concurrency state as an operational incident rather than a trustworthy queue. Raw `workflow_dispatch` input is not an authoritative project-resolution surface, so an invalid or unknown `project-key` may still occupy only its own input-scoped GitHub concurrency slot before `preflight-validate` hard-fails; the real safety boundary is that no privileged work, no live-lock mutation, and no admission-state authority exist until canonical project resolution succeeds.
- The checked-in runbooks must include an explicit cross-release-line contention procedure for urgent hotfixes. That procedure may prioritize a mainline or maintenance-line release operationally, but it must do so only by resolving the existing blocked plan through the reviewed abort / clear / recovery paths in this design; bypassing the per-project lock scope is forbidden. The runbook must define a maximum contention-decision time by severity: security-critical or actively exploited fixes must reach an explicit continue-vs-abort decision within 30 minutes, other urgent production hotfixes within 2 hours, and lower-priority maintenance traffic within one business day. The runbook must also name the release-duty incident commander as the owner of that timer and must define the exact criteria for choosing “wait”, “continue existing plan”, or “abort lower-priority plan to unblock the hotfix”.
- GitHub retains at most one running and one pending run per concurrency group. If runs `A`, `B`, and `C` for the same project are queued in close succession, `A` may remain running, `B` may become pending, and a newer `C` may cause GitHub to cancel and replace pending `B`. When that happens, `B` never acquires mutation authority; operators must treat `B` as superseded rather than as a recoverable blocked release, and diagnostics must persist a durable supersession record containing the cancelled run id, planDigest when known, cancellation time, and best-effort superseding run id.
- That durable supersession record must be written by the repository-owned external monitor as the authoritative writer. A surviving run may emit supplemental diagnostics, but the cancelled pending run itself must not be the only writer of the supersession record because GitHub gives it no execution opportunity.
- Supersession records must be persisted to a durable append-only sink that is administratively separate from the live monitor process instance itself. The monitor must also persist a durable scan watermark after each successful poll cycle. When the monitor later resumes after outage or deployment, it must backfill from the last durable watermark rather than from a fixed lookback window; if no watermark is available, it must backfill at least the prior 60 minutes of missed supersession observations into that same sink and mark those entries `recordedDuringRecovery: true`.
- Distinct projects may release in parallel.

### 7.2.1 Required phase-boundary implementation pattern

- GitHub concurrency can be attached at workflow scope or to individual jobs. For the official path, this design now chooses workflow-level `concurrency` on `official.yml` itself because OIDC-backed trusted publishing keys off the direct workflow identity and the design forbids moving official publish jobs behind a reusable-workflow boundary.
- `official.yml` must therefore remain one direct workflow:
  1. the workflow-level `concurrency.group` is `release/<project-key>` only for the exact canonical `workflow_dispatch` input `project-key`; because GitHub evaluates workflow-level concurrency before jobs begin, this design forbids aliases, friendly names, or non-canonical input forms. Empty inputs must be routed to a unique `release-invalid/<run-id>` group that cannot contend with any real project slot. Repositories should also reject syntactically invalid values before dispatch when they provide a wrapper UI or CLI, and any still-dispatched unknown or invalid value must fail in `preflight-validate` before any approval, environment entry, or privileged work continues
  2. `preflight-validate`, `static-analysis`, `official-review-surface`, `baseline-approval-and-audit`, `build-test-package-preparation`, `create-live-lock`, `attestation-verification`, `require-provenance`, `create-release-tag`, every official publish job, `confirm-publish-state`, and `release-complete` all remain direct jobs in `.github/workflows/official.yml`
  3. only the exact official publish jobs that need OIDC receive `id-token: write`
  4. no same-repository reusable workflow may become the authorization boundary for official publish, tag, or lock-clear work
- `buddy.yml` may still use a repository-owned internal mutation worker because buddy publication does not rely on external OIDC workflow identity in the same way official trusted-publishing targets do:
  1. `resolve-context`, `static-analysis`, the selected build job, and `buddy-audit` run without the shared `release/<project-key>` group
  2. only after `buddy-audit` succeeds does `buddy.yml` invoke one repository-owned internal mutation worker that acquires workflow-level `release/<project-key>` concurrency before any buddy publish job enters any buddy environment
  3. that worker must validate that it was called only from `buddy.yml`, must reject any non-canonical `project-key` input before any privileged step, and after acquiring the shared `release/<project-key>` slot must re-read the authoritative official admission entry plus `refs/tags/official-lock/<project-key>` and the §7.6 control-plane suspension record before any buddy environment entry or credential use
  4. that worker rebinds the frozen `buddy-audit` payload and reviewer confirmation as its first publish-job action before any external mutation
- Buddy internal mutation workers are implementation patterns, not extra public release entry points. They must declare only `on: workflow_call`, must never accept raw top-level `workflow_dispatch` inputs as their concurrency key, and must validate allowed callers through both repository-owned call-site allowlisting and a worker-internal hardcoded allowlist of permitted dispatcher workflow paths. Caller-emitted binding data may be compared as an additional consistency check, but it is never authoritative by itself. In this design, the call-site allowlist means only reviewed workflow files in this repository may contain those worker invocations under CODEOWNERS/bootstrap-hash protection; it is not a standalone runtime API proof of caller identity. `github.workflow_ref` identifies the called worker itself and is not sufficient caller proof. If GitHub later exposes a documented caller-workflow metadata field, implementations may compare it as an additional defense, but no undocumented caller context name is normative in this design. Their `project-key` input must already be the canonical dispatcher-emitted key satisfying the full §5.8 contract, and both the caller and the worker must reject the empty string before the workflow-level concurrency key is allowed to become authoritative. The externally exposed release and validation workflows remain `ci.yml`, `buddy.yml`, and `official.yml`; dependency-maintenance workflows such as `renovate.yml` must remain outside release authority.
- Because GitHub evaluates workflow-level concurrency before jobs begin, the design treats any remaining concurrency-slot contention from already-authorized `buddy.yml` or `official.yml` dispatchers as the only accepted residual risk; arbitrary extra callers are forbidden by design.

Illustrative skeleton:

```yaml
name: official
on:
  workflow_dispatch:
    inputs:
      project-key:
        required: true
        type: string
concurrency:
  group: ${{ inputs.project-key != '' && format('release/{0}', inputs.project-key) || format('release-invalid/{0}', github.run_id) }}
  cancel-in-progress: false
jobs:
  preflight-validate:
    # validate canonical project key and fail closed if raw input does not map to one exact project
    ...

  baseline-approval-and-audit:
    needs: [preflight-validate]
    ...

  build-test-package-preparation:
    needs: [baseline-approval-and-audit]
    ...

  create-live-lock:
    needs:
      [preflight-validate, baseline-approval-and-audit, build-test-package-preparation]
    if: ${{ always() && needs.preflight-validate.result == 'success' && needs.preflight-validate.outputs.project-key != '' && needs.baseline-approval-and-audit.result == 'success' && needs.build-test-package-preparation.result == 'success' }}
    ...

  attestation-verification:
    needs: [create-live-lock]
    ...
```

The illustrative skeleton above is still normative about six details: `official.yml` itself owns the workflow-level concurrency group; only canonical non-empty project keys may enter the real `release/<project-key>` namespace while empty inputs must be isolated under `release-invalid/<run-id>`; the dispatch input must already be the exact canonical `project-key` and `preflight-validate` hard-fails any mismatch against the checked-in contract before any privileged job continues; `create-live-lock` occurs before any attestation creation or durable evidence write; approval-pending official runs must be cancelled under the §7.6 degraded-mode rules rather than holding the shared slot indefinitely during monitor outage; and the expected `attestation-verification` skip in `provenance-uncertain` must not cascade into unintended downstream execution.

```yaml
name: _buddy-mutation-worker
on:
  workflow_call:
    inputs: ...
concurrency:
  group: ${{ inputs.project-key != '' && format('release/{0}', inputs.project-key) || format('release-invalid/{0}', github.run_id) }}
  cancel-in-progress: false
jobs:
  validate-caller:
    # verify caller workflow identity and canonical project-key before any privileged step
    ...
```

### 7.3 Idempotency, rerun, and recovery

- Every mutation-stage job (`require-provenance`, `create-release-tag`, each direct publish job, `confirm-publish-state` when it still relies on the live lock, and `release-complete`) must revalidate both the frozen `planDigest` and the frozen `lockInstanceToken`; matching only one of those values is insufficient.
- `create-release-tag` is idempotent only when the existing annotated official tag already points to the same frozen `release-plan.payloadSha`, the tag annotation carries the same frozen `planDigest`, canonical frozen `release-plan` payload, `artifactLocator`, `attestationRef`, and exact subject filename-and-digest bindings, and the protected live lock, if present, carries the same frozen `planDigest` and `lockInstanceToken`; otherwise it is a hard conflict.
- `provenance-uncertain` is a distinct blocked stage used only when operators cannot yet prove whether a durable write completed. Its only approved recovery mode is `reconcile-store`, which performs a read-only store lookup by `planDigest` to reconstruct authoritative `artifactIdentity` before any new publish authorization is granted.
- The design requires one global convergence rule for uncertain targets and uncertain stores: uncertainty may converge only to one of three reviewed outcomes for the same frozen plan—same-identity success, explicit abort/burn, or a more authoritative blocked state with immutable `artifactIdentity`. The workflow must not spin indefinitely on repeated “maybe published” observations.
- A publish target may be treated as a same-identity no-op only when live remote state proves that the already-present version or release artifact corresponds to the current frozen release plan and the authoritative artifact identity for that plan.
- For `github:release`, same-identity proof requires the release to be attached to the expected tag and the full remote asset set to match the authoritative artifact identity exactly by canonical asset name and digest. Missing, extra, renamed, or digest-mismatched assets are conflicts.
- For `npm:official`, same-identity proof requires the exact package name/version plus the published tarball digest to match the authoritative artifact identity. Version-only presence without matching tarball digest is a conflict, not a no-op.
- For `pypi:official`, same-identity proof requires the exact expected distribution filename set (wheel and/or sdist) plus each file digest to match the authoritative artifact identity exactly. Missing, extra, renamed, or digest-mismatched files are conflicts.
- For `rubygems:official`, same-identity proof requires the exact gem version and authoritative gem payload digest to match the authoritative artifact identity. When the registry surface cannot prove that digest, the design must treat the result as uncertain rather than as a same-identity success.
- When RubyGems exposes version presence but cannot provide authoritative digest proof for that version, the release must remain in reviewed blocked state (`post-provenance` when publication may have happened, or `pre-provenance` when no publication happened yet) until another authoritative evidence source resolves identity; the workflow must neither treat version-only presence as success nor attempt a content-changing republish of that same version.
- When any target lacks a documented read-only identity proof strong enough for same-version reconciliation, the repository must treat that target exactly like the RubyGems case above: remain blocked until another authoritative evidence source proves same identity, or record an abort/burn decision. “Version exists somewhere” is never enough to converge an uncertain target to success.
- For `nuget:official`, same-identity proof requires the exact package id/version and authoritative `.nupkg` digest to match the authoritative artifact identity. Version-only presence or metadata-only inspection is insufficient.
- If any official external mutation succeeds but the overall release result is partial, failed, or uncertain, the project must remain blocked in both the live lock and `.github/official-admission-state/<project-key>.json` until a reviewed recovery change records the disposition and evidence for that same frozen release plan.
- A blocked recovery uses exactly the machine-readable mode recorded for that blocked stage. `pre-provenance` recovery reruns the already-frozen plan from `release-plan.payloadSha` only to create the first authoritative durable artifact identity for that plan; after that write succeeds, operators must submit a new reviewed blocked-entry update that records the resulting `artifactIdentity`, any known digest differences from prior failed-run evidence, the intentional-stop reason `pre-provenance-write-completed-awaiting-review`, and the next disposition before any publish-capable recovery is dispatched. `provenance-uncertain` recovery uses `reconcile-store` to query the durable store by `planDigest`; if reconciliation succeeds, operators must submit a new reviewed blocked-entry PR that records the reconstructed `artifactIdentity` and advances the stage before any publish-capable recovery is dispatched. `post-provenance` recovery reuses the previously recorded immutable artifact bundle referenced by `artifactLocator`. `post-confirmation` recovery clears only the residual live lock after re-validating the already-successful publish state. When a `pre-provenance` digest-drift decision is reached before any official tag is created and before any external target mutation has occurred, the required abort path may use the simplified no-external-mutation procedure from §7.5 instead of the full per-registry cleanup matrix.
- `post-provenance` recovery is target-granular, not all-or-nothing. The authoritative per-target decision inputs are the checked-in `targetResults` map plus any persisted confirmation records under the same frozen `planDigest`. A target already recorded as `confirmed` must not be re-published; its recovery path is verification-only. Targets recorded as `not-started`, `failed`, `unknown`, `verification-mismatch`, or `publish-succeeded-unconfirmed` remain eligible for the reviewed `restore-bundle` continuation, but each resumed publish job must re-check the current blocked entry and skip itself when the reviewed target state has already advanced to `confirmed` since dispatch. Mixed states such as “`github:release` already confirmed while `npm:official` remains uncertain” are therefore first-class and must be preserved through recovery instead of being collapsed into a synthetic all-target retry
- `pre-provenance` recovery is a last-resort path. It rebuilds from the same source snapshot, but it does **not** guarantee byte-for-byte identity with the original failed run because runner images, external tooling, or registry-side resolution behavior may have changed. Official release enablement therefore requires pinned toolchain and dependency inputs as described in §4.1, and the workflow must move from `create-live-lock` through `attestation-verification` to `require-provenance` without discretionary delay. Those controls reduce—but do not eliminate—that risk. Recovery approval surfaces, release-status output, and runbooks must display this risk prominently. If no prior-run subject digest evidence exists for the same frozen plan, the rebuild may proceed, but it must emit the structured `riskFlags` marker `no-prior-digest-baseline`, classify the path as high-risk recovery, and require the next reviewed blocked-entry update to preserve that flag before any publish-capable recovery is approved. In that case the rebuilt bundle is only a newly established reviewed artifact identity; it must not be described as proof that the failed earlier run produced equivalent bytes. Any publish-capable continuation after that diagnostic requires the additional independent review defined in §6.4.1. If the rebuilt digest manifest differs from any known prior-run subject digest evidence for the same frozen plan, the workflow must stop, record `digestChangeReason`, and route the plan to the §7.5 break-glass abort path instead of silently continuing.
- Even with concrete GitHub-hosted runner labels such as `windows-2022` or `ubuntu-24.04`, the underlying hosted image may still evolve over time. Repositories that require stronger reproducibility than versioned hosted labels provide must use self-hosted immutable images or an equivalently reviewed image-pin strategy; otherwise that residual rebuild-drift risk remains part of the design.
- If a target registry refuses same-version publication after a differing or uncertain prior attempt, or if `digestChangeReason` records differing rebuilt bytes, the design must route that frozen plan to the break-glass abort path in §7.5 instead of attempting a second content-changing publish.
- A successful recovery run may clear the live lock after `confirm-publish-state` succeeds, but the project is not release-ready again until reviewed checked-in state transitions the project entry back to `ready`.
- Persisted confirmation records from §4.10 are the durable machine-readable proof for advancing a blocked release from `post-provenance` to `post-confirmation`; recovery must not rely only on ephemeral current-run memory when deciding that only `clear-lock-only` remains.
- `post-provenance` may advance to `post-confirmation` only when every target in `targetResults` is either durably proven `confirmed` or has been explicitly retired by the reviewed abort path. One target reaching `confirmed` never authorizes `clear-lock-only` for the whole plan while another target remains `not-started`, `failed`, `unknown`, `verification-mismatch`, or `publish-succeeded-unconfirmed`
- If the durable artifact store becomes permanently unavailable after a `post-provenance` boundary, the design does **not** silently downgrade to rebuild-and-republish the same frozen plan. Operators must either externally prove every target already succeeded and advance to `post-confirmation`, or use the break-glass abort path in §7.5 to burn that frozen plan and version.
- When the durable artifact store read path is unavailable after a `post-provenance` boundary, the official tag annotation is an allowed fallback source for reconstructing the already-recorded release identity anchor (`artifactLocator`, `attestationRef`, and exact subject filename-and-digest map) during read-only diagnosis and blocked-stage classification. That fallback is never an authority to perform a normal publish-path write, and persisted confirmation records are still required before any `post-confirmation` success claim.
- If the durable artifact store becomes permanently unavailable or irreconcilable while the project is still in `provenance-uncertain`, the only legal next state is a reviewed blocked-entry update that records `recovery.approvalState = aborted` and routes the frozen plan to the §7.5 break-glass abort path; the design does not permit an unbounded retry loop in that state. In that combination path, the runbook must treat every selected external target as conservatively suspected-published until contrary evidence exists and must apply the §7.5 burn-only cleanup posture rather than assuming a lighter target-specific cleanup remains safe.

### 7.4 Failure boundary matrix

| Boundary | Expected live lock | Expected admission state | Allowed next mode | Operator action |
| --- | --- | --- | --- | --- |
| approved pre-mutation work failed before `create-live-lock` | absent | no blocked entry required | none in-place; fresh dispatch only | investigate the build/attestation failure, then re-dispatch the workflow and obtain fresh approval because no durable mutation boundary was crossed |
| `create-live-lock` succeeded, but attestation generation/verification or the first durable-write path failed before one authoritative bundle existed | present with frozen `planDigest` | `blockedStage = pre-provenance` with reason chosen from `{attestation-generation-failed, artifact-store-unavailable, artifact-store-digest-mismatch, artifact-store-timeout}` | `rerun-plan` | create reviewed blocked entry with lock payload, evidence, the exact failure reason, and approval mode `rerun-plan` |
| durable write outcome uncertain and authoritative `artifactIdentity` not yet known | present with frozen `planDigest` | `blockedStage = provenance-uncertain` | `reconcile-store` | run read-only store reconciliation by `planDigest`; update the blocked entry before any publish-capable recovery |
| `pre-provenance` rerun produced bytes that differ from prior-run evidence | present with frozen `planDigest` | `blockedStage = pre-provenance` plus non-null `digestChangeReason` and later `recovery.approvalState = aborted` when the abort decision is recorded | none; break-glass abort only | record the exact digest-drift evidence, use the simplified §7.5 no-external-mutation abort path when no tag or external target mutation exists, otherwise use the full cleanup/burn path, and do not authorize `restore-bundle` or any new publish-capable recovery |
| `pre-provenance` rerun returned `create-if-absent.status = already-exists` before one authoritative bundle identity was established for the current recovery | present with frozen `planDigest` | `blockedStage = provenance-uncertain` with `reason = existing-bundle-ownership-ambiguous` | `reconcile-store` | capture the returned bundle metadata, fail closed, and switch to read-only store reconciliation rather than assuming the earlier write is safe to reuse |
| `pre-provenance` rerun created the first authoritative artifact identity and intentionally stopped for reviewed follow-up | present with frozen `planDigest` | `blockedStage = post-provenance` with `reason = pre-provenance-write-completed-awaiting-review` | `restore-bundle` | create reviewed blocked entry carrying exact `artifactIdentity`, any known digest-difference evidence, and authorize `restore-bundle` only after operator review |
| durable artifact write succeeded, but `create-release-tag` failed or conflicted before publish began | present with frozen `planDigest` | `blockedStage = post-provenance` with reason `tag-conflict` or `tag-write-failure` | `restore-bundle` | verify the persisted artifact identity, resolve the tag conflict or tag-write cause, and resume only through `restore-bundle` |
| official tag created, publish incomplete or uncertain | present with frozen `planDigest` | `blockedStage = post-provenance` with reason `publish-job-failure`, `publish-confirmation-failed`, or `publish-confirmation-timeout` as observed | `restore-bundle` | verify official tag annotation plus per-target state, preserve mixed `targetResults`, and rerun only the targets that are not already durably `confirmed` |
| all direct publish jobs completed, but `confirm-publish-state` failed or timed out | present with frozen `planDigest` | `blockedStage = post-provenance` unless reviewed persisted confirmation records plus external evidence upgrade it to `post-confirmation` | `restore-bundle` until full proof exists, then `clear-lock-only` | treat target state as uncertain until persisted confirmation records and external evidence prove every target succeeded; preserve any mixed confirmed/unconfirmed target split in `targetResults`, and advance to `post-confirmation` only after every target is durably resolved |
| a `post-provenance` recovery cannot read the durable bundle from `artifactLocator` because the store is unavailable, times out, or returns a digest mismatch | present with frozen `planDigest` | `blockedStage = post-provenance` with reason `artifact-store-unavailable`, `artifact-store-timeout`, or `artifact-store-digest-mismatch` | `restore-bundle` once the store is healthy again, or `clear-lock-only` only after reviewed evidence proves every target already succeeded | do not mislabel the failure as `pre-provenance`; preserve the already-reached boundary, use official tag annotation as read-only fallback identity evidence when available, and route to abort only through the reviewed §7.5 path |
| `create-live-lock` returned an uncertain result (for example ref-write timeout after request submission) and the repository cannot yet prove whether the lock was created | unknown until reconciled | no immediate new blocked entry until the observed lock state is re-read, then either no blocked entry or the exact stage that was actually reached | none until lock state is re-read | run a bounded lock-stability protocol against one documented GitHub API surface used consistently for that protocol: perform at least 3 re-reads over up to 60 seconds from that same surface, honor `Retry-After`/reset metadata for throttling, require one stable observation set before proceeding, and stop for operator diagnosis if observations remain inconsistent; continue only if the current run can prove it owns the stable lock state |
| `create-live-lock` finds an existing live lock for a different `planDigest`, and that lock payload matches another same-repository reviewed run that is already authoritative for the same `project-key` | present for the competing frozen `planDigest` | no new blocked entry is created by the losing run | none in-place; wait for the authoritative run or blocked-path resolution to finish, then re-dispatch later if still needed | classify the result as `LOCK_HELD_BY_CONCURRENT_RUN`, surface the competing run id/plan or blocked-entry reference when known, and do **not** escalate it as `LOCK_STOLEN` unless later evidence shows the lock was illegitimate |
| `create-live-lock` returns `LOCK_REUSE_REQUIRES_REVIEW` because an earlier same-`planDigest` run left a live lock with no authoritative blocked entry or success record | present with the same frozen `planDigest` | no authoritative blocked entry yet; the next reviewed blocked entry must use the deterministic classifier below | none until classified | treat the lock as an orphaned same-plan boundary, classify it using the same authoritative evidence order as orphan-live-lock handling, then create exactly one reviewed blocked entry at the classified stage before any continuation |
| `LOCK_MISSING` or unreconciled `LOCK_STOLEN` occurs after lock creation in any mutation-stage job | missing, changed, or mismatched without a legitimate same-repository concurrent-run explanation | preserve the last authoritative blocked stage already reached; do not silently downgrade | none until operator diagnosis | stop immediately, capture the observed lock state, determine whether another mutation already occurred, and route either to reviewed state repair or to break-glass if control-plane integrity is in doubt |
| a pending official run is cancelled or replaced by a newer same-project run before it reaches `create-live-lock` | absent | no blocked entry is created | none in-place; fresh dispatch only if still needed after the surviving run finishes | record the durable supersession note with cancelled run id, planDigest when known, cancellation time, and best-effort superseding run id; do not treat the cancelled pending run as a blocked release |
| publish confirmation succeeded, lock clear failed | present with frozen `planDigest` | `blockedStage = post-confirmation` | `clear-lock-only` | do not rebuild or republish; authorize and run the lightweight lock-clear path |
| a `post-confirmation` verification-only run finds a mismatched official tag anchor, conflicting persisted confirmation evidence, or live remote identity that no longer matches the frozen plan | present with frozen `planDigest` | `blockedStage = post-confirmation` with reason `post-confirmation-verification-failed` | none in-place; break-glass repair/abort only until reviewed evidence restores a safe `clear-lock-only` posture | treat this as a tamper-sensitive incident, preserve the lock, do not rebuild or republish, and require a reviewed incident disposition before any later `clear-lock-only` run |
| lock cleared and all targets confirmed | absent | may stay `ready` for a normal success or transition from `blocked` back to `ready` after reviewed evidence | none | normal path ends, or merge the ready-transition PR produced by recovery |

The §7.4 matrix is authoritative for blocked-stage selection and takes precedence over any shorter heuristic elsewhere in the document. If `preflight-validate` encounters an orphan live lock with no matching checked-in blocked entry, it must print the exact lock payload and a blocked-entry template so the operator can create the reviewed state transition without guessing the frozen plan fields. `LOCK_REUSE_REQUIRES_REVIEW` for the same `planDigest` is classified by this same section; there is no separate heuristic path. Any older text that classifies orphans without the official-tag fallback or without the `post-confirmation-verification-failed` terminal verification result is superseded by this section.

Orphan-live-lock classification is deterministic and never uses a newly parsed dispatch plan as its lookup key. The classifier must:

1. read `planDigest`, `lockInstanceToken`, `payloadSha`, `officialTag`, `runId`, and `runAttempt` from the lock payload itself
2. query the durable store by that frozen `planDigest`; if the durable store is unavailable for read-only access, times out, or returns a digest-mismatch read failure, the classifier must immediately fall back to the official tag annotation for release-identity reconstruction rather than prematurely downgrading the stage
3. query the official tag and persisted confirmation evidence for that same frozen plan; when the official tag annotation is present and valid, its carried `artifactLocator`, `attestationRef`, and exact subject filename-and-digest map are authoritative fallback identity evidence for read-only classification when the durable store cannot currently be read
4. classify the orphan as:
   - `pre-provenance` when no authoritative durable bundle exists and no valid official tag annotation can supply the release-identity anchor
   - `provenance-uncertain` when durable evidence exists but cannot yet prove one authoritative immutable `artifactIdentity`
   - `post-provenance` when authoritative `artifactIdentity` exists from either the durable store or a valid official tag annotation fallback but full publish success is not yet proved
   - `post-confirmation` when persisted confirmation evidence proves every target already succeeded and only lock cleanup remains
   - `post-confirmation` with reason `post-confirmation-verification-failed` when the run is already in verification-only territory but the official tag anchor, persisted confirmation evidence, or live target identity conflicts with the frozen plan

The first reviewed blocked entry created for that orphan must copy the frozen lock payload identity into `lockIdentity` exactly, including `planDigest` and `lockInstanceToken`, and must not substitute values recomputed from a newer branch snapshot.

### 7.5 Break-glass process

Break-glass exists only for situations where normal reviewed PR or workflow paths cannot restore safe authority in time.

- the break-glass role is a separately managed human or automation identity that is narrower than normal release dispatch permission and is explicitly listed in the repository contract
- allowed break-glass actions are limited to: clearing a residual live lock; repairing a protected official tag to the documented canonical release-identity anchor; pushing an admission-state-only update when CI is unavailable or when the documented cross-release-line contention deadline cannot be met for a higher-priority hotfix; disabling release enablement for a project; and aborting a frozen release when continuing it would be unsafe
- every break-glass action requires two-person authorization, a linked incident ticket, and a written statement of why the normal reviewed path was unavailable or unsafe
- GitHub Environments on GitHub.com provide only a single approval from the configured reviewer list, not an authenticated “two-of-two” primitive. Therefore the checked-in break-glass environment is an **additional online gate only**. It may require one approval from a dedicated break-glass reviewer list and `prevent self-review`, but it does not by itself satisfy the design’s two-person control requirement.
- the actual two-person technical control is the mandatory split-control custody path recorded in the repository contract. Every break-glass action must require two distinct named custodians from that path to release or reconstruct the break-glass credential or secret material, regardless of whether the optional GitHub environment gate also succeeds.
- the repository contract must predefine one out-of-band split-control path, independent of GitHub workflow execution and environment approval, for cases where GitHub Actions, GitHub environment approval, GitHub App token issuance, ruleset drift, or workflow queue/concurrency behavior prevents the normal path. That fallback must name the custody mechanism, the exact `offlineControlledMaterial`, the named custodians, and the minimum evidence package required before use.
- `assuranceProfile = standard` may use any reviewed custody mechanism from the contract’s closed set; `assuranceProfile = high-assurance` should prefer `hsm-split-control` when the organization already operates it, otherwise `sealed-secret-split-control` with explicit reviewed justification. No profile requires a hardware HSM when the repository does not already operate one.
- every break-glass action must leave an audit trail containing actor identity, authorizer identities, timestamp, affected project key, affected refs/files, linked incident, and the exact before/after state
- within 24 hours, checked-in state on the authoritative protected branch must again become the authoritative record of the project’s release status
- runbook templates must exist at the checked-in `breakGlass.runbookRef` location for at least these scenarios: successful publish with residual lock, partial publish with residual lock, durable artifact store unavailable or corrupted, credential/provider outage that prevents the normal recovery path, GitHub control-plane outage or degraded environment approval, GitHub App or token-issuance failure, ruleset or protection drift that blocks reviewed recovery, stuck queue or stuck per-project concurrency state, unauthorized release or protected-ref mutation outside the workflow path, suspected credential leakage or misuse, suspected durable evidence tampering, and aborting a frozen release
- every backend-specific durable-store runbook must define the orphan-upload reconciliation loop end to end: the explicit finite grace period before an unmatched upload becomes an orphan candidate, the monitoring/scan cadence, the evidence operators must capture before cleanup, and the exact cleanup action for each backend outcome class
- the break-glass runbooks for GitHub control-plane degradation must explicitly identify the named repository administrator/security contacts allowed to invoke the out-of-band path, the evidence required to prove the platform outage or control-plane failure, and the exact sequence for later reconciling the repository back to checked-in authoritative state
- the abort-release runbooks must cover at least these cases: lock exists but no durable write yet; durable write exists but no official tag yet; official tag exists but no publish happened; partially published targets; `provenance-uncertain` plus permanently lost durable-store evidence; and `pre-provenance` digest drift where rebuilt bytes no longer match prior evidence. For partially published targets, the runbook must classify each target as delete-capable, unlist-capable, yank-capable, deprecate-only, or burn-only; point to the documented per-registry cleanup procedure for that class; require explicit operator evidence for each target; and record whether the version is permanently burned.
- The compromise/tamper runbooks must explicitly standardize the immediate freeze-and-forensics path: disable new official and buddy publication for the affected project set, preserve run and provider evidence, revoke or rotate the suspected credential or broker path, verify protected refs and remote target state against the last authoritative release identity, and require a reviewed incident disposition before any publication path is re-enabled.
- the checked-in cleanup matrix starts with these required classifications: `github:release` = delete-capable; buddy GitHub Packages targets `{nuget:gpr, npm:gpr, rubygems:gpr}` = delete-capable; `nuget:official` = unlist-capable; `pypi:official` = yank-capable; `rubygems:official` = yank-capable; `npm:official` = deprecate-only. A project-specific runbook may classify a target more conservatively as burn-only, but never less conservatively than this baseline without a reviewed design update.
- For `pre-provenance` digest drift, the abort runbook must explicitly: capture the prior digest evidence and rebuilt digest manifest side by side; identify any targets already mutated for that frozen plan; record whether each affected version/tag/release must be yanked, deprecated, unlisted, deleted, or permanently burned; update the blocked entry with `digestChangeReason` plus evidence references; require the checked-in state to record `recovery.approvalState = aborted` once the decision is reviewed; and prohibit any publish-capable continuation until the abort decision is recorded. When the drift is discovered before any official tag exists and before any external target mutation occurred, the runbook must use a simplified no-external-mutation abort record: `targetsMutated = none`, no per-registry cleanup classification is required, and the only additional decision is whether the version is reusable or permanently burned.
- For `provenance-uncertain` combined with permanently lost or irreconcilable durable-store evidence, the abort runbook must explicitly take the conservative assumed-published posture: every selected external target is initially classified as `burn-only` until operator evidence proves a less destructive cleanup is actually safe, and the version remains burned unless later authoritative evidence proves no external mutation occurred.
- Minimum exercise cadence is assurance-profile-dependent so the design scales down for standard open-source operation without dropping the control entirely:
  - `assuranceProfile = standard`: annual durable-store restore drill, annual `clear-lock-only` drill or tabletop for projects that support `post-confirmation` recovery, and annual `pre-provenance` digest-drift abort tabletop
  - `assuranceProfile = high-assurance`: quarterly durable-store restore drill, semiannual `clear-lock-only` drill, and annual `pre-provenance` digest-drift abort tabletop
  - the latest successful exercise date, owner, and evidence reference must be recorded in the checked-in runbook or in the readiness record named by `readinessEvidenceRef` before official release remains enabled

### 7.6 Operational diagnostics

The repository design includes one reviewed helper command under `eng/scripts/` named `release-status`. It is a Day 0 implementation prerequisite for official release enablement. It accepts `project-key` and, by default, must aggregate every protected official branch that can authorize that project rather than pretending there is one implicit authoritative branch. Its diagnostic summary therefore contains:

- one per-branch admission-state section for every protected branch that can authorize the selected project, clearly labeled by branch ref
- which branch, if any, currently holds the active live lock, plus live-lock age and full annotation payload
- matching official tag identity when present for each reported branch state
- durable artifact-store status summary, including whether the authoritative bundle exists, whether confirmation records exist, and whether the store currently appears reachable for read-only diagnostics
- pending/running workflow information relevant to the shared mutation-stage concurrency group
- whether a same-project pending run was cancelled or replaced by a newer queued run, plus the best-effort superseding run identifier when that identifier is inferable from current GitHub API/UI evidence; otherwise it must explicitly print `supersedingRunId: unknown` and direct the operator to the Actions UI
- baseline or subordinate environment approval state when an in-flight run is waiting on review
- incident-response fields needed to act without reopening multiple tools: current `entryVersion`, current `evidenceRef`, current `recovery.authorizationRef` when present, latest incident or blocked-entry PR reference, current monitor heartbeat status, current control-plane suspension state, monitor-acknowledgement state when degraded mode is active, next escalation deadline, current SLA state, whether break-glass is now eligible, and the named responder role expected to act next
- recommended next step derived from the current blocked stage and evidence
- a high-visibility warning banner when any reported branch is in `blockedStage = pre-provenance`, stating that rebuilds are not byte-stable and may burn the version
- when called with `--show-digest-drift`, every known prior digest evidence item and the latest rebuilt digest manifest for the frozen plan side by side

`eng/scripts/release-status` must meet this minimum Day 0 interface contract:

- implementation baseline: Python `3.12+` authoritative helper with repository-owned logic and no mutation permissions
- invocation contract: `eng/scripts/release-status <project-key> [--branch <protected-ref>] [--show-digest-drift] [--format text|json]`
- read contract: it may read only checked-in repository files, live GitHub metadata needed for diagnostics, and the configured durable artifact-store metadata; it must not create, modify, delete, or approve anything
- stdout contract:
  - `--format text` prints one stable human-readable summary with named sections for per-branch admission state, live lock, official tag, in-flight runs, and recommended next step
  - `--format json` prints one closed JSON object containing exactly `projectKey`, `requestedBranch`, `multiBranchProject`, `branchSummaries`, `activeLockBranch`, `officialTagPresent`, `artifactStoreReachable`, `confirmationEvidencePresent`, `supersedingRunIdKnown`, `lockAgeSeconds`, `planDigest`, `lockInstanceToken`, `runningRunId`, `runningRunState`, `pendingRunId`, `pendingRunState`, `concurrencySlotOwner`, `latestIncidentRef`, `monitorHealthy`, `monitorLastHeartbeatAt`, `monitorAcknowledgedUntil`, `controlPlaneSuspensionState`, `recommendedResponderRole`, `nextEscalationAt`, `slaState`, `breakGlassEligible`, and `recommendedAction`. Every listed top-level key is mandatory; values use explicit `null` rather than omission whenever the field is not applicable or cannot currently be determined from the available diagnostic surfaces, and `false` is reserved for states that were actually checked and found absent. `officialTagPresent`, `artifactStoreReachable`, and `confirmationEvidencePresent` are aggregate rollups over the branch summaries: `true` means every relevant checked branch or store read that contributes to the requested scope was positively confirmed, `false` means every relevant checked source was positively absent/unreachable in the same direction, and `null` means the helper could not derive one aggregate answer without ambiguity. `branchSummaries` is a closed array of closed objects containing exactly `branchRef`, `admissionState`, `blockedStage`, `entryVersion`, `updatedAt`, `evidenceRef`, `authorizationRef`, `lockPresent`, `officialTagPresent`, `targetResults`, and `recommendedAction`, and every listed branch-summary key is likewise mandatory. When `admissionState = ready`, `blockedStage`, `entryVersion`, `evidenceRef`, `authorizationRef`, and `targetResults` must appear as explicit `null` values rather than being omitted; when lock or tag presence for a branch cannot be confirmed, `lockPresent` and `officialTagPresent` must be explicit `null` rather than omitted. When `targetResults` is non-null, it is the same closed per-target result map described in §6.3 rather than an ad hoc helper-only structure.
- exit codes: `0` success; `2` invalid invocation; `3` unknown project or invalid checked-in contract; `4` partial-diagnostics failure where some external diagnostic surface was unavailable (the tool must still print the partial summary and mark the unavailable section explicitly)

The external credential broker and the external release monitor are external trust roots in this design. They therefore require a checked-in cryptographic integrity commitment, not just an organizational process note:

- `.github/external-control-plane-commitments.json` is a closed-schema manifest in the bootstrap-governance surface. It must contain exactly one commitment entry for `credential-broker` and one for `release-monitor`.
- each entry must pin the control-plane role, the exact covered policy surface, the current commitment digest, the verifier-key reference, and the runtime attestation or commitment-discovery endpoint used to prove which reviewed policy the external service is currently enforcing
- `commitmentDigest` is the canonical `sha256:<64 lowercase hex>` digest of the UTF-8 RFC 8785 / JCS serialization of one closed reviewed service-policy object containing exactly `service`, `policyVersion`, `coveredBehavior`, `verifierKeyRef`, `runtimeClaimShape`, and `artifactSha256`. Changing any covered issuance, cancellation, escalation, suspension, or heartbeat rule therefore requires a reviewed digest change rather than an out-of-band note
- `ci.yml` must validate the manifest schema and signature material, and reviewed bootstrap-governance changes must update that manifest whenever the broker or monitor changes any credential-issuance rule, actor binding, heartbeat sink, cancellation rule, supersession-record rule, approval-suspension rule, or escalation policy covered by the manifest
- every workflow job that requests broker-minted credentials must verify that the broker's advertised runtime commitment digest matches the checked-in pinned digest before it accepts a credential; mismatch is a hard failure before credential use
- the release monitor must emit its current commitment digest in its health/readiness signal and must open an incident immediately if the running digest cannot be proven to match the checked-in manifest

#### 7.6.1 External credential broker for GitHub App-backed GitHub mutations

The target design requires one repository-owned external credential broker for GitHub App-backed GitHub mutations: high-privilege official GitHub mutations (`production-ref-write-<project-key>` and `production-github-<project-key>`) plus buddy `github:release` publication (`buddy-github-<project-key>`). It is separate from the external release monitor, even when both run on the same organization-managed platform.

- the broker must run on organization-managed compute outside repository release workflows (for example a reviewed internal service, serverless function, or scheduled/long-lived control-plane component)
- the broker’s GitHub-side authority must be limited to minting the exact short-lived GitHub App installation tokens needed for the documented protected-ref writer, official GitHub Release publisher, and buddy GitHub Release publisher actors; it must not hold broader repository mutation authority than those actors already require
- the workflow-to-broker request path must use short-lived authenticated caller proof rather than a long-lived shared secret in the workflow. The normal design is GitHub OIDC plus environment gating, or an equivalently reviewed short-lived broker-auth mechanism
- the broker request contract must bind at minimum: repository, workflow path, job name, run id, run attempt, project key, required environment name, and the requested actor class. This eight-field tuple is the authoritative minimum broker validation contract for both §4.8 and §4.9. Requests missing any of those fields, or whose values do not match the checked-in contract for the run, are hard failures
- the broker must mint only short-lived installation tokens or equivalent short-lived credentials, must log the bound request tuple plus issued actor class, and must never return long-lived key material to the workflow
- the broker must write one durable append-only audit record for every issuance, denial, suspension, duplicate-request result, and malformed-request failure. Those records must land in a sink separate from process-local memory or ephemeral disk and must be retained for at least 400 days so incident review can reconstruct who requested which actor class and why the broker responded as it did
- the broker must expose one reviewed health/readiness signal and participate in the same deadman/heartbeat monitoring regime as the external release monitor. Broker outage is not a silent condition
- the broker must have an explicit degraded-mode rule: when the broker is unavailable, official publish/ref-write jobs fail closed before credential minting. There is no normal-path fallback to directly reading a long-lived GitHub App private key from the branch-scoped environment. Any emergency bypass uses only the §7.5 break-glass path
- the broker must consult the §7.6.2 repository-owned control-plane suspension record before minting any GitHub App-backed credential. When that record has `status = active` for the requested path, the broker must return a closed `suspended` response unless the request matches one explicit unexpired break-glass exception plan recorded in that same suspension state
- the broker’s own bootstrap credential or signing key must be covered by the checked-in break-glass split-control record when that material would let an operator mint equivalent high-privilege release credentials outside the normal path
- `assuranceProfile = high-assurance` projects must use an active-standby broker deployment or an equivalently reviewed redundant architecture; `standard` projects may use a singleton broker only when the outage mode, manual recovery procedure, and reviewed monthly availability objective of at least `99.5%` are explicitly documented in the runbook
- the broker response must be a closed object that includes the credential material, an explicit RFC 3339 UTC `expiresAt`, the bound request tuple, the issued actor class, and the runtime commitment digest that justified the issuance
- broker-minted credentials must be short-lived by policy: normal-path GitHub mutation credentials must expire no later than 15 minutes after issuance, and the workflow must fail closed if fewer than 5 minutes remain when the credential is first received
- broker issuance must be idempotent for the tuple `(repository, workflow path, job name, run id, run attempt, project key, required environment name, requested actor class)` while an unexpired credential already exists; duplicate same-tuple requests must return the same still-valid issuance record or a closed duplicate-request error, never silently mint a second unrelated credential
- malformed, truncated, or immediately expired broker responses are hard failures. The workflow must not attempt credential fallback, must not reinterpret partial responses as success, and must preserve the broker correlation identifiers for incident diagnostics
- the broker must enforce explicit backpressure behavior: when rate limits or upstream GitHub App limits are near exhaustion, it must return a closed retryable-overload response with retry metadata rather than timing out indefinitely or partially issuing credentials
- the broker path must have a reviewed latency budget. Normal-path credential issuance is expected to complete within 10 seconds at p95 and must fail closed at 30 seconds wall-clock so downstream confirmation budgets and publish jobs do not hang indefinitely on a partial broker failure

#### 7.6.2 Control-plane suspension record contract

The control-plane suspension record is a first-class repository protocol, not an informal note in an incident ticket. It is the shared fail-closed authorization surface used by `baseline-approval-and-audit`, the buddy mutation worker, the external credential broker, `eng/scripts/release-status`, and the §7.5 break-glass path.

- storage location: the repository must keep one current closed JSON record at the durable repository-owned locator `artifact://control-plane/suspension/current.json`, plus one append-only history record per state change under `artifact://control-plane/suspension/history/`. For v1, `artifact://control-plane/...` is not an abstract placeholder: it must resolve to a dedicated primary-endpoint Azure Blob Storage container/prefix with strong primary consistency, blob versioning or equivalent immutable history for the append-only records, and optimistic-concurrency writes on the current record via ETag or an equivalent compare-and-swap primitive. To credibly claim the §7.6 control-plane targets of `RPO <= 15 minutes` and `RTO <= 60 minutes`, that Azure-backed surface must also have a reviewed disaster-recovery posture that includes all of: a secondary-region or independent-copy strategy outside the primary failure domain, immutable history retained through failover or reconstructable from exports no older than 15 minutes, and a rehearsed procedure that restores read-only reconstruction plus current-record write authority within 60 minutes. This Azure requirement is a v1 scoping choice, not a permanent multi-cloud exclusion: a later reviewed design revision may approve another backend only if it provides the same repository-owned durability, immutable history, compare-and-swap semantics, and DR posture. Process-local memory, ephemeral disk, and untracked chat/ticket state are never authoritative
- current-record schema: the current record is a closed object with exactly `schemaVersion`, `recordVersion`, `status`, `reason`, `activatedAt`, `activatedBy`, `monitorLastHeartbeatAt`, `acknowledgedAt`, `acknowledgedBy`, `reCheckDeadline`, `clearedAt`, `clearedBy`, `writerCommitmentDigest`, `historySha256`, and `exceptionPlans`. `schemaVersion` must equal `1`; `recordVersion` is a monotonically increasing integer; `status` is exactly `active` or `cleared`; `reason` is one of `{monitor-heartbeat-missing, operator-forced-suspension, broker-integrity-mismatch, policy-drift, other-reviewed-control-plane-incident}`; nullable timestamps use explicit `null` rather than omission when not applicable
- exception-plan schema: `exceptionPlans` is a closed array of closed objects containing exactly `runId`, `planDigest`, `scope`, `authorizationRef`, `authorizedBy`, `authorizedAt`, and `expiresAt`. `scope` is one of `{official-approval-continuation, buddy-mutation-entry, broker-credential-mint}`. An exception matches only when all of `runId`, `planDigest`, and `scope` match the current request and `expiresAt` has not passed
- write authority: only the external monitor or its documented standby may create normal-path monitor-outage records, acknowledgements, and clear operations; only the documented §7.5 break-glass actor may create operator-forced suspension records or add exception plans. Every write must read the latest `recordVersion` first and fail on stale-base updates so concurrent writers cannot silently overwrite one another
- concurrency semantics: updates are optimistic-concurrency writes against `recordVersion`. Writers that lose the race must re-read the current record and re-apply their intended change explicitly. Last-writer-wins without version checks is forbidden
- binding semantics: exception plans are bound to one frozen run and one frozen plan. A re-dispatch, a new `run_id`, or a different `planDigest` requires a new exception plan; a generic project-level exemption is invalid
- expiry and clearing: a record may return to `status = cleared` only after the monitor has confirmed healthy heartbeat continuity for at least one full poll interval and has completed the required recovery scan/backfill from the durable watermark. Cleared current records and append-only history records must be retained for at least 400 days for audit and post-incident review. The underlying blob backend for this control-plane record must be operated with repository-owned backup / disaster-recovery configuration whose documented targets are at least `RPO <= 15 minutes` and `RTO <= 60 minutes`; the runbook must identify the exact replication or export mechanism, the failover decision authority, and the step that re-establishes compare-and-swap writes on the recovered current record. If the primary region is unavailable and that SLA cannot currently be met, readers must fail closed and escalate rather than silently bypassing suspension-state enforcement
- integrity model: each history record must be immutable, must carry the checked-in runtime commitment digest of the writer (`writerCommitmentDigest`), and must hash-chain to the prior history record. `historySha256` in the current record points to the latest append-only history object. Readers must fail closed if the current record is missing, malformed, references a missing history object, or carries a `writerCommitmentDigest` that does not match `.github/external-control-plane-commitments.json` for the actor that wrote it
- reader behavior: `baseline-approval-and-audit`, the buddy mutation worker, and the broker must all re-read the current record immediately before the action they gate. When `status = active` and there is no matching unexpired exception plan, they must fail closed with a diagnostic that names the suspension reason, `recordVersion`, and `reCheckDeadline`
- `eng/scripts/release-status` must report the exact `status`, `reason`, `recordVersion`, current exception-plan summary, and whether the current run would match any still-valid exception plan

Day 0 bootstrap procedure for the external monitor and suspension record is part of the design:

1. deploy the broker and monitor binaries/configuration from one reviewed service-policy package per control-plane role, compute each role’s `commitmentDigest` from that reviewed package by the rule above, and publish the corresponding runtime commitment endpoint before any repository branch is marked ready for official release
2. create the durable suspension-store container/prefix and write the initial `current.json` plus first history record in `status = active` with `reason = operator-forced-suspension`, no exception plans, and the writer’s checked-in `writerCommitmentDigest`; official release enablement stays blocked while the control plane is still bootstrapping
3. merge the reviewed `.github/external-control-plane-commitments.json`, repository contract, runbooks, and any release-enablement branch changes together so bootstrap governance covers the exact digests and verifier-key references the workflows will trust
4. start the release monitor in bootstrap mode. Before it may clear suspension, it must prove that its own runtime commitment digest matches the checked-in digest, confirm the broker reports the checked-in digest, perform one full heartbeat/poll cycle against GitHub and the suspension backend, and record that first healthy observation in durable history
5. only after step 4 succeeds may the monitor clear the suspension record to `status = cleared`; until then, all brokered credential minting and approval-bearing release entry remains fail-closed. Any later monitor or broker redeploy repeats this reviewed digest-and-runtime proof flow before normal release traffic resumes

Operational observability is a required part of the design, not an implementation afterthought. The repository must define monitored signals and alerting for at least:

Required collection architecture:

- one repository-owned external release monitor, independent of `buddy.yml` and `official.yml`, must run on organization-managed compute outside the repository’s own release workflows (for example an organization scheduler plus container/VM/serverless job) and must poll GitHub metadata, checked-in protected-branch state, and durable-store metadata at least every 5 minutes for active release-state, live-lock, concurrency, and artifact-store signals
- `assuranceProfile = high-assurance` projects must use an active-standby release-monitor design or an equivalent reviewed redundancy strategy; `assuranceProfile = standard` projects may run a singleton monitor only when the runbook explicitly documents the accepted outage mode, reviewed off-hours degraded-mode coverage, recovery procedure, and reviewed monthly availability objective of at least `99.5%`
- that same external monitor, or a sibling repository-owned external monitor, must also run for provider-review freshness checks and best-effort `workflow-only` provider-drift probes; provider freshness must not depend on a release workflow self-reporting its own status. The maximum probe interval is 24 hours for `standard` projects and 1 hour for `high-assurance` projects when a target is both `workflow-only` and `providerSupportsReadOnlyInspection = false`
- the external monitor’s GitHub authentication must use a dedicated GitHub App installation token or equivalently narrow brokered credential that is read-mostly: metadata read, actions/deployments read, contents read for protected branches, and issue/incident artifact write only as needed to record alerts and durable supersession notes. It must not hold publish, protected-ref-write, or release-mutation permissions, and its own credentials must rotate on a reviewed cadence no longer than 90 days
- the external monitor’s durable-store access must use only read-only credentials scoped to the store metadata required for diagnostics and reconciliation; it must not share the write credential used by `require-provenance`
- the external monitor is the authoritative writer for supersession notes, stale-state incident annotations, monitor-health incidents, and approval-timeout cancellations because cancelled or replaced runs cannot be relied upon to write their own terminal status
- the external monitor's supersession-note and stale-state artifacts must land in a durable append-only sink that is distinct from the monitor's process-local memory or ephemeral disk; monitor restarts or instance loss must not erase already-written diagnostics
- when an official run is waiting on the baseline environment approval gate, the external monitor must track the wait age against the project's checked-in `approvalWaitMaxSeconds`. If that bound is exceeded, the monitor must cancel the run, annotate the incident as `approval-timeout-abandoned`, and record the cancelled run id plus the freed concurrency group. GitHub job timeout is only a backstop for this case, not the primary abandonment mechanism; when monitor outage prevents that automatic cancellation, the degraded-mode procedure below becomes the mandatory manual backstop
- the external monitor must emit a deadman heartbeat at least every 5 minutes to an independent monitored sink whose availability target and deployment procedure are documented in the runbook; routine monitor deployments must not create more than 5 minutes of heartbeat gap. The runbook must also document the exact Azure Blob account/container selection, replication mode, backup/export procedure, and the read-only recovery procedure for reconstructing `artifact://control-plane/suspension/current.json` plus history after backend loss or regional failover
- the minimum acceptable deployment shape is assurance-sensitive. For `assuranceProfile = standard`, a singleton monitor is acceptable only when it persists scan watermarks, supersession notes, and suspension-record writes to durable storage outside process memory, has a documented monthly availability objective of at least `99.5%`, and also has reviewed off-hours coverage for degraded-mode enforcement: either a 24x7 named on-call owner or an independently scheduled standby automation that can still execute the required cancellation/escalation actions when the primary monitor is down. Repositories that cannot meet that off-hours coverage must keep official release disabled outside staffed windows. For `assuranceProfile = high-assurance`, the monitor must use at least two failure-independent executors in active-standby or equivalently reviewed redundant form, share the same durable watermark/suspension state, and be able to continue heartbeat emission plus degraded-mode enforcement after loss of any one executor or planned deployment
- missing heartbeats older than 10 minutes must page the release owner. The design must not rely on a 24-hour or 26-hour window to discover monitor outage
- the repository must define a monitoring-degraded mode for external-monitor outage. The §7.6.2 repository-owned durable control-plane suspension record is mandatory and must be administratively independent from monitor process memory. When heartbeat age exceeds 15 minutes, open or update a monitor-outage incident, page the on-call release owner, and set the suspension record to `status = active` for new official approval-bearing continuations and new buddy mutation-worker entries unless it is already active. Human acknowledgement may tighten escalation or add one explicitly bounded break-glass exception plan, but it must not defer that 15-minute fail-closed suspension. `baseline-approval-and-audit`, the buddy mutation worker, and the external credential broker must all read that suspension record and fail closed when it blocks their path. The same degraded-mode procedure must also enumerate any already-running official workflows that are still pending baseline approval; unless one explicit break-glass exception names that run and plan digest, a surviving monitor/standby or the documented off-hours responder must cancel each such run within 15 minutes after suspension activates so it cannot hold `release/<project-key>` indefinitely. A `standard` singleton deployment may satisfy this only when the reviewed off-hours coverage above exists; a `high-assurance` deployment must satisfy it with the redundant monitor shape above rather than depending on one live process
- when degraded mode clears, the monitor must perform a recovery scan from the last durable scan watermark rather than from a fixed lookback window. If no watermark is available, it must cover the entire interval since the last known healthy poll or, if that cannot be proved, at least the prior 24 hours. It must backfill any missed supersession notes or equivalent monitor-authored status artifacts with `recordedDuringRecovery: true`
- alerts, supersession records, and stale-state incident artifacts must be derived from those external reads, not from assuming a cancelled, timed-out, or half-completed release run will successfully write its own final status
- `eng/scripts/release-status` is the on-demand operator view over the same authoritative sources; release workflows may emit supplemental summaries, but monitoring and paging authority stays outside the release entry workflows

- count of blocked projects
- live-lock age by project
- approval-to-`create-live-lock` delay
- active official-run age while holding the shared concurrency slot
- baseline-approval wait age, pending shared-slot queue age, and runner-start delay for release jobs that have not yet reached mutation
- confirmation retry counts, timeout counts, and terminal conflict counts by target
- artifact-store write error rate, read error rate, read-back verification failures, commit-marker / bundle divergence, persisted-confirmation write failures, orphan-upload count (defined as currently visible unmatched uploads older than the backend's documented grace period), and remaining capacity or quota by backend
- durable-store restore drill freshness and configured backup / replication freshness against the declared RTO/RPO policy
- stale `providerConfigReviewedAt` records for OIDC-backed targets
- `providerConfigReviewedAt` records that will become stale within the applicable warning window for OIDC-backed targets whose `providerSupportsReadOnlyInspection = false`
- `workflow-only` provider-drift probe outcomes from the closed set `{match, drift-detected, inspection-unsupported, inspection-unavailable, inspection-error}`
- ready-transition PRs that remain unmerged past the documented SLA
- scheduler or monitor missed-run age for the required external monitoring jobs themselves
- cross-project runner-queue delay and release-provider rate-limit saturation

Required alert thresholds are severity-tiered rather than one-size-fits-all. These SLAs measure acknowledgement, triage, escalation, and controlled-state-transition deadlines, not full recovery completion or PR-merge completion:

- **Tier 0 — pre-mutation queueing / approval wait**
  - baseline approval pending longer than `baselineWaitTimerMinutes + 15` minutes: warn
  - pending for the shared `release/<project-key>` slot longer than 15 minutes with no active mutation-stage owner: warn
  - runner-start delay for a release job longer than 10 minutes after scheduling: warn
  - any of the above older than 60 minutes: page
- **Tier 0.5 — external monitor degradation**
  - heartbeat older than 10 minutes: page the release owner and open/update the monitor-health incident
- heartbeat older than 15 minutes: the §7.6.2 suspension record must already be `active`; any other state is a control-plane integrity incident
- any pending baseline-approval run not cancelled within 15 minutes after suspension activation and not covered by one explicit break-glass exception: immediate page and escalation to the release-duty incident commander
- heartbeat older than 45 minutes: hard suspension must still be active for new official approvals and buddy mutation entries; if not, escalate as a control-plane integrity incident
  - suspension still active after 24 hours: management-visible escalation
- **Tier 1 — blocked or live-lock state with no known external mutation yet** (`pre-provenance`)
  - older than 15 minutes: warn
  - older than 4 hours: page the on-call release owner
  - older than 12 hours: require named human triage and incident annotation
  - older than 24 hours: evaluate break-glass feasibility explicitly
  - older than 48 hours: SLA breach requiring management-visible escalation
- **Tier 2 — uncertain or partially mutated state** (`provenance-uncertain` or `post-provenance`, or any state with possible external mutation)
  - older than 5 minutes: warn
  - older than 30 minutes: page the on-call release owner
  - older than 2 hours: require named human triage and incident annotation
  - older than 6 hours: evaluate break-glass feasibility explicitly
  - older than 24 hours: SLA breach requiring management-visible escalation
- **Tier 3 — customer-visible or tamper-sensitive state** (`post-confirmation` residual lock, suspected credential compromise, unauthorized ref/release mutation, or suspected durable evidence tampering)
  - immediate incident open plus page
  - named human triage within 30 minutes
  - break-glass evaluation within 2 hours if normal clearance is still unavailable
  - management-visible escalation within 12 hours if unresolved

Issue automation and diagnostics must support those signals by applying consistent labels/assignees to blocked incidents, emitting machine-readable lock age, distinguishing normal verification-only recovery from control-plane failure, distinguishing `LOCK_HELD_BY_CONCURRENT_RUN` from unreconciled `LOCK_STOLEN`, and preserving durable supersession notes for cancelled pending runs.

The operator runbook must explicitly cover GitHub's latest-wins pending-run replacement behavior for the shared mutation-stage concurrency group. When run `A` is running, run `B` is pending, and newer run `C` causes GitHub to cancel and replace pending `B`, operators must treat `B` as superseded work rather than as a blocked release: confirm from the Actions UI or API that `B` concluded as cancelled, inspect whether `A` or `C` now owns the shared slot, and re-dispatch only if the repository still needs `B`'s frozen plan after the surviving run finishes. SLA tracking for the cancelled run stops at cancellation; any later work starts a new run and new approval lifecycle.

Provider-review freshness alerting is mandatory, not optional:

- for OIDC-backed targets with `providerSupportsReadOnlyInspection = false` that rely only on the 365-day outer bound: warning alert at 30 days before expiry and blocking-severity alert at 7 days before expiry
- for `workflow-only` targets on `assuranceProfile = standard`: warning alert at 48 hours before the 7-day limit and blocking-severity alert at 24 hours before that limit
- for `workflow-only` targets on `assuranceProfile = high-assurance`: warning alert at 8 hours before the 24-hour limit and blocking-severity alert at 2 hours before that limit
- after the applicable limit is exceeded: release-time hard failure plus incident annotation until the reviewed record is refreshed
- any OIDC-backed target whose `providerConfigReviewedAt` is already older than 365 days is configuration-invalid even when no stricter `workflow-only` limit applies; `ci.yml` and release-time validation must both fail it closed

Emergency override for the applicable stricter `workflow-only` provider-review freshness limit exists only through the §7.5 break-glass path and only for targets with `providerSupportsReadOnlyInspection = false`. That override is single-use for one frozen release plan, must record an incident ticket plus explicit evidence that `providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerAudience`, and `allowedRefClaims` still match the last reviewed contract, and must not be used to authorize any change to those trust inputs. The repository must refresh the normal reviewed provider verification record within 24 hours after that emergency release or keep the target disabled.

Because this design exposes only `ci.yml`, `buddy.yml`, and `official.yml` as release and validation top-level workflows, provider-review freshness alerting must not rely on inventing an undocumented extra release-entry workflow later. The required mechanism is:

- one reviewed repository-owned provider-freshness monitor, outside the release entry workflows (for example an organization scheduler or external repository monitor), owned by the release-duty incident commander rotation, that runs at least once every 24 hours for `standard` projects and at least once every hour for `high-assurance` projects whose target is both `workflow-only` and `providerSupportsReadOnlyInspection = false`, still participates in the <45 minute deadman heartbeat regime above when monitor outage has been acknowledged, alerts if its schedule slips by more than 2 hours for the daily cadence or by more than 10 minutes for the hourly cadence, reads `.github/repository-release-contract.json` from every protected branch that enables official release for any project, and evaluates every OIDC-backed target with `providerSupportsReadOnlyInspection = false`
- for every `workflow-only` target, that monitor must also attempt a best-effort provider-side read-only drift probe and record one closed outcome from `{match, drift-detected, inspection-unsupported, inspection-unavailable, inspection-error}` even when the checked-in support flag remains `false`; if the latest outcome is `inspection-unavailable` or `inspection-error`, the next release for that target must require a fresh reviewed provider verification rather than relying on stale monitor coverage
- that same monitor must verify that every non-null `providerConfigReviewRef.locator` for those targets remains reachable and that the fetched bytes still hash to `evidenceSha256`; a missing or mismatched evidence artifact is a hard incident because bootstrap hashing intentionally does not authenticate those fields
- that monitor must open or update one labeled repository incident/notification artifact when a target enters the applicable pre-expiry warning or blocking window for its actual freshness limit, when a best-effort drift probe returns `drift-detected`, or when probe coverage is unavailable for an acknowledged reason; it must preserve the target key, provider key, current `providerConfigReviewedAt`, deadline timestamp, review-evidence reference, and latest probe outcome
- when a protected branch is temporarily unreachable or monitoring data is incomplete, that monitor must fail noisy, annotate the affected branch/project set, and page the release owner instead of silently skipping coverage
- `ci.yml` must also warn when a PR leaves any such target inside its applicable warning window and must fail when a PR would merge already-expired review metadata or a `providerConfigReviewRef` whose evidence locator is unreadable or hash-mismatched on the reviewed evidence surface
- `preflight-validate` remains the authoritative release-time hard stop after the applicable provider-review freshness limit is exceeded

## 8. Shared Workflow Rules

- Build/test reusable workflows and attestation reusable workflows must declare their own minimal `permissions:` upper bounds rather than omitting `permissions:` entirely. For normal build/test paths that means `contents: read` only, with no `id-token: write` and no write-scoped package or contents permissions unless a documented design exception requires them.
- Build/test and attestation reusable workflows must be called with `secrets: {}`. The explicit empty mapping is required so review can see that no caller secrets are being inherited; `secrets: inherit` is forbidden for these reusable calls, and omitting the key is not the approved style for this design.
- Official publish jobs are direct jobs, not reusable-workflow hops.
- Attestation reusable workflows are not publish-capable authorization boundaries. A reusable `_attest-build-*.yml` helper may hold `id-token: write` only for attestation generation/verification under a direct `official.yml` job that already passed the required lock/identity checks; it must not mint publish credentials, mutate protected refs, or publish to external registries.
- Build/test/package reusable workflow runner selection is fixed by `buildKind`: `csharp-pack` on `windows-2022`; `python-package`, `node-npm`, `node-wxt`, and `ruby-gem` on `ubuntu-24.04`.
- Every `actions/checkout` invocation in `buddy.yml`, `official.yml`, and their reusable workflows must set `persist-credentials: false`. If a later step truly needs authenticated `git` access, it must re-authenticate explicitly for that step instead of inheriting persisted checkout credentials.
- Shell steps must treat workflow inputs and derived values as untrusted: map through `env:` first, then reference quoted variables.
- Raw `${{ ... }}` interpolation inside shell scripts is forbidden. Workflow expressions must be mapped into environment variables or explicit action inputs before shell execution.
- `eval`, untrusted `bash -c`, and sourcing any shell content that comes from payload-controlled files, workflow inputs, or other untrusted data are forbidden.
- Writes to `GITHUB_OUTPUT`, `GITHUB_ENV`, and `GITHUB_STEP_SUMMARY` must use the documented file-append form with trusted keys, trusted here-doc delimiters, and delimiter values that cannot be influenced by untrusted content. Approval-related reviewer text written to `GITHUB_STEP_SUMMARY` is allowed only from `buddy-audit` and `official-review-surface`, and only when derived from already-validated frozen outputs. For those summaries, single-line dynamic values must be rendered as inline code, multi-line or structured values must be rendered inside fenced code blocks, and raw Markdown-significant text from dynamic values is forbidden. If a value must appear outside code formatting, the workflow must escape Markdown metacharacters at minimum for backtick, backslash, pipe, asterisk, underscore, hash, angle-bracket, bracket, parenthesis, exclamation-mark, and hyphen-plus-space/task-list sequences so untrusted/package-derived text cannot change heading structure, table layout, task-list state, links, images, or raw-HTML rendering.
- All non-local third-party actions outside the GitHub-maintained `actions/` organization must be pinned to a full commit SHA. First-party `actions/*` references may use reviewed version tags and do not require SHA pinning.
- All jobs that rely on NBGV or other git-history-derived metadata must use full history.
- Permission grants default to `permissions: {}` at workflow level, with job-level least-privilege escalation.
- `buddy-audit` must declare its own explicit job-level permissions: `contents: read`, `deployments: write`, and `pull-requests: write` are required; `actions: read` is allowed only when the implementation reads Actions-run metadata beyond the default artifact upload path.
- `baseline-approval-and-audit` must declare its own explicit job-level permissions. `actions: read` is mandatory because the job reads the documented workflow-run approvals and pending-deployments APIs. Any additional permission such as `contents: read` must be justified by another explicit step in that same job; there is no blanket exception to the least-privilege rule.
- `id-token: write` must appear only on the publish job that actually needs GitHub OIDC for trusted publishing or provenance; it must not be granted at workflow scope or to build/test jobs.
- When a release workflow freezes distinct control-plane and payload SHAs, it must check them out into distinct fixed paths such as `control-root/` and `payload-root/`.
- Local composite actions, helper scripts, and other workflow-owned control-plane code must execute only from the control checkout.
- Project build/test/package commands may read payload files only from the payload checkout, and any file that influences project resolution, version resolution, dependency resolution, build, package, or artifact selection belongs to the payload checkout even when it lives at repository root.
- Jobs must not re-resolve the selected protected dispatch branch into a new HEAD after `preflight-validate`; they must consume the emitted frozen values only.

## 9. Summary of Key Design Properties

- The only externally exposed release and release-authority validation workflows are `ci.yml`, `buddy.yml`, and `official.yml`; `.github/workflows/codeql.yml` is allowed as non-release security analysis without release authority, publish credentials, protected-ref bypass credentials, or release mutation worker access; scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflows are allowed only without release authority.
- `.github/repository-release-contract.json` plus per-project files under `.github/official-admission-state/` are the checked-in machine-readable source of truth for repository-side release prerequisites, admission/recovery authority, buddy/official environment contracts, durable artifact-store contracts, and target-auth contracts, and `ci.yml` validates them with strict duplicate-key rejection.
- Buddy publish authorization stays in direct jobs rooted in the dispatch-selected snapshot plus pre-created protected buddy environments; it does not rely on same-repository reusable publish workflows or ambient credentials outside the documented target auth contract.
- Buddy runs fail closed when the selected project is blocked by official admission state or a live official lock.
- `production-<project-key>` is the authoritative human approval gate and must include required reviewers, prevent-self-review, deployment-branch restriction, and a bounded wait timer.
- Official target auth contracts are closed-schema objects with workflow-enforced exact OIDC ref claims, recorded provider support facts, provider trust capability sets, machine-readable `workflow-only` rationale when exact provider ref enforcement is unavailable, target-specific confirmation retry settings, and broker-backed high-privilege GitHub mutation for the official GitHub/API path.
- Recovery resumes only the already-frozen blocked release plan rather than recomputing release identity from a newer branch snapshot. Blocked-entry approval is bound to `(entryVersion, blockedStage, planDigest)`, `pre-provenance` recovery is a last-resort rebuild that stops after creating the first authoritative durable artifact identity for reviewed digest confirmation, `provenance-uncertain` recovery reconciles durable-store facts without publishing, `post-provenance` recovery reuses the persisted immutable artifact bundle, and `post-confirmation` recovery clears only a residual lock after confirming the already-successful publish state.
- Successful official releases keep a durable canonical release-identity anchor on the annotated official release tag even after the live lock is cleared, and that tag annotation is also the documented read-only fallback release-identity source when durable-store reads are temporarily unavailable during classification or recovery. The durable artifact store still remains the authoritative source for blocked and successful bundles plus per-target confirmation evidence for the documented retention periods.
- The design includes a reviewed `eng/scripts/release-status` operational helper plus a repository-owned external release monitor for blocked-project diagnostics, durable supersession notes, provider-freshness alerting, and monitor-health enforcement.
- Active projects use the canonical roots under `src/`, `src/lab/`, and `tests/`; the former `OneDotNet/` subtree has been migrated, but release pipelines are still not set up.
