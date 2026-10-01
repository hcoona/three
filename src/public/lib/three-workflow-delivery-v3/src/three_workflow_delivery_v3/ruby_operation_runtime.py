"""Concrete hosted phase ownership, durable spending and authority refresh."""

from __future__ import annotations

import os
from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    require_bootstrap,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_runtime import (
    RubyBootstrapPhaseBudget,
    check_ruby_bootstrap_authority,
)
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyRegistryReader,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform.ruby_github import (
    RUBY_GITHUB_READ_LIMIT,
    RubyGitHubRuntime,
)
from three_workflow_delivery_v3.platform.ruby_operation_http import (
    RubyOperationHttpsTransport,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_REGISTRY_PARTITIONS,
)
from three_workflow_delivery_v3.ruby_operation_host import (
    HostedRubyArtifacts,
    HostedRubyOperation,
    now,
    write,
)

if TYPE_CHECKING:
    from collections.abc import Callable
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyHttpResponse,
        RubyHttpTransport,
    )
    from three_workflow_delivery_v3.release.ruby_governance import (
        RubyGovernance,
    )


def _spending_identity(directory: Path) -> dict[str, JsonValue]:
    return {
        "schema": "workflow-delivery/v3/ruby-hosted-spending-membership-v1",
        "run-id": int(os.environ["GITHUB_RUN_ID"]),
        "run-attempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
        "job": os.environ["GITHUB_JOB"],
        "directory": str(directory.resolve()),
    }


def initialize_spending(directory: Path) -> None:
    """Initialize exactly once in a fixed job/phase stage, before any sends."""
    state = directory.with_name(directory.name + "-membership.json")
    require_bootstrap(
        not directory.exists()
        and not directory.is_symlink()
        and not any(parent.is_symlink() for parent in directory.parents),
        "Ruby spending initialization is already spent",
    )
    # The independent member record is spent before creating the history.
    # Interrupted initialization is never silently repaired by a reader.
    write(
        state,
        {**_spending_identity(directory), "members": []},
        exclusive=True,
    )
    directory.mkdir()
    descriptor = os.open(directory.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def spending_members(directory: Path) -> list[JsonValue]:
    """Require independent complete membership and all original entries."""
    state = directory.with_name(directory.name + "-membership.json")
    require_bootstrap(
        directory.is_dir()
        and not directory.is_symlink()
        and state.is_file()
        and not state.is_symlink()
        and state.stat().st_nlink == 1
        and not any(parent.is_symlink() for parent in directory.parents),
        "Ruby spending history or membership is missing or aliased",
    )
    doc = ruby_object(
        parse_canonical_json(state.read_bytes()),
        {*_spending_identity(directory), "members"},
    )
    members = doc["members"]
    require_bootstrap(
        {key: doc[key] for key in _spending_identity(directory)}
        == _spending_identity(directory)
        and isinstance(members, list)
        and len(members) <= RUBY_GITHUB_READ_LIMIT,
        "Ruby spending membership belongs to another native job",
    )
    members = cast("list[JsonValue]", members)
    require_bootstrap(
        sorted(path.name for path in directory.iterdir())
        == [f"{n:03}.json" for n in range(1, len(members) + 1)],
        "Ruby spending history has missing or unknown entries",
    )
    for ordinal, digest in enumerate(members, 1):
        path = directory / f"{ordinal:03}.json"
        require_bootstrap(
            path.is_file()
            and not path.is_symlink()
            and path.stat().st_nlink == 1
            and ruby_digest(path.read_bytes()) == digest,
            "Ruby spending history changed or aliases another entry",
        )
    return members


def reserve_local(
    directory: Path, ordinal: int, value: dict[str, JsonValue]
) -> None:
    """Spend before sending; never recreate a missing initialized history."""
    members = spending_members(directory)
    require_bootstrap(
        ordinal == len(members) + 1,
        "Ruby hosted spending history is incomplete or already spent",
    )
    write(directory / f"{ordinal:03}.json", value, exclusive=True)
    write(
        directory.with_name(directory.name + "-membership.json"),
        {
            **_spending_identity(directory),
            "members": [*members, canonical_sha256(value)],
        },
    )


def phase_claim(
    operation: HostedRubyOperation, phase: str
) -> dict[str, JsonValue]:
    """Reserve a fixed nontransferable partition before any native reads."""
    require_bootstrap(
        phase in RUBY_REGISTRY_PARTITIONS, "Unknown Ruby registry phase"
    )
    claimed_at = now()
    operation.require_current(claimed_at)
    return {
        "schema": "workflow-delivery/v3/ruby-hosted-phase-claim-v1",
        "request-digest": operation.request.digest,
        "run-id": operation.run_id,
        "run-attempt": 1,
        "phase": phase,
        "maximum": operation.request.envelope.registry_partitions[phase],
        "operator-spent": 1
        if operation.bootstrap
        and operation.registry.name == "rubygems"
        and phase == "eligibility"
        else 0,
        "claimed-at": claimed_at.isoformat(),
        "deadline": operation.deadline(started_at=claimed_at).isoformat(),
        "reservation-digest": canonical_sha256(
            operation.document["reservation"]
        ),
    }


class PhaseReservation:
    """One immutable uploaded phase claim and its exclusive local ordinals."""

    def __init__(self, inputs: HostedRubyArtifacts, phase: str) -> None:
        """Require the actual current producer's raw uploaded phase marker."""
        self.inputs, self.phase = inputs, phase
        self.operation = inputs.operation()
        role = "phase-" + phase
        self.doc = ruby_object(
            inputs.document(role),
            {
                "schema",
                "request-digest",
                "run-id",
                "run-attempt",
                "phase",
                "maximum",
                "operator-spent",
                "claimed-at",
                "deadline",
                "reservation-digest",
            },
        )
        expected = phase_claim(self.operation, phase)
        for key in set(expected) - {"claimed-at", "deadline"}:
            require_bootstrap(
                canonicalize(self.doc[key]) == canonicalize(expected[key]),
                "Ruby phase claim changed its exact operation",
            )
        claimed = datetime.fromisoformat(ruby_text(self.doc["claimed-at"]))
        self.deadline = datetime.fromisoformat(ruby_text(self.doc["deadline"]))
        require_bootstrap(
            claimed.tzinfo is not None
            and self.deadline.tzinfo is not None
            and claimed
            <= now()
            < self.deadline
            <= claimed + timedelta(minutes=30)
            and self.deadline
            <= datetime.fromisoformat(
                ruby_text(self.operation.request.document["expires-at"])
            )
            and os.environ["GITHUB_JOB"] == inputs.producer(role),
            "Ruby phase claim is expired or belongs to another job",
        )
        self.directory = inputs.directory / "spending" / phase

    def reserve(self, phase: str, ordinal: int) -> None:
        """Bind each attempted send to its already reserved immutable cap."""
        self.operation.require_current(now())
        require_bootstrap(
            phase == self.phase
            and now() < self.deadline
            and 0
            < ordinal
            <= self.operation.request.envelope.registry_partitions[phase],
            "Ruby registry send exceeds its current phase",
        )
        operator_spent = cast("int", self.doc["operator-spent"])
        reserve_local(
            self.directory,
            ordinal - operator_spent,
            {
                "phase-claim-reference": self.inputs.reference(
                    "phase-" + phase
                ).to_document(),
                "ordinal": ordinal,
                "reserved-at": now().isoformat(),
            },
        )


class _NormalBudget(RubyRequestBudget):
    def __init__(
        self, transport: RubyHttpTransport, reservation: PhaseReservation
    ) -> None:
        super().__init__(transport)
        self.reservation = reservation

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        self.reservation.reserve(self.reservation.phase, self.used + 1)
        return super().request(method, url, headers, body, maximum_bytes)


def registry_reader(
    inputs: HostedRubyArtifacts, phase: str, token: str
) -> RubyRegistryReader:
    """Join one concrete job's phase lease and supervised native transport."""
    reservation = PhaseReservation(inputs, phase)
    operation = reservation.operation
    transport = RubyOperationHttpsTransport(reservation.deadline)
    budget = (
        RubyBootstrapPhaseBudget(
            operation.bootstrap_run(now()),
            phase,
            transport,
            reservation.reserve,
            deadline=reservation.deadline,
            clock=now,
        )
        if operation.bootstrap
        else _NormalBudget(transport, reservation)
    )
    return RubyRegistryReader(
        operation.registry,
        budget,
        github_read_token=token
        if operation.registry.name == "github-packages"
        else None,
    )


class HostedRubyGitHub(RubyGitHubRuntime):
    """One job's shared durable direct-control budget across CLI stages."""

    def __init__(self, inputs: HostedRubyArtifacts, token: str) -> None:
        """Use scoped job capability and an absolute supervised phase."""
        self.inputs = inputs
        super().__init__(
            token,
            RubyOperationHttpsTransport(inputs.operation().deadline()),
            clock=now,
        )

    def get(self, path: str) -> JsonValue:
        """Persist a direct read before sending, with no per-process refill."""
        directory = self.inputs.directory / "github-spending"
        count = len(spending_members(directory))
        require_bootstrap(
            count < RUBY_GITHUB_READ_LIMIT,
            "Ruby job direct GitHub read budget exhausted",
        )
        reserve_local(
            directory,
            count + 1,
            {"path": path, "reserved-at": now().isoformat()},
        )
        document = super().get(path)
        evidence = self.inputs.directory / "github-responses"
        write(
            evidence / f"{count + 1:03}.json",
            {"path": path, "body": document},
            exclusive=True,
        )
        return document


def authority_guard(
    inputs: HostedRubyArtifacts,
    github: HostedRubyGitHub,
    initial: RubyGovernance | None = None,
) -> Callable[[], dict[str, JsonValue]]:
    """Invoke full-history freshness at every real authority boundary."""
    operation = inputs.operation()

    def check() -> dict[str, JsonValue]:
        operation.require_current(now())
        if operation.bootstrap:
            return check_ruby_bootstrap_authority(
                operation.bootstrap_run(now()), github, os.environ, now()
            )
        read = github.operation_control(operation.target)
        require_bootstrap(
            ruby_digest(read.content)
            == operation.request.document["envelope-digest"],
            "Ruby operation envelope changed after dispatch",
        )
        fresh = github.governance(
            operation.registry, initial=initial or operation.governance(now())
        )
        operation.require_current(now())
        require_bootstrap(
            fresh.digest == operation.request.document["configuration-digest"],
            "Ruby normal authority drifted from its request",
        )
        return {
            "schema": "workflow-delivery/v3/ruby-hosted-current-authority-v1",
            "request-digest": operation.request.digest,
            "main": read.main_sha,
            "governance": fresh.document,
            "source-commit": fresh.source_commit,
            "observed-at": fresh.observed_at.isoformat(),
        }

    return check
