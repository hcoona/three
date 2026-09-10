# nbgv Hatch Demo

## Overview

- Illustrates how to configure Hatch to source package versions from [nbgv-python](https://github.com/hcoona/three/blob/main/src/public/lib/nbgv-python/README.md).
- Uses a local `version.json` compatible with Nerdbank.GitVersioning to describe the version recipe.
- Uses the library's command discovery for the external `nbgv` CLI. A lightweight stub is included, with the limitations described below.

This README owns sample usage. The library's [architecture](https://github.com/hcoona/three/blob/main/src/public/lib/nbgv-python/docs/architecture/overview.md) and [Hatch integration design](https://github.com/hcoona/three/blob/main/src/public/lib/nbgv-python/docs/architecture/hatch-integration.md) own its implementation rationale and evidence limits. The sample does not establish a library support or publication promise.

## Quickstart

Run these commands from `src/sample/nbgv-hatch-demo` within the repository so Hatch selects this sample's configuration. Ensure the real NBGV CLI is available before synchronizing, since package metadata resolution can invoke it.

1. Synchronize the workspace environment: `uv sync --package nbgv-hatch-demo`.
2. Inspect the calculated version: `uv run --package nbgv-hatch-demo hatch version`.
3. Observe the generated `src/nbgv_hatch_demo/_version.py` after running the command above.
4. Import `nbgv_hatch_demo` to consume the dynamic version at runtime.
5. Access `nbgv_hatch_demo.VERSION_TUPLE` or call `nbgv_hatch_demo.pep440_version("2.0.0-rc.1")` to inspect the templated helpers.

## Project Layout

- `pyproject.toml`: Declares Hatch build metadata and the `nbgv` version source configuration.
- `version.json`: Defines the current Nerdbank.GitVersioning recipe (`1.2.3-dev.{height}` with inheritance). The earlier README's `1.2.3-beta.1` baseline no longer matches this file.
- `scripts/nbgv_stub.py`: Limited helper that emulates part of `nbgv get-version`; it is not a substitute for the current sample's real NBGV invocation.
- `src/nbgv_hatch_demo/_version.py`: Generated at build time using the custom template in `pyproject.toml`.
- `src/nbgv_hatch_demo/__init__.py`: Re-exports the generated constants and provides a convenience `pep440_version()` function. Without the generated module, it falls back to installed metadata or `0.0.0`, and `pep440_version()` returns its input unchanged.

## Notes

- Ensure `nbgv` (the Nerdbank.GitVersioning CLI) is installed; the plugin auto-discovers it from `PATH` or via `dotnet tool run`.
- The earlier stub override, `NBGV_PYTHON_COMMAND="python scripts/nbgv_stub.py"`, resolves the script relative to the caller's working directory. Although the override selects the helper, it cannot currently complete the default sample configuration: the helper leaves `{height}` literal in the current version recipe and omits the default tuple fields `VersionMajor`, `VersionMinor`, `BuildNumber`, and `PrereleaseVersionNoLeadingHyphen`. These limits are visible in [the stub](scripts/nbgv_stub.py), [version.json](version.json), and [the sample configuration](pyproject.toml); no successful offline sample build is claimed.
- The custom template renders raw Nerdbank fields (`{SimpleVersion}`, `{SemVer2}`, `{GitCommitId}`), the PEP 440-normalized value, and a tuple representation (`{version_tuple}`).
- The stub's intended offline use remains unresolved for this recipe. Changing the stub or configuration requires separately authorized product work; this documentation migration preserves both files.
