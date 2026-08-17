"""Contracts for the permanent smoke-package consumer policy."""

from __future__ import annotations

import importlib.util
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from collections.abc import Mapping
    from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[6]
SCAN_ERROR_EXIT_CODE = 2


def _load_policy() -> Any:
    script = REPO_ROOT / "eng/scripts/workflow_delivery_v3_consumer_policy.py"
    spec = importlib.util.spec_from_file_location("_consumer_policy", script)
    if spec is None or spec.loader is None:
        message = f"cannot load consumer policy from {script}"
        raise AssertionError(message)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


POLICY = _load_policy()
ACCEPTANCE_FIXTURE_PATH = POLICY.ACCEPTANCE_FIXTURE_PATH
ACCEPTANCE_NPM_MANIFEST_PATH = POLICY.ACCEPTANCE_NPM_MANIFEST_PATH
APPROVED_CONSUMER_EXCEPTIONS = POLICY.APPROVED_CONSUMER_EXCEPTIONS
CONSUMER_POLICY_HK_GLOBS = POLICY.CONSUMER_POLICY_HK_GLOBS
ConsumerPolicyScanError = POLICY.ConsumerPolicyScanError
DEPENDENCY_SURFACE_CATALOG = POLICY.DEPENDENCY_SURFACE_CATALOG
GIT_ATTRIBUTES_PATH = POLICY.GIT_ATTRIBUTES_PATH
MAX_LOCAL_ACTION_DEPTH = POLICY._MAX_LOCAL_ACTION_DEPTH  # noqa: SLF001
OWN_DECLARATION_PATH = POLICY.OWN_DECLARATION_PATH
PACKAGE_NAME = POLICY.PACKAGE_NAME
POLICY_IMPLEMENTATION_PATH = POLICY.POLICY_IMPLEMENTATION_PATH
classify_dependency_surface = POLICY.classify_dependency_surface
main = POLICY.main
scan_consumer_policy = POLICY.scan_consumer_policy


def _git(repository: Path, *arguments: str) -> str:
    result = subprocess.run(  # noqa: S603
        ("git", *arguments),
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def _write(repository: Path, path: str, content: bytes | str) -> None:
    destination = repository / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, bytes):
        destination.write_bytes(content)
    else:
        destination.write_text(content, encoding="utf-8")


def _repository(tmp_path: Path) -> tuple[Path, str]:
    repository = tmp_path / "repo"
    repository.mkdir()
    for path in (
        GIT_ATTRIBUTES_PATH,
        OWN_DECLARATION_PATH,
        ACCEPTANCE_FIXTURE_PATH,
        ACCEPTANCE_NPM_MANIFEST_PATH,
    ):
        source = REPO_ROOT / path
        destination = repository / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    _git(repository, "init", "--quiet")
    _git(repository, "config", "user.name", "Consumer Policy Test")
    _git(
        repository,
        "config",
        "user.email",
        "consumer-policy@example.invalid",
    )
    _git(repository, "add", "--all")
    _git(repository, "commit", "--quiet", "--message", "baseline")
    return repository, _git(repository, "rev-parse", "HEAD")


def _assert_consumer(
    repository: Path,
    path: str,
) -> None:
    result = scan_consumer_policy(repository)
    assert result.consumers == (path,)
    assert tuple(
        surface.path for surface in result.admitted_exceptions
    ) == tuple(
        sorted(
            (
                OWN_DECLARATION_PATH,
                ACCEPTANCE_FIXTURE_PATH,
                ACCEPTANCE_NPM_MANIFEST_PATH,
            )
        ),
    )
    assert path in {surface.path for surface in result.scanned_surfaces}


@pytest.mark.parametrize(
    ("path", "content"),
    [
        pytest.param(
            "node/package.json",
            lambda: json.dumps(
                {"dependencies": {PACKAGE_NAME: "1.0.0"}},
            ),
            id="node-manifest",
        ),
        pytest.param(
            "python/pyproject.toml",
            lambda: (f'[project]\ndependencies = ["{PACKAGE_NAME}>=1"]\n'),
            id="python-manifest",
        ),
        pytest.param(
            "dotnet/consumer.csproj",
            lambda: (
                "<Project><ItemGroup>"
                f'<PackageReference Include="{PACKAGE_NAME}" Version="1" />'
                "</ItemGroup></Project>"
            ),
            id="dotnet-manifest",
        ),
        pytest.param(
            "node/package-lock.json",
            lambda: json.dumps(
                {
                    "lockfileVersion": 3,
                    "packages": {
                        f"node_modules/{PACKAGE_NAME}": {"version": "1.0.0"},
                    },
                },
            ),
            id="node-lock",
        ),
        pytest.param(
            "python/uv.lock",
            lambda: f'version = 1\n[[package]]\nname = "{PACKAGE_NAME}"\n',
            id="python-lock",
        ),
        pytest.param(
            "dotnet/packages.lock.json",
            lambda: json.dumps(
                {"version": 1, "dependencies": {PACKAGE_NAME: {}}},
            ),
            id="dotnet-lock",
        ),
    ],
)
def test_scans_python_dotnet_and_node_manifests_and_locks(
    tmp_path: Path,
    path: str,
    content: Any,
) -> None:
    """Detect exact dependency declarations in all three ecosystems."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content())

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("path", "content", "category"),
    [
        pytest.param(
            ".github/workflows/consume.yml",
            lambda: (
                "jobs:\n"
                "  consume:\n"
                "    steps:\n"
                f"      - run: npm install {PACKAGE_NAME}\n"
            ),
            "workflow",
            id="workflow",
        ),
        pytest.param(
            "tools/install-consumer.sh",
            lambda: f"npm install {PACKAGE_NAME}\n",
            "install-bootstrap-script",
            id="install-script",
        ),
        pytest.param(
            ".npmrc",
            lambda: f"smoke-package={PACKAGE_NAME}\n",
            "dependency-configuration",
            id="dependency-configuration",
        ),
        pytest.param(
            ".github/dependabot.yml",
            lambda: (
                "version: 2\n"
                "updates:\n"
                "  - package-ecosystem: npm\n"
                '    directory: "/"\n'
                "    schedule:\n"
                "      interval: weekly\n"
                "    ignore:\n"
                f'      - dependency-name: "{PACKAGE_NAME}"\n'
            ),
            "dependency-configuration",
            id="dependabot",
        ),
        pytest.param(
            "pnpm-workspace.yaml",
            lambda: f'catalog:\n  "{PACKAGE_NAME}": ^1.0.0\n',
            "dependency-configuration",
            id="pnpm-workspace",
        ),
        pytest.param(
            "bunfig.toml",
            lambda: f'dependencies = ["{PACKAGE_NAME}"]\n',
            "dependency-configuration",
            id="bunfig",
        ),
        pytest.param(
            "NuGet.config",
            lambda: (
                "<configuration><packageSourceMapping>"
                '<packageSource key="nuget.org">'
                f'<package pattern="{PACKAGE_NAME}" />'
                "</packageSource></packageSourceMapping></configuration>"
            ),
            "dependency-configuration",
            id="nuget-config",
        ),
        pytest.param(
            ".pnpmfile.cjs",
            lambda: (
                "module.exports = { hooks: { readPackage(pkg) {\n"
                f'  pkg.dependencies["{PACKAGE_NAME}"] = "1.0.0";\n'
                "  return pkg;\n"
                "} } };\n"
            ),
            "dependency-configuration",
            id="pnpmfile",
        ),
    ],
)
def test_rejects_each_non_manifest_surface_class(
    tmp_path: Path,
    path: str,
    content: Any,
    category: str,
) -> None:
    """Detect one concrete consumer in every remaining surface class."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content())

    _assert_consumer(repository, path)
    rule = classify_dependency_surface(path)
    assert rule is not None
    assert rule.category == category


@pytest.mark.parametrize(
    ("path", "content", "category"),
    [
        (
            "tools/postinstall-smoke.js",
            (
                'const { execFile } = require("node:child_process");\n'
                f'execFile("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "install-bootstrap-script",
        ),
        (
            "postinstall-smoke.mjs",
            (
                'import { execFile } from "child_process";\n'
                f'execFile("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "install-bootstrap-script",
        ),
        (
            "tools/postinstall-smoke.cjs",
            (
                'const childProcess = require("child_process");\n'
                "childProcess.execFile("
                f'"npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "install-bootstrap-script",
        ),
        (
            "tools/postinstall-smoke.ts",
            (
                'import * as childProcess from "node:child_process";\n'
                "childProcess.execFile("
                f'"npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "install-bootstrap-script",
        ),
        (
            "node/bun.lock",
            json.dumps(
                {
                    "packages": {
                        PACKAGE_NAME: [f"{PACKAGE_NAME}@1.0.0"],
                    },
                },
            ),
            "lockfile",
        ),
    ],
    ids=[
        "postinstall-js",
        "postinstall-mjs-root",
        "postinstall-cjs-nested",
        "postinstall-ts-nested",
        "bun-lock",
    ],
)
def test_scans_postinstall_scripts_and_bun_lock(
    tmp_path: Path,
    path: str,
    content: str,
    category: str,
) -> None:
    """Catalog and scan the adjudicated postinstall and Bun surfaces."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    _assert_consumer(repository, path)
    rule = classify_dependency_surface(path)
    assert rule is not None
    assert rule.category == category


@pytest.mark.parametrize(
    "command",
    [
        f"npm add {PACKAGE_NAME}",
        f"npm install {PACKAGE_NAME}",
        f"npm exec {PACKAGE_NAME}",
        f"pnpm add {PACKAGE_NAME}",
        f"pnpm install {PACKAGE_NAME}",
        f"pnpm exec {PACKAGE_NAME}",
        f"pnpm dlx {PACKAGE_NAME}",
        f"yarn add {PACKAGE_NAME}",
        f"yarn install {PACKAGE_NAME}",
        f"yarn exec {PACKAGE_NAME}",
        f"yarn dlx {PACKAGE_NAME}",
        f"bun add {PACKAGE_NAME}",
        f"bun install {PACKAGE_NAME}",
        f"bunx {PACKAGE_NAME}",
        f"npx {PACKAGE_NAME}",
    ],
)
def test_detects_standard_package_manager_command_families(
    tmp_path: Path,
    command: str,
) -> None:
    """Detect the approved explicit manager and execution command forms."""
    repository, _ = _repository(tmp_path)
    path = "tools/install-consumer.sh"
    _write(repository, path, command + "\n")

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    "command",
    [
        f"npm --prefix ./tmp install {PACKAGE_NAME}",
        f"pnpm --dir ./tmp add {PACKAGE_NAME}",
        f"yarn --cwd ./tmp dlx {PACKAGE_NAME}",
        f"bun --cwd ./tmp add {PACKAGE_NAME}",
        f"npm --userconfig=/tmp/npmrc install {PACKAGE_NAME}",
        f"pnpm --config.strict=true add {PACKAGE_NAME}",
    ],
)
def test_detects_manager_options_before_subcommands(
    tmp_path: Path,
    command: str,
) -> None:
    """Permit standard manager-global options before the subcommand."""
    repository, _ = _repository(tmp_path)
    path = "tools/install-options.sh"
    _write(repository, path, command + "\n")

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    "command",
    [
        f"npm exec -- {PACKAGE_NAME}",
        f"npx -- {PACKAGE_NAME}",
        f"  npm install -- {PACKAGE_NAME}",
        f"\tpnpm add -- {PACKAGE_NAME}",
    ],
    ids=["npm-exec", "npx", "npm-install", "pnpm-add"],
)
def test_detects_exec_delimiter_before_the_package(
    tmp_path: Path,
    command: str,
) -> None:
    """Accept one bounded package delimiter before the exact package."""
    repository, _ = _repository(tmp_path)
    path = "tools/install-delimiter.sh"
    _write(repository, path, command + "\n")

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    "command",
    [
        f"npm exec other-tool -- {PACKAGE_NAME}",
        f"npx other-tool -- {PACKAGE_NAME}",
        f"npm install -- -- {PACKAGE_NAME}",
        f"pnpm add -- -- {PACKAGE_NAME}",
    ],
    ids=["npm-exec", "npx", "npm-install-double", "pnpm-add-double"],
)
def test_ignores_exec_delimiter_after_another_executable(
    tmp_path: Path,
    command: str,
) -> None:
    """Reject repeated delimiters or arguments to another executable."""
    repository, _ = _repository(tmp_path)
    _write(repository, "tools/install-delimiter.sh", command + "\n")

    assert scan_consumer_policy(repository).consumers == ()


def test_detects_python_subprocess_package_manager_command(
    tmp_path: Path,
) -> None:
    """Detect an explicit Python subprocess command without string dataflow."""
    repository, _ = _repository(tmp_path)
    path = "tools/install-consumer.py"
    _write(
        repository,
        path,
        (
            "import subprocess\n"
            f'subprocess.run(["npm", "install", "{PACKAGE_NAME}"])\n'
        ),
    )

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("path", "command"),
    [
        (
            "tools/install-call.cmd",
            f"call npm.cmd install {PACKAGE_NAME}",
        ),
        (
            "tools/install-cmd.bat",
            f'cmd.exe /d /s /c "pnpm add {PACKAGE_NAME}"',
        ),
        (
            "tools/install-powershell.ps1",
            f"& yarn.cmd add {PACKAGE_NAME}",
        ),
        (
            "tools/install-start.cmd",
            f'start "" /wait bun.exe add {PACKAGE_NAME}',
        ),
        (
            "tools/install-nested.ps1",
            f'powershell -Command "npx {PACKAGE_NAME}"',
        ),
    ],
)
def test_detects_common_windows_command_forms(
    tmp_path: Path,
    path: str,
    command: str,
) -> None:
    """Detect bounded CMD, BAT, and PowerShell execution forms."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, command + "\n")

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("path", "content"),
    [
        (
            "tools/install-start-process.ps1",
            (
                'Start-Process -FilePath "npm" '
                f'-aRgUmEnTlIsT @("exec", "--", "{PACKAGE_NAME}") '
                "-WorkingDirectory $PWD\n"
            ),
        ),
        (
            "tools/install-start-process-positional.ps1",
            f'Start-Process "npm" @("install", "{PACKAGE_NAME}")\n',
        ),
        (
            "tools/install-start-process-scalar.ps1",
            (
                "Start-Process -WorkingDirectory $PWD "
                '-FilePath "npm" -ArgumentList '
                f'"install -D {PACKAGE_NAME}"\n'
            ),
        ),
        (
            "tools/install-start-process-options.ps1",
            (
                f'Start-Process "npm" @("install", "-D", "{PACKAGE_NAME}") '
                "-WorkingDirectory $PWD\n"
            ),
        ),
        (
            "tools/postinstall-exec-file.js",
            (
                'import { execFile } from "node:child_process";\n'
                f'execFile("npm", ["exec", "--", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-exec-file-sync.js",
            (
                "const { execFileSync: run } = "
                'require("child_process");\n'
                f'run("npx", ["--", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-spawn.js",
            (
                'import * as childProcess from "node:child_process";\n'
                f'childProcess.spawn("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-spawn-sync.js",
            (
                'const childProcess = require("child_process");\n'
                f'childProcess.spawnSync("npx", ["--", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-exec.js",
            (
                'import { exec as run } from "node:child_process";\n'
                f'run("npm install {PACKAGE_NAME}");\n'
            ),
        ),
        (
            "tools/postinstall-exec-sync.js",
            (
                'require("child_process").execSync('
                f"`pnpm add -- '{PACKAGE_NAME}'`);\n"
            ),
        ),
        (
            "tools/install-call.py",
            (
                "import subprocess\n"
                f'subprocess.call(["npm", "install", "{PACKAGE_NAME}"])\n'
            ),
        ),
        (
            "tools/install-keyword.py",
            (
                "import subprocess\n"
                "subprocess.run("
                f'args=["npm", "install", "{PACKAGE_NAME}"])\n'
            ),
        ),
        (
            "tools/install-async.py",
            (
                "import asyncio\n"
                "async def install():\n"
                "    await asyncio.create_subprocess_exec("
                f'"npx", "--", "{PACKAGE_NAME}")\n'
            ),
        ),
        (
            "tools/postinstall-resolve.js",
            f'require.resolve("{PACKAGE_NAME}/client");\n',
        ),
    ],
    ids=[
        "powershell-start-process-named",
        "powershell-start-process-positional",
        "powershell-start-process-quoted-scalar",
        "powershell-start-process-array-options",
        "node-exec-file",
        "node-exec-file-sync",
        "node-spawn",
        "node-spawn-sync",
        "node-exec",
        "node-exec-sync",
        "python-subprocess-call",
        "python-subprocess-args-keyword",
        "python-create-subprocess-exec",
        "require-resolve",
    ],
)
def test_detects_exact_literal_process_and_resolution_apis(
    tmp_path: Path,
    path: str,
    content: str,
) -> None:
    """Detect only exact literal executable, argument, and specifier APIs."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("path", "content"),
    [
        (
            "node/package.json",
            lambda: json.dumps(
                {
                    "dependencies": {
                        "smoke-alias": f"npm:{PACKAGE_NAME}@1.0.0",
                    },
                },
            ),
        ),
        (
            "tools/install-alias.sh",
            lambda: f"npm install smoke-alias@npm:{PACKAGE_NAME}@1.0.0\n",
        ),
        (
            "tools/setup-import.mjs",
            lambda: f'import value from "{PACKAGE_NAME}/client";\n',
        ),
        (
            "node/package-lock.json",
            lambda: json.dumps(
                {"packages": {f"alias@npm:{PACKAGE_NAME}@1.0.0": {}}},
            ),
        ),
        (
            "node/package.json",
            lambda: json.dumps(
                {
                    "dependencies": {
                        "smoke-workspace": (f"workspace:{PACKAGE_NAME}@^1.0.0"),
                    },
                },
            ),
        ),
    ],
    ids=[
        "manifest-alias",
        "command-alias",
        "scoped-subpath-import",
        "lock-alias",
        "pnpm-workspace-alias",
    ],
)
def test_detects_aliases_and_exact_scoped_subpath_imports(
    tmp_path: Path,
    path: str,
    content: Any,
) -> None:
    """Detect exact npm aliases and scoped package subpath imports."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content())

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("path", "content"),
    [
        (
            "python/setup.py",
            (
                "import subprocess\n"
                f'subprocess.call(["npm", "install", "{PACKAGE_NAME}"])\n'
            ),
        ),
        (
            "dotnet/consumer.csproj",
            (
                '<Project><Target Name="Install">'
                f'<Exec Command="npm install {PACKAGE_NAME}" />'
                "</Target></Project>"
            ),
        ),
    ],
    ids=["setup-py-subprocess", "msbuild-exec"],
)
def test_detects_literal_manifest_process_commands(
    tmp_path: Path,
    path: str,
    content: str,
) -> None:
    """Detect literal setup.py and MSBuild package-manager execution."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("step", "expected_context"),
    [
        (
            {"uses": f"{PACKAGE_NAME}/action@v1"},
            "uses",
        ),
        (
            {
                "env": {"SMOKE": PACKAGE_NAME},
                "run": 'pnpm add "${{ env.SMOKE }}"',
            },
            "run",
        ),
        (
            {"with": {"install": f"npm install {PACKAGE_NAME}"}},
            "with",
        ),
    ],
    ids=["uses", "env-feeds-run", "with-feeds-command"],
)
def test_detects_concrete_workflow_uses_with_and_env_flows(
    tmp_path: Path,
    step: Mapping[str, object],
    expected_context: str,
) -> None:
    """Detect direct workflow values and exact local environment feeds."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/consume.yml"
    document = {"jobs": {"consume": {"steps": [step]}}}
    _write(repository, path, json.dumps(document))

    _assert_consumer(repository, path)
    contexts = POLICY._workflow(  # noqa: SLF001
        (repository / path).read_bytes(),
        repository_root=repository,
    )
    assert expected_context in contexts


def test_detects_job_level_reusable_workflow_command_input(
    tmp_path: Path,
) -> None:
    """Detect a concrete command passed to a reusable-workflow job."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/caller.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "uses": "./.github/workflows/install.yml",
                        "with": {
                            "command": f"npm exec -- {PACKAGE_NAME}",
                        },
                    },
                },
            },
        ),
    )

    _assert_consumer(repository, path)


def test_detects_static_matrix_and_reusable_default_flows(
    tmp_path: Path,
) -> None:
    """Resolve closed matrix values and literal reusable input defaults."""
    repository, _ = _repository(tmp_path)
    matrix_path = ".github/workflows/matrix.yml"
    default_path = ".github/workflows/default.yml"
    _write(
        repository,
        matrix_path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "package": [PACKAGE_NAME, "other-package"],
                            },
                        },
                        "steps": [
                            {
                                "run": ("npm install ${{ matrix.package }}"),
                            },
                        ],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        default_path,
        json.dumps(
            {
                "on": {
                    "workflow_call": {
                        "inputs": {
                            "package": {"default": PACKAGE_NAME},
                        },
                    },
                },
                "jobs": {
                    "consume": {
                        "steps": [
                            {
                                "run": ("npm install ${{ inputs.package }}"),
                            },
                        ],
                    },
                },
            },
        ),
    )

    result = scan_consumer_policy(repository)

    assert result.consumers == tuple(sorted((matrix_path, default_path)))


def test_detects_repository_local_reusable_caller_input_flow(
    tmp_path: Path,
) -> None:
    """Resolve one local reusable-workflow caller input into a run command."""
    repository, _ = _repository(tmp_path)
    callee = ".github/workflows/install.yml"
    caller = ".github/workflows/caller.yml"
    _write(
        repository,
        callee,
        json.dumps(
            {
                "on": {
                    "workflow_call": {
                        "inputs": {"package": {"required": True}},
                    },
                },
                "jobs": {
                    "consume": {
                        "steps": [
                            {
                                "run": ("npm install ${{ inputs.package }}"),
                            },
                        ],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        caller,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "uses": "./.github/workflows/install.yml",
                        "with": {"package": PACKAGE_NAME},
                    },
                },
            },
        ),
    )

    result = scan_consumer_policy(repository)

    assert result.consumers == (caller,)


def test_detects_workflow_dispatch_default_across_dual_trigger_alternatives(
    tmp_path: Path,
) -> None:
    """Resolve each static workflow input-default trigger independently."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/dispatch-default.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "on": {
                    "workflow_call": {
                        "inputs": {
                            "package": {"default": "other-package"},
                        },
                    },
                    "workflow_dispatch": {
                        "inputs": {
                            "package": {"default": PACKAGE_NAME},
                        },
                    },
                },
                "jobs": {
                    "consume": {
                        "steps": [
                            {
                                "run": ("npm install ${{ inputs.package }}"),
                            },
                        ],
                    },
                },
            },
        ),
    )

    _assert_consumer(repository, path)


def test_static_matrix_include_preserves_correlated_rows(
    tmp_path: Path,
) -> None:
    """Apply static include rows without cross-pairing their values."""
    repository, _ = _repository(tmp_path)
    positive = ".github/workflows/include-positive.yml"
    negative = ".github/workflows/include-negative.yml"
    for path, rows in (
        (
            positive,
            [
                {"manager": "npm", "package": PACKAGE_NAME},
                {"manager": "echo", "package": "other-package"},
            ],
        ),
        (
            negative,
            [
                {"manager": "echo", "package": PACKAGE_NAME},
                {"manager": "npm", "package": "other-package"},
            ],
        ),
    ):
        _write(
            repository,
            path,
            json.dumps(
                {
                    "jobs": {
                        "consume": {
                            "strategy": {
                                "matrix": {"include": rows},
                            },
                            "env": {
                                "MANAGER": "${{ matrix.manager }}",
                                "PACKAGE": "${{ matrix.package }}",
                            },
                            "steps": [
                                {
                                    "run": ("$MANAGER install $PACKAGE"),
                                },
                            ],
                        },
                    },
                },
            ),
        )

    assert scan_consumer_policy(repository).consumers == (positive,)


@pytest.mark.parametrize(
    ("scope", "variable"),
    [
        ("root", "$SMOKE"),
        ("job", "${SMOKE}"),
        ("step", "%SMOKE%"),
    ],
)
def test_resolves_transitive_workflow_environment_at_each_scope(
    tmp_path: Path,
    scope: str,
    variable: str,
) -> None:
    """Resolve literal input values through transitive environment aliases."""
    repository, _ = _repository(tmp_path)
    path = f".github/workflows/{scope}-env.yml"
    environment = {
        "SOURCE": "${{ inputs.package }}",
        "SMOKE": "${{ env.SOURCE }}",
    }
    job: dict[str, object] = {
        "steps": [{"run": f'npm install "{variable}"'}],
    }
    document: dict[str, object] = {
        "on": {
            "workflow_dispatch": {
                "inputs": {"package": {"default": PACKAGE_NAME}},
            },
        },
        "jobs": {"consume": job},
    }
    if scope == "root":
        document["env"] = environment
    elif scope == "job":
        job["env"] = environment
    else:
        job["steps"] = [
            {
                "env": environment,
                "run": f'npm install "{variable}"',
            },
        ]
    _write(repository, path, json.dumps(document))

    _assert_consumer(repository, path)


def test_resolves_local_reusable_input_through_environment_and_shell(
    tmp_path: Path,
) -> None:
    """Resolve a caller input through callee environment aliases."""
    repository, _ = _repository(tmp_path)
    callee = ".github/workflows/install-env.yml"
    caller = ".github/workflows/caller-env.yml"
    _write(
        repository,
        callee,
        json.dumps(
            {
                "on": {
                    "workflow_call": {
                        "inputs": {"package": {"required": True}},
                    },
                },
                "env": {"SOURCE": "${{ inputs.package }}"},
                "jobs": {
                    "consume": {
                        "env": {"SMOKE": "${{ env.SOURCE }}"},
                        "steps": [{"run": 'npm install "$SMOKE"'}],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        caller,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "uses": f"./{callee}",
                        "with": {"package": PACKAGE_NAME},
                    },
                },
            },
        ),
    )

    assert scan_consumer_policy(repository).consumers == (caller,)


def test_resolves_bracket_context_syntax_for_matrix_inputs_and_env(
    tmp_path: Path,
) -> None:
    """Resolve bounded bracket-form GitHub context references."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/bracket-contexts.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "on": {
                    "workflow_dispatch": {
                        "inputs": {
                            "package": {"default": PACKAGE_NAME},
                        },
                    },
                },
                "env": {"SOURCE": "${{ inputs['package'] }}"},
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "include": [{"manager": "npm"}],
                            },
                        },
                        "env": {
                            "MANAGER": '${{ matrix["manager"] }}',
                        },
                        "steps": [
                            {
                                "env": {
                                    "SMOKE": "${{ env['SOURCE'] }}",
                                },
                                "run": ("${{ env['MANAGER'] }} install $SMOKE"),
                            },
                        ],
                    },
                },
            },
        ),
    )

    _assert_consumer(repository, path)


def test_scans_composite_action_default_through_environment(
    tmp_path: Path,
) -> None:
    """Detect a direct composite default through input and env aliases."""
    repository, _ = _repository(tmp_path)
    path = ".github/actions/direct/action.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "inputs": {
                    "package": {"default": PACKAGE_NAME},
                },
                "runs": {
                    "using": "composite",
                    "steps": [
                        {
                            "env": {
                                "SOURCE": "${{ inputs['package'] }}",
                                "SMOKE": "${{ env['SOURCE'] }}",
                            },
                            "run": 'npm install "$SMOKE"',
                            "shell": "bash",
                        },
                    ],
                },
            },
        ),
    )

    _assert_consumer(repository, path)
    rule = classify_dependency_surface(path)
    assert rule is not None
    assert rule.category == "composite-action"


def test_workflow_follows_nested_local_composite_action_inputs(
    tmp_path: Path,
) -> None:
    """Propagate caller inputs and env through nested local actions."""
    repository, _ = _repository(tmp_path)
    inner = ".github/actions/inner/action.yaml"
    outer = ".github/actions/outer/action.yml"
    caller = ".github/workflows/action-caller.yml"
    _write(
        repository,
        inner,
        json.dumps(
            {
                "inputs": {"package": {"required": True}},
                "runs": {
                    "using": "composite",
                    "steps": [
                        {
                            "env": {
                                "SMOKE": "${{ inputs.package }}",
                            },
                            "run": 'npm install "$SMOKE"',
                            "shell": "bash",
                        },
                    ],
                },
            },
        ),
    )
    _write(
        repository,
        outer,
        json.dumps(
            {
                "inputs": {"package": {"required": True}},
                "runs": {
                    "using": "composite",
                    "steps": [
                        {
                            "uses": "./.github/actions/inner",
                            "with": {
                                "package": "${{ inputs.package }}",
                            },
                        },
                    ],
                },
            },
        ),
    )
    _write(
        repository,
        caller,
        json.dumps(
            {
                "on": {
                    "workflow_dispatch": {
                        "inputs": {
                            "package": {"default": PACKAGE_NAME},
                        },
                    },
                },
                "env": {"SMOKE": "${{ inputs.package }}"},
                "jobs": {
                    "consume": {
                        "steps": [
                            {
                                "uses": "./.github/actions/outer",
                                "with": {
                                    "package": "${{ env.SMOKE }}",
                                },
                            },
                        ],
                    },
                },
            },
        ),
    )

    assert scan_consumer_policy(repository).consumers == (caller,)


@pytest.mark.parametrize(
    ("kind", "message"),
    [
        ("cycle", "local composite action cycle"),
        ("depth", "local composite action depth exceeds"),
        ("non-regular", "not a regular file"),
        ("escape", "non-canonical local action path"),
    ],
)
def test_local_composite_action_resolution_fails_closed(
    tmp_path: Path,
    kind: str,
    message: str,
) -> None:
    """Reject unsafe local action graphs and manifest objects."""
    repository, _ = _repository(tmp_path)
    if kind == "cycle":
        for name, target in (("a", "b"), ("b", "a")):
            _write(
                repository,
                f".github/actions/{name}/action.yml",
                json.dumps(
                    {
                        "runs": {
                            "using": "composite",
                            "steps": [
                                {
                                    "uses": (f"./.github/actions/{target}"),
                                },
                            ],
                        },
                    },
                ),
            )
    elif kind == "depth":
        for index in range(MAX_LOCAL_ACTION_DEPTH + 1):
            steps = (
                [
                    {
                        "uses": (f"./.github/actions/depth-{index + 1}"),
                    },
                ]
                if index < MAX_LOCAL_ACTION_DEPTH
                else [{"run": "echo clean", "shell": "bash"}]
            )
            _write(
                repository,
                f".github/actions/depth-{index}/action.yml",
                json.dumps(
                    {
                        "runs": {
                            "using": "composite",
                            "steps": steps,
                        },
                    },
                ),
            )
    elif kind == "non-regular":
        destination = repository / ".github/actions/link/action.yml"
        destination.parent.mkdir(parents=True)
        destination.symlink_to("missing-action.yml")
    else:
        _write(
            repository,
            ".github/actions/escape/action.yml",
            json.dumps(
                {
                    "runs": {
                        "using": "composite",
                        "steps": [{"uses": "./../outside"}],
                    },
                },
            ),
        )

    with pytest.raises(ConsumerPolicyScanError, match=message):
        scan_consumer_policy(repository)


def test_ignores_noncomposite_identity_and_dynamic_local_action_uses(
    tmp_path: Path,
) -> None:
    """Ignore identity-only actions and unresolved local action paths."""
    repository, _ = _repository(tmp_path)
    _write(
        repository,
        ".github/actions/identity/action.yml",
        json.dumps(
            {
                "inputs": {
                    "command": {"required": True},
                },
                "runs": {
                    "using": "composite",
                    "steps": [
                        {
                            "run": "echo ${{ inputs.command }}",
                            "shell": "bash",
                        },
                    ],
                },
            },
        ),
    )
    _write(
        repository,
        ".github/actions/node/action.yml",
        json.dumps(
            {
                "inputs": {
                    "package": {"default": PACKAGE_NAME},
                },
                "runs": {
                    "using": "node20",
                    "main": "index.js",
                },
            },
        ),
    )
    _write(
        repository,
        ".github/workflows/identity-action.yml",
        json.dumps(
            {
                "jobs": {
                    "observe": {
                        "steps": [
                            {
                                "uses": ("./.github/actions/identity"),
                                "with": {
                                    "command": (f"npm install {PACKAGE_NAME}"),
                                },
                            },
                        ],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        ".github/workflows/dynamic-action.yml",
        json.dumps(
            {
                "jobs": {
                    "observe": {
                        "steps": [
                            {
                                "uses": ("./${{ inputs.action }}"),
                                "with": {"package": PACKAGE_NAME},
                            },
                        ],
                    },
                },
            },
        ),
    )

    assert scan_consumer_policy(repository).consumers == ()


def test_ignores_dynamic_defaults_unknown_rows_and_workflow_cycles(
    tmp_path: Path,
) -> None:
    """Bound unresolved expressions and cyclic value flows as nonmatches."""
    repository, _ = _repository(tmp_path)
    documents = {
        ".github/workflows/dynamic-default.yml": {
            "on": {
                "workflow_dispatch": {
                    "inputs": {
                        "package": {
                            "default": "${{ github.event.package }}",
                        },
                    },
                },
            },
            "jobs": {
                "consume": {
                    "steps": [
                        {"run": "npm install ${{ inputs.package }}"},
                    ],
                },
            },
        },
        ".github/workflows/include-unknown.yml": {
            "jobs": {
                "consume": {
                    "strategy": {
                        "matrix": {
                            "include": [
                                {
                                    "manager": "npm",
                                    "package": ("${{ github.event.package }}"),
                                },
                            ],
                        },
                    },
                    "steps": [
                        {
                            "run": (
                                "${{ matrix.manager }} install "
                                "${{ matrix.package }}"
                            ),
                        },
                    ],
                },
            },
        },
        ".github/workflows/env-cycle.yml": {
            "env": {
                "FIRST": "${{ env.SECOND }}",
                "SECOND": "${{ env.FIRST }}",
            },
            "jobs": {
                "consume": {
                    "steps": [{"run": "npm install $FIRST"}],
                },
            },
        },
    }
    for path, document in documents.items():
        _write(repository, path, json.dumps(document))

    assert scan_consumer_policy(repository).consumers == ()


def test_ignores_unresolved_matrix_and_identity_only_local_input_flow(
    tmp_path: Path,
) -> None:
    """Ignore unresolved expressions and non-command local inputs."""
    repository, _ = _repository(tmp_path)
    _write(
        repository,
        ".github/workflows/matrix-unresolved.yml",
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "package": ["${{ github.event.package }}"],
                            },
                        },
                        "steps": [
                            {
                                "run": ("npm install ${{ matrix.package }}"),
                            },
                        ],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        ".github/workflows/identity-callee.yml",
        json.dumps(
            {
                "on": {
                    "workflow_call": {
                        "inputs": {"package": {"required": True}},
                    },
                },
                "jobs": {
                    "observe": {
                        "steps": [
                            {"run": "echo ${{ inputs.package }}"},
                        ],
                    },
                },
            },
        ),
    )
    _write(
        repository,
        ".github/workflows/identity-caller.yml",
        json.dumps(
            {
                "jobs": {
                    "observe": {
                        "uses": ("./.github/workflows/identity-callee.yml"),
                        "with": {"package": PACKAGE_NAME},
                    },
                },
            },
        ),
    )

    assert scan_consumer_policy(repository).consumers == ()


@pytest.mark.parametrize(
    ("path", "content"),
    [
        (
            "tools/setup-comment.js",
            f"// npm install {PACKAGE_NAME}\n",
        ),
        (
            "tools/setup-constant.js",
            f'const packageName = "{PACKAGE_NAME}";\n',
        ),
        (
            "tools/setup-command-constant.js",
            f'const example = "npm install {PACKAGE_NAME}";\n',
        ),
        (
            "tools/setup-docstring.py",
            f'"""\nnpm install {PACKAGE_NAME}\n"""\n',
        ),
        (
            "tools/setup-template.js",
            f"const example = `\nnpm install {PACKAGE_NAME}\n`;\n",
        ),
        (
            "tools/install-quoted.sh",
            f'"npm install {PACKAGE_NAME}"\n',
        ),
        (
            "tools/install-unbounded-option.sh",
            f"npm --custom value install {PACKAGE_NAME}\n",
        ),
        (
            "tools/postinstall-dynamic.js",
            (
                'import { execFile } from "node:child_process";\n'
                'const manager = "npm";\n'
                f'execFile(manager, ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-dynamic-args.js",
            (
                'const { execFile } = require("child_process");\n'
                'const command = "install";\n'
                f'execFile("npm", [command, "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-dynamic-spawn.js",
            (
                'import * as childProcess from "node:child_process";\n'
                'const manager = "npm";\n'
                f'childProcess.spawn(manager, ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-dynamic-spawn-args.js",
            (
                'const childProcess = require("child_process");\n'
                'const command = "install";\n'
                "childProcess.spawnSync("
                f'"npm", [command, "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-dynamic-exec.js",
            (
                'import { exec } from "node:child_process";\n'
                f'const command = "npm install {PACKAGE_NAME}";\n'
                "exec(command);\n"
            ),
        ),
        (
            "tools/postinstall-concatenated-exec.js",
            (
                'const { exec } = require("child_process");\n'
                f'const packageName = "{PACKAGE_NAME}";\n'
                'exec("npm install " + packageName);\n'
            ),
        ),
        (
            "tools/postinstall-interpolated-exec.js",
            (
                'import { execSync } from "node:child_process";\n'
                f'const packageName = "{PACKAGE_NAME}";\n'
                "execSync(`npm install ${packageName}`);\n"
            ),
        ),
        (
            "tools/postinstall-regexp-exec.js",
            f'/npm/.exec("npm install {PACKAGE_NAME}");\n',
        ),
        (
            "tools/postinstall-unrelated-spawn.js",
            f'runner.spawn("npm", ["install", "{PACKAGE_NAME}"]);\n',
        ),
        (
            "tools/postinstall-unbound-exec.js",
            f'exec("npm install {PACKAGE_NAME}");\n',
        ),
        (
            "tools/postinstall-dynamic-resolve.js",
            (
                f'const specifier = "{PACKAGE_NAME}/client";\n'
                "require.resolve(specifier);\n"
            ),
        ),
        (
            "tools/install-dynamic.py",
            (
                'manager = "npm"\n'
                "import subprocess\n"
                f'subprocess.call([manager, "install", "{PACKAGE_NAME}"])\n'
            ),
        ),
        (
            "tools/install-dynamic-keyword.py",
            (
                f'command = ["npm", "install", "{PACKAGE_NAME}"]\n'
                "import subprocess\n"
                "subprocess.run(args=command)\n"
            ),
        ),
        (
            "tools/install-dynamic.ps1",
            (
                "Start-Process (Get-PackageManager) "
                f'-ArgumentList @("npm", "install", "{PACKAGE_NAME}")\n'
            ),
        ),
        (
            "tools/install-dynamic-args.ps1",
            (
                '$verb = "install"\n'
                "Start-Process -FilePath npm "
                f'-ArgumentList @($verb, "{PACKAGE_NAME}") '
                "-WorkingDirectory $PWD\n"
            ),
        ),
        (
            "tools/install-expression-args.ps1",
            (
                "Start-Process -FilePath npm "
                '-ArgumentList @("install", (Get-PackageName), '
                f'"{PACKAGE_NAME}")\n'
            ),
        ),
        (
            "tools/install-dynamic-scalar.ps1",
            (
                f'$package = "{PACKAGE_NAME}"\n'
                "Start-Process -FilePath npm "
                f'-ArgumentList "install $package {PACKAGE_NAME}"\n'
            ),
        ),
        (
            "tools/install-outer-option.ps1",
            (
                "Start-Process -FilePath npm "
                '-ArgumentList "install other-package" '
                f'-WorkingDirectory "npm install {PACKAGE_NAME}"\n'
            ),
        ),
        (
            "tools/install-unclosed-array.ps1",
            (
                "Start-Process -FilePath npm "
                f'-ArgumentList @("install", "{PACKAGE_NAME}" '
                "-WorkingDirectory $PWD\n"
            ),
        ),
        (
            "python/setup.py",
            (
                'manager = "npm"\n'
                "import subprocess\n"
                f'subprocess.call([manager, "install", "{PACKAGE_NAME}"])\n'
            ),
        ),
        (
            "dotnet/consumer.csproj",
            (
                '<Project><Target Name="Install">'
                '<Exec Command="$(InstallCommand)" />'
                "</Target></Project>"
            ),
        ),
        (
            "tools/install-echo.sh",
            f"echo npm install {PACKAGE_NAME}\n",
        ),
        (
            "tools/setup-adjacent.mjs",
            f'import "{PACKAGE_NAME}-extra/client";\n',
        ),
        (
            ".pnpmfile.cjs",
            f'const documentedPackage = "{PACKAGE_NAME}";\n',
        ),
        (
            ".github/workflows/identity.yml",
            json.dumps(
                {
                    "env": {"SMOKE": PACKAGE_NAME},
                    "jobs": {"noop": {"steps": [{"run": "echo clean"}]}},
                },
            ),
        ),
        (
            ".github/workflows/reusable-identity.yml",
            json.dumps(
                {
                    "jobs": {
                        "consume": {
                            "uses": "./.github/workflows/install.yml",
                            "with": {"package": PACKAGE_NAME},
                        },
                    },
                },
            ),
        ),
    ],
    ids=[
        "comment",
        "constant",
        "command-constant",
        "docstring",
        "template-literal",
        "standalone-quoted-command",
        "generic-option-with-separate-value",
        "dynamic-node-executable",
        "dynamic-node-arguments",
        "dynamic-spawn-executable",
        "dynamic-spawn-arguments",
        "dynamic-exec-command",
        "concatenated-exec-command",
        "interpolated-exec-template",
        "regexp-exec",
        "unrelated-spawn-method",
        "unbound-exec",
        "dynamic-require-resolve",
        "dynamic-python-executable",
        "dynamic-python-args-keyword",
        "dynamic-powershell-executable",
        "dynamic-powershell-arguments",
        "expression-powershell-arguments",
        "dynamic-powershell-scalar",
        "powershell-outer-option",
        "powershell-unclosed-array",
        "dynamic-setup-py",
        "msbuild-property",
        "echo",
        "adjacent-import",
        "dependency-config-constant",
        "unconsumed-workflow-env",
        "reusable-workflow-identity-only",
    ],
)
def test_ignores_identity_only_mentions_and_near_misses(
    tmp_path: Path,
    path: str,
    content: str,
) -> None:
    """Ignore comments, constants, prose-like commands, and adjacent names."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    result = scan_consumer_policy(repository)

    assert result.consumers == ()


def test_admits_only_the_three_exact_digest_bound_exceptions(
    tmp_path: Path,
) -> None:
    """Admit only the reviewed product and acceptance fixture bytes."""
    repository, target = _repository(tmp_path)

    result = scan_consumer_policy(repository)

    assert result.target == target
    assert result.consumers == ()
    assert tuple(
        surface.path for surface in result.admitted_exceptions
    ) == tuple(
        sorted(
            (
                OWN_DECLARATION_PATH,
                ACCEPTANCE_FIXTURE_PATH,
                ACCEPTANCE_NPM_MANIFEST_PATH,
            )
        ),
    )
    assert tuple(
        (
            item.path,
            item.category,
            item.context,
            item.content_digest,
        )
        for item in APPROVED_CONSUMER_EXCEPTIONS
    ) == (
        (
            OWN_DECLARATION_PATH,
            "dependency-manifest",
            "name",
            (
                "sha256:"
                "a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566"
            ),
        ),
        (
            ACCEPTANCE_FIXTURE_PATH,
            "dependency-manifest",
            f"dependencies.{PACKAGE_NAME}",
            (
                "sha256:"
                "a28d7f1e161df6948cdc2f122e78b9a38f425b481877178e29c8cd8ef30b0aa2"
            ),
        ),
        (
            ACCEPTANCE_NPM_MANIFEST_PATH,
            "dependency-manifest",
            "name",
            (
                "sha256:"
                "d032b543a77820f9660a629e7deee6140664150a2c0a7de8048d37947afc957e"
            ),
        ),
    )


def test_lf_attributes_preserve_exception_digests_with_autocrlf_checkout(
    tmp_path: Path,
) -> None:
    """Keep both digest-bound exception files byte-stable on CRLF hosts."""
    source, _ = _repository(tmp_path)
    checkout = tmp_path / "checkout"
    subprocess.run(  # noqa: S603
        (
            "git",
            "-c",
            "core.autocrlf=true",
            "clone",
            "--quiet",
            str(source),
            str(checkout),
        ),
        check=True,
        capture_output=True,
        text=True,
    )

    attributes = (checkout / GIT_ATTRIBUTES_PATH).read_text(
        encoding="utf-8",
    )
    for path in (
        OWN_DECLARATION_PATH,
        ACCEPTANCE_FIXTURE_PATH,
        ACCEPTANCE_NPM_MANIFEST_PATH,
    ):
        assert f"{path} text eol=lf" in attributes
        assert b"\r\n" not in (checkout / path).read_bytes()
    result = scan_consumer_policy(checkout)
    assert result.consumers == ()
    assert len(result.admitted_exceptions) == len(
        APPROVED_CONSUMER_EXCEPTIONS,
    )


@pytest.mark.parametrize(
    "mutation",
    ["fixture-context", "fixture-digest", "manifest-digest"],
)
def test_changed_exception_context_or_digest_is_a_consumer(
    tmp_path: Path,
    mutation: str,
) -> None:
    """Reject any context or byte mutation of an approved exception."""
    repository, _ = _repository(tmp_path)
    path = ACCEPTANCE_FIXTURE_PATH
    target = repository / path
    if mutation == "fixture-context":
        document = json.loads(target.read_text(encoding="utf-8"))
        document["devDependencies"] = document.pop("dependencies")
        target.write_text(json.dumps(document), encoding="utf-8")
    elif mutation == "fixture-digest":
        target.write_bytes(target.read_bytes() + b"\n")
    else:
        path = OWN_DECLARATION_PATH
        target = repository / path
        target.write_bytes(target.read_bytes() + b"\n")

    result = scan_consumer_policy(repository)

    assert result.consumers == (path,)
    assert path not in {surface.path for surface in result.admitted_exceptions}
    assert len(result.admitted_exceptions) == (
        len(APPROVED_CONSUMER_EXCEPTIONS) - 1
    )


def test_changed_exception_path_is_a_scan_error(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Fail closed when the exact approved exception path disappears."""
    repository, _ = _repository(tmp_path)
    source = repository / ACCEPTANCE_FIXTURE_PATH
    source.rename(source.with_name("consumer-policy-acceptance-moved.json"))

    return_code = main(["--repository-root", str(repository)])
    captured = capsys.readouterr()

    assert return_code == SCAN_ERROR_EXIT_CODE
    assert captured.out == ""
    assert "result=scan-error" in captured.err
    assert "approved consumer-policy exception is missing" in captured.err


@pytest.mark.parametrize(
    ("path", "content"),
    [
        ("node/package.json", b"{"),
        ("python/pyproject.toml", b"[project"),
        ("dotnet/consumer.csproj", b"<Project>"),
        (".github/workflows/consume.yml", b"jobs: ["),
        (".github/actions/consume/action.yml", b"runs: ["),
        ("tools/setup-consumer.py", b"\xff"),
        ("tools/setup-syntax.py", b"def broken(:\n"),
        ("renovate.json", b"{"),
    ],
    ids=[
        "json",
        "toml",
        "xml",
        "yaml",
        "composite-action-yaml",
        "unreadable-text",
        "python-syntax",
        "config",
    ],
)
def test_malformed_or_unreadable_cataloged_surfaces_are_scan_errors(
    tmp_path: Path,
    path: str,
    content: bytes,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Classify structured parse and UTF-8 failures as scan errors."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    with pytest.raises(ConsumerPolicyScanError):
        scan_consumer_policy(repository)
    return_code = main(["--repository-root", str(repository)])
    captured = capsys.readouterr()

    assert return_code == SCAN_ERROR_EXIT_CODE
    assert captured.out == ""
    assert captured.err.startswith("consumer-policy result=scan-error:")
    assert path in captured.err


def test_tracked_dangling_catalog_symlink_is_a_scan_error(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject a tracked dangling symlink before following existence checks."""
    repository, _ = _repository(tmp_path)
    path = "consumer/package.json"
    destination = repository / path
    destination.parent.mkdir(parents=True)
    destination.symlink_to("missing-package.json")
    _git(repository, "add", path)
    _git(repository, "commit", "--quiet", "--message", "dangling surface")

    with pytest.raises(ConsumerPolicyScanError):
        scan_consumer_policy(repository)
    return_code = main(["--repository-root", str(repository)])
    captured = capsys.readouterr()

    assert return_code == SCAN_ERROR_EXIT_CODE
    assert captured.out == ""
    assert path in captured.err
    assert "not a regular file" in captured.err
    assert "Traceback" not in captured.err


def test_output_categories_exit_codes_and_findings_are_deterministic(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Emit explicit categories and sorted complete consumer findings."""
    repository, _ = _repository(tmp_path)

    clean_code = main(["--repository-root", str(repository)])
    clean = json.loads(capsys.readouterr().out)
    _write(
        repository,
        "z/package.json",
        json.dumps({"dependencies": {PACKAGE_NAME: "1"}}),
    )
    _write(
        repository,
        "a/package.json",
        json.dumps({"dependencies": {PACKAGE_NAME: "1"}}),
    )

    consumer_code = main(["--repository-root", str(repository)])
    captured = capsys.readouterr()
    consumer = json.loads(captured.out)

    assert clean_code == 0
    assert clean["result"] == "clean"
    assert clean["consumers"] == []
    assert consumer_code == 1
    assert captured.err == ""
    assert consumer["result"] == "consumer"
    assert consumer["consumers"] == ["a/package.json", "z/package.json"]
    paths = [item["path"] for item in consumer["scanned-surfaces"]]
    assert paths == sorted(paths)


def test_policy_implementation_is_cataloged_without_counting_its_identity(
    tmp_path: Path,
) -> None:
    """Scan the policy source without treating its package constant as use."""
    repository, _ = _repository(tmp_path)
    _write(
        repository,
        POLICY_IMPLEMENTATION_PATH,
        (REPO_ROOT / POLICY_IMPLEMENTATION_PATH).read_bytes(),
    )

    result = scan_consumer_policy(repository)
    rule = classify_dependency_surface(POLICY_IMPLEMENTATION_PATH)

    assert rule is not None
    assert rule.category == "install-bootstrap-script"
    assert POLICY_IMPLEMENTATION_PATH in {
        surface.path for surface in result.scanned_surfaces
    }
    assert result.consumers == ()


def test_hk_trigger_inventory_has_exhaustive_policy_catalog_parity() -> None:
    """Mechanically prove HK and policy trigger inventories are identical."""
    hk_config = (REPO_ROOT / "hk.pkl").read_text(encoding="utf-8")
    start = hk_config.index(
        "local hcoona_release_smoke_npm_consumer_policy_files",
    )
    list_start = hk_config.index("List(", start)
    list_end = hk_config.index("\n    )", list_start)
    hk_globs = tuple(
        re.findall(r'"([^"]+)"', hk_config[list_start:list_end]),
    )

    assert len(hk_globs) == len(set(hk_globs))
    assert tuple(sorted(hk_globs)) == CONSUMER_POLICY_HK_GLOBS
    assert GIT_ATTRIBUTES_PATH in hk_globs
    assert "**/.gitattributes" not in hk_globs
    assert classify_dependency_surface(GIT_ATTRIBUTES_PATH) is not None
    assert classify_dependency_surface("nested/.gitattributes") is None
    assert "hk.pkl" in hk_globs
    assert POLICY_IMPLEMENTATION_PATH in hk_globs
