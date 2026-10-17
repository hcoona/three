"""One mise runtime authority for local and CI consumers."""

import importlib.util
import json
import os
import shlex
import shutil
import subprocess
import tomllib
from pathlib import Path

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
    version_file = tmp_path / ".python-version"
    version_file.write_text("3.13.12\n")
    with pytest.raises(ValueError, match="sync:python-version"):
        runtime.synchronize(tmp_path)
    assert version_file.read_text() == "3.13.12\n"
    runtime.synchronize(tmp_path, write=True)
    assert runtime.synchronize(tmp_path) == "3.14.3"
    (tmp_path / "mise.toml").write_text('[tools]\npython = "3.15"\n')
    with pytest.raises(ValueError, match="disagrees"):
        runtime.synchronize(tmp_path, write=True)
    assert (tmp_path / ".python-version").read_text() == "3.14.3\n"


def test_python_tasks_preserve_exact_preparation_and_full_runner():
    """Documented tasks cannot retain unrelated workspace executables."""
    tasks = tomllib.loads((ROOT / "mise.toml").read_text())["tasks"]
    assert shlex.split(tasks["test:python"]["run"]) == [
        "bash",
        "eng/scripts/test-python.sh",
    ]
    assert "depends" not in tasks["test:python"]


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


def test_uv_workflow_and_renovate_consumers_use_mise_projection():
    """Updates flow from mise to actual setup-uv consumers."""
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
        assert {config_path, ".python-version"} <= set(tasks["fileFilters"])


@pytest.mark.parametrize(
    "defect", ["dirty", "option", "state", "source", "outside"]
)
def test_native_full_launcher_rejects_invalid_source_before_bootstrap(
    tmp_path, defect
):
    """Reject invalid committed-source requests before bootstrap effects."""
    root = tmp_path / "source"
    root.mkdir()
    native_git = shutil.which("git")
    native_bash = shutil.which("bash")
    assert native_git is not None
    assert native_bash is not None

    def git(*arguments):
        subprocess.run(  # noqa: S603 - Native Git and this test's owned fixture.
            [native_git, "-C", str(root), *arguments],
            check=True,
        )

    git("init", "-q")
    git("config", "user.name", "Fixture")
    git("config", "user.email", "fixture@example.invalid")
    source = root / "source.py"
    source.write_text("committed = True\n")
    git("add", "source.py")
    git(
        "-c",
        "core.hooksPath=/dev/null",
        "-c",
        "commit.gpgSign=false",
        "commit",
        "-qm",
        "Fixture",
    )
    state = tmp_path / "state"
    arguments = ["--repository", str(root), "--directory", str(state)]
    if defect == "dirty":
        source.write_text("committed = False\n")
    elif defect == "option":
        arguments.append("--from-ref=HEAD")
    elif defect in {"state", "outside"}:
        state.mkdir()
    elif defect == "source":
        arguments[-1] = str(root / "state")
    result = subprocess.run(  # noqa: S603 - Exact native launcher and fixture.
        [native_bash, str(ROOT / "eng/scripts/test-python.sh"), *arguments],
        capture_output=True,
        text=True,
        check=False,
        cwd=tmp_path if defect == "outside" else root,
    )
    assert result.returncode == 2, result.stderr
    assert not (state / "control-checkout").exists()
    assert not (root / "state").exists()


def test_native_full_launcher_reports_complete_worktree_cleanup(tmp_path):
    """Print removal of every registered worktree, even after a failure."""
    root = tmp_path / "source"
    root.mkdir()
    native_git = shutil.which("git")
    native_bash = shutil.which("bash")
    assert native_git is not None
    assert native_bash is not None

    def git(*arguments):
        return subprocess.run(  # noqa: S603 - Native Git and this test's owned fixture.
            [native_git, "-C", str(root), *arguments],
            check=True,
            capture_output=True,
            text=True,
        ).stdout

    git("init", "-q")
    git("config", "user.name", "Fixture")
    git("config", "user.email", "fixture@example.invalid")
    (root / "source.py").write_text("committed = True\n")
    git("add", "source.py")
    git(
        "-c",
        "core.hooksPath=/dev/null",
        "-c",
        "commit.gpgSign=false",
        "commit",
        "-qm",
        "Fixture",
    )
    # The bootstrap stand-in registers a group endpoint worktree, then fails.
    tools = tmp_path / "tools"
    tools.mkdir()
    pwsh = tools / "pwsh"
    pwsh.write_text(
        "#!/usr/bin/env bash\n"
        'state="$(dirname "$PWD")"\n'
        f'"{native_git}" -C "{root}" worktree add -q --detach '
        '"$state/group/basis" HEAD\n'
        "exit 3\n"
    )
    pwsh.chmod(0o755)
    state = (tmp_path / "state").resolve()
    path = f"{tools}{os.pathsep}{os.environ['PATH']}"
    environment = {**os.environ, "PATH": path}
    result = subprocess.run(  # noqa: S603 - Exact native launcher and fixture.
        [
            native_bash,
            str(ROOT / "eng/scripts/test-python.sh"),
            "--repository",
            str(root),
            "--directory",
            str(state),
        ],
        capture_output=True,
        text=True,
        check=False,
        cwd=root,
        env=environment,
    )
    assert result.returncode == 3, result.stderr
    removal = next(
        line.removeprefix("Remove it when finished: ")
        for line in result.stdout.splitlines()
        if line.startswith("Remove it when finished: ")
    )
    assert str(state / "control-checkout") in removal
    assert str(state / "group" / "basis") in removal
    assert removal.endswith(f"rm -rf {state}")
    subprocess.run([native_bash, "-c", removal], check=True)  # noqa: S603
    assert not state.exists()
    listed = git("worktree", "list", "--porcelain")
    assert str(state) not in listed
