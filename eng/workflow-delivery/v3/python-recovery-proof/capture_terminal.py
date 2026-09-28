# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: E501, PLR0915, PLR2004, S101

"""Retain terminal platform facts, original logs and explicit new artifact edges."""

import io
import json
import re
import zipfile

from capture_artifact import retain_artifact, retain_bytes, validate_artifacts
from dispatch_normal import ROOT, TARGET
from normal_operator import Operator


def main() -> None:
    """Execute the bound caller stage once."""
    directory = ROOT / "operation"
    assert (directory / "ledger.json").exists()
    op = Operator(directory, TARGET)
    with op.deadline():
        run_id = op.ledger["run"]
        endpoint = f"repos/hcoona/three/actions/runs/{run_id}"
        run = op.capture_get(endpoint, "terminal-run")
        assert run["head_sha"] == TARGET
        assert run["run_attempt"] == 1
        assert run["status"] == "completed"
        op.ledger.setdefault("terminal_capture_started", op.now())
        op.save()
        jobs = op.capture_get(endpoint + "/jobs?per_page=100", "terminal-jobs")
        assert jobs["total_count"] <= 100
        assert all(
            j["run_id"] == run_id
            and j["head_sha"] == TARGET
            and j["run_attempt"] == 1
            for j in jobs["jobs"]
        )
        op.capture_get(endpoint + "/approvals", "terminal-approval-history")
        raw = op.transfer("log", endpoint + "/logs", "terminal-logs")
        retain_bytes(directory / "terminal-logs.zip", raw)
        logs = directory / "terminal-logs"
        logs.mkdir(exist_ok=True)
        maps = []
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            infos = archive.infolist()
            assert len(infos) <= 500
            assert sum(i.file_size for i in infos) <= 128 << 20
            for ordinal, item in enumerate(infos):
                if item.is_dir():
                    continue
                assert item.file_size <= 32 << 20
                content = archive.read(item)
                # Assign a local numeric path, never extract the archive path.
                local = f"{ordinal:03d}.log"
                retain_bytes(logs / local, content)
                for line_no, line in enumerate(
                    content.decode("utf-8").splitlines(), 1
                ):
                    match = re.search(
                        r"(?:NEXT_REFERENCES|WDV3_REFERENCES): (\{.*\})\s*$",
                        line,
                    )
                    if match:
                        value = json.loads(match.group(1))
                        assert isinstance(value, dict)
                        maps.append(
                            {
                                "archive_path": item.filename,
                                "local_path": local,
                                "line": line_no,
                                "references": value,
                            }
                        )
        retain_bytes(
            logs / "edge-trace.json",
            (json.dumps(maps, indent=2) + "\n").encode(),
        )
        assert maps
        refs = {}
        for item in maps:
            for role, reference in item["references"].items():
                assert role not in refs or refs[role] == reference, (
                    "conflicting current-run edge"
                )
                refs[role] = reference
        allowed = {
            "request",
            "intent",
            "governance",
            "provider",
            "model",
            "qualification",
            "wheel",
            "sdist",
            "build-report",
            "artifacts",
            "quality",
            "decision",
            "observation",
            "publication",
            "summary",
            "bundle",
            "authorization",
            "marker",
            "result",
            "outcome",
            "exact-proof",
        }
        assert set(refs) <= allowed
        assert len(refs) <= 20
        inputs = directory / "inputs"
        inputs.mkdir(exist_ok=True)
        previous_path = inputs / "references.json"
        previous = (
            json.loads(previous_path.read_text())
            if previous_path.exists()
            else {}
        )
        assert all(refs.get(role) == value for role, value in previous.items())
        metadata = op.capture_get(
            endpoint + "/artifacts?per_page=100", "terminal-artifacts"
        )
        assert metadata["total_count"] <= 32
        artifacts = validate_artifacts(refs, metadata, run_id, TARGET)
        for role, ref in refs.items():
            retain_artifact(
                op,
                inputs,
                role,
                ref,
                artifacts[ref["artifact-id"]],
                previous=role in previous,
            )
        retain_bytes(
            inputs / "terminal-references.json",
            json.dumps(refs, sort_keys=True, separators=(",", ":")).encode(),
        )
        op.ledger.setdefault("terminal_capture_completed", op.now())
        op.save()


if __name__ == "__main__":
    main()
