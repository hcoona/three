"""Focused offline tests for the case-specific Platform-Orphan observer."""

# ruff: noqa: ARG001, ARG002, C901, D101, D102, D103, D107, EM101, EM102, FBT001, PLR0911, PLR0912, PLR2004, SLF001, TRY003

from __future__ import annotations

import base64
import gzip
import hashlib
import http.client
import io
import json
import tarfile
import urllib.error
import urllib.parse
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any, Self, cast
from uuid import UUID

import pytest
import three_workflow_delivery_v3.governance.platform_orphan as observer
from three_workflow_delivery_v3.adapters.github_packages import (
    GitHubPackagesHttpResponse,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.governance.platform_orphan import (
    InjectedPlatformOrphanGetTransport,
    PlatformOrphanObservationError,
    UrllibPlatformOrphanOneHopGet,
    observe_platform_orphan_32809578776,
)
from three_workflow_delivery_v3.governance.platform_orphan_coordinator import (
    reconcile_platform_orphan_32809578776,
)
from three_workflow_delivery_v3.records import (
    PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256,
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_RESULT_SCHEMA,
    admit_platform_orphan_reconciliation_result,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
ACTIVE_BYTES = (
    Path(__file__).resolve().parents[1]
    / "fixtures/governance/platform-orphan-active-authority.json"
).read_bytes()
CONTROL_COMMIT = "c" * 40
INVOCATION_ID = "12345678-1234-5678-9234-567812345678"
VALID_TARBALL_PATH = (
    "/@hcoona/hcoona-release-smoke-npm/-/"
    "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz"
)


def _blob_oid(content: bytes) -> str:
    return hashlib.sha1(
        f"blob {len(content)}\0".encode() + content,
        usedforsecurity=False,
    ).hexdigest()


def _probe_document() -> dict[str, Any]:
    scenario = {
        "scenario": "absent-create-readback",
        "package-coordinate": observer.PACKAGE_COORDINATE,
        "tag": observer.PACKAGE_TAG,
        "mutation-classification": "incomplete",
        "pre": {"state": "absent"},
        "action": {
            "operation": "npm-publish-create-only",
            "executed": True,
            "mutation-started": True,
        },
        "response": {
            "result": "runner-failed-after-mutation-start",
            "identity-digest": (
                "sha256:"
                "3a9c915660b8014a36ddeb753c286c36a3d8088b3fc4f7834681d7f4c6d05e3d"
            ),
            "diagnostics": ["runner-did-not-prove-controlled-outcome"],
        },
        "post": {
            "state": "exact",
            "content-sha512": observer.EXPECTED_TARBALL_SHA512,
        },
    }
    document = {
        "schema": "workflow-delivery/v3/fixed-acceptance-suite",
        "suite": "absent-create-readback",
        "scenario-inventory": ["absent-create-readback"],
        "scenarios": [scenario],
        "mutation-classification": "incomplete",
        "result": "incomplete",
    }
    document["record-digest"] = canonical_sha256(cast("JsonValue", document))
    return document


def _governance_document(
    probe: dict[str, Any],
    *,
    review_id: int,
    probe_id: int,
    probe_digest: str,
) -> dict[str, Any]:
    return {
        "schema": "workflow-delivery/v3/governance-acceptance-evidence",
        "purpose": "destination-acceptance",
        "workflow": {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": observer.WORKFLOW_PATH,
            "sha": observer.ACCEPTANCE_WORKFLOW,
        },
        "target-sha": observer.ACCEPTANCE_TARGET,
        "package-coordinate": observer.PACKAGE_COORDINATE,
        "confirmation-digest": (
            "sha256:"
            "1215f9d01cd343462c3f826ba67ebee86b6f6142b7fcfe5630572a5a808314f8"
        ),
        "environment": observer.ENVIRONMENT_NAME,
        "reviewer": {
            "login": None,
            "source": "unavailable-in-job-context",
        },
        "recovery": {
            "workflow-run-id": observer.ACCEPTANCE_RUN_ID,
            "environment": observer.ENVIRONMENT_NAME,
            "deployment": (
                f"run:{observer.ACCEPTANCE_RUN_ID}/environment:acceptance"
            ),
            "job": "acceptance-review",
            "artifact-id": review_id,
        },
        "dependency-results": [
            {"job": "validate-fixed-inputs", "result": "success"},
            {"job": "acceptance-review", "result": "success"},
            {"job": "probe-absent-create-readback", "result": "failure"},
            {"job": "probe-exact-and-conflict", "result": "skipped"},
        ],
        "probe-facts": [
            {
                "probe": "probe-absent-create-readback",
                "result": "incomplete",
                "scenario-inventory": probe["scenario-inventory"],
                "record-digest": probe["record-digest"],
                "artifact-id": probe_id,
                "artifact-digest": probe_digest,
                "scenarios": probe["scenarios"],
            },
            {
                "probe": "probe-exact-and-conflict",
                "result": "incomplete",
                "scenario-inventory": [
                    "exact",
                    "identical-race",
                    "differing-race",
                    "lost-response",
                ],
                "record-digest": None,
                "artifact-id": None,
                "artifact-digest": None,
                "scenarios": [],
            },
        ],
        "mutation-classification": "unknown",
        "producer": "capture-governance-evidence",
        "workflow-run-id": observer.ACCEPTANCE_RUN_ID,
        "run-attempt": 1,
        "release-lineage": "none",
    }


def _tarball() -> bytes:
    manifest = canonicalize(
        {
            "name": observer.ACCEPTANCE_PACKAGE_NAME,
            "version": observer.PACKAGE_VERSION,
            "repository": {
                "type": "git",
                "url": observer.ACCEPTANCE_REPOSITORY_URL,
            },
        }
    )
    witness = canonicalize(
        {
            "purpose": "destination-acceptance",
            "target-sha": observer.ACCEPTANCE_TARGET,
        }
    )
    entries = {
        "package/package.json": manifest,
        "package/index.js": b"export default 'ok';\n",
        observer.ACCEPTANCE_WITNESS_PATH: witness,
    }
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w:gz") as archive:
        for name, content in sorted(entries.items()):
            info = tarfile.TarInfo(name)
            info.size = len(content)
            info.mode = 0o644
            info.mtime = 0
            archive.addfile(info, io.BytesIO(content))
    payload = bytearray(gzip.decompress(output.getvalue()))
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        members = archive.getmembers()
    for member in members:
        start = member.offset
        header = bytearray(payload[start : start + tarfile.BLOCKSIZE])
        header[0:100] = member.name.encode() + bytes(100 - len(member.name))
        header[100:108] = b"000644 \0"
        header[108:124] = bytes(16)
        header[124:136] = f"{member.size:010o} \0".encode()
        header[136:148] = f"{cast('int', member.mtime):010o} \0".encode()
        header[148:156] = b" " * 8
        header[156:157] = tarfile.REGTYPE
        header[157:257] = bytes(100)
        header[257:263] = b"ustar\0"
        header[263:265] = b"00"
        header[265:329] = bytes(64)
        header[329:337] = b"000000 \0"
        header[337:345] = b"000000 \0"
        header[345:512] = bytes(167)
        checksum = sum(header)
        header[148:156] = f"{checksum:06o} \0".encode()
        payload[start : start + tarfile.BLOCKSIZE] = header
    data_end = max(
        (
            (member.offset_data + member.size + tarfile.BLOCKSIZE - 1)
            // tarfile.BLOCKSIZE
            * tarfile.BLOCKSIZE
            for member in members
        ),
        default=0,
    )
    closed = payload[:data_end] + bytes(tarfile.BLOCKSIZE * 2)
    return gzip.compress(bytes(closed), mtime=0)


@dataclass
class FakeTransport:
    tarball: bytes
    tag_value: object = observer.PACKAGE_VERSION
    omit_target_tag: bool = False
    manifest_status: int = 200
    calls: list[tuple[str, tuple[tuple[str, str], ...]]] = field(
        default_factory=list
    )
    run_reads: int = 0
    drift_final_run: bool = False
    override_status_path: str | None = None
    redirect_path: str | None = None
    malformed_path: str | None = None
    jobs_total: int = 0
    incomplete_jobs: bool = False
    forbidden_after_source: bool = False
    final_source_commit: str = CONTROL_COMMIT
    source_reads: int = 0
    manifest_integrity: str | None = None
    manifest_tarball_url: str | None = None
    response_incomplete_path: str | None = None

    def _response(
        self,
        url: str,
        status: int,
        document: object,
        *,
        redirects: tuple[str, ...] = (),
        final_url: str | None = None,
    ) -> GitHubPackagesHttpResponse:
        body = (
            document
            if type(document) is bytes
            else json.dumps(document, separators=(",", ":")).encode()
        )
        return GitHubPackagesHttpResponse(
            status=status,
            url=final_url or url,
            headers=(),
            body=body,
            redirects=redirects,
            truncated=self.response_incomplete_path
            == urllib.parse.urlsplit(url).path,
        )

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
        redirect_policy: str,
    ) -> GitHubPackagesHttpResponse:
        if self.forbidden_after_source:
            raise AssertionError("network request after final source read")
        self.calls.append((url, headers))
        parsed = urllib.parse.urlsplit(url)
        path = parsed.path
        if self.override_status_path == path:
            return self._response(url, 500, {})
        if self.redirect_path == path:
            changed = f"https://api.github.com{path}/unexpected"
            return self._response(
                url,
                200,
                {},
                redirects=(changed,),
                final_url=changed,
            )
        if self.malformed_path == path:
            return self._response(url, 200, b"{")
        if path == "/repos/hcoona/three/branches/main":
            self.source_reads += 1
            commit = (
                self.final_source_commit
                if self.source_reads == 2
                else CONTROL_COMMIT
            )
            return self._response(
                url,
                200,
                {
                    "name": "main",
                    "protected": True,
                    "commit": {"sha": commit},
                },
            )
        if path == "/repos/hcoona/three/git/ref/heads/main":
            commit = (
                self.final_source_commit
                if self.source_reads == 2
                else CONTROL_COMMIT
            )
            return self._response(
                url,
                200,
                {
                    "ref": "refs/heads/main",
                    "object": {"type": "commit", "sha": commit},
                },
            )
        if path.endswith(
            "/contents/.github/workflow-delivery/governance/"
            "platform-orphan-run-32809578776.json"
        ):
            commit = (
                self.final_source_commit
                if self.source_reads == 2
                else CONTROL_COMMIT
            )
            assert urllib.parse.parse_qs(parsed.query) == {"ref": [commit]}
            return self._response(
                url,
                200,
                {
                    "type": "file",
                    "path": PLATFORM_ORPHAN_AUTHORITY_PATH,
                    "sha": _blob_oid(ACTIVE_BYTES),
                    "encoding": "base64",
                    "content": base64.b64encode(ACTIVE_BYTES).decode(),
                },
            )
        if path.endswith(observer.WORKFLOW_PATH):
            return self._response(url, 404, {})
        if path == f"/repos/hcoona/three/actions/runs/{observer.RUN_ID}":
            self.run_reads += 1
            updated = (
                "2026-08-25T04:36:00Z"
                if self.drift_final_run and self.run_reads == 2
                else "2026-08-25T04:35:59Z"
            )
            return self._response(
                url,
                200,
                {
                    "id": observer.RUN_ID,
                    "node_id": "WFR_fixed",
                    "check_suite_id": 88878593045,
                    "workflow_id": observer.WORKFLOW_ID,
                    "run_attempt": 1,
                    "event": "workflow_dispatch",
                    "run_number": 2,
                    "status": "queued",
                    "conclusion": None,
                    "head_branch": observer.TRANSITION_REF.removeprefix(
                        "refs/heads/"
                    ),
                    "head_sha": observer.ACCEPTANCE_WORKFLOW,
                    "created_at": "2026-08-25T04:35:59Z",
                    "run_started_at": "2026-08-25T04:35:59Z",
                    "updated_at": updated,
                    "path": observer.WORKFLOW_PATH,
                    "repository": {"full_name": "hcoona/three"},
                },
            )
        if path.endswith(f"/actions/workflows/{observer.WORKFLOW_ID}"):
            return self._response(
                url,
                200,
                {
                    "id": observer.WORKFLOW_ID,
                    "path": observer.WORKFLOW_PATH,
                    "state": "disabled_manually",
                },
            )
        if path.endswith("/jobs"):
            page = int(urllib.parse.parse_qs(parsed.query)["page"][0])
            count = (
                min(100, max(0, self.jobs_total - ((page - 1) * 100)))
                if not self.incomplete_jobs
                else 0
            )
            return self._response(
                url,
                200,
                {
                    "total_count": self.jobs_total,
                    "jobs": [
                        {
                            "id": ((page - 1) * 100) + index + 1,
                            "run_id": observer.RUN_ID,
                            "run_attempt": 1,
                            "status": "queued",
                            "conclusion": None,
                        }
                        for index in range(count)
                    ],
                },
            )
        if path.endswith("/artifacts"):
            return self._response(
                url,
                200,
                {"total_count": 0, "artifacts": []},
            )
        if path.endswith("/pending_deployments"):
            return self._response(url, 200, [])
        if "/environments/" in path or path.endswith(
            "workflow-delivery-v3-acceptance-retry-2-transition"
        ):
            return self._response(url, 404, {})
        if path == "/users/hcoona/packages/npm/hcoona-release-smoke-npm":
            return self._response(
                url,
                200,
                {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "repository": {"full_name": "hcoona/three"},
                },
            )
        if path.endswith(f"/{observer.PACKAGE_VERSION}"):
            if self.manifest_status == 404:
                return self._response(url, 404, {})
            integrity = (
                self.manifest_integrity or observer.EXPECTED_TARBALL_SHA512
            )
            raw_digest = bytes.fromhex(integrity.removeprefix("sha512:"))
            return self._response(
                url,
                200,
                {
                    "name": observer.ACCEPTANCE_PACKAGE_NAME,
                    "version": observer.PACKAGE_VERSION,
                    "repository": {
                        "type": "git",
                        "url": observer.ACCEPTANCE_REPOSITORY_URL,
                    },
                    "dist": {
                        "integrity": (
                            "sha512-" + base64.b64encode(raw_digest).decode()
                        ),
                        "tarball": (
                            self.manifest_tarball_url
                            or observer.NPM_ORIGIN + VALID_TARBALL_PATH
                        ),
                    },
                },
            )
        if path.endswith("/dist-tags"):
            tags: dict[str, Any] = {"latest": {"unexpected": ["discard-me"]}}
            if not self.omit_target_tag:
                tags[observer.PACKAGE_TAG] = self.tag_value
            return self._response(
                url,
                200,
                tags,
            )
        admitted_tarball_path = urllib.parse.urlsplit(
            self.manifest_tarball_url
            or observer.NPM_ORIGIN + VALID_TARBALL_PATH
        ).path
        if path == admitted_tarball_path:
            return self._response(url, 200, self.tarball)
        raise AssertionError(f"unexpected fixed test path: {path}")


class Clock:
    def __init__(self, start: datetime | None = None) -> None:
        self.value = start or observer.ELIGIBLE_AFTER

    def __call__(self) -> datetime:
        current = self.value
        self.value += timedelta(seconds=1)
        return current


@pytest.fixture
def invocation(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> dict[str, Any]:
    tarball = _tarball()
    probe = _probe_document()
    review = (
        b"purpose=destination-acceptance\n"
        b"target-sha=b031e5e0bd98a95943a03a1529b64e856e1a8aa1\n"
        b"package-coordinate=@hcoona/hcoona-release-smoke-npm@"
        b"0.0.0-wdv3-acceptance.5\n"
        b"workflow=hcoona/three/.github/workflows/"
        b"workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml@"
        b"refs/heads/main\n"
        b"run=32805739095\n"
        b"run-attempt=1\n"
    )
    probe_bytes = canonicalize(probe)
    review_id, review_digest = observer._ARTIFACT_DIGESTS["review"]
    probe_id, probe_digest = observer._ARTIFACT_DIGESTS["probe"]
    governance = canonicalize(
        _governance_document(
            probe,
            review_id=review_id,
            probe_id=probe_id,
            probe_digest=probe_digest,
        )
    )
    assert "sha256:" + hashlib.sha256(review).hexdigest() == review_digest
    assert "sha256:" + hashlib.sha256(probe_bytes).hexdigest() == probe_digest
    assert (
        "sha256:" + hashlib.sha256(governance).hexdigest()
        == observer._ARTIFACT_DIGESTS["governance"][1]
    )

    packed_manifest = canonicalize(
        {
            "name": observer.ACCEPTANCE_PACKAGE_NAME,
            "version": observer.PACKAGE_VERSION,
            "repository": {
                "type": "git",
                "url": observer.ACCEPTANCE_REPOSITORY_URL,
            },
        }
    )
    witness = canonicalize(
        {
            "purpose": "destination-acceptance",
            "target-sha": observer.ACCEPTANCE_TARGET,
        }
    )
    monkeypatch.setattr(
        observer,
        "inspect_fixed_acceptance_tarball",
        lambda *_args, **_kwargs: {
            "content-sha512": observer.EXPECTED_TARBALL_SHA512
        },
    )
    monkeypatch.setattr(
        observer,
        "_read_tarball",
        lambda _content: {
            "package/package.json": packed_manifest,
            "package/index.js": b"export default 'ok';\n",
            observer.ACCEPTANCE_WITNESS_PATH: witness,
        },
    )
    paths = {}
    for name, content in (
        ("review", review),
        ("probe", probe_bytes),
        ("governance", governance),
    ):
        path = tmp_path / name
        path.write_bytes(content)
        paths[f"{name}_artifact"] = path
    transport = FakeTransport(tarball)
    return {
        "transport": transport,
        "clock": Clock(),
        "token": "secret-token",
        "local_control_commit": CONTROL_COMMIT,
        **paths,
    }


def _observe(invocation: dict[str, Any]) -> Any:
    observer_invocation = {
        key: value
        for key, value in invocation.items()
        if key != "local_control_commit"
    }
    return observe_platform_orphan_32809578776(**observer_invocation)


class SimpleBodyResponse:
    def __init__(
        self,
        body: bytes | http.client.IncompleteRead,
        *,
        content_length: str | None,
    ) -> None:
        self._body = body
        self.headers = (
            {} if content_length is None else {"Content-Length": content_length}
        )
        self.read_sizes: list[int] = []

    def read(self, size: int) -> bytes:
        self.read_sizes.append(size)
        if isinstance(self._body, http.client.IncompleteRead):
            raise self._body
        return self._body

    def close(self) -> None:
        return None


def test_concrete_one_hop_transport_is_get_only_bounded_and_no_redirect(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls: list[tuple[Any, float]] = []
    handlers: list[object] = []

    class Response:
        def __init__(self) -> None:
            self.status = 200
            self.headers = {"Content-Type": "application/json"}

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            return None

        def read(self, size: int) -> bytes:
            assert size == 4
            return b"four"

        def geturl(self) -> str:
            return observer.API_ORIGIN + "/fixed"

    class Opener:
        def open(self, request: Any, *, timeout: float) -> Response:
            calls.append((request, timeout))
            return Response()

    def build_opener(*values: object) -> Opener:
        handlers.extend(values)
        return Opener()

    monkeypatch.setattr(observer.urllib.request, "build_opener", build_opener)
    response = UrllibPlatformOrphanOneHopGet()(
        observer.API_ORIGIN + "/fixed",
        (("Authorization", "Bearer secret"),),
        2.0,
        3,
    )

    assert len(calls) == 1
    assert calls[0][0].get_method() == "GET"
    assert calls[0][1] == 2.0
    assert len(handlers) == 2
    assert isinstance(handlers[0], observer.urllib.request.ProxyHandler)
    assert cast("Any", handlers[0]).proxies == {}
    assert handlers[1] is UrllibPlatformOrphanOneHopGet._NoRedirect
    assert response.body == b"fou"
    assert response.truncated is True
    assert response.complete is False
    assert response.redirects == ()


def test_concrete_one_hop_transport_ignores_ambient_proxy_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    handlers: list[object] = []

    class Response:
        def __init__(self) -> None:
            self.status = 200
            self.headers = {"Content-Length": "2"}

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            return None

        def read(self, size: int) -> bytes:
            assert size == 9
            return b"ok"

        def geturl(self) -> str:
            return observer.API_ORIGIN + "/fixed"

    class Opener:
        def open(self, *_args: object, **_kwargs: object) -> Response:
            return Response()

    def build_opener(*values: object) -> Opener:
        handlers.extend(values)
        return Opener()

    monkeypatch.setenv("HTTPS_PROXY", "http://proxy.invalid:8080")
    monkeypatch.setenv("https_proxy", "http://proxy.invalid:8081")
    monkeypatch.setattr(observer.urllib.request, "build_opener", build_opener)

    response = UrllibPlatformOrphanOneHopGet()(
        observer.API_ORIGIN + "/fixed",
        (),
        1,
        8,
    )

    assert response.body == b"ok"
    assert response.complete is True
    assert len(handlers) == 2
    assert isinstance(handlers[0], observer.urllib.request.ProxyHandler)
    assert cast("Any", handlers[0]).proxies == {}
    assert handlers[1] is UrllibPlatformOrphanOneHopGet._NoRedirect


@pytest.mark.parametrize(
    "content_length",
    ["-1", "+1", " 1", "1 ", "01", "1.0", ""],
)
def test_bounded_body_reader_rejects_invalid_nonnegative_content_length_syntax(
    content_length: str,
) -> None:
    response = SimpleBodyResponse(b"x", content_length=content_length)

    with pytest.raises(
        PlatformOrphanObservationError,
        match="Content-Length",
    ):
        observer._read_bounded_http_body(response, max_bytes=8)

    assert response.read_sizes == []


@pytest.mark.parametrize(
    ("content_length", "body", "max_bytes", "expected"),
    [
        ("0", b"", 3, (b"", False, True)),
        ("3", b"abc", 3, (b"abc", False, True)),
        (None, b"abcd", 3, (b"abc", True, False)),
        ("4", b"abcd", 3, (b"abc", True, False)),
    ],
)
def test_bounded_body_reader_preserves_declared_length_and_truncation_semantics(
    content_length: str | None,
    body: bytes,
    max_bytes: int,
    expected: tuple[bytes, bool, bool],
) -> None:
    response = SimpleBodyResponse(body, content_length=content_length)

    assert (
        observer._read_bounded_http_body(response, max_bytes=max_bytes)
        == expected
    )
    assert response.read_sizes == [max_bytes + 1]


@pytest.mark.parametrize(
    "payload",
    [
        b"ab",
        http.client.IncompleteRead(b"ab", 3),
    ],
)
def test_bounded_body_reader_rejects_short_declared_body(
    payload: bytes | http.client.IncompleteRead,
) -> None:
    response = SimpleBodyResponse(
        payload,
        content_length="5",
    )

    with pytest.raises(
        PlatformOrphanObservationError,
        match="shorter than Content-Length",
    ):
        observer._read_bounded_http_body(response, max_bytes=4)

    assert response.read_sizes == [5]


def test_bounded_body_reader_truncates_incomplete_read_partial_bytes() -> None:
    response = SimpleBodyResponse(
        http.client.IncompleteRead(b"abcde", 2),
        content_length="7",
    )

    assert observer._read_bounded_http_body(
        response,
        max_bytes=3,
    ) == (b"abc", True, False)
    assert response.read_sizes == [4]


@pytest.mark.parametrize("error_path", [False, True])
@pytest.mark.parametrize("mode", ["short", "incomplete-read"])
def test_concrete_transport_rejects_short_and_incomplete_bodies_on_both_paths(
    monkeypatch: pytest.MonkeyPatch,
    error_path: bool,
    mode: str,
) -> None:
    payload: bytes | http.client.IncompleteRead
    payload = b"ab" if mode == "short" else http.client.IncompleteRead(b"ab", 3)
    body = SimpleBodyResponse(payload, content_length="5")

    class SuccessResponse(SimpleBodyResponse):
        status = 200

        def __enter__(self) -> Self:
            return self

        def __exit__(self, *_args: object) -> None:
            return None

        def geturl(self) -> str:
            return observer.API_ORIGIN + "/fixed"

    success = SuccessResponse(payload, content_length="5")
    http_error = urllib.error.HTTPError(
        observer.API_ORIGIN + "/fixed",
        404,
        "not found",
        cast("Any", body.headers),
        cast("Any", body),
    )

    class Opener:
        def open(self, *_args: object, **_kwargs: object) -> SuccessResponse:
            if error_path:
                raise http_error
            return success

    monkeypatch.setattr(
        observer.urllib.request,
        "build_opener",
        lambda *_handlers: Opener(),
    )

    with pytest.raises(
        PlatformOrphanObservationError,
        match="shorter than Content-Length",
    ):
        UrllibPlatformOrphanOneHopGet()(
            observer.API_ORIGIN + "/fixed",
            (),
            1,
            4,
        )

    assert (body if error_path else success).read_sizes == [5]


@pytest.mark.parametrize(
    ("classification", "manifest_status", "omit_tag", "tag_value"),
    [
        ("exact", 200, False, observer.PACKAGE_VERSION),
        ("absent", 404, True, observer.PACKAGE_VERSION),
        ("partial", 200, True, observer.PACKAGE_VERSION),
        ("conflicting", 200, False, "unexpected-sensitive-tag-target"),
    ],
)
def test_coordinator_emits_one_strict_canonical_candidate(
    invocation: dict[str, Any],
    classification: str,
    manifest_status: int,
    omit_tag: bool,
    tag_value: str,
) -> None:
    transport = cast("FakeTransport", invocation["transport"])
    transport.manifest_status = manifest_status
    transport.omit_target_tag = omit_tag
    transport.tag_value = tag_value
    writes: list[bytes] = []

    admitted = reconcile_platform_orphan_32809578776(
        **invocation,
        invocation_id_factory=lambda: UUID(INVOCATION_ID),
        output=writes.append,
    )

    assert len(writes) == 1
    assert canonicalize(admitted.to_document()) == writes[0]
    assert admit_platform_orphan_reconciliation_result(writes[0]) == admitted
    result = cast("dict[str, Any]", admitted.to_document()["result"])
    assert result["package_classification"] == classification
    expected = ["platform-orphan-admitted"]
    if classification != "exact":
        expected.append(f"package-{classification}")
    assert result["diagnostics"] == sorted(expected)
    producer = cast("dict[str, Any]", admitted.to_document()["producer"])
    authority = cast("dict[str, Any]", admitted.to_document()["authority"])
    assert producer["control_commit"] == CONTROL_COMMIT
    assert authority["initial_commit"] == CONTROL_COMMIT
    assert authority["final_commit"] == CONTROL_COMMIT
    assert "unexpected-sensitive-tag-target" not in writes[0].decode()
    assert "secret-token" not in writes[0].decode()


def test_coordinator_checks_local_control_before_later_requests(
    invocation: dict[str, Any],
) -> None:
    writes: list[bytes] = []
    invocation["local_control_commit"] = "d" * 40

    with pytest.raises(ValueError, match="does not match local control"):
        reconcile_platform_orphan_32809578776(
            **invocation,
            invocation_id_factory=lambda: UUID(INVOCATION_ID),
            output=writes.append,
        )

    requested_paths = [
        urllib.parse.urlsplit(url).path
        for url, _headers in invocation["transport"].calls
    ]
    assert requested_paths == [
        "/repos/hcoona/three/branches/main",
        "/repos/hcoona/three/git/ref/heads/main",
        (
            "/repos/hcoona/three/contents/.github/workflow-delivery/"
            "governance/platform-orphan-run-32809578776.json"
        ),
    ]
    assert writes == []


def test_coordinator_rejects_malformed_local_head_before_requests_or_output(
    invocation: dict[str, Any],
) -> None:
    writes: list[bytes] = []
    invocation["local_control_commit"] = "c" * 39

    with pytest.raises(ValueError, match="local control commit"):
        reconcile_platform_orphan_32809578776(
            **invocation,
            invocation_id_factory=lambda: UUID(INVOCATION_ID),
            output=writes.append,
        )

    assert invocation["transport"].calls == []
    assert writes == []


def test_coordinator_observer_failure_emits_no_candidate(
    invocation: dict[str, Any],
) -> None:
    cast("FakeTransport", invocation["transport"]).jobs_total = 1
    writes: list[bytes] = []

    with pytest.raises(PlatformOrphanObservationError):
        reconcile_platform_orphan_32809578776(
            **invocation,
            invocation_id_factory=lambda: UUID(INVOCATION_ID),
            output=writes.append,
        )

    assert writes == []


def test_coordinator_rejects_non_uuid_source_without_output(
    invocation: dict[str, Any],
) -> None:
    writes: list[bytes] = []

    with pytest.raises(ValueError, match="UUID source"):
        reconcile_platform_orphan_32809578776(
            **invocation,
            invocation_id_factory=lambda: cast("Any", INVOCATION_ID),
            output=writes.append,
        )

    assert writes == []


def _candidate(data: Any) -> dict[str, Any]:
    destination = data.destination_documents()[0]["state"]
    classification = destination["classification"]
    diagnostics = ["platform-orphan-admitted"]
    if classification != "exact":
        diagnostics.append(f"package-{classification}")
    document = {
        "schema": PLATFORM_ORPHAN_RESULT_SCHEMA,
        "version": 1,
        "producer": {
            "id": "three-workflow-delivery-v3/platform-orphan-reconcile",
            "entry_point": (
                "three-workflow-delivery-v3 governance "
                "reconcile-platform-orphan-32809578776"
            ),
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "control_commit": data.control_commit,
        },
        "invocation": {
            "id": INVOCATION_ID,
            "started_at": data.started_at,
            "completed_at": data.completed_at,
        },
        "authority": {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": PLATFORM_ORPHAN_AUTHORITY_PATH,
            "initial_commit": data.initial_source.commit,
            "initial_blob_oid": data.initial_source.blob_oid,
            "initial_content_sha256": data.initial_source.content_sha256,
            "final_commit": data.final_source.commit,
            "final_blob_oid": data.final_source.blob_oid,
            "final_content_sha256": data.final_source.content_sha256,
            "parent_main_commit": data.initial_source.commit,
        },
        "acceptance": data.acceptance.to_document(),
        "requests": data.request_documents(),
        "platform_observations": data.platform_documents(),
        "destination_observations": data.destination_documents(),
        "result": {
            "terminalization_blocker_exclusion": ("admitted:run:32809578776"),
            "reconciliation_authority": "not-granted-by-exception",
            "acceptance_result": "unsuccessful",
            "platform_cleanup": "incomplete-with-admitted-orphan",
            "run_terminal": False,
            "release_lineage": "none",
            "package_classification": classification,
            "package_mutation": "prohibited",
            "live_activation": "prohibited",
            "diagnostics": sorted(diagnostics),
        },
    }
    document["result_digest"] = canonical_sha256(cast("JsonValue", document))
    return document


def test_happy_path_produces_phase_1_admissible_query_only_data(
    invocation: dict[str, Any],
) -> None:
    data = _observe(invocation)

    admitted = admit_platform_orphan_reconciliation_result(
        canonicalize(_candidate(data))
    )
    retained = canonicalize(_candidate(data))
    assert admitted.to_document() == _candidate(data)
    assert (
        data.initial_source.content_sha256
        == PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256
    )
    assert b"secret-token" not in retained
    assert str(invocation["review_artifact"]).encode() not in retained
    assert all(
        ("Authorization", "Bearer secret-token") in headers
        for _url, headers in invocation["transport"].calls
    )
    assert len(data.platform_observations) == 2
    assert len(data.destination_observations) == 2


def test_cooling_off_rejects_before_source_or_transport(
    invocation: dict[str, Any],
) -> None:
    invocation["clock"] = Clock(observer.ELIGIBLE_AFTER - timedelta(seconds=1))

    with pytest.raises(PlatformOrphanObservationError, match="cooling-off"):
        _observe(invocation)

    assert invocation["transport"].calls == []


@pytest.mark.parametrize("failure", ["status", "path", "malformed"])
def test_endpoint_status_path_and_malformed_responses_fail_closed(
    invocation: dict[str, Any],
    failure: str,
) -> None:
    transport = invocation["transport"]
    run_path = f"/repos/hcoona/three/actions/runs/{observer.RUN_ID}"
    if failure == "status":
        transport.override_status_path = run_path
    elif failure == "path":
        transport.redirect_path = run_path
    else:
        transport.malformed_path = run_path

    with pytest.raises(PlatformOrphanObservationError):
        _observe(invocation)


@pytest.mark.parametrize(
    ("jobs_total", "incomplete", "minimum_job_requests"),
    [(101, False, 2), (101, True, 1)],
)
def test_jobs_pagination_is_exhaustive_or_rejected(
    invocation: dict[str, Any],
    jobs_total: int,
    incomplete: bool,
    minimum_job_requests: int,
) -> None:
    transport = invocation["transport"]
    transport.jobs_total = jobs_total
    transport.incomplete_jobs = incomplete

    with pytest.raises(PlatformOrphanObservationError):
        _observe(invocation)

    job_requests = [
        url
        for url, _headers in transport.calls
        if url.split("?")[0].endswith("/jobs")
    ]
    assert len(job_requests) >= minimum_job_requests


def test_source_and_platform_state_drift_fail_closed(
    invocation: dict[str, Any],
) -> None:
    invocation["transport"].final_source_commit = "d" * 40
    with pytest.raises(PlatformOrphanObservationError, match="source drifted"):
        _observe(invocation)

    invocation["transport"] = FakeTransport(
        invocation["transport"].tarball,
        drift_final_run=True,
    )
    with pytest.raises(PlatformOrphanObservationError, match="run identity"):
        _observe(invocation)


def test_target_tag_mismatch_discards_scalar_and_never_follows_it(
    invocation: dict[str, Any],
) -> None:
    unexpected = "0.0.0-wdv3-acceptance.6"
    invocation["transport"].tag_value = unexpected

    data = _observe(invocation)
    retained = canonicalize(_candidate(data))

    assert (
        data.destination_documents()[0]["state"]["tag_projection"] == "mismatch"
    )
    assert data.destination_documents()[0]["state"]["classification"] == (
        "conflicting"
    )
    assert unexpected.encode() not in retained
    assert all(
        unexpected not in url for url, _ in invocation["transport"].calls
    )


def test_manifest_404_with_target_tag_present_is_conflicting(
    invocation: dict[str, Any],
) -> None:
    invocation["transport"].manifest_status = 404

    data = _observe(invocation)
    state = data.destination_documents()[0]["state"]

    assert state["classification"] == "conflicting"
    assert state["manifest_version"] is None
    assert not any(
        urllib.parse.urlsplit(url).path == VALID_TARBALL_PATH
        for url, _ in invocation["transport"].calls
    )


def test_existing_manual_redirect_transport_strips_credentials() -> None:
    seen: list[tuple[str, tuple[tuple[str, str], ...]]] = []

    def opener(
        url: str,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> Any:
        seen.append((url, headers))
        if len(seen) == 1:
            return GitHubPackagesHttpResponse(
                302,
                url,
                (("Location", "https://objects.githubusercontent.com/file"),),
                b"",
            )
        return GitHubPackagesHttpResponse(200, url, (), b"ok")

    transport = InjectedPlatformOrphanGetTransport(opener)
    response = transport.get(
        observer.NPM_ORIGIN + VALID_TARBALL_PATH,
        headers=(
            ("Authorization", "Bearer secret"),
            ("Cookie", "secret"),
            ("Npm-Token", "secret"),
        ),
        timeout=1,
        max_bytes=100,
        redirect_policy="tarball",
    )

    assert response.status == 200
    assert {name.lower() for name, _value in seen[1][1]}.isdisjoint(
        {"authorization", "cookie", "npm-token"}
    )


@pytest.mark.parametrize(
    "mode",
    ["host", "limit"],
)
def test_redirect_host_hop_and_limit_are_denied(
    mode: str,
) -> None:
    calls = 0

    def opener(
        url: str,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> Any:
        nonlocal calls
        calls += 1
        location = (
            "https://evil.example/file"
            if mode == "host"
            else f"https://objects.githubusercontent.com/hop-{calls}"
        )
        return GitHubPackagesHttpResponse(
            302,
            url,
            (("Location", location),),
            b"",
        )

    transport = InjectedPlatformOrphanGetTransport(opener)
    with pytest.raises(PlatformOrphanObservationError):
        transport.get(
            observer.NPM_ORIGIN + VALID_TARBALL_PATH,
            headers=(("Authorization", "Bearer secret"),),
            timeout=1,
            max_bytes=100,
            redirect_policy="tarball",
        )
    assert calls == (1 if mode == "host" else 6)


def test_unreadable_target_tag_emits_no_data(
    invocation: dict[str, Any],
) -> None:
    invocation["transport"].tag_value = 5

    with pytest.raises(PlatformOrphanObservationError, match="unreadable"):
        _observe(invocation)


@pytest.mark.parametrize("value", [None, 5, [], {}])
def test_present_non_string_target_tag_is_unreadable(
    invocation: dict[str, Any],
    value: object,
) -> None:
    invocation["transport"].tag_value = value

    with pytest.raises(PlatformOrphanObservationError, match="unreadable"):
        _observe(invocation)


def test_absent_target_tag_key_is_missing(
    invocation: dict[str, Any],
) -> None:
    invocation["transport"].omit_target_tag = True

    data = _observe(invocation)
    state = data.destination_documents()[0]["state"]

    assert state["tag_projection"] == "missing"
    assert state["classification"] == "partial"


def test_differing_admitted_tarball_is_retained_as_conflicting(
    invocation: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    actual = "sha512:" + ("1" * 128)
    invocation["transport"].manifest_integrity = actual
    monkeypatch.setattr(
        observer,
        "inspect_fixed_acceptance_tarball",
        lambda *_args, **_kwargs: {"content-sha512": actual},
    )

    data = _observe(invocation)
    state = data.destination_documents()[0]["state"]

    assert state["classification"] == "conflicting"
    assert state["tarball_sha512"] == actual
    assert admit_platform_orphan_reconciliation_result(
        canonicalize(_candidate(data))
    ).to_document() == _candidate(data)


def test_tarball_bytes_must_match_advertised_manifest_integrity(
    invocation: dict[str, Any],
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        observer,
        "inspect_fixed_acceptance_tarball",
        lambda *_args, **_kwargs: {"content-sha512": "sha512:" + ("2" * 128)},
    )

    with pytest.raises(PlatformOrphanObservationError, match="integrity"):
        _observe(invocation)


@pytest.mark.parametrize(
    "url",
    [
        (
            "http://npm.pkg.github.com/@hcoona/"
            "hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz"
        ),
        (
            "https://evil.example/@hcoona/hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz"
        ),
        (
            "https://npm.pkg.github.com/@hcoona/other/-/"
            "other-0.0.0-wdv3-acceptance.5.tgz"
        ),
        (
            "https://npm.pkg.github.com/@hcoona/"
            "hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.6.tgz"
        ),
        (
            "https://npm.pkg.github.com/@hcoona/"
            "hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.6.tgz"
        ),
        (
            "https://npm.pkg.github.com/download/@hcoona/"
            "hcoona-release-smoke-npm/0.0.0-wdv3-acceptance.5/id/extra"
        ),
        (
            "https://npm.pkg.github.com/download/@hcoona/"
            "hcoona-release-smoke-npm/0.0.0-wdv3-acceptance.5/%69d"
        ),
        (
            "https://npm.pkg.github.com/@hcoona/"
            "hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5%2Etgz"
        ),
        (
            "https://npm.pkg.github.com/@hcoona/"
            "hcoona-release-smoke-npm/-/"
            "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz?token=x"
        ),
    ],
)
def test_manifest_tarball_url_is_narrowly_fixed_and_safe(
    invocation: dict[str, Any],
    url: str,
) -> None:
    invocation["transport"].manifest_tarball_url = url

    with pytest.raises(PlatformOrphanObservationError, match="tarball"):
        _observe(invocation)


def test_manifest_derived_safe_tarball_path_is_requested_and_ledgered(
    invocation: dict[str, Any],
) -> None:
    path = (
        "/download/@hcoona/hcoona-release-smoke-npm/"
        "0.0.0-wdv3-acceptance.5/7f0a2c91-acde-4b55"
    )
    invocation["transport"].manifest_tarball_url = observer.NPM_ORIGIN + path

    data = _observe(invocation)

    physical = [
        urllib.parse.urlsplit(url).path
        for url, _headers in invocation["transport"].calls
        if urllib.parse.urlsplit(url).path == path
    ]
    retained = [
        request.path for request in data.requests if request.path == path
    ]
    assert physical == retained == [path, path]


@pytest.mark.parametrize(
    "path",
    [
        "/repos/hcoona/three/branches/main",
        "/repos/hcoona/three/git/ref/heads/main",
        (
            "/repos/hcoona/three/contents/.github/workflow-delivery/"
            "governance/platform-orphan-run-32809578776.json"
        ),
    ],
)
def test_each_source_endpoint_rejects_redirect_without_hidden_request(
    invocation: dict[str, Any],
    path: str,
) -> None:
    invocation["transport"].redirect_path = path

    with pytest.raises(PlatformOrphanObservationError):
        _observe(invocation)

    calls = [
        url
        for url, _headers in invocation["transport"].calls
        if urllib.parse.urlsplit(url).path == path
    ]
    assert len(calls) == 1


def test_six_source_requests_are_exactly_six_source_ledger_entries(
    invocation: dict[str, Any],
) -> None:
    data = _observe(invocation)
    source_paths = {
        "/repos/hcoona/three/branches/main",
        "/repos/hcoona/three/git/ref/heads/main",
        (
            "/repos/hcoona/three/contents/.github/workflow-delivery/"
            "governance/platform-orphan-run-32809578776.json"
        ),
    }
    physical = [
        urllib.parse.urlsplit(url).path
        for url, _headers in invocation["transport"].calls
        if urllib.parse.urlsplit(url).path in source_paths
    ]
    retained = [
        request.path
        for request in data.requests
        if request.path in source_paths
    ]

    assert len(physical) == len(retained) == 6
    assert physical == retained


@pytest.mark.parametrize("incomplete_hop", [1, 2])
def test_incomplete_tarball_hop_stops_before_following_redirect(
    incomplete_hop: int,
) -> None:
    calls = 0

    def opener(
        url: str,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> Any:
        nonlocal calls
        calls += 1
        location = (
            "https://objects.githubusercontent.com/file"
            if calls == 1
            else "https://github-registry-files.githubusercontent.com/file"
        )
        return GitHubPackagesHttpResponse(
            302,
            url,
            (("Location", location),),
            b"",
            truncated=calls == incomplete_hop,
        )

    with pytest.raises(PlatformOrphanObservationError, match="incomplete"):
        InjectedPlatformOrphanGetTransport(opener).get(
            observer.NPM_ORIGIN + VALID_TARBALL_PATH,
            headers=(("Authorization", "Bearer token"),),
            timeout=1,
            max_bytes=100,
            redirect_policy="tarball",
        )
    assert calls == incomplete_hop


def test_observer_rejects_out_of_order_clock_samples(
    invocation: dict[str, Any],
) -> None:
    samples = iter(
        [
            observer.ELIGIBLE_AFTER,
            observer.ELIGIBLE_AFTER + timedelta(seconds=2),
            observer.ELIGIBLE_AFTER + timedelta(seconds=1),
            observer.ELIGIBLE_AFTER + timedelta(seconds=3),
            observer.ELIGIBLE_AFTER + timedelta(seconds=4),
            observer.ELIGIBLE_AFTER + timedelta(seconds=5),
        ]
    )
    invocation["clock"] = lambda: next(samples)

    with pytest.raises(PlatformOrphanObservationError, match="clock samples"):
        _observe(invocation)


def test_no_network_request_occurs_after_final_source_read(
    invocation: dict[str, Any],
) -> None:
    data = _observe(invocation)

    assert data.completed_at >= data.started_at
    final_source_path = (
        "/repos/hcoona/three/contents/.github/workflow-delivery/governance/"
        "platform-orphan-run-32809578776.json"
    )
    assert (
        urllib.parse.urlsplit(invocation["transport"].calls[-1][0]).path
        == final_source_path
    )
