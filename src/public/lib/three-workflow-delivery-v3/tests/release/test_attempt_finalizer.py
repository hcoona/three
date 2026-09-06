"""Current-DAG terminal scenarios through canonical transported lineage."""

# ruff: noqa: D103

import hashlib
from dataclasses import replace
from datetime import datetime, timedelta

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    AttemptOutcome,
    DirectPredecessor,
    MutationMayHaveStartedMarker,
    PublicationResult,
    admit_release_record,
    release_record_digest,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release.attempt_finalizer import (
    FinalizationInputs,
    finalize_attempt_outcome,
    parse_publication_terminal_reference,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification

from .observation_fixtures import (
    authority_arguments,
    current_arguments,
    exact_finalization_arguments,
    qualification_arguments,
    uploaded_arguments,
)
from .test_observation_admission import NOW, _observation
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)
from .test_publication import _execute, _prepare
from .test_publication import publisher as publisher  # noqa: PLC0414


def pair(record, name, artifact_id):
    reference = ArtifactReference(
        artifact_id=artifact_id,
        artifact_digest=release_record_digest(record),
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/"
            f"{record.attempt.workflow_run_id}/artifacts/{artifact_id}"
        ),
        payload_path=name,
        payload_digest=release_record_digest(record),
    )
    admitted = admit_release_record(
        canonicalize(record.to_document()),
        expected_type=type(record),
        expected_digest=reference.payload_digest,
    )
    return admitted, reference


def base_inputs(case):
    return FinalizationInputs(
        intent=case.intent,
        attempt_binding=case.attempt_binding,
        eligibility=case.eligibility,
        policy=case.policy,
        snapshot=case.snapshot,
        decision=case.decision,
        decision_reference=case.decision_reference,
        evidence=case.evidence,
        artifacts=(case.artifact,),
    )


def action_inputs(case, publisher):
    prepared = publisher[0]
    return replace(
        base_inputs(case),
        observations=(pair(prepared.observation, "observation.json", 108),),
        publication=(
            prepared.publication_snapshot,
            prepared.publication_snapshot_reference,
        ),
        bundle=(prepared.approval_bundle, prepared.approval_bundle_reference),
        reviewer_summary=(
            prepared.reviewer_summary,
            prepared.reviewer_summary_reference,
        ),
        authorization=(
            prepared.authorization,
            prepared.authorization_reference,
        ),
    )


def exact_inputs(case):
    closure = exact_finalization_arguments(case)
    return replace(
        base_inputs(case),
        observations=(
            pair(closure["observations"][0], "observation.json", 108),
        ),
        publication=(
            closure["publication_snapshot"],
            closure["publication_snapshot_reference"],
        ),
        exact_proof=pair(
            closure["exact_satisfied_finalization_proof"],
            "exact-proof.json",
            115,
        ),
    )


def finalize(
    inputs, *, job="skipped", step=None, wire=None, observer="skipped"
):
    if wire is None:
        wire = (
            "null"
            if inputs.terminal is None
            else canonicalize(inputs.terminal[1].to_document()).decode()
        )
    outcome = finalize_attempt_outcome(
        inputs,
        current=ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=inputs.intent.workflow_run_id,
            run_attempt=None,
            target=inputs.intent.target,
        ),
        run_attempt=1,
        publisher_conclusion=job,
        publication_step_outcome=step,
        publication_terminal_reference=wire,
        observation_conclusion="success" if inputs.observations else observer,
    )
    if outcome is not None:
        assert (
            admit_release_record(
                canonicalize(outcome.to_document()),
                expected_type=AttemptOutcome,
                expected_digest=outcome.outcome_digest,
            )
            == outcome
        )
    return outcome


@pytest.mark.parametrize("job", ["success", "failure", "cancelled"])
@pytest.mark.parametrize(
    ("result_state", "mutation", "expected", "possible"),
    [
        ("published", "mutated", "published", False),
        ("failed", "not-mutated", "publication-failed", False),
        ("failed", "possibly-mutated", "publication-failed", True),
        ("failed", "mutated", "publication-failed", True),
    ],
)
def test_durable_result_controls_even_after_late_platform_failure(  # noqa: PLR0913, PLR0917
    observation_case, publisher, job, result_state, mutation, expected, possible
):
    marker = _prepare(publisher)
    result = _execute(publisher, *marker)
    assert result.result == "published"
    if result_state == "failed":
        result = replace(
            result,
            result="failed",
            mutation_classification=mutation,
            command_classification="not-initiated"
            if mutation == "not-mutated"
            else "definitive-non-success",
            post_action_readback=None
            if mutation == "not-mutated"
            else result.post_action_readback,
        )
    inputs = replace(
        action_inputs(observation_case, publisher),
        terminal=pair(result, "result.json", 116),
        result_marker=marker,
    )
    outcome = finalize(
        inputs,
        job=job,
        step="success" if result_state == "published" else "failure",
    )
    assert (outcome.disposition, outcome.possibly_mutated) == (
        expected,
        possible,
    )
    assert outcome.direct_predecessor == DirectPredecessor(
        "publication-result", inputs.terminal[1]
    )


@pytest.mark.parametrize("job", ["success", "failure", "cancelled"])
def test_marker_without_result_is_unknown_even_when_execution_skipped(
    observation_case, publisher, job
):
    inputs = replace(
        action_inputs(observation_case, publisher), terminal=_prepare(publisher)
    )
    outcome = finalize(inputs, job=job, step="skipped")
    assert outcome.disposition == "unknown"
    assert outcome.possibly_mutated is True
    assert outcome.direct_predecessor.kind == "mutation-marker"


@pytest.mark.parametrize(
    ("job", "step", "expected", "possible"),
    [
        ("skipped", None, "failed-before-publication", False),
        ("failure", "skipped", "failed-before-publication", False),
        ("cancelled", "skipped", "failed-before-publication", False),
        ("success", "success", "unknown", True),
        ("failure", "failure", "unknown", True),
        ("failure", None, "unknown", True),
        ("cancelled", "", "unknown", True),
    ],
)
@pytest.mark.parametrize(
    "tier",
    [
        "publication-authorization",
        "approval-bundle",
        "action-bearing-publication-snapshot",
        "blocking-observation",
        "qualification-decision",
    ],
)
def test_null_terminal_uses_latest_admissible_predecessor(  # noqa: PLR0913, PLR0917
    observation_case, publisher, tier, job, step, expected, possible
):
    inputs = action_inputs(observation_case, publisher)
    if tier != "publication-authorization":
        inputs = replace(inputs, authorization=None)
    if tier not in {"publication-authorization", "approval-bundle"}:
        inputs = replace(inputs, bundle=None, reviewer_summary=None)
    if tier in {"blocking-observation", "qualification-decision"}:
        inputs = replace(inputs, publication=None, observations=())
    if tier == "blocking-observation":
        inputs = replace(
            inputs,
            observations=(
                pair(
                    _observation(observation_case, classification="unknown"),
                    "observation.json",
                    108,
                ),
            ),
        )
    outcome = finalize(inputs, job=job, step=step)
    assert (outcome.disposition, outcome.possibly_mutated) == (
        expected,
        possible,
    )
    assert outcome.direct_predecessor.kind == tier


def test_null_terminal_rejects_successful_publisher_with_skipped_execution(
    observation_case, publisher
):
    inputs = action_inputs(observation_case, publisher)
    with pytest.raises(ValueError, match=r"Successful Publisher.*skipped"):
        finalize(inputs, job="success", step="skipped")


@pytest.mark.parametrize("classification", ["absent", "exact-satisfied"])
@pytest.mark.parametrize("job", ["skipped", "failure", "cancelled", "success"])
def test_ready_observation_without_snapshot_never_falls_back_to_qualification(
    observation_case, classification, job
):
    inputs = replace(
        base_inputs(observation_case),
        observations=(
            pair(
                _observation(observation_case, classification=classification),
                "observation.json",
                108,
            ),
        ),
    )
    assert finalize(inputs, job=job, step="skipped") is None


@pytest.mark.parametrize("proof_present", [True, False])
def test_zero_action_uses_exact_proof_or_zero_snapshot(
    observation_case, proof_present
):
    inputs = exact_inputs(observation_case)
    if not proof_present:
        inputs = replace(inputs, exact_proof=None)
    outcome = finalize(inputs)
    assert outcome.disposition == (
        "exact-satisfied" if proof_present else "unknown"
    )
    assert outcome.possibly_mutated is False
    assert outcome.direct_predecessor.kind == (
        "exact-satisfied-finalization-proof"
        if proof_present
        else "zero-action-publication-snapshot"
    )


@pytest.mark.parametrize("job", ["success", "failure", "cancelled"])
def test_zero_action_with_running_publisher_is_contradictory(
    observation_case, job
):
    with pytest.raises(ValueError, match="Zero-action"):
        finalize(exact_inputs(observation_case), job=job, step="skipped")


def test_skipped_publisher_cannot_have_terminal(observation_case, publisher):
    inputs = replace(
        action_inputs(observation_case, publisher), terminal=_prepare(publisher)
    )
    with pytest.raises(ValueError, match="Skipped Publisher"):
        finalize(inputs)


def test_unsuccessful_qualification_remains_terminal_without_outcome(
    observation_case,
):
    case = observation_case
    decision = finalize_qualification(case.snapshot, (), ())
    inputs = replace(
        base_inputs(case),
        decision=decision,
        evidence=(),
        artifacts=(),
        decision_reference=replace(
            case.decision_reference,
            artifact_digest=decision.decision_digest,
            payload_digest=decision.decision_digest,
        ),
    )
    assert finalize(inputs) is None
    with pytest.raises(ValueError, match="Unsuccessful Qualification"):
        finalize(
            replace(
                inputs,
                observations=(
                    pair(_observation(case), "observation.json", 108),
                ),
            )
        )


@pytest.mark.parametrize(
    "wire", ["", None, '"null"', "[]", "[{}]", "{}", " null", "null\n", "false"]
)
def test_running_publisher_never_maps_malformed_or_missing_wire_to_null(wire):
    with pytest.raises((TypeError, ValueError)):
        parse_publication_terminal_reference(
            wire, publisher_conclusion="failure"
        )


@pytest.mark.parametrize("wire", [None, "", "null"])
def test_only_skipped_boundary_maps_absent_output_to_null(wire):
    assert (
        parse_publication_terminal_reference(
            wire, publisher_conclusion="skipped"
        )
        is None
    )


@pytest.mark.parametrize(
    "change",
    [
        "missing-marker",
        "different-marker-reference",
        "wrong-readback",
        "different-authorization",
        "profile",
        "duplicate-observation",
        "foreign-attempt",
    ],
)
def test_malformed_or_conflicting_chain_forms_no_outcome(
    observation_case, publisher, change
):
    marker = _prepare(publisher)
    result = _execute(publisher, *marker)
    inputs = replace(
        action_inputs(observation_case, publisher),
        terminal=pair(result, "result.json", 116),
        result_marker=marker,
    )
    if change == "missing-marker":
        inputs = replace(inputs, result_marker=None)
    elif change == "different-marker-reference":
        inputs = replace(
            inputs,
            result_marker=(marker[0], replace(marker[1], artifact_id=999)),
        )
    elif change == "wrong-readback":
        result = replace(
            result,
            post_action_readback=replace(
                result.post_action_readback, content_sha256="sha256:" + "a" * 64
            ),
        )
        inputs = replace(inputs, terminal=pair(result, "result.json", 116))
    elif change == "different-authorization":
        changed = replace(
            marker[0],
            publication_authorization_reference=replace(
                marker[0].publication_authorization_reference, artifact_id=999
            ),
        )
        inputs = replace(
            inputs, result_marker=pair(changed, "mutation-marker.json", 114)
        )
    elif change == "profile":
        changed = replace(
            marker[0],
            profile_match=replace(marker[0].profile_match, npm_version="1.0.0"),
        )
        changed_pair = pair(changed, "mutation-marker.json", 114)
        result = replace(result, mutation_marker_reference=changed_pair[1])
        inputs = replace(
            inputs,
            result_marker=changed_pair,
            terminal=pair(result, "result.json", 116),
        )
    elif change == "duplicate-observation":
        inputs = replace(inputs, observations=inputs.observations * 2)
    else:
        changed = replace(
            inputs.authorization[0],
            attempt=replace(
                inputs.authorization[0].attempt, workflow_run_id=999
            ),
            workflow_run_id=999,
        )
        inputs = replace(
            inputs, authorization=pair(changed, "authorization.json", 113)
        )
    with pytest.raises(
        ValueError,
        match=(
            r"(?i)(mismatch|requires|profile|readback|duplicate|direct marker)"
        ),
    ):
        finalize(inputs, job="failure", step="failure")


def test_result_readback_rejects_another_target_tag(
    observation_case, publisher
):
    marker = _prepare(publisher)
    result = _execute(publisher, *marker)
    result = replace(
        result,
        post_action_readback=replace(
            result.post_action_readback, tag="buddy-sha-" + "a" * 40
        ),
    )
    inputs = replace(
        action_inputs(observation_case, publisher),
        terminal=pair(result, "result.json", 116),
        result_marker=marker,
    )
    with pytest.raises(ValueError, match="readback binding mismatch"):
        finalize(inputs, job="success", step="success")


def test_outcome_has_only_owned_classification_and_one_direct_reference(
    observation_case,
):
    outcome = finalize(base_inputs(observation_case))
    assert set(outcome.to_document()) == {
        "schema",
        "attempt",
        "disposition",
        "possibly-mutated",
        "direct-predecessor",
        "producer",
        "control",
        "workflow-run-id",
    }
    for key in (
        "result",
        "terminal-phase",
        "action-result-digests",
        "next-action",
        "qualification-decision-digest",
    ):
        document = outcome.to_document() | {key: None}
        with pytest.raises(ValueError, match="unknown field"):
            admit_release_record(
                canonicalize(document),
                expected_type=AttemptOutcome,
                expected_digest=release_record_digest(outcome),
            )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("result", "success"),
        ("disposition", "incomplete"),
        ("disposition", "published"),
        ("possibly-mutated", True),
        ("producer", "publish-github-packages"),
    ],
)
def test_outcome_transport_rejects_copied_or_inconsistent_classification(
    observation_case, field, value
):
    document = finalize(base_inputs(observation_case)).to_document()
    document[field] = value
    payload = canonicalize(document)
    with pytest.raises(
        ValueError,
        match=r"(?i)(field|disposition|classification|producer)",
    ):
        admit_release_record(
            payload,
            expected_type=AttemptOutcome,
            expected_digest="sha256:" + hashlib.sha256(payload).hexdigest(),
        )


def test_outcome_admission_is_bound_to_current_run(observation_case):
    outcome = finalize(base_inputs(observation_case))
    with pytest.raises(ValueError, match=r"(?i)run"):
        admit_release_record(
            canonicalize(outcome.to_document()),
            expected_type=AttemptOutcome,
            expected_digest=outcome.outcome_digest,
            expected_bindings=ReleaseAdmissionBindings(
                purpose="live-release",
                workflow_run_id=outcome.workflow_run_id + 1,
                run_attempt=None,
                target=observation_case.intent.target,
            ),
        )


@pytest.mark.parametrize(
    "observer", ["success", "failure", "cancelled", None, ""]
)
def test_missing_observation_transport_cannot_fall_back_to_qualification(
    observation_case, observer
):
    assert finalize(base_inputs(observation_case), observer=observer) is None


@pytest.mark.parametrize("malformed", [False, True])
def test_presented_summary_is_admitted_even_before_bundle_materialization(
    observation_case, publisher, malformed
):
    inputs = replace(
        action_inputs(observation_case, publisher),
        bundle=None,
        authorization=None,
    )
    if malformed:
        inputs = replace(
            inputs,
            reviewer_summary=(b"substituted", inputs.reviewer_summary[1]),
        )
        with pytest.raises(ValueError, match="Reviewer payload reference"):
            finalize(inputs)
    else:
        outcome = finalize(inputs)
        assert outcome.disposition == "failed-before-publication"
        assert outcome.direct_predecessor.kind == (
            "action-bearing-publication-snapshot"
        )


def test_cli_partial_presented_record_never_forms_an_outcome(
    observation_case, tmp_path, monkeypatch
):
    args = finalization_cli_arguments(
        tmp_path / "inputs",
        observation_case,
        base_inputs(observation_case),
        monkeypatch,
    )
    outcome = tmp_path / "outcome.json"
    assert (
        cli.main(
            [
                "release",
                "finalize-live",
                *args,
                "--publisher-conclusion",
                "skipped",
                "--publication-terminal-reference",
                "",
                "--publication-snapshot",
                str(tmp_path / "not-uploaded.json"),
                "--outcome-output",
                str(outcome),
                "--summary-output",
                str(tmp_path / "summary.md"),
            ]
        )
        == 1
    )
    assert not outcome.exists()


@pytest.mark.parametrize(
    "invalid",
    ["artifact-digest", "payload-digest", "foreign-run-url", "schema"],
)
def test_cli_admits_no_reference_for_invalid_persisted_terminal(
    observation_case, publisher, tmp_path, invalid
):
    marker, reference = _prepare(publisher)
    if invalid == "schema":
        marker = publisher[0].authorization
        reference = pair(marker, reference.payload_path, reference.artifact_id)[
            1
        ]
    elif invalid == "foreign-run-url":
        reference = replace(
            reference,
            artifact_url="https://github.com/hcoona/three/actions/runs/999/artifacts/114",
        )
    args = uploaded_arguments(
        tmp_path,
        "terminal",
        marker.to_document(),
        reference.artifact_id,
        reference=reference,
    )
    if invalid in {"payload-digest", "artifact-digest"}:
        option = (
            "--terminal-digest"
            if invalid == "payload-digest"
            else "--terminal-artifact-digest"
        )
        args[args.index(option) + 1] = "sha256:" + "a" * 64
    output = tmp_path / "terminal-output"
    assert (
        cli.main(
            [
                "release",
                "admit-publication-terminal",
                *current_arguments(observation_case),
                *args,
                "--github-output",
                str(output),
            ]
        )
        == 1
    )
    assert not output.exists()
    assert not any(
        call[0][:2] == ("npm", "publish")
        for call in publisher[1]["runner"].calls
    )


def finalization_cli_arguments(root, case, inputs, monkeypatch):
    args = [
        *current_arguments(case),
        *authority_arguments(root, case, monkeypatch),
        *qualification_arguments(root, case),
    ]
    for evidence in case.evidence:
        name = {
            "release:build:npm-package": "build_evidence",
            "release:quality:project-test": "project_test_evidence",
            "release:quality:npm-artifact-contents": (
                "artifact_contents_evidence"
            ),
            "release:quality:npm-install-import": "install_import_evidence",
        }[evidence.obligation.obligation_id]
        args += uploaded_arguments(
            root, name, evidence.to_document(), 120 + len(args)
        )
    for name, record_pair in (
        (
            "observation",
            inputs.observations[0] if inputs.observations else None,
        ),
        ("publication_snapshot", inputs.publication),
        ("approval_bundle", inputs.bundle),
        ("publication_authorization", inputs.authorization),
        ("exact_satisfied_finalization_proof", inputs.exact_proof),
    ):
        if record_pair is not None:
            record, reference = record_pair
            args += uploaded_arguments(
                root,
                name,
                record.to_document(),
                reference.artifact_id,
                reference=reference,
            )
    if inputs.reviewer_summary is not None:
        content, reference = inputs.reviewer_summary
        (root / reference.payload_path).write_bytes(content)
        args += [
            "--reviewer-summary",
            str(root / reference.payload_path),
            "--reviewer-summary-digest",
            reference.payload_digest,
            "--reviewer-summary-artifact-id",
            str(reference.artifact_id),
            "--reviewer-summary-artifact-digest",
            reference.artifact_digest,
            "--reviewer-summary-artifact-url",
            reference.artifact_url,
            "--reviewer-summary-payload-path",
            reference.payload_path,
        ]
    return args


@pytest.mark.parametrize("terminal_kind", ["marker", "published", "failed"])
def test_cli_resolves_only_terminal_and_result_direct_marker(
    observation_case, publisher, tmp_path, monkeypatch, terminal_kind
):
    case = observation_case
    marker = _prepare(publisher)
    inputs = action_inputs(case, publisher)
    if terminal_kind == "marker":
        inputs = replace(inputs, terminal=marker)
    else:
        result = _execute(publisher, *marker)
        if terminal_kind == "failed":
            result = replace(
                result,
                result="failed",
                command_classification="ambiguous",
                mutation_classification="possibly-mutated",
            )
        inputs = replace(
            inputs,
            terminal=pair(result, "result.json", 116),
            result_marker=marker,
        )
    args = finalization_cli_arguments(
        tmp_path / "inputs", case, inputs, monkeypatch
    )
    terminal_dir = tmp_path / "terminal"
    terminal_dir.mkdir()
    (terminal_dir / inputs.terminal[1].payload_path).write_bytes(
        canonicalize(inputs.terminal[0].to_document())
    )
    reference = canonicalize(inputs.terminal[1].to_document()).decode()
    resolver_output = tmp_path / "resolver-output"
    assert (
        cli.main(
            [
                "release",
                "resolve-publication-terminal",
                *current_arguments(case),
                "--publisher-conclusion",
                "failure",
                "--publication-terminal-reference",
                reference,
                "--terminal-directory",
                str(terminal_dir),
                "--github-output",
                str(resolver_output),
            ]
        )
        == 0
    )
    resolved = resolver_output.read_text()
    assert f"terminal-artifact-id={inputs.terminal[1].artifact_id}" in resolved
    if terminal_kind != "marker":
        assert f"marker-artifact-id={marker[1].artifact_id}" in resolved
        marker_dir = tmp_path / "marker"
        marker_dir.mkdir()
        (marker_dir / marker[1].payload_path).write_bytes(
            canonicalize(marker[0].to_document())
        )
        args += ["--marker-directory", str(marker_dir)]
    else:
        assert "marker-artifact-id" not in resolved
    monkeypatch.setattr(
        cli,
        "GitHubPackagesHttpTransport",
        lambda: pytest.fail("Finalizer queried destination"),
    )
    monkeypatch.setattr(
        cli,
        "GitHubGovernanceClient",
        lambda **_: pytest.fail("Finalizer queried Governance"),
    )
    outcome_path = tmp_path / "outcome.json"
    status = cli.main(
        [
            "release",
            "finalize-live",
            *args,
            "--publisher-conclusion",
            "failure",
            "--publication-step-outcome",
            "failure",
            "--publication-terminal-reference",
            reference,
            "--terminal-directory",
            str(terminal_dir),
            "--outcome-output",
            str(outcome_path),
            "--summary-output",
            str(tmp_path / "summary.md"),
        ]
    )
    assert status == (0 if terminal_kind == "published" else 1)
    outcome = admit_release_record(
        outcome_path.read_bytes(),
        expected_type=AttemptOutcome,
        expected_digest="sha256:"
        + hashlib.sha256(outcome_path.read_bytes()).hexdigest(),
    )
    assert (
        outcome.disposition
        == {
            "marker": "unknown",
            "published": "published",
            "failed": "publication-failed",
        }[terminal_kind]
    )


@pytest.mark.parametrize("published", [True, False])
def test_cli_preparation_persistence_admission_and_execution_order(  # noqa: PLR0915
    observation_case, publisher, tmp_path, monkeypatch, published
):
    case = observation_case
    _prepared, common, preparation = publisher
    inputs = action_inputs(case, publisher)
    finalizer_args = finalization_cli_arguments(
        tmp_path / "inputs", case, inputs, monkeypatch
    )
    args = finalizer_args.copy()
    if not published:
        common["runner"].outcome = NpmProcessOutcome(
            "definitive-non-success", returncode=1
        )
    for prefix in (
        "build-evidence",
        "project-test-evidence",
        "artifact-contents-evidence",
        "install-import-evidence",
    ):
        for suffix in ("", "-digest", "-artifact-id", "-artifact-digest"):
            index = args.index("--" + prefix + suffix)
            del args[index : index + 2]
    for suffix in ("-artifact-url", "-payload-path"):
        index = args.index("--observation" + suffix)
        del args[index : index + 2]
    args += uploaded_arguments(
        tmp_path / "inputs", "adapter_context", case.context.to_document(), 112
    )
    args += [
        "--runtime-directory",
        str(common["runtime_directory"]),
        "--toolchain-directory",
        str(common["toolchain_directory"]),
        "--repo-root",
        str(common["checkout"]),
        "--github-token",
        common["token"],
    ]

    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            now = NOW + timedelta(seconds=2)
            return now if tz is None else now.astimezone(tz)

    monkeypatch.setattr(cli, "datetime", Clock)
    monkeypatch.setattr(
        cli, "IsolatedNpmProcessRunner", lambda: common["runner"]
    )
    monkeypatch.setattr(
        cli, "GitHubPackagesHttpTransport", lambda: common["transport"]
    )
    monkeypatch.setattr(
        cli,
        "GitHubGovernanceClient",
        lambda **_: preparation["governance_client"],
    )
    marker_path = tmp_path / "mutation-marker.json"
    outputs = tmp_path / "prepare-output"
    assert (
        cli.main(
            [
                "release",
                "prepare-publication",
                *args,
                "--tarball",
                str(preparation["tarball"]),
                "--output",
                str(marker_path),
                "--github-output",
                str(outputs),
            ]
        )
        == 0
    )
    assert common["runtime_directory"].is_dir()
    assert not any(
        call[0][:2] == ("npm", "publish") for call in common["runner"].calls
    )
    marker_bytes = marker_path.read_bytes()
    marker = admit_release_record(
        marker_bytes,
        expected_type=MutationMayHaveStartedMarker,
        expected_digest="sha256:" + hashlib.sha256(marker_bytes).hexdigest(),
        expected_bindings=common["current"],
    )
    marker_reference = pair(marker, marker_path.name, 114)[1]
    terminal_output = tmp_path / "terminal-output"
    terminal_args = uploaded_arguments(
        tmp_path,
        "terminal",
        marker.to_document(),
        114,
        reference=marker_reference,
    )
    assert (
        cli.main(
            [
                "release",
                "admit-publication-terminal",
                *current_arguments(case),
                *terminal_args,
                "--github-output",
                str(terminal_output),
            ]
        )
        == 0
    )
    wire = terminal_output.read_text().strip().split("=", 1)[1]
    assert wire == canonicalize(marker_reference.to_document()).decode()
    result_path = tmp_path / "result.json"
    result_outputs = tmp_path / "result-output"
    assert cli.main(
        [
            "release",
            "execute-publication",
            *args,
            "--publication-terminal-reference",
            wire,
            "--terminal-directory",
            str(tmp_path),
            "--output",
            str(result_path),
            "--github-output",
            str(result_outputs),
        ]
    ) == (0 if published else 1)
    result_bytes = result_path.read_bytes()
    result_digest = "sha256:" + hashlib.sha256(result_bytes).hexdigest()
    result = admit_release_record(
        result_bytes,
        expected_type=PublicationResult,
        expected_digest=result_digest,
        expected_bindings=common["current"],
    )
    assert result_bytes == canonicalize(result.to_document())
    assert result.result == ("published" if published else "failed")
    assert result.command_classification == (
        "definitive-success" if published else "definitive-non-success"
    )
    assert result.mutation_marker_reference == marker_reference
    emitted = dict(
        line.split("=", 1) for line in result_outputs.read_text().splitlines()
    )
    assert emitted["publication-result-digest"] == result_digest
    assert len(common["runner"].publications) == 1
    assert not common["runtime_directory"].exists()

    result_reference = ArtifactReference(
        artifact_id=116,
        artifact_digest=result_digest,
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/"
            f"{case.intent.workflow_run_id}/artifacts/116"
        ),
        payload_path=result_path.name,
        payload_digest=emitted["publication-result-digest"],
    )
    result_directory = tmp_path / "persisted-publication-result"
    result_directory.mkdir()
    persisted_result = result_directory / result_reference.payload_path
    persisted_result.write_bytes(result_bytes)
    result_terminal_output = tmp_path / "result-terminal-output"
    assert (
        cli.main(
            [
                "release",
                "admit-publication-terminal",
                *current_arguments(case),
                "--terminal",
                str(persisted_result),
                "--terminal-digest",
                emitted["publication-result-digest"],
                "--terminal-artifact-id",
                str(result_reference.artifact_id),
                "--terminal-artifact-digest",
                result_reference.artifact_digest,
                "--terminal-artifact-url",
                result_reference.artifact_url,
                "--terminal-payload-path",
                result_reference.payload_path,
                "--github-output",
                str(result_terminal_output),
            ]
        )
        == 0
    )
    wire = dict(
        line.split("=", 1)
        for line in result_terminal_output.read_text().splitlines()
    )["publication-terminal-reference"]
    assert wire == canonicalize(result_reference.to_document()).decode()
    platform_outcome = "success" if published else "failure"
    resolver_output = tmp_path / "resolver-output"
    assert (
        cli.main(
            [
                "release",
                "resolve-publication-terminal",
                *current_arguments(case),
                "--publisher-conclusion",
                platform_outcome,
                "--publication-terminal-reference",
                wire,
                "--terminal-directory",
                str(result_directory),
                "--github-output",
                str(resolver_output),
            ]
        )
        == 0
    )
    resolved = dict(
        line.split("=", 1) for line in resolver_output.read_text().splitlines()
    )
    assert resolved == {
        "terminal-artifact-id": str(result_reference.artifact_id),
        "marker-artifact-id": str(result.mutation_marker_reference.artifact_id),
    }
    monkeypatch.setattr(
        cli,
        "GitHubPackagesHttpTransport",
        lambda: pytest.fail("Finalizer queried destination"),
    )
    monkeypatch.setattr(
        cli,
        "GitHubGovernanceClient",
        lambda **_: pytest.fail("Finalizer queried Governance"),
    )
    outcome_path = tmp_path / "outcome.json"
    assert cli.main(
        [
            "release",
            "finalize-live",
            *finalizer_args,
            "--publisher-conclusion",
            platform_outcome,
            "--publication-step-outcome",
            platform_outcome,
            "--publication-terminal-reference",
            wire,
            "--terminal-directory",
            str(result_directory),
            "--marker-directory",
            str(tmp_path),
            "--outcome-output",
            str(outcome_path),
            "--summary-output",
            str(tmp_path / "summary.md"),
        ]
    ) == (0 if published else 1)
    outcome_bytes = outcome_path.read_bytes()
    outcome = admit_release_record(
        outcome_bytes,
        expected_type=AttemptOutcome,
        expected_digest="sha256:" + hashlib.sha256(outcome_bytes).hexdigest(),
        expected_bindings=common["current"],
    )
    assert outcome.disposition == (
        "published" if published else "publication-failed"
    )
    assert outcome.possibly_mutated is not published
    assert outcome.direct_predecessor == DirectPredecessor(
        "publication-result", result_reference
    )
    assert persisted_result.read_bytes() == result_bytes
    assert marker_path.read_bytes() == marker_bytes
