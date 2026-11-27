# /// script
# requires-python = ">=3.13"
# dependencies = [
#   "PyYAML==6.0.4", "jsonschema==4.26.0", "markdown-it-py==4.3.0",
# ]
# ///
"""Validate repository record bindings and requirement heading identifiers."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path, PurePosixPath
from typing import Any
from urllib.parse import urlsplit

import yaml
from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError, ValidationError
from markdown_it import MarkdownIt
from repository_path_patterns import matches, safe_path, safe_record_pattern

FAMILY_CATALOG = "docs/governance/record-families.yaml"
FAMILY_SCHEMA = "schemas/governance/record-families.schema.json"
MARKDOWN = MarkdownIt("commonmark")
REQUIREMENT_ID = re.compile(
    r"^((?:[A-Z][A-Z0-9]*-)*(?:REQ|FR|NFR|AC)-\d+[A-Z]?)(?=$|[\s:\u2014\u2013-])"
)
BARE_REQUIREMENT_ID = re.compile(
    r"^(?:[A-Z][A-Z0-9]*-)*(?:REQ|FR|NFR|AC)-\d+[A-Z]?$"
)
RETIREMENT_TARGET = re.compile(
    r"Retired\s*[-\u2014\u2013]\s*current authority:\s*(.*)$",
    re.IGNORECASE,
)


class RecordError(ValueError):
    """Raised when a Git snapshot or record input cannot be read."""


def git(root: Path, *args: str, keep_index: bool = False) -> bytes:
    """Run Git against root without inheriting hook repository overrides."""
    env = {
        k: v
        for k, v in os.environ.items()
        if not k.startswith("GIT_") or (keep_index and k == "GIT_INDEX_FILE")
    }
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        capture_output=True,
        env=env,
        check=False,
    )
    if result.returncode:
        detail = result.stderr.decode(errors="replace").strip()
        msg = f"git {' '.join(args)}: {detail}"
        raise RecordError(msg)
    return result.stdout


def _has_symlink_component(root: Path, rel: str) -> bool:
    cur = root
    for part in Path(rel).parts:
        cur = cur / part
        if cur.is_symlink():
            return True
    return False


class Snapshot:
    """Immutable Git commit tree or current non-ignored working tree."""

    def __init__(self, root: Path, revision: str | None = None) -> None:  # noqa: C901 - Dispatches commit, index, and worktree capture.
        """Capture file modes and blob IDs from commit, index, or worktree."""
        self.root = root.resolve()
        target_rev = "HEAD" if revision in {None, ":index"} else revision
        self.commit = (
            git(self.root, "rev-parse", "--verify", f"{target_rev}^{{commit}}")
            .decode()
            .strip()
        )
        self.label = (
            "index"
            if revision == ":index"
            else ("worktree" if revision is None else self.commit)
        )
        self.modes: dict[str, str] = {}
        self._blobs: dict[str, str] = {}
        self._bytes: dict[str, bytes] = {}
        if revision == ":index":
            for item in git(
                self.root, "ls-files", "--stage", "-z", keep_index=True
            ).split(b"\0"):
                if item:
                    meta, name = item.split(b"\t", 1)
                    mode, oid, _ = meta.decode().split()
                    path = name.decode()
                    self.modes[path] = mode
                    self._blobs[path] = oid
            return
        if revision is not None:
            for item in git(self.root, "ls-tree", "-rz", self.commit).split(
                b"\0"
            ):
                if item:
                    meta, name = item.split(b"\t", 1)
                    mode, _, oid = meta.decode().split()
                    path = name.decode()
                    self.modes[path] = mode
                    self._blobs[path] = oid
            return
        paths = git(
            self.root,
            "ls-files",
            "-z",
            "--cached",
            "--others",
            "--exclude-standard",
        ).split(b"\0")
        for raw in paths:
            if not raw:
                continue
            rel = raw.decode()
            target = self.root / rel
            if target.is_symlink() or (
                _has_symlink_component(self.root, rel) and target.is_file()
            ):
                self.modes[rel] = "120000"
            elif target.is_file():
                self.modes[rel] = "100644"
                self._bytes[rel] = target.read_bytes()

    def read(self, path: str) -> bytes:
        """Return raw UTF-8/binary contents of a regular snapshot file."""
        mode = self.modes.get(path)
        if mode is None:
            msg = f"missing snapshot file: {path}"
            raise RecordError(msg)
        if mode == "120000":
            msg = f"symbolic link cannot be read as a record: {path}"
            raise RecordError(msg)
        if path not in self._bytes:
            self._bytes[path] = git(
                self.root, "cat-file", "blob", self._blobs[path]
            )
        return self._bytes[path]

    def directory(self, path: str) -> bool:
        """Report whether any captured file exists beneath path."""
        prefix = path.rstrip("/") + "/"
        return any(p.startswith(prefix) for p in self.modes)


def _token_text(token: Any) -> str:  # noqa: ANN401 - markdown-it Token.
    if token.type in {"softbreak", "hardbreak"}:
        return " "
    if token.type in {"text", "code_inline", "image"}:
        return str(token.content)
    return ""


def _is_local_fragment_link(href: str) -> bool:
    url = urlsplit(href)
    return (
        not url.scheme
        and not url.netloc
        and bool(url.fragment)
        and not url.fragment.startswith(":~:")
    )


def parse_headings(data: bytes) -> list[tuple[str, str | None, bool]]:
    """Return (visible_heading, tail_fragment_href, has_tail_link) tuples."""
    tokens = MARKDOWN.parse(data.decode("utf-8"))
    headings: list[tuple[str, str | None, bool]] = []
    for index, token in enumerate(tokens):
        if token.type != "heading_open":
            continue
        children = tokens[index + 1].children or []
        parts: list[str] = []
        links: list[tuple[int, int, str]] = []
        active_href: str | None = None
        link_start = 0
        pos = 0
        for child in children:
            if child.type == "link_open":
                active_href = child.attrGet("href")
                link_start = pos
            elif child.type == "link_close":
                if active_href is not None:
                    links.append((link_start, pos, active_href))
                active_href = None
            else:
                piece = _token_text(child)
                parts.append(piece)
                pos += len(piece)
        full = "".join(parts)
        visible = full.strip()
        ret = RETIREMENT_TARGET.search(full)
        tail_href: str | None = None
        has_tail_link = False
        if ret:
            t_start = ret.start(1) + (
                len(ret.group(1)) - len(ret.group(1).lstrip())
            )
            t_end = ret.start(1) + len(ret.group(1).rstrip())
            has_tail_link = any(
                l_end > t_start and l_start < t_end
                for l_start, l_end, _ in links
            )
            tail_href = next(
                (
                    href
                    for l_start, l_end, href in links
                    if l_start == t_start
                    and l_end == t_end
                    and _is_local_fragment_link(href)
                ),
                None,
            )
        headings.append((visible, tail_href, has_tail_link))
    return headings


def is_canonical_candidate(path: str) -> bool:
    """Identify files required to match a binding in record-families.yaml."""
    if path.startswith((".agents/", ".github/agents/")):
        return False
    parts = PurePosixPath(path).parts
    if "fixtures" in parts or "examples" in parts:
        return False
    if path.startswith("src/") and any(
        p in {"skills", "agents"} for p in parts[3:]
    ):
        return False
    if path in {
        FAMILY_CATALOG,
        FAMILY_SCHEMA,
        "docs/governance/reference-license.txt",
        (
            "src/public/lib/asciidoctor-latexmath/docs/research/"
            "processor-selection-input.txt"
        ),
    }:
        return True
    return PurePosixPath(path).suffix.lower() == ".md"


def valid_binding_pattern(pattern: str) -> bool:
    """Verify safe star-glob syntax compatible with CiScope.Bindings."""
    return safe_record_pattern(pattern) and not any(
        "**" in part and part != "**" for part in pattern.split("/")
    )


def resolve_namespace(
    snapshot: Snapshot, file_path: str, namespace: str
) -> str | None:
    """Resolve literal or single-star prefix namespaces for a matched file."""
    if namespace == "repository":
        return "repository"
    if "*" in namespace:
        ns_parts = namespace.split("/")
        file_parts = file_path.split("/")
        if (
            "**" in namespace
            or not safe_record_pattern(namespace)
            or len(file_parts) <= len(ns_parts)
        ):
            return None
        resolved = "/".join(file_parts[: len(ns_parts)])
        return resolved if matches(resolved, namespace) else None
    valid = safe_path(namespace) and snapshot.directory(namespace)
    return namespace if valid else None


def collect_identifiers(
    snapshot: Snapshot,
    bindings: list[dict[str, Any]],
) -> tuple[
    dict[tuple[str, str], list[str]],
    list[tuple[str, str, str]],
    list[tuple[str, str]],
]:
    """Collect requirement ID definitions, bare retirements, and bad targets."""
    defs: dict[tuple[str, str], list[str]] = defaultdict(list)
    retirements: list[tuple[str, str, str]] = []
    invalid_targets: list[tuple[str, str]] = []
    for path, mode in sorted(snapshot.modes.items()):
        if not path.endswith(".md") or mode == "120000":
            continue
        matched = [
            b
            for b in bindings
            if b.get("state") == "current"
            and valid_binding_pattern(b.get("path", ""))
            and matches(path, b["path"])
        ]
        if len(matched) != 1:
            continue
        ns = resolve_namespace(snapshot, path, matched[0]["namespace"])
        if ns is None:
            continue
        for visible, tail_href, has_tail_link in parse_headings(
            snapshot.read(path)
        ):
            req = REQUIREMENT_ID.match(visible)
            if not req:
                continue
            defs[(ns, req.group(1))].append(path)
            ret = RETIREMENT_TARGET.search(visible)
            if not ret:
                continue
            target = ret.group(1).strip()
            if tail_href is not None:
                continue
            if not has_tail_link and BARE_REQUIREMENT_ID.fullmatch(target):
                retirements.append((ns, target, path))
            else:
                invalid_targets.append((path, target or visible))
    return defs, retirements, invalid_targets


def _validate_binding_schema(
    current: Snapshot,
    binding: dict[str, Any],
    selected: list[str],
    error: Any,  # noqa: ANN401 - Diagnostic callback.
) -> None:
    schema_path = binding.get("schema")
    if not schema_path:
        return
    if not safe_path(schema_path) or schema_path not in current.modes:
        error("schema-reference-missing", binding["path"], schema_path)
        return
    try:
        schema_doc = json.loads(current.read(schema_path).decode("utf-8"))
        Draft202012Validator.check_schema(schema_doc)
        validator = Draft202012Validator(schema_doc)
        for sel in selected:
            raw = current.read(sel).decode("utf-8")
            doc = (
                json.loads(raw)
                if sel.endswith(".json")
                else yaml.safe_load(raw)
            )
            validator.validate(doc)
    except (
        RecordError,
        ValueError,
        TypeError,
        UnicodeError,
        yaml.YAMLError,
        SchemaError,
        ValidationError,
    ) as exc:
        error("record-schema-invalid", binding["path"], str(exc))


def _validate_bindings(  # noqa: C901 - Single-pass binding verification.
    current: Snapshot,
    active_bindings: list[dict[str, Any]],
    error: Any,  # noqa: ANN401 - Diagnostic callback.
) -> None:
    for item, count in Counter(b["id"] for b in active_bindings).items():
        if count > 1:
            error("duplicate-id", FAMILY_CATALOG, f"duplicate ID: {item}")
    owners: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for binding in active_bindings:
        pattern = binding["path"]
        ns_spec = binding["namespace"]
        if not valid_binding_pattern(pattern):
            error("unsafe-binding-path", pattern, binding["id"])
            continue
        selected = [p for p in current.modes if matches(p, pattern)]
        if binding["state"] == "scheduled":
            if selected:
                error("scheduled-has-instances", pattern, binding["id"])
            continue
        if not selected and "*" not in pattern:
            error("binding-empty", pattern, binding["id"])
        for sel in selected:
            if resolve_namespace(current, sel, ns_spec) is None:
                error("invalid-namespace", sel, ns_spec)
            owners[sel].append(binding)
        _validate_binding_schema(current, binding, selected, error)
    canonical = {p for p in current.modes if is_canonical_candidate(p)}
    for path in sorted(canonical | set(owners)):
        if len(owners[path]) != 1:
            error(
                "record-coverage",
                path,
                f"Expected 1 current binding, found {len(owners[path])}",
            )
        if current.modes.get(path) == "120000":
            error(
                "canonical-symlink", path, "Canonical records forbid symlinks"
            )


def _validate_identifiers(
    accepted: Snapshot | None,
    current: Snapshot,
    active_bindings: list[dict[str, Any]],
    error: Any,  # noqa: ANN401 - Diagnostic callback.
) -> None:
    new_ids, retirements, invalid_targets = collect_identifiers(
        current, active_bindings
    )
    for (ns, req_id), paths in sorted(new_ids.items()):
        if len(paths) > 1:
            error(
                "duplicate-requirement-definition", paths[0], f"{ns}:{req_id}"
            )
    for src, bad in invalid_targets:
        error("retirement-target-invalid", src, bad)
    for ns, target, src in retirements:
        if (ns, target) not in new_ids:
            error("retirement-target-missing", src, f"{ns}:{target}")
    if accepted is None or FAMILY_CATALOG not in accepted.modes:
        return
    base_doc = yaml.safe_load(accepted.read(FAMILY_CATALOG).decode("utf-8"))
    base_bindings = (
        base_doc.get("bindings", []) if isinstance(base_doc, dict) else []
    )
    if isinstance(base_bindings, list):
        old_ids, _, _ = collect_identifiers(accepted, base_bindings)
        for key in sorted(set(old_ids) - set(new_ids)):
            error(
                "established-identifier-removed",
                old_ids[key][0],
                f"{key[0]}:{key[1]}",
            )


def check_repository(
    root: Path,
    base: str | None,
    candidate: str | None,
) -> dict[str, Any]:
    """Validate candidate bindings and requirement IDs against optional base."""
    diagnostics: list[dict[str, str]] = []

    def error(code: str, path: str, message: str) -> None:
        diagnostics.append(
            {
                "severity": "error",
                "code": code,
                "path": path,
                "message": message,
            }
        )

    try:
        accepted = Snapshot(root, base) if base is not None else None
        current = Snapshot(root, candidate)
        schema = json.loads(current.read(FAMILY_SCHEMA).decode("utf-8"))
        Draft202012Validator.check_schema(schema)
        catalog = yaml.safe_load(current.read(FAMILY_CATALOG).decode("utf-8"))
        issues = list(Draft202012Validator(schema).iter_errors(catalog))
    except (
        RecordError,
        OSError,
        UnicodeError,
        ValueError,
        yaml.YAMLError,
        SchemaError,
    ) as exc:
        error("schema-input-invalid", FAMILY_CATALOG, str(exc))
        return {"diagnostics": diagnostics}

    for issue in issues:
        error(
            "schema-invalid",
            FAMILY_CATALOG,
            f"{list(issue.absolute_path)}: {issue.message}",
        )
    if not issues:
        active_bindings: list[dict[str, Any]] = catalog["bindings"]
        _validate_bindings(current, active_bindings, error)
        _validate_identifiers(accepted, current, active_bindings, error)

    diagnostics.sort(key=lambda d: (d["path"], d["code"], d["message"]))
    return {
        "base": accepted.commit if accepted else None,
        "candidate": current.label,
        "diagnostics": diagnostics,
    }


def _default_accepted_base(root: Path) -> str:
    for ref in ("refs/remotes/origin/main", "refs/heads/main"):
        try:
            oid = git(root, "merge-base", "HEAD", ref).decode().strip()
        except RecordError:
            continue
        if oid:
            return oid
    return "HEAD"


def _resolve_revisions(
    root: Path,
    args: argparse.Namespace,
) -> tuple[str | None, str | None]:
    if args.base is not None:
        base: str | None = args.base
    elif os.environ.get("CI_FULL") == "true":
        base = None
    else:
        base = (
            os.environ.get("HK_BASE")
            or os.environ.get("CI_BASE")
            or _default_accepted_base(root)
        )

    if args.worktree:
        candidate: str | None = None
    elif args.staged:
        candidate = ":index"
    elif args.candidate is not None:
        candidate = args.candidate
    else:
        candidate = (
            os.environ.get("HK_CANDIDATE")
            or os.environ.get("CI_CANDIDATE")
            or None
        )
    return base, candidate


def main(argv: list[str] | None = None) -> int:
    """Run record validation and exit non-zero on any diagnostic."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", default=Path(), type=Path)
    parser.add_argument("--base")
    snapshot = parser.add_mutually_exclusive_group()
    snapshot.add_argument("--candidate")
    snapshot.add_argument("--staged", action="store_true")
    snapshot.add_argument("--worktree", action="store_true")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args(argv)

    base, candidate = _resolve_revisions(args.repository_root, args)
    report = check_repository(args.repository_root, base, candidate)
    rendered = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    elif not args.quiet:
        sys.stdout.write(rendered)
    for item in report["diagnostics"]:
        sys.stderr.write(f"{item['code']}: {item['path']}: {item['message']}\n")
    return 1 if report["diagnostics"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
