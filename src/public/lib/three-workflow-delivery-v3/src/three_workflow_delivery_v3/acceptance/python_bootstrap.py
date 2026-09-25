"""Explicit local and hosted entry points for the closed TestPyPI bootstrap."""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    WORKFLOW,
    BootstrapRequest,
    load_bootstrap_request,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_fixture import (
    build_bootstrap_fixture,
    fixtures_from_files,
    prepare_bootstrap,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_suite import (
    BootstrapContext,
    audit,
    authorize,
    execute,
    marker,
    replay_audit,
)
from three_workflow_delivery_v3.acceptance.python_native import read_artifact
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    commit,
    pack_bundle,
    positive,
    require,
    write_exclusive,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    git_output,
    validate_hosted_targets,
)
from three_workflow_delivery_v3.adapters.pypi import PythonHttpsTransport
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
from three_workflow_delivery_v3.repository.python_provider import (
    python_digest,
    python_object,
    python_text,
)

_INPUTS = {
    "prepare": (),
    "authorize": ("prepared",),
    "marker": ("prepared", "authorization"),
    "execute": ("prepared", "authorization", "marker"),
    "audit": ("prepared", "authorization", "marker", "result"),
}
_REPLAY_ROLES = (*_INPUTS["audit"], "audit")


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    for command, roles in _INPUTS.items():
        phase = commands.add_parser(command)
        phase.add_argument("--root", type=Path, default=Path())
        phase.add_argument("--request-digest", required=True)
        phase.add_argument("--tooling-sha", required=True)
        phase.add_argument("--output", type=Path, required=True)
        for role in roles:
            phase.add_argument(f"--{role}", type=Path, required=True)
    build = commands.add_parser("build-fixtures")
    build.add_argument("--root", type=Path, default=Path())
    build.add_argument("--target", required=True)
    build.add_argument("--output", type=Path, required=True)
    archive = commands.add_parser("archive")
    archive.add_argument("--directory", type=Path, required=True)
    archive.add_argument("--output", type=Path, required=True)
    digest_parser = commands.add_parser("digest")
    digest_parser.add_argument("--output", type=Path, required=True)
    replay = commands.add_parser("replay")
    for role in _REPLAY_ROLES:
        replay.add_argument(f"--{role}", type=Path, required=True)
    replay.add_argument("--references", type=Path, required=True)
    replay.add_argument("--output", type=Path, required=True)
    return parser


def _write_payload(path: Path, files: dict[str, bytes]) -> None:
    write_exclusive(path.parent, path.name, pack_bundle(files))


def _archive(args: argparse.Namespace) -> None:
    require(args.directory.is_dir(), "no surviving bootstrap evidence")
    files = {}
    for path in args.directory.rglob("*"):
        require(not path.is_symlink(), "bootstrap archive contains a symlink")
        if path.is_file():
            files[path.relative_to(args.directory).as_posix()] = (
                path.read_bytes()
            )
    _write_payload(args.output, files)


def _digest(path: Path) -> None:
    value = python_digest(path.read_bytes())
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a") as stream:
            stream.write(f"digest={value}\n")
    sys.stdout.write(value + "\n")


def _build(args: argparse.Namespace) -> None:
    target = commit(args.target)
    git_output(
        args.root,
        "merge-base",
        "--is-ancestor",
        target,
        "refs/remotes/origin/main",
    )
    fixtures = build_bootstrap_fixture(args.root, target, 1)
    _write_payload(args.output, fixtures.files())
    source = fixtures.distributions["wheel"].witness
    summary: dict[str, JsonValue] = {
        "source": {"commit": target, "version": source.nbgv.pep440_version},
        "fixture-digests": {
            name: item.digest for name, item in fixtures.distributions.items()
        },
        "filenames": {
            name: item.filename for name, item in fixtures.distributions.items()
        },
    }
    sys.stdout.write(canonicalize(summary).decode() + "\n")


def _summary(
    request: BootstrapRequest, prepared: dict[str, bytes], tooling: str
) -> None:
    if output := os.environ.get("GITHUB_STEP_SUMMARY"):
        fixtures = fixtures_from_files(prepared)
        facts: dict[str, JsonValue] = {
            "repository": "hcoona/three",
            "repository-id": 1102295886,
            "oidc-audience": "testpypi",
            "request": request.document,
            "request-digest": request.digest,
            "tooling-sha": tooling,
            "originals": {
                name: {"filename": item.filename, "digest": item.digest}
                for name, item in fixtures.distributions.items()
            },
        }
        with Path(output).open("a") as stream:
            stream.write(
                "TestPyPI first-project bootstrap "
                "(separate owner grant required)\n\n"
                "```json\n" + canonicalize(facts).decode() + "\n```\n\n"
                "At most 2 uploads, 5 index reads, 5 file reads, "
                "16 GitHub proof reads, 1 OIDC assertion, 1 exchange "
                "and 5 immutable artifacts. "
                "Authorization and marker readback precede credentials. "
                "The shared authorization window is 600 seconds. Partial or "
                "ambiguous success stops the generation; no retry, rerun, "
                "refill, rollback or cleanup. Missing execution evidence after "
                "the marker means unknown/possibly-mutated. Bootstrap evidence "
                "does not establish account ownership or native admission.\n"
            )


def _hosted(args: argparse.Namespace) -> None:
    request = load_bootstrap_request(args.root, args.request_digest)
    run_id = validate_hosted_targets(
        args.root,
        os.environ,
        args.tooling_sha,
        (python_text(request.source["commit"]),),
        WORKFLOW,
    )
    if args.command == "prepare":
        files = prepare_bootstrap(args.root, request, run_id, args.tooling_sha)
        _write_payload(args.output, files)
        _summary(request, files, args.tooling_sha)
        return
    references: dict[str, ArtifactReference] = {}
    bundles = {}
    for role in _INPUTS[args.command]:
        reference = artifact_reference_from_document(
            parse_json_strict(os.environ[f"WDV3_{role.upper()}_REFERENCE"])
        )
        bundles[role] = read_artifact(getattr(args, role), reference, run_id)
        references[role] = reference
    context = BootstrapContext(request, run_id, args.tooling_sha, references)
    if args.command == "authorize":
        authorize(
            context,
            bundles["prepared"],
            args.output,
            PythonHttpsTransport(),
            os.environ,
        )
    elif args.command == "marker":
        _write_payload(
            args.output,
            marker(context, bundles["prepared"], bundles["authorization"]),
        )
    elif args.command == "execute":
        execute(
            context,
            bundles["prepared"],
            bundles["authorization"],
            bundles["marker"],
            args.output,
            PythonHttpsTransport(),
            os.environ,
        )
    else:
        audit(
            context,
            bundles["prepared"],
            bundles["authorization"],
            bundles["marker"],
            bundles["result"],
            args.output,
            PythonHttpsTransport(),
        )


def _replay(args: argparse.Namespace) -> None:
    lineage = python_object(
        parse_canonical_json(args.references.read_bytes()),
        {"run-id", "tooling-sha", *_REPLAY_ROLES},
    )
    run_id = positive(lineage["run-id"])
    tooling = commit(lineage["tooling-sha"])
    references = {
        role: artifact_reference_from_document(lineage[role])
        for role in _REPLAY_ROLES
    }
    bundles = {
        role: read_artifact(getattr(args, role), references[role], run_id)
        for role in _REPLAY_ROLES
    }
    request = BootstrapRequest(bundles["prepared"]["request.json"])
    context = BootstrapContext(request, run_id, tooling, references)
    result = replay_audit(
        context,
        bundles["prepared"],
        bundles["authorization"],
        bundles["marker"],
        bundles["result"],
        bundles["audit"],
    )
    result["lineage.json"] = canonicalize(lineage)
    _write_payload(args.output, result)


def main(argv: list[str] | None = None) -> int:
    """Run one explicit phase; never dispatch, discover artifacts or recover."""
    args = _parser().parse_args(argv)
    try:
        if args.command == "digest":
            _digest(args.output)
        else:
            require(not args.output.exists(), "bootstrap output already exists")
            if args.command == "archive":
                _archive(args)
            elif args.command == "build-fixtures":
                _build(args)
            elif args.command == "replay":
                _replay(args)
            else:
                _hosted(args)
    except Exception:  # noqa: BLE001 - do not expose credential-bearing exceptions
        sys.stderr.write(
            "Bootstrap phase failed; retained evidence grants no retry, "
            "account ownership, native admission or publication authority.\n"
        )
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
