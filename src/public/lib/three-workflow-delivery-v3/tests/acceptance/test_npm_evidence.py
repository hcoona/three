"""Synthetic service metadata and local fixtures, never native provenance."""

# ruff: noqa: D103, PLR2004

from __future__ import annotations

import json
from dataclasses import dataclass, replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance.npm_evidence import read_npm_evidence
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    build_npm_fixture,
)
from three_workflow_delivery_v3.acceptance.npm_probe import (
    NpmProbeRequest,
    NpmProbeResult,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.release import ProfileMatchEvidence
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

ROOT = Path(__file__).resolve().parents[6]
RUN_ID = 456
TOOLING_SHA = "c" * 40
WORKFLOW = ".github/workflows/workflow-delivery-v3-native-npm-acceptance.yml"
REQUEST = NpmProbeRequest(
    NpmFixtureSpec(
        "@hcoona/synthetic-native-audit",
        "0.0.1-acceptance.1",
        "b" * 40,
        "synthetic-audit",
    ),
    DisposablePackagePreconditions(
        "@hcoona/synthetic-native-audit",
        preexisting_container=True,
        operator_controlled=True,
        production_dependency=False,
    ),
)


@dataclass
class SyntheticServiceMetadata:
    """Only test documents; no authenticated response or native provenance."""

    run: dict
    artifact: dict


@pytest.fixture
def evidence_case(tmp_path, request):
    requested = replace(
        REQUEST,
        fixture=replace(
            REQUEST.fixture, variant=getattr(request, "param", "original")
        ),
    )
    fixture = build_npm_fixture(requested.fixture, repository_root=ROOT)
    profile = github_packages_destination_operation_profile()
    tag = "buddy-sha-" + requested.fixture.target
    match = ProfileMatchEvidence(
        destination_operation_profile_digest=profile.profile_digest,
        node_version=profile.node_version,
        npm_version=profile.npm_version,
        command=tuple(
            {
                "{tarball-path}": (
                    f"/runner/wdv3-native-npm-{RUN_ID}/runtime/fixture.tgz"
                ),
                "{tag}": tag,
            }.get(word, word)
            for word in profile.command_template
        ),
        configuration=tuple(
            sorted(
                {
                    "@hcoona:registry": profile.registry,
                    "registry": profile.registry + "/",
                    "tag": tag,
                    "ignore-scripts": "true",
                    "fetch-retries": "0",
                    "access": "null",
                }.items()
            )
        ),
        matched_at="2026-09-07T00:00:00Z",
    )
    bundle = tmp_path / "bundle"
    evidence = bundle / "evidence"
    evidence.mkdir(parents=True)
    (bundle / "platform.json").write_bytes(
        canonicalize(
            {
                "schema": "workflow-delivery-v3/native-npm-actions-context/v1",
                "run_id": str(RUN_ID),
                "run_attempt": "1",
                "sha": TOOLING_SHA,
                "ref": "refs/heads/main",
                "actor_id": "712433",
                "repository": "hcoona/three",
                "event_name": "workflow_dispatch",
                "workflow_ref": f"hcoona/three/{WORKFLOW}@refs/heads/main",
            }
        )
    )
    (evidence / "request.json").write_bytes(
        canonicalize(requested.to_document())
    )
    (evidence / "fixture.tgz").write_bytes(fixture.tarball)
    (evidence / "profile-match.json").write_bytes(
        canonicalize(match.to_document())
    )
    (evidence / "command-started").write_bytes(b"")
    (evidence / "result.json").write_bytes(
        canonicalize(
            NpmProbeResult(
                canonical_sha256(requested.to_document()),
                match.match_digest,
                fixture.content,
                "definitive-success",
                0,
                truncated=False,
            ).to_document()
        )
    )
    metadata = SyntheticServiceMetadata(
        run={
            "id": RUN_ID,
            "run_attempt": 1,
            "head_sha": TOOLING_SHA,
            "head_branch": "main",
            "path": WORKFLOW,
            "event": "workflow_dispatch",
            "status": "completed",
            "conclusion": "success",
            "actor": {"id": 712433, "login": "hcoona"},
            "repository": {"full_name": "hcoona/three"},
            "html_url": f"https://github.com/hcoona/three/actions/runs/{RUN_ID}",
        },
        artifact={
            "id": 789,
            "name": f"wdv3-native-npm-probe-{RUN_ID}",
            "expired": False,
            "digest": "sha256:" + "d" * 64,
            "url": "https://api.github.com/repos/hcoona/three/actions/artifacts/789",
            "archive_download_url": (
                "https://api.github.com/repos/hcoona/three/actions/artifacts/789/zip"
            ),
            "workflow_run": {"id": RUN_ID, "head_sha": TOOLING_SHA},
        },
    )
    return {
        "bundle": bundle,
        "request": requested,
        "fixture": fixture,
        "match": match,
        "metadata": metadata,
    }


def _read(case):
    return read_npm_evidence(
        case["bundle"],
        raw_run_metadata=json.dumps(case["metadata"].run, indent=2).encode(),
        raw_artifact_metadata=json.dumps(
            case["metadata"].artifact, indent=2
        ).encode(),
        expected_run_id=RUN_ID,
        expected_tooling_sha=TOOLING_SHA,
        expected_request=case["request"],
        repository_root=ROOT,
    )


def _rewrite(case, name, **changes):
    path = case["bundle"] / name
    document = parse_canonical_json(path.read_bytes())
    document.update(changes)
    path.write_bytes(canonicalize(document))
    return document


@pytest.mark.parametrize(
    "evidence_case", ["original", "different"], indirect=True
)
def test_complete_audit_binds_actual_fixture_and_preserves_raw_service_facts(
    evidence_case,
):
    case = evidence_case
    evidence = _read(case)

    assert evidence.run_id == RUN_ID
    assert evidence.tooling_sha == TOOLING_SHA
    assert evidence.request == case["request"]
    assert evidence.fixture == case["fixture"]
    assert evidence.profile_match == case["match"]
    assert evidence.process == NpmProcessOutcome(
        "definitive-success", returncode=0
    )
    assert evidence.artifact_id == 789
    assert evidence.artifact_digest == case["metadata"].artifact["digest"]
    assert evidence.artifact_digest != evidence.fixture.content.sha256
    assert evidence.artifact_url == case["metadata"].artifact["url"]
    assert (
        evidence.raw_run_metadata
        == json.dumps(case["metadata"].run, indent=2).encode()
    )
    assert (
        evidence.raw_artifact_metadata
        == json.dumps(case["metadata"].artifact, indent=2).encode()
    )


@pytest.mark.parametrize(
    ("classification", "returncode", "truncated"),
    [
        ("definitive-non-success", 1, False),
        ("ambiguous", -9, True),
        ("not-initiated", None, False),
        ("definitive-success", 0, True),
    ],
)
def test_negative_duplicate_and_uncertain_outcomes_remain_process_facts(
    evidence_case, classification, returncode, truncated
):
    _rewrite(
        evidence_case,
        "evidence/result.json",
        command_classification=classification,
        returncode=returncode,
        truncated=truncated,
    )
    evidence_case["metadata"].run["conclusion"] = (
        "success" if classification == "definitive-success" else "failure"
    )

    result = _read(evidence_case)

    assert result.process == NpmProcessOutcome(
        classification, truncated=truncated, returncode=returncode
    )
    assert result.fixture == evidence_case["fixture"]
    assert not hasattr(result, "native_pass")
    assert not hasattr(result, "not_mutated")


@pytest.mark.parametrize(
    ("source", "changes"),
    [
        ("run", {"id": RUN_ID + 1}),
        ("run", {"run_attempt": 2}),
        ("run", {"run_attempt": True}),
        ("run", {"head_sha": "a" * 40}),
        ("run", {"head_branch": "topic"}),
        ("run", {"path": ".github/workflows/other.yml"}),
        ("run", {"actor": {"id": 123}}),
        ("run", {"repository": {"full_name": "other/three"}}),
        ("run", {"event": "push"}),
        ("run", {"status": "in_progress"}),
        ("artifact", {"name": "wdv3-native-npm-probe-other"}),
        ("artifact", {"expired": True}),
        (
            "artifact",
            {"workflow_run": {"id": RUN_ID + 1, "head_sha": TOOLING_SHA}},
        ),
        ("artifact", {"workflow_run": {"id": RUN_ID, "head_sha": "a" * 40}}),
        ("artifact", {"id": 0}),
        ("artifact", {"digest": ""}),
        (
            "artifact",
            {
                "url": "https://api.github.com/repos/other/three/actions/artifacts/789"
            },
        ),
    ],
)
def test_core_service_identity_mismatches_stop_reading(
    evidence_case, source, changes
):
    getattr(evidence_case["metadata"], source).update(changes)

    with pytest.raises(ValueError, match=r"mismatch|positive|digest"):
        _read(evidence_case)


@pytest.mark.parametrize(
    "conclusion", ["failure", "cancelled", "timed_out", "skipped"]
)
def test_run_conclusion_must_match_recorded_process(evidence_case, conclusion):
    evidence_case["metadata"].run["conclusion"] = conclusion

    with pytest.raises(ValueError, match=r"run\.conclusion"):
        _read(evidence_case)


def test_failed_process_cannot_use_successful_run(evidence_case):
    _rewrite(
        evidence_case,
        "evidence/result.json",
        command_classification="definitive-non-success",
        returncode=1,
    )
    with pytest.raises(ValueError, match=r"run\.conclusion"):
        _read(evidence_case)


def test_absent_workflow_run_relies_on_callers_exact_run_list_membership(
    evidence_case,
):
    del evidence_case["metadata"].artifact["workflow_run"]

    result = _read(evidence_case)

    assert result.run_id == RUN_ID
    assert "workflow_run" not in json.loads(result.raw_artifact_metadata)


@pytest.mark.parametrize(
    "changes",
    [
        {"run_id": str(RUN_ID + 1)},
        {"run_attempt": "2"},
        {"actor_id": "123"},
        {"ref": "refs/heads/topic"},
        {"sha": "a" * 40},
        {"workflow_ref": f"hcoona/three/{WORKFLOW}@refs/heads/topic"},
    ],
)
def test_actual_platform_context_is_bound_independently(evidence_case, changes):
    _rewrite(evidence_case, "platform.json", **changes)

    with pytest.raises(ValueError, match="platform context"):
        _read(evidence_case)


@pytest.mark.parametrize(
    "name", ["result.json", "command-started", "fixture.tgz"]
)
def test_incomplete_audit_has_no_fallback(evidence_case, name):
    (evidence_case["bundle"] / "evidence" / name).unlink()

    with pytest.raises(ValueError, match="artifact files"):
        _read(evidence_case)


@pytest.mark.parametrize("name", ["command-started", "unexpected"])
def test_indicator_content_or_extra_files_are_not_accepted(evidence_case, name):
    (evidence_case["bundle"] / "evidence" / name).write_bytes(b"unexpected")

    with pytest.raises(ValueError, match=r"command-started|artifact files"):
        _read(evidence_case)


@pytest.mark.parametrize(
    "changes",
    [
        {"generation": "another-generation"},
        {"version": "0.0.1-acceptance.2"},
        {"package": "@hcoona/other-synthetic-audit"},
        {"target": "a" * 40},
        {"variant": "different"},
    ],
)
def test_actual_fixture_substitution_rejects_even_with_consistent_result(
    evidence_case, changes
):
    fixture = build_npm_fixture(
        replace(evidence_case["request"].fixture, **changes),
        repository_root=ROOT,
    )
    (evidence_case["bundle"] / "evidence/fixture.tgz").write_bytes(
        fixture.tarball
    )
    _rewrite(
        evidence_case,
        "evidence/result.json",
        fixture_content=fixture.content.to_document(),
    )

    with pytest.raises(ValueError, match="actual fixture/request"):
        _read(evidence_case)


def test_request_and_actual_fixture_cannot_replace_caller_expectation(
    evidence_case,
):
    alternate = replace(
        evidence_case["request"],
        fixture=replace(evidence_case["request"].fixture, generation="other"),
    )
    _rewrite(evidence_case, "evidence/request.json", **alternate.to_document())
    fixture = build_npm_fixture(alternate.fixture, repository_root=ROOT)
    (evidence_case["bundle"] / "evidence/fixture.tgz").write_bytes(
        fixture.tarball
    )
    _rewrite(
        evidence_case,
        "evidence/result.json",
        request_digest=canonical_sha256(alternate.to_document()),
        fixture_content=fixture.content.to_document(),
    )

    with pytest.raises(ValueError, match="expected request"):
        _read(evidence_case)


def test_request_preconditions_are_not_inferred(evidence_case):
    document = evidence_case["request"].to_document()
    document["disposable_package_preconditions"]["operator_controlled"] = False
    _rewrite(evidence_case, "evidence/request.json", **document)

    with pytest.raises(ValueError, match="disposable_package_preconditions"):
        _read(evidence_case)


@pytest.mark.parametrize(
    "field", ["request_digest", "profile_match_digest", "fixture_content"]
)
def test_result_digest_substitutions_are_rejected(evidence_case, field):
    value = "sha256:" + "a" * 64
    if field == "fixture_content":
        value = {
            **evidence_case["fixture"].content.to_document(),
            "sha256": value,
        }
    _rewrite(evidence_case, "evidence/result.json", **{field: value})

    with pytest.raises(ValueError, match="probe result"):
        _read(evidence_case)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("node-version", "24.0.0"),
        ("npm-version", "11.0.0"),
        ("destination-operation-profile-digest", "sha256:" + "a" * 64),
        ("tag", "latest"),
        (
            "operand",
            f"/runner/wdv3-native-npm-{RUN_ID + 1}/runtime/fixture.tgz",
        ),
    ],
)
def test_profile_binding_cannot_be_replaced_by_rehashing(
    evidence_case, field, value
):
    document = evidence_case["match"].to_document()
    if field == "tag":
        document["command"][document["command"].index("--tag") + 1] = value
    elif field == "operand":
        document["command"][2] = value
    else:
        document[field] = value
    _rewrite(evidence_case, "evidence/profile-match.json", **document)
    _rewrite(
        evidence_case,
        "evidence/result.json",
        profile_match_digest=canonical_sha256(document),
    )

    with pytest.raises(ValueError, match=r"profile|operand"):
        _read(evidence_case)


@pytest.mark.parametrize(
    "changes",
    [
        {"command_classification": "native-PASS"},
        {"returncode": 1},
        {"returncode": False},
        {"truncated": 0},
        {"not_mutated": True},
        {"output": "must never be retained"},
        {"schema": "other"},
    ],
)
def test_result_uses_closed_process_facts(evidence_case, changes):
    _rewrite(evidence_case, "evidence/result.json", **changes)

    with pytest.raises((ValueError, TypeError)):
        _read(evidence_case)


@pytest.mark.parametrize(
    "name",
    [
        "platform.json",
        "evidence/request.json",
        "evidence/profile-match.json",
        "evidence/result.json",
    ],
)
def test_producer_json_must_remain_canonical(evidence_case, name):
    path = evidence_case["bundle"] / name
    path.write_bytes(path.read_bytes() + b"\n")

    with pytest.raises(ValueError, match="canonical"):
        _read(evidence_case)


def test_malformed_actual_tarball_is_rejected_by_official_fixture_reader(
    evidence_case,
):
    (evidence_case["bundle"] / "evidence/fixture.tgz").write_bytes(
        b"not a tarball"
    )

    with pytest.raises(ValueError, match="invalid npm tarball"):
        _read(evidence_case)
