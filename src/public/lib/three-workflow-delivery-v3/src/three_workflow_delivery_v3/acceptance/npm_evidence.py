"""Bind a downloaded probe audit to the operator's exact run and request.

The authenticated caller uses existing gh commands to select the exact run,
require its uniquely named artifact and download it, retaining raw responses.
Caller files cannot prove their own origin: this reader relies on that caller,
Actions provenance/immutability and the protected producer's shared runtime
matcher. If artifact metadata omits workflow_run, exact-run list membership
is entirely the caller's responsibility. Archive extraction belongs to gh;
the service digest is retained, not checked against unpacked file hashes.

This is only local process evidence, never native acceptance, no-mutation
proof, approval, registry readback or a normal Release artifact reference.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import PurePosixPath
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixture,
    _witness,
    inspect_npm_fixture,
)
from three_workflow_delivery_v3.acceptance.npm_probe import (
    NPM_PROBE_RESULT_SCHEMA,
    NpmProbeRequest,
    read_request,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.release_transport import (
    _boolean,
    _closed,
    _integer,
    _object,
    _profile_match,
    _string,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.npm_process import (
        CommandClassification,
    )
    from three_workflow_delivery_v3.canonical import JsonValue
    from three_workflow_delivery_v3.records.release import ProfileMatchEvidence

_WORKFLOW = ".github/workflows/workflow-delivery-v3-native-npm-acceptance.yml"
_REPOSITORY = "hcoona/three"
_FILES = {
    "request.json",
    "fixture.tgz",
    "profile-match.json",
    "command-started",
    "result.json",
}


@dataclass(frozen=True, slots=True)
class NpmProbeEvidence:
    """Bound local facts and raw service responses, not a suite verdict."""

    run_id: int
    tooling_sha: str
    artifact_id: int
    artifact_digest: str
    artifact_url: str
    request: NpmProbeRequest
    fixture: NpmFixture
    profile_match: ProfileMatchEvidence
    process: NpmProcessOutcome
    raw_run_metadata: bytes
    raw_artifact_metadata: bytes


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _matches(
    actual: dict[str, JsonValue],
    expected: dict[str, JsonValue],
    *,
    field: str,
) -> None:
    for key, value in expected.items():
        observed = actual.get(key)
        _require(
            type(observed) is type(value) and observed == value,
            f"{field}.{key} mismatch",
        )


def _read_profile(
    path: Path, request: NpmProbeRequest, run_id: int
) -> ProfileMatchEvidence:
    match = _profile_match(parse_canonical_json(path.read_bytes()))
    profile = github_packages_destination_operation_profile()
    _require(
        match.destination_operation_profile_digest == profile.profile_digest
        and match.node_version == profile.node_version
        and match.npm_version == profile.npm_version,
        "probe current profile/toolchain mismatch",
    )
    # The producer validates effective configuration; bind its actual operands,
    # which the typed profile record alone does not constrain.
    operand_index = profile.command_template.index("{tarball-path}")
    operand = (
        match.command[operand_index]
        if len(match.command) > operand_index
        else ""
    )
    tarball = PurePosixPath(operand)
    _require(
        tarball.is_absolute()
        and ".." not in tarball.parts
        and tarball.parts[-3:]
        == (f"wdv3-native-npm-{run_id}", "runtime", "fixture.tgz"),
        "probe command fixture operand mismatch",
    )
    expected = tuple(
        {
            "{tarball-path}": operand,
            "{tag}": "buddy-sha-" + request.fixture.target,
        }.get(word, word)
        for word in profile.command_template
    )
    _require(match.command == expected, "probe command profile mismatch")
    return match


def _classification(value: JsonValue) -> CommandClassification:
    match value:
        case (
            "not-initiated"
            | "definitive-success"
            | "definitive-non-success"
            | "ambiguous"
        ):
            return value
    message = "unsupported probe command classification"
    raise ValueError(message)


def read_npm_evidence(  # noqa: PLR0913
    bundle_directory: Path,
    *,
    raw_run_metadata: bytes,
    raw_artifact_metadata: bytes,
    expected_run_id: int,
    expected_tooling_sha: str,
    expected_request: NpmProbeRequest,
    repository_root: Path,
) -> NpmProbeEvidence:
    """Read one complete audit or raise, without retries or fallback results.

    Raw metadata is the unmodified JSON response for one run and one artifact,
    not a list envelope. Service JSON may have extra fields and whitespace;
    producer JSON must be canonical and closed. The caller retains the bundle
    and raw responses beyond Actions retention as needed. repository_root must
    supply the trusted locked official npm parser, not downloaded target code.
    """
    _require(
        type(expected_run_id) is int and expected_run_id > 0,
        "expected run ID must be a positive integer",
    )
    _require(
        type(expected_tooling_sha) is str
        and re.fullmatch(r"[0-9a-f]{40}", expected_tooling_sha) is not None,
        "expected tooling revision must be a full lowercase SHA",
    )
    _require(
        type(expected_request) is NpmProbeRequest,
        "expected request must be a parsed npm probe request",
    )
    _require(
        type(raw_run_metadata) is bytes
        and type(raw_artifact_metadata) is bytes,
        "raw service metadata must be immutable response bytes",
    )
    run = _object(parse_json_strict(raw_run_metadata), field="run metadata")
    artifact = _object(
        parse_json_strict(raw_artifact_metadata), field="artifact metadata"
    )
    _matches(
        run,
        {
            "id": expected_run_id,
            "run_attempt": 1,
            "head_sha": expected_tooling_sha,
            "head_branch": "main",
            "path": _WORKFLOW,
            "event": "workflow_dispatch",
            "status": "completed",
        },
        field="run",
    )
    _matches(
        _object(run.get("actor"), field="actor"), {"id": 712433}, field="actor"
    )
    _matches(
        _object(run.get("repository"), field="repository"),
        {"full_name": _REPOSITORY},
        field="repository",
    )
    _matches(
        artifact,
        {"name": f"wdv3-native-npm-probe-{expected_run_id}", "expired": False},
        field="artifact",
    )
    if "workflow_run" in artifact:
        _matches(
            _object(artifact["workflow_run"], field="artifact.workflow_run"),
            {"id": expected_run_id, "head_sha": expected_tooling_sha},
            field="artifact.workflow_run",
        )
    artifact_id = _integer(artifact.get("id"), field="artifact.id")
    artifact_digest = _string(artifact.get("digest"), field="artifact.digest")
    artifact_url = _string(artifact.get("url"), field="artifact.url")
    _require(artifact_id > 0, "artifact ID must be positive")
    _require(
        re.fullmatch(r"sha256:[0-9a-f]{64}", artifact_digest) is not None,
        "artifact service digest must be sha256",
    )
    _require(
        artifact_url
        == f"https://api.github.com/repos/{_REPOSITORY}/actions/artifacts/{artifact_id}",
        "artifact service URL mismatch",
    )
    evidence = bundle_directory / "evidence"
    _require(
        {path.name for path in bundle_directory.iterdir()}
        == {"platform.json", "evidence"}
        and {path.name for path in evidence.iterdir()} == _FILES,
        "incomplete or unexpected probe artifact files",
    )
    platform = parse_canonical_json(
        (bundle_directory / "platform.json").read_bytes()
    )
    _require(
        platform
        == {
            "schema": "workflow-delivery-v3/native-npm-actions-context/v1",
            "run_id": str(expected_run_id),
            "run_attempt": "1",
            "sha": expected_tooling_sha,
            "ref": "refs/heads/main",
            "actor_id": "712433",
            "repository": _REPOSITORY,
            "event_name": "workflow_dispatch",
            "workflow_ref": f"{_REPOSITORY}/{_WORKFLOW}@refs/heads/main",
        },
        "probe platform context mismatch",
    )
    _require(
        (evidence / "command-started").read_bytes() == b"",
        "probe command-started must be present and empty",
    )
    request = read_request(evidence / "request.json")
    _require(request == expected_request, "probe expected request mismatch")
    tarball = (evidence / "fixture.tgz").read_bytes()
    content = inspect_npm_fixture(tarball, repository_root=repository_root)
    _require(
        content.witness == canonicalize(_witness(request.fixture)),
        "probe actual fixture/request mismatch",
    )
    match = _read_profile(
        evidence / "profile-match.json", request, expected_run_id
    )
    result = _closed(
        parse_canonical_json((evidence / "result.json").read_bytes()),
        field="probe result",
        schema=NPM_PROBE_RESULT_SCHEMA,
        fields=frozenset(
            {
                "request_digest",
                "profile_match_digest",
                "fixture_content",
                "command_classification",
                "returncode",
                "truncated",
            }
        ),
    )
    _matches(
        result,
        {
            "request_digest": canonical_sha256(request.to_document()),
            "profile_match_digest": match.match_digest,
            "fixture_content": content.to_document(),
        },
        field="probe result",
    )
    process = NpmProcessOutcome(
        classification=_classification(result["command_classification"]),
        returncode=(
            None
            if result["returncode"] is None
            else _integer(result["returncode"], field="returncode")
        ),
        truncated=_boolean(result["truncated"], field="truncated"),
    )
    _matches(
        run,
        {
            "conclusion": (
                "success"
                if process.classification == "definitive-success"
                else "failure"
            )
        },
        field="run",
    )
    return NpmProbeEvidence(
        run_id=expected_run_id,
        tooling_sha=expected_tooling_sha,
        artifact_id=artifact_id,
        artifact_digest=artifact_digest,
        artifact_url=artifact_url,
        request=request,
        fixture=NpmFixture(tarball, content),
        profile_match=match,
        process=process,
        raw_run_metadata=raw_run_metadata,
        raw_artifact_metadata=raw_artifact_metadata,
    )
