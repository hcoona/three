"""Repository-owned projection over normalized ecosystem authority facts."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.release.static_reference_model import (
    PRODUCER_MANIFEST,
    PRODUCER_PACKAGE,
    PRODUCER_ROOT,
    StaticReferenceFinding,
    normalized_repository_path,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.release.static_reference_source import (
        StaticReferenceCandidate,
    )


class StaticReferenceProjectionError(RuntimeError):
    """Normalized authority facts do not satisfy the bounded projection."""


@dataclass(frozen=True, slots=True)
class _SnapshotProjection:
    name: str
    resolution_kind: str
    local_path: str | None


def _object(value: object, *, field: str) -> Mapping[str, object]:
    if not isinstance(value, dict) or any(
        type(key) is not str for key in value
    ):
        message = f"{field} must be an object"
        raise StaticReferenceProjectionError(message)
    return value


def _array(value: object, *, field: str) -> Sequence[object]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise StaticReferenceProjectionError(message)
    return value


def _string(value: object, *, field: str) -> str:
    if type(value) is not str or not value:
        message = f"{field} must be an exact nonempty string"
        raise StaticReferenceProjectionError(message)
    return value


def _exact_string(value: object, *, field: str) -> str:
    if type(value) is not str:
        message = f"{field} must be an exact string"
        raise StaticReferenceProjectionError(message)
    return value


def _nullable_string(value: object, *, field: str) -> str | None:
    if value is None:
        return None
    return _string(value, field=field)


def _nullable_exact_string(value: object, *, field: str) -> str | None:
    if value is None:
        return None
    return _exact_string(value, field=field)


def _integer(value: object, *, field: str) -> int:
    if type(value) is not int or value < 0:
        message = f"{field} must be a nonnegative integer"
        raise StaticReferenceProjectionError(message)
    return value


def _expect_fields(
    value: Mapping[str, object],
    expected: set[str],
    *,
    field: str,
) -> None:
    if set(value) != expected:
        message = f"{field} fields are not exact"
        raise StaticReferenceProjectionError(message)


def _local_path(value: object, *, field: str) -> str | None:
    path = _nullable_string(value, field=field)
    if path is None or path == ".":
        return path
    try:
        return normalized_repository_path(path, field=field)
    except ValueError as error:
        message = f"{field} is not a normalized logical path"
        raise StaticReferenceProjectionError(message) from error


def _finding(
    candidate: StaticReferenceCandidate,
    *,
    context: str,
    prohibited_form: str,
    matched_identity: str,
) -> StaticReferenceFinding:
    return StaticReferenceFinding(
        path=candidate.path,
        family=candidate.selection.family,
        context=context,
        prohibited_form=prohibited_form,
        matched_identity=matched_identity,
    )


def _dependency_key_finding(
    candidate: StaticReferenceCandidate,
    *,
    dependency_key: str,
    context: str,
) -> StaticReferenceFinding | None:
    if dependency_key != PRODUCER_PACKAGE:
        return None
    return _finding(
        candidate,
        context=context,
        prohibited_form="dependency-key",
        matched_identity=PRODUCER_PACKAGE,
    )


def _npa_reference_findings(
    candidate: StaticReferenceCandidate,
    reference_value: object,
    *,
    context: str,
) -> list[StaticReferenceFinding]:
    reference = _object(reference_value, field="npm reference")
    _expect_fields(
        reference,
        {
            "aliasTarget",
            "fetchSpec",
            "localPath",
            "name",
            "rawSpec",
            "saveSpec",
            "type",
        },
        field="npm reference",
    )
    name = _string(reference["name"], field="npm reference.name")
    reference_type = _string(
        reference["type"],
        field="npm reference.type",
    )
    _exact_string(reference["rawSpec"], field="npm reference.rawSpec")
    _nullable_string(reference["saveSpec"], field="npm reference.saveSpec")
    _nullable_exact_string(
        reference["fetchSpec"],
        field="npm reference.fetchSpec",
    )
    local_path = _local_path(
        reference["localPath"],
        field="npm reference.localPath",
    )
    alias_target = reference["aliasTarget"]
    if reference_type == "alias":
        if alias_target is None:
            message = "npm alias reference is missing its target"
            raise StaticReferenceProjectionError(message)
        target = _object(alias_target, field="npm alias target")
        target_findings = _npa_reference_findings(
            candidate,
            target,
            context=context,
        )
        target_name = _string(target["name"], field="npm alias target.name")
        if target_name == PRODUCER_PACKAGE:
            return [
                _finding(
                    candidate,
                    context=context,
                    prohibited_form="A",
                    matched_identity=PRODUCER_PACKAGE,
                )
            ]
        return target_findings
    if alias_target is not None:
        message = "non-alias npm reference contains an alias target"
        raise StaticReferenceProjectionError(message)
    if local_path == PRODUCER_ROOT:
        return [
            _finding(
                candidate,
                context=context,
                prohibited_form="L",
                matched_identity=PRODUCER_ROOT,
            )
        ]
    if name == PRODUCER_PACKAGE:
        return [
            _finding(
                candidate,
                context=context,
                prohibited_form="V",
                matched_identity=PRODUCER_PACKAGE,
            )
        ]
    return []


def _project_npm_manifest(
    candidate: StaticReferenceCandidate,
    facts: tuple[dict[str, JsonValue], ...],
) -> list[StaticReferenceFinding]:
    findings: list[StaticReferenceFinding] = []
    for value in facts:
        fact = _object(value, field="npm fact")
        kind = _string(fact.get("kind"), field="npm fact.kind")
        if kind == "npm-package-name":
            _expect_fields(
                fact,
                {"context", "kind", "name"},
                field="npm package-name fact",
            )
            context = _string(fact["context"], field="npm name context")
            name = _string(fact["name"], field="npm package name")
            if name == PRODUCER_PACKAGE and candidate.path != PRODUCER_MANIFEST:
                findings.append(
                    _finding(
                        candidate,
                        context=context,
                        prohibited_form="D",
                        matched_identity=PRODUCER_PACKAGE,
                    )
                )
            continue
        if kind != "npm-reference":
            message = "npm authority emitted an unknown fact kind"
            raise StaticReferenceProjectionError(message)
        _expect_fields(
            fact,
            {
                "dependencyKey",
                "kind",
                "reference",
                "section",
                "sourceSpec",
            },
            field="npm reference fact",
        )
        dependency_key = _string(
            fact["dependencyKey"],
            field="npm dependency key",
        )
        section = _string(fact["section"], field="npm dependency section")
        _exact_string(fact["sourceSpec"], field="npm source spec")
        context = f"{section}.{dependency_key}"
        key_finding = _dependency_key_finding(
            candidate,
            dependency_key=dependency_key,
            context=context,
        )
        if key_finding is not None:
            findings.append(key_finding)
        findings.extend(
            _npa_reference_findings(
                candidate,
                fact["reference"],
                context=context,
            )
        )
    return findings


def _project_workspace_reference(
    candidate: StaticReferenceCandidate,
    reference_value: object,
    *,
    context: str,
) -> list[StaticReferenceFinding]:
    reference = _object(reference_value, field="workspace reference")
    kind = _string(reference.get("kind"), field="workspace reference.kind")
    if kind == "npm":
        _expect_fields(
            reference,
            {"kind", "npm"},
            field="workspace npm reference",
        )
        return _npa_reference_findings(
            candidate,
            reference["npm"],
            context=context,
        )
    if kind != "workspace":
        message = "workspace authority emitted an unknown reference kind"
        raise StaticReferenceProjectionError(message)
    _expect_fields(
        reference,
        {"kind", "workspace"},
        field="workspace protocol reference",
    )
    workspace = _object(
        reference["workspace"],
        field="workspace reference value",
    )
    _expect_fields(
        workspace,
        {"fetchSpec", "name", "selector", "type"},
        field="workspace reference value",
    )
    _string(workspace["fetchSpec"], field="workspace fetch spec")
    name = _string(workspace["name"], field="workspace package name")
    _exact_string(workspace["selector"], field="workspace selector")
    _string(workspace["type"], field="workspace registry type")
    if name != PRODUCER_PACKAGE:
        return []
    return [
        _finding(
            candidate,
            context=context,
            prohibited_form="W",
            matched_identity=PRODUCER_PACKAGE,
        )
    ]


def _project_pnpm_workspace(
    candidate: StaticReferenceCandidate,
    facts: tuple[dict[str, JsonValue], ...],
) -> list[StaticReferenceFinding]:
    findings: list[StaticReferenceFinding] = []
    for value in facts:
        fact = _object(value, field="pnpm workspace fact")
        kind = _string(
            fact.get("kind"),
            field="pnpm workspace fact.kind",
        )
        if kind == "pnpm-workspace-pattern":
            _expect_fields(
                fact,
                {"index", "kind", "pattern"},
                field="pnpm workspace pattern",
            )
            _integer(fact["index"], field="workspace pattern index")
            _string(fact["pattern"], field="workspace pattern")
            continue
        if kind != "pnpm-workspace-reference":
            message = "pnpm workspace authority emitted an unknown fact kind"
            raise StaticReferenceProjectionError(message)
        _expect_fields(
            fact,
            {
                "catalogKind",
                "catalogName",
                "dependencyKey",
                "kind",
                "reference",
                "sourceSpec",
            },
            field="pnpm workspace reference",
        )
        catalog_kind = _string(
            fact["catalogKind"],
            field="workspace catalog kind",
        )
        catalog_name = _nullable_exact_string(
            fact["catalogName"],
            field="workspace catalog name",
        )
        if (catalog_kind == "default") != (catalog_name is None):
            message = "workspace catalog identity is inconsistent"
            raise StaticReferenceProjectionError(message)
        if catalog_kind not in {"default", "named"}:
            message = "workspace catalog kind is invalid"
            raise StaticReferenceProjectionError(message)
        dependency_key = _string(
            fact["dependencyKey"],
            field="workspace dependency key",
        )
        _exact_string(fact["sourceSpec"], field="workspace source spec")
        catalog_context = (
            "catalog" if catalog_name is None else f"catalogs.{catalog_name}"
        )
        context = f"{catalog_context}.{dependency_key}"
        key_finding = _dependency_key_finding(
            candidate,
            dependency_key=dependency_key,
            context=context,
        )
        if key_finding is not None:
            findings.append(key_finding)
        findings.extend(
            _project_workspace_reference(
                candidate,
                fact["reference"],
                context=context,
            )
        )
    return findings


def _pnpm_resolution(
    value: object,
) -> tuple[str, str | None]:
    resolution = _object(value, field="pnpm lock resolution")
    kind = _string(
        resolution.get("kind"),
        field="pnpm lock resolution.kind",
    )
    if kind in {"directory", "file-tarball"}:
        _expect_fields(
            resolution,
            {"kind", "localPath"},
            field="pnpm local resolution",
        )
        return kind, _local_path(
            resolution["localPath"],
            field="pnpm resolution local path",
        )
    if kind == "git":
        _expect_fields(
            resolution,
            {"commit", "kind", "path", "repo"},
            field="pnpm git resolution",
        )
        _string(resolution["commit"], field="pnpm git commit")
        _nullable_string(resolution["path"], field="pnpm git path")
        _string(resolution["repo"], field="pnpm git repository")
        return kind, None
    if kind == "hosted-git":
        _expect_fields(
            resolution,
            {"kind", "path", "tarball"},
            field="pnpm hosted Git resolution",
        )
        _nullable_string(resolution["path"], field="pnpm hosted Git path")
        _string(resolution["tarball"], field="pnpm hosted Git tarball")
        return kind, None
    if kind == "registry":
        _expect_fields(
            resolution,
            {"kind"},
            field="pnpm registry resolution",
        )
        return kind, None
    message = "pnpm lock resolution kind is invalid"
    raise StaticReferenceProjectionError(message)


def _project_pnpm_snapshot(
    candidate: StaticReferenceCandidate,
    fact: Mapping[str, object],
) -> tuple[list[StaticReferenceFinding], str, _SnapshotProjection]:
    _expect_fields(
        fact,
        {
            "dependencies",
            "dependencyPath",
            "kind",
            "name",
            "nonSemverVersion",
            "registryName",
            "resolution",
            "version",
        },
        field="pnpm lock snapshot",
    )
    dependency_path = _string(
        fact["dependencyPath"],
        field="pnpm dependency path",
    )
    name = _string(fact["name"], field="pnpm snapshot name")
    version = _nullable_string(
        fact["version"],
        field="pnpm snapshot version",
    )
    non_semver_version = _nullable_string(
        fact["nonSemverVersion"],
        field="pnpm snapshot non-semver version",
    )
    if version is None and non_semver_version is None:
        message = "pnpm snapshot has no version identity"
        raise StaticReferenceProjectionError(message)
    _nullable_string(
        fact["registryName"],
        field="pnpm snapshot registry",
    )
    resolution_kind, local_path = _pnpm_resolution(fact["resolution"])
    projection = _SnapshotProjection(name, resolution_kind, local_path)
    context = f"packages.{dependency_path}"
    findings: list[StaticReferenceFinding] = []
    if local_path == PRODUCER_ROOT:
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form="L",
                matched_identity=PRODUCER_ROOT,
            )
        )
    elif name == PRODUCER_PACKAGE:
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form="V",
                matched_identity=PRODUCER_PACKAGE,
            )
        )

    for edge_value in _array(
        fact["dependencies"],
        field="pnpm snapshot dependencies",
    ):
        edge = _object(edge_value, field="pnpm snapshot dependency")
        _expect_fields(
            edge,
            {"dependencyKey", "reference", "section"},
            field="pnpm snapshot dependency",
        )
        dependency_key = _exact_string(
            edge["dependencyKey"],
            field="pnpm snapshot dependency key",
        )
        section = _string(
            edge["section"],
            field="pnpm snapshot dependency section",
        )
        _exact_string(
            edge["reference"],
            field="pnpm snapshot dependency reference",
        )
        key_finding = _dependency_key_finding(
            candidate,
            dependency_key=dependency_key,
            context=f"{context}.{section}.{dependency_key}",
        )
        if key_finding is not None:
            findings.append(key_finding)
    return findings, dependency_path, projection


def _registry_spec(value: object) -> Mapping[str, object] | None:
    if value is None:
        return None
    spec = _object(value, field="pnpm importer registry spec")
    _expect_fields(
        spec,
        {"fetchSpec", "name", "type"},
        field="pnpm importer registry spec",
    )
    _string(spec["fetchSpec"], field="pnpm registry fetch spec")
    _string(spec["name"], field="pnpm registry package name")
    _string(spec["type"], field="pnpm registry spec type")
    return spec


def _project_pnpm_importer(
    candidate: StaticReferenceCandidate,
    fact: Mapping[str, object],
    snapshots: Mapping[str, _SnapshotProjection],
) -> list[StaticReferenceFinding]:
    _expect_fields(
        fact,
        {
            "dependencyKey",
            "importerId",
            "kind",
            "rawSpecifier",
            "registrySpec",
            "resolvedReference",
            "section",
            "snapshotKey",
            "workspaceSelector",
        },
        field="pnpm lock importer reference",
    )
    dependency_key = _string(
        fact["dependencyKey"],
        field="pnpm importer dependency key",
    )
    importer_id = _string(
        fact["importerId"],
        field="pnpm importer ID",
    )
    raw_specifier = _exact_string(
        fact["rawSpecifier"],
        field="pnpm importer raw specifier",
    )
    resolved_reference = _string(
        fact["resolvedReference"],
        field="pnpm importer resolved reference",
    )
    section = _string(fact["section"], field="pnpm importer section")
    snapshot_key = _nullable_string(
        fact["snapshotKey"],
        field="pnpm importer snapshot key",
    )
    workspace_selector = _nullable_exact_string(
        fact["workspaceSelector"],
        field="pnpm importer workspace selector",
    )
    registry_spec = _registry_spec(fact["registrySpec"])
    del raw_specifier, resolved_reference
    context = f"importers.{importer_id}.{section}.{dependency_key}"
    findings: list[StaticReferenceFinding] = []
    key_finding = _dependency_key_finding(
        candidate,
        dependency_key=dependency_key,
        context=context,
    )
    if key_finding is not None:
        findings.append(key_finding)

    registry_name = (
        _string(
            registry_spec["name"],
            field="pnpm registry package name",
        )
        if registry_spec is not None
        else None
    )
    if workspace_selector is not None and registry_name == PRODUCER_PACKAGE:
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form="W",
                matched_identity=PRODUCER_PACKAGE,
            )
        )
    elif registry_name == PRODUCER_PACKAGE:
        prohibited_form = "A" if dependency_key != PRODUCER_PACKAGE else "V"
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form=prohibited_form,
                matched_identity=PRODUCER_PACKAGE,
            )
        )

    if snapshot_key is None:
        return findings
    snapshot = snapshots.get(snapshot_key)
    if snapshot is None:
        message = "pnpm importer references an unknown snapshot"
        raise StaticReferenceProjectionError(message)
    if snapshot.local_path == PRODUCER_ROOT:
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form="L",
                matched_identity=PRODUCER_ROOT,
            )
        )
    elif (
        snapshot.name == PRODUCER_PACKAGE and registry_name != PRODUCER_PACKAGE
    ):
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form=(
                    "A" if dependency_key != PRODUCER_PACKAGE else "V"
                ),
                matched_identity=PRODUCER_PACKAGE,
            )
        )
    return findings


def _project_pnpm_lock(
    candidate: StaticReferenceCandidate,
    facts: tuple[dict[str, JsonValue], ...],
) -> list[StaticReferenceFinding]:
    findings: list[StaticReferenceFinding] = []
    snapshots: dict[str, _SnapshotProjection] = {}
    importer_facts: list[Mapping[str, object]] = []
    for value in facts:
        fact = _object(value, field="pnpm lock fact")
        kind = _string(fact.get("kind"), field="pnpm lock fact.kind")
        if kind == "pnpm-lock-snapshot":
            snapshot_findings, dependency_path, projection = (
                _project_pnpm_snapshot(candidate, fact)
            )
            if dependency_path in snapshots:
                message = "pnpm authority emitted a duplicate snapshot"
                raise StaticReferenceProjectionError(message)
            snapshots[dependency_path] = projection
            findings.extend(snapshot_findings)
        elif kind == "pnpm-lock-importer-reference":
            importer_facts.append(fact)
        else:
            message = "pnpm lock authority emitted an unknown fact kind"
            raise StaticReferenceProjectionError(message)

    for fact in importer_facts:
        findings.extend(
            _project_pnpm_importer(
                candidate,
                fact,
                snapshots,
            )
        )
    return findings


def _nuget_identity_findings(
    candidate: StaticReferenceCandidate,
    *,
    identity: str,
    context: str,
    dependency_key: bool,
    versioned: bool,
) -> list[StaticReferenceFinding]:
    if not identity.isascii() or identity.lower() != PRODUCER_PACKAGE:
        return []
    findings: list[StaticReferenceFinding] = []
    if dependency_key:
        findings.append(
            _finding(
                candidate,
                context=context,
                prohibited_form="dependency-key",
                matched_identity=PRODUCER_PACKAGE,
            )
        )
    findings.append(
        _finding(
            candidate,
            context=context,
            prohibited_form="V" if versioned else "D",
            matched_identity=PRODUCER_PACKAGE,
        )
    )
    return findings


def _project_nuget_lock_fact(
    candidate: StaticReferenceCandidate,
    fact: Mapping[str, object],
) -> list[StaticReferenceFinding]:
    _expect_fields(
        fact,
        {
            "dependencies",
            "dependencyType",
            "id",
            "kind",
            "requestedRange",
            "resolvedVersion",
            "target",
        },
        field="NuGet lock dependency",
    )
    target = _string(fact["target"], field="NuGet target")
    identity = _string(fact["id"], field="NuGet dependency ID")
    _string(fact["dependencyType"], field="NuGet dependency type")
    requested_range = _nullable_string(
        fact["requestedRange"],
        field="NuGet requested range",
    )
    resolved_version = _nullable_string(
        fact["resolvedVersion"],
        field="NuGet resolved version",
    )
    context = f"targets.{target}.{identity}"
    findings = _nuget_identity_findings(
        candidate,
        identity=identity,
        context=context,
        dependency_key=True,
        versioned=requested_range is not None or resolved_version is not None,
    )
    for edge_value in _array(
        fact["dependencies"],
        field="NuGet dependency edges",
    ):
        edge = _object(edge_value, field="NuGet dependency edge")
        _expect_fields(
            edge,
            {"id", "requestedRange"},
            field="NuGet dependency edge",
        )
        edge_id = _string(edge["id"], field="NuGet edge ID")
        edge_requested_range = _nullable_string(
            edge["requestedRange"],
            field="NuGet edge requested range",
        )
        findings.extend(
            _nuget_identity_findings(
                candidate,
                identity=edge_id,
                context=f"{context}.dependencies.{edge_id}",
                dependency_key=True,
                versioned=edge_requested_range is not None,
            )
        )
    return findings


def _project_nuget(
    candidate: StaticReferenceCandidate,
    facts: tuple[dict[str, JsonValue], ...],
) -> list[StaticReferenceFinding]:
    findings: list[StaticReferenceFinding] = []
    for value in facts:
        fact = _object(value, field="NuGet fact")
        kind = _string(fact.get("kind"), field="NuGet fact.kind")
        if kind == "nuget-lock-dependency":
            if candidate.selection.family != "nuget-lock":
                message = "NuGet lock fact does not match the selector"
                raise StaticReferenceProjectionError(message)
            findings.extend(_project_nuget_lock_fact(candidate, fact))
            continue
        if kind != "nuget-packages-config-entry":
            message = "NuGet authority emitted an unknown fact kind"
            raise StaticReferenceProjectionError(message)
        if candidate.selection.family != "nuget-packages-config":
            message = "packages.config fact does not match the selector"
            raise StaticReferenceProjectionError(message)
        _expect_fields(
            fact,
            {"id", "kind", "version"},
            field="NuGet packages.config entry",
        )
        identity = _string(fact["id"], field="NuGet package ID")
        _string(fact["version"], field="NuGet package version")
        findings.extend(
            _nuget_identity_findings(
                candidate,
                identity=identity,
                context=f"packages.{identity}",
                dependency_key=False,
                versioned=True,
            )
        )
    return findings


def project_static_reference_facts(
    candidate: StaticReferenceCandidate,
    facts: tuple[dict[str, JsonValue], ...],
) -> tuple[StaticReferenceFinding, ...]:
    """Project one complete graph result into bounded policy findings."""
    if candidate.selection.family == "npm-manifest":
        findings = _project_npm_manifest(candidate, facts)
    elif candidate.selection.family == "pnpm-workspace":
        findings = _project_pnpm_workspace(candidate, facts)
    elif candidate.selection.family == "pnpm-lock":
        findings = _project_pnpm_lock(candidate, facts)
    else:
        findings = _project_nuget(candidate, facts)
    return tuple(
        sorted(
            set(findings),
            key=StaticReferenceFinding.sort_key,
        )
    )


__all__ = [
    "StaticReferenceProjectionError",
    "project_static_reference_facts",
]
