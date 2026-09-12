"""Native destination contracts and modeled current-Attempt scenarios."""

from __future__ import annotations

# Modeled service and native admission fixtures supply no platform guarantee.
# ruff: noqa: D103
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.records.release import (
    NugetDestinationOperationProfile,
    NugetDestinationReadback,
    NugetPublicationAction,
    NugetRemoteStateObservation,
    PackageControlProof,
    PackageControlSubject,
    PublicationAction,
    PublicationDiagnostics,
    RemoteStateObservation,
    admit_release_record,
    form_nuget_publication_action,
    publication_mutable_resource_keys,
    publication_serialization_projection,
)
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)

from ..repository.test_dotnet_compiler import (
    native_scenario as native_scenario,  # noqa: PLC0414
)
from ..repository.test_dotnet_compiler import (
    native_scenario_basis as native_scenario_basis,  # noqa: PLC0414
)
from .test_nuget_qualification import _qualified
from .test_nuget_qualification import (
    nuget_scenario as nuget_scenario,  # noqa: PLC0414
)

NOW_TEXT = "2026-09-10T14:00:00Z"
DIGEST = "sha256:" + "a" * 64
CONTROL_URL = "https://api.github.com/users/hcoona/packages/nuget/Hcoona.ReleaseSmoke.GithubPackages"
RESOURCES = native.NuGetServiceResources(
    "https://nuget.pkg.github.com/hcoona/download/",
    "https://nuget.pkg.github.com/hcoona/",
    "a" * 64,
)


def _modeled_profile_document(resources=RESOURCES):
    # Synthetic imported host facts exercise records, not native acceptance.
    return native._nuget_profile_document(  # noqa: SLF001
        resources.package_publish,
        {
            "executableSha256": "b" * 64,
            "runtimeBuild": "modeled CPython 3.13.12 build",
            "sslSourceSha256": "c" * 64,
            "platform": "Windows",
            "tlsLibrary": "modeled TLS library",
            "adapterSha256": "d" * 64,
        },
    )


@pytest.fixture
def native_profile():
    return NugetDestinationOperationProfile(
        canonicalize(_modeled_profile_document())
    )


def _reference(digest, run_id, artifact_id=500):
    return ArtifactReference(
        artifact_id,
        DIGEST,
        f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/{artifact_id}",
        "record.json",
        digest,
    )


def _observation(artifact, decision_reference, *, exact):
    subject = PackageControlSubject(
        native.NUGET_DESTINATION_ID,
        native.NUGET_SERVICE_INDEX,
        artifact.identity.normalized_package_id,
    )
    control = PackageControlProof(
        subject,
        NOW_TEXT,
        (CONTROL_URL,),
        (
            ("exposed-access", ()),
            ("owner", ("hcoona",)),
            ("repository-association", ("hcoona/three",)),
            ("visibility", ("public",)),
        ),
        ((CONTROL_URL, DIGEST),),
    )
    readback = NugetDestinationReadback(
        artifact.identity,
        "exact-satisfied" if exact else "absent",
        artifact.content.content_sha256 if exact else None,
        artifact.content.content_sha512 if exact else None,
        artifact.witness_digest if exact else None,
        artifact.target if exact else None,
        NOW_TEXT,
        ((native.NUGET_SERVICE_INDEX, DIGEST),),
    )
    return NugetRemoteStateObservation(
        artifact.subject,
        decision_reference,
        subject,
        artifact.identity,
        artifact.content.content_sha256,
        artifact.content.content_sha512,
        artifact.witness_digest,
        readback.classification,
        control,
        readback,
        DIGEST,
        PublicationDiagnostics(entries=(), truncated=False),
        "observe-github-packages",
        f"workflow-delivery-v3:{artifact.target}",
        artifact.subject.workflow_run_id,
    )


def test_native_profile_preserves_imported_bytes_without_local_reconstruction(
    native_profile, monkeypatch
):
    original = native_profile.canonical_bytes
    monkeypatch.setattr(
        native,
        "nuget_operation_profile",
        lambda _resources: pytest.fail("import evaluated runtime"),
    )
    restored = NugetDestinationOperationProfile(original)
    assert restored == native_profile
    assert restored.profile_digest == canonical_sha256(restored.to_document())
    assert restored.to_document()["platform"] == "Windows"
    assert restored.to_document()["executableSha256"] == "b" * 64
    detached = restored.to_document()
    detached["platform"] = "substituted"
    assert canonicalize(restored.to_document()) == original


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("transportRetries", 1),
        ("redirects", False),
        ("automaticProxy", 0),
        ("packagePublish", "https://attacker.example/upload"),
        ("httpClientSha256", "f" * 64),
        ("executableSha256", "broken"),
        ("authentication", {}),
        ("unrecognized", True),
    ],
)
def test_native_profile_rejects_open_or_changed_protocol(
    native_profile, field, value
):
    document = native_profile.to_document()
    document[field] = value
    with pytest.raises((TypeError, ValueError)):
        NugetDestinationOperationProfile(canonicalize(document))


def test_native_action_binds_original_nupkg_and_equivalent_resource(
    nuget_scenario, native_profile, monkeypatch, tmp_path
):
    artifact, _evidence, _calls = _qualified(
        nuget_scenario, monkeypatch, tmp_path
    )
    projection = nuget_scenario.snapshot.destination_projections[0]
    action = form_nuget_publication_action(
        destination_operation_profile=native_profile,
        projection=projection,
        artifact=artifact,
    )
    assert action.identity == artifact.identity
    assert action.nupkg_reference.artifact_id == artifact.transport.artifact_id
    assert (
        action.nupkg_reference.payload_digest == artifact.content.content_sha256
    )
    assert action.nupkg_reference.payload_path == artifact.content.basename
    assert len(action.mutable_resource_keys) == 1
    assert "tag" not in action.to_document()
    assert "tarball-reference" not in action.to_document()
    assert (
        admit_release_record(
            canonicalize(action.to_document()),
            expected_type=NugetPublicationAction,
            expected_digest=action.action_digest,
        )
        == action
    )
    with pytest.raises((TypeError, ValueError)):
        admit_release_record(
            canonicalize(action.to_document()),
            expected_type=PublicationAction,
            expected_digest=action.action_digest,
        )
    alias = replace(
        artifact.identity,
        package_name=artifact.identity.package_name.lower(),
        native_version=artifact.identity.native_version + "+ignored",
    )
    equivalent_projection = replace(
        projection, coordinate=replace(projection.coordinate, identity=alias)
    )
    assert (
        publication_mutable_resource_keys(equivalent_projection)
        == action.mutable_resource_keys
    )
    assert (
        publication_serialization_projection(equivalent_projection)
        == action.serialization_projection
    )
    assert equivalent_projection.coordinate.identity != action.identity


@pytest.mark.parametrize("exact", [False, True])
def test_native_observation_roundtrip_closes_no_tag_identity(
    nuget_scenario, monkeypatch, tmp_path, exact
):
    artifact, _evidence, _calls = _qualified(
        nuget_scenario, monkeypatch, tmp_path
    )
    reference = _reference(DIGEST, artifact.subject.workflow_run_id)
    observation = _observation(artifact, reference, exact=exact)
    assert observation.classification == (
        "exact-satisfied" if exact else "absent"
    )
    assert "tag" not in observation.active_readback.to_document()
    assert (
        admit_release_record(
            canonicalize(observation.to_document()),
            expected_type=NugetRemoteStateObservation,
            expected_digest=observation.observation_digest,
            expected_bindings=ReleaseAdmissionBindings(
                "live-release",
                artifact.subject.workflow_run_id,
                None,
                artifact.target,
                "observe-github-packages",
            ),
        )
        == observation
    )
    with pytest.raises((TypeError, ValueError)):
        admit_release_record(
            canonicalize(observation.to_document()),
            expected_type=RemoteStateObservation,
            expected_digest=observation.observation_digest,
        )
    with pytest.raises(ValueError, match="identity mismatch"):
        replace(
            observation,
            active_readback=replace(
                observation.active_readback,
                identity=replace(
                    artifact.identity, normalized_version="999.0.0"
                ),
            ),
        )
    if exact:
        with pytest.raises(ValueError, match="differs from desired"):
            replace(observation, desired_content_sha256=DIGEST)
    else:
        with pytest.raises(ValueError, match="cannot contain version facts"):
            replace(observation.active_readback, content_sha256=DIGEST)
