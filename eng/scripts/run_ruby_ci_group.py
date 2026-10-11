"""Receive, prepare and execute the finite native Ruby CI group."""

from __future__ import annotations

import argparse
import json
import os
import shutil
from pathlib import Path
from typing import Any

import ci_scope
import run_dotnet_ci_group as dotnet
import run_node_ci_group as node

NATIVE_SOURCE = "src/private/app/workflow-delivery/Native/Ruby/facts.rb"
SCOPE = "ruby/native-paired-facts-v1"


def executable(name: str) -> str:
    """Preserve native executable identity with absolute path spelling."""
    value = shutil.which(name)
    if value is None:
        message = f"Required native executable is unavailable: {name}"
        raise FileNotFoundError(message)
    return os.path.abspath(value)  # noqa: PTH100 - Preserve shim identity.


def materialize(
    root: Path, scope_path: Path, transfer: Path, directory: Path
) -> dict[str, Any]:
    """Create exact committed endpoints before native query effects."""
    scope = node.read_json(scope_path)
    before, after = node.comparison(root, scope)
    application = transfer / "control/WorkflowDelivery.dll"
    helper = transfer / "ruby/facts.rb"
    if not application.is_file() or not helper.is_file():
        message = "The complete candidate Ruby control transfer is missing"
        raise ValueError(message)
    if (
        directory == root
        or directory.is_relative_to(root)
        or root.is_relative_to(directory)
    ):
        message = "Ruby group state must be outside the source checkout"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    endpoints = {}
    for name, revision in (("basis", before), ("candidate", after)):
        endpoint = directory / name
        node.run(
            root,
            directory,
            name + "-checkout",
            "git",
            "worktree",
            "add",
            "--detach",
            str(endpoint),
            revision,
        )
        endpoints[name] = {"directory": str(endpoint), "reference": revision}
    context = endpoints | {
        "full": scope["full"],
        "scopePath": str(scope_path),
        "application": str(application),
        "helper": str(helper),
    }
    node.write_json(directory / "context.json", context)
    return context


def plan(root: Path, directory: Path) -> dict[str, Any]:
    """Prepare once and retain both endpoints with the original plan."""
    context = node.read_json(directory / "context.json")
    scratch = directory / "query-preparation"
    scratch.mkdir()
    request = {
        name: context[name]
        for name in ("basis", "candidate", "full", "scopePath")
    } | {
        "helper": context["helper"],
        "scratch": str(scratch),
        "tools": {
            name: executable(name)
            for name in ("ruby", "gem", "bundle", "dotnet")
        },
        "bundlerProbeVersion": "2.7.2",
        "deadlineSeconds": 900,
    }
    request_path = directory / "preparation-request.json"
    node.write_json(request_path, request)
    preparation = invoke(
        root,
        directory,
        context,
        "preparation",
        "prepare-ruby-group",
        request_path,
    )
    node.write_json(directory / "preparation.json", preparation)
    group_request = directory / "group-request.json"
    node.write_json(group_request, preparation["request"])
    readback = invoke(
        root, directory, context, "planning", "plan-ruby-group", group_request
    )
    validate_plan(readback["plan"], context)
    for name in ("basis", "candidate"):
        if (
            readback[name]["facts"]["revision"]["commit"]
            != context[name]["reference"]
        ):
            message = "Ruby collection returned a different endpoint"
            raise ValueError(message)
    node.write_json(
        directory / "readback.json",
        {name: value for name, value in readback.items() if name != "plan"},
    )
    node.write_json(directory / "plan.json", readback["plan"])
    node.write_json(
        directory / "node-inputs.json",
        {name: readback[name]["facts"] for name in ("basis", "candidate")},
    )
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write(
                "selected="
                + str(bool(readback["plan"]["checks"])).lower()
                + "\n"
            )
    return readback


def validate_plan(value: dict[str, Any], context: dict[str, Any]) -> None:
    """Bind process results to the original received comparison."""
    if (
        value["comparison"] != context["basis"]["reference"]
        or value["candidate"] != context["candidate"]["reference"]
        or value["scope"] != SCOPE
    ):
        message = "The original Ruby plan has a different binding"
        raise ValueError(message)


def execute(root: Path, directory: Path) -> dict[str, Any]:
    """Execute every selected check and collect against the unchanged plan."""
    context = node.read_json(directory / "context.json")
    plan_value = node.read_json(directory / "plan.json")
    validate_plan(plan_value, context)
    if not plan_value["checks"]:
        node.write_json(directory / "results.json", [])
        outcome = invoke(
            root,
            directory,
            context,
            "collection",
            "result",
            directory / "plan.json",
            directory / "results.json",
        )
        node.write_json(directory / "outcome.json", outcome)
        return outcome
    readback = node.read_json(directory / "readback.json")
    preparation = node.read_json(directory / "preparation.json")
    candidate = preparation["request"]["candidate"]
    if candidate["checkout"] != context["candidate"]:
        message = "Prepared Ruby candidate differs from execution"
        raise ValueError(message)
    scratch = directory / "execution"
    scratch.mkdir()
    collection = candidate["collection"]
    tools = node.read_json(directory / "preparation-request.json")["tools"]
    request = {
        "checkout": context["candidate"]["directory"],
        "scratch": str(scratch),
        "facts": collection["tools"],
        "gem": tools["gem"],
        "bundle": tools["bundle"],
        "nbgv": collection["nbgv"],
        "candidate": readback["candidate"],
    }
    path = directory / "execution-request.json"
    node.write_json(path, request)
    result = invoke(
        root,
        directory,
        context,
        "execution",
        "run-ruby-group",
        directory / "plan.json",
        path,
        required=False,
    )
    node.write_json(directory / "result.json", result)
    node.write_json(directory / "results.json", result["results"])
    outcome = invoke(
        root,
        directory,
        context,
        "collection",
        "result",
        directory / "plan.json",
        directory / "results.json",
        required=False,
    )
    node.write_json(directory / "outcome.json", outcome)
    print(json.dumps(outcome, indent=2))
    return outcome


def invoke(  # noqa: PLR0913 - Finite phase inputs.
    root: Path,
    directory: Path,
    context: dict[str, Any],
    label: str,
    command: str,
    *inputs: Path,
    required: bool = True,
) -> dict[str, Any]:
    """Keep actual command diagnostics without retries or replanning."""
    raw = node.run(
        root,
        directory,
        label,
        node.read_json(directory / "preparation-request.json")["tools"][
            "dotnet"
        ],
        context["application"],
        "ci",
        command,
        *(str(path) for path in inputs),
        required=required,
        timeout=3600,
    )
    if not raw:
        message = f"Native Ruby {label} did not emit a result"
        raise ValueError(message)
    return json.loads(raw, object_pairs_hook=ci_scope._unique_object)  # noqa: SLF001


def main() -> int:
    """Receive native artifacts and use finite prepare/plan/run phases."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ci_scope.ROOT)
    commands = parser.add_subparsers(dest="command", required=True)
    receiver = commands.add_parser("materialize")
    receiver.add_argument("--scope-file", type=Path, required=True)
    receiver.add_argument("--transfer", type=Path, required=True)
    for prefix in ("scope", "control"):
        receiver.add_argument(
            "--" + prefix + "-metadata", type=Path, required=True
        )
        receiver.add_argument(
            "--" + prefix + "-artifact-id", type=int, required=True
        )
    receiver.add_argument("--run-id", type=int, required=True)
    receiver.add_argument("--repository-id", type=int, required=True)
    for command in (
        receiver,
        commands.add_parser("plan"),
        commands.add_parser("execute"),
    ):
        command.add_argument("--directory", type=Path, required=True)
    options = parser.parse_args()
    root, directory = options.repository.resolve(), options.directory.resolve()
    if options.command == "materialize":
        node.validate_scope_artifact(
            node.read_json(options.scope_metadata),
            options.scope_artifact_id,
            options.run_id,
            options.repository_id,
        )
        dotnet.validate_artifact(
            node.read_json(options.control_metadata),
            options.control_artifact_id,
            options.run_id,
            options.repository_id,
        )
        materialize(
            root,
            options.scope_file.resolve(),
            options.transfer.resolve(),
            directory,
        )
    elif options.command == "plan":
        plan(root, directory)
    else:
        return 0 if execute(root, directory)["satisfied"] else 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
