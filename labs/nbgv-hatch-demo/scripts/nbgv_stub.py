"""Minimal stub that emulates `nbgv get-version` for testing purposes."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass(slots=True)
class ParsedVersion:
    """Simple representation of a SemVer string."""

    release: str
    prerelease: str | None

    @property
    def semver1(self) -> str:
        if self.prerelease is None:
            return self.release
        return f"{self.release}-{self.prerelease.replace('.', '')}"

    @property
    def semver2(self) -> str:
        if self.prerelease is None:
            return self.release
        return f"{self.release}-{self.prerelease}"


def main(argv: list[str] | None = None) -> int:
    """Entry point for the stub command."""

    parser = _build_parser()
    args = parser.parse_args(argv)
    if args.command != "get-version":
        parser.error("only the get-version command is implemented")
    if args.format != "json":
        parser.error("only --format json is supported")
    payload = _load_payload(args.project)
    print(json.dumps(payload))
    return 0


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="nbgv", add_help=True)
    subparsers = parser.add_subparsers(dest="command")

    get_version = subparsers.add_parser("get-version")
    get_version.add_argument("--format", default="text")
    get_version.add_argument(
        "-p",
        "--project",
        dest="project",
        default=None,
        help="Path to the project directory (defaults to current working directory)",
    )
    return parser


def _load_payload(project: str | None) -> dict[str, object]:
    project_dir = Path(project or Path.cwd())
    version_json = _find_version_json(project_dir)
    if not version_json.exists():
        raise SystemExit("version.json not found")
    data = json.loads(version_json.read_text(encoding="utf-8"))
    raw_version = str(data.get("version", "0.0"))
    parsed = _parse_version(raw_version)
    release_parts = parsed.release.split(".")
    while len(release_parts) < 3:
        release_parts.append("0")
    release = ".".join(release_parts[:3])
    assembly_semver = release
    assembly_file_semver = f"{release}.0"
    informational = parsed.semver2

    commit = _detect_git_commit(version_json.parent)

    payload: dict[str, object] = {
        "SimpleVersion": parsed.semver2,
        "NuGetPackageVersion": parsed.semver1,
        "SemVer1": parsed.semver1,
        "SemVer2": parsed.semver2,
        "AssemblySemVer": assembly_semver,
        "AssemblyFileSemVer": assembly_file_semver,
        "AssemblyInformationalVersion": informational,
        "BuildMetadata": "",
        "VersionHeight": 0,
        "GitCommitId": commit,
        "GitCommitIdShort": commit[:7],
        "GitBranch": data.get("gitBranch", "main"),
        "PublicRelease": bool(data.get("publicRelease", False)),
    }
    return payload


def _find_version_json(project_dir: Path) -> Path:
    candidate = project_dir / "version.json"
    if candidate.exists():
        return candidate
    # Mirror nbgv behaviour: look for version.json in ancestors
    for parent in project_dir.parents:
        candidate = parent / "version.json"
        if candidate.exists():
            return candidate
    return project_dir / "version.json"


def _detect_git_commit(path: Path) -> str:
    command = ["git", "-C", str(path), "rev-parse", "HEAD"]
    try:
        result = _run_command(command)
    except OSError:
        return "0000000000000000000000000000000000000000"
    if result.returncode != 0 or not result.stdout:
        return "0000000000000000000000000000000000000000"
    return result.stdout.strip()


def _run_command(command: Iterable[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(  # noqa: S603,S607 - intentional stub helper
        tuple(command),
        check=False,
        capture_output=True,
        text=True,
    )


def _parse_version(value: str) -> ParsedVersion:
    if "-" in value:
        release, prerelease = value.split("-", 1)
        prerelease = prerelease or None
    else:
        release, prerelease = value, None
    return ParsedVersion(release=release, prerelease=prerelease)


if __name__ == "__main__":
    sys.exit(main())
