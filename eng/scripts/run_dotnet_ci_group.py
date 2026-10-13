"""Prepare the native .NET group and consume its unchanged original plan."""

from __future__ import annotations

import argparse
import json
import os
import shutil
from pathlib import Path
from typing import Any

import ci_scope
import run_node_ci_group as node

PROPERTIES = (
    "-property:Configuration=Debug",
    "-property:ContinuousIntegrationBuild=true",
    "-property:RestoreLockedMode=true",
)


def bootstrap(root: Path, directory: Path) -> Path:
    """Build control once and transfer its complete native output."""
    directory.mkdir(parents=True, exist_ok=False)
    log, application = node.build(root, directory, "candidate-control")
    transfer = directory / "transfer"
    shutil.copytree(application.parent, transfer / "control")
    shutil.copyfile(log, transfer / "candidate-control.binlog")
    node.run(
        root,
        directory,
        "python-control",
        "git",
        "archive",
        "--format=zip",
        "--output=" + str(transfer / "python-control.zip"),
        "HEAD",
        "src/private/app/workflow-delivery/Native/Python",
    )
    node.run(
        root,
        directory,
        "ruby-control",
        "git",
        "show",
        "HEAD:src/private/app/workflow-delivery/Native/Ruby/facts.rb",
    )
    (transfer / "ruby").mkdir()
    shutil.copyfile(
        directory / "ruby-control.stdout", transfer / "ruby/facts.rb"
    )
    node.write_json(
        directory / "control.json",
        {
            "application": str(application),
            "log": str(log),
            "root": str(root),
            "transfer": str(transfer),
        },
    )
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(f"application={application}\ntransfer={transfer}\n")
    return application


def materialize(
    root: Path,
    directory: Path,
    application: Path,
    native_event: tuple[str, Path, str],
) -> dict:
    """Resolve the native comparison and materialize its exact Git objects."""
    if directory.is_relative_to(root):
        message = "Control endpoints must be outside the candidate source root"
        raise ValueError(message)
    comparison = json.loads(
        node.run(
            root,
            directory.parent,
            directory.name + "-comparison",
            "dotnet",
            str(application),
            "ci",
            "comparison",
            native_event[0],
            str(native_event[1]),
            native_event[2],
            timeout=300,
        )
    )
    for name in ("basis", "candidate"):
        comparison[name] = ci_scope.git(
            root,
            "rev-parse",
            "--verify",
            "--end-of-options",
            comparison[name] + "^{commit}",
        ).strip()
    if (
        comparison["candidate"]
        != ci_scope.git(root, "rev-parse", "HEAD").strip()
    ):
        message = "Native comparison does not name the checked-out candidate"
        raise ValueError(message)
    if comparison["full"] and comparison["basis"] != comparison["candidate"]:
        message = "Explicit full must retain one exact comparison identity"
        raise ValueError(message)
    ci_scope.owner_present(root, comparison["basis"])
    if not ci_scope.owner_present(root, comparison["candidate"]):
        message = "The candidate must retain the control test owner"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    after = directory / "candidate"
    node.run(
        root,
        directory,
        "candidate-materialization",
        "git",
        "worktree",
        "add",
        "--detach",
        str(after),
        comparison["candidate"],
        timeout=300,
    )
    if comparison["basis"] == comparison["candidate"]:
        before = after
    else:
        before = directory / "basis"
        node.run(
            root,
            directory,
            "basis-materialization",
            "git",
            "worktree",
            "add",
            "--detach",
            str(before),
            comparison["basis"],
            timeout=300,
        )
    endpoints = {
        "full": comparison["full"],
        "basis": {"directory": str(before), "reference": comparison["basis"]},
        "candidate": {
            "directory": str(after),
            "reference": comparison["candidate"],
        },
    }
    (directory / "group-endpoints.json").write_text(
        json.dumps(endpoints, indent=2) + "\n", encoding="utf-8"
    )
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(f"candidate_directory={after}\n")
            stream.write(f"basis_directory={before}\n")
            separate = str(before is not None and before != after).lower()
            stream.write(f"basis_separate={separate}\n")
            stream.write(
                f"basis={comparison['basis']}\ncandidate={comparison['candidate']}\n"
            )
            stream.write(f"full={str(comparison['full']).lower()}\n")
    return endpoints


def prepare(root: Path, directory: Path, endpoints_path: Path) -> Path:
    """Prepare exact endpoints with locked native assets."""
    control = node.read_json(directory / "control.json")
    endpoints = node.read_json(endpoints_path)
    state = directory / "planning"
    state.mkdir(exist_ok=False)
    visited: set[Path] = set()
    for name in ("basis", "candidate"):
        endpoint = Path(endpoints[name]["directory"])
        if endpoint in visited:
            continue
        visited.add(endpoint)
        node.run(
            endpoint,
            state,
            name + "-runtime-tools",
            "mise",
            "install",
            "--locked",
            "dotnet",
            "node",
            "pnpm",
        )
        node.run(endpoint, state, name + "-tools", "dotnet", "tool", "restore")
        node.run(
            endpoint,
            state,
            name + "-restore",
            "dotnet",
            "restore",
            "dirs.proj",
            *PROPERTIES,
            "-binaryLogger:" + str(state / (name + "-restore.binlog")),
        )
    basis = Path(endpoints["basis"]["directory"])
    basis_log = None
    basis_control = None
    if node.owner_present(root, endpoints["basis"]["reference"]):
        if (
            endpoints["basis"]["reference"]
            == endpoints["candidate"]["reference"]
        ):
            basis_log = control["log"]
            basis_control = {
                "directory": str(root),
                "reference": endpoints["basis"]["reference"],
            }
        else:
            log, _ = node.build(basis, state, "basis-control")
            basis_log = str(log)
            basis_control = endpoints["basis"]
    files = {}
    for name in ("basis", "candidate"):
        files[name] = {
            "assembly": control["application"],
            "wrapper": str(state / (name + ".proj")),
            "request": str(state / (name + "-graph-request.json")),
            "response": str(state / (name + "-graph.json")),
            "deadlineSeconds": 900,
        }
    completion = state / "completion"
    completion.mkdir()
    request = {
        **endpoints,
        "scopePath": str(state / "preliminary-scope.json"),
        "basisFiles": files["basis"],
        "candidateFiles": files["candidate"],
        "basisControl": basis_control,
        "candidateControl": {
            "directory": str(root),
            "reference": endpoints["candidate"]["reference"],
        },
        "basisBuildLog": basis_log,
        "candidateBuildLog": control["log"],
        "completion": {
            "checkout": endpoints["candidate"]["directory"],
            "scratch": str(completion),
            "deadlineSeconds": 900,
        },
    }
    request_path = state / "request.json"
    node.write_json(request_path, request)
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(f"request={request_path}\n")
    return request_path


def finish(
    root: Path,
    preliminary: dict[str, Any],
    request_path: Path,
    application: Path,
) -> dict[str, Any]:
    """Finalize scope after C# binds both native endpoints."""
    request = node.read_json(request_path)
    state = request_path.parent
    endpoints = {
        name: Path(request[name]["directory"])
        for name in ("basis", "candidate")
    }
    joined = node.join_record_owners(root, preliminary, endpoints)
    node.write_json(Path(request["scopePath"]), joined)
    output = node.run(
        root,
        state,
        "plan",
        "dotnet",
        str(application),
        "ci",
        "plan-dotnet-group",
        str(request_path),
    )
    readback = json.loads(
        output,
        object_pairs_hook=ci_scope._unique_object,  # noqa: SLF001 - Shared receiver parser.
    )
    plan = readback["plan"]
    if (
        plan["comparison"] != preliminary["base"]
        or plan["candidate"] != preliminary["candidate"]
        or plan["scope"] != "dotnet/native-endpoint-selection-v1"
    ):
        message = "Native .NET readback has a different comparison or scope"
        raise ValueError(message)
    resources = {
        name: readback[name + "Resources"] for name in ("basis", "candidate")
    }
    for name, revision in (
        ("basis", preliminary["base"]),
        ("candidate", preliminary["candidate"]),
    ):
        ci_scope.validate_resource_endpoint(
            root, resources[name], revision, name
        )
        joined["endpoint_owners"][name]["paths"] = readback[name]
    joined["nuget_reproducibility"] = preliminary[
        "nuget_reproducibility"
    ] or any(
        path in endpoint["inputs"]
        for endpoint in resources.values()
        for path in preliminary["changed_paths"]
    )
    joined["scopes"]["dotnet"] = bool(plan["checks"])
    joined["reasons"]["dotnet"] = sorted(
        {
            f"{reason['path']} -> {reason['project']} ({reason['revision']})"
            for check in plan["checks"]
            for reason in check["reasons"]
        }
    )
    transfer = request_path.parent.parent / "transfer"
    node.write_json(transfer / "dotnet-plan.json", plan)
    node.write_json(
        transfer / "control-context.json", readback["candidateContext"]
    )
    node.write_json(state / "readback.json", readback)
    return joined


def validate_artifact(
    metadata: dict[str, Any], artifact: int, run_id: int, repository: int
) -> None:
    """Check the native artifact producer and lifetime at the second runner."""
    node.validate_scope_artifact(
        metadata, artifact, run_id, repository, name="ci-dotnet-plan"
    )


def execute(
    root: Path,
    transfer: Path,
    directory: Path,
    expected_basis: str,
    expected_candidate: str,
) -> dict[str, Any]:
    """Receive the original plan without replanning or rebuilding control."""
    plan_path = transfer / "dotnet-plan.json"
    plan = node.read_json(plan_path)
    before, after = node.comparison(
        root,
        {
            "base": plan["comparison"],
            "candidate": plan["candidate"],
            "full": plan["comparison"] == plan["candidate"],
        },
    )
    if (
        before != expected_basis
        or after != expected_candidate
        or plan["scope"] != "dotnet/native-endpoint-selection-v1"
    ):
        message = "The original native .NET plan has a different binding"
        raise ValueError(message)
    application = transfer / "control/WorkflowDelivery.dll"
    if not application.is_file():
        message = "The complete candidate control distribution is missing"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    if plan["checks"]:
        node.run(root, directory, "tools", "dotnet", "tool", "restore")
        node.run(
            root,
            directory,
            "restore",
            "dotnet",
            "restore",
            "dirs.proj",
            *PROPERTIES,
        )
    scratch = directory / "execution"
    scratch.mkdir()
    request_path = directory / "request.json"
    node.write_json(
        request_path,
        {
            "checkout": str(root),
            "scratch": str(scratch),
            "deadlineSeconds": 1200,
        },
    )
    result = node.run(
        root,
        directory,
        "execution",
        "dotnet",
        str(application),
        "ci",
        "run-dotnet",
        str(plan_path),
        str(request_path),
        required=False,
        timeout=3600,
    )
    if not result:
        message = "Native .NET execution did not emit a result"
        raise ValueError(message)
    readback = json.loads(
        result,
        object_pairs_hook=ci_scope._unique_object,  # noqa: SLF001 - Shared receiver parser.
    )
    node.write_json(directory / "result.json", readback)
    results_path = directory / "results.json"
    node.write_json(results_path, readback["results"])
    outcome = json.loads(
        node.run(
            root,
            directory,
            "collection",
            "dotnet",
            str(application),
            "ci",
            "result",
            str(plan_path),
            str(results_path),
        )
    )
    node.write_json(directory / "outcome.json", outcome)
    return outcome


def main() -> int:
    """Run finite preparation or the immutable second-runner receiver."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ci_scope.ROOT)
    commands = parser.add_subparsers(dest="command", required=True)
    first = commands.add_parser("bootstrap")
    first.add_argument("--directory", type=Path, required=True)
    materialization = commands.add_parser("materialize")
    materialization.add_argument("--directory", type=Path, required=True)
    materialization.add_argument("--application", type=Path, required=True)
    materialization.add_argument("--event-name", required=True)
    materialization.add_argument("--event-path", type=Path, required=True)
    materialization.add_argument("--candidate", required=True)
    preparation = commands.add_parser("prepare")
    preparation.add_argument("--directory", type=Path, required=True)
    preparation.add_argument("--endpoints", type=Path, required=True)
    receiver = commands.add_parser("execute")
    receiver.add_argument("--directory", type=Path, required=True)
    receiver.add_argument("--transfer", type=Path, required=True)
    receiver.add_argument("--artifact-metadata", type=Path, required=True)
    for name in ("artifact-id", "run-id", "repository-id"):
        receiver.add_argument("--" + name, type=int, required=True)
    receiver.add_argument("--basis", required=True)
    receiver.add_argument("--candidate", required=True)
    options = parser.parse_args()
    root, directory = options.repository.resolve(), options.directory.resolve()
    if (
        directory == root
        or directory.is_relative_to(root)
        or root.is_relative_to(directory)
    ):
        parser.error("native group state must be outside the source checkout")
    if options.command == "bootstrap":
        bootstrap(root, directory)
    elif options.command == "materialize":
        materialize(
            root,
            directory,
            options.application.resolve(),
            (
                options.event_name,
                options.event_path.resolve(),
                options.candidate,
            ),
        )
    elif options.command == "prepare":
        prepare(root, directory, options.endpoints.resolve())
    else:
        validate_artifact(
            node.read_json(options.artifact_metadata),
            options.artifact_id,
            options.run_id,
            options.repository_id,
        )
        execute(
            root,
            options.transfer.resolve(),
            directory,
            options.basis,
            options.candidate,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
