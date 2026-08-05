# Workflow Delivery v3 Release Delivery MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed on 2026-08-04**.

This middle-level design defines how Release Delivery accepts a manual Release
Intent, derives channel-specific identity, independently builds and qualifies a
complete Release Unit, observes destination state, obtains authorization,
performs isolated publication side effects, records Receipts, and recovers
through whole-release replay or separately authorized remediation.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md),
[High-Level Design](./high-level-design.md),
[Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md),
[Governance Integration MLD](./governance-integration-mld.md), and
[Shared Foundation MLD](./shared-foundation-mld.md).
Exact record schemas, descriptor syntax, workflow YAML, commands, and
destination API mappings remain lower-layer decisions.

## Scope

This MLD owns:

- manual Release Intent normalization and same-revision target selection;
- Official, Buddy, and dry-run identity semantics;
- Release Execution and append-only Attempt lineage;
- channel-specific release quality policy and destination projections;
- complete artifact variant build and Release qualification;
- Qualification and Publication Snapshot finality;
- artifact identity, internal provenance, and external attestation projections;
- destination observation and publication action planning;
- release-level authorization and destination-specific capabilities;
- capability-scoped side-effect execution and Receipt admission;
- Release completion, replay, reconciliation, and Break-Glass Remediation;
- GitHub-native execution serialization and duplicate request coalescing; and
- platform-native retention behavior.

This MLD does not own:

- CI scope, CI Evidence, or CI decisions;
- Project Node and dependency fact discovery;
- Build Definition authoring;
- destination-specific command implementations;
- signing or notarization that changes artifact bytes after qualification;
- a permanent external release database or ledger;
- automated release initiation;
- exact GitHub Environment names; or
- exact workflow job serialization.

## Governing Principles

1. One Release Intent concerns one Release Unit, one immutable target, and one
   channel.
2. A Release Unit owns one complete publishable artifact variant set,
   independent of channel.
3. Buddy and Official select different identities and destination projections,
   not different subsets of the product.
4. Release independently builds and qualifies its target. CI runtime records are
   not Release inputs.
5. Every quality obligation in a Release Plan is required.
6. Publication authorization binds exact artifacts, observations, actions, and
   the successful Qualification Decision.
7. Target-code execution and publication capability never coexist in one
   runtime boundary.
8. Publication is an append-only Saga, not a cross-destination transaction.
9. Whole-release replay creates a new Attempt and never combines partial records
   from different workflow attempts.
10. GitHub concurrency reduces duplicate internal execution but is not a
    distributed correctness lock.
11. Security controls address the repository's governed single-writer model and
    do not invent unavailable destination transaction features.

## Domain Model

### Release Intent

The initial v3 entry point is manual `workflow_dispatch`.

The operator selects the Git branch or tag in GitHub's **Run workflow** control.
That selected workflow ref is the Release target. The workflow pins
`github.sha` before planning.

The normalized Intent contains:

- Release Unit identity;
- pinned target commit;
- channel: Buddy or Official;
- live or dry-run mode;
- Buddy Intent identity when live Buddy is requested;
- initiator; and
- request identity.

The Intent does not contain:

- a version override;
- artifact variant selection;
- destination selection;
- credential or Environment selection; or
- a force, clobber, or overwrite mode.

The selected workflow ref, Planner, Finalizer, workflow topology, and target
source therefore come from the same revision.

Official live execution accepts only Governance-configured authoritative refs.
Buddy live execution accepts only Buddy-authorized refs. Other refs may exercise
allowed dry-run behavior without receiving live capability.

### Release Identity

Release Identity is channel-specific.

#### Official

Official identity consists of:

- Release Unit identity; and
- the NBGV canonical version for the target.

The identity binds one target commit. An existing mapping to another target or
artifact identity is a conflict, not a second release with the same version.

#### Buddy

Buddy identity consists of:

- Release Unit identity;
- immutable Buddy Intent ID;
- target commit; and
- a derived preview identity.

A new manual dispatch creates a new Buddy Intent even when the target is
unchanged. GitHub **Re-run all jobs** preserves the existing Buddy Intent. A
cross-run recovery must explicitly reference the original Intent record.

Buddy preview identity is create-or-exact. Normal Buddy publication never
overwrites conflicting bytes. A new target requires a new Buddy Intent and
preview identity.

### Channel Version Projection

NBGV remains the canonical product version authority.

- Official projects the canonical version.
- Buddy derives an isolated preview identity from the canonical version, target,
  and Buddy Intent.
- Ecosystem Adapters encode the logical Release Identity as valid NuGet,
  PEP 440, SemVer, or other ecosystem version strings.

Version projection is a frozen Build Request input. Buddy and Official may
therefore produce different bytes when the projected version is embedded, while
still using the same Build Definition.

All artifact variants in one Attempt use one logical Release Identity.
Destinations cannot choose independent versions.

### Release Execution

A Release Execution is the logical business Release for one channel-specific
Release Identity.

It owns an append-only sequence of Release Attempts. It is not a permanent
database row or a mutable document.

Different Release Units at the same commit remain separate Release Executions.
Different Buddy Intents at the same commit remain separate Release Executions.

### Release Attempt

A Release Attempt is one coherent:

- planning;
- build;
- qualification;
- observation;
- authorization;
- publication; and
- finalization pass.

Every whole-release replay creates a new Attempt. An Attempt never adopts build
outputs, Evidence, approval, observations, Receipts, or outcomes from another
workflow attempt.

### Dry-Run Simulation

Dry-run is a separate, non-authoritative simulation execution.

For a Buddy dry-run, the Planner derives an immutable non-live simulation
projection identity from the Release Unit, pinned target, and request identity.
It substitutes for Buddy Intent only when deriving simulated Buddy version and
destination projections. Its namespace cannot collide with or reserve a live
Buddy Intent or preview identity.

It uses the selected channel's:

- Release Unit variant set;
- quality policy;
- version projection;
- destination projections;
- observation rules; and
- action planning.

It builds, qualifies, observes, and emits a hypothetical Publication Snapshot,
but it:

- obtains no approval;
- obtains no publication capability;
- performs no external attestation or destination mutation;
- does not reserve Official or Buddy live identity; and
- does not enter live Release Execution history.

Live Release never reuses dry-run artifacts, Evidence, observations, or results.

## Release Unit Policy

### Complete Artifact Variant Set

Artifact variants belong to the Release Unit, not to Buddy or Official.

Every live or dry-run Attempt builds and qualifies every publishable variant
declared by the Release Unit. The caller and channel cannot select a subset.

If two artifact sets can be independently versioned, authorized, completed, and
recovered, they should be separate Release Units rather than selectable subsets
of one Release.

### Channel Quality Policy

Each Release Unit selects one complete Buddy release quality policy and one
complete Official release quality policy.

The policies are independent. They may share authoring helpers or definitions,
but the Planner consumes fully expanded policy and does not infer inheritance or
attempt to prove that one channel is stronger than the other.

Release quality policy:

- is independent from CI project presets;
- may reuse the same Quality Definitions and ecosystem targets;
- contains required obligations only; and
- may explicitly select source-tree checks, including HK, but does not inherit
  the CI root HK gate.

Non-blocking diagnostics belong in CI advisory or separate scheduled reporting,
not in a Release Plan.

### Release Presets and Custom Policy

A Release Unit may select a semantically versioned release preset or custom
policy for each channel.

Adding or strengthening any of these semantics requires a new preset identity
or an explicit Release Unit policy change:

- publishable artifact variant coverage;
- required quality obligations;
- destination projections;
- external provenance projections; or
- capability requirements.

Equivalent Adapter repairs and diagnostic improvements may evolve without
changing the semantic identity. Each Qualification Snapshot binds the fully
expanded policy digest.

### Destination Projections

The Release Unit channel policy selects logical destination projections.

A projection states:

- logical destination identity;
- artifact variant and output mapping;
- external release or package identity;
- logical operation, such as package publication or release attachment; and
- required provenance projection.

Every publishable variant must be accounted for by each channel's complete
projection set.

The caller cannot remove a destination for one Attempt. A temporary destination
failure is handled by Saga replay, not by shrinking the Release.

## Destination Responsibility Split

Destination behavior has three owners.

### Release Unit

The Release Unit chooses the complete logical projections for Buddy and
Official.

### Destination Definition and Adapter

Release Delivery Destination Definitions and Adapters own:

- endpoint family and mechanical identity;
- observable remote facts;
- projection classification;
- supported publication operations;
- action decomposition;
- Receipt shape;
- required GitHub permissions;
- OIDC or token capability requirements; and
- destination mutability constraints.

They do not decide whether a Release Unit publishes to that destination.

Shared Foundation provides only generic artifact, digest, invocation,
capability-requirement, GitHub, and registry client primitives. Those clients do
not classify projections, plan actions, define Receipts, or choose recovery.

### Delivery Governance

Delivery Governance owns:

- approval Environments;
- destination Environments;
- required reviewers;
- GitHub job permissions;
- OIDC trust;
- destination trusted-publisher configuration; and
- capability grant or denial.

Release Unit policy cannot grant authority to itself.

## Flow

```text
manual Release Intent on selected Git ref
  -> pin exact target commit
  -> structural and lineage eligibility
  -> derive channel Release Identity
  -> compile complete Release Unit policy
  -> seal Qualification Snapshot
  -> build every artifact variant
  -> execute every required Release quality obligation
  -> admit Release Evidence
  -> Qualification Decision with artifact digests and provenance
  -> observe every logical destination projection
  -> seal Publication Snapshot and action groups
  -> channel-level human authorization
  -> optional external provenance prerequisite groups
  -> destination-specific capability groups
  -> action Receipts and group result bundles
  -> Release Finalizer
  -> Attempt Outcome and derived Release Execution state
```

## Eligibility and Authority

### Structural and Lineage Eligibility

The Release Planner validates before expensive execution:

- Release Unit and channel policy exist;
- selected ref and pinned target agree;
- Release Identity can be derived;
- every variant, quality definition, destination projection, and required
  Adapter exists;
- Official live target belongs to an authoritative Git lineage; and
- Buddy live target belongs to a configured Buddy-authorized lineage.

The Planner does not query reviews or re-adjudicate Rulesets.

A structurally valid target is not thereby authorized to publish.

### Live Authority

Live authority is granted only after a successful Qualification Decision,
complete observation, and sealed Publication Snapshot.

Delivery Governance grants:

- one channel-level human approval for the exact Publication Snapshot; and
- destination-specific capabilities just in time for the matching side-effect
  groups.

No qualification or observation job has publication capability.

## Release Plan Lineage

Each live Attempt has one logical Plan lineage with two sealed snapshots.

### Qualification Snapshot

The Qualification Snapshot freezes:

- Release Execution and Attempt identity;
- Release Unit, target, channel, and projected version identity;
- complete Project Node and declared-input closure;
- every publishable artifact variant;
- complete Build Definitions and Build Requests;
- every required Release quality obligation;
- concrete targets, dimensions, runner constraints, and prerequisite DAG;
- complete destination projection set;
- internal provenance requirements;
- external provenance projection requirements; and
- policy and definition digests.

The snapshot authorizes only unprivileged build and qualification work.

### Publication Snapshot

After successful qualification and complete destination observation, the
Publication Snapshot references the Qualification Snapshot digest and adds:

- successful Qualification Decision;
- actual artifact IDs, content digests, and internal provenance;
- immutable artifact transport IDs;
- one Observation Record per logical projection;
- exact desired destination identities;
- materialized Publication Actions and prerequisites;
- capability-group assignment;
- expected action Receipts;
- channel-level approval requirement; and
- destination-specific capability requirements.

It cannot change:

- target;
- Release Identity;
- variant set;
- Build Definitions;
- quality obligations;
- destination projection set; or
- artifact bytes.

Governance approval binds the Publication Snapshot through an Authorization
Record. No in-place Plan backfill is allowed.

## Release Qualification

### Obligations

The Qualification Snapshot materializes:

- one Build obligation for every artifact variant; and
- all channel release quality obligations.

Obligation identity is:

```text
Definition
  x concrete target
  x concrete dimensions
```

Providers resolve supporting tests, native aggregate targets, dimensions,
runners, and true prerequisite relationships before the snapshot seals.

Only identical definition, target, and dimensions deduplicate. Mechanical reuse
does not merge semantic obligations.

### Independent Evidence

Release creates Attempt-specific Evidence for every obligation.

It does not consume:

- CI Plans;
- CI Evidence;
- CI artifacts;
- CI checks;
- dry-run records; or
- earlier Release Attempt Evidence.

CI and Release may share Build Definitions, Quality Definitions, Providers, and
Adapters only.

### Failure Continuation

After the first definitive required qualification failure:

- no new pending obligations start;
- in-flight obligations may finish and report;
- dependents become `incomplete(blocked-by-prerequisite)`;
- independent work not yet started becomes
  `incomplete(aborted-after-failure)`; and
- the Qualification Finalizer produces a failed Decision.

Recovery uses a new whole-release Attempt.

### Artifact Byte Finality

Build Adapters produce final publishable bytes before qualification completes.

Release qualification binds those exact bytes. Publication groups may copy,
attest, or upload them but may not transform them.

The initial design does not support privileged code signing, notarization, or
other byte-changing finalization after qualification. Such support requires a
future explicit artifact-finalization phase that revalidates and refreezes the
resulting bytes before Publication Snapshot creation.

### Internal Artifact Provenance

Every artifact variant has internal provenance that binds:

- Release Identity and target commit;
- Build Definition and Build Request digests;
- projected version;
- declared toolchain and inputs;
- producer job and workflow attempt;
- artifact transport identity; and
- content digest.

The Qualification Decision admits this provenance and binds the complete
artifact set.

### External Provenance Projections

Channel policy may require GitHub Artifact Attestation or another external
provenance projection.

External provenance:

- does not change artifact bytes;
- executes after successful qualification;
- executes only after channel authorization;
- uses a capability-scoped job with no target checkout;
- produces a Receipt; and
- may be a prerequisite for destination publication groups.

Dry-run reports the hypothetical external provenance actions but does not
perform them.

## Remote-State Observation

### Permission Boundary

Observation occurs after qualification and before publication authorization.

Observation jobs may use:

- public destination APIs;
- `contents: read`;
- `packages: read`; or
- an equivalent minimal read-only destination identity.

They do not use:

- `id-token: write`;
- publication secrets;
- write tokens; or
- live publication Environments.

A destination that cannot provide sufficient pre-authorization observation is
unsupported for live v3 publication.

### Projection-Atomic Classification

Each logical destination projection is classified as:

- `absent`;
- `exact-satisfied`;
- `partial`;
- `conflicting`;
- `unknown`; or
- `unprovable`.

Classification covers the projection as a whole.

For example, a GitHub Release object with one expected asset missing is
`partial`, even when the missing upload could be described as an individual
action.

Only `absent` and `exact-satisfied` allow a ready Publication Snapshot:

- `absent` produces publication actions; and
- `exact-satisfied` produces no side effect.

All other classifications block ordinary publication and require
reconciliation.

### Single Governed Writer Assumption

The initial design assumes Delivery Governance is the only normal writer for
managed Release identities.

The system also uses GitHub execution serialization to reduce duplicate
in-repository publishers. It does not add a second observation immediately
before mutation and does not require a uniform destination conditional-write
API.

An out-of-band mutation between observation and publication is an accepted
residual operational risk.

Normal Adapters still:

- avoid destructive overwrite;
- use natural create/conflict behavior where available;
- treat destination conflict or API failure as action failure; and
- defer reclassification to the next whole-release Attempt.

## Publication Planning

### Projection and Action Granularity

Each absent logical projection expands through its Destination Adapter into a
closed Publication Action DAG.

Each action binds:

- action ID;
- logical projection;
- destination identity;
- operation;
- exact artifact or metadata input;
- prerequisite action IDs;
- capability-group identity;
- expected result; and
- Receipt contract.

Actions are not raw HTTP calls. An Adapter may group lower-level calls only
when the resulting action has coherent observation, failure, and replay
semantics.

### Projection-Atomic Replay

Action-level Receipts do not allow ordinary replay to complete a partial
projection.

If only part of a projection exists, the next observation returns `partial` and
requires reconciliation. Action records explain the state and support a
separately authorized remediation workflow.

### Publication Control Bundle

The system emits one small immutable control bundle containing:

- Publication Snapshot;
- Authorization binding inputs;
- action manifests;
- artifact IDs and expected digests;
- capability-group manifests; and
- expected Receipt contracts.

Variant artifacts remain separate immutable Actions artifacts. Each active
capability group downloads the control bundle once and only the artifact IDs it
needs.

## Authorization

### Channel-Level Approval

One approval job represents the human decision for the complete Publication
Snapshot.

The job:

- depends on the sealed Snapshot;
- binds a Buddy or Official approval Environment;
- has no publication credentials or `id-token: write`;
- cannot start until GitHub Environment protection passes; and
- emits an Authorization Record after approval.

The Authorization Record binds:

- Publication Snapshot digest;
- Release Execution and Attempt identity;
- GitHub workflow run and attempt;
- approval job identity;
- channel; and
- approval completion time.

GitHub natively approves the exact job/run. The system-created record binds that
approved job to the exact Snapshot digest. The design does not claim that
GitHub produces a cryptographic signature over an arbitrary digest.

Reviewer visibility of the Snapshot summary and digest through the deployment
URL or equivalent UI is an LLD acceptance requirement.

### Destination Capabilities

After channel approval, each active capability group:

- validates the Authorization Record and Snapshot digest;
- binds its exact action ID set;
- enters its destination-specific Environment;
- receives only required GitHub permissions;
- requests OIDC or another destination capability just in time; and
- cannot execute actions from another group.

Destination policy may impose an additional approval. The channel-level
Authorization Record does not bypass destination Governance.

GitHub and destination platforms may scope credentials only to an Environment,
workflow identity, repository, package, or destination account. The trusted
group executor, rather than the credential format alone, enforces the exact
Snapshot, artifact digest, action ID, and Attempt bindings.

## Publication Execution

### Capability-Scoped Groups

The scheduler groups actions only when they share:

- the same destination trust boundary;
- the same Environment;
- the same GitHub permission set;
- the same identity or OIDC contract; and
- compatible runner requirements.

One group job may execute multiple planned actions sequentially. Actions retain
individual identity and Receipts.

Typical groups include:

- GitHub contents;
- GitHub Packages;
- PyPI OIDC;
- npmjs OIDC;
- RubyGems OIDC; and
- external artifact attestation.

Only groups present in the Publication Snapshot execute.

### Parallelism and Failure

External provenance prerequisite groups complete before publication groups that
depend on them.

Independent publication capability groups may run in parallel. Global fail-stop
is not promised because GitHub Actions cannot dynamically stop future matrix
work while allowing every in-flight side effect to finish safely.

Within one group:

- actions execute in DAG order;
- an action failure stops later actions in that group;
- completed actions retain their Receipts; and
- no automatic rollback occurs.

Other already-started independent groups may complete. The resulting
cross-destination partial publication is handled by the normal Saga and
whole-release replay rules.

### Result Bundles

After every successful mutation, the group persists an immutable action Receipt
before starting any later mutation in the same group. If Receipt persistence
fails, the group stops. The Attempt records that mutation may have occurred
without a durable Receipt, and the next Attempt must reobserve the projection.

Each active capability group emits at most one result bundle containing:

- every planned action ID in the group;
- action outcome;
- destination response identity;
- references to the separately persisted Receipt for each completed mutation;
- diagnostic reference; and
- group completion state.

A planned active group with no admissible result bundle is incomplete.

## Release Finalization

The Release Finalizer:

- validates the Qualification and Publication Snapshot lineage;
- admits the Authorization Record for every live Attempt;
- admits Observation Records;
- admits capability-group result bundles and Receipts;
- verifies artifact, action, destination, run, attempt, and digest bindings;
- computes Attempt Outcome; and
- derives the current Release Execution state.

It does not:

- query remote destinations;
- rerun quality checks;
- reinterpret Adapter-specific API output;
- infer success from a GitHub job conclusion alone; or
- repair a missing Receipt.

Remote uncertainty after execution is handled by the next Attempt's normal
observation.

### Completion Invariant

A live Attempt is `completed` only when:

- Qualification Decision succeeded;
- every logical projection is proven exact desired state;
- every executed action has an admitted Receipt;
- every already-satisfied projection has an admitted Observation Record;
- all required authoritative records were persisted; and
- no required action is failed, incomplete, unknown, or conflicting.

Destination API success without durable Receipt does not produce completed
Release.

When all projections were already exact, the Attempt has no live actions and
requires no destination capability. Channel approval still binds the no-op
Publication Snapshot and its observations before the live Release can complete.

### Attempt Outcome

Attempt Outcome records:

- terminal phase;
- structural or quality failures;
- authorization result;
- observations;
- action and group outcomes;
- admitted Receipts;
- whether mutation may have occurred without a durable Receipt; and
- allowed next operator action.

It is append-only.

### Release Execution State

The current business Release state is derived from Attempt history and the
latest proven destination state:

- `in-progress`;
- `replayable`;
- `reconciliation-required`; or
- `completed`.

`replayable` includes failures for which a normal whole-release Attempt may
safely re-observe destination state. For example, one projection may be exact
while another remains wholly absent.

`reconciliation-required` applies after authoritative observation establishes a
partial, conflicting, unknown, or unprovable projection.

The latest Attempt result is not itself the business Release state.

## Whole-Release Replay

GitHub **Re-run all jobs** is the normal transient replay path.

Every replay:

- creates a new Attempt;
- reuses the same live Release Identity;
- reruns planning;
- rebuilds every artifact variant;
- reruns every Release quality obligation;
- creates new Evidence and Qualification Decision;
- observes every destination projection;
- creates a new Publication Snapshot;
- obtains new channel approval;
- obtains fresh capabilities through the new Attempt's authorized group jobs;
  and
- creates new Receipts and Attempt Outcome.

Replays do not use GitHub **Re-run failed jobs**.

If rebuilt artifact bytes differ from an exact remote artifact for the same
Release Identity, observation is conflicting. The system does not overwrite the
remote artifact.

Ordinary replay of an older target uses the workflow and control code from the
same selected ref. If that ref or required target records are no longer
available, replay is unsupported rather than silently using current control
code.

## Reconciliation and Break-Glass Remediation

### Reconciliation

Reconciliation is read-only.

It:

- references the original Release and projection;
- collects additional destination facts;
- records actual current state;
- determines whether the projection can be proven absent or exact; and
- emits an append-only Reconciliation Record.

It performs no mutation.

### Break-Glass Remediation

Remediation is a separate manual workflow, not a `force` option on Release
Intent.

It requires:

- original Release and projection identity;
- a qualified source artifact when the action consumes bytes;
- exact expected current state;
- one allowlisted action;
- reason and incident or work-item reference;
- stronger approval;
- a capability scoped to the narrowest platform-supported remediation boundary;
  and
- append-only before-and-after records.

Remediation uses current protected remediation control code. It does not inject
new code into ordinary replay of the old target.

Before requesting remediation approval, current code admits the cross-revision
reconciliation request by verifying:

- producer repository and protected workflow identity;
- producer ref and target revision;
- workflow run, attempt, and producer job;
- original Release Execution and Attempt;
- Qualification and Publication Snapshot digests;
- successful Qualification Decision;
- Reconciliation Record and logical projection;
- qualified artifact transport identities and content digests; and
- cross-revision contract kind, version, and payload digest.

An internally consistent but untrusted or unrelated Actions artifact is not an
admissible remediation request.

After remediation, a normal whole-release replay must rebuild, requalify,
observe, and prove the Release completed.

### Concrete GitHub Release Missing-Asset Flow

For a GitHub Release projection with an exact ZIP and missing installer:

1. A normal replay rebuilds and qualifies the installer, observes the partial
   projection, and emits an immutable reconciliation request artifact.
2. The operator runs a dedicated protected
   `release-remediate-github-release.yml` workflow.
3. A read-only preflight job verifies the request's protected producer and
   original Release, Qualification, Reconciliation, and artifact lineage.
4. The preflight job downloads the admitted qualified artifact by Actions
   artifact ID, verifies SHA-256, and uses `gh api` to verify the release,
   target, existing ZIP digest, and missing installer.
5. A write job binds a stronger remediation Environment, receives only
   `contents: write`, performs no target checkout, re-fetches and compares the
   complete expected remote state after approval, and then executes
   `gh release upload` without `--clobber`.
6. The job verifies the GitHub-computed asset digest and persists a Remediation
   Record with before and after state.
7. A later normal Release replay proves every projection exact and completes
   the Release.

If any expected state changed after preflight or the asset now exists,
remediation fails without overwrite.

## GitHub Execution Serialization

The design uses GitHub Actions concurrency as a platform execution guard, not a
distributed lock or source of Release correctness.

For one Release Identity:

- `cancel-in-progress` is false;
- the currently running Attempt is not canceled;
- `queue: single` retains only the latest pending duplicate request; and
- a pending run canceled before execution does not become a Release Attempt.

Different Release identities may execute concurrently.

When a Destination Adapter has a concrete cross-version mutable-resource
constraint, its capability-group job may use an additional destination
concurrency group. The architecture does not introduce an external lock or queue
service.

GitHub concurrency does not protect against:

- manual destination changes;
- other repositories;
- external publishers; or
- destination-side races.

Those are covered only by the governed-writer assumption, normal destination
conflict behavior, observation on replay, and reconciliation.

## Platform-Native Retention

Release Execution is a logical aggregate over platform-native records.

Within GitHub Actions retention, each Attempt preserves:

- snapshots;
- Evidence;
- Decisions;
- artifacts and provenance;
- Authorization;
- Observations;
- action result bundles;
- Receipts; and
- outcomes.

Longer-lived identity and state rely on:

- Git commits and refs;
- NBGV version identity;
- package and registry records;
- GitHub tags and Releases when selected; and
- external attestations when selected.

After Actions records expire, replay may proceed only from facts still provable
through these platforms.

For example, a rebuilt package may be compared with PyPI file hashes and GitHub
Release asset digests. If all projections are exact, a no-op Attempt still
requires channel approval but no destination capability. If a destination
cannot prove exact identity or digest, the Release becomes
`reconciliation-required`.

The initial design does not add a permanent release database or require every
Release Unit to create a GitHub Release audit anchor.

## Failure Conditions

Planning or execution fails closed when:

- the selected workflow ref and pinned target differ;
- target or channel lineage is ineligible;
- Release Identity cannot be derived;
- Release Unit policy is missing, invalid, or incomplete;
- a publishable variant, Build Definition, or quality obligation is unresolved;
- a destination projection or required Adapter is unsupported;
- build or Release qualification fails;
- artifact identity or internal provenance is incomplete;
- observation is partial, conflicting, unknown, or unprovable;
- Publication Snapshot changes a Qualification Snapshot semantic field;
- required external provenance fails;
- channel approval is denied, canceled, or timed out;
- Authorization Record is missing or mismatched;
- a capability cannot be obtained;
- a publication action or capability group fails;
- an action may have mutated state without a durable Receipt;
- authoritative records cannot be persisted; or
- Finalizer admission finds missing or conflicting bindings.

No failure falls back to a weaker channel, credential, Environment, target,
destination subset, artifact subset, or overwrite mode.

## Acceptance Scenarios

### Cross-Destination Whole-Release Replay

A Python Official Release publishes to PyPI but fails before creating the
GitHub Release.

- The Attempt fails.
- PyPI is exact and GitHub Release is absent, so the Release is replayable.
- Replay rebuilds and requalifies all variants.
- Artifact digests must match the exact PyPI state.
- PyPI is skipped and GitHub Release publishes.
- Only after both projections are exact is the Release completed.

### Projection-Internal Partial State

A GitHub Release object and ZIP exist, but the installer is missing.

- Observation classifies the projection as partial.
- Ordinary replay performs no mutation.
- A separately approved missing-asset remediation may upload the exact qualified
  installer without `--clobber`.
- Normal replay then proves the complete projection exact.

### Distinct Buddy Intents

Two manual Buddy dispatches for the same target create different Buddy Intent
IDs and preview identities.

- Each builds every Release Unit variant.
- Each publishes to isolated preview identity.
- Re-run all preserves the original Intent.
- Official later rebuilds and never promotes Buddy artifacts or Evidence.

### Official Dry-Run

A feature ref executes Official dry-run.

- The workflow ref is the exact target.
- All variants and Official release checks run.
- Official destinations are observed.
- Hypothetical actions and capability requirements are reported.
- No approval, attestation, capability, mutation, or live Release identity is
  created.

### Approval Timeout

Qualification and observation succeed, but the Official approval job times out.

- No Authorization Record exists.
- No destination capability group starts.
- The Attempt fails before publication.
- The Release remains replayable.
- Replay rebuilds, requalifies, reobserves, and requests a new approval.

### Parallel Capability Groups

External attestations succeed, then PyPI and GitHub contents groups run in
parallel.

- Channel authorization completes before the attestation group starts.
- PyPI fails while GitHub Release succeeds.
- Each group records its own actions.
- Finalizer performs no remote query.
- The Release is replayable when GitHub is exact and PyPI absent.
- Replay skips GitHub and retries PyPI after rebuilding and requalification.

### Duplicate Manual Requests

Three duplicate Official dispatches arrive.

- The first run forms Attempt 1.
- The second is pending.
- The third replaces the second pending run.
- The running Attempt is not canceled.
- The surviving pending run forms a new Attempt after the first finishes.

### Replay After Actions Record Expiration

A later replay rebuilds the target after operational artifacts expired.

- Git, PyPI hashes, GitHub tags, Releases, and asset digests are used when
  available.
- All exact projections produce a no-op Attempt that requires channel approval
  but no destination capability.
- An unprovable destination blocks and requires reconciliation.
- Version existence alone is not exact proof.

## Deferred LLD Decisions

The first Release LLD must define:

- strict Release Intent, Identity, Snapshot, Evidence, Decision, Authorization,
  Observation, Action, Receipt, Outcome, Reconciliation, and Remediation
  schemas;
- Release Unit release-policy authoring and preset catalog syntax;
- exact Buddy preview identity encoding per ecosystem;
- exact destination projection and capability-group catalogs;
- GitHub Environment and permission mappings;
- manual workflow inputs and selected-ref validation;
- artifact and control-bundle naming and retention;
- qualification batching and fail-stop implementation;
- approval deployment URL and reviewer-visible Snapshot summary;
- active capability-group job topology and empty-group handling;
- exact result-bundle admission;
- concrete Destination Adapter observation and publication commands;
- the GitHub Release remediation workflow;
- platform serialization keys and queue behavior; and
- acceptance tests for every scenario in this MLD.
