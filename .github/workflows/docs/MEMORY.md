# Workflow Design Memory

For AI agents editing workflow design docs.

## Do not reintroduce these old patterns

- blanket `secrets: inherit` for publish flows
- OIDC `job_workflow_ref` assumptions that collapse direct entry-workflow identities and reusable-workflow identities into a single `official.yml` anchor
- pre-checkout selection of `semver2` vs `pep440` validators
- silent filtering of unknown `release.json` targets
- unofficial Python registry targets unless they are explicitly designed
- buddy -> official as a required promotion chain
- buddy GitHub Releases or buddy repository traceability tags in this repository
- buddy `force=true` inputs or any unofficial overwrite path in this repository
- reusable workflows declaring their own `permissions` blocks
- documentation-only production-environment protection that is not target-specific as `production-<target>-<project-name>` plus the separate tag-write and evidence-write environments
- reusable workflow contracts that omit caller `permissions`
- action pinning rules that exempt first-party `actions/*`
- a single `publish-nuget-official` job that statically keeps `id-token: write` even when the reviewed `api-key-secret` fallback path is selected
- `confirm-publish-state` worker models that rely only on convention instead of explicitly unsetting `GITHUB_OUTPUT`, `GITHUB_ENV`, and `GITHUB_STEP_SUMMARY` for each worker
- `confirm-publish-state` fan-out designs that let worker failure short-circuit the reducer or that let parallel workers write job outputs or step-summary checkpoints directly
- `confirm-publish-state` cross-step worker joins that rely on shell `wait` or on PID-only `kill -0` polling; the authoritative completion signal must be a per-target scratch completion marker, with PID data used only for bounded cleanup after the deadline
- `CONTROL_PLANE_ENVIRONMENT_ROLE` closed sets that omit `publish-pypi-testpypi`; the dedicated buddy TestPyPI environment uses that exact role value
- `confirm-publish-state` npm dist-tag wording that implies exact equality against the version's entire registry tag set; the design's confirmation semantic is requested-tag subset inclusion in the ordered `npm-dist-tags` array
- `confirm-publish-state` checkpoint designs that imply worker-time writes to `$GITHUB_STEP_SUMMARY` or checkpoint artifacts; incremental checkpointing is allowed only inside the single reducer execution
- `confirm-publish-state` wording that promises realtime per-target checkpoint visibility in the current single-job reducer-only architecture; reducer checkpoints are batch-visible only after worker collection unless the design explicitly changes to a different multi-step/polling model
- Section 6 contract tables that use `.github/actions/collect-gate-input` in examples but omit its required inputs `jobs-json` and `include-jobs`, required output `gate-input-json`, or the compact-JSON projection constraints
- `confirm-publish-state` `github:official` verification paths that re-download the CI artifact as the digest source instead of using `artifact-evidence-url` plus Git Blobs API and remote `SHA256SUMS`
- `confirm-publish-state` npm-native-provenance checks that talk about package-access facts without naming an allowed source of truth such as npm registry metadata `access` or the packed `package.json` `publishConfig.access`
- npm-native-provenance package-access rules that omit the fallback when both reviewed sources are absent; the current design defaults unscoped packages to `public` and hard-fails scoped packages as indeterminate instead of silently treating them as private
- npm-native-provenance classification sets that include `intentionally-disabled` without an explicit operator-controlled trigger; the current design uses the validated `official.yml` input `npm-native-provenance-intent = auto | disabled` and allows `intentionally-disabled` only for otherwise eligible public-repo/public-package runs
- `confirm-publish-state` NuGet symbol-package checks that guess `.snupkg` expectation instead of deriving it from durable evidence via `artifact-evidence-url` and `artifactManifestBlobApiUrl`
- NuGet fallback readiness models that rely on a permanently open or permanently pre-approved PR per project
- static-analysis requirements that claim `hk.pkl` can hard-fail transitive worker-helper writes to workflow-command files; only direct writes are statically enforced, while transitive containment comes from `CODEOWNERS` review plus launcher-time environment sanitization
- NuGet fallback readiness models that omit the 7-day CI-validated control-plane snapshot and recorded readiness state
- emergency-cleanup dual-control rules that have no time-bounded P0/P1 escalation path when a second approver is unavailable
- buddy TestPyPI trusted-publishing jobs that request `id-token: write` without a dedicated environment-role check and selector self-check for repository/workflow/environment identity
- unquantified `require-provenance` retry budgets for evidence-branch append and evidence-anchor compare-and-swap
- leaving `rubygems:official` `providerContractStatus = audience-undocumented` without an automated freshness probe in `governance-and-runbook-freshness.yml`
- `detect-changes.infra` inventories that duplicate C#-ecosystem-local root/tooling files and therefore spuriously trigger all-language suites

## March 2026 external confirmation for the current design-fix pass

### Confirmed facts

- npm trusted-publishing docs say automatic npm-native provenance is generated only when trusted publishing is used for a public package from a public repository; private repositories are explicitly ineligible even when publishing a public package
- npm trusted-publishing docs continue to require GitHub-hosted runners and document the GitHub Actions selector as workflow filename plus optional environment
- `NuGet/login@v1` documents a required `user` input and exposes the temporary credential as the step output `NUGET_API_KEY`
- the `NuGet/login@v1` output name `NUGET_API_KEY` is the same string commonly used for a long-lived fallback secret, so output-vs-secret ambiguity is a real design hazard rather than a hypothetical one
- GitHub does not provide a platform guarantee that parallel subprocesses inside one job are isolated from workflow-command files, so worker isolation must be created explicitly by environment sanitization plus repository policy
- `pypa/gh-action-pypi-publish` is GNU/Linux-only, documents the GitHub-provided Ubuntu VM as its expected environment, and says `ubuntu-latest` is smoke-tested in CI
- `pypa/gh-action-pypi-publish` continues to document trusted publishing in reusable workflows as unsupported, composite-action invocation as unsupported, and TestPyPI publishing through `repository-url: https://test.pypi.org/legacy/`
- RubyGems public trusted-publishing docs still do not publish a fixed required audience value, so `audience-undocumented` needs an explicit freshness probe rather than indefinite trust

### Remaining assumptions

- remapping the temporary `NuGet/login@v1` output to a repository-local variable such as `NUGET_OIDC_API_KEY` is repository design guidance to avoid collisions; upstream docs require using the step output but do not mandate a specific local variable name
- npm docs describe when automatic provenance is eligible, but they do not define one single stable machine-readable confirmation surface for every post-publish state; `confirm-publish-state` still needs repository-owned logic to decide how long to wait and what to treat as sufficiently confirmed when classification is `expected`
- package-public/private classification for npm may depend on reviewed local package metadata plus registry response shape, so the exact runtime source of truth for `not-applicable-private-package` remains an implementation choice rather than a provider-documented field contract

## March 2026 external-system research

### Confirmed documented facts

- GitHub's documented rerun availability for a workflow run is 30 days from the initial run
- GitHub's public examples for build attestations require both `id-token: write` and `attestations: write`
- GitHub Actions does not document an `environments` workflow-permission key
- GitHub's Environments and deployment-branch-policy REST reads for GitHub Apps use repository `actions: read`, while authoritative Repository Rulesets reads that must include `bypass_actors` require a permission shape at least as strong as the current documented write-level ruleset access
- exact environment branch-name allowlists require the deployment-branch-policies endpoint rather than only the top-level environment document
- GitHub's documented OIDC claims include `workflow_ref` and, for reusable workflows, `job_workflow_ref`; GitHub does not document a `caller_workflow_ref` claim
- npmjs publicly documents trusted publishing for GitHub Actions with repository owner/name, workflow filename, optional environment, and the documented audience `npm:registry.npmjs.org`
- npm publicly documents the OIDC audience value `npm:registry.npmjs.org`
- npm public trusted-publishing docs now warn that `workflow_dispatch` and `workflow_call` must be chosen carefully because each package can have only one trusted publisher connection
- npm trusted publishing can auto-generate provenance for eligible publishes; `npm publish --provenance` remains a separately documented manual path
- PyPI's documented GitHub Actions trusted-publishing flow uses runtime audience discovery rather than a stable checked-in audience literal
- PyPI's documented GitHub Actions trusted-publishing guidance continues to be entry-workflow based, and its troubleshooting docs identify reusable workflows as unsupported because the trusted publisher binds to the workflow identity present in the token; composite actions do not change that provider-side identity, but the upstream `pypa/gh-action-pypi-publish` action still documents composite-action invocation as unsupported
- PyPI's documented GitHub Actions trusted-publishing flow requires `id-token: write`; environment binding is optional in provider configuration but still recommended by PyPI and required by this design on the GitHub side
- RubyGems.org publicly documents reusable-workflow support and the separate Workflow Repository Owner/Name fields used when the reusable workflow lives in a different repository
- the public provider documentation reviewed for March 2026 did not document exact branch, tag, or commit-SHA binding in the provider UI for `npmjs`, `PyPI`, or `RubyGems.org`
- GitHub reusable workflows are automatically callable from the same repository, private-repository cross-repo calling requires an explicit access policy, and GitHub visibility alone is not a same-repository allowlist
- GitHub attestation verification publicly documents repository identity, repository owner identity, ref, source SHA, signer workflow path, and workflow digest as verifiable provenance dimensions; for this design, exact source/workflow/environment checks still require parsing verified JSON output rather than relying on CLI flags alone

### Review follow-up facts for the March 2026 v2.6 fixes

- `gh attestation verify` does not expose every claim this design requires through dedicated CLI switches, so exact source-SHA and workflow-SHA enforcement must parse verified JSON output rather than rely on flags alone
- the documented `.NET` restore locked-mode syntax is `dotnet restore --locked-mode`; `-LockedMode` is not the documented form
- GitHub content addressed by blob SHA is immutable, so durable evidence should point to a Git blob permalink rather than to a mutable branch/path URL
- GNU `sha256sum` escapes filenames containing backslash, newline, or carriage return unless `--zero` is used; control characters in release filenames therefore create interoperability risk for downstream consumers that do not parse GNU escapes correctly
- npm provenance (`npm publish --provenance`) is publicly documented but remains supplemental evidence in this design, not a replacement for the GitHub attestation gate
- GitHub-hosted Linux workflow `run:` steps default to `bash -e` when `shell:` is omitted; explicit `shell: bash` adds `--noprofile --norc -eo pipefail`
- `git ls-remote --tags` emits the annotated tag object's SHA at `refs/tags/<tag>` and the peeled target object's SHA at `refs/tags/<tag>^{}`; annotated-tag commit comparisons must use the peeled entry or the GitHub refs API
- PyPI deletion is permanent: deleted files cannot be re-uploaded later, so deleting a PyPI file or release burns that exact filename/version identity; yanking is the non-destructive alternative when exact-version installs must remain possible
- GitHub Environment deployment branch policies are evaluated against the workflow `GITHUB_REF` and are independent from repository branch rulesets; a non-environment branch such as `refs/heads/release-evidence` therefore requires its own exact-ref ruleset check rather than relying on environment-derived branch enumeration
- GitHub App installation `contents: write` is repository-scoped rather than ref-scoped, so ref-level write boundaries in this design come from rulesets and environment gates, not from narrower App token scoping
- for attestations generated by reusable workflows, `gh attestation verify` treats the reusable workflow as the signer to validate, and `statement.predicate` is attacker-controlled input rather than a trustworthy source of verified identity

### Current design-remediation external facts

- in private GitHub repositories, fork-originated `pull_request` workflow runs do not receive repository or organization secrets, so secret-dependent control-plane checks cannot rely on that path
- GitHub does not publicly document a native workflow-file-level actor allowlist for `workflow_dispatch`; access control must therefore come from branch protection, repository permissions, environments, and workflow logic rather than a per-workflow caller allowlist feature
- npmjs public setup docs are entry-workflow centric and now explicitly warn that `workflow_dispatch` and `workflow_call` must be configured carefully because each package can have only one trusted publisher connection
- PyPI trusted publishing uses endpoint-based audience discovery; provider-side docs still treat reusable workflows as unsupported, while the upstream `pypa/gh-action-pypi-publish` action separately documents composite-action invocation as unsupported
- the attestation verifier's trusted identity comes from verifier-checked outputs, not from `statement.predicate`; predicate content remains untrusted input even when the DSSE envelope itself verifies

### March 2026 external-system follow-up facts for v2.7 remediation

- GitHub Environments required reviewers are a one-of-N gate: when multiple required reviewers are configured, only one approval is needed for the job to proceed
- GitHub Environments expose an explicit `Allow administrators to bypass configured protection rules` setting; designs that require non-bypassable approvals must require that setting to be disabled
- the GitHub repository rulesets REST response returns `bypass_actors` only when the caller has write access to the ruleset, so a read-only metadata App is insufficient for authoritative bypass-actor verification
- GitHub does not publicly document native time-limited team membership or native JIT expiry for team membership; any <=2 hour emergency membership guarantee must come from external identity automation or a separate revoker automation layer
- GitHub does not provide a general-purpose API endpoint that restores an arbitrary deleted branch; recovery depends on recreating the ref from a known commit SHA or on limited UI restore paths tied to closed pull requests
- npm unpublish policy remains time-windowed and conditional: versions younger than 72 hours are handled differently from older versions, all-version package removal creates a 24-hour republish block, and an unpublished `name@version` tuple cannot be reused
- NuGet unlisting is semantically distinct from both deprecation and removal: it hides a package from normal search while still allowing exact-version installs and does not itself surface a deprecation warning
- GitHub artifact attestations are stored as Sigstore bundles that embed run-specific provenance metadata, and later reruns are not expected to reproduce byte-identical attestation bundles even for identical artifacts

### March 2026 external-system confirmation for the current DESIGN cleanup

#### Confirmed facts

- GitHub Environments required reviewers remain a one-of-N approval gate, jobs show `waiting` while environment protection rules are still blocking execution, and administrator bypass can be disabled explicitly in environment settings
- three sequential environment-gated jobs with independent 7-day wait timers would create an approximately 21-day minimum elapsed path for a single official release, because the waits accumulate across the dependency chain
- GitHub's REST API treats `action_required` as a workflow-run status value that can be queried directly; it is not a step or job conclusion value
- GitHub web URLs do not support immutable `/blob/<blob-sha>/path` navigation; immutable blob reads must use the Git blobs API, while human-facing web links must be anchored by a commit SHA rather than a blob SHA
- GitHub pull request `mergeable` is asynchronous and may be `null` until GitHub finishes computing mergeability; readiness checks must poll for a non-null value rather than treating `null` as mergeable
- npm unpublish remains time-windowed and destructive: an unpublished `name@version` tuple cannot be reused later, and removing all versions of a package name imposes a 24-hour block before any new version of that package name may be published again
- npm public trusted-publishing docs state that each package can have only one trusted publisher connection, and the documented GitHub Actions audience remains `npm:registry.npmjs.org`
- PyPI yanking is the non-destructive withdrawal path, while deletion is permanent and burns the deleted filename/version identity so the same file cannot be re-uploaded later
- PyPI's trusted-publishing client discovers its audience from the upload endpoint, and the reviewed docs still split the limitations between provider-side reusable-workflow unsupported behavior and upstream-action composite-action unsupported behavior
- RubyGems public documentation confirms `gem yank` removal semantics but does not publicly document same-version republish as a supported recovery path after yank
- NuGet symbol publication depends on the corresponding `.nupkg` already existing, and a missing primary package causes symbol upload to fail rather than silently creating partial state
- RubyGems.org public trusted-publishing documentation still does not publish a required audience value

#### Remaining assumptions

- although GitHub run status exposes `waiting`, provider APIs do not publicly guarantee a distinct machine-readable reason that always separates wait-timer delay from reviewer-pending delay for every environment-gated job state transition
- RubyGems may or may not permit same-version republish after `gem yank` in some operational edge cases, but because that behavior is not a documented contract, this design should continue to treat it as unsupported
- GitHub's documented artifact-retention behavior is clear, but a separate universally documented artifact-based recovery contract beyond the 30-day rerun limit was not found in public docs; design recovery should therefore rely on durable repository-controlled evidence rather than on artifact reavailability assumptions

### March 2026 external-system follow-up assumptions for v2.7 remediation

- if emergency-cleanup membership expiry is implemented through an identity provider rather than GitHub-native automation, the exact TTL guarantee depends on that external identity system and is outside GitHub's own documented contract
- branch-deletion recovery remains dependent on retaining or reconstructing the target commit SHA; GitHub does not document a universal deleted-branch restore retention window for all cases
- GitHub does not document a read-only ruleset permission that still exposes `bypass_actors`, so this design must treat any future narrower permission model as an external improvement rather than a current guarantee

### Review follow-up assumptions that remain assumptions

- whether attestation verification surfaces environment name for every relevant build case remains conditional; the design must treat `verifiedEnvironment` as optional and only require it when the verifier actually surfaces that claim
- this design does not rely on a documented PyPI-native provenance contract beyond the GitHub attestation gate
- if a future PyPI design switches from entry-workflow trust to reusable-workflow trust, that selector change still requires disposable-publisher validation because PyPI's public docs do not yet describe reusable-workflow-path handling as a stable contract

### Current design-remediation assumptions

- because GitHub lacks a documented `workflow_dispatch` actor allowlist, this design uses a reviewed checked-in dispatcher allowlist on `main` plus protected-branch selection and later environment approval as the authoritative guardrails for manual official dispatches
- because private-repo fork PR runs cannot use the metadata App secret path safely, external contributions that need secret-backed control-plane validation must be mirrored onto same-repository branches before this design's CI path is used
- because npmjs public docs document only one trusted publisher connection per package, PyPI provider docs still treat reusable workflows as unsupported, and the upstream PyPI publish action still treats composite-action wrapping as unsupported, this design keeps those official trusted-publisher-backed publish steps in direct entry-workflow jobs rather than treating reusable-workflow behavior as a guaranteed external contract

### Independent external confirmation for the current DESIGN.v2 remediation

#### Confirmed facts

- same-repository reusable workflow references using `./.github/workflows/<file>.yml` resolve from the same commit as the caller workflow run
- workflow reruns keep the original `GITHUB_SHA` and `GITHUB_REF` from the first attempt, so this design must not rely on mutable-ref re-resolution during rerun recovery
- GitHub does not publicly document a native workflow-file-level actor allowlist for `workflow_dispatch`
- GitHub Actions concurrency groups are case-insensitive, allow at most one running plus one pending run per group, and replace any older pending run with the newest queued run for that same group
- each GitHub Environment has its own deployment branch policy; removing or adding a branch on one environment does not implicitly change any other environment
- a job that references an environment must satisfy that environment's protection rules before it runs or accesses environment secrets, so a deployment-branch-policy mismatch blocks the job before its steps execute
- environment approvals are tracked per environment-gated job, so one target-specific publish job can already have been approved and completed while another job in the same workflow run is still waiting on a different environment
- GitHub does not document any generic environment concept that implicitly covers multiple distinct named environments; each environment name is configured independently
- public `NuGet.org` documentation now documents GitHub Actions trusted publishing through `NuGet/login@v1`; the action documents a default audience of `https://www.nuget.org` and emits a short-lived `NUGET_API_KEY`
- NuGet.org repository-signs uploaded packages, so downloaded `.nupkg` bytes are not expected to match the originally uploaded unsigned `.nupkg` bytes
- npm publicly documents the trusted-publishing OIDC audience `npm:registry.npmjs.org`
- npm public trusted-publishing docs warn that `workflow_dispatch` and `workflow_call` must be configured carefully because each package can have only one trusted publisher connection
- `npm publish` accepts either a package directory or a tarball path, and npm publicly documents `--ignore-scripts` as the supported way to suppress package lifecycle scripts during publish
- npm does not mutate uploaded package tarball bytes after publish, and an unpublished `name@version` tuple cannot be reused later
- Twine uploads pre-built distribution files and does not run `setup.py` as part of upload; `twine check --strict` is the documented local validation step before upload
- PyPI trusted publishing obtains the OIDC audience from `https://{repository_domain}/_/oidc/audience` rather than from a stable checked-in literal
- PyPI trusted publishing still has two separate reviewed limitations: provider-side docs treat reusable workflows as unsupported, while `pypa/gh-action-pypi-publish` documents composite-action wrapping as unsupported; `skip-existing` remains opt-in rather than default behavior
- the PyPI JSON API at `/pypi/<project>/<version>/json` exposes the released file list and is sufficient to verify that both the expected wheel and source distribution are present before `confirm-publish-state` closes
- no publicly documented non-mutating PyPI server-side preflight upload endpoint was found in the reviewed docs, so design preflight should stay local and repository-controlled
- RubyGems.org publicly documents trusted publishing with reusable-workflow support, and `gem push` uploads a pre-built `.gem` rather than executing package build scripts during upload
- RubyGems.org public documentation reviewed here still does not publish a required OIDC audience value or document same-version republish after `gem yank` as a supported recovery contract

#### Remaining assumptions

- GitHub's public docs do not give a precise enough caller-job-plus-reusable-workflow environment contract for this design to depend on, so official environment-gated jobs stay as direct jobs in `official.yml`
- npmjs and PyPI public trusted-publishing setup docs remain entry-workflow-centric, so this design keeps official trusted-publisher-backed jobs in `official.yml` until provider docs describe reusable-workflow selectors as a stable contract
- GitHub's public docs state that environment protection is enforced before job execution, but do not document a more precise internal evaluation moment than that
- GitHub's public docs do not expose a single machine-readable approval-state primitive that by itself distinguishes pre-publish waiting from partial-publish waiting, so the design must classify `open-before-publish` versus `open-partial-publish` from job and remote-state evidence rather than from wait state alone
- exact unauthenticated or default-`GITHUB_TOKEN` read coverage for every public-repository control-plane endpoint in scope is still incomplete, so this design continues to require dedicated audit Apps instead of relying on opportunistic public access

## March 2026 external confirmation for current remediation

### Confirmed facts

- GitHub documents the repository Actions settings needed for fork-PR approval and pull-request write-token policy behind Administration-read APIs, so untrusted public `pull_request` runs cannot safely re-verify those live settings without an additional trusted credential
- `workflow_run` follow-up workflows do not receive upstream job outputs directly; the post-tag monitor should therefore use the immutable uploaded `tag-reservation-result-<project-name>` artifact as its reviewed cross-run handoff rather than relying on upstream outputs
- job-level `secrets:` is documented for reusable-workflow call jobs, not for ordinary jobs with `steps`, so direct official publish jobs should rely on explicit job permissions, OIDC, and environment-scoped credentials instead of a job-level `secrets:` map

### Remaining assumptions

- GitHub may expand read-only repository-settings coverage in the future, but until that is publicly documented and verified the design should keep repository Actions settings checks in trusted control-plane monitoring rather than in untrusted PR validation

## March 2026 external confirmation for the DESIGN.v2 review-fix pass

### Confirmed facts

- PyPI's trusted-publisher documentation names reusable workflows as unsupported, but does not say the same about composite actions; the provider-facing limitation is specifically about workflow identity in the OIDC token
- the upstream `pypa/gh-action-pypi-publish` action separately documents composite-action invocation as unsupported for that action's supported/safe usage model
- the upstream `pypa/gh-action-pypi-publish` action documents `skip-existing` as opt-in and disabled by default
- the upstream `pypa/gh-action-pypi-publish` action documents the GitHub-provided Ubuntu VM as the expected/supported environment, while self-hosted runners are only best-effort for that action
- PyPI's trusted-publishing docs expose the audience through the `/_/oidc/audience` endpoint and keep recommending the direct `pypa/gh-action-pypi-publish` path on `ubuntu-latest`
- npm trusted publishing currently supports GitHub Actions only on GitHub-hosted runners; npm docs explicitly say self-hosted runners are not currently supported
- npm trusted publishing still requires Node `>= 22.14.0` and npm CLI `>= 11.5.1`
- NuGet trusted-publishing docs describe GitHub Actions setup with workflow file name, optional environment binding, and `NuGet/login@v1` using a required `user` input to mint a temporary API key
- NuGet trusted-publishing docs say the temporary API key is valid for 1 hour and that one OIDC token exchange yields one temporary API key

### Remaining assumptions

- using `skip-existing: true` together with a `packages-dir` that contains only the missing PyPI files is a repository design for same-identity partial recovery built from upstream action capabilities; PyPI docs do not present it as a first-class named recovery contract
- extending the GitHub-hosted-only requirement from official publish jobs to the entire official build pipeline is a repository supply-chain policy choice informed by npm's hosted-runner restriction and PyPI action support boundaries, not a provider-side claim that every non-publish build job is independently rejected by all registries

## March 2026 external confirmation for DESIGN.v2 remediation rewrite

### Confirmed facts

- GitHub documents a separate 30-day gate approval time for environment approvals, distinct from the 30-day workflow-rerun limit even though they currently share the same numeric value
- GitHub Actions still uses repository-scoped `contents: write` as the minimum permission for GitHub Release mutation; there is no narrower release-only permission namespace
- GitHub documents `github.workflow_ref` for the executing workflow, but does not document a trusted reusable-workflow runtime context that reveals the caller workflow path, and no documented `caller_workflow_ref` exists
- the reviewed `pypa/gh-action-pypi-publish` trusted-publishing flow documents audience discovery through `https://{repository_domain}/_/oidc/audience`; PyPI's user-facing docs do not present that URL shape as a stable operator contract
- PyPI deletion is permanent in the reviewed provider docs: deleting an uploaded file prevents re-upload of that exact filename, deleting an entire release burns that version slot, and yanking remains the non-destructive alternative
- GitHub Rulesets API visibility for `bypass_actors` remains permission-gated; reviewed results confirmed that the field is visible only to callers with write access to the ruleset

### Remaining assumptions

- NuGet.org's public docs reviewed for this rewrite do not publish a stable `.snupkg` availability or indexing SLA, so any confirmation timeout for symbol-package presence remains an implementation assumption rather than an externally confirmed provider contract
- because GitHub does not expose a documented trusted runtime caller-workflow-path context inside reusable workflows, any caller-supplied `caller-workflow-path` input is only a reviewed wiring guard and not an independent authorization boundary
- environment-scoped approvals such as `production-github-<project-name>` isolate review and operator flow for GitHub Release mutation, but the underlying App token permission remains repository-scoped `contents: write`

## March 2026 independent external confirmation for post-review fixes

### Confirmed facts

- GitHub documents that referencing an environment name that does not already exist will create that environment automatically, and the new environment has no protection rules or secrets unless a Pages-specific implicit source-branch rule applies
- GitHub's manual `workflow_dispatch` UI documentation describes selecting a branch, while the REST API for `workflow_dispatch` explicitly accepts either a branch name or a tag name in `ref`
- GitHub documents that environment deployment branch or tag rules are matched against the workflow run `GITHUB_REF`, and environment protection is evaluated while the job is pending before execution; this is separate from repository rulesets
- GitHub documents that the repository rulesets REST API returns `bypass_actors` only when the caller has write access to the ruleset; no documented read-only permission exposes that field

### Remaining assumptions

- GitHub's public UI documentation for manual `workflow_dispatch` does not explicitly say whether tags are selectable in the branch dropdown, so tag selection through the web UI remains undocumented even though the REST API accepts tag refs
- GitHub publicly documents that environment deployment branch/tag policy matches `GITHUB_REF` while the job is pending, but does not publish a more precise ordering guarantee relative to every other environment protection rule

## March 2026 independent external confirmation for DESIGN.v2 post-review remediation

### Confirmed facts

- PyPI's troubleshooting docs say reusable workflows cannot currently be used as the workflow in a trusted publisher because the trusted publisher binds to the workflow identity present in the OIDC token
- the reviewed `pypa/gh-action-pypi-publish` docs still say trusted publishing is unsupported when that action is invoked from a reusable workflow or a composite action, and the action discovers its audience from `https://{repository_domain}/_/oidc/audience`
- npm's trusted-publishing docs now make Node `>= 22.14.0` and npm CLI `>= 11.5.1` explicit prerequisites for the GitHub Actions trusted-publishing flow
- Microsoft Learn and NuGet public docs now document GitHub Actions trusted publishing through `NuGet/login@v1` or a reviewed successor, using OIDC to mint a short-lived API key; one token exchange yields one temporary API key and the key currently expires after one hour
- the reviewed public `NuGet/login` repository is comparatively young upstream infrastructure, with its `v1` release line starting in August 2025 and only limited visible adoption and maintainer surface compared with more established first-party registry actions

### Remaining assumptions

- keeping a reviewed, mutually exclusive `authMechanism = api-key-secret` fallback for `nuget:official` is a repository design decision driven by the current maturity and rollback-risk assessment of `NuGet/login@v1`, not a provider-mandated contract
- when CI or `official.yml` uses two checkouts in one job, GitHub does not provide a second trust boundary between those directories, so any non-frozen checkout used alongside the trusted control-plane snapshot must stay data-only and no executed validator/helper code may be sourced from it
- because npm and PyPI trusted-publishing setup docs remain entry-workflow centric, the design keeps the actual trusted-publisher-backed official publish step direct in `official.yml` even when local composite actions or scripts are used around it for preparation or post-processing

## March 2026 independent external confirmation for the post-review fix pass

### Confirmed facts

- GitHub Actions `permissions` are declared statically at workflow/job definition time; reviewed docs do not describe any runtime mechanism that changes a job permission set after YAML evaluation
- GitHub's documented OIDC claims include both `workflow_ref` and `job_workflow_ref`; the latter is specific to reusable-workflow identity rather than a generic caller-workflow abstraction
- npm trusted-publishing docs say that when `workflow_call` is used npm validates the calling workflow name, while still requiring the documented audience `npm:registry.npmjs.org` and the reviewed Node/npm minimum versions
- PyPI trusted-publishing docs still treat reusable workflows as unsupported, and the reviewed `pypa/gh-action-pypi-publish` docs continue to describe audience discovery through `https://{repository_domain}/_/oidc/audience` rather than through a caller-supplied fixed audience input
- RubyGems trusted-publishing docs reviewed here still do not publish a fixed required OIDC audience value
- `NuGet/login@v1` documents a required `user` input, a default audience of `https://www.nuget.org`, and issuance of a short-lived `NUGET_API_KEY`

### Remaining assumptions

- mapping npm's documented “calling workflow name” behavior precisely onto GitHub's `workflow_ref` claim is still a design inference built from npm's wording plus GitHub's OIDC claim model, because npm's docs do not name the JWT claim explicitly
- same-project cross-ref run detection via workflow-run enumeration is only an advisory admission lock, because GitHub does not document an atomic repository-wide run reservation primitive for this case
- the `CONTROL_PLANE_ENVIRONMENT_ROLE` variable remains a fail-closed misconfiguration guard rather than a complete live-environment integrity proof, because GitHub does not document any stronger environment-side self-attestation primitive that the workflow can verify in-job

## Current assumptions

- before implementation starts, design reviews should ignore mismatches between the current repo implementation and the target design unless the task explicitly asks to reconcile implementation
- build jobs and reusable publish workflows must be called with `secrets: {}` and never with a non-empty `secrets:` map; direct official publish jobs are ordinary jobs and instead rely on explicit job permissions, OIDC, and environment-scoped credentials
- all actions, including `actions/*`, are pinned to full commit SHA, `docker://` references are pinned to immutable digests, and `hk` runs both `actionlint` and `zizmor --strict`
- dependency-update automation must cover `.github/workflows/**` so pinned action SHAs are refreshed intentionally rather than drifting indefinitely
- official external auth defaults to trusted publishing for all four production registries, but `nuget:official` also carries one reviewed, mutually exclusive `api-key-secret` fallback recorded in `.github/publish-trust-inventory.json`; GitHub-side branch eligibility remains enforced through deployment-branch policy and checked-in inventory
- official releases are `workflow_dispatch` runs from protected control-plane branches, the workflow derives and creates the official release tag internally from the resolved project version, and non-branch dispatch refs are unsupported even if the REST API can accept them generically
- the official protected control-plane branch set is `main` plus eligible protected maintenance branches `release/<project-name>/v<series>`, where `<series>` is numeric like `1.2.x` without a leading `v`
- official publish jobs and release payload source come from the dispatch-selected protected control-plane branch, but CI `trusted-release-inventory` and `official.yml` `preflight-check` also use a separate frozen `main` control-plane checkout; whenever two checkouts are present, only the frozen control-plane checkout may supply executed helper or validator code and the other checkout remains data-only
- reusable workflow shell steps must map `inputs.*` through `env:` before use
- shell hardening also applies to `${{ github.* }}`, `${{ needs.*.outputs.* }}`, and `${{ env.* }}` values derived from untrusted contexts in `run:` steps; untrusted `${{ ... }}` expansions should not appear in shell source and must be mapped through `env:` first, trusted Bash steps declare `shell: bash` explicitly, and the design also bans unsafe writes to `GITHUB_ENV`/`GITHUB_OUTPUT`/`GITHUB_PATH`/`GITHUB_STEP_SUMMARY`, `eval`, shell tracing around credential handling, and other dynamic shell execution with untrusted data
- local composite actions under `.github/actions/**` follow the same shell-hardening rule, and values received through `with:` inputs must also be remapped through `env:` before shell use
- `ci.yml` on `pull_request` must require repository-level approval for forked PR workflow runs because local workflows, actions, and helper scripts execute from the PR merge commit; in private repositories this design does not rely on fork PR execution at all for secret-backed control-plane checks and instead requires same-repository mirror branches for external contributions
- live repository Actions settings for fork-PR approval and pull-request write-token policy are audited by trusted control-plane monitoring rather than by an untrusted `pull_request` job
- `release.json` is strict: valid JSON, non-empty, unique targets, unknown targets fail
- `release.json` has `schemaVersion: 1` and allows only `schemaVersion` plus `targets`
- `release.json` top-level validation is strict and equivalent to `additionalProperties: false`; unknown top-level keys are hard failures
- unsupported future `schemaVersion` values are hard failures; schema upgrades are coordinated and do not need backward-compatibility shims before implementation starts
- official release identity tag format is `release/<project-name>/v<version>`
- `project-name` is a canonical ASCII-lowercase release identity, must resolve to exactly one releasable project, and must reject any occurrence of `..`, any trailing `.`, and any `.lock` suffix for ref safety
- project resolution is by exact canonical lowercase leaf-directory-name match from the repository root with no substring matching or heuristic tie-breakers
- project resolution must emit exactly one workflow language in `{csharp, python, jsts, ruby}`; no match, ambiguous match, or unsupported language is a hard failure
- each `buddy.yml` / `official.yml` run releases exactly one project
- buddy intentionally allows releases from development branches
- buddy uses the workflow definitions from the selected dispatch branch and currently publishes only to unofficial package registries `{nuget:gpr, npm:gpr, pypi:testpypi, rubygems:gpr}`; Python's pre-production channel is `pypi:testpypi`
- official releases must come from protected `main` or protected maintenance branches `release/<project-name>/v<series>`
- maintenance branches are explicitly managed supported lines; missing non-default lines fail with operator guidance, and non-`main` release lines require a separate maintenance-branch existence check plus exact caller-ref matching before official release is allowed
- omission of a required reusable-workflow input is a hard validation failure; the design may rely on that failure mode, but should not claim a more precise runner-allocation timing guarantee unless GitHub documents it explicitly
- official release-line validation derives `release/<project-name>/v<series>` by stripping suffix material, reading at most the first two numeric components, zero-padding a missing minor component to `0`, and rendering `<major>.<minor>.x`
- official release-line validation derives the release line from the selected source ref, requires the current caller ref to be listed in the frozen caller-ref registry from `main`, requires any non-`main` caller ref to be the exact matching protected maintenance branch `refs/heads/release/<project-name>/v<release-line>`, and requires `refs/heads/main` releases to match the project's checked-in `currentMainReleaseLine` in `.github/publish-trust-inventory.json`
- official release tags under `refs/tags/release/**` must be protected
- tag protection must cover both tag creation and tag updates; legacy protection that only blocks deletion or force-push is insufficient for `refs/tags/release/**`
- Ruby uses the repository's `validate_rubygems_version.py` subset policy rather than generic RubyGems version compatibility
- `github:official` is the official GitHub Release target, and the resulting release is prerelease or stable according to the resolved version
- same-tag stable GitHub Release is idempotent, not a hard fail
- an official run may replace an existing same-tag GitHub pre-release with a stable GitHub Release after remote asset identity checks succeed
- `official.yml` includes a `preflight-check` job inside the project-scoped `control-plane-monitoring-<project-name>` environment, and that job derives and validates that project-scoped monitoring environment plus the required target-specific publish environments and the dedicated tag-write and evidence-write environments before any production approval is consumed; each protected environment also carries a required `CONTROL_PLANE_ENVIRONMENT_ROLE` variable so downstream jobs fail closed if GitHub auto-creates a missing environment without protection
- `official.yml` also runs `static-analysis` symmetrically with `buddy.yml`
- repository protection uses GitHub repository rulesets only for protected branches and protected tags; legacy branch-protection compatibility is out of scope before implementation starts
- `detect-changes` in `ci.yml` requires `pull-requests: read`
- `preflight-check` uses `permissions: { contents: read }` on the job because it performs repository checkout before any privileged environment reads, still performs a frozen `main` checkout plus a separate dispatch-ref data checkout, and mints dedicated just-in-time audit App installation tokens from secrets stored only in the matching `control-plane-monitoring-<project-name>` environment
- `preflight-check` also validates that `control-plane-monitoring-<project-name>` exposes the exact protected caller-ref subset frozen from `main` for the selected project, so onboarding or retirement mismatch holds only that project's official release path
- the environment-reader App requires `actions: read` plus the minimum documented read scope GitHub requires for environment, deployment-policy, and repository Actions settings reads; the separate ruleset-auditor App needs the minimum GitHub App permission set that exposes `bypass_actors`, and this design does not rely on job-level `GITHUB_TOKEN` environment or ruleset reads
- while the ruleset-auditor token is live in `preflight-check`, `.release-src/**` stays data-only and may be read but not executed; the allowlist during that window is GitHub API reads, read-only inspection of `.release-src/**` and `.ctrl-main/.github/**`, execution of reviewed `.ctrl-main/eng/scripts/**` normalization/validation helpers, local `jq`/Python processing over already checked-out data, and token masking/revocation helpers
- missing GitHub Environments auto-create without protection if first referenced by workflow YAML, so `preflight-check` must treat environment existence and required-reviewer policy as explicit invariants rather than assuming absent environments fail closed
- GitHub App installation tokens minted at runtime are masked before first use, and App private keys rotate at least every 90 days and immediately on suspected compromise
- `preflight-check` must hard-fail on GitHub API errors outside explicitly handled cases
- `preflight-check` must set an explicit client timeout on every GitHub API call so a hung response fails fast rather than consuming the whole job timeout
- `preflight-check` must specifically require a `required_reviewers` protection rule, `prevent_self_review = true`, an exact-name deployment branch policy restricted to the official protected control-plane branch set, reject wildcard deployment-branch patterns, query the Repository Rulesets API only, verify that allowed maintenance branches carry the same ruleset profile as `main`, verify branch-ruleset bypass actors are limited to the dedicated release-engineering emergency-cleanup group, and verify an active tag ruleset for `refs/tags/release/**`
- before the first production run for any project, repository bootstrap must create the protected `release-evidence` branch, create the project-scoped protected anchor tag `refs/tags/control-plane/release-evidence-head/<project-name>` for that project, and configure the exact-ref or prefix rulesets that the official `preflight-check` later verifies for durable evidence writes
- workflows covered by this design reject `pull_request_target` and `secrets: inherit` through repository-policy linting; `workflow_run` is allowed only for the exact reviewed monitor workflows `.github/workflows/official-run-health-monitor.yml` and `.github/workflows/control-plane-post-tag-failure.yml`, and remains prohibited elsewhere except for the reviewed post-tag monitor's data-only download of `tag-reservation-result-<project-name>`
- emergency-cleanup group size and reviewer-overlap constraints are governance requirements, not checks that `preflight-check` can machine-enforce with the metadata App
- `preflight-check` verifies both the protection profile and the exact completeness of the live production deployment branch set against `main`'s authoritative caller-ref registry for the selected project; `resolve-context` separately verifies that the checked-in publish trust inventory and checked-in caller-ref registry agree with each other
- `preflight-check` is an audit-before-use guard and still has a residual TOCTOU window if environment or tag-ruleset protection changes after the check passes; later environment evaluation and live tag ruleset evaluation remain authoritative
- reusable workflows must not declare `permissions:` blocks
- build reusable workflows require caller `contents: read`
- build reusable workflows perform internal `fetch-depth: 0` checkout with `persist-credentials: false` and accept `checkout-ref` so callers can pin the exact workflow commit when they want to be explicit
- reusable publish workflows take a required `caller-workflow-path` input and may use it only as a reviewed wiring guard; same-repo authorization still comes from CODEOWNERS, protected refs, and environment gates because GitHub does not expose a documented trusted runtime caller-workflow-path context in reusable workflows
- build reusable workflows take a required `build-scope` input with values `ci` or `release`; `ci.yml` is the only `build-scope: ci` caller, must omit `project-path` and `project-name`, must run the language-wide CI suite, must keep provenance disabled, and must not upload release artifacts, while `buddy.yml` and `official.yml` call the same workflows in `build-scope: release` mode and must provide both `project-path` and `project-name` for project-scoped packaging
- buddy publish jobs must depend on `static-analysis` directly
- buddy publish jobs also gate explicitly on `resolve-context.result == 'success'` and `static-analysis.result == 'success'`
- buddy publish jobs also gate explicitly on the single language-matching build job succeeding while the three non-matching build jobs are skipped
- `buddy.yml` and `official.yml` end with a `release-complete` gate that first asserts resolver/static-analysis success, validates the selected target set against the actual publish-job results, requires non-selected publish jobs to be skipped, and verifies the single language-matching build result
- official publish jobs should gate explicitly on `resolve-context.result == 'success'`, `static-analysis.result == 'success'`, and `create-release-tag.result == 'success'`, using `fromJson(... || '[]')` style defaults for target-array guards; `nuget:official` is modeled as two mutually exclusive direct jobs (`publish-nuget-official-trusted-publisher` with `id-token: write`, and `publish-nuget-official-api-key` with `contents: read` only), and official `release-complete` also validates `require-provenance.result == 'success'`, the language-matching attestation job success pattern, `create-release-tag.outputs.tag-result in {created, no-op}`, and the npm `applied-dist-tags` output when npm is selected
- `.github/CODEOWNERS`, `.github/workflows/**`, `.github/actions/**`, `.github/official-caller-refs.json`, `.github/official-dispatch-authorizers.json`, `.github/publish-trust-inventory.json`, `.github/provenance-signer-map.json`, `.github/release-recovery-ledger.jsonl`, `eng/scripts/**`, `**/release.json`, `**/version.json`, `hk.pkl`, `PklProject`, `PklProject.deps.json`, `mise.toml`, `mise.lock`, `global.json`, `nuget.config`, `**/NuGet.Config`, `Directory.*.props`, `**/*.targets`, `package.json`, `pyproject.toml`, `biome.jsonc`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `.npmrc`, `**/.npmrc`, `uv.lock`, `Gemfile.lock`, `Directory.Packages.props`, and other trusted control-plane helper or shared dependency-control files must be protected by `CODEOWNERS` review, and protected control-plane branches must require code-owner review via rulesets
- only trust-bearing cross-language control-plane files belong in `detect-changes.infra`; C#-ecosystem-local files such as `global.json`, `Directory.*.props`, `Directory.Packages.props`, `nuget.config`, `**/NuGet.Config`, `**/*.targets`, and `**/packages.lock.json` stay exclusively in the `csharp` filter even when they live at repository root
- target-specific publish environments `production-nuget-<project-name>`, `production-npm-<project-name>`, `production-pypi-<project-name>`, `production-rubygems-<project-name>`, and `production-github-<project-name>`, plus the dedicated `production-tag-write-<project-name>` and `production-evidence-write-<project-name>` environments, replace the old single `environment: production` model, and each such environment's deployment branch policy allows only the official protected control-plane branch set for that project and only as exact branch names, never wildcard patterns
- each `control-plane-monitoring-<project-name>` environment has its own independent deployment branch policy equal to that project's exact protected control-plane branch set from frozen `main`, while `control-plane-governance-monitoring` is limited to `main`; these environments must be updated and drift-checked alongside the affected publish/tag/evidence environments whenever the relevant branch set changes; no environment in this design may use deployment tag policies
- official registry auth uses trusted publishing for `npmjs`, `PyPI`, and `RubyGems.org`; `nuget:official` prefers trusted publishing but may temporarily use the reviewed checked-in `api-key-secret` fallback, so provider capability differences now affect selector details, runtime prerequisites, and whether the NuGet fallback path is active
- approval-age monitoring must classify `open-before-publish` versus `open-partial-publish` from live per-target publish state, because target-specific publish environments approve and unblock jobs independently
- no portable wildcard future-branch trust is assumed; branch-set changes are therefore managed as bounded control-plane transitions through exact deployment-branch-policy entries plus reviewed `main` updates to `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json`, while only repository-identity changes, auth-mode changes, selector-workflow changes, fixed-audience changes, or audience-discovery-endpoint changes require registry-side auth updates
- the authoritative repository-side source of active official caller refs is `.github/official-caller-refs.json` on `refs/heads/main`; official runs from maintenance branches freeze and consult `main`'s copy, and each inventory `allowedCallerRefs` entry must mirror the project-scoped subset derived from that file as `refs/heads/main` plus only the maintenance refs that match the current project name
- the publish trust inventory has `schemaVersion: 3`, records `entryWorkflowPath`, project-scoped `currentMainReleaseLine` and `allowedCallerRefs`, and per-target `publishExecutionPath`, `environment`, `authMechanism`, optional `trustedPublisherSelector`, optional `documentedOidcAudience`, and optional `oidcAudienceEndpoint` fields for official targets; `authMechanism` is closed to `{trusted-publisher, api-key-secret, github-token}`, with `api-key-secret` legal only for `nuget:official`; buddy targets are intentionally excluded because they do not rely on external registry-side trust state
- publish trust inventory validation is strict and equivalent to `additionalProperties: false` at the top level
- official `resolve-context` performs a publish trust inventory preflight against the checked-in inventory frozen from `refs/heads/main` after official target resolution and before any publish job becomes eligible
- CI includes an explicit `trusted-release-inventory` job that checks out the PR merge commit as candidate data and a separate frozen `main` snapshot as the trusted validator source, executes validators only from the frozen `main` checkout, and validates `entryWorkflowPath`, `currentMainReleaseLine`, the project-scoped `allowedCallerRefs` subset derived from the candidate `.github/official-caller-refs.json`, and the per-target `publishExecutionPath`, `environment`, `authMechanism`, optional `trustedPublisherSelector`, optional `documentedOidcAudience`, and optional `oidcAudienceEndpoint` fields against the candidate `.github/publish-trust-inventory.json`; CI fails on any mismatch whether or not the inventory file itself changed
- registry-side OIDC trust settings are not queried portably; release operators must verify registry-side trust separately when diagnosing control-plane drift
- reusable publish docs must list required caller permissions
- package-registry publish workflows require caller `contents: read` plus their registry-specific write scope so they can check out trusted helper code; the direct `publish-github-official` job is official-only and uses `contents: write`
- `contents: read` cannot push tags, branches, or GitHub Releases, and `persist-credentials: false` only prevents checkout from persisting credentials locally; it does not grant write capability or substitute for explicit job permissions
- `contents: write` is the minimum GitHub Actions permission available for creating or updating GitHub Releases in this design, and that permission is repository-scoped rather than narrowed to a single ref or release object
- idempotent publish handling only treats duplicate-version outcomes as success when remote artifact identity matches; auth and upstream failures stay hard-fail
- reusable publish workflows must emit a machine-readable workflow output indicating whether the run performed a new publish or an idempotent no-op
- a selected publish target that settles as `publish-result = no-op` still finishes with job result `success`; job result `skipped` is reserved for non-selected targets only
- the direct `publish-github-official` job receives `project-name` explicitly so it can create deterministic release titles `<project-name> v<version>`
- the direct `publish-github-official` job is not a standalone authorization boundary; same-repo caller restriction comes from CODEOWNERS plus protected `release/**` tags, not from GitHub workflow-call semantics
- GitHub Release scans in `resolve-context` and the direct `publish-github-official` job must paginate to completion and hard-fail on API, auth, rate-limit, transport, or response-shape errors; overwrite and no-op decisions are never made from partial scan results
- the direct `publish-github-official` job re-checks for a conflicting stable release title immediately before mutating GitHub Release state, and draft releases with the deterministic stable title are part of the same stable identity space
- official GitHub Release idempotency also requires matching remote asset identity
- read-only checkouts in resolve/static-analysis jobs use `persist-credentials: false`
- every workflow job must declare `timeout-minutes`; omission is a lint failure enforced through `hk`/`actionlint`
- timeout defaults are explicit: `preflight-check`/resolve/static-analysis `15`, Ubuntu builds `30`, Windows builds `45` because hosted Windows runners have higher startup and restore/test overhead, isolated attestation jobs `15`, `require-provenance` `45`, publish jobs `25` except `publish-pypi-official` `35`, `confirm-publish-state` `45`, tag-management jobs `10`, and `ci-passed`/`release-complete` `10`
- reusable workflow `permissions:` prohibition is also lint-enforced through a custom `hk` check because `actionlint` does not cover it directly
- official `resolve-context` depends explicitly on `preflight-check`
- official static-analysis evaluates the dispatch-selected source ref directly, the same payload that will be built and released by that run
- `resolve-context` in both buddy and official hard-fail if `nbgv-python` cannot resolve the version deterministically; deterministic means one governing `version.json`, one normalized version string from a full-history checkout, and successful language-specific validation
- `resolve-context` outputs the NBGV-resolved version directly in both channels; official then derives the protected release tag `release/<project-name>/v<version>` from that resolved version
- official releases may publish valid prerelease versions; prerelease status does not relax branch or tag rules and only changes npm dist-tag derivation
- `resolve-context` in official derives deterministic npm dist-tags from branch, release line, and prerelease channel as a compact JSON array: stable `main` uses `["latest"]`, prerelease `main` lowercases the first prerelease identifier and uses that entire identifier as the channel token only when it matches `^[a-uw-z][a-z0-9]*$` and does not start with `v`, stable maintenance branches use `["release-v<major>.<minor>"]` and never append `latest`, and maintenance prereleases use `["release-v<major>.<minor>-<channel>"]`
- official Python version validation also rejects `.devN` development-release forms
- buddy NBGV versions may differ across branches or after new commits change git-history height; that is expected unofficial-channel behavior rather than a recovery bug
- PEP 440 epoch markers (`!`) are intentionally unsupported in release tag versions
- release-line derivation is uniform across ecosystems: strip suffix material, read at most the first two numeric release components, zero-pad a missing minor component to `0`, then render `<major>.<minor>.x` (for example `1.1 -> 1.1.x`, `1.2.3rc1 -> 1.2.x`)
- `mise.lock` is committed alongside `mise.toml`; jobs hard-fail when `mise.lock` is absent, key caches by both files, and use an `hk.pkl`-enforced digest-backed backend allowlist so official build/publish tools always have lockfile-backed digest verification
- `release.json` is loaded only from `<project-root>/release.json`; there is no upward search or inherited fallback
- project-name lowercase-collision validation scans all candidate project roots, not only roots with valid `release.json`
- release target validation is language-aware: `csharp -> nuget/github:official`, `jsts -> npm/github:official`, `python -> pypi/github:official`, `ruby -> rubygems/github:official`
- RubyGems repository policy accepts only `MAJOR.MINOR.PATCH[.suffix...]` with no leading `v`, no `-` or `+`, ASCII-alphanumeric suffix segments, and at least one letter in any suffix chain
- official creates the protected release tag only after resolver, static-analysis, the language-matching build, and `require-provenance` succeed, and the tag reservation itself is gated by `production-tag-write-<project-name>` approval
- `require-provenance`, `create-release-tag`, and every official publish job fail closed through the later GitHub environment gate plus an in-job `CONTROL_PLANE_ENVIRONMENT_ROLE` check; downstream jobs do not mint audit App tokens or perform live control-plane API rechecks
- official GitHub Releases use deterministic release titles `<project-name> v<version>` so overwrite guards can detect same-version identity conflicts across tags
- artifact manifests include per-file `publishRoles` from `{package, github-release-asset}` so package outputs and GitHub Release assets can be selected independently without ambiguous top-level file rules
- the direct `publish-npm-official` path, including its reviewed helper action, hard-fails if `dist-tags` is missing, empty, or not an allowed deterministic ordered tag array for the current run
- the direct `publish-npm-official` path derives caller ref from runtime `github.ref` rather than a caller input, and it emits the exact validated dist-tags via `applied-dist-tags`, which official `release-complete` compares to the deterministic tag array derived in `resolve-context`
- recovery-ledger incident disposition includes `open-before-publish`, `open-partial-publish`, `abandoned-before-publish`, and `abandoned-after-partial-publish`, and every incident record carries a stable UUID `incidentId`
- build artifacts include a manifest of published files and SHA-256 digests; publish workflows verify that manifest before upload, the manifest file name is fixed as `artifact-manifest.json`, it is internal metadata rather than a GitHub Release asset, it uses a fixed schema with `schemaVersion: 1` plus a non-empty `files` array of `{path, sha256, publishRoles}` objects with strict key whitelists and exact 64-character lowercase SHA-256 digests, and deterministic rerun uploads use `overwrite: true` for both build and provenance artifacts
- official build workflows with `require-provenance: true` emit verifier input material rather than final durable evidence: the build side produces deterministic `build-verification-input.json` inside the main build artifact, the isolated attestation job generates `attestation-manifest.json` plus the attestation bundle set in the provenance sidecar artifact, and `require-provenance` verifies those materials, writes the repository-controlled `artifact-evidence.json` to the protected `release-evidence` branch, and records recovery against the durable blob permalink rather than an expiring CI artifact URL; same-path overwrites are disallowed
- `.github/provenance-signer-map.json` is a reviewed control-plane contract with `schemaVersion: 2` that maps each supported language to its language-specific reusable attestation workflow path plus the corresponding top-level attestation caller job; `require-provenance` must validate signer expectations from that checked-in mapping rather than from ad hoc hard-coded language logic
- the authoritative durable-evidence link is `artifact-evidence-url`, and the recovery ledger field is `artifactEvidenceUrl`; future edits must not reintroduce the old `artifact-manifest-evidence-url` or `artifactManifestEvidenceUrl` names
- `artifact-evidence.json` is strict rather than open-ended and records exact attestation verification outputs including workflow run attempt, verified repository, ref, source SHA, `job_workflow_ref`, workflow SHA, repository owner identity, verifier tool, and optional verified environment
- every `artifact-manifest.json` entry path must be a flat top-level file name with no `/` or `\`, must not equal `.` or `..`, and must contain no ASCII control characters, and every publish workflow rejects nested, dot-segment, or control-character paths during manifest validation
- the build workflow, not `publish-github-official`, generates the public checksum asset `SHA256SUMS` whenever GitHub Release assets are present; `publish-github-official` uploads that manifest-selected file byte-for-byte and includes it in remote identity checks
- NuGet build artifacts may also include matching `.snupkg` symbol packages, which should be pushed alongside the corresponding `.nupkg` when the target supports them
- build workflows must produce reproducible package outputs for the same source commit and locked toolchain so rerun idempotency remains valid
- the direct `publish-npm-official` path must use an explicit dist-tag on every publish, separate tarball idempotency from dist-tag idempotency, allow missing tags to be attached to the same version, and never move `latest`, prerelease channel tags, or maintenance-line tags backward
- `confirm-publish-state` is an in-job parallel fan-out with a strict launcher/waiter/reducer pattern; the reducer is the only writer of outputs/checkpoints and is the always-success boundary for semantic states `{complete, partial-timeout, partial-upstream-failure}`
- `confirm-publish-state` workers may span multiple steps only through detached launch plus scratch-recorded PID/deadline and completion-marker metadata; later steps must use the per-target completion marker as the authoritative completion signal rather than shell `wait` or PID-only `kill -0` polling, because they are not the workers' parent process
- `github:official` confirmation derives expected digests from `artifact-evidence-url` and the linked durable `artifactManifestBlobApiUrl`, then cross-checks the remote `SHA256SUMS` asset; it does not re-download the CI artifact as a second truth source
- reusable workflow JSON-array outputs such as resolved targets, npm dist-tags, applied dist-tags, and confirmed publish-state target lists use compact canonical JSON serialization rather than pretty-printed or order-unstable JSON
- GitHub Packages versions are treated as immutable within workflow execution even though GitHub supports delete/restore with elevated package-admin privileges; the workflow design does not request delete/admin permissions and does not support delete-and-republish recovery
- recovery guidance distinguishes fresh dispatch from GitHub's Re-run button and covers partial official publishes plus preflight failures
- GitHub reruns use the original workflow snapshot and do not pick up later fixes to workflow files, reusable workflows, or helper scripts
- recovery guidance tells operators to check the original run's artifacts in the GitHub Actions run UI or API before choosing rerun versus fresh dispatch, and any same-identity rebuild after run expiry or artifact expiry must still satisfy the durable-evidence rule before a fresh dispatch is allowed
- recovery guidance distinguishes GitHub's documented 30-day workflow-rerun limit, GitHub's documented 30-day gate approval time, and the 90-day official artifact retention window as separate timers
- recovery guidance also distinguishes pre-publish validation/build failures from partial publish failures
- recovery guidance treats `require-provenance` failures after possible durable-evidence writes as a special pre-tag case: normal retry is allowed only if the `release-evidence` branch, the project-scoped `control-plane/release-evidence-head/<project-name>` anchor tag, and the expected durable evidence directory already converge on the same verified evidence commit
- recovery guidance distinguishes official failures that happen before `resolve-context` succeeds, before `create-release-tag` succeeds, and after the immutable official release tag has already been created
- recovery guidance includes OIDC trust drift after control-plane branch or workflow-path changes
- recovery guidance distinguishes repository-side `resolve-context` publish trust inventory preflight failures from registry-side Trusted Publisher drift during publish jobs
- recovery guidance treats `release-complete` target-mapping failures as control-plane wiring drift that must be fixed in workflow code rather than retried
- recovery guidance prefers `Re-run failed jobs` on the same official workflow run for transient failures, uses `Re-run all jobs` for declined approvals and other cancellation-style approval outcomes, requires inspecting and draining stale queued runs in the same concurrency group before any fresh dispatch, and allows a fresh dispatch only when the selected protected branch still points to the same commit as the original run
- if a corrected official source commit still resolves to the same version as a burned identity, recovery must bump the version or explicitly delete the burned protected tag through the authorized bypass path before redispatch
- orphaned official tags are not silently accepted; recovery either reruns against the same commit or deletes the tag only through the reviewed emergency-cleanup helper `eng/scripts/official_emergency_cleanup.py` using the authorized `refs/tags/release/**` bypass actor before abandoning that release identity
- recovery guidance includes the case where a draft or published stable GitHub Release with deterministic title blocks a new official run and must be resolved explicitly rather than bypassed by renaming
- if official artifacts expire and the protected branch has moved, the previous partially released identity is treated as burned and recovery proceeds with the next version
- burned, partially published, partially withdrawn, delisted, and fully withdrawn official identities, plus required periodic tag audits, are recorded in `.github/release-recovery-ledger.jsonl` using `recordType` values `{incident, audit}` under `CODEOWNERS` review, with a P0/P1 break-glass path for minimal emergency ledger updates followed by a reviewed cleanup PR; incident records now include `incidentId`, monotonic `revision`, required `releaseLine`, `selectedTargets`, `unpublishedTargets`, strict key whitelists, and hold-window evidence fields for destructive stable-release recovery, while audit records carry their own `auditId` plus monotonic `revision`
- incident and audit ledger records both include `schemaVersion` and `recordType`; `disposition`, `publishedTargets`, `unpublishedTargets`, `deprecatedTargets`, `delistedTargets`, `removedTargets`, `retainedTargets`, `tagState`, `githubReleaseState`, `audit.scope`, and `audit.result` all use closed schemas or enums, `closedAt` is absent rather than null while an incident remains open, terminal published incident states require exhaustive target accounting across `retainedTargets ∪ deprecatedTargets ∪ delistedTargets ∪ removedTargets`, `partially-withdrawn` and `fully-withdrawn` are terminal incident dispositions, and audit records require `closedAt` for both `followUpStatus = resolved` and `followUpStatus = not-required`; `automationId` and `scriptVersion` are an all-or-nothing pair for automated audit records
- `mise.lock` is mandatory repository state, regenerated with `mise lock`, and enforced by `hk check --all`
- the checked-in publish trust inventory is drift detection and audit trail only, not an independent cryptographic backstop
- reusable publish workflows must write `publish-result` to both workflow outputs and `$GITHUB_STEP_SUMMARY`, and `release-complete` both validates selected-target `publish-result` values and aggregates them into its own summary
- official production release is machine-gated by a `require-provenance` job between build and tag reservation; `create-release-tag` and official publish jobs are ineligible until that gate succeeds
- build reusable workflows default `checkout-ref` to the caller job's `github.sha` when the input is omitted
- maintenance branch onboarding is a bounded control-plane transition and project-local official-release hold in this design: create and protect the branch first, update GitHub-side deployment-branch policy and any required registry-side selector or secret change next, then land the reviewed `main` control-plane change, and lift the hold only after GitHub-side and checked-in state converge; the drift monitor opens `high-nonpage` immediately and pages only after prolonged mismatch or overlap with any active official run for that same project
- maintenance branch retirement is also a bounded control-plane transition and project-local official-release hold: first drain or settle queued and in-progress official runs for that branch, then land the reviewed `main` removal of that caller ref from `.github/official-caller-refs.json` and from the matching project's `allowedCallerRefs` in `.github/publish-trust-inventory.json` while preserving the rest of the project entry, then remove the branch from the protected environments, and page only if the resulting mismatch persists too long or overlaps any active official run for that same project; if retirement cannot finish in one operator session, the supported alternatives are to finish it immediately or restore the pre-retirement state

## March 2026 independent external confirmation for the DESIGN.v2 review remediation follow-up

### Confirmed facts

- npm trusted-publishing docs still document a single trusted publisher connection per package, require GitHub-hosted runners, and auto-generate provenance for eligible public publishes
- PyPI trusted-publishing docs still treat reusable workflows as unsupported; the upstream `pypa/gh-action-pypi-publish` docs separately treat composite-action invocation as unsupported and keep `skip-existing` opt-in
- PyPI trusted publishing supports TestPyPI through the same action path using `repository-url: https://test.pypi.org/legacy/`
- GitHub documents `github.actor`, `github.actor_id`, and `github.triggering_actor`; reviewed docs make clear that actor IDs can identify app actors as well as human actors
- `gh attestation verify` exposes reusable-workflow signer identity through verified `job_workflow_ref`, so a language-specific reusable attestation workflow path is a verifier-distinguishable signer boundary
- NuGet trusted-publishing docs still describe `NuGet/login@v1` using a required `user` input and issuing a short-lived API key

### Remaining assumptions

- a checked-in dispatcher allowlist that mixes `users` and GitHub App `appIds` is a repository design choice layered on top of GitHub's documented actor contexts; GitHub does not provide a native workflow-file actor allowlist
- the `publish-pypi-testpypi` same-identity recovery behavior documented for `buddy.yml` is a repository workflow design built from supported upstream capabilities, not a separately named provider contract
- `official.yml` uses four GitHub Apps in the design: an `environment-reader` App for protected-environment policy reads, a `ruleset-auditor` App for ruleset inspection, a release-tag writer App scoped to protected tag creation, and a release-evidence writer App scoped only to the protected `release-evidence` branch
- the design intentionally avoids standalone registry-auth and write-credential canary workflows; readiness is checked through reviewed inventory, periodic drift audits, and the later environment-gate plus environment-role fail-closed checks inside `official.yml`
- `ci-passed` must re-derive which language suites were required from `detect-changes.outputs` and may not treat an unexplained skipped test job as success
- buddy and official static-analysis scope HK by passing the project path directly to `hk check`; the design no longer pre-enumerates file lists in shell
- official artifact retention is `90` days to exceed GitHub's documented 30-day workflow-rerun limit and preserve post-expiry recovery margin
- official tag rulesets use a dedicated GitHub App as the workflow automation bypass actor rather than the GitHub Actions app; emergency manual cleanup is limited to the dedicated release-engineering group
- stable GitHub Release conflict detection treats draft releases with the deterministic stable title as part of the same stable identity space rather than a separate namespace
- declined approvals and other cancellation-style approval outcomes are recovered with `Re-run all jobs`, while transient failed jobs use `Re-run failed jobs`
- operations must audit protected `release/**` tags against completed official releases at least once every 7 days and immediately after run expiry, reviewed manual orphan-tag deletion, burned-identity declaration, or an approval incident escalated beyond the normal waiting budget, and record that audit in `.github/release-recovery-ledger.jsonl`
- official recovery monitoring now uses five control-plane monitors: a 30-minute drift monitor that opens `high-nonpage` immediately and pages only after prolonged mismatch or overlap with an active official run, a 5-minute official-run health monitor on the `high-nonpage` route that combines approval-age, queued-run loss, post-tag failure detection, and recent GitHub Release asset-integrity checks, a 6-hour open-incident freshness monitor with severity-aware escalation, a 7-day operational audit that escalates discrepancies on the `high-nonpage` route and revalidates any `github:official` release asset set against durable evidence rather than checking only for existence, and a daily governance-and-runbook freshness monitor that defaults to `tracked-follow-up` and escalates when freshness is already stale; page and high-nonpage monitors use dual independent external heartbeats, and alert delivery is proven periodically with an out-of-band canary
- NuGet fallback readiness is a `high-nonpage` condition: the monitored pre-staged fallback path must stay reproducible as a reviewed control-plane snapshot, be CI-validated at least every 7 days, and record its readiness snapshot without requiring a standing open or pre-approved PR
- emergency-cleanup destructive bypass still requires contemporaneous dual control, but P0/P1 incidents now have a 2-hour escalation path to temporary engineering-management or security-leadership authorization with mandatory ledger/ticket recording and later backfill of the normal second-approval record
- automation may prepare candidate `audit` ledger payloads or open reviewed PRs, but routine ledger writes are reviewed PRs on `main`; direct automated pushes to `.github/release-recovery-ledger.jsonl` are unsupported outside break-glass
- any open incident with `disposition` in `{open-before-publish, open-partial-publish}` is re-checked at least every 6 hours against live state, compared against stored `publishedTargets` plus `unpublishedTargets`, and escalates according to the incident's declared severity and overdue `nextReviewAt`; unresolved `discrepancy-found` audit follow-ups also alert after 24 hours
- the registry-withdrawal runbook and the registry-auth rollback runbook require release-engineering re-attestation at least every 90 days
- `production-evidence-write-<project-name>` is a third protected environment reserved exclusively for durable evidence persistence by `require-provenance`
- there is no checked-in planned-change-window file in the current design; onboarding, retirement, and emergency cleanup are coordinated runbooks that must either reach a converged end state or restore the previous converged state without leaving durable exception state on `main`
- GitHub Actions concurrency groups are case-insensitive, so design contracts must normalize or compare them without assuming case-distinct groups are separate
- `create-release-tag` must check the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API rather than relying on a local tag list from checkout
- repository policy must include a CI validation that rejects releasable project-name collisions under ASCII lowercase normalization
- repository policy must also include a CI validation that rejects releasable project roots resolving to more than one workflow language
- buddy `resolve-context` should emit a Python-specific error when unofficial-target filtering becomes empty because Python has no buddy channel in this design
- adding a new supported language requires updating every buddy publish-job `if:` guard that maps targets to the single language-matching build result
- incident ledger records require `evidenceUrl`; `workflowRunUrl` and `runAttempt` are conditional on `attemptScope = single-run-attempt`, and corrections for the same incident reuse the same `incidentId`
- `open-before-publish` requires `publishedTargets = []` and `unpublishedTargets ∪ indeterminateTargets = selectedTargets`, `open-partial-publish` requires `publishedTargets` to be non-empty and at least one remaining target to stay in either `unpublishedTargets` or `indeterminateTargets`, `recovered` requires `unpublishedTargets = []` plus `retainedTargets = publishedTargets`, `abandoned-before-publish` additionally requires `indeterminateTargets = []`, `abandoned-after-partial-publish` requires `publishedTargets` to be non-empty with `indeterminateTargets = []` and at least one remaining `unpublishedTargets` entry, `partially-withdrawn` requires `unpublishedTargets = []` plus both retained and withdrawn published targets, and `fully-withdrawn` requires `unpublishedTargets = []` plus `deprecatedTargets ∪ delistedTargets ∪ removedTargets = publishedTargets`
- PyPI withdrawal may use yank or delete; deleting a PyPI file or release permanently burns that exact filename/version identity, and any such irreversible burn requires an explicit same-ledger decision for each surviving already-published non-PyPI target before a new-version dispatch proceeds
- `holdStartedAt`, `eligibleDeleteAt`, and `consumerImpactEvidenceUrl` are required only for destructive stable-release recovery when the 48-hour hold actually applies and must be absent otherwise; there is no shorter same-identity hold-waiver path in the current design
- break-glass ledger bypass may touch only `.github/release-recovery-ledger.jsonl`, and automation alerts if the required reviewed cleanup PR is not merged by the next business day
- gate jobs such as `ci-passed` and `release-complete` obtain `jq` through the repository-managed `mise` toolchain rather than from the runner image

## Maintenance and future edits

If any of these rules changes, update both:

- `.github/workflows/docs/MEMORY.md`
- `.github/workflows/docs/DESIGN.v2.md`
