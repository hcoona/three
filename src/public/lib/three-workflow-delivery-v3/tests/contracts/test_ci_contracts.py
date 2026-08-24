"""Canonical record and admission contracts for the CI model core."""

from __future__ import annotations

import copy
import json
from dataclasses import FrozenInstanceError, replace
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, cast

import pytest
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.ci.evidence import (
    form_ci_evidence,
    form_empty_lane_result,
    form_evidence_lane_result,
)
from three_workflow_delivery_v3.ci.finalizer import finalize_ci_slice
from three_workflow_delivery_v3.records.ci import (
    CI_LANE_IDS,
    CI_WORKFLOW_PATH,
    CiArtifact,
    CiCandidate,
    CiEvidence,
    CiLaneResult,
    CiObligation,
    CiObligationDisposition,
    CiQualificationSnapshot,
    CiSliceDecision,
    CiSliceSummary,
    admit_ci_artifact_json,
    admit_ci_candidate_json,
    admit_ci_evidence_json,
    admit_ci_lane_result_json,
    admit_ci_qualification_snapshot_json,
    admit_ci_slice_decision_json,
    ci_artifact_digest,
    ci_candidate_digest,
    ci_evidence_digest,
    ci_lane_result_digest,
    ci_qualification_snapshot_digest,
    ci_slice_decision_digest,
)

if TYPE_CHECKING:
    from collections.abc import Callable

FIXTURE_ROOT = Path(__file__).parents[1] / "fixtures" / "ci"
SHA_A = "a" * 40
SHA_B = "b" * 40
SHA_C = "c" * 40
DIGEST_C = "sha256:" + ("c" * 64)
DIGEST_D = "sha256:" + ("d" * 64)
DIGEST_E = "sha256:" + ("e" * 64)
SHA512_F = "sha512:" + ("f" * 128)
ELAPSED_SECONDS = 600

GOLDEN_DIGESTS = {
    "pr-candidate": (
        "sha256:0ad5a70c7f93a670b06f7b41ad911468d974f7d5b7b35aac9bcfbf8120a9b428"
    ),
    "manual-candidate": (
        "sha256:66393b1fe56039c8fef6729b41c788c3c1a454954f15c630fbe578e3c31352eb"
    ),
    "ready-plan": (
        "sha256:7bb6d4003e5abb66d42e1c7831bd02ed96eb5f3d704350c309657f1c6d7e9227"
    ),
    "npm-artifact": (
        "sha256:4c6d2537140d55ac57443ee188b00a38f132af69b27220b26b9aee5d720e0065"
    ),
    "empty-lane-result": (
        "sha256:142991d2654858e3e50fb62f61a116a67ad9d6ae45b7f76e59d2f46037b92a9c"
    ),
    "satisfied-evidence": (
        "sha256:bff4704c9afda896484bae3dddc2fca22e89f22f45b1c937d053bd8335a60774"
    ),
    "non-authoritative-decision": (
        "sha256:4d51ad600d8d2a4b75d91c0abec38be1d5fbfb92d9d9e213edc243f2bab611da"
    ),
}


class _Record(Protocol):
    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical record document."""


def _candidate(*, manual: bool = False) -> CiCandidate:
    if manual:
        return CiCandidate(
            event_kind="workflow_dispatch",
            purpose="slice-validation",
            repository="hcoona/three",
            workflow_path=CI_WORKFLOW_PATH,
            workflow_sha=SHA_C,
            request_id="slice-validation-7001",
            producer="request",
            workflow_run_id=7001,
            run_attempt=2,
            selected_ref="refs/heads/feature/manual-slice",
            target=SHA_C,
            base_sha=None,
            head_sha=None,
            tested_merge_sha=None,
        )
    return CiCandidate(
        event_kind="pull_request",
        purpose="ci-pr-slice-shadow",
        repository="hcoona/three",
        workflow_path=CI_WORKFLOW_PATH,
        workflow_sha=SHA_C,
        request_id="pr-42",
        producer="request",
        workflow_run_id=7001,
        run_attempt=2,
        selected_ref="refs/pull/42/merge",
        target=SHA_C,
        base_sha=SHA_A,
        head_sha=SHA_B,
        tested_merge_sha=SHA_C,
    )


def _rebind_plan_document(
    source: dict[str, JsonValue],
    *,
    selected_lanes: tuple[str, ...],
    ready: bool,
    manual: bool = False,
    complete_scope: bool = False,
) -> dict[str, JsonValue]:
    document = copy.deepcopy(source)
    document["selected-outputs"] = cast(
        "list[JsonValue]",
        [
            {
                "output-id": "npm-tarball",
                "logical-role": "primary-package",
                "media-kind": "npm-tarball",
            }
        ],
    )
    if manual:
        document["candidate"] = cast(
            "dict[str, JsonValue]",
            json.loads((FIXTURE_ROOT / "manual-candidate.json").read_bytes()),
        )
        document["scope-mode"] = "slice-validation"
        document["changed-paths"] = []
        document["diagnostics"] = [
            "slice-validation selected the complete first-slice scope"
        ]
    elif complete_scope:
        document["diagnostics"] = ["selected complete first-slice scope"]
    else:
        document["changed-paths"] = ["docs/wiki/README.md"]
        document["selected-project-nodes"] = []
        document["selected-release-units"] = []
        document["selected-variants"] = []
        document["selected-outputs"] = []
        document["diagnostics"] = [
            "incremental comparison selected repository "
            "source-tree conformance only"
        ]
    document["ready"] = ready
    if not ready:
        document["selected-project-nodes"] = []
        document["selected-release-units"] = []
        document["selected-variants"] = []
        document["selected-outputs"] = []
        document["diagnostics"] = ["changed path is unclassified"]

    obligations = cast("list[JsonValue]", document["obligations"])
    expected_evidence_ids: list[JsonValue] = []
    for value in obligations:
        obligation = cast("dict[str, JsonValue]", value)
        lane_id = cast("str", obligation["lane-id"])
        selected = lane_id in selected_lanes
        obligation["selected"] = selected
        obligation["required"] = selected
        request_digest = canonical_sha256(
            {
                "schema": "workflow-delivery/v3/ci-obligation-request",
                "candidate-digest": canonical_sha256(document["candidate"]),
                "repository-model-digest": document["repository-model-digest"],
                "lane-id": lane_id,
                "definition-id": obligation["definition-id"],
                "definition-digest": obligation["definition-digest"],
                "prerequisites": obligation["prerequisites"],
                "selected": selected,
                "required": selected,
                "scope-mode": document["scope-mode"],
                "changed-paths": document["changed-paths"],
                "selected-project-nodes": document["selected-project-nodes"],
                "selected-release-units": document["selected-release-units"],
                "selected-variants": document["selected-variants"],
                "selected-outputs": document["selected-outputs"],
            }
        )
        evidence_id = (
            f"evidence:{lane_id}:{request_digest.removeprefix('sha256:')}"
        )
        obligation["request-digest"] = request_digest
        obligation["expected-evidence-id"] = evidence_id
        if selected:
            expected_evidence_ids.append(evidence_id)
    document["expected-evidence-ids"] = expected_evidence_ids
    return document


def _plan_document(
    *,
    selected_lanes: tuple[str, ...] = CI_LANE_IDS,
    ready: bool = True,
    manual: bool = False,
    complete_scope: bool | None = None,
) -> dict[str, JsonValue]:
    source = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / "ready-plan.json").read_bytes()),
    )
    return _rebind_plan_document(
        source,
        selected_lanes=selected_lanes,
        ready=ready,
        manual=manual,
        complete_scope=(
            selected_lanes == CI_LANE_IDS
            if complete_scope is None
            else complete_scope
        ),
    )


def _obligation_from_document(
    document: dict[str, JsonValue],
) -> CiObligation:
    return CiObligation(
        obligation_id=cast("str", document["obligation-id"]),
        lane_id=cast("str", document["lane-id"]),
        request_digest=cast("str", document["request-digest"]),
        definition_id=cast("str", document["definition-id"]),
        definition_digest=cast("str", document["definition-digest"]),
        prerequisites=tuple(cast("list[str]", document["prerequisites"])),
        selected=cast("bool", document["selected"]),
        required=cast("bool", document["required"]),
        expected_evidence_id=cast(
            "str",
            document["expected-evidence-id"],
        ),
    )


def _snapshot_from_document(
    document: dict[str, JsonValue],
) -> CiQualificationSnapshot:
    obligations = tuple(
        _obligation_from_document(cast("dict[str, JsonValue]", value))
        for value in cast("list[JsonValue]", document["obligations"])
    )
    return CiQualificationSnapshot(
        candidate=_candidate(
            manual=document["scope-mode"] == "slice-validation",
        ),
        producer=cast("str", document["producer"]),
        workflow_run_id=cast("int", document["workflow-run-id"]),
        run_attempt=cast("int", document["run-attempt"]),
        repository_model_digest=cast(
            "str",
            document["repository-model-digest"],
        ),
        root_hk_definition=cast(
            "str",
            document["root-hk-definition"],
        ),
        root_hk_definition_digest=cast(
            "str",
            document["root-hk-definition-digest"],
        ),
        scope_mode=cast("str", document["scope-mode"]),
        changed_paths=tuple(cast("list[str]", document["changed-paths"])),
        selected_project_nodes=tuple(
            cast("list[str]", document["selected-project-nodes"]),
        ),
        selected_release_units=tuple(
            cast("list[str]", document["selected-release-units"]),
        ),
        selected_variants=tuple(
            cast("list[str]", document["selected-variants"]),
        ),
        selected_outputs=tuple(
            (
                cast("str", output["output-id"]),
                cast("str", output["logical-role"]),
                cast("str", output["media-kind"]),
            )
            for output in cast(
                "list[dict[str, JsonValue]]",
                document["selected-outputs"],
            )
        ),
        obligations=obligations,
        expected_evidence_ids=tuple(
            cast("list[str]", document["expected-evidence-ids"]),
        ),
        ready=cast("bool", document["ready"]),
        diagnostics=tuple(cast("list[str]", document["diagnostics"])),
    )


def _snapshot(
    *,
    selected_lanes: tuple[str, ...] = CI_LANE_IDS,
    ready: bool = True,
    manual: bool = False,
) -> CiQualificationSnapshot:
    return _snapshot_from_document(
        _plan_document(
            selected_lanes=selected_lanes,
            ready=ready,
            manual=manual,
        )
    )


def _obligation(
    plan: CiQualificationSnapshot,
    lane_id: str,
) -> CiObligation:
    return next(item for item in plan.obligations if item.lane_id == lane_id)


def _artifact(plan: CiQualificationSnapshot) -> CiArtifact:
    return CiArtifact(
        candidate=plan.candidate,
        producer="npm-artifact-build",
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        output_id="npm-tarball",
        logical_role="primary-package",
        media_kind="npm-tarball",
        artifact_id=9001,
        artifact_name=(
            f"wdv3-{plan.workflow_run_id}-{plan.run_attempt}-npm-tarball.tgz"
        ),
        artifact_url=(
            f"https://github.com/{plan.candidate.repository}/actions/runs/"
            f"{plan.workflow_run_id}/artifacts/9001"
        ),
        transport_digest=DIGEST_D,
        tarball_basename="hcoona-hcoona-release-smoke-npm-1.2.3.tgz",
        content_sha256=DIGEST_D,
        content_sha512=SHA512_F,
        byte_size=1234,
        provenance_digest=DIGEST_E,
        entries=(
            "package/README.md",
            "package/dist/index.js",
            "package/package.json",
            "package/workflow-delivery/provenance.json",
        ),
        lifecycle_scripts=(("test", "node --test"),),
    )


def _evidence(
    plan: CiQualificationSnapshot,
    lane_id: str = "root-hk",
) -> CiEvidence:
    return form_ci_evidence(
        plan,
        obligation=_obligation(plan, lane_id),
        producer=lane_id,
        workflow_run_id=plan.workflow_run_id,
        run_attempt=plan.run_attempt,
        runner="ubuntu-24.04",
        raw_outcome="success",
        output_digests=(DIGEST_C,),
        artifacts=(
            (_artifact(plan),) if lane_id == "npm-artifact-build" else ()
        ),
        diagnostics=(f"{lane_id} completed mechanically",),
    )


def _lane_results(
    plan: CiQualificationSnapshot,
) -> tuple[CiLaneResult, ...]:
    return tuple(
        (
            form_evidence_lane_result(
                plan,
                _evidence(plan, obligation.lane_id),
            )
            if obligation.selected
            else form_empty_lane_result(plan, lane_id=obligation.lane_id)
        )
        for obligation in plan.obligations
    )


def _decision() -> CiSliceDecision:
    plan = _snapshot()
    return finalize_ci_slice(
        plan,
        _lane_results(plan),
        elapsed_seconds=ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )


def _golden_records() -> dict[str, object]:
    repository_only = _snapshot(selected_lanes=("root-hk",))
    return {
        "pr-candidate": _candidate(),
        "manual-candidate": _candidate(manual=True),
        "ready-plan": _snapshot(),
        "npm-artifact": _artifact(_snapshot()),
        "empty-lane-result": form_empty_lane_result(
            repository_only,
            lane_id="project-build",
        ),
        "satisfied-evidence": _evidence(_snapshot()),
        "non-authoritative-decision": _decision(),
    }


@pytest.mark.parametrize(
    ("fixture_name", "digest"),
    tuple(GOLDEN_DIGESTS.items()),
)
def test_ci_contract_golden_fixtures_and_digests(
    fixture_name: str,
    digest: str,
) -> None:
    """Keep canonical fixture bytes and public record digests stable."""
    record = _golden_records()[fixture_name]
    fixture = (FIXTURE_ROOT / f"{fixture_name}.json").read_bytes()
    document = cast(
        "dict[str, JsonValue]",
        cast("_Record", record).to_document(),
    )
    assert fixture == canonicalize(document)
    digesters: dict[type[object], Callable[[object], str]] = {
        CiCandidate: lambda value: ci_candidate_digest(
            cast("CiCandidate", value)
        ),
        CiQualificationSnapshot: lambda value: (
            ci_qualification_snapshot_digest(
                cast("CiQualificationSnapshot", value)
            )
        ),
        CiArtifact: lambda value: ci_artifact_digest(cast("CiArtifact", value)),
        CiEvidence: lambda value: ci_evidence_digest(cast("CiEvidence", value)),
        CiLaneResult: lambda value: ci_lane_result_digest(
            cast("CiLaneResult", value)
        ),
        CiSliceDecision: lambda value: ci_slice_decision_digest(
            cast("CiSliceDecision", value)
        ),
    }
    assert digesters[type(record)](record) == digest


def test_ci_records_are_frozen_slotted_and_tuple_backed() -> None:
    """Preserve frozen slotted records and immutable tuple collections."""
    decision = _decision()
    records = (
        _candidate(),
        _snapshot(),
        _artifact(_snapshot()),
        _evidence(_snapshot()),
        _lane_results(_snapshot())[0],
        decision.obligation_dispositions[0],
        decision.summary,
        decision,
    )
    for record in records:
        assert not hasattr(record, "__dict__")
    candidate = _candidate()
    with pytest.raises(FrozenInstanceError):
        candidate.repository = "forged"  # type: ignore[misc]
    assert type(_snapshot().obligations) is tuple
    assert type(_evidence(_snapshot()).output_digests) is tuple
    assert type(_artifact(_snapshot()).entries) is tuple


def test_candidate_admission_requires_canonical_closed_json_and_binding() -> (
    None
):
    """Reject noncanonical, open, duplicate, or wrongly bound Candidates."""
    candidate = _candidate()
    document = candidate.to_document()
    encoded = canonicalize(document)
    assert (
        admit_ci_candidate_json(
            encoded,
            expected_candidate=candidate,
        )
        == candidate
    )
    with pytest.raises(ValueError, match="canonical"):
        admit_ci_candidate_json(
            b" " + encoded,
            expected_candidate=candidate,
        )
    opened = dict(document)
    opened["extra"] = "forged"
    with pytest.raises(ValueError, match="unknown field"):
        admit_ci_candidate_json(
            canonicalize(opened),
            expected_candidate=candidate,
        )
    duplicate = encoded[:-1] + b',"purpose":"ci-pr-slice-shadow"}'
    with pytest.raises(ValueError, match="duplicate"):
        admit_ci_candidate_json(
            duplicate,
            expected_candidate=candidate,
        )
    with pytest.raises(ValueError, match="trusted current candidate"):
        admit_ci_candidate_json(
            encoded,
            expected_candidate=_candidate(manual=True),
        )


def test_ci_artifact_admission_is_canonical_and_current_candidate_bound() -> (
    None
):
    """Bind retained npm artifacts to exact platform and candidate facts."""
    plan = _snapshot()
    artifact = _artifact(plan)
    encoded = canonicalize(artifact.to_document())
    assert artifact.artifact_name.endswith(".tgz")
    assert artifact.to_document()["artifact-url"] == artifact.artifact_url
    assert (
        artifact.output_id,
        artifact.logical_role,
        artifact.media_kind,
    ) == ("npm-tarball", "primary-package", "npm-tarball")
    assert (
        admit_ci_artifact_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_artifact_id=artifact.artifact_id,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url,
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role=artifact.logical_role,
            expected_media_kind=artifact.media_kind,
        )
        == artifact
    )
    with pytest.raises(ValueError, match="trusted current candidate"):
        admit_ci_artifact_json(
            encoded,
            expected_candidate=_candidate(manual=True),
            expected_artifact_id=artifact.artifact_id,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url,
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role=artifact.logical_role,
            expected_media_kind=artifact.media_kind,
        )
    with pytest.raises(ValueError, match="trusted platform metadata"):
        admit_ci_artifact_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_artifact_id=artifact.artifact_id + 1,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url,
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role=artifact.logical_role,
            expected_media_kind=artifact.media_kind,
        )
    with pytest.raises(ValueError, match="trusted platform metadata"):
        admit_ci_artifact_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_artifact_id=artifact.artifact_id,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url,
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role="secondary-package",
            expected_media_kind=artifact.media_kind,
        )
    with pytest.raises(ValueError, match="trusted platform metadata"):
        admit_ci_artifact_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_artifact_id=artifact.artifact_id,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url + "?forged=1",
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role=artifact.logical_role,
            expected_media_kind=artifact.media_kind,
        )
    forged_urls = (
        artifact.artifact_url.replace(
            "/hcoona/three/",
            "/hcoona/other/",
        ),
        artifact.artifact_url.replace("/runs/7001/", "/runs/7002/"),
        artifact.artifact_url.replace("/artifacts/9001", "/artifacts/9002"),
        artifact.artifact_url + "?forged=1",
    )
    for artifact_url in forged_urls:
        with pytest.raises(ValueError, match="artifact URL"):
            replace(artifact, artifact_url=artifact_url)
    with pytest.raises(ValueError, match="artifact name"):
        replace(
            artifact,
            artifact_name=artifact.artifact_name.removesuffix(".tgz"),
        )
    opened = artifact.to_document()
    opened["platform-metadata"] = "forged"
    with pytest.raises(ValueError, match="unknown field"):
        admit_ci_artifact_json(
            canonicalize(opened),
            expected_candidate=plan.candidate,
            expected_artifact_id=artifact.artifact_id,
            expected_artifact_name=artifact.artifact_name,
            expected_artifact_url=artifact.artifact_url,
            expected_transport_digest=artifact.transport_digest,
            expected_output_id=artifact.output_id,
            expected_logical_role=artifact.logical_role,
            expected_media_kind=artifact.media_kind,
        )


def test_transported_records_reject_single_current_candidate_mutations() -> (
    None
):
    """Reject one-field current-candidate drift in every transported record."""
    candidate = _candidate()
    plan = _snapshot()
    artifact = _artifact(plan)
    evidence = _evidence(plan)
    lane_result = form_evidence_lane_result(plan, evidence)
    decision = finalize_ci_slice(
        plan,
        _lane_results(plan),
        elapsed_seconds=ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )
    expected_evidence = tuple(
        _evidence(plan, obligation.lane_id) for obligation in plan.obligations
    )
    admissions: tuple[
        tuple[str, dict[str, JsonValue], Callable[[bytes], object]],
        ...,
    ] = (
        (
            "Candidate",
            candidate.to_document(),
            lambda payload: admit_ci_candidate_json(
                payload,
                expected_candidate=candidate,
            ),
        ),
        (
            "Artifact",
            artifact.to_document(),
            lambda payload: admit_ci_artifact_json(
                payload,
                expected_candidate=candidate,
                expected_artifact_id=artifact.artifact_id,
                expected_artifact_name=artifact.artifact_name,
                expected_artifact_url=artifact.artifact_url,
                expected_transport_digest=artifact.transport_digest,
                expected_output_id=artifact.output_id,
                expected_logical_role=artifact.logical_role,
                expected_media_kind=artifact.media_kind,
            ),
        ),
        (
            "Plan",
            plan.to_document(),
            lambda payload: admit_ci_qualification_snapshot_json(
                payload,
                expected_candidate=candidate,
                expected_repository_model_digest=plan.repository_model_digest,
                expected_root_hk_definition=plan.root_hk_definition,
                expected_root_hk_definition_digest=(
                    plan.root_hk_definition_digest
                ),
                expected_plan_digest=ci_qualification_snapshot_digest(plan),
            ),
        ),
        (
            "Evidence",
            evidence.to_document(),
            lambda payload: admit_ci_evidence_json(
                payload,
                expected_candidate=candidate,
                expected_plan_digest=ci_qualification_snapshot_digest(plan),
                expected_obligation=evidence.obligation,
            ),
        ),
        (
            "Lane",
            lane_result.to_document(),
            lambda payload: admit_ci_lane_result_json(
                payload,
                expected_candidate=candidate,
                expected_plan_digest=ci_qualification_snapshot_digest(plan),
                expected_lane_id=lane_result.lane_id,
            ),
        ),
        (
            "Decision",
            decision.to_document(),
            lambda payload: admit_ci_slice_decision_json(
                payload,
                expected_plan=plan,
                expected_evidence=expected_evidence,
                expected_elapsed_seconds=ELAPSED_SECONDS,
                expected_supersession_state="not-superseded",
            ),
        ),
    )
    mutations: tuple[tuple[str, JsonValue], ...] = (
        ("purpose", "slice-validation"),
        ("target", SHA_B),
        ("producer", "forged"),
        ("workflow-run-id", 7002),
        ("run-attempt", 3),
    )

    for record_name, document, admit in admissions:
        for field, value in mutations:
            transported = copy.deepcopy(document)
            candidate_document = (
                transported
                if record_name == "Candidate"
                else cast(
                    "dict[str, JsonValue]",
                    transported["candidate"],
                )
            )
            candidate_document[field] = value
            with pytest.raises((TypeError, ValueError)):
                admit(canonicalize(transported))


def test_plan_admission_binds_trusted_candidate_model_and_digest() -> None:
    """Bind admitted Plans to trusted candidate, model, and digest facts."""
    plan = _snapshot()
    encoded = canonicalize(plan.to_document())
    admitted = admit_ci_qualification_snapshot_json(
        encoded,
        expected_candidate=plan.candidate,
        expected_repository_model_digest=plan.repository_model_digest,
        expected_root_hk_definition=plan.root_hk_definition,
        expected_root_hk_definition_digest=plan.root_hk_definition_digest,
        expected_plan_digest=ci_qualification_snapshot_digest(plan),
    )
    assert admitted == plan
    with pytest.raises(ValueError, match="Repository Model digest"):
        admit_ci_qualification_snapshot_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_repository_model_digest="sha256:" + ("f" * 64),
            expected_root_hk_definition=plan.root_hk_definition,
            expected_root_hk_definition_digest=plan.root_hk_definition_digest,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
        )
    with pytest.raises(ValueError, match="trusted Plan digest"):
        admit_ci_qualification_snapshot_json(
            encoded,
            expected_candidate=plan.candidate,
            expected_repository_model_digest=plan.repository_model_digest,
            expected_root_hk_definition=plan.root_hk_definition,
            expected_root_hk_definition_digest=plan.root_hk_definition_digest,
            expected_plan_digest="sha256:" + ("f" * 64),
        )


def test_direct_self_consistent_partial_ready_plan_is_rejected() -> None:
    """Reject a directly constructed self-consistent partial ready Plan."""
    document = _plan_document(
        selected_lanes=("root-hk", "project-build"),
        complete_scope=True,
    )
    with pytest.raises(ValueError, match="invalid partial scope"):
        _snapshot_from_document(document)


def test_admitted_self_consistent_partial_ready_plan_is_rejected() -> None:
    """Reject a canonically admitted self-consistent partial ready Plan."""
    document = _plan_document(
        selected_lanes=("root-hk", "project-build"),
        complete_scope=True,
    )
    with pytest.raises(ValueError, match="invalid partial scope"):
        admit_ci_qualification_snapshot_json(
            canonicalize(document),
            expected_candidate=_candidate(),
            expected_repository_model_digest=cast(
                "str",
                document["repository-model-digest"],
            ),
            expected_root_hk_definition=cast(
                "str",
                document["root-hk-definition"],
            ),
            expected_root_hk_definition_digest=cast(
                "str",
                document["root-hk-definition-digest"],
            ),
            expected_plan_digest=canonical_sha256(document),
        )


def test_manual_and_blocked_plan_shapes_are_exact() -> None:
    """Require all lanes for manual Plans and no lanes for blocked Plans."""
    manual = _snapshot(manual=True)
    assert (
        tuple(
            obligation.lane_id
            for obligation in manual.obligations
            if obligation.selected
        )
        == CI_LANE_IDS
    )
    blocked = _snapshot(selected_lanes=(), ready=False)
    assert blocked.expected_evidence_ids == ()
    assert not any(obligation.selected for obligation in blocked.obligations)
    with pytest.raises(ValueError, match="actionable diagnostics"):
        replace(blocked, diagnostics=())
    with pytest.raises(ValueError, match="repository-only changed paths"):
        replace(
            _snapshot(selected_lanes=("root-hk",)),
            changed_paths=(
                "src/public/lib/hcoona-release-smoke-npm/src/index.ts",
            ),
        )
    with pytest.raises(ValueError, match="complete first-slice scope"):
        _snapshot_from_document(
            _plan_document(
                selected_lanes=("root-hk",),
                manual=True,
                complete_scope=False,
            )
        )


def test_evidence_and_lane_admission_bind_exact_plan_position() -> None:
    """Bind admitted Evidence and lane results to one exact Plan position."""
    plan = _snapshot()
    evidence = _evidence(plan)
    assert (
        admit_ci_evidence_json(
            canonicalize(evidence.to_document()),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
            expected_obligation=evidence.obligation,
        )
        == evidence
    )
    with pytest.raises(ValueError, match="trusted obligation"):
        admit_ci_evidence_json(
            canonicalize(evidence.to_document()),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
            expected_obligation=_obligation(plan, "project-test"),
        )

    lane = form_evidence_lane_result(plan, evidence)
    assert (
        admit_ci_lane_result_json(
            canonicalize(lane.to_document()),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
            expected_lane_id="root-hk",
        )
        == lane
    )
    with pytest.raises(ValueError, match="trusted static lane"):
        admit_ci_lane_result_json(
            canonicalize(lane.to_document()),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
            expected_lane_id="project-test",
        )


def test_decision_admission_binds_plan_evidence_and_elapsed_time() -> None:
    """Bind admitted Decisions to Plan, Evidence, and trusted elapsed time."""
    plan = _snapshot()
    evidence = tuple(
        _evidence(plan, obligation.lane_id) for obligation in plan.obligations
    )
    decision = _decision()
    encoded = canonicalize(decision.to_document())
    assert (
        admit_ci_slice_decision_json(
            encoded,
            expected_plan=plan,
            expected_evidence=evidence,
            expected_elapsed_seconds=ELAPSED_SECONDS,
            expected_supersession_state="not-superseded",
        )
        == decision
    )
    with pytest.raises(ValueError, match="trusted elapsed time"):
        admit_ci_slice_decision_json(
            encoded,
            expected_plan=plan,
            expected_evidence=evidence,
            expected_elapsed_seconds=ELAPSED_SECONDS + 1,
            expected_supersession_state="not-superseded",
        )
    with pytest.raises(ValueError, match="trusted admitted Evidence"):
        admit_ci_slice_decision_json(
            encoded,
            expected_plan=plan,
            expected_evidence=evidence[:-1],
            expected_elapsed_seconds=ELAPSED_SECONDS,
            expected_supersession_state="not-superseded",
        )


def test_decision_summary_and_slo_are_exact_derivations() -> None:
    """Reject contradictions in derived explanation, summary, or PR SLO."""
    decision = _decision()
    assert decision.pr_slo == "met"
    assert decision.pr_slo_reason == "ordinary-pull-request"
    assert decision.failure_class == "none"
    assert decision.next_action == "none"
    assert decision.admitted_artifact_digests == (
        ci_artifact_digest(_artifact(_snapshot())),
    )
    assert "elapsed=600s" in decision.summary.text
    with pytest.raises(ValueError, match="explanation is not deterministic"):
        replace(decision, explanation="all work passed")
    with pytest.raises(ValueError, match="Summary text"):
        replace(
            decision,
            summary=CiSliceSummary(
                authority="non-authoritative",
                terminal_result="success",
                text="non-authoritative contradictory summary",
            ),
        )
    with pytest.raises(ValueError, match="SLO result"):
        replace(decision, pr_slo="missed")
    with pytest.raises(ValueError, match="failure class"):
        replace(decision, failure_class="quality-failure")
    with pytest.raises(ValueError, match="SLO result"):
        replace(decision, pr_slo_reason="broad-change")


@pytest.mark.parametrize(
    "outcome",
    ["canceled", "conflicted", "advisory"],
)
def test_impossible_disposition_states_are_rejected(outcome: str) -> None:
    """Reject canceled, conflict, and advisory disposition APIs."""
    plan = _snapshot()
    obligation = _obligation(plan, "root-hk")
    with pytest.raises(ValueError, match="invalid closed value"):
        CiObligationDisposition(
            obligation=obligation,
            outcome=outcome,
            evidence_digests=(),
            explanation=f"root-hk {outcome}",
        )


def test_incomplete_is_finalizer_only_and_has_no_evidence() -> None:
    """Derive incomplete only from missing selected work without Evidence."""
    plan = _snapshot()
    decision = finalize_ci_slice(
        plan,
        tuple(
            result
            for result in _lane_results(plan)
            if result.lane_id != "project-test"
        ),
        elapsed_seconds=ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )
    incomplete = next(
        disposition
        for disposition in decision.obligation_dispositions
        if disposition.obligation.lane_id == "project-test"
    )
    assert incomplete.outcome == "incomplete"
    assert incomplete.evidence_digests == ()
    with pytest.raises(ValueError, match="has no Evidence"):
        replace(incomplete, evidence_digests=(DIGEST_C,))
    with pytest.raises(ValueError, match="invalid closed value"):
        replace(_lane_results(plan)[0], disposition="incomplete")


@pytest.mark.parametrize(
    ("fixture_name", "admit"),
    [
        (
            "pr-candidate",
            lambda data: admit_ci_candidate_json(
                data,
                expected_candidate=_candidate(),
            ),
        ),
        (
            "ready-plan",
            lambda data: admit_ci_qualification_snapshot_json(
                data,
                expected_candidate=_snapshot().candidate,
                expected_repository_model_digest=(
                    _snapshot().repository_model_digest
                ),
                expected_root_hk_definition=_snapshot().root_hk_definition,
                expected_root_hk_definition_digest=(
                    _snapshot().root_hk_definition_digest
                ),
                expected_plan_digest=ci_qualification_snapshot_digest(
                    _snapshot()
                ),
            ),
        ),
        (
            "npm-artifact",
            lambda data: admit_ci_artifact_json(
                data,
                expected_candidate=_snapshot().candidate,
                expected_artifact_id=_artifact(_snapshot()).artifact_id,
                expected_artifact_name=_artifact(_snapshot()).artifact_name,
                expected_artifact_url=_artifact(_snapshot()).artifact_url,
                expected_transport_digest=(
                    _artifact(_snapshot()).transport_digest
                ),
                expected_output_id=_artifact(_snapshot()).output_id,
                expected_logical_role=_artifact(_snapshot()).logical_role,
                expected_media_kind=_artifact(_snapshot()).media_kind,
            ),
        ),
        (
            "satisfied-evidence",
            lambda data: admit_ci_evidence_json(
                data,
                expected_candidate=_snapshot().candidate,
                expected_plan_digest=ci_qualification_snapshot_digest(
                    _snapshot()
                ),
                expected_obligation=_obligation(_snapshot(), "root-hk"),
            ),
        ),
        (
            "non-authoritative-decision",
            lambda data: admit_ci_slice_decision_json(
                data,
                expected_plan=_snapshot(),
                expected_evidence=tuple(
                    _evidence(_snapshot(), obligation.lane_id)
                    for obligation in _snapshot().obligations
                ),
                expected_elapsed_seconds=ELAPSED_SECONDS,
                expected_supersession_state="not-superseded",
            ),
        ),
    ],
)
def test_fixture_admission_rejects_unknown_top_level_field(
    fixture_name: str,
    admit: Callable[[bytes], object],
) -> None:
    """Keep each admitted top-level record schema closed."""
    document = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / f"{fixture_name}.json").read_bytes()),
    )
    document["unknown"] = "forged"
    with pytest.raises(ValueError, match="unknown field"):
        admit(canonicalize(document))
