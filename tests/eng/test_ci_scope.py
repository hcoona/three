"""Affected execution decisions and real Git comparison failures."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import subprocess
import sys
import tomllib
from importlib import import_module
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "eng/scripts"
SPEC = importlib.util.spec_from_file_location(
    "ci_scope", SCRIPTS / "ci_scope.py"
)
assert SPEC is not None
assert SPEC.loader is not None
sys.path.insert(0, str(SCRIPTS))
try:
    scope = importlib.util.module_from_spec(SPEC)
    SPEC.loader.exec_module(scope)
    preparation = import_module("prepare_ci_control_inputs")
finally:
    sys.path.remove(str(SCRIPTS))
V3_TESTS = scope.V3 + "/tests"
AZURE_TESTS = scope.AZURE + "/python/tests"
NBGV_TESTS = "src/public/lib/nbgv-python/tests"
LEGACY_RELEASE_TESTS = "tests/eng/test_legacy_release_contract.py"


@pytest.mark.parametrize(
    ("path", "jobs", "roots"),
    [
        ("docs/README.md", set(), set()),
        pytest.param(
            "tests/eng/test_run_node_ci_group.py",
            {"python"},
            {"tests/eng/test_run_node_ci_group.py"},
            id="node-caller-test",
        ),
        pytest.param(
            "eng/scripts/run_node_ci_group.py",
            {"python"},
            {"tests/eng/test_run_node_ci_group.py"},
            id="node-caller-source",
        ),
        ("eng/scripts/hk_file_operands.py", {"python"}, {V3_TESTS}),
        (scope.V3 + "/docs/requirements.md", set(), set()),
        ("src/public/lib/CircularList/CircularList.cs", {"dotnet"}, set()),
        (
            ".editorconfig",
            {"dotnet", "azureauth", "python"},
            {V3_TESTS, AZURE_TESTS, NBGV_TESTS},
        ),
        (
            "src/private/app/.editorconfig",
            {"dotnet", "azureauth", "python"},
            {V3_TESTS, AZURE_TESTS},
        ),
        (
            "src/public/lib/CircularList/.editorconfig",
            {"dotnet"},
            set(),
        ),
        ("docs/.editorconfig", set(), set()),
        (
            "src/public/lib/CircularList/Directory.Build.props",
            {"dotnet"},
            set(),
        ),
        (
            "src/public/lib/CircularList/workflow-delivery.quality.yml",
            {"dotnet"},
            set(),
        ),
        (
            "src/private/app/git-commit-heatmap/README.md",
            {"python"},
            {"src/private/app/git-commit-heatmap/tests"},
        ),
        (
            "src/public/lib/nbgv-python/src/nbgv_python/cli.py",
            {"python", "azureauth"},
            {NBGV_TESTS, AZURE_TESTS, V3_TESTS},
        ),
        (
            "src/private/app/workflow-delivery-v3-nuget-consumer/Program.cs",
            {"dotnet", "python"},
            {V3_TESTS},
        ),
        (
            "tests/private/app/workflow-delivery-v3-nuget-authority/ProgramTests.cs",
            {"dotnet", "python"},
            {V3_TESTS},
        ),
        ("src/public/lib/hexo-renderer-asciidoc/README.md", {"node"}, set()),
        ("src/public/lib/asciidoctor-latexmath/README.adoc", {"ruby"}, set()),
        (
            scope.SCHOLARLY + "/tests/test_assemble_print.py",
            {"scholarly"},
            set(),
        ),
        (
            "eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",
            {"azureauth", "python"},
            {AZURE_TESTS},
        ),
        (
            ".github/workflows/workflow-delivery-v3-ci.yml",
            {"python"},
            {V3_TESTS, LEGACY_RELEASE_TESTS},
        ),
        (
            ".github/workflows/unrelated-project.yml",
            {"python"},
            {LEGACY_RELEASE_TESTS},
        ),
        (
            "eng/scripts/publish_node_npmjs_idempotent.sh",
            {"python"},
            {LEGACY_RELEASE_TESTS},
        ),
        (
            ".typos.toml",
            {"python"},
            {"tests/eng/test_typos_config.py"},
        ),
    ],
)
def test_selects_consumers_without_unrelated_suites(path, jobs, roots):
    """Different projects and consumed metadata have independent CI owners."""
    selected = scope.select(ROOT, (path,), base="HEAD")
    assert {
        key for key, value in selected["scopes"].items() if value
    } == jobs | {"validation"}
    assert set(selected["python_roots"]) == roots
    assert all(path in why for why in selected["python_reasons"].values())


def test_shared_tool_inputs_select_native_consumers_and_full_is_explicit():
    """Native build inputs reach Python consumers; full includes every suite."""
    selected = scope.select(ROOT, ("Directory.Packages.props",), base="HEAD")
    assert {V3_TESTS, AZURE_TESTS} <= set(selected["python_roots"])
    assert selected["scopes"]["dotnet"]
    assert selected["scopes"]["azureauth"]
    full = scope.select(ROOT, (), base="", full=True)
    assert all(full["scopes"].values())
    config = tomllib.loads((ROOT / "pyproject.toml").read_text())
    assert (
        full["python_roots"]
        == config["tool"]["pytest"]["ini_options"]["testpaths"]
    )
    assert set(full["python_packages"]) == {
        "hcoona-three-monorepo",
        "azureauth-credprovider-keyring",
        "git-commit-heatmap",
        "llm-text-splitter",
        "nbgv-python",
        "three-workflow-delivery-v3",
    }
    assert full["python_dotnet"] is True
    assert full["python_v3"] is True
    empty = scope.select(ROOT, (), base="HEAD")
    assert not empty["python_roots"]
    assert not empty["scopes"]["python"]
    assert empty["python_packages"] == []
    assert empty["python_dotnet"] is False
    assert empty["python_v3"] is False


def test_full_python_runner_change_selects_every_configured_test_root():
    """The shared preparation entry is consumed by all configured suites."""
    selected = scope.select(
        ROOT, ("eng/scripts/run_python_tests.py",), base="HEAD"
    )
    config = tomllib.loads((ROOT / "pyproject.toml").read_text())
    assert (
        selected["python_roots"]
        == config["tool"]["pytest"]["ini_options"]["testpaths"]
    )
    assert selected["python_dotnet"] is True
    assert selected["python_v3"] is True


@pytest.mark.parametrize(
    ("paths", "packages", "dotnet", "v3"),
    [
        ((".typos.toml",), set(), False, False),
        (
            ("src/private/app/git-commit-heatmap/README.md",),
            {"git-commit-heatmap"},
            False,
            False,
        ),
        (
            ("eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",),
            {"azureauth-credprovider-keyring"},
            True,
            False,
        ),
        (
            (scope.V3 + "/tests/test_example.py",),
            {"three-workflow-delivery-v3"},
            True,
            True,
        ),
        (
            ("src/public/lib/nbgv-python/src/nbgv_python/cli.py",),
            {
                "nbgv-python",
                "azureauth-credprovider-keyring",
                "three-workflow-delivery-v3",
            },
            True,
            True,
        ),
        (
            (scope.V3 + "/tests/test_example.py", ".typos.toml"),
            {"three-workflow-delivery-v3"},
            True,
            True,
        ),
    ],
)
def test_python_preparation_follows_selected_consumers(
    paths, packages, dotnet, v3
):
    """Prepare only selected current members and their native tools."""
    selected = scope.select(ROOT, paths, base="HEAD")
    assert selected["python_packages"] == [
        "hcoona-three-monorepo",
        *sorted(packages),
    ]
    assert selected["python_dotnet"] is dotnet
    assert selected["python_v3"] is v3


def _git(root, *arguments):
    return subprocess.run(  # noqa: S603 - Fixed tool/script and owned fixture arguments.
        [  # noqa: S607 - Git is the integration boundary.
            "git",
            "-c",
            "core.hooksPath=/dev/null",
            "-c",
            "user.name=CI scope test",
            "-c",
            "user.email=ci@example.invalid",
            *arguments,
        ],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


@pytest.fixture
def comparison(tmp_path):
    """Create an owned repository with a genuine rename and deletion."""
    _git(tmp_path, "init", "-q")
    (tmp_path / "pyproject.toml").write_text(
        '[project]\nname = "sample-root"\n'
        '[tool.uv.workspace]\nmembers = ["src/python"]\n'
        '[tool.pytest.ini_options]\ntestpaths = ["src/python/tests"]\n'
    )
    python = tmp_path / "src/python"
    python.mkdir(parents=True)
    (python / "pyproject.toml").write_text('[project]\nname = "sample"\n')
    (python / "tests").mkdir()
    (python / "tests/test_sample.py").write_text("def test_sample(): pass\n")
    (python / "old.py").write_text("retained = True\n" * 10)
    (python / "deleted.py").write_text("removed = True\n")
    node = tmp_path / "src/node"
    node.mkdir()
    (node / "package.json").write_text('{"name":"node"}')
    project = tmp_path / scope.CONTROL_PROJECT
    project.parent.mkdir(parents=True)
    project.write_text("<Project />", encoding="utf-8")
    _git(tmp_path, "add", ".")
    _git(tmp_path, "commit", "-qm", "Base")
    base = _git(tmp_path, "rev-parse", "HEAD")
    (python / "old.py").rename(node / "renamed.txt")
    (python / "deleted.py").unlink()
    _git(tmp_path, "add", "-A")
    _git(tmp_path, "commit", "-qm", "Move and delete inputs")
    return tmp_path, base, _git(tmp_path, "rev-parse", "HEAD")


def _control_response(base, candidate, *, full=False, before=(), after=()):
    def endpoint(revision, inputs):
        return {
            "revision": revision,
            "project": scope.CONTROL_PROJECT,
            "present": True,
            "dimension": {
                "configuration": "Debug",
                "targetFramework": "net10.0",
                "runtimeIdentifier": "",
            },
            "inputs": list(inputs),
        }

    return {
        "comparison": {"basis": base, "candidate": candidate, "full": full},
        "basis": endpoint(base, before),
        "candidate": endpoint(candidate, after),
    }


def _select_cli(root, *arguments, receipt=None):
    candidate = _git(root, "rev-parse", "HEAD")
    full = "--full" in arguments
    base = (
        candidate
        if full or "--from-ref" not in arguments
        else arguments[arguments.index("--from-ref") + 1]
    )
    response = (
        _control_response(base, candidate, full=full)
        if receipt is None
        else receipt
    )
    source = root / "control-inputs.json"
    source.write_text(
        response if isinstance(response, str) else json.dumps(response),
        encoding="utf-8",
    )
    return subprocess.run(  # noqa: S603 - Fixed tool/script and owned fixture arguments.
        [
            sys.executable,
            str(SCRIPTS / "ci_scope.py"),
            "--repository",
            str(root),
            "--control-inputs",
            str(source),
            *arguments,
        ],
        env=dict(os.environ, GITHUB_OUTPUT=str(root / "outputs")),
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def test_git_range_retains_deleted_and_both_rename_paths(comparison):
    """A rename away still schedules its old owner and the destination owner."""
    root, base, candidate = comparison
    result = _select_cli(root, "--from-ref", base, "--to-ref", candidate)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert set(selected["changed_paths"]) == {
        "src/python/deleted.py",
        "src/python/old.py",
        "src/node/renamed.txt",
    }
    assert selected["candidate"] == candidate
    assert selected["base"] == base
    assert selected["scopes"]["node"]
    assert selected["python_roots"] == ["src/python/tests"]
    assert selected["python_packages"] == ["sample-root", "sample"]
    assert "python=true\n" in (root / "outputs").read_text()


@pytest.mark.parametrize("failure", ["missing", "invalid", "wrong-candidate"])
def test_selection_failure_never_emits_successful_applicability(
    comparison, failure
):
    """Missing Git evidence cannot turn required checks into green skips."""
    root, base, _ = comparison
    arguments = {
        "missing": [],
        "invalid": ["--from-ref", "absent-ref"],
        "wrong-candidate": ["--from-ref", base, "--to-ref", base],
    }[failure]
    result = _select_cli(root, *arguments)
    assert result.returncode != 0
    assert not (root / "outputs").exists()
    full = _select_cli(root, "--full")
    assert full.returncode == 0, full.stderr
    assert all(json.loads(full.stdout)["scopes"].values())


@pytest.mark.parametrize(
    "inventory", ["[]", '"src/python/tests"', '[""]', "[true]"]
)
def test_unusable_test_inventory_never_emits_successful_applicability(
    comparison, inventory
):
    """Unsupported discovery configuration cannot become a green empty job."""
    root, base, _ = comparison
    config = root / "pyproject.toml"
    config.write_text(
        config.read_text().replace('["src/python/tests"]', inventory)
    )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Change test inventory")
    for arguments in (("--from-ref", base), ("--full",)):
        result = _select_cli(root, *arguments)
        assert result.returncode != 0
        assert "nonempty list of explicit paths" in result.stderr
        assert not (root / "outputs").exists()


def test_candidate_test_migration_selects_replacement_root(comparison):
    """Reviewed test moves use the candidate inventory, not historical paths."""
    root, base, _ = comparison
    (root / "src/python/tests").rename(root / "src/python/checks")
    config = root / "pyproject.toml"
    config.write_text(
        config.read_text().replace("src/python/tests", "src/python/checks")
    )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Move test owner")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["python_roots"] == ["src/python/checks"]
    assert selected["scopes"]["python"]
    assert not (root / "src/python/tests").exists()


@pytest.mark.parametrize(
    "change", ["version-only", "removed", "added", "moved"]
)
def test_resource_input_selects_existing_dotnet_owner_from_both_revisions(
    comparison, change
):
    """Both native resource sets keep the existing .NET owner selected."""
    root, _, _ = comparison
    old = "src/node/version 雪.json"
    new = "src/node/moved 雪.json" if change == "moved" else old
    (root / scope.CONTROL_PROJECT).write_text(
        "<Project><PropertyGroup>"
        "<TargetFramework>net10.0</TargetFramework></PropertyGroup>"
        "<ItemGroup Condition=\"'$(Configuration)' == 'Debug'\">"
        '<EmbeddedResource Include="../../../../src/node/*.json" />'
        "</ItemGroup></Project>",
        encoding="utf-8",
    )
    if change != "added":
        (root / old).write_text("before", encoding="utf-8")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Resource basis")
    base = _git(root, "rev-parse", "HEAD")
    if change in {"removed", "moved"}:
        (root / old).unlink()
    if change != "removed":
        (root / new).write_text("after", encoding="utf-8")
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Change native resource")
    candidate = _git(root, "rev-parse", "HEAD")
    before = () if change == "added" else (old,)
    after = () if change == "removed" else (new,)
    response = _control_response(base, candidate, before=before, after=after)

    result = _select_cli(root, "--from-ref", base, receipt=response)

    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["scopes"]["dotnet"]
    assert selected["scopes"]["node"]
    assert not selected["scopes"]["python"]
    expected = {
        f"{path} -> {scope.CONTROL_PROJECT} ({revision})"
        for revision, inputs in ((base, before), (candidate, after))
        for path in inputs
    }
    assert set(selected["reasons"]["dotnet"]) == expected
    assert "dotnet=true\n" in (root / "outputs").read_text(encoding="utf-8")


def test_unrelated_node_input_does_not_select_control_tests(comparison):
    """Known unrelated Node inputs exclude the .NET control tests."""
    root, _, _ = comparison
    (root / "src/node/version.json").write_text(
        "unchanged resource", encoding="utf-8"
    )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Unchanged native resource")
    candidate = _git(root, "rev-parse", "HEAD")
    (root / "src/node/unrelated.txt").write_text("change", encoding="utf-8")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Unrelated Node source")
    current = _git(root, "rev-parse", "HEAD")
    response = _control_response(
        candidate,
        current,
        before=("src/node/version.json",),
        after=("src/node/version.json",),
    )

    result = _select_cli(root, "--from-ref", candidate, receipt=response)

    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["scopes"]["node"]
    assert not selected["scopes"]["dotnet"]
    assert selected["reasons"]["dotnet"] == []
    assert "dotnet=false\n" in (root / "outputs").read_text(encoding="utf-8")


@pytest.mark.parametrize(
    "defect",
    [
        "malformed",
        "missing-field",
        "wrong-basis",
        "wrong-candidate",
        "wrong-revision",
        "wrong-project",
        "wrong-presence",
        "empty-framework",
        "wrong-configuration",
        "inputs-null",
        "outside-input",
        "duplicate",
    ],
)
def test_invalid_native_response_emits_no_successful_scope(comparison, defect):
    """Required malformed facts fail before applicability output."""
    root, base, candidate = comparison
    response = _control_response(base, candidate)
    if defect == "malformed":
        response = "{"
    elif defect == "missing-field":
        del response["basis"]
    elif defect == "duplicate":
        response = json.dumps(response).replace(
            '"basis": {', '"basis": {}, "basis": {', 1
        )
    else:
        mutations = {
            "wrong-basis": (("comparison", "basis"), candidate),
            "wrong-candidate": (("comparison", "candidate"), base),
            "wrong-revision": (("basis", "revision"), candidate),
            "wrong-project": (("candidate", "project"), "other.csproj"),
            "wrong-presence": (("basis", "present"), False),
            "empty-framework": (
                ("candidate", "dimension", "targetFramework"),
                "",
            ),
            "wrong-configuration": (
                ("candidate", "dimension", "configuration"),
                "Release",
            ),
            "inputs-null": (("candidate", "inputs"), None),
            "outside-input": (("candidate", "inputs"), ["../outside.json"]),
        }
        path, value = mutations[defect]
        target = response
        for key in path[:-1]:
            target = target[key]
        target[path[-1]] = value

    result = _select_cli(root, "--from-ref", base, receipt=response)

    assert result.returncode != 0
    assert result.stdout == ""
    assert not (root / "outputs").exists()


def test_missing_native_response_is_not_an_incremental_or_full_fallback(
    comparison,
):
    """Unavailable facts fail full as well as incremental selection."""
    root, base, _ = comparison
    for mode in (("--from-ref", base), ("--full",)):
        result = subprocess.run(  # noqa: S603 - Owned CLI fixture.
            [
                sys.executable,
                str(SCRIPTS / "ci_scope.py"),
                "--repository",
                str(root),
                *mode,
                "--control-inputs",
                str(root / "missing-response.json"),
            ],
            env=dict(os.environ, GITHUB_OUTPUT=str(root / "outputs")),
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )
        assert result.returncode != 0
        assert result.stdout == ""
        assert not (root / "outputs").exists()


def test_full_scope_retains_equal_native_endpoints(comparison):
    """Manual full preserves the equality of the native source endpoints."""
    root, _, candidate = comparison

    result = _select_cli(root, "--full")

    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["base"] == selected["candidate"] == candidate
    assert selected["full"] is True
    assert all(selected["scopes"].values())
    assert selected["endpoint_owners"] == {
        "basis": {"revision": candidate, "paths": []},
        "candidate": {"revision": candidate, "paths": []},
    }


def test_confirmed_base_owner_absence_is_explicit_and_candidate_is_required(
    comparison,
):
    """Only base absence is allowed; the candidate retains its owner."""
    root, _, _ = comparison
    project = root / scope.CONTROL_PROJECT
    project.unlink()
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Absent basis owner")
    base = _git(root, "rev-parse", "HEAD")
    project.write_text("<Project />", encoding="utf-8")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Add candidate owner")
    candidate = _git(root, "rev-parse", "HEAD")
    response = _control_response(base, candidate)
    response["basis"].update(present=False, dimension=None)

    result = _select_cli(root, "--from-ref", base, receipt=response)

    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout)["scopes"]["dotnet"]
    (root / "outputs").unlink()
    project.unlink()
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Remove candidate owner")
    absent = _git(root, "rev-parse", "HEAD")
    response = _control_response(candidate, absent)
    response["candidate"].update(present=False, dimension=None)
    result = _select_cli(root, "--from-ref", candidate, receipt=response)
    assert result.returncode != 0
    assert not (root / "outputs").exists()


@pytest.mark.parametrize("full", [False, True])
def test_native_prepare_preserves_endpoints_and_locked_context(
    comparison, monkeypatch, tmp_path_factory, full
):
    """Native preparation uses exact Git endpoints and locked contexts."""
    root, basis, candidate = comparison
    if full:
        basis = candidate
    native_comparison = {"basis": basis, "candidate": candidate, "full": full}
    restored = []
    original = preparation.run
    response = _control_response(basis, candidate, full=full)

    def native(directory, *arguments):
        if arguments[0] == "git":
            return original(directory, *arguments)
        if arguments[1] == "restore":
            restored.append((directory, arguments))
            return "Locked fixture preparation\n"
        if "comparison" in arguments:
            return json.dumps(native_comparison)
        assert "control-inputs" in arguments
        return json.dumps(response)

    monkeypatch.setattr(preparation, "run", native)
    scratch = tmp_path_factory.mktemp("control-inputs") / "endpoints"
    request = preparation.prepare(
        root,
        scratch,
        root / "control.dll",
        ("pull_request", root / "event.json", candidate),
    )
    before = Path(request["basisDirectory"])
    after = Path(request["candidateDirectory"])
    assert not before.is_relative_to(root)
    assert not after.is_relative_to(root)
    assert _git(before, "rev-parse", "HEAD") == basis
    assert _git(after, "rev-parse", "HEAD") == candidate
    assert (before == after) is full
    assert (
        json.loads((scratch / "request.json").read_text(encoding="utf-8"))
        == request
    )
    result = preparation.evaluate(
        root,
        root / "control.dll",
        scratch / "request.json",
        scratch / "response.json",
    )
    assert result == response
    assert {directory for directory, _ in restored} == {before, after}
    for _, arguments in restored:
        assert arguments == (
            "dotnet",
            "restore",
            scope.CONTROL_PROJECT,
            "--locked-mode",
            "-p:Configuration=Debug",
        )
    assert (
        json.loads((scratch / "response.json").read_text(encoding="utf-8"))
        == response
    )


def test_native_prepare_failure_does_not_write_facts(comparison, monkeypatch):
    """Failed required native operations never write successful facts."""
    root, basis, candidate = comparison
    scratch = root.parent / (root.name + "-failed-endpoints")
    request = {
        "comparison": {"basis": basis, "candidate": candidate, "full": False},
        "repository": str(root),
        "basisDirectory": str(scratch / "basis"),
        "candidateDirectory": str(scratch / "candidate"),
    }
    scratch.mkdir()
    request_path = scratch / "request.json"
    request_path.write_text(json.dumps(request), encoding="utf-8")

    def unavailable(*_arguments):
        raise subprocess.CalledProcessError(1, ["dotnet", "restore"])

    monkeypatch.setattr(preparation, "run", unavailable)
    with pytest.raises(subprocess.CalledProcessError):
        preparation.evaluate(
            root, root / "control.dll", request_path, scratch / "response.json"
        )
    assert not (scratch / "response.json").exists()
    with pytest.raises(subprocess.CalledProcessError):
        preparation.prepare(
            root,
            scratch / "not-created",
            root / "control.dll",
            ("push", root / "event.json", candidate),
        )
    assert not (scratch / "not-created").exists()


def test_native_prepare_rejects_source_subdirectories(
    comparison,
):
    """Temporary checkouts cannot pollute the candidate source inventory."""
    root, _, candidate = comparison
    with pytest.raises(ValueError, match="outside"):
        preparation.prepare(
            root,
            root / "generated",
            root / "control.dll",
            ("push", root / "event.json", candidate),
        )
    assert not (root / "generated").exists()


def _owner_rows(result, name):
    return {
        row["path"]: row for row in result["endpoint_owners"][name]["paths"]
    }


def test_control_preparation_remains_available_without_endpoint_parser():
    """System-Python bootstrap must not require the UV endpoint parser."""
    result = subprocess.run(  # noqa: S603 - Exact interpreter and repository help command.
        [
            sys.executable,
            "-S",
            str(SCRIPTS / "prepare_ci_control_inputs.py"),
            "--help",
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "prepare" in result.stdout
    assert "evaluate" in result.stdout


def test_endpoint_owners_preserve_changed_coordinate_identity(comparison):
    """Removed coordinates and both rename endpoints keep independent facts."""
    root, base, candidate = comparison
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    owners = selected["endpoint_owners"]
    assert owners["basis"]["revision"] == base
    assert owners["candidate"]["revision"] == candidate
    before, after = (
        _owner_rows(selected, "basis"),
        _owner_rows(selected, "candidate"),
    )
    assert list(before) == list(after) == selected["changed_paths"]
    removed = before["src/python/deleted.py"]
    assert removed["present"] is True
    assert removed["mode"] == "100644"
    assert removed["reasons"] == [
        {
            "owner": "python",
            "target": "src/python/tests",
            "rule": "retained-python-test-input",
            "sources": ["pyproject.toml", "src/python/pyproject.toml"],
        }
    ]
    assert after["src/python/deleted.py"] == {
        "path": "src/python/deleted.py",
        "present": False,
        "mode": None,
        "reasons": [],
    }
    assert before["src/node/renamed.txt"]["present"] is False
    assert after["src/node/renamed.txt"] == {
        "path": "src/node/renamed.txt",
        "present": True,
        "mode": "100644",
        "reasons": [],
    }
    # Retained aggregate execution selects Node; PNPM ownership is pending.
    assert selected["scopes"]["node"] is True
    assert selected["python_roots"] == ["src/python/tests"]


def test_endpoint_owners_keep_basis_python_targets_from_committed_configuration(
    comparison,
):
    """Committed endpoint facts explain surviving candidate execution."""
    root, base, _ = comparison
    project = root / "src/python/pyproject.toml"
    project.unlink()
    config = root / "pyproject.toml"
    config.write_text(
        config.read_text().replace(
            'testpaths = ["src/python/tests"]',
            'testpaths = ["tests/replacement"]',
        )
    )
    replacement = root / "tests/replacement/test_current.py"
    replacement.parent.mkdir(parents=True)
    replacement.write_text("pass\n")
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Replace candidate Python test target")
    candidate = _git(root, "rev-parse", "HEAD")
    # Dirty manifest contents do not replace committed endpoint facts.
    config.write_text(
        config.read_text().replace('"tests/replacement"', '"tests/dirty"')
    )
    owners = scope.endpoint_owners(
        root,
        ("src/python/deleted.py", "pyproject.toml"),
        base=base,
        candidate=candidate,
        control_inputs=_control_response(base, candidate),
    )
    assert (
        owners["basis"]["paths"][0]["reasons"][0]["target"]
        == "src/python/tests"
    )
    assert owners["candidate"]["paths"][0]["reasons"] == []
    assert owners["candidate"]["paths"][1]["reasons"] == [
        {
            "owner": "python",
            "target": "tests/replacement",
            "rule": "retained-python-test-input",
            "sources": ["pyproject.toml"],
        }
    ]
    _git(root, "checkout", "--", "pyproject.toml")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["python_roots"] == ["tests/replacement"]
    assert selected["python_packages"] == ["sample-root"]
    assert "src/python/tests" not in selected["python_reasons"]


@pytest.mark.parametrize("member_pattern", ["*", "?", "[abc]"])
def test_endpoint_owners_use_committed_member_dependencies(
    comparison, member_pattern
):
    """Surviving consumers use endpoint dependencies, never dirty members."""
    root, _, _ = comparison
    paths = tuple(f"src/provider-{name}/input.py" for name in ("a", "b", "c"))
    test_roots = [
        "src/python/tests",
        *(f"src/provider-{name}/tests" for name in ("a", "b", "c")),
    ]
    (root / "pyproject.toml").write_text(
        '[project]\nname = "sample-root"\n'
        '[tool.uv.workspace]\nmembers = ["src/python", '
        + json.dumps("src/provider-" + member_pattern)
        + "]\n"
        + "[tool.pytest.ini_options]\ntestpaths = "
        + json.dumps(test_roots)
        + "\n"
    )
    member = root / "src/python/pyproject.toml"
    member.write_text(
        '[project]\nname = "sample"\ndependencies = ["provider-a"]\n'
    )
    for name, path in zip(("a", "b", "c"), paths, strict=True):
        directory = (root / path).parent
        directory.mkdir()
        (directory / "pyproject.toml").write_text(
            f'[project]\nname = "provider-{name}"\n'
        )
        (directory / "tests").mkdir()
        (directory / "tests/test_provider.py").write_text("pass\n")
        (root / path).write_text("original = True\n")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Add basis Python dependency")
    base = _git(root, "rev-parse", "HEAD")
    member.write_text(member.read_text().replace("provider-a", "provider-b"))
    for path in paths:
        (root / path).write_text("updated = True\n")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Change candidate Python dependency")
    candidate = _git(root, "rev-parse", "HEAD")
    member.write_text(member.read_text().replace("provider-b", "provider-c"))

    owners = scope.endpoint_owners(
        root,
        paths,
        base=base,
        candidate=candidate,
        control_inputs=_control_response(base, candidate),
    )
    for endpoint, dependency in (("basis", "a"), ("candidate", "b")):
        assert owners[endpoint]["revision"] == (
            base if endpoint == "basis" else candidate
        )
        for name, row in zip(
            ("a", "b", "c"), owners[endpoint]["paths"], strict=True
        ):
            targets = [f"src/provider-{name}/tests"]
            if name == dependency:
                targets.append("src/python/tests")
            assert row == {
                "path": f"src/provider-{name}/input.py",
                "present": True,
                "mode": "100644",
                "reasons": [
                    {
                        "owner": "python",
                        "target": target,
                        "rule": "retained-python-test-input",
                        "sources": [
                            "pyproject.toml",
                            target.removesuffix("/tests") + "/pyproject.toml",
                        ],
                    }
                    for target in sorted(targets)
                ],
            }

    _git(root, "checkout", "--", "src/python/pyproject.toml")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert selected["python_roots"] == test_roots
    assert selected["python_packages"] == [
        "sample-root",
        "provider-a",
        "provider-b",
        "provider-c",
        "sample",
    ]
    assert selected["python_reasons"] == {
        "src/python/tests": [paths[1], "src/python/pyproject.toml"],
        **{
            f"src/provider-{name}/tests": [path]
            for name, path in zip(("a", "b", "c"), paths, strict=True)
        },
    }


def test_endpoint_owners_retain_multiple_native_and_project_responsibilities(
    comparison,
):
    """Keep general .NET, special-job and native resource reasons separate."""
    root, _, base = comparison
    azure = root / scope.AZURE / "Sample.csproj"
    azure.parent.mkdir(parents=True)
    azure.write_text("<Project />")
    source = azure.parent / "Input.cs"
    source.write_text("class Input {}")
    (root / "dirs.proj").write_text("<Project />")
    resource = root / (
        "tests/private/app/workflow-delivery/fixtures/products/"
        "hcoona-release-smoke-npm/version.json"
    )
    resource.parent.mkdir(parents=True)
    resource.write_text('{"version":"1.0"}')
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Add owned native inputs")
    candidate = _git(root, "rev-parse", "HEAD")
    result = _select_cli(
        root,
        "--from-ref",
        base,
        receipt=_control_response(
            base, candidate, after=(str(resource.relative_to(root)),)
        ),
    )
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    azure_reasons = _owner_rows(selected, "candidate")[
        str(source.relative_to(root))
    ]["reasons"]
    assert {(item["owner"], item["target"]) for item in azure_reasons} == {
        ("dotnet", str(azure.relative_to(root))),
        ("azureauth", scope.AZURE),
    }
    native_reasons = _owner_rows(selected, "candidate")[
        str(resource.relative_to(root))
    ]["reasons"]
    assert native_reasons == [
        {
            "owner": "dotnet",
            "target": scope.CONTROL_PROJECT,
            "rule": "native-embedded-resource",
            "sources": [scope.CONTROL_PROJECT],
        },
        {
            "owner": "dotnet",
            "target": scope.CONTROL_PROJECT,
            "rule": "retained-project-input",
            "sources": ["dirs.proj", scope.CONTROL_PROJECT],
        },
    ]
    assert selected["scopes"]["dotnet"]
    assert selected["scopes"]["azureauth"]
    assert not selected["endpoint_owners"]["basis"]["paths"][0]["reasons"]


def _catalog(root, pattern="docs/*.md"):
    target = root / scope.FAMILY_CATALOG
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        yaml.safe_dump(
            {
                "schema_version": 3,
                "families": [{"id": "records"}],
                "bindings": [
                    {
                        "id": "project-docs",
                        "family": "records",
                        "namespace": "repository",
                        "state": "current",
                        "carrier": "repository-files",
                        "path": pattern,
                    }
                ],
            }
        )
    )
    return target


@pytest.fixture
def record_control(comparison):
    """Committed local control with deliberately noncanonical script names."""
    root, _, _ = comparison
    scripts = {
        "records:check": "eng/record_check.py",
        "records:test": "tests/record_test.py",
    }
    control = {
        "id": scope.RECORD_CONTROL,
        "state": "current",
        "class": "mechanical",
        "enforcement": "advisory",
        "runner": "local-tool",
        "execution_points": ["local-validation"],
        "governing_rules": [scope.CHECKER_CONTRACT],
        "implementation": {
            "kind": "repository-path",
            "value": scripts["records:check"],
        },
    }
    for path in (*scripts.values(), scope.CHECKER_CONTRACT):
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("Committed source\n")
    catalog = root / scope.CONTROL_CATALOG
    catalog.write_text(
        yaml.safe_dump({"schema_version": 2, "controls": [control]})
    )
    (root / "mise.toml").write_text(
        '[tasks."records:check"]\nrun = "unused controlled fixture"\n'
    )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Record control")
    return root, _git(root, "rev-parse", "HEAD"), control, scripts


def _record_task(checkout, name, scripts):
    return {
        "name": "//:" + name,
        "source": str(checkout / "mise.toml"),
        "config_sources": [str(checkout / "mise.toml")],
        "dir": str(checkout),
        "depends": [],
        "depends_post": [],
        "wait_for": [],
        "env": [],
        "file": None,
        "shell": None,
        "run": ["uv run --script " + scripts[name]],
    }


def test_record_control_owners_derive_native_coordinates(record_control):
    """Native facts name the two current local scripts and their authorities."""
    root, revision, _, scripts = record_control
    reasons = scope.record_control_owners(
        root,
        revision,
        root,
        query=lambda checkout, name: json.dumps(
            _record_task(checkout, name, scripts)
        ),
    )
    assert reasons == {
        script: [
            {
                "owner": "record-system",
                "target": script,
                "rule": "record-control:" + scope.RECORD_CONTROL
                if name == "records:check"
                else "record-regression:records:test",
                "sources": sorted(
                    [scope.CONTROL_CATALOG, scope.CHECKER_CONTRACT, "mise.toml"]
                ),
            }
        ]
        for name, script in scripts.items()
    }
    assert "eng/unrelated.py" not in reasons
    assert "tests/unrelated.py" not in reasons
    selected = scope.select(root, tuple(scripts.values()), base=revision)
    assert selected["python_roots"] == []
    assert not selected["scopes"]["python"]


def test_record_control_owners_preserve_both_roles_for_one_script(
    record_control,
):
    """Multiple current responsibilities survive an identical script target."""
    root, revision, _, scripts = record_control
    scripts["records:test"] = scripts["records:check"]
    reasons = scope.record_control_owners(
        root,
        revision,
        root,
        query=lambda checkout, name: json.dumps(
            _record_task(checkout, name, scripts)
        ),
    )
    assert set(reasons) == {scripts["records:check"]}
    assert [reason["rule"] for reason in reasons[scripts["records:check"]]] == [
        "record-control:" + scope.RECORD_CONTROL,
        "record-regression:records:test",
    ]


def test_record_control_owners_preserve_endpoint_moves(record_control):
    """Each endpoint retains its own script identity across coherent moves."""
    root, base, control, old = record_control
    basis = root.parent / "record-basis"
    _git(root, "worktree", "add", "--detach", str(basis), base)
    new = {name: path.replace(".py", "_moved.py") for name, path in old.items()}
    for name, path in old.items():
        (root / path).rename(root / new[name])
    control["implementation"]["value"] = new["records:check"]
    (root / scope.CONTROL_CATALOG).write_text(
        yaml.safe_dump({"schema_version": 2, "controls": [control]})
    )
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Move both record scripts")
    candidate = _git(root, "rev-parse", "HEAD")
    before = scope.record_control_owners(
        root,
        base,
        basis,
        query=lambda checkout, name: json.dumps(
            _record_task(checkout, name, old)
        ),
    )
    after = scope.record_control_owners(
        root,
        candidate,
        root,
        query=lambda checkout, name: json.dumps(
            _record_task(checkout, name, new)
        ),
    )
    assert set(before) == set(old.values())
    assert set(after) == set(new.values())
    assert set(before).isdisjoint(after)
    assert before[old["records:check"]][0]["target"] == old["records:check"]
    with pytest.raises(ValueError, match="exact committed endpoint"):
        scope.record_control_owners(root, base, root)


@pytest.mark.parametrize("change", ["head", "dirty-manifest"])
def test_record_control_owners_require_exact_task_context(
    record_control, change
):
    """A different or dirty task context cannot represent the endpoint."""
    root, revision, _, _ = record_control
    if change == "head":
        _git(root, "commit", "--allow-empty", "-qm", "Another endpoint")
    else:
        (root / "mise.toml").write_text("Changed task source\n")
    with pytest.raises(ValueError, match="exact committed endpoint"):
        scope.record_control_owners(
            root,
            revision,
            root,
            query=lambda *_: pytest.fail("Wrong endpoint must not query tasks"),
        )


@pytest.mark.parametrize(
    "change",
    [
        "malformed",
        "duplicate",
        "failure",
        "foreign-source",
        "foreign-config",
        "foreign-dir",
        "name",
        "depends",
        "env",
        "shell",
        "file",
        "multiple",
        "operator",
        "substitution",
        "option",
        "traversal",
        "missing-script",
        "nonregular",
    ],
)
def test_record_control_owners_reject_unsupported_native_facts(
    record_control, change
):
    """Unavailable native metadata cannot establish responsibility."""
    root, revision, _, scripts = record_control

    def query(checkout, name):
        task = _record_task(checkout, name, scripts)
        if change == "malformed":
            return "{"
        if change == "duplicate":
            return '{"name": "x", "name": "y"}'
        if change == "failure":
            raise subprocess.CalledProcessError(1, ["mise", "tasks", "info"])
        updates = {
            "foreign-source": {"source": str(root.parent / "mise.toml")},
            "foreign-config": {
                "config_sources": [
                    str(root / "mise.toml"),
                    str(root.parent / "mise.toml"),
                ]
            },
            "foreign-dir": {"dir": str(root.parent)},
            "name": {"name": "//:another"},
            "depends": {"depends": ["another"]},
            "env": {"env": ["CUSTOM=1"]},
            "shell": {"shell": "bash"},
            "file": {"file": "other.py"},
            "multiple": {"run": [*task["run"], "uv run --script other.py"]},
            "operator": {"run": [task["run"][0] + "; echo other"]},
            "substitution": {"run": ["uv run --script $(echo other.py)"]},
            "option": {"run": ["uv run --frozen --script other.py"]},
            "traversal": {"run": ["uv run --script ../outside.py"]},
            "missing-script": {"run": ["uv run --script missing.py"]},
            "nonregular": {"run": ["uv run --script link.py"]},
        }
        if (
            change not in {"missing-script", "nonregular"}
            or name == "records:test"
        ):
            task.update(updates[change])
        return json.dumps(task)

    if change == "nonregular":
        _git(
            root,
            "update-index",
            "--add",
            "--cacheinfo",
            "120000,"
            + _git(root, "rev-parse", "HEAD:" + scripts["records:check"])
            + ",link.py",
        )
        _git(root, "commit", "-qm", "Nonregular script")
        revision = _git(root, "rev-parse", "HEAD")
    if change in {"missing-script", "nonregular"}:
        script = "missing.py" if change == "missing-script" else "link.py"
        with pytest.raises(
            ValueError,
            match="Native record task script is not a committed regular file: "
            + re.escape(script),
        ):
            scope.record_control_owners(root, revision, root, query=query)
    else:
        with pytest.raises((ValueError, subprocess.CalledProcessError)):
            scope.record_control_owners(root, revision, root, query=query)


@pytest.mark.parametrize(
    "state", ["absent-catalog", "absent-control", "scheduled"]
)
def test_record_control_owners_do_not_activate_absent_or_scheduled(
    record_control, state
):
    """Inactive controls contribute neither queries nor ownership."""
    root, _, control, _ = record_control
    catalog = root / scope.CONTROL_CATALOG
    if state == "absent-catalog":
        catalog.unlink()
    else:
        control["state"] = "scheduled"
        catalog.write_text(
            yaml.safe_dump(
                {
                    "schema_version": 2,
                    "controls": [] if state == "absent-control" else [control],
                }
            )
        )
    _git(root, "add", "-A")
    _git(root, "commit", "-qm", "Inactive record control")
    assert (
        scope.record_control_owners(
            root,
            _git(root, "rev-parse", "HEAD"),
            root,
            query=lambda *_: pytest.fail("Inactive route must not query tasks"),
        )
        == {}
    )


@pytest.mark.parametrize(
    "change",
    [
        "duplicate-control",
        "duplicate-yaml",
        "schema",
        "enforcement",
        "execution",
        "contract",
        "implementation",
    ],
)
def test_record_control_owners_reject_conflicting_controls(
    record_control, change
):
    """Conflicting active authority cannot become a positive reason."""
    root, _, control, scripts = record_control
    catalog = {"schema_version": 2, "controls": [control]}
    if change == "duplicate-control":
        catalog["controls"].append(control)
    if change == "schema":
        catalog["schema_version"] = True
    if change == "enforcement":
        control["enforcement"] = "blocking"
    if change == "execution":
        control["execution_points"] = ["ci"]
    if change == "contract":
        control["governing_rules"] = []
    if change == "implementation":
        control["implementation"]["value"] = scripts["records:test"]
    (root / scope.CONTROL_CATALOG).write_text(
        yaml.safe_dump(catalog)
        + ("schema_version: 2\n" if change == "duplicate-yaml" else "")
    )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Conflicting record control")
    with pytest.raises(
        ValueError, match=r"Duplicate|Unsupported|Conflicting|disagree"
    ):
        scope.record_control_owners(
            root,
            _git(root, "rev-parse", "HEAD"),
            root,
            query=lambda checkout, name: json.dumps(
                _record_task(checkout, name, scripts)
            ),
        )


def test_record_control_metadata_query_never_executes_tasks(
    record_control, monkeypatch
):
    """Only native info queries run; local task commands remain opaque."""
    root, revision, _, scripts = record_control
    commands = []
    native_run = scope.subprocess.run

    def run(command, **kwargs):
        if command[0] != "mise":
            return native_run(command, **kwargs)
        commands.append(tuple(command))
        assert kwargs["cwd"] == root
        return subprocess.CompletedProcess(
            command, 0, json.dumps(_record_task(root, command[3], scripts)), ""
        )

    monkeypatch.setattr(scope.subprocess, "run", run)
    assert set(scope.record_control_owners(root, revision, root)) == set(
        scripts.values()
    )
    assert commands == [
        ("mise", "tasks", "info", name, "--json")
        for name in ("records:check", "records:test")
    ]


def test_endpoint_owners_use_record_bindings_without_guessing_node_membership(
    comparison,
):
    """Endpoint record routing keeps unmatched paths unknown."""
    root, _, _ = comparison
    catalog = _catalog(root)
    for path in ("docs/guide.md", "docs/nested/guide.md", "unknown.txt"):
        target = root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("Original")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Bind direct documentation")
    base = _git(root, "rev-parse", "HEAD")
    _catalog(root, "docs/**/*.md")
    for path in (
        "docs/guide.md",
        "docs/nested/guide.md",
        "unknown.txt",
        "src/node/package.json",
    ):
        target = root / path
        target.write_text(target.read_text() + " ")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Bind nested documentation")
    candidate = _git(root, "rev-parse", "HEAD")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    before, after = (
        _owner_rows(selected, "basis"),
        _owner_rows(selected, "candidate"),
    )
    record = {
        "owner": "record-system",
        "target": "docs/nested/guide.md",
        "rule": "record-binding:project-docs",
        "sources": [str(catalog.relative_to(root))],
    }
    assert before["docs/nested/guide.md"]["reasons"] == []
    assert after["docs/nested/guide.md"]["reasons"] == [record]
    assert before["docs/guide.md"]["reasons"][0]["owner"] == "record-system"
    assert after["unknown.txt"]["reasons"] == []
    assert after["src/node/package.json"]["reasons"] == []
    assert selected["scopes"]["validation"] is True
    assert selected["scopes"]["node"] is True
    assert selected["python_roots"] == []
    assert selected["candidate"] == candidate


def test_endpoint_record_owners_accept_native_yaml_merge(comparison):
    """Native merge mappings retain the canonical checker's catalog meaning."""
    root, _, base = comparison
    catalog = _catalog(root)
    catalog.write_text(
        "schema_version: 3\n"
        "families: [{id: records}]\n"
        "bindings:\n"
        "  - <<: {id: project-docs, family: records, namespace: repository, "
        "state: current, carrier: repository-files, path: 'docs/*.md'}\n"
    )
    target = root / "docs/guide.md"
    target.write_text("Owned record\n")
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Use native merged record binding")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    assert _owner_rows(selected, "candidate")["docs/guide.md"]["reasons"] == [
        {
            "owner": "record-system",
            "target": "docs/guide.md",
            "rule": "record-binding:project-docs",
            "sources": [str(catalog.relative_to(root))],
        }
    ]


@pytest.mark.parametrize(
    "defect",
    [
        "duplicate-yaml",
        "duplicate-merged-yaml",
        "wrong-version",
        "foreign-family",
        "unsafe-binding",
        "question-binding",
        "bracket-binding",
        "missing-base-object",
    ],
)
def test_invalid_endpoint_owner_inputs_emit_no_successful_scope(
    comparison, defect
):
    """Failed committed reads cannot emit successful applicability."""
    root, _, base = comparison
    catalog = _catalog(root)
    if defect == "duplicate-yaml":
        catalog.write_text(catalog.read_text() + "schema_version: 3\n")
    elif defect == "duplicate-merged-yaml":
        catalog.write_text(catalog.read_text() + "<<: {schema_version: 3}\n")
    elif defect == "wrong-version":
        catalog.write_text(
            catalog.read_text().replace(
                "schema_version: 3", "schema_version: 2"
            )
        )
    elif defect == "foreign-family":
        catalog.write_text(
            catalog.read_text().replace("family: records", "family: unknown")
        )
    elif defect == "unsafe-binding":
        catalog.write_text(
            catalog.read_text().replace("docs/*.md", "../outside/*.md")
        )
    elif defect in {"question-binding", "bracket-binding"}:
        catalog.write_text(
            catalog.read_text().replace(
                "docs/*.md",
                "docs/?.md" if defect == "question-binding" else "docs/[ab].md",
            )
        )
    _git(root, "add", ".")
    _git(root, "commit", "-qm", "Candidate endpoint input")
    if defect == "missing-base-object":
        base = "f" * 40
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode != 0
    assert not result.stdout.strip()
    assert not (root / "outputs").exists()


def test_endpoint_owners_keep_nonregular_git_modes_distinct(comparison):
    """Gitlink and symlink coordinates retain distinct entry modes."""
    root, _, base = comparison
    blob = root / "target.txt"
    blob.write_text("outside\n")
    link_object = _git(root, "hash-object", "-w", "target.txt")
    _git(
        root,
        "update-index",
        "--add",
        "--cacheinfo",
        "120000",
        link_object,
        "unknown-link",
    )
    _git(
        root,
        "update-index",
        "--add",
        "--cacheinfo",
        "160000",
        base,
        "unknown-submodule",
    )
    _git(root, "commit", "-qm", "Add nonregular coordinates")
    result = _select_cli(root, "--from-ref", base)
    assert result.returncode == 0, result.stderr
    selected = json.loads(result.stdout)
    before, after = (
        _owner_rows(selected, "basis"),
        _owner_rows(selected, "candidate"),
    )
    for path, mode in (
        ("unknown-link", "120000"),
        ("unknown-submodule", "160000"),
    ):
        assert before[path] == {
            "path": path,
            "present": False,
            "mode": None,
            "reasons": [],
        }
        assert after[path] == {
            "path": path,
            "present": True,
            "mode": mode,
            "reasons": [],
        }
    assert not selected["scopes"]["node"]
    assert selected["python_roots"] == []
