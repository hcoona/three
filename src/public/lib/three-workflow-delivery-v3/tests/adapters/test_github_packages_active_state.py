"""Active readback scenarios without authorization or legacy observation."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004, SLF001
import hashlib
import http.client
import importlib.util
import io
import json
import sys
import urllib.error
import urllib.request
import urllib.response
from dataclasses import replace
from email.message import Message
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters.github_packages import (
    GitHubPackagesHttpResponse,
    GitHubPackagesHttpTransport,
    GitHubPackagesNetworkError,
    GitHubPackagesPolicyError,
    read_github_packages_active_state,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize

_SPEC = importlib.util.spec_from_file_location(
    "wdv3_npmjs_active_readback_fixtures",
    Path(__file__).with_name("test_npmjs.py"),
)
assert _SPEC is not None
assert _SPEC.loader is not None
_fixtures = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = _fixtures
_SPEC.loader.exec_module(_fixtures)

PACKAGE = "@hcoona/hcoona-release-smoke-npm"
VERSION = "1.2.3-beta.42.ge123456"
TARGET = "e" * 40
TAG = "buddy-sha-" + TARGET
TOKEN = "active-readback-secret-token"  # noqa: S105
OBSERVED_AT = "2026-09-05T18:00:00Z"
CONTROL_URL = (
    "https://api.github.com/users/hcoona/packages/npm/hcoona-release-smoke-npm"
)
TAGS_URL = "https://npm.pkg.github.com/@hcoona%2Fhcoona-release-smoke-npm"
EXACT_URL = TAGS_URL + "/" + VERSION
TARBALL_URL = (
    "https://npm.pkg.github.com/@hcoona/hcoona-release-smoke-npm/-/package.tgz"
)


@pytest.fixture
def basis(monkeypatch):
    simulation = _fixtures.qualified_simulation.__wrapped__(monkeypatch)
    tarball = _fixtures._tarball(simulation)
    return replace(
        simulation,
        tarball=tarball,
        artifact=_fixtures._artifact_for_tarball(simulation, tarball),
    )


class ScenarioTransport:
    """Serve only the scenario's read endpoints, without external effects."""

    def __init__(self, responses):
        """Keep only explicit local response scenarios."""
        self.responses = responses
        self.requests = []

    def get(self, url, *, headers, timeout, max_bytes):
        """Capture reads and reject any undeclared endpoint."""
        self.requests.append((url, headers, timeout, max_bytes))
        response = self.responses[url]
        if isinstance(response, BaseException):
            raise response
        return response


def _response(url, document=None, *, status=200, **changes):
    return replace(
        GitHubPackagesHttpResponse(
            url=url,
            status=status,
            headers=(),
            body=json.dumps(document).encode(),
        ),
        **changes,
    )


def _control(**changes):
    return {
        "name": "hcoona-release-smoke-npm",
        "package_type": "npm",
        "visibility": "public",
        "repository": {
            "full_name": "hcoona/three",
            "permissions": {
                "admin": True,
                "maintain": True,
                "push": True,
                "triage": True,
                "pull": True,
            },
        },
        **changes,
    }


def _responses(basis):
    return {
        CONTROL_URL: _response(CONTROL_URL, _control()),
        EXACT_URL: _response(
            EXACT_URL,
            {
                "name": PACKAGE,
                "version": VERSION,
                "dist": {
                    "tarball": TARBALL_URL,
                    "integrity": "sha512-not-authoritative",
                },
            },
        ),
        TARBALL_URL: _response(TARBALL_URL, body=basis.tarball),
        TAGS_URL: _response(TAGS_URL, {"name": PACKAGE, "dist-tags": {}}),
    }


def _read(basis, responses, **options):
    return read_github_packages_active_state(
        basis.artifact,
        basis.expectation,
        token=TOKEN,
        observed_at=OBSERVED_AT,
        transport=ScenarioTransport(responses),
        **options,
    )


@pytest.mark.parametrize("name", ["hcoona-release-smoke-npm", PACKAGE])
@pytest.mark.parametrize(
    "owner", [{}, {"owner": None}, {"owner": {"login": "hcoona"}}]
)
def test_control_proof_uses_user_route_without_inventing_access(
    basis, name, owner
):
    responses = _responses(basis)
    responses[CONTROL_URL] = _response(
        CONTROL_URL, _control(name=name, **owner)
    )
    result = _read(basis, responses)

    proof = result.package_control
    assert proof is not None
    assert proof.subject.to_document() == {
        "destination-id": "npm/github-packages-hcoona-three-v1",
        "registry": "https://npm.pkg.github.com",
        "normalized-package": PACKAGE,
    }
    assert proof.facts == (
        ("exposed-access", ()),
        ("owner", ("hcoona",)),
        ("repository-association", ("hcoona/three",)),
        ("visibility", ("public",)),
    )
    assert proof.observed_at == OBSERVED_AT
    assert proof.endpoints == (CONTROL_URL,)
    expected_response_digest = canonical_sha256(
        {
            "stage": "rest",
            "requested-url": CONTROL_URL,
            "final-url": CONTROL_URL,
            "redirects": [],
            "status": 200,
            "selected-headers": [],
            "truncated": False,
            "complete": True,
            "body-sha256": "sha256:"
            + hashlib.sha256(responses[CONTROL_URL].body).hexdigest(),
            "detail": None,
        }
    )
    assert proof.response_digests == ((CONTROL_URL, expected_response_digest),)
    assert result.diagnostics.entries == ()


@pytest.mark.parametrize(
    ("changes", "fact", "observed"),
    [
        ({"owner": {"login": "Other-User"}}, "owner", "other-user"),
        (
            {"repository": {"full_name": "Other/Repository"}},
            "repository-association",
            "other/repository",
        ),
        ({"visibility": "private"}, "visibility", "private"),
    ],
)
def test_differing_control_facts_are_not_governance_admission(
    basis, changes, fact, observed
):
    responses = _responses(basis)
    responses[CONTROL_URL] = _response(CONTROL_URL, _control(**changes))
    result = _read(basis, responses)

    assert result.package_control is not None
    assert dict(result.package_control.facts)[fact] == (observed,)
    assert result.readback.classification == "exact-satisfied"


@pytest.mark.parametrize(
    "changes",
    [
        {"name": "@other/hcoona-release-smoke-npm"},
        {"name": "other-package"},
        {"package_type": "container"},
        {"owner": {"login": "hcoona", "type": "Organization"}},
        {"owner": {}},
        {"repository": None},
        {"repository": {"permissions": {"admin": True}}},
        {"repository": {"full_name": "not-a-repository"}},
        {"visibility": None},
    ],
)
def test_incomplete_or_wrong_control_identity_has_no_proof(basis, changes):
    responses = _responses(basis)
    responses[CONTROL_URL] = _response(CONTROL_URL, _control(**changes))
    result = _read(basis, responses)

    assert result.package_control is None
    assert result.readback.classification == "exact-satisfied"
    assert result.diagnostics.entries == ("package-control: unprovable",)


@pytest.mark.parametrize(
    "field", ["name", "package_type", "repository", "visibility"]
)
def test_missing_required_control_fact_is_not_filled_from_desired(basis, field):
    document = _control()
    del document[field]
    responses = _responses(basis)
    responses[CONTROL_URL] = _response(CONTROL_URL, document)
    assert _read(basis, responses).package_control is None


_TAG_CASES = [
    pytest.param({}, "absent", None, id="absent"),
    pytest.param({TAG: VERSION}, "present", VERSION, id="same"),
    pytest.param({TAG: "9.0.0"}, "present", "9.0.0", id="elsewhere"),
    pytest.param({TAG: None}, "unreadable", None, id="null"),
    pytest.param({TAG: ""}, "unreadable", None, id="empty"),
    pytest.param({"other-tag": None}, "absent", None, id="unrelated-tag"),
    pytest.param([], "unreadable", None, id="malformed-tags"),
    pytest.param(
        b'{"name":"@other/package","dist-tags":{}}',
        "unreadable",
        None,
        id="wrong-package",
    ),
    pytest.param(404, "absent", None, id="http-absence"),
    pytest.param(403, "unreadable", None, id="http-denial"),
    pytest.param(b"{", "unreadable", None, id="invalid-json"),
    pytest.param(
        GitHubPackagesNetworkError(TOKEN), "unreadable", None, id="network"
    ),
]


def _tag_response(value):
    if isinstance(value, BaseException):
        return value
    if type(value) is int:
        return _response(TAGS_URL, status=value)
    if isinstance(value, bytes):
        return _response(TAGS_URL, body=value)
    return _response(TAGS_URL, {"name": PACKAGE, "dist-tags": value})


@pytest.mark.parametrize(("tag_value", "tag_state", "tag_version"), _TAG_CASES)
@pytest.mark.parametrize("version_state", ["exact-satisfied", "absent"])
def test_version_classification_keeps_independent_tag_state(
    basis, tag_value, tag_state, tag_version, version_state
):
    responses = _responses(basis)
    responses[TAGS_URL] = _tag_response(tag_value)
    if version_state == "absent":
        responses[EXACT_URL] = _response(EXACT_URL, status=404)
        del responses[TARBALL_URL]
    result = _read(basis, responses)
    readback = result.readback

    assert readback.classification == version_state
    assert (readback.package, readback.version) == (PACKAGE, VERSION)
    assert (readback.tag, readback.tag_state, readback.tag_version) == (
        TAG,
        tag_state,
        tag_version,
    )
    assert readback.observed_at == OBSERVED_AT
    facts = (
        readback.content_sha256,
        readback.content_sha512,
        readback.witness_digest,
        readback.witness_target,
    )
    if version_state == "absent":
        assert facts == (None, None, None, None)
        assert dict(readback.response_digests).keys() == {
            "npm-metadata",
            "npm-tags",
        }
    else:
        assert facts == (
            "sha256:" + hashlib.sha256(basis.tarball).hexdigest(),
            "sha512:" + hashlib.sha512(basis.tarball).hexdigest(),
            "sha256:"
            + hashlib.sha256(basis.expectation.witness_bytes).hexdigest(),
            TARGET,
        )
    assert result.package_control is not None


@pytest.mark.parametrize("url", [CONTROL_URL, EXACT_URL, TARBALL_URL, TAGS_URL])
@pytest.mark.parametrize(
    ("failure", "classification"),
    [
        (401, "unprovable"),
        (403, "unprovable"),
        (503, "unknown"),
        ("network", "unknown"),
        ("policy", "unprovable"),
        ("off-origin", "unprovable"),
        ("truncated", "unknown"),
        ("oversize", "unknown"),
        ("invalid-body", "unprovable"),
        ("encoding", "unprovable"),
    ],
)
def test_failed_reads_are_classified_without_hiding_independent_facts(
    basis, url, failure, classification
):
    responses = _responses(basis)
    response = responses[url]
    if isinstance(failure, int):
        response = replace(response, status=failure)
    elif failure == "network":
        response = GitHubPackagesNetworkError(TOKEN)
    elif failure == "policy":
        response = GitHubPackagesPolicyError(TOKEN)
    elif failure == "off-origin":
        response = replace(response, url="https://outside.invalid/" + TOKEN)
    elif failure == "truncated":
        response = replace(response, truncated=True)
    elif failure == "oversize":
        response = replace(response, body=b"x" * 1001)
    elif failure == "invalid-body":
        response = replace(response, body=b"{")
    elif failure == "encoding":
        response = replace(response, headers=(("Content-Encoding", "gzip"),))
    responses[url] = response
    result = _read(
        basis, responses, metadata_limit_bytes=1000, tarball_limit_bytes=1000
    )

    assert (result.package_control is None) == (url == CONTROL_URL)
    assert result.readback.classification == (
        classification if url in {EXACT_URL, TARBALL_URL} else "exact-satisfied"
    )
    assert result.readback.tag_state == (
        "unreadable" if url == TAGS_URL else "absent"
    )
    assert result.diagnostics.entries
    assert len(result.diagnostics.entries) <= 3
    assert not result.diagnostics.truncated
    assert TOKEN not in repr(result)
    assert result.response_identity.startswith("sha256:")


@pytest.mark.parametrize(
    ("url", "classification", "proof_available"),
    [
        (CONTROL_URL, "exact-satisfied", False),
        (EXACT_URL, "absent", True),
        (TARBALL_URL, "unprovable", True),
    ],
)
def test_http_absence_is_specific_to_the_requested_resource(
    basis, url, classification, proof_available
):
    responses = _responses(basis)
    responses[url] = _response(url, status=404)
    result = _read(basis, responses)
    assert result.readback.classification == classification
    assert (result.package_control is not None) == proof_available


@pytest.mark.parametrize("url", [CONTROL_URL, EXACT_URL, TAGS_URL])
def test_same_origin_wrong_route_cannot_assert_identity_or_absence(basis, url):
    responses = _responses(basis)
    responses[url] = _response(url, status=404, redirects=(url + "/other",))
    result = _read(basis, responses)
    if url == CONTROL_URL:
        assert result.package_control is None
    elif url == EXACT_URL:
        assert result.readback.classification == "unprovable"
    else:
        assert result.readback.tag_state == "unreadable"


@pytest.mark.parametrize(
    "changes",
    [
        {"name": "@other/package"},
        {"version": "9.0.0"},
        {"dist": {"integrity": "sha512-not-bytes"}},
        {"dist": {"tarball": "https://outside.invalid/package.tgz"}},
        {"dist": {"tarball": "https://["}},
    ],
)
def test_exact_metadata_alone_never_proves_exact_bytes(basis, changes):
    responses = _responses(basis)
    document = json.loads(responses[EXACT_URL].body)
    responses[EXACT_URL] = _response(EXACT_URL, {**document, **changes})
    del responses[TARBALL_URL]
    result = _read(basis, responses)
    assert result.readback.classification == "unprovable"
    assert result.readback.content_sha512 is None
    assert result.readback.tag_state == "absent"


@pytest.mark.parametrize(
    ("change", "classification"),
    [
        ("bytes", "conflicting"),
        ("target", "conflicting"),
        ("invalid-witness", "unprovable"),
        ("invalid-tarball", "unprovable"),
        ("expansion-limit", "unprovable"),
    ],
)
def test_real_tarball_bytes_and_embedded_witness_determine_exactness(
    basis, change, classification
):
    options = {}
    tarball = basis.tarball
    if change == "bytes":
        tarball = _fixtures._tarball(
            basis,
            extra=(("package/dist/index.js", b"export const changed=1;"),),
        )
    elif change == "target":
        witness = json.loads(basis.expectation.witness_bytes)
        witness["target"] = "b" * 40
        witness["nbgv"]["canonical"]["gitCommitId"] = "b" * 40
        tarball = _fixtures._tarball(basis, witness=canonicalize(witness))
    elif change == "invalid-witness":
        tarball = _fixtures._tarball(basis, witness=b"not-a-witness")
    elif change == "invalid-tarball":
        tarball = b"not-a-tarball"
    elif change == "expansion-limit":
        options["expanded_tarball_limit_bytes"] = 1
    responses = _responses(basis)
    responses[TARBALL_URL] = _response(TARBALL_URL, body=tarball)
    result = _read(basis, responses, **options)

    assert result.readback.classification == classification
    assert result.readback.content_sha256 == (
        "sha256:" + hashlib.sha256(tarball).hexdigest()
    )
    assert result.readback.content_sha512 == (
        "sha512:" + hashlib.sha512(tarball).hexdigest()
    )
    if change == "target":
        assert result.readback.witness_target == "b" * 40
        assert result.readback.witness_digest == canonical_sha256(witness)
    elif classification == "unprovable":
        assert result.readback.witness_digest is None
    assert result.package_control is not None


@pytest.mark.parametrize(
    "tarball_url",
    [
        TARBALL_URL,
        "https://objects.githubusercontent.com/package.tgz",
    ],
)
def test_transport_uses_only_active_gets_with_origin_scoped_auth(
    basis, tarball_url
):
    responses = _responses(basis)
    responses[EXACT_URL] = _response(
        EXACT_URL,
        {
            "name": PACKAGE,
            "version": VERSION,
            "dist": {"tarball": tarball_url},
        },
    )
    responses[tarball_url] = _response(tarball_url, body=basis.tarball)
    requests = []

    def open_once(request, timeout, max_bytes):
        assert request.get_method() == "GET"
        expected_auth = (
            None
            if request.full_url.startswith("https://objects.")
            else "Bearer " + TOKEN
        )
        assert request.get_header("Authorization") == expected_auth
        assert timeout > 0
        assert max_bytes > 0
        requests.append(request.full_url)
        return responses[request.full_url]

    result = read_github_packages_active_state(
        basis.artifact,
        basis.expectation,
        token=TOKEN,
        observed_at=OBSERVED_AT,
        transport=GitHubPackagesHttpTransport(open_once),
    )
    assert set(requests) == {CONTROL_URL, EXACT_URL, tarball_url, TAGS_URL}
    assert result.readback.classification == "exact-satisfied"


def test_selected_response_digest_is_sanitized_not_synthetic_headers(basis):
    responses = _responses(basis)
    responses[TARBALL_URL] = replace(
        responses[TARBALL_URL],
        url=TARBALL_URL + "?credential=" + TOKEN,
        redirects=(TARBALL_URL + "?redirect=" + TOKEN,),
        headers=(
            ("ETag", TOKEN),
            ("Authorization", "Bearer " + TOKEN),
            ("Set-Cookie", TOKEN),
        ),
    )
    result = _read(basis, responses)
    expected = canonical_sha256(
        {
            "stage": "tarball",
            "requested-url": TARBALL_URL,
            "final-url": TARBALL_URL + "?credential=******",
            "redirects": [TARBALL_URL + "?redirect=******"],
            "status": 200,
            "selected-headers": [["etag", "******"]],
            "truncated": False,
            "complete": True,
            "body-sha256": "sha256:"
            + hashlib.sha256(basis.tarball).hexdigest(),
            "detail": None,
        }
    )
    assert result.response_identity == expected
    assert dict(result.readback.response_digests)["tarball"] == expected
    assert TOKEN not in repr(result)


@pytest.mark.parametrize(
    "oversize_url", [None, CONTROL_URL, EXACT_URL, TAGS_URL]
)
def test_metadata_and_tarballs_obey_their_separate_byte_budgets(
    basis, oversize_url
):
    responses = _responses(basis)
    metadata_limit = len(basis.tarball) - 1
    tarball_limit = len(basis.tarball) + 512
    if oversize_url is not None:
        response = responses[oversize_url]
        responses[oversize_url] = replace(
            response,
            body=response.body
            + b" " * (metadata_limit + 1 - len(response.body)),
        )

    def open_once(request, _timeout, max_bytes):
        response = responses[request.full_url]
        oversized = len(response.body) > max_bytes
        return replace(
            response,
            body=response.body[:max_bytes],
            truncated=oversized,
            complete=not oversized,
        )

    result = read_github_packages_active_state(
        basis.artifact,
        basis.expectation,
        token=TOKEN,
        observed_at=OBSERVED_AT,
        transport=GitHubPackagesHttpTransport(open_once),
        metadata_limit_bytes=metadata_limit,
        tarball_limit_bytes=tarball_limit,
    )

    assert (result.package_control is None) == (oversize_url == CONTROL_URL)
    assert result.readback.classification == (
        "unknown" if oversize_url == EXACT_URL else "exact-satisfied"
    )
    assert result.readback.tag_state == (
        "unreadable" if oversize_url == TAGS_URL else "absent"
    )


@pytest.mark.parametrize("status", [200, 503])
def test_premature_http_bodies_preserve_independent_readback(
    basis, monkeypatch, status
):
    responses = _responses(basis)

    class InterruptedBody(io.BytesIO):
        def read(self, _amount):
            partial = b"partial"
            raise http.client.IncompleteRead(partial, 10)

    class Opener:
        def open(self, request, *, timeout):
            del timeout
            url = request.full_url
            if url == EXACT_URL:
                body = InterruptedBody()
                if status == 503:
                    raise urllib.error.HTTPError(
                        url, status, "unavailable", Message(), body
                    )
                return urllib.response.addinfourl(body, Message(), url, status)
            response = responses[url]
            return urllib.response.addinfourl(
                io.BytesIO(response.body), Message(), url, response.status
            )

    monkeypatch.setattr(
        urllib.request, "build_opener", lambda *_handlers: Opener()
    )
    result = read_github_packages_active_state(
        basis.artifact,
        basis.expectation,
        token=TOKEN,
        observed_at=OBSERVED_AT,
        transport=GitHubPackagesHttpTransport(),
    )

    assert result.readback.classification == "unknown"
    assert result.readback.tag_state == "absent"
    assert result.package_control is not None
    assert result.diagnostics.entries == ("exact-version: unknown",)


def test_malformed_redirect_is_policy_evidence_not_an_exception(basis):
    responses = _responses(basis)
    responses[EXACT_URL] = _response(
        EXACT_URL, status=302, headers=(("Location", "https://["),)
    )

    def open_once(request, _timeout, _max_bytes):
        return responses[request.full_url]

    result = read_github_packages_active_state(
        basis.artifact,
        basis.expectation,
        token=TOKEN,
        observed_at=OBSERVED_AT,
        transport=GitHubPackagesHttpTransport(open_once),
    )
    assert result.readback.classification == "unprovable"
    assert result.readback.tag_state == "absent"
    assert result.package_control is not None


def test_response_payload_cannot_echo_credentials_as_observed_facts(basis):
    responses = _responses(basis)
    responses[CONTROL_URL] = _response(
        CONTROL_URL, _control(owner={"login": TOKEN})
    )
    responses[TAGS_URL] = _tag_response({TAG: TOKEN})
    result = _read(basis, responses)
    assert result.package_control is None
    assert result.readback.tag_state == "unreadable"
    assert TOKEN not in repr(result)


def test_transport_type_mismatch_remains_programming_error(basis):
    responses = _responses(basis)
    responses[EXACT_URL] = object()
    with pytest.raises(TypeError, match="transport returned"):
        _read(basis, responses)


def test_invalid_desired_witness_binding_fails_before_reads(basis):
    with pytest.raises(ValueError, match="version binding mismatch"):
        read_github_packages_active_state(
            basis.artifact,
            replace(basis.expectation, npm_package_version="9.0.0"),
            token=TOKEN,
            observed_at=OBSERVED_AT,
            transport=ScenarioTransport({}),
        )
