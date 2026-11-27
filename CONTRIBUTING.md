# Contributing to Three

Start with [`docs/README.md`](docs/README.md) and the `README.md` of the project
being changed. Workspace and package manifests define build membership rather
than product scope.

## Before Starting

Start with [work authorization](docs/governance/record-system.md#work-authorization-and-concurrency)
as accepted on `main` and check the [Wave](docs/delivery-wave.md) for bounds
applicable to your workstream. Use the ordinary route when standing authority
covers the action. Apply [shared engineering principles](docs/engineering/engineering-principles.md)
with the owning project's records for design, security, and test decisions.

## Local Checks

Prepare locked tools per [`README.md`](README.md#repository-toolchain) and
[`docs/engineering/workspaces.md`](docs/engineering/workspaces.md). Check staged
or commit-range changes with the full HK profile set (covering formatters,
`lychee` offline Markdown link/anchor validation, `records:check` binding and
requirement-ID checks, and `public-release-inventory`):

```powershell
python eng/scripts/workflow_delivery_v3_hk.py --staged --files0 -- `
  mise exec -- hk --profile small --profile medium --profile large `
  check --check --no-stage --no-progress --no-fail-fast
```

Use `--from-ref <base> --to-ref <candidate>` instead of `--staged` to check a
commit range (`records:check` compares the selected index or commit endpoints,
while `lychee` and `public-release-inventory` inspect the checked-out tree); run
`mise run records:test` when editing `eng/scripts/check_repository_records.py`.

HK validates source and record conformance; run affected project tests
explicitly before implementation review
([execution contract](docs/engineering/hk-execution.md#ci-execution-contract)):

- **.NET / Workflow Delivery:**
  `dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj`
  (or the affected test project).
- **Python (Linux / WSL):** `mise run test:python` at committed `HEAD` for the
  complete native Python CI group, or targeted `uv run pytest` during iteration.

## Pull Requests and Review

Use English, follow the Conventional Commits + AP title-case `commitlint`
contract, and complete `.github/pull_request_template.md` concisely (problem and
change, authorization/scope, executed validation commands with outcomes, and
applicable independent reviews/dispositions under
[`enforcement`](docs/governance/record-system.md#enforcement) and
[`GOV-011`](docs/governance/governance-system.md#gov-011-separate-findings-from-disposition)).
Do not create extra records or Issues merely to satisfy templates when a single
PR or existing README suffices.

## Source and License

Preserve each project's copyright, license, source baseline, and privacy
notices. Governance mechanisms derive from `hcoona/microsoft-authentication-cli`
(`1a024985`); [`docs/governance/reference-license.txt`](docs/governance/reference-license.txt)
is retained.
