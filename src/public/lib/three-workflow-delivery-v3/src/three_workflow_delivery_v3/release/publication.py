"""Profile-bound one-shot publication with externally owned persistence."""

# ruff: noqa: EM101, TRY003

from __future__ import annotations

import hashlib
import os
import shutil
import stat
from contextlib import ExitStack
from dataclasses import dataclass, replace
from datetime import datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.github_packages import (
    DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    GITHUB_PACKAGES_PUBLISHER_PRODUCER,
    _validate_local_tarball_preconditions,
    github_packages_destination_operation_profile,
    read_github_packages_active_state,
    validate_github_packages_publication_action,
)
from three_workflow_delivery_v3.adapters.node import (
    _load_packed_manifest,
    _read_tarball,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    GovernanceProof,
    MutationMayHaveStartedMarker,
    ProfileMatchEvidence,
    PublicationDiagnostics,
    PublicationResult,
    admit_release_record,
)
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceFreshnessRejectionError,
    governance_observation_provenance,
    require_action_governance,
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.release.finalizer import (
    materialize_publication_snapshot,
)
from three_workflow_delivery_v3.release.live import (
    validate_approval_bundle_closure,
)
from three_workflow_delivery_v3.release.observation import (
    classify_package_control,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.github_packages import (
        GitHubPackagesTransport,
    )
    from three_workflow_delivery_v3.adapters.node import ArtifactExpectation
    from three_workflow_delivery_v3.adapters.npm_process import (
        CommandClassification,
        NpmProcessRunner,
    )
    from three_workflow_delivery_v3.records.release import (
        ApprovalBundle,
        DestinationReadback,
        PublicationAuthorization,
        PublicationSnapshot,
        QualificationDecision,
        QualificationSnapshot,
        ReleaseArtifact,
        ReleaseAttemptBinding,
        ReleaseIntent,
        RemoteStateObservation,
    )
    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )
    from three_workflow_delivery_v3.release.eligibility import (
        AdmittedLiveEligibilityDecision,
        GovernanceSourceClient,
    )
    from three_workflow_delivery_v3.repository.descriptors import ReleasePolicy

_QUERY_TIMEOUT = 20.0
_PUBLISH_TIMEOUT = 120.0
_OUTPUT_LIMIT = 4096
_PRIVATE_DIRECTORY_MODE = 0o700
_PRIVATE_FILE_MODE = 0o600
_USER_CONFIG = (
    "@hcoona:registry=https://npm.pkg.github.com\n"
    "//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}\n"
)
_LOCAL_MANIFEST = b'{"private":true}\n'


class _ProfileRejectionError(ValueError):
    """Controlled local artifact or nonmutating profile-query rejection."""


@dataclass(frozen=True, slots=True)
class PublicationInputs:
    """Already transported canonical inputs, not another authority record."""

    intent: ReleaseIntent
    attempt_binding: ReleaseAttemptBinding
    eligibility: AdmittedLiveEligibilityDecision
    policy: ReleasePolicy
    snapshot: QualificationSnapshot
    decision: QualificationDecision
    decision_reference: ArtifactReference
    artifact: ReleaseArtifact
    observation: RemoteStateObservation
    publication_snapshot: PublicationSnapshot
    publication_snapshot_reference: ArtifactReference
    approval_bundle: ApprovalBundle
    approval_bundle_reference: ArtifactReference
    reviewer_summary: bytes
    reviewer_summary_reference: ArtifactReference
    authorization: PublicationAuthorization
    authorization_reference: ArtifactReference

    def validate(
        self,
        *,
        current: ReleaseAdmissionBindings,
        run_attempt: int,
        now: datetime,
    ) -> None:
        """Close actual payloads and exact references before publisher IO."""
        if (
            type(run_attempt) is not int
            or run_attempt != 1
            or current.purpose != "live-release"
            or current.workflow_run_id != self.intent.workflow_run_id
            or current.target != self.intent.target
        ):
            raise ValueError("Publisher requires the current attempt-one run")
        profile = github_packages_destination_operation_profile()
        publication = materialize_publication_snapshot(
            self.snapshot,
            self.decision,
            (self.observation,),
            (self.artifact,),
            intent=self.intent,
            attempt_binding=self.attempt_binding,
            eligibility=self.eligibility,
            policy=self.policy,
            decision_reference=self.decision_reference,
            action_creation_at=now,
            destination_operation_profile=profile,
        )
        if (
            publication != self.publication_snapshot
            or len(publication.materialized_actions) != 1
        ):
            raise ValueError(
                "Publisher requires the exact action-bearing Snapshot"
            )
        validate_approval_bundle_closure(
            approval_bundle=self.approval_bundle,
            intent=self.intent,
            attempt_binding=self.attempt_binding,
            qualification_decision=self.decision,
            qualification_snapshot=self.snapshot,
            release_artifact=self.artifact,
            destination_operation_profile=profile,
            publication_snapshot=publication,
            publication_snapshot_reference=self.publication_snapshot_reference,
            reviewer_summary_reference=self.reviewer_summary_reference,
            control=self.eligibility.context.control,
        )
        validate_github_packages_publication_action(
            action=publication.materialized_actions[0],
            projection=self.snapshot.destination_projections[0],
            artifact=self.artifact,
        )
        for record, reference in (
            (self.authorization, self.authorization_reference),
            (self.approval_bundle, self.approval_bundle_reference),
            (publication, self.publication_snapshot_reference),
        ):
            admit_release_record(
                canonicalize(record.to_document()),
                expected=record,
                expected_digest=reference.payload_digest,
                expected_bindings=current,
            )
        initial = self.eligibility.governance
        if (
            self.authorization.approval_bundle_reference
            != self.approval_bundle_reference
            or self.authorization.attempt != publication.attempt
            or self.authorization.governance_proof.provenance
            != initial.provenance
            or datetime.fromisoformat(
                self.authorization.governance_proof.expires_at
            )
            != initial.attestation.expires_at
            or not initial.observed_at
            <= datetime.fromisoformat(
                self.authorization.governance_proof.observed_at
            )
            <= datetime.fromisoformat(self.authorization.completed_at)
            <= now
            or "sha256:" + hashlib.sha256(self.reviewer_summary).hexdigest()
            != self.reviewer_summary_reference.payload_digest
        ):
            raise ValueError(
                "Publisher Authorization or reviewer closure mismatch"
            )


def _instant(now: datetime) -> str:
    if now.tzinfo is None or now.utcoffset() is None:
        raise ValueError("Publisher clock must be timezone-aware")
    return now.isoformat().replace("+00:00", "Z")


def _runtime_paths(
    directory: Path, toolchain: Path, checkout: Path
) -> tuple[Path, Path]:
    directory = directory.absolute()
    toolchain = toolchain.resolve(strict=True)
    if (
        directory != directory.resolve()
        or directory.is_relative_to(checkout.resolve())
        or toolchain.is_relative_to(checkout.resolve())
    ):
        raise ValueError(
            "Publisher runtime and toolchain must be outside checkout"
        )
    return directory, toolchain


def _environment(
    directory: Path, toolchain: Path, token: str
) -> dict[str, str]:
    if type(token) is not str or not token or any(c in token for c in "\r\n\0"):
        raise ValueError("Publisher requires a current repository GITHUB_TOKEN")
    # Nothing from ambient configuration, including NODE_OPTIONS, is inherited.
    return {
        "PATH": os.pathsep.join((str(toolchain), os.defpath)),
        "HOME": str(directory / "home"),
        "TMPDIR": str(directory / "scratch"),
        "GITHUB_TOKEN": token,
        "NPM_CONFIG_USERCONFIG": str(directory / "user.npmrc"),
        "NPM_CONFIG_GLOBALCONFIG": str(directory / "global.npmrc"),
        "NPM_CONFIG_CACHE": str(directory / "cache"),
        "NPM_CONFIG_LOGS_MAX": "0",
    }


def _private_file(path: Path, content: bytes) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(content)


def _validate_runtime(directory: Path) -> None:
    if (
        not stat.S_ISDIR(directory.lstat().st_mode)
        or stat.S_IMODE(directory.stat().st_mode) != _PRIVATE_DIRECTORY_MODE
    ):
        raise ValueError("Publisher runtime is not runner-private")
    for name, expected in (
        ("package.json", _LOCAL_MANIFEST),
        ("user.npmrc", _USER_CONFIG.encode()),
        ("global.npmrc", b""),
    ):
        path = directory / name
        if (
            not stat.S_ISREG(path.lstat().st_mode)
            or stat.S_IMODE(path.stat().st_mode) != _PRIVATE_FILE_MODE
            or path.read_bytes() != expected
        ):
            raise ValueError("Publisher prepared configuration changed")
    if (directory / ".npmrc").exists():
        raise ValueError("Publisher project configuration is forbidden")


def _profile_match(  # noqa: PLR0913
    inputs: PublicationInputs,
    *,
    directory: Path,
    toolchain: Path,
    token: str,
    expectation: ArtifactExpectation,
    runner: NpmProcessRunner,
    clock: Callable[[], datetime],
) -> ProfileMatchEvidence:
    _validate_runtime(directory)
    profile = github_packages_destination_operation_profile()
    action = inputs.publication_snapshot.materialized_actions[0]
    tarball = directory / inputs.artifact.content.basename
    try:
        _validate_local_tarball_preconditions(
            tarball=tarball,
            artifact=inputs.artifact,
            expectation=expectation,
            expanded_tarball_limit_bytes=DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
        )
    except ValueError as error:
        raise _ProfileRejectionError(
            "Publisher qualified tarball mismatch"
        ) from error
    entries = _read_tarball(
        tarball.read_bytes(),
        max_payload_bytes=DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    )
    manifest = _load_packed_manifest(entries["package/package.json"])
    if "publishConfig" in manifest:
        raise _ProfileRejectionError(
            "Publisher forbids packed publishConfig operands"
        )
    command = tuple(
        {"{tarball-path}": str(tarball), "{tag}": action.tag}.get(word, word)
        for word in profile.command_template
    )
    environment = _environment(directory, toolchain, token)

    def query(argv: tuple[str, ...]) -> str:
        outcome = runner.run(
            argv,
            cwd=directory,
            environment=environment,
            timeout=_QUERY_TIMEOUT,
            output_limit=_OUTPUT_LIMIT,
        )
        if outcome.classification != "definitive-success" or outcome.truncated:
            raise _ProfileRejectionError(
                "Publisher toolchain/configuration query failed"
            )
        try:
            return outcome.output.decode("utf-8").strip()
        except UnicodeError:
            raise _ProfileRejectionError(
                "Publisher toolchain/configuration is not UTF-8"
            ) from None

    node = query(("node", "--version"))
    npm = query(("npm", "--version"))
    if node != "v" + profile.node_version or npm != profile.npm_version:
        raise _ProfileRejectionError("Publisher pinned toolchain mismatch")
    # The trusted manifest anchors npm's documented local-prefix discovery.
    # User/global config paths are fixed by the exact environment and checked
    # files, not npm's intentionally redacted textual rendering of paths.
    expected = {
        "@hcoona:registry": profile.registry,
        # npm's URL-typed registry config serializes the root slash.
        "registry": profile.registry + "/",
        "tag": action.tag,
        "ignore-scripts": "true",
        "fetch-retries": "0",
        "access": "null",
    }
    actual = {}
    for key, value in expected.items():
        observed = query(("npm", "config", "get", key, *command[3:]))
        if observed != value:
            raise _ProfileRejectionError(
                "Publisher effective npm configuration mismatch"
            )
        actual[key] = observed
    return ProfileMatchEvidence(
        destination_operation_profile_digest=profile.profile_digest,
        node_version=node.removeprefix("v"),
        npm_version=npm,
        command=command,
        configuration=tuple(sorted(actual.items())),
        matched_at=_instant(clock()),
    )


def prepare_publication(  # noqa: PLR0913
    inputs: PublicationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    tarball: Path,
    runtime_directory: Path,
    toolchain_directory: Path,
    checkout: Path,
    expectation: ArtifactExpectation,
    runner: NpmProcessRunner,
    governance_client: GovernanceSourceClient,
    transport: GitHubPackagesTransport,
    token: str,
    clock: Callable[[], datetime],
) -> MutationMayHaveStartedMarker:
    """Return the canonical marker for standard upload after fresh preparation.

    Preserve this exact runner-local directory across marker upload/admission.
    Execution reconstructs and revalidates it; no local manifest is authority.
    """
    inputs.validate(current=current, run_attempt=run_attempt, now=clock())
    directory, toolchain = _runtime_paths(
        runtime_directory, toolchain_directory, checkout
    )
    _environment(directory, toolchain, token)
    _validate_local_tarball_preconditions(
        tarball=tarball,
        artifact=inputs.artifact,
        expectation=expectation,
        expanded_tarball_limit_bytes=DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES,
    )
    directory.mkdir(mode=0o700)
    with ExitStack() as cleanup:
        cleanup.callback(shutil.rmtree, directory)
        for name in ("home", "scratch", "cache"):
            (directory / name).mkdir(mode=0o700)
        _private_file(directory / "package.json", _LOCAL_MANIFEST)
        _private_file(directory / "user.npmrc", _USER_CONFIG.encode())
        _private_file(directory / "global.npmrc", b"")
        _private_file(directory / tarball.name, tarball.read_bytes())
        initial = inputs.eligibility.governance
        fresh = require_fresh_governance_identity(
            inputs.policy.governance,
            governance_client,
            now=clock(),
            expected_provenance=initial.provenance,
            expected_canonical_content_digest=initial.canonical_content_digest,
            expected_expires_at=_instant(initial.attestation.expires_at),
            expected_live_enabled=initial.attestation.live_enabled,
        )
        profile = github_packages_destination_operation_profile()
        require_action_governance(
            fresh.attestation,
            now=clock(),
            destination_operation_profile_digest=profile.profile_digest,
        )
        state = read_github_packages_active_state(
            inputs.artifact,
            expectation,
            token=token,
            transport=transport,
            observed_at=_instant(clock()),
        )
        if (
            state.package_control is None
            or classify_package_control(
                state.package_control,
                subject=inputs.observation.desired_subject,
                eligibility=inputs.eligibility,
            )
            != "ready"
        ):
            raise ValueError("Publisher fresh package control is not ready")
        # Version/tag absence is deliberately not rechecked after Observation.
        match = _profile_match(
            inputs,
            directory=directory,
            toolchain=toolchain,
            token=token,
            expectation=expectation,
            runner=runner,
            clock=clock,
        )
        require_action_governance(
            fresh.attestation,
            now=clock(),
            destination_operation_profile_digest=profile.profile_digest,
        )
        marker = MutationMayHaveStartedMarker(
            attempt=inputs.authorization.attempt,
            publication_authorization_reference=inputs.authorization_reference,
            governance_proof=GovernanceProof(
                provenance=governance_observation_provenance(fresh),
                current_main_sha=fresh.current_main_sha,
                observed_at=_instant(fresh.observed_at),
                expires_at=_instant(fresh.attestation.expires_at),
                live_enabled=fresh.attestation.live_enabled,
            ),
            package_control_proof=state.package_control,
            profile_match=match,
            producer=GITHUB_PACKAGES_PUBLISHER_PRODUCER,
            control=inputs.eligibility.context.control,
            workflow_run_id=inputs.intent.workflow_run_id,
        )
        cleanup.pop_all()
        return marker


def _publication_result(  # noqa: PLR0913
    inputs: PublicationInputs,
    marker: MutationMayHaveStartedMarker,
    marker_reference: ArtifactReference,
    *,
    command_classification: CommandClassification,
    readback: DestinationReadback | None,
    diagnostics: PublicationDiagnostics,
) -> PublicationResult:
    published = (
        command_classification == "definitive-success"
        and readback is not None
        and readback.classification == "exact-satisfied"
    )
    mutation = (
        "not-mutated"
        if command_classification == "not-initiated"
        else "mutated"
        if published
        else "possibly-mutated"
    )
    return PublicationResult(
        attempt=marker.attempt,
        mutation_marker_reference=marker_reference,
        command_classification=command_classification,
        post_action_readback=readback,
        result="published" if published else "failed",
        mutation_classification=mutation,
        # The process exposes no supported command-response identity.
        # Actual readback response digests belong to the readback itself.
        response_identity=None,
        diagnostics=diagnostics,
        producer=GITHUB_PACKAGES_PUBLISHER_PRODUCER,
        control=inputs.eligibility.context.control,
        workflow_run_id=inputs.intent.workflow_run_id,
    )


def execute_publication(  # noqa: PLR0913
    inputs: PublicationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    durable_marker: MutationMayHaveStartedMarker,
    marker_reference: ArtifactReference,
    runtime_directory: Path,
    toolchain_directory: Path,
    checkout: Path,
    expectation: ArtifactExpectation,
    runner: NpmProcessRunner,
    transport: GitHubPackagesTransport,
    token: str,
    clock: Callable[[], datetime],
) -> PublicationResult:
    """Consume a service-admitted marker once; return an unuploaded Result.

    The caller obtains both marker and reference through Shared Foundation
    artifact admission, never from a local upload intention. This function
    cannot attest to transport and deliberately invents no service proof.
    """
    inputs.validate(current=current, run_attempt=run_attempt, now=clock())
    if (
        type(durable_marker) is not MutationMayHaveStartedMarker
        or type(marker_reference) is not ArtifactReference
    ):
        raise ValueError("Publication requires a validated durable marker")
    admit_release_record(
        canonicalize(durable_marker.to_document()),
        expected=durable_marker,
        expected_digest=marker_reference.payload_digest,
        expected_bindings=current,
    )
    initial = inputs.eligibility.governance
    if (
        durable_marker.attempt != inputs.authorization.attempt
        or durable_marker.publication_authorization_reference
        != inputs.authorization_reference
        or durable_marker.governance_proof.provenance != initial.provenance
        or datetime.fromisoformat(durable_marker.governance_proof.expires_at)
        != initial.attestation.expires_at
        or classify_package_control(
            durable_marker.package_control_proof,
            subject=inputs.observation.desired_subject,
            eligibility=inputs.eligibility,
        )
        != "ready"
        or not (
            datetime.fromisoformat(inputs.authorization.completed_at)
            <= datetime.fromisoformat(
                durable_marker.governance_proof.observed_at
            )
            <= datetime.fromisoformat(
                durable_marker.package_control_proof.observed_at
            )
            <= datetime.fromisoformat(durable_marker.profile_match.matched_at)
            <= clock()
            < initial.attestation.expires_at
        )
    ):
        raise ValueError("Publication marker authority or freshness mismatch")
    directory, toolchain = _runtime_paths(
        runtime_directory, toolchain_directory, checkout
    )
    _validate_runtime(directory)
    # Claim the local lifecycle before entering its cleanup scope. A competing
    # caller must neither invoke npm nor delete the owner's prepared files.
    _private_file(directory / "command-started", b"")
    try:
        try:
            match = _profile_match(
                inputs,
                directory=directory,
                toolchain=toolchain,
                token=token,
                expectation=expectation,
                runner=runner,
                clock=clock,
            )
            if (
                replace(
                    match, matched_at=durable_marker.profile_match.matched_at
                )
                != durable_marker.profile_match
            ):
                # Marker authority mismatch is not an operational rejection.
                raise ValueError(
                    "Publication differs from the prepared profile"
                )
            require_action_governance(
                initial.attestation,
                now=clock(),
                destination_operation_profile_digest=(
                    match.destination_operation_profile_digest
                ),
            )
        except (_ProfileRejectionError, GovernanceFreshnessRejectionError):
            return _publication_result(
                inputs,
                durable_marker,
                marker_reference,
                command_classification="not-initiated",
                readback=None,
                diagnostics=PublicationDiagnostics(
                    entries=(
                        "npm publish not initiated: profile/freshness rejected",
                    ),
                    truncated=False,
                ),
            )
        outcome = runner.run(
            match.command,
            cwd=directory,
            environment=_environment(directory, toolchain, token),
            timeout=_PUBLISH_TIMEOUT,
            output_limit=_OUTPUT_LIMIT,
        )
        state = read_github_packages_active_state(
            inputs.artifact,
            expectation,
            token=token,
            transport=transport,
            observed_at=_instant(clock()),
        )
        # npm output is not a supported registry-response grammar. Retain only
        # bounded process facts and the active reader's sanitized diagnostics.
        return _publication_result(
            inputs,
            durable_marker,
            marker_reference,
            command_classification=outcome.classification,
            readback=state.readback,
            diagnostics=PublicationDiagnostics(
                entries=(
                    f"npm process: {outcome.classification}",
                    *state.diagnostics.entries,
                ),
                truncated=outcome.truncated or state.diagnostics.truncated,
            ),
        )
    finally:
        shutil.rmtree(directory)
