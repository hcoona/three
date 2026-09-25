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

### Reduce CI feedback time and superseded work

- **Issue:** [#837](https://github.com/hcoona/three/issues/837).
- **Accepted inputs:** the owner's approval to investigate and fix the CI
  execution and process issues identified from #823; current engineering
  principles, general CI selection, V3 test responsibilities and retained
  timing evidence.
- **Advancement and outcome:** diagnose and fix cancellation of superseded
  ordinary CI, scope Python test preparation to its selected consumers, and
  investigate the measured V3 .NET adapter and Node provider hotspots before
  implementing justified scenario-layer or immutable-preparation improvements.
  Evaluate root-tool trigger granularity and dependent-candidate sequencing;
  retain simple conservative behavior where finer mechanisms lack benefit.
  Record confirmed fixes, bounded evidence and justified no-change decisions.
- **Effects and delivery:** permit public-source research, read-only CI and
  review inspection, local dependency preparation and isolated tests, ordinary
  CI and protected Issue/PR delivery, and independent delegated review. Record
  queue, initialization, execution, commit time and inode observations; monitor
  CI and comments through delivery and use open-code-review-delegate before
  each merge. Preserve native dependency ownership, complete required-check
  results, isolated writable test state and cleanup.
- **Exclusions:** no publication, native acceptance, release dispatch,
  credentials/access changes, weakened gates or independent review, broad new
  test matrix, arbitrary test-count target, duplicate dependency admission or
  result reuse across incompatible revisions. HK 2 compatibility remains a
  separate concern under #647/#823. New hosted experiments require a bounded
  protocol before execution; this entry grants no new native operation.

### Verify TestPyPI operational readiness

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the owner's instruction to continue after preparation
  closeout #856; accepted Python requirements, native protocol and readiness
  interface; protected protocol/tooling through #854/#855 and their verified
  delivery evidence.
- **Advancement and outcome:** establish the actual remaining TestPyPI resource
  gaps and prepare a concrete, reviewable configuration/native-operation
  request for `hcoona-release-smoke-python`. Inspect only the selected
  `hcoona/three` repository, protected main and
  `workflow-delivery-v3-python-testpypi` Environment; reconcile owner-supplied
  TestPyPI account/project-control and publisher facts with the protocol.
  Recheck applicable official setup/ownership/bootstrap documentation. If the
  project is absent or control is unproved, report the prerequisite and a
  bounded proposed creation path rather than interpreting a pending publisher
  as established ownership. Deliver sanitized readiness evidence and the
  exact next proposal in #843, then close this grant.
- **Effects and delivery:** permit ordinary Issue/PR/check operations, local
  documentation and validation, protected delivery/closeout, and independent
  domain/security, record, research-evidence and full-manifest OCR review with
  separate finding triage. After this entry merges, permit read-only GitHub
  inspection of the named repository's identity, writer inventories, branch
  protection/rules and exact Environment configuration/sentinel, plus one
  public JSON Simple Index GET for the exact TestPyPI project. Read no secret
  values or unrelated accounts/projects. Bound GitHub inspection to 32 GETs,
  at most two 100-entry pages per inventory, with no request retries. Retain
  complete inventories within those bounds and original sanitized responses;
  do not poll or silently interpret failed,
  truncated or inaccessible reads as absence. The index read establishes
  current public visibility only, not ownership or historical availability.
  Use owner-supplied account facts without requesting credentials or inferring
  private browser/account access. Recheck mutable interfaces before changed
  reliance; record remaining uncertainty explicitly.
- **Exclusions:** both Python destinations remain disabled and native request
  slots remain null. No account/project/Environment creation or modification,
  publisher registration, access/authentication change, OIDC/token request,
  file download, native-suite or release dispatch/rerun, upload, activation,
  recovery/deletion or cleanup. Configuration, any initial project creation,
  native execution and each normal publication require their own concrete
  owner authorization and domain gates. No PyPI inspection/operation, artifact
  or approval promotion, reopened npm/NuGet operation or Ruby work.
