from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ZERO_SHA = "0" * 40


def git(*args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )


def all_reachable_history(reason: str) -> str:
    print(f"{reason}; scanning all commits reachable from HEAD.", file=sys.stderr)
    return "HEAD"


def resolve_log_options() -> str:
    head = git("rev-parse", "HEAD").stdout.strip()
    explicit_base = os.environ.get("HK_BASE_REF")
    if explicit_base is not None:
        if not explicit_base or explicit_base == ZERO_SHA:
            return all_reachable_history("HK_BASE_REF does not identify a prior commit")
        if (
            git("rev-parse", "--verify", "--quiet", f"{explicit_base}^{{commit}}").returncode
            != 0
        ):
            return all_reachable_history(f"HK_BASE_REF {explicit_base!r} cannot be resolved")
        merge_base = git("merge-base", "HEAD", explicit_base)
        if merge_base.returncode != 0:
            return all_reachable_history(
                f"HK_BASE_REF {explicit_base!r} has no merge base with HEAD"
            )
        value = merge_base.stdout.strip()
        if not value or value == head:
            return all_reachable_history(
                f"HK_BASE_REF {explicit_base!r} does not define a prior commit range"
            )
        return f"{value}..HEAD"

    for candidate in ("main-v2", "origin/main-v2"):
        if git("rev-parse", "--verify", "--quiet", f"{candidate}^{{commit}}").returncode != 0:
            continue
        merge_base = git("merge-base", "HEAD", candidate)
        if merge_base.returncode == 0:
            value = merge_base.stdout.strip()
            if value and value != head:
                return f"{value}..HEAD"

    return all_reachable_history("No trusted base ref is available")


def main() -> int:
    log_options = resolve_log_options()
    return subprocess.run(
        [
            "gitleaks",
            "git",
            "--redact",
            "--no-banner",
            f"--log-opts=--diff-merges=first-parent {log_options}",
            str(ROOT),
        ],
        cwd=ROOT,
        check=False,
    ).returncode


if __name__ == "__main__":
    sys.exit(main())
