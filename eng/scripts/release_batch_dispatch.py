# /// script
# requires-python = ">=3.13"
# dependencies = ["PyYAML==6.0.2"]
# ///
"""Dispatch one ordinary per-unit Release caller run per entry and await them.

Each entry is ``CALLER:INPUT=VALUE,...`` and must give every declared
``workflow_dispatch`` input of
``.github/workflows/workflow-delivery-v3-CALLER-release.yml``, including
``dry-run``. All entries are validated before any dispatch. Every run keeps its
own request, Environment approval and completion; one failure does not stop the
others.
"""

from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"
PREFIX = "workflow-delivery-v3-"
SUFFIX = "-release.yml"
REPO = "repos/{owner}/{repo}"


class EntryError(ValueError):
    """Raised for an entry that does not match its caller's inputs."""


@dataclass
class Run:
    """One dispatched per-unit request and its observed outcome."""

    caller: str
    workflow: str
    inputs: dict[str, str]
    run_id: int | None = None
    url: str = ""
    status: str = ""
    conclusion: str = ""
    detail: list[str] = field(default_factory=list)

    @property
    def label(self) -> str:
        """Describe the entry as given."""
        values = ",".join(
            f"{key}={value}" for key, value in self.inputs.items()
        )
        return f"{self.caller}:{values}"


def callers() -> dict[str, dict]:
    """Read the declared dispatch inputs of each Release caller."""
    found = {}
    for path in sorted(WORKFLOWS.glob(f"{PREFIX}*{SUFFIX}")):
        workflow = yaml.safe_load(path.read_text(encoding="utf-8"))
        # PyYAML reads the bare `on` key as boolean true.
        triggers = workflow.get(True) or workflow.get("on") or {}
        found[path.name[len(PREFIX) : -len(SUFFIX)]] = triggers[
            "workflow_dispatch"
        ]["inputs"]
    return found


def parse_entry(text: str, known: dict[str, dict]) -> Run:
    """Validate one entry against its caller's declared inputs."""
    caller, separator, rest = text.partition(":")
    if caller not in known:
        message = f"{text!r}: unknown caller; expected one of {sorted(known)}"
        raise EntryError(message)
    if not separator or not rest:
        message = f"{text!r}: expected CALLER:INPUT=VALUE,..."
        raise EntryError(message)
    declared = known[caller]
    inputs: dict[str, str] = {}
    for pair in rest.split(","):
        key, equals, value = pair.partition("=")
        if not equals or not key or not value:
            message = f"{text!r}: expected INPUT=VALUE, got {pair!r}"
            raise EntryError(message)
        if key in inputs:
            message = f"{text!r}: duplicate input {key!r}"
            raise EntryError(message)
        if key not in declared:
            message = f"{text!r}: {caller} has no input {key!r}"
            raise EntryError(message)
        spec = declared[key]
        if spec.get("type") == "choice" and value not in spec["options"]:
            message = f"{text!r}: {key} must be one of {spec['options']}"
            raise EntryError(message)
        if spec.get("type") == "boolean" and value not in {"true", "false"}:
            message = f"{text!r}: {key} must be true or false"
            raise EntryError(message)
        inputs[key] = value
    # Defaults are not applied: every input, including dry-run, is explicit.
    missing = [key for key in declared if key not in inputs]
    if missing:
        message = f"{text!r}: missing inputs {missing}"
        raise EntryError(message)
    return Run(caller, f"{PREFIX}{caller}{SUFFIX}", inputs)


def gh(*arguments: str, body: dict | None = None) -> dict:
    """Call the GitHub API through gh and return its JSON response."""
    completed = subprocess.run(
        ["gh", "api", *arguments],
        input=None if body is None else json.dumps(body),
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or completed.stdout.strip())
    return json.loads(completed.stdout)


def dispatch(run: Run, ref: str) -> None:
    """Create the run and record the identity GitHub returns for it."""
    try:
        response = gh(
            "--method",
            "POST",
            f"{REPO}/actions/workflows/{run.workflow}/dispatches",
            "--input",
            "-",
            body={"ref": ref, "inputs": run.inputs, "return_run_details": True},
        )
        run.run_id = int(response["workflow_run_id"])
        run.url = response["html_url"]
    except (RuntimeError, ValueError, KeyError, TypeError) as error:
        run.status = "completed"
        run.conclusion = "not-dispatched"
        run.detail.append(str(error))
        return
    print(f"Dispatched {run.label}: {run.url}", flush=True)


def observe(run: Run) -> None:
    """Refresh one run's status; an unreadable run ends as unknown."""
    try:
        response = gh(f"{REPO}/actions/runs/{run.run_id}")
    except (RuntimeError, ValueError) as error:
        run.status = "completed"
        run.conclusion = "unknown"
        run.detail.append(str(error))
        return
    status = response.get("status") or ""
    if status != run.status:
        note = " (pending deployment review)" if status == "waiting" else ""
        print(f"Run {run.run_id} ({run.label}): {status}{note}", flush=True)
    run.status = status
    run.conclusion = response.get("conclusion") or ""


def interval(text: str) -> float:
    """Accept a finite, non-negative number of seconds."""
    seconds = float(text)
    if not math.isfinite(seconds) or seconds < 0:
        message = "must be a finite, non-negative number of seconds"
        raise argparse.ArgumentTypeError(message)
    return seconds


def main(argv: list[str] | None = None) -> int:
    """Validate, dispatch, await and summarize the requested runs."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--ref", required=True, help="branch or tag to run on")
    parser.add_argument(
        "--poll-interval",
        type=interval,
        default=30,
        help="seconds between polls",
    )
    parser.add_argument("entries", nargs="+", metavar="CALLER:INPUT=VALUE,...")
    arguments = parser.parse_args(argv)

    known = callers()
    try:
        runs = [parse_entry(entry, known) for entry in arguments.entries]
    except EntryError as error:
        print(
            f"Invalid entry {error}; nothing was dispatched.", file=sys.stderr
        )
        return 2

    for run in runs:
        dispatch(run, arguments.ref)
    while True:
        for run in runs:
            if run.status != "completed":
                observe(run)
        if all(run.status == "completed" for run in runs):
            break
        time.sleep(arguments.poll_interval)

    print("\nRun ID      Conclusion      Entry / URL")
    for run in runs:
        print(
            f"{run.run_id or '-':<11} {run.conclusion or '-':<15} {run.label}"
        )
        if run.url:
            print(f"{'':<27} {run.url}")
        for line in run.detail:
            print(f"{'':<27} {line}")
    return 0 if all(run.conclusion == "success" for run in runs) else 1


if __name__ == "__main__":
    sys.exit(main())
