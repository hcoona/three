# /// script
# requires-python = ">=3.14"
# dependencies = []
# ///

"""Verify built artifact versions match expected version."""

from __future__ import annotations

import argparse
import io
import sys
import tarfile
import zipfile
from pathlib import Path


def get_wheel_version(path: Path) -> str:
    """Extract version from a wheel file."""
    with zipfile.ZipFile(path) as zf:
        meta = next((n for n in zf.namelist() if n.endswith("METADATA")), None)
        if not meta:
            sys.exit(f"{path.name} is missing METADATA")
        content = zf.read(meta).decode()
        for line in content.splitlines():
            if line.startswith("Version:"):
                return line.split(":", 1)[1].strip()
    sys.exit(f"Version not found in {path.name}")


def get_sdist_version(path: Path) -> str:
    """Extract version from a source distribution file."""
    with tarfile.open(path, mode="r:gz") as tf:
        member = next(
            (m for m in tf.getmembers() if m.name.endswith("PKG-INFO")),
            None,
        )
        if member is None:
            sys.exit(f"{path.name} is missing PKG-INFO")

        extracted = tf.extractfile(member)
        if extracted is None:
            sys.exit(f"Could not extract PKG-INFO from {path.name}")

        with extracted as fh:
            for line in io.TextIOWrapper(fh, encoding="utf-8"):
                if line.startswith("Version:"):
                    return line.split(":", 1)[1].strip()
    sys.exit(f"Version not found in {path.name}")


def main() -> None:
    """Verify built artifact versions match expected version."""
    parser = argparse.ArgumentParser(
        description="Verify built artifact versions match expected version."
    )
    parser.add_argument("expected_version", help="The expected version string")
    parser.add_argument(
        "dist_dir", type=Path, help="Directory containing build artifacts"
    )
    args = parser.parse_args()

    expected: str = args.expected_version
    dist: Path = args.dist_dir

    if not dist.exists():
        sys.exit(f"Dist directory not found: {dist}")

    artifacts = sorted(dist.glob("*"))
    # Filter out directories or non-files if necessary,
    # though glob("*") gets everything.
    artifacts = [f for f in artifacts if f.is_file() and f.name != ".gitignore"]

    if not artifacts:
        sys.exit(f"No build artifacts found in {dist}")

    versions: set[str] = set()
    expected_types = {"wheel (.whl)", "sdist (.tar.gz)"}

    for path in artifacts:
        name = path.name

        if path.suffix == ".whl":
            versions.add(get_wheel_version(path))
        elif path.name.endswith(".tar.gz"):
            versions.add(get_sdist_version(path))
        else:
            sys.exit(
                "Unknown artifact type: "
                f"{name}. Expected one of {sorted(expected_types)}. "
                f"All artifacts: {[p.name for p in artifacts]}"
            )

    if versions != {expected}:
        sys.exit(
            f"Built artifact version mismatch: expected {expected}"
            f", got {sorted(versions)}"
        )

    print(f"Verified {len(artifacts)} artifacts match version {expected}")


if __name__ == "__main__":
    main()
