"""Error-taxonomy regressions for commit-8 Governance observation."""

from __future__ import annotations

# ruff: noqa: D102, D103, D107
import hashlib
from dataclasses import replace
from datetime import UTC, datetime
from typing import cast

import pytest
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.platform.github import GitHubRestError
from three_workflow_delivery_v3.release import eligibility
from three_workflow_delivery_v3.release.eligibility import (
    GovernanceFreshnessRejectionError,
    GovernanceRejectionError,
    require_fresh_governance_identity,
)
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitRead,
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

COMMIT = "a" * 40
BLOB = "b" * 40
NOW = datetime(2026, 8, 13, 23, 0, tzinfo=UTC)


def _source() -> GovernanceSource:
    return GovernanceSource(
        repository=GOVERNANCE_REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        max_age_days=GOVERNANCE_MAX_AGE_DAYS,
    )


def _ready_activation() -> dict[str, JsonValue]:
    return {
        "state": "ready",
        "approval_environment": {
            "name": "workflow-delivery-v3-buddy-approval",
            "environment_id": 20895030723,
            "required_reviewers": [{"login": "hcoona", "id": 712433}],
            "prevent_self_review": False,
            "can_admins_bypass": False,
            "wait_timer_minutes": 0,
            "deployment_policy": "all",
            "secret_count": 0,
            "variables": [
                {
                    "name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
                    "value": "workflow-delivery-v3-buddy-approval/v1",
                    "scope": "environment",
                }
            ],
            "same_name_repository_variable_absent": True,
            "same_name_organization_variable": "not-applicable-user-owner",
            "evidence": [
                {
                    "endpoint": (
                        "GET /repos/hcoona/three/environments/"
                        "workflow-delivery-v3-buddy-approval"
                    ),
                    "captured_at": "2026-07-01T00:00:00Z",
                    "response_digest": "sha256:" + ("1" * 64),
                }
            ],
        },
        "artifact_retention": {
            "endpoint": (
                "GET /repos/hcoona/three/actions/permissions/"
                "artifact-and-log-retention"
            ),
            "captured_at": "2026-07-01T00:00:00Z",
            "days": 45,
            "response_digest": "sha256:" + ("2" * 64),
        },
        "destination_primitive": {
            "primitive_id": "github-packages-conditional-create-v1",
            "operation": ("conditional-create-npm-version-and-target-tag"),
            "captured_at": "2026-07-01T00:00:00Z",
            "race_inputs": [
                ["coordinate", "@hcoona/hcoona-release-smoke-npm"],
                ["target-tag", "buddy-sha-" + ("a" * 40)],
                ["version", "1.2.3"],
            ],
            "race_results": [
                ["conflicting-tag-preserved", "pass"],
                ["target-version-remained-absent", "pass"],
            ],
            "evidence_digest": "sha256:" + ("3" * 64),
        },
    }


def _blocked_activation() -> dict[str, JsonValue]:
    return {
        "state": "blocked",
        "blockers": [
            "destination-primitive-unproven",
            "fresh-native-evidence-required",
            "repository-retention-readback-required",
        ],
    }


def _document(**updates: JsonValue) -> dict[str, JsonValue]:
    document: dict[str, JsonValue] = {
        "schema": (
            "workflow-delivery/v3/normal-live-governance-attestation-v1"
        ),
        "release_policy": "hcoona-release-smoke-npm",
        "package": "@hcoona/hcoona-release-smoke-npm",
        "issuer": "hcoona",
        "inspected_at": "2026-08-01T00:00:00Z",
        "expires_at": "2026-10-01T00:00:00Z",
        "accepted_writers": [{"login": "hcoona", "role": "Admin"}],
        "accepted_publisher": "hcoona",
        "access_inventory": {
            "repository": [{"subject": "hcoona", "access": "admin"}],
            "package": [{"subject": "hcoona", "access": "write"}],
            "manage_actions": [{"subject": "hcoona", "access": "allowed"}],
        },
        "package_principal": {
            "repository": "hcoona/three",
            "intended_coordinate": "@hcoona/hcoona-release-smoke-npm",
            "known_wider_reach": [
                "@hcoona/hexo-renderer-asciidoc",
                "disposable-smoke-packages",
            ],
        },
        "limitations": [
            "GitHub Packages does not expose a complete grants API.",
            (
                "Protected-source disablement has bounded review and merge "
                "latency."
            ),
        ],
        "activation": _ready_activation(),
        "live_enabled": True,
    }
    document.update(updates)
    if updates.get("live_enabled") is False and "activation" not in updates:
        document["activation"] = _blocked_activation()
    return document


def _content(**updates: JsonValue) -> bytes:
    return canonicalize(cast("JsonValue", _document(**updates)))


class ObservationClient:
    """Injectable source client with separately mutable remote identities."""

    def __init__(self, content: bytes) -> None:
        self.content = content
        self.protected: object = True
        self.main_sha: object = COMMIT
        self.object_format: object = "sha1"
        self.blob_oid: object = BLOB
        self.read_value: object | None = None
        self.failure: tuple[str, Exception] | None = None
        self.calls: list[str] = []
        self.eligibility_main_shas: list[str | None] = []

    def _raise_at(self, stage: str) -> None:
        if self.failure is not None and self.failure[0] == stage:
            raise self.failure[1]

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        del repository, ref
        self.calls.append("protected")
        self._raise_at("protected")
        return cast("bool", self.protected)

    def read_source(
        self,
        repository: str,
        ref: str,
        path: str,
        *,
        eligibility_main_sha: str | None = None,
    ) -> GovernanceGitRead:
        del repository, ref, path
        self.calls.append("read")
        self.eligibility_main_shas.append(eligibility_main_sha)
        self._raise_at("read")
        if self.read_value is not None:
            return cast("GovernanceGitRead", self.read_value)
        return GovernanceGitRead(
            main_sha=cast("str", self.main_sha),
            object_format=cast("str", self.object_format),
            blob_oid=cast("str", self.blob_oid),
            content=self.content,
        )


def _capture_observation_error(
    client: ObservationClient,
    *,
    source: GovernanceSource | None = None,
    now: datetime = NOW,
) -> pytest.ExceptionInfo[Exception]:
    with pytest.raises(Exception) as raised:  # noqa: PT011
        eligibility.observe_governance_source(
            source or _source(),
            client,
            now=now,
        )
    return raised


def _assert_definitive_rejection(error: BaseException) -> None:
    assert type(error) is GovernanceRejectionError
    assert isinstance(error, ValueError)


def _assert_not_definitive_rejection(error: BaseException) -> None:
    assert not isinstance(error, GovernanceRejectionError)


def test_governance_freshness_rejection_derives_from_definitive_base() -> None:
    assert issubclass(
        GovernanceFreshnessRejectionError,
        GovernanceRejectionError,
    )


def _provenance(
    *,
    eligibility_main_sha: str = COMMIT,
    object_format: str = "sha1",
    blob: str = BLOB,
    content: bytes,
) -> tuple[tuple[str, str], ...]:
    return tuple(
        sorted(
            (
                ("repository", GOVERNANCE_REPOSITORY),
                ("ref", GOVERNANCE_REF),
                ("path", GOVERNANCE_PATH),
                ("eligibility-main-sha", eligibility_main_sha),
                ("git-object-format", object_format),
                ("blob-oid", blob),
                (
                    "canonical-content-digest",
                    f"sha256:{hashlib.sha256(content).hexdigest()}",
                ),
            )
        )
    )


def test_unprotected_ref_is_definitive_governance_rejection() -> None:
    client = ObservationClient(_content())
    client.protected = False

    raised = _capture_observation_error(client)

    _assert_definitive_rejection(raised.value)
    assert str(raised.value) == "Governance ref is not protected"
    assert client.calls == ["protected"]


@pytest.mark.parametrize(
    "content",
    [
        pytest.param(b'{"schema":', id="malformed-json"),
        pytest.param(
            canonicalize(cast("JsonValue", {"schema": "wrong"})),
            id="invalid-schema",
        ),
        pytest.param(_content() + b"\n", id="noncanonical-json"),
    ],
)
def test_fetched_invalid_canonical_or_schema_content_is_definitive_rejection(
    content: bytes,
) -> None:
    client = ObservationClient(content)

    raised = _capture_observation_error(client)

    _assert_definitive_rejection(raised.value)
    assert client.calls == ["protected", "read"]


@pytest.mark.parametrize(
    "updates",
    [
        pytest.param(
            {"release_policy": "other-policy"},
            id="policy-package-binding",
        ),
        pytest.param(
            {"expires_at": "2026-11-01T00:00:01Z"},
            id="lifetime",
        ),
        pytest.param(
            {
                "access_inventory": {
                    "repository": [{"subject": "hcoona", "access": "admin"}],
                    "package": [],
                    "manage_actions": [
                        {"subject": "hcoona", "access": "allowed"}
                    ],
                }
            },
            id="inventory",
        ),
        pytest.param({"accepted_writers": []}, id="attestation-semantics"),
        pytest.param({"live_enabled": "true"}, id="nonboolean-control"),
    ],
)
def test_fetched_invalid_governance_semantics_are_definitive_rejection(
    updates: dict[str, JsonValue],
) -> None:
    client = ObservationClient(_content(**updates))

    raised = _capture_observation_error(client)

    _assert_definitive_rejection(raised.value)
    assert client.calls == ["protected", "read"]


def test_fetched_content_digest_inconsistency_is_definitive_rejection(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    baseline = _content()
    changed = _content(issuer="other-human")
    attestation = eligibility.parse_governance_attestation(baseline)
    monkeypatch.setattr(
        eligibility,
        "parse_governance_attestation",
        lambda _content: attestation,
    )
    client = ObservationClient(changed)

    raised = _capture_observation_error(client)

    _assert_definitive_rejection(raised.value)
    assert "content digest mismatch" in str(raised.value)
    assert client.calls == ["protected", "read"]


@pytest.mark.parametrize(
    "case",
    ["disabled", "expired", "changed"],
)
def test_disabled_expired_and_changed_governance_remain_freshness_rejections(
    case: str,
) -> None:
    if case == "disabled":
        content = _content(live_enabled=False)
        expected_provenance = _provenance(content=content)
        expected_enabled = False
        expected_expiry = "2026-10-01T00:00:00Z"
    elif case == "expired":
        content = _content(expires_at="2026-08-13T22:59:59Z")
        expected_provenance = _provenance(content=content)
        expected_enabled = True
        expected_expiry = "2026-08-13T22:59:59Z"
    else:
        content = _content()
        expected_provenance = _provenance(content=content)
        expected_enabled = True
        expected_expiry = "2026-10-01T00:00:00Z"
    client = ObservationClient(content)
    if case == "changed":
        client.blob_oid = "c" * 40

    with pytest.raises(GovernanceFreshnessRejectionError) as raised:
        require_fresh_governance_identity(
            _source(),
            client,
            now=NOW,
            expected_provenance=expected_provenance,
            expected_canonical_content_digest=(
                f"sha256:{hashlib.sha256(content).hexdigest()}"
            ),
            expected_expires_at=expected_expiry,
            expected_live_enabled=expected_enabled,
        )

    assert type(raised.value) is GovernanceFreshnessRejectionError
    assert str(raised.value) == "Governance freshness comparison failed"
    assert client.calls == ["protected", "read"]
    assert client.eligibility_main_shas == [COMMIT]


def test_unrelated_main_advance_preserves_fresh_governance_identity() -> None:
    content = _content()
    client = ObservationClient(content)
    client.main_sha = "c" * 40

    observation = require_fresh_governance_identity(
        _source(),
        client,
        now=NOW,
        expected_provenance=_provenance(content=content),
        expected_canonical_content_digest=(
            f"sha256:{hashlib.sha256(content).hexdigest()}"
        ),
        expected_expires_at="2026-10-01T00:00:00Z",
        expected_live_enabled=True,
    )

    assert observation.eligibility_main_sha == COMMIT
    assert observation.current_main_sha == "c" * 40
    assert observation.blob_oid == BLOB
    assert client.eligibility_main_shas == [COMMIT]


@pytest.mark.parametrize("case", ["source", "time"])
def test_local_source_and_time_configuration_errors_are_not_governance_rejections(  # noqa: E501
    case: str,
) -> None:
    client = ObservationClient(_content())
    source = (
        replace(_source(), repository="other/repository")
        if case == "source"
        else _source()
    )
    now = (
        datetime(2026, 8, 13, 23, 0)  # noqa: DTZ001
        if case == "time"
        else NOW
    )

    raised = _capture_observation_error(client, source=source, now=now)

    _assert_not_definitive_rejection(raised.value)
    assert type(raised.value) is ValueError
    assert client.calls == []


@pytest.mark.parametrize(
    ("field", "value", "expected_type"),
    [
        pytest.param(
            "protected",
            1,
            ValueError,
            id="nonboolean-protection",
        ),
        pytest.param("main_sha", 17, ValueError, id="nonstring-main-sha"),
        pytest.param("blob_oid", 17, ValueError, id="nonstring-blob"),
        pytest.param(
            "object_format",
            "sha512",
            ValueError,
            id="unsupported-object-format",
        ),
        pytest.param(
            "read_value",
            {"sha": BLOB, "content": _content()},
            TypeError,
            id="non-git-read-api-identity",
        ),
    ],
)
def test_malformed_remote_identities_are_not_governance_rejections(
    field: str,
    value: object,
    expected_type: type[Exception],
) -> None:
    client = ObservationClient(_content())
    setattr(client, field, value)

    raised = _capture_observation_error(client)

    _assert_not_definitive_rejection(raised.value)
    assert type(raised.value) is expected_type
    assert client.calls


@pytest.mark.parametrize(
    ("stage", "failure"),
    [
        pytest.param(
            "protected",
            GitHubRestError("permission denied"),
            id="permission",
        ),
        pytest.param(
            "protected",
            GitHubRestError("HTTP 503"),
            id="http-5xx",
        ),
        pytest.param("read", OSError("network lost"), id="unexpected-network"),
        pytest.param(
            "read",
            GitHubRestError("malformed base64"),
            id="unexpected-rest-error",
        ),
        pytest.param(
            "read",
            ValueError("malformed protocol result"),
            id="unexpected-value-error",
        ),
    ],
)
def test_transport_failures_are_not_governance_rejections(
    stage: str,
    failure: Exception,
) -> None:
    client = ObservationClient(_content())
    client.failure = (stage, failure)

    raised = _capture_observation_error(client)

    _assert_not_definitive_rejection(raised.value)
    assert raised.value is failure
    assert client.calls[-1] == stage


@pytest.mark.parametrize(
    "failure",
    [
        "Governance Git fetch failed",
        "Governance Git history is shallow",
        "Governance protected path changed after eligibility",
    ],
)
def test_governance_git_failures_are_definitive_rejections(
    failure: str,
) -> None:
    client = ObservationClient(_content())
    client.failure = ("read", GovernanceGitReadError(failure))

    raised = _capture_observation_error(client)

    _assert_definitive_rejection(raised.value)
    assert str(raised.value) == failure
    assert isinstance(raised.value.__cause__, GovernanceGitReadError)
    assert client.calls == ["protected", "read"]
