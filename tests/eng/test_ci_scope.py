"""Affected execution decisions and real Git comparison failures."""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
import tomllib
from importlib import import_module
from pathlib import Path

import pytest

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
