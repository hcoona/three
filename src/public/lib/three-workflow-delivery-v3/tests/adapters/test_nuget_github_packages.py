"""NuGet actual-byte observation and local fault-server no-replay scenarios."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import base64
import hashlib
import http.client
import json
import platform
import socket
import ssl
import sys
import threading
from dataclasses import replace
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.adapters import nuget_github_packages as nuget
from three_workflow_delivery_v3.canonical import canonical_sha256

PACKAGE = nuget.NUGET_PACKAGE_ID
VERSION = "1.2.3-beta.4"
TOKEN = "test-only-repository-token"  # noqa: S105
PACKAGE_BYTES = b"PK\x03\x04qualified-archive\x00\xff\r\narchive-bytes"
WITNESS = b'{"schema":"test-witness","target":"0123456789abcdef"}'
BASE = "https://nuget.pkg.github.com/hcoona/download/"
PUBLISH = "https://nuget.pkg.github.com/hcoona/"
CONTROL = f"https://api.github.com/users/hcoona/packages/nuget/{PACKAGE}"
VERSIONS_API = CONTROL + "/versions?state=active&per_page=100&page=1"
PACKAGE_ROOT = BASE + PACKAGE.lower() + "/"
ARCHIVE_URL = (
    PACKAGE_ROOT + VERSION + "/" + PACKAGE.lower() + "." + VERSION + ".nupkg"
)
RESOURCES = nuget.NuGetServiceResources(BASE, PUBLISH, "a" * 64)
_PINNED_PROFILE_RUNTIME = (
    sys.implementation.name == "cpython"
    and platform.python_version() == "3.13.12"
)
_REQUIRES_PROFILE_RUNTIME = pytest.mark.skipif(
    not _PINNED_PROFILE_RUNTIME,
    reason="The mandatory HK profile proof runs on CPython 3.13.12",
)


def _native(package_id=PACKAGE, version=VERSION, normalized_version=VERSION):
    return {
        "displayPackageId": package_id,
        "displayVersion": version,
        "normalizedPackageId": PACKAGE.lower(),
        "normalizedVersion": normalized_version,
    }


@pytest.fixture
def authority():
    helper = Mock(spec=nuget.NuGetAuthority)
    helper.normalize_identity.side_effect = _native
    helper.service_resources.return_value = {
        "packageBaseAddress": BASE,
        "packagePublish": PUBLISH,
    }
    helper.inspect_package.return_value = {
        "identity": _native(),
        "witnessBase64": base64.b64encode(WITNESS).decode(),
        "entries": [
            "workflow-delivery/provenance.json",
            "lib/net10.0/Smoke.dll",
        ],
        "frameworks": ["net10.0"],
        "dependencies": [],
        "repository": {
            "url": "https://github.com/hcoona/three",
            "commit": "a" * 40,
        },
    }
    return helper


def _response(url, document=None, *, body=None, status=200, headers=()):
    return nuget.NuGetHttpResponse(
        url,
        status,
        headers,
        json.dumps(document).encode() if body is None else body,
    )


def _responses(*, absent=False):
    return {
        nuget.NUGET_SERVICE_INDEX: _response(
            nuget.NUGET_SERVICE_INDEX, {"resources": []}
        ),
        CONTROL: _response(
            CONTROL,
            {
                "id": 12024661,
                "name": PACKAGE,
                "package_type": "nuget",
                "visibility": "public",
                "owner": {"login": "hcoona", "type": "User"},
                "repository": {
                    "full_name": "hcoona/three",
                    "permissions": {"pull": True},
                },
            },
        ),
        VERSIONS_API: _response(
            VERSIONS_API, [] if absent else [{"id": 71, "name": VERSION}]
        ),
        PACKAGE_ROOT + "index.json": _response(
            PACKAGE_ROOT + "index.json",
            {"versions": [] if absent else [VERSION]},
        ),
        ARCHIVE_URL: _response(ARCHIVE_URL, body=PACKAGE_BYTES),
    }


def _reader(responses):
    transport = Mock(spec=nuget.NuGetReadTransport)
    transport.get.side_effect = lambda url, **_kwargs: responses[url]
    return transport


def _observe(transport, authority):
    return nuget.read_nuget_active_state(
        transport=transport,
        authority=authority,
        token=TOKEN,
        package_id=PACKAGE,
        version=VERSION,
    )


def test_observation_binds_official_identity_inventory_bytes_and_witness(
    authority,
):
    transport = _reader(_responses())
    observed = _observe(transport, authority)
    assert observed.identity.coordinate == PACKAGE.lower() + "@" + VERSION
    assert [item["id"] for item in observed.github_versions] == [71]
    assert observed.package.content == PACKAGE_BYTES
    assert observed.package.sha256 == hashlib.sha256(PACKAGE_BYTES).hexdigest()
    assert observed.package.sha512 == hashlib.sha512(PACKAGE_BYTES).hexdigest()
    assert observed.package.witness == WITNESS
    assert (
        observed.package.witness_sha256 == hashlib.sha256(WITNESS).hexdigest()
    )
    assert observed.package_control["repository"]["permissions"] == {
        "pull": True
    }
    assert [exchange.url for exchange in observed.exchanges] == list(
        _responses()
    )
    authority.inspect_package.assert_called_once_with(PACKAGE_BYTES)
    authority.service_resources.assert_called_once_with(
        _responses()[nuget.NUGET_SERVICE_INDEX].body
    )
    for call in transport.get.call_args_list:
        headers = dict(call.kwargs["headers"])
        if call.args[0].startswith("https://api.github.com/"):
            assert headers["Authorization"] == "Bearer " + TOKEN
        else:
            assert (
                headers["Authorization"]
                == "Basic "
                + base64.b64encode(("hcoona:" + TOKEN).encode()).decode()
            )
        assert headers["Accept-Encoding"] == "identity"


def test_native_equivalent_coordinates_share_resource_identity(authority):
    authority.normalize_identity.side_effect = [
        _native(PACKAGE, "1.2.3.0+build"),
        _native(PACKAGE.lower(), "1.2.3"),
    ]
    left = nuget.normalize_nuget_identity(authority, PACKAGE, "1.2.3.0+build")
    right = nuget.normalize_nuget_identity(authority, PACKAGE.lower(), "1.2.3")
    assert left.resource_key == right.resource_key
    assert left.display_version != right.display_version
    assert authority.normalize_identity.call_args_list[0].args == (
        PACKAGE,
        "1.2.3.0+build",
    )


def test_observation_absence_requires_complete_agreeing_inventories(authority):
    transport = _reader(_responses(absent=True))
    observed = _observe(transport, authority)
    assert observed.package is None
    assert observed.github_versions == ()
    assert observed.active_versions == ()
    assert transport.get.call_count == 4
    authority.inspect_package.assert_not_called()


@pytest.mark.parametrize(
    "scenario",
    [
        "api-only",
        "service-only",
        "duplicate-native",
        "wrong-owner",
        "wrong-repository",
        "wrong-type",
        "missing-control",
        "missing-bytes",
        "encoded-bytes",
        "duplicate-object",
        "unexpected-pagination",
    ],
)
def test_observation_blocks_incomplete_or_contradictory_facts(
    authority, scenario
):
    responses = _responses()
    if scenario in ("api-only", "duplicate-native"):
        values = [] if scenario == "api-only" else [VERSION, VERSION]
        responses[PACKAGE_ROOT + "index.json"] = _response(
            PACKAGE_ROOT + "index.json", {"versions": values}
        )
    elif scenario == "service-only":
        responses[VERSIONS_API] = _response(VERSIONS_API, [])
    elif scenario == "duplicate-object":
        responses[VERSIONS_API] = _response(
            VERSIONS_API, [{"id": 71, "name": VERSION}] * 2
        )
    elif scenario == "unexpected-pagination":
        responses[VERSIONS_API] = replace(
            responses[VERSIONS_API],
            headers=(("Link", '<https://evil.invalid>; rel="next"'),),
        )
    elif scenario in ("missing-bytes", "encoded-bytes"):
        responses[ARCHIVE_URL] = replace(
            responses[ARCHIVE_URL],
            status=404 if scenario == "missing-bytes" else 200,
            headers=()
            if scenario == "missing-bytes"
            else (("Content-Encoding", "gzip"),),
        )
    else:
        control = json.loads(responses[CONTROL].body)
        if scenario == "wrong-owner":
            control["owner"]["login"] = "another-owner"
        elif scenario == "wrong-repository":
            control["repository"]["full_name"] = "hcoona/another"
        elif scenario == "wrong-type":
            control["package_type"] = "npm"
        elif scenario == "missing-control":
            del control["repository"]
        responses[CONTROL] = _response(CONTROL, control)
    transport = _reader(responses)
    with pytest.raises((nuget.NuGetAdapterError, nuget.NuGetTransportError)):
        _observe(transport, authority)
    authority.inspect_package.assert_not_called()


@pytest.mark.parametrize(
    "scenario", ["redirect-status", "followed-redirect", "resource-origin"]
)
def test_observation_rejects_credential_redirect(authority, scenario):
    responses = _responses()
    if scenario == "resource-origin":
        authority.service_resources.return_value["packageBaseAddress"] = (
            "https://storage.example.invalid/hcoona/"
        )
    else:
        responses[ARCHIVE_URL] = replace(
            responses[ARCHIVE_URL],
            status=302 if scenario == "redirect-status" else 200,
            url=ARCHIVE_URL
            if scenario == "redirect-status"
            else "https://storage.example.invalid/blob",
            headers=(("Location", "https://storage.example.invalid/blob"),),
        )
    transport = _reader(responses)
    with pytest.raises(nuget.NuGetAdapterError):
        _observe(transport, authority)
    assert all(
        "storage.example.invalid" not in call.args[0]
        for call in transport.get.call_args_list
    )
    authority.inspect_package.assert_not_called()


def test_observation_blocks_wrong_downloaded_identity_and_missing_witness(
    authority,
):
    authority.inspect_package.return_value["identity"]["normalizedVersion"] = (
        "9.0.0"
    )
    with pytest.raises(
        nuget.NuGetAdapterError, match="another native identity"
    ):
        _observe(_reader(_responses()), authority)
    authority.inspect_package.return_value.pop("witnessBase64")
    with pytest.raises(nuget.NuGetAdapterError, match="incomplete"):
        _observe(_reader(_responses()), authority)


@_REQUIRES_PROFILE_RUNTIME
def test_profile_pins_source_runtime_endpoint_and_credential_free_auth():
    profile = nuget.nuget_operation_profile(RESOURCES)
    assert profile["runtime"] == "CPython@3.13.12"
    assert profile["httpClientSha256"] == nuget.NUGET_HTTP_CLIENT_SHA256
    assert profile["packagePublish"] == PUBLISH
    assert profile["resourceType"] == "PackagePublish/2.0.0"
    assert (
        profile["adapterSha256"]
        == hashlib.sha256(nuget.Path(nuget.__file__).read_bytes()).hexdigest()
    )
    assert all(
        profile[key] == 0
        for key in (
            "transportRetries",
            "applicationRetries",
            "redirects",
            "authenticationResubmissions",
        )
    )
    assert not profile["duplicateSkipping"]
    assert TOKEN not in json.dumps(profile)
    assert profile["authentication"]["X-NuGet-ApiKey"] == "GITHUB_TOKEN"


@_REQUIRES_PROFILE_RUNTIME
def test_publication_rejects_changed_profile_before_network(monkeypatch):
    connection = Mock()
    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    with pytest.raises(nuget.NuGetAdapterError, match="profile or package"):
        nuget.publish_nuget_once(
            resources=RESOURCES,
            package=PACKAGE_BYTES,
            token=TOKEN,
            expected_profile_sha256="0" * 64,
            expected_package_sha256=hashlib.sha256(PACKAGE_BYTES).hexdigest(),
        )
    connection.assert_not_called()


def test_publication_rejects_unpinned_runtime_before_network(monkeypatch):
    if _PINNED_PROFILE_RUNTIME:
        monkeypatch.setattr(nuget.platform, "python_version", lambda: "3.13.13")
    connection = Mock()
    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    with pytest.raises(nuget.NuGetAdapterError, match="runtime is not pinned"):
        nuget.publish_nuget_once(
            resources=RESOURCES,
            package=PACKAGE_BYTES,
            token=TOKEN,
            expected_profile_sha256="0" * 64,
            expected_package_sha256=hashlib.sha256(PACKAGE_BYTES).hexdigest(),
        )
    connection.assert_not_called()


@pytest.fixture
def fault_server(monkeypatch):
    """Route the unchanged HTTP send/read implementation to a loopback socket.

    The fixture substitutes connection addressing and TLS termination only;
    HTTPConnection request/send/response code is the production pinned source.
    """
    received = []
    release = threading.Event()
    behavior = {"kind": 201}

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def do_PUT(self):
            body = self.rfile.read(int(self.headers["Content-Length"]))
            received.append((self.path, self.headers, body))
            kind = behavior["kind"]
            if kind == "timeout":
                release.wait(3)
                return
            if kind == "dropped":
                self.connection.shutdown(socket.SHUT_RDWR)
                self.connection.close()
                return
            self.send_response(
                201 if kind in ("truncated", "oversize") else kind
            )
            self.send_header("Location", PUBLISH + "retry")
            self.send_header("WWW-Authenticate", 'Basic realm="GitHub"')
            self.send_header("Retry-After", "0")
            body = b"" if kind == 204 else b"response"
            if kind == "oversize":
                body = b"x" * (64 * 1024 + 1)
            self.send_header(
                "Content-Length",
                str(len(body) + (5 if kind == "truncated" else 0)),
            )
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(body)
            self.wfile.flush()

        def log_message(self, _format, *_args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    def connection(host, *, timeout, context):
        assert host == "nuget.pkg.github.com"
        assert timeout == 60
        assert context.check_hostname
        assert context.verify_mode == ssl.CERT_REQUIRED
        return http.client.HTTPConnection(
            "127.0.0.1",
            server.server_port,
            timeout=0.1 if behavior["kind"] == "timeout" else 3,
        )

    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    yield behavior, received
    release.set()
    server.shutdown()
    server.server_close()
    thread.join(timeout=3)


def _publish():
    return nuget.publish_nuget_once(
        resources=RESOURCES,
        package=PACKAGE_BYTES,
        token=TOKEN,
        expected_profile_sha256=canonical_sha256(
            nuget.nuget_operation_profile(RESOURCES)
        ),
        expected_package_sha256=hashlib.sha256(PACKAGE_BYTES).hexdigest(),
    )


@_REQUIRES_PROFILE_RUNTIME
def test_local_server_receives_one_exact_multipart_package(fault_server):
    _behavior, received = fault_server
    result = _publish()
    assert result.definitive_success
    assert not result.possibly_mutated
    assert result.response.status == 201
    assert len(received) == 1
    path, headers, body = received[0]
    assert path == "/hcoona/"
    assert headers["X-NuGet-ApiKey"] == TOKEN
    assert (
        headers["Authorization"]
        == "Basic " + base64.b64encode(("hcoona:" + TOKEN).encode()).decode()
    )
    assert headers["Content-Length"] == str(len(body))
    assert headers["Transfer-Encoding"] is None
    mime = BytesParser(policy=policy.default).parsebytes(
        ("Content-Type: " + headers["Content-Type"] + "\r\n\r\n").encode()
        + body
    )
    parts = list(mime.iter_parts())
    assert len(parts) == 1
    assert parts[0].get_filename() == "package.nupkg"
    assert parts[0].get_payload(decode=True) == PACKAGE_BYTES
    assert result.request_body_sha256 == hashlib.sha256(body).hexdigest()


@pytest.mark.parametrize(
    "fault",
    [
        200,
        202,
        204,
        301,
        302,
        303,
        307,
        308,
        401,
        403,
        409,
        429,
        500,
        502,
        503,
        504,
        "timeout",
        "dropped",
    ],
)
@_REQUIRES_PROFILE_RUNTIME
def test_local_server_fault_has_one_put_and_conservative_result(
    fault_server, fault
):
    behavior, received = fault_server
    behavior["kind"] = fault
    result = _publish()
    assert len(received) == 1
    assert received[0][0] == "/hcoona/"
    assert not result.definitive_success
    assert result.possibly_mutated
    if isinstance(fault, int):
        assert result.response.status == fault
        assert result.error_kind is None
    else:
        assert result.response is None
        assert result.error_kind in ("TimeoutError", "RemoteDisconnected")
    assert TOKEN not in repr(result)


@pytest.mark.parametrize("fault", ["truncated", "oversize"])
@_REQUIRES_PROFILE_RUNTIME
def test_local_server_response_limit_and_truncation_are_uncertain(
    fault_server, fault
):
    behavior, received = fault_server
    behavior["kind"] = fault
    result = _publish()
    assert len(received) == 1
    assert result.response is None
    assert result.error_kind is not None
    assert result.possibly_mutated
    assert not result.definitive_success


@pytest.mark.parametrize(
    "url",
    [
        "http://nuget.pkg.github.com/hcoona/",
        "https://nuget.pkg.github.com.evil.invalid/hcoona/",
        "https://token@nuget.pkg.github.com/hcoona/",
        "https://nuget.pkg.github.com/hcoona/#fragment",
        "https://nuget.pkg.github.com/hcoona/../another/",
        "https://nuget.pkg.github.com/hcoona/%2e%2e/another/",
        "https://nuget.pkg.github.com/hcoona/\r\nX:injected",
    ],
)
def test_transport_rejects_unsafe_request_before_connect(monkeypatch, url):
    connection = Mock()
    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    with pytest.raises(nuget.NuGetAdapterError):
        nuget.NuGetHttpTransport().request_once(
            "PUT", url, headers=(), body=b"package", timeout=60, max_bytes=1024
        )
    connection.assert_not_called()


def test_observation_reads_all_pages_and_accepts_terminal_previous_link(
    authority,
):
    versions = [f"1.0.{index}" for index in range(101)]
    responses = _responses()
    authority.normalize_identity.side_effect = lambda package_id, version: (
        _native(package_id, version, version)
    )
    responses[VERSIONS_API] = _response(
        VERSIONS_API,
        [
            {"id": index + 1, "name": version}
            for index, version in enumerate(versions[:100])
        ],
        headers=(("Link", f'<{VERSIONS_API[:-1]}2>; rel="next"'),),
    )
    next_url = VERSIONS_API[:-1] + "2"
    responses[next_url] = _response(
        next_url,
        [{"id": 101, "name": versions[-1]}],
        headers=(("Link", f'<{VERSIONS_API}>; rel="prev"'),),
    )
    responses[PACKAGE_ROOT + "index.json"] = _response(
        PACKAGE_ROOT + "index.json", {"versions": versions}
    )
    observed = _observe(_reader(responses), authority)
    assert observed.package is None
    assert len(observed.active_versions) == 101
    assert observed.github_versions[-1] == {"id": 101, "name": "1.0.100"}
    assert [item.url for item in observed.exchanges].count(next_url) == 1
    authority.inspect_package.assert_not_called()


def test_observation_blocks_inventory_that_never_reaches_terminal_page(
    authority, monkeypatch
):
    monkeypatch.setattr(nuget, "_MAX_VERSION_PAGES", 1)
    responses = _responses()
    versions = [f"1.0.{index}" for index in range(100)]
    authority.normalize_identity.side_effect = lambda package_id, version: (
        _native(package_id, version, version)
    )
    responses[VERSIONS_API] = _response(
        VERSIONS_API,
        [
            {"id": index + 1, "name": version}
            for index, version in enumerate(versions)
        ],
    )
    transport = _reader(responses)
    with pytest.raises(nuget.NuGetAdapterError, match="page bound"):
        _observe(transport, authority)
    assert [call.args[0] for call in transport.get.call_args_list] == [
        nuget.NUGET_SERVICE_INDEX,
        CONTROL,
        VERSIONS_API,
    ]
    authority.inspect_package.assert_not_called()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("packagePublish", "https://nuget.pkg.github.com/another/"),
        ("packagePublish", "https://nuget.pkg.github.com/hcoona/?fallback=1"),
        ("packageBaseAddress", None),
    ],
)
def test_service_discovery_rejects_unbounded_resource_projection(
    authority, field, value
):
    authority.service_resources.return_value[field] = value
    with pytest.raises(nuget.NuGetAdapterError):
        nuget.discover_nuget_resources(authority, b'{"resources":[]}')


@_REQUIRES_PROFILE_RUNTIME
def test_publication_rejects_changed_package_before_network(monkeypatch):
    connection = Mock()
    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    with pytest.raises(nuget.NuGetAdapterError, match="profile or package"):
        nuget.publish_nuget_once(
            resources=RESOURCES,
            package=PACKAGE_BYTES + b"changed",
            token=TOKEN,
            expected_profile_sha256=canonical_sha256(
                nuget.nuget_operation_profile(RESOURCES)
            ),
            expected_package_sha256=hashlib.sha256(PACKAGE_BYTES).hexdigest(),
        )
    connection.assert_not_called()


@pytest.mark.parametrize("token", ["", "new\nheader", "non-ascii-\u03b1"])
@_REQUIRES_PROFILE_RUNTIME
def test_publication_rejects_invalid_token_before_network(monkeypatch, token):
    connection = Mock()
    monkeypatch.setattr(http.client, "HTTPSConnection", connection)
    with pytest.raises(nuget.NuGetAdapterError, match="repository token"):
        nuget.publish_nuget_once(
            resources=RESOURCES,
            package=PACKAGE_BYTES,
            token=token,
            expected_profile_sha256=canonical_sha256(
                nuget.nuget_operation_profile(RESOURCES)
            ),
            expected_package_sha256=hashlib.sha256(PACKAGE_BYTES).hexdigest(),
        )
    connection.assert_not_called()
