"""Concrete native .NET caller transfers and finite preparation failures."""

from __future__ import annotations

import importlib
import json
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng/scripts"))
try:
    group = importlib.import_module("run_dotnet_ci_group")
finally:
    sys.path.pop(0)

BASIS = "a" * 40
CANDIDATE = "b" * 40
SCOPE = "dotnet/native-endpoint-selection-v1"


@pytest.mark.parametrize(
    "defect", [None, "id", "name", "expired", "run", "repository"]
)
def test_receiver_uses_actual_native_artifact_identity(defect):
    """Reject wrong producer, run, repository or lifetime."""
    metadata = {
        "id": 11,
        "name": "ci-dotnet-plan",
        "expired": False,
        "workflow_run": {"id": 22, "repository_id": 33},
    }
    if defect == "run":
        metadata["workflow_run"]["id"] = 23
    elif defect == "repository":
        metadata["workflow_run"]["repository_id"] = 34
    elif defect:
        metadata[defect] = {"id": 12, "name": "ci-scope", "expired": True}[
            defect
        ]
    if defect:
        with pytest.raises(ValueError, match="producer or lifetime"):
            group.validate_artifact(metadata, 11, 22, 33)
    else:
        group.validate_artifact(metadata, 11, 22, 33)


def test_bootstrap_transfers_complete_native_default_output(
    tmp_path, monkeypatch
):
    """Preserve native runtime and dependency files together."""
    root = tmp_path / "source"
    output = root / "bin/Debug/net10.0"
    output.mkdir(parents=True)
    for name in (
        "WorkflowDelivery.dll",
        "WorkflowDelivery.deps.json",
        "WorkflowDelivery.runtimeconfig.json",
        "Microsoft.Build.Utilities.Core.dll",
        "native/runtimes/runtime.bin",
    ):
        file = output / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(name, encoding="utf-8")
    log = tmp_path / "native.binlog"
    log.write_bytes(b"native operation")
    source = "src/private/app/workflow-delivery/Native/Python"
    committed = {
        source + "/packages.py": b"# Original control bytes\r\n",
        source + "/uv/Cargo.lock": b"# Original native lock\n",
    }
    for name, content in committed.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    hooks = tmp_path / "empty-hooks"
    hooks.mkdir()
    group.ci_scope.git(root, "init", "--quiet")
    group.ci_scope.git(root, "-c", "core.autocrlf=false", "add", "--", source)
    group.ci_scope.git(
        root,
        "-c",
        "user.name=Tests",
        "-c",
        "user.email=tests@example.invalid",
        "-c",
        "commit.gpgSign=false",
        "-c",
        "core.hooksPath=" + str(hooks),
        "commit",
        "--quiet",
        "-m",
        "Commit candidate control sources",
    )
    (root / source / "packages.py").write_bytes(b"# Dirty working copy\n")
    untracked = root / source / "uv/target/local-build"
    untracked.parent.mkdir(parents=True)
    untracked.write_bytes(b"Untracked native build output")
    monkeypatch.setattr(
        group.node, "build", lambda *_: (log, output / "WorkflowDelivery.dll")
    )
    state = tmp_path / "state"
    actual = group.bootstrap(root, state)
    assert actual == output / "WorkflowDelivery.dll"
    assert {
        path.relative_to(output) for path in output.rglob("*") if path.is_file()
    } == {
        path.relative_to(state / "transfer/control")
        for path in (state / "transfer/control").rglob("*")
        if path.is_file()
    }
    assert (
        state / "transfer/candidate-control.binlog"
    ).read_bytes() == log.read_bytes()
    with zipfile.ZipFile(state / "transfer/python-control.zip") as archive:
        assert {
            item.filename: archive.read(item)
            for item in archive.infolist()
            if not item.is_dir()
        } == committed


def test_failed_python_control_archive_does_not_complete_bootstrap(
    tmp_path, monkeypatch
):
    """Native source-transfer failure cannot return a completed bootstrap."""
    root = tmp_path / "root"
    output = root / "output"
    output.mkdir(parents=True)
    application = output / "WorkflowDelivery.dll"
    application.write_bytes(b"Native managed output")
    log = tmp_path / "candidate.binlog"
    log.write_bytes(b"Native build log")
    monkeypatch.setattr(group.node, "build", lambda *_: (log, application))
    failure = subprocess.CalledProcessError(128, ["git", "archive"])

    def native(*_args, **_kwargs):
        raise failure

    monkeypatch.setattr(group.node, "run", native)
    state = tmp_path / "state"
    with pytest.raises(subprocess.CalledProcessError) as captured:
        group.bootstrap(root, state)
    assert captured.value is failure
    assert not (state / "control.json").exists()


@pytest.mark.parametrize("shared_endpoint", [False, True])
@pytest.mark.parametrize("locked_failure", [False, True])
def test_absent_basis_control_still_prepares_both_native_endpoints(
    tmp_path, monkeypatch, shared_endpoint, locked_failure
):
    """Owner absence never removes native endpoint preparation."""
    root = tmp_path / "root"
    state = tmp_path / "state"
    state.mkdir()
    group.node.write_json(
        state / "control.json",
        {
            "application": str(root / "WorkflowDelivery.dll"),
            "log": str(state / "candidate.binlog"),
            "root": str(root),
            "transfer": str(state / "transfer"),
        },
    )
    endpoints = {
        "full": False,
        "basis": {"directory": str(tmp_path / "basis"), "reference": BASIS},
        "candidate": {
            "directory": str(tmp_path / "candidate"),
            "reference": CANDIDATE,
        },
    }
    if shared_endpoint:
        endpoints["basis"] = dict(endpoints["candidate"])
        endpoints["full"] = True
    endpoints_path = tmp_path / "endpoints.json"
    group.node.write_json(endpoints_path, endpoints)
    commands = []
    error = subprocess.CalledProcessError(
        2,
        ("mise", "install", "--locked", "node", "pnpm"),
        output=b"",
        stderr=b"locked URLs unavailable",
    )

    def native(endpoint, _directory, label, *args, required=True, timeout=900):
        assert required is True
        assert timeout == 900
        commands.append((endpoint, label, args))
        if locked_failure and args[:2] == ("mise", "install"):
            raise error

    monkeypatch.setattr(
        group.node,
        "run",
        native,
    )
    monkeypatch.setattr(group.node, "owner_present", lambda *_: False)
    if locked_failure:
        with pytest.raises(subprocess.CalledProcessError) as captured:
            group.prepare(root, state, endpoints_path)
        assert captured.value is error
        assert not (state / "planning/request.json").exists()
        assert commands[-1][2] == error.cmd
        return
    request_path = group.prepare(root, state, endpoints_path)
    request = group.node.read_json(request_path)
    assert request["basisControl"] is None
    assert request["basisBuildLog"] is None
    assert request["basisFiles"]["deadlineSeconds"] == 900
    assert request["candidateFiles"]["deadlineSeconds"] == 900
    assert request["completion"]["deadlineSeconds"] == 900
    assert request["basis"] == endpoints["basis"]
    restores = [
        (endpoint, args)
        for endpoint, _, args in commands
        if args[:2] == ("dotnet", "restore")
    ]
    assert {endpoint for endpoint, _ in restores} == {
        Path(endpoints[name]["directory"]) for name in ("basis", "candidate")
    }
    for _, args in restores:
        assert args[2] == "dirs.proj"
        assert set(group.PROPERTIES) <= set(args)
    installs = [
        (endpoint, args)
        for endpoint, _, args in commands
        if args[:2] == ("mise", "install")
    ]
    assert installs == [
        (endpoint, ("mise", "install", "--locked", "node", "pnpm"))
        for endpoint in dict.fromkeys(
            Path(endpoints[name]["directory"])
            for name in ("basis", "candidate")
        )
    ]


@pytest.mark.parametrize("failed", [False, True])
def test_only_successful_planning_finalizes_applicability(
    tmp_path, monkeypatch, failed
):
    """Failed planning emits no transferable original plan."""
    state = tmp_path / "state"
    (state / "planning").mkdir(parents=True)
    (state / "transfer").mkdir()
    request_path = state / "planning/request.json"
    group.node.write_json(
        request_path,
        {
            "scopePath": str(state / "planning/preliminary.json"),
            "basis": {"directory": "basis"},
            "candidate": {"directory": "candidate"},
        },
    )
    preliminary = {
        "base": BASIS,
        "candidate": CANDIDATE,
        "full": False,
        "scopes": {"dotnet": True, "python": True},
        "reasons": {
            "dotnet": ["old heuristic"],
            "python": ["native outside job"],
        },
        "endpoint_owners": {
            "basis": {"revision": BASIS, "paths": []},
            "candidate": {"revision": CANDIDATE, "paths": []},
        },
    }
    plan = {
        "comparison": BASIS,
        "candidate": CANDIDATE,
        "scope": SCOPE,
        "checks": [],
    }
    monkeypatch.setattr(
        group.node,
        "join_record_owners",
        lambda _, value, __: json.loads(json.dumps(value)),
    )

    def native(*_args, **_kwargs):
        if failed:
            raise subprocess.CalledProcessError(
                2, ["dotnet", "ci", "plan-dotnet-group"]
            )
        return json.dumps(
            {
                "plan": plan,
                "basis": [],
                "candidate": [],
                "candidateContext": {"revision": CANDIDATE},
            }
        )

    monkeypatch.setattr(group.node, "run", native)
    if failed:
        with pytest.raises(subprocess.CalledProcessError):
            group.finish(
                tmp_path, preliminary, request_path, tmp_path / "control.dll"
            )
        assert not (state / "transfer/dotnet-plan.json").exists()
    else:
        result = group.finish(
            tmp_path, preliminary, request_path, tmp_path / "control.dll"
        )
        assert result["scopes"] == {"dotnet": False, "python": True}
        assert result["reasons"]["dotnet"] == []
        assert result["reasons"]["python"] == ["native outside job"]
        assert group.node.read_json(state / "transfer/dotnet-plan.json") == plan
    assert preliminary["scopes"]["dotnet"] is True


@pytest.mark.parametrize(
    ("populated", "termination"),
    [(False, "passed"), (True, "passed"), (True, "failed"), (True, "timedOut")],
)
def test_original_plan_still_reaches_receiver_and_collector(
    tmp_path, monkeypatch, populated, termination
):
    """Preserve original work and terminal outcomes under finite budgets."""
    root, transfer = tmp_path / "root", tmp_path / "transfer"
    (transfer / "control").mkdir(parents=True)
    application = transfer / "control/WorkflowDelivery.dll"
    application.write_bytes(b"complete fixture distribution")
    plan_path = transfer / "dotnet-plan.json"
    key = {"target": "control", "check": "test", "variant": "net10.0"}
    checks = [{"work": {"key": key}, "reasons": []}] if populated else []
    raw = (
        '{ "comparison": "'
        + BASIS
        + '", "candidate": "'
        + CANDIDATE
        + '", "scope": "'
        + SCOPE
        + '", "checks": '
        + json.dumps(checks)
        + " }\n"
    )
    plan_path.write_text(raw, encoding="utf-8")
    results = (
        [
            {
                "candidate": CANDIDATE,
                "key": key,
                "status": "Failed" if termination == "failed" else "Passed",
            }
        ]
        if populated
        else []
    )
    outcome = {
        "comparison": BASIS,
        "candidate": CANDIDATE,
        "scope": SCOPE,
        "satisfied": termination == "passed",
    }
    commands = []
    directory = tmp_path / "execution"
    monkeypatch.setattr(group.node, "comparison", lambda *_: (BASIS, CANDIDATE))

    def native(args, **options):
        label = {
            "tool": "tools",
            "restore": "restore",
            "run-dotnet": "execution",
            "result": "collection",
        }[args[3] if args[1] == str(application) else args[1]]
        commands.append((label, options["timeout"]))
        if label == "execution":
            assert args[4] == str(plan_path)
            if termination == "timedOut":
                raise subprocess.TimeoutExpired(
                    args, options["timeout"], b"partial", b"deadline"
                )
            payload = {"candidate": CANDIDATE, "results": results}
        elif label == "collection":
            assert args[4] == str(plan_path)
            assert group.node.read_json(Path(args[5])) == results
            payload = outcome
        else:
            return subprocess.CompletedProcess(args, 0, b"", b"")
        return subprocess.CompletedProcess(
            args,
            int(termination == "failed"),
            json.dumps(payload).encode("utf-8"),
            b"native diagnostics",
        )

    monkeypatch.setattr(group.node.subprocess, "run", native)
    if termination == "timedOut":
        with pytest.raises(ValueError, match="did not emit a result"):
            group.execute(root, transfer, directory, BASIS, CANDIDATE)
        assert not (directory / "results.json").exists()
        assert (directory / "execution.stdout").read_bytes() == b"partial"
        assert (directory / "execution.stderr").read_bytes() == b"deadline"
    elif termination == "failed":
        with pytest.raises(subprocess.CalledProcessError) as captured:
            group.execute(root, transfer, directory, BASIS, CANDIDATE)
        assert captured.value.returncode == 1
        assert json.loads(captured.value.output) == outcome
        assert captured.value.stderr == b"native diagnostics"
    else:
        assert (
            group.execute(root, transfer, directory, BASIS, CANDIDATE)
            == outcome
        )
        assert group.node.read_json(directory / "outcome.json") == outcome
    assert commands == (
        ([("tools", 900), ("restore", 900)] if populated else [])
        + [("execution", 3600)]
        + ([] if termination == "timedOut" else [("collection", 900)])
    )
    assert (
        group.node.read_json(directory / "request.json")["deadlineSeconds"]
        == 900
    )
    assert plan_path.read_text(encoding="utf-8") == raw
    if termination != "passed":
        assert not (directory / "outcome.json").exists()


def test_wrong_original_plan_binding_stops_before_preparation(
    tmp_path, monkeypatch
):
    """Wrong comparison stops before any native effect."""
    transfer = tmp_path / "transfer"
    transfer.mkdir()
    group.node.write_json(
        transfer / "dotnet-plan.json",
        {
            "comparison": BASIS,
            "candidate": CANDIDATE,
            "scope": SCOPE,
            "checks": [],
        },
    )
    monkeypatch.setattr(group.node, "comparison", lambda *_: (BASIS, CANDIDATE))
    monkeypatch.setattr(
        group.node,
        "run",
        lambda *_: pytest.fail("Native effect before binding"),
    )
    with pytest.raises(ValueError, match="different binding"):
        group.execute(
            tmp_path / "root",
            transfer,
            tmp_path / "execution",
            CANDIDATE,
            CANDIDATE,
        )
    assert not (tmp_path / "execution").exists()
