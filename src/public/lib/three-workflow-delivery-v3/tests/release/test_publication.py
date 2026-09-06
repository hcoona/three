"""Publisher preparation, durable-marker boundary, and one-shot readback."""

# ruff: noqa: D103, PLR2004

from __future__ import annotations

import hashlib
import io
import json
import os
import shutil
import tarfile
from dataclasses import replace
from datetime import timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters.github_packages import (
    GitHubPackagesTimeoutError,
    _validate_local_tarball_preconditions,
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
    NpmProcessOutcome,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    ApprovalBundle,
    MutationMayHaveStartedMarker,
    PublicationResult,
    PublicationSnapshot,
    admit_release_record,
    release_record_digest,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.release import publication
from three_workflow_delivery_v3.release.eligibility import (
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.release.live import (
    form_publication_authorization,
)

from ..adapters.test_github_packages_active_state import (
    CONTROL_URL,
    TAGS_URL,
    TARBALL_URL,
    _control,
    _response,
)
from ..adapters.test_npmjs import _make_tarball
from .observation_fixtures import (
    active_transport,
    publication_authority_arguments,
)
from .test_eligibility import RecordingGovernanceClient
from .test_observation_admission import NOW, _observation, _parsed
from .test_observation_admission import (
    observation_case as observation_case,  # noqa: PLC0414
)

TOKEN = "publisher-current-repository-secret"  # noqa: S105


def _reference(record, name, artifact_id):
    digest = release_record_digest(record)
    return ArtifactReference(
        artifact_id=artifact_id,
        artifact_digest=digest,
        artifact_url=(
            "https://github.com/hcoona/three/actions/runs/"
            f"{record.workflow_run_id}/artifacts/{artifact_id}"
        ),
        payload_path=name,
        payload_digest=digest,
    )


def _load(path, record_type):
    content = path.read_bytes()
    return admit_release_record(
        content,
        expected_type=record_type,
        expected_digest="sha256:" + hashlib.sha256(content).hexdigest(),
    )


class ControlledNpm:
    """Model official configuration queries, never a registry protocol."""

    def __init__(self):
        """Select success unless a scenario explicitly changes the outcome."""
        self.calls = []
        self.overrides = {}
        self.outcome = NpmProcessOutcome("definitive-success", returncode=0)
        self.after_publish = None

    @property
    def publications(self):
        """Return only mutating process invocations."""
        return [
            call for call in self.calls if call[0][:2] == ("npm", "publish")
        ]

    def run(self, argv, *, cwd, environment, timeout, output_limit):
        """Record the exact process boundary and return controlled facts."""
        self.calls.append((argv, cwd, dict(environment), timeout, output_limit))
        if argv[:2] == ("npm", "publish"):
            if self.after_publish is not None:
                self.after_publish()
            if isinstance(self.outcome, BaseException):
                raise self.outcome
            return self.outcome
        profile = github_packages_destination_operation_profile()
        if argv == ("node", "--version"):
            value = self.overrides.get("node", "v" + profile.node_version)
        elif argv == ("npm", "--version"):
            value = self.overrides.get("npm", profile.npm_version)
        else:
            assert argv[:3] == ("npm", "config", "get")
            tag = argv[argv.index("--tag") + 1]
            values = {
                "@hcoona:registry": profile.registry,
                "registry": profile.registry + "/",
                "tag": tag,
                "ignore-scripts": "true",
                "fetch-retries": "0",
                "access": "null",
                "userconfig": environment["NPM_CONFIG_USERCONFIG"],
                "globalconfig": environment["NPM_CONFIG_GLOBALCONFIG"],
            }
            value = self.overrides.get(argv[3], values[argv[3]])
        return NpmProcessOutcome(
            "definitive-success", (value + "\n").encode(), returncode=0
        )


@pytest.fixture
def publisher(observation_case, tmp_path, monkeypatch):
    case = observation_case
    authority_dir = tmp_path / "authority"
    publication_authority_arguments(
        authority_dir, case, monkeypatch, classification="absent"
    )
    snapshot = _load(
        authority_dir / "publication-snapshot.json", PublicationSnapshot
    )
    bundle = _load(authority_dir / "approval-bundle.json", ApprovalBundle)
    bundle_reference = _reference(bundle, "approval-bundle.json", 111)
    initial = case.eligibility.governance
    governance = require_fresh_governance_identity(
        case.policy.governance,
        RecordingGovernanceClient(
            canonicalize(initial.attestation.to_document())
        ),
        now=NOW,
        expected_provenance=initial.provenance,
        expected_canonical_content_digest=initial.canonical_content_digest,
        expected_expires_at=initial.attestation.expires_at.isoformat().replace(
            "+00:00", "Z"
        ),
        expected_live_enabled=True,
    )
    authorization = _parsed(
        form_publication_authorization(
            approval_bundle=bundle,
            approval_bundle_reference=bundle_reference,
            approval_boundary_sentinel_result="success",
            governance=governance,
            destination_operation_profile_digest=(
                github_packages_destination_operation_profile().profile_digest
            ),
            completed_at=(NOW + timedelta(seconds=1))
            .isoformat()
            .replace("+00:00", "Z"),
            control=case.eligibility.context.control,
        )
    )
    inputs = publication.PublicationInputs(
        **case.arguments(),
        observation=_observation(case, classification="absent"),
        publication_snapshot=snapshot,
        publication_snapshot_reference=bundle.publication_snapshot_reference,
        approval_bundle=bundle,
        approval_bundle_reference=bundle_reference,
        reviewer_summary=(authority_dir / "reviewer-summary.md").read_bytes(),
        reviewer_summary_reference=bundle.reviewer_summary_reference,
        authorization=authorization,
        authorization_reference=_reference(
            authorization, "authorization.json", 113
        ),
    )
    checkout = tmp_path / "target-checkout"
    checkout.mkdir()
    (checkout / ".npmrc").write_text(
        "registry=https://untrusted.invalid\n"
        "@hcoona:registry=https://untrusted.invalid\nignore-scripts=false\n"
    )
    toolchain = tmp_path / "trusted-toolchain"
    toolchain.mkdir()
    tarball = checkout / case.artifact.content.basename
    tarball.write_bytes(case.tarball)
    common = {
        "current": ReleaseAdmissionBindings(
            purpose="live-release",
            workflow_run_id=case.intent.workflow_run_id,
            run_attempt=None,
            target=case.intent.target,
        ),
        "run_attempt": 1,
        "runtime_directory": tmp_path / "publisher-runtime",
        "toolchain_directory": toolchain,
        "checkout": checkout,
        "expectation": case.expectation,
        "runner": ControlledNpm(),
        "transport": active_transport(case),
        "token": TOKEN,
        "clock": lambda: NOW + timedelta(seconds=2),
    }
    return (
        inputs,
        common,
        {
            "tarball": tarball,
            "governance_client": RecordingGovernanceClient(
                canonicalize(
                    case.eligibility.governance.attestation.to_document()
                )
            ),
        },
    )


def _prepare(publisher):
    inputs, common, preparation = publisher
    marker = publication.prepare_publication(inputs, **common, **preparation)
    # The pure core round-trips canonical payloads. It does not fabricate a
    # platform upload proof; native artifact-service admission is integration.
    reference = _reference(marker, "mutation-marker.json", 114)
    admitted = admit_release_record(
        canonicalize(marker.to_document()),
        expected_type=MutationMayHaveStartedMarker,
        expected_digest=reference.payload_digest,
        expected_bindings=common["current"],
    )
    return admitted, reference


def _execute(publisher, marker=None, reference=None):
    inputs, common, _ = publisher
    if marker is None:
        marker, reference = _prepare(publisher)
    return publication.execute_publication(
        inputs,
        **common,
        durable_marker=marker,
        marker_reference=reference,
    )


def test_preparation_queries_actual_profile_and_retains_isolated_files(
    publisher, monkeypatch
):
    inputs, common, _ = publisher
    monkeypatch.setenv("NPM_CONFIG_REGISTRY", "https://untrusted.invalid")
    monkeypatch.setenv("NODE_OPTIONS", "--import=target-hook.mjs")
    monkeypatch.setenv("NPM_TOKEN", "unrelated-pat")
    marker, _ = _prepare(publisher)

    runner = common["runner"]
    assert not runner.publications
    assert marker.publication_authorization_reference == (
        inputs.authorization_reference
    )
    assert marker.profile_match.node_version == "24.19.0"
    assert marker.profile_match.npm_version == "11.17.0"
    assert marker.package_control_proof.observed_at == (
        NOW + timedelta(seconds=2)
    ).isoformat().replace("+00:00", "Z")
    assert common["runtime_directory"].exists()
    assert (common["runtime_directory"] / "user.npmrc").stat().st_mode & (
        0o777
    ) == 0o600
    assert all(call[1] == common["runtime_directory"] for call in runner.calls)
    assert all(call[2]["GITHUB_TOKEN"] == TOKEN for call in runner.calls)
    assert all(
        not {"NODE_OPTIONS", "NPM_TOKEN", "NPM_CONFIG_REGISTRY"}
        & call[2].keys()
        for call in runner.calls
    )
    assert all(
        call[2]["NPM_CONFIG_USERCONFIG"]
        == str(common["runtime_directory"] / "user.npmrc")
        for call in runner.calls
    )
    for argv, *_ in runner.calls:
        if argv[:3] == ("npm", "config", "get"):
            assert argv[4:] == marker.profile_match.command[3:]
    assert TOKEN.encode() not in canonicalize(marker.to_document())
    _execute(publisher, marker, _reference(marker, "mutation-marker.json", 114))
    assert not common["runtime_directory"].exists()


def test_official_npm_config_queries_ignore_target_and_ambient_config(
    publisher, monkeypatch
):
    node = shutil.which("node")
    npm = shutil.which("npm")
    if node is None or npm is None:
        pytest.skip("Nonmutating integration requires installed Node/npm")
    toolchain = Path(node).parent
    if Path(npm).parent != toolchain:
        pytest.skip("Nonmutating integration requires one pinned toolchain")
    _, common, _ = publisher
    runner = IsolatedNpmProcessRunner()
    marker, _ = _prepare(publisher)
    monkeypatch.setenv("NODE_OPTIONS", "--require=/nonexistent/target-hook")
    monkeypatch.setenv("npm_config_registry", "https://untrusted.invalid")
    monkeypatch.setenv("npm_config_userconfig", "/nonexistent/target-config")
    try:
        effective = dict(marker.profile_match.configuration)
        # Exercise the installed official parser, not native publish acceptance.
        # Producer tests separately exercise pinned-version admission.
        for argv, cwd, environment, timeout, output_limit in common[
            "runner"
        ].calls:
            if argv[:3] == ("npm", "config", "get"):
                expected = effective[argv[3]]
            else:
                continue
            observed = runner.run(
                argv,
                cwd=cwd,
                environment={
                    **environment,
                    "PATH": os.pathsep.join((str(toolchain), os.defpath)),
                },
                timeout=timeout,
                output_limit=output_limit,
            )
            assert observed.classification == "definitive-success", (
                argv,
                observed.output.decode().replace(TOKEN, "[redacted]"),
            )
            assert observed.output.decode().strip() == expected, argv
    finally:
        if common["runtime_directory"].exists():
            shutil.rmtree(common["runtime_directory"])


@pytest.mark.parametrize("wrong", ["run", "attempt", "target"])
def test_current_platform_binding_rejects_before_io(publisher, wrong):
    inputs, common, preparation = publisher
    if wrong == "attempt":
        common["run_attempt"] = 2
    elif wrong == "run":
        common["current"] = replace(common["current"], workflow_run_id=991122)
    else:
        common["current"] = replace(common["current"], target="f" * 40)
    with pytest.raises(ValueError, match="current attempt-one"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not common["runner"].calls
    assert not common["transport"].requests
    assert not preparation["governance_client"].calls


@pytest.mark.parametrize(
    "observation_case",
    [NOW - timedelta(days=90) + timedelta(seconds=3)],
    indirect=True,
)
def test_final_clock_rejects_native_acceptance_expiring_during_queries(
    publisher, monkeypatch
):
    inputs, common, preparation = publisher
    now = [NOW + timedelta(seconds=2)]
    common["clock"] = lambda: now[0]
    runner = common["runner"]
    original = runner.run

    def advance_after_query(*args, **kwargs):
        result = original(*args, **kwargs)
        now[0] = NOW + timedelta(seconds=4)
        return result

    monkeypatch.setattr(runner, "run", advance_after_query)
    with pytest.raises(ValueError, match="unexpired native acceptance"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not runner.publications
    assert not common["runtime_directory"].exists()


@pytest.mark.parametrize(
    "observation_case",
    [NOW - timedelta(days=90) + timedelta(seconds=3)],
    indirect=True,
)
def test_execution_query_expiry_returns_not_mutated_result(
    publisher, monkeypatch
):
    _, common, _ = publisher
    now = [NOW + timedelta(seconds=2)]
    common["clock"] = lambda: now[0]
    marker, reference = _prepare(publisher)
    runner = common["runner"]
    original = runner.run

    def advance_after_query(*args, **kwargs):
        result = original(*args, **kwargs)
        now[0] = NOW + timedelta(seconds=4)
        return result

    monkeypatch.setattr(runner, "run", advance_after_query)
    result = _execute(publisher, marker, reference)
    assert result.result == "failed"
    assert result.command_classification == "not-initiated"
    assert result.mutation_classification == "not-mutated"
    assert result.mutation_marker_reference == reference
    assert result.post_action_readback is None
    assert result.response_identity is None
    assert _parsed(result) == result
    assert not runner.publications
    assert not common["runtime_directory"].exists()


def test_marker_upload_delay_cannot_outlive_governance(publisher):
    inputs, common, _ = publisher
    marker, reference = _prepare(publisher)
    common["clock"] = lambda: (
        inputs.eligibility.governance.attestation.expires_at
    )
    with pytest.raises(ValueError, match="currently fresh Live Eligibility"):
        _execute(publisher, marker, reference)
    assert not common["runner"].publications


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("node", "v24.18.0"),
        ("npm", "11.16.0"),
        ("registry", "https://untrusted.invalid"),
        ("@hcoona:registry", "https://untrusted.invalid"),
        ("ignore-scripts", "false"),
        ("fetch-retries", "2"),
        ("access", "public"),
        ("tag", "latest"),
    ],
)
def test_profile_mismatch_prevents_marker_and_cleans_files(
    publisher, key, value
):
    inputs, common, preparation = publisher
    common["runner"].overrides[key] = value
    with pytest.raises(ValueError, match="Publisher"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not common["runner"].publications
    assert not common["runtime_directory"].exists()


@pytest.mark.parametrize(
    "change",
    ["authorization-transport", "bundle-transport", "reviewer", "observation"],
)
def test_invalid_authority_blocks_before_io(publisher, change):
    inputs, common, preparation = publisher
    if change == "authorization-transport":
        inputs = replace(
            inputs,
            authorization_reference=replace(
                inputs.authorization_reference,
                payload_digest="sha256:" + "f" * 64,
            ),
        )
    elif change == "bundle-transport":
        inputs = replace(
            inputs,
            approval_bundle_reference=replace(
                inputs.approval_bundle_reference, artifact_id=9876
            ),
        )
    elif change == "reviewer":
        inputs = replace(inputs, reviewer_summary=b"different reviewed content")
    else:
        inputs = replace(
            inputs,
            observation=replace(
                inputs.observation,
                qualification_decision_reference=replace(
                    inputs.decision_reference, artifact_id=9876
                ),
            ),
        )
    with pytest.raises(ValueError, match=r"mismatch|differs"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not common["runner"].calls
    assert not common["transport"].requests
    assert not common["runtime_directory"].exists()


@pytest.mark.parametrize("failure", ["governance", "package-control"])
def test_fresh_governance_and_package_control_precede_marker(
    publisher, failure
):
    inputs, common, preparation = publisher
    client = preparation["governance_client"]
    if failure == "governance":
        client.protected = False
    else:
        common["transport"].responses[CONTROL_URL] = _response(
            CONTROL_URL, _control(visibility="private")
        )
    with pytest.raises(ValueError, match=r"package control|Governance"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not common["runner"].publications
    assert not common["runtime_directory"].exists()


@pytest.mark.parametrize("invalid", ["absent", "digest", "authorization"])
def test_invalid_or_unpersisted_marker_forbids_mutation(publisher, invalid):
    inputs, common, _ = publisher
    marker, reference = _prepare(publisher)
    calls_before = len(common["runner"].calls)
    if invalid == "absent":
        reference = None
    elif invalid == "digest":
        reference = replace(reference, payload_digest="sha256:" + "f" * 64)
    else:
        marker = replace(
            marker,
            publication_authorization_reference=replace(
                inputs.authorization_reference, artifact_id=9988
            ),
        )
        reference = _reference(marker, "mutation-marker.json", 115)
    with pytest.raises(ValueError, match=r"marker|digest"):
        publication.execute_publication(
            inputs,
            **common,
            durable_marker=marker,
            marker_reference=reference,
        )
    assert len(common["runner"].calls) == calls_before
    assert not common["runner"].publications


@pytest.mark.parametrize(
    "changed", ["tarball", "config", "toolchain", "effective-config"]
)
def test_execution_revalidates_prepared_lifecycle(publisher, changed):
    inputs, common, _ = publisher
    marker, reference = _prepare(publisher)
    if changed == "tarball":
        (
            common["runtime_directory"] / inputs.artifact.content.basename
        ).write_bytes(b"changed qualified bytes")
    elif changed == "config":
        (common["runtime_directory"] / "user.npmrc").write_text(
            "registry=https://different.invalid\n"
        )
    elif changed == "toolchain":
        common["runner"].overrides["node"] = "v24.20.0"
    else:
        common["runner"].overrides["registry"] = "https://different.invalid"
    if changed == "config":
        with pytest.raises(ValueError, match="configuration changed"):
            _execute(publisher, marker, reference)
        assert common["runtime_directory"].is_dir()
        assert not (common["runtime_directory"] / "command-started").exists()
    else:
        result = _execute(publisher, marker, reference)
        assert result.result == "failed"
        assert result.command_classification == "not-initiated"
        assert result.mutation_classification == "not-mutated"
        assert result.mutation_marker_reference == reference
        assert result.post_action_readback is None
        assert result.response_identity is None
        assert _parsed(result) == result
        assert not common["runtime_directory"].exists()
    assert not common["runner"].publications


def test_controlled_profile_query_failure_returns_not_mutated_result(
    publisher, monkeypatch
):
    _, common, _ = publisher
    marker, reference = _prepare(publisher)
    original = common["runner"].run

    def fail_query(*args, **kwargs):
        original(*args, **kwargs)
        return NpmProcessOutcome(
            "definitive-non-success",
            f"query diagnostic: {TOKEN}".encode(),
            truncated=True,
            returncode=1,
        )

    monkeypatch.setattr(common["runner"], "run", fail_query)
    result = _execute(publisher, marker, reference)
    assert result.result == "failed"
    assert result.command_classification == "not-initiated"
    assert result.mutation_classification == "not-mutated"
    assert result.mutation_marker_reference == reference
    assert result.post_action_readback is None
    assert result.response_identity is None
    assert result.diagnostics.entries
    assert TOKEN.encode() not in canonicalize(result.to_document())
    assert _parsed(result) == result
    assert not common["runner"].publications
    assert not common["runtime_directory"].exists()


def test_prepared_profile_binding_mismatch_does_not_fabricate_result(
    publisher,
):
    _, common, _ = publisher
    marker, _ = _prepare(publisher)
    marker = replace(
        marker,
        profile_match=replace(marker.profile_match, npm_version="11.16.0"),
    )
    reference = _reference(marker, "mutation-marker.json", 115)
    with pytest.raises(ValueError, match="differs from the prepared profile"):
        _execute(publisher, marker, reference)
    assert not common["runner"].publications


@pytest.mark.parametrize("tag", ["absent", "changed", "unreadable"])
def test_success_requires_actual_bytes_but_ignores_tag_races(publisher, tag):
    inputs, common, _ = publisher
    now = [NOW + timedelta(seconds=2)]
    common["clock"] = lambda: now[0]
    transport = common["transport"]
    exact_url = TAGS_URL + "/" + common["expectation"].npm_package_version
    exact_response = transport.responses[exact_url]
    transport.responses[exact_url] = _response(exact_url, status=404)
    if tag == "changed":
        common["transport"].responses[TAGS_URL] = _response(
            TAGS_URL,
            {
                "name": common["expectation"].package_name,
                "dist-tags": {
                    inputs.publication_snapshot.materialized_actions[
                        0
                    ].tag: "a-racing-version"
                },
            },
        )
    elif tag == "unreadable":
        common["transport"].responses[TAGS_URL] = GitHubPackagesTimeoutError(
            "unreadable tag"
        )
    marker, reference = _prepare(publisher)
    requests_before = len(transport.requests)

    def publish_exact_version():
        assert len(transport.requests) == requests_before
        transport.responses[exact_url] = exact_response
        now[0] = NOW + timedelta(seconds=3)

    common["runner"].after_publish = publish_exact_version
    result = _execute(publisher, marker, reference)
    assert result.result == "published"
    assert result.mutation_classification == "mutated"
    assert result.mutation_marker_reference == reference
    assert result.post_action_readback.observed_at == (
        NOW + timedelta(seconds=3)
    ).isoformat().replace("+00:00", "Z")
    post_action_urls = [url for url, *_ in transport.requests[requests_before:]]
    assert exact_url in post_action_urls
    assert TARBALL_URL in post_action_urls
    assert result.post_action_readback.content_sha256 == (
        inputs.artifact.content.content_sha256
    )
    assert (
        result.post_action_readback.witness_digest
        == inputs.artifact.witness_digest
    )
    assert (
        result.post_action_readback.tag_state
        == {
            "absent": "absent",
            "changed": "present",
            "unreadable": "unreadable",
        }[tag]
    )
    assert len(common["runner"].publications) == 1
    argv = common["runner"].publications[0][0]
    assert argv == (
        "npm",
        "publish",
        str(common["runtime_directory"] / inputs.artifact.content.basename),
        "--registry",
        "https://npm.pkg.github.com",
        "--tag",
        inputs.publication_snapshot.materialized_actions[0].tag,
        "--ignore-scripts",
        "--fetch-retries=0",
    )
    assert _parsed(result) == result
    assert not common["runtime_directory"].exists()


@pytest.mark.parametrize(
    ("outcome", "mutation"),
    [
        (
            NpmProcessOutcome("definitive-non-success", returncode=1),
            "possibly-mutated",
        ),
        (NpmProcessOutcome("ambiguous"), "possibly-mutated"),
        (NpmProcessOutcome("not-initiated"), "not-mutated"),
    ],
)
def test_non_success_exact_remote_state_remains_failed_without_retry(
    publisher, outcome, mutation
):
    _, common, _ = publisher
    common["runner"].outcome = outcome
    marker, reference = _prepare(publisher)
    result = _execute(publisher, marker, reference)
    assert result.result == "failed"
    assert result.command_classification == outcome.classification
    assert result.mutation_classification == mutation
    assert result.mutation_marker_reference == reference
    assert result.post_action_readback.classification == "exact-satisfied"
    assert len(common["runner"].publications) == 1
    assert _parsed(result) == result


@pytest.mark.parametrize(
    "readback", ["different-bytes", "different-witness", "unknown"]
)
def test_success_cannot_substitute_local_expectations_for_readback(
    publisher, readback, observation_case
):
    _, common, _ = publisher
    marker, reference = _prepare(publisher)
    if readback == "unknown":
        response = GitHubPackagesTimeoutError("no authoritative readback")
    else:
        with tarfile.open(
            fileobj=io.BytesIO(observation_case.tarball), mode="r:gz"
        ) as archive:
            entries = {
                member.name: archive.extractfile(member).read()
                for member in archive.getmembers()
            }
        if readback == "different-bytes":
            entries["package/dist/index.js"] = b"export const changed = true;"
        else:
            witness_path = "package/workflow-delivery/provenance.json"
            witness = json.loads(entries[witness_path])
            witness["target"] = "f" * 40
            entries[witness_path] = canonicalize(witness)
        response = _response(TARBALL_URL, body=_make_tarball(entries))
    common["transport"].responses[TARBALL_URL] = response
    result = _execute(publisher, marker, reference)
    assert result.result == "failed"
    assert result.command_classification == "definitive-success"
    assert result.mutation_classification == "possibly-mutated"
    assert result.post_action_readback.classification != "exact-satisfied"
    assert len(common["runner"].publications) == 1


def test_uncontrolled_failure_leaves_marker_not_a_fabricated_result(publisher):
    _, common, _ = publisher
    marker, reference = _prepare(publisher)
    common["runner"].outcome = RuntimeError("uncontrolled runner termination")
    with pytest.raises(RuntimeError, match="uncontrolled"):
        _execute(publisher, marker, reference)
    assert len(common["runner"].publications) == 1
    assert not common["runtime_directory"].exists()


def test_command_diagnostics_never_serialize_credentials(publisher):
    _, common, _ = publisher
    common["runner"].outcome = NpmProcessOutcome(
        "definitive-non-success",
        (
            f"Authorization: Bearer {TOKEN}\n"
            f"//npm.pkg.github.com/:_authToken={TOKEN}\n"
        ).encode(),
        truncated=True,
        returncode=1,
    )
    result = _execute(publisher)
    assert result.diagnostics.truncated
    assert result.diagnostics.entries == (
        "npm process: definitive-non-success",
    )
    assert TOKEN.encode() not in canonicalize(result.to_document())
    assert TOKEN not in repr(common["runner"].outcome)


def test_same_prepared_lifecycle_cannot_invoke_a_second_time(publisher):
    _, common, _ = publisher
    marker, reference = _prepare(publisher)
    first = _execute(publisher, marker, reference)
    assert isinstance(first, PublicationResult)
    with pytest.raises(FileNotFoundError):
        _execute(publisher, marker, reference)
    assert len(common["runner"].publications) == 1


def test_competing_execution_cannot_remove_inflight_prepared_files(publisher):
    inputs, common, _ = publisher
    marker, reference = _prepare(publisher)

    def attempt_second_execution():
        with pytest.raises(FileExistsError):
            _execute(publisher, marker, reference)
        assert (
            common["runtime_directory"] / inputs.artifact.content.basename
        ).is_file()

    common["runner"].after_publish = attempt_second_execution
    assert _execute(publisher, marker, reference).result == "published"
    assert len(common["runner"].publications) == 1
    assert not common["runtime_directory"].exists()


def test_local_tarball_admission_enforces_its_expansion_bound(publisher):
    inputs, common, preparation = publisher
    with pytest.raises(ValueError, match="tarball"):
        _validate_local_tarball_preconditions(
            tarball=preparation["tarball"],
            artifact=inputs.artifact,
            expectation=common["expectation"],
            expanded_tarball_limit_bytes=1,
        )
    assert not common["runner"].calls


@pytest.mark.parametrize(
    "observation_case",
    [{"publishConfig": {"registry": "https://untrusted.invalid"}}],
    indirect=True,
)
def test_packed_publish_config_is_not_an_extra_operand_source(publisher):
    inputs, common, preparation = publisher
    with pytest.raises(ValueError, match="packed publishConfig"):
        publication.prepare_publication(inputs, **common, **preparation)
    assert not common["runner"].calls
    assert not common["runtime_directory"].exists()
