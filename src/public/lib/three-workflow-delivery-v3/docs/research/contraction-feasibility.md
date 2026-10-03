# Contraction Feasibility and Accepted Assumptions

## Decision and Evidence Scope

The [replacement HLD](../high-level-design.md) permits native NBGV execution
during build and consolidates the .NET provider and native-consumer helpers.
This record preserves the local observations informing those choices and the
owner's Windows assumption. It is not a live-publication or cutover acceptance
record and does not reopen any completed ecosystem campaign.

On 2026-10-03, after reviewing the Linux results, the owner directed that the
separate Windows feasibility check be skipped and work proceed assuming
feasibility. **Windows feasibility is an accepted assumption, not an observed
result or a deferred experiment gate.** Normal implementation/CI failures remain
actionable; this decision does not relabel historical validation or remove
required product behavior.

## Source and Local Protocol

The source was `c56b1efa64637f056b63a497aabbdaf33c1fbf1f`. Subjects were:

- `src/public/lib/hcoona-release-smoke-github-packages/` through its ordinary
  native build path, without frozen-version injection or a V3 package witness;
- `src/private/app/workflow-delivery-v3-dotnet-provider/` and
  `src/private/app/workflow-delivery-v3-nuget-consumer/` in one temporary executable;
- the existing `ConsumerRestoreTests` under
  `tests/private/app/workflow-delivery-v3-nuget-consumer/`, with only its project
  reference redirected to the combined executable.

The environment was Azure Linux 3.0, linux-x64, .NET SDK 10.0.401, MSBuild
18.9.11, runtime 10.0.12, NBGV 3.10.94, NuGet 7.9.0 and ProtectedData 10.0.12.
The source, tools and relevant inputs were fixed. Two sparse local clones shared
the read-only original Git object database, had independent working trees and
used detached HEAD at that revision with origin `https://github.com/hcoona/three.git`.
This did not test independent history reconstruction or shallow clones.

The bounded local protocol used independent NuGet extraction/HTTP caches,
intermediate directories and output roots. A local dependency-archive feed
seeded restores; NuGet.org was permitted for dependency setup. There was no live
destination access, credential acquisition, publication or platform change.
Each command had a maximum five-minute timeout. Six fixture pack attempts,
one combined-project/test build and one filtered test invocation were executed.
All mutations stayed in the disposable local experiment directory.

The replay recipe is:

1. Check out the subject with the required Git history, refs and native build
   configuration. Create two independent extraction and HTTP caches. Set source
   file mtimes to 1600000000 and 1700000000 in the two working trees.
2. Invoke `dotnet restore`, `dotnet build --no-restore` and
   `dotnet pack --no-restore --no-build` for the existing smoke project. Capture
   a distinct `-bl:<path>` for each invocation. Use Release, locked restore,
   ContinuousIntegrationBuild, Deterministic and target timestamp 1790988286;
   disable shared compilation. Keep BaseIntermediateOutputPath,
   MSBuildProjectExtensionsPath, output paths and cache locations explicit.
3. Map the checkout parent and intermediate parent to stable separate virtual
   roots with native MSBuild PathMap. Inspect original nupkg, DLL and portable
   PDB hashes, native package/assembly metadata, PDB documents and PE CodeView.
   Replay binlogs to check actual NBGV execution and absence of Csc during pack.
4. For separate negative controls, change only the working-tree smoke marker
   string, then change only the deterministic package timestamp by 120 seconds.
   These are controlled input changes, not valid builds of unchanged target input.
5. Restore the successful original package into a fresh standalone consumer from
   a local-only package source. Compile and invoke the expected marker; compare
   the installed original archive bytes with the produced package.
6. Combine the helpers' original namespaces/sources and dependency union. Make
   their Main methods callable by a small dispatcher. Preserve native restore's
   process responsibility. Build the existing test project against that assembly
   and run its 14 ConsumerRestoreTests cases through the existing MTP runner.
   Exercise the combined CLI's identity, package, service-index and binlog readers
   on local inputs. The restore cases use synthetic in-memory HTTP and credentials.

Original logs, packages, source-control files, scripts, assets, TRX and hashes
are retained in the local packet `workflow-v3-feasibility-2026-10-03`, including
`protocol.md`, `probe.py`, `combined.py`, `finish.py`, `observations.json` and
`evidence-sha256.json`. The packet is local working evidence, not a published
portable artifact. This record does not claim independent evidence acceptance;
canonical design review must distinguish the observations from support claims.

## Observed Results

The initial pair built successfully but failed byte equality. Its PathMap
covered the checkout parent and omitted the external intermediate root. PDB
documents and PE CodeView retained the differing physical paths of generated
NBGV and SDK assembly-info sources. Adding that root to native PathMap corrected
the recipe; no product-source or custom trust mechanism was needed.

| Case                  | Original nupkg SHA-256                                             | Result                                 |
| --------------------- | ------------------------------------------------------------------ | -------------------------------------- |
| Initial A             | `17125812dba9a6975d7f4116a7b529f446fcdd58818eac990b220a622b0589ba` | Different physical intermediate path   |
| Initial B             | `2e4bd9b00cc9717e4be2dd3d4296a28871751894dc4238239bd5a80feecfe7f3` | Equality failed                        |
| Corrected A and B     | `f27af96c4f99ecf92d02ba0291194a1921e65533bee305a63fc0d143e6d0d761` | Byte-identical package, DLL and PDB    |
| Source-change control | `bf36d0ded6d98f33922625a683b6e70b8e4a3b9d7f21562672737eb88c3d6eac` | Package, DLL and PDB all differ        |
| Timestamp control     | `222dd8fcfcfbba5a622828f9a0808d675e957457bb1696f826cf8788978f964f` | Package differs; DLL and PDB unchanged |

The corrected DLL hash is
`16975a0afa14357a485268e4fdaff896ca8d2352be79cdaa3a21663403a739e5`;
the corrected PDB hash is
`eb38c39a56407cfd7b2c9a0866a1f7e46727271bc78d4e84deb818764f63ce18`.
Build and pack each executed native NBGV; only build executed Csc. The clean
consumer installed exact original bytes and returned the expected marker.

Native package version was
`1.0.0-beta.254.gc56b1efa64637f056b63a497aabbdaf33c1fbf1f`.
Assembly informational version contained the full target commit. The nuspec
repository URL was present but its commit field was empty in this no-build-pack
recipe. Choosing nuspec commit as a carrier therefore requires normal native
RepositoryCommit configuration and output checking; this additional configuration
was not tested here. No universal in-package provenance format follows.

The combined helper/test build completed with zero warnings and errors. Native
identity normalization, package metadata inspection, service-index parsing and
MSBuild binlog replay succeeded. Existing consumer scenarios reported 14 passed,
zero failed/skipped, including direct/redirected native restore, exact installed
bytes, clean execution and rejection/failure cases. Provider commands were
separate CLI invocations; restore tests referenced the same combined assembly.
Simultaneous restore and project evaluation in one privileged process was neither
tested nor proposed.

## Architectural Inference and Limits

The observed Linux path supports permitting native NBGV recomputation while
retaining ordinary deterministic build inputs. It supports consolidating the
two helpers' dependency closure without merging real permission/process
boundaries. These are bounded feasibility inferences, not proof for every project,
platform or future dependency combination.

The earlier [reproducible-build change][repro-pr] and its
[independent acceptance][repro-review] remain evidence for the frozen Windows
path. They do not validate the newly restored native path on Windows. The owner
chose an assumption for that gap, rather than another experiment. No new Windows,
cross-OS, cross-SDK, cross-commit or old/new-format byte-equality claim is made.
Planner/build projection integration and final caller cutover are implementation
work; local success does not demonstrate them or any live registry behavior.

[repro-pr]: https://github.com/hcoona/three/pull/952
[repro-review]: https://github.com/hcoona/three/issues/948#issuecomment-5920516725
