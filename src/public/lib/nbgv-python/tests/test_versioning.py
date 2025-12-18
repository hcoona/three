"""Tests for version normalization."""

from __future__ import annotations

import pytest
from nbgv_python.versioning import normalize_version_field


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("1.2.3", "1.2.3"),
        ("1.2.3-alpha.1", "1.2.3a1"),
        ("1.2.3-beta", "1.2.3b0"),
        ("1.2.3-rc.4", "1.2.3rc4"),
        ("1.2.3-pre.2+sha", "1.2.3rc2+sha"),
        ("1.2.3-dev.5", "1.2.3.dev5"),
        ("1.2.3-gabcdef", "1.2.3+gabcdef"),
        ("1.2.3-alpha.1.gabcdef", "1.2.3a1+gabcdef"),
    ],
)
def test_normalize_version_field(value: str, expected: str) -> None:
    """Ensure semver-like values are converted to PEP 440."""
    assert normalize_version_field(value, field="simple_version") == expected


def test_normalize_version_field_rejects_invalid() -> None:
    """Invalid values raise a runtime error with context."""
    with pytest.raises(RuntimeError):
        normalize_version_field("not-a-version", field="simple_version")
