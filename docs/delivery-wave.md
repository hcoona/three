# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Advance Workflow Delivery v3

- **Work carrier:** [Issue #721](https://github.com/hcoona/three/issues/721)
  and its delivery PRs.
- **Authorized outcome:** Make repeated pytest, root HK and normal commit-hook
  runs sustainable by measuring and reducing unnecessary temporary-file creation
  and bounding retained test-owned data. Start with Workflow Delivery v3 fixtures
  and their directly consumed repository, dependency and subprocess helpers.
  Preserve test isolation, required behavioral and real integration evidence,
  and useful failure diagnostics. This includes scoped fixture/lifecycle changes,
  applicable validation, independent review and normal protected PR delivery.
- **Governing inputs:** The current
  [v3 handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md),
  [requirements including WD-NFR-007](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md#quality-attributes),
  [CI qualification MLD](../src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md),
  [shared engineering principles](engineering/engineering-principles.md),
  and applicable domain design, validation and review gates. Refresh observations
  against accepted `main`; an unmerged candidate is not an accepted prerequisite.
- **Measurement and recovery:** Before diagnostic execution, retain an
  independently reviewed protocol in the work carrier specifying commands,
  environment, owned temporary roots, before/during/after counts, finite run
  bounds, resource stop conditions and cleanup. Measure principal producers and
  success, failure and interruption retention. Retain compact evidence outside
  disposable roots; cleanup is limited to attributable inactive test-owned data.
  Protect active runs and unrelated host data. Report observed peaks, retained
  counts and limitations; relocating temporary roots or raising limits alone
  does not complete the outcome.
- **Order and concurrency:** Resolve this resource-lifecycle outcome before
  [#720 test cleanup](https://github.com/hcoona/three/issues/720), which remains
  paused and needs its own merged grant. Preserve its accepted coverage owners
  when changing overlapping fixtures. Follow the
  [agent-execution guidance](engineering/agent-execution.md), coordinate
  conflicting writes, and refresh affected evidence when prerequisites change.
- **Scope boundary:** All other Workflow Delivery v3 mainline development
  remains paused. No layer-wide test cleanup, production behavior or architecture
  change, HK scheduling/input-selection change, weakened check, host-wide cleanup,
  host resource-limit change, general caching or benchmark project is included.
  Test count, runtime and inode reduction are not arbitrary completion quotas.
- **Effects and domain gates:** Normal Git/Issue/PR operations, scoped
  public-source inspection, isolated local test work and applicable checks are
  permitted. Preserve independent review and independent disposition of material
  findings. Rerunning failed required PR-validation jobs in the ordinary
  [repository CI workflow](../.github/workflows/ci.yml) remains permitted. This
  does not authorize v3 business-workflow reruns, native service probes, workflow
  dispatch, publication, administrative, authentication or access changes, or
  replay of completed npm/NuGet proving. Existing domain gates and spent-operation
  boundaries remain in force.
- **Completion boundary:** Delete or replace this entry through a reviewed Wave
  change once the bounded resource-lifecycle outcome is accepted or the owner
  changes its scope. Completion does not resume #720 or other v3 development.
