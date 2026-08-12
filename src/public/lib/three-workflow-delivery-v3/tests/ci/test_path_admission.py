"""Parity tests for package-owned CI changed-path admission."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from typing import Any

from three_workflow_delivery_v3.ci.path_admission import (
    CI_CONSUMER_POLICY_SURFACE_PATTERNS,
    is_consumer_policy_surface_path,
    is_repository_only_path,
)

REPO_ROOT = Path(__file__).resolve().parents[6]


def _load_consumer_policy() -> Any:
    path = REPO_ROOT / "eng/scripts/workflow_delivery_v3_consumer_policy.py"
    spec = importlib.util.spec_from_file_location("_ci_path_policy", path)
    if spec is None or spec.loader is None:
        message = f"cannot load consumer policy from {path}"
        raise AssertionError(message)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def _representative_path(pattern: str) -> str:
    path = pattern.removeprefix("**/")
    if pattern.startswith("**/"):
        path = f"nested/{path}"
    return path.replace("*", "consumer")


def test_package_admission_has_exhaustive_consumer_policy_catalog_parity() -> (
    None
):
    """Keep planner/record admission aligned with every policy path pattern."""
    policy = _load_consumer_policy()
    policy_patterns = tuple(
        pattern
        for rule in policy.DEPENDENCY_SURFACE_CATALOG
        for pattern in rule.path_patterns
    )

    assert len(policy_patterns) == len(set(policy_patterns))
    assert set(CI_CONSUMER_POLICY_SURFACE_PATTERNS) == set(policy_patterns)
    for pattern in policy_patterns:
        path = _representative_path(pattern)
        assert policy.classify_dependency_surface(path) is not None
        assert is_consumer_policy_surface_path(path)
        assert is_repository_only_path(path)


def test_nested_gitattributes_is_not_invented_as_a_policy_surface() -> None:
    """Admit only the cataloged root .gitattributes policy surface."""
    assert is_consumer_policy_surface_path(".gitattributes")
    assert not is_consumer_policy_surface_path("nested/.gitattributes")
