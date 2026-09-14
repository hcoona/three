"""Controlled HTTP boundaries: original bytes, finite effects and no replay."""

from __future__ import annotations

import hashlib

# ruff: noqa: D103, PLR2004
import io
import time
from dataclasses import replace
from email.message import Message
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.acceptance import nuget_github as github
from three_workflow_delivery_v3.canonical import parse_canonical_json

TOKEN = "controlled-github-token"  # noqa: S105 - controlled test credential
ORIGIN = "https://artifact-storage.example"
ROUTE = "/repos/hcoona/three/actions/runs/81"


class _Response(io.BytesIO):
    def __init__(self, body, status, headers):
        super().__init__(body)
        self.status, self.headers = status, headers

    def getheader(self, name):
        return self.headers.get(name)


def _response(body=b"{}", status=200, location=None, **headers):
    fields = Message()
    fields["Content-Length"] = str(len(body))
    if location is not None:
        fields["Location"] = location
    for key, value in headers.items():
        fields[key.replace("_", "-")] = value
    return _Response(body, status, fields)


@pytest.fixture
def transport(tmp_path, monkeypatch):  # noqa: C901 - controlled transport seam
    def create(responses, **overrides):
        pending = list(responses)
        calls = []

        class Connection:
            def __init__(self, host, **kwargs):
                self.host, self.options = host, kwargs
                self.auto_open = 1

            def set_debuglevel(self, value):
                assert value == 0

            def connect(self):
                assert self.auto_open == 0

            def request(self, method, target, **kwargs):
                calls.append((self.host, method, target, kwargs))

            def getresponse(self):
                outcome = pending.pop(0)
                if isinstance(outcome, BaseException):
                    raise outcome
                return outcome

            def close(self):
                pass

        def supervise(target, arguments, timeout):
            assert timeout > 0
            control = SimpleNamespace(
                send=lambda _: None, recv=lambda: "start", close=lambda: None
            )
            try:
                target(control, *arguments)
            except SystemExit as error:
                return error.code
            return 0

        monkeypatch.setattr(github.http.client, "HTTPSConnection", Connection)
        monkeypatch.setattr(github.ssl, "create_default_context", object)
        monkeypatch.setattr(github.os, "setsid", lambda: None)
        monkeypatch.setattr(github.process, "_supervise", supervise)
        limits = replace(
            github.NuGetGitHubLimits(
                30,
                5_000_000,
                100_000,
                1_000_000,
                10,
                2,
                0.001,
                ORIGIN,
            ),
            **overrides,
        )
        client = github.NuGetGitHubClient(tmp_path / "http", limits, TOKEN)
        return SimpleNamespace(client=client, calls=calls, pending=pending)

    return create


def test_http_preserves_raw_bytes_without_replay(transport):
    original = b'{ "text": "\\u001b", "number": 1 }\n'
    item = transport([_response(original)])
    body = b'{"ref":"main"}'
    assert (
        item.client.request(ROUTE, body=body, deadline=time.monotonic() + 10)
        == original
    )
    assert len(item.calls) == 1
    host, method, route, supplied = item.calls[0]
    assert (host, method, route) == ("api.github.com", "POST", ROUTE)
    assert supplied["body"] == body
    assert supplied["headers"]["Authorization"] == "Bearer " + TOKEN
    assert supplied["encode_chunked"] is False
    assert (
        item.client.directory / "call-0001/api.body"
    ).read_bytes() == original
    assert item.client.requests == 1
    assert item.client.response_bytes == len(original)


def test_artifact_redirect_omits_credentials_and_retains_no_capability(
    transport,
):
    capability = ORIGIN + "/original.zip?sig=controlled-capability&se=2030"
    escaped = capability.replace("&", "&amp;").encode()
    redirect_body = b'<a href="' + escaped + b'">download</a>'
    original = b"PK\x00original-artifact-bytes"
    item = transport(
        [_response(redirect_body, 302, capability), _response(original)]
    )
    actual = item.client.request(
        ROUTE, download=True, deadline=time.monotonic() + 10
    )
    assert actual == original
    assert len(item.calls) == 2
    assert item.calls[0][3]["headers"]["Authorization"] == "Bearer " + TOKEN
    host, method, target, supplied = item.calls[1]
    assert (host, method, target) == (
        "artifact-storage.example",
        "GET",
        "/original.zip?sig=controlled-capability&se=2030",
    )
    assert "Authorization" not in supplied["headers"]
    assert supplied["body"] is None
    retained = b"".join(
        path.read_bytes()
        for path in item.client.directory.rglob("*")
        if path.is_file()
    )
    assert capability.encode() not in retained
    assert escaped not in retained
    assert TOKEN.encode() not in retained
    assert b"locationSha256" in retained
    assert item.client.requests == 2
    assert item.client.response_bytes == len(original) + len(redirect_body)
    directory = item.client.directory / "call-0001"
    assert not (directory / "api.body").exists()
    metadata = parse_canonical_json((directory / "api.json").read_bytes())
    assert metadata["bodyRetention"] == "omitted"
    assert metadata["bodyOmissionReason"] == "location"
    assert metadata["bodyBytesRead"] == len(redirect_body)
    assert metadata["bodySha256"] == (hashlib.sha256(redirect_body).hexdigest())


def test_actor_response_retains_only_typed_identity(transport):
    original = (
        b'{"id":712433,"login":"hcoona",'
        b'"private_profile":"unused-account-value"}'
    )
    item = transport([_response(original)])
    result = item.client.request("/user", deadline=time.monotonic() + 10)
    assert parse_canonical_json(result) == {"id": 712433, "login": "hcoona"}
    directory = item.client.directory / "call-0001"
    retained = b"".join(path.read_bytes() for path in directory.iterdir())
    assert b"private_profile" not in retained
    assert b"unused-account-value" not in retained
    assert original not in retained
    metadata = parse_canonical_json((directory / "api.json").read_bytes())
    assert metadata["bodyRetention"] == "omitted"
    assert metadata["bodyOmissionReason"] == "authenticated-actor"
    assert metadata["bodyBytesRead"] == len(original)
    assert metadata["bodySha256"] == (hashlib.sha256(original).hexdigest())
    projection = parse_canonical_json(
        (directory / "actor-identity.json").read_bytes()
    )
    assert projection == {
        "evidenceKind": "derived-identity",
        "endpoint": "/user",
        "fields": ["id", "login"],
        "bodyFile": "api.body",
        "bodySha256": hashlib.sha256(result).hexdigest(),
    }
    assert item.client.requests == 1
    assert item.client.response_bytes == len(original)


@pytest.mark.parametrize(
    "failure",
    [
        "malformed",
        "identity",
        "login-type",
        "status",
        "redirect",
        "overflow",
        "encoding",
    ],
)
def test_actor_failure_never_retains_raw_profile(transport, failure):
    original = (
        b'{"id":712433,"login":"hcoona","private":"unused-account-value"}'
    )
    response = {
        "malformed": _response(b"unused-account-value"),
        "identity": _response(original.replace(b"712433", b"true")),
        "login-type": _response(
            original.replace(b'"hcoona"', b'["unused-account-value"]')
        ),
        "status": _response(original, 403),
        "redirect": _response(original, 302, ORIGIN + "/actor"),
        "overflow": _response(original + b" " * 101),
        "encoding": _response(original, Content_Encoding="gzip"),
    }[failure]
    item = transport([response], metadata_bytes=100)
    with pytest.raises(ValueError, match="GitHub call failed"):
        item.client.request("/user", deadline=time.monotonic() + 10)
    directory = item.client.directory / "call-0001"
    retained = b"".join(path.read_bytes() for path in directory.iterdir())
    assert b"unused-account-value" not in retained
    assert b'"private"' not in retained
    assert not (directory / "api.body").exists()
    assert not (directory / "completed.json").exists()
    assert item.client.failed
    assert item.client.requests == 1
    assert item.client.response_bytes == 101
    with pytest.raises(ValueError, match="already failed"):
        item.client.request("/user", deadline=time.monotonic() + 10)
    assert len(item.calls) == 1


@pytest.mark.parametrize(
    "failure", ["status", "foreign", "overflow", "second-hop"]
)
def test_location_body_is_omitted_on_failure(transport, failure):
    origin = "https://other.example" if failure == "foreign" else ORIGIN
    capability = origin + "/artifact?sig=controlled&se=2030"
    escaped = capability.replace("&", "&amp;").encode()
    body = b'<a href="' + escaped + b'">download</a>'
    if failure == "overflow":
        body += b" " * 1000
    responses = [
        _response(body, 503 if failure == "status" else 302, capability)
    ]
    if failure == "second-hop":
        responses.append(_response(body, 302, capability))
    item = transport(responses, metadata_bytes=1000)
    with pytest.raises(ValueError, match="GitHub call failed"):
        item.client.request(
            ROUTE, download=True, deadline=time.monotonic() + 10
        )
    directory = item.client.directory / "call-0001"
    retained = b"".join(path.read_bytes() for path in directory.iterdir())
    assert capability.encode() not in retained
    assert escaped not in retained
    assert not (directory / "api.body").exists()
    assert not (directory / "artifact.body").exists()
    metadata = parse_canonical_json((directory / "api.json").read_bytes())
    assert metadata["bodyRetention"] == "omitted"
    assert metadata["bodyBytesRead"] == min(len(body), 1001)
    assert metadata["bodySha256"] == (hashlib.sha256(body[:1001]).hexdigest())
    assert item.client.failed
    assert item.client.response_bytes == 1_001_002
    assert len(item.calls) == (2 if failure == "second-hop" else 1)


@pytest.mark.parametrize(
    "failure",
    [
        "lost",
        "status",
        "redirect",
        "overflow",
        "encoding",
        "reflection",
        "foreign",
        "second-hop",
    ],
)
def test_http_failure_is_bounded_and_retained(transport, failure):
    download = failure in {"foreign", "second-hop"}
    responses = {
        "lost": [OSError("controlled lost response")],
        "status": [_response(b"failed", 503)],
        "redirect": [_response(b"", 307, ORIGIN + "/replay")],
        "overflow": [_response(b"x" * 101)],
        "encoding": [_response(b"encoded", Content_Encoding="gzip")],
        "reflection": [_response(TOKEN.encode())],
        "foreign": [_response(b"", 302, "https://other.example/artifact")],
        "second-hop": [
            _response(b"", 302, ORIGIN + "/artifact"),
            _response(b"", 302, ORIGIN + "/again"),
        ],
    }[failure]
    item = transport(responses, metadata_bytes=100)
    with pytest.raises(ValueError, match="GitHub call failed"):
        item.client.request(
            ROUTE,
            body=None if download else b"{}",
            download=download,
            deadline=time.monotonic() + 10,
        )
    calls = len(item.calls)
    assert calls == (2 if failure == "second-hop" else 1)
    assert item.client.failed
    with pytest.raises(ValueError, match="already failed"):
        item.client.request(ROUTE, deadline=time.monotonic() + 10)
    assert len(item.calls) == calls
    retained = b"".join(
        path.read_bytes()
        for path in item.client.directory.rglob("*")
        if path.is_file()
    )
    assert TOKEN.encode() not in retained
    failure_record = parse_canonical_json(
        (item.client.directory / "call-0001/call-failed.json").read_bytes()
    )
    assert failure_record["reservationSpent"] is True
    assert failure_record["remoteCompletionUnknown"] is (not download)
    assert item.client.response_bytes == (1_000_102 if download else 101)


@pytest.mark.parametrize("boundary", ["requests", "bytes", "deadline"])
def test_http_budget_stops_before_effects(transport, boundary):
    item = transport(
        [_response()], requests=1, metadata_bytes=10, response_body_bytes=11
    )
    if boundary != "deadline":
        item.client.request(ROUTE, deadline=time.monotonic() + 10)
        if boundary == "bytes":
            item.client.requests = 0
    previous = len(item.calls)
    with pytest.raises(ValueError, match="allowance exhausted"):
        item.client.request(
            ROUTE,
            deadline=time.monotonic() + (-1 if boundary == "deadline" else 10),
        )
    assert len(item.calls) == previous


@pytest.mark.parametrize(
    "change",
    [
        {"requests": True},
        {"metadata_bytes": 0},
        {"polls_per_probe": -1},
        {"call_timeout_seconds": float("nan")},
        {"poll_interval_seconds": float("inf")},
        {"storage_origin": "https://user:secret@artifact-storage.example"},
        {"storage_origin": ORIGIN + "/path"},
    ],
)
def test_http_limits_reject_invalid_values(transport, change):
    with pytest.raises(ValueError, match=r"invalid GitHub|storage origin"):
        transport([], **change)


def test_http_rejects_late_supervised_completion(transport, monkeypatch):
    item = transport([_response()])
    now = time.monotonic()
    original = github.process._supervise  # noqa: SLF001

    def late(*arguments):
        result = original(*arguments)
        monkeypatch.setattr(github.time, "monotonic", lambda: now + 20)
        return result

    monkeypatch.setattr(github.process, "_supervise", late)
    with pytest.raises(ValueError, match="completed late"):
        item.client.request(ROUTE, deadline=now + 10, body=b"{}")
    assert len(item.calls) == 1
    assert item.client.failed
    assert item.client.response_bytes == item.client.limits.metadata_bytes + 1
