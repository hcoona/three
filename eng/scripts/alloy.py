"""Parse Alloy sources or solve bounded commands with the locked Analyzer."""

from __future__ import annotations

import argparse
import subprocess
import tempfile
from pathlib import Path

TOOL = "github:AlloyTools/org.alloytools.alloy"
JAR = "org.alloytools.alloy.dist.jar"


def main() -> int:
    """Keep parsing solver-free and solver output outside the worktree."""
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    parse = commands.add_parser("parse", help="Parse and type-check modules")
    parse.add_argument("models", nargs="+", type=Path)
    execute = commands.add_parser("exec", help="Run all commands in one model")
    execute.add_argument("model", type=Path)
    execute.add_argument("--output", type=Path, help="Retain solutions here")
    args = parser.parse_args()

    where = subprocess.run(
        ["mise", "where", TOOL],
        cwd=Path(__file__).resolve().parents[2],
        capture_output=True,
        text=True,
        check=False,
    )
    if where.returncode:
        parser.exit(
            2, f"Alloy unavailable; run 'mise install --locked {TOOL}'.\n"
        )
    with tempfile.TemporaryDirectory(prefix="alloy-") as temp:
        java = [
            "java",
            f"-Djava.io.tmpdir={temp}",
            "-jar",
            str(Path(where.stdout.strip()) / JAR),
        ]
        if args.command == "parse":
            for model in args.models:
                status = subprocess.run(
                    [*java, "commands", str(model.resolve())], check=False
                )
                if status.returncode:
                    return status.returncode
            return 0
        return subprocess.run(
            [
                *java,
                "exec",
                "--solver",
                "sat4j",
                "--command",
                "*",
                "--type",
                "xml" if args.output else "none",
                "--output",
                str(
                    args.output.resolve()
                    if args.output
                    else Path(temp) / "results"
                ),
                str(args.model.resolve()),
            ],
            check=False,
        ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
