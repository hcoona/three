# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Advance Workflow Delivery v3

- **Work carriers:** [Issue #712](https://github.com/hcoona/three/issues/712),
  its three child issues and their delivery PRs. These carriers coordinate the
  bounded work below; they cannot enlarge this grant.
- **Authorized outcome:** Complete the CI qualification test-responsibility
  workstream from changed inputs through required obligations and admitted
  Evidence to the final Decision. Align business scenarios, precise core
  algorithm and contract tests, and external integration tests with their owning
  responsibilities while preserving required behavior and critical invariants.
  This includes scoped test and fixture changes, applicable validation,
  independent review, necessary documentation and normal protected PR delivery.
- **Governing inputs:** The current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements including WD-NFR-007](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md#quality-attributes),
  [CI qualification MLD](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [shared testing principles](engineering/engineering-principles.md#testing),
  and applicable HLD, MLDs, slice LLDs and domain review/delivery gates. Refresh
  source observations against accepted `main`; an unmerged candidate is not an
  accepted prerequisite.
- **Delivery order:** Deliver
  [#713 business scenarios](https://github.com/hcoona/three/issues/713), then
  [#714 core algorithms and contracts](https://github.com/hcoona/three/issues/714),
  then [#715 integration boundaries](https://github.com/hcoona/three/issues/715).
  Each stage starts only after the preceding stage's independently reviewed
  result is merged into `main`. Each PR focuses on one responsibility layer of
  this CI flow. Record cross-layer findings in their owning child issue; changes
  needed to preserve required coverage remain atomic.
- **Concurrency and recovery:** Follow the
  [agent-execution guidance](engineering/agent-execution.md) within the active
  stage. Coordinate conflicting changes and refresh affected validation/review
  when accepted prerequisites change. Keep progress and evidence in the work
  carriers, not this Wave.
- **Scope boundary:** All other Workflow Delivery v3 mainline development is
  paused and requires a new or amended merged Wave entry to resume. Preserve
  production behavior; no production architecture redesign, broad v3 test purge,
  new record hierarchy, HK scheduling or input-selection change, marker taxonomy,
  caching, benchmark or test-speed project is included. Test count, runtime,
  coverage percentage and assertion volume are not completion quotas. This
  workstream does not certify every v3 test or extend to release/publication flows.
- **Effects and domain gates:** Normal Git/Issue/PR operations, scoped
  public-source inspection, isolated test work and applicable checks are
  permitted. Preserve independent review and independent disposition of material
  findings. Rerunning failed required PR-validation jobs in the ordinary
  [repository CI workflow](../.github/workflows/ci.yml) remains permitted. This
  does not authorize v3 business-workflow reruns, native service probes, workflow
  dispatch, publication, administrative, authentication or access changes, or
  replay of completed npm/NuGet proving. Existing domain gates and spent-operation
  boundaries remain in force.
- **Completion boundary:** Delete or replace this entry through a reviewed Wave
  change when all three stages of the bounded CI qualification workstream are
  accepted, or the owner changes its scope. Completion does not resume other v3
  development; any further advancement requires a new or amended merged grant.
