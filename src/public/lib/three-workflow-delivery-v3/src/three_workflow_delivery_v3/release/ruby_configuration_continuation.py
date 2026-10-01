"""Exact M01 acknowledgement for unused reads in one failed plan."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import JsonValue, parse_canonical_json

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.release.ruby_configuration_ledger import (
        RubyConfigurationPlan,
    )

# One observed and independently disposed incident.
M01_IDENTITIES = {
    "plan-digest": (
        "sha256:"
        "418c68da191ec0f6b78c37144a147fce6cd6092a56575f3134e38d50fe05b09b"
    ),
    "failed-phase-digest": (
        "sha256:"
        "660f1af39737c8b4e7522dd6dc91b842eb97cc3904fdc50b574ea1425f88744d"
    ),
    "failed-request-digest": (
        "sha256:"
        "8b9292bb0df4274e92a212f8cb3a7d14dde310a4e375d26671c85c92e3d3139c"
    ),
    "failed-response-digest": (
        "sha256:"
        "6e84a967e742477092e71591fb659a20009dfd9de7aa6e840a500efa3261f1c1"
    ),
    "failed-receipt-digest": (
        "sha256:"
        "354d39a2be9d4fa43c6317bc94c655052995467732061ec8476628769d12ee99"
    ),
    "original-journal-digest": (
        "sha256:"
        "3d53cae953091234f0d2e863261150fe3beb7a45353f2b32c13d3f1a7ffa93c5"
    ),
}
M01_ORIGINAL_PHASES = (
    "initial-controls",
    "initial-inventory",
    "environment-write",
    "branch-write",
    "marker-write",
)
M01_READ_PHASES = frozenset(
    {
        "post-configuration-controls",
        "bootstrap-controls",
        "bootstrap-inventory",
        "bootstrap-main",
        "postbootstrap-controls",
        "postbootstrap-package",
        *(
            f"normal{index:02}-{role}"
            for index in range(1, 5)
            for role in ("controls", "package", "main")
        ),
    }
)


def _original(path: Path) -> bytes:
    if path.is_symlink() or not path.is_file() or path.stat().st_nlink != 1:
        msg = "M01 original or acknowledgement is missing or replaced"
        raise ValueError(msg)
    return path.read_bytes()


def _utc(value: JsonValue) -> datetime:
    result = datetime.fromisoformat(ruby_text(value))
    if result.tzinfo is None or result.utcoffset() != UTC.utcoffset(result):
        msg = "M01 acknowledgement requires a UTC timestamp"
        raise ValueError(msg)
    return result


@dataclass(frozen=True, slots=True)
class RubyConfigurationM01Continuation:
    """Bind one review; hashes do not establish independent provenance."""

    content: bytes

    def __post_init__(self) -> None:
        """Reject another incident, arbitrary scope or renewed lifetime."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            {
                "schema",
                *M01_IDENTITIES,
                "source-commit",
                "caller-digest",
                "protocol-digest",
                "reviewer",
                "author",
                "reviewed-at",
                "carrier",
                "verdict",
            },
        )
        if (
            doc["schema"]
            != "workflow-delivery/v3/ruby-configuration-m01-continuation-v1"
            or any(doc[key] != value for key, value in M01_IDENTITIES.items())
            or doc["verdict"] != "accepted"
            or not ruby_text(doc["reviewer"])
            or not ruby_text(doc["author"])
            or doc["reviewer"] == doc["author"]
            or re.fullmatch(
                r"https://github\.com/hcoona/three/issues/954#issuecomment-[1-9][0-9]*",
                ruby_text(doc["carrier"]),
            )
            is None
        ):
            msg = "M01 acknowledgement does not match the disposed incident"
            raise ValueError(msg)
        for field, pattern in (
            ("source-commit", r"[0-9a-f]{40}"),
            ("caller-digest", r"sha256:[0-9a-f]{64}"),
            ("protocol-digest", r"sha256:[0-9a-f]{64}"),
        ):
            if re.fullmatch(pattern, ruby_text(doc[field])) is None:
                msg = "M01 corrected source identity is malformed"
                raise ValueError(msg)
        _utc(doc["reviewed-at"])

    @property
    def document(self) -> dict[str, JsonValue]:
        """Read the immutable acknowledgement without extending authority."""
        return parse_canonical_json(self.content)

    @property
    def digest(self) -> str:
        """Join the acknowledgement into each new phase identity."""
        return ruby_digest(self.content)

    def require_current(
        self, plan: RubyConfigurationPlan, *, now: datetime
    ) -> None:
        """Require unchanged failed originals and original plan expiry."""
        plan.require_current(now)
        doc = self.document
        if (
            ruby_digest(plan.content) != doc["plan-digest"]
            or plan.document["destination"] != "github-packages"
        ):
            msg = "M01 continuation cannot select another plan or destination"
            raise ValueError(msg)
        path = plan.directory.with_name(
            plan.directory.name + ".m01-continuation.json"
        )
        if _original(path) != self.content:
            msg = "M01 acknowledgement was lost or changed"
            raise ValueError(msg)
        lines = _original(plan.directory / "phases.jsonl").splitlines(
            keepends=True
        )
        prefix_count = len(M01_ORIGINAL_PHASES) + 1
        if (
            len(lines) < prefix_count
            or ruby_digest(b"".join(lines[:prefix_count]))
            != doc["original-journal-digest"]
            or tuple(
                parse_canonical_json(line[:-1])["phase"]
                for line in lines[1:prefix_count]
            )
            != M01_ORIGINAL_PHASES
        ):
            msg = "M01 original phase history is missing or changed"
            raise ValueError(msg)
        failed = plan.directory / "marker-write"
        members = {
            "phase.json": "failed-phase-digest",
            "01.request.json": "failed-request-digest",
            "01.evidence.json": "failed-response-digest",
            "01.receipt.json": "failed-receipt-digest",
        }
        if failed.is_symlink() or {p.name for p in failed.iterdir()} != set(
            members
        ):
            msg = "M01 failed phase cannot be completed or reconstructed"
            raise ValueError(msg)
        for name, field in members.items():
            if ruby_digest(_original(failed / name)) != doc[field]:
                msg = "M01 failed phase evidence changed"
                raise ValueError(msg)
        originals = plan.directory.with_name(plan.directory.name + "-originals")
        if (
            originals.resolve() != originals
            or _original(originals / "plan.json") != plan.content
            or _original(originals / "marker-write" / "01.response.json")
            != _original(failed / "01.evidence.json")
        ):
            msg = "M01 retained original response lineage changed"
            raise ValueError(msg)
        receipt = parse_canonical_json(_original(failed / "01.receipt.json"))
        if (
            receipt["successful"] is not False
            or not _utc(receipt["completed-at"])
            <= _utc(doc["reviewed-at"])
            <= now
        ):
            msg = "M01 review predates the failure or changes its disposition"
            raise ValueError(msg)
