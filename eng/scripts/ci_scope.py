"""Select general-CI work from tested inputs and Python consumers."""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import subprocess
import tomllib
from pathlib import Path

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
    "mise.toml",
    "mise.lock",
}
PYTHON_INPUTS = {
    "pyproject.toml",
    "uv.lock",
    "uv.toml",
    ".python-version",
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
    root: Path, files: set[str], members: list[str], base: str
) -> dict[str, tuple[str, set[str]]]:
    packages = {}
    for path in sorted(files):
        directory = str(Path(path).parent)
        if not path.endswith("/pyproject.toml") or not any(
            fnmatch.fnmatchcase(directory, member) for member in members
        ):
            continue
        source = root / path
        content = (
            source.read_text()
            if source.exists()
            else git(root, "show", f"{base}:{path}")
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
    inherited = Path(path).name in {
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
        or (inherited and _under(directory, str(Path(path).parent)))
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
        str(Path(item).parent)
        for item in files
        if item.endswith("/package.json") and item.startswith("src/")
    }
    dotnet_roots = {
        str(Path(item).parent)
        for item in files
        if item.endswith((".csproj", ".fsproj", ".vbproj"))
    }
    ruby_roots = {
        str(Path(item).parent) for item in files if item.endswith(".gemspec")
    }
    applicability = {
        "mise": Path(path).name in {"mise.toml", "mise.lock"},
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


def select(
    root: Path, paths: tuple[str, ...], *, base: str, full: bool = False
) -> dict:
    """Select Python roots and the existing Node/.NET workspace units."""
    config = tomllib.loads((root / "pyproject.toml").read_text())
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


def main() -> int:
    """Emit explicit applicability only after successful candidate selection."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--from-ref")
    parser.add_argument("--to-ref", default="HEAD")
    parser.add_argument("--full", action="store_true")
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
        base, paths = "", ()
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
    result = {
        "base": base,
        "candidate": candidate,
        "full": options.full,
        "changed_paths": paths,
        **select(root, paths, base=base, full=options.full),
    }
    rendered = json.dumps(result, indent=2) + "\n"
    print(rendered, end="")
    if options.output:
        options.output.parent.mkdir(parents=True, exist_ok=True)
        options.output.write_text(rendered)
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
