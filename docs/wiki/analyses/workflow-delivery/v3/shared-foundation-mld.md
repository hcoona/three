# Workflow Delivery v3 Shared Foundation MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This middle-level design defines the reusable mechanism layer used by CI
Qualification and Release Delivery. It covers canonical records and digests,
Artifact References, Repository Model compilation, ecosystem Providers,
mechanism Definitions, Build and Quality Adapters, execution bindings,
mechanical outcomes, and generic Git, GitHub, registry, and process clients.

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
- a universal Plan, Evidence, Release, or record wrapper;
- workflow orchestration;
- scheduling or retry;
- CI or Release policy;
- approval or authorization;
- GitHub Environment selection;
- publication credentials;
- finalization or verdicts; or
- a durable service database.

CI Qualification and Release Delivery call Foundation mechanisms but retain
complete ownership of their scope, Plans, authoritative records, state
transitions, and outcomes.

## Scope

This MLD owns:

- deterministic canonicalization and digest primitives;
- strict parsing and exact binding helpers;
- stable identity and context-discriminator value types;
- shared Artifact Reference and internal provenance primitives;
- static-reference source-reading mechanisms;
- Repository Model Provider contracts;
- target-bound Fact Bundle contracts;
- the read-only Repository Model Compiler;
- Build, Quality, and other mechanism Definition schemas and static catalogs;
- Build and Quality Adapter contracts;
- family-specific Invocation and mechanical Result contracts;
- execution-class and generic Publication Capability declarations;
- closed mechanical outcome and error taxonomies;
- transparent non-authoritative cache semantics;
- generic Git, GitHub, registry, HTTP, artifact, and process clients;
- selective versioning for intentional cross-revision exchange; and
- conformance and contract-test expectations.

This MLD does not own:

- CI affected-scope or required/advisory policy;
- Release Unit or channel policy;
- Buddy, Official, Execution, or Attempt identity;
- logical destination projection selection;
- destination Observation classification;
- Publication Action planning;
- Approval Bundle, Publication Authorization, Publication Result, or Attempt
  Outcome semantics;
- mutable-resource concurrency policy;
- new-dispatch retry;
- Break-Glass Remediation policy;
- job topology or matrix partitioning;
- GitHub Environment configuration or approval;
- OIDC or credential grant;
- an Environment Profile abstraction;
- a first-slice second publication Environment;
- a separate post-approval admission record;
- capability-group manifest or result wrappers;
- exhaustive Actions history discovery or admission; or
- runtime plugin loading.

## Governing Principles

1. Share mechanisms only when their inputs, outputs, and semantics are genuinely
   common.
2. Foundation never chooses business scope, policy, authority, or verdict.
3. Providers resolve facts and capabilities; Adapters execute closed
   mechanical invocations.
4. A universal plugin, executor, Plan, Release, Evidence, or record envelope is
   not introduced.
5. Contexts form and admit authoritative records from mechanical results.
6. Same-revision static registration is preferred over dynamic extension.
7. Target-influenced evaluation never runs in an authoritative pure-control
   process.
8. Platform credentials are granted outside Foundation and injected only into
   the runtime boundary that needs them.
9. Cache changes performance only; it never changes identity, scope, Evidence,
   provenance, or verdict.
10. Cross-revision compatibility is introduced only for a concrete
    cross-revision consumer.
11. Normal-Live `github.run_attempt` omission is contextual, not a universal
    removal from simulation or other execution contracts.
12. Future OIDC or multiple-action abstractions require a concrete second
    scenario rather than speculative symmetry.

## Ownership Map

| Concern                          | Shared Foundation           | CI Qualification          | Release Delivery               | Delivery Governance        |
| -------------------------------- | --------------------------- | ------------------------- | ------------------------------ | -------------------------- |
| Canonicalization and digest      | Owns mechanism              | Uses                      | Uses                           | Does not own               |
| Repository facts                 | Discovers and compiles      | Consumes for impact       | Consumes for complete closure  | Does not own               |
| Definition schemas and catalogs  | Owns mechanism contracts    | Selects through CI policy | Selects through Release policy | Protects changes           |
| Build and Quality execution      | Executes closed invocations | Schedules and admits      | Schedules and admits           | Protects permissions       |
| Artifact identity and provenance | Defines primitives          | Admits CI artifacts       | Admits Release artifacts       | Protects access            |
| Static-reference file access     | Defines source mechanisms   | Selects feedback use      | Selects eligibility use        | Protects policy            |
| Destination semantics            | Generic clients only        | Does not own              | Owns ports and Adapters        | Grants authority           |
| Approval and publication records | Canonical helpers only      | Does not own              | Owns                           | Supplies authority outcome |
| Scheduling and retry             | Hints only                  | Owns                      | Owns                           | Does not own               |
| Publication Capability           | Declares requirement shape  | Has no grant              | Requests in governed runtime   | Owns grant and denial      |
| Final verdict                    | Does not own                | Owns CI Decision          | Owns Release outcome           | Does not create            |

## Logical Module Decomposition

The architecture defines logical modules without requiring one package per
module.

### Record Primitives

Record Primitives provide:

- deterministic canonical JSON or equivalent serialization;
- content digest computation;
- strict parsing and unknown-field rejection;
- immutable identity value types;
- explicit context and purpose discriminators;
- exact Git target, workflow run, producer, Definition, request, artifact, and
  result binding helpers;
- context-selectable `github.run_attempt` binding where required;
- timestamp and diagnostic-reference primitives; and
- reusable exact-admission checks.

They do not define a universal record envelope.

CI Plans, CI Evidence, CI Decisions, Release Snapshots, Release Evidence,
Observation Records, Approval Bundles, Publication Authorizations,
Publication Results, Attempt Outcomes, Reconciliation Records, and Remediation
Records remain context-owned schemas.

Foundation may canonicalize or validate those records through typed
family-specific helpers. It does not choose their business meaning.

### Contextual Run Binding

Run-attempt binding is selected by the owning execution contract.

For normal Live:

- every authoritative job independently requires
  `github.run_attempt == 1`;
- current-Attempt records and Artifact References bind
  `workflow_run_id`, target, purpose, producer, and digest; and
- `github.run_attempt` is omitted from domain identity, authoritative record
  fields, artifact bindings, and Publication Authorization.

For simulation:

- the Repository Model Snapshot and Simulation Identity retain
  `github.run_attempt`; and
- a rerun is a distinct simulation pass.

CI and other existing contexts retain their own approved run-attempt contracts.
Foundation must not erase or infer those bindings through a universal default.

### Artifact and Provenance Primitives

Artifact primitives define:

- logical artifact identity;
- content digest, size, and media or package kind;
- immutable transport identity;
- producer invocation identity;
- target and purpose binding;
- expected output role;
- internal provenance; and
- artifact-set manifests.

They do not decide whether an artifact is admissible for CI or Release.

For package formats that require durable target attribution, Foundation may
provide canonical target-witness encoding and parsing. The first-slice npm
witness is `workflow-delivery/provenance.json` inside the tarball and binds:

- target;
- Release Unit;
- canonical and native NBGV facts;
- Build Definition;
- catalog and control digests;
- purpose; and
- schema.

It excludes run and Attempt identity so repeated builds of one target can
remain deterministic. The selected Release Unit and Build Definition freeze
whether the witness is required and its exact contract. Build and Quality
Adapters only execute and verify that requirement. Release policy carries the
frozen requirement into desired state, and Release-owned Destination Adapters
classify observed state against it; they do not redefine artifact semantics.

### Static-Reference Mechanisms

Foundation may provide deterministic enumeration and byte-reading mechanisms
for the approved static-reference source kinds:

- `git-target`: exact blobs from an explicit full commit SHA;
- `index`: stage-0 Git index entries; and
- `worktree`: tracked plus eligible untracked filesystem paths.

The mechanical result binds source kind, exact target when applicable, policy
ID and digest, sorted exact ecosystem-authority implementation identities
actually loaded, result, canonical error kind when result is error, and sorted
findings.
Index and worktree bytes are never represented as `HEAD` or commit identity.

The typed invocation boundary rejects an omitted or unknown source kind and
malformed required source parameters before constructing a mechanical result.
For an admitted source request, inability to deterministically enumerate, read,
or minimally materialize the declared exact source returns
`source-acquisition-failed`.

Foundation may also provide the isolated exact-source snapshot transport and
typed adapter envelope used by the LLD's Ecosystem Authority Graph. The graph
owns foreign ecosystem models through authoritative artifacts, official
libraries or CLIs, and published standards; Foundation owns only source
binding, snapshot closure, envelope validation, repository path normalization,
and canonical result construction.

Foundation does not decide whether a finding blocks CI feedback or Live
eligibility. It does not claim exhaustive consumer discovery, dataflow
analysis, interpreter behavior, or a universal scanned-surface digest.

### Repository Model Mechanisms

Repository Model mechanisms include:

- Provider contracts;
- Provider Results and Provider Request Manifests;
- Fact Bundles;
- strict fact admission;
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
- generic client definitions.

Release-owned Destination Definitions and Destination Adapters are not part of
Shared Foundation. They may consume Foundation clients and value types.

### Build and Quality Adapters

Build and Quality Adapters execute family-specific invocations.

They do not:

- add themselves to a Plan;
- choose required or advisory status;
- select a Release channel;
- schedule work;
- authorize a side effect;
- classify a Release projection;
- emit a Publication Result; or
- produce a final business verdict.

### Execution and Client Primitives

Execution primitives provide:

- family-specific Invocation bindings;
- execution-class declarations;
- capability-requirement descriptions;
- environment and toolchain identity;
- batching compatibility hints;
- cache identity hints;
- timeout and cancellation inputs;
- normalized mechanical outcomes; and
- generic API and process clients.

They do not create jobs, Environments, permissions, credentials, matrices,
concurrency groups, or approval policy.

When Release supplies a closed platform serialization projection, Foundation
may canonicalize, digest, and round-trip validate it. Foundation does not decide
whether the projection safely serializes overlapping mutable-resource keys and
does not treat its digest as a substitute for the complete key set.

## Shared Record Boundary

### No Universal Record Envelope

Fields that look mechanically similar can carry different authority and
lifecycle semantics:

- CI Evidence belongs to one candidate and obligation;
- Release Evidence belongs to one live Attempt and obligation;
- an Observation Record binds one Attempt, desired-state basis, and remote
  facts;
- a Publication Authorization binds one action-bearing current Attempt;
- a Publication Result records one controlled action outcome and available
  normalized post-action facts.

A controlled failed Publication Result may retain available normalized
post-action evidence but does not satisfy the complete Release-owned
`published` predicate. Foundation validates this shape only through a
Release-owned schema; it does not create a universal result wrapper.

### Strict Validation

Every Foundation record parser or admission helper:

- rejects unknown fields unless the family contract explicitly allows an
  extension map;
- rejects duplicate or ambiguous identities;
- validates canonical encoding before digest comparison;
- validates required target, purpose, producer, Definition, request, artifact,
  and result bindings;
- applies the owning context's run-attempt contract;
- rejects cross-purpose and cross-context inputs;
- rejects conflicting duplicates; and
- returns a typed mechanical failure rather than silently defaulting.

Strict validation is a mechanism. The calling context decides whether failure
blocks planning, fails an obligation, requires reconciliation, or changes an
outcome.

For foreign ecosystem semantics, strict Foundation validation starts at the
normalized authority-fact envelope. The exact Ecosystem Authority Graph owns
its manifest, lock, descriptor, locator, workspace, action, or language model
through authoritative artifacts, official libraries or CLIs, and published
standards, including syntax, comments, quoting, duplicate handling, case rules,
and normalization. Foundation must not recreate the source schema, run a
competing authority for cross-validation, reject an official normalized model
because a local implementation disagrees, or add defensive checks for
invariants guaranteed by the selected graph. `source-acquisition-failed`,
`encoding-rejected`, `authority-rejected`, `authority-execution-failed`,
`unsupported-projection`, and `authority-mismatch` remain distinct typed
failures. Required Session-owned snapshot or scratch cleanup adds
`cleanup-failed`.

### Current-Context Admission

Foundation provides strict helpers for current-context admission when the
mechanics are shared across CI and Release.

For a normal-Live current-Attempt artifact, admission verifies:

- immutable artifact ID;
- transport digest;
- record kind and payload digest;
- current `workflow_run_id`;
- exact target and purpose;
- expected producer and output role; and
- context-owned lineage digests.

It does not search prior runs, select by name, use latest-artifact fallback, or
admit an earlier Attempt. Native history may be returned as raw diagnostics by
a generic client, but Foundation defines no exhaustive history Snapshot,
history-admission mode, historical record authority, or aggregate Execution
reconstruction.

### Authoritative Record Formation

Providers and Adapters emit family-specific mechanical Results.

The calling context then:

1. binds the Result to its Plan or Attempt;
2. binds the exact obligation, Observation, or action identity;
3. binds producer and current execution context;
4. binds artifacts and diagnostics;
5. canonicalizes and digests the record; and
6. persists the context-owned authoritative record.

The context Finalizer performs Evidence, Observation, or Result admission.
Foundation does not run a shared Finalizer.

## Selective Contract Versioning

### Same-Revision Internal Records

Internal records whose producers and consumers run from the same revision and
execution context do not require a universal API version.

Their exact shape is bound by:

- same-revision producer and consumer code;
- context and purpose;
- exact target;
- current execution identity;
- Definition and request digests; and
- strict schema and digest checks.

### Cross-Revision Exchange Contracts

A contract intentionally produced by one revision and consumed by another must
contain:

- stable `kind`;
- explicit `contract-version`;
- producer repository, workflow, job, and revision;
- original domain lineage;
- canonical payload digest; and
- compatibility constraints.

The established architectural example is a future reconciliation or
remediation request from an older Release Attempt consumed by separately
approved protected Break-Glass code. The first-slice Normal-Live
implementation has no cross-revision consumer.

Compatibility rules are explicit:

- only declared compatible versions are accepted;
- unknown major versions fail closed;
- migration preserves the original payload and appends the transformed form;
- current code never guesses a missing version; and
- compatibility code is owner-reviewed.

This exception does not create a universal version field or a universal Release
wrapper.

## Definition and Catalog Model

### Stable Logical Identity

Every Definition has a stable logical ID. Exact behavior is frozen through:

- Definition Snapshot;
- Definition digest;
- selected implementation identity;
- implementation or catalog digest;
- toolchain constraints; and
- normalized parameters.

The logical ID alone is insufficient to reconstruct historical behavior.

### Static Same-Revision Catalog

The initial design uses a statically registered catalog in the same revision as
the calling Planner and Finalizer.

It does not support:

- dynamic code paths from descriptors;
- remote plugin download;
- runtime package discovery;
- an external plugin marketplace;
- unreviewed adapter scripts; or
- a stable cross-version plugin ABI.

Descriptors and policies may select only allowlisted logical IDs and
parameters.

### Definition Versus Policy

A Definition states mechanical semantics:

- accepted input type;
- parameter model;
- dimensions;
- toolchain;
- outputs;
- execution class;
- capability requirement;
- mechanical prerequisites; and
- Result contract.

It does not decide whether the Definition applies, whether it is required,
which channel selects it, which Release Unit is delivered, whether a
Publication Action is authorized, or how a failure affects the business
verdict.

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
- supporting test and aggregate-target discovery;
- dimensions and runner constraints;
- build and packaging capability discovery;
- target-bound NBGV facts when assigned; and
- unresolved or conflicting fact reporting.

A Provider does not compute final CI affected scope, create obligations, select
Release Units or variants, execute Build or Quality Definitions, choose a
channel, or emit a business verdict.

### Provider Execution Modes

Every Provider emits a Provider Result binding:

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
- executes no target-defined hooks or scripts;
- may run in an authoritative pure-control process; and
- returns its Result directly to Repository Model compilation.

#### Target-Evaluating Provider

A target-evaluating Provider may invoke an ecosystem tool influenced by
target-controlled content.

It runs in an unprivileged job that:

- has no publication capability or destination secret;
- cannot write protected repository state;
- receives the exact target;
- receives a closed Provider request with current context bindings; and
- emits an immutable target-bound Fact Bundle.

When a Provider owns NBGV facts dependent on Git history, it materializes the
exact target with complete ancestry and tags, verifies that the checkout remains
pinned, and fails before NBGV invocation if completeness cannot be proved.

### Fact Bundle

A Fact Bundle binds:

- complete Provider Result and digest;
- target-bound canonical and native NBGV facts when owned by the Provider;
- request identity and purpose;
- `workflow_run_id`;
- target, producer job, and control identity;
- request artifact and digest;
- immutable transport identity; and
- Bundle digest.

For normal Live it omits `github.run_attempt`; the producing and consuming jobs
must independently satisfy the attempt-1 guard. For simulation it includes the
simulation run-attempt binding. CI follows its own contract.

A Fact Bundle contains no CI or Release policy.

### Provider Determinism

For the same target, Provider implementation, request, toolchain, and
authoritative dependency metadata, a Provider emits canonically equivalent
facts.

Time, branch display name, workflow URL, cache availability, or unrelated
environment state must not change semantic facts.

When a required fact cannot be established, the Provider returns
`unsupported`, `unknown`, or `conflicting` rather than inventing a default or a
narrower graph.

## Repository Model Compiler

The Repository Model Compiler is a shared, read-only mechanism.

It consumes:

- exact target identity;
- Release Unit declarations;
- closed Provider Request Manifest;
- direct pure-Provider Results;
- Provider Results from admitted Fact Bundles;
- static Definition catalogs;
- NBGV facts;
- declared extra inputs; and
- repository path-policy facts.

It emits one immutable Repository Model Snapshot containing:

- exact target, purpose, caller request, `workflow_run_id`, producer, and
  control bindings;
- context-selected `github.run_attempt` binding when required;
- purpose-required selected channel and Release Unit;
- sealed Provider Request Manifest identity and digest;
- the complete ordered terminal Provider Result identity-and-digest closure;
- admitted Fact Bundle payload and immutable transport identities and digests
  where target-evaluating Providers are used;
- Project Nodes and dependency facts;
- path ownership and global-input facts;
- capabilities and dimensions;
- Release Units and artifact variants;
- Build Definition references;
- build and declared-input closure;
- canonical and native NBGV facts;
- reverse indexes; and
- explicit unresolved or conflicting facts.

A ready Snapshot closes every required descriptor, Project Node, dependency,
Build Definition, modeled output, version fact, build scope, and artifact
scope. Missing, unknown, or conflicting facts produce blocking state rather
than a partial ready Snapshot.

For a normal-Live request, Release compiles one Snapshot before Execution
lookup and reuses it through the resulting Attempt. The Snapshot does not bind
`github.run_attempt`. Every authoritative producer and consumer independently
enforces attempt 1.

For simulation, Release compiles one Snapshot per simulation pass and binds
`github.run_attempt`, selected channel, and Release Unit. The Snapshot does not
bind a future Simulation Identity; Release derives that identity only after
validation.

CI independently compiles its own context-bound Snapshot. Cross-purpose,
other-request, and prior-Attempt Snapshots are rejected.

The compiler validates and records NBGV facts produced by the NBGV-owning
Provider. It does not invoke NBGV or recompute canonical or native version
facts.

### Provider Request Manifest

Before target-evaluating discovery, the compilation coordinator closes one
Provider Request Manifest.

It binds:

- exact target;
- caller request, purpose, `workflow_run_id`, producer, and control identities;
- context-required `github.run_attempt` when applicable;
- purpose-required selected channel and Release Unit;
- static catalog digest;
- every expected Provider and implementation identity;
- execution mode;
- request ID and digest;
- discovery basis;
- expected terminal Result identity; and
- manifest digest.

Once sealed, no Provider may be added or removed. Compilation requires exactly
one terminal Result for every entry. Missing, duplicate, conflicting, or
unexpected Results block the Snapshot.

The compiler does not select CI scope, quality policy, Release channel,
destination projections, Publication Actions, approval, or authority.

## Adapter Model

### Family-Specific Interfaces

Build and Quality use separate Adapter interfaces.

The architecture does not define one universal:

- plugin interface;
- Invocation payload;
- Result payload;
- retry model;
- exit-code interpretation; or
- artifact contract.

One ecosystem package may implement several interfaces, but each remains
separately invocable and permissioned.

### Build Adapter

A Build Adapter receives a closed Build Invocation containing:

- exact target;
- Release Unit and artifact-variant identity;
- Build Definition Snapshot and digest;
- Build Request digest;
- exact selected native NBGV projection and source-fact binding;
- dimensions;
- declared toolchain and inputs;
- expected output roles;
- execution class;
- cache hints; and
- producer binding inputs.

It emits a Build Result containing:

- mechanical outcome;
- materialized outputs;
- content digests and sizes;
- output-role mapping;
- toolchain identity;
- provenance inputs;
- cache diagnostics;
- producer identity; and
- diagnostic reference.

The Adapter applies and verifies the frozen version projection. It must not
recompute NBGV, derive a substitute, or fall back to ambient manifest state.

### Quality Adapter

A Quality Adapter receives a closed Quality Invocation containing:

- exact target;
- Quality Definition Snapshot and digest;
- request and obligation bindings;
- concrete target and dimensions;
- runner and toolchain constraints;
- prerequisite outputs;
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
- optional measurements.

It does not know whether an obligation is required or advisory and does not emit
CI or Release Evidence directly.

### Destination Boundary

Release Delivery owns Destination ports and Adapters, including:

- projection Observation and classification;
- publication operation semantics;
- action formation;
- complete mutable-resource keys;
- Publication Result and successful-evidence meaning;
- new-dispatch recovery; and
- remediation operations.

Foundation may provide generic clients for:

- anonymous or authenticated HTTP;
- Git and GitHub API access;
- registry API access;
- retryable transport;
- response canonicalization;
- digest parsing;
- artifact streams; and
- capability-requirement declaration.

Generic clients expose facts and responses. They do not classify a projection,
plan a Publication Action, decide whether exact state is sufficient, create a
Publication Authorization, or emit a Publication Result.

## Invocation and Execution Model

### Closed Invocation

Before execution, the calling context closes the semantic request.

An Invocation identifies:

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

An Adapter may perform mechanical discovery inside the selected operation, but
it cannot add a new obligation, variant, projection, or publication action.

### Context-Owned Scheduling

CI and Release own:

- DAG construction;
- prerequisite semantics;
- ready-work selection;
- batching;
- matrix partitioning;
- fail-stop;
- retry;
- supersession;
- skip behavior; and
- final aggregation.

Foundation may emit a compatibility key for invocations sharing Adapter,
toolchain, runner, dimensions, prerequisites, and cache or workspace needs.
Batching must preserve each Invocation and Result identity.

### Execution Classes

Initial execution classes are:

- `authoritative-pure`;
- `unprivileged-target-evaluation`;
- `unprivileged-target-execution`;
- `read-only-remote-observation`;
- `privileged-side-effect`; and
- `privileged-remediation`.

Foundation declares the class and minimum capability requirements. The calling
context and Delivery Governance create the job, permissions, Environment,
identity trust, and credential grant.

### Publication Capability Declaration

Publication Capability is a generic declaration of the minimum external
authority required by one closed privileged Invocation.

The declaration may bind:

- destination family;
- allowed origin, audience, or resource;
- operation class;
- minimum GitHub permission;
- OIDC requirement when applicable; and
- credential lifetime expectations.

Foundation does not:

- grant the capability;
- select an Environment;
- decide approval;
- broaden permissions;
- find ambient fallback credentials;
- substitute a PAT;
- create a capability group; or
- retry through another authority path.

For the first-slice publisher, Release and Governance inject the short-lived
repository token only after the Approval job emits Publication Authorization.
Foundation defines no second Environment or separate admission decision.

A future OIDC Environment or multiple-action capability model requires a
concrete second scenario and external trust claim contract.

## Mechanical Outcomes and Diagnostics

### Closed Outcome Taxonomy

Foundation families use a closed mechanical taxonomy:

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
- CI success or failure;
- Release exact-satisfied or published;
- Release reconciliation-required;
- publication authorized; or
- final Attempt outcome.

### Diagnostics

Every non-success Result includes:

- stable category;
- human-readable summary;
- machine-readable detail code;
- diagnostic reference when available;
- relevant target, Definition, request, and producer bindings; and
- whether output or remote mutation may have occurred.

Diagnostics never substitute for a required Result or authoritative record.

## Artifact Identity and Provenance

### Identity Layers

Foundation separates:

1. logical identity;
2. content identity;
3. immutable transport identity; and
4. producer identity.

No layer substitutes for another.

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
- producer job and `workflow_run_id`;
- context-required `github.run_attempt` only when applicable;
- output role;
- content identity; and
- transport identity.

CI and Release may reuse the structure while applying independent admission.
Matching bytes do not make a CI artifact admissible for Release.

### Artifact Materialization

Actions artifact names are non-authoritative indexes. Uploads use
collision-safe names within the workflow run and disable overwrite.

Producers capture:

- artifact ID;
- digest;
- URL;
- producer;
- target;
- purpose; and
- payload identity.

Consumers retrieve by immutable ID, recompute content digest and size, validate
manifest and provenance, and reject missing, extra, or conflicting outputs.

Normal-Live artifacts omit `github.run_attempt`. Simulation and other contexts
retain it where their contracts require it.

Name fallback, latest selection, and history-based artifact authority are not
provided.

## Git and GitHub Client Primitives

Foundation clients may provide bounded mechanical operations for:

- resolving a fully qualified Git ref;
- reading an exact blob by repository, ref, and path;
- enumerating commits that touch one path between two lineage points;
- validating exact target checkout and complete ancestry or tags;
- retrieving an artifact by immutable ID;
- reading current workflow, run, job, deployment, or Environment facts;
- validating supported REST response schemas;
- reading native Environment settings for Governance attestation;
- reading current destination facts; and
- returning native run history as non-authoritative diagnostics.

The protected-path helper returns mechanical lineage and content facts.
Release owns the decision that any path touch invalidates an Attempt and that
unrelated `main` commits are allowed.

The Environment client returns native reviewer, self-review, bypass, branch or
tag, wait, variable, and secret facts that the API or authenticated readback can
establish. Governance owns the approved configuration and attestation. Runtime
marker validation does not replace this readback.

The client layer does not:

- claim exhaustive package-grant enumeration;
- construct a history-admission Snapshot;
- infer an artifact-to-prior-attempt edge;
- reconstruct aggregate Release state;
- infer approval or authorization from diagnostics; or
- blind-redispatch after an ambiguous workflow-dispatch response.

## Cache and Mechanical Reuse

### Transparent Shared Cache

CI and Release may share non-authoritative caches for:

- verified tool downloads;
- package-manager downloads validated by lock or package metadata;
- immutable dependencies;
- content-addressed intermediates whose key closes semantic inputs; and
- compiler outputs only when writer trust and provenance are acceptable.

Cache keys may include:

- target or input digests;
- Definition and request digests;
- toolchain;
- dimensions;
- platform;
- dependency metadata; and
- Adapter-specific compatibility data.

Cross-context cache use additionally binds producer trust class, repository and
workflow identity, namespace, complete input closure, Invocation digest, and
material provenance.

A pull request or lower-trust job cannot populate a compiler or intermediate
namespace consumed by Release. A cache miss must permit rederivation from
authoritative inputs.

### Release Independence

Release still:

- performs its own Build Invocation;
- materializes final outputs;
- recomputes digests;
- creates Release-purpose provenance;
- performs Release quality obligations; and
- admits only current Release Attempt records.

A cache hit does not import a CI, simulation, other-request, or prior-Attempt
Snapshot, Artifact Reference, Evidence, success, or Result.

## Security Model

### Static Supply-Chain Boundary

Executable Provider, Adapter, compiler, canonicalization, and client code comes
from the selected revision and static catalog.

Definitions and descriptors may select allowlisted IDs and parameters but
cannot inject executable paths, packages, commands, or remote code.

Static-reference authority implementations are exact-version dependencies or
toolchain nodes selected by the static catalog, not target-selected plugins.
File-oriented libraries and CLIs see only a Session-owned isolated snapshot
containing declared exact-source bytes and controlled environment. They receive
no publication capability, registry credential, undeclared worktree input, or
external writable cache.

### Target-Controlled Input

Target-controlled manifests and configuration are untrusted inputs.

Pure Providers do not execute target code. Target-evaluating Providers, Build
Adapters, and Quality Adapters run only in unprivileged classes.

No runtime that executes target-defined product or build code receives
publication capability.

The first-slice Buddy exception permits target-revision publisher control code
after Approval Environment review. That publisher remains a
`privileged-side-effect` Invocation and validates exact bindings by contract,
but it is not an independent boundary against the accepted writer TCB.
Foundation does not generalize the exception to Official or another
destination.

### Boundary Validation

Every process boundary validates:

- exact input identity and digest;
- allowed Definition and implementation ID;
- execution class;
- expected artifact inputs;
- producer binding;
- Result shape;
- output digests; and
- absence of undeclared outputs where closure requires it.

An exit code of zero is not sufficient authority.

### Generic Client Safety

Credential-bearing clients:

- accept endpoint and operation only from a closed Invocation and static
  destination configuration;
- receive capability only through the governed runtime;
- bind capability to allowed origin, audience or resource, identity, and
  operation class;
- refuse credentials outside that binding;
- reject credential-bearing cross-origin redirects;
- never log secrets;
- do not infer a stronger operation;
- expose conflict and unknown state explicitly;
- do not enable destructive overwrite by default; and
- return typed mechanical responses for context-owned interpretation.

## Failure Conditions

Foundation fails closed when:

- an implementation ID is absent or ambiguous;
- a descriptor attempts dynamic code selection;
- a Definition, request, or record is malformed;
- canonicalization or digest verification fails;
- a target, purpose, producer, request, artifact, or Result binding mismatches;
- a normal-Live record contains or requires `github.run_attempt` as authority;
- a simulation record omits or mismatches its required run-attempt binding;
- a pure Provider attempts target evaluation;
- a target-evaluating Provider lacks an unprivileged boundary;
- a Provider Request Manifest changes after sealing;
- an expected Provider Result is missing, duplicate, conflicting, or
  unexpected;
- required Repository Model facts cannot close;
- an Adapter receives unsupported inputs or dimensions;
- an Adapter emits missing, extra, or conflicting outputs;
- a Build Adapter recomputes or substitutes the frozen version;
- artifact content differs from manifest or provenance;
- a context attempts to admit another context's artifact or Result;
- a live Release and simulation input are mixed;
- a current-context helper attempts prior-run or latest-artifact admission;
- an untrusted cache entry is offered to a higher-trust consumer;
- a capability requirement cannot be satisfied;
- a credential-bearing request escapes its capability binding;
- a client attempts credential fallback;
- an unknown mechanical outcome is returned;
- a cross-revision contract is absent, unknown, or incompatible; or
- a required mechanical Result cannot be persisted.

Foundation does not convert any condition into a weaker operation.

## Acceptance Scenarios

### Target-Evaluating Provider

A Provider needs ecosystem-native graph evaluation.

- It declares `unprivileged-target-evaluation`.
- The job receives the exact target and closed request.
- It has no publication capability or Environment.
- It emits a target-bound Fact Bundle.
- The compiler strictly admits the Bundle and normalizes repository facts.

### Normal-Live Fact Bundle

A normal-Live Provider emits a Fact Bundle.

- The Bundle binds current `workflow_run_id`, target, purpose, producer, and
  digest.
- It omits `github.run_attempt`.
- Producer and consumer independently require attempt 1.
- A partial GitHub rerun fails at the job guard rather than creating a second
  domain identity.

### Simulation Fact Bundle

A simulation rerun executes.

- The Bundle and Snapshot bind the new `github.run_attempt`.
- The new pass derives a distinct Simulation Identity.
- No normal-Live contraction is applied.

### Shared Build Definition

CI and Release invoke the same Build Definition.

- Their context, purpose, target, Plan, and producer bindings differ.
- Both may use admissible non-authoritative caches.
- Each rematerializes output and recomputes digests.
- CI forms CI provenance and Evidence.
- Release forms Release provenance and Evidence.
- Neither context admits the other's artifact identity.

### Static-Reference Sources

The same scanner mechanism is used in three source modes.

- `git-target` reads exact commit blobs and may support Live Eligibility.
- `index` reads stage-0 entries for staged feedback.
- `worktree` reads eligible filesystem bytes for manual feedback.
- Every Result binds its source kind.
- File-oriented ecosystem APIs receive a minimal isolated snapshot made only
  from those exact bytes; the source mode, not the snapshot, remains authority.
- The exact authority graph emits normalized facts before Foundation applies
  repository-specific prohibited-form comparison.
- Foundation reports findings but does not decide business eligibility.

### Approval and Publication Records

Release forms an action-bearing Approval Bundle and later Publication
Authorization.

- Foundation canonicalizes and validates typed bindings.
- It does not select the Approval Environment or grant capability.
- A successful Publication Result validates the complete Release-owned
  `published` predicate.
- A controlled failed Result may retain available normalized post-action
  evidence but cannot satisfy that predicate.
- No generic group result wrapper is introduced.

### Protected Governance Freshness

Release asks Foundation clients for current protected-path facts.

- The client returns repository, ref, path, resolved commit, blob/content
  identity, and path-touch lineage.
- An unrelated `main` commit does not alter the blob or path lineage.
- A change-then-revert still reports a protected-path touch.
- Release, not Foundation, invalidates the current Attempt.

### Future Cross-Revision Remediation Contract

A future separately approved protected remediation implementation may consume
an older reconciliation request. The first-slice Normal-Live implementation
does not.

- The request has stable kind, contract version, producer identity, original
  lineage, and payload digest.
- That future implementation accepts only declared compatible versions.
- Incompatible input fails before approval or mutation.
- Any migration preserves the original payload.

### Cache Unavailable

Shared caches are unavailable.

- Provider, Build, and Quality requests remain unchanged.
- Adapters retrieve authoritative dependencies and run normally.
- Artifact identity, provenance, Evidence, and verdict semantics remain
  unchanged.
- Only elapsed time and diagnostics differ.

### Credential-Bound Client

A faulty Invocation names an endpoint outside the allowed destination origin.

- The client refuses to attach the credential.
- It rejects a cross-origin redirect.
- No authorized request reaches the unbound endpoint.
- Release records the typed mechanical failure through its own Result.

## Conformance and Testing

Every Foundation implementation requires:

- strict record-parser and admission tests;
- canonicalization and digest golden tests;
- context and purpose isolation tests;
- normal-Live no-`run_attempt` and simulation run-attempt binding tests;
- Provider fixtures and target-binding negative tests;
- Repository Model ready-versus-blocked scenarios;
- exact-target NBGV ancestry and tag completeness tests;
- Build Adapter artifact-manifest and frozen-version tests;
- Quality Adapter Result-shape tests;
- Artifact Reference ID-only transport tests;
- static-reference source-kind tests;
- static-reference exact-authority identity, snapshot-isolation, normalized
  fact, and typed authority-failure tests;
- Git protected-path touch and change-then-revert fixtures;
- current-context admission tests that reject prior-run and latest selection;
- cache-disabled equivalence scenarios;
- execution-class and capability-denial tests;
- generic client credential-boundary tests;
- cross-revision compatibility tests where applicable; and
- integration tests against actual ecosystem or platform abstractions.

Contract tests validate lower-layer behavior once at the implementation
boundary. Runtime planning relies on the accepted contract rather than
reimplementing the platform.

## Deferred LLD Decisions

Lower-layer design may define:

- logical package and executable decomposition;
- exact canonicalization and digest algorithms;
- strict value types and family-specific schemas;
- context-selectable run-attempt binding representation;
- Provider Result, Fact Bundle, Request Manifest, and Repository Model
  Snapshot schemas;
- static catalog registration;
- exact target-pinning and complete-history checks;
- Build and Quality Invocation and Result schemas;
- Artifact Reference, manifest, and provenance schemas;
- collision-safe artifact naming and ID-only transport;
- static-reference enumeration and parsing implementations;
- protected-ref, protected-path history, and Environment readback clients;
- supported workflow-dispatch response validation;
- generic GitHub, registry, HTTP, and process client surfaces;
- cache keys and trust namespaces;
- batching compatibility keys;
- cross-revision contract compatibility;
- mechanical outcome and diagnostic codes; and
- tests for every scenario and failure condition above.

Lower-layer design must not add a universal Release wrapper, first-slice
Environment Profile, separate post-approval admission abstraction,
capability-group manifest or result wrapper, Actions history admission, or
future OIDC or multiple-action abstractions without a concrete second scenario.
