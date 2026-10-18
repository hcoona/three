"""Check CiScope's fixed HK step copies against the evaluated hk.pkl."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
COPIES = Path(
    "src/private/app/workflow-delivery/Repository/resources/hk-scope-copies.json"
)
CI_WORKFLOW = Path(".github/workflows/ci.yml")
PKL = ("mise", "exec", "--locked", "pkl", "--", "pkl", "eval", "-f", "json")


GLOB_CHARACTERS = frozenset("*?[]{}")


def _has_glob(text: str) -> bool:
    return any(character in GLOB_CHARACTERS for character in text)


def can_exclude(pattern: str, suffixes: list[str]) -> bool:
    """Conservatively report whether an exclusion can match a copied file.

    Only the final path segment is read. ``**`` is relevant. A literal
    segment is relevant only when it ends with a copied suffix. A segment with
    glob characters is relevant unless the text after its last ``.`` is a
    literal ending that matches no copied suffix.
    """
    last = pattern.rsplit("/", 1)[-1]
    if last == "**":
        return True
    if not _has_glob(last):
        return last.endswith(tuple(suffixes))
    if any(PurePosixPath("x" + suffix).full_match(last) for suffix in suffixes):
        return True
    dot = last.rfind(".")
    tail = last[dot:] if dot >= 0 else None
    if tail is None or _has_glob(tail):
        return True
    return tail in suffixes


def ci_profiles(text: str) -> set[str]:
    """Return the HK profiles that ci.yml's hk check commands run."""
    return {
        profile
        for line in text.splitlines()
        if re.search(r"\bhk\b.*\bcheck\b", line)
        for profile in re.findall(r"--profile (\w+)", line)
    }


def drift(
    steps: dict[str, dict], rules: dict[str, dict], profiles: set[str]
) -> list[str]:
    """Return every difference between the copied rules and the HK steps."""
    errors = []
    if not profiles:
        errors.append(f"{CI_WORKFLOW} runs no HK check profile.")
    for rule, copy in rules.items():
        suffixes = copy["suffixes"]
        directory = copy["directory"]
        for name in copy["steps"]:
            step = steps.get(name)
            if step is None:
                errors.append(f"{rule}: HK check step {name} is missing.")
                continue
            if not set(step["profiles"]) & profiles:
                errors.append(f"{rule}: {name} is in no profile ci.yml runs.")
            for suffix in suffixes:
                sample = PurePosixPath(directory or "", "x" + suffix)
                if not any(sample.full_match(glob) for glob in step["glob"]):
                    errors.append(
                        f"{rule}: {name} glob no longer matches {sample}."
                    )
            relevant = {
                pattern
                for pattern in step["exclude"]
                if can_exclude(pattern, suffixes)
            }
            if relevant != set(copy["exclusions"]):
                errors.append(
                    f"{rule}: {name} exclusions {sorted(relevant)} differ from "
                    f"the copy {sorted(copy['exclusions'])}."
                )
    return errors


def main() -> int:
    """Compare the copies with root hk.pkl evaluated by the locked Pkl."""
    evaluated = subprocess.run(
        [*PKL, "hk.pkl"],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if evaluated.returncode:
        sys.stderr.write(evaluated.stderr)
        return evaluated.returncode
    steps = json.loads(evaluated.stdout)["hooks"]["check"]["steps"]
    rules = json.loads((ROOT / COPIES).read_text(encoding="utf-8"))["rules"]
    profiles = ci_profiles((ROOT / CI_WORKFLOW).read_text(encoding="utf-8"))
    errors = drift(steps, rules, profiles)
    for error in errors:
        print(error, file=sys.stderr)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
