# Workflow Delivery v3 Middle-Level Design

## Scope and Authority

This design refines the [replacement HLD](./high-level-design.md) into module,
selection, execution, transfer and outcome contracts for the internal C# CLI.
It implements the [requirements](./requirements.md) and the owner's contraction
decisions. Implementers and reviewers use it to determine what must survive the
rewrite and where the old mechanisms end. It does not describe a completed
implementation or grant publication authority.

The [transition](./requirements.md#requirements-and-implementation-transition)
scopes existing code, ecosystem LLDs and completed campaigns. The five former
MLDs remain available at their [pinned source](./README.md#normative-hierarchy)
for those historical consumers. Their record chains, smoke-specific constraints
and hypothetical extension frameworks are not requirements for the new core.

This document owns the common middle-level contracts. Concrete native extraction
coverage, destination read limits and workflow wiring are reviewed with their
implementation scope. Missing necessary native capabilities remain unsupported;
implementation details cannot silently weaken these contracts. The
[handoff](./agent-handoff.md) retains the operating and evidence limits.

The [first Node execution group](./migration-strategy.md#first-node-execution-group)
specifies the initial caller's endpoint responsibility, native control inputs,
runtime/check ownership and atomic fixture relocation. The
[native .NET CI group](./migration-strategy.md#native-net-ci-group) defines the
Windows extraction and caller contract. The
[native Python planning facts](#native-python-planning-facts) select its next
extraction boundary; execution and caller mapping remain separate design work.
Each group requires its own native implementation and applicable qualification
before cutover; source design acceptance does not establish those results.

## Components and Native Responsibilities

The application starts at `src/private/app/workflow-delivery/` with one test
project at `tests/private/app/workflow-delivery/`. The following names describe
source modules inside that application, not required projects, interfaces or
processes. Add a boundary only for an actual independent consumer or dependency.

| Module           | Owned behavior                                                                                         | Principal consumers                              |
| ---------------- | ------------------------------------------------------------------------------------------------------ | ------------------------------------------------ |
| Repository       | Read native projects, ownership, dependencies, shared inputs, unit declarations and quality selections | CI impact selection; Release's requested closure |
| CI               | Resolve candidate/comparison, select work and reasons, evaluate required results                       | Static Actions jobs and the required CI check    |
| Release          | Resolve request, prepare/qualify complete outputs, choose remaining writes and evaluate completion     | Dry run, Buddy, Official and ordinary recovery   |
| Ecosystems       | Native version projection, build, package inspection and clean consumption                             | Repository facts and the CI/Release use cases    |
| Destinations     | Native coordinates, observation, upload and verification semantics                                     | Release only                                     |
| Platform helpers | Git, bounded processes/HTTP, Actions artifacts and native execution facts                              | Actual callers of those mechanisms               |

CI and Release share ordinary functions and data where the behavior is shared.
They have separate executions and result ownership. There is no requirement for
matching planners, finalizers, aggregates or directory structures. Static
registration selects supported ecosystem/destination combinations. New native
behavior or a special project may require implementation.

Native tools own project evaluation, compilation ordering, dependency resolution,
version computation and caches. Actions owns jobs, waiting, cancellation and
concurrency; Environments and destination trust configuration own authorization.
The CLI owns selecting its required work, using those facilities correctly and
interpreting the resulting business state.

The HLD's [native integration boundary](./high-level-design.md#native-integrations)
governs every ecosystem module. Call a maintained ecosystem library in its native
language, or a supported tool interface, when that is how the required semantics
are exposed. A helper is part of this application and its selected control
revision, not another policy service. Transfer typed results only at an actual
process or runner boundary; field spelling and internal interfaces may change
with their producers and consumers without compatibility aliases.

## Repository Facts and Impact Selection

### Minimum Facts

Repository-maintained dependency locks are valid and synchronized with their
native manifests. Analysis consumes that existing repository guarantee; it does
not introduce another lock-validation or admission protocol. Reading facts for
a revision must still address that revision's committed inputs rather than
silently substitute a different installed state.

Repository analysis returns the following facts, with their source revision and
any relevant coverage limitation:

- native project identity, ecosystem, manifest and owned paths;
- native project/workspace dependencies and their relevant dimensions;
- shared configuration, build, version and generated-input consumers;
- supporting test targets and the quality contracts they serve;
- Release Unit identity, native entry points, build variants and expected outputs;
- explicit extra inputs or cross-ecosystem relations unavailable from native tools;
- effective project quality selection and available native check targets.

Keep build-input dependencies and quality-consumer relationships distinguishable.
A test project can implement another project's checks without becoming an input
to that product's package or version. Resolve native references and imports using
the ecosystem's supported metadata interface. Converting native identities and
edges into Workflow values is adaptation; interpreting dependency specifiers,
conditional imports or version-file inheritance is native evaluation and follows
the HLD boundary. Combining already resolved edges across revisions and computing
their impact closure remain Workflow responsibilities.

Use existing native project/output metadata for identity association. The
PNPM [directory adaptation candidate](./research/pnpm-native-planning.md#directory-coordinate-adaptation-candidate)
tests joining resolved coordinates to discovered roots and native publish
directories without a second dependency declaration. Its evidence limits remain
separate from acceptance of an implemented reader.

For NBGV, use the pinned public managed API for committed effective options and
native filter matching. Workflow assigns input consumers from those answers and
the native ancestor configuration read boundary; it does not reconstruct
inheritance or historical version-height evaluation. Preserve native case
semantics, missing/added/deleted inputs and both revision identities. The
[source findings and local qualification](./research/nbgv-native-inputs.md)
identify the compile-time official-package integration and its observed Linux
delivery boundary. Consumer mapping remains to be implemented and validated;
unresolved necessary inputs still fail planning. This component evidence does
not establish complete CI fact assembly.

Use ordinary typed values inside one invocation. Do not require a Provider Request
Manifest, Fact Bundle, admission token or immutable Repository Model aggregate.
When native evaluation requires another process or runner, transfer the relevant
facts and errors under the [transfer contract](#data-crossing-boundaries).
Project evaluation runs without publication capability.

CI must obtain enough ownership and reverse-consumer facts to justify both
selection and exclusion. An incomplete graph cannot certify that a changed path
has no consumers. Release resolves its selected unit's complete input/build/check
closure; it need not evaluate unrelated projects.

### Native Python Planning Facts

Within the existing private app's `Native/Python`, use the repository-pinned UV metadata CLI, passive PyPA build string extraction
and the pinned UV Rust parsing/lowering interfaces in the
[source assessment](./research/python-native-planning.md). Standard tomllib supplies
physical key/shape guards only. Native libraries own requirements, defaults, sources, markers
and NBGV semantics. No parallel parser/settings interpreter/resolver, record chain or
compatibility promise.

For each exact basis/candidate checkout, bind revision, workspace root, interpreter/tool
identity and operation dimensions. Invoke `uv workspace metadata --frozen --python <interpreter>` at that workspace root, without sync/active/script mode. Consume each endpoint's committed lock under the existing synchronized-lock guarantee;
frozen does not revalidate freshness. Consume pinned
preview schema, native member paths/names, structured sources, opaque graph IDs, ordinary
edges, group/extra entry IDs, markers and `requires_python`. Never decode IDs or guess owners
from package names, URLs, prefixes or caches.

Admit the current explicit default-index, source-enabled request. Read project settings through
`uv_settings::FilesystemOptions::find(root)`/`into_options`; use public `ResolverOptions::from`,
`IndexLocations::from` and `NoSources::from_args` for native settings construction. Under the
finite guard, these yield native default index locations and NoSources::None. Admit tool.uv
workspace/sources only; additional necessary settings are unsupported until native adaptation,
not ignored. Malformed settings remain errors. Do not port private CLI precedence or suppress
configuration/sources. DiscoveryOptions defaults discover all members; stop_discovery_at=None
for endpoint trees, not a sandbox.

Construct a credential-free child request with owned HOME/XDG/cache/temp roots, explicit
interpreter, downloads disabled and no inherited UV/PIP/index/credential settings. Use `FilesystemOptions::user()`/`system()` with their native result/error semantics,
including the `/etc/uv/uv.toml` fallback. Relevant returned ambient settings are
outside this default request. Do not add filesystem proofs of native configuration
results or reproduce CLI precedence. Bind project configuration and
version-file inputs. Select an owned absent UV_PROJECT_ENVIRONMENT: metadata can inspect/lock
existing environments, so unexpected environment facts fail this request. Native Cache,
WorkspaceCache and empty CredentialsCache supply owned state; no new cache/proof service.

For named package contexts with explicit Hatchling build tables, no backend-path/package
override, obtain unchanged strings from `ProjectBuilder.build_system_requires`. Parse through
UV Requirement::from_str and lower with `BuildRequires::from_project_maybe_workspace`, native
name/install path/settings, editable=true and owned caches. Reject necessary
unsupported native source shapes before a fetch-capable lowering branch. Join typed Directory.install_path
exactly to endpoint-native member roots and committed ownership. Repeat extraction for every
necessary local build-requirement source, combining its ordinary local dependencies/requested
extras with its build associations. Workflow computes transitive/reverse closure and cycles.
Such sources can be plugins: nbgv-python is a build requirement/Hatch version-source plugin;
Hatchling remains the backend. Association does not prove isolated version satisfaction or
build success. Tableless native quality/group contexts remain; necessary forced package builds,
default backends or undeclared dynamic inputs are unsupported.

Use native Interpreter::query(...).markers(), MarkerTree parsing/evaluation,
VirtualProject::default_groups_for_packages and DependencyGroups::from_args(...).with_defaults.
Root pytest preparation includes root plus selected quality owners;
control preparation uses native DevMode::Exclude for no-dev; build-requirement ordinary legs use no development groups.
Initial operation-selected extras are empty; native build-requirement extras retain producer
entry IDs. Require native conflicts empty; necessary unknown group/extra/marker/conflict
selection fails. Preserve endpoint, interpreter/platform, groups/extras and check/build variant;
resolve activity or its justified finite union before markerless ProjectFacts.Dependencies.
Union both endpoints so removed/renamed producers retain consumers.

Adapt into existing RepositoryFacts/ProjectFacts/SharedInput/CheckSpec and impact closure;
keep quality consumers separate from build inputs and root ownership explicit. Managed NBGV
and passive plugin coordinates supply project-scoped version/generated input consumers.
Missing/ambiguous ownership, native errors or unmodeled necessary scope fail planning;
no reduced completeness, parser fallback or all-run.

### Python Quality Owners and Input-Only Members

Native workspace membership does not imply a build or test obligation. Keep the
complete native producer graph, including tableless and unadopted members. Actual
retained pytest targets and adopted package checks define runnable quality owners.
Resolve nearest source ownership over all native members before restricting the
result to runnable owners; an input-only nested member is not its ancestor's source.

The paired assembler associates endpoint-native identities and unions resolved
ordinary, build and quality relations from both comparison revisions before
computing producer-to-runnable-owner closure. Do not compose only endpoint-local
closures after removing intermediate producers. For example, a basis relation
`A -> B` and candidate relation `B -> C`, expressed as producer-to-consumer, must
select surviving runnable `C` for a changed `A` input even when `B` has no checks.
Keep each endpoint's committed path association and initiating path/revision reason;
resolve executable work against surviving candidate owners. Do not infer native
identity matches from package names or opaque graph ID spelling.

Keep same-endpoint exact input consumers in existing `SharedInput` and explicit
`ProjectFacts.OwnedPaths` values. A paired input association may reach a runnable
owner present only at the other endpoint. Transfer that direct selection through
the existing `SelectionReason(Path, Revision, Project)` value: `Project` identifies
the resolved quality consumer, while `Revision` identifies the committed input's
initiating endpoint. It does not assert that consumer's presence at that endpoint.
Only actual endpoint runnable owners become `ProjectFacts` with nonempty resolved
checks; input-only members retain their graph role without synthetic builds,
empty-check policy, candidate-only basis proxies or a new core origin. A path can enter
`UnaffectedPaths` only when the complete paired analysis establishes no applicable
owner. An endpoint-local empty closure, missing facts or unknown ownership cannot
justify exclusion. The core selected-check guard remains unchanged.

The shared selection operation accepts these paired direct reasons before its
existing union relation and Release Unit closure. Validate each reason against a
normalized changed path, an exact comparison revision and a real owner in the
union of actual endpoint owners. Native Git/ownership binding establishes input
presence and complete scope before this transfer. A valid reason resolves only
its own path; other unresolved changed paths still fail, including in full mode.
Do not clear the changed-path set, transfer an externally completed plan or bypass
ordinary fact validation. Candidate survival, complete checks/variants, selected
origins and prerequisite expansion remain the shared core's responsibilities.

Read root pytest configuration through its public `pytest.main(..., plugins=[...])`
and `pytest_cmdline_main` hook, using native `Config.getini`, `rootpath` and `inipath`.
The initial request admits the current nonempty explicit committed `testpaths` and
the existing import-mode option. Reject necessary unsupported glob, implicit
invocation-directory fallback, escaping target, custom plugin or option behavior;
do not copy pytest's configuration precedence or private target-selection algorithm.
Configuration parsing can import explicit plugins and alter Python paths before
the main hook: the query runs unprivileged with owned state, no inherited
`PYTEST_ADDOPTS`/`PYTEST_PLUGINS`, disabled plugin autoload and conftest discovery.
Those switches alone do not prove absence of explicit plugin processing. This
finite native query supplies configuration facts, not successful test collection
or execution. Its source basis is recorded in the
[Python planning assessment](./research/python-native-planning.md#pytest-configuration-source-basis).

Associate configured targets under native members with their actual quality owners;
standalone repository tests are concrete target consumers, not invented workspace
members. Preparation uses root plus selected quality-member contexts and native
operation groups. Input analysis does not require building every workspace package.
Retain adopted package checks, complete outputs and their own prerequisites. The
[caller mapping](./migration-strategy.md#python-quality-and-caller-mapping) owns the
concrete engineering relations that native package tools cannot discover.

### Native .NET Project Facts

MSBuild owns .NET discovery, SDK resolution, evaluation, graph identity and
reference propagation. The existing application supplies a task loaded by the
endpoint's selected `dotnet msbuild` process; Workflow does not host a second
resolver, parse project XML or schedule compilation nodes. The
[integration contract](./migration-strategy.md#native-net-ci-group) identifies
the finite input roles and preparation stages supported by this operation.

Keep each retained business or traversal node's project path and complete
effective globals, including outer/inner identity and native reference edges.
Business impact uses
the committed project path as its project ID. Aggregate native relations for
selection only; every surviving selected candidate project's supported inner
variants expand their complete checks. A changed framework/global tuple must not
make a surviving project's new variant disappear from execution.

NBGV's native caching project is a finite tool adapter exception. Keep it in
MSBuild's local graph and retain the default native caching mode. Identify it
through the marked native reference and the consumer's validated restored NBGV package pin,
not a general rule for external projects. Its internal configuration, physical
globals and tool-only reference edges do not become portable business nodes or
checks. Preserve its necessary observed inputs for its business consumers.
Receiving checkouts let MSBuild/NBGV construct their own local helper context;
Workflow does not replay helper coordinates or implement version caching.

Native provider associations can include finite sibling inputs. The workload
autoimport locator's successful returned `Sdk` directory and its actual
`AutoImport.props` import associate that specific parent pack with the native SDK
provider. This does not admit all installed packs, expand unrelated SDK roots or
implement workload resolution. Preserve the native locator identity and version
field without claiming an exact pack version; unknown external inputs remain
unsupported. The [application entry](../../../../private/app/workflow-delivery/README.md)
retains the source basis and implementation boundary. At the separate
control-build binlog boundary, the selected SDK's literal autoimport and its
native evaluation/import lineage associate only observed, connected files
inside the specific pack. This supplies exact dependency membership without
reconstructing SDK results or enumerating installed pack files.

Exact native locked-restore configuration contributors remain source inputs when
committed and invocation-local environment facts when outside the checkout.
Require absolute existing files and preserve the restore role; this association
adds no generic dependency membership or configuration-directory authority.

Bind version-input discovery to each supported effective native
`GitVersionBaseDirectory`, not necessarily the product directory. Union native
business variant consumers. Traversal's unused self-version metadata does not
widen product selection; genuine root configuration/provider/helper inputs retain
their consumers. Business projects may use a root version subject.
Keep this physical subject local to the endpoint; portable checks retain only
product globals. Quality adoption continues to use the product directory.
Unsupported external or ambiguous native version contexts remain unavailable.

`ProjectFacts.OwnedPaths` is optional: null retains existing nearest-directory
ownership; a populated array names exact committed endpoint coordinates. The
.NET adapter derives ordinary regions from native project directories, preserving
nearest-project ownership and multiple projects sharing a directory, and narrows
them with native evaluated default excludes using the official MSBuild glob
matcher. The traversal entry is a discovery input, never a repository-root owner.
Explicit native input consumers remain additional relations even when ordinary
ownership excludes a path. Union consumers from all native nodes and both
endpoints; unsupported or ambiguous necessary facts fail planning.

These ordinary regions are a business ownership policy, not complete compiler or
custom-target I/O discovery. For example, ordinary npm fixture source excluded by
the .NET test project's default excludes does not select that parent; its exact
embedded `version.json` still selects the .NET resource consumer. Concrete
repository producers outside ordinary ownership require a reviewed native
adapter. Trust SDK/package behavior without an application task allowlist or
dynamic admission policy.

### Control-Test Resource Consumer

The existing general .NET CI owner must be selected when a changed input is
embedded by `tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj`.
Read `EmbeddedResource` at both comparison revisions through supported native
MSBuild evaluation. This is one concrete input-consumer projection, not a
complete .NET graph or another resource declaration. Preserve the existing owner.

Evaluate the owner's default property context in one query: set
`Configuration=Debug`, with no TFM/RID global override, and request
`MSBuildProjectFullPath`, `Configuration`, `TargetFramework`, `TargetFrameworks`,
`RuntimeIdentifier`, `RuntimeIdentifiers` and `EmbeddedResource` together.
Use `-noAutoResponse`; request no target, target result, restore or repair.
The initial supported shape has one nonempty effective TFM, empty multiple-TFM
and multiple-RID declarations and a possibly empty effective RID. Reject an
unsupported relevant shape. Reuse the native reader's project/path/item
validation and original committed spelling; do not build a dimension service.
Each endpoint uses its own committed inputs, SDK and locked native preparation.

At the C#-to-Python process boundary, the required response carries the exact
native comparison and each endpoint's revision, project, concrete effective
dimension and committed resource array. Represent confirmed base-project absence
explicitly; missing facts or failed evaluation are not absence. The candidate
must retain the named owner; moving or replacing it requires an atomic reviewed
owner change. An absent/nonregular candidate project, conflicting identity,
missing materialized input or unavailable native preparation fails selection.

The receiving selector binds the response to the checked-out candidate and
Git-resolved basis, then intersects changed paths with both resource input sets.
A hit adds the existing .NET owner with its changed path, project and revision
reason to the final scope. Retain other selector reasons and owners. Emit
successful scope outputs only after required response validation and final
selection; no later executor override or automatic full fallback is permitted.
Explicit manual full retains the native `basis == candidate` comparison identity.
The process response is a consumed input, not an internal trust or approval record.

### GitHub Event Comparison

The finite event adapter reads the native `GITHUB_EVENT_PATH` JSON and the
platform's tested candidate from ordinary process arguments. It consumes only
the relevant native fields; unrelated payload fields and workflow-selected PR
activities do not require an application mirror.

| Native event                       | Comparison basis        | Candidate                                  | Mode      |
| ---------------------------------- | ----------------------- | ------------------------------------------ | --------- |
| `pull_request`                     | `pull_request.base.sha` | Tested `github.sha` merge commit           | Selective |
| `merge_group` / `checks_requested` | `merge_group.base_sha`  | `merge_group.head_sha`, equal tested SHA   | Selective |
| `push`                             | `before`                | `after`, equal tested SHA; `deleted=false` | Selective |
| `workflow_dispatch`                | Tested candidate        | Tested candidate                           | Full      |

Native identities must be full nonzero 40- or 64-character hexadecimal commit
IDs, normalized for comparison. Missing, malformed or conflicting relevant
fields, unsupported events/actions and creation/deletion pushes without a
supported incremental basis fail. They do not select full mode automatically.
The native Git reader subsequently resolves actual objects and compares the
endpoints; the adapter neither requires ancestry nor reconstructs a merge base
or group members. Force-push endpoint comparison remains valid when both
objects are available. PR source head does not replace the tested merge.

The selected semantics follow pinned
[GitHub Actions event documentation](https://github.com/github/docs/blob/2bd66de8cea336061c9ea060c9b37385136e6ab3/content/actions/reference/workflows-and-actions/events-that-trigger-workflows.md)
and the consumed fields in the pinned
[PR](https://github.com/octokit/webhooks/blob/aa80c24a3f55354465b8b1438d33dd5602956d0d/payload-schemas/api.github.com/common/pull-request.schema.json),
[merge-group](https://github.com/octokit/webhooks/blob/aa80c24a3f55354465b8b1438d33dd5602956d0d/payload-schemas/api.github.com/merge_group/checks_requested.schema.json)
and [push](https://github.com/octokit/webhooks/blob/aa80c24a3f55354465b8b1438d33dd5602956d0d/payload-schemas/api.github.com/push/event.schema.json)
schemas. Supplied-payload tests establish the adapter contract, not hosted event
execution or caller cutover. Group ownership, checkout preparation and transfers
remain separate integration responsibilities.

### Selection Algorithm

1. Resolve the candidate tree and event-appropriate comparison through the finite
   mapping above. Explicit full
   validation is a distinct mode with its supported coverage stated.
2. Read changed paths, including both sides of rename/deletion. Resolve ownership
   and relevant relations in the comparison basis and candidate. Changes to
   dependency declarations must not erase the evidence needed to select consumers.
3. Select directly affected projects and shared-input consumers, including valid
   paired direct-selection reasons above. Follow required
   transitive reverse dependencies and quality-consumer relations from both sides.
4. Resolve surviving work against the candidate revision. A deleted project is
   not a runnable target; its surviving consumers and changed declarations still
   require analysis. Do not require an old release artifact for CI.
5. Resolve affected Release Units and include all their required variants. Resolve
   effective checks for selected projects and their supporting test targets.
6. Deduplicate identical check/target/dimension combinations, retain all selection
   reasons and close real prerequisites. Reject unresolved scope before execution.

The initial precision is project and actual input-consumer scope. Ordinary source
changes exclude known unrelated projects. A shared lockfile, toolchain or native
workspace operation may select all its actual consumers when that is the supported
semantic input boundary. Explain the broad selection. Use narrower native facts
when available; do not discard them merely to simplify planning.

Package-entry semantic diffing for every lock format and symbol-level impact are
not initial obligations. This accepts wider dependency-maintenance runs while
preserving selective ordinary changes. Unknown paths, failed evaluation, missing
dependencies or unsupported relevant structures block planning; none becomes a
full-run fallback. Explicit full validation also reports unresolved required
facts instead of claiming unsupported repository-wide coverage.

Version and impact input declarations must include genuine shared build and
dependency inputs. A selection optimization cannot make a version-affecting
dependency invisible to the project's native version contract.

### Declarations and Quality Autonomy

Keep project YAML selection of registered ecosystem presets. Resolve it from the
native manifest directory toward the root: the nearest descriptor containing the
ecosystem entry wins; an unrelated ecosystem entry does not stop the search.
Replacement is explicit, with no implicit ancestor merge. A project-specific
registered preset is valid. Do not introduce an arbitrary YAML command language.
If selected work needs a quality contract and no effective selection resolves,
planning fails rather than inventing a default checklist or omitting its checks.
An explicit `NativeRetained` origin also permits the concrete .NET adapter to
resolve existing general-CI Build/Test behavior for unadopted projects without a
new YAML declaration. This requires nonempty resolved checks and native capability
and variant facts; it is never a fallback from a missing or invalid adopted
preset. Node declaration behavior remains preset-based.

Resolved checks retain `Preset` and/or `NativeRetained` origins. Actual adopted
preset names remain in `QualityPresets`; native-only checks have an empty preset
collection. Identical check keys union origins, actual presets and selection
reasons, with required status combined by OR. Conflicting definitions, runners,
dimensions or prerequisites still fail. This distinction expresses existing
behavior, not a new policy registry or policy-version lifecycle.

Registered checks distinguish required and advisory behavior. A check declared
required must resolve; a supported conditional-on-presence check becomes required
when present. Absence is valid only when the selected contract says so. Stronger
required semantics use an identifiable new preset with explicit project adoption;
equivalent implementation fixes need no new policy lifecycle.

Release Unit declarations supply the product boundary, entry points, variants,
outputs and otherwise unavailable relations. They do not duplicate native project
membership, package names, frameworks or version values. Release channel and
destination selection remain explicit. Removed dynamic Governance content is not
relocated into these declarations.

## CI Work and Result Contract

CI produces a finite work list before execution. Each selected item identifies:

- the concrete project, native aggregate or artifact target;
- operation and dimensions, suitable runner/toolchain and required inputs;
- required/advisory status and its resolved preset/native-retained origin;
- real prerequisites and the reasons it was selected.

The workflow has static planning, execution and collection phases. Planning can
supply runner matrices and selected lists; it does not create an arbitrary runtime
graph or queue. Native fact collection may use separate runners where ecosystem
evaluation requires them. These are real transfers, not proof-only phases.

Group compatible work around a native execution target. Build and check its
outputs in the same job when there is no runner, permission or independent-lifetime
reason to split them. A Python build can produce wheel and sdist together; one
native invocation can produce multiple declared outputs or establish several
checks if its contract actually covers them. Output count does not determine job
count. Cache sharing is not a semantic prerequisite.

Keep distinct selected checks visible even when they reuse a build. A successful
command cannot stand in for a check it never ran. Native build tools own their
project prerequisite ordering; the CLI must not schedule individual compilation
nodes to reproduce it.

The required CI result joins the expected work with candidate-bound results.
Every required result must be present and satisfied. Failure, conflicting results,
cancellation, timeout, skipping or missing required work prevents success.
Advisory results remain visible without changing the required result. Collection
must account for failed dependencies; platform cancellation may still prevent a
final report and must not be interpreted as success.

HK retains source/configuration conformance. The control application's tests have
one ordinary CI owner, selected by actual inputs/dependencies. CI never imports
Release authority or exposes publication capability. General C# execution follows
the repository's Windows default unless a declared variant selects another runner.

### Native Python CI Group

The existing Linux Python job selects and executes one original candidate-bound plan for
configured pytest targets and adopted distribution checks. This group composes
the native graph collector, pytest configuration/target binding, repository input
associations and paired fact assembler already defined above. It adds no project,
dependency, test-target or backend declaration. The
[caller mapping](./migration-strategy.md#python-quality-and-caller-mapping)
retains the concrete repository and special-job consumers.

The Windows scope job transfers the exact event comparison and explicit
full/incremental mode, together with the complete candidate control distribution.
The existing Linux Python job performs the first Python selection: it materializes
both exact endpoints and collects their native facts for its actual Linux product
interpreter and supported operation dimensions. Windows interpreter activity
cannot substitute for Linux marker answers. The selected control distribution
and native query tools must run in that context; unavailable necessary capability
fails analysis rather than introducing platform emulation.

Planning binds each materialized endpoint to its own native membership,
configuration, interpreter/platform, operation groups and committed inputs.
Prepare a separately owned query-tool environment for each endpoint with native
`uv sync --frozen --only-group dev --only-group workflow-delivery-python --no-editable`,
noneditable workspace dependency installation and default build isolation. Use
that endpoint's committed root groups, lock and source configuration, and the
bound Linux interpreter. UV owns the selected tool-group closure and any native
backend preparation; no separate library or backend roster is maintained. The
declared plugin may be built as a query-tool dependency without making unrelated
workspace members product or quality owners. Passive query execution remains
distinct from this tool installation. Keep the installed helper environment
separate from product/test/package environments and from the owned absent
`UV_PROJECT_ENVIRONMENT` required by native metadata/activity queries.

Supply the candidate control version's existing `Native/Python/uv` supplement
compiled for Linux using `cargo build --locked`, its committed Cargo manifests,
lock, `rust-toolchain.toml` and sources, and an owned output directory. Transfer
candidate control sources/resources rather than a Windows native executable.
Bind helper source, toolchain, executable and runtime identities to both endpoint
queries; each query retains its own endpoint configuration, lock, groups and
actual Linux interpreter facts. Preserve native configuration preflight,
ambient-setting/error behavior and owned credential-free query state. Necessary
tool preparation or compilation failure, timeout or cancellation stops analysis.
This proposed preparation still needs actual caller qualification; the stopped
graph/enrichment observations do not establish installation or caller success.

Reuse the accepted native metadata, passive extraction, UV supplement and pytest query;
candidate facts cannot substitute for missing basis facts. Bind actual quality
adoption and release declarations before complete paired selection. Existing
shared-core closure selects surviving checks and supplies initiating path/revision
reasons. Explicit full mode states the supported group it covers; an incremental
analysis failure cannot become a full run.

Begin that analysis for every valid comparison, independently of legacy Windows
Python applicability, target, package or special-input heuristics. Successful
incremental analysis may establish that no checks are affected: report the
explainable no-work scope and perform no product/test/package preparation or
execution. Query-tool preparation remains separate. Missing facts, failed
transfer, cancellation or timeout cannot become successful non-applicability.
For selected work, keep the existing nonempty selected-check requirement.

The Linux job invokes shared selection once, then passes its original plan to
the finite executor in the same job. At that boundary, preserve candidate-native
membership
and pytest target bindings, tool/operation context and complete candidate control
distribution. Validate their agreement with the receiving exact candidate before
product dispatch. Native member names come from metadata, and target/member
coordinates come from the existing native target binding. This request validation
must not resolve dependencies, choose targets again or introduce a trust record.
Selected V3 consumers prepare mise's native configuration trust in their owned
HOME before HK/Pkl queries. Mise owns that local tool state.

The finite executor partitions that plan into native-retained `python/pytest-v1`
checks and the registered Python distribution preset. Validate both supported
partitions and their complete prerequisites before either executes. Keep one
required result per original check and collect against the original mixed plan;
neither executor may remove or substitute the other partition. Empty partitions
need no preparation. This follows the existing .NET group composition rather
than introducing a task scheduler.

For a nonempty pytest partition, prepare a separate test environment with native
`uv sync --frozen`, selecting the root context plus every selected quality-member
context. Resolve names from supplied native membership, retain the interpreter
and native default-group operation, and let UV own installation and dependency
semantics. Standalone repository targets require the root context; member targets
also require their bound member. Dependency producers do not become automatic
quality owners. Do not substitute the distribution-tool environment or force
unrelated input-only members to build.

Execute each original selected target with
`uv run --no-project --python <prepared-environment> python -m pytest`, retaining
the supported native configuration/options. Set `UV_PROJECT_ENVIRONMENT` only
for preparation and remove it from product execution so nested project-native
UV calls do not retarget the prepared environment. Product preparation and
execution retain ordinary Python import behavior; query-only `PYTHONSAFEPATH`
is removed at that boundary while native query isolation remains unchanged.
One invocation per target supplies its distinct required outcome while sharing
preparation. A preparation failure, timeout or cancellation leaves every affected
required target unsatisfied; it cannot produce an empty successful result. Keep
independent package obligations visible even when pytest fails. The package
partition retains its separate phase-specific preparation, original archives and
four required distribution checks under the contract below.

Controlled validation covers root-only and mixed member preparation, native
name/target agreement, explicit full versus incremental selection, complete
partition/result transfer and failures/cancellation. Ordinary authorized PR CI
must qualify the complete caller group before cutover. The existing local full
runner and smoke PR caller remain until their selected/full preparation and check
consumers switch together. No local package experiment, historical campaign,
Release or publication authority follows from this integration contract.

## Builds, Packages and Consumers

### Native Python Package Execution

Use the private C# application to expand adopted package checks, invoke native
operations and join their outcomes through existing `CheckSpec`, `PackageTarget`
and result contracts. UV owns frozen group/workspace selection, phase-specific native installation,
PEP 517 backend requirements and build dispatch. The maintained
`nbgv-python` plugin owns official NBGV invocation. Do not port these semantics,
maintain a second backend-requirement declaration, rewrite project version
metadata or inject a replacement version engine. The
[source assessment](./research/python-native-packaging.md) distinguishes these
selected boundaries from unqualified runtime behavior.

For the current adopted Python distribution contract, prepare an owned native
project environment from the existing root `dev` and `workflow-delivery-python`
groups and synchronized lock. Use one frozen sync selecting both groups with
UV's default build isolation and the workspace's declared editability. UV owns
declared and backend-reported auxiliary build requirements, including editable
tool preparation. Do not precede it with a separate root-dev bootstrap, disable
that native requirement preparation, maintain a backend roster or manually
orchestrate requirement hooks. Keep the selected installed tools through
completion of the original wheel/sdist set: native source-build requirements
retain workspace source and editability semantics. Exact sync removes omitted
tools, so selection includes the development backend tools as well as the
build/plugin group. The root-dev footprint is existing infrastructure,
not an application-maintained minimal backend roster. Actual selection and
installation must establish that unrelated workspace products are not built.
This package-tool preparation does not replace root pytest's selected/full
member preparation obligations.

Frozen group selection owns the installed tools; it does not promise that
auxiliary isolated backend resolution is lock-frozen. Preserve the distinction
and any relevant native builder/version input in qualification evidence. Product
builds below use the prepared selected tools and shared native build mode.
Neither a frozen sync nor the lock alone establishes same-input byte
reproducibility or Release/recovery qualification.

Build the complete declared wheel/sdist set with native `uv build`, the selected
package and prepared interpreter. Enable the pinned native
`build-dependency-check` preview with shared build mode; it checks declared,
backend-reported and transitive requirements. Missing native prerequisites fail;
there is no skip flag or application fallback. Frozen preparation remains the
selected installation authority. Native Directory requirements still enforce
editability equality under permissive checking; compatible-version checks do not
prove original-archive identity. A changed tool or necessary
backend shape requires the affected native-contract review before use.

Consume normal native distribution metadata through maintained packaging and
Python library interfaces. Preserve necessary archive path, size and ambiguous
member checks without requiring a universal literal payload, tar encoding or
V3 witness. Distribution spelling does not determine its import module. The
registered project quality implementation retains actual payload and installed
API expectations; those expectations are not universal package-format rules.

Wheel consumption installs the original wheel in a fresh owned environment.
Before Git-free sdist consumption, reconcile the same owned package-tool
environment with one frozen native sync selecting both groups, shared build mode
and `--no-editable`. Require successful reconciliation and unchanged original
archive bytes before continuing; failure, cancellation or timeout fails the sdist
check and prevents its downstream consumer operations. This does not erase an
independently completed original-wheel result.

Git-free sdist consumption builds the original archive file from a neutral
directory outside Git using the reconciled noneditable backend/plugin
distributions, then installs its resulting wheel into another clean environment.
Pass the original archive file, rather than an extracted directory, to UV: its
native archive path uses ordinary declared PEP 517 requirements without the
source-directory workspace lowering. This source-supported distinction remains
subject to complete native qualification. Neither consumer
implicitly imports the source checkout. Native installed metadata, import origin
and the applicable project API check must agree with the intended native version
and outputs; command exit alone cannot satisfy them. Native Hatchling metadata
reuse is a source-supported candidate for retaining the sdist version, not an
accepted Git-free runtime result. Do not rewrite `pyproject.toml` to force success.

Keep complete build, contents, wheel install/import and Git-free sdist
build/install/import obligations individually visible even when one native
build serves multiple checks. Native fact selection remains separate from
execution. The exact tools/helper/harness, environment/download/cache effects,
finite command/diagnostic/time/state bounds, stop and owned cleanup must receive
independent review and protected protocol acceptance before native qualification.
Earlier graph/enrichment campaigns supply no package allowance. Missing necessary
native capability stops for owner disposition without reduced check coverage.

Build and qualification operations receive the selected revision, native entry
point, dimensions, expected native version projections and relevant locked inputs.
Planning may calculate NBGV; builds may calculate it again under the same relevant
Git/ref/history, configuration, dependency and supported toolchain inputs. Required
package/assembly projections must agree. Missing facts and disagreement fail the
operation; no fallback version or execution suffix is invented.

Use native deterministic build configuration, stable paths/timestamps and locked
dependencies. Routine publication does not require a duplicate build. Same-input
recovery must reproduce original bytes; different commits need not produce equal
bytes merely because their product version is equal. Python source distributions
may need native static version materialization for Git-free downstream builds.

Inspect normal native package output with maintained ecosystem tools/libraries.
Qualification checks coordinate/version, the complete expected output set, declared
payload/dependency requirements and clean native consumption. Product-specific
extra content rules belong to that project's registered quality contract.

Do not promote a smoke package's literal file list, USTAR physical encoding,
padding or UID/GID into universal package validity rules. Retain actual extraction
protections for paths, sizes and ambiguous members where the chosen reader needs
them. A native library's format support is not a claim that every extraction mode
is safe. Preserve appropriate native source metadata without a V3 witness or an
identical mandatory commit field across ecosystems.

Local clean consumers use the complete original package set without source-tree
references or populated consumer caches masking omissions. Release performs its
own build and qualification; it does not promote CI artifacts or results.

### Remote Consumer Conditions

Ordinary Release requires local package qualification plus authoritative remote
metadata/visibility and original-byte verification. A clean remote native consumer
is additionally required when the destination/product contract needs it to prove
a distinct property, such as actual feed resolution. It runs without publication
capability and contributes a required result in the same active request.

For each destination integration, enumerate the completion conditions and identify
which are established by local consumption, remote readback or a remote native
consumer. No applicable consumer condition may disappear merely because the common
flow is smaller. Byte equality does not prove credentials, feed routing or native
resolution work. Conversely, a completed proving campaign does not automatically
create a generic remote-install step for every future release.

This choice avoids redundant recurring installation where it adds no required
property, while accepting less uniform end-to-end exercise than always reinstalling
every remote output. Historical campaigns retain all their original consumer
requirements, evidence and terminal outcomes. A later consumer audit cannot rewrite
an already terminal request.

## Release Operations and State

### Request and Preparation

A request contains one immutable source/control revision, Release Unit, channel,
destination and live/dry-run mode. Resolve the native version, complete output set
and applicable quality/consumer conditions before execution. Live Official checks
its eligible official ref/tag; Buddy permits configured development refs. Dry run
can use development control code and has no write capability. Unsupported live job
reruns are rejected; ordinary recovery uses a fresh dispatch.

Preparation builds and qualifies the complete set even when recovery may need to
upload only a missing file. It exposes the qualified files, native coordinates,
destination and maximum intended operations before the native gate. The publisher
can remove a now-unnecessary operation after observation but cannot enlarge that
reviewed request.

Inside one invocation these are ordinary values. Persist preparation only for its
actual review, job transfer and operation-window consumers. Separate Execution,
Attempt, Approval Bundle, Authorization and marker aggregates are unnecessary.

### Destination Interface

Destination integrations implement concrete operations with the following contract:

| Operation                  | Input                                                           | Output and responsibility                                                                                     |
| -------------------------- | --------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Resolve coordinate         | Selected destination and native package identity                | Physical registry, normalized package/version, expected file identities and supported mode                    |
| Observe                    | Coordinate and qualified expected files                         | Per-file absent/exact/conflicting/unknown state, required native metadata, visibility and bounded diagnostics |
| Determine supported writes | Current observation and expected set                            | Whether native creation or exact-subset completion is supported, and the ordered missing files                |
| Upload once                | One still-needed qualified original and scoped write capability | Actual success/failure/ambiguous command response and available effect facts; no blind resend                 |
| Verify                     | Expected complete state and applicable completion conditions    | Bounded authoritative observations and required checks, with remaining uncertainty                            |

Release owns permission to proceed and business completion. The adapter owns native
identity, endpoint, response and partial-set semantics. Do not encode npm tags as
universal identity or Python's two-file behavior as a universal transaction. A
supported interface does not require each method to have its own class or record.

Each concrete integration specifies native non-overwrite reliance, required files,
metadata/readback, credential scope, safe redirects, visibility/read bounds and
partial-set behavior. Use operation deadlines and native capability lifetimes;
do not reinstate a dynamic Governance expiry. Different destinations may need
different read budgets. Necessary guarantees cannot be supplied by application
locks or extra probe records.

### Decision Table

| Observed condition                                         | Write behavior                                                        | Business result                                                     |
| ---------------------------------------------------------- | --------------------------------------------------------------------- | ------------------------------------------------------------------- |
| Complete exact state and all required checks               | No write or write approval                                            | Satisfied by observed state                                         |
| Absent required state with supported creation              | Native gate, fresh observation, then only still-needed writes         | Pending until complete required verification                        |
| Verified exact subset supported by the destination         | Fresh request qualifies the complete set and plans only missing files | Pending until the full set is verified                              |
| Conflicting bytes or unknown required state                | No mutation                                                           | Unsatisfied or unknown, with the blocking reason                    |
| Acknowledged upload and complete required verification     | No additional write                                                   | Satisfied; preserve the acknowledged command result                 |
| Failed/ambiguous upload and complete required verification | Stop mutation; bounded reads only                                     | Satisfied by observed state; preserve the command failure/ambiguity |
| Failed/ambiguous upload with incomplete/uncertain state    | Stop mutation; bounded reads only                                     | Unsatisfied or unknown; fresh recovery may be possible              |
| Interrupted publisher or missing result                    | No inferred retry                                                     | Effects may remain unknown unless native facts establish non-start  |

For Python, ambiguity after wheel upload stops the write sequence even if a later
read confirms the wheel. It does not authorize uploading sdist in that same
sequence. If the complete intended set is independently verified, the active
request can be satisfied; otherwise a fresh request can consider supported
missing-file completion. Successful acknowledged uploads can continue in their
declared order only after the required intermediate verification.

An initially exact request and a newly published request use the same completion
predicate. There is no separate exact-satisfaction certificate. Required metadata,
file bytes and applicable consumer results must all be established; a version
label or successful upload alone is insufficient.

### Result and Exit Behavior

Keep four facts separate: attempted effects, raw command outcomes, observed remote
state and business satisfaction. Completion established by observation does not
claim that this invocation created remote objects. Missing application records
never prove absence of effects.

The required business check succeeds when the active request establishes all
completion conditions, including after a handled upload error or lost response.
Show the abnormal command outcome explicitly and identify observed satisfaction.
Handled upload outcomes are returned as data for result evaluation. An unexpected
process crash remains a native execution failure; do not blanket-ignore job errors
or overwrite platform conclusions. A business result and the overall workflow
conclusion may differ when an independent infrastructure failure remains.

Final evaluation accounts for missing transfers/checks and interruption. Required
missing evidence blocks a satisfaction claim; optional telemetry failure does not
undo an otherwise established result. Once terminal, an outcome stays terminal.
A later request never changes it retrospectively.

### Recovery

Fresh recovery uses the original source/control revision and relevant inputs,
rebuilds and qualifies its own complete set, and observes current destination state.
It obtains current native authorization only if a write remains. Do not reuse
old approval, old output files or a prior model as authority. A tooling fix is a
new revision; it cannot silently be combined with the old source for recovery.

Conflicting bytes, unsupported partial state, missing necessary facts or required
administrative operations need a separate operator decision. Normal release does
not delete, restore, retarget, change access or compensate across registries. No
historical run discovery, permanent ledger or general repair engine is required.

## Workflow, Permission and Transfer Boundaries

Build the CLI from the selected control revision before publication capability
exists. Transfer the prepared control artifact separately from product-mutated
output. Evaluation, package scripts, builds and consumers run in jobs/processes
that cannot obtain publication capability or mutate the publisher's context.
Installing write credentials after product execution in the same job is not
sufficient isolation. Privileged metadata readers must not execute product code.

The native Environment gate belongs on the publisher job. Its effective settings
are the policy; do not mirror reviewers, replay approval/deployment history or
maintain a sentinel certificate. Destination-specific jobs remain appropriate for
different permissions and runners. A common Release use case does not require a
job with the union of every destination's privileges.

Logical phases are prepare/qualify, observe/describe, gated publication when needed,
and verification/result. Combine or split them according to actual runner,
credential, product-execution and transfer boundaries. Verification consumers run
outside the privileged publisher. Exact-state and dry-run paths do not enter a
write gate; dry run reports hypothetical operations and actual validation limits.

Serialize this repository's publication operation by physical registry and normalized
package, including required read-before-write and verification. If that operation
spans publisher and unprivileged consumer jobs, use a containing native workflow
serialization boundary that covers them rather than releasing a job lock early.
Do not partition colliding resources solely by channel or version, automatically
cancel an active publication, or promise FIFO/lossless queue behavior. External
writers remain governed by registry guarantees, not an application-wide lock.

### Data Crossing Boundaries

| Transfer                                 | Minimum information                                                                                                                                 | Consumer validation                                                    |
| ---------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Native facts from another runner/process | Revision, relevant entry points, facts, errors and coverage limitations                                                                             | Correct subject and required fact coverage                             |
| CI plan and task results                 | Candidate/comparison, selected work/reasons, required checks, concrete result subjects and outcomes                                                 | Result corresponds to selected work; complete required closure         |
| Release preparation                      | Source/control revision, unit/channel/destination/mode, native versions, complete original files, qualification results and maximum intended writes | Actual files and qualified subject match the reviewed request          |
| Prepared CLI                             | Selected control revision and immutable output with its producer/integrity binding                                                                  | Publisher executes the intended control artifact without rebuilding it |
| Publication and verification result      | Attempted operations, raw responses/outcomes, required observations/checks and unresolved effects                                                   | Completion is supported; missing data does not prove non-start         |

These are consumer-specific typed contracts, not a universal envelope or one file
per row. Keep data in memory when no real boundary requires transfer. Executors
need their work, not a serialized copy of the entire repository graph.

Use native immutable artifact IDs and relevant producer/integrity bindings; reject
missing, substituted or mismatched inputs. Display names and latest-artifact lookup
are not identity. Trust successful native upload; the actual receiving consumer
validates its download. Do not upload/download/re-admit every producer intermediate
just to prove the platform accepted it. Package byte identity remains necessary
for qualification and remote comparison even when native transport owns integrity.

Retain qualified files and required request/results for approval waits and the
supported operation window within platform limits. Concrete durations are workflow
settings, reviewed with their caller. Expired required inputs block that operation.
Fresh same-revision recovery may use current native state under its destination
contract without requiring a permanent application record store.

## Validation and Implementation Transition

### Behavioral Validation Basis

| Contract   | Required cases                                                                                                          | Primary validation boundary                                       |
| ---------- | ----------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| Impact     | Unrelated source; transitive consumers; shared input; deletion/rename; changed dependency edge; unknown scope           | Selection scenarios and native fact integration                   |
| Quality    | Nearest matching ecosystem; explicit stronger-preset adoption; required missing/advisory failed results                 | Policy resolution and CI scenarios                                |
| Execution  | Complete selected variants/checks; shared prerequisites; runner mismatch; failed/missing execution                      | Work planning and real workflow transfers                         |
| Package    | Required native version agreement; complete outputs; clean consumers; same-input reproducibility                        | Ecosystem integration and package scenarios                       |
| Isolation  | Prepared selected control; no product execution with write capability; exact qualified inputs                           | Actual job/process and artifact boundaries                        |
| Completion | Initially exact; absent; supported subset; conflict; delayed visibility; upload error with complete/incomplete readback | Destination contract and Release state scenarios                  |
| Recovery   | Interruption; unknown effects; fresh original-revision rebuild; only supported missing-file writes                      | End-to-end use-case scenarios with bounded dependency substitutes |
| Retention  | Missing/expired required transfer; optional telemetry failure; historical outcomes remain unchanged                     | Transfer/result scenarios and evidence review                     |

Use focused unit tests for selection and outcome algorithms, scenario tests for
journeys and contract tests for actual transfers. Exercise native integrations
where their behavior matters. Do not preserve tests solely enforcing retired
record chains or duplicating platform authorization. Measure ordinary-PR latency
separately from legitimately broad changes; fast full execution is not successful
impact selection.

Local/controlled results do not establish live platform acceptance. The owner's
[Windows assumption](./research/contraction-feasibility.md#decision-and-evidence-scope)
remains accepted without a separate experiment gate. Completed campaigns remain
closed; no implementation validation can silently consume a spent external grant.

### Source Basis and Migration Groups

Static inspection at [the pre-cutover source][source-base] found that the
[Node Provider][node-provider] rejects workspace closure, the
[.NET Provider][dotnet-provider] rejects project references and additional
framework/RID scope, and the [Python Provider][python-provider] validates a literal
smoke shape. The [Node CI][node-ci] has four fixed lanes; Python/Ruby use fixed
check lists. [Python][python-publication] and [Ruby][ruby-publication] normal
post-upload reads currently follow successful upload only.
These are source findings, not empirical evidence of replacement feasibility.

Consequently the rewrite must add supported native ownership/dependency coverage
and observation-based completion while removing obsolete proof machinery. A
literal class-for-class language port would preserve the current limitations.

The [implementation plan](./migration-strategy.md) owns the concrete project and
CLI/workflow caller inventory, integration payloads, destination conditions,
behavior/evidence mapping and dependency-ordered transitions. Keep that mapping
with actual caller changes; do not maintain a second inventory here.

Old records or code survive only for actual consumers. Pin historical source when
it serves an evidence reader, preserving original outcomes and provenance. Retiring
the five former MLD files uses this mechanism; runtime and ecosystem LLD retirement
still follows the corresponding actual caller transition.

[source-base]: https://github.com/hcoona/three/tree/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3
[node-provider]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/node_provider.py#L1264
[dotnet-provider]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/dotnet_provider.py#L973
[python-provider]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/python_provider.py#L192
[node-ci]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/ci/rules.py#L13
[python-publication]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/python_publication.py#L735
[ruby-publication]: https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/ruby_execution.py#L264
