"""Finite, durable Ruby configuration spending for the sole POSIX operator."""

from __future__ import annotations

import os
import re
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import cast

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_operation_ledger import (
    _durable_new,
    _sync_directory,
)

_REPO = "/repos/hcoona/three"
_CONTROL_PHASES = (
    "initial-controls",
    "post-configuration-controls",
    "bootstrap-controls",
    "postbootstrap-controls",
    "normal01-controls",
    "normal02-controls",
    "normal03-controls",
    "normal04-controls",
)
_SLOTS = ("bootstrap", "normal01", "normal02", "normal03", "normal04")


def _time(value: JsonValue) -> datetime:
    text = ruby_text(value)
    result = datetime.fromisoformat(text)
    if not text.endswith("Z") or result.utcoffset() != timedelta():
        msg = "Ruby configuration ledger requires UTC timestamps"
        raise ValueError(msg)
    return result


def _stamp(now: datetime) -> str:
    if now.tzinfo is None or now.utcoffset() is None:
        msg = "Ruby configuration ledger requires an aware clock"
        raise ValueError(msg)
    return now.astimezone(UTC).isoformat().replace("+00:00", "Z")


def configuration_phase_limits(destination: str) -> dict[str, int]:
    """Return fresh fixed allocations; unused sends never transfer."""
    RubyRegistry(destination)
    result = dict.fromkeys(_CONTROL_PHASES, 32)
    result.update({slot + "-main": 1 for slot in _SLOTS})
    result.update(
        dict.fromkeys(("environment-write", "branch-write", "marker-write"), 1)
    )
    if destination == "github-packages":
        result.update({"initial-inventory": 10, "bootstrap-inventory": 10})
        result.update(
            {slot + "-package": 1 for slot in ("postbootstrap", *_SLOTS[1:])}
        )
    else:
        result.update({"initial-owners": 1, "postbootstrap-owners": 1})
    return result


@dataclass(frozen=True, slots=True)
class RubyConfigurationPlan:
    """Exact independently reviewed plan identity, not approval provenance."""

    content: bytes

    def __post_init__(self) -> None:
        """Reject altered quotas, identities and plan lifetimes."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "campaign",
                "destination",
                "directory",
                "source-commit",
                "caller-digest",
                "protocol-digest",
                "issued-at",
                "expires-at",
                "phases",
            },
        )
        if doc["schema"] != "workflow-delivery/v3/ruby-configuration-plan-v1":
            msg = "Unsupported Ruby configuration plan"
            raise ValueError(msg)
        for key, pattern in (
            ("campaign", r"[0-9a-f]{32}"),
            ("source-commit", r"[0-9a-f]{40}"),
            ("caller-digest", r"sha256:[0-9a-f]{64}"),
            ("protocol-digest", r"sha256:[0-9a-f]{64}"),
        ):
            if not re.fullmatch(pattern, ruby_text(doc[key])):
                msg = "Ruby configuration plan identity is malformed"
                raise ValueError(msg)
        location = Path(ruby_text(doc["directory"]))
        if not location.is_absolute() or location.resolve() != location:
            msg = "Ruby configuration requires its exact absolute ledger path"
            raise ValueError(msg)
        limits = configuration_phase_limits(ruby_text(doc["destination"]))
        if canonicalize(doc["phases"]) != canonicalize(
            cast("JsonValue", limits)
        ):
            msg = "Ruby configuration plan changes fixed phase allocations"
            raise ValueError(msg)
        issued, expires = _time(doc["issued-at"]), _time(doc["expires-at"])
        if not issued < expires <= issued + timedelta(days=7):
            msg = "Ruby configuration plan exceeds its finite lifetime"
            raise ValueError(msg)

    @property
    def document(self) -> dict[str, JsonValue]:
        """Parse a fresh view of immutable canonical bytes."""
        return parse_canonical_json(self.content)

    @property
    def directory(self) -> Path:
        """Select the independently fixed durable location."""
        return Path(ruby_text(self.document["directory"]))

    def require_current(self, now: datetime) -> None:
        """Reject stale plans without extending timestamps."""
        _stamp(now)
        if (
            not _time(self.document["issued-at"])
            <= now
            < _time(self.document["expires-at"])
        ):
            msg = "Ruby configuration plan is not current"
            raise ValueError(msg)


def initialize_ruby_configuration_ledger(
    plan: RubyConfigurationPlan, *, now: datetime
) -> None:
    """Create once after admission; never repair an old directory."""
    plan.require_current(now)
    plan.directory.mkdir(mode=0o700)
    _durable_new(plan.directory / "plan.json", plan.content)
    _durable_new(
        plan.directory / "phases.jsonl",
        canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-configuration-phases-v1",
                "plan-digest": ruby_digest(plan.content),
            }
        )
        + b"\n",
    )
    _sync_directory(plan.directory.parent)


def _read(path: Path) -> bytes:
    if path.is_symlink() or not path.is_file():
        msg = "Ruby configuration evidence is missing or replaced"
        raise ValueError(msg)
    return path.read_bytes()


class RubyConfigurationLedger:
    """A plan's finite phases; an interrupted phase prevents further work."""

    def __init__(self, plan: RubyConfigurationPlan) -> None:
        """Bind existing bytes; opening never resumes an interrupted phase."""
        self.plan = plan
        self._require_present()

    def _require_present(self) -> None:
        if (
            self.plan.directory.resolve() != self.plan.directory
            or _read(self.plan.directory / "plan.json") != self.plan.content
        ):
            msg = "Ruby configuration ledger lost its admitted plan"
            raise ValueError(msg)
        lines = _read(self.plan.directory / "phases.jsonl").splitlines(
            keepends=True
        )
        header = (
            canonicalize(
                {
                    "schema": (
                        "workflow-delivery/v3/ruby-configuration-phases-v1"
                    ),
                    "plan-digest": ruby_digest(self.plan.content),
                }
            )
            + b"\n"
        )
        if not lines or lines[0] != header:
            msg = "Ruby configuration phase inventory is missing or changed"
            raise ValueError(msg)
        limits = configuration_phase_limits(
            ruby_text(self.plan.document["destination"])
        )
        names = {"plan.json", "phases.jsonl"}
        for line in lines[1:]:
            if not line.endswith(b"\n"):
                msg = "Ruby configuration phase reservation is partial"
                raise ValueError(msg)
            entry = ruby_object(
                parse_canonical_json(line[:-1]), {"phase", "phase-digest"}
            )
            name = ruby_text(entry["phase"])
            path = self.plan.directory / name
            if (
                name not in limits
                or name in names
                or path.is_symlink()
                or not path.is_dir()
                or ruby_digest(_read(path / "phase.json"))
                != entry["phase-digest"]
            ):
                msg = "Ruby configuration reserved phase is missing or changed"
                raise ValueError(msg)
            names.add(name)
        if {path.name for path in self.plan.directory.iterdir()} != names:
            msg = "Ruby configuration phase inventory has unjoined state"
            raise ValueError(msg)

    def begin(self, phase: str, *, now: datetime) -> RubyConfigurationPhase:
        """Reserve one whole phase before its first possible send."""
        self._require_present()
        self.plan.require_current(now)
        limits = configuration_phase_limits(
            ruby_text(self.plan.document["destination"])
        )
        if phase not in limits:
            msg = "Ruby configuration phase is outside its plan"
            raise ValueError(msg)
        for path in self.plan.directory.iterdir():
            if path.name in {"plan.json", "phases.jsonl"}:
                continue
            if (
                path.name not in limits
                or path.is_symlink()
                or not path.is_dir()
            ):
                msg = "Ruby configuration ledger has unknown state"
                raise ValueError(msg)
            _verify_completed_phase(path, self.plan)
        directory = self.plan.directory / phase
        if directory.exists():
            raise FileExistsError(directory)
        deadline = min(
            now + timedelta(minutes=30), _time(self.plan.document["expires-at"])
        )
        content = canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-configuration-phase-v1",
                "plan-digest": ruby_digest(self.plan.content),
                "phase": phase,
                "started-at": _stamp(now),
                "deadline": _stamp(deadline),
                "limit": limits[phase],
            }
        )
        reservation = (
            canonicalize({"phase": phase, "phase-digest": ruby_digest(content)})
            + b"\n"
        )
        # A torn append or missing phase remains spent and stops reopening.
        descriptor = os.open(
            self.plan.directory / "phases.jsonl",
            os.O_WRONLY | os.O_APPEND | os.O_NOFOLLOW,
        )
        try:
            if os.write(descriptor, reservation) != len(reservation):
                msg = "Ruby configuration phase reservation write was partial"
                raise OSError(msg)
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
        directory.mkdir(mode=0o700)
        _durable_new(directory / "phase.json", content)
        _sync_directory(directory.parent)
        return RubyConfigurationPhase(self, phase, content)


def _verify_completed_phase(
    directory: Path, plan: RubyConfigurationPlan
) -> None:
    """Reject missing original evidence before beginning a later phase."""
    phase_content = _read(directory / "phase.json")
    phase = ruby_object(parse_canonical_json(phase_content))
    terminal = ruby_object(
        parse_canonical_json(_read(directory / "complete.json")),
        {
            "schema",
            "plan-digest",
            "phase",
            "phase-digest",
            "status",
            "completed-at",
            "receipts",
        },
    )
    receipts = terminal["receipts"]
    if (
        terminal["schema"]
        != "workflow-delivery/v3/ruby-configuration-phase-result-v1"
        or terminal["phase"] != directory.name
        or terminal["plan-digest"] != ruby_digest(plan.content)
        or terminal["phase-digest"] != ruby_digest(phase_content)
        or terminal["status"] != "complete"
        or not isinstance(receipts, list)
        or len(receipts)
        > configuration_phase_limits(ruby_text(plan.document["destination"]))[
            directory.name
        ]
        or phase.get("plan-digest") != ruby_digest(plan.content)
        or phase.get("phase") != directory.name
    ):
        message = "Ruby configuration prior phase is incomplete or changed"
        raise ValueError(message)
    names = {"phase.json", "complete.json"}
    for ordinal, digest in enumerate(receipts, 1):
        request_name = f"{ordinal:02d}.request.json"
        evidence_name = f"{ordinal:02d}.evidence.json"
        receipt_name = f"{ordinal:02d}.receipt.json"
        names.update((request_name, evidence_name, receipt_name))
        request = _read(directory / request_name)
        evidence = _read(directory / evidence_name)
        content = _read(directory / receipt_name)
        receipt = ruby_object(parse_canonical_json(content))
        if (
            ruby_digest(content) != digest
            or receipt.get("request-digest") != ruby_digest(request)
            or receipt.get("evidence-digest") != ruby_digest(evidence)
            or receipt.get("successful") is not True
        ):
            message = "Ruby configuration prior send evidence is incomplete"
            raise ValueError(message)
    if {path.name for path in directory.iterdir()} != names:
        message = "Ruby configuration prior phase has unjoined spending"
        raise ValueError(message)


class RubyConfigurationPhase:
    """One process owns a phase; crashes leave durable unresumable spending."""

    def __init__(
        self, ledger: RubyConfigurationLedger, name: str, content: bytes
    ) -> None:
        """Retain the exclusive phase identity and its local send cursor."""
        self.ledger, self.name, self.content = ledger, name, content
        self.directory = ledger.plan.directory / name
        doc = parse_canonical_json(content)
        self.deadline = _time(doc["deadline"])
        self.limit = cast("int", doc["limit"])
        self._started = _time(doc["started-at"])
        self._latest = self._started
        self._sends: list[tuple[bytes, bytes]] = []
        self._pending: bytes | None = None
        self._failed = False
        self._finished = False

    def _current(self, now: datetime) -> None:
        self.ledger._require_present()  # noqa: SLF001 - same ledger implementation
        self.ledger.plan.require_current(now)
        if (
            self._finished
            or self._failed
            or not self._latest <= now < self.deadline
        ):
            msg = "Ruby configuration phase is closed, failed or stale"
            raise ValueError(msg)
        if (
            self.directory.is_symlink()
            or _read(self.directory / "phase.json") != self.content
        ):
            msg = "Ruby configuration phase identity changed"
            raise ValueError(msg)
        if (self.directory / "complete.json").exists():
            msg = "Ruby configuration phase is already closed"
            raise ValueError(msg)
        for index, (request, receipt) in enumerate(self._sends, 1):
            if (
                _read(self.directory / f"{index:02d}.request.json") != request
                or _read(self.directory / f"{index:02d}.receipt.json")
                != receipt
            ):
                msg = "Ruby configuration prior spending evidence changed"
                raise ValueError(msg)
            evidence = _read(self.directory / f"{index:02d}.evidence.json")
            if (
                ruby_digest(evidence)
                != parse_canonical_json(receipt)["evidence-digest"]
            ):
                message = (
                    "Ruby configuration original response evidence changed"
                )
                raise ValueError(message)

    def spend(
        self, method: str, path: str, body: bytes | None, *, now: datetime
    ) -> bytes:
        """Persist exact intent before transport; one unfinished send stops."""
        self._current(now)
        if self._pending is not None or len(self._sends) >= self.limit:
            msg = "Ruby configuration send is pending or exhausted"
            raise ValueError(msg)
        _require_request(self.ledger.plan, self.name, method, path, body)
        ordinal = len(self._sends) + 1
        content = canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-configuration-send-v1",
                "phase-digest": ruby_digest(self.content),
                "ordinal": ordinal,
                "method": method,
                "path": path,
                "body-digest": None if body is None else ruby_digest(body),
                "reserved-at": _stamp(now),
            }
        )
        # Set pending before persistence too: failed writes are never reusable.
        self._pending = content
        self._latest = now
        _durable_new(self.directory / f"{ordinal:02d}.request.json", content)
        return content

    def complete_send(
        self,
        reservation: bytes,
        evidence: bytes,
        *,
        successful: bool,
        now: datetime,
    ) -> None:
        """Retain sanitized evidence; failed sends remain spent."""
        self._current(now)
        if (
            type(successful) is not bool
            or reservation != self._pending
            or self._pending is None
        ):
            msg = "Ruby configuration receipt lacks its pending send"
            raise ValueError(msg)
        ordinal = len(self._sends) + 1
        if _read(self.directory / f"{ordinal:02d}.request.json") != reservation:
            msg = "Ruby configuration original reservation changed"
            raise ValueError(msg)
        parse_canonical_json(evidence)
        content = canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-configuration-receipt-v1",
                "request-digest": ruby_digest(reservation),
                "evidence-digest": ruby_digest(evidence),
                "successful": successful,
                "completed-at": _stamp(now),
            }
        )
        _durable_new(self.directory / f"{ordinal:02d}.evidence.json", evidence)
        _durable_new(self.directory / f"{ordinal:02d}.receipt.json", content)
        self._sends.append((reservation, content))
        self._latest = now
        self._pending = None
        self._failed = not successful

    def finish(self, *, now: datetime) -> bytes:
        """Close successful sends without transferring remaining quota."""
        self._current(now)
        if self._pending is not None:
            msg = "Ruby configuration phase has a pending send"
            raise ValueError(msg)
        content = canonicalize(
            {
                "schema": (
                    "workflow-delivery/v3/ruby-configuration-phase-result-v1"
                ),
                "plan-digest": ruby_digest(self.ledger.plan.content),
                "phase": self.name,
                "phase-digest": ruby_digest(self.content),
                "status": "complete",
                "completed-at": _stamp(now),
                "receipts": [
                    ruby_digest(receipt) for _, receipt in self._sends
                ],
            }
        )
        self._finished = True
        _durable_new(self.directory / "complete.json", content)
        return content


def _require_request(  # noqa: C901, PLR0912 - fixed protocol roles
    plan: RubyConfigurationPlan,
    phase: str,
    method: str,
    path: str,
    body: bytes | None,
) -> None:
    """Reject foreign paths and write bodies before any intent is spent."""
    registry = RubyRegistry(ruby_text(plan.document["destination"]))
    environment = _REPO + "/environments/" + registry.environment
    if phase.endswith("-write"):
        expected: dict[str, JsonValue]
        if phase == "environment-write":
            selected_method, selected_path = "PUT", environment
            expected = {
                "wait_timer": 0,
                "prevent_self_review": False,
                "reviewers": [{"type": "User", "id": 712433}],
                "deployment_branch_policy": {
                    "protected_branches": False,
                    "custom_branch_policies": True,
                },
            }
        elif phase == "branch-write":
            selected_method, selected_path = (
                "POST",
                environment + "/deployment-branch-policies",
            )
            expected = {"name": "main", "type": "branch"}
        else:
            selected_method = method
            selected_path = environment + "/variables"
            expected = {
                "name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
                "value": registry.environment + "/v1",
            }
            if method == "PATCH":
                selected_path += "/WDV3_APPROVAL_ENVIRONMENT_MARKER"
                expected.pop("name")
            elif method != "POST":
                msg = "Ruby configuration marker method is unsupported"
                raise ValueError(msg)
        if (
            method != selected_method
            or path != selected_path
            or body != canonicalize(expected)
        ):
            msg = "Ruby configuration write differs from its exact role"
            raise ValueError(msg)
        return
    if method != "GET" or body is not None:
        msg = "Ruby configuration read phase cannot mutate"
        raise ValueError(msg)
    allowed = False
    if phase.endswith("-main"):
        allowed = path == _REPO + "/branches/main"
    elif phase.endswith("-owners"):
        allowed = path == "/api/v1/gems/hcoona-release-smoke-ruby/owners.json"
    elif phase.endswith("-package"):
        allowed = path == "/user/packages/rubygems/hcoona-release-smoke-ruby"
    elif phase.endswith("-inventory"):
        allowed = (
            re.fullmatch(
                r"/user/packages\?package_type=rubygems&per_page=100&page=([1-9]|10)",
                path,
            )
            is not None
        )
    elif phase in _CONTROL_PHASES:
        allowed = _control_path(path, environment)
    if not allowed:
        msg = "Ruby configuration request is outside its finite role"
        raise ValueError(msg)


def _control_path(path: str, environment: str) -> bool:
    fixed = {
        "/user",
        _REPO,
        _REPO + "/branches/main",
        _REPO + "/branches/main/protection",
        environment,
    }
    if path in fixed:
        return True
    patterns = (
        re.escape(_REPO) + r"/rules/branches/main\?per_page=100&page=[1-5]",
        re.escape(_REPO)
        + r"/rulesets\?includes_parents=true&per_page=100&page=[1-5]",
        re.escape(_REPO) + r"/rulesets/[1-9][0-9]*\?includes_parents=true",
        re.escape(_REPO)
        + r"/collaborators\?affiliation=all&per_page=100&page=[1-5]",
        re.escape(environment)
        + (
            r"/(deployment-branch-policies|secrets|variables)"
            r"\?per_page=100&page=[12]"
        ),
        re.escape(environment) + r"/deployment_protection_rules",
    )
    return any(re.fullmatch(pattern, path) is not None for pattern in patterns)
