# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Implement Reviewed Workflow Delivery v3 Test Reductions

- **Work carrier:** [Issue #735](https://github.com/hcoona/three/issues/735)
  and its delivery and review carriers.
- **Authorized outcome:** Implement the finite set of 22 case-reduction
  recommendations and two count-neutral setup changes in the independently
  reviewed [#732 plan](https://github.com/hcoona/three/issues/732#issuecomment-5740359945),
  with its [dispositions and review](https://github.com/hcoona/three/issues/732#issuecomment-5740360883).
  Assign coverage to sufficient core/contract, scenario/composition and real
  integration owners. Begin with GC-01, GC-03 through GC-08, AA-02 and AA-03,
  then implement the remaining named recommendations in reviewable groups.
  Proposed counts are planning evidence, not quotas or acceptance gates.
- **Prerequisites and governing inputs:** Follow the current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  relevant design and validation authorities, and
  [shared testing principles](engineering/engineering-principles.md#testing).
  Preserve the accepted #721 temporary-resource lifecycle and the #732 plan's
  named fault models, retained cases and assertion-transfer conditions.
  Recheck candidate source and surviving owners before editing; refresh
  dependent analysis and independent disposition when evidence contradicts a
  recommendation or a relied-on prerequisite changes.
- **Validation and review:** Reconcile actual removed and retained collected
  nodes without overlapping savings or hiding parameter rows in loops. Run
  complete affected modules and their named surviving owners, applicable HK,
  normal hooks and hosted checks. Use task-owned temporary roots and bounded
  executions; retain failed/interrupted evidence. Keep proposed, realized and
  measured resource savings distinct. Follow the
  [agent-execution guidance](engineering/agent-execution.md); independent
  domain/evidence reviewers assess each group and the combined responsibility
  boundaries, with independent disposition of material findings.
- **Scope and effects:** Change only tests and directly related fixture
  branches needed for the named recommendations. Normal local testing and
  Git/Issue/PR delivery are permitted. Preserve production behavior, contracts,
  actual historical profiles and native evidence. Conditional NBGV or shared
  secrecy redesigns, the inventory-only remainder, other v3 development,
  workflow/HK or resource-limit changes, native service probes, dispatch,
  publication, administrative or authentication/access changes, process
  termination and host-wide cleanup are excluded. Existing npm/NuGet domain
  gates and spent-operation boundaries remain in force.
- **Completion boundary:** After the implemented recommendations, surviving
  coverage and combined independent review are accepted, delete or replace
  this entry through a reviewed Wave change. Retain outcomes in the Issue and
  delivery carriers; completion grants no further implementation or native work.
