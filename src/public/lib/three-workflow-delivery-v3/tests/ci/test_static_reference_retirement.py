"""Retirement contracts for the superseded consumer-policy route."""

from __future__ import annotations

import importlib.util
from pathlib import Path

from three_workflow_delivery_v3.ci.path_admission import (
    is_static_reference_surface_path,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
RETIRED_FIXTURE = (
    Path(__file__).resolve().parents[1]
    / "fixtures/release/consumer-policy-acceptance.json"
)


def test_consumer_policy_runtime_and_script_are_retired() -> None:
    """Keep removed policy mechanics absent instead of adding compatibility."""
    package_spec = importlib.util.find_spec(
        "three_workflow_delivery_v3.release"
    )

    assert package_spec is not None
    assert (
        importlib.util.find_spec(
            "three_workflow_delivery_v3.release.consumer_policy"
        )
        is None
    )
    assert (
        importlib.util.find_spec(
            "three_workflow_delivery_v3.release.javascript_consumer"
        )
        is None
    )
    assert not (
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_consumer_policy.py"
    ).exists()
    assert not RETIRED_FIXTURE.exists()


def test_bounded_static_reference_replaces_broad_consumer_classification() -> (
    None
):
    """Retain only exact model-backed static-reference basenames."""
    selected = {
        path
        for path in (
            "package.json",
            "nested/package.json",
            "pnpm-lock.yaml",
            "packages.lock.json",
            "requirements.txt",
            "workflow.yml",
            "postinstall.mjs",
        )
        if is_static_reference_surface_path(path)
    }

    assert selected == {
        "package.json",
        "nested/package.json",
        "pnpm-lock.yaml",
        "packages.lock.json",
    }


def test_no_production_file_imports_consumer_policy() -> None:
    """Prevent a hidden production compatibility route from returning."""
    production = (
        REPO_ROOT / "src/public/lib/three-workflow-delivery-v3/src/"
        "three_workflow_delivery_v3"
    )
    offenders = {
        path.relative_to(production).as_posix()
        for path in production.rglob("*.py")
        if "consumer_policy" in path.read_text(encoding="utf-8")
    }

    assert offenders == set()
