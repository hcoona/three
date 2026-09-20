# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Design the Admitted Repository Model Consumer Boundary

- **Work carrier:** [Issue #773](https://github.com/hcoona/three/issues/773).
- **Authorized outcome:** Produce one source-bound, independently reviewed
  design proposal for preserving admitted Repository Model state through
  same-revision application composition. Identify each validation responsibility
  and its owner before proposing any test or production change. Deliver the
  caller inventory, API proposal, scenario obligations and exact later
  implementation scope in the Issue; this grant authorizes no implementation.
- **Accepted basis:** Refresh the admitted-model finding in the independently reviewed
  [responsibility assessment](https://github.com/hcoona/three/pull/766#issuecomment-5745485171)
  against current source. Follow the current v3
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [HLD](../src/public/lib/three-workflow-delivery-v3/docs/high-level-design.md),
  [Repository Model MLD](../src/public/lib/three-workflow-delivery-v3/docs/repository-model-release-unit-mld.md),
  [CI MLD](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [Release MLD](../src/public/lib/three-workflow-delivery-v3/docs/release-delivery-mld.md),
  [npm design](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-npm-lld.md),
  [NuGet design](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-github-packages-lld.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [shared engineering principles](engineering/engineering-principles.md).
- **Bounded assessment:** Trace all direct constructors, admitters and consumers
  of `AdmittedRepositoryModelSnapshot`, starting from `repository/compiler.py`,
  `cli.py`, `release/eligibility.py` and `ci/` under
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/`.
  Include their actual tests and caller-specific readiness/context contracts.
  Distinguish structural/canonical consistency from external transport admission,
  current request/target/purpose/control/candidate/digest binding, freshness and
  effect authorization. Evaluate a narrow composed API against success,
  blocked/unready, wrong-context and invalid-wrapper scenarios. Trace discovered
  consumers only as needed to close that boundary; do not audit unrelated
  modules or invent a general admission framework.
- **Preserved obligations:** Current invalid-wrapper rejection, frozen/slotted
  representation, CI's identity-valid blocked-model path, same-revision
  composition, caller-specific context/freshness checks and separate trust/effect
  boundaries remain requirements. Constructor consistency is not authority over
  a new request; Python wrappers are not unforgeable capabilities. Do not infer
  that every repeated validation is redundant or introduce a broader in-process
  attacker model. Preserve external wire contracts and current support bounds.
- **Evidence and review:** Record exact accepted source identities and anchors,
  distinguish static reasoning from runtime observation, and explain why each
  proposed retained, moved or retired check belongs at its selected boundary.
  Name the smallest later code/test scope and its independent semantic oracles.
  Obtain independent domain/OCR and research-evidence review, independently
  triage material findings, and apply the handoff's contraction protocol.
  No test-count target, runtime equivalence, platform support or resource saving
  follows from this assessment. Accepted authorities cannot be amended by an
  Issue comment; a conflict or needed contract change must be reported for its
  applicable disposition before dependent implementation is proposed.
- **Scope and effects:** Read-only source/design work, task-owned evidence and
  ordinary GitHub Issue/PR delivery only. Preserve
  [agent execution guidance](engineering/agent-execution.md), #721 resource
  ownership and failed/interrupted evidence. No production, test, workflow,
  dependency, schema, resource-limit or accepted-authority edit; no collection,
  test execution, mutation experiment, native probe, dispatch/rerun, publication,
  administrative or authentication/access change, host cleanup or unrelated
  process termination. CI pure-rule consolidation, legacy acceptance retirement
  and the held #761–#764 removal schedule are excluded. Existing npm/NuGet
  gates and all spent-operation boundaries remain in force.
- **Completion:** Retain the reviewed responsibility/API proposal and evidence
  limits in the Issue. End this finite grant through reviewed Wave deletion or
  replacement after delivery. Any implementation requires its own accepted
  scope and applicable design/domain gates.
