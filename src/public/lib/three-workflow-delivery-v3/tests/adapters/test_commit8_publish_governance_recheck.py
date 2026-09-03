"""Regressions for fail-closed unsupported GitHub Packages publication."""

from __future__ import annotations

# ruff: noqa: D102, D103
from datetime import UTC, datetime
from typing import TYPE_CHECKING, Any, cast

import pytest
from three_workflow_delivery_v3.adapters import github_packages
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    GovernanceSource,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.release.governance_git import (
        GovernanceGitRead,
    )

TARGET = "a" * 40
OBSERVED_AT = datetime(2026, 8, 13, 22, 15, tzinfo=UTC)
TOKEN = "publish-governance-regression-token"  # noqa: S105
UNSUPPORTED_MESSAGE = (
    "The conditional GitHub Packages version-and-tag primitive is not "
    "implemented; normal Live remains activation-blocked"
)
GOVERNANCE_SOURCE = GovernanceSource(
    repository=GOVERNANCE_REPOSITORY,
    ref=GOVERNANCE_REF,
    path=GOVERNANCE_PATH,
    max_age_days=GOVERNANCE_MAX_AGE_DAYS,
)


class NeverCalledRunner:
    """Runner fake that turns any npm invocation into a test failure."""

    def __init__(self) -> None:
        self.calls = 0

    def run(self, argv: tuple[str, ...], *, env: dict[str, str]) -> object:
        self.calls += 1
        raise AssertionError((argv, env, "npm runner must not be invoked"))


class NeverCalledGovernanceClient:
    """Governance fake that rejects reads after the primitive is blocked."""

    def __init__(self) -> None:
        self.calls: list[object] = []

    def is_ref_protected(self, repository: str, ref: str) -> bool:
        self.calls.append(("protected", repository, ref))
        raise AssertionError("blocked preflight must not reread Governance")

    def read_source(
        self,
        repository: str,
        ref: str,
        path: str,
        *,
        eligibility_main_sha: str | None = None,
    ) -> GovernanceGitRead:
        self.calls.append(
            (
                "read-source",
                repository,
                ref,
                path,
                eligibility_main_sha,
            )
        )
        raise AssertionError("blocked preflight must not reread Governance")


def test_publish_fails_closed_before_npm_runner_when_no_primitive_is_admitted(
    tmp_path: Path,
) -> None:
    runner = NeverCalledRunner()
    client = NeverCalledGovernanceClient()
    tarball = tmp_path / "must-not-be-read.tgz"
    temp_root = tmp_path / "must-not-be-created"

    with pytest.raises(
        github_packages.UnsupportedPublicationPrimitiveError,
    ) as raised:
        github_packages.publish_github_packages_action(
            tarball=tarball,
            target=TARGET,
            token=TOKEN,
            runner=runner,
            temp_root=temp_root,
            governance_source=GOVERNANCE_SOURCE,
            governance_client=client,
            governance_observed_at=OBSERVED_AT,
        )

    assert str(raised.value) == UNSUPPORTED_MESSAGE
    assert runner.calls == 0
    assert client.calls == []
    assert not tarball.exists()
    assert not temp_root.exists()


def test_preflight_validates_publication_closure_then_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    closure_calls: list[dict[str, object]] = []
    forbidden_calls: list[str] = []
    client = NeverCalledGovernanceClient()
    records = {
        name: cast("Any", object())
        for name in (
            "publication_snapshot",
            "authorization",
            "action",
            "qualification_snapshot",
            "qualification_decision",
            "artifact",
            "expectation",
        )
    }
    tarball = tmp_path / "must-not-be-read.tgz"

    def record_publication_closure(**kwargs: object) -> None:
        closure_calls.append(kwargs)

    def reject_forbidden_preflight(*_args: object, **_kwargs: object) -> None:
        forbidden_calls.append("called")
        message = "mutation-capable preflight must not run"
        raise AssertionError(message)

    monkeypatch.setattr(
        github_packages,
        "_validate_publish_preconditions",
        record_publication_closure,
    )
    for helper in (
        "_validate_local_tarball_preconditions",
        "_npm_configuration_digest",
        "_write_private_npm_config",
    ):
        monkeypatch.setattr(
            github_packages,
            helper,
            reject_forbidden_preflight,
        )

    with pytest.raises(
        github_packages.UnsupportedPublicationPrimitiveError,
    ) as raised:
        github_packages.preflight_github_packages_action(
            tarball=tarball,
            target=TARGET,
            publication_snapshot=records["publication_snapshot"],
            authorization=records["authorization"],
            action=records["action"],
            qualification_snapshot=records["qualification_snapshot"],
            qualification_decision=records["qualification_decision"],
            artifact=records["artifact"],
            expectation=records["expectation"],
            governance_source=GOVERNANCE_SOURCE,
            governance_client=client,
            governance_observed_at=OBSERVED_AT,
        )

    assert str(raised.value) == UNSUPPORTED_MESSAGE
    assert closure_calls == [records]
    assert forbidden_calls == []
    assert client.calls == []
    assert not tarball.exists()


# Existing fail-fast fakes intentionally raise direct assertion diagnostics.
# ruff: noqa: D102, D103, D107, EM101, TRY003
