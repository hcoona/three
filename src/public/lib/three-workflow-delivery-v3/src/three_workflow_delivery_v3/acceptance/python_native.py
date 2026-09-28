"""Local readers for retained Python native acceptance evidence."""

from __future__ import annotations

import argparse
import os
import sys
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_contract import (
    WORKFLOW,
    NativeRequest,
    commit,
    pack_bundle,
    positive,
    require,
    unpack_bundle,
    write_exclusive,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    fixtures_from_files,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    prove_native_approval,
)
from three_workflow_delivery_v3.acceptance.python_native_suite import (
    audit_suite,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

if TYPE_CHECKING:
    from three_workflow_delivery_v3.platform.python_github import (
        PythonGitHubRuntime,
    )

_MAX_GITHUB_READS = 8


def read_artifact(
    path: Path, reference: ArtifactReference, run_id: int
) -> dict[str, bytes]:
    """Verify both raw artifact-service and logical payload digest bindings."""
    content = path.read_bytes()
    require(
        path.name == reference.payload_path
        and python_digest(content) == reference.payload_digest
        and python_digest(content) == reference.artifact_digest
        and reference.artifact_url
        == f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/{reference.artifact_id}",
        "native artifact transport or payload binding differs",
    )
    return unpack_bundle(content)


def _binding(
    files: dict[str, bytes],
    request: NativeRequest,
    run_id: int,
    tooling_sha: str,
) -> None:
    require(
        files["request.json"] == request.content
        and parse_canonical_json(files["binding.json"])
        == {
            "request-digest": request.digest,
            "tooling-sha": tooling_sha,
            "run-id": run_id,
            "run-attempt": 1,
            "producer": "prepare-python-native",
        },
        "prepared current-run binding differs",
    )


def audit_native(  # noqa: PLR0913, PLR0917 - same supplied-fact tuple
    request: NativeRequest,
    prepared: dict[str, bytes],
    probe_files: dict[str, bytes],
    prepared_reference: ArtifactReference,
    run_id: int,
    tooling_sha: str,
) -> dict[str, bytes]:
    """Replay original GitHub/registry facts without native registry effects."""
    _binding(prepared, request, run_id, tooling_sha)
    require(
        "failure.json" not in probe_files
        and probe_files["request.json"] == request.content,
        "incomplete or foreign native probe",
    )
    require(
        parse_canonical_json(probe_files["binding.json"])
        == {
            "request-digest": request.digest,
            "prepared-reference": prepared_reference.to_document(),
            "run-id": run_id,
            "run-attempt": 1,
            "tooling-sha": tooling_sha,
            "producer": "probe-python-native",
            "profile": request.registry.profile,
        },
        "native probe binding differs",
    )
    require(
        parse_canonical_json(probe_files["platform.json"])
        == {
            "GITHUB_REPOSITORY": "hcoona/three",
            "GITHUB_REPOSITORY_ID": "1102295886",
            "GITHUB_ACTOR": "hcoona",
            "GITHUB_ACTOR_ID": "712433",
            "GITHUB_REF": "refs/heads/main",
            "GITHUB_REF_PROTECTED": "true",
            "GITHUB_RUN_ID": str(run_id),
            "GITHUB_RUN_ATTEMPT": "1",
            "GITHUB_SHA": tooling_sha,
            "GITHUB_WORKFLOW_SHA": tooling_sha,
            "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW}@refs/heads/main",
            "RUNNER_OS": "Linux",
        },
        "native platform evidence differs",
    )

    class ProofReplay:
        count = 0

        def get(self, route: str) -> JsonValue:
            require(
                self.count < _MAX_GITHUB_READS,
                "native proof replay budget exhausted",
            )
            metadata = parse_canonical_json(
                probe_files[f"github/{self.count}.json"]
            )
            content = probe_files[f"github/{self.count}.body"]
            self.count += 1
            require(
                metadata["url"] == "https://api.github.com" + route
                and metadata["status"] == HTTPStatus.OK,
                "native proof source differs",
            )
            return parse_json_strict(content)

    require(
        parse_canonical_json(probe_files["credentials.json"])
        == {"oidc-requests": 1, "token-exchanges": 1},
        "incomplete native credential accounting",
    )
    replay = ProofReplay()
    config = cast("dict[str, JsonValue]", request.document["environment"])
    approval = prove_native_approval(
        request,
        run_id,
        tooling_sha,
        cast("str", config["sentinel"]),
        cast("PythonGitHubRuntime", replay),
    )
    require(
        approval == probe_files["approval.json"]
        and len([k for k in probe_files if k.startswith("github/")])
        == replay.count * 2,
        "native proof inventory differs",
    )
    fixtures = fixtures_from_files(prepared)
    fixtures.match(request, run_id=run_id)
    result = audit_suite(
        request,
        fixtures,
        {
            k.removeprefix("suite/"): v
            for k, v in probe_files.items()
            if k.startswith("suite/")
        },
    )
    result["approval.json"] = approval
    return result


def _write_payload(path: Path, files: dict[str, bytes]) -> None:
    write_exclusive(path.parent, path.name, pack_bundle(files))


def _local_replay(args: argparse.Namespace) -> None:
    """Audit explicit immutable inputs without a hosted credential context."""
    lineage = parse_canonical_json(args.references.read_bytes())
    run_id = positive(lineage["run-id"])
    tooling_sha = commit(lineage["tooling-sha"])
    prepared_reference = artifact_reference_from_document(lineage["prepared"])
    probe_reference = artifact_reference_from_document(lineage["probe"])
    prepared = read_artifact(args.prepared, prepared_reference, run_id)
    probe_files = read_artifact(args.probe, probe_reference, run_id)
    request = NativeRequest(prepared["request.json"])
    result = audit_native(
        request,
        prepared,
        probe_files,
        prepared_reference,
        run_id,
        tooling_sha,
    )
    result["lineage.json"] = canonicalize(lineage)
    _write_payload(args.output, result)


def main(argv: list[str] | None = None) -> int:
    """Read retained historical evidence without a hosted execution route."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("replay", "archive", "digest"))
    parser.add_argument("--prepared", type=Path)
    parser.add_argument("--probe", type=Path)
    parser.add_argument("--directory", type=Path)
    parser.add_argument("--references", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == "digest":
            with Path(os.environ["GITHUB_OUTPUT"]).open("a") as stream:
                stream.write(
                    "digest=" + python_digest(args.output.read_bytes()) + "\n"
                )
        elif args.command == "archive":
            require(args.directory.is_dir(), "no surviving probe evidence")
            _write_payload(
                args.output,
                {
                    p.relative_to(args.directory).as_posix(): p.read_bytes()
                    for p in args.directory.rglob("*")
                    if p.is_file()
                },
            )
        else:
            _local_replay(args)
    except Exception:  # noqa: BLE001 - preserve historical sanitized failure
        sys.stderr.write(
            "Python native evidence unavailable or failed; retained evidence "
            "grants no retry or native admission.\n"
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
