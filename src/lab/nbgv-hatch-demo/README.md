# nbgv Hatch Demo

## Overview

- Illustrates how to configure Hatch to source package versions from `nbgv-python`.
- Uses a local `version.json` compatible with Nerdbank.GitVersioning to describe the version recipe.
- Requires the `nbgv` CLI to be available on `PATH`; a lightweight stub is included for offline experiments.

## Quickstart

1. Synchronize the workspace environment: `uv sync --package nbgv-hatch-demo`.
2. Inspect the calculated version: `uv run --package nbgv-hatch-demo hatch version`.
3. Observe the generated `src/nbgv_hatch_demo/_version.py` after running the command above.
4. Import `nbgv_hatch_demo` to consume the dynamic version at runtime.
5. Access `nbgv_hatch_demo.VERSION_TUPLE` or call `nbgv_hatch_demo.pep440_version("2.0.0-rc.1")` to inspect the templated helpers.

## Project Layout

- `pyproject.toml`: Declares Hatch build metadata and the `nbgv` version source configuration.
- `version.json`: Defines the Nerdbank.GitVersioning baseline version (`1.2.3-beta.1`).
- `scripts/nbgv_stub.py`: Optional helper that emulates `nbgv get-version` when the real CLI is unavailable.
- `src/nbgv_hatch_demo/_version.py`: Generated at build time using the custom template in `pyproject.toml`.
- `src/nbgv_hatch_demo/__init__.py`: Re-exports the generated constants and provides a convenience `pep440_version()` function.

## Notes

- Ensure `nbgv` (the Nerdbank.GitVersioning CLI) is installed; the plugin auto-discovers it from `PATH` or via `dotnet tool run`.
- To force the stub, set `NBGV_PYTHON_COMMAND="python scripts/nbgv_stub.py"` before invoking Hatch.
- The custom template renders raw Nerdbank fields (`{SimpleVersion}`, `{SemVer2}`, `{GitCommitId}`), the PEP 440-normalized value, and a tuple representation (`{version_tuple}`).
