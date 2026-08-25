"""Strict records for the one approved Platform-Orphan exception."""

# ruff: noqa: C901, EM101, EM102, PLR0912, PLR0915, TRY003

from __future__ import annotations

import hashlib
import re
from copy import deepcopy
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import TYPE_CHECKING, cast
from uuid import UUID

from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping

    from three_workflow_delivery_v3.canonical import JsonValue

PLATFORM_ORPHAN_ACTIVE_SCHEMA = "workflow-delivery/v3/platform-orphan-exception"
PLATFORM_ORPHAN_AUDIT_SCHEMA = (
    "workflow-delivery/v3/platform-orphan-exception-audit"
)
PLATFORM_ORPHAN_RESULT_SCHEMA = (
    "workflow-delivery/v3/platform-orphan-acceptance-reconciliation-result"
)
PLATFORM_ORPHAN_AUTHORITY_PATH = (
    ".github/workflow-delivery/governance/platform-orphan-run-32809578776.json"
)
PLATFORM_ORPHAN_RESULT_PATH = (
    ".github/workflow-delivery/governance/"
    "platform-orphan-run-32809578776-result.json"
)
PLATFORM_ORPHAN_REPOSITORY = "hcoona/three"
PLATFORM_ORPHAN_REF = "refs/heads/main"
PLATFORM_ORPHAN_RUN_ID = 32809578776

_SHA1 = re.compile(r"[0-9a-f]{40}")
_SHA256 = re.compile(r"sha256:[0-9a-f]{64}")
_SHA512 = re.compile(r"sha512:[0-9a-f]{128}")
_ENCODED_PATH = re.compile(
    r"/(?:[A-Za-z0-9._~!$&'()*+,;=:@/-]|%[0-9A-Fa-f]{2})*"
)
_PRODUCER_ID = "three-workflow-delivery-v3/platform-orphan-reconcile"
_PRODUCER_ENTRY_POINT = (
    "three-workflow-delivery-v3 governance "
    "reconcile-platform-orphan-32809578776"
)
_ACCEPTANCE_TARGET_SHA = "b031e5e0bd98a95943a03a1529b64e856e1a8aa1"
_ACCEPTANCE_WORKFLOW_SHA = "953c1db0712f6ff4d41b7e6a35767d71a2b19c4d"
_PACKAGE_COORDINATE = "@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.5"
_PACKAGE_TAG = "wdv3-acceptance-5"
_PACKAGE_TARBALL_PATH = (
    "/@hcoona/hcoona-release-smoke-npm/-/"
    "hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.5.tgz"
)
_ACTIVE_AUTHORITY: dict[str, JsonValue] = {
    "schema": PLATFORM_ORPHAN_ACTIVE_SCHEMA,
    "version": 1,
    "exception": {
        "id": "cleanup-probe-run-32809578776",
        "state": "active",
    },
    "authority": {
        "repository": PLATFORM_ORPHAN_REPOSITORY,
        "ref": PLATFORM_ORPHAN_REF,
        "path": PLATFORM_ORPHAN_AUTHORITY_PATH,
        "eligible_after": "2026-09-08T04:35:59Z",
    },
    "workflow": {
        "id": 341728447,
        "path": (
            ".github/workflows/"
            "workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml"
        ),
        "state": "disabled_manually",
        "source_on_main": "absent",
    },
    "run": {
        "id": PLATFORM_ORPHAN_RUN_ID,
        "attempt": 1,
        "event": "workflow_dispatch",
        "number": 2,
        "status": "queued",
        "conclusion": None,
        "head_branch": ("workflow-delivery-v3-acceptance-retry-2-transition"),
        "head_sha": _ACCEPTANCE_WORKFLOW_SHA,
        "created_at": "2026-08-25T04:35:59Z",
        "started_at": "2026-08-25T04:35:59Z",
        "updated_at": "2026-08-25T04:35:59Z",
    },
    "execution": {
        "jobs": 0,
        "pending_deployments": 0,
        "artifacts": 0,
    },
    "environment": {
        "id": 20531285468,
        "name": ("workflow-delivery-v3-buddy-smoke-acceptance-retry-2"),
        "state": "absent",
    },
    "transition_ref": {
        "name": (
            "refs/heads/workflow-delivery-v3-acceptance-retry-2-transition"
        ),
        "state": "absent",
    },
    "recovery": {
        "cancel": {
            "method": "POST",
            "endpoint": ("/repos/hcoona/three/actions/runs/32809578776/cancel"),
            "status": 500,
        },
        "force_cancel": {
            "method": "POST",
            "endpoint": (
                "/repos/hcoona/three/actions/runs/32809578776/force-cancel"
            ),
            "status": 500,
        },
        "delete": {
            "method": "DELETE",
            "endpoint": "/repos/hcoona/three/actions/runs/32809578776",
            "status": 403,
        },
        "response_bodies_retained": False,
        "operation_timestamps_retained": False,
    },
    "historical_audit_anchors": {
        "run_sha256": (
            "sha256:"
            "549b9324f9cd4999dd5d79e7dbc75bb3746a1926920da0dafba4572caee06101"
        ),
        "jobs_sha256": (
            "sha256:"
            "b3dab5870ff3a8209160335de7c1077b89e461d875d9041ac9d8b8ed7c1666f6"
        ),
        "pending_deployments_sha256": (
            "sha256:"
            "4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945"
        ),
        "artifact_snapshot_retained": False,
    },
    "acceptance": {
        "run_id": 32805739095,
        "run_attempt": 1,
        "target_sha": _ACCEPTANCE_TARGET_SHA,
        "workflow_sha": _ACCEPTANCE_WORKFLOW_SHA,
        "result": "unsuccessful",
        "mutation_classification": "unknown",
        "review_artifact": {
            "id": 9548188898,
            "sha256": (
                "sha256:"
                "b7386651bea7c441a038c61c7d143596490a985eca33efaee7d1ede8d9701bc4"
            ),
        },
        "probe_artifact": {
            "id": 9548197128,
            "sha256": (
                "sha256:"
                "c2153d565cb1380fdf9d86fbe777fb104b6a9a4de9ecc181bbc1b84ba12ca75c"
            ),
        },
        "governance_artifact": {
            "id": 9548202666,
            "sha256": (
                "sha256:"
                "9e1aaf6701d166db0188ad7a9dce784bdaed4034e6a276545dd2a6351b3dab37"
            ),
        },
    },
    "package": {
        "coordinate": _PACKAGE_COORDINATE,
        "tag": _PACKAGE_TAG,
        "repository": PLATFORM_ORPHAN_REPOSITORY,
        "target_sha": _ACCEPTANCE_TARGET_SHA,
        "content_sha512": (
            "sha512:"
            "080c3d828a30d73d1febc3b6773015fafb529cf3a2be81fe597e83a83a589d32"
            "c1be62e933fb38ac4a77f9cb561c6399d3b2e6fe9179b3e4aed93087007140f2"
        ),
    },
}
PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256 = canonical_sha256(_ACTIVE_AUTHORITY)
_ACTIVE_AUTHORITY_BYTES = canonicalize(_ACTIVE_AUTHORITY)
_ACTIVE_AUTHORITY_BLOB_OID = hashlib.sha1(
    b"blob "
    + str(len(_ACTIVE_AUTHORITY_BYTES)).encode("ascii")
    + b"\0"
    + _ACTIVE_AUTHORITY_BYTES,
    usedforsecurity=False,
).hexdigest()


def _object(value: object, *, field: str) -> dict[str, JsonValue]:
    if type(value) is not dict:
        raise TypeError(f"{field} has the wrong runtime type")
    return cast("dict[str, JsonValue]", value)


def _closed(
    value: object,
    fields: frozenset[str],
    *,
    field: str,
) -> dict[str, JsonValue]:
    document = _object(value, field=field)
    unknown = sorted(set(document) - fields)
    if unknown:
        raise ValueError(f"{field} has unknown closed fields: {unknown}")
    missing = sorted(fields - set(document))
    if missing:
        raise ValueError(f"{field} is missing required fields: {missing}")
    return document


def _exact_type(value: object, expected: type[object], *, field: str) -> None:
    if type(value) is not expected:
        raise TypeError(f"{field} has the wrong runtime type")


def _string(value: object, *, field: str) -> str:
    _exact_type(value, str, field=field)
    result = cast("str", value)
    if not result or result != result.strip():
        raise ValueError(f"{field} must be a nonempty exact string")
    return result


def _exact(value: object, expected: object, *, field: str) -> None:
    _exact_type(value, type(expected), field=field)
    if value != expected:
        raise ValueError(f"{field} must be {expected!r}")


def _integer(value: object, *, field: str, positive: bool = False) -> int:
    _exact_type(value, int, field=field)
    result = cast("int", value)
    if positive and result <= 0:
        raise ValueError(f"{field} must be positive")
    return result


def _digest(
    value: object,
    *,
    field: str,
    sha512: bool = False,
    nullable: bool = False,
) -> str | None:
    if value is None and nullable:
        return None
    result = _string(value, field=field)
    pattern = _SHA512 if sha512 else _SHA256
    if pattern.fullmatch(result) is None:
        raise ValueError(f"{field} has an invalid digest")
    return result


def _sha(value: object, *, field: str) -> str:
    result = _string(value, field=field)
    if _SHA1.fullmatch(result) is None:
        raise ValueError(f"{field} must be a lowercase 40-character SHA")
    return result


def _instant(value: object, *, field: str) -> datetime:
    text = _string(value, field=field)
    try:
        instant = datetime.strptime(text, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=UTC
        )
    except ValueError as error:
        raise ValueError(f"{field} must be a canonical UTC instant") from error
    if instant.strftime("%Y-%m-%dT%H:%M:%SZ") != text:
        raise ValueError(f"{field} must be a canonical UTC instant")
    return instant


def _uuid(value: object, *, field: str) -> str:
    text = _string(value, field=field)
    try:
        parsed = UUID(text)
    except ValueError as error:
        raise ValueError(f"{field} must be a canonical UUID") from error
    if str(parsed) != text:
        raise ValueError(f"{field} must be a canonical UUID")
    return text


def _copy(document: Mapping[str, JsonValue]) -> dict[str, JsonValue]:
    return deepcopy(dict(document))


@dataclass(frozen=True, slots=True)
class PlatformOrphanActiveAuthority:
    """The exact active singleton authority."""

    _document: dict[str, JsonValue]

    @property
    def authority_digest(self) -> str:
        """Return the canonical active-authority content digest."""
        return canonical_sha256(self._document)

    def to_document(self) -> dict[str, JsonValue]:
        """Return a detached JSON representation."""
        return _copy(self._document)


@dataclass(frozen=True, slots=True)
class PlatformOrphanReconciliationResult:
    """A validated non-authoritative reconciliation candidate."""

    _document: dict[str, JsonValue]

    @property
    def invocation_id(self) -> str:
        """Return the candidate invocation UUID."""
        invocation = cast("dict[str, JsonValue]", self._document["invocation"])
        return cast("str", invocation["id"])

    @property
    def result_digest(self) -> str:
        """Return the digest whose preimage omits the digest member."""
        return cast("str", self._document["result_digest"])

    @property
    def content_digest(self) -> str:
        """Return the digest of the complete canonical candidate."""
        return canonical_sha256(self._document)

    def to_document(self) -> dict[str, JsonValue]:
        """Return a detached JSON representation."""
        return _copy(self._document)


@dataclass(frozen=True, slots=True)
class PlatformOrphanConsumedAudit:
    """A validated inert authority audit cross-bound to its result."""

    _document: dict[str, JsonValue]

    def to_document(self) -> dict[str, JsonValue]:
        """Return a detached JSON representation."""
        return _copy(self._document)


def admit_platform_orphan_active_authority(
    document: bytes | bytearray,
) -> PlatformOrphanActiveAuthority:
    """Admit only the exact canonical active singleton authority."""
    parsed = parse_canonical_json(document)
    if bytes(document) != canonicalize(_ACTIVE_AUTHORITY):
        raise ValueError("active Platform-Orphan authority is not exact")
    return PlatformOrphanActiveAuthority(_copy(parsed))


def _validate_request(value: object, *, index: int) -> dict[str, JsonValue]:
    field = f"requests.{index}"
    request = _closed(
        value,
        frozenset(
            {
                "sequence",
                "phase",
                "method",
                "origin",
                "path",
                "page_cursor",
                "page_index",
                "http_status",
                "complete",
            }
        ),
        field=field,
    )
    _integer(request["sequence"], field=f"{field}.sequence", positive=True)
    phase = _string(request["phase"], field=f"{field}.phase")
    if phase not in {"initial", "final"}:
        raise ValueError(f"{field}.phase has an invalid closed value")
    _exact(request["method"], "GET", field=f"{field}.method")
    origin = _string(request["origin"], field=f"{field}.origin")
    if origin not in {
        "https://api.github.com",
        "https://npm.pkg.github.com",
        "https://objects.githubusercontent.com",
        "https://github-registry-files.githubusercontent.com",
    }:
        raise ValueError(f"{field}.origin has an invalid closed value")
    path = _string(request["path"], field=f"{field}.path")
    if _ENCODED_PATH.fullmatch(path) is None:
        raise ValueError(f"{field}.path must be an absolute encoded path")
    cursor = request["page_cursor"]
    if cursor is not None:
        _string(cursor, field=f"{field}.page_cursor")
    _integer(request["page_index"], field=f"{field}.page_index", positive=True)
    _integer(request["http_status"], field=f"{field}.http_status")
    _exact_type(request["complete"], bool, field=f"{field}.complete")
    if request["complete"] is not True:
        raise ValueError(f"{field}.complete must be true")
    return request


def _request_signature(
    *,
    origin: str,
    path: str,
    status: int,
    page_index: int = 1,
    page_cursor: str | None = None,
) -> tuple[str, str, int, int, str | None]:
    return origin, path, status, page_index, page_cursor


def _expected_source_requests() -> list[tuple[str, str, int, int, str | None]]:
    api = "https://api.github.com"
    return [
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/branches/main",
            status=200,
        ),
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/git/ref/heads/main",
            status=200,
        ),
        _request_signature(
            origin=api,
            path=(
                "/repos/hcoona/three/contents/"
                ".github/workflow-delivery/governance/"
                "platform-orphan-run-32809578776.json"
            ),
            status=200,
        ),
    ]


def _expected_observation_requests(
    *,
    jobs_page_count: int,
    artifact_page_count: int,
    manifest_present: bool,
) -> list[tuple[str, str, int, int, str | None]]:
    api = "https://api.github.com"
    npm = "https://npm.pkg.github.com"
    requests = [
        _request_signature(
            origin=api,
            path=(
                "/repos/hcoona/three/contents/"
                ".github/workflows/workflow-delivery-v3-buddy-smoke-"
                "acceptance-retry-2.yml"
            ),
            status=404,
        ),
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/actions/runs/32809578776",
            status=200,
        ),
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/actions/workflows/341728447",
            status=200,
        ),
    ]
    requests.extend(
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/actions/runs/32809578776/jobs",
            status=200,
            page_index=page,
            page_cursor=f"filter=all&per_page=100&page={page}",
        )
        for page in range(1, jobs_page_count + 1)
    )
    requests.extend(
        _request_signature(
            origin=api,
            path="/repos/hcoona/three/actions/runs/32809578776/artifacts",
            status=200,
            page_index=page,
            page_cursor=f"per_page=100&page={page}",
        )
        for page in range(1, artifact_page_count + 1)
    )
    requests.extend(
        [
            _request_signature(
                origin=api,
                path=(
                    "/repos/hcoona/three/actions/runs/32809578776/"
                    "pending_deployments"
                ),
                status=200,
            ),
            _request_signature(
                origin=api,
                path=(
                    "/repos/hcoona/three/environments/"
                    "workflow-delivery-v3-buddy-smoke-acceptance-retry-2"
                ),
                status=404,
            ),
            _request_signature(
                origin=api,
                path=(
                    "/repos/hcoona/three/git/ref/heads/"
                    "workflow-delivery-v3-acceptance-retry-2-transition"
                ),
                status=404,
            ),
            _request_signature(
                origin=api,
                path="/users/hcoona/packages/npm/hcoona-release-smoke-npm",
                status=200,
            ),
            _request_signature(
                origin=npm,
                path=(
                    "/@hcoona%2Fhcoona-release-smoke-npm/"
                    "0.0.0-wdv3-acceptance.5"
                ),
                status=200 if manifest_present else 404,
            ),
            _request_signature(
                origin=npm,
                path=(
                    "/-/package/@hcoona%2Fhcoona-release-smoke-npm/dist-tags"
                ),
                status=200,
            ),
        ]
    )
    if manifest_present:
        requests.append(
            _request_signature(
                origin=npm,
                path=_PACKAGE_TARBALL_PATH,
                status=200,
            )
        )
    return requests


def _validate_request_ledger(
    requests: list[dict[str, JsonValue]],
    *,
    jobs_page_count: int,
    artifact_page_count: int,
    manifest_present: bool,
) -> None:
    if jobs_page_count + artifact_page_count > len(requests) // 2:
        raise ValueError("pagination page counts exceed request ledger")
    source = _expected_source_requests()
    observations = _expected_observation_requests(
        jobs_page_count=jobs_page_count,
        artifact_page_count=artifact_page_count,
        manifest_present=manifest_present,
    )
    ordered = (
        *(("initial", signature) for signature in source),
        *(("initial", signature) for signature in observations),
        *(("final", signature) for signature in observations),
        *(("final", signature) for signature in source),
    )
    expected = [
        (sequence, phase, "GET", *signature, True)
        for sequence, (phase, signature) in enumerate(
            ordered,
            start=1,
        )
    ]
    actual = [
        (
            request["sequence"],
            request["phase"],
            request["method"],
            request["origin"],
            request["path"],
            request["http_status"],
            request["page_index"],
            request["page_cursor"],
            request["complete"],
        )
        for request in requests
    ]
    if actual != expected:
        raise ValueError(
            "requests do not match the complete fixed observation ledger"
        )


_PLATFORM_STATE_FIELDS = frozenset(
    {
        "run_id",
        "run_attempt",
        "run_status",
        "run_conclusion",
        "run_updated_at",
        "workflow_id",
        "workflow_state",
        "job_count",
        "pending_deployment_count",
        "artifact_count",
        "workflow_source_absent",
        "environment_absent",
        "transition_ref_absent",
        "jobs_page_count",
        "artifact_page_count",
    }
)
_DESTINATION_STATE_FIELDS = frozenset(
    {
        "package_coordinate",
        "repository_association",
        "expected_tag",
        "target_sha",
        "classification",
        "manifest_version",
        "tag_projection",
        "tarball_sha512",
        "manifest_digest",
        "package_target_witness_digest",
    }
)


def _validate_platform_state(
    value: object,
    *,
    field: str,
) -> dict[str, JsonValue]:
    state = _closed(value, _PLATFORM_STATE_FIELDS, field=field)
    constants = {
        "run_id": PLATFORM_ORPHAN_RUN_ID,
        "run_attempt": 1,
        "run_status": "queued",
        "run_conclusion": None,
        "run_updated_at": "2026-08-25T04:35:59Z",
        "workflow_id": 341728447,
        "workflow_state": "disabled_manually",
        "job_count": 0,
        "pending_deployment_count": 0,
        "artifact_count": 0,
        "workflow_source_absent": True,
        "environment_absent": True,
        "transition_ref_absent": True,
    }
    for name, expected in constants.items():
        _exact(state[name], expected, field=f"{field}.{name}")
    _instant(state["run_updated_at"], field=f"{field}.run_updated_at")
    _integer(
        state["jobs_page_count"],
        field=f"{field}.jobs_page_count",
        positive=True,
    )
    _integer(
        state["artifact_page_count"],
        field=f"{field}.artifact_page_count",
        positive=True,
    )
    return state


def _validate_destination_state(
    value: object,
    *,
    field: str,
) -> dict[str, JsonValue]:
    state = _closed(value, _DESTINATION_STATE_FIELDS, field=field)
    _exact(
        state["package_coordinate"],
        _PACKAGE_COORDINATE,
        field=f"{field}.package_coordinate",
    )
    _exact(
        state["repository_association"],
        PLATFORM_ORPHAN_REPOSITORY,
        field=f"{field}.repository_association",
    )
    _exact(state["expected_tag"], _PACKAGE_TAG, field=f"{field}.expected_tag")
    _exact(
        state["target_sha"],
        _ACCEPTANCE_TARGET_SHA,
        field=f"{field}.target_sha",
    )
    classification = _string(
        state["classification"], field=f"{field}.classification"
    )
    if classification not in {"exact", "absent", "partial", "conflicting"}:
        raise ValueError(f"{field}.classification has an invalid closed value")
    for name in ("manifest_version",):
        if state[name] is not None:
            _exact(
                state[name],
                "0.0.0-wdv3-acceptance.5",
                field=f"{field}.{name}",
            )
    tag_projection = _string(
        state["tag_projection"], field=f"{field}.tag_projection"
    )
    if tag_projection not in {"match", "missing", "mismatch"}:
        raise ValueError(f"{field}.tag_projection has an invalid closed value")
    _digest(
        state["tarball_sha512"],
        field=f"{field}.tarball_sha512",
        sha512=True,
        nullable=True,
    )
    for name in ("manifest_digest", "package_target_witness_digest"):
        _digest(state[name], field=f"{field}.{name}", nullable=True)
    evidence_fields = (
        "manifest_version",
        "tarball_sha512",
        "manifest_digest",
        "package_target_witness_digest",
    )
    evidence_absent = all(state[name] is None for name in evidence_fields)
    evidence_present = all(state[name] is not None for name in evidence_fields)
    if not evidence_absent and not evidence_present:
        raise ValueError(f"{field} has incomplete manifest evidence")
    expected_content = cast(
        "dict[str, JsonValue]", _ACTIVE_AUTHORITY["package"]
    )["content_sha512"]
    if evidence_absent:
        expected_classification = (
            "absent" if tag_projection == "missing" else "conflicting"
        )
    elif state["tarball_sha512"] != expected_content:
        expected_classification = "conflicting"
    else:
        expected_classification = {
            "match": "exact",
            "missing": "partial",
            "mismatch": "conflicting",
        }[tag_projection]
    if classification != expected_classification:
        raise ValueError(
            f"{field}.classification is inconsistent with observed facts"
        )
    return state


def _validate_observations(
    value: object,
    *,
    field: str,
    state_validator: Callable[..., dict[str, JsonValue]],
) -> tuple[
    dict[str, JsonValue],
    dict[str, JsonValue],
    datetime,
    datetime,
]:
    _exact_type(value, list, field=field)
    observations = cast("list[JsonValue]", value)
    if len(observations) != len(("initial", "final")):
        raise ValueError(f"{field} must contain exactly initial and final")
    states: list[dict[str, JsonValue]] = []
    instants: list[datetime] = []
    for index, expected_phase in enumerate(("initial", "final")):
        item_field = f"{field}.{index}"
        envelope = _closed(
            observations[index],
            frozenset({"phase", "observed_at", "state", "state_sha256"}),
            field=item_field,
        )
        _exact(envelope["phase"], expected_phase, field=f"{item_field}.phase")
        instants.append(
            _instant(envelope["observed_at"], field=f"{item_field}.observed_at")
        )
        state = state_validator(envelope["state"], field=f"{item_field}.state")
        digest = _digest(
            envelope["state_sha256"], field=f"{item_field}.state_sha256"
        )
        if digest != canonical_sha256(state):
            raise ValueError(f"{item_field}.state_sha256 does not match state")
        states.append(state)
    if states[0] != states[1]:
        raise ValueError(f"{field} initial and final states differ")
    return states[0], states[1], instants[0], instants[1]


def _validate_candidate(document: dict[str, JsonValue]) -> None:
    _closed(
        document,
        frozenset(
            {
                "schema",
                "version",
                "producer",
                "invocation",
                "authority",
                "acceptance",
                "requests",
                "platform_observations",
                "destination_observations",
                "result",
                "result_digest",
            }
        ),
        field="candidate",
    )
    _exact(document["schema"], PLATFORM_ORPHAN_RESULT_SCHEMA, field="schema")
    _exact(document["version"], 1, field="version")

    producer = _closed(
        document["producer"],
        frozenset({"id", "entry_point", "repository", "ref", "control_commit"}),
        field="producer",
    )
    _exact(producer["id"], _PRODUCER_ID, field="producer.id")
    _exact(
        producer["entry_point"],
        _PRODUCER_ENTRY_POINT,
        field="producer.entry_point",
    )
    _exact(
        producer["repository"],
        PLATFORM_ORPHAN_REPOSITORY,
        field="producer.repository",
    )
    _exact(producer["ref"], PLATFORM_ORPHAN_REF, field="producer.ref")
    control_commit = _sha(
        producer["control_commit"], field="producer.control_commit"
    )

    invocation = _closed(
        document["invocation"],
        frozenset({"id", "started_at", "completed_at"}),
        field="invocation",
    )
    _uuid(invocation["id"], field="invocation.id")
    started = _instant(invocation["started_at"], field="invocation.started_at")
    completed = _instant(
        invocation["completed_at"], field="invocation.completed_at"
    )
    eligible = _instant(
        cast("dict[str, JsonValue]", _ACTIVE_AUTHORITY["authority"])[
            "eligible_after"
        ],
        field="authority.eligible_after",
    )
    if started < eligible:
        raise ValueError("invocation.started_at precedes eligible_after")
    if completed < started:
        raise ValueError("invocation.completed_at precedes started_at")

    authority = _closed(
        document["authority"],
        frozenset(
            {
                "repository",
                "ref",
                "path",
                "initial_commit",
                "initial_blob_oid",
                "initial_content_sha256",
                "final_commit",
                "final_blob_oid",
                "final_content_sha256",
                "parent_main_commit",
            }
        ),
        field="authority",
    )
    _exact(
        authority["repository"],
        PLATFORM_ORPHAN_REPOSITORY,
        field="authority.repository",
    )
    _exact(authority["ref"], PLATFORM_ORPHAN_REF, field="authority.ref")
    _exact(
        authority["path"],
        PLATFORM_ORPHAN_AUTHORITY_PATH,
        field="authority.path",
    )
    initial_commit = _sha(
        authority["initial_commit"], field="authority.initial_commit"
    )
    initial_blob = _sha(
        authority["initial_blob_oid"], field="authority.initial_blob_oid"
    )
    initial_content = _digest(
        authority["initial_content_sha256"],
        field="authority.initial_content_sha256",
    )
    for name, expected in (
        ("final_commit", initial_commit),
        ("parent_main_commit", initial_commit),
        ("final_blob_oid", initial_blob),
        ("final_content_sha256", initial_content),
    ):
        _exact(authority[name], expected, field=f"authority.{name}")
    if initial_commit != control_commit:
        raise ValueError("authority commit does not equal producer control")
    if initial_content != PLATFORM_ORPHAN_ACTIVE_AUTHORITY_SHA256:
        raise ValueError("authority content digest is not the active singleton")
    if initial_blob != _ACTIVE_AUTHORITY_BLOB_OID:
        raise ValueError("authority blob OID is not the active singleton")

    acceptance = _closed(
        document["acceptance"],
        frozenset(
            {
                "run_id",
                "run_attempt",
                "target_sha",
                "workflow_sha",
                "review_artifact_id",
                "review_artifact_sha256",
                "probe_artifact_id",
                "probe_artifact_sha256",
                "probe_record_sha256",
                "governance_artifact_id",
                "governance_artifact_sha256",
                "governance_record_sha256",
            }
        ),
        field="acceptance",
    )
    active_acceptance = cast(
        "dict[str, JsonValue]", _ACTIVE_AUTHORITY["acceptance"]
    )
    acceptance_constants = {
        "run_id": active_acceptance["run_id"],
        "run_attempt": active_acceptance["run_attempt"],
        "target_sha": active_acceptance["target_sha"],
        "workflow_sha": active_acceptance["workflow_sha"],
        "review_artifact_id": cast(
            "dict[str, JsonValue]", active_acceptance["review_artifact"]
        )["id"],
        "review_artifact_sha256": cast(
            "dict[str, JsonValue]", active_acceptance["review_artifact"]
        )["sha256"],
        "probe_artifact_id": cast(
            "dict[str, JsonValue]", active_acceptance["probe_artifact"]
        )["id"],
        "probe_artifact_sha256": cast(
            "dict[str, JsonValue]", active_acceptance["probe_artifact"]
        )["sha256"],
        "governance_artifact_id": cast(
            "dict[str, JsonValue]", active_acceptance["governance_artifact"]
        )["id"],
        "governance_artifact_sha256": cast(
            "dict[str, JsonValue]", active_acceptance["governance_artifact"]
        )["sha256"],
    }
    for name, expected in acceptance_constants.items():
        _exact(acceptance[name], expected, field=f"acceptance.{name}")
    for name in ("probe_record_sha256", "governance_record_sha256"):
        _digest(acceptance[name], field=f"acceptance.{name}")

    (
        initial_platform_state,
        _,
        initial_platform_at,
        final_platform_at,
    ) = _validate_observations(
        document["platform_observations"],
        field="platform_observations",
        state_validator=_validate_platform_state,
    )
    (
        initial_destination_state,
        _,
        initial_destination_at,
        final_destination_at,
    ) = _validate_observations(
        document["destination_observations"],
        field="destination_observations",
        state_validator=_validate_destination_state,
    )
    _exact_type(document["requests"], list, field="requests")
    requests = cast("list[JsonValue]", document["requests"])
    validated_requests = [
        _validate_request(value, index=index)
        for index, value in enumerate(requests)
    ]
    _validate_request_ledger(
        validated_requests,
        jobs_page_count=cast("int", initial_platform_state["jobs_page_count"]),
        artifact_page_count=cast(
            "int", initial_platform_state["artifact_page_count"]
        ),
        manifest_present=initial_destination_state["manifest_version"]
        is not None,
    )
    for field, initial_at, final_at in (
        (
            "platform_observations",
            initial_platform_at,
            final_platform_at,
        ),
        (
            "destination_observations",
            initial_destination_at,
            final_destination_at,
        ),
    ):
        if not started <= initial_at <= final_at <= completed:
            raise ValueError(f"{field} timestamps are outside invocation order")
    if not (
        initial_platform_at
        <= initial_destination_at
        <= final_platform_at
        <= final_destination_at
    ):
        raise ValueError(
            "observation timestamps do not preserve complete phase order"
        )

    result = _closed(
        document["result"],
        frozenset(
            {
                "terminalization_blocker_exclusion",
                "reconciliation_authority",
                "acceptance_result",
                "platform_cleanup",
                "run_terminal",
                "release_lineage",
                "package_classification",
                "package_mutation",
                "live_activation",
                "diagnostics",
            }
        ),
        field="result",
    )
    result_constants = {
        "terminalization_blocker_exclusion": ("admitted:run:32809578776"),
        "reconciliation_authority": "not-granted-by-exception",
        "acceptance_result": "unsuccessful",
        "platform_cleanup": "incomplete-with-admitted-orphan",
        "run_terminal": False,
        "release_lineage": "none",
        "package_mutation": "prohibited",
        "live_activation": "prohibited",
    }
    for name, expected in result_constants.items():
        _exact(result[name], expected, field=f"result.{name}")
    _exact(
        result["package_classification"],
        initial_destination_state["classification"],
        field="result.package_classification",
    )
    if initial_platform_state["run_id"] != PLATFORM_ORPHAN_RUN_ID:
        raise ValueError("platform state does not bind the admitted orphan")
    _exact_type(result["diagnostics"], list, field="result.diagnostics")
    diagnostics = [
        _string(value, field=f"result.diagnostics.{index}")
        for index, value in enumerate(
            cast("list[JsonValue]", result["diagnostics"])
        )
    ]
    allowed_diagnostics = {
        "platform-orphan-admitted",
        "package-absent",
        "package-partial",
        "package-conflicting",
    }
    if diagnostics != sorted(set(diagnostics)):
        raise ValueError("result.diagnostics must be sorted and unique")
    if not set(diagnostics) <= allowed_diagnostics:
        raise ValueError("result.diagnostics has an invalid closed value")

    claimed_digest = _digest(document["result_digest"], field="result_digest")
    preimage = _copy(document)
    del preimage["result_digest"]
    if claimed_digest != canonical_sha256(preimage):
        raise ValueError("result_digest does not match its canonical preimage")


def admit_platform_orphan_reconciliation_result(
    document: bytes | bytearray,
) -> PlatformOrphanReconciliationResult:
    """Admit a canonical candidate and its documented cross-bindings."""
    parsed = parse_canonical_json(document)
    _validate_candidate(parsed)
    return PlatformOrphanReconciliationResult(_copy(parsed))


def admit_platform_orphan_consumed_audit(
    document: bytes | bytearray,
    *,
    result: PlatformOrphanReconciliationResult,
) -> PlatformOrphanConsumedAudit:
    """Admit a consumed audit only when reciprocally bound to a candidate."""
    parsed = parse_canonical_json(document)
    expected_fields = frozenset(set(_ACTIVE_AUTHORITY) | {"consumption"})
    _closed(parsed, expected_fields, field="audit")

    base = _copy(parsed)
    consumption_value = base.pop("consumption")
    base["schema"] = PLATFORM_ORPHAN_ACTIVE_SCHEMA
    exception = _object(base["exception"], field="exception")
    exception["state"] = "active"
    if base != _ACTIVE_AUTHORITY:
        raise ValueError("consumed audit does not retain the active authority")

    _exact(parsed["schema"], PLATFORM_ORPHAN_AUDIT_SCHEMA, field="schema")
    audit_exception = _object(parsed["exception"], field="exception")
    _exact(audit_exception["state"], "consumed", field="exception.state")
    consumption = _closed(
        consumption_value,
        frozenset(
            {
                "invocation_id",
                "producer_control_commit",
                "active_authority_sha256",
                "result_path",
                "result_sha256",
            }
        ),
        field="consumption",
    )
    _uuid(consumption["invocation_id"], field="consumption.invocation_id")
    _sha(
        consumption["producer_control_commit"],
        field="consumption.producer_control_commit",
    )
    active_digest = _digest(
        consumption["active_authority_sha256"],
        field="consumption.active_authority_sha256",
    )
    result_digest = _digest(
        consumption["result_sha256"],
        field="consumption.result_sha256",
    )
    _exact(
        consumption["result_path"],
        PLATFORM_ORPHAN_RESULT_PATH,
        field="consumption.result_path",
    )

    candidate = result.to_document()
    _validate_candidate(candidate)
    producer = _object(candidate["producer"], field="producer")
    authority = _object(candidate["authority"], field="authority")
    invocation = _object(candidate["invocation"], field="invocation")
    cross_bindings = (
        (
            consumption["invocation_id"],
            invocation["id"],
            "consumption invocation",
        ),
        (
            consumption["producer_control_commit"],
            producer["control_commit"],
            "consumption producer control commit",
        ),
        (
            active_digest,
            authority["initial_content_sha256"],
            "consumption active authority digest",
        ),
        (
            result_digest,
            canonical_sha256(candidate),
            "consumption complete result digest",
        ),
    )
    for actual, expected, field in cross_bindings:
        if actual != expected:
            raise ValueError(f"{field} does not match the result")
    if active_digest != canonical_sha256(base):
        raise ValueError("active_authority_sha256 has the wrong preimage")
    if result_digest == candidate["result_digest"]:
        raise ValueError(
            "complete result digest must differ from result_digest"
        )
    return PlatformOrphanConsumedAudit(_copy(parsed))
