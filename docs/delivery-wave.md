# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Analyze Remaining Workflow Delivery v3 Test Responsibilities

- **Work carrier:** [Issue #738](https://github.com/hcoona/three/issues/738) and its delivery and review carriers.
- **Authorized outcome:** Substantively assess the test functions and cases
  left inventory-only by the independently reviewed
  [#732 report](https://github.com/hcoona/three/issues/732#issuecomment-5740359945), reconciled
  against accepted current source after
  [#735](https://github.com/hcoona/three/issues/735) and
  [#737](https://github.com/hcoona/three/pull/737). Produce a reviewed, prioritized
  keep/consolidate/remove plan with exact candidate identities, credible fault
  models, sufficient surviving owners and explicit residual uncertainty.
  Approximate count reductions are diagnostic evidence, never quotas.
- **Prerequisites and governing inputs:** Follow the current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  relevant design and validation authorities, and
  [shared testing principles](engineering/engineering-principles.md#testing).
  Use #732's assessed-function manifest and accepted #735 implementation to
  distinguish prior assessment from newly inspected scope. Refresh dependent
  evidence when current source or a relied-on authority changes.
- **Validation and review:** Reconcile exact collected-node and function
  inventories without double counting. Inspect ordinary scenarios and CLI
  orchestration as well as matrices; establish layers from assertions and
  dependency responsibilities. Name assertion transfers, shared-rule owners
  and required consumer/integration coverage for every candidate. Independently
  review each concern and the combined plan, and independently dispose material
  findings. State substantive versus inventory-only coverage and residual
  assumptions; do not extrapolate to unassessed cases or book conditional gains.
  Follow the [agent-execution guidance](engineering/agent-execution.md).
- **Scope and effects:** Permit source and retained-evidence inspection,
  ordinary pytest collection under the accepted #721 lifecycle, task-owned
  temporary roots, external local analysis scripts and normal Git/Issue/PR
  delivery. Preserve failed/interrupted evidence. Do not execute test bodies
  solely for inventory. Production, tests, fixtures, workflows/HK, resource
  limits and historical/native evidence remain unchanged. Conditional NBGV
  and shared-secrecy redesigns from #732, other v3 development, native probes,
  dispatch/rerun, publication, administrative or authentication/access changes,
  process termination and host-wide cleanup are excluded. Existing npm/NuGet
  gates and spent-operation boundaries remain in force.
- **Completion boundary:** Retain the reviewed plan, scope and next finite
  implementation proposal in the Issue and delivery carriers, then delete or
  replace this entry through a reviewed Wave change. A partial assessment must
  identify its remainder. Analysis completion grants no implementation or native
  work; those require their own accepted bounded authorization.
