"""Exercise the solver-free HK boundary without provisioning Java in pytest."""

import runpy
import subprocess
import sys
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[2] / "eng/scripts/alloy.py"


def test_batch_parse_propagates_upstream_error(tmp_path, monkeypatch):
    """Keep the upstream diagnostic status from a later file in a batch."""
    good, bad = (tmp_path / name for name in ("Good.als", "Bad file.als"))
    parsed = []

    def upstream(argv, **_kwargs):
        if argv[:2] == ["mise", "where"]:
            return subprocess.CompletedProcess(argv, 0, stdout=str(tmp_path))
        assert argv[-2] == "commands"
        parsed.append(Path(argv[-1]))
        return subprocess.CompletedProcess(argv, 1 if parsed[-1] == bad else 0)

    monkeypatch.setattr(subprocess, "run", upstream)
    monkeypatch.setattr(
        sys, "argv", [str(SCRIPT), "parse", "--", str(good), str(bad)]
    )
    with pytest.raises(SystemExit) as result:
        runpy.run_path(str(SCRIPT), run_name="__main__")
    assert result.value.code == 1
    assert parsed == [good, bad]
