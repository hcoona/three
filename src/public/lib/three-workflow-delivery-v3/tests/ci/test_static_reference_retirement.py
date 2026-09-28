"""Retirement contracts for the superseded consumer-policy route."""

from __future__ import annotations

import importlib.util
from pathlib import Path

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
