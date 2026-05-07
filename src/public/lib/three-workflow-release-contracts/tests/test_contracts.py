"""Contract fixture tests for workflow-release JSON handoffs."""

from __future__ import annotations

import json
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest
from three_workflow_release_contracts import (
    REGISTERED_BUILD_DIAGNOSTIC_CODES,
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


def _make_artifact_executable(artifact: dict[str, Any]) -> None:
    """Mutate a build-request artifact fixture into an executable artifact."""
    artifact["role"] = "primary-binary"
    artifact["kind-family"] = "binary"
    artifact["concrete-kind"] = "executable"


def _add_companion(
    artifact: dict[str, Any], companion_path: str = "*.dbg"
) -> None:
    """Attach one companion declaration to a build-request artifact fixture."""
    artifact["companions"] = [
        {
            "path": companion_path,
            "role": "debug-symbol",
            "required": False,
        }
    ]


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
    assert "BUILD_CHECKOUT_FAILED" in REGISTERED_BUILD_DIAGNOSTIC_CODES


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


@pytest.mark.parametrize(
    ("scope_kind", "key", "bad_value"),
    [
        ("project", "project-id", ""),
        ("variant", "variant-id", None),
        ("artifact", "artifact-id", 7),
    ],
)
def test_build_diagnostic_scope_ids_must_be_non_empty_strings(
    scope_kind: str,
    key: str,
    bad_value: object,
) -> None:
    """Reject invalid build diagnostic scope identity values."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["scope-kind"] = scope_kind
    diagnostic["project-id"] = "example"
    if scope_kind in {"variant", "artifact"}:
        diagnostic["variant-id"] = "variant/package"
    if scope_kind == "artifact":
        diagnostic["artifact-id"] = "artifact/wheel"
    diagnostic[key] = bad_value
    with pytest.raises(ContractValidationError):
        validate_contract(document)


@pytest.mark.parametrize(
    "scope_kind",
    ["project", "variant", "artifact"],
)
def test_build_diagnostic_accepts_valid_scope_ids(scope_kind: str) -> None:
    """Accept non-empty build diagnostic scope identity strings."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["scope-kind"] = scope_kind
    diagnostic["project-id"] = "example"
    if scope_kind in {"variant", "artifact"}:
        diagnostic["variant-id"] = "variant/package"
    if scope_kind == "artifact":
        diagnostic["artifact-id"] = "artifact/wheel"
    validate_contract(document)


@pytest.mark.parametrize(
    "companion_path",
    ["*.dbg", "playwright.ps1", "playwright.sh"],
)
def test_build_request_accepts_root_level_companion_paths(
    companion_path: str,
) -> None:
    """Accept root-level executable companion paths and globs."""
    document = _load(VALID_ROOT / "build-request.json")
    artifact = document["artifacts"]["artifact/package"]
    assert isinstance(artifact, dict)
    _make_artifact_executable(artifact)
    _add_companion(artifact, companion_path)
    validate_contract(document)


def test_build_request_rejects_companions_on_non_executable_artifacts() -> None:
    """Reject companion declarations on package artifacts."""
    document = _load(VALID_ROOT / "build-request.json")
    artifact = document["artifacts"]["artifact/package"]
    assert isinstance(artifact, dict)
    _add_companion(artifact)
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    message = str(error.value)
    assert "companions" in message
    assert "executable" in message


@pytest.mark.parametrize(
    "companion_path",
    [
        "",
        ".",
        "..",
        "**",
        "../secret",
        "/secret",
        "C:/secret",
        "C:secret",
        "nested/file.dbg",
        r"nested\file.dbg",
    ],
)
def test_build_request_rejects_unsafe_companion_paths(
    companion_path: str,
) -> None:
    """Reject companion paths that could escape root-level output matching."""
    document = _load(VALID_ROOT / "build-request.json")
    artifact = document["artifacts"]["artifact/package"]
    assert isinstance(artifact, dict)
    _make_artifact_executable(artifact)
    _add_companion(artifact, companion_path)
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "companions[0].path" in str(error.value)


def test_build_diagnostic_request_scope_omits_narrower_ids() -> None:
    """Reject project/variant/artifact IDs on request-scoped diagnostics."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["project-id"] = "example"
    with pytest.raises(ContractValidationError):
        validate_contract(document)


@pytest.mark.parametrize(
    ("scope_kind", "extra_key"),
    [
        ("project", "variant-id"),
        ("project", "artifact-id"),
        ("variant", "artifact-id"),
    ],
)
def test_build_diagnostic_rejects_too_narrow_scope_ids(
    scope_kind: str,
    extra_key: str,
) -> None:
    """Reject IDs narrower than the declared build diagnostic scope."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["scope-kind"] = scope_kind
    diagnostic["project-id"] = "example"
    if scope_kind == "variant":
        diagnostic["variant-id"] = "variant/package"
    diagnostic[extra_key] = f"{extra_key}/unexpected"
    with pytest.raises(ContractValidationError):
        validate_contract(document)


@pytest.mark.parametrize("bad_plan_id", ["", 123])
def test_build_diagnostic_plan_id_must_be_non_empty_string(
    bad_plan_id: object,
) -> None:
    """Reject invalid optional build diagnostic plan IDs."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["plan-id"] = bad_plan_id
    with pytest.raises(ContractValidationError):
        validate_contract(document)


def test_build_diagnostic_accepts_valid_plan_id() -> None:
    """Accept a non-empty optional build diagnostic plan ID."""
    document = _load(VALID_ROOT / "build-diagnostics.json")
    diagnostic = document["diagnostics"][0]
    assert isinstance(diagnostic, dict)
    diagnostic["plan-id"] = "plan/example"
    validate_contract(document)


def test_official_planner_request_cannot_force() -> None:
    """Validate the profile/force conditional planner request rule."""
    document = _load(VALID_ROOT / "planner-request.json")
    document["profile"] = "official"
    document["request-flags"] = {"force": True}
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "official" in str(error.value)


def test_immutable_proof_rejects_root_extensions() -> None:
    """The immutable proof wrapper is a closed contract."""
    document = _load(VALID_ROOT / "immutable-proof.json")
    document["transport-provenance"] = {
        "artifact-created-at": "2026-01-01T00:00:00Z"
    }
    with pytest.raises(ContractValidationError):
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


def test_publish_request_requires_explicit_publish_node_id() -> None:
    """Publish requests carry the exact publish node identifier."""
    document = _load(VALID_ROOT / "publish-request.json")
    del document["publish-node-id"]
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "publish-node-id" in str(error.value)


def test_publish_request_node_id_must_be_project_member() -> None:
    """The publish node id must identify one project publish node."""
    document = _load(VALID_ROOT / "publish-request.json")
    document["publish-node-id"] = "publish-node/missing"
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "publish-node-ids" in str(error.value)


def test_publish_request_node_id_must_match_embedded_node() -> None:
    """The request id is bound to the embedded publish-node payload."""
    document = _load(VALID_ROOT / "publish-request.json")
    document["publish-node"]["publish-node-id"] = "publish-node/nuget"
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "publish-node.publish-node-id" in str(error.value)


def test_github_release_base_request_may_omit_attestations() -> None:
    """Base GitHub Release requests precede attestations."""
    document = _load(VALID_ROOT / "publish-request.json")
    del document["github-release-asset-attestations"]

    validate_contract(document)


def test_github_release_request_rejects_partial_attestations() -> None:
    """Attached GitHub Release attestations must cover every asset."""
    document = _load(VALID_ROOT / "publish-request.json")
    del document["github-release-asset-attestations"]["artifact/symbols"]
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "github-release-asset-attestations" in str(error.value)


def test_github_release_request_rejects_wrong_asset_attestation_shape() -> None:
    """Attached GitHub Release attestations must remain an object."""
    document = _load(VALID_ROOT / "publish-request.json")
    document["github-release-asset-attestations"] = []
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "github-release-asset-attestations" in str(error.value)


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
