"""Bind original probe uploads to supplied current GitHub run metadata.

The concrete operator owns authenticated collection and retention. This reader
does not authenticate its caller, invoke a helper, or admit native evidence.
It preserves duplicate rejection as a failed publication invocation.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    _array,
    _object,
    _read,
    _require,
    _sha,
)
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    _archive_files,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference

if TYPE_CHECKING:
    from collections.abc import Mapping
    from pathlib import Path

_ROLES = ("prepared", "prepare", "publish")


@dataclass(frozen=True)
class NuGetProbeEvidence:
    """One expected response and its lineage, without suite authority."""

    request: probe.NuGetProbeRequest
    artifacts: tuple[ArtifactReference, ...]
    raw_run_sha256: str
    raw_artifacts_sha256: str
    result_sha256: str
    status: int
    http_created: bool
    possibly_mutated: bool
    response_body: bytes


def _matches(
    actual: dict[str, JsonValue], expected: dict[str, JsonValue]
) -> None:
    _require(
        all(
            type(actual.get(key)) is type(value) and actual[key] == value
            for key, value in expected.items()
        ),
        "probe service or terminal binding mismatch",
    )


def read_probe_evidence(  # noqa: PLR0915
    directory: Path,
    request: probe.NuGetProbeRequest,
    *,
    raw_run_metadata: bytes,
    raw_artifact_metadata: bytes,
    maximum_artifact_bytes: int,
) -> NuGetProbeEvidence:
    """Admit the three original envelopes and the expected definitive outcome.

    Metadata arguments are unmodified responses for this run and its complete
    artifact list. Body files use the exact raw-upload names in that list.
    No unpacked-directory hash is substituted for a service artifact digest.
    """
    _require(
        type(maximum_artifact_bytes) is int and maximum_artifact_bytes > 0,
        "missing probe artifact byte allowance",
    )
    run = _object(parse_json_strict(raw_run_metadata))
    _matches(
        run,
        {
            "id": request.workflow_run_id,
            "run_attempt": 1,
            "head_sha": request.tooling_sha,
            "head_branch": "main",
            "event": "workflow_dispatch",
            "path": probe.WORKFLOW_PATH,
            "status": "completed",
            "conclusion": "success",
        },
    )
    for field in ("repository", "head_repository"):
        _matches(
            _object(run[field]), {"id": 1102295886, "full_name": "hcoona/three"}
        )
    for field in ("actor", "triggering_actor"):
        _matches(_object(run[field]), {"id": 712433})
    listing = _object(parse_json_strict(raw_artifact_metadata))
    items = [_object(item) for item in _array(listing["artifacts"])]
    _require(
        listing.get("total_count") == len(items) == len(_ROLES),
        "probe requires its complete three-artifact inventory",
    )
    envelopes: dict[str, dict[str, bytes]] = {}
    references: dict[str, ArtifactReference] = {}
    identities: set[int] = set()
    for item in items:
        identity, name, digest = (
            item.get("id"),
            item.get("name"),
            item.get("digest"),
        )
        _require(
            type(identity) is int
            and identity > 0
            and identity not in identities
            and type(name) is str
            and type(digest) is str
            and re.fullmatch(r"sha256:[0-9a-f]{64}", digest) is not None,
            "invalid probe artifact identity",
        )
        identity = cast("int", identity)
        name, digest = cast("str", name), cast("str", digest)
        identities.add(identity)
        run_id = request.workflow_run_id
        names = {
            "prepared": f"wdv3-nuget-probe-input-r{run_id}-{digest[7:]}.zip",
            "prepare": f"wdv3-nuget-probe-prepare-diagnostics-r{run_id}.zip",
            "publish": f"wdv3-nuget-probe-publish-diagnostics-r{run_id}.zip",
        }
        roles = [role for role, expected in names.items() if name == expected]
        _require(
            len(roles) == 1 and roles[0] not in references,
            "unexpected probe artifact",
        )
        role = roles[0]
        api = f"https://api.github.com/repos/hcoona/three/actions/artifacts/{identity}"
        _matches(
            item,
            {
                "expired": False,
                "url": api,
                "archive_download_url": api + "/zip",
            },
        )
        _matches(
            _object(item["workflow_run"]),
            {
                "id": request.workflow_run_id,
                "repository_id": 1102295886,
                "head_repository_id": 1102295886,
                "head_branch": "main",
                "head_sha": request.tooling_sha,
            },
        )
        body = _read(directory, name, maximum_artifact_bytes)
        _matches(
            item, {"size_in_bytes": len(body), "digest": "sha256:" + _sha(body)}
        )
        references[role] = ArtifactReference(
            identity,
            digest,
            f"https://github.com/hcoona/three/actions/runs/{request.workflow_run_id}/artifacts/{identity}",
            name,
            digest,
        )
        envelopes[role] = _archive_files(body)
    _require(
        set(references) == set(_ROLES),
        "missing probe uploads",
    )
    preparation, publication = envelopes["prepare"], envelopes["publish"]
    _require(
        not any(
            name == "failure.json" or name.endswith("/failure.json")
            for files in envelopes.values()
            for name in files
        ),
        "probe retained a failed stage",
    )
    prepare_platform = _object(parse_json_strict(preparation["platform.json"]))
    publish_platform = _object(parse_json_strict(publication["platform.json"]))
    for platform in (prepare_platform, publish_platform):
        probe.admit_probe_platform(request, cast("Mapping[str, str]", platform))
    _require(
        probe.bind_probe_spec(
            preparation["spec.json"],
            cast("Mapping[str, str]", prepare_platform),
        )
        == request
        and preparation["request.json"] == canonicalize(request.to_document()),
        "probe prospective specification or request changed",
    )
    prepared_reference = references["prepared"]
    admitted, package, resources = probe.read_prepared_probe(
        directory / prepared_reference.payload_path,
        prepared_reference,
        cast("Mapping[str, str]", publish_platform),
    )
    _require(admitted == request, "prepared probe request changed")
    _require(
        _object(parse_json_strict(publication["reference.json"]))
        == prepared_reference.to_document()
        and publication["publish/prepared-reference.json"]
        == canonicalize(prepared_reference.to_document())
        and publication["publish/request.json"]
        == canonicalize(request.to_document()),
        "publisher original input reference changed",
    )
    marker = _object(
        parse_canonical_json(publication["publish/invocation-started.json"])
    )
    profile_digest = canonical_sha256(request.profile)
    _matches(
        marker,
        {
            "schema": "workflow-delivery/v3/nuget-probe-invocation-marker",
            "requestDigest": request.request_digest,
            "workflowRunId": request.workflow_run_id,
            "runAttempt": 1,
            "profileDigest": profile_digest,
            "packageSha256": _sha(package),
            "preparedReference": prepared_reference.to_document(),
            "mutationMayHaveStarted": True,
            "retryPermitted": False,
        },
    )
    result_body = publication["publish/result.json"]
    result = _object(parse_canonical_json(result_body))
    created = request.scenario == "create"
    status = 201 if created else 409
    _matches(
        result,
        {
            "schema": probe.RESULT_SCHEMA,
            "requestDigest": request.request_digest,
            "workflowRunId": request.workflow_run_id,
            "runAttempt": 1,
            "scenario": request.scenario,
            "profileDigest": profile_digest,
            "packageSha256": _sha(package),
            "transportError": False,
            "httpCreated": created,
            "possiblyMutated": not created,
            "invocations": 1,
            "requestSpent": True,
            "retryPermitted": False,
        },
    )
    request_body_digest = result.get("requestBodySha256")
    elapsed = result.get("elapsedSeconds")
    _require(
        type(request_body_digest) is str
        and re.fullmatch(r"[0-9a-f]{64}", request_body_digest) is not None
        and type(elapsed) in (int, float)
        and math.isfinite(cast("float", elapsed))
        and cast("float", elapsed) >= 0,
        "probe invocation facts are incomplete",
    )
    response_body = publication["publish/response.body"]
    response = _object(
        parse_canonical_json(publication["publish/response.json"])
    )
    _matches(
        response,
        {
            "url": resources.package_publish,
            "status": status,
            "body": "response.body",
            "bodySha256": _sha(response_body),
            "bodyBytes": len(response_body),
        },
    )
    _require(
        result.get("response") == response, "probe original response changed"
    )
    return NuGetProbeEvidence(
        request,
        tuple(references[role] for role in _ROLES),
        _sha(raw_run_metadata),
        _sha(raw_artifact_metadata),
        _sha(result_body),
        status,
        created,
        not created,
        response_body,
    )
