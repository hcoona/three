"""Transfer comparisons and other-job responsibility to native CI groups."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING

from repository_path_patterns import matches, safe_path, safe_record_pattern
from workflow_delivery_v3_hk import changed_paths

if TYPE_CHECKING:
    from collections.abc import Callable

ROOT = Path(__file__).resolve().parents[2]
AZURE = "src/private/app/azureauth-credprovider"
SCHOLARLY = "src/private/lib/scholarly-publication"
SCOPES = (
    "validation",
    "mise",
    "dotnet",
    "node",
    "azureauth",
    "ruby",
    "scholarly",
)
ALL_INPUTS = {
    ".github/workflows/ci.yml",
    "eng/scripts/ci_scope.py",
    "eng/scripts/repository_path_patterns.py",
    "eng/scripts/prepare_ci_control_inputs.py",
    "mise.toml",
    "mise.lock",
}
CONTROL_PROJECT = (
    "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj"
)
NUGET_SCENARIO_ROOTS = {
    "src/private/app/workflow-delivery",
    str(PurePosixPath(CONTROL_PROJECT).parent),
    "tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-github-packages",
}
PYTHON_INPUTS = {
    "pyproject.toml",
    "uv.lock",
    "uv.toml",
    ".python-version",
    ".config/uv/uv.toml",
    "conftest.py",
    "pytest.ini",
    "eng/scripts/sync_python_version.py",
    "eng/scripts/run_python_tests.py",
}
DOTNET_INPUTS = {
    ".editorconfig",
    ".gitattributes",
    "global.json",
    "NuGet.Config",
    "nuget.config",
    ".config/dotnet-tools.json",
    "version.json",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "dirs.proj",
}
NODE_INPUTS = {
    "package.json",
    "pnpm-lock.yaml",
    "pnpm-workspace.yaml",
    ".npmrc",
    "version.json",
    ".config/dotnet-tools.json",
    "global.json",
}


def git(root: Path, *arguments: str) -> str:
    """Read the candidate's Git inventory without package restore."""
    return subprocess.run(
        ("git", *arguments),
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    ).stdout


def _under(path: str, directory: str) -> bool:
    return path == directory or path.startswith(directory.rstrip("/") + "/")


def _dotnet_input(path: str, roots: set[str]) -> bool:
    inherited = PurePosixPath(path).name in {
        ".editorconfig",
        ".gitattributes",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "version.json",
        "NuGet.Config",
        "nuget.config",
        "global.json",
    }
    return path in DOTNET_INPUTS or any(
        _under(path, directory)
        or (inherited and _under(directory, str(PurePosixPath(path).parent)))
        for directory in roots
    )


def _nuget_scenario_input(path: str) -> bool:
    products = "tests/private/app/workflow-delivery/fixtures/products"
    nuget = products + "/hcoona-release-smoke-github-packages"
    if _under(path, products) and not _under(path, nuget):
        return _dotnet_input(path, {nuget})
    return _dotnet_input(path, NUGET_SCENARIO_ROOTS)


def _azure_input(path: str) -> bool:
    return (
        _dotnet_input(path, {AZURE, "tests/private/app/azureauth-credprovider"})
        or path.startswith(
            (
                "tests/private/app/azureauth-credprovider/",
                "eng/scripts/azureauth-credprovider/",
                "src/public/lib/nbgv-python/",
            )
        )
        or path in PYTHON_INPUTS
    )


def _work_roots(files: set[str]) -> dict[str, set[str]]:
    roots: dict[str, set[str]] = {"node": set(), "dotnet": set(), "ruby": set()}
    for item in files:
        directory = str(PurePosixPath(item).parent)
        if item.endswith("/package.json") and item.startswith("src/"):
            roots["node"].add(directory)
        if item.endswith((".csproj", ".fsproj", ".vbproj")):
            roots["dotnet"].add(directory)
        if item.endswith(".gemspec"):
            roots["ruby"].add(directory)
    return roots


def _work_scopes(path: str, roots: dict[str, set[str]]) -> set[str]:
    if path in ALL_INPUTS:
        return set(SCOPES)
    applicability = {
        "mise": PurePosixPath(path).name in {"mise.toml", "mise.lock"},
        "dotnet": _dotnet_input(path, roots["dotnet"]),
        "node": path in NODE_INPUTS
        or any(_under(path, directory) for directory in roots["node"]),
        "azureauth": _azure_input(path),
        "ruby": path in {"Gemfile", "Gemfile.lock", ".ruby-version"}
        or any(_under(path, directory) for directory in roots["ruby"]),
        "scholarly": _under(path, SCHOLARLY)
        or path.startswith(".agents/skills/scholarly-")
        or path in {"apm.yml", "apm.lock.yaml"},
    }
    return {scope for scope, selected in applicability.items() if selected}


def _legacy_release_input(path: str) -> bool:
    return (
        path.startswith(
            (
                ".github/workflows/",
                "eng/release/",
                "src/public/lib/hcoona-release-smoke",
                "src/public/lib/three-workflow-release-",
                "tests/fixtures/workflow-release-",
                "tests/test_workflow_release_",
            )
        )
        or path.endswith(("/three.release.yml", "/three.quality.yml"))
        or path
        in {
            ".github/actionlint.yaml",
            "eng/release",
            "eng/scripts/publish_node_gpr_idempotent.sh",
            "eng/scripts/publish_node_npmjs_idempotent.sh",
            "eng/scripts/release_orchestrate_lint_caller_completeness.sh",
            "eng/scripts/verify_python_distribution_exactness.py",
            "eng/scripts/workflow_release_acceptance_gate.py",
            "eng/scripts/workflow_release_control.py",
        }
    )


def select(
    root: Path,
    paths: tuple[str, ...],
    *,
    base: str,
    full: bool = False,
    control_inputs: dict | None = None,
) -> dict:
    """Select retained special jobs; native groups own product impact."""
    files = set(
        git(root, "ls-tree", "-r", "--name-only", "-z", "HEAD").split("\0")
    )
    if base:
        files.update(
            git(root, "ls-tree", "-r", "--name-only", "-z", base).split("\0")
        )
    reasons = {scope: set() for scope in SCOPES}
    reasons["validation"].add("source conformance")
    roots = _work_roots(files)
    for path in paths:
        for job in _work_scopes(path, roots):
            reasons[job].add(path)
        reasons["dotnet"].update(_resource_reasons(path, control_inputs))
    if full:
        for why in reasons.values():
            why.add("explicit full validation")
    return {
        "scopes": {job: bool(why) for job, why in reasons.items()},
        "nuget_reproducibility": full
        or any(
            path in ALL_INPUTS
            or path == "stylecop.json"
            or _nuget_scenario_input(path)
            or bool(_resource_reasons(path, control_inputs))
            for path in paths
        ),
        "reasons": {job: sorted(why) for job, why in reasons.items()},
    }


def _resource_reasons(path: str, response: dict | None) -> set[str]:
    if response is None:
        return set()
    return {
        f"{path} -> {endpoint['project']} ({endpoint['revision']})"
        for endpoint in (response["basis"], response["candidate"])
        if path in endpoint["inputs"]
    }


def _unique_object(
    pairs: list[tuple[str, object]], context: str = "control input"
) -> dict:
    result: dict = {}
    for name, value in pairs:
        if name in result:
            message = f"Duplicate {context} field: {name}"
            raise ValueError(message)
        result[name] = value
    return result


FAMILY_CATALOG = "docs/governance/record-families.yaml"
RECORD_CATALOG_VERSION = 3


def _entry_modes(root: Path, revision: str) -> dict[str, str]:
    entries = {}
    for raw in git(root, "ls-tree", "-r", "-z", "--full-tree", revision).split(
        "\0"
    ):
        if raw:
            metadata, path = raw.split("\t", 1)
            mode, _, _ = metadata.split(" ")
            entries[path] = mode
    return entries


def _endpoint_text(
    root: Path, revision: str, modes: dict[str, str], path: str
) -> str:
    if modes.get(path) not in {"100644", "100755"}:
        raise ValueError(
            "Required endpoint configuration is not a regular file: " + path
        )
    return git(root, "show", f"{revision}:{path}")


def _endpoint_yaml(
    root: Path, revision: str, modes: dict[str, str], path: str
) -> object:
    # Bootstrap callers use only standard-library selectors.
    import yaml  # noqa: PLC0415 - Only endpoint record parsing needs locked PyYAML.

    class _OwnerCatalogLoader(yaml.SafeLoader):
        """Reject duplicate mapping keys with the locked safe parser."""

    def mapping(loader: yaml.SafeLoader, node: yaml.MappingNode) -> dict:
        loader.flatten_mapping(node)
        return _unique_object(
            loader.construct_pairs(node, deep=True), f"{path} YAML"
        )

    _OwnerCatalogLoader.add_constructor(
        yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
        mapping,
    )
    return yaml.load(
        _endpoint_text(root, revision, modes, path),
        Loader=_OwnerCatalogLoader,  # noqa: S506 - Subclass adds duplicate-key checks to SafeLoader.
    )


def _record_owner_bindings(
    root: Path, revision: str, modes: dict[str, str]
) -> list[dict]:
    if FAMILY_CATALOG not in modes:
        return []
    catalog = _endpoint_yaml(root, revision, modes, FAMILY_CATALOG)
    if (
        not isinstance(catalog, dict)
        or type(catalog.get("schema_version")) is not int
        or catalog["schema_version"] != RECORD_CATALOG_VERSION
        or not isinstance(catalog.get("families"), list)
        or not isinstance(catalog.get("bindings"), list)
    ):
        message = "Unsupported endpoint record-family catalog"
        raise ValueError(message)
    families = set()
    for family in catalog["families"]:
        if (
            not isinstance(family, dict)
            or not isinstance(family.get("id"), str)
            or not family["id"]
            or family["id"] in families
        ):
            message = "Malformed endpoint record families"
            raise ValueError(message)
        families.add(family["id"])
    identifiers = set()
    current = []
    for binding in catalog["bindings"]:
        if (
            not isinstance(binding, dict)
            or any(
                not isinstance(binding.get(key), str) or not binding[key]
                for key in (
                    "id",
                    "family",
                    "namespace",
                    "state",
                    "carrier",
                    "path",
                )
            )
            or binding["id"] in identifiers
            or binding["family"] not in families
            or binding["state"] not in {"current", "scheduled"}
            or binding["carrier"] not in {"repository-file", "repository-files"}
            or not safe_record_pattern(binding["path"])
            or (
                binding["carrier"] == "repository-file"
                and any(char in binding["path"] for char in "*?[]")
            )
        ):
            message = "Malformed endpoint record binding"
            raise ValueError(message)
        identifiers.add(binding["id"])
        if binding["state"] == "current":
            current.append(binding)
    return current


CONTROL_CATALOG_VERSION = 2
CONTROL_CATALOG = "docs/governance/controls.yaml"
CHECKER_CONTRACT = "docs/governance/checker-contract.md"
RECORD_CONTROL = "repository-record-validation"


def _mise_task_info(checkout: Path, name: str) -> str:
    return subprocess.run(
        ("mise", "tasks", "info", name, "--json"),
        cwd=checkout,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
        timeout=30,
    ).stdout


def _record_task_script(
    checkout: Path, name: str, raw: str, modes: dict[str, str]
) -> str:
    task = json.loads(
        raw,
        object_pairs_hook=lambda pairs: _unique_object(
            pairs, "native mise task"
        ),
    )
    source = str(checkout / "mise.toml")
    if (
        not isinstance(task, dict)
        or task.get("name") != "//:" + name
        or task.get("source") != source
        or task.get("config_sources") != [source]
        or task.get("dir") != str(checkout)
        or any(
            task.get(key) != []
            for key in ("depends", "depends_post", "wait_for", "env")
        )
        or any(task.get(key) is not None for key in ("file", "shell"))
        or not isinstance(task.get("run"), list)
        or len(task["run"]) != 1
        or not isinstance(task["run"][0], str)
    ):
        raise ValueError("Unsupported native record task context: " + name)
    command = re.fullmatch(
        r"uv run --script ([A-Za-z0-9_./-]+\.py)", task["run"][0]
    )
    if command is None or not safe_path(command[1]):
        raise ValueError("Unsupported native record task command: " + name)
    script = command[1]
    if modes.get(script) not in {"100644", "100755"}:
        raise ValueError(
            "Native record task script is not a committed regular file: "
            + script
        )
    return script


def record_control_owners(
    root: Path,
    revision: str,
    checkout: Path,
    *,
    query: Callable[[Path, str], str] = _mise_task_info,
) -> dict[str, list[dict]]:
    """Derive finite local record responsibility without executing its tasks."""
    modes = _entry_modes(root, revision)
    if CONTROL_CATALOG not in modes:
        return {}
    catalog = _endpoint_yaml(root, revision, modes, CONTROL_CATALOG)
    if (
        not isinstance(catalog, dict)
        or type(catalog.get("schema_version")) is not int
        or catalog["schema_version"] != CONTROL_CATALOG_VERSION
        or not isinstance(catalog.get("controls"), list)
    ):
        message = "Unsupported endpoint control catalog"
        raise ValueError(message)
    selected = [
        entry
        for entry in catalog["controls"]
        if isinstance(entry, dict) and entry.get("id") == RECORD_CONTROL
    ]
    if not selected:
        return {}
    if len(selected) != 1:
        message = "Conflicting endpoint record controls"
        raise ValueError(message)
    control = selected[0]
    if control.get("state") == "scheduled":
        return {}
    implementation = control.get("implementation")
    if (
        control.get("state") != "current"
        or control.get("class") != "mechanical"
        or control.get("runner") != "local-tool"
        or control.get("enforcement") != "advisory"
        or control.get("execution_points") != ["local-validation"]
        or not isinstance(control.get("governing_rules"), list)
        or CHECKER_CONTRACT not in control["governing_rules"]
        or not isinstance(implementation, dict)
        or implementation.get("kind") != "repository-path"
        or not isinstance(implementation.get("value"), str)
        or not safe_path(implementation["value"])
    ):
        message = "Unsupported active record-control responsibility"
        raise ValueError(message)
    _endpoint_text(root, revision, modes, CHECKER_CONTRACT)
    committed_mise = _endpoint_text(root, revision, modes, "mise.toml")
    checkout = checkout.resolve()
    if (
        git(checkout, "rev-parse", "HEAD").strip() != revision
        or (checkout / "mise.toml").read_text(encoding="utf-8")
        != committed_mise
    ):
        message = "Record task metadata requires the exact committed endpoint"
        raise ValueError(message)
    scripts = {
        name: _record_task_script(checkout, name, query(checkout, name), modes)
        for name in ("records:check", "records:test")
    }
    if scripts["records:check"] != implementation["value"]:
        message = "Record control and native task implementation disagree"
        raise ValueError(message)
    reasons: dict[str, list[dict]] = {}
    for name, rule in (
        ("records:check", "record-control:" + RECORD_CONTROL),
        ("records:test", "record-regression:records:test"),
    ):
        script = scripts[name]
        reasons.setdefault(script, []).append(
            {
                "owner": "record-system",
                "target": script,
                "rule": rule,
                "sources": sorted(
                    [CONTROL_CATALOG, "mise.toml", CHECKER_CONTRACT]
                ),
            }
        )
    return reasons


def _retiring_python_consumers(path: str) -> tuple[str, ...]:
    """Keep exact old readers until retirement removes the comparison basis."""
    return {
        ".gitattributes": (
            "src/public/lib/three-workflow-delivery-v3/tests/adapters/test_dotnet.py",
        ),
        "eng/scripts/sync_python_build_backend.py": (
            "src/public/lib/three-workflow-delivery-v3/tests/repository/test_python_backend.py",
        ),
        "eng/scripts/workflow_delivery_v3_prepare_static_reference.py": (
            "src/public/lib/three-workflow-delivery-v3/tests/release/test_static_reference.py",
        ),
        "eng/scripts/workflow_delivery_v3_run_created_epoch.py": (
            (
                "src/public/lib/three-workflow-delivery-v3/tests/contracts/"
                "test_workflow_delivery_v3_run_created_epoch.py"
            ),
        ),
        "eng/scripts/workflow_delivery_v3_static_reference.py": (
            "src/public/lib/three-workflow-delivery-v3/tests/release/test_static_reference.py",
            "src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py",
        ),
        ".github/actions/workflow-delivery-v3-python-setup/action.yml": (
            ".github/workflows/workflow-delivery-v3-bootstrap-python.yml",
            "tests/eng/test_python_runtime.py",
        ),
        (
            ".github/actions/workflow-delivery-v3-ruby-operation-record/"
            "action.yml"
        ): (".github/workflows/workflow-delivery-v3-ruby-bootstrap.yml",),
        ".github/actions/workflow-delivery-v3-ruby-setup/action.yml": (
            ".github/workflows/workflow-delivery-v3-ruby-bootstrap.yml",
        ),
        ".github/workflow-delivery/bootstrap/python-pypi-request.json": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/acceptance/"
                "python_bootstrap_contract.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/acceptance/"
                "test_python_bootstrap_contract.py"
            ),
        ),
        ".github/workflow-delivery/bootstrap/python-request.json": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/acceptance/"
                "python_bootstrap_contract.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/acceptance/"
                "test_python_bootstrap_contract.py"
            ),
        ),
        (
            ".github/workflow-delivery/configuration/"
            "hcoona-release-smoke-ruby-github-packages.json"
        ): (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/ruby_controls.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/repository/"
                "test_ruby_control_version.py"
            ),
        ),
        (
            ".github/workflow-delivery/configuration/"
            "hcoona-release-smoke-ruby-rubygems.json"
        ): (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/ruby_controls.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/repository/"
                "test_ruby_control_version.py"
            ),
        ),
        (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-github-packages.json"
        ): (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/descriptors.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/compiler.py"
            ),
        ),
        ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/descriptors.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/compiler.py"
            ),
        ),
        (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-python-pypi.json"
        ): (
            (
                "eng/workflow-delivery/v3/policies/"
                "hcoona-release-smoke-python.yml"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release/python_governance.py"
            ),
        ),
        (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-python-testpypi.json"
        ): (
            (
                "eng/workflow-delivery/v3/policies/"
                "hcoona-release-smoke-python.yml"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release/python_governance.py"
            ),
        ),
        (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-ruby-github-packages.json"
        ): (
            ("eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby.yml"),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release/ruby_governance.py"
            ),
        ),
        (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-ruby-rubygems.json"
        ): (
            ("eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby.yml"),
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release/ruby_governance.py"
            ),
        ),
        ".github/workflow-delivery/native/python-requests.json": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/acceptance/python_native_contract.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/acceptance/"
                "test_python_bootstrap_workflow.py"
            ),
        ),
        ".github/workflow-delivery/requests/hcoona-release-smoke-ruby.json": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/ruby_controls.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/repository/"
                "test_ruby_control_version.py"
            ),
        ),
        (
            "eng/workflow-delivery/v3/policies/"
            "hcoona-release-smoke-github-packages.yml"
        ): (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/catalogs.py"
            ),
        ),
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-npm.yml": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/catalogs.py"
            ),
        ),
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-python.yml": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/catalogs.py"
            ),
        ),
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby.yml": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/catalogs.py"
            ),
        ),
        "eng/workflow-delivery/v3/python-build-constraints.txt": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/repository/python_provider.py"
            ),
            "eng/scripts/sync_python_build_backend.py",
        ),
        "eng/scripts/workflow_delivery_v3_native_npm.mjs": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/acceptance/npm_fixture.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/acceptance/"
                "test_npm_fixture.py"
            ),
        ),
        "eng/scripts/workflow_delivery_v3_static_reference_node.mjs": (
            (
                "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release/static_reference_authority.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/release/"
                "test_static_reference_sidecars.py"
            ),
        ),
        "hk.pkl": (
            (
                "src/public/lib/three-workflow-delivery-v3/tests/ci/"
                "test_scenarios.py"
            ),
            (
                "src/public/lib/three-workflow-delivery-v3/tests/"
                "test_hk_trigger.py"
            ),
        ),
    }.get(path, ())


def _path_owner_reasons(  # noqa: C901, PLR0912, PLR0913 - Finite roles share explicit endpoint facts.
    path: str,
    *,
    modes: dict[str, str],
    work_roots: dict[str, set[str]],
    manifests: tuple[str, ...],
    bindings: list[dict],
    native: dict,
) -> list[dict]:
    """Describe other-job responsibility without a Python graph or selection."""
    if path not in modes:
        return []
    reasons = []

    def add(owner: str, target: str, rule: str, sources: list[str]) -> None:
        sources = sorted(
            {
                source
                for source in sources
                if modes.get(source) in {"100644", "100755"}
            }
        )
        if sources:
            reasons.append(
                {
                    "owner": owner,
                    "target": target,
                    "rule": rule,
                    "sources": sources,
                }
            )

    if path == ".github/CODEOWNERS":
        add("github", path, "native-codeowners-routing", [path])

    if path == "hk.pkl" and modes.get(".github/workflows/ci.yml") in {
        "100644",
        "100755",
    }:
        add(
            "conformance",
            ".github/workflows/ci.yml#conformance",
            "native-hk-configuration",
            [".github/workflows/ci.yml", path],
        )

    if (
        path == "eng/scripts/update_node_mise_lock.py"
        and modes.get("hk.pkl") in {"100644", "100755"}
        and modes.get(".github/workflows/ci.yml") in {"100644", "100755"}
    ):
        add(
            "conformance",
            ".github/workflows/ci.yml#conformance",
            "native-hk-node-mise-authority",
            [".github/workflows/ci.yml", "hk.pkl", path],
        )

    # These finite old readers disappear after this retirement is accepted.
    consumers = _retiring_python_consumers(path)
    if (
        consumers
        and modes.get("pyproject.toml") in {"100644", "100755"}
        and any(
            modes.get(source) in {"100644", "100755"} for source in consumers
        )
    ):
        add(
            "python",
            ".github/workflows/ci.yml#python-tests",
            "retiring-python-consumer",
            ["pyproject.toml", *consumers],
        )
    if (
        path == "renovate.json"
        and modes.get("pyproject.toml") in {"100644", "100755"}
        and modes.get("tests/eng/test_python_runtime.py")
        in {"100644", "100755"}
    ):
        add(
            "python",
            "pytest:tests/eng/test_python_runtime.py",
            "configured-python-runtime-input",
            ["pyproject.toml", "tests/eng/test_python_runtime.py"],
        )

    scopes = _work_scopes(path, work_roots)
    for manifest in manifests:
        directory = str(PurePosixPath(manifest).parent)
        if (
            manifest.endswith(".gemspec")
            and "ruby" in scopes
            and (
                path
                in ALL_INPUTS | {"Gemfile", "Gemfile.lock", ".ruby-version"}
                or _under(path, directory)
            )
        ):
            add(
                "ruby",
                manifest,
                "retained-gem-input",
                [manifest, "Gemfile"],
            )
    for scope, target, sources in (
        (
            "mise",
            ".github/workflows/ci.yml#mise-lock",
            ["mise.toml", path],
        ),
        (
            "azureauth",
            AZURE,
            ["dirs.proj", AZURE + "/python/pyproject.toml"],
        ),
        (
            "scholarly",
            SCHOLARLY,
            [SCHOLARLY + "/pyproject.toml", "apm.yml"],
        ),
    ):
        if scope in scopes:
            add(scope, target, "retained-special-job-input", sources)
    # This only tells another ecosystem that a coordinate has a Python consumer.
    # Linux ignores Python-only reasons and computes its own native scope.
    python_manifests = [
        manifest
        for manifest in manifests
        if manifest.endswith("/pyproject.toml")
        and _under(path, str(PurePosixPath(manifest).parent))
    ]
    if python_manifests or (
        (
            path.endswith(".py")
            and path
            not in {
                "eng/scripts/check_repository_records.py",
                "eng/scripts/update_node_mise_lock.py",
            }
        )
        or path in ALL_INPUTS | PYTHON_INPUTS
        or _under(path, "tests/eng")
        or _under(path, "src/private/app/workflow-delivery")
        or (
            _under(path, "tests/private/app/workflow-delivery")
            and not _under(
                path, "tests/private/app/workflow-delivery/fixtures/products"
            )
        )
        or _under(
            path,
            "tests/private/app/workflow-delivery/fixtures/products/"
            "hcoona-release-smoke-ruby",
        )
        or path
        in {
            ".github/workflows/workflow-delivery-v3-python-smoke.yml",
            ".github/workflows/workflow-delivery-v3-ruby-smoke.yml",
        }
        or _azure_input(path)
        or _legacy_release_input(path)
        or path == ".typos.toml"
    ):
        add(
            "python",
            ".github/workflows/ci.yml#python-tests",
            "native-python-caller-responsibility",
            ["pyproject.toml", *python_manifests],
        )
    if path in native["inputs"]:
        add(
            "dotnet",
            native["project"],
            "native-embedded-resource",
            [native["project"]],
        )
    for binding in bindings:
        if matches(path, binding["path"]):
            add(
                "record-system",
                path,
                "record-binding:" + binding["id"],
                [FAMILY_CATALOG],
            )
    return sorted(
        reasons,
        key=lambda reason: (reason["owner"], reason["target"], reason["rule"]),
    )


def endpoint_owners(
    root: Path,
    paths: tuple[str, ...],
    *,
    base: str,
    candidate: str,
    control_inputs: dict,
) -> dict:
    """Project retained responsibility; PNPM supplies native Node ownership."""
    endpoints = {}
    for name, revision in (("basis", base), ("candidate", candidate)):
        modes = _entry_modes(root, revision)
        files = set(modes)
        work_roots = _work_roots(files)
        manifests = tuple(
            sorted(
                path
                for path in files
                if path.endswith(
                    (
                        ".csproj",
                        ".fsproj",
                        ".vbproj",
                        ".gemspec",
                        "/pyproject.toml",
                    )
                )
            )
        )
        bindings = _record_owner_bindings(root, revision, modes)
        rows = [
            {
                "path": path,
                "present": path in modes,
                "mode": modes.get(path),
                "reasons": _path_owner_reasons(
                    path,
                    modes=modes,
                    work_roots=work_roots,
                    manifests=manifests,
                    bindings=bindings,
                    native=control_inputs[name],
                ),
            }
            for path in paths
        ]
        endpoints[name] = {"revision": revision, "paths": rows}
    return endpoints


def owner_present(root: Path, revision: str) -> bool:
    """Distinguish confirmed native owner absence from unavailable inventory."""
    entry = git(root, "ls-tree", "-z", revision, "--", CONTROL_PROJECT)
    if not entry:
        return False
    metadata, path = entry.removesuffix("\0").split("\t", 1)
    mode, kind, _ = metadata.split(" ")
    if (
        path != CONTROL_PROJECT
        or kind != "blob"
        or mode not in {"100644", "100755"}
    ):
        message = "The control test owner must be a committed regular project"
        raise ValueError(message)
    return True


def read_control_inputs(
    root: Path, source: Path, *, base: str, candidate: str, full: bool
) -> dict:
    """Validate the receiving-process contract before final scope output."""
    response = json.loads(
        source.read_text(encoding="utf-8"), object_pairs_hook=_unique_object
    )
    if not isinstance(response, dict) or set(response) != {
        "comparison",
        "basis",
        "candidate",
    }:
        message = "Missing or malformed native control input response"
        raise ValueError(message)
    expected = {"basis": base, "candidate": candidate, "full": full}
    comparison = response["comparison"]
    if (
        not isinstance(comparison, dict)
        or type(comparison.get("full")) is not bool
        or comparison != expected
        or (full and base != candidate)
    ):
        message = "Control inputs disagree with the exact Git comparison"
        raise ValueError(message)
    for name, revision in (("basis", base), ("candidate", candidate)):
        _validate_control_endpoint(root, response[name], revision, name)
    return response


def _validate_control_endpoint(
    root: Path, endpoint: dict, revision: str, name: str
) -> None:
    if (
        not isinstance(endpoint, dict)
        or set(endpoint)
        != {"revision", "project", "present", "dimension", "inputs"}
        or endpoint["revision"] != revision
        or endpoint["project"] != CONTROL_PROJECT
        or type(endpoint["present"]) is not bool
        or not isinstance(endpoint["inputs"], list)
    ):
        message = "Malformed native control endpoint identity"
        raise ValueError(message)
    present = owner_present(root, revision)
    if endpoint["present"] != present or (name == "candidate" and not present):
        message = "Control project presence disagrees with the native inventory"
        raise ValueError(message)
    dimension = endpoint["dimension"]
    if not present:
        if dimension is not None or endpoint["inputs"]:
            message = "An absent control owner cannot return resource facts"
            raise ValueError(message)
        return
    if (
        not isinstance(dimension, dict)
        or set(dimension)
        != {"configuration", "targetFramework", "runtimeIdentifier"}
        or dimension["configuration"] != "Debug"
        or not isinstance(dimension["targetFramework"], str)
        or not dimension["targetFramework"].strip()
        or not isinstance(dimension["runtimeIdentifier"], str)
    ):
        message = "Unsupported native control resource dimension"
        raise ValueError(message)
    for path in endpoint["inputs"]:
        if (
            not isinstance(path, str)
            or not path
            or path.startswith("/")
            or "\\" in path
            or any(part in {"", ".", ".."} for part in path.split("/"))
        ):
            message = "Noncanonical native resource input coordinate"
            raise ValueError(message)


def _complete_dotnet_scope(
    root: Path, result: dict, options: argparse.Namespace
) -> dict:
    """Join native facts before final applicability."""
    if not options.dotnet_group_request:
        return result
    from run_dotnet_ci_group import finish  # noqa: PLC0415

    return finish(
        root,
        result,
        options.dotnet_group_request.resolve(),
        options.application.resolve(),
    )


def main() -> int:
    """Emit explicit applicability only after successful candidate selection."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--from-ref")
    parser.add_argument("--to-ref", default="HEAD")
    parser.add_argument("--full", action="store_true")
    parser.add_argument("--control-inputs", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--dotnet-group-request", type=Path)
    parser.add_argument("--application", type=Path)
    options = parser.parse_args()
    if options.dotnet_group_request and options.application is None:
        parser.error("native .NET group requires --application")
    root = options.repository.resolve()
    candidate = git(
        root,
        "rev-parse",
        "--verify",
        "--end-of-options",
        f"{options.to_ref}^{{commit}}",
    ).strip()
    if candidate != git(root, "rev-parse", "HEAD").strip():
        parser.error("comparison target must be the checked-out candidate")
    if options.full:
        base, paths = candidate, ()
    else:
        if not options.from_ref:
            parser.error("provide --from-ref or explicitly request --full")
        base = git(
            root,
            "rev-parse",
            "--verify",
            "--end-of-options",
            f"{options.from_ref}^{{commit}}",
        ).strip()
        paths = changed_paths(root, base, candidate)
    control_inputs = read_control_inputs(
        root,
        options.control_inputs,
        base=base,
        candidate=candidate,
        full=options.full,
    )
    result = {
        "base": base,
        "candidate": candidate,
        "full": options.full,
        "changed_paths": paths,
        "endpoint_owners": endpoint_owners(
            root,
            paths,
            base=base,
            candidate=candidate,
            control_inputs=control_inputs,
        ),
        **select(
            root,
            paths,
            base=base,
            full=options.full,
            control_inputs=control_inputs,
        ),
    }
    result = _complete_dotnet_scope(root, result, options)
    rendered = json.dumps(result, indent=2) + "\n"
    print(rendered, end="")
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(rendered, encoding="utf-8")
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            for scope, selected in result["scopes"].items():
                stream.write(f"{scope}={str(selected).lower()}\n")
            stream.write(
                "nuget_reproducibility="
                + str(result["nuget_reproducibility"]).lower()
                + "\n"
            )
            stream.write(
                f"base={base}\ncandidate={candidate}\nfull={str(options.full).lower()}\n"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
