"""Capture scenarios with controlled readers; no native destination access."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import base64
import hashlib
import json
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path
from unittest.mock import Mock
from urllib.parse import quote

import pytest
from three_workflow_delivery_v3.acceptance.nuget_capture import (
    NuGetCaptureLimits,
    NuGetCaptureRequest,
    capture_nuget_state,
)
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)

PACKAGE = native.NUGET_PACKAGE_ID
VERSION = "2.0.0-beta.1"
TOKEN = "test-only-read-credential"  # noqa: S105
ARCHIVE = b"PK\x03\x04controlled-original-package\x00\xff\r\n"
WITNESS = (
    b'{"purpose":"destination-acceptance",'
    b'"target":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}'
)
BASE = "https://nuget.pkg.github.com/hcoona/download/"
CONTROL = f"https://api.github.com/users/hcoona/packages/nuget/{PACKAGE}"
PAGE = CONTROL + "/versions?state=active&per_page=100&page="
FLAT = BASE + PACKAGE.lower() + "/index.json"
ARCHIVE_URL = (
    BASE
    + PACKAGE.lower()
    + "/"
    + VERSION
    + "/"
    + PACKAGE.lower()
    + "."
    + VERSION
    + ".nupkg"
)
NOW = datetime(2026, 9, 12, tzinfo=UTC)


def _identity(package_id, version):
    return {
        "displayPackageId": package_id,
        "displayVersion": version,
        "normalizedPackageId": PACKAGE.lower(),
        "normalizedVersion": version,
    }


def _response(url, document=None, *, body=None, status=200):
    return native.NuGetHttpResponse(
        url,
        status,
        (("content-type", "application/json"),),
        body if body is not None else json.dumps(document).encode(),
    )


@pytest.fixture
def scenario():
    authority = Mock(spec=native.NuGetAuthority)
    authority.normalize_identity.side_effect = _identity
    authority.service_resources.return_value = {
        "packageBaseAddress": BASE,
        "packagePublish": "https://nuget.pkg.github.com/hcoona/",
    }
    authority.inspect_package.return_value = {
        "identity": _identity(PACKAGE, VERSION),
        "witnessBase64": base64.b64encode(WITNESS).decode(),
        "entries": [
            "lib/net10.0/Smoke.dll",
            "workflow-delivery/provenance.json",
        ],
    }
    responses = {
        native.NUGET_SERVICE_INDEX: _response(
            native.NUGET_SERVICE_INDEX, {"resources": []}
        ),
        CONTROL: _response(
            CONTROL,
            {
                "id": 12024661,
                "name": PACKAGE,
                "package_type": "nuget",
                "visibility": "public",
                "repository": {"full_name": "hcoona/three"},
            },
        ),
        PAGE + "1": _response(PAGE + "1", [{"id": 71, "name": VERSION}]),
        FLAT: _response(FLAT, {"versions": [VERSION]}),
        ARCHIVE_URL: _response(ARCHIVE_URL, body=ARCHIVE),
    }
    transport = Mock(spec=native.NuGetReadTransport)
    transport.get.side_effect = lambda url, **_kwargs: responses[url]
    return authority, transport, responses


@pytest.fixture
def capture_request():
    return NuGetCaptureRequest(
        "controlled-generation",
        "before-create",
        "a" * 40,
        "b" * 64,
        12024661,
        VERSION,
        NuGetCaptureLimits(10, 3, 1_000_000, 12, 60),
    )


def _capture(tmp_path, capture_request, scenario, **kwargs):
    authority, transport, _responses = scenario
    return capture_nuget_state(
        capture_request,
        authority=authority,
        transport=transport,
        token=TOKEN,
        audit_directory=tmp_path / "capture",
        clock=lambda: NOW,
        **kwargs,
    )


def _read(path):
    return parse_canonical_json(path.read_bytes())


@pytest.mark.parametrize("present", [False, True])
def test_capture_retains_complete_state_and_actual_scenario_bytes(
    tmp_path, capture_request, scenario, present
):
    authority, transport, responses = scenario
    if not present:
        responses[PAGE + "1"] = _response(PAGE + "1", [])
        responses[FLAT] = _response(FLAT, {"versions": []})
    output = _capture(tmp_path, capture_request, scenario)
    document = _read(output)
    assert document["requestDigest"] == canonical_sha256(
        capture_request.to_document()
    )
    assert document["coordinate"] == PACKAGE.lower() + "@" + VERSION
    assert document["activeCoordinates"] == (
        [PACKAGE.lower() + "@" + VERSION] if present else []
    )
    assert document["githubVersions"] == (
        [{"id": 71, "name": VERSION}] if present else []
    )
    assert document["packageControl"]["id"] == 12024661
    assert document["startedAt"] == document["completedAt"] == NOW.isoformat()
    urls = [call.args[0] for call in transport.get.call_args_list]
    assert urls == list(responses)[: 5 if present else 4]
    returned_bytes = 0
    for exchange in document["responses"]:
        expected = responses[exchange["requestedUrl"]]
        assert (output.parent / exchange["body"]).read_bytes() == expected.body
        assert exchange["responseUrl"] == expected.url
        returned_bytes += len(expected.body)
    assert document["counts"] == {
        "requests": 5 if present else 4,
        "versionPages": 1,
        "chargedResponseBytes": returned_bytes,
        "returnedResponseBytes": returned_bytes,
    }
    if present:
        package = document["scenarioPackage"]
        assert (output.parent / package["body"]).read_bytes() == ARCHIVE
        assert package["sha256"] == hashlib.sha256(ARCHIVE).hexdigest()
        assert package["sha512"] == hashlib.sha512(ARCHIVE).hexdigest()
        assert (output.parent / package["witness"]).read_bytes() == WITNESS
        assert package["witnessSha256"] == hashlib.sha256(WITNESS).hexdigest()
        authority.inspect_package.assert_called_once_with(ARCHIVE)
    else:
        assert document["scenarioPackage"] is None
        authority.inspect_package.assert_not_called()
    for name, reference in document["files"].items():
        body = (output.parent / name).read_bytes()
        assert reference == {
            "bytes": len(body),
            "sha256": hashlib.sha256(body).hexdigest(),
        }
    runtime = _read(output.parent / "reader-runtime.json")
    assert (
        runtime["kind"]
        == "local-capture-runtime; not publication-profile admission"
    )
    assert "independent native audit required" in document["evidenceLevel"]
    assert (
        runtime["adapterSourceSha256"]
        == hashlib.sha256(Path(native.__file__).read_bytes()).hexdigest()
    )
    assert not (output.parent / "failure.json").exists()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("generation", "../reuse"),
        ("label", ""),
        ("tooling_sha", "main"),
        ("helper_runtime_sha256", "unknown"),
        ("container_id", True),
        ("container_id", -1),
        ("version", ""),
        ("limits", {}),
    ],
)
def test_capture_rejects_invalid_requests_before_reads(
    capture_request, scenario, field, value
):
    with pytest.raises(ValueError, match=r"invalid|requires exact|missing"):
        replace(capture_request, **{field: value})
    scenario[1].get.assert_not_called()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("requests", 0),
        ("version_pages", True),
        ("response_bytes", -1),
        ("socket_timeout_seconds", float("nan")),
        ("completion_timeout_seconds", float("inf")),
        ("completion_timeout_seconds", 0),
        ("socket_timeout_seconds", True),
    ],
)
def test_capture_rejects_invalid_limits(capture_request, field, value):
    with pytest.raises(ValueError, match="invalid capture"):
        replace(capture_request.limits, **{field: value})


def test_capture_never_reuses_evidence_directory(
    tmp_path, capture_request, scenario
):
    output = _capture(tmp_path, capture_request, scenario)
    original = output.read_bytes()
    scenario[1].reset_mock()
    with pytest.raises(FileExistsError):
        _capture(tmp_path, capture_request, scenario)
    scenario[1].get.assert_not_called()
    assert output.read_bytes() == original


@pytest.mark.parametrize("budget", ["requests", "pages"])
def test_capture_stops_at_cumulative_read_and_page_bounds(
    tmp_path, capture_request, scenario, budget
):
    _, transport, responses = scenario
    if budget == "requests":
        limits = replace(capture_request.limits, requests=3)
    else:
        limits = replace(capture_request.limits, version_pages=1)
        responses[PAGE + "1"] = _response(
            PAGE + "1",
            [{"id": index + 1, "name": f"1.0.{index}"} for index in range(100)],
        )
    with pytest.raises(ValueError, match="limit reached"):
        _capture(tmp_path, replace(capture_request, limits=limits), scenario)
    assert [call.args[0] for call in transport.get.call_args_list] == [
        native.NUGET_SERVICE_INDEX,
        CONTROL,
        PAGE + "1",
    ]
    failure = _read(tmp_path / "capture/failure.json")
    assert failure["counts"]["requests"] == 3
    assert failure["counts"]["versionPages"] == 1
    assert len(failure["returnedResponses"]) == 3
    assert not (tmp_path / "capture/capture.json").exists()


@pytest.mark.parametrize("extra", [0, 1])
def test_capture_byte_limit_includes_overflow_sentinel(
    tmp_path, capture_request, scenario, extra
):
    _, transport, responses = scenario
    required = sum(len(response.body) for response in responses.values())
    bounded = replace(
        capture_request,
        limits=replace(capture_request.limits, response_bytes=required + extra),
    )
    if extra:
        result = _read(_capture(tmp_path, bounded, scenario))
        assert result["counts"]["chargedResponseBytes"] == required
    else:
        with pytest.raises(
            ValueError, match="response exceeds capture byte bound"
        ):
            _capture(tmp_path, bounded, scenario)
        failure = _read(tmp_path / "capture/failure.json")
        assert failure["counts"]["returnedResponseBytes"] == required
        assert not (tmp_path / "capture/capture.json").exists()
    assert (
        transport.get.call_args.kwargs["max_bytes"] == len(ARCHIVE) - 1 + extra
    )


def test_capture_charges_actual_bytes_and_reserves_failed_reads(
    tmp_path, capture_request, scenario
):
    _, transport, responses = scenario

    def read(url, **_kwargs):
        if url == CONTROL:
            message = "unavailable response"
            raise native.NuGetTransportError(message)
        return responses[url]

    transport.get.side_effect = read
    with pytest.raises(native.NuGetTransportError):
        _capture(tmp_path, capture_request, scenario)
    failure = _read(tmp_path / "capture/failure.json")
    assert failure["counts"] == {
        "requests": 2,
        "versionPages": 0,
        "chargedResponseBytes": capture_request.limits.response_bytes,
        "returnedResponseBytes": len(
            responses[native.NUGET_SERVICE_INDEX].body
        ),
    }
    events = [
        json.loads(line)
        for line in (tmp_path / "capture/requests.jsonl")
        .read_text()
        .splitlines()
    ]
    assert [item["event"] for item in events] == [
        "reserved",
        "returned",
        "reserved",
    ]
    assert len(failure["returnedResponses"]) == 1
    assert not (tmp_path / "capture/response-002.body").exists()
    assert not (tmp_path / "capture/capture.json").exists()


@pytest.mark.parametrize("late", ["before-read", "after-read", "after-helper"])
def test_capture_rejects_late_results_and_bounds_socket_timeout(
    tmp_path, capture_request, scenario, late
):
    authority, transport, responses = scenario
    elapsed = [0.0]

    def monotonic():
        return elapsed[0]

    def read(url, **kwargs):
        assert 0 < kwargs["timeout"] <= 12
        if late == "after-read":
            elapsed[0] = 60
        return responses[url]

    transport.get.side_effect = read
    if late == "before-read":
        authority.normalize_identity.side_effect = lambda package, version: (
            elapsed.__setitem__(0, 60) or _identity(package, version)
        )
    elif late == "after-helper":
        facts = authority.inspect_package.return_value
        authority.inspect_package.side_effect = lambda _content: (
            elapsed.__setitem__(0, 60) or facts
        )
    with pytest.raises(ValueError, match="deadline expired"):
        _capture(tmp_path, capture_request, scenario, monotonic=monotonic)
    failure = _read(tmp_path / "capture/failure.json")
    assert (
        failure["counts"]["requests"]
        == {"before-read": 0, "after-read": 1, "after-helper": 5}[late]
    )
    assert not (tmp_path / "capture/capture.json").exists()


@pytest.mark.parametrize(
    ("defect", "read_count"),
    [("container", 2), ("inventory", 4), ("status", 1), ("redirect", 1)],
)
def test_capture_retains_partial_evidence_without_completion(
    tmp_path, capture_request, scenario, defect, read_count
):
    _, transport, responses = scenario
    if defect == "container":
        control = json.loads(responses[CONTROL].body)
        control["id"] += 1
        responses[CONTROL] = _response(CONTROL, control)
    elif defect == "inventory":
        responses[FLAT] = _response(FLAT, {"versions": []})
    else:
        responses[native.NUGET_SERVICE_INDEX] = replace(
            responses[native.NUGET_SERVICE_INDEX],
            **(
                {"status": 403}
                if defect == "status"
                else {"url": "https://unadmitted.example/index.json"}
            ),
        )
    with pytest.raises(
        ValueError, match=r"mismatch|disagree|HTTP status|redirect"
    ):
        _capture(tmp_path, capture_request, scenario)
    failure = _read(tmp_path / "capture/failure.json")
    assert failure["completedCapture"] is False
    assert failure["counts"]["requests"] == read_count
    assert len(failure["returnedResponses"]) == read_count
    assert transport.get.call_count == read_count
    assert not (tmp_path / "capture/capture.json").exists()


def test_capture_never_persists_request_credentials_or_exception_text(
    tmp_path, capture_request, scenario
):
    scenario[1].get.side_effect = native.NuGetTransportError(
        "credential=" + TOKEN
    )
    with pytest.raises(native.NuGetTransportError):
        _capture(tmp_path, capture_request, scenario)
    files = list((tmp_path / "capture").iterdir())
    assert files
    for path in files:
        assert TOKEN.encode() not in path.read_bytes()
        assert b"Authorization" not in path.read_bytes()
    failure = _read(tmp_path / "capture/failure.json")
    assert failure["errorType"] == "NuGetTransportError"
    assert failure["returnedResponses"] == []
    scenario[1].get.assert_called_once()


@pytest.mark.parametrize("encoded", [False, True])
@pytest.mark.parametrize(
    "field", ["body", "response-url", "selected-header", "link"]
)
def test_capture_rejects_reflected_credentials_before_response_retention(
    tmp_path, capture_request, scenario, field, encoded
):
    _, transport, responses = scenario
    secret = (
        base64.b64encode(("hcoona:" + TOKEN).encode()).decode()
        if encoded
        else TOKEN
    )
    changed = {
        "body": {"body": b"server diagnostic: " + secret.encode()},
        "response-url": {
            "url": CONTROL + "?credential=" + quote(secret, safe="")
        },
        "selected-header": {"headers": (("ETag", secret),)},
        "link": {
            "headers": (
                (
                    "LiNk",
                    "<https://api.github.com/related?value="
                    + quote(secret, safe="").replace("%3D", "%3d")
                    + '>; rel="related"',
                ),
            )
        },
    }[field]
    responses[CONTROL] = replace(responses[CONTROL], **changed)
    with pytest.raises(ValueError, match="contains a request credential"):
        _capture(tmp_path, capture_request, scenario)
    directory = tmp_path / "capture"
    for path in directory.iterdir():
        assert secret.encode() not in path.read_bytes()
        assert quote(secret, safe="").encode() not in path.read_bytes()
    assert not (directory / "capture.json").exists()
    assert not (directory / "response-002.body").exists()
    assert not (directory / "response-002.json").exists()
    assert (directory / "response-001.body").read_bytes() == responses[
        native.NUGET_SERVICE_INDEX
    ].body
    failure = _read(directory / "failure.json")
    assert failure["completedCapture"] is False
    assert failure["counts"]["requests"] == transport.get.call_count == 2
    assert len(failure["returnedResponses"]) == 1
    received = sum(
        len(responses[url].body)
        for url in (native.NUGET_SERVICE_INDEX, CONTROL)
    )
    assert failure["counts"]["returnedResponseBytes"] == received
    assert failure["counts"]["chargedResponseBytes"] == received


@pytest.mark.parametrize("encoded", [False, True])
def test_capture_rejects_credential_bearing_request_url_before_journaling(
    tmp_path, capture_request, scenario, encoded
):
    authority, transport, responses = scenario
    secret = (
        base64.b64encode(("hcoona:" + TOKEN).encode()).decode()
        if encoded
        else TOKEN
    )
    authority.service_resources.return_value["packageBaseAddress"] = (
        BASE + quote(secret, safe="") + "/"
    )
    with pytest.raises(ValueError, match="contains a request credential"):
        _capture(tmp_path, capture_request, scenario)
    directory = tmp_path / "capture"
    for path in directory.iterdir():
        assert secret.encode() not in path.read_bytes()
        assert quote(secret, safe="").encode() not in path.read_bytes()
    assert [call.args[0] for call in transport.get.call_args_list] == list(
        responses
    )[:3]
    assert not (directory / "capture.json").exists()
    assert len(_read(directory / "failure.json")["returnedResponses"]) == 3


def test_capture_projects_headers_without_changing_original_body(
    tmp_path, capture_request, scenario
):
    _, _, responses = scenario
    responses[ARCHIVE_URL] = replace(
        responses[ARCHIVE_URL],
        headers=(
            ("Content-Type", "application/octet-stream"),
            ("Content-Encoding", "identity"),
            ("Content-Length", str(len(ARCHIVE))),
            ("ETag", '"original-package"'),
            ("Link", '<https://api.github.com/related?a=b%3Dc>; rel="related"'),
            ("Set-Cookie", "session=independent-cookie-secret"),
            ("X-Diagnostic", TOKEN),
        ),
    )
    output = _capture(tmp_path, capture_request, scenario)
    document = _read(output)
    response = document["responses"][-1]
    assert response["headers"] == [
        ["content-type", "application/octet-stream"],
        ["content-encoding", "identity"],
        ["content-length", str(len(ARCHIVE))],
        ["etag", '"original-package"'],
        ["link", '<https://api.github.com/related?a=b%3Dc>; rel="related"'],
    ]
    assert (output.parent / response["body"]).read_bytes() == ARCHIVE
    assert (
        document["scenarioPackage"]["sha256"]
        == hashlib.sha256(ARCHIVE).hexdigest()
    )
    for path in output.parent.iterdir():
        content = path.read_bytes()
        assert b"independent-cookie-secret" not in content
        assert TOKEN.encode() not in content
        assert b"Set-Cookie" not in content
    assert not (output.parent / "failure.json").exists()


def test_capture_narrows_socket_timeout_to_remaining_deadline(
    tmp_path, capture_request, scenario
):
    limited = replace(
        capture_request,
        limits=replace(capture_request.limits, completion_timeout_seconds=5),
    )
    output = _capture(tmp_path, limited, scenario, monotonic=lambda: 0.0)
    assert _read(output)["counts"]["requests"] == 5
    assert [
        call.kwargs["timeout"] for call in scenario[1].get.call_args_list
    ] == [5.0] * 5
