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

Start with [work authorization](docs/governance/record-system.md#work-authorization-and-concurrency)
as accepted on `main`. Check [Wave entries](docs/delivery-wave.md) applicable
to the workstream before relying on standing authority. Find owning records via
[`docs/README.md`](docs/README.md) and respect their
[namespaces](docs/governance/record-system.md#project-namespaces).

Invoke [engineering guidance](.github/skills/engineering-guidance/SKILL.md)
before architecture, abstraction, dependency, security/recovery or test/evidence
strategy decisions. For governance, grant, contract or evidence changes, use
[review applicability](docs/governance/record-system.md#independent-review-applicability)
to route independent [record-system](.github/skills/record-system-review/SKILL.md)
and [research-evidence](.github/skills/research-evidence-review/SKILL.md) reviews.
Apply [`GOV-011`](docs/governance/governance-system.md#gov-011-separate-findings-from-disposition)
to material findings. [Amendments](docs/governance/governance-system.md#changing-rules)
cannot waive their own accepted review obligations.

For multi-worktree delegated execution, follow
[`docs/engineering/agent-execution.md`](docs/engineering/agent-execution.md).
