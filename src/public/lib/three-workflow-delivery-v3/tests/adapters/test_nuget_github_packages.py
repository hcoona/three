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
from unittest.mock import MagicMock, Mock

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


@pytest.mark.parametrize("status", [301, 302])
def test_package_read_follows_one_original_location_with_safe_exchanges(
    authority, status
):
    location = (
        "https://packages-storage.example/objects/a%2Fb.nupkg?sig=private&v=1"
    )
    responses = _responses()
    redirect_body = ("<a href='" + location + "'>download</a>").encode()
    source = _response(
        ARCHIVE_URL,
        body=redirect_body,
        status=status,
        headers=(("Location", location),),
    )
    responses[ARCHIVE_URL] = source
    transport = _reader(responses)
    transport.get_package_storage.return_value = _response(
        location,
        body=PACKAGE_BYTES,
        headers=(
            ("Content-Length", str(len(PACKAGE_BYTES))),
            ("Set-Cookie", "unretained-cookie"),
        ),
    )
    state = _observe(transport, authority)
    assert transport.get.call_count == 5
    transport.get_package_storage.assert_called_once()
    call = transport.get_package_storage.call_args
    assert call.args == (source,)
    assert set(call.kwargs) == {"timeout", "max_bytes"}
    assert 0 < call.kwargs["timeout"] <= 60
    assert state.package.content == PACKAGE_BYTES
    assert state.package.witness == WITNESS
    assert len(state.exchanges) == 6
    assert state.exchanges[-2].body == b""
    assert state.exchanges[-2].omitted_body_bytes == len(redirect_body)
    assert (
        state.exchanges[-2].location_sha256
        == hashlib.sha256(location.encode()).hexdigest()
    )
    assert state.exchanges[-1].url == ARCHIVE_URL
    assert state.exchanges[-1].body == PACKAGE_BYTES
    assert (
        state.exchanges[-1].storage_origin == "https://packages-storage.example"
    )
    assert {response.url: response for response in state.exchanges}[
        CONTROL
    ].body == responses[CONTROL].body
    assert location not in repr(state.exchanges)
    assert all(
        response.header("location") is None for response in state.exchanges
    )
    assert state.exchanges[-1].header("set-cookie") is None


@pytest.mark.parametrize(
    "location",
    [
        None,
        "",
        "http://storage.example/a",
        "https://127.1/a",
        " https://storage.example/a",
        "https://storage.example/a#",
        "https://user@storage.example/a",
        "https://storage.example:8443/a",
        "https://storage.example/a?sig=" + TOKEN,
        "https://storage.example/a?sig="
        + "".join(f"%{ord(c):02X}" for c in TOKEN),
    ],
)
def test_package_read_rejects_invalid_location_without_storage(
    authority, location
):
    responses = _responses()
    responses[ARCHIVE_URL] = _response(
        ARCHIVE_URL,
        status=302,
        body=b"redirect",
        headers=() if location is None else (("Location", location),),
    )
    transport = _reader(responses)
    with pytest.raises(nuget.NuGetAdapterError):
        _observe(transport, authority)
    transport.get_package_storage.assert_not_called()
    authority.inspect_package.assert_not_called()


@pytest.mark.parametrize(
    "failure",
    ["second-hop", "status", "reflection", "target", "exception", "late"],
)
def test_package_storage_failure_never_becomes_byte_proof(
    authority, monkeypatch, failure
):
    location = "https://storage.example/objects/package?sig=private-capability"
    responses = _responses()
    responses[ARCHIVE_URL] = _response(
        ARCHIVE_URL,
        status=302,
        body=b"redirect",
        headers=(("Location", location),),
    )
    transport = _reader(responses)
    terminal = _response(location, body=PACKAGE_BYTES)
    if failure == "second-hop":
        terminal = replace(
            terminal, status=302, headers=(("Location", location),)
        )
    elif failure == "status":
        terminal = replace(terminal, status=403, body=location.encode())
    elif failure == "reflection":
        terminal = replace(terminal, body=location.encode())
    elif failure == "target":
        terminal = replace(terminal, url="https://other.example/objects/a")
    if failure == "exception":
        transport.get_package_storage.side_effect = OSError(location)
    elif failure == "late":
        elapsed = [0.0]
        monkeypatch.setattr(nuget.time, "monotonic", lambda: elapsed[0])
        transport.get_package_storage.side_effect = lambda *_args, **_kwargs: (
            elapsed.__setitem__(0, 61.0) or terminal
        )
    else:
        transport.get_package_storage.return_value = terminal
    with pytest.raises(
        (nuget.NuGetAdapterError, nuget.NuGetTransportError)
    ) as error:
        _observe(transport, authority)
    assert "private-capability" not in str(error.value)
    assert transport.get.call_count == 5
    transport.get_package_storage.assert_called_once()
    authority.inspect_package.assert_not_called()


@pytest.mark.parametrize("suffix", ["?sig=a%2Fb&v=1", "?"])
def test_storage_transport_sends_one_exact_credential_free_get(
    monkeypatch, suffix
):
    location = "https://Storage.example/objects/a%2Fb.nupkg" + suffix
    source = _response(
        ARCHIVE_URL,
        status=302,
        body=b"redirect",
        headers=(("Location", location),),
    )
    response = MagicMock()
    response.__enter__.return_value = response
    response.status = 200
    response.getheaders.return_value = [
        ("Content-Length", str(len(PACKAGE_BYTES)))
    ]
    response.read.return_value = PACKAGE_BYTES
    connection = Mock()
    connection.getresponse.return_value = response
    factory = Mock(return_value=connection)
    monkeypatch.setattr(nuget.http.client, "HTTPSConnection", factory)
    result = nuget.NuGetHttpTransport().get_package_storage(
        source, timeout=3.5, max_bytes=128
    )
    assert result.url == location
    assert result.body == PACKAGE_BYTES
    assert factory.call_args.args == ("storage.example",)
    assert factory.call_args.kwargs["timeout"] == 3.5
    connection.connect.assert_called_once_with()
    connection.request.assert_called_once_with(
        "GET",
        "/objects/a%2Fb.nupkg" + suffix,
        body=None,
        headers={
            "Accept": "application/octet-stream",
            "Accept-Encoding": "identity",
        },
        encode_chunked=False,
    )
    assert connection.auto_open == 0
    response.read.assert_called_once_with(129)
    connection.close.assert_called_once_with()


@pytest.mark.parametrize("display_version", [VERSION, VERSION + "+build"])
def test_observation_binds_official_identity_inventory_bytes_and_witness(
    authority,
    display_version,
):
    responses = _responses()
    responses[VERSIONS_API] = _response(
        VERSIONS_API, [{"id": 71, "name": display_version}]
    )
    transport = _reader(responses)
    observed = _observe(transport, authority)
    assert observed.identity.coordinate == PACKAGE.lower() + "@" + VERSION
    assert [item["id"] for item in observed.github_versions] == [71]
    assert observed.github_versions[0]["name"] == display_version
    assert observed.github_coordinates == (
        (71, PACKAGE.lower() + "@" + VERSION),
    )
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
    assert observed.github_coordinates == ()
    assert observed.active_versions == ()
    assert transport.get.call_count == 4
    authority.inspect_package.assert_not_called()


def test_observation_accepts_actual_owner_root_publish_resource(authority):
    # Minimal public resource shape from the independently audited discovery
    # failure; the helper projection remains a controlled dependency seam.
    index = json.dumps(
        {
            "version": "3.0.0",
            "resources": [
                {
                    "@id": "https://nuget.pkg.github.com/hcoona/download",
                    "@type": "PackageBaseAddress/3.0.0",
                },
                {
                    "@id": "https://nuget.pkg.github.com/hcoona",
                    "@type": "PackagePublish/2.0.0",
                },
            ],
        }
    ).encode()
    authority.service_resources.return_value = {
        "packageBaseAddress": "https://nuget.pkg.github.com/hcoona/download",
        "packagePublish": "https://nuget.pkg.github.com/hcoona",
    }
    responses = _responses(absent=True)
    responses[nuget.NUGET_SERVICE_INDEX] = _response(
        nuget.NUGET_SERVICE_INDEX, body=index
    )
    observed = _observe(_reader(responses), authority)
    assert observed.resources == nuget.NuGetServiceResources(
        BASE,
        "https://nuget.pkg.github.com/hcoona",
        hashlib.sha256(index).hexdigest(),
    )
    assert observed.package_control["id"] == 12024661
    assert observed.package_control["name"] == PACKAGE
    assert observed.package is None
    assert observed.active_versions == ()
    assert observed.github_versions == ()
    assert observed.github_coordinates == ()
    assert [exchange.url for exchange in observed.exchanges] == [
        nuget.NUGET_SERVICE_INDEX,
        CONTROL,
        VERSIONS_API,
        PACKAGE_ROOT + "index.json",
    ]
    authority.service_resources.assert_called_once_with(index)
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
    assert profile["successStatuses"] == [200, 201, 202]
    assert "successStatus" not in profile
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


@pytest.mark.parametrize(
    "statuses", [None, [201], [200, 201, 202, 204], ["200", 201, 202]]
)
@_REQUIRES_PROFILE_RUNTIME
def test_profile_rejects_unselected_success_status_contract(statuses):
    profile = nuget.nuget_operation_profile(RESOURCES)
    if statuses is None:
        profile.pop("successStatuses")
        profile["successStatus"] = 201
    else:
        profile["successStatuses"] = statuses
    with pytest.raises(nuget.NuGetAdapterError, match="closed HTTP contract"):
        nuget.validate_nuget_operation_profile(profile)


@pytest.mark.parametrize(
    ("status", "error"),
    [
        (204, None),
        (200.0, None),
        ("200", None),
        (True, None),
        (None, None),
        (200, "incomplete"),
        (202, "incomplete"),
    ],
)
def test_invocation_rejects_unselected_or_ambiguous_success(status, error):
    result = nuget.NuGetPublicationInvocation(
        "a" * 64,
        "b" * 64,
        "c" * 64,
        None
        if status is None
        else nuget.NuGetHttpResponse(PUBLISH, status, (), b"registered"),
        error,
    )
    assert not result.definitive_success
    assert result.possibly_mutated


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


@pytest.mark.parametrize("status", [200, 201, 202])
@_REQUIRES_PROFILE_RUNTIME
def test_local_server_receives_one_exact_multipart_package(
    fault_server, status
):
    behavior, received = fault_server
    behavior["kind"] = status
    result = _publish()
    assert result.definitive_success
    assert not result.possibly_mutated
    assert result.response.status == status
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
        203,
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
    assert observed.github_coordinates == tuple(
        (index + 1, PACKAGE.lower() + "@" + version)
        for index, version in enumerate(versions)
    )
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
        ("packagePublish", "https://nuget.pkg.github.com/hcoona?fallback=1"),
        ("packagePublish", "https://nuget.pkg.github.com/hcoonax"),
        ("packageBaseAddress", "https://nuget.pkg.github.com/hcoonax"),
        (
            "packagePublish",
            "https://nuget.pkg.github.com/hcoona-other/download",
        ),
        (
            "packageBaseAddress",
            "https://nuget.pkg.github.com/hcoona-other/download",
        ),
        ("packagePublish", "https://foreign.invalid/hcoona"),
        ("packagePublish", "https://nuget.pkg.github.com/hcoona/../another"),
        (
            "packageBaseAddress",
            "https://nuget.pkg.github.com/hcoona/%2e%2e/another",
        ),
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
