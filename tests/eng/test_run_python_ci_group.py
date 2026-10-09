"""Native Python caller transfers, original-plan execution and no-work."""

from __future__ import annotations

import importlib
import json
import os
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

SCRIPTS = Path(__file__).resolve().parents[2] / "eng/scripts"
sys.path.insert(0, str(SCRIPTS))
try:
    group = importlib.import_module("run_python_ci_group")
finally:
    sys.path.remove(str(SCRIPTS))

BEFORE, AFTER = "a" * 40, "b" * 40


@pytest.mark.parametrize("full", [False, True])
def test_receiver_materializes_exact_committed_endpoints(tmp_path, full):
    """Native Git supplies both identities and the committed source archive."""
    root = tmp_path / "repository"
    root.mkdir()
    hooks = tmp_path / "hooks"
    hooks.mkdir()

    def git(*arguments):
        return subprocess.check_output(  # noqa: S603 - Controlled Git fixture.
            [  # noqa: S607 - Git owns the local fixture.
                "git",
                "-c",
                "core.hooksPath=" + str(hooks),
                "-c",
                "user.name=Caller Fixture",
                "-c",
                "user.email=caller@example.invalid",
                *arguments,
            ],
            cwd=root,
            text=True,
        ).strip()

    git("init", "-q")
    native = root / group.NATIVE_SOURCE / "uv"
    native.mkdir(parents=True)
    lock = native / "Cargo.lock"
    lock.write_bytes(b"# Original committed native lock\r\n")
    git("add", ".")
    git("commit", "-qm", "Create native source")
    before = git("rev-parse", "HEAD")
    (root / "changed.txt").write_text("Candidate contents", encoding="utf-8")
    git("add", ".")
    git("commit", "-qm", "Create candidate")
    after = git("rev-parse", "HEAD")
    if full:
        before = after
    transfer = tmp_path / "transfer"
    control = transfer / "control"
    control.mkdir(parents=True)
    application = control / "WorkflowDelivery.dll"
    application.write_bytes(b"Controlled managed output")
    dependency = control / "native/runtime.bin"
    dependency.parent.mkdir()
    dependency.write_bytes(b"Complete transferred dependency")
    git(
        "archive",
        "--format=zip",
        "--output=" + str(transfer / "python-control.zip"),
        after,
        "--",
        group.NATIVE_SOURCE,
    )
    scope = tmp_path / "scope.json"
    group.node.write_json(
        scope, {"base": before, "candidate": after, "full": full}
    )
    directory = tmp_path / "group"
    context = group.materialize(root, scope, transfer, directory)

    for name, revision in (("basis", before), ("candidate", after)):
        endpoint = context[name]
        assert endpoint["reference"] == revision
        assert (
            group.ci_scope.git(
                Path(endpoint["directory"]), "rev-parse", "HEAD"
            ).strip()
            == revision
        )
    assert (
        context["basis"]["directory"] == context["candidate"]["directory"]
    ) is full
    assert (
        Path(context["controlSources"]).joinpath("uv/Cargo.lock").read_bytes()
        == lock.read_bytes()
    )
    assert Path(context["application"]) == application
    assert dependency.read_bytes() == b"Complete transferred dependency"
    with pytest.raises(FileExistsError):
        group.materialize(root, scope, transfer, directory)


def test_cargo_shim_identity_is_not_resolved_into_rustup(tmp_path, monkeypatch):
    """Absolute spelling preserves the native executable name."""
    shim = tmp_path / "cargo"
    target = tmp_path / "rustup"
    target.touch()
    shim.symlink_to(target)
    monkeypatch.setattr(group.shutil, "which", lambda _name: str(shim))
    assert group.executable("cargo") == str(shim)
    assert group.executable("cargo") != str(shim.resolve())


@pytest.fixture
def flow(tmp_path, monkeypatch):
    """Use caller serializers with controlled native CLI responses."""
    root = tmp_path / "source"
    root.mkdir()
    directory = tmp_path / "group"
    directory.mkdir()
    context = {
        "basis": {"directory": str(tmp_path / "basis"), "reference": BEFORE},
        "candidate": {
            "directory": str(tmp_path / "candidate"),
            "reference": AFTER,
        },
        "full": False,
        "scopePath": str(tmp_path / "scope.json"),
        "application": str(tmp_path / "control/WorkflowDelivery.dll"),
        "controlSources": str(tmp_path / "native-source"),
    }
    group.node.write_json(directory / "context.json", context)
    state = {"checks": [], "satisfied": True, "defect": None}
    calls = []
    tool_requests = []

    def executable(name):
        tool_requests.append(name)
        directory = "powershell" if name == "pwsh" else "tools"
        return str(tmp_path / directory / name)

    monkeypatch.setattr(group, "executable", executable)
    output = tmp_path / "github-output"
    monkeypatch.setenv("GITHUB_OUTPUT", str(output))

    def run(_root, _directory, label, *arguments, **options):
        calls.append((label, arguments, options))
        if label == "preparation":
            if state["defect"] == "preparation":
                raise subprocess.CalledProcessError(2, arguments)
            request = group.node.read_json(Path(arguments[-1]))
            endpoints = {}
            for name in ("basis", "candidate"):
                home = str(directory / "query-preparation" / name / "home")
                endpoints[name] = {
                    "checkout": request[name],
                    "environment": {
                        "HOME": home,
                        "DOTNET_CLI_HOME": home,
                        "PATH": "/native/bin",
                    },
                    "collection": {
                        "graph": {
                            "root": request[name]["directory"],
                            "uv": request["tools"]["uv"],
                            "interpreter": request["tools"]["interpreter"],
                        },
                        "packageHelper": context["controlSources"]
                        + "/packages.py",
                    },
                }
            return json.dumps(
                {
                    "request": endpoints
                    | {
                        "full": request["full"],
                        "scopePath": request["scopePath"],
                    },
                    "commands": [
                        {
                            "command": {
                                "executable": request["tools"]["cargo"],
                                "directory": context["controlSources"] + "/uv",
                                "arguments": ["build", "--locked"],
                                "environment": {
                                    "PATH": "/native/bin",
                                    "HOME": str(directory / "native-home"),
                                },
                            }
                        }
                    ],
                }
            )
        if label == "planning":
            return json.dumps(
                {
                    "plan": {
                        "comparison": BEFORE,
                        "candidate": AFTER,
                        "scope": group.SCOPE,
                        "checks": state["checks"],
                    },
                    "members": [
                        {
                            "directory": ".",
                            "name": "native-root",
                            "id": "native-opaque-id",
                        }
                    ],
                    "interpreter": str(tmp_path / "tools/python"),
                    "configuration": {
                        "configurationFile": "pyproject.toml",
                        "testPaths": [],
                        "options": [],
                    },
                    "targets": {
                        "revision": AFTER,
                        "configurationFile": "pyproject.toml",
                        "targets": [],
                    },
                    "operation": {
                        "runner": "ubuntu-latest",
                        "variant": "python-3.14",
                        "dimensions": {"python": "3.14"},
                    },
                }
            )
        if label == "execution":
            if state["defect"] == "missing-result":
                return ""
            return json.dumps(
                {
                    "candidate": AFTER,
                    "results": [],
                    "outcome": {
                        "comparison": BEFORE,
                        "candidate": AFTER,
                        "scope": group.SCOPE,
                        "satisfied": state["satisfied"],
                        "checks": state["checks"],
                        "errors": [],
                    },
                }
            )
        return ""

    monkeypatch.setattr(group.node, "run", run)
    return SimpleNamespace(
        root=root,
        directory=directory,
        context=context,
        state=state,
        calls=calls,
        output=output,
        tool_requests=tool_requests,
    )


def check(kind, target=None):
    """Supply an original selected work item."""
    return {
        "work": {
            "key": {
                "target": "actual-selected-target",
                "check": kind,
                "variant": "default",
            },
            "dimensions": {} if target is None else {"testPath": target},
        }
    }


@pytest.mark.parametrize("satisfied", [True, False])
def test_original_mixed_plan_and_native_context_reach_executor(
    flow, monkeypatch, satisfied
):
    """One planner's complete selected work reaches one finite executor."""
    flow.state.update(
        checks=[
            check("python/pytest-v1", "tests/eng/check.py"),
            check("python/distribution-set-v1"),
        ],
        satisfied=satisfied,
    )
    monkeypatch.setenv(
        "UNRELATED_RECEIVER_OVERRIDE", "must not reach execution"
    )
    readback = group.plan(flow.root, flow.directory)
    outcome = group.execute(flow.root, flow.directory)
    assert outcome["satisfied"] is satisfied
    assert [row[0] for row in flow.calls] == [
        "preparation",
        "planning",
        "execution",
    ]
    execution = next(row for row in flow.calls if row[0] == "execution")
    assert group.node.read_json(Path(execution[1][-2])) == readback["plan"]
    request = group.node.read_json(Path(execution[1][-1]))
    native = request["native"]
    for name in (
        "members",
        "configuration",
        "targets",
        "operation",
        "interpreter",
    ):
        assert native[name] == readback[name]
    assert native["checkout"] == flow.context["candidate"]["directory"]
    assert (
        native["environment"]["HOME"]
        == native["environment"]["DOTNET_CLI_HOME"]
    )
    assert "UNRELATED_RECEIVER_OVERRIDE" not in execution[2]["environment"]
    assert execution[2]["environment"] == native["environment"]
    assert execution[2]["required"] is False
    assert "plan" not in group.node.read_json(flow.directory / "readback.json")


def test_no_work_has_no_product_or_auxiliary_preparation(flow):
    """Empty work returns the original candidate-bound outcome."""
    group.plan(flow.root, flow.directory)
    assert group.execute(flow.root, flow.directory)["satisfied"] is True
    assert [row[0] for row in flow.calls] == [
        "preparation",
        "planning",
        "execution",
    ]
    assert "selected=false" in flow.output.read_text()
    assert "legacy_v3=false" in flow.output.read_text()
    assert "native_helper=false" in flow.output.read_text()
    assert "pwsh" not in flow.tool_requests


@pytest.mark.parametrize("defect", ["preparation", "missing-result"])
def test_missing_phase_result_cannot_complete_or_replan(flow, defect):
    """Failed preparation and absent execution output remain terminal."""
    flow.state["defect"] = defect
    if defect == "preparation":
        with pytest.raises(subprocess.CalledProcessError):
            group.plan(flow.root, flow.directory)
    else:
        group.plan(flow.root, flow.directory)
        with pytest.raises(ValueError, match="did not emit a result"):
            group.execute(flow.root, flow.directory)
    assert not (flow.directory / "outcome.json").exists()
    assert [x[0] for x in flow.calls].count("planning") == (
        0 if defect == "preparation" else 1
    )


@pytest.mark.parametrize("field", ["comparison", "candidate", "scope"])
def test_substituted_plan_stops_before_product_dispatch(flow, field):
    """Actual endpoint and supported scope constrain the receiving process."""
    group.plan(flow.root, flow.directory)
    plan_value = group.node.read_json(flow.directory / "plan.json")
    plan_value[field] = "foreign"
    group.node.write_json(flow.directory / "plan.json", plan_value)
    with pytest.raises(ValueError, match="different binding"):
        group.execute(flow.root, flow.directory)
    assert [row[0] for row in flow.calls] == ["preparation", "planning"]


@pytest.mark.parametrize(
    "target",
    [
        group.V3_TESTS,
        group.NATIVE_TESTS,
        group.AZURE_TESTS,
        "tests/eng/check.py",
        None,
    ],
)
@pytest.mark.parametrize("configured", [False, True])
def test_only_selected_native_target_prepares_auxiliary_tools(
    flow, tmp_path, monkeypatch, target, configured
):
    """Adapters follow selected targets and preserve owned contexts."""
    native_settings = {
        "DOTNET_ROOT": str(tmp_path / "runtime"),
        "DOTNET_ROOT_X64": str(tmp_path / "runtime-x64"),
        "DOTNET_ROOT_ARM64": str(tmp_path / "runtime-arm64"),
        "HK_PROFILE": "small,medium",
    }
    for name, value in native_settings.items():
        if configured:
            monkeypatch.setenv(name, value)
        else:
            monkeypatch.delenv(name, raising=False)
    selected = (
        check("python/distribution-set-v1")
        if target is None
        else check("python/pytest-v1", target)
    )
    flow.state["checks"] = [selected]
    group.plan(flow.root, flow.directory)
    mise_data = tmp_path / "prepared-mise"
    mise_data.mkdir()
    group.execute(flow.root, flow.directory, mise_data)
    labels = [row[0] for row in flow.calls]
    if target == group.V3_TESTS:
        assert labels == [
            "preparation",
            "planning",
            "static-reference-preparation",
            "execution",
        ]
        native = group.node.read_json(
            flow.directory / "execution-request.json"
        )["native"]
        assert native["environment"]["MISE_DATA_DIR"] == str(mise_data)
        assert native["environment"]["MISE_TASK_RUN_AUTO_INSTALL"] == "false"
        assert str(tmp_path / "tools") in native["environment"]["PATH"].split(
            os.pathsep
        )
    elif target == group.NATIVE_TESTS:
        assert labels == [
            "preparation",
            "planning",
            "native-helper-format",
            "native-helper-test",
            "native-helper-clippy",
            "execution",
        ]
        for _label, arguments, options in flow.calls[2:-1]:
            assert arguments[0] == str(tmp_path / "tools/cargo")
            assert options["environment"]["HOME"] == str(
                flow.directory / "native-home"
            )
    else:
        assert labels == ["preparation", "planning", "execution"]
    native = group.node.read_json(flow.directory / "execution-request.json")[
        "native"
    ]
    paths = native["environment"]["PATH"].split(os.pathsep)
    assert "/native/bin" in paths
    assert (str(tmp_path / "powershell") in paths) is (
        target in (group.AZURE_TESTS, group.V3_TESTS)
    )
    assert ("pwsh" in flow.tool_requests) is (
        target in (group.AZURE_TESTS, group.V3_TESTS)
    )
    for name, value in native_settings.items():
        consumer = group.V3_TESTS if name == "HK_PROFILE" else group.AZURE_TESTS
        if configured and target == consumer:
            assert native["environment"][name] == value
        else:
            assert name not in native["environment"]
        assert (
            name
            not in group.node.read_json(flow.directory / "preparation.json")[
                "request"
            ]["candidate"]["environment"]
        )
    assert (
        group.node.read_json(flow.directory / "preparation.json")["request"][
            "candidate"
        ]["environment"]["PATH"]
        == "/native/bin"
    )
    assert group.node.read_json(flow.directory / "plan.json")["checks"] == [
        selected
    ]


def test_shared_capture_replaces_environment_without_changing_default(
    tmp_path, monkeypatch
):
    """Owned launches exclude overrides; default inheritance stays usable."""
    seen = []
    monkeypatch.setenv("AMBIENT_CAPTURE_SETTING", "ordinary")
    monkeypatch.setattr(
        group.node.subprocess,
        "run",
        lambda _args, **kwargs: (
            seen.append(kwargs["env"])
            or SimpleNamespace(returncode=0, stdout=b"result", stderr=b"")
        ),
    )
    group.node.run(tmp_path, tmp_path, "ordinary", "native")
    group.node.run(
        tmp_path,
        tmp_path,
        "owned",
        "native",
        environment={"PATH": "/owned", "REMOVED": None},
    )
    assert seen[0]["AMBIENT_CAPTURE_SETTING"] == "ordinary"
    assert seen[0]["MSBUILDLOGTASKINPUTS"] == "1"
    assert seen[1] == {"PATH": "/owned"}


@pytest.mark.parametrize("invalid", ["scope", "control"])
def test_receiver_rejects_foreign_artifact_before_materialization(
    tmp_path, monkeypatch, invalid
):
    """Neither artifact may cross the receiver with a different producer."""
    directory = tmp_path / "group"
    arguments = [
        "run_python_ci_group.py",
        "--repository",
        str(tmp_path),
        "materialize",
        "--scope-file",
        str(tmp_path / "scope.json"),
        "--transfer",
        str(tmp_path / "transfer"),
        "--directory",
        str(directory),
        "--run-id",
        "20",
        "--repository-id",
        "30",
    ]
    for name, artifact, label in (
        ("scope", 10, "ci-scope"),
        ("control", 11, "ci-dotnet-plan"),
    ):
        path = tmp_path / (name + "-metadata.json")
        group.node.write_json(
            path,
            {
                "id": artifact,
                "name": label,
                "expired": False,
                "workflow_run": {
                    "id": 999 if invalid == name else 20,
                    "repository_id": 30,
                },
            },
        )
        arguments.extend(
            [
                "--" + name + "-metadata",
                str(path),
                "--" + name + "-artifact-id",
                str(artifact),
            ]
        )
    monkeypatch.setattr(sys, "argv", arguments)
    effects = []
    monkeypatch.setattr(
        group, "materialize", lambda *args: effects.append(args)
    )
    with pytest.raises(ValueError, match="different native producer"):
        group.main()
    assert effects == []
    assert not directory.exists()


@pytest.mark.parametrize(
    "kind", ["legacy_v3", "native_helper", "azure_bundle", "legacy_v3_pwsh"]
)
def test_failed_selected_auxiliary_cannot_dispatch_products(
    flow, tmp_path, monkeypatch, kind
):
    """Failed retained prerequisites remain terminal, with no replan."""
    target = {
        "legacy_v3": group.V3_TESTS,
        "native_helper": group.NATIVE_TESTS,
        "azure_bundle": group.AZURE_TESTS,
        "legacy_v3_pwsh": group.V3_TESTS,
    }[kind]
    flow.state["checks"] = [check("python/pytest-v1", target)]
    group.plan(flow.root, flow.directory)
    data = tmp_path / "mise"
    data.mkdir()
    native_run = group.node.run

    def run(root, directory, label, *args, **options):
        if label == "static-reference-preparation" or label.startswith(
            "native-helper-"
        ):
            raise subprocess.CalledProcessError(17, args)
        return native_run(root, directory, label, *args, **options)

    monkeypatch.setattr(group.node, "run", run)
    if kind in ("azure_bundle", "legacy_v3_pwsh"):
        native_executable = group.executable

        def executable(name):
            if name == "pwsh":
                message = "pwsh"
                raise FileNotFoundError(message)
            return native_executable(name)

        monkeypatch.setattr(group, "executable", executable)
        with pytest.raises(FileNotFoundError, match="pwsh"):
            group.execute(flow.root, flow.directory, data)
    else:
        with pytest.raises(subprocess.CalledProcessError) as error:
            group.execute(flow.root, flow.directory, data)
        assert error.value.returncode == 17
    assert [row[0] for row in flow.calls] == ["preparation", "planning"]
    assert not (flow.directory / "result.json").exists()
    assert not (flow.directory / "outcome.json").exists()


def test_execution_retains_prepared_sdk_after_late_path_change(
    flow, monkeypatch
):
    """Late adapter activation cannot replace the captured managed receiver."""
    flow.state["checks"] = [check("python/pytest-v1", "tests/eng/check.py")]
    group.plan(flow.root, flow.directory)
    tools = group.node.read_json(flow.directory / "preparation-request.json")[
        "tools"
    ]
    late = str(flow.directory / "late-mise-shims/dotnet")
    monkeypatch.setattr(group, "executable", lambda _name: late)
    monkeypatch.setenv("PATH", str(Path(late).parent))
    outcome = group.execute(flow.root, flow.directory)
    assert outcome["satisfied"] is True
    execution = flow.calls[-1]
    assert execution[0] == "execution"
    assert execution[1][0] == tools["dotnet"]
    assert execution[1][0] != late
    assert execution[2]["environment"]["PATH"] == "/native/bin"
    assert (
        execution[2]["environment"]["HOME"]
        == (execution[2]["environment"]["DOTNET_CLI_HOME"])
    )
