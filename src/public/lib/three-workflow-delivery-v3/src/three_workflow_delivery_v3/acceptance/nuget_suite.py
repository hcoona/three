"""The fixed three-probe NuGet sequence over separately admitted operations.

This module performs no IO and grants no native authority. The concrete operator
owns one-time effects and original provenance; independent native and atomic
admission remain separate from a completed candidate observation.
"""

from __future__ import annotations

import base64
import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, Protocol, cast

from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_consumer import (
    NuGetConsumerRequest,
)
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    NuGetStateEvidence,
    _object,
    _require,
    _sha,
    require_creation_delta,
    require_unchanged_delta,
)
from three_workflow_delivery_v3.acceptance.nuget_operator import (
    _POSITIONS,
    NuGetReadRequest,
)
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.acceptance.nuget_probe_evidence import (
        NuGetProbeEvidence,
    )


@dataclass(frozen=True)
class NuGetSuitePlan:
    """Static inputs and bounded components, never an execution grant."""

    probe_inputs: bytes
    reads: NuGetReadRequest
    consumer: NuGetConsumerRequest

    def __post_init__(self) -> None:
        """Align fixed subjects without inventing future operation facts."""
        _require(
            type(self.reads) is NuGetReadRequest
            and type(self.consumer) is NuGetConsumerRequest,
            "suite requires bounded read and consumer requests",
        )
        _require(
            tuple(item.label for item in self.reads.captures) == _POSITIONS,
            "suite requires all six capture positions",
        )
        first = self.reads.captures[0]
        inputs = _object(parse_canonical_json(self.probe_inputs))
        fixed: dict[str, JsonValue] = {
            "repository": native.NUGET_REPOSITORY,
            "repositoryId": 1102295886,
            "actorId": 712433,
            "workflowPath": probe.WORKFLOW_PATH,
            "runAttempt": 1,
            "generation": first.generation,
            "toolingSha": first.tooling_sha,
            "packageId": native.NUGET_PACKAGE_ID,
            "containerId": 12024661,
            "maximumPublicationInvocations": 1,
            "publicationRetries": 0,
            "retentionDays": 45,
            "purpose": "destination-acceptance",
        }
        _require(
            set(inputs)
            == set(fixed)
            | {
                "fixture",
                "preflightSha256",
                "serviceIndexBase64",
                "operationProfile",
            }
            and all(
                type(inputs.get(key)) is type(value) and inputs[key] == value
                for key, value in fixed.items()
            ),
            "suite static probe scope is incomplete or changed",
        )
        fixture = _object(inputs["fixture"])
        _require(
            set(fixture)
            == {
                "producerToolingSha",
                "producerRunId",
                "target",
                "reference",
                "helperReference",
                "helperSourceInputs",
                "independentAuditSha256",
                "inspection",
            },
            "suite fixture inputs are incomplete",
        )
        for value, length in (
            (inputs["preflightSha256"], 64),
            (fixture["independentAuditSha256"], 64),
            (fixture["target"], 40),
        ):
            _require(
                type(value) is str
                and re.fullmatch(rf"[0-9a-f]{{{length}}}", value) is not None,
                "suite requires exact prior evidence and target identities",
            )
        reference = artifact_reference_from_document(
            _object(fixture["reference"])
        )
        _require(
            fixture["producerToolingSha"] == self.reads.helper_tooling_sha
            and fixture["producerRunId"] == self.reads.helper_run_id
            and fixture["helperReference"]
            == self.reads.helper_reference.to_document()
            and fixture["helperSourceInputs"]
            == dict(self.reads.helper_source_inputs)
            and reference.artifact_id != self.reads.helper_reference.artifact_id
            and reference.artifact_digest == reference.payload_digest
            and reference.artifact_url
            == f"https://github.com/hcoona/three/actions/runs/{self.reads.helper_run_id}/artifacts/{reference.artifact_id}",
            "suite original fixture/helper lineage changed",
        )
        inspection = _object(fixture["inspection"])
        _require(
            set(inspection) == {"original", "comparison", "witness"},
            "suite requires the complete original inspection",
        )
        original, comparison = (
            _object(inspection[key]) for key in ("original", "comparison")
        )
        original_id, comparison_id = (
            _object(item["identity"]) for item in (original, comparison)
        )
        _require(
            original_id["normalizedPackageId"]
            == comparison_id["normalizedPackageId"]
            == native.NUGET_PACKAGE_ID.lower()
            and original_id["normalizedVersion"]
            == comparison_id["normalizedVersion"]
            == self.consumer.version
            and original_id["displayVersion"] == first.version
            and original["sha256"] == "sha256:" + self.consumer.package_sha256
            and original["sha256"] != comparison["sha256"]
            and _sha(canonicalize(inspection["witness"]))
            == self.consumer.witness_sha256
            and self.consumer.generation == first.generation
            and self.consumer.tooling_sha == first.tooling_sha
            and first.container_id == fixed["containerId"],
            "suite capture, fixture or consumer subject changed",
        )
        index = base64.b64decode(
            cast("str", inputs["serviceIndexBase64"]), validate=True
        )
        _require(
            bool(index) and _sha(index) == self.consumer.service_index_sha256,
            "suite service-index bytes changed",
        )
        profile = _object(inputs["operationProfile"])
        native.validate_nuget_operation_profile(profile)
        _require(
            profile["platform"] == "Windows",
            "suite requires the admitted Windows profile",
        )

    @property
    def coordinate(self) -> str:
        """Use the aligned official fixture/consumer native coordinate."""
        return native.NUGET_PACKAGE_ID.lower() + "@" + self.consumer.version

    def to_document(self) -> dict[str, JsonValue]:
        """Retain static inputs and the fixed sequence, without future facts."""
        return {
            "schema": "workflow-delivery/v3/nuget-suite-plan",
            "probeInputs": _object(parse_canonical_json(self.probe_inputs)),
            "readRequest": self.reads.to_document(),
            "consumerRequest": self.consumer.to_document(),
            "sequence": list[JsonValue](probe.SCENARIOS),
            "maximumDispatches": len(probe.SCENARIOS),
            "maximumPublicationInvocations": len(probe.SCENARIOS),
            "reruns": 0,
            "retries": 0,
        }

    def probe_spec(self, scenario: str, before: NuGetStateEvidence) -> bytes:
        """Derive only the selected scenario and actual before-capture hash."""
        _require(scenario in probe.SCENARIOS, "unsupported suite scenario")
        document = _object(parse_canonical_json(self.probe_inputs))
        document.update(
            schema=probe.SPEC_SCHEMA,
            scenario=scenario,
            beforeCaptureSha256=before.capture_sha256,
        )
        return canonicalize(document)


class NuGetSuiteOperations(Protocol):
    """The concrete one-use operator owns effects and retained originals."""

    def capture(self, label: str) -> NuGetStateEvidence:
        """Spend and collect the next existing bounded capture position."""
        ...

    def probe(self, spec: bytes) -> NuGetProbeEvidence:
        """Dispatch once and collect that exact run's original evidence."""
        ...

    def consume(self) -> str:
        """Consume A once and return the admitted original completion digest."""
        ...


def run_nuget_suite(
    plan: NuGetSuitePlan,
    operations: NuGetSuiteOperations,
    *,
    preflight: NuGetStateEvidence,
    original: bytes,
    witness: bytes,
) -> dict[str, JsonValue]:
    """Require each prior result before the next mutation; never retry.

    The operator must reserve one fresh lifetime before calling this function,
    persist partial evidence on failure and admit actual source/access/runtime
    provenance. A callback result alone cannot prove an actual native operation.
    """
    inputs = _object(parse_canonical_json(plan.probe_inputs))
    resources = {
        "serviceIndexSha256": plan.consumer.service_index_sha256,
        "packageBaseAddress": plan.consumer.package_base_address,
        "packagePublish": _object(inputs["operationProfile"])["packagePublish"],
    }
    _require(
        preflight.capture_sha256 == inputs["preflightSha256"]
        and preflight.coordinate == plan.coordinate
        and preflight.resources == resources
        and preflight.package is None
        and plan.coordinate not in dict(preflight.objects).values()
        and _sha(original) == plan.consumer.package_sha256
        and _sha(witness) == plan.consumer.witness_sha256,
        "suite preflight or original consumer inputs changed",
    )
    captures: dict[str, JsonValue] = {}
    probes: list[JsonValue] = []
    runs: set[int] = set()
    previous = preflight
    consumer_digest = None
    for index, scenario in enumerate(probe.SCENARIOS):
        before_label, after_label = _POSITIONS[2 * index : 2 * index + 2]
        before = operations.capture(before_label)
        _require(
            before.coordinate == plan.coordinate
            and before.resources == resources,
            "suite before-capture subject changed",
        )
        captures[before_label] = before.capture_sha256
        if index == 0:
            _require(
                before.objects == preflight.objects
                and before.control == preflight.control
                and before.package is None,
                "suite state changed after preflight",
            )
        else:
            require_unchanged_delta(
                previous, before, original=original, witness=witness
            )
        spec = plan.probe_spec(scenario, before)
        observed = operations.probe(spec)
        actual_spec = observed.request.to_document()
        actual_spec.pop("workflowRunId")
        actual_spec["schema"] = probe.SPEC_SCHEMA
        created = scenario == "create"
        _require(
            canonicalize(actual_spec) == spec
            and observed.request.workflow_run_id not in runs
            and observed.status == (201 if created else 409)
            and observed.http_created is created
            and observed.possibly_mutated is (not created),
            "suite probe request, run or definitive outcome changed",
        )
        runs.add(observed.request.workflow_run_id)
        after = operations.capture(after_label)
        captures[after_label] = after.capture_sha256
        if created:
            require_creation_delta(
                before, after, original=original, witness=witness
            )
            consumer_digest = operations.consume()
            _require(
                type(consumer_digest) is str
                and re.fullmatch(r"[0-9a-f]{64}", consumer_digest) is not None,
                "suite consumer completion is missing",
            )
        else:
            require_unchanged_delta(
                before, after, original=original, witness=witness
            )
        previous = after
        probes.append(
            {
                "scenario": scenario,
                "runId": observed.request.workflow_run_id,
                "requestDigest": observed.request.request_digest,
                "resultSha256": observed.result_sha256,
                "runMetadataSha256": observed.raw_run_sha256,
                "artifactMetadataSha256": observed.raw_artifacts_sha256,
                "artifacts": [
                    reference.to_document() for reference in observed.artifacts
                ],
                "status": observed.status,
                "httpCreated": observed.http_created,
                "possiblyMutated": observed.possibly_mutated,
            }
        )
    return {
        "schema": "workflow-delivery/v3/nuget-suite-observation",
        "suite": "workflow-delivery-v3/native-nuget-suite/v1",
        "planDigest": canonical_sha256(plan.to_document()),
        "generation": plan.consumer.generation,
        "toolingSha": plan.consumer.tooling_sha,
        "coordinate": plan.coordinate,
        "profileDigest": canonical_sha256(inputs["operationProfile"]),
        "preflightSha256": preflight.capture_sha256,
        "captures": captures,
        "probes": probes,
        "consumerSha256": consumer_digest,
        "nativeAdmissionEstablished": False,
        "atomicAssuranceEstablished": False,
        "evidenceLevel": (
            "candidate sequential observations; "
            "independent native audit required"
        ),
    }
