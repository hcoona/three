# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Make the NuGet Consumer Deadline Test Deterministic

- **Work carrier:** [Issue #786](https://github.com/hcoona/three/issues/786).
- **Accepted inputs:** The v3 NuGet
  [consumer contract](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-github-packages-lld.md#evidence-admission-and-completion),
  [companion README](../src/private/app/workflow-delivery-v3-nuget-consumer/README.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [engineering principles](engineering/engineering-principles.md), with the
  [independently diagnosed and triaged timing defect](https://github.com/hcoona/three/issues/786#issuecomment-5747940822).
  Reconcile the bound source identities against accepted main before work.
- **Authorized outcome and boundary:** Make the existing shared-deadline and
  terminal-stop/no-retry test deterministic. Change only
  `src/private/app/workflow-delivery-v3-nuget-consumer/BoundedHttpHandler.cs`
  and `tests/private/app/workflow-delivery-v3-nuget-consumer/BoundedHttpTests.cs`.
  Use the platform time-provider boundary at handler construction, preserving
  existing callers and system-time defaults; keep any small timer substitute
  local to that test file. Establish storage entry before advancing the original
  remaining deadline. Preserve exact send/evidence accounting, cancellation and
  no-package-success assertions, the real-timer wiring case and other tests.
  Watchdog or caller cancellation cannot establish deadline correctness.
- **Preserved contracts:** Keep one original cumulative deadline, request/byte
  bounds, terminal stopping, credential/redirect restrictions, serialized
  contracts and diagnostics. No new dependency or general timing framework,
  assertion relaxation, timeout inflation, retry or skipped case is selected.
- **Validation and review:** Validate the affected consumer suite and applicable
  root HK/hooks before independent domain/OCR and evidence review. Show that a
  reset storage deadline or a send after terminal stop fails the expectations.
  Independently adjudicate findings and return the correction to its originating
  reviewer. Preserve the original hosted failure and #721 task-owned resources
  and failed/interrupted evidence. Local tests establish no native feed or
  publication claim; ordinary required hosted checks remain effective.
- **Effects and completion:** Permit bounded local implementation/validation
  and ordinary Issue/PR delivery only. Preserve all native/publication and spent
  operation gates. No dispatch/rerun, publication, workflow/check/access changes,
  administrative actions, host cleanup or unrelated process termination.
  Deliver this correction before the CI rule implementation below, then end its
  finite grant through a reviewed Wave deletion or replacement.

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
