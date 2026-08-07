# Workflow Delivery v3 High-Level Design

## Status

Architecture version: **v3**.

Review state: **Confirmed; Release identity model reconfirmed on 2026-08-05**.

This page is the normative high-level design for the clean v3 implementation
line. It realizes the confirmed
[Workflow Delivery v3 Requirements](./requirements.md) and intentionally does
not inherit the v1 or v2 control-plane architecture.

Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

## Architectural Shape

```text
                         Delivery Governance
             policy authority / review / environments / OIDC
               |                                     |
        CI Qualification                       Release Delivery
      planner / finalizer                    planner / finalizer
               |                                     |
               +------------------+------------------+
                                  |
                           Shared Foundation
 record / digest / repository / artifact / provenance / build / quality / clients
```

CI Qualification and Release Delivery are peer bounded contexts. Delivery
Governance is an external authority boundary, not a third business system. The
Shared Foundation provides mechanisms and normalized facts, not business
policy.

CI and Release each own their Planner, Finalizer, aggregate roots, Plans,
Evidence, Decisions, and state machines. There is no universal cross-system
Planner or Finalizer.

## System Boundaries

### CI Qualification

CI Qualification:

- evaluates the GitHub candidate tree;
- maps changed paths through Project Nodes, dependency relationships, and
  global inputs to affected Release Units;
- closes the complete Qualification Target;
- builds every publishable variant of each affected Release Unit;
- executes required and advisory quality obligations;
- admits Evidence and emits an explainable Final Decision; and
- publishes the latest authoritative Decision through the required GitHub
  check.

It has no publication side effects and does not authorize Release.

### Release Delivery

Release Delivery:

- accepts a Release Intent for one Release Unit and immutable target commit;
- verifies target eligibility through Delivery Governance;
- independently plans, builds, and qualifies the complete Release Unit closure;
- creates a Publication Snapshot bound to exact artifacts and destination state;
- obtains destination-specific Publication Capabilities;
- performs publication through Side-Effect Zone adapters;
- persists a Receipt for every completed mutation, records capability-group
  results, and derives the final Release outcome; and
- handles retry through whole-release replay.

It does not consume CI Plans, Evidence, artifacts, status checks, or verdicts.

### Buddy and Official

Buddy and Official are Release policy channels over the same Release machinery.

- Buddy produces distributable but non-authoritative previews through isolated
  channel, destination, package-coordinate, and Capability boundaries. Its
  package version remains the frozen native NBGV product version.
- Official Product Identity is channel, Release Unit, and canonical NBGV
  version. Official Release Execution Identity adds the immutable target.
  Different targets with the same Product Identity are separate Executions.
  Ecosystem publication and dry-run use the exact frozen native NBGV projection,
  such as `npmPackageVersion`, unchanged. Authorization binds the immutable
  Publication Snapshot.
- Buddy Release Execution Identity is channel, Release Unit, and immutable
  target.
- Buddy and Official may use the same product-version string when their
  complete destination coordinates are isolated.
- Buddy artifacts, Evidence, and Receipts are never promoted to Official.
- Non-authoritative branches may exercise Official dry-run behavior but cannot
  obtain live Official publication capability.

### Delivery Governance

Delivery Governance controls:

- protected branch and target eligibility;
- policy and control-code review;
- protected environments and required reviewers;
- OIDC and registry trust policy;
- Publication Capability grant and revocation; and
- Break-Glass Remediation approval.

A business workflow may request authority but cannot grant authority to itself.

## Repository Facts and Core Domain Model

### Project Node

A Project Node is a normalized technical fact discovered from an ecosystem
manifest or workspace. Examples include a .NET project, Python project, or
JavaScript package.

Project Nodes and their dependency relationships describe how the ecosystem
builds software. They are not independently authored business objects and do
not define ownership, qualification policy, versioning, or publication
identity.

Repository Model Providers also identify global inputs and path relationships
that affect Project Nodes, such as `Directory.Build.props`, workspace
configuration, or an explicitly declared cross-ecosystem generated-source
dependency.

### Release Unit

A Release Unit is an independently versioned and delivered product unit
containing one or more artifact variants and the Build Definitions needed to
produce them.

Release Units are explicit business objects. Their Build Definitions select
entry points, while ecosystem build systems retain responsibility for internal
project dependency closure and output composition.

### Qualification Target

A Qualification Target is the immutable object covered by a quality decision.

- CI derives it from a candidate change, affected Project Node closure, affected
  Release Units, and applicable repository obligations.
- Release derives it from the complete Project Node and declared-input closure
  needed by the Release Unit Build Definitions, the complete artifact variant
  set, and
  explicit compatibility obligations.

The target closes before execution. Unknown or unclassified scope is blocking.

### Aggregate Ownership

CI Qualification owns the CI Qualification Plan, CI Evidence admission state,
and CI Final Decision.

Release Delivery owns Release Intent, Release Attempt, Release Plan lineage,
Qualification Decision, destination observations, publication actions,
Receipts, and Release outcome.

Shared Foundation values may appear in either aggregate, but no aggregate
imports the other system's runtime state.

### Abstraction Discipline

A named domain abstraction must have concrete independent behavior, identity,
lifecycle, or policy responsibility. The architecture does not add a layer
solely to group existing objects or make the model appear more symmetrical.

Project Nodes remain technical repository facts because the current CI and
Release scenarios require no independent domain object between them and Release
Units.

## Shared Foundation

The Shared Foundation exposes six stable mechanism families:

1. Shared Record Primitives provide canonicalization, digest, strict record
   validation, and exact binding functions without defining a universal record
   envelope.
2. Artifact and Provenance Primitives separate logical, content, transport, and
   producer identity while leaving admission to CI or Release.
3. Repository Model Providers and the Repository Model Compiler normalize
   ecosystem manifests, workspaces, Project Nodes, dependency relationships,
   path impact, and capabilities.
4. Static Definition Catalogs describe Build, Quality, and other mechanism
   contracts without self-activating into a Plan.
5. Build and Quality Adapters execute closed family-specific invocations and
   return mechanical results rather than authoritative Evidence.
6. Execution and Client Primitives declare execution classes, capability
   requirements, mechanical outcomes, cache hints, and generic platform calls
   without creating jobs or granting authority.

Adding an ecosystem normally adds Provider, Build Adapter, or Quality Adapter
implementations and policy mapping rather than modifying context-owned decision
semantics.

Repository Model compilation emits target-bound canonical NBGV facts and the
required native ecosystem projections. CI and Release select and freeze the
exact projection in their Plans and Build Requests. Build Adapters apply and
verify that frozen value; they do not recompute NBGV, derive another version, or
fall back to ambient manifest fields. Before those facts are compiled, the
NBGV-owning Provider checks out and remains pinned to the exact target with
complete ancestry and tags, equivalent to `fetch-depth: 0`, and fails closed on
shallow or otherwise incomplete history.

Destination projection classification, action planning, Receipt semantics, and
remediation belong to Release Delivery. Shared Foundation may provide generic
GitHub or registry client primitives, but it does not own a Destination Adapter
business contract.

Providers provide normalized facts. Adapters execute closed mechanical
operations. Neither decides business scope, downgrades obligations, authorizes
publication, or reinterprets verdicts.

## Governance and Trust

### Context-Owned Planning and Finalization

CI Qualification owns CI scope planning, obligation disposition, Evidence
Admission, and CI Final Decision.

Release Delivery owns Release planning, qualification finalization,
Publication Snapshot finalization, Receipt admission, and Release outcome.

Shared Foundation supplies mechanical canonicalization, digest, strict record
validation, fact compilation, artifact and provenance identity, closed
invocation, result normalization, and binding functions. It does not choose
scope, policy, authorization, or verdicts.

### Governed Same-Revision Control

CI uses its Planner and Finalizer from the tested candidate revision. Live
Release uses its Planner and Finalizer from the exact selected target revision.
Official requires that target to be protected and authoritative. The
`hcoona-release-smoke-npm` live Buddy GitHub Packages slice may use any
same-repository ref selected by `workflow_dispatch`, without protected-ref or
CODEOWNERS-approved eligibility. Dry-run simulation uses the Planner and
Finalizer from the exact selected simulation revision and receives no approval
or live publication Capability. There is no independently selected
decision-code revision or runtime promotion protocol.

GitHub Governance supplies authority through control-code ownership, required
review, protected refs, workflow permissions, protected environments, and OIDC
trust. A change to planning, finalization, workflow control code, authoritative
record shape, minimum policy, executable Provider, Adapter, compiler,
authenticated client, static catalog, capability declaration, or cross-revision
compatibility code becomes eligible only as part of the reviewed revision that
contains it. The named first-slice Buddy exception instead accepts selected-ref
workflow, Planner, Finalizer, Providers, Adapters, compiler, authenticated
clients, static catalogs, capability declarations, and publisher without
protected-ref or CODEOWNERS eligibility. CI and Official, future Buddy and
production scopes, protected cross-revision compatibility code, and Break-Glass
Remediation remain owner-reviewed or separately governed.

A control-code fix therefore creates a new candidate or Release target. An
ordinary replay of an older target continues to use that target's original
control code. Exceptional state left by an older target is handled through
reconciliation or separately authorized remediation.

### Runtime Zones

The architecture has three runtime trust zones:

1. **Decision Zone:** Runs authoritative planning, Evidence Admission, and final
   decision logic from same-revision control code. It executes no
   target-defined project/build hooks and holds no publication credentials. For
   the named Buddy exception, the control code itself is branch-controlled.
2. **Build and Qualification Zone:** Executes candidate or release-target code.
   It holds no publication credentials and cannot approve itself.
3. **Side-Effect Zone:** Receives a short-lived destination Capability and
   consumes only verified immutable artifacts plus a fully materialized
   Publication Snapshot. It does not execute target-defined product/build code.

The zone separation above remains normative for CI, Official, simulation, and
future destinations unless separately approved. The first live Buddy
`hcoona-release-smoke-npm` GitHub Packages slice is a bounded exception: after
dedicated Buddy Environment approval and credential-free Capability Admission,
its target-revision side-effect job runs target-revision publisher code with
short-lived `GITHUB_TOKEN` and minimum
`packages: write`; this exception permits target-revision control and publisher
code, not execution of target-defined product/build code. It receives no PAT
and no `id-token: write`.

For that slice, Environment approval is the trust elevation for
branch-controlled publication code. The architecture does not claim an
independent protected publisher can constrain malicious target code after
approval, and approval is not cryptographic or independent semantic validation.
The reviewer must see target SHA and ref, exact package coordinate, artifact
digest and manifest, lifecycle scripts, and exact action summary.

Every repository actor with Write, Maintain, or Admin access is inside the
first-slice Buddy trusted publisher TCB. External/fork contributors and actors
without repository write are outside it and cannot manually dispatch the live
path under normal GitHub permissions. Environment approval is mandatory against
mistakes, accidental publication, and ordinary process violations, but it does
not impose a non-bypassable `GITHUB_TOKEN` permission ceiling against a
malicious repository writer. Such a trusted writer can create alternate
workflow YAML or jobs with `packages: write`.

The exception is limited to the dedicated disposable smoke package and isolated
GitHub Packages destination and Buddy Environment. The package has no normal
developer, CI, or production consumers. Access is minimized; delete, restore,
and planned permission, visibility, or admin actions are excluded from ordinary
publication; deletion and restore require Break-Glass handling. Latent
repository/package admin authority held by trusted actors remains accepted
misuse risk. Correct isolation prevents Official capability and known
Official/production package access. Rollout records actual token permissions
and package/repository grants and uses safe denial probes only for enumerated
unrelated assets; it does not claim universal negative reach proof. Other
reachable package operations under the smallest configured grants remain
accepted writer-TCB risk. Optional workflow-execution protections may reduce
who can execute workflows but are not required and are not treated as per-job
permission ceilings. If repository membership changes so that any
Write/Maintain/Admin actor is not trusted to publish, the slice blocks until
either that actor's repository access is reduced below Write/Maintain/Admin or
package-write Capability and destination access are placed behind an
independently enforced publisher boundary unavailable to writer-authored
workflows. Ref narrowing, Environment branch restrictions, CODEOWNERS, and
workflow-execution protections may remain defense in depth but are insufficient
remediation by themselves while an untrusted writer can author alternate
workflows with `packages: write`. Future Buddy destinations do not inherit this
decision, and neither does any production package.

The permanent root-HK dependency-policy gate scans manifests, lockfiles,
workflows, install scripts, and dependency configuration for normal
developer/CI/production consumption of the disposable package. It runs on
dependency-surface changes and unconditionally during `slice-validation`; a
consumer blocks live use and reopens the exception.

After activation, human Governance re-attests the writer TCB and
package/repository/Manage Actions access at least every 90 days and after
relevant role, team, or permission changes. Operators immediately disable v3
live pending reinspection and reacceptance. Runtime does not claim complete
writer or GitHub Packages grant enumeration; expiry bounds normal-flow
staleness.

The implementation PR merge is the direct repository-wide v1 Buddy-to-v3 Buddy
cutover. It lands v3 disabled and preserves no legacy Buddy compatibility.
The merge removes both legacy Buddy workflow files. Governance freezes Buddy
dispatch, disables both repository-level legacy workflow identities
(`buddy.yml` and `release-buddy.yml`), cancels or drains queued, waiting,
approval-pending, and running executions, and verifies disabled state, removal,
and old-ref dispatch rejection before destination acceptance. A guard that
exists only in new selected-ref YAML cannot close old-ref routes. All former
Buddy projects are unsupported until migrated into future v3 slices, and an
intentional Buddy outage is allowed. v1 Official and CI assets remain
unchanged; legacy Buddy workflows, Buddy-specific tests and matrices, and Buddy
documentation are excluded from that preservation and are retired or rewritten.

Destination acceptance then uses a temporary protected one-time workflow with a
non-Release purpose, exact approved target SHA, fixed acceptance-only coordinate
in the same disposable package, explicit confirmation, dedicated reviewer
Environment, and write permission only in probe jobs. It cannot create live
Release identity or history. Every probe independently requires
`github.run_attempt == 1`. The terminal evidence-capture job uses
`if: ${{ always() && github.run_attempt == 1 }}` or an exact equivalent so the
first attempt persists dependency outcomes, failures, and ambiguous mutation
evidence even when a probe dependency fails, then classifies incomplete or
unknown state for reconciliation. It still rejects non-first attempts. Partial
reruns therefore cannot reuse an earlier Environment review or coordinate. A
retry is a new reviewed workflow invocation with a new fixed disposable
coordinate/version. Governance captures the probes, removes the workflow,
bypass, and Environment, verifies removal, and only then enables the protected
attestation's boolean `live_enabled` field through a new protected commit.
Failed acceptance leaves all Buddy publication disabled and the temporary path
removed while legacy Buddy remains retired; recovery is reconciliation, not a
retained bypass or compatibility rollback. Restoring legacy Buddy requires a
separate user-approved rollback PR. The sequence therefore has an expected
brief Buddy outage.

## CI Qualification Design

### Flow

```text
GitHub event
  -> candidate identity (base/head/tested merge, merge-group, or push SHA)
  -> repository model facts
  -> closed CI Qualification Plan
       + opaque source-tree conformance
       + affected-system quality obligations
  -> parallel required and advisory execution lanes
  -> CI-owned Evidence envelopes
  -> required Evidence Admission
  -> immutable required Final Decision
  -> stable required-check projection
  -> non-authoritative advisory reporting
```

### Responsibility Split

The Planner owns semantic scope. It resolves the candidate identity, affected
Project Node and Release Unit closure, project-selected quality policy, concrete
quality targets and dimensions, artifact variants, and required and advisory
obligations.

The repository-root HK gate implements one required opaque
`SourceTreeConformance` definition. The Planner binds the definition and
candidate input but does not inspect HK profiles, steps, file applicability, or
internal planning.

For the first slice, HK adds a path-triggered v3 control-package pytest step for
the complete v3 control package/catalog/test tree, first-slice descriptors, the
exact first-slice Release policy, every v3 workflow consumer, direct Python
workspace/lock inputs, and HK configuration/helpers. Unrelated product source
alone does not trigger it. Manual `slice-validation` runs it unconditionally.
It remains internal to the opaque root-HK invocation and does not create another
CI obligation, Evidence record, or job.

Executors resolve only mechanical details required to perform an immutable
Plan. They may not add, remove, substitute, or downgrade planned scope.

Evidence producers execute in the Build and Qualification Zone. Evidence
Admission and Final Decision execute in the Decision Zone.

Success requires a ready Plan and `satisfied` Evidence for every required
obligation. Missing, skipped, canceled, timed-out, unknown, and conflicting
states cannot become success.

The CI Finalizer considers required obligations only. Advisory obligations use
Plan-bound Evidence and a separate non-authoritative Reporter so they do not
delay or indirectly gate the required check.

During first-slice coexistence, v3 CI has only two slice-scoped modes:

- a shadow pull-request incremental check for slice-relevant changes; and
- non-authoritative manual `slice-validation` of the complete
  `hcoona-release-smoke-npm` slice.

Neither becomes a Ruleset required check or replaces v1 required CI, and v1 and
v3 do not issue parallel authoritative Decisions. Manual slice validation is
not repository-wide full validation. Canonical explicit and scheduled full
validation remains the complete-repository mode defined by the CI MLD and is
deferred until every active Project Node, Release Unit, and repository
obligation is modeled.

## Release Delivery Design

### Flow

```text
manual Release Intent on selected Git ref
  -> pinned same-revision target
  -> branch by requested purpose
       live release:
         -> compile exactly one live-purpose request-local Repository Model Snapshot
         -> run exact-target Release-owned consumer scan and validate the
            fixed-source Governance attestation; emit current-attempt
            Live Eligibility Decision
         -> validate live channel, Release Unit, and target lineage eligibility
         -> derive Product/Execution Identity inputs from that Snapshot
         -> enter one Release Execution concurrency-scoped caller
         -> caller invokes same-revision reusable live-Attempt workflow
              -> admit; paginate and strictly admit retained same-Execution
                 history as history-only records
              -> snapshot admitted history IDs/digests
              -> create or join Execution and bind current Attempt to the
                 Live Eligibility Decision
              -> reuse the same Snapshot throughout the Attempt
              -> Qualification Snapshot, build, qualify, observe
              -> Publication Snapshot, authorization, Capabilities, mutations,
                 Receipts, Attempt Outcome, and Release Execution state
         -> caller holds the Execution identity slot through finalization
       release simulation:
         -> compile exactly one simulation-purpose request-local Snapshot
         -> create separately namespaced request-scoped Simulation Identity
         -> reuse the same Snapshot throughout the simulation pass
         -> simulate channel policy, build, qualification, observation,
            requirements, and hypothetical actions
         -> Simulation Outcome
         -> no live Product/Execution/Attempt identity, authorization,
            Capability, Receipt, or mutation
```

### Release Plan Lineage

Each Release Attempt has one logical Plan lineage with two immutable snapshots.

- Before live eligibility or identity lookup, the candidate run attempt branches
  to live release or release simulation. Each branch compiles exactly one
  same-revision Repository Model Snapshot for its own purpose and reuses it
  throughout the resulting live Attempt or simulation pass. Every admitted Fact
  Bundle and Snapshot bind purpose, request identity, `github.run_id`,
  `github.run_attempt`, target, producer, and control identity. Prior-run-attempt
  and cross-purpose artifacts are rejected. A replay or other new run attempt
  compiles a new Snapshot even when request identity, `github.run_id`, and target
  remain unchanged.
- For the named live Buddy slice, the exact-target Release eligibility stage
  runs after Snapshot compilation and before Product/Execution lookup,
  concurrency, history admission, or Attempt creation. It scans dependency
  surfaces independently of CI HK and validates a Governance-approved,
  non-executable human-inspection attestation from the immutable first-slice
  source contract: repository `hcoona/three`, exact protected ref
  `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`. The
  concrete Release policy or selected static Governance-policy catalog carries
  those exact fields. Eligibility verifies the fields and ref protection,
  resolves that ref once, and reads the blob at the resolved commit. The
  attestation inventories accepted writers and package/repository/Manage
  Actions access or binds its evidence digest, with issuer, inspection time,
  acknowledged limitations, expiry of at most 90 days, and a required top-level
  boolean `live_enabled` field. Using `contents: read`, eligibility freshly
  resolves the protected ref and reads the document at the resolved commit. The
  immutable Live Eligibility Decision
  binds the current purpose/request/run/attempt/ref/SHA, Repository Model,
  producer/control, policy/catalog, scanned surfaces and exceptions,
  the attestation's `live_enabled` value, attestation
  repository/ref/resolved commit/path/Git blob OID/canonical content SHA-256,
  and result. Only a current-attempt success
  transported by artifact ID/digest may enter Attempt binding. Missing,
  unreadable, malformed, expired, provenance-mismatched, disabled, or
  consumer-positive state blocks. The eligibility job receives only
  `contents: read`; effective `actions: read` remains confined to history
  admission and explicit `packages: read` to destination observation. Historical
  records and CI decisions cannot substitute. Runtime does not enumerate
  current writers or GitHub Packages grants. Relevant changes require an
  authorized human to promptly commit `live_enabled: false` to the protected
  source and later update and re-attest before restoring it to true. Protected
  review, merge, and fresh-read latency mean this is bounded operational
  response rather than instantaneous platform disablement; expiry separately
  bounds staleness. No repository variable, PAT, App, service, ledger, OIDC
  expansion, additional token permission, or malicious-writer protection is
  claimed.
- History-only admission is deliberately weaker than current authority.
  Platform attribution binds artifact ID/digest, source workflow run ID, head
  SHA, payload integrity, and metadata exposed by artifact/run APIs; Jobs/Run
  APIs separately provide attempt/job/phase facts. Producer-job,
  reusable-workflow, exact-attempt, purpose, and control claims inside retained
  payloads are diagnostic. History cannot satisfy current Evidence,
  authorization, artifacts, Receipts, outcomes, or eligibility. Strict
  historical provenance is unsupported without separately approved Artifact
  Attestations or OIDC, neither of which this slice enables.
- Attempt planning uses that same request-local Snapshot to compile complete
  channel policy and validate policy-selected variants and obligations,
  compatibility obligations, and required native projection selection. It
  selects and freezes the native projections from the Snapshot rather than
  deriving or recomputing them. The Qualification Snapshot freezes complete
  destination projections and coordinates, Adapter and version bindings,
  logical operations, potential action and dependency schema, capability
  policy, and deterministic complete mutable-resource-key derivation and
  enforceability basis. It does not freeze actual mutation actions or actual
  action key sets. The admitted request does not recompute the Repository Model.
- First-slice npm build output contains canonical
  `workflow-delivery/provenance.json` inside the tarball. The witness binds the
  immutable target, Release Unit, canonical/native NBGV facts, Build Definition,
  catalog/control digests, purpose, and schema, but no run or Attempt identity.
  Build, npm contents, and install/import qualification validate it. Remote
  observation downloads and hashes the tarball and extracts the witness;
  coordinate, ownership, witness, bytes, and the target-specific
  `buddy-sha-<40-lowercase-target-sha>` mapping to the frozen native version
  must all match. The tag is routing, not provenance. A sidecar alone is
  insufficient, and a different target is conflict even when version or tag
  claims otherwise.
- The Qualification Snapshot binds the request-local Repository Model Snapshot
  digest and freezes what must be built and qualified plus the deterministic
  pre-observation publication basis.
- The Publication Snapshot references the Qualification Snapshot and adds the
  exact artifact bytes and provenance, snapshot-bound desired state,
  observations, exact materialized action DAG and inputs, complete
  Adapter-declared key set for each actual mutation, groups, capabilities,
  Receipt contracts, and Decision.

The Publication Snapshot cannot alter fields frozen by the Qualification
Snapshot. A deterministic reviewer summary and canonical Snapshot JSON travel
as one immutable artifact. Governance approval and the Authorization Record bind
the Publication Snapshot digest plus that summary artifact's ID and digest;
mismatch fails closed.

For the first slice, live Buddy and Official simulation each qualify the built
npm tarball through distinct artifact-content and install/import obligations.
One physical tarball-dependent job may batch them, but it emits two Evidence
records and qualification requires both.

This structure preserves one Release Attempt identity while preventing
post-qualification mutation from changing what was qualified.

Retained prior Attempt bindings, outcomes, artifacts, and platform conclusions
are discovered under whole-Execution concurrency with read-only Actions access
before the current Attempt binding exists. Strict admission binds them only as
history for explanation and finalization; they cannot satisfy current Evidence.
Incomplete pagination, API denial, malformed records, duplicate or conflicting
bindings, and cross-Execution history block admission. After retention expiry,
no permanent ledger is required: provably absent or exact destination state may
proceed, while partial, conflicting, unknown, or unprovable state requires
reconciliation.

### Build Alignment

CI and Release use the same Build Definitions and Build Adapters.

CI builds every publishable variant of an affected Release Unit. Release
rebuilds every publishable variant of the Release Unit for its final target
commit and reruns all required Release quality obligations. Artifact variants
belong to the Release Unit rather than to Buddy or Official. After Attempt
admission, channel policy selects complete destination projections without
altering the NBGV product version. For Buddy, the frozen native NBGV version and
complete deterministic projection set are Snapshot bindings derived during
Attempt planning from same-target control definitions, not Product or Execution
Identity fields.

Pull-request artifacts and CI Evidence are never used by Release.

Release builds are required by business contract to be bit-for-bit
reproducible for identical target, Build Definition, toolchain, and declared
inputs. The delivery system records and compares observed digests where needed
for identity safety but does not certify reproducibility through duplicate
builds.

### Remote-State Observation

Every Release Attempt observes all destinations before requesting publication
Capability.

- Each logical projection is classified atomically against snapshot-bound
  desired projection state, not Product or Execution Identity.
- Desired state derives from the Qualification Snapshot and admitted qualified
  artifacts and includes exact destination coordinate, expected ownership,
  target binding, and artifact bytes or digest.
- The Observation Record binds the Release Attempt, logical projection,
  immutable desired-state basis, and canonical remote response and observed
  facts, including observed artifact digests. It cannot bind a future
  Publication Snapshot; that later Snapshot seals admitted Observation Records
  with resulting desired state and materialized actions.
- Absent state may publish.
- Exact satisfied state skips the side effect.
- Partial, unknown, conflicting, or unprovable projection state fails closed.

An absent registry coordinate is not a reservation gap. With or without
retained operational lineage, it is a legitimate initial-publication state.
After qualification, observation, and approval, the Destination Adapter uses
atomic non-overwriting create semantics. Pre-observed exact state has no action.
At mutation linearization, absent state creates; an atomic create-or-exact
operation may accept a concurrently created exact state without mutation;
differing state fails without mutation. Release never implements this as
read-then-upsert, overwrite, or delete-and-recreate. A pure create-only
destination may conflict and rely on replay. Durable creation establishes
observable state; an Attempt that fails before mutation burns nothing.

Reconciliation is exceptional handling for state that cannot safely proceed.
Observation uses read-only capability before approval. The initial architecture
assumes Delivery Governance is the only normal writer and accepts the residual
risk of an out-of-band mutation after observation. Live registry support also
depends on a documented lower-layer contract for atomic non-overwriting creation
and durable exact-state observation. An incapable destination is unsupported,
not emulated through an application-level lock or permanent index.

### Retry

Retry uses whole-release replay.

- GitHub `Re-run all jobs` is the standard transient retry.
- GitHub `Re-run failed jobs` is not a supported Release recovery protocol.
- Every replay reruns planning, build, qualification, authorization checks,
  observation, and reporting, beginning with a new request-local Repository
  Model compilation bound to the new `github.run_attempt`, even though request
  identity, `github.run_id`, and target remain unchanged.
- Replay never reuses an older Attempt's Repository Model, Qualification, or
  Publication Snapshot.
- Already satisfied remote destinations do not repeat side effects.
- A control-code fix produces a new target revision; it is not injected into an
  ordinary replay of the old target.

Each Release Execution is one channel-specific business Release containing
append-only Attempts. Separate requests create separate request and Intent
records. Each admitted, non-coalesced request for the same Release Execution
Identity creates a new Attempt in that Execution; a replaced or coalesced
pending dispatch is not admitted and creates no Attempt. Different Official
targets with one Product Identity and different Buddy targets are separate
Executions even when they resolve to the same coordinate. Complete
Adapter-declared mutable-resource keys serialize overlapping live actions.
Durable destination state determines absent, exact, or conflict; pre-mutation
failure reserves nothing. Dry-run remains a separate simulation and does not
enter live Release lineage. It first validates a purpose-discriminated Snapshot
covering request/run attempt, target, channel, Release Unit, canonical and native
version facts, producer, and control identity, then derives the separately
namespaced, request-scoped Simulation Identity from those bindings. Shared
record shapes are admissible across neither purpose boundary without an explicit
matching discriminator.

### Partial Publication and Remediation

Publication follows an append-only Saga. Independent capability groups may run
in parallel only after successful channel-level approval produces a valid
Authorization Record. Terminal denial Evidence is admissible only where a
platform provides documented exact current-attempt proof. The first-slice
GitHub Environment rejection surface does not, so rejection is unknown
approval-contract failure with a replayable incomplete Attempt and diagnostic
review information only. No Capability starts. Workflow Delivery adds no
approval watchdog. GitHub cancellation or platform expiry
while approval is pending may terminate the run before a separate Evidence
record or Finalizer outcome exists. When no capability group started, the
platform run/job conclusion is sufficient no-side-effect terminal evidence and
the Attempt is incomplete and replayable, not successful. If any capability job
may have started, cancellation proves no such thing; the Attempt is incomplete
and possibly mutated, and replay reobserves. A running Finalizer records
governed failure when terminal Evidence is admissible, or unknown contract
failure when neither valid authorization nor applicable terminal evidence
exists. Actions within a capability group run in order and stop after failure.
Successful destinations are not automatically rolled back when another group
fails.

Qualification declares Capability requirements but cannot request, approve, or
create live Capability. The normal v3 live path may request destination
Capability only after a credential-free capability admission gate validates the
Authorization Record and exact Publication Snapshot, summary artifact, actions,
artifacts, resource keys, and group manifest. Immediately before admission, the gate uses `contents: read`
to freshly resolve the policy-fixed protected ref and re-read the attestation
document, revalidates schema, canonical content, bindings, current expiry,
and `live_enabled: true`, and requires repository/ref/path plus
commit/blob/content provenance and content identity to match the admitted Live
Eligibility Decision. A false `live_enabled` value, expiry,
source/provenance/content mismatch, or invalidation blocks publication and
requires a new Attempt after Governance is restored; the existing approval
cannot be resumed. Only gate success may schedule the credential-bearing
group. The publisher may repeat the same `contents: read` binding and
Governance-freshness check immediately before mutation as defense in depth.
This adds no credential or service and does not create a malicious-writer
boundary.

Ordinary replay may resume only absent or exactly satisfied state.
Break-Glass Remediation is a separately approved operation with expected-state
checks, scoped Capability, and append-only before-and-after records. It never
rewrites the original Release history.

## Concurrency Design

- CI cancels superseded candidate runs.
- Release Execution lineage and duplicate request coalescing use the complete
  channel-specific Release Execution Identity. Official uses Product Identity
  plus immutable target. Buddy uses channel, Release Unit, and immutable target.
- Every live Destination Adapter declares complete deterministic
  mutable-resource keys for each mutating action. Publication Snapshots and
  action manifests bind them, and overlapping actions serialize on them.
- Package mutation keys include the exact External Package Coordinate:
  channel, destination, package, and version, plus any additional
  Adapter-required keys. First-slice npm publication is one compound
  version-and-tag action keyed by both that coordinate and
  destination/package/`buddy-sha-<40-lowercase-target-sha>`. The tag is routing,
  not provenance; no separate normal tag mutation is allowed. Non-package
  destinations define their exact keys through Adapter contracts.
- GitHub provides equality concurrency groups rather than arbitrary
  set-overlap locks. The first-slice GitHub Packages Adapter therefore maps
  every mutation for one physical destination and normalized npm package name
  to one conservative shared group, including mutations with different
  versions or target-derived tags. This intentionally over-serializes while
  preserving the complete coordinate-plus-tag key set in the Publication
  Snapshot, action, Receipt, and validation. Future Adapters retain abstract
  complete-set overlap semantics and must block if no safe platform projection
  can enforce them.
- Missing, unknown, incomplete, or conflicting required keys block live
  publication.
- In-progress Release executions are never auto-canceled.
- `queue: single` retains only the newest pending duplicate request for the same
  complete Release Execution Identity.
- Every request that survives coalescing and is admitted creates a distinct
  Attempt. A pending dispatch replaced before execution creates no Attempt.
- Each candidate compiles its request-local Repository Model before entering
  execution concurrency. The surviving concurrency-scoped caller invokes one
  same-revision reusable live-Attempt workflow and holds the Execution identity
  slot continuously from admission through finalization.
- Live actions may run concurrently only when their complete mutable-resource
  key sets do not overlap.
- Remediation reuses exactly the complete frozen Adapter-declared resource-key
  set from the original action and never derives it from Product or Execution
  Identity.

Release Execution and coalescing keys derive from Release Execution Identity.
Live-action resource keys derive independently from complete Adapter-declared
mutable-resource keys. A platform group may be a conservative Adapter-declared
projection that preserves all overlap serialization while over-serializing.
GitHub concurrency is a best-effort repository execution mechanism, not a
distributed correctness lock, authorization source, or protection against
external writers.

## Evidence, Decisions, and Explanation

Evidence Admission verifies exact ownership and integrity without rerunning the
quality command.

Final Decisions are append-only. GitHub checks and summaries are projections of
the latest authoritative Decision, not the audit record itself.

CI explanations connect paths, Project Nodes, dependency relationships, Release
Units, obligations, variants, Evidence, outcomes, and verdicts.

Release explanations connect target, version, channel, artifacts,
destinations, observations, actions, Receipts, authority, authorization, and
allowed operator actions.

The same structured model drives machine-readable records and human
projections. Diagnostics explain a verdict but never determine it.

## Platform-Aware Record Retention

Caches are non-authoritative performance mechanisms.

GitHub Actions artifacts and logs are operational records subject to the
platform retention window. This public repository supports at most 90 days. The
first slice uses 45-day Release control and artifact retention to exceed the
platform Environment gate-expiry window, currently up to 30 days, and blocks
activation if repository policy cannot supply that margin. The approval wait
does not preserve Governance eligibility: capability admission still requires
the protected attestation's `live_enabled` field to remain true and the
at-most-90-day document to remain unexpired and provenance-identical to the
pre-Attempt decision.

Artifact names are deterministic, non-authoritative indexes unique across the
complete workflow run with overwrite disabled. Every physical name includes
`github.run_attempt` directly or in its deterministic hash preimage. Producers
retain artifact ID, digest, and URL. Consumers use artifact IDs only and verify
name metadata, producer, run ID, run attempt, and digest. Name fallback, latest
selection, and prior-attempt IDs are rejected.

Longer-lived release identity and provenance rely on Git tags, registry
records, GitHub Releases when selected, and GitHub Artifact Attestations with
public Sigstore transparency-log publication.

The first architecture does not add a Durable Release Ledger, a global Official
Product Identity-to-target mapping, or a GitHub Release audit anchor for every
Release Unit. Different target-specific Executions may share Product Identity;
destination serialization and durable observation determine absent, exact, or
conflict. State that cannot be proved after operational records expire fails
closed. An absent destination is still a valid initial-publication observation
and does not require retained Intent or Attempt lineage, a tag witness, or a
binding index.

## Requirement Coverage

| Requirement Group | Owning Design Elements                                                                                                                                                                         |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WD-SYS-*`        | Peer bounded contexts, aggregate ownership, Shared Foundation, Delivery Governance                                                                                                             |
| `WD-CI-*`         | CI Qualification flow, Planner, executors, Evidence Admission, Final Decision                                                                                                                  |
| `WD-REL-*`        | Release Attempt, Plan lineage, independent build and qualification, Side-Effect Zone                                                                                                           |
| `WD-CHN-*`        | Buddy and Official channel policy over Release Delivery                                                                                                                                        |
| `WD-AUTH-*`       | Same-revision context decision code, protected review, Delivery Governance                                                                                                                     |
| `WD-SEC-*`        | Decision, Build and Qualification, and Side-Effect runtime zones                                                                                                                               |
| `WD-EVD-*`        | Evidence Admission, append-only Decisions, structured explanation projections                                                                                                                  |
| `WD-OPS-*`        | Remote-State Observation, whole-release replay, Saga, reconciliation, remediation                                                                                                              |
| `WD-CON-*`        | Domain-derived GitHub execution serialization and destination concurrency groups                                                                                                               |
| `WD-RET-*`        | Platform-aware records, durable destination identities, fail-closed expiration                                                                                                                 |
| `WD-SLICE-*`      | Delivery Governance, Governed Same-Revision Control, Runtime Zones; bounded writer TCB, permanent consumer policy, direct repository-wide Buddy retirement, and removable acceptance bootstrap |
| `WD-NFR-*`        | Context separation, adapter extension model, explanation contract, CI objective                                                                                                                |

## Middle-Layer Design Decomposition

The next design stage should produce separate MLDs for:

1. **Repository Model and Release Units:** Project Node discovery, dependency
   and path-impact facts, Release Unit authoring, variants, and Build
   Definitions.
2. **Governance Integration:** same-revision control, channel-specific review
   policy, the bounded first-slice Buddy exception, platform-native authority,
   and authorization boundaries.
3. **CI Qualification:** candidate identity, affected-scope planning,
   project-selected quality policy, opaque source-tree conformance,
   model-driven execution, Evidence, Decision, advisory reporting, and GitHub
   projection contracts.
4. **Release Delivery:** manual same-revision Intent, channel identity, Plan
   lineage, complete variant build and qualification, projection observation,
   authorization, capability groups, publication, Receipt, replay, and
   remediation contracts.
5. **Shared Foundation:** record primitives, Provider and Adapter interfaces,
   Repository Model compilation, static Definition catalogs, artifact identity,
   provenance, execution classes, and generic client primitives.

CI and Release remain together in this HLD because their separation, shared
foundation, and governance relationship are top-level architectural decisions.
They separate into bounded-context documents at the MLD stage.

## Deferred Lower-Layer Questions

The HLD intentionally leaves these questions to later design:

- exact strict record schemas and transport;
- adapter package decomposition;
- exact GitHub workflow and job topology;
- artifact and Evidence physical naming;
- batching and matrix partitioning;
- destination-specific observation and digest APIs;
- user-interface rendering and operator commands; and
- migration from the current implementation into the selected vertical slice.
