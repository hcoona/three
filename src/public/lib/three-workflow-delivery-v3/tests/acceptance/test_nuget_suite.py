"""Required suite ordering and failure stops with controlled operations."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import json
from dataclasses import replace
from importlib import import_module
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_capture import (
    NuGetCaptureLimits,
    NuGetCaptureRequest,
)
from three_workflow_delivery_v3.acceptance.nuget_consumer import (
    NuGetConsumerLimits,
    NuGetConsumerRequest,
)
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    NuGetStateEvidence,
)
from three_workflow_delivery_v3.acceptance.nuget_operator import (
    _POSITIONS,
    NuGetReadRequest,
)
from three_workflow_delivery_v3.acceptance.nuget_probe_evidence import (
    NuGetProbeEvidence,
)
from three_workflow_delivery_v3.acceptance.nuget_suite import (
    NuGetSuiteOperations,
    NuGetSuitePlan,
    run_nuget_suite,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference

_fixtures = import_module(".test_nuget_probe", __package__)
BASE, PUBLISH, A = _fixtures.BASE, _fixtures.PUBLISH, _fixtures.A
_sha = _fixtures._sha  # noqa: SLF001
inputs = _fixtures.inputs


@pytest.fixture
def suite(inputs):
    request = inputs.request
    static = request.to_document()
    for key in ("schema", "workflowRunId", "scenario", "beforeCaptureSha256"):
        static.pop(key)
    captures = tuple(
        NuGetCaptureRequest(
            request.generation,
            label,
            request.tooling_sha,
            request.helper_reference.payload_digest[7:],
            12024661,
            request.inspection["original"]["identity"]["displayVersion"],
            NuGetCaptureLimits(10, 2, 1_000_000, 10, 60),
        )
        for label in _POSITIONS
    )
    reads = NuGetReadRequest(
        captures,
        3600,
        request.helper_reference,
        request.fixture_tooling_sha,
        request.fixture_run_id,
        "a" * 64,
        request.helper_source_inputs,
        20,
        100_000,
        10,
        "b" * 64,
        "c" * 64,
        {},
    )
    witness = canonicalize(request.inspection["witness"])
    consumer = NuGetConsumerRequest(
        request.generation,
        request.tooling_sha,
        request.inspection["original"]["identity"]["normalizedVersion"],
        _sha(A),
        _sha(witness),
        _sha(request.service_index),
        BASE,
        tuple(
            (name, "d" * 64)
            for name in (
                "WorkflowDeliveryV3NuGetConsumer.dll",
                "WorkflowDeliveryV3NuGetConsumer.deps.json",
                "WorkflowDeliveryV3NuGetConsumer.runtimeconfig.json",
                "NuGet.Commands.dll",
                "NuGet.Protocol.dll",
            )
        ),
        "b" * 64,
        NuGetConsumerLimits(6, 100_000, 30, 60, 120, 100_000),
    )
    plan = NuGetSuitePlan(canonicalize(static), reads, consumer)
    resources = {
        "serviceIndexSha256": consumer.service_index_sha256,
        "packageBaseAddress": BASE,
        "packagePublish": PUBLISH,
    }
    neighbor = probe.native.NUGET_PACKAGE_ID.lower() + "@0.0.1"
    preflight = NuGetStateEvidence(
        request.preflight_sha256,
        plan.coordinate,
        ((10, neighbor),),
        resources,
        (
            12024661,
            probe.native.NUGET_PACKAGE_ID,
            "nuget",
            "public",
            "hcoona/three",
        ),
        None,
        None,
    )
    present = replace(
        preflight,
        objects=(*preflight.objects, (20, plan.coordinate)),
        package=A,
        witness=witness,
    )
    states = {
        label: replace(
            preflight if index == 0 else present,
            capture_sha256=_sha(label.encode()),
        )
        for index, label in enumerate(_POSITIONS)
    }
    events, specs = [], []
    operations = Mock(spec=NuGetSuiteOperations)

    def capture(label):
        events.append("capture:" + label)
        return states[label]

    def observe(spec):
        document = json.loads(spec)
        events.append("probe:" + document["scenario"])
        specs.append(document.copy())
        document.update(
            schema=probe.REQUEST_SCHEMA, workflowRunId=900 + len(specs)
        )
        current = probe.read_probe_request(canonicalize(document))
        created = current.scenario == "create"
        references = tuple(
            ArtifactReference(
                current.workflow_run_id * 10 + index,
                "sha256:" + "1" * 64,
                "https://github.com/hcoona/three/actions/runs/"
                f"{current.workflow_run_id}/artifacts/"
                f"{current.workflow_run_id * 10 + index}",
                role + ".zip",
                "sha256:" + "1" * 64,
            )
            for index, role in enumerate(("prepared", "prepare", "publish"))
        )
        return NuGetProbeEvidence(
            current,
            references,
            "2" * 64,
            "3" * 64,
            "4" * 64,
            201 if created else 409,
            created,
            not created,
            b"controlled response",
        )

    def consume():
        events.append("consume")
        return "5" * 64

    operations.capture.side_effect = capture
    operations.probe.side_effect = observe
    operations.consume.side_effect = consume
    return SimpleNamespace(
        plan=plan,
        preflight=preflight,
        original=A,
        witness=witness,
        operations=operations,
        states=states,
        events=events,
        specs=specs,
    )


def _run(suite):
    return run_nuget_suite(
        suite.plan,
        suite.operations,
        preflight=suite.preflight,
        original=suite.original,
        witness=suite.witness,
    )


@pytest.mark.parametrize("status", [200, 201, 202])
def test_fixed_suite_preserves_all_six_captures_and_consumes_before_duplicates(
    suite,
    status,
):
    observe = suite.operations.probe.side_effect

    def response(spec):
        result = observe(spec)
        return replace(result, status=status) if result.http_created else result

    suite.operations.probe.side_effect = response
    result = _run(suite)
    assert [record["status"] for record in result["probes"]] == [
        status,
        409,
        409,
    ]
    assert suite.events == [
        "capture:before-create",
        "probe:create",
        "capture:after-create",
        "consume",
        "capture:before-identical",
        "probe:identical-duplicate",
        "capture:after-identical",
        "capture:before-equivalent",
        "probe:equivalent-duplicate",
        "capture:after-equivalent",
    ]
    assert list(result["captures"]) == list(_POSITIONS)
    assert [record["runId"] for record in result["probes"]] == [901, 902, 903]
    assert [record["httpCreated"] for record in result["probes"]] == [
        True,
        False,
        False,
    ]
    assert [record["possiblyMutated"] for record in result["probes"]] == [
        False,
        True,
        True,
    ]
    for index, spec in enumerate(suite.specs):
        assert "workflowRunId" not in spec
        assert spec["scenario"] == probe.SCENARIOS[index]
        assert (
            spec["beforeCaptureSha256"]
            == result["captures"][_POSITIONS[2 * index]]
        )
        assert spec["fixture"] == json.loads(suite.plan.probe_inputs)["fixture"]
    assert result["consumerSha256"] == "5" * 64
    assert result["nativeAdmissionEstablished"] is False
    assert result["atomicAssuranceEstablished"] is False


@pytest.mark.parametrize("status", [200, 202])
@pytest.mark.parametrize("field", ["package", "witness"])
def test_suite_success_status_requires_exact_bytes_and_witness(
    suite, status, field
):
    observe = suite.operations.probe.side_effect
    suite.operations.probe.side_effect = lambda spec: replace(
        observe(spec), status=status
    )
    suite.states["after-create"] = replace(
        suite.states["after-create"], **{field: b"different original bytes"}
    )
    with pytest.raises(ValueError, match="creation"):
        _run(suite)
    assert suite.operations.probe.call_count == 1
    suite.operations.consume.assert_not_called()


@pytest.mark.parametrize(
    ("scenario", "status", "probes"),
    [
        ("create", 204, 1),
        ("create", 409, 1),
        ("identical-duplicate", 200, 2),
        ("equivalent-duplicate", 202, 3),
    ],
)
def test_suite_rejects_unselected_probe_status(suite, scenario, status, probes):
    observe = suite.operations.probe.side_effect

    def changed(spec):
        result = observe(spec)
        return (
            replace(result, status=status)
            if result.request.scenario == scenario
            else result
        )

    suite.operations.probe.side_effect = changed
    with pytest.raises(ValueError, match="definitive outcome changed"):
        _run(suite)
    assert suite.operations.probe.call_count == probes
    assert suite.operations.capture.call_count == 2 * probes - 1


@pytest.mark.parametrize(
    ("change", "probes"),
    [
        ("preflight", 0),
        ("before-create", 0),
        ("create-fails", 1),
        ("substituted-probe", 1),
        ("after-create", 1),
        ("consumer", 1),
        ("before-identical", 1),
        ("duplicate-fails", 2),
        ("after-identical", 2),
        ("repeated-run", 2),
        ("after-equivalent", 3),
    ],
)
def test_suite_stops_before_later_mutations_on_failed_prerequisite(
    suite, change, probes
):
    if change == "preflight":
        suite.preflight = replace(suite.preflight, capture_sha256="0" * 64)
    elif change in {
        "before-create",
        "after-create",
        "before-identical",
        "after-identical",
        "after-equivalent",
    }:
        state = suite.states[change]
        suite.states[change] = replace(
            state, objects=(*state.objects, (30, "unexpected@0.0.2"))
        )
    elif change == "consumer":
        suite.operations.consume.side_effect = lambda: None
    else:
        observe = suite.operations.probe.side_effect

        def changed(spec):
            result = observe(spec)
            if change == "create-fails" or (
                change == "duplicate-fails"
                and result.request.scenario == "identical-duplicate"
            ):
                message = "controlled failed probe"
                raise ValueError(message)
            if change == "substituted-probe":
                return replace(
                    result,
                    request=replace(
                        result.request, before_capture_sha256="0" * 64
                    ),
                )
            if change == "repeated-run":
                return replace(
                    result, request=replace(result.request, workflow_run_id=901)
                )
            return result

        suite.operations.probe.side_effect = changed
    with pytest.raises(ValueError, match=r"suite|probe|creation|active state"):
        _run(suite)
    assert suite.operations.probe.call_count == probes
    assert suite.operations.consume.call_count <= 1
    assert len(suite.events) < 10 or change == "after-equivalent"


@pytest.mark.parametrize(
    "change",
    [
        "future-run",
        "future-hash",
        "scenario",
        "source",
        "generation",
        "helper",
        "consumer",
        "profile",
        "captures",
    ],
)
def test_suite_plan_rejects_misaligned_static_subjects(suite, change):
    static = json.loads(suite.plan.probe_inputs)
    reads, consumer = suite.plan.reads, suite.plan.consumer
    if change in {
        "future-run",
        "future-hash",
        "scenario",
        "source",
        "generation",
    }:
        key, value = {
            "future-run": ("workflowRunId", 900),
            "future-hash": ("beforeCaptureSha256", "a" * 64),
            "scenario": ("scenario", "create"),
            "source": ("toolingSha", "f" * 40),
            "generation": ("generation", "other"),
        }[change]
        static[key] = value
    elif change == "helper":
        static["fixture"]["producerRunId"] = 999
    elif change == "consumer":
        consumer = replace(consumer, package_sha256="0" * 64)
    elif change == "profile":
        static["operationProfile"]["platform"] = "Linux"
    else:
        reads = replace(
            reads, captures=(replace(reads.captures[0], label="preflight"),)
        )
    with pytest.raises(ValueError, match="suite"):
        NuGetSuitePlan(canonicalize(static), reads, consumer)
