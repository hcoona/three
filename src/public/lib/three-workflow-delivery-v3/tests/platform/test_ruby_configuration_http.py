"""Bounded supervised HTTPS against controlled native transport substitutes."""

import base64
import io
import json
import ssl
import time
from datetime import UTC, datetime, timedelta
from http import HTTPStatus

import pytest
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.adapters.ruby_project import (
    classify_rubygems_project,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform import ruby_configuration_http as http
from three_workflow_delivery_v3.platform.ruby_configuration_http import (
    BODY_LIMIT,
    HEADER_LIMIT,
    RubyConfigurationRequest,
    RubyConfigurationResponse,
    RubyConfigurationTransport,
)

from ..release.ruby_configuration_ledger_fixtures import ledger, mutation
from ..ruby_integration_fixtures import NOW
from .ruby_configuration_fixtures import OWNER, response

TOKEN = "synthetic_local_token"  # noqa: S105 - deliberately synthetic seam input


@pytest.mark.parametrize(
    ("role", "path"),
    [
        ("principal", "/user"),
        ("repository", "/repos/hcoona/three"),
        ("main", "/repos/hcoona/three/branches/main"),
        ("protection", "/repos/hcoona/three/branches/main/protection"),
        (
            "selected-package",
            "/user/packages/rubygems/hcoona-release-smoke-ruby",
        ),
        (
            "effective-rules",
            "/repos/hcoona/three/rules/branches/main?per_page=100&page=1",
        ),
        (
            "rulesets",
            "/repos/hcoona/three/rulesets?includes_parents=true&per_page=100&page=1",
        ),
        (
            "collaborators",
            "/repos/hcoona/three/collaborators?affiliation=all&per_page=100&page=1",
        ),
        (
            "owner-packages",
            "/user/packages?package_type=rubygems&per_page=100&page=1",
        ),
    ],
)
def test_request_derives_fixed_credential_free_read_endpoint(role, path):
    """A role selects an exact native endpoint without arbitrary URLs."""
    request = RubyConfigurationRequest("github-packages", role)
    assert request.path == path
    assert request.method == "GET"
    assert request.body is None
    assert request.mutation_role is None


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize(
    ("role", "ledger_role", "update"),
    [
        ("environment-put", "environment-write", False),
        ("branch-policy-create", "branch-write", False),
        ("marker-create", "marker-write", False),
        ("marker-update", "marker-write", True),
    ],
)
def test_mutations_use_exact_protocol_body_and_shared_marker_role(
    destination, role, ledger_role, update
):
    """No body override or undocumented bypass field is exposed."""
    request = RubyConfigurationRequest(destination, role)
    assert (request.method, request.path, request.body) == mutation(
        destination, ledger_role, update=update
    )
    assert b"can_admins_bypass" not in request.body
    if ledger_role == "marker-write":
        assert request.mutation_role == "marker"


@pytest.mark.parametrize(
    ("destination", "role", "page", "identifier"),
    [
        ("pypi", "principal", 1, None),
        ("github-packages", "foreign", 1, None),
        ("github-packages", "principal", True, None),
        ("github-packages", "principal", 2, None),
        ("github-packages", "owner-packages", 11, None),
        ("github-packages", "owner-packages", 0, None),
        ("github-packages", "rulesets", 6, None),
        ("github-packages", "secrets", 3, None),
        ("github-packages", "ruleset-detail", 1, True),
        ("github-packages", "ruleset-detail", 1, 0),
        ("github-packages", "ruleset-detail", 1, None),
        ("github-packages", "principal", 1, 1),
    ],
)
def test_request_rejects_unbounded_page_foreign_role_or_non_native_id(
    destination, role, page, identifier
):
    """Strict native selection rejects aliases and Boolean ID substitutes."""
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationRequest(destination, role, page, identifier)


@pytest.mark.parametrize(
    ("role", "page"), [("owner-packages", 10), ("rulesets", 5), ("secrets", 2)]
)
def test_request_accepts_final_allowed_page(role, page):
    """Inclusive pagination limits do not silently remove the last page."""
    assert RubyConfigurationRequest(
        "github-packages", role, page
    ).path.endswith("page=" + str(page))


@pytest.mark.parametrize(
    "change",
    [
        "status",
        "body-limit",
        "header-limit",
        "duplicate-header",
        "credential-header",
        "backwards",
        "naive",
        "failure-body",
        "failure-status",
        "unknown-failure",
    ],
)
def test_response_rejects_inconsistent_or_unsafe_retained_envelope(change):
    """Only complete bounded success or a closed sanitized failure is valid."""
    values = {
        "request": RubyConfigurationRequest("github-packages", "principal"),
        "receipt": "receipt",
        "started_at": NOW,
        "completed_at": NOW,
        "status": 200,
    }
    updates = {
        "status": {"status": True},
        "body-limit": {"body": b"x" * (BODY_LIMIT + 1)},
        "header-limit": {"headers": (("date", "x" * HEADER_LIMIT),)},
        "duplicate-header": {"headers": (("date", "a"), ("date", "b"))},
        "credential-header": {"headers": (("authorization", TOKEN),)},
        "backwards": {"completed_at": NOW - timedelta(seconds=1)},
        "naive": {"started_at": NOW.replace(tzinfo=None)},
        "failure-body": {"status": None, "failure": "transport", "body": b"x"},
        "failure-status": {"failure": "deadline"},
        "unknown-failure": {"status": None, "failure": "stacktrace"},
    }
    values.update(updates[change])
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationResponse(**values)


def test_response_retains_original_body_without_exposing_private_data_in_repr():
    """Private body bytes are recoverable in evidence but omitted in repr."""
    original = response(
        RubyConfigurationRequest("github-packages", "principal"),
        {"private": "fixture-owner-detail"},
    )
    assert "fixture-owner-detail" not in repr(original)
    assert base64.b64decode(original.document()["body-base64"]) == original.body
    assert original.document()["url"] == "https://api.github.com/user"


def _fake_https(  # noqa: PLR0913 - independent transport fixture axes
    monkeypatch,
    log,
    *,
    body=b"{}",
    status=200,
    content_type="application/json",
    headers=(),
    error=False,
    hostname="api.github.com",
):
    class Reply:
        def read(self, amount):
            assert amount == BODY_LIMIT + 1
            return body

        def getheader(self, name):
            return dict(headers).get(name)

        def getheaders(self):
            return [("Content-Type", content_type), *headers]

    class Connection:
        def __init__(self, actual_hostname, port, *, timeout, context):
            assert actual_hostname == hostname
            assert port == 443  # noqa: PLR2004
            assert timeout == 30  # noqa: PLR2004
            assert context.minimum_version == ssl.TLSVersion.TLSv1_2

        def request(self, method, path, *, body, headers):
            with log.open("a") as stream:
                stream.write(
                    json.dumps(
                        {
                            "method": method,
                            "path": path,
                            "body": None if body is None else body.decode(),
                            "headers": headers,
                        }
                    )
                    + "\n"
                )
            if error:
                raise OSError("private failure " + TOKEN)

        def getresponse(self):
            reply = Reply()
            reply.status = status
            return reply

        def close(self):
            pass

    monkeypatch.setattr(http.http.client, "HTTPSConnection", Connection)


def _transport(reserved, retained, *, deadline=None):
    def reserve(request):
        reserved.append(request)
        return "durable-fixture-receipt"

    return RubyConfigurationTransport(
        TOKEN,
        deadline=deadline or datetime.now(UTC) + timedelta(seconds=5),
        reserve=reserve,
        retain=retained.append,
    )


@pytest.mark.parametrize(
    ("status", "role"),
    [(200, "principal"), (302, "principal"), (201, "marker-create")],
)
def test_supervised_send_reserves_and_retains_once_without_redirect(
    tmp_path, monkeypatch, status, role
):
    """A real fork performs one substituted HTTPS call and retains original."""
    log = tmp_path / "controlled-requests.jsonl"
    _fake_https(
        monkeypatch, log, status=status, headers=(("Set-Cookie", TOKEN),)
    )
    reserved, retained = [], []
    request = RubyConfigurationRequest("github-packages", role)
    result = _transport(reserved, retained).send(request)
    assert reserved == [request]
    assert retained == [result]
    assert result.status == status
    assert result.failure is None
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    assert len(calls) == 1
    assert calls[0]["headers"]["Authorization"] == "Bearer " + TOKEN
    assert calls[0]["headers"]["X-GitHub-Api-Version"] == "2022-11-28"
    assert calls[0]["path"] == request.path
    assert calls[0]["method"] == request.method
    assert calls[0]["body"] == (
        None if request.body is None else request.body.decode()
    )
    assert TOKEN not in repr(result)
    assert TOKEN not in str(result.document())
    assert "set-cookie" not in result.document()["headers"]


@pytest.mark.parametrize(
    ("body", "headers", "error", "failure"),
    [
        (b"{}", (), True, "transport"),
        (TOKEN.encode(), (), False, "unsafe-response"),
        (b"{}", (("Date", TOKEN),), False, "unsafe-response"),
        (b"x" * (BODY_LIMIT + 1), (), False, "response-limit"),
        (b"{}", (("Content-Length", "3"),), False, "transport"),
        (b"{}", (("Content-Type", "text/plain"),), False, "transport"),
    ],
)
def test_supervised_failures_retain_spending_without_credential_detail(  # noqa: PLR0913, PLR0917 - parametrized transport outcomes
    tmp_path, monkeypatch, capfd, body, headers, error, failure
):
    """Failed response validation preserves one spent sanitized observation."""
    log = tmp_path / "controlled-requests.jsonl"
    _fake_https(monkeypatch, log, body=body, headers=headers, error=error)
    reserved, retained = [], []
    result = _transport(reserved, retained).send(
        RubyConfigurationRequest("github-packages", "principal")
    )
    assert result.failure == failure
    assert result.status is None
    assert result.body == b""
    assert len(reserved) == 1
    assert retained == [result]
    assert len(log.read_text().splitlines()) == 1
    assert TOKEN not in str(result.document())
    output = capfd.readouterr()
    assert TOKEN not in output.out + output.err


def test_reservation_refusal_prevents_child_or_transport_start(
    tmp_path, monkeypatch
):
    """No network seam is entered before durable admission succeeds."""
    log = tmp_path / "controlled-requests.jsonl"
    _fake_https(monkeypatch, log)
    retained = []

    def refuse(_request):
        message = "fixture ledger refused"
        raise ValueError(message)

    transport = RubyConfigurationTransport(
        TOKEN,
        deadline=datetime.now(UTC) + timedelta(seconds=5),
        reserve=refuse,
        retain=retained.append,
    )
    with pytest.raises(ValueError, match="ledger refused"):
        transport.send(RubyConfigurationRequest("github-packages", "principal"))
    assert not log.exists()
    assert retained == []


def test_expired_transport_stops_before_reservation(tmp_path, monkeypatch):
    """An expired phase cannot spend another receipt or start HTTPS."""
    log = tmp_path / "controlled-requests.jsonl"
    _fake_https(monkeypatch, log)
    reserved, retained = [], []
    transport = _transport(
        reserved, retained, deadline=datetime.now(UTC) - timedelta(seconds=1)
    )
    with pytest.raises(ValueError, match="expired"):
        transport.send(RubyConfigurationRequest("github-packages", "principal"))
    assert reserved == retained == []
    assert not log.exists()


def test_outer_deadline_kills_blocked_child_and_retains_spending(monkeypatch):
    """Whole-request deadline covers a child that never returns a response."""

    def blocked(_request, _token, _output):
        time.sleep(10)

    monkeypatch.setattr(http, "_perform", blocked)
    reserved, retained = [], []
    transport = _transport(
        reserved,
        retained,
        deadline=datetime.now(UTC) + timedelta(milliseconds=150),
    )
    started = time.monotonic()
    result = transport.send(
        RubyConfigurationRequest("github-packages", "principal")
    )
    assert time.monotonic() - started < 2  # noqa: PLR2004 - generous bound around 150ms deadline
    assert result.failure == "deadline"
    assert len(reserved) == 1
    assert retained == [result]


def test_raw_header_and_chunk_trailer_limits_are_bounded():
    """Actual stdlib parsing cannot accept an oversized trailer envelope."""

    class Socket:
        def __init__(self, content):
            self.content = content

        def makefile(self, _mode):
            return io.BytesIO(self.content)

    oversized = (
        b"HTTP/1.1 200 OK\r\nX-Long: " + b"a" * HEADER_LIMIT + b"\r\n\r\n"
    )
    native = http._Response(Socket(oversized))  # noqa: SLF001 - real stdlib adapter boundary
    with pytest.raises(ValueError, match="headers exceed limit"):
        native.begin()
    chunked = (
        b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n0\r\nX-Trailer: "
        + b"a" * HEADER_LIMIT
        + b"\r\n\r\n"
    )
    native = http._Response(Socket(chunked))  # noqa: SLF001 - actual wire parser fixture
    native.begin()
    with pytest.raises(ValueError, match="headers exceed limit"):
        native.read(BODY_LIMIT + 1)


@pytest.mark.parametrize(
    ("status", "body", "content_type", "classification"),
    [
        (404, b"This rubygem could not be found.", "text/plain", "absent"),
        (200, b"[]", "application/json", "present"),
        (200, b'[{"handle":"fixture-owner"}]', "application/json", "present"),
    ],
)
def test_public_rubygems_owners_send_uses_registry_origin_without_credentials(  # noqa: PLR0913, PLR0917 - direct project response cases
    tmp_path, monkeypatch, status, body, content_type, classification
):
    """The allocated project read reaches RubyGems with only public headers."""
    log = tmp_path / "controlled-public-request.jsonl"
    _fake_https(
        monkeypatch,
        log,
        hostname="rubygems.org",
        status=status,
        body=body,
        content_type=content_type,
    )
    reserved, retained = [], []
    request = RubyConfigurationRequest("rubygems", "rubygems-project-owners")
    result = _transport(reserved, retained).send(request)
    assert result.status == status
    assert result.failure is None
    assert result.body == body
    assert result.header("content-type") == content_type
    assert (
        classify_rubygems_project(
            RubyHttpResponse(
                result.status, result.body, result.header("content-type")
            )
        )
        == classification
    )
    assert (
        result.document()["url"]
        == "https://rubygems.org/api/v1/gems/hcoona-release-smoke-ruby/owners.json"
    )
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    assert calls == [
        {
            "method": "GET",
            "path": "/api/v1/gems/hcoona-release-smoke-ruby/owners.json",
            "body": None,
            "headers": {
                "Accept": "application/json",
                "User-Agent": "three-workflow-delivery-v3",
            },
        }
    ]
    assert reserved == [request]
    assert retained == [result]


@pytest.mark.parametrize(
    "reply",
    [
        (404, b"This rubygem could not be found.", "text/plain"),
        (200, b"[]", "application/json"),
    ],
    ids=["absent", "present"],
)
@pytest.mark.parametrize(
    "headers",
    [
        (("Location", "https://example.invalid/owners"),),
        (("location", "https://example.invalid/owners"),),
        (("lOcAtIoN", "https://example.invalid/owners"),),
        (("Location", ""),),
        (("Location", "not a URL"),),
        (("Location", "https://example.invalid/" + TOKEN),),
        (("Location", ""), ("location", "https://example.invalid/owners")),
    ],
    ids=[
        "canonical",
        "lower",
        "mixed",
        "empty",
        "malformed",
        "token",
        "duplicate",
    ],
)
def test_public_rubygems_owners_location_is_durable_sanitized_failure(
    tmp_path, monkeypatch, capfd, headers, reply
):
    """An anomalous project reply spends once and cannot establish absence."""
    selected = ledger(tmp_path / "ledger", "rubygems")
    phase = selected.begin("initial-owners", now=datetime.now(UTC))
    log = tmp_path / "requests.jsonl"
    _fake_https(
        monkeypatch,
        log,
        hostname="rubygems.org",
        status=reply[0],
        body=reply[1],
        content_type=reply[2],
        headers=headers,
    )
    reservations, retained = [], []

    def reserve(request):
        original = phase.spend(
            request.method, request.path, request.body, now=datetime.now(UTC)
        )
        reservations.append(original)
        assert (phase.directory / "01.request.json").read_bytes() == original
        assert not log.exists()
        return ruby_digest(original)

    def retain(original):
        retained.append(original)
        assert len(log.read_text().splitlines()) == 1
        phase.complete_send(
            reservations[0],
            canonicalize(original.document()),
            successful=original.failure is None,
            now=datetime.now(UTC),
        )

    transport = RubyConfigurationTransport(
        TOKEN, deadline=phase.deadline, reserve=reserve, retain=retain
    )
    request = RubyConfigurationRequest("rubygems", "rubygems-project-owners")
    result = transport.send(request)
    assert result.failure == "transport"
    assert result.status is None
    assert result.body == b""
    assert result.headers == ()
    assert (
        classify_rubygems_project(
            RubyHttpResponse(
                result.status, result.body, result.header("content-type")
            )
        )
        == "unknown"
    )
    assert len(reservations) == 1
    assert result.receipt == ruby_digest(reservations[0])
    assert retained == [result]
    evidence = (phase.directory / "01.evidence.json").read_bytes()
    assert evidence == canonicalize(result.document())
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    assert calls == [
        {
            "method": "GET",
            "path": request.path,
            "body": None,
            "headers": {
                "Accept": "application/json",
                "User-Agent": "three-workflow-delivery-v3",
            },
        }
    ]
    with pytest.raises(ValueError, match="failed"):
        transport.send(request)
    with pytest.raises(ValueError, match="failed"):
        phase.finish(now=datetime.now(UTC))
    assert len(log.read_text().splitlines()) == 1
    assert not (phase.directory / "02.request.json").exists()
    output = capfd.readouterr()
    diagnostics = evidence.decode() + repr(result) + output.out + output.err
    assert TOKEN not in diagnostics
    for _, value in headers:
        if value:
            assert value not in diagnostics


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize("status", [200, 302])
def test_nonowners_configuration_location_preserves_original_without_following(
    tmp_path, monkeypatch, destination, status
):
    """The owners guard does not change GitHub control response handling."""
    log = tmp_path / "requests.jsonl"
    _fake_https(
        monkeypatch,
        log,
        status=status,
        headers=(("Location", "https://example.invalid/" + TOKEN),),
    )
    reserved, retained = [], []
    request = RubyConfigurationRequest(destination, "principal")
    result = _transport(reserved, retained).send(request)
    assert result.status == status
    assert result.failure is None
    assert result.body == b"{}"
    assert result.headers == (("content-type", "application/json"),)
    assert reserved == [request]
    assert retained == [result]
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    assert len(calls) == 1
    assert calls[0]["path"] == "/user"
    assert calls[0]["headers"]["Authorization"] == "Bearer " + TOKEN
    assert TOKEN not in str(result.document())


@pytest.mark.parametrize("valid_owner", [False, True])
def test_transport_callbacks_join_real_ledger_and_validated_original(
    tmp_path, monkeypatch, valid_owner
):
    """Validate response identity before closing a durable send."""
    selected = ledger(tmp_path / "ledger")
    phase = selected.begin("initial-controls", now=datetime.now(UTC))
    body = canonicalize(OWNER if valid_owner else {"login": "foreign", "id": 1})
    log = tmp_path / "requests.jsonl"
    _fake_https(monkeypatch, log, body=body)
    reservations = []
    retained = []

    def reserve(request):
        original = phase.spend(
            request.method, request.path, request.body, now=datetime.now(UTC)
        )
        reservations.append(original)
        assert (phase.directory / "01.request.json").read_bytes() == original
        assert not log.exists()
        return ruby_digest(original)

    def retain(original):
        retained.append(original)
        assert len(log.read_text().splitlines()) == 1
        accepted = (
            original.status == HTTPStatus.OK
            and parse_canonical_json(original.body) == OWNER
        )
        phase.complete_send(
            reservations[0],
            canonicalize(original.document()),
            successful=accepted,
            now=datetime.now(UTC),
        )

    transport = RubyConfigurationTransport(
        TOKEN, deadline=phase.deadline, reserve=reserve, retain=retain
    )
    result = transport.send(
        RubyConfigurationRequest("github-packages", "principal")
    )
    assert result.receipt == ruby_digest(reservations[0])
    assert retained == [result]
    original_bytes = (phase.directory / "01.evidence.json").read_bytes()
    assert original_bytes == canonicalize(result.document())
    assert TOKEN.encode() not in original_bytes
    if valid_owner:
        terminal = parse_canonical_json(phase.finish(now=datetime.now(UTC)))
        assert terminal["status"] == "complete"
    else:
        with pytest.raises(ValueError, match="failed"):
            transport.send(
                RubyConfigurationRequest("github-packages", "principal")
            )
        with pytest.raises(ValueError, match="failed"):
            phase.finish(now=datetime.now(UTC))
        assert len(log.read_text().splitlines()) == 1
        assert not (phase.directory / "02.request.json").exists()


@pytest.mark.parametrize(
    ("destination", "role"),
    [
        ("github-packages", "rubygems-project-owners"),
        ("rubygems", "owner-packages"),
        ("rubygems", "selected-package"),
    ],
)
def test_request_cannot_transfer_registry_specific_roles(destination, role):
    """A destination cannot borrow another registry's native lookup role."""
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationRequest(destination, role)


def test_slow_reservation_cannot_extend_expired_send_window(
    tmp_path, monkeypatch
):
    """Time spent durably reserving is deducted before child startup."""
    log = tmp_path / "requests.jsonl"
    _fake_https(monkeypatch, log)
    reserved, retained = [], []

    def reserve(request):
        reserved.append(request)
        time.sleep(0.08)
        return "spent-before-expiry"

    transport = RubyConfigurationTransport(
        TOKEN,
        deadline=datetime.now(UTC) + timedelta(milliseconds=40),
        reserve=reserve,
        retain=retained.append,
    )
    result = transport.send(
        RubyConfigurationRequest("github-packages", "principal")
    )
    assert result.failure == "deadline"
    assert len(reserved) == 1
    assert retained == [result]
    assert not log.exists()
