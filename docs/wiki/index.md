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
- [GitHub Packages Supported Registries](./sources/2026-04-22-github-packages-supported-registries.md) | Official GitHub capability boundary for npm, RubyGems, and NuGet, and the lack of a PyPI registry. |

## Concepts

No concept pages yet.

## Entities

No entity pages yet.

## Analyses

- [Workflow Delivery Architecture Versions](./analyses/workflow-delivery/README.md) | Entry point and normative priority for v1, v2, and v3 delivery architecture.
- [Workflow Delivery v3 Requirements](./analyses/workflow-delivery/v3/requirements.md) | Confirmed normative product and system requirements baseline.
- [Workflow Delivery v3 High-Level Design](./analyses/workflow-delivery/v3/high-level-design.md) | Active top-level architecture for the clean v3 implementation line.
- [Workflow Delivery v3 Repository Model and Release Unit MLD](./analyses/workflow-delivery/v3/repository-model-release-unit-mld.md) | Project fact discovery, Release Unit authoring, Build Definitions, authoritative native NBGV projections, and Repository Model compilation.
- [Workflow Delivery v3 Governance Integration MLD](./analyses/workflow-delivery/v3/governance-integration-mld.md) | Native platform authority, same-revision control, first-slice accepted-risk boundary, one Approval Environment, permission isolation, and normal Live activation governance.
- [Workflow Delivery v3 CI Qualification MLD](./analyses/workflow-delivery/v3/ci-qualification-mld.md) | Candidate identity, HK source-tree conformance, project-selected quality policy, affected-scope planning, Evidence, and CI decision contracts.
- [Workflow Delivery v3 Release Delivery MLD](./analyses/workflow-delivery/v3/release-delivery-mld.md) | Request-local Repository Model input, active-only destination observation, exact-version authority, zero-or-one action authorization, Publication Result and Finalizer lineage, and new-dispatch retry.
- [Workflow Delivery v3 `hcoona-release-smoke-npm` First-Slice LLD](./analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md) | Exact first-slice contract for Governance v2, native npm profile acceptance, package-control proofs, one-Environment authorization, marker/Result terminal transport, current-DAG finalization, and one auditable activation dispatch.
- [Workflow Delivery v3 Shared Foundation MLD](./analyses/workflow-delivery/v3/shared-foundation-mld.md) | Record and artifact primitives, Repository Model and NBGV fact transport, static Definition catalogs, Providers, Build and Quality Adapters, execution classes, and generic clients.
- [Workflow Delivery v3 AI Agent Handoff](./analyses/workflow-delivery/v3/agent-handoff.md) | Protected disabled runtime, completed legacy cleanup, bounded native tooling and its separate delivery/authorization gates, live Git inspection, and remaining activation work.
- [Workflow Delivery v3 Architecture Glossary](./analyses/workflow-delivery/v3/architecture-glossary.md) | Normative v3 terminology and confirmed architecture principles.
- [Workflow Delivery v3 Migration and Document Policy](./analyses/workflow-delivery/v3/migration-strategy.md) | Clean-line implementation strategy and explicit rules for porting, rewriting, or archiving v2 assets.
