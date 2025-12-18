---
post_title: nbgv-python Architecture Overview
author1: GitHub Copilot
post_slug: nbgv-python-architecture
microsoft_alias: copilot
featured_image: https://devblogs.microsoft.com/azuremigrate/wp-content/uploads/sites/113/2023/01/microsoft-logo.png
categories:
    - Architecture
tags:
    - nbgv
    - python
    - hatchling
ai_note: This document was prepared with AI assistance.
summary: High-level architecture mapping Nerdbank.GitVersioning concepts onto a Python package that integrates with hatchling.
post_date: 2025-11-08
---

# nbgv-python Architecture Overview

## Context

- Nerdbank.GitVersioning exposes version metadata through the `nbgv` .NET CLI, which returns structured JSON for `get-version` and mutates repository state for commands like `set-cloud-build-version`.
- The `nerdbank-gitversioning.npm` package wraps the CLI by bundling the `nbgv` tool and invoking it through Node's subprocess APIs, keeping a single implementation of version semantics in the CLI.
- The `versioningit` Python package demonstrates how Hatch plugins surface VCS-driven versions by implementing `VersionSourceInterface` and deferring business logic to reusable core modules.

## Architectural Principles

- Reuse the `nbgv` CLI as the single source of truth for version calculations, avoiding reimplementation of Git parsing logic in Python.
- Encapsulate all command discovery and invocation logic in a dedicated runner component so both library APIs and Hatch integrations share the same execution path.
- Represent the CLI's JSON payload as a thin, immutable data object that preserves both the original field names and snake_case aliases expected by Python consumers.
- Provide a Hatch `VersionSourceInterface` plugin so dynamic versions can be resolved during build without ad-hoc scripting, mirroring `versioningit`'s approach.

## Key Components

- `CommandLocator`: resolves how to execute `nbgv`, checking for an explicit environment override, a direct executable on `PATH`, or a `dotnet tool run` fallback; mirrors the lookup strategy used by the JavaScript wrapper's `getNbgvCommand` utility.
- `NbgvRunner`: a thin subprocess facade that normalises argument construction, handles error reporting, and funnels all higher-level operations through the CLI, ensuring behavioural parity with Nerdbank.GitVersioning.
- `GitVersion`: immutable view over the CLI's JSON output with attribute and dictionary-style access, easing integration in build hooks and templating contexts.
- `NbgvVersionSource`: Hatch plugin that uses `NbgvRunner` to fetch version data at build time, exposing configuration knobs (command path, version field selection, working directory) akin to `versioningit`'s plugin.
- `normalize_version_field`: helper that maps SemVer-style output (e.g., `-beta.1`, `-gabcdef`) to PEP 440 compliant strings for Python packaging compatibility.
- `TemplateFieldsBuilder`: produces structured template variables (version tuple, normalized version, CLI metadata) for templating scenarios.
- `write_version_file`: emits templated artifacts (e.g., `_version.py`) while keeping the CLI as the single truth source.

## Data & Control Flow

- Build tools call into the Hatch plugin to obtain the package version. The plugin delegates to `NbgvRunner.get_version()`, which executes `nbgv get-version --format json` within the project root and parses the JSON reply into `GitVersion`.
- Library consumers can call helper APIs (e.g., `ensure_placeholder_version()`) that wrap specific `nbgv` commands; these helpers rely on the same runner, guaranteeing consistent behaviour.
- CLI entry point forwards user arguments directly to the resolved `nbgv` command while still benefiting from command discovery so developers can rely on the Python package even when `nbgv` is installed as a local .NET tool.

## External Integration Points

- Requires either a globally installed `nbgv` executable or access to `dotnet tool run nbgv` within the project; we surface configuration hooks and informative errors to guide users when the toolchain is missing.
- Hatch build backend loads the plugin via the `hatchling.version` entry point, so projects declare `dynamic = ["version"]` and set `[tool.hatch.version] source = "nbgv"` to activate Nerdbank.GitVersioning semantics.
- Future work can extend the plugin to propagate template fields or on-build hooks similar to `versioningit`, using the stored `GitVersion` payload for templated file mutations.
