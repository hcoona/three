"""Select general-CI work from tested inputs and Python consumers."""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import subprocess
import tomllib
from pathlib import Path, PurePosixPath

from repository_path_patterns import matches, safe_path
from workflow_delivery_v3_hk import changed_paths

ROOT = Path(__file__).resolve().parents[2]
V3 = "src/public/lib/three-workflow-delivery-v3"
AZURE = "src/private/app/azureauth-credprovider"
SCHOLARLY = "src/private/lib/scholarly-publication"
SCOPES = (
    "validation",
    "mise",
    "dotnet",
    "node",
    "python",
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
V3_NATIVE = tuple(
    f"src/private/app/workflow-delivery-v3-{name}/"
    for name in ("dotnet-provider", "nuget-authority", "nuget-consumer")
)


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


def _name(requirement: str) -> str:
    return (
        re.split(r"[\[ ;<>=!~]", requirement, maxsplit=1)[0]
        .lower()
        .replace("_", "-")
    )


def _python_packages(
    root: Path,
    files: set[str],
    members: list[str],
    base: str,
    *,
    revision: str | None = None,
) -> dict[str, tuple[str, set[str]]]:
    packages = {}
    for path in sorted(files):
        directory = str(PurePosixPath(path).parent)
        if not path.endswith("/pyproject.toml") or not any(
            fnmatch.fnmatchcase(directory, member) for member in members
        ):
            continue
        source = root / path
        content = (
            git(root, "show", f"{revision}:{path}")
            if revision is not None
            else (
                source.read_text(encoding="utf-8")
                if source.exists()
                else git(root, "show", f"{base}:{path}")
            )
        )
        manifest = tomllib.loads(content)
        project = manifest["project"]
        dependencies = project.get("dependencies", []) + manifest.get(
            "build-system", {}
        ).get("requires", [])
        for group in project.get("optional-dependencies", {}).values():
            dependencies += group
        packages[_name(project["name"])] = (
            directory,
            {_name(item) for item in dependencies},
        )
    return packages


def _python_consumers(
    path: str, packages: dict[str, tuple[str, set[str]]]
) -> set[str]:
    affected = {
        name
        for name, (directory, _) in packages.items()
        if _under(path, directory)
    }
    if _under(path, V3 + "/docs") or path == V3 + "/README.md":
        affected.discard("three-workflow-delivery-v3")
    while True:
        consumers = {
            name
            for name, (_, dependencies) in packages.items()
            if dependencies & affected
        }
        if consumers <= affected:
            return {
                directory
                for name, (directory, _) in packages.items()
                if name in affected
            }
        affected.update(consumers)


def _dotnet_input(path: str, roots: set[str]) -> bool:
    inherited = PurePosixPath(path).name in {
        ".editorconfig",
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


def _v3_input(path: str) -> bool:
    return (
        _dotnet_input(
            path,
            {
                *V3_NATIVE,
                "tests/private/app/workflow-delivery-v3-nuget-authority",
                "tests/private/app/workflow-delivery-v3-nuget-consumer",
                "src/public/lib/hcoona-release-smoke-github-packages",
            },
        )
        or path in NODE_INPUTS
        or path
        in {
            "hk.pkl",
            ".gitignore",
            ".gitattributes",
            ".github/CODEOWNERS",
            "eng/scripts/hk_exec.py",
            "eng/scripts/hk_file_operands.py",
            "eng/scripts/hk_actionlint.py",
            "eng/scripts/hk_pkl_eval.py",
        }
        or path.startswith(
            (
                ".github/workflows/workflow-delivery-v3-",
                ".github/actions/workflow-delivery-v3",
                ".github/workflow-delivery/",
                "eng/workflow-delivery/v3/",
                "eng/scripts/workflow_delivery_v3_",
                "src/private/lib/hk/",
                "tests/private/app/workflow-delivery-v3-",
                "src/public/lib/hcoona-release-smoke-",
            )
        )
    )


def _work_scopes(path: str, files: set[str]) -> set[str]:
    if path in ALL_INPUTS:
        return set(SCOPES)
    node_roots = {
        str(PurePosixPath(item).parent)
        for item in files
        if item.endswith("/package.json") and item.startswith("src/")
    }
    dotnet_roots = {
        str(PurePosixPath(item).parent)
        for item in files
        if item.endswith((".csproj", ".fsproj", ".vbproj"))
    }
    ruby_roots = {
        str(PurePosixPath(item).parent)
        for item in files
        if item.endswith(".gemspec")
    }
    applicability = {
        "mise": PurePosixPath(path).name in {"mise.toml", "mise.lock"},
        "dotnet": _dotnet_input(path, dotnet_roots),
        "node": path in NODE_INPUTS
        or any(_under(path, directory) for directory in node_roots),
        "azureauth": _azure_input(path),
        "ruby": path in {"Gemfile", "Gemfile.lock", ".ruby-version"}
        or any(_under(path, directory) for directory in ruby_roots),
        "scholarly": _under(path, SCHOLARLY)
        or path.startswith(".agents/skills/scholarly-")
        or path in {"apm.yml", "apm.lock.yaml"},
    }
    return {scope for scope, selected in applicability.items() if selected}


def _python_tests(
    path: str, test_roots: list[str], packages: dict[str, tuple[str, set[str]]]
) -> set[str]:
    if path in ALL_INPUTS | PYTHON_INPUTS:
        return set(test_roots)
    affected = _python_consumers(path, packages)
    return {
        test
        for test in test_roots
        if _under(path, test)
        or any(_under(test, directory) for directory in affected)
        or (test.startswith(V3 + "/") and _v3_input(path))
        or (test.startswith(AZURE + "/") and _azure_input(path))
        or (
            test == "tests/eng/test_legacy_release_contract.py"
            and _legacy_release_input(path)
        )
        or (test == "tests/eng/test_typos_config.py" and path == ".typos.toml")
        or (
            test.startswith("src/public/lib/nbgv-python/")
            and path in DOTNET_INPUTS
        )
        or (
            test.startswith("tests/eng/")
            and path
            in {
                "eng/scripts/ci_scope.py",
                "eng/scripts/sync_python_version.py",
                "eng/scripts/workflow_delivery_v3_hk.py",
            }
        )
    }


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


def _test_roots(config: dict) -> list[str]:
    test_roots = config["tool"]["pytest"]["ini_options"]["testpaths"]
    if (
        not isinstance(test_roots, list)
        or not test_roots
        or any(
            not isinstance(test, str) or not test.strip() for test in test_roots
        )
    ):
        message = "Python testpaths must be a nonempty list of explicit paths"
        raise ValueError(message)
    return test_roots


def select(
    root: Path,
    paths: tuple[str, ...],
    *,
    base: str,
    full: bool = False,
    control_inputs: dict | None = None,
) -> dict:
    """Select Python roots and the existing Node/.NET workspace units."""
    config = tomllib.loads(
        (root / "pyproject.toml").read_text(encoding="utf-8")
    )
    test_roots = _test_roots(config)
    files = set(
        git(root, "ls-tree", "-r", "--name-only", "-z", "HEAD").split("\0")
    )
    if base:
        files.update(
            git(root, "ls-tree", "-r", "--name-only", "-z", base).split("\0")
        )
    packages = _python_packages(
        root, files, config["tool"]["uv"]["workspace"]["members"], base
    )
    reasons = {scope: set() for scope in SCOPES}
    reasons["validation"].add("source conformance")
    python_reasons = {test: set() for test in test_roots}
    for path in paths:
        for scope in _work_scopes(path, files):
            reasons[scope].add(path)
        for test in _python_tests(path, test_roots, packages):
            python_reasons[test].add(path)
        reasons["dotnet"].update(_resource_reasons(path, control_inputs))
    if full:
        for why in (*reasons.values(), *python_reasons.values()):
            why.add("explicit full validation")
    selected = [test for test, why in python_reasons.items() if why]
    reasons["python"].update(selected)
    selected_packages = {
        name: dependencies
        for name, (directory, dependencies) in packages.items()
        if (root / directory / "pyproject.toml").is_file()
        and any(_under(test, directory) for test in selected)
    }
    v3 = any(_under(test, V3) for test in selected)
    return {
        "scopes": {scope: bool(why) for scope, why in reasons.items()},
        "python_roots": selected,
        "python_packages": (
            [config["project"]["name"], *sorted(selected_packages)]
            if selected
            else []
        ),
        "python_v3": v3,
        "python_dotnet": v3
        or any("nbgv-python" in deps for deps in selected_packages.values()),
        "reasons": {scope: sorted(why) for scope, why in reasons.items()},
        "python_reasons": {
            test: sorted(why) for test, why in python_reasons.items() if why
        },
    }


def _resource_reasons(path: str, response: dict | None) -> set[str]:
    if response is None:
        return set()
    return {
        f"{path} -> {endpoint['project']} ({endpoint['revision']})"
        for endpoint in (response["basis"], response["candidate"])
        if path in endpoint["inputs"]
    }


def _unique_object(pairs: list[tuple[str, object]]) -> dict:
    result: dict = {}
    for name, value in pairs:
        if name in result:
            message = f"Duplicate control input field: {name}"
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


def _record_owner_bindings(
    root: Path, revision: str, modes: dict[str, str]
) -> list[dict]:
    if FAMILY_CATALOG not in modes:
        return []
    # Bootstrap callers use only standard-library selectors.
    import yaml  # noqa: PLC0415 - Only endpoint record parsing needs locked PyYAML.

    class _OwnerCatalogLoader(yaml.SafeLoader):
        """Reject duplicate mapping keys with the locked safe parser."""

    def mapping(loader: yaml.SafeLoader, node: yaml.MappingNode) -> dict:
        loader.flatten_mapping(node)
        return _unique_object(loader.construct_pairs(node, deep=True))

    _OwnerCatalogLoader.add_constructor(
        yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
        mapping,
    )
    catalog = yaml.load(
        _endpoint_text(root, revision, modes, FAMILY_CATALOG),
        Loader=_OwnerCatalogLoader,  # noqa: S506 - Subclass adds duplicate-key checks to SafeLoader.
    )
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
            or not safe_path(binding["path"], pattern=True)
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


def _path_owner_reasons(  # noqa: C901, PLR0913 - Finite roles share explicit endpoint facts.
    path: str,
    *,
    modes: dict[str, str],
    packages: dict[str, tuple[str, set[str]]],
    test_roots: list[str],
    bindings: list[dict],
    native: dict,
) -> list[dict]:
    """Reuse retained selector results without claiming complete consumption."""
    if path not in modes:
        return []
    reasons = []
    files = set(modes)

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

    scopes = _work_scopes(path, files)
    for manifest in sorted(files):
        directory = str(PurePosixPath(manifest).parent)
        if (
            manifest.endswith((".csproj", ".fsproj", ".vbproj"))
            and "dotnet" in scopes
            and (path in ALL_INPUTS or _dotnet_input(path, {directory}))
        ):
            add(
                "dotnet",
                manifest,
                "retained-project-input",
                [manifest, "dirs.proj"],
            )
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
    for test in sorted(_python_tests(path, test_roots, packages)):
        sources = [
            "pyproject.toml",
            *[
                directory + "/pyproject.toml"
                for directory, _ in packages.values()
                if _under(test, directory)
            ],
        ]
        add("python", test, "retained-python-test-input", sources)
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
        config = tomllib.loads(
            _endpoint_text(root, revision, modes, "pyproject.toml")
        )
        test_roots = _test_roots(config)
        members = config["tool"]["uv"]["workspace"]["members"]
        if (
            any(not safe_path(item) for item in test_roots)
            or not isinstance(members, list)
            or any(
                not isinstance(item, str) or not safe_path(item, pattern=True)
                for item in members
            )
        ):
            message = "Malformed endpoint Python workspace/test configuration"
            raise ValueError(message)
        files = set(modes)
        packages = _python_packages(
            root, files, members, revision, revision=revision
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
                    packages=packages,
                    test_roots=test_roots,
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


def main() -> int:
    """Emit explicit applicability only after successful candidate selection."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--from-ref")
    parser.add_argument("--to-ref", default="HEAD")
    parser.add_argument("--full", action="store_true")
    parser.add_argument("--control-inputs", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args()
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
    rendered = json.dumps(result, indent=2) + "\n"
    print(rendered, end="")
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(rendered, encoding="utf-8")
    if output := os.environ.get("GITHUB_OUTPUT"):
        with Path(output).open("a", encoding="utf-8") as stream:
            for scope, selected in result["scopes"].items():
                stream.write(f"{scope}={str(selected).lower()}\n")
            for name in ("python_roots", "python_packages"):
                stream.write(f"{name}={json.dumps(result[name])}\n")
            for name in ("python_v3", "python_dotnet"):
                stream.write(f"{name}={str(result[name]).lower()}\n")
            stream.write(
                f"base={base}\ncandidate={candidate}\nfull={str(options.full).lower()}\n"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
