"""One mise runtime authority for local and CI consumers."""

import importlib.util
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


# Retired campaign callers.
UNGUARDED_WORKFLOWS = {
    "workflow-delivery-v3-nuget-live-attempt.yml",
}
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
# The Node CI matrix is the retained packages' engines compatibility roster,
# not a tool selection; the committed Node selection must occur in it.
COMPATIBILITY_MATRICES = {
    ("ci.yml", ("jobs", "node-tests", "strategy", "matrix", "node-version")),
}
VERSIONS = importlib.util.spec_from_file_location(
    "locked_tool_versions", ROOT / "eng/scripts/locked_tool_versions.py"
)
assert VERSIONS is not None
assert VERSIONS.loader is not None
locked = importlib.util.module_from_spec(VERSIONS)
VERSIONS.loader.exec_module(locked)


def guarded_workflows():
    """Workflows whose tool versions committed configuration owns."""
    return [
        path
        for path in sorted((ROOT / ".github/workflows").glob("*.yml"))
        if path.name not in UNGUARDED_WORKFLOWS
    ]


def _version_keys(name, node, path=()):
    """Yield literal numeric values of tool-version keys anywhere in YAML."""
    if isinstance(node, dict):
        for key, value in node.items():
            child = (*path, str(key))
            if (
                VERSION_KEY.search(str(key))
                and (
                    name,
                    child,
                )
                not in COMPATIBILITY_MATRICES
            ):
                values = value if isinstance(value, list) else [value]
                yield from (
                    f"{key} {item}"
                    for item in values
                    if NUMERIC.fullmatch(str(item).strip())
                )
            yield from _version_keys(name, value, child)
    elif isinstance(node, list):
        for item in node:
            yield from _version_keys(name, item, path)


def literal_tool_versions(text, document, name="workflow.yml"):
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
    found.extend(_version_keys(name, document))
    return sorted(set(found))


def test_workflows_take_tool_versions_from_committed_configuration():
    """A routine tool update edits only mise.lock or global.json."""
    for path in guarded_workflows():
        text = path.read_text()
        found = literal_tool_versions(text, yaml.safe_load(text), path.name)
        assert found == [], path


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


def test_tool_version_guard_exempts_only_the_node_compatibility_matrix():
    """The retained engines matrix is not a tool selection."""
    text = (ROOT / ".github/workflows/ci.yml").read_text()
    document = yaml.safe_load(text)
    assert literal_tool_versions(text, document, "ci.yml") == []
    assert literal_tool_versions(text, document, "other.yml")
    # The committed Node selection must occur in the compatibility matrix.
    node = locked.locked_version(ROOT, "node")
    matrix = document["jobs"]["node-tests"]["strategy"]["matrix"]
    assert f"{node.split('.', 1)[0]}.x" in matrix["node-version"]


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
