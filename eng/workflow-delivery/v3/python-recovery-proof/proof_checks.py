# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# ruff: noqa: ANN401, PLR2004, S101

"""Pure hosted proof predicates, shared by callers and controlled tests."""

import json
from pathlib import Path
from typing import Any


def preparation(
    snapshot: Any, binding: Any, contents: Any, *, seed_contents: Any = None
) -> Any:
    """Validate the intended seed or fresh recovery preparation."""
    assert snapshot.registry.name == "testpypi"
    assert snapshot.action_required
    assert snapshot.proof_mode == binding["mode"]
    dispositions = tuple(snapshot.dispositions)
    if binding["mode"] == "stop-after-wheel":
        assert dispositions == ("upload", "upload")
        assert snapshot.observation.classification == "absent"
    else:
        assert dispositions == ("already-present", "upload")
        assert seed_contents is not None
        assert tuple(contents) == tuple(seed_contents), (
            "independent rebuild bytes changed"
        )
    return dispositions


def diagnostic_gate(proof: Any, run: Any) -> None:
    """Require resolved dispatch and publisher quiescence before reads."""
    assert proof["run"] == run
    assert proof["terminal"] is True
    assert proof["dispatch_resolved"] is True
    assert proof["publisher_quiescent"] is True
    assert proof["native_read_admitted"] is True


def result_contract(result: Any, mode: Any) -> None:
    """Require the intended seed or recovery operation outcomes."""
    statuses = tuple(op.status for op in result.operations)
    if mode == "stop-after-wheel":
        assert result.result == "failed"
        assert statuses == (
            "succeeded",
            "not-attempted",
        )
    else:
        assert result.result == "published"
        assert statuses == (
            "already-present",
            "succeeded",
        )


def seed_archives(root: Any, binding: Any) -> Any:
    """Read prior original bytes through their retained artifact edges."""
    seed = binding["seed_attempt"]
    assert seed < binding["attempt"]
    assert len(seed) == 2
    assert seed.isdigit()
    directory = root.parent / seed / "operation/inputs"
    refs = json.loads((directory / "references.json").read_bytes())
    names = [refs[role]["payload-path"] for role in ("wheel", "sdist")]
    assert all(Path(name).name == name for name in names)
    return tuple((directory / name).read_bytes() for name in names)
