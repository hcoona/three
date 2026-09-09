# NuGet Second-Slice Research Handoff

## Status and Authorization

This is an operating handoff. The research delivery and interactive
requirements confirmation are complete. The canonical confirmed product,
trust, and acceptance requirements are `WD-NUGET-*` in
[Requirements](./requirements.md#nuget-second-slice). The design extends the
HLD, five MLDs, and brief LLD in that order toward validation of this slice.
Do not repeat the completed research delivery.

The user selected `Hcoona.ReleaseSmoke.GithubPackages` and confirmed that its
existing container is operator-controlled, dedicated to smoke use, and has no
production dependency or existing consumers requiring compatibility. This is
an operator statement of intended use, not an exhaustive consumer or access
audit. Product shape, channel scope, destination-specific trust, and acceptance
criteria are confirmed in `WD-NUGET-*`. Design delivery does not authorize
restoration, implementation, native acceptance, publication, or adoption of
historical delivery policy.

Do not change product code, tests, descriptors, workflows, dependencies,
Governance, Environments, credentials, or package access during design.
Do not dispatch, rerun, approve a deployment, publish a package, execute a
native acceptance probe, delete, restore, or repair anything. A subsequent user
request must authorize further work; this handoff grants no standing mutation
budget.

The npm Normal Live objective remains complete. Its two dispatch
authorizations are spent, and its native acceptance, first failure, successful
publication, and unresolved original-D recovery retain the boundaries in the
[main handoff](./agent-handoff.md). A second ecosystem cannot reuse that
authorization, Approval, native evidence, or `WD-SLICE-*` exception.

## Fresh-Context Start

Use a verified checkout of `hcoona/three`, not an absolute path copied from an
old session. Read root `AGENTS.md`, any gitignored `AGENTS.local.md`, the
[docs contract](../../../../AGENTS.md), [wiki index](../../../index.md), and
[main v3 handoff](./agent-handoff.md) before changing documentation.

Inspect current branch, `HEAD`, remote `main`, merge base, tracked and untracked
changes, and relevant PR state. Preserve unrelated work. The research baseline
is `d87b83b8a63652455c4cb72d4ca4b2ecde2650ee`; it is an evidence revision, not an
instruction to reset a newer checkout. Check whether this handoff is already
merged before repeating any documentary delivery.

Keep these facts in the working context:

- Requirements are confirmed and the design package is present. Verify its
  protected delivery in Git and the PR, completing only outstanding delivery
  gates. After delivery, disabled implementation is the next separately
  authorized phase. Targeted read-only inspection remains permitted;
  implementation and native or publication operations are not authorized.
- Both remembered .NET smoke projects existed, were intentionally removed,
  and still have associated public GitHub NuGet package containers.
- The GitHub Packages-named project is selected for the next slice, but has
  not been restored or admitted as a v3 Release Unit.
- Current release execution remains npm-specific; the NuGet static-reference
  helper is not a .NET Release Provider or publisher.
- Scope and trust are confirmed. The LLD identifies local and platform
  capability gates; concrete native acceptance and publication requests must
  be closed before their respective execution authorizations.
- Preserve the waterfall, validation-before-OCR, independent adjudication,
  contraction, and protected-delivery gates below.

Load deeper context by decision, in v3 authority order:

| Decision                                      | Read next                                                                                                                                                            |
| --------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Product boundary and trust                    | [Requirements](./requirements.md): `WD-NFR-003`, `WD-AUTH-*`, `WD-SEC-*`, and the explicit scope of `WD-SLICE-*`; [Governance MLD](./governance-integration-mld.md)  |
| Provider, native version, and Build ownership | [HLD](./high-level-design.md#shared-foundation); [Repository Model MLD](./repository-model-release-unit-mld.md); [Shared Foundation MLD](./shared-foundation-mld.md) |
| Qualification and consumer acceptance         | [CI Qualification MLD](./ci-qualification-mld.md)                                                                                                                    |
| Observation, action, and terminal evidence    | [Release Delivery MLD](./release-delivery-mld.md)                                                                                                                    |
| Historical mechanism reuse                    | [Migration policy](./migration-strategy.md), then only the permitted historical mechanism                                                                            |

Use the [glossary](./architecture-glossary.md) for terminology. The npm LLD is
evidence of the first slice, not a NuGet specification. Do not preload its
operator commands, all prior run artifacts, or the full historical log to
answer the next requirements question. The protected documents and referenced
Git objects must suffice; old session folders, agent IDs, and local memory are
not prerequisites for continuing this research.

## The Remembered Projects

The current tree has `src/public/lib/hcoona-release-smoke-npm`, but neither of
the following .NET projects. Both were added in
`ac1659d92e2d80b2a6687af657193abf75ee322d` and deliberately removed in
`50e4463a8d355e28ad23d3fca944182fd04efe0d`. The removal commit explicitly
removed obsolete smoke fixtures and inherited pre-v3 control-plane assets
while restoring the clean merge scope.

The last pre-removal revision is
`21de27b796b376c6e086e7c63664cf23f94cbc89`:

| Historical project                                                                                                                                                        | Native package ID                    | Small reusable product behavior                                                   |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------ | --------------------------------------------------------------------------------- |
| [hcoona-release-smoke-github-packages](https://github.com/hcoona/three/tree/21de27b796b376c6e086e7c63664cf23f94cbc89/src/public/lib/hcoona-release-smoke-github-packages) | `Hcoona.ReleaseSmoke.GithubPackages` | `HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId` returns its project identifier |
| [hcoona-release-smoke-nuget](https://github.com/hcoona/three/tree/21de27b796b376c6e086e7c63664cf23f94cbc89/src/public/lib/hcoona-release-smoke-nuget)                     | `Hcoona.ReleaseSmoke.Nuget`          | `HcoonaReleaseSmokeNuget.Smoke.ProjectId` returns its project identifier          |

Each directory had six files: a README, `Smoke.cs`, SDK-style project,
`packages.lock.json`, `version.json`, and `three.release.yml`. Both projects
targeted `net8.0`, enabled nullable annotations and XML documentation, and
exposed a stable marker rather than general-purpose library functionality.

The names do not establish a working registry path. At introduction, the
NuGet-named descriptor selected nuget.org for Official; the GitHub
Packages-named descriptor selected GitHub Packages for Official. Commit
`800b128b2693796b9dff2b401fd42cdcf3f2d422` also added GitHub Packages to the
latter's Buddy profile. After
`7a55d760c17e8fa79897fcb8aaa4e1f30b40b5c5`, both final pre-removal descriptors
selected only `github-release/public` for both profiles. They declared a
`.nupkg` and a `.snupkg`, rather than the confirmed single-artifact scope.

Those `three.release/v1alpha1` descriptors are historical inventory, not v3
authoring. Do not restore them, their old locks, workflows, or control plane
wholesale. Historical test references include the acceptance matrix and
workflow-control tests; this inventory does not establish a reusable clean
consumer scenario or a v3 acceptance verdict.

A portable read-only inspection is:

```text
git show 21de27b796b376c6e086e7c63664cf23f94cbc89:src/public/lib/hcoona-release-smoke-github-packages/hcoona-release-smoke-github-packages.csproj
git show 21de27b796b376c6e086e7c63664cf23f94cbc89:src/public/lib/hcoona-release-smoke-github-packages/three.release.yml
git log --all -- src/public/lib/hcoona-release-smoke-github-packages src/public/lib/hcoona-release-smoke-nuget
```

If the objects are unavailable in a shallow checkout, establish the relevant
ancestry before concluding the project never existed. Do not restore files as
a way of inspecting history.

### Retained Package Containers

Authenticated, paginated GET-only inventory on 2026-09-09 returned these public
NuGet containers associated with `hcoona/three`:

| Package                                                                                                                         | Container ID | Active versions observed                             |
| ------------------------------------------------------------------------------------------------------------------------------- | ------------ | ---------------------------------------------------- |
| [Hcoona.ReleaseSmoke.GithubPackages](https://github.com/users/hcoona/packages/nuget/package/Hcoona.ReleaseSmoke.GithubPackages) | `12024661`   | `1.0.0-beta.253.gc1837dc`, `1.0.0-beta.254.g9fa9b96` |
| [Hcoona.ReleaseSmoke.Nuget](https://github.com/users/hcoona/packages/nuget/package/Hcoona.ReleaseSmoke.Nuget)                   | `12026442`   | `1.0.0-beta.254.g9fa9b96`                            |

This establishes retained service objects, not current write authority,
disposability, absence of consumers, exact package bytes, or publication
semantics. No `.nupkg` was downloaded, built, executed, or published for this
inventory. Exact version IDs and observation chronology belong in the
[research log](../../../log.md#2026-09-09-query--research-the-nuget-second-slice-handoff).
Reinspect accessible state before a future decision; do not encode these
counts or old coordinates as invariants or reuse them for probes.

## Current Repository Constraints

The following are source-inspection findings at the research baseline, not
evaluated MSBuild or successful build results:

| Surface                                                         | Finding and consequence                                                                                                                                                                                                                                                                           |
| --------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `global.json`                                                   | SDK `10.0.300` is pinned with roll-forward disabled; the test runner is Microsoft.Testing.Platform. Historical `net8.0` is not automatically the new target decision.                                                                                                                             |
| `Directory.Build.props`                                         | The current framework property is `net10.0`; CPM, NBGV, SourceLink, deterministic builds, and locked CI restore are shared. `IncludeSymbols=true` and `SymbolPackageFormat=snupkg` conflict with blindly assuming one package output.                                                             |
| `src/Directory.Build.props`, `src/public/Directory.Build.props` | Public libraries are packable. The initial root-default `GitVersionBaseDirectory` is overridden for public projects by the nearest `version.json` above the project directory; inspect the complete import chain, not only the initial default.                                                   |
| Root and project `version.json`                                 | Root path filters omit the removed smoke projects, but a project-local file selects its own base through the public-project override. The historical files used `inherit=false`. Do not add root filters merely because the names are missing; validate the chosen lineage and native projection. |
| `src/public/lib/Directory.Build.props`                          | Public-library analyzer and AOT-compatibility defaults are inherited. Do not copy a historical lock or suppress new diagnostics without understanding current evaluation.                                                                                                                         |
| `dirs.proj`                                                     | Traversal discovers projects under `src/` and `tests/`; even restoring a small project changes repository build scope. General C# CI uses Windows per `AGENTS.md`.                                                                                                                                |

For any later implementation, use official MSBuild/NuGet/NBGV mechanisms to
evaluate native facts rather than interpreting `.csproj` XML as the final
model. Do not recompute a different version while building: the Provider owns
target-bound canonical and native NBGV facts; Build applies the frozen native
projection. Required complete Git history and neutral exact-target evaluation
must not be replaced by the dispatch ref.

### What v3 Already Has, and What It Does Not

Python paths below are relative to
`src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/`.

| Surface                                                                                                | Reuse boundary                                                                                                                                                                                              |
| ------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Shared canonicalization, artifact/provenance primitives, Qualification and Release authority semantics | Reuse their contracts; do not invent a second Approval/Outcome protocol for NuGet. This does not imply all current representations are ecosystem-neutral.                                                   |
| `repository/node_provider.py`, `repository/compiler.py`                                                | Current admission uses `NodeProviderFactBundle` and `NodeProviderResult`; a native .NET Release Provider is not implemented.                                                                                |
| `repository/descriptors.py` and `eng/workflow-delivery/v3/policies/`                                   | The admitted Release Unit, output, Build/Quality definitions, destinations, and policy are bounded to the npm first slice. A new descriptor alone cannot enable NuGet.                                      |
| `adapters/node.py`, `adapters/github_packages.py`, `adapters/npm_runtime.py`                           | Existing package construction, destination state, and command profiles are npm implementations despite the generic-sounding GitHub Packages filename.                                                       |
| `release/publication.py`, `records/release.py`                                                         | Publication imports Node/npm mechanisms; Release Artifact admission includes the exact `build-tarball` producer. Inspect the concrete seams before claiming a drop-in adapter change.                       |
| `src/private/app/workflow-delivery-v3-nuget-authority/Program.cs`                                      | This existing C# helper admits `nuget-lock` and `nuget-packages-config` static-reference facts. It does not evaluate a Release Unit, freeze NBGV, build a package, publish, or observe a NuGet destination. |

The HLD, MLD extensions, and NuGet LLD identify the representation and adapter
changes required by the concrete second case. `WD-NFR-003` expects ecosystem additions
without changing cross-system authority semantics; it does not authorize a
universal plugin framework or speculative Environment Profiles.

## Verified Platform Findings and Remaining Gates

Independent official-source research on 2026-09-09 established the following
documentation baseline. It performed no registry experiment. Distinguish
documented client behavior from a GitHub service guarantee and from native
acceptance evidence:

| Topic                    | Confirmed documentation and limit                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Authentication           | [GitHub's NuGet guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) supports Actions `GITHUB_TOKEN` for packages associated with the workflow repository. A local operator's PAT authentication uses PAT classic. That is not permission to put a PAT into runtime or adopt nuget.org trusted publishing.                                                                                                                           |
| Access                   | [GitHub package access](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility) distinguishes repository linkage, permission inheritance, visibility, and Manage Actions access. A linked public container alone proves neither current writer access nor package-isolated token reach.                                                                                                                                                     |
| Native identity          | [NuGet versioning](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning) documents normalization, including removal of build metadata from normalized identity. Use official NuGet identity/version comparison; do not distinguish packages only by metadata that NuGet ignores or rewrite display casing merely to construct protocol paths.                                                                                                                                        |
| Native version authority | [NBGV's VersionOracle](https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/src/NerdBank.GitVersioning/VersionOracle.cs) exposes `NuGetPackageVersion`. [MSBuild evaluation](https://learn.microsoft.com/en-us/visualstudio/msbuild/evaluate-items-and-properties) distinguishes evaluated properties from post-target values; NBGV-computed properties require the appropriate native target path. Match current upstream findings to the pinned repository toolchain before implementation. |
| Pack metadata            | [NuGet pack targets](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets) define `PackageId`, `PackageVersion`, `RepositoryUrl`, and optional `RepositoryCommit`. Optional source metadata is not registry-enforced provenance and is not automatically the v3 artifact witness.                                                                                                                                                                                                      |
| Push result              | [dotnet nuget push](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push) publishes an existing package. `--no-symbols` concerns publication, not whether pack created a symbol artifact. `--skip-duplicate` converts an HTTP 409 to a warning; it proves neither equality nor exact-satisfied state. No command profile is approved here.                                                                                                                                         |
| Readback                 | The GitHub guide publishes `https://nuget.pkg.github.com/NAMESPACE/index.json`. [PackageBaseAddress](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource) defines discovered resource endpoints for version enumeration and actual `.nupkg` download. Confirm the selected feed's supported resources; do not invent endpoint paths or substitute metadata for actual artifact observation.                                                                                   |
| Non-overwrite            | The [generic NuGet publication protocol](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource) explicitly distinguishes nuget.org's duplicate rejection from other feeds that may replace a package. The reviewed GitHub guide does not establish a GitHub-specific active-version non-overwrite or upload-byte-preservation contract. This is a required capability gate, not evidence that GitHub actually permits overwrite.                                                     |
| Administrative lifecycle | [GitHub deletion/restoration](https://docs.github.com/en/packages/learn-github-packages/deleting-and-restoring-a-package) differs from nuget.org unlisting; the generic NuGet delete operation is server-dependent. Do not transfer npm tombstone conclusions, tag-race scenarios, or administrative authorization.                                                                                                                                                                                 |

The NuGet design requires a sufficient destination contract and bounded
native acceptance for required creation, active duplicates, actual bytes,
provenance, and clean consumption. Documentation alone has not established
those GitHub-specific guarantees. Do not weaken v3 content or witness
requirements to metadata-only or semantic-only success because a service
guarantee is missing. Native-equivalent collision behavior, evaluated
toolchain/pack inputs, and current package grants still need evidence.
Intended use and the destination-specific threat/cost decision are confirmed
in `WD-NUGET-*`; those decisions do not establish platform or native facts.

Missing required platform guarantees block the corresponding capability;
they do not justify a runtime history service, administrator compensation,
unbounded retry, or a success-shaped fallback. Any later native acceptance
must have its own approved contract, disposable scope, bounded operations,
stop conditions, retained evidence, and independent audit.

## Confirmed Requirements

The user accepted the complete packet. The normative requirements are
[WD-NUGET-001 through WD-NUGET-008](./requirements.md#nuget-second-slice);
this summary routes the next agent to that authority.

| Confirmed concern        | Requirement and next evidence boundary                                                                                                                                                                                                                               |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Product and scope        | `WD-NUGET-001`: the selected marker library, `net10.0`, Windows, one `.nupkg`, no separate `.snupkg`, GitHub Packages Buddy only.                                                                                                                                    |
| Native facts and version | `WD-NUGET-002`: official evaluation at the exact target, complete history, and a frozen native NBGV NuGet projection. Actual evaluated facts are still future evidence.                                                                                              |
| Trust and authority      | `WD-NUGET-003` and `WD-NUGET-004`: reviewed protected-main control, isolated target evaluation/build, build-free publication, self-approval by the sole trusted writer, and explicitly accepted repository-token reach. Platform configuration still needs readback. |
| Qualification            | `WD-NUGET-005`: distinct package-content and clean exact-version restore/build/marker evidence for the Release-owned artifact.                                                                                                                                       |
| Destination acceptance   | `WD-NUGET-006`: independently establish the required active creation, duplicate, actual-byte, witness, and observation behavior for the NuGet profile.                                                                                                               |
| Completion               | `WD-NUGET-007`: local/package evidence, an audited native suite, and one audited real publication with its own current-run authority and actual destination bytes.                                                                                                   |
| Execution boundaries     | `WD-NUGET-008`: ordered design, subsequent implementation authorization, and separate concrete native and real-publication authorization.                                                                                                                            |

The next implementation still requires separate authorization. A descriptor alone cannot enable
NuGet: Provider/compiler, artifact representation, Build/Quality, destination
observation/publication, and authority integration need bounded extensions.
The [HLD extension](./high-level-design.md#nuget-second-slice-extension)
assigns ownership. Local tests or historical package versions cannot supply
missing native or real-publication evidence. No implementation or external
operation is authorized by the confirmed completion objective alone.

The [NuGet LLD](./hcoona-release-smoke-github-packages-lld.md) follows the five
MLD extensions. It selects a project-local frozen NBGV Build mode and a
one-shot standard-protocol HTTP publisher, with explicit local and native
admission gates. Review and protected-delivery evidence belong in Git, the
PR, and the append-only log. A passing three-invocation sequential suite alone does
not establish the required service-owned atomic-creation guarantee.

## Workflow and Discipline

These gates remain binding after the research is handed off:

1. **Requirements before design.** Preserve the confirmed `WD-NUGET-*`
   scope and threat/cost decision. Any new scope or risk change requires
   confirmation before the affected HLD, MLD, and brief LLD reconciliation,
   in that order. Existing general v3 approval does not approve a new
   slice-specific exception. Implementation follows design delivery and
   subsequent authorization.
2. **Bounded work ownership.** The user-facing agent owns scope and external
   authorization; the orchestrator decomposes substantial work into bounded
   worker tasks. A worker reports a need for further decomposition instead of
   silently expanding scope. Keep related commits human-reviewable and
   dependency-ordered; a PR may contain several related commits.
3. **Proportional design and tests.** Trust appropriate platform abstractions
   and official ecosystem parsers. Justify abstractions with actual scenarios,
   preserve valid public/internal contract states, and fail closed for
   unsupported extremes rather than endlessly hardening them. Prefer business
   scenario tests; reserve strict unit/contract tests for core algorithms,
   schemas, identity, authority, and failure behavior.
4. **Byte reuse by construction.** Adapters preserve identical logical build
   bytes by construction; verify that invariant during acceptance, not by
   routine build-versus-unpacked-artifact comparison. Required artifact
   integrity and provenance checks remain distinct obligations.
5. **Validation before OCR.** After future implementation, run affected
   project tests, root HK, and commit hooks before multi-agent review.
   Documentation-only delivery uses its applicable documentation/HK gates,
   links/reference checks, and append-only log verification, not an unrelated
   product build or full test campaign. Use `mise exec -- hk ...` for the
   configured toolchain and profiles.
6. **Independent review and contraction.** Use the
   `open-code-review-delegate` skill: preview, resolve rules, and review the
   complete Git path manifest and diff, including Markdown or new files
   omitted by a tool filter. Use multiple reviewers; each finding is one
   independently resolvable issue. A separate adjudicator decides TP/FP;
   fix confirmed findings and return to original reviewers until clean.
   Insert contraction after every two review iterations, and again after all
   changes/reviews and before creating the PR. Remove unnecessary scope,
   duplicated authority, over-modeling, and brittle tests; do not use
   contraction to omit required behavior.
7. **Protected delivery.** Open the PR only after the related commit group,
   validation, review, and contraction are complete. Use a ready PR when it
   should merge; draft is for stacked work not yet mergeable. Run hooks with
   commits, preserve the required Copilot co-author trailer, satisfy protected
   checks and review threads, merge the exact reviewed head without bypass,
   and verify the reviewed and merged trees. Do not label a review
   recommendation as formal approval or claim a pending merge is complete.
8. **Durable, concise handoff.** Write repository code, comments, commits, and
   documentation in professional American English for senior engineers.
   Keep claims truthful, relevant, clear, and no longer than necessary.
   Synchronize the handoff, v3 README, overview, index, and append-only log when
   status changes; keep chronology in the log rather than duplicating a run
   ledger in current-state pages.

Do not broaden credentials, destinations, channels, services, or requirements
without user confirmation. On ambiguous external outcomes, stop mutation and
investigate read-only; a retry or administrative repair is a new operation,
not implied permission to finish the original task.

## Handoff Acceptance

The handoff is usable only if a new subagent, given the checkout and this entry
path without the preceding conversation or research summary, can:

- distinguish completed npm work, confirmed NuGet requirements, and the
  unimplemented NuGet design, and name the next permitted action without
  restoring or publishing anything;
- find both historical projects, their intentional removal, retained
  containers, and the difference between old descriptors and current v3;
- identify native-evaluation and current implementation gaps without treating
  the static-reference helper as the missing Release Provider;
- reconstruct the authority order, waterfall, testing, OCR, independent
  adjudication, two-iteration/final/pre-PR contraction, and delivery gates;
- separate confirmed facts, recommendations, missing decisions, and
  future native evidence, using repository/public references rather than
  private session state.

The probe must demonstrate these tasks with references, not merely say that
the document is clear. Record actual gaps and closure in the append-only log;
if a correction materially changes takeover instructions, probe the corrected
entry again from fresh context. Do not treat the probe as a substitute for
OCR or protected-delivery gates.
