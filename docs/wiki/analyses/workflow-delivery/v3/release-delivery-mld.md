# Workflow Delivery v3 Release Delivery MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This middle-level design defines how Release Delivery accepts a manual Release
Intent, derives channel-specific identity, independently builds and qualifies a
complete Release Unit, observes destination state, obtains authorization when
one action is required, records publication state, and recovers through a new
manual dispatch or separately authorized remediation.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md),
[High-Level Design](./high-level-design.md),
[Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md),
[Governance Integration MLD](./governance-integration-mld.md), and
[Shared Foundation MLD](./shared-foundation-mld.md).

Exact record schemas, descriptor syntax, workflow YAML, commands, and
destination API mappings remain lower-layer decisions.

The normal Live implementation remains delivered but disabled through
protected Governance with `live_enabled: false`.

## Scope

This MLD owns:

- manual Release Intent normalization and same-revision target selection;
- Official, Buddy, and simulation identity semantics;
- deterministic Release Execution identity and current Attempt identity;
- channel-specific quality policy and destination projections;
- complete artifact-variant build and Release qualification;
- Qualification and Publication Snapshot finality;
- destination Observation and publication-action planning;
- the first-slice zero-or-one Publication Action contract;
- Approval Bundle and Publication Authorization formation;
- mutation marker, Publication Result, and Attempt Outcome semantics;
- new-dispatch retry;
- Release Execution and mutable-resource concurrency boundaries;
- read-only reconciliation and Break-Glass Remediation; and
- platform-native retention behavior.

This MLD does not own:

- CI scope, CI Evidence, or CI Decisions;
- Project Node and dependency discovery;
- Build Definition authoring;
- destination-specific command implementations;
- a permanent external Release database or ledger;
- exhaustive GitHub Actions history discovery or admission;
- a history-derived aggregate Release Execution state;
- automated Release initiation; or
- an exact GitHub job DAG or shell choreography.

## Governing Principles

1. One Release Intent concerns one Release Unit, one immutable target, and one
   channel.
2. A Release Unit owns one complete publishable artifact-variant set,
   independent of channel.
3. Buddy and Official select different channel, destination, and authority
   boundaries without changing the NBGV product version.
4. Release independently builds and qualifies its target; CI runtime records
   are not Release inputs.
5. Every Release quality obligation is required.
6. The Qualification Snapshot closes build and qualification; the Publication
   Snapshot later closes actual artifacts, observations, and publication.
7. First-slice normal Live admits exactly zero or one Publication Action.
8. Action-bearing publication requires one complete Publication Authorization.
9. The mutation marker is durable before mutation. After every controlled
   post-marker terminal state, the publisher forms one Publication Result and
   initiates its persistence.
10. Retry is a new manual dispatch, never a GitHub rerun.
11. Native Actions history is diagnostic only.
12. GitHub concurrency reduces duplicate or overlapping repository-controlled
    work but is not a distributed correctness lock.
13. Break-Glass Remediation is separate authority, not a force option.

## Domain Model

### Release Intent

The initial v3 entry point is manual `workflow_dispatch`.

For normal Buddy, the operator selects a same-repository branch or tag through
GitHub's **Run workflow** control. The selected ref resolves to one exact SHA,
which is both:

- the workflow and control-code revision; and
- the Release target.

The normalized Intent contains:

- Release Unit identity;
- pinned target commit;
- channel: Buddy or Official;
- live or simulation purpose;
- initiator; and
- request identity.

It does not contain:

- a version override;
- artifact-variant selection;
- destination selection;
- credential or Environment selection;
- a previous Attempt to resume; or
- a force, clobber, or overwrite mode.

Intent is request identity, not Product, Execution, Attempt, or package
identity. Separate manual dispatches retain separate Intents.

### Purpose Branch and Request-Local Repository Model

Each request selects live release or release simulation before live
eligibility, Product or Execution lookup, coalescing, admission, or Attempt
creation.

Each branch compiles exactly one same-revision Repository Model Snapshot for
its purpose and reuses it throughout that live Attempt or simulation pass. The
Snapshot closes:

- descriptors and Release Unit declarations;
- Project Nodes and dependency relationships;
- Build Definitions;
- modeled artifact variants and outputs;
- complete build and artifact scope;
- target-bound canonical and native NBGV facts; and
- producer, control, request, target, and purpose bindings.

For normal Live, the Snapshot and its current-Attempt transports bind
`workflow_run_id` but not `github.run_attempt`. Every authoritative job
independently enforces attempt 1 instead.

Simulation retains its existing request-scoped `github.run_attempt` binding. A
simulation rerun is a distinct simulation pass and compiles a new
simulation-purpose Snapshot.

Inputs from CI, another purpose, another request, or an earlier live Attempt are
inadmissible. Compilation failure creates no live Attempt.

Any NBGV-owning Provider remains pinned to the exact target with complete
ancestry and tags. Missing or unprovable history blocks target-bound version
facts rather than producing a fallback.

### Live Eligibility

For the named first-slice Buddy path, live eligibility occurs after
request-local Repository Model compilation and before Execution lookup,
concurrency, or Attempt creation.

It combines:

- the `git-target` static-reference result for the exact target;
- channel and Release Unit eligibility;
- the protected Governance source and attestation; and
- the current request, target, producer, control, and Repository Model
  bindings.

The static-reference result proves only that no prohibited direct reference was
found in the closed supported catalog. It is not exhaustive runtime-consumer
proof.

Protected Governance is read from repository `hcoona/three`, ref
`refs/heads/main`, and path
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.
Eligibility binds repository, ref, path, and attestation blob/content identity
or an explicit attestation generation. It does not require equality of the
complete resolved `main` commit.

The attestation:

- names `hcoona` as the sole accepted writer and publisher;
- expires within 90 days;
- binds the policy and package;
- records the relevant bounded access inspection and limitations; and
- requires `live_enabled: true`.

Any later commit touching the protected path invalidates the current Attempt,
even if later bytes revert. Unrelated `main` commits are allowed. Missing,
unreadable, malformed, expired, disabled, path-touched, or binding-mismatched
Governance blocks.

### Official Product Identity

Official Product Identity consists of:

- channel;
- Release Unit identity; and
- canonical NBGV version.

It is destination-independent and does not permanently bind one target.
Different targets may share one Official Product Identity.

Official ecosystem publication and dry-run use the exact frozen native NBGV
projection, such as `npmPackageVersion`, unchanged.

### Release Execution Identity

Release Execution lookup, request coalescing, and caller-held concurrency use:

- Official: Official Product Identity plus immutable target; and
- Buddy: channel, Release Unit identity, and immutable target.

Different targets always create different Release Execution identities,
including when they derive the same external coordinate.

An external package coordinate remains:

- channel;
- destination identity;
- package identity; and
- native product version.

It is not part of Buddy Release Execution Identity. It becomes a
Publication Snapshot and mutable-resource binding.

### Release Execution

A Release Execution is the conceptual scope addressed by one deterministic
Release Execution Identity.

Multiple manual Intents may address that identity, but the first slice does not
maintain an authoritative aggregate Execution state or exhaustive Attempt
sequence. Caller-held concurrency serializes the identity. Current-Attempt
records and fresh destination Observation provide authority.

### Normal-Live Release Attempt

One normal-Live Attempt is a coherent:

- planning;
- build;
- qualification;
- destination Observation;
- Publication Snapshot formation;
- conditional approval and publication; and
- best-effort finalization pass.

Attempt identity is:

- Release Execution Identity; plus
- unique `workflow_run_id`.

`github.run_attempt` is not Product, Execution, Attempt, record, artifact,
Snapshot, Authorization, or Result identity.

Every authoritative normal-Live job independently fails closed unless
`github.run_attempt == 1`. This includes entry and eligibility, Repository Model
and planning, authoritative build and Evidence production, Observation,
Snapshot formation, Approval, exact-satisfied finalization, publisher, and
read-only Finalizer work. An entry-only guard is insufficient because GitHub
supports partial reruns.

GitHub **Re-run all jobs** and **Re-run failed jobs** are unsupported for
normal Live.

### Release Simulation

Simulation is a separate, non-authoritative Release execution.

It branches before live eligibility, Product or Execution identity lookup,
coalescing, admission, or Attempt creation. Its Snapshot binds:

- simulation purpose;
- request identity;
- `workflow_run_id`;
- `github.run_attempt`;
- target;
- channel;
- Release Unit;
- canonical and native NBGV facts;
- producer; and
- control identity.

Only after Snapshot validation does Release derive the separately namespaced,
request-scoped Simulation Identity. Later simulation records bind both the
Simulation Identity and Snapshot digest.

Simulation may plan, build, qualify, observe, and report hypothetical
publication requirements and actions. It obtains no:

- live Product, Release Execution, or Attempt identity;
- Approval Environment review;
- Publication Authorization;
- live Publication Capability;
- Publication Result; or
- mutation lineage.

Simulation retains its existing run-attempt identity and rerun semantics. The
normal-Live first-attempt-only contraction does not apply.

## Release Unit and Channel Policy

### Complete Artifact Variant Set

Artifact variants belong to the Release Unit, not to Buddy or Official.

Every live Attempt or simulation pass builds and qualifies every publishable
variant declared by the Release Unit. A caller cannot select a subset.

If two artifact sets can be independently versioned, authorized, completed, and
recovered, they are separate Release Units rather than selectable subsets.

### Channel Quality Policy

Each Release Unit selects one complete Buddy quality policy and one complete
Official quality policy.

The policies:

- are independent from CI project presets;
- may reuse the same Quality Definitions and ecosystem targets;
- contain required obligations only; and
- may select source-tree checks explicitly without inheriting CI runtime state.

The first-slice npm unit requires separate
`node/npm-artifact-contents-v1` and `node/npm-install-import-v1` obligations.
They may run in one tarball-dependent physical job but emit separate Evidence
and must both succeed.

### Channel Version Projection

NBGV is the only product-version authority.

- Official Product Identity uses the canonical NBGV version.
- Buddy uses the same target-derived product version.
- Ecosystem publication and simulation use the exact frozen native projection
  required by that ecosystem.
- Adapters do not append request, run, Attempt, branch, or channel-derived
  components to the product version.

Each Build Request selects the required native projection from the
request-local Repository Model Snapshot. A Build Adapter may apply and verify
that value but may not recompute NBGV, derive an alternative, or use an ambient
fallback.

### Destination Responsibility

The Release Unit chooses logical channel projections. Release-owned
Destination Definitions and Adapters own:

- observable remote facts;
- projection classification;
- supported publication operations;
- action formation;
- successful-result evidence semantics;
- destination mutability;
- required Publication Capability;
- complete deterministic mutable-resource keys; and
- any conservative platform serialization projection.

Shared Foundation may provide generic clients and binding primitives but does
not classify projections, plan actions, or decide recovery.

For a registry destination, the Adapter contract must establish:

- atomic non-overwriting creation of the authoritative exact package-version
  object;
- create-only semantics for that authoritative version effect in the
  action-bearing Attempt;
- durable exact-state observation for coordinate, bytes, and target witness;
  and
- deterministic conflict classification.

If the destination cannot provide those guarantees, live publication is
unsupported rather than emulated through a reservation, tag witness, binding
index, application lock, or permanent ledger. An explicitly authorized
non-authoritative tag side effect remains governed by its bounded race contract
and is not misrepresented as part of the version-object guarantee.

## Release Flow

```text
manual Release Intent on selected Git ref
  -> resolve one exact same-revision target/control SHA
  -> branch by purpose
       normal Live:
         -> every authoritative job requires github.run_attempt == 1
         -> compile one live-purpose Repository Model Snapshot
         -> evaluate static-reference and protected Governance eligibility
         -> derive Execution identity
         -> enter caller-held Execution concurrency
         -> create Attempt = Execution + workflow_run_id
         -> Qualification Snapshot
         -> build, qualify, and observe
         -> Publication Snapshot with exactly zero or one action
              zero: exact-satisfied read-only finalization
              one: Approval Bundle -> Approval Environment
                   -> Publication Authorization -> publisher
                   -> mutation marker -> action -> Publication Result
         -> best-effort Attempt Outcome
       simulation:
         -> compile one simulation-purpose Snapshot
         -> derive request-scoped Simulation Identity
         -> build, qualify, observe, and report hypothetical actions
         -> no live authority or mutation
```

The diagram shows authority-critical ordering, not an exact job topology.

## Release Plan Lineage

Each live Attempt has one logical Plan lineage with two immutable snapshots.

### Qualification Snapshot

The Qualification Snapshot freezes:

- Release Execution Identity and current Attempt identity;
- request-local Repository Model Snapshot identity and digest;
- Release Unit, target, channel, and selected NBGV projections;
- complete Project Node and declared-input closure;
- every publishable artifact variant;
- Build Definitions and Build Requests;
- every required Release quality obligation;
- concrete targets, dimensions, runner constraints, and prerequisites;
- destination projections and coordinates;
- Adapter and version bindings;
- deterministic desired-state basis;
- potential first-slice action schema;
- complete mutable-resource-key derivation and enforceability basis;
- provenance requirements; and
- policy and Definition digests.

It authorizes only unprivileged build, qualification, and read-only Observation.
It does not freeze an actual mutation action before artifacts and remote state
are known.

It does not bind `github.run_attempt`.

### Build and Qualification

Release builds every variant and creates current-Attempt Evidence for every
required obligation.

It does not consume:

- CI Plans, Evidence, artifacts, or checks;
- simulation artifacts or outcomes; or
- records from an earlier live Attempt.

After a definitive required failure, no new dependent work starts, in-flight
work may finish and report, and the Qualification Decision remains failed or
incomplete. No publication planning follows an unsuccessful Qualification
Decision.

Build Adapters produce final publishable bytes before qualification completes.
Publication may copy, attest, or upload those exact bytes but may not transform
them. Byte-changing signing or notarization requires a future explicit
refreezing design.

### Deterministic First-Slice npm Artifact

For the first-slice npm Release Unit, the same target, frozen inputs, Build
Definition, and toolchain must produce bit-for-bit identical tarball bytes.
The architecture does not certify that property by duplicate building.

The tarball contains canonical
`workflow-delivery/provenance.json`. The witness binds:

- target commit;
- Release Unit;
- canonical and native NBGV facts;
- Build Definition;
- catalog and control digests;
- purpose; and
- schema.

It excludes run and Attempt identity so a new dispatch can reproduce identical
bytes. Build, tarball-content, install/import, and remote Observation all verify
the same witness.

A nondeterministic Release Unit is unsupported by the first slice. Supporting
one requires a future sealed-artifact publication-resume design.

### Qualification Decision

The immutable Qualification Decision binds:

- Qualification Snapshot;
- complete required obligation set;
- admitted current-Attempt Evidence;
- exact artifact identities and digests; and
- success, failure, or incomplete disposition.

Only success may proceed to destination Observation and Publication Snapshot
formation.

## Remote-State Observation

Observation occurs after qualification and before any approval or publication
capability.

Observation may use public APIs or the minimum read-only destination authority.
It receives no destination write token, PAT, `id-token: write`, or Approval
Environment.

Each logical projection is classified atomically against snapshot-bound desired
state:

- `absent`;
- `exact-satisfied`;
- `partial`;
- `conflicting`;
- `unknown`; or
- `unprovable`.

Desired state derives from the Qualification Snapshot and admitted artifact
bytes. It includes:

- exact destination coordinate;
- immutable in-package target witness;
- target binding;
- artifact bytes or digest.

For first-slice npm, authoritative exactness is the normalized package name,
frozen native version, downloaded tarball bytes and digests, and embedded
witness in the active registry projection. Runtime Observation does not
enumerate deleted/restorable versions. Ownership, repository association,
visibility, and access are admission and Governance preconditions. The
target-derived
`buddy-sha-<40-lowercase-target-sha>` dist-tag is separately observed
non-authoritative routing metadata, not part of exactness, identity, or
provenance.

Each first-slice Observation separately embeds a package-control proof for the
version-independent destination/normalized-package subject. The closed value
binds supported authoritative endpoints, owner, repository association,
visibility, exposed access facts, observation time, canonical response
digests, and no expected values or Governance digest. The Observation's
eligibility lineage binds the applicable protected Governance; admission
derives the package-control expectations from that lineage and jointly
validates the proof. Unexposed access-grant completeness remains a
Governance-attested limitation.

The Observation Record binds:

- current Attempt;
- exact successful Qualification Decision;
- logical projection;
- immutable desired-state basis;
- canonical remote response; and
- observed facts and digests.

It cannot bind a future Publication Snapshot. The later Snapshot admits the
Observation and seals its resulting action decision.

Only:

- `absent`, meaning absent from the active projection and eligible to produce
  one action only under current tombstone acceptance; and
- `exact-satisfied`, which produces zero actions

may form a ready first-slice Publication Snapshot. Partial, conflicting,
unknown, or unprovable state fails closed before Snapshot formation or
publication. Proven publication-step non-start may finalize as
`failed-before-publication`; a new manual dispatch is the only normal
continuation, while Release Reconciliation remains a separate exceptional
process.

An active-absent coordinate is a legitimate action candidate without prior
Attempt history, but it is not proof that the version was never published, is
not retained as deleted/restorable state, or will accept creation. Intent
reserves nothing. Exact pre-observed active version state produces no action
regardless of dist-tag state or tag-read availability. Differing version bytes
fail closed. A duplicate, hidden-tombstone, conflict, non-success, or ambiguous
response remains failed in the current Attempt even when post-failure readback
is exact. A new dispatch may reobserve the exact active version and take
`exact-satisfied`. Release never uses version overwrite, delete-and-recreate,
compensation, or tag repair.

When the version is absent from active state, action formation additionally
requires the target-derived tag to be successfully observed absent and current
Governance to bind unexpired native acceptance of the deleted/restorable
same-version case. A present or unprovable tag blocks a known overwrite.
Standard `npm publish --tag` has no conditional tag write, so an authorized
external writer may assign the tag after Observation and the approved publish
may move it. This is an explicit bounded risk for the dedicated smoke-only
package and sole-writer TCB. The tag remains
inside the complete mutation footprint and readback, but no supported consumer
uses it as authority. Repository concurrency serializes repository-controlled
publishers only; Release does not claim it constrains external writers.

## Publication Snapshot

After successful qualification and complete Observation, the Publication
Snapshot preserves the Qualification Snapshot and adds:

- Qualification Decision;
- actual artifact identities, digests, and provenance;
- admitted Observation Records;
- exact desired and observed destination state;
- exactly zero or one first-slice Publication Action;
- complete action inputs;
- complete Adapter-declared mutable-resource keys;
- conservative platform serialization projection and group;
- Approval Bundle requirements when an action exists;
- Publication Authorization binding requirements when an action exists; and
- Publication Result contract.

It cannot change target, Execution identity, Release Unit, artifact variants,
Build Definitions, obligations, destination projection, version facts, or
artifact bytes.

The first slice has no capability group, group manifest, group admission
decision, group scheduler, or group result bundle. A future multiple-action or
multiple-destination design is deferred until a concrete second scenario
requires it.

## Zero-Action Exact Finalization

A Publication Snapshot with zero actions means Observation proved the complete
desired state already exact.

The manual Intent and valid Live Eligibility Decision authorize this read-only
path. It:

- repeats protected Governance ancestry, path-touch, blob/content, expiry, and
  `live_enabled` validation immediately before success;
- repeats supported package-control readback;
- repeats authoritative exact-version readback and proves the normalized
  package and version, downloaded tarball bytes and digests, and embedded
  witness still equal the zero-action Snapshot;
- persists one exact-satisfied finalization proof binding the zero-action
  Snapshot, fresh Governance proof, fresh package-control proof, and fresh
  exact-version readback for Finalizer admission;
- requests no Environment approval;
- prepares no approval deployment;
- carries no Approval Bundle or other action-bearing lineage;
- emits no Publication Authorization;
- has current-DAG publisher conclusion `skipped`;
- obtains no Publication Capability;
- persists no mutation marker;
- emits no Publication Result.

The proof uses only
`workflow-delivery/v3/exact-satisfied-finalization-proof`. The narrower
`workflow-delivery/v3/exact-satisfied-governance-proof` schema is incompatible
and has no alias.

The Attempt may finalize with disposition `exact-satisfied`.

This path does not apply to partial, unknown, conflicting, unprovable, or
possibly mutated state. Missing, differing, or unprovable fresh exact-version
state prevents formation of the proof and leaves the zero-action Snapshot on
the existing `unknown` path.

## One-Action Authorization

### Approval Bundle

An action-bearing Publication Snapshot causes Release to prepare one immutable
Approval Bundle before the Environment wait.

The bundle adds only:

- the Publication Snapshot canonical payload digest and Artifact Reference;
- the immutable reviewer-summary payload digest and Artifact Reference.

The Snapshot remains sole owner of the current Attempt, selected ref and target,
Qualification Decision, artifact identities and digests, manifest, lifecycle
scripts, exact compound Publication Action, complete mutable-resource keys, and
serialization projection. Bundle admission resolves and validates that
immutable chain rather than copying those fields.

The reviewer-visible summary includes the target SHA and selected ref, exact
package coordinate, artifact digest and manifest, lifecycle scripts, and the
exact action.

### Approval Job and Publication Authorization

The Approval job references the literal
`workflow-delivery-v3-buddy-approval` Environment. It:

- has no publication capability;
- may use `contents: read` for fresh protected Governance;
- validates the Environment sentinel as its first authority-critical executable
  check;
- proves no protected Governance-path touch since eligibility;
- validates current expiry, bindings, and `live_enabled: true`;
- validates the action's destination-operation-profile digest against current
  Governance, verifies that native acceptance remains unexpired for this
  action-bearing admission, and admits the immutable action as an instantiation
  of the profile;
- strictly admits the Approval Bundle and transitively resolves its complete
  Snapshot, reviewer, artifact, action, and resource closure; and
- durably emits the sole Publication Authorization.

The Publication Authorization adds only:

- the Approval Bundle canonical payload digest and Artifact Reference;
- approval-boundary evidence; and
- the fresh protected-Governance proof.

Its standard producer/current-run envelope identifies the current Attempt. It
reaches selected ref and target, Live Eligibility Decision, reviewer artifact,
Publication Snapshot, Qualification Decision, exact artifacts, action,
operation-profile digest, resource keys, and serialization projection through
the admitted immutable predecessor chain. It does not copy those values,
contain a credential, or bind `github.run_attempt`.

There is no separate post-approval admission authority. The Approval job
performs the complete semantic admission, and the publisher independently
revalidates the resulting closure.

### Publisher Authority

The publisher has an ordinary success dependency on the Approval job. It is the
only step-running job with effective `packages: write`.

A `uses`-only caller may declare `packages: write` solely as the
reusable-workflow ceiling. That caller has no steps or direct token use.

Before mutation, the publisher:

- strictly validates the Publication Authorization;
- transitively resolves and revalidates every Bundle, Snapshot, reviewer,
  artifact, action, and resource binding;
- performs the final fresh protected Governance check, including path-touch
  anti-rollback;
- verifies that `live_enabled` remains true;
- repeats supported package-control readback;
- verifies native acceptance remains within 90 days of capture; and
- validates the action's operation profile against current Governance and the
  actual pinned toolchain and effective command configuration.

It receives short-lived repository `GITHUB_TOKEN`, no PAT fallback, and no
`id-token: write`.

The publisher's final package-control proof uses the same closed value shape,
is observed immediately before marker persistence, and is embedded in the
marker. A mismatch blocks before the marker. A package-control change after that
last read is accepted only inside the already declared sole-writer/publisher
TCB; the architecture does not claim a package-administration lock.

The GitHub Packages principal is repository `hcoona/three`; every package
granting that repository Actions access is within effective token reach. Exact
action validation governs intended behavior, not package-level token
isolation.

## Publication Execution and Records

### First-Slice npm Publish Action

The one-action contract uses the pinned standard npm client to:

- create the exact npm package version as the authoritative effect; and
- assign the target-derived
  `buddy-sha-<40-lowercase-target-sha>` tag as a declared non-authoritative
  routing side effect.

There is no separate normal tag action. If the version is exact, normal flow
materializes zero actions regardless of whether the tag is absent, points to
that version, points elsewhere, or cannot be read. No tag repair is permitted.
If the version is absent from active state, the tag must have been observed
absent and current Governance must bind unexpired acceptance covering the
deleted/restorable same-version case; a present or unprovable tag blocks action
formation.

The action binds the canonical Destination Operation Profile digest plus exact
tarball, package, version, and explicit tag operands. The resolved profile is
the sole owner of registry, access mode, pinned Node/npm toolchain, normalized
command template including all fixed options, request-generation behavior, and
retry prohibitions. Publisher admission resolves it without defaults, validates
the typed operands, and materializes the complete request. Its highest-precedence
CLI option `--fetch-retries=0` forbids automatic retry of the mutating registry
request. Only bounded read-only Observation and readback retries are permitted.

The package container must pre-exist with expected ownership, repository
association, visibility, and access. Before activation, native acceptance must
validate the exact normalized request and bounded observable mutation
footprint through the closed canonical comparison shape owned by the versioned
acceptance suite. Its semantic delta contains only the declared new version and
target tag; unrelated projected versions, tags, and supported package-control
facts remain unchanged. Every required invariant must rely on either a cited
documented lower-layer contract or complete observation through a supported
authoritative interface. A required invariant that is neither documented nor
completely observable blocks activation.

### Mutation-May-Have-Started Marker

Immediately before the first mutating destination operation, the publisher
durably persists the mutation-may-have-started marker.

The marker uses the normal-Live producer/current-run envelope and adds only:

- the Publication Authorization canonical payload digest and Shared Foundation
  Artifact Reference;
- the canonical final publisher-side Governance proof observed at the later
  mutation boundary and its recomputed digest; and
- the canonical final supported package-control proof observed at that boundary
  and its recomputed digest; and
- canonical normalized evidence that the actual pinned toolchain and effective
  command configuration matched the admitted Destination Operation Profile at
  that boundary.

It does not copy the Authorization's Snapshot, action, resource, artifact, or
Attempt closure. The normal-Live producer/current-run envelope identifies the
publisher. The payload does not bind post-upload marker transport values. The
publisher uploads the marker through the standard immutable artifact transport
and validates the returned reference and payload digest before mutation.
Persistence or validation failure blocks mutation.

Once the marker exists, the architecture conservatively assumes mutation may
have started until a durable Publication Result proves a controlled outcome.

### Publication Result

For each controlled post-marker terminal state, the publisher forms one logical
Result and initiates one logical persistence operation. Transport may retry
only the same immutable payload without creating another logical Result. The
only admitted schema is `workflow-delivery/v3/publication-result`; the former
`workflow-delivery/v3/action-result` schema is incompatible.

The current DAG supplies one nullable scalar immutable
`publication-terminal-reference` to the Finalizer. It points to the Result when
one was durably persisted, otherwise to the marker when one was durably
persisted, otherwise it is null. Result takes precedence; the transport does
not introduce a wrapper schema. The Finalizer accepts only null or one
well-formed, correctly bound Artifact Reference whose target schema is exactly
the mutation marker or Publication Result, evaluates only that explicitly
propagated reference, and neither lists nor infers other artifacts. A Result
target must resolve its marker through direct lineage. Malformed, non-scalar,
misbound, or other-kind input fails admission. Release performs no name
fallback or history recovery.

A Publication Result uses the normal-Live producer/current-run envelope and
directly binds:

- the mutation marker canonical payload digest and Shared Foundation Artifact
  Reference;
- command classification;
- post-action Observation or equivalent normalized readback evidence, including
  actual remote coordinate/version state, remote-observed artifact digests,
  remote-extracted witness, and observed state of the action-bound
  target-derived tag when available;
- result `published` or `failed`;
- mutation classification `not-mutated`, `possibly-mutated`, or `mutated`;
- sanitized response identity when a response exists; and
- diagnostics.

The Result does not copy the Authorization's action, resource, artifact,
Snapshot, Attempt closure, requested coordinate or tag, pre-action Observation,
expected artifact digests, or expected witness. Admission reaches those
bindings through the strictly admitted marker and its Authorization. The
auditable before/after view combines the Snapshot's pre-action Observation with
the Result's post-action readback rather than duplicating either authority.

Publication result and mutation classification form the closed state space:

| Publication result | Mutation classification                         | Post-action evidence                          |
| ------------------ | ----------------------------------------------- | --------------------------------------------- |
| `published`        | `mutated`                                       | authoritative exact-version readback required |
| `failed`           | `not-mutated`, `possibly-mutated`, or `mutated` | available normalized readback retained        |

`published` requires definitive success from the current command and successful
authoritative exact-version readback. Conflict, non-success, or ambiguous
responses remain `failed` in the current Attempt even when readback is exact.
`published` with `not-mutated` or `possibly-mutated`, or any other combination
is invalid. `not-mutated` after failure requires complete proof; ambiguity is
`possibly-mutated`. Post-action tag state and exact readback on a failed Result
remain diagnostic and never upgrade the Result.

A failure before the marker emits no Publication Result. Uncontrolled
termination or Result persistence or transport failure may leave no durable
Result.

A marker without a durable Publication Result means the Attempt is unknown and
possibly mutated. A later manual dispatch must reobserve the destination before
deciding whether any action remains.

The first slice emits no wrapper or aggregate result bundle.

## Finalization and Attempt Outcome

The Release Finalizer is read-only and best effort.

When it runs, it validates:

- Intent, Execution, and Attempt identity;
- Repository Model and Snapshot lineage;
- Qualification Decision and Evidence;
- Observation Records;
- the zero-action disposition or action-bearing Approval Bundle and
  Publication Authorization;
- mutation marker and Publication Result;
- the embedded final publisher-side Governance proof carried by the marker;
- the embedded final supported package-control proof carried by the marker;
- the marker's effective toolchain and command-configuration match evidence;
- successful Publication Result evidence when publication succeeded;
- artifact, action, resource, producer, run, purpose, and digest bindings; and
- direct current-DAG facts needed to classify missing lineage.

It does not:

- query destination state to invent a result;
- rerun quality checks;
- reinterpret Adapter-specific output;
- infer publication from a green job;
- repair a missing Result; or
- guarantee that it can run after cancellation or transport failure.

### Current-DAG Platform Facts

The workflow supplies direct facts from the Finalizer's declared `needs`; it
does not persist a platform-history record. The admitted fact set contains:

- the publisher conclusion `success`, `failure`, `cancelled`, or `skipped`;
- when the publisher ran, the isolated publication step's exact
  platform-evaluated outcome `success`, `failure`, `cancelled`, or `skipped`,
  propagated directly from `steps.<publication-step>.outcome` rather than
  produced by a script; and
- one nullable scalar explicitly propagated publication terminal Artifact
  Reference, resolving only to a mutation marker or Publication Result.

The Finalizer derives publisher non-start from `publisher == skipped`. For
publisher `failure` or `cancelled`, only publication-step outcome `skipped`
proves that mutation-capable execution never started. It does not reconstruct
the scheduler's dependency cause. Classification as
`failed-before-publication` additionally requires an exact successful
Qualification Decision, no valid zero-action Publication Snapshot, a null
publication terminal reference, and no contradictory lineage. Publisher conclusion alone, missing
transport, missing output, or a caller- or script-produced boolean does not
prove non-start. The publisher's ordinary success dependency on Approval
remains a statically validated authorization boundary, not runtime evidence for
why GitHub skipped the job.

The Finalizer first admits all supplied records and transports. A malformed,
non-scalar, misbound, or other-kind terminal reference is contradictory and
does not fall back to the null path. Publisher `skipped` with a non-null
terminal reference is also contradictory because a skipped job did not
execute.

When the terminal reference resolves to one valid durable Publication Result:

- publisher `success`, `failure`, or `cancelled` is compatible; the Result
  controls the publication business outcome and the platform conclusion remains
  diagnostic.

When the terminal reference resolves to a valid durable mutation marker:

- mutation cannot be excluded, so the Outcome is `unknown` with
  `possibly_mutated: true`.

When the terminal reference is null:

- a valid zero-action Snapshot with a valid exact-satisfied finalization proof, publisher
  `skipped`, and no Approval Bundle, Authorization, or
  other action-bearing lineage becomes `exact-satisfied`;
- a valid zero-action Snapshot without that proof, with the same publisher and
  no-action-lineage tuple, becomes `unknown` with `possibly_mutated: false`;
- publisher `skipped` with exactly one admitted pre-marker predecessor, no
  valid zero-action Snapshot, and no contradictory lineage becomes
  `failed-before-publication`;
- publisher `failure` or `cancelled` with exact platform-derived
  publication-step outcome `skipped`, exactly one admitted pre-marker
  predecessor, no valid zero-action Snapshot, and no
  contradictory lineage also becomes `failed-before-publication`;
- publisher `success`, publisher `failure` or `cancelled` without the exact
  `skipped` publication-step proof above supplies no publication evidence and
  becomes `unknown` with `possibly_mutated: true`.

A failed or incomplete Qualification Decision remains the terminal
authoritative record and forms no Attempt Outcome.

Any zero-action Snapshot with publisher `success`, `failure`, or `cancelled`,
or with a non-null publication terminal reference, Approval Bundle,
Authorization, or other
action-bearing lineage, is contradictory. The generic missing-Result
`unknown` case never absorbs a valid zero-action Snapshot.

The Finalizer never reconstructs cancellation timing or queries platform
history to refine these facts.

### Failed Before Publication

After successful Qualification, Observation, Snapshot materialization, artifact
transport, approval waiting, or platform cancellation may stop the Attempt
before publication.

The Finalizer may classify `failed-before-publication` only when direct
current-DAG facts prove that mutation-capable execution never started, exact
Qualification succeeded, and no valid zero-action Publication Snapshot
applies. Publisher `skipped` supplies that proof; for publisher `failure` or
`cancelled`, the isolated publication step must have the exact platform-derived
`skipped` outcome. Missing or script-produced execution facts, artifacts, or
records are insufficient.

Consistent proof includes:

- the exact successful Qualification Decision;
- current workflow facts showing publisher non-start or exact platform-derived
  publication-step non-start;
- no valid zero-action Publication Snapshot;
- no mutation marker;
- no Publication Result; and
- no contradictory downstream lineage.

If a Publication Authorization exists, the Outcome binds it as the direct
predecessor. Otherwise it binds, in order, a persisted Approval Bundle, an
action-bearing Publication Snapshot, the sole blocking Observation, or the
exact successful Qualification Decision only when no Observation exists.
Multiple candidates at the selected tier are contradictory. A non-blocking
Observation followed by Snapshot materialization or transport failure has no
admitted direct predecessor and forms no Outcome. Publication-preparation
uncertainty remains only a derived explanation.

When this classification is provable, the Outcome records
`failed-before-publication` and `possibly_mutated: false`. A later normal
continuation is a new manual dispatch, but reconciliation or remediation may
be required first. The Outcome does not fabricate a missing Snapshot.

The exact reason for an Environment rejection need not be reconstructed.

When publication-step start cannot be excluded, the Finalizer preserves
`unknown` disposition with `possibly_mutated: true`. GitHub cancellation or
Finalizer transport failure may leave no durable Attempt Outcome at all.

### Successful Attempt Outcomes

A successful Attempt has exactly one disposition:

- `exact-satisfied`: zero actions and no approval or publication lineage; or
- `published`: one authorized action, a successful Publication Result, and
  authoritative exact post-action readback.

Incomplete, unknown, conflicting, partial, or possibly mutated state is never a
success.

`disposition` and `possibly_mutated` are the complete authoritative Outcome
classification. Human-readable result summaries and operator guidance are
derived outside the canonical record.

For a Result-bearing path, Outcome directly binds the Publication Result
payload digest and reaches marker, Authorization, and action through that
admitted Result. A marker-without-Result Outcome directly binds the marker
payload digest. Earlier terminal paths bind only their latest valid
predecessor:

| Terminal case                                           | Tagged direct predecessor                                                                                                                                                         |
| ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `exact-satisfied`                                       | exact-satisfied finalization proof, which directly binds the zero-action Snapshot plus fresh Governance and package-control proofs and fresh authoritative exact-version readback |
| `published` or `publication-failed`                     | Publication Result                                                                                                                                                                |
| `unknown` with a marker and no Result                   | mutation marker                                                                                                                                                                   |
| `unknown` for zero-action Snapshot missing fresh proof  | zero-action Publication Snapshot                                                                                                                                                  |
| pre-marker action path with Authorization               | Publication Authorization                                                                                                                                                         |
| pre-Authorization path with a persisted Approval Bundle | Approval Bundle                                                                                                                                                                   |
| pre-bundle path with an action-bearing Snapshot         | action-bearing Publication Snapshot                                                                                                                                               |
| pre-Snapshot path with a sole blocking Observation      | blocking Observation Record                                                                                                                                                       |
| interruption before any Observation                     | exact successful Qualification Decision                                                                                                                                           |

The selected Observation directly binds the exact successful Qualification
Decision. Multiple candidates at the selected predecessor tier are
contradictory. The Outcome carries no parallel ancestor digests reachable
through the selected predecessor.

Attempt Outcome is immutable when formed, but the architecture does not depend
on a permanent exhaustive Outcome ledger.

## New-Dispatch Retry

Retry is a new manual dispatch with a new `workflow_run_id` and a new Attempt.

It repeats:

- purpose selection;
- request-local Repository Model compilation;
- live eligibility;
- Execution concurrency admission;
- Qualification Snapshot formation;
- complete build;
- all Release quality obligations;
- destination Observation;
- Publication Snapshot formation;
- Approval Bundle and Environment approval when one action remains;
- Publication Authorization;
- publication when authorized; and
- best-effort reporting.

It reuses no prior Attempt's Snapshot, Evidence, artifact, Observation,
approval, Publication Authorization, or Result.

Dispatching an older immutable target continues to use the workflow and control
code contained in that target. A fix in a newer revision is not substituted
into the older target's Attempt.

For the first-slice npm unit, identical target, frozen inputs, Build Definition,
and toolchain must reproduce identical bytes. Existing differing destination
bytes fail closed into reconciliation and separately authorized remediation.

Nondeterministic Release Units require a future sealed-artifact
publication-resume design and are unsupported now.

Native Actions history may help an operator diagnose a prior run. It is not
discovered or admitted as publication authority and does not determine whether
the new Attempt may proceed.

## Reconciliation and Break-Glass Remediation

### Read-Only Reconciliation

Reconciliation:

- starts from current destination facts;
- references the affected Release and projection when available;
- determines whether state is absent, exact, partial, conflicting, unknown, or
  unprovable;
- emits an append-only Reconciliation Record; and
- performs no mutation.

A normal new dispatch already performs Observation. A separate reconciliation
workflow is reserved for exceptional state that cannot safely proceed through
normal Observation. It is not an Attempt Outcome field and never resumes the
old Attempt. The first-slice Normal-Live implementation defines this boundary
but defers a standalone reconciliation workflow and Reconciliation Record until
a concrete exceptional case requires durable read-only adjudication beyond
manual investigation and fresh normal Observation. First-slice operator
investigation emits no Reconciliation Record and is not formal Release
Reconciliation.

### Break-Glass Remediation

Remediation is a separate manual, protected process. It is not a `force` option
on Release Intent. The first-slice implementation defers this workflow. Any
future implementation must consume a valid formal Reconciliation Record; manual
investigation alone cannot authorize remediation.

It requires:

- the original Release Execution, Attempt, Publication Snapshot, and action;
- the original action's complete frozen Adapter-declared mutable-resource keys;
- exact expected current destination state;
- a qualified source artifact when bytes are required;
- one allowlisted remediation action;
- reason and incident or work-item reference;
- stronger approval;
- narrowly scoped remediation capability; and
- append-only before-and-after records.

A future protected remediation implementation must strictly admit any
cross-revision request before approval. It must verify producer repository and
workflow, original target and run, Snapshot and action digests, qualified
artifacts, Reconciliation Record, contract version, and payload digest.

Remediation reuses the original complete resource-key set exactly. It never
derives keys from Product or Execution Identity, current destination state, or
current Adapter defaults. It performs expected-state checks immediately before
mutation and never silently overwrites.

A later normal manual dispatch rebuilds, requalifies, and observes the
destination to prove the resulting state.

## Concurrency

Release has two independent concurrency boundaries.

### Release Execution Concurrency

Caller-held concurrency and pending-request coalescing use the complete Release
Execution Identity.

- In-progress Executions are not auto-canceled.
- At most the newest pending duplicate request is retained.
- A pending request replaced before admission creates no Attempt.
- Each surviving dispatch compiles its request-local Repository Model before
  entering the concurrency-scoped caller.
- The caller holds the Execution identity slot through terminal workflow state,
  including the read-only Finalizer when it runs.

This boundary prevents duplicate repository-controlled execution for the same
business identity. It does not serialize unrelated external resources.

### Mutable-Resource Concurrency

Every mutating Destination Adapter declares the complete deterministic
mutable-resource key set for its action.

Package keys include the exact External Package Coordinate plus every
additional Adapter-required resource. For first-slice npm, the standard publish
action includes:

- the exact package coordinate; and
- the destination, normalized package name, and target-derived routing tag.

Publication Snapshot and action bind the complete key set directly.
Publication Authorization, publisher admission, and Publication Result
admission reach that set transitively through
`Result -> marker -> Authorization -> Approval Bundle -> Snapshot`; they do not
copy it. Remediation binds the original action's complete key set.

Live actions whose complete key sets overlap must serialize. GitHub supports
equality groups rather than arbitrary set-overlap locking, so the first-slice
Adapter uses a conservative projection of physical destination plus normalized
npm package name. This intentionally serializes different versions and
target-derived tags for the same package.

The projection must not replace or weaken the complete key set. Missing,
unknown, incomplete, conflicting, or unenforceable keys or projections block
publication.

GitHub concurrency is not authorization, a durable queue, a distributed lock,
or protection against external writers.

## Platform-Native Retention

Current-Attempt control records and artifacts are operational records within
the configured Actions retention window. The first slice uses 45-day Release
retention, exceeding the supported Environment approval-expiry window with
operational margin. Fresh preactivation and post-merge evidence uses
authenticated repository Actions artifact-and-log retention readback and
requires an effective value of at least 45 days.

Actions artifact names are non-authoritative collision-safe indexes. Producers
capture immutable artifact ID, digest, and URL. Current-Attempt consumers fetch
only by ID and validate:

- record kind;
- producer;
- `workflow_run_id`;
- target;
- purpose;
- payload identity; and
- digest.

Normal-Live records and artifact bindings omit `github.run_attempt` because the
all-authoritative-job guard makes attempt 1 a platform invariant. Simulation
and other contexts retain their own run-attempt contracts.

Name fallback, latest-artifact selection, and history-derived authority are
invalid.

Longer-lived identity and provenance may rely on Git commits and refs, package
or registry records, GitHub tags and Releases when selected, and external
attestations when selected.

After operational records expire, a new dispatch proceeds only from facts it
can currently prove. An absent destination remains valid initial state. Present
but unprovable state fails closed. No permanent Release ledger, Product-to-
target binding index, or exhaustive Attempt lineage is required.

## Official and Future Publication

Official retains:

- protected authoritative target eligibility;
- owner-reviewed same-revision control code;
- Official Product and Execution identities;
- exact native NBGV publication projection;
- destination and credential isolation from Buddy;
- independent Release build and qualification; and
- separately governed remediation.

Official simulation retains the full simulation contract described above.

The first-slice zero-or-one action model is not a speculative multi-destination
framework. Multiple live actions, multiple destinations, or sealed-artifact
publication resume require a concrete second scenario and a new reviewed
design. This MLD does not preselect a generic transaction, compensation,
rollback, or Saga protocol.

## Failure Conditions

Pre-Attempt processing fails closed when:

- purpose is ambiguous or cross-purpose input is supplied;
- the request-local Repository Model is missing, incomplete, or misbound;
- exact target ancestry or native NBGV facts cannot be proved;
- selected ref and target differ;
- static-reference or protected Governance eligibility blocks;
- Governance is expired, disabled, binding-mismatched, or path-touched;
- Official target lineage is ineligible;
- Release Execution Identity cannot be derived; or
- an authoritative normal-Live job is not on `github.run_attempt == 1`.

Attempt processing fails closed when:

- policy, variants, obligations, destinations, or Adapter bindings are
  incomplete;
- a Build Adapter recomputes or substitutes a version;
- a required obligation is not satisfied;
- artifact identity, provenance, or deterministic first-slice bytes are
  unprovable;
- destination Observation is partial, conflicting, unknown, or unprovable;
- an action remains and the pinned standard npm publish operation has not
  passed the bounded destination acceptance;
- the Publication Snapshot contains more than one first-slice action;
- the action-bearing Approval Bundle is incomplete;
- the Approval job lacks approval or cannot emit a valid Publication
  Authorization;
- a publisher starts without ordinary success dependency on the Approval job;
- any step-running nonpublisher job has effective `packages: write`;
- the publisher's final Governance or authorization closure check fails;
- the mutation marker cannot be persisted;
- a successful publication lacks a Publication Result with complete success
  evidence;
- marker exists without Result, leaving unknown possible mutation;
- resource keys or serialization projection are incomplete or unenforceable;
- an action attempts overwrite, delete-and-recreate, or an unapproved
  administrative operation;
- an authoritative record cannot be persisted; or
- Finalizer inputs conflict.

No failure falls back to a weaker channel, credential, Environment, target,
artifact subset, destination subset, previous Attempt, or overwrite mode.

## Acceptance Scenarios

### Destination Mutation Capability

The action-bearing first slice is not activation-admissible until the pinned
standard npm publish operation passes a separately authorized native acceptance
against a pre-existing disposable GitHub Packages package with expected
ownership, repository association, visibility, and access.

Each required safety property must rely on either a cited documented lower-layer
contract or complete observation through a supported authoritative interface.
If neither is available, activation remains blocked.

The versioned Native Acceptance Suite compares before and after state through
one closed canonical shape containing:

- normalized package identity;
- complete active version-name inventory;
- complete dist-tag mapping;
- remote-observed bytes, digests, and embedded witness for scenario versions;
  and
- supported owner, repository-association, visibility, and exposed-access
  facts.

For the deleted/restorable scenario only, the acceptance procedure uses
separately authorized package-admin credentials and extends the projection with
the complete deleted-version inventory for the disposable package, the targeted
deleted version's stable identity and restorable state, and the original bytes,
digests, and witness after restoration. Those credentials and facts never enter
runtime Observation or publication.

Raw responses and their digests remain evidence. The projection explicitly
excludes server-generated timestamps, request identifiers, URLs, and equivalent
volatile metadata. Derived counters such as `version_count` are recomputed from
the projected version inventory or validated against the scenario's expected
delta.

Acceptance validates:

1. active-absent version `V` and absent target-derived tag `T` permit exactly
   one `npm publish --tag T --fetch-retries=0`;
2. downloaded `V` bytes, digests, and embedded witness are exact;
3. duplicate publishes of `V` with identical or differing bytes cannot replace
   the existing version or alter `T` or other observed package state;
4. after Observation of absent `V` and `T`, a competing writer may create
   distinct version `W` and map `T` to `W`; the candidate may fail or may create
   exact `V` and move `T`, but both immutable versions must remain exact;
5. only the declared new version and `T` may change; `latest`, unrelated
   versions and tags, and supported owner, repository-association, visibility,
   and access readbacks remain unchanged;
6. an exact version with absent, mismatched, or unreadable `T` produces the
   zero-action path and no tag repair; and
7. conflict, non-success, and ambiguous-response cases do not become
   same-Attempt `published` and do not trigger a second mutating invocation;
   and
8. a fresh unique disposable version can be published and verified, deleted
   into restorable state, and then subjected sequentially to identical- and
   differing-byte same-version invocations of the exact pinned profile. Each
   invocation must fail definitively and leave the complete active-version
   inventory, deleted-version inventory and targeted tombstone identity,
   dist-tag mapping, and package-control facts unchanged. The first empty delta
   is proved before the second invocation. The original object is then restored
   and its original bytes, digests, and witness are verified.

Any success, ambiguous response, projection change, inability to prove
continued restorability, or restore/readback failure in scenario 8 rejects the
profile and keeps Live disabled.

The accepted tag race is bounded routing damage, not an atomic tag guarantee.
Synthetic tests alone cannot establish GitHub Packages behavior.

Protected Governance reuses `DestinationPrimitiveAttestation`; it does not
introduce a second acceptance authority. That subrecord binds:

- canonical Destination Operation Profile digest;
- native-acceptance-suite version;
- approved disposable-package preconditions;
- GitHub API version and cited lower-layer contract revision;
- capture time;
- canonical evidence digest identifying the exact successful acceptance
  generation.

A change to the resolved profile, suite, disposable-package precondition, API,
or contract revision reopens acceptance.
Detailed inputs, active/deleted projections, tombstone facts, and raw results
remain in the separately authorized acceptance evidence rather than runtime
Governance.
Every Publication Action carries the
admitted profile digest. Approval compares it with current Governance and
validates the concrete package, version, tarball, and tag operands as a profile
instantiation; publisher admission repeats those checks against the actual
runtime toolchain and effective command configuration. Those concrete values
remain exclusively in the Publication Action. Initial activation of a newly
admitted profile binds acceptance captured after implementation of that exact
profile and no later than Governance `inspected_at`. Later Governance may reuse
the generation only while every bound input remains identical and
action-bearing admission occurs no later than 90 days after `captured_at`.
Binding change or age expiry requires recapture before action-bearing
publication. Expiry does not block zero-action exact-satisfied finalization.

### Exact-Satisfied First Slice

Observation initially proves version bytes and embedded witness are exact. The
exact-satisfied finalization proof freshly repeats that authoritative
exact-version readback and supported package-control validation immediately
before success. Target-derived tag state is diagnostic and may be absent,
mismatched, or unavailable.

- Publication Snapshot contains zero actions.
- No Environment approval is requested.
- No Approval Bundle, Publication Authorization, capability, marker, Result, or
  other action-bearing lineage exists.
- The publisher conclusion is `skipped`.
- Attempt Outcome has `exact-satisfied` disposition and
  `possibly_mutated: false`.

### Action-Bearing First Slice

After destination mutation capability admission, Observation proves the exact
version absent and the target-derived tag absent.

- Publication Snapshot contains the one standard npm publish action, including
  the declared target-derived tag side effect.
- Approval Bundle binds the Snapshot and reviewer-summary artifacts without
  copying their authoritative fields.
- The Approval job receives Environment approval and emits Publication
  Authorization after fresh Governance and operation-profile admission.
- The publisher transitively revalidates the closure, fresh Governance,
  supported package-control state, native-acceptance age, and actual operation
  profile.
- The marker persists before mutation.
- A successful Publication Result contains definitive command success and
  authoritative exact post-action readback.
- Attempt Outcome has `published` disposition and `possibly_mutated: false`.

### Controlled Failure After Marker

The marker persists and the destination operation returns a controlled
failure.

- Publication Result records the failure and mutation classification.
- Success-only evidence is absent.
- Finalizer does not report success.
- A new manual dispatch reobserves before planning another action.

### Lost Result After Marker

The marker persists, but cancellation or transport failure prevents durable
Publication Result persistence.

- State is unknown and possibly mutated.
- Finalization may be absent.
- No blind retry or GitHub rerun is permitted.
- A new manual dispatch begins from fresh destination Observation.

### New-Dispatch Retry

A prior Attempt fails before publication.

- The operator creates a new manual dispatch and `workflow_run_id`.
- Release recompiles, rebuilds, requalifies, and reobserves.
- If the destination is exact, the new Attempt takes the zero-action path.
- If one action remains, it obtains a new approval and Authorization.
- No prior Attempt artifact or approval is reused.

### Official Dry-Run

A feature ref runs Official simulation.

- The simulation-purpose Snapshot binds run ID and run attempt.
- Simulation derives its separate identity only after Snapshot validation.
- All variants and Official quality obligations run.
- Hypothetical destination requirements and actions are reported.
- No live identity, approval, Publication Authorization, capability,
  Publication Result, or mutation is created.

### Overlapping Package Resources

Two distinct Executions plan actions for the same physical destination and npm
package.

- Their business identities remain distinct.
- Complete resource keys remain distinct and authoritative.
- The conservative destination/package projection serializes both actions.
- Any duplicate, conflict, non-success, or ambiguous response keeps the later
  Attempt failed; only a new dispatch may reobserve exact state.
- Serialization never weakens Observation or authorization.

### Future Break-Glass Remediation Contract

A future separately approved implementation handles destination state that is
partial or conflicting and cannot proceed normally. It is not part of
first-slice delivery acceptance.

- A protected remediation workflow admits the original immutable lineage and
  exact frozen resource keys.
- Stronger approval and expected-state checks precede mutation.
- Before-and-after state is appended without rewriting the original Attempt.
- A later normal dispatch proves the resulting destination state.

## Deferred LLD Decisions

Lower-layer design may define:

- exact Intent, identity, Snapshot, Evidence, Observation, Approval Bundle,
  Publication Authorization, marker, Result, Outcome, reconciliation,
  and remediation schemas;
- exact canonicalization and digest algorithms;
- exact normal-Live attempt-1 guards on every authoritative job;
- exact current-Attempt artifact names and ID-only transport;
- exact static-reference and Governance path-touch proof;
- exact Build and Quality batching;
- exact destination Observation and atomic non-overwriting create-only
  authoritative package-version effects, including the explicit bounded
  exception for non-authoritative tag side effects;
- exact reviewer summary and Approval Environment integration;
- exact publisher permission declarations and caller ceiling;
- exact marker and Result persistence mechanism;
- exact Finalizer current-DAG fact inputs;
- exact concurrency key and conservative group encoding;
- tests for every scenario and failure condition above.

Lower-layer design must not add first-slice capability groups, a second
publication Environment, history admission, run-attempt domain bindings,
GitHub rerun recovery, or a speculative multi-action framework.
