"""Deterministic artifact names for workflow-release handoffs."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from typing import Literal

ArtifactKind = Literal[
    "plan",
    "planner-diagnostics",
    "dotnet-planner-metadata-input",
    "dotnet-planner-metadata",
    "execution-sets",
    "entry-publish-handoff",
    "variant-bundle",
    "build-result",
    "tag-result",
    "publish-result",
    "skip-result",
    "immutable-proof",
    "github-release-asset-proof",
    "release-report",
]


@dataclass(frozen=True, slots=True)
class ArtifactNameInputs:
    """Inputs required to render deterministic artifact names."""

    run_id: int
    attempt: int
    plan_id: str | None = None
    variant_id: str | None = None
    publish_node_id: str | None = None
    binding_json: str | None = None


def safe_id(value: str) -> str:
    """Return the first 24 lowercase SHA-256 hex chars for UTF-8 *value*."""
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:24]


def immutable_binding_json(
    *,
    publish_node_id: str,
    artifact_id: str,
    package_name: str,
    version: str,
) -> str:
    """Serialize the immutable proof binding with frozen member order."""
    return json.dumps(
        {
            "publish-node-id": publish_node_id,
            "artifact-id": artifact_id,
            "package-name": package_name,
            "version": version,
        },
        ensure_ascii=False,
        separators=(",", ":"),
    )


def github_release_asset_binding_json(
    *,
    publish_node_id: str,
    artifact_id: str,
    release_tag: str,
    asset_name: str,
) -> str:
    """Serialize the GitHub Release asset proof binding."""
    return json.dumps(
        {
            "publish-node-id": publish_node_id,
            "artifact-id": artifact_id,
            "release-tag": release_tag,
            "asset-name": asset_name,
        },
        ensure_ascii=False,
        separators=(",", ":"),
    )


def _require(value: str | None, name: str) -> str:
    """Return *value* or raise a helpful argument error."""
    if value is None:
        msg = f"{name} is required"
        raise ValueError(msg)
    return value


def artifact_name(  # noqa: C901,PLR0911,PLR0912
    kind: ArtifactKind, inputs: ArtifactNameInputs
) -> str:
    """Render one current-scope GitHub Actions artifact name."""
    run = inputs.run_id
    attempt = inputs.attempt
    if kind == "plan":
        plan = _require(inputs.plan_id, "plan_id")
        return f"release-plan-v1-{run}-{attempt}-{safe_id(plan)}"
    if kind == "planner-diagnostics":
        return f"release-planner-diagnostics-v1-{run}-{attempt}"
    if kind == "dotnet-planner-metadata-input":
        return f"release-dotnet-planner-metadata-input-v1-{run}-{attempt}"
    if kind == "dotnet-planner-metadata":
        return f"release-dotnet-planner-metadata-v1-{run}-{attempt}"
    if kind == "execution-sets":
        plan = _require(inputs.plan_id, "plan_id")
        return f"release-execution-sets-v1-{run}-{attempt}-{safe_id(plan)}"
    if kind == "entry-publish-handoff":
        plan = _require(inputs.plan_id, "plan_id")
        return (
            f"release-entry-publish-handoff-v1-{run}-{attempt}-{safe_id(plan)}"
        )
    if kind == "variant-bundle":
        key = (
            f"{_require(inputs.plan_id, 'plan_id')}\n"
            f"{_require(inputs.variant_id, 'variant_id')}"
        )
        return f"release-build-bundle-v1-{run}-{attempt}-{safe_id(key)}"
    if kind == "build-result":
        key = (
            f"{_require(inputs.plan_id, 'plan_id')}\n"
            f"{_require(inputs.variant_id, 'variant_id')}"
        )
        return f"release-build-result-v1-{run}-{attempt}-{safe_id(key)}"
    if kind == "tag-result":
        plan = _require(inputs.plan_id, "plan_id")
        return f"release-tag-result-v1-{run}-{attempt}-{safe_id(plan)}"
    if kind == "publish-result":
        key = (
            f"{_require(inputs.plan_id, 'plan_id')}\n"
            f"{_require(inputs.publish_node_id, 'publish_node_id')}"
        )
        return f"release-publish-result-v1-{run}-{attempt}-{safe_id(key)}"
    if kind == "skip-result":
        key = (
            f"{_require(inputs.plan_id, 'plan_id')}\n"
            f"{_require(inputs.publish_node_id, 'publish_node_id')}"
        )
        return f"release-skip-result-v1-{run}-{attempt}-{safe_id(key)}"
    if kind == "immutable-proof":
        digest = safe_id(_require(inputs.binding_json, "binding_json"))
        return f"release-immutable-proof-v1-{digest}-{run}-{attempt}"
    if kind == "github-release-asset-proof":
        digest = safe_id(_require(inputs.binding_json, "binding_json"))
        return f"release-github-release-asset-proof-v1-{digest}-{run}-{attempt}"
    if kind == "release-report":
        return f"release-report-v1-{run}-{attempt}"
    msg = f"unsupported artifact kind: {kind}"
    raise ValueError(msg)
