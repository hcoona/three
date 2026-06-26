# GitHub Workflows Design (v2.50)

<!-- markdownlint-disable MD013 MD028 MD029 -->

This document describes the target GitHub Actions release design for the `three` monorepo.

> **Current repository layout:** Active projects now use the canonical monorepo roots under `src/`, `src/lab/`, and `tests/`; the former `OneDotNet/` subtree has been migrated into those canonical roots. Release workflows are already active; the remaining gap called out below is deferred NuGet registry publication.

> **Scope constraint:** Each releaseable project maps to exactly one language ecosystem and exactly one checked-in `buildKind`. Multi-language or multi-build-kind projects are out of scope.

> **Release-unit constraint:** Each `buddy.yml` or `official.yml` run releases exactly one project.

> **Identity split:** The active manual workflow inputs are `project`, `version`, optional `target`, and `force_update_tag`. `project` must resolve to exactly one canonical internal project identity before release planning, while `packageIdentity` remains the external package identifier and must never be derived by normalizing a workflow input.

The active release-authoring authority is the checked-in descriptor/catalog pair: `src/**/three.release.yml` descriptors plus `eng/release/target-instances.yml`. Older control-plane files such as `.github/repository-release-contract.json`, `.github/external-control-plane-commitments.json`, and `.github/official-admission-state/**` are superseded/future-only unless a later reviewed design reintroduces them.

Active release workflow implementation is already organized around reviewed workflow files, `src/**/three.release.yml` descriptors, and `eng/release/target-instances.yml`. The older Day 0 helper set (`compute-bootstrap-hash`, `jcs-canonicalize`, `create-blocked-entry`, `release-status`, and `compute-build-time-p95`) is historical/future-only unless a later reviewed design reintroduces that control-plane model.

This document keeps the current active design plus explicitly marked historical/future-only material. Superseded alternatives are not compatibility paths unless a later reviewed design reintroduces them.

> **Active split-topology override:** The active repository workflow topology has moved past the direct `official.yml` OIDC publishing shape described by older v2 sections. Current live package-registry publication is orchestrated by `.github/workflows/release-orchestrate.yml`, which dispatches split token-minting / publish paths for PyPI (`pypi/pypi`), npmjs (`npm/npmjs`), and RubyGems.org (`rubygems/rubygems-org`). NuGet registry publication is deferred: `eng/release/target-instances.yml` keeps `families.nuget.instances: []`, so `nuget:gpr`, `nuget:official`, `nuget/nuget-org`, and `nuget/github-packages` are reserved historical/future names only until a reviewed dotnet/NuGet workflow path and target catalog entries exist.

## 1. Architecture Overview

The externally exposed release and release-authority validation entry workflows are:

- `ci.yml`
- `buddy.yml`
- `official.yml`

`.github/workflows/release-buddy.yml` and
`.github/workflows/release-official.yml` are non-active compatibility /
dispatch-registration stubs. They intentionally remain manually dispatchable so
GitHub keeps the legacy dispatch surfaces registered, but they fail closed and
redirect operators to `buddy.yml` or `official.yml`. They are not active release
entry points, trusted-publisher identities, or workflows that may mint publish
credentials.

No additional triggered top-level workflows are release entry points in this design. `.github/workflows/codeql.yml` is allowed as a triggered top-level non-release security analysis workflow only when it has no release authority, cannot call release mutation workers, and does not mint publish credentials or protected-ref bypass credentials. A scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflow such as `renovate.yml` is allowed only when it has no release authority, uses explicit least-privilege job permissions from a workflow-level `permissions: {}` baseline, cannot call release mutation workers, and does not mint publish credentials or protected-ref bypass credentials. It may use a dedicated GitHub App installation token to create dependency branches and pull requests. Renovate may use GitHub platform automerge with squash merge for configured dependency pull requests after required CI and branch protection/rulesets pass, and branch protection/rulesets must continue to prevent the dependency-maintenance token from mutating or bypassing protected branches and release refs directly.

The active shared execution layer is:

- `.github/workflows/release-orchestrate.yml` as the split-topology orchestration and token-minting / publish host
- `.github/workflows/release-resolve.yml` for project/version/ref resolution
- ecosystem build workflows such as `.github/workflows/release-build-python.yml`, `.github/workflows/release-build-node-pack.yml`, `.github/workflows/release-build-dotnet.yml`, `.github/workflows/release-build-ruby-gem.yml`, and `.github/workflows/release-build-wxt.yml`
- release support workflows such as `.github/workflows/release-create-github-release.yml` and `.github/workflows/release-prepare-release-notes.yml`
- reviewed local composite actions under `.github/actions/**`
- reviewed helper scripts under `eng/scripts/**`

The older `_build-test-*.yml`, `_attest-build-*.yml`, and `_buddy-mutation-worker.yml` layer described by earlier v2 drafts is superseded and is not the active workflow topology.

Security-sensitive publication now follows the active split topology. `.github/workflows/release-orchestrate.yml` is the orchestration boundary for live package-registry publication and dispatches the reviewed token-minting / publish paths for PyPI, npmjs, and RubyGems. The older direct `buddy.yml` / `official.yml` publish-job shape and the buddy-only mutation-worker authorization model are superseded for live package-registry publication. .NET GitHub Release asset builds are active through `release-build-dotnet.yml`; active buddy GitHub Release publication and previews are unsupported and fail closed before tag or release mutation while buddy attestations remain disabled. NuGet registry publication is not active in this topology because the shared catalog keeps `families.nuget.instances: []` until reviewed NuGet target catalog instances and publish routing are added.

## 2. `ci.yml` — Pull Request Validation

Main responsibilities:

1. Run repository static analysis through HK.
2. Detect affected ecosystems and build kinds.
3. Build, test, and package only the affected ecosystem/build-kind suites when required.
4. Validate that workflows and docs do not drift from active release descriptors and `eng/release/target-instances.yml`.
5. Finish with one final gate job suitable for branch protection.

Design rules:

- HK is repository-wide, not project-specific.
- Infrastructure and shared control-plane changes must trigger all ecosystem/build-kind suites.
- Ecosystem build/test execution uses the active release workflow calls such as `release-build-python.yml`, `release-build-node-pack.yml`, `release-build-dotnet.yml`, `release-build-ruby-gem.yml`, and `release-build-wxt.yml`; any future NuGet registry publish workflow or target catalog instance must be added explicitly before NuGet registry targets are re-enabled.
- The active reusable runner contract follows checked-in workflow routing: .NET release variants select `ubuntu-latest`, `macos-latest`, or `windows-latest` from OS/RID dimensions, while current Python, Node, WXT, and Ruby release build workflows use `ubuntu-latest`.
- `ci.yml` must parse the active release descriptor/catalog surfaces and fail the PR when workflow code or checked-in docs drift from that machine-readable source of truth.
- `ci.yml` must parse active `src/**/three.release.yml` descriptors and `eng/release/target-instances.yml` with duplicate-key rejection enabled before any schema validation or digest computation. Because Python's default `json.loads()` silently accepts duplicate keys and keeps the last value, the Day 0 helpers and validators must use `object_pairs_hook` or an equivalent parser path that turns duplicate keys into hard failures.
- The `ci.yml` bootstrap-governance surface is `.github/CODEOWNERS`, `.github/actionlint.yaml`, `.github/workflows/ci.yml`, `.github/workflows/buddy.yml`, `.github/workflows/release-orchestrate.yml`, `.github/workflows/release-resolve.yml`, `.github/workflows/release-build-python.yml`, `.github/workflows/release-build-node-pack.yml`, `.github/workflows/release-build-dotnet.yml`, `.github/workflows/release-build-ruby-gem.yml`, `.github/workflows/release-build-wxt.yml`, `.github/workflows/release-create-github-release.yml`, `.github/workflows/release-prepare-release-notes.yml`, `.github/workflows/official.yml`, `.github/workflows/docs/DESIGN.v2.md`, active `src/**/three.release.yml` descriptors, `eng/release/target-instances.yml`, every checked-in file under `.github/actions/**`, and every checked-in file under `eng/scripts/**`. That surface must be protected by dedicated CODEOWNERS review from the repository’s release-governance owners, and repository protection/rulesets must require code-owner review for that surface.
- The older `prTrustModel.bootstrapTrustedFilesSha256` bootstrap-hash rule is superseded with the removed repository-release-contract model. Active drift validation relies on checked-in workflow review, active descriptors/catalog validation, and CODEOWNERS/ruleset coverage for the active release surface.
- `ci.yml` must use event-specific concurrency groups with `cancel-in-progress: true`: PR-triggered runs use `ci/pr/<pull-request-number>`, while push-triggered runs use `ci/push/<full-ref>`. Bare `ci/<head-ref>` is forbidden because different forks can reuse the same branch name and would otherwise collide, and bare `ci/` is forbidden because push-triggered runs must not all share one slot. There is no grandfathered exception for any pre-existing branch-name-based `ci.yml` concurrency group.
- `ci.yml` drift validation must parse every checked-in workflow `on:` block and fail when a `pull_request_target` workflow checks out, executes, or otherwise sources PR-head code or PR-head refs. Official release enablement is forbidden until every repository `pull_request_target` workflow satisfies the metadata-only rule from this section; there is no grandfathered exception list in this design.
- `ci.yml` drift validation must parse every workflow file and fail when any non-local third-party action reference outside the GitHub-maintained `actions/` organization is not pinned to a full 40-character commit SHA. First-party `actions/*` references may use reviewed version tags and do not require SHA pinning.
- `ci.yml` drift validation must parse every checked-in workflow file and fail when the workflow-level top-level `permissions:` mapping is missing or broader than the active baseline required by that workflow. Current release workflows normally use workflow-level `contents: read`, with `actions: read` only where the workflow needs Actions metadata, and job-level least-privilege escalation for publish or provenance jobs. The older universal `permissions: {}` rule is superseded.
- `ci.yml` drift validation must validate active workflow files, `src/**/three.release.yml` descriptors, and `eng/release/target-instances.yml` for target and topology coherence before changes merge. Historical `officialTargetConfirmationPolicies` retry-budget checks are superseded with the removed repository-release-contract model.
- `ci.yml` drift validation must, for every release-enabled project, validate the active descriptor/catalog target-key contract on the same frozen branch snapshot. Historical `<project-root>/three.release.yml.targets` cross-file checks are superseded by `three.release.yml` plus `eng/release/target-instances.yml`.
- `ci.yml` drift validation must reject changes that wire active GitHub Release or registry publication without the matching active environments (`github-release`, `pypi`, `npmjs-gate`, `npmjs`, or `rubygems`) and the corresponding `release-orchestrate.yml` publish path.
- Historical `baselineWaitTimerMinutes`, `approvalWaitMaxSeconds`, and `providerConfigReviewedAt` contract-field checks are superseded unless a later reviewed design reintroduces those repository-release-contract fields.
- The older `.github/external-control-plane-commitments.json` drift rule is superseded with the removed external broker/monitor control-plane model. Active drift validation focuses on workflow files, active descriptors, and `eng/release/target-instances.yml`.
- `ci.yml` drift validation must fail if any repository-owned internal reusable release workflow outside the documented active release orchestration/build/support set declares `workflow_dispatch`. Active reusable release workflows such as `release-orchestrate.yml`, `release-resolve.yml`, and `release-build-*.yml` must remain `workflow_call`-only unless a later reviewed design explicitly changes the entry model. `ci.yml` must also fail if any top-level workflow other than `buddy.yml` or `official.yml` is introduced as a release entry point; scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflows are permitted only when they remain outside release authority and satisfy the no-release-authority constraints in §1.
- `ci.yml` drift validation must also parse every checked-in workflow file and fail unless release entry workflows call only the documented active reusable release workflows. The superseded `_buddy-mutation-worker.yml` path must not be reintroduced or wrapped as a release authorization boundary.
- `ci.yml` drift validation must fail if any workflow outside the documented active release entry/orchestration set declares a workflow-level or job-level concurrency group that collides with the active buddy/official release concurrency namespaces.
- `ci.yml` drift validation must fail if any workflow outside `official.yml`, `buddy.yml`, or the documented active release orchestration/build/publish set references a reserved release environment name from the checked-in release contract or active registry environment model.
- `ci.yml` drift validation must parse every `actions/checkout` invocation in `buddy.yml`, `official.yml`, active `release-*.yml` reusable workflows, and every checked-in composite action under `.github/actions/**`, and fail when any checkout omits `persist-credentials: false`.
- `ci.yml` drift validation must fail when `id-token: write` appears anywhere other than the exact publish/provenance job that needs GitHub OIDC for trusted publishing or provenance, or a reusable-workflow caller job granting `id-token: write` only as the upper-bound permission for documented called OIDC publish/provenance jobs. For external-registry trusted publishing, actual registry-token minting remains restricted to the exact environment-scoped publish/provenance jobs. GitHub artifact-attestation/provenance jobs may grant `id-token: write` and `attestations: write` without an external registry environment when they run only on the authorized release workflow path and do not mint external registry credentials. Additionally, npmjs `workflow_call` trusted publishing requires the active caller job in `official.yml` to grant `id-token: write` because npm validates the caller workflow identity while the publish command remains in the reusable workflow.
- `ci.yml` drift validation must fail when build/test or attestation reusable-workflow calls omit the explicit `secrets: {}` mapping.
- The shell-safety rules from §8 apply equally to `ci.yml`. Even apparently low-risk values such as `${{ github.sha }}`, `${{ github.ref }}`, or `${{ github.event.pull_request.number }}` must be mapped through `env:` or explicit action inputs before shell execution rather than interpolated directly inside shell source.
- PR validation for untrusted code must use `pull_request`, not `pull_request_target`.
- If `pull_request_target` is ever used for repository-maintenance work, it must be metadata-only: it must not check out, execute, or source PR-head code and must not mint publish, protected-ref-write, or other privileged release credentials.
- Fork PRs are always untrusted input. PR workflows, including fork PR workflows, must never receive repository secrets, environment-scoped publication credentials, or protected-ref-write credentials.
- Repository settings that would grant fork PRs secrets or privileged write tokens are out of scope for this design and must remain disabled as an explicit repository prerequisite.
- PR workflows must never publish artifacts to external registries, create releases, or mutate protected refs.
- The final gate job must succeed when the required build/test/package work succeeded, even if some ecosystem jobs were intentionally skipped.

### 2.1 Bootstrap integrity hash computation and maintenance

This bootstrap-hash section is retained as historical/future-only material from
the removed repository-release-contract control-plane design. Active CI
governance validates the checked-in workflow files, active `three.release.yml`
descriptors, and `eng/release/target-instances.yml`; it does not require
`.github/repository-release-contract.json` or
`.github/external-control-plane-commitments.json`.

- Unless this document explicitly says otherwise, every SHA-256 value stored in checked-in JSON, tag payloads, or reviewed manifests uses the canonical text form `sha256:<64 lowercase hex>`.
- The canonical bootstrap manifest is the UTF-8 RFC 8785 / JCS serialization of one JSON array of objects, where each object has exactly `path` and `sha256` keys.
- Each `path` is the exact repository-relative slash-separated path from the repository root.
- For every bootstrap-governance file except `.github/repository-release-contract.json`, the manifest `sha256` is the canonical `sha256:<64 lowercase hex>` digest of that file’s UTF-8 text bytes after line-ending normalization to LF. The helper must reject non-UTF-8 bootstrap files rather than hashing platform-specific raw bytes.
- For `.github/repository-release-contract.json`, the manifest `sha256` is computed from the same LF-normalized UTF-8 text **after** replacing the JSON string value of `prTrustModel.bootstrapTrustedFilesSha256` with the literal placeholder `sha256:0000000000000000000000000000000000000000000000000000000000000000`, every `providerConfigReviewedAt` value with the literal placeholder `1970-01-01T00:00:00Z`, and every non-null `providerConfigReviewRef` object with a placeholder object that preserves the original closed-schema `kind` value while replacing only `locator` with `artifact://provider-review/placeholder` and `evidenceSha256` with `sha256:0000000000000000000000000000000000000000000000000000000000000000`. `null` `providerConfigReviewRef` values remain `null`. The helper must hard-fail if `prTrustModel.bootstrapTrustedFilesSha256` is missing, duplicated, not a string, or appears anywhere other than `prTrustModel.bootstrapTrustedFilesSha256`, and it must also hard-fail when any non-null `providerConfigReviewRef` is duplicated or violates the closed schema before normalization. This placeholder normalization is the authoritative bootstrap-hash contract and replaces any impossible self-hash/fixed-point interpretation; preserving `providerConfigReviewRef.kind` is part of that contract because different evidence-capture assurance levels are semantically distinct even when the evidence bytes themselves are normalized out of bootstrap hashing.
- The manifest entries are sorted lexicographically by `path`. Duplicate paths are forbidden.
- The bootstrap-governance surface is exact, not heuristic: `.github/CODEOWNERS`, `.github/actionlint.yaml`, `.github/workflows/ci.yml`, `.github/workflows/buddy.yml`, `.github/workflows/release-orchestrate.yml`, `.github/workflows/release-resolve.yml`, `.github/workflows/release-build-python.yml`, `.github/workflows/release-build-node-pack.yml`, `.github/workflows/release-build-dotnet.yml`, `.github/workflows/release-build-ruby-gem.yml`, `.github/workflows/release-build-wxt.yml`, `.github/workflows/release-create-github-release.yml`, `.github/workflows/release-prepare-release-notes.yml`, `.github/workflows/official.yml`, `.github/workflows/docs/DESIGN.v2.md`, active `src/**/three.release.yml` descriptors, `eng/release/target-instances.yml`, every checked-in file under `.github/actions/**`, and every checked-in file under `eng/scripts/**`. Membership in that surface does not depend on whether `ci.yml` currently invokes a given helper.
- `.github/CODEOWNERS` itself is a mandatory bootstrap prerequisite. Official release enablement is forbidden until that file exists, covers the bootstrap-governance surface, and repository protection/rulesets require code-owner review for that surface.
- `ci.yml` must parse `.github/CODEOWNERS` and fail closed if any bootstrap-governance path lacks the dedicated release-governance owner coverage required by this design. The bootstrap hash is therefore not self-authenticating; CODEOWNERS coverage drift is part of the same bootstrap validation surface.
- Repository protection/ruleset enforcement remains a separate repository prerequisite. Official release enablement is forbidden until repository protection/rulesets require code-owner review for the bootstrap-governance surface, and §4.1 treats that prerequisite as an explicit enablement checklist item rather than an operator memory task.
- Any identity that can bypass that CODEOWNERS or protection/ruleset enforcement surface, including a repository administrator using an allowed bypass path, is inside the bootstrap trust root by definition. Such a bypass is treated as compromise of bootstrap governance rather than as an ordinary reviewed change.
- Historical/future-only: the removed repository-release-contract model required one reviewed helper command, `eng/scripts/compute-bootstrap-hash`, that recomputed the manifest and final `prTrustModel.bootstrapTrustedFilesSha256` value from repository contents using placeholder normalization for `.github/repository-release-contract.json`. That helper is not an active Day 0 prerequisite for the current descriptor/catalog topology. If a later design reintroduces it, Python implementations must still use duplicate-key-rejecting parsing such as `object_pairs_hook` rather than the default parser path.
- `eng/scripts/compute-bootstrap-hash` is a required cross-platform helper with this minimum interface contract:
    - implementation baseline: Python `3.12+` with only checked-in repository code and explicitly reviewed dependencies; Linux and Windows CI entrypoints may be thin wrappers, but the authoritative logic is repository-owned and deterministic
    - invocation contract: `eng/scripts/compute-bootstrap-hash --repo-root <path> [--format json|hash] [--manifest-out <path>]`
    - default stdout contract (`--format json`): one JSON object with exactly `bootstrapTrustedFilesSha256` and `manifest`, where `manifest` is the exact canonical `(path, sha256)` list sorted as required by this section and already reflects the placeholder-normalized digest rule for `.github/repository-release-contract.json`
    - `--format hash` stdout contract: only the canonical `sha256:<64 lowercase hex>` value followed by `\n`
    - exit codes: `0` success; `2` invalid invocation; `3` bootstrap-surface resolution failure (including duplicate paths); `4` file-read or digest-computation failure
    - minimum tests: golden fixtures under `eng/tests/jcs-fixtures/` for path ordering, explicit LF normalization from both CRLF and LF inputs, duplicate-path rejection, placeholder normalization for `.github/repository-release-contract.json`, and a fixture proving that the emitted manifest and checked-in placeholder-normalized contract bytes recompute back to the same `bootstrapTrustedFilesSha256`
    - the placeholder-normalization fixture set must explicitly cover the exact literal `sha256:0000000000000000000000000000000000000000000000000000000000000000`, the exact timestamp placeholder `1970-01-01T00:00:00Z`, one non-null `providerConfigReviewRef` case for every supported `kind`, and a `null` `providerConfigReviewRef` case so cross-language implementations cannot silently drift on omitted prefixes, null handling, or placeholder object shape
- Any PR that adds, removes, renames, or changes a file in that bootstrap-governance surface must update the surface itself and `prTrustModel.bootstrapTrustedFilesSha256` in the same PR. There is no deferred or compatibility-preserving update path.
- A PR that changes only `providerConfigReviewedAt` and/or `providerConfigReviewRef` for unchanged target-auth bindings does not require a bootstrap-hash update because those operational freshness fields are placeholder-normalized out of the bootstrap manifest digest. It still requires ordinary reviewed contract changes on `.github/repository-release-contract.json`, must not be used to hide changes to workflow path, environment, actor, audience, or allowed-ref bindings, and must keep the old and new evidence locators/digests reviewable side by side in the PR description or linked review record. This is a deliberate residual-risk tradeoff: bootstrap integrity no longer authenticates the freshness-evidence bytes or locator/digest details inside `providerConfigReviewRef`, so drift validation is mandatory. `ci.yml`, release-time validation, and the active provider-freshness diagnostics from §7.6 must treat those fields as operational evidence by verifying that every referenced locator remains reachable when the relevant surface is available and that the fetched bytes still hash to the recorded `evidenceSha256`, and that the evidence still asserts the same normalized trust tuple (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerAudience`, `providerRefClaimMode`, `providerTrustCapabilities`, and `allowedRefClaims`) rather than drifting to a weaker or differently-scoped conclusion. An unreachable, mismatched, or semantically divergent evidence record is configuration-invalid rather than a warning.
- The Day 0 helper exit-code tables in this document are tool-local contracts, not a repository-wide shared numeric taxonomy. Implementations and runbooks must not infer cross-tool meaning from the same numeric exit code unless this document explicitly says so.
- `ci.yml` must fail closed with both the checked-in hash and the recomputed hash, and it must print the canonical manifest diff or equivalent per-path digest mismatch list so reviewers can see exactly which bootstrap file changed.
- The dedicated CODEOWNERS protection for the bootstrap-governance surface is required even when the hash matches; the hash proves exact content identity, while CODEOWNERS review proves reviewed authority to change that surface.

## 3. `buddy.yml` — Unofficial Release

`buddy.yml` is the manual workflow for unofficial releases. It is independent of `official.yml`. The active `workflow_dispatch` interface exposes `project`, `version`, optional `target`, and `force_update_tag`; it freezes two SHA roles before orchestration. The selected dispatch ref freezes `workflowDispatchSha`, the trusted workflow/control-plane commit that hosts policy and workflow code. The optional `target` selector resolves `releaseTargetSha`, the release payload/content commit used for build inputs, tags, and assets; `releaseTargetSha` equals `workflowDispatchSha` only when `target` is empty.

Buddy is unofficial, but it is **not** an arbitrary-branch publication path. Each buddy-enabled project must declare exact authorized refs in the active descriptor/catalog and workflow policy surfaces, and the corresponding buddy environments must enforce the same allowlist through deployment-branch restrictions. Wildcard catch-alls such as `refs/heads/*` are forbidden.

Because GitHub approval/review history is documented per `run_id` rather than per `run_attempt`, buddy publication is single-attempt only. `buddy.yml` must hard-fail when `github.run_attempt != 1`, and any retry after approval, partial publish, or stale reviewer state requires a fresh manual dispatch rather than a GitHub rerun attempt.

### 3.1 Responsibilities

1. Resolve exactly one project from repository state.
2. Reject buddy publication unless the selected branch is one of the checked-in buddy-authorized refs for that project.
3. Rely on active dynamic entry concurrency and orchestrator checks rather than the superseded official admission-state/live-lock gate.
4. Run bounded static analysis for that project plus buddy control-plane files.
5. Build, test, and package exactly one ecosystem/build-kind path when that path requires packaging.
6. Publish only unofficial targets.

### 3.2 Active job outline

1. **`authorize-entry` in `buddy.yml`**
    - validates workflow inputs `project`, `version`, optional `target`, and
      `force_update_tag`
    - resolves the active dynamic release-identity concurrency group
      `release/${project_id}/v${release_version}` before orchestration
    - rejects unauthorized actors or untrusted refs before any publish-capable work

2. **`orchestrate` delegation**
    - calls `.github/workflows/release-orchestrate.yml`
    - lets `release-resolve.yml` resolve the selected target/ref to the commit used
      by later build and publish jobs
    - validates active `three.release.yml` descriptors and
      `eng/release/target-instances.yml`
    - treats zero-target or no-side-effect selector sets as valid when explicitly
      represented by the orchestrator outputs

3. **Hosted build and publish jobs**
    - run inside the active `release-orchestrate.yml` split topology
    - use the selected ecosystem build workflow and active registry/GitHub Release
      environments
    - publish only active buddy targets selected by the descriptor/catalog graph

The older direct `resolve-context` / `static-analysis` / direct build /
`buddy-audit` graph is superseded and is not the active `buddy.yml` execution
shape.

#### Canonical `buddy-review-payload` schema

`buddy-review-payload` is a closed object. It contains exactly these fields and no others:

| Field                    | Type             | Notes                                                                                                                                                                                                 |
| ------------------------ | ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `releaseTargetSha`       | `string`         | 40-character lowercase release content commit SHA; this is the plan `commit-sha` / `release_target_sha`.                                                                                              |
| `releaseTargetShaPrefix` | `string`         | Lowercase hex prefix derived from `releaseTargetSha`; minimum length `12`.                                                                                                                            |
| `workflowDispatchSha`    | `string`         | 40-character lowercase trusted workflow/control-plane commit SHA used only for workflow provenance.                                                                                                   |
| `projectKey`             | `string`         | Canonical checked-in project key.                                                                                                                                                                     |
| `packageIdentity`        | `string`         | Exact external package identity for the selected project.                                                                                                                                             |
| `version`                | `string`         | Canonical validated buddy version.                                                                                                                                                                    |
| `target`                 | `string`         | One selected buddy target from `{github-release/public, npm/github-packages, rubygems/github-packages}`; `github-release/public` is recognized but fail-closed while buddy attestations are disabled. |
| `requiredEnvironment`    | `string`         | Exact buddy environment name for that target.                                                                                                                                                         |
| `workflowPath`           | `string`         | Exact path `.github/workflows/buddy.yml`.                                                                                                                                                             |
| `runId`                  | `integer`        | GitHub Actions run id for this buddy dispatch.                                                                                                                                                        |
| `runAttempt`             | `integer`        | Must be `1`; GitHub rerun attempts are forbidden for buddy publication.                                                                                                                               |
| `artifactAliases`        | `string[]`       | Lexicographically sorted exact artifact aliases routed to this target.                                                                                                                                |
| `buddyTag`               | `string \| null` | Exact buddy release tag for `github-release/public`; otherwise `null`.                                                                                                                                |

5. **Superseded direct buddy publish sketch**
    - Older v2 drafts modeled one direct job per supported buddy target plus a buddy-only internal mutation worker.
    - That direct buddy publish / mutation-worker sketch is superseded for active package-registry publication.
    - Current guidance is that `buddy.yml` remains the entry and authorization gate, then delegates active token-minting / publish work to `.github/workflows/release-orchestrate.yml`.
    - The delegated publish path must still consume the corresponding buddy audit outputs, honor the active dynamic entry concurrency, and enter exactly one pre-created active target environment before any external mutation. Superseded official admission and `refs/tags/official-lock/<project-key>` re-read requirements are not part of the active path.

### 3.3 Buddy equivalent reviewed approval surface

GitHub environment approval remains the credential gate for buddy publish jobs, but GitHub does **not** natively render arbitrary workflow outputs in that approval UI. Buddy therefore requires one equivalent reviewed approval surface per target, and that surface is part of the authorization model:

- `buddy-audit` must create exactly one GitHub Deployment audit record per target/environment pair before the publish job becomes eligible for environment approval.
- The deployment `description` must be a short human-readable summary only, showing at least `releaseTargetShaPrefix`, `packageIdentity`, `version`, and target name, and it should stay comfortably below any undocumented platform limit by remaining under roughly 100 characters.
- The deployment `payload` must carry the full canonical serialized `buddy-review-payload`, including the full `releaseTargetSha`, computed buddy tag or `null`, exact artifact aliases, target/environment binding, the current run tuple, and `workflowDispatchSha` as trusted workflow/control-plane provenance only. `buddyReviewDigest` is derived from that payload and compared separately; it is **not** a self-embedded field inside the canonical payload object.
- `buddy-audit` must also emit a linked human-readable comment or run-summary block that repeats the same summary and instructs the reviewer to place one exact machine-readable confirmation line in the environment approval comment for that target: `buddy-approve target=<target> release-target-sha=<releaseTargetShaPrefix> run-id=<runId> run-attempt=1 digest=<buddyReviewDigestPrefix>`. `buddyReviewDigestPrefix` must be exactly 16 lowercase hex characters derived from the first 16 characters of the full canonical digest. That prefix is a reviewer-usable binding hint, not the authoritative integrity value and not a substitute for collision-resistant comparison of the full digest; runtime verification must always compare the full canonical `buddyReviewDigest`, and the 16-hex prefix length is a deliberate usability-versus-human-transcription tradeoff recorded by this design rather than an implicit security claim. Approval-related summary text must be derived only from already-validated frozen outputs; it must not interpolate unchecked workflow inputs or payload-controlled strings directly into `GITHUB_STEP_SUMMARY`, and every rendered data value must be Markdown-escaped or code-fenced so package names, versions, refs, and similar fields cannot create headings, tables, links, images, checkboxes, or raw HTML.
- `buddy-audit` itself must stay outside every buddy publication environment. It only creates the deployment audit record and reviewer-facing approval surface; it is not allowed to mint publication credentials or request `id-token: write`.
- A buddy publish job becomes environment-eligible only after the matching `buddy-audit` record exists. As the literal first step after environment approval, before any checkout, setup action, local composite action use, external API call, package upload, GitHub App token minting, or OIDC token request, that publish job must re-read the deployment audit record and reviewer confirmation and fail closed unless both are bound to the same frozen `releaseTargetSha`, `packageIdentity`, `version`, computed buddy tag, target/environment pair, `runId`, and `runAttempt`.
- Environment approval remains the GitHub credential gate, so environment-scoped secrets or OIDC availability may already exist for that job when it starts. The design therefore treats the deployment-payload re-read and reviewer-confirmation check as a mandatory first action before any checkout, tool setup, external API call, package upload, release mutation, or credential minting/use beyond reading the audit record itself. Any later step that mints a publish credential must be conditionally gated on the verification step’s explicit success output (for example `if: steps.verify.outputs.verified == 'true'`).
- A failed post-approval re-check ends the credentialed job immediately. No later job may inherit those credentials, and there is no fallback path that allows a buddy publish to continue after a stale or mismatched approval record.
- Missing, stale, mismatched, unparsable, or format-incomplete reviewer confirmation invalidates approval. The workflow must reject any buddy approval comment whose `digest` value is not exactly 16 lowercase hex characters or does not exactly match the first 16 characters of the current run's full `buddyReviewDigest`. If the environment was approved but the comment omits the exact `releaseTargetShaPrefix`, `runId`, or digest prefix required above, the target must fail closed and require a fresh manual dispatch; there is no downgrade path that allows a buddy publish without the bound audit surface.

### 3.4 Buddy authorization boundary and minimum environment contract

The active authorization boundary for buddy publishing is `buddy.yml` entry authorization plus the `release-orchestrate.yml` hosted publish/token-minting jobs and the repository-side controls that scope credentials to authorized buddy runs:

- job-level `permissions`
- jobs whose workflow/control-plane code is rooted in the selected `workflow_dispatch` snapshot and delegated through the reviewed orchestrator
- repository review on the entry/orchestrator wiring and helper code at the frozen `workflowDispatchSha`
- pre-created active buddy publication environments and credentials that are available only to documented buddy/orchestrator jobs from documented buddy-authorized protected branches

The older direct buddy publish job, `buddy-*` environment, and internal buddy
mutation-worker model is superseded. Current buddy publication treats `buddy.yml`
as the entry/auth gate and `release-orchestrate.yml` as the active publish host;
caller-supplied path inputs, `project-key` worker inputs, and buddy NuGet
environments are historical only. Active target environments are the registry and
GitHub Release environments used by the orchestrator:

- GitHub Release publication uses the active GitHub Release environment binding
  when configured.
- npm GitHub Packages publication uses the active npm/GitHub Packages path.
- RubyGems GitHub Packages publication uses the active RubyGems/GitHub Packages
  path.
- NuGet buddy environments remain deferred with NuGet registry publication.
- Active environments must be pre-created before their corresponding live publish
  path is enabled and must not rely on repository-level long-lived publication
  credentials.

### 3.5 Buddy targets

Buddy recognizes these unofficial target selectors in the split topology, but only the GitHub Packages registry selectors are active for mutation while buddy attestations remain disabled:

- `npm/github-packages`
- `rubygems/github-packages`
- `github-release/public` (recognized but unsupported for active buddy publication and fail-closed before mutation)

The legacy `nuget:gpr` target key is superseded for the current repository state. NuGet registry targets remain unavailable while the shared target catalog has `families.nuget.instances: []`.

Python has no unofficial package-registry target. Python buddy previews are currently unsupported because buddy `github-release/public` fails closed before mutation while buddy attestations remain disabled. `pypi:testpypi` is not a supported target.

### 3.5.1 Python buddy preview rationale

- `pypi:testpypi` is intentionally excluded from this design. TestPyPI is a separate registry surface with separate credentials, separate environment wiring, separate cleanup behavior, and separate partial-publish recovery concerns.
- The buddy channel stays intentionally smaller than the official channel. For Python preview distribution, GitHub prereleases already provide a repository-local surface that avoids introducing a second unofficial Python registry trust domain.
- Python buddy preview distribution through `github-release/public` is currently unsupported because active buddy GitHub Release paths fail closed before mutation while buddy attestations remain disabled.
- If a later reviewed design enables buddy GitHub Release previews, repository-owned instructions must document at minimum: the canonical preview asset naming convention; a `pip install --no-index --find-links <release-assets-url> <packageIdentity>==<version>` path and, when a direct asset URL is used, the exact command form including `--hash=sha256:<digest>` for every referenced wheel or sdist; the fact that GitHub Release preview assets are not PyPI and are not a supported package index; the preview asset support lifetime/SLA; and the cleanup policy for superseded preview assets.
- Preview-asset cleanup must be explicit after any future enablement: when a buddy preview is abandoned or superseded, operators must either remove the prerelease or mark it unsupported in the release notes and repository-owned instructions so consumers do not mistake it for the official channel.
- Adding `pypi:testpypi` in the future would require an explicit design amendment covering its target auth contract, confirmation rules, partial-publish cleanup runbook, and why that additional unofficial registry surface is worth the added operational complexity. Until then, it remains unsupported.

### 3.6 Buddy GitHub Release identity and auth

- Buddy GitHub Release identity is separate from the official release identity.
- Buddy has two SHA roles. The selected dispatch snapshot, recorded as
  `workflowDispatchSha`, is the trusted workflow/control-plane code and policy
  snapshot used to authorize the run. The resolved `releaseTargetSha` is the
  payload/content commit used for build inputs and release assets. They may be
  equal when the manual `target` input is empty, but the approval payload and
  audit model must keep their meanings separate.
- The buddy tag format is `buddy/<project-key>/v<version>/<releaseTargetSha>`.
- Because that tag format contains a second `/` segment after `v<version>`, the required protecting ruleset pattern is `refs/tags/buddy/<project-key>/v**`; `v*` is insufficient because GitHub tag-pattern `*` does not cross `/`.
- Buddy GitHub Release tags must be annotated tags and must match the active descriptor/workflow-derived buddy tag namespace. The workflow must hard-fail if the computed buddy tag would fall outside that namespace or collide with the official tag namespace. The older `.github/repository-release-contract.json` `buddyTagPattern` source is superseded.
- Active buddy GitHub Release publication and previews are unsupported and fail closed before tag or release mutation while buddy attestations remain disabled. The namespace and idempotency rules below are retained as future enablement prerequisites; they do not authorize active buddy GitHub Release publishing.
- If later enabled, buddy `github-release/public` must always attach to that already-derived buddy tag; it must not reuse the official `release/<project-key>/v<version>` namespace.
- If later enabled, a fresh buddy redispatch of the same frozen buddy identity is idempotent only when the existing buddy tag already points to the same frozen buddy `releaseTargetSha`, the existing release is attached to that same buddy tag, and the live release asset set exactly matches the current-run immutable artifact set plus digest manifest; otherwise it is a hard conflict.
- If later enabled for buddy `github-release/public`, missing, extra, renamed, or digest-mismatched release assets are conflicts, not same-identity no-ops.
- Buddy registry targets use target-specific idempotent publish helpers that consume the current-run immutable artifact set plus digest manifest.
- A buddy registry publish may be treated as a same-identity no-op only when live remote state proves the already-present package version corresponds to the same frozen buddy identity and the same current-run artifact identity; version-only matches are insufficient.
- If a registry already contains the requested version but remote state cannot prove same-identity, or proves different bytes/metadata for that buddy identity, the workflow must hard-fail rather than overwrite or silently accept the conflict.
- Buddy publication credentials must be explicitly minted only after the job has entered the pre-created buddy environment and completed the documented post-approval rebinding checks. Ambient credential availability outside the documented target auth contract is not the authorization boundary for buddy publishing.
- For official `github-release/public`, the active path uses the configured `github-release` environment and GitHub Release permissions in `release-orchestrate.yml`. Buddy `github-release/public` remains unsupported and fail-closed while buddy attestations are disabled. The older brokered buddy GitHub Release actor model is superseded unless a later reviewed design reintroduces it.
- For active GitHub Packages buddy targets (`npm/github-packages` and `rubygems/github-packages`), use the documented GitHub Packages auth contract for that ecosystem, normally job-scoped `GITHUB_TOKEN` with `packages: write`; that permission is broader than the single package being published, and workflow permissions alone do not express package-scoped narrowing. Any narrower repository/package-level access control is external GitHub configuration that must be reviewed separately and recorded as residual risk in the checked-in contract; any stronger GitHub-native package credential is a repository hardening choice that must be documented target-by-target in the checked-in release contract rather than implied by the generic term “GitHub-native”. The old `nuget:gpr` target is deferred and unavailable while `families.nuget.instances: []`.
- Buddy publishing must not use long-lived publication credentials or normal-path private-key material. Any emergency use of long-lived bootstrap material is break-glass only under §7.5.

### 3.7 Buddy failure and partial publish behavior

- Buddy does not use the official checked-in blocked-state ledger. Its recovery surface is intentionally smaller and is limited to bounded fresh manual redispatches of the same frozen buddy identity plus manual cleanup when same-identity proof fails.
- The expected recovery path for a partial buddy publish is a fresh manual dispatch that resolves to the same frozen `releaseTargetSha`. GitHub rerun attempts are forbidden. A new manual dispatch that freezes a different `releaseTargetSha` is a new buddy release identity, not a continuation of the partial one.
- On a fresh redispatch, targets that already prove same-identity must be treated as no-op success, while targets that were not yet mutated or remain uncertain must continue through the normal bounded confirmation logic.
- If any already-published buddy target cannot prove same-identity to the redispatched artifact set, the workflow must fail hard and require manual cleanup instead of overwriting or silently accepting drift.
- Manual cleanup is mandatory runbook content, not an operator improvisation. For every active buddy target (`npm/github-packages` and `rubygems/github-packages`) and any later-enabled buddy `github-release/public` target, the runbook must define this sequence: detect partial publication; capture evidence for the frozen `releaseTargetSha`, artifact names, and digests actually published; verify whether the remote bytes match the redispatched artifact identity; perform the target-specific delete/deprecate/yank cleanup if the platform allows it; decide whether the version must be permanently burned when deletion is impossible or identity proof failed; record the cleanup result and disposition; and decide whether a same-identity fresh redispatch is still allowed. NuGet buddy cleanup is deferred until NuGet registry targets are re-enabled.
- For `github-release/public`, the cleanup runbook must explicitly order operations as release-asset evidence capture → release-asset cleanup or release deletion → buddy tag cleanup when needed, so tag identity and asset evidence are not lost prematurely.
- The runbook must state who signs each step: the release engineer opens and tracks the incident, the package owner performs registry-specific cleanup, and the approver on duty records the retry-versus-abandon decision with evidence references.
- If platform rules make the buddy version non-reusable after a partial publish, the runbook must say so explicitly and require the version to be burned rather than retried with changed bytes.

## 4. `official.yml` — Production Release

`official.yml` is the production release entry workflow. It supports the active `push.tags: release/*/v*` trigger and manual `workflow_dispatch`, and it is independent of `buddy.yml`.

The official release tag format is:

- `release/<project-key>/v<version>`

`official.yml` derives that tag internally. It is not a workflow input.

`official.yml` has two active entry shapes: tag pushes under `refs/tags/release/*/v*`, and manual `workflow_dispatch` with `project`, `version`, optional `target`, and `force_update_tag`. For manual runs, `project` must resolve to exactly one canonical project; exact display-name aliases are canonicalized to that project id, but fuzzy matching remains forbidden. For tag-triggered runs, the release tag supplies the project/version identity. Superseded admission-state and live-lock recovery modes are not part of the active entry contract.

### 4.1 Official repository prerequisites

Official release enablement for a project is allowed only after these repository-side controls already exist:

Readiness review is ordered. Repositories must clear these gates in sequence:

| Gate                       | Priority | Purpose                                  | Minimum examples                                                                                                                                                                                                          |
| -------------------------- | -------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Governance gate            | P0       | Authorize the control plane              | protected official branches/tags, bootstrap hash + CODEOWNERS coverage, protected official/buddy refs, no privileged fork-PR path, no tag-push trigger outside `release/*/v*`                                             |
| Protected mutation gate    | P1       | Constrain credentialed mutation surfaces | pre-created active release/registry environments (`github-release`, `pypi`, `npmjs-gate`, `npmjs`, `rubygems` as applicable), actor separation, dynamic entry concurrency, and `release-orchestrate.yml` publish receipts |
| Operational readiness gate | P2       | Make failure and recovery operable       | registry-environment runbooks, orchestrator receipt checks, runner pinning, key rotation, and alerting                                                                                                                    |

- every protected branch that may dispatch `official.yml` for that project is covered by a branch protection rule or ruleset that at minimum:
    - prevents force-push and deletion
    - requires the repository’s official CI gate before merge
    - requires reviewed changes or an explicitly restricted bypass path
- the selected protected branch must itself be the authoritative release line for the resolved project version: `main` for the current mainline release line, or `release/<project-key>/v<release-line>` for a maintenance line
- `.github/CODEOWNERS` must already exist before the first official release is enabled, must cover the bootstrap-governance surface from §2.1, and repository protection/rulesets must require code-owner review for that surface
- official release enablement must include an explicit repository-readiness check that `.github/CODEOWNERS` still covers every bootstrap-governance path from §2.1 and that repository protection/rulesets still require code-owner review for that same surface; bootstrap governance is not considered healthy when either half of that pair is missing
- a project may have different official release lines over time; active serialization is provided by the entry workflow's job-level `orchestrate` concurrency group `release/${project_id}/v${release_version}`, while the older per-project admission-file/live-lock model is superseded
- repository readiness must include a reviewed confirmation that the repository's current GitHub plan/visibility exposes repository rulesets for tag namespaces; unless re-verified otherwise for that exact repository type, readiness must conservatively assume GitHub Team or GitHub Enterprise Cloud (or an equivalent plan tier that explicitly includes repository rulesets for this repository visibility)
- the official tag namespace `refs/tags/release/<project-key>/v*` is covered by a tag-targeted ruleset
- the buddy tag namespace `refs/tags/buddy/<project-key>/v**` is covered by a tag-targeted ruleset because the actual buddy tag format appends `/<releaseTargetSha>` after `v<version>`
- the historical live official lock tag `refs/tags/official-lock/<project-key>` is not active unless a later reviewed design reintroduces live locks
- active `three.release.yml` descriptors and `eng/release/target-instances.yml` exist and are reviewed on every protected official release branch/tag that may dispatch `official.yml`; superseded `.github/repository-release-contract.json` and `.github/official-admission-state/` inputs are not active requirements
- before a project’s first official dispatch, that branch already contains a valid active descriptor and target catalog entry for the selected active target set
- every active release or registry environment that can grant approval or credentials is pre-created and configured so only runs dispatched from allowed protected official release refs may enter it. Active validation must fail closed when it cannot confirm the live GitHub environment policy for the selected run.
- active release readiness relies on `release-orchestrate.yml` publish receipts and registry evidence. The older durable artifact-store prerequisite in §4.10 is superseded unless a later reviewed design reintroduces that recovery backend.
- every build-affecting toolchain and dependency-resolution input for the enabled release path is already pinned in checked-in files before official release is enabled, including `mise.lock` when used, ecosystem lockfiles when applicable, and any restore/install inputs that would otherwise float across reruns
- the older measured `postApprovalValidatedAt` → `create-live-lock` baseline approval window is superseded for the active topology. Active readiness evidence must instead cover the entry workflow's dynamic concurrency slot, orchestrator startup/scheduling jitter, and registry-environment approval/publish timing for the selected target set
- every official-release project must account for the maximum wall-clock time one official run may remain waiting for active registry-environment approval while occupying its dynamic `official.yml` entry concurrency group. Active runbooks must define cancellation/escalation behavior for that wait; the older `approvalWaitMaxSeconds` contract field and sizing formula are superseded.
- `approvalToLiveLockMaxDelaySeconds` is a superseded post-approval bound from the older live-lock model. Active readiness evidence must instead cover dynamic entry concurrency, orchestrator startup, registry-environment approval, and publish/token-minting timing for the selected target set
- the repository must keep checked-in release-operations runbooks at stable reviewed paths, including at minimum the break-glass runbook index, the per-target cleanup matrix, the schema-migration runbook, and cross-release-line contention / hotfix-preemption guidance; official release enablement is forbidden until those runbooks exist on every protected branch that may authorize the release
- official release enablement is forbidden until the repository has completed and recorded the minimum exercise cadence from §7.5 for the project’s checked-in `assuranceProfile`
- GitHub-hosted runner labels used by the active workflows are the checked-in labels (`ubuntu-latest`, `macos-latest`, and `windows-latest`). Repositories that need stronger reproducibility may replace those with self-hosted immutable images or a reviewed fixed-label migration, but runner docs, workflow code, and trusted allowlists must stay in sync.
- the official release tag namespace is protected by repository rulesets, and active tag mutation is performed by the reviewed workflow path using the job-scoped `GITHUB_TOKEN` permissions granted to `release-orchestrate.yml` / `release-create-github-release.yml`
- the buddy tag namespace is protected by repository rulesets, and buddy GitHub Release publication remains unsupported and fail-closed while buddy attestations are disabled; any later enablement must use a reviewed GitHub Release workflow path with the `github-release` environment when `github-release/public` is selected
- historical protected-ref writer, GitHub App private-key, external broker, and durable-store marker-writer setup is superseded/future-only unless a later reviewed design reintroduces it
- §4.1 minimum startup-set guidance is capability-scoped rather than “provision every actor on day one”: active `github-release/public` requires the reviewed `github-release` environment and the appropriate workflow permissions only when that target is enabled
- Historical `.github/external-control-plane-commitments.json` prerequisites are superseded with the removed external broker/monitor model. Active prerequisites are reviewed workflow files, active descriptors, `eng/release/target-instances.yml`, and registry environment configuration.
- repository settings that would expose fork PR runs to secrets or privileged write tokens remain disabled as an explicit out-of-workflow prerequisite
- Active `official.yml` may retain its checked-in `on: push: tags: release/*/v*` trigger alongside `workflow_dispatch`. The older blanket prohibition on tag-push triggering is superseded; any tag trigger outside the reviewed release-tag namespace remains forbidden.
- official release enablement is forbidden until every checked-in workflow already satisfies the SHA-pinning rules from §2; there is no grandfathered pre-design workflow exception during rollout

Official publish jobs must not rely on “protected branch” alone as the full trust-root prerequisite. Active trust also requires protected release tags, checked-in descriptors/catalog, and registry-environment policy. The protected live-lock tag and durable evidence-store contract are superseded/future-only requirements from the older model.

#### 4.1.1 Historical build-time measurement helper contract

The `compute-build-time-p95` / `approvalToLiveLockMaxDelaySeconds` helper
contract below belongs to the superseded baseline/live-lock model and is not an
active official enablement prerequisite.

- `eng/scripts/compute-build-time-p95` is a Day 0 helper reserved by this design so `approvalToLiveLockMaxDelaySeconds` can be justified by one repository-owned measurement path rather than ad hoc manual calculations.
- minimum invocation contract: `eng/scripts/compute-build-time-p95 --project-key <key> --branch <protected-ref> [--sample-size <n>] [--percentile 95] [--format text|json]`
- read contract: it may read GitHub Actions timing metadata, checked-in readiness evidence, and runbook-linked measurement records; it must not mutate repository state or external release state
- measurement contract: it must measure the bounded post-approval window from `postApprovalValidatedAt` through the final `create-live-lock` revalidation using reviewed rehearsal data or prior successful official runs on the authoritative branch, compute P95 or a stricter requested percentile over the sampled durations, and add the required `600` second safety buffer before printing the recommended `approvalToLiveLockMaxDelaySeconds`. When enough data exists on the authoritative branch, the helper must default to at least 30 samples. The output must state how many samples came from prior successful official runs versus rehearsals. Fewer than 10 total samples is insufficient unless the readiness record referenced by `readinessEvidenceRef` carries an explicit waiver; any sub-10 case must include at least 3 recent rehearsal samples when available and must name the risk owner who accepted the smaller data set
- `--format text` must print sample count, branch, percentile used, raw measured percentile seconds, safety buffer, recommended bound, and the oldest/newest sampled run or rehearsal identifiers
- `--format json` must print one closed object with exactly `projectKey`, `branch`, `sampleSize`, `percentile`, `rawPercentileSeconds`, `safetyBufferSeconds`, `recommendedApprovalToLiveLockMaxDelaySeconds`, `measurementSource`, `oldestSampleRef`, `newestSampleRef`, and `measuredAt`
- exit codes: `0` success; `2` invalid invocation; `3` no usable measurement data; `4` external-read failure
- In the superseded baseline/live-lock model, official release enablement was forbidden until the project's checked-in readiness record referenced by `readinessEvidenceRef` recorded the measurement owner, source window, and output from this helper or an equivalently reviewed repository-owned wrapper around it. Active enablement does not require this helper.

#### 4.1.2 Migration path from legacy repository state

Release workflows are already active, so migration targets the checked-in workflow and descriptor state rather than a pre-implementation baseline. The repository must migrate in one reviewed release-freeze sequence:

1. **Prepare prerequisites off the release path.**
    - land the Day 0 helper/tooling set from §1
    - validate active descriptors, `eng/release/target-instances.yml`, and the checked-in runbooks
    - pre-create only the exact active registry and buddy environments required by each enabled project/target set
    - create the protected tag rulesets for the active official and buddy tag namespaces
    - configure active release/registry environments, trusted-publisher entries, descriptor/catalog validation, and registry-readiness checks for the enabled target set. Historical durable-store, external broker, and external monitor commitments are not active prerequisites unless a later reviewed design reintroduces them
2. **Freeze release traffic.**
    - stop new buddy and official dispatches
    - confirm no active release run or recovery PR is mid-review; historical live-lock checks apply only if a future design reintroduces live locks
    - if any project is already mid-release under legacy tooling, finish or abort it before continuing
    - complete the Day -1 legacy-automation clearance checklist on the branch being migrated:
        - no `official.yml` or other workflow retains unreviewed tag-push triggers outside the active `release/*/v*` release-tag namespace
        - no same-repository reusable workflow still acts as an official publish authorization boundary
        - no extra top-level release entry workflow remains beyond `ci.yml`, `buddy.yml`, and `official.yml`
        - every remaining `pull_request_target` workflow that can affect release
          authorization is metadata-only; dependency-maintenance automation may
          exist only as non-release-authority Renovate-style maintenance with
          least privilege, no release mutation worker calls, and no publish or
          protected-ref bypass credentials
        - the branch already contains the checked-in bootstrap prerequisites from §1 and §4.1, so migration does not pause halfway through an enablement sequence
3. **Land one enabling change set per protected branch.**
    - merge the workflow files, active descriptors/catalog, CODEOWNERS/ruleset changes, and documentation updates together
    - remove or disable every legacy trigger, reusable publish boundary, or release path that is replaced by this design in that same reviewed change set
    - mixed states where legacy release automation and this design both have authority for the same project are forbidden
4. **Validate branch-by-branch in deterministic order.**
    - migrate `main` first, then each maintenance branch in documented order
    - after each branch update, run `ci.yml` validation for the bootstrap surface, release contract, and migration-specific drift checks before proceeding
    - `ci.yml` validates only the branch snapshot under test. It must **not** claim repository-wide proof that every other protected branch is already on the same migration/schema epoch, because a workflow run on one ref cannot authoritatively inspect unmerged branch snapshots for enforcement decisions
    - the cross-branch “no mixed schema / no mixed authority” invariant is therefore enforced by the release freeze plus one reviewed repository-owned migration coordinator record referenced from the schema-migration runbook. That coordinator must list every protected official branch, the required migration epoch for this design revision, and whether that branch has completed the reviewed cutover. Official release traffic must remain frozen until that coordinator says every protected branch for the project set is on the same epoch
5. **Enable project release traffic only after readiness passes.**
    - each project remains disabled until its exact active target set is verified on the authoritative branch: the `src/**/three.release.yml` descriptor, `eng/release/target-instances.yml` catalog entry, reviewed workflow path, protected official/buddy tag rulesets, required GitHub environments, and registry-side trusted-publisher or GitHub Release controls must all match the selected target set
    - phased adoption is still allowed, but only by active capability tier: repositories may land `ci.yml` governance and `buddy.yml` first while official release remains disabled; official release enablement requires the active descriptor/catalog/workflow/tag/environment/registry controls for the selected target set, not the superseded external broker path
    - historical durable-store, broker-readiness, and monitor-bootstrap requirements are future-only in the active direct workflow topology unless a later reviewed design reintroduces that control-plane model
    - if migration validation fails on any protected branch, restore every already-migrated branch to one consistent pre-enable state before reopening releases

There is no supported partial rollout where `official.yml` is direct-job based on one branch while another branch still relies on legacy release orchestration, tag-push triggering, missing admission-state files, or a different migration/schema epoch for the same project. The migration coordinator record, not `ci.yml` alone, is the authoritative repository-wide cutover checklist for that invariant.

### 4.2 Official environment model

The older baseline `production-<project-key>` approval-gate model is superseded
for the active split topology. Current official registry publication gates live
side effects through the active registry environments used by
`release-orchestrate.yml`:

- `github-release`
- `pypi`
- `npmjs-gate` for npmjs human approval
- `npmjs` for npmjs OIDC token scoping
- `rubygems`

Target-specific production-style mechanics may be reintroduced only by a later
reviewed design. The following examples are historical/superseded only and are
not active requirements:

- `production-nuget-<project-key>`
- `production-npm-<project-key>`
- `production-pypi-<project-key>`
- `production-rubygems-<project-key>`
- `production-github-<project-key>`
- `production-ref-write-<project-key>`
- `production-evidence-write-<project-key>`

Those historical subordinate environments were for narrowly scoped credentials or target-specific variables only. They are not active environment requirements.

Superseded workflow-only OIDC drafts used branch-scoped `production-<surface>-<project-key>-<branchScopeKey>` environments. Active targets use release/registry environments instead: `github-release`, `pypi`, `npmjs` (with `npmjs-gate` for npm approval), and `rubygems`.

GitHub Environments are a ref-scoped credential gate, not a workflow-file identity boundary. GitHub does not provide a native checked-in rule that says only one workflow may enter an active release or registry environment such as `github-release`, `pypi`, `npmjs-gate`, `npmjs`, or `rubygems`. Any other workflow on an allowed protected branch that can target the same environment name could reach that environment unless reviewed repository governance prevents it.

Therefore the design distinguishes two layers: the active registry environment gates credential minting, while reviewed workflow files, bootstrap governance, protected refs, and actor separation constrain which jobs are supposed to request those credentials. Historical `production-*` GitHub mutation environments are superseded in the active registry-publish topology; long-lived GitHub App private keys must not be introduced as environment secrets.

Every active release or registry environment that gates approval or token minting must encode a real boundary. The minimum contract is:

- at least one required reviewer user or team
- `prevent self-review` enabled
- deployment-branch policy or equivalent repository-side restriction that allows entry only from the protected official release branches allowed by the checked-in release contract
- explicit documented admin-bypass policy; if any admin bypass is allowed, it is break-glass only and not part of the normal release path
- the reviewer population should be administratively narrower than the routine workflow-dispatch caller population
- npmjs keeps approval and OIDC token scoping separate: `npmjs-gate` is the human approval environment and `npmjs` is the token-minting environment
- any wait timer or approval timeout must be documented against the active registry environment model, not the superseded baseline/live-lock model

The active split topology keeps `official.yml` as the entry and authorization gate, while `.github/workflows/release-orchestrate.yml` hosts GitHub Release and registry publish jobs for GitHub Release, PyPI, npmjs, and RubyGems. PyPI and RubyGems bind their trusted-publishing workflow identity to `release-orchestrate.yml`; npmjs binds the Trusted Publisher to the active caller workflow `official.yml` because npm validates the direct caller for `workflow_call`, even though the publish command runs in `release-orchestrate.yml`. Older direct-publish identity drafts are superseded except for the npm caller-identity binding required by the provider. Active approval occupancy is bounded by the entry workflow's job-level `orchestrate` concurrency group `release/${project_id}/v${release_version}` plus the active release/registry environment gates (`github-release`, `pypi`, `npmjs-gate` / `npmjs`, and `rubygems`), not by workflow-level concurrency or a baseline/live-lock approval window.

The superseded baseline/live-lock occupancy formula used `baselineWaitTimerMinutes` plus `approvalToLiveLockMaxDelaySeconds`; it is historical/future-only and not active registry publication guidance.

When GitHub Actions, environment approval, or provider metadata is unavailable long enough to strand an active registry-environment approval wait, operators use the §7.6 cancellation/escalation thresholds and the reviewed runbooks from §7.5. The decision is based on GitHub Actions run/job/environment metadata plus the absence or presence of active receipt/report evidence; historical degraded-mode suspension records and baseline/live-lock outage rules are not active authority.

### 4.3 Control-plane trust root and preflight sequencing

For a normal official release, the selected `workflow_dispatch` ref or active `push.tags: release/*/v*` tag supplies the trusted workflow/control-plane code and checked-in policy used to authorize the run. The release payload/content target is a separate pinned commit resolved from the optional `target` selector or from the dispatch/tag commit when `target` is empty. Empty `target` may make the trusted dispatch/control-plane SHA and the release target SHA equal, but their roles remain distinct:

- `policy-sha` is the immutable event snapshot commit for the selected protected dispatch ref or active release tag and identifies the trusted workflow/control-plane code
- `release-plan` is the immutable canonical envelope/graph plan consumed by build, test, provenance, tag, and publish jobs
- `release-plan.envelope.plan-id` is the stable plan identifier used by artifact naming and receipt/report correlation
- `release-plan.envelope.commit-sha` is the immutable release target commit whose build outputs are published; for an empty-target normal release it can equal `policy-sha`, while for a non-empty target selector it remains the separately pinned payload snapshot

The frozen `release-plan` is the exact closed-schema envelope/graph object defined in §5.10. It carries descriptor/catalog snapshots, target-instance snapshots, artifact nodes, and publish nodes; it does not carry active `environmentBindings` or `artifactStoreBinding` requirements. Target authorization and confirmation policy are validated from the active descriptor/catalog/workflow-environment surfaces and runbooks rather than from historical durable-store bindings.

`official.yml` is the active entry and authorization gate. It validates the manual inputs, enforces the trusted entry policy, and delegates active package-registry token-minting / publish work to `.github/workflows/release-orchestrate.yml`, which emits the publish receipts for PyPI, npmjs, and RubyGems. Older drafts modeled `preflight-validate`, `official-review-surface`, `baseline-approval-and-audit`, `create-live-lock`, and a full direct `official.yml` mutation graph; that baseline/live-lock model is superseded for the active topology unless a later reviewed design reintroduces it. The active abandonment controls are the entry workflow's job-level `orchestrate` concurrency group plus the registry-environment gates in the orchestrated jobs.

After the active entry workflow resolves the selected ref and delegates to `release-orchestrate.yml`, later publish jobs must not re-resolve a moving branch HEAD for release identity. Historical baseline-approval audit and live-lock checks in this section describe the superseded direct-`official.yml` model only. Active approval evidence comes from the release/registry-specific environment jobs (`github-release`, `pypi`, `npmjs-gate` / `npmjs`, and `rubygems`) and from the orchestrated publish receipts.

#### 4.3.1 Required attestation/provenance profile

This design chooses one concrete attestation format so every implementation and consumer verifies the same trust chain:

- the authoritative format is GitHub Artifact Attestations backed by GitHub's Sigstore trust root, carrying a DSSE-wrapped in-toto statement whose predicate type is SLSA provenance for the immutable artifact set published by the run
- `attestation-verification` must verify, before any publish-capable continuation, that the attestation subjects exactly equal the canonical filename-and-digest bindings from the digest manifest, that the attesting workflow path is `.github/workflows/release-orchestrate.yml` for active jobs, and that the attestation binds to the same repository and current run identity that produced the immutable artifact set. Historical direct-`official.yml` attestation identity is superseded.
- the canonical durable `attestationRef` string format is `github-attestation://<owner>/<repo>/runs/<run-id>/attestations/<attestation-id>`; the identifier must resolve to one GitHub Artifact Attestation record whose verified subjects exactly match the stored subject map
- `artifactLocator`, `attestationRef`, and the exact subject filename-and-digest map together form the canonical publishable artifact identity recorded in the durable bundle, official tag annotation, and blocked-entry evidence
- alternate provenance systems, alternate predicate formats, or opaque provider-specific attestation blobs are out of scope unless this document is explicitly revised first

The following direct official job graph is superseded/future-only and is retained as historical design context. Active `official.yml` delegates publish/token-minting work to `release-orchestrate.yml`; it does not run this baseline/live-lock job graph.

Job sequence:

1. **`preflight-validate`** — no environment
    - validates workflow input `project` as exactly one canonical internal project identity
    - requires the selected `workflow_dispatch` ref to be a branch ref, not a tag ref
    - requires the selected branch to be a protected branch
    - freezes immutable `policy-sha` from the `workflow_dispatch` event snapshot of that selected protected branch
    - reads the checked-in per-project admission/recovery state file from the frozen `policy-sha` and fails closed if the file or schema is missing or invalid
    - historical/future-only: for a new release, older drafts read `.github/repository-release-contract.json`, `three.release.yml`, and `packageManifestPath` from that same frozen snapshot. Active orchestration resolves release identity from active `three.release.yml` descriptors, `eng/release/target-instances.yml`, and ecosystem manifests.
    - for an approved recovery, loads the full frozen `release-plan`, the persisted `lockIdentity`, the blocked-stage discriminator, any existing artifact identity, and machine-readable reviewed recovery authorization from the blocked admission entry; recovery must not rewrite any release-identity field from current checked-in project metadata
    - validates project existence, uniqueness, and single-ecosystem/single-build-kind shape for a new release plan; for recovery it validates that the requested `project-key` and selected protected dispatch branch match the frozen blocked plan being resumed
    - strictly validates `three.release.yml`, target compatibility, target-to-artifact routing completeness, durable-store contract completeness, and target-auth completeness before any environment entry
    - verifies that the selected protected dispatch branch itself matches the authoritative branch rule for the frozen plan:
        - `officialBranchMode = main` requires `main`
        - `officialBranchMode = release-line` requires `release/<project-key>/v<release-line>`
    - derives or verifies the full official release tag ref carried by the frozen plan and computes the canonical `planDigest` only after manifest/package identity equivalence and canonical version normalization both succeed
    - validates the closed schemas in §5.10, §5.11, and §5.12, including that OIDC-backed targets declare exact workflow-enforced `allowedRefClaims`, that those refs contain no wildcard patterns, that each OIDC target records `providerRefClaimSupport`, `providerRefClaimMode`, `providerRefClaimModeRationale` when required, `providerConfigReviewedAt`, machine-readable `providerConfigReviewRef`, and `providerTrustCapabilities`, that `providerConfigReviewedAt` is not later than the current UTC time, not older than 365 days, and that each allowed-ref set is coherent with the project’s `officialBranchMode` and `releaseLine`
    - loads and validates the checked-in per-target `targetConfirmationPolicies` for the selected official targets; those policies are emitted separately from `release-plan` so operators may tune confirmation timing without burning the frozen release identity
    - historical/future-only: older drafts checked `.github/repository-release-contract.json` for release-enabled entries, live-lock requirements, ref-write requirements, evidence-store completeness, and target-auth completeness. Active validation uses descriptors, target catalog entries, registry environments, and release-tag conflicts.
    - performs bounded GitHub-side non-mutating checks of the protected live lock tag `refs/tags/official-lock/<project-key>` and the authoritative official tag `refs/tags/release/<project-key>/v<version>`
    - if a live lock exists while the checked-in per-project admission file still says `ready`, fails closed with a dedicated orphan-lock diagnostic that prints the full lock annotation payload, proposes a blocked-entry JSON template, and applies the authoritative boundary rules from §7.4 using the frozen `planDigest` read from the lock payload itself rather than from any new dispatch input: first query the durable artifact store by that frozen `planDigest`; if no authoritative bundle exists use `blockedStage = pre-provenance`; if authoritative durable state cannot yet prove one complete immutable bundle identity use `blockedStage = provenance-uncertain`; if a bundle exists and no publish-confirmation evidence exists use `blockedStage = post-provenance`; otherwise use `blockedStage = post-confirmation`
    - performs bounded GitHub-side non-mutating checks that every active registry environment named by the frozen plan already exists and matches the required protection policy before any environment entry
    - emits validated outputs including `policy-sha`, the frozen `release-plan`, any persisted `lockIdentity`, validated `targetConfirmationPolicies`, `publishExpectationByTarget`, the blocked-stage discriminator, any persisted blocked artifact identity, and release identity for downstream jobs

2. **`static-analysis`** — no environment; `new-release` only
    - runs after `preflight-validate` only when `recoveryMode = new-release`
    - every blocked recovery mode skips this job entirely; a reviewed recovery must not become permanently impossible only because newer HK rules or control-plane policy changes would reject the same already-frozen payload snapshot today
    - uses the frozen `policy-sha` only for workflow/control-plane files and the frozen `release-plan.payloadSha` for every file that can influence project resolution, version resolution, dependency resolution, build, package, or artifact selection
    - runs `hk check` over the resolved project path from `release-plan.payloadSha`, any payload-scoped shared/root build inputs from `release-plan.payloadSha`, plus the official release control-plane surface from `policy-sha`:
        - from `policy-sha`: `.github/workflows/official.yml`, active `.github/workflows/release-*.yml`, `.github/actions/**`, `eng/scripts/**`, `hk.pkl`, and other pure control-plane rule code
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

| Field                         | Type             | Notes                                                                                                                                                                                   |
| ----------------------------- | ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `projectKey`                  | `string`         | Canonical checked-in project key.                                                                                                                                                       |
| `policySha`                   | `string`         | 40-character lowercase git commit SHA of the frozen control-plane snapshot.                                                                                                             |
| `payloadSha`                  | `string`         | 40-character lowercase git commit SHA of the frozen build/publish payload snapshot.                                                                                                     |
| `packageIdentity`             | `string`         | Exact external package identity.                                                                                                                                                        |
| `version`                     | `string`         | Canonical validated release version.                                                                                                                                                    |
| `officialTag`                 | `string`         | Exact full official tag ref.                                                                                                                                                            |
| `releaseLine`                 | `string \| null` | Frozen release line, or `null` for `main`-authorized releases.                                                                                                                          |
| `planDigest`                  | `string`         | Canonical digest of the frozen `release-plan`.                                                                                                                                          |
| `targets`                     | `string[]`       | Lexicographically sorted exact official target list.                                                                                                                                    |
| `requiredBaselineEnvironment` | `string \| null` | Historical baseline approval environment; `null` in the active registry-environment model.                                                                                              |
| `requiredTargetEnvironments`  | `object`         | Closed object keyed exactly by `targets`, with each value equal to that target’s active registry or GitHub environment name.                                                            |
| `blockedStage`                | `string \| null` | `null` for a new release; otherwise one of `{pre-provenance, provenance-uncertain, post-provenance, post-confirmation}`.                                                                |
| `preProvenanceWarning`        | `string \| null` | Non-null only when `blockedStage = pre-provenance`; otherwise `null`.                                                                                                                   |
| `recoveryContext`             | `object \| null` | `null` for a new release. For recovery, closed object containing `allowedMode`, `evidenceRef`, `authorizationRef`, `authorizedAt`, and `artifactIdentitySummary`.                       |
| `workflowPath`                | `string`         | Exact path `.github/workflows/official.yml`.                                                                                                                                            |
| `runId`                       | `integer`        | GitHub Actions run id for this official dispatch.                                                                                                                                       |
| `runAttempt`                  | `integer`        | Observed GitHub run attempt for this official dispatch; reruns are allowed under the `allow_idempotent` policy and must follow the normal idempotent authorize/orchestrate/report flow. |

Official reruns are not a green no-op path. Target SHA pinning, concurrency, idempotent publish behavior, `force_update_tag` controls, and fail-closed reports still apply. Buddy publication remains single-attempt only.

All integer-valued fields in `official-review-payload`, `release-plan`, checked-in admission state, and confirmation records must be representable exactly as IEEE 754 safe integers in addition to satisfying any narrower range listed in this document; non-integer numbers and out-of-range integers are invalid because the canonical JSON / cross-language interoperability contract depends on exact numeric round-tripping.

When `recoveryContext` is non-null, it is a closed object with exactly these fields:

| Field                     | Type             | Notes                                                                                                                                                 |
| ------------------------- | ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| `allowedMode`             | `string`         | Exact reviewed recovery mode from checked-in state.                                                                                                   |
| `evidenceRef`             | `string`         | Exact blocked-entry evidence reference being approved.                                                                                                |
| `authorizationRef`        | `string`         | Exact reviewed recovery authorization reference.                                                                                                      |
| `authorizedAt`            | `string`         | RFC 3339 UTC timestamp of that checked-in authorization.                                                                                              |
| `artifactIdentitySummary` | `object \| null` | `null` only when no persisted artifact identity exists yet. Otherwise closed object with exactly `artifactLocator`, `attestationRef`, and `subjects`. |

4. **`baseline-approval-and-audit`** — historical/future-only
    - belongs to the superseded baseline/live-lock job graph. Active approval and token scoping are provided by the `release-orchestrate.yml` release/registry environment jobs: `github-release`, `pypi`, `npmjs-gate` / `npmjs`, and `rubygems` as applicable
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

This section is retained as superseded/future-only design history for the older
baseline/live-lock official mutation graph. It is not the active execution graph:
active `official.yml` authorizes entry and delegates registry publication to
`release-orchestrate.yml`.

This section covers the active post-approval execution graph and the
historical direct-job graph it superseded.

The active graph is orchestrated by `.github/workflows/release-orchestrate.yml`.
It consumes the frozen §5.10 envelope/graph `release-plan`, execution sets,
descriptor/catalog snapshots, and policy outputs from the reviewed caller
workflow. It does not require checked-in admission-state files, durable
artifact-store bindings, live-lock tags, or a `release-status` helper.

The active publish-relevant paths are:

- **New release path:** resolve the envelope/graph plan, build/test/package from
  the plan-selected target commit and project snapshot, verify planner-frozen
  artifact names and digests where the registry requires exact bytes, create or
  verify the official release tag, publish through the selected registry or
  GitHub Release nodes, and emit per-target receipts plus `release-report.json`.
- **Reviewed recovery path:** operators classify each publish node from the
  frozen plan identity, run summaries, receipt artifacts, release reports, and
  read-only remote observations. A recovery run may continue only the targets
  whose same resolved package/version/tag/asset identity is absent or explicitly
  safe to complete. Conflicting, missing, corrupt, or tamper-suspect evidence
  routes to the §7.5 abort or break-glass runbooks.
- **Verification-only path:** when receipts and remote evidence already prove a
  target complete for the frozen identity, active recovery records that target
  as satisfied without rebuilding, republishing, or clearing historical locks.

GitHub Actions job conditions must be written so expected skipped upstream jobs
do **not** grant publish authority. Each publish-capable job must consume the
active execution-set outputs from `prepare-release-plan`, check that its target
node is selected and active for the current run, and fail closed on `failure`,
`cancelled`, or unexpected `skipped` upstream states. Environment approval is the
GitHub credential gate, not proof that the reviewed release identity is still
current; in-job checks must revalidate the frozen plan identity, artifact
identity, and registry/GitHub target identity before upload, publish, or receipt
emission.

Historical/future-only: the superseded direct-job design kept approval,
live-lock creation, provenance persistence, tag creation, publish confirmation,
and lock clearing inside one direct `official.yml` graph. The older execution
contract below remains design memory only unless a later reviewed design
reintroduces checked-in admission state, live locks, or durable artifact-store
reconciliation:

4. **One static conditional build-test-package-preparation job**
    - exactly one preparation path runs for the resolved `(ecosystem, buildKind)`
    - the runner contract follows the active workflow code: .NET release variants select `ubuntu-latest`, `macos-latest`, or `windows-latest` from OS/RID dimensions, while current Python, Node, WXT, and Ruby release build workflows use `ubuntu-latest`
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
    - historical/future-only: for a new release, older drafts wrote the immutable artifact bundle, digest manifest, and attestation/provenance record to the durable artifact store using only the credential scoped to `production-evidence-write-<project-key>`. The active topology does not use that production evidence-write environment.
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

9. **Orchestrated publish/token-minting jobs**
    - one hosted publish/token-minting job per active official target in `release-orchestrate.yml`
    - each job mutates exactly one destination
    - each job depends on successful baseline approval, validated preflight outputs, successful build-test-package-preparation, provenance, and tag creation when those stages are required for the selected recovery mode
    - when `publishExpectationByTarget[target] = must-run`, that target's publish job must execute and succeed; an unexpected `skipped` result is a hard failure recorded as `reason = publish-job-failure` rather than being silently treated as a verification-only path
    - each job consumes only the immutable artifact set and digest manifest selected and verified earlier in the run
    - publish jobs must not rebuild, repackage, or substitute files after attestation
    - before any external mutation, each hosted publish/token-minting job must independently re-read `refs/tags/official-lock/<project-key>` and hard-fail with `LOCK_MISSING` or `LOCK_STOLEN` unless the live lock still exists and still carries the same frozen `planDigest` and `lockInstanceToken`
    - within each credentialed publish job, that lock revalidation and audit-payload revalidation must be the first repository-controlled step before any local composite action use
    - the target-specific publish credential or OIDC token must be minted or requested only after that final lock revalidation succeeds so the race window between validation and mutation is minimized
    - `release-plan.payloadSha` is metadata for identity and audit only at publish time; the publish bytes come from the attested or restored immutable artifact set named by the frozen plan and artifact identity
    - `blockedStage = post-confirmation` and `blockedStage = provenance-uncertain` must skip publish jobs entirely because those modes are verification-only or reconciliation-only
    - a hosted publish/token-minting job that cannot obtain or validate its brokered or OIDC credential must fail closed as `publish-job-failure`; it must not fall back to a different credential path or silently downgrade to verification-only behavior

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
    - historical confirmation records contained `recordDigest`, `target`, `planDigest`, `version`, `outcome`, `confirmedAt`, provider/API status, correlation identifiers, monitor backfill annotations, and the strongest remote identity proof available for that target. That durable-store confirmation-record contract is retained only for the superseded model; active success evidence is the current receipt/report/remote-observation set.
    - if all publish jobs completed but `confirm-publish-state` itself fails or times out, operators must treat the project as `blockedStage = post-provenance` unless and until reviewed persisted confirmation records plus external evidence prove every selected target succeeded; once that proof exists, the blocked entry may advance to `post-confirmation` and only the `clear-lock-only` path remains legal
    - recommended defaults for currently enableable official targets are:

| Target                  | `confirmMaxAttempts` | `confirmIntervalSeconds` | `perAttemptBudgetSeconds` | `providerDelayBudgetSeconds` | `confirmTimeoutSeconds` |
| ----------------------- | -------------------: | -----------------------: | ------------------------: | ---------------------------: | ----------------------: |
| `github-release/public` |                    3 |                       10 |                        10 |                            0 |                     110 |
| `npm/npmjs`             |                    5 |                       30 |                        15 |                          300 |                    1020 |
| `pypi/pypi`             |                    5 |                       30 |                        15 |                          300 |                    1020 |
| `rubygems/rubygems-org` |                    5 |                       30 |                        15 |                          300 |                    1020 |

- NuGet registry targets have no default confirmation profile in this revision because `families.nuget.instances: []` makes them unavailable until a reviewed dotnet/NuGet workflow path exists.

11. **`release-complete`**
    - final aggregation gate for the workflow
    - uses an explicit `if:` condition that permits lock clear after verification-only `post-confirmation` runs
    - on successful confirmation of every selected destination, performs a compare-delete check by re-reading `refs/tags/official-lock/<project-key>` and failing closed with `LOCK_MISSING`, `LOCK_STOLEN`, or `lock-integrity-failure` unless the still-live lock matches the frozen `planDigest` and the current authorized run or recovery authority before deleting anything
    - only after that compare-delete check succeeds may it clear the protected live official lock tag for the selected project using the same dedicated repository-ref-write credential
    - the lock-clear operation must use bounded retry with exponential backoff for at most 60 seconds total
    - if lock clear still fails after bounded retry, the run outcome is `published-with-lock-residue`, not a generic re-publish failure; the workflow must emit `lock-clear-failed: true`, keep the lock in place, and route the project to the lightweight `clear-lock-only` recovery mode rather than to full rebuild/re-publish recovery
    - `published-with-lock-residue` is a blocked release outcome, not a warning-only success; it must trigger the §6.4 blocked-entry/issue-creation path with `blockedStage = post-confirmation` and `recovery.allowedMode = clear-lock-only`

Reference timeout contract for `official.yml` (overrideable per project in `.github/repository-release-contract.json`):

| Job                              |                                                      Default `timeout-minutes` |
| -------------------------------- | -----------------------------------------------------------------------------: |
| `preflight-validate`             |                                                                             10 |
| `static-analysis`                |                                                                             15 |
| `official-review-surface`        |                                                                             10 |
| `baseline-approval-and-audit`    |   `ceil(max(baselineWaitTimerMinutes * 60, approvalWaitMaxSeconds) / 60) + 20` |
| `build-test-package-preparation` |                             60 for `csharp-pack`, 30 for all other build kinds |
| `create-live-lock`               |                                                                              5 |
| `attestation-verification`       |                                                                             15 |
| `require-provenance`             |                                                                             10 |
| `create-release-tag`             |                                                                              5 |
| `publish-github-release`         |                                                                             15 |
| `publish-npm-official`           |                                                                             15 |
| `publish-pypi-official`          |                                                                             15 |
| `publish-rubygems-official`      |                                                                             15 |
| `confirm-publish-state`          | `ceil(sum(selectedTargetConfirmationPolicies.confirmTimeoutSeconds) / 60) + 5` |
| `release-complete`               |                                                                              5 |

### 4.5 Official targets

Official filters to the active split-topology target set:

- `npm/npmjs`
- `pypi/pypi`
- `rubygems/rubygems-org`
- `github-release/public`

There is no separate `github:official` target.

`nuget:official` / `nuget/nuget-org` remain reserved historical/future names only. They are not active target catalog entries and must not be enabled in checked-in project config until a later reviewed dotnet/NuGet workflow path adds NuGet target instances and closes the NuGet provider contract.

For the official channel, `github-release/public` attaches to the official tag `release/<project-key>/v<version>`. In the non-force path, a missing tag may be created at the selected target, but an existing tag that points elsewhere must fail closed. When the operator explicitly sets `force_update_tag=true`, the active workflow may retarget the release tag according to that reviewed dispatch input. Same-identity acceptance requires both the protected official tag identity and an exact match between the live GitHub Release asset set and the authoritative artifact identity for the frozen plan by both canonical asset name and digest. Tag-only equality is insufficient.

### 4.6 External-system checks

This design does not depend on extra scheduled readiness workflows or aging snapshot artifacts.

If a provider-specific readiness or authorization check is still required, it must be either:

- a checked-in policy fact in the active descriptor/catalog or workflow-environment runbooks, or
- a bounded same-run check performed by `release-orchestrate.yml` before the corresponding registry publish job requests credentials

No official admission decision may depend on scanning arbitrarily old workflow runs.

### 4.7 Baseline and subordinate environment requirements

- Active release/registry environments required by the selected target set must be pre-created before the workflow is enabled for that project.
- Those active environments must carry the expected protection rules; a missing or unprotected required registry environment is a hard failure.
- The minimum acceptable active environment protection policy is the same minimum contract defined in §4.2.
- Active release/registry environments should be used for approval and narrowly scoped credential/token facts only. They must not be treated as storage for long-lived publication credentials.
- Any environment referenced by the validated plan must also be pre-created before workflow enablement.
- Additional per-target or production-style environments are not part of the active model unless documented explicitly in the active descriptors/catalog and workflow-environment runbooks.
- The superseded baseline `production-<project-key>` wait-timer model is not an active registry publication gate. Any future wait-timer boundary must be reintroduced by a reviewed design update.
- Referencing a missing environment is never an acceptable bootstrap path, because GitHub may auto-create it without the required protection semantics.
- Subordinate environments are not a workflow-path isolation primitive. Their native GitHub protection is ref-scoped, so the design must treat repository governance and reviewed workflow wiring as the controls that keep other allowed-branch workflows from targeting the same environment name.
- Historical/future-only: in the superseded admission/live-lock model, post-approval changes to checked-in admission state did not retroactively cancel an in-flight run by themselves, and live-lock removal or mismatch acted only as a downstream interruption signal. Active cancellation and stale-run handling use dynamic entry concurrency, registry-environment gates, and orchestrator job state instead.

### 4.8 Historical protected repository-ref write contract

This section describes the superseded production/live-lock ref-write model. The
active registry-publish topology uses dynamic entry concurrency, protected release
tags, registry environments, and `release-orchestrate.yml`; it does not use
`production-ref-write-<project-key>` or a live-lock ref unless a later reviewed
design reintroduces them.

The historical model used these concrete GitHub tag refs for protected official repository writes:

- `refs/tags/release/<project-key>/v<version>`
- `refs/tags/official-lock/<project-key>`

Commit-marker tags used by the §4.10 durable artifact store for `oci-registry` and `github-packages` are a separate namespace. They are not mutated by the normal `production-ref-write-<project-key>` path and instead use the dedicated `artifactStoreMarkerWriterActorClass` under the evidence-write contract.

The repository-ref write contract is:

- only `create-live-lock`, `create-release-tag`, `release-complete`, and the documented `clear-lock-only` maintenance path may mutate those refs
- those jobs must enter `production-ref-write-<project-key>`
- the credential used there is a dedicated GitHub App installation token for this repository; by design `GITHUB_TOKEN` is not the protected-ref writer for official release/tag-lock operations
- that GitHub App must hold only the repository permissions required for reviewed protected-tag mutation, must be distinct from the actor used by official `github-release/public`, and the workflow must mint its installation token inside `production-ref-write-<project-key>` through the reviewed external credential broker described in §7.6.1. Because GitHub Environments do not natively bind credentials to one workflow file path, directly storing the long-lived App private key in that branch-scoped environment is no longer part of the normal design. The broker request contract in §7.6.1 is authoritative and the broker must validate at minimum repository, workflow path, job name, run id, run attempt, project key, required environment name, and requested actor class before minting the short-lived installation token
- ref-level restrictions are enforced by the corresponding tag-targeted rulesets, not by the token alone
- the corresponding tag-targeted rulesets must allow only that ref-write GitHub App actor plus the documented break-glass actor to create, update, or delete the protected release-tag and live-lock refs; commit-marker tag namespaces are protected separately and must allow only the dedicated `artifactStoreMarkerWriterActorClass` plus break-glass
- the official `github-release/public` publisher actor must not appear on the protected-tag bypass list; environment names alone are not a separation boundary if the same actor can be minted in both environments
- the official `github-release/public` publisher, the buddy `github-release/public` publisher, and the protected-ref writer must map to separate GitHub App identities in the broker policy and in repository governance; sharing one GitHub App identity across those paths is forbidden because the approval surfaces, key-custody rules, and failure domains are intentionally different
- environment approval does not itself bypass protected tag rules; the credential and actor allowance must already be correct
- GitHub does not document linearizable create/read/delete semantics for refs or tags. This design therefore treats protected-ref state as authoritative only after the operation-specific bounded read-back and stabilization rules succeed on the same GitHub API surface; a one-off `201`, `200`, or `404` by itself never proves durable lock creation, absence, or deletion.
- All protected-ref reads and writes must classify `403`, `429`, abuse-throttle responses, and exhausted `x-ratelimit-*` budgets as retryable or uncertain based on `Retry-After` or reset metadata. Lock protocols must use truncated exponential backoff with full jitter, must never interpret throttling as proof that a lock is absent, and must fail closed if the state cannot be stabilized within the documented wall-clock budget. The bounded stabilization allowance assumed elsewhere in this design is 60 seconds, and any project-specific `approvalToLiveLockMaxDelaySeconds` sizing must include that full allowance rather than assuming the older 30-second heuristic.

### 4.9 Official target authentication contract

Official target authentication must be explicit and target-scoped. Repository-level long-lived publication credentials are out of scope.

| Target                               | Auth class                                | Required subordinate environment                                                   | Current exact-ref support record                                     | Credential rule                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ------------------------------------ | ----------------------------------------- | ---------------------------------------------------------------------------------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `github-release/public`              | GitHub-token API auth                     | `github-release`                                                                   | not applicable                                                       | Use GitHub-native publication only through the reviewed active release path. The active `release-create-github-release.yml` job runs in `environment: github-release`, uses job-scoped `GITHUB_TOKEN` / `GH_TOKEN`, requires `contents: write`, and must not use brokered GitHub App credentials. Historical brokered `production-github-<project-key>` examples are superseded unless a later design reintroduces them.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `nuget:official` / `nuget/nuget-org` | Deferred                                  | unavailable                                                                        | unavailable                                                          | NuGet registry publication is not active in this repository state because the shared target catalog has `families.nuget.instances: []`. The checked-in project config must not include NuGet registry targets, and no run may request `id-token: write` for NuGet.org until a later reviewed dotnet/NuGet workflow path adds target instances and removes this block.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `npm/npmjs`                          | External-registry OIDC trusted publishing | `npmjs-gate` for human approval and `npmjs` for the OIDC token-minting publish job | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only through the active `.github/workflows/official.yml` caller plus `.github/workflows/release-orchestrate.yml` reusable publish job. For `workflow_call`, npm validates the direct caller workflow name, so the active npm Trusted Publisher must bind repository `hcoona/three`, workflow filename `official.yml`, provider environment `npmjs`, and audience `npm:registry.npmjs.org`; the npm publish command still runs in `release-orchestrate.yml`. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`. Both the `official.yml` orchestrate caller job and the `release-orchestrate.yml` npmjs publish job must receive `id-token: write`. If the provider-side trusted-publishing capability cannot satisfy the recorded contract for the project, `npm/npmjs` is not enabled. |
| `pypi/pypi`                          | External-registry OIDC trusted publishing | `pypi`                                                                             | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only through `.github/workflows/release-orchestrate.yml`. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`, must bind repository `hcoona/three`, workflow filename `release-orchestrate.yml`, provider environment `pypi`, and audience `pypi`. Day 0 enablement must still re-confirm that exact value against then-current first-party PyPI trusted-publishing documentation before `pypi/pypi` is enabled. The active reusable-workflow caller job may grant `id-token: write` only as the upper-bound permission for called OIDC jobs; actual token minting remains restricted to the environment-scoped `release-orchestrate.yml` PyPI publish/provenance job.                                                                                                                   |
| `rubygems/rubygems-org`              | External-registry OIDC trusted publishing | `rubygems`                                                                         | `providerRefClaimSupport` = `supported`, `unsupported`, or `unknown` | Use trusted publishing only through `.github/workflows/release-orchestrate.yml`. Workflow-side checks must enforce the checked-in `allowedRefClaims`. Provider-side trust must record `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, and `providerRefClaimModeRationale`, must bind repository `hcoona/three`, workflow filename `release-orchestrate.yml`, provider environment `rubygems`, and audience `rubygems.org`. Until reviewed provider evidence proves exact ref-claim enforcement, the default contract is `providerRefClaimMode = workflow-only` with capabilities at least `repository`, `workflow-path`, and `environment`. The active reusable-workflow caller job may grant `id-token: write` only as the upper-bound permission for called OIDC jobs; actual token minting remains restricted to the environment-scoped `release-orchestrate.yml` RubyGems publish/provenance job.                                                |

The validated official target-auth surface is the checked-in `officialTargetAuthContracts` catalog/descriptor contract, keyed by official target and validated separately from the frozen §5.10 `release-plan`. Each entry contains exactly the closed-schema fields in §5.11, including the required environment name, auth class, allowed credential source, exact workflow-enforced `allowedRefClaims`, provider trust summary, `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, `providerRefClaimModeRationale`, `providerConfigReviewedAt`, and machine-readable `providerConfigReviewRef`. The active `release-plan` must not carry top-level `targetAuthContracts`; auth contracts stay in the separately validated `officialTargetAuthContracts` surface. Confirm-publish retry parameters live in the separately validated `officialTargetConfirmationPolicies` from §5.12 and are intentionally excluded from the frozen release identity. Wildcard ref claims are forbidden. A target with no documented auth contract is not releaseable.

Workflow-side branch enforcement is mandatory for every OIDC-backed target: `preflight-validate` must verify that the selected protected branch is one of the checked-in `allowedRefClaims` for the frozen plan before any publish job may request `id-token: write`.

For every OIDC-backed target, `providerEnvironment` and `providerKey` must both be non-empty exact checked-in values. The checked-in `providerTrustCapabilities` set records which provider-side claims are actually enforced from the closed set `{repository, workflow-path, environment, ref}`, while `providerRefClaimSupport` and `providerSupportsReadOnlyInspection` record per-target/provider support facts consumed by validation and audit.

Provider-side exact ref-claim pinning is preferred and is required whenever the checked-in support record says exact ref claims are `supported` for that target. In that case `providerRefClaimMode` must be `provider-enforced`. `workflow-only` is a lower-assurance compensating-control mode, not a peer security level to `provider-enforced`: it is legal only when `providerRefClaimSupport` is `unsupported` or `unknown`, `providerRefClaimModeRationale` is a non-null machine-readable reason, and the provider-side capability set contains at least `{repository, workflow-path, environment}`.

Provider-side trusted-publishing configuration is repository-external state and therefore part of release readiness, not an implementation detail. Active descriptors/catalog and generated plan data must carry the expected provider-side trust summary and support record for each official target, and any change to workflow path, environment naming, or allowed refs must update both the checked-in active configuration and the provider-side configuration. Active release validation checks internal coherence of those fields, including `providerConfigReviewedAt <= now()` in UTC, and performs bounded provider-side drift checks whenever the checked-in `providerSupportsReadOnlyInspection` flag is `true`.

When `providerSupportsReadOnlyInspection = false`, the workflow has no independent runtime proof that the provider-side configuration still matches the checked-in contract. In that mode, release readiness additionally requires a repository-reviewed manual verification record carried by `providerConfigReviewedAt` and machine-readable `providerConfigReviewRef`, and any official target that uses `workflow-only` ref enforcement must refresh that manual verification at least every 7 days for `standard` projects and at least every 24 hours for `high-assurance` projects. `providerConfigReviewRef` is not a free-form opaque string: it must point to one machine-readable evidence record whose schema is defined in §5.11. A stale, future-dated, missing, or >365-day-old verification record is a hard failure before publication, and §7.6 requires pre-expiry alerting plus best-effort external provider-drift probes rather than waiting for release-time failure. Those reviews, probes, and alerts are compensating controls for provider-side drift; they are not a native provider guarantee. Because `workflow-only` omits provider-enforced exact-ref binding, any target in that mode is explicitly lower assurance than `provider-enforced` even when every compensating control is healthy; repositories should therefore use `workflow-only` only as a reviewed exception path, not as the preferred steady-state posture. The superseded branch-scoped `production-<surface>-<project-key>-<branchScopeKey>` rule is not active; workflow-only targets must use the active registry environment recorded for their provider (`pypi`, `npmjs`, or `rubygems`, with `npmjs-gate` for npm approval where applicable) unless a later reviewed design reintroduces branch-scoped environments. GitHub Release uses `github-release` rather than OIDC trusted publishing. For those `workflow-only` targets, the active provider-drift probe cadence is assurance-sensitive: `high-assurance` projects require best-effort drift probes at least once per hour, while `standard` projects may use a period up to 24 hours.

### 4.10 Durable artifact store contract

This durable artifact store contract is historical/future-only for the superseded
admission/live-lock recovery model. Active release validation relies on
`release-orchestrate.yml` receipts, registry evidence, and descriptor/catalog
state rather than requiring this store before enablement. In the historical
model, placeholder URIs such as `artifact-store://...` were descriptive only, and
an enabled project had to bind them to one of these concrete backend classes:

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
- for `backendClass = oci-registry`, the historical `create-if-absent` path uploaded the immutable bundle as digest-addressed OCI content inside the configured repository, performed mandatory read-back verification, and then created exactly one commit-marker Git tag named `refs/tags/<commitMarkerTagPrefix><planDigest-hex>` only after that verification succeeded, where `planDigest-hex` was the 64 lowercase hexadecimal suffix of `planDigest` with the `sha256:` prefix removed. That commit-marker tag write was performed by the dedicated `artifactStoreMarkerWriterActorClass`, not by `actors.refWriterActorClass`. The superseded `production-evidence-write-<project-key>` environment was part of that older broker path, not an active requirement. `get-by-planDigest` resolved the authoritative bundle exclusively through that marker tag, and a missing marker meant the bundle was absent even if uncommitted OCI blobs or manifests were uploaded. A marker tag that resolved to different verified bundle metadata was storage corruption and a hard failure. The runbook and external monitor defined OCI orphan-upload detection as reconciliation between visible uploaded bundle/manifests and visible commit-marker tags.
- for `backendClass = azure-blob-storage`, every authoritative `create-if-absent`, `get-by-planDigest`, `verify-digest`, and `put-confirmation` operation must use the storage account's primary endpoint only. RA-GRS or other secondary reads may be used for non-authoritative diagnostics only and must be labeled stale/non-authoritative. Both the immutable bundle blob and the commit-marker blob must use the block-blob type; append blobs and page blobs are forbidden. `create-if-absent` must write the immutable bundle under the configured `blobPrefix`, verify the uploaded bytes, and only then create exactly one commit-marker block blob named `<commitMarkerBlobPrefix><planDigest-hex>.json` on the primary endpoint using an atomic create-if-absent operation equivalent to `If-None-Match: *`. `get-by-planDigest` must resolve the authoritative bundle exclusively through that marker blob, and a missing marker means the bundle is absent even if payload blobs or staged blocks were uploaded earlier. When the bundle upload uses staged blocks (`Put Block` + `Put Block List`), the workflow must stage them under one unique payload blob name per attempted write; uncommitted blocks, abandoned staged blocks, or failed staging blobs are not authoritative state and must be treated as orphan candidates by the backend-specific runbook until they are cleaned up or expire. The marker blob must contain at minimum the authoritative bundle locator, the frozen `planDigest`, the persisted digest-manifest digest, and the attestation/provenance reference so recovery can rehydrate one complete verified bundle identity without historical run scans.
- for `backendClass = github-packages`, only the container-backed GitHub Packages/ghcr surface was supported in the historical model. `create-if-absent` uploaded the immutable bundle as a digest-addressed package version, then created exactly one commit-marker Git tag named `refs/tags/<commitMarkerTagPrefix><planDigest-hex>` only after read-back verification succeeded. That commit-marker tag write was performed by the dedicated `artifactStoreMarkerWriterActorClass`, not by `actors.refWriterActorClass`. The superseded `production-evidence-write-<project-key>` environment was part of that older broker path, not an active requirement. `get-by-planDigest` resolved the authoritative bundle exclusively through that marker tag, and a missing marker meant the bundle was absent even if uncommitted package bytes were uploaded.
- `get-by-planDigest` must resolve the authoritative `artifactLocator`, the exact `github-attestation://...` `attestationRef`, the subject filename-and-digest map, and every immutable per-target confirmation record previously persisted with `put-confirmation(planDigest, target, record)` without requiring historical workflow-run scans
- `verify-digest` must prove that the fetched bundle still matches the expected subject filename-and-digest bindings before any restored bytes are published
- `put-confirmation` must persist one immutable per-target confirmation record under the same frozen `planDigest`. Each record carries a canonical `recordDigest` over the full closed record. Retrying the exact same `recordDigest` is allowed and must be idempotent; attempting to replace a different record for the same target/outcome boundary is a hard conflict. After any ambiguous timeout or connection loss, the workflow must resolve the result by reading the existing record and comparing `recordDigest` before retrying. For weak-proof outcomes such as `digest-proof-unavailable`, the workflow may persist at most one conservative uncertain record for that target in that run and must stop in blocked state rather than oscillating between competing records. Later recovery runs must not keep rewriting fresh uncertain records for the same target/plan; they may either discover stronger proof and persist that exact stronger record, or stop after one reviewed verification pass and require an explicit reviewed terminal disposition (`post-confirmation` under a target policy that accepts the available proof, or `recovery.approvalState = aborted`). Indefinite weak-proof retry loops are forbidden. Those records are authoritative recovery evidence for advancing a blocked release from `post-provenance` to `post-confirmation`
- every write attempt uses at most 3 attempts with exponential backoff, 60 seconds maximum per attempt, and 180 seconds maximum wall-clock time for the whole operation
- every successful write must be followed by mandatory read-back verification before the workflow may emit `artifactLocator` or `attestationRef`
- if the store is unavailable or verification fails, the workflow must fail fast, emit a structured error code such as `ARTIFACT_STORE_UNAVAILABLE`, `ARTIFACT_STORE_DIGEST_MISMATCH`, or `ARTIFACT_STORE_TIMEOUT`, keep the live lock in place, and require checked-in blocked-state evidence rather than silently degrading to ephemeral GitHub Actions artifacts
- historical write credentials were available only in `production-evidence-write-<project-key>` and had to be short-lived OIDC-issued or equivalently brokered credentials. That production evidence-write environment is not active in the current registry publication topology.
- recovery reads must use a read-only credential administratively narrower than the write credential
- blocked-release bundles, attestation records, and persisted confirmation records must be retained until the blocked entry is cleared, and never less than one year from the blocked entry’s `updatedAt`
- successful-release bundles, attestation records, and persisted confirmation records must be retained for at least two years from official tag creation, or longer when the checked-in repository release contract declares a longer retention period
- every enabled backend class must have documented capacity/quota monitoring, orphan-upload cleanup procedures, credential-rotation procedures, retention/immutability verification, and a backend-specific disaster-recovery runbook before official release is enabled
- the checked-in repository release contract must also declare the durable-store resilience strategy for each enabled project: either a second independent immutable copy or a reviewed backup/replication plan with explicit RTO/RPO values. A project is not release-ready until that resilience strategy exists and the latest required restore drill from §7.5 has passed
- when `backendClass = azure-blob-storage` and the declared resilience target is at least `RPO <= 15 minutes` / `RTO <= 60 minutes`, the reviewed strategy must include at minimum: region-loss-tolerant storage or a second independent immutable copy outside the primary failure domain; blob versioning or an equivalent immutable-history mechanism for commit markers and confirmation records; a documented backup/export cadence no worse than 15 minutes for whichever metadata would otherwise be lost on regional failover; and a rehearsed operator failover/restore procedure that re-establishes authoritative primary-endpoint reads within 60 minutes. If the repository cannot currently prove all four properties, it must declare weaker RPO/RTO targets instead of implying the stronger pair
- for `backendClass = github-packages`, the runbook must cover orphan uploaded package versions whose commit-marker tag was never written, marker/tag divergence, and cleanup of uncommitted versions after failed writes

## 5. Historical Release Configuration Contract

The `<project-root>/three.release.yml` contract in this section is superseded by
active `three.release.yml` descriptors. It is retained as historical/future-only
reference material.

### 5.1 Schema

```json
{
    "schemaVersion": 1,
    "packageIdentity": "@three/example-project",
    "packageManifestPath": "src/example-project/package.json",
    "buildKind": "node-npm",
    "officialBranchMode": "release-line",
    "releaseLine": "1.2",
    "targets": ["npm/github-packages", "npm/npmjs", "github-release/public"],
    "artifacts": {
        "package": { "kind": "npm-package" }
    },
    "targetArtifacts": {
        "npm/github-packages": ["package"],
        "npm/npmjs": ["package"],
        "github-release/public": ["package"]
    },
    "npmAccessHint": "public"
}
```

### 5.2 Fields

| Field                 | Type       | Required      | Description                                                                                                                                                                                   |
| --------------------- | ---------- | ------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------- | ---------------------------- |
| `schemaVersion`       | `number`   | Yes           | Must be `1`.                                                                                                                                                                                  |
| `packageIdentity`     | `string`   | Yes           | Exact external package identifier published to the ecosystem.                                                                                                                                 |
| `packageManifestPath` | `string`   | Yes           | Explicit repo-relative path to the manifest or project file that defines `packageIdentity`; the workflow validates exact identity equivalence from this file before `planDigest` is computed. |
| `buildKind`           | `string`   | Yes           | Closed set `{csharp-pack, python-package, node-npm, node-wxt, ruby-gem}`.                                                                                                                     |
| `officialBranchMode`  | `string`   | Yes           | Closed set `{main, release-line}` defining which protected branch shape may authorize official releases.                                                                                      |
| `releaseLine`         | `string`   | Conditionally | Required when `officialBranchMode = release-line`; forbidden when `officialBranchMode = main`; must match `(0                                                                                 | [1-9][0-9]\*)\.(0 | [1-9][0-9]\*)` when present. |
| `targets`             | `string[]` | Yes           | Non-empty array of unique publish targets in active `family/instance-id` format. Historical `ecosystem:destination` / colon keys are superseded.                                              |
| `artifacts`           | `object`   | Yes           | Non-empty artifact catalog keyed by checked-in artifact alias.                                                                                                                                |
| `targetArtifacts`     | `object`   | Yes           | Exact target-to-artifact routing map.                                                                                                                                                         |
| `npmAccessHint`       | `string`   | No            | Optional checked-in npm access hint for `node-npm` projects declaring `npm/*`; closed set `{public, restricted}`.                                                                             |

### 5.3 Validation rules

- `three.release.yml` must be valid JSON.
- `schemaVersion` must equal `1`.
- `packageIdentity` must be present and non-empty.
- `packageManifestPath` must be present, must normalize to exactly one repo-relative manifest or project file under the resolved project root after path normalization and symlink resolution, and that exact file must resolve the same `packageIdentity` observed by the ecosystem-specific resolver.
- `preflight-validate` must perform the `packageManifestPath` identity-equivalence check before target filtering, before version/tag derivation, and before `planDigest` computation. Any mismatch between `three.release.yml.packageIdentity`, the manifest-resolved identity, and the repository-contract-resolved project identity is a hard failure.
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
- `npmAccessHint` is legal only for `node-npm` projects that declare at least one `npm/*` target.
- Because release workflows are already active, removing or changing a target must be delivered as a reviewed contract migration; no silent backward-compatibility shim is assumed.

### 5.4 Supported targets

| Target                     | Channel use        | Processed by                                       | Description                                         | Conservative version-burn behavior                                                                                                                                                          |
| -------------------------- | ------------------ | -------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm/github-packages`      | Buddy only         | `release-orchestrate.yml` split path               | Publish npm tarball to GitHub Packages npm registry | Repository-owned delete-capable package surface, but reuse is allowed only after explicit cleanup or same-identity proof.                                                                   |
| `npm/npmjs`                | Official only      | `release-orchestrate.yml` split token-minting path | Publish npm tarball to npmjs                        | Treat the version as effectively burned once same-identity cannot be proved; deprecate is available, while delete/unpublish is not a dependable normal-path recovery tool.                  |
| `pypi/pypi`                | Official only      | `release-orchestrate.yml` split token-minting path | Publish wheel/sdist to PyPI                         | Treat published version/file identities as burned for reuse on uncertainty; operator cleanup may yank, but the design must not rely on deletion-and-republish as the routine recovery path. |
| `rubygems/github-packages` | Buddy only         | `release-orchestrate.yml` split path               | Publish gem to GitHub Packages RubyGems host        | Repository-owned delete-capable package surface, but reuse is allowed only after explicit cleanup or same-identity proof.                                                                   |
| `rubygems/rubygems-org`    | Official only      | `release-orchestrate.yml` split token-minting path | Publish gem to RubyGems.org                         | Treat uncertain or differing same-version publication as burned for automatic reuse; operator cleanup may yank, but rerun with changed bytes is forbidden.                                  |
| `github-release/public`    | Buddy and official | `release-orchestrate.yml` split path               | Publish release assets to GitHub Releases           | Delete-capable release surface; reuse is allowed only after exact release/tag cleanup or same-identity proof.                                                                               |

`pypi:testpypi` and `github:official` are not supported targets. `nuget:gpr`, `nuget:official`, `nuget/nuget-org`, and `nuget/github-packages` remain deferred/unavailable until a later reviewed dotnet/NuGet workflow path adds NuGet target instances.

### 5.5 Ecosystem/build-kind target compatibility matrix

The mapping below is authoritative and total for v1. `ecosystem` is derived from `buildKind`; it is not an independently configurable field anywhere in the checked-in release metadata.

| Resolved ecosystem | `buildKind`      | Allowed targets                                                                 |
| ------------------ | ---------------- | ------------------------------------------------------------------------------- |
| `csharp`           | `csharp-pack`    | `github-release/public` only until a reviewed dotnet/NuGet workflow path exists |
| `python`           | `python-package` | `pypi/pypi`, `github-release/public`                                            |
| `jsts`             | `node-npm`       | `npm/*`, `github-release/public`                                                |
| `jsts`             | `node-wxt`       | `github-release/public`                                                         |
| `ruby`             | `ruby-gem`       | `rubygems/*`, `github-release/public`                                           |

### 5.6 Version resolution and validator contract

Version validation is ecosystem-aware. The workflow must first resolve the project’s canonical ecosystem/build-kind identity, then run exactly one validator family for that resolved release path:

| Resolved ecosystem | `buildKind`      | Canonical version source                                                                                                                          | Required validator family        |
| ------------------ | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------- |
| `csharp`           | `csharp-pack`    | The releasable normalized NuGet package version resolved by the canonical .NET packaging toolchain for the releasable `.csproj` at the frozen SHA | NuGet package version validator  |
| `python`           | `python-package` | The releasable version resolved by the canonical Python packaging metadata/toolchain at the frozen SHA                                            | PEP 440 public version validator |
| `jsts`             | `node-npm`       | The releasable version resolved by the canonical Node/npm release path at the frozen SHA                                                          | npm SemVer validator             |
| `jsts`             | `node-wxt`       | The releasable version resolved by the canonical Node/WXT release path at the frozen SHA                                                          | npm SemVer validator             |
| `ruby`             | `ruby-gem`       | The releasable gem version resolved by the canonical RubyGems release path at the frozen SHA                                                      | RubyGems/Gem::Version validator  |

For `csharp-pack`, the canonical version is the exact normalized NuGet public package version string that the pack/push toolchain would publish. The official tag `release/<project-key>/v<version>` must use that normalized form exactly; a raw project-file literal that normalizes differently is not a distinct release identity and must never appear in the official tag namespace.

For `node-wxt`, the canonical releasable version and package identity both come from the exact `package.json` located at `packageManifestPath`. `node-wxt` is the release path for built web-extension artifacts, not for npm-registry publication. Its build contract produces redistributable release assets for `github-release/public` only; a project that needs npm publication must use `node-npm` instead of `node-wxt`.

If a `node-wxt` project also needs distribution through Chrome Web Store or another browser-store surface outside this design, that store may impose an additional manifest-version format such as Chrome’s four-integer requirement. This design does not derive or validate any browser-store-specific version mapping; it freezes only the repository’s canonical `package.json.version` value used for the GitHub Release path, and any separate store-specific version translation must be reviewed as a distinct out-of-scope release surface.

For WXT releases, the active descriptor/profile artifact list is the whitelist for routed browser-release assets. Browser zip basenames are allowed only for descriptor/profile-declared `browser-zip` artifact IDs, currently `${project}-${version}-{chrome,firefox,edge}.zip` for the corresponding declared browser IDs. `${project}-${version}-sources.zip` is allowed only when the descriptor profile artifact list declares `firefox-sources`. Each browser zip payload must also carry a root `manifest.json` whose `version` matches the resolved browser-manifest version derived from the canonical `package.json.version`.

- Valid `csharp-pack` frozen `version` examples: `1.2.3`, `1.2.3-rc.1`.
- Invalid `csharp-pack` frozen `version` examples: `v1.2.3` (leading tag prefix belongs only to the Git ref), `1.2.3+build.5` (NuGet build metadata is not part of the canonical published version identity).

### 5.7 Artifact routing contract

The build workflow and release metadata must together define exactly which immutable files may reach which destinations.

- `artifacts` is the checked-in per-project catalog of artifact aliases.
- Each alias declares one canonical artifact `kind`.
- The build workflow must emit digest-manifest entries keyed by those aliases only. Each entry must include the canonical output filename used for publication and the canonical `sha256:<64 lowercase hex>` digest of that file.
- A publish job may consume only the aliases listed for its target in `targetArtifacts`.

Supported artifact kinds by `buildKind`:

| `buildKind`      | Supported artifact kinds                 |
| ---------------- | ---------------------------------------- |
| `csharp-pack`    | `nuget-package`, `github-release-asset`  |
| `python-package` | `wheel`, `sdist`, `github-release-asset` |
| `node-npm`       | `npm-package`, `github-release-asset`    |
| `node-wxt`       | `github-release-asset`                   |
| `ruby-gem`       | `ruby-gem`, `github-release-asset`       |

Destination compatibility rules:

- Active `nuget/*` targets are deferred; future NuGet registry targets may reference only `nuget-package`.
- `npm/*` targets may reference only `npm-package`.
- `pypi/pypi` may reference only `wheel` and/or `sdist`.
- `rubygems/*` targets may reference only `ruby-gem`.
- `github-release/public` may reference any explicitly declared artifact aliases, but only those aliases. Recovery and confirmation logic must prove exact GitHub Release asset identity by both canonical filename and digest using the persisted artifact identity.

### 5.8 Project resolution contract

The active project resolution contract starts from the `project` workflow input,
the selected ref or tag, and the checked-in `three.release.yml` descriptor for
the resolved project. The older `project-key` /
`.github/repository-release-contract.json` resolution model is superseded.

- Active `project` values must resolve to exactly one descriptor-owned project identity; no workflow or helper may infer a different package identity by lowercasing, de-scoping, or otherwise normalizing the input.
- `packageIdentity` is the external package identity and may differ from the active project identity.
- Historical `project-key` values in older examples are not active workflow inputs.
- `packageManifestPath` identity equivalence is part of project resolution itself: `preflight-validate` must resolve the canonical manifest file, read package identity and version from that exact file, and prove exact string equality before it constructs the frozen `release-plan` or computes `planDigest`.
- C# projects resolve by the `PackageId` declared in the releasable `.csproj` at `packageManifestPath`. Recommended resolver: `dotnet msbuild <path> -getProperty:PackageId`.
- Python projects resolve by `[project].name` at `packageManifestPath`. Recommended resolver: `python - <<'PY'` using `tomllib` to read `pyproject.toml`.
- `jsts` projects resolve by `package.json.name` at `packageManifestPath`. Recommended resolver: `node -p "require('./package.json').name"` executed from the manifest directory.
- `node-wxt` uses that same `package.json` identity source at `packageManifestPath`, and its canonical releasable version is `package.json.version` validated with npm SemVer rules.
- `node-wxt` build outputs must be releasable extension artifacts routed only as `github-release-asset` entries. The design does not treat `node-wxt` as an npm-registry publish path.
- Ruby projects resolve by the exact `packageIdentity` declared by the `.gemspec` at `packageManifestPath`. Recommended resolver: `ruby -e 'spec = Gem::Specification.load(ARGV[0]); abort unless spec; puts spec.name' <path>`.
- A resolved project root must map to exactly one supported ecosystem and exactly one supported `buildKind`.
- No match, ambiguous match, unsupported ecosystem, unsupported build kind, or multi-language/multi-build-kind match is a hard failure.
- `<project-root>/three.release.yml` is required; there is no inheritance or upward fallback.

### 5.9 Historical repository release contract

The repository-wide `.github/repository-release-contract.json` model in this
section is superseded by active `three.release.yml` descriptors plus
`eng/release/target-instances.yml`. The schema below is retained only as
historical/future-only reference material.

Top-level schema:

| Field           | Type     | Required | Notes                                                                                         |
| --------------- | -------- | -------- | --------------------------------------------------------------------------------------------- |
| `schemaVersion` | `number` | Yes      | Must equal `1`.                                                                               |
| `projects`      | `object` | Yes      | Object keyed by canonical `project-key`. Each value uses the closed per-project schema below. |
| `prTrustModel`  | `object` | Yes      | Closed object recording the repository PR trust rules consumed by `ci.yml`.                   |

Per-project schema (`projects.<project-key>`) is also closed and contains exactly these fields:

| Field                                  | Type             | Required | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| -------------------------------------- | ---------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `projectPath`                          | `string`         | Yes      | Canonical repo-relative project root.                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `assuranceProfile`                     | `string`         | Yes      | Closed set `{standard, high-assurance}`. `standard` is the default profile sized for normal open-source / small-team operation; `high-assurance` opts into the stricter drill cadence and offline custody expectations from §7.5.                                                                                                                                                                                                                                                                            |
| `releaseEnabled`                       | `boolean`        | Yes      | `true` enables official release consideration.                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `buddyAuthorizedRefs`                  | `string[]`       | Yes      | Exact buddy-authorized branch refs; lexicographically sorted; wildcard refs forbidden; must be non-empty when any buddy target is enabled and must otherwise be the empty array.                                                                                                                                                                                                                                                                                                                             |
| `buddyEnvironments`                    | `object`         | Yes      | Closed object keyed by enabled active buddy target names. npm and RubyGems GitHub Packages targets map to their active package environments; `github-release/public` maps to `github-release` only after a later reviewed enablement because active buddy GitHub Release publication is currently fail-closed. Historical `buddy-<surface>-<project-key>` environments are superseded. NuGet buddy target names are reserved/deferred while `families.nuget.instances: []`.                                  |
| `officialEnvironments`                 | `object`         | Yes      | Closed object containing `baseline`, `refWrite`, `evidenceWrite`, and `targets`. `targets` is a closed object keyed by official target name to the exact subordinate environment name for the current branch snapshot; `workflow-only` targets must use the branch-scoped naming rule defined below.                                                                                                                                                                                                         |
| `officialJobTimeoutMinutes`            | `object`         | No       | Closed object of positive integer minute overrides keyed only by the formal timeout keys defined below.                                                                                                                                                                                                                                                                                                                                                                                                      |
| `baselineWaitTimerMinutes`             | `number`         | Yes      | Explicit reviewer wait timer in integer minutes. Must be in the inclusive range `1..1440`. `60` is the recommended default; values above `240` require the checked-in machine-readable `baselineWaitTimerJustification`.                                                                                                                                                                                                                                                                                     |
| `baselineWaitTimerJustification`       | `string \| null` | Yes      | Machine-readable kebab-case justification for `baselineWaitTimerMinutes > 240`. Must be `null` when the wait timer is `<= 240`. Recommended values include `change-freeze-window`, `cross-time-zone-review`, `regulated-release-window`, `release-train-coordination`, `security-incident-response`, and `on-call-capacity-constraint`.                                                                                                                                                                      |
| `approvalWaitMaxSeconds`               | `number`         | Yes      | Maximum wall-clock time an official run may remain waiting for active registry-environment approval while holding the dynamic `official.yml` entry concurrency group. Active §7.6 diagnostics and reviewed runbooks use GitHub Actions/environment metadata to cancel or escalate runs that exceed this bound.                                                                                                                                                                                               |
| `approvalToLiveLockMaxDelaySeconds`    | `number`         | Yes      | Historical/superseded field for the older live-lock model. In the active topology, readiness timing evidence must instead cover entry authorization, orchestrator startup, registry-environment approval, and publish/token-minting timing for the selected active target set.                                                                                                                                                                                                                               |
| `approvalToLiveLockDelayJustification` | `string \| null` | Yes      | Machine-readable kebab-case justification required when `approvalToLiveLockMaxDelaySeconds > 900`; otherwise `null`.                                                                                                                                                                                                                                                                                                                                                                                         |
| `readinessEvidenceRef`                 | `string`         | Yes      | Non-empty reviewed repository-relative path or durable locator naming the project’s authoritative readiness record. That record carries the measured approval-delay evidence, any sub-10-sample waiver for §4.1.1, the normal or exceptional `approvalWaitMaxSeconds` justification when smaller than the recommended `+1800` buffer, any `approvalToLiveLockMaxDelayJustification` support package, the latest required exercise evidence, and any temporary monitor-bootstrap exception allowed by §4.1.2. |
| `protectedRefs`                        | `object`         | Yes      | Closed object containing `officialTagPattern`, `buddyTagPattern`, and `liveLockRef`.                                                                                                                                                                                                                                                                                                                                                                                                                         |
| `actors`                               | `object`         | Yes      | Closed object containing `refWriterActorClass`, `artifactStoreMarkerWriterActorClass`, `githubReleasePublisherActorClass`, `buddyGithubReleasePublisherActorClass`, and `breakGlassActorClass`.                                                                                                                                                                                                                                                                                                              |
| `artifactStore`                        | `object`         | Yes      | Closed object using the §4.10 durable-store discriminated-union schema below.                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `buddyTargetAuthContracts`             | `object`         | Yes      | Closed object keyed by buddy targets only. Each value uses the §5.11 closed target-auth schema.                                                                                                                                                                                                                                                                                                                                                                                                              |
| `officialTargetAuthContracts`          | `object`         | Yes      | Closed object keyed by official targets only. Each value uses the §5.11 closed target-auth schema.                                                                                                                                                                                                                                                                                                                                                                                                           |
| `officialTargetConfirmationPolicies`   | `object`         | Yes      | Closed object keyed by official targets only. Each value uses the §5.12 closed confirmation-policy schema. These operational settings are not copied into `release-plan`.                                                                                                                                                                                                                                                                                                                                    |
| `breakGlass`                           | `object`         | Yes      | Closed object naming the required two-person execution mechanism, the mandatory split-control custody path, the checked-in runbook reference, and the incident-record requirements from §7.5.                                                                                                                                                                                                                                                                                                                |

`artifactStore` is a closed discriminated union keyed by `backendClass`.

`branchScopeKey` is a historical/superseded branch-scope suffix from the older production-environment model. Active official OIDC targets do not use branch-scoped production environments: `requiredEnvironment` and `providerEnvironment` use the active registry environment names (`pypi`, `npmjs`, or `rubygems`, with `npmjs-gate` for npm approval where applicable). GitHub Release uses `github-release`.

Common required fields for every backend:

| Field                     | Type             | Required | Notes                                                                                   |
| ------------------------- | ---------------- | -------- | --------------------------------------------------------------------------------------- |
| `backendClass`            | `string`         | Yes      | Closed set `{oci-registry, azure-blob-storage, github-packages}`.                       |
| `bundleFormatVersion`     | `number`         | Yes      | Must equal `1`.                                                                         |
| `writeEnvironment`        | `string \| null` | Yes      | Historical evidence-write environment; `null` in the active registry-environment model. |
| `readCredentialScope`     | `string`         | Yes      | Non-empty machine-readable read-only credential scope name.                             |
| `blockedRetentionDays`    | `number`         | Yes      | Integer `>= 365`.                                                                       |
| `successfulRetentionDays` | `number`         | Yes      | Integer `>= 730`.                                                                       |

Backend-specific required fields:

| `backendClass` value | Additional required fields                                                                                                                                                                                                                                             |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `oci-registry`       | `repository: string` — exact immutable OCI repository used for bundles; `commitMarkerTagPrefix: string` — prepended to the 64-hex `planDigest` suffix when forming the authoritative commit-marker Git tag ref `refs/tags/<commitMarkerTagPrefix><planDigest-hex>`     |
| `azure-blob-storage` | `accountUrl: string`, `container: string`, `blobPrefix: string`, `commitMarkerBlobPrefix: string`                                                                                                                                                                      |
| `github-packages`    | `packageType: string` with closed set `{container}`, `packageName: string`, `commitMarkerTagPrefix: string` (prepended to the 64-hex `planDigest` suffix when forming the authoritative commit-marker Git tag ref `refs/tags/<commitMarkerTagPrefix><planDigest-hex>`) |

Historical `officialJobTimeoutMinutes` is a closed object with only these keys in
the superseded baseline/live-lock model:

| Key                              | Type                     | Default source     |
| -------------------------------- | ------------------------ | ------------------ |
| `preflight-validate`             | positive integer minutes | §4.4 timeout table |
| `static-analysis`                | positive integer minutes | §4.4 timeout table |
| `official-review-surface`        | positive integer minutes | §4.4 timeout table |
| `baseline-approval-and-audit`    | positive integer minutes | §4.4 timeout table |
| `build-test-package-preparation` | positive integer minutes | §4.4 timeout table |
| `attestation-verification`       | positive integer minutes | §4.4 timeout table |
| `create-live-lock`               | positive integer minutes | §4.4 timeout table |
| `require-provenance`             | positive integer minutes | §4.4 timeout table |
| `create-release-tag`             | positive integer minutes | §4.4 timeout table |
| `publish-github-release`         | positive integer minutes | §4.4 timeout table |
| `publish-npm-official`           | positive integer minutes | §4.4 timeout table |
| `publish-pypi-official`          | positive integer minutes | §4.4 timeout table |
| `publish-rubygems-official`      | positive integer minutes | §4.4 timeout table |
| `confirm-publish-state`          | positive integer minutes | §4.4 timeout table |
| `release-complete`               | positive integer minutes | §4.4 timeout table |

Missing keys use the documented defaults. Unknown keys are hard failures.

`breakGlass` is a closed object with exactly these fields:

| Field                         | Type       | Required | Notes                                                                                                                                                                                                                                   |
| ----------------------------- | ---------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `executionMechanism`          | `string`   | Yes      | Closed set `{github-environment-single-approval-plus-offline-split-control}`. The GitHub environment portion is an additional in-platform gate only; the actual two-person control comes from the mandatory offline split-control path. |
| `incidentTicketRequired`      | `boolean`  | Yes      | Must be `true`.                                                                                                                                                                                                                         |
| `actorClassRef`               | `string`   | Yes      | Must exactly equal `actors.breakGlassActorClass`.                                                                                                                                                                                       |
| `runbookRef`                  | `string`   | Yes      | Exact reviewed repository-relative path or approved URL for the break-glass and cleanup runbook index used for this project.                                                                                                            |
| `offlineCustodyMechanism`     | `string`   | Yes      | Closed set `{sealed-secret-split-control, hsm-split-control, password-manager-split-control}` describing the out-of-band fallback path used when GitHub control-plane approval or workflow execution is unavailable.                    |
| `offlineControlledMaterial`   | `string`   | Yes      | Non-empty machine-readable name of the exact secret/key package placed under split control, such as the break-glass GitHub App private key, broker signing key, or encrypted recovery package.                                          |
| `offlineCustodians`           | `string[]` | Yes      | Closed non-empty list of named repository administrators or security contacts who jointly control the out-of-band path; at least two distinct custodians are required.                                                                  |
| `offlineEvidenceRequirements` | `string[]` | Yes      | Closed non-empty subset of `{incident-ticket, control-plane-outage-evidence, requested-action, before-after-state, operator-identity, approver-identity}`.                                                                              |

`prTrustModel` is a closed object with exactly these fields:

| Field                                | Type      | Required | Notes                                                                                                                                                                                                                       |
| ------------------------------------ | --------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `untrustedPullRequestEvent`          | `string`  | Yes      | Must equal `pull_request`.                                                                                                                                                                                                  |
| `allowPullRequestTargetMetadataOnly` | `boolean` | Yes      | Records whether metadata-only `pull_request_target` usage is allowed.                                                                                                                                                       |
| `forkSecretsEnabled`                 | `boolean` | Yes      | Must be `false`.                                                                                                                                                                                                            |
| `forkPrivilegedWriteTokensEnabled`   | `boolean` | Yes      | Must be `false`.                                                                                                                                                                                                            |
| `bootstrapCodeOwnerReviewRequired`   | `boolean` | Yes      | Must be `true`; records that the bootstrap-governance surface uses a dedicated CODEOWNERS or equivalent special-review path.                                                                                                |
| `bootstrapTrustedFilesSha256`        | `string`  | Yes      | `sha256:<64 lowercase hex>` over the canonical bootstrap-governance manifest `(path, sha256)` list consumed by `ci.yml`, using the placeholder-normalized `.github/repository-release-contract.json` digest rule from §2.1. |

Example shape:

```json
{
    "schemaVersion": 1,
    "projects": {
        "example-project": {
            "projectPath": "src/example-project",
            "assuranceProfile": "standard",
            "releaseEnabled": true,
            "buddyAuthorizedRefs": ["refs/heads/main", "refs/heads/release/example-project/v1.2"],
            "buddyEnvironments": {
                "github-release/public": "github-release",
                "npm/github-packages": "buddy-npm-example-project"
            },
            "officialEnvironments": {
                "baseline": null,
                "refWrite": null,
                "evidenceWrite": null,
                "targets": {
                    "github-release/public": "github-release",
                    "npm/npmjs": "npmjs"
                }
            },
            "officialJobTimeoutMinutes": {
                "registry-approval-and-publish": 110,
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
                "writeEnvironment": null,
                "readCredentialScope": "artifact-store-readonly",
                "blockedRetentionDays": 365,
                "successfulRetentionDays": 730
            },
            "buddyTargetAuthContracts": {
                "github-release/public": {
                    "requiredEnvironment": "github-release",
                    "authClass": "github-token",
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
                },
                "npm/github-packages": {
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
                "github-release/public": {
                    "requiredEnvironment": "github-release",
                    "authClass": "github-token",
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
                },
                "npm/npmjs": {
                    "requiredEnvironment": "npmjs",
                    "authClass": "external-registry-oidc-trusted-publishing",
                    "allowedCredentialSource": "github-oidc",
                    "actorClass": null,
                    "providerWorkflowPath": ".github/workflows/official.yml",
                    "providerEnvironment": "npmjs",
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
                "github-release/public": {
                    "confirmIntervalSeconds": 10,
                    "confirmMaxAttempts": 3,
                    "perAttemptBudgetSeconds": 10,
                    "providerDelayBudgetSeconds": 0,
                    "confirmTimeoutSeconds": 110
                },
                "npm/npmjs": {
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
- When validated together with `<project-root>/three.release.yml` from the same branch snapshot, the buddy target subset in `three.release.yml.targets` must exactly match `buddyEnvironments` and `buddyTargetAuthContracts`, while the official target subset must exactly match `officialEnvironments.targets`, `officialTargetAuthContracts`, and `officialTargetConfirmationPolicies`.
- Until a later reviewed dotnet/NuGet workflow path adds NuGet target instances, NuGet registry targets must not appear in `officialEnvironments.targets`, `officialTargetAuthContracts`, or `officialTargetConfirmationPolicies`.
- `protectedRefs.officialTagPattern` must exactly equal `refs/tags/release/<project-key>/v*`, `protectedRefs.buddyTagPattern` must exactly equal `refs/tags/buddy/<project-key>/v**`, and `protectedRefs.liveLockRef` must exactly equal `refs/tags/official-lock/<project-key>`.
- `buddyTargetAuthContracts` and `officialTargetAuthContracts` are separate namespaces. The same bare target name, such as `github-release/public`, may appear in both without collision because each channel stores an independent closed auth object.
- `officialEnvironments.targets` key sets must exactly match `officialTargetAuthContracts` and `officialTargetConfirmationPolicies`; `buddyEnvironments` and `buddyTargetAuthContracts` must use the same full buddy-target keys and those key sets must exactly match. Their values must also match exactly: `buddyEnvironments[t] == buddyTargetAuthContracts[t].requiredEnvironment` for every buddy target and `officialEnvironments.targets[t] == officialTargetAuthContracts[t].requiredEnvironment` for every official target. The historical `officialEnvironments.evidenceWrite == artifactStore.writeEnvironment` rule is superseded in the active registry-environment model.
- In the active registry-environment model, `officialEnvironments.baseline` may be `null` and active OIDC target environments must match the registry environment names used by `release-orchestrate.yml`.
- `officialEnvironments.targets.github-release/public`, when present, must equal the active GitHub Release environment name for that path. Active OIDC-backed official targets use `pypi`, `npmjs` (with `npmjs-gate` as the human approval gate), and `rubygems` as applicable. A future NuGet environment name must be added only with the reviewed NuGet workflow path. The older branch-scoped `production-<surface>-<project-key>-<branchScopeKey>` model is superseded unless a later reviewed design reintroduces it.
- `buddyEnvironments.github-release/public`, when present after later enablement, must equal `github-release`; `buddyEnvironments.npm/github-packages` must equal the active npm GitHub Packages environment for that path; and `buddyEnvironments.rubygems/github-packages` must equal the active RubyGems GitHub Packages environment for that path. A NuGet buddy environment is deferred until NuGet registry targets are re-enabled.
- `artifactStore` must satisfy the closed discriminated-union schema for its selected `backendClass`.
- in the historical durable artifact-store model, when `artifactStore.backendClass` was `oci-registry` or `github-packages`, `commitMarkerTagPrefix` had to be non-empty, had to form a repository Git tag namespace under `refs/tags/` that did not overlap `protectedRefs.officialTagPattern`, `protectedRefs.buddyTagPattern`, or `protectedRefs.liveLockRef`, and had to be protected by a tag-targeted ruleset; this rule is not active unless §4.10 or an equivalent durable artifact store is reintroduced by a later reviewed design
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
- `approvalWaitMaxSeconds` must leave a documented human-action buffer for the active registry-environment approval gate. Repositories should normally budget at least `+1800` seconds beyond any reviewed wait timer unless the readiness evidence referenced by `readinessEvidenceRef` justifies a smaller buffer and the §7.6 cancellation/escalation runbook names the owner of that shorter timer.
- `approvalToLiveLockMaxDelaySeconds` must be an integer in the inclusive range `30..7200`.
- `approvalToLiveLockDelayJustification` must be `null` when `approvalToLiveLockMaxDelaySeconds <= 900`, and must be a non-null kebab-case string matching `[a-z0-9]+(-[a-z0-9]+)*` when `approvalToLiveLockMaxDelaySeconds > 900`. `assuranceProfile = high-assurance` must additionally keep `approvalToLiveLockMaxDelaySeconds <= 900`.
- `readinessEvidenceRef` must be a non-empty reviewed repository-relative path or durable locator, and that named readiness record is the authoritative place for the measurement owner, source window, helper output, any sub-10-sample waiver, exercise evidence, and any temporary monitor-bootstrap exception that this design permits.
- `offlineCustodians`, `offlineEvidenceRequirements`, `buddyAuthorizedRefs`, `allowedRefClaims`, and every non-null `providerTrustCapabilities` array must contain unique elements.
- Historical actor-class fields for ref writers, artifact-store marker writers, and GitHub App-backed release publishers are superseded for active `github-release/public` publication.
- `officialTargetAuthContracts.github-release/public` and any later-enabled `buddyTargetAuthContracts.github-release/public`, when present, must use `authClass = github-token`, `allowedCredentialSource = github-token`, `actorClass = null`, and `requiredEnvironment = github-release`.
- Active `officialTargetAuthContracts.{npm/npmjs,pypi/pypi,rubygems/rubygems-org}`, when present, must use `authClass = external-registry-oidc-trusted-publishing`; active `buddyTargetAuthContracts.{npm/github-packages,rubygems/github-packages}` must use `authClass = github-packages-github-token`. NuGet auth-contract keys are reserved/deferred and must not be enabled until a reviewed dotnet/NuGet workflow path adds catalog instances.
- `prTrustModel.bootstrapCodeOwnerReviewRequired` must be `true`, and `prTrustModel.bootstrapTrustedFilesSha256` must match `sha256:<64 lowercase hex>`.
- `prTrustModel` must reject any configuration that would expose fork PRs to secrets or privileged write tokens.
- Any PR that changes provider-side trust inputs (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerTrustCapabilities`, `providerRefClaimSupport`, `providerSupportsReadOnlyInspection`, `providerRefClaimMode`, `providerRefClaimModeRationale`, `allowedRefClaims`, or `providerAudience`) for an official target must update the corresponding `providerConfigReviewedAt` and `providerConfigReviewRef` fields in the same change set.
- Example and default confirmation budgets should leave timing slack above the computed minimum. This design uses a `+10` second recommendation for boundary-sized targets such as `github-release/public` and `npm/github-packages` rather than configuring those examples exactly at the formula floor.
- Unknown fields at the top level, in any project entry, or in `prTrustModel` are hard failures.

### 5.10 Canonical `release-plan` schema

`release-plan` is a closed envelope/graph object emitted by
`three-workflow-release-planner`. It contains exactly these fields and no
others:

| Field         | Type     | Notes                                                                     |
| ------------- | -------- | ------------------------------------------------------------------------- |
| `api-version` | `string` | Must equal `three.release.plan/v1alpha1`.                                 |
| `kind`        | `string` | Must equal `release-plan`.                                                |
| `envelope`    | `object` | Frozen request, selected project, and descriptor/catalog input metadata.  |
| `graph`       | `object` | Frozen variants, artifacts, publish nodes, and target-instance snapshots. |

The active `envelope` contains exactly `plan-id`, `profile`, `commit-sha`,
`request-flags`, `requested-project-ids`, `selected-project-ids`,
`authoring-inputs`, and `projects`. Each project snapshot records
`display-name`, `ecosystem`, `release-kind`, `descriptor-path`, `release-root`,
`source`, `resolved-version`, `variant-ids`, and `publish-node-ids`.

The active `graph` contains exactly `variants`, `artifacts`, `publish-nodes`,
and `target-instance-snapshots`. Publish nodes freeze the selected target
instance, artifact IDs, resolved publish identity, projection metadata such as
final distribution filenames and digests, and the planner's publish
disposition. Target-instance snapshots freeze the selected catalog instance,
destination, capabilities, and artifact contract.

Validation rules:

- `release-plan` remains a closed object; extra fields are hard failures.
- Active plans must not contain historical top-level `environmentBindings`,
  `artifactStoreBinding`, `planDigest`, `payloadSha`, `targetArtifacts`, or
  `targetAuthContracts` fields. Those shapes are retained only in historical
  admission/live-lock examples.
- `envelope.commit-sha` is the frozen release target commit for all build and
  publish inputs.
- `graph.artifacts` key sets must exactly match the artifact IDs referenced by
  `graph.variants[*].artifact-ids` and `graph.publish-nodes[*].artifact-ids`.
- Every `graph.publish-nodes[*].target-instance-snapshot-id` must identify one
  entry in `graph.target-instance-snapshots`.
- Active environment and credential requirements come from the reviewed
  workflow files, active registry environments, `src/**/three.release.yml`,
  `eng/release/target-instances.yml`, and checked-in runbooks; no active
  durable artifact-store binding is required by the plan schema.

### 5.11 Canonical `targetAuthContracts` schema

Each `targetAuthContracts.<target>` entry is also a closed object. It contains exactly these fields and no others:

| Field                                | Type               | Notes                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| ------------------------------------ | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `requiredEnvironment`                | `string`           | Exact subordinate environment name used by the target.                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| `authClass`                          | `string`           | Closed set `{github-token, github-app-installation-token, github-packages-github-token, external-registry-oidc-trusted-publishing}`.                                                                                                                                                                                                                                                                                                                                                       |
| `allowedCredentialSource`            | `string`           | Closed set `{github-token, github-oidc}` for active targets; historical brokered GitHub App paths used `environment-gated-external-broker`.                                                                                                                                                                                                                                                                                                                                                |
| `actorClass`                         | `string \| null`   | Required for GitHub App actor-based auth, otherwise `null`.                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `providerWorkflowPath`               | `string \| null`   | Exact workflow path expected by the provider-side trust configuration when applicable, otherwise `null`.                                                                                                                                                                                                                                                                                                                                                                                   |
| `providerEnvironment`                | `string \| null`   | For OIDC-backed targets, the non-empty exact environment name bound to the publish job; otherwise `null`.                                                                                                                                                                                                                                                                                                                                                                                  |
| `providerKey`                        | `string \| null`   | For OIDC-backed targets, exact external provider identifier from the closed set `{npmjs, nuget.org, pypi, rubygems.org}`; otherwise `null`.                                                                                                                                                                                                                                                                                                                                                |
| `providerTrustCapabilities`          | `string[] \| null` | For OIDC-backed targets, lexicographically sorted closed subset of `{repository, workflow-path, environment, ref}` describing provider-side claim enforcement; `null` for GitHub-native targets.                                                                                                                                                                                                                                                                                           |
| `providerRefClaimSupport`            | `string \| null`   | For OIDC-backed targets, closed set `{supported, unsupported, unknown}` recording whether this provider/target pair can enforce exact ref claims; otherwise `null`.                                                                                                                                                                                                                                                                                                                        |
| `providerSupportsReadOnlyInspection` | `boolean \| null`  | For OIDC-backed targets, records whether the provider exposes a documented read-only drift-inspection path; otherwise `null`.                                                                                                                                                                                                                                                                                                                                                              |
| `providerRefClaimMode`               | `string \| null`   | For OIDC-backed targets, closed set `{provider-enforced, workflow-only}`. `provider-enforced` is the preferred/higher-assurance mode; `workflow-only` is the reviewed lower-assurance exception mode that relies on repository-side compensating controls. `null` for GitHub-native targets.                                                                                                                                                                                               |
| `providerRefClaimModeRationale`      | `string \| null`   | Required closed set `{provider-does-not-support-exact-ref-claims, provider-ref-claims-not-available-for-this-target, provider-ref-claims-cannot-pin-required-branch-shape, provider-support-status-unknown}` when `providerRefClaimMode = workflow-only`; otherwise `null`.                                                                                                                                                                                                                |
| `providerConfigReviewedAt`           | `string \| null`   | For OIDC-backed targets, RFC 3339 UTC timestamp of the most recent repository-reviewed provider-side trust-configuration verification; otherwise `null`. The authoritative validation clock is the validating workflow runner's current UTC time. It must never be later than that current UTC value and must never be older than 365 days; some targets use stricter freshness rules. This is operational freshness evidence, not a release-identity equality field for blocked recovery. |
| `providerConfigReviewRef`            | `object \| null`   | For OIDC-backed targets, closed machine-readable evidence object for that provider-side trust review; otherwise `null`. It contains exactly `kind`, `locator`, and `evidenceSha256`. This is operational freshness evidence, not a release-identity equality field for blocked recovery. Its referenced evidence must still assert the same normalized trust tuple recorded in the checked-in contract rather than merely linking to some raw screenshot or payload.                       |
| `allowedRefClaims`                   | `string[]`         | Exact workflow-enforced ref claims; lexicographically sorted; wildcard patterns are forbidden. Empty only for non-OIDC GitHub-native targets.                                                                                                                                                                                                                                                                                                                                              |
| `providerAudience`                   | `string \| null`   | Exact OIDC audience when applicable, otherwise `null`.                                                                                                                                                                                                                                                                                                                                                                                                                                     |

`providerConfigReviewRef`, when non-null, is a closed object with exactly these fields:

| Field            | Type     | Notes                                                                                                                                                                                                                               |
| ---------------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `kind`           | `string` | Closed set `{api-snapshot, reviewed-console-export, signed-review-record}`.                                                                                                                                                         |
| `locator`        | `string` | Non-empty durable locator for the machine-readable provider review evidence. The locator must remain readable for the lifetime of the reviewed target configuration and any blocked entry that still depends on that configuration. |
| `evidenceSha256` | `string` | Canonical `sha256:<64 lowercase hex>` digest of the evidence bytes referenced by `locator`. A locator whose fetched bytes do not match this digest is invalid.                                                                      |

When `providerConfigReviewRef.kind = api-snapshot`, the referenced evidence must itself record at minimum the exact first-party source reviewed, the retrieval or capture timestamp, the normalized audience/trust-shape conclusion, the normalized trust tuple it supports (`providerWorkflowPath`, `providerEnvironment`, `providerKey`, `providerAudience`, `providerRefClaimMode`, `providerTrustCapabilities`, and `allowedRefClaims`), and the raw or normalized machine-readable payload used for that review. Evidence that omits the reviewed conclusion tuple, or whose conclusion no longer matches the checked-in contract even though the bytes are still readable, is semantically invalid.

Class-specific validation rules:

- `authClass = github-token` is the active auth class for `github-release/public`. It requires `allowedCredentialSource = github-token`, `actorClass = null`, empty `allowedRefClaims`, and `null` for every provider-side field. The active workflow binds the mutating job to `environment: github-release` and uses job-scoped `contents: write`.
- `authClass = github-app-installation-token` is historical/future-only for brokered GitHub API publication. It requires a later reviewed design before use.
- `authClass = github-packages-github-token` is allowed only for active buddy GitHub Packages targets `{npm/github-packages, rubygems/github-packages}`. It requires `allowedCredentialSource = github-token`, `actorClass = null`, empty `allowedRefClaims`, and `null` for every provider-side field. The old `nuget:gpr` key is reserved/deferred and configuration-invalid while `families.nuget.instances: []`.
- `authClass = external-registry-oidc-trusted-publishing` requires `allowedCredentialSource = github-oidc`, `actorClass = null`, non-empty `providerWorkflowPath`, non-empty `providerEnvironment`, non-empty `providerKey`, non-empty `providerTrustCapabilities`, non-empty `allowedRefClaims`, non-empty `providerAudience`, non-empty `providerRefClaimSupport`, a non-null read-only-inspection support flag, non-empty `providerConfigReviewedAt`, and non-null `providerConfigReviewRef`. Provider-specific checked-in defaults are part of this schema contract where current first-party documentation is stable: `providerKey = npmjs` must use `providerAudience = npm:registry.npmjs.org`; `providerKey = pypi` must use `providerAudience = pypi`; `providerKey = rubygems.org` must use `providerAudience = rubygems.org`. `providerKey = nuget.org` is reserved but configuration-invalid in this revision because NuGet registry targets are absent from the active catalog and the repository still lacks one approved closed audience contract; `api://NuGet` is not an approved default.

For OIDC-backed active targets, `allowedRefClaims` must be non-empty and must exactly enumerate the workflow-authorized refs for the project’s active release lines. `release-orchestrate.yml` hosted publish jobs enforce those refs before requesting `id-token: write`. `providerTrustCapabilities` must always include `repository` and `workflow-path`; if it includes `environment`, the value must match the active checked-in `providerEnvironment` (`pypi`, `npmjs`, or `rubygems`). If `providerRefClaimMode = provider-enforced`, the capability set must also include `ref` and `providerRefClaimSupport` must be `supported`. If `providerRefClaimMode = workflow-only`, then `providerRefClaimSupport` must be `unsupported` or `unknown`, `providerRefClaimModeRationale` must be non-null, and the provider-side capability set must contain at least `{repository, workflow-path, environment}` while omitting `ref`. The superseded branch-scoped `production-<surface>-<project-key>-<branchScopeKey>` rule is not active; workflow-only targets use the active registry environment recorded for their provider (`pypi`, `npmjs`, or `rubygems`, with `npmjs-gate` for npm approval where applicable) unless a later reviewed design reintroduces branch-scoped environments.

`providerConfigReviewedAt` and `providerConfigReviewRef` record the last repository-reviewed verification of the provider-side trust configuration. The authoritative clock for both `ci.yml` and release-time freshness checks is the validating workflow runner's current UTC time, not an author workstation clock. Every non-null `providerConfigReviewedAt` must therefore be `<= now()` and no older than 365 days. When the relevant evidence surface is available, `ci.yml`, release validation, and the active §7.6 provider-freshness diagnostics must also verify that `providerConfigReviewRef.locator` remains readable and that its bytes still hash to `evidenceSha256`; missing or mismatched evidence is a hard failure, not a soft warning. If `providerSupportsReadOnlyInspection = false`, the workflow has no independent runtime proof that the provider-side configuration still matches the checked-in contract, so release readiness additionally requires current checked-in review fields and a latest external drift-probe result within the expected cadence. For `providerKey = pypi`, Day 0 enablement must still re-confirm the checked-in `pypi` audience against then-current first-party PyPI documentation. For `providerKey = nuget.org`, no checked-in configuration is valid in this revision: NuGet registry targets are absent from the active catalog until a later reviewed dotnet/NuGet workflow path adds target instances and approves one exact audience contract. For GitHub-native targets that do not use provider-side OIDC trust, `allowedRefClaims` is the empty array, `providerRefClaimMode` is `null`, and provider-specific fields are `null`.

### 5.12 Canonical `officialTargetConfirmationPolicies` schema

Each `officialTargetConfirmationPolicies.<target>` entry is also a closed object. It contains exactly these fields and no others:

| Field                        | Type      | Notes                                                                                                                                                                                                                                                                                                                          |
| ---------------------------- | --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `confirmMaxAttempts`         | `integer` | Integer in the inclusive range `1..8`.                                                                                                                                                                                                                                                                                         |
| `confirmIntervalSeconds`     | `integer` | Integer `>= 1`.                                                                                                                                                                                                                                                                                                                |
| `perAttemptBudgetSeconds`    | `integer` | Integer `>= 1`; per-attempt allowance for API latency, token issuance, and response parsing.                                                                                                                                                                                                                                   |
| `providerDelayBudgetSeconds` | `integer` | Integer `>= 0`; cumulative wall-clock allowance reserved for provider-mandated waits such as `Retry-After` across the whole target-confirmation loop. Use `0` only when the target has no such provider-managed delay path in the reviewed confirmation surface.                                                               |
| `confirmTimeoutSeconds`      | `integer` | Integer `>= 1` and `>= confirmIntervalSeconds * (2^(confirmMaxAttempts - 1) - 1) + confirmMaxAttempts * perAttemptBudgetSeconds + providerDelayBudgetSeconds` under the exact truncated-exponential full-jitter retry model from §4.4. Repository guidance is to add at least 10 seconds of slack above that computed minimum. |

Validation rules:

- `officialTargetConfirmationPolicies` key sets must exactly match `officialTargetAuthContracts` for the same project.
- These confirmation policies are operational controls, not release-identity fields. `preflight-validate` must validate and emit them for the current run, but they are intentionally excluded from `release-plan`, blocked-entry `frozenPlan`, and `planDigest` so operators may tune confirmation behavior without burning the frozen version.
- `ci.yml` must statically validate the same `confirmTimeoutSeconds` inequality during PR validation so invalid retry budgets fail before merge rather than during a release run.
- Retryable visibility/rate-limit/provider faults use the exact retry model from §4.4: attempt `1` is immediate, each later gap sleeps a jittered duration within that gap’s exponential ceiling, and deterministic conflicts are terminal.

### 5.13 Schema evolution policy

- All checked-in release-control JSON surfaces in this design are closed schemas. In v1, adding a field is a breaking change, not a silent compatible extension.
- `schemaVersion` is repository-policy versioning, not a consumer-negotiated API. The workflows must hard-fail on any version they do not explicitly implement.
- Because release workflows are already active, schema changes must preserve live repository safety during migration. A schema change must migrate every affected checked-in file in one reviewed change set, and `ci.yml` must reject mixed-version repository states.
- A future `schemaVersion` increment is required for any field addition, removal, rename, type change, enum-set change, or semantic reinterpretation across `three.release.yml`, `.github/repository-release-contract.json`, `.github/official-admission-state/<project-key>.json`, `release-plan`, `targetAuthContracts`, or `officialTargetConfirmationPolicies`.
- The repository-wide `schemaVersion` remains mandatory for identity-bearing and authorization-bearing surfaces. However, this design now explicitly allows future reviewed subordinate `structureVersion` evolution for non-authoritative diagnostic or evidence helper outputs when—and only when—the affected structure is not part of `planDigest`, blocked-stage selection, target-auth equality, or bootstrap integrity. Any such exception must be called out explicitly in this document rather than inferred from silence.
- In repositories with multiple protected release branches, schema migration is an operational procedure, not just a file edit. The runbook must define the supported branch order (normally `main` first, then maintenance branches), a temporary release freeze window while mixed-schema branches are being updated, the conversion procedure for already-blocked admission entries, the rollback procedure if migration tooling fails, and the rule that any recovery needed during the freeze must first restore all relevant branches to one consistent schema version.
- The schema-migration runbook must define an explicit freeze-window upper bound not exceeding 24 hours. If migration cannot complete within that bound, operators must either roll back to the last consistent schema version or declare a management-visible incident that keeps release traffic frozen under one named owner.
- The schema-migration runbook must include one concrete coordination checklist and example timeline. Minimum checklist items are: identify every protected branch affected; name one release owner per branch; announce the freeze window and rollback owner; pause new `official.yml` dispatches; confirm from GitHub Actions run summaries that no official run is still active on any affected branch; use the latest `release-report.json`, receipt artifacts, and read-only remote observation helpers to prove that every affected project has no active in-flight release target or has an explicit reviewed conversion/abort plan for the current receipt/report state; pause or withdraw every in-flight recovery-authorization PR during the freeze; convert any historical blocked admission entries only when that historical model is being migrated; verify `ci.yml` schema validation on each branch after migration; and record the exact condition for lifting the freeze.
- The example migration timeline must at minimum show: `T-5d` branch-owner notice and branch inventory; `T-1d` freeze reminder plus reviewed migration PR preparation; `T0` freeze starts only after active official runs are drained and the latest reports, receipts, run summaries, and remote observations show no unresolved publish target for the affected projects before `main` migrates first; `T0+n` each maintenance branch migrates in documented order; `T0+verify` all branches pass schema validation and any historical-entry conversions required by that migration; `T0+lift` release freeze ends only after every protected branch is back on one schema version. During the freeze there are only two legal paths for an unresolved release: re-submit the recovery authorization after every affected branch has been migrated to the new schema, or fully roll back the migration and execute recovery on the restored old schema. Mixing an old-schema recovery PR with an in-progress schema migration is forbidden.
- A schema-version bump must ship with: updated examples in this design document, updated duplicate-key-rejecting validators in workflow code, reviewed migration tooling under `eng/scripts/`, and explicit operator runbook steps for updating multi-project repositories atomically.

## 6. Checked-in Admission and Recovery State

The checked-in admission / live-lock model in this section is superseded for the
active workflow topology. Active workflows rely on dynamic entry concurrency,
registry-environment gates, and `release-orchestrate.yml` delegation. The shapes
below remain historical/future-only reference material unless a later reviewed
design reintroduces checked-in admission state and live locks.

### 6.1 File and live lock

- Historical `.github/official-admission-state/<project-key>.json`
- Historical protected live lock tag `refs/tags/official-lock/<project-key>` as an annotated tag whose annotation payload carries the frozen lock identity

### 6.2 Purpose

In the superseded model, the checked-in file for the selected project on the selected protected official release branch was the authoritative reviewed admission and recovery ledger for that project and that `official.yml` run.

In the superseded model, the live lock was the immediate durable blocker for a project before the first irreversible external mutation of an official run. Active workflows must not treat these historical shapes as current admission requirements.

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

In the superseded model, before any official release was dispatched for a project from a protected branch, that branch had to contain the exact minimal `ready` entry for `.github/official-admission-state/<project-key>.json`. Active project enablement is instead validated from `three.release.yml`, the target-instance catalog, registry-environment configuration, and the active readiness checks described above.

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
        "github-release/public": {
            "state": "confirmed",
            "evidenceRef": "artifact://official-confirmation/github-release/blocked-project/sha256-aaaa.json"
        },
        "npm/npmjs": {
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
            "evidenceWrite": null,
            "targets": {
                "github-release/public": "github-release",
                "npm/npmjs": "npmjs"
            }
        },
        "artifactStoreBinding": {
            "backendClass": "oci-registry",
            "repository": "ghcr.io/three/blocked-project-release-bundles",
            "commitMarkerTagPrefix": "plan-",
            "bundleFormatVersion": 1,
            "writeEnvironment": null,
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
        "targets": ["github-release/public", "npm/npmjs"],
        "targetArtifacts": {
            "github-release/public": ["package"],
            "npm/npmjs": ["package"]
        },
        "targetAuthContracts": {
            "github-release/public": {
                "requiredEnvironment": "github-release",
                "authClass": "github-token",
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
            },
            "npm/npmjs": {
                "requiredEnvironment": "npmjs",
                "authClass": "external-registry-oidc-trusted-publishing",
                "allowedCredentialSource": "github-oidc",
                "actorClass": null,
                "providerWorkflowPath": ".github/workflows/official.yml",
                "providerEnvironment": "npmjs",
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

In the superseded live-lock model, `lockIdentity` is a closed object with exactly these fields: `planDigest`, `lockInstanceToken`, `lockRef`, `lockedAt`, `runId`, and `runAttempt`. It persisted the reviewed live-lock instance independently from `frozenPlan` and had to be copied from the authoritative lock payload rather than recomputed from current branch state.

`blockedStage × reason` validity matrix:

| `blockedStage`         | Allowed `reason` values                                                                                                                                                                                                                                                                                                                                |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `pre-provenance`       | `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `attestation-generation-failed`, `lock-integrity-failure`, `operator-aborted`                                                                                                                                                                                |
| `provenance-uncertain` | `provenance-reconciliation-failed`, `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `existing-bundle-ownership-ambiguous`, `lock-integrity-failure`, `operator-aborted`                                                                                                                                      |
| `post-provenance`      | `pre-provenance-write-completed-awaiting-review`, `tag-conflict`, `tag-write-failure`, `attestation-verification-failed`, `publish-job-failure`, `publish-confirmation-failed`, `publish-confirmation-timeout`, `artifact-store-unavailable`, `artifact-store-digest-mismatch`, `artifact-store-timeout`, `lock-integrity-failure`, `operator-aborted` |
| `post-confirmation`    | `published-with-lock-residue`, `post-confirmation-verification-failed`, `lock-integrity-failure`, `operator-aborted`                                                                                                                                                                                                                                   |

Artifact-identity schema (`artifactIdentity`) is a closed object with these fields:

| Field             | Type     | Required | Notes                                                                                                   |
| ----------------- | -------- | -------- | ------------------------------------------------------------------------------------------------------- |
| `artifactLocator` | `string` | Yes      | Non-empty durable locator returned by the §4.10 store.                                                  |
| `attestationRef`  | `string` | Yes      | Non-empty durable attestation/provenance locator.                                                       |
| `subjects`        | `object` | Yes      | Closed object keyed exactly by the union of every alias named anywhere in `frozenPlan.targetArtifacts`. |

Each `artifactIdentity.subjects.<alias>` entry is also closed and contains exactly these fields: `filename`, the canonical publication filename or GitHub Release asset name for that alias; and `sha256`, which must be `sha256:<64 lowercase hex>`. No other digest algorithms are allowed in v1.

This stage/presence matrix is the single authoritative source for when `artifactIdentity` may appear:

| Admission state                       | `artifactIdentity` rule                                                                                         |
| ------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| `ready`                               | absent                                                                                                          |
| `blockedStage = pre-provenance`       | absent                                                                                                          |
| `blockedStage = provenance-uncertain` | either absent, or the full closed object once read-only reconciliation has reconstructed authoritative identity |
| `blockedStage = post-provenance`      | required and must be the full closed object                                                                     |
| `blockedStage = post-confirmation`    | required and must be the full closed object                                                                     |

`recovery` is also a closed object. When present, it contains exactly these fields:

| Field                     | Type      | Required presence | Notes                                                                                                                                                                                                                                       |
| ------------------------- | --------- | ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `approvalState`           | `string`  | Always            | Closed set `{not-approved, approved, aborted}`.                                                                                                                                                                                             |
| `allowedMode`             | `string`  | Conditional       | Closed set `{rerun-plan, reconcile-store, restore-bundle, clear-lock-only}`. Required only when `approvalState = approved`; optional advisory recommendation when `approvalState = not-approved`; forbidden when `approvalState = aborted`. |
| `authorizationRef`        | `string`  | Conditional       | Closed `{kind}:{identifier}` reference for the approving or aborting record. Required for `approved` and `aborted`; forbidden for `not-approved`.                                                                                           |
| `authorizedAt`            | `string`  | Conditional       | RFC 3339 UTC timestamp for the approving or aborting record. Required for `approved` and `aborted`; forbidden for `not-approved`.                                                                                                           |
| `approvedForEntryVersion` | `integer` | Conditional       | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal the current top-level `entryVersion` whenever present.                                                                                              |
| `approvedForPlanDigest`   | `string`  | Conditional       | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal `frozenPlan.planDigest` whenever present.                                                                                                           |
| `approvedForBlockedStage` | `string`  | Conditional       | Required for `approved` and `aborted`; forbidden for `not-approved`. Must exactly equal the current `blockedStage` whenever present.                                                                                                        |

Validation rules:

- `schemaVersion` must equal `1`.
- `projectKey` must exactly equal the canonical `project-key` encoded by the file path `.github/official-admission-state/<project-key>.json`.
- `status` must be exactly `ready` or `blocked`.
- `updatedAt` is required for every entry and must be an RFC 3339 UTC timestamp.
- A `ready` entry must not contain `blockedStage`, `entryVersion`, `digestChangeReason`, `riskFlags`, `targetResults`, `lockIdentity`, `frozenPlan`, `artifactIdentity`, `reason`, `evidenceRef`, or `recovery`.
- A `blocked` entry must include `blockedStage`, `entryVersion`, `digestChangeReason`, `riskFlags`, `targetResults`, `lockIdentity`, `frozenPlan`, `reason`, `evidenceRef`, and `recovery`.
- `blockedStage` must be exactly `pre-provenance`, `provenance-uncertain`, `post-provenance`, or `post-confirmation`.
- `entryVersion` must be an integer `>= 1` and is the versioned review/approval binding for the current blocked facts.
- `lockIdentity.planDigest` must exactly equal `frozenPlan.planDigest`, `lockIdentity.lockRef` must exactly equal `refs/tags/official-lock/<project-key>`, `lockIdentity.runAttempt` must equal the observed official run attempt copied from the authoritative live-lock payload, and `lockIdentity.lockedAt` must be an RFC 3339 UTC timestamp copied from the authoritative live-lock payload.
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
- When `recovery.approvalState = approved`, `recovery.allowedMode` must be `rerun-plan` for `pre-provenance`, `reconcile-store` for `provenance-uncertain`, `restore-bundle` for `post-provenance`, and `clear-lock-only` for `post-confirmation`; `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` are all required. `rerun-plan` means a reviewed `official.yml` run for the frozen blocked plan; the official `allow_idempotent` rerun policy permits either a GitHub rerun attempt or a fresh dispatch when the same frozen plan and normal fail-closed proof requirements are satisfied.
- When `recovery.approvalState = not-approved`, `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` must all be absent. `allowedMode` may be absent, or it may appear only as the single stage-valid machine-generated recommendation (`rerun-plan` for `pre-provenance`, `reconcile-store` for `provenance-uncertain`, `restore-bundle` for `post-provenance`, `clear-lock-only` for `post-confirmation`). A present `allowedMode` in `not-approved` state is advisory only and becomes binding only after `approvalState = approved`.
- When `recovery.approvalState = aborted`, `allowedMode` must be absent and `authorizationRef`, `authorizedAt`, `approvedForEntryVersion`, `approvedForPlanDigest`, and `approvedForBlockedStage` are all required so the checked-in state distinguishes an explicit abort decision from an unreviewed blocked entry.
- When `digestChangeReason` is non-null, `recovery.approvalState` may be `not-approved` or `aborted`, but never `approved`.
- When any recovery binding field is present, `approvedForEntryVersion` must exactly equal the current top-level `entryVersion`, `approvedForPlanDigest` must exactly equal `frozenPlan.planDigest`, and `approvedForBlockedStage` must exactly equal the current `blockedStage`.
- `recovery` is a closed object; unknown nested fields are hard failures.
- Unknown top-level fields are hard failures until explicitly added to the schema.

### 6.4 Historical update model

All rules in this subsection describe the superseded checked-in
admission/live-lock recovery model. They are retained as design memory only and
are not active requirements for the current report/receipt/remote-evidence
recovery model unless a later reviewed design explicitly reintroduces them.

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

| Current context                         | Allowed next state without `entryVersion` increment | Allowed next state only with `entryVersion` increment |
| --------------------------------------- | --------------------------------------------------- | ----------------------------------------------------- |
| `status = ready` (no `recovery` object) | none                                                | first blocked entry starts at `not-approved`          |
| `not-approved`                          | `not-approved`, `approved`, `aborted`               | `not-approved`, `approved`, `aborted`                 |
| `approved`                              | `approved`                                          | `approved`, `aborted`                                 |
| `aborted`                               | `aborted`                                           | `aborted`                                             |

- A return from any blocked state to `ready` removes the `recovery` object entirely and is legal only through the reviewed terminal-disposition rule in this section; `ready -> approved` and `aborted -> approved` are never direct transitions.
- After baseline approval, checked-in admission-state changes do not by themselves retroactively cancel an already-running release. Live-lock removal is only a best-effort interruption signal: it can stop later irreversible mutations only if a downstream job has not already completed its final lock revalidation. The runbook must explicitly warn that one or more additional external requests inside the current publish job may still complete after break-glass lock removal, and any post-provenance or post-confirmation cleanup must account for that possibility.
- The workflow itself must create or update a GitHub issue when it fails after lock creation, when it intentionally stops after the first `pre-provenance` durable write, or when it ends in `published-with-lock-residue`, containing a structured blocked-entry JSON draft, required evidence fields, and the recommended `blockedStage`; this issue-creation duty applies to verification-only failures too, so a `post-confirmation` anchor mismatch detected in `create-release-tag` must emit the same structured blocked-entry path even if `confirm-publish-state` is skipped; it must also emit a machine-generated `event-evidence` JSON payload so operators are not expected to hand-author incident evidence under pressure. If issue creation fails, the run log must print the same structured payload verbatim **and** the workflow must persist that same structured draft into the live-lock annotation payload or the durable artifact store pointer referenced by that payload. For the intentional `pre-provenance` stop, that draft must use `blockedStage = post-provenance`, `reason = pre-provenance-write-completed-awaiting-review`, and the advisory `recovery.allowedMode = restore-bundle` while keeping `recovery.approvalState = not-approved`. For `published-with-lock-residue`, that draft must use `blockedStage = post-confirmation` and the advisory `recovery.allowedMode = clear-lock-only` while keeping `recovery.approvalState = not-approved`. Every machine-generated blocked-entry draft after lock creation must also populate `targetResults` from the best available in-run evidence so mixed per-target states are preserved rather than collapsed into one top-level reason.
- Historical/future-only: the reviewed helper `eng/scripts/create-blocked-entry` was a Day 0 implementation prerequisite for the superseded admission/live-lock model. It accepted the frozen lock payload plus structured event evidence and emitted a schema-valid blocked-entry JSON draft so operators did not hand-author closed-schema recovery objects during an incident or an intentional recovery stop.
- If reintroduced, `eng/scripts/create-blocked-entry` must meet this minimum interface contract:
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
- That CODEOWNERS path must be owned by the repository’s release-governance owners, and that reviewer population must be equal to or narrower than the active official approval population for the applicable registry environments. Generic contributor review is not sufficient.
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

- The active plan step must emit immutable `policy-sha` plus one canonical frozen
  `release-plan` and matching `execution-sets`.
- For a normal official release, `policy-sha` identifies the trusted workflow
  code, while `release-plan.envelope.commit-sha` is the frozen payload snapshot.
- The authoritative official release identity is the frozen `release-plan`
  envelope/graph plus its execution sets, not `policy-sha` alone.
- `release-plan` must be exactly the closed object defined in §5.10.
- Historical `planDigest`, top-level `targetAuthContracts`, and provider-review
  placeholder-normalization rules belonged to the superseded admission/live-lock
  plan shape. Active identity and receipt correlation use
  `release-plan.envelope.plan-id`, `release-plan.envelope.commit-sha`, publish
  node IDs, target-instance snapshots, package/release identities, artifact
  filenames from the envelope/graph plan, and producer-bound build/publish/proof
  digests for content exactness.
- Implementations must use an RFC 8785 / JCS conformant serializer that emits UTF-8 bytes, preserves the exact closed-schema field set, and does not pretty-print, reorder outside the documented sort rules, or coerce strings or path separators. All numeric fields in those canonicalized surfaces must remain exact IEEE 754 safe integers as noted above; implementations must reject values that cannot round-trip identically across the supported languages.
- This design intentionally uses the full RFC 8785 / JCS contract even though today’s checked-in release objects are mostly ASCII, string-heavy, and schema-constrained. The full spec avoids later interoperability breaks when fields expand, keeps one canonicalizer across `release-plan`, review payloads, and checked-in state, and removes any temptation to invent a repository-local “almost JSON” digest scheme.
- Historical/future-only: the checked-in reference implementation for canonicalization was reserved as `eng/scripts/jcs-canonicalize` and the checked-in fixture suite was reserved under `eng/tests/jcs-fixtures/` for the superseded control-plane model. They are not active prerequisites for the current descriptor/catalog topology.
- If a later reviewed design reintroduces the historical JCS helper, `eng/scripts/jcs-canonicalize` and `eng/tests/jcs-fixtures/` must meet this minimum interface contract:
    - implementation baseline: Python `3.12+` authoritative reference implementation, deterministic on the active Linux and Windows workflow runners, with any wrappers treated as non-authoritative launchers. Because Python's default `json.loads()` silently accepts duplicate keys, implementations must use `object_pairs_hook` or an equivalent duplicate-key-rejecting parser path instead of the default loader
    - invocation contract: `eng/scripts/jcs-canonicalize --input <path|-> --mode canonicalize|digest --schema-surface release-plan --reject-duplicates [--debug-out <path>]` and `eng/scripts/jcs-canonicalize --verify-fixtures --fixtures-root eng/tests/jcs-fixtures/`
    - `--mode canonicalize` stdout contract: exactly the UTF-8 RFC 8785 / JCS serialization bytes for the supplied JSON value, with no trailing explanatory text
    - `--mode digest` stdout contract: only `sha256:<64 lowercase hex>` followed by `\n`, computed from those canonical bytes
    - `--debug-out` writes a structured machine-readable trace containing the validated schema surface, canonical key ordering, canonical-byte digest, and any normalization decisions needed for operator debugging without changing the stdout contract
    - failure contract: duplicate keys, non-UTF-8 input, unsupported numeric forms, or schema-shape violations are hard failures with non-zero exit; exit codes are `0` success, `2` invalid invocation or invalid JSON input, `3` fixture mismatch or duplicate-key rejection, and `4` internal canonicalization failure
    - fixture-suite minimum coverage: object-key ordering, nested arrays/objects, required explicit `null` fields, duplicate-key rejection, non-BMP Unicode, lone-surrogate rejection, UTF-8 BOM rejection, `-0` normalization coverage, escape normalization, representative IEEE 754 boundary values that are valid JSON numbers, and golden `(input, canonical-bytes, digest)` vectors consumed identically on Linux and Windows
- Historical/future-only: the superseded model required a checked-in JCS fixture suite and golden digests before cross-language helpers were trusted for release. Active tooling must still use duplicate-key-rejecting parsers and closed-schema validation for descriptor/catalog/plan surfaces, but the historical fixture suite is not a current release prerequisite.
- Before schema validation or serialization, implementations must parse active `three.release.yml` descriptors and `eng/release/target-instances.yml` in strict duplicate-key-rejecting mode; duplicate keys are hard failures and must never be normalized away before identity or plan computation. Python implementations must not rely on the default `json.loads()` behavior because it silently overwrites earlier duplicate keys.
- Before serialization, active planner implementations must use the deterministic key ordering and array ordering defined by the §5.10 envelope/graph schema for `envelope`, `graph.target-instance-snapshots`, `graph.artifacts`, `graph.publish-nodes`, and nested snapshot structures. Historical `targetArtifacts` and top-level `targetAuthContracts` ordering rules belong only to the superseded plan shape.
- Nullable schema fields remain part of the canonical object. Implementations must serialize required nullable fields explicitly as `null` rather than omitting them, and all cooperating implementations must agree on that exact null-field handling.
- Historical/future-only: the superseded plan-digest model required C# and Ruby helpers to delegate `planDigest` computation to the checked-in reference implementation until native implementations passed shared fixtures. Active release identity uses the planner-produced envelope/graph identity, report, receipt, and remote-evidence set.
- Extra fields are forbidden in `release-plan` and active descriptor/catalog-derived plan snapshots; implementations must reject them rather than ignoring them. Active `release-plan` objects must use `api-version: three.release.plan/v1alpha1`, `kind: release-plan`, and the closed §5.10 envelope/graph shape in this document revision.
- `artifact://` is a repository-owned durable-evidence locator scheme. Its canonical URI shape is `artifact://<collection>/<path>` with no query string or fragment. `collection` names the reviewed storage namespace, the remaining slash-separated path is an opaque repository-owned locator inside that namespace, and the paired digest field such as `evidenceSha256` remains the authoritative byte-identity check for the referenced object.
- `providerConfigReviewRef.locator` may use `artifact://` only for machine-readable provider-review evidence retained in repository-controlled durable storage; human-only screenshots or transient console state are insufficient.
- Release tooling and runbooks must treat unknown `artifact://` collections as unsupported rather than guessing a storage backend.
- For a reviewed official recovery run, `release-plan.envelope.commit-sha` may differ from the current `policy-sha` only when the reviewed recovery authorization and active evidence prove the same frozen envelope/graph plan, resolved target SHA, and per-target receipt/remote state for the recovered release. Checked-in blocked state and live-lock records are historical/future-only evidence formats, not active requirements.
- The active plan records version lineage through the envelope project snapshot and publish-node identities defined in §5.10. Historical top-level `releaseLine` and `npmAccessHint` fields are not part of the active plan shape.
- The annotated official release tag is the durable release-identity anchor for a frozen release plan; it is created before external publication and therefore must **not** be treated as proof that publication succeeded. It must carry the canonical frozen release identity for the resolved release target. Active completion authority comes from the current run's release-plan and execution-set artifacts, valid package-registry `publish-result` receipts, valid GitHub Release `github-release-result` receipts, the release report, and matching remote registry/GitHub Release evidence for every active publish node. A release becomes successful only when every active target is satisfied by that receipt/report/remote-evidence set, or when a reviewed recovery path proves the same per-target state or records an explicit abort/burn disposition. Historical `confirm-publish-state`, `release-complete`, live-lock clearing, and persisted confirmation-record requirements belong only to the superseded admission/live-lock model unless a later reviewed design reintroduces them.

Active shape example for `release-plan`:

```json
{
    "api-version": "three.release.plan/v1alpha1",
    "kind": "release-plan",
    "envelope": {
        "plan-id": "plan/abc123",
        "profile": "official",
        "commit-sha": "1111111111111111111111111111111111111111",
        "request-flags": {
            "force": false
        },
        "requested-project-ids": ["example-project"],
        "selected-project-ids": ["example-project"],
        "authoring-inputs": {
            "catalog-path": "eng/release/target-instances.yml",
            "descriptor-api-version": "three.release/v1alpha1"
        },
        "projects": {
            "example-project": {
                "display-name": "Example Project",
                "ecosystem": "jsts",
                "release-kind": "lib",
                "descriptor-path": "src/example-project/three.release.yml",
                "release-root": "src/example-project",
                "source": {
                    "primary-manifest-path": "src/example-project/package.json",
                    "auxiliary-input-paths": [],
                    "version-authority-kind": "build-system-nbgv"
                },
                "resolved-version": "1.2.3",
                "variant-ids": ["variant/main"],
                "publish-node-ids": ["publish-node/npmjs"]
            }
        }
    },
    "graph": {
        "variants": {
            "variant/main": {
                "project-id": "example-project",
                "descriptor-handle": "default",
                "dimensions": {},
                "artifact-ids": ["artifact/package"]
            }
        },
        "artifacts": {
            "artifact/package": {
                "project-id": "example-project",
                "variant-id": "variant/main",
                "descriptor-handle": "npm-package",
                "role": "primary-package",
                "kind-family": "package",
                "concrete-kind": "npm-package",
                "produced-from-artifact-ids": []
            }
        },
        "publish-nodes": {
            "publish-node/npmjs": {
                "publish-node-id": "publish-node/npmjs",
                "project-id": "example-project",
                "profile": "official",
                "descriptor-target-index": 0,
                "target-instance-snapshot-id": "npm/npmjs",
                "artifact-ids": ["artifact/package"],
                "publish-disposition": "publish",
                "publish-mode": "create-only",
                "resolved-publish-identity": {
                    "package-name": "@three/example-project",
                    "version": "1.2.3"
                },
                "projection": {
                    "final-distribution-filenames-by-artifact-id": {
                        "artifact/package": "example-project-1.2.3.tgz"
                    },
                    "final-distribution-digests-by-artifact-id": {
                        "artifact/package": {
                            "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                            "sha512": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                        }
                    }
                }
            }
        },
        "target-instance-snapshots": {
            "npm/npmjs": {
                "family": "npm",
                "instance-id": "npmjs",
                "catalog-ref": "npm/npmjs",
                "destination": {
                    "host": "registry.npmjs.org"
                },
                "capabilities": {
                    "publish-topology": "external-oidc-caller-workflow"
                },
                "contract": {
                    "id": "npm-publish",
                    "allowed-artifact-tuples": [],
                    "aggregate-rules": {}
                }
            }
        }
    }
}
```

The example above is illustrative. The active checked-in fixtures under
`three-workflow-release-contracts/tests/fixtures/valid/` are authoritative for
validator shape. Historical examples that include top-level
`environmentBindings` or `artifactStoreBinding` are future-only design memory and
must not be used to impose durable artifact-store or environment-binding
requirements on active plans.

### 7.2 Concurrency model

- Active entry workflows resolve raw `inputs.target` once before concurrency/orchestration and use their checked-in dynamic release-identity concurrency group, for example `release/${project_id}/v${release_version}` as emitted by `needs.authorize-entry.outputs.release_group`. The key is scoped to the resolved project and version release tag; `.github/workflows/release-orchestrate.yml` has no workflow-level concurrency of its own.
- An official run waiting on active registry-environment approval must not be allowed to occupy its dynamic release-identity concurrency slot indefinitely. Active diagnostics in §7.6 use GitHub Actions metadata, environment review state, job timeouts, and reviewed runbook intervention to identify and clear stuck approval waits without relying on historical suspension records.
- Active ordering and recovery authority comes from the dynamic release-identity concurrency group, `release-orchestrate.yml` execution, active registry environments, orchestrator publish receipts, and descriptor/catalog state. The older checked-in admission file and live-lock authority are superseded/future-only.
- `cancel-in-progress: false` is used only to avoid overlapping execution and to avoid evicting an in-flight mutation phase; it must not be treated as a durable FIFO queue or admission ledger.
- Because GitHub concurrency ordering is not a durable queue, durable ordering, recovery, and unblock decisions come from active descriptor/catalog state, orchestrator receipts, registry evidence, and reviewed runbook decisions rather than from GitHub Actions pending-run behavior. Monitoring and runbooks must therefore treat stuck pending/running dynamic entry concurrency state as an operational incident rather than a trustworthy queue. Raw `workflow_dispatch` input is not an authoritative project-resolution surface; invalid or unknown `project` values must fail before privileged work or registry mutation.
- The checked-in runbooks must include an explicit cross-release-line contention procedure for urgent hotfixes. That procedure may prioritize a mainline or maintenance-line release operationally, but it must do so only by resolving the existing blocked plan through the reviewed abort / clear / recovery paths in this design; bypassing the active project/version release-identity group is forbidden. The runbook must define a maximum contention-decision time by severity: security-critical or actively exploited fixes must reach an explicit continue-vs-abort decision within 30 minutes, other urgent production hotfixes within 2 hours, and lower-priority maintenance traffic within one business day. The runbook must also name the release-duty incident commander as the owner of that timer and must define the exact criteria for choosing “wait”, “continue existing plan”, or “abort lower-priority plan to unblock the hotfix”.
- GitHub retains at most one running and one pending run per concurrency group. If runs `A`, `B`, and `C` land in the same active dynamic release-identity group, `A` may remain running, `B` may become pending, and newer pending run `C` may cancel and replace pending `B`. That is GitHub's latest-wins pending replacement inside the project/version release-tag group, not a durable FIFO queue and not a repository-defined supersession protocol for an already-running release. A pending run cancelled this way may have no opportunity to emit release artifacts or diagnostics; operators must treat its cancellation as ordinary platform cancellation before mutation authority, and rely on surviving workflow evidence plus later planner/registry observation for recovery decisions.
- Distinct projects may release in parallel.

### 7.2.1 Required phase-boundary implementation pattern

- GitHub concurrency can be attached at workflow scope or to individual jobs. For the active official package-registry path, `official.yml` owns the dynamic entry concurrency group and `release-orchestrate.yml` owns the publish/token-minting orchestration boundary without declaring its own concurrency group. Invalid inputs must fail before any approval, environment entry, token minting, or privileged work continues.
- `release-orchestrate.yml` must therefore remain the current publish orchestration workflow:
    1. PyPI, npmjs, and RubyGems token-minting jobs run only in the reviewed split path rooted in `release-orchestrate.yml`
    2. only the exact environment-scoped token-minting / publish jobs that need external OIDC request tokens; reusable-workflow caller jobs may grant `id-token: write` only as the upper-bound permission for called OIDC publish/provenance jobs. For npmjs, both the active `official.yml` caller job and the `release-orchestrate.yml` npmjs publish job must receive `id-token: write` so npm can validate the direct caller workflow identity
    3. NuGet token-minting / publish jobs are absent until the dotnet/NuGet workflow path and catalog instances are re-enabled
    4. any legacy direct `official.yml` publish-job sketch in this document is historical/superseded, not an active authorization model
- The older buddy internal mutation-worker model is superseded. Active buddy publication uses `buddy.yml` as the entry/auth gate and delegates current publish/token-minting work to `release-orchestrate.yml`, with concurrency provided by `buddy.yml`'s dynamic entry group.
- Active entry workflows leave top-level workflow concurrency unset. After `authorize-entry` resolves the canonical project, version, and target SHA, only the entry workflow's job-level `orchestrate` concurrency group `release/${project_id}/v${release_version}` serializes the authorized release identity with `cancel-in-progress: false`; arbitrary extra callers are forbidden by design.

Superseded historical illustrative skeleton:

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

The illustrative skeleton above is a superseded direct-`official.yml` sketch. The current active entry workflow uses the checked-in `official.yml` dispatch inputs (`project`, `version`, optional `target`, and `force_update_tag`), resolves raw `target` once to `release_target_sha`, uses the resolved `needs.authorize-entry.outputs.release_group` concurrency key, then delegates publish/token-minting work to `release-orchestrate.yml`. The remaining durable requirements are that invalid project input fails before privileged work, mutation happens only after the required identity and receipt checks, approval-pending runs are cancelled or escalated under the active §7.6 GitHub Actions/environment metadata thresholds instead of holding their slot indefinitely, and expected recovery-path skips must not cascade into unintended downstream execution.

Superseded historical sketch from the older buddy mutation-worker model:

```yaml
name: _buddy-mutation-worker # superseded; do not reintroduce as an active publish boundary
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

- Active recovery authority comes from the resolved release identity (`project`, `version`, and target SHA), the checked-in descriptor/catalog snapshot, the `release-orchestrate.yml` graph, currently active receipt/report artifacts, package-registry `publish-result` receipts, GitHub Release `github-release-result` receipts, and registry/remote observations. The older admission-file, live-lock, durable artifact store, and direct mutation-stage recovery model is historical/future-only unless a later reviewed design reintroduces it.
- Active reruns must revalidate the same resolved release identity and the same planned publish-node identity before any package-registry or GitHub Release write. A receipt for one publish-node ID, target instance, package identity, version, release tag, or target SHA never satisfies another node.
- GitHub Release publish nodes are satisfied only by `github-release-result` receipts plus matching remote release/tag/asset evidence. Package-registry publish nodes are satisfied only by package-registry `publish-result` receipts plus matching registry evidence. A GitHub Release-shaped `publish-result` receipt is not a successful active publish result.
- Active recovery converges uncertain publish state to one of three reviewed outcomes for the same resolved release target: same-identity success, explicit abort/burn, or a documented retry/resume decision that preserves already-confirmed per-target state. The workflow and runbooks must not spin indefinitely on repeated “maybe published” observations.
- A publish target may be treated as a same-identity no-op only when live remote state proves that the already-present version or release artifact corresponds to the current resolved release plan and the authoritative artifact identity for that plan.
- For `github-release/public`, same-identity proof requires the release to be attached to the expected tag and the full remote asset set to match the authoritative artifact identity exactly by canonical asset name and digest. Missing, extra, renamed, or digest-mismatched assets are conflicts.
- For `npm/npmjs`, same-identity proof requires the exact package name/version plus the published tarball digest to match the authoritative artifact identity. Version-only presence without matching tarball digest is a conflict, not a no-op.
- For `pypi/pypi`, same-identity proof requires the exact expected distribution filename set (wheel and/or sdist) plus each file digest to match the authoritative artifact identity exactly. Missing, extra, renamed, or digest-mismatched files are conflicts.
- For `rubygems/rubygems-org`, same-identity proof requires the exact gem version and authoritative gem payload digest to match the authoritative artifact identity. When the registry surface cannot prove that digest, the design must treat the result as uncertain rather than as a same-identity success.
- When RubyGems exposes version presence but cannot provide authoritative digest proof for that version, the target must remain `publish-succeeded-unconfirmed` or `uncertain` in the active report/runbook state until another authoritative evidence source resolves identity; the workflow must neither treat version-only presence as success nor attempt a content-changing republish of that same version. If no remote mutation is observed for the target, the active state is `not-started`, not a blocked-stage value.
- When any target lacks a documented read-only identity proof strong enough for same-version reconciliation, the repository must treat that target exactly like the RubyGems case above: keep the target in `publish-succeeded-unconfirmed` or `uncertain` until another authoritative evidence source proves same identity, or record an abort/burn decision. “Version exists somewhere” is never enough to converge an uncertain target to success.
- Deferred future-only NuGet note: if `nuget:official` is re-enabled by a later reviewed dotnet/NuGet workflow path and active catalog instances, same-identity proof must require the exact package id/version and authoritative `.nupkg` digest to match the authoritative artifact identity. This requirement is not applicable to the current active target set while `families.nuget.instances: []`; version-only presence or metadata-only inspection would remain insufficient for any future enablement.
- If any official external mutation succeeds but the overall release result is partial, failed, or uncertain, operators must preserve the report, receipts, remote observations, and descriptor/catalog snapshot for that same resolved release target before approving any retry, resume, cleanup, or version-burn decision.
- Active recovery is target-granular, not all-or-nothing. A target already confirmed by a valid receipt plus matching remote evidence must not be re-published; its recovery path is verification-only. Targets recorded by the active report or runbook as `not-started`, `publish-succeeded-unconfirmed`, or `uncertain` remain eligible for a reviewed retry/resume, but each resumed publish job must re-check current receipts and remote state and skip itself when the target has already advanced to confirmed since dispatch. Mixed states such as “`github-release/public` already confirmed while `npm/npmjs` remains uncertain” are therefore first-class and must be preserved through recovery instead of being collapsed into a synthetic all-target retry.
- Pre-publish rebuild recovery is a last-resort path. It rebuilds from the same source snapshot, but it does **not** guarantee byte-for-byte identity with the original failed run because runner images, external tooling, or registry-side resolution behavior may have changed. Official release enablement therefore requires pinned toolchain and dependency inputs as described in §4.1, and the workflow must move from release identity resolution through build, provenance, tag, and orchestration without discretionary delay. Those controls reduce—but do not eliminate—that risk. Active recovery approval surfaces, `release-report.json`, receipt artifacts, run summaries, and runbooks must display this risk prominently. If no prior-run subject digest evidence exists for the same resolved release target, the rebuild may proceed only under a reviewed high-risk recovery decision that preserves the new digest evidence before any publish-capable continuation. In that case the rebuilt bundle is only a newly established reviewed artifact identity; it must not be described as proof that the failed earlier run produced equivalent bytes. If the rebuilt digest manifest differs from any known prior-run subject digest evidence for the same resolved release target, the workflow must stop and route the plan to the §7.5 break-glass abort path instead of silently continuing.
- Even with reviewed GitHub-hosted runner labels such as `ubuntu-latest`, `macos-latest`, or `windows-latest`, the underlying hosted image may evolve over time. Repositories that require stronger reproducibility than hosted labels provide must use self-hosted immutable images or an equivalently reviewed image-pin strategy; otherwise that residual rebuild-drift risk remains part of the design.
- If a target registry refuses same-version publication after a differing or uncertain prior attempt, or if `digestChangeReason` records differing rebuilt bytes, the design must route that frozen plan to the break-glass abort path in §7.5 instead of attempting a second content-changing publish.
- Active publish receipts, GitHub Release result receipts, skip receipts for intentionally skipped publish nodes, the tag result receipt, the release-plan and execution-set artifacts, the entry publish handoff, the `release-report-v1-<run_id>-<run_attempt>` artifact, and matching registry/remote observations are the active machine-readable evidence for declaring an active release complete; recovery must not rely only on ephemeral current-run memory or on §4.10 persisted confirmation records.
- A release may be treated as recovered only when every active target is either proven confirmed by active receipts plus matching remote evidence or has been explicitly retired by the reviewed abort/burn path. One target reaching confirmed never authorizes success for the whole plan while another target remains `not-started`, `publish-succeeded-unconfirmed`, or `uncertain`.
- If currently active receipt/report artifacts become unavailable after any publish-capable boundary, the design does **not** silently downgrade to rebuild-and-republish the same resolved release target. Operators must either externally prove every target already succeeded through active receipts plus matching remote evidence, or use the break-glass abort path in §7.5 to burn that target/version.
- When an active receipt/report read path is unavailable, the official tag annotation is an allowed fallback source for reconstructing the already-recorded release identity anchor during read-only diagnosis. That fallback is never an authority to perform a normal publish-path write, and active receipts plus matching remote evidence are still required before any success claim.
- Historical/future-only: older `pre-provenance`, `post-provenance`, `post-confirmation`, and `provenance-uncertain` blocked-stage transitions described the superseded admission/live-lock model. Active runbooks may use those words only as incident labels unless a later reviewed design reintroduces them as machine-readable workflow state.

### 7.4 Failure boundary matrix

This active matrix is scoped to the current dynamic release-identity/orchestrator/registry model. Historical admission-state and live-lock boundary tables from earlier drafts are not active requirements.

| Boundary                                                                                                                                                        | Active evidence to inspect                                                                                             | Allowed next mode                                                     | Operator action                                                                                                                                                                    |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| input, descriptor, catalog, or plan validation fails before orchestration                                                                                       | failed entry/plan diagnostics and no active publish receipts                                                           | fresh dispatch after reviewed fix                                     | fix the invalid input or checked-in descriptor/catalog state; do not infer any package-registry or GitHub Release mutation                                                         |
| build, provenance, or tag preparation fails before any publish job starts                                                                                       | build/provenance/tag diagnostics, immutable build receipts when present, and absence of publish receipts               | fresh dispatch or reviewed pre-publish retry                          | investigate the failure, preserve any prior digest evidence, and re-dispatch only after confirming no active target was mutated                                                    |
| active GitHub Release publish node lacks a valid `github-release-result` receipt                                                                                | report artifact inventory, `github-release-result` absence or schema failure, GitHub Release remote release/tag/assets | reviewed GitHub Release retry, same-identity success, or abort/burn   | never satisfy this node from `publish-result`; verify the release tag and full planned asset set before treating the node as complete                                              |
| active package-registry publish node lacks a valid `publish-result` receipt                                                                                     | report artifact inventory, `publish-result` absence or schema failure, registry package/version/digest observation     | reviewed registry retry, same-identity success, or abort/burn         | verify package identity, version, and artifact digest before retrying; version-only presence is not enough                                                                         |
| a receipt exists for the wrong publish-node ID, target instance, release tag, package identity, version, or target SHA                                          | the mismatched receipt plus the descriptor/catalog graph for the resolved release target                               | none until reviewed diagnosis                                         | ignore the receipt for success accounting, preserve it as incident evidence, and decide whether the affected target is absent, already same-identity, or unsafe and must be burned |
| one target is confirmed while another remains absent, failed, or uncertain                                                                                      | per-target receipts, GitHub Release result receipts, registry observations, and report failed-node lists               | reviewed target-granular continuation                                 | preserve the mixed state; rerun only targets that are not already durably confirmed                                                                                                |
| all active targets have valid receipts and matching remote evidence, but the final report/gate failed                                                           | release report, package-registry receipts, GitHub Release receipts, and remote observations                            | verification-only report repair or fresh report generation            | do not republish; reconstruct the report from durable receipts and remote evidence, then close the runbook decision                                                                |
| a pending or running official run is cancelled or replaced inside the same dynamic project/version release-tag concurrency group before publish evidence exists | GitHub Actions run state and absence of active publish receipts                                                        | fresh dispatch only if still needed                                   | treat the cancellation as platform concurrency behavior, not as a durable queue or blocked release state                                                                           |
| a protected official tag, package version, or GitHub Release remote identity conflicts with the resolved release target                                         | tag annotation, registry/GitHub Release remote evidence, descriptor/catalog graph, and receipts                        | break-glass repair/abort only until reviewed evidence restores safety | stop publish-capable work, preserve evidence, and route through §7.5 instead of attempting a content-changing republish                                                            |

Historical/future-only: older drafts used `blockedStage`, `.github/official-admission-state/**`, protected live locks, orphan-live-lock classifiers, `lockInstanceToken`, `LOCK_REUSE_REQUIRES_REVIEW`, and `clear-lock-only` as active recovery machinery. Those terms may still appear in retained design-memory context, but they are not active requirements for the current descriptor/catalog plus orchestrator receipt topology.

### 7.5 Break-glass process

Break-glass exists only for situations where normal reviewed PR or workflow paths cannot restore safe authority in time.

- the break-glass role is a separately managed human or automation identity that is narrower than normal release dispatch permission. In the active topology it must be recorded in the checked-in break-glass runbook index required by §4.1, or in active descriptor/catalog readiness evidence referenced by that runbook; it must not depend on `.github/repository-release-contract.json`.
- historical/future-only break-glass actions for the superseded live-lock model included clearing a residual live lock; active break-glass guidance focuses on repairing protected official tags, disabling release enablement for a project, and aborting a frozen release when continuing it would be unsafe
- every break-glass action requires two-person authorization, a linked incident ticket, and a written statement of why the normal reviewed path was unavailable or unsafe
- GitHub Environments on GitHub.com provide only a single approval from the configured reviewer list, not an authenticated “two-of-two” primitive. Therefore the checked-in break-glass environment is an **additional online gate only**. It may require one approval from a dedicated break-glass reviewer list and `prevent self-review`, but it does not by itself satisfy the design’s two-person control requirement.
- the actual two-person technical control is the mandatory split-control custody path recorded in the active checked-in break-glass runbook index and its readiness evidence from §4.1. Every break-glass action must require two distinct named custodians from that path to release or reconstruct the break-glass credential or secret material, regardless of whether the optional GitHub environment gate also succeeds.
- that active runbook/readiness evidence set must predefine one out-of-band split-control path, independent of GitHub workflow execution and environment approval, for cases where GitHub Actions, GitHub environment approval, GitHub App token issuance, ruleset drift, or workflow queue/concurrency behavior prevents the normal path. It must name the custody mechanism, the exact `offlineControlledMaterial`, the named custodians, and the minimum evidence package required before use. Per-project applicability and target-specific cleanup/verification requirements are linked through the active `src/**/three.release.yml` descriptors and `eng/release/target-instances.yml` catalog.
- `assuranceProfile = standard` and `assuranceProfile = high-assurance` are active readiness attributes recorded in the checked-in runbook/readiness evidence required by §4.1, with descriptor/catalog references for the projects and targets they govern. `standard` may use any reviewed custody mechanism named there; `high-assurance` should prefer `hsm-split-control` when the organization already operates it, otherwise `sealed-secret-split-control` with explicit reviewed justification. No profile requires a hardware HSM when the repository does not already operate one.
- every break-glass action must leave an audit trail containing actor identity, authorizer identities, timestamp, affected project key, affected refs/files, linked incident, and the exact before/after state
- within 24 hours, the protected branch descriptor/catalog state, release reports, and incident records must again describe the authoritative project release status
- the active checked-in source of truth for break-glass and readiness behavior is this §7.5 runbook contract, the readiness prerequisites in §4.1, active `src/**/three.release.yml` descriptors, `eng/release/target-instances.yml`, and the required stable repository-owned runbook paths named by §4.1. Dedicated runbook files may later split those templates into more concrete checked-in paths, but no active decision may depend on historical repository-release-contract runbook fields. The active runbook set must cover at least these scenarios: missing or corrupt receipt/report artifacts after a publish-capable boundary, partial target confirmation where some active targets have valid receipts plus remote evidence and others remain absent or uncertain, registry package-version identity conflicts, GitHub Release tag/asset identity conflicts, stuck or replaced dynamic release-identity concurrency runs, GitHub Actions queue or environment-approval degradation, credential or provider outages that prevent normal publish or verification, GitHub App or token-issuance failure, ruleset or protection drift that blocks reviewed recovery, unauthorized protected-ref mutation, unauthorized registry/GitHub Release remote mutation outside the workflow path, suspected credential leakage or misuse, suspected receipt/report or remote-evidence tampering, and aborting a frozen release
- the active missing/corrupt receipt or report runbooks must require operators to preserve all remaining artifacts, collect read-only registry/GitHub Release observations, classify every active target as confirmed, absent, partial, partial-authoritative, conflicting, or uncertain, and avoid rebuilding or republishing any target whose same identity is already proven
- the active break-glass runbooks for GitHub control-plane degradation must explicitly identify the named repository administrator/security contacts allowed to invoke the out-of-band path, the evidence required to prove the platform outage, queue/concurrency failure, environment-approval failure, token-issuance failure, or ruleset/protection drift, and the exact sequence for later reconciling the repository back to checked-in authoritative state
- the active abort-release runbooks must cover at least these cases: official tag exists but no publish receipt exists; one or more package-registry targets published while another target is absent or uncertain; GitHub Release exists with missing, extra, renamed, or digest-mismatched assets; registry package/version digest identity conflicts; permanently lost receipt/report evidence after remote mutation; and rebuild digest drift where rebuilt bytes no longer match prior evidence. For partially published targets, the runbook must classify each target as delete-capable, unlist-capable, yank-capable, deprecate-only, or burn-only; point to the documented per-registry cleanup procedure for that class; require explicit operator evidence for each target; and record whether the version is permanently burned.
- historical/future-only: residual live-lock cleanup, durable-store orphan-upload reconciliation, `blockedStage`, `pre-provenance`, `provenance-uncertain`, and `clear-lock-only` runbooks described the superseded admission/live-lock model and are not active requirements unless a later reviewed design reintroduces that topology
- The compromise/tamper runbooks must explicitly standardize the immediate freeze-and-forensics path: disable new official and buddy publication for the affected project set, preserve run and provider evidence, revoke or rotate the suspected credential or broker path, verify protected refs and remote target state against the last authoritative release identity, and require a reviewed incident disposition before any publication path is re-enabled.
- the checked-in cleanup matrix starts with these required classifications for active targets: official `github-release/public` = delete-capable; any later-enabled buddy `github-release/public` = delete-capable; buddy GitHub Packages targets `{npm/github-packages, rubygems/github-packages}` = delete-capable; `pypi/pypi` = yank-capable; `rubygems/rubygems-org` = yank-capable; `npm/npmjs` = deprecate-only. NuGet cleanup classifications are deferred until NuGet registry targets are re-enabled. A project-specific runbook may classify a target more conservatively as burn-only, but never less conservatively than this baseline without a reviewed design update.
- For rebuild digest drift, the active abort runbook must explicitly capture the prior digest evidence and rebuilt digest manifest side by side, identify any targets already mutated for that resolved release target, record whether each affected version/tag/release must be yanked, deprecated, unlisted, deleted, or permanently burned, and prohibit any publish-capable continuation until the abort decision is reviewed and recorded. When drift is discovered before any official tag exists and before any external target mutation occurred, the runbook may use a simplified no-external-mutation abort record: `targetsMutated = none`, no per-registry cleanup classification is required, and the only additional decision is whether the version is reusable or permanently burned.
- Historical/future-only: `provenance-uncertain` combined with permanently lost or irreconcilable durable-store evidence was a conservative assumed-published posture in the superseded model. The active equivalent is permanently lost receipt/report evidence after possible remote mutation, which must be handled by the active missing-evidence and abort runbooks above.
- Minimum exercise cadence is assurance-profile-dependent so the design scales down for standard open-source operation without dropping the control entirely:
    - `assuranceProfile = standard`: annual receipt/report reconstruction tabletop, annual protected-ref or remote-identity conflict tabletop, and annual frozen-release abort tabletop
    - `assuranceProfile = high-assurance`: quarterly receipt/report reconstruction drill, semiannual protected-ref or remote-identity conflict drill, and annual frozen-release abort tabletop
    - the latest successful exercise date, owner, and evidence reference must be recorded in the checked-in runbook or in the active descriptor/catalog readiness evidence described by §4.1 before official release remains enabled; historical repository-release-contract readiness fields are not an active source of truth

### 7.6 Operational diagnostics

Active operational diagnostics use only the currently implemented release
surfaces:

- GitHub Actions run, job, and environment metadata for `buddy.yml`,
  `official.yml`, and `release-orchestrate.yml`
- the dynamic release-identity concurrency group emitted by the entry workflow,
  including the resolved `release_group` / `release_group_snapshot` values and
  the active job-level group
  `release/${project_id}/v${release_version}`
- active release and registry environments: `github-release`, `pypi`,
  `npmjs-gate`, `npmjs`, and `rubygems`
- `release-orchestrate.yml` artifacts and logs, especially `release-plan.json`,
  `execution-sets.json`, `release-report.json`, package-registry
  `publish-result.json` receipts, GitHub Release `github-release-result.json`
  receipts, `skip-result.json`, `tag-result.json`, build receipts, and the
  `release-completed` receipt-validation outcome
- checked-in descriptor/catalog state from `src/**/three.release.yml` and
  `eng/release/target-instances.yml`
- provider freshness and provider-drift evidence derived from the active
  descriptor/catalog bindings, workflow/environment configuration, and reviewed
  provider evidence used by CI and release validation
- package-registry and GitHub remote evidence, including package/version/digest
  observations, GitHub tag state, GitHub Release state, and release asset names,
  sizes, and digests

The implemented operator view is report-based: inspect the Actions run,
downloaded `release-report.json`, and receipt artifacts emitted by
`release-orchestrate.yml`; when remote state must be reconciled, compare those
receipts with current package-registry and GitHub evidence collected by active
helper commands such as `eng/scripts/workflow_release_control.py
observe-remote-publications`. This view is authoritative for current
diagnostics and replaces historical live-lock, checked-in admission-state,
durable-store, and suspension dashboards.

Operational observability is a required part of the design, not an
implementation afterthought. Active monitored signals and runbook checks are:

- count and age of active registry-environment approval waits by project,
  target, run id, run attempt, and environment
- age of pending active buddy/official entry workflow job-level `orchestrate` dynamic release-identity concurrency group instances, keyed by `release/${project_id}/v${release_version}`
- `orchestrate` job age while it holds `release/${project_id}/v${release_version}`, including project id, release version, target SHA, run id, and run attempt
- runner-start delay for release jobs after GitHub schedules them but before a
  runner begins executing the job
- missing, schema-invalid, stale-run, or planned-identity-mismatched
  package-registry `publish-result` receipts for active package-registry publish
  nodes
- missing, schema-invalid, stale-run, or planned-identity-mismatched GitHub
  Release `github-release-result` receipts for active GitHub Release publish
  nodes
- `release-report.json` failures, `failed-publish-node-ids`, and published-node
  counts that do not match the active execution set
- registry package/version/digest conflicts or GitHub tag/release/asset conflicts
  against the frozen release plan
- stale active provider-review evidence records for OIDC-backed targets
- active provider-review evidence records that will become stale within the
  applicable warning window for OIDC-backed workflow-only targets without
  read-only inspection support
- `workflow-only` provider-drift probe outcomes from the closed set
  `{match, drift-detected, inspection-unsupported, inspection-unavailable,
inspection-error}`
- reviewed recovery, abort, cleanup, or descriptor/catalog repair PRs that
  remain unmerged past the documented SLA
- GitHub Actions queue delay, runner scarcity, release-provider rate-limit
  saturation, environment-approval failure, token-issuance failure, and
  ruleset/protection drift as observed through active GitHub or registry
  metadata

Required alert thresholds are severity-tiered rather than one-size-fits-all.
These SLAs measure acknowledgement, triage, escalation, and
controlled-state-transition deadlines, not full recovery completion or PR-merge
completion:

- **Tier 0 — pre-mutation queueing / approval wait**
    - registry-environment approval pending longer than the configured approval
      timeout plus 15 minutes: warn
    - pending for an active buddy/official entry workflow job-level
      `orchestrate` dynamic release-identity concurrency group keyed by
      `release/${project_id}/v${release_version}` longer
      than 15 minutes with no running `orchestrate` job holding that same group:
      warn
    - running `orchestrate` job holding
      `release/${project_id}/v${release_version}` longer
      than the documented pre-mutation bound without producing the expected next
      active receipt/report artifact: warn
    - runner-start delay for a release job longer than 10 minutes after
      scheduling: warn
    - any of the above older than 60 minutes: page
- **Tier 1 — receipt/report or remote-evidence mismatch**
    - any active publish node missing its required current-run receipt after the
      corresponding publish or GitHub Release job reports success: immediate
      run failure plus incident annotation
    - any receipt whose publish-node ID, target instance, package identity,
      version, release tag, target SHA, asset set, size, or digest does not match
      the frozen plan: immediate run failure plus incident annotation
    - any active target with remote state that conflicts with the frozen plan:
      page the release owner within 30 minutes and route through the §7.5
      break-glass abort/repair decision before any content-changing retry
- **Tier 2 — partial or uncertain target state**
    - one active target confirmed while another remains absent, failed, or
      uncertain for longer than 30 minutes: page
    - unresolved partial/uncertain state older than 2 hours: require named human
      triage and incident annotation
    - unresolved partial/uncertain state older than 24 hours: management-visible
      escalation
- **Tier 3 — customer-visible or tamper-sensitive state**
    - suspected credential compromise, unauthorized protected-ref mutation,
      unauthorized registry/GitHub Release mutation, receipt/report tampering, or
      provider-drift mismatch: immediate incident open plus page
    - named human triage within 30 minutes
    - break-glass evaluation within 2 hours if normal clearance is still
      unavailable
    - management-visible escalation within 12 hours if unresolved

The operator runbook must explicitly cover GitHub's latest-wins pending-run
replacement behavior for the entry workflow `orchestrate` job's dynamic
release-identity concurrency group. When run `A` is running in
`release/${project_id}/v${release_version}`, run `B` is
pending in that same project/version release-tag group, and newer run `C` causes
GitHub to cancel and replace pending `B`, operators must treat `B` as platform
cancellation before mutation rather than as a blocked release: confirm from the
Actions UI or API that `B` concluded as cancelled, inspect whether `A` or `C`
now owns the active job-level release-identity group, and re-dispatch only if the
repository still needs `B`'s resolved release target after the surviving run
finishes. SLA tracking for the cancelled run stops at cancellation; any later
work starts a new run and new approval lifecycle.

Provider-review freshness alerting is mandatory, not optional:

- for OIDC-backed targets with `providerSupportsReadOnlyInspection = false` that
  rely only on the 365-day outer bound: warning alert at 30 days before expiry
  and blocking-severity alert at 7 days before expiry
- for `workflow-only` targets on `assuranceProfile = standard`: warning alert at
  48 hours before the 7-day limit and blocking-severity alert at 24 hours before
  that limit
- for `workflow-only` targets on `assuranceProfile = high-assurance`: warning
  alert at 8 hours before the 24-hour limit and blocking-severity alert at 2
  hours before that limit
- after the applicable limit is exceeded: release-time hard failure plus incident
  annotation until the reviewed record is refreshed
- any OIDC-backed target whose `providerConfigReviewedAt` is already older than
  365 days is configuration-invalid even when no stricter `workflow-only` limit
  applies; `ci.yml` and release-time validation must both fail it closed

Emergency override for the applicable stricter `workflow-only` provider-review
freshness limit exists only through the §7.5 break-glass path and only for
targets with `providerSupportsReadOnlyInspection = false`. That override is
single-use for one frozen release plan, must record an incident ticket plus
explicit evidence that `providerWorkflowPath`, `providerEnvironment`,
`providerKey`, `providerAudience`, and `allowedRefClaims` still match the last
reviewed contract, and must not be used to authorize any change to those trust
inputs. The repository must refresh the normal reviewed provider verification
record within 24 hours after that emergency release or keep the target disabled.

- Historical/future-only diagnostics for superseded release models include:

- `eng/scripts/release-status` and its former per-branch admission-state
  JSON/text contract
- checked-in blocked-state files, historical recovery `blockedStage` values, and
  any requirement that a live lock carry a frozen plan
- durable artifact-store metadata, `artifact://...` release anchors,
  confirmation-record rollups, durable-store restore drills, and orphan-upload
  metrics
- live-lock polling, lock-age escalation, baseline approval polling, subordinate
  live-lock cleanup, `LOCK_HELD_BY_CONCURRENT_RUN`, and `LOCK_STOLEN`
  classification
- suspension-record status, degraded-mode suspension state, monitor
  heartbeat/acknowledgement state, control-plane commitment digests, external
  release-monitor cancellation decisions, and external broker health as active
  release blockers
- brokered GitHub App mutation credentials, production-ref-write environments,
  production-evidence-write environments, and the older buddy mutation-worker
  model

Those surfaces may be retained as historical context or reintroduced only by a
later reviewed design. They must not be used by the active workflows, reports, or
runbooks to declare release success, block release enablement, or satisfy active
package-registry/GitHub Release receipt validation.

## 8. Shared Workflow Rules

- Build/test reusable workflows and attestation reusable workflows must declare their own minimal `permissions:` upper bounds rather than omitting `permissions:` entirely. For normal build/test paths that means `contents: read` only, with no `id-token: write` and no write-scoped package or contents permissions unless a documented design exception requires them.
- Build/test and attestation reusable workflows must be called with `secrets: {}`. The explicit empty mapping is required so review can see that no caller secrets are being inherited; `secrets: inherit` is forbidden for these reusable calls, and omitting the key is not the approved style for this design.
- Current official package-registry publication is orchestrated by `release-orchestrate.yml`, whose split token-minting / publish jobs are the active workflow identity boundary for PyPI and RubyGems and the publish-command boundary for npmjs. For npmjs `workflow_call`, the provider-side Trusted Publisher identity is the active `official.yml` caller. Older direct `official.yml` publish-job guidance is superseded.
- Attestation reusable workflows are not publish-capable authorization boundaries. A release attestation helper may hold `id-token: write` only for attestation generation/verification under an authorized release workflow job that already passed the required lock/identity checks; it must not mint publish credentials, mutate protected refs, or publish to external registries.
- Build/test/package reusable workflow runner selection follows the active workflow code: .NET release variants select `ubuntu-latest`, `macos-latest`, or `windows-latest` from OS/RID dimensions, while current Python, Node, WXT, and Ruby release build workflows use `ubuntu-latest`.
- Every `actions/checkout` invocation in `buddy.yml`, `official.yml`, and their reusable workflows must set `persist-credentials: false`. If a later step truly needs authenticated `git` access, it must re-authenticate explicitly for that step instead of inheriting persisted checkout credentials.
- Shell steps must treat workflow inputs and derived values as untrusted: map through `env:` first, then reference quoted variables.
- Raw `${{ ... }}` interpolation inside shell scripts is forbidden. Workflow expressions must be mapped into environment variables or explicit action inputs before shell execution.
- `eval`, untrusted `bash -c`, and sourcing any shell content that comes from payload-controlled files, workflow inputs, or other untrusted data are forbidden.
- Writes to `GITHUB_OUTPUT`, `GITHUB_ENV`, and `GITHUB_STEP_SUMMARY` must use the documented file-append form with trusted keys, trusted here-doc delimiters, and delimiter values that cannot be influenced by untrusted content. Approval-related reviewer text written to `GITHUB_STEP_SUMMARY` is allowed only from `buddy-audit` and `official-review-surface`, and only when derived from already-validated frozen outputs. For those summaries, single-line dynamic values must be rendered as inline code, multi-line or structured values must be rendered inside fenced code blocks, and raw Markdown-significant text from dynamic values is forbidden. If a value must appear outside code formatting, the workflow must escape Markdown metacharacters at minimum for backtick, backslash, pipe, asterisk, underscore, hash, angle-bracket, bracket, parenthesis, exclamation-mark, and hyphen-plus-space/task-list sequences so untrusted/package-derived text cannot change heading structure, table layout, task-list state, links, images, or raw-HTML rendering.
- All non-local third-party actions outside the GitHub-maintained `actions/` organization must be pinned to a full commit SHA. First-party `actions/*` references may use reviewed version tags and do not require SHA pinning.
- All jobs that rely on NBGV or other git-history-derived metadata must use full history.
- Permission grants use the active workflow-level baseline required by each workflow, normally `contents: read` and `actions: read` only where Actions metadata is required, with job-level least-privilege escalation for publish or provenance jobs. The older universal `permissions: {}` baseline is superseded.
- `buddy-audit` must declare its own explicit job-level permissions: `contents: read`, `deployments: write`, and `pull-requests: write` are required; `actions: read` is allowed only when the implementation reads Actions-run metadata beyond the default artifact upload path.
- `baseline-approval-and-audit` must declare its own explicit job-level permissions. `actions: read` is mandatory because the job reads the documented workflow-run approvals and pending-deployments APIs. Any additional permission such as `contents: read` must be justified by another explicit step in that same job; there is no blanket exception to the least-privilege rule.
- `id-token: write` must not be granted at workflow scope or to build/test jobs. For external-registry trusted publishing, it may appear on the exact environment-scoped publish/provenance job that actually mints GitHub OIDC for registry credentials, and it may also appear on a reusable-workflow caller job only as the upper-bound permission needed by called OIDC publish/provenance jobs. That caller grant is not itself a token-minting boundary: the called environment-scoped external-registry job remains the scoped token requester. GitHub artifact-attestation/provenance jobs may grant `id-token: write` and `attestations: write` without an external registry environment when constrained to the authorized release workflow path; those jobs request GitHub provenance signing only and must not mint external registry credentials. For npmjs trusted publishing, the caller workflow identity remains an additional provider-specific identity requirement alongside the reusable publish job's scoped OIDC permission.
- When a release workflow freezes distinct control-plane and payload SHAs, it must check them out into distinct fixed paths such as `control-root/` and `payload-root/`.
- Local composite actions, helper scripts, and other workflow-owned control-plane code must execute only from the control checkout.
- Project build/test/package commands may read payload files only from the payload checkout, and any file that influences project resolution, version resolution, dependency resolution, build, package, or artifact selection belongs to the payload checkout even when it lives at repository root.
- Jobs must not re-resolve the selected protected dispatch branch into a new HEAD after `preflight-validate`; they must consume the emitted frozen values only.

## 9. Summary of Key Design Properties

- The only externally exposed release and release-authority validation workflows are `ci.yml`, `buddy.yml`, and `official.yml`; `.github/workflows/codeql.yml` is allowed as non-release security analysis without release authority, publish credentials, protected-ref bypass credentials, or release mutation worker access; scheduled, manually dispatched, or carefully dashboard-edit-triggered dependency-maintenance workflows are allowed only without release authority.
- Active `src/**/three.release.yml` descriptors plus `eng/release/target-instances.yml` are the checked-in machine-readable source of truth for release descriptors and target catalog data, and `ci.yml` validates them with strict duplicate-key rejection.
- Buddy publish authorization starts in `buddy.yml` entry authorization and delegates active publish work to `release-orchestrate.yml`; direct buddy publish jobs and buddy mutation workers are superseded.
- Buddy runs use dynamic entry concurrency, descriptor/catalog state, and orchestrator checks; superseded official admission state and live-lock checks are not active gates.
- Active release/registry environments (`github-release`, `pypi`, `npmjs-gate`, `npmjs`, and `rubygems` as applicable) are the current human approval / OIDC environment gates. The older `production-<project-key>` baseline gate is superseded unless a later reviewed design reintroduces it.
- Official target auth contracts are closed-schema objects with workflow-enforced exact OIDC ref claims, recorded provider support facts, provider trust capability sets, machine-readable `workflow-only` rationale when exact provider ref enforcement is unavailable, and target-specific confirmation retry settings. Active GitHub Release publication uses `github-token` with `environment: github-release`; broker-backed high-privilege GitHub mutation is historical/future-only.
- Active recovery is target-granular and resumes from the resolved release identity already frozen by the descriptor/catalog-selected plan; it does not recompute package names, versions, target SHAs, or release tags from a newer branch snapshot. Operators classify each selected target from current receipts, release reports, read-only registry or GitHub Release evidence, and the protected descriptor/catalog state, then continue only targets whose same resolved identity is absent or explicitly safe to complete.
- Package-registry recovery is driven by package-registry receipts and remote package/version evidence; GitHub Release recovery is driven by `github-release-result` receipts, the official release tag, and the live release asset set. Missing, corrupt, conflicting, or tamper-suspect receipt/report evidence routes to the active break-glass or abort runbooks in §7.5 rather than to historical `blockedStage`, blocked-entry approval, pre/post-provenance, durable-store fact reconciliation, residual live-lock clearing, or `release-status` workflows.
- Successful official releases preserve their resolved release identity through active receipts, reports, remote target evidence, and descriptor/catalog state. Historical durable artifact-store anchors, live locks, and the `eng/scripts/release-status` helper are future-only unless a later reviewed design reintroduces them.
- Active projects use the canonical roots under `src/`, `src/lab/`, and `tests/`; the former `OneDotNet/` subtree has been migrated, and release workflows are already active. The remaining gap is deferred NuGet registry publication.
