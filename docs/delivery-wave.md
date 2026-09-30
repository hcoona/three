# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Make V3 NuGet frozen builds reproducible

- **Issue:** [#948](https://github.com/hcoona/three/issues/948).
- **Accepted inputs:** the owner's approval to implement the demonstrated
  reproducible-build mechanism, upgrade the .NET SDK and align its consumers;
  current V3 NuGet requirements, architecture, recovery and operation-profile
  contracts; and the independently reviewed
  [Linux and Windows feasibility evidence](https://github.com/hcoona/three/issues/948#issuecomment-5918963584).
- **Advancement and outcome:** accept the focused requirement, design and
  validation alignment, then implement original unsigned NuGet archive
  reproducibility for the managed smoke's frozen build. Upgrade the repository
  SDK to the verified 10.0.401 distribution while retaining net10.0; align
  helper, MSBuild, runtime, tool projections, locks and affected toolchain or
  profile admission. Apply stable source/intermediate path mapping and a
  target-derived deterministic package timestamp. Validate the actual
  production provider/adapter, unchanged frozen version and witness semantics,
  and fresh-Attempt exact-byte recovery; complete affected regressions and
  Windows original-archive acceptance with independent review. Retain evidence
  and close the Issue, then remove this completed entry through protected
  delivery.
- **Effects and delivery:** permit local isolated SDK/dependency preparation,
  builds and tests, ordinary Windows CI and build-only acceptance, required
  record and configuration alignment, ordinary GitHub Issue/PR/check/comment
  operations, protected delivery and independent delegated review. Monitor CI
  and review through acceptance; use open-code-review-delegate before each
  merge. Retain original packages, binlogs, exact tool identities, paired-build
  comparisons and source/timestamp negative controls. Additional hosted
  experiments require a bounded reviewed protocol; spent experiment runs are
  not reusable grants. Preserve isolated writable test state and inode bounds.
- **Exclusions:** no package publication, registry probes or native destination
  acceptance, release dispatch, signing, credentials/access changes, revival of
  completed smoke operations, separate symbol package, target-framework upgrade,
  general cross-OS equality promise or custom archive rewriting. Preserve
  current exact-readback, failed-Attempt, approval and independent-review gates;
  do not relabel historical artifacts or installed native evidence as proof of
  the new toolchain.
