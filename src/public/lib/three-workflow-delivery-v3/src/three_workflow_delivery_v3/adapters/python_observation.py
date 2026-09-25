"""Finite post-upload index observation and original-response audit."""

from __future__ import annotations

import base64
import math
import re
import time
from dataclasses import dataclass
from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from packaging.utils import (
    canonicalize_name,
    parse_sdist_filename,
    parse_wheel_filename,
)
from packaging.version import Version

from three_workflow_delivery_v3.adapters.pypi import (
    HTTP_TIMEOUT_SECONDS,
    MAX_INDEX_BYTES,
    MAX_INDEX_FILES,
    POST_UPLOAD_OBSERVATION_POLICY,
    PythonHttpResponse,
    PythonRegistry,
    PythonUploadResponse,
    _https_url,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
    python_object,
    python_text,
)

if TYPE_CHECKING:
    from collections.abc import Callable

    from three_workflow_delivery_v3.adapters.pypi import PythonHttpTransport
    from three_workflow_delivery_v3.adapters.python import (
        PythonDistribution,
        PythonPackageTargetWitness,
    )

_PHASES = {
    "bootstrap-p2",
    "bootstrap-p3",
    "native-c1",
    "native-c2",
    "native-c7",
    "native-c8",
    "normal-0",
    "normal-1",
}
_MAX_READS = 6
_SPACING = 10
_WINDOW = 60


def number(value: object) -> float:
    """Reject invalid trusted-clock observations, including booleans."""
    if (
        type(value) not in {int, float}
        or not math.isfinite(cast("float", value))
        or cast("float", value) < 0
    ):
        message = "invalid Python observation clock"
        raise ValueError(message)
    return float(cast("float", value))


def response_document(response: PythonHttpResponse) -> dict[str, JsonValue]:
    """Preserve the original public response bytes."""
    return {
        "status": response.status,
        "content-type": response.content_type,
        "body": base64.b64encode(response.body).decode("ascii"),
        "digest": python_digest(response.body),
    }


def response_from_document(
    value: JsonValue, *, maximum: int = MAX_INDEX_BYTES
) -> PythonHttpResponse:
    """Read one strict bounded original-index response."""
    doc = python_object(value, {"status", "content-type", "body", "digest"})
    if type(doc["status"]) is not int:
        message = "invalid Python observation status"
        raise ValueError(message)
    if not isinstance(doc["body"], str) or not isinstance(
        doc["content-type"], str
    ):
        message = "invalid Python observation response text"
        raise ValueError(message)  # noqa: TRY004 - invalid response document
    content = base64.b64decode(doc["body"], validate=True)
    response = PythonHttpResponse(
        cast("int", doc["status"]), content, doc["content-type"]
    )
    if len(content) > maximum or response_document(response) != doc:
        message = "Python observation response binding differs"
        raise ValueError(message)
    return response


def index_inventory(  # noqa: C901 - bounded native inventory validation
    registry: PythonRegistry, response: PythonHttpResponse, version: str | None
) -> dict[str, JsonValue]:
    """Validate a complete listing, selecting only the context-owned scope."""
    if (
        response.status != HTTPStatus.OK
        or len(response.body) > MAX_INDEX_BYTES
        or response.content_type.split(";", 1)[0].strip()
        != "application/vnd.pypi.simple.v1+json"
    ):
        message = "Python observation index unavailable"
        raise ValueError(message)
    doc = parse_json_strict(response.body)
    if (
        not isinstance(doc, dict)
        or not isinstance(doc.get("meta"), dict)
        or doc["meta"].get("api-version")
        not in {"1.0", "1.1", "1.2", "1.3", "1.4"}
        or doc.get("name") != PYTHON_RELEASE_UNIT
    ):
        message = "Python observation index identity differs"
        raise ValueError(message)
    files = doc.get("files")
    if not isinstance(files, list) or len(files) > MAX_INDEX_FILES:
        message = "Python observation inventory unavailable"
        raise ValueError(message)
    inventory: dict[str, JsonValue] = {}
    seen: set[str] = set()
    for item in files:
        if not isinstance(item, dict):
            message = "invalid Python observation entry"
            raise ValueError(message)  # noqa: TRY004 - invalid index entry
        name = python_text(item.get("filename"))
        if name in seen:
            message = "duplicate Python observation entry"
            raise ValueError(message)
        seen.add(name)
        identity = (
            parse_wheel_filename(name)
            if name.endswith(".whl")
            else parse_sdist_filename(name)
        )
        if identity[0] != canonicalize_name(PYTHON_RELEASE_UNIT):
            message = "foreign Python observation filename"
            raise ValueError(message)
        if version is not None and identity[1] != Version(version):
            continue
        _https_url(
            python_text(item.get("url")),
            host=registry.file_host,
            prefix="/packages/",
        )
        hashes = item.get("hashes")
        if (
            not isinstance(hashes, dict)
            or not isinstance(hashes.get("sha256"), str)
            or re.fullmatch(r"[0-9a-f]{64}", cast("str", hashes["sha256"]))
            is None
        ):
            message = "Python observation entry lacks SHA-256"
            raise ValueError(message)
        if item.get("yanked") is not False:
            message = "Python observation inventory has yanked or unknown state"
            raise ValueError(message)
        # Retain every file-entry field; ignore top-level serial metadata.
        inventory[name] = item
    return inventory


@dataclass(frozen=True)
class ExpectedAddition:
    """An approved artifact identity used by credential-free Result replay."""

    filename: str
    digest: str
    witness: PythonPackageTargetWitness


@dataclass(frozen=True)
class ObservationBasis:
    """A successful upload and its verified preceding inventory."""

    registry: PythonRegistry
    phase: str
    addition: PythonDistribution | ExpectedAddition
    previous: PythonHttpResponse | None
    upload: PythonUploadResponse
    outer_deadline: float | None = None

    @property
    def version_scope(self) -> str | None:
        """Keep normal version and acceptance project scopes distinct."""
        return (
            self.addition.witness.nbgv.pep440_version
            if self.phase.startswith("normal-")
            else None
        )

    @property
    def deadline(self) -> float:
        """Preserve the original outer authority deadline."""
        end = number(self.upload.finished) + _WINDOW
        return (
            min(end, number(self.outer_deadline))
            if self.outer_deadline is not None
            else end
        )

    def document(self) -> dict[str, JsonValue]:
        """Bind the profile, upload, prior inventory and timing."""
        if (
            self.phase not in _PHASES
            or self.upload.classification != "definitive-success"
            or self.upload.status != HTTPStatus.OK
            or self.upload.response_digest is None
        ):
            message = "Python observation lacks eligible upload success"
            raise ValueError(message)
        if self.phase != "bootstrap-p2" and self.previous is None:
            message = "Python observation lacks verified previous index"
            raise ValueError(message)
        if self.phase == "bootstrap-p2" and self.previous is not None:
            message = "initial bootstrap observation has a foreign basis"
            raise ValueError(message)
        if (
            self.previous is not None
            and self.addition.filename
            in index_inventory(self.registry, self.previous, self.version_scope)
        ):
            message = "Python observation addition already exists"
            raise ValueError(message)
        return {
            "schema": "workflow-delivery/v3/python-index-phase-v1",
            "profile-digest": self.registry.profile_digest,
            "policy": POST_UPLOAD_OBSERVATION_POLICY,
            "phase": self.phase,
            "clock": "process-monotonic-seconds",
            "upload-response-digest": self.upload.response_digest,
            "upload-finished": number(self.upload.finished),
            "deadline": self.deadline,
            "outer-deadline": self.outer_deadline,
            "addition": {
                "filename": self.addition.filename,
                "digest": self.addition.digest,
            },
            "previous": None
            if self.previous is None
            else response_document(self.previous),
        }

    def classify(self, response: PythonHttpResponse) -> str:
        """Only the approved missing-addition states can remain pending."""
        if (
            self.phase == "bootstrap-p2"
            and response.status == HTTPStatus.NOT_FOUND
        ):
            if len(response.body) > MAX_INDEX_BYTES:
                message = "oversized bootstrap pending response"
                raise ValueError(message)
            return "pending"
        previous = (
            {}
            if self.previous is None
            else index_inventory(
                self.registry, self.previous, self.version_scope
            )
        )
        actual = index_inventory(self.registry, response, self.version_scope)
        if self.previous is not None and actual == previous:
            return "pending"
        if (
            self.addition.filename in previous
            or set(actual) != {*previous, self.addition.filename}
            or any(actual[name] != value for name, value in previous.items())
        ):
            message = "Python observation has conflicting inventory"
            raise ValueError(message)
        added = cast("dict[str, JsonValue]", actual[self.addition.filename])
        if added.get("yanked") is not False or cast("dict", added["hashes"])[
            "sha256"
        ] != self.addition.digest.removeprefix("sha256:"):
            message = "Python observation addition differs"
            raise ValueError(message)
        return "exact"


class IndexPhase:
    """One finite phase; terminal state is never resumed or reopened."""

    def __init__(self, basis: ObservationBasis) -> None:
        """Close the phase before requests and retain reached facts."""
        self.basis = basis
        self.trace = basis.document()
        self.reads: list[JsonValue] = []
        self.trace["reads"] = self.reads
        self.trace["terminal"] = "not-started"
        self.trace["stopped-at"] = number(basis.upload.finished)

    def run(  # noqa: C901, PLR0912, PLR0915 - finite observation state protocol
        self,
        transport: PythonHttpTransport,
        *,
        retain: Callable[[str, bytes], None],
        clock: Callable[[], float] = time.monotonic,
        wait: Callable[[float], None] = time.sleep,
    ) -> PythonHttpResponse:
        """Observe pending indexes with bounded waits and retention."""
        if self.trace["terminal"] != "not-started":
            message = "Python observation phase already consumed"
            raise ValueError(message)
        earliest = number(self.basis.upload.finished)
        last = earliest
        try:
            for ordinal in range(_MAX_READS):
                now = number(clock())
                if now < last:
                    message = "Python observation clock reversed"
                    raise ValueError(message)  # noqa: TRY301 - phase failure must be retained
                if now < earliest:
                    wait(earliest - now)
                    now = number(clock())
                if now < earliest or now >= self.basis.deadline:
                    self.trace["terminal"] = "exhausted"
                    message = "Python observation admission exhausted"
                    raise ValueError(message)  # noqa: TRY301 - phase failure must be retained
                entry: dict[str, JsonValue] = {
                    "ordinal": ordinal,
                    "url": self.basis.registry.index_url,
                    "admitted-at": now,
                    "start": None,
                    "finish": None,
                    "response": None,
                    "classification": "transport-failed",
                }
                self.reads.append(entry)
                last = now
                response = transport.request(
                    "GET",
                    self.basis.registry.index_url,
                    {"Accept": "application/vnd.pypi.simple.v1+json"},
                    None,
                    MAX_INDEX_BYTES,
                )
                entry.update(
                    {
                        "start": response.started,
                        "finish": response.finished,
                        "response": response_document(response),
                        "classification": "invalid",
                    }
                )
                # Retain safe original bytes even if response timing is invalid.
                retain(f"index-{ordinal}.body", response.body)
                retain(f"index-{ordinal}.response.json", canonicalize(entry))
                start, finish = (
                    number(response.started),
                    number(response.finished),
                )
                if (
                    not now <= start < self.basis.deadline
                    or not start <= finish <= start + HTTP_TIMEOUT_SECONDS
                ):
                    message = "Python observation response timing differs"
                    raise ValueError(message)  # noqa: TRY301 - phase failure must be retained
                last = finish
                classification = self.basis.classify(response)
                entry["classification"] = classification
                if classification == "exact":
                    self.trace["terminal"] = "exact"
                    return response
                earliest = finish + _SPACING
            self.trace["terminal"] = "exhausted"
            message = "Python observation read allowance exhausted"
            raise ValueError(message)  # noqa: TRY301 - phase failure must be retained
        except (OSError, TypeError, ValueError):
            if self.trace["terminal"] == "not-started":
                self.trace["terminal"] = "failed"
            raise
        except BaseException:
            self.trace["terminal"] = "cancelled"
            raise
        finally:
            valid_terminal = False
            try:
                stopped = number(clock())
                self.trace["stopped-at"] = stopped
                valid_terminal = stopped >= last
            except (TypeError, ValueError):
                self.trace["stopped-at"] = None
            if not valid_terminal:
                self.trace["terminal"] = "failed"
            retain("phase.json", canonicalize(self.trace))
            if not valid_terminal:
                message = "Python observation terminal clock differs"
                raise ValueError(message)


def replay_index_phase(
    value: JsonValue, basis: ObservationBasis
) -> PythonHttpResponse:
    """Replay every raw response without network IO or waiting."""
    doc = python_object(
        value, {*basis.document(), "reads", "terminal", "stopped-at"}
    )
    if (
        any(doc[key] != item for key, item in basis.document().items())
        or doc["terminal"] != "exact"
        or not isinstance(doc["reads"], list)
    ):
        message = "Python observation trace binding differs"
        raise ValueError(message)
    reads = doc["reads"]
    if not 1 <= len(reads) <= _MAX_READS:
        message = "Python observation read count differs"
        raise ValueError(message)
    earliest = number(basis.upload.finished)
    response = None
    finish = earliest
    for ordinal, raw in enumerate(reads):
        entry = python_object(
            raw,
            {
                "ordinal",
                "url",
                "admitted-at",
                "start",
                "finish",
                "response",
                "classification",
            },
        )
        start, finish = number(entry["start"]), number(entry["finish"])
        admitted = number(entry["admitted-at"])
        if (
            type(entry["ordinal"]) is not int
            or entry["ordinal"] != ordinal
            or entry["url"] != basis.registry.index_url
            or not earliest <= admitted <= start < basis.deadline
            or not start <= finish <= start + HTTP_TIMEOUT_SECONDS
        ):
            message = "Python observation replay timing differs"
            raise ValueError(message)
        response = response_from_document(entry["response"])
        classification = basis.classify(response)
        expected = "exact" if ordinal == len(reads) - 1 else "pending"
        if classification != expected or entry["classification"] != expected:
            message = "Python observation replay state differs"
            raise ValueError(message)
        earliest = finish + _SPACING
    if number(doc["stopped-at"]) < finish:
        message = "Python observation terminal time reversed"
        raise ValueError(message)
    if response is None:
        message = "Python observation has no final response"
        raise ValueError(message)
    return response


def replay_retained_phase(
    files: dict[str, bytes],
    prefix: str,
    basis: ObservationBasis,
    transport: PythonHttpTransport | None = None,
) -> PythonHttpResponse:
    """Bind raw phase files and, when present, the independent HTTP journal."""
    trace = parse_json_strict(files[prefix + "phase.json"])
    final = replay_index_phase(trace, basis)
    doc = cast("dict[str, JsonValue]", trace)
    expected = {prefix + "phase.json": canonicalize(doc)}
    for ordinal, raw in enumerate(cast("list[JsonValue]", doc["reads"])):
        entry = cast("dict[str, JsonValue]", raw)
        response = response_from_document(entry["response"])
        expected[prefix + f"index-{ordinal}.body"] = response.body
        expected[prefix + f"index-{ordinal}.response.json"] = canonicalize(
            {**entry, "classification": "invalid"}
        )
        if transport is not None:
            actual = transport.request(
                "GET",
                basis.registry.index_url,
                {"Accept": "application/vnd.pypi.simple.v1+json"},
                None,
                MAX_INDEX_BYTES,
            )
            if (
                response_document(actual) != entry["response"]
                or actual.started != entry["start"]
                or actual.finished != entry["finish"]
            ):
                message = "Python phase differs from original request journal"
                raise ValueError(message)
    if expected != {
        key: value for key, value in files.items() if key.startswith(prefix)
    }:
        message = "Python phase raw evidence inventory differs"
        raise ValueError(message)
    return final


def replay_failed_index_phase(
    value: JsonValue, basis: ObservationBasis
) -> None:
    """Reject success or continuation hidden inside a failed bounded phase."""
    doc = python_object(
        value, {*basis.document(), "reads", "terminal", "stopped-at"}
    )
    if (
        any(doc[key] != item for key, item in basis.document().items())
        or doc["terminal"] not in {"failed", "exhausted"}
        or not isinstance(doc["reads"], list)
        or len(doc["reads"]) > _MAX_READS
    ):
        message = "Python failed observation trace binding differs"
        raise ValueError(message)
    earliest = number(basis.upload.finished)
    last = earliest
    terminal_failure = False
    for ordinal, raw in enumerate(doc["reads"]):
        entry = python_object(
            raw,
            {
                "ordinal",
                "url",
                "admitted-at",
                "start",
                "finish",
                "response",
                "classification",
            },
        )
        admitted = number(entry["admitted-at"])
        if (
            terminal_failure
            or type(entry["ordinal"]) is not int
            or entry["ordinal"] != ordinal
            or entry["url"] != basis.registry.index_url
            or not earliest <= admitted < basis.deadline
        ):
            message = "Python failed observation request order differs"
            raise ValueError(message)
        last = admitted
        if entry["response"] is None:
            if (
                entry["classification"] != "transport-failed"
                or entry["start"] is not None
                or entry["finish"] is not None
            ):
                message = "Python failed observation transport record differs"
                raise ValueError(message)
            terminal_failure = True
            continue
        start, finish = number(entry["start"]), number(entry["finish"])
        if (
            not admitted <= start < basis.deadline
            or not start <= finish <= start + HTTP_TIMEOUT_SECONDS
        ):
            message = "Python failed observation response timing differs"
            raise ValueError(message)
        last = finish
        response = response_from_document(entry["response"])
        try:
            classification = basis.classify(response)
        except (TypeError, ValueError):
            classification = "invalid"
        if (
            classification == "exact"
            or entry["classification"] != classification
        ):
            message = "Python failed observation classification differs"
            raise ValueError(message)
        terminal_failure = classification == "invalid"
        earliest = finish + _SPACING
    stopped = number(doc["stopped-at"])
    if (
        stopped < last
        or (doc["terminal"] == "failed" and not terminal_failure)
        or (
            doc["terminal"] == "exhausted"
            and (
                terminal_failure
                or (
                    len(doc["reads"]) != _MAX_READS and stopped < basis.deadline
                )
            )
        )
    ):
        message = "Python failed observation terminal differs"
        raise ValueError(message)
