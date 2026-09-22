# Current Delivery Wave

This record is the sole positive work-authorization authority under the
[repository record policy](governance/record-system.md#work-authorization-and-concurrency).
Only its accepted `main` version grants work. Issues, PRs, sessions and proposed
edits cannot add to or enlarge a grant. Merge changes authorization; deletion
ends a grant. Git and the proposing PR retain the reason and history.

## Authorized Advancements

### CI Execution Ownership and Python Unification

- **Work carrier:** [Issue #817](https://github.com/hcoona/three/issues/817).
- **Accepted inputs:** The repository owner's approval to unify Workflow
  Delivery v3 on Python 3.14, move project tests from HK to affected CI
  execution, and continue timing diagnosis; the current
  [engineering principles](engineering/engineering-principles.md),
  [HK execution guidance](engineering/hk-execution.md), and
  [v3 requirements and design](../src/public/lib/three-workflow-delivery-v3/docs/README.md).
- **Bounded advancement:** Align active v3 workflow, script, local and test
  runtime selection with the repository's mise-managed Python 3.14 authority.
  First accept the affected v3 requirements/design and migration contract.
  Then implement the dependent execution changes, synchronizing directly
  affected checks and guidance so HK owns source/configuration conformance
  and CI owns project tests. Preserve existing required validation until its
  replacement is accepted.
  Select language tests, builds and platform artifacts from actual affected
  inputs and dependencies; eliminate duplicated v3 test execution and
  unnecessary job ordering. Preserve explicit local and full-validation test
  commands. Then optimize expensive local integration scenarios at the least
  costly sufficient test layer, retaining required real-tool properties,
  fixture isolation, bounded concurrency and owned temporary-file cleanup.
- **Outcome:** Relevant changes retain complete required validation, unrelated
  changes avoid unrelated project suites, and normal local commits avoid
  project tests. Issue/PR evidence records local command and commit duration,
  inode observations, and CI job/step timing and critical-path diagnosis.
  Runtime-bound historical observations remain historical; changed active
  entry points receive applicable validation before relying on their results.
- **Permitted effects:** Local development and validation, isolated owned test
  fixtures, dependency preparation, GitHub Issue/PR/review updates, ordinary
  PR/push CI, and read-only collection of their results. Independent domain,
  record and evidence review and separate finding triage remain required.
- **Exclusions:** Publication or destination operations, native acceptance
  probes, manual workflow dispatch, credential/access or branch-protection
  changes, unrelated host/cache cleanup, and unbounded test concurrency.
  Existing native-operation and publication gates remain in force.
