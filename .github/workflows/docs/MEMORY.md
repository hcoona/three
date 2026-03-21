# Workflow Design Memory

## Current repository reality

- `AGENTS.md` says the repository has not yet completed the per-project root migration under `src/`.
- The workflow design can describe the target release model now, but implementation must wait until that migration exists.

## Current design decisions to preserve

- Keep externally exposed workflows exactly `ci.yml`, `buddy.yml`, and `official.yml`.
- Do not add extra triggered workflow files for readiness, drift, governance, or health monitoring.
- Keep buddy publish authorization in direct jobs, not in same-repository reusable publish workflows.
- Use `github:release` as the GitHub Release target.
- Do not reintroduce `pypi:testpypi`.
- Python buddy preview, if needed, uses `github:release`.
- In `official.yml`, do all project canonicalization, existence, uniqueness, and target-compatibility checks before any environment with secrets is entered.
- Use `production-<project-key>` as the authoritative official human approval gate.
- Keep target-specific official environments subordinate to that baseline gate.
- Use bounded checked-in admission/recovery state plus one bounded live lock per project instead of unbounded historical run scans or scheduled snapshot freshness.
- Route JS/TS releases by checked-in `buildKind`, not by `jsts` alone.
- Keep buddy GitHub Release identity separate from the official tag namespace.

## GitHub Actions semantics relevant here

### FACT

- A job-level `environment` is evaluated before that job's steps can use environment-scoped secrets or protected deployment approval.
- A same-repository reusable workflow call does not create a new independent authorization boundary just because the caller passes an input such as a path or workflow name.
- Every additional triggered workflow file is another repository entrypoint.
- Scheduled workflows on GitHub Actions can be delayed or missed.
- GitHub Actions does not provide a native per-project admission index for "is this release currently blocked?".
- A `workflow_dispatch` run on a branch is associated with a specific commit snapshot; if workflow logic re-queries the branch later, it can observe a newer HEAD than the run snapshot.
- GitHub Releases are keyed to repository tags, and GitHub can auto-create a tag while creating a release if the tag does not already exist.
- Branch protection/rulesets and tag protection/rulesets are separate controls.
- Referencing a missing environment can auto-create it without required reviewers.
- `GITHUB_TOKEN` is GitHub-native auth and is suitable for GitHub APIs and GitHub-hosted package surfaces, but not for external registries such as PyPI/npmjs/RubyGems.org/NuGet.org.

### INFERENCE

- Because environment access happens at job entry, official preflight validation that should happen before secrets must stay in no-environment jobs.
- Because reusable-workflow indirection is not a strong authorization boundary, buddy publish should remain in direct jobs.
- Because each triggered workflow file is another entrypoint, control-plane behavior should stay inside `ci.yml`, `buddy.yml`, and `official.yml` rather than adding more triggered workflows.
- Because schedules can slip, scheduled snapshot freshness is a weak foundation for release admission.
- Because GitHub has no native admission index, the repository needs an explicitly materialized checked-in admission ledger and a separate immediate live lock if official admission must be both bounded and durable.
- Because GitHub Release shares the repository tag namespace, buddy and official releases need separate tag namespaces.
- Because GitHub can auto-create unprotected environments and tags unless policy blocks it, the design must treat pre-created protected environments and protected tag namespaces as explicit prerequisites.

### ASSUMPTION

- A baseline approval job using `production-<project-key>` can gate the official release before subordinate target-specific jobs run.
- Any remaining provider-specific readiness check can be performed as a bounded same-run check inside `official.yml` rather than by a separate scheduled workflow.
- The per-project root migration called out in `AGENTS.md` will happen before workflow implementation begins.
- Preferred external-registry auth for official targets should be OIDC trusted publishing where the provider/tooling contract is validated for this repository; otherwise the design must make any target-specific secret fallback explicit.

## External-system confirmation for current design edits

### FACT

- `DESIGN.prompt.md` says the `workflow_dispatch` selected branch supplies both trusted control-plane code and the release payload source for `official.yml`.
- Checked-in docs define `.github/official-admission-state.json`, but they do **not** define any separate dispatcher-allowlist file or caller-ref-eligibility file/schema/path.
- GitHub Actions evaluates a job-level `environment` before that job can use environment-scoped secrets or approval.
- Checked-in docs in this repository did not confirm that official environments are already pre-created or protected; that must be stated as a design requirement rather than treated as an established repository fact.
- `eng/scripts/find_project_path.py` resolves projects by ecosystem identity from manifests (`pyproject.toml`, `package.json`, exact `.gemspec`), not by leaf-directory-name matching.
- The current `DESIGN.v2.md` buddy static-analysis scope omitted `.github/workflows/_build-test-*.yml` even though buddy is designed to invoke one of those reusable workflows.
- A `workflow_dispatch` branch run is snapshot-based, not "follow branch HEAD forever"; re-querying the branch later can observe a newer commit than the run snapshot.
- GitHub evaluates environment protection at job entry, and each referenced environment can independently create an approval boundary.
- GitHub may auto-create a referenced environment without required-reviewer protection.
- GitHub Release identity is tied to repository tag identity, and GitHub can auto-create a tag as part of release creation if policy does not prevent it.
- Source-branch protection and release-tag protection are separate repository controls.
- `GITHUB_TOKEN` is the correct auth class for GitHub-native targets; external registries require a different auth mechanism.

### ASSUMPTION / DESIGN CHOICE

- For a normal official release, `policy-sha` and `payload-sha` should be the same dispatch snapshot commit, and no later job should re-resolve branch HEAD.
- Recovery should be modeled explicitly: updated checked-in recovery evidence may live at a newer `policy-sha` while the release payload remains pinned to the original blocked `payload-sha`.
- The revised design should require `production-<project-key>` and every referenced subordinate environment to be pre-created and policy-checked, with missing or unprotected environments treated as hard failures.
- The revised design should require protected official tag and live-lock namespaces in addition to protected source branches.
- All official publish jobs should depend on successful official tag creation, and `github:release` should attach to an already-created official tag instead of relying on GitHub's implicit tag creation path.
- Buddy GitHub Releases should use a tag namespace disjoint from official `release/*` tags.
- The revised design should define a target-auth matrix: `GITHUB_TOKEN` for GitHub-native targets, and either validated OIDC trusted publishing or an explicitly documented target-scoped secret fallback for external registries.

## Independent GitHub platform confirmation for v2.22 edits

### FACT

- GitHub treats branch protection and tag protection as separate controls. A branch-targeted rule does not generically protect tags, and a tag-targeted rule does not generically protect branches.
- GitHub environments are for deployment protection rules plus environment-scoped secrets and variables. Using an environment does **not** itself bypass branch or tag protection/rulesets.
- Whether a workflow can write a protected branch or protected tag depends on the actor/credential used for the write and whether that actor is allowed by the applicable protection rule/ruleset.
- GitHub can auto-create a referenced environment that does not yet exist, and that auto-created environment starts without required reviewers or other protection semantics.
- A single workflow can reference multiple environments, and each environment with required reviewers creates its own approval boundary for the jobs that reference it.
- GitHub protection/ruleset primitives are defined for branches and tags. The design must not assume a generic protected custom ref namespace.
- GitHub environments support required reviewers and a `prevent self-review` option.

### DESIGN IMPLICATION

- The live lock must be modeled as an actual protected branch ref or tag ref. In `DESIGN.v2.md` it is now a protected tag ref.
- The design must explicitly define the credential/actor that writes protected release tags and the live lock; baseline environment approval is not sufficient by itself.
- Every approval-gated environment must be pre-created and policy-checked before use.
- If `production-<project-key>` is intended to be the only required human gate, no subordinate environment may independently require reviewers.
- The baseline approval environment should explicitly require reviewers, enable `prevent self-review`, and document its admin-bypass policy so the approval boundary is real instead of nominal.

## Independent external-system confirmation for v2.23 design fixes

### FACT

- A `workflow_dispatch` run executes the workflow file from the dispatched ref snapshot. If a user dispatches `official.yml` from a branch, that branch's copy of `official.yml` becomes the entry workflow before any in-workflow branch-protection check can run.
- Because of that execution model, an in-workflow "selected branch must be protected" check is validation of a runtime input, not a standalone trust boundary against branch-local workflow edits.
- GitHub App installation tokens are scoped by the App's repository permissions and installation target, not by individual branch/tag refs. Ref-level allow/deny behavior comes from branch/tag protections or rulesets plus workflow design.
- GitHub branch protection and tag protection are separate control surfaces. Actor restrictions for protected official tags/live locks therefore need tag-targeted rulesets rather than branch protection or a generic "protected ref" assumption.
- GitHub Actions artifact storage and attestation references do not by themselves provide an indefinite durability/immutability contract. Retention, deletion authority, recovery readability, and immutability guarantees must be defined explicitly by the design if recovery depends on old bytes remaining fetchable.
- A `workflow_dispatch` run captures a snapshot SHA at dispatch time, while later branch re-queries can observe newer commits. Frozen control/payload SHAs therefore need to be captured explicitly and reused downstream.

### ASSUMPTION / DESIGN CHOICE

- `official.yml` should be dispatched only from one designated protected control branch, with the releasable source branch provided as validated data input instead of as the workflow-code trust root.
- The official live lock should be modeled as an annotated protected tag carrying comparable frozen-plan identity (`planDigest`), not merely as a tag that points to `payloadSha`.
- Recovery should depend on a separately defined durable immutable artifact-store contract. Until the design adds a reproducibility matrix by `buildKind`, rebuild-based recovery should remain out of scope.
- Until such a reproducibility contract is documented per build kind, the normative recovery path is reuse of the previously persisted immutable artifact bundle.

## Independent external-system confirmation for current design-review fixes

This section supersedes earlier GitHub-native publication and control/payload-snapshot assumptions where they conflict with the current design-review fixes.

### FACT

- A `workflow_dispatch` run executes the workflow file from the dispatched ref snapshot. An in-workflow check that says "this run must be from `main`" can fail a run, but it cannot by itself make `main` the trust root.
- When a workflow uses local composite actions such as `uses: ./.github/actions/...` or runs repository scripts such as `bash eng/scripts/...`, those paths resolve from the checked-out workspace contents used by that job. If payload checkout is placed where those paths resolve, the payload copy's code is what executes.
- GitHub Actions `concurrency` with a shared group and `cancel-in-progress: false` prevents concurrent execution for that group and avoids canceling the in-flight run, but GitHub does not document it as a durable FIFO queue.
- Because `workflow_dispatch` and workspace-path execution are both ref-sensitive, a design that wants a reviewed control-plane snapshot separate from payload content must physically separate control checkout and payload checkout and must keep privileged credentials behind repository-side controls rather than relying on in-workflow self-assertion.

### ASSUMPTION / DESIGN CHOICE

- `buddy.yml` and `official.yml` should both treat the designated protected control branch as the only supported source of privileged workflow/control-plane code, with the selected source branch providing payload data only.
- Privileged GitHub-native publication for buddy and official flows should use dedicated environment-issued or brokered credentials instead of ambient branch-local `GITHUB_TOKEN` write authority.
- The checked-in official admission and recovery ledger should have a single authority branch (`main`), while release-line branches remain payload inputs rather than separate admission ledgers.
- The live lock should clear in-run only for fully successful release completion; partial, failed, and uncertain states stay blocked until reviewed checked-in state records the disposition for that frozen plan.
- Machine-readable recovery approval must include whether recovery is approved and, in the current design revision, whether restore-from-bundle recovery is authorized.

## Independent external-system confirmation for v2.24 review fixes

This section supersedes earlier target-auth and success-identity assumptions where they conflict with the current v2.24 design edits.

### FACT

- Tag protection and tag-ruleset bypass are evaluated against the actor represented by the credential presented to the GitHub API. An environment can gate whether a job may mint or receive that credential, but the environment name is not part of the GitHub ref-write authorization decision.
- Therefore, using the same GitHub App actor in both `production-ref-write-<project-key>` and `production-github-<project-key>` does **not** preserve separation of duties for protected tags. If the same actor is allowed by the protected-tag ruleset, any job that can mint that actor's token can write the protected tags.
- GitHub Release creation, Release asset upload/delete, and protected-tag mutation are separate API operations, but they all sit under repository `contents` write capability. GitHub does not provide a native permission split such as "release assets only" versus "protected tag writer only"; separation must come from distinct actors plus protected-tag rulesets.
- GitHub Release assets are mutable independently from both the git tag ref and the Release object. A release can still point to the expected tag while its asset set has drifted through deletion, re-upload, rename, or partial upload.
- GitHub OIDC permission is job-scoped through `id-token: write`. The least-privilege pattern is to grant it only to the publish job that actually requests an OIDC token, not at workflow scope.
- PyPI and RubyGems trusted publishing support provider-side trust configuration that can bind to repository/workflow identity and, where configured, environment identity.
- npm publishing still requires an npm credential for registry authentication; GitHub OIDC is relevant there for provenance/signing rather than as a direct replacement for the npm publish credential.
- NuGet does not currently provide the same GitHub OIDC trusted-publishing model used by PyPI/RubyGems, so a scoped NuGet credential remains the confirmed design-safe path.

### ASSUMPTION / DESIGN CHOICE

- Official protected ref writing should use a dedicated actor that is never reused for `github:release` publishing jobs.
- Official successful release identity should remain durably discoverable after the live lock is cleared, so the annotated official release tag should carry the frozen release-plan identity used for same-identity checks on rerun.
- Same-identity checks for `github:release` should include exact asset-set equality with the authoritative digest manifest, not just tag equality or release existence.
- PyPI and RubyGems official targets should require trusted publishing with provider-side pinning to the exact official workflow/environment contract, while npm and NuGet should keep environment-scoped publish credentials for authentication.
- Rebuild-based recovery should stay out of scope until the design later adds an explicit reproducibility matrix by `buildKind`.

## Independent external-system confirmation for v2.25 review fixes

This section supersedes the earlier npm-auth statement and related design choice in the v2.24 note where they conflict, and adds confirmed PR trust-model facts used by the current design edits.

### FACT

- GitHub withholds repository secrets from workflows triggered by `pull_request` events from forks.
- GitHub repository settings can separately opt fork PR workflows into broader token or secret exposure; that is a repository setting choice, not an automatic property of the `pull_request` event itself.
- `pull_request_target` runs in the context of the base repository and base branch workflow definition, so a workflow in that event family can access the base repository's secrets and permissions if the job/environment configuration allows it.
- npm supports trusted publishing from GitHub Actions through GitHub OIDC for registry authentication.
- NuGet.org does not offer a comparable GitHub OIDC trusted-publishing path for package publication; API-key-style credentials remain the documented publication model.
- GitHub environments can require reviewers and can restrict which branches or tags may deploy to that environment, but environment controls are separate from branch/tag protection and rulesets.
- GitHub rulesets for protected branches/tags and GitHub environments are separate repository control surfaces; an environment approval does not itself authorize a protected ref write.

### ASSUMPTION / DESIGN CHOICE

- `ci.yml` should use `pull_request` for untrusted PR validation. If `pull_request_target` is ever used, it should be limited to metadata-only repository-maintenance work that does not check out, execute, or source PR-head code.
- PR workflows should never mint publish credentials, protected-ref-write credentials, or other privileged release credentials, and they should never be used as a publication path.
- Repository settings that would expose fork PRs to secrets or privileged write tokens should remain disabled in the normal design.
- npm official publication should use trusted publishing as the normal auth path, while npm provenance/signing remains a separate concern from registry authentication.
- NuGet official publication should remain the explicit provider-exception credential path until a comparable trusted-publishing model exists.
- The checked-in machine-readable repository release contract should be the single source of truth for repository-side prerequisites, PR trust-model rules, environment contracts, and target-auth contracts, with PR CI validating drift against it.

## Independent external-system confirmation for v2.26 review fixes

This section supersedes earlier control-branch and generic "GitHub-native target auth" assumptions where they conflict with the current review-driven design corrections.

### FACT

- For `workflow_dispatch`, the branch selected in the UI determines which snapshot of the workflow file GitHub executes. An in-workflow check can fail closed, but it cannot retroactively make some other branch the workflow-code trust root.
- GitHub Actions `concurrency` with a shared group and `cancel-in-progress: false` prevents overlapping execution, but GitHub does not document it as a durable FIFO queue or release-admission ledger.
- GitHub Releases and GitHub Packages are different GitHub product/API surfaces even though both are GitHub-native. Release publication is tied to repository releases/tags, while package publication is tied to package registries and package permissions.
- For GitHub Packages publishing from Actions, the normal same-repository auth model is job-scoped `GITHUB_TOKEN` with `packages: write`. A GitHub App can still be a repository hardening choice, but it is not a platform requirement for GitHub Packages publication.
- The checked-in design creates the live lock before the durable provenance write. Therefore the blocked-state schema must be able to represent a real `pre-provenance` blocked window where a live lock exists but authoritative durable `artifactIdentity` does not yet exist.

### ASSUMPTION / DESIGN CHOICE

- The reviewed design should restore prompt alignment by treating the dispatch-selected protected branch as the single official trust root for normal official releases, while recovery remains an explicit frozen-plan exception.
- Buddy and official should share one per-project runtime concurrency key only as a mutex; durable ordering and recovery must stay in checked-in admission state plus the live lock.
- The target-auth model should distinguish at least three classes: GitHub Release API auth, GitHub Packages registry auth, and external-registry auth.
- If the design keeps "live lock before provenance", the admission-state schema should use an explicit blocked-stage discriminator so `pre-provenance` and `post-provenance` blocked states are both representable without fabricating missing provenance data.
