# /// script
# requires-python = ">=3.13"
# dependencies = [
#   "PyYAML==6.0.2", "jsonschema==4.25.1", "markdown-it-py==4.0.0",
# ]
# ///
"""Validate repository records prospectively within an explicit scope."""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import os
import posixpath
import re
import subprocess
import sys
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import unquote, urlsplit

import yaml
from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError, ValidationError
from markdown_it import MarkdownIt
from referencing.exceptions import Unresolvable

CURRENT_CATALOG_VERSION = 3
LEGACY_CATALOG_VERSION = 2
FAMILY_CATALOG = "docs/governance/record-families.yaml"
CONTROL_CATALOG = "docs/governance/controls.yaml"
SCHEMA_BINDINGS = {
    FAMILY_CATALOG: "schemas/governance/record-families.schema.json",
    CONTROL_CATALOG: "schemas/governance/controls.schema.json",
}
RECORD_SUFFIXES = {
    ".md",
    ".rst",
    ".adoc",
    ".asciidoc",
    ".yaml",
    ".yml",
    ".json",
    ".jsonl",
    ".csv",
}
MARKDOWN = MarkdownIt("commonmark", {"html": True})
# Definitions, not occurrences: a supported ID must start a Markdown heading.
REQUIREMENT_ID = re.compile(
    r"^((?:[A-Z][A-Z0-9]*-)*(?:REQ|FR|NFR|AC)-\d+[A-Z]?)(?=$|[\s:\u2014\u2013-])"
)


class RecordError(ValueError):
    """An input cannot establish a required validation condition."""


class StrictLoader(yaml.SafeLoader):
    """Reject duplicate keys before any schema validation."""


def unique_pairs(pairs: list[tuple[Any, Any]]) -> dict[Any, Any]:
    """Build a mapping while rejecting duplicate keys."""
    result: dict[Any, Any] = {}
    for key, value in pairs:
        if key in result:
            message = f"duplicate mapping key: {key}"
            raise RecordError(message)
        result[key] = value
    return result


def strict_mapping(
    loader: StrictLoader, node: yaml.MappingNode
) -> dict[Any, Any]:
    """Construct safe YAML mappings without silently replacing keys."""
    loader.flatten_mapping(node)
    return unique_pairs(
        [
            (loader.construct_object(k), loader.construct_object(v))
            for k, v in node.value
        ]
    )


StrictLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, strict_mapping
)


def parse_document(data: bytes, path: str) -> Any:  # noqa: ANN401 - JSON/YAML values are dynamically typed until schema validation.
    """Parse JSON or YAML with duplicate-key rejection."""
    text = data.decode("utf-8")
    if path.endswith(".json"):
        return json.loads(text, object_pairs_hook=unique_pairs)
    return yaml.load(text, Loader=StrictLoader)  # noqa: S506 - SafeLoader subclass rejects duplicate keys.


def git(root: Path, *args: str) -> bytes:
    """Read Git state without inheriting hook repository overrides."""
    # Ambient hook variables must not redirect repository reads.
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        capture_output=True,
        env=env,
        check=False,
    )
    if result.returncode:
        detail = result.stderr.decode(errors="replace").strip()
        message = f"git {' '.join(args)}: {detail}"
        raise RecordError(message)
    return result.stdout


@dataclass
class Entry:
    """Captured file mode, content identity and optional immutable bytes."""

    mode: str
    oid: str
    data: bytes | None = None
    untracked: bool = False


class Snapshot:
    """One immutable Git tree, or an explicitly captured bounded worktree."""

    def __init__(self, root: Path, revision: str | None = None) -> None:  # noqa: C901 - Separate commit and worktree capture branches.
        """Capture the requested Git tree or nonignored working tree."""
        self.root = root.resolve()
        actual_root = Path(
            git(self.root, "rev-parse", "--show-toplevel").decode().strip()
        ).resolve()
        if actual_root != self.root:
            message = "repository-root must be the Git worktree root"
            raise RecordError(message)
        self.entries: dict[str, Entry] = {}
        self.worktree = revision is None
        self.commit = (
            git(
                root,
                "rev-parse",
                "--verify",
                f"{'HEAD' if revision is None else revision}^{{commit}}",
            )
            .decode()
            .strip()
        )
        self.tree = (
            git(root, "rev-parse", f"{self.commit}^{{tree}}").decode().strip()
        )
        if revision is not None:
            for item in git(root, "ls-tree", "-rz", self.commit).split(b"\0"):
                if item:
                    metadata, name = item.split(b"\t", 1)
                    mode, _, oid = metadata.decode().split()
                    self.entries[name.decode()] = Entry(mode, oid)
        else:
            paths = git(
                root,
                "ls-files",
                "-z",
                "--cached",
                "--others",
                "--exclude-standard",
            ).split(b"\0")
            tracked = set(git(root, "ls-files", "-z").split(b"\0"))
            for raw in paths:
                if not raw:
                    continue
                path = raw.decode()
                target = root / path
                indirect = next(
                    (
                        a
                        for a in target.parents
                        if a != root
                        and a.is_relative_to(root)
                        and a.is_symlink()
                    ),
                    None,
                )
                if indirect is not None:
                    ancestor = indirect.relative_to(root).as_posix()
                    data = str(indirect.readlink()).encode()
                    self.entries[ancestor] = Entry(
                        "120000",
                        "sha256:" + hashlib.sha256(data).hexdigest(),
                        data,
                    )
                    self.entries[path] = Entry(
                        "000000", "unavailable:symlink-ancestor"
                    )
                    continue
                if target.is_symlink():
                    data, mode = str(target.readlink()).encode(), "120000"
                elif target.is_file():
                    # Capture once: validation never rereads a mutable file.
                    data, mode = target.read_bytes(), "100644"
                elif target.is_dir():
                    continue
                else:
                    continue  # Comparison retains tracked deletions.
                digest = hashlib.sha256(data).hexdigest()
                self.entries[path] = Entry(
                    mode, f"sha256:{digest}", data, raw not in tracked
                )
        manifest = [(p, e.mode, e.oid) for p, e in sorted(self.entries.items())]
        self.identity = hashlib.sha256(
            json.dumps(manifest, separators=(",", ":")).encode()
        ).hexdigest()

    def read(self, path: str) -> bytes:
        """Read immutable captured bytes without dereferencing symlinks."""
        entry = self.entries.get(path)
        if entry is None:
            message = f"missing snapshot file: {path}"
            raise RecordError(message)
        if entry.mode == "120000":
            message = f"symbolic link cannot be read as a record: {path}"
            raise RecordError(message)
        if entry.data is None:
            entry.data = git(self.root, "cat-file", "blob", entry.oid)
        return entry.data

    def directory(self, path: str) -> bool:
        """Check directory existence from the captured file inventory."""
        return any(p.startswith(path.rstrip("/") + "/") for p in self.entries)

    def symlink_ancestor(self, path: str) -> bool:
        """Reject direct or ancestor symlinks for authority routing."""
        return any(
            self.entries.get(str(p), Entry("", "")).mode == "120000"
            for p in [PurePosixPath(path), *PurePosixPath(path).parents]
        )

    def describe(self) -> dict[str, Any]:
        """Describe exact commit identity or captured worktree contents."""
        return {
            "kind": "worktree" if self.worktree else "commit",
            "commit": self.commit,
            "tree": None if self.worktree else self.tree,
            "manifest_sha256": self.identity,
            "identity_scope": "captured nonignored files"
            if self.worktree
            else "Git tree",
        }


def schema_validator(snapshot: Snapshot, path: str) -> Draft202012Validator:
    """Load a valid, local-only schema from the selected snapshot."""
    schema = parse_document(snapshot.read(path), path)
    Draft202012Validator.check_schema(schema)
    if re.search(r'"\$(?:ref|dynamicRef)"\s*:\s*"(?!#)', json.dumps(schema)):
        message = "only local schema references are supported"
        raise RecordError(message)
    return Draft202012Validator(schema)


def input_structure(path: str, catalog: Any, *, base: bool = False) -> None:  # noqa: ANN401 - Validate untrusted catalog values before consumption.
    """Enforce consumed field types independently of editable policy schemas."""
    text = {"type": "string"}

    def record(**fields: Any) -> dict[str, Any]:  # noqa: ANN401 - JSON Schema values have varied shapes.
        return {
            "type": "object",
            "required": list(fields),
            "properties": fields,
        }

    def items(shape: dict[str, Any]) -> dict[str, Any]:
        return {"type": "array", "items": shape}

    if path == FAMILY_CATALOG:
        binding = record(id=text, state=text, path=text, namespace=text)
        if (
            not base
            or not isinstance(catalog, dict)
            or catalog.get("schema_version") != LEGACY_CATALOG_VERSION
        ):
            binding["required"] += ["family", "carrier"]
            binding["properties"].update(family=text, carrier=text, schema=text)
            shape = record(
                schema_version={"const": CURRENT_CATALOG_VERSION},
                families=items(record(id=text)),
                bindings=items(binding),
            )
        else:
            shape = record(
                schema_version={"const": LEGACY_CATALOG_VERSION},
                families=items(binding),
            )
    else:
        implementation = record(kind=text)
        implementation["properties"]["value"] = text
        implementation["allOf"] = [
            {
                "if": {"properties": {"kind": {"const": "repository-path"}}},
                "then": {"required": ["value"]},
            }
        ]
        shape = record(
            schema_version={"const": 2},
            controls=items(
                record(
                    id=text,
                    governing_rules=items(text),
                    implementation=implementation,
                )
            ),
        )
    Draft202012Validator(shape).validate(catalog)


def generated_sources(snapshot: Snapshot) -> dict[str, dict[str, str]]:  # noqa: C901, PLR0912 - Validate native root, local and remote source boundaries.
    """Resolve exact APM deployments to their locked source package."""
    path = "apm.lock.yaml"
    lock = parse_document(snapshot.read(path), path)
    text = {"type": "string", "minLength": 1}
    strings = {"type": "array", "items": text}
    Draft202012Validator(
        {
            "type": "object",
            "required": ["dependencies", "deployments"],
            "properties": {
                "local_deployed_files": strings,
                "dependencies": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "required": ["repo_url", "deployed_files"],
                        "properties": {
                            "repo_url": text,
                            "deployed_files": strings,
                            "virtual_path": text,
                            "local_path": text,
                            "resolved_commit": text,
                        },
                    },
                },
                "deployments": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "required": ["kind", "value", "owners", "active_owner"],
                        "properties": {
                            "kind": text,
                            "value": text,
                            "owners": strings,
                            "active_owner": text,
                        },
                    },
                },
            },
        }
    ).validate(lock)
    dependencies: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for dependency in lock["dependencies"]:
        identity = dependency.get("local_path", dependency["repo_url"])
        if "local_path" not in dependency and dependency.get("virtual_path"):
            identity += "/" + dependency["virtual_path"]
        dependencies[identity].append(dependency)
    deployments: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for deployment in lock["deployments"]:
        if deployment["kind"] == "project-relative":
            deployments[deployment["value"]].append(deployment)
    result = {}
    for path, entries in deployments.items():
        if len(entries) != 1 or not safe_path(path):
            continue
        entry = entries[0]
        owner = entry["active_owner"]
        if owner == "." and owner in entry["owners"]:
            source_path = path.replace(".agents/skills/", ".apm/skills/", 1)
            if (
                path.startswith(".agents/skills/")
                and path in lock.get("local_deployed_files", [])
                and source_path in snapshot.entries
                and not snapshot.symlink_ancestor(source_path)
            ):
                result[path] = {"kind": "local-file", "path": source_path}
            continue
        sources = dependencies[owner]
        if owner not in entry["owners"] or len(sources) != 1:
            continue
        source = sources[0]
        if path not in source["deployed_files"]:
            continue
        if "local_path" in source:
            package = posixpath.normpath(source["local_path"])
            if (
                not safe_path(package)
                or not snapshot.directory(package)
                or snapshot.symlink_ancestor(package)
            ):
                continue
            result[path] = {"kind": "local-package", "path": package}
        elif re.fullmatch(r"[0-9a-f]{40}", source.get("resolved_commit", "")):
            result[path] = {
                "kind": "locked-package",
                "repository": source["repo_url"],
                "path": source.get("virtual_path", ""),
                "commit": source["resolved_commit"],
            }
    return result


def classify(path: str) -> str:  # noqa: C901, PLR0911, PLR0912 - Ordered independent classification selectors.
    """Independent selectors; no catalog entries are used by discovery."""
    parts = PurePosixPath(path).parts
    suffix = PurePosixPath(path).suffix.lower()
    if path.startswith((".agents/", ".github/agents/")):
        return "unresolved"
    if (
        "fixtures" in parts
        or "examples" in parts
        or path == "src/public/lib/asciidoctor-latexmath/sample.adoc"
    ):
        return "fixture-product-asset"
    if path.startswith("src/") and any(
        p in {"skills", "agents"} for p in parts[3:]
    ):
        return "domain-owned-package-interface"
    if path.startswith("schemas/governance/") or path in SCHEMA_BINDINGS:
        return "canonical-record"
    if path == (
        "src/public/lib/asciidoctor-latexmath/docs/research/"
        "processor-selection-input.txt"
    ):
        return "human-source-input"
    if suffix in {".md", ".rst", ".adoc", ".asciidoc"}:
        return "canonical-record"
    if PurePosixPath(path).name.lower().startswith(
        ("license", "notice", "copying")
    ) or path.endswith("reference-license.txt"):
        return "package-legal-interface"
    if suffix not in RECORD_SUFFIXES:
        return "source-or-other-asset"
    if any(
        p
        in {
            "docs",
            "contracts",
            "schemas",
            "evidence",
            "data",
            "assets",
            "resources",
            "samples",
            "examples",
        }
        for p in parts
    ):
        return "domain-owned-contract-evidence"
    if path.startswith("eng/workflow-delivery/v3/policies/") or (
        path.startswith("src/private/lib/scholarly-publication/")
        and any(p in {"evals", "tests"} for p in parts)
    ):
        return "domain-owned-contract-evidence"
    name = PurePosixPath(path).name
    if (
        any(
            segment in parts
            for segment in (".vscode", ".config", ".devcontainer")
        )
        or any(
            f"/{segment}/" in "/" + path
            for segment in (
                ".github/workflows",
                ".github/actions",
                ".github/codeql",
                ".github/workflow-delivery",
            )
        )
        or path == ".github/lsp.json"
    ):
        return "configuration-source"
    if name in {
        "package.json",
        "package-lock.json",
        "plugin.json",
        "renovate.json",
        "stylecop.json",
        "docker-compose.yml",
        "docker-compose.yaml",
        "pnpm-lock.yaml",
        "pnpm-workspace.yaml",
        "uv.lock",
        "apm.yml",
        "apm.lock.yaml",
        "global.json",
        "version.json",
        "biome.json",
        "biome.jsonc",
        "tsconfig.json",
        "launchSettings.json",
        "appsettings.json",
        "pyrightconfig.json",
        "workflow-delivery.quality.yml",
        "workflow-delivery.release-unit.yml",
    }:
        return "configuration-source"
    if name.startswith(("tsconfig.", "appsettings.", ".")) or name.endswith(
        (".schema.json", ".deps.json", ".lock.json")
    ):
        return "configuration-source"
    return "unresolved"


def safe_path(path: str, *, pattern: bool = False) -> bool:
    """Accept repository-relative POSIX paths and optional star globs."""
    return (
        bool(path)
        and not path.startswith("/")
        and "\\" not in path
        and ":" not in path
        and all(p not in {"", ".", ".."} for p in path.split("/"))
        and (pattern or not any(c in path for c in "*?[]"))
    )


def matches(path: str, pattern: str) -> bool:
    """Match star globs by path component, with recursive double stars."""
    regex = (
        re.escape(pattern)
        .replace(r"\*\*/", "(?:.*/)?")
        .replace(r"\*\*", ".*")
        .replace(r"\*", "[^/]*")
    )
    return re.fullmatch(regex, path) is not None


def bindings(
    catalog: dict[str, Any], *, base: bool = False
) -> list[dict[str, Any]]:
    """Read current bindings or the explicitly accepted legacy base."""
    if catalog.get("schema_version") == CURRENT_CATALOG_VERSION:
        return catalog["bindings"]
    if base and catalog.get("schema_version") == LEGACY_CATALOG_VERSION:
        return catalog["families"]
    message = (
        "Candidate family catalog must use schema_version 3; "
        "version 2 is accepted only for the explicit base"
    )
    raise RecordError(message)


class ExplicitAnchors(HTMLParser):
    """Collect explicit HTML anchors embedded in parsed Markdown."""

    def __init__(self) -> None:
        """Initialize the bounded anchor collection."""
        super().__init__()
        self.anchors: set[str] = set()

    def handle_starttag(
        self, tag: str, attrs: list[tuple[str, str | None]]
    ) -> None:
        """Retain IDs and named anchors from real HTML tokens."""
        for name, value in attrs:
            if value and (name == "id" or (tag == "a" and name == "name")):
                self.anchors.add(value)


def plain(tokens: list[Any]) -> str:
    """Extract visible inline text from parsed Markdown tokens."""
    return "".join(
        t.content if t.type in {"text", "code_inline", "image"} else ""
        for t in tokens
    )


def markdown(data: bytes) -> dict[str, Any]:
    """Extract real Markdown destinations, headings and explicit anchors."""
    tokens = MARKDOWN.parse(data.decode("utf-8"))
    links: list[str] = []
    images: list[str] = []
    anchors: set[str] = set()
    headings: list[str] = []
    counts: Counter[str] = Counter()
    html = ExplicitAnchors()

    def visit(items: list[Any]) -> None:
        for token in items:
            if token.type == "link_open":
                links.append(token.attrGet("href"))
            if token.type == "image":
                images.append(token.attrGet("src"))
                # Image children render as alt text, not navigation or anchors.
                continue
            if token.type in {"html_inline", "html_block"}:
                html.feed(token.content)
            if token.children:
                visit(token.children)

    visit(tokens)
    for index, token in enumerate(tokens):
        if token.type != "heading_open":
            continue
        heading = plain(tokens[index + 1].children or [])
        headings.append(heading)
        slug = "".join(
            c
            for c in heading.lower()
            if c in "-_ " or unicodedata.category(c)[0] in {"L", "N", "M"}
        ).replace(" ", "-")
        unique = slug if not counts[slug] else f"{slug}-{counts[slug]}"
        while unique in anchors:
            counts[slug] += 1
            unique = f"{slug}-{counts[slug]}"
        anchors.add(unique)
        counts[slug] += 1
    return {
        "links": links,
        "images": images,
        "anchors": anchors | html.anchors,
        "headings": headings,
    }


def check_repository(  # noqa: C901, PLR0912, PLR0915 - One ordered report transaction.
    root: Path,
    base: str,
    candidate: str | None,
    *,
    command: list[str] | None = None,
) -> dict[str, Any]:
    """Validate one candidate against an explicit accepted base."""
    report: dict[str, Any] = {
        "report_version": 1,
        "purpose": (
            "prospective bounded validation; "
            "not historical replay or contextual acceptance"
        ),
        "checker_source_sha256": hashlib.sha256(
            Path(__file__).read_bytes()
        ).hexdigest(),
        "repository": str(root.resolve()),
        "command": command or [],
        "base": None,
        "candidate": None,
        "diagnostics": [],
        "checks": {},
        "manifest": [],
        "deleted_paths": [],
        "binding_comparison": [],
        "tool_versions": {
            "python": sys.version.split()[0],
            **{
                name: importlib.metadata.version(name)
                for name in ("PyYAML", "jsonschema", "markdown-it-py")
            },
        },
    }

    def error(code: str, path: str, message: str) -> None:
        report["diagnostics"].append(
            {
                "severity": "error",
                "code": code,
                "path": path,
                "message": message,
            }
        )

    try:
        accepted = Snapshot(root, base)
        report["base"] = accepted.describe()
        current = Snapshot(root, candidate)
        report["candidate"] = current.describe()
    except (RecordError, OSError) as exc:
        error("snapshot-unavailable", "", str(exc))
        report["checks"]["snapshot"] = "failed"
        return report
    report["tool_versions"]["git"] = git(root, "--version").decode().strip()
    report["checks"]["snapshot"] = "executed"
    report["deleted_paths"] = sorted(
        set(accepted.entries) - set(current.entries)
    )
    generated: dict[str, dict[str, str]] = {}
    if any(
        p.startswith((".agents/", ".github/agents/")) for p in current.entries
    ):
        try:
            generated = generated_sources(current)
        except (
            RecordError,
            ValueError,
            TypeError,
            yaml.YAMLError,
            ValidationError,
        ) as exc:
            error("generated-source-map-invalid", "apm.lock.yaml", str(exc))
    classes = {
        p: "generated-interface"
        if p in generated and p.startswith((".agents/", ".github/agents/"))
        else classify(p)
        for p in current.entries
    }
    report["manifest"] = [
        {
            "path": p,
            "mode": e.mode,
            "blob": e.oid,
            "classification": classes[p],
            "untracked": e.untracked,
            **(
                {"canonical_source": generated[p]}
                if classes[p] == "generated-interface"
                else {}
            ),
        }
        for p, e in sorted(current.entries.items())
    ]
    for path, category in classes.items():
        if category == "unresolved":
            error(
                "classification-unresolved",
                path,
                "No independent selector establishes this candidate's "
                "classification",
            )
    report["checks"]["independent-discovery"] = "executed"
    catalogs: dict[str, Any] = {}
    for path, schema_path in SCHEMA_BINDINGS.items():
        try:
            validator = schema_validator(current, schema_path)
            instance = parse_document(current.read(path), path)
            issues = list(validator.iter_errors(instance))
            for issue in issues:
                error(
                    "schema-invalid",
                    path,
                    f"{list(issue.absolute_path)}: {issue.message}",
                )
            if not issues:
                input_structure(path, instance)
                catalogs[path] = instance
        except (
            ValueError,
            TypeError,
            UnicodeError,
            yaml.YAMLError,
            RecordError,
            SchemaError,
            ValidationError,
            Unresolvable,
        ) as exc:
            error("schema-input-invalid", path, str(exc))
    report["checks"]["fixed-schemas"] = "executed"
    if len(catalogs) != len(SCHEMA_BINDINGS):
        report["checks"]["dependent-record-checks"] = (
            "unavailable: catalog/schema validation failed"
        )
        return report
    family_catalog = catalogs[FAMILY_CATALOG]
    controls = catalogs[CONTROL_CATALOG]
    try:
        active_bindings = bindings(family_catalog)
        base_catalog = parse_document(
            accepted.read(FAMILY_CATALOG), FAMILY_CATALOG
        )
        schema_validator(accepted, SCHEMA_BINDINGS[FAMILY_CATALOG]).validate(
            base_catalog
        )
        input_structure(FAMILY_CATALOG, base_catalog, base=True)
        base_bindings = bindings(base_catalog, base=True)
    except (
        RecordError,
        ValueError,
        TypeError,
        yaml.YAMLError,
        SchemaError,
        ValidationError,
        Unresolvable,
    ) as exc:
        error("catalog-version-or-base-invalid", FAMILY_CATALOG, str(exc))
        report["checks"]["accepted-family-catalog"] = "failed"
        report["checks"]["dependent-record-checks"] = (
            "unavailable: accepted catalog/schema validation failed"
        )
        return report
    report["checks"]["accepted-family-catalog"] = "executed"
    for path, key in (
        (FAMILY_CATALOG, "families"),
        (FAMILY_CATALOG, "bindings"),
        (CONTROL_CATALOG, "controls"),
    ):
        ids = [item["id"] for item in catalogs[path][key]]
        for item, count in Counter(ids).items():
            if count > 1:
                error(
                    "duplicate-id", path, f"{key} contains duplicate ID {item}"
                )
    family_ids = {f["id"] for f in family_catalog["families"]}
    used_families = {b["family"] for b in active_bindings}
    for unused in sorted(family_ids - used_families):
        error("unused-family", FAMILY_CATALOG, unused)
    owners: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for binding in active_bindings:
        path = binding["path"]
        namespace = binding["namespace"]
        if binding["family"] not in family_ids:
            error(
                "unknown-family",
                FAMILY_CATALOG,
                f"{binding['id']}: {binding['family']}",
            )
        if not safe_path(path, pattern=True) or (
            binding["carrier"] == "repository-file"
            and any(c in path for c in "*?[]")
        ):
            error("unsafe-binding-path", path, binding["id"])
            continue
        if namespace != "repository" and (
            not safe_path(namespace)
            or not namespace.startswith("src/")
            or not current.directory(namespace)
        ):
            error("invalid-namespace", path, namespace)
        selected = [p for p in current.entries if matches(p, path)]
        if binding["state"] == "scheduled":
            if selected:
                error("scheduled-has-instances", path, binding["id"])
            continue
        if not selected:
            error("binding-empty", path, binding["id"])
        for selected_path in selected:
            owners[selected_path].append(binding)
        schema_path = binding.get("schema")
        if schema_path and (
            not safe_path(schema_path) or schema_path not in current.entries
        ):
            error("schema-reference-missing", path, schema_path)
    canonical = {
        p
        for p, c in classes.items()
        if c in {"canonical-record", "human-source-input"}
    }
    # Accepted record obligations survive candidate catalog deletion.
    inherited = {
        p
        for p in current.entries.keys() & accepted.entries.keys()
        if any(
            b["state"] == "current" and matches(p, b["path"])
            for b in base_bindings
        )
    }
    canonical.update(inherited)
    report["accepted_record_obligations"] = sorted(inherited)
    for path in sorted(canonical | set(owners)):
        count = len(owners[path])
        if count != 1:
            error(
                "record-coverage",
                path,
                f"Expected one current binding, found {count}",
            )
        if current.symlink_ancestor(path):
            error(
                "canonical-symlink",
                path,
                "Canonical records require direct files and ancestors",
            )
    report["checks"]["bindings-and-coverage"] = "executed"
    parsed: dict[str, Any] = {}
    for path in current.entries:
        if path.endswith(".md") and not current.symlink_ancestor(path):
            try:
                parsed[path] = markdown(current.read(path))
            except (UnicodeError, RecordError) as exc:
                if path in canonical or path in owners:
                    error("markdown-input-invalid", path, str(exc))
    edges: dict[str, set[str]] = defaultdict(set)

    def reference(  # noqa: C901, PLR0911, PLR0912 - Each reference boundary has one diagnostic.
        source: str,
        destination: str,
        *,
        repository_relative: bool = False,
        local_only: bool = False,
        require_anchor: bool = False,
    ) -> str | None:
        try:
            url = urlsplit(destination)
        except ValueError:
            error("reference-invalid", source, destination)
            return None
        if url.scheme or url.netloc:
            if local_only:
                error("repository-reference-required", source, destination)
            return None
        if require_anchor and (not url.path or not url.fragment):
            error("retirement-target-invalid", source, destination)
            return None
        decoded = unquote(url.path)
        path = (
            posixpath.normpath(
                posixpath.join(
                    "" if repository_relative else posixpath.dirname(source),
                    decoded,
                )
            )
            if decoded
            else source
        )
        if not safe_path(path):
            error("unsafe-reference", source, destination)
            return None
        if current.symlink_ancestor(path):
            error("symlink-reference", source, destination)
            return None
        if path not in current.entries:
            if current.directory(path):
                readme = path.rstrip("/") + "/README.md"
                if readme in current.entries:
                    path = readme
                elif not url.fragment:
                    return None  # Valid directory browsing, not a portal edge.
                else:
                    error("directory-anchor-unresolved", source, destination)
                    return None
            else:
                error("reference-missing", source, destination)
                return None
        if url.fragment and (
            path not in parsed
            or unquote(url.fragment) not in parsed[path]["anchors"]
        ):
            error("anchor-missing", source, destination)
        return path

    for path in sorted(canonical | set(owners)):
        if path not in parsed:
            continue
        for destination in parsed[path]["links"]:
            target = reference(path, destination)
            if target:
                edges[path].add(target)
        for destination in parsed[path]["images"]:
            reference(path, destination)
    for control in controls["controls"]:
        for destination in control["governing_rules"]:
            reference(CONTROL_CATALOG, destination, repository_relative=True)
        implementation = control["implementation"]
        if implementation["kind"] == "repository-path":
            reference(
                CONTROL_CATALOG,
                implementation["value"],
                repository_relative=True,
                local_only=True,
            )
    reached: set[str] = set()
    pending = ["docs/README.md"]
    while pending:
        path = pending.pop()
        if path not in reached:
            reached.add(path)
            pending.extend(edges[path])
    for path in sorted((canonical | set(owners)) - reached):
        error(
            "portal-unreachable",
            path,
            "No explicit link chain from docs/README.md",
        )
    report["checks"]["markdown-and-structured-references"] = "executed"
    report["checks"]["portal-reachability"] = "executed"

    retirements: list[tuple[str, str, str]] = []

    def base_document(path: str) -> dict[str, Any] | None:
        try:
            return markdown(accepted.read(path))
        except (UnicodeError, RecordError) as exc:
            error("base-markdown-input-invalid", path, str(exc))
            return None

    def identifiers(
        snapshot: Snapshot, definitions: list[dict[str, Any]], *, is_base: bool
    ) -> dict[tuple[str, str], list[str]] | None:
        found: dict[tuple[str, str], list[str]] = defaultdict(list)
        complete = True
        for path in snapshot.entries:
            if not path.endswith(".md") or snapshot.symlink_ancestor(path):
                continue
            routes = [
                b
                for b in definitions
                if b["state"] == "current" and matches(path, b["path"])
            ]
            if len(routes) != 1:
                continue
            document = base_document(path) if is_base else parsed.get(path)
            if document is None:
                complete = False
                continue
            for heading in document["headings"]:
                match = REQUIREMENT_ID.match(heading)
                if not match:
                    continue
                identifier = match.group(1)
                found[(routes[0]["namespace"], identifier)].append(path)
                retirement = re.search(
                    r"Retired\s*[-\u2014\u2013]\s*current authority:\s*(\S+)",
                    heading,
                    re.I,
                )
                if retirement and not is_base:
                    target = retirement.group(1)
                    if "/" in target or "#" in target:
                        reference(
                            path,
                            target,
                            repository_relative=True,
                            local_only=True,
                            require_anchor=True,
                        )
                    else:
                        retirements.append(
                            (routes[0]["namespace"], target, path)
                        )
        return found if complete else None

    old_ids = identifiers(accepted, base_bindings, is_base=True)
    new_ids = identifiers(current, active_bindings, is_base=False)
    if old_ids is None or new_ids is None:
        report["checks"]["requirement-heading-identifiers"] = (
            "unavailable: canonical Markdown input could not be parsed"
        )
    else:
        report["identifier_comparison"] = {
            "base_definition_count": sum(map(len, old_ids.values())),
            "candidate_definition_count": sum(map(len, new_ids.values())),
            "recognized_syntax": REQUIREMENT_ID.pattern,
        }
        for namespace, target, path in retirements:
            if (namespace, target) not in new_ids:
                error(
                    "retirement-target-missing", path, f"{namespace}:{target}"
                )
        for key in sorted(old_ids.keys() - new_ids.keys()):
            error(
                "established-identifier-removed",
                old_ids[key][0],
                f"{key[0]}:{key[1]}",
            )
        for key, paths in sorted(new_ids.items()):
            if len(paths) > 1:
                error(
                    "duplicate-requirement-definition",
                    paths[0],
                    f"{key[0]}:{key[1]}: {paths}",
                )
        report["checks"]["requirement-heading-identifiers"] = (
            "executed: REQ/FR/NFR/AC numeric heading definitions only; "
            "semantic meaning and other syntax require review"
        )
    for path in sorted(set(current.entries) | set(accepted.entries)):
        previous = (
            [
                b["namespace"]
                for b in base_bindings
                if b["state"] == "current" and matches(path, b["path"])
            ]
            if path in accepted.entries
            else []
        )
        now = [b["namespace"] for b in owners.get(path, [])]
        if previous or now:
            report["binding_comparison"].append(
                {
                    "path": path,
                    "base_namespaces": previous,
                    "candidate_namespaces": now,
                }
            )
    report["checks"].update(
        {
            "hk-plan-execution": (
                "unavailable: perform explicit profile/path plans "
                "and retain their results separately"
            ),
            "contextual-admission-and-authority": (
                "unavailable: independent review required"
            ),
            "other-identifier-syntax-and-meaning": (
                "unavailable: review project conventions and retirement meaning"
            ),
            "domain-contract-evidence": (
                "unavailable: owning domain validators remain required"
            ),
            "historical-validator-replay": (
                "unavailable: this is a prospective implementation"
            ),
        }
    )
    report["diagnostics"].sort(
        key=lambda d: (d["path"], d["code"], d["message"])
    )
    return report


def main(argv: list[str] | None = None) -> int:
    """Write a reproducible JSON report and return a deterministic status."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", default=Path(), type=Path)
    parser.add_argument("--base", required=True)
    snapshot = parser.add_mutually_exclusive_group(required=True)
    snapshot.add_argument("--candidate")
    snapshot.add_argument("--worktree", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    report = check_repository(
        args.repository_root,
        args.base,
        args.candidate,
        command=argv if argv is not None else sys.argv[1:],
    )
    output = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(output, encoding="utf-8")
    else:
        print(output, end="")
    return 1 if report["diagnostics"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
