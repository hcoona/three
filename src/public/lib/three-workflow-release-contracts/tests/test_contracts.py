"""Contract fixture tests for workflow-release JSON handoffs."""

from __future__ import annotations

import json
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest
import three_workflow_release_contracts
import three_workflow_release_contracts.ci_validation_assignments as assignments
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


def test_writer_observation_helpers_are_not_public_exports() -> None:
    """Legacy writer-observation helpers are not current public APIs."""
    names = {
        "admit_ci_validation_writer_observation_artifact",
        "ci_validation_writer_observation_artifact_ref",
        "freeze_ci_validation_writer_observation",
        "validate_ci_validation_writer_observation",
    }
    for name in names:
        assert name not in three_workflow_release_contracts.__all__
        assert not hasattr(three_workflow_release_contracts, name)
        assert not hasattr(assignments, name)


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


def test_selected_project_id_negative_fixtures_are_explicitly_covered() -> None:
    """Keep selected-project-id cardinality fixtures in the contract suite."""
    for fixture_name in (
        "release-plan-empty-selected-project-ids.json",
        "release-plan-multiple-selected-project-ids.json",
        "release-report-empty-selected-project-ids.json",
        "release-report-multiple-selected-project-ids.json",
    ):
        fixture = INVALID_ROOT / fixture_name
        assert fixture.is_file()
        document = _load(fixture)
        with pytest.raises(ContractValidationError) as error:
            validate_contract(document)
        assert "selected-project-ids" in str(error.value)


@pytest.mark.parametrize(
    "field",
    [
        "plan-id",
        "selected-project-ids",
    ],
)
def test_successful_release_report_requires_plan_identity(field: str) -> None:
    """Successful release reports must bind to the emitted release plan."""
    document = _load(VALID_ROOT / "release-report.json")
    document["plan"][field] = None
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert field in str(error.value)


@pytest.mark.parametrize(
    ("section", "field"),
    [
        ("plan", "plan-id"),
        ("plan", "selected-project-ids"),
        ("artifacts", "plan-artifact-name"),
        ("artifacts", "execution-sets-artifact-name"),
        ("artifacts", "entry-publish-handoff-artifact-name"),
    ],
)
def test_successful_release_report_requires_plan_tuple_when_plan_skipped(
    section: str,
    field: str,
) -> None:
    """A successful run must still bind to authoritative plan artifacts."""
    document = _load(VALID_ROOT / "release-report.json")
    document["jobs"]["plan"]["conclusion"] = "skipped"
    document[section][field] = None

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert field in str(error.value)


def test_publish_request_rejects_skip_satisfied_node() -> None:
    """Publish-request handoffs are only valid for active publish nodes."""
    document = _load(VALID_ROOT / "publish-request.json")
    document["publish-node"]["publish-disposition"] = "skip-satisfied"
    document["publish-node"].pop("publish-mode")

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.publish-node.publish-disposition" in str(error.value)


def test_overwrite_mutable_authorization_forgery_fixtures_are_covered() -> None:
    """Reject forged buddy overwrite authorization."""
    for fixture_name in (
        "release-plan-overwrite-mutable-forged-force-authorization.json",
        "release-plan-overwrite-mutable-official-envelope-authorization.json",
    ):
        fixture = INVALID_ROOT / fixture_name
        assert fixture.is_file()
        document = _load(fixture)
        with pytest.raises(ContractValidationError) as error:
            validate_contract(document)
        assert "overwrite-mutable-authorization" in str(error.value)


def test_overwrite_mutable_rejects_package_registry_force_authorization() -> (
    None
):
    """Package registries cannot use mutable-overwrite publish mode."""
    fixture_name = (
        "release-plan-overwrite-mutable-package-registry-force-authorization"
        ".json"
    )
    fixture = INVALID_ROOT / fixture_name
    assert fixture.is_file()
    document = _load(fixture)

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    message = str(error.value)
    assert "$.graph.publish-nodes.publish-node/nuget.publish-mode" in message
    assert "mutable-prerelease GitHub Release targets" in message


def test_replace_authoritative_rejects_non_official_final_github_release() -> (
    None
):
    """replace-authoritative is limited to official final GitHub Releases."""
    for fixture_name in (
        "release-plan-replace-authoritative-buddy-context.json",
        "release-plan-replace-authoritative-prerelease-context.json",
        "release-plan-replace-authoritative-package-registry-context.json",
    ):
        fixture = INVALID_ROOT / fixture_name
        assert fixture.is_file()
        document = _load(fixture)

        with pytest.raises(ContractValidationError) as error:
            validate_contract(document)

        message = str(error.value)
        assert "replace-authoritative" in message
        assert "official final GitHub Release" in message


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


def test_publish_result_rejects_github_release_shape() -> None:
    """Publish-result receipts are package-registry receipts only."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["publish-node-id"] = "publish-node/gh"
    document["target-instance-snapshot-id"] = "github-release/public"
    document["resolved-publish-identity"] = {
        "release-tag": "release/example/v1.2.3",
    }

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    message = str(error.value)
    assert "$.target-instance-snapshot-id" in message
    assert "$.resolved-publish-identity.release-tag" in message


def test_publish_result_rejects_unknown_target_family() -> None:
    """Publish-result receipts must use a known package-registry family."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["target-instance-snapshot-id"] = "unknown/example"

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.target-instance-snapshot-id" in str(error.value)


@pytest.mark.parametrize(
    "snapshot_id",
    [
        "nuget",
        "nuget:",
        "nuget:public",
        "/public",
        "nuget/",
        "nuget/public/extra",
    ],
)
def test_publish_result_rejects_malformed_target_snapshot_id(
    snapshot_id: str,
) -> None:
    """Publish-result target snapshots must use exact family/instance shape."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["target-instance-snapshot-id"] = snapshot_id

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.target-instance-snapshot-id" in str(error.value)


def test_publish_result_rejects_empty_publish_identity() -> None:
    """Publish-result receipts must carry package name and version identity."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["resolved-publish-identity"] = {}

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    message = str(error.value)
    assert "$.resolved-publish-identity.package-name" in message
    assert "$.resolved-publish-identity.version" in message


def test_publish_result_rejects_extra_publish_identity_keys() -> None:
    """Registry publish identities are closed to package name/version."""
    document = _load(VALID_ROOT / "publish-result.json")
    document["resolved-publish-identity"]["registry-url"] = (
        "https://example.invalid"
    )

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.resolved-publish-identity.registry-url" in str(error.value)


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


def test_official_planner_request_can_force() -> None:
    """Accept official force for explicit release-tag retargeting."""
    document = _load(VALID_ROOT / "planner-request.json")
    document["profile"] = "official"
    document["request-flags"] = {"force": True}
    validate_contract(document)


@pytest.mark.parametrize(
    "bad_commit_sha",
    [
        "HEAD",
        "a" * 39,
        "a" * 41,
        "A" * 40,
        "g" * 40,
    ],
)
def test_planner_request_commit_sha_must_be_immutable_sha(
    bad_commit_sha: str,
) -> None:
    """Reject mutable, malformed, or uppercase planner request SHAs."""
    document = _load(VALID_ROOT / "planner-request.json")
    document["commit-sha"] = bad_commit_sha

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.commit-sha" in str(error.value)
    assert "40-char lowercase hex SHA" in str(error.value)


@pytest.mark.parametrize("bad_commit_sha", ["HEAD", "A" * 40])
def test_release_plan_envelope_commit_sha_must_be_immutable_sha(
    bad_commit_sha: str,
) -> None:
    """Reject mutable or uppercase release plan envelope SHAs."""
    document = _load(VALID_ROOT / "release-plan.json")
    document["envelope"]["commit-sha"] = bad_commit_sha

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "$.envelope.commit-sha" in str(error.value)
    assert "40-char lowercase hex SHA" in str(error.value)


@pytest.mark.parametrize(
    "requested_project_ids",
    [
        [],
        ["example", "other"],
    ],
)
def test_planner_request_requires_exactly_one_project_id(
    requested_project_ids: list[str],
) -> None:
    """Current planner requests normalize exactly one project id."""
    document = _load(VALID_ROOT / "planner-request.json")
    document["requested-project-ids"] = requested_project_ids
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "requested-project-ids" in str(error.value)


@pytest.mark.parametrize(
    "selected_project_ids",
    [
        [],
        ["example", "other"],
    ],
)
def test_release_plan_requires_exactly_one_selected_project_id(
    selected_project_ids: list[str],
) -> None:
    """Current release plan envelopes select exactly one project id."""
    document = _load(VALID_ROOT / "release-plan.json")
    document["envelope"]["selected-project-ids"] = selected_project_ids
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "selected-project-ids" in str(error.value)


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
    document["attestation"]["signer-workflow"] = "release-orchestrate.yml"
    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)
    assert "signer-workflow" in str(error.value)


def test_publish_request_artifact_set_must_match_node_members() -> None:
    """Publish requests cannot silently drop planned artifact members."""
    document = _load(VALID_ROOT / "publish-request.json")
    del document["artifacts"]["artifact/symbols"]
    with pytest.raises(ContractValidationError):
        validate_contract(document)


def test_package_registry_distribution_filenames_must_be_unique() -> None:
    """Registry nodes cannot map two artifacts to one remote filename."""
    document = _load(VALID_ROOT / "release-plan.json")
    node = document["graph"]["publish-nodes"]["publish-node/nuget"]
    node["artifact-ids"] = ["artifact/package", "artifact/symbols"]
    filenames = node["projection"][
        "final-distribution-filenames-by-artifact-id"
    ]
    filenames["artifact/symbols"] = filenames["artifact/package"]

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "distribution filenames must be unique" in str(error.value)


@pytest.mark.parametrize(
    (
        "family",
        "snapshot_id",
        "contract_id",
        "concrete_kind",
        "filename",
        "host",
    ),
    [
        (
            "npm",
            "npm/npmjs",
            "npm-publish",
            "npm-package",
            "Example-1.2.3.tgz",
            "registry.npmjs.org",
        ),
        (
            "rubygems",
            "rubygems/rubygems-org",
            "rubygems-publish",
            "rubygem",
            "Example-1.2.3.gem",
            "rubygems.org",
        ),
    ],
)
def test_npm_rejects_standalone_sha256_but_rubygems_accepts_it(  # noqa: PLR0913
    family: str,
    snapshot_id: str,
    contract_id: str,
    concrete_kind: str,
    filename: str,
    host: str,
) -> None:
    """Npm requires algorithm-qualified digests; RubyGems keeps SHA-256."""
    document = _load(VALID_ROOT / "release-plan.json")
    graph = document["graph"]
    snapshot = graph["target-instance-snapshots"].pop("nuget/github-packages")
    snapshot["catalog-ref"] = snapshot_id
    snapshot["contract"] = {
        "aggregate-rules": {
            "cross-variant-policy": "forbid",
            "max-artifact-count": 1,
            "min-artifact-count": 1,
            "tuple-rules": [
                {
                    "concrete-kind": concrete_kind,
                    "kind-family": "package",
                    "max-count": 1,
                    "min-count": 1,
                    "role": "primary-package",
                }
            ],
        },
        "allowed-artifact-tuples": [
            {
                "concrete-kind": concrete_kind,
                "kind-family": "package",
                "role": "primary-package",
            }
        ],
        "id": contract_id,
    }
    snapshot["destination"] = {"host": host}
    snapshot["family"] = family
    snapshot["instance-id"] = snapshot_id.rsplit("/", maxsplit=1)[-1]
    snapshot["capabilities"] = {
        "credential-posture": "oidc",
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "profile-coexistence-rule": "requires-distinct-name",
        "publish-topology": (
            "external-oidc-caller-workflow"
            if family == "npm"
            else "external-oidc-reusable-workflow"
        ),
        "version-uniqueness-rule": "package-name-plus-version",
    }
    graph["target-instance-snapshots"][snapshot_id] = snapshot
    graph["artifacts"]["artifact/package"]["concrete-kind"] = concrete_kind
    node = graph["publish-nodes"]["publish-node/nuget"]
    node["target-instance-snapshot-id"] = snapshot_id
    projection: dict[str, object] = {
        "final-distribution-filenames-by-artifact-id": {
            "artifact/package": filename
        },
        "final-distribution-sha256-by-artifact-id": {
            "artifact/package": "a" * 64
        },
    }
    node["projection"] = projection
    if family == "npm":
        projection["package-name"] = "Example"

    if family == "npm":
        with pytest.raises(ContractValidationError) as error:
            validate_contract(document)
        assert "final-distribution-sha256-by-artifact-id" in str(error.value)
        del projection["final-distribution-sha256-by-artifact-id"]
    validate_contract(document)

    if family == "npm":
        projection["final-distribution-digests-by-artifact-id"] = {
            "artifact/package": {"sha512": "b" * 128}
        }
        validate_contract(document)

        projection["final-distribution-digests-by-artifact-id"] = {
            "artifact/package": {"sha512": "Z" * 128}
        }
        with pytest.raises(ContractValidationError):
            validate_contract(document)

        projection["final-distribution-digests-by-artifact-id"] = {
            "artifact/package": {"sha512": "b" * 128}
        }

    projection["final-distribution-sha256-by-artifact-id"] = {}
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


def test_github_release_request_omits_historical_attestations() -> None:
    """Active publish requests keep superseded attestations out-of-contract."""
    document = _load(VALID_ROOT / "publish-request.json")

    validate_contract(document)


def test_github_release_request_rejects_historical_attestations() -> None:
    """Superseded GitHub Release attestations are historical-only."""
    document = _load(VALID_ROOT / "publish-request.json")
    document["github-release-asset-attestations"] = {
        "artifact/package": {
            "attestation-id": "att-1",
            "attestation-url": "https://github.com/hcoona/three/attestations/1",
            "bundle-path": "attestations/Example.1.2.3.nupkg.json",
        },
    }

    with pytest.raises(ContractValidationError) as error:
        validate_contract(document)

    assert "github-release-asset-attestations" in str(error.value)
    assert "is not allowed" in str(error.value)


def test_artifact_names_follow_low_level_patterns() -> None:
    """Verify deterministic artifact naming helpers."""
    inputs = ArtifactNameInputs(
        run_id=123,
        attempt=4,
        plan_id="plan/example",
        variant_id="variant/example",
        publish_node_id="publish-node/example",
    )
    assert safe_id("abc") == "".join(
        (
            "b",
            "a7816bf8f01cfea414140de",
        )
    )
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
    github_release_result_binding = (
        '{"tagName":"release/example/v1.2.3",'
        '"targetSha":"0123456789abcdef0123456789abcdef01234567"}'
    )
    assert artifact_name(
        "github-release-result",
        ArtifactNameInputs(
            run_id=123,
            attempt=4,
            binding_json=github_release_result_binding,
        ),
    ) == (
        "release-github-release-result-v1-123-4-"
        f"{safe_id(github_release_result_binding)}"
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
