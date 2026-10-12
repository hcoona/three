"""Retained Ruby Release actions and common native runtime preparation."""

import json
import os
import shutil
from pathlib import Path

import yaml
from three_workflow_delivery_v3._ruby_native import (
    ruby_executable,
    ruby_profile,
)
from three_workflow_delivery_v3.repository.dotnet_provider import (
    neutral_dotnet_environment,
    run_native,
)

from .ruby_fixtures import repository

_ROOT = Path(__file__).resolve().parents[5]
_SETUP = _ROOT / ".github/actions/workflow-delivery-v3-ruby-setup/action.yml"


def test_ruby_setup_locks_control_dependencies_without_project_install():
    """Setup cannot execute smoke installation or retain auth material."""
    steps = yaml.safe_load(_SETUP.read_text())["runs"]["steps"]
    uv = next(
        step
        for step in steps
        if step.get("uses", "").startswith("astral-sh/setup-uv@")
    )
    ruby = next(
        step
        for step in steps
        if step.get("uses", "").startswith("ruby/setup-ruby@")
    )
    assert uv["with"]["version-file"] == ".config/uv/uv.toml"
    assert uv["with"]["github-token"] == ""
    assert ruby["with"]["ruby-version"] == "4.0.7"
    assert ruby["with"]["bundler-cache"] is False
    commands = "\n".join(step.get("run", "") for step in steps)
    assert (
        "uv sync --frozen --package three-workflow-delivery-v3 --no-dev"
        in commands
    )
    assert "GITHUB_PATH" in commands
    assert "gem install" not in commands
    assert "bundle install" not in commands
    assert "GITHUB_TOKEN" not in commands
    assert "ACTIONS_ID_TOKEN" not in commands


def test_common_python_ci_keeps_native_ruby_ahead_of_late_mise_shim(
    tmp_path, monkeypatch
):
    """Later shim creation cannot replace the installed native interpreter."""
    native = ruby_executable()
    mise = shutil.which("mise")
    assert mise is not None
    steps = yaml.safe_load((_ROOT / ".github/workflows/ci.yml").read_text())[
        "jobs"
    ]["python-tests"]["steps"]
    shims = tmp_path / "mise-shims"
    shims.mkdir()
    # Setup actions prepend their tool directories. Use the actual workflow
    # ordering while keeping the competing shim entirely inside this fixture.
    path = []
    for step in steps:
        action = step.get("uses", "").split("@", 1)[0]
        if action == "ruby/setup-ruby":
            assert step["with"]["ruby-version"] == "4.0.7"
            path.insert(0, str(native.parent))
        elif action == "jdx/mise-action":
            path.insert(0, str(shims))
    assert set(path) == {str(native.parent), str(shims)}
    monkeypatch.setenv("PATH", os.pathsep.join(path))
    before = ruby_profile(cwd=tmp_path, home=tmp_path / "before-home")
    (shims / "ruby").symlink_to(Path(mise).resolve())
    after = ruby_profile(cwd=tmp_path, home=tmp_path / "after-home")
    assert ruby_executable() == native
    assert after == before
    assert (after["ruby"], after["rubygems"]) == ("4.0.7", "4.0.20")


def test_common_python_ci_keeps_native_nbgv_ahead_of_late_mise_shim(tmp_path):
    """Late mise shims cannot replace the SDK used by neutral native NBGV."""
    executable = shutil.which("dotnet")
    mise = shutil.which("mise")
    assert executable is not None
    assert mise is not None
    native = Path(executable).resolve()
    source, _, _ = repository(tmp_path / "repository")
    expected = json.loads((source / ".config/dotnet-tools.json").read_text())[
        "tools"
    ]["nbgv"]["version"]
    steps = yaml.safe_load((_ROOT / ".github/workflows/ci.yml").read_text())[
        "jobs"
    ]["python-tests"]["steps"]
    shims = tmp_path / "mise-shims"
    shims.mkdir()
    materialize = next(
        i for i, step in enumerate(steps) if step.get("id") == "endpoints"
    )
    plan = next(i for i, step in enumerate(steps) if step.get("id") == "plan")
    sdks = [
        i
        for i, step in enumerate(steps)
        if step.get("uses", "").startswith("actions/setup-dotnet@")
    ]
    mise_index = next(
        i
        for i, step in enumerate(steps)
        if step.get("uses", "").startswith("jdx/mise-action@")
    )
    assert all(materialize < i < plan < mise_index for i in sdks)
    assert all(
        "steps.endpoints.outputs." in steps[i]["with"]["global-json-file"]
        for i in sdks
    )
    environment = neutral_dotnet_environment()
    environment["PATH"] = os.pathsep.join(
        [str(shims), str(native.parent), os.defpath]
    )
    # Preparation captures the native SDK before selected mise setup; subsequent
    # execution reuses its absolute path even when ambient resolution changes.
    before = run_native((str(native), "nbgv", "--version"), source, environment)
    (shims / "dotnet").symlink_to(Path(mise).resolve())
    assert (
        Path(shutil.which("dotnet", path=environment["PATH"])).resolve()
        != native
    )
    after = run_native((str(native), "nbgv", "--version"), source, environment)
    assert after == before
    assert after.strip().split("+", 1)[0] == expected
