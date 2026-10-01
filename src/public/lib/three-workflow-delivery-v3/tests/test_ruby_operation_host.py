"""Native input admission and immutable current-run artifact boundaries."""

from copy import deepcopy
from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.release.ruby_governance import (
    blocked_ruby_governance,
)
from three_workflow_delivery_v3.ruby_operation_host import (
    HostedRubyOperation,
    admit_operation_input,
)

from .acceptance.ruby_bootstrap_fixtures import CURRENT
from .ruby_hosted_fixtures import hosted_case


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_hosted_admission_joins_actual_run_to_reserved_exact_request(
    tmp_path, monkeypatch, bootstrap, destination
):
    """Bootstrap has distinct authority; normal retains domain attempt None."""
    case = hosted_case(tmp_path, destination=destination, bootstrap=bootstrap)
    operation = case.admit(monkeypatch)
    assert operation.request.digest == case.request.digest
    assert operation.run_id == int(case.env()["GITHUB_RUN_ID"])
    assert operation.bootstrap is bootstrap
    assert operation.document["run-join"] == {
        "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
        "request-digest": case.request.digest,
        "reservation-digest": ruby_digest(
            canonicalize(case.raw["reservation"])
        ),
        "run-id": 991,
        "run-attempt": 1,
    }
    if bootstrap:
        assert (
            operation.bootstrap_run(CURRENT).binding_digest
            == case.native_run.binding_digest
        )
        with pytest.raises(ValueError, match="cannot use normal"):
            operation.governance(CURRENT)
    else:
        assert operation.context().run_attempt is None
        assert operation.governance(CURRENT).document["state"] == "ready"


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("GITHUB_RUN_ID", "992"),
        ("GITHUB_SHA", "b" * 40),
        ("GITHUB_WORKFLOW_SHA", "b" * 40),
        ("GITHUB_REF", "refs/heads/topic"),
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_EVENT_NAME", "pull_request"),
        ("GITHUB_ACTOR", "another"),
        (
            "GITHUB_WORKFLOW_REF",
            "hcoona/three/.github/workflows/foreign.yml@refs/heads/main",
        ),
    ],
)
def test_hosted_retained_operation_rejects_native_identity_drift(
    tmp_path, monkeypatch, bootstrap, key, value
):
    """Previously valid bytes never supply current native authority."""
    case = hosted_case(tmp_path, bootstrap=bootstrap)
    operation = case.admit(monkeypatch)
    monkeypatch.setenv(key, value)
    with pytest.raises(ValueError, match=r"Ruby|canonical|disabled"):
        HostedRubyOperation(operation.content, case.root, CURRENT)


@pytest.mark.parametrize(
    "change",
    [
        "whitespace",
        "extra",
        "reviewer-alias",
        "reservation",
        "request",
        "inspection",
    ],
)
@pytest.mark.parametrize("bootstrap", [True, False])
def test_hosted_input_rejects_unjoined_or_noncanonical_admission(
    tmp_path, monkeypatch, change, bootstrap
):
    """Closed input cannot smuggle a different reviewer, slot or inspection."""
    case = hosted_case(tmp_path, bootstrap=bootstrap)
    case.install(monkeypatch)
    raw = deepcopy(case.raw)
    if change == "extra":
        raw["unreviewed"] = True
    elif change == "reviewer-alias":
        raw["review"]["reviewer"] = raw["review"]["author"]
    elif change == "reservation":
        raw["reservation"]["request-digest"] = "sha256:" + "0" * 64
    elif change == "request":
        raw["request"]["generation"] = "9" * 32
    elif change == "inspection":
        raw["inspection"] = None if bootstrap else case.raw["review"]
    content = canonicalize(raw).decode() + (
        "\n" if change == "whitespace" else ""
    )
    with pytest.raises((ValueError, TypeError)):
        admit_operation_input(case.root, content)
    assert case.references == {}
    assert not (case.directory / "outputs").exists()


@pytest.mark.parametrize("change", ["blocked", "too-long", "changed"])
def test_hosted_normal_ready_governance_cannot_be_replaced_or_extended(
    tmp_path, monkeypatch, change
):
    """Normal admission checks actual protected bytes and seven-day bounds."""
    case = hosted_case(tmp_path, bootstrap=False)
    operation = case.admit(monkeypatch)
    path = case.root / operation.binding["configuration-path"]
    document = parse_json_strict(path.read_bytes())
    if change == "blocked":
        document = blocked_ruby_governance(operation.registry)
    elif change == "too-long":
        document["expires-at"] = (CURRENT + timedelta(days=8)).isoformat()
    else:
        document["attestation-digest"] = "sha256:" + "9" * 64
    path.write_bytes(canonicalize(document))
    with pytest.raises(ValueError, match=r"Ruby|canonical|disabled"):
        operation.require_current(CURRENT)


@pytest.mark.parametrize(
    "change", ["missing", "symlink", "digest", "empty", "noncanonical"]
)
def test_hosted_download_requires_present_original_raw_bytes(
    tmp_path, monkeypatch, change
):
    """Action status cannot replace the selected downloaded file."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    reference = case.add("plan", {"valid": True})
    path = case.directory / reference.payload_path
    if change == "missing":
        path.rename(path.with_suffix(".lost"))
    elif change == "symlink":
        moved = path.with_suffix(".original")
        path.rename(moved)
        path.symlink_to(moved)
    elif change == "digest":
        path.write_bytes(canonicalize({"valid": False}))
    elif change == "empty":
        path.write_bytes(b"")
    else:
        payload = b'{ "valid": true }'
        path.write_bytes(payload)
        case.references["plan"] = replace(
            reference,
            artifact_digest=ruby_digest(payload),
            payload_digest=ruby_digest(payload),
        ).to_document()
    with pytest.raises(ValueError, match=r"Ruby|canonical|disabled"):
        case.inputs().document("plan")


@pytest.mark.parametrize(
    "change",
    [
        "zero-id",
        "other-run",
        "alias-id",
        "alias-path",
        "extra-role",
        "missing-id",
    ],
)
def test_hosted_artifact_reference_map_is_closed_and_unambiguous(
    tmp_path, monkeypatch, change
):
    """Unknown roles and duplicate ID/path selectors cannot create DAG edges."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    case.add("plan", {"plan": True})
    raw = case.references["plan"]
    if change == "zero-id":
        raw["artifact-id"] = 0
    elif change == "other-run":
        raw["artifact-url"] = raw["artifact-url"].replace("/991/", "/992/")
    elif change == "alias-id":
        raw["artifact-id"] = case.references["operation"]["artifact-id"]
        raw["artifact-url"] = case.references["operation"]["artifact-url"]
    elif change == "alias-path":
        raw["payload-path"] = case.references["operation"]["payload-path"]
    elif change == "extra-role":
        case.references["unknown"] = case.references.pop("plan")
    else:
        raw.pop("artifact-id")
    with pytest.raises((ValueError, KeyError)):
        case.inputs()


@pytest.mark.parametrize("bootstrap", [True, False])
@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_hosted_upload_binds_exact_destination_producer_and_readback(
    tmp_path, monkeypatch, bootstrap, destination
):
    """The upload role selects the actual job from the fixed map."""
    case = hosted_case(tmp_path, destination=destination, bootstrap=bootstrap)
    case.admit(monkeypatch)
    inputs = case.inputs()
    path = case.directory / "authorization.json"
    path.write_bytes(canonicalize({"modeled-authorization": True}))
    digest = ruby_digest(path.read_bytes())
    publisher = (
        "publish-ruby-bootstrap-" if bootstrap else "publish-ruby-"
    ) + destination
    monkeypatch.setenv("GITHUB_JOB", publisher)
    inputs.bind(
        "authorization",
        path,
        "8001",
        digest,
        "https://github.com/hcoona/three/actions/runs/991/artifacts/8001",
    )
    assert inputs.transport("authorization").producer == publisher
    assert inputs.transport("authorization").run_attempt == (
        1 if bootstrap else None
    )
    assert inputs.content("authorization") == path.read_bytes()
    assert (
        parse_json_strict((case.directory / "references.json").read_bytes())
        == inputs.references
    )
    with pytest.raises(ValueError, match="already spent"):
        inputs.bind(
            "authorization",
            path,
            "8002",
            digest,
            "https://github.com/hcoona/three/actions/runs/991/artifacts/8002",
        )


@pytest.mark.parametrize(
    "producer",
    [
        "publish-ruby",
        "publish-ruby-bootstrap",
        "publish-ruby-github-packages",
        "publish-ruby-bootstrap-github-packages",
        "build-ruby",
    ],
)
def test_hosted_upload_rejects_legacy_swapped_or_unprivileged_producer(
    tmp_path, monkeypatch, producer
):
    """An apparently successful upload cannot confer another job's authority."""
    case = hosted_case(tmp_path)
    case.admit(monkeypatch)
    inputs = case.inputs()
    path = case.directory / "marker.json"
    path.write_bytes(b"{}")
    monkeypatch.setenv("GITHUB_JOB", producer)
    with pytest.raises(ValueError, match="different actual producer"):
        inputs.bind(
            "marker",
            path,
            "8001",
            ruby_digest(b"{}"),
            "https://github.com/hcoona/three/actions/runs/991/artifacts/8001",
        )
    assert not (case.directory / "references.json").exists()
    assert not (case.directory / "outputs").exists()


def test_hosted_admission_retains_procedural_provenance_limit(
    tmp_path, monkeypatch
):
    """Modeled independent names pass structure but never prove real review."""
    case = hosted_case(tmp_path)
    operation = case.admit(monkeypatch)
    assert (
        operation.document["review"]["reviewer"]
        == "fixture-independent-reviewer"
    )
    assert operation.document["review"]["author"] == "fixture-request-author"
    assert "independent-audit" not in operation.document
    assert "normal-live-completion" not in operation.document
