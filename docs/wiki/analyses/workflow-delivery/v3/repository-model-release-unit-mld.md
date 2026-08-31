# Workflow Delivery v3 Repository Model and Release Unit MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This middle-level design defines how Workflow Delivery discovers technical
repository facts, authors Release Units, resolves build semantics, and compiles
an immutable Repository Model Snapshot.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md) and
[High-Level Design](./high-level-design.md). Exact file syntax, schemas, command
lines, and package decomposition remain lower-layer decisions.

## Scope

This MLD owns:

- Release Unit discovery and product/build authoring;
- Project Node and dependency fact discovery;
- Build Definition and artifact variant semantics;
- first-slice Release Unit determinism support;
- NBGV version authority resolution;
- context-owned request and run binding for Repository Model records;
- Repository Model compilation and validation; and
- the graph needed by later CI affected-selection and Release planning.

This MLD does not own:

- CI required or advisory obligation policy;
- Buddy or Official destination selection;
- branch eligibility, approval, OIDC, or Publication Capability;
- exact changed-path classification rules for each ecosystem;
- workflow job topology; or
- adapter command-line implementation.

## Design Principles

1. Release Units are explicit business objects.
2. Project Nodes and dependency relationships are discovered technical facts.
3. Ecosystem-native metadata remains authoritative for ecosystem semantics.
4. Authoring does not duplicate facts already owned by project manifests or
   build systems.
5. A model abstraction requires concrete independent behavior, identity,
   lifecycle, or policy responsibility.
6. Unknown or conflicting required facts block model compilation.
7. Adapter correctness is validated during acceptance rather than repeatedly
   re-proved through expensive runtime inspection.
8. Request and run-attempt fields follow the owning CI, normal-Live, or
   simulation contract rather than one universal Repository Model schema.
9. First-slice npm determinism is a Release Unit and Build Definition support
   boundary, not duplicate-build certification.

## Model Overview

```text
target Git tree
  |
  +-- fixed-basename Release Unit descriptors
  |      -> Release Unit declarations
  |
  +-- ecosystem manifests and workspace configuration
  |      -> Repository Model Providers
  |      -> Project Nodes / dependency facts / build capabilities
  |
  +-- NBGV version.json lineage
         -> canonical version and required native ecosystem projections

Release Unit declarations
  + complete Provider Request Manifest
  + terminal Provider Results
  + target-bound NBGV facts
  -> Repository Model Compiler
  -> immutable Repository Model Snapshot
```

The Repository Model contract and compiler are shared technical mechanisms for
CI and Release. Runtime Snapshot instances remain context- and request-bound;
live Release does not import a CI, simulation, other-request, or prior-Attempt
Snapshot. A Snapshot contains no CI Plan, Release Plan, Evidence, authorization,
or business verdict.

The Repository Model Compiler and Provider contracts are Shared Foundation
mechanisms. This MLD defines their repository and Release Unit semantics; the
[Shared Foundation MLD](./shared-foundation-mld.md)
defines their execution, trust, record, and extension boundaries.

## Technical Facts

### Project Node

A Project Node is a normalized technical unit discovered from an ecosystem
manifest or workspace at the target revision.

Examples include:

- a .NET project;
- a Python project;
- a PNPM workspace package; and
- a nested standalone package with its own workspace boundary.

A Project Node may expose:

- ecosystem identity;
- manifest path and project root;
- target frameworks, runtime dimensions, or equivalent pivots;
- direct project or workspace dependencies;
- build and packaging capabilities;
- known global configuration inputs; and
- other stable manifest facts needed by a Build Adapter.

Project Nodes are not authored Release identities and do not own approval,
versioning, publication, or organizational governance.

### Dependency Facts

Dependency facts describe relationships owned by an ecosystem build or
workspace system. Providers obtain them through native metadata or graph
facilities rather than reproducing ecosystem evaluation rules.

Examples include:

- MSBuild project references and evaluated imports;
- PNPM workspace dependencies;
- UV workspace and locked project dependencies; and
- declared build-system inputs that affect multiple nodes.

Cross-ecosystem or root-external inputs that no ecosystem can express may be
declared as explicit extra input edges in the Release Unit build authoring.
These edges are exceptions, not a replacement for native dependency graphs.

### Provider Result and Fact Bundle

Each Provider produces a target-bound Provider Result containing:

- target commit SHA;
- Provider logical and implementation identity;
- Provider request digest;
- relevant ecosystem toolchain version;
- manifest and configuration input digests;
- normalized Project Nodes;
- dependency and known global-input facts;
- build capabilities;
- authoritative target-bound version facts and required native projections when
  the Provider owns NBGV resolution;
- exact-target checkout and complete-history/tag verification facts when NBGV
  version height depends on Git ancestry;
- explicit unresolved or conflicting facts;
- mechanical outcome; and
- diagnostic reference.

A pure Provider returns its Provider Result directly within authoritative
compilation.

A target-evaluating Provider wraps the same Provider Result in an immutable Fact
Bundle that additionally binds producer job, request identity, explicit
purpose, `workflow_run_id`, target, producer and control identities, request
artifact, and transport digest. Run-attempt binding is contextual:

- normal-Live Fact Bundles omit `github.run_attempt`; every authoritative
  normal-Live job, including each producer and consumer, independently requires
  `github.run_attempt == 1`;
- simulation Fact Bundles bind `github.run_attempt`, and each rerun is a
  distinct simulation pass; and
- CI Fact Bundles retain CI's existing candidate and run-attempt contract.

Providers run without publication credentials. The Decision Zone consumes
admitted Fact Bundles rather than evaluating target-controlled project systems
directly.

When NBGV resolution requires target evaluation, the canonical version facts
and every required native ecosystem projection travel in that Provider Result
and Fact Bundle. The transport does not reduce them to one generic version
field.

## Repository Model Providers

### Native Metadata First

Providers use official ecosystem metadata and graph abstractions when those
abstractions own the relevant semantics.

- The .NET Provider uses MSBuild evaluation and graph capabilities rather than
  manually interpreting conditional XML.
- The Python Provider uses UV workspace and lock metadata plus the selected
  build backend's documented metadata surface.
- The JavaScript Provider uses PNPM workspace and package metadata rather than
  rebuilding workspace resolution.

Static parsing remains appropriate for simple, declarative, stable data. A
Provider must not claim stronger knowledge than the lower layer exposes.

### Execution Boundary

Provider discovery executes in an unprivileged environment against the target
commit. It may perform metadata evaluation but must not:

- obtain publication credentials;
- perform publication;
- grant authority;
- reinterpret product policy; or
- silently execute build, test, or publish hooks as a substitute for metadata.

If native metadata cannot establish a required relationship, the model requires
an explicit edge or remains blocked.

## Release Unit Authoring

### Descriptor Discovery

Release Unit declarations are colocated with their primary product/build
boundary and discovered from the target revision's tracked tree by a fixed
descriptor basename.

The discovery shape intentionally reuses a proven v2 mechanism:

1. enumerate tracked paths from the target Git tree;
2. select paths with the configured descriptor basename;
3. sort them deterministically;
4. parse and validate each declaration; and
5. reject duplicate Release Unit identities.

The mechanism is reusable; v2 `Project`, `Profile`, and target-catalog semantics
are not.

The descriptor directory is the base for relative authoring paths. Stable
Release Unit identity comes from descriptor content rather than its physical
path.

The exact basename and serialization format are LLD decisions.

### Descriptor Responsibility

At this design layer, a Release Unit declaration owns only:

- stable Release Unit identity;
- Build Definitions;
- artifact variant and output identities;
- product-level build parameters that must be frozen; and
- extra inputs or dependency edges that ecosystem metadata cannot express.

It does not duplicate:

- project membership;
- project references or workspace dependencies;
- package identity already owned by an ecosystem manifest;
- target framework facts already owned by project configuration; or
- the canonical version value.

Quality policy, channel policy, destination mapping, and governance controls
are separate normalized concerns associated through Release Unit identity.
Later design may choose one physical authoring file without collapsing those
responsibilities in the model.

## Build Definitions

### Semantic Contract

A Build Definition is the complete semantic contract for one artifact variant
build. It freezes:

- ecosystem adapter and operation;
- build entry point;
- configuration and variant dimensions;
- build properties that affect output identity;
- expected output identities and shape;
- declared root-external inputs; and
- provenance requirements.

For the first-slice npm Build Definition, provenance requirements include a
canonical immutable package target witness at
`workflow-delivery/provenance.json` inside the tarball. Its schema binds target
commit, Release Unit, canonical and native NBGV facts, Build Definition,
catalog/control digests, and purpose. It excludes run and Attempt IDs so the
same target, definition, toolchain, and declared inputs remain reproducible
across attempts. The witness is a package content requirement, not a detached
sidecar. The first-slice npm pack contract may satisfy that requirement through
deterministic isolated staging without mutating the source manifest. Its staged
`package.json` `files` allowlist must preserve the existing intended entries and
include `workflow-delivery/provenance.json`.

A native build operation may emit multiple artifacts atomically. For example,
one Python build can produce a wheel and source distribution. The definition
records the complete output set rather than pretending that the tool performed
independent builds.

Each variant receives a complete normalized Build Definition. CI and Release
materialize separate Build Requests from the same definition.

### First-Slice npm Determinism

The first-slice npm Release Unit may select only Build Definitions whose closed
contract produces bit-for-bit identical artifact bytes for the same target,
frozen inputs, Build Definition, and toolchain. That requirement covers every
publishable variant of the Release Unit.

Workflow Delivery records and validates the produced digest but does not
certify determinism by performing a duplicate build. A Release Unit that cannot
meet the contract is unsupported by the first slice. Publishing a
nondeterministic unit requires a future explicit sealed-artifact
publication-resume design.

### No Manual Project Membership

A Release Unit does not maintain a list such as
`projects: [A.UI, A.Core]`.

Instead:

1. the Build Definition selects `A.UI.csproj` as its entry point;
2. the .NET Provider obtains the `A.UI -> A.Core` dependency relationship;
3. the model compiler derives the prerequisite Project Node closure; and
4. the Build Adapter delegates compilation and output composition to MSBuild.

The same rule applies across ecosystems. Build-system dependency behavior is
not reproduced in Release Unit metadata.

### Adapter Reuse

Reusable Build Adapters implement ecosystem mechanics. Build Definitions do
not inherit from a global hierarchy of business build profiles.

Authoring helpers may reduce repetition after multiple concrete definitions
demonstrate stable identical structure. They must expand before planning into
complete immutable Build Definitions with no hidden inherited defaults.

### Intermediate Output Reuse

Intermediate-output reuse is Adapter orchestration, not Release Unit policy.

When multiple artifact materializations use the same logical compilation pivot,
the Adapter reuses the same compiled bytes by construction. This is a
cross-ecosystem default invariant and is not exposed as an authoring option.

Different target frameworks, runtime identifiers, AOT, ReadyToRun, trimming, or
other binary-changing properties define different compilation pivots and do not
carry a cross-output byte-identity requirement.

Acceptance tests verify the invariant for each Adapter. Normal Release
execution does not unpack artifacts and compare internal files to re-prove it.

For .NET, the official CLI supports this orchestration:

- [`dotnet pack`](https://learn.microsoft.com/dotnet/core/tools/dotnet-pack)
  and
  [`dotnet publish`](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish)
  support `--no-build`;
- dependent commands must use compatible build properties; and
- .NET 8+
  [artifacts output layout](https://learn.microsoft.com/dotnet/core/sdk/artifacts-output)
  provides stable build, intermediate, publish, and package locations.

The .NET Adapter must also respect that `dotnet pack` does not automatically
embed referenced project assemblies in one NuGet package. Custom packaging is
an explicit project/build concern rather than a Workflow Delivery assumption.

## Version Authority

NBGV is the sole canonical version authority for v3 Release Units in this
repository.

Before emitting NBGV facts, the NBGV-owning Provider must check out the exact
target commit with credentials disabled and complete ancestry and tags,
equivalent to `fetch-depth: 0`. It verifies that `HEAD` is still the exact target
and rejects a shallow repository, missing required tags, incomplete ancestry, or
any checkout that cannot establish the full-history guarantee. The Repository
Model Compiler must not admit canonical or native NBGV facts from a Provider
that did not establish this contract.

The Repository Model Compiler:

1. validates the effective NBGV `version.json` lineage reported for each Build
   Definition entry point;
2. requires all entry points in one Release Unit to report one canonical
   lineage;
3. admits the NBGV-owning Provider's canonical version facts and required
   native ecosystem projections without recomputation;
4. records those exact values as authoritative NBGV outputs in the Repository
   Model Snapshot; and
5. exposes them for exact selection and freezing by each CI or Release Plan and
   Build Request.

The ecosystem-native NBGV projections are authoritative published product
versions, not downstream derivations. Required projections include
`npmPackageVersion` for this npm slice. Release uses that frozen value
unchanged. Channel, Release Intent, request, workflow, run, and Attempt
identities must not append or otherwise derive additional published version
components.

Official Product Identity uses the canonical NBGV version fact. Official
Release Execution Identity adds immutable target. The Product Identity field
does not replace the native ecosystem projection: Official live publication
and dry-run select and freeze the exact required native projection from the same
target-bound fact set. Repository Model compilation does not create or require a
global Product Identity-to-target binding.

Release Unit descriptors do not select alternative version authorities.

`nbgv-python` projects the same NBGV version into Python/Hatch builds. It is an
ecosystem adapter for the repository version authority, not a second authority.

Conflicting lineages, incompatible manifest versions, or an unresolvable NBGV
version block model compilation.

## Repository Model Compilation

The compiler combines Release Unit declarations, a closed Provider Request
Manifest, terminal Provider Results, and NBGV facts into one immutable
Repository Model Snapshot.

Every Provider Request Manifest and Repository Model Snapshot binds request
identity, purpose, `workflow_run_id`, target, producer and control identities,
and the expected Provider request/result closure. Both the manifest and
Snapshot also bind the caller-selected channel and Release Unit where the
owning purpose requires them. The Snapshot binds the manifest identity and
digest, every terminal Provider Result identity and digest, and each admitted
Fact Bundle transport and payload identity and digest where applicable.
Exactly one terminal Provider Result must exist for every request. Missing,
duplicate, unexpected, or differently bound results block model compilation.

Run-attempt binding follows the owning execution contract:

- **Normal Live:** the request-local Manifest, Fact Bundles, Snapshot, and
  current-Attempt records and transports bind `workflow_run_id` and omit
  `github.run_attempt`. Every authoritative normal-Live job, including each
  producer and consumer, independently requires `github.run_attempt == 1`.
  Release compiles one Snapshot for the current request before Execution lookup
  and reuses it throughout the resulting Attempt.
- **Release simulation:** the Manifest, Fact Bundles, and Snapshot bind
  `github.run_attempt`, selected channel, and Release Unit. A rerun is a
  distinct simulation pass with a new purpose-bound Snapshot. The Snapshot
  does not bind the not-yet-created Simulation Identity; Release derives that
  identity only after Snapshot validation.
- **CI Qualification:** the Snapshot and Fact Bundles retain CI's approved
  candidate and run-attempt contract.

Every context rejects cross-purpose, other-request, and prior-Attempt
Repository Model inputs. This is strict current-context admission, not custom
Actions history discovery or prior-Attempt reconstruction.

For each Release Unit, it:

1. validates stable identity and descriptor-path rules;
2. resolves every Build Definition entry point to a Project Node;
3. validates that the selected Build Adapter supports the modeled build
   operation and dimensions;
4. computes the prerequisite Project Node and declared-input closure;
5. resolves one NBGV lineage and its target-bound canonical and required native
   projection facts;
6. validates modeled variants, output identities, artifact variant uniqueness,
   and complete build and artifact scope;
7. creates the reverse index from Project Nodes to dependent Build Definitions
   and Release Units; and
8. records unresolved facts as blocking model state.

The compiler does not execute Build Definitions and does not choose CI or
Release policy.

## CI and Release Consumption

### CI Qualification

CI receives Git changed paths and uses the Repository Model Snapshot as the
technical graph for affected selection:

```text
changed paths
  -> affected Project Nodes and global inputs
  -> reverse dependency closure
  -> affected Release Unit Build Definitions
  -> all publishable variants of those Release Units
```

Project Graph use is an internal affected-selection mechanism, not a
user-authored domain layer.

The
[CI Qualification MLD](./ci-qualification-mld.md)
defines exact responsibility boundaries for Provider-native path ownership,
ecosystem-global inputs, declared extra inputs, repository path policy,
unclassified-path handling, and repository-level obligations.

### Release Delivery

Release ignores changed-path optimization. It selects one explicit Release Unit
and uses the complete Project Node, declared-input, Build Definition, variant,
and output closure compiled for that Release Unit.

For normal Buddy, `workflow_dispatch` may select any same-repository ref. The
ref resolves to one exact SHA that supplies both the workflow/control revision
and the Release target. Protected Governance remains a separate read from
`refs/heads/main`; it is not substituted for the selected same-revision
Repository Model or control stack. The selected-revision control must strictly
admit exact schema
`workflow-delivery/v3/normal-live-governance-attestation-v1`; an incompatible
ref fails before Release Execution lookup, Attempt creation, or any
Environment job.

Each Release request branches to live release or release simulation before
live eligibility, Product or Execution lookup, coalescing, admission, or
Attempt creation. The selected branch performs same-revision, request-local
Repository Model compilation and binds the Snapshot to that request,
`workflow_run_id`, target, producer, control revision, and purpose. Its
canonical NBGV fact supplies Official Product Identity; its native facts remain
authoritative later planning selections.

Compilation must close descriptors, Project Nodes and dependency graph, Build
Definitions, modeled variants and outputs, canonical and native NBGV facts, and
build and artifact scope. Failure ends the candidate before Execution lookup,
coalescing, or admission and creates no Attempt.

If the request is admitted, Attempt planning uses that same Snapshot to compile
channel policy and validate policy-selected variants, obligations, and
compatibility obligations. It selects and freezes required native projections
from the Snapshot, then derives and validates destination projections and
coordinates, Adapter and version bindings, logical operations, potential action
and dependency schemas, capability policy, and deterministic complete
mutable-resource-key derivation and enforceability basis. Actual actions,
inputs, and complete action key sets materialize only after build,
qualification, and observation and freeze in the Publication Snapshot. Live
Attempt planning or simulation planning does not recompute the Repository Model
within the current pass.

Normal-Live retry is a new manual dispatch and `workflow_run_id`. It compiles a
new request-local Snapshot and never adopts one from an older Attempt. GitHub
rerun commands are unsupported for normal Live, and every authoritative job's
attempt-1 guard prevents rerun formation of authority. Simulation retains its
separate run-attempt identity and rerun behavior. Native Actions history may be
used for diagnostics only; it is not Repository Model admission authority.

Release rebuilds from the immutable target commit and never consumes CI build
outputs or Evidence.

## Failure Conditions

Repository Model compilation is blocked when:

- descriptor syntax or identity is invalid;
- duplicate Release Unit IDs exist;
- a Build Definition entry point is missing or ambiguous;
- the modeled Build Adapter operation or dimension is unsupported;
- a required dependency or extra input cannot be resolved;
- one Release Unit resolves to conflicting NBGV lineages;
- the NBGV-owning Provider reports a shallow checkout, missing required tags,
  incomplete ancestry, a target mismatch, or an unproved full-history
  guarantee;
- a required canonical or native NBGV projection is missing, unknown,
  conflicting, or not target-bound;
- output identities collide;
- a Provider reports unresolved required facts;
- the model cannot establish a closed build and artifact scope;
- a Provider Request, Fact Bundle, or Repository Model Snapshot is not bound to
  the current purpose, request identity, `workflow_run_id`, target, producer,
  control identity, and context-required run-attempt contract;
- an authoritative normal-Live job is not on `github.run_attempt == 1`;
- a normal-Live record contains or requires `github.run_attempt` as authority;
- a simulation or CI record omits or mismatches the run-attempt binding required
  by its owning contract;
- a Fact Bundle or Snapshot from another request or prior Attempt is offered to
  the current compilation or pre-Execution admission path; or
- a live-release compilation or admission path receives a simulation-purpose
  artifact, or a simulation pass receives a live-release artifact.

Diagnostics explain the blocking facts. They do not authorize partial model
compilation.

## Acceptance Scenarios

### .NET Application With an Internal Library

`A.UI.csproj` references `A.Core.csproj`. The Release Unit declares an
application Build Definition rooted at `A.UI.csproj`.

- No Project Node membership list is authored.
- A change to Core reaches the application through the MSBuild graph.
- `dotnet publish` remains responsible for including runtime dependencies.
- Adapter acceptance tests verify compatible materializations reuse the same
  logical compiled bytes.

### Python Package

A Python Release Unit points to one `pyproject.toml` and uses a Python Build
Adapter.

- UV and build-backend metadata provide project and dependency facts.
- One build operation may produce wheel and source-distribution outputs.
- `nbgv-python` injects the canonical NBGV version.
- The Python Build Request selects and freezes the required authoritative native
  projection from the Repository Model Snapshot; the Adapter applies and
  verifies that value without recomputing NBGV or falling back to another
  field.

### Native npm Projection

An npm Release Unit resolves a target-bound NBGV fact set.

- The Repository Model Snapshot contains the canonical NBGV facts and native
  `npmPackageVersion`.
- The Release Unit and selected Build Definition satisfy the first-slice
  deterministic-byte contract without requiring a duplicate build.
- Buddy Release Execution Identity ignores version. After admission, the
  Release Plan and Build Request select and freeze `npmPackageVersion` from the
  Snapshot before deriving package coordinates and projections.
- The Node Build Adapter applies and verifies exactly that value.
- Missing projection data, recomputation, alternative derivation, and fallback
  version fields are not admissible.

### Nested Node Example Workspace

`hexo-renderer-asciidoc/examples/hexo-site` has its own `package.json` and
workspace boundary while linking to the parent package.

- The PNPM Provider discovers both Project Nodes and the dependency direction.
- Directory nesting alone does not create a Release Unit relationship.
- The later CI MLD decides how changes propagate into compatibility obligations.

## Current Implementation Fact to Replace

This is non-normative v1/v2 mechanism evidence, not the v3 contract.

The current reusable Node pack workflow receives planner-provided expected
versions and verifies packaged manifest values and filenames against them. The
current WXT workflow also invokes `nbgv get-version -v NpmPackageVersion` and
uses an `nbgv-version.mjs` build wrapper inside the build workflow. The v3
implementation must replace that in-build NBGV recomputation path with the
Repository Model Snapshot and Build Request projection binding defined here.

## Deferred LLD Decisions

- descriptor basename and serialization format;
- exact strict descriptor, Provider Result, Fact Bundle, target-bound NBGV fact,
  native projection, and Provider Request Manifest schemas, including explicit
  request identity, purpose, `workflow_run_id`, target, producer, control, and
  context-selected run-attempt bindings;
- canonical Project Node identity encoding;
- Provider command lines and isolation details;
- Build Definition digest canonicalization;
- artifact output path conventions;
- authoring helper syntax;
- per-ecosystem Adapter package layout;
- context-appropriate collision-safe non-authoritative physical artifact names,
  immutable artifact ID/digest/URL transport, and ID-only admission;
- conformance fixtures proving Repository Model Snapshot and Fact Bundle
  preservation of canonical and native NBGV outputs;
- NBGV Provider contract and control fixtures proving exact-target
  `fetch-depth: 0` or equivalent full-history/tag materialization, rejection of
  shallow or incomplete history before version compilation, and preservation of
  the exact target commit after fetch;
- first-slice npm Build Definition provenance-witness schema and fixtures
  proving exact target/Release Unit/version/definition/catalog/control/purpose
  binding without run/Attempt IDs;
- ready-versus-blocked completeness fixtures for descriptors, technical graph,
  Build Definitions, modeled variants and outputs, and build and artifact scope;
- normal-Live request-local Snapshot identity, `workflow_run_id`, producer,
  control, target, and purpose binding; omission of `github.run_attempt`; and
  independent attempt-1 guards on every authoritative job;
- simulation Snapshot and Fact Bundle run-attempt binding, rerun identity, and
  recompilation contracts;
- CI candidate and run-attempt binding compatibility;
- negative admission fixtures rejecting cross-purpose, other-request,
  prior-Attempt, and context-mismatched Provider Requests, Fact Bundles, and
  Snapshots;
- purpose-binding fixtures proving live Release and simulation compile and
  reuse separate Snapshots, derive Simulation Identity only after Snapshot
  validation, and reject cross-purpose Provider Requests, Fact Bundles, and
  Snapshots; and
- Build Adapter contract tests proving exact frozen-projection application and
  verification without NBGV recomputation, alternative derivation, or fallback.
