# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### Resolve Legacy Acceptance Ownership and Lifecycle

- **Work carrier:** [Issue #789](https://github.com/hcoona/three/issues/789).
- **Accepted inputs:** The independently reviewed
  [RA-1 responsibility assessment](https://github.com/hcoona/three/pull/766#issuecomment-5745485171),
  current v3
  [requirements](../src/public/lib/three-workflow-delivery-v3/docs/requirements.md),
  [HLD](../src/public/lib/three-workflow-delivery-v3/docs/high-level-design.md),
  [npm LLD](../src/public/lib/three-workflow-delivery-v3/docs/hcoona-release-smoke-npm-lld.md),
  [handoff](../src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md)
  and [engineering principles](engineering/engineering-principles.md).
  Reconcile the older assessment's source and consumer identities against
  accepted main before relying on them.
- **Authorized outcome:** Produce an independently reviewed consumer,
  compatibility and responsibility assessment for the older executable
  acceptance subsystem, with a concrete recommendation and exact follow-up
  scope for repository-owner disposition. Correct abstraction and test
  classification govern the result; no removal count is selected.
- **Design boundary:** Start from `governance run-fixed-acceptance-probe`
  and its CLI proxy, runner, transport and archive helpers; fixed acceptance
  operations and exports in `adapters/github_packages.py`; historical
  `records/governance.py` admission; and their direct tests. Trace their
  relevant caller, export, shared-helper, workflow, documentation and retained
  evidence closure. Separate current executable journeys, shared infrastructure
  and historical readers. Use repository and public support/distribution
  records plus available owner-supplied information; absent repository
  references do not establish absent external consumers.
- **Decision boundary:** Compare retirement of exclusively obsolete operations
  with an explicitly owned compatibility boundary. Preserve current native
  npm/NuGet operators, Release, shared origin/profile/credential/deadline/process
  contracts and historical evidence. Explain any unknown support obligations,
  owner decisions, reader lifecycle, exact source/test migration and validation
  closure. This grant neither chooses retirement nor creates a support promise;
  either requires explicit owner disposition and separately accepted
  implementation authorization.
- **Review and effects:** Permit source-bound design, independent domain and
  evidence review, separate material-finding disposition and ordinary Issue/PR
  delivery. Keep the result in the Issue's review carrier. No production/test,
  workflow, schema, dependency or authority edits, project imports, test
  collection/execution, legacy-command invocation, native probes,
  dispatch/rerun, publication, administrative/access changes, host cleanup or
  unrelated process termination are granted. Documentation validation for
  this Wave remains applicable. Preserve #721 task resources, failed/interrupted
  evidence, [agent execution guidance](engineering/agent-execution.md), native
  and publication gates and spent-operation boundaries.
- **Completion:** End this finite grant through reviewed Wave deletion or
  replacement after delivering the assessment and follow-up proposal. The
  held #761–#764 removals remain untouched. Design establishes no runtime,
  resource-saving or overall test-classification completion claim.
