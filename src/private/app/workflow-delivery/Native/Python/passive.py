"""Project passive native build strings and plugin input coordinates."""

from __future__ import annotations

import argparse
import json
import sys
import tomllib
import warnings
from pathlib import Path


class UnsupportedContextError(ValueError):
    """Report physical request guards without source values."""


def inspect_project(directory: Path) -> dict[str, object]:
    """Keep native strings opaque without backend or generator execution."""
    root = directory.resolve(strict=True)
    with (root / "pyproject.toml").open("rb") as stream:
        document = tomllib.load(stream)
    tool = document.get("tool", {})
    if not isinstance(tool, dict):
        message = "The tool table must be a table."
        raise UnsupportedContextError(message)
    uv = tool.get("uv", {})
    if not isinstance(uv, dict):
        message = "The UV configuration must be a table."
        raise UnsupportedContextError(message)
    if set(uv) - {"workspace", "sources"}:
        message = "The requested UV settings need native adaptation."
        raise UnsupportedContextError(message)

    requirements = None
    if "build-system" in document:
        project = document.get("project", {})
        build = document["build-system"]
        if (
            not isinstance(project, dict)
            or not isinstance(project.get("name"), str)
            or not project["name"].strip()
            or not isinstance(build, dict)
            or build.get("build-backend") != "hatchling.build"
            or "backend-path" in build
        ):
            message = "Only explicit named Hatchling contexts are supported."
            raise UnsupportedContextError(message)
        from build import ProjectBuilder  # noqa: PLC0415

        requirements = sorted(ProjectBuilder(root).build_system_requires)

    coordinates = None
    hatch = tool.get("hatch", {})
    if not isinstance(hatch, dict):
        message = "The Hatch configuration must be a table."
        raise UnsupportedContextError(message)
    version = hatch.get("version", {})
    if not isinstance(version, dict):
        message = "The Hatch version configuration must be a table."
        raise UnsupportedContextError(message)
    if version.get("source") == "nbgv":
        from nbgv_python.config import PluginConfig  # noqa: PLC0415

        config = PluginConfig.from_mapping(root, version.get("nbgv"))
        coordinates = {
            "working_directory": str(config.working_directory),
            "generated_file": str(config.write.file) if config.write else None,
        }
    return {
        "directory": str(root),
        "build_requirements": requirements,
        "nbgv": coordinates,
    }


def main() -> int:
    """Return one complete response or a sanitized terminal diagnostic."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    arguments = parser.parse_args()
    try:
        # Preserve native outcomes without forwarding source-valued warnings.
        with warnings.catch_warnings(record=True):
            response = inspect_project(arguments.directory)
    except UnsupportedContextError as error:
        print(f"Unsupported Python context: {error}", file=sys.stderr)  # noqa: T201
        return 1
    except Exception as error:  # noqa: BLE001
        # Native diagnostics may contain source values; report the type only.
        print(  # noqa: T201
            f"Passive extraction failed ({type(error).__name__}).",
            file=sys.stderr,
        )
        return 1
    print(  # noqa: T201
        json.dumps(response, ensure_ascii=False, allow_nan=False)
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
