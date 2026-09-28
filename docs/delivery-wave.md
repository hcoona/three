# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Complete Python partial-publication recovery

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the owner's [direction to connect reproducible rebuilding
  with partial-publication recovery](https://github.com/hcoona/three/issues/843#issuecomment-5876826664),
  current Python requirements/design, locked reproducible-build evidence and
  the completed TestPyPI normal-publication audit.
- **Advancement and outcome:** first revise and independently review the Python
  requirements, HLD/MLDs and LLD for a fresh Attempt that rebuilds and qualifies
  the complete pair, verifies existing files against those exact bytes, uploads
  only missing files and verifies the final complete set. Protected-deliver the
  design before implementing its contracts, workflow, replay and failure
  handling. Validate and independently review the complete recovery flow,
  protected-deliver it and retain evidence before closing this grant.
- **Recovery boundary:** the owner accepts completing an exact existing subset
  in a new independently qualified Attempt. Preserve current-Attempt Approval,
  authorization and evidence; deterministic rebuilding does not promote old
  artifacts or Approval. Preserve failed prior Attempts. Conflicting, extra,
  yanked or unknown state blocks; failed or ambiguous uploads stop the current
  Attempt. Do not infer same-Attempt retries or unlimited automatic recovery.
- **Effects and delivery:** permit ordinary Issue/PR/check/comment operations,
  local locked dependency preparation, native-tool and controlled-transport
  tests, ordinary credential-free CI, independent domain/record/research/OCR
  review and protected delivery. Evaluate applicable mutable-source rechecks.
  Routine fixes, testing, review and delivery inside this outcome require no
  repeated owner approval.
- **Exclusions:** no registry observation, upload, OIDC, release dispatch,
  configuration/publisher/Environment or host-trust change, deletion, rollback,
  Governance renewal, duplicate/race probe or old-failure reclassification.
  The completed TestPyPI campaign stays stopped; production PyPI and Ruby
  remain outside this grant. A real hosted recovery experiment requires its
  own concrete bounded protocol and operation authorization.

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
