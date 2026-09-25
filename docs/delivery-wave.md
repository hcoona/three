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

### Prepare Python destination-native acceptance

- **Issue:** [#843](https://github.com/hcoona/three/issues/843).
- **Accepted inputs:** the owner's approved
  [preparation proposal](https://github.com/hcoona/three/issues/843#issuecomment-5824278492);
  accepted Python requirements, design and dated source evidence under
  `src/public/lib/three-workflow-delivery-v3/docs/`; and the protected disabled
  implementation delivered through #849 and its closeout #850.
- **Advancement and outcome:** define and protected-deliver the bounded Python
  native-acceptance protocol, missing fixture/probe/capture/audit tools and
  hosted entry, reusing the actual Python transport and clean consumers.
  Cover two-format creation, identical/different-byte rejection, finite
  competing creation with either winner, preserved winner bytes and complete
  before/after inventory. Before executor implementation, close the fixed
  schedule, finite mutation/read/token/dispatch/concurrency budgets, valid
  fixtures, exact filenames, partial/ambiguity stops and evidence retention.
  Deliver a concrete TestPyPI operator request template and readiness report;
  PyPI requires separate qualification. Actual coordinates, ownership and
  configuration close in later operational requests, and disposable tuples
  cannot silently admit the smoke tuple.
- **Effects and delivery:** permit local design/runtime/workflow/test changes,
  dependency preparation, credential-free builds and isolated consumers,
  public-source rechecks, ordinary Issue/PR/check operations and CI, independent
  delegated review, protected delivery and grant closeout. Retain domain and
  security, record, research-evidence and full-manifest open-code-review-delegate
  review, independent finding triage, contraction, HK/hooks and postmerge checks.
  Recheck mutable interfaces before relying on a changed or newly selected
  contract. Ordinary CI validates local behavior only.
- **Exclusions:** keep TestPyPI and PyPI `live_enabled: false`. No account,
  project or Environment provisioning, publisher registration, ownership,
  access or authentication changes, OIDC assertion/token requests, native
  registry reads/probes, native/manual release dispatch or rerun, uploads,
  activation, recovery/deletion or remote cleanup. Actual configuration and
  native execution need separate concrete bounded authorization; each normal
  publication also needs its own grant and current-Attempt Approval. No
  cross-destination artifact, Approval or evidence promotion, inferred private
  account access, reopened npm/NuGet operations or Ruby implementation.
