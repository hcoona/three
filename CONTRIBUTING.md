# Contributing to Three

Start with the [documentation portal](docs/README.md) and the README of the
project being changed. Workspace and package manifests define build membership;
they do not establish product scope or documentation ownership.

## Before Starting

Read the accepted target-branch [governance policy](docs/governance/governance-system.md),
[record policy](docs/governance/record-system.md), and
[Delivery Wave](docs/delivery-wave.md) before starting work. Unmerged changes
are proposals and cannot authorize themselves.

Identify the applicable accepted entry, bounded outcome, inputs, exclusions,
and effects, then read the owning project authorities. Use an Issue when several
contributors or PRs need coordination; a bounded single change may use its PR
as the work carrier. Neither carrier enlarges the accepted grant. The
[record policy](docs/governance/record-system.md#work-authorization-and-concurrency)
retains the source work-authorization and dependency rules for repository work.

If a relied-on accepted prerequisite changes, pause affected work and refresh
scope, validation, and review. Preserve unrelated work and human-source inputs.
Product/domain preconditions and external-effects authorization still apply.

For design, security or test/evidence decisions, apply the relevant
[shared engineering principles](docs/engineering/engineering-principles.md)
with the owning project's accepted records. Use them during both authorship and
review; keep concrete assumptions, choices and evidence in the existing project
record or work carrier. Project requirements and domain gates retain their
scope. Pure text edits, test execution and faithful status reporting alone do
not require a new engineering review or report.

## Local Checks

Before an expensive validation or commit, inspect the selected checks and
validate the intended commit message with the existing rules:

```powershell
git diff --cached --check
mise exec -- python eng/scripts/workflow_delivery_v3_hk.py --staged --files0 -- `
  hk --profile small --profile medium --profile large check --plan --json
mise exec -- python eng/scripts/workflow_delivery_v3_hk.py --staged --files0 -- hk run impact-check --plan --json
'perf(hk): Select Validation by Consumed Inputs' | pnpm exec -- commitlint --strict
```

Use your actual intended message in the last command. A passing plan is only
a selection preview; it does not run checks. The first plan shows file checks
and the impact dispatcher; the second shows product suite selection. The
[HK execution guide](docs/engineering/hk-execution.md#selecting-checks-by-their-inputs)
explains input scope and the checks that remain unconditional. The normal
pre-commit and commit-msg hooks still apply when committing.

Use the [repository toolchain guidance](README.md#repository-toolchain) and
existing `hk.pkl` checks. With the pinned tools available, check the intended
staged files using the CI profile set:

```powershell
mise exec -- python eng/scripts/workflow_delivery_v3_hk.py --staged --files0 -- `
  hk --profile small --profile medium --profile large check --check --no-stage --no-progress --no-fail-fast
```

The helper retains staged deletions and both rename paths for HK selection;
file-reading wrappers pass existing operands to their tools. Record the snapshot and scope
actually checked. CI uses the same helper with explicit base/head refs;
local pre-commit uses HK's configured stashing. Do not claim staged/worktree
equivalence without evidence. Existing profile and path exclusions still apply.
The [checker contract](docs/governance/checker-contract.md) defines record
validation interfaces; no new general checker is installed.

## Pull Requests and Review

State the bounded problem and resulting repository change. Link the accepted
authorization, work carrier, and governing records; identify material exclusions,
external effects, record/consumer changes, and the smallest useful validation.
Distinguish source findings, observations, inference, hypotheses, and decisions.
Keep progress and review results in the PR or Issue, not policy or the Wave.

The [control catalog](docs/governance/controls.yaml) identifies existing checks
and review triggers. Invoke the canonical procedures directly by path:

- [Record-system review](.github/skills/record-system-review/SKILL.md)
- [Research-evidence review](.github/skills/research-evidence-review/SKILL.md)

Retain required
domain reviews. Identify the independent reviewer, material findings, independent
triage, and required owner dispositions in the governing carrier. Author checks
cannot satisfy independent review. A review that finds no change necessary does
not require a separate repository report.

Do not create an Issue, decision record, design, or new metadata solely to fill
a template. The record admission rules and actual consumers determine the need.
Use English for repository content and follow the existing commitlint contract.

## Source and License

Preserve each project's copyright, license, source baseline, privacy obligations,
and package-facing notices. This guidance does not relicense imported projects.
The governance mechanisms derive from `hcoona/microsoft-authentication-cli` at
`1a02498589769c38bc16eefc2efc5f9eca6e6994`; its
[copyright and MIT notice](docs/governance/reference-license.txt) is retained.
