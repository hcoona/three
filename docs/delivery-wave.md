# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Establish normal TestPyPI configuration and admission

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the owner's approved
  [TestPyPI proposal](https://github.com/hcoona/three/issues/843#issuecomment-5861383218),
  the disabled Governance v2 implementation delivered by PR #880, and the
  current Python requirements, design and source evidence under
  `src/public/lib/three-workflow-delivery-v3/docs/`.
- **Advancement and outcome:** register the normal-workflow publisher on the
  existing TestPyPI project; inspect and independently review configuration;
  protected-deliver TestPyPI-only ready Governance v2. Prepare and review the
  concrete subsequent publication Wave under the same owner-approved scope,
  preserving its separate accepted grant before execution. Carry forward the
  confirmed Sole Owner and existing bootstrap/native registrations unless
  changed or contradicted. The owner adds only `hcoona/three`, workflow
  `workflow-delivery-v3-python-smoke.yml`, Environment
  `workflow-delivery-v3-python-testpypi` on `hcoona-release-smoke-python`.
- **Effects and delivery:** permit the named owner-side registration, bounded
  GitHub configuration reads and TestPyPI project-registration attestation,
  independently reviewed TestPyPI-only protected admission, locked dependencies
  and isolated local validation, engineering/domain/record/research-evidence/OCR
  reviews, ordinary CI and protected Issue/PR delivery. Bind actual configuration,
  source-evidence revision and finite freshness before admission. Trust platform
  non-replacement without duplicate/race proving; ready admission does not
  establish publication or clean-consumer completion.
- **Risk and stopping:** unexpected configuration drift or a failed prerequisite
  stops dependent work without implicit repair. Preserve prior failed evidence
  and existing service registrations. The later concrete publication Wave must
  bind the actual target/tooling revision, unchanged NBGV version, finite
  requests/budgets and stop conditions before its one normal Attempt.
- **Exclusions:** no distribution-state/file reads, OIDC, dispatch, Environment
  approval, uploads, new native generation or duplicate/race probes, retry/refill,
  deletion/cleanup, other publisher/access/Environment changes, credential export,
  production PyPI or Ruby operations, or promotion of prior failures. PyPI stays
  blocked; native/bootstrap request slots stay null. This entry does not execute
  the separately owner-approved publication stage.

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
