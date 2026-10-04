# NBGV Native Inputs and Application Qualification

## Question and Evidence Scope

Can repository analysis consume native NBGV configuration and path matching
without another version-file parser, inheritance evaluator or filter language?
The [middle-level contract](../middle-level-design.md#minimum-facts) consumes this
research. Source inspection identifies the integration; the
[local application observation](#local-application-observation) qualifies
ordinary compilation and runtime dependency delivery on Linux. Complete CI
integration remains pending.

The source pin is NBGV 3.10.94, commit
`dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb`, retrieved on 2026-10-03/04.
The repository's package and CLI pins are both 3.10.94. The delivery PR retains
retrieved source bytes, the upstream MIT notice, package inventories and hashes.

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
| The maintained Cake wrapper defines ordinary `lib/net8.0` packaging, with additional wrapper dependencies                                                              | [Cake project][cake], lines 4–16, 55–80                                                     |

The exact standard NuGet archive contains
`build/MSBuildCore/Nerdbank.GitVersioning.dll` and its managed dependencies.
This package inventory does not establish that normal assembly resolution and
publish will deliver every runtime dependency to an application. Cake packaging
is a source finding, not an inspected Cake archive or selected integration.
No public single-call complete input list was found in the inspected interfaces;
this is not an exhaustive claim about every maintained extension.

## Integration Candidate and Limits

Use the pinned official public API through a compile-time assembly reference.
The imported task-path property is defined in targets, so a scoped target must
add the reference after that definition and before assembly resolution. Ordinary
MSBuild resolves/copies dependencies. No dynamic loading, installation probing,
private reflection, vendoring or additional runtime/helper is proposed.

Bind and dispose a native managed read-only context at the manifest directory
and explicit committed subject. Require a real repository, selected commit and
effective options; no working-copy, missing-repository or default substitution.
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

The source-backed integration choices have the bounded Linux packaging evidence
below. The accepted protocol qualified that boundary before application
implementation; it does not establish complete input-consumer mapping.
A concrete gap stops dependent work for owner disposition; it does not authorize
a replacement evaluator or a full-run fallback.

## Bounded Local Application Qualification Protocol

This protocol becomes executable only after repository acceptance under the
[contraction Wave](../../../../../../docs/delivery-wave.md). It qualifies the
official-library delivery candidate and the already implemented PNPM reader at
the repository-selected pin. It is a distinct application qualification, not a
refill or repetition of either completed PNPM CLI research budget.

- **Subject and environment:** active Linux x86_64 host, repository-pinned .NET
  SDK, NBGV 3.10.94 and PNPM 12.8.2. Record the accepted protocol commit, exact
  application/reader tree, native executable/package identities and hashes,
  environment, commands, elapsed times, exit codes and observations. Windows
  remains the owner's accepted assumption; no separate Windows experiment.
- **Isolation:** use one disposable NBGV probe project/repository with at most
  three local commits and two project directories, plus copies of the retained
  base/candidate inputs and native locks from the directory-join observation
  packet of PR #1009. Keep at most 32 manifests per PNPM copy. Use empty local
  auth inputs and isolated store/state/cache directories. Disable Git hooks;
  no credentials, registry reads, package scripts, install or lock regeneration.
  Restore probe dependencies from prepared local packages/caches; a missing
  dependency stops that case. Do not mutate repository locks or installed state.
- **NBGV operations:** at most five `dotnet` restore/build/publish commands,
  each bounded to five minutes, and four probe launches bounded to 30 seconds.
  The ceiling includes one bounded packaging correction, if necessary. Verify
  normal build and relocated publish execution with an actual public native
  context, effective options, filter calls and `VersionOracle` projection. Check
  historical committed-options/filter binding against a different worktree/HEAD,
  native inheritance and shared/excluded source association, configuration
  addition/deletion and a missing required subject. Qualify full oracle projection
  separately on clean exact-target materialization with the intended ref and
  target's native tags; the different-HEAD options test cannot establish it.
  Assert application use of native results rather than re-proving the parser or
  history algorithm. Retain failures before correction.
- **PNPM operations:** at most two complete calls to the accepted C# graph reader,
  one per fixture revision, with a combined ceiling of 240 native queries and
  15 minutes. Each native query retains the reader's 30-second deadline/output
  bounds. The ceiling also includes at most six direct
  `pnpm --dir <discovered-directory> pkg get scripts --json` queries. Check actual
  root membership, local/publish-directory joins, peer snapshots, local tarball
  consumers, unrelated exclusions and retention of removed base relations.
  Exercise a present script map and an absent field without executing scripts.
  The existing synchronized locks are inputs, not a new admission subject.
- **Stops and interpretation:** crashes, unexpected effects, missing inputs,
  ambiguous facts, invalid output or exhausted bounds stop the affected case.
  Report a necessary unavailable capability to the owner. A source-supported
  but unobserved shape remains unobserved; no complete-CI or repository-wide
  coverage claim follows from component success. No custom PNPM/NBGV evaluator,
  duplicate declaration, helper runtime or automatic full-run fallback.
- **Evidence and cleanup:** retain sanitized fixture/probe inputs, native locks,
  runner, full command outputs, package/deployment inventories, failed/successful
  results and hashes in the governing PR before deleting only generated owned
  state. Leave source caches available for review. No hosted dispatch,
  publication, authentication, account/Environment/access or host-trust changes.

## Local Application Observation

The protocol was accepted at `ab0b6f67afa73e842e3d70d9c09a881c83f6b351`,
tree `7f7d59119c121f030e73f621980d2ddeb39c244d`, and executed on
2026-10-04 on Linux x86_64 with SDK 10.0.401, NBGV 3.10.94 and PNPM 12.8.2.
[PR #1015](https://github.com/hcoona/three/pull/1015) retains the qualification
packet: exact protocol, native/source identities, fixtures, runner, command
outputs, original failures, package/deployment inventories and independent
reviews. These are component integration observations, not complete CI or
Windows runtime evidence.

The standard official package supplied a compile-time reference through a
scoped `BeforeTargets="ResolveAssemblyReferences"` target using
`NerdbankGitVersioningTasksPath`. Local-only restore, ordinary build and
framework-dependent publish succeeded. The copied publish directory ran with
the same full native oracle result; its managed dependency bytes matched the
official package. No dynamic loading or additional helper runtime was needed.
Restore used only the prepared local feed, with audit disabled; build and
publish prohibited implicit restore.

Native committed options and filters were exercised across configuration
absence, addition with inheritance, and deletion. Effective product versions
were `1.2-beta`, `2.4-beta` and `1.2-beta`. Shared/project inclusion, explicit
exclusion and unrelated-input exclusion matched the fixture. Historical options
remained bound to their selected commit with a different HEAD and deliberately
different working-copy options. Full projection was observed separately at clean
exact-target HEAD with `release/native-qualification` and its target-associated
`vqualification` tag; normal and relocated projections were equal.

Two preparation failures remain failures: initial restore could not discover the
archive under its acquisition-carrier filename, and an incorrectly attempted
dependent build failed on the same missing package. One bounded correction copied
the identical prepared archive under NuGet's native flat-feed filename, without
new acquisition. The historical runner then passed 23 assertions and exited 1
because the native missing-commit rejection was `GitException`, while the runner
expected `ArgumentException`. Its original source/output and failed status remain
retained. A separate read-only verifier confirms the actual explicit native
rejection; it does not turn that process into a passing run.

The fixture's committed runner and the corrected external assertion runner are
recorded separately. Two count/descendant assertion corrections preceded native
evaluation. Build compiled the corrected external runner, then restored the
committed file before clean exact-target oracle calls; publish reused that build.
The fixture repository supplied the oracle's product inputs, not the identity of
the externally compiled assertion runner. The copied production PNPM sources
were unchanged.

The [PNPM application observation](./pnpm-native-planning.md#actual-reader-and-script-metadata-observation)
qualifies both actual reader revisions and native script metadata. Six read-only
retained-output tests passed, covering native options, missing-subject rejection,
relocated projection, reader relations/scripts, removed-base consumers, exact
inputs and finite effects. They execute no native operations.

The original cumulative ledger used all five restore/build/publish commands,
three of four NBGV launches, all three local commits, both reader calls,
169 of 240 PNPM queries and four of six direct script queries. Combined reader
wall clock was 10.7984 seconds within 900 seconds. Acceptance retains these spent
limits; no operation is repeated or budget renewed by this observation.
Consumer mapping, complete native fact assembly, quality/check expansion and
workflow cutover still require implementation and their own ordinary validation.

## Recheck Trigger

Before relying on these findings after an NBGV pin/engine/package change or a PNPM
pin/reader-query change, the integration author rechecks affected native APIs,
package delivery and read/output semantics. An independent engineering/evidence
reviewer evaluates the retained comparison and required qualification scope in
the governing PR. The next affected integration/caller review is the fallback.
Existing observations keep their original pins and limits; a recheck neither
renews spent budgets nor supplies runtime evidence by source comparison alone.

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
[cake]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Cake.GitVersioning/Cake.GitVersioning.csproj#L55-L80
[projection-context]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs#L44-L117
[managed-context]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Managed/ManagedGitContext.cs#L50-L67
