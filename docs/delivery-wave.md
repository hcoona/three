# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Retire Legacy Acceptance Producers and Align Test Ownership

- **Work carrier:** [Issue #793](https://github.com/hcoona/three/issues/793).
- **Accepted inputs:** The independently reviewed
  [legacy acceptance assessment](https://github.com/hcoona/three/issues/789#issuecomment-5748587918),
  its [independent reviews](https://github.com/hcoona/three/issues/789#issuecomment-5748589591),
  the [owner's retirement disposition](https://github.com/hcoona/three/issues/789#issuecomment-5754927058),
  current v3 [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [HLD](../src/public/lib/three-workflow-delivery-v3/docs/high-level-design.md),
  [npm LLD](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-npm-lld.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [engineering principles](engineering/engineering-principles.md).
  Reconcile the assessment's source and consumer inventory with the accepted base.
- **Authorized outcome:** Remove the old executable fixed-coordinate acceptance
  CLI and Python producer surface and its exclusive tests. First decouple
  historical reader test inputs from the retiring producer types. Preserve
  literal digest expectations and corruption, closure, identity and generation
  rejection. Align remaining tests with their actual behavior owners; no removal
  quota or generic acceptance framework is selected.
- **Owner disposition:** Accept the compatibility change for the old
  `governance run-fixed-acceptance-probe` command, old adapter probe/suite
  functions, producer types and their exports, including unknown external
  callers. Do not alias current native acceptance behind the retired interface
  or create a new support promise. This retires an older tool inside v3;
  it changes neither the normative Workflow version nor implementation language.
- **Preserved boundaries:** Keep current native npm/NuGet acceptance and Release,
  shared origin/credential/profile/deadline/process/archive contracts, the
  historical Governance reader and reader CLI, original evidence and raw
  fixtures/provenance. Exact historical Git source remains available for old
  Adapter replay without new operation permission. Preserve interleaved and
  shared helpers and nonlegacy functions outside the selected symbol closure.
  Update only the necessary current-state documentation and routing.
- **Validation and effects:** Permit local production/test/document changes,
  bounded local tests, independent domain/records/evidence review and ordinary
  Issue/PR delivery. Validate retained reader, CLI, adapter, native-operator and
  Release behavior, the full affected v3 suite, applicable root HK, normal hooks
  and hosted gates. Preserve the [#721](https://github.com/hcoona/three/issues/721)
  task-resource lifecycle, failed/interrupted evidence and
  [agent execution guidance](engineering/agent-execution.md).
  No new native probe, dispatch/rerun, publication, credential/access/admin
  change, dependency/schema/native-workflow change, host cleanup or unrelated
  process termination is authorized. Spent-operation boundaries remain intact.
- **Completion:** End this finite grant by reviewed Wave deletion or replacement
  after delivery and verification. The held #761–#764 removals remain untouched.
  This work establishes no wider test-classification completion or unmeasured
  runtime/resource-saving claim.
