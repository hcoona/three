"""Tests for the data models."""

from __future__ import annotations

import pytest
from nbgv_python.models import GitVersion


@pytest.fixture(name="payload")
def payload_fixture() -> dict[str, object]:
    """Return a representative payload for GitVersion tests."""
    return {
        "SimpleVersion": "1.2.3",
        "NuGetPackageVersion": "1.2.3",
        "SemVer2": "1.2.3+gabcdef",
    }


def test_git_version_normalises_snake_case(payload: dict[str, object]) -> None:
    """Ensure PascalCase keys receive snake_case aliases."""
    version = GitVersion.from_payload(payload)
    assert version["simple_version"] == "1.2.3"
    assert version["SimpleVersion"] == "1.2.3"
    assert version["nuget_package_version"] == "1.2.3"
    assert version["semver2"] == "1.2.3+gabcdef"


def test_require_raises_for_missing_key(payload: dict[str, object]) -> None:
    """Verify `require` raises when a field is absent."""
    version = GitVersion.from_payload(payload)
    with pytest.raises(KeyError):
        version.require("does_not_exist")
    with pytest.raises(KeyError):
        _ = version["does_not_exist"]


def test_as_dict_optionally_includes_raw(payload: dict[str, object]) -> None:
    """`as_dict(include_raw=True)` should merge original keys."""
    version = GitVersion.from_payload(payload)
    merged = version.as_dict(include_raw=True)
    assert "SimpleVersion" in merged
    assert merged["nuget_package_version"] == "1.2.3"


def test_git_version_iterates_all_keys(payload: dict[str, object]) -> None:
    """Ensure iteration yields both snake_case aliases and raw keys."""
    version = GitVersion.from_payload(payload)
    keys = list(version)
    assert "simple_version" in keys
    assert "SimpleVersion" in keys
    assert len(version) == len(keys)
