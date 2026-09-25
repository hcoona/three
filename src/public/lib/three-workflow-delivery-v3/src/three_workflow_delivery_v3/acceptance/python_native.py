"""Manual hosted entry and credential-free local Python acceptance tooling."""

from __future__ import annotations

import argparse
import os
import sys
import time
from http import HTTPStatus
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_contract import (
    WORKFLOW,
    NativeRequest,
    commit,
    load_request,
    pack_bundle,
    positive,
    require,
    unpack_bundle,
    write_exclusive,
)
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    build_fixture_set,
    fixtures_from_files,
    prepare_fixtures,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    git_output,
    prove_native_approval,
    validate_hosted,
)
from three_workflow_delivery_v3.acceptance.python_native_suite import (
    audit_suite,
    run_suite,
)
from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpResponse,
    PythonHttpsTransport,
    PythonHttpTransport,
    mint_python_token,
)
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
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

if TYPE_CHECKING:
    from collections.abc import Mapping


_MAX_GITHUB_READS = 8
_CREDENTIAL_READS = 2


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


def _reference(environment: Mapping[str, str], role: str) -> ArtifactReference:
    return artifact_reference_from_document(
        parse_json_strict(environment[f"WDV3_{role.upper()}_REFERENCE"])
    )


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


class _ProofTransport:
    def __init__(
        self,
        transport: PythonHttpTransport,
        output: Path,
        secret: str,
        deadline: float,
    ) -> None:
        self.transport = transport
        self.output = output
        self.secret = secret.encode()
        self.deadline = deadline
        self.count = 0

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        require(
            method == "GET"
            and url.startswith("https://api.github.com/repos/hcoona/three/")
            and self.count < _MAX_GITHUB_READS
            and time.monotonic() < self.deadline,
            "native GitHub proof budget exhausted",
        )
        ordinal = self.count
        self.count += 1
        response = self.transport.request(
            method, url, headers, body, maximum_bytes
        )
        require(self.secret not in response.body, "unsafe native GitHub proof")
        write_exclusive(self.output, f"github/{ordinal}.body", response.body)
        write_exclusive(
            self.output,
            f"github/{ordinal}.json",
            canonicalize(
                {
                    "url": url,
                    "status": response.status,
                    "content-type": response.content_type,
                }
            ),
        )
        return response


class _CredentialTransport:
    def __init__(
        self,
        transport: PythonHttpTransport,
        request: NativeRequest,
        deadline: float,
    ) -> None:
        self.transport = transport
        self.request_spec = request
        self.deadline = deadline
        self.count = 0

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> PythonHttpResponse:
        require(
            time.monotonic() < self.deadline
            and (
                (self.count == 0 and method == "GET")
                or (
                    self.count == 1
                    and method == "POST"
                    and url
                    == self.request_spec.registry.origin + "/_/oidc/mint-token"
                )
            ),
            "native credential budget exhausted",
        )
        self.count += 1
        return self.transport.request(method, url, headers, body, maximum_bytes)


def probe(  # noqa: PLR0913, PLR0917 - closed native input tuple
    request: NativeRequest,
    prepared: dict[str, bytes],
    reference: ArtifactReference,
    run_id: int,
    tooling_sha: str,
    output: Path,
    environment: Mapping[str, str],
) -> None:
    """Persist authority before minting and execute no target/product code."""
    _binding(prepared, request, run_id, tooling_sha)
    fixtures = fixtures_from_files(prepared)
    fixtures.match(request, run_id=run_id)
    output.mkdir(parents=True, exist_ok=False)
    deadline = time.monotonic() + 600
    try:
        write_exclusive(output, "request.json", request.content)
        write_exclusive(
            output,
            "binding.json",
            canonicalize(
                {
                    "request-digest": request.digest,
                    "prepared-reference": reference.to_document(),
                    "run-id": run_id,
                    "run-attempt": 1,
                    "tooling-sha": tooling_sha,
                    "producer": "probe-python-native",
                    "profile": request.registry.profile,
                }
            ),
        )
        write_exclusive(
            output,
            "platform.json",
            canonicalize(
                {
                    key: value
                    for key, value in environment.items()
                    if key
                    in {
                        "GITHUB_REPOSITORY",
                        "GITHUB_REPOSITORY_ID",
                        "GITHUB_ACTOR",
                        "GITHUB_ACTOR_ID",
                        "GITHUB_REF",
                        "GITHUB_REF_PROTECTED",
                        "GITHUB_RUN_ID",
                        "GITHUB_RUN_ATTEMPT",
                        "GITHUB_SHA",
                        "GITHUB_WORKFLOW_SHA",
                        "GITHUB_WORKFLOW_REF",
                        "RUNNER_OS",
                    }
                }
            ),
        )
        transport = PythonHttpsTransport()
        github_token = environment["GITHUB_TOKEN"]
        proof_transport = _ProofTransport(
            transport, output, github_token, deadline
        )
        proof = prove_native_approval(
            request,
            run_id,
            tooling_sha,
            environment["WDV3_APPROVAL_ENVIRONMENT_MARKER"],
            PythonGitHubRuntime(github_token, proof_transport),
        )
        write_exclusive(output, "approval.json", proof)
        credentials = _CredentialTransport(transport, request, deadline)
        assertion = obtain_python_oidc_assertion(
            request.registry.name, environment, credentials
        )
        token = mint_python_token(request.registry, assertion, credentials)
        require(
            credentials.count == _CREDENTIAL_READS,
            "native credential sequence differs",
        )
        write_exclusive(
            output,
            "credentials.json",
            canonicalize({"oidc-requests": 1, "token-exchanges": 1}),
        )
        run_suite(
            request,
            fixtures,
            transport,
            token,
            output / "suite",
            secrets=(
                assertion,
                github_token,
                environment["ACTIONS_ID_TOKEN_REQUEST_TOKEN"],
            ),
            deadline=deadline,
        )
    except Exception:  # noqa: BLE001 - never retain credential-bearing exceptions
        write_exclusive(
            output,
            "failure.json",
            canonicalize(
                {
                    "result": "spent-possibly-mutated",
                    "category": "native-probe-failed",
                }
            ),
        )
        msg = "Native probe stopped; generation remains spent"
        raise ValueError(msg) from None


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


def main(argv: list[str] | None = None) -> int:  # noqa: PLR0915 - closed CLI phases
    """Run only an explicitly selected local or hosted acceptance phase."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=(
            "build-fixtures",
            "prepare",
            "probe",
            "audit",
            "replay",
            "archive",
            "digest",
        ),
    )
    parser.add_argument("--root", type=Path, default=Path())
    parser.add_argument("--registry", choices=("testpypi", "pypi"))
    parser.add_argument("--request-digest")
    parser.add_argument("--tooling-sha")
    parser.add_argument("--target-a")
    parser.add_argument("--target-b")
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
            return 0
        if args.command == "archive":
            require(args.directory.is_dir(), "no surviving probe evidence")
            _write_payload(
                args.output,
                {
                    p.relative_to(args.directory).as_posix(): p.read_bytes()
                    for p in args.directory.rglob("*")
                    if p.is_file()
                },
            )
            return 0
        if args.command == "build-fixtures":
            for target in (args.target_a, args.target_b):
                git_output(
                    args.root,
                    "merge-base",
                    "--is-ancestor",
                    target,
                    "refs/remotes/origin/main",
                )
            fixtures = build_fixture_set(
                args.root, {"a": args.target_a, "b": args.target_b}, 1
            )
            _write_payload(args.output, fixtures.files())
            sys.stdout.write(
                canonicalize(
                    {
                        "fixture-digests": {
                            k: d.digest
                            for k, d in fixtures.distributions.items()
                        },
                        "targets": {
                            label: {
                                "commit": fixtures.distributions[
                                    f"{label}/original/wheel"
                                ].witness.target,
                                "version": fixtures.distributions[
                                    f"{label}/original/wheel"
                                ].witness.nbgv.pep440_version,
                            }
                            for label in ("a", "b")
                        },
                    }
                ).decode()
            )
            return 0
        if args.command == "replay":
            _local_replay(args)
            return 0
        request = load_request(args.root, args.registry, args.request_digest)
        run_id = validate_hosted(
            args.root, os.environ, args.tooling_sha, request
        )
        if args.command == "prepare":
            files = prepare_fixtures(
                args.root, request, run_id, args.tooling_sha
            )
            _write_payload(args.output, files)
            summary = os.environ.get("GITHUB_STEP_SUMMARY")
            if summary:
                with Path(summary).open("a") as stream:
                    stream.write(
                        "Python native acceptance request "
                        "(separate owner approval required)\n\n```json\n"
                        + request.content.decode()
                        + "\n```\n\nThis run permits at most 10 uploads, "
                        "27 registry reads, 8 GitHub proof reads, one OIDC "
                        "request and one exchange. Four files may remain. "
                        "No retries, reruns or cleanup. Missing evidence "
                        "leaves the generation spent and possibly mutated.\n"
                    )
        else:
            reference = _reference(os.environ, "prepared")
            prepared = read_artifact(args.prepared, reference, run_id)
            if args.command == "probe":
                probe(
                    request,
                    prepared,
                    reference,
                    run_id,
                    args.tooling_sha,
                    args.output,
                    os.environ,
                )
            else:
                probe_reference = _reference(os.environ, "probe")
                probe_files = read_artifact(args.probe, probe_reference, run_id)
                result = audit_native(
                    request,
                    prepared,
                    probe_files,
                    reference,
                    run_id,
                    args.tooling_sha,
                )
                result["lineage.json"] = canonicalize(
                    {
                        "prepared": reference.to_document(),
                        "probe": probe_reference.to_document(),
                        "run-id": run_id,
                        "tooling-sha": args.tooling_sha,
                    }
                )
                _write_payload(args.output, result)
    except Exception:  # noqa: BLE001 - never retain credential-bearing exceptions
        sys.stderr.write(
            "Python native acceptance unavailable or failed; retained evidence "
            "grants no retry or native admission.\n"
        )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
