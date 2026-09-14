"""Original capture closure and fixed native-state deltas, without effects.

Supplied records do not authenticate their producer. The operator owns source,
runtime and service provenance; independent native admission remains separate.
NuGet identity interpretation belongs to the existing official helper.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, cast
from urllib.parse import urlsplit

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.acceptance.nuget_capture import (
        NuGetCaptureRequest,
    )

_MANIFEST_LIMIT = 8 * 1024 * 1024


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    _require(type(value) is dict, "expected NuGet evidence object")
    return cast("dict[str, JsonValue]", value)


def _array(value: JsonValue) -> list[JsonValue]:
    _require(type(value) is list, "expected NuGet evidence array")
    return cast("list[JsonValue]", value)


def _sha(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _read(directory: Path, name: str, maximum: int) -> bytes:
    path = directory / name
    _require(
        bool(name)
        and Path(name).name == name
        and name not in {".", ".."}
        and "\\" not in name
        and ":" not in name
        and path.is_file()
        and not path.is_symlink(),
        "missing or unsafe NuGet evidence file",
    )
    with path.open("rb") as stream:
        body = stream.read(maximum + 1)
    _require(len(body) <= maximum, "NuGet evidence file exceeds its bound")
    return body


@dataclass(frozen=True)
class NuGetStateEvidence:
    """Byte-bound supplied facts; not an authenticated native admission."""

    capture_sha256: str
    coordinate: str
    objects: tuple[tuple[int, str], ...]
    resources: dict[str, JsonValue]
    control: tuple[JsonValue, ...]
    package: bytes | None
    witness: bytes | None


def read_capture_evidence(  # noqa: PLR0915
    directory: Path, request: NuGetCaptureRequest
) -> NuGetStateEvidence:
    """Require complete originals before exposing facts for delta checks."""
    _require(
        not (directory / "failure.json").exists(), "capture retained failure"
    )
    body = _read(directory, "capture.json", _MANIFEST_LIMIT)
    document = _object(parse_canonical_json(body))
    _require(
        document.get("schema") == "workflow-delivery/v3/nuget-active-capture"
        and document.get("requestDigest")
        == canonical_sha256(request.to_document()),
        "capture request binding mismatch",
    )
    files = _object(document["files"])
    _require(
        len(files) <= 2 * request.limits.requests + 4,
        "capture file inventory exceeds its request bound",
    )
    originals: dict[str, bytes] = {}
    for name, value in files.items():
        reference = _object(value)
        size = reference.get("bytes")
        _require(type(size) is int and size >= 0, "invalid evidence size")
        size = cast("int", size)
        maximum = (
            request.limits.response_bytes
            if name.endswith(".body")
            else _MANIFEST_LIMIT
        )
        _require(size <= maximum, "evidence exceeds the admitted size")
        original = _read(directory, name, cast("int", size))
        _require(
            reference == {"bytes": len(original), "sha256": _sha(original)},
            "capture original bytes changed",
        )
        originals[name] = original
    _require(
        originals.get("request.json") == canonicalize(request.to_document())
        and "reader-runtime.json" in originals
        and "requests.jsonl" in originals,
        "capture is missing its original request or runtime evidence",
    )
    responses = [_object(item) for item in _array(document["responses"])]
    journal = [
        _object(parse_canonical_json(line))
        for line in originals["requests.jsonl"].splitlines()
    ]
    _require(
        0 < len(responses) <= request.limits.requests
        and len(journal) == 2 * len(responses),
        "capture request journal is incomplete",
    )
    returned = 0
    pages = 0
    for index, response in enumerate(responses, 1):
        name = f"response-{index:03d}"
        _require(
            response.get("request") == index
            and response.get("status") == HTTPStatus.OK
            and response.get("requestedUrl") == response.get("responseUrl")
            and response.get("body") == name + ".body"
            and originals.get(name + ".json") == canonicalize(response),
            "capture response binding mismatch",
        )
        returned += len(originals[name + ".body"])
        url = response["requestedUrl"]
        _require(type(url) is str, "missing capture request URL")
        pages += int(urlsplit(cast("str", url)).path.endswith("/versions"))
        reserved, completed = journal[2 * index - 2 : 2 * index]
        _require(
            reserved.get("event") == "reserved"
            and reserved.get("request") == index
            and reserved.get("method") == "GET"
            and reserved.get("url") == url
            and completed.get("event") == "returned"
            and completed.get("request") == index,
            "capture has an outstanding or substituted request",
        )
    counts = {
        "requests": len(responses),
        "versionPages": pages,
        "chargedResponseBytes": returned,
        "returnedResponseBytes": returned,
    }
    _require(
        document.get("counts") == counts
        and journal[-1].get("counts") == counts
        and pages <= request.limits.version_pages
        and returned <= request.limits.response_bytes,
        "capture accounting is incomplete or exceeds its request",
    )
    control = _object(document["packageControl"])
    repository = _object(control["repository"])
    _require(
        control.get("id") == request.container_id
        and repository.get("full_name") == "hcoona/three",
        "capture container changed",
    )
    objects: dict[int, str] = {}
    for item in _array(document["githubCoordinates"]):
        entry = _object(item)
        identity, coordinate = entry.get("id"), entry.get("coordinate")
        _require(
            set(entry) == {"id", "coordinate"}
            and type(identity) is int
            and identity > 0
            and identity not in objects
            and type(coordinate) is str
            and bool(coordinate),
            "invalid GitHub coordinate association",
        )
        objects[cast("int", identity)] = cast("str", coordinate)
    raw_objects = [_object(item) for item in _array(document["githubVersions"])]
    _require(
        len(raw_objects) == len(objects)
        and {item.get("id") for item in raw_objects} == set(objects)
        and len(set(objects.values())) == len(objects)
        and document.get("activeCoordinates") == sorted(objects.values()),
        "capture native and GitHub object inventories disagree",
    )
    coordinate = document["coordinate"]
    _require(type(coordinate) is str, "missing capture coordinate")
    package = witness = None
    scenario = document["scenarioPackage"]
    if scenario is not None:
        record = _object(scenario)
        package = originals[cast("str", record["body"])]
        witness = originals[cast("str", record["witness"])]
        _require(
            coordinate in objects.values()
            and record.get("sha256") == _sha(package)
            and record.get("sha512") == hashlib.sha512(package).hexdigest()
            and record.get("witnessSha256") == _sha(witness),
            "capture scenario bytes or coordinate mismatch",
        )
    else:
        _require(
            coordinate not in objects.values(), "capture omitted scenario bytes"
        )
    return NuGetStateEvidence(
        _sha(body),
        cast("str", coordinate),
        tuple(sorted(objects.items())),
        _object(document["resources"]),
        (
            control["id"],
            control["name"],
            control["package_type"],
            control["visibility"],
            repository["full_name"],
        ),
        package,
        witness,
    )


def _same_subject(
    before: NuGetStateEvidence, after: NuGetStateEvidence
) -> None:
    _require(
        before.coordinate == after.coordinate
        and before.resources == after.resources
        and before.control == after.control,
        "NuGet capture subject or resources changed",
    )


def require_creation_delta(
    before: NuGetStateEvidence,
    after: NuGetStateEvidence,
    *,
    original: bytes,
    witness: bytes,
) -> int:
    """Admit exactly one newly active object with the original A payload."""
    _same_subject(before, after)
    previous, current = dict(before.objects), dict(after.objects)
    added = set(current) - set(previous)
    _require(
        before.package is None
        and before.coordinate not in previous.values()
        and len(added) == 1
        and len(current) == len(previous) + 1
        and all(current.get(key) == value for key, value in previous.items())
        and current[next(iter(added))] == after.coordinate
        and after.package == original
        and after.witness == witness,
        "creation changed more than the allowed NuGet object or bytes",
    )
    return next(iter(added))


def require_unchanged_delta(
    before: NuGetStateEvidence,
    after: NuGetStateEvidence,
    *,
    original: bytes,
    witness: bytes,
) -> None:
    """Require stable declared objects and exact A bytes on both sides."""
    _same_subject(before, after)
    _require(
        before.objects == after.objects
        and before.package == after.package == original
        and before.witness == after.witness == witness,
        "NuGet declared active state or original bytes changed",
    )
