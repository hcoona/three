# NuGet Smoke Second-Slice LLD

## Status and Authority

This design realizes the confirmed `WD-NUGET-*` [requirements](./requirements.md#nuget-second-slice),
the [HLD extension](./high-level-design.md#nuget-second-slice-extension), and
the five current MLDs. It specifies the design and its remaining admission
gates. No implementation, native probe, activation, or publication
is authorized by this document. The [handoff](./nuget-smoke-research-handoff.md)
owns current operating status.

## Bounded Product and Identities

| Concern                       | Selected contract                                                                                       |
| ----------------------------- | ------------------------------------------------------------------------------------------------------- |
| Repository and Live ref       | `hcoona/three`, protected `refs/heads/main`; resolved workflow, control, and target SHA are identical   |
| Release Unit and product root | `hcoona-release-smoke-github-packages`, `src/public/lib/hcoona-release-smoke-github-packages`           |
| Package                       | `Hcoona.ReleaseSmoke.GithubPackages`, GitHub Packages Buddy only                                        |
| Product API                   | `HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId` returns `hcoona-release-smoke-github-packages`       |
| Variant and Build Definition  | One `net10.0` managed library, Windows, `dotnet/nuget-package-v1`                                       |
| Logical output and producer   | `nuget-package`, one `.nupkg`, producer `build-nuget-package`                                           |
| Required artifact quality     | `dotnet/nuget-artifact-contents-v1` and `dotnet/nuget-restore-build-invoke-v1`                          |
| Destination                   | `nuget/github-packages-hcoona-three-v1`; service index `https://nuget.pkg.github.com/hcoona/index.json` |
| Protected NuGet source        | `.github/workflow-delivery/governance/hcoona-release-smoke-github-packages.json` on protected `main`    |
| NuGet Governance schema       | `workflow-delivery/v3/normal-live-nuget-governance-attestation-v1`                                      |
| Approval Environment          | Existing `workflow-delivery-v3-buddy-approval`; same human-approval semantics, new current-run Approval |

These new catalog identities and the NuGet Governance path are design
selections, not existing runtime capabilities or external configuration.
Current v3 descriptor basenames remain `workflow-delivery.release-unit.yml`
and `workflow-delivery.quality.yml`. The release policy is a new unit-specific
file under `eng/workflow-delivery/v3/policies/`. No historical descriptor,
dependency lock, or control-plane asset is restored wholesale.

## Provider and Frozen Build

The .NET Provider is a target-evaluating, unprivileged mechanism. It binds the
exact target, complete ancestry and tags, pinned SDK and package tools, project
and imported configuration, dependency graph, framework, packability, native
package identity, canonical NBGV facts, native `NuGetPackageVersion`, and the
assembly-version inputs needed by the selected Build Definition. It evaluates
the real imported MSBuild/NBGV target path rather than treating XML or
pre-target properties as final facts. Ambient dispatch-ref, CI, and branch
variables must not select a different native version.

A strict `dotnet-provider-result` and `dotnet-provider-fact-bundle` carry these
facts with the existing purpose, target, request, producer, digest, and
immutable-transport bindings. Decision code admits the Bundle without loading
the project. The existing static-reference NuGet helper remains a separate
mechanism; it is not renamed into a Release Provider.

Build applies the frozen native projection and produces one package. The
pinned NBGV `3.10.94` targets call `GetBuildVersion` from pack/build hooks;
setting `PackageVersion`, disabling assembly-info generation, or disabling
NBGV's Git engine does not by itself suppress recomputation. No undocumented
skip switch is permitted.

The selected implementation mechanism is a project-local frozen-build mode
that updates the NBGV `GlobalPackageReference` with `ExcludeAssets="all"`
before NuGet converts global references to package references. It retains the
dependency and CPM version while excluding NBGV's executable build assets.
Normal Provider evaluation retains NBGV. Frozen restore uses a separate clean
intermediate directory so previously generated NBGV imports cannot load before
the new asset selection takes effect. The same mode and property set apply
through restore, build, and pack; other packages' build assets remain enabled.

The SDK receives `Version` and `PackageVersion` from frozen
`NuGetPackageVersion`, `AssemblyVersion` from its namesake native fact,
`FileVersion` from `AssemblyFileVersion`, and `InformationalVersion` from
`AssemblyInformationalVersion`. Set
`IncludeSourceRevisionInInformationalVersion=false` in this mode. Missing
frozen inputs fail closed. This source-supported mechanism still requires the
local gate below; a failure blocks the Build Definition rather than relaxing
`WD-NUGET-002`.

Before this Build Definition is admitted, pinned-toolchain validation must
prove that the complete restore/build/pack path consumes the frozen package
and assembly projection, executes no NBGV version-selection task, preserves
the declared dependency graph and locked restore, and emits the expected
native package identity. The SDK must not append another source revision to
an already frozen informational version. A resulting `.nupkg` match alone
does not prove that recomputation was suppressed. Any required adjustment
stays in the selected project or adapter; global NBGV and symbol policy remain
unchanged.

## Package Bytes and Witness

The Build Adapter includes canonical UTF-8
`workflow-delivery/provenance.json` in the `.nupkg` through the declared pack
input before packaging. Its logical fields reuse the v3 Package Target
Witness: schema, target, Release Unit, canonical/native version facts, Build
Definition, catalog/control digests, and purpose. It excludes run, Attempt,
Approval, transport name, and wall-clock identity.

Internal provenance separately binds the current request, producer, source
input manifest, exact toolchain, output role, immutable transport, content
size, SHA-256, SHA-512, and witness digest. NuGet `RepositoryUrl` and
`RepositoryCommit` metadata do not replace either provenance layer.

The archive produced by pack is the archive qualified, uploaded, downloaded,
and submitted for publication. Transport materialization may restore its
logical basename; it must not unpack/repack, rewrite metadata, strip entries,
or transform signed or unsigned bytes. Native package inspection uses
`NuGet.Packaging`; native identity uses NuGet's version and identity libraries.
Preserve source display values separately from normalized protocol identity.

The concrete Node and NuGet record variants share existing binding primitives.
NuGet has a typed native version and `build-nuget-package` producer; it must
not populate npm version or lifecycle-script fields to pass old admission.
Admission derives the expected producer, media kind, Build Definition, and
quality obligations from the selected unit. Unknown variants and
cross-ecosystem substitutions fail closed. Existing npm record acceptance
must retain its exact bindings.

## Qualification and Consumption

The content obligation validates official NuGet package identity against the
frozen projection, the selected framework and assembly, declared content,
dependency metadata, the exact witness and source bindings, and whole-archive
digests. It rejects missing or extra primary outputs, a separate `.snupkg`,
inconsistent package contents, and substituted provenance.

The consumer obligation uses a temporary SDK-style consumer with an exact
NuGet version range, a fresh package directory/cache, controlled source
configuration, and no project reference or ambient fallback source for the
smoke package. It restores the qualified local archive, builds against its
assembly, and invokes the marker API. Evidence binds the actual selected
package, package bytes, and resulting behavior to the current artifact.
Content and consumer evidence remain distinct even if one physical job runs
both checks. CI and Release use the same definitions for their own separate
builds and evidence; production CI replacement is outside this slice.

After native acceptance and real publication, a fresh destination consumer
must restore the exact version from the selected GitHub feed and establish
the bytes actually consumed. That closes destination consumption, which a
local package-source test cannot establish. Restore may use the minimum
read-only credential; build and invocation must not retain it or receive
publication authority. A public GitHub package is not assumed to permit
anonymous NuGet restore.

## Observation, Publication, and Terminal Evidence

The destination reader discovers supported resources from the NuGet service
index. It uses supported version enumeration and actual `.nupkg` download,
plus GitHub package-control readback for owner, repository association,
visibility, and exposed access facts. Credential forwarding follows the
validated resource origin and redirect policy. A guessed download URL or a
metadata-only response is insufficient.

Desired state binds the native-equivalent package/version coordinate, actual
qualified bytes and digests, and in-package target witness. The resource key
uses NuGet-native identity so equivalent spellings serialize together. No npm
tag is part of this destination or its mutable-resource set.

| Observed state                                                                | Permitted outcome                                                                                      |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Exact current package bytes and witness                                       | Zero actions; repeat fresh Governance, package-control, and exact-byte checks before `exact-satisfied` |
| Definitive active absence with admitted native creation profile               | One fully materialized create action, subject to current-run Approval                                  |
| Different bytes or witness, incomplete readback, unknown or conflicting state | Block; no overwrite, repair, or success-shaped fallback                                                |

The action binds only the selected package, frozen native version, exact
archive, resource keys, and canonical NuGet operation-profile digest. Approval
sees those values, artifact manifest/digests, target, qualifications, fresh
Governance, and native admission before authorizing that action. The trusted
publisher executes no MSBuild, pack, restore, product hook, or consumer code.

The selected profile is `nuget/github-packages-http-put-v1`: a dedicated
one-shot HTTP adapter for the official `PackagePublish/2.0.0` resource. It
discovers and admits that endpoint before mutation, then sends one `PUT`
whose first multipart part contains the unchanged qualified `.nupkg` bytes.
No symbol upload, wildcard, duplicate-skipping mode, or secondary command is
present. Official NuGet libraries continue to own package/resource
interpretation and identity; this is not a custom NuGet grammar.

The repository-pinned SDK's ordinary `dotnet nuget push` path permits three
total attempts and has no public push argument to make that path one-shot.
It is therefore not the selected publication operation. Do not use NuGet's
testing-only retry-handler injection in production.

The HTTP adapter must bind its exact library/runtime, endpoint, authentication
headers, multipart construction, timeout, and response handling into the
canonical profile. Use preconfigured short-lived repository-token
authentication; automatic redirects, authentication-challenge resubmission,
transport retries, and application retries are disabled. Before profile
admission, source inspection and fault scenarios must demonstrate no second
potentially mutating request after authentication denial, redirect, conflict,
throttling, server failure, timeout, or dropped response. One transport API
call is not by itself that proof. Missing proof keeps publication disabled.

The publisher persists the existing mutation-may-have-started marker before
the mutating operation. A successful Publication Result requires definitive
success and supported actual-byte/witness readback. A duplicate, timeout,
non-success, ambiguous response, or missing Result remains conservative under
the existing current-Attempt terminal protocol. Exact post-failure readback is
diagnostic, not permission to relabel failure as publication success. The
read-only Finalizer and new-dispatch retry semantics remain unchanged.

## Protected Admission and Permissions

Every authoritative Live job requires platform attempt one. Eligibility
requires `hcoona/three`, `hcoona`, protected-main selection, reviewed
same-revision control, exact unit/policy/profile bindings, and fresh NuGet
Governance. It rejects before any authority-bearing Environment job when
those facts cannot be established. Old npm control cannot admit the distinct
NuGet Governance schema or borrow npm native readiness.

The NuGet Governance document reuses the v3 freshness and path-touch
anti-rollback obligations at its own path. It binds the selected package,
protected-ref and review policy, sole-writer and self-review decision,
repository-token reach, exact Approval Environment configuration, retention,
and NuGet native profile/evidence. A blocked native state carries no fabricated
generation and requires `live_enabled: false`. Ready admission requires the
exact independently audited profile, fixtures, native evidence generation,
producer/tooling identities, and fresh platform facts.

The existing Approval Environment supplies the same operator confirmation;
its current marker and native settings must be read back. No prior Approval
or Authorization is adopted. Only the build-free publisher has effective
`packages: write`; a step-free reusable-workflow caller may declare only its
required non-elevating ceiling. Other jobs have no publication authority.
There is no PAT fallback, `id-token: write`, administrator credential, new
permission grant, or Environment modification in this design delivery.

## Native Acceptance Plan and Gates

The proposed minimum generation has one fresh coordinate and two fixture
archives: A and a different-byte candidate for the same native coordinate.
It performs three sequential publication invocations: create A, repeat the
identical A, then attempt the different-byte candidate. Each invocation uses
the actual Actions-issued repository token under the exact pinned profile,
with complete before/after active-state captures. Both duplicates must fail
definitively and leave the declared active state unchanged.

Sequential scenarios do not alone establish atomic behavior under concurrent
creation. Native admission also needs a sufficient service-owned guarantee
for atomic non-overwriting active creation; the generic NuGet protocol does
not promise that for every feed. Missing GitHub-specific assurance blocks
activation even if the sequential scenarios pass.

Official-parser cases must cover native-equivalent version and package-ID
spellings. The native fixture contract must close how those equivalences are
exercised by the selected profile; if an extra native scenario is necessary,
revise and separately authorize the concrete budget before executing it.
There is no tag race, deleted-state query, delete, restore, or cleanup step.

The generation retains requests, actual fixtures, package/native identities,
toolchain and profile digests, workflow/run/actor/token-principal provenance,
process outcomes, raw service responses, complete declared active inventories,
actual scenario bytes/witness, and consumer evidence. Independent audit checks
native lineage, allowed deltas, both duplicate failures, exact byte preservation,
and clean destination consumption. Unknown outcomes or unexpected deltas stop
the generation without retry. No scenario count in this proposal is executable
authorization.

The following evidence is still required before corresponding admission:

| Gate             | Evidence that closes it                                                                                                                                                       |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Frozen Build     | Pinned native evaluation and restore/build/pack evidence proving frozen package/assembly projection with no NBGV recomputation                                                |
| Profile          | Source, effective-configuration, and fault-scenario proof for the exact one-shot HTTP adapter, followed by native acceptance                                                  |
| Service          | Required resource discovery, actual-byte preservation, active duplicate behavior, target witness, and clean destination consumption                                           |
| Authority        | Fresh authenticated main/review, actor, Approval Environment, package-control, retention, and relevant access readback                                                        |
| Real publication | A separately authorized fresh run with its own qualification and Approval, authoritative published Outcome, exact destination bytes, consumer evidence, and independent audit |

## Delivery Units and Validation

1. Deliver the coherent requirements, HLD, MLD, glossary, migration, and LLD
   changes after documentation/HK checks, independent review and adjudication,
   and contraction. No runtime file changes belong in this unit.
2. After implementation authorization, deliver the bounded native Provider and
   frozen Build/Quality mechanisms, then typed artifact and destination
   integration, all with NuGet Live disabled. Preserve npm contract behavior.
3. Deliver acceptance tooling for the closed NuGet profile. Present its exact
   fixture coordinates, operation budget, evidence paths, and stop conditions
   before requesting native execution authorization.
4. After passing independent native audit, prepare fresh NuGet Governance and
   protected activation. Present the concrete first publication request;
   execute only with its separate authorization and current-run Approval.
5. Retain and independently audit all required real-run and destination
   evidence. A failed or ambiguous run does not satisfy the completion goal
   or grant a replacement run.

Implementation validation emphasizes Provider/native-version bindings,
purpose and producer substitution failures, package and consumer scenarios,
native-equivalent identity, zero-action freshness, one-action authority,
publication uncertainty, and cross-ecosystem rejection. Use core contract
tests for those invariants and scenario tests for behavior. Do not freeze
ordinary shell choreography or an exact job DAG. Local gates precede OCR;
protected delivery and exact reviewed/merged-tree checks precede claims of
remote availability.

## Source Basis

These sources support design choices; they are not native acceptance evidence.

- [NuGet identity and normalized versions](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#normalized-version-numbers),
  [package content discovery](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource),
  and [the publication protocol](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource#push-a-package).
- [GitHub NuGet authentication and package association](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry).
- [NBGV 3.10.94 targets](https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/build/Nerdbank.GitVersioning.targets)
  and [native output mapping](https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/Nerdbank.GitVersioning.Tasks/GetBuildVersion.cs).
- [Bundled NuGet global-reference conversion](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/nuget-client/src/NuGet.Core/NuGet.Build.Tasks/NuGet.targets)
  and [SDK assembly-info generation](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.GenerateAssemblyInfo.targets).
- [Bundled ordinary push construction](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/nuget-client/src/NuGet.Core/NuGet.Protocol/Resources/PackageUpdateResource.cs),
  [request defaults](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/nuget-client/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceRequest.cs),
  and [retry behavior](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/nuget-client/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpRetryHandler.cs).
