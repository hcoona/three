# Wiki Index

This file is the content-oriented catalog for the wiki.
Read it first when deciding which pages to inspect.

## Core

| Page                                        | Summary                                                    |
| ------------------------------------------- | ---------------------------------------------------------- |
| [Overview](./overview.md)                   | Current high-level synthesis and open questions.           |
| [Log](./log.md)                             | Append-only timeline of ingests, queries, and lint passes. |
| [Source Digests Guide](./sources/README.md) | Conventions for per-source summary pages.                  |
| [Concepts Guide](./concepts/README.md)      | Conventions for cross-source topic pages.                  |
| [Entities Guide](./entities/README.md)      | Conventions for durable named-entity pages.                |
| [Analyses Guide](./analyses/README.md)      | Conventions for durable query outputs.                     |

## Source Digests

- [README Monorepo Layout](./sources/2026-04-21-readme-monorepo-layout.md) | Root repo layout, workspace membership, and monorepo contract. |
- [C# Packability Rules](./sources/2026-04-21-src-directory-build-props-packability.md) | Path-driven `IsPackable` policy for C# projects. |
- [Root Python Workspace](./sources/2026-04-21-root-pyproject-python-workspace.md) | uv workspace membership and Python publishability signals. |
- [Root pnpm Workspace](./sources/2026-04-21-root-package-json-pnpm-workspace.md) | Root JS workspace scripts and the lack of release orchestration. |
- [mise Tooling Profile](./sources/2026-04-21-mise-tooling-profile.md) | Shared toolchain contract and current HK profile settings. |
- [HK Hook Profiles](./sources/2026-04-21-hk-hook-profiles.md) | Current validation gate profile model. |
- [Release Publish-Target Policy Script](./sources/2026-04-21-release-policy-publish-targets-script.md) | Existing buddy/official policy logic for non-C# languages. |
- [RubyGems Trusted Publishing Script](./sources/2026-04-21-publish-rubygems-script.md) | Existing OIDC-oriented publish pattern. |
- [ImageOcclusionEditor WinUI3 Project](./sources/2026-04-21-image-occlusion-editor-winui3-csproj.md) | Public WinUI app configured for self-contained RID publish. |
- [ImageOcclusionEditor dotnet publish Script](./sources/2026-04-21-image-occlusion-editor-dotnet-publish-script.md) | Current explicit `dotnet publish` binary workflow. |
- [ImageOcclusionEditor Inno Installer Script](./sources/2026-04-21-image-occlusion-editor-inno-installer-script.md) | Current second-stage Windows installer workflow. |
- [PhiFailureDetector Console App](./sources/2026-04-21-phi-failure-detector-console-csproj.md) | Public app still using the Artifacts SDK pattern. |
- [Hjg.Pngcs C# Package Metadata](./sources/2026-04-21-hjg-pngcs-csproj.md) | Representative public NuGet package metadata and compatibility signals. |
- [nbgv-python Metadata](./sources/2026-04-21-nbgv-python-pyproject.md) | Representative public Python package metadata. |
- [markdown-hybrid-search-mcp Metadata](./sources/2026-04-21-markdown-hybrid-search-mcp-pyproject.md) | Public-path Python app that is still metadata-private. |
- [hexo-renderer-asciidoc Package Metadata](./sources/2026-04-21-hexo-renderer-asciidoc-package-json.md) | Representative publishable Node.js package metadata. |
- [steam-account-history-to-csv Package Metadata](./sources/2026-04-21-steam-account-history-to-csv-package-json.md) | Public-path browser-extension project that is still private. |
- [Workflow Release Requirements Interview](./sources/2026-04-21-workflow-release-requirements-interview.md) | Requirement-phase clarifications from the workflow release discussion. |

## Concepts

No concept pages yet.

## Entities

No entity pages yet.

## Analyses

- [Repository Release Landscape](./analyses/repository-release-landscape.md) | Repo-wide release inventory, current implementation signals, and gaps to the desired buddy/official model. |
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md) | Confirmed requirement-phase rules for descriptor-driven release workflows. |
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md) | Review of what belongs in requirements, what should move to design, and what requirement gaps remain. |
