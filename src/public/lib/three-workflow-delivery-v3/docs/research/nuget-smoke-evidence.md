# NuGet Smoke Research and Design Evidence

These extracts support the [NuGet handoff](../nuget-smoke-research-handoff.md),
[confirmed WD-NUGET requirements](../requirements.md#nuget-second-slice), and
[NuGet LLD](../hcoona-release-smoke-github-packages-lld.md). Read them in their
original sequence: researched facts and open choices, operator selection,
then confirmed scope/trust/acceptance and source-supported design. The
confirmed requirements are not native service evidence. Separate disabled
implementation, native-operation, and publication grants remain necessary.
Git and the delivery PR carry subsequent merge evidence; none is inferred
from a then-pending source entry.

The source is [`docs/wiki/log.md` at the accepted base](https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/docs/wiki/log.md),
Git blob `6764f2e8f6aede4894bc4ef826329fe81081e9f5`. Each dated heading and
its complete section below retain the source bytes, including then-pending
steps, failed observations, and later qualifications within that section.
The sections retain their original evidence level; they are not a new audit
or work authorization. The original operators and reviewers produced them;
v3 maintainers preserve them for implementers and reviewers at the linked
decision boundary. Referenced operator files and Actions artifacts have not
been downloaded or reverified by this record migration.

## [2026-09-09] query | Research the NuGet second-slice handoff

- The user explicitly limited the new task to research, a handoff document,
  a fresh-context subagent takeover probe covering both work and discipline,
  review, and merge. No implementation or external mutation was authorized.
  The npm proving objective remains complete and its two dispatches spent.
- Read-only inspection used protected baseline
  `d87b83b8a63652455c4cb72d4ca4b2ecde2650ee`. The active tree contains the npm
  smoke, not either remembered .NET smoke. Both
  `hcoona-release-smoke-github-packages` and `hcoona-release-smoke-nuget` were
  introduced in `ac1659d92e2d80b2a6687af657193abf75ee322d` and deliberately
  removed as obsolete fixtures in clean-scope commit
  `50e4463a8d355e28ad23d3fca944182fd04efe0d`. Their final pre-removal files
  remain inspectable at `21de27b796b376c6e086e7c63664cf23f94cbc89`.
- Both historical libraries targeted `net8.0` and exposed `Smoke.ProjectId`.
  Their old descriptors declared `.nupkg` and `.snupkg`; although earlier
  revisions selected native NuGet destinations, their final descriptors
  selected only GitHub Release assets. No historical source, descriptor,
  workflow, dependency lock, or control-plane implementation was restored.
- Authenticated paginated GET inventory started at `2026-09-09T16:25:31Z`
  using `/users/hcoona/packages?package_type=nuget`, filtered to association
  with `hcoona/three`, followed by each named container's active-version GET.
  Public `Hcoona.ReleaseSmoke.GithubPackages`, container `12024661`, retained
  version `851754773` (`1.0.0-beta.253.gc1837dc`, created May 9 at `01:14:30Z`)
  and `851978655` (`1.0.0-beta.254.g9fa9b96`, created May 9 at `05:24:34Z`).
  Public `Hcoona.ReleaseSmoke.Nuget`, container `12026442`, retained version
  `851978628` (`1.0.0-beta.254.g9fa9b96`, created May 9 at `05:24:33Z`).
  These observations do not prove current grants, consumer absence,
  disposability, byte identity, or native publication semantics. No package
  content was downloaded or executed and no native acceptance was performed.
- Current source inspection found a pinned .NET 10 SDK, inherited symbol
  package defaults, root-default `GitVersionBaseDirectory`, and root version
  path filters requiring future native evaluation. v3 Provider admission,
  descriptors, artifact producers, and publication remain npm-bound.
  The existing C# NuGet helper supplies static-reference facts, not a Release
  Provider or publisher. No product build or test campaign was run.
- Added a portable research handoff and synchronized its main handoff,
  README, overview, and index entry. The candidate scope and trust decision
  remain unconfirmed; old npm authority and evidence cannot transfer.
  Fresh-context probing, documentary validation, OCR closure, contraction,
  and protected delivery are subsequent gates, not claimed complete here.
- Follow-up inspection of the complete ancestor property chain qualified the
  initial root-default version observation: `src/public/Directory.Build.props`
  overrides `GitVersionBaseDirectory` with the nearest `version.json` above the
  project directory. A project-local file can therefore select its own base,
  and both historical smoke files used `inherit=false`. The handoff now
  explicitly warns against changing root path filters merely because the old
  smoke names are absent; future native evaluation must use the whole chain.
- Independent official-source research confirmed Actions-token versus local
  PAT-classic authentication, granular package relationships, native NuGet
  normalization and NBGV projection, service discovery, actual-package
  download, and the limited HTTP-409 meaning of `--skip-duplicate`. The
  reviewed GitHub documentation did not establish a GitHub-specific active
  non-overwrite or upload-byte-preservation contract; the generic NuGet
  protocol expressly permits different behavior on other feeds.
  These remain capability/acceptance gates, not evidence of a GitHub defect
  or permission to weaken v3 artifact and witness requirements. The returned
  research and import-chain correction are retained as
  `nuget-second-slice-platform-research.txt`, SHA-256
  `b917ae4df7d7a565753720794520c1e574501c8b8cab9e1f3087a5f59bcba3d2`.
- Initial staged documentation/HK validation passed, including the existing
  static-reference gate; the full v3 product suite was correctly unselected.
  The hook prepared its existing NuGet authority tool but did not build a
  smoke product. The original 233,759 log bytes were preserved exactly.
  OCR preview classified all six Markdown paths as unsupported extensions;
  explicit rule resolution and the complete Git manifest retain all six in
  the review scope rather than silently treating that empty preview as clean.
- The fresh-context takeover probe passed with no blocking gaps. The new
  agent received only the checkout, entry path, and read-only probe task.
  Without parent session history or research reports, it independently
  recovered the historical projects and deletion, final descriptors, retained
  containers, native property chain, and current implementation seams. It
  reconstructed the requirements gate, exhausted npm authority, waterfall,
  validation-before-OCR, independent finding adjudication, contraction
  cadence, and protected delivery. All fourteen local linked paths/anchors
  resolved, and the original log prefix remained unchanged.
- That probe also detected protected `main` moving to `a33e71eb` while the
  local tracking ref was stale. Follow-up fetch and inspection confirmed the
  new bootstrap and Delivery Wave govern repository-record/control migration,
  not this product research or runtime release authority. This handoff does
  not create a parallel migration policy, Wave, or authorization record.
- Both original OCR tracks, source/contract and caller/handoff/process,
  completed pre-PR round 1 with zero material findings for reviewed head
  `7e4ba954e03ab8d218c158c4b4bd191dfe7c0fd9`, tree
  `3d65d4cdb245b6e848d5a2e128f5754248308fb0`. Final and pre-PR contraction
  were clean; no second iteration was required at that point. This closes
  the previously pending OCR/contraction gates for that revision, not
  protected delivery or merge. Review of this narrow chronology closure
  remains in PR #666 rather than recursively adding review-status markers.

## [2026-09-09] query | Begin NuGet slice requirements confirmation

- GET-only inspection confirmed PR #666 merged as
  `f38d9f8d5c8ce0bcdf3d11d285f8edc1700d5550`; protected `main` and the clean
  starting checkout were both `d4c30ceba97190f4771141bc0200e5326ac3fce7`.
  The completed research delivery was not repeated.
- The user selected `Hcoona.ReleaseSmoke.GithubPackages` and confirmed that
  its existing container is operator-controlled, dedicated to smoke use,
  and has no production dependency or existing consumers requiring
  compatibility. This records operator intent, not an exhaustive consumer
  or grant audit. Follow-up GETs returned public NuGet container `12024661`,
  owned by `hcoona`, associated with `hcoona/three`, and the same two active
  version IDs recorded above. No package bytes were downloaded or executed.
- The user set the objective to complete slice validation. Scope, trust,
  and acceptance questions remain unanswered. The handoff now records those
  proposals separately from the confirmed product selection; implementation,
  native probes, and publication remain unauthorized.
- Current-source inspection reconfirmed Node Provider admission,
  `build-tarball` Release Artifact admission, and npm publication imports.
  Existing implementation or test success cannot establish a NuGet release
  capability. The historical marker returns
  `hcoona-release-smoke-github-packages`.
- Updated the handoff and navigation as a requirements-confirmation draft.
  No product, test, workflow, dependency, Governance, credential, package
  access, or external resource was changed. Documentation validation, review,
  and protected delivery are not claimed complete by this entry.

## [2026-09-09] query | Confirm NuGet scope and prepare the design

- The user confirmed the complete scope, NuGet-specific trust boundary, and
  acceptance objective. Added `WD-NUGET-001` through `WD-NUGET-008` as the
  canonical requirements, then drafted HLD ownership, the five MLD extensions,
  glossary/migration alignment, and the brief NuGet LLD. Implementation,
  native probes, and publication remain subject to subsequent authorization.
- Two bounded workers contributed nonoverlapping MLD updates; source-only
  Microsoft documentation and pinned-toolchain inspection informed the LLD.
  NBGV `3.10.94` source at `dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb`
  has no frozen-version replay switch. The selected project-local Build mode
  excludes its assets through native NuGet metadata, uses separate restore
  intermediates, and supplies frozen SDK package/assembly properties. Actual
  restore/build/pack evidence is still an implementation gate.
- SDK `10.0.300` bundles NuGet `7.6.0-rc.23102`; the helper's CPM `7.9.0`
  is not that client. Exact bundled source at dotnet/dotnet commit
  `caa81fa4971f74880cdab61990cb1b11420939ec` confirms ordinary push defaults
  to three total attempts. The LLD selects one standard-protocol HTTP PUT,
  requiring source/configuration and fault-scenario evidence for no replay,
  rather than an undocumented CLI retry switch or testing-only injection.
- The proposed minimum native generation has three invocations and no
  administrative operation. It is not authorized for execution. Sequential
  creation/duplicate evidence cannot alone establish concurrent atomic
  creation; sufficient service-owned assurance remains an activation gate.
  Existing npm evidence, authority, and completed operation remain unchanged.
- The earlier requirements-confirmation draft passed applicable root HK
  checks, 103 local-link/anchor checks, and exact preservation of the original
  240,161 log bytes. The expanded design requires fresh validation,
  independent review/adjudication, contraction, and protected delivery.
- The complete 16-path design passed applicable root HK checks, 152 local
  Markdown link/anchor checks, log-prefix verification, and commit hooks before
  independent OCR. OCR preview excluded Markdown, so explicit rule resolution
  and the complete Git manifest kept every changed path in review scope.
- At head `e9b721f259060e0f7ac35dda46a23942108e8173`, the source/contract
  reviewer reported zero material findings. The integration reviewer found
  one stale handoff sentence treating confirmed intended use and trust as
  open. A separate adjudicator classified it as a true positive. The fix
  retains unresolved native/platform evidence while preserving the confirmed
  requirements. Current-state wording was also contracted to remove transient
  review-stage labels; review and delivery chronology remain in Git and the PR.
  Original-reviewer rereview follows this correction.
- Both original reviewers completed rereview at
  `2cfa271c337a0fbe51d7fe6f8a0ff401984db03b`; the stale-decision finding was
  closed, with zero remaining material findings across the full 16-path scope.
- A fresh-context handoff probe at that revision passed every takeover task
  with repository, historical Git, and GET-only package references. It found
  no material takeover gap and correctly retained the separate implementation,
  native, and publication authorization gates. No product or registry
  operation was performed.
- Contraction after the second review iteration and complete review found no
  further design changes necessary: scope remains one NuGet slice, current
  v3 authority is reused, and source-supported design remains distinct from
  native evidence. This final append records completed checks; protected
  delivery and merged-tree verification remain subsequent gates.
- PR #669's Copilot review found three stale takeover directions in the main
  handoff, NuGet handoff, and overview. An independent adjudicator classified
  each as a true positive. The correction routes agents to the existing design
  package, actual Git/PR delivery state, and separately authorized disabled
  implementation; it does not claim a pending merge is complete. Current LLD
  reading lists and navigation now include the applicable NuGet design while
  retaining the historical npm baseline's specificity.
- A new fresh-context probe of the corrected entries passed all takeover tasks
  with repository, Git, PR, and GET-only package evidence. It identified no
  material gap and correctly distinguished outstanding design-delivery gates
  from post-delivery implementation authorization. The correction's 155 local
  Markdown path/anchor checks passed. No product or native execution occurred.

## [2026-09-12] query | Prepare the NuGet native fixture contract

- A bounded recheck of three GitHub-owned raw documentation bodies at
  `078b5832caa5cde591c2babb389ef447a0ef66eb` did not close the service-owned
  atomic non-overwrite gate. Client support and restoration constraints do
  not establish competing-write behavior. This is an evidence-sufficiency
  conclusion for those sources, not proof of overwrite or universal absence
  of a guarantee. Exact sources, independent review, hashes, and scope limits
  are retained in the [Issue evidence carrier](https://github.com/hcoona/three/issues/676#issuecomment-5642567112).
- Microsoft's [pack target reference](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target)
  defines `PackageId`, `PackageVersion`, and `PackageDescription` as native pack
  inputs. The repository-pinned SDK source's
  [manifest serializer](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/nuget-client/src/NuGet.Core/NuGet.Packaging/PackageCreation/Xml/PackageMetadataXmlExtensions.cs#L40-L42)
  writes the supplied ID and `NuGetVersion.ToFullString()`. These are source
  findings supporting fixture construction through pack; they are not proof
  that a generated fixture, GitHub feed, or native scenario passes.
- The [LLD fixture contract](../hcoona-release-smoke-github-packages-lld.md#fixture-preparation-contract)
  selects original pack outputs, official coordinate-equivalence checks,
  separate bounded preflight and mutation requests, and independently audited
  evidence. Concrete coordinates, tool/profile identities, operation budgets,
  and actual pinned-toolchain results remain execution prerequisites. No
  native request, dispatch, publication, or administrative operation was
  performed by this source research.
