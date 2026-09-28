# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Each audited stage keeps one reservation/evidence transaction boundary.
# ruff: noqa: ANN401, PLR0913, PLR2004, S101

"""Admit raw immutable artifacts by original ID/edge, then payload basename."""

import hashlib
from pathlib import Path
from typing import Any


def retain_bytes(path: Path, content: bytes) -> None:
    """Reuse identical output; reject changed retained evidence."""
    if path.exists():
        assert path.read_bytes() == content, "retained capture bytes changed"
    else:
        with path.open("xb") as stream:
            stream.write(content)


def validate_artifacts(
    references: Any, metadata: Any, run: Any, target: Any
) -> Any:
    """Verify immutable current-run artifact identities and payload names."""
    assert metadata["total_count"] == len(metadata["artifacts"]) <= 32
    artifacts = {item["id"]: item for item in metadata["artifacts"]}
    assert len(artifacts) == metadata["total_count"]
    assert len({ref["artifact-id"] for ref in references.values()}) == len(
        references
    )
    names = [ref["payload-path"] for ref in references.values()]
    assert len(set(names)) == len(names)
    for ref in references.values():
        name = ref["payload-path"]
        assert name not in ("", ".", "..")
        assert Path(name).name == name
        assert "/" not in name
        assert "\\" not in name
        assert "\0" not in name
        artifact = artifacts[ref["artifact-id"]]
        assert not artifact["expired"]
        assert artifact["name"] == name
        assert artifact["workflow_run"]["id"] == run
        assert artifact["workflow_run"]["head_sha"] == target
        assert (
            artifact["digest"]
            == ref["artifact-digest"]
            == ref["payload-digest"]
        )
    return artifacts


def retain_artifact(
    operator: Any,
    inputs: Any,
    role: Any,
    reference: Any,
    artifact: Any,
    *,
    previous: Any = False,
) -> None:
    """Retain a new immutable ID once or verify its existing bytes."""
    path = inputs / reference["payload-path"]
    if previous or path.exists():
        content = path.read_bytes()
    else:
        content = operator.transfer(
            "artifact",
            f"repos/hcoona/three/actions/artifacts/{artifact['id']}/zip",
            "artifact-" + role,
        )
    assert "sha256:" + hashlib.sha256(content).hexdigest() == artifact["digest"]
    if not path.exists():
        with path.open("xb") as stream:
            stream.write(content)
