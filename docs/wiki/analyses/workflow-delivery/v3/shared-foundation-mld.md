# Workflow Delivery v3 Shared Foundation MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed on 2026-08-04**.

This middle-level design defines the reusable mechanism layer used by CI
Qualification and Release Delivery. It covers record primitives, artifact
identity and provenance, Repository Model compilation, ecosystem Providers,
mechanism Definitions, Build and Quality Adapters, execution bindings,
mechanical outcomes, and generic platform client primitives.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md),
[High-Level Design](./high-level-design.md),
[Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md),
[CI Qualification MLD](./ci-qualification-mld.md), and
[Release Delivery MLD](./release-delivery-mld.md).

Exact schemas, package names, command lines, serialization formats, and
implementation language remain lower-layer decisions.

## Architectural Role

Shared Foundation is a logical mechanism layer, not an independently deployed
business system.

It may be implemented through:

- libraries;
- CLIs;
- statically registered Providers and Adapters;
- reusable Actions;
- family-specific executors; and
- generic platform clients.

It does not own:

- an aggregate root;
- a business lifecycle;
- a universal Plan or record model;
- workflow orchestration;
- scheduling or retries;
- qualification or release policy;
- authorization;
- publication credentials;
- CI or Release finalization; or
- a durable service database.

CI Qualification and Release Delivery call Foundation mechanisms but retain
complete ownership of their own scope, Plans, authoritative records, state
transitions, and verdicts.

## Scope

This MLD owns:

- canonicalization, digest, and strict validation primitives;
- stable identity and binding value types;
- shared Artifact Reference and internal provenance primitives;
- Repository Model Provider contracts;
- target-bound Fact Bundle contracts;
- the read-only Repository Model Compiler;
- Build, Quality, and other mechanism Definition schemas and static catalogs;
- Build and Quality Adapter contracts;
- family-specific Invocation and mechanical Result contracts;
- execution-class and capability-requirement declarations;
- closed mechanical outcome and error taxonomies;
- transparent non-authoritative cache semantics;
- generic platform client primitives;
- selective versioning for intentional cross-revision exchange contracts; and
- conformance and contract-test expectations.

This MLD does not own:

- CI affected-scope policy;
- required or advisory selection;
- Release Unit or channel policy;
- Buddy or Official identity;
- logical destination projection selection;
- destination observation classification;
- publication action planning;
- Observation Record or Receipt semantics;
- job topology, matrix partitioning, or concurrency;
- GitHub Environment selection;
- OIDC or credential grant;
- Evidence Admission or Final Decision;
- Release completion or remediation policy; or
- runtime plugin loading.

## Governing Principles

1. Share mechanisms only when their inputs, outputs, and semantics are genuinely
   common.
2. Shared Foundation never chooses business scope, policy, authority, or
   verdict.
3. Providers resolve facts and capabilities; Adapters execute closed mechanical
   invocations.
4. A universal plugin, executor, Plan, Evidence model, or record envelope is not
   introduced.
5. Contexts form and admit authoritative records from mechanical results.
6. Same-revision static registration is preferred over dynamic extension.
7. Target-influenced evaluation never runs in an authoritative decision
   process.
8. Platform credentials are granted outside Foundation and injected only into
   the exact runtime boundary that needs them.
9. Cache changes performance only; it never changes identity, scope, Evidence,
   provenance, or verdict.
10. Cross-revision compatibility is introduced only for a concrete
    cross-revision consumer.

## Ownership Map

| Concern                           | Shared Foundation             | CI Qualification             | Release Delivery               | Delivery Governance           |
| --------------------------------- | ----------------------------- | ---------------------------- | ------------------------------ | ----------------------------- |
| Canonicalization and digest       | Owns mechanism                | Uses                         | Uses                           | Does not own                  |
| Repository facts                  | Discovers and compiles        | Consumes for impact          | Consumes for complete closure  | Does not own                  |
| Definition schemas and catalogs   | Owns mechanism contracts      | Selects through CI policy    | Selects through Release policy | Protects changes              |
| Build and Quality execution       | Executes closed invocations   | Schedules and admits results | Schedules and admits results   | Protects workflow permissions |
| Artifact identity and provenance  | Defines shared primitives     | Admits CI artifacts          | Admits Release artifacts       | Protects artifact access      |
| Destination projection semantics  | Provides generic clients only | Does not own                 | Owns ports and adapters        | Grants destination authority  |
| Authoritative Evidence or Receipt | Provides binding helpers      | Owns                         | Owns                           | Does not create               |
| Scheduling, batching, and retry   | Provides hints only           | Owns                         | Owns                           | Does not own                  |
| Capability grant                  | Declares requirements only    | Has no publication grant     | Requests through jobs          | Owns grant and denial         |
| Final business verdict            | Does not own                  | Owns CI Decision             | Owns Release state             | Supplies authority outcomes   |

## Logical Module Decomposition

The architecture defines logical modules without requiring one package per
module.

### Record Primitives

Record Primitives provide:

- deterministic canonicalization;
- content digest computation;
- deterministic canonicalization and digesting of context-owned platform
  serialization projections without defining their fields or concurrency
  semantics;
- strict parsing and unknown-field rejection;
- immutable identity value types;
- explicit context and purpose discriminators covered by identity and digest;
- Git target, workflow run, attempt, job, and producer bindings;
- definition, request, Plan, artifact, and result digest bindings;
- timestamp and diagnostic-reference primitives; and
- reusable admission checks.

They do not define one universal record envelope.

CI Plan, CI Evidence, CI Decision, Release Snapshots, Release Evidence,
Authorization Record, Approval Outcome Evidence, Observation Record, Receipt,
Attempt Outcome, Execution History Admission Snapshot, Reconciliation Record,
and Remediation Record remain context-owned schemas.

Foundation may provide strict primitives for admitting GitHub run/job
conclusions and phase-state bindings. Release owns whether those facts prove
pre-capability no-side-effect termination, indicate possible mutation, or
require a context-owned Approval Outcome Evidence record. Foundation does not
promise that a canceled or expired platform run can execute a downstream
Finalizer.

Foundation admission helpers accept a trusted caller-selected mode; serialized
payloads cannot choose it. `current-authority` mechanically verifies exact
current purpose, request, run, run attempt, Attempt, target, producer, control,
artifact, and digest bindings and rejects prior attempts. `execution-history`
mechanically binds only platform-exposed artifact ID/digest, source workflow run
ID, head SHA, payload integrity, and available artifact/run metadata. Jobs and
Run API helpers separately return run-attempt, job, conclusion, and phase facts.
The source may be another run or an earlier attempt of the current run; same-run
history requires separately queried existence of that earlier attempt.
Producer-job, exact-run-attempt, reusable-workflow, purpose, and control claims
inside a historical payload remain diagnostic self-assertions. Release alone
may invoke history mode during pre-Attempt admission and owns correlation to the
same Execution/live purpose/target, the history-only Snapshot, and the
prohibition against satisfying current authority. Foundation does not claim
strict historical workflow/attempt provenance without separately approved
Artifact Attestations or OIDC; the first slice enables no `id-token`.
The helpers never manufacture an artifact-to-attempt or artifact-to-job edge.

### Artifact and Provenance Primitives

Artifact primitives define:

- logical artifact identity;
- content identity;
- immutable transport identity;
- producer invocation identity;
- target and purpose binding;
- expected output role;
- content digest and size;
- media or package kind;
- internal provenance; and
- artifact-set manifests.

For package formats that require durable target attribution, Foundation may
provide canonical target-witness encoding and parsing. The first-slice npm
witness is `workflow-delivery/provenance.json` inside the tarball and binds
target, Release Unit, canonical/native version facts, Build Definition,
catalog/control digests, purpose, and schema while excluding run/Attempt IDs.
Release and Build/Quality Adapters own when that witness is required and how it
affects exact-state classification.

The primitives do not decide whether an artifact is admissible for CI or
Release.

Actions artifact helpers treat names as non-authoritative metadata. Uploads use
deterministic names unique across the complete workflow run and disable
overwrite. Every physical name incorporates `github.run_attempt` directly or in
the deterministic hash preimage. Helpers return artifact ID, digest, and URL.
Downloads require an explicit ID and expose name, producer, run ID, run attempt,
and digest for context-owned admission. They provide no name fallback or
latest-artifact selection.

Foundation also provides generic fixed-source freshness primitives for
context-owned Governance checks. Given immutable repository, fully qualified
ref, and path fields, the GitHub client verifies ref protection, resolves the
ref, reads the blob, canonicalizes content, and returns resolved commit, blob
OID, content digest, and observation time. A read-only live-state helper returns
the current configured value without treating an earlier workflow-context value
as fresh. Comparison helpers detect changed source fields, provenance, content,
schema/binding validation facts, or expiry. Release owns the decision to block,
require a new Attempt, and compare against a Live Eligibility Decision;
Foundation creates no Governance authority, credential, service, or
malicious-writer boundary.

### Repository Model Mechanisms

Repository Model mechanisms include:

- Provider contracts;
- Provider Results and Provider Request Manifests;
- Fact Bundles;
- fact admission;
- normalized Project Node and dependency facts;
- path and global-input facts;
- capability and dimension facts;
- Release Unit declaration loading;
- NBGV fact resolution; and
- Repository Model compilation.

They do not compute CI affected scope or choose Release policy.

### Definition Catalogs

Definition catalogs contain statically registered:

- Build Definitions;
- Quality Definitions;
- ecosystem capability definitions;
- generic execution-class definitions; and
- generic platform client definitions.

Release-owned Destination Definitions and Destination Adapters are not part of
Shared Foundation. They may use Foundation clients and primitives.

### Build and Quality Adapters

Build and Quality Adapters execute family-specific invocations.

They do not:

- add themselves to a Plan;
- choose required or advisory status;
- choose a Release channel;
- schedule dependent work;
- authorize a side effect; or
- emit a final business verdict.

### Execution and Client Primitives

Execution primitives provide:

- family-specific Invocation bindings;
- execution-class declarations;
- capability-requirement descriptions;
- environment and toolchain identity primitives;
- batching compatibility hints;
- cache identity hints;
- timeout and cancellation inputs;
- normalized mechanical outcomes; and
- generic API and command client helpers.

They do not create jobs, Environments, permissions, credentials, matrices, or
concurrency groups.

When Release Delivery supplies a closed platform serialization projection,
Foundation may canonicalize and digest that payload and validate exact
round-trip binding. Foundation does not choose the projection, decide whether
it safely covers mutable-resource overlap, or treat its digest as a substitute
for the context-owned complete resource-key set.

## Shared Record Boundary

### No Universal Record Envelope

The architecture intentionally does not introduce a cross-context
`RecordEnvelope`.

Fields that look mechanically similar may have different authority and
lifecycle semantics. For example:

- CI Evidence belongs to one candidate and obligation;
- Release Evidence belongs to one Release Attempt and obligation;
- an Observation Record binds one Release Attempt, logical projection,
  immutable desired-state basis, and canonical remote response and observed
  facts; and
- a Receipt proves one authorized Release action.

Shared value types and binding helpers prevent accidental inconsistency without
claiming that these records are one domain type.

An Observation Record cannot bind a future Publication Snapshot. Release later
admits the Record and seals it, the resulting desired state, and any materialized
actions into the Publication Snapshot.

### Strict Validation

Every Foundation parser or admission helper:

- rejects unknown fields unless the family contract explicitly allows an
  extension map;
- rejects duplicate or ambiguous identities;
- validates canonical encodings before digest comparison;
- validates all required target, producer, definition, request, and artifact
  bindings;
- rejects a record whose context or purpose discriminator does not match the
  admitting execution, including live Release versus release simulation;
- rejects conflicting duplicate records; and
- returns a typed mechanical failure rather than silently defaulting.

Strict validation is a mechanism. The calling context decides whether the
failure blocks planning, fails an obligation, requires reconciliation, or
changes another business state.

### Authoritative Record Formation

Providers and Adapters emit family-specific mechanical result payloads.

The context executor then:

1. binds the result to the context-owned Plan or Attempt;
2. binds the exact obligation or action identity;
3. binds producer job and workflow attempt;
4. binds artifacts and diagnostics;
5. canonicalizes and digests the record; and
6. persists the context-owned authoritative record.

The context Finalizer performs Evidence, Observation, or Receipt admission.
Foundation does not run a shared Finalizer.

## Selective Contract Versioning

### Same-Revision Internal Records

Internal records whose producers and consumers run from the same revision and
workflow attempt do not require a universal API version.

Their exact schema is bound by:

- the same target revision;
- the same workflow attempt;
- the definition and request digests; and
- strict producer and consumer code from that revision.

### Cross-Revision Exchange Contracts

A contract intentionally produced by one revision and consumed by another must
contain:

- stable `kind`;
- explicit `contract-version`;
- producer revision;
- canonical payload digest; and
- compatibility constraints.

The initial concrete example is a reconciliation request produced by an older
Release Attempt and consumed by a current protected Break-Glass Remediation
workflow.

Adding `contract-version` to this exchange contract does not imply a universal
version field on every internal record.

Compatibility rules are explicit:

- a consumer accepts only declared compatible versions;
- unknown major versions fail closed;
- a migration must preserve the original payload and append the transformed
  representation;
- current code never guesses an omitted or unknown version; and
- compatibility code is owner-reviewed control code.

## Definition and Catalog Model

### Stable Logical Identity

Every Definition has a stable logical ID identifying its intended mechanism
role.

The exact behavior used by one Plan is frozen through:

- Definition Snapshot;
- definition digest;
- selected implementation identity;
- implementation or catalog digest;
- toolchain constraints; and
- normalized parameters.

The logical ID is not sufficient to reconstruct historical behavior without the
snapshot and digests.

### Static Same-Revision Catalog

The initial design uses a statically registered catalog contained in the same
revision as the calling Planner and Finalizer.

It does not support:

- dynamic code loading from descriptors;
- remote plugin download;
- runtime package discovery;
- an external plugin marketplace;
- unreviewed adapter scripts; or
- a stable cross-version plugin ABI.

Descriptors and policies may select only allowlisted logical IDs and parameters.
They cannot provide executable implementation paths.

### Definition Versus Policy

A Definition states mechanical semantics such as:

- accepted input type;
- parameter model;
- supported dimensions;
- required toolchain;
- expected outputs;
- execution class;
- capability requirements;
- mechanical prerequisites; and
- result contract.

A Definition does not decide:

- whether it applies to a candidate;
- whether it is required or advisory;
- whether a Release channel selects it;
- which Release Unit is delivered;
- whether a destination projection is authorized; or
- whether a failure changes a business verdict.

Those decisions belong to CI or Release policy and planning.

## Provider Model

### Provider Responsibility

A Provider consumes an exact target and emits normalized repository facts or
resolves ecosystem capabilities.

Provider responsibilities include:

- manifest and workspace discovery;
- Project Node identity;
- dependency direction and edge type;
- path ownership;
- global-input relationships;
- supporting test target discovery;
- native aggregate target resolution;
- dimensions and runner constraints;
- build and packaging capability discovery; and
- unresolved or conflicting fact reporting.

A Provider does not:

- select a CI comparison range;
- compute the final affected closure;
- create CI obligations;
- select Release Units;
- choose Release variants;
- execute Build or Quality Definitions; or
- emit a business verdict.

### Provider Execution Modes

Every Provider produces one family-specific Provider Result binding:

- exact target;
- Provider logical and implementation identity;
- request digest;
- toolchain identity;
- normalized facts;
- unresolved or conflicting facts;
- mechanical outcome; and
- diagnostic reference.

Every Provider declares one execution mode.

#### Pure Provider

A pure Provider:

- reads files and immutable configuration only;
- does not invoke target-defined build hooks or scripts;
- may run in the authoritative Planner process; and
- returns its Provider Result directly to Repository Model compilation.

#### Target-Evaluating Provider

A target-evaluating Provider may invoke an ecosystem tool whose behavior is
influenced by target-controlled content.

Examples may include:

- MSBuild project or graph evaluation;
- package-manager workspace commands;
- build-backend metadata evaluation; or
- another native graph facility with executable extension points.

It must run in an unprivileged discovery job that:

- has no publication capability;
- has no destination secret;
- cannot write protected repository state;
- receives the exact target;
- receives a closed Provider request bound to request identity, explicit
  purpose, `github.run_id` and `github.run_attempt`, target, producer, and control
  identities; and
- emits an immutable target-bound Fact Bundle wrapping its Provider Result.

When a target-evaluating Provider owns NBGV facts whose version height depends
on Git history, it must materialize the exact target with complete ancestry and
tags, equivalent to `fetch-depth: 0`, verify that the checkout remains pinned to
the exact target, and fail before NBGV invocation if the repository is shallow,
required tags or ancestry are incomplete, or the guarantee cannot be proved.

The authoritative Repository Model Compiler consumes the Fact Bundle only after
strict admission.

### Fact Bundle

A Fact Bundle binds:

- complete Provider Result payload and digest;
- authoritative target-bound canonical and native NBGV projection facts when
  the wrapped Provider Result owns version resolution;
- request identity, explicit purpose, `github.run_id` and `github.run_attempt`,
  target, producer job, and control identity;
- request artifact and digest;
- immutable transport identity; and
- Fact Bundle digest.

A Fact Bundle does not contain CI or Release policy.
Strict admission requires its run-attempt binding to equal the current
`github.run_attempt`; a prior-attempt Bundle is invalid even when its request
identity, `github.run_id`, and target match.

### Provider Determinism

For the same:

- target;
- Provider implementation;
- request;
- declared toolchain; and
- authoritative dependency metadata,

a Provider must emit canonically equivalent normalized facts.

Time, branch display name, workflow URL, cache availability, or unrelated
environment state must not change semantic facts.

When an ecosystem cannot provide a required fact, the Provider emits
`unsupported`, `unknown`, or `conflicting` rather than inventing a default.

Strict Fact Bundle admission proves identity, integrity, and contract
conformance. It does not independently re-prove every ecosystem semantic fact.
Semantic completeness is an accepted Provider contract obligation, validated
through conformance fixtures and integration scenarios against the
ecosystem-native authoritative abstraction.

A Provider must not emit a narrower dependency, path, global-input, capability,
or dimension model when the authoritative abstraction cannot establish
completeness. Such state is `unknown`, `unsupported`, or `conflicting` and
blocks model closure.

## Repository Model Compiler

The Repository Model Compiler is a shared, read-only mechanism.

It consumes:

- exact target identity;
- Release Unit declarations;
- closed Provider Request Manifest;
- direct pure-Provider Results;
- Provider Results extracted from admitted Fact Bundles;
- static Definition catalogs;
- NBGV facts;
- declared extra inputs; and
- repository path policy facts.

It emits one immutable Repository Model Snapshot containing:

- exact target, compilation purpose, caller request identity,
  `github.run_id`, `github.run_attempt`, producer, and control bindings;
- caller-selected channel and Release Unit binding when required by Release
  simulation purpose;
- Project Nodes;
- dependency and reverse-dependency facts;
- path ownership and global-input facts;
- supported capabilities and dimensions;
- Release Units and artifact variants;
- Build Definition references;
- build and declared-input closures;
- target-bound NBGV canonical facts and required native ecosystem projections;
- reverse indexes; and
- explicit unresolved or conflicting model facts.

Canonical Repository Model Snapshot serialization and its digest cover every
binding above, including request identity, `github.run_id`,
`github.run_attempt`, target, producer, and control identity.

A ready Snapshot closes descriptor loading, Project Nodes and dependency graph,
Build Definitions, modeled variants and outputs, canonical and required native
NBGV facts, including `npmPackageVersion` where required, and complete build and
artifact scope. The compiler emits blocking state rather than a partial ready
Snapshot when any of those facts is missing, unknown, or conflicting.

For each Release candidate run attempt, the live-release or release-simulation
caller compiles exactly one authoritative Snapshot for its purpose and reuses it
throughout the resulting live Attempt or simulation pass. A new
`github.run_attempt` compiles a new Snapshot even when request identity,
`github.run_id`, and target remain unchanged. Shared transport schemas may be
reused across these purposes only when the purpose discriminator is explicit
and digest-bound; admission rejects the other purpose.

For simulation, the Repository Model Snapshot binds validated purpose, request,
run, target, channel, Release Unit, version facts, producer, and control inputs;
it does not bind a future Simulation Identity. Release derives that Identity
only after Snapshot validation, and later simulation records may bind both.

### Provider Request Manifest

Before launching target-evaluating discovery, the authoritative compilation
coordinator closes one Provider Request Manifest.

The manifest binds:

- exact target;
- caller request identity, purpose, `github.run_id`, `github.run_attempt`,
  producer, and control identities;
- caller-selected channel and Release Unit when required by simulation purpose;
- static catalog digest;
- every expected Provider logical and implementation identity;
- execution mode;
- request ID and request digest;
- discovery basis;
- expected terminal result identity; and
- manifest digest.

Pure structural discovery may determine which target-evaluating Provider
requests are required. Once the manifest seals, no Provider or discovery job may
add or remove a request.

Compilation requires exactly one terminal Provider Result for every manifest
entry. A result may arrive directly from a pure Provider or through an admitted
Fact Bundle. Missing, duplicate, conflicting, or unexpected results block the
Repository Model Snapshot.

The compiler validates structure and closure but does not:

- compute one CI candidate's affected scope;
- select quality policy;
- create obligations;
- select Buddy or Official;
- choose destination projections; or
- authorize execution.

CI and Release use the same compiler and Snapshot contract but independently
compile context-bound Snapshot instances. Each Release candidate run attempt
branches to live Release or release simulation and compiles exactly one
same-revision, request-local Snapshot for that purpose before live Execution
lookup, coalescing, or admission. Compilation failure creates no Attempt. The
resulting live Attempt or simulation pass reuses that Snapshot without a second
compilation. Live planning validates channel-selected variants and obligations,
selects and freezes native projections, and then derives and validates
destination projections and coordinates, Adapter and version bindings, logical
operations, potential action and dependency schemas, capability policy, and
deterministic complete mutable-resource-key derivation and enforceability basis.
Actual live actions, inputs, and complete action key sets materialize only after
build, qualification, and observation and freeze in the Publication Snapshot. A
new run attempt compiles a new Snapshot. Cross-purpose, other-request, and
prior-attempt Snapshots are rejected.

## Adapter Model

### Family-Specific Interfaces

Build and Quality use separate Adapter interfaces.

The architecture does not define one universal:

- plugin interface;
- request payload;
- result payload;
- retry model;
- exit-code interpretation; or
- artifact contract.

Shared binding primitives remain common, while each family defines only the
fields required by its mechanical semantics.

One ecosystem package may implement Provider, Build Adapter, and Quality Adapter
interfaces, but the interfaces remain separately invocable and permissioned.

### Build Adapter

A Build Adapter receives a closed Build Invocation containing:

- exact target;
- Release Unit and artifact variant identity;
- Build Definition Snapshot and digest;
- Build Request digest;
- exact selected authoritative native NBGV projection and source fact binding;
- dimensions;
- declared toolchain;
- declared inputs;
- expected output roles;
- execution class;
- cache hints; and
- producer binding inputs.

It emits a Build Result containing:

- mechanical outcome;
- materialized output references;
- content digests and sizes;
- output-role mapping;
- toolchain identity;
- internal provenance inputs;
- cache-use diagnostics;
- producer identity; and
- diagnostic reference.

The Adapter does not decide whether the output satisfies CI or Release.

The Adapter applies and verifies the exact frozen projection from the Build
Invocation. It must not invoke NBGV to recompute the value, derive a substitute
from another version field, or fall back to a manifest or ambient build-system
version.

For Official, the canonical NBGV version remains an Official Product Identity
binding and immutable target completes Release Execution Identity. The Build
Invocation still carries the exact frozen native ecosystem projection used by
publication or dry-run.

### Quality Adapter

A Quality Adapter receives a closed Quality Invocation containing:

- exact target;
- Quality Definition Snapshot and digest;
- request and obligation identity inputs;
- concrete target;
- dimensions;
- runner and toolchain constraints;
- prerequisite output references;
- timeout and cancellation inputs; and
- cache hints.

It emits a Quality Result containing:

- mechanical outcome;
- normalized check result;
- target and dimension bindings;
- relevant artifact or output digests;
- tool-specific summary;
- diagnostic reference;
- producer identity; and
- optional structured measurements.

The Adapter does not know whether the obligation is required or advisory and
does not emit CI or Release Evidence directly.

### Destination Boundary

Logical destination projections, projection-atomic classification,
Publication Actions, Receipts, replay rules, and remediation are Release
Delivery semantics.

Release Delivery therefore owns:

- Destination port contracts;
- Destination Definitions;
- destination-specific observation and publication Adapters;
- projection classification;
- action decomposition;
- Receipt payload semantics; and
- remediation operations.

Shared Foundation may provide generic clients for:

- authenticated or anonymous HTTP;
- GitHub API and CLI invocation;
- complete REST pagination and GraphQL cursor traversal with recorded query
  basis;
- registry API invocation;
- retryable transport;
- response canonicalization;
- digest parsing;
- artifact upload and download streams; and
- capability-requirement declaration.

Generic clients expose remote facts and responses. They do not classify a
Release projection, plan a publication action, decide replay safety, or emit a
Receipt.

GitHub clients may expose workflow run `node_id`, run attempt, jobs,
deployments, and deployment-review facts. Deployment Review data is raw
diagnostic material, not authoritative current-attempt denial Evidence: it lacks
documented `run_attempt`/job binding and no review-ID delta helper may manufacture
that authority. Release owns any future exact admission contract.

## Invocation Model

### Closed Invocation

Before execution, the calling context closes the semantic request.

An Invocation must not allow the Adapter to discover new business scope.
It identifies:

- exact target or remote object;
- Definition Snapshot and digest;
- implementation identity;
- request digest;
- concrete target and dimensions;
- declared input identities and digests;
- expected output contract;
- execution class;
- timeout and cancellation contract; and
- producer binding inputs.

An Adapter may perform mechanical discovery required by the selected operation,
such as enumerating tests inside a chosen aggregate target. It cannot add a new
obligation, variant, projection, or action.

### Context-Owned Scheduling

CI and Release own:

- DAG construction;
- prerequisite semantics;
- ready-work selection;
- matrix partitioning;
- batching;
- fail-stop;
- retry;
- supersession;
- skip behavior; and
- final aggregation.

Foundation may emit a batching compatibility key when invocations share:

- Adapter implementation;
- toolchain;
- runner;
- dimensions;
- compatible prerequisites; and
- cache or workspace requirements.

The context may batch only while preserving each invocation's semantic identity
and individual result.

### Execution Classes

Every Provider or Adapter declares one execution class.

Initial classes are:

- `authoritative-pure`: same-revision control code with no target project/build
  execution; its governance trust eligibility is context-owned;
- `unprivileged-target-evaluation`: target-influenced discovery;
- `unprivileged-target-execution`: build or quality execution;
- `read-only-remote-observation`: remote reads with minimal read capability;
- `privileged-side-effect`: authorized remote mutation; and
- `privileged-remediation`: separately authorized exceptional mutation.

Foundation declares the class and minimum capability requirements. The calling
context and Delivery Governance create the actual job, Environment,
permissions, OIDC trust, and credential grant.

Release Qualification may bind declared Capability requirements into its
Snapshot, but it cannot request, approve, or create live Capability. Only an
authorized side-effect capability group in the normal v3 flow may request
destination Capability after a credential-free context-owned admission decision
validates the Authorization Record and exact Snapshot, summary, action,
artifact, resource-key, and group bindings. The credential-bearing invocation
also revalidates them. The first-slice writer-TCB exception is context-owned and
does not change Foundation contracts for other destinations.

### Capability Consumption

Foundation never:

- grants a credential;
- selects an Environment;
- broadens permissions;
- searches for ambient fallback credentials;
- substitutes a personal token;
- selects a weaker identity; or
- retries through an alternate authority path.

A privileged client consumes only the capability explicitly injected into the
authorized runtime boundary.

The client validates the Invocation binding that the external credential format
cannot express. A credential's platform scope and the semantic action
authorization remain distinct.

## Mechanical Outcomes and Diagnostics

### Closed Outcome Taxonomy

Foundation families use a closed mechanical taxonomy.

Initial categories are:

- `succeeded`;
- `invalid-request`;
- `unsupported`;
- `unavailable`;
- `execution-failed`;
- `timed-out`;
- `canceled`;
- `conflicting`;
- `unknown`;
- `unprovable`; and
- `internal-contract-error`.

Family contracts may refine details without changing the top-level category.

Foundation does not emit:

- required or advisory;
- CI passed or failed;
- Release replayable;
- Release reconciliation-required;
- Release completed; or
- publication authorized.

The calling context maps mechanical outcomes to its own state model.

### Diagnostics

Every non-success result includes:

- stable category;
- human-readable summary;
- machine-readable detail code;
- diagnostic artifact or log reference when available;
- relevant target, definition, request, and producer bindings; and
- whether any output or remote mutation may have occurred.

Diagnostics never substitute for a required result payload or authoritative
record.

## Artifact Identity and Provenance

### Identity Layers

Foundation separates:

1. **Logical identity**: Release Unit, variant, output role, and purpose.
2. **Content identity**: canonical digest, size, and media or package kind.
3. **Transport identity**: immutable Actions artifact ID or equivalent storage
   identity.
4. **Producer identity**: target, Definition, request, job, workflow run, and
   attempt.

No one layer substitutes for another.

### Internal Provenance

Internal provenance binds:

- target commit;
- context and purpose;
- Release Unit and variant when applicable;
- Definition Snapshot and digest;
- request digest;
- version projection;
- dimensions;
- declared inputs;
- toolchain identity;
- producer job and workflow attempt;
- output role;
- content identity; and
- transport identity.

CI and Release may use the same provenance structure while applying different
admission rules.

A valid CI provenance object is not sufficient for Release admission because
its context, purpose, Plan, and producer bindings differ.

### Artifact Materialization

The consumer:

- retrieves by immutable transport identity;
- verifies expected producer and artifact metadata;
- recomputes content digest and size;
- verifies artifact-set manifest consistency;
- verifies provenance bindings; and
- rejects missing, extra, or conflicting outputs.

Artifact name alone is never authoritative.

## Cache and Mechanical Reuse

### Transparent Shared Cache

CI and Release may share non-authoritative caches for:

- tool downloads verified by trusted distribution identity or digest;
- package-manager downloads verified against authoritative lock or package
  metadata;
- restored immutable dependencies;
- content-addressed intermediates whose action identity closes every semantic
  input; and
- compiler outputs only when cache writer trust and material provenance are
  acceptable to the consuming execution class.

Cache keys may use:

- target or input digests;
- Definition and request digests;
- toolchain identity;
- dimensions;
- platform;
- declared dependency metadata; and
- Adapter-specific compatibility data.

Cross-context cache entries also bind:

- producer trust class;
- writer repository and workflow identity;
- cache namespace;
- source target or complete input closure;
- action or Invocation digest; and
- material provenance.

An ordinary pull-request, fork, or otherwise lower-trust job cannot populate a
compiler or intermediate cache namespace consumed by Release.

Release may consume target-derived compiler or intermediate outputs from
another context only when:

- authenticated cache infrastructure prevents substitution under an existing
  action identity;
- the producer class is explicitly trusted for Release cache consumption;
- the cache key closes target inputs, Definition, request, dimensions,
  toolchain, and declared dependencies;
- material provenance is available and verified; and
- a cache miss can rederive the same semantic output from authoritative inputs.

Otherwise cross-context reuse is limited to independently verifiable immutable
downloads and dependencies.

### Release Independence

Release still:

- runs its own Build Invocation;
- materializes its own final output set;
- recomputes output digests;
- creates Release-purpose provenance;
- performs Release quality obligations; and
- admits only Release Attempt records.

A cache hit does not import:

- a CI, simulation, other-request, or prior-Attempt Repository Model Snapshot;
- CI Artifact Reference identity;
- CI Evidence;
- CI producer identity;
- CI success;
- a dry-run result; or
- an earlier Release Attempt result.

Continuous cache unavailability may reduce performance but cannot change
semantic output, scope, or verdict.

## Security Model

### Static Supply-Chain Boundary

All executable Provider, Adapter, compiler, canonicalization, and client code is
contained in the selected revision and static catalog.

Definitions and descriptors may select allowlisted implementation IDs and
parameters but cannot inject executable paths, packages, commands, or remote
code.

### Target-Controlled Input

Target-controlled manifests and configuration are untrusted inputs to trusted
Foundation code.

Pure Providers must not cross into target execution.
Target-evaluating Providers, Build Adapters, and Quality Adapters run only in
unprivileged execution classes.

No runtime that evaluates or executes target-defined product/build content
receives publication capability. The context-owned, explicitly accepted
`hcoona-release-smoke-npm` live Buddy GitHub Packages exception runs
target-revision control and publisher code after dedicated Environment
approval, but does not execute target-defined product/build code in that
side-effect invocation. That target-revision publisher remains a
`privileged-side-effect` invocation and validates exact bindings by contract,
but is not an independent adversarial boundary. Every repository writer is
inside the slice publisher TCB and may author alternate write-capable workflow
jobs; Environment approval governs the normal flow rather than imposing a
malicious-writer permission ceiling. Foundation does not generalize this
exception to Official or another Buddy destination.

### Boundary Validation

Every process boundary validates:

- exact input identity and digest;
- allowed Definition and implementation ID;
- execution class;
- expected artifact inputs;
- producer binding;
- result shape;
- output digests; and
- absence of undeclared outputs where the family contract requires closure.

No Adapter result is trusted solely because the process exited zero.

### Generic Client Safety

Generic remote clients:

- accept endpoint and operation inputs only from a closed invocation and static
  destination configuration;
- receive credentials only through the authorized runtime;
- receive a capability handle binding the credential to allowed origin,
  audience or resource, endpoint family, identity, and operation class;
- refuse to attach credentials when the requested endpoint or operation falls
  outside the capability handle;
- reject credential-bearing cross-origin redirects;
- never log secrets or tokens;
- do not infer a stronger operation from a weaker request;
- expose conflict and unknown state explicitly;
- do not enable destructive overwrite by default; and
- return typed mechanical responses for the Release-owned adapter to interpret.

## Failure Conditions

Foundation processing fails closed when:

- a logical implementation ID is absent or ambiguous;
- a descriptor attempts dynamic code selection;
- a Definition or request is malformed;
- canonicalization or digest verification fails;
- a target, producer, request, artifact, or result binding mismatches;
- a Provider Request, Fact Bundle, or Repository Model Snapshot does not match
  the current `github.run_attempt`, including reuse from a prior run attempt;
- a pure Provider attempts a target-evaluating operation;
- a target-evaluating Provider lacks an unprivileged execution boundary;
- a Provider Request Manifest is absent or changes after isolated discovery
  starts;
- an expected Provider Result is missing, duplicated, conflicting, or
  unexpected;
- a Provider emits unresolved or conflicting required facts;
- the Repository Model cannot close required facts;
- an Adapter receives unsupported dimensions or inputs;
- an Adapter emits missing, extra, or conflicting outputs;
- artifact content differs from its manifest or provenance;
- a context attempts to admit another context's artifact or result;
- a live Release or release simulation attempts to admit a Snapshot, Fact
  Bundle, artifact, or record from the other purpose;
- an untrusted cache writer or incomplete cache provenance is offered to a
  higher-trust consumer;
- a capability requirement cannot be satisfied;
- a credential-bearing client request falls outside the capability handle's
  origin, audience, resource, identity, or operation binding;
- a client attempts credential fallback;
- an unknown mechanical outcome is returned;
- a cross-revision contract version is absent, unknown, or incompatible; or
- a required mechanical result cannot be persisted.

Foundation does not convert any of these conditions into a weaker operation.

## Acceptance Scenarios

### Target-Evaluating .NET Provider

The .NET Provider requires native MSBuild graph evaluation.

- The Provider declares `unprivileged-target-evaluation`.
- A discovery job receives the exact target and closed Provider request.
- It has no publication credential or Environment.
- It emits a target-bound Fact Bundle.
- The Repository Model Compiler admits the bundle and normalizes Project Nodes
  and dependency facts.
- CI later computes affected scope; Release later selects one complete Release
  Unit closure.

### Pure Descriptor Provider

A fixed-basename Release Unit descriptor is parsed without executing target
code.

- The Provider declares `authoritative-pure`.
- Strict parsing rejects unknown fields and executable implementation paths.
- The result enters Repository Model compilation directly.
- The descriptor can select only static catalog IDs.

### Shared Build Definition With Separate Contexts

CI and Release invoke the same Build Definition for one artifact variant.

- Their Build Invocations have different context, purpose, target, Plan, and
  producer bindings.
- Both may hit the same transparent compiler or dependency cache.
- Each rematerializes outputs and recomputes digests.
- CI forms CI-owned artifact provenance and Evidence.
- Release forms Release-owned artifact provenance and Evidence.
- Release cannot admit the CI Artifact Reference.

### Quality Result Wrapping

A Quality Adapter executes one concrete test target.

- The Adapter does not know whether the obligation is required or advisory.
- It emits one Quality Result with mechanical outcome and diagnostics.
- CI binds it to a CI obligation and creates CI Evidence.
- Release may invoke the same Definition and bind a separate result to a
  Release obligation.
- Neither context consumes the other's Evidence.

### Identity-Preserving Batch

Five compatible Quality Invocations share Adapter, toolchain, runner, and
dimensions.

- Foundation exposes one compatibility key.
- CI chooses to batch the invocations.
- The Adapter returns five separately identified Quality Results.
- One failed result does not change the identity or meaning of the other four.
- The CI Finalizer admits each Evidence object independently.

### GitHub Release Destination Split

A GitHub Release projection has one missing installer.

- Release-owned GitHub destination logic classifies the projection as partial.
- Foundation GitHub client primitives only return release, target, asset, and
  digest facts.
- Foundation does not decide replay or remediation.
- The Release adapter creates the reconciliation or remediation semantics.
- Governance grants any required capability outside Foundation.

### Cross-Revision Remediation Request

An old Release Attempt emits a reconciliation request consumed later by current
protected remediation code.

- The request carries stable kind, contract version, producer revision, and
  payload digest.
- Current code accepts only declared compatible versions.
- An incompatible version fails before approval or mutation.
- Any explicit migration preserves the original request and appends the
  migrated representation.

### Cache Unavailable

The shared package and compiler caches are unavailable.

- Provider, Build, and Quality requests remain unchanged.
- Adapters retrieve authoritative dependencies and execute normally.
- Artifact identity, provenance, Evidence, and verdict semantics remain
  unchanged.
- Only elapsed time and cache diagnostics differ.

### Lower-Trust Cache Entry

A pull-request job writes a compiler output under a key that resembles a
Release-compatible action identity.

- The entry belongs to a lower-trust writer and namespace.
- Release refuses the entry before materialization.
- Release rederives the output from authoritative inputs or uses an admissible
  trusted cache entry.
- No CI artifact identity, producer claim, or cached bytes become Release
  provenance.

### Credential-Bound Generic Client

A faulty destination invocation supplies an endpoint outside the static
destination origin while a registry credential is present.

- The capability handle identifies the allowed origin, audience or resource,
  endpoint family, identity, and operation class.
- The generic client refuses to attach the credential or follow a cross-origin
  redirect.
- No request reaches the unbound endpoint with authorization material.
- Release records the mechanical client failure through its own action result.

### Unsupported Mechanism

A policy selects a Quality Definition whose Provider cannot resolve a required
target for the ecosystem.

- The Provider returns `unsupported` with diagnostics.
- Foundation does not silently choose another target or Definition.
- The context maps the result to its own blocked or failed state.

## Conformance and Testing

Every Foundation implementation requires:

- strict contract parsing tests;
- canonicalization and digest golden tests;
- opaque platform-serialization-projection canonicalization and binding tests
  that preserve, rather than replace, context-owned complete resource-key sets;
- Provider fixture tests;
- target-binding and producer-binding negative tests;
- Repository Model compilation scenarios;
- Repository Model ready-versus-blocked completeness-gate tests;
- Build Adapter artifact-manifest tests;
- Build Adapter frozen-version-projection tests that reject recomputation,
  alternative derivation, and fallback;
- Quality Adapter result-shape tests;
- context-isolation tests proving CI outputs cannot satisfy Release;
- cache-disabled equivalence scenarios;
- execution-class and capability-denial tests;
- static catalog allowlist tests;
- cross-revision contract compatibility tests where applicable; and
- integration tests against the actual ecosystem or platform abstraction.

Adapter acceptance tests validate trusted ecosystem behavior once at the
implementation boundary. Runtime planning relies on the accepted contract
rather than repeatedly re-proving the underlying build or platform system.

## Deferred LLD Decisions

The first Shared Foundation LLD must define:

- logical package and executable decomposition;
- exact canonicalization and digest algorithm;
- opaque context-owned platform-serialization-projection canonicalization,
  digest, and exact-binding fixtures without Foundation-owned lock semantics;
- strict value-type and binding schemas;
- family-specific Invocation and Result schemas;
- Fact Bundle and Repository Model Snapshot transport, including authoritative
  target-bound canonical and native NBGV projections and explicit
  `github.run_id` and `github.run_attempt` bindings;
- Repository Model Snapshot purpose, request identity, run ID, run attempt,
  producer, control, and target bindings, plus Release pre-admission
  compilation, exactly-once per-run-attempt reuse, and replay-recompilation
  tests;
- static catalog registration syntax;
- Provider execution-mode declaration;
- Provider Result, Fact Bundle, and Provider Request Manifest schemas;
- NBGV-owning Provider checkout contracts and control fixtures proving exact
  target pinning, `fetch-depth: 0` or equivalent complete ancestry/tag
  availability, and fail-closed rejection of shallow or incomplete history
  before canonical or native NBGV facts are compiled;
- exact mechanical outcome and diagnostic codes;
- Artifact Reference, artifact-set manifest, and provenance schemas;
- Actions artifact upload results and ID-only download contracts, deterministic
  workflow-run-unique physical naming with `github.run_attempt` directly or in
  the deterministic hash preimage, overwrite disabled, and rejection of
  prior-attempt ID, name-fallback, and latest-selection behavior;
- generic protected-ref fixed-source and live-state freshness helpers, including
  exact repository/ref/path input binding, uncached repeated observation,
  commit/blob/content provenance comparison, current-time expiry evaluation,
  and change/disablement fixtures without context-owned admission policy;
- caller-selected current-authority versus execution-history helpers,
  platform-limited historical artifact/run attribution, separate Jobs/Run phase
  facts, and negative tests proving self-asserted producer/attempt/workflow
  claims never become authority;
- canonical package target-witness encoding/parsing and npm fixture coverage
  without run/Attempt identity;
- cache-key construction and cache namespace policy;
- implementation and catalog digest calculation;
- batch compatibility-key calculation;
- generic GitHub and registry client surfaces;
- complete REST/GraphQL pagination and query-basis capture, workflow
  run/node/job response fixtures, diagnostic-only deployment-review fixtures,
  and malformed, 403/404, timeout, and truncation handling;
- cross-revision contract version and compatibility rules;
- process-boundary validation entry points;
- contract-test harnesses and fixtures;
- negative binding tests rejecting mismatched and prior-attempt Provider
  Requests, Fact Bundles, and Repository Model Snapshots, plus replay tests
  proving `Re-run all jobs` recompiles for the new `github.run_attempt` and
  rejects prior-attempt artifacts;
- purpose-discriminator schema and admission tests proving live Release and
  simulation may reuse mechanical shapes only while rejecting every
  cross-purpose Snapshot, Fact Bundle, artifact, and record;
- generic Approval Outcome Evidence binding helpers only for platforms with
  documented exact attempt-bound proof, plus first-slice negative tests proving
  GitHub Deployment Review data and review-ID deltas cannot create such Evidence
  or grant Capability;
- platform conclusion and phase-state binding helpers plus tests distinguishing
  pre-capability no-side-effect cancellation/expiry from cancellation after a
  capability job may have started, without requiring a distinction GitHub does
  not expose;
- acceptance tests for every scenario in this MLD.
