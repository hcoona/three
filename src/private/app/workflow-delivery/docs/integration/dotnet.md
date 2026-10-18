# Workflow Delivery .NET and NuGet Integration Contract

This integration contract owns the .NET native facts, CI group, NuGet Buddy
Release caller and NuGet destination conditions. It refines the
[MLD](../middle-level-design.md), whose [integration
mapping](../middle-level-design.md#integration-mapping) states the shared scope
and authority of the integration contracts.

## Native .NET Project Facts

MSBuild owns .NET discovery, SDK resolution, evaluation, graph identity and
reference propagation. The existing application supplies a task loaded by the
endpoint's selected `dotnet msbuild` process; Workflow does not host a second
resolver, parse project XML or schedule compilation nodes. The
[native .NET CI group](#native-net-ci-group) identifies
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
unsupported. The [application entry](../../README.md)
retains the source basis and implementation boundary. Control inputs use the same native graph/provider contract, retaining
explicit references, evaluated inputs and concrete configuration consumers.
No separate build-task log interpreter is required.

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

The
.NET adapter populates [`ProjectFacts.OwnedPaths`](../middle-level-design.md#minimum-facts).
It derives ordinary regions from native project directories, preserving
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

## Control-Test Resource Consumer

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
Each endpoint uses its own committed inputs, SDK and existing root locked native
preparation. Do not restore the test project again. The full root graph uses
additional CI/locked-restore globals; retain this query's default Debug context
rather than claiming equivalent conditional resources.

The response that the C# scope selector (`ci caller-dotnet select`) consumes in
process carries the exact
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

## Native .NET CI Group

This group owns general .NET CI selection and execution using the Windows Debug
operation. It does not cover Release or change test frameworks. Root CI
consumes the implemented group under ordinary changed-code Windows qualification
in [PR #1054](https://github.com/hcoona/three/pull/1054). The following contract governs
changes to this operation.

### Native Process and Endpoint Facts

Build the candidate private application completely before task use. Its native
locked restore, build and output-path query specify `dotnet` as the mise tool
argument, preserving configured SDK choices while restricting automatic missing-tool
installation to that control dependency. Other configured tools may remain in
the native environment; this is not isolation or a zero-network guarantee. Its
assembly supplies one public `ITask` entry loaded through `UsingTask` in an ordinary
endpoint-selected `dotnet msbuild` process. Use native `ProjectGraph` with explicit
entry/global properties. MSBuild owns SDK resolution, task/dependency loading,
evaluation and reference propagation. Do not introduce MSBuildLocator, a custom
ProjectCollection host/resolver, a helper application or an XML/item evaluator.
An unavailable native capability stops this operation.

Prepare each exact endpoint with its own `global.json` SDK and locked `dirs.proj`
restore using `Configuration=Debug`, `ContinuousIntegrationBuild=true` and
`RestoreLockedMode=true`. Candidate assets cannot substitute for basis facts;
there is no unlocked repair. Discover through unfiltered `dirs.proj`. Projection
task/import/output properties are wrapper inputs, not semantic graph globals.

The typed task request carries endpoint revision/root, traversal entry, operation
globals and private response destination. Its response carries native nodes,
edges, input-consumer rows, test capabilities and unavailable facts. Nodes retain
project path, complete globals, outer/inner distinction and native configuration,
TFM and RID. Node identity is project path plus sorted complete globals. Edges
refer to those identities, including native analyzer references. Input rows retain
path, role, observation stage and consuming nodes. Required unavailable facts
name the endpoint, project/node when known, role and reason; they are errors.

The CLI binds physical native paths to exact endpoint roots and committed Git
spelling. SDK/package/generated paths retain their environmental/producer roles.
The native workload autoimport locator associates a returned `Sdk` directory's
immediate parent pack only when that result actually imports its `AutoImport.props`.
Consume primary and additional paths without an installed-pack inventory or a
second workload resolver. Other SDK roots and unknown external hooks do not gain
that expansion; native provider identity/version do not claim an exact pack pin.
Control-build collection separately consumes the successful import's immediate
parent, unexpanded text and native evaluation identity.

The selected SDK's literal
autoimport seeds exact file membership for observed connected imports in that
same evaluation and pack; no directory inventory or reconstructed SDK result
supplies missing membership.
Reject unsupported semantic globals containing endpoint-dependent coordinates;
do not invent path rewriting or AssemblyName/relocation aliases. Aggregate native
relations into business project-path IDs for the shared impact planner, retaining
full native identity for execution. Both-endpoint relation union handles removed
projects/references and rename coordinates; selected surviving projects expand
all supported candidate inner variants.

Retain NBGV's default `MSBuildTargetCaching`. The native
`NBGV_InnerProject` reference and the consuming project's validated restored NBGV
package pin identify its `build/PrivateP2PCaching.proj` helper. Adapt this concrete
tool dependency separately from business projects: leave the native graph
unchanged, omit the helper's internal identity and edges from portable business
facts, and preserve its observed input responsibilities for each business
consumer. Arbitrary external projects and unsupported helper overrides gain no
exception. The receiving native operation reconstructs local helper paths.
The [pinned NBGV source findings](../research/nbgv-native-inputs.md#native-msbuild-caching)
explain this boundary; source support does not qualify hosted execution.

### Finite Input and Ownership Coverage

Evaluation supplies main projects/imports, native source/resource/AdditionalFiles/
Analyzer/XAML/manifest items and relevant icon/manifest properties, native graph
references and test capabilities, and Roslyn's existing/potential analyzer
configuration inputs. Locked restore supplies assets, CPM/lock and configuration
facts. Read `ProjectAssetsFile` through official NuGet `LockFileFormat` and consume
`PackageSpec.RestoreMetadata.ConfigFilePaths`. Require supported format, readable
populated native metadata, exact project identity and the current committed
`nuget.config` contributor. An empty model collection alone cannot establish
serialized presence. Do not parse configuration hierarchy or add a second JSON
reader. Native NBGV supplies actual configuration/version inputs; generated `obj`
outputs retain their native producers instead of becoming committed diff paths.

The root Traversal SDK project uses its own actual NuGet assets and generated
imports, with effective restored global-package pins checked against native assets.
Inactive declarations supply no provider; NuGet owns applicability.
Its package providers describe the restored closure without claiming a committed
traversal lock; the managed-project lock requirement is unchanged. Do not borrow
descendant assets or add a lock solely to fit a business-project validator.
Native version-input subjects bind effective `GitVersionBaseDirectory` to the
endpoint's committed directory and union business consumers across variants.

Traversal's unused self-version metadata does not add semantic product inputs.
Its genuine project/configuration/provider/helper inputs keep their native
reachable consumers. Business projects may still consume a root version subject.
These physical subjects are not transferred check globals. Quality selection remains at the
product directory. Unsupported external/repository-relative contexts fail.
Build and native inner `GetTargetPath` supply runnable output at their proper
stage; passive evaluation does not promise target-produced values.

The adapter populates the exact [`OwnedPaths`](#native-net-project-facts) from committed endpoint entries
in ordinary native project regions, narrowed by evaluated `DefaultItemExcludes`
and `DefaultItemExcludesInProjectFolder`. Match each native file spec through
selected-SDK `Microsoft.Build.Globbing.MSBuildGlob.Parse`/`IsMatch`, preserving
native scalar-list boundaries, escaping and path semantics. Illegal or ambiguous
necessary values fail instead of broadening ownership. Explicit input-consumer
facts independently add their consumers; union across native nodes rather than
intersecting variant excludes. Do not simulate SDK item rules.

The current test project's `fixtures/products/**` exclusion separates ordinary
Node fixture edits from its .NET parent, while its explicit embedded npm
`version.json` retains that exact .NET consumer. WebHdfs assigns `ConfigFilePath`
inside `PreBuild`; its in-project copy input is covered by ordinary committed
ownership, including additions/deletions. Keep native Copy unchanged, without a
passive-property promise or default-None assumption. External/shared producer
inputs need concrete native facts/adapters.

Native `ProjectInstance.Targets` and `ProjectTargetInstance.Location` identify
effective repository target definitions and their origins, not arbitrary task
I/O. The current finite producer scope includes Pack-only `SetPackageReleaseNotes`,
official tool references in `ReferenceOfficialNbgv`, WebHdfs `PreBuild`, the
traversal's non-Windows restore customization. Preserve their native
conditions/effects; ordinary Windows CI does not activate the non-Windows
restore operation.
New or unrecognized repository producers need a reviewed adapter/operation scope;
report unsupported origin/name. This is no SDK task allowlist, hash admission
policy, universal tracer or claim about every locked package's hidden I/O.

### Planning, Execution and Collection

Extend existing Windows `scope`; do not add a planning job for runner availability.
Reuse its prepared candidate application and native event/comparison handoff.
Obtain both endpoint facts before .NET applicability, and reuse the shared
comparison/responsibility binder and impact planner rather than duplicating them.
The old coarse .NET Boolean is not selected-plan authority.

Resolve adopted presets and explicit native-retained checks under the MLD's
[quality-origin contract](../middle-level-design.md#declarations-and-quality-autonomy).
Emit the original .NET
[`CiPlan`](../middle-level-design.md#implementation-type-names) as a distinct artifact for the existing
`dotnet-tests` execution/collection consumer, with native artifact ID and resolved
applicability. Failed/missing/cancelled planning cannot become a successful skip.
Successful empty work remains valid; required CI composition still accounts for
the planning owner and .NET execution/result check.

Submit selected native projects/variants together to the selected SDK's native
`ProjectGraph` and `BuildManager` graph-build interface, with the original
operation globals for each entry. The SDK-hosted task disables its nested
manager's in-process node and uses native node lifetimes and result codes.
MSBuild owns references, ordering, parallelism and its invocation-local target
cache. A thin task in the existing application projects native per-entry target
outputs; it does not resolve dependencies or implement a scheduler. Retain one
batch command observation and bind its outputs to the original selected entries.

Preserve successful independent builds when another entry fails. Missing native
outputs, interruption or failed extraction cannot establish a passed build.
Tests follow their own satisfied build obligations. Retain Build in each test
request where native build-time properties or environment are required; do
not substitute inferred paths or a test-binary launcher. Runnable tests enter
one SDK-hosted `BuildManager` session as separate native submissions with their
original globals and `Build`/`GetTargetPath`/test targets. MSBuild owns node
scheduling and concurrency across them.

Each completed submission records its own
overall/target result codes, outputs and submission-attributed diagnostics, bound
to the original selected check; one failed test cannot mask another. A wrapper
that failed for another reason cannot promote these results. After cancellation
or timeout, completed entries are retained and unfinished entries take that
termination; the one operation deadline covers the whole test session. Retain
one test batch command observation.

As with `dotnet test`, the session uses
native terminal-logger mode and VSTest MSBuild output (the SDK selects its
MSBuild-output VSTest task from that mode), so VSTest reports through its own
submission's diagnostics rather than raw console text; a plain console logger
writes the captured output. Evaluated
`IsTestingPlatformApplication` selects MTP; otherwise `IsTestProject` selects
VSTest using native inner `GetTargetPath` output. Do not infer framework paths or
capability from package names. Every selected supported variant contributes its
required checks, collected against the original plan and candidate. Exit zero
cannot satisfy absent, skipped or failed obligations.

The Workflow Delivery test project's native MTP configuration excludes the
`NuGetNativePreparation` and `NuGetNativeScenarios` categories by default.
The Windows `NuGet native preparation (Windows)` CI job owns the two
preparation rows (real preparation, original package and clean consumer). Its
applicability follows the existing .NET/Workflow scope selection, which covers
any Workflow Delivery source or test change, the fixture product and shared
.NET/CI inputs; Validate requires its result. Same-input .NET
reproducibility was established once ([Issue #948](https://github.com/hcoona/three/issues/948),
[PR #952](https://github.com/hcoona/three/pull/952)); by owner decision it is build-infrastructure behavior, not
per-PR evidence. The Windows reproducibility workflow runs the three
sensitivity/recovery tests (source and timestamp sensitivity, and one
fresh-recovery test over exact and conflicting state) on owner dispatch and on
`main` pushes changing the SDK, shared MSBuild/NuGet/NBGV/Git inputs, NuGet
Release/package adapter source or the fixture product.

Its run ID, source commit
and 30-day artifact are the evidence carrier; no additional record is needed.
The triggering PR's author, then the repository owner, owns a failed `main`
run. Skipped intermediate `main` commits are acceptable because the next run
covers the newer state; dispatch with `ref` can target a specific commit. Both
Windows jobs retain original archives, DLLs, separate native portable PDBs,
debug paths, TRX attachments and binlogs. `IncludeSymbols=false` does not
promise a PDB inside the package. A run qualifies only its own source.
Both override the default through MTP's ordinary command-line filter. Explicit
local filters also override the default; this is project-owned test
configuration, not a Workflow filter interpreter.
Native graph-test fixtures may batch restore their disposable projects through
an ordinary solution. Seed fixture locks and still validate the locked restore;
do not share mutable fixture state or introduce a Workflow restore cache.

The current native plan and executor own the complete group; retired commands
cannot satisfy its results. Affected changes qualify task/dependency
loading; graph identity/dimensions/references; finite committed/generated input
ownership; official configuration facts; native test capability/output; selected
execution; both-endpoint mapping; and changed-code Windows plan transfer/results.
Cover actual local/transitive/analyzer references, exclusions plus explicit
resources, additions/deletions/renames, changed variants/edges and unavailable
facts. Windows feasibility remains the owner's assumption, not another experiment.
No retired provider or caller is a runtime fallback.
No publication/proving dispatch, authentication, access or Environment change
follows from this group.

## Control-Test Input Routing

Root CI connects the
[control-test resource consumer](#control-test-resource-consumer)
to root CI. Run native evaluation and final selection in the existing `scope`
job on Windows, keeping its job name, outputs, downstream guards and final
`ci-scope` artifact. Keep `Build & Test (.NET 10)` as the sole general .NET test
executor. This co-location needs no extra resource producer job, and the
comparison/resource response does not cross a job boundary: only the concrete
receiving process in the scope job consumes it.

Prepare the selected candidate control application once using its pinned SDK and
locked native dependencies. Run that same-runner build output directly with its
selected .NET runtime, keeping required assemblies, dependency metadata and
runtime configuration together, without publishing or packaging it. The scope
job additionally uploads that build output, together with a Git archive of the
candidate Native/Python helper sources (`python-control.zip`), the Ruby facts
helper and the original .NET plan, as the `ci-dotnet-plan` transfer that the
later CI jobs consume. Resolve the native event comparison once with `ci
comparison`, then resolve its actual Git objects.

Confirm the named test
project's committed entry, materialize exact endpoints outside the candidate
source root, install each endpoint's selected `global.json` SDK, and perform
necessary locked restore of that test project and its native reference closure
under Debug before the single evaluation query. Candidate preparation never
substitutes for unavailable base inputs. Native preparation/evaluation is
unprivileged; the scope job runs no second test suite. Confirmed base absence
requires successful inventory inspection; the candidate owner must remain
present. Identical full-mode endpoints may reuse one evaluation.

The existing scope job explicitly prepares locked dotnet, Node and PNPM tools
before native environment export, with `MISE_EXEC_AUTO_INSTALL=0` scoped to that
job and inherited by its control commands and native children. Before each
endpoint's first dotnet operation, explicitly prepare that endpoint's own locked
dotnet, Node and PNPM declarations. Keep Actions-selected `global.json` SDKs and
native DOTNET_ROOT semantics; candidate tool preparation cannot stand in for a
different basis declaration. Missing tools or native verification still fail.
This bounds implicit installation, not tool visibility or all network effects.

The scope caller uses repository-relative POSIX Git coordinates and strict UTF-8
at the process boundary. It reads the required native response before emitting
final applicability and reasons. Preserve all
other source inventories/selectors and the existing successful-scope guards.
Missing response, malformed/conflicting identities or native errors fail the
scope job; they cannot appear as non-applicable downstream work.

Validate fixture-version-only and Debug-conditional resource changes selecting
the existing .NET owner; base/candidate removed, moved and added resources;
confirmed base absence versus native failure; default and unsupported dimensions;
missing/malformed response and candidate mismatch; unrelated Node nonselection;
and coherent explicit full. Reuse existing native item-semantics tests and run
existing selector scenarios, including Unicode and inherited/deleted/renamed
inputs. Ordinary changed-code hosted CI must exercise Windows preparation, final
selection and required-check behavior. A component or design result alone does
not establish routing or complete-group acceptance. Retained Python consumers,
all selected Node variants and explicit group execution ownership remain
separate qualification responsibilities of the complete group.

## Concrete NuGet Buddy Integration

This contract selects the existing declared
`Hcoona.ReleaseSmoke.GithubPackages` product, one `net10.0` `.nupkg`, no symbols
or runtime dependencies, and GitHub Packages NuGet
[Buddy](../high-level-design.md#domain-values) with development dry runs. It adds no product, destination or native-operation authorization.

Preparation reads the exact committed release declaration, entry and quality
selection through the existing Git materialization and declaration readers.
Resolve the supported `dotnet/nuget-package-v1` build and primary package output;
unknown quality recipes, variants or required tests remain unavailable. Use the
normal selected SDK, locked restore, `Release`/`net10.0`/no RID and
`ContinuousIntegrationBuild`. Native `GetBuildVersion`, Build and
`Pack,_GetOutputItemsFromPack` use those same dimensions; compare their native
package and assembly version projections. Native reproducibility configuration
remains authoritative. Inject neither frozen versions nor a package witness.

Share concrete archive inspection, original size/digest comparison and clean
local consumption in the [Ecosystems module](../middle-level-design.md#components-and-native-responsibilities); CI adapts its own plan/results to them.
Release does not consume `CiPlan` or `CheckSpec`. Complete preparation requires
native inputs/version, build, pack, contents, local consumer and original-byte
results, plus any actual applicable declared tests. Retain command termination
and missing/failed/cancelled results. Transfer native package properties needed
by the consumer, omitting checkout-specific physical paths.

Use the coherent official packaged NuGet 7.9.0 group: Commands, Protocol,
Configuration, ProjectModel, Packaging, Versioning and Frameworks. Replace their
SDK assembly references together; retain the selected SDK's Microsoft.Build
references separately. Resolve Newtonsoft.Json through the official package
graph rather than a second NBGV task-directory copy source; retain official NBGV.
The [publisher source basis](../research/nuget-native-publisher.md#client-distribution-selection)
explains this selection. Verify resolved package and output assembly identities,
NBGV binding, native restore and consumer execution before accepting the
implementation. Source inspection and installed package availability establish
neither runtime compatibility nor live publication. Keep this client untrimmed
and JIT; no AOT claim is made.

Observe the selected `https://nuget.pkg.github.com/hcoona/index.json` through the
shared registry HTTP client described in the
[MLD](../middle-level-design.md#registry-observation-and-retention). Official `ServiceIndexResourceV3` interprets the service
index; require unique HTTPS owner-origin PackageBaseAddress and PackagePublish
endpoints without user information, query or fragment. Native NuGet identity
comparison owns normalized package/version equality. Require the selected
GitHub package's NuGet type, normalized name, public visibility and
`hcoona/three` repository association, then complete native version metadata and
original archive comparison. API inventory is supporting metadata with bounded
pagination, not an independent admission authority.

Bound each observation to 90 seconds and 80 application reads, each
body-inclusive read to 30 seconds and metadata bodies to 2 MiB. The shared client follows the archive's storage redirect;
signed URLs are not retained in diagnostics. Compare original size and digest. Exact, absent, conflicting and unknown observations
use the same predicate before and after publication. Unknown control, identity
or original bytes block writes; absence permits creation only with established
native package control.

The publisher constructs supported `HttpSource` and `HttpHandlerResourceV3`
resources for the selected publish endpoint, using native `NullThrottle`. Invoke
official `PackageUpdateResource.PushAsync` once for one qualified regular file,
with no symbols, `allowSnupkg=false`, `skipDuplicate=false`,
`noServiceEndpoint=true`, a 120-second native timeout and insecure connections
disabled. Official NuGet owns multipart construction and its default bounded
retries. Use sanitized typed failures and no credential-bearing native logger.
The standard handler supplies Basic authentication only to the selected NuGet
origin; the API-key delegate supplies the same scoped native capability.
GitHub metadata API reads use Bearer authentication separately. Do not inherit
credential plugins or prompts, override the testing retry handler, introduce an
application resend loop or infer finer package isolation than the platform grants.

Link parent cancellation to standard handler sends and body consumption, and
await native invocation completion after cancellation. Stopping the await does
not stop writes. Preserve actual timeout/cancellation/failure and uncertainty;
process termination may interrupt the invocation, after which only bounded reads
may run. Refresh observation immediately before publication: exact removes the
write, absent permits it, conflict or unknown stops it. A handled invocation
failure still permits bounded readback but no further application write.

Required completion covers destination control, coordinate and the downloaded
original bytes, observed again in a separate no-write verification job; no
remote restore, build or invocation runs after publication. Common result
evaluation may report satisfaction after a handled upload error only with a
complete fresh observation, preserving the command abnormality. Missing or malformed subject, file,
digest or required result cannot satisfy the request.

The ordinary NuGet Buddy entry and reusable live caller use four concrete jobs:
prepare, observe, publish and verify/result. Use the existing Environment on the
actual publisher and native package concurrency through verification. Transfer
control, preparation and original artifacts by same-run immutable artifact ID
with the native download digest check; `job-inputs nuget receive <phase>` binds
the received preparation's source subject to the request. Keep 45-day retention.
Product code runs outside the publication context. Development Buddy refs and
dry runs are supported; dry runs enter no write gate and make no live claim.
Retained closed-campaign readers keep their actual historical consumers; there
is no compatibility alias for old runtime schemas.

Changes follow the [shared review core](../middle-level-design.md#release-integration-review) and preserve
this contract and its governing native-invocation boundary. Validate application
journeys, authentication separation, cancellation, transfer/result completeness
and official-client binding with controlled contract tests and real
credential-free local native package/consumer tests. Complete affected
managed/Python suites and ordinary Windows CI before caller delivery. These
checks do not prove new hosted Release/feed behavior or grant publication,
campaign replay, configuration changes or postmerge tests.

### NuGet Destination Completion

The MLD's [post-publication completion](../middle-level-design.md#post-publication-completion)
contract applies to these destinations; this table owns their concrete
conditions and observation.

| Existing destination/use    | Native completion conditions to preserve                                                  | Post-publication observation                                                                                                                                                                                                            |
| --------------------------- | ----------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| GitHub Packages NuGet Buddy | Normalized package/version, intended owner/feed/repository and visibility, exact `.nupkg` | [Package control](../high-level-design.md#domain-values), GitHub and flat-container (NuGet v3 package content resource) version sets containing the version, downloaded `.nupkg` size/SHA-256 equal to the original and native identity |
