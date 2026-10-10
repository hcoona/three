"""One mise runtime authority for local, CI and installed package consumers."""

import importlib.util
import json
import shlex
import sys
import tomllib
from pathlib import Path
from types import SimpleNamespace

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "sync_python_version", ROOT / "eng/scripts/sync_python_version.py"
)
assert SPEC is not None
assert SPEC.loader is not None
runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtime)


@pytest.mark.parametrize("uv", ["0.10.9", "0.12.19"])
def test_runtime_projection_checks_drift_without_rewriting(tmp_path, uv):
    """A tool update requires explicit regeneration of both consumed files."""
    (tmp_path / "mise.toml").write_text(
        f'[tools]\npython = "3.14"\nuv = "{uv}"\n'
    )
    (tmp_path / "mise.lock").write_text(
        '[[tools.python]]\nversion = "3.14.3"\n'
        f'[[tools.uv]]\nversion = "{uv}"\n'
    )
    assert runtime.synchronize(tmp_path, write=True) == "3.14.3"
    assert (tmp_path / ".python-version").read_text() == "3.14.3\n"
    package = tmp_path / runtime.PACKAGE_RUNTIME
    assert 'PYTHON_VERSION = "3.14.3"' in package.read_text()
    assert f'UV_VERSION = "{uv}"' in package.read_text()
    assert tomllib.loads((tmp_path / runtime.UV_CONFIG).read_text()) == {
        "required-version": f"=={uv}"
    }
    package.write_text('PYTHON_VERSION = "3.13.12"\n')
    with pytest.raises(ValueError, match="sync:python-version"):
        runtime.synchronize(tmp_path)
    assert package.read_text() == 'PYTHON_VERSION = "3.13.12"\n'
    runtime.synchronize(tmp_path, write=True)
    assert runtime.synchronize(tmp_path) == "3.14.3"
    (tmp_path / "mise.toml").write_text('[tools]\npython = "3.15"\n')
    with pytest.raises(ValueError, match="disagrees"):
        runtime.synchronize(tmp_path, write=True)
    assert (tmp_path / ".python-version").read_text() == "3.14.3\n"


@pytest.fixture
def test_runner(monkeypatch):
    """Load the actual bounded runner with its existing selector import."""
    monkeypatch.syspath_prepend(str(ROOT / "eng/scripts"))
    spec = importlib.util.spec_from_file_location(
        "run_python_tests", ROOT / "eng/scripts/run_python_tests.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def committed_full_runner(test_runner, monkeypatch, tmp_path):
    """Use real committed Git source and substitute costly native phases."""
    root = tmp_path / "source"
    root.mkdir()
    (root / "source.py").write_text("original = True\n")

    def git(*args):
        return test_runner.ci_scope.git(
            root,
            "-c",
            "core.hooksPath=/dev/null",
            "-c",
            "user.name=Full Caller",
            "-c",
            "user.email=full@example.invalid",
            *args,
        ).strip()

    git("init", "-q")
    git("add", ".")
    git("commit", "-qm", "Commit full caller input")
    candidate = git("rev-parse", "HEAD")
    directory = tmp_path / "results"
    data = tmp_path / "mise-data"
    data.mkdir()
    monkeypatch.setattr(sys, "platform", "linux")
    monkeypatch.setattr(
        sys,
        "argv",
        [
            "run_python_tests.py",
            "--repository",
            str(root),
            "--directory",
            str(directory),
            "--mise-data-directory",
            str(data),
        ],
    )
    calls = []
    state = {"satisfied": True, "failure": False}

    def materialize(receiving, scope, transfer, owned):
        value = test_runner.group.node.read_json(scope)
        calls.append(("materialize", receiving, value, transfer, owned))
        assert value["base"] == value["candidate"] == candidate
        assert value["full"] is True
        assert value["changed_paths"] == []
        assert value["endpoint_owners"] == {
            name: {"revision": candidate, "paths": []}
            for name in ("basis", "candidate")
        }

    def plan(_root, _directory):
        calls.append(("plan",))
        if state["failure"]:
            message = "Native preparation failed"
            raise ValueError(message)

    def execute(_root, owned, mise):
        calls.append(("execute", owned, mise))
        return {"satisfied": state["satisfied"]}

    monkeypatch.setattr(
        test_runner.group.node, "run", lambda *args: calls.append(("git", args))
    )
    monkeypatch.setattr(
        test_runner.dotnet,
        "bootstrap",
        lambda *args: calls.append(("bootstrap", args)),
    )
    monkeypatch.setattr(test_runner.group, "materialize", materialize)
    monkeypatch.setattr(test_runner.group, "plan", plan)
    monkeypatch.setattr(test_runner.group, "execute", execute)
    return SimpleNamespace(
        root=root,
        candidate=candidate,
        directory=directory,
        data=data,
        state=state,
        calls=calls,
        runner=test_runner,
    )


@pytest.mark.parametrize("satisfied", [True, False])
def test_full_python_runner_uses_committed_native_group(
    committed_full_runner, satisfied, capsys
):
    """Full means the original mixed group at the named committed candidate."""
    fixture = committed_full_runner
    fixture.state["satisfied"] = satisfied
    assert fixture.runner.main() == (0 if satisfied else 1)
    assert fixture.candidate in capsys.readouterr().out
    assert [row[0] for row in fixture.calls] == [
        "git",
        "bootstrap",
        "materialize",
        "plan",
        "execute",
    ]
    assert fixture.calls[-1][1:] == (fixture.directory / "group", fixture.data)


def test_full_python_runner_dirty_source_fails_before_preparation(
    committed_full_runner,
):
    """The committed-source claim cannot silently omit tracked edits."""
    fixture = committed_full_runner
    (fixture.root / "source.py").write_text("original = False\n")
    with pytest.raises(SystemExit) as error:
        fixture.runner.main()
    assert error.value.code == 2
    assert fixture.calls == []
    assert not fixture.directory.exists()


def test_full_python_runner_native_failure_cannot_be_success(
    committed_full_runner,
):
    """Failed query installation never becomes an empty or full fallback."""
    fixture = committed_full_runner
    fixture.state["failure"] = True
    with pytest.raises(ValueError, match="Native preparation failed"):
        fixture.runner.main()
    assert [row[0] for row in fixture.calls] == [
        "git",
        "bootstrap",
        "materialize",
        "plan",
    ]


@pytest.mark.parametrize(
    "option", ["-q", "--junitxml=original.xml", "--from-ref=HEAD"]
)
def test_full_python_runner_rejects_unsupported_options(
    committed_full_runner, monkeypatch, option
):
    """Unsupported pytest/selection options cannot silently narrow full."""
    fixture = committed_full_runner
    monkeypatch.setattr(sys, "argv", [*sys.argv, option])
    with pytest.raises(SystemExit) as error:
        fixture.runner.main()
    assert error.value.code == 2
    assert fixture.calls == []
    assert not fixture.directory.exists()


def test_python_tasks_preserve_exact_preparation_and_full_runner():
    """Documented tasks cannot retain unrelated workspace executables."""
    tasks = tomllib.loads((ROOT / "mise.toml").read_text())["tasks"]
    assert shlex.split(tasks["test:python"]["run"]) == [
        "python",
        "eng/scripts/run_python_tests.py",
    ]
    assert shlex.split(tasks["test:v3"]["run"]) == [
        "uv",
        "run",
        "--exact",
        "--frozen",
        "--package",
        "three-workflow-delivery-v3",
        "pytest",
        "src/public/lib/three-workflow-delivery-v3/tests",
    ]
    assert "depends" not in tasks["test:python"]
    assert tasks["test:v3"]["depends"] == [
        "prepare:static-reference-authorities"
    ]


@pytest.mark.parametrize(
    "uv_entries",
    [
        "",
        '[[tools.uv]]\nversion = "0.12.19"\n' * 2,
        '[[tools.uv]]\nversion = "latest"\n',
        '[[tools.uv]]\nversion = "0.12.18"\n',
    ],
)
def test_uv_projection_rejects_invalid_lock_before_writes(tmp_path, uv_entries):
    """A broken resolution cannot partially replace existing projections."""
    (tmp_path / "mise.toml").write_text(
        '[tools]\npython = "3.14"\nuv = "0.12.19"\n'
    )
    (tmp_path / "mise.lock").write_text(
        '[[tools.python]]\nversion = "3.14.3"\n' + uv_entries
    )
    version_file = tmp_path / ".python-version"
    version_file.write_text("previous\n")
    with pytest.raises(ValueError, match="UV"):
        runtime.synchronize(tmp_path, write=True)
    assert version_file.read_text() == "previous\n"
    assert not (tmp_path / runtime.UV_CONFIG).exists()
    assert not (tmp_path / runtime.PACKAGE_RUNTIME).exists()


def test_uv_workflow_and_renovate_consumers_use_mise_projection():
    """Updates flow from mise to setup-uv and packaged runtime together."""
    runtime.synchronize(ROOT)
    config_path = runtime.UV_CONFIG.as_posix()
    workflow_paths = [
        ROOT / ".github/workflows/ci.yml",
        *sorted(
            (ROOT / ".github/workflows").glob("workflow-delivery-v3-*.yml")
        ),
    ]
    for path in workflow_paths:
        document = yaml.safe_load(path.read_text())
        for name, job in document["jobs"].items():
            for step in job.get("steps", []):
                if not step.get("uses", "").startswith("astral-sh/setup-uv@"):
                    continue
                if path.name == "ci.yml" and name != "python-tests":
                    continue
                # setup-uv resolves version-file relative to working-directory,
                # including the existing nested tooling checkout in NuGet jobs.
                assert step["with"]["version-file"] == config_path
                assert "version" not in step["with"]
    action = yaml.safe_load(
        (
            ROOT
            / ".github/actions/workflow-delivery-v3-python-setup/action.yml"
        ).read_text()
    )
    setup = next(
        step
        for step in action["runs"]["steps"]
        if step.get("uses", "").startswith("astral-sh/setup-uv@")
    )
    assert setup["with"]["version-file"] == config_path
    assert "version" not in setup["with"]
    assert "python-version" not in setup["with"]
    rules = json.loads((ROOT / "renovate.json").read_text())["packageRules"]
    uv_rule = next(rule for rule in rules if rule.get("groupName") == "Mise uv")
    maintenance = next(
        rule
        for rule in rules
        if rule.get("matchManagers") == ["mise"]
        and rule.get("matchUpdateTypes") == ["lockFileMaintenance"]
    )
    for rule in (uv_rule, maintenance):
        tasks = rule["postUpgradeTasks"]
        assert (
            "uv run --no-project python "
            "eng/scripts/sync_python_version.py --write" in tasks["commands"]
        )
        assert {config_path, runtime.PACKAGE_RUNTIME.as_posix()} <= set(
            tasks["fileFilters"]
        )
