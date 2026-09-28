# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Execute one normal TestPyPI publication and audit

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the [owner-approved
  proposal](https://github.com/hcoona/three/issues/843#issuecomment-5861383218) and
  [approval](https://github.com/hcoona/three/issues/843#issuecomment-5861978161),
  reviewed ready Governance v2 delivered by PR #885 at
  `ebc97ca0551be0ce39e70f4da28ea7400bc7d33e`, and the current Python requirements,
  LLD and publication profile. The independently reviewed concrete protocol is
  [publication protocol](https://github.com/hcoona/three/issues/843#issuecomment-5862463902)
  (SHA-256 `bf6624a3843ed8b853de54d9c084cf2eac2356ddf1423b333df63973d31d6360`); its
  original bytes and digest are retained there.
- **Advancement and outcome:** execute at most one new normal TestPyPI Attempt for
  `hcoona-release-smoke-python` version 0.1.0b19, wheel
  `hcoona_release_smoke_python-0.1.0b19-py3-none-any.whl` and sdist
  `hcoona_release_smoke_python-0.1.0b19.tar.gz`. Independently review current-run
  original qualified artifacts, Snapshot and Bundle before one Environment approval;
  audit terminal lineage, actual destination downloads and separate clean
  wheel/sdist consumers. Retain the truthful success, exact-satisfied, failure or
  incomplete result, then close this grant.
- **Revision and prerequisites:** target and same-revision tooling are the protected
  merge of this Wave-only change atop `ebc97ca0551be0ce39e70f4da28ea7400bc7d33e`,
  with the unchanged accepted runtime/profile/Governance. Before dispatch bind that
  actual SHA and reviewed/merged tree in the Issue, require applicable post-merge
  CI, confirm unchanged full-history NBGV public projection 0.1.0b19 and fresh ready
  admission. Any intervening main change, mismatch, failed prerequisite or expired
  evidence stops dependent execution without replacement.
- **Effects and bounds:** permit exactly one owner/manual attempt 1 dispatch of
  `workflow-delivery-v3-python-smoke.yml` on `main` with `registry=testpypi`; at
  most one approval for Environment `22765954016` after independent original-byte
  review; one GitHub OIDC assertion and one TestPyPI token exchange; at most one
  wheel POST then one sdist POST with required exact readback. Runtime plus
  independent audit has at most 15 index GETs and 6 file GETs, retaining the profile's
  post-upload visibility, byte, origin, timing and authority bounds. Bound GitHub
  control/transfer and operator evidence requests by the reviewed protocol. Permit
  locked dependency preparation, isolated local consumers, ordinary CI, independent
  engineering/domain/record/research-evidence/OCR reviews and protected Issue/PR
  evidence delivery.
- **Risk and stopping:** preserve sole-writer/owner and retained three-publisher
  configuration unless contradicted; trust platform file non-replacement without
  duplicate/race probes. No atomic pair or indefinite retention is promised. Exact
  existing pair uses the zero-action path without approval/token/upload. Rejection,
  partial upload, ambiguous mutation or incomplete evidence remains
  failed/incomplete and consumes its reached slots; no automatic resend or
  replacement Attempt. Do not promote old failed evidence or infer creation from
  HTTP 200 alone.
- **Exclusions:** no PyPI or Ruby operation, native/bootstrap generation,
  duplicate/race probe, registry preflight outside the named flow,
  rerun/retry/refill, cleanup/deletion/rollback, version rewrite,
  access/publisher/Environment change, credential export or administrative
  credential in Actions. PyPI remains blocked and native/bootstrap slots remain
  null.

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
