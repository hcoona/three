"""Original capture closure and fixed native-state deltas, without effects.

Supplied records do not authenticate their producer. The operator owns source,
runtime and service provenance; independent native admission remains separate.
NuGet identity interpretation belongs to the existing official helper.
"""

from __future__ import annotations

import hashlib
import math
import re
from dataclasses import dataclass
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, cast
from urllib.parse import quote, urlsplit

from three_workflow_delivery_v3.adapters import nuget_github_packages as native
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


def read_capture_evidence(  # noqa: C901, PLR0915 - close one ordered transcript
    directory: Path, request: NuGetCaptureRequest
) -> NuGetStateEvidence:
    """Require complete originals before exposing facts for delta checks."""
    _require(
        not (directory / "failure.json").exists(), "capture retained failure"
    )
    body = _read(directory, "capture.json", _MANIFEST_LIMIT)
    document = _object(parse_canonical_json(body))
    _require(
        document.get("schema") == "workflow-delivery/v3/nuget-active-capture-v2"
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
    expected_files = {"request.json", "reader-runtime.json", "requests.jsonl"}
    package_record = document["scenarioPackage"]
    coordinate = document["coordinate"]
    _require(type(coordinate) is str, "missing capture coordinate")
    parts = cast("str", coordinate).split("@")
    _require(
        len(parts) == 2  # noqa: PLR2004 - coordinate has exactly two components
        and parts[0] == native.NUGET_PACKAGE_ID.lower()
        and bool(parts[1]),
        "invalid capture coordinate",
    )
    resources = _object(document["resources"])
    admitted_resources = native.nuget_service_resources_from_projection(
        {
            "packageBaseAddress": resources["packageBaseAddress"],
            "packagePublish": resources["packagePublish"],
        },
        index_sha256=cast("str", resources["serviceIndexSha256"]),
    )
    root = (
        admitted_resources.package_base_address + quote(parts[0], safe="") + "/"
    )
    package_url = (
        root
        + quote(parts[1], safe="")
        + "/"
        + quote(parts[0] + "." + parts[1] + ".nupkg", safe="")
    )
    storage_seen = False
    for index, response in enumerate(responses, 1):
        name = f"response-{index:03d}"
        expected_files.add(name + ".json")
        _require(
            set(response)
            == {
                "request",
                "requestedUrl",
                "responseUrl",
                "status",
                "headers",
                "body",
                "bodyRetention",
                "bodyOmissionReason",
                "bodyBytesRead",
                "bodySha256",
                "locationSha256",
                "storageHop",
            }
            and type(response["status"]) is int,
            "capture response shape mismatch",
        )
        for header in _array(response["headers"]):
            pair = _array(header)
            _require(
                len(pair) == 2  # noqa: PLR2004 - HTTP header name/value pair
                and all(type(value) is str for value in pair)
                and pair[0]
                in {
                    "cache-control",
                    "content-encoding",
                    "content-length",
                    "content-type",
                    "date",
                    "etag",
                    "last-modified",
                    "link",
                    "retry-after",
                    "x-github-request-id",
                },
                "capture response contains unselected headers",
            )
        hop = response.get("storageHop")
        redirected = response.get("status") in (301, 302)
        if hop is not None:
            relation = _object(hop)
            previous = responses[index - 2] if index > 1 else {}
            origin = relation.get("origin")
            _require(
                not storage_seen
                and index == len(responses)
                and package_record is not None
                and previous.get("status") in (301, 302)
                and previous.get("requestedUrl") == package_url
                and previous.get("storageHop") is None
                and response.get("status") == HTTPStatus.OK
                and type(origin) is str
                and native.read_redirect_origin(cast("str", origin)) == origin
                and relation
                == {
                    "sourceRequest": index - 1,
                    "sourceUrl": package_url,
                    "origin": origin,
                    "locationSha256": previous.get("locationSha256"),
                }
                and response.get("locationSha256")
                == previous.get("locationSha256"),
                "capture storage hop source mismatch",
            )
            storage_seen = True
        if redirected:
            digest = response.get("locationSha256")
            _require(
                package_record is not None
                and index == len(responses) - 1
                and response.get("requestedUrl") == package_url
                and hop is None
                and type(digest) is str
                and re.fullmatch(r"[0-9a-f]{64}", cast("str", digest))
                is not None,
                "capture has an orphan or unselected redirect",
            )
        _require(
            response.get("request") == index
            and (response.get("status") == HTTPStatus.OK or redirected)
            and response.get("requestedUrl") == response.get("responseUrl")
            and response.get("body") == (None if redirected else name + ".body")
            and response.get("bodyRetention")
            == ("omitted" if redirected else "original")
            and response.get("bodyOmissionReason")
            == ("location" if redirected else None)
            and originals.get(name + ".json") == canonicalize(response),
            "capture response binding mismatch",
        )
        size = response.get("bodyBytesRead")
        _require(type(size) is int and size >= 0, "invalid response byte count")
        if not redirected:
            expected_files.add(name + ".body")
            content = originals.get(name + ".body")
            _require(
                content is not None
                and len(content) == size
                and response.get("bodySha256") == _sha(content),
                "capture original response body mismatch",
            )
        else:
            _require(
                name + ".body" not in originals
                and type(response.get("bodySha256")) is str
                and re.fullmatch(
                    r"[0-9a-f]{64}", cast("str", response["bodySha256"])
                )
                is not None,
                "capture redirect omission mismatch",
            )
        before = returned
        returned += cast("int", size)
        url = response["requestedUrl"]
        _require(type(url) is str, "missing capture request URL")
        pages += int(urlsplit(cast("str", url)).path.endswith("/versions"))
        reserved, completed = journal[2 * index - 2 : 2 * index]
        _require(
            reserved.get("event") == "reserved"
            and reserved.get("request") == index
            and reserved.get("method") == "GET"
            and reserved.get("url") == url
            and reserved.get("storageHop") == hop
            and completed.get("event") == "returned"
            and completed.get("request") == index,
            "capture has an outstanding or substituted request",
        )
        bound, timeout = (
            reserved.get("maximumBodyBytes"),
            reserved.get("socketTimeoutSeconds"),
        )
        _require(
            type(bound) is int
            and bound > 0
            and cast("int", size) <= bound
            and before + bound + 1 <= request.limits.response_bytes
            and type(timeout) in (int, float)
            and math.isfinite(cast("float", timeout))
            and 0
            < cast("float", timeout)
            <= request.limits.socket_timeout_seconds
            and reserved.get("counts")
            == {
                "requests": index,
                "versionPages": pages,
                "chargedResponseBytes": before + bound + 1,
                "returnedResponseBytes": before,
            }
            and completed.get("counts")
            == {
                "requests": index,
                "versionPages": pages,
                "chargedResponseBytes": returned,
                "returnedResponseBytes": returned,
            },
            "capture per-call accounting mismatch",
        )
    control_url = "https://api.github.com/users/hcoona/packages/nuget/" + quote(
        native.NUGET_PACKAGE_ID, safe=""
    )
    expected_urls = [
        native.NUGET_SERVICE_INDEX,
        control_url,
        *(
            control_url + f"/versions?state=active&per_page=100&page={page}"
            for page in range(1, pages + 1)
        ),
        root + "index.json",
    ]
    if package_record is not None:
        expected_urls.append(package_url)
        if storage_seen:
            expected_urls.append(package_url)
    _require(
        [response["requestedUrl"] for response in responses] == expected_urls,
        "capture request sequence differs from its selected subject",
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
        expected_files.add("scenario-witness.json")
        facts = _object(record["nativeFacts"])
        identity = _object(facts["identity"])
        _require(
            record.get("body") == responses[-1]["body"]
            and responses[-1].get("status") == HTTPStatus.OK
            and record.get("witness") == "scenario-witness.json"
            and identity.get("normalizedPackageId") == parts[0]
            and identity.get("normalizedVersion") == parts[1],
            "capture scenario is not the terminal package response",
        )
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
    _require(
        set(originals) == expected_files
        and {path.name for path in directory.iterdir()}
        == expected_files | {"capture.json"},
        "capture file inventory is incomplete or unmatched",
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
