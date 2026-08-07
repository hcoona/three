# Workflow Delivery v3 Release Delivery MLD

## Status

Architecture version: **v3**.

Review state: **Identity and absent-coordinate decisions reopened and
reconfirmed before LLD on 2026-08-05**.

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
3. Buddy and Official select different channel-scoped destination identities
   and Capability boundaries, not different product versions or subsets.
4. Release independently builds and qualifies its target. CI runtime records are
   not Release inputs.
5. Every quality obligation in a Release Plan is required.
6. Publication authorization binds exact artifacts, observations, actions, and
   the successful Qualification Decision.
7. Target-code execution and publication capability do not coexist except for
   the explicitly accepted first-slice live Buddy GitHub Packages publisher.
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

Intent identifies a request, not a product version or external package. More
than one admitted Intent may address the same Release Execution Identity.

The normalized mode branches into live release or release simulation before
live eligibility, Product or Execution Identity lookup, coalescing, admission,
or Attempt creation. Official live execution accepts only
Governance-configured authoritative refs. For the
`hcoona-release-smoke-npm` GitHub Packages slice, Buddy live execution accepts
any same-repository ref selected by `workflow_dispatch`. Future Buddy
destinations do not inherit this exception. Other refs may exercise allowed
simulation behavior without entering live identity or receiving live
capability.

### Request-Local Repository Model Input

After the purpose branch and before any live Release Execution lookup, request
coalescing, or admission, each candidate run attempt performs exactly one
same-revision Repository Model compilation for its pinned target and branch
purpose.

The resulting immutable Repository Model Snapshot:

- is bound to purpose, request identity, `github.run_id`, `github.run_attempt`,
  target, producer, and control identity;
- closes descriptors, Project Nodes and dependency graph, Build Definitions,
  modeled variants and outputs, and complete build and artifact scope;
- supplies authoritative target-bound canonical and native NBGV facts;
- is not imported from CI, another purpose, another request, or a prior live
  Attempt or simulation pass; and
- becomes the sole authoritative technical model input reused throughout the
  resulting admitted live Attempt or simulation pass.

Any NBGV-owning Provider used by this compilation checks out and remains pinned
to the exact target with complete ancestry and tags, equivalent to
`fetch-depth: 0`, and blocks compilation before producing version facts when
history is shallow, incomplete, or cannot be proved complete.

Compilation failure stops before Execution lookup, coalescing, or admission and
creates no Attempt.

### Live Eligibility Before Execution Lookup

For the named first-slice live Buddy path, Release next performs one
exact-target eligibility evaluation before Product or Execution lookup,
coalescing, concurrency, history admission, or Attempt creation.

The Release-owned evaluator:

- scans the exact target's cataloged dependency manifests, lockfiles, workflows,
  install/bootstrap scripts, package-manager configuration, and other declared
  dependency surfaces for normal consumption of the disposable package;
- admits only explicit policy-bound exceptions such as the package's own
  declaration and the one-time acceptance fixture;
- validates a Governance-approved, non-executable human-inspection attestation
  from the exact immutable source fields carried by the concrete Release policy
  or selected static Governance-policy catalog: repository `hcoona/three`, ref
  `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`;
- verifies those policy fields, verifies that the ref is protected, resolves it
  once to a full commit SHA, and reads the attestation blob at that commit; and
- validates schema, canonical content, policy/package bindings, explicit
  accepted writer inventory, package/repository/Manage Actions access inventory
  or evidence digest, issuer, inspection time, expiry, and acknowledged
  limitations.

The attestation expires no later than 90 days after inspection. It grants no
Capability and contains no executable code. Its payload need not self-reference
Git provenance because eligibility binds source provenance externally.

The evaluator emits an immutable Live Eligibility Decision bound to:

- `live-release`, request identity, `github.run_id`, `github.run_attempt`,
  selected ref, and exact target SHA;
- Repository Model Snapshot ID/digest;
- producer and same-revision control identity;
- consumer-policy and catalog ID/digests;
- exact scanned surfaces, content digests, and admitted exceptions;
- the attestation's required boolean `live_enabled` value and attestation
  repository, exact ref, resolved commit, path, Git blob OID, and canonical
  content SHA-256;
  and
- pass/block result and diagnostics.

The workflow transports the Decision by immutable artifact ID/digest. Only
current-attempt success may proceed, and the eventual Attempt binding and human
summary retain that ID/digest and complete attestation provenance.
CI HK results, Execution history, and prior-run Decisions are not authoritative
inputs.

Missing, unreadable, malformed, expired, provenance-mismatched,
`live_enabled: false`, or consumer-positive state blocks before Attempt
creation. The evaluator uses `contents: read` to freshly verify ref protection,
resolve the fixed ref, and read the document at the resolved commit. Runtime
does not claim current repository-writer or GitHub Packages grant enumeration
because `GITHUB_TOKEN` cannot provide it and Packages has no complete grants
API. Relevant role, grant, or Manage Actions changes require an authorized
human to promptly commit `live_enabled: false` to the protected source and later
update and re-attest before restoring it to true; periodic expiry bounds
normal-flow staleness. Protection, review, merge, and fresh-read latency make
this bounded operational response rather than instantaneous platform
disablement, and a capability job already past its final check may complete.
The design adds no repository variable, PAT, GitHub App, service account, OIDC
permission, continuous reconciler, ledger, or additional token permission.

After live Attempt creation, or after creation of the Simulation Identity,
planning uses that branch's same request-local Snapshot to validate complete
channel policy, policy-selected variants and obligations, compatibility
obligations, and native projection selection. It selects and freezes native
projections from the Snapshot, then derives and validates complete destination
projections and coordinates, Adapter and version bindings, logical operations,
potential action and dependency schema, capability policy, and deterministic
complete mutable-resource-key derivation and enforceability basis. Planning
does not recompute the Repository Model within that run attempt. A replay or
other new `github.run_attempt` compiles a new Snapshot even when request
identity, `github.run_id`, and target remain unchanged.

### Official Product Identity

Official Product Identity consists of:

- channel;
- Release Unit identity; and
- the canonical NBGV version from the request-local Repository Model Snapshot.

It is destination-independent and does not permanently bind one target.
Different targets may share one Official Product Identity.

### Release Execution Identity

Release Execution lookup, coalescing, and Attempt lineage use:

- Official: Official Product Identity plus immutable target; and
- Buddy: channel, Release Unit identity, and immutable target.

Different targets always create different Release Executions. The architecture
does not require a permanent global Official Product Identity-to-target ledger.

Buddy identity ignores the canonical and native NBGV facts already computed in
the request-local Snapshot. It needs no External Package Coordinate, Destination
Adapter, or complete destination projection set.

External package coordinate consists exactly of:

- channel;
- destination identity;
- package identity; and
- the native NBGV product version.

After Attempt creation, planning selects and freezes the required native NBGV
projection from the request-local Snapshot, then derives External Package
Coordinates, the complete deterministic destination projection set, and its
digest. They are Plan and Snapshot bindings, not additional Buddy Release
Execution Identity fields.

For package publication, its external resource address is the External Package
Coordinate. Destination and Capability isolation separate Buddy from Official;
the product-version string need not differ.

A new manual dispatch creates a new Buddy Intent record even when the target is
unchanged. When its complete Release Execution Identity matches an existing
Execution and the request is admitted and was not coalesced, it creates a
distinct new Attempt in that Execution. A pending dispatch replaced or coalesced
before execution is not admitted and creates no Attempt. GitHub **Re-run all
jobs** preserves the existing Intent and creates a replay Attempt. Normal Buddy
publication is atomic create-only or atomic create-or-exact and never overwrites
conflicting bytes.

Release Unit and target are part of Buddy Release Execution Identity. A
different Release Unit or target creates a different Execution even when all
derived coordinates match. Complete Adapter-declared mutable-resource keys
serialize overlapping live actions.

Different Official targets with the same Product Identity likewise create
separate Executions. If their planned live actions overlap, they serialize on
complete Adapter-declared mutable-resource keys. Durable destination state then
determines absent, exact, or conflict.

A Release Intent does not reserve an absent coordinate. Durable destination
creation establishes the observable package binding. A rejected, canceled, or
failed Attempt before mutation burns nothing.

### Channel Version Projection

NBGV remains the canonical product version authority.

- Official Product Identity uses the canonical NBGV version.
- Buddy uses the same frozen canonical product version.
- Buddy and Official ecosystem publication and dry-run use the exact frozen
  native NBGV projection for their ecosystem, including `npmPackageVersion` for
  npm, unchanged.
- Adapters must not append Release Intent, request, workflow, run, or Attempt
  components to the published product version.

Repository Model compilation emits the target-bound canonical NBGV facts and
required native ecosystem projections as authoritative NBGV outputs. The
Qualification Snapshot and each Build Request select and freeze the exact
required projection. A Build Adapter applies and verifies only that value; it
must not recompute NBGV, derive an alternative version, or use fallback version
fields. Buddy and Official use the same target-derived product version while
channel policy selects different complete destination projections and
Capability boundaries.

All artifact variants in one Attempt use one Release Execution Identity and one
snapshot-bound version fact set. Destinations cannot choose independent
versions.

### Release Execution

A Release Execution is the logical business execution for one channel-specific
Release Execution Identity.

It owns an append-only sequence of Release Attempts and may be referenced by
multiple separate Intent records. It is not a permanent database row or a
mutable document.

Different Release Units at the same commit remain separate Release Executions.
Buddy Intents refer to the same Release Execution when they name the same
channel, Release Unit, and target. Each admitted, non-coalesced request creates
a distinct Attempt in that Execution. Another target or Release Unit creates a
different Release Execution Identity and Execution even when every derived
coordinate is the same. Official requests join only when Product Identity and
target both match.

### Release Attempt

A Release Attempt is one coherent:

- planning;
- build;
- qualification;
- observation;
- authorization;
- publication; and
- finalization pass.

Attempt identity consists of:

- Release Execution Identity;
- `github.run_id`; and
- `github.run_attempt`.

The Attempt also has required immutable bindings to the originating Release
Intent, request identity, current-attempt Live Eligibility Decision ID/digest,
complete Governance attestation source provenance, and the pre-Attempt Execution
History Admission Snapshot ID/digest. Those bindings are not Attempt Identity
components. Every whole-release replay creates a new Attempt.
Historical records explain prior Execution state but never become current
authority. An Attempt never adopts a Repository Model, Live Eligibility
Decision, Qualification or Publication Snapshot, build outputs, Evidence,
approval, observations, Receipts, or outcomes from another workflow attempt.

### Dry-Run Simulation

Dry-run is a separate, non-authoritative simulation execution.

It branches before live lineage eligibility, Product or Execution Identity
lookup, coalescing, admission, or Attempt creation. Its separately namespaced,
request-scoped Simulation Identity consists of:

- the `release-simulation` namespace;
- request identity;
- `github.run_id`; and
- `github.run_attempt`.

It has immutable target, channel, and Release Unit bindings but no live Product,
Release Execution, or Attempt identity.

The simulation-purpose Repository Model Snapshot is compiled first and binds
strict simulation purpose, request identity, `github.run_id`,
`github.run_attempt`, target, channel, Release Unit, canonical and native
version facts, producer, and control identity. After those bindings validate,
the Planner derives the separately namespaced Simulation Identity from them.
Later simulation planning snapshots bind both that Identity and the Repository
Model Snapshot digest. They are the sole authoritative inputs for that
simulation pass. A later live request or replay cannot use them as live
request-local identity or planning input.

For a Buddy simulation, the Planner uses the same target-derived native NBGV
version and deterministic destination projections that live Buddy would use.

It uses the selected channel's:

- Release Unit variant set;
- quality policy;
- version projection;
- destination projections;
- observation rules; and
- action planning, including the same Adapter-declared mutable-resource keys.

It builds, qualifies, observes, and may emit purpose-discriminated hypothetical
requirements, actions, and a Simulation Outcome, but it:

- obtains no approval;
- creates no Authorization Record;
- obtains no publication capability;
- performs no external attestation or destination mutation;
- creates no Receipt;
- does not reserve Official or Buddy live identity; and
- does not enter live Release Execution history.

Shared schemas may be reused only when an explicit purpose discriminator is
covered by identity and digest. Cross-purpose admission rejects the record.
Live Release never reuses simulation Snapshots, artifacts, Evidence,
observations, hypothetical actions, or outcomes.

## Release Unit Policy

### Complete Artifact Variant Set

Artifact variants belong to the Release Unit, not to Buddy or Official.

Every live Attempt or simulation pass builds and qualifies every publishable
variant declared by the Release Unit. The caller and channel cannot select a
subset.

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
- destination mutability constraints; and
- complete deterministic mutable-resource keys for every mutating action.

Each mutating action declares its complete resource-key set. Package actions
include the exact External Package Coordinate plus any additional
Adapter-required keys. Non-package destinations define their exact resource
identities through the Adapter contract. A missing, unknown, incomplete, or
conflicting required key blocks live publication.

When the execution platform supports equality concurrency groups rather than
arbitrary set-overlap locking, the Destination Adapter also defines a
deterministic conservative serialization projection. It may over-serialize, but
it must map every pair of overlapping complete key sets to the same enforced
group. The projection and resulting group are additional action bindings; they
never replace the complete frozen resource-key set.

For a live registry destination, the Adapter contract must establish:

- atomic non-overwriting version creation;
- create-only behavior, or atomic create-or-exact behavior that creates when
  absent, accepts concurrently established exact state without mutation, and
  rejects differing state without mutation;
- sufficiently durable observation of exact package identity, ownership,
  immutable in-package target witness, target binding, and bytes or digest; and
- deterministic conflict classification after another contender creates the
  coordinate.

Acceptance tests must prove these guarantees against the destination, including
concurrent exact and differing-state outcomes for create-or-exact where
supported. If GitHub Packages cannot provide them, Buddy publication to GitHub
Packages is unsupported or blocked. Release does not emulate the missing
guarantee with a tag witness, binding index, application-level lock, or
permanent ledger.

They do not decide whether a Release Unit publishes to that destination.

Shared Foundation provides only generic artifact, digest, invocation,
capability-requirement, GitHub, and registry client primitives. Those clients do
not classify projections, plan actions, define Receipts, choose recovery, or
define Destination Adapter mutable-resource keys.

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
  -> branch by purpose before live eligibility or identity lookup
       live release:
         -> compile exactly one live-purpose request-local Repository Model Snapshot
         -> validate channel, Release Unit, and target lineage eligibility
         -> derive Product and Release Execution identities
         -> coalesce/admit, create or join Execution, create Attempt
         -> reuse the same Snapshot throughout the Attempt
         -> plan and seal Qualification Snapshot
         -> build, qualify, and observe
         -> materialize actions and seal Publication Snapshot
         -> authorize, acquire capability, mutate, persist Receipts, finalize
       release simulation:
         -> compile exactly one simulation-purpose request-local Snapshot
         -> create separately namespaced request-scoped Simulation Identity
         -> reuse the same Snapshot throughout the simulation pass
         -> simulate planning, build, qualification, observation,
            requirements, and hypothetical actions
         -> emit Simulation Outcome
         -> no live identity, admission, Attempt, authorization, capability,
            Receipt, or mutation
```

## Eligibility and Authority

### Identity and Lineage Eligibility

After the purpose branch, and before live Execution lookup, coalescing, and
admission, the live candidate compiles its own same-revision Repository Model
Snapshot and validates the
complete technical model and the inputs and facts needed to derive Product and
Execution identities and establish target eligibility:

- the request-local Snapshot and every admitted Fact Bundle are bound to the
  current request identity, `github.run_id`, `github.run_attempt`, target,
  producer, and control identity;
- descriptors are valid and Release Unit identity exists;
- Project Nodes, dependency graph, and Build Definitions are closed;
- modeled variants, outputs, build scope, and artifact scope are complete;
- canonical and required native NBGV facts, including `npmPackageVersion` where
  required, resolve for the target;
- selected ref and pinned target agree;
- Official live target belongs to an authoritative Git lineage; and
- for the named first-slice Buddy destination, the selected target is any ref in
  the same repository.

The Snapshot is not imported from CI, simulation, another request, or a prior
Attempt. A prior-run-attempt Fact Bundle or Snapshot is rejected even when
request identity, `github.run_id`, and target match. Any failure stops before
Execution lookup, coalescing, or admission and creates no Attempt. Buddy
Execution Identity ignores the Snapshot's version facts and does not use
coordinates, destination projections, or Adapter facts.

After admission creates an Attempt, Attempt planning uses the same request-local
Repository Model Snapshot without recompiling it. Planning compiles the complete
channel policy and validates policy-selected obligations and variants,
compatibility obligations, and required native projection selection. It selects
and freezes native projections from the Snapshot rather than deriving or
recomputing them, then derives and validates complete destination projections
and coordinates, Adapter and version bindings, logical operations, potential
action and dependency schema, capability policy, and deterministic complete
mutable-resource-key derivation and enforceability basis. Missing or invalid
required state blocks that Attempt before snapshot execution.

The Planner does not query reviews or re-adjudicate Rulesets.

A structurally valid target is not thereby authorized to publish.
For the first-slice Buddy exception, no protected-ref or CODEOWNERS-approved
eligibility is required; the later dedicated Buddy Environment approval is the
explicit trust elevation.

### Live Authority

Live authority is granted only after a successful Qualification Decision,
complete observation, and sealed Publication Snapshot.

Delivery Governance grants:

- one channel-level human approval for the exact Publication Snapshot; and
- destination-specific capabilities just in time for the matching side-effect
  groups.

No qualification or observation job has publication capability.

### First-Slice Buddy Authority Exception

The exception is limited to live Buddy publication of the dedicated disposable
`hcoona-release-smoke-npm` package to its isolated GitHub Packages destination.
This bounded risk decision was reopened and reconfirmed before LLD on
2026-08-06.

- workflow, Planner, Finalizer, Providers, Adapters, compiler, authenticated
  clients, static catalogs, capability declarations, and publisher code come
  from the exact selected target revision without owner-reviewed eligibility;
- control is not substituted from protected main;
- every Attempt seals the exact Publication Snapshot before requesting approval
  through the dedicated protected Buddy Environment;
- the normal reusable live path keeps workflow-level permissions empty or
  read-only and declares `packages: write` only on the `uses`-only caller job as
  the reusable-workflow ceiling and on the called Environment-referencing
  publisher job as effective capability;
- all other jobs explicitly remain least-privilege, cannot inherit package
  write by omission, and the called workflow cannot elevate beyond the
  caller-job ceiling;
- after approval and successful credential-free Capability Admission, the
  target-revision side-effect job receives short-lived `GITHUB_TOKEN` with
  minimum `packages: write`;
- the job receives no PAT fallback and no `id-token: write`; and
- the reviewer sees target SHA and ref, exact package coordinate, artifact
  digest and manifest, package lifecycle scripts, and exact action summary.

Self-review prevention is required where available. Approval is an explicit
workflow and Governance control against mistakes, accidental publication, and
ordinary process violations, not cryptographic or independent semantic
validation and not a non-bypassable permission ceiling against a malicious
repository writer. The architecture does not claim that a protected independent
publisher constrains malicious target control code after approval.

Every repository actor with Write, Maintain, or Admin access is inside the Buddy
trusted publisher TCB for this slice. External/fork contributors and actors
without repository write are outside it and cannot manually dispatch the live
path under normal GitHub permissions. A trusted writer can create alternate
workflow YAML or jobs with `packages: write`. Optional workflow-execution
protections may reduce who can run workflows, but are not required and are not
per-job permission ceilings.

The accepted risk includes arbitrary or malicious bytes, reachable package-name
or version squatting, registry clutter or cost, and abuse of package operations
within the token's repository/package permissions. Inspection and safe probes
must deny Official capability and known Official/production asset reach; no
universal negative reach proof is claimed. Other reachable package operations
under the smallest configured grants remain accepted writer-TCB risk. The smoke
package has no normal developer, CI, or production consumer. Planned and
ordinary delete, restore, permission, visibility, and admin actions are forbidden;
deletion and restore require Break-Glass handling. Latent repository/package
admin authority held by trusted writers remains accepted misuse risk. If any
repository Write/Maintain/Admin actor ceases to be trusted to publish, the slice
blocks until either that actor's repository access is reduced below
Write/Maintain/Admin or package-write Capability and destination access are
placed behind an independently enforced publisher boundary unavailable to
writer-authored workflows. Ref narrowing, Environment branch restrictions,
CODEOWNERS, and workflow-execution protections may remain defense in depth but
are insufficient remediation by themselves while an untrusted writer can
author alternate workflows with `packages: write`. Future Buddy destinations
and production packages require independent threat and cost decisions and do
not inherit this exception.

Human Governance re-attests the writer TCB and
package/repository/Manage Actions access at least every 90 days and after
relevant role, team, or permission changes. Operators immediately disable v3
live pending reacceptance, while attestation expiry independently bounds stale
normal flows. A
permanent repository-wide HK policy scans dependency manifests, lockfiles,
workflows, install scripts, and dependency configuration for normal consumption
of the disposable package; dependency-surface changes and `slice-validation`
run it, and any consumer blocks live use and reopens the exception.

The implementation PR merge is the direct repository-wide v1 Buddy-to-v3 Buddy
cutover and preserves no compatibility route. v3 lands with the protected
attestation's `live_enabled` field false, and the merge removes both legacy
Buddy workflow files. Governance freezes dispatch, disables both `buddy.yml`
and `release-buddy.yml`, cancels or drains queued, waiting, approval-pending,
and running executions, and verifies disabled state, removal, and old-ref
dispatch rejection before destination acceptance. A check present only in new
selected-ref YAML is insufficient because old refs retain old publisher code.
After inspection proves all legacy Buddy routes retired, a temporary protected
Governance acceptance workflow may probe fixed acceptance-only coordinates in
the same disposable package. It uses a non-Release purpose, exact approved
target SHA, explicit confirmation, dedicated reviewer Environment, and
package-write permission only in probe jobs. It accepts no normal Release
inputs and emits no Product, Execution, Attempt, Authorization, Receipt, or live
Release history. Every probe independently requires
`github.run_attempt == 1`. Terminal evidence capture uses
`always() && github.run_attempt == 1` or an exact equivalent so first-attempt
dependency failures, skipped or canceled probes, and ambiguous mutation
evidence are retained and incomplete or unknown state enters reconciliation.
The evidence job still rejects non-first attempts. Governance then removes the
workflow, bypass, and Environment and verifies removal. Only successful
acceptance with no unreconciled incomplete or unknown state permits an
authorized protected commit to set `live_enabled` true. Failed acceptance
leaves all Buddy publication disabled and the temporary path removed while
legacy Buddy remains retired. Former Buddy projects are unsupported until
explicitly migrated. Restoring legacy Buddy requires a separate user-approved
rollback PR. v1 Official and CI assets remain unchanged; legacy Buddy workflows,
Buddy-specific tests and matrices, and Buddy documentation are excluded from
that preservation and are retired or rewritten. An intentional brief Buddy
outage is expected.

## Release Plan Lineage

Each live Attempt has one logical Plan lineage with two sealed snapshots.

Attempt planning consumes the same request-local Repository Model Snapshot used
for identity and eligibility. Without recompiling that model, it validates
channel-selected variants, quality and compatibility obligations, destination
projections and coordinates, Adapter and version bindings, logical operations,
potential action and dependency schema, capability policy, and deterministic
complete mutable-resource-key derivation and enforceability basis. It selects
and freezes authoritative native NBGV projections from the Snapshot before
deriving External Package Coordinates and other destination identities. It does
not freeze actual mutation actions or actual action key sets.

### Qualification Snapshot

The Qualification Snapshot freezes:

- Release Execution Identity and Attempt identity;
- request-local Repository Model Snapshot identity and digest;
- Release Unit, target, channel, and the selected authoritative NBGV projection;
- complete Project Node and declared-input closure;
- every publishable artifact variant;
- complete Build Definitions and Build Requests;
- every required Release quality obligation;
- concrete targets, dimensions, runner constraints, and prerequisite DAG;
- complete destination projections and coordinates;
- Adapter and version bindings;
- logical operations and potential action and dependency schema;
- capability policy;
- deterministic complete mutable-resource-key derivation and enforceability
  basis;
- internal provenance requirements;
- external provenance projection requirements; and
- policy and definition digests.

The snapshot authorizes only unprivileged build and qualification work.
It declares Capability requirements but cannot request, approve, or create any
live Capability.

### Publication Snapshot

After successful qualification and complete destination observation, the
Publication Snapshot references the Qualification Snapshot digest and adds:

- successful Qualification Decision;
- actual artifact IDs, content digests, and internal provenance;
- immutable artifact transport IDs;
- snapshot-bound desired state for every logical projection, derived from the
  Qualification Snapshot and admitted qualified artifacts;
- one Observation Record per logical projection;
- exact desired destination identities;
- exact materialized Publication Action DAG, prerequisites, and inputs;
- the complete mutable-resource key set for every mutating action;
- any Adapter-declared conservative platform serialization projection and
  resulting group for every mutating action;
- capability-group assignment;
- expected Receipt contracts;
- channel-level approval requirement; and
- destination-specific capability requirements.

It cannot change:

- target;
- Product or Release Execution Identity;
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

The first slice includes distinct `node/npm-artifact-contents-v1` and
`node/npm-install-import-v1` obligations for live Buddy and Official
simulation. Both depend on the built tarball. They may batch in one physical job
but retain separate obligation identities and emit separate Evidence; the
Qualification Finalizer requires both.

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

- Release Execution Identity and target commit;
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

Classification compares observed remote state atomically with snapshot-bound
desired projection state, not Product or Execution Identity. Desired state
derives from the Qualification Snapshot and admitted qualified artifacts and
includes the exact destination coordinate, expected ownership, immutable
in-package target witness, target binding, qualified artifact bytes or digest,
and required routing projections. For first-slice npm, it includes the exact
dist-tag `buddy-sha-<40-lowercase-target-sha>` mapped to the frozen native
version. The tag is routing, not provenance. The Publication Snapshot seals that
desired state with the Observation Record.

For npm, the qualified tarball contains canonical
`workflow-delivery/provenance.json`. The witness is inside the package and
covered by its artifact digest. It binds target commit, Release Unit,
canonical/native NBGV facts, Build Definition, catalog/control digests, purpose,
and schema, but excludes run and Attempt IDs. Build/pack and tarball
qualification validate it. Observation downloads and hashes remote bytes,
extracts the witness, and validates every field. A local sidecar cannot prove
remote target provenance. Matching package/version claims with a different
target witness are conflicting.

The Observation Record binds the Release Attempt, logical projection, immutable
desired-state basis, and canonical remote response and observed facts, including
observed artifact digests. It cannot bind the future Publication Snapshot. That
later Snapshot seals admitted Observation Records with resulting desired state
and materialized actions. Any mismatch with the current Attempt's desired
projection state is conflicting and stops ordinary publication.

For example, a GitHub Release object with one expected asset missing is
`partial`, even when the missing upload could be described as an individual
action.

Only `absent` and `exact-satisfied` allow a ready Publication Snapshot:

- `absent` produces publication actions; and
- `exact-satisfied` produces no side effect.

`absent` remains valid when no operational lineage for the coordinate is
retained. A coordinate is an external address, not a reservation, so absence
does not require an earlier Intent, Attempt, tag, or index.

For first-slice npm, `exact-satisfied` requires the complete destination
coordinate, expected package ownership, target binding, exact artifact bytes or
digest, and exact required dist-tag-to-version mapping. A missing tag is
`partial`; a mismatched tag is `conflicting`; unreadable tag state is `unknown`
or `unprovable`. A different target, differing bytes, or conflicting ownership
is `conflicting`; version existence alone is not exact proof.

All other classifications block ordinary publication and require
reconciliation.

### Single Governed Writer Assumption

The initial design assumes Delivery Governance is the only normal
repository-controlled writer for managed Release identities.

The system uses Adapter-declared mutable-resource-key serialization to prevent
repository-controlled contenders from racing. Package actions include the
exact External Package Coordinate plus any additional Adapter-required keys.
The coordinate itself excludes Release Unit and target. The destination must
additionally provide atomic non-overwriting version creation. The system does
not add a second observation immediately before mutation.

An out-of-band mutation between observation and publication is an accepted
residual operational risk.

Normal Adapters still:

- use atomic create-only or create-or-exact behavior;
- never destructively overwrite;
- never implement create-or-exact as read-then-upsert, overwrite, or
  delete-and-recreate;
- accept concurrently created exact state without mutation only when the
  destination operation provides that atomic result;
- treat destination conflict or API failure as action failure; and
- defer reclassification to the next whole-release Attempt.

A pure create-only destination may report conflict when another writer creates
the resource after observation. That Attempt fails without overwrite and relies
on whole-release replay to observe and classify the resulting state.

## Publication Planning

### Projection and Action Granularity

Each absent logical projection expands through its Destination Adapter into a
closed Publication Action DAG.

Registry publish actions use the Adapter's atomic non-overwriting create
operation. Successful durable creation establishes observable package state.
For create-or-exact, the mutation linearization point either creates absent
state, accepts already exact state without mutation, or rejects differing state
without mutation. Pre-observed exact state never creates an action. Pure
create-only conflict relies on replay. Failure before mutation establishes no
reservation or binding.

Each action binds:

- action ID;
- logical projection;
- destination identity;
- operation;
- exact artifact or metadata input;
- prerequisite action IDs;
- complete deterministic mutable-resource keys;
- conservative platform serialization projection and resulting group when
  required by the Adapter;
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
- current-attempt Live Eligibility Decision and its complete fixed-source
  Governance provenance;
- reviewer-summary artifact ID, URL, and digest;
- action manifests;
- artifact IDs and expected digests;
- capability-group manifests; and
- expected Receipt contracts.

Variant artifacts remain separate immutable Actions artifacts. Each active
capability group downloads the control bundle once and only the artifact IDs it
needs.

Artifact names are deterministic and unique across the complete producing
workflow run, with overwrite disabled. Every physical name includes
`github.run_attempt` directly or through the deterministic hash preimage. Every
producer captures artifact ID, digest, and URL. Consumers select only explicit
artifact IDs and verify name metadata, producer, `github.run_id`,
`github.run_attempt`, and digest. Prior-attempt IDs, name fallback, and
"latest" selection are invalid.

## Authorization

### Channel-Level Approval

One approval job represents the human decision for the complete Publication
Snapshot.

The job:

- depends on the sealed Snapshot;
- binds a Buddy or Official approval Environment;
- has no publication credentials or `id-token: write`;
- cannot start until GitHub Environment protection passes; and
- emits an Authorization Record only after successful approval.

The Authorization Record binds:

- Publication Snapshot digest;
- reviewer-summary artifact ID and digest;
- Release Execution and Attempt identity;
- GitHub workflow run and attempt;
- approval job identity;
- channel; and
- approval completion time.

GitHub natively approves the exact job/run. The system-created record binds that
approved job to the exact Snapshot digest. The design does not claim that
GitHub produces a cryptographic signature over an arbitrary digest.

Reviewer visibility of the canonical Snapshot JSON and deterministic
digest-bound summary through the deployment URL and completed job summary is an
LLD acceptance requirement. Authorization admission fails closed if the
reviewer-summary artifact ID or digest does not match the Snapshot-bound
authorization inputs.

Terminal denial Evidence is admissible only where a platform supplies a
documented append-only record with exact current run attempt, approval job,
Snapshot, channel, and terminal-result bindings. GitHub Environment
`DeploymentReview` lacks authoritative `run_attempt` and job binding, and no
documented append-only/consistency contract makes review-ID delta inference
safe. The first slice therefore admits no Approval Outcome Evidence for
Environment rejection or denial.

Rejection is unknown approval-contract failure and leaves a replayable
incomplete Attempt. Observable review data may be retained only as a
non-authoritative human diagnostic. No capability group starts. Workflow
Delivery adds no approval watchdog. GitHub cancellation or platform expiry
while approval remains pending
may terminate the run before a separate Approval Outcome Evidence record or
Finalizer outcome exists. If no capability group started, the platform run/job
conclusion is sufficient no-side-effect terminal evidence; the Attempt is
replayable and incomplete, not successful. The architecture does not require
distinguishing manual cancellation from platform expiry unless GitHub exposes
it. If any capability job may have started, cancellation is not no-side-effect
proof; the Attempt remains incomplete and possibly mutated, and the next
Attempt reobserves. When finalization runs and neither valid authorization nor
an applicable admissible terminal result exists, it records approval-contract
failure.

### Destination Capabilities

After successful channel approval, a credential-free Capability Admission Gate:

- validates the Authorization Record;
- validates the Publication Snapshot and reviewer-summary artifact;
- validates every planned action, artifact, complete resource-key set, and
  capability-group manifest;
- uses `contents: read` immediately before admission to freshly resolve the
  policy-fixed protected ref and read the Governance attestation document;
- revalidates the exact repository/ref/path contract, protected-ref status,
  schema, canonical content, policy/package bindings, current expiry,
  `live_enabled: true`, and commit/blob/content provenance and content identity
  against the current-attempt Live Eligibility Decision;
- has no destination credential or package-write permission; and
- emits an admitted group decision only on exact success.

A false `live_enabled` value, expiry, source/provenance/content mismatch,
binding change, or other invalidation blocks publication. A new valid
attestation or later re-enablement does not revive the current Attempt; Release
must start a new Attempt and repeat eligibility through approval. Only that
decision may schedule or start the credential-bearing capability job. The
first-slice LLD uses the existing `approval-finalizer` for this gate.

Each admitted active capability group then:

- may revalidate the Authorization Record, Snapshot digest, and the same
  `contents: read` fixed-source Governance check immediately before mutation as
  defense in depth;
- binds its exact action ID set;
- enters its destination-specific Environment;
- receives only required GitHub permissions;
- requests OIDC or another destination capability just in time; and
- cannot execute actions from another group.

The normal v3 live path may request destination Capability only in this
admitted side-effect capability group. Qualification, planning, approval, and
capability-admission jobs never request live Capability. Publisher-side repeat
validation, when used, adds no new credential or service and is not an independent
malicious-writer boundary.

Destination policy may impose an additional approval. The channel-level
Authorization Record does not bypass destination Governance.

The channel approval job and destination capability job remain separate. The
first-slice publisher Environment need not repeat the human review already
performed by the approval Environment; it gates capability delivery after the
credential-free admission decision. A second destination reviewer is optional
policy, not a required second approval.

GitHub and destination platforms may scope credentials only to an Environment,
workflow identity, repository, package, or destination account. The trusted
group executor, rather than the credential format alone, enforces the exact
Snapshot, artifact digest, action ID, and Attempt bindings.

Independent trusted executors enforce the exact bindings for normal
destinations. The first-slice Buddy group is the bounded exception: its
target-revision publisher validates the Authorization Record and bindings by
contract, but is not an independent adversarial boundary and trusted repository
writers can disregard or bypass that contract. The dedicated Environment,
reviewer-visible inputs, disposable package, and minimum short-lived
`GITHUB_TOKEN` scope govern the normal flow; they do not impose a ceiling on a
malicious writer. That group uses `packages: write` without PAT or
`id-token: write`.

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

Each active capability group emits exactly one result bundle containing:

- Release Attempt, Publication Snapshot, `github.run_id`, and
  `github.run_attempt` bindings;
- capability-group ID;
- every planned action ID in the group;
- per-action outcome, destination response identity, separately persisted
  Receipt reference when mutated, and diagnostic reference;
- group completion state; and
- producer and same-revision control identity.

A planned active group with no admissible result bundle is incomplete. Missing,
duplicate, mismatched, or extra action coverage blocks finalization. Even a
one-action active group must emit exactly one group result bundle covering that
action.

## Release Finalization

The Release Finalizer:

- validates the Qualification and Publication Snapshot lineage;
- conditionally admits the Authorization Record after successful approval;
- otherwise admits terminal Approval Outcome Evidence only when the configured
  platform supplies documented exact current-attempt proof; the first-slice
  GitHub path supplies none;
- admits Capability Admission Decisions for every active group;
- admits Observation Records;
- admits capability-group result bundles and Receipts;
- verifies artifact, action, destination, mutable-resource-key, run, attempt,
  and digest bindings;
- computes Attempt Outcome; and
- derives the current Release Execution state.

A valid Authorization Record plus successful Capability Admission Decision
permits the matching authorized execution. Admissible terminal Approval Outcome
Evidence produces a governed failed Attempt on supporting platforms. If neither
applicable approval result exists, the Finalizer records unknown approval state
and contract failure when it runs.
Platform cancellation or expiry may prevent that Finalizer from running. An
approval-pending conclusion when no capability group started is sufficient
no-side-effect evidence but leaves the Attempt incomplete and replayable;
cancellation after capability may have started leaves it incomplete and
possibly mutated. The Finalizer never
infers authorization or grants Capability from a job conclusion or failure
Evidence.

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
- a valid Authorization Record is admitted;
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

- compiles a new same-revision, request-local Repository Model Snapshot bound
  to the new `github.run_attempt`, even though request identity,
  `github.run_id`, and target remain unchanged;
- creates a new Attempt;
- preserves the same live Product and Release Execution identities;
- reruns planning from the new request-local Repository Model Snapshot;
- creates a new Attempt-specific Qualification Snapshot;
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

A separate admitted, non-coalesced manual request for the same Release Execution
Identity is not a replay of an earlier request, but it follows the same
independence rules: it compiles its own request-local Repository Model Snapshot,
creates a new Attempt, and does not consume an earlier Repository Model,
Qualification, or Publication Snapshot, Plan, Evidence, approval, Observation,
Receipt, or outcome as an authoritative input. Within that request, Attempt
planning uses the newly compiled Snapshot without recompiling it. A replaced or
coalesced pending dispatch is not admitted and creates no Attempt.

If rebuilt artifact bytes differ from an exact remote artifact for the same
desired projection state, observation is conflicting. The system does not
overwrite the remote artifact.

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
- original Publication Action identity and its complete frozen
  Adapter-declared mutable-resource key set;
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
- original Publication Action and exact complete frozen mutable-resource key
  bindings;
- successful Qualification Decision;
- Reconciliation Record and logical projection;
- qualified artifact transport identities and content digests; and
- cross-revision contract kind, version, and payload digest.

An internally consistent but untrusted or unrelated Actions artifact is not an
admissible remediation request.

Remediation never derives or recomputes resource keys from Product or Execution
Identity, current destination state, or current Adapter defaults.

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
6. The write job reuses exactly the complete frozen Adapter-declared
   mutable-resource key set from the original action, without deriving or
   recomputing keys from Product or Execution Identity, verifies the
   GitHub-computed asset digest, and persists a Remediation Record with before
   and after state.
7. A later normal Release replay proves every projection exact and completes
   the Release.

If any expected state changed after preflight or the asset now exists,
remediation fails without overwrite.

## GitHub Execution Serialization

The design uses GitHub Actions concurrency as a platform execution guard, not a
distributed lock or source of Release correctness.

Release Execution lineage and duplicate request coalescing use complete Release
Execution Identity. Official uses Product Identity plus immutable target. Buddy
uses channel, Release Unit, and immutable target. These workflow keys derive
from Release Execution Identity.

Each candidate compiles its request-local Repository Model Snapshot before
entering execution concurrency. The concurrency-scoped caller job then invokes
one same-revision reusable live-Attempt workflow containing admission through
finalization. The caller holds the Release Execution identity slot for the
entire admitted Attempt; concurrency is not released after admission or
planning.

For the first slice, the caller workflow has no workflow-wide package write.
The `run-live-attempt` caller job alone declares an explicit ceiling of
`contents: read`, `actions: read`, and `packages: write` so the called publisher
can receive its minimum capability. The caller job is `uses`-only, has no steps,
and does not use a token. In the caller, `evaluate-live-eligibility` declares
only `contents: read`. In the called workflow, only the history-admission job
declares effective `actions: read`, the observer alone declares
`packages: read`, and the Environment-referencing publisher alone declares
effective `packages: write`. Other jobs declare only their exact minimum
permissions and cannot receive Actions-history or package permission by
omission. Unspecified permissions are none, and a called workflow cannot
elevate beyond the caller-job ceiling.

### Execution History Admission

Inside the whole-Execution concurrency slot, live `admit` discovers history
before creating the current Attempt binding. With `actions: read`, it fully
paginates retained runs for the exact caller and reusable workflow identities,
then fully paginates each candidate run's artifacts and jobs. Artifact names may not select candidates; enumeration, downloads, and admission
use immutable artifact IDs. The current run ID is not categorically excluded
from history. Artifacts from an earlier run attempt of that run may be admitted
as history-only diagnostics when the artifact/run facts and separately queried
existence of that prior run attempt validate.

The trusted caller selects one admission mode; record payloads cannot request
or override it:

- `current-authority` requires exact current live purpose, request,
  `github.run_id`, `github.run_attempt`, Attempt, target, producer, control,
  artifact ID, and digest bindings and rejects every prior attempt; or
- `execution-history` is valid only in this pre-Attempt `admit` phase. The
  source may be a different workflow run or an earlier run attempt of the
  current run. It must correlate to the same Release Execution Identity, live
  purpose, and target asserted by the payload.
  Authoritative platform attribution independently binds only artifact ID and
  digest, workflow run ID, head SHA, payload integrity, and metadata actually
  exposed by Actions artifact/run APIs.

The admit command validates and records:

- Release Execution Identity;
- source workflow run database ID and node ID, `head_sha`, conclusion, and
  timestamps exposed by the Run API;
- artifact ID, name metadata, digest, URL, source workflow run ID, and platform
  metadata exposed by the Artifact API;
- historical Attempt binding and outcome when retained;
- separately queried Jobs/Run API facts for run attempt, job, conclusion, and
  capability phase state when a context-owned outcome is absent; and
- schema, payload integrity, self-asserted purpose/control/producer claims, and
  canonical admission digests.

Producer job, exact `run_attempt`, reusable-workflow path/control, and similar
claims inside historical payloads are diagnostic self-assertions, not
authoritative artifact provenance. They may be compared with separately queried
platform facts for explanation but cannot strengthen history into current
authority. Same-run history additionally requires separately queried proof that
the claimed earlier run attempt exists; this still does not establish an
artifact-to-attempt or artifact-to-job edge. If future correctness requires
strict historical workflow/attempt provenance, history admission is unsupported
until Artifact Attestations or an OIDC-backed mechanism is separately approved.
This slice adds no `id-token: write`.

An immutable Execution History Admission Snapshot binds the current request,
run, run attempt, Execution Identity, exhaustive pagination/query basis, sorted
admitted artifact IDs/digests and source workflow run/head-SHA facts, separately
queried job/run phase facts, and an explicit history-only authority marker.
Finalization and human explanation consume that Snapshot. Historical Evidence,
authorization, artifacts, Receipts, and outcomes are never admitted into the
current Attempt as authority.

REST or GraphQL denial, rate-limit truncation, incomplete pagination, malformed
or duplicate records, digest mismatch, prior-purpose content, and conflicting or
cross-Execution bindings fail admission before current Attempt creation. The
implementation follows pagination links/cursors to exhaustion and records the
page/cursor basis; a fixed page cap is not a complete search.

An artifact explicitly marked expired, or a run older than the configured
retention window with no retained binding artifact, is recorded as unavailable
history rather than malformed current Evidence. A recent run missing an expected
non-expired binding remains an admission failure. History expiry does not create
a permanent-ledger requirement. After retained records disappear, current
destination observation may proceed when every projection is provably absent or
exact. Partial, conflicting, unknown, or unprovable state requires
reconciliation. No service, tag witness, binding index, or permanent Release
ledger is added.

For one such identity:

- `cancel-in-progress` is false;
- the currently running Attempt is not canceled;
- `queue: single` retains only the latest pending duplicate request; and
- a pending run canceled before execution does not become a Release Attempt.

Every request that survives coalescing and is admitted creates one distinct
Attempt. A replaced pending dispatch is not admitted and creates no Attempt.
A superseded pending caller never invokes the reusable workflow.

Every live Destination Adapter declares the complete deterministic
mutable-resource key set for every mutating action. Publication Snapshots and
action manifests bind those keys. Live actions may execute concurrently only
when their key sets do not overlap; any overlap serializes. Live-action
resource keys derive independently from Product or Execution Identity.

GitHub concurrency compares one group value for equality; it does not acquire a
set of locks or serialize arbitrary intersecting key sets. An Adapter using this
primitive must therefore provide a deterministic conservative serialization
projection whose equality guarantees that every overlapping complete key set
serializes. The projection may merge non-overlapping actions and reduce
parallelism, but it must not permit an overlap. Publication Snapshots, action
manifests, Receipts, and admission preserve and validate both the complete
frozen key set and the enforced projection/group. An Adapter whose declared key
shape has no safe enforceable projection is unsupported for live mutation.

For package actions, the key set includes the exact External Package Coordinate:
channel, destination, package, and version, plus any additional
Adapter-required keys. The coordinate excludes Release Unit and target, so
distinct Buddy or Official Releases are not coalesced but cannot race for the
package resource. At mutation linearization, atomic create-or-exact may accept a
concurrently established exact state without mutation; differing state fails
without mutation. Pure create-only conflict relies on replay. Later contenders
observe exact state only for the same desired projection state, otherwise
conflict.

Non-package Destination Adapters define their exact mutable-resource keys
through their contracts. Missing, unknown, incomplete, or conflicting required
keys block live publication. Remediation reuses exactly the complete frozen
Adapter-declared keys from the original actions and never derives or recomputes
them from Product or Execution Identity. The architecture does not introduce an
external lock, queue service, tag witness, global Product Identity-to-target
binding index, or permanent Release ledger.

The first-slice npm Adapter defines publication as one compound version-and-tag
action. Its complete key set contains both the External Package Coordinate and
the destination/package/`buddy-sha-<40-lowercase-target-sha>` mutable resource.
The command explicitly supplies that tag; implicit `latest` and shared moving
Buddy tags are forbidden.

Its GitHub serialization projection contains only the physical destination ID
and normalized npm package name. Every action touching that destination/package
therefore receives the same equality group, including actions with different
versions or target-derived tags. This intentionally over-serializes the first
slice so GitHub's equality-only primitive enforces the complete-set overlap
requirement. The complete coordinate-plus-tag key set remains the authoritative
resource identity and is frozen in the Publication Snapshot, action binding,
Receipt, remediation input, and validation. The Receipt also records the
enforced projection/group, version creation or exact-race result, and resulting
tag mapping. Normal flow provides no separate dist-tag mutation. If the version
is exact but the tag is absent or mismatched, reconciliation is required.

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
- the Authorization Record after successful approval, or Approval Outcome
  Evidence only for a future platform contract with exact attempt-bound denial
  proof;
- Capability Admission Decisions;
- Observations;
- capability-group result bundles;
- Receipts; and
- outcomes.

For the first slice, Release control bundles, artifacts, records, and outcomes
use 45-day retention, exceeding GitHub's Environment gate-expiry window,
currently up to 30 days. Activation is blocked if repository policy cannot
provide that supported margin. The approval interval does not freeze the
pre-Attempt eligibility result or extend the at-most-90-day attestation:
capability admission must observe live still enabled and the same attestation
provenance/content still valid and unexpired.

If a Finalizer runs after the approval job completed but neither a valid
Authorization Record nor applicable admissible terminal Approval Outcome
Evidence is retained, approval state is unknown and the outcome is
approval-contract failure. Platform cancellation or expiry may leave no
context-owned final outcome; the retained platform conclusion and phase state
govern replay and reobservation.

Longer-lived identity and state rely on:

- Git commits and refs;
- NBGV version identity;
- package and registry records;
- GitHub tags and Releases when selected; and
- external attestations when selected.

Separate Intent records may identify who requested each Attempt, but the
durable external package state remains observable through each External Package
Coordinate and its registry state. Those resources do not replace Buddy
Release Execution Identity.

The architecture does not retain a permanent global mapping from Official
Product Identity to one target. Different target-specific Executions may share
the Product Identity; complete Adapter-declared mutable-resource serialization
and durable destination observation determine absent, exact, or conflict.

After Actions records expire, replay may proceed only from facts still provable
through these platforms.

No retained operational lineage plus an absent destination is a legitimate
initial-publication state. If a present destination cannot prove required
ownership, target, or digest state, the operation fails closed under
`WD-RET-005`. The design does not add a tag witness, binding index, or permanent
Release ledger.

For example, a rebuilt package may be compared with PyPI file hashes and GitHub
Release asset digests. If all projections are exact, a no-op Attempt still
requires channel approval but no destination capability. If a destination
cannot prove exact identity or digest, the Release becomes
`reconciliation-required`.

The initial design does not add a permanent release database or require every
Release Unit to create a GitHub Release audit anchor.

## Failure Conditions

Pre-admission identity or eligibility fails closed when:

- request-local Repository Model compilation is missing, invalid, or not bound
  to the current request identity, `github.run_id`, `github.run_attempt`,
  target, producer, and control identity;
- any admitted Provider Request, Fact Bundle, or Repository Model Snapshot
  belongs to a prior `github.run_attempt`;
- a live path receives a simulation-purpose Snapshot, Fact Bundle, artifact, or
  record, or a simulation pass receives a live-purpose input;
- a descriptor, Project Node, dependency edge, Build Definition, modeled
  variant or output, canonical or native NBGV fact, or required build or
  artifact scope cannot be closed;
- the Release-owned exact-target consumer scan finds a normal disposable-package
  consumer or cannot close its scanned surface set and exceptions;
- the fixed-source Governance attestation is missing, unreadable, expired,
  malformed, provenance-mismatched, or inconsistent with policy/package
  bindings, or has `live_enabled: false`;
- the Live Eligibility Decision is absent, blocking, prior-attempt, or not bound
  by immutable artifact ID/digest to the current request, target, Repository
  Model, producer/control, policy/catalog, admitted `live_enabled` value, and complete
  attestation source provenance;
- the selected workflow ref and pinned target differ;
- target or channel lineage is ineligible under the applicable channel policy;
- the named Buddy slice target is not a ref in the same repository;
- Official Product Identity cannot be derived; or
- Release Execution Identity cannot be derived.

After Attempt creation, planning or execution fails closed when:

- Release Unit policy is missing, invalid, or incomplete;
- a policy-selected variant, quality obligation, or compatibility obligation is
  unresolved or inconsistent with the Repository Model Snapshot;
- a required native projection is not selected and frozen exactly from the
  Repository Model Snapshot;
- a coordinate or Publication Action is invalid or incomplete;
- a destination projection or required Adapter is unsupported;
- a required mutable-resource key is missing, unknown, incomplete, conflicting,
  or cannot be enforced;
- a registry destination cannot prove atomic non-overwriting creation and
  durable exact-state observation;
- the npm tarball or remote package lacks a valid in-package target witness, the
  witness differs from snapshot-bound target/Release Unit/version/definition/
  catalog/control/purpose facts, or only detached provenance is available;
- build or Release qualification fails;
- artifact identity or internal provenance is incomplete;
- observation is partial, conflicting, unknown, or unprovable;
- Publication Snapshot changes a Qualification Snapshot semantic field;
- required external provenance fails;
- first-slice channel approval is rejected or denied, producing unknown
  approval state and no Capability;
- successful approval lacks a valid Authorization Record;
- Authorization Record or Approval Outcome Evidence is mismatched;
- capability admission observes `live_enabled: false`, an expired or invalidated
  attestation, or fixed-source ref/commit/blob/content/schema/binding state that
  differs from the current-attempt Live Eligibility Decision;
- first-slice Buddy approval uses the wrong Environment, omits required reviewer
  context, or fails to enable self-review prevention where available and
  document the human fallback otherwise;
- the normal first-slice capability job requests write before approval, uses a
  PAT, includes `id-token: write`, exceeds minimum `packages: write`, or reaches
  an unrelated package or repository;
- any repository actor with Write, Maintain, or Admin access is not trusted as a
  Buddy publisher;
- the smoke package has a normal developer, CI, or production consumer, or the
  planned/ordinary action set includes delete, restore, permission, visibility,
  or admin operations;
- a capability cannot be obtained;
- a publication action or capability group fails;
- an action may have mutated state without a durable Receipt;
- authoritative records cannot be persisted; or
- Finalizer admission finds missing or conflicting bindings.

Live admission creates no current Attempt and fails closed when Execution
history discovery has incomplete pagination, API denial or truncation, malformed
or duplicate artifacts, digest mismatch, or conflicting/cross-Execution
bindings. Completed approval rejection remains unknown rather than governed
denial for this first-slice GitHub path because no exact current-attempt denial
contract exists.

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

### Commit-Stable Buddy Package

Two admitted, non-coalesced Buddy dispatches for the same channel, Release Unit,
and target have different request and Intent records and address the same
Release Execution.

- Pre-admission request-local Repository Model compilation computes canonical
  and native facts, including `npmPackageVersion`; Buddy Execution Identity
  ignores version and uses only channel, Release Unit, and target.
- The first Intent creates the Release Execution and Attempt 1.
- Attempt 1 planning validates policy-selected variants and obligations, selects
  and freezes `npmPackageVersion` from the request-local Snapshot, then derives
  package coordinates and complete projections. The Qualification Snapshot
  freezes Adapter/version binding, logical operation, potential action schema,
  capability policy, and deterministic resource-key derivation and
  enforceability basis, but no actual mutation action, and seals the first
  snapshot in Attempt 1's Plan lineage.
- Attempt 1 builds every Release Unit variant and qualifies the exact artifacts
  and digests, producing its Qualification Decision.
- Observation classifies the qualified artifact's snapshot-bound desired
  package state as absent, exact, or blocking.
- The Publication Snapshot freezes the desired state, Observation Record, and
  either no mutation for exact state or the exact materialized publish action
  DAG, inputs, complete Adapter-declared key set, capability group and
  requirements, and Receipt contract for absent state.
- Channel approval binds that exact Publication Snapshot digest.
- For absent state, the authorized capability group acquires destination
  capability, publishes the frozen native NBGV `npmPackageVersion`, persists its
  Receipt, and finalizes the Attempt.
- For exact state, the approved Attempt acquires no destination capability,
  performs no mutation, admits the Observation Record, and finalizes as a no-op.
- The later admitted Intent creates a new Attempt in the same Release Execution.
- The later Attempt independently replans, rebuilds, qualifies, observes,
  materializes its Publication Snapshot, receives channel approval, and
  finalizes through the same absent or exact disposition.
- Each Attempt selects and freezes the same authoritative native NBGV projection
  from its request-local Snapshot and then derives the deterministic GitHub
  Packages projection.
- Each Build Request selects the authoritative target-bound
  `npmPackageVersion` from the Repository Model Snapshot; the npm Build Adapter
  applies and verifies it without recomputing NBGV or using a fallback.
- Differing bytes, conflicting ownership, or unprovable state fail closed.
- No prior Plan, Evidence, approval, Observation, Receipt, or outcome is reused
  as an authoritative input.
- Official later rebuilds and never promotes Buddy artifacts or Evidence.

A request for a different target that resolves to the same GitHub Packages
coordinate creates a different Release Execution Identity and Execution. Its
complete Adapter-declared package resource-key set includes that coordinate and
serializes it with overlapping contenders. If it creates the absent coordinate
first, its durable state becomes observable; if another target created first,
observation is conflicting.

A different Release Unit claiming the same coordinate remains a distinct
Release Execution Identity. It does not join or coalesce with the existing
Execution. The complete Adapter-declared package resource-key set includes that
coordinate and serializes access. Whichever authorized Attempt creates the
absent coordinate first establishes state; later incompatible contenders
observe conflict.

### Approved Same-Repository Buddy Ref

An operator dispatches live Buddy for `hcoona-release-smoke-npm` from an
unprotected same-repository feature branch.

- The exact branch commit supplies workflow, Planner, Finalizer, Providers,
  Adapters, compiler, authenticated clients, catalogs, capability declarations,
  publisher, and all other control code.
- No protected-ref or CODEOWNERS eligibility check blocks the Attempt.
- Build, qualification, observation, and exact Publication Snapshot creation
  run without package-write Capability.
- The dedicated Buddy Environment shows target SHA and ref, exact GitHub
  Packages coordinate, artifact digest and manifest, lifecycle scripts, and
  exact action summary.
- Approval creates the Authorization Record. The credential-free admission gate
  validates all group bindings; only its success starts the target-revision
  side-effect job with short-lived `GITHUB_TOKEN` and only `packages: write`.
- The job has no PAT and no `id-token: write`.
- Approval does not prove the branch code or bytes are benign. An approved
  malicious branch can abuse permissions reachable by that token. Inspection
  and safe probes must prove no known Official or production reach; other
  reachable package operations under the smallest configured grants remain
  accepted writer-TCB risk.
- Every repository writer is in the slice publisher TCB. A malicious writer can
  create alternate workflow YAML with `packages: write`; Environment approval
  governs the normal path rather than constraining that adversary.
- Planned ordinary publication cannot delete, restore, change permission or
  visibility, or administer the package. Latent trusted-writer admin authority
  remains accepted risk.
- A first-slice Environment rejection or denial creates no admissible Approval
  Outcome Evidence. It is unknown approval-contract failure, leaves a replayable
  incomplete Attempt, starts no capability group, and publishes nothing.
  Observable review information is diagnostic only.
- Cancellation or platform expiry while approval is pending may end the run
  without a separate record or Finalizer outcome. If no capability group
  started, the platform conclusion is sufficient no-side-effect evidence and
  the Attempt is incomplete and replayable. If capability may have started,
  mutation is possible and the next Attempt reobserves.

### Overlapping Mutable Resources

Two distinct Releases plan live actions whose Adapter-declared resource-key
sets overlap.

- Their business Release identities and Attempt histories remain separate.
- Publication Snapshots bind the complete key sets before approval.
- The overlapping live actions serialize even when their Release Units,
  targets, business identities, or destination action types differ.
- On an equality-group platform, a conservative Adapter projection may also
  serialize non-overlapping actions. The first-slice GitHub Packages projection
  serializes every action for the same physical destination and npm package
  name, including different versions and target-derived tags, while preserving
  the distinct complete key sets.
- A package action includes the exact External Package Coordinate plus any
  additional Adapter-required keys.
- An action with missing, unknown, incomplete, or conflicting required keys is
  blocked before live publication.
- Remediation for an original action reuses exactly its complete frozen
  Adapter-declared key set and never derives it from Product or Execution
  Identity.
- If both Attempts observed the package absent, serialization admits only one
  mutation at a time. The later atomic create-or-exact action may succeed
  without mutation only when the first created exact state; differing state
  fails without mutation. A pure create-only action conflicts and relies on
  replay.

### Official Product Identity Across Targets

Two Official requests name different immutable targets that resolve to the same
canonical NBGV version.

- Each request independently compiles its own same-revision request-local
  Repository Model Snapshot before Execution lookup or admission.
- They share one Official Product Identity but have different Release Execution
  identities and separate Attempt histories.
- No permanent Product Identity-to-target ledger rejects either request or
  reserves destination state.
- After admission, each Attempt plans from its own request-local Snapshot.
- Overlapping live actions serialize on their complete Adapter-declared
  mutable-resource keys.
- The first authorized Attempt to create absent destination state establishes
  durable observable state.
- A later Attempt is exact only if its complete snapshot-bound desired
  projection state, including target binding and artifact bytes, matches.
  Otherwise it observes conflict and fails without overwrite.
- A failed, rejected, or canceled pre-mutation Attempt reserves nothing.

### Official Dry-Run

A feature ref executes Official dry-run.

- The workflow ref is the exact target.
- The request branches to simulation before live lineage eligibility, Product or
  Execution Identity lookup, admission, or Attempt creation.
- The pass compiles exactly one simulation-purpose Repository Model Snapshot and
  binds request identity, run ID and attempt, target, channel, Release Unit,
  canonical and native NBGV facts, producer, and control identity.
- After Snapshot validation, the Planner derives the separately namespaced,
  request-scoped Simulation Identity from those bindings and reuses both
  throughout simulation. A new run attempt compiles a new Snapshot.
- npm build and hypothetical destination projection use the exact frozen native
  `npmPackageVersion` unchanged.
- All variants and Official release checks run.
- Official destinations are observed.
- Hypothetical actions and capability requirements are reported.
- A Simulation Outcome is emitted.
- No live Product, Execution, or Attempt identity, Authorization Record,
  approval, attestation, capability, Receipt, or mutation is created.

### Approval-Pending Run Cancellation or Expiry

Qualification and observation succeed, but GitHub cancels or expires the run
while the Environment approval remains pending. Workflow Delivery has no
watchdog and need not distinguish cancellation from expiry unless the platform
exposes it.

- No Authorization Record exists.
- No destination capability group starts.
- The platform run/job conclusion is sufficient no-side-effect terminal
  evidence.
- A separate Approval Outcome Evidence record and Finalizer outcome may be
  absent.
- The Attempt remains incomplete and replayable, never successful.

If a capability job may have started before cancellation, the conclusion is not
no-side-effect proof. The Attempt remains incomplete and possibly mutated, and
the next Attempt reobserves before planning any action.

- The absent destination remains unreserved; the failed Attempt burns nothing.
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

Three duplicate dispatches for one Release Execution Identity arrive.

- Each candidate run first compiles its own request-local Repository Model
  Snapshot.
- Buddy coalescing derives its Execution key from channel, Release Unit, and
  pinned target without using version, coordinates, or destination projections.
- The first run forms Attempt 1.
- The second is pending.
- The third replaces the second pending run.
- The running Attempt is not canceled.
- The surviving pending run forms a new Attempt after the first finishes.
- Each created Attempt independently compiles policy and derives projections,
  Adapter requirements, and mutable-resource keys from its request-local
  Snapshot without recomputing the Repository Model.
- Each admitted request retains its own Intent record while all Attempts belong
  to the same Release Execution.
- The replaced pending dispatch is not admitted and creates no Attempt.

### Replay After Actions Record Expiration

A later replay rebuilds the target after operational artifacts expired.

- Git, PyPI hashes, GitHub tags, Releases, and asset digests are used when
  available.
- All exact projections produce a no-op Attempt that requires channel approval
  but no destination capability.
- An absent projection remains eligible for initial publication even when no
  operational lineage is retained.
- An unprovable destination blocks and requires reconciliation.
- Version existence alone is not exact proof.

## Deferred LLD Decisions

The first Release LLD must define:

- strict Release Intent, Official Product Identity, Release Execution Identity,
  Attempt Identity, Simulation Identity, Repository Model Snapshot,
  Qualification Snapshot, Publication Snapshot, Simulation Outcome, Evidence,
  Decision, Authorization, Approval Outcome Evidence, Observation, Action,
  Receipt, Outcome, Reconciliation, and Remediation schemas, including Attempt
  binding to Release Execution Identity, `github.run_id`, `github.run_attempt`,
  originating Intent, request identity, and Execution History Admission Snapshot
  ID/digest, with those values treated as required immutable bindings rather
  than Attempt Identity components;
- Execution History Admission Record and Snapshot schemas, complete Actions API
  pagination/query basis, history-only admission, and finalization/explanation
  bindings;
- conditional approval binding and retention schemas: Authorization Record
  after successful approval; generic Approval Outcome Evidence only for a future
  exact attempt-bound denial contract; first-slice rejection as unknown
  replayable incomplete state with diagnostic-only review data; platform
  conclusion and phase-state handling when cancellation or expiry prevents
  context-owned terminal records; and explicit unknown approval-contract failure
  when a running Finalizer has neither applicable admissible result;
- exact Product and Execution Identity canonicalization, lookup, and coalescing
  keys without a global Product Identity-to-target ledger dependency;
- Release Unit release-policy authoring and preset catalog syntax;
- exact Repository Model canonical and native NBGV fact schemas, required
  request identity/run ID/run attempt/target/producer/control bindings,
  pre-admission compilation and admission rules, technical completeness
  diagnostics that create no Attempt, native projection selection, Build
  Request binding, deterministic Buddy destination projection-set derivation,
  post-admission policy/destination completeness, and same-run-attempt Snapshot
  reuse without duplicate compilation;
- NBGV Provider contract tests proving exact-target full-history/tag checkout
  with `fetch-depth: 0` or an equivalent guarantee, rejection of shallow or
  incomplete history before frozen facts are compiled, and continued pinning to
  the exact target commit;
- negative pre-admission tests rejecting mismatched and prior-attempt Provider
  Requests, Fact Bundles, and Repository Model Snapshots, plus `Re-run all jobs`
  tests proving recompilation for the new `github.run_attempt` and rejection of
  prior-attempt artifacts;
- exact-target consumer-policy surface catalog, protected-ref non-executable TCB
  attestation schema, exact `hcoona/three` + `refs/heads/main` +
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json` policy
  binding, fixed-source ref/commit/blob/content verification, Live Eligibility
  Decision transport/binding, and pre-Attempt fail-closed tests for missing,
  unreadable, expired, malformed, provenance-mismatched, disabled,
  prior-attempt, and consumer-positive inputs;
- live-versus-simulation branch tests proving exactly one purpose-bound
  Repository Model compilation per run attempt, same-pass Snapshot reuse,
  Simulation Identity derivation only after Snapshot validation, separately
  namespaced Simulation Identity, cross-purpose admission rejection, and
  absence of live identity, Authorization Record, capability, Receipt, and
  mutation from simulation;
- approval finalization tests covering successful Authorization Record
  admission, first-slice rejection with no admissible Approval Outcome Evidence,
  approval-pending pre-capability cancellation/expiry without a context-owned
  outcome,
  possible-post-capability cancellation requiring reobservation, governed
  failure versus missing-record unknown contract failure, binding rejection,
  and proof that capability groups cannot start without successful
  credential-free admission;
- tests proving GitHub `DeploymentReview` and review-ID deltas are never
  authoritative current-attempt denial Evidence and remain diagnostic only;
- exact destination projection and capability-group catalogs;
- credential-free Capability Admission Decision schema and exact validation of
  Authorization Record, Snapshot, summary, actions, artifacts, resource keys,
  group manifest, the current `live_enabled` value, and fixed-source attestation
  freshness before publisher scheduling, including expiry during the approval
  wait, `live_enabled: false`, ref/commit/blob/content/schema/binding change,
  invalidation, and mandatory new-Attempt recovery;
- GitHub Environment and permission mappings;
- first-slice permission tests proving `evaluate-live-eligibility` receives only
  `contents: read`, effective `actions: read` is confined to history admission,
  explicit `packages: read` is confined to observation, and no other job
  inherits those permissions;
- exact bounded first-slice Buddy package/destination identity,
  reviewer-visible approval summary, target-revision publisher entry point,
  `GITHUB_TOKEN` permission shape, no-PAT/no-OIDC enforcement, self-review
  behavior, repository-writer TCB inventory and revalidation,
  consumer-isolation checks, and Break-Glass deletion/restore handoff;
- negative tests proving the normal path requests no package-write Capability
  before approval, has no known Official or production reach, passes safe denial
  probes for enumerated unrelated assets, plans no
  delete/restore/permission/visibility/admin action, blocks when a repository
  writer is outside the trusted publisher TCB, and does not extend the exception
  to another Buddy destination;
- manual workflow inputs and selected-ref validation;
- artifact and control-bundle naming and retention;
- artifact production with workflow-run-unique deterministic physical names
  that include `github.run_attempt` directly or in the hash preimage, overwrite
  disabled, ID/digest/URL transport, ID-only consumption, and rejection of
  prior-attempt IDs, name fallback, and latest selection;
- history-only platform attribution using artifact ID/digest, source workflow
  run ID, head SHA, payload integrity, exposed metadata, and separately queried
  Jobs/Run phase facts, with payload producer/attempt/workflow claims diagnostic
  and no strict provenance claim without separately approved attestation/OIDC;
- qualification batching and fail-stop implementation;
- immutable reviewer-summary artifact, approval deployment URL, completed job
  summary, and Authorization Record binding/admission;
- active capability-group job topology and empty-group handling;
- exact result-bundle admission;
- optional publisher-side repetition of the same `contents: read` Governance
  and binding revalidation immediately before mutation, with tests when enabled
  proving failure blocks mutation but creates neither a new credential/service
  nor a malicious-writer boundary;
- concrete Destination Adapter observation and publication commands;
- registry Adapter acceptance tests proving atomic non-overwriting creation,
  durable exact-state observation, pure create conflict behavior, and atomic
  create-or-exact concurrent exact versus differing-state behavior where
  supported;
- canonical in-tarball npm target-witness generation, reproducibility,
  build/contents/install validation, remote extraction, and conflict tests;
- Destination Adapter mutable-resource-key declarations, completeness
  validation, abstract overlap serialization, conservative equality-group
  projection coverage, preservation of complete key sets in Snapshots, actions,
  Receipts, and remediation, and exact remediation reuse of the original
  action's complete frozen key set without Product or Execution Identity
  derivation;
- the GitHub Release remediation workflow;
- platform serialization topology and queue behavior, including how available
  GitHub primitives safely serialize every supported overlapping key set and
  block an Adapter whose declared key shape cannot be enforced; and
- one-time protected destination-acceptance workflow, fixed probe coordinates,
  an independent `github.run_attempt == 1` guard on every probe, terminal
  evidence capture guarded by
  `always() && github.run_attempt == 1`, non-first-attempt rejection,
  dependency-failure and ambiguous-mutation evidence persistence,
  incomplete/unknown reconciliation classification, partial-rerun rejection,
  new-invocation/new-coordinate retry, removal verification, and failure
  handling with normal live disabled and legacy Buddy still retired; and
- acceptance tests for every scenario in this MLD.
