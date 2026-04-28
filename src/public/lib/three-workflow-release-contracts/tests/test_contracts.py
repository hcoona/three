"""Contract fixture tests for workflow-release JSON handoffs."""

from __future__ import annotations

import json
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest
from three_workflow_release_contracts import (
    REGISTERED_DIAGNOSTIC_CODES,
    ArtifactNameInputs,
    ContractValidationError,
    artifact_name,
    github_release_asset_binding_json,
    immutable_binding_json,
    safe_id,
    validate_contract,
)

FIXTURE_ROOT = Path(__file__).parent / "fixtures"
VALID_ROOT = FIXTURE_ROOT / "valid"
INVALID_ROOT = FIXTURE_ROOT / "invalid"


def _load(path: Path) -> dict[str, Any]:
    """Load one JSON fixture."""
    return json.loads(path.read_text(encoding="utf-8"))


@pytest.fixture(name="metadata_input")
def metadata_input_fixture() -> dict[str, Any]:
    """Return the valid .NET metadata input fixture."""
    return _load(VALID_ROOT / "dotnet-planner-metadata-input.json")


@pytest.mark.parametrize(
    "fixture", sorted(VALID_ROOT.glob("*.json")), ids=lambda path: path.name
)
def test_valid_fixtures_match_frozen_contracts(
    fixture: Path,
    metadata_input: dict[str, Any],
) -> None:
    """Validate every golden fixture without live external systems."""
    document = _load(fixture)
    kwargs = (
        {"metadata_input": metadata_input}
        if fixture.name == "dotnet-planner-metadata.json"
        else {}
    )
    validate_contract(document, **kwargs)


def test_registered_diagnostic_vocabulary_is_exposed() -> None:
    """Expose the frozen planner diagnostic code vocabulary to callers."""
    assert "REQ_INVALID_INPUT" in REGISTERED_DIAGNOSTIC_CODES
    assert "PLAN_INTERNAL_INVARIANT" in REGISTERED_DIAGNOSTIC_CODES


def test_dotnet_metadata_requires_metadata_input_context() -> None:
    """Metadata output validation requires its input manifest."""
    document = _load(VALID_ROOT / "dotnet-planner-metadata.json")
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "metadata_input is required" in str(error.value)


@pytest.mark.parametrize(
    "fixture", sorted(INVALID_ROOT.glob("*.json")), ids=lambda path: path.name
)
def test_invalid_fixtures_are_rejected(
    fixture: Path,
    metadata_input: dict[str, Any],
) -> None:
    """Reject representative negative frozen-contract fixtures."""
    document = _load(fixture)
    kwargs = (
        {"metadata_input": metadata_input}
        if fixture.name.startswith("dotnet-metadata")
        else {}
    )
    with pytest.raises(ContractValidationError):
        validate_contract(document, **kwargs)


def test_extra_root_fields_are_rejected_for_closed_results() -> None:
    """Ensure closed result contracts reject accidental root extensions."""
    document = _load(VALID_ROOT / "build-result.json")
    document["unexpected"] = "not allowed"
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "unexpected" in str(error.value)


def test_wrong_kind_is_rejected() -> None:
    """Reject contracts whose discriminator does not match a registered kind."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["kind"] = "publish-receipt"
    with pytest.raises(ContractValidationError):
        validate_contract(document)


def test_official_planner_request_cannot_force() -> None:
    """Validate the profile/force conditional planner request rule."""
    document = _load(VALID_ROOT / "planner-request.json")
    document["profile"] = "official"
    document["request-flags"] = {"force": True}
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "official" in str(error.value)


def test_immutable_proof_allows_registered_provenance_extension() -> None:
    """The immutable proof wrapper may carry extra provenance fields."""
    document = _load(VALID_ROOT / "immutable-proof.json")
    document["transport-provenance"] = {
        "artifact-created-at": "2026-01-01T00:00:00Z"
    }
    validate_contract(document)


def test_github_asset_proof_requires_full_signer_workflow() -> None:
    """Enforce full signer workflow identities in asset proof wrappers."""
    document = _load(VALID_ROOT / "github-release-asset-proof.json")
    document["attestation"]["signer-workflow"] = "release-publish-node.yml"
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "signer-workflow" in str(error.value)


def test_publish_request_artifact_set_must_match_node_members() -> None:
    """Publish requests cannot silently drop planned artifact members."""
    document = _load(VALID_ROOT / "publish-request.json")
    del document["artifacts"]["artifact/symbols"]
    with pytest.raises(ContractValidationError):
        validate_contract(document)


def test_artifact_names_follow_low_level_patterns() -> None:
    """Verify deterministic artifact naming helpers."""
    inputs = ArtifactNameInputs(
        run_id=123,
        attempt=4,
        plan_id="plan/example",
        variant_id="variant/example",
        publish_node_id="publish-node/example",
    )
    assert safe_id("abc") == "ba7816bf8f01cfea414140de"
    assert artifact_name("plan", inputs) == (
        f"release-plan-v1-123-4-{safe_id('plan/example')}"
    )
    assert artifact_name("build-result", inputs) == (
        "release-build-result-v1-123-4-"
        f"{safe_id('plan/example\nvariant/example')}"
    )
    assert artifact_name("publish-result", inputs) == (
        "release-publish-result-v1-123-4-"
        f"{safe_id('plan/example\npublish-node/example')}"
    )
    assert artifact_name("release-report", inputs) == "release-report-v1-123-4"


def test_proof_binding_json_uses_frozen_member_order() -> None:
    """Verify canonical proof binding JSON byte inputs."""
    immutable = immutable_binding_json(
        publish_node_id="publish-node/one",
        artifact_id="artifact/one",
        package_name="Example",
        version="1.2.3",
    )
    github = github_release_asset_binding_json(
        publish_node_id="publish-node/one",
        artifact_id="artifact/one",
        release_tag="release/example/v1.2.3",
        asset_name="Example.1.2.3.nupkg",
    )
    assert immutable == (
        '{"publish-node-id":"publish-node/one","artifact-id":"artifact/one",'
        '"package-name":"Example","version":"1.2.3"}'
    )
    assert github == (
        '{"publish-node-id":"publish-node/one","artifact-id":"artifact/one",'
        '"release-tag":"release/example/v1.2.3",'
        '"asset-name":"Example.1.2.3.nupkg"}'
    )
    proof_name = artifact_name(
        "immutable-proof",
        ArtifactNameInputs(run_id=123, attempt=4, binding_json=immutable),
    )
    assert proof_name.startswith(
        f"release-immutable-proof-v1-{safe_id(immutable)}-123-4"
    )


def test_fixture_mutation_does_not_need_live_registry() -> None:
    """Prove validation is pure by accepting independent in-memory copies."""
    document = deepcopy(_load(VALID_ROOT / "release-plan.json"))
    validate_contract(document)
