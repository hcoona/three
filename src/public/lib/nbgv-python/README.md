# nbgv-python Package Overview

## Overview

- `nbgv-python` wraps the Nerdbank.GitVersioning CLI so Python projects can reuse the same versioning semantics.
- The package discovers the CLI via `NBGV_PYTHON_COMMAND`, a direct `nbgv` executable, or `dotnet tool run nbgv` as a fallback.
- A Hatch version source plugin is provided, allowing projects to declare `dynamic = ["version"]` and resolve their version at build time.

## Installation

- Install the package into your monorepo environment using `uv add --package nbgv-python nbgv-python`.
- Ensure the `nbgv` CLI is available either as a global .NET tool (`dotnet tool install -g nbgv`) or via a local tool manifest.
- Optionally set `NBGV_PYTHON_COMMAND` when the executable lives outside of `PATH` or requires additional arguments.

## Hatch Integration

Add the plugin to your project configuration:

```toml
[project]
dynamic = ["version"]

[tool.hatch.version]
source = "nbgv"

[tool.hatch.version.nbgv]
version-field = "SemVer2"
```

During builds Hatch invokes `nbgv get-version --format json` in the project root and uses the selected field for the package version.
Additional configuration keys include:

- `command` (override CLI invocation)
- `working-directory` (relative path for the repository root)
- `epoch` _(default: `null`)_ – when provided, this non-negative integer is prepended to the normalized version to emit a PEP 440 epoch (for example `2!1.2.3`). Leave it unset or `null` to omit the epoch entirely.

The plugin normalizes the chosen value to a PEP 440 compliant version, so SemVer pre-release tags such as `-beta.1` are mapped to `b1` automatically.

### Template Fields

- Extend the available template variables via the optional `template-fields` table. Supported options and defaults are:
    - `version-tuple.mode` _(default: `"nbgv"`)_ – choose how to build the tuple. Use `"nbgv"` (default) to assemble components from the Nerdbank.GitVersioning JSON payload, or `"pep440"` to parse the normalized version string.
    - `version-tuple.fields` _(default: `["VersionMajor", "VersionMinor", "BuildNumber", "PrereleaseVersionNoLeadingHyphen"]`)_ – when `mode = "nbgv"`, this ordered list determines which NBGV variables populate the tuple. Empty strings and `null` values are skipped.
    - `version-tuple.normalized-prerelease` _(default: `false`)_ – when `mode = "nbgv"`, convert the `PrereleaseVersionNoLeadingHyphen` entry to its PEP 440 shorthand (for example `beta.1` → `b1`).
    - `version-tuple.epoch` _(default: `null`)_ – only applies when `mode = "pep440"`. Keep it `null` to emit the epoch only when the source string already includes a non-zero epoch (e.g., `2!1.0.0`), set `true` to force inclusion, or `false` to suppress it.
    - `version-tuple.double-quote` _(default: `true`)_ – controls whether string parts in the tuple use double quotes (`""`) or single quotes (`''`).

    ```toml
    [tool.hatch.version.nbgv.template-fields.version-tuple]
    fields = [
      "VersionMajor",
      "VersionMinor",
      "BuildNumber",
      "PrereleaseVersionNoLeadingHyphen",
    ]
    double-quote = false
    ```

    ```toml
    [tool.hatch.version.nbgv.template-fields.version-tuple]
    mode = "pep440"
    epoch = true
    ```

    ```toml
    [tool.hatch.version.nbgv.template-fields.version-tuple]
    normalized-prerelease = true
    ```

    ```toml
    [tool.hatch.version.nbgv]
    epoch = 2
    ```

- The generated mapping includes `version`, `normalized_version`, `version_tuple`, and every key exposed by `nbgv get-version`.
    - `version` relays the raw value selected by `version-field` (defaults to `SemVer2`). Use this if you need the exact string returned by NBGV (e.g. `1.2.3-beta.1`).
    - `normalized_version` applies PEP 440 normalization to that value using `packaging.version.Version` so `1.2.3-beta.1` becomes `1.2.3b1`. Use this for Python `__version__` strings.
    - `version_tuple` expands into a Python tuple using either the selected NBGV variables (`mode = "nbgv"`, respecting `normalized-prerelease` when enabled) or the parsed PEP 440 segments (`mode = "pep440"`).

### Writing Version Files

- Emit a templated artifact (for example `src/pkg/_version.py`) during build. The `write` table accepts the following keys:
    - `file` _(required)_ – relative or absolute path of the generated file.
    - `template` _(optional)_ – custom format string. When omitted, `.py` files default to `__version__ = "{normalized_version}"` when that field is available (otherwise `version` is used), while `.txt` or extension-less files default to `{version}`; other suffixes require an explicit template.
    - `encoding` _(optional, default: `"utf-8"`)_ – text encoding used when writing the file.

    ```toml
    [tool.hatch.version.nbgv.write]
    file = "src/pkg/_version.py"
    template = "__version__ = '{normalized_version}'"
    ```

- When `template` is omitted the plugin mirrors `versioningit`: `.py` files render `__version__ = "{normalized_version}"` (falling back to `version` if normalization was not provided), while `.txt` or extension-less files render `{version}`.
- The target path is resolved relative to the project root and created on demand; remember to include it in your source distribution configuration if needed.

## Python API

Retrieve version metadata directly from Python code:

```python
from nbgv_python import get_version

version = get_version()
print(version["semver2"])  # -> 1.2.3+gabcdef
```

Forward arbitrary commands to the CLI:

```python
from nbgv_python import forward

forward(["cloud", "--ci"])
```

All helpers raise `NbgvNotFoundError` when the CLI cannot be resolved and `NbgvCommandError` for non-zero exit codes.

## Command-Line Usage

The console script `nbgv-python` proxies all arguments to the real CLI:

```bash
nbgv-python get-version --format json
```

The wrapper surfaces the same exit codes as the underlying tool and prints diagnostic guidance when the command cannot be located.
