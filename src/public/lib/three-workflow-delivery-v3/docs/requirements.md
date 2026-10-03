# Workflow Delivery v3 Requirements

## Status

This page defines the requirements for the repository-internal V3 contraction.
It owns intended product behavior and scope, including the requirements changes
accepted for the contraction. Architecture and implementation alignment follow
these requirements; their completion is not a prerequisite for agreeing them.

The [Delivery Wave](../../../../../docs/delivery-wave.md) owns work authorization.
The [handoff](./agent-handoff.md) owns operating entry points and retained limits.
Requirement identifiers remain stable; retired identifiers below are navigation
markers, not continuing obligations.

## Requirements and Implementation Transition

These requirements govern new contraction work. The
[HLD](./high-level-design.md) defines the replacement architecture and order;
the [middle-level design](./middle-level-design.md) defines its component,
selection, execution, transfer and outcome contracts. The
[implementation plan](./migration-strategy.md) maps their concrete callers and
validation dependencies. These documents do not claim implementation cutover.
The existing glossary, ecosystem LLDs and pinned former migration plan, together with the
[pinned former MLDs](./README.md#normative-hierarchy), describe the pre-contraction
implementation and retained evidence contracts for their actual consumers.
They do not prescribe the
replacement or reinstate retired requirements. Their references to confirmed
requirements and architecture refer to that baseline; affected HLD references
use its pinned historical version.

For interpreting existing code and historical evidence, use the
[pinned pre-contraction requirements][implementation-requirements] with those
implementation records. That historical text is not a second authority for new
product requirements. Do not change original evidence or reclassify terminal
outcomes to match the new completion or authorization semantics.

All npm, NuGet, Python and Ruby proving campaigns are complete and stopped.
Neither this requirements change nor the contraction work grant reopens a
campaign, restores a spent slot, changes native permissions or authorizes a
publication. Existing operation protocols continue to bound their historical
readers and any separately authorized operation; future operations require their
own concrete authorization and applicable protocol.

For each affected scope, confirm these requirements first, reconcile the HLD
and necessary contracts next, then implement and validate before switching its
callers. Update the relevant implementation-baseline notice when that scope is
aligned. Any feasibility conflict returns to an explicit requirement decision.
Do not assume the current implementation already satisfies the replacement
requirements or silently keep removed mechanisms as new design obligations.

[implementation-requirements]: https://github.com/hcoona/three/blob/f1c3ec1039625464d76cf09a06bd2363fdaaf056/src/public/lib/three-workflow-delivery-v3/docs/requirements.md

## Mission

Workflow Delivery validates changes and delivers software from this polyglot
monorepo using GitHub Actions and native ecosystem tools.

It supports five journeys:

1. Validate an exact change candidate using meaningful impact selection, or
   perform an explicitly requested full validation.
2. Exercise release preparation and qualification from a selected development
   revision without publication capability.
3. Publish a qualified release unit to Buddy from an allowed repository ref.
4. Publish a qualified release unit to Official from an eligible official ref.
5. Report existing, partial, conflicting or uncertain destination state and
   support bounded ordinary recovery where the destination contract permits it.

CI determines whether a candidate satisfies its applicable quality requirements.
Release independently builds and qualifies the complete requested output set,
then determines whether the selected destination satisfies the release request.
The system explains scope, results and external effects sufficiently for an
operator to act on them.

When qualities conflict, prioritize security and correctness, then traceability
and explainability, then evolvability and recoverability, then latency and cost.

## Design Assumptions

Rely on documented Git, GitHub Actions, identity-provider, ecosystem-tool and
registry contracts within their actual limits. The application owns its request
bindings, input selection and result checks. It does not reimplement platform
approval, scheduling or registry concurrency guarantees.

If a required lower-layer capability is unavailable, report the affected path
as unsupported. Application locks, reservations, ledgers or repeated probes
cannot establish a missing native guarantee. Finite observations can establish
integration evidence, not universal service behavior.

Native destination differences include identity normalization, file-level
versus package-level creation, duplicate responses, visibility delays, metadata,
credential scope and administrative deletion. An adapter must state the native
semantics on which its supported operations depend. Do not assume atomic
multi-file publication, global read consistency, permanent availability or
permanent coordinate reservation where the destination does not provide them.

An authorized writer of publication control code is inside the publisher trust
boundary. Environment approval does not independently validate that code.
Intended-package checks cannot narrow the actual package reach of a repository
token. Configuration review must acknowledge effective credential scope; the
application must not claim isolation beyond that scope.

## System Requirements

### System Scope and Separation

- **WD-SYS-001:** Provide CI and Release as distinct business capabilities with
  independent qualification and publication decisions. This does not require
  matching internal models, components, record hierarchies or job graphs.
- **WD-SYS-002:** CI and Release must use consistent native repository,
  dependency, version and build semantics for the same subject. Sharing their
  implementation is justified by actual common behavior, not required symmetry.
- **WD-SYS-004:** Native platform permissions, Environment protections and
  destination publisher configuration own publication authorization. A workflow
  must not grant itself authority or maintain a parallel dynamic admission
  authority, reviewer mirror or platform-approval proof system. A bypass allowed
  by effective native policy counts as native authorization.
- **WD-SYS-005:** Each decision or external action must identify its immutable
  source target and complete intended scope. The application must preserve the
  binding between request, qualified outputs and destination across actual
  execution and permission boundaries.
- **WD-SYS-006:** Missing or conflicting facts required to establish scope,
  eligibility, qualification or completion must produce an explicit failure or
  unknown result; they must not be interpreted as success or non-applicability.

### CI Qualification

- **WD-CI-001:** Identify the exact candidate tree and the comparison basis
  appropriate to the event, including PR merge, merge-group and push inputs.
- **WD-CI-002:** For supported repository structures, derive affected projects
  and release units from changed paths, native dependency facts, shared inputs
  and explicit relationships unavailable from native tools. Include required
  transitive consumers and exclude work known to be unrelated. The system need
  not prove a mathematically minimal scope or model arbitrary build systems.
- **WD-CI-003:** Establish the complete required check and artifact scope before
  execution. Genuine global inputs and documented native workspace or aggregate
  operation boundaries may broaden scope, with the reason visible. Unknown
  ownership, failed native evaluation, missing dependency facts or an unsupported
  relevant structure must fail planning. Automatic full execution must not hide
  analysis failure or be reported as successful impact selection.
- **WD-CI-004:** Build every publishable artifact variant of each affected
  release unit. Selection narrows the affected units, not their required
  variant coverage.
- **WD-CI-005:** Run all required checks selected by each project's effective
  quality requirements and distinguish them from advisory checks. Executors
  must not silently remove, substitute or downgrade required work.
- **WD-CI-006:** CI succeeds only when every required result is present and
  satisfied for the selected candidate and scope. Missing, skipped, cancelled,
  timed-out, unknown or conflicting required outcomes cannot become success.
- **WD-CI-007:** Report an explainable candidate-bound result through the
  applicable GitHub check. Preserve enough information to reconstruct the scope
  and required outcomes; no additional internal verdict database is mandated.
- **WD-CI-008:** CI must have no publication capability and must not authorize
  Release.
- **WD-CI-010:** HK owns source and configuration conformance; CI owns project
  unit, scenario and integration checks. The workflow control package is tested
  when its implementation, tests, consumed definitions or actual dependencies
  select it, with one owner in ordinary CI. Unrelated changes must not select
  it. Local and explicit complete validation remain available. A control-package
  test result is not another project's qualification or Release qualification.
  Smoke dependencies have no special publication-admission rule.
- **WD-CI-011:** Provide an explicit full-validation mode separately from
  incremental selection. A result must state the scope actually covered; a
  supported-subset validation must not claim repository-wide completeness.
- **WD-CI-012:** Provide shared quality infrastructure and ecosystem presets
  while allowing project-specific choices and gradual adoption. Strengthening
  required policy semantics must be identifiable and explicitly adopted by
  affected projects; equivalent implementation repairs need not create a new
  policy version. Projects need not all use the same baseline. Required checks
  under the selected baseline remain mandatory. This does not require a general
  policy language, migration service or indefinite support for old presets.

### Release Delivery

- **WD-REL-001:** A request identifies one release unit, an immutable full Git
  commit, a channel and one selected destination. Identify its canonical NBGV
  version, native package coordinate and complete intended output set. Distinguish
  the product/version from a particular execution and its external effects;
  this does not require a global product-to-commit index.
- **WD-REL-002:** Enforce the selected channel's ref eligibility and the actual
  destination authorization before a live side effect. Source and configuration
  declarations must not substitute for native permission enforcement.
- **WD-REL-003:** Select live publication or dry run explicitly. Bind planning,
  build and qualification to the selected revision and the relevant build,
  dependency and version inputs. Missing history or native facts needed for a
  correct NBGV result block planning. Internal snapshot counts, record order and
  serialization boundaries are design choices.
- **WD-REL-004:** Release builds and qualifies its own complete output set. CI
  artifacts, checks and verdicts do not substitute for Release qualification;
  cross-run CI artifact promotion is outside the initial scope.
- **WD-REL-005:** CI and Release must use equivalent build definitions for the
  same variant and inputs. Build, package metadata and publication must agree
  with the selected native NBGV projection. Do not invent versions, use fallback
  fields or append execution-derived suffixes to work around invalid input.
- **WD-REL-006:** Ordinary rebuild-based recovery requires identical original
  package bytes for the same target, relevant frozen inputs and supported
  toolchain. Routine releases need not duplicate-build to certify this. Do not
  require equal package bytes across different commits: native source metadata
  may change even when the product version and project content do not. Unrelated
  changes must not force republishing unchanged products. Genuine shared build
  and dependency inputs belong in version and impact declarations.
- **WD-REL-007:** Before a publication authorization gate, expose the exact
  target, destination, package/version, complete artifact set and intended
  operations for review when native policy requires review. Preserve the binding
  from that request to the actual publication. Rely on the native gate; do not
  mirror reviewers, replay approval/deployment history or create an additional
  application authorization certificate merely to prove it passed. An already
  satisfied request needs no write capability or write-approval ceremony.
- **WD-REL-008:** Only the authorized publisher may acquire the destination's
  supported short-lived write capability. Build and qualification have none.
  Observation uses public access or the minimum required read-only authority.
  Authorization failure must not trigger a long-lived-credential fallback.
- **WD-REL-009:** Keep upload-command results and business request completion
  distinct. Record success, failure or ambiguity of the actual command accurately.
  While an execution remains active, bounded authoritative verification may
  establish completion despite a lost or unsuccessful upload response, but only
  if the complete intended state and all required qualification, artifact,
  destination and applicable consumer conditions are established. Version-label
  equality or a successful command alone is insufficient. Completion by observed
  state must not claim that this invocation created the remote objects.
- **WD-REL-009A:** Report whether the request is satisfied, whether mutation was
  attempted, the command outcome and any unresolved effects. Do not convert a
  terminal failure into success retrospectively or invent missing publication
  evidence from a platform job's success. Missing final records after publication
  may leave effects unknown. Platform results remain authoritative for native
  execution facts; application checks establish business completion.
- **WD-REL-010:** Recovery must not depend on exhaustive discovery of prior
  Actions runs, artifacts or an application-maintained release history. Use the
  current request, the business data required across its real boundaries and
  authoritative destination observation.
- **WD-REL-012:** Qualify both artifact contents and clean native consumption of
  the complete release set. A project-reference substitute, populated consumer
  cache or source checkout must not hide missing package contents or undeclared
  consumer prerequisites. Concrete checks and supported runners follow the
  project's selected quality contract and ecosystem semantics.

### Buddy and Official Channels

- **WD-CHN-001:** Buddy provides distributable previews using destination,
  coordinate and capability policy distinct from Official. It permits real
  publication from allowed development branches; this is ordinary channel
  behavior and is not a smoke-only exception. Channel isolation need not change
  the canonical product-version string and must reflect actual token reach.
- **WD-CHN-002:** Official publication uses the canonical NBGV version and its
  correct native projection from an eligible official branch or tag, such as the
  configured `release/*` refs. NBGV can give different repository commits the
  same public product version when relevant version inputs are unchanged. A
  version label alone does not identify exact package bytes or prove two release
  requests equivalent.
- **WD-CHN-003:** Buddy outputs and results must not be reclassified or promoted
  as Official qualification or publication.
- **WD-CHN-004:** Dry run must exercise the selected revision's release code,
  build and qualification, including from development branches. It must acquire
  no publication capability, perform no destination mutation and make no live
  completion claim. Report hypothetical operations and the boundary it actually
  exercised. Do not require live ref eligibility merely to test release code.

### Governed Control Code

- **WD-AUTH-001:** CI control comes from the candidate revision; Release control
  and product source come from the selected release revision. Official uses
  eligible reviewed official refs; Buddy uses its allowed refs; dry run uses the
  selected simulation revision. Separate tooling-version compatibility is not
  an initial requirement.
- **WD-AUTH-002:** Control-code and configuration changes follow the repository's
  applicable review rules and the channel's native platform protections. These
  controls belong to those authorities, not a duplicate V3 runtime policy file.
- **WD-AUTH-003:** A reviewed control-code change becomes usable as part of the
  resulting revision without an independent application promotion protocol.
  Changes elsewhere in Git history must not invalidate a running request merely
  because a former Governance path was touched or restored. Revocation and
  cancellation use native platform controls within their actual limits.

### Trust and Capability Isolation

- **WD-SEC-001:** Execution of project, build or package-defined code must not
  share publication credentials or a mutable execution context with privileged
  publication control. Native metadata evaluation that executes project code
  belongs in the unprivileged context.
- **WD-SEC-002:** Scope and result decisions consume the required native facts
  and outputs without allowing project-code execution to alter trusted control.
  An ordinary internal function boundary alone does not require a new security
  proof, persistent record or independent job.
- **WD-SEC-003:** The publisher uses the selected control revision and verified
  qualified artifacts. It must not run product build code, gemspec evaluation or
  package lifecycle scripts with publication capability. Bind the actual request
  to package, version, destination and outputs; trust in the platform does not
  establish bindings that application code itself selects.
- **WD-SEC-004:** Use the narrowest supported credential scope and acknowledge
  broader platform grants. Provisioning and configuration changes must establish
  the intended Environment and publisher settings. Do not continually duplicate
  those settings in dynamic admission files or claim that an application check
  makes a broad credential package-specific.

### Observation, Retry, and Recovery

- **WD-OPS-001:** Observe the selected destination before mutation and compare
  its native coordinates and actual required artifact state with the qualified
  set. The adapter reports absent, exact, partial, conflicting or unknown state
  where applicable. Routing labels are not source or artifact identity.
- **WD-OPS-002:** Exact complete state can satisfy a request without a write.
  Absent state can permit native creation. Conflicting or unknown required state
  blocks mutation. A supported, explicitly verified exact subset can permit
  missing-file completion in a fresh request; this is not a general promise to
  repair partial releases. Never implement publication by active-version
  overwrite, delete-and-recreate or blind mutation retries. Stop further mutation
  after ambiguity; bounded read-only verification may still establish completion
  under WD-REL-009. Incomplete or uncertain sets remain incomplete or unknown.
- **WD-OPS-003:** Ordinary live recovery starts a new manual dispatch. GitHub job
  reruns are not a supported live recovery mechanism.
- **WD-OPS-004:** A recovery dispatch targeting the original release uses its
  original source/control revision and relevant inputs, rebuilds and qualifies
  its own complete set, reobserves the destination and obtains current native
  authorization when a write remains. It must not reuse prior approval or
  silently substitute older artifacts. A same-version rebuild from another
  commit is not proof of exact recovery; mismatched bytes block publication.
- **WD-OPS-005:** A control-code fix produces a new revision. Ordinary recovery
  does not combine repaired latest tooling with an older target. A defect may
  require a new product version or a separately decided exceptional operation;
  no universal repair or resume engine is required.
- **WD-OPS-006:** Each request targets one destination. Native multi-file upload
  may expose partial state. No cross-registry transaction, rollback, compensation
  or full-set atomicity is promised beyond native capabilities.
- **WD-OPS-007:** State that cannot safely continue through supported observation
  and a fresh dispatch requires a separate operator decision. Normal publication
  must not delete, restore, retarget or change access as automatic repair.
- **WD-OPS-008:** Exceptional administrative corrections use separately
  authorized platform operations and retain the relevant incident and
  before/after information without rewriting release history. A generic
  remediation subsystem is outside the product scope.
- **WD-OPS-009:** Preserve appropriate source information through the ecosystem's
  native package metadata and applicable platform provenance mechanisms. Keep
  the source-to-artifact-to-destination binding needed by the request. Do not
  require V3-specific in-package JSON, source-code inclusion, an identical commit
  field across ecosystems, or a universal attestation service. Native metadata
  availability and assurance differ and must be stated by the adapter; none
  substitutes for checking the actual required artifact state.

### Evidence, Decisions, and Explanation

- **WD-EVD-001:** At an actual transfer or trust boundary, validate the target,
  purpose, scope, producer and artifact integrity needed by the consumer. Use
  native transport guarantees where they suffice; do not reconstruct their own
  proof chain. Internal trusted calls need no serialize-and-readmit ceremony.
- **WD-EVD-002:** Preserve terminal results and uncertainty. A later observation
  or new request can add information but must not erase or relabel earlier
  failures. This does not require a permanent append-only application ledger.
- **WD-EVD-003:** CI explanation connects relevant changes and dependency reasons
  to selected projects, variants, required checks and outcomes, including the
  reason for any legitimate broad selection or planning failure.
- **WD-EVD-004:** Release explanation connects target, version, channel, artifact
  set, destination, authorization boundary, observed state, attempted effects,
  command results and business completion, with the next supported operator action.
- **WD-EVD-005:** Persist business data when it must cross a permission or process
  boundary or survive interruption and recovery. In particular, interrupted
  publication must not be mistaken for proven absence of effects. Record formats
  and write placement follow those concrete consumers; every conceptual stage
  does not need a durable record. Optional telemetry failure does not change an
  otherwise established business result.
- **WD-EVD-006:** Artifact transfers must identify the intended immutable output
  and enforce the relevant provenance and integrity bindings. A mutable display
  name or an unqualified latest-artifact lookup is insufficient. Rely on native
  immutable artifact identity and integrity facilities where applicable.

### Retention and Platform State

- **WD-RET-001:** Caches are performance mechanisms, not evidence of successful
  qualification or authoritative release state.
- **WD-RET-002:** Retain the artifacts and business information required across
  approval waits and the supported recovery period within actual platform
  retention limits. Expiry cannot be ignored or treated as permission to invent
  missing facts. Concrete durations belong in the supported operation design.
- **WD-RET-003:** Longer-lived source and publication information may use native
  package metadata, tags, destination records and platform releases or
  attestations where appropriate. No uniform capability is assumed.
- **WD-RET-004:** No permanent external release ledger, global product-to-target
  binding index or mandatory per-unit GitHub Release audit anchor is required.
- **WD-RET-005:** If required state cannot be established after record expiry,
  report that limitation and block the affected operation. Native current-state
  observation may suffice where its contract allows it; absent active state
  does not prove a coordinate never existed or authorize restoration.

### Concurrency

- **WD-CON-001:** CI may cancel work superseded by a newer candidate.
- **WD-CON-002:** Select semantic work and real prerequisites in V3; delegate
  job execution, waiting and concurrency to GitHub Actions and native tools.
  Serialize this repository's publication work by destination registry and
  package using supported native concurrency. Do not require general resource
  sets, overlap locks or arbitrary dynamic task-graph execution.
- **WD-CON-003:** Do not automatically cancel an in-progress live publication
  when another request arrives. Explicit platform cancellation can still leave
  uncertain external effects and requires ordinary recovery handling.
- **WD-CON-004:** External writers are coordinated by the destination's native
  guarantees. V3 must handle their observable results but cannot claim a global
  lock, ordering guarantee or transaction across platforms.
- **WD-CON-006:** Duplicate pending requests may be rejected or coalesced using
  native workflow semantics, without an unbounded application queue. Do not
  promise FIFO execution, fairness or lossless enqueueing beyond the platform.

## Quality Attributes

- **WD-NFR-001:** Security and correctness take precedence within the stated
  platform and accepted-writer trust boundary. Scope claims must not overstate
  the protection supplied by application checks or native credentials.
- **WD-NFR-002:** An operator must be able to understand scope, checks,
  authorization, actions, outcomes and uncertainty from the ordinary result.
- **WD-NFR-003:** New ecosystems, destinations or special projects may require
  explicit adapter implementations. Use common business contracts where behavior
  is shared without requiring all native capabilities to be symmetric.
- **WD-NFR-004:** Bounded recovery must preserve request identity and truthful
  external-effect information without depending on exhaustive platform history.
- **WD-NFR-005:** Ordinary PR CI has a P95 12-minute result objective. Genuine
  broad control, policy, toolchain and multi-unit changes are measured separately.
  Evaluate selectivity as well as latency; a fast blanket run is not evidence
  that incremental selection works.
- **WD-NFR-006:** Performance work must not weaken required checks, variant
  coverage, application binding checks or native authorization.
- **WD-NFR-007:** Validation should protect user journeys, impact correctness,
  actual boundary contracts and effect/recovery behavior. It must not fix the
  exact internal record count, component layout or job graph, or attempt to
  re-prove guarantees already owned by a supported platform.

## Non-Goals

The initial product does not replace native build tools; implement a general
workflow scheduler or quality-policy language; maintain dynamic Governance
admission files or a second approval system; provide cross-platform locks,
transactions or a permanent release ledger; prove arbitrary artifact equivalence
across commits; promote CI artifacts into Release; resume nondeterministic
publication from historical sealed artifacts; or automate general registry
administration and repair.

Smoke projects exercise these ordinary contracts. Their names, marker APIs,
initial support matrices, proving order, finite operation budgets and spent
authorizations belong to project descriptions, adapter/support designs and
operation or evidence records. They confer no special publication trust.

## Retired Requirement References

The markers below preserve existing identifiers and section links. Their target
requirements own the retained general behavior. Removed mechanisms do not remain
obligatory through these markers. The [implementation transition](#requirements-and-implementation-transition)
and pinned historical requirements preserve the interpretation of existing code
and evidence without carrying slice ceremonies into the new product contract.

### Retired Cross-Cutting Requirements

CI coexistence and cutover details remain in the [implementation migration record](./migration-strategy.md). Dynamic admission, approval mirrors, smoke-reference admission and general resource-set coordination are retired as product requirements.

- **WD-SYS-003:** Retired - current authority: WD-SYS-001
- **WD-CI-009:** Retired - current authority: WD-CI-011
- **WD-REL-007A:** Retired - current authority: WD-REL-009A
- **WD-REL-011:** Retired - current authority: WD-SYS-004
- **WD-CHN-005:** Retired - current authority: WD-CHN-001
- **WD-AUTH-004:** Retired - current authority: WD-REL-008
- **WD-AUTH-005:** Retired - current authority: WD-SYS-004
- **WD-AUTH-006:** Retired - current authority: WD-SYS-004
- **WD-SEC-005:** Retired - current authority: WD-REL-008
- **WD-OPS-002A:** Retired - current authority: WD-OPS-002
- **WD-CON-005:** Retired - current authority: WD-OPS-008

### First-Slice Buddy Accepted Risk Exception

Buddy now follows ordinary channel and trust requirements. The [npm LLD](./hcoona-release-smoke-npm-lld.md) and [retained evidence](./validation/native-and-normal-live-evidence.md) preserve concrete existing support, token reach and completed campaign limits.

- **WD-SLICE-001:** Retired - current authority: WD-AUTH-001
- **WD-SLICE-002:** Retired - current authority: WD-REL-007
- **WD-SLICE-003:** Retired - current authority: WD-REL-008
- **WD-SLICE-004:** Retired - current authority: WD-SEC-004
- **WD-SLICE-005:** Retired - current authority: WD-SEC-004
- **WD-SLICE-006:** Retired - current authority: WD-OPS-007
- **WD-SLICE-007:** Retired - current authority: WD-SYS-004
- **WD-SLICE-008:** Retired - current authority: WD-SYS-004
- **WD-SLICE-009:** Retired - current authority: WD-REL-012
- **WD-SLICE-010:** Retired - current authority: WD-OPS-003
- **WD-SLICE-011:** Retired - current authority: WD-SYS-004
- **WD-SLICE-012:** Retired - current authority: WD-OPS-003

### NuGet Second Slice

The [NuGet LLD](./hcoona-release-smoke-github-packages-lld.md) retains the existing package, Windows runner and native identity/support details. [Completion evidence](./validation/nuget-normal-live-evidence.md) retains the bounded native and publication observations; their scope is not widened here.

- **WD-NUGET-001:** Retired - current authority: WD-REL-001
- **WD-NUGET-002:** Retired - current authority: WD-REL-005
- **WD-NUGET-003:** Retired - current authority: WD-SEC-001
- **WD-NUGET-004:** Retired - current authority: WD-SEC-004
- **WD-NUGET-005:** Retired - current authority: WD-REL-012
- **WD-NUGET-006:** Retired - current authority: WD-OPS-002
- **WD-NUGET-007:** Retired - current authority: WD-REL-009
- **WD-NUGET-008:** Retired - current authority: WD-SYS-004

### Python Smoke Slice

The [Python LLD](./hcoona-release-smoke-python-lld.md) retains native version, wheel/sdist, consumer and supported subset-recovery details. [Bootstrap](./validation/python-bootstrap.md), [recovery](./validation/python-hosted-recovery.md) and [normal publication evidence](./validation/python-normal-live-evidence.md) retain the spent operation limits and original outcomes.

- **WD-PY-001:** Retired - current authority: WD-REL-001
- **WD-PY-002:** Retired - current authority: WD-REL-005
- **WD-PY-003:** Retired - current authority: WD-REL-012
- **WD-PY-004:** Retired - current authority: WD-REL-004
- **WD-PY-005:** Retired - current authority: WD-OPS-002
- **WD-PY-006:** Retired - current authority: WD-OPS-002
- **WD-PY-007:** Retired - current authority: WD-SYS-004
- **WD-PY-008:** Retired - current authority: WD-SYS-004
- **WD-PY-009:** Retired - current authority: WD-SEC-004

### Ruby Smoke Slice

The [Ruby LLD](./hcoona-release-smoke-ruby-lld.md) retains the existing gem, native version and runner support. The [operation protocol](./ruby-operation-protocol.md) and [completion evidence](./validation/ruby-normal-live-evidence.md) preserve stopped destinations, spent slots and historical replay. No bootstrap or native operation is reopened.

- **WD-RUBY-001:** Retired - current authority: WD-REL-001
- **WD-RUBY-002:** Retired - current authority: WD-REL-005
- **WD-RUBY-003:** Retired - current authority: WD-REL-006
- **WD-RUBY-004:** Retired - current authority: WD-REL-009
- **WD-RUBY-005:** Retired - current authority: WD-OPS-002
- **WD-RUBY-006:** Retired - current authority: WD-SYS-004
- **WD-RUBY-007:** Retired - current authority: WD-SEC-004

## Requirements Stage Exit

Requirements review is complete when the supported journeys, mandatory behavior,
non-goals and platform assumptions have explicit dispositions and no unresolved
product decision is hidden in implementation detail. Architecture reconciliation
then maps the accepted behavior to responsibilities and necessary contracts.

Later design cannot silently weaken these requirements. Support and completion
claims require the corresponding implementation and validation; requirements
acceptance alone supplies neither. The Wave entry is removed when its complete
bounded contraction outcome is accepted, not merely when this requirements stage
finishes.
