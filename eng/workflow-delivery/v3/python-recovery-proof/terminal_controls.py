# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# ruff: noqa: ANN401, PLR2004, S101

"""Recover original scalar controls from one retained Finalizer step log."""

import hashlib
import json
import re
import zipfile
from typing import Any

FIELDS = {
    "references": "WDV3_REFERENCES",
    "publisher_conclusion": "PUBLISHER_CONCLUSION",
    "publication_step_outcome": "PUBLICATION_STEP_OUTCOME",
    "terminal_reference": "TERMINAL_REFERENCE",
    "observation_conclusion": "OBSERVATION_CONCLUSION",
}


def extract(directory: Any, run: Any, target: Any) -> Any:
    """Recover exact Finalizer scalar values from retained logs."""
    ledger = json.loads((directory / "ledger.json").read_bytes())
    jobs = [
        r
        for r in ledger["requests"]
        if r["name"] == "terminal-jobs" and r["status"] == 200
    ]
    assert len(jobs) == 1
    with zipfile.ZipFile(directory / "terminal-logs.zip") as archive:
        entries = [
            (i, x)
            for i, x in enumerate(archive.infolist())
            if re.fullmatch(
                r"finalize-attempt/\d+_finalize Python outcome\.txt", x.filename
            )
        ]
        assert len(entries) == 1, (
            "exact Finalizer step log missing or ambiguous"
        )
        ordinal, item = entries[0]
        raw = archive.read(item)
    local = f"terminal-logs/{ordinal:03d}.log"
    assert (directory / local).read_bytes() == raw
    controls = {
        "target": target,
        "run": run,
        "source_archive_path": item.filename,
    }
    for field, env in FIELDS.items():
        values = re.findall(
            r"Z\s+" + env + r": (.*)$", raw.decode(), re.MULTILINE
        )
        assert len(values) == 1, f"original scalar missing/ambiguous: {env}"
        controls[field] = values[0]
    controls["files_sha256"] = {
        name: hashlib.sha256((directory / name).read_bytes()).hexdigest()
        for name in (local, "terminal-logs.zip", jobs[0]["body"])
    }
    return controls
