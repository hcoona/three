# /// script
# requires-python = ">=3.13"
# dependencies = ["packaging==26.3", "PyYAML==6.0.4"]
# ///
"""Report public release changes without dispatching or publishing releases."""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys
import tomllib
from dataclasses import dataclass, replace
from datetime import datetime
from fnmatch import fnmatchcase
from functools import partial
from http import HTTPStatus
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import urlopen
from xml.etree import ElementTree as ET

import yaml
from packaging.version import Version

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Iterator, Sequence

ROOT = Path(__file__).resolve().parents[2]
DESCRIPTOR = "workflow-delivery.release-unit.yml"
NON_RELEASE_PUBLIC_DIRS = {
    "src/public/lib/Memoization.Generators": (
        "Roslyn source-generator companion packed into Memoization"
    ),
    "src/public/lib/Nanopass.Generators": (
        "Roslyn source-generator and analyzer companion packed into Nanopass"
    ),
}
KINDS = {
    "dotnet/github-release-assets-v1": "github-release",
    "node/browser-extension-v1": "browser-store",
    "node/npm-package-v1": "npm",
    "dotnet/nuget-package-v1": "nuget",
    "python/distribution-set-v1": "pypi",
    "ruby/gem-v1": "rubygems",
}
STORES = ("chrome-web-store", "firefox-amo", "edge-addons")
STORE_WORKFLOW = "workflow-delivery-v3-browser-store-release.yml"
DEV_SCRIPTS = frozenset(
    {"lint", "typecheck", "test", "test-cov", "format", "hooks", "dev", "clean"}
)
PROPS = ("src/public/lib/Directory.Packages.props", "Directory.Packages.props")
NO_CHANGES = "No untriaged public release changes.\n"
LABEL = "release-triage"
type GitHub = Callable[[str, str, dict | None], dict | list | None]
type ReadApi = Callable[[str], dict | list | None]
type Fetch = Callable[[str], bytes | None]
type Git = Callable[..., str]


class InventoryError(ValueError):
    """A public directory or release declaration cannot be classified."""


@dataclass(frozen=True)
class Unit:
    """An Official unit and its repository-relative inputs."""

    id: str
    directory: str
    entry: str
    kind: str
    package_name: str
    tier: str
    companions: tuple[str, ...]
    packages: frozenset[str]
    prerelease_tag: str | None = None
    static_version: str | None = None

    @property
    def watched(self) -> tuple[str, ...]:
        """Return the unit, companions and primary shared dependency inputs."""
        extra = {
            "github-release": ("global.json",),
            "nuget": PROPS,
            "browser-store": ("pnpm-lock.yaml",),
        }
        return (self.directory, *self.companions, *extra.get(self.kind, ()))


def _required(value: object, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        message = f"{field} must be a non-empty string"
        raise InventoryError(message)
    return value.strip()


def _xml(text: str | bytes) -> ET.Element:
    return ET.fromstring(text)  # noqa: S314 - stdlib parser; no external entities.


def _read_manifest(root: Path, unit: Unit, entry: Path) -> Unit:
    match unit.kind:
        case "github-release" | "nuget":
            project = _xml(entry.read_bytes())
            if (
                entry.suffix != ".csproj"
                or project.tag.split("}")[-1] != "Project"
            ):
                message = "expected an MSBuild .csproj entry point"
                raise InventoryError(message)
            name = unit.id
            packages = frozenset(
                ref.attrib["Include"].casefold()
                for ref in project.iterfind(".//{*}PackageReference[@Include]")
            )
            references = project.iterfind(".//{*}ProjectReference[@Include]")
            dirs = (
                entry.parent / r.attrib["Include"].replace("\\", "/")
                for r in references
            )
            companions = tuple(
                p.resolve().parent.relative_to(root).as_posix() for p in dirs
            )
            if unit.kind == "nuget":
                name = _required(
                    project.findtext(".//{*}PackageId"), "PackageId"
                )
            return replace(
                unit,
                package_name=name,
                companions=companions,
                packages=packages,
            )
        case "npm" | "browser-store":
            manifest = json.loads(entry.read_bytes())
            name = _required(manifest["name"], "package name")
            versions = manifest.get("dependencies", {}).values()
            exact = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?")
            tier = (
                "bundled"
                if any(exact.fullmatch(v) for v in versions)
                else unit.tier
            )
            return replace(unit, package_name=name, tier=tier)
        case "pypi":
            manifest = tomllib.loads(entry.read_text(encoding="utf-8"))
            project = manifest["project"]
            name = _required(project["name"], "project.name")
            raw_ver = project.get("version")
            static = (
                raw_ver.strip()
                if isinstance(raw_ver, str) and raw_ver.strip()
                else None
            )
            return replace(unit, package_name=name, static_version=static)
        case _:
            if (
                entry.suffix != ".gemspec"
                or not entry.read_text(encoding="utf-8").strip()
            ):
                message = "expected a non-empty UTF-8 .gemspec entry point"
                raise InventoryError(message)
            return unit


def _prerelease_tag(root: Path, directory: Path) -> str | None:
    local = directory / "version.json"
    local_data = json.loads(local.read_bytes()) if local.exists() else {}
    inherits = local_data.get("inherit") is not False and (
        not local.exists() or bool(local_data.get("inherit"))
    )
    root_path = root / "version.json"
    root_data = (
        json.loads(root_path.read_bytes())
        if inherits and root_path.exists()
        else {}
    )
    merged = {**root_data, **local_data}
    if "prerelease" in merged:
        raw = str(merged["prerelease"] or "").lstrip("-")
    else:
        raw = str(merged.get("version") or "").partition("-")[2]
    return raw.split(".", 1)[0].split("+", 1)[0].lower() or None


def _read_unit(root: Path, path: Path) -> Unit:
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    if data["schema"] != "workflow-delivery/v3/release-unit":
        message = "expected schema workflow-delivery/v3/release-unit"
        raise InventoryError(message)
    uid = _required(data["release-unit"], "release-unit")
    builds = data["builds"]
    if not isinstance(builds, list) or len(builds) != 1:
        message = "expected exactly one build"
        raise InventoryError(message)
    build = builds[0]
    definition = _required(build["definition"], "definition")
    if definition not in KINDS:
        message = f"unsupported build definition {definition!r}"
        raise InventoryError(message)
    kind = KINDS[definition]
    dir_path = path.parent
    entry = (
        dir_path / _required(build["entry-point"], "entry-point")
    ).resolve()
    entry.relative_to(dir_path)
    directory, relative = (
        p.relative_to(root).as_posix() for p in (dir_path, entry)
    )
    tier = (
        "bundled" if kind in {"github-release", "browser-store"} else "library"
    )
    unit = Unit(
        uid,
        directory,
        relative,
        kind,
        uid,
        tier,
        (),
        frozenset(),
        prerelease_tag=_prerelease_tag(root, dir_path),
    )
    return _read_manifest(root, unit, entry)


def _check_directories(root: Path) -> None:
    for path in NON_RELEASE_PUBLIC_DIRS:
        if not (root / path).is_dir() or (root / path / DESCRIPTOR).exists():
            message = f"{path}: stale non-release exemption"
            raise InventoryError(message)
    for category in ("app", "lib"):
        parent = root / "src/public" / category
        if not parent.is_dir():
            message = f"{parent.relative_to(root)}: public directory is missing"
            raise InventoryError(message)
        for path in sorted(parent.iterdir()):
            local = path.relative_to(root).as_posix()
            if not path.is_dir() or local in NON_RELEASE_PUBLIC_DIRS:
                continue
            if not (path / DESCRIPTOR).is_file():
                message = f"{local}: missing {DESCRIPTOR}"
                raise InventoryError(message)


def discover_public_units(root: Path) -> list[Unit]:
    """Validate the public inventory and discover all release units offline."""
    root = root.resolve()
    _check_directories(root)
    units = {}
    for path in sorted((root / "src/public").rglob(DESCRIPTOR)):
        try:
            unit = _read_unit(root, path)
        except (
            ValueError,
            KeyError,
            TypeError,
            AttributeError,
            OSError,
            ET.ParseError,
            yaml.YAMLError,
        ) as error:
            message = f"{path.relative_to(root)}: {error}"
            raise InventoryError(message) from error
        if unit.id in units:
            message = f"{path.relative_to(root)}: duplicate unit {unit.id!r}"
            raise InventoryError(message)
        units[unit.id] = unit
    return list(units.values())


def _git(root: Path, *args: str, check: bool = True) -> str:
    argv = ["git", "--literal-pathspecs", *args]
    result = subprocess.run(argv, cwd=root, capture_output=True, check=check)
    return result.stdout.decode("utf-8")


def commit_exists(root: Path, sha: str | None) -> bool:
    """Check that a full SHA names a local commit."""
    if not sha or not re.fullmatch(r"[a-fA-F0-9]{40}", sha):
        return False
    args = ["git", "cat-file", "-e", f"{sha}^{{commit}}"]
    return (
        subprocess.run(
            args, cwd=root, capture_output=True, check=False
        ).returncode
        == 0
    )


def _tags(root: Path) -> dict[str, str]:
    fmt = "%(refname:strip=2) %(objectname) %(*objectname)"
    refs = _git(
        root,
        "for-each-ref",
        "--sort=-version:refname",
        f"--format={fmt}",
        "refs/tags/release",
        "refs/tags/buddy",
    )
    return {
        tag: peeled or sha
        for tag, sha, peeled in (
            line.split(" ", 2) for line in refs.splitlines()
        )
    }


def gh(method: str, endpoint: str, body: dict | None) -> dict | list | None:
    """Use gh for GitHub reads and JSON-formatted issue writes."""
    args = ["gh", "api", "--method", method, endpoint]
    if body is not None:
        args += ["--input", "-"]
    payload = None if body is None else json.dumps(body)
    result = subprocess.run(
        args, input=payload, capture_output=True, text=True, check=False
    )
    if result.returncode:
        if endpoint.endswith("/labels") and "(HTTP 422)" in result.stderr:
            return None
        message = f"GitHub request failed: {result.stderr.strip()}"
        raise RuntimeError(message)
    return json.loads(result.stdout) if result.stdout else None


def fetch(url: str) -> bytes | None:
    """Read public registry metadata, returning None only for HTTP 404."""
    try:
        with urlopen(url, timeout=30) as response:  # noqa: S310 - HTTPS registries.
            content = response.read()
            return (
                gzip.decompress(content)
                if response.headers.get("Content-Encoding") == "gzip"
                else content
            )
    except HTTPError as error:
        if error.code == HTTPStatus.NOT_FOUND:
            return None
        raise


@dataclass(frozen=True)
class Baseline:
    """An Official commit, publication time, or missing verification."""

    key: str
    version: str | None = None
    sha: str | None = None
    published: str | None = None
    note: str = ""


def _nuget(
    pkg: str, read: Fetch, tag: str | None
) -> tuple[str, str | None, Callable[[], str | None]]:
    flat = f"https://api.nuget.org/v3-flatcontainer/{pkg}"
    versions = json.loads(read(f"{flat}/index.json") or '{"versions": []}')[
        "versions"
    ]

    def matches(ver: str) -> bool:
        suffix = ver.partition("-")[2]
        return not suffix or (
            bool(tag)
            and suffix.split(".", 1)[0].split("+", 1)[0].lower() == tag
        )

    version = next((v for v in reversed(versions) if matches(v)), "")
    nuspec = read(f"{flat}/{version.lower()}/{pkg}.nuspec") if version else None
    repository = _xml(nuspec).find(".//{*}repository") if nuspec else None
    sha = repository.get("commit") if repository is not None else None
    registration = "https://api.nuget.org/v3/registration5-gz-semver2"
    leaf = f"{registration}/{pkg}/{version.lower()}.json"
    return version, sha, lambda: json.loads(read(leaf) or "{}").get("published")


def _npm_version(data: dict, tag: str | None) -> str | None:
    tags = data.get("dist-tags", {})
    candidates = [tags[t] for t in dict.fromkeys((tag, "latest")) if t in tags]
    times = data.get("time", {})
    if len(candidates) > 1 and all(times.get(v) for v in candidates):
        return max(candidates, key=lambda v: datetime.fromisoformat(times[v]))
    return next(iter(candidates), None)


def _pypi_baseline(
    data: dict, static_version: str | None
) -> tuple[str | None, str | None]:
    releases = data.get("releases", {})
    info_ver = (data.get("info") or {}).get("version")
    normalized = str(Version(static_version)) if static_version else None
    ranked: list[tuple[datetime, str, str]] = []
    fallback: str | None = None
    for ver in dict.fromkeys((normalized, info_ver)):
        if not ver or ver not in releases:
            continue
        fallback = fallback or ver
        date = min(
            (r["upload_time_iso_8601"] for r in releases[ver]),
            default=None,
        )
        if date:
            ranked.append((datetime.fromisoformat(date), ver, date))
    if ranked:
        _, ver, date = max(ranked, key=lambda item: item[0])
        return ver, date
    return fallback or info_ver, None


def _registry(
    unit: Unit,
    tags: dict[str, str],
    exists: Callable[[str | None], bool],
    read: Fetch,
) -> Baseline:
    kind = unit.kind
    pkg = quote(unit.package_name, safe="@")
    sha = None
    match kind:
        case "nuget":
            version, sha, published = _nuget(
                pkg.lower(), read, unit.prerelease_tag
            )
        case "npm":
            data = json.loads(read(f"https://registry.npmjs.org/{pkg}") or "{}")
            version = _npm_version(data, unit.prerelease_tag)
            sha = data["versions"][version].get("gitHead") if version else None
            published = lambda: data.get("time", {}).get(version)  # noqa: E731
        case "pypi":
            data = json.loads(read(f"https://pypi.org/pypi/{pkg}/json") or "{}")
            version, pypi_date = (
                _pypi_baseline(data, unit.static_version)
                if data
                else (None, None)
            )
            published = lambda: pypi_date  # noqa: E731
        case _:
            url = f"https://rubygems.org/api/v1/versions/{pkg}.json"
            versions = json.loads(read(url) or "[]")
            prefix = (unit.prerelease_tag or "").replace("-", ".pre.")
            selected = next(
                (
                    v
                    for v in versions
                    if not v["prerelease"]
                    or (
                        bool(prefix)
                        and bool(
                            re.fullmatch(
                                rf"{re.escape(prefix)}(?:\.\d+)*",
                                re.sub(
                                    r"^\d+(?:\.\d+)*(?:\.pre)?\.",
                                    "",
                                    v["number"],
                                ).lower(),
                            )
                        )
                    )
                ),
                {},
            )
            version = selected.get("number")
            published = lambda: selected.get("created_at")  # noqa: E731
    if not version:
        return Baseline("unreleased")
    for candidate in (sha, tags.get(f"release/{unit.id}/v{version}")):
        if candidate and exists(candidate):
            return Baseline(candidate, version=version, sha=candidate)
    if date := published():
        datetime.fromisoformat(date)
        return Baseline(date, version=version, published=date)
    message = f"{unit.id}: no commit or publication date for {version}"
    raise ValueError(message)


def _iter_pages(get: ReadApi, endpoint: str) -> Iterator[dict]:
    sep = "&" if "?" in endpoint else "?"
    page = 1
    while True:
        batch = get(f"{endpoint}{sep}per_page=100&page={page}")
        if not isinstance(batch, list) or not batch:
            return
        yield from batch
        if len(batch) < 100:  # noqa: PLR2004 - GitHub page size.
            return
        page += 1


def _release(
    uid: str,
    tags: dict[str, str],
    exists: Callable[[str | None], bool],
    releases: Iterable[dict],
) -> Baseline:
    prefix = f"release/{uid}/v"
    for release in releases:
        tag = release["tag_name"]
        if tag.startswith(prefix) and not (
            release["draft"] or release["prerelease"]
        ):
            if (sha := tags.get(tag)) and exists(sha):
                ver = tag.removeprefix(f"release/{uid}/")
                return Baseline(sha, version=ver, sha=sha)
            break
    buddy = next((tag for tag in tags if tag.startswith(f"buddy/{uid}/v")), "")
    return Baseline(
        "unreleased", note=f"latest Buddy tag: {buddy}" if buddy else ""
    )


def _store(
    api: ReadApi,
    store: str,
    runs: dict[str, dict],
    exists: Callable[[str | None], bool],
) -> Baseline:
    query = f"deployments?environment=workflow-delivery-v3-{store}"
    for deployment in _iter_pages(api, query):
        statuses = api(f"deployments/{deployment['id']}/statuses?per_page=10")
        if not statuses or statuses[0]["state"] != "success":
            continue
        status = statuses[0]
        links = f"{status.get('log_url')} {status.get('target_url')}"
        match = re.search(r"/actions/runs/(\d+)", links)
        sha = deployment["sha"]
        if match and exists(sha):
            rid = match[1]
            if rid not in runs:
                runs[rid] = api(f"actions/runs/{rid}")
            run = runs[rid]
            path = run.get("path", "").split("@", 1)[0]
            if path == f".github/workflows/{STORE_WORKFLOW}" and (
                run["status"],
                run["conclusion"],
            ) == ("completed", "success"):
                return Baseline(sha, version=sha[:7], sha=sha)
    return Baseline("unverified")


IGNORE_DIRS = {"docs", "examples", "test", "tests", "spec", ".github"}
IGNORE = (
    "README*.md",
    "CHANGELOG.md",
    "ChangeLog.md",
    "PRIVACY.md",
    ".travis.yml",
    ".markdownlint.yml",
    ".prettierrc*",
    ".typos.toml",
    ".standard.yml",
    ".rspec",
    ".nwa-config.yaml",
    "hk.pkl",
    "steps.pkl",
    ".mise.toml",
    "mise.lock",
    "Rakefile",
    "*.plantuml",
    "*.test.*",
    "*.spec.*",
)
LIB_NOISE = {
    "packages.lock.json",
    "Gemfile",
    "Gemfile.lock",
    "ReleaseNotes.md",
    "pnpm-lock.yaml",
    "package-lock.json",
    "yarn.lock",
    "uv.lock",
}


def _delivery_path(unit: Unit, path: str) -> bool:
    if unit.kind == "browser-store" and path == "pnpm-lock.yaml":
        return True
    parts = PurePosixPath(path).parts
    name = parts[-1]
    if IGNORE_DIRS.intersection(parts[:-1]) or any(
        fnmatchcase(name, p) for p in IGNORE
    ):
        return False
    if unit.tier == "library":
        return name not in LIB_NOISE
    if unit.kind == "github-release" or path == unit.entry:
        return True
    local = path.removeprefix(unit.directory + "/")
    prefixes = (
        ("src/", "patches/", "scripts/", "wxt.config.", "LICENSE")
        if unit.kind == "browser-store"
        else ("src/", "scripts/", "tsdown.config.", "LICENSE", "COPYING")
    )
    return local.startswith(prefixes) or local in {"version.json", DESCRIPTOR}


def _pick(data: dict, keys: str) -> dict:
    return {key: data[key] for key in keys.split() if key in data}


def _node(kind: str, text: str) -> tuple[dict, dict]:
    data = json.loads(text or "{}")
    scripts = {
        k: v
        for k, v in data.get("scripts", {}).items()
        if k.split(":")[0] not in DEV_SCRIPTS
    }
    if kind == "browser-store":
        tools = _pick(
            data.get("devDependencies", {}), "wxt @wxt-dev/auto-icons"
        )
        return _pick(data, "dependencies engines") | tools, scripts
    keys = (
        "type dependencies peerDependencies optionalDependencies "
        "exports main types files engines"
    )
    return _pick(data, keys), scripts


def _python(text: str) -> tuple[dict, object, dict]:
    data = tomllib.loads(text)
    keys = (
        "version dependencies optional-dependencies "
        "requires-python scripts entry-points dynamic"
    )
    hatch = (data.get("tool") or {}).get("hatch") or {}
    return (
        _pick(data.get("project", {}), keys),
        data.get("build-system"),
        _pick(hatch, "build version metadata"),
    )


def _versions(
    packages: frozenset[str], text: str
) -> set[tuple[str, str | None]]:
    result = set()
    for el in _xml(text or "<Project/>").iterfind(".//{*}PackageVersion"):
        name = el.get("Include", el.get("Update", "")).casefold()
        if name in packages:
            result.add((name, el.get("Version") or el.findtext("{*}Version")))
    return result


def _gem(text: str) -> list[str]:
    return [
        line
        for line in map(str.strip, text.splitlines())
        if line
        and not line.startswith("#")
        and "add_development_dependency" not in line
    ]


def _pnpm(directory: str, text: str) -> dict:
    return (yaml.safe_load(text) or {}).get("importers", {}).get(directory, {})


def _semantic(unit: Unit, path: str) -> Callable[[str], object] | None:
    if unit.kind == "browser-store" and path == "pnpm-lock.yaml":
        return partial(_pnpm, unit.directory)
    if path == unit.entry and unit.kind in {"npm", "browser-store"}:
        return partial(_node, unit.kind)
    if path == unit.entry and unit.kind == "pypi":
        return _python
    if unit.kind == "nuget" and path in PROPS:
        return partial(_versions, unit.packages)
    return (
        _gem if unit.kind == "rubygems" and path.endswith(".gemspec") else None
    )


def _diff(git: Git, before: str, after: str, paths: Sequence[str]) -> set[str]:
    revs = (before, after) if before else (after,)
    args = (
        "diff-tree",
        "--root",
        "--no-commit-id",
        "-r",
        "--no-renames",
        "--name-only",
        "-z",
    )
    return set(git(*args, *revs, "--", *paths).split("\0")) - {""}


def _affects(git: Git, unit: Unit, path: str, old: str, new: str) -> bool:
    if not _delivery_path(unit, path):
        return False
    if read := _semantic(unit, path):
        before = git("show", f"{old}:{path}", check=False) if old else ""
        after = git("show", f"{new}:{path}", check=False)
        if path == unit.entry and (not before or not after):
            return before != after
        return read(before) != read(after)
    return True


def _input_head(
    git: Git, unit: Unit, base: Baseline, security: set[str]
) -> str | None:
    watched = unit.watched
    paths = None
    if base.sha:
        paths = {
            p
            for p in _diff(git, base.sha, "HEAD", watched)
            if _affects(git, unit, p, base.sha, "HEAD")
        }
        if not paths:
            unit_commits = (
                git(
                    "log",
                    "--full-history",
                    "--format=%H",
                    f"{base.sha}..HEAD",
                    "--",
                    unit.directory,
                ).splitlines()
                if security
                else []
            )
            return next((sha for sha in unit_commits if sha in security), None)
        revs = (f"{base.sha}..HEAD",)
    elif base.published:
        revs = (f"--after={base.published}", "HEAD")
    else:
        revs = ("HEAD",)
    log = git("log", "--full-history", "--format=%H %P", *revs, "--", *watched)
    for line in log.splitlines():
        sha, _, parents = line.partition(" ")
        parent = parents.split(" ", 1)[0]
        changed = _diff(git, parent, sha, watched)
        candidates = changed if paths is None else changed & paths
        if (
            sha in security
            and any(p.startswith(unit.directory + "/") for p in changed)
        ) or any(_affects(git, unit, p, parent, sha) for p in candidates):
            return sha
    if not (base.sha or base.published):
        return git("rev-parse", "HEAD").strip()
    return (
        git("log", "-1", "--format=%H", "HEAD", "--", *watched).strip()
        if paths
        else None
    )


@dataclass(frozen=True)
class Pending:
    """A unit needing review, with a dismissible delivery state."""

    unit: Unit
    token: str
    reasons: tuple[str, ...]
    latest: str = ""


def _pending(
    git: Git, unit: Unit, bases: dict[str, Baseline], security: set[str]
) -> Pending | None:
    reasons, heads = [], set()
    labels = {
        "unverified": "No verified v3 run yet",
        "unreleased": "No Official release in local history",
    }
    inputs = {
        base: _input_head(git, unit, base, security)
        for base in dict.fromkeys(bases.values())
    }
    for channel, base in bases.items():
        head = inputs[base]
        if head is not None:
            heads.add(head)
            ver = f" ({base.version})" if base.version else ""
            note = f" ({base.note})" if base.note else ""
            reason = labels.get(base.key, f"Delivery inputs changed{ver}")
            reasons.append(f"{channel}: {reason}{note}")
    if not heads:
        return None
    unreleased = Baseline("unreleased")
    delivery_head = inputs.get(unreleased) or (
        _input_head(git, unit, unreleased, security) or ""
    )
    latest = (
        git("log", "-1", "--format=`%h` %s", delivery_head).strip()
        if delivery_head
        else ""
    )
    return Pending(
        unit,
        hashlib.sha256(delivery_head.encode()).hexdigest()[:16],
        tuple(reasons),
        latest,
    )


def _pages(call: GitHub, endpoint: str) -> list[dict]:
    return list(_iter_pages(lambda path: call("GET", path, None), endpoint))


def _security(repo: str, call: GitHub, api: ReadApi) -> set[str]:
    query = (
        f"search/issues?q=repo:{repo}+is:pr+is:merged+label:security"
        "&sort=updated&order=desc&per_page=100"
    )
    data = call("GET", query, None) or {}
    if not isinstance(data, dict) or data.get("incomplete_results"):
        message = "Incomplete GitHub security PR search results"
        raise RuntimeError(message)
    commits = set()
    for item in data.get("items", ()):
        pull = api(f"pulls/{item['number']}")
        commits.update(
            filter(None, (pull["merge_commit_sha"], pull["head"]["sha"]))
        )
    return commits


def evaluate(
    root: Path,
    units: Sequence[Unit],
    repo: str,
    call: GitHub,
    read: Fetch,
) -> list[Pending]:
    """Compare local delivery inputs with each unit's Official baseline."""

    def api(path: str) -> dict | list | None:
        return call("GET", f"repos/{repo}/{path}", None)

    git, exists = partial(_git, root), partial(commit_exists, root)
    tags = _tags(root)
    kinds = {unit.kind for unit in units}
    releases = (
        _pages(call, f"repos/{repo}/releases")
        if "github-release" in kinds
        else []
    )
    security = (
        _security(repo, call, api)
        if any(u.tier == "library" for u in units)
        else set()
    )
    runs, pending = {}, []
    for unit in units:
        match unit.kind:
            case "browser-store":
                bases = {
                    store: _store(api, store, runs, exists) for store in STORES
                }
            case "github-release":
                bases = {
                    "GitHub Release": _release(unit.id, tags, exists, releases)
                }
            case _:
                bases = {unit.kind: _registry(unit, tags, exists, read)}
        flagged = security if unit.tier == "library" else set()
        if item := _pending(git, unit, bases, flagged):
            pending.append(item)
    return pending


def _dispatch(unit: Unit) -> list[str]:
    if unit.kind == "browser-store":
        return [f"browser-store:store={s},channel=official" for s in STORES]
    target = {
        "npm": "npm:registry=npmjs",
        "nuget": "nuget:registry=nugetorg",
        "pypi": "python:registry=pypi",
        "rubygems": "ruby:destination=rubygems",
    }
    return [f"{target[unit.kind]},unit={unit.id}"]


def render_report(pending: Sequence[Pending], repo: str) -> str:
    """Render the pending units and owner-run dry-run and live commands."""
    if not pending:
        return NO_CHANGES
    lines = [
        "Public release units need review before their next Official release."
    ]
    titles = {
        "bundled": "Bundled and exact-locked deliverables",
        "library": "Unbundled libraries",
    }
    for tier, (key, title) in enumerate(titles.items(), 1):
        items = [
            f"- **`{p.unit.id}`** — {'; '.join(p.reasons)}"
            + (f"; latest change: {p.latest}." if p.latest else ".")
            for p in pending
            if p.unit.tier == key
        ]
        if items:
            lines += ["", f"## Tier {tier}: {title}", "", *items]
    lines += [
        "",
        "Run a dry run first. Choose which units and stores to release.",
    ]
    for mode, heading in (("true", "Dry run"), ("false", "Live release")):
        lines += ["", f"### {heading}", "", "```sh"]
        entries = [
            shlex.quote(f"{spec},dry-run={mode}")
            for p in pending
            if p.unit.kind != "github-release"
            for spec in _dispatch(p.unit)
        ]
        if entries:
            batch = "uv run --script eng/scripts/release_batch_dispatch.py"
            lines.append(f"{batch} --ref main {' '.join(entries)}")
        # The GitHub Release caller has no "-release" suffix for the batch tool.
        for item in pending:
            if item.unit.kind == "github-release":
                unit = shlex.quote(item.unit.id)
                lines.append(
                    "gh workflow run workflow-delivery-v3-github-release.yml "
                    f"--repo {shlex.quote(repo)} --ref main "
                    "-f destination=github-release "
                    f"-f unit={unit} -f dry-run={mode}"
                )
        lines += ["```"]
    return "\n".join(lines) + "\n"


BOT_LOGINS = frozenset({"github-actions[bot]", "app/github-actions"})


def _latest_state(issue: dict, comments: Sequence[dict]) -> dict:
    pattern = r"<!-- release-triage-state: (\{.*?\}) -->"
    for entry in reversed([issue, *comments]):
        author = (entry.get("user") or {}).get("login")
        if author not in BOT_LOGINS:
            continue
        for marker in reversed(re.findall(pattern, entry.get("body") or "")):
            try:
                return json.loads(marker)
            except json.JSONDecodeError:
                continue
    return {}


def sync_triage_issue(
    repo: str,
    assignee: str,
    pending: Sequence[Pending],
    report: str,
    call: GitHub,
) -> None:
    """Maintain the repo's single open triage ticket and owner dismissal."""
    base = f"repos/{repo}/issues"
    issues = [
        i
        for i in _pages(call, f"{base}?labels={LABEL}&state=all")
        if "pull_request" not in i
    ]
    opened = [i for i in issues if i["state"] == "open"]
    if not pending:
        for issue in opened:
            path = f"{base}/{issue['number']}"
            note = (
                "All public release units are aligned with their Official "
                "baselines; closing this triage ticket."
            )
            call("POST", f"{path}/comments", {"body": note})
            call("PATCH", path, {"state": "closed"})
        return
    state = {p.unit.id: p.token for p in pending}
    marker = (
        f"<!-- release-triage-state: {json.dumps(state, sort_keys=True)} -->"
    )
    issue = (
        opened[0]
        if opened
        else max(issues, key=lambda i: i.get("closed_at") or "", default=None)
    )
    previous = {}
    if issue is not None:
        path = f"{base}/{issue['number']}/comments"
        comments = _pages(call, path)
        previous = _latest_state(issue, comments)
        changed = [
            f"`{u}`" for u in sorted(state) if previous.get(u) != state[u]
        ]
        if opened and changed:
            note = (
                f"@{assignee} New or updated release changes "
                f"({', '.join(changed)}):"
            )
            call("POST", path, {"body": f"{note}\n\n{report}\n{marker}"})
    if not opened and not all(
        previous.get(uid) == token for uid, token in state.items()
    ):
        call("POST", f"repos/{repo}/labels", {"name": LABEL})
        call(
            "POST",
            base,
            {
                "title": "chore(release): Public Release Triage",
                "labels": [LABEL],
                "assignees": [assignee],
                "body": f"@{assignee}\n\n{report}\n{marker}",
            },
        )


def main(argv: Sequence[str] | None = None) -> int:
    """Check inventory offline or evaluate and report pending releases."""
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--root", type=Path, default=ROOT)
    cli.add_argument("--check-inventory", action="store_true")
    cli.add_argument(
        "--repo", default=os.environ.get("GITHUB_REPOSITORY", "hcoona/three")
    )
    cli.add_argument("--assignee", default="hcoona")
    cli.add_argument("--dry-run", action="store_true")
    cli.add_argument("--summary-file", type=Path)
    args = cli.parse_args(argv)
    try:
        units = discover_public_units(args.root)
        if args.check_inventory:
            return 0
        pending = evaluate(args.root, units, args.repo, gh, fetch)
        report = render_report(pending, args.repo)
        if args.summary_file:
            args.summary_file.write_text(report, encoding="utf-8")
        if args.dry_run:
            print(report, end="")
        else:
            sync_triage_issue(args.repo, args.assignee, pending, report, gh)
    except (
        ValueError,
        KeyError,
        TypeError,
        ET.ParseError,
        yaml.YAMLError,
        OSError,
        RuntimeError,
        subprocess.CalledProcessError,
    ) as error:
        print(f"Release triage: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
