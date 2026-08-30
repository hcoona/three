# Workflow Delivery v3 Requirements

## Status

Architecture version: **v3**.

Review state: **Confirmed; normal Live activation requirements refined on
2026-08-29**.

This page is the normative product and system requirements baseline for the
clean v3 implementation line. It defines what Workflow Delivery must achieve,
not the internal structure used to achieve it.

The [High-Level Design](./high-level-design.md) describes the architectural
realization. Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

Requirement identifiers are stable traceability anchors. Later design and
acceptance artifacts may refine a requirement, but they must not silently
weaken or reinterpret it.

## Mission

Workflow Delivery must provide evidence-driven software delivery governance for
a polyglot monorepo.

- CI Qualification determines whether an immutable change candidate satisfies
  every applicable quality obligation.
- Release Delivery independently rebuilds and qualifies an immutable Release
  Unit, obtains explicit authorization, and performs traceable external
  publication.

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
  active Project Node, Release Unit, and repository obligation. Before
  coexistence begins, the first implementation pull request may project one
  canonical non-authoritative `incomplete-model-plan` Decision as a successful
  bootstrap check conclusion only when its exact base commit does not contain
  the canonical v3 CI workflow and every Plan diagnostic identifies an
  unclassified changed path. The canonical Decision remains failure. The
  projection must bind the exact pull-request base, head, tested merge, and
  request identity; reject superseded candidates, selected or failed lanes,
  mixed diagnostics, manual validation, and missing or malformed records; and
  self-disable once the workflow exists in the base commit. It is not CI
  success, a required check, Release authority, or a general exception to
  changed-path closure.
- **WD-CI-010:** Root HK SourceTreeConformance must run the new v3 control
  package pytest suite when the v3 control package/catalogs/tests, first-slice
  descriptors, exact first-slice Release policy, any v3 workflow consumer,
  direct Python workspace/lock input, or HK configuration/helper changes, and
  unconditionally for manual `slice-validation`. Unrelated product-source
  changes alone must not select this step. It remains an internal root-HK step,
  not a separate CI obligation, Evidence record, or job.

### Release Delivery

- **WD-REL-001:** Release must begin from an explicit Release Intent for one
  Release Unit, immutable target commit, and release channel. Official Product
  Identity is channel, Release Unit, and canonical NBGV version. Official
  Release Execution Identity is Official Product Identity plus immutable target.
  Buddy Release Execution Identity is channel, Release Unit, and immutable
  target. Different targets with the same Official Product Identity are
  different Release Executions. External package coordinate is strictly
  channel, destination, package, and version and is not a Product or Execution
  Identity field.
- **WD-REL-002:** Release must verify target and channel eligibility through
  Delivery Governance before live publication can be authorized.
- **WD-REL-003:** Each candidate Release request or run must first select an
  explicit live-release or release-simulation purpose before live eligibility,
  Product or Execution Identity lookup, coalescing, admission, or Attempt
  creation. For that purpose, Release must establish one authoritative,
  immutable, same-revision repository fact basis bound to the current request
  and execution attempt, exact target, producer, and same-revision control
  identity. It must close descriptors, the Project Node and dependency graph,
  Build Definitions, modeled variants and outputs, target-bound canonical and
  required native NBGV facts, including `npmPackageVersion` where required,
  and complete build and artifact scope. Inputs from CI, another request or
  purpose, a prior Attempt, or a prior execution attempt must be rejected.
  Missing, stale, differently bound, incomplete, unknown, or conflicting
  required facts must stop before live Execution lookup, coalescing, or
  admission and create no Attempt.
  When an NBGV value depends on Git history, Release must accept version facts
  only when they are proven for the exact target with all required ancestry and
  tags. If exact-target binding or required history completeness cannot be
  proved, Release must reject those facts and fail closed.
  After live admission creates an Attempt, Release must use that same
  authoritative fact basis to validate channel policy,
  policy-selected obligations and variants, compatibility obligations, and
  required native projection selection. Attempt planning selects and freezes
  those projections from that basis and must not derive, recompute, or fall
  back for them. It then derives and validates external coordinates,
  complete destination projections, Adapter and version bindings, logical
  operations, potential action and dependency schemas, capability policy, and
  the deterministic complete mutable-resource-key derivation and enforceability
  basis frozen by the Qualification Snapshot. It does not freeze actual mutation
  actions or actual action key sets before build, qualification, and
  observation. The Publication Snapshot later freezes the exact materialized
  action DAG and inputs, complete Adapter-declared key set for each actual
  mutation, groups, capabilities, and Receipt contracts. Release must not replace or recompute the authoritative fact basis within
  that admitted execution attempt. A simulation pass must likewise use its one
  authoritative simulation-purpose fact basis throughout that pass.
- **WD-REL-004:** Release must not consume CI Plans, Evidence, artifacts,
  checks, or verdicts as Release qualification inputs.
- **WD-REL-005:** CI and Release must use the same Build Definition for the same
  artifact variant, while materializing separate builds for their distinct
  immutable targets and purposes. Each Plan and Build Request must select and
  freeze the exact required native version projection emitted by Repository
  Model compilation. A Build Adapter may apply and verify that value but must
  not recompute NBGV, derive another version, or use fallback version fields.
- **WD-REL-006:** Release outputs must be bit-for-bit reproducible for
  identical target, Build Definition, toolchain, and declared inputs. The
  system is not required to certify reproducibility by performing duplicate
  builds.
- **WD-REL-007:** Publication authorization must bind the exact artifact bytes,
  provenance, destination observations, intended actions, qualification
  decision, and required destination capabilities. The Finalizer must admit an
  Authorization Record only after successful approval. Terminal denial Evidence
  is admissible only where a platform supplies documented exact
  current-attempt/job/Snapshot proof. The first-slice GitHub Environment
  rejection surface does not supply that proof, so rejection is unknown
  approval-contract failure and leaves a replayable incomplete Attempt; any
  observable review information is diagnostic only and no Capability may start.
  Workflow Delivery must not invent an approval watchdog. If
  GitHub cancels or expires a run while approval remains pending and no
  capability group started, the platform run/job conclusion is sufficient
  no-side-effect terminal evidence; a separate Approval Outcome Evidence record
  and Finalizer outcome may be absent, and the Attempt is replayable and
  incomplete rather than successful. The system need not distinguish manual
  cancellation from platform expiry unless GitHub exposes that distinction. If
  any capability job may have started, cancellation is not no-side-effect
  proof; the Attempt remains incomplete and possibly mutated, and the next
  Attempt must reobserve. When finalization does run and neither valid
  authorization nor an applicable admissible terminal result exists, it must
  report approval-contract failure rather than governed rejection.
- **WD-REL-007A:** After successful Qualification and before a durable
  Publication Snapshot exists, Observation, Publication Snapshot
  materialization, artifact upload, or platform cancellation may leave the
  Attempt incomplete without side effects. A running Finalizer may record this
  only when direct platform job/DAG facts prove that no durable Publication
  Snapshot exists and no Authorization, Capability Admission, mutation marker,
  capability-group result bundle, or Receipt exists. Missing Snapshot transport
  alone is not sufficient proof. The Outcome must bind the exact successful
  Qualification Decision, use terminal phase `publication-preparation`, record
  `incomplete`, preserve uncertainty, set `possibly_mutated` false, and require
  a new Attempt. If the Publication Snapshot was durably persisted before a
  later failure, the Finalizer must retain that lineage and use the existing
  Snapshot-bound lifecycle instead. Platform termination may still prevent the
  Finalizer itself from running; in that case the retained platform conclusion
  remains the operational evidence.
- **WD-REL-008:** Release must obtain short-lived, destination-specific
  Publication Capabilities only after qualification and observation establish
  the exact authorized action and a valid Authorization Record exists. Approval
  failure Evidence must never grant Capability. Qualification may declare
  Capability requirements but must not request, approve, or create live
  Capability. The normal v3 live path may request destination Capability only in
  an authorized side-effect capability group after a credential-free capability
  admission gate validates the Authorization Record and exact Publication
  Snapshot, summary artifact, actions, artifacts, resource keys, and group
  manifest. Immediately before admission, that gate must use `contents: read` to
  freshly resolve the policy-fixed protected ref and read the Governance
  attestation document at the resolved commit. It must validate ref protection,
  schema, canonical content, policy/package bindings, unexpired status, and the
  required boolean `live_enabled` field, then require repository/ref/path,
  commit/blob/content provenance, content identity, and enabled state to match
  the current Attempt's admitted Live Eligibility Decision. A false
  `live_enabled` value, expiry, source or provenance change, content or binding
  mismatch, or other invalidation blocks publication. Restoring Governance does
  not resume that Attempt; a new Attempt must repeat eligibility, planning,
  qualification, observation, and approval. Only gate success may schedule or
  start the credential-bearing job. That job may repeat the same
  `contents: read` source, binding, and freshness checks immediately before
  mutation as defense in depth, without creating an independent
  malicious-writer boundary or requiring another credential or service.
- **WD-REL-009:** Release must record a Receipt for every completed destination
  side effect, persist it before starting a later side effect in the same
  capability group, and produce an explainable final outcome for the Release
  Attempt whenever platform termination does not prevent finalization. The
  retained platform conclusion and phase state remain authoritative operational
  evidence when cancellation or expiry prevents a context-owned final outcome.
- **WD-REL-010:** During live admission, after entering whole-Execution
  concurrency and before binding the current Attempt, Release must use
  read-only Actions access to discover and strictly admit retained runs and run
  attempts,
  artifacts, Attempt bindings, outcomes, and platform conclusions for the same
  Release Execution Identity. The trusted caller, never the record payload,
  selects either `current-authority` or `execution-history` admission.
  `current-authority` requires exact current purpose, request, run, run attempt,
  Attempt, target, producer, control, artifact, and digest bindings and rejects
  every prior attempt. `execution-history` is valid only in pre-Attempt admission. Its source may be
  a different workflow run or an earlier run attempt of the current workflow
  run. It binds
  platform-exposed artifact ID/digest, source workflow run ID, head SHA, payload
  integrity, and available metadata; Jobs and Run APIs separately establish
  attempt/job/phase facts. Historical payload producer, exact attempt,
  reusable-workflow, purpose, and control claims are diagnostic self-assertions.
  Historical records must never satisfy current-Attempt Evidence,
  authorization, artifacts, Receipts, outcomes, or eligibility. Finalization
  and explanation must bind a history-only snapshot of the current
  request/run/attempt, exhaustive query basis, sorted admitted platform
  identities/digests, and separately queried phase facts. Missing expired
  history does not require a permanent ledger:
  current observation may proceed when every projection is provably absent or
  exact; partial, conflicting, unknown, or unprovable state requires
  reconciliation.
- **WD-REL-011:** For the named live Buddy slice, after exact target pinning and
  request-local Repository Model compilation but before Execution lookup,
  concurrency, history admission, or Attempt creation, Release must perform an
  exact-target live eligibility check. It must scan repository dependency
  surfaces using Release-owned consumer policy and validate a
  Governance-approved, non-executable human-inspection attestation at the
  policy-fixed repository, exact protected ref, and path. The immutable
  first-slice source contract is repository `hcoona/three`, ref
  `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`; the
  concrete Release policy or its selected static Governance-policy catalog must
  carry those exact fields. The attestation must contain the explicit accepted
  writer inventory, package/repository and Manage Actions access inventory or
  evidence digest, policy/package binding, issuer, inspection time, expiry no
  later than 90 days, acknowledged API and staleness limitations, and a required
  top-level boolean `live_enabled` field. Eligibility must use `contents: read`
  to freshly verify the policy fields and ref protection, resolve the ref to a
  full commit SHA, and read the attestation blob at that commit. Missing,
  unreadable, malformed, expired, provenance-mismatched, `live_enabled: false`,
  or consumer-positive state must block before Attempt creation. The immutable
  Live Eligibility Decision must bind
  purpose, request, run ID, run attempt, selected ref and SHA, Repository Model
  digest, producer/control, policy and catalog digests, scanned surfaces,
  explicit exceptions, the attestation's `live_enabled` value, attestation
  repository/ref/resolved commit/path/Git blob OID/canonical content SHA-256,
  and result.
  Current-attempt success must be transported by artifact ID/digest and bound
  into the Attempt and human summary. CI HK results and historical records are
  not admissible substitutes. The eligibility job must receive only
  `contents: read`; it must not receive `actions: read`, `packages: read`, or
  package-write permission. In the normal live path, effective
  `actions: read` is confined to execution-history admission, and explicit
  `packages: read` is confined to destination observation. The runtime check
  does not enumerate or compare current repository writers or GitHub Packages
  grants. Relevant role, grant, or Manage Actions changes require an authorized
  human to promptly commit
  `live_enabled: false` to the protected source, then inspect, update, and
  re-attest before a later protected commit may restore `live_enabled: true`.
  This is bounded operational response, not instantaneous platform disablement:
  protection, review, merge, and fresh-read latency remain, and a capability job
  already past its final check may complete. Expiry bounds normal-flow
  staleness. No repository variable, PAT, GitHub App, service, ledger, OIDC
  expansion, or additional token permission is authorized.

### Buddy and Official Channels

- **WD-CHN-001:** Buddy must produce distributable, non-authoritative previews
  through complete channel, destination, package-coordinate, and capability
  boundaries isolated from Official. Isolation does not require Buddy to alter
  the canonical product-version string when the destination coordinate already
  provides an independent identity boundary.
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
  purpose-discriminated Snapshot binding request identity, run ID and attempt,
  target, channel, Release Unit, canonical and native version facts, producer,
  and control identity. Only then may it derive the separately namespaced,
  request-scoped Simulation Identity from those bindings. The Repository Model
  Snapshot must not bind that not-yet-created Identity. Simulation must not
  contain or acquire a live Product, Release Execution, or Attempt identity,
  Authorization Record, publication capability, Receipt, or mutation. It may
  emit hypothetical requirements and actions and a Simulation Outcome. Shared
  schemas may be reused only with an explicit purpose discriminator and
  cross-purpose admission rejection.
- **WD-CHN-005:** The `hcoona-release-smoke-npm` live Buddy slice may publish
  only the dedicated disposable smoke package to its isolated GitHub Packages
  destination. No normal developer, CI, or production dependency may consume
  that package. After the first-slice cutover, former v1 Buddy projects are
  unsupported and blocked until explicitly migrated into future v3 slices.
  This exception is not inherited by Official or by any future Buddy destination
  or production package.

### Governed Control Code

- **WD-AUTH-001:** CI decision code must come from the tested candidate
  revision. Live Release decision code must come from the exact selected target
  revision being released. Official requires a protected authoritative target.
  For the `hcoona-release-smoke-npm` live Buddy GitHub Packages slice, the target
  may be any same-repository ref selected by `workflow_dispatch`; workflow,
  control, Planner, Finalizer, and publisher code all come from that exact
  target. Dry-run Release simulation must use decision code from the exact
  selected simulation revision and must receive no approval or live publication
  capability.
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
  policy. The first-slice live Buddy exception requires the exact Publication
  Snapshot and successful approval through its selected Buddy Approval
  Environment Profile, but not protected-ref eligibility.
- **WD-AUTH-005:** Delivery Governance must control protected target
  eligibility, control-code review, protected environment review, OIDC and
  destination trust, capability grant and revocation, and Break-Glass
  Remediation approval. Protected target eligibility and owner-reviewed live
  control code remain mandatory for Official but are explicitly waived only for
  the named first-slice Buddy exception.
- **WD-AUTH-006:** Delivery Governance must select GitHub Environment
  identities by authority policy rather than by Release Unit, package, or slice
  name alone. An Approval Environment Profile is keyed by repository, channel,
  reviewer and self-review policy, wait and branch/tag policy,
  administrator-bypass posture, and credential-free behavior. A Capability
  Environment Profile is keyed by repository, channel, destination trust
  boundary, credential source including its identity contract, GitHub permission
  and destination-access policy, reviewer policy fixed to `none`, wait and
  branch/tag policy, and administrator-bypass posture. Release policies may
  reuse an Environment identity only when the complete applicable profile is
  identical. Reuse never transfers Governance eligibility, an Environment
  approval, an Authorization Record, Capability, Attempt history, or package
  authorization. Any incompatible profile requires a distinct Environment
  identity. A destination that requires human approval at the capability
  boundary needs a new architecture decision rather than a reviewer-bearing
  Capability Environment Profile. Exact platform names and profile mappings are
  Delivery Governance-owned lower-layer configuration contracts.

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
  first-slice Buddy target-revision publisher is a bounded exception: it
  validates those bindings by contract but is not an independent adversarial
  enforcement boundary.
- **WD-SEC-005:** Failure to obtain the required OIDC identity or Publication
  Capability must block the affected side effect. No long-lived credential
  fallback is permitted.

### First-Slice Buddy Accepted Risk Exception

- **WD-SLICE-001:** Live Buddy for `hcoona-release-smoke-npm` may target any
  same-repository branch or ref selected by `workflow_dispatch`. The same
  selected target supplies workflow, control, Planner, Finalizer, and publisher
  code. The architecture must not substitute protected-main control code.
- **WD-SLICE-002:** Every such Attempt must seal its exact Publication Snapshot
  and receive human approval through the Buddy approval Environment selected by
  its exact Approval Environment Profile before any package-write Capability
  exists. Reviewer-visible approval context
  must include target SHA, selected branch or ref, exact package coordinate,
  artifact digest and manifest, package lifecycle scripts, and exact action
  summary. The Authorization Record must bind the Publication Snapshot digest
  and immutable reviewer-summary artifact identity and digest. Self-review
  prevention must be enabled where available unless a separately confirmed
  slice-scoped single-maintainer exception applies. For
  repository `hcoona/three`, package
  `@hcoona/hcoona-release-smoke-npm`, and approval Environment
  `workflow-delivery-v3-buddy-approval`, the confirmed exception permits
  sole accepted writer and reviewer `hcoona` to approve their own dispatch with
  `prevent_self_review: false`. This approval is explicit operator
  self-confirmation and process control, not independent review or an
  independent security boundary. The exception is valid only while the
  human-attested effective Write/Maintain/Admin and required-reviewer sets
  remain exactly `hcoona`; any relevant actor, reviewer, role, team, or access
  change requires `live_enabled: false` and a new Governance decision before
  another dispatch. The normal v3 live path must keep this credential-free
  human approval job separate from the downstream Environment-referencing
  capability job. The first-slice Capability Environment has no required
  reviewer and performs no human approval. A reviewer-bearing destination is
  unsupported by this architecture and requires a new architecture decision. A
  preceding credential-free approval Finalizer must admit the capability group,
  and its success alone may schedule the publisher. The publisher must
  revalidate the Authorization Record and exact Snapshot, summary artifact,
  action, artifact, resource-key, and group bindings before using
  `packages: write`.
- **WD-SLICE-003:** After approval and successful credential-free Capability
  Admission, the target-revision side-effect job may receive only a short-lived
  `GITHUB_TOKEN` with the minimum `packages: write` permission needed for the
  dedicated GitHub Packages smoke destination. It must receive no PAT fallback
  and no `id-token: write`. The normal reusable-workflow path must keep
  workflow-level permissions empty or read-only. It may declare
  `packages: write` only on the `uses`-only caller job as the reusable-workflow
  ceiling and on the called Environment-referencing publisher job as the
  effective capability. No other job may receive package write through an
  explicit grant or inherited omission, and the called workflow cannot elevate
  beyond the caller-job ceiling.
- **WD-SLICE-004:** Environment approval is the trust elevation for this
  branch-controlled publisher. Approval is neither cryptographic validation nor
  independent semantic validation of the target code, artifact, lifecycle
  scripts, or action. The architecture must not claim that an independent
  protected publisher constrains malicious target code after approval.
  Environment is a mandatory workflow and Governance control against mistakes,
  accidental publication, and ordinary process violations, but does not impose
  a non-bypassable ceiling on `GITHUB_TOKEN` permissions for a malicious
  repository writer.
- **WD-SLICE-005:** The accepted residual risk is that an approved malicious or
  mistaken branch may publish arbitrary or malicious bytes, squat package
  versions or namespaces reachable by the token, create registry clutter or
  cost, or abuse package operations within its repository/package permissions.
  A trusted repository writer may also create alternate workflow YAML or jobs
  with `packages: write` and may possess latent repository/package
  administration authority.
  Correct isolation must prevent Official capability and known
  Official/production package access. Activation must inspect and record actual
  token permissions and package/repository grants and may use safe denial probes
  only for enumerated unrelated assets. It need not prove universal negative
  reach; other package operations reachable under the smallest configured
  repository/package grants remain accepted writer-TCB risk.
- **WD-SLICE-006:** Rollout must bind the exception to the exact dedicated
  disposable smoke package and GitHub Packages destination, the exact selected
  Approval and Capability Environment Profiles, smallest package/repository
  access, no normal consumer, no planned or ordinary delete, restore,
  permission, visibility, or admin action, Break-Glass handling for package
  deletion or restore, and explicit human Governance inspection. Reusing a
  compatible Environment identity does not extend this package-specific
  exception. The rollout need not prove absence of latent admin authority
  already held by trusted repository/package actors. Any future Buddy
  destination requires its own threat and cost decision. A permanent
  repository-wide HK dependency-policy gate must scan dependency manifests,
  lockfiles, workflows, install scripts, and dependency configuration for
  normal developer, CI, or production consumption of the disposable package.
  It must run for dependency-surface changes and unconditionally during
  `slice-validation`; any consumer blocks and reopens this exception.
- **WD-SLICE-007:** Every actor with repository Write, Maintain, or Admin access
  is inside the trusted Buddy publisher TCB for this slice. External or fork
  contributors and actors without repository write are outside that TCB and
  cannot manually dispatch the live path under normal GitHub permissions.
  Optional workflow-execution protections may reduce who can run workflows but
  are neither a required platform dependency nor a per-job permission ceiling.
  Repository access, team membership, package/repository/Manage Actions access,
  and writer-TCB acceptance must be re-attested by a human after relevant role,
  team, or permission changes and at least every 90 days. Operators must
  promptly commit `live_enabled: false` to the policy-fixed protected
  attestation pending reacceptance; the change takes effect for normal flows
  when that protected commit is visible to a fresh check, not instantaneously.
  Attestation expiry independently bounds stale normal flows. Runtime does not
  claim complete current writer or GitHub Packages grant enumeration. If any
  repository Write/Maintain/Admin actor is not
  trusted to publish, the live slice is blocked until either that actor's
  repository access is reduced below
  Write/Maintain/Admin or package-write Capability and destination access are
  placed behind an independently enforced publisher boundary unavailable to
  writer-authored workflows. Ref narrowing, Environment branch restrictions,
  CODEOWNERS, and workflow-execution protections may be defense in depth but
  are insufficient remediation by themselves while an untrusted writer can
  author alternate workflows with `packages: write`.
- **WD-SLICE-008:** The implementation PR merge is the direct repository-wide
  v1 Buddy-to-v3 Buddy cutover; no legacy Buddy compatibility route is
  preserved. The merge removes the legacy Buddy workflow files, and the
  controlled activation procedure must freeze Buddy dispatch, disable both
  legacy workflow identities, `buddy.yml` and `release-buddy.yml`, cancel or
  drain every queued, waiting, approval-pending, or running execution, and
  verify disabled state, removal, and old-ref dispatch rejection before
  destination acceptance. A project-local check in new YAML is insufficient
  because older refs retain old workflow code. All legacy Buddy publication
  routes retire at cutover, and an intentional Buddy outage is allowed. v1
  Official and CI assets remain unchanged. That preservation explicitly
  excludes the retired legacy Buddy workflows, Buddy-specific tests and
  matrices, and Buddy documentation that the direct cutover removes or
  rewrites.
- **WD-SLICE-009:** Live Buddy and Official simulation must qualify the built
  npm tarball with distinct `node/npm-artifact-contents-v1` and
  `node/npm-install-import-v1` obligation identities. They may execute in one
  tarball-dependent physical job, but must emit two separately admitted
  Evidence records and cannot finalize qualification successfully unless both
  are satisfied.
- **WD-SLICE-010:** Before normal first-slice live activation, Delivery
  Governance may use one temporary protected acceptance workflow with a
  dedicated non-Release purpose and reviewer-protected acceptance Environment.
  It must be hard-bound to an approved target SHA, fixed acceptance-only
  coordinate in the disposable smoke package, and explicit confirmation; accept
  no normal Release inputs; grant `packages: write` only to probe jobs; and emit
  no live Release identity or history. Every package probe job must
  independently fail closed unless `github.run_attempt == 1`. The terminal
  evidence-capture job must use
  `if: ${{ always() && github.run_attempt == 1 }}` or an exact equivalent so the
  first attempt persists each dependency result, available probe response and
  diagnostic, and any failed, skipped, canceled, incomplete, or ambiguously
  mutating disposition even when an upstream dependency fails. It must classify
  incomplete or unknown destination state for reconciliation. The
  evidence-capture job must still reject non-first attempts, so a partial rerun
  cannot reuse an earlier Environment review or disposable coordinate. A retry
  requires a new reviewed workflow invocation and a new fixed disposable
  coordinate/version. Acceptance coordinates are Governance fixtures, not NBGV
  product versions or Release projections. The merged v3 code keeps the
  protected attestation's `live_enabled` field false while Buddy dispatch is
  frozen, both legacy identities are disabled/drained/removed, and the probes
  run. The workflow, bypass, and Environment must then be removed and removal
  verified before an authorized protected commit may set `live_enabled` to
  true. Failure leaves all Buddy publication disabled, removes the temporary
  path, keeps legacy Buddy retired, and enters reconciliation without restoring
  a reusable bypass. Restoring legacy Buddy requires a separate user-approved
  rollback PR. The procedure therefore includes an expected brief Buddy outage.
- **WD-SLICE-011:** Normal Live activation requires the permanent approval
  Environment `workflow-delivery-v3-buddy-approval` and capability Environment
  `workflow-delivery-v3-buddy-github-packages` to be
  explicitly created, configured, and read back before `live_enabled` may
  become true. GitHub's implicit creation of a missing Environment is not
  accepted configuration. Before either final Environment is created, a
  protected implementation change while false must replace the transitional
  `workflow-delivery-v3-buddy-smoke-approval` and
  `workflow-delivery-v3-buddy-smoke-github-packages` names and marker values
  across workflow, source, record, formatter, validator, test, and current-state
  contracts with the final profile mappings. The resulting activation revision
  and its descendants must treat distinct exact Environment-scoped
  configuration markers as the first executable check in the two Environment
  jobs. Each check must map the marker through step `env`, perform a quoted
  case-sensitive shell comparison, and disallow `continue-on-error`; GitHub
  expression equality is not an exact comparison. Missing or mismatched markers
  must stop Authorization or publication before checkout, setup, artifact
  download, preflight, mutation marking, or publish. Every later operational
  step must require marker-check success. Any exceptional finalizer that can run
  after failure must remain non-mutating and must classify rather than mask the
  marker failure. Repository- and organization-scoped variables with the same
  names must be absent. These markers are configuration sentinels only: they do
  not prove reviewer, self-review, administrator-bypass, branch-policy, secret,
  or credential settings. Delivery Governance must still inspect and retain
  authenticated readback of those native settings. When a documented public
  API cannot configure or authoritatively read back a required control, the
  operator must retain authenticated post-save UI evidence; undocumented API
  fields may corroborate but cannot replace that evidence or be inferred false
  when absent. Historical selected refs remain within the accepted writer TCB
  and do not gain a repository-wide enforcement claim from revision-local
  marker checks. These names are the first-slice mappings of the shared Buddy
  approval profile and Buddy GitHub Packages capability profile. A future
  Release policy may reuse either identity only after its own Governance and
  threat decision and an exact profile-compatibility check; the shared
  Environment never supplies package eligibility or reusable approval.
- **WD-SLICE-012:** Successful destination acceptance and cleanup make the named
  slice eligible for a later production decision; they do not authorize or
  automatically activate normal Live. Readiness repair, the protected
  Environment-contract implementation rename, and permanent Environment
  configuration occur while `live_enabled` remains false. A protected
  preparation change must record fresh activation-gate evidence and refresh the
  Governance attestation while preserving false. A later, separately authorized
  protected activation change may set true. Before that activation change
  merges, operators must freeze all other `main` writes and normal Buddy
  dispatch. After merge they must bind rollout preflight to the exact activation
  merge SHA, preserve the freeze, capture the pre-dispatch run set, and dispatch
  `main` exactly once. Exactly one new
  `workflow_dispatch` run by the authorized operator must correlate to that
  SHA with `run_attempt == 1`; ambiguity blocks approval and must not cause a
  blind second dispatch. Normal Live remains enabled after the first run only
  if the canonical Attempt Outcome has result `success`; every required
  artifact and disposition-specific binding is retained; the disposition is
  either action-bearing publication with exact Capability Admission, durable
  result, and Receipt or canonical exact-satisfied no-action with no capability
  or Receipt lineage; destination ownership, bytes, in-package target witness,
  and target tag are exact; and no incomplete, unknown, conflicting, or
  possibly-mutated state remains.
  Otherwise operators freeze new dispatch, inventory every nonterminal run and
  deployment, promptly restore false through a protected change, drain or
  cancel only with correct capability-startedness classification, wait for
  terminal platform state, and reconcile read-only. Flag-off prevents future
  admission after fresh observation; it is not destination rollback and cannot
  revoke a publisher already past its final Governance check. Any later retry
  requires explicit reactivation and a fresh whole Attempt. It must not rewrite
  the original Attempt as successful.

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
  must be deterministic and unique across the complete workflow run with
  overwrite disabled. Every physical artifact name must incorporate
  `github.run_attempt` directly or through the deterministic hash preimage.
  Producers must capture artifact ID, digest, and URL. Consumers must fetch only
  by artifact ID and verify name metadata, producer, `github.run_id`,
  `github.run_attempt`, and digest for current-authority admission.
  Prior-attempt IDs, name fallback, and latest-artifact selection are rejected
  for current authority. History-only
  admission independently binds only immutable artifact ID/digest, workflow run
  ID, head SHA, payload integrity, and platform metadata actually exposed by the
  API. Job, run-attempt, and phase facts must be verified separately through
  Jobs and Run APIs. Producer-job, exact-run-attempt, and reusable-workflow
  claims inside a historical payload are diagnostic self-assertions, not
  authority. If strict historical workflow/attempt provenance becomes required,
  that capability is unsupported until Artifact Attestations or OIDC are
  separately approved; the first slice must not enable `id-token`.

### Observation, Replay, and Recovery

- **WD-OPS-001:** Every Release Attempt must observe all destinations before
  requesting publication capability. Observation must classify each logical
  projection atomically against snapshot-bound desired projection state, not
  Product or Execution Identity. Desired state must include the exact destination
  coordinate, expected ownership, immutable in-package target witness, target
  binding, qualified artifact bytes or digest, and every required destination
  routing projection. For first-slice npm this includes the exact immutable
  dist-tag `buddy-sha-<40-lowercase-target-sha>` mapped to the frozen native
  version. The tag is routing, not provenance. Desired state is derived from the
  Qualification Snapshot and admitted artifacts.
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
  rely on whole-release replay. Successful durable creation establishes the
  observable package binding, while a pre-mutation failure reserves nothing.
- **WD-OPS-003:** Release retry must use whole-release replay. GitHub
  `Re-run failed jobs` is not a supported recovery protocol.
- **WD-OPS-004:** Every replay must rerun request-local Repository Model
  compilation, planning, build, qualification, authorization checks,
  observation, and reporting for the complete Release Attempt. It must not
  reuse an older Attempt's Repository Model, Qualification, or Publication
  Snapshot. `Re-run all jobs` must compile a new Snapshot bound to the new
  `github.run_attempt` even when request identity, `github.run_id`, and target
  remain unchanged.
- **WD-OPS-005:** A control-code fix creates a new candidate or Release target
  revision. Ordinary replay of an older target must continue using that
  target's original control code.
- **WD-OPS-006:** Publication must use append-only Saga semantics. A successful
  destination must not be automatically rolled back solely because another
  destination fails.
- **WD-OPS-007:** Reconciliation must be exceptional handling for destination
  state that cannot safely proceed through normal observation and replay.
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
  Adapter-required keys. The first-slice npm publish is one compound action
  whose complete key set includes both the External Package Coordinate and
  destination/package/dist-tag mutable resource; no separate normal tag
  mutation is permitted. Its GitHub concurrency group uses the conservative
  shared destination/package projection so every action touching the same
  destination and npm package name serializes, including actions with different
  target-derived tags. Non-package keys and any safe serialization projections
  are defined by the Destination Adapter contract. Missing, unknown,
  incomplete, conflicting, or unenforceable required keys or projections must
  block live publication. Request-local Repository Model compilation occurs
  before execution concurrency. The surviving concurrency-scoped caller then
  invokes one same-revision reusable live-Attempt workflow from admission
  through finalization and holds the Release Execution identity slot for that
  entire admitted Attempt.
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
  a distinct Attempt in the existing Release Execution and must not create a
  second Execution for the same identity. A pending dispatch replaced or
  coalesced before execution is not admitted and creates no Attempt.

### Retention and Platform State

- **WD-RET-001:** Caches must be treated as non-authoritative performance
  mechanisms.
- **WD-RET-002:** Workflow Delivery must not assume that GitHub Actions
  artifacts or logs outlive the configured platform retention window.
  First-slice live Release control and artifact retention must exceed the
  platform Environment approval-expiry window with operational margin; the
  initial LLD uses 45 days and activation is blocked if repository policy cannot
  provide it. Retention and a pending Environment approval do not freeze or
  extend Governance validity: immediately before capability admission the live
  flag must still be enabled and the at-most-90-day attestation must still be
  unexpired and provenance-identical to the admitted eligibility input.
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
  explicitly accepted and bounded first-slice Buddy publication risk documented
  by `WD-SLICE-*`.
- **WD-NFR-002 - Explainability:** Operators and reviewers must be able to
  understand why scope, obligations, authorization, actions, and verdicts were
  selected.
- **WD-NFR-003 - Evolvability:** Adding an ecosystem or destination should
  normally require an adapter and policy mapping, not changes to cross-system
  authority semantics. A future Buddy destination must still make its own
  explicit threat and cost decision and cannot inherit `WD-SLICE-*`.
  Environment identities should be reused through exact Approval or Capability
  Environment Profile matching rather than duplicated for every package or
  slice; a policy difference requires a distinct profile and identity.
- **WD-NFR-004 - Recoverability:** Retry and remediation must preserve identity,
  authority, and append-only history across partial external side effects.
- **WD-NFR-005 - CI latency:** Ordinary pull-request CI has a P95 12-minute
  Final Decision objective. Broad authority, policy, toolchain, and
  multi-Release-Unit changes are measured separately.
- **WD-NFR-006 - Performance safety:** Performance work must not weaken
  obligation coverage, artifact variant coverage, Evidence Admission, or
  authorization.

## Non-Goals

Workflow Delivery v3 does not:

- replace ecosystem build and package-management tools;
- become a general workflow engine;
- provide distributed transactions across destinations;
- promote pull-request artifacts into Release;
- consume CI results as Release Evidence;
- certify reproducible builds through duplicate building;
- provide a permanent external Release ledger in the initial scope; or
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
