# nbgv-python Architecture Overview

This record owns the library's component boundaries and CLI-delegation rationale.
Library contributors produce and maintain it when those boundaries change;
implementers and reviewers consume it before changing the wrapper or Hatch
adapter. The package [README](../../README.md) owns usage guidance. Keeping the
internal rationale here avoids making package readers reconstruct the design
from source or treating the separate sample as the library specification.

This record reconciles the original [architecture record][original-architecture]
and [design plan][original-design] with code and tests at Three commit
`d4c30ceba97190f4771141bc0200e5326ac3fce7`. Both originals identify GitHub Copilot
as author, disclose AI assistance, and carry the date 2025-11-08. These are their
provenance statements, not evidence that the designs were independently reviewed.
Product or runtime work requires its own accepted Delivery Wave grant.

## Context

- Nerdbank.GitVersioning supplies version metadata through the `nbgv` .NET CLI.
  Version retrieval consumes structured JSON; forwarding other commands can
  have effects, such as the original design's `set-cloud-build-version` example.
- The original architecture cites `nerdbank-gitversioning.npm` as the model for
  bundling and invoking the CLI through Node subprocess APIs, keeping version
  semantics in NBGV. This is retained design context, not a newly verified claim
  of parity with that wrapper or a claim that this Python package bundles NBGV.
- It cites `versioningit` as the model for a Hatch `VersionSourceInterface`
  adapter backed by reusable core modules. That analogy explains the separation
  of concerns; it does not establish complete compatibility with `versioningit`.

## Architectural Principles

- Reuse the `nbgv` CLI for version calculations rather than reimplementing Git
  parsing in Python. Python-specific PEP 440 normalization remains local.
- Share command discovery and invocation between library APIs, the console
  script, and Hatch integration so fixes reach the same execution path.
- Keep the JSON representation thin and retain original names alongside Python
  aliases. The original immutability and attribute-access expectations need the
  qualifications under [Data Contracts](#data-contracts).
- Expose a Hatch version source for dynamic package versions without requiring
  consumers to write their own CLI adapter.

## Key Components

| Component                                     | Responsibility and implementation                                                                                                                                                                                                                       |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `discover_command`, `parse_command_tokens`    | [command.py](../../src/nbgv_python/command.py) resolves explicit configuration, `NBGV_PYTHON_COMMAND`, `nbgv` on `PATH`, then `dotnet tool run nbgv`, in that order. `CommandLocator` was a design name; there is no class by that name.                |
| `NbgvRunner`                                  | [runner.py](../../src/nbgv_python/runner.py) constructs subprocess arguments, retrieves JSON, forwards commands, and reports non-zero exits. Delegation avoids a second NBGV version engine; it does not prove parity for every environment or command. |
| `GitVersion`                                  | [models.py](../../src/nbgv_python/models.py) provides a mapping over original keys and snake_case aliases.                                                                                                                                              |
| `PluginConfig`, `NbgvVersionSource`           | [config.py](../../src/nbgv_python/config.py) parses configuration; [hatch_plugin.py](../../src/nbgv_python/hatch_plugin.py) selects and normalizes a version, builds template fields, and optionally writes a file.                                     |
| `normalize_version_field`                     | [versioning.py](../../src/nbgv_python/versioning.py) uses `packaging.version.Version` and a SemVer conversion fallback for Python packaging.                                                                                                            |
| `build_template_fields`, `write_version_file` | [templating.py](../../src/nbgv_python/templating.py) constructs the template mapping and tuple text; [writer.py](../../src/nbgv_python/writer.py) renders the artifact. `TemplateFieldsBuilder` was a design name, not an implemented class.            |
| Public helpers and console script             | [**init**.py](../../src/nbgv_python/__init__.py) exports reusable APIs; [cli.py](../../src/nbgv_python/cli.py) forwards arguments and returns exit codes.                                                                                               |

## Data & Control Flow

1. Hatch parses `[tool.hatch.version.nbgv]` through `PluginConfig` and creates
   `NbgvRunner`. Public `get_version()` and `forward()` helpers create the same
   runner; the console entry point uses its forwarding path.
2. `get_version(project_dir)` resolves an absolute project path, invokes
   `nbgv get-version --format json --project <path>`, and parses the response
   into `GitVersion`. It keeps the caller's working directory. This supersedes
   the original description of running the subprocess within the project root;
   `test_get_version_uses_absolute_project_path_without_changing_cwd` in
   [test_runner.py](../../tests/test_runner.py) preserves the path-duplication
   regression rationale also recorded in the [2.0.0 changelog](../../CHANGELOG.md#200---2025-12-18).
3. Hatch selects the configured field, normalizes it for packaging, optionally
   prefixes the configured epoch, creates template fields, and writes the
   configured artifact during version resolution. It returns `version`,
   `metadata`, and `template_fields` and exposes `get_template_fields()`.
4. Forwarding streams output instead of capturing it. Public `forward()` maps
   `project_dir` to subprocess `cwd`; version retrieval instead passes
   `--project`. Neither interface restricts which underlying CLI commands a
   caller can request.

## Data Contracts

`GitVersion.from_payload()` copies a mapping into `raw`, stringifying keys, and
builds snake_case aliases with special handling for `NuGet` and `SemVer`.
Dictionary access, `get()`, `require()`, iteration, length, and `as_dict()` are
implemented. [test_models.py](../../tests/test_models.py) checks aliases,
missing-key errors, combined dictionaries, and iteration over both key forms.

The original architecture promises an immutable view with attribute access,
and the design plan names `GitVersion.fields`. The current dataclass is frozen,
but its dictionaries remain mutable; there is no public `fields` property or
dynamic attribute lookup. `as_dict()` supplies a copy of aliases. Whether deep
immutability or attribute/`fields` access is still required remains unresolved.
This reconciliation does not withdraw those design expectations or authorize an
API change. Consumers should use the implemented mapping interfaces described
above rather than infer an API from the original sketches.

The original `ensure_placeholder_version()` helper example has no definition or
export in this package. Its intended behavior is not established by current
code or tests. It remains an unresolved design example, not a usable API or an
authorized feature request.

## External Integration Points

[pyproject.toml](../../pyproject.toml) registers the module through the `hatch`
entry-point group. The module's `hatch_register_version_source()` hook returns
`NbgvVersionSource`; the original `hatchling.version` entry-point description
does not match the manifest. Consumers configure `source = "nbgv"` and dynamic
versions as documented in the [README](../../README.md#hatch-integration).

The wrapper requires an external NBGV command. Finding `dotnet` only selects a
fallback invocation; it does not confirm a local tool manifest or installation.
See the [Hatch integration design](hatch-integration.md) for error boundaries
and validation limits. The library's own manifest uses a checked-in static
version; supplying a dynamic version source to consumers does not change that
package metadata or establish release support.

The original suggestion of future template propagation and on-build hooks is
partly realized by `get_template_fields()` and configured file writing. A
separate build-hook plugin is not registered. No additional hook work or
compatibility commitment follows from that open-ended suggestion; its history
remains in the original record.

[original-architecture]: https://github.com/hcoona/three/blob/d4c30ceba97190f4771141bc0200e5326ac3fce7/src/public/lib/nbgv-python/.copilot/ARCHITECTURE.md
[original-design]: https://github.com/hcoona/three/blob/d4c30ceba97190f4771141bc0200e5326ac3fce7/src/public/lib/nbgv-python/.copilot/DESIGN.md
