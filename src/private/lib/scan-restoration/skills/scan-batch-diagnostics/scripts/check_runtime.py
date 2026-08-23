from __future__ import annotations

import argparse
import importlib
import importlib.metadata
import json
import site
import sys
from pathlib import Path


REQUIRED_PYTHON = (3, 12, 13)
REQUIRED_DISTRIBUTIONS = {
    "imagecodecs": "2026.6.26",
    "numpy": "2.2.6",
    "opencv-python-headless": "4.12.0.88",
    "Pillow": "12.3.0",
    "tifffile": "2026.7.31",
}
REQUIRED_IMPORT_VERSIONS = {
    "imagecodecs": "2026.6.26",
    "numpy": "2.2.6",
    "cv2": "4.12.0",
    "PIL": "12.3.0",
    "tifffile": "2026.7.31",
}
OPENCV_DISTRIBUTIONS = {
    "opencv-python",
    "opencv-contrib-python",
    "opencv-python-headless",
    "opencv-contrib-python-headless",
}
SCRIPTS_DIRECTORY = Path(__file__).resolve().parent


def is_within(path: Path | None, parent: Path) -> bool:
    if path is None:
        return False
    try:
        path.resolve().relative_to(parent.resolve())
    except (OSError, ValueError):
        return False
    return True


def distribution_details(name: str, site_packages: Path) -> dict[str, object]:
    try:
        distribution = importlib.metadata.distribution(name)
    except importlib.metadata.PackageNotFoundError:
        return {"version": None, "origin": None, "inside_site_packages": False}
    origin = Path(distribution.locate_file("")).resolve()
    return {
        "version": distribution.version,
        "origin": str(origin),
        "inside_site_packages": is_within(origin, site_packages),
    }


def imported_module_details(name: str, site_packages: Path) -> dict[str, object]:
    try:
        module = importlib.import_module(name)
    except (ImportError, OSError) as error:
        return {
            "version": None,
            "origin": None,
            "inside_site_packages": False,
            "error": str(error),
        }
    origin_text = getattr(module, "__file__", None)
    origin = Path(origin_text).resolve() if origin_text else None
    return {
        "version": str(getattr(module, "__version__", "")) or None,
        "origin": str(origin) if origin else None,
        "inside_site_packages": is_within(origin, site_packages),
        "error": None,
    }


def readiness(expected_runtime: Path) -> dict[str, object]:
    expected_runtime = expected_runtime.absolute()
    site_packages = expected_runtime / "Lib" / "site-packages"
    distributions = {
        name: distribution_details(name, site_packages)
        for name in sorted(set(REQUIRED_DISTRIBUTIONS) | OPENCV_DISTRIBUTIONS)
    }
    imports = {
        name: imported_module_details(name, site_packages)
        for name in REQUIRED_IMPORT_VERSIONS
    }
    python_ok = sys.version_info[:3] == REQUIRED_PYTHON
    runtime_root = Path(sys.prefix)
    expected_python = expected_runtime / "Scripts" / "python.exe"
    runtime_root_ok = (
        expected_runtime.name.startswith(".runtime-")
        and expected_runtime.parent == SCRIPTS_DIRECTORY
        and runtime_root.resolve() == expected_runtime.resolve()
        and Path(sys.executable).resolve() == expected_python.resolve()
    )
    runtime_isolated = bool(sys.flags.isolated) and site.ENABLE_USER_SITE is False
    dependencies_ok = all(
        distributions[name]["version"] == required
        and distributions[name]["inside_site_packages"]
        for name, required in REQUIRED_DISTRIBUTIONS.items()
    )
    imports_ok = all(
        imports[name]["version"] == required
        and imports[name]["inside_site_packages"]
        for name, required in REQUIRED_IMPORT_VERSIONS.items()
    )
    conflicting_opencv_distributions = sorted(
        name
        for name in OPENCV_DISTRIBUTIONS - {"opencv-python-headless"}
        if distributions[name]["version"] is not None
    )
    opencv_conflicts_ok = not conflicting_opencv_distributions
    ready = all(
        (
            python_ok,
            runtime_root_ok,
            runtime_isolated,
            dependencies_ok,
            imports_ok,
            opencv_conflicts_ok,
        )
    )
    return {
        "ready": ready,
        "python": (
            f"{sys.version_info.major}.{sys.version_info.minor}."
            f"{sys.version_info.micro}"
        ),
        "python_ok": python_ok,
        "required_python": ".".join(map(str, REQUIRED_PYTHON)),
        "runtime_root": str(runtime_root.resolve()),
        "required_runtime_root": str(expected_runtime),
        "runtime_root_ok": runtime_root_ok,
        "installed_distributions": distributions,
        "required_distributions": REQUIRED_DISTRIBUTIONS,
        "dependencies_ok": dependencies_ok,
        "imported_modules": imports,
        "required_import_versions": REQUIRED_IMPORT_VERSIONS,
        "imports_ok": imports_ok,
        "conflicting_opencv_distributions": conflicting_opencv_distributions,
        "opencv_conflicts_ok": opencv_conflicts_ok,
        "runtime_isolated": runtime_isolated,
        "user_site_enabled": site.ENABLE_USER_SITE,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", type=Path, required=True)
    args = parser.parse_args()
    result = readiness(args.runtime)
    print(json.dumps(result, sort_keys=True))
    return 0 if result["ready"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
