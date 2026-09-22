"""Independent literal oracles for CI request identities and decision rules."""

from __future__ import annotations

import hashlib
import json

import pytest
from three_workflow_delivery_v3.ci.rules import (
    CI_LANE_IDS,
    CI_LANES,
    CI_ROOT_HK_DEFINITION,
    EVIDENCE_OUTCOMES,
    RAW_OUTCOMES,
    SUPERSESSION_REASONS,
    SUPERSESSION_STATES,
    ci_definition_digest,
    ci_obligation_request_digest,
    decision_explanation,
    disposition_explanation,
    failure_action,
    normalize_required_outcome,
    supersession_reason,
    terminal_result,
)

DIGEST_A = "sha256:" + "a" * 64
DIGEST_B = "sha256:" + "b" * 64
DIGEST_C = "sha256:" + "c" * 64
DIGEST_D = "sha256:" + "d" * 64

REQUEST_INPUTS: dict[str, object] = {
    "candidate_digest": DIGEST_A,
    "repository_model_digest": DIGEST_B,
    "lane_id": "project-test",
    "definition_id": "node/project-test-v1",
    "definition_digest": DIGEST_C,
    "prerequisites": ("ci:project-build",),
    "selected": True,
    "scope_mode": "incremental",
    "changed_paths": ("src/example/index.ts", "src/example/package.json"),
    "selected_project_nodes": ("@example/project",),
    "selected_release_units": ("example-unit",),
    "selected_variants": ("npm-package",),
    "selected_outputs": (("npm-tarball", "primary-package", "npm-tarball"),),
}


def _literal_digest(document: object) -> str:
    """Hash literal JSON oracles independently of CI production rules."""
    encoded = json.dumps(
        document, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    ).encode("utf-8")
    return "sha256:" + hashlib.sha256(encoded).hexdigest()


def test_lane_definitions_and_prerequisites() -> None:
    """Bind slice lanes to their definitions and semantic prerequisites."""
    assert CI_LANES == {
        "root-hk": ("repository/source-tree-conformance-v1", ()),
        "project-build": ("node/project-build-v1", ()),
        "project-test": ("node/project-test-v1", ()),
        "npm-artifact-build": ("node/npm-artifact-v1", ()),
    }
    assert CI_LANE_IDS == (
        "root-hk",
        "project-build",
        "project-test",
        "npm-artifact-build",
    )
    assert CI_ROOT_HK_DEFINITION == "repository/source-tree-conformance-v1"


@pytest.mark.parametrize(
    ("definition_id", "document"),
    [
        (
            "repository/source-tree-conformance-v1",
            {
                "schema": "workflow-delivery/v3/quality-definition",
                "logical-id": "repository/source-tree-conformance-v1",
                "subject": "repository",
                "operation": "source-tree-conformance",
                "implementation-id": "repository/source-tree-conformance-v1",
                "execution-class": "control/read-only-v1",
                "capability-requirements": [],
            },
        ),
        (
            "node/project-build-v1",
            {
                "schema": "workflow-delivery/v3/quality-definition",
                "logical-id": "node/project-build-v1",
                "subject": "project-node",
                "operation": "project-build",
                "implementation-id": "node/project-build-v1",
                "execution-class": "target-execution/unprivileged-v1",
                "capability-requirements": [],
            },
        ),
        (
            "node/project-test-v1",
            {
                "schema": "workflow-delivery/v3/quality-definition",
                "logical-id": "node/project-test-v1",
                "subject": "project-node",
                "operation": "project-test",
                "implementation-id": "node/project-test-v1",
                "execution-class": "target-execution/unprivileged-v1",
                "capability-requirements": [],
            },
        ),
        (
            "node/npm-artifact-v1",
            {
                "schema": "workflow-delivery/v3/quality-definition",
                "logical-id": "node/npm-artifact-v1",
                "subject": "release-unit-variant",
                "operation": "npm-artifact",
                "implementation-id": "node/npm-artifact-v1",
                "execution-class": "target-execution/unprivileged-v1",
                "capability-requirements": [],
            },
        ),
    ],
    ids=("root-hk", "project-build", "project-test", "npm-artifact-build"),
)
def test_definition_digest_matches_literal_projection(
    definition_id: str,
    document: dict[str, object],
) -> None:
    """Protect every definition field with an independent literal projection."""
    assert ci_definition_digest(definition_id) == _literal_digest(document)


@pytest.mark.parametrize(
    ("selected", "digest"),
    [
        (
            True,
            "sha256:4c8608da00dea6e73ac2a08169ee382ed83bdf70d8d761d9f174444885d11f0d",
        ),
        (
            False,
            "sha256:188c3f2e2a27b538f16eb4384dbc42d1fa6a0bd5e28d9d3ec66221fa37602841",
        ),
    ],
    ids=("selected", "unselected"),
)
def test_request_identity_matches_literal_preimage(
    digest: str, *, selected: bool
) -> None:
    """Bind the complete request, including both selected and required flags."""
    document = {
        "schema": "workflow-delivery/v3/ci-obligation-request",
        "candidate-digest": DIGEST_A,
        "repository-model-digest": DIGEST_B,
        "lane-id": "project-test",
        "definition-id": "node/project-test-v1",
        "definition-digest": DIGEST_C,
        "prerequisites": ["ci:project-build"],
        "selected": selected,
        "required": selected,
        "scope-mode": "incremental",
        "changed-paths": ["src/example/index.ts", "src/example/package.json"],
        "selected-project-nodes": ["@example/project"],
        "selected-release-units": ["example-unit"],
        "selected-variants": ["npm-package"],
        "selected-outputs": [
            {
                "output-id": "npm-tarball",
                "logical-role": "primary-package",
                "media-kind": "npm-tarball",
            }
        ],
    }
    inputs = REQUEST_INPUTS | {"selected": selected}
    assert _literal_digest(document) == digest
    assert ci_obligation_request_digest(**inputs) == digest  # type: ignore[arg-type]


def test_request_identity_preserves_complete_alternate_mapping() -> None:
    """Map distinct scalar and ordered tuple inputs, without claiming a Plan."""
    document = {
        "schema": "workflow-delivery/v3/ci-obligation-request",
        "candidate-digest": "sha256:" + "d" * 64,
        "repository-model-digest": "sha256:" + "e" * 64,
        "lane-id": "project-build",
        "definition-id": "node/project-build-v1",
        "definition-digest": "sha256:" + "f" * 64,
        "prerequisites": ["ci:z", "ci:a"],
        "selected": False,
        "required": False,
        "scope-mode": "slice-validation",
        "changed-paths": ["z/package.json", "a/index.ts"],
        "selected-project-nodes": ["@example/zeta", "@example/alpha"],
        "selected-release-units": ["z-unit", "a-unit"],
        "selected-variants": ["z-variant", "a-variant"],
        "selected-outputs": [
            {
                "output-id": "z-output",
                "logical-role": "z-role",
                "media-kind": "z-media",
            },
            {
                "output-id": "a-output",
                "logical-role": "a-role",
                "media-kind": "a-media",
            },
        ],
    }
    digest = (
        "sha256:"
        "5689d006eedd5c8519be10b6d2a30b61eb9c023abe65fbc9adb74ecdd164b863"
    )
    assert _literal_digest(document) == digest
    assert (
        ci_obligation_request_digest(
            candidate_digest=DIGEST_D,
            repository_model_digest="sha256:" + "e" * 64,
            lane_id="project-build",
            definition_id="node/project-build-v1",
            definition_digest="sha256:" + "f" * 64,
            prerequisites=("ci:z", "ci:a"),
            selected=False,
            scope_mode="slice-validation",
            changed_paths=("z/package.json", "a/index.ts"),
            selected_project_nodes=("@example/zeta", "@example/alpha"),
            selected_release_units=("z-unit", "a-unit"),
            selected_variants=("z-variant", "a-variant"),
            selected_outputs=(
                ("z-output", "z-role", "z-media"),
                ("a-output", "a-role", "a-media"),
            ),
        )
        == digest
    )


@pytest.mark.parametrize(
    ("raw", "normalized"),
    [
        ("success", "satisfied"),
        ("failure", "failed"),
        ("skipped", "skipped"),
        ("timed-out", "timed-out"),
        ("unknown", "unknown"),
    ],
)
def test_required_outcomes_are_closed_and_mechanical(
    raw: str,
    normalized: str,
) -> None:
    """Normalize only mechanically observable executor outcomes."""
    assert normalize_required_outcome(raw) == normalized


def test_closed_rule_domains() -> None:
    """Protect the closed outcome and supersession domains."""
    assert {
        "success",
        "failure",
        "skipped",
        "timed-out",
        "unknown",
    } == RAW_OUTCOMES
    assert {
        "satisfied",
        "failed",
        "skipped",
        "timed-out",
        "unknown",
    } == EVIDENCE_OUTCOMES
    assert {
        "not-superseded",
        "superseded",
        "unsupported",
        "not-applicable",
    } == SUPERSESSION_STATES
    assert {
        "trusted-current-candidate",
        "trusted-superseded-candidate",
        "platform-proof-unavailable",
        "not-pull-request",
    } == SUPERSESSION_REASONS


def test_finalizer_only_outcome_cannot_form_evidence() -> None:
    """Keep finalizer incompleteness out of raw Evidence normalization."""
    with pytest.raises(ValueError):  # noqa: PT011 - Internal exception wording.
        normalize_required_outcome("incomplete")


@pytest.mark.parametrize(
    ("lane_outcomes", "result", "explanation", "failure"),
    [
        pytest.param(
            (),
            "failure",
            "CI slice Plan was not ready for required work",
            ("incomplete-model-plan", "fix-model-plan-and-rerun"),
            id="no-selected-work",
        ),
        pytest.param(
            (("root-hk", "satisfied"),),
            "success",
            "all selected CI slice obligations were satisfied",
            ("none", "none"),
            id="satisfied",
        ),
        pytest.param(
            (("root-hk", "satisfied"), ("project-test", "satisfied")),
            "success",
            "all selected CI slice obligations were satisfied",
            ("none", "none"),
            id="all-selected-satisfied",
        ),
        pytest.param(
            (("project-test", "failed"),),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "project-test=failed",
            ("quality-failure", "fix-quality-failure-and-rerun"),
            id="failed",
        ),
        pytest.param(
            (("project-test", "skipped"),),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "project-test=skipped",
            ("incomplete-qualification", "rerun-candidate"),
            id="skipped",
        ),
        pytest.param(
            (("project-test", "timed-out"),),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "project-test=timed-out",
            ("incomplete-qualification", "rerun-candidate"),
            id="timed-out",
        ),
        pytest.param(
            (("project-test", "unknown"),),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "project-test=unknown",
            ("incomplete-qualification", "rerun-candidate"),
            id="unknown",
        ),
        pytest.param(
            (("project-test", "incomplete"),),
            "incomplete",
            "selected CI slice obligations are incomplete: project-test",
            ("incomplete-qualification", "rerun-candidate"),
            id="missing",
        ),
        pytest.param(
            (("project-build", "failed"), ("project-test", "incomplete")),
            "incomplete",
            "selected CI slice obligations are incomplete: project-test",
            ("incomplete-qualification", "rerun-candidate"),
            id="failed-and-missing",
        ),
        pytest.param(
            (("project-test", "failed"), ("root-hk", "unknown")),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "project-test=failed, root-hk=unknown",
            ("incomplete-qualification", "rerun-candidate"),
            id="failed-and-unknown",
        ),
        pytest.param(
            (
                ("root-hk", "incomplete"),
                ("project-build", "satisfied"),
                ("project-test", "incomplete"),
            ),
            "incomplete",
            "selected CI slice obligations are incomplete: "
            "root-hk, project-test",
            ("incomplete-qualification", "rerun-candidate"),
            id="missing-explanation-order",
        ),
        pytest.param(
            (
                ("root-hk", "failed"),
                ("project-build", "satisfied"),
                ("project-test", "timed-out"),
            ),
            "failure",
            "selected CI slice obligations were not satisfied: "
            "root-hk=failed, project-test=timed-out",
            ("incomplete-qualification", "rerun-candidate"),
            id="unsatisfied-explanation-order",
        ),
    ],
)
def test_selected_outcomes_determine_decision(
    lane_outcomes: tuple[tuple[str, str], ...],
    result: str,
    explanation: str,
    failure: tuple[str, str],
) -> None:
    """Preserve verdict priority, ordered explanations and corrective action."""
    outcomes = tuple(outcome for _, outcome in lane_outcomes)
    assert terminal_result(outcomes) == result
    assert decision_explanation(lane_outcomes, result) == explanation
    assert failure_action(outcomes) == failure


@pytest.mark.parametrize(
    ("selected", "outcome", "explanation"),
    [
        (False, "empty", "project-test was not selected"),
        (
            True,
            "incomplete",
            "project-test selected work did not emit Evidence",
        ),
        (True, "satisfied", "project-test satisfied"),
        (True, "failed", "project-test failed"),
    ],
)
def test_disposition_explanation(
    outcome: str, explanation: str, *, selected: bool
) -> None:
    """Distinguish unselected work, absent Evidence and observed outcomes."""
    assert (
        disposition_explanation(
            "project-test", selected=selected, outcome=outcome
        )
        == explanation
    )


@pytest.mark.parametrize(
    ("state", "reason"),
    [
        ("not-superseded", "trusted-current-candidate"),
        ("superseded", "trusted-superseded-candidate"),
        ("unsupported", "platform-proof-unavailable"),
        ("not-applicable", "not-pull-request"),
    ],
)
def test_supersession_reason(state: str, reason: str) -> None:
    """Distinguish trusted freshness from unavailable or inapplicable proof."""
    assert supersession_reason(state) == reason


def test_supersession_reason_rejects_unknown_state() -> None:
    """Reject unsupported state spellings instead of claiming current proof."""
    with pytest.raises(ValueError):  # noqa: PT011 - Internal exception wording.
        supersession_reason("current")
