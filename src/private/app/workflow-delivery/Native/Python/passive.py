"""Project passive native build requirements and plugin input coordinates."""

from __future__ import annotations

import argparse
import json
import sys
import tomllib
import warnings
from pathlib import Path


class UnsupportedContextError(ValueError):
    """Report physical request guards without source values."""


def _guard_uv_configuration(document: dict) -> dict:
    """Check physical table shapes and return the UV table uninterpreted."""
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
    return uv


def _source_kinds(uv: dict) -> dict[str, str]:
    """Classify each declared source as the admitted workspace shape or not."""
    from packaging.utils import canonicalize_name  # noqa: PLC0415

    sources = uv.get("sources", {})
    if not isinstance(sources, dict):
        message = "The UV sources must be a table."
        raise UnsupportedContextError(message)
    kinds: dict[str, str] = {}
    for name, source in sources.items():
        canonical = canonicalize_name(name)
        if canonical in kinds:
            message = "UV source names must be distinct."
            raise UnsupportedContextError(message)
        # Build planning admits only an exact local workspace reference.
        workspace = (
            isinstance(source, dict)
            and set(source) == {"workspace"}
            and source["workspace"] is True
        )
        kinds[canonical] = "workspace" if workspace else "unsupported"
    return kinds


def _parse_build_requirements(strings: set[str]) -> list[dict[str, object]]:
    """Parse PEP 508 strings with PyPA packaging and reject URL forms."""
    from packaging.requirements import Requirement  # noqa: PLC0415
    from packaging.utils import canonicalize_name  # noqa: PLC0415

    requirements = []
    for value in sorted(strings):
        requirement = Requirement(value)
        if requirement.url is not None:
            message = "Direct URL build requirements are unsupported."
            raise UnsupportedContextError(message)
        requirements.append(
            {
                "name": canonicalize_name(requirement.name),
                "extras": sorted(
                    {canonicalize_name(extra) for extra in requirement.extras}
                ),
            }
        )
    return requirements


def inspect_configuration(directories: list[Path]) -> dict[str, object]:
    """Preflight supplied contexts without native extraction."""
    inputs = []
    for directory in directories:
        root = directory.resolve(strict=True)
        manifest = root / "pyproject.toml"
        if str(manifest) in inputs:
            message = "Configuration contexts must be distinct."
            raise UnsupportedContextError(message)
        with manifest.open("rb") as stream:
            _guard_uv_configuration(tomllib.load(stream))
        inputs.append(str(manifest))
    return {"configuration_inputs": inputs}


def inspect_project(directory: Path) -> dict[str, object]:
    """Extract build requirements without backend or generator hooks."""
    root = directory.resolve(strict=True)
    with (root / "pyproject.toml").open("rb") as stream:
        document = tomllib.load(stream)
    sources = _source_kinds(_guard_uv_configuration(document))
    tool = document.get("tool", {})

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

        requirements = _parse_build_requirements(
            ProjectBuilder(root).build_system_requires
        )

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
        "sources": sources,
        "nbgv": coordinates,
    }


def main() -> int:
    """Return one complete response or a sanitized terminal diagnostic."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration-only", action="store_true")
    parser.add_argument("directory", type=Path, nargs="+")
    arguments = parser.parse_args()
    if not arguments.configuration_only and len(arguments.directory) != 1:
        parser.error("Project extraction requires one directory.")
    try:
        # Preserve native outcomes without forwarding source-valued warnings.
        with warnings.catch_warnings(record=True):
            response = (
                inspect_configuration(arguments.directory)
                if arguments.configuration_only
                else inspect_project(arguments.directory[0])
            )
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
