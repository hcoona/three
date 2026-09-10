# Instructions for Current Repository

This repository is designed to manage polyglot projects in a monorepo structure. Currently it supports the following languages:

1. C#: Managed via `global.json`, `dirs.proj` and CPM (Central Package Manager).
2. Python: Managed by UV workspaces.
3. JavaScript/TypeScript: Managed by PNPM workspaces.

The current status of the repository is that active projects now follow the canonical root monorepo layout under `src/`, `src/lab/`, and `tests/`. The former `OneDotNet/` subtree has been migrated into those canonical roots. Project release scope and readiness follow the accepted project authorities.

The versioning of the projects is managed by NBGV (Nerdbank.GitVersioning). We write a hatching plugin (`nbgv-python`) to adapt NBGV for our Python projects.

The code linting and formatting tools are set up as follows:

1. C#: `dotnet build` and `dotnet format`.
2. Python: `ruff` for linting and formatting, `pyrefly` for type checking.
3. JavaScript/TypeScript: `biome` for linting and formatting, `tsgo` for type checking.

We use [MISE](https://mise.jdx.dev/) to manage tools across different projects in the monorepo. Check `mise.toml` for further details.

We use [HK](https://hk.jdx.dev/) for both git hooks manager and CI validation gate. Check `hk.pkl` for further details.

Note that in GitHub workflows, general C# builds run on Windows runners unless a release descriptor variant explicitly targets another platform; Python and JavaScript/TypeScript projects can be built on Ubuntu runners.

Do not get stuck in a pager when executing CLI commands.

You must use ENGLISH rather than CHINESE for all code, comments, commit messages, documentation in this repository.

## Repository Governance

Before work, read the accepted target-branch
[governance policy](docs/governance/governance-system.md),
[record policy](docs/governance/record-system.md), and
[Delivery Wave](docs/delivery-wave.md). Proposed changes cannot authorize
themselves or weaken their accepted review obligations.

Use the [documentation portal](docs/README.md) and
[family catalog](docs/governance/record-families.yaml) to find project authorities.
Keep project records separate and use path-qualified cross-project references.
A README is sufficient when no distinct record has a current consumer.

Before making or reviewing architecture, abstraction, dependency-boundary,
security/recovery or test/evidence strategy decisions, invoke
[shared engineering guidance](.github/skills/engineering-guidance/SKILL.md)
and follow its project routing.

For record-system changes, invoke
[record-system review](.github/skills/record-system-review/SKILL.md) directly.
For research/evidence changes and every merged Wave change, invoke
[research-evidence review](.github/skills/research-evidence-review/SKILL.md)
directly, retaining applicable domain reviews. Authors and implementers cannot
satisfy their own independent review or solely adjudicate material findings.
These canonical paths are the invocation interface; no implicit skill discovery
is assumed. Do not edit generated `.agents/skills/` or `.github/agents/` outputs
by hand. For delegated execution, follow
[agent execution and recovery](docs/engineering/agent-execution.md).

Read root or current-directory `AGENTS.local.md` after tracked instructions when
present. It is private, machine-specific, gitignored, and must not be committed.

## Workflow Delivery v3

Before acting on any Workflow Delivery v3 request, read
`src/public/lib/three-workflow-delivery-v3/docs/agent-handoff.md`.

Workflow Delivery v3 is the only normative source for new workflow delivery
work. Do not use v1 or v2 to fill a v3 decision gap unless the v3 documents
explicitly require mechanism extraction and revalidation.

The handoff's domain gates, including separate NuGet implementation, native-operation,
and publication authorization, survive the move. Repository governance does not
reopen the completed npm proving work or supply permission for publication,
dispatch, authentication, or access changes.
