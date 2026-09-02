"""Regressions for the commit-8 publish-path Governance recheck."""

from __future__ import annotations

# ruff: noqa: D102, D103
import inspect
from datetime import UTC, datetime
from types import SimpleNamespace
from typing import TYPE_CHECKING, Any, cast

import pytest
from three_workflow_delivery_v3.adapters import github_packages
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release import (
    PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER,
    BuddyExecutionIdentity,
    ReleaseAttemptIdentity,
)
from three_workflow_delivery_v3.release.eligibility import GovernanceBlob
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

if TYPE_CHECKING:
    from pathlib import Path

TARGET = "a" * 40
BASELINE_COMMIT = "b" * 40
BASELINE_BLOB = "c" * 40
OBSERVED_AT = datetime(2026, 8, 13, 22, 15, tzinfo=UTC)
TOKEN = "publish-governance-regression-token"  # noqa: S105


class _StopAfterRunner(BaseException):
    """Stop the publish path immediately after proving runner invocation."""


class RecordingRunner:
    """Runner fake that aborts immediately after recording one call."""

    def __init__(self, events: list[object]) -> None:
        """Retain the shared event stream."""
        self.events = events
        self.calls = 0

    def run(self, argv: tuple[str, ...], *, env: dict[str, str]) -> object:
        self.calls += 1
        self.events.append(("runner.run", argv, dict(env)))
        raise _StopAfterRunner


class RecordingGovernanceClient:
    """Fixed-source client fake with an auditable read sequence."""

    def __init__(
        self,
        events: list[object],
        *,
        content: bytes,
        resolved_commit: str = BASELINE_COMMIT,
        blob_oid: str = BASELINE_BLOB,
        protected: bool = True,
    ) -> None:
        """Configure one exact protected-source observation."""
        self.events = events
        self.content = content
        self.resolved_commit = resolved_commit
        self.blob_oid = blob_oid
        self.protected = protected

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        self.events.append(("protected", repository, ref))
        return self.protected

    def resolve_ref(self, repository: str, ref: str) -> str:
        self.events.append(("resolve", repository, ref))
        return self.resolved_commit

    def read_blob(
        self,
        repository: str,
        commit: str,
        path: str,
    ) -> GovernanceBlob:
        self.events.append(("read", repository, commit, path))
        return GovernanceBlob(blob_oid=self.blob_oid, content=self.content)


def _governance_bytes(
    *,
    live_enabled: bool = True,
    expires_at: str = "2026-10-01T00:00:00Z",
    extra_limitation: str | None = None,
    release_policy: str = "hcoona-release-smoke-npm",
) -> bytes:
    limitations = [
        "GitHub Packages does not expose a complete grants API.",
        "Protected-source disablement has bounded review and merge latency.",
    ]
    if extra_limitation is not None:
        limitations.append(extra_limitation)
    return canonicalize(
        cast(
            "JsonValue",
            {
                "schema": "workflow-delivery/v3/governance-attestation",
                "release_policy": release_policy,
                "package": "@hcoona/hcoona-release-smoke-npm",
                "issuer": "hcoona",
                "inspected_at": "2026-08-01T00:00:00Z",
                "expires_at": expires_at,
                "accepted_writers": [{"login": "hcoona", "role": "Admin"}],
                "access_inventory": {
                    "repository": [{"subject": "hcoona", "access": "admin"}],
                    "package": [{"subject": "hcoona", "access": "write"}],
                    "manage_actions": [
                        {"subject": "hcoona", "access": "allowed"}
                    ],
                },
                "limitations": limitations,
                "live_enabled": live_enabled,
            },
        )
    )


def _attempt() -> ReleaseAttemptIdentity:
    return ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        workflow_run_id=812,
    )


def _capability_decision(content: bytes) -> SimpleNamespace:
    content_sha256 = canonical_sha256(parse_json_strict(content))
    return SimpleNamespace(
        governance_provenance=(
            ("blob-oid", BASELINE_BLOB),
            ("content-sha256", content_sha256),
            ("path", GOVERNANCE_PATH),
            ("ref", GOVERNANCE_REF),
            ("repository", GOVERNANCE_REPOSITORY),
            ("resolved-commit", BASELINE_COMMIT),
        ),
        governance_content_sha256=content_sha256,
        governance_expires_at="2026-10-01T00:00:00Z",
        governance_live_enabled=True,
        control="control:" + ("6" * 64),
    )


def _successful_preflight(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> tuple[
    github_packages.GitHubPackagesPublishPreflight,
    SimpleNamespace,
    Path,
]:
    baseline_content = _governance_bytes()
    capability = _capability_decision(baseline_content)
    events: list[object] = []
    client = RecordingGovernanceClient(events, content=baseline_content)
    attempt = _attempt()
    publication = SimpleNamespace(
        attempt=attempt,
        snapshot_digest="sha256:" + ("1" * 64),
    )
    artifact = object()
    action = SimpleNamespace(
        artifact=artifact,
        projection=SimpleNamespace(
            coordinate=SimpleNamespace(channel="buddy"),
        ),
        action_digest="sha256:" + ("2" * 64),
        lock_group="github-packages:hcoona",
    )
    tarball = tmp_path / "qualified.tgz"
    tarball.write_bytes(b"qualified")
    monkeypatch.setattr(
        github_packages,
        "_validate_publish_preconditions",
        lambda **_kwargs: None,
    )
    monkeypatch.setattr(
        github_packages,
        "_validate_local_tarball_preconditions",
        lambda **_kwargs: None,
    )
    monkeypatch.setattr(
        github_packages,
        "_npm_configuration_digest",
        lambda **_kwargs: "sha256:" + ("5" * 64),
    )

    preflight = github_packages.preflight_github_packages_action(
        tarball=tarball,
        target=TARGET,
        publication_snapshot=cast("Any", publication),
        authorization=cast("Any", object()),
        capability_decision=cast("Any", capability),
        action=cast("Any", action),
        qualification_snapshot=cast("Any", object()),
        qualification_decision=cast("Any", object()),
        artifact=cast("Any", artifact),
        expectation=cast("Any", object()),
        governance_source=GovernanceSource(
            repository=GOVERNANCE_REPOSITORY,
            ref=GOVERNANCE_REF,
            path=GOVERNANCE_PATH,
            max_age_days=GOVERNANCE_MAX_AGE_DAYS,
        ),
        governance_client=client,
        governance_observed_at=OBSERVED_AT,
    )

    assert events == [
        ("protected", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ("resolve", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ("read", GOVERNANCE_REPOSITORY, BASELINE_COMMIT, GOVERNANCE_PATH),
    ]
    return preflight, capability, tarball


def _publish_arguments(  # noqa: PLR0913
    tmp_path: Path,
    *,
    client: RecordingGovernanceClient,
    runner: RecordingRunner,
    preflight: github_packages.GitHubPackagesPublishPreflight,
    capability: SimpleNamespace,
    tarball: Path,
) -> dict[str, object]:
    return {
        "tarball": tarball,
        "target": TARGET,
        "token": TOKEN,
        "runner": runner,
        "temp_root": tmp_path / "npm-temp",
        "transport": object(),
        "publication_snapshot": object(),
        "authorization": object(),
        "capability_decision": capability,
        "action": object(),
        "qualification_snapshot": object(),
        "qualification_decision": object(),
        "artifact": object(),
        "expectation": object(),
        "preflight": preflight,
        "mutation_marker": (
            github_packages.form_mutation_may_have_started_marker(
                preflight=preflight
            )
        ),
        "governance_source": GovernanceSource(
            repository=GOVERNANCE_REPOSITORY,
            ref=GOVERNANCE_REF,
            path=GOVERNANCE_PATH,
            max_age_days=GOVERNANCE_MAX_AGE_DAYS,
        ),
        "governance_client": client,
        "governance_observed_at": OBSERVED_AT,
        "defer_receipt_binding": True,
    }


def test_publish_api_requires_fresh_governance_reader_seam() -> None:
    parameters = inspect.signature(
        github_packages.publish_github_packages_action
    ).parameters

    assert {"governance_client", "governance_observed_at"} <= set(parameters)


@pytest.mark.parametrize(
    "governance_case",
    [
        pytest.param(
            (
                _governance_bytes(live_enabled=False),
                BASELINE_COMMIT,
                BASELINE_BLOB,
                True,
            ),
            id="disabled",
        ),
        pytest.param(
            (
                _governance_bytes(expires_at="2026-08-13T00:00:00Z"),
                BASELINE_COMMIT,
                BASELINE_BLOB,
                True,
            ),
            id="expired",
        ),
        pytest.param(
            (_governance_bytes(), "d" * 40, BASELINE_BLOB, True),
            id="resolved-commit-changed",
        ),
        pytest.param(
            (_governance_bytes(), BASELINE_COMMIT, "e" * 40, True),
            id="blob-oid-changed",
        ),
        pytest.param(
            (
                _governance_bytes(
                    extra_limitation="Fourth-round substitution."
                ),
                BASELINE_COMMIT,
                BASELINE_BLOB,
                True,
            ),
            id="content-changed",
        ),
        pytest.param(
            (_governance_bytes(), BASELINE_COMMIT, BASELINE_BLOB, False),
            id="authoritative-unprotected",
        ),
        pytest.param(
            (b"{}", BASELINE_COMMIT, BASELINE_BLOB, True),
            id="invalid-schema",
        ),
        pytest.param(
            (
                _governance_bytes(release_policy="other-policy"),
                BASELINE_COMMIT,
                BASELINE_BLOB,
                True,
            ),
            id="invalid-semantics",
        ),
    ],
)
def test_publish_second_governance_read_returns_terminal_no_side_effect(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    governance_case: tuple[bytes, str, str, bool],
) -> None:
    content, resolved_commit, blob_oid, protected = governance_case
    preflight, capability, tarball = _successful_preflight(
        monkeypatch,
        tmp_path,
    )
    events: list[object] = []
    runner = RecordingRunner(events)
    client = RecordingGovernanceClient(
        events,
        content=content,
        resolved_commit=resolved_commit,
        blob_oid=blob_oid,
        protected=protected,
    )

    def admit_marker(**_kwargs: object) -> None:
        events.append("marker-admitted")

    monkeypatch.setattr(
        github_packages,
        "_admit_mutation_marker",
        admit_marker,
    )

    publish = cast("Any", github_packages.publish_github_packages_action)
    with pytest.raises(
        github_packages.PublisherGovernanceRecheckRejectionError
    ) as raised:
        publish(
            **_publish_arguments(
                tmp_path,
                client=client,
                runner=runner,
                preflight=preflight,
                capability=capability,
                tarball=tarball,
            )
        )

    result = raised.value.result
    assert isinstance(
        result,
        github_packages.DeferredPublicationExecutionResult,
    )
    assert result.classification.outcome == "failed"
    assert result.classification.mutation_disposition == "no-side-effect"
    assert result.classification.receipt_digest is None
    assert (
        result.diagnostic_reference
        == PUBLISHER_GOVERNANCE_RECHECK_FAILED_BEFORE_RUNNER
    )
    assert result.response_identity_digest is None
    assert result.receipt is None
    assert result.observation is None
    assert runner.calls == 0
    assert events == (
        [
            "marker-admitted",
            ("protected", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ]
        if not protected
        else [
            "marker-admitted",
            ("protected", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
            ("resolve", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
            ("read", GOVERNANCE_REPOSITORY, resolved_commit, GOVERNANCE_PATH),
        ]
    )


def test_publish_unchanged_second_governance_read_runs_exactly_once(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    preflight, capability, tarball = _successful_preflight(
        monkeypatch,
        tmp_path,
    )
    events: list[object] = []
    runner = RecordingRunner(events)
    client = RecordingGovernanceClient(events, content=_governance_bytes())

    def admit_marker(**_kwargs: object) -> None:
        events.append("marker-admitted")

    monkeypatch.setattr(
        github_packages,
        "_admit_mutation_marker",
        admit_marker,
    )

    publish = cast("Any", github_packages.publish_github_packages_action)
    with pytest.raises(_StopAfterRunner):
        publish(
            **_publish_arguments(
                tmp_path,
                client=client,
                runner=runner,
                preflight=preflight,
                capability=capability,
                tarball=tarball,
            )
        )

    assert runner.calls == 1
    assert events[:4] == [
        "marker-admitted",
        ("protected", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ("resolve", GOVERNANCE_REPOSITORY, GOVERNANCE_REF),
        ("read", GOVERNANCE_REPOSITORY, BASELINE_COMMIT, GOVERNANCE_PATH),
    ]
    runner_event = events[4]
    assert isinstance(runner_event, tuple)
    assert runner_event[0] == "runner.run"
