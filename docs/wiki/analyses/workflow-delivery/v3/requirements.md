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
  wait. The bundle directly binds the Publication Snapshot and immutable
  reviewer-summary artifact by canonical payload digest and Artifact Reference;
  the Snapshot remains sole owner of the target, Qualification Decision,
  artifact, action, and mutable-resource closure. The Attempt may publish only
  after the Approval job receives the required Environment approval, freshly
  validates Governance, transitively admits that complete immutable chain, and
  durably emits one Publication Authorization. The Authorization directly binds
  the admitted Approval Bundle plus approval-boundary and fresh-Governance
  evidence. It reaches action, artifact, and resource inputs through that
  predecessor and must not copy them or bind `github.run_attempt`.
  A Publication Snapshot with zero actions represents successful
  `exact-satisfied` no-op finalization. Observation or an explicit no-op
  reobservation may use only the minimum read-only destination authority
  permitted by WD-REL-008. The path requires no Environment approval,
  Publication Authorization, publisher, destination write or publication
  credential, Publication Capability, mutation marker, or Publication Result.
  Its current-DAG publisher conclusion must be `skipped`, and no Approval
  Bundle, Publication Authorization, or other action-bearing lineage may
  exist. Immediately before success, the zero-action finalizer must freshly
  validate supported package-control state as well as protected Governance and
  repeat authoritative exact-version readback against the Snapshot-bound bytes,
  digests, and embedded witness. It must bind all three checks with the
  zero-action Snapshot in one exact-satisfied finalization proof. The only
  admitted schema is
  `workflow-delivery/v3/exact-satisfied-finalization-proof`; the former
  `workflow-delivery/v3/exact-satisfied-governance-proof` schema is
  incompatible and has no alias.
- **WD-REL-007A:** After successful Qualification and before publication,
  Observation, Publication Snapshot materialization or transport, approval
  waiting or rejection, or platform cancellation may stop the Attempt without
  side effects. If the read-only Finalizer runs, it may record a
  `failed-before-publication` disposition only when current-DAG facts prove
  that the mutation-capable publication step never started, the exact
  Qualification Decision succeeded, no valid zero-action Publication Snapshot
  applies, no mutation marker or Publication Result exists, and no
  contradictory downstream lineage exists. Publisher `skipped` proves
  publisher non-start. For publisher `failure` or `cancelled`, only the exact
  platform-evaluated `skipped` outcome of the isolated publication step proves
  that the step did not start; a script-produced flag, missing output, or
  missing artifact or record is insufficient. If an
  action-bearing Publication Snapshot was durably persisted, the Outcome must
  retain that lineage. Otherwise it must bind the exact successful
  Qualification Decision when no Observation exists, or the sole blocking
  Observation when one exists, and preserve publication-preparation
  uncertainty.
  The Outcome sets `possibly_mutated` false and requires a new manual dispatch.
  A zero-action Snapshot with publisher `skipped`, a null publication terminal
  reference, and no Approval Bundle, Authorization, or other action-bearing lineage, but without
  a valid exact-satisfied finalization proof, instead has `unknown` disposition and
  `possibly_mutated: false`. A zero-action Snapshot with a non-skipped
  publisher or action-bearing lineage is contradictory. A failed or incomplete
  Qualification Decision remains the terminal authoritative record and forms
  no Attempt Outcome. GitHub cancellation or finalizer transport failure may
  leave no durable Attempt Outcome; finalization is best effort, not
  guaranteed.
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
  Publication Authorization and transitively resolve its exact Snapshot,
  reviewer, action, artifact, and resource closure, then perform a final fresh
  Governance check. The Approval job validates the immutable action's
  destination-operation-profile digest against current Governance and admits
  the action as an instantiation of that profile; it does not inspect mutable
  destination package-control state or claim to validate the publisher's
  effective runtime. The publisher must freshly read supported package-control
  state immediately before the mutation marker and verify the expected owner,
  repository association, visibility, and exposed access facts.
  Unexposed access-grant completeness remains an explicit protected-Governance
  attestation limitation rather than an invented runtime proof. The publisher
  must compare the action's profile digest with current Governance, validate
  the concrete action as a profile instantiation, and verify the actual pinned
  toolchain and effective command configuration. The first slice has no
  Capability Environment,
  Capability Admission Decision, capability group, group manifest, or group
  result bundle. A package-administration change after the publisher's final
  supported readback remains inside the declared sole-writer/publisher TCB; the
  design does not claim a package-administration lock.
- **WD-REL-009:** Immediately before the first mutating destination operation,
  the publisher must durably persist a mutation-may-have-started marker.
  The marker must directly bind the Publication Authorization, the final
  publisher-side Governance proof, and the final supported package-control
  proof observed at that later mutation boundary. It also binds canonical
  evidence that the actual pinned toolchain and effective command
  configuration matched the admitted Destination Operation Profile. The
  normal-Live
  producer/current-run envelope identifies the publisher. The Authorization
  remains the sole approved closure over the Snapshot, action, resources,
  artifact, and Attempt. The publisher must validate the durably persisted
  marker before crossing the mutation boundary; persistence or validation
  failure blocks mutation.

    For every controlled post-marker terminal state, the publisher forms one
    logical `workflow-delivery/v3/publication-result` and initiates one logical
    persistence operation. The transport may retry only the same immutable
    payload without creating another logical Result. Publisher exposes one
    nullable scalar `publication-terminal-reference` to the current DAG. It is
    the immutable Publication Result Artifact Reference when a Result was
    durably persisted, otherwise the durable mutation-marker Artifact Reference
    when a marker was persisted, otherwise null. Result takes precedence over
    marker. This transport is not a wrapper record. The Finalizer accepts only
    null or one well-formed reference whose target is exactly a mutation marker
    or Publication Result, evaluates only that explicitly propagated reference,
    and neither lists nor infers other artifacts. A non-scalar, malformed,
    misbound, or other-kind reference fails admission. A Result reference must
    resolve its marker through Result lineage. Empty or missing output from a
    running publisher is not null and fails admission.

    The Result must directly bind the durable marker, which reaches the
    Publication Authorization, plus command classification, post-action
    readback, mutation classification, sanitized command/response diagnostics,
    and destination outcome. It must not repeat the requested coordinate or tag,
    pre-action Observation, expected artifact digests, target witness, action,
    resources, or other state already authoritative through
    `Result -> marker -> Authorization -> Approval Bundle -> Snapshot`. When
    available, post-action readback records the actual remote coordinate/version
    state, remote-observed artifact digests, remote-extracted witness, and
    observed state of the action-bound target-derived tag. Those newly observed
    facts may be present on either a published or controlled failed Result and do
    not by themselves determine the publication outcome. A `published` Result is
    valid only when
    the current command definitively succeeded, mutation classification is
    `mutated`, and authoritative exact-version readback succeeded. Conflict,
    non-success, or ambiguous responses remain failed in that Attempt even if
    readback is exact. A failure before the marker emits no Publication Result.
    A missing durable Result after the marker means the Attempt is unknown and
    possibly mutated; neither the publisher nor Finalizer may repair or synthesize
    it. A later dispatch must reobserve the destination before deciding whether
    any action remains.

- **WD-REL-009A:** A strictly admitted current-Attempt Publication Result is
  authoritative for the publication business outcome. GitHub job and workflow
  conclusions remain authoritative for scheduling and termination facts and
  for conservative classification when no valid Result exists; they do not
  erase a valid durable Result. A non-null publication terminal reference from
  a publisher reported as `skipped` is contradictory and must fail closed. Publisher
  `failure` or `cancelled` after a valid durable Result remains diagnostic.
  Publisher `success` without a valid Result is not publication evidence.

    Attempt Outcome uses the closed disposition set `exact-satisfied`,
    `published`, `failed-before-publication`, `publication-failed`, and
    `unknown`, plus the authoritative `possibly_mutated` classification.
    Platform conclusions are not business dispositions. For an action-bearing
    path with a Result, Outcome binds the one canonical Publication Result
    digest; the marker and Publication Authorization have no independent Outcome
    lineage. A marker-without-Result Outcome instead binds the marker digest.
    An `exact-satisfied` Outcome binds the exact-satisfied finalization proof,
    which directly binds the zero-action Snapshot plus fresh Governance and
    package-control proofs and fresh authoritative exact-version readback. A
    zero-action Snapshot missing that proof binds the Snapshot. A pre-marker
    action path binds the Publication Authorization when present, otherwise the
    Approval Bundle when present, otherwise the action-bearing Snapshot when
    present, otherwise the sole blocking Observation when present, otherwise the
    exact successful Qualification Decision only when no Observation exists. A
    selected Observation must directly bind that Qualification Decision. A
    retained non-blocking Observation without a Snapshot has no admitted direct
    predecessor and forms no Outcome.
    Multiple candidates at the selected predecessor tier are contradictory.
    Outcome uses one tagged direct-predecessor reference and does not copy
    ancestors reachable through that predecessor. Only combinations defined by
    the HLD terminal-state matrix are valid; every other combination fails closed
    and may leave no durable Outcome. Result summaries and operator guidance are
    non-authoritative projections outside the canonical Outcome. Finalization
    remains read-only and best effort.

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
    `HEAD` or commit identity. The supported catalog covers only selector rows
    paired with an exact Ecosystem Authority Graph in the first-slice LLD. Git
    Source Authority supplies exact bytes directly or materializes only the
    declared files into a Session-owned isolated snapshot when an official
    library or CLI requires paths. The graph binds authoritative source
    artifact schemas, ecosystem standards, exact package, CLI, runtime, tool,
    module, or assembly identities; exact versions and lock/integrity
    provenance; public APIs or commands; input modes; admitted format
    generations; required normalized facts; and explicitly unsupported cases.
    File-oriented authorities may read only that snapshot. No graph may fall
    back to the real worktree, resolve an undeclared import or preset, expand
    ambient environment, access a registry or network, evaluate GitHub
    expressions or MSBuild properties, install or restore packages, execute
    candidate code, or write repository or external state.

    Authoritative lockfiles or manifests, official ecosystem libraries or CLIs,
    and published standards may compose to own the manifest, lock, descriptor,
    locator, workspace, or language model. Distinct semantic layers may compose
    in an ordered graph, such as a pnpm lock model followed by official
    dependency-path, lockfile-resolution, workspace-specifier, and registry
    specifier helpers, but two authorities must not compete over the same layer.
    Policy code consumes only stable package identity, reference-kind,
    local-path, and source-location facts. It must not recreate, cross-check, or
    harden syntax or invariants already owned by the selected graph.

    The policy rejects the prohibited forms assigned to each retained surface,
    including normalized direct, versioned, aliased, and workspace
    smoke-coordinate facts and local dependency paths resolving to the known
    producer root. It must not reject the producer path globally because
    workflows may legitimately build that source. The only allowed
    package-name occurrence is the top-level `name` in `package.json` at an
    exact known producer path. Findings mean prohibited references, not proven
    runtime consumers. Encoded or split construction, arbitrary runtime
    downloads, surfaces or dialects without an admitted authority graph,
    external configuration, and novel layouts are explicit non-goals.

    Static-reference evidence must bind schema, result, source kind, exact
    target when applicable, policy ID and digest, sorted exact implementation
    identities actually loaded, canonical error kind when result is error, and
    sorted findings. The invocation schema must reject an omitted or unknown
    source kind and malformed required source parameters before constructing a
    Result. After source admission, inability to deterministically enumerate,
    read, or minimally materialize the declared exact source is
    `source-acquisition-failed`. File, surface, or finding counts may be
    diagnostics only. Whole-file digest exceptions, fixed inventory counts,
    scanned-surface digest authority, handwritten ecosystem grammars or
    schemas, competing-authority cross-validation or hardening,
    dataflow/interpreter claims beyond the bound authority projections,
    trigger-catalog authority, and claims of universal consumer absence are
    forbidden. Strict byte-to-text behavior, BOM handling, snapshot inputs,
    loaded authority identities, normalized fact contracts, and distinct
    `source-acquisition-failed`, `encoding-rejected`, `authority-rejected`,
    `authority-execution-failed`, `unsupported-projection`,
    `authority-mismatch`, and `cleanup-failed` failures are part of the policy
    contract. Changing any source schema, standard, authority identity, version,
    API or command, input mode, format generation, or fact contract changes the
    policy digest. Source candidates and graph-owned projections must follow
    one deterministic declared traversal. The first typed non-cleanup failure
    is the canonical error; required-root cleanup failure overrides it and
    retains the earlier sanitized cause only as diagnostic.

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
    `workflow-delivery/v3/normal-live-governance-attestation-v2`. This
    intentionally incompatible schema is the selected-ref control compatibility
    fence. It replaces the disabled v1 contract because native destination
    acceptance now has a different closed field set. A selected revision must
    require v2 exactly; v1 is not an admission alias, and a superseded parser
    must fail before Release Execution lookup, Attempt creation, or any
    Environment job. This does not substitute protected-main control code for
    the selected revision.

    V2 retains a strict activation-state union. `blocked` contains only
    `state: "blocked"` and is valid only with `live_enabled: false`; it carries
    no native evidence. `ready` contains the
    complete pass-only Approval Environment, artifact-retention, and
    destination-primitive attestations. `live_enabled: true` requires `ready`;
    `ready` may remain present with `live_enabled: false` so an emergency
    disable does not rewrite evidence. The implementation migration must use
    `blocked`; activation installs `ready` evidence and enables the flag in one
    protected change.

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
- **WD-CHN-003:** Buddy artifacts, Evidence, Decisions, and Publication Results
  must not be promoted or reclassified as Official.
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
  publication capability, Publication Result, or mutation. It may emit hypothetical
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
  `workflow-delivery/v3/normal-live-governance-attestation-v2`. A ref whose
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
  Bundle and its transitive Snapshot, reviewer, artifact, and exact
  action/resource closure, and durably emits the complete Publication
  Authorization.
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
  until the first-slice destination satisfies the bounded standard-publication
  acceptance in WD-OPS-002A and fresh repository retention readback satisfies
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
  read-only operator investigation and native run lookup, never a blind
  redispatch. That first-slice handling does not create a formal Reconciliation
  Record or invoke a standalone Release Reconciliation workflow. Later normal
  Buddy runs may again select arbitrary same-repository refs whose
  selected-revision control strictly admits the active Governance schema.
  Before activation,
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
  artifacts, destinations, observations, actions, Publication Results, authority,
  authorization, outcome, and allowed operator actions.
- **WD-EVD-005:** Authoritative Plans, Evidence, Decisions, artifact identities,
  and Publication Results must persist before a later stage relies on them. Optional
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
  Product or Execution Identity. Desired state must include the exact
  destination coordinate, immutable in-package target witness, target binding,
  and qualified artifact bytes or digest. For
  first-slice npm, exactness is determined only by the normalized package name,
  frozen native version, downloaded tarball bytes and digests, and embedded
  witness in the active registry projection. Runtime Observation does not claim
  to enumerate deleted/restorable versions. The target-derived dist-tag
  `buddy-sha-<40-lowercase-target-sha>` is declared non-authoritative routing
  metadata, not part of exactness, identity, or provenance. Ownership,
  repository association, visibility, and access remain admission and
  Governance preconditions. Each first-slice Observation must separately embed
  a package-control proof for the destination/normalized-package subject,
  supported authoritative endpoints, owner, repository association, visibility,
  exposed access facts, observation time, and response digests. The proof is
  never standalone: its parent must bind the applicable protected-Governance
  identity or proof, derive package-control expectations from that parent-bound
  Governance, and jointly validate the observed facts. It does not copy those
  expected values or the Governance content digest. Unexposed access-grant
  completeness remains a Governance-attested limitation. Desired state is
  derived from the Qualification Snapshot and admitted artifacts.
  The Observation Record must bind the Release Attempt, exact successful
  Qualification Decision, logical projection, immutable desired-state basis,
  and canonical remote response and observed facts, including observed artifact
  digests and separately classified routing tag diagnostics. It must not bind a
  future Publication Snapshot; that later Snapshot must seal admitted
  Observation Records with resulting desired state and materialized actions.
- **WD-OPS-002:** State absent from the active destination projection may form a
  publish action, exact satisfied state must skip the side effect, and partial,
  unknown, conflicting, or unprovable active state must fail closed. Active
  absence with no retained operational lineage is a legitimate action candidate
  and is not inherently unprovable, but it does not prove the coordinate was
  never published, is not retained as deleted/restorable state, or will accept
  creation. The authoritative package-version effect must use atomic
  non-overwriting create-only semantics. Pre-observed exact active state
  produces no action. At mutation linearization, active-absent state may be
  created; a hidden deleted/restorable reservation or competing creation may
  instead cause definitive failure. The admitted primitive must not replace,
  recreate, or alter retained version state. The explicitly authorized
  non-authoritative tag side effect remains governed by its bounded race
  contract rather than this version-object guarantee.
  Complete proof that the current command made no mutation permits
  `failed/not-mutated`; otherwise the Result remains conservative. Release must
  never implement creation as read-then-upsert, overwrite, or
  delete-and-recreate. A later new dispatch may observe exact active state.
  Successful durable creation establishes the observable package binding; the
  Attempt creates no reservation before mutation. A manual
  Release Intent may authorize normal read-only Observation; exact pre-observed
  state may finalize as `success` with `exact-satisfied` disposition without
  approval or publication lineage. Immediately before that success, the
  zero-action path must repeat protected Governance ancestry, path-touch,
  blob/content, expiry, and `live_enabled` validation, repeat supported
  package-control readback, repeat authoritative exact-version readback against
  the Snapshot-bound bytes, digests, and embedded witness, and bind all three
  fresh checks with the zero-action Snapshot into no-op finalization.
  For first-slice npm, an exact version produces no action regardless of tag
  state or tag-read availability, and normal flow never repairs a tag. An
  active-absent version may form an action only when the target-derived tag is
  successfully observed absent from the active tag mapping, the current
  Governance-bound native acceptance includes the deleted/restorable
  same-version scenario, and that acceptance remains valid for action-bearing
  admission. An already occupied or unprovable tag blocks rather than
  authorizing a known overwrite. The action carries the canonical destination-
  operation-profile digest plus exact tarball, package, version, and explicit
  target-derived tag operands. The resolved profile supplies the fixed
  registry, access mode, toolchain, command template, and highest-precedence
  `fetch-retries=0` configuration for one standard `npm publish` invocation;
  no implicit default or runtime override may alter that request.
  Automatic retry of the mutating registry request, a separate tag command,
  compensation, removal, rollback, or repair is forbidden. Bounded read-only
  observation and readback retries remain permitted. For this first slice, any
  conflict, non-success, or ambiguous publish response remains failed in the
  current Attempt. Exact post-failure readback is diagnostic only; a new manual
  dispatch must reobserve and may then take `exact-satisfied`.
- **WD-OPS-002A:** The first-slice npm publication contract relies on the
  destination's non-overwriting exact-version behavior and explicitly treats
  the required dist-tag assignment as a declared, non-authoritative,
  potentially last-writer-wins routing side effect. The tag remains in the
  action, reviewer summary, Authorization lineage, complete mutable-resource
  keys, pre-action Observation, and Publication Result post-action diagnostics
  and readback. It is not used for exactness, identity, provenance, success,
  retry, installation, or any other supported consumer lookup.

    The post-Observation race in which another authorized external writer assigns
    that exact tag before publication is an accepted first-slice risk for the
    dedicated smoke-only package and sole-writer TCB. The approved publish may
    move only that declared tag to the new version. This acceptance does not
    authorize another tag, a tag-only operation, package deletion, version
    replacement, or an administrative mutation. Repository-controlled
    publications remain serialized by package; known tag conflicts block before
    action.

    Before activation, a separately authorized acceptance against a pre-existing
    disposable package with expected ownership, visibility, access, and
    repository association must execute the exact admitted Destination Operation
    Profile. Each required safety property must be backed either by a cited
    documented lower-layer contract or by complete observation through a
    supported authoritative interface; an unsupported and unobservable required
    property blocks activation. The versioned native acceptance suite owns both
    its scenarios and the closed canonical before/after comparison shape used to
    validate the normalized outbound publish profile and bounded observable
    GitHub Packages mutation footprint. That shape includes normalized package
    identity, complete active version-name inventory, complete dist-tag mapping,
    remote-observed bytes/digests and witness for scenario versions, and
    supported owner, visibility, exposed access, and repository-association
    facts. For the deleted/restorable scenario only, it additionally includes
    the complete deleted-version inventory for the disposable package, the
    targeted deleted version's stable identity and restorable status, and the
    restored version's original bytes, digests, and witness. It explicitly
    excludes server-generated timestamps, request identifiers, URLs, and
    equivalent volatile metadata. Derived counters such as `version_count` must
    be recomputed from the applicable active or deleted inventory or validated
    against its expected scenario delta, not silently ignored.

    Acceptance must establish that an existing exact version cannot be replaced;
    exact bytes and witness can be read back; the projected delta contains only
    the scenario-declared new version and target-tag mapping; unrelated projected
    versions, tags, and package-control facts remain unchanged; and conflict,
    non-success, and ambiguous mutation responses are not upgraded to
    same-Attempt success. A tag-race case may end with the declared tag mapped to
    either competing version, but both immutable versions must remain exact.
    Identical and differing duplicate publish cases must have an empty projected
    semantic delta.

    Acceptance must also use a fresh unique disposable version to establish the
    hidden tombstone case. After publishing and verifying that exact version, the
    acceptance-only operator deletes it with separately authorized package-admin
    credentials and proves it is absent from active state but present as
    deleted/restorable state. Identical-byte and differing-byte invocations of the
    exact pinned publish profile must then run sequentially. Each must produce a
    definitive non-success and leave the complete active-version inventory,
    deleted-version inventory and targeted tombstone identity, dist-tag mapping,
    and package-control facts unchanged; the first empty delta must be proved
    before the second invocation. Acceptance then restores the original deleted
    object and verifies its original bytes, digests, and witness. Any success,
    ambiguous response, projection change, inability to prove continued
    restorability, or restore/readback failure rejects the profile and keeps Live
    disabled. These privileged delete/restore credentials and facts exist only in
    the separately authorized acceptance procedure and never enter runtime
    Observation or publication. Synthetic tests alone cannot establish
    destination support.

    Protected Governance must reuse its destination-primitive attestation to bind
    the canonical Destination Operation Profile digest, native-acceptance-suite
    version, approved disposable package preconditions, GitHub API version, cited
    lower-layer contract revision, capture time, and canonical evidence digest
    identifying the exact successful acceptance generation.
    Detailed acceptance inputs, active/deleted projections, tombstone facts, and
    raw results remain only in the separately authorized acceptance evidence and
    do not enter runtime Governance. The reusable profile is the sole owner of
    its stable profile identity, registry, access mode, toolchain, normalized
    request template including all fixed command options, and typed operand
    derivation and validation rules; concrete package, version, tarball, and tag
    values remain exclusively in the Publication Action. A change to the resolved profile, native
    acceptance suite, GitHub API version, or relied-on documented contract
    revision invalidates the acceptance. Every Publication Action must carry the
    same operation-profile digest. Approval must resolve that profile without
    defaults, compare its digest exactly with current Governance, and validate
    the immutable action as a profile instantiation; the publisher must repeat
    those checks against the actual runtime configuration.
    Initial activation of a newly admitted operation profile must bind acceptance
    evidence captured after implementation of that exact profile and no later
    than the Governance `inspected_at`. Later Governance attestations may reuse
    that generation only while every bound input remains identical and action-
    bearing admission occurs no later than 90 days after `captured_at`. Any
    binding change or age expiry requires new acceptance before an action-bearing
    Attempt may authorize or mutate. Expired acceptance does not invalidate a
    fresh Governance attestation for zero-action exact-satisfied finalization.
    The first slice remains `live_enabled: false` until initial acceptance
    passes.

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
- **WD-OPS-006:** Multi-action or multi-destination publication is outside the
  first slice and requires a concrete scenario and a new reviewed design. The
  first slice has one action and defines no generic transaction, compensation,
  rollback, or Saga protocol.
- **WD-OPS-007:** Reconciliation must be exceptional handling for destination
  state that cannot safely proceed through normal observation and a new
  dispatch. It is a separate process, not an Attempt Outcome field. A new
  manual dispatch is the only normal Release continuation and does not preclude
  reconciliation or remediation before that dispatch.
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
  manifests; Publication Authorization and publisher admission validate them
  transitively through the immutable predecessor chain. Overlapping live
  actions must serialize on them. Publication Results validate the same set
  transitively through `Result -> marker -> Authorization -> Approval Bundle
-> Snapshot`; they do not copy mutable-resource keys. When the available
  platform supports equality concurrency
  groups rather than arbitrary set-overlap locking, an Adapter may additionally
  define a conservative deterministic serialization projection. The projection
  may intentionally over-serialize, but every pair of actions whose complete
  key sets overlap must resolve to the same enforced group; the projection must
  never replace or weaken the complete frozen key set. Package mutation keys
  must include the exact External Package Coordinate plus any additional
  Adapter-required keys. The admitted first-slice standard npm publish is one
  action whose complete key set includes both the External Package Coordinate
  and destination/package/dist-tag mutable resource; no separate normal tag
  mutation is permitted. Its GitHub concurrency group uses the conservative
  shared destination/package projection so every action touching the same
  destination and npm package name serializes, including actions with different
  target-derived tags. Non-package keys and any safe serialization projections
  are defined by the Destination Adapter contract. Missing, unknown,
  incomplete, conflicting, or unenforceable required keys or projections must
  block live publication. Request-local Repository Model
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
  Absence from the active destination projection is sufficient
  initial-publication state when the required lower-layer destination contract
  and tombstone acceptance are established. No retained Intent or Attempt
  lineage is required before publication may proceed, and none reserves the
  active-absent coordinate or proves a tombstone absent.

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
