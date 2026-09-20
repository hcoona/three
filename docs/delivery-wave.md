# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Implement CI Rule and Test Ownership

- **Work carrier:** [Issue #784](https://github.com/hcoona/three/issues/784).
- **Accepted inputs:** The independently reviewed
  [CI rule and test ownership design](https://github.com/hcoona/three/issues/782#issuecomment-5747838623),
  current v3
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [CI qualification design](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [Shared Foundation ownership](../src/public/lib/three-workflow-delivery-v3/docs/shared-foundation-mld.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [engineering principles](engineering/engineering-principles.md).
  Reconcile the reviewed source and consumer identities against accepted main
  before implementation.
- **Authorized outcome:** Give the design's common CI derivations one CI-owned
  implementation and allocate their tests to core rules, admission contracts
  and composed scenarios. Correct responsibility governs the result; there is
  no case-reduction target.
- **Implementation boundary:** Under
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/`,
  add `ci/rules.py` and update only `ci/planner.py`, `ci/evidence.py`,
  `ci/finalizer.py` and `records/ci.py`. Share fixed lane/request, outcome,
  terminal/explanation/failure-action and supersession-reason derivations.
  Keep model-driven path impact, transported supported-scope admission,
  platform acquisition and ordinary-PR SLO responsibilities distinct.
- **Test boundary:** In the same project's `tests/`, add `ci/test_rules.py`
  and update only `ci/test_evidence.py`, `ci/test_finalizer.py` and
  `contracts/test_ci_contracts.py` as specified by the reviewed design.
  Move rule matrices, retain required composition and extend bounded external
  contradiction checks. Preserve all CI fixture bytes and literal digests,
  independent expected values and other test consumers. Record actual collected
  case dispositions; producer/validator agreement is not an oracle.
- **Preserved contracts:** Keep public imports and signatures, constants,
  serialized bytes/digests, outcomes, diagnostics and non-authoritative
  coexistence. Preserve current-context binding, independent external
  admission, ready/blocked behavior, partial-Plan and contradictory-Decision
  rejection and supersession. Reassess scope before any additional caller or
  authority edit.
- **Validation and review:** Complete affected and full package validation,
  CLI/package/workflow/HK consumer checks, root HK and hooks before independent
  domain/OCR and evidence review. Independently adjudicate material findings,
  contract the result and retain ordinary PR/CI/merge evidence. Preserve #721
  task-owned temporary resources, failed/interrupted evidence and
  [agent execution guidance](engineering/agent-execution.md). Static design
  establishes no runtime, performance or inode saving.
- **Effects and completion:** Permit bounded local implementation/validation
  and ordinary Issue/PR delivery. No held #761–#764 removals, broader v3 mainline
  work, product-authority/schema/catalog/dependency/workflow edits, native
  probes, dispatch/rerun, publication, administrative or access changes, host
  cleanup or unrelated process termination are granted. Preserve native and
  publication gates and spent-operation boundaries. End this grant after the
  reviewed implementation is delivered through a reviewed Wave deletion or
  replacement.
