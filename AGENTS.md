# Instructions for Current Repository

This repository is designed to manage polyglot projects in a monorepo structure. Currently it supports the following languages:

1. C#: Managed via `global.json`, `dirs.proj` and CPM (Central Package Manager).
2. Python: Managed by UV workspaces.
3. JavaScript/TypeScript: Managed by PNPM workspaces.

The current status of the repository is that active projects now follow the canonical root monorepo layout under `src/`, `src/lab/`, and `tests/`. The former `OneDotNet/` subtree has been migrated into those canonical roots, and we have not yet set up the release pipelines for each project.

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

## Workflow Delivery v3

Before acting on any Workflow Delivery v3 request, read
`docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`.

Workflow Delivery v3 is the only normative source for new workflow delivery
work. Do not use v1 or v2 to fill a v3 decision gap unless the v3 documents
explicitly require mechanism extraction and revalidation.
