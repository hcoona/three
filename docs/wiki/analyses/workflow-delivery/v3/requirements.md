# Workflow Delivery v3 Requirements

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This page is the normative product and system requirements baseline for the
clean v3 implementation line. It defines what Workflow Delivery must achieve,
not the internal structure used to achieve it.

The [High-Level Design](./high-level-design.md) describes the architectural
realization. Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

Requirement identifiers are stable traceability anchors. Later design and
acceptance artifacts may refine a requirement, but they must not silently
weaken or reinterpret it.

The current implementation remains delivered but disabled with
`live_enabled: false`. Completed acceptance, provisioning, and retry ceremony
is historical evidence in Git and the append-only log, not evergreen
requirements prose.

## Mission

Workflow Delivery must provide evidence-driven software delivery governance for
a polyglot monorepo.

- CI Qualification determines whether an immutable change candidate satisfies
  every applicable quality obligation.
- Release Delivery independently rebuilds and qualifies an immutable Release
  Unit, reconciles exact destination state, and obtains explicit authorization
  before any traceable external publication.

When requirements conflict, use this priority order:

1. security and correctness;
2. traceability and explainability;
3. evolvability and recoverability; and
4. latency and operating cost.

## Design Assumptions

The design may rely on documented guarantees owned by lower platform layers,
including immutable Git commit identity, protected-environment review, OIDC
claims, and destination API contracts. Workflow Delivery must validate bindings
that it creates, but it must not reimplement a lower layer merely to prove the
lower layer's own contract.

Live registry publication may rely on a documented destination contract that
version creation is atomic and non-overwriting and that exact package state is
sufficiently durable and observable. Destination Adapter acceptance tests must
prove that contract. If a destination, including GitHub Packages, cannot provide
it, live publication to that destination is unsupported or blocked rather than
emulated through an application-level reservation, lock, tag witness, binding
index, or permanent ledger.

If a required guarantee is unavailable at the layer that must own it, the
affected capability is unsupported or blocked. Application logic must not
simulate a weaker substitute and present it as equivalent assurance.

Security controls must address realistic threats and balance risk reduction
against implementation and maintenance cost.

For first-slice GitHub Packages publication, the credential principal is
repository `hcoona/three`. Its effective package-side reach is every package
whose GitHub Actions access grant authorizes that repository. Exact smoke
coordinate, action, artifact, and resource validation is an intended-action and
reconciliation contract; it is not token or package isolation.

`hcoona` is the sole accepted writer and publisher trusted-computing-base member
for this slice. Controls against outsiders, mistakes, and accidental operators
remain required. A malicious accepted writer is outside the constrained threat
model: protected `main`, Environment approval, workflow permissions, the
static-reference policy, and exact action validation must not be described as
limiting that writer to one package. Official npmjs PAT, OIDC, and secret
isolation are separate authority boundaries and remain unchanged.

## System Requirements

### System Scope and Separation

- **WD-SYS-001:** Workflow Delivery must provide CI Qualification and Release
  Delivery as separate business capabilities with independent runtime Plans,
  Evidence, artifacts, state, and verdicts.
- **WD-SYS-002:** The two capabilities must share normalized repository facts,
  target-bound canonical and native NBGV version facts, Build Definitions, and
  mechanism-level adapters where semantics are genuinely common.
- **WD-SYS-003:** Shared mechanisms must not own CI scope policy, release
  channel policy, approval policy, or final business decisions.
- **WD-SYS-004:** Delivery authority must be controlled by an external
  governance boundary. A business workflow may request authority but must not
  grant final authority to itself.
- **WD-SYS-005:** Every authoritative operation must bind an immutable target
  identity and the complete scope of the decision or side effect.
- **WD-SYS-006:** Unknown, unclassified, incomplete, or conflicting required
  scope must block a successful decision.

### CI Qualification

- **WD-CI-001:** CI must identify the exact candidate tree under evaluation,
  including the applicable base, head, tested merge, merge-group, or push
  revision identity.
- **WD-CI-002:** CI must map changed paths through discovered Project Nodes,
  dependency relationships, and global inputs to the affected Release Unit
  closure before execution.
- **WD-CI-003:** CI must close the complete Qualification Target before
  execution. Executors must not add, remove, substitute, or downgrade planned
  obligations.
- **WD-CI-004:** CI must build every publishable artifact variant of each
  affected Release Unit.
- **WD-CI-005:** CI must execute every applicable required quality obligation
  and distinguish required outcomes from advisory outcomes.
- **WD-CI-006:** CI success requires admitted, satisfied Evidence for every
  required obligation. Missing, skipped, canceled, timed-out, unknown, and
  conflicting outcomes must not become success.
- **WD-CI-007:** CI must produce an immutable, explainable Final Decision and
  project the latest authoritative result through the required GitHub check.
- **WD-CI-008:** CI must not authorize Release or perform publication side
  effects.
- **WD-CI-009:** During first-slice coexistence, v3 exposes only a shadow
  pull-request incremental check for slice-relevant changes and a
  non-authoritative manual `slice-validation` purpose. Manual slice validation
  covers the complete `hcoona-release-smoke-npm` slice and must not be named,
  summarized, or projected as canonical repository-wide full validation. It is
  not a Ruleset required check, the shadow pull-request check does not replace
  v1 required CI, and v1 and v3 must not produce parallel authoritative
  Decisions. Canonical explicit or scheduled full validation remains defined by
  the CI MLD and is deferred until the Repository Model and policies cover every
  active Project Node, Release Unit, and repository obligation.
- **WD-CI-010:** Whenever root HK SourceTreeConformance runs, its lightweight
  static-reference policy must run in the caller-selected `index` or `worktree`
  feedback mode. Separately, the expensive v3 control package pytest suite is
  path-selected for changes to the v3 control package/catalogs/tests,
  first-slice descriptors, exact first-slice Release policy, any v3 workflow
  consumer, direct Python workspace/lock input, or HK configuration/helpers.
  Manual `slice-validation` runs that suite unconditionally. Unrelated
  product-source changes alone must not select the pytest step. Both remain
  internal root-HK steps, not separate CI obligations, Evidence records, or
  jobs.

### Release Delivery

- **WD-REL-001:** Release must begin from an explicit Release Intent for one
  Release Unit, immutable full Git commit target, and release channel. Official
  Product Identity is channel, Release Unit, and canonical NBGV version.
  Official Release Execution Identity is Official Product Identity plus target.
  Buddy Release Execution Identity is channel, Release Unit, and target. An
  admitted live Attempt is identified by its Release Execution Identity and
  unique `workflow_run_id`. `github.run_attempt` is a required platform guard
  and diagnostic only; it is not Product, Execution, Attempt, record, or
  artifact identity. External package coordinate remains channel, destination,
  package, and version and is not a Product or Execution Identity field.
- **WD-REL-002:** Release must verify target and channel eligibility through
  Delivery Governance before live publication can be authorized.
- **WD-REL-003:** Each Release request must select live release or release
  simulation before live eligibility, Product or Execution lookup, coalescing,
  admission, or Attempt creation. For that purpose, Release must compile one
  authoritative same-revision Repository Model Snapshot bound to the request,
  exact target, producer, and control revision. For normal Buddy, the selected
  same-repository ref resolves to one exact SHA; that SHA supplies both the
  workflow/control revision and the Release target. The architecture must not
  model the workflow ref, selected ref, workflow SHA, and target as independent
  identities when they are the same revision.
  The Snapshot must close descriptors, Project Nodes and dependencies, Build
  Definitions, modeled variants and outputs, target-bound canonical and native
  NBGV facts, and complete build and artifact scope. Inputs from CI, another
  request or purpose, or a prior Attempt are inadmissible. NBGV facts that
  depend on Git history require complete exact-target ancestry and tags.
  Attempt planning must reuse this Snapshot, select and freeze its native
  projections, and derive the deterministic publication basis without
  recomputation or fallback. The Qualification Snapshot freezes build,
  qualification, destination, and resource-key derivation scope. The
  Publication Snapshot later freezes actual artifacts, observations, and the
  first slice's exact zero-or-one Publication Action. Neither Snapshot binds
  `github.run_attempt`.
- **WD-REL-004:** Release must not consume CI Plans, Evidence, artifacts,
  checks, or verdicts as Release qualification inputs.
- **WD-REL-005:** CI and Release must use the same Build Definition for the same
  artifact variant, while materializing separate builds for their distinct
  immutable targets and purposes. Each Plan and Build Request must select and
  freeze the exact required native version projection emitted by Repository
  Model compilation. A Build Adapter may apply and verify that value but must
  not recompute NBGV, derive another version, or use fallback version fields.
- **WD-REL-006:** The first-slice npm Release Unit must produce bit-for-bit
  deterministic bytes for the same target, frozen inputs, Build Definition,
  and toolchain. The system is not required to certify this property with a
  duplicate build. A Release Unit that cannot meet this contract is unsupported
  by this slice; publication resume from a sealed artifact for nondeterministic
  units requires a future explicit design.
- **WD-REL-007:** For an action-bearing first-slice Publication Snapshot,
  Release must prepare one immutable Approval Bundle before the Environment
  wait. It closes the target and selected ref, Qualification Decision,
  Publication Snapshot, artifact identities and digests, manifest and lifecycle
  information, and the exact Publication Action with its complete
  mutable-resource keys. The Attempt may publish only after the Approval job
  receives the required Environment approval, freshly validates Governance,
  strictly admits that complete closure, and durably emits one Publication
  Authorization. The Publication Authorization must bind all current-Attempt
  Governance, action, artifact, and resource inputs and must not bind
  `github.run_attempt`.
  A Publication Snapshot with zero actions represents successful
  `exact-satisfied` reconciliation. Observation or an explicit no-op
  reobservation may use only the minimum read-only destination authority
  permitted by WD-REL-008. The path requires no Environment approval,
  Publication Authorization, publisher, destination write or publication
  credential, Publication Capability, mutation marker, Publication Result, or
  Receipt.
- **WD-REL-007A:** After successful Qualification and before publication,
  Observation, Publication Snapshot materialization or transport, approval
  waiting or rejection, or platform cancellation may stop the Attempt without
  side effects. If the read-only Finalizer runs, it may record a replayable
  `failed-before-publication` disposition only when current-DAG facts prove
  that the publisher never started, no mutation marker, Publication Result, or
  Receipt exists, and no contradictory downstream lineage exists. A missing
  artifact or record alone is insufficient. If a Publication Snapshot was
  durably persisted, the Outcome must retain that lineage. Otherwise it must
  bind the exact successful Qualification Decision and preserve
  publication-preparation uncertainty. The Outcome sets `possibly_mutated`
  false and requires a new manual dispatch. GitHub cancellation or finalizer
  transport failure may leave no durable Attempt Outcome; finalization is best
  effort, not guaranteed.
- **WD-REL-008:** Build and qualification must receive no destination
  credential or publication capability. Destination Observation may use public
  APIs or the minimum read-only destination authority required for exact-state
  readback, but it must receive no destination write authority, PAT,
  `id-token: write`, Approval Environment, or publication capability. Release
  must obtain short-lived, destination-specific Publication Capability only in
  the action-bearing publisher after Qualification, Observation, Approval
  Bundle admission, Environment approval, and durable Publication
  Authorization. The Approval job has no publication capability and references
  the one literal Approval Environment
  `workflow-delivery-v3-buddy-approval`. The publisher has an ordinary success
  dependency on that job. It is the only step-running job with effective
  `packages: write`; a `uses`-only reusable-workflow caller may declare
  `packages: write` solely as a non-elevating ceiling and must have no steps or
  direct token use. Before mutation, the publisher must strictly validate the
  Publication Authorization and its exact action, artifact, resource, and
  Governance bindings, then perform a final fresh Governance check. The first
  slice has no Capability Environment, Capability Admission Decision,
  capability group, group manifest, or group result bundle.
- **WD-REL-009:** Immediately before the first mutating destination operation,
  the publisher must durably persist a mutation-may-have-started marker.
  Marker persistence failure blocks mutation. After the attempted or completed
  operation, it must durably persist one Publication Result that binds the
  Publication Authorization and exact action and records the destination
  outcome. A successful `published` Result must embed exactly one Receipt. A
  controlled failed Result after the marker may omit the Receipt and must
  preserve mutation classification and diagnostics. A missing durable Result
  after the marker means the Attempt is unknown and possibly mutated. A later
  dispatch must reobserve the destination before deciding whether any action
  remains. A read-only Finalizer may emit an explainable Attempt Outcome when
  it runs, but the architecture does not guarantee that finalization survives
  cancellation or transport failure.
- **WD-REL-010:** GitHub Actions run history is diagnostic only. Normal-Live
  admission, publication, and finalization must not depend on exhaustive
  discovery or custom admission of prior runs, attempts, artifacts, or
  outcomes. The architecture must not claim an authoritative aggregate
  Execution state, exhaustive append-only Attempt lineage, or history-based
  publication admission. Recovery authority comes from the current Release
  Intent, current Attempt records, and fresh destination observation.
- **WD-REL-011:** For the named live Buddy slice, after exact target pinning and
  request-local Repository Model compilation but before Execution lookup,
  concurrency, or Attempt creation, Release must perform an exact-target
  static-reference check and independently read protected Governance from
  `refs/heads/main`.

    The new-version bounded static-reference policy proves only that its closed
    supported catalog contains no prohibited direct static reference. Its
    canonical source kinds are exactly `git-target`, `index`, and `worktree`.
    `git-target` enumerates and reads exact blobs from an explicit full commit
    SHA; only `git-target` is admissible Live Eligibility evidence. `index`
    enumerates and reads stage-0 Git index entries for staged or pre-commit
    candidate feedback. `worktree` enumerates tracked plus eligible untracked
    paths and reads filesystem bytes for manual developer feedback. Every result
    binds its source kind. Index or worktree bytes must never be represented as
    `HEAD` or commit identity. The supported catalog covers manifests,
    lockfiles, workflows, dependency and configuration files, composite
    actions, and conventional install/bootstrap automation. It rejects direct,
    versioned, aliased, workspace, and subpath forms of the exact smoke
    coordinate. In manifest and lock dependency positions, it also rejects
    direct `file:`, `link:`, or `workspace:` paths that resolve to the known
    producer root. It must not reject the producer path globally because
    workflows may legitimately build that source. The only allowed package-name
    occurrence is the top-level `name` in `package.json` at an exact known
    producer path. Findings mean prohibited references, not proven runtime
    consumers. Encoded or split construction, arbitrary runtime downloads,
    external configuration, and novel layouts are explicit non-goals.

    Static-reference evidence must bind schema and result, source kind, exact
    target when applicable, policy ID and digest, and sorted findings. File,
    surface, or finding counts may be diagnostics only. Whole-file digest
    exceptions, fixed inventory counts, scanned-surface digest authority,
    parser/dataflow/interpreter claims, trigger-catalog authority, and claims of
    universal consumer absence are forbidden.

    The immutable Governance source contract is repository `hcoona/three`, ref
    `refs/heads/main`, and path
    `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`. The
    attestation must identify sole accepted writer/publisher `hcoona`, bind the
    policy and package, record the relevant access inspection, include issuer and
    inspection time, acknowledge limitations, expire within 90 days, and carry
    top-level `live_enabled`. Eligibility must bind repository, ref, path, and
    attestation blob/content identity or an explicit attestation generation. It
    must not require the complete resolved `main` commit to remain equal; unrelated
    `main` commits do not invalidate an Attempt. Anti-rollback is mandatory:
    every commit touching the protected Governance path after the eligibility
    read invalidates that Attempt even if later bytes revert. Restoration
    requires a new dispatch and Attempt. Missing, unreadable, malformed, expired,
    disabled, path-touched, or binding-mismatched Governance blocks. A false flag
    blocks fresh admission and the publisher's final fresh check but cannot
    revoke a publisher already past that check. Official npmjs PAT, OIDC, and
    secret isolation remain separate and unchanged.

    The replacement normal-Live implementation and activated Governance use
    exact schema
    `workflow-delivery/v3/normal-live-governance-attestation-v1`. This
    intentionally incompatible schema is the selected-ref control compatibility
    fence. A selected revision must require it exactly; a superseded parser must
    fail before Release Execution lookup, Attempt creation, or any Environment
    job. This does not substitute protected-main control code for the selected
    revision.

### Buddy and Official Channels

- **WD-CHN-001:** Buddy must produce distributable, non-authoritative previews
  through channel, destination, package-coordinate, and capability policy
  distinct from Official. Official credentials and destinations must remain
  separate. Isolation does not require Buddy to alter the canonical
  product-version string. For the first-slice GitHub Packages token, distinct
  intended coordinates do not imply package-level credential isolation.
- **WD-CHN-002:** Official Product Identity must use the canonical NBGV version
  for a target commit that Delivery Governance recognizes as authoritative.
  Official ecosystem publication and dry-run must use the exact frozen native
  NBGV projection required by that ecosystem, such as `npmPackageVersion`,
  unchanged.
- **WD-CHN-003:** Buddy artifacts, Evidence, Decisions, and Receipts must not be
  promoted or reclassified as Official.
- **WD-CHN-004:** Non-authoritative branches may exercise Official dry-run
  behavior but must branch into release simulation before live eligibility or
  identity lookup. Simulation must first compile and validate its
  purpose-discriminated Snapshot binding request identity, workflow run ID and
  run attempt, target, channel, Release Unit, canonical and native version
  facts, producer, and control identity. Only then may it derive the separately
  namespaced, request-scoped Simulation Identity from those bindings. The
  Repository Model Snapshot must not bind that not-yet-created Identity.
  Simulation reruns remain distinct simulation passes and retain their
  `github.run_attempt` binding; the normal-Live first-attempt-only contraction
  does not apply to simulation. Simulation must not contain or acquire a live
  Product, Release Execution, or Attempt identity, Publication Authorization,
  publication capability, Receipt, or mutation. It may emit hypothetical
  requirements and actions and a Simulation Outcome. Shared schemas may be
  reused only with an explicit purpose discriminator and cross-purpose
  admission rejection.
- **WD-CHN-005:** The `hcoona-release-smoke-npm` live Buddy slice may publish
  only the dedicated disposable smoke coordinate as its intended normal action.
  The bounded static-reference policy must find no prohibited direct reference
  in its supported repository catalog, but this is not proof that no runtime
  consumer exists and does not constrain the repository `GITHUB_TOKEN` to that
  package. Every package granting Actions access to `hcoona/three` is in the
  effective publisher blast radius. Former v1 Buddy projects remain unsupported
  until explicitly migrated. Official, future Buddy destinations, and
  production packages do not inherit this exception.

### Governed Control Code

- **WD-AUTH-001:** CI decision code must come from the tested candidate
  revision. Live Release decision code must come from the exact selected target
  revision. Official requires a protected authoritative target. For normal
  `hcoona-release-smoke-npm` Buddy, `workflow_dispatch` may select any
  same-repository ref. That ref resolves to one exact SHA, and the same SHA
  supplies the workflow, control, Planner, Finalizer, publisher, and Release
  target. Dry-run Release simulation uses its selected simulation revision and
  receives no approval or live publication capability.
- **WD-AUTH-002:** Changes to CI or Release planning, finalization, workflow
  control code, authoritative record shapes, minimum policy, executable
  Providers, Adapters, compilers, authenticated clients, static catalogs,
  capability declarations, or cross-revision compatibility code must require
  Governance-configured owner review before merge or live Release eligibility,
  except that solely for the confirmed first-slice Buddy live Attempt,
  owner-reviewed eligibility is waived for the selected-ref workflow, Planner,
  Finalizer, Providers, Adapters, compiler, authenticated clients, static
  catalogs, capability declarations, and publisher. CI and Official, future
  Buddy and production scopes, protected cross-revision compatibility code, and
  Break-Glass Remediation remain owner-reviewed or separately governed.
- **WD-AUTH-003:** Merging a reviewed control-code change makes that code
  eligible only as part of the resulting new candidate or Release target
  revision. No independent runtime promotion protocol is required.
- **WD-AUTH-004:** CI execution must not receive publication capability, and
  Release execution must not receive live capability until the target revision
  satisfies applicable policy. Official requires protected-ref and Environment
  policy. An action-bearing first-slice Buddy Attempt requires the exact
  Publication Snapshot and successful human approval through
  `workflow-delivery-v3-buddy-approval`, but not protected-ref eligibility. An
  exact-satisfied zero-action Attempt requires no approval or publication
  capability.
- **WD-AUTH-005:** Delivery Governance must control protected target
  eligibility, control-code review, protected environment review, OIDC and
  destination trust, capability grant and revocation, and Break-Glass
  Remediation approval. Protected target eligibility and owner-reviewed live
  control code remain mandatory for Official but are explicitly waived only for
  the named first-slice Buddy exception. These controls remain effective against
  outsiders and accidental operation; they are not claimed to constrain the
  malicious accepted writer.
- **WD-AUTH-006:** The first slice uses exactly one authority-bearing GitHub
  Environment: `workflow-delivery-v3-buddy-approval`, with its required
  reviewer and Environment-scoped marker. There is no first-slice Capability
  Environment. A generic Environment Profile abstraction is deferred until a
  concrete second policy demonstrates independent semantics. A future OIDC
  channel may introduce a channel-specific Environment only when external
  destination trust validates that Environment's OIDC claims.

### Trust and Capability Isolation

- **WD-SEC-001:** Runtime execution of target-controlled code and possession of
  publication authority must not coexist in one trust boundary except for the
  explicitly accepted `hcoona-release-smoke-npm` live Buddy GitHub Packages
  exception defined below.
- **WD-SEC-002:** Authoritative planning, Evidence Admission, and final
  decision logic must not execute target-defined project/build code or hold
  publication credentials. It uses same-revision control code; for the named
  Buddy exception that control code is branch-controlled and unreviewed by
  design.
- **WD-SEC-003:** Publication execution must consume only verified immutable
  artifacts and a fully materialized, authorized publication description. It
  must not execute target-defined product/build code. The named first-slice
  Buddy exception permits target-revision control and publisher code in the
  side-effect job, but does not permit that job to execute target-defined
  product/build code.
- **WD-SEC-004:** Delivery Governance must scope publication credentials to the
  narrowest destination and identity boundary the platform supports. The
  independent trusted side-effect executor must additionally enforce the exact
  authorized Snapshot, artifact, and action set for normal destinations. The
  first-slice Buddy target-revision publisher is an explicit trust exception: it
  validates those bindings by contract but is not an independent adversarial
  enforcement boundary. For GitHub Packages, the credential principal is
  repository `hcoona/three`; every package granting that repository Actions
  access is within effective token reach. Exact action validation does not
  narrow that package-side grant.
- **WD-SEC-005:** Failure to obtain the required OIDC identity or Publication
  Capability must block the affected side effect. No long-lived credential
  fallback is permitted. Official npmjs PAT, OIDC, and secret isolation remain
  separate from the first-slice GitHub Packages exception.

### First-Slice Buddy Accepted Risk Exception

- **WD-SLICE-001:** Live Buddy for `hcoona-release-smoke-npm` may target any
  same-repository branch or ref selected by `workflow_dispatch`. The selected
  ref resolves to one exact commit; that commit supplies workflow, control,
  Planner, Finalizer, publisher, and Release target. Protected Governance is
  fetched independently from `main`. The architecture must not substitute
  protected-main control code for the selected same-revision stack. The
  selected revision must strictly admit the active exact Governance schema
  `workflow-delivery/v3/normal-live-governance-attestation-v1`. A ref whose
  control does not implement that contract is unsupported and must fail before
  Release Execution lookup, Attempt creation, or any Environment job.
- **WD-SLICE-002:** Every such Attempt must seal its exact Publication Snapshot
  and, when that Snapshot contains an action, prepare the immutable Approval
  Bundle before any Environment wait. For that action-bearing path, the one
  Approval job references `workflow-delivery-v3-buddy-approval`, validates the
  resolved exact marker value as its first authority-critical executable check,
  and has no publication capability. Same-name repository or organization
  variable absence is authenticated native
  Governance/provisioning/activation readback and attestation evidence, not a
  fact the job can prove. After human approval, it freshly validates protected
  Governance, including path-touch anti-rollback, strictly admits the Approval
  Bundle, Publication Snapshot, artifact, and exact action/resource closure,
  and durably emits the complete Publication Authorization.
  Reviewer-visible context must include target SHA and selected ref, exact
  package coordinate, artifact digest and manifest, lifecycle scripts, and the
  exact action. For repository `hcoona/three` and this package, sole accepted
  writer and reviewer `hcoona` may self-approve with
  `prevent_self_review: false`. This is explicit operator confirmation, not
  independent review or a security boundary.
- **WD-SLICE-003:** The target-revision publisher may receive only the
  short-lived repository `GITHUB_TOKEN` with effective `packages: write`. It
  must receive no PAT fallback and no `id-token: write`. Workflow-level
  permissions remain empty or read-only. A `uses`-only caller may declare
  `packages: write` solely as the reusable-workflow ceiling and must have no
  steps or direct token use. The called publisher is the only step-running job
  with effective package write. Every other job must be explicitly
  non-publishing, and the called workflow cannot elevate beyond the caller
  ceiling.
- **WD-SLICE-004:** Environment approval is the trust elevation for this
  branch-controlled publisher. Approval is neither cryptographic validation nor
  independent semantic validation of the target code, artifact, lifecycle
  scripts, or action. `hcoona` is the sole accepted writer/publisher TCB member.
  Controls against outsiders, accidental operators, and mistakes remain
  required. A malicious accepted writer is not constrained by protected
  `main`, the Approval Environment, workflow permissions, static-reference
  policy, or exact action validation, and the architecture must not claim
  otherwise.
- **WD-SLICE-005:** The GitHub Packages credential principal is repository
  `hcoona/three`, and its full package-side Actions grant reach defines the
  effective publisher blast radius. Every package that grants this repository
  access is in scope, whether or not the normal Publication Action names it.
  Exact coordinate, action, artifact, and resource validation governs intended
  behavior and reconciliation only; it is not token/package isolation.
  Official npmjs credentials and destinations remain separately isolated.
- **WD-SLICE-006:** The normal intended-action and reconciliation contract is
  bound to the exact dedicated smoke coordinate and GitHub Packages action.
  Normal publication permits no delete, restore, permission, visibility, or
  admin action; deletion or restore requires Break-Glass handling and explicit
  human Governance inspection. The bounded static-reference result required by
  `WD-REL-011` must be clean, but it proves only that no prohibited direct
  static reference was found in the supported catalog. It does not prove
  absence of every runtime consumer or constrain `GITHUB_TOKEN` reach. Any
  future Buddy destination requires its own threat and cost decision.
- **WD-SLICE-007:** `hcoona` is the sole accepted writer and publisher TCB
  member for this slice. External or fork contributors and actors without
  repository write remain outside that TCB. Any added Write, Maintain, or Admin
  actor, reviewer change, or relevant package/repository/Manage Actions access
  change requires `live_enabled: false` and a new Governance decision.
  Governance must re-attest at least every 90 days. Protected review, merge, and
  fresh-read latency make disablement a bounded operational response rather than
  instantaneous revocation, and a publisher already past its final fresh check
  may complete.
- **WD-SLICE-008:** Normal Live implementation and activation are independent
  deliveries. The delivered implementation remains present with
  `live_enabled: false`. Final activation uses one small protected Activation
  PR. There is no separate Preparation PR, repository-wide `main` freeze,
  pre-pinned Activation SHA, or activation tag. Activation remains blocked
  until the first-slice destination satisfies the non-overwriting mutation
  proof in WD-OPS-002A and fresh repository retention readback satisfies
  WD-RET-002.
- **WD-SLICE-009:** Live Buddy and Official simulation must qualify the built
  npm tarball with distinct `node/npm-artifact-contents-v1` and
  `node/npm-install-import-v1` obligation identities. They may execute in one
  tarball-dependent physical job, but must emit two separately admitted
  Evidence records and cannot finalize qualification successfully unless both
  are satisfied.
- **WD-SLICE-010:** Every authoritative normal-Live job must fail closed unless
  `github.run_attempt == 1`. This includes eligibility and planning jobs, the
  Approval job, exact-satisfied no-op finalization, publisher, and read-only
  Finalizer. An entry-only guard is insufficient because GitHub supports partial
  reruns. `run_attempt` remains a platform invariant and diagnostic; it must not
  be copied into normal-Live domain identity, Publication Authorization,
  records, or artifact bindings. GitHub rerun commands are unsupported for
  normal Live.
- **WD-SLICE-011:** The literal Approval Environment
  `workflow-delivery-v3-buddy-approval` must exist with required reviewer
  `hcoona`, the confirmed self-review setting, and its exact Environment-scoped
  marker. Authenticated native Governance/provisioning/activation readback and
  attestation must establish the absence of same-name repository or
  organization variables. Under that externally verified precondition, the
  Approval job must validate the resolved marker value as its first
  authority-critical executable check so accidental implicit creation of the
  named Environment fails closed. The job cannot determine the marker's source
  scope or itself prove broader-variable absence. The marker is a configuration
  sentinel, not proof of native reviewer, bypass, branch-policy, secret,
  credential, or Environment-identity settings. Delivery Governance must retain
  the authenticated readback and attestation evidence. No Capability
  Environment is part of the first-slice authority model.
- **WD-SLICE-012:** The first proving run is dispatched from then-current
  protected `main` after the single Activation PR merges. Dispatch must use an
  explicitly supported REST API version whose success response contains
  `workflow_run_id`. The operator must validate the response schema and read
  back the returned workflow and run identity, actor, `workflow_dispatch`
  event, exact actual head SHA, `refs/heads/main`, and
  `github.run_attempt == 1`. A lost response or ambiguous correlation triggers
  read-only reconciliation and never a blind redispatch. Later normal Buddy
  runs may again select arbitrary same-repository refs whose selected-revision
  control strictly admits the active Governance schema. Before activation,
  exact repository inspection and compatibility fixtures must prove that every
  retained dispatchable ref either implements the one-Environment contract or
  rejects the active schema before any Environment job or deployment.

### Evidence, Decisions, and Explanation

- **WD-EVD-001:** Evidence Admission must verify exact ownership, target,
  obligation, artifact, attempt, and integrity bindings without rerunning the
  quality command.
- **WD-EVD-002:** Final Decisions must be append-only. GitHub checks and human
  summaries are projections, not the authoritative audit record.
- **WD-EVD-003:** CI explanation must connect changed paths, Project Nodes,
  dependency relationships, Release Units, variants, obligations, Evidence,
  outcomes, and the verdict.
- **WD-EVD-004:** Release explanation must connect target, version, channel,
  artifacts, destinations, observations, actions, Receipts, authority,
  authorization, outcome, and allowed operator actions.
- **WD-EVD-005:** Authoritative Plans, Evidence, Decisions, artifact identities,
  and Receipts must persist before a later stage relies on them. Optional
  telemetry may fail without changing the business verdict.
- **WD-EVD-006:** Actions artifact names are non-authoritative indexes. Names
  must be collision-safe within the workflow run and use overwrite-disabled
  immutable transport. Producers must capture artifact ID, digest, and URL.
  Current-Attempt consumers fetch only by artifact ID and validate record kind,
  producer, `workflow_run_id`, target, purpose, payload identity, and digest.
  For normal Live, `github.run_attempt` is not a record or artifact binding
  because every authoritative job independently requires attempt 1. Other
  contexts, including simulation, retain their own run-attempt contracts. Name
  fallback, latest-artifact selection, and history-derived authority are
  forbidden. Native Actions history may be shown only as diagnostics.

### Observation, Retry, and Recovery

- **WD-OPS-001:** Every Release Attempt must observe all destinations before
  requesting publication capability. Observation must classify each logical
  projection atomically against snapshot-bound desired projection state, not
  Product or Execution Identity. Desired state must include the exact destination
  coordinate, expected ownership, immutable in-package target witness, target
  binding, qualified artifact bytes or digest, and every required destination
  routing projection. For first-slice npm this includes the exact
  dist-tag `buddy-sha-<40-lowercase-target-sha>` mapped to the frozen native
  version. The mutable tag is routing, not provenance. Desired state is derived
  from the Qualification Snapshot and admitted artifacts.
  The Observation Record must bind the Release Attempt, logical projection,
  immutable desired-state basis, and canonical remote response and observed
  facts, including observed artifact digests. It must not bind a future
  Publication Snapshot; that later Snapshot must seal admitted Observation
  Records with resulting desired state and materialized actions.
- **WD-OPS-002:** Absent destination state may publish, exact satisfied state
  must skip the side effect, and partial, unknown, conflicting, or unprovable
  state must fail closed. An absent coordinate with no retained operational
  lineage is a legitimate initial-publication state and is not inherently
  unprovable. Publication must use destination create-only or create-or-exact
  semantics that are atomic and non-overwriting. Pre-observed exact state
  produces no action. At mutation linearization, absent state may be created;
  a concurrently created exact state may satisfy an atomic create-or-exact
  action without mutation; and differing state must fail without mutation.
  Release must never implement create-or-exact as read-then-upsert, overwrite,
  or delete-and-recreate. A pure create-only destination may report conflict and
  rely on a new dispatch. Successful durable creation establishes the observable
  package binding, while a pre-mutation failure reserves nothing. A manual
  Release Intent may authorize read-only reconciliation; exact pre-observed
  state may finalize as `success` with `exact-satisfied` disposition without
  approval or publication lineage. Immediately before that success, the
  zero-action path must repeat protected Governance ancestry, path-touch,
  blob/content, expiry, and `live_enabled` validation and bind the fresh proof
  into no-op finalization.
  Any first-slice mutation primitive that invokes npm must set the
  highest-precedence `fetch-retries=0`; automatic retries of its mutating
  registry request are forbidden. This is necessary but insufficient for
  conditional tag safety. Bounded read-only observation retries remain
  permitted.
- **WD-OPS-002A:** The first-slice version-and-tag projection may become an
  action only when a documented destination primitive is proven to preserve
  the complete atomic non-overwriting contract at mutation linearization.
  Acceptance must observe the desired version and target-derived tag absent,
  establish a different version under that tag after Observation, invoke the
  candidate primitive, and prove that it fails without creating the desired
  version or moving the competing tag. Standard `npm publish --tag` provides no
  conditional tag assignment and is not an admitted normal-Live primitive.
  GitHub Packages admission requires this proof against a disposable package
  under separate authorization; a synthetic client test alone cannot establish
  destination support.
  Repository concurrency, another pre-mutation read, or post-action readback
  does not close that race. The GitHub Packages first slice must remain
  `live_enabled: false` until a reviewed design identifies a supported
  primitive and this acceptance passes.
- **WD-OPS-003:** Release retry is a new manual dispatch. GitHub
  `Re-run all jobs` and `Re-run failed jobs` are unsupported recovery protocols
  for normal Live.
- **WD-OPS-004:** Every retry receives a new `workflow_run_id` and reruns
  request-local Repository Model compilation and live eligibility. A retry
  rejected or coalesced before admission creates no Attempt. If it survives
  coalescing and is admitted, it creates a new Attempt for the same
  deterministic Release Execution Identity and reruns planning, build,
  qualification, destination observation, authorization when an action
  remains, and reporting. It must not reuse an older Attempt's Repository
  Model, Qualification, artifacts, Publication Snapshot, Environment approval,
  or Publication Authorization. For the first-slice npm unit, identical
  target, frozen inputs, and toolchain must reproduce identical bytes. If
  existing destination bytes differ, the Attempt fails closed into
  reconciliation and separately authorized remediation. Nondeterministic
  Release Units are unsupported until a future sealed-artifact
  publication-resume design exists.
- **WD-OPS-005:** A control-code fix creates a new candidate or Release target
  revision. A later dispatch of an older target must continue using that
  target's original control code.
- **WD-OPS-006:** Future multi-destination publication must use append-only Saga
  semantics. A successful destination must not be automatically rolled back
  solely because another destination fails. The first slice has one action.
- **WD-OPS-007:** Reconciliation must be exceptional handling for destination
  state that cannot safely proceed through normal observation and a new
  dispatch.
- **WD-OPS-008:** Break-Glass Remediation must be separately approved, use
  expected-state checks and scoped capability, and record append-only
  before-and-after state without rewriting the original Release history.
- **WD-OPS-009:** Every first-slice npm tarball must contain canonical immutable
  `workflow-delivery/provenance.json` covered by the package bytes and artifact
  digest. It must bind target commit, Release Unit, canonical and native NBGV
  facts, Build Definition, catalog and control digests, purpose, and schema, but
  exclude run and Attempt identifiers so repeated builds of one target remain
  reproducible. Isolated pack staging must deterministically update and verify
  the staged `package.json` `files` allowlist so it preserves the existing
  intended package entries and includes `workflow-delivery/provenance.json`;
  this must not require mutation of the source working-tree manifest. The npm
  artifact-content qualification must inspect the packed tarball and fail
  unless the witness is present at exact tar entry path
  `package/workflow-delivery/provenance.json` and its extracted canonical bytes
  equal the frozen witness input. Install/import qualification must validate
  the installed copy against the same frozen input. Remote exact-state
  observation must download and hash the tarball, extract the witness, and
  validate it; a local sidecar is insufficient. Equal package/version bytes
  claimed for a different target are conflicting, not exact.

### Concurrency

- **WD-CON-001:** CI may cancel runs superseded by a newer candidate identity.
- **WD-CON-002:** Release Execution and request coalescing must use the complete
  Release Execution Identity. Live-action resource-key sets are independently
  derived as complete deterministic mutable-resource keys declared by each
  Destination Adapter, not from Product or Execution Identity. Attempt planning
  must validate and bind those keys in Publication Snapshots and action
  manifests, Receipts, and validation, and overlapping live actions must
  serialize on them. When the available platform supports equality concurrency
  groups rather than arbitrary set-overlap locking, an Adapter may additionally
  define a conservative deterministic serialization projection. The projection
  may intentionally over-serialize, but every pair of actions whose complete
  key sets overlap must resolve to the same enforced group; the projection must
  never replace or weaken the complete frozen key set. Package mutation keys
  must include the exact External Package Coordinate plus any additional
  Adapter-required keys. Any admitted first-slice version-and-tag publication
  is one compound action whose complete key set includes both the External
  Package Coordinate and destination/package/dist-tag mutable resource; no
  separate normal tag mutation is permitted. No such primitive is currently
  admitted under WD-OPS-002A. Its GitHub concurrency group uses the
  conservative shared destination/package projection so every action touching
  the same destination and npm package name serializes, including actions with
  different target-derived tags. Non-package keys and any safe serialization
  projections are defined by the Destination Adapter contract. Missing,
  unknown, incomplete, conflicting, or unenforceable required keys or
  projections must block live publication. Request-local Repository Model
  compilation occurs
  before execution concurrency. The surviving concurrency-scoped caller then
  invokes one same-revision reusable live-Attempt workflow and holds the Release
  Execution identity slot through terminal workflow state, including the
  read-only Finalizer when it runs.
- **WD-CON-003:** An in-progress Release execution must not be automatically
  canceled.
- **WD-CON-004:** Live actions may execute concurrently only when their complete
  Adapter-declared mutable-resource key sets do not overlap.
- **WD-CON-005:** Remediation must reuse exactly the complete frozen
  Adapter-declared mutable-resource key set from the original action. It must
  not derive or recompute resource keys from Product or Execution Identity.
- **WD-CON-006:** Duplicate pending Release requests for the same Release
  Execution Identity must be rejected or coalesced rather than accumulated as
  an unbounded workflow queue. Every admitted, non-coalesced request must create
  one distinct Attempt identified by the Execution and its unique
  `workflow_run_id`; it must not create a second Execution identity for the same
  channel, Release Unit, and target. A pending dispatch replaced or coalesced
  before execution is not admitted and creates no Attempt.

### Retention and Platform State

- **WD-RET-001:** Caches must be treated as non-authoritative performance
  mechanisms.
- **WD-RET-002:** Workflow Delivery must not assume that GitHub Actions
  artifacts or logs outlive the configured platform retention window.
  First-slice live Release control and artifact retention must exceed the
  platform Environment approval-expiry window with operational margin; the
  initial LLD uses 45 days and activation is blocked if repository policy cannot
  provide it. Fresh authenticated preactivation and post-merge readback must
  prove that the effective repository artifact-retention policy permits at
  least 45 days. Retention and a pending Environment approval do not freeze or
  extend Governance validity: the Approval job and publisher must require an
  unexpired at-most-90-day attestation, valid repository/ref/path and
  blob/content or generation binding, no protected Governance-path touch since
  eligibility, and `live_enabled: true`. They must not require equality of an
  otherwise unrelated resolved `main` commit.
- **WD-RET-003:** Longer-lived release identity and provenance may rely on Git
  tags, destination records, GitHub Releases when selected, and GitHub Artifact
  Attestations.
- **WD-RET-004:** The initial scope must not require a permanent external
  Release ledger, a global Official Product Identity-to-target binding index, or
  a GitHub Release audit anchor for every Release Unit.
- **WD-RET-005:** If required state can no longer be established after
  operational records expire, the affected operation must fail closed.
  Destination absence is itself sufficient initial-publication state when the
  required lower-layer destination contract is established. No retained Intent
  or Attempt lineage is required before publication may proceed, and none
  reserves the absent coordinate.

## Quality Attributes

- **WD-NFR-001 - Security and correctness:** Security and correctness dominate
  availability and latency when the qualities conflict, except for the
  explicitly accepted first-slice Buddy publication risk and
  repository-principal blast radius documented by `WD-SLICE-*`.
- **WD-NFR-002 - Explainability:** Operators and reviewers must be able to
  understand why scope, obligations, authorization, actions, and verdicts were
  selected.
- **WD-NFR-003 - Evolvability:** Adding an ecosystem or destination should
  normally require an adapter and policy mapping, not changes to cross-system
  authority semantics. A future Buddy destination must still make its own
  explicit threat and cost decision and cannot inherit `WD-SLICE-*`. Generic
  Environment Profiles remain deferred until a second concrete policy requires
  them.
- **WD-NFR-004 - Recoverability:** Retry and remediation must preserve identity,
  authority, and durable current-Attempt evidence across partial external side
  effects without depending on exhaustive Actions history.
- **WD-NFR-005 - CI latency:** Ordinary pull-request CI has a P95 12-minute
  Final Decision objective. Broad authority, policy, toolchain, and
  multi-Release-Unit changes are measured separately.
- **WD-NFR-006 - Performance safety:** Performance work must not weaken
  obligation coverage, artifact variant coverage, Evidence Admission, or
  authorization.
- **WD-NFR-007 - Test stability:** Business-flow tests must be semantic and
  scenario-oriented. Strict unit and contract tests remain required for
  schemas, canonicalization, identity, concurrency, mutation and recovery, and
  fail-closed logic. Tests must not require an exact job DAG, non-authoritative
  shell choreography, fixed inventory counts, parser branches, or step order
  beyond authority-critical ordering.

## Non-Goals

Workflow Delivery v3 does not:

- replace ecosystem build and package-management tools;
- become a general workflow engine;
- provide distributed transactions across destinations;
- promote pull-request artifacts into Release;
- consume CI results as Release Evidence;
- certify reproducible builds through duplicate building;
- provide a permanent external Release ledger in the initial scope;
- discover or admit exhaustive GitHub Actions history as publication authority;
- prove absence of every runtime consumer from a bounded static-reference scan;
- constrain a malicious accepted writer to the smoke package;
- support nondeterministic first-slice Release Units without a future sealed
  artifact publication-resume design;
- define a generic Environment Profile before a second policy requires it; or
- use ordinary Release force flags to rewrite published history.

## Requirements Stage Exit

The requirements stage is complete when:

1. every requirement is accepted, rejected, or explicitly marked for later
   scope;
2. the HLD maps each accepted requirement group to an owning architectural
   element;
3. assumptions owned by GitHub, identity providers, and destinations are
   documented at the appropriate contract boundary;
4. unresolved product policy is not hidden inside implementation detail; and
5. no later design document weakens these requirements without an explicit
   requirements change.
