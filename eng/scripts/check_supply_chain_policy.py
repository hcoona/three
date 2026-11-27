# /// script
# requires-python = ">=3.13"
# dependencies = ["packaging==26.3", "PyYAML==6.0.4"]
# ///
"""Enforce repository release-age cooldown and public-library floor policies."""

from __future__ import annotations

import argparse
import configparser
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
from collections.abc import Callable, Iterator
from pathlib import Path
from xml.etree import ElementTree as ET

import yaml
from packaging.requirements import InvalidRequirement, Requirement
from packaging.specifiers import InvalidSpecifier, SpecifierSet
from packaging.version import Version

ROOT = Path(__file__).resolve().parents[2]
THREE_DAYS_MINUTES = 4320
MIN_BUNDLER_COOLDOWN = Version("4.0.20")
SEMVER_RE = re.compile(
    r"^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)"
    r"(?:-(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)"
    r"(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*)?"
    r"(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$",
)
ENGINE_VERSION_RE = re.compile(
    r"^(?:0|[1-9]\d*)(?:\.(?:0|[1-9]\d*)){0,2}$",
)
RUBY_CONTEXT_PREFIX = (
    "tests/private/app/workflow-delivery/Native/Ruby/contexts/"
)
NPM_EXACT_FLOOR_EXCEPTIONS = {
    (
        "src/public/lib/hexo-renderer-asciidoc/package.json",
        "dependencies",
        "@asciidoctor/core",
    ): "4.3.0",
}
ALLOWED_RENOVATE_EXEMPTIONS = (
    {
        "matchManagers": ["github-actions"],
        "matchUpdateTypes": ["digest", "pin", "pinDigest"],
        "minimumReleaseAge": None,
        "automerge": True,
    },
    {
        "matchManagers": ["custom.regex"],
        "matchDepNames": ["nodejs/Release"],
        "minimumReleaseAge": None,
        "automerge": True,
    },
    {
        "matchManagers": ["devcontainer"],
        "matchDatasources": ["docker"],
        "minimumReleaseAgeBehaviour": "timestamp-optional",
        "automerge": True,
    },
)
DEV_DEP_TYPES = frozenset(
    {
        "devDependencies",
        "dependency-groups",
        "development",
        "test",
    },
)

RUBY_INSPECTOR = """\
require "json"
require "rubygems"
require "rubygems/config_file"
require "bundler"

Gem.configuration = Gem::ConfigFile.new(["--norc"])
request = JSON.parse(File.read(ARGV.fetch(0)))
lockfiles = request.fetch("lockfiles").to_h do |rel, path|
  parsed = Bundler::LockfileParser.new(File.read(path))
  [rel, parsed.bundler_version&.to_s]
end
gemspecs = request.fetch("gemspecs").to_h do |rel, path|
  spec = Gem::Specification.load(path)
  raise "Failed to load #{rel}" unless spec
  ruby_reqs = spec.required_ruby_version.requirements.map do |op, ver|
    [op, ver.to_s]
  end
  deps = spec.runtime_dependencies.map do |dep|
    {
      "name" => dep.name,
      "requirements" => dep.requirement.requirements.map do |op, ver|
        [op, ver.to_s]
      end
    }
  end
  [rel, {
    "required_ruby_version" => ruby_reqs,
    "runtime_dependencies" => deps
  }]
end
puts JSON.generate({ "lockfiles" => lockfiles, "gemspecs" => gemspecs })
"""


def _rel(root: Path, path: Path) -> str:
    return path.relative_to(root).as_posix()


IGNORED_DIRS = frozenset(
    {".git", ".venv", ".bundle", "node_modules", "bin", "obj", "artifacts"},
)


def _walk_owned_files(base: Path) -> Iterator[Path]:
    if not base.is_dir():
        return
    for current, dirs, files in os.walk(base):
        current_path = Path(current)
        is_vendor = current_path.name == "vendor"
        dirs[:] = sorted(
            d
            for d in dirs
            if d not in IGNORED_DIRS and not (is_vendor and d == "bundle")
        )
        for filename in sorted(files):
            yield current_path / filename


def _discover_files(root: Path, name: str) -> list[Path]:
    return sorted(path for path in _walk_owned_files(root) if path.name == name)


def _discover_suffix(base: Path, suffix: str) -> list[Path]:
    return sorted(
        path for path in _walk_owned_files(base) if path.name.endswith(suffix)
    )


def _check_mise(root: Path, errors: list[str]) -> None:
    path = root / "mise.toml"
    if not path.is_file():
        errors.append("mise.toml is missing.")
        return
    data = tomllib.loads(path.read_text(encoding="utf-8"))
    actual = data.get("settings", {}).get("minimum_release_age")
    if actual != "3d":
        errors.append(
            "mise.toml: settings.minimum_release_age must be '3d', "
            f"got {actual!r}.",
        )


def _check_pnpm_workspaces(root: Path, errors: list[str]) -> None:
    workspaces = [
        path
        for path in _discover_files(root, "pnpm-workspace.yaml")
        if not _rel(root, path).startswith("tests/")
    ]
    if not workspaces:
        errors.append("pnpm-workspace.yaml is missing.")
        return
    for path in workspaces:
        rel = _rel(root, path)
        data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
        actual = data.get("minimumReleaseAge")
        if type(actual) is not int or actual != THREE_DAYS_MINUTES:
            errors.append(
                f"{rel}: minimumReleaseAge must be {THREE_DAYS_MINUTES}, "
                f"got {actual!r}.",
            )
        if "minimumReleaseAgeExclude" in data:
            errors.append(f"{rel}: minimumReleaseAgeExclude is not permitted.")


def _check_renovate_cli_npmrc(root: Path, errors: list[str]) -> None:
    path = root / "eng/renovate/.npmrc"
    if not path.is_file():
        errors.append("eng/renovate/.npmrc is missing.")
        return
    parser = configparser.ConfigParser(interpolation=None)
    parser.read_string("[npmrc]\n" + path.read_text(encoding="utf-8"))
    section = parser["npmrc"]
    actual = section.get("min-release-age")
    if actual != "3":
        errors.append(
            "eng/renovate/.npmrc: min-release-age must be '3', "
            f"got {actual!r}.",
        )
    if any(key.startswith("min-release-age-exclude") for key in section):
        errors.append(
            "eng/renovate/.npmrc: min-release-age-exclude is not permitted.",
        )


def _check_uv(root: Path, errors: list[str]) -> None:
    pyproject = root / "pyproject.toml"
    uv_lock = root / "uv.lock"
    if not pyproject.is_file() or not uv_lock.is_file():
        errors.append("pyproject.toml and uv.lock must both exist.")
        return
    uv_cfg = (
        tomllib.loads(pyproject.read_text(encoding="utf-8"))
        .get("tool", {})
        .get("uv", {})
    )
    if uv_cfg.get("exclude-newer") != "3 days":
        errors.append(
            "pyproject.toml: tool.uv.exclude-newer must be '3 days', "
            f"got {uv_cfg.get('exclude-newer')!r}.",
        )
    if "exclude-newer-package" in uv_cfg:
        errors.append(
            "pyproject.toml: tool.uv.exclude-newer-package is not permitted.",
        )
    lock_opts = tomllib.loads(uv_lock.read_text(encoding="utf-8")).get(
        "options",
        {},
    )
    if lock_opts.get("exclude-newer-span") != "P3D":
        errors.append(
            "uv.lock: options.exclude-newer-span must be 'P3D', "
            f"got {lock_opts.get('exclude-newer-span')!r}.",
        )


def _strip_description(rule: dict[str, object]) -> dict[str, object]:
    return {key: value for key, value in rule.items() if key != "description"}


def _exemption_projection(rule: dict[str, object]) -> dict[str, object]:
    keep = {
        "matchManagers",
        "matchDatasources",
        "matchPackageNames",
        "matchDepNames",
        "matchFileNames",
        "matchUpdateTypes",
        "minimumReleaseAge",
        "minimumReleaseAgeBehaviour",
        "automerge",
    }
    return {key: value for key, value in rule.items() if key in keep}


def _check_renovate_release_age(
    data: dict[str, object],
    rules: list[dict[str, object]],
    errors: list[str],
) -> None:
    if data.get("minimumReleaseAge") != "3 days":
        errors.append(
            "renovate.json: top-level minimumReleaseAge must be '3 days', "
            f"got {data.get('minimumReleaseAge')!r}.",
        )
    if "minimumReleaseAgeBehaviour" in data:
        errors.append(
            "renovate.json: top-level minimumReleaseAgeBehaviour is forbidden.",
        )
    exemption_rules = [
        _exemption_projection(rule)
        for rule in rules
        if "minimumReleaseAge" in rule or "minimumReleaseAgeBehaviour" in rule
    ]
    for projected in exemption_rules:
        if projected not in ALLOWED_RENOVATE_EXEMPTIONS:
            errors.append(
                "renovate.json: unapproved release-age exemption rule "
                f"{projected!r}.",
            )
    for expected in ALLOWED_RENOVATE_EXEMPTIONS:
        if expected not in exemption_rules:
            errors.append(
                "renovate.json: missing expected timestamp-less datasource "
                f"rule {expected!r}.",
            )


REQUIRED_PUBLIC_FLOOR_RULES: tuple[dict[str, object], ...] = (
    {
        "matchManagers": ["pep621"],
        "matchFileNames": ["src/public/lib/**/pyproject.toml"],
        "matchDepTypes": [
            "project.dependencies",
            "project.optional-dependencies",
            "requires-python",
        ],
        "enabled": False,
    },
    {
        "matchManagers": ["npm"],
        "matchFileNames": [
            "src/public/lib/**/package.json",
            "!src/public/lib/**/examples/**",
        ],
        "matchDepTypes": [
            "dependencies",
            "optionalDependencies",
            "peerDependencies",
            "engines",
        ],
        "enabled": False,
    },
    {
        "matchManagers": ["bundler"],
        "matchFileNames": ["src/public/lib/**"],
        "matchUpdateTypes": ["!lockFileMaintenance"],
        "enabled": False,
    },
    {
        "matchManagers": ["gemspec"],
        "matchFileNames": ["src/public/lib/**"],
        "matchDepTypes": ["runtime", "required_ruby_version"],
        "enabled": False,
    },
    {
        "matchManagers": ["nuget"],
        "matchFileNames": ["src/public/lib/Directory.Packages.props"],
        "matchDepTypes": ["nuget"],
        "enabled": False,
    },
)


def _targets_public_lib(rule: dict[str, object]) -> bool:
    file_names = rule.get("matchFileNames")
    if not isinstance(file_names, list):
        return False
    return any(
        isinstance(entry, str)
        and entry.startswith("src/public/lib")
        and "/examples/" not in entry
        for entry in file_names
    )


def _is_dev_only_rule(rule: dict[str, object]) -> bool:
    dep_types = rule.get("matchDepTypes")
    return (
        isinstance(dep_types, list)
        and bool(dep_types)
        and (set(dep_types) <= DEV_DEP_TYPES)
    )


def _check_renovate_public_floors(
    rules: list[dict[str, object]],
    errors: list[str],
) -> None:
    stripped_rules = [_strip_description(rule) for rule in rules]
    floor_indices: list[int] = []
    for expected in REQUIRED_PUBLIC_FLOOR_RULES:
        idx = next(
            (
                pos
                for pos, candidate in enumerate(stripped_rules)
                if candidate == expected
            ),
            None,
        )
        if idx is None:
            errors.append(
                "renovate.json: missing unnarrowed enabled=false "
                f"public-library floor rule {expected!r}.",
            )
        else:
            floor_indices.append(idx)
    if floor_indices:
        first_floor_idx = min(floor_indices)
        for later_rule in rules[first_floor_idx + 1 :]:
            if later_rule.get("enabled") is True and not _is_dev_only_rule(
                later_rule,
            ):
                stripped = _strip_description(later_rule)
                errors.append(
                    "renovate.json: enabled=true rule after public-library "
                    "floor protection must target only dev/test depTypes, "
                    f"got {stripped!r}.",
                )
    for rule in rules:
        if not _targets_public_lib(rule):
            continue
        is_dev_only = _is_dev_only_rule(rule)
        if rule.get("enabled") is True and not is_dev_only:
            stripped = _strip_description(rule)
            errors.append(
                "renovate.json: enabled=true under src/public/lib is only "
                f"allowed for dev/test depTypes, got {stripped!r}.",
            )
        if (
            rule.get("rangeStrategy") in {"bump", "update-lockfile"}
            and not is_dev_only
        ):
            errors.append(
                "renovate.json: rangeStrategy bump/update-lockfile is "
                "forbidden on public-library floors: "
                f"{_strip_description(rule)!r}.",
            )


def _check_renovate(root: Path, errors: list[str]) -> None:
    path = root / "renovate.json"
    if not path.is_file():
        errors.append("renovate.json is missing.")
        return
    data = json.loads(path.read_text(encoding="utf-8"))
    raw_rules = data.get("packageRules")
    if not isinstance(raw_rules, list):
        errors.append("renovate.json: packageRules must be a list.")
        return
    rules = [rule for rule in raw_rules if isinstance(rule, dict)]
    _check_renovate_release_age(data, rules, errors)
    _check_renovate_public_floors(rules, errors)


def _check_pep440_floor(
    spec_text: str,
    label: str,
    errors: list[str],
) -> None:
    try:
        specifiers = SpecifierSet(spec_text)
    except InvalidSpecifier:
        errors.append(f"{label}: invalid PEP 440 specifier {spec_text!r}.")
        return
    ops = {spec.operator for spec in specifiers}
    if ">=" not in ops or ops & {"==", "===", "~=", "<", "<=", "!="}:
        errors.append(
            f"{label}: expected an open-ended '>=' minimum floor, "
            f"got {spec_text!r}.",
        )


def _check_public_python_floors(root: Path, errors: list[str]) -> None:
    public_lib = root / "src/public/lib"
    if not public_lib.is_dir():
        return
    for path in _discover_files(public_lib, "pyproject.toml"):
        rel = _rel(root, path)
        project = tomllib.loads(path.read_text(encoding="utf-8")).get("project")
        if not isinstance(project, dict):
            continue
        requires_python = project.get("requires-python", "")
        _check_pep440_floor(
            str(requires_python),
            f"{rel} project.requires-python",
            errors,
        )
        for dep in project.get("dependencies", []):
            _check_python_requirement(
                str(dep),
                f"{rel} project.dependencies",
                errors,
            )
        for group, deps in project.get("optional-dependencies", {}).items():
            for dep in deps:
                _check_python_requirement(
                    str(dep),
                    f"{rel} project.optional-dependencies.{group}",
                    errors,
                )


def _check_python_requirement(
    dep_text: str,
    label: str,
    errors: list[str],
) -> None:
    try:
        req = Requirement(dep_text)
    except InvalidRequirement:
        errors.append(f"{label}: invalid PEP 508 requirement {dep_text!r}.")
        return
    _check_pep440_floor(str(req.specifier), f"{label} ({req.name})", errors)


def _is_valid_npm_floor(
    spec_text: str,
    *,
    allow_partial: bool = False,
) -> bool:
    for prefix in ("^", ">="):
        if spec_text.startswith(prefix):
            remainder = spec_text[len(prefix) :].strip()
            if SEMVER_RE.fullmatch(remainder):
                return True
            return bool(
                allow_partial and ENGINE_VERSION_RE.fullmatch(remainder),
            )
    return False


def _check_npm_section(
    rel: str,
    section: str,
    entries: dict[str, object],
    errors: list[str],
) -> None:
    for name, spec in entries.items():
        key = (rel, section, name)
        if key in NPM_EXACT_FLOOR_EXCEPTIONS:
            if not isinstance(spec, str) or not _is_exact_version(spec):
                errors.append(
                    f"{rel} {section}.{name}: documented exact-pin "
                    f"exception must stay exact, got {spec!r}.",
                )
            continue
        if not isinstance(spec, str) or not _is_valid_npm_floor(spec):
            errors.append(
                f"{rel} {section}.{name}: expected '^' or '>=' "
                f"minimum floor, got {spec!r}.",
            )


def _check_public_npm_floors(root: Path, errors: list[str]) -> None:
    public_lib = root / "src/public/lib"
    if not public_lib.is_dir():
        return
    for path in _discover_files(public_lib, "package.json"):
        rel = _rel(root, path)
        if "/examples/" in rel:
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        for engine, spec in (data.get("engines") or {}).items():
            if not isinstance(spec, str) or not _is_valid_npm_floor(
                spec,
                allow_partial=True,
            ):
                errors.append(
                    f"{rel} engines.{engine}: expected '>=' or '^' floor, "
                    f"got {spec!r}.",
                )
        if data.get("private") is True:
            continue
        for section in (
            "dependencies",
            "optionalDependencies",
            "peerDependencies",
        ):
            _check_npm_section(rel, section, data.get(section) or {}, errors)


def _is_exact_version(text: str) -> bool:
    return bool(SEMVER_RE.fullmatch(text))


def run_ruby_inspector(
    lockfiles: dict[str, str],
    gemspecs: dict[str, str],
) -> dict[str, object] | None:
    """Inspect Gemfile.lock and .gemspec files via the Ruby standard library."""
    ruby = shutil.which("ruby")
    if ruby is None:
        return None
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        req_path = tmp_dir / "request.json"
        req_path.write_text(
            json.dumps({"lockfiles": lockfiles, "gemspecs": gemspecs}),
            encoding="utf-8",
        )
        if os.name == "nt":
            stub = tmp_dir / "nbgv-stub.cmd"
            stub.write_text('@echo {"SemVer2":"0.0.0"}\r\n', encoding="utf-8")
        else:
            stub = tmp_dir / "nbgv-stub.sh"
            stub.write_text(
                '#!/bin/sh\nprintf \'{"SemVer2":"0.0.0"}\\n\'\n',
                encoding="utf-8",
            )
            stub.chmod(0o755)
        env = os.environ.copy()
        env["THREE_WORKFLOW_RELEASE_NBGV_PATH"] = str(stub)
        result = subprocess.run(
            [ruby, "-e", RUBY_INSPECTOR, str(req_path)],
            capture_output=True,
            text=True,
            check=True,
            env=env,
        )
        return json.loads(result.stdout)


def _is_major_ceiling(version_text: str) -> bool:
    parts = version_text.split(".")
    return (
        bool(parts)
        and parts[0].isdigit()
        and all(part == "0" for part in parts[1:])
    )


def _check_ruby_requirements(
    reqs: list[list[str]],
    label: str,
    *,
    allow_major_ceiling: bool,
    errors: list[str],
) -> None:
    ops = [op for op, _ in reqs]
    if ">=" not in ops:
        errors.append(f"{label}: missing '>=' minimum floor in {reqs!r}.")
        return
    for op, ver in reqs:
        if op == ">=":
            continue
        if allow_major_ceiling and op == "<" and _is_major_ceiling(ver):
            continue
        errors.append(
            f"{label}: disallowed upper/exact constraint '{op} {ver}' "
            "on public gem floor.",
        )


def _collect_active_ruby_lockfiles(
    root: Path,
    errors: list[str],
) -> dict[str, str]:
    active_gemfiles = [
        path
        for path in _discover_files(root, "Gemfile")
        if not _rel(root, path).startswith(RUBY_CONTEXT_PREFIX)
    ]
    lockfiles: dict[str, str] = {}
    for gemfile in active_gemfiles:
        bundle_dir = gemfile.parent
        config_path = bundle_dir / ".bundle/config"
        lock_path = bundle_dir / "Gemfile.lock"
        config_rel = _rel(root, config_path)
        lock_rel = _rel(root, lock_path)
        if not config_path.is_file():
            errors.append(f"{config_rel}: missing active Bundler config.")
        else:
            cfg = yaml.safe_load(config_path.read_text(encoding="utf-8")) or {}
            if cfg.get("BUNDLE_COOLDOWN") != "3":
                errors.append(
                    f"{config_rel}: BUNDLE_COOLDOWN must be '3', "
                    f"got {cfg.get('BUNDLE_COOLDOWN')!r}.",
                )
        if not lock_path.is_file():
            errors.append(f"{lock_rel}: missing Gemfile.lock.")
        else:
            lockfiles[lock_rel] = str(lock_path)
    return lockfiles


def _check_inspected_ruby(
    inspected: dict[str, object],
    errors: list[str],
) -> None:
    inspected_locks = inspected["lockfiles"]
    if isinstance(inspected_locks, dict):
        for rel, ver_text in inspected_locks.items():
            is_valid = (
                isinstance(ver_text, str)
                and Version(ver_text) >= MIN_BUNDLER_COOLDOWN
            )
            if not is_valid:
                errors.append(
                    f"{rel}: Bundler version must be >= {MIN_BUNDLER_COOLDOWN} "
                    f"to honor BUNDLE_COOLDOWN, got {ver_text!r}.",
                )
    inspected_specs = inspected["gemspecs"]
    if not isinstance(inspected_specs, dict):
        return
    for rel, spec_data in inspected_specs.items():
        if not isinstance(spec_data, dict):
            continue
        _check_ruby_requirements(
            spec_data["required_ruby_version"],
            f"{rel} required_ruby_version",
            allow_major_ceiling=False,
            errors=errors,
        )
        for dep in spec_data["runtime_dependencies"]:
            _check_ruby_requirements(
                dep["requirements"],
                f"{rel} runtime_dependency({dep['name']})",
                allow_major_ceiling=True,
                errors=errors,
            )


RubyInspector = Callable[
    [dict[str, str], dict[str, str]],
    dict[str, object] | None,
]


def _check_ruby(
    root: Path,
    errors: list[str],
    inspector: RubyInspector = run_ruby_inspector,
) -> None:
    lockfiles = _collect_active_ruby_lockfiles(root, errors)
    public_lib = root / "src/public/lib"
    gemspecs = {
        _rel(root, p): str(p) for p in _discover_suffix(public_lib, ".gemspec")
    }
    if not lockfiles and not gemspecs:
        return
    inspected = inspector(lockfiles, gemspecs)
    if inspected is None:
        errors.append(
            "ruby executable is required on PATH to inspect Gemfile.lock "
            "and .gemspec contracts.",
        )
        return
    _check_inspected_ruby(inspected, errors)


def _xml_tag(elem: ET.Element) -> str:
    return elem.tag.rsplit("}", 1)[-1]


def _is_nuget_open_floor(version_text: str) -> bool:
    if not (version_text.startswith("[") and version_text.endswith(",)")):
        return False
    floor = version_text[1:-2].strip()
    return _is_exact_version(floor)


CANONICAL_PUBLIC_CPM_IMPORT = (
    "$([MSBuild]::GetPathOfFileAbove("
    "'Directory.Packages.props', '$(MSBuildThisFileDirectory)../'))"
)


def _parse_cpm_package_version(
    rel: str,
    elem: ET.Element,
    *,
    group_invalid: bool,
    errors: list[str],
) -> str | None:
    include = elem.attrib.get("Include", "").strip()
    update = elem.attrib.get("Update", "").strip()
    pkg_id = include or update
    extra_attrs = set(elem.attrib) - {"Include", "Update", "Version"}
    invalid = group_invalid
    if not pkg_id or (bool(include) and bool(update)):
        errors.append(
            f"{rel}: PackageVersion must set one of Include or Update.",
        )
        return None
    if extra_attrs or len(elem) > 0:
        errors.append(
            f"{rel} ({pkg_id}): extra attributes {sorted(extra_attrs)!r} or "
            "child metadata on PackageVersion are forbidden.",
        )
        invalid = True
    version_text = elem.attrib.get("Version", "")
    if not _is_nuget_open_floor(version_text):
        errors.append(
            f"{rel} ({pkg_id}): Version must use '[x.y.z,)' "
            f"minimum-floor syntax, got {version_text!r}.",
        )
        invalid = True
    return None if invalid else pkg_id.lower()


def _parse_cpm_item_group(
    rel: str,
    group: ET.Element,
    isolated: set[str],
    errors: list[str],
) -> None:
    group_invalid = bool(group.attrib)
    if group_invalid:
        errors.append(
            f"{rel}: attributes {sorted(group.attrib)!r} on ItemGroup are "
            "forbidden; isolate PackageVersion floors unconditionally.",
        )
    for elem in group:
        tag = _xml_tag(elem)
        if tag != "PackageVersion":
            errors.append(
                f"{rel}: unsupported ItemGroup child <{tag}>; only "
                "PackageVersion is allowed.",
            )
            continue
        pkg_lower = _parse_cpm_package_version(
            rel,
            elem,
            group_invalid=group_invalid,
            errors=errors,
        )
        if pkg_lower is not None:
            isolated.add(pkg_lower)


def _read_public_cpm_floors(root: Path, errors: list[str]) -> set[str]:
    props_path = root / "src/public/lib/Directory.Packages.props"
    if not props_path.is_file():
        errors.append("src/public/lib/Directory.Packages.props is missing.")
        return set()
    rel = _rel(root, props_path)
    children = list(ET.parse(props_path).getroot())  # noqa: S314
    if (
        not children
        or _xml_tag(children[0]) != "Import"
        or children[0].attrib != {"Project": CANONICAL_PUBLIC_CPM_IMPORT}
        or len(children[0]) > 0
    ):
        errors.append(
            f"{rel}: first element must be the canonical unconditional "
            "parent Directory.Packages.props Import.",
        )
    isolated: set[str] = set()
    for child in children[1:]:
        tag = _xml_tag(child)
        if tag != "ItemGroup":
            errors.append(
                f"{rel}: unsupported element <{tag}> after canonical Import; "
                "only unconditional ItemGroup is allowed.",
            )
            continue
        _parse_cpm_item_group(rel, child, isolated, errors)
    return isolated


def _is_packable_csproj(root_elem: ET.Element) -> bool:
    all_decls = [e for e in root_elem.iter() if _xml_tag(e) == "IsPackable"]
    if not all_decls:
        return True
    unconditional_decls = [
        prop
        for group in root_elem
        if _xml_tag(group) == "PropertyGroup" and not group.attrib
        for prop in group
        if _xml_tag(prop) == "IsPackable" and not prop.attrib and len(prop) == 0
    ]
    if len(unconditional_decls) != len(all_decls):
        return True
    return (unconditional_decls[-1].text or "").strip().lower() != "false"


def _is_unconditionally_private_all(elem: ET.Element) -> bool:
    pa_children = [c for c in elem if _xml_tag(c) == "PrivateAssets"]
    if not pa_children:
        return elem.attrib.get("PrivateAssets", "").strip().lower() == "all"
    return (
        len(pa_children) == 1
        and not pa_children[0].attrib
        and len(pa_children[0]) == 0
        and (pa_children[0].text or "").strip().lower() == "all"
    )


def _check_csproj_package_ref(
    rel: str,
    elem: ET.Element,
    *,
    isolated_lower: set[str],
    errors: list[str],
) -> None:
    if _is_unconditionally_private_all(elem):
        return
    pkg_id = (
        elem.attrib.get("Include") or elem.attrib.get("Update") or ""
    ).strip()
    if not pkg_id:
        return
    has_inline = any(
        attr in elem.attrib or any(_xml_tag(child) == attr for child in elem)
        for attr in ("Version", "VersionOverride")
    )
    if has_inline:
        errors.append(
            f"{rel} ({pkg_id}): inline Version/VersionOverride is forbidden; "
            "isolate in src/public/lib/Directory.Packages.props.",
        )
    if pkg_id.lower() not in isolated_lower:
        errors.append(
            f"{rel} ({pkg_id}): missing unconditional '[x.y.z,)' isolation "
            "entry in src/public/lib/Directory.Packages.props.",
        )


def _check_public_nuget_floors(root: Path, errors: list[str]) -> None:
    public_lib = root / "src/public/lib"
    if not public_lib.is_dir():
        return
    isolated_lower = _read_public_cpm_floors(root, errors)
    for csproj in _discover_suffix(public_lib, ".csproj"):
        root_elem = ET.parse(csproj).getroot()  # noqa: S314
        if not _is_packable_csproj(root_elem):
            continue
        rel = _rel(root, csproj)
        for elem in root_elem.iter():
            if _xml_tag(elem) == "PackageReference":
                _check_csproj_package_ref(
                    rel,
                    elem,
                    isolated_lower=isolated_lower,
                    errors=errors,
                )


def check_repository(
    root: Path,
    *,
    ruby_inspector: RubyInspector = run_ruby_inspector,
) -> list[str]:
    """Validate release-age alignment and public-library floor invariants."""
    errors: list[str] = []
    _check_mise(root, errors)
    _check_pnpm_workspaces(root, errors)
    _check_renovate_cli_npmrc(root, errors)
    _check_uv(root, errors)
    _check_renovate(root, errors)
    _check_public_python_floors(root, errors)
    _check_public_npm_floors(root, errors)
    _check_ruby(root, errors, inspector=ruby_inspector)
    _check_public_nuget_floors(root, errors)
    return errors


def main(argv: list[str] | None = None) -> int:
    """Run the repository supply-chain policy check."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=ROOT,
        help="Repository root directory to validate.",
    )
    args = parser.parse_args(argv)
    errors = check_repository(args.root.resolve())
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
