"""Native publisher CLI with admitted records and modeled destination I/O."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import hashlib
import json
from datetime import timedelta
from pathlib import Path
from subprocess import TimeoutExpired
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import (
    ArtifactReference,
    AttemptOutcome,
    ExactSatisfiedFinalizationProof,
    MutationMayHaveStartedMarker,
    PublicationResult,
)
from three_workflow_delivery_v3.release.finalizer import finalize_qualification
from three_workflow_delivery_v3.release.nuget_qualification import (
    NugetMechanicalBuildResult,
    form_uploaded_nuget_release_artifact,
    qualify_release_nuget_consumer,
    qualify_release_nuget_contents,
)

from .release.test_nuget_destination import RESOURCES, _modeled_profile_document
from .release.test_nuget_destination_runtime import NOW
from .release.test_nuget_qualification import _transport
from .test_nuget_approval_cli import (
    COMMANDS as APPROVAL_COMMANDS,
)
from .test_nuget_approval_cli import (
    _args as _approval_args,
)
from .test_nuget_approval_cli import (
    _prepare,
    _reference_args,
)
from .test_nuget_approval_cli import (
    approval_case as approval_case,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    control_case as control_case,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    control_source as control_source,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    enabled_case as enabled_case,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    prepared_records as prepared_records,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    qualification_case as qualification_case,  # noqa: PLC0414
)
from .test_nuget_approval_cli import (
    qualification_source as qualification_source,  # noqa: PLC0414
)
from .test_nuget_cli import TOKEN, _clock, _set_option, _uploaded
from .test_nuget_qualification_cli import _artifact_args, _record

COMMANDS = (
    "prove-exact-satisfied",
    "prepare-publication",
    "execute-publication",
    "finalize-live",
)


@pytest.fixture
def publication_case(approval_case, monkeypatch):
    case = approval_case
    # This CLI layer models the imported Windows profile and native helper.
    # The adapter's own tests cover actual host/source profile collection.
    monkeypatch.setattr(
        native, "nuget_operation_profile", _modeled_profile_document
    )
    case.package.write_bytes(case.qualified.result.package)
    for name in ("proof", "marker", "result", "outcome"):
        setattr(case, name, case.root / (name + ".json"))
    case.runtime = case.root / "publisher-runtime"
    case.final_summary = case.root / "final-summary.md"
    case.step_summary = case.root / "step-summary.md"
    case.publish = Mock(side_effect=AssertionError("unexpected publication"))
    monkeypatch.setattr(native, "publish_nuget_once", case.publish)
    return case


def _reset_effects(case):
    case.github_output.unlink(missing_ok=True)
    case.feed.events.clear()
    for mock in (
        case.client,
        case.client_factory,
        case.helper_factory,
        case.transport_factory,
        case.build,
        case.contents,
        case.consumer,
        case.publish,
    ):
        mock.reset_mock()


def _basis(case, *, exact=False):
    if exact:
        case.feed.present = True
        key = "publication-exact-basis"
        paths = (case.observation, case.publication)
        if key not in case.preconditions:
            for command in APPROVAL_COMMANDS[:2]:
                assert cli.main(_approval_args(case, command)) == 0
            case.preconditions[key] = tuple(path.read_bytes() for path in paths)
        else:
            for path, content in zip(
                paths, case.preconditions[key], strict=True
            ):
                path.write_bytes(content)
    else:
        arguments = _prepare(case, APPROVAL_COMMANDS[3])
        key = "publication-input-authorization"
        if key not in case.preconditions:
            assert cli.main(arguments) == 0
            case.preconditions[key] = case.authorization.read_bytes()
        else:
            case.authorization.write_bytes(case.preconditions[key])
    _reset_effects(case)


def _authority_args(case):
    return [
        *case.current,
        "--repo-root",
        str(case.source.repo),
        *case.intent_args,
        *case.model_args,
        *case.eligibility_args,
        *case.attempt_args,
        *case.snapshot_args,
        *_reference_args(case, "qualification_decision", case.decision, 506),
    ]


def _args(case, command):
    arguments = [
        "release",
        "nuget",
        command,
        *_authority_args(case),
        *_artifact_args(case),
        *_uploaded(
            case.root,
            "observation",
            json.loads(case.observation.read_bytes()),
            601,
        ),
        *_reference_args(case, "publication_snapshot", case.publication, 602),
        "--github-token",
        TOKEN,
        "--helper-dll",
        str(case.root / "authority.dll"),
        "--github-output",
        str(case.github_output),
    ]
    output = {
        COMMANDS[0]: case.proof,
        COMMANDS[1]: case.marker,
        COMMANDS[2]: case.result,
    }[command]
    arguments += ["--output", str(output)]
    if command == COMMANDS[0]:
        return [
            *arguments,
            "--control",
            case.source.context.control,
            "--publisher-conclusion",
            "skipped",
        ]
    for name, path, artifact_id in (
        ("approval_bundle", case.bundle, 604),
        ("reviewer_summary", case.summary, 603),
        ("publication_authorization", case.authorization, 605),
    ):
        arguments += _reference_args(case, name, path, artifact_id)
    arguments += ["--runtime-directory", str(case.runtime)]
    if command == COMMANDS[1]:
        arguments += ["--package", str(case.package)]
    else:
        arguments += [
            "--publication-terminal-reference",
            case.marker_wire,
            "--terminal-directory",
            str(case.root),
        ]
    return arguments


def _admit_terminal(case, path, artifact_id):
    output = case.root / "terminal-output"
    output.unlink(missing_ok=True)
    assert (
        cli.main(
            [
                "release",
                "admit-publication-terminal",
                *case.current,
                *_reference_args(case, "terminal", path, artifact_id),
                "--github-output",
                str(output),
            ]
        )
        == 0
    )
    return dict(line.split("=", 1) for line in output.read_text().splitlines())[
        "publication-terminal-reference"
    ]


def _prepared(case):
    _basis(case)
    # Preparation has separate command scenarios. Consumer cases may reuse its
    # immutable output bytes; every case owns its runtime and admits the marker.
    key = "publication-prepared-inputs"
    package = case.runtime / case.artifact_record.content.basename
    if key not in case.preconditions:
        assert cli.main(_args(case, COMMANDS[1])) == 0
        case.preconditions[key] = (
            case.marker.read_bytes(),
            package.read_bytes(),
        )
    else:
        marker_bytes, package_bytes = case.preconditions[key]
        case.marker.write_bytes(marker_bytes)
        case.runtime.mkdir(mode=0o700)
        package.write_bytes(package_bytes)
    case.marker_wire = _admit_terminal(case, case.marker, 606)
    _reset_effects(case)


def _invocation(case, disposition):
    def publish(**kwargs):
        # The durable marker was admitted before the exclusive claim/invocation.
        assert case.marker.is_file()
        assert (case.runtime / "command-started").is_file()
        assert kwargs["package"] == case.qualified.result.package
        assert kwargs["expected_profile_sha256"] == case.profile.profile_digest
        assert kwargs["expected_package_sha256"] == (
            case.artifact_record.content.content_sha256.removeprefix("sha256:")
        )
        case.feed.present = True
        response = (
            None
            if disposition == "lost-response"
            else native.NuGetHttpResponse(
                RESOURCES.package_publish,
                201 if disposition == "created" else 409,
                (),
                b"modeled-response",
            )
        )
        return native.NuGetPublicationInvocation(
            case.profile.profile_digest,
            kwargs["expected_package_sha256"],
            "f" * 64,
            response,
            "response-lost" if response is None else None,
        )

    case.publish.side_effect = publish


def _assert_build_free(case):
    case.evaluate.assert_not_called()
    case.build.assert_not_called()
    case.contents.assert_not_called()
    case.consumer.assert_not_called()


def test_nuget_publication_cli_proves_fresh_exact_state(
    publication_case, monkeypatch
):
    case = publication_case
    _basis(case, exact=True)
    _clock(monkeypatch, NOW + timedelta(minutes=1))
    assert cli.main(_args(case, COMMANDS[0])) == 0
    proof = _record(case.proof, ExactSatisfiedFinalizationProof)
    assert proof.attempt == case.qualified.binding.attempt
    assert proof.proved_at == (NOW + timedelta(minutes=1)).strftime(
        "%Y-%m-%dT%H:%M:%SZ"
    )
    assert (
        proof.exact_version_readback.content_sha256
        == case.artifact_record.content.content_sha256
    )
    assert (
        proof.exact_version_readback.witness_digest
        == case.artifact_record.witness_digest
    )
    assert (
        f"exact-satisfied-finalization-proof-digest={proof.proof_digest}"
        in case.github_output.read_text()
    )
    assert case.feed.events
    case.client.read_source.assert_called_once()
    case.publish.assert_not_called()
    _assert_build_free(case)


@pytest.mark.parametrize("change", ["archive", "governance", "publisher"])
def test_nuget_publication_cli_rejects_changed_exact_state(
    publication_case, change, capsys
):
    case = publication_case
    _basis(case, exact=True)
    arguments = _args(case, COMMANDS[0])
    if change == "archive":
        case.feed.content += b"later different archive"
    elif change == "governance":
        case.client.read_source.side_effect = ValueError(
            "protected path touched"
        )
    else:
        _set_option(arguments, "--publisher-conclusion", "success")
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    assert not case.proof.exists()
    assert not case.github_output.exists()
    case.publish.assert_not_called()
    _assert_build_free(case)


def test_nuget_publication_cli_prepares_original_archive(publication_case):
    case = publication_case
    _basis(case)
    assert cli.main(_args(case, COMMANDS[1])) == 0
    marker = _record(case.marker, MutationMayHaveStartedMarker)
    assert marker.attempt == case.qualified.binding.attempt
    assert marker.publication_authorization_reference.artifact_id == 605
    assert (
        case.runtime / case.artifact_record.content.basename
    ).read_bytes() == case.qualified.result.package
    assert not (case.runtime / "command-started").exists()
    assert (
        f"mutation-marker-digest={marker.marker_digest}"
        in case.github_output.read_text()
    )
    assert "runtime-created=true" in case.github_output.read_text()
    case.client.read_source.assert_called_once()
    case.publish.assert_not_called()
    _assert_build_free(case)


def test_nuget_publication_cli_rejects_changed_archive(
    publication_case, capsys
):
    case = publication_case
    _basis(case)
    case.package.write_bytes(case.qualified.result.package + b"altered")
    assert cli.main(_args(case, COMMANDS[1])) == 1
    assert capsys.readouterr().err
    assert not case.runtime.exists()
    assert not case.marker.exists()
    assert not case.github_output.exists()
    assert not case.feed.events
    case.client.read_source.assert_not_called()
    case.publish.assert_not_called()
    _assert_build_free(case)


@pytest.mark.parametrize(
    "failure", ["marker-output", "github-output", "existing-runtime"]
)
def test_nuget_publication_cli_cleans_failed_marker_output(
    publication_case, failure, capsys
):
    case = publication_case
    _basis(case)
    arguments = _args(case, COMMANDS[1])
    if failure == "marker-output":
        case.marker.mkdir()
    elif failure == "github-output":
        case.github_output.mkdir()
    else:
        case.runtime.mkdir()
        (case.runtime / "owned-elsewhere").write_bytes(b"preserve")
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    if failure == "existing-runtime":
        assert (case.runtime / "owned-elsewhere").read_bytes() == b"preserve"
        assert not case.marker.exists()
    else:
        assert not case.runtime.exists()
    case.publish.assert_not_called()
    _assert_build_free(case)


@pytest.mark.parametrize("invocation", ["created", "conflict", "lost-response"])
def test_nuget_publication_cli_executes_durable_marker_once(
    publication_case, invocation, capsys
):
    case = publication_case
    _prepared(case)
    _invocation(case, invocation)
    arguments = _args(case, COMMANDS[2])
    assert cli.main(arguments) == (0 if invocation == "created" else 1)
    result = _record(case.result, PublicationResult)
    assert result.result == (
        "published" if invocation == "created" else "failed"
    )
    assert result.post_action_readback.classification == "exact-satisfied"
    assert result.mutation_marker_reference.to_document() == json.loads(
        case.marker_wire
    )
    assert (
        f"publication-result-digest={result.result_digest}"
        in case.github_output.read_text()
    )
    case.publish.assert_called_once()
    assert not case.runtime.exists()
    _assert_build_free(case)
    result_bytes = case.result.read_bytes()
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    case.publish.assert_called_once()
    assert case.result.read_bytes() == result_bytes


@pytest.mark.parametrize("failure", ["http-status", "transport", "resources"])
def test_nuget_publication_cli_records_discovery_failure(
    publication_case, failure, monkeypatch, capsys
):
    case = publication_case
    _prepared(case)
    original_get = case.feed.get
    claimed = []

    def discover(url, **kwargs):
        assert url == native.NUGET_SERVICE_INDEX
        claimed.append((case.runtime / "command-started").is_file())
        if failure == "http-status":
            return native.NuGetHttpResponse(url, 503, (), b"unavailable")
        if failure == "transport":
            message = "modeled discovery failure"
            raise native.NuGetTransportError(message)
        return original_get(url, **kwargs)

    if failure == "resources":
        case.feed.authority.service_resources.return_value = {
            "packageBaseAddress": "https://untrusted.example.invalid/",
            "packagePublish": RESOURCES.package_publish,
        }
    read = Mock(side_effect=discover)
    monkeypatch.setattr(case.feed, "get", read)
    arguments = _args(case, COMMANDS[2])
    assert cli.main(arguments) == 1
    result = _record(case.result, PublicationResult)
    assert result.result == "failed"
    assert result.command_classification == "not-initiated"
    assert result.mutation_classification == "not-mutated"
    assert result.post_action_readback is None
    assert result.mutation_marker_reference.to_document() == json.loads(
        case.marker_wire
    )
    assert (
        f"publication-result-digest={result.result_digest}"
        in case.github_output.read_text()
    )
    assert claimed == [True]
    read.assert_called_once()
    case.publish.assert_not_called()
    assert not case.runtime.exists()
    _assert_build_free(case)
    result_bytes = case.result.read_bytes()
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    read.assert_called_once()
    case.publish.assert_not_called()
    assert case.result.read_bytes() == result_bytes


@pytest.mark.parametrize("owner", ["different-authorization", "claimed"])
def test_nuget_publication_cli_preserves_unowned_runtime(
    publication_case, owner, monkeypatch, capsys
):
    case = publication_case
    _prepared(case)
    claim = case.runtime / "command-started"
    if owner == "claimed":
        claim.write_bytes(b"another caller owns this claim")
    else:
        document = json.loads(case.marker.read_bytes())
        reference = document["publication-authorization-reference"]
        reference["artifact-id"] += 1
        reference["artifact-url"] = (
            reference["artifact-url"].rsplit("/", 1)[0]
            + f"/{reference['artifact-id']}"
        )
        case.marker.write_bytes(canonicalize(document))
        case.marker_wire = _admit_terminal(case, case.marker, 606)
    package = case.runtime / case.artifact_record.content.basename
    package_bytes = package.read_bytes()
    read = Mock(wraps=case.feed.get)
    monkeypatch.setattr(case.feed, "get", read)
    assert cli.main(_args(case, COMMANDS[2])) == 1
    assert capsys.readouterr().err
    read.assert_not_called()
    case.publish.assert_not_called()
    assert not case.result.exists()
    assert not case.github_output.exists()
    assert package.read_bytes() == package_bytes
    if owner == "claimed":
        assert claim.read_bytes() == b"another caller owns this claim"
    else:
        assert not claim.exists()
    _assert_build_free(case)


@pytest.mark.parametrize("operation", ["inspect_package", "service_resources"])
def test_nuget_publication_cli_records_helper_timeout_before_invocation(
    publication_case, operation, capsys
):
    case = publication_case
    _prepared(case)
    reader = getattr(case.feed.authority, operation)
    reader.reset_mock()

    def timeout(*_args):
        assert (case.runtime / "command-started").is_file()
        raise TimeoutExpired(("dotnet", "modeled-helper"), 300)

    reader.side_effect = timeout
    arguments = _args(case, COMMANDS[2])
    assert cli.main(arguments) == 1
    result = _record(case.result, PublicationResult)
    assert result.result == "failed"
    assert result.command_classification == "not-initiated"
    assert result.mutation_classification == "not-mutated"
    assert result.post_action_readback is None
    assert result.response_identity is None
    assert result.mutation_marker_reference.to_document() == json.loads(
        case.marker_wire
    )
    assert result.diagnostics.entries == (
        "NuGet pre-invocation rejection: TimeoutExpired",
    )
    assert (
        f"publication-result-digest={result.result_digest}"
        in case.github_output.read_text()
    )
    reader.assert_called_once()
    case.publish.assert_not_called()
    assert not case.runtime.exists()
    _assert_build_free(case)
    result_bytes = case.result.read_bytes()
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    reader.assert_called_once()
    case.publish.assert_not_called()
    assert case.result.read_bytes() == result_bytes


@pytest.mark.parametrize(
    ("invocation", "classification", "has_response"),
    [
        ("created", "definitive-success", True),
        ("conflict", "definitive-non-success", True),
        ("lost-response", "ambiguous", False),
    ],
)
def test_nuget_publication_cli_retains_invocation_after_readback_timeout(
    publication_case, invocation, classification, has_response, capsys
):
    case = publication_case
    _prepared(case)
    _invocation(case, invocation)
    reader = case.feed.authority.normalize_identity
    reader.reset_mock()
    reader.side_effect = TimeoutExpired(("dotnet", "modeled-helper"), 300)
    arguments = _args(case, COMMANDS[2])
    assert cli.main(arguments) == 1
    result = _record(case.result, PublicationResult)
    assert result.result == "failed"
    assert result.command_classification == classification
    assert result.mutation_classification == "possibly-mutated"
    assert result.post_action_readback is None
    assert result.response_identity == (
        "sha256:" + hashlib.sha256(b"modeled-response").hexdigest()
        if has_response
        else None
    )
    assert result.mutation_marker_reference.to_document() == json.loads(
        case.marker_wire
    )
    assert result.diagnostics.entries == (
        "NuGet post-publication read failed: TimeoutExpired",
    )
    assert (
        f"publication-result-digest={result.result_digest}"
        in case.github_output.read_text()
    )
    reader.assert_called_once()
    case.publish.assert_called_once()
    assert not case.runtime.exists()
    _assert_build_free(case)
    result_bytes = case.result.read_bytes()
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    reader.assert_called_once()
    case.publish.assert_called_once()
    assert case.result.read_bytes() == result_bytes


def _wire(case, path, artifact_id):
    digest = "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
    return canonicalize(
        ArtifactReference(
            artifact_id,
            digest,
            "https://github.com/hcoona/three/actions/runs/"
            f"{case.source.intent.workflow_run_id}/artifacts/{artifact_id}",
            path.name,
            digest,
        ).to_document()
    ).decode()


@pytest.mark.parametrize(
    "invalid",
    ["absent", "result-variant", "foreign-url", "actual-run", "archive"],
)
def test_nuget_publication_cli_rejects_invalid_marker(
    publication_case, invalid, capsys
):
    case = publication_case
    _prepared(case)
    if invalid == "result-variant":
        _invocation(case, "created")
        assert cli.main(_args(case, COMMANDS[2])) == 0
        case.marker_wire = _admit_terminal(case, case.result, 607)
        case.result = case.root / "rejected-result.json"
        _reset_effects(case)
    elif invalid == "absent":
        case.marker_wire = "null"
    elif invalid == "foreign-url":
        reference = json.loads(case.marker_wire)
        reference["artifact-url"] = (
            "https://github.com/hcoona/three/actions/runs/999/artifacts/606"
        )
        case.marker_wire = canonicalize(reference).decode()
    elif invalid == "actual-run":
        document = json.loads(case.marker.read_bytes())
        document["workflow-run-id"] += 1
        case.marker.write_bytes(canonicalize(document))
        case.marker_wire = _wire(case, case.marker, 606)
    else:
        (case.runtime / case.artifact_record.content.basename).write_bytes(
            b"substituted"
        )
    assert cli.main(_args(case, COMMANDS[2])) == 1
    case.publish.assert_not_called()
    if invalid == "archive":
        result = _record(case.result, PublicationResult)
        assert result.command_classification == "not-initiated"
        assert result.mutation_classification == "not-mutated"
        assert result.result == "failed"
        assert not case.runtime.exists()
    else:
        assert capsys.readouterr().err
        assert not case.result.exists()
        assert not case.github_output.exists()
        assert not case.feed.events
    _assert_build_free(case)


@pytest.mark.parametrize(
    "terminal", ["marker", "result", "skipped", "cancelled"]
)
def test_nuget_publication_cli_admits_and_resolves_native_terminal(
    publication_case, terminal
):
    case = publication_case
    _prepared(case)
    wire, expected = case.marker_wire, {"terminal-artifact-id": "606"}
    if terminal == "result":
        _invocation(case, "created")
        assert cli.main(_args(case, COMMANDS[2])) == 0
        wire = _admit_terminal(case, case.result, 607)
        expected = {"terminal-artifact-id": "607", "marker-artifact-id": "606"}
    elif terminal in {"skipped", "cancelled"}:
        wire, expected = ("" if terminal == "skipped" else "null"), {}
    output = case.root / "resolved-output"
    arguments = [
        "release",
        "resolve-publication-terminal",
        *case.current,
        "--publisher-conclusion",
        terminal if terminal in {"skipped", "cancelled"} else "success",
        "--publication-terminal-reference",
        wire,
        "--github-output",
        str(output),
    ]
    if terminal in {"marker", "result"}:
        arguments += ["--terminal-directory", str(case.root)]
    assert cli.main(arguments) == 0
    resolved = (
        dict(line.split("=", 1) for line in output.read_text().splitlines())
        if output.exists()
        else {}
    )
    assert resolved == expected
    if terminal in {"marker", "result"}:
        assert wire == _wire(
            case, getattr(case, terminal), 606 if terminal == "marker" else 607
        )


def _evidence_args(case):
    # Cache immutable producer results only; admit fresh files per case.
    key = "publication-finalizer-evidence"
    if key not in case.preconditions:
        snapshot, result = case.qualified.snapshot, case.qualified.result
        artifact, build = form_uploaded_nuget_release_artifact(
            snapshot,
            NugetMechanicalBuildResult(
                snapshot.snapshot_digest,
                snapshot.build_requests[0].request_digest,
                result,
            ),
            _transport(snapshot),
        )
        contents = qualify_release_nuget_contents(
            snapshot, artifact, result.package, result.expectation, case.helper
        )
        consumer = qualify_release_nuget_consumer(
            snapshot,
            artifact,
            result.package,
            result.expectation,
            case.helper,
            evidence_directory=case.root / "finalizer-consumer",
        )
        evidence = (build, contents, consumer)
        assert artifact == case.artifact_record
        assert (
            finalize_qualification(snapshot, evidence, (artifact,))
            == case.decision_record
        )
        case.preconditions[key] = tuple(
            canonicalize(item.to_document()) for item in evidence
        )
    arguments = []
    for name, content, artifact_id in zip(
        ("build_evidence", "artifact_contents_evidence", "consumer_evidence"),
        case.preconditions[key],
        (701, 702, 703),
        strict=True,
    ):
        arguments += _uploaded(
            case.root, name, json.loads(content), artifact_id
        )
    case.contents.reset_mock()
    case.consumer.reset_mock()
    return arguments


def _final_args(
    case, *, publisher="skipped", step="skipped", terminal="null", exact=False
):
    arguments = [
        "release",
        "nuget",
        "finalize-live",
        *_authority_args(case),
        *_evidence_args(case),
        *_artifact_args(case),
        "--publisher-conclusion",
        publisher,
        "--publication-step-outcome",
        step,
        "--publication-terminal-reference",
        terminal,
        "--observation-conclusion",
        "success",
        "--outcome-output",
        str(case.outcome),
        "--summary-output",
        str(case.final_summary),
        "--github-step-summary",
        str(case.step_summary),
        "--github-output",
        str(case.github_output),
    ]
    for name, path, artifact_id in (
        ("observation", case.observation, 601),
        ("publication_snapshot", case.publication, 602),
    ):
        arguments += _reference_args(case, name, path, artifact_id)
    if not exact:
        for name, path, artifact_id in (
            ("approval_bundle", case.bundle, 604),
            ("reviewer_summary", case.summary, 603),
            ("publication_authorization", case.authorization, 605),
        ):
            arguments += _reference_args(case, name, path, artifact_id)
    if terminal != "null":
        arguments += ["--terminal-directory", str(case.root)]
        if json.loads(terminal)["artifact-id"] == 607:
            arguments += ["--marker-directory", str(case.root)]
    return arguments


@pytest.mark.parametrize(
    ("scenario", "expected", "mutated", "predecessor"),
    [
        ("created", "published", False, "publication-result"),
        ("conflict", "publication-failed", True, "publication-result"),
        ("lost-response", "publication-failed", True, "publication-result"),
        ("cancelled-marker", "unknown", True, "mutation-marker"),
        (
            "cancelled-before-marker",
            "failed-before-publication",
            False,
            "publication-authorization",
        ),
        ("missing-terminal", "unknown", True, "publication-authorization"),
    ],
)
def test_nuget_publication_cli_finalizes_current_terminal(
    publication_case, scenario, expected, mutated, predecessor
):
    case = publication_case
    _prepared(case)
    terminal, publisher, step = "null", "cancelled", "skipped"
    if scenario in {"created", "conflict", "lost-response"}:
        _invocation(case, scenario)
        assert cli.main(_args(case, COMMANDS[2])) == (
            0 if scenario == "created" else 1
        )
        terminal = _admit_terminal(case, case.result, 607)
        publisher = step = "success" if scenario == "created" else "failure"
    elif scenario == "cancelled-marker":
        terminal, step = case.marker_wire, "cancelled"
    elif scenario == "missing-terminal":
        step = "cancelled"
    arguments = _final_args(
        case, publisher=publisher, step=step, terminal=terminal
    )
    _reset_effects(case)
    assert cli.main(arguments) == (0 if expected == "published" else 1)
    outcome = _record(case.outcome, AttemptOutcome)
    assert outcome.disposition == expected
    assert outcome.possibly_mutated is mutated
    assert outcome.direct_predecessor.kind == predecessor
    assert outcome.direct_predecessor.reference.to_document() == json.loads(
        terminal if terminal != "null" else _wire(case, case.authorization, 605)
    )
    assert case.final_summary.read_bytes() == case.step_summary.read_bytes()
    assert f"Disposition: `{expected}`" in case.final_summary.read_text()
    assert (
        f"attempt-outcome-digest={outcome.outcome_digest}"
        in case.github_output.read_text()
    )
    assert not case.feed.events
    case.client_factory.assert_not_called()
    case.transport_factory.assert_not_called()
    case.publish.assert_not_called()
    _assert_build_free(case)


def test_nuget_publication_cli_finalizes_only_fresh_exact_proof(
    publication_case, monkeypatch
):
    case = publication_case
    _basis(case, exact=True)
    arguments = _final_args(case, exact=True)
    assert cli.main(arguments) == 1
    before = _record(case.outcome, AttemptOutcome)
    assert before.disposition == "unknown"
    assert not before.possibly_mutated
    assert before.direct_predecessor.kind == "zero-action-publication-snapshot"
    _clock(monkeypatch, NOW + timedelta(minutes=1))
    assert cli.main(_args(case, COMMANDS[0])) == 0
    arguments += _reference_args(
        case, "exact_satisfied_finalization_proof", case.proof, 608
    )
    _reset_effects(case)
    assert cli.main(arguments) == 0
    outcome = _record(case.outcome, AttemptOutcome)
    assert outcome.disposition == "exact-satisfied"
    assert not outcome.possibly_mutated
    assert (
        outcome.direct_predecessor.kind == "exact-satisfied-finalization-proof"
    )
    assert (
        outcome.direct_predecessor.reference.payload_digest
        == _record(case.proof, ExactSatisfiedFinalizationProof).proof_digest
    )
    assert not case.feed.events
    case.client_factory.assert_not_called()
    case.publish.assert_not_called()


@pytest.mark.parametrize(
    "missing", ["terminal-directory", "marker-directory", "marker-payload"]
)
def test_nuget_publication_cli_rejects_broken_terminal_lineage(
    publication_case, missing, capsys
):
    case = publication_case
    _prepared(case)
    _invocation(case, "created")
    assert cli.main(_args(case, COMMANDS[2])) == 0
    terminal = _admit_terminal(case, case.result, 607)
    arguments = _final_args(
        case, publisher="success", step="success", terminal=terminal
    )
    if missing == "marker-payload":
        case.marker.unlink()
    else:
        index = arguments.index("--" + missing)
        del arguments[index : index + 2]
    _reset_effects(case)
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    assert not case.outcome.exists()
    assert not case.github_output.exists()
    assert not case.feed.events
    case.publish.assert_not_called()


def test_nuget_publication_cli_preserves_incomplete_qualification(
    publication_case,
):
    case = publication_case
    _basis(case)
    decision = finalize_qualification(case.qualified.snapshot, (), ())
    assert decision.terminal_result == "incomplete"
    case.decision.write_bytes(canonicalize(decision.to_document()))
    arguments = [
        "release",
        "nuget",
        "finalize-live",
        *_authority_args(case),
        "--publisher-conclusion",
        "skipped",
        "--publication-terminal-reference",
        "null",
        "--outcome-output",
        str(case.outcome),
        "--summary-output",
        str(case.final_summary),
        "--github-output",
        str(case.github_output),
    ]
    _reset_effects(case)
    assert cli.main(arguments) == 1
    assert not case.outcome.exists()
    assert not case.final_summary.exists()
    assert not case.github_output.exists()
    assert not case.feed.events
    case.publish.assert_not_called()


OPTIONAL_RECORDS = (
    "build-evidence",
    "artifact-contents-evidence",
    "consumer-evidence",
    "release-artifact",
    "observation",
    "publication-snapshot",
    "approval-bundle",
    "publication-authorization",
    "exact-satisfied-finalization-proof",
    "reviewer-summary",
)


@pytest.mark.parametrize("record", OPTIONAL_RECORDS)
def test_nuget_publication_cli_rejects_partial_finalization_transport(
    publication_case, record, capsys
):
    case = publication_case
    _basis(case)
    arguments = _final_args(case)
    option = "--" + record
    if option in arguments:
        index = arguments.index(option)
        del arguments[index : index + 2]
    else:
        arguments += [option + "-artifact-id", "608"]
    _reset_effects(case)
    assert cli.main(arguments) == 1
    assert "partial" in capsys.readouterr().err
    assert not case.outcome.exists()
    assert not case.final_summary.exists()
    assert not case.github_output.exists()
    assert not case.feed.events
    case.publish.assert_not_called()


def _current_command(case, command):
    if command == COMMANDS[0]:
        _basis(case, exact=True)
    elif command == COMMANDS[2]:
        _prepared(case)
    else:
        _basis(case)
    arguments = (
        _final_args(case) if command == COMMANDS[3] else _args(case, command)
    )
    _reset_effects(case)
    return arguments


@pytest.mark.parametrize("command", COMMANDS)
def test_nuget_publication_cli_rejects_reruns(
    publication_case, command, capsys
):
    case = publication_case
    arguments = _current_command(case, command)
    _set_option(arguments, "--run-attempt", 2)
    assert cli.main(arguments) == 1
    assert "rejects GitHub reruns" in capsys.readouterr().err
    output = "--outcome-output" if command == "finalize-live" else "--output"
    assert not Path(arguments[arguments.index(output) + 1]).exists()
    assert not case.github_output.exists()
    assert not case.feed.events
    case.client_factory.assert_not_called()
    case.publish.assert_not_called()
    _assert_build_free(case)


@pytest.mark.parametrize(
    ("command", "option"),
    [
        (COMMANDS[0], "--control"),
        (COMMANDS[1], "--intent-artifact-digest"),
        (COMMANDS[1], "--approval-bundle-artifact-url"),
        (COMMANDS[2], "--workflow-run-id"),
        (COMMANDS[2], "--publication-authorization-digest"),
        (COMMANDS[3], "--repository-model-digest"),
        (COMMANDS[3], "--attempt-binding-digest"),
        (COMMANDS[3], "--live-eligibility-payload-digest"),
    ],
)
def test_nuget_publication_cli_rejects_substituted_authority(
    publication_case, command, option, capsys
):
    case = publication_case
    arguments = _current_command(case, command)
    value = "sha256:" + "f" * 64
    if option == "--control":
        value = "f" * 40
    elif option == "--workflow-run-id":
        value = str(case.source.intent.workflow_run_id + 1)
    elif option.endswith("-url"):
        value = "https://github.com/hcoona/three/actions/runs/999/artifacts/604"
    _set_option(arguments, option, value)
    assert cli.main(arguments) == 1
    assert capsys.readouterr().err
    assert not case.github_output.exists()
    output = "--outcome-output" if command == "finalize-live" else "--output"
    assert not Path(arguments[arguments.index(output) + 1]).exists()
    assert not case.feed.events
    case.client_factory.assert_not_called()
    case.publish.assert_not_called()
    _assert_build_free(case)
