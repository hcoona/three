"""Synthetic native reads and real local fixture inspection; no network IO."""

from __future__ import annotations

import hashlib
import json
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from urllib.parse import quote

import pytest
from three_workflow_delivery_v3.acceptance import npm_capture as capture
from three_workflow_delivery_v3.acceptance.native_npm import (
    VersionIdentity,
    require_deleted_duplicate_delta,
    require_restoration_delta,
)
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    build_npm_fixture,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    DEFAULT_MAX_PAGES,
    DEFAULT_METADATA_LIMIT_BYTES,
    DEFAULT_TARBALL_LIMIT_BYTES,
    DEFAULT_TIMEOUT_SECONDS,
    GitHubPackagesHttpResponse,
)
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

# Explicit scenario IDs and counts are independent expected observations.
# ruff: noqa: PLR2004

ROOT = Path(__file__).resolve().parents[6]
PACKAGE = "@hcoona/synthetic-complete-capture"
ROUTE = "/users/hcoona/packages/npm/synthetic-complete-capture"
ACTIVE = ROUTE + "/versions?state=active&per_page=100"
DELETED = ROUTE + "/versions?state=deleted&per_page=100"
METADATA = "https://npm.pkg.github.com/" + quote(PACKAGE, safe="")
SPEC = NpmFixtureSpec(PACKAGE, "1.0.0", "a" * 40, "synthetic")
OTHER = replace(SPEC, version="2.0.0", target="b" * 40)
ABSENT = replace(SPEC, version="3.0.0")
APPROVED = DisposablePackagePreconditions(
    PACKAGE,
    preexisting_container=True,
    operator_controlled=True,
    production_dependency=False,
)
NOW = datetime(2026, 9, 7, tzinfo=UTC)
TAG = "buddy-sha-" + SPEC.target


def _bytes(value):
    return json.dumps(value, indent=2).encode() + b"\n"


def _url(version):
    return f"https://npm.pkg.github.com/download/{PACKAGE}/{version}/fixture"


def _identity(version, version_id):
    return {
        "id": version_id,
        "name": version,
        "metadata": {"package_type": "npm"},
    }


@pytest.fixture(scope="module")
def fixtures():
    """Use installed official parsers and real tarball/witness inspection."""
    return {
        "original": build_npm_fixture(SPEC, repository_root=ROOT),
        "other": build_npm_fixture(OTHER, repository_root=ROOT),
        "different": build_npm_fixture(
            replace(SPEC, target="c" * 40, variant="different"),
            repository_root=ROOT,
        ),
        "foreign": build_npm_fixture(
            replace(SPEC, package="@hcoona/other-synthetic"),
            repository_root=ROOT,
        ),
    }


class SyntheticReads:
    """Serve raw observations independently of the requested fixture specs."""

    def __init__(self, fixtures):
        """Seed two actual active versions on separate official gh pages."""
        self.fixtures = fixtures
        self.package = {
            "id": 750,
            "name": "synthetic-complete-capture",
            "package_type": "npm",
            "visibility": "public",
            "owner": None,
            "repository": {
                "full_name": "hcoona/three",
                "permissions": {"admin": True, "push": True},
            },
            "version_count": 847,
        }
        self.tags = {"latest": OTHER.version, TAG: SPEC.version, "old": "0.1.0"}
        self.gh_calls = []
        self.http_calls = []
        self.events = []
        self.gh_failure = None
        self.set_versions(
            [[_identity(SPEC.version, 71)], [_identity(OTHER.version, 72)]]
        )

    def set_versions(self, active, deleted=None):
        """Change observed inventories without changing desired selectors."""
        self.gh_bodies = {
            ROUTE: _bytes(self.package),
            ACTIVE: _bytes(active),
            DELETED: _bytes([[]] if deleted is None else deleted),
        }
        self.packument = {
            "name": PACKAGE,
            "versions": {
                item["name"]: {
                    "name": PACKAGE,
                    "version": item["name"],
                    "dist": {"tarball": _url(item["name"])},
                }
                for page in active
                for item in page
            },
            "dist-tags": self.tags.copy(),
        }
        self.responses = {
            METADATA: GitHubPackagesHttpResponse(
                200, METADATA, (), _bytes(self.packument)
            ),
            _url(SPEC.version): GitHubPackagesHttpResponse(
                200, _url(SPEC.version), (), self.fixtures["original"].tarball
            ),
            _url(OTHER.version): GitHubPackagesHttpResponse(
                200, _url(OTHER.version), (), self.fixtures["other"].tarball
            ),
        }

    def run(self, argv, *, max_bytes):
        """Record only synthetic gh reads; no child process is launched."""
        self.gh_calls.append((argv, max_bytes))
        self.events.append(argv[-1])
        if self.gh_failure is not None:
            raise self.gh_failure
        return self.gh_bodies[argv[-1]]

    def get(self, url, *, headers, timeout, max_bytes):
        """Record bounded GET requests and return supplied bytes."""
        self.http_calls.append((url, headers, timeout, max_bytes))
        self.events.append(url)
        return self.responses[url]

    def clock(self):
        """The final call must follow all REST, registry and fixture reads."""
        self.events.append("clock")
        return NOW

    def take(self, directory, *, scenarios=(SPEC,), context=None, clock=None):
        """Call the public collector API with explicitly synthetic seams."""
        return capture.capture_npm_state(
            approved_disposable_package_preconditions=APPROVED,
            scenarios=scenarios,
            token="synthetic-local-read-token",  # noqa: S106
            repository_root=ROOT,
            audit_directory=directory,
            gh_runner=self,
            transport=self,
            original_deletion=context,
            clock=self.clock if clock is None else clock,
        )


@pytest.fixture
def reads(fixtures):
    """Create isolated in-memory read sources for each scenario."""
    return SyntheticReads(fixtures)


def test_complete_paginated_capture_preserves_raw_bytes_and_observed_control(
    reads, fixtures, tmp_path
):
    """Full inventories, dangling tags and actual content remain auditable."""
    audit = tmp_path / "capture"
    result = reads.take(audit, scenarios=(OTHER, ABSENT, SPEC))

    assert result.state.active_versions == (SPEC.version, OTHER.version)
    assert result.state.contents == (
        fixtures["original"].content,
        fixtures["other"].content,
    )
    assert result.state.tags == tuple(sorted(reads.tags.items()))
    assert result.state.tombstone is None
    assert result.state.control.to_document() == {
        "container_id": 750,
        "full_scoped_name": PACKAGE,
        "owner": "hcoona",
        "visibility": "public",
        "repository_full_name": "hcoona/three",
        "exposed_access": [],
    }
    assert result.state.to_document()["active_version_count"] == 2
    assert (audit / "github-package.json").read_bytes() == reads.gh_bodies[
        ROUTE
    ]
    assert (
        json.loads((audit / "github-package.json").read_bytes())[
            "version_count"
        ]
        == 847
    )
    assert (audit / "github-active-pages.json").read_bytes() == reads.gh_bodies[
        ACTIVE
    ]
    assert (audit / "npm-packument.json").read_bytes() == reads.responses[
        METADATA
    ].body
    assert (audit / "scenario-0.tgz").read_bytes() == fixtures[
        "original"
    ].tarball
    assert (audit / "scenario-1.tgz").read_bytes() == fixtures["other"].tarball
    assert (audit / "state.json").read_bytes() == canonicalize(
        result.state.to_document()
    )
    descriptor = parse_canonical_json((audit / "capture.json").read_bytes())
    assert descriptor["captured_at"] == NOW.isoformat()
    assert descriptor["state_digest"] == result.state.digest()
    assert descriptor["original_deletion"] is None
    raw_responses = descriptor["raw_responses"]
    assert isinstance(raw_responses, list)
    sources = []
    for entry in raw_responses:
        assert isinstance(entry, dict)
        sources.append(entry["source"])
    assert sources == [
        ROUTE,
        ACTIVE,
        METADATA,
        _url(SPEC.version),
        _url(OTHER.version),
    ]
    assert {item.filename for item in result.files} == {
        path.name for path in audit.iterdir()
    }
    for item in result.files:
        assert item.sha256 == (
            "sha256:"
            + hashlib.sha256((audit / item.filename).read_bytes()).hexdigest()
        )
        assert (
            b"synthetic-local-read-token"
            not in (audit / item.filename).read_bytes()
        )
    assert len(reads.gh_calls) == 2
    for argv, limit in reads.gh_calls:
        assert argv[:6] == (
            "gh",
            "api",
            "--hostname",
            "github.com",
            "--method",
            "GET",
        )
        assert "X-GitHub-Api-Version: 2026-03-10" in argv
        assert "synthetic-local-read-token" not in argv
        assert ("--paginate" in argv) == (argv[-1] == ACTIVE)
        assert ("--slurp" in argv) == (argv[-1] == ACTIVE)
        assert limit == DEFAULT_METADATA_LIMIT_BYTES * (
            DEFAULT_MAX_PAGES if argv[-1] == ACTIVE else 1
        )
    assert reads.events == [
        ROUTE,
        ACTIVE,
        METADATA,
        _url(SPEC.version),
        _url(OTHER.version),
        "clock",
    ]
    assert [call[3] for call in reads.http_calls] == [
        DEFAULT_METADATA_LIMIT_BYTES,
        DEFAULT_TARBALL_LIMIT_BYTES,
        DEFAULT_TARBALL_LIMIT_BYTES,
    ]
    assert all(call[2] == DEFAULT_TIMEOUT_SECONDS for call in reads.http_calls)
    assert dict(reads.http_calls[0][1])["Accept"] == "application/json"


def test_changed_actual_variant_and_target_are_not_replaced_by_expectations(
    reads, fixtures, tmp_path
):
    """A consistent but different remote fixture must reach the comparator."""
    reads.responses[_url(SPEC.version)] = replace(
        reads.responses[_url(SPEC.version)], body=fixtures["different"].tarball
    )
    result = reads.take(tmp_path / "changed")
    assert result.state.contents == (fixtures["different"].content,)
    assert result.state.contents[0].target == "c" * 40
    assert (
        result.state.contents[0].sha256 != fixtures["original"].content.sha256
    )
    assert parse_canonical_json(result.state.contents[0].witness)[
        "variant"
    ] == ("different")


def test_inactive_scenario_has_no_synthetic_content_or_deleted_read(
    reads, tmp_path
):
    """Authoritative active absence is not claimed from fixture expectations."""
    reads.set_versions([[]])
    result = reads.take(tmp_path / "absent")
    assert result.state.active_versions == ()
    assert result.state.contents == ()
    assert dict(result.state.tags)[TAG] == SPEC.version
    assert result.state.tombstone is None
    assert reads.events == [ROUTE, ACTIVE, METADATA, "clock"]


@pytest.mark.parametrize(
    "case",
    [
        "missing-page",
        "missing-list",
        "missing-version",
        "duplicate-name",
        "duplicate-id",
    ],
)
def test_partial_or_conflicting_inventory_stops_capture(reads, tmp_path, case):
    """No incomplete REST projection can establish absence."""
    pages = json.loads(reads.gh_bodies[ACTIVE])
    if case == "missing-page":
        pages[1] = None
    elif case == "missing-list":
        pages = {"versions": pages[0]}
    elif case == "missing-version":
        pages.pop()
    elif case == "duplicate-name":
        pages[1][0]["name"] = SPEC.version
    else:
        pages[1][0]["id"] = 71
    reads.gh_bodies[ACTIVE] = _bytes(pages)
    audit = tmp_path / case
    with pytest.raises((ValueError, TypeError)):
        reads.take(audit)
    assert not (audit / "state.json").exists()
    assert not (audit / "capture.json").exists()
    assert "clock" not in reads.events


@pytest.mark.parametrize(
    "changes",
    [
        {"complete": False},
        {"truncated": True},
        {"status": 206},
        {"headers": (("Content-Encoding", "gzip"),)},
        {"headers": (("Content-Length", "999999"),)},
        {
            "headers": (
                ("Content-Encoding", "identity"),
                ("Content-Encoding", "gzip"),
            )
        },
        {"url": METADATA + "/1.0.0"},
        {"redirects": ("https://example.invalid/metadata",)},
    ],
)
def test_partial_or_off_policy_registry_response_stops_capture(
    reads, tmp_path, changes
):
    """Successful HTTP status alone cannot prove metadata completeness."""
    reads.responses[METADATA] = replace(reads.responses[METADATA], **changes)
    audit = tmp_path / "partial"
    with pytest.raises(ValueError, match="registry response"):
        reads.take(audit)
    assert (audit / "npm-packument.json").read_bytes() == reads.responses[
        METADATA
    ].body
    assert not (audit / "capture.json").exists()
    assert len(reads.http_calls) == 1


@pytest.mark.parametrize(
    "case", ["package", "version", "tags", "duplicate-key"]
)
def test_conflicting_packument_never_reaches_tarball(reads, tmp_path, case):
    """Require packument and every manifest to bind exact identities."""
    if case == "package":
        reads.packument["name"] = "@hcoona/another-package"
    elif case == "version":
        reads.packument["versions"][OTHER.version]["version"] = "8.0.0"
    elif case == "tags":
        del reads.packument["dist-tags"]
    body = _bytes(reads.packument)
    if case == "duplicate-key":
        body = b'{"name":"one","name":"two"}'
    reads.responses[METADATA] = replace(reads.responses[METADATA], body=body)
    with pytest.raises((ValueError, KeyError)):
        reads.take(tmp_path / case)
    assert len(reads.http_calls) == 1


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("owner", {"login": "other-owner"}),
        ("visibility", "private"),
        ("name", "other-package"),
        ("package_type", "container"),
        ("repository", {"full_name": "hcoona/other"}),
        ("id", True),
    ],
)
def test_nonmatching_public_package_control_is_not_inferred(
    reads, tmp_path, field, value
):
    """Preconditions never overwrite an observed control mismatch."""
    reads.package[field] = value
    reads.gh_bodies[ROUTE] = _bytes(reads.package)
    with pytest.raises((ValueError, TypeError)):
        reads.take(tmp_path / "control")
    assert reads.events == [ROUTE]


@pytest.mark.parametrize("fixture", ["foreign", "other"])
def test_actual_tarball_manifest_and_witness_must_bind_selector(
    reads, fixtures, tmp_path, fixture
):
    """A foreign package or version cannot pass through a correct packument."""
    reads.responses[_url(SPEC.version)] = replace(
        reads.responses[_url(SPEC.version)], body=fixtures[fixture].tarball
    )
    with pytest.raises(ValueError, match="actual fixture identity"):
        reads.take(tmp_path / fixture)


def test_foreign_tarball_request_strips_credentials_and_signed_audit_url(
    reads, fixtures, tmp_path
):
    """Use existing transport policy; do not store redirected signed URLs."""
    url = "https://objects.githubusercontent.com/native.tgz?signature=secret"
    reads.packument["versions"][SPEC.version]["dist"]["tarball"] = url
    reads.responses[METADATA] = replace(
        reads.responses[METADATA], body=_bytes(reads.packument)
    )
    reads.responses[url] = GitHubPackagesHttpResponse(
        200,
        url + "&redirect-signature=other",
        (),
        fixtures["original"].tarball,
        redirects=(url + "&redirect-signature=other",),
    )
    audit = tmp_path / "foreign"
    result = reads.take(audit)
    assert result.state.contents == (fixtures["original"].content,)
    assert all(
        name.lower() != "authorization" for name, _ in reads.http_calls[-1][1]
    )
    descriptor = (audit / "capture.json").read_bytes()
    assert b"signature=" not in descriptor
    assert b"https://objects.githubusercontent.com/native.tgz" in descriptor


def test_active_deleted_refreshed_and_restored_capture_keeps_original_anchor(
    reads, fixtures, tmp_path
):
    """Refresh inspection time and restore the same ID and actual content."""
    before = reads.take(tmp_path / "active")
    context = capture.OriginalDeletionContext(
        before.state.control,
        VersionIdentity(71, SPEC.version),
        NOW - timedelta(minutes=1),
    )
    historical = _identity("0.5.0", 50)
    reads.set_versions(
        [[_identity(OTHER.version, 72)]],
        [[historical], [_identity(SPEC.version, 71)]],
    )
    deleted = reads.take(tmp_path / "deleted", context=context)
    refreshed = reads.take(
        tmp_path / "refreshed",
        context=context,
        clock=lambda: NOW + timedelta(minutes=2),
    )
    require_deleted_duplicate_delta(deleted.state, refreshed.state)
    tombstone = deleted.state.tombstone
    assert tombstone is not None
    assert tombstone.restorability is not None
    assert tombstone.target == context.original_version
    assert tombstone.deleted_versions == (
        VersionIdentity(50, "0.5.0"),
        VersionIdentity(71, SPEC.version),
    )
    assert tombstone.to_document()["deleted_version_count"] == 2
    assert tombstone.restorability.deletion_observed_at == (
        context.deletion_lower_bound_at
    )
    assert tombstone.restorability.inspected_at == NOW
    assert refreshed.state.tombstone.restorability.inspected_at == (
        NOW + timedelta(minutes=2)
    )
    assert deleted.state.to_document() == refreshed.state.to_document()
    assert deleted.state.active_versions == (OTHER.version,)
    assert deleted.state.contents == ()
    assert (
        tmp_path / "deleted" / "github-deleted-pages.json"
    ).read_bytes() == (reads.gh_bodies[DELETED])
    descriptor = json.loads(
        (tmp_path / "refreshed" / "capture.json").read_bytes()
    )
    assert descriptor["original_deletion"] == context.to_document()
    reads.set_versions(
        [[_identity(SPEC.version, 71)], [_identity(OTHER.version, 72)]],
        [[historical]],
    )
    restored = reads.take(tmp_path / "restored", context=context)
    require_restoration_delta(
        deleted.state, restored.state, fixtures["original"].content, TAG
    )
    assert restored.state.tombstone.target == context.original_version
    assert restored.state.tombstone.restorability is None
    assert restored.state.contents == before.state.contents


@pytest.mark.parametrize(
    "case",
    [
        "neither",
        "both",
        "changed-id",
        "changed-name",
        "cross-state-id",
        "restored-wrong-id",
        "missing-deleted",
        "new-container",
    ],
)
def test_unknown_or_conflicting_tombstone_fails_closed(reads, tmp_path, case):
    """Never substitute unknown restorability with restored-active None."""
    before = reads.take(tmp_path / "before")
    context = capture.OriginalDeletionContext(
        before.state.control,
        VersionIdentity(71, SPEC.version),
        NOW - timedelta(minutes=1),
    )
    active = [[_identity(OTHER.version, 72)]]
    deleted = [[_identity(SPEC.version, 71)]]
    if case == "neither":
        deleted = [[]]
    elif case == "both":
        active[0].append(_identity(SPEC.version, 71))
    elif case == "changed-id":
        deleted[0][0]["id"] = 999
    elif case == "changed-name":
        deleted[0][0]["name"] = "4.0.0"
    elif case == "cross-state-id":
        deleted[0][0]["id"] = 72
    elif case == "restored-wrong-id":
        active[0].append(_identity(SPEC.version, 999))
        deleted = [[]]
    elif case == "new-container":
        reads.package["id"] = 999
    reads.set_versions(active, deleted)
    if case == "missing-deleted":
        reads.gh_bodies[DELETED] = b"[]"
    audit = tmp_path / case
    with pytest.raises(ValueError, match=r"unprovable|overlap|missing|control"):
        reads.take(audit, context=context)
    assert not (audit / "capture.json").exists()


@pytest.mark.parametrize("age", [timedelta(days=30), -timedelta(seconds=1)])
def test_original_deletion_time_cannot_be_refreshed_or_future_dated(
    reads, tmp_path, age
):
    """The inference window uses the original lower bound, not discovery."""
    before = reads.take(tmp_path / "before")
    context = capture.OriginalDeletionContext(
        before.state.control, VersionIdentity(71, SPEC.version), NOW - age
    )
    reads.set_versions([[]], [[_identity(SPEC.version, 71)]])
    with pytest.raises(ValueError, match=r"restore window|precedes"):
        reads.take(tmp_path / "expired", context=context)


def test_audit_is_fresh_and_unknown_read_errors_propagate(reads, tmp_path):
    """Retain partial audit without overwriting or swallowing read failure."""
    audit = tmp_path / "existing"
    audit.mkdir()
    marker = audit / "marker"
    marker.write_bytes(b"untouched")
    with pytest.raises(FileExistsError):
        reads.take(audit)
    assert marker.read_bytes() == b"untouched"
    assert reads.events == []
    error = OSError("synthetic transport failure")
    reads.gh_failure = error
    with pytest.raises(OSError, match="synthetic transport failure") as caught:
        reads.take(tmp_path / "failed")
    assert caught.value is error
    assert not (tmp_path / "failed" / "capture.json").exists()


@pytest.mark.parametrize(
    "outcome",
    [
        NpmProcessOutcome(
            "definitive-non-success", b"private diagnostic", returncode=1
        ),
        NpmProcessOutcome("ambiguous", b"partial", returncode=-9),
        NpmProcessOutcome(
            "definitive-success", b"[]", truncated=True, returncode=0
        ),
        NpmProcessOutcome("not-initiated"),
    ],
)
def test_gh_process_failure_timeout_and_truncation_stop_without_retry(
    reads, monkeypatch, tmp_path, outcome
):
    """Exercise the real wrapper with a synthetic bounded-process outcome."""
    calls = []

    def run(_self, argv, **kwargs):
        calls.append((argv, kwargs))
        return outcome

    monkeypatch.setattr(capture.IsolatedNpmProcessRunner, "run", run)
    with pytest.raises(ValueError, match="gh capture failed"):
        capture.capture_npm_state(
            approved_disposable_package_preconditions=APPROVED,
            scenarios=(SPEC,),
            token="synthetic-read-only",  # noqa: S106
            repository_root=ROOT,
            audit_directory=tmp_path / "failed",
            transport=reads,
            clock=reads.clock,
        )
    assert len(calls) == 1
    assert calls[0][1]["timeout"] == DEFAULT_TIMEOUT_SECONDS
    assert calls[0][1]["output_limit"] == DEFAULT_METADATA_LIMIT_BYTES
    assert "synthetic-read-only" not in calls[0][1]["environment"].values()
    assert list((tmp_path / "failed").iterdir()) == []
    assert reads.http_calls == []


def test_gh_success_keeps_raw_json_and_uses_only_existing_auth(
    monkeypatch,
):
    """Local registry credentials are never installed as gh credentials."""
    raw = b'[ [ {"id": 71, "name": "1.0.0"} ] ]\n'
    calls = []

    def run(_self, argv, **kwargs):
        calls.append((argv, kwargs))
        return NpmProcessOutcome("definitive-success", raw, returncode=0)

    monkeypatch.setenv("GH_TOKEN", "synthetic-existing-auth")
    monkeypatch.setenv("GH_DEBUG", "api")
    monkeypatch.setattr(capture.IsolatedNpmProcessRunner, "run", run)
    runner = capture.GhCliCommandRunner(ROOT)
    assert runner.run(("gh", "api", ACTIVE), max_bytes=1000) == raw
    assert (
        calls[0][1]["environment"]["GH_TOKEN"] == "synthetic-existing-auth"  # noqa: S105
    )
    assert calls[0][1]["environment"]["GH_PROMPT_DISABLED"] == "1"
    assert "GH_DEBUG" not in calls[0][1]["environment"]


def test_off_policy_tarball_is_rejected_before_transport(reads, tmp_path):
    """A manifest cannot send the read credential to an arbitrary origin."""
    reads.packument["versions"][SPEC.version]["dist"]["tarball"] = (
        "https://example.invalid/native.tgz"
    )
    reads.responses[METADATA] = replace(
        reads.responses[METADATA], body=_bytes(reads.packument)
    )
    with pytest.raises(ValueError, match="off-policy requested URL"):
        reads.take(tmp_path / "untrusted")
    assert [call[0] for call in reads.http_calls] == [METADATA]


def test_registry_io_error_preserves_only_partial_audit(
    reads, monkeypatch, tmp_path
):
    """Unknown GET errors propagate; complete REST reads are not a snapshot."""
    error = OSError("synthetic registry IO failure")

    def get(*_args, **_kwargs):
        raise error

    monkeypatch.setattr(reads, "get", get)
    audit = tmp_path / "failed-registry"
    with pytest.raises(
        OSError, match="synthetic registry IO failure"
    ) as caught:
        reads.take(audit)
    assert caught.value is error
    assert {item.name for item in audit.iterdir()} == {
        "github-package.json",
        "github-active-pages.json",
    }
    assert "clock" not in reads.events
