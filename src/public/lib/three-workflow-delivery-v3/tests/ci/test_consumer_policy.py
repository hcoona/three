"""Contracts for the permanent smoke-package consumer policy."""

from __future__ import annotations

import importlib.util
import json
import re
import shutil
import subprocess
import sys
from collections import Counter
from pathlib import Path
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.release import javascript_consumer
from three_workflow_delivery_v3.release.consumer_policy import (
    CONSUMER_POLICY_DIGEST,
    JAVASCRIPT_ANALYSIS_SEMANTICS_ID,
    JAVASCRIPT_AST_DEPTH_LIMIT,
    JAVASCRIPT_AST_NODE_LIMIT,
    JAVASCRIPT_COMMONJS_GLOBAL_SUFFIXES,
    JAVASCRIPT_RELEVANT_UNKNOWN_ADMISSION_RULE,
    JAVASCRIPT_SOURCE_BYTE_LIMIT,
    JAVASCRIPT_SUPPORTED_CONSTRUCTS,
    JAVASCRIPT_UNKNOWN_ADMISSION_POLICY,
    NODE_DEPENDENCY_FIELDS,
    TREE_SITTER_JAVASCRIPT_VERSION,
    TREE_SITTER_TYPESCRIPT_VERSION,
    TREE_SITTER_VERSION,
    consumer_policy_document,
    consumer_policy_parser_profile,
)

if TYPE_CHECKING:
    from collections.abc import Mapping
    from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[6]
SCAN_ERROR_EXIT_CODE = 2
RELEVANT_JAVASCRIPT_ERROR = "relevant unsupported JavaScript consumer flow"


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


def _assert_javascript_scan_error(repository: Path, path: str) -> None:
    with pytest.raises(ConsumerPolicyScanError) as captured:
        scan_consumer_policy(repository)

    assert captured.value.findings == (f"{path}: {RELEVANT_JAVASCRIPT_ERROR}",)


def _scan_javascript(
    source: str | bytes,
    *,
    language: javascript_consumer.JavaScriptLanguage = "javascript",
    pnpmfile: bool = False,
    commonjs_globals: bool = True,
) -> bool:
    content = source.encode() if isinstance(source, str) else source
    return javascript_consumer.scan_javascript_consumer(
        content,
        language=language,
        manager_reference=POLICY._manager_references,  # noqa: SLF001
        pnpmfile=pnpmfile,
        commonjs_globals=commonjs_globals,
    )


def _assert_javascript_outcome(
    source: str,
    outcome: str,
    *,
    language: javascript_consumer.JavaScriptLanguage = "javascript",
    pnpmfile: bool = False,
    commonjs_globals: bool = True,
) -> None:
    if outcome == "error":
        with pytest.raises(ValueError, match=RELEVANT_JAVASCRIPT_ERROR):
            _scan_javascript(
                source,
                language=language,
                pnpmfile=pnpmfile,
                commonjs_globals=commonjs_globals,
            )
        return
    assert _scan_javascript(
        source,
        language=language,
        pnpmfile=pnpmfile,
        commonjs_globals=commonjs_globals,
    ) is (outcome == "consumer")


def _j(case: str, source: str, outcome: str) -> tuple[str, str, str, str]:
    return case, "javascript", source, outcome


_P = PACKAGE_NAME
_SPAWN_IMPORT = 'import { spawn } from "node:child_process";\n'


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
            "tools/postinstall-const-manager.js",
            (
                'import { execFile } from "node:child_process";\n'
                'const manager = "npm";\n'
                f'execFile(manager, ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-const-argv.mjs",
            (
                'import { execFile as run } from "child_process";\n'
                'const command = "install";\n'
                f'const argv = [command, "{PACKAGE_NAME}"];\n'
                'run("npm", argv);\n'
            ),
        ),
        (
            "tools/postinstall-immutable-aliases.cjs",
            (
                'const child = require("node:child_process");\n'
                "const launch = child.spawnSync;\n"
                "const manager = `/opt/tools/pnpm`;\n"
                f'const original = ["add", "{PACKAGE_NAME}"];\n'
                "const argv = original;\n"
                "launch(manager, argv);\n"
            ),
        ),
        (
            "tools/postinstall-computed-static.js",
            (
                'const child = require("child_process");\n'
                f'child["spawn"]("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-typescript.ts",
            (
                'import { execFile } from "node:child_process";\n'
                'const manager = ("npm" as string)!;\n'
                f'const argv: string[] = ["install", "{PACKAGE_NAME}"];\n'
                "execFile(manager satisfies string, argv);\n"
            ),
        ),
        (
            "tools/postinstall-import-equals.ts",
            (
                'import child = require("node:child_process");\n'
                f'child.spawn("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "tools/postinstall-import-equals-package.ts",
            f'import smoke = require("{PACKAGE_NAME}");\n',
        ),
        (
            "tools/postinstall-require-resolve.js",
            (
                f'const specifier = "{PACKAGE_NAME}/client";\n'
                "require.resolve(specifier);\n"
            ),
        ),
        (
            "tools/postinstall-require-package.cjs",
            f'const loaded = require("{PACKAGE_NAME}");\n',
        ),
        (
            "tools/postinstall-import-package.mjs",
            f'void import("{PACKAGE_NAME}/client");\n',
        ),
    ],
    ids=[
        "const-manager",
        "const-argv",
        "immutable-aliases",
        "computed-static-member",
        "typescript-wrappers",
        "typescript-import-equals-child-process",
        "typescript-import-equals-package",
        "require-resolve-const",
        "require-package",
        "exact-dynamic-import-package",
    ],
)
def test_rc033_supported_static_subset_detects_consumers(
    tmp_path: Path,
    path: str,
    content: str,
) -> None:
    """Detect the explicitly supported immutable JavaScript forms."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("case", "content"),
    [
        (
            "commonjs-loader",
            rf'requ\u0069re("{PACKAGE_NAME}");',
        ),
        (
            "child-process-member",
            (
                'const child = require("node:child_process");\n'
                rf'child.sp\u0061wn("npm", ["install", "{PACKAGE_NAME}"]);'
            ),
        ),
    ],
)
def test_rc033_decodes_identifier_escapes_in_sensitive_positions(
    tmp_path: Path,
    case: str,
    content: str,
) -> None:
    """Normalize escaped identifiers before matching sensitive bindings."""
    repository, _ = _repository(tmp_path)
    path = f"tools/postinstall-escaped-identifier-{case}.cjs"
    _write(repository, path, content)

    _assert_consumer(repository, path)


def test_rc033_decoded_identifier_shadowing_remains_fail_closed(
    tmp_path: Path,
) -> None:
    """Preserve file-global ambiguity for an escaped loader declaration."""
    repository, _ = _repository(tmp_path)
    path = "tools/postinstall-shadowed-escaped-identifier.cjs"
    _write(
        repository,
        path,
        (
            r"const requ\u0069re = (value) => value;"
            rf'requ\u0069re("{PACKAGE_NAME}");'
        ),
    )

    _assert_javascript_scan_error(repository, path)


@pytest.mark.parametrize(
    ("case", "body"),
    [
        (
            "mutable-manager",
            (
                'let manager = "npm";\n'
                f'execFile(manager, ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "mutated-argv",
            (
                'const argv = ["install"];\n'
                f'argv.push("{PACKAGE_NAME}");\n'
                'execFile("npm", argv);\n'
            ),
        ),
        (
            "unknown-reference-pass",
            (
                f'const argv = ["install", "{PACKAGE_NAME}"];\n'
                "observe(argv);\n"
                'execFile("npm", argv);\n'
            ),
        ),
        (
            "escaped-array-element",
            (
                'const command = "install";\n'
                f'const argv = [command, "{PACKAGE_NAME}"];\n'
                "observe(command);\n"
                'execFile("npm", argv);\n'
            ),
        ),
        (
            "aggregate-escaped-array-element",
            (
                'const command = "install";\n'
                f'const argv = [command, "{PACKAGE_NAME}"];\n'
                "observe({ command });\n"
                'execFile("npm", argv);\n'
            ),
        ),
        (
            "dynamic-member",
            (
                'const child = require("child_process");\n'
                'const method = "spawn";\n'
                "child[getMethod(method)]("
                f'"npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "concatenated-command",
            (
                f'const packageName = "{PACKAGE_NAME}";\n'
                'exec("npm install " + packageName);\n'
            ),
        ),
        (
            "split-binary-package",
            (
                'const packageName = "@hcoona/" + '
                '"hcoona-release-smoke-npm";\n'
                'execFile("npm", ["install", packageName]);\n'
            ),
        ),
        (
            "array-hole",
            f'execFile("npm", [, "install", "{PACKAGE_NAME}"]);\n',
        ),
    ],
    ids=[
        "mutable-manager",
        "mutated-argv",
        "unknown-reference-pass",
        "escaped-array-element",
        "aggregate-escaped-array-element",
        "dynamic-member",
        "concatenated-command",
        "split-binary-package",
        "array-hole",
    ],
)
def test_rc033_unresolved_required_data_fails_closed(
    tmp_path: Path,
    case: str,
    body: str,
) -> None:
    """Reject package-relevant calls outside the immutable subset."""
    repository, _ = _repository(tmp_path)
    path = f"tools/postinstall-{case}.js"
    _write(
        repository,
        path,
        'import { execFile, exec } from "node:child_process";\n' + body,
    )

    _assert_javascript_scan_error(repository, path)


@pytest.mark.parametrize(
    ("case", "content"),
    [
        (
            "local-wrapper",
            (
                'import { spawn } from "node:child_process";\n'
                "function launch(manager, argv) { spawn(manager, argv); }\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "closure",
            (
                'import { spawn } from "node:child_process";\n'
                "const launch = () => "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "async",
            (
                'import { spawn } from "node:child_process";\n'
                "async function install() {\n"
                f'  spawn("npm", ["install", "{PACKAGE_NAME}"]);\n'
                "}\n"
            ),
        ),
        (
            "class",
            (
                'import { spawn } from "node:child_process";\n'
                "class Launcher { run() { "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " } }\n"
            ),
        ),
        (
            "for-redeclaration",
            (
                'import { spawn } from "node:child_process";\n'
                "for (const spawn of launchers) { "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " }\n"
            ),
        ),
        (
            "switch-redeclaration",
            (
                'import { spawn } from "node:child_process";\n'
                "switch (mode) { case 1: const spawn = runner; "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " }\n"
            ),
        ),
        (
            "unknown-child-process-import",
            (
                'import { fork } from "node:child_process";\n'
                f'fork("{PACKAGE_NAME}");\n'
            ),
        ),
        (
            "relevant-shadow",
            (
                'import { spawn } from "node:child_process";\n'
                "function inspect(spawn) { "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " }\n"
            ),
        ),
    ],
    ids=[
        "local-wrapper",
        "closure",
        "async",
        "class",
        "for-redeclaration",
        "switch-redeclaration",
        "unknown-child-process-import",
        "relevant-shadow",
    ],
)
def test_rc033_relevant_unsupported_flows_fail_closed(
    tmp_path: Path,
    case: str,
    content: str,
) -> None:
    """Fail closed instead of modeling unsupported JavaScript runtime flow."""
    repository, _ = _repository(tmp_path)
    path = f"tools/postinstall-unsupported-{case}.js"
    _write(repository, path, content)

    _assert_javascript_scan_error(repository, path)


@pytest.mark.parametrize(
    ("case", "content"),
    [
        (
            "exact-non-manager",
            (
                'import { spawn } from "node:child_process";\n'
                f'spawn("echo", ["{PACKAGE_NAME}"]);\n'
            ),
        ),
        (
            "scalar-metadata",
            (
                'import { spawn } from "node:child_process";\n'
                f'const metadata = "{PACKAGE_NAME}";\n'
                'spawn("echo", []);\n'
            ),
        ),
        (
            "type-only-modules",
            (
                'import { spawn } from "node:child_process";\n'
                f'import {{ type Package }} from "{PACKAGE_NAME}";\n'
                f'export type {{ Package }} from "{PACKAGE_NAME}";\n'
                'spawn("echo", []);\n'
            ),
        ),
        (
            "regex",
            (
                'import { spawn } from "node:child_process";\n'
                "const pattern = "
                f"/{re.escape(PACKAGE_NAME).replace('/', r'\/')}/;\n"
                'spawn("echo", []);\n'
            ),
        ),
        (
            "case-and-slash-near-misses",
            (
                'import { spawn } from "node:child_process";\n'
                f'spawn("npm", ["install", "{PACKAGE_NAME.upper()}"]);\n'
                'spawn("npm", ["install", '
                f'"{PACKAGE_NAME.replace("/", "\\\\")}"]);\n'
            ),
        ),
    ],
    ids=[
        "exact-non-manager",
        "scalar-metadata",
        "type-only-modules",
        "regex",
        "case-and-slash-near-misses",
    ],
)
def test_rc033_harmless_identity_and_near_misses_remain_clean(
    tmp_path: Path,
    case: str,
    content: str,
) -> None:
    """Keep identity-only and unrelated unsupported syntax non-consuming."""
    repository, _ = _repository(tmp_path)
    suffix = "ts" if case in {"type-only-modules", "types-only"} else "js"
    path = f"tools/postinstall-clean-{case}.{suffix}"
    _write(repository, path, content)

    assert scan_consumer_policy(repository).consumers == ()


@pytest.mark.parametrize(
    ("case", "literal"),
    [
        ("unicode-escape", r'"\u0040hcoona/hcoona-release-smoke-npm"'),
        ("hex-escape", r'"\x40hcoona/hcoona-release-smoke-npm"'),
        ("braced-unicode", r'"\u{40}hcoona/hcoona-release-smoke-npm"'),
        ("escaped-slash", r'"@hcoona\/hcoona-release-smoke-npm"'),
        ("identity-escape", r'"\@hcoona/hcoona-release-smoke-npm"'),
        ("legacy-octal", r'"\100hcoona/hcoona-release-smoke-npm"'),
        (
            "line-continuation",
            '"@hcoona/\\\nhcoona-release-smoke-npm"',
        ),
        ("template", f"`{PACKAGE_NAME}`"),
    ],
)
def test_rc033_decodes_exact_string_and_template_values(
    tmp_path: Path,
    case: str,
    literal: str,
) -> None:
    """Classify decoded literals rather than matching source spelling."""
    repository, _ = _repository(tmp_path)
    suffix = "cjs" if case == "legacy-octal" else "js"
    path = f"tools/postinstall-decoded-{case}.{suffix}"
    _write(
        repository,
        path,
        (
            'import { spawn } from "node:child_process";\n'
            f'spawn("npm", ["install", {literal}]);\n'
        ),
    )

    _assert_consumer(repository, path)


@pytest.mark.parametrize(
    ("pattern", "initializer"),
    [
        ("{ launch = spawn }", "{}"),
        ("[launch = spawn]", "[]"),
    ],
    ids=["object", "array"],
)
def test_rc033_destructuring_defaults_with_sensitive_aliases_fail_closed(
    pattern: str,
    initializer: str,
) -> None:
    """Keep sensitive destructuring defaults inside the unsupported barrier."""
    _assert_javascript_outcome(
        (
            'import { spawn } from "node:child_process";\n'
            f"const {pattern} = {initializer};\n"
            f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
        ),
        "error",
    )


@pytest.mark.parametrize(
    ("_case", "source", "outcome"),
    [
        (
            "assignment-target",
            (
                'import { spawn } from "node:child_process";\n'
                "let launch;\n"
                "({ launch = spawn } = {});\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "function-parameter",
            (
                'import { spawn } from "node:child_process";\n'
                "function run(launch = spawn) {\n"
                f'  launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
                "}\n"
            ),
            "error",
        ),
        (
            "harmless-function-parameter",
            (
                "function run(value = harmless) {\n"
                f'  value("{PACKAGE_NAME}");\n'
                "}\n"
            ),
            "clean",
        ),
    ],
)
def test_rc033_defaults_in_unsupported_contexts_preserve_relevance(
    _case: str,
    source: str,
    outcome: str,
) -> None:
    """Track only direct default sensitivity outside variable declarations."""
    _assert_javascript_outcome(source, outcome)


@pytest.mark.parametrize(
    ("source", "outcome"),
    [
        (
            (
                'import { spawn } from "node:child_process";\n'
                "const { nested: { launch = spawn } } = { nested: {} };\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            (
                'import { spawn } from "node:child_process";\n'
                "const { nested: { launch } = spawn } = {};\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "clean",
        ),
    ],
    ids=["nested-direct-default", "aggregate-default"],
)
def test_rc033_nested_patterns_track_only_direct_defaults(
    source: str,
    outcome: str,
) -> None:
    """Recurse through patterns without spreading aggregate defaults."""
    _assert_javascript_outcome(source, outcome)


@pytest.mark.parametrize(
    ("target", "values", "outcome"),
    [
        (
            "{ launch = spawn }",
            "[{}]",
            "error",
        ),
        (
            "[launch = spawn]",
            "[[]]",
            "error",
        ),
        (
            "const { launch = spawn }",
            "[{}]",
            "error",
        ),
        (
            "{ launch = harmless }",
            "[{}]",
            "clean",
        ),
    ],
    ids=["object-assignment", "array-assignment", "declaration", "harmless"],
)
def test_rc033_loop_targets_preserve_direct_default_relevance(
    target: str,
    values: str,
    outcome: str,
) -> None:
    """Index direct loop-target defaults without modeling iterations."""
    declaration = "" if target.startswith("const ") else "let launch;\n"
    source = (
        'import { spawn } from "node:child_process";\n'
        f"const harmless = () => undefined;\n{declaration}"
        f"for ({target} of {values}) {{\n"
        f'  launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
        "}\n"
    )
    _assert_javascript_outcome(source, outcome)


@pytest.mark.parametrize(
    ("options", "outcome"),
    [
        ("{ shell: true }", "consumer"),
        ('{ "shell": "/bin/sh" }', "consumer"),
        ("{ shell: false }", "clean"),
        ("{ shell: enabled }", "error"),
        ("{ get shell() { return true; } }", "error"),
        ('{ get cwd() { return "/tmp"; } }', "clean"),
    ],
)
def test_rc033_static_shell_options_control_command_matching(
    options: str,
    outcome: str,
) -> None:
    """Match full commands only when the direct shell option can enable them."""
    _assert_javascript_outcome(
        (
            'const child = require("node:child_process");\n'
            "child.spawnSync("
            f'"npm install {PACKAGE_NAME}", {options}'
            ");\n"
        ),
        outcome,
    )


def test_rc033_third_argument_shell_option_controls_command_matching() -> None:
    """Honor the explicit options position when argv is present."""
    _assert_javascript_outcome(
        (
            'const child = require("node:child_process");\n'
            "child.spawnSync("
            f'"npm install {PACKAGE_NAME}", [], {{ shell: true }}'
            ");\n"
        ),
        "consumer",
    )


@pytest.mark.parametrize(
    ("executable", "argv", "outcome"),
    [
        (
            "npm install",
            f'["{PACKAGE_NAME}"]',
            "consumer",
        ),
        (
            "echo",
            f'["{PACKAGE_NAME}"]',
            "clean",
        ),
    ],
)
def test_rc033_enabled_shell_matches_exact_command_and_argv(
    executable: str,
    argv: str,
    outcome: str,
) -> None:
    """Use Node's exact shell concatenation without interpreting the shell."""
    _assert_javascript_outcome(
        (
            'const child = require("node:child_process");\n'
            f'child.spawnSync("{executable}", {argv}, {{ shell: true }});\n'
        ),
        outcome,
    )


@pytest.mark.parametrize(
    ("executable", "argv", "options", "outcome"),
    [
        (
            f"npm install {PACKAGE_NAME}",
            "[]",
            "options",
            "error",
        ),
        (
            f"npm install {PACKAGE_NAME}",
            "[]",
            "",
            "clean",
        ),
        (
            f"echo {PACKAGE_NAME}",
            "[]",
            "options",
            "clean",
        ),
        (
            "npm install",
            f'["{PACKAGE_NAME}"]',
            "options",
            "error",
        ),
        (
            "npm install",
            f'["{PACKAGE_NAME}"]',
            "",
            "clean",
        ),
    ],
)
def test_rc033_nonliteral_third_options_remain_distinct_from_absent(
    executable: str,
    argv: str,
    options: str,
    outcome: str,
) -> None:
    """Fail closed when an unresolved third argument can enable a consumer."""
    declaration = "const options = { shell: true };\n" if options else ""
    third = f", {options}" if options else ""
    _assert_javascript_outcome(
        (
            'const child = require("node:child_process");\n'
            f"{declaration}"
            f'child.spawnSync("{executable}", {argv}{third});\n'
        ),
        outcome,
    )


@pytest.mark.parametrize(
    ("executable", "package", "outcome"),
    [
        ("npm install", PACKAGE_NAME, "error"),
        ("echo", PACKAGE_NAME, "clean"),
        ("npm install", "unrelated-package", "clean"),
    ],
)
def test_rc033_enabled_shell_with_unresolved_argv_uses_exact_prefix(
    executable: str,
    package: str,
    outcome: str,
) -> None:
    """Fail closed only for a relevant exact package-manager prefix."""
    _assert_javascript_outcome(
        (
            'const child = require("node:child_process");\n'
            f'let argv = ["{package}"];\n'
            f'child.spawnSync("{executable}", argv, {{ shell: true }});\n'
        ),
        outcome,
    )


@pytest.mark.parametrize("outcome", ["clean", "consumer"])
def test_rc033_exact_manager_results_are_memoized(
    outcome: str,
) -> None:
    """Evaluate each distinct exact manager input only once per scan."""
    calls: Counter[tuple[str, tuple[str, ...] | None]] = Counter()
    consumer = outcome == "consumer"

    def manager(executable: str, arguments: tuple[str, ...] | None) -> bool:
        calls[(executable, arguments)] += 1
        return POLICY._manager_references(  # noqa: SLF001
            executable, arguments
        )

    package = f', "{PACKAGE_NAME}"' if consumer else ""
    source = (
        'import { spawn } from "node:child_process";\n'
        f'const argv = ["install"{package}];\n'
        + 'spawn("npm", argv);\n' * 128
        + ("" if consumer else f'const metadata = "{PACKAGE_NAME}";\n')
    )

    assert (
        javascript_consumer.scan_javascript_consumer(
            source.encode(),
            language="javascript",
            manager_reference=manager,
        )
        is consumer
    )
    assert calls
    assert set(calls.values()) == {1}


@pytest.mark.parametrize(
    ("case", "language", "source", "outcome"),
    [
        (
            "comments-in-arguments",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                'spawn(/* executable */ "npm", /* argv */ '
                f'["install", "{PACKAGE_NAME}"] /* trailing */);\n'
            ),
            "consumer",
        ),
        (
            "create-require-child-process",
            "javascript",
            (
                'import { createRequire } from "node:module";\n'
                "const load = createRequire(import.meta.url);\n"
                'const child = load("node:child_process");\n'
                f'child.spawn("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "consumer",
        ),
        (
            "get-builtin-child-process",
            "javascript",
            (
                'const child = process.getBuiltinModule("child_process");\n'
                f'child.spawnSync("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "consumer",
        ),
        (
            "unrelated-loader-modules",
            "javascript",
            (
                'import { createRequire } from "node:module";\n'
                "const load = createRequire(import.meta.url);\n"
                'load("node:path");\n'
                'process.getBuiltinModule("node:url");\n'
                f'const metadata = "{PACKAGE_NAME}";\n'
            ),
            "clean",
        ),
        (
            "structured-punctuation",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'spawn("npm", ["install", "{PACKAGE_NAME},"]);\n'
                f'spawn("npm", ["install", "\\"{PACKAGE_NAME}\\""]);\n'
            ),
            "clean",
        ),
        (
            "second-argument-options",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'spawn("npm", {{ env: {{ PACKAGE: "{PACKAGE_NAME}" }} }});\n'
            ),
            "clean",
        ),
        (
            "sequence-final-harmless",
            "javascript",
            f'(eval, harmless)("{PACKAGE_NAME}");\n',
            "clean",
        ),
        (
            "sequence-final-eval",
            "javascript",
            f'(harmless, eval)("{PACKAGE_NAME}");\n',
            "error",
        ),
        (
            "cross-function-package",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'function packageName() {{ return "{PACKAGE_NAME}"; }}\n'
                'spawn("npm", ["install", packageName()]);\n'
            ),
            "error",
        ),
        (
            "global-receiver-mutation",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'globalThis.args.push("{PACKAGE_NAME}");\n'
                'spawn("npm", globalThis.args);\n'
            ),
            "error",
        ),
        (
            "runtime-namespace",
            "typescript",
            (
                'import { spawn } from "node:child_process";\n'
                "namespace Tools { "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " }\n"
            ),
            "error",
        ),
        (
            "runtime-module",
            "typescript",
            (
                'import { spawn } from "node:child_process";\n'
                "module Tools { "
                f'spawn("npm", ["install", "{PACKAGE_NAME}"]);'
                " }\n"
            ),
            "error",
        ),
        (
            "ambient-namespace",
            "typescript",
            (
                "declare namespace Docs { "
                f'type Package = import("{PACKAGE_NAME}").Thing;'
                " }\n"
            ),
            "clean",
        ),
        (
            "ambient-module",
            "typescript",
            (
                'declare module "docs" { '
                f'type Package = import("{PACKAGE_NAME}").Thing;'
                " }\n"
            ),
            "clean",
        ),
        (
            "computed-member-const",
            "javascript",
            (
                'const child = require("node:child_process");\n'
                'const method = "spawn";\n'
                f'child[method]("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "consumer",
        ),
        (
            "projected-manager",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                'const record = "npm";\n'
                "const { manager } = record;\n"
                f'spawn(manager, ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "projected-argv",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'const record = ["install", "{PACKAGE_NAME}"];\n'
                "const { argv } = record;\n"
                'spawn("npm", argv);\n'
            ),
            "error",
        ),
        (
            "eval-const-package",
            "javascript",
            f'const packageName = "{PACKAGE_NAME}"; eval(packageName);\n',
            "error",
        ),
        (
            "unknown-child-const-package",
            "javascript",
            (
                'import { fork } from "node:child_process";\n'
                f'const packageName = "{PACKAGE_NAME}";\n'
                "fork(packageName);\n"
            ),
            "error",
        ),
        (
            "new-function-const-package",
            "javascript",
            (
                f'const packageName = "{PACKAGE_NAME}";\n'
                "new Function(packageName);\n"
            ),
            "error",
        ),
        (
            "arrow-const-package",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'const packageName = "{PACKAGE_NAME}";\n'
                'const launch = () => spawn("npm", ["install", packageName]);\n'
            ),
            "error",
        ),
        (
            "mutable-sensitive-alias",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "let launch = spawn;\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "escaped-sensitive-alias",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "const launch = spawn;\n"
                "observe(launch);\n"
                f'launch("npm", ["install", "{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "compound-unknown-escape",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                f'const argv = ["install", "{PACKAGE_NAME}"];\n'
                "observe({ argv });\n"
                'spawn("npm", argv);\n'
            ),
            "error",
        ),
        (
            "sensitive-array-escape",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "const stored = [spawn];\n"
                f'const packageName = "{PACKAGE_NAME}";\n'
            ),
            "error",
        ),
        (
            "sensitive-object-escape",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "const stored = { spawn };\n"
                f'const packageName = "{PACKAGE_NAME}";\n'
            ),
            "error",
        ),
        (
            "sensitive-return-escape",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "function expose() { return spawn; }\n"
                f'const packageName = "{PACKAGE_NAME}";\n'
            ),
            "error",
        ),
        (
            "sensitive-assignment-escape",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "let stored;\n"
                "stored = spawn;\n"
                f'const packageName = "{PACKAGE_NAME}";\n'
            ),
            "error",
        ),
        (
            "unrelated-container",
            "javascript",
            (
                "const stored = [harmless];\n"
                f'const packageName = "{PACKAGE_NAME}";\n'
            ),
            "clean",
        ),
        (
            "sensitive-container-without-package",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                "const stored = [spawn];\n"
            ),
            "clean",
        ),
        (
            "lone-surrogate-package",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                r'spawn("npm", ["install", "\uD800 '
                f'{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "out-of-range-unicode-package",
            "javascript",
            (
                'import { spawn } from "node:child_process";\n'
                r'spawn("npm", ["install", "\u{110000} '
                f'{PACKAGE_NAME}"]);\n'
            ),
            "error",
        ),
        (
            "standard-escape-command",
            "javascript",
            (
                'import { exec } from "node:child_process";\n'
                f'exec("npm\\tinstall {PACKAGE_NAME}");\n'
            ),
            "consumer",
        ),
        _j("require-options", f'require("{_P}", {{}});\n', "consumer"),
        _j("import-options", f'import("{_P}", {{}});\n', "consumer"),
        _j(
            "resolve-options",
            f'require.resolve("{_P}", {{ paths: [] }});\n',
            "consumer",
        ),
        _j(
            "unresolved-loader",
            f'let target = "{_P}"; require(target, {{}});\n',
            "error",
        ),
        _j(
            "unrelated-loader",
            f'require("node:path", {{}}); const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "structured-spread",
            _SPAWN_IMPORT + f'spawn(...["npm", "install", "{_P}"]);\n',
            "error",
        ),
        _j(
            "structured-zero",
            _SPAWN_IMPORT + f'const metadata = "{_P}"; spawn();\n',
            "clean",
        ),
        _j("conditional-eval", f'(flag ? eval : harmless)("{_P}");\n', "error"),
        _j(
            "conditional-spawn",
            _SPAWN_IMPORT
            + '(flag ? spawn : harmless)("npm", '
            + f'["install", "{_P}"]);\n',
            "error",
        ),
        _j(
            "conditional-loader",
            f'(flag ? require : harmless)("{_P}");\n',
            "error",
        ),
        _j(
            "deep-alias",
            _SPAWN_IMPORT
            + 'const root = "npm"; const one = root; const two = one;\n'
            + f'spawn(two, ["install", "{_P}"]);\n',
            "error",
        ),
        _j(
            "cyclic-alias",
            _SPAWN_IMPORT
            + "const one = two; const two = one;\n"
            + f'spawn(one, ["install", "{_P}"]);\n',
            "error",
        ),
        _j(
            "harmless-deep-alias",
            "const root = harmless; const one = root; const two = one;\n"
            f'two("docs"); const metadata = "{_P}";\n',
            "clean",
        ),
        _j("module-require", f'module.require("{_P}", {{}});\n', "consumer"),
        _j(
            "module-namespace-require",
            'import * as Module from "node:module";\n'
            f'Module.require("{_P}");\n',
            "clean",
        ),
        _j(
            "module-namespace-aggregate",
            'import * as Module from "node:module";\n'
            f'const stored = [Module]; const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "commonjs-module-create-require",
            f'const load = module.createRequire(__filename); load("{_P}");\n',
            "clean",
        ),
        _j(
            "module-aggregate",
            f'const stored = [module]; const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "module-mutable",
            f'let stored = module; const metadata = "{_P}";\n',
            "error",
        ),
        _j("module-package-free", "const stored = [module];\n", "clean"),
        _j(
            "object-non-pnpm",
            f'const stored = [Object]; const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "esm-module-unrelated",
            'import "node:path"; module.require("node:url");\n'
            f'const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "import-meta-loader",
            f'require(import.meta.url); const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-parenthesized-loader",
            f'require((import.meta).url); const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-process",
            "process.getBuiltinModule(import.meta.url);\n"
            f'const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-parenthesized-process",
            "process.getBuiltinModule((import.meta).url);\n"
            f'const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-dynamic",
            f'eval(import.meta.url); const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-computed-dynamic",
            f'eval(import.meta["url"]); const metadata = "{_P}";\n',
            "error",
        ),
        (
            "import-meta-typescript-process",
            "typescript",
            "process.getBuiltinModule(import.meta.url as string);\n"
            f'const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-computed-factory",
            'import { createRequire } from "node:module";\n'
            'createRequire(import.meta["url"]);\n'
            f'const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-commented-factory",
            'import { createRequire } from "node:module";\n'
            "const load = createRequire(import.meta /* comment */ .url);\n"
            f'const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "valid-surrogate-pair",
            _SPAWN_IMPORT
            + r'spawn("npm", ["install", "\uD83D\uDE00", '
            + f'"{_P}"]);\n',
            "consumer",
        ),
        _j(
            "reversed-surrogate-pair",
            _SPAWN_IMPORT
            + r'spawn("npm", ["install", "\uDE00\uD83D", '
            + f'"{_P}"]);\n',
            "error",
        ),
        _j(
            "arrow-non-manager",
            _SPAWN_IMPORT + f'const run = () => spawn("echo", ["{_P}"]);\n',
            "clean",
        ),
        _j(
            "async-options",
            _SPAWN_IMPORT
            + "async function run() { "
            + f'spawn("echo", {{ env: {{ P: "{_P}" }} }}); }}\n',
            "clean",
        ),
        _j(
            "nested-syntactic-package",
            f'const p = "{_P}"; '
            'eval({ args: [...(flag ? [p] : ["safe"])], text: `${p}` });\n',
            "error",
        ),
        _j(
            "logical-assignment-package",
            f'const p = "{_P}"; eval(slot = (flag && p));\n',
            "error",
        ),
        _j(
            "exact-unrelated-tree",
            'eval({ args: [...(flag ? ["left"] : ["right"])] }); '
            f'const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "compound-sensitive-binding",
            _SPAWN_IMPORT
            + "const sink = flag ? spawn : harmless; "
            + f'sink("npm", ["install", "{_P}"]);\n',
            "error",
        ),
        _j(
            "compound-harmless-binding",
            f'const sink = flag ? first : second; sink("{_P}");\n',
            "clean",
        ),
        _j(
            "compound-call",
            f'(flag ? eval : harmless).call(null, "{_P}");\n',
            "error",
        ),
        _j(
            "compound-apply",
            f'(flag ? eval : harmless).apply(null, ["{_P}"]);\n',
            "error",
        ),
        _j(
            "compound-bind",
            f'(flag ? eval : harmless).bind(null, "{_P}");\n',
            "error",
        ),
        _j(
            "compound-member-harmless",
            f'(flag ? first : second).call(null, "{_P}");\n',
            "clean",
        ),
        _j(
            "import-meta-resolve", f'import.meta.resolve("{_P}");\n', "consumer"
        ),
        _j(
            "import-meta-resolve-comment",
            f'import.meta /* comment */ .resolve("{_P}");\n',
            "consumer",
        ),
        _j(
            "import-meta-resolve-parenthesized",
            f'(import.meta).resolve("{_P}");\n',
            "consumer",
        ),
        _j(
            "import-meta-resolve-computed",
            f'import.meta["resolve"]("{_P}");\n',
            "consumer",
        ),
        _j(
            "import-meta-resolve-mutable-projection",
            f'let method = "resolve"; import.meta[method]("{_P}");\n',
            "error",
        ),
        _j(
            "import-meta-alias-resolve",
            f'const meta = import.meta; meta.resolve("{_P}");\n',
            "consumer",
        ),
        _j(
            "import-meta-alias-dynamic-projection",
            'const meta = import.meta; let method = "resolve"; '
            f'meta[method]("{_P}");\n',
            "error",
        ),
        _j(
            "import-meta-alias-aggregate",
            "const meta = import.meta; const stored = [meta]; "
            f'const metadata = "{_P}";\n',
            "error",
        ),
        _j(
            "import-meta-alias-url",
            "const meta = import.meta; const location = meta.url; "
            f'const metadata = "{_P}";\n',
            "clean",
        ),
        _j(
            "import-meta-resolve-const",
            f'const p = "{_P}"; import.meta.resolve(p);\n',
            "consumer",
        ),
        _j(
            "import-meta-resolve-mutable",
            f'let p = "{_P}"; import.meta.resolve(p);\n',
            "error",
        ),
        _j(
            "import-meta-resolve-unrelated",
            'import.meta.resolve("node:path");\n',
            "clean",
        ),
        _j(
            "harmless-long-alias",
            f'const a = console.log; const b = a; const c = b; c("{_P}");\n',
            "clean",
        ),
        _j(
            "sensitive-alias-chain",
            f'const a = eval; const b = a; b("{_P}");\n',
            "error",
        ),
        _j(
            "builtin-package-spelling",
            f'process.getBuiltinModule("{_P}");\n',
            "clean",
        ),
        _j(
            "builtin-direct-child-process-chain",
            'process.getBuiltinModule("node:child_process")'
            f'.spawn("npm", ["install", "{_P}"]);\n',
            "consumer",
        ),
        _j(
            "builtin-unresolved-package",
            f'let name = "{_P}"; process.getBuiltinModule(name);\n',
            "error",
        ),
        _j(
            "known-object-reflect-properties",
            f'Object.keys("{_P}"); Reflect.get({{}}, "{_P}");\n',
            "clean",
        ),
        _j(
            "known-writer-outside-pnpm",
            f'Object.assign({{}}, {{ value: "{_P}" }});\n',
            "clean",
        ),
        _j(
            "unknown-object-projection",
            f'let method = "keys"; Object[method]("{_P}");\n',
            "error",
        ),
    ],
)
def test_rc033_compact_static_regressions(
    case: str,
    language: javascript_consumer.JavaScriptLanguage,
    source: str,
    outcome: str,
) -> None:
    """Cover compact syntax admission and explicit fail-closed barriers."""
    assert case
    _assert_javascript_outcome(source, outcome, language=language)


@pytest.mark.parametrize("field", NODE_DEPENDENCY_FIELDS)
def test_rc033_direct_pnpm_writes_are_consumers(field: str) -> None:
    """Recognize direct writes to every canonical dependency field."""
    assignment = f'pkg.{field}["{PACKAGE_NAME}"] = "1";\n'
    computed = f'const key = "{PACKAGE_NAME}"; pkg.{field}[key] = "1";\n'
    deletion = f'delete pkg.{field}["{PACKAGE_NAME}"];\n'
    update = f'pkg.{field}["{PACKAGE_NAME}"]++;\n'
    mutation = f'Object.assign(pkg.{field}, {{"{PACKAGE_NAME}": "1"}});\n'
    assert all(
        _scan_javascript(source, pnpmfile=True)
        for source in (assignment, computed, deletion, update, mutation)
    )


@pytest.mark.parametrize(
    "source",
    [
        (
            f'Object.defineProperty(pkg.devDependencies, "{PACKAGE_NAME}", '
            '{ value: "1" });\n'
        ),
        (
            "Object.defineProperties(pkg.optionalDependencies, "
            f'{{"{PACKAGE_NAME}": {{ value: "1" }} }});\n'
        ),
        f'Reflect.set(pkg.peerDependencies, "{PACKAGE_NAME}", "1");\n',
        (
            f'Reflect.defineProperty(pkg.dependencies, "{PACKAGE_NAME}", '
            '{ value: "1" });\n'
        ),
        (f'Reflect.deleteProperty(pkg.dependencies, "{PACKAGE_NAME}");\n'),
        (
            f'const key = "{PACKAGE_NAME}"; '
            'Object.assign(pkg.dependencies, { [key]: "1" });\n'
        ),
    ],
)
def test_rc033_direct_pnpm_known_writers_are_consumers(source: str) -> None:
    """Recognize each direct known dependency-map writer."""
    assert _scan_javascript(source, pnpmfile=True)


@pytest.mark.parametrize(
    ("source", "outcome"),
    [
        (
            f'Object.assign(pkg.metadata, {{"{PACKAGE_NAME}": "docs"}});\n',
            "clean",
        ),
        (
            f'pkg.metadata["{PACKAGE_NAME}"] = "docs";\n',
            "clean",
        ),
        (
            "const write = Object.assign;\n"
            f'write(pkg.dependencies, {{"{PACKAGE_NAME}": "1"}});\n',
            "error",
        ),
        (
            f'Object.assign(pkg[getField()], {{"{PACKAGE_NAME}": "1"}});\n',
            "error",
        ),
        (
            f'let key = "{PACKAGE_NAME}"; pkg.dependencies[key] = "1";\n',
            "error",
        ),
        (
            "const source = "
            f'{{"{PACKAGE_NAME}": "1"}}; '
            "Object.assign(pkg.dependencies, { ...source });\n",
            "error",
        ),
        (
            f'let key = "{PACKAGE_NAME}"; delete pkg.dependencies[key];\n',
            "error",
        ),
        (
            f'delete pkg.metadata["{PACKAGE_NAME}"];\n',
            "clean",
        ),
        (
            f'const stored = [Object]; const metadata = "{PACKAGE_NAME}";\n',
            "error",
        ),
        (
            f'let stored = Reflect; const metadata = "{PACKAGE_NAME}";\n',
            "error",
        ),
        (
            "let key = "
            f'"{PACKAGE_NAME}"; '
            'Object.assign(pkg.metadata, { [key]: "docs" });\n',
            "clean",
        ),
        (
            "Object.assign(...[pkg.dependencies, "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            "const args = [pkg.dependencies, "
            f'{{"{PACKAGE_NAME}": "1"}}]; Object.assign(...args);\n',
            "error",
        ),
        (
            "const target = pkg.dependencies; "
            "Object.assign(...[target, "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            "const target = pkg.metadata; "
            "Object.assign(...[target, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "clean",
        ),
        (
            "const root = pkg; Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "clean",
        ),
        (
            "pkg = getTarget(); Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "pkg++; Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "delete pkg.metadata; Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "pkg.observe(); Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "const root = pkg; pkg = getTarget(); "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "observe(pkg); Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "observe({ pkg }); Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "const root = pkg; observe(pkg); "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "const holder = { target: pkg.dependencies }; "
            "Object.assign(...[holder.target, "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            "const holder = [pkg.dependencies]; "
            "Object.assign(...[holder[0], "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            "let root = pkg; Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "let root; Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            'import root from "docs"; Object.assign(...[root.metadata, '
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "function root() {} Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "class root {} Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "try { throw 0; } catch (root) { "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "for (const root of []) { Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root = pkg) { "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(...root) { Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook({ root }) { Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { root = pkg; "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { observe(root); "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { observe({ root }); "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { const target = root; observe(root); "
            "Object.assign(...[target.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { "
            "for (root.metadata of values) {} "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { "
            'for (root["metadata"] in values) {} '
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { "
            "for ({ value: root.metadata } of values) {} "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "error",
        ),
        (
            "function hook(root) { Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "clean",
        ),
        (
            "function hook(root) { const target = root; "
            "Object.assign(...[target.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "clean",
        ),
        (
            "function hook(root) { for (const item of values) {} "
            "Object.assign(...[root.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]); }}\n',
            "clean",
        ),
        (
            "Object.assign(...[getTarget().metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "error",
        ),
        (
            "let target = pkg.dependencies; "
            "Object.assign(...[target, "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            "let target = pkg.metadata; target = pkg.dependencies; "
            "Object.assign(...[target, "
            f'{{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            f'Object.assign(...makeArgs("{PACKAGE_NAME}"));\n',
            "error",
        ),
        (
            "Object.assign(...[pkg.metadata, "
            f'{{"{PACKAGE_NAME}": "docs"}}]);\n',
            "clean",
        ),
        (
            "Object.assign(pkg.metadata, "
            f'...[pkg.dependencies, {{"{PACKAGE_NAME}": "1"}}]);\n',
            "error",
        ),
        (
            f'Object.assign(pkg["{PACKAGE_NAME}"].dependencies, {{}});\n',
            "error",
        ),
        (
            "({ value: "
            f'pkg.dependencies["{PACKAGE_NAME}"] }} = {{ value: "1" }});\n',
            "error",
        ),
        (
            f'pkg.dependencies["{PACKAGE_NAME}"].version = "1";\n',
            "error",
        ),
        (
            f'pkg.metadata["{PACKAGE_NAME}"].version = "docs";\n',
            "clean",
        ),
    ],
)
def test_rc033_pnpm_writer_boundaries(source: str, outcome: str) -> None:
    """Keep metadata clean and fail closed for aliased or complex writers."""
    _assert_javascript_outcome(source, outcome, pnpmfile=True)


def test_rc033_parser_profile_and_policy_digest_are_exact() -> None:
    """Bind the compact supported subset and deterministic parser limits."""
    profile = consumer_policy_parser_profile()

    assert JAVASCRIPT_ANALYSIS_SEMANTICS_ID == (
        "rc-033-tree-sitter-static-consumer-subset-v1"
    )
    assert JAVASCRIPT_UNKNOWN_ADMISSION_POLICY == (
        "relevant-unknown-fail-closed-v1"
    )
    assert profile == {
        "engine": "tree-sitter",
        "analysis": {
            "semantics-id": JAVASCRIPT_ANALYSIS_SEMANTICS_ID,
            "unknown-admission-policy": (JAVASCRIPT_UNKNOWN_ADMISSION_POLICY),
            "relevant-unknown-admission-rule": (
                JAVASCRIPT_RELEVANT_UNKNOWN_ADMISSION_RULE
            ),
            "supported-constructs": list(JAVASCRIPT_SUPPORTED_CONSTRUCTS),
        },
        "runtime": {
            "distribution": "tree-sitter",
            "version": TREE_SITTER_VERSION,
        },
        "grammars": [
            {
                "language": "javascript",
                "distribution": "tree-sitter-javascript",
                "version": TREE_SITTER_JAVASCRIPT_VERSION,
            },
            {
                "language": "typescript",
                "distribution": "tree-sitter-typescript",
                "version": TREE_SITTER_TYPESCRIPT_VERSION,
            },
        ],
        "limits": {
            "source-bytes": JAVASCRIPT_SOURCE_BYTE_LIMIT,
            "ast-nodes": JAVASCRIPT_AST_NODE_LIMIT,
            "ast-depth": JAVASCRIPT_AST_DEPTH_LIMIT,
        },
        "commonjs-global-suffixes": list(JAVASCRIPT_COMMONJS_GLOBAL_SUFFIXES),
    }
    assert consumer_policy_document()["parser"] == profile
    assert consumer_policy_document()["node-dependency-fields"] == list(
        NODE_DEPENDENCY_FIELDS
    )
    assert CONSUMER_POLICY_DIGEST == (
        "sha256:1cde1072759e720f7753923338749b6e"
        "616820b376b9b5d705eb475f3a5afc08"
    )


def test_rc033_source_limit_has_an_exact_boundary() -> None:
    """Accept the source-byte limit and reject the next byte."""
    assert not _scan_javascript(b" " * JAVASCRIPT_SOURCE_BYTE_LIMIT)
    with pytest.raises(
        ValueError,
        match=(
            rf"JavaScript source exceeds {JAVASCRIPT_SOURCE_BYTE_LIMIT} bytes"
        ),
    ):
        _scan_javascript(b" " * (JAVASCRIPT_SOURCE_BYTE_LIMIT + 1))


def test_rc033_ast_node_and_depth_limits_have_exact_boundaries(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Apply node and depth limits to the complete parse tree."""
    source = b"const value = 'clean';"
    _, node_count = javascript_consumer._parse(  # noqa: SLF001
        source,
        "javascript",
    )
    monkeypatch.setattr(
        javascript_consumer,
        "JAVASCRIPT_AST_NODE_LIMIT",
        node_count,
    )
    assert not _scan_javascript(source)
    monkeypatch.setattr(
        javascript_consumer,
        "JAVASCRIPT_AST_NODE_LIMIT",
        node_count - 1,
    )
    with pytest.raises(
        ValueError,
        match=rf"JavaScript AST node count exceeds {node_count - 1}",
    ):
        _scan_javascript(source)

    monkeypatch.setattr(
        javascript_consumer,
        "JAVASCRIPT_AST_NODE_LIMIT",
        JAVASCRIPT_AST_NODE_LIMIT,
    )
    monkeypatch.setattr(javascript_consumer, "JAVASCRIPT_AST_DEPTH_LIMIT", 1)
    assert not _scan_javascript("")
    with pytest.raises(
        ValueError,
        match="JavaScript AST depth exceeds 1",
    ):
        _scan_javascript(";")


def test_rc033_shared_static_argv_array_is_decoded_once(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Memoize expansion of a shared successful static array."""
    expansions = 0
    original = (
        javascript_consumer._Scanner._array_literal_uncached  # noqa: SLF001
    )

    def counted(scanner: Any, node: Any) -> tuple[str, ...] | None:
        nonlocal expansions
        if node.type == "array":
            expansions += 1
        return original(scanner, node)

    monkeypatch.setattr(
        javascript_consumer._Scanner,  # noqa: SLF001
        "_array_literal_uncached",
        counted,
    )
    fill = ", ".join('"x"' for _ in range(64))
    source = (
        'import { spawn } from "node:child_process";\n'
        f'const argv = ["install", "{PACKAGE_NAME}", {fill}];\n'
        + 'spawn("echo", argv);\n'
        * 32
    )

    assert not _scan_javascript(source)
    assert expansions == 1


def test_rc033_shared_pnpm_spread_analysis_is_memoized(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Memoize failed array decoding and shared syntactic relevance."""
    arrays = 0
    package_expansions: dict[int, int] = {}
    dependency_expansions: dict[int, int] = {}
    scanner_type = javascript_consumer._Scanner  # noqa: SLF001
    original_array = scanner_type._array_literal_uncached  # noqa: SLF001
    original_package = scanner_type._syntax_package_uncached  # noqa: SLF001
    original_dependency = (
        scanner_type._pnpm_dependency_syntax_uncached  # noqa: SLF001
    )

    def counted_array(scanner: Any, node: Any) -> tuple[str, ...] | None:
        nonlocal arrays
        if node.type == "array":
            arrays += 1
        return original_array(scanner, node)

    def counted_package(scanner: Any, node: Any) -> int:
        key = scanner._constant_nodes(node)[-1].id  # noqa: SLF001
        package_expansions[key] = package_expansions.get(key, 0) + 1
        return original_package(scanner, node)

    def counted_dependency(scanner: Any, node: Any) -> bool:
        key = scanner._constant_nodes(node)[-1].id  # noqa: SLF001
        dependency_expansions[key] = dependency_expansions.get(key, 0) + 1
        return original_dependency(scanner, node)

    monkeypatch.setattr(scanner_type, "_array_literal_uncached", counted_array)
    monkeypatch.setattr(
        scanner_type, "_syntax_package_uncached", counted_package
    )
    monkeypatch.setattr(
        scanner_type,
        "_pnpm_dependency_syntax_uncached",
        counted_dependency,
    )
    fill = ", ".join('"x"' for _ in range(64))
    source = (
        "const args = [pkg.metadata, "
        f'{{"{PACKAGE_NAME}": "docs"}}, {fill}];\n'
        + "Object.assign(...args);\n"
        * 32
    )

    assert not _scan_javascript(source, pnpmfile=True)
    assert arrays == 1
    assert package_expansions
    assert max(package_expansions.values()) == 1
    assert dependency_expansions
    assert max(dependency_expansions.values()) == 1


def test_rc033_path_routing_selects_typescript_only_for_ts(
    tmp_path: Path,
) -> None:
    """Route .ts through TypeScript and reject the same syntax in .js."""
    source = (
        'import { spawn } from "node:child_process";\n'
        'const manager: string = "npm";\n'
        f'spawn(manager, ["install", "{PACKAGE_NAME}"]);\n'
    )
    repository, _ = _repository(tmp_path)
    typescript_path = "tools/postinstall-routing.ts"
    _write(repository, typescript_path, source)
    _assert_consumer(repository, typescript_path)

    javascript_path = "tools/postinstall-routing.js"
    _write(repository, javascript_path, source)
    with pytest.raises(ConsumerPolicyScanError) as captured:
        scan_consumer_policy(repository)
    assert captured.value.findings == (
        (
            f"{javascript_path}: "
            "JavaScript parse tree contains errors or missing nodes"
        ),
    )


@pytest.mark.parametrize(
    ("path", "content", "consumer"),
    [
        (
            "tools/postinstall-esm.mjs",
            f'require("{PACKAGE_NAME}"); module.require("{PACKAGE_NAME}");\n',
            False,
        ),
        (
            "tools/postinstall-create-require.mjs",
            (
                'import { createRequire } from "node:module";\n'
                "const require = createRequire(import.meta.url);\n"
                f'require("{PACKAGE_NAME}");\n'
            ),
            True,
        ),
        (
            "tools/postinstall-commonjs.js",
            f'require("{PACKAGE_NAME}");\n',
            True,
        ),
        (
            "tools/postinstall-commonjs.cjs",
            f'module.require("{PACKAGE_NAME}");\n',
            True,
        ),
        (
            "tools/postinstall-commonjs.ts",
            f'require("{PACKAGE_NAME}");\n',
            True,
        ),
    ],
)
def test_rc033_path_routing_controls_commonjs_globals(
    tmp_path: Path,
    path: str,
    content: str,
    *,
    consumer: bool,
) -> None:
    """Enable implicit CommonJS globals only for the canonical suffixes."""
    repository, _ = _repository(tmp_path)
    _write(repository, path, content)

    result = scan_consumer_policy(repository)

    assert (path in result.consumers) is consumer


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


def test_oversized_base_matrix_fails_before_post_exclude_reduction(
    tmp_path: Path,
) -> None:
    """Never scan a truncated prefix of a 272-row base matrix."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/oversized-exclude.yml"
    managers = [f"echo-{index}" for index in range(16)]
    packages = [f"other-{index}" for index in range(15)] + [PACKAGE_NAME]
    _write(
        repository,
        path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "manager": [*managers, "npm"],
                                "package": packages,
                                "exclude": [
                                    {"manager": manager} for manager in managers
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
        ),
    )

    with pytest.raises(
        ConsumerPolicyScanError,
        match="workflow matrix base static expansion exceeds 256 states",
    ):
        scan_consumer_policy(repository)


def test_exact_matrix_state_boundary_scans_the_final_row(
    tmp_path: Path,
) -> None:
    """Scan all 256 static rows, including a consumer in the final row."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/exact-boundary.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "manager": [
                                    *(f"echo-{index}" for index in range(15)),
                                    "npm",
                                ],
                                "package": [
                                    *(f"other-{index}" for index in range(15)),
                                    PACKAGE_NAME,
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
        ),
    )

    _assert_consumer(repository, path)


def test_standalone_include_beyond_matrix_state_boundary_fails_closed(
    tmp_path: Path,
) -> None:
    """Reject a standalone include that would become the 257th row."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/include-overflow.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "manager": [
                                    f"echo-{index}" for index in range(16)
                                ],
                                "package": [
                                    f"other-{index}" for index in range(16)
                                ],
                                "include": [
                                    {
                                        "manager": "npm",
                                        "package": PACKAGE_NAME,
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
        ),
    )

    with pytest.raises(
        ConsumerPolicyScanError,
        match=(
            "workflow matrix static expansion after include exceeds 256 states"
        ),
    ):
        scan_consumer_policy(repository)


def test_include_overflow_does_not_process_later_entries() -> None:
    """Stop at state 257 without inspecting any later include entry."""

    class UnreachableInclude(dict[str, str]):
        def items(self):
            message = "entry after overflow was processed"
            raise AssertionError(message)

    matrix = {
        "include": [
            *({"sequence": str(index)} for index in range(257)),
            UnreachableInclude(sequence="unreachable"),
        ],
    }

    with pytest.raises(
        ValueError,
        match="workflow matrix static expansion after include exceeds 256",
    ):
        POLICY._matrix_rows(matrix)  # noqa: SLF001


def test_under_limit_exclude_keeps_surviving_consumer_visible(
    tmp_path: Path,
) -> None:
    """Apply a bounded exclude without hiding the surviving consumer row."""
    repository, _ = _repository(tmp_path)
    path = ".github/workflows/under-limit-exclude.yml"
    _write(
        repository,
        path,
        json.dumps(
            {
                "jobs": {
                    "consume": {
                        "strategy": {
                            "matrix": {
                                "manager": ["echo", "npm"],
                                "package": ["other-package", PACKAGE_NAME],
                                "exclude": [
                                    {
                                        "manager": "echo",
                                        "package": PACKAGE_NAME,
                                    },
                                    {
                                        "manager": "npm",
                                        "package": "other-package",
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
        ),
    )

    _assert_consumer(repository, path)


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
    path = ".github/actions/team/direct/action.yml"
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
        "regexp-exec",
        "unrelated-spawn-method",
        "unbound-exec",
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
def test_changed_exception_context_or_digest_reopens_as_consumer(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
    mutation: str,
) -> None:
    """Reopen a seen but nonmatching approved exception as a consumer."""
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
    return_code = main(["--repository-root", str(repository)])
    captured = capsys.readouterr()

    assert result.consumers == (path,)
    assert path not in {surface.path for surface in result.admitted_exceptions}
    assert return_code == 1
    assert captured.err == ""
    assert json.loads(captured.out)["consumers"] == [path]


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
        ("tools/setup-syntax.js", b"const = ;\n"),
        ("tools/setup-syntax.ts", b"const value: = 1;\n"),
        (".pnpmfile.cjs", b"module.exports = {\n"),
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
        "javascript-syntax",
        "typescript-syntax",
        "pnpmfile-javascript-syntax",
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
    assert set(POLICY.JAVASCRIPT_ANALYZER_PATHS) <= set(hk_globs)
    assert all(
        classify_dependency_surface(path) is not None
        for path in POLICY.JAVASCRIPT_ANALYZER_PATHS
    )


def _assert_token_branches_are_disjoint() -> None:
    """Keep quoted escapes disjoint and able to consume newlines."""
    assert POLICY._TOKEN.pattern == (  # noqa: SLF001
        r""""(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|[^\s]+"""
    )


def test_quoted_token_branches_are_disjoint() -> None:
    """Keep quoted escape and ordinary-character alternatives disjoint."""
    _assert_token_branches_are_disjoint()


@pytest.mark.parametrize(
    ("route", "quote"),
    [
        pytest.param(
            "command-argument",
            '"',
            id="command-argument-double",
        ),
        pytest.param(
            "command-argument",
            "'",
            id="command-argument-single",
        ),
        pytest.param("bun-lock", '"', id="bun-lock-double"),
        pytest.param("bun-lock", "'", id="bun-lock-single"),
    ],
)
def test_backslash_newline_continuation_keeps_quoted_package_hidden(
    route: str,
    quote: str,
) -> None:
    """Hide a continued quoted decoy while preserving a following consumer."""
    backslash_newline = "\\\n"
    quoted_decoy = (
        f"{quote}ordinary {backslash_newline}{PACKAGE_NAME} hidden{quote}"
    )
    payload = f"{quoted_decoy} {PACKAGE_NAME}"

    if route == "command-argument":
        assert not POLICY._manager_references(  # noqa: SLF001
            f"npm install {quoted_decoy}",
        )
        assert POLICY._manager_references(  # noqa: SLF001
            f"npm install {payload}",
        )
    else:
        assert (
            POLICY._lockfile(  # noqa: SLF001
                "bun.lock",
                quoted_decoy.encode(),
            )
            == set()
        )
        assert POLICY._lockfile(  # noqa: SLF001
            "bun.lock",
            payload.encode(),
        ) == {"lockfile-reference"}


def _token_route_references_package(route: str, payload: str) -> bool:
    """Evaluate a token payload through one consumer-policy route."""
    if route == "command-argument":
        return bool(
            POLICY._manager_references(  # noqa: SLF001
                f"npm install {payload}",
            ),
        )
    assert route == "bun-lock"
    return POLICY._lockfile(  # noqa: SLF001
        "bun.lock",
        payload.encode(),
    ) == {"lockfile-reference"}


@pytest.mark.parametrize("route", ["command-argument", "bun-lock"])
@pytest.mark.parametrize("quote", ['"', "'"], ids=["double", "single"])
def test_large_unterminated_escaped_quote_is_not_a_consumer(
    route: str,
    quote: str,
) -> None:
    """Ignore a large unterminated quoted package decoy."""
    _assert_token_branches_are_disjoint()
    escaped_ordinary = r"\a" * 10_000
    payload = f"{quote}{escaped_ordinary}{PACKAGE_NAME}{escaped_ordinary}"

    assert not _token_route_references_package(route, payload)


@pytest.mark.parametrize("route", ["command-argument", "bun-lock"])
@pytest.mark.parametrize("quote", ['"', "'"], ids=["double", "single"])
def test_escaped_quoted_decoy_preserves_following_package_token(
    route: str,
    quote: str,
) -> None:
    """Ignore an escaped quoted decoy and detect the following package."""
    escaped_quote = f"\\{quote}"
    quoted_decoy = (
        f"{quote}ordinary {escaped_quote} {PACKAGE_NAME} hidden{quote}"
    )

    assert not _token_route_references_package(route, quoted_decoy)
    assert _token_route_references_package(
        route,
        f"{quoted_decoy} {PACKAGE_NAME}",
    )
