# NBGV Native Inputs

## Question and Evidence Scope

Can repository analysis consume native NBGV configuration and path matching
without another version-file parser, inheritance evaluator or filter language?
The [middle-level contract](../middle-level-design.md#minimum-facts) consumes
these source findings. The [original application qualification][qualification]
separately records bounded Linux compilation/runtime dependency delivery and
PNPM reader observations, not complete CI or Windows qualification.

The source pin is NBGV 3.10.94, commit
`dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb`, retrieved on 2026-10-03/04.
At that assessment, the repository's package and CLI pins were both 3.10.94.
The delivery PR retains retrieved source bytes, the upstream MIT notice,
package inventories and hashes.

## Public Source Findings

| Finding                                                                                                                                                                | Immutable supporting source                                                                 |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| CLI JSON serializes `VersionOracle`, including effective options, but does not expose complete version-file read lineage                                               | [`OnGetVersionCommand`][cli], lines 597–657; [`VersionOracle`][oracle], lines 44–82         |
| Public read-only contexts accept a committed subject; public `VersionFile.GetVersion` selects committed evaluation when a commit is selected                           | [`GitContext.Create`][context], lines 197–232; [`GetVersion`][version-file], lines 118–128  |
| The managed engine probes `version.txt` and `version.json` from the root toward the project; parsing and inheritance remain native                                     | [`ManagedVersionFile`][managed-version], lines 50–183                                       |
| `VersionFileLocations` contains two aggregate directory facts, not a complete ordered read lineage or a universal nearest-file answer                                  | [`VersionFileLocations`][locations]                                                         |
| Public `FilterPath` exposes resolved paths and inclusion, exclusion and descendant matching; managed repository case behavior is available                             | [`FilterPath`][filter], lines 34–189; [`GitRepository`][repository], lines 100–111, 865–900 |
| Include/exclude composition is documented; complete version-height commit relevance and history traversal remain private                                               | [Path-filter documentation][filter-doc]; [`GitExtensions`][height], lines 187–274           |
| The standard package ships the core and managed dependencies as build assets and defines `NerdbankGitVersioningTasksPath`; the core project is not separately packable | [Package definition][package]; [Common targets][targets]; [Core project][core]              |

The standard archive contains
`build/MSBuildCore/Nerdbank.GitVersioning.dll` and its managed dependencies.
Inventory alone does not establish runtime delivery by assembly resolution and
publish. No public single-call complete input list was found in the inspected
interfaces; this is not an exhaustive claim about maintained extensions.
The historical assessment retains the unselected Cake packaging alternative.

## Native MSBuild Caching

The same pinned 3.10.94 source defines `NBGV_CacheMode=MSBuildTargetCaching`
as the default. It reuses version target results inside an MSBuild build session;
`None` computes separately in each project. The [native props][cache-props]
state that caching mode changes performance and compatible systems, not computed
versions. This is not a persistent cache across jobs or checkouts, nor a promise
of one version calculation for different monorepo version inputs.

The [native caching targets][cache-targets] declare a graph-only
`ProjectReference` marked `NBGV_InnerProject=true`. Its package-owned
`PrivateP2PCaching.proj` executes version targets with input-keyed global
properties, including local `GitVersionBaseDirectory`. It removes TFM/RID
properties that do not affect version calculation and uses internal
`Configuration=Release`. These are tool execution facts, not a product build
configuration or portable business-check identity. The [helper][cache-helper]
imports the versioning targets and optionally `NBGV_PrivateP2PAuxTargets`;
ignoring its actual inputs would lose that consumer relationship.

The owner selected a finite helper adapter with native caching retained.
The .NET business projection identifies the marked native dependency through
the consuming project's validated restored package pin, retains necessary helper input
responsibilities, and leaves helper execution to MSBuild/NBGV. Product globals
remain complete and unsupported endpoint-dependent product globals still fail.
These pinned source findings establish the adaptation basis; the delivery PR
retains local integration and changed-source hosted qualification separately.

## Native API Integration and Limits

Use the pinned official public API through a compile-time assembly reference.
The imported task-path property is defined in targets, so a scoped target must
add the reference after that definition and before assembly resolution. Ordinary
MSBuild resolves/copies dependencies. No dynamic loading, installation probing,
private reflection, vendoring or additional runtime/helper is proposed.

Bind and dispose a native managed read-only context at the manifest directory
and explicit committed subject. Require a real repository, selected commit and
effective options; no working-copy, missing-repository or default substitution.
Native disposal does not establish immediate release of every object stream.
The pinned [legacy `version.txt` branch][managed-version] constructs an unscoped
`StreamReader`, while JSON reads use `using`; [context disposal][managed-context]
closes the repository's packs, not that returned loose-object stream. Such handles
may remain until managed finalization. Keep the source object store alive through
the reading process. A future in-process materialization teardown needs its own
validated lifetime boundary; test-fixture finalization is not a production cleanup
protocol. These are source findings and integration limits, not broader runtime
qualification.

Explicit commit selection binds committed options and filters. It does not bind
complete `VersionOracle` projection: the pinned [oracle][projection-context]
uses the cloud-build ref or current HEAD ref and native HEAD tags;
[the managed context][managed-context] does not retarget those facts to a
historical selected commit. When that subject is HEAD, the oracle also reads
working-copy options.

Invoke canonical version/package projection on clean exact-target materialization,
with HEAD equal to the selected commit, the intended ref or controlled native
cloud-build context, tags associated with that target, and required history and
version inputs. A cloud branch override alone cannot correct another HEAD's tag
fallback. CLI/API agreement includes these ref/tag/worktree inputs as well as the
pin, subject, engine and configuration. NBGV owns public-release evaluation and
package suffixes; the application supplies their intended inputs.

Known ancestor `version.txt`/`version.json` search candidates can describe the
pinned engine's configuration read-input boundary. This is deliberately distinct
from the files contributing to final values: an overridden ancestor may still
be read. Match identities against committed inventories using native case
behavior and retain missing, added and deleted candidates across both revisions.
Do not reconstruct inheritance or mistake aggregate locations for a read trace.

Consume effective native `FilterPath` objects and their matching methods. The
application can combine already-native Boolean answers under the documented set
contract to assign Workflow input consumers. Include/exclude defaults must remain
pin-specific. Leaf matching does not implement historical commit relevance,
version-height evaluation or artifact equality. Other project, build, dependency,
toolchain and quality inputs remain necessary for CI coverage.

A concrete gap stops dependent work for owner disposition; it does not authorize
a replacement evaluator or a full-run fallback.

## Historical Application Evidence

The [closed protocol][qualification] and [original observation][observation]
retain exact source/tool/fixture identities, original commands and hashes,
packaging corrections, the committed versus externally compiled assertion
runners, independent reviews and spent limits. The source snapshot preserves
that text; it is not the original producer. Local-only restore/build/publish
and relocated native projection succeeded on the recorded Linux subject.
Initial restore/build failures remain failures; the historical assertion runner
exited 1 after 23 assertions, and its separate read-only verifier does not
relabel it as a passing process.

The separate [PNPM reader result][pnpm-observation] retains both reader calls,
native script metadata, original fixtures and query accounting.
These component observations do not establish current complete fact assembly,
caller integration, Windows or publication. Their finite protocols grant no
repetition, cleanup, refilled allowance or new operation.

## Pin Guard

NBGV pin changes are guarded by tests in the default WorkflowDelivery test run,
which CI selects when `.config/dotnet-tools.json`, `Directory.Build.props` or
`global.json` changes:

- `NbgvInputReaderTests` and the `DotNetGraphTaskTests` NBGV helper rows run the
  pinned library and its packaged MSBuild assets.
- `RepositoryPinContractsTests.NbgvToolPinEqualsTheNbgvPackagePin` requires the
  CLI and package pins to be equal.
- `NbgvCliContractTests` restores the manifest-pinned CLI and runs
  `get-version --format json` against the library's `VersionOracle` on the same
  commit. Its rows cover a local main branch, another branch, a detached tag and
  detached GitHub Actions branch, tag and non-release refs. They require equal
  `SemVer2`, `PublicRelease`, `NuGetPackageVersion` and `NpmPackageVersion`,
  and the exact `GitCommitId`.

These tests do not cover cloud-build providers other than GitHub Actions, the
`PublicRelease` and `IGNORE_GITHUB_REF` overrides, or the git-engine switches
`NBGV_GitEngine`, `DEPENDABOT` and a bot `GITHUB_ACTOR`. The rows clear all of
them, and the Ruby callers launch the CLI with a constructed, non-inherited
environment. Shallow clones (preparation rejects them) and Windows paths beyond
the Windows CI run are not covered either. PNPM reader queries are guarded by
the PNPM [Pin Guard](pnpm-native-planning.md#pin-guard). Existing observations
keep their original pins and limits.

[qualification]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/nbgv-native-inputs.md#bounded-local-application-qualification-protocol
[observation]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/nbgv-native-inputs.md#local-application-observation
[pnpm-observation]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#actual-reader-and-script-metadata-observation
[cli]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/nbgv/Program.cs#L597-L657
[oracle]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs#L44-L82
[context]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/GitContext.cs#L197-L232
[version-file]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionFile.cs#L118-L128
[managed-version]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Managed/ManagedVersionFile.cs#L50-L183
[locations]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionFileLocations.cs
[filter]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/FilterPath.cs#L34-L189
[repository]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/ManagedGit/GitRepository.cs#L100-L111
[filter-doc]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/docfx/docs/path-filters.md
[height]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Managed/GitExtensions.cs#L187-L274
[package]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/Nerdbank.GitVersioning.nuspec
[targets]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/build/Nerdbank.GitVersioning.Common.targets
[core]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Nerdbank.GitVersioning.csproj
[projection-context]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs#L44-L117
[managed-context]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Managed/ManagedGitContext.cs#L50-L67
[cache-props]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/build/Nerdbank.GitVersioning.props
[cache-targets]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/build/MSBuildTargetCaching.targets
[cache-helper]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/build/PrivateP2PCaching.proj
