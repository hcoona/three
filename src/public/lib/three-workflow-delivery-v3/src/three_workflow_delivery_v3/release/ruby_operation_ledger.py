"""Durable sole-operator Ruby slot reservations without dispatch or retry."""

from __future__ import annotations

import os
import re
import stat
from datetime import UTC
from pathlib import Path
from typing import TYPE_CHECKING

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_OPERATION_SLOTS,
    RUBY_OPERATION_SLOTS_V2,
    RUBY_SUCCESSOR_VERSION,
    RubyOperationRequest,
    _digest,
    _time,
    validate_ruby_operation_admission,
)

if TYPE_CHECKING:
    from collections.abc import Mapping
    from datetime import datetime

_PREDECESSOR_SLOT = "github-packages-bootstrap"
_PREDECESSOR_MEMBERS = {
    "campaign.json",
    "events.jsonl",
    _PREDECESSOR_SLOT + ".json",
}


def _durable_new(path: Path, content: bytes) -> None:
    """Exclusively create and fsync; a failed partial write stays spent."""
    with path.open("xb") as stream:
        if stream.write(content) != len(content):
            message = "Ruby durable file write was partial"
            raise OSError(message)
        stream.flush()
        os.fsync(stream.fileno())
    _sync_directory(path.parent)


def _sync_directory(path: Path) -> None:
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def initialize_ruby_operation_ledger(directory: Path, campaign: str) -> bytes:
    """Create once at the independently selected POSIX operator location.

    An existing directory is never initialized or repaired. The operator must
    retain this directory for the entire campaign, including all failed slots.
    """
    if (
        not directory.is_absolute()
        or directory.resolve() != directory
        or not re.fullmatch(r"[0-9a-f]{32}", campaign)
    ):
        message = (
            "Ruby ledger needs an exact absolute path and campaign identity"
        )
        raise ValueError(message)
    directory.mkdir(mode=0o700)
    content = canonicalize(
        {
            "schema": "workflow-delivery/v3/ruby-operation-ledger-v1",
            "campaign": campaign,
            "directory": str(directory),
            "slots": list(RUBY_OPERATION_SLOTS),
        }
    )
    _durable_new(directory / "campaign.json", content)
    _durable_new(
        directory / "events.jsonl",
        canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-operation-events-v1",
                "campaign-digest": ruby_digest(content),
            }
        )
        + b"\n",
    )
    _sync_directory(directory.parent)
    return content


def _regular_bytes(path: Path) -> bytes:
    metadata = path.lstat()
    if not stat.S_ISREG(metadata.st_mode) or metadata.st_nlink != 1:
        message = "Ruby successor state is not a regular unaliased file"
        raise ValueError(message)
    return path.read_bytes()


def _predecessor(directory: Path, members: Mapping[str, str]) -> bytes:
    """Verify the admitted original single reservation without changing it."""
    if (
        not directory.is_absolute()
        or directory.resolve() != directory
        or set(members) != _PREDECESSOR_MEMBERS
        or {path.name for path in directory.iterdir()} != _PREDECESSOR_MEMBERS
    ):
        message = "Ruby predecessor directory or member inventory differs"
        raise ValueError(message)
    originals = {name: _regular_bytes(directory / name) for name in members}
    for name, content in originals.items():
        if ruby_digest(content) != _digest(members[name]):
            message = "Ruby predecessor original has changed"
            raise ValueError(message)
    header = originals["campaign.json"]
    document = ruby_object(parse_canonical_json(header))
    if (
        document.get("schema")
        != "workflow-delivery/v3/ruby-operation-ledger-v1"
    ):
        message = "Ruby successor requires its original v1 predecessor"
        raise ValueError(message)
    RubyOperationLedger(directory)
    reservation = ruby_object(
        parse_canonical_json(originals[_PREDECESSOR_SLOT + ".json"]),
        {
            "schema",
            "ledger-digest",
            "slot",
            "generation",
            "request-digest",
            "admission-digest",
            "reserved-at",
        },
    )
    if (
        reservation["schema"]
        != "workflow-delivery/v3/ruby-operation-reservation-v1"
        or reservation["slot"] != _PREDECESSOR_SLOT
        or reservation["ledger-digest"] != ruby_digest(header)
        or not re.fullmatch(
            r"[0-9a-f]{32}", ruby_text(reservation["generation"])
        )
    ):
        message = "Ruby predecessor does not retain the spent bootstrap"
        raise ValueError(message)
    _digest(reservation["request-digest"])
    _digest(reservation["admission-digest"])
    _time(reservation["reserved-at"])
    return header


def _successor_anchor(header: bytes) -> tuple[Path, bytes]:
    document = ruby_object(parse_canonical_json(header))
    predecessor = ruby_object(document["predecessor"])
    directory = Path(ruby_text(predecessor["directory"]))
    return directory.with_name(
        directory.name + ".successor.json"
    ), canonicalize(
        {
            "schema": "workflow-delivery/v3/ruby-operation-successor-anchor-v1",
            "predecessor-directory": str(directory),
            "directory": document["directory"],
            "campaign": document["campaign"],
            "campaign-digest": ruby_digest(header),
        }
    )


def initialize_ruby_successor_ledger(
    directory: Path,
    campaign: str,
    *,
    predecessor_directory: Path,
    predecessor_members: Mapping[str, str],
) -> bytes:
    """Reserve one durable successor location before creating any new state."""
    if (
        not directory.is_absolute()
        or directory.resolve() != directory
        or directory.is_relative_to(predecessor_directory)
        or not re.fullmatch(r"[0-9a-f]{32}", campaign)
    ):
        message = "Ruby successor needs a distinct exact absolute location"
        raise ValueError(message)
    if directory.exists() or directory.is_symlink():
        raise FileExistsError(directory)
    predecessor = _predecessor(predecessor_directory, predecessor_members)
    if parse_canonical_json(predecessor)["campaign"] == campaign:
        message = "Ruby successor needs a new campaign identity"
        raise ValueError(message)
    content = canonicalize(
        {
            "schema": "workflow-delivery/v3/ruby-operation-ledger-v2",
            "campaign": campaign,
            "directory": str(directory),
            "slots": list(RUBY_OPERATION_SLOTS_V2),
            "predecessor": {
                "directory": str(predecessor_directory),
                "campaign-digest": ruby_digest(predecessor),
                "members": dict(predecessor_members),
            },
        }
    )
    anchor, anchor_content = _successor_anchor(content)
    _durable_new(anchor, anchor_content)
    directory.mkdir(mode=0o700)
    _durable_new(directory / "campaign.json", content)
    _durable_new(
        directory / "events.jsonl",
        canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-operation-events-v1",
                "campaign-digest": ruby_digest(content),
            }
        )
        + b"\n",
    )
    _sync_directory(directory.parent)
    RubyOperationLedger(directory)
    return content


class RubyOperationLedger:
    """Use the exact admitted ledger; missing state cannot be reconstructed."""

    def __init__(self, directory: Path) -> None:
        """Read the existing canonical ledger without creating it."""
        if not directory.is_absolute() or directory.resolve() != directory:
            message = "Ruby ledger path is not its admitted canonical location"
            raise ValueError(message)
        self.directory = directory
        path = directory / "campaign.json"
        if path.is_symlink():
            message = "Ruby ledger identity cannot be a symbolic link"
            raise ValueError(message)
        self.content = _regular_bytes(path)
        document = ruby_object(parse_canonical_json(self.content))
        schema = document.get("schema")
        versions = {
            f"workflow-delivery/v3/ruby-operation-ledger-v{v}": v
            for v in (1, 2)
        }
        if not isinstance(schema, str) or schema not in versions:
            message = "Ruby ledger schema is unsupported"
            raise ValueError(message)
        self.version = versions[schema]
        self.slots = (
            RUBY_OPERATION_SLOTS
            if self.version == 1
            else RUBY_OPERATION_SLOTS_V2
        )
        fields = {"schema", "campaign", "directory", "slots"}
        if self.version == RUBY_SUCCESSOR_VERSION:
            fields.add("predecessor")
        ruby_object(document, fields)
        if (
            document["directory"] != str(directory)
            or document["slots"] != list(self.slots)
            or not re.fullmatch(
                r"[0-9a-f]{32}", ruby_text(document["campaign"])
            )
        ):
            message = "Ruby ledger does not describe this finite campaign"
            raise ValueError(message)
        if self.version == RUBY_SUCCESSOR_VERSION:
            self._verify_predecessor(document)
        self._verify_events()

    def _verify_predecessor(self, document: dict[str, JsonValue]) -> None:
        predecessor = ruby_object(
            document["predecessor"], {"directory", "campaign-digest", "members"}
        )
        directory = Path(ruby_text(predecessor["directory"]))
        members = {
            name: ruby_text(value)
            for name, value in ruby_object(
                predecessor["members"], _PREDECESSOR_MEMBERS
            ).items()
        }
        header = _predecessor(directory, members)
        if (
            predecessor["campaign-digest"] != ruby_digest(header)
            or parse_canonical_json(header)["campaign"] == document["campaign"]
            or self.directory.is_relative_to(directory)
        ):
            message = "Ruby successor predecessor binding differs"
            raise ValueError(message)
        anchor, expected = _successor_anchor(self.content)
        if _regular_bytes(anchor) != expected:
            message = "Ruby successor lost its original exclusive anchor"
            raise ValueError(message)

    def _verify_events(self) -> None:
        journal = self.directory / "events.jsonl"
        if journal.is_symlink() or not journal.is_file():
            message = "Ruby operation event journal is missing or replaced"
            raise ValueError(message)
        content = (
            _regular_bytes(journal)
            if self.version == RUBY_SUCCESSOR_VERSION
            else journal.read_bytes()
        )
        lines = content.splitlines(keepends=True)
        header = (
            canonicalize(
                {
                    "schema": "workflow-delivery/v3/ruby-operation-events-v1",
                    "campaign-digest": ruby_digest(self.content),
                }
            )
            + b"\n"
        )
        if not lines or lines[0] != header:
            message = "Ruby operation event journal has changed"
            raise ValueError(message)
        allowed = {
            slot + suffix
            for slot in self.slots
            if self.version == 1 or slot != _PREDECESSOR_SLOT
            for suffix in (".json", ".run.json")
        }
        allowed.update(
            {"github-packages.complete.json", "rubygems.complete.json"}
        )
        names = {"campaign.json", "events.jsonl"}
        for line in lines[1:]:
            if not line.endswith(b"\n"):
                message = "Ruby operation event reservation is partial"
                raise ValueError(message)
            event = ruby_object(
                parse_canonical_json(line[:-1]), {"name", "digest"}
            )
            name = ruby_text(event["name"])
            path = self.directory / name
            if (
                name not in allowed
                or name in names
                or path.is_symlink()
                or not path.is_file()
                or ruby_digest(path.read_bytes()) != event["digest"]
            ):
                message = "Ruby operation reserved event is missing or changed"
                raise ValueError(message)
            names.add(name)
            if self.version == RUBY_SUCCESSOR_VERSION:
                _regular_bytes(path)
        if {p.name for p in self.directory.iterdir()} != names:
            message = "Ruby operation ledger has unjoined state"
            raise ValueError(message)

    def _record(self, name: str, content: bytes) -> None:
        path = self.directory / name
        if path.exists() or path.is_symlink():
            raise FileExistsError(path)
        reservation = (
            canonicalize({"name": name, "digest": ruby_digest(content)}) + b"\n"
        )
        descriptor = os.open(
            self.directory / "events.jsonl",
            os.O_WRONLY | os.O_APPEND | os.O_NOFOLLOW,
        )
        try:
            if os.write(descriptor, reservation) != len(reservation):
                message = "Ruby operation event reservation write was partial"
                raise OSError(message)
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
        _durable_new(path, content)

    def reserve(
        self,
        request: RubyOperationRequest,
        admission: bytes,
        *,
        review: bytes,
        now: datetime,
    ) -> bytes:
        """Spend the unique slot durably before the caller may dispatch once."""
        self._require_present()
        self._require_request_version(request)
        admitted = validate_ruby_operation_admission(
            admission, request, review=review
        )
        if admitted["ledger-digest"] != ruby_digest(self.content):
            message = "Ruby independent admission selected a different ledger"
            raise ValueError(message)
        doc = request.document
        slot = ruby_text(doc["slot"])
        request.envelope.enabled_slot(slot, now)
        if not _time(admitted["reviewed-at"]) <= now < _time(doc["expires-at"]):
            message = "Ruby reservation is outside its admitted lifetime"
            raise ValueError(message)
        completion = self.directory / (
            slot.rsplit("-", 1)[0] + ".complete.json"
        )
        if completion.exists() or completion.is_symlink():
            message = "Ruby destination campaign is already complete"
            raise ValueError(message)
        reservation = canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-operation-reservation-v1",
                "ledger-digest": ruby_digest(self.content),
                "slot": slot,
                "generation": doc["generation"],
                "request-digest": request.digest,
                "admission-digest": ruby_digest(admission),
                "reserved-at": now.astimezone(UTC)
                .isoformat()
                .replace("+00:00", "Z"),
            }
        )
        self._record(slot + ".json", reservation)
        return reservation

    def join_run(
        self, request: RubyOperationRequest, reservation: bytes, run_id: int
    ) -> bytes:
        """Append one actual run without changing its reservation."""
        self._require_present()
        self._require_request_version(request)
        slot = ruby_text(request.document["slot"])
        path = self.directory / (slot + ".json")
        if path.is_symlink() or path.read_bytes() != reservation:
            message = "Ruby actual-run join lost its original reservation"
            raise ValueError(message)
        doc = ruby_object(parse_canonical_json(reservation))
        if (
            doc.get("request-digest") != request.digest
            or doc.get("ledger-digest") != ruby_digest(self.content)
            or type(run_id) is not int
            or run_id <= 0
        ):
            message = "Ruby actual-run join differs from its reserved request"
            raise ValueError(message)
        content = canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
                "request-digest": request.digest,
                "reservation-digest": ruby_digest(reservation),
                "run-id": run_id,
                "run-attempt": 1,
            }
        )
        self._record(slot + ".run.json", content)
        return content

    def stop_destination(self, destination: str, audit: JsonValue) -> None:
        """Retain independently admitted completion and prevent fresh spending.

        The reviewed caller must establish the audit's provenance and verdict.
        This persistence primitive supplies neither independent review nor a
        completion decision. Even partial writes stop subsequent reservations.
        """
        self._require_present()
        if destination not in {"github-packages", "rubygems"}:
            message = "Ruby completion selected a foreign destination"
            raise ValueError(message)
        self._record(destination + ".complete.json", canonicalize(audit))

    def _require_present(self) -> None:
        if RubyOperationLedger(self.directory).content != self.content:
            message = "Ruby ledger identity changed after admission"
            raise ValueError(message)

    def _require_request_version(self, request: RubyOperationRequest) -> None:
        slot = ruby_text(request.document["slot"])
        if (
            request.envelope.version != self.version
            or slot not in self.slots
            or (
                self.version == RUBY_SUCCESSOR_VERSION
                and slot == _PREDECESSOR_SLOT
            )
        ):
            message = "Ruby ledger version or slot is unavailable"
            raise ValueError(message)
