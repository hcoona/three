"""One mise runtime authority for local and CI consumers."""

import json
import os
import re
import shlex
import shutil
import subprocess
import tomllib
from pathlib import Path

import pytest
import yaml

from .ci_workflow_shell import (
    action_steps,
    effective_env,
    load_script,
    output_values,
    resolve,
    run_step,
)

ROOT = Path(__file__).resolve().parents[2]
runtime = load_script(ROOT / "eng/scripts/sync_python_version.py")


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


# Tools whose exact committed version a workflow could duplicate. Go and Java
# lock entries are not exact release versions, so only their keys are guarded.
COMMITTED_TOOLS = ("dotnet", "node", "pnpm", "python", "ruby", "uv", "pkl")
SETUP_VERSION_INPUTS = {
    "actions/setup-dotnet": "dotnet-version",
    "actions/setup-go": "go-version",
    "actions/setup-java": "java-version",
    "actions/setup-node": "node-version",
    "actions/setup-python": "python-version",
    "ruby/setup-ruby": "ruby-version",
    "pnpm/action-setup": "version",
    "astral-sh/setup-uv": "version",
    "pkl-community/setup-pkl": "pkl-version",
}
VERSION_KEY = re.compile(
    r"(?i)(?:\A|[_-])(NODE|PYTHON|RUBY|PNPM|UV|DOTNET|PKL|GO|JAVA|POWERSHELL)"
    r"[\w-]*VERSION\Z"
)
NUMERIC = re.compile(r"\A\d+(?:\.(?:\d+|x|\*))*\Z")
locked = load_script(ROOT / "eng/scripts/locked_tool_versions.py")


def test_framework_provider_does_not_select_the_native_sdk(tmp_path):
    """Installation capability and the native compiler use distinct inputs."""
    (tmp_path / "global.json").write_text(
        json.dumps({"sdk": {"version": "11.2.3"}})
    )
    profile = tmp_path / "eng/dotnet-frameworks"
    profile.mkdir(parents=True)
    (profile / "mise.lock").write_text('[[tools.dotnet]]\nversion = "10.4.5"\n')
    assert locked.locked_version(tmp_path, "dotnet") == "11.2.3"
    assert locked.locked_version(tmp_path, "dotnet-frameworks") == "10.4.5"


def guarded_workflows():
    """Workflows whose tool versions committed configuration owns."""
    return sorted((ROOT / ".github/workflows").glob("*.yml"))


def _version_keys(node):
    """Yield literal numeric values of tool-version keys anywhere in YAML."""
    if isinstance(node, dict):
        for key, value in node.items():
            if VERSION_KEY.search(str(key)):
                values = value if isinstance(value, list) else [value]
                yield from (
                    f"{key} {item}"
                    for item in values
                    if NUMERIC.fullmatch(str(item).strip())
                )
            yield from _version_keys(value)
    elif isinstance(node, list):
        for item in node:
            yield from _version_keys(item)


def literal_tool_versions(text, document):
    """Find tool/runtime selections written as literals in one workflow."""
    found = []
    for tool in COMMITTED_TOOLS:
        version = locked.locked_version(ROOT, tool)
        if re.search(rf"(?<![\w.]){re.escape(version)}(?![\w.])", text):
            found.append(f"{tool} {version}")
        found.extend(
            match[0]
            for match in re.finditer(rf"(?<![\w/.-]){tool}@\d[\w.]*", text)
        )
    for job in document.get("jobs", {}).values():
        for step in job.get("steps", []):
            action = step.get("uses", "").split("@", 1)[0]
            value = step.get("with", {}).get(SETUP_VERSION_INPUTS.get(action))
            if value is not None and not str(value).startswith("$" + "{{"):
                found.append(f"{action} {value}")
    found.extend(_version_keys(document))
    return sorted(set(found))


def test_workflows_take_tool_versions_from_committed_configuration():
    """A routine tool update edits only mise.lock or global.json."""
    for path in guarded_workflows():
        text = path.read_text()
        found = literal_tool_versions(text, yaml.safe_load(text))
        assert found == [], path


@pytest.mark.parametrize(
    ("name", "actions"),
    [
        (
            "conformance",
            (
                ("actions/setup-node", "node"),
                ("pnpm/action-setup", "pnpm"),
                ("ruby/setup-ruby", "ruby"),
            ),
        ),
        ("node-tests", (("pnpm/action-setup", "pnpm"),)),
        ("ruby-group", (("ruby/setup-ruby", "ruby"),)),
    ],
)
def test_ci_setup_actions_consume_committed_versions(tmp_path, name, actions):
    """Setup actions consume the intended tool's version from the lock."""
    locked_tools = tomllib.loads((ROOT / "mise.lock").read_text())["tools"]
    workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
    job = workflow["jobs"][name]
    reader = next(s for s in job["steps"] if s.get("id") == "tools")
    output = tmp_path / "versions"
    result = run_step(
        reader, cwd=ROOT, env={"GITHUB_OUTPUT": str(output)}, bindings={}
    )
    assert result.returncode == 0, result.stderr
    bindings = {
        f"steps.tools.outputs.{key}": value
        for key, value in output_values(output).items()
    }
    for action, tool in actions:
        (step,) = action_steps(job, action)
        (version,) = {entry["version"] for entry in locked_tools[tool]}
        value = step["with"][SETUP_VERSION_INPUTS[action]]
        assert resolve(value, bindings) == version


def _read_scope_provider(cwd, output):
    workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
    scope = workflow["jobs"]["scope"]
    reader = next(s for s in scope["steps"] if s.get("id") == "tools")
    result = run_step(
        reader,
        cwd=cwd,
        env={"GITHUB_OUTPUT": str(output)},
        bindings={},
        workflow=workflow,
        job=scope,
    )
    return workflow["jobs"], result


def test_framework_provider_reaches_every_native_sdk_root(tmp_path):
    """Prepare existing frameworks without replacing native SDK selection."""
    output = tmp_path / "outputs"
    jobs, result = _read_scope_provider(ROOT, output)
    assert result.returncode == 0, result.stderr
    provider = output_values(output)["dotnet-frameworks"]
    lock = ROOT / "eng/dotnet-frameworks/mise.lock"
    (expected,) = {
        entry["version"]
        for entry in tomllib.loads(lock.read_text())["tools"]["dotnet"]
    }
    assert provider == expected
    assert jobs["scope"]["outputs"]["dotnet_frameworks"] == (
        "${{ steps.tools.outputs.dotnet-frameworks }}"
    )
    bindings = {
        "steps.tools.outputs.dotnet-frameworks": provider,
        "needs.scope.outputs.dotnet_frameworks": provider,
    }
    for job in jobs.values():
        for step in action_steps(job, "actions/setup-dotnet"):
            assert resolve(step["with"]["dotnet-version"], bindings) == provider
            assert "global-json-file" in step["with"]


def test_standalone_delivery_workflows_provision_framework_provider():
    """Standalone Workflow Delivery v3 workflows provision locked .NET 10."""
    sdk = locked.locked_version(ROOT, "dotnet")
    provider = locked.locked_version(ROOT, "dotnet-frameworks")
    bindings = {
        "steps.tools.outputs.dotnet": sdk,
        "steps.tools.outputs.dotnet-frameworks": provider,
        "needs.prepare.outputs.dotnet": sdk,
        "needs.prepare.outputs.dotnet-frameworks": provider,
    }
    workflows_dir = ROOT / ".github/workflows"
    repro_path = (
        workflows_dir / "workflow-delivery-v3-nuget-reproducibility.yml"
    )
    repro = yaml.safe_load(repro_path.read_text())
    push_paths = set(repro[True]["push"]["paths"])
    assert {
        "eng/dotnet-frameworks/mise.toml",
        "eng/dotnet-frameworks/mise.lock",
        "eng/scripts/locked_tool_versions.py",
    } <= push_paths
    (repro_job,) = repro["jobs"].values()
    repro_reader = next(
        step for step in repro_job["steps"] if step.get("id") == "tools"
    )
    assert "locked_tool_versions.py dotnet-frameworks" in repro_reader["run"]
    (setup_step,) = action_steps(repro_job, "actions/setup-dotnet")
    assert resolve(setup_step["with"]["dotnet-version"], bindings) == provider
    assert setup_step["with"]["global-json-file"] == "global.json"

    release_workflows = sorted(
        workflows_dir.glob("workflow-delivery-v3-*-release.yml")
    )
    assert len(release_workflows) == 6
    for path in release_workflows:
        workflow = yaml.safe_load(path.read_text())
        prepare = workflow["jobs"]["prepare"]
        reader = next(
            step for step in prepare["steps"] if step.get("id") == "tools"
        )
        assert "locked_tool_versions.py" in reader["run"]
        for required in ("dotnet", "dotnet-frameworks"):
            assert re.search(rf"\b{re.escape(required)}\b", reader["run"]), (
                path,
                required,
            )
        assert resolve(prepare["outputs"]["dotnet"], bindings) == sdk, path
        assert (
            resolve(prepare["outputs"]["dotnet-frameworks"], bindings)
            == provider
        ), path
        for job_name, job in workflow["jobs"].items():
            mise_actions = action_steps(job, "jdx/mise-action")
            assert len(mise_actions) == 1, (path, job_name)
            install_args = mise_actions[0].get("with", {}).get("install_args")
            if install_args is not None:
                tokens = shlex.split(install_args)
                assert tokens[:2] == ["--jobs", "1"], (path, job_name)
                assert "dotnet" in tokens, (path, job_name)
                assert any(
                    "mise install --monorepo --locked --jobs 1 dotnet"
                    in step.get("run", "")
                    for step in job["steps"]
                ), (path, job_name)
                continue
            install_step = next(
                step
                for step in job["steps"]
                if "mise install" in step.get("run", "")
            )
            merged_env = effective_env(workflow, job, install_step)
            assert (
                resolve(str(merged_env["DOTNET_VERSION"]), bindings) == sdk
            ), (path, job_name)
            assert (
                resolve(str(merged_env["DOTNET_FRAMEWORKS_VERSION"]), bindings)
                == provider
            ), (path, job_name)
            run_text = install_step["run"]
            assert "mise install --jobs 1 --yes" in run_text, (path, job_name)
            assert re.search(r"dotnet@\$(?:env:)?DOTNET_VERSION\b", run_text), (
                path,
                job_name,
            )
            assert re.search(
                r"dotnet@\$(?:env:)?DOTNET_FRAMEWORKS_VERSION\b", run_text
            ), (path, job_name)


@pytest.mark.parametrize("defect", ["missing", "malformed"])
def test_scope_framework_metadata_cannot_fall_back_to_native_sdk(
    tmp_path, defect
):
    """Reject missing provider metadata before dependent setup can run."""
    script = tmp_path / "eng/scripts/locked_tool_versions.py"
    script.parent.mkdir(parents=True)
    shutil.copyfile(ROOT / "eng/scripts/locked_tool_versions.py", script)
    (tmp_path / "global.json").write_text('{"sdk": {"version": "11.2.3"}}')
    if defect == "malformed":
        profile = tmp_path / "eng/dotnet-frameworks/mise.lock"
        profile.parent.mkdir(parents=True)
        profile.write_text("version =\n")
    output = tmp_path / "outputs"
    _, result = _read_scope_provider(tmp_path, output)
    assert result.returncode != 0
    assert output.read_text() == ""


def _step(uses, inputs):
    step = f"      - uses: {uses}\n        with:\n" if uses else "      -\n"
    return "jobs:\n  job:\n    steps:\n" + step + inputs


@pytest.mark.parametrize(
    "text",
    [
        _step("actions/setup-node@v7", "          node-version: 24\n"),
        _step("ruby/setup-ruby@v1", "          ruby-version: '4.0'\n"),
        _step("pnpm/action-setup@v6", "          version: '{pnpm}'\n"),
        _step("actions/setup-go@v7", "          go-version: '1.*'\n"),
        _step("actions/setup-java@v6", "          java-version: '25'\n"),
        _step(None, "        run: mise install --yes dotnet@{dotnet}\n"),
        _step(None, "        run: mise which uv@0.1.0\n"),
        "env:\n  RELEASE_NODE_VERSION: '24.19.0'\njobs: {{}}\n",
        "jobs:\n  job:\n    env:\n      PYTHON_VERSION: 3.13\n    steps: []\n",
        (
            "jobs:\n  job:\n    strategy:\n      matrix:\n"
            "        node-version: [22.x, 24.x]\n    steps: []\n"
        ),
    ],
)
def test_tool_version_guard_rejects_literals(text):
    """The guard reports setup inputs, version keys and mise selectors."""
    text = text.format(
        **{
            tool: locked.locked_version(ROOT, tool)
            for tool in ("pnpm", "dotnet")
        }
    )
    assert literal_tool_versions(text, yaml.safe_load(text))


@pytest.mark.parametrize(
    "text",
    [
        _step(
            "actions/setup-node@v7",
            "          node-version: $" + "{{ steps.tools.outputs.node }}\n",
        ),
        "env:\n  NODE_VERSION: $" + "{{ needs.prepare.outputs.node }}\n"
        "jobs: {}\n",
        _step("jdx/mise-action@v5", "          version: '2026.10.11'\n"),
        "env:\n  RENOVATE_VERSION: 43.200.1\njobs: {}\n",
        _step("actions/setup-python@v7", "          python-version-file: x\n"),
    ],
)
def test_tool_version_guard_accepts_committed_and_unrelated_values(text):
    """Derived tool versions and versions of unrelated tools remain allowed."""
    assert literal_tool_versions(text, yaml.safe_load(text)) == []


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
        subprocess.run(  # noqa: S603 - Native Git on an isolated fixture.
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
        return subprocess.run(  # noqa: S603 - Native Git on a fixture.
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
    # A path Git would C-quote in plain porcelain output must still match.
    state = (tmp_path / 'st "äte').resolve()
    path = f"{tools}{os.pathsep}{os.environ['PATH']}"
    # A UTF-8 locale keeps bash's %q output literal for shlex.
    environment = {
        **os.environ,
        "PATH": path,
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
    }
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
    # Shell-quoted words must reproduce the literal worktree paths.
    words = shlex.split(removal)
    assert str(state / "control-checkout") in words
    assert str(state / "group" / "basis") in words
    assert words[-3:] == ["rm", "-rf", str(state)]
    subprocess.run([native_bash, "-c", removal], check=True)  # noqa: S603
    assert not state.exists()
    listed = git("worktree", "list", "--porcelain")
    assert str(state) not in listed
