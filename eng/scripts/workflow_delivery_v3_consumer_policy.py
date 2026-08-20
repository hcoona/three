"""Enforce the permanent first-slice npm consumer policy."""

# ruff: noqa: C901, E501, EM101, EM102, PLR0911, PLR0912, PLR0913, S105, S314, TRY003, TRY004, TRY301

from __future__ import annotations

import argparse
import ast
import hashlib
import json
import re
import stat
import subprocess
import sys
import tomllib
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING

import yaml
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256
from three_workflow_delivery_v3.release.eligibility import (
    CONSUMER_POLICY_ID,
    ConsumerPolicyResult,
    SurfaceDigest,
    validate_consumer_policy_result,
)

if TYPE_CHECKING:
    from collections.abc import Iterable, Mapping, Sequence

# This is intentionally a closed syntactic policy, not a shell interpreter.
# fmt: off

PACKAGE_NAME = "@hcoona/hcoona-release-smoke-npm"
POLICY_IMPLEMENTATION_PATH = "eng/scripts/workflow_delivery_v3_consumer_policy.py"
GIT_ATTRIBUTES_PATH = ".gitattributes"
OWN_DECLARATION_PATH = "src/public/lib/hcoona-release-smoke-npm/package.json"
ACCEPTANCE_FIXTURE_PATH = "src/public/lib/three-workflow-delivery-v3/tests/fixtures/release/consumer-policy-acceptance.json"
ACCEPTANCE_NPM_MANIFEST_PATH = "src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/package.json"

_PACKAGE = re.escape(PACKAGE_NAME)
_DIRECT_SPEC = re.compile(rf"{_PACKAGE}(?:@[^\s]+)?\Z")
_ALIAS_SPEC = re.compile(rf"(?:[A-Za-z0-9_.-]+@)?npm:{_PACKAGE}(?:@[^\s]+)?\Z")
_WORKSPACE_SPEC = re.compile(rf"workspace:{_PACKAGE}(?:@[^\s]+)?\Z")
_DEPENDENCY_SPEC = re.compile(rf"{_PACKAGE}(?:\[[^\]]+\])?(?:\s*(?:===|==|!=|~=|<=|>=|<|>|@)\s*.+)?(?:\s*;.*)?\Z")
_LOCK_SPEC = re.compile(rf"(?:[A-Za-z0-9_.-]+@)?npm:{_PACKAGE}(?:@[^\s]+)?\Z|workspace:{_PACKAGE}(?:@[^\s]+)?\Z|{_PACKAGE}(?:@[^\s]+)?\Z|/{_PACKAGE}(?:@[^\s]+)?\Z|(?:^|/)node_modules/{_PACKAGE}(?:/|\Z)")
_IMPORT = re.compile(rf"""(?mx)(?:^\s*(?:import|export)\s+(?:[^"'()]*?\s+from\s+)?["']{_PACKAGE}(?:/[^"'\s]+)?["']|(?:^|[=(:,]\s*)(?:await\s+)?(?:import|require)\s*\(\s*["']{_PACKAGE}(?:/[^"'\s]+)?["']\s*\)|(?:^|[=(:,]\s*)require\.resolve\s*\(\s*["']{_PACKAGE}(?:/[^"'\s]+)?["']\s*\))""")
_ACTION_USE = re.compile(rf"{_PACKAGE}(?:/[^@\s]+)?(?:@[^\s]+)?\Z")
_REUSABLE_WORKFLOW_USE = re.compile(r"(?:^\.\/|^[^/\s]+\/[^/\s]+\/)\.github/workflows/[^@\s]+\.ya?ml(?:@[^\s]+)?\Z")
_TOKEN = re.compile(r""""(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|[^\s]+""")
_BLOCK_COMMENT = re.compile(r"/\*.*?\*/|<#.*?#>", re.DOTALL)
_TEMPLATE_LITERAL = re.compile(r"`(?:\\.|[^`\\])*`", re.DOTALL)
_PNPMFILE_DEPENDENCY = re.compile(rf"""(?ix)\b(?:dependencies|devDependencies|optionalDependencies|peerDependencies)\s*\[\s*["']{_PACKAGE}["']\s*\]""")
_JS_LITERAL = re.compile(r"""(["'])([^"'\\]*(?:\\.[^"'\\]*)*)\1""")
_START_PROCESS = re.compile(r"(?im)^\s*(?:&\s*)?Start-Process\b(?P<body>[^\r\n#]*)")
_JS_IDENTIFIER = r"[A-Za-z_$][A-Za-z0-9_$]*"
_CHILD_PROCESS_MODULE = r"(?:node:)?child_process"
_NODE_APIS = frozenset(("exec", "execFile", "execFileSync", "execSync", "spawn", "spawnSync"))
_NODE_NAMED_IMPORT = re.compile(rf"""(?sx)\bimport\s*\{{(?P<bindings>[^}}]*)\}}\s*from\s*["']{_CHILD_PROCESS_MODULE}["']""")
_NODE_NAMESPACE_IMPORT = re.compile(rf"""(?sx)\bimport\s+(?:\*\s+as\s+)?(?P<name>{_JS_IDENTIFIER})\s+from\s*["']{_CHILD_PROCESS_MODULE}["']""")
_NODE_NAMED_REQUIRE = re.compile(rf"""(?sx)\b(?:const|let|var)\s*\{{(?P<bindings>[^}}]*)\}}\s*=\s*require\(\s*["']{_CHILD_PROCESS_MODULE}["']\s*\)""")
_NODE_NAMESPACE_REQUIRE = re.compile(rf"""(?mx)\b(?:const|let|var)\s+(?P<name>{_JS_IDENTIFIER})\s*=\s*require\(\s*["']{_CHILD_PROCESS_MODULE}["']\s*\)\s*;?\s*$""")
_NODE_MEMBER_REQUIRE = re.compile(rf"""(?sx)\b(?:const|let|var)\s+(?P<name>{_JS_IDENTIFIER})\s*=\s*require\(\s*["']{_CHILD_PROCESS_MODULE}["']\s*\)\s*\.\s*(?P<api>{'|'.join(sorted(_NODE_APIS))})\b""")
_JS_COMMAND_LITERAL = r"""(?:"(?P<double>(?:\\.|[^"\\])*)"|'(?P<single>(?:\\.|[^'\\])*)'|`(?P<template>(?:\\.|[^`\\])*)`)"""
_MATRIX_REFERENCE = re.compile(r"""\${{\s*matrix(?:\.([A-Za-z_][A-Za-z0-9_-]*)|\[\s*["']([A-Za-z_][A-Za-z0-9_-]*)["']\s*\])\s*}}""")
_INPUT_REFERENCE = re.compile(r"""\${{\s*inputs(?:\.([A-Za-z_][A-Za-z0-9_-]*)|\[\s*["']([A-Za-z_][A-Za-z0-9_-]*)["']\s*\])\s*}}""")
_ENV_REFERENCE = re.compile(r"""\${{\s*env(?:\.([A-Za-z_][A-Za-z0-9_-]*)|\[\s*["']([A-Za-z_][A-Za-z0-9_-]*)["']\s*\])\s*}}""")
_DEPENDENCY_FIELDS = ("dependencies", "devDependencies", "optionalDependencies", "peerDependencies")
_MAX_WORKFLOW_STATES = 256
_MAX_WORKFLOW_PASSES = 16
_MAX_LOCAL_ACTION_DEPTH = 16


def _root_and_recursive(*patterns: str) -> tuple[str, ...]:
    return (*patterns, *(f"**/{pattern}" for pattern in patterns))


def _script_patterns() -> tuple[str, ...]:
    suffixes = ("bat", "bash", "cjs", "cmd", "js", "mjs", "ps1", "py", "sh", "ts", "zsh")
    names = [f"{prefix}*.{suffix}" for prefix in ("bootstrap", "install", "setup") for suffix in suffixes]
    names.extend(f"postinstall*.{suffix}" for suffix in ("cjs", "js", "mjs", "ts"))
    return _root_and_recursive(*names)


@dataclass(frozen=True, slots=True)
class DependencySurfaceRule:
    """One closed dependency-consumer path and syntax family."""

    category: str
    path_patterns: tuple[str, ...]
    syntax_contexts: tuple[str, ...]

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical policy representation."""
        path_patterns: list[JsonValue] = list(self.path_patterns)
        syntax_contexts: list[JsonValue] = list(self.syntax_contexts)
        return {"category": self.category, "path-patterns": path_patterns, "syntax-contexts": syntax_contexts}


@dataclass(frozen=True, slots=True)
class ApprovedConsumerException:
    """One reviewed path, syntax context, and byte-digest exception."""

    path: str
    category: str
    context: str
    content_digest: str

    def to_document(self) -> dict[str, JsonValue]:
        """Return the canonical policy representation."""
        return {"path": self.path, "category": self.category, "context": self.context, "content-digest": self.content_digest}


class ConsumerPolicyScanError(ValueError):
    """A deterministic set of catalog scan failures."""

    def __init__(self, findings: Iterable[str]) -> None:
        """Initialize the sorted complete scan-error findings."""
        self.findings = tuple(sorted(set(findings)))
        super().__init__("; ".join(self.findings))


DEPENDENCY_SURFACE_CATALOG = (
    DependencySurfaceRule("dependency-manifest", (*_root_and_recursive("Directory.Packages.props", "package.json", "packages.config", "pyproject.toml", "requirements*.txt", "setup.py", "*.csproj", "*.fsproj", "*.vbproj"), ACCEPTANCE_FIXTURE_PATH), ("node-dependency", "python-dependency", "dotnet-package-reference")),
    DependencySurfaceRule("lockfile", _root_and_recursive("bun.lock", "npm-shrinkwrap.json", "package-lock.json", "packages.lock.json", "pnpm-lock.yaml", "poetry.lock", "uv.lock", "yarn.lock"), ("dependency-key", "package-key", "node_modules-path")),
    DependencySurfaceRule("workflow", _root_and_recursive(".github/workflows/*.yaml", ".github/workflows/*.yml"), ("uses", "run", "with", "env")),
    DependencySurfaceRule("composite-action", (".github/actions/**/action.yaml", ".github/actions/**/action.yml"), ("uses", "run", "with", "env")),
    DependencySurfaceRule("install-bootstrap-script", (*_script_patterns(), POLICY_IMPLEMENTATION_PATH), ("package-manager-command", "module-import")),
    DependencySurfaceRule("dependency-configuration", (*_root_and_recursive(".github/dependabot.yaml", ".github/dependabot.yml", ".npmrc", ".pnpmfile.cjs", ".yarnrc", ".yarnrc.yml", "NuGet.config", "bunfig.toml", "nuget.config", "pnpm-workspace.yaml", "renovate.json"), GIT_ATTRIBUTES_PATH), ("dependency-selection", "package-manager-command")),
)
APPROVED_CONSUMER_EXCEPTIONS = (
    ApprovedConsumerException(OWN_DECLARATION_PATH, "dependency-manifest", "name", "sha256:a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566"),
    ApprovedConsumerException(ACCEPTANCE_FIXTURE_PATH, "dependency-manifest", f"dependencies.{PACKAGE_NAME}", "sha256:a28d7f1e161df6948cdc2f122e78b9a38f425b481877178e29c8cd8ef30b0aa2"),
    ApprovedConsumerException(ACCEPTANCE_NPM_MANIFEST_PATH, "dependency-manifest", "name", "sha256:d032b543a77820f9660a629e7deee6140664150a2c0a7de8048d37947afc957e"),
)
CONSUMER_POLICY_HK_GLOBS = tuple(sorted(
    {pattern for rule in DEPENDENCY_SURFACE_CATALOG for pattern in rule.path_patterns}
    | {
        "hk.pkl",
        "src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py",
        "src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py",
    },
))


def _matches(path: str, pattern: str) -> bool:
    candidate = PurePosixPath(path)
    if pattern.startswith("**/"):
        return candidate.match(pattern) or candidate.match(pattern[3:])
    return len(candidate.parts) == len(PurePosixPath(pattern).parts) and candidate.match(pattern)


def classify_dependency_surface(path: str) -> DependencySurfaceRule | None:
    """Classify one canonical repository-relative path."""
    candidate = PurePosixPath(path)
    if not path or candidate.is_absolute() or candidate.as_posix() != path or "\\" in path or any(part in {"", ".", ".."} for part in candidate.parts):
        raise ValueError(f"non-canonical repository path: {path!r}")
    return next((rule for rule in DEPENDENCY_SURFACE_CATALOG if any(_matches(path, pattern) for pattern in rule.path_patterns)), None)


def _sha256(content: bytes) -> str:
    return f"sha256:{hashlib.sha256(content).hexdigest()}"


def _is_package(value: object) -> bool:
    return isinstance(value, str) and any(pattern.fullmatch(value.strip()) is not None for pattern in (_DIRECT_SPEC, _ALIAS_SPEC, _WORKSPACE_SPEC))


def _is_dependency(value: object) -> bool:
    return isinstance(value, str) and any(pattern.fullmatch(value.strip()) is not None for pattern in (_DEPENDENCY_SPEC, _ALIAS_SPEC, _WORKSPACE_SPEC))


def _tree_has_package(value: object) -> bool:
    if isinstance(value, dict):
        return any(key == PACKAGE_NAME or _is_package(key) or _tree_has_package(item) for key, item in value.items())
    if isinstance(value, list):
        return any(_tree_has_package(item) for item in value)
    return _is_package(value) or _is_dependency(value)


def _node_manifest(document: object) -> set[str]:
    if not isinstance(document, dict):
        raise ValueError("Node manifest must be a JSON object")
    contexts = {"name"} if document.get("name") == PACKAGE_NAME else set()
    for field in _DEPENDENCY_FIELDS:
        dependencies = document.get(field)
        if not isinstance(dependencies, dict):
            continue
        if PACKAGE_NAME in dependencies:
            contexts.add(f"{field}.{PACKAGE_NAME}")
        if any(_is_package(value) for value in dependencies.values()):
            contexts.add(f"{field}.npm-alias")
    for field in ("bundleDependencies", "bundledDependencies", "overrides", "resolutions"):
        if _tree_has_package(document.get(field)):
            contexts.add(field)
    pnpm = document.get("pnpm")
    if isinstance(pnpm, dict) and _tree_has_package(pnpm.get("overrides")):
        contexts.add("pnpm.overrides")
    scripts = document.get("scripts")
    if isinstance(scripts, dict):
        contexts.update(f"scripts.{name}" for name, command in scripts.items() if isinstance(command, str) and _script_references(command))
    return contexts


def _python_manifest(path: str, text: str) -> set[str]:
    name = PurePosixPath(path).name
    if name == "setup.py":
        tree = ast.parse(text)
        setup_values = (keyword.value for node in ast.walk(tree) if isinstance(node, ast.Call) for keyword in node.keywords if keyword.arg in {"extras_require", "install_requires"})
        found = any(isinstance(item, ast.Constant) and _is_dependency(item.value) for value in setup_values for item in ast.walk(value)) or _python_script_references(text)
    elif name.startswith("requirements"):
        found = any(_is_dependency(line.split("#", 1)[0].strip()) for line in text.splitlines())
    else:
        document = tomllib.loads(text)
        project = document.get("project")
        selected: list[object] = [document.get("dependency-groups")]
        if isinstance(project, dict):
            selected.extend(project.get(field) for field in ("dependencies", "optional-dependencies"))
        tool = document.get("tool")
        if isinstance(tool, dict):
            selected.extend(tool.get(field) for field in ("pdm", "poetry", "uv"))
        found = any(_tree_has_package(value) for value in selected)
    return {"python-dependency"} if found else set()


def _dotnet_manifest(text: str) -> set[str]:
    root = ET.fromstring(text)
    names = {"PackageDownload", "PackageReference", "PackageVersion", "package"}
    found = any(
        element.tag.rsplit("}", 1)[-1] in names
        and any(element.attrib.get(attribute) == PACKAGE_NAME for attribute in ("Include", "Update", "id"))
        for element in root.iter()
    )
    found = found or any(element.tag.rsplit("}", 1)[-1] == "Exec" and isinstance(element.attrib.get("Command"), str) and _script_references(element.attrib["Command"]) for element in root.iter())
    return {"dotnet-package-reference"} if found else set()


def _manifest(path: str, content: bytes) -> set[str]:
    text = content.decode("utf-8", "strict")
    name = PurePosixPath(path).name
    if name == "package.json" or path == ACCEPTANCE_FIXTURE_PATH:
        return _node_manifest(json.loads(text))
    if name in {"pyproject.toml", "setup.py"} or name.startswith("requirements"):
        return _python_manifest(path, text)
    return _dotnet_manifest(text)


def _lock_token(value: object) -> bool:
    return isinstance(value, str) and _LOCK_SPEC.search(value.strip().strip("\"'")) is not None


def _lock_tree(value: object) -> bool:
    if isinstance(value, dict):
        return any(_lock_token(key) or _lock_tree(item) for key, item in value.items())
    if isinstance(value, list):
        return any(_lock_tree(item) for item in value)
    return _lock_token(value)


def _yarn_lock(text: str) -> bool:
    found = False
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or line[:1].isspace():
            continue
        if not stripped.endswith(":"):
            raise ValueError("malformed yarn.lock selector")
        found = found or any(_lock_token(selector.strip()) for selector in stripped[:-1].split(","))
    return found


def _lockfile(path: str, content: bytes) -> set[str]:
    text = content.decode("utf-8", "strict")
    name = PurePosixPath(path).name
    if name == "yarn.lock":
        found = _yarn_lock(text)
    elif name == "bun.lock":
        found = any(_lock_token(token.strip("\"',:[]{}")) for token in _TOKEN.findall(_code_text(text)))
    elif name in {"npm-shrinkwrap.json", "package-lock.json", "packages.lock.json"}:
        found = _lock_tree(json.loads(text))
    elif name in {"poetry.lock", "uv.lock"}:
        found = _lock_tree(tomllib.loads(text))
    else:
        found = _lock_tree(yaml.safe_load(text))
    return {"lockfile-reference"} if found else set()


_PREFIX = r"""(?:^[\t ]*|[;&|]\s*["']?|(?:call\s+["']?|cmd(?:\.exe)?(?:\s+/[A-Za-z:]+)*\s+/[ck]\s+["']?|(?:powershell|pwsh)(?:\.exe)?(?:\s+-[A-Za-z]+\s+\S+)*\s+-(?:c|command)\s+["']?|start(?:\s+"[^"]*")?(?:\s+/[A-Za-z]+)*\s+))"""
_EXECUTABLE = r"""(?:[^\s"']*[\\/])?{manager}(?:\.(?:bat|cmd|exe))?"""
_GLOBAL_OPTIONS = r"""(?:(?:--(?:cwd|dir|filter|prefix|workspace)(?:=\S+|\s+\S+)|-[wCF]\s+\S+|--[A-Za-z][A-Za-z0-9_.-]*(?:=\S+)?)\s+)*"""
_MANAGER_PATTERNS = tuple(
    re.compile(rf"""(?imx){_PREFIX}@?{_EXECUTABLE.format(manager=manager)}["']?\s+{_GLOBAL_OPTIONS}(?P<command>{'|'.join(commands)})\b(?P<args>[^;&|]*)""")
    for manager, commands in {
        "npm": ("add", "exec", "i", "install", "x"),
        "pnpm": ("add", "dlx", "exec", "i", "install"),
        "yarn": ("add", "dlx", "exec", "install"),
        "bun": ("add", "i", "install", "x"),
    }.items()
)
_EXEC_PATTERNS = tuple(
    (manager, re.compile(rf"""(?imx){_PREFIX}@?{_EXECUTABLE.format(manager=manager)}["']?\b(?P<args>[^;&|]*)"""))
    for manager in ("bunx", "npx")
)


def _arguments_reference(arguments: str, command: str) -> bool:
    executable_seen = False
    delimiter_seen = False
    executable_commands = {"bunx", "dlx", "exec", "npx", "x"}
    for raw in _TOKEN.findall(arguments):
        token = raw.strip("\"'").rstrip(",)")
        if token == "--":
            if not delimiter_seen and command in {"add", "exec", "i", "install", "npx"} and not executable_seen:
                delimiter_seen = True
                continue
            return False
        if _is_package(token) or (token.startswith(("--package=", "-p=")) and _is_package(token.partition("=")[2])):
            return command not in executable_commands or not executable_seen
        if token.startswith("-"):
            continue
        if command in executable_commands:
            if executable_seen:
                return False
            executable_seen = True
    return False


def _code_text(text: str, *, preserve_templates: bool = False) -> str:
    lines: list[str] = []
    in_template = False
    source = _BLOCK_COMMENT.sub("", text)
    if not preserve_templates:
        source = _TEMPLATE_LITERAL.sub("", source)
    for source_line in source.splitlines():
        line = source_line
        if in_template and not preserve_templates:
            if line.count("`") % 2:
                in_template = False
            continue
        if not preserve_templates and line.count("`") % 2:
            line = line.split("`", 1)[0]
            in_template = True
        stripped = line.lstrip()
        if stripped.startswith(("#", "//", "::", ";")) or re.match(r"(?i)rem(?:\s|\Z)", stripped):
            continue
        lines.append(re.split(r"\s+(?:#|//)", line, maxsplit=1)[0])
    return "\n".join(lines)


def _python_script_references(text: str) -> bool:
    tree = ast.parse(text)
    call_names = {"Popen", "call", "check_call", "check_output", "create_subprocess_exec", "run", "system"}
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        function = node.func
        name = function.attr if isinstance(function, ast.Attribute) else getattr(function, "id", "")
        if name not in call_names:
            continue
        first = node.args[0] if node.args else next((keyword.value for keyword in node.keywords if keyword.arg == "args"), None)
        if first is None:
            continue
        if isinstance(first, (ast.List, ast.Tuple)):
            values = tuple(item.value for item in first.elts if isinstance(item, ast.Constant) and isinstance(item.value, str))
            exact = len(values) == len(first.elts)
        elif name == "create_subprocess_exec":
            values = tuple(item.value for item in node.args if isinstance(item, ast.Constant) and isinstance(item.value, str))
            exact = len(values) == len(node.args)
        elif isinstance(first, ast.Constant) and isinstance(first.value, str):
            values = (first.value,)
            exact = True
        else:
            values = ()
            exact = False
        if exact and _script_references(" ".join(values)):
            return True
    return False


def _literal_list(text: str) -> tuple[str, ...] | None:
    values = tuple(match.group(2) for match in _JS_LITERAL.finditer(text))
    residue = _JS_LITERAL.sub("", text)
    return values if values and not residue.strip(" \t\r\n,") else None


def _node_bindings(code: str) -> tuple[dict[str, str], set[str]]:
    direct: dict[str, str] = {}
    namespaces = {match.group("name") for match in (*_NODE_NAMESPACE_IMPORT.finditer(code), *_NODE_NAMESPACE_REQUIRE.finditer(code))}
    for match in _NODE_NAMED_IMPORT.finditer(code):
        for item in match.group("bindings").split(","):
            binding = re.fullmatch(rf"\s*(?P<api>{'|'.join(sorted(_NODE_APIS))})(?:\s+as\s+(?P<name>{_JS_IDENTIFIER}))?\s*", item)
            if binding is not None:
                direct[binding.group("name") or binding.group("api")] = binding.group("api")
    for match in _NODE_NAMED_REQUIRE.finditer(code):
        for item in match.group("bindings").split(","):
            binding = re.fullmatch(rf"\s*(?P<api>{'|'.join(sorted(_NODE_APIS))})(?:\s*:\s*(?P<name>{_JS_IDENTIFIER}))?\s*", item)
            if binding is not None:
                direct[binding.group("name") or binding.group("api")] = binding.group("api")
    direct.update((match.group("name"), match.group("api")) for match in _NODE_MEMBER_REQUIRE.finditer(code))
    return direct, namespaces


def _node_call_references(code: str, callee: str, api: str) -> bool:
    prefix = rf"(?<![A-Za-z0-9_$.]){callee}\s*\(\s*"
    if api in {"exec", "execSync"}:
        for match in re.finditer(prefix + _JS_COMMAND_LITERAL + r"\s*(?:,|\))", code, re.DOTALL):
            command = match.group("double") or match.group("single") or match.group("template")
            if (match.group("template") is None or re.search(r"(?<!\\)\$\{", command) is None) and _manager_references(command):
                return True
        return False
    pattern = re.compile(prefix + r"""(?P<quote>["'])(?P<executable>(?:[^"'\s]*[\\/])?(?:npm|pnpm|yarn|bun|npx|bunx)(?:\.(?:cmd|exe))?)\1\s*,\s*\[(?P<arguments>.*?)\]""", re.DOTALL)
    return any((arguments := _literal_list(match.group("arguments"))) is not None and _manager_references(" ".join((match.group("executable"), *arguments))) for match in pattern.finditer(code))


def _node_api_references(code: str, binding_code: str) -> bool:
    direct, namespaces = _node_bindings(binding_code)
    calls = [(re.escape(name), api) for name, api in direct.items()]
    calls.extend((rf"{re.escape(namespace)}\s*\.\s*{api}", api) for namespace in namespaces for api in _NODE_APIS)
    calls.extend((rf"""require\(\s*["']{_CHILD_PROCESS_MODULE}["']\s*\)\s*\.\s*{api}""", api) for api in _NODE_APIS)
    return any(_node_call_references(code, callee, api) for callee, api in calls)


def _powershell_arguments(text: str) -> str:
    source = text.lstrip()
    if source.startswith("@("):
        depth = 0
        quote: str | None = None
        index = 0
        while index < len(source):
            character = source[index]
            if quote is not None:
                if character == "`":
                    index += 2
                    continue
                if character == quote:
                    if index + 1 < len(source) and source[index + 1] == quote:
                        index += 2
                        continue
                    quote = None
            elif character in "\"'":
                quote = character
            elif character == "(":
                depth += 1
                if depth > _MAX_WORKFLOW_PASSES:
                    return ""
            elif character == ")":
                depth -= 1
                if depth == 0:
                    return source[: index + 1]
            index += 1
        return ""
    if not source.startswith(('"', "'")):
        return ""
    index = 0
    end = 0
    while index < len(source) and source[index] in "\"'":
        quote = source[index]
        index += 1
        while index < len(source):
            if source[index] == "`":
                index += 2
                continue
            if source[index] == quote:
                if index + 1 < len(source) and source[index + 1] == quote:
                    index += 2
                    continue
                index += 1
                end = index
                break
            index += 1
        else:
            return ""
        while index < len(source) and source[index].isspace():
            index += 1
        if index >= len(source) or source[index] != ",":
            return source[:end]
        index += 1
        while index < len(source) and source[index].isspace():
            index += 1
        if index >= len(source) or source[index] not in "\"'":
            return ""
    return ""


def _powershell_reference(body: str) -> bool:
    manager = r"""["']?((?:[^\s"']*[\\/])?(?:npm|pnpm|yarn|bun|npx|bunx)(?:\.(?:cmd|exe))?)["']?"""
    named = re.search(rf"(?ix)-FilePath\s+{manager}", body)
    positional = re.match(rf"(?ix)^\s*{manager}\s+", body)
    argument_option = re.search(r"(?i)-ArgumentList\b", body)
    executable = named or positional
    if executable is None:
        return False
    arguments = _powershell_arguments(body[argument_option.end():] if argument_option is not None else body[executable.end():])
    payload = arguments.strip()
    if payload.startswith("@(") and payload.endswith(")"):
        payload = payload[2:-1]
    literal_arguments = _literal_list(payload)
    if "$" in executable.group(0) or "$" in payload or literal_arguments is None:
        return False
    return _manager_references(" ".join((executable.group(1), *literal_arguments)))


def _literal_api_references(code: str, binding_code: str) -> bool:
    if _node_api_references(code, binding_code):
        return True
    for match in _START_PROCESS.finditer(code):
        if _powershell_reference(match.group("body")):
            return True
    return False


def _manager_references(code: str) -> bool:
    return any(_arguments_reference(match.group("args"), match.group("command")) for pattern in _MANAGER_PATTERNS for match in pattern.finditer(code)) or any(_arguments_reference(match.group("args"), manager) for manager, pattern in _EXEC_PATTERNS for match in pattern.finditer(code))


def _script_references(text: str, *, python: bool = False) -> bool:
    if python:
        return _python_script_references(text)
    code = _code_text(text)
    return _IMPORT.search(code) is not None or _literal_api_references(_code_text(text, preserve_templates=True), code) or _manager_references(code)


def _environment(value: object) -> dict[str, str]:
    return {name: item for name, item in value.items() if isinstance(name, str) and isinstance(item, str)} if isinstance(value, dict) else {}


def _input_defaults(value: object) -> dict[str, str]:
    inputs = value.get("inputs") if isinstance(value, dict) else None
    return {name: item["default"] for name, item in inputs.items() if isinstance(name, str) and isinstance(item, dict) and isinstance(item.get("default"), str) and "${{" not in item["default"]} if isinstance(inputs, dict) else {}


def _workflow_triggers(document: Mapping[object, object]) -> Mapping[object, object]:
    return trigger if isinstance((trigger := document.get("on", document.get(True))), dict) else {}


def _workflow_input_alternatives(document: Mapping[object, object]) -> tuple[dict[str, str], ...]:
    triggers = _workflow_triggers(document)
    alternatives = tuple(_input_defaults(triggers.get(name)) for name in ("workflow_call", "workflow_dispatch") if name in triggers)
    return alternatives or ({},)


def _resolve_text(text: str, inputs: Mapping[str, str], matrix: Mapping[str, str], environment: Mapping[str, str]) -> str:
    expanded = text
    for _ in range(_MAX_WORKFLOW_PASSES):
        previous = expanded
        for pattern, values in ((_INPUT_REFERENCE, inputs), (_MATRIX_REFERENCE, matrix), (_ENV_REFERENCE, environment)):
            expanded = pattern.sub(lambda match, selected=values: selected.get(match.group(1) or match.group(2), match.group(0)), expanded)
        for name, value in sorted(environment.items()):
            for reference in (f"${{{name}}}", f"%{name}%", f"$env:{name}"):
                expanded = expanded.replace(reference, value)
            expanded = re.sub(rf"\${re.escape(name)}(?![A-Za-z0-9_])", lambda _match, replacement=value: replacement, expanded)
        if expanded == previous:
            break
    return expanded


def _resolved_environment(scopes: Iterable[object], inputs: Mapping[str, str], matrix: Mapping[str, str]) -> dict[str, str]:
    environment: dict[str, str] = {}
    for scope in scopes:
        environment.update(_environment(scope))
    for _ in range(_MAX_WORKFLOW_PASSES):
        updated = {name: _resolve_text(value, inputs, matrix, environment) for name, value in environment.items()}
        if updated == environment:
            break
        environment = updated
    return environment


def _matrix_rows(value: object) -> tuple[dict[str, str], ...]:
    if not isinstance(value, dict):
        return ({},)
    raw_axes = {name: items for name, items in value.items() if isinstance(name, str) and name not in {"exclude", "include"}}
    axes = {name: items for name, items in raw_axes.items() if isinstance(items, list) and items and all(isinstance(item, str) and "${{" not in item for item in items)}
    rows: list[dict[str, str]] = [{}]
    for name, items in axes.items():
        rows = [(row | {name: item}) for row in rows for item in items][:_MAX_WORKFLOW_STATES]
    if len(axes) == len(raw_axes):
        exclusions = value.get("exclude")
        if isinstance(exclusions, list):
            rows = [row for row in rows if not any(isinstance(item, dict) and item and all(isinstance(selected, str) and row.get(name) == selected for name, selected in item.items()) for item in exclusions)]
    includes = value.get("include")
    static_includes = [item for item in includes if isinstance(item, dict) and item and all(isinstance(name, str) and isinstance(selected, str) and "${{" not in selected for name, selected in item.items())] if isinstance(includes, list) else []
    if not raw_axes:
        rows = []
    originals = tuple(dict(row) for row in rows) if len(axes) == len(raw_axes) else ()
    for included in static_includes:
        indexes = [index for index, original in enumerate(originals) if all(name not in original or original[name] == selected for name, selected in included.items())]
        if indexes:
            for index in indexes:
                rows[index].update(included)
        else:
            rows.append(dict(included))
    return tuple(rows[:_MAX_WORKFLOW_STATES]) or ({},)


def _local_action_manifest(repository_root: Path, uses: str) -> tuple[str, Mapping[object, object]] | None:
    if not uses.startswith("./"):
        return None
    if "${{" in uses:
        return None
    candidate = PurePosixPath(uses[2:])
    if not candidate.parts or candidate.is_absolute() or "\\" in uses or any(part in {"", ".", ".."} for part in candidate.parts):
        raise ValueError(f"non-canonical local action path: {uses!r}")
    candidates = (candidate,) if candidate.name in {"action.yml", "action.yaml"} else (candidate / "action.yml", candidate / "action.yaml")
    found: list[tuple[str, Path]] = []
    for relative in candidates:
        source = repository_root / relative
        try:
            mode = source.lstat().st_mode
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(mode) or not stat.S_ISREG(mode):
            raise ValueError(f"local action manifest is not a regular file: {relative.as_posix()}")
        try:
            source.resolve(strict=True).relative_to(repository_root)
        except ValueError as error:
            raise ValueError(f"local action manifest escapes repository: {relative.as_posix()}") from error
        found.append((relative.as_posix(), source))
    if len(found) != 1:
        category = "missing" if not found else "ambiguous"
        raise ValueError(f"{category} local action manifest: {uses}")
    path, source = found[0]
    document = yaml.safe_load(source.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"local action manifest must be a YAML mapping: {path}")
    return path, document


def _local_action(repository_root: Path, uses: str, inputs: Mapping[str, str], environment: Mapping[str, str], visited: dict[tuple[object, ...], frozenset[str]], active: tuple[str, ...]) -> set[str]:
    loaded = _local_action_manifest(repository_root, uses)
    if loaded is None:
        return set()
    path, document = loaded
    if path in active:
        raise ValueError(f"local composite action cycle: {' -> '.join((*active, path))}")
    if len(active) >= _MAX_LOCAL_ACTION_DEPTH:
        raise ValueError(f"local composite action depth exceeds {_MAX_LOCAL_ACTION_DEPTH}: {' -> '.join((*active, path))}")
    key = (path, tuple(sorted(inputs.items())), tuple(sorted(environment.items())))
    if key in visited:
        return set(visited[key])
    contexts = _action_contexts(document, repository_root=repository_root, inputs=inputs, inherited_environment=environment, visited=visited, active=(*active, path))
    visited[key] = frozenset(contexts)
    return contexts


def _step_contexts(steps: object, *, repository_root: Path, inputs: Mapping[str, str], matrix: Mapping[str, str], scopes: tuple[object, ...], action_visited: dict[tuple[object, ...], frozenset[str]], action_active: tuple[str, ...]) -> set[str]:
    if not isinstance(steps, list):
        return set()
    contexts: set[str] = set()
    for step in steps:
        if not isinstance(step, dict):
            continue
        environment = _resolved_environment((*scopes, step.get("env")), inputs, matrix)
        raw_inputs = step.get("with")
        resolved_inputs = {name: _resolve_text(value, inputs, matrix, environment) for name, value in raw_inputs.items() if isinstance(name, str) and isinstance(value, str)} if isinstance(raw_inputs, dict) else {}
        uses = step.get("uses")
        local_uses = False
        if isinstance(uses, str):
            resolved_uses = _resolve_text(uses, inputs, matrix, environment)
            local_uses = resolved_uses.startswith("./")
            if _ACTION_USE.fullmatch(resolved_uses.strip()) or (local_uses and _local_action(repository_root, resolved_uses, resolved_inputs, environment, action_visited, action_active)):
                contexts.add("uses")
        if not local_uses and any(_script_references(value) for value in resolved_inputs.values()):
            contexts.add("with")
        run = step.get("run")
        if isinstance(run, str) and _script_references(_resolve_text(run, inputs, matrix, environment)):
            contexts.add("run")
    return contexts


def _action_contexts(document: Mapping[object, object], *, repository_root: Path, inputs: Mapping[str, str], inherited_environment: Mapping[str, str], visited: dict[tuple[object, ...], frozenset[str]], active: tuple[str, ...]) -> set[str]:
    runs = document.get("runs")
    if not isinstance(runs, dict) or runs.get("using") != "composite":
        return set()
    steps = runs.get("steps")
    if not isinstance(steps, list):
        raise ValueError(f"composite action steps must be a list: {active[-1]}")
    selected_inputs = _input_defaults(document) | dict(inputs)
    return _step_contexts(steps, repository_root=repository_root, inputs=selected_inputs, matrix={}, scopes=(inherited_environment, document.get("env")), action_visited=visited, action_active=active)


def _action(path: str, content: bytes, *, repository_root: Path) -> set[str]:
    document = yaml.safe_load(content.decode("utf-8", "strict"))
    if not isinstance(document, dict):
        raise ValueError("action manifest must be a YAML mapping")
    return _action_contexts(document, repository_root=repository_root, inputs={}, inherited_environment={}, visited={}, active=(path,))


def _local_workflow(repository_root: Path, reusable: str, inputs: Mapping[str, str], visited: frozenset[str], action_visited: dict[tuple[object, ...], frozenset[str]]) -> set[str]:
    path = reusable[2:] if reusable.startswith("./") else ""
    if not path or path in visited or len(visited) >= _MAX_WORKFLOW_PASSES:
        return set()
    source = repository_root / path
    if source.is_symlink() or not source.is_file():
        return set()
    try:
        document = yaml.safe_load(source.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, yaml.YAMLError):
        return set()
    if not isinstance(document, dict):
        return set()
    defaults = _input_defaults(_workflow_triggers(document).get("workflow_call"))
    return _workflow_contexts(document, repository_root=repository_root, input_alternatives=(defaults | dict(inputs),), visited=visited | {path}, action_visited=action_visited)


def _workflow_contexts(document: Mapping[object, object], *, repository_root: Path, input_alternatives: tuple[dict[str, str], ...], visited: frozenset[str], action_visited: dict[tuple[object, ...], frozenset[str]]) -> set[str]:
    root_environment = _environment(document.get("env"))
    jobs = document.get("jobs")
    if not isinstance(jobs, dict):
        return set()
    contexts: set[str] = set()
    for inputs in input_alternatives:
        for job in jobs.values():
            if not isinstance(job, dict):
                continue
            strategy = job.get("strategy")
            matrix = strategy.get("matrix") if isinstance(strategy, dict) else None
            for row in _matrix_rows(matrix):
                job_environment = _resolved_environment((root_environment, job.get("env")), inputs, row)
                job_inputs = job.get("with")
                resolved_inputs = {name: _resolve_text(value, inputs, row, job_environment) for name, value in job_inputs.items() if isinstance(name, str) and isinstance(value, str)} if isinstance(job_inputs, dict) else {}
                reusable = job.get("uses")
                if resolved_inputs and (any(_script_references(value) for value in resolved_inputs.values()) or (isinstance(reusable, str) and _REUSABLE_WORKFLOW_USE.fullmatch(reusable.strip()) and _local_workflow(repository_root, reusable, resolved_inputs, visited, action_visited))):
                    contexts.add("with")
                contexts.update(_step_contexts(job.get("steps"), repository_root=repository_root, inputs=inputs, matrix=row, scopes=(root_environment, job.get("env")), action_visited=action_visited, action_active=()))
    return contexts


def _workflow(content: bytes, *, repository_root: Path) -> set[str]:
    document = yaml.safe_load(content.decode("utf-8", "strict"))
    if not isinstance(document, dict):
        raise ValueError("workflow must be a YAML mapping")
    return _workflow_contexts(document, repository_root=repository_root, input_alternatives=_workflow_input_alternatives(document), visited=frozenset(), action_visited={})


def _selected(value: object, names: frozenset[str]) -> tuple[object, ...]:
    if isinstance(value, dict):
        return tuple(found for key, item in value.items() for found in (((item,) if key in names else ()) + _selected(item, names)))
    if isinstance(value, list):
        return tuple(found for item in value for found in _selected(item, names))
    return ()


def _configuration(path: str, content: bytes) -> set[str]:
    text = content.decode("utf-8", "strict")
    name = PurePosixPath(path).name
    if name == "renovate.json":
        found = any(_tree_has_package(value) for value in _selected(json.loads(text), frozenset({"ignoreDeps", "matchPackageNames"})))
    elif name in {"dependabot.yaml", "dependabot.yml"}:
        found = any(_tree_has_package(value) for value in _selected(yaml.safe_load(text), frozenset({"dependency-name", "patterns"})))
    elif name == "pnpm-workspace.yaml":
        document = yaml.safe_load(text)
        if not isinstance(document, dict):
            raise ValueError("pnpm-workspace.yaml must be a YAML mapping")
        found = any(_tree_has_package(document.get(field)) for field in ("catalog", "catalogs"))
    elif name == "bunfig.toml":
        document = tomllib.loads(text)
        found = any(_tree_has_package(value) for value in _selected(document, frozenset({"dependencies", "packages"})))
    elif name.lower() == "nuget.config":
        found = any(element.attrib.get("pattern") == PACKAGE_NAME for element in ET.fromstring(text).iter())
    elif name == ".pnpmfile.cjs":
        code = _code_text(text)
        found = _PNPMFILE_DEPENDENCY.search(code) is not None or _script_references(code)
    else:
        found = any(
            _is_dependency(line.partition("=")[2].strip() if "=" in line else line.strip()) or _script_references(line)
            for line in _code_text(text).splitlines()
            if line.strip()
        )
    return {"configuration-reference"} if found else set()


def _surface(rule: DependencySurfaceRule, path: str, content: bytes, *, repository_root: Path) -> set[str]:
    if rule.category == "dependency-manifest":
        return _manifest(path, content)
    if rule.category == "lockfile":
        return _lockfile(path, content)
    if rule.category == "workflow":
        return _workflow(content, repository_root=repository_root)
    if rule.category == "composite-action":
        return _action(path, content, repository_root=repository_root)
    if rule.category == "install-bootstrap-script":
        text = content.decode("utf-8", "strict")
        return {"script-reference"} if _script_references(text, python=path.endswith(".py")) else set()
    if rule.category == "dependency-configuration":
        return _configuration(path, content)
    raise ValueError(f"unsupported dependency-surface category: {rule.category}")


def _tracked_paths(root: Path) -> tuple[str, ...]:
    result = subprocess.run(("git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"), cwd=root, check=True, capture_output=True)
    return tuple(sorted(path for path in result.stdout.decode("utf-8", "strict").split("\0") if path))


def _target(root: Path) -> str:
    result = subprocess.run(("git", "rev-parse", "--verify", "--end-of-options", "HEAD^{commit}"), cwd=root, check=True, capture_output=True, text=True)
    return result.stdout.strip()


def _policy_digest() -> str:
    return canonical_sha256({
        "schema": "workflow-delivery/v3/consumer-policy",
        "policy-id": CONSUMER_POLICY_ID,
        "package": PACKAGE_NAME,
        "catalog": [rule.to_document() for rule in DEPENDENCY_SURFACE_CATALOG],
        "approved-exceptions": [item.to_document() for item in APPROVED_CONSUMER_EXCEPTIONS],
    })


def scan_consumer_policy(repository_root: Path) -> ConsumerPolicyResult:
    """Scan the closed dependency-surface catalog in one Git worktree."""
    root = repository_root.resolve(strict=True)
    scanned: list[SurfaceDigest] = []
    admitted: list[SurfaceDigest] = []
    consumers: set[str] = set()
    errors: list[str] = []
    seen: set[str] = set()
    exceptions = {item.path: item for item in APPROVED_CONSUMER_EXCEPTIONS}
    for path in _tracked_paths(root):
        rule = classify_dependency_surface(path)
        if rule is None:
            continue
        source = root / path
        try:
            mode = source.lstat().st_mode
        except FileNotFoundError:
            continue
        seen.add(path)
        try:
            if stat.S_ISLNK(mode) or not stat.S_ISREG(mode):
                raise ValueError("dependency surface is not a regular file")
            content = source.read_bytes()
            digest = SurfaceDigest(path, _sha256(content))
            scanned.append(digest)
            contexts = _surface(rule, path, content, repository_root=root)
        except (OSError, SyntaxError, UnicodeDecodeError, ValueError, json.JSONDecodeError, tomllib.TOMLDecodeError, ET.ParseError, yaml.YAMLError) as error:
            errors.append(f"{path}: {error}")
            continue
        exception = exceptions.get(path)
        if exception is None:
            if contexts:
                consumers.add(path)
            continue
        if rule.category == exception.category and exception.context in contexts and digest.content_digest == exception.content_digest:
            admitted.append(digest)
            contexts.remove(exception.context)
        else:
            consumers.add(path)
        if contexts:
            consumers.add(path)
    errors.extend(f"{item.path}: approved consumer-policy exception is missing" for item in APPROVED_CONSUMER_EXCEPTIONS if item.path not in seen)
    if errors:
        raise ConsumerPolicyScanError(errors)
    result = ConsumerPolicyResult(
        policy_id=CONSUMER_POLICY_ID,
        policy_digest=_policy_digest(),
        target=_target(root),
        scanned_surfaces=tuple(sorted(scanned, key=lambda item: item.path)),
        admitted_exceptions=tuple(sorted(admitted, key=lambda item: item.path)),
        consumers=tuple(sorted(consumers)),
    )
    validate_consumer_policy_result(result)
    return result


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository-root", type=Path, default=Path.cwd())
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Run the policy and emit its deterministic result category."""
    options = _parser().parse_args(argv)
    try:
        result = scan_consumer_policy(options.repository_root)
    except (OSError, subprocess.CalledProcessError, UnicodeDecodeError, ValueError) as error:
        sys.stderr.write(f"consumer-policy result=scan-error: {error}\n")
        return 2
    document = result.to_document()
    document["result"] = "consumer" if result.consumers else "clean"
    sys.stdout.buffer.write(json.dumps(document, ensure_ascii=False, separators=(",", ":"), sort_keys=True).encode("utf-8") + b"\n")
    return 1 if result.consumers else 0


if __name__ == "__main__":
    sys.exit(main())

# fmt: on
