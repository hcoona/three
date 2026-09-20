# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Design CI Rule and Test Ownership

- **Work carrier:** [Issue #782](https://github.com/hcoona/three/issues/782).
- **Accepted inputs:** Current v3
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [CI qualification design](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [Shared Foundation ownership](../src/public/lib/three-workflow-delivery-v3/docs/shared-foundation-mld.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [engineering principles](engineering/engineering-principles.md), with the
  independently reviewed CI derivation assessment linked from the Issue. Reconcile
  its source identities against current accepted code before relying on it.
- **Authorized outcome:** A source-bound, independently reviewed design assigning
  the implicated CI derivations and their tests to their actual owners, with an
  exact bounded implementation proposal if a shared boundary is justified.
  Correct abstraction and classification govern the result; there is no case
  reduction target.
- **Read-only design scope:** Trace the rules in
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/ci/planner.py`,
  `ci/evidence.py`, `ci/finalizer.py` and `records/ci.py` under that same package,
  including their complete direct caller, import and test-consumer closure.
  Distinguish pure lane/request, normalized-outcome, terminal/explanation and
  supersession policy from schema/canonicalization, context admission and
  orchestration. Design the smallest CI-owned shared boundary only where its
  concrete consumers justify it; avoid circular dependencies or a general
  policy framework.
- **Preserved contracts:** Keep independent admission of transported input,
  partial-Plan and contradictory-Decision rejection, current-context binding,
  ready/blocked model behavior and supersession. Preserve public entrypoints,
  serialized bytes/digests, outcomes and diagnostics. Assign core rule matrices,
  admission contracts and composed scenarios explicitly; preserve independent
  literal expected values and canonical goldens. Producer/validator agreement
  alone is not an oracle. Identify separately any required authority amendment.
- **Evidence and review:** Retain the responsibility map, exact source and
  consumer identities, proposed source/test paths, case dispositions, migration
  order and validation closure in the Issue's review carrier. Show the permitted
  refactors and credible contract violations the proposed tests must distinguish.
  Obtain independent domain/OCR and evidence review, independently adjudicate
  material findings and apply contraction. Preserve #721 resource ownership,
  failed/interrupted evidence and
  [agent execution guidance](engineering/agent-execution.md). Static design
  evidence establishes no runtime, performance or inode saving.
- **Effects and completion:** Permit task-owned read-only source/design work and
  ordinary Issue/PR delivery. No v3 production/test/workflow/dependency/schema
  or product-authority edits, project imports, test collection/execution, held
  #761–#764 removals, native probes, dispatch/rerun, publication, administrative
  or access changes, host cleanup or unrelated process termination are granted.
  Preserve current native/publication gates and spent-operation boundaries.
  Complete the reviewed design and implementation recommendation, then end this
  grant by reviewed Wave deletion or replacement. Implementation requires a
  separately accepted bounded grant.
