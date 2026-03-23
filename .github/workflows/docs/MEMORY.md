# Workflow Design Memory

## Current repository reality

- `AGENTS.md` says the repository has not yet completed the per-project root migration under `src/`.
- The workflow design can describe the target release model now, but implementation must wait until that migration exists.

## Current design decisions to preserve

- Keep externally exposed workflows exactly `ci.yml`, `buddy.yml`, and `official.yml`.
- Do not add extra triggered top-level workflow files for readiness, drift, governance, or health monitoring.
- Keep buddy publish authorization in direct jobs, not in same-repository reusable publish workflows.
- Keep official publish authorization in direct `official.yml` jobs so OIDC-backed trusted publishing sees `.github/workflows/official.yml` as the workflow identity.
- Use `github:release` as the GitHub Release target.
- Do not reintroduce `pypi:testpypi`.
- Python buddy preview, if needed, uses `github:release`.
- Use `production-<project-key>` as the authoritative official human approval gate.
- Keep target-specific official environments subordinate to that baseline gate.
- Use bounded checked-in admission/recovery state plus one bounded live lock per project instead of unbounded historical run scans or scheduled snapshot freshness.
- Route JS/TS releases by checked-in `buildKind`, not by `jsts` alone.
- Keep buddy GitHub Release identity separate from the official tag namespace.

## External-system confirmation for the current review-driven design revision

### FACT

- GitHub Actions skips a downstream job by default when one of its `needs` jobs is skipped, unless the downstream job uses an explicit `if:` expression such as `always()` plus `needs.<job>.result` logic to override the default skip cascade.
- `workflow_dispatch` runs against the selected ref snapshot; an in-workflow check can fail closed, but it cannot retroactively make some other branch the workflow-code trust root.
- `actions/checkout` defaults `persist-credentials` to `true`; disabling it reduces the exposure surface of the checkout credential that would otherwise be available to later steps.
- Reusable workflows operate under the caller’s effective permission ceiling; the called workflow can reduce permissions but not elevate beyond what the caller grants.
- GitHub can auto-create a referenced environment that does not yet exist, and that auto-created environment starts without required reviewers or equivalent protection semantics.
- GitHub environments support required reviewers, `prevent self-review`, wait timers, and deployment branch/tag restrictions.
- GitHub concurrency is not a durable FIFO queue. GitHub documents one running and one pending slot per concurrency group, and newer queued work can replace older pending work.
- Branch/tag protection and GitHub environments are separate control surfaces. Environment approval does not itself authorize protected ref writes.
- npm supports trusted publishing from GitHub Actions through GitHub OIDC.
- PyPI supports trusted publishing from GitHub Actions through GitHub OIDC.
- RubyGems supports trusted publishing from GitHub Actions through GitHub OIDC.
- Current external research indicates NuGet/NuGet.org now has a GitHub Actions trusted-publishing path using OIDC, although provider rollout details may still evolve.
- Default GitHub Actions artifacts are retention-limited and deletion-prone; by themselves they are not a sufficient long-term immutable recovery store.

### ASSUMPTION / UNCERTAINTY

- The design can require exact checked-in provider-trust summaries plus bounded read-only drift checks where a provider exposes an inspection mechanism, but provider inspection APIs are not guaranteed to be uniform across all registries.
- Current registry trusted-publishing integrations do not appear to provide one uniform provider-side mechanism for exact GitHub `ref` claim pinning across npmjs, PyPI, RubyGems.org, and NuGet.org, so the design must record per-target capability mode instead of assuming universal provider enforcement.
- Treating GitHub-maintained actions under `actions/` as part of the same SHA-pinning requirement as other non-local actions is a deliberate supply-chain policy choice, not a property automatically enforced by GitHub.
- Because NuGet trusted publishing appears to be available now, the design removes the older long-lived NuGet secret exception instead of preserving compatibility with the previous assumption.
- Because GitHub concurrency is not a durable queue, release ordering and recovery authority must remain in checked-in admission state plus the live lock even if runtime queue behavior changes later.

## External-system confirmation for the latest design-only fixes

### FACT

- GitHub Packages buddy publication commonly uses the job-scoped `GITHUB_TOKEN` with `packages: write`; this is the design baseline for `nuget:gpr`, `npm:gpr`, and `rubygems:gpr`.
- GitHub environment approval UI does not natively display arbitrary workflow output fields, so any requirement to show frozen release identity at approval time needs an equivalent reviewed approval surface implemented by the repository.
- GitHub Actions concurrency is not a durable FIFO queue; a design that holds a shared per-project concurrency slot during long approval waits risks avoidable head-of-line blocking and pending-run replacement.
- npm, NuGet.org, and RubyGems differ in version immutability and republish behavior; a recovery design must assume that some registries will reject same-version republish after uncertain or differing content is observed.
- Provider-side OIDC trust capabilities are not uniform across registries, so the design must record provider-enforced claim capabilities explicitly instead of assuming every registry can pin the same GitHub claims.

### ASSUMPTION / UNCERTAINTY

- The exact GitHub Packages behavior for every ecosystem surface is treated as sufficiently similar for the design baseline (`GITHUB_TOKEN` plus `packages: write`), but ecosystem-specific edge cases may still need later implementation-time confirmation.
- The exact registry-side combinations of provider-enforced repository, workflow-path, environment, and ref claims may evolve over time; the checked-in capability summary remains the authoritative design input.
- Registry-specific rejection and cleanup paths after a partial same-version publish are not fully uniform, so the design must keep the abort path explicit rather than assuming automated retry is always legal.

## External-system confirmation for the review-fix revision

### FACT

- GitHub environment approval UI still does not show arbitrary job outputs inline, so a repository-owned equivalent reviewed surface is required whenever approvers must inspect frozen release identity before granting environment approval.
- GitHub Deployments can carry description/payload metadata, but that metadata is descriptive only; it is not itself the environment-approval UI and therefore must be paired with repository-side validation of the deployment payload and reviewer confirmation text.
- CODEOWNERS and branch protection are separate repository controls from GitHub Actions workflow logic; they can protect bootstrap files such as `ci.yml` even though `ci.yml` cannot be its own root of trust.
- GitHub concurrency remains non-FIFO and non-durable, so approval-to-mutation handoff rules must be enforced by checked-in policy plus live-lock revalidation rather than by queue behavior.
- GitHub Actions does not provide a documented SLA that approval and the first post-approval job step happen back-to-back; repository state can change between reviewer approval and later job execution, so stale-approval revalidation must be explicit and fail-closed.
- Some registries reject same-version publication after differing bytes or uncertain partial publication, so a safe design cannot assume that a changed rebuild may be republished under the same version.
- Current trusted-publishing integrations across npm, PyPI, RubyGems, and NuGet do not provide one uniform provider-side exact GitHub `ref` enforcement model; workflow-side `allowedRefClaims` checks remain mandatory whenever provider-side exact-ref support is absent or unclear.
- OCI/GHCR content addressing is keyed by manifest digest, not by an arbitrary business key such as `planDigest`, and native atomic `create-if-absent(planDigest, ...)` semantics are not provided by the OCI distribution model.

### ASSUMPTION / UNCERTAINTY

- A GitHub Packages artifact-store backend can satisfy the design’s `create-if-absent` semantics when constrained with a repository-owned commit-marker discipline, but the exact implementation details remain a repository responsibility until code exists.
- Cross-language RFC 8785 / JCS libraries should interoperate for the release-plan payloads in this design, but the repository still needs implementation-time golden-vector testing to confirm identical null-field handling across languages.
- Provider-side exact-ref enforcement and read-only inspection support may continue to change by target and provider, so the checked-in per-target support record must remain authoritative even when external provider documentation evolves.
- GitHub’s exposed approval-event timestamps and comment surfaces may evolve, so the exact implementation mechanism for binding reviewer confirmation text to a deployment audit record still needs implementation-time confirmation against the then-current GitHub APIs.

## External-system confirmation for the current design-only cleanup pass

### FACT

- GitHub Actions supports both workflow-level and job-level `concurrency`, but only a workflow-level concurrency group can hold one shared slot across an entire multi-job mutation phase. A design that needs approval jobs to run outside the shared slot and mutation jobs to hold it across several later jobs therefore needs an internal phase boundary such as a dispatcher/worker split.
- GitHub Actions concurrency remains non-FIFO and non-durable: GitHub documents one running and one pending slot per concurrency group, and a newer queued run can replace an older pending run.
- GitHub environment approval remains the gate for environment-scoped secrets and job-scoped OIDC access; those credentials become usable only after the approved job starts in that environment.
- GitHub environment approval UI still does not natively display arbitrary workflow outputs, so reviewed approval identity must be surfaced through repository-owned artifacts, summaries, comments, or equivalent checked surfaces.
- GitHub Deployment description/payload metadata is descriptive only; it is useful for audit binding, but it is not itself the environment-approval UI or an independent authorization surface.
- When an official target records `providerSupportsReadOnlyInspection = false`, the workflow can validate only its own checked-in contract coherence and workflow-side branch conditions at runtime; it cannot independently prove that the external provider-side trust configuration still matches the checked-in record.
- Provider-side exact GitHub `ref` enforcement remains non-uniform across npm, PyPI, RubyGems, and NuGet trusted-publishing integrations, so `supported`, `unsupported`, and `unknown` must remain per-target checked facts rather than one global assumption.
- TestPyPI is administratively separate from production PyPI and would require separate credentials and operational handling if this repository ever chose to support it.
- GitHub Actions provides no automatic schema-version negotiation, migration, or compatibility semantics for checked-in repository contracts; schema evolution policy must therefore be defined by repository design and enforced by workflow logic.

### ASSUMPTION / UNCERTAINTY

- Provider-side trust capabilities and inspection APIs may continue to evolve, so any recorded support matrix must be treated as time-bound reviewed evidence rather than a timeless universal truth.
- WXT is fundamentally a web-extension build/distribution tool, but restricting `node-wxt` projects to `github:release` assets instead of any registry publication path is still a repository design choice rather than an external-system requirement.
- Excluding `pypi:testpypi` from the buddy design is a repository design choice made for scope and operational simplicity, not a GitHub Actions limitation.

## External-system confirmation for the v2.31 design review fixes

### FACT

- `CODEOWNERS` enforcement is separate from workflow logic and only becomes an effective control when repository protection or rulesets require code-owner review.
- GitHub environment deployment restrictions and branch protection are separate control surfaces; environment branch restrictions do not replace branch protection, and protected-branches-only deployment restrictions rely on real branch protection existing.
- GitHub Actions workflow-level `concurrency` may use the `inputs` context, so `workflow_dispatch` or `workflow_call` inputs can influence the concurrency-group key before jobs begin.
- GitHub Actions concurrency is latest-wins rather than FIFO: one running and one pending slot are retained per group, and a newer queued run can replace an older pending run.
- Reusable workflows run in the caller’s effective context and cannot exceed the caller’s permission ceiling; by themselves they are not a sufficient authorization boundary.
- GitHub workflow artifacts are retention-limited and not a durable long-term store; same-name replacement semantics make them unsuitable as the sole integrity boundary between build and publish.
- GitHub environment approval UI still does not natively present arbitrary workflow outputs, and current reviewed platform surfaces do not provide one clearly documented authoritative `approved_at` value suitable for strict freshness enforcement.
- Referencing a missing environment can auto-create that environment without the intended protection rules.
- GitHub Release asset metadata exposes asset names and digests, so a design may require exact name-plus-digest verification.
- GitHub control-plane approval/workflow features do not provide an out-of-band offline emergency path; any control-plane-independent fallback must be designed and governed by the repository.

### ASSUMPTION / UNCERTAINTY

- Treating `.github/CODEOWNERS` as part of the bootstrap-governance hash surface is a repository design choice made because the design depends on that file as a trust anchor.
- Because workflow-level concurrency keys can include caller-supplied inputs and GitHub does not provide FIFO guarantees, the design assumes caller validation and constrained key composition are required to prevent avoidable slot-starvation or denial-of-service behavior.
- The safest approval-freshness design remains fail-closed when a trustworthy approval-grant timestamp is unavailable; using any other timestamp as a fallback would be only a conservative approximation, not a fully authoritative approval time.
- Exact provider-side and platform-side inspectability for stuck queues, approval events, and protection drift may evolve, so the design keeps monitoring and runbook obligations explicit rather than assuming one complete authoritative API exists for every failure mode.

## External-system confirmation for the v2.32 design-only fixes

### FACT

- In reusable workflows invoked by `workflow_call`, documented runtime context such as `github.workflow` and `github.workflow_ref` identifies the called workflow, not a documented caller-workflow path. The review suggestion to rely on `github.workflow_ref` for caller authentication was therefore unsafe.
- `secrets: {}` is a valid reusable-workflow call shape and makes the “pass no secrets” decision explicit in reviewed YAML; `secrets: inherit` would intentionally widen the secret surface.
- GitHub-hosted labels such as `ubuntu-latest` and `windows-latest` are floating labels, so using versioned labels is a safer design baseline for reproducibility than relying on `*-latest`.
- CODEOWNERS by itself is not an enforcement boundary; repository protection or rulesets must require code-owner review for it to become an effective control.
- Public registry cleanup semantics are not uniform. Conservative design assumptions are: npm official releases may be effectively burned once same-identity cannot be proved; PyPI supports yanking but the design should not assume routine delete-and-republish; NuGet.org distinguishes unlisting from deletion and same-version reuse should stay conservative; RubyGems supports yanking but that is not a safe basis for changed-byte republish; GitHub Releases and repository-owned GitHub Packages surfaces are comparatively more delete-capable.
- Chrome Web Store uses a stricter extension-version format than npm semver prerelease strings, so a `node-wxt` release design should not assume one version string automatically fits both GitHub Release assets and a browser-store surface.

### ASSUMPTION / UNCERTAINTY

- I could not independently confirm any documented runtime context named `github.calling_workflow_ref` or `github.calling_workflow_path`. The safer design choice is therefore to avoid depending on undocumented caller-context names and instead require repository-owned allowlisted call sites plus explicit dispatcher-to-worker binding data.
- GitHub may expose some approval/deployment audit timestamps through APIs, but there does not appear to be one clearly documented universally authoritative “approved_at” surface suitable for strict approval-freshness enforcement without repository-owned fail-closed logic.
- Even versioned hosted-runner labels such as `windows-2022` and `ubuntu-24.04` may still drift over time because the underlying image contents can change. Stronger reproducibility may still require self-hosted immutable images or an equivalent image-pin strategy.
- Registry-side delete, yank, unlist, and cache behavior can evolve by provider and policy, so the design should keep per-target cleanup/burn behavior explicit instead of assuming a timeless universal rule.


## External-system confirmation for the review-summary design fixes

### FACT

- In reusable workflows invoked by `workflow_call`, documented runtime fields such as `github.workflow` and `github.workflow_ref` identify the called workflow, not the caller workflow. This design therefore cannot treat those fields as authoritative caller proof.
- GitHub Actions skips a downstream job by default when one of its `needs` jobs is skipped, so expected recovery-path skips require an explicit downstream guard such as `always()` plus `needs.<job>.result` logic.
- GitHub environment approval remains the gate for environment-scoped secrets and job-scoped OIDC, but the approval UI still does not natively render arbitrary workflow outputs; post-approval jobs must re-read repository-owned reviewed release identity rather than assuming the approval dialog carried it.

### ASSUMPTION / UNCERTAINTY

- The review summary suggested `GET /repos/{owner}/{repo}/actions/runs/{run_id}/approvals` as the likely GitHub API lookup for an authoritative approval timestamp. That endpoint shape is treated as the intended implementation-time candidate, but this design revision does not treat any one endpoint or `approved_at` field as timelessly normative without re-confirming the then-current GitHub documentation.
- Because no documented runtime caller-workflow identity field has been independently confirmed, repository-owned call-site allowlisting remains a governance and code-review control backed by CODEOWNERS plus bootstrap integrity, not a standalone runtime API proof.
- GitHub’s approval-event audit surfaces may evolve, so the design continues to fail closed whenever the workflow cannot obtain an authoritative current-run approval-grant timestamp from a documented GitHub surface.

## External-system confirmation for the final review-summary design fixes

### FACT

- GitHub Actions concurrency keeps at most one running run and one pending run per concurrency group; a newer queued run can replace an older pending run.
- Operators can observe cancelled or replaced pending runs in GitHub Actions run history and UI, but that surface is operational evidence only and not a durable release-order ledger.
- Creating GitHub Deployment audit records requires `deployments: write`.
- Creating or updating pull request comments requires `pull-requests: write`.
- Actions artifact upload does not by itself require broader repository write scope; documenting `actions: read` is conservative only when a job also reads Actions run metadata beyond the default artifact upload path.
- OIDC contract fields such as provider audience, provider review timestamp, and provider review evidence reference are repository-controlled checked-in fields, not GitHub-enforced runtime guarantees.

### ASSUMPTION / UNCERTAINTY

- Distinguishing `LOCK_HELD_BY_CONCURRENT_RUN` from `LOCK_STOLEN` is a repository design classification built from repository-known run and frozen-plan identity; GitHub does not expose that distinction as a native platform error class.
- Reconciling a pre-existing live lock to “another legitimate in-flight run” depends on the repository’s own reviewed lock payload format and runbook logic, not on one authoritative GitHub API verdict.
- Requiring OIDC fields such as `providerAudience`, `providerConfigReviewedAt`, and `providerConfigReviewRef` to be non-empty strings is a deliberate repository contract choice for audit quality; external providers do not uniformly enforce every such field for us.

## External-system confirmation for the external-backend and diagnostics follow-up

### FACT

- GitHub Actions concurrency keeps at most one running run and one pending run per concurrency group, and a newer queued run can replace an older pending run.
- GitHub does not expose one reliable native `superseded_by` or equivalent workflow-run field that directly tells which newer run replaced a cancelled pending run.
- Azure Blob Storage supports atomic blob creation with conditional requests such as `If-None-Match: *`; that primitive can back a repository-owned completion-marker blob.
- Azure Blob conditional create is atomic for one blob, but multi-blob publication still needs a repository-owned marker discipline if the design wants `get-by-planDigest` to see either nothing or one complete bundle.
- OCI registries, including GHCR-style OCI distribution surfaces, are content-addressed by blob/manifest digest and do not natively provide an atomic `create-if-absent(planDigest, ...)` operation keyed by an arbitrary business key.
- OCI tag publication is a separate operation from blob and manifest upload, so a repository-owned commit-marker convention is required when the design wants one authoritative “fully committed” visibility point.

### ASSUMPTION / UNCERTAINTY

- A `release-status` view of the “superseding run id” can only be best-effort unless GitHub later documents a stable API field for that relationship.
- An Azure Blob marker-blob pattern is a sound design basis for atomic visibility, but the exact marker payload shape and retry/reconciliation behavior remain repository implementation details until code exists.
- An OCI commit-marker tag pattern is a repository design convention layered on top of OCI distribution behavior, not a native registry transaction primitive.

## External-system confirmation for the v2.33 review-summary design fixes

### FACT

- GitHub environment protection gates job start: an environment-scoped job waits for approval before it starts, and environment secrets / job-scoped OIDC are not usable until the approved job begins running. Once the job starts, however, those credentials are available to that job's steps unless the workflow's own step ordering prevents early use.
- GitHub Deployment metadata is better suited to a split between a short human-readable `description` and richer structured `payload`; repository designs that need detailed reviewed identity should put the canonical JSON in `payload` and keep `description` concise.
- GitHub Artifact Attestations are Sigstore-backed and map naturally to a concrete GitHub Actions release design that wants one reviewed provenance format instead of per-implementation variation.
- RubyGems trusted publishing for GitHub Actions uses audience `rubygems.org`.
- NuGet trusted publishing uses GitHub OIDC and provider-side trust checks over GitHub workflow claims rather than long-lived registry credentials.

### ASSUMPTION / UNCERTAINTY

- I did not independently confirm one timelessly documented GitHub approval timestamp field for environment approvals. The design therefore keeps a Day 0 readiness check for the then-current authoritative approvals or pending-deployments API surface and fails closed if no trustworthy current-run approval timestamp exists.
- I did not independently confirm a stable documented maximum length for GitHub Deployment `description`. Keeping it below roughly 100 characters remains a conservative repository design choice rather than a quoted platform guarantee.
- GitHub deployment `payload` is treated as serialized JSON metadata in current guidance; exact transport/field-shape details should still be re-checked at implementation time.
- GitHub Artifact Attestations are the strongest currently confirmed design fit here, but exact verification mechanics, attestation storage APIs, and trust-root operational details still need implementation-time confirmation against then-current GitHub docs.
- Independent research confirmed that NuGet trusted publishing uses GitHub OIDC, but I saw conflicting secondary references for the exact audience string. This design revision records `api://NuGet` as the reviewed checked-in default from the review summary, and Day 0 implementation must re-confirm the then-current official NuGet documentation before enablement.
- I did not independently confirm provider-side exact-ref claim enforcement for RubyGems trusted publishing, so the design keeps the conservative `workflow-only` default until a reviewed provider check says otherwise.

## External-system confirmation for the v2.34 design-only review fixes

### FACT

- In GitHub reusable-workflow scenarios, the effective workflow identity exposed to OIDC-backed trusted-publishing systems is the called workflow, not the original caller workflow. Keeping official publish jobs as direct jobs in `.github/workflows/official.yml` therefore preserves the intended workflow identity boundary.
- GitHub Environments on GitHub.com provide a single approval from the configured reviewer list; they are not a native “two-of-two” approval primitive. Any true break-glass dual control must therefore come from an additional split-control mechanism outside the environment approval rule itself.
- GitHub Actions concurrency still keeps at most one running run and one pending run per concurrency group, and a newer queued run can replace an older pending run. Pending-slot delay and supersession are therefore platform facts that monitoring and runbooks must treat explicitly.
- The reviewed checked-in audience constants already evidenced for registry trusted publishing in this repository are: npm uses `npm:registry.npmjs.org`, RubyGems uses `rubygems.org`, and NuGet uses `api://NuGet`.
- The conservative cleanup-capability baseline supported by prior reviewed external research remains: `github:release` and buddy GitHub Packages targets are delete-capable; `nuget:official` is unlist-capable; `pypi:official` and `rubygems:official` are yank-capable; `npm:official` is deprecate-only.

### ASSUMPTION / UNCERTAINTY

- The design now records `providerAudience = pypi` as the reviewed checked-in PyPI baseline so the schema is fully specified, but that exact string still needs Day 0 re-confirmation against then-current PyPI trusted-publishing documentation before `pypi:official` is enabled.
- GitHub’s platform semantics for environment approvals and concurrency are stable enough for the current design revision, but exact operational APIs and UI fields for supersession evidence, approval timestamps, and queue diagnostics may still evolve and must be rechecked at implementation time.
- The cleanup-capability matrix above is a conservative repository baseline, not a timeless provider guarantee. Provider-side deletion, yank, unlist, and deprecation behavior can still change over time, so the checked-in runbooks must remain reviewed operational evidence rather than frozen assumptions.

## External-system confirmation for the v2.35 design-only review fixes

### FACT

- GitHub documents `GET /repos/{owner}/{repo}/actions/runs/{run_id}/approvals` as the workflow-run review-history API. The documented response includes review `state`, `comment`, `environments`, and `user`, but not one authoritative approval-grant timestamp field.
- GitHub documents `GET /repos/{owner}/{repo}/actions/runs/{run_id}/pending_deployments` for still-pending deployment reviews. That surface exposes pending-review data such as `wait_timer_started_at`, but it is not the historical approval record after approval completes.
- The documented approvals and pending-deployments APIs are scoped by `run_id`, not by `run_attempt`.
- GitHub’s OIDC/reusable-workflow documentation exposes the called workflow identity through `job_workflow_ref`. Keeping official publish-capable jobs direct in `.github/workflows/official.yml` therefore preserves the intended workflow-identity boundary for trust configurations that key on that called-workflow identity.
- `github.head_ref` reflects only the pull request source branch name. Different forks can therefore collide if they use the same branch name, whereas the repository-local pull request number avoids that collision.

### ASSUMPTION / UNCERTAINTY

- I did not independently confirm any current documented GitHub API field that provides one authoritative attempt-scoped environment-approval timestamp suitable for strict approval-grant freshness enforcement.
- I did not independently confirm one documented GitHub API that attributes approval history to a specific `run_attempt`; the design therefore treats GitHub rerun attempts as unsafe for approval-bearing buddy or official publication paths.
- GitHub’s exact rerun/approval reuse semantics may evolve, so Day 0 implementation must still re-check the then-current documentation before relying on any finer-grained approval audit behavior than the facts above.

## External-system confirmation for the v2.36 review-summary design fixes

### FACT

- GitHub Artifact Attestations are durable GitHub-hosted Sigstore-backed records, and the canonical `github-attestation://<owner>/<repo>/runs/<run-id>/attestations/<attestation-id>` form binds the record to repository and run identity. Treating attestation creation as a persistent external write is therefore the conservative design baseline.
- In GitHub OIDC trusted-publishing scenarios that use reusable workflows, `job_workflow_ref` exposes the called workflow identity used by many trust configurations. Keeping official publish-capable jobs direct in `.github/workflows/official.yml` remains the correct conservative workflow-identity boundary.
- Provider-side exact GitHub ref enforcement is not uniform across npm, PyPI, RubyGems, and NuGet. The design must keep per-target capability facts such as `providerRefClaimSupport`, `providerRefClaimMode`, and `providerSupportsReadOnlyInspection` instead of assuming one universal provider model.
- GitHub Actions concurrency still keeps at most one running run and one pending run per concurrency group, and a newer queued run can replace an older pending run.
- Azure Blob supports atomic create-if-absent semantics for a single blob via conditional create; OCI/GHCR-style registries do not natively provide atomic `create-if-absent(planDigest, ...)` keyed by an arbitrary business key such as `planDigest`.
- GitHub Environments and provider-side trusted-publishing docs do not give a native guarantee that `workflow-only` ref enforcement is protected against provider-side trust-config drift. Repository-owned monitoring and reviewed re-verification are compensating controls, not platform-enforced guarantees.

### ASSUMPTION / UNCERTAINTY

- Exact GitHub Artifact Attestation retention/SLA details and the final Day 0 retrieval APIs still need implementation-time re-check against then-current GitHub documentation.
- RubyGems exact ref-claim enforcement remains unconfirmed, so the design keeps the conservative `workflow-only` default until reviewed evidence says otherwise.
- NuGet trusted-publishing details, especially audience-string specifics and provider-side claim support details, may continue to evolve; Day 0 enablement still needs a fresh check against then-current official NuGet documentation.
- OCI marker-tag / commit-marker publication remains a repository-owned convention layered on top of OCI behavior, not a native registry transaction primitive.

## External-system confirmation for the v2.37 review-summary design fixes

### FACT

- Native GitHub Environment protection is ref-scoped. GitHub documents branch/tag deployment restrictions, but not a native workflow-file allowlist for environments, so any workflow/job on an allowed ref can target that environment name unless repository governance prevents it.
- GitHub rulesets can protect tag namespaces with pattern matching, so custom tag prefixes are protectable repository refs.
- GitHub Actions concurrency allows at most one running and one pending item per concurrency group, and a newer queued item can replace the older pending one.
- Referencing a non-existent GitHub Environment can auto-create it without the required protection rules or reviewer setup.

### ASSUMPTION / UNCERTAINTY

- For reusable-workflow OIDC, `job_workflow_ref` exposes the called workflow identity, but standard claims still describe the caller too; implementations must verify the exact trust-policy behavior they rely on at Day 0 instead of assuming every provider keys only on the called workflow.
- Describing `GITHUB_TOKEN` plus `packages: write` as strictly package-scoped or strictly repo-wide is too coarse. The workflow permission itself is not a per-package constraint, while some package access can still be configured externally on the GitHub Packages side; the design should treat that as reviewed residual risk, not as a workflow-enforced package boundary.
- GitHub’s approval-history surfaces are still not confirmed by this research to provide one clean attempt-scoped approval record suitable for strict `run_attempt` binding; implementation-time re-check remains required.

## External-system confirmation for the v2.38 review-summary design fixes

### FACT

- GitHub Environments are still ref-scoped only: required reviewers, `prevent self-review`, wait timers, and branch/tag deployment restrictions are separate environment settings, and GitHub does not provide a native workflow-file allowlist for one environment name.
- GitHub can auto-create a referenced environment without the intended reviewer or protection settings, so the design must keep “missing environment = hard failure” and pre-create every release environment.
- Workflow-level `concurrency` can use `workflow_dispatch` inputs before jobs begin, and GitHub still allows at most one running plus one pending run per concurrency group; a newer queued run can replace an older pending run.
- A pending run cancelled by GitHub concurrency replacement gets no chance to execute cleanup logic or write durable state, so any durable supersession note must come from some other still-running or external actor.
- GitHub’s documented approval-history surfaces remain scoped by `run_id`, not by `run_attempt`, and the environment approval UI still does not natively render arbitrary workflow outputs before approval.
- GitHub Deployment records still provide a practical reviewed payload carrier (`description` + structured `payload`) for buddy approval binding, even though the approval UI itself is limited.

### ASSUMPTION / UNCERTAINTY

- There is still no independently confirmed documented GitHub API field that gives one authoritative attempt-scoped approval-grant timestamp suitable for strict freshness enforcement; Day 0 implementation must re-check the then-current docs.
- Empty or syntactically invalid dispatch inputs can be isolated from real concurrency groups, but syntactically valid yet unknown `project-key` values may still acquire their own non-authoritative slot before `preflight-validate` rejects them; the design therefore treats preflight hard-fail before privileged work as the safety boundary.
- GitHub does not appear to expose one authoritative `superseded_by` field for a cancelled pending run, so any recorded superseding run id remains best-effort monitor evidence rather than a platform-guaranteed ordering field.
- For reusable-workflow OIDC, `job_workflow_ref` exposes the called workflow identity, but providers may still evaluate multiple claims; implementation-time trust-policy verification remains necessary before relying on any one claim shape.

## External-system confirmation for the v2.39 iter17 design fixes

### FACT

- GitHub Actions concurrency still provides at most one running item plus one pending item per concurrency group, and a newer queued run can replace the older pending run. Cancelled pending runs therefore still cannot be the authoritative writer of supersession state.
- GitHub tag protections can be applied to distinct namespaces, so commit-marker tags for the durable artifact store can be protected separately from the official release-tag and live-lock namespaces and can therefore use a dedicated marker-writer actor class.
- GitHub Environments remain ref-scoped rather than workflow-file-scoped. They are still a credential gate, not a native proof that only one reviewed workflow file may enter the environment.
- Provider-side trusted-publishing capabilities are still uneven across npm, PyPI, RubyGems, and NuGet: exact `ref` enforcement and documented read-only trust-configuration inspection are not uniformly available. For `workflow-only` targets without documented inspection, repository-owned review records and external monitoring remain compensating controls rather than native provider guarantees.

### ASSUMPTION / UNCERTAINTY

- Providers currently marked with `providerSupportsReadOnlyInspection = false` may still expose evolving or undocumented read-only trust-inspection surfaces later. The design therefore treats the external monitor's `workflow-only` drift probe as best-effort and requires the recorded outcome to distinguish `inspection-unsupported` from temporary probe failure.
- External-monitor redundancy, heartbeat-sink availability, and monitor deployment safety remain repository-owned architecture decisions rather than guarantees supplied by GitHub. The design can require active-standby or outage bounds, but Day 0 implementation still needs to confirm the concrete organization-managed platform can satisfy those bounds.
- The exact provider-review evidence artifact format, retention, and retrieval mechanism remain repository-owned conventions layered on top of provider and GitHub primitives. Day 0 implementation must verify that the chosen evidence locator and digest scheme stay practical for the providers actually enabled.

## External-system confirmation for the v2.40 iter18 review-summary design fixes

### FACT

- GitHub Actions still evaluates workflow-level `concurrency.group` before any job executes. Caller-validation jobs therefore cannot retroactively protect a shared release slot; empty or malformed inputs must be isolated in the workflow-level key itself, and durable supersession evidence must come from some surviving or external actor rather than from a cancelled pending run.
- GitHub Actions concurrency still keeps at most one running item plus one pending item per group, and a newer queued item can replace the older pending one. The design may treat this as a stable platform constraint for release-slot serialization, supersession diagnostics, and monitor-owned backfill.
- GitHub tag protection/rulesets can be applied to separate ref namespaces. Commit-marker refs for OCI / GitHub Packages durable-store markers can therefore remain protected independently from official release-tag and live-lock namespaces and can use a dedicated marker-writer actor class.
- GitHub Environments remain ref-scoped credential gates rather than native workflow-file allowlists. They do not by themselves prove that only one reviewed workflow path may mint a target credential, so `workflow-only` trusted-publishing targets still require repository-owned reviewed contracts, evidence, and monitoring as compensating controls.
- Provider-side trusted-publishing capabilities remain uneven across npm, PyPI, RubyGems, NuGet, and GitHub Packages / OCI-style backends: exact provider-enforced `ref` matching and documented read-only trust-configuration inspection are not uniformly available. A target with `providerSupportsReadOnlyInspection = false` therefore cannot rely on native runtime drift detection and must use repository-reviewed evidence plus best-effort external probing instead.
- GitHub does not supply repository-owned monitor redundancy, heartbeat-sink availability, or deployment-gap guarantees for us. Those operational guarantees remain outside the platform and must be supplied by the organization-managed monitor architecture named by the design.

### ASSUMPTION / UNCERTAINTY

- Providers currently modeled with `providerSupportsReadOnlyInspection = false` may later expose new or undocumented read-only inspection surfaces. The design should therefore continue to record `inspection-unsupported` separately from transient probe failure and re-check provider capabilities at Day 0 enablement time for each enabled target.
- The exact machine-readable `providerConfigReviewRef` evidence artifact profile, retention policy, and retrieval path remain repository-owned conventions layered on top of external provider and GitHub primitives. Day 0 implementation must verify that the chosen locator and digest scheme remain practical for every enabled provider.
- The reviewed design now requires active-standby monitoring for `high-assurance` projects, a heartbeat sink with no more than 5 minutes of routine deployment gap, and monitor credential rotation no longer than 90 days. Whether the concrete organization-managed platform can actually satisfy those bounds is still a repository deployment question, not a GitHub platform guarantee.
- The design's dedicated `artifactStoreMarkerWriterActorClass` assumes the repository-owned broker and GitHub-side credential model can mint or broker a distinct commit-marker writer identity without collapsing it back into the normal protected-ref writer. That separation is a reviewed design requirement, but the exact Day 0 actor/token topology remains a repository implementation decision.

## External-system confirmation for the v2.41 iter19 review-summary design fixes

### FACT

- GitHub tag/ruleset pattern matching does not let a single `*` cross `/`. Because the buddy tag format is `refs/tags/buddy/<project-key>/v<version>/<dispatchSha>`, a protecting pattern of `refs/tags/buddy/<project-key>/v*` cannot cover the actual created tag namespace; the pattern must either avoid the extra slash in the tag format or use a cross-segment wildcard such as `v**`.
- GitHub Environments on GitHub.com still provide an approval gate, not a native authenticated two-person control. A single environment approval can be required, but the platform does not itself supply a true “two-of-two” technical control.
- GitHub Actions workflow concurrency is still evaluated before jobs execute, and each concurrency group still behaves as one running item plus at most one pending item, with newer queued work able to replace the older pending item. A run waiting on environment approval can therefore continue holding the shared slot until some external actor cancels it.
- Trusted-publishing provider capabilities remain uneven across npm, PyPI, RubyGems, and NuGet. Exact provider-enforced `ref` matching and documented read-only inspection are not uniformly available, so `workflow-only` targets with `providerSupportsReadOnlyInspection = false` still require repository-reviewed evidence plus external monitoring as compensating controls rather than native provider guarantees.
- For OCI / GitHub Packages style durable stores, the reviewed design still depends on commit-marker visibility as the authoritative `create-if-absent` boundary. Uploads can become visible before the marker write succeeds, so orphan-upload detection and cleanup remain repository-owned operational work rather than something the backend guarantees automatically.

### ASSUMPTION / UNCERTAINTY

- GitHub's exact ruleset wildcard syntax and documentation may continue evolving. The design records the current slash-matching behavior as a platform fact, but Day 0 implementation should still confirm the exact supported pattern syntax before creating production tag rulesets.
- Providers currently modeled with `providerSupportsReadOnlyInspection = false` may later expose new documented inspection surfaces. The design therefore treats the hourly-or-daily drift-probe cadence as a compensating control for today's platform reality, not as proof that these providers will always lack better inspection APIs.
- The exact orphan-upload reconciliation algorithm for OCI / GitHub Packages backends — including grace period, listing primitive, and cleanup mechanics — remains a repository-owned operational design choice layered on top of the backend's visible manifests / package versions plus commit-marker tags.

## External-system confirmation for the v2.42 iter20 review-summary design fixes

### FACT

- GitHub Actions approval and concurrency behavior still impose the same external constraints that this design must model: approval history is keyed to `run_id` rather than a separately reviewable `run_attempt`, workflow-level concurrency is evaluated before jobs execute, and one running item plus at most one pending item per group means a run can continue holding the shared official-release slot while it waits for approval until some external actor cancels it.
- GitHub Environments still act as ref-scoped approval and credential gates rather than as workflow-file allowlists. They can gate entry to `production-*` environments, but they do not by themselves prove that only one reviewed workflow path may mint a protected GitHub credential.
- GitHub tag/ruleset pattern matching still does not let a single `*` cross `/`. Because buddy tags include `/<dispatchSha>` after `v<version>`, the design must use a cross-segment protecting pattern rather than `refs/tags/buddy/<project-key>/v*`.
- Trusted-publishing provider capabilities remain uneven across npm, PyPI, RubyGems, and NuGet. Exact provider-enforced `ref` matching and documented read-only trust inspection are not uniformly available, so `workflow-only` targets with `providerSupportsReadOnlyInspection = false` still require repository-reviewed evidence plus external monitoring as compensating controls rather than native provider guarantees.
- OCI / GitHub Packages durable-store backends still do not provide a native `create-if-absent(planDigest, ...)` boundary keyed to the repository's business identity. The reviewed design therefore relies on repository-owned commit-marker visibility as the authoritative boundary, which means orphan uploads can exist before the marker write succeeds and must be handled operationally.

### ASSUMPTION / UNCERTAINTY

- GitHub's exact documented cross-segment ruleset wildcard syntax may continue evolving. The design currently records the required buddy-tag protecting pattern as `v**`, but Day 0 implementation should still confirm the exact production syntax GitHub documents at that time before creating real rulesets.
- Providers currently modeled with `providerSupportsReadOnlyInspection = false` may later expose new documented inspection or stronger exact-ref enforcement surfaces. The design therefore treats today's hourly-or-daily drift-probe cadence and `workflow-only` compensating controls as current-platform accommodations, not as timeless provider facts.
- The exact orphan-upload reconciliation procedure for OCI / GitHub Packages backends — including listing primitive, grace period, cleanup cadence, and delete/unlist/burn mechanics — remains a repository-owned operational design layered on top of backend-visible uploads plus commit-marker tags rather than a guarantee supplied by the backend itself.
- The concrete organization-managed monitor and broker platform still has to prove at Day 0 that it can satisfy the documented heartbeat, cancellation, and credential-isolation requirements. Those guarantees come from repository-owned operational architecture, not from GitHub's platform alone.
