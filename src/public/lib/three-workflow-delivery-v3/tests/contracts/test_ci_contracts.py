"""Canonical record and admission contracts for the CI model core."""

from __future__ import annotations

import copy
import json
from dataclasses import replace
from operator import setitem
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, cast

import pytest
from three_workflow_delivery_v3 import cli as cli_module
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
    admit_ci_bootstrap_projection_decision_json,
    admit_ci_lane_result_json,
    admit_ci_qualification_snapshot_json,
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
            (
                "incremental comparison selected repository "
                "source-tree conformance only"
            )
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
    candidate: CiCandidate | None = None,
) -> dict[str, JsonValue]:
    source = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / "ready-plan.json").read_bytes()),
    )
    candidate = _candidate(manual=manual) if candidate is None else candidate
    source["candidate"] = candidate.to_document()
    source["workflow-run-id"] = candidate.workflow_run_id
    source["run-attempt"] = candidate.run_attempt
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
    *,
    candidate: CiCandidate | None = None,
) -> CiQualificationSnapshot:
    obligations = tuple(
        _obligation_from_document(cast("dict[str, JsonValue]", value))
        for value in cast("list[JsonValue]", document["obligations"])
    )
    return CiQualificationSnapshot(
        candidate=(
            _candidate(manual=document["scope-mode"] == "slice-validation")
            if candidate is None
            else candidate
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
    candidate: CiCandidate | None = None,
) -> CiQualificationSnapshot:
    return _snapshot_from_document(
        _plan_document(
            selected_lanes=selected_lanes,
            ready=ready,
            manual=manual,
            candidate=candidate,
        ),
        candidate=candidate,
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


def _decision(plan: CiQualificationSnapshot | None = None) -> CiSliceDecision:
    plan = _snapshot() if plan is None else plan
    return finalize_ci_slice(
        plan,
        _lane_results(plan),
        elapsed_seconds=ELAPSED_SECONDS,
        supersession_state="not-superseded",
    )


def _golden_record(fixture_name: str) -> object:
    factories: dict[str, Callable[[], object]] = {
        "pr-candidate": _candidate,
        "manual-candidate": lambda: _candidate(manual=True),
        "ready-plan": _snapshot,
        "npm-artifact": lambda: _artifact(_snapshot()),
        "empty-lane-result": lambda: form_empty_lane_result(
            _snapshot(selected_lanes=("root-hk",)),
            lane_id="project-build",
        ),
        "satisfied-evidence": lambda: _evidence(_snapshot()),
        "non-authoritative-decision": _decision,
    }
    return factories[fixture_name]()


@pytest.mark.parametrize(
    ("fixture_name", "digest"),
    tuple(GOLDEN_DIGESTS.items()),
)
def test_ci_contract_golden_fixtures_and_digests(
    fixture_name: str,
    digest: str,
) -> None:
    """Keep canonical fixture bytes and public record digests stable."""
    record = _golden_record(fixture_name)
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
        CiQualificationSnapshot: lambda value: ci_qualification_snapshot_digest(
            cast("CiQualificationSnapshot", value)
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


def test_ci_record_fields_cannot_change_after_construction() -> None:
    """Preserve admitted identity through the public record interface."""
    plan = _snapshot()
    decision = _decision()
    mutations = (
        (_candidate(), "repository", "forged"),
        (plan, "candidate", _candidate(manual=True)),
        (plan.obligations[0], "selected", False),
        (_artifact(plan), "artifact_id", 9002),
        (_evidence(plan), "normalized_outcome", "failed"),
        (_lane_results(plan)[0], "disposition", "failed"),
        (decision.obligation_dispositions[0], "outcome", "failed"),
        (decision.summary, "text", "forged"),
        (decision, "terminal_result", "failure"),
    )
    for record, field, value in mutations:
        before = canonicalize(record.to_document())
        with pytest.raises(AttributeError):
            setattr(record, field, value)
        assert canonicalize(record.to_document()) == before


def test_ci_record_collections_cannot_replace_bound_members() -> None:
    """Prevent in-place substitution of obligations, outputs, and entries."""
    plan = _snapshot()
    artifact = _artifact(plan)
    evidence = _evidence(plan)
    decision = _decision()
    mutations = (
        (plan, plan.obligations, plan.obligations[1]),
        (artifact, artifact.entries, "package/other.txt"),
        (evidence, evidence.output_digests, DIGEST_D),
        (
            decision,
            decision.obligation_dispositions,
            decision.obligation_dispositions[1],
        ),
    )
    for record, collection, replacement in mutations:
        before = canonicalize(record.to_document())
        with pytest.raises(TypeError):
            setitem(collection, 0, replacement)
        assert canonicalize(record.to_document()) == before


def test_ci_record_documents_do_not_alias_immutable_state() -> None:
    """Editing exported nested objects and arrays cannot change their record."""
    plan = _snapshot()
    artifact = _artifact(plan)
    evidence = _evidence(plan)
    decision = _decision()

    plan_document = plan.to_document()
    cast("dict[str, JsonValue]", plan_document["candidate"])["target"] = SHA_B
    cast("list[JsonValue]", plan_document["obligations"]).clear()
    artifact_document = artifact.to_document()
    entries = cast("list[JsonValue]", artifact_document["entries"])
    entries[0] = "package/forged.txt"
    evidence_document = evidence.to_document()
    cast("list[JsonValue]", evidence_document["output-digests"]).clear()
    cast("dict[str, JsonValue]", evidence_document["obligation"])[
        "selected"
    ] = False
    decision_document = decision.to_document()
    cast("dict[str, JsonValue]", decision_document["summary"])["text"] = (
        "forged"
    )
    dispositions = cast(
        "list[dict[str, JsonValue]]",
        decision_document["obligation-dispositions"],
    )
    cast("list[JsonValue]", dispositions[0]["evidence-digests"]).clear()

    assert (
        ci_qualification_snapshot_digest(plan) == GOLDEN_DIGESTS["ready-plan"]
    )
    assert ci_artifact_digest(artifact) == GOLDEN_DIGESTS["npm-artifact"]
    assert ci_evidence_digest(evidence) == GOLDEN_DIGESTS["satisfied-evidence"]
    assert (
        ci_slice_decision_digest(decision)
        == GOLDEN_DIGESTS["non-authoritative-decision"]
    )


def test_ci_artifact_value_binds_platform_url_and_name() -> None:
    """Bind the Artifact URL and name to its own checked platform identity."""
    artifact = _artifact(_snapshot())
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


@pytest.mark.parametrize(
    "alternate_context",
    ["workflow-run", "attempt", "target", "manual", "plan-diagnostic"],
)
def test_lane_loader_rejects_coherent_foreign_context(
    tmp_path: Path,
    alternate_context: str,
) -> None:
    """Reject valid foreign Lane bytes against the receiving Plan."""
    plan = _snapshot()
    candidate = plan.candidate
    if alternate_context == "workflow-run":
        candidate = replace(candidate, workflow_run_id=7002)
    elif alternate_context == "attempt":
        candidate = replace(candidate, run_attempt=3)
    elif alternate_context == "target":
        candidate = replace(
            candidate,
            target=SHA_B,
            workflow_sha=SHA_B,
            tested_merge_sha=SHA_B,
        )
    elif alternate_context == "manual":
        candidate = _candidate(manual=True)
    alternate = _snapshot(
        candidate=candidate,
        manual=alternate_context == "manual",
    )
    if alternate_context == "plan-diagnostic":
        alternate = replace(alternate, diagnostics=("alternate scope reason",))
    lane = form_evidence_lane_result(alternate, _evidence(alternate))
    path = tmp_path / "foreign-lane.json"
    encoded = canonicalize(lane.to_document())
    path.write_bytes(encoded)
    assert (
        cli_module._load_lane_result(  # noqa: SLF001
            str(path), plan=alternate
        )
        == lane
    )
    error = (
        "trusted Plan digest"
        if alternate_context == "plan-diagnostic"
        else "trusted current candidate"
    )
    with pytest.raises(ValueError, match=error):
        cli_module._load_lane_result(str(path), plan=plan)  # noqa: SLF001
    assert path.read_bytes() == encoded


def test_plan_loader_binds_supplied_digest_and_current_root_hk(
    tmp_path: Path,
) -> None:
    """Bind original Plan bytes to the supplied digest and current rules."""
    plan = _snapshot()
    path = tmp_path / "plan.json"
    path.write_bytes(canonicalize(plan.to_document()))
    assert (
        cli_module._load_ci_plan(  # noqa: SLF001
            str(path), ci_qualification_snapshot_digest(plan)
        )
        == plan
    )
    with pytest.raises(ValueError, match="trusted Plan digest"):
        cli_module._load_ci_plan(str(path), DIGEST_E)  # noqa: SLF001
    document = plan.to_document()
    document["root-hk-definition-digest"] = DIGEST_E
    path.write_bytes(canonicalize(document))
    with pytest.raises(ValueError, match="root-HK definition is not current"):
        cli_module._load_ci_plan(  # noqa: SLF001
            str(path), canonical_sha256(document)
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("definition-id", "node/project-test-v1"),
        ("definition-digest", DIGEST_E),
        ("prerequisites", ["ci:root-hk"]),
        ("request-digest", DIGEST_E),
    ],
)
def test_plan_admission_rejects_canonical_fixed_obligation_forgery(
    field: str,
    value: JsonValue,
) -> None:
    """Reject forged fixed work even with matching request and outer hashes."""
    source = cast(
        "dict[str, JsonValue]",
        json.loads((FIXTURE_ROOT / "ready-plan.json").read_bytes()),
    )
    obligations = cast("list[dict[str, JsonValue]]", source["obligations"])
    obligations[1][field] = value
    document = _rebind_plan_document(
        source,
        selected_lanes=CI_LANE_IDS,
        ready=True,
        complete_scope=True,
    )
    if field == "request-digest":
        obligations = cast(
            "list[dict[str, JsonValue]]", document["obligations"]
        )
        obligations[1][field] = value
        evidence_id = "evidence:project-build:" + ("e" * 64)
        obligations[1]["expected-evidence-id"] = evidence_id
        cast("list[JsonValue]", document["expected-evidence-ids"])[1] = (
            evidence_id
        )

    plan = _snapshot()
    with pytest.raises(ValueError, match="does not match fixed definition"):
        admit_ci_qualification_snapshot_json(
            canonicalize(document),
            expected_root_hk_definition=plan.root_hk_definition,
            expected_root_hk_definition_digest=plan.root_hk_definition_digest,
            expected_plan_digest=canonical_sha256(document),
        )


def test_lane_admission_rejects_nested_outcome_contradiction() -> None:
    """Reject a nested success claim whose raw execution failed."""
    plan = _snapshot()
    lane = form_evidence_lane_result(plan, _evidence(plan))
    document = lane.to_document()
    assert (
        admit_ci_lane_result_json(
            canonicalize(document),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
        )
        == lane
    )
    evidence = cast("dict[str, JsonValue]", document["evidence"])
    evidence["raw-outcome"] = "failure"
    with pytest.raises(ValueError, match="does not match raw mechanics"):
        admit_ci_lane_result_json(
            canonicalize(document),
            expected_candidate=plan.candidate,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
        )


@pytest.mark.parametrize(
    ("field", "value", "error"),
    [
        ("terminal-result", "success", "terminal result contradicts"),
        ("explanation", "all work passed", "explanation is not deterministic"),
        ("failure-class", "quality-failure", "failure class or next action"),
        ("next-action", "rerun-candidate", "failure class or next action"),
        (
            "supersession-reason",
            "platform-proof-unavailable",
            "supersession reason is not deterministic",
        ),
        (
            "disposition-explanation",
            "root-hk failed",
            "disposition explanation is not deterministic",
        ),
    ],
)
def test_decision_admission_rejects_canonical_rule_contradictions(
    field: str,
    value: str,
    error: str,
) -> None:
    """Keep intrinsic Decision derivation checks at bootstrap admission."""
    plan = _snapshot(selected_lanes=(), ready=False)
    decision = _decision(plan)
    document = decision.to_document()
    assert (
        admit_ci_bootstrap_projection_decision_json(
            canonicalize(document), expected_plan=plan
        )
        == decision
    )
    if field == "disposition-explanation":
        dispositions = cast(
            "list[dict[str, JsonValue]]", document["obligation-dispositions"]
        )
        dispositions[0]["explanation"] = value
    else:
        document[field] = value
    with pytest.raises(ValueError, match=error):
        admit_ci_bootstrap_projection_decision_json(
            canonicalize(document),
            expected_plan=plan,
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


@pytest.mark.parametrize("field", ["ready", "selected", "required"])
def test_plan_admission_rejects_integer_boolean_fields(field: str) -> None:
    """Keep readiness and obligation selection as Booleans, not integers."""
    plan = _snapshot()
    document = plan.to_document()
    if field == "ready":
        document[field] = 1
    else:
        obligations = cast(
            "list[dict[str, JsonValue]]", document["obligations"]
        )
        obligations[0][field] = 1
    with pytest.raises(TypeError, match="must be a Boolean"):
        admit_ci_qualification_snapshot_json(
            canonicalize(document),
            expected_root_hk_definition=plan.root_hk_definition,
            expected_root_hk_definition_digest=plan.root_hk_definition_digest,
            expected_plan_digest=ci_qualification_snapshot_digest(plan),
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


@pytest.mark.parametrize("record_kind", ["plan", "lane", "bootstrap-decision"])
@pytest.mark.parametrize(
    "fault",
    [
        "noncanonical",
        "unknown-field",
        "missing-field",
        "boolean-run-id",
    ],
)
def test_ci_record_admission_rejects_invalid_envelopes(
    record_kind: str,
    fault: str,
) -> None:
    """Require canonical, closed, typed input at each used CI entry."""
    record: _Record
    if record_kind == "plan":
        plan = _snapshot()
        record = plan
    elif record_kind == "lane":
        plan = _snapshot(selected_lanes=("root-hk",))
        record = form_empty_lane_result(plan, lane_id="project-build")
    else:
        assert record_kind == "bootstrap-decision"
        plan = _snapshot(selected_lanes=(), ready=False)
        record = _decision(plan)

    def admit(data: bytes) -> _Record:
        if record_kind == "plan":
            return admit_ci_qualification_snapshot_json(
                data,
                expected_root_hk_definition=plan.root_hk_definition,
                expected_root_hk_definition_digest=(
                    plan.root_hk_definition_digest
                ),
                expected_plan_digest=ci_qualification_snapshot_digest(plan),
            )
        if record_kind == "lane":
            return admit_ci_lane_result_json(
                data,
                expected_candidate=plan.candidate,
                expected_plan_digest=ci_qualification_snapshot_digest(plan),
            )
        return admit_ci_bootstrap_projection_decision_json(
            data, expected_plan=plan
        )

    encoded = canonicalize(record.to_document())
    admitted = cast("_Record", admit(encoded))
    assert canonicalize(admitted.to_document()) == encoded
    document = cast(
        "dict[str, JsonValue]",
        json.loads(encoded),
    )
    error: type[Exception] = ValueError
    message: str
    if fault == "noncanonical":
        encoded = b" " + encoded
        message = "canonical"
    elif fault == "unknown-field":
        document["unknown"] = "forged"
        encoded = canonicalize(document)
        message = "unknown field"
    elif fault == "missing-field":
        del document["producer"]
        encoded = canonicalize(document)
        message = "missing required field: producer"
    else:
        assert fault == "boolean-run-id"
        document["workflow-run-id"] = True
        encoded = canonicalize(document)
        error = TypeError
        message = "workflow-run-id must be an integer"
    with pytest.raises(error, match=message):
        admit(encoded)


@pytest.mark.parametrize(
    "nested_record",
    ["plan-candidate", "lane-evidence", "lane-artifact"],
)
@pytest.mark.parametrize(
    "fault",
    ["unknown-field", "missing-field", "boolean-run-id"],
)
def test_ci_admission_rejects_nested_field_map_faults(
    tmp_path: Path,
    nested_record: str,
    fault: str,
) -> None:
    """Reach each nested closed field map through a used receiving boundary."""
    plan = _snapshot()
    record: _Record
    if nested_record == "plan-candidate":
        record = plan
    else:
        lane_id = (
            "npm-artifact-build"
            if nested_record == "lane-artifact"
            else "root-hk"
        )
        record = form_evidence_lane_result(plan, _evidence(plan, lane_id))
    document = record.to_document()
    path = tmp_path / "nested-record.json"
    path.write_bytes(canonicalize(document))

    def load() -> _Record:
        if nested_record == "plan-candidate":
            return cli_module._load_ci_plan(  # noqa: SLF001
                str(path), canonical_sha256(document)
            )
        return cli_module._load_lane_result(str(path), plan=plan)  # noqa: SLF001

    assert load() == record
    if nested_record == "plan-candidate":
        nested = cast("dict[str, JsonValue]", document["candidate"])
    else:
        nested = cast("dict[str, JsonValue]", document["evidence"])
        if nested_record == "lane-artifact":
            nested = cast("list[dict[str, JsonValue]]", nested["artifacts"])[0]
    error: type[Exception] = ValueError
    if fault == "unknown-field":
        nested["unknown"] = "forged"
        message = "unknown field"
    elif fault == "missing-field":
        del nested["producer"]
        message = "missing required field: producer"
    else:
        assert fault == "boolean-run-id"
        nested["workflow-run-id"] = True
        error = TypeError
        message = "workflow-run-id must be an integer"
    path.write_bytes(canonicalize(document))
    with pytest.raises(error, match=message):
        load()
