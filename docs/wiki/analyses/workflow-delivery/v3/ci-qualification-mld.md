# Workflow Delivery v3 CI Qualification MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed on 2026-08-03**.

This middle-level design defines how CI Qualification identifies an immutable
candidate, derives affected scope, resolves project-selected quality policy,
closes a qualification Plan, executes required and advisory work, admits
Evidence, and publishes one stable required-check decision.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md),
[High-Level Design](./high-level-design.md),
[Repository Model and Release Unit MLD](./repository-model-release-unit-mld.md),
and
[Shared Foundation MLD](./shared-foundation-mld.md).
Exact file syntax, strict schemas, command lines, and GitHub workflow topology
remain lower-layer decisions.

## Scope

This MLD owns:

- CI candidate identity and incremental or full-validation mode;
- changed-path classification and affected Project Node closure;
- project-selected quality presets and custom quality policy;
- CI obligation identity, disposition, dependency, and Plan readiness;
- the root HK source-tree conformance contract;
- execution batching, runner dimensions, and failure continuation;
- CI Evidence, required Final Decision, and advisory reporting;
- candidate supersession and cache boundaries; and
- the ordinary pull-request latency objective.

This MLD does not own:

- Release qualification, authorization, or publication;
- ecosystem-native project and dependency semantics;
- Release Unit authoring or Build Definition semantics;
- GitHub Rulesets, CODEOWNERS, or Environment authority;
- exact HK profiles or steps;
- exact preset descriptor syntax;
- exact workflow job and matrix serialization; or
- Shared Foundation package decomposition.

## Governing Boundary

CI Qualification proves two different classes of assertion:

1. **Source-tree conformance** proves that the immutable checkout satisfies
   repository-local rules.
2. **Affected-system qualification** proves that the systems affected by the
   candidate remain correct across project graphs, quality targets, execution
   dimensions, and publishable artifact variants.

The root HK gate owns source-tree conformance. The CI Planner owns
affected-system qualification.

Tool names do not define this boundary. A formatter, linter, lock check,
generated-file synchronization check, or repository scenario test may belong
to HK when it proves checkout-local conformance. A compiler, type checker,
analyzer, or test runner belongs to model-driven CI when its applicability or
meaning depends on a Project Node, dependency graph, native workspace, runner
matrix, or Release Unit.

## Flow

```text
GitHub event
  -> immutable candidate identity and validation mode
  -> Repository Model and path-impact facts
  -> affected Project Node reverse closure
  -> effective preset or custom policy selection
  -> concrete quality targets and dimensions
  -> affected Release Units and publishable variants
  -> closed CI Qualification Plan
       + required SourceTreeConformance definition
       + required affected-system obligations
       + advisory affected-system obligations
       + prerequisite DAG
  -> required and advisory execution lanes
  -> Plan-bound Evidence envelopes
  -> required Evidence Admission and Final Decision
  -> stable required check
  -> non-authoritative Advisory Report
```

## Candidate Identity and Validation Modes

### Incremental Candidates

Incremental CI requires an authoritative comparison range and immutable tested
candidate:

- a pull request binds the base commit, head commit, and GitHub-generated
  tested merge commit;
- a merge queue binds the merge-group comparison identity and merge-group
  commit; and
- a protected-branch push binds the event `before` and `after` commits.

The Planner records the exact revisions used for changed-path computation and
the exact candidate tree used for execution.

Missing, unavailable, or conflicting comparison identities block planning.
Incremental CI does not silently replace an invalid range with full validation.

### Explicit Full Validation

Schedule and explicitly selected manual full-validation events use a distinct
mode. They do not synthesize a fake changed-path range.

Full validation:

- runs root HK against all tracked files;
- resolves effective quality policy for all active Project Nodes that are not
  solely Provider-resolved supporting targets;
- resolves supporting test and aggregate targets through their owning project
  policies;
- expands every required and advisory concrete quality target;
- builds every publishable variant of every Release Unit;
- executes all repository and control-plane obligations; and
- performs no changed-path pruning.

Manual full validation cannot omit required scope through ad hoc operator
selection.

### First-Slice Transitional Modes

The first `hcoona-release-smoke-npm` implementation does not expose the
canonical full-validation mode above. During coexistence it exposes only:

- a shadow pull-request incremental check for slice-relevant changes; and
- a manually dispatched `slice-validation` purpose that validates the complete
  first slice without changed-path pruning.

Manual slice validation includes root HK plus the first-slice Project Node,
project build, project tests, and Release Unit npm artifact build/pack. It is
non-authoritative, is not a Ruleset required check, and must never be named or
reported as repository-wide full validation. The shadow pull-request check also
does not replace v1 required CI. v1 and v3 must not issue parallel authoritative
Decisions during coexistence.

Canonical explicit or scheduled full validation remains the complete-repository
mode defined in the preceding section. Its implementation is deferred until the
Repository Model and policies cover every active Project Node, Release Unit,
repository path class, and repository obligation.

## Changed-Path Classification

Every tracked changed path must receive at least one explicit interpretation.
The Planner combines four existing ownership sources rather than implementing
one cross-ecosystem nearest-manifest algorithm.

### Native Project Ownership

Each ecosystem Provider maps ordinary source and project-local files through
the ecosystem's native project and workspace boundaries.

This rule handles independent nested boundaries. For example,
`hexo-renderer-asciidoc/examples/hexo-site` is a separate PNPM workspace linked
to its parent package. Directory nesting alone does not make the path belong to
both Project Nodes.

### Ecosystem-Global Inputs

Each Provider identifies configuration that affects multiple Project Nodes in
its ecosystem, such as workspace definitions, lockfiles, toolchain selection,
and global build configuration.

The Provider emits the affected native scope rather than assigning such a file
to an arbitrary nearby project.

### Declared External Inputs

Build Definitions and custom Quality Definitions declare root-external scripts,
templates, fixtures, generated sources, or other inputs that native project
metadata cannot express.

A change to such an input affects every declared consumer.

### Repository Path Policy

A small repository-owned path policy classifies paths that are not ecosystem
facts or declared definition inputs, including:

- repository-only documentation;
- global CI policy and decision code;
- quality preset and definition catalogs;
- cross-ecosystem control files; and
- other repository-level conformance or control surfaces.

The policy does not duplicate normal project ownership.

### Control Definition Consumers

Control definitions propagate through their actual consumers:

- a preset change affects Project Nodes that select that preset;
- an ecosystem Provider or Quality Adapter change affects targets that depend on
  that implementation;
- a Build Adapter change affects Release Unit variants that use it; and
- Planner, Finalizer, Evidence Admission, or global CI policy changes affect the
  repository-wide qualification semantics.

Only genuinely global decision semantics trigger repository-wide qualification.
Other control changes use the narrowest complete consumer set.

An unclassified path, unresolved consumer, or conflicting path interpretation
blocks the Plan.

## Affected Scope

### Project Node Closure

The Planner starts from directly affected Project Nodes and computes the full
reverse closure over dependency edges whose Provider-defined semantics affect
qualification.

The closure continues until stable. It is not limited to direct dependents.
Build, runtime, tooling, analyzer, test-only, and other edge kinds propagate
according to ecosystem-native semantics exposed by the Provider.

Unknown or conflicting required edges block planning.

### Release Unit Closure

The Repository Model reverse index maps affected Project Nodes and declared
inputs to dependent Build Definitions and Release Units.

Every affected Release Unit contributes all of its publishable artifact
variants to the CI Plan. Changed-path optimization does not select a subset of
publishable variants inside an affected Release Unit.

For each selected variant, the CI Plan and Build Request select and freeze the
exact required target-bound native NBGV projection from the Repository Model
Snapshot. The Build Adapter applies and verifies that value; it does not
recompute NBGV, derive another version, or use a fallback field.

### Repository-Only Scope

A path may be classified as repository-conformance-only. Such a candidate still
requires the root HK gate but does not create Project Node or Release Unit scope
unless another ownership source declares a consumer.

## Source-Tree Conformance Through HK

### One Opaque Composite Definition

Every ready CI Plan contains one required `SourceTreeConformance` obligation.
Its provider is the root HK validation gate.

The CI Planner knows:

- the definition identity and digest;
- the immutable candidate revision;
- the authoritative comparison range or explicit full-validation mode; and
- the root configuration identity from the candidate revision.

The Planner does not know or select:

- HK profiles;
- HK steps;
- per-step file applicability;
- HK batching; or
- HK's internal execution plan.

HK internally plans and executes the steps needed to satisfy the composite
definition. Its internal steps are not CI obligations and do not produce
independent CI Evidence.

### First-Slice v3 Control Tests

The current root HK configuration does not yet run the new v3 control package
pytest suite because that package does not exist. The implementation commit that
adds the package also adds one HK-internal step.

That step runs when the comparison includes:

- `src/public/lib/three-workflow-delivery-v3/**`;
- any addition, deletion, rename, or modification matching
  `src/**/workflow-delivery.release-unit.yml`;
- any addition, deletion, rename, or modification matching
  `src/**/workflow-delivery.quality.yml`;
- `eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml`;
- `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`;
- every v3 control, catalog, and test path;
- every governed v3 workflow, action, and directly invoked script;
- `.github/CODEOWNERS`;
- root `pyproject.toml`, `uv.lock`, and other direct Python workspace inputs;
  and
- `hk.pkl`, imported HK configuration modules, and directly invoked HK helpers.

Manual `slice-validation` forces the step to run regardless of changed paths.
It remains part of the single `SourceTreeConformance` obligation and creates no
separate CI obligation, Evidence record, or job. Unrelated product source alone
does not select this control-test step. A policy-only change to the exact
first-slice policy does.

### Permanent Disposable-Package Consumer Policy

Root HK permanently includes a repository-wide dependency-policy step for
`@hcoona/hcoona-release-smoke-npm`. It scans dependency manifests, lockfiles,
workflows, install/bootstrap scripts, package-manager configuration, and other
cataloged dependency surfaces for normal developer, CI, or production
consumption.

Dependency-surface changes select the step. Manual `slice-validation` forces it
to run regardless of changed paths. The smoke package's own declaration and
explicit destination-acceptance fixtures are narrowly classified exceptions,
not consumers. Any other consumption fails `SourceTreeConformance` and reopens
the first-slice Governance exception. The step remains internal to root HK and
does not create a separate CI obligation or Evidence record.

### Root Configuration Authority

The repository-root `hk.pkl` is the only CI-authoritative HK configuration.

Projects may keep commands or reusable step modules near project code. The root
configuration may import or register those definitions. A project-local
`hk.pkl` may provide local developer experience, but its mere presence does not
create a required CI gate.

This keeps requiredness centralized at the repository gate while allowing
project-owned command implementation.

### Execution Contract

Incremental execution gives HK the authoritative comparison revisions. Full
validation gives HK an explicit all-files mode. HK computes its own internal
file and step selection.

The executor uses read-only check behavior. Configuration load failure, panic,
timeout, tool failure, or any failed internal step fails the composite
obligation.

No matching internal step is a legitimate HK decision and may satisfy the
composite obligation.

The architecture does not depend on `hk --plan`, JSON plan output, or per-step
machine-readable results.

### Semantic Scope

The root gate may include:

- formatting and linting;
- syntax and source-file static checks;
- lock consistency;
- generated or projected configuration synchronization; and
- path-triggered repository scenario tests.

It does not own affected Project Node builds, project or native-workspace type
checking, graph-derived tests, runner matrices, Release Unit artifact builds,
or publication-shaped validation.

## Project Quality Policy

### Project Autonomy

The monorepo platform defines the qualification protocol. Projects select their
quality contract.

The repository provides ecosystem-specific presets, and a project may select a
preset or an explicit custom policy. The Planner validates and expands that
selection but does not replace it with a universal project checklist.

### Quality Capability

A Quality Capability is an operation that an ecosystem Provider can resolve for
a Project Node or native aggregate target, such as build, type checking,
analysis, or test execution.

Providers discover standard capabilities from native manifests and metadata.
Custom policy supplies only project-specific definitions that the ecosystem
cannot express.

### Quality Preset

A Quality Preset is an ecosystem-qualified, named semantic contract. Examples
may include:

- `dotnet/library-v1`;
- `python/service-v1`;
- `node/library-v1`; or
- `node/example-v1`.

Presets are not cross-ecosystem aliases. A Node preset has no meaning for a
.NET Project Node.

Each preset requirement uses one of these modes:

- `required`: the capability must resolve; absence blocks the Plan;
- `required-when-present`: a discovered capability becomes required, while
  absence is valid; or
- `advisory`: a discovered capability executes and reports without affecting
  the required Final Decision.

Capabilities not named by the effective preset are not implicitly required.

### Preset Evolution

Adding or strengthening required semantics creates a new preset identity.
Projects adopt that semantic change explicitly.

An existing preset version may receive equivalent implementation repairs,
adapter corrections, or diagnostic improvements that do not change its quality
contract. Plans bind the exact preset and definition digests from the candidate
revision.

### Custom Policy

A project may select explicit custom policy when no preset represents its
quality contract.

Custom policy follows the same capability, disposition, target, dimension, and
Evidence rules as a preset. It is not an escape from Plan closure, strict
bindings, or fail-closed behavior.

### Directory-Scoped Selection

Preset or custom selection uses a simple cascading authoring rule rather than
ecosystem-specific inheritance.

For each Project Node:

1. identify the Project Node's ecosystem;
2. begin at the native manifest directory;
3. walk toward the repository root;
4. select the nearest tracked quality-policy descriptor containing an entry for
   that ecosystem; and
5. resolve that entry to one preset or custom policy.

A descriptor without the relevant ecosystem entry does not stop the search.
The nearest matching entry replaces the next matching ancestor entry; preset
contents are not implicitly merged across directories.

Directory scope is an authoring convenience, not a new business domain object.
The compiled Plan records the effective selection for each affected Project
Node.

An affected Project Node that requires its own quality contract and has no
effective selection blocks planning.

## Concrete Quality Resolution

### Quality Definition

A Quality Definition is the executable semantic contract for one kind of
quality proof. It identifies:

- its Provider or Quality Adapter;
- the supported target kind;
- required inputs;
- coverage intent and dimension rules;
- runner constraints;
- prerequisite relationships;
- raw result interpretation; and
- Evidence output requirements.

A preset selects capability semantics. The Provider resolves those semantics to
concrete Quality Definitions and execution targets.

### Provider-Native Targets

The concrete execution target may be:

- a Project Node;
- a supporting test Project Node;
- a Provider-native workspace or aggregate target;
- the repository checkout;
- a Release Unit artifact variant; or
- another explicitly modeled target supported by a definition.

The architecture does not force an aggregate native operation into artificial
per-project obligations.

### Supporting Test Targets

Ecosystem Providers resolve tests through native facts.

- Node and Python tests commonly execute against the selected package or
  project.
- .NET tests commonly execute through separate test Project Nodes discovered
  from test-project metadata and `ProjectReference` relationships.
- Integration or compatibility tests that cannot be derived from native facts
  require explicit custom definitions.

The Planner materializes each actual test target. If one test target supports
multiple affected projects, the Plan contains one obligation and records all
`required_by` sources.

A change to a supporting test target propagates to the quality requirements it
implements. A supporting target with no resolvable owner or explicit custom
policy is blocking rather than silently ignored.

### Dimensions and Runners

Presets and custom policy express coverage intent. Providers resolve that intent
with native project facts into concrete dimensions such as:

- operating system or runner family;
- target framework;
- runtime or language version;
- architecture; or
- other definition-owned variants.

Each independently decidable matrix cell becomes a distinct obligation. If a
native operation produces one indivisible aggregate result, the Provider
models one aggregate target or dimension set instead.

Workflow YAML does not create additional semantic matrix cells.

## CI Qualification Plan

### Obligation Identity

The identity of a model-driven obligation is:

```text
Quality Definition
  x concrete Qualification Target
  x concrete Dimensions
```

The Plan records:

- stable obligation ID;
- definition and request digests;
- disposition;
- target and dimensions;
- runner constraints;
- prerequisite obligation IDs;
- `required_by` policy and affected-scope sources; and
- expected Evidence contract.

The fixed HK obligation follows the same top-level identity rule, but its
definition is intentionally composite and opaque.

### Deduplication

Only obligations with the same definition, target, and dimensions are
deduplicated. Their `required_by` sources are combined.

If at least one active source requires the obligation, the deduplicated
obligation is required.

Different definitions do not become equivalent merely because an Adapter can
reuse one command, restore, compilation output, or cache entry. For example,
project build and Release Unit artifact build remain distinct quality proofs.

### Prerequisite DAG

The Planner closes an obligation dependency DAG.

Edges represent true semantic prerequisites, such as artifact validation
depending on an artifact build. Cache reuse, common restore, or scheduler
convenience does not create a semantic dependency.

Executors do not discover new obligations or dependencies at runtime.

### Plan Readiness

A Plan is `ready` only when:

- candidate identity and validation mode are complete;
- every changed path is classified;
- Project Node and control-definition closures are complete;
- every affected project has an effective quality policy where required;
- every required capability resolves;
- every concrete target and dimension is supported;
- every affected Release Unit variant has a valid Build Definition;
- every obligation dependency is closed; and
- no required fact conflicts.

Otherwise the Planner emits a blocked Plan with structured diagnostics.

A blocked Plan executes no authoritative qualification obligations. Optional
bootstrap diagnostics are non-authoritative and cannot produce admitted
Evidence for the blocked Plan.

## Execution

### Required and Advisory Lanes

Planner output is partitioned into static required and advisory execution lanes.

Required and advisory obligations are never mixed in one execution batch.
GitHub job dependencies remain static:

```text
Planner
  +-- required executor lanes -> required Finalizer -> stable required check
  |
  +-- advisory executor lanes -> Advisory Reporter
```

The required Finalizer does not wait for advisory lanes.

### Identity-Preserving Batching

The scheduler may batch compatible obligations to reduce runner and tool startup
cost. A batch request lists exact obligation IDs and cannot add scope.

Each independent obligation retains its own raw result and Evidence:

- completed successful members are `satisfied`;
- completed quality failures are `failed`;
- members not started because of an earlier batch failure are `incomplete`; and
- members blocked by a prerequisite are
  `incomplete(blocked-by-prerequisite)`.

If a tool exposes only one indivisible result, the Planner must model one
aggregate obligation rather than fabricate member Evidence after execution.

### Failure Continuation

A failed prerequisite prevents dependent obligations from running.
Independent obligations continue so one candidate produces useful complete
diagnostics.

Global cancellation is reserved for:

- candidate supersession;
- the execution deadline; or
- infrastructure termination that prevents trustworthy continuation.

At the deadline, remaining required obligations become `incomplete`.

### Mechanical Reuse

Adapters may reuse restore results, dependency downloads, compiled outputs, or
other intermediate work when semantic inputs are compatible.

Mechanical reuse does not merge obligation identities or Evidence contracts.
Cache unavailability must not change Plan scope or verdict semantics.

## Evidence, Decision, and Reporting

### Evidence Envelope

The CI execution framework creates one strict Evidence envelope per obligation.
The target command produces a raw result but does not self-assert an unbound
success.

An envelope binds at least:

- candidate identity;
- Plan digest;
- obligation ID;
- definition and request digests;
- outcome;
- producer job and workflow attempt;
- runner family;
- relevant artifact or output digests; and
- diagnostic reference.

The initial implementation does not require a cross-revision schema or API
version because producers and consumers run from the same candidate revision
and workflow attempt.

### Required Finalizer

The CI Finalizer admits required Evidence and produces one immutable Final
Decision.

Success requires:

- a ready Plan; and
- satisfied admitted Evidence for every required obligation.

Failed, incomplete, conflicted, missing, skipped, cancelled, or timed-out
required outcomes cannot become success.

The Finalizer does not rerun checks, interpret test output, inspect HK internal
steps, or compensate for missing executor results.

One stable GitHub required-check context projects the authoritative Decision.
GitHub job names, batches, and matrix cells are execution details rather than
branch-policy interfaces.

### Advisory Reporter

Advisory obligations produce Plan-bound Evidence but do not enter the
authoritative required Final Decision.

An independent Advisory Reporter validates bindings and produces a
non-authoritative report through a non-required job, artifact, or human summary.
Advisory failure and Reporter failure are visible but do not change the required
verdict.

If a check must affect qualification, its project policy must classify it as
required.

## Concurrency and Reuse

Candidate identity defines supersession.

- a newer pull-request candidate cancels the older candidate run;
- a changed merge group cancels the obsolete merge-group run;
- a newer protected-branch push supersedes the older pending run for that ref;
  and
- schedule and manual full-validation runs use separate concurrency domains.

Evidence belongs only to its exact candidate and Plan. A new candidate executes
all of its required obligations.

Caches may be reused across candidates when their mechanical keys match. Cached
content is never admitted as prior candidate Evidence.

Cancelled obligations are incomplete for the cancelled Plan. Their existing
records remain diagnostic history but cannot satisfy a later Plan.

## Ordinary Pull-Request Latency

The P95 12-minute objective measures ordinary pull-request CI from GitHub
workflow creation to publication of the stable required Final Decision. Runner
queue time is included.

The ordinary cohort:

- uses incremental pull-request mode;
- does not change governance, Planner, Finalizer, Evidence Admission, preset
  semantics, root toolchains, or other broad control surfaces;
- affects no more than one Release Unit;
- is not an explicit full-validation run; and
- excludes runs superseded by a newer candidate.

Real quality failures and blocked Plans remain in the ordinary cohort when the
candidate does not meet a broad-change exclusion. They are not removed merely
to improve the metric.

Planning, runner queue, execution, and finalization durations are recorded
separately for diagnosis. Performance work may change parallelism, batching,
native aggregate use, intermediate reuse, and cache behavior. It may not reduce
the reverse closure, required obligations, publishable variants, or Evidence
Admission.

## Failure Conditions

CI planning or qualification fails closed when:

- candidate or comparison identity is incomplete or inconsistent;
- a tracked changed path is unclassified;
- native project ownership or dependency facts are unresolved;
- a control definition has unknown consumers;
- an affected project has no effective required quality policy;
- a required capability, target, dimension, or runner is unsupported;
- an affected Release Unit variant cannot be built;
- the obligation DAG is incomplete or cyclic;
- the root HK definition cannot execute successfully;
- required Evidence is missing, invalid, or conflicting; or
- a required obligation fails or remains incomplete.

Diagnostics explain the failing fact and its affected scope. They do not
authorize partial success.

## Acceptance Scenarios

### Documentation-Only Pull Request

A normal wiki Markdown change is repository-conformance-only.

- The Plan contains the required HK composite obligation.
- No Project Node or Release Unit scope is added unless another definition
  declares the document as an input.
- HK internally selects applicable checks.
- No matching HK step is a legitimate successful internal decision.

### .NET Dependency Change

`A.UI` depends on `A.Core`, and the projects belong to different Release Units.

- A change to Core affects Core and UI through the typed reverse closure.
- Each affected project resolves its effective .NET quality policy.
- Provider-discovered supporting test projects become concrete test targets.
- Both affected Release Units contribute all publishable variants.
- Project build and artifact build remain separate obligations while Adapters
  may reuse compatible intermediate outputs.

### Independent Nested Node Workspace

The Hexo example workspace links to the parent renderer package.

- The PNPM Provider discovers two Project Nodes and the dependency direction.
- A parent change may affect the example consumer.
- An example-only change does not affect the parent through a reverse edge.
- Each Project Node resolves the nearest ancestor descriptor containing a Node
  policy entry.
- The example uses an explicit lighter preset or custom policy when the parent
  library preset is not appropriate.

### Control Definition Change

- A preset implementation change affects projects selecting that preset.
- A Node Provider change affects Node consumers.
- A Build Adapter change affects Release Unit variants using that Adapter.
- A Planner or Finalizer change triggers repository-wide qualification.
- Semantic strengthening of a preset requires a new preset identity and project
  opt-in.

### Required and Advisory Work

A Python service selects required build, typecheck, and tests plus an advisory
compatibility probe.

- Required and advisory obligations use separate execution lanes.
- The required Finalizer publishes without waiting for the advisory probe.
- The Advisory Reporter later exposes the advisory result.
- An advisory failure does not change the required verdict.

### Full Validation and Supersession

- Schedule and explicit manual full validation qualify the complete repository
  without changed-path pruning.
- A newer pull-request candidate cancels the older candidate run.
- Evidence from the older candidate cannot satisfy the new Plan.
- Compatible caches may still reduce mechanical work.
- An invalid incremental range blocks planning rather than silently invoking
  full validation.

## Deferred LLD Decisions

The first CI LLD must define:

- candidate request, Plan, Evidence, Decision, and Advisory Report schemas;
- the quality-policy descriptor basename and strict syntax;
- the initial preset catalog and custom-policy representation;
- Provider contracts for ownership, capabilities, supporting tests, dimensions,
  and control consumers;
- exact HK adapter invocation and root CI gate configuration;
- exact GitHub runner lanes, dynamic matrix encoding, and empty-lane handling;
- batch request and per-obligation result transport;
- execution and advisory deadlines;
- artifact and Evidence naming and retention;
- stable required-check and human-summary rendering; and
- acceptance tests for every scenario in this MLD.
