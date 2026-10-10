"""Receive, plan and execute the native Python CI group once."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import zipfile
from pathlib import Path
from typing import Any

import ci_scope
import run_dotnet_ci_group as dotnet
import run_node_ci_group as node

NATIVE_SOURCE = "src/private/app/workflow-delivery/Native/Python"
SCOPE = "python/native-paired-facts-v1"
V3_TESTS = "src/public/lib/three-workflow-delivery-v3/tests"
NATIVE_TESTS = "tests/private/app/workflow-delivery/Native/Python"
RUBY_TESTS = "tests/private/app/workflow-delivery/Native/Ruby"
AZURE_TESTS = "src/private/app/azureauth-credprovider/python/tests"


def executable(name: str) -> str:
    """Keep native shim identity while supplying canonical absolute spelling."""
    value = shutil.which(name)
    if value is None:
        message = f"Required native executable is unavailable: {name}"
        raise FileNotFoundError(message)
    return os.path.abspath(value)  # noqa: PTH100 - Preserve native shim identity.


def materialize(
    root: Path, scope_path: Path, transfer: Path, directory: Path
) -> dict[str, Any]:
    """Create fresh committed endpoints before query or product effects."""
    scope = node.read_json(scope_path)
    before, after = node.comparison(root, scope)
    application = transfer / "control/WorkflowDelivery.dll"
    archive = transfer / "python-control.zip"
    if not application.is_file() or not archive.is_file():
        message = "The complete candidate control transfer is missing"
        raise ValueError(message)
    if (
        directory == root
        or directory.is_relative_to(root)
        or root.is_relative_to(directory)
    ):
        message = "Python group state must be outside the source checkout"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    sources = directory / "control-source"
    sources.mkdir()
    with zipfile.ZipFile(archive) as native:
        native.extractall(sources)
    endpoints: dict[str, dict[str, str]] = {}
    for name, revision in (("basis", before), ("candidate", after)):
        if name == "candidate" and before == after:
            endpoint = Path(endpoints["basis"]["directory"])
        else:
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
    context = {
        **endpoints,
        "full": scope["full"],
        "scopePath": str(scope_path),
        "application": str(application),
        "controlSources": str(sources / NATIVE_SOURCE),
    }
    node.write_json(directory / "context.json", context)
    output(
        basis_directory=endpoints["basis"]["directory"],
        candidate_directory=endpoints["candidate"]["directory"],
        basis_separate=str(before != after).lower(),
    )
    return context


def plan(root: Path, directory: Path) -> dict[str, Any]:
    """Compose accepted native preparation and one paired selection."""
    context = node.read_json(directory / "context.json")
    scratch = directory / "query-preparation"
    scratch.mkdir()
    request = {
        name: context[name]
        for name in (
            "basis",
            "candidate",
            "full",
            "scopePath",
            "controlSources",
        )
    } | {
        "scratch": str(scratch),
        "tools": {
            name: executable(command)
            for name, command in (
                ("uv", "uv"),
                ("interpreter", "python"),
                ("dotnet", "dotnet"),
                ("rustup", "rustup"),
                ("cargo", "cargo"),
            )
        },
        "operation": {
            "runner": "ubuntu-latest",
            "variant": "python-3.14",
            "dimensions": {"python": "3.14"},
        },
        "deadlineSeconds": 900,
    }
    request_path = directory / "preparation-request.json"
    node.write_json(request_path, request)
    preparation = invoke(
        root,
        directory,
        context,
        "preparation",
        "prepare-python-group",
        request_path,
    )
    node.write_json(directory / "preparation.json", preparation)
    group_request = directory / "group-request.json"
    node.write_json(group_request, preparation["request"])
    readback = invoke(
        root, directory, context, "planning", "plan-python-group", group_request
    )
    validate_plan(readback["plan"], context)
    node.write_json(
        directory / "readback.json",
        {name: value for name, value in readback.items() if name != "plan"},
    )
    node.write_json(directory / "plan.json", readback["plan"])
    selected = auxiliary(readback["plan"])
    output(
        selected=str(bool(readback["plan"]["checks"])).lower(),
        legacy_v3=str(selected["legacy_v3"]).lower(),
        native_helper=str(selected["native_helper"]).lower(),
        native_ruby=str(selected["native_ruby"]).lower(),
    )
    return readback


def validate_plan(plan_value: dict[str, Any], context: dict[str, Any]) -> None:
    """Keep the original comparison and candidate at the process boundary."""
    if (
        plan_value["comparison"] != context["basis"]["reference"]
        or plan_value["candidate"] != context["candidate"]["reference"]
        or plan_value["scope"] != SCOPE
    ):
        message = "The original Python plan has a different binding"
        raise ValueError(message)


def auxiliary(plan_value: dict[str, Any]) -> dict[str, bool]:
    """Map actual selected pytest targets to their retained native adapters."""
    targets = {
        item["work"]["dimensions"]["testPath"]
        for item in plan_value["checks"]
        if item["work"]["key"]["check"] == "python/pytest-v1"
    }
    return {
        "legacy_v3": any(
            target == V3_TESTS or target.startswith(V3_TESTS + "/")
            for target in targets
        ),
        "native_helper": NATIVE_TESTS in targets,
        "native_ruby": RUBY_TESTS in targets,
        "azure_bundle": AZURE_TESTS in targets,
    }


def prepare_ruby_test_tools(root: Path, directory: Path) -> dict[str, str]:
    """Prepare the finite native Ruby fixture matrix in owned gem homes."""
    ruby = executable("ruby")
    homes = {}
    for version in ("2.4.20", "2.7.2"):
        home = directory / "ruby-test-tools" / version
        home.mkdir(parents=True)
        gems = home / "gems"
        native_environment = {
            "HOME": str(home),
            "PATH": str(Path(ruby).parent) + os.pathsep + os.defpath,
            "GEM_HOME": str(gems),
            "GEM_PATH": str(gems),
            "LANG": "C.UTF-8",
            "LC_ALL": "C.UTF-8",
        }
        packages = ["bundler:" + version]
        if version == "2.4.20":
            packages += ["rspec-core:3.13.6", "rspec:3.13.2"]
        node.run(
            root,
            directory,
            "ruby-test-tools-" + version,
            ruby,
            "-S",
            "gem",
            "install",
            *packages,
            "--no-document",
            "--minimal-deps",
            environment=native_environment,
            timeout=300,
        )
        homes[version] = str(gems)
    return {
        "WORKFLOW_DELIVERY_TEST_RUBY": ruby,
        "WORKFLOW_DELIVERY_TEST_RUBY_GEM_HOMES": json.dumps(homes),
    }


def execute(
    root: Path, directory: Path, mise_data: Path | None = None
) -> dict[str, Any]:
    """Execute and collect the original mixed plan without replanning."""
    context = node.read_json(directory / "context.json")
    readback = node.read_json(directory / "readback.json")
    plan_value = node.read_json(directory / "plan.json")
    validate_plan(plan_value, context)
    preparation = node.read_json(directory / "preparation.json")
    candidate = preparation["request"]["candidate"]
    if candidate["checkout"] != context["candidate"]:
        message = "Prepared Python candidate differs from execution"
        raise ValueError(message)
    selected = auxiliary(plan_value)
    environment = dict(candidate["environment"])
    if selected["legacy_v3"] or selected["azure_bundle"]:
        environment["PATH"] = os.pathsep.join(
            dict.fromkeys(
                [
                    str(Path(executable("pwsh")).parent),
                    *environment["PATH"].split(os.pathsep),
                ]
            )
        )
    if selected["azure_bundle"]:
        environment.update(
            {
                name: os.environ[name]
                for name in (
                    "DOTNET_ROOT",
                    "DOTNET_ROOT_X64",
                    "DOTNET_ROOT_ARM64",
                )
                if name in os.environ
            }
        )
    if selected["legacy_v3"]:
        if (
            mise_data is None
            or not mise_data.is_absolute()
            or not mise_data.is_dir()
        ):
            message = (
                "Selected V3 tests require their prepared mise data directory"
            )
            raise ValueError(message)
        native_tools = {
            name: executable(name)
            for name in ("node", "pnpm", "hk", "pkl", "ruby", "mise")
        }
        environment["MISE_DATA_DIR"] = str(mise_data)
        environment["MISE_TASK_RUN_AUTO_INSTALL"] = "false"
        environment["MISE_EXEC_AUTO_INSTALL"] = "0"
        environment.update(
            {
                name: os.environ[name]
                for name in ("HK_PROFILE",)
                if name in os.environ
            }
        )
        environment["PATH"] = os.pathsep.join(
            dict.fromkeys(
                [str(Path(path).parent) for path in native_tools.values()]
                + environment["PATH"].split(os.pathsep)
            )
        )
        node.run(
            Path(context["candidate"]["directory"]),
            directory,
            "mise-trust",
            native_tools["mise"],
            "trust",
            str(Path(context["candidate"]["directory"]) / "mise.toml"),
            timeout=30,
            environment=environment,
        )
        node.run(
            Path(context["candidate"]["directory"]),
            directory,
            "static-reference-preparation",
            candidate["collection"]["graph"]["interpreter"],
            "eng/scripts/workflow_delivery_v3_prepare_static_reference.py",
            environment=environment,
        )
    if selected["native_helper"]:
        compilation = next(
            row["command"]
            for row in preparation["commands"]
            if row["command"]["arguments"] == ["build", "--locked"]
        )
        for label, arguments in (
            ("format", ["fmt", "--all", "--", "--check"]),
            ("test", ["test", "--locked", "--all-targets", "--jobs", "4"]),
            (
                "clippy",
                [
                    "clippy",
                    "--locked",
                    "--all-targets",
                    "--jobs",
                    "4",
                    "--",
                    "-D",
                    "warnings",
                ],
            ),
        ):
            node.run(
                Path(compilation["directory"]),
                directory,
                "native-helper-" + label,
                compilation["executable"],
                *arguments,
                environment=compilation["environment"],
            )
    if selected["native_ruby"]:
        environment.update(prepare_ruby_test_tools(root, directory))
    environment = environment | {"PYTHONSAFEPATH": None}
    scratch = directory / "execution"
    scratch.mkdir()
    graph = candidate["collection"]["graph"]
    request = {
        "native": {
            "checkout": context["candidate"]["directory"],
            "scratch": str(scratch),
            "uv": graph["uv"],
            "interpreter": readback["interpreter"],
            **{
                name: readback[name]
                for name in ("members", "configuration", "targets", "operation")
            },
            "deadlineSeconds": 1800,
            "environment": environment,
        },
        "packageHelper": candidate["collection"]["packageHelper"],
    }
    request_path = directory / "execution-request.json"
    node.write_json(request_path, request)
    result = invoke(
        root,
        directory,
        context,
        "execution",
        "run-python-group",
        directory / "plan.json",
        request_path,
        required=False,
        environment=environment,
    )
    if result["candidate"] != plan_value["candidate"]:
        message = "Python execution returned a different candidate"
        raise ValueError(message)
    node.write_json(directory / "result.json", result)
    node.write_json(directory / "outcome.json", result["outcome"])
    print(json.dumps(result["outcome"], indent=2))
    return result["outcome"]


def invoke(  # noqa: PLR0913 - Concrete native phase invocation.
    root: Path,
    directory: Path,
    context: dict[str, Any],
    label: str,
    command: str,
    *inputs: Path,
    required: bool = True,
    environment: dict[str, str | None] | None = None,
) -> dict[str, Any]:
    """Retain native phase diagnostics without an application retry."""
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
        environment=environment,
    )
    if not raw:
        message = f"Native Python {label} did not emit a result"
        raise ValueError(message)
    return json.loads(raw, object_pairs_hook=ci_scope._unique_object)  # noqa: SLF001 - Shared receiver.


def output(**values: str) -> None:
    """Expose actual phase outputs to the existing static workflow."""
    if target := os.environ.get("GITHUB_OUTPUT"):
        with Path(target).open("a", encoding="utf-8") as stream:
            for name, value in values.items():
                stream.write(f"{name}={value}\n")


def main() -> int:
    """Use finite phases with native artifact checks at the receiver."""
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
    execution = commands.add_parser("execute")
    execution.add_argument("--mise-data-directory", type=Path)
    for command in (receiver, commands.add_parser("plan"), execution):
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
        return (
            0
            if execute(root, directory, options.mise_data_directory)[
                "satisfied"
            ]
            else 1
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
