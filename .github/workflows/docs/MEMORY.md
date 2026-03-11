# Workflow Design Memory

For AI agents editing workflow design docs.

## Do not reintroduce these old patterns

- blanket `secrets: inherit` for publish flows
- OIDC `job_workflow_ref` anchored only to `official.yml`
- pre-checkout selection of `semver2` vs `pep440` validators
- silent filtering of unknown `release.json` targets
- unofficial Python registry targets unless they are explicitly designed
- buddy -> official as a required promotion chain
- buddy GitHub Releases or buddy repository traceability tags in this repository
- buddy `force=true` inputs or any unofficial overwrite path in this repository
- reusable workflows declaring their own `permissions` blocks
- documentation-only production-environment protection that is not project-scoped as `production-<project-name>`
- reusable workflow contracts that omit caller `permissions`
- action pinning rules that exempt first-party `actions/*`

## March 2026 external-system research

### Confirmed documented facts

- GitHub's documented rerun availability for a workflow run is 30 days from the initial run
- GitHub's public examples for build attestations require both `id-token: write` and `attestations: write`
- npmjs trusted publishing is anchored to the calling workflow filename `.github/workflows/official.yml`; when publish happens through a reusable workflow, npmjs does not treat the called reusable workflow path as the trust anchor
- npm publicly documents the OIDC audience value `npm:registry.npmjs.org`
- PyPI's documented GitHub Actions trusted-publishing audience is `pypi`
- RubyGems.org publicly documents reusable-workflow binding via GitHub's `job_workflow_ref` behavior
- this design does not rely on any documented exact branch, tag, or commit-SHA binding in the provider UI for `npmjs`, `PyPI`, or `RubyGems.org`
- this design does not treat `NuGet.org` as having a documented GitHub Actions trusted-publishing path; official NuGet publication therefore uses an explicit API key instead
- GitHub attestation verification publicly documents repository identity, repository owner identity, ref, source SHA, signer workflow path, and workflow digest as verifiable provenance dimensions; environment may be present when the job used a GitHub environment

### Not yet documented or still treated as reviewed assumptions

- GitHub does not publicly document a separate environment-approval expiry clock; pending approvals are therefore tracked operationally against the enclosing run's 30-day rerun deadline rather than against an assumed GitHub expiry
- PyPI publicly documents workflow-file trust but does not explicitly document reusable-workflow path handling; the design models `pypi:official` as reusable-workflow based on GitHub `job_workflow_ref` semantics and requires pre-implementation validation
- npmjs does not publicly document stronger job-level separation when multiple jobs in the same entry workflow enter the same protected environment, so the design treats those jobs as one shared external trust boundary
- RubyGems.org does not publicly document a required OIDC audience value that this design can check into inventory

## Current assumptions

- before implementation starts, design reviews should ignore mismatches between the current repo implementation and the target design unless the task explicitly asks to reconcile implementation
- build and publish jobs default to `secrets: {}`
- all actions, including `actions/*`, are pinned to full commit SHA, `docker://` references are pinned to immutable digests, and `hk` runs both `actionlint` and `zizmor --strict`
- dependency-update automation must cover `.github/workflows/**` so pinned action SHAs are refreshed intentionally rather than drifting indefinitely
- official external auth is not uniform: `npmjs`, `PyPI`, and `RubyGems.org` use trusted publishing with provider-specific selector models, while `NuGet.org` uses an explicit `NUGET_API_KEY` environment secret; branch eligibility is enforced on the GitHub side through deployment-branch policy and checked-in inventory
- official releases are `workflow_dispatch` runs from protected control-plane branches, and the workflow derives and creates the official release tag internally from the resolved project version
- the official protected control-plane branch set is `main` plus eligible protected maintenance branches `release/<project-name>/v<series>`, where `<series>` is numeric like `1.2.x` without a leading `v`
- privileged official caller workflow, reusable workflows, helper code, and release payload source all come from the same dispatch-selected protected control-plane branch
- reusable workflow shell steps must map `inputs.*` through `env:` before use
- shell hardening also applies to `${{ github.* }}`, `${{ needs.*.outputs.* }}`, and `${{ env.* }}` values derived from untrusted contexts in `run:` steps; untrusted `${{ ... }}` expansions should not appear in shell source and must be mapped through `env:` first, and the design also bans unsafe writes to `GITHUB_ENV`/`GITHUB_OUTPUT`/`GITHUB_PATH`/`GITHUB_STEP_SUMMARY`, `eval`, and other dynamic shell execution with untrusted data
- local composite actions under `.github/actions/**` follow the same shell-hardening rule, and values received through `with:` inputs must also be remapped through `env:` before shell use
- `ci.yml` on `pull_request` must require repository-level approval for forked PR workflow runs because local workflows, actions, and helper scripts execute from the PR merge commit
- `ci.yml` includes a repo-policy check that verifies the fork-PR approval setting through the metadata App token
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- `release.json` has `schemaVersion: 1` and allows only `schemaVersion` plus `targets`
- `release.json` top-level validation is strict and equivalent to `additionalProperties: false`; unknown top-level keys are hard failures
- unsupported future `schemaVersion` values are hard failures; schema upgrades are coordinated and do not need backward-compatibility shims before implementation starts
- official release identity tag format is `release/<project-name>/v<version>`
- `project-name` is case-sensitive, must resolve to exactly one project, and must reject any occurrence of `..`, any trailing `.`, and any `.lock` suffix for ref safety
- project resolution is by exact leaf-directory-name match from the repository root with no case folding, substring matching, or heuristic tie-breakers
- project resolution must emit exactly one workflow language in `{csharp, python, jsts, ruby}`; no match, ambiguous match, or unsupported language is a hard failure
- each `buddy.yml` / `official.yml` run releases exactly one project
- buddy intentionally allows releases from development branches
- buddy uses the workflow definitions from the selected dispatch branch and currently publishes only to unofficial package registries `{nuget:gpr, npm:gpr, rubygems:gpr}`; Python currently has no unofficial channel
- official releases must come from protected `main` or protected maintenance branches `release/<project-name>/v<series>`
- maintenance branches are explicitly managed supported lines; missing non-default lines fail with operator guidance, and non-`main` release lines require a separate maintenance-branch existence check plus exact caller-ref matching before official release is allowed
- omission of a required reusable-workflow input is a hard validation failure; the design may rely on that failure mode, but should not claim a more precise runner-allocation timing guarantee unless GitHub documents it explicitly
- official release-line validation derives `release/<project-name>/v<series>` by stripping suffix material, reading at most the first two numeric components, zero-padding a missing minor component to `0`, and rendering `<major>.<minor>.x`
- official release-line validation first compares the resolved release line against a frozen `origin/main` comparison snapshot captured at the start of `resolve-context`; if they match, caller ref must be `main`; if they differ, caller ref must be the exact matching protected maintenance branch
- official release tags under `refs/tags/release/**` must be protected
- tag protection must cover both tag creation and tag updates; legacy protection that only blocks deletion or force-push is insufficient for `refs/tags/release/**`
- Ruby uses the repository's `validate_rubygems_version.py` subset policy rather than generic RubyGems version compatibility
- stable GitHub Releases use `github:official`
- same-tag stable GitHub Release is idempotent, not a hard fail
- an official run may replace an existing same-tag GitHub pre-release with a stable GitHub Release after remote asset identity checks succeed
- `official.yml` includes `preflight-check` for the derived project-scoped environment `production-<project-name>` with required reviewers
- `official.yml` also runs `static-analysis` symmetrically with `buddy.yml`
- repository protection uses GitHub repository rulesets only for protected branches and protected tags; legacy branch-protection compatibility is out of scope before implementation starts
- `detect-changes` in `ci.yml` requires `pull-requests: read`
- public repositories may read enough live environment and ruleset metadata for basic drift checks via unauthenticated requests or the default read-only `GITHUB_TOKEN`; private repositories or checks that need sensitive fields still require the dedicated metadata App token
- `preflight-check` uses `permissions: {}` on the job, does not perform checkout, and mints a dedicated just-in-time read-only GitHub App installation token for both environment metadata and repository rulesets metadata; the GitHub App private key lives in an organization-level Actions secret scoped only to this repository
- the metadata GitHub App requires `metadata: read`, `administration: read`, and `environments: read`
- missing GitHub Environments auto-create without protection if first referenced by workflow YAML, so `preflight-check` must treat environment existence and required-reviewer policy as explicit invariants rather than assuming absent environments fail closed
- GitHub App installation tokens minted at runtime are masked before first use, and App private keys rotate at least every 90 days and immediately on suspected compromise
- `preflight-check` must hard-fail on GitHub API errors outside explicitly handled cases
- `preflight-check` must set an explicit client timeout on every GitHub API call so a hung response fails fast rather than consuming the whole job timeout
- `preflight-check` must specifically require a `required_reviewers` protection rule, `prevent_self_review = true`, an exact-name deployment branch policy restricted to the official protected control-plane branch set, reject wildcard deployment-branch patterns, query the Repository Rulesets API only, verify that allowed maintenance branches carry the same ruleset profile as `main`, verify branch-ruleset bypass actors are limited to the dedicated release-engineering emergency-cleanup group, and verify an active tag ruleset for `refs/tags/release/**`
- workflows covered by this design reject `pull_request_target`, `workflow_run`, and `secrets: inherit` through repository-policy linting
- emergency-cleanup group size and reviewer-overlap constraints are governance requirements, not checks that `preflight-check` can machine-enforce with the metadata App
- `preflight-check` verifies the protection profile of every branch already listed in the production deployment policy; completeness of the allowed official caller-ref set is enforced separately by `resolve-context`'s publish trust inventory preflight
- `preflight-check` is an audit-before-use guard and still has a residual TOCTOU window if environment or tag-ruleset protection changes after the check passes; later environment evaluation and live tag ruleset evaluation remain authoritative
- reusable workflows must not declare `permissions:` blocks
- build reusable workflows require caller `contents: read`
- build reusable workflows perform internal `fetch-depth: 0` checkout with `persist-credentials: false` and accept `checkout-ref` so callers can pin the exact workflow commit when they want to be explicit
- buddy publish jobs must depend on `static-analysis` directly
- buddy publish jobs also gate explicitly on `resolve-context.result == 'success'` and `static-analysis.result == 'success'`
- buddy publish jobs also gate explicitly on the single language-matching build job succeeding while the three non-matching build jobs are skipped
- `buddy.yml` and `official.yml` end with a `release-complete` gate that first asserts resolver/static-analysis success, validates the selected target set against the actual publish-job results, requires non-selected publish jobs to be skipped, and verifies the single language-matching build result
- official publish jobs should gate explicitly on `resolve-context.result == 'success'`, `static-analysis.result == 'success'`, and `create-release-tag.result == 'success'`, using `fromJson(... || '[]')` style defaults for target-array guards, and official `release-complete` also validates `require-provenance.result == 'success'`, `create-release-tag.outputs.tag-result in {created, no-op}`, and the npm `applied-dist-tags` output when npm is selected
- `.github/CODEOWNERS`, `.github/workflows/**`, `.github/actions/**`, `.github/official-caller-refs.json`, `.github/publish-trust-inventory.json`, `.github/planned-change-windows.json`, `.github/release-recovery-ledger.jsonl`, `eng/scripts/**`, `**/release.json`, `**/version.json`, `hk.pkl`, `PklProject`, `PklProject.deps.json`, `mise.toml`, `mise.lock`, `global.json`, `nuget.config`, `**/NuGet.Config`, `Directory.*.props`, `**/*.targets`, `package.json`, `pyproject.toml`, `biome.jsonc`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `.npmrc`, `**/.npmrc`, `uv.lock`, `Gemfile.lock`, `Directory.Packages.props`, and other trusted control-plane helper or shared dependency-control files must be protected by `CODEOWNERS` review, and protected control-plane branches must require code-owner review via rulesets
- project-scoped environments `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` replace the old single `environment: production` model, and each such environment's deployment branch policy allows only the official protected control-plane branch set for that project and only as exact branch names, never wildcard patterns
- official registry auth is split by documented provider capability: `npmjs`, `PyPI`, and `RubyGems.org` use trusted publishing, while `NuGet.org` uses an explicit `NUGET_API_KEY` environment secret because no documented trusted-publishing path is assumed in this design
- no portable wildcard future-branch trust is assumed; branch-set changes are therefore managed on the GitHub side by exact deployment-branch-policy entries plus same-PR updates to `.github/official-caller-refs.json`, `.github/planned-change-windows.json`, and `.github/publish-trust-inventory.json`, while only repository-identity changes, auth-mode changes, selector-workflow changes, or documented audience changes require registry-side auth updates
- the authoritative repository-side source of active official caller refs is `.github/official-caller-refs.json`; every active protected control-plane branch carries the same normalized contents, and inventory `allowedCallerRefs` must mirror it exactly
- the publish trust inventory has `schemaVersion: 1`, records `entryWorkflowPath`, fully qualified `allowedCallerRefs`, a target-to-publish-workflow-path mapping, a target-to-environment mapping, a target-to-auth-mechanism mapping for official targets, a `targetTrustedPublisherSelectors` mapping for trusted-publisher-backed targets, and `targetOidcAudiences` only for targets whose provider publicly documents a required audience; buddy targets are intentionally excluded because they do not rely on external registry-side trust state
- publish trust inventory validation is strict and equivalent to `additionalProperties: false` at the top level
- official `resolve-context` performs a publish trust inventory preflight against the checked-in inventory from the current protected caller ref after official target resolution and before any publish job becomes eligible
- CI includes an explicit `trusted-release-inventory` job that checks out the PR merge commit and compares the post-change `entryWorkflowPath`, the deduplicated fully qualified `allowedCallerRefs` set derived from `.github/official-caller-refs.json`, the `publishWorkflowPaths` mapping, the `targetEnvironments` mapping, the `targetAuthMechanisms` mapping, the `targetTrustedPublisherSelectors` mapping, and the `targetOidcAudiences` mapping against `.github/publish-trust-inventory.json`; CI fails on any mismatch whether or not the inventory file itself changed
- registry-side OIDC trust settings are not queried portably; release operators must verify registry-side trust separately when diagnosing control-plane drift
- reusable publish docs must list required caller permissions
- package-registry publish workflows require caller `contents: read` plus their registry-specific write scope so they can check out trusted helper code; `_publish-github.yml` is official-only and uses `contents: write`
- `contents: read` cannot push tags, branches, or GitHub Releases, and `persist-credentials: false` only prevents checkout from persisting credentials locally; it does not grant write capability or substitute for explicit job permissions
- `contents: write` is the minimum GitHub Actions permission available for creating or updating GitHub Releases in this design
- idempotent publish handling only treats duplicate-version outcomes as success when remote artifact identity matches; auth and upstream failures stay hard-fail
- reusable publish workflows must emit a machine-readable workflow output indicating whether the run performed a new publish or an idempotent no-op
- a selected publish target that settles as `publish-result = no-op` still finishes with job result `success`; job result `skipped` is reserved for non-selected targets only
- `_publish-github.yml` is official-only and receives `project-name` explicitly so it can create deterministic release titles `<project-name> v<version>`
- `_publish-github.yml` is not a standalone authorization boundary; same-repo caller restriction comes from CODEOWNERS plus protected `release/**` tags, not from GitHub workflow-call semantics
- GitHub Release scans in `resolve-context` and `_publish-github.yml` must paginate to completion and hard-fail on API, auth, rate-limit, transport, or response-shape errors; overwrite and no-op decisions are never made from partial scan results
- `_publish-github.yml` re-checks for a conflicting stable release title immediately before mutating GitHub Release state, and draft releases with the deterministic stable title are part of the same stable identity space
- official GitHub Release idempotency also requires matching remote asset identity
- read-only checkouts in resolve/static-analysis jobs use `persist-credentials: false`
- every workflow job must declare `timeout-minutes`; omission is a lint failure enforced through `hk`/`actionlint`
- timeout defaults are explicit: `preflight-check`/resolve/static-analysis `15`, Ubuntu builds `30`, Windows builds `45` because hosted Windows runners have higher startup and restore/test overhead, publish jobs `15`, tag-management jobs `10`, and `ci-passed`/`release-complete` `10`
- reusable workflow `permissions:` prohibition is also lint-enforced through a custom `hk` check because `actionlint` does not cover it directly
- official `resolve-context` depends explicitly on `preflight-check`
- official static-analysis evaluates the dispatch-selected source ref directly, the same payload that will be built and released by that run
- `resolve-context` in both buddy and official hard-fail if `nbgv-python` cannot resolve the version deterministically; deterministic means one governing `version.json`, one normalized version string from a full-history checkout, and successful language-specific validation
- `resolve-context` outputs the NBGV-resolved version directly in both channels; official then derives the protected release tag `release/<project-name>/v<version>` from that resolved version
- official releases may publish valid prerelease versions; prerelease status does not relax branch or tag rules and only changes npm dist-tag derivation
- `resolve-context` in official derives deterministic npm dist-tags from branch, release line, and prerelease channel as a compact JSON array: stable `main` uses `["latest"]`, prerelease `main` lowercases the first prerelease identifier and uses that entire identifier as the channel token only when it matches `^[a-z][a-z0-9]*$`, stable maintenance branches use `["release-v<major>.<minor>"]` and never append `latest`, and maintenance prereleases use `["release-v<major>.<minor>-<channel>"]`
- official Python version validation also rejects `.devN` development-release forms
- buddy NBGV versions may differ across branches or after new commits change git-history height; that is expected unofficial-channel behavior rather than a recovery bug
- PEP 440 epoch markers (`!`) are intentionally unsupported in release tag versions
- release-line derivation is uniform across ecosystems: strip suffix material, read at most the first two numeric release components, zero-pad a missing minor component to `0`, then render `<major>.<minor>.x` (for example `1.1 -> 1.1.x`, `1.2.3rc1 -> 1.2.x`)
- `mise.lock` is committed alongside `mise.toml`; jobs hard-fail when `mise.lock` is absent, key caches by both files, and use lockfile-backed digest verification where supported by the selected MISE backend
- `release.json` is loaded only from `<project-root>/release.json`; there is no upward search or inherited fallback
- project-name lowercase-collision validation scans all candidate project roots, not only roots with valid `release.json`
- release target validation is language-aware: `csharp -> nuget/github:official`, `jsts -> npm/github:official`, `python -> pypi/github:official`, `ruby -> rubygems/github:official`
- RubyGems repository policy accepts only `MAJOR.MINOR.PATCH[.suffix...]` with no leading `v`, no `-` or `+`, ASCII-alphanumeric suffix segments, and at least one letter in any suffix chain
- official creates the protected release tag only after resolver, static-analysis, the language-matching build, and `require-provenance` succeed, and the tag reservation itself is gated by `production-tag-write-<project-name>` approval
- official GitHub Releases use deterministic release titles `<project-name> v<version>` so overwrite guards can detect same-version identity conflicts across tags
- artifact manifests include per-file `publishRoles` from `{package, github-release-asset}` so package outputs and GitHub Release assets can be selected independently without ambiguous top-level file rules
- `_publish-npm.yml` hard-fails if `dist-tags` is missing, empty, or not an allowed deterministic ordered tag array for the current run
- `_publish-npm.yml` derives caller ref from runtime `github.ref` rather than a caller input, and it emits the exact validated dist-tags via `applied-dist-tags`, which official `release-complete` compares to the deterministic tag array derived in `resolve-context`
- recovery-ledger incident disposition includes `abandoned-after-partial-publish` in addition to `abandoned-before-publish`
- build artifacts include a manifest of published files and SHA-256 digests; publish workflows verify that manifest before upload, the manifest file name is fixed as `artifact-manifest.json`, it is internal metadata rather than a GitHub Release asset, and it uses a fixed schema with `schemaVersion: 1` plus a non-empty `files` array of `{path, sha256, publishRoles}` objects with strict key whitelists and exact 64-character lowercase SHA-256 digests
- official build workflows with `require-provenance: true` also emit a separate provenance artifact `build-provenance-<project-name>` plus a durable identity record `artifact-evidence.json`, and `artifactManifestEvidenceUrl` in the recovery ledger points to a repository-controlled durable copy of that evidence rather than to an expiring CI artifact URL
- `artifact-evidence.json` is strict rather than open-ended and records exact attestation verification outputs including verified repository, ref, source SHA, `job_workflow_ref`, workflow SHA, repository owner identity, verifier tool, and optional verified environment
- every `artifact-manifest.json` entry path must be a flat top-level file name with no `/` or `\`, and every publish workflow rejects nested paths during manifest validation
- `_publish-github.yml` derives and uploads a public checksum asset such as `SHA256SUMS` from `artifact-manifest.json` so GitHub Release consumers can verify downloaded assets with standard tooling in addition to the attestation-based provenance gate
- NuGet build artifacts may also include matching `.snupkg` symbol packages, which should be pushed alongside the corresponding `.nupkg` when the target supports them
- build workflows must produce reproducible package outputs for the same source commit and locked toolchain so rerun idempotency remains valid
- `_publish-npm.yml` must use an explicit dist-tag on every publish, separate tarball idempotency from dist-tag idempotency, allow missing tags to be attached to the same version, and never move `latest`, prerelease channel tags, or maintenance-line tags backward
- reusable workflow JSON-array outputs such as resolved targets, npm dist-tags, applied dist-tags, and confirmed publish-state target lists use compact canonical JSON serialization rather than pretty-printed or order-unstable JSON
- GitHub Packages versions are treated as immutable within workflow execution even though GitHub supports delete/restore with elevated package-admin privileges; the workflow design does not request delete/admin permissions and does not support delete-and-republish recovery
- recovery guidance distinguishes fresh dispatch from GitHub's Re-run button and covers partial official publishes plus preflight failures
- GitHub reruns use the original workflow snapshot and do not pick up later fixes to workflow files, reusable workflows, or helper scripts
- recovery guidance tells operators to check the original run's artifacts in the GitHub Actions run UI or API before choosing rerun versus fresh dispatch
- recovery guidance distinguishes GitHub's documented 30-day workflow-rerun limit from the 90-day official artifact retention window and treats long-lived pending approvals against that rerun boundary rather than against an undocumented GitHub approval-expiry clock
- recovery guidance also distinguishes pre-publish validation/build failures from partial publish failures
- recovery guidance distinguishes official failures that happen before `resolve-context` succeeds, before `create-release-tag` succeeds, and after the immutable official release tag has already been created
- recovery guidance includes OIDC trust drift after control-plane branch or workflow-path changes
- recovery guidance distinguishes repository-side `resolve-context` publish trust inventory preflight failures from registry-side Trusted Publisher drift during publish jobs
- recovery guidance treats `release-complete` target-mapping failures as control-plane wiring drift that must be fixed in workflow code rather than retried
- recovery guidance prefers `Re-run failed jobs` on the same official workflow run for transient failures, uses `Re-run all jobs` for declined approvals and other cancellation-style approval outcomes, requires inspecting and draining stale queued runs in the same concurrency group before any fresh dispatch, and allows a fresh dispatch only when the selected protected branch still points to the same commit as the original run
- if a corrected official source commit still resolves to the same version as a burned identity, recovery must bump the version or explicitly delete the burned protected tag through the authorized bypass path before redispatch
- orphaned official tags are not silently accepted; recovery either reruns against the same commit or explicitly deletes the tag with a member of the dedicated release-engineering group configured as a `refs/tags/release/**` bypass actor before abandoning that release identity
- recovery guidance includes the case where a draft or published stable GitHub Release with deterministic title blocks a new official run and must be resolved explicitly rather than bypassed by renaming
- if official artifacts expire and the protected branch has moved, the previous partially released identity is treated as burned and recovery proceeds with the next version
- burned and partially published official identities, plus required periodic tag audits, are recorded in `.github/release-recovery-ledger.jsonl` using `recordType` values `{incident, audit}` under `CODEOWNERS` review, with a P0/P1 break-glass path for minimal emergency ledger updates followed by a reviewed cleanup PR; incident records now include `selectedTargets`, strict key whitelists, and hold-window evidence fields for destructive stable-release recovery
- incident and audit ledger records both include `schemaVersion` and `recordType`; `disposition`, `publishedTargets`, `pendingTargets`, `tagState`, `githubReleaseState`, `audit.scope`, and `audit.result` all use closed schemas or enums, `closedAt` is absent rather than null while an incident remains open, and audit records require `closedAt` for both `followUpStatus = resolved` and `followUpStatus = not-required`
- `mise.lock` is mandatory repository state, regenerated with `mise lock`, and enforced by `hk check --all`
- the checked-in publish trust inventory is drift detection and audit trail only, not an independent cryptographic backstop
- reusable publish workflows must write `publish-result` to both workflow outputs and `$GITHUB_STEP_SUMMARY`, and `release-complete` both validates selected-target `publish-result` values and aggregates them into its own summary
- official production release is machine-gated by a `require-provenance` job between build and tag reservation; `create-release-tag` and official publish jobs are ineligible until that gate succeeds
- build reusable workflows default `checkout-ref` to the caller job's `github.sha` when the input is omitted
- maintenance branch onboarding is a GitHub-side trust change in this design: create the branch, protect it, add it to `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>`, merge matching `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` updates onto every branch in the protected control-plane branch set, perform any required registry-side selector or secret update when repository identity, selector path, auth mode, or environment naming changed, then dispatch from it; branch onboarding does not require registry-side branch-specific trust edits when the repository/workflow/auth model is unchanged
- maintenance branch retirement first drains queued, waiting, action-required, requested, or in-progress official runs from that branch with separate status-specific queries, performs active polling for up to 5 minutes with at least 30-second intervals before each re-check, verifies there is no still-open incident ledger entry for that release line, quarantines unresolved ledger states for at most 7 days before escalation, caps drain retries at three consecutive reappearances before escalating into the emergency-cleanup path, and uses that emergency path to keep the branch outside `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` while cancelling newly queued or waiting runs before restarting step 0; normal retirement then removes the exact branch from all three protected environments plus `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json`, with rollback restoring the checked-in caller-ref and inventory entries before the deployment-policy entries
- `official.yml` uses three GitHub Apps in the design: a metadata reader App, a release-tag writer App scoped to protected tag creation, and a release-evidence writer App scoped only to the protected `release-evidence` branch
- `ci-passed` must re-derive which language suites were required from `detect-changes.outputs` and may not treat an unexplained skipped test job as success
- buddy and official static-analysis scope HK by passing the project path directly to `hk check`; the design no longer pre-enumerates file lists in shell
- official artifact retention is `90` days to exceed GitHub's documented 30-day workflow-rerun limit and preserve post-expiry recovery margin
- official tag rulesets use a dedicated GitHub App as the workflow automation bypass actor rather than the GitHub Actions app; emergency manual cleanup is limited to the dedicated release-engineering group
- stable GitHub Release conflict detection treats draft releases with the deterministic stable title as part of the same stable identity space rather than a separate namespace
- declined approvals and other cancellation-style approval outcomes are recovered with `Re-run all jobs`, while transient failed jobs use `Re-run failed jobs`
- operations must audit protected `release/**` tags against completed official releases at least once every 7 days and immediately after run expiry, manual orphan-tag deletion, burned-identity declaration, or an escalated long-waiting approval incident, and record that audit in `.github/release-recovery-ledger.jsonl`
- official recovery monitoring uses three control-plane monitors: a 6-hour approval-age monitor with its own external heartbeat, an event-driven post-tag failure monitor for non-success official runs after tag creation, and a 7-day operational audit with its own external heartbeat
- the periodic protected-tag audit may automatically append `audit` ledger records only when `result = clean` and `followUpStatus = not-required`; discrepancies, reconciliations, break-glass actions, and incident-state changes still require reviewed ledger updates
- any `open-partial-publish` incident that remains unresolved for 14 days escalates automatically to release-engineering owners with current live-state evidence
- the registry-withdrawal runbook and the registry-auth rollback runbook require release-engineering re-attestation at least every 90 days
- `production-evidence-write-<project-name>` is a third protected environment reserved exclusively for durable evidence persistence by `require-provenance`
- `.github/planned-change-windows.json` is a strict schema with `schemaVersion: 1` and a `windows` array; each window has `windowId`, `operation`, `status`, `projectName`, `releaseLine`, `openedAt`, `expiresAt`, `openedBy`, plus optional `linkedLedgerEntry` and `notes`; `active` windows are capped at 4 hours and `quarantine` windows at 7 days
- GitHub Actions concurrency groups are case-insensitive, so design contracts must normalize or compare them without assuming case-distinct groups are separate
- `create-release-tag` must check the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API rather than relying on a local tag list from checkout
- repository policy must include a CI validation that rejects releasable project-name collisions under ASCII lowercase normalization
- repository policy must also include a CI validation that rejects releasable project roots resolving to more than one workflow language
- buddy `resolve-context` should emit a Python-specific error when unofficial-target filtering becomes empty because Python has no buddy channel in this design
- adding a new supported language requires updating every buddy publish-job `if:` guard that maps targets to the single language-matching build result
- incident ledger records require `evidenceUrl`; `workflowRunUrl` and `runAttempt` are conditional on `attemptScope = single-run-attempt`
- `open-partial-publish` requires both `publishedTargets` and `pendingTargets` to be non-empty, `recovered` requires `pendingTargets = []`, and `abandoned-after-partial-publish` requires both sets to be non-empty
- hold-window evidence fields are required only for destructive stable-release recovery and must be absent otherwise, except that `consumerImpactEvidenceUrl` is required on both the hold and hold-waiver paths
- break-glass ledger bypass may touch only `.github/release-recovery-ledger.jsonl`, and automation alerts if the required reviewed cleanup PR is not merged by the next business day
- gate jobs such as `ci-passed` and `release-complete` obtain `jq` through the repository-managed `mise` toolchain rather than from the runner image

## Maintenance and future edits

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
