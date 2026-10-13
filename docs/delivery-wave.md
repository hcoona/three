# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Remove excess Workflow Delivery CI execution and validation cost

- **Work carrier:** the pull request proposing this entry and its linked
  implementation PRs. The owner requested removal of unnecessary work under the
  [engineering principles](engineering/engineering-principles.md).
- **Accepted inputs:** the current Workflow Delivery
  [requirements](../src/private/app/workflow-delivery/docs/requirements.md),
  [HLD](../src/private/app/workflow-delivery/docs/high-level-design.md),
  [MLD](../src/private/app/workflow-delivery/docs/middle-level-design.md),
  [integration contracts](../src/private/app/workflow-delivery/docs/migration-strategy.md)
  and [handoff](../src/private/app/workflow-delivery/docs/agent-handoff.md).
  Completed ecosystem evidence retains its original scope and spent limits.
- **Advancement and outcome:** reconcile affected execution and validation
  responsibilities, then remove duplicate CI scenario execution, unnecessarily
  repeated native fixture preparation and tests that only preserve retired or
  incidental implementation structure. Retain necessary business, impact,
  contract, native integration and failure coverage at its least costly sufficient
  level. Replace avoidable per-project .NET process orchestration with supported
  native build scheduling where its selected-project, variant and result contracts
  are established. Reduce avoidable endpoint and planning preparation without
  duplicating native dependency/version interpretation or broadening failed
  selection. Update affected callers and current records atomically.
- **Ordering and gates:** independently classify material findings before they
  drive repairs. Review affected architecture, check ownership and validation
  contracts before implementation. Preserve meaningful impact selection, complete
  required checks and supported variants, exact candidate/control bindings and
  truthful failed, missing, cancelled and unknown results. A check may move to
  another native job or test layer only when its triggering and result collection
  still establish the required coverage. New feasibility or product tradeoffs
  return to the owner; do not simulate missing lower-layer guarantees.
- **Acceptance:** verify the retained behavior and the work actually removed.
  Compare execution scope, preparation, command and scenario costs with their
  source, environment and evidence limits. Measure ordinary and genuinely broad
  changes separately under WD-NFR-005; a single passing run, larger timeout,
  lower test count or blanket full run is not performance acceptance. Preserve
  residual uncertainty rather than claiming an unmeasured latency improvement.
  Delete this entry when the bounded implementation, validation and retirement
  outcome is accepted.
- **Effects:** permit source and repository/GitHub inspection, documentation and
  implementation changes, dependency/tool preparation, isolated local builds and
  credential-free tests, independent delegated review, ordinary PR/check
  operations and their normal CI. Native experiments still require an accepted
  concrete protocol; this entry does not revive a spent operation allowance.
  Repository delivery follows the existing protected merge process.
- **Exclusions:** no new ecosystem or production-product migration; no release
  or proving dispatch, manual replay of completed campaigns, publication,
  credential acquisition, Environment/account/publisher/access changes, registry
  administration or retrospective reclassification of evidence. Preserve current
  historical-evidence consumers. Do not add a general application scheduler,
  cache, dynamic admission, approval mirror, trust-proof or recovery service, or
  weaken required coverage to obtain a shorter run.
