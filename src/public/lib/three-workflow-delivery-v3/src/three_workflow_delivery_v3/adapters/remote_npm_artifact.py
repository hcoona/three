"""Retained native tarball observation for the existing Buddy caller."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.node import (
    _PACKED_WITNESS_PATH,
    ArtifactExpectation,
    _qualify_npm_artifact_entries,
    _read_tarball,
    _TarballExpansionLimitError,
    package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.release import ReleaseArtifact

DEFAULT_EXPANDED_TARBALL_LIMIT_BYTES = 100_000_000


@dataclass(frozen=True, slots=True)
class _RemoteTarballObservation:
    classification: str
    content_sha512: str
    byte_size: int
    witness_digest: str | None
    witness_target: str | None = None


def _parsed_witness_identity(
    entries: dict[str, bytes],
) -> tuple[str, str]:
    witness = entries[_PACKED_WITNESS_PATH]
    document = parse_canonical_json(witness)
    parsed = package_target_witness_from_document(document)
    return canonical_sha256(document), parsed.target


def _remote_tarball_observation(  # noqa: C901, PLR0911
    tarball: bytes,
    *,
    artifact: ReleaseArtifact,
    expectation: ArtifactExpectation,
    expanded_limit_bytes: int,
) -> _RemoteTarballObservation:
    remote_sha512 = f"sha512:{hashlib.sha512(tarball).hexdigest()}"
    byte_size = len(tarball)
    try:
        entries = _read_tarball(
            tarball,
            max_payload_bytes=expanded_limit_bytes,
        )
    except _TarballExpansionLimitError:
        return _RemoteTarballObservation(
            "unprovable",
            remote_sha512,
            byte_size,
            None,
        )
    except (TypeError, ValueError):
        return _RemoteTarballObservation(
            "unprovable",
            remote_sha512,
            byte_size,
            None,
        )
    try:
        _qualify_npm_artifact_entries(tarball, entries, expectation)
    except (TypeError, ValueError):
        try:
            manifest = parse_json_strict(entries["package/package.json"])
        except (KeyError, TypeError, ValueError):
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        if not isinstance(manifest, dict):
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        identity_mismatch = (
            manifest.get("name") != expectation.package_name
            or manifest.get("version") != expectation.npm_package_version
        )
        witness = entries.get(_PACKED_WITNESS_PATH)
        witness_document: JsonValue | None = None
        witness_digest: str | None = None
        witness_target: str | None = None
        if witness is not None:
            try:
                witness_document = parse_canonical_json(witness)
                parsed_witness = package_target_witness_from_document(
                    witness_document
                )
                witness_digest = canonical_sha256(witness_document)
                witness_target = parsed_witness.target
            except (TypeError, ValueError):
                witness_document = None
                witness_digest = None
        if identity_mismatch:
            return _RemoteTarballObservation(
                "conflicting",
                remote_sha512,
                byte_size,
                witness_digest,
                witness_target,
            )
        if witness_document is None:
            return _RemoteTarballObservation(
                "unprovable",
                remote_sha512,
                byte_size,
                None,
            )
        return _RemoteTarballObservation(
            "conflicting",
            remote_sha512,
            byte_size,
            canonical_sha256(witness_document),
            witness_target,
        )
    witness_digest, witness_target = _parsed_witness_identity(entries)
    if remote_sha512 == artifact.content.content_sha512:
        return _RemoteTarballObservation(
            "exact-satisfied",
            remote_sha512,
            byte_size,
            witness_digest,
            witness_target,
        )
    return _RemoteTarballObservation(
        "conflicting",
        remote_sha512,
        byte_size,
        witness_digest,
        witness_target,
    )
