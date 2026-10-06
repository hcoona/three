"""Execute the concrete Node CI group from the received immutable scope."""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import ci_scope

CONTROL_PROJECT = "src/private/app/workflow-delivery/WorkflowDelivery.csproj"
BUILD_PROPERTIES = (
    "-property:Configuration=Debug",
    "-property:ContinuousIntegrationBuild=true",
    "-property:RestoreLockedMode=true",
    "-property:MSBuildLogVerboseTaskParameters=true",
    "-property:NuGetInteractive=false",
)


def read_json(path: Path) -> dict[str, Any]:
    """Read one supported process payload without duplicate field ambiguity."""
    return json.loads(
        path.read_text(encoding="utf-8"),
        object_pairs_hook=lambda pairs: ci_scope._unique_object(  # noqa: SLF001 - Shared receiver parser.
            pairs, "Node group"
        ),
    )


def write_json(path: Path, value: object) -> None:
    """Retain one process payload in the owned external carrier."""
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def validate_scope_artifact(
    metadata: dict[str, Any],
    artifact: int,
    run_id: int,
    repository: int,
    *,
    name: str = "ci-scope",
) -> None:
    """Use native artifact/run identity at the receiving boundary."""
    if any(
        type(value) is not int or value <= 0
        for value in (artifact, run_id, repository)
    ):
        message = "Missing native artifact context"
        raise ValueError(message)
    workflow = metadata.get("workflow_run")
    if (
        metadata.get("id") != artifact
        or metadata.get("name") != name
        or metadata.get("expired") is not False
        or not isinstance(workflow, dict)
        or workflow.get("id") != run_id
        or workflow.get("repository_id") != repository
    ):
        message = "Scope artifact has a different native producer or lifetime"
        raise ValueError(message)


def run(
    root: Path, directory: Path, label: str, *args: str, required: bool = True
) -> str:
    """Retain the actual native outcome; never repair or retry an invocation."""
    outcome: dict[str, Any] = {"arguments": args, "cwd": str(root)}
    try:
        result = subprocess.run(
            args,
            cwd=root,
            capture_output=True,
            check=False,
            timeout=900,
            env=os.environ
            | {"MSBUILDLOGTASKINPUTS": "1", "MSBUILDLOGTASKOUTPUTS": "1"},
        )
        stdout, stderr = result.stdout, result.stderr
        outcome.update(exitCode=result.returncode, termination="exited")
    except subprocess.TimeoutExpired as error:
        stdout, stderr = error.stdout or b"", error.stderr or b""
        outcome.update(exitCode=None, termination="timedOut")
    (directory / (label + ".stdout")).write_bytes(stdout)
    (directory / (label + ".stderr")).write_bytes(stderr)
    write_json(directory / (label + ".command.json"), outcome)
    try:
        output = stdout.decode("utf-8", errors="strict")
        error_output = stderr.decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        if required:
            raise
        return ""
    if required and (
        outcome["termination"] == "timedOut" or outcome["exitCode"]
    ):
        try:
            sys.stderr.write(output)
            sys.stderr.write(error_output)
        except (OSError, UnicodeError):
            pass
    if outcome["termination"] == "timedOut":
        if required:
            raise subprocess.TimeoutExpired(args, 900, stdout, stderr)
        return ""
    if not required and outcome["exitCode"] not in (0, 1):
        return ""
    if required and outcome["exitCode"]:
        raise subprocess.CalledProcessError(
            outcome["exitCode"], args, stdout, stderr
        )
    return output


def owner_present(root: Path, revision: str) -> bool:
    """Distinguish committed owner absence from unsupported entry modes."""
    raw = ci_scope.git(root, "ls-tree", "-z", revision, "--", CONTROL_PROJECT)
    if not raw:
        return False
    metadata, path = raw.removesuffix("\0").split("\t", 1)
    mode, kind, _ = metadata.split(" ")
    if (
        path != CONTROL_PROJECT
        or kind != "blob"
        or mode not in {"100644", "100755"}
    ):
        message = "The group control owner must be a committed regular project"
        raise ValueError(message)
    return True


def comparison(root: Path, scope: dict[str, Any]) -> tuple[str, str]:
    """Require the received comparison to name exact available Git commits."""
    if not isinstance(scope, dict) or not isinstance(scope.get("full"), bool):
        message = "Missing scope comparison"
        raise ValueError(message)  # noqa: TRY004 - Malformed transfer.
    before, after = scope.get("base"), scope.get("candidate")
    for value in (before, after):
        if not isinstance(value, str) or not re.fullmatch(
            r"[0-9a-f]{40}", value
        ):
            message = "Scope requires full native commit identities"
            raise ValueError(message)
        if (
            ci_scope.git(
                root,
                "rev-parse",
                "--verify",
                "--end-of-options",
                value + "^{commit}",
            ).strip()
            != value
        ):
            message = "Scope commit is unavailable"
            raise ValueError(message)
    if after != ci_scope.git(root, "rev-parse", "HEAD").strip():
        message = "Scope does not name the checked-out candidate"
        raise ValueError(message)
    if scope["full"] and before != after:
        message = "Explicit full has a different basis"
        raise ValueError(message)
    return before, after


def join_record_owners(
    root: Path, scope: dict[str, Any], endpoints: dict[str, Path]
) -> dict[str, Any]:
    """Join finite native endpoint reasons, keeping consumption independent."""
    joined = json.loads(json.dumps(scope))
    for name, revision in (
        ("basis", scope["base"]),
        ("candidate", scope["candidate"]),
    ):
        owners = ci_scope.record_control_owners(root, revision, endpoints[name])
        endpoint = joined["endpoint_owners"][name]
        if endpoint["revision"] != revision:
            message = "Record responsibility has a different endpoint"
            raise ValueError(message)
        for row in endpoint["paths"]:
            for reason in owners.get(row["path"], []):
                if reason not in row["reasons"]:
                    row["reasons"].append(reason)
    return joined


def build(root: Path, directory: Path, name: str) -> tuple[Path, Path]:
    """Build fresh control outputs and retain complete native operation logs."""
    run(
        root,
        directory,
        name + "-restore",
        "mise",
        "exec",
        "--locked",
        "--",
        "dotnet",
        "restore",
        CONTROL_PROJECT,
        "--locked-mode",
        "--configfile",
        str(root / "nuget.config"),
        *BUILD_PROPERTIES,
        "-binaryLogger:" + str(directory / (name + "-restore.binlog")),
    )
    log = directory / (name + ".binlog")
    run(
        root,
        directory,
        name + "-build",
        "mise",
        "exec",
        "--locked",
        "--",
        "dotnet",
        "msbuild",
        CONTROL_PROJECT,
        "-nologo",
        "-noAutoResponse",
        "-target:Build",
        *BUILD_PROPERTIES,
        "-binaryLogger:" + str(log),
    )
    target = run(
        root,
        directory,
        name + "-target",
        "mise",
        "exec",
        "--locked",
        "--",
        "dotnet",
        "msbuild",
        CONTROL_PROJECT,
        "-nologo",
        "-noAutoResponse",
        *BUILD_PROPERTIES,
        "-getProperty:TargetPath",
        "-binaryLogger:" + str(directory / (name + "-target.binlog")),
    ).strip()
    path = Path(target)
    if (
        not path.is_absolute()
        or not path.is_file()
        or not path.is_relative_to(root)
    ):
        message = "The native control target is unavailable"
        raise ValueError(message)
    return log, path


def execute(root: Path, source: Path, directory: Path) -> dict[str, Any]:
    """Join native endpoint inputs and run the concrete adopted CI recipe."""
    root, source, directory = (
        root.resolve(),
        source.resolve(),
        directory.resolve(),
    )
    if (
        root == directory
        or directory.is_relative_to(root)
        or root.is_relative_to(directory)
    ):
        message = "Node group state must be outside the source checkout"
        raise ValueError(message)
    scope = read_json(source)
    before, after = comparison(root, scope)
    basis_owner = owner_present(root, before)
    if not owner_present(root, after):
        message = "The candidate must retain the control application"
        raise ValueError(message)
    directory.mkdir(parents=True, exist_ok=False)
    endpoints = {name: directory / name for name in ("basis", "candidate")}
    for name, revision in (("basis", before), ("candidate", after)):
        run(
            root,
            directory,
            name + "-checkout",
            "git",
            "worktree",
            "add",
            "--detach",
            str(endpoints[name]),
            revision,
        )
        run(
            endpoints[name],
            directory,
            name + "-tools",
            "mise",
            "install",
            "--locked",
        )
        run(
            endpoints[name],
            directory,
            name + "-dependencies",
            "mise",
            "exec",
            "--",
            "pnpm",
            "install",
            "--frozen-lockfile",
        )
        run(
            endpoints[name],
            directory,
            name + "-dotnet-tools",
            "mise",
            "exec",
            "--",
            "dotnet",
            "tool",
            "restore",
        )
    candidate_log, target = build(
        endpoints["candidate"], directory, "candidate"
    )
    basis_log = (
        build(endpoints["basis"], directory, "basis")[0]
        if basis_owner
        else None
    )
    control = directory / "control"
    shutil.copytree(target.parent, control)
    application = control / target.name
    joined = directory / "scope-joined.json"
    write_json(joined, join_record_owners(root, scope, endpoints))
    request = directory / "planning-request.json"
    write_json(
        request,
        {
            "basis": {
                "directory": str(endpoints["basis"]),
                "reference": before,
            },
            "candidate": {
                "directory": str(endpoints["candidate"]),
                "reference": after,
            },
            "full": scope["full"],
            "scopePath": str(joined),
            "basisBuildLog": str(basis_log) if basis_log else None,
            "candidateBuildLog": str(candidate_log),
        },
    )
    for name, endpoint in endpoints.items():
        run(
            endpoint,
            directory,
            name + "-tracked-status",
            "git",
            "--no-lazy-fetch",
            "--no-replace-objects",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=no",
            "--ignore-submodules=none",
        )
    raw = run(
        endpoints["candidate"],
        directory,
        "planning",
        "mise",
        "exec",
        "--",
        "dotnet",
        str(application),
        "ci",
        "plan-node-group",
        str(request),
    )
    plan_path = directory / "group-plan.json"
    plan_path.write_text(raw, encoding="utf-8")
    group = read_json(plan_path)
    parent_path = directory / "parent-plan.json"
    write_json(parent_path, group["adopted"])
    results = run_runtimes(root, directory, application, group, parent_path)
    result_path = directory / "results.json"
    write_json(result_path, results)
    raw = run(
        endpoints["candidate"],
        directory,
        "collection",
        "mise",
        "exec",
        "--",
        "dotnet",
        str(application),
        "ci",
        "result",
        str(parent_path),
        str(result_path),
    )
    (directory / "outcome.json").write_text(raw, encoding="utf-8")
    if output := os.environ.get("GITHUB_OUTPUT"):
        retained = [member["directory"] for member in group["retained"]]
        with Path(output).open("a", encoding="utf-8") as stream:
            stream.write("retained=" + str(bool(retained)).lower() + "\n")
            stream.write("retained_directories=" + json.dumps(retained) + "\n")
            stream.write(
                "adopted_directory=" + group["adoptedDirectory"] + "\n"
            )
    return group


def run_runtimes(
    root: Path,
    directory: Path,
    application: Path,
    group: dict[str, Any],
    parent_path: Path,
) -> list[dict[str, Any]]:
    """Prepare distinct sources and retain both original runtime result sets."""
    after = group["adopted"]["candidate"]
    results = []
    if group["adopted"]["checks"]:
        for variant in group["runtime"]["variants"]:
            key = variant["key"]
            checkout = directory / key
            run(
                root,
                directory,
                key + "-checkout",
                "git",
                "worktree",
                "add",
                "--detach",
                str(checkout),
                after,
            )
            selector = "node@" + (
                variant["exactVersion"]
                or variant["selector"].removesuffix(".x")
            )
            run(
                checkout,
                directory,
                key + "-tools",
                "mise",
                "--no-config",
                "install",
                selector,
            )
            run(
                checkout,
                directory,
                key + "-dependencies",
                "mise",
                "exec",
                selector,
                "--",
                "pnpm",
                "install",
                "--frozen-lockfile",
            )
            run(
                checkout,
                directory,
                key + "-dotnet-tools",
                "mise",
                "exec",
                selector,
                "--",
                "dotnet",
                "tool",
                "restore",
            )
            scratch = directory / (key + "-scratch")
            scratch.mkdir()
            execution = directory / (key + "-request.json")
            write_json(
                execution,
                {
                    "checkout": str(checkout),
                    "scratch": str(scratch),
                    "runtime": key,
                },
            )
            run(
                checkout,
                directory,
                key + "-tracked-status",
                "git",
                "--no-lazy-fetch",
                "--no-replace-objects",
                "status",
                "--porcelain=v1",
                "-z",
                "--untracked-files=no",
                "--ignore-submodules=none",
            )
            output = run(
                checkout,
                directory,
                key + "-execution",
                "mise",
                "exec",
                selector,
                "--",
                "dotnet",
                str(application),
                "ci",
                "run-node",
                str(parent_path),
                str(execution),
                required=False,
            )
            if output:
                result_path = directory / (key + "-result.json")
                result_path.write_text(output, encoding="utf-8")
                result = read_json(result_path)
                if result["candidate"] != after or result["runtime"] != key:
                    message = "Runtime result has a different original subject"
                    raise ValueError(message)
                results.extend(result["results"])
    return results


def run_retained(
    root: Path, directories: list[str], adopted: str, directory: Path
) -> None:
    """Delegate selected retained members to native PNPM scheduling."""
    if (
        not isinstance(directories, list)
        or not directories
        or any(not isinstance(value, str) or not value for value in directories)
        or len(set(directories)) != len(directories)
        or adopted in directories
    ):
        message = "Retained execution requires distinct selected native members"
        raise ValueError(message)
    filters = [
        argument
        for member in directories
        for argument in ("--filter", "./" + member)
    ]
    filters.extend(("--filter", "!./" + adopted))
    directory.mkdir(parents=True, exist_ok=False)
    probe = os.environ["PACKAGE_DIR"]
    if probe in directories:
        run(
            root,
            directory,
            "typecheck",
            "pnpm",
            "--dir",
            probe,
            "run",
            "typecheck",
        )
    run(
        root,
        directory,
        "test",
        "pnpm",
        *filters,
        "--recursive",
        "--if-present",
        "run",
        "test",
    )
    run(
        root,
        directory,
        "build",
        "pnpm",
        *filters,
        "--recursive",
        "--if-present",
        "run",
        "build",
    )
    if probe in directories:
        run(
            root,
            directory,
            "packed",
            "pnpm",
            "--dir",
            probe,
            "run",
            "validate:packed-artifact",
        )


def main() -> None:
    """Receive the exact native scope artifact before starting product work."""
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    adopted = modes.add_parser("group")
    retained = modes.add_parser("retained")
    for mode in (adopted, retained):
        mode.add_argument("--repository-root", type=Path, default=ci_scope.ROOT)
        mode.add_argument("--directory", type=Path, required=True)
    retained.add_argument("--directories", type=json.loads, required=True)
    retained.add_argument("--adopted-directory", required=True)
    adopted.add_argument("--scope-file", type=Path, required=True)
    adopted.add_argument("--artifact-metadata", type=Path, required=True)
    adopted.add_argument("--artifact-id", type=int, required=True)
    adopted.add_argument("--run-id", type=int, required=True)
    adopted.add_argument("--repository-id", type=int, required=True)
    args = parser.parse_args()
    if args.mode == "retained":
        run_retained(
            args.repository_root,
            args.directories,
            args.adopted_directory,
            args.directory,
        )
        return
    validate_scope_artifact(
        read_json(args.artifact_metadata),
        args.artifact_id,
        args.run_id,
        args.repository_id,
    )
    execute(args.repository_root, args.scope_file, args.directory)


if __name__ == "__main__":
    main()
