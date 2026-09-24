# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Define the Python V3 smoke scope and destination

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the owner's request to select Python as the next V3
  smoke, followed by Ruby; current repository governance, V3 requirements,
  design and engineering principles; the completed npm and NuGet boundaries;
  and the proposed minimal-package journey in #843.
- **Advancement and outcome:** research and confirm one dependency-free Python
  smoke package's scope, channel, version projection, artifact set and
  publication destination, evaluating TestPyPI first. Deliver confirmed
  requirements, applicable HLD/MLD changes, a brief LLD and validation basis
  for target-bound NBGV versioning, wheel/sdist builds, clean installed
  consumption and eventual actual publication verification. Resolve frozen
  PEP 440 version consumption, source builds without Git metadata, per-file
  publication and partial success, destination retention and atomic
  non-overwrite requirements, and publisher trust before design acceptance.
  Record unsupported capabilities and any owner decisions needed; keep Live
  blocked where required guarantees cannot be established. This outcome is
  design readiness, not completion of the end-to-end smoke.
- **Effects and delivery:** permit public-source research, read-only public
  platform inspection, documentation validation, ordinary Issue/PR delivery
  and CI, and independent delegated domain, record and evidence review. Recheck
  mutable destination facts at confirmation and before later implementation
  authorization. Preserve requirements confirmation, design ordering and
  existing implementation, native-operation and publication gates.
- **Exclusions:** no package/runtime/workflow implementation, package builds,
  hosted experiments, native acceptance, release dispatch, publication,
  provisioning, authentication/access changes or cleanup. No production PyPI
  commitment, promotion of Buddy evidence, inherited destination exceptions,
  or reopening completed npm/NuGet smoke operations. Implementation requires
  a later accepted Wave after design acceptance. Ruby remains subsequent
  scope: after Python's independent completion audit, the owner evaluates a
  separate Ruby proposal at the Wave review closing Python; this entry grants
  no Ruby work.

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
