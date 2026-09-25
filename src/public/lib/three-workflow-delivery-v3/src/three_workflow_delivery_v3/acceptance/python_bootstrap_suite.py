"""Durable bootstrap phases and original-response credential-free replay."""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_bootstrap_capture import (
    capture_exact,
    require_absent,
    upload_pair,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    SENTINEL,
    WINDOW_SECONDS,
    WORKFLOW,
    BootstrapRequest,
    authority_window,
    phase_binding,
    platform_facts,
    utc_number,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_fixture import (
    BootstrapFixtures,
    validate_prepared,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_transport import (
    JournalTransport,
    ReplayTransport,
)
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    require,
    write_exclusive,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    consumer_evidence,
    validate_consumer_evidence,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    prove_approval,
)
from three_workflow_delivery_v3.adapters.pypi import mint_python_token
from three_workflow_delivery_v3.adapters.python import qualify_python_consumer
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.platform.python_github import (
    PythonGitHubRuntime,
    obtain_python_oidc_assertion,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.pypi import PythonHttpTransport
    from three_workflow_delivery_v3.adapters.python import (
        PythonConsumerResult,
        PythonDistribution,
    )
    from three_workflow_delivery_v3.records.artifacts import ArtifactReference

BASE_NAMES = {
    "request.json",
    "binding.json",
    "platform.json",
    "references.json",
}
SUCCESS: dict[str, JsonValue] = {
    "status": "success",
    "account-ownership": False,
    "native-admission": False,
    "live-enabled": False,
}


@dataclass(frozen=True)
class BootstrapContext:
    """Exact execution identity and immutable artifact lineage."""

    request: BootstrapRequest
    run_id: int
    tooling_sha: str
    references: dict[str, ArtifactReference]

    def files(self, phase: str, roles: tuple[str, ...]) -> dict[str, bytes]:
        """Create the closed retained phase identity."""
        return {
            "request.json": self.request.content,
            "binding.json": canonicalize(
                phase_binding(
                    self.request, self.run_id, self.tooling_sha, phase
                )
            ),
            "platform.json": canonicalize(
                platform_facts(self.run_id, self.tooling_sha)
            ),
            "references.json": canonicalize(
                {role: self.references[role].to_document() for role in roles}
            ),
        }

    def validate(
        self,
        files: dict[str, bytes],
        phase: str,
        roles: tuple[str, ...],
        names: set[str],
    ) -> None:
        """Close the inventory including every raw response record."""
        expected = self.files(phase, roles)
        require(
            all(files.get(name) == value for name, value in expected.items()),
            "bootstrap phase identity or lineage differs",
        )
        actual = {
            name
            for name in files
            if name != "requests.json"
            and not name.startswith("http/")
            and not (
                phase == "execute"
                and name.startswith(("observation/p2/", "observation/p3/"))
            )
        }
        require(
            actual == BASE_NAMES | names
            and parse_canonical_json(files["result.json"]) == SUCCESS,
            "bootstrap phase incomplete or unsuccessful",
        )


def _proof(
    context: BootstrapContext,
    http: PythonHttpTransport,
    token: str,
    sentinel: str,
) -> bytes:
    return prove_approval(
        context.request,
        context.run_id,
        context.tooling_sha,
        sentinel,
        PythonGitHubRuntime(token, http),
        WORKFLOW,
    )


def _retainer(
    output: Path, files: dict[str, bytes]
) -> Callable[[str, bytes], None]:
    output.mkdir(parents=True, exist_ok=False)

    def retain(name: str, content: bytes) -> None:
        write_exclusive(output, name, content)

    for name, value in files.items():
        retain(name, value)
    return retain


def authorize(  # noqa: PLR0913 - exact phase artifact tuple
    context: BootstrapContext,
    prepared: dict[str, bytes],
    output: Path,
    http: PythonHttpTransport,
    environment: Mapping[str, str],
    *,
    clock: Callable[[], float] = time.time,
) -> None:
    """Start the original fixed UTC window before the first proof/P0 call."""
    validate_prepared(
        prepared, context.request, context.run_id, context.tooling_sha
    )
    created = utc_number(clock())
    window: dict[str, JsonValue] = {
        "created-at": created,
        "deadline": created + WINDOW_SECONDS,
    }
    retain = _retainer(
        output,
        {
            **context.files("authorize", ("prepared",)),
            "window.json": canonicalize(window),
        },
    )
    journal = JournalTransport(
        http,
        "authorize",
        retain,
        window=window,
        clock=clock,
        secrets=(environment["GITHUB_TOKEN"],),
    )
    try:
        retain(
            "approval.json",
            _proof(
                context,
                journal,
                environment["GITHUB_TOKEN"],
                environment["WDV3_APPROVAL_ENVIRONMENT_MARKER"],
            ),
        )
        require_absent(journal)
    except Exception:
        retain(
            "result.json",
            canonicalize({"status": "failed", "effects": "read-only"}),
        )
        raise
    else:
        retain("result.json", canonicalize(SUCCESS))
    finally:
        journal.close()


def replay_authorization(
    context: BootstrapContext, files: dict[str, bytes]
) -> JsonValue:
    """Recompute the first proof and P0 from original response bodies."""
    context.validate(
        files,
        "authorize",
        ("prepared",),
        {"window.json", "approval.json", "result.json"},
    )
    window = parse_canonical_json(files["window.json"])
    authority_window(window)
    replay = ReplayTransport(files, "authorize", window=window)
    require(
        files["approval.json"]
        == _proof(context, replay, "offline-proof", SENTINEL),
        "bootstrap first proof differs",
    )
    require_absent(replay)
    replay.finished()
    return window


def marker(
    context: BootstrapContext,
    prepared: dict[str, bytes],
    authorization: dict[str, bytes],
    *,
    clock: Callable[[], float] = time.time,
) -> dict[str, bytes]:
    """Bind immutable authorization readback before admitting credentials."""
    validate_prepared(
        prepared, context.request, context.run_id, context.tooling_sha
    )
    window = replay_authorization(context, authorization)
    created = utc_number(clock())
    authority_window(window, created)
    last = ReplayTransport(authorization, "authorize", window=window).records[
        -1
    ]
    require(
        created >= cast("float", last["finish"]),
        "bootstrap marker predates authorization",
    )
    return {
        **context.files("marker", ("prepared", "authorization")),
        "window.json": canonicalize(window),
        "created-at.json": canonicalize(created),
        "result.json": canonicalize(SUCCESS),
    }


def validate_marker(
    context: BootstrapContext,
    authorization: dict[str, bytes],
    files: dict[str, bytes],
) -> JsonValue:
    """Reject a renewed, foreign, early or surplus marker."""
    window = replay_authorization(context, authorization)
    context.validate(
        files,
        "marker",
        ("prepared", "authorization"),
        {"window.json", "created-at.json", "result.json"},
    )
    require(
        set(files)
        == BASE_NAMES | {"window.json", "created-at.json", "result.json"}
        and files["window.json"] == canonicalize(window),
        "bootstrap marker authority differs",
    )
    created = utc_number(parse_json_strict(files["created-at.json"]))
    authority_window(window, created)
    require(
        created
        >= cast(
            "float",
            ReplayTransport(authorization, "authorize", window=window).records[
                -1
            ]["finish"],
        ),
        "bootstrap marker predates authorization",
    )
    return window


def execute(  # noqa: PLR0913, PLR0917 - exact phase artifact tuple
    context: BootstrapContext,
    prepared: dict[str, bytes],
    authorization: dict[str, bytes],
    marked: dict[str, bytes],
    output: Path,
    http: PythonHttpTransport,
    environment: Mapping[str, str],
    *,
    clock: Callable[[], float] = time.time,
    monotonic: Callable[[], float] = time.monotonic,
    wait: Callable[[float], None] = time.sleep,
) -> None:
    """P1 and one token exchange precede U1/P2/U2/P3, without recovery sends."""
    fixtures = validate_prepared(
        prepared, context.request, context.run_id, context.tooling_sha
    )
    window = validate_marker(context, authorization, marked)
    now = utc_number(clock())
    authority_window(window, now)
    require(
        now >= utc_number(parse_json_strict(marked["created-at.json"])),
        "bootstrap execution precedes marker",
    )
    retain = _retainer(
        output,
        {
            **context.files("execute", ("prepared", "authorization", "marker")),
            "window.json": canonicalize(window),
        },
    )
    journal = JournalTransport(
        http,
        "execute",
        retain,
        window=window,
        clock=clock,
        monotonic=monotonic,
        secrets=(
            environment["GITHUB_TOKEN"],
            environment["ACTIONS_ID_TOKEN_REQUEST_TOKEN"],
        ),
    )
    try:
        retain(
            "approval.json",
            _proof(
                context,
                journal,
                environment["GITHUB_TOKEN"],
                environment["WDV3_APPROVAL_ENVIRONMENT_MARKER"],
            ),
        )
        require_absent(journal)
        assertion = obtain_python_oidc_assertion(
            "testpypi", environment, journal
        )
        journal.add_secret(assertion)
        token = mint_python_token(context.request.registry, assertion, journal)
        journal.add_secret(token)
        upload_pair(
            journal,
            fixtures.distributions,
            token,
            retain=retain,
            deadline=journal.monotonic_deadline,
            clock=journal.monotonic,
            wait=wait,
        )
    except Exception:
        retain(
            "result.json",
            canonicalize({"status": "failed", "effects": "unknown-or-partial"}),
        )
        raise
    else:
        retain("result.json", canonicalize(SUCCESS))
    finally:
        journal.close()


def replay_execution(
    context: BootstrapContext,
    prepared: dict[str, bytes],
    authorization: dict[str, bytes],
    marked: dict[str, bytes],
    result: dict[str, bytes],
) -> BootstrapFixtures:
    """Replay original proof, absent state, upload bodies and every readback."""
    fixtures = validate_prepared(
        prepared, context.request, context.run_id, context.tooling_sha
    )
    window = validate_marker(context, authorization, marked)
    original_authorization = {
        key.removeprefix("authorization/"): value
        for key, value in result.items()
        if key.startswith("authorization/")
    }
    files = {
        key.removeprefix("execution/"): value
        for key, value in result.items()
        if key.startswith("execution/")
    }
    require(
        original_authorization == authorization
        and all(
            key.startswith(("authorization/", "execution/")) for key in result
        ),
        "bootstrap original authorization subtree differs",
    )
    context.validate(
        files,
        "execute",
        ("prepared", "authorization", "marker"),
        {"window.json", "approval.json", "result.json"},
    )
    require(
        files["window.json"] == canonicalize(window),
        "bootstrap execution renewed authority",
    )
    replay = ReplayTransport(files, "execute", window=window)
    require(
        bool(replay.records)
        and cast("float", replay.records[0]["start"])
        >= utc_number(parse_json_strict(marked["created-at.json"])),
        "bootstrap execution precedes marker",
    )
    require(
        files["approval.json"]
        == _proof(context, replay, "offline-proof", SENTINEL),
        "bootstrap second proof differs",
    )
    require_absent(replay)
    replay.credential("oidc")
    replay.credential("mint")
    upload_pair(
        replay,
        fixtures.distributions,
        "pypi-offline-replay",
        retain=lambda _name, _content: None,
        deadline=replay.monotonic_deadline,
        replay=files,
    )
    replay.finished()
    return fixtures


def audit(  # noqa: PLR0913, PLR0917 - exact phase artifact tuple
    context: BootstrapContext,
    prepared: dict[str, bytes],
    authorization: dict[str, bytes],
    marked: dict[str, bytes],
    result: dict[str, bytes],
    output: Path,
    http: PythonHttpTransport,
    *,
    consumer: Callable[
        [PythonDistribution], PythonConsumerResult
    ] = qualify_python_consumer,
    clock: Callable[[], float] = time.time,
) -> None:
    """Run a separate credential-free P4 and two fresh real clean consumers."""
    fixtures = replay_execution(
        context, prepared, authorization, marked, result
    )
    execution = {
        key.removeprefix("execution/"): value
        for key, value in result.items()
        if key.startswith("execution/")
    }
    previous = ReplayTransport(
        execution,
        "execute",
        window=parse_canonical_json(execution["window.json"]),
    )
    require(
        utc_number(clock()) >= cast("float", previous.records[-1]["finish"]),
        "bootstrap P4 precedes execution",
    )
    retain = _retainer(
        output,
        context.files(
            "audit", ("prepared", "authorization", "marker", "result")
        ),
    )
    journal = JournalTransport(http, "audit", retain, clock=clock)
    try:
        for item in capture_exact(
            journal, tuple(fixtures.distributions.values())
        ):
            evidence = consumer_evidence(consumer(item))
            validate_consumer_evidence(evidence, item)
            retain(f"consumer/{item.variant}.json", evidence)
    except Exception:
        retain(
            "result.json",
            canonicalize({"status": "failed", "effects": "read-only"}),
        )
        raise
    else:
        retain("result.json", canonicalize(SUCCESS))
    finally:
        journal.close()


def replay_audit(  # noqa: PLR0913, PLR0917 - exact phase artifact tuple
    context: BootstrapContext,
    prepared: dict[str, bytes],
    authorization: dict[str, bytes],
    marked: dict[str, bytes],
    result: dict[str, bytes],
    audited: dict[str, bytes],
) -> dict[str, bytes]:
    """Replay retained P4 facts without making a new observation."""
    fixtures = replay_execution(
        context, prepared, authorization, marked, result
    )
    context.validate(
        audited,
        "audit",
        ("prepared", "authorization", "marker", "result"),
        {"consumer/wheel.json", "consumer/sdist.json", "result.json"},
    )
    replay = ReplayTransport(audited, "audit")
    execution = {
        key.removeprefix("execution/"): value
        for key, value in result.items()
        if key.startswith("execution/")
    }
    previous = ReplayTransport(
        execution,
        "execute",
        window=parse_canonical_json(execution["window.json"]),
    )
    require(
        bool(replay.records)
        and cast("float", replay.records[0]["start"])
        >= cast("float", previous.records[-1]["finish"]),
        "bootstrap P4 precedes execution",
    )
    for item in capture_exact(replay, tuple(fixtures.distributions.values())):
        validate_consumer_evidence(
            audited[f"consumer/{item.variant}.json"], item
        )
    replay.finished()
    return {
        **context.files(
            "replay", ("prepared", "authorization", "marker", "result", "audit")
        ),
        "result.json": canonicalize(
            {**SUCCESS, "evidence": "supplied-facts-only"}
        ),
    }
