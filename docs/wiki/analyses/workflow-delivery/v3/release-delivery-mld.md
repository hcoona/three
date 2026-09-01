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
- mutation marker, Publication Result, Receipt, and Attempt Outcome semantics;
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
9. The mutation marker is durable before mutation, and Publication Result is
   durable after a controlled action outcome.
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
Snapshot, Authorization, Result, or Receipt identity.

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
- Receipt; or
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
- Receipt payload semantics;
- destination mutability;
- required Publication Capability;
- complete deterministic mutable-resource keys; and
- any conservative platform serialization projection.

Shared Foundation may provide generic clients and binding primitives but does
not classify projections, plan actions, or decide recovery.

For a registry destination, the Adapter contract must establish:

- atomic non-overwriting creation;
- pure create-only or atomic create-or-exact semantics;
- durable exact-state observation for coordinate, ownership, bytes, and target
  witness; and
- deterministic conflict classification.

If the destination cannot provide those guarantees, live publication is
unsupported rather than emulated through a reservation, tag witness, binding
index, application lock, or permanent ledger.

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
- expected ownership;
- immutable in-package target witness;
- target binding;
- artifact bytes or digest; and
- required routing projections.

For first-slice npm, desired state includes
`buddy-sha-<40-lowercase-target-sha>` mapped to the frozen native version. The
tag is routing, not provenance.

The Observation Record binds:

- current Attempt;
- logical projection;
- immutable desired-state basis;
- canonical remote response; and
- observed facts and digests.

It cannot bind a future Publication Snapshot. The later Snapshot admits the
Observation and seals its resulting action decision.

Only:

- `absent`, which may produce one action; and
- `exact-satisfied`, which produces zero actions

may form a ready first-slice Publication Snapshot. Partial, conflicting,
unknown, or unprovable state fails closed into reconciliation.

An absent coordinate is legitimate initial-publication state. It is not
reserved by Intent and does not require prior Attempt history. Atomic
create-or-exact may accept a concurrently created exact state without mutation;
differing state fails without mutation. Release never uses read-then-upsert,
overwrite, or delete-and-recreate.

For the first-slice version-and-tag projection, the mutation primitive must
preserve that rule across both resources. Standard `npm publish --tag` protects
immutable version creation but can unconditionally move a conflicting tag
introduced after Observation. It is not an admitted normal-Live primitive.
Repository concurrency, another read, and post-action readback do not close
that race. The GitHub Packages first slice remains activation-blocked until a
reviewed supported primitive passes the conditional non-overwrite acceptance.

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
- Publication Result and Receipt contract.

It cannot change target, Execution identity, Release Unit, artifact variants,
Build Definitions, obligations, destination projection, version facts, or
artifact bytes.

The first slice has no capability group, group manifest, group admission
decision, group scheduler, or group result bundle. A future multiple-action or
multiple-destination design is deferred until a concrete second scenario
requires it.

## Zero-Action Exact Reconciliation

A Publication Snapshot with zero actions means Observation proved the complete
desired state already exact.

The manual Intent and valid Live Eligibility Decision authorize this read-only
path. It:

- repeats protected Governance ancestry, path-touch, blob/content, expiry, and
  `live_enabled` validation immediately before success;
- persists that fresh no-op Governance proof for Finalizer admission;
- requests no Environment approval;
- prepares no approval deployment;
- emits no Publication Authorization;
- starts no publisher;
- obtains no Publication Capability;
- persists no mutation marker;
- emits no Publication Result; and
- creates no Receipt.

The Attempt may finalize as `success` with disposition `exact-satisfied`.

This path does not apply to partial, unknown, conflicting, unprovable, or
possibly mutated state.

## One-Action Authorization

### Approval Bundle

An action-bearing Publication Snapshot causes Release to prepare one immutable
Approval Bundle before the Environment wait.

The bundle closes:

- current Attempt, selected ref, and target;
- Qualification Decision;
- Publication Snapshot;
- immutable reviewer summary;
- artifact identities, digests, and manifest;
- lifecycle scripts;
- the exact compound Publication Action;
- complete mutable-resource keys; and
- conservative serialization projection.

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
- strictly admits the complete Approval Bundle and current Snapshot, reviewer,
  artifact, action, and resource closure; and
- durably emits the sole Publication Authorization.

The Publication Authorization binds:

- current Attempt;
- selected ref and target;
- Live Eligibility Decision and fresh Governance result;
- Approval Bundle and reviewer artifact;
- Publication Snapshot and Qualification Decision;
- exact artifact identities and digests;
- the one Publication Action;
- complete mutable-resource keys; and
- conservative serialization projection.

It contains no credential and does not bind `github.run_attempt`.

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
- revalidates every Snapshot, reviewer, artifact, action, and resource binding;
- performs the final fresh protected Governance check, including path-touch
  anti-rollback; and
- verifies that `live_enabled` remains true.

It receives short-lived repository `GITHUB_TOKEN`, no PAT fallback, and no
`id-token: write`.

The GitHub Packages principal is repository `hcoona/three`; every package
granting that repository Actions access is within effective token reach. Exact
action validation governs intended behavior, not package-level token
isolation.

## Publication Execution and Records

### Candidate First-Slice Compound Action

The required one-action contract atomically covers:

- creation of the exact npm package version; and
- assignment of the target-derived
  `buddy-sha-<40-lowercase-target-sha>` routing tag.

There is no separate normal tag action. If the version is exact but the tag is
absent or mismatched, normal flow does not weaken the projection into two
actions; it enters reconciliation.

No current invocation is admitted for this action. Standard
`npm publish --tag` is a rejected baseline because it can overwrite a
post-Observation competing tag. A future supported primitive must be added by a
reviewed design update and pass the required race acceptance before an
action-bearing Snapshot can form. If that primitive invokes npm, it must set
the highest-precedence CLI option `--fetch-retries=0`. Automatic retry of a
mutating registry request remains forbidden; only bounded read-only Observation
and readback retries are permitted.

### Mutation-May-Have-Started Marker

Immediately before the first mutating destination operation, the publisher
durably persists the mutation-may-have-started marker.

Marker persistence failure blocks mutation. Once the marker exists, the
architecture conservatively assumes mutation may have started until a durable
Publication Result proves a controlled outcome.

### Publication Result and Receipt

After the attempted or completed action, the publisher durably persists one
Publication Result bound to:

- Publication Authorization;
- exact action and mutable resources;
- actual destination response and state;
- mutation classification; and
- diagnostics.

A successful `published` Result embeds exactly one Receipt.

A controlled failed Result after the marker may omit the Receipt. It must still
preserve the mutation classification and diagnostics needed for safe
reobservation.

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
- Receipt when publication succeeded;
- artifact, action, resource, producer, run, purpose, and digest bindings; and
- direct current-DAG facts needed to classify missing lineage.

It does not:

- query destination state to invent a result;
- rerun quality checks;
- reinterpret Adapter-specific output;
- infer publication from a green job;
- repair a missing Result or Receipt; or
- guarantee that it can run after cancellation or transport failure.

### Failed Before Publication

After successful Qualification, Observation, Snapshot materialization, artifact
transport, approval waiting, or platform cancellation may stop the Attempt
before publication.

The Finalizer may classify `failed-before-publication` only when direct
current-DAG facts prove that the publisher never started. A missing artifact or
record alone is insufficient.

Consistent proof includes:

- the exact successful Qualification Decision;
- current workflow facts showing publisher non-start;
- no mutation marker;
- no Publication Result or Receipt; and
- no contradictory downstream lineage.

If the Publication Snapshot was durably persisted, the Outcome retains that
Snapshot lineage. If it was not persisted, the Outcome retains the successful
Qualification lineage and publication-preparation uncertainty.

When this classification is provable, the Outcome records
`failed-before-publication`, `possibly_mutated: false`, and next action
`new-dispatch`. It does not fabricate a missing Snapshot.

The exact reason for an Environment rejection need not be reconstructed.

When publisher start cannot be excluded, the Finalizer preserves incomplete or
unknown state. GitHub cancellation or Finalizer transport failure may leave no
durable Attempt Outcome at all.

### Successful Attempt Outcomes

A successful Attempt has exactly one disposition:

- `exact-satisfied`: zero actions and no approval or publication lineage; or
- `published`: one authorized action, a successful Publication Result, and
  exactly one embedded Receipt.

Incomplete, unknown, conflicting, partial, or possibly mutated state is never a
success.

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
approval, Publication Authorization, Result, or Receipt.

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
normal Observation.

### Break-Glass Remediation

Remediation is a separate manual, protected process. It is not a `force` option
on Release Intent.

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

Current protected remediation code strictly admits any cross-revision request
before approval. It verifies producer repository and workflow, original target
and run, Snapshot and action digests, qualified artifacts, Reconciliation
Record, contract version, and payload digest.

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
additional Adapter-required resource. For first-slice npm, the compound
version-and-tag action includes:

- the exact package coordinate; and
- the destination, normalized package name, and target-derived routing tag.

Publication Snapshot, action, Publication Authorization, Receipt, and
remediation all bind the complete key set.

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
publication resume require a concrete second scenario and a new design. Future
multi-destination publication must use append-only Saga semantics, but its
implementation is deferred rather than represented through current capability
groups.

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
- an action remains and the selected destination primitive has not passed the
  complete version-and-tag conditional non-overwrite acceptance;
- the Publication Snapshot contains more than one first-slice action;
- the action-bearing Approval Bundle is incomplete;
- the Approval job lacks approval or cannot emit a valid Publication
  Authorization;
- a publisher starts without ordinary success dependency on the Approval job;
- any step-running nonpublisher job has effective `packages: write`;
- the publisher's final Governance or authorization closure check fails;
- the mutation marker cannot be persisted;
- a successful publication lacks a Publication Result with exactly one Receipt;
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

The action-bearing first slice is not activation-admissible until a documented
supported destination primitive passes this race against a separately
authorized disposable GitHub Packages coordinate:

1. Observation proves desired version `V` and target-derived tag `T` absent.
2. After Observation, a competing writer creates distinct version `W` and maps
   `T` to `W`.
3. The candidate operation for `V` and `T` fails without creating `V` and
   without moving `T` from `W`.

Standard `npm publish V --tag T` does not provide that conditional behavior and
must fail this admission. Repository concurrency, a second read, or successful
post-action exact readback is insufficient. Synthetic tests may reject a client
mechanism but cannot establish destination support.

### Exact-Satisfied First Slice

Observation proves package ownership, version bytes, embedded witness, and
target-derived tag mapping are exact.

- Publication Snapshot contains zero actions.
- No Environment approval is requested.
- No Publication Authorization, publisher, capability, marker, Result, or
  Receipt exists.
- Attempt Outcome is `success` with `exact-satisfied` disposition.

### Action-Bearing First Slice

After destination mutation capability admission, Observation proves the
complete desired projection absent.

- Publication Snapshot contains the one compound version-and-tag action.
- Approval Bundle exposes the complete reviewer and machine closure.
- The Approval job receives Environment approval and emits Publication
  Authorization.
- The publisher revalidates the closure and fresh Governance.
- The marker persists before mutation.
- A successful Publication Result embeds exactly one Receipt.
- Attempt Outcome is `success` with `published` disposition.

### Controlled Failure After Marker

The marker persists, the destination operation returns a controlled failure,
and no success Receipt exists.

- Publication Result records the failure and mutation classification.
- Receipt may be absent.
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
- No live identity, approval, Publication Authorization, capability, Receipt,
  or mutation is created.

### Overlapping Package Resources

Two distinct Executions plan actions for the same physical destination and npm
package.

- Their business identities remain distinct.
- Complete resource keys remain distinct and authoritative.
- The conservative destination/package projection serializes both actions.
- The later action may accept an atomic exact race or fail on differing state.
- Serialization never weakens Observation or authorization.

### Break-Glass Remediation

Destination state is partial or conflicting and cannot proceed normally.

- A protected remediation workflow admits the original immutable lineage and
  exact frozen resource keys.
- Stronger approval and expected-state checks precede mutation.
- Before-and-after state is appended without rewriting the original Attempt.
- A later normal dispatch proves the resulting destination state.

## Deferred LLD Decisions

Lower-layer design may define:

- exact Intent, identity, Snapshot, Evidence, Observation, Approval Bundle,
  Publication Authorization, marker, Result, Receipt, Outcome, reconciliation,
  and remediation schemas;
- exact canonicalization and digest algorithms;
- exact normal-Live attempt-1 guards on every authoritative job;
- exact current-Attempt artifact names and ID-only transport;
- exact static-reference and Governance path-touch proof;
- exact Build and Quality batching;
- exact destination Observation and atomic create-or-exact commands;
- exact reviewer summary and Approval Environment integration;
- exact publisher permission declarations and caller ceiling;
- exact marker and Result persistence mechanism;
- exact Finalizer current-DAG fact inputs;
- exact concurrency key and conservative group encoding;
- exact remediation workflow commands; and
- tests for every scenario and failure condition above.

Lower-layer design must not add first-slice capability groups, a second
publication Environment, history admission, run-attempt domain bindings,
GitHub rerun recovery, or a speculative multi-action framework.
