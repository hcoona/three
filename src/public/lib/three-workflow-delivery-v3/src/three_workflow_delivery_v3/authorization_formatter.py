"""Dependency-free exact-revision Authorization formatter."""

# ruff: noqa: C901, EM101, EM102, TRY003

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import re
from pathlib import Path
from typing import Any

_DIGEST = re.compile(r"sha256:[0-9a-f]{64}")
_SHA = re.compile(r"[0-9a-f]{40}")


def _load_object(path: Path) -> dict[str, Any]:
    def reject_duplicates(
        pairs: list[tuple[str, Any]],
    ) -> dict[str, Any]:
        value: dict[str, Any] = {}
        for name, item in pairs:
            if name in value:
                raise ValueError(f"duplicate JSON member: {name}")
            value[name] = item
        return value

    value = json.loads(
        path.read_text(encoding="utf-8"),
        object_pairs_hook=reject_duplicates,
    )
    if type(value) is not dict:
        raise TypeError("formatter input must be a JSON object")
    return value


def _canonical(value: dict[str, Any]) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        allow_nan=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode()


def _digest(payload: bytes) -> str:
    return f"sha256:{hashlib.sha256(payload).hexdigest()}"


def _positive(value: int, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError(f"{field} must be a positive integer")
    return value


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workflow-run-id", required=True, type=int)
    parser.add_argument("--run-attempt", required=True, type=int)
    parser.add_argument("--target", required=True)
    parser.add_argument("--formatter-input", required=True, type=Path)
    parser.add_argument("--approval-job-id", required=True, type=int)
    parser.add_argument("--completed-at", required=True)
    parser.add_argument("--control", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--github-output", type=Path)
    arguments = parser.parse_args()

    if _SHA.fullmatch(arguments.target) is None:
        raise ValueError("target must be an exact lowercase SHA")
    _positive(arguments.workflow_run_id, field="workflow-run-id")
    _positive(arguments.run_attempt, field="run-attempt")
    _positive(arguments.approval_job_id, field="approval-job-id")

    formatter = _load_object(arguments.formatter_input)
    if formatter.get("schema") != (
        "workflow-delivery/v3/bound-reviewer-formatter-input"
    ):
        raise ValueError("formatter input schema mismatch")
    snapshot_bytes = base64.b64decode(
        formatter["snapshot-base64"],
        validate=True,
    )
    summary_bytes = base64.b64decode(
        formatter["summary-base64"],
        validate=True,
    )
    snapshot_digest = formatter["snapshot-payload-digest"]
    summary_digest = formatter["summary-payload-digest"]
    upload_digest = formatter["reviewer-artifact-digest"]
    for field, value in (
        ("snapshot-payload-digest", snapshot_digest),
        ("summary-payload-digest", summary_digest),
        ("reviewer-artifact-digest", upload_digest),
    ):
        if type(value) is not str or _DIGEST.fullmatch(value) is None:
            raise ValueError(f"{field} is malformed")
    if _digest(snapshot_bytes) != snapshot_digest:
        raise ValueError("snapshot payload digest mismatch")
    if _digest(summary_bytes) != summary_digest:
        raise ValueError("summary payload digest mismatch")
    snapshot = json.loads(snapshot_bytes)
    if type(snapshot) is not dict or _canonical(snapshot) != snapshot_bytes:
        raise ValueError("Publication Snapshot is not canonical")
    attempt = snapshot.get("attempt")
    if type(attempt) is not dict:
        raise ValueError("Publication Snapshot Attempt is missing")
    execution = attempt.get("execution")
    if type(execution) is not dict:
        raise ValueError("Publication Snapshot Execution is missing")
    if (
        attempt.get("workflow-run-id") != arguments.workflow_run_id
        or attempt.get("run-attempt") != arguments.run_attempt
        or execution.get("target") != arguments.target
    ):
        raise ValueError("Authorization current Attempt binding mismatch")

    authorization = {
        "schema": "workflow-delivery/v3/authorization-record",
        "attempt": attempt,
        "publication-snapshot-digest": snapshot_digest,
        "reviewer-summary-artifact-id": _positive(
            formatter["reviewer-artifact-id"],
            field="reviewer-artifact-id",
        ),
        "reviewer-summary-upload-digest": upload_digest,
        "reviewer-summary-payload-digest": summary_digest,
        "workflow-run-id": arguments.workflow_run_id,
        "run-attempt": arguments.run_attempt,
        "approval-job-id": arguments.approval_job_id,
        "approval-job": "approval",
        "environment": "workflow-delivery-v3-buddy-approval",
        "channel": "buddy",
        "completed-at": arguments.completed_at,
        "producer": "approval",
        "control": arguments.control,
        "result": "success",
    }
    payload = _canonical(authorization)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_bytes(payload)
    authorization_digest = _digest(payload)
    if arguments.github_output is not None:
        with arguments.github_output.open("a", encoding="utf-8") as stream:
            stream.write(f"authorization-digest={authorization_digest}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
