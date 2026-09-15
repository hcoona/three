"""Suite ownership with controlled capture, probe and consumer seams."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import json
import time
from dataclasses import replace
from importlib import import_module
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import (
    nuget_suite_operator as operator,
)
from three_workflow_delivery_v3.acceptance.nuget_github import NuGetGitHubLimits
from three_workflow_delivery_v3.acceptance.nuget_suite import NuGetSuitePlan
from three_workflow_delivery_v3.canonical import (
    canonicalize,
)

_fixtures = import_module(".test_nuget_probe", __package__)
_sha = _fixtures._sha  # noqa: SLF001
inputs = _fixtures.inputs
suite = import_module(".test_nuget_suite", __package__).suite


@pytest.fixture
def prepared(inputs, suite, tmp_path, monkeypatch):
    monkeypatch.delenv("GITHUB_ACTIONS", raising=False)
    audits = {}
    for name in ("admission", "fixture", "helper"):
        path = tmp_path / (name + "-audit.md")
        path.write_bytes(("Controlled " + name + " audit").encode())
        audits[name] = path
    static = json.loads(suite.plan.probe_inputs)
    static["fixture"]["independentAuditSha256"] = _sha(
        audits["fixture"].read_bytes()
    )
    read_request = replace(
        suite.plan.reads,
        helper_audit_sha256=_sha(audits["helper"].read_bytes()),
    )
    suite.plan = NuGetSuitePlan(
        canonicalize(static), read_request, suite.plan.consumer
    )
    preflight_request = replace(
        read_request,
        captures=(replace(read_request.captures[0], label="preflight"),),
    )
    request = operator.NuGetSuiteRequest(
        suite.plan,
        NuGetGitHubLimits(
            60,
            10_000_000,
            100_000,
            1_000_000,
            10,
            2,
            0.01,
            "github-api-location-v1",
        ),
        preflight_request,
        _sha(audits["admission"].read_bytes()),
    )
    preflight = tmp_path / "original-preflight"
    preflight.mkdir()
    body = canonicalize(preflight_request.captures[0].to_document())
    (preflight / "request.json").write_bytes(body)
    (preflight / "capture.json").write_bytes(
        canonicalize(
            {
                "files": {
                    "request.json": {"bytes": len(body), "sha256": _sha(body)}
                }
            }
        )
    )
    root = tmp_path / "checkout"
    root.mkdir()
    local = operator.NuGetSuiteInputs(
        root,
        preflight,
        inputs.fixture,
        audits["fixture"],
        inputs.helper_path,
        audits["helper"],
        tmp_path / "host/consumer.dll",
        audits["admission"],
    )
    source = Mock()
    monkeypatch.setattr(operator.reads, "_admit_source", source)

    def read_capture(directory, request):
        if request.label == "preflight":
            assert (directory / "request.json").read_bytes() == body
            return suite.preflight
        return suite.states[request.label]

    class Reader:
        def __init__(self, request, **kwargs):
            self.next_index = 0
            self.directory = kwargs["audit_directory"]
            self.directory.mkdir()
            self.deadline = float("inf")
            assert (
                operator.read_uploaded(
                    kwargs["helper_archive"], request.helper_reference
                )
                == inputs.helper_path.read_bytes()
            )
            assert request == read_request

        def capture(self, label):
            suite.operations.capture(label)
            self.next_index += 1
            directory = self.directory / label
            directory.mkdir()
            path = directory / "capture.json"
            path.write_bytes(canonicalize({"controlledCapture": label}))
            return path

    class Collector:
        def __init__(self, plan, _client, directory):
            assert plan.to_document() == suite.plan.to_document()
            self.directory = directory
            directory.mkdir()

        def collect(self, spec, *, deadline):
            assert deadline > time.monotonic()
            result = suite.operations.probe(spec)
            (self.directory / (result.request.scenario + ".json")).write_bytes(
                canonicalize(result.request.to_document())
            )
            return result

    def consumer(request, **kwargs):
        suite.operations.consume()
        assert request.to_document() == suite.plan.consumer.to_document()
        assert kwargs["original_package"] == suite.original
        assert kwargs["witness"] == suite.witness
        assert kwargs["restore_host"] == local.restore_host
        directory = kwargs["audit_directory"]
        directory.mkdir()
        (directory / "actual-package.nupkg").write_bytes(suite.original)
        (directory / "actual-witness.json").write_bytes(suite.witness)
        result = directory / "consumer.json"
        result.write_bytes(canonicalize({"controlledCompletion": True}))
        return result

    monkeypatch.setattr(operator, "read_capture_evidence", read_capture)
    monkeypatch.setattr(operator.reads, "NuGetReadOperator", Reader)
    monkeypatch.setattr(operator, "NuGetProbeCollector", Collector)
    monkeypatch.setattr(operator, "run_nuget_consumer", consumer)
    return SimpleNamespace(
        request=request,
        inputs=local,
        suite=suite,
        source=source,
        audit=tmp_path / "suite-audit",
    )


def _operator(prepared):
    return operator.NuGetSuiteOperator(
        prepared.request, prepared.inputs, prepared.audit, "controlled-token"
    )


def test_suite_operator_retains_originals_and_completes_once(prepared):
    instance = _operator(prepared)
    assert not prepared.suite.events
    completed = instance.execute()
    result = json.loads(completed.read_bytes())
    assert result["nativeAdmissionEstablished"] is False
    assert result["atomicAssuranceEstablished"] is False
    assert len(result["captures"]) == 6
    assert len(result["probes"]) == 3
    assert result["consumerSha256"] == _sha(
        (prepared.audit / "consumer/consumer.json").read_bytes()
    )
    assert result["admissionSha256"] == _sha(
        prepared.inputs.admission.read_bytes()
    )
    for retained, original in (
        ("fixture-original.zip", prepared.inputs.fixture_archive),
        (
            "helper-upload/"
            + prepared.request.plan.reads.helper_reference.payload_path,
            prepared.inputs.helper_archive,
        ),
    ):
        assert (prepared.audit / retained).read_bytes() == original.read_bytes()
    files = result["files"]
    assert "preflight/request.json" in files
    assert files["consumer/actual-package.nupkg"] == _sha(
        prepared.suite.original
    )
    assert files["consumer/actual-witness.json"] == _sha(prepared.suite.witness)
    assert files["consumer-files.json"] == _sha(
        (prepared.audit / "consumer-files.json").read_bytes()
    )
    events = list(prepared.suite.events)
    assert events.index("consume") < events.index("probe:identical-duplicate")
    with pytest.raises(ValueError, match="lifetime already spent"):
        instance.execute()
    with pytest.raises(ValueError, match="not running"):
        instance.probe(b"{}")
    with pytest.raises(ValueError, match="not running"):
        instance.capture("before-identical")
    with pytest.raises(ValueError, match="not running"):
        instance.consume()
    assert prepared.suite.events == events
    assert not (prepared.audit / "suite-failed.json").exists()
    with pytest.raises(FileExistsError):
        _operator(prepared)


@pytest.mark.parametrize("failure", ["consumer", "probe", "after-state"])
def test_suite_operator_failure_retains_partial_evidence_and_never_resumes(
    prepared, failure
):
    if failure == "consumer":
        prepared.suite.operations.consume.side_effect = ValueError(
            "controlled consumer failure"
        )
    elif failure == "probe":
        prepared.suite.operations.probe.side_effect = ValueError(
            "controlled probe failure"
        )
    else:
        prepared.suite.states["after-create"] = replace(
            prepared.suite.states["after-create"], package=b"changed"
        )
    instance = _operator(prepared)
    with pytest.raises(ValueError, match=r"controlled|bytes"):
        instance.execute()
    events = list(prepared.suite.events)
    assert "probe:identical-duplicate" not in events
    with pytest.raises(ValueError, match="lifetime already spent"):
        instance.execute()
    with pytest.raises(ValueError, match="not running"):
        instance.probe(b"{}")
    assert prepared.suite.events == events
    assert not (prepared.audit / "suite-observation.json").exists()
    result = json.loads((prepared.audit / "suite-failed.json").read_bytes())
    assert result["generationSpent"] is True
    assert result["nativeAdmissionEstablished"] is False
    assert (prepared.audit / "started.json").is_file()
    assert (prepared.audit / "captures/before-create/capture.json").is_file()


@pytest.mark.parametrize(
    "change", ["extra", "budget", "sequence", "preflight", "admission"]
)
def test_suite_request_rejects_unbound_inputs(prepared, change):
    document = prepared.request.to_document()
    if change == "extra":
        document["authorized"] = True
    elif change == "budget":
        document["github"]["artifactRedirects"] = 2
    elif change == "sequence":
        document["plan"]["sequence"].reverse()
    elif change == "preflight":
        document["preflight"]["captures"][0]["callerToolingSha"] = "f" * 40
    else:
        document["admissionSha256"] = ""
    with pytest.raises(ValueError, match=r"closure|subject|admission"):
        operator.read_suite_request(canonicalize(document))
    assert not prepared.suite.events


@pytest.mark.parametrize(
    "change", ["old", "mixed", "missing", "unknown", "extra"]
)
def test_suite_request_requires_closed_artifact_redirect_policy(
    prepared, change
):
    document = prepared.request.to_document()
    github = document["github"]
    if change in {"old", "missing"}:
        del github["artifactRedirectPolicy"]
    if change in {"old", "mixed"}:
        github["storageOrigin"] = "https://earlier-storage.example"
    elif change == "unknown":
        github["artifactRedirectPolicy"] = "github-api-location-v2"
    elif change == "extra":
        github["rememberPreviousOrigin"] = True
    with pytest.raises(ValueError, match=r"closure|redirect policy"):
        operator.read_suite_request(canonicalize(document))
    assert not prepared.suite.events


@pytest.mark.parametrize(
    "change", ["old-schema", "missing-policy", "unknown-policy"]
)
def test_suite_request_rejects_previous_consumer_redirect_contract(
    prepared, change
):
    document = prepared.request.to_document()
    consumer = document["plan"]["consumerRequest"]
    if change == "old-schema":
        consumer["schema"] = "workflow-delivery/v3/nuget-consumer-request"
    elif change == "missing-policy":
        del consumer["packageRedirectPolicy"]
    else:
        consumer["packageRedirectPolicy"] = "automatic"
    with pytest.raises(ValueError, match="closure mismatch"):
        operator.read_suite_request(canonicalize(document))
    assert not prepared.suite.events


def test_suite_cli_uses_only_the_supplied_request_and_token(
    prepared, monkeypatch, tmp_path
):
    request = tmp_path / "request.json"
    request.write_bytes(canonicalize(prepared.request.to_document()))
    runner = Mock()
    factory = Mock(return_value=runner)
    monkeypatch.setattr(operator, "NuGetSuiteOperator", factory)
    monkeypatch.setenv("WDV3_CONTROLLED_TOKEN", "controlled-cli-value")
    arguments = [
        "--request",
        str(request),
        "--audit-directory",
        str(prepared.audit),
        "--token-env",
        "WDV3_CONTROLLED_TOKEN",
    ]
    for name in prepared.inputs.__dataclass_fields__:
        arguments.extend(
            ["--" + name.replace("_", "-"), str(getattr(prepared.inputs, name))]
        )
    assert operator.main(arguments) == 0
    factory.assert_called_once()
    supplied, *remaining = factory.call_args.args
    assert supplied.to_document() == prepared.request.to_document()
    assert remaining == [
        prepared.inputs,
        prepared.audit,
        "controlled-cli-value",
    ]
    runner.execute.assert_called_once_with()
    runner.execute.side_effect = ValueError(
        "controlled confidential diagnostic"
    )
    assert operator.main(arguments) == 1


@pytest.mark.parametrize(
    "changed", ["fixture_archive", "helper_archive", "admission"]
)
def test_suite_operator_rejects_changed_originals_before_effects(
    prepared, changed
):
    path = getattr(prepared.inputs, changed)
    path.write_bytes(path.read_bytes() + b"changed")
    with pytest.raises(ValueError, match=r"bytes changed|artifact binding"):
        _operator(prepared)
    assert not prepared.suite.events
    assert not (prepared.audit / "started.json").exists()


def test_suite_operator_preserves_remaining_consumer_deadline(
    prepared, monkeypatch
):
    instance = _operator(prepared)
    original_capture = instance.capture

    def capture(label):
        result = original_capture(label)
        if label == "after-create":
            instance.deadline = time.monotonic() + 0.5
        return result

    monkeypatch.setattr(instance, "capture", capture)
    with pytest.raises(ValueError, match="insufficient generation time"):
        instance.execute()
    assert "consume" not in prepared.suite.events
    assert "probe:identical-duplicate" not in prepared.suite.events
    assert (prepared.audit / "suite-failed.json").is_file()
    assert not (prepared.audit / "suite-observation.json").exists()


def test_suite_operator_rejects_late_final_evidence(prepared, monkeypatch):
    instance = _operator(prepared)
    original = operator.reads._runtime_files  # noqa: SLF001

    def collect(directory):
        files = original(directory)
        if directory == prepared.audit:
            instance.deadline = time.monotonic() - 1
        return files

    monkeypatch.setattr(operator.reads, "_runtime_files", collect)
    with pytest.raises(ValueError, match="evidence completed late"):
        instance.execute()
    assert not (prepared.audit / "suite-observation.json").exists()
    assert (prepared.audit / "suite-failed.json").is_file()
