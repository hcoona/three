# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "jsonschema==4.25.1",
#   "PyYAML==6.0.2",
# ]
# ///

"""Validate scholarly-publication source and deployed package state."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import stat
import sys
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING, NamedTuple, Never, cast

import yaml
from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError

if TYPE_CHECKING:
    import os
    from collections.abc import Sequence

    from yaml.nodes import MappingNode

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
PACKAGE_NAME = "scholarly-publication"
EXPECTED_SKILLS = (
    "scholarly-pdf-reconstruction",
    "scholarly-print-assembly",
    "scholarly-render-qa",
)
RUNTIME_DIRECTORIES = frozenset({"assets", "references", "scripts"})
PLUGIN_FIELDS = frozenset(
    "author category description keywords license name skills tags version".split()  # noqa: E501, SIM905
)
APM_FIELDS = frozenset(
    "author dependencies description devDependencies includes license name scripts targets version".split()  # noqa: E501, SIM905
)
SHARED_FILE_PAIRS = (
    "skills/scholarly-pdf-reconstruction/assets/source-package.schema.json|skills/scholarly-print-assembly/assets/source-package.schema.json",
    "skills/scholarly-pdf-reconstruction/assets/source-blocks.schema.json|skills/scholarly-print-assembly/assets/source-blocks.schema.json",
    "skills/scholarly-pdf-reconstruction/assets/figure-map.schema.json|skills/scholarly-print-assembly/assets/figure-map.schema.json",
    "skills/scholarly-print-assembly/assets/assembly-manifest.schema.json|skills/scholarly-render-qa/assets/assembly-manifest.schema.json",
    "skills/scholarly-print-assembly/assets/publication-profile.json|skills/scholarly-render-qa/assets/publication-profile.json",
)
REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


class ValidationError(ValueError):
    """Report one package contract violation."""


class UniqueKeyLoader(yaml.SafeLoader):
    """Safe YAML loader that rejects duplicate mapping keys."""

    def construct_mapping(
        self,
        node: MappingNode,
        deep: bool = False,  # noqa: FBT001, FBT002
    ) -> dict[object, object]:
        """Construct a mapping after checking every key."""
        self.flatten_mapping(node)
        result: dict[object, object] = {}
        for key_node, value_node in node.value:
            key = self.construct_object(key_node, deep=deep)
            if key in result:
                fail(f"found duplicate YAML mapping key {key!r}")
            result[key] = self.construct_object(value_node, deep=deep)
        return result


class Package(NamedTuple):
    """Descriptor values needed by the package checks."""

    name: str
    version: str
    license: str
    includes: tuple[str, ...]


def fail(message: str) -> Never:
    """Raise one validation failure."""
    raise ValidationError(message)


def _json(path: Path) -> object:
    def reject_constant(value: str) -> Never:
        message = f"non-finite JSON number: {value}"
        raise ValueError(message)

    def finite_float(value: str) -> float:
        result = float(value)
        if not math.isfinite(result):
            message = f"non-finite JSON number: {value}"
            raise ValueError(message)
        return result

    def unique_object(
        pairs: list[tuple[str, object]],
    ) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                message = f"duplicate JSON object key: {key}"
                raise ValueError(message)
            result[key] = value
        return result

    try:
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=unique_object,
            parse_constant=reject_constant,
            parse_float=finite_float,
        )
    except (OSError, UnicodeError, ValueError) as error:
        fail(f"cannot parse JSON {path}: {error}")


def _yaml(path: Path) -> object:
    try:
        loader = UniqueKeyLoader(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError) as error:
        fail(f"cannot read YAML {path}: {error}")
    try:
        return loader.get_single_data()
    except yaml.YAMLError as error:
        fail(f"cannot parse YAML {path}: {error}")
    finally:
        loader.dispose()


def _mapping(value: object, context: str) -> dict[str, object]:
    if not isinstance(value, dict) or not all(
        isinstance(key, str) for key in value
    ):
        fail(f"{context} must be a string-keyed mapping")
    return cast("dict[str, object]", value)


def _strings(value: object, context: str) -> list[str]:
    if (
        not isinstance(value, list)
        or not value
        or not all(isinstance(item, str) and item.strip() for item in value)
    ):
        fail(f"{context} must be a non-empty string list")
    return cast("list[str]", value)


def _check_include(value: str) -> None:
    path = PurePosixPath(value)
    parts = path.parts
    if (
        "\\" in value
        or path.is_absolute()
        or value != path.as_posix()
        or ".." in parts
    ):
        fail(f"apm.yml include is not normalized: {value}")
    if (
        len(parts) < 3  # noqa: PLR2004
        or parts[0] != "skills"
        or parts[1] not in EXPECTED_SKILLS
    ):
        fail(f"apm.yml include is outside the package skills: {value}")
    if any(part.casefold() in {"evals", "tests"} for part in parts[2:]):
        fail(f"apm.yml include cannot contain tests or evals: {value}")
    tail = parts[2:]
    if tail != ("SKILL.md",) and tail[0] not in RUNTIME_DIRECTORIES:
        fail(f"apm.yml include has an unsupported category: {value}")


def load_package() -> Package:
    """Load strict plugin and APM descriptors and require agreement."""
    plugin = _mapping(_json(PACKAGE_ROOT / "plugin.json"), "plugin.json")
    apm = _mapping(_yaml(PACKAGE_ROOT / "apm.yml"), "apm.yml")
    if set(plugin) != PLUGIN_FIELDS or set(apm) != APM_FIELDS:
        fail("package descriptors do not have the required shape")
    author = _mapping(plugin["author"], "plugin.json author")
    if set(author) != {"name"} or plugin["skills"] != "skills/":
        fail("plugin.json author or skills field is not exact")

    shared = {
        "name": plugin["name"],
        "version": plugin["version"],
        "description": plugin["description"],
        "author": author["name"],
        "license": plugin["license"],
    }
    if not all(
        isinstance(value, str) and value.strip() for value in shared.values()
    ):
        fail("shared descriptor metadata must use non-empty strings")
    if shared["name"] != PACKAGE_NAME:
        fail(f"plugin.json name must be {PACKAGE_NAME}")
    for field, expected in shared.items():
        if apm[field] != expected:
            fail(f"plugin.json and apm.yml disagree on {field}")
    if (
        apm["targets"] != ["copilot"]
        or apm["dependencies"] != {"apm": [], "mcp": []}
        or apm["devDependencies"] != {"apm": []}
        or apm["scripts"] != {}
    ):
        fail("apm.yml targets, dependencies, or scripts are not exact")

    includes = _strings(apm["includes"], "apm.yml includes")
    if len(includes) != len(set(includes)):
        fail("apm.yml includes must not contain duplicates")
    for value in includes:
        _check_include(value)
    return Package(
        cast("str", shared["name"]),
        cast("str", shared["version"]),
        cast("str", shared["license"]),
        tuple(includes),
    )


def _stat(path: Path, context: str) -> os.stat_result:
    try:
        info = path.lstat()
    except OSError as error:
        fail(f"cannot inspect {context} {path}: {error}")
    if stat.S_ISLNK(info.st_mode) or (
        getattr(info, "st_file_attributes", 0) & REPARSE_POINT
    ):
        fail(f"{context} contains a symlink or reparse point: {path}")
    return info


def _require(path: Path, kind: str, context: str) -> None:
    info = _stat(path, context)
    valid = (
        stat.S_ISDIR(info.st_mode)
        if kind == "directory"
        else stat.S_ISREG(info.st_mode)
    )
    if not valid:
        fail(f"{context} must be a {kind}: {path}")


def _files(root: Path, base: Path, context: str) -> set[str]:
    result: set[str] = set()
    pending = [root]
    while pending:
        for path in sorted(pending.pop().iterdir()):
            info = _stat(path, context)
            if stat.S_ISDIR(info.st_mode):
                pending.append(path)
            elif stat.S_ISREG(info.st_mode):
                result.add(path.relative_to(base).as_posix())
            else:
                fail(f"{context} contains a non-regular entry: {path}")
    return result


def _runtime_files() -> set[str]:
    skills_root = PACKAGE_ROOT / "skills"
    _require(skills_root, "directory", "canonical skills root")
    skill_roots = sorted(skills_root.iterdir())
    for path in skill_roots:
        _require(path, "directory", "canonical skill")
    if tuple(path.name for path in skill_roots) != EXPECTED_SKILLS:
        fail("skills/ must contain exactly: " + ", ".join(EXPECTED_SKILLS))

    result: set[str] = set()
    for skill in EXPECTED_SKILLS:
        root = skills_root / skill
        names = {path.name for path in root.iterdir()}
        extra = sorted(names - RUNTIME_DIRECTORIES - {"SKILL.md"})
        if extra:
            fail(f"{skill} has unsupported entries: {', '.join(extra)}")
        skill_file = root / "SKILL.md"
        _require(skill_file, "regular file", f"canonical skill {skill}")
        result.add(skill_file.relative_to(PACKAGE_ROOT).as_posix())
        for name in sorted(names & RUNTIME_DIRECTORIES):
            directory = root / name
            _require(directory, "directory", f"canonical skill {skill}")
            result.update(_files(directory, PACKAGE_ROOT, f"canonical {skill}"))
    return result


def validate_canonical(package: Package) -> set[str]:
    """Validate canonical inventory, JSON, schemas, and shared files."""
    runtime_files = _runtime_files()
    missing = sorted(runtime_files - set(package.includes))
    extra = sorted(set(package.includes) - runtime_files)
    if missing or extra:
        fail(
            f"apm.yml include closure differs; missing={missing}, extra={extra}"
        )
    for relative in sorted(runtime_files):
        if not relative.endswith(".json"):
            continue
        document = _json(PACKAGE_ROOT.joinpath(*PurePosixPath(relative).parts))
        if relative.endswith(".schema.json"):
            schema = (
                document
                if isinstance(document, bool)
                else _mapping(document, f"{relative} schema")
            )
            try:
                Draft202012Validator.check_schema(schema)
            except SchemaError as error:
                fail(
                    f"{relative} is not a valid Draft 2020-12 schema: "
                    f"{error.message}"
                )
    for pair in SHARED_FILE_PAIRS:
        left, right = pair.split("|")
        left_path = PACKAGE_ROOT.joinpath(*PurePosixPath(left).parts)
        right_path = PACKAGE_ROOT.joinpath(*PurePosixPath(right).parts)
        if left_path.read_bytes() != right_path.read_bytes():
            fail(f"shared package files differ: {left} != {right}")
    return runtime_files


def find_repository_root() -> Path:
    """Find the repository containing the package."""
    for candidate in PACKAGE_ROOT.parents:
        if (candidate / ".git").exists() and (candidate / "apm.yml").is_file():
            return candidate
    fail("cannot locate repository root for runtime validation")


def _lock_dependency(
    lock: dict[str, object],
    package: Package,
    local_path: str,
) -> dict[str, object]:
    values = lock.get("dependencies")
    if not isinstance(values, list):
        fail("apm.lock.yaml dependencies must be a list")
    repo_url = f"_local/{package.name}"
    candidates = []
    for value in values:
        if not isinstance(value, dict):
            continue
        record = _mapping(value, "apm.lock.yaml dependency")
        if (
            record.get("local_path") == local_path
            or record.get("repo_url") == repo_url
            or record.get("name") == package.name
        ):
            candidates.append(record)
    if len(candidates) != 1:
        fail("apm.lock.yaml must have exactly one scholarly local dependency")
    record = candidates[0]
    expected = {
        "declared_license": package.license,
        "local_path": local_path,
        "name": package.name,
        "package_type": "marketplace_plugin",
        "repo_url": repo_url,
        "source": "local",
        "version": package.version,
    }
    keys = set(expected) | {"deployed_files", "deployed_file_hashes"}
    if set(record) != keys or any(
        record[key] != value for key, value in expected.items()
    ):
        fail("apm.lock.yaml scholarly dependency identity is not exact")
    return record


def _check_deployments(
    lock: dict[str, object],
    paths: set[str],
    hashes: dict[str, str],
    local_path: str,
) -> None:
    values = lock.get("deployments")
    if not isinstance(values, list):
        fail("apm.lock.yaml deployments must be a list")
    relevant = []
    for value in values:
        if not isinstance(value, dict):
            continue
        record = _mapping(value, "apm.lock.yaml deployment")
        owners = record.get("owners")
        owned = record.get("active_owner") == local_path or (
            isinstance(owners, list) and local_path in owners
        )
        if record.get("value") in paths or owned:
            relevant.append(record)
    extras = [
        item.get("value") for item in relevant if item.get("value") not in paths
    ]
    if extras:
        fail(f"apm.lock.yaml has extra scholarly deployments: {extras}")
    for path in sorted(paths):
        matches = [item for item in relevant if item.get("value") == path]
        expected = {
            "kind": "project-relative",
            "target": "copilot",
            "value": path,
            "runtime": None,
            "scope": "project",
            "owners": [local_path],
            "active_owner": local_path,
            "content_hash": hashes.get(path),
        }
        if len(matches) != 1 or matches[0] != expected:
            fail(f"apm.lock.yaml deployment is not exact for {path}")


def validate_runtime(  # noqa: C901
    package: Package,
) -> set[str]:
    """Validate deployed files and all package-owned lock bindings."""
    repository_root = find_repository_root()
    try:
        relative = PACKAGE_ROOT.relative_to(repository_root).as_posix()
    except ValueError:
        fail("canonical package must be beneath the repository root")
    local_path = f"./{relative}"

    root_apm = _mapping(_yaml(repository_root / "apm.yml"), "root apm.yml")
    dependencies = _mapping(
        root_apm.get("dependencies"),
        "root apm.yml dependencies",
    )
    values = dependencies.get("apm")
    if not isinstance(values, list):
        fail("root apm.yml dependencies.apm must be a list")
    registrations = [
        value
        for value in values
        if isinstance(value, dict) and value.get("path") == local_path
    ]
    if registrations != [{"path": local_path}]:
        fail("root apm.yml must register the package exactly once")

    files = {f".agents/{value}" for value in package.includes}
    roots = {f".agents/skills/{skill}" for skill in EXPECTED_SKILLS}
    actual: set[str] = set()
    for skill in EXPECTED_SKILLS:
        root = repository_root / ".agents" / "skills" / skill
        _require(root, "directory", "runtime skill root")
        actual.update(_files(root, repository_root, "runtime deployment"))
    if actual != files:
        fail(
            "runtime deployment file set differs; "
            f"missing={sorted(files - actual)}, extra={sorted(actual - files)}"
        )

    hashes: dict[str, str] = {}
    for include in package.includes:
        canonical = PACKAGE_ROOT.joinpath(*PurePosixPath(include).parts)
        deployed_relative = f".agents/{include}"
        deployed = repository_root.joinpath(
            *PurePosixPath(deployed_relative).parts
        )
        _require(canonical, "regular file", "canonical runtime file")
        content = canonical.read_bytes()
        if content != deployed.read_bytes():
            fail(f"runtime deployment differs from canonical: {include}")
        hashes[deployed_relative] = (
            f"sha256:{hashlib.sha256(content).hexdigest()}"
        )

    lock = _mapping(_yaml(repository_root / "apm.lock.yaml"), "apm.lock.yaml")
    if lock.get("lockfile_version") != "1":
        fail("apm.lock.yaml lockfile_version must be '1'")
    dependency = _lock_dependency(lock, package, local_path)
    deployed_files = _strings(
        dependency["deployed_files"],
        "scholarly deployed_files",
    )
    all_paths = roots | files
    if len(deployed_files) != len(set(deployed_files)) or (
        set(deployed_files) != all_paths
    ):
        fail("apm.lock.yaml scholarly deployed_files set is not exact")
    locked_hashes = _mapping(
        dependency["deployed_file_hashes"],
        "scholarly deployed_file_hashes",
    )
    if set(locked_hashes) != set(hashes):
        fail("apm.lock.yaml scholarly deployed_file_hashes set is not exact")
    wrong = [path for path in hashes if locked_hashes[path] != hashes[path]]
    if wrong:
        fail(f"apm.lock.yaml deployed_file_hashes differ for: {sorted(wrong)}")
    _check_deployments(lock, all_paths, hashes, local_path)
    return actual


def main(argv: Sequence[str] | None = None) -> int:
    """Run the selected validation scopes."""
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--scope",
        choices=("canonical", "runtime", "all"),
        default="all",
    )
    args = parser.parse_args(argv)
    package = load_package()
    canonical_files: set[str] = set()
    deployed_files: set[str] = set()
    if args.scope in {"canonical", "all"}:
        canonical_files = validate_canonical(package)
    if args.scope in {"runtime", "all"}:
        deployed_files = validate_runtime(package)
    print(
        json.dumps(
            {
                "canonical_runtime_files": len(canonical_files),
                "deployed_runtime_files": len(deployed_files),
                "plugin": package.name,
                "runtime_includes": len(package.includes),
                "scope": args.scope,
                "skills": list(EXPECTED_SKILLS),
                "status": "pass",
                "version": package.version,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValidationError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1) from error
