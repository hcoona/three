# Bounded unsigned NuGet reproducibility experiment

## Owner instruction and boundary

The owner explicitly requested completion of feasibility experiments before any
production change, and reserved Delivery Wave authorization for implementation.
This later instruction authorizes this isolated experiment only; it does not
amend the accepted Wave or authorize production integration. No main-branch
source/configuration change, release dispatch, registry upload/probe, signing,
credential/access change or native acceptance operation is included.

## Question and frozen inputs

Can the existing managed `net10.0` smoke source produce identical original,
unsigned `.nupkg` bytes in independent clean roots using maintained .NET/NuGet
mechanisms? Use accepted source `499e01fb4446d4d7ec65974a2481f0340ffc4457`,
the production provider's source closure, one native NBGV evaluation and one
canonical simulation witness. Preserve the package dependency lock byte for
byte. Freeze the timestamp to that source commit's Unix committer timestamp.
Record source/lock/witness hashes and every experimental input difference.

The harness mirrors the adapter's frozen restore, build and no-build pack
sequence in retained scratch directories. It does not weaken or bypass the
production adapter's pinned-toolchain admission. Candidate SDK selection and
MSBuild PathMap/timestamp overrides exist only in experimental staged copies.
Results establish a build mechanism, not production provider/profile admission.

## Protocol

1. Preserve the prior Linux counterexample and original packages. Prepare a
   frozen source packet with exact file hashes, NBGV facts and canonical witness.
2. Select a maintained official SDK containing the NuGet deterministic-pack
   fix. Inspect stable SDK 10.0.401 first. If its actual packer lacks the fix,
   inspect SDK 11.0.100-rc.1.26425.128 as a prerelease feasibility fallback;
   report its prerelease limitation. Do not change TargetFramework from net10.0.
   Explicit NuGet.Build.Tasks.Pack is considered only if a suitable released
   package exists; the current public index stops at 6.14.0.
3. Run two builds for each selected cell: old SDK unchanged; old SDK with
   normalized paths; corrected SDK with frozen timestamp but original paths;
   corrected SDK with both normalized paths and frozen timestamp. Every build
   has a distinct source/intermediate/output root and isolated writable caches.
   Perturb source file modification times between pair members. Do not reuse
   compiled outputs or normalize/repack archives before comparison.
4. Require the successful cell to have equal complete archive bytes, member
   names/order/content/timestamps and DLL/PDB bytes, unchanged source lock and
   witness, recorded actual SDK/MSBuild/NuGet identity and build/pack binlogs.
   Check PDB/debug data for physical-root leakage. Failure of an intermediate
   cell is diagnostic evidence, not a passing reproducibility result.
5. For the successful mechanism, change source content once and timestamp once
   as negative controls: full archive bytes must change. Retain those outputs
   separately and never count them as members of an equal-input pair.
6. Execute the same bounded comparison on Windows x64. Use a dedicated
   non-main experiment branch and a push-triggered experiment-only Actions
   workflow on windows-2025, with contents:read, no secrets, no Environment,
   no id-token grant and checkout credentials disabled. The branch contains
   only protocol/harness/input packet/workflow; it is not an implementation PR
   and will not be merged. Ordinary experiment artifact upload is permitted;
   no package registry operation or release workflow is invoked.
7. Retain original archives, DLL/PDB, source manifests, commands, binlogs,
   comparison JSON, actual runner/tool identities and public run/source links.
   Download hosted evidence locally, and retain an immutable experiment branch
   as the replay recipe. Obtain independent technical/evidence review before
   presenting a feasible solution. Record limitations and no-go outcomes.

## Bounds and stop conditions

At most 16 frozen package build invocations on Linux and 16 on Windows, at most
two experiment-only hosted workflow runs, each at most 40 minutes. Each native
command has a finite timeout. SDK discovery/download is bounded to the two
named candidates plus the existing baseline. Stop on archive/dependency hash
failure, unexpected external effect, changed source lock or witness, unsupported
toolchain, exhausted bounds, or contradictory evidence. Diagnose retained
failures before a corrected attempt; never blindly rerun an ambiguous dispatch.
If the supported Windows environment is unavailable, report the blocker and
do not claim Windows feasibility. Cross-OS byte equality is not required.

## Isolation and cleanup

All local experiments live under `/workspace/wdv3-repro-experiment-20260930`;
native temporary directories use a task-owned root. System SDKs and repository
tracked files stay unchanged. Retain evidence and protocol on completion;
remove only task-owned disposable caches after evidence is secured if space
requires it. No unrelated data/process cleanup. Hosted runners are disposable;
artifacts retain the configured finite lifetime, with downloaded evidence and
the pinned replay branch providing the continuation basis.
