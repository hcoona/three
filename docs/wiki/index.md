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
- [GitHub Packages Supported Registries](./sources/2026-04-22-github-packages-supported-registries.md) | Official GitHub capability boundary for npm, RubyGems, and NuGet, and the lack of a PyPI registry. |

## Concepts

No concept pages yet.

## Entities

No entity pages yet.

## Analyses

- [Repository Release Landscape](./analyses/repository-release-landscape.md) | Repo-wide release inventory, current implementation signals, and gaps to the desired buddy/official model. |
- [Workflow Release Requirements Baseline](./analyses/workflow-release-requirements-baseline.md) | Confirmed requirement-phase rules for descriptor-driven release workflows. |
- [Workflow Release Requirements-Phase Review](./analyses/workflow-release-requirements-phase-review.md) | Review of what belongs in requirements, what should move to design, and what requirement gaps remain. |
- [Workflow Release Design Direction](./analyses/workflow-release-design-direction.md) | Top-level architecture options and recommended direction for the design phase. |
- [Workflow Release Architecture Model](./analyses/workflow-release-architecture-model.md) | Settled architecture-layer model for envelopes, variants, artifacts, publish nodes, shared target-instance snapshots, reusable destination contracts, and control-plane boundaries. |
- [Workflow Release Design Layering and Implementation Handoff Scope](./analyses/workflow-release-design-layering-and-handoff-scope.md) | Three-layer reading of the current design corpus, recording closed upper-layer and middle-layer scope plus guardrails now resolved by low-level design and current implementation handoff for traceability, diagnostics, dry-run build policy, and receipt lookup layout. |
- [Workflow Release Descriptor Schema](./analyses/workflow-release-descriptor-schema.md) | Author-time file inventory and YAML schema for project descriptors, shared target-instance catalog entries, field-scoped path rules, out-of-`src/` descriptor rejection, descriptor-owned `project.id` as the stable current-scope release-tag slug, project-local variant-id handle uniqueness plus rejection of duplicate semantic variant `dimensions` sets, variant-local semantic artifact-identity uniqueness despite descriptor-local `artifact.id` handles, checked-in source-file resolution, closed current-scope ecosystem-to-primary-manifest mapping, current-scope contract, capability, projection, and artifact-compatibility rules, discovery, references, and validation boundaries. |
- [Workflow Release Plan Shape](./analyses/workflow-release-plan-shape.md) | Authoritative `three.release.plan/v1alpha1` planner output shape covering the top-level envelope and normalized graph, frozen project-selection normalization and error semantics, deterministic plan and graph id generation, normalized request flags and resolved `selected-project-ids` inside `plan-id`, selected-project source snapshots, planner-authored NBGV-derived publish identity plus replay disposition and live publish mode, the official-frozen predicate for buddy `FORCE`, normalized projection data, frozen target-instance snapshots, deterministic Group 1 mapping, and explicit out-of-plan boundaries. |
- [Workflow Release Workflow and Executor Boundaries](./analyses/workflow-release-workflow-executor-boundaries.md) | Control-plane entry-point and orchestration boundaries for consuming `three.release.plan/v1alpha1`, including normalization of the planner-facing request contract with frozen project-selection semantics, one build unit per variant, topology-partitioned publish units per publish node, explicit job-to-job artifact and receipt handoffs, a distinct control-plane-authored synthetic skip receipt contract, a minimal structured planner-diagnostic contract, executor input or output contracts, and explicit ownership of approvals, concurrency, project-scoped tagging, runtime wiring, and reporting. |
- [Workflow Release Deferred PyPI Multi-Wheel Support](./analyses/workflow-release-deferred-pypi-multi-wheel-support.md) | Deferred issue record for future PyPI multi-wheel or cross-variant wheel support beyond the current one-wheel-plus-optional-sdist scope. |
- [Workflow Release Low-Level Design](./analyses/workflow-release-low-level-design.md) | Lower-layer implementation handoff that freezes stable workflow filenames for trusted publishing, entry-workflow-bound first-delivery live PyPI publication, entry inputs, dry-run validation-build behavior, planner diagnostic-code registration, JSON handoff files, artifact and immutable-proof naming, registry adapter obligations, GitHub permissions, tag orchestration, and acceptance traceability while leaving internal code decomposition implementation-owned. |
- [Workflow Release OIDC Publish Topology Research](./analyses/workflow-release-oidc-publish-topology.md) | Registry trusted-publisher support matrix showing why external OIDC publish topology must become a first-class design dimension and why live PyPI publication belongs in first delivery. |
