# Workflow Delivery v3 Repository Model and Release Unit MLD

## Status

Architecture version: **v3**.

Review state: **Draft synthesized from confirmed decisions**.

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
- NBGV version authority resolution;
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
         -> canonical version authority

Release Unit declarations
  + complete Provider Request Manifest
  + terminal Provider Results
  + NBGV facts
  -> Repository Model Compiler
  -> immutable Repository Model Snapshot
```

The compiled snapshot is the shared technical input to CI and Release. It
contains no CI Plan, Release Plan, Evidence, authorization, or business
verdict.

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
- build capabilities; and
- explicit unresolved or conflicting facts;
- mechanical outcome; and
- diagnostic reference.

A pure Provider returns its Provider Result directly within authoritative
compilation.

A target-evaluating Provider wraps the same Provider Result in an immutable Fact
Bundle that additionally binds producer job, workflow run and attempt, request
artifact, and transport digest. Providers run without publication credentials.
The Decision Zone consumes admitted Fact Bundles rather than evaluating
target-controlled project systems directly.

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

A native build operation may emit multiple artifacts atomically. For example,
one Python build can produce a wheel and source distribution. The definition
records the complete output set rather than pretending that the tool performed
independent builds.

Each variant receives a complete normalized Build Definition. CI and Release
materialize separate Build Requests from the same definition.

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

The Repository Model Compiler:

1. resolves the effective NBGV `version.json` lineage for each Build Definition
   entry point;
2. requires all entry points in one Release Unit to resolve to one canonical
   lineage;
3. computes the canonical version for the immutable target commit once; and
4. freezes that value as an input to every Build Request.

Release Unit descriptors do not select alternative version authorities.

`nbgv-python` projects the same NBGV version into Python/Hatch builds. It is an
ecosystem adapter for the repository version authority, not a second authority.

Conflicting lineages, incompatible manifest versions, or an unresolvable NBGV
version block model compilation.

## Repository Model Compilation

The compiler combines Release Unit declarations, a closed Provider Request
Manifest, terminal Provider Results, and NBGV facts into one immutable
Repository Model Snapshot.

The Provider Request Manifest binds every expected Provider request, execution
mode, request digest, and expected result identity. Exactly one terminal
Provider Result must exist for every request. Missing, duplicate, or unexpected
results block model compilation.

For each Release Unit, it:

1. validates stable identity and descriptor-path rules;
2. resolves every Build Definition entry point to a Project Node;
3. validates that the selected Adapter supports the requested operation and
   dimensions;
4. computes the prerequisite Project Node and declared-input closure;
5. resolves one NBGV lineage and canonical version authority;
6. validates output identities and artifact variant uniqueness;
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

Release rebuilds from the immutable target commit and never consumes CI build
outputs or Evidence.

## Failure Conditions

Repository Model compilation is blocked when:

- descriptor syntax or identity is invalid;
- duplicate Release Unit IDs exist;
- a Build Definition entry point is missing or ambiguous;
- the requested Adapter operation or dimension is unsupported;
- a required dependency or extra input cannot be resolved;
- one Release Unit resolves to conflicting NBGV lineages;
- output identities collide;
- a Provider reports unresolved required facts; or
- the model cannot establish a closed build and artifact scope.

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

### Nested Node Example Workspace

`hexo-renderer-asciidoc/examples/hexo-site` has its own `package.json` and
workspace boundary while linking to the parent package.

- The PNPM Provider discovers both Project Nodes and the dependency direction.
- Directory nesting alone does not create a Release Unit relationship.
- The later CI MLD decides how changes propagate into compatibility obligations.

## Deferred LLD Decisions

- descriptor basename and serialization format;
- exact strict descriptor, Provider Result, Fact Bundle, and Provider Request
  Manifest schemas;
- canonical Project Node identity encoding;
- Provider command lines and isolation details;
- Build Definition digest canonicalization;
- artifact output path conventions;
- authoring helper syntax;
- per-ecosystem Adapter package layout; and
- exact admission artifact names and transport.
