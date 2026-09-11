"""Build-free native publication with external marker and Result persistence."""

from __future__ import annotations

import hashlib
import shutil
from datetime import datetime
from subprocess import TimeoutExpired
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    GovernanceProof,
    MutationMayHaveStartedMarker,
    NugetDestinationOperationProfile,
    NugetDestinationReadback,
    NugetProfileMatchEvidence,
    NugetReleaseArtifact,
    NugetRemoteStateObservation,
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
from three_workflow_delivery_v3.release.nuget_eligibility import (
    AdmittedNugetLiveEligibilityDecision,
)
from three_workflow_delivery_v3.release.nuget_observation import (
    classify_nuget_package_control,
    nuget_readback_from_state,
)
from three_workflow_delivery_v3.release.publication import PublicationInputs

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.records.release_transport import (
        ReleaseAdmissionBindings,
    )
    from three_workflow_delivery_v3.release.eligibility import (
        GovernanceSourceClient,
    )


def _native_inputs(
    inputs: PublicationInputs,
) -> tuple[
    AdmittedNugetLiveEligibilityDecision,
    NugetReleaseArtifact,
    NugetRemoteStateObservation,
]:
    if (
        type(inputs) is not PublicationInputs
        or type(inputs.eligibility) is not AdmittedNugetLiveEligibilityDecision
        or type(inputs.artifact) is not NugetReleaseArtifact
        or type(inputs.observation) is not NugetRemoteStateObservation
    ):
        message = "NuGet publisher requires exact native authority inputs"
        raise TypeError(message)
    return inputs.eligibility, inputs.artifact, inputs.observation


def _instant(now: datetime) -> str:
    if now.tzinfo is None or now.utcoffset() is None:
        message = "NuGet publisher clock must be timezone-aware"
        raise ValueError(message)
    return now.isoformat().replace("+00:00", "Z")


def _original_package(
    path: Path, artifact: NugetReleaseArtifact, authority: native.NuGetAuthority
) -> bytes:
    # Only the prebuilt trusted inspector runs here. No project, pack, restore,
    # consumer, or target-supplied executable participates in publication.
    package = path.read_bytes()
    if (
        len(package) != artifact.content.byte_size
        or "sha256:" + hashlib.sha256(package).hexdigest()
        != artifact.content.content_sha256
        or "sha512:" + hashlib.sha512(package).hexdigest()
        != artifact.content.content_sha512
    ):
        message = "NuGet publisher original qualified archive mismatch"
        raise ValueError(message)
    inspection = native.inspect_nuget_package(authority, package)
    if (
        inspection.identity.normalized_package_id,
        inspection.identity.normalized_version,
    ) != (
        artifact.identity.normalized_package_id,
        artifact.identity.normalized_version,
    ) or "sha256:" + inspection.witness_sha256 != artifact.witness_digest:
        message = "NuGet publisher official identity or witness mismatch"
        raise ValueError(message)
    return package


def prepare_nuget_publication(  # noqa: PLR0913
    inputs: PublicationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    nupkg: Path,
    runtime_directory: Path,
    authority: native.NuGetAuthority,
    governance_client: GovernanceSourceClient,
    transport: native.NuGetReadTransport,
    token: str,
    clock: Callable[[], datetime],
) -> MutationMayHaveStartedMarker:
    """Prepare original bytes and fresh proofs for external marker upload."""
    eligibility, artifact, observation = _native_inputs(inputs)
    inputs.validate(current=current, run_attempt=run_attempt, now=clock())
    package = _original_package(nupkg, artifact, authority)
    runtime_directory.mkdir(mode=0o700)
    try:
        with (runtime_directory / artifact.content.basename).open(
            "xb"
        ) as output:
            output.write(package)
        initial = eligibility.governance
        fresh = require_fresh_governance_identity(
            inputs.policy.governance,
            governance_client,
            now=clock(),
            expected_provenance=initial.provenance,
            expected_canonical_content_digest=initial.canonical_content_digest,
            expected_expires_at=_instant(initial.attestation.expires_at),
            expected_live_enabled=initial.attestation.live_enabled,
        )
        state = native.read_nuget_active_state(
            transport=transport,
            authority=authority,
            token=token,
            package_id=artifact.identity.package_name,
            version=artifact.identity.native_version,
        )
        proof, _readback = nuget_readback_from_state(
            state, artifact=artifact, observed_at=_instant(clock())
        )
        if (
            classify_nuget_package_control(
                proof,
                subject=observation.desired_subject,
                eligibility=eligibility,
            )
            != "ready"
        ):
            message = "NuGet publisher fresh package control is not ready"
            raise ValueError(message)  # noqa: TRY301 - local failure cleanup/classification
        profile = NugetDestinationOperationProfile(
            canonicalize(native.nuget_operation_profile(state.resources))
        )
        if profile != eligibility.profile:
            message = (
                "NuGet publisher actual transport differs from admitted profile"
            )
            raise ValueError(message)  # noqa: TRY301 - local failure cleanup/classification
        require_action_governance(
            fresh.attestation,
            now=clock(),
            destination_operation_profile_digest=profile.profile_digest,
        )
        return MutationMayHaveStartedMarker(
            inputs.authorization.attempt,
            inputs.authorization_reference,
            GovernanceProof(
                governance_observation_provenance(fresh),
                fresh.current_main_sha,
                _instant(fresh.observed_at),
                _instant(fresh.attestation.expires_at),
                fresh.attestation.live_enabled,
            ),
            proof,
            NugetProfileMatchEvidence(profile, _instant(clock())),
            "publish-github-packages",
            eligibility.context.control,
            inputs.intent.workflow_run_id,
        )
    except BaseException:
        shutil.rmtree(runtime_directory)
        raise


def _result(  # noqa: PLR0913
    inputs: PublicationInputs,
    marker: MutationMayHaveStartedMarker,
    reference: ArtifactReference,
    *,
    classification: str,
    readback: NugetDestinationReadback | None,
    diagnostics: tuple[str, ...],
    response_identity: str | None = None,
) -> PublicationResult:
    published = (
        classification == "definitive-success"
        and readback is not None
        and readback.classification == "exact-satisfied"
    )
    mutation = (
        "not-mutated"
        if classification == "not-initiated"
        else "mutated"
        if published
        else "possibly-mutated"
    )
    return PublicationResult(
        marker.attempt,
        reference,
        classification,
        readback,
        "published" if published else "failed",
        mutation,
        response_identity,
        PublicationDiagnostics(entries=diagnostics, truncated=False),
        "publish-github-packages",
        inputs.eligibility.context.control,
        inputs.intent.workflow_run_id,
    )


def execute_nuget_publication(  # noqa: PLR0913
    inputs: PublicationInputs,
    *,
    current: ReleaseAdmissionBindings,
    run_attempt: int,
    durable_marker: MutationMayHaveStartedMarker,
    marker_reference: ArtifactReference,
    runtime_directory: Path,
    read_resources: Callable[[], native.NuGetServiceResources],
    authority: native.NuGetAuthority,
    transport: native.NuGetReadTransport,
    token: str,
    clock: Callable[[], datetime],
) -> PublicationResult:
    """Consume a service-admitted marker once and return an unuploaded Result.

    The caller obtains both through immutable artifact admission.
    A local file or upload intention never stands in for that service evidence.
    Resource discovery runs only after full marker admission and ownership.
    """
    eligibility, artifact, observation = _native_inputs(inputs)
    if (
        type(durable_marker) is not MutationMayHaveStartedMarker
        or type(marker_reference) is not ArtifactReference
    ):
        message = "NuGet publication requires a validated durable marker"
        raise ValueError(message)
    inputs.validate(current=current, run_attempt=run_attempt, now=clock())
    admit_release_record(
        canonicalize(durable_marker.to_document()),
        expected=durable_marker,
        expected_digest=marker_reference.payload_digest,
        expected_bindings=current,
    )
    initial = eligibility.governance
    if (
        type(durable_marker.profile_match) is not NugetProfileMatchEvidence
        or durable_marker.profile_match.actual_profile != eligibility.profile
        or durable_marker.attempt != inputs.authorization.attempt
        or durable_marker.publication_authorization_reference
        != inputs.authorization_reference
        or durable_marker.governance_proof.provenance != initial.provenance
        or datetime.fromisoformat(durable_marker.governance_proof.expires_at)
        != initial.attestation.expires_at
        or classify_nuget_package_control(
            durable_marker.package_control_proof,
            subject=observation.desired_subject,
            eligibility=eligibility,
        )
        != "ready"
        or not datetime.fromisoformat(inputs.authorization.completed_at)
        <= datetime.fromisoformat(durable_marker.governance_proof.observed_at)
        <= datetime.fromisoformat(
            durable_marker.package_control_proof.observed_at
        )
        <= datetime.fromisoformat(durable_marker.profile_match.matched_at)
        <= clock()
        < initial.attestation.expires_at
    ):
        message = "NuGet publication marker authority or freshness mismatch"
        raise ValueError(message)
    # An exclusive local claim supplements current-Attempt platform controls.
    # Failure to claim does not enter cleanup or touch another caller's files.
    with (runtime_directory / "command-started").open("xb"):
        pass
    try:
        try:
            package = _original_package(
                runtime_directory / artifact.content.basename,
                artifact,
                authority,
            )
            resources = read_resources()
            profile = NugetDestinationOperationProfile(
                canonicalize(native.nuget_operation_profile(resources))
            )
            if profile != eligibility.profile:
                message = "NuGet transport changed after marker preparation"
                raise native.NuGetAdapterError(message)
            require_action_governance(
                initial.attestation,
                now=clock(),
                destination_operation_profile_digest=profile.profile_digest,
            )
        except (
            OSError,
            ValueError,
            TimeoutExpired,
            GovernanceFreshnessRejectionError,
            native.NuGetTransportError,
        ) as error:
            return _result(
                inputs,
                durable_marker,
                marker_reference,
                classification="not-initiated",
                readback=None,
                diagnostics=(
                    f"NuGet pre-invocation rejection: {type(error).__name__}",
                ),
            )
        try:
            invocation = native.publish_nuget_once(
                resources=resources,
                package=package,
                token=token,
                expected_profile_sha256=eligibility.profile.profile_digest,
                expected_package_sha256=artifact.content.content_sha256.removeprefix(
                    "sha256:"
                ),
            )
        except Exception as error:  # noqa: BLE001 - retain unknown invocation outcomes
            # A lost process/response cannot safely establish no mutation.
            return _result(
                inputs,
                durable_marker,
                marker_reference,
                classification="ambiguous",
                readback=None,
                diagnostics=(
                    f"NuGet invocation did not return: {type(error).__name__}",
                ),
            )
        classification = (
            "definitive-success"
            if invocation.definitive_success
            else "definitive-non-success"
            if invocation.response is not None and invocation.error_kind is None
            else "ambiguous"
        )
        response_identity = (
            None
            if invocation.response is None
            else "sha256:"
            + hashlib.sha256(invocation.response.body).hexdigest()
        )
        readback = None
        diagnostics: tuple[str, ...] = ()
        try:
            state = native.read_nuget_active_state(
                transport=transport,
                authority=authority,
                token=token,
                package_id=artifact.identity.package_name,
                version=artifact.identity.native_version,
            )
            _proof, readback = nuget_readback_from_state(
                state, artifact=artifact, observed_at=_instant(clock())
            )
        except (
            OSError,
            ValueError,
            TimeoutExpired,
            native.NuGetTransportError,
        ) as error:
            diagnostics = (
                f"NuGet post-publication read failed: {type(error).__name__}",
            )
        return _result(
            inputs,
            durable_marker,
            marker_reference,
            classification=classification,
            readback=readback,
            diagnostics=diagnostics,
            response_identity=response_identity,
        )
    finally:
        shutil.rmtree(runtime_directory)
