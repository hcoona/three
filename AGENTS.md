# Instructions for Current Repository

This polyglot monorepo manages active projects under `src/`, `src/lab/`,
`src/sample/`, and `tests/`:

1. **C#:** `global.json`, `dirs.proj`, Central Package Management, `dotnet build`,
   and `dotnet format` (versioned via Nerdbank.GitVersioning; general C# CI runs
   on Windows unless a release variant targets another OS).
2. **Python:** `uv` workspaces (`pyproject.toml`), `nbgv-python` Hatchling
   plugin, `ruff` lint/format, and `pyrefly` type checking.
3. **JavaScript/TypeScript:** `pnpm` workspaces, `biome` lint/format, and `tsgo`
   type checking.

Toolchain versions are managed with [MISE](https://mise.jdx.dev/) (`mise.toml`),
and Git hooks plus CI source conformance use [HK](https://hk.jdx.dev/)
(`hk.pkl`). Disable CLI pagers (`--no-pager`), and use **English** for all code,
comments, commit messages, and repository documentation.

Read any directory-local `AGENTS.md` (such as
[`src/private/app/workflow-delivery/AGENTS.md`](src/private/app/workflow-delivery/AGENTS.md))
when working in that subtree, and read root or local `AGENTS.local.md` after
tracked instructions when present (private, gitignored, never committed). Do not
hand-edit generated `.agents/skills/` or `.github/agents/` outputs.

## Repository Governance

Before starting work, read target-branch
[`governance-system.md`](docs/governance/governance-system.md),
[`record-system.md`](docs/governance/record-system.md) (especially
[work authorization](docs/governance/record-system.md#work-authorization-and-concurrency),
[project namespaces](docs/governance/record-system.md#project-namespaces), and
[enforcement](docs/governance/record-system.md#enforcement)), and
[`docs/delivery-wave.md`](docs/delivery-wave.md), then locate owning project
records through [`docs/README.md`](docs/README.md). Proposed changes cannot
authorize themselves or waive accepted review obligations.

Invoke canonical skills directly when their triggers apply (authors and
implementers cannot satisfy their own independent review or solely dismiss
material findings under
[`GOV-011`](docs/governance/governance-system.md#gov-011-separate-findings-from-disposition)):

- [Engineering guidance](.github/skills/engineering-guidance/SKILL.md): before
  making or reviewing architecture, abstraction, dependency-boundary,
  security/recovery, or test/evidence strategy decisions.
- [Record-system review](.github/skills/record-system-review/SKILL.md): for
  edits to `docs/governance/**`, `docs/delivery-wave.md`,
  `schemas/governance/**`, `docs/engineering/engineering-principles.md`, root
  `AGENTS.md` / `CONTRIBUTING.md` / PR template, `record-families.yaml` or
  `apm.lock.yaml` provenance, `hk` conformance controls, governance review
  Skills, authority/requirement-ID/immutability/license contracts in any
  record, or [Record-System Gate](docs/governance/record-system.md#record-system-gate)
  claims.
- [Research-evidence review](.github/skills/research-evidence-review/SKILL.md):
  for `research` records, empirical platform/support claims, fired
  mutable-source rechecks, formal/empirical evidence reports, or Wave
  grants/operations with external, credentialed, or paid empirical effects.

For multi-worktree delegated execution, follow
[`docs/engineering/agent-execution.md`](docs/engineering/agent-execution.md).
