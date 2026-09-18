# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Advance Workflow Delivery v3

- **Work carrier:** [Issue #720](https://github.com/hcoona/three/issues/720)
  and its delivery PRs, within [#712](https://github.com/hcoona/three/issues/712).
- **Authorized outcome:** Clean up CI qualification tests by responsibility
  layer, then review the complete flow from changed inputs through required
  obligations and admitted Evidence to the final Decision. Remove redundant
  cases, incidental implementation constraints and unnecessary test scaffolding
  while preserving required behavior and critical invariants. This includes
  scoped test and fixture changes, applicable validation, independent review,
  necessary documentation and normal protected PR delivery.
- **Prerequisites and governing inputs:** Start from the accepted #713, #714
  and #715 results and the delivered
  [#721 temporary-resource lifecycle correction](https://github.com/hcoona/three/issues/721).
  Follow the current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements including WD-NFR-007](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md#quality-attributes),
  [CI qualification MLD](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [shared testing principles](engineering/engineering-principles.md#testing),
  [temporary-resource guidance](engineering/workspaces.md#python),
  and applicable domain design, validation and delivery gates. Refresh source
  observations against accepted `main`; an unmerged result is not a prerequisite.
- **Delivery order:** Review and deliver business-scenario cleanup, then
  core/contract cleanup, then integration-boundary cleanup. Each layer starts
  only after the preceding layer's independently reviewed result is accepted
  in `main`. Each
  removal or consolidation identifies the preserved invariant and its remaining
  test owner, with necessary coverage changes kept atomic. Finish with an
  independent whole-flow review for coverage gaps, duplicate ownership and
  unnecessary abstractions before completing #712.
- **Concurrency and recovery:** Follow the
  [agent-execution guidance](engineering/agent-execution.md) within the active
  layer. Coordinate conflicting writes and refresh affected evidence when
  prerequisites change. Preserve failure diagnostics and active/unrelated data;
  no host-wide cleanup or resource-limit changes are included.
- **Scope boundary:** All other Workflow Delivery v3 mainline development
  remains paused. Preserve production behavior, immutable identities/digests
  and authority-critical contracts. No production redesign, broad v3 test purge,
  release/publication test work, new record hierarchy, HK scheduling or
  input-selection change, marker taxonomy, general caching or benchmark project
  is included. Test count and runtime are observations, not reduction quotas.
- **Effects and domain gates:** Normal Git/Issue/PR operations, scoped
  public-source inspection, isolated local tests and applicable checks are
  permitted. Preserve independent review and independent disposition of material
  findings. Rerunning failed required PR-validation jobs in the ordinary
  [repository CI workflow](../.github/workflows/ci.yml) remains permitted. This
  does not authorize v3 business-workflow reruns, native service probes, workflow
  dispatch, publication, administrative, authentication or access changes, or
  replay of completed npm/NuGet proving. Existing domain gates and spent-operation
  boundaries remain in force.
- **Completion boundary:** Delete or replace this entry through a reviewed Wave
  change after all three layer dispositions and the whole-flow review are
  accepted, or the owner changes scope. Completion does not resume other v3 work.
