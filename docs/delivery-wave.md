# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Restore dependency updates and simplify V3 tooling ownership

- **Issue:** [#823](https://github.com/hcoona/three/issues/823).
- **Accepted inputs:** the owner's approved dependency-repair scope; current
  repository governance, V3 requirements and engineering principles; and the
  diagnosed failures in Renovate PRs #650, #649, #647, #646, #626, #605, #604
  and #595.
- **Advancement and outcome:** first deliver the revised V3 tooling dependency
  contract, then implement and validate scoped Renovate post-update tasks,
  package-manager-owned lockfile formatting, complete mise lock/projection
  updates, managed V3 initialization without duplicate dependency admission,
  and HtmlAgilityPack caller compatibility. Preserve native locked installation
  and V3-owned source, policy, approval and publication artifact relationships.
  Record normal local commit and CI timings and refresh the eight named
  Renovate candidates against the repaired main branch.
- **Effects and delivery:** permit local dependency preparation, builds and
  tests, ordinary GitHub Issue/PR/check/comment operations, protected delivery
  and ordinary CI, independent delegated review, and non-force candidate
  refresh through Renovate or GitHub. Monitor CI and comments through delivery;
  run open-code-review-delegate before each merge. Preserve inode cleanup and
  isolated writable test state.
- **Exclusions:** no replacement dependency-admission manifest or per-package
  runtime verification system; no unrelated dependency upgrades, package
  publication, native acceptance, release dispatch, credentials/access changes,
  or reactivation of spent smoke operations. Existing publication-profile
  requirements and independent review obligations remain in force.
