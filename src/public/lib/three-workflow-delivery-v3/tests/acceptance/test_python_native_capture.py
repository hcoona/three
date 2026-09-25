"""Actual index readers, complete retained captures and finite HTTP effects."""

import base64
from urllib.parse import quote

import pytest
from three_workflow_delivery_v3.acceptance.python_native_capture import (
    NativeTransport,
    collect_capture,
    replay_capture,
)
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_FILE_BYTES,
    MAX_INDEX_BYTES,
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

from ..adapters.test_pypi import FakeHttp, _entry, _index
from . import test_python_native_fixture as fixture_tests

modeled_fixtures = fixture_tests.modeled_fixtures
_CAPTURE_READS = 5
_INVENTORY_FILES = 5
_SECRET = "pypi-synthetic+/secret"  # noqa: S105 - synthetic, never sent remotely


@pytest.fixture(autouse=True)
def deny_real_registry(monkeypatch):
    """A mistaken concrete HTTPS call cannot leave a local capture test."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real registry/OIDC access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


def witnesses(fixtures):
    """Return the two exact source witnesses for real index readers."""
    return tuple(
        fixtures.distributions[f"{label}/original/wheel"].witness
        for label in ("a", "b")
    )


def captured_pair(fixtures):
    """Capture two full versions alongside an untouched unrelated entry."""
    registry = PythonRegistry("testpypi")
    selected = [
        fixtures.distributions[f"{label}/original/{variant}"]
        for label in ("a", "b")
        for variant in ("wheel", "sdist")
    ]
    unrelated = {
        "filename": "hcoona_release_smoke_python-0.0.1-py3-none-any.whl",
        "url": f"https://{registry.file_host}/packages/unrelated.whl",
        "hashes": {"sha256": "d" * 64},
        "yanked": False,
        "requires-python": ">=3.14",
    }
    index = _index(
        [unrelated, *reversed([_entry(registry, item) for item in selected])]
    )
    http = FakeHttp(
        index,
        *(
            PythonHttpResponse(200, item.content, "application/octet-stream")
            for item in selected
        ),
    )
    transport = NativeTransport(registry, http)
    capture = collect_capture(registry, witnesses(fixtures), transport)
    return capture, transport, http, unrelated


def test_python_native_capture_reuses_index_and_preserves_inventory(
    modeled_fixtures,
):
    """Two actual version readers share one index while all entries survive."""
    capture, transport, http, unrelated = captured_pair(modeled_fixtures)
    assert transport.counts == {"index": 1, "file": 4, "upload": 0}
    assert len(http.calls) == _CAPTURE_READS
    assert capture.inventory[unrelated["filename"]] == unrelated
    assert len(capture.inventory) == _INVENTORY_FILES
    assert {item.digest for item in capture.distributions} == {
        modeled_fixtures.distributions[f"{label}/original/{variant}"].digest
        for label in ("a", "b")
        for variant in ("wheel", "sdist")
    }
    assert all(call[2] == {} for call in http.calls[1:])
    files = capture.files(0)
    replayed = replay_capture(
        files, 0, transport.registry, witnesses(modeled_fixtures)
    )
    assert replayed == capture
    assert len(http.calls) == _CAPTURE_READS


def test_python_native_replay_rejects_404_as_complete_empty_index(
    modeled_fixtures,
):
    """A coherent missing-project capture cannot prove a complete inventory."""
    registry = PythonRegistry("testpypi")
    capture = collect_capture(
        registry,
        witnesses(modeled_fixtures),
        NativeTransport(registry, FakeHttp(_index([]))),
    )
    files = capture.files(0)
    summary = parse_canonical_json(files["capture/c0.json"])
    assert summary["inventory"] == {}
    summary["index-status"] = 404
    files["capture/c0.json"] = canonicalize(summary)
    response = parse_canonical_json(files["capture/c0/index.response.json"])
    response["status"] = 404
    files["capture/c0/index.response.json"] = canonicalize(response)
    with pytest.raises(ValueError, match="complete HTTP 200 index"):
        replay_capture(files, 0, registry, witnesses(modeled_fixtures))


@pytest.mark.parametrize(
    "change", ["raw-index", "raw-file", "inventory", "download-url", "status"]
)
def test_python_native_capture_replay_rejects_substituted_raw_facts(
    modeled_fixtures, change
):
    """Replay inspects actual original bytes and metadata bindings again."""
    capture, transport, _, _ = captured_pair(modeled_fixtures)
    files = capture.files(0)
    metadata = parse_canonical_json(files["capture/c0.json"])
    if change == "raw-index":
        files["capture/c0/index.body"] = b"{}"
    elif change == "raw-file":
        files["capture/c0/file-0.body"] = b"substitution"
    elif change == "inventory":
        metadata["inventory"].pop(next(iter(metadata["inventory"])))
    elif change == "download-url":
        metadata["downloads"][0]["url"] = "https://example.invalid/foreign"
    else:
        metadata["index-status"] = 403
    files["capture/c0.json"] = canonicalize(metadata)
    with pytest.raises(
        ValueError, match=r"Python|capture|complete HTTP 200 index"
    ):
        replay_capture(
            files, 0, transport.registry, witnesses(modeled_fixtures)
        )


@pytest.mark.parametrize(
    "failure", ["malformed-index", "failed-download", "wrong-hash"]
)
def test_python_native_failed_capture_retains_reached_raw_evidence(
    modeled_fixtures, failure
):
    """A read failure preserves its raw diagnostic bytes before stopping."""
    registry = PythonRegistry("testpypi")
    item = modeled_fixtures.distributions["a/original/wheel"]
    download = PythonHttpResponse(
        503 if failure == "failed-download" else 200,
        b"unexpected native bytes",
        "application/octet-stream",
    )
    index = (
        PythonHttpResponse(
            200, b"malformed", "application/vnd.pypi.simple.v1+json"
        )
        if failure == "malformed-index"
        else _index([_entry(registry, item)])
    )
    http = FakeHttp(
        index, *([] if failure == "malformed-index" else [download])
    )
    retained = {}
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting"):
        collect_capture(
            registry,
            witnesses(modeled_fixtures),
            NativeTransport(registry, http),
            retain=retained.__setitem__,
            ordinal=3,
        )
    assert retained["capture/c3/index.body"] == index.body
    assert "capture/c3/index.response.json" in retained
    if failure != "malformed-index":
        assert retained["capture/c3/file-0.body"] == download.body
        assert "capture/c3/file-0.response.json" in retained
    assert "capture/c3.json" not in retained
    assert http.responses == []


@pytest.mark.parametrize(
    ("kind", "method", "limit", "maximum"),
    [
        ("upload", "POST", 10, MAX_RESPONSE_BYTES),
        ("index", "GET", 9, MAX_INDEX_BYTES),
        ("file", "GET", 18, MAX_FILE_BYTES),
    ],
)
def test_python_native_transport_stops_before_exceeding_each_budget(
    kind, method, limit, maximum
):
    """The final admitted operation is sent; the next never reaches HTTP."""
    registry = PythonRegistry("testpypi")
    url = {
        "upload": registry.upload_url,
        "index": registry.index_url,
        "file": f"https://{registry.file_host}/packages/file.whl",
    }[kind]
    http = FakeHttp(
        *(
            PythonHttpResponse(200, b"bounded", "text/plain")
            for _ in range(limit)
        )
    )
    transport = NativeTransport(
        registry, http, clock=lambda: 10.0, deadline=20.0
    )
    for _ in range(limit):
        transport.request(method, url, {}, None, maximum)
    with pytest.raises(ValueError, match="budget exhausted"):
        transport.request(method, url, {}, None, maximum)
    assert len(http.calls) == limit
    assert transport.counts[kind] == limit
    assert sum(transport.counts.values()) == limit


def test_python_native_transport_deadline_blocks_new_requests():
    """Expiry is checked before sending, without a wait or request retry."""
    now = [599.0]
    registry = PythonRegistry("testpypi")
    http = FakeHttp(PythonHttpResponse(200, b"bounded", "text/plain"))
    transport = NativeTransport(
        registry, http, clock=lambda: now[0], deadline=600.0
    )
    transport.request("GET", registry.index_url, {}, None, MAX_INDEX_BYTES)
    now[0] = 600.0
    with pytest.raises(ValueError, match="budget exhausted"):
        transport.request("GET", registry.index_url, {}, None, MAX_INDEX_BYTES)
    assert transport.counts == {"index": 1, "upload": 0, "file": 0}
    assert len(http.calls) == 1


@pytest.mark.parametrize("encoding", ["plain", "base64", "basic", "url"])
@pytest.mark.parametrize("location", ["body", "content-type"])
def test_python_native_transport_rejects_secret_echo_before_retention(
    encoding, location
):
    """A native echo of a capability never becomes a retained response."""
    encoded = {
        "plain": _SECRET,
        "base64": base64.b64encode(_SECRET.encode()).decode(),
        "basic": base64.b64encode(f"__token__:{_SECRET}".encode()).decode(),
        "url": quote(_SECRET, safe=""),
    }[encoding]
    response = PythonHttpResponse(
        400,
        encoded.encode() if location == "body" else b"safe",
        encoded if location == "content-type" else "text/plain",
    )
    registry = PythonRegistry("testpypi")
    http = FakeHttp(response)
    transport = NativeTransport(registry, http, secrets=(_SECRET,))
    with pytest.raises(ValueError, match="unsafe acceptance response") as error:
        transport.request(
            "POST", registry.upload_url, {}, b"fixture", MAX_RESPONSE_BYTES
        )
    assert _SECRET not in str(error.value)
    assert encoded not in str(error.value)
    assert len(http.calls) == 1


@pytest.mark.parametrize(
    "change", ["foreign-origin", "response-budget", "token-endpoint"]
)
def test_python_native_transport_rejects_unadmitted_effects_before_http(change):
    """Suite capability cannot add token refresh, origins or response limits."""
    registry = PythonRegistry("testpypi")
    http = FakeHttp()
    transport = NativeTransport(registry, http)
    method, url, limit = "GET", registry.index_url, MAX_INDEX_BYTES
    if change == "foreign-origin":
        url = PythonRegistry("pypi").index_url
    elif change == "response-budget":
        limit += 1
    else:
        method, url = "POST", registry.origin + "/_/oidc/mint-token"
    with pytest.raises(ValueError, match=r"foreign|bound differs"):
        transport.request(method, url, {}, None, limit)
    assert http.calls == []
