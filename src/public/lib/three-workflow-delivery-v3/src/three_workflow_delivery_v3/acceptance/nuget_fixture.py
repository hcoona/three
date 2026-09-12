"""Offline original NuGet fixtures without publication authority."""

from __future__ import annotations

import hashlib
import io
import re
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.dotnet import (
    DotnetArtifactExpectation,
    DotnetArtifactManifest,
    DotnetBuildRequest,
    DotnetConsumerResult,
    DotnetFixturePackMetadata,
    DotnetPackOutput,
    dotnet_package_target_witness_from_document,
    pack_frozen_dotnet_archives,
    qualify_nuget_artifact_contents,
    qualify_nuget_restore_build_invoke,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_PACKAGE,
    NativeNuGetHelper,
)

if TYPE_CHECKING:
    from pathlib import Path


@dataclass(frozen=True, slots=True)
class NuGetFixtureRequest:
    """Frozen Build inputs, local dependencies and a generation label.

    Protected revision/admission and immutable transport belong to the caller.
    A label does not establish freshness or destination absence.
    """

    build: DotnetBuildRequest
    dependency_archives: tuple[Path, ...]
    generation: str

    @property
    def comparison_metadata(self) -> DotnetFixturePackMetadata:
        """Vary ID casing and generation-bound version/description metadata."""
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", self.generation):
            message = "unsupported NuGet fixture generation label"
            raise ValueError(message)
        version = self.build.witness.nbgv.nuget_package_version
        separator = "." if "+" in version else "+"
        return DotnetFixturePackMetadata(
            DOTNET_PACKAGE.lower(),
            version + separator + "wdv3fixture." + self.generation,
            "Workflow Delivery v3 acceptance fixture " + self.generation + ".",
        )


@dataclass(frozen=True, slots=True)
class NuGetFixtureInspection:
    """Original byte identities and acceptance-only content observations."""

    original: DotnetArtifactManifest
    comparison: DotnetArtifactManifest
    original_identity: dict[str, JsonValue]
    comparison_identity: dict[str, JsonValue]
    witness: bytes

    def to_document(self) -> dict[str, JsonValue]:
        """Retain both native displays separately from the equal coordinate."""

        def archive(
            manifest: DotnetArtifactManifest, identity: dict[str, JsonValue]
        ) -> dict[str, JsonValue]:
            entries: list[JsonValue] = list(manifest.entries)
            return {
                "basename": manifest.basename,
                "entries": entries,
                "sha256": manifest.sha256,
                "sha512": manifest.sha512,
                "size": manifest.byte_size,
                "identity": identity,
            }

        return {
            "original": archive(self.original, self.original_identity),
            "comparison": archive(self.comparison, self.comparison_identity),
            "witness": parse_canonical_json(self.witness),
        }


@dataclass(frozen=True, slots=True)
class NuGetFixturePair:
    """A local pair with successful A content and consumer evidence."""

    original: DotnetPackOutput
    comparison: DotnetPackOutput
    inspection: NuGetFixtureInspection
    consumer: DotnetConsumerResult


def _identity(facts: dict[str, JsonValue]) -> dict[str, JsonValue]:
    value = facts.get("identity")
    if not isinstance(value, dict):
        message = "fixture native identity is missing"
        raise ValueError(message)  # noqa: TRY004
    return value


def _inspect_xml_variation(
    original: bytes, comparison: bytes, changes: tuple[tuple[str, str], ...]
) -> None:
    # Official NuGet inspection precedes this narrow comparison of local pack
    # metadata. ElementTree does not resolve external entities.
    left = ET.fromstring(original)  # noqa: S314
    right = ET.fromstring(comparison)  # noqa: S314
    for path, expected in changes:
        before = left.find(path)
        after = right.find(path)
        if (
            before is None
            or after is None
            or after.text != expected
            or before.text == after.text
        ):
            message = "fixture pack discarded its declared metadata variation"
            raise ValueError(message)
        after.text = before.text
    if ET.tostring(left) != ET.tostring(right):
        message = "fixture pack changed undeclared package metadata"
        raise ValueError(message)


def _inspect_relationships(
    original: bytes,
    comparison: bytes,
    targets: dict[str, str],
) -> None:
    left = ET.fromstring(original)  # noqa: S314
    right = ET.fromstring(comparison)  # noqa: S314
    if len(left) != len(right):
        message = "fixture package relationships changed"
        raise ValueError(message)
    for before, after in zip(left, right, strict=True):
        after.set("Id", before.get("Id", ""))
        actual = after.get("Target", "")
        after.set("Target", targets.get(actual, actual))
    if ET.tostring(left) != ET.tostring(right):
        message = "fixture package relationships changed"
        raise ValueError(message)


def inspect_nuget_fixture_pair(
    original: DotnetPackOutput,
    comparison: DotnetPackOutput,
    expectation: DotnetArtifactExpectation,
    metadata: DotnetFixturePackMetadata,
    helper: NativeNuGetHelper,
) -> NuGetFixtureInspection:
    """Inspect pack spellings and reuse without qualifying B for Release."""
    witness = dotnet_package_target_witness_from_document(
        parse_canonical_json(expectation.witness_bytes)
    )
    if witness.purpose != "destination-acceptance":
        message = "NuGet fixtures require destination-acceptance purpose"
        raise ValueError(message)
    manifest = qualify_nuget_artifact_contents(
        original.content, expectation, helper
    )
    left = helper.inspect_package(original.content)
    right = helper.inspect_package(comparison.content)
    left_identity, right_identity = _identity(left), _identity(right)
    expected_right = helper.normalize_identity(
        metadata.package_id, metadata.version
    )
    if (
        left_identity.get("displayPackageId") != expectation.package_name
        or left_identity.get("displayVersion")
        != expectation.nuget_package_version
        or right_identity != expected_right
        or metadata.package_id == expectation.package_name
        or metadata.version == expectation.nuget_package_version
        or any(
            left_identity.get(key) != right_identity.get(key)
            for key in ("normalizedPackageId", "normalizedVersion")
        )
    ):
        message = "fixture native identity or retained display mismatch"
        raise ValueError(message)
    if any(
        left.get(key) != right.get(key)
        for key in (
            "assembly",
            "repository",
            "frameworks",
            "dependencies",
            "witnessBase64",
        )
    ):
        message = "fixture frozen payload facts differ"
        raise ValueError(message)
    left_nuspec = expectation.package_name + ".nuspec"
    right_nuspec = metadata.package_id + ".nuspec"
    with (
        zipfile.ZipFile(io.BytesIO(original.content)) as left_zip,
        zipfile.ZipFile(io.BytesIO(comparison.content)) as right_zip,
    ):
        names = tuple(right_zip.namelist())
        core = tuple(
            name
            for name in names
            if re.fullmatch(
                r"package/services/metadata/core-properties/[0-9a-f]+\.psmdcp",
                name,
            )
        )
        stable = {
            name
            for name in manifest.entries
            if not name.startswith("package/services/metadata/core-properties/")
        } - {left_nuspec}
        if len(core) != 1 or set(names) != stable | {right_nuspec, *core}:
            message = "fixture comparison entry closure mismatch"
            raise ValueError(message)
        for name in stable - {"_rels/.rels"}:
            if left_zip.read(name) != right_zip.read(name):
                message = (
                    "fixture changed compiled inputs or fixed package content"
                )
                raise ValueError(message)
        _inspect_xml_variation(
            left_zip.read(left_nuspec),
            right_zip.read(right_nuspec),
            (
                ("{*}metadata/{*}id", metadata.package_id),
                ("{*}metadata/{*}version", metadata.version),
                ("{*}metadata/{*}description", metadata.description),
            ),
        )
        left_core = next(
            name for name in manifest.entries if name.endswith(".psmdcp")
        )
        _inspect_xml_variation(
            left_zip.read(left_core),
            right_zip.read(core[0]),
            (
                ("{*}identifier", metadata.package_id),
                ("{*}description", metadata.description),
            ),
        )
        _inspect_relationships(
            left_zip.read("_rels/.rels"),
            right_zip.read("_rels/.rels"),
            {
                "/" + right_nuspec: "/" + left_nuspec,
                "/" + core[0]: "/" + left_core,
            },
        )
    if (
        original.content == comparison.content
        or original.basename != manifest.basename
        or comparison.basename
        != f"{metadata.package_id}.{expectation.normalized_version}.nupkg"
    ):
        message = "fixture original archive identity mismatch"
        raise ValueError(message)
    return NuGetFixtureInspection(
        manifest,
        DotnetArtifactManifest(
            comparison.basename,
            names,
            "sha256:" + hashlib.sha256(comparison.content).hexdigest(),
            "sha512:" + hashlib.sha512(comparison.content).hexdigest(),
            len(comparison.content),
        ),
        left_identity,
        right_identity,
        expectation.witness_bytes,
    )


def prepare_nuget_fixture_pair(
    request: NuGetFixtureRequest,
) -> NuGetFixturePair:
    """Retain originals and diagnostics; return only after all checks pass."""
    metadata = request.comparison_metadata
    original, comparison = pack_frozen_dotnet_archives(
        request.build,
        comparison=metadata,
        dependency_archives=request.dependency_archives,
    )
    identity = request.build.helper.normalize_identity(
        DOTNET_PACKAGE, request.build.witness.nbgv.nuget_package_version
    )
    expectation = DotnetArtifactExpectation(
        DOTNET_PACKAGE,
        request.build.witness.nbgv.nuget_package_version,
        str(identity["normalizedPackageId"]),
        str(identity["normalizedVersion"]),
        request.build.witness.canonical_bytes,
    )
    inspection = inspect_nuget_fixture_pair(
        original, comparison, expectation, metadata, request.build.helper
    )
    evidence = request.build.evidence_directory
    (evidence / "witness.json").write_bytes(inspection.witness)
    consumer = qualify_nuget_restore_build_invoke(
        original.content,
        expectation,
        request.build.helper,
        evidence_directory=evidence / "consumer",
        offline_fixture=True,
    )
    (evidence / "fixtures.json").write_bytes(
        canonicalize(
            {
                "generation": request.generation,
                "inspection": inspection.to_document(),
                "consumer": {
                    "projectId": consumer.project_id,
                    "witnessSha256": consumer.witness_sha256,
                    "packageSha256": consumer.package_sha256,
                    "normalizedPackageId": consumer.normalized_package_id,
                    "normalizedVersion": consumer.normalized_version,
                },
            }
        )
    )
    return NuGetFixturePair(original, comparison, inspection, consumer)
