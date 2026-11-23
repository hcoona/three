# Contributing to Three

Start with the [documentation portal](docs/README.md) and the README of the
project being changed. Workspace and package manifests define build membership;
they do not establish product scope or documentation ownership.

## Before Starting

Read the accepted target-branch [governance policy](docs/governance/governance-system.md),
[record policy](docs/governance/record-system.md), and
[Delivery Wave](docs/delivery-wave.md) before starting work. Unmerged changes
are proposals and cannot authorize themselves.

Confirm whether the work qualifies as contract-preserving routine maintenance
without new external, paid, credentialed, or privacy effects, or requires an
accepted Delivery Wave entry (identifying its bounded outcome, inputs,
exclusions, and any permitted external effects), then read the owning project
authorities. Within authorized work, routine local editing,
build/lint/test/pilot debugging, and ordinary CI validation are permitted by
default without attempt quotas, and implementation may evolve concurrently with
its owning records in one branch or across a stacked PR chain so long as each
merge into `main` is self-consistent
([work authorization](docs/governance/record-system.md#work-authorization-and-concurrency)).
Use an Issue when several contributors or PRs need coordination; a bounded
single change may use its PR as the work carrier. Neither carrier enlarges
authorization, and project records do not add workflow gates on local
development ([project namespaces](docs/governance/record-system.md#project-namespaces)).

If a relied-on shared canonical authority changes on `main`, pause affected
work and refresh scope, validation, and review. Preserve unrelated work and
human-source inputs. Explicit Wave risk dispositions and accepted protocols
still gate external or paid effects.

For design, security or test/evidence decisions, apply the relevant
[shared engineering principles](docs/engineering/engineering-principles.md)
with the owning project's accepted records. Use them during both authorship and
review; keep concrete assumptions, choices and evidence in the existing project
record or work carrier. Project requirements and domain gates retain their
scope. Pure text edits, test execution and faithful status reporting alone do
not require a new engineering review or report.

## Local Checks

Use the [repository toolchain guidance](README.md#repository-toolchain) and
existing `hk.pkl` checks. With the pinned tools available, check the intended
staged change paths, including deletions and rename sources, using the CI
profile set:

```powershell
python eng/scripts/workflow_delivery_v3_hk.py --staged --files0 -- `
  mise exec -- hk --profile small --profile medium --profile large `
  check --check --no-stage --no-progress --no-fail-fast
```

The helper selects staged paths; `--from-ref <base> --to-ref <candidate>`
selects a commit range instead. These commands select paths without stashing
unstaged content. Record the snapshot and scope actually checked.
CI compares its base with the actual tested checkout;
local pre-commit uses HK's configured stashing. Do not claim staged/worktree
equivalence without evidence. Existing profile and path exclusions still apply.
When changing maintained records, portals, generated interfaces,
`apm.lock.yaml`, or governance catalogs, moving or deleting paths they
reference, or adding or renaming
[discovered record candidates](docs/governance/checker-contract.md#independent-discovery-and-classification)
([enforcement](docs/governance/record-system.md#enforcement)), run
`mise run records:check -- --base <accepted-commit> --worktree --output ../three-record-review.json`
and record the command, snapshot commits, and diagnostic summary in the
governing PR or Issue review carrier. Use `--candidate <commit>` instead of
`--worktree` for an immutable candidate. The
[checker contract](docs/governance/checker-contract.md) defines the advisory
tool's scope, limitations and separate contextual review. Run
`mise run records:test` when changing that implementation. Write the report
outside the worktree so it does not become an input to the next validation run.

HK checks source/configuration conformance. Run affected project tests
explicitly before independent implementation review. With the pinned SDK and
tools prepared, run Workflow Delivery's managed tests from the repository root:

```powershell
dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj
```

The project's native MTP default excludes the NuGet native preparation and
reproducibility categories. Their CI and reproducibility-workflow triggers are
owned by the
[native .NET CI group contract](src/private/app/workflow-delivery/docs/integration/dotnet.md#planning-execution-and-collection),
and the local override filters by the application's
[validation section](src/private/app/workflow-delivery/README.md#validation-and-evidence-limits).

`mise run test:python` runs the
[native Python group](src/private/app/workflow-delivery/README.md#native-python-ci-caller)
at committed HEAD in explicit full mode on Linux, including native preparation,
configured pytest targets and adopted distribution checks. Commit tracked changes
before using this command; Windows contributors need a Linux environment for it.
The managed project command does not replace that complete native group.
General CI selects affected work from its actual Git comparison; its explicit
full mode selects all suites. See the
[execution contract](docs/engineering/hk-execution.md#ci-execution-contract).
Record actual scope and test results in the work carrier (add timing or JUnit
durations when investigating performance or timeouts). For native tests, retain
the [temporary-file and inode constraints](docs/engineering/workspaces.md#python).

## Pull Requests and Review

State the bounded problem and resulting repository change. Cite the applicable
authorization (or routine contract-preserving maintenance), governing records,
and any external effects or copied-source provenance when present, along with
the smallest useful validation. Keep progress and review results in the PR or
Issue, not policy or the Wave.

The [control catalog](docs/governance/controls.yaml) identifies existing checks
and review triggers. Invoke the canonical procedures directly by path:

- [Record-system review](.github/skills/record-system-review/SKILL.md)
- [Research-evidence review](.github/skills/research-evidence-review/SKILL.md)

Retain required domain reviews. Record required checks, independent reviews, and
any disputed or unresolved `GOV-011` classifications or owner dispositions once
in the governing PR or Issue carrier
([enforcement](docs/governance/record-system.md#enforcement)). Author checks
cannot satisfy independent review; agreed findings not requiring an owner
decision are verified on the remediation delta by the originating independent
reviewer, nonmaterial mechanical defects use mechanical verification, and a
clean review requires no separate repository report.

Do not create an Issue, decision record, design, or new metadata solely to fill
a template. The record admission rules and actual consumers determine the need.
Use English for repository content and follow the existing commitlint contract.

## Source and License

Preserve each project's copyright, license, source baseline, privacy obligations,
and package-facing notices. This guidance does not relicense imported projects.
The governance mechanisms derive from `hcoona/microsoft-authentication-cli` at
`1a02498589769c38bc16eefc2efc5f9eca6e6994`; its
[copyright and MIT notice](docs/governance/reference-license.txt) is retained.
