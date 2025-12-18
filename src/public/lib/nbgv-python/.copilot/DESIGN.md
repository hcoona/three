---
post_title: nbgv-python Design Plan
author1: GitHub Copilot
post_slug: nbgv-python-design
microsoft_alias: copilot
featured_image: https://devblogs.microsoft.com/azuremigrate/wp-content/uploads/sites/113/2023/01/microsoft-logo.png
categories:
    - Architecture
tags:
    - design
    - nbgv
    - hatch
ai_note: This document was prepared with AI assistance.
summary: Design blueprint for implementing the nbgv-python package with hatchling integration and CLI delegation.
post_date: 2025-11-08
---

# nbgv-python Design Plan

## Functional Scope

- Expose a Python API for retrieving version metadata through the `nbgv` CLI and reusing the JSON payload in automation tasks.
- Provide a console script (`nbgv-python`) that forwards commands to `nbgv`, ensuring users can depend on the package even when the CLI is surfaced only as a local or global .NET tool.
- Deliver a Hatch `VersionSourceInterface` plugin so packages can declare `dynamic = ["version"]` and reuse Nerdbank.GitVersioning semantics during builds.
- Supply clear diagnostics and configuration options when the CLI cannot be located, aiding CI/CD automation.

## Module Breakdown

- `command.py`: discover the appropriate command invocation strategy (environment override, `nbgv` on PATH, or `dotnet tool run nbgv`).
- `runner.py`: implement the public API surface (`get_version()` and `forward()`) while funnelling all work through a shared subprocess helper.
- `models.py`: define `GitVersion` as an immutable view over CLI output, preserving both PascalCase and snake_case keys for ergonomic access.
- `versioning.py`: normalize version strings to PEP 440 so Hatch integration and consumers receive packaging-compatible values.
- `templating.py`: construct template fields (including version tuples) that mirror `versioningit`'s defaults.
- `writer.py`: render templates and persist version artifacts (e.g., `_version.py`) when requested by configuration.
- `hatch_plugin.py`: register the Hatch plugin, parse configuration from `[tool.hatch.version.nbgv]`, and map chosen fields to PEP 440-compatible versions.
- `cli.py`: implement the entry point that reuses `runner.forward()` to stream CLI output directly to the terminal.

## Data Contracts

- CLI JSON payload is preserved unmodified under `GitVersion.raw`, while `GitVersion.fields` holds snake_case aliases.
- Hatch configuration schema:
    - `command` (optional `str | list[str]`): override of the CLI invocation.
    - `version-field` (`str`, default `SemVer2`): which `GitVersion` attribute to emit as the package version.
    - `working-directory` (`str`, default project root): directory passed to the CLI for resolving repository metadata.
    - `epoch` (`int`, optional, default `null`): prepend a PEP 440 epoch (e.g., `2!`) to the normalized version when supplied; omit or set to `null` to skip the epoch.
    - `template-fields.version-tuple` (mapping): controls tuple generation for templated artifacts. Defaults to `mode = "nbgv"` with `fields = ["VersionMajor", "VersionMinor", "BuildNumber", "PrereleaseVersionNoLeadingHyphen"]` and `normalized-prerelease = false`; when set to `mode = "pep440"` the tuple is derived from the normalized PEP 440 version and honours the `epoch` toggle.

## Error Handling Strategy

- Missing CLI raises `NbgvNotFoundError` with suggestions for installing the dotnet tool or providing `command`/`NBGV_PYTHON_COMMAND` overrides.
- Non-zero exit statuses wrap `CalledProcessError` with captured stderr and the executed command for troubleshooting.
- JSON parsing errors raise `NbgvJsonError`, exposing the raw output to aid debugging of incompatible CLI versions.

## Testing Plan

- Use temporary Python scripts to emulate the `nbgv` CLI, allowing deterministic responses without depending on the .NET runtime.
- Cover command discovery (env override, explicit command list, fallback failure) and JSON parsing, including camelCase-to-snake_case mapping edge cases (e.g., `NuGetPackageVersion`).
- Validate Hatch plugin behaviour by exercising the configuration parsing helper with synthetic metadata and verifying version field selection logic.
- Exercise the CLI entry point via `CliRunner`-style invocation (e.g., `subprocess` with `sys.executable -m nbgv_python.cli`) to ensure exit codes propagate correctly.
