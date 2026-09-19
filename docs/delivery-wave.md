# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Analyze Workflow Delivery v3 Test Responsibilities

- **Work carrier:** [Issue #732](https://github.com/hcoona/three/issues/732)
  and its delivery and review carriers, following
  [#712](https://github.com/hcoona/three/issues/712).
- **Authorized outcome:** Analyze the entire v3 test suite and deliver an
  independently reviewed, prioritized keep/consolidate/remove plan. Establish
  an exact-source inventory of collected cases, test functions and parameter
  combinations; derive necessary scenario, core/contract and integration
  responsibilities from accepted requirements and fault models. Inspect the
  largest contributors across project concerns, including local release and
  acceptance test code. Identify preserved invariants and remaining owners for
  proposed reductions, and select the next bounded cleanup slice. Approximate
  reduction expectations are diagnostic signals, not quotas or acceptance gates.
- **Prerequisites and governing inputs:** Use the accepted #712/#720 results
  and #721 temporary-resource correction. Follow the current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  relevant project design and validation authorities, and
  [shared testing principles](engineering/engineering-principles.md#testing).
  Refresh the inventory and conclusions if their accepted source changes.
- **Method and evidence:** Source and retained-evidence inspection, ordinary
  pytest collection without executing test bodies, and temporary local analysis
  scripts outside the repository are permitted. Separate inventory coverage
  from substantive review, and confirmed candidates from conditional estimates
  and unreviewed remainder. Do not double-count overlapping candidates or claim
  savings from moving parameter rows into loops. Retain results in existing
  Issue/PR carriers rather than adding a reporting framework or record family.
- **Concurrency and review:** Follow the
  [agent-execution guidance](engineering/agent-execution.md). Independent
  reviews may inspect separate concerns concurrently, followed by combined
  domain/evidence review. Preserve independent disposition of material findings
  and refresh dependent conclusions after corrections.
- **Scope and effects:** Normal Git/Issue/PR delivery and applicable checks are
  permitted. Analysis leaves production, tests, fixtures, workflows, HK and
  resource limits unchanged. Preserve the accepted temporary-resource lifecycle
  and failed/interrupted evidence. Test implementation requires a subsequent
  accepted bounded Wave grant. Other v3 mainline development remains paused.
  No native service probes, workflow dispatch, publication, administrative,
  authentication/access changes, process termination or host-wide cleanup are
  included. Completed npm/NuGet proving and spent-operation boundaries remain
  in force.
- **Completion boundary:** After the analysis, independent finding dispositions
  and whole-report review are accepted, delete or replace this entry through a
  reviewed Wave change. Completion alone grants no implementation or native work.
