"""Private process boundary for the retained ecosystem authority graphs."""

from __future__ import annotations

import base64
import json
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.release.static_reference_model import (
    StaticReferenceErrorKind,
    utf8_sort_key,
)

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

    from three_workflow_delivery_v3.release.static_reference_session import (
        MaterializedAuthorityInvocation,
        StaticReferenceSession,
    )
    from three_workflow_delivery_v3.release.static_reference_source import (
        StaticReferenceCandidate,
    )

_NODE_REQUEST_SCHEMA = (
    "workflow-delivery/v3/static-reference-node-authority-request"
)
_NODE_RESPONSE_SCHEMA = (
    "workflow-delivery/v3/static-reference-node-authority-response"
)
_NUGET_REQUEST_SCHEMA = (
    "workflow-delivery/v3/static-reference-nuget-authority-request"
)
_NUGET_RESPONSE_SCHEMA = (
    "workflow-delivery/v3/static-reference-nuget-authority-response"
)
_NODE_SCRIPT = Path(
    "eng/scripts/workflow_delivery_v3_static_reference_node.mjs"
)
_NUGET_DLL = Path(
    "artifacts/workflow-delivery-v3/static-reference/nuget-authority/"
    "WorkflowDeliveryV3NuGetAuthority.dll"
)
_PROCESS_TIMEOUT_SECONDS = 30
_GRAPH_ERROR_KINDS = frozenset(
    {
        "encoding-rejected",
        "authority-rejected",
        "unsupported-projection",
    }
)


class AuthorityExecutionError(RuntimeError):
    """An executable authority node did not return its private protocol."""


@dataclass(frozen=True, slots=True)
class AuthorityGraphOutcome:
    """One complete private authority-graph response."""

    graph_id: str
    implementation_identities: tuple[str, ...]
    facts: tuple[dict[str, JsonValue], ...]
    error_kind: StaticReferenceErrorKind | None = None

    def __post_init__(self) -> None:
        """Reject a noncanonical private response."""
        expected_identities = tuple(
            sorted(set(self.implementation_identities), key=utf8_sort_key)
        )
        if self.implementation_identities != expected_identities:
            message = "authority implementation identities are not canonical"
            raise ValueError(message)
        if self.error_kind is not None:
            if self.error_kind not in _GRAPH_ERROR_KINDS:
                message = "authority graph returned an invalid error kind"
                raise ValueError(message)
            if self.facts:
                message = "authority error response retained partial facts"
                raise ValueError(message)


def _exact_string(value: object, *, field: str) -> str:
    if type(value) is not str or not value:
        message = f"{field} must be an exact nonempty string"
        raise AuthorityExecutionError(message)
    return value


def _object(value: object, *, field: str) -> Mapping[str, object]:
    if not isinstance(value, dict) or any(
        type(key) is not str for key in value
    ):
        message = f"{field} must be an object"
        raise AuthorityExecutionError(message)
    return value


def _array(value: object, *, field: str) -> Sequence[object]:
    if not isinstance(value, list):
        message = f"{field} must be an array"
        raise AuthorityExecutionError(message)
    return value


def _unique_object_pairs(
    pairs: list[tuple[str, object]],
) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            message = "authority response contains duplicate fields"
            raise AuthorityExecutionError(message)
        result[key] = value
    return result


def _json_fact(value: object) -> dict[str, JsonValue]:
    fact = _object(value, field="authority fact")
    try:
        canonical = canonicalize(cast("JsonValue", fact))
        parsed = json.loads(canonical)
    except (TypeError, ValueError, json.JSONDecodeError) as error:
        message = "authority fact is not bounded JSON"
        raise AuthorityExecutionError(message) from error
    if not isinstance(parsed, dict):
        message = "authority fact must be an object"
        raise AuthorityExecutionError(message)
    return cast("dict[str, JsonValue]", parsed)


def _outcome(
    *,
    graph_id: str,
    implementation_identities: tuple[str, ...],
    facts: tuple[dict[str, JsonValue], ...],
    error_kind: StaticReferenceErrorKind | None = None,
) -> AuthorityGraphOutcome:
    try:
        return AuthorityGraphOutcome(
            graph_id=graph_id,
            implementation_identities=implementation_identities,
            facts=facts,
            error_kind=error_kind,
        )
    except ValueError as error:
        message = "authority response is not canonical"
        raise AuthorityExecutionError(message) from error


def _parse_response(
    output: bytes,
    *,
    expected_schema: str,
    expected_graph: str,
) -> AuthorityGraphOutcome:
    try:
        decoded = output.decode("utf-8", "strict")
        parsed_value = json.loads(
            decoded,
            object_pairs_hook=_unique_object_pairs,
        )
    except (
        UnicodeDecodeError,
        json.JSONDecodeError,
        AuthorityExecutionError,
    ) as error:
        message = "authority response is not valid UTF-8 JSON"
        raise AuthorityExecutionError(message) from error
    parsed = _object(parsed_value, field="authority response")
    result = _exact_string(parsed.get("result"), field="authority result")
    expected_fields = {
        "schema",
        "result",
        "graph",
        "implementationIdentities",
    }
    if result == "facts":
        expected_fields.add("facts")
    elif result == "error":
        expected_fields.add("errorKind")
    else:
        message = "authority response result is invalid"
        raise AuthorityExecutionError(message)
    if set(parsed) != expected_fields:
        message = "authority response fields are not exact"
        raise AuthorityExecutionError(message)
    if parsed["schema"] != expected_schema or parsed["graph"] != expected_graph:
        message = "authority response identity is invalid"
        raise AuthorityExecutionError(message)

    identities = tuple(
        _exact_string(value, field="implementation identity")
        for value in _array(
            parsed["implementationIdentities"],
            field="implementation identities",
        )
    )
    if result == "error":
        error_kind_value = _exact_string(
            parsed["errorKind"],
            field="authority error kind",
        )
        if error_kind_value not in _GRAPH_ERROR_KINDS:
            message = "authority response error kind is invalid"
            raise AuthorityExecutionError(message)
        return _outcome(
            graph_id=expected_graph,
            implementation_identities=identities,
            facts=(),
            error_kind=cast("StaticReferenceErrorKind", error_kind_value),
        )

    facts = tuple(
        _json_fact(value)
        for value in _array(parsed["facts"], field="authority facts")
    )
    return _outcome(
        graph_id=expected_graph,
        implementation_identities=identities,
        facts=facts,
    )


def _run_process(
    command: tuple[str, ...],
    *,
    request: dict[str, JsonValue],
    invocation: MaterializedAuthorityInvocation,
    session: StaticReferenceSession,
) -> bytes:
    environment = session.environment_for(invocation)
    try:
        completed = subprocess.run(  # noqa: S603
            command,
            cwd=invocation.root,
            check=True,
            capture_output=True,
            env=environment,
            input=canonicalize(request),
            timeout=_PROCESS_TIMEOUT_SECONDS,
        )
    except (
        OSError,
        subprocess.CalledProcessError,
        subprocess.TimeoutExpired,
    ) as error:
        message = "static-reference authority process failed"
        raise AuthorityExecutionError(message) from error
    return completed.stdout


def _node_outcome(
    repository_root: Path,
    candidate: StaticReferenceCandidate,
    invocation: MaterializedAuthorityInvocation,
    session: StaticReferenceSession,
) -> AuthorityGraphOutcome:
    environment = session.environment_for(invocation)
    executable = shutil.which("node", path=environment.get("PATH"))
    script = repository_root / _NODE_SCRIPT
    if executable is None or not script.is_file():
        message = "static-reference Node authority is not prepared"
        raise AuthorityExecutionError(message)
    if invocation.candidate_path is None:
        message = "static-reference Node authority input is not materialized"
        raise AuthorityExecutionError(message)
    request: dict[str, JsonValue] = {
        "schema": _NODE_REQUEST_SCHEMA,
        "graph": candidate.selection.graph_id,
        "snapshotRoot": str(invocation.snapshot_root),
        "candidatePath": str(invocation.candidate_path),
        "logicalPath": candidate.path,
    }
    output = _run_process(
        (executable, str(script)),
        request=request,
        invocation=invocation,
        session=session,
    )
    return _parse_response(
        output,
        expected_schema=_NODE_RESPONSE_SCHEMA,
        expected_graph=candidate.selection.graph_id,
    )


def _nuget_outcome(
    repository_root: Path,
    candidate: StaticReferenceCandidate,
    invocation: MaterializedAuthorityInvocation,
    session: StaticReferenceSession,
) -> AuthorityGraphOutcome:
    environment = session.environment_for(invocation)
    executable = shutil.which("dotnet", path=environment.get("PATH"))
    authority_dll = repository_root / _NUGET_DLL
    if executable is None or not authority_dll.is_file():
        message = "static-reference NuGet authority is not prepared"
        raise AuthorityExecutionError(message)
    request: dict[str, JsonValue] = {
        "schema": _NUGET_REQUEST_SCHEMA,
        "family": candidate.selection.family,
        "logicalPath": candidate.path,
        "contentBase64": base64.b64encode(candidate.content).decode("ascii"),
    }
    output = _run_process(
        (executable, str(authority_dll)),
        request=request,
        invocation=invocation,
        session=session,
    )
    return _parse_response(
        output,
        expected_schema=_NUGET_RESPONSE_SCHEMA,
        expected_graph=candidate.selection.graph_id,
    )


def run_authority_graph(
    repository_root: Path,
    candidate: StaticReferenceCandidate,
    invocation: MaterializedAuthorityInvocation,
    session: StaticReferenceSession,
) -> AuthorityGraphOutcome:
    """Run exactly the candidate's retained authority graph."""
    if candidate.selection.graph_id == "nuget-lock-v1":
        return _nuget_outcome(
            repository_root,
            candidate,
            invocation,
            session,
        )
    return _node_outcome(
        repository_root,
        candidate,
        invocation,
        session,
    )


__all__ = [
    "AuthorityExecutionError",
    "AuthorityGraphOutcome",
    "run_authority_graph",
]
