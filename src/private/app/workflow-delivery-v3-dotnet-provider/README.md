# Workflow Delivery v3 native .NET helper

This helper reads native NuGet package data and MSBuild binary logs. It never
publishes packages. `NativeNuGetHelper` invokes a previously built DLL; reader
and publisher code must not build the helper or evaluate a product project.

The [Workflow Delivery v3 project](../../../public/lib/three-workflow-delivery-v3/README.md)
owns this component's contracts. Its
[NuGet LLD](../../../public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-github-packages-lld.md)
and [handoff](../../../public/lib/three-workflow-delivery-v3/docs/nuget-smoke-research-handoff.md)
define the native Provider, frozen Build and admission boundaries.

The pinned dependencies are .NET SDK `10.0.401`, runtime `10.0.12`, NuGet
libraries `7.9.0`, and the SDK's MSBuild `18.9.11` binary-log reader. The
project-specific `System.Security.Cryptography.ProtectedData` `10.0.8` central
version resolves the actual NuGet/MSBuild dependency conflict without changing
other projects' transitive pins.

The CLI writes one JSON object and returns nonzero on rejected input:

| Operation                                | Native interpretation                                                                                                                        |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `normalize-identity <id> <version>`      | `PackageIdValidator`, `NuGetVersion`, and `PackageIdentity`; original display strings plus lowercase normalized protocol identity            |
| `inspect-package <absolute-path>`        | `PackageArchiveReader`, native identity, frameworks, dependencies, repository metadata, exact witness bytes, and read-only assembly metadata |
| `service-resources <absolute-json-path>` | `ServiceIndexResourceV3`; exactly one distinct `PackageBaseAddress/3.0.0` and `PackagePublish/2.0.0` endpoint                                |
| `audit-binlog <absolute-path>`           | SDK `BinaryLogReplayEventSource`; completion, success, executed tasks, imports, errors, and whether an NBGV task executed                    |

## Frozen-build source contract

The selected smoke project updates its existing NBGV global reference with
`ExcludeAssets="all"` in frozen mode. SDK `10.0.401`'s `NuGet.targets`
converts `GlobalPackageReference` items into `PackageReference` items while
retaining source item metadata. The conversion explicitly sets only version,
included assets, and private assets, so the exclusion survives. The resolved
NBGV dependency and lock remain present; its executable build assets do not.

Pinned NBGV `3.10.94`'s `Nerdbank.GitVersioning.targets` adds `GetBuildVersion`
to build/pack hooks. `GetBuildVersion` depends on
`InvokeGetBuildVersionTask`; the selected caching path executes
`Nerdbank.GitVersioning.Tasks.GetBuildVersion`. Setting package or assembly
properties alone therefore does not suppress selection. The Provider
deliberately runs that native target and compares its projection with the
pinned official `nbgv get-version` result at the same detached exact target.

The Build adapter stages admitted source bytes in a new temporary directory,
uses a separate clean intermediate directory, and applies identical frozen
properties through locked restore, build, and pack. SDK assembly generation
receives the frozen package, assembly, file, and informational versions;
`IncludeSourceRevisionInInformationalVersion=false` prevents another suffix.
Project-local `IncludeSymbols=false` leaves repository symbol policy intact.
The adapter maps the entire temporary build root, including generated sources,
to `/_/` and passes the exact witness target commit's Unix committer time as
`DeterministicTimestamp` through restore, build, and pack. The frozen target
already binds this timestamp; it is not taken from the runner clock. Pack uses
`--no-build`, and its official binlog audit rejects compilation.

## Executable evidence

`tests/adapters/test_dotnet.py` in `three-workflow-delivery-v3` validates the
actual native path. The Provider log must contain NBGV execution. All three
frozen logs must complete successfully, contain no NBGV task or imported NBGV
build asset, and preserve the exact locked dependency file. Native inspection
then verifies the package and assembly versions, declared entries, and exact
witness. A separate fresh-cache exact-version consumer restores those same
archive bytes, builds, and invokes the marker API. Paired native builds vary
roots, caches, and source mtimes and compare original nupkg, DLL, and PDB bytes;
source and timestamp controls must change the package. The Windows CI job
retains originals and binlogs. Fresh modeled release Attempts join independently
built originals to new authority and prove exact-byte recovery without a
registry operation.

Evidence lives in each invocation's explicit evidence directory. A local
Linux pass establishes the mechanism; the selected Windows workflow still
owns its platform qualification. Neither result establishes destination
atomic creation, native acceptance, or publication authority.
