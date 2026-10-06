"""Prepare and evaluate the two native endpoints for root CI's control tests."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
from pathlib import Path

from ci_scope import CONTROL_PROJECT, ROOT, git, owner_present


def run(root: Path, *arguments: str) -> str:
    """Run one required native operation without repair or fallback."""
    return subprocess.run(
        arguments,
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
        timeout=300,
    ).stdout


def prepare(
    root: Path,
    directory: Path,
    application: Path,
    native_event: tuple[str, Path, str],
) -> dict:
    """Resolve the native comparison and materialize its exact Git objects."""
    if directory.is_relative_to(root):
        message = "Control endpoints must be outside the candidate source root"
        raise ValueError(message)
    comparison = json.loads(
        run(
            root,
            "dotnet",
            str(application),
            "ci",
            "comparison",
            native_event[0],
            str(native_event[1]),
            native_event[2],
        )
    )
    for name in ("basis", "candidate"):
        comparison[name] = git(
            root,
            "rev-parse",
            "--verify",
            "--end-of-options",
            comparison[name] + "^{commit}",
        ).strip()
    if comparison["candidate"] != git(root, "rev-parse", "HEAD").strip():
        message = "Native comparison does not name the checked-out candidate"
        raise ValueError(message)
    if comparison["full"] and comparison["basis"] != comparison["candidate"]:
        message = "Explicit full must retain one exact comparison identity"
        raise ValueError(message)
    before_present = owner_present(root, comparison["basis"])
    if not owner_present(root, comparison["candidate"]):
        message = "The candidate must retain the control test owner"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    after = directory / "candidate"
    run(
        root,
        "git",
        "worktree",
        "add",
        "--detach",
        str(after),
        comparison["candidate"],
    )
    if comparison["basis"] == comparison["candidate"]:
        before = after
    else:
        before = directory / "basis"
        run(
            root,
            "git",
            "worktree",
            "add",
            "--detach",
            str(before),
            comparison["basis"],
        )
    endpoints = {
        "full": comparison["full"],
        "basis": {"directory": str(before), "reference": comparison["basis"]},
        "candidate": {
            "directory": str(after),
            "reference": comparison["candidate"],
        },
    }
    (directory / "group-endpoints.json").write_text(
        json.dumps(endpoints, indent=2) + "\n", encoding="utf-8"
    )
    request = {
        "comparison": comparison,
        "repository": str(root),
        "basisDirectory": str(before) if before_present else None,
        "candidateDirectory": str(after),
    }
    request_path = directory / "request.json"
    request_path.write_text(
        json.dumps(request, indent=2) + "\n", encoding="utf-8"
    )
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(
                f"request={request_path}\ncandidate_directory={after}\n"
            )
            stream.write(f"basis_directory={before}\n")
            separate = str(before is not None and before != after).lower()
            stream.write(f"basis_separate={separate}\n")
            stream.write(
                f"basis={comparison['basis']}\ncandidate={comparison['candidate']}\n"
            )
            stream.write(f"full={str(comparison['full']).lower()}\n")
    return request


def evaluate(
    root: Path, application: Path, request_path: Path, response_path: Path
) -> dict:
    """Use each endpoint's selected SDK and locked reference preparation."""
    request = json.loads(request_path.read_text(encoding="utf-8"))
    directories = dict.fromkeys(
        item
        for item in (request["basisDirectory"], request["candidateDirectory"])
        if item is not None
    )
    for directory in directories:
        print(
            run(
                Path(directory),
                "dotnet",
                "restore",
                CONTROL_PROJECT,
                "--locked-mode",
                "-p:Configuration=Debug",
            ),
            end="",
        )
    response = json.loads(
        run(
            root,
            "dotnet",
            str(application),
            "ci",
            "control-inputs",
            str(request_path),
        )
    )
    response_path.parent.mkdir(parents=True, exist_ok=True)
    response_path.write_text(
        json.dumps(response, indent=2) + "\n", encoding="utf-8"
    )
    return response


def main() -> int:
    """Prepare native facts before ci_scope emits final applicability."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--application", type=Path, required=True)
    commands = parser.add_subparsers(dest="command", required=True)
    materialize = commands.add_parser("prepare")
    materialize.add_argument("--directory", type=Path, required=True)
    materialize.add_argument("--event-name", required=True)
    materialize.add_argument("--event-path", type=Path, required=True)
    materialize.add_argument("--candidate", required=True)
    query = commands.add_parser("evaluate")
    query.add_argument("--request", type=Path, required=True)
    query.add_argument("--response", type=Path, required=True)
    options = parser.parse_args()
    root = options.repository.resolve()
    application = options.application.resolve()
    if options.command == "prepare":
        prepare(
            root,
            options.directory.resolve(),
            application,
            (
                options.event_name,
                options.event_path.resolve(),
                options.candidate,
            ),
        )
    else:
        evaluate(
            root,
            application,
            options.request.resolve(),
            options.response.resolve(),
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
