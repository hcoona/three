"""Tests for Python distribution exactness verification."""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).parents[1]
SCRIPT = REPO_ROOT / "eng/scripts/verify_python_distribution_exactness.py"

spec = importlib.util.spec_from_file_location(
    "verify_python_distribution_exactness", SCRIPT
)
assert spec is not None
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)


def test_verify_distribution_exactness_accepts_exact_names_and_digests(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Matching filenames and SHA-256 digests pass."""
    wheel = tmp_path / "example-1.2.3-py3-none-any.whl"
    sdist = tmp_path / "example-1.2.3.tar.gz"
    wheel.write_bytes(b"wheel")
    sdist.write_bytes(b"sdist")
    expected = {
        wheel.name: hashlib.sha256(b"wheel").hexdigest(),
        sdist.name: hashlib.sha256(b"sdist").hexdigest(),
    }

    module.main([json.dumps(expected), str(tmp_path)])

    assert "Verified 2 Python distributions" in capsys.readouterr().out


def test_verify_distribution_exactness_rejects_extra_file(
    tmp_path: Path,
) -> None:
    """Unexpected produced files fail closed before upload."""
    wheel = tmp_path / "example-1.2.3-py3-none-any.whl"
    wheel.write_bytes(b"wheel")
    (tmp_path / "unexpected.tar.gz").write_bytes(b"unexpected")
    expected = {wheel.name: hashlib.sha256(b"wheel").hexdigest()}

    with pytest.raises(SystemExit, match="filename mismatch"):
        module.main([json.dumps(expected), str(tmp_path)])


def test_verify_distribution_exactness_rejects_digest_mismatch(
    tmp_path: Path,
) -> None:
    """Planner-frozen SHA-256 evidence must match produced bytes."""
    wheel = tmp_path / "example-1.2.3-py3-none-any.whl"
    wheel.write_bytes(b"wheel")
    expected = {wheel.name: "0" * 64}

    with pytest.raises(SystemExit, match="SHA-256 mismatch"):
        module.main([json.dumps(expected), str(tmp_path)])
