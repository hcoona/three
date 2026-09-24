"""Fake-HTTP registry reads, one-shot uploads and token exchange contracts."""

import base64
import csv
import hashlib
import io
import ssl
import tarfile
import zipfile
from dataclasses import replace
from email import policy
from email.parser import BytesParser
from http import HTTPStatus
from pathlib import Path
from unittest.mock import Mock

import pytest
import tomli_w
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import (
    HTTP_TIMEOUT_SECONDS,
    MAX_FILE_BYTES,
    MAX_INDEX_BYTES,
    MAX_INDEX_FILES,
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonHttpsTransport,
    PythonRegistry,
    mint_python_token,
    read_python_index,
    upload_python_once,
)
from three_workflow_delivery_v3.adapters.python import (
    WITNESS_PATH,
    PythonDistribution,
    PythonPackageTargetWitness,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_IMPORT,
    PYTHON_MANIFEST,
    PYTHON_RELEASE_UNIT,
    PythonNbgvFacts,
    python_digest,
    validate_python_source_manifest,
)

_ROOT = Path(__file__).resolve().parents[6]
_INDEX_TYPE = "application/vnd.pypi.simple.v1+json"
_PYTHON_VERSION = "3.14.3"
_TLS_VERSION = "OpenSSL 3.5.5 27 Jan 2026"
_TOKEN = "pypi-test-only-secret"  # noqa: S105 - synthetic, never leaves process


@pytest.fixture(autouse=True)
def deny_real_network(monkeypatch):
    """Any accidental use of the real HTTP boundary fails locally."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real registry or OIDC network access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


class FakeHttp:
    """Consume a finite response queue while recording every request."""

    def __init__(self, *responses):
        """Retain finite responses and an initially empty effect log."""
        self.responses = list(responses)
        self.calls = []

    def request(self, method, url, headers, body, maximum_bytes):
        """Record one request and consume exactly one supplied outcome."""
        self.calls.append((method, url, dict(headers), body, maximum_bytes))
        assert self.responses, "Unexpected additional HTTP request"
        response = self.responses.pop(0)
        if isinstance(response, Exception):
            raise response
        return response


@pytest.fixture
def witness():
    """Use a public version so no native registry eligibility is simulated."""
    facts = PythonNbgvFacts(
        canonicalize(
            {
                "SimpleVersion": "0.1.0",
                "SemVer2": "0.1.0-beta.7",
                "GitCommitId": "a" * 40,
                "VersionHeight": 7,
                "PublicRelease": True,
            }
        ),
        "0.1.0b7",
    )
    return PythonPackageTargetWitness(
        "a" * 40,
        facts,
        "sha256:" + "b" * 64,
        "sha256:" + "c" * 64,
        "live-release",
    )


def _distribution(variant, witness):
    """Build actual native-format fixture bytes without network dependencies."""
    version = witness.nbgv.pep440_version
    metadata = (
        f"Metadata-Version: 2.4\nName: {PYTHON_RELEASE_UNIT}\n"
        f"Version: {version}\nRequires-Python: >=3.14\n\n"
    ).encode()
    module = b"def project_id(): return 'hcoona-release-smoke-python'\n"
    output = io.BytesIO()
    if variant == "wheel":
        prefix = f"{PYTHON_IMPORT}-{version}.dist-info"
        members = {
            f"{PYTHON_IMPORT}/__init__.py": module,
            WITNESS_PATH: witness.canonical_bytes,
            f"{prefix}/METADATA": metadata,
            f"{prefix}/WHEEL": (
                b"Wheel-Version: 1.0\nRoot-Is-Purelib: true\n"
                b"Tag: py3-none-any\n"
            ),
            f"{prefix}/licenses/LICENSE": b"MIT License\n",
        }
        record = io.StringIO()
        writer = csv.writer(record)
        for name, data in members.items():
            digest = (
                base64.urlsafe_b64encode(hashlib.sha256(data).digest())
                .decode()
                .rstrip("=")
            )
            writer.writerow([name, "sha256=" + digest, len(data)])
        writer.writerow([f"{prefix}/RECORD", "", ""])
        members[f"{prefix}/RECORD"] = record.getvalue().encode()
        with zipfile.ZipFile(output, "w") as archive:
            for name, data in members.items():
                archive.writestr(name, data)
        filename = f"{PYTHON_IMPORT}-{version}-py3-none-any.whl"
    else:
        manifest = validate_python_source_manifest(
            (_ROOT / PYTHON_MANIFEST).read_bytes()
        )
        manifest["project"].pop("dynamic")
        manifest["project"]["version"] = version
        manifest["build-system"]["requires"] = ["hatchling==1.32.0"]
        manifest["tool"]["hatch"].pop("version")
        manifest["tool"].pop("uv")
        members = {
            "pyproject.toml": tomli_w.dumps(manifest).encode(),
            "README.md": b"Smoke\n",
            "LICENSE": b"MIT License\n",
            "PKG-INFO": metadata,
            f"src/{PYTHON_IMPORT}/__init__.py": module,
            f"src/{WITNESS_PATH}": witness.canonical_bytes,
        }
        with tarfile.open(fileobj=output, mode="w:gz") as archive:
            for name, data in members.items():
                info = tarfile.TarInfo(f"{PYTHON_IMPORT}-{version}/{name}")
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
        filename = f"{PYTHON_IMPORT}-{version}.tar.gz"
    return PythonDistribution(variant, filename, output.getvalue(), witness)


@pytest.fixture
def pair(witness):
    """Supply the ordered original wheel and sdist native fixtures."""
    return tuple(
        _distribution(variant, witness) for variant in ("wheel", "sdist")
    )


def _entry(registry, distribution):
    return {
        "filename": distribution.filename,
        "url": f"https://{registry.file_host}/packages/{distribution.filename}",
        "hashes": {"sha256": distribution.digest.removeprefix("sha256:")},
        "yanked": False,
    }


def _index(files, *, status=200, api="1.4"):
    return PythonHttpResponse(
        status,
        canonicalize(
            {
                "meta": {"api-version": api},
                "name": PYTHON_RELEASE_UNIT,
                "files": files,
            }
        ),
        _INDEX_TYPE,
    )


@pytest.mark.parametrize(
    ("name", "origin", "upload", "file_host", "channel"),
    [
        (
            "testpypi",
            "https://test.pypi.org",
            "https://test.pypi.org/legacy/",
            "test-files.pythonhosted.org",
            "buddy",
        ),
        (
            "pypi",
            "https://pypi.org",
            "https://upload.pypi.org/legacy/",
            "files.pythonhosted.org",
            "official",
        ),
    ],
)
def test_python_registries_have_independent_closed_profiles(
    name, origin, upload, file_host, channel
):
    """Destination and authority identities never cross registries."""
    registry = PythonRegistry(name)
    assert registry.origin == origin
    assert registry.upload_url == upload
    assert registry.file_host == file_host
    assert registry.channel == channel
    assert registry.environment == f"workflow-delivery-v3-python-{name}"
    profile = registry.profile
    assert profile["oidc-audience"] == name
    assert profile["mint-token"] == origin + "/_/oidc/mint-token"
    assert profile["python"] == _PYTHON_VERSION
    assert profile["tls"] == _TLS_VERSION
    assert profile["redirects"] == profile["retries"] == 0
    assert profile["upload-posts-per-file"] == 1
    assert profile["proxy"] is False
    assert profile["verify-certificates"] is True
    other = PythonRegistry("pypi" if name == "testpypi" else "testpypi")
    assert registry.profile_digest != other.profile_digest


@pytest.mark.parametrize(
    "name", ["TestPyPI", "https://pypi.org", "buddy", "", "pypi "]
)
def test_python_registry_rejects_aliases_and_origin_overrides(name):
    """Registry configuration admits exact names without origin overrides."""
    with pytest.raises(ValueError, match="unsupported Python registry"):
        PythonRegistry(name)


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
@pytest.mark.parametrize("selection", [(), (0,), (1,), (1, 0)])
def test_python_index_reads_actual_originals_and_classifies_set(
    name, selection, pair, witness
):
    """Fresh native bytes, including reversed listings, determine set state."""
    registry = PythonRegistry(name)
    selected = [pair[index] for index in selection]
    index = _index([_entry(registry, item) for item in selected])
    ordered = sorted(selected, key=lambda item: item.variant != "wheel")
    transport = FakeHttp(
        index,
        *(
            PythonHttpResponse(200, item.content, "application/octet-stream")
            for item in ordered
        ),
    )
    observed = read_python_index(registry, witness, transport)
    expected = (
        "absent"
        if not selection
        else "complete"
        if len(selection) == len(pair)
        else "partial"
    )
    assert observed.classification == expected
    assert observed.version == "0.1.0b7"
    assert observed.index_digest == python_digest(index.body)
    assert [
        (item.variant, item.digest, item.content) for item in observed.files
    ] == [(item.variant, item.digest, item.content) for item in ordered]
    assert transport.calls[0] == (
        "GET",
        registry.index_url,
        {"Accept": _INDEX_TYPE},
        None,
        MAX_INDEX_BYTES,
    )
    assert all(
        call[2:] == ({}, None, MAX_FILE_BYTES) for call in transport.calls[1:]
    )
    assert len(transport.calls) == 1 + len(selection)


def test_python_index_project_not_found_is_absent(witness):
    """A missing project is absent without file downloads."""
    transport = FakeHttp(PythonHttpResponse(404, b"missing", "text/html"))
    observed = read_python_index(PythonRegistry("testpypi"), witness, transport)
    assert observed.classification == "absent"
    assert observed.files == ()
    assert observed.index_digest == python_digest(b"missing")
    assert len(transport.calls) == 1


@pytest.mark.parametrize("api", ["1.0", "1.1", "1.2", "1.3", "1.4"])
def test_python_index_accepts_only_supported_minor_profiles(api, witness):
    """Each supported Simple Index API minor admits a bounded empty set."""
    transport = FakeHttp(_index([], api=api))
    assert (
        read_python_index(
            PythonRegistry("pypi"), witness, transport
        ).classification
        == "absent"
    )
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "change",
    [
        "extra-wheel",
        "duplicate",
        "foreign",
        "unclassifiable",
        "yanked",
        "unknown-yanked",
        "missing-hash",
        "invalid-hash",
        "unsupported-api",
        "excessive",
        "missing-files",
    ],
)
def test_python_index_rejects_unknown_or_conflicting_inventory(
    change, pair, witness
):
    """Unknown file state fails before any original-file download."""
    registry = PythonRegistry("testpypi")
    entry = _entry(registry, pair[0])
    files = [entry]
    response = None
    if change == "extra-wheel":
        files.append(
            {
                **entry,
                "filename": entry["filename"].replace(
                    "-py3-none-any", "-cp314-cp314-linux_x86_64"
                ),
            }
        )
    elif change == "duplicate":
        files.append(dict(entry))
    elif change == "foreign":
        entry["filename"] = "foreign_package-0.1.0b7-py3-none-any.whl"
    elif change == "unclassifiable":
        entry["filename"] = "unknown.zip"
    elif change == "yanked":
        entry["yanked"] = "withdrawn"
    elif change in {"unknown-yanked", "missing-hash"}:
        entry.pop("yanked" if change == "unknown-yanked" else "hashes")
    elif change == "invalid-hash":
        entry["hashes"] = {"sha256": "g" * 64}
    elif change == "unsupported-api":
        response = _index(files, api="2.0")
    elif change == "excessive":
        files = [entry] * (MAX_INDEX_FILES + 1)
    else:
        response = PythonHttpResponse(
            200,
            canonicalize(
                {"meta": {"api-version": "1.4"}, "name": PYTHON_RELEASE_UNIT}
            ),
            _INDEX_TYPE,
        )
    transport = FakeHttp(response or _index(files))
    with pytest.raises(ValueError, match=r"Python|sdist"):
        read_python_index(registry, witness, transport)
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "url",
    [
        "https://files.pythonhosted.org/packages/file.whl",
        "http://test-files.pythonhosted.org/packages/file.whl",
        "https://user:secret@test-files.pythonhosted.org/packages/file.whl",
        "https://test-files.pythonhosted.org/other/file.whl",
        "https://test-files.pythonhosted.org/packages/file.whl?token=secret",
        "https://test-files.pythonhosted.org/packages/file.whl#fragment",
        "https://test-files.pythonhosted.org:443/packages/file.whl",
    ],
)
def test_python_index_rejects_foreign_or_credentialed_file_urls(
    url, pair, witness
):
    """Public downloads cannot cross the destination file-host policy."""
    registry = PythonRegistry("testpypi")
    entry = {**_entry(registry, pair[0]), "url": url}
    transport = FakeHttp(_index([entry]))
    with pytest.raises(ValueError, match="origin policy"):
        read_python_index(registry, witness, transport)
    assert len(transport.calls) == 1


@pytest.mark.parametrize("where", ["index", "file"])
@pytest.mark.parametrize("status", [301, 302, 403, 429, 500])
def test_python_index_never_follows_redirect_or_failed_response(
    where, status, pair, witness
):
    """Unavailable index or file reads stop without redirect or retry."""
    registry = PythonRegistry("pypi")
    responses = [PythonHttpResponse(status, b"refused", _INDEX_TYPE)]
    if where == "file":
        responses.insert(0, _index([_entry(registry, pair[0])]))
    transport = FakeHttp(*responses)
    with pytest.raises(ValueError, match="Python"):
        read_python_index(registry, witness, transport)
    assert len(transport.calls) == (1 if where == "index" else 2)


@pytest.mark.parametrize(
    "change", ["bytes", "witness", "content-type", "project"]
)
def test_python_index_checks_downloaded_bytes_and_exact_witness(
    change, pair, witness
):
    """Actual digest and embedded witness must match the claimed file."""
    registry = PythonRegistry("pypi")
    distribution = pair[0]
    if change == "witness":
        distribution = _distribution(
            "wheel", replace(witness, control_digest="sha256:" + "d" * 64)
        )
    entry = _entry(registry, distribution)
    index = _index([entry])
    if change == "content-type":
        index = replace(index, content_type="text/html")
    elif change == "project":
        index = replace(
            index,
            body=index.body.replace(
                PYTHON_RELEASE_UNIT.encode(), b"foreign-project", 1
            ),
        )
    content = b"tampered" if change == "bytes" else distribution.content
    transport = FakeHttp(
        index, PythonHttpResponse(200, content, "application/octet-stream")
    )
    with pytest.raises(ValueError, match="Python"):
        read_python_index(registry, witness, transport)
    assert len(transport.calls) == (
        1 if change in {"content-type", "project"} else 2
    )


@pytest.mark.parametrize("component", ["python", "tls"])
def test_python_https_transport_rejects_unpinned_runtime(
    monkeypatch, component
):
    """Python and TLS implementation versions are exact profile inputs."""
    monkeypatch.setattr(
        pypi.platform,
        "python_version",
        lambda: _PYTHON_VERSION if component == "tls" else "3.14.2",
    )
    monkeypatch.setattr(
        pypi.ssl,
        "OPENSSL_VERSION",
        _TLS_VERSION if component == "python" else "OpenSSL unsupported",
    )
    with pytest.raises(ValueError, match="admitted profile"):
        PythonHttpsTransport()


@pytest.mark.parametrize(
    "outcome", ["success", "redirect", "oversized", "timeout"]
)
def test_python_https_transport_sends_once_closes_and_bounds_response(
    monkeypatch, outcome
):
    """One verified TLS connection closes on success and failure."""
    monkeypatch.setattr(
        pypi.platform, "python_version", lambda: _PYTHON_VERSION
    )
    monkeypatch.setattr(pypi.ssl, "OPENSSL_VERSION", _TLS_VERSION)
    context = Mock()
    monkeypatch.setattr(pypi.ssl, "create_default_context", lambda: context)
    response = Mock()
    response.status = 302 if outcome == "redirect" else 200
    response.read.return_value = b"extra" if outcome == "oversized" else b"okay"
    response.getheader.return_value = "application/json"
    connection = Mock()
    connection.getresponse.return_value = response
    if outcome == "timeout":
        connection.request.side_effect = TimeoutError("send uncertain")
    factory = Mock(return_value=connection)
    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", factory)
    transport = PythonHttpsTransport()
    if outcome in {"oversized", "timeout"}:
        error = ValueError if outcome == "oversized" else TimeoutError
        with pytest.raises(error):
            transport.request(
                "POST", "https://pypi.org/path", {"X-Test": "value"}, b"body", 4
            )
    else:
        result = transport.request(
            "POST", "https://pypi.org/path", {"X-Test": "value"}, b"body", 4
        )
        assert result.status == response.status
        assert result.body == b"okay"
    factory.assert_called_once_with(
        "pypi.org", 443, timeout=HTTP_TIMEOUT_SECONDS, context=context
    )
    connection.request.assert_called_once_with(
        "POST", "/path", body=b"body", headers={"X-Test": "value"}
    )
    connection.close.assert_called_once_with()
    assert context.minimum_version == ssl.TLSVersion.TLSv1_2
    if outcome != "timeout":
        response.read.assert_called_once_with(5)


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
@pytest.mark.parametrize("variant", ["wheel", "sdist"])
def test_python_upload_maps_complete_multipart_to_exact_original(
    name, variant, pair
):
    """One multipart POST carries every field and original binary payload."""
    registry = PythonRegistry(name)
    original = next(item for item in pair if item.variant == variant)
    transport = FakeHttp(PythonHttpResponse(200, b"accepted", "text/plain"))
    result = upload_python_once(registry, original, _TOKEN, transport)
    assert len(transport.calls) == 1
    method, url, headers, body, budget = transport.calls[0]
    assert (method, url, budget) == (
        "POST",
        registry.upload_url,
        MAX_RESPONSE_BYTES,
    )
    assert (
        base64.b64decode(headers["Authorization"].removeprefix("Basic "))
        == f"__token__:{_TOKEN}".encode()
    )
    message = BytesParser(policy=policy.default).parsebytes(
        (
            f"Content-Type: {headers['Content-Type']}\r\n"
            "MIME-Version: 1.0\r\n\r\n"
        ).encode()
        + body
    )
    parts = list(message.iter_parts())
    fields = {
        part.get_param("name", header="content-disposition"): part.get_payload(
            decode=True
        )
        for part in parts
    }
    assert fields == {
        ":action": b"file_upload",
        "protocol_version": b"1",
        "metadata_version": b"2.4",
        "name": PYTHON_RELEASE_UNIT.encode(),
        "version": b"0.1.0b7",
        "filetype": b"bdist_wheel" if variant == "wheel" else b"sdist",
        "pyversion": b"py3" if variant == "wheel" else b"source",
        "requires_python": b">=3.14",
        "sha256_digest": original.digest.removeprefix("sha256:").encode(),
        "content": original.content,
    }
    assert len(parts) == len(fields)
    assert parts[-1].get_filename() == original.filename
    assert result.classification == "definitive-success"
    assert result.response_digest == python_digest(b"accepted")
    assert _TOKEN not in repr(result)


@pytest.mark.parametrize("status", [200, 400, 409, 301, 302, 403, 429, 500])
def test_python_upload_never_retries_or_skips_duplicate_response(status, pair):
    """Every native response is terminal for this single file send."""
    transport = FakeHttp(
        PythonHttpResponse(status, b"native response", "text/plain")
    )
    result = upload_python_once(
        PythonRegistry("pypi"), pair[0], _TOKEN, transport
    )
    assert result.classification == (
        "definitive-success"
        if status == HTTPStatus.OK
        else "definitive-non-success"
    )
    assert result.status == status
    assert result.response_digest == python_digest(b"native response")
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "error",
    [
        TimeoutError("after send"),
        ConnectionError("lost response"),
        ValueError("response oversized"),
    ],
)
def test_python_upload_exception_retains_ambiguity_without_resend(error, pair):
    """A lost response retains possible mutation and prevents resend."""
    transport = FakeHttp(error)
    result = upload_python_once(
        PythonRegistry("testpypi"), pair[0], _TOKEN, transport
    )
    assert result.classification == "ambiguous"
    assert result.status is None
    assert result.response_digest is None
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "token", ["static-token", "pypi-injected\r\nAuthorization: other", ""]
)
def test_python_upload_rejects_invalid_token_before_send(token, pair):
    """Invalid credentials fail before the upload request boundary."""
    transport = FakeHttp()
    with pytest.raises(ValueError, match="short-lived registry token"):
        upload_python_once(PythonRegistry("pypi"), pair[0], token, transport)
    assert transport.calls == []


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_token_exchange_uses_exact_destination_and_one_assertion(name):
    """One assertion exchange returns the destination short-lived token."""
    registry = PythonRegistry(name)
    transport = FakeHttp(
        PythonHttpResponse(
            200, canonicalize({"token": _TOKEN}), "application/json"
        )
    )
    assert (
        mint_python_token(registry, "test-only-assertion", transport) == _TOKEN
    )
    assert transport.calls == [
        (
            "POST",
            registry.origin + "/_/oidc/mint-token",
            {"Content-Type": "application/json"},
            canonicalize({"token": "test-only-assertion"}),
            MAX_RESPONSE_BYTES,
        )
    ]


@pytest.mark.parametrize(
    "failure",
    [
        "rejected",
        "redirect",
        "missing",
        "malformed-json",
        "wrong-prefix",
        "newline",
        "timeout",
    ],
)
def test_python_token_exchange_fails_closed_without_fallback(failure):
    """Malformed or unavailable token responses stop without fallback."""
    if failure == "timeout":
        response = TimeoutError("token endpoint unavailable")
    elif failure in {"rejected", "redirect"}:
        response = PythonHttpResponse(
            403 if failure == "rejected" else 302, b"refused", "text/plain"
        )
    else:
        body = {
            "missing": b"{}",
            "malformed-json": b"{",
            "wrong-prefix": canonicalize({"token": "other"}),
            "newline": canonicalize({"token": "pypi-value\n"}),
        }[failure]
        response = PythonHttpResponse(200, body, "application/json")
    transport = FakeHttp(response)
    error = TimeoutError if failure == "timeout" else ValueError
    with pytest.raises(error):
        mint_python_token(PythonRegistry("testpypi"), "assertion", transport)
    assert len(transport.calls) == 1


@pytest.mark.parametrize("operation", ["index", "upload"])
def test_python_registry_operations_reject_local_version_before_network(
    operation, witness
):
    """CI-valid local metadata never obtains a registry read or upload."""
    raw = {
        "SimpleVersion": "0.1.0",
        "SemVer2": "0.1.0-beta.7+abc1234",
        "GitCommitId": witness.target,
        "VersionHeight": 7,
        "PublicRelease": False,
    }
    local = replace(
        witness,
        nbgv=PythonNbgvFacts(canonicalize(raw), "0.1.0b7+abc1234"),
    )
    registry = PythonRegistry("testpypi")
    transport = FakeHttp()
    if operation == "index":
        with pytest.raises(ValueError, match="public version without local"):
            read_python_index(registry, local, transport)
    else:
        with pytest.raises(ValueError, match="public version without local"):
            upload_python_once(
                registry, _distribution("wheel", local), _TOKEN, transport
            )
    assert transport.calls == []


def test_python_upload_rejects_changed_original_before_send(pair):
    """An original-file identity cannot authorize different archive bytes."""
    changed = replace(pair[0], content=b"not the qualified wheel")
    transport = FakeHttp()
    with pytest.raises(zipfile.BadZipFile):
        upload_python_once(PythonRegistry("pypi"), changed, _TOKEN, transport)
    assert transport.calls == []


@pytest.mark.parametrize("assertion", ["", " assertion", "assertion\n"])
def test_python_token_exchange_rejects_malformed_assertion_before_send(
    assertion,
):
    """Malformed assertion input cannot fall back to ambient authentication."""
    transport = FakeHttp()
    with pytest.raises(ValueError, match="Python record string"):
        mint_python_token(PythonRegistry("pypi"), assertion, transport)
    assert transport.calls == []
