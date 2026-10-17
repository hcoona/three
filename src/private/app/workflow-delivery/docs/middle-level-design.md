# Workflow Delivery v3 Middle-Level Design

## Scope and Authority

This design refines the [HLD](high-level-design.md) into module,
selection, execution, transfer and outcome contracts for the internal C# CLI.
It implements the [requirements](requirements.md). Implementers and reviewers
use it to determine the contracts the implementation must keep. Source/native/hosted
qualification retains its actual scope; this design grants no publication
authority.

Former MLDs, ecosystem LLDs and completed campaigns keep their original scope
under the [historical source rule](README.md#historical-source-rule); their
record chains and extension frameworks are not requirements for this design.

This document owns the common middle-level contracts. Concrete native extraction
coverage, destination read limits and workflow wiring are reviewed with their
implementation scope. Missing necessary native capabilities remain unsupported;
implementation details cannot silently weaken these contracts. The
[handoff](agent-handoff.md) retains the operating and evidence limits.

The per-ecosystem integration contracts for [.NET and NuGet](integration/dotnet.md),
[Node and npm](integration/node.md), [Python](integration/python.md) and
[Ruby](integration/ruby.md) own each ecosystem's native fact extraction, CI group,
Release caller and destination conditions; the
[common integration tables](integration/common.md) own the Official channel scope
and CLI/boundary payloads (see [integration mapping](#integration-mapping)). Each group requires its own applicable native and
hosted qualification; source design acceptance does not establish those results.

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

The HLD's [native integration boundary](high-level-design.md#native-integrations)
governs every ecosystem module. Call a maintained ecosystem library in its native
language, or a supported tool interface, when that is how the required semantics
are exposed. A helper is part of this application and its selected control
revision, not another policy service. Transfer typed results only at an actual
process or runner boundary; field spelling and internal interfaces may change
with their producers and consumers without compatibility aliases.

### Owned Projects

| Owner                                                               | Responsibility                                                                                                             |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `src/private/app/workflow-delivery/WorkflowDelivery.csproj`         | Internal CI and Release entry point, native readers, selection, execution, package and destination operations              |
| `tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj` | Managed scenario, algorithm, transfer and native integration tests                                                         |
| `tests/private/app/workflow-delivery/Native/{Python,Ruby}/`         | Tests of the corresponding native-language helpers                                                                         |
| `tests/private/app/workflow-delivery/fixtures/products/`            | Four ordinary npm, NuGet, Python and Ruby package inputs, with native manifests, versions and quality/release declarations |
| `src/private/app/workflow-delivery/docs/`                           | Current requirements/design/contracts and retained research/evidence readers                                               |
| `src/public/lib/nbgv-python/` and `src/sample/nbgv-hatch-demo/`     | Independent public NBGV component and sample, retaining their own consumers and authorities                                |

Module names in this design are source organization, not additional applications.
Native product fixtures retain their package coordinates but have no old/new
byte or version continuity promise. Their placement must preserve deliberate
native version bases, quality ancestry, workspace membership and build imports.
Root traversal retains normal native project semantics; moving a fixture does
not justify a separate discovery or scheduler service.

### Native Helper and Caller Boundaries

Complete .NET, Python and Ruby CI group execution owns its original plan's outcome.
CLI exit and process receivers use that bound outcome, retaining comparison,
candidate, scope, raw results and diagnostics. Receivers reject missing or malformed
outcomes rather than launching the same control to reevaluate its unchanged results.
Pre-execution plan validation and Node's join across independent runtime results
remain distinct.

The application owns ordinary Release job inputs, request subject binding,
original transfer, phase configuration and required write-state output. Existing
Ruby/Python/npm/NuGet workflows invoke `release job-inputs <ecosystem> <operation>` through
their already distributed candidate control. They do not carry a separate
language-specific Release caller implementation. Actions retains job ordering,
Environment, credentials, concurrency and immutable artifact transfer. npm's
single-job Official simulation creates a consumed typed request. Same-run
immutable artifact IDs carry the producer run; each later job checks the received
preparation once against the request subject rather than restating run and
attempt in separate producer records. Live ref eligibility has one definition,
applied where the request is formed and again by the privileged publisher.
Every `complete-*` command (npm, NuGet, Python and Ruby) consumes the retained
initial observation to distinguish legitimate no-write completion from missing
required publication information. NuGet callers forward native process
termination; typed completion owns result interpretation, without a shell enum
mirror.

Ruby helpers retain RubyGems/Bundler evaluation and native source reads; local
installation/API qualification uses the native gem and Ruby commands. Python helpers retain PyPA/NBGV library bindings over
the pinned UV CLI; .NET and Node use their supported native process interfaces.
A helper is justified by a required library interface, not by the language label.
Adding a distinct product can require a concrete recipe; no generic plugin or
automatic adoption capability is implied by these four implemented products.

[Native wire schemas](../Native/contracts/) are generated from the actual
application serializer metadata by `native-message-schemas <directory>`.
They describe retained Ruby fact/source messages with independent
native consumers. Python helper responses are tested by their managed consumers;
no unused schema export is retained. Managed tests require regeneration equivalence; native Ruby
tests consume the static descriptions without building the application. These
are shape descriptions, not a schema registry, runtime admission policy,
upstream PNPM/MSBuild format mirror or compatibility lifecycle. Semantic native
identity, path, version, producer and command checks remain required. Existing
native command-argument interfaces need no invented JSON envelope.

### Runtime and CLI Consumers

The [application README](../README.md) owns current commands and supported
options. The internal CLI has no historical Python command or record aliases.

| Consumer                     | Native input/output boundary                                                                                                                       |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Root CI planning             | Exact event candidate/comparison, native paired facts, quality declarations and selected required work                                             |
| Ecosystem CI group wrappers  | Prepared current control, complete selected checks/variants and candidate-bound command/results                                                    |
| Ordinary Release preparation | Selected revision, one release unit/channel/destination/mode, complete original package set and clean qualification                                |
| Observation and publisher    | Bound preparation/original files, native destination state, upload-once command facts and required verification                                    |
| Separate verification jobs   | No-write registry readback of exact coordinates and original bytes and business completion, retaining command abnormalities and unresolved effects |
| Historical auditors          | Original producer-qualified Git source and retained evidence; no current campaign/replay entry point                                               |

Native PNPM, MSBuild/NuGet, UV/Python and RubyGems/Bundler own ecosystem semantics.
The application does not retain the old public Python control package, exclusive
provider/authority/consumer projects, dependency scanner, runtime admission files
or closed campaign operators as compatibility services.

### Workflow Caller Boundaries

| Caller                                                     | Required atomic boundary                                                                                                                                                                            |
| ---------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `.github/workflows/ci.yml` and the CI scope caller         | Actual native ownership, control preparation, group selection, complete results and retained originals                                                                                              |
| Native Node, .NET, Python and Ruby group wrappers          | Paired endpoint facts, selected work, effective quality, original tool outputs and terminal process facts                                                                                           |
| Windows NuGet preparation job and reproducibility workflow | Two preparation rows in CI and three sensitivity/recovery tests on dispatch or `main` changes, executed and passed, with original packages, separate PDBs, native diagnostics and failure retention |
| Ordinary npm, NuGet, Python and Ruby release workflows     | Selected control/product revision, prepared artifact bindings, native authorization, upload-once behavior and separate no-write verification/results                                                |
| Remaining platform-specific CI and independent projects    | Preserve their own ownership, runners, preparations and required checks                                                                                                                             |

The Windows NuGet jobs' success cannot substitute for the complete ordinary CI
gate. General C# checks use Windows; Python and Ruby callers use their supported
Linux runner. Fixture and local successes do not establish live destination,
authentication or publisher configuration. Closed bootstrap/acceptance workflows
and exclusive setup/operation-record actions have no current execution consumer.
Their actual historical producers remain recoverable through pinned readers.

### Root Tooling, Declarations and Record Readers

| Surface                                                                        | Retained responsibility                                                                                                |
| ------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| Product release/quality declarations, native manifests, versions and locks     | Explicit native ownership, complete output/check scope and genuine shared inputs                                       |
| `mise.toml`, `hk.pkl`, runtime projections and dependency-update configuration | Current tool preparation and conformance; no old control-package generators, scanner tasks or parser-only dependencies |
| Root workspace/build manifests and locks                                       | Actual current projects and native fixture inheritance; native tools update their own locks                            |
| Root selectors, attributes, ignore rules and CODEOWNERS                        | Current source/configuration paths and generated/output ownership                                                      |
| Agent instructions, portals, family bindings and project interfaces            | One current application authority; fixture carrier paths remain distinct from owning namespace                         |

Retire an exclusive dependency or helper only with its last current caller.
Native version/build inputs and the public NBGV component remain when their real
consumers need them. Historical evidence uses original source identities rather
than a transplanted admission, approval-proof or replay implementation.

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
PNPM [directory adaptation candidate](research/pnpm-native-planning.md#directory-coordinate-adaptation-candidate)
tests joining resolved coordinates to discovered roots and native publish
directories without a second dependency declaration. Its evidence limits remain
separate from acceptance of an implemented reader.

For NBGV, use the pinned public managed API for committed effective options and
native filter matching. Workflow assigns input consumers from those answers and
the native ancestor configuration read boundary; it does not reconstruct
inheritance or historical version-height evaluation. Preserve native case
semantics, missing/added/deleted inputs and both revision identities. The
[source findings and local qualification](research/nbgv-native-inputs.md)
identify the compile-time official-package integration and its observed Linux
delivery boundary. The implemented `NbgvInputReader` supplies those committed
native inputs for consumer mapping in paired CI fact assembly;
unresolved necessary inputs still fail planning. The component research retains
its original bounded scope; ordinary caller qualification is separate evidence.

Use ordinary typed values inside one invocation. Do not require a Provider Request
Manifest, Fact Bundle, admission token or immutable Repository Model aggregate.
When native evaluation requires another process or runner, transfer the relevant
facts and errors under the [transfer contract](#data-crossing-boundaries).
Project evaluation runs without publication capability.

CI must obtain enough ownership and reverse-consumer facts to justify both
selection and exclusion. An incomplete graph cannot certify that a changed path
has no consumers. Release resolves its selected unit's complete input/build/check
closure; it need not evaluate unrelated projects.

### Native Fact Integration

For each new integration, first identify its maintained native library or
supported tool interface, the inputs/configuration it evaluates, the facts and
errors it exposes, and the required facts still unavailable. Review that concrete
boundary before implementation. Follow the HLD's
[native integration boundary](high-level-design.md#native-integrations), including
native-language helpers and owner disposition when a necessary capability is
missing. Internal implementation and transfer formats have no compatibility
obligation. Rewrite producers and consumers together instead of adding aliases
or porting ecosystem parsing and resolution rules into C#.

| Ecosystem | Native inputs and required initial coverage                                                                                                                    | Unsupported relevant shape                                                                                                    |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Node      | PNPM workspace membership and local dependency edges; package manifests/scripts; shared lock/toolchain consumers; NBGV project inputs; declared package output | Unknown workspace protocol or dynamic dependency/input relation that native metadata and explicit declarations cannot resolve |
| .NET      | Evaluated MSBuild projects/imports and `ProjectReference` per selected configuration/TFM/RID; CPM/lock inputs; declared package outputs and supporting tests   | Missing evaluation runner, unresolved conditional/import inputs or an unmodeled relevant dimension                            |
| Python    | UV workspace/project and local dependency facts; `pyproject.toml`, native lock/build-system inputs; wheel/sdist outputs and Git-free sdist build requirements  | Undeclared dynamic backend/source inputs or native metadata insufficient for affected closure                                 |
| Ruby      | Evaluated gemspec/native dependency facts, locked build/check inputs and explicit repository-local relations unavailable from native metadata                  | Dynamic gemspec behavior whose input/ownership closure is not bounded                                                         |

Analyze base and candidate as specified by the [selection algorithm](#selection-algorithm). Native evaluation is product
execution and runs unprivileged. Initial graph scenarios must include real local
references and transitive consumers, not just the current reference-free smoke
projects. The fixtures do not define the limit of the repository model. Shared
lockfile changes may select all actual consumers; unknown scope never selects a
full-run fallback. New production-product onboarding needs its own accepted Wave entry.

Use Ubuntu runners for the Node/Python/Ruby groups and Windows for general
.NET evaluation/build/consumption, with explicit product variants taking precedence.
Tool versions come from `global.json`, `mise.toml`/`mise.lock` and native locks;
do not introduce a second toolchain authority.

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
   paired direct-selection reasons from the
   [Python quality-owner contract](integration/python.md#python-quality-owners-and-input-only-members). Follow required
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

Native tool setup and queries consume each endpoint's committed locks without
refreshing tracked inputs. Select a tool version compatible with both basis and
candidate before loading its environment, and use native locked mode for setup
and inherited queries. A changed tracked lock is a planning failure, not a
cleanup condition.

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

Finite `CI/Callers` composition assembles the existing .NET, Node, Python and Ruby
requests and collects their original results, including retained-job scope and
record-catalog responsibility. Actions owns SDK and locked tool setup, the
initial native control build and immutable artifact transport. Control-resource
prepare/query remain separate around endpoint SDK setup; the query retains its
default Debug context. The Linux-only Python and Ruby managed preparation scenarios
run directly through .NET, independently of pytest. Their selected source/build/
version/quality triggers, original reports and packages remain required. The
test runner's filter, per-module `--minimum-expected-tests` (which excludes
skipped rows) and exit code qualify them; missing, skipped or failed rows fail.

## Builds, Packages and Consumers

Build and qualification operations receive the selected revision, native entry
point, dimensions, expected native version projections and relevant locked inputs.
Planning may calculate NBGV; builds may calculate it again under the same relevant
Git/ref/history, configuration, dependency and supported toolchain inputs. CI
package execution owns its process environment and does not inherit the runner's
ambient ref context, which would otherwise change the NBGV projection. Required
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

### Post-Publication Completion

Ordinary Release requires local package qualification, including clean local
consumption of the qualified originals, plus authoritative remote verification.
For every destination, post-publication completion is the registry or platform
API observation of each exact intended coordinate, its required metadata and
visibility, and the actual served original downloaded and compared by size and
SHA-256 with the qualified original. Metadata or listing digests alone are not
sufficient. No remote native install, restore or import runs after publication.

Byte equality carries the pre-publication qualification to the published state.
It does not prove that a stock client's credentials, feed or index routing,
source mapping or dependency resolution select and install the published
package; Release does not claim those properties. The observation remains
bounded, and unknown or conflicting state blocks completion.

Historical campaigns retain all their original consumer requirements, evidence
and terminal outcomes. A later change of completion conditions cannot rewrite an
already terminal request.

## Release Operations and State

### Request and Preparation

A request contains one immutable source/control revision, Release Unit, channel,
destination and live/dry-run mode. Resolve the native version, complete output set
and applicable quality conditions before execution. Live Official checks
its eligible official ref under the
[Official channel scope](integration/common.md#official-channel-scope); Buddy
permits configured development refs. Dry run
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

| Operation                  | Input                                                           | Output and responsibility                                                                                              |
| -------------------------- | --------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Resolve coordinate         | Selected destination and native package identity                | Physical registry, normalized package/version, expected file identities and supported mode                             |
| Observe                    | Coordinate and qualified expected files                         | Per-file absent/exact/conflicting/unknown state, required native metadata, visibility and bounded diagnostics          |
| Determine supported writes | Current observation and expected set                            | Whether native creation or exact-subset completion is supported, and the ordered missing files                         |
| Invoke native upload once  | One still-needed qualified original and scoped write capability | Native command outcome and available effect facts, including built-in bounded transport retries; no application resend |
| Verify                     | Expected complete state and applicable completion conditions    | Bounded authoritative observations and required checks, with remaining uncertainty                                     |

“Once” counts application invocations, not HTTP attempts inside a supported native
publisher. Delegate its built-in bounded transport retries rather than adding a
custom retry handler or reconstructing the upload protocol. The destination must
still supply the required active-coordinate non-overwrite semantics. A native
retry may resend after an internally ambiguous response; the application must
not claim transport-level exactly-once delivery. When the invocation returns
failure or ambiguity, or is interrupted, stop subsequent application writes and
use bounded reads. This does not remove fresh manual same-revision recovery.
For NuGet, the [native publisher source basis](research/nuget-native-publisher.md)
records the accepted integration boundary and the selected client's behavior.

Release owns permission to proceed and business completion. The adapter owns native
identity, endpoint, response and partial-set semantics. Do not encode npm tags as
universal identity or Python's two-file behavior as a universal transaction. A
supported interface does not require each method to have its own class or record.

Each concrete integration specifies native non-overwrite reliance, required
files, metadata/readback, credential scope, redirect handling, visibility/read
bounds and partial-set behavior. Rely on the native client's redirect contract;
the application does not reimplement redirect following or storage-host
admission. Use operation deadlines and native capability lifetimes; do not
reinstate a dynamic Governance expiry. Different destinations may need different
read budgets. Necessary guarantees cannot be supplied by application locks or
extra probe records.

### Decision Table

| Observed condition                                                    | Write behavior                                                        | Business result                                                     |
| --------------------------------------------------------------------- | --------------------------------------------------------------------- | ------------------------------------------------------------------- |
| Complete exact state and all required checks                          | No write or write approval                                            | Satisfied by observed state                                         |
| Absent required state with supported creation                         | Native gate, fresh observation, then only still-needed writes         | Pending until complete required verification                        |
| Verified exact subset supported by the destination                    | Fresh request qualifies the complete set and plans only missing files | Pending until the full set is verified                              |
| Conflicting bytes or unknown required state                           | No mutation                                                           | Unsatisfied or unknown, with the blocking reason                    |
| Acknowledged upload and complete required verification                | No additional write                                                   | Satisfied; preserve the acknowledged command result                 |
| Acknowledged upload with missing required verification                | No additional write                                                   | Unsatisfied or unknown; retain the successful command result        |
| Failed/ambiguous native invocation and complete required verification | Stop application mutation; bounded reads only                         | Satisfied by observed state; preserve the command failure/ambiguity |
| Failed/ambiguous native invocation with incomplete/uncertain state    | Stop application mutation; bounded reads only                         | Unsatisfied or unknown; fresh recovery may be possible              |
| Interrupted publisher or missing result                               | No inferred retry                                                     | Effects may remain unknown unless native facts establish non-start  |

For Python, a failed or ambiguous wheel invocation stops the write sequence even if a later
read confirms the wheel. It does not authorize uploading sdist in that same
sequence. If the complete intended set is independently verified, the active
request can be satisfied; otherwise a fresh request can consider supported
missing-file completion. Successful acknowledged uploads can continue in their
declared order only after the required intermediate verification.

An initially exact request and a newly published request use the same completion
predicate. There is no separate exact-satisfaction certificate. Required metadata
and downloaded original file bytes must be established; a version label or
successful upload alone is insufficient.

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
not delete, yank, restore, retarget, change access or compensate across registries. No
historical run discovery, permanent ledger or general repair engine is required.

### Registry Observation and Retention

The integrations make no stock-client routing or resolution claim, including
scoped-registry routing. The destination tables in the integration contracts
distinguish the current mechanisms and required properties; they do not assert new platform guarantees or themselves enable any remote operation.

Use the existing bounded native read mechanisms as extraction inputs, not the old
Governance expiry/profile digests. Each integration closes endpoint, redirect and
credential handling, metadata/response-body and archive-expansion bounds, read
deadline/poll bound and cancellation before it is callable; registries own
package size. No missing lower-layer guarantee
may be replaced by an application reservation or probe ledger. A failed upload
still permits bounded read-only evaluation; it never permits application resend or
the next file write after the native invocation reports failure or ambiguity.
Built-in bounded native retries remain within that one invocation, under the
[destination contract](#destination-interface).
The [NuGet source basis](research/nuget-native-publisher.md) closes the selected
publisher retry boundary without supplying new runtime evidence.

The npm, NuGet and Python registry observations share one standard HTTP client
with no cookies, proxies, default credentials or automatic decompression. It
follows at most five redirects under the platform's
[documented redirect contract](https://learn.microsoft.com/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-10.0#remarks)
([runtime source](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/RedirectHandler.cs)),
which clears the \`Authorization\` header on every automatic redirect, including
a same-host redirect, and does not follow an HTTPS-to-HTTP redirect. The
adapter validates the HTTPS origin of each URL it requests and attaches
credentials only to the registry or API origin it selects. It does not
validate redirect targets or implement its own redirect hop. A redirect target
that requires the dropped credential, such as an authenticated path on the
same registry host, therefore fails closed as an unknown read. An unfollowed
redirect, including one beyond the limit, is a non-success response and cannot
establish exact or absent state. Exact and conflicting original state rests on
exact coordinate metadata and original size/SHA-256 equality, not on the
serving host. Absence and metadata rely on the selected HTTPS origin and the
redirects it issues. Each body-inclusive read deadline covers its redirects.
Metadata bodies keep fixed limits because they are parsed in memory. Served
originals have no application package-size limit; registries own package size.
An original is compared by a streaming SHA-256, and its read stops one byte past
the qualified original's size, because a longer body cannot match.

Ordinary workflows request 45-day retention for required prepared CLI,
qualified packages and request/results, matching the existing workflow setting.
This is a workflow default, not a new policy expiry or platform-retention proof.
If required inputs expire or the effective platform limit cannot cover the chosen
operation, that operation cannot continue. Optional diagnostics may expire sooner.
A fresh original-revision recovery rebuilds; it does not need a permanent ledger.
Keep registry/package serialization over required verification jobs, with native
concurrency and without automatic live cancellation.

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
credential, product-execution and transfer boundaries. Verification readback runs
outside the privileged publisher. Exact-state and dry-run paths do not enter a
write gate; dry run reports hypothetical operations and actual validation limits.

Serialize this repository's publication operation by physical registry and normalized
package, including required read-before-write and verification. If that operation
spans publisher and unprivileged verification jobs, use a containing native workflow
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
| Prepared CLI                             | Selected control revision and immutable output with its same-run artifact ID and native integrity                                                   | Publisher executes the intended control artifact without rebuilding it |
| Publication and verification result      | Attempted operations, raw responses/outcomes, required observations/checks and unresolved effects                                                   | Completion is supported; missing data does not prove non-start         |
| Operator                                 | Scope, outcome, diagnostic reasons and supported next action                                                                                        | Preserve terminal outcomes; later requests cannot rewrite them         |

These are consumer-specific typed contracts, not a universal envelope or one file
per row, recursive digest chains, approval certificates, a durable attempt
aggregate or a record engine. Concrete JSON fields and command signatures follow
actual implementation consumers. Internal formats may break; callers change or
retire together without mandatory historical readers in the new core.
Independently consumed helper messages may have static schemas generated from
their actual serializer types; keep producer/consumer shape tests separate from
native semantic tests. Do not mirror upstream native formats or introduce a
runtime schema registry. Keep data in memory when no real boundary requires
transfer. Executors need their work, not a serialized copy of the entire
repository graph.

Use same-run native immutable artifact IDs and native download integrity; reject
missing, substituted or mismatched inputs. Display names and latest-artifact lookup
are not identity. Trust successful native upload; the actual receiving consumer
validates its download. Do not upload/download/re-admit every producer intermediate
just to prove the platform accepted it. Package byte identity remains necessary
for qualification and remote comparison even when native transport owns integrity.

Retain qualified files and required request/results for approval waits and the
supported operation window within platform limits. Concrete durations are workflow
settings, reviewed with their caller. Expired required inputs block that operation.
Fresh same-revision recovery may use current native state under its destination
contract without requiring a permanent application record store. Optional
telemetry failure does not undo an established result; missing required
completion evidence leaves uncertainty. No permanent ledger or mandatory
per-product GitHub Release is added.

## Validation and Integration

### Behavioral Validation Basis

| Contract   | Required cases                                                                                                                               | Primary validation boundary                                       |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| Impact     | Unrelated source; transitive consumers; shared input; deletion/rename; changed dependency edge; unknown scope                                | Selection scenarios and native fact integration                   |
| Quality    | Nearest matching ecosystem; explicit stronger-preset adoption; required missing/advisory failed results                                      | Policy resolution and CI scenarios                                |
| Execution  | Complete selected variants/checks; shared prerequisites; runner mismatch; failed/missing execution                                           | Work planning and real workflow transfers                         |
| Package    | Required native version agreement; complete outputs; clean consumers; same-input reproducibility                                             | Ecosystem integration and package scenarios                       |
| Isolation  | Prepared selected control; no product execution with write capability; exact qualified inputs                                                | Actual job/process and artifact boundaries                        |
| Completion | Initially exact; absent; supported subset; conflict; delayed visibility; upload error with complete/incomplete readback                      | Destination contract and Release state scenarios                  |
| Recovery   | Interruption; unknown effects; fresh original-revision rebuild; only supported missing-file writes                                           | End-to-end use-case scenarios with bounded dependency substitutes |
| Channel    | Buddy development branch ref; Official ref outside scope rejected at preparation and publisher; dry run from a development ref with no write | Release state and preparation/publisher scenarios                 |
| Retention  | Missing/expired required transfer; optional telemetry failure; historical outcomes remain unchanged                                          | Transfer/result scenarios and evidence review                     |

Use focused unit tests for selection and outcome algorithms, scenario tests for
journeys and contract tests for actual transfers. Exercise native integrations
where their behavior matters. Do not preserve tests solely enforcing retired
record chains or duplicating platform authorization. Measure ordinary-PR latency
separately from legitimately broad changes; fast full execution is not successful
impact selection.

Local/controlled results do not establish live platform acceptance. The owner's
[Windows assumption](research/contraction-feasibility.md#decision-and-evidence-scope)
remains accepted without a separate experiment gate. Completed campaigns remain
closed; no implementation validation can silently consume a spent external grant.

### Release Integration Review

The [Concrete NuGet Buddy](integration/dotnet.md#concrete-nuget-buddy-integration),
[Python Release](integration/python.md#concrete-python-release-integration) and
[Ruby Release](integration/ruby.md#concrete-ruby-release-integration) integrations share one review
core: review affected changes with independent engineering/OCR and
record/research review before implementation, run complete affected suites, HK
and record checks, and qualify the final caller subject in ordinary CI before
delivery. These checks do not prove a new publication. Each section states its
own controlled validation cases, runner, additional checks and non-claims.

### Integration Mapping

The per-ecosystem integration contracts for [.NET and NuGet](integration/dotnet.md),
[Node and npm](integration/node.md), [Python](integration/python.md) and
[Ruby](integration/ruby.md), with the [common integration tables](integration/common.md),
own the concrete project and CLI/workflow caller inventory, integration payloads
and destination conditions. These contracts govern current integrations and
changes to them. Keep that mapping with actual caller changes; do not
maintain a second inventory here. Implementers and reviewers use them to preserve
complete journeys across actual process, permission and artifact transfers; they
are not an execution or publication grant. The
[Wave](../../../../../docs/delivery-wave.md) owns work authorization;
[agent handoff](agent-handoff.md) retains operation and evidence limits.

The former caller inventory and migration order remain at their
[immutable source][former-plan]. Historical producer identities and their exact
implementation, fixture and outcome readers are routed by
[historical evidence readers](validation/historical-readers.md) through the
[project portal](README.md#historical-source-rule). Current source
acceptance does not requalify those originals or reopen their closed campaigns.

The final private CLI/test projects own native facts, meaningful impact selection,
complete selected CI checks and common Release behavior. Node, .NET, Python and
Ruby groups use their native contracts and original candidate-bound plans.
Ordinary destination callers retain their actual permission, transfer, readback
and result boundaries. The fixtures, native helpers, root tooling and project
records use their current private locations. Independently useful public
components retain their own consumers.

Closed campaign callers, the public Python control package, obsolete admission
and scanner groups, and old-basis compatibility mappings are retired. Historical
evidence readers use actual pinned producer source rather than a runtime fallback.
Later changes reconcile actual callers, selectors, workspace/version inputs,
record routing and complete affected validation together. Their source acceptance
does not qualify new live destination behavior or reopen a stopped campaign;
work and operation authorization remain with the accepted Wave and domain gates.

[former-plan]: https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md
