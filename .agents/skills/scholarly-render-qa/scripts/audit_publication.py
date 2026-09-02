# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "jsonschema==4.25.1",
#   "playwright==1.56.0",
#   "pymupdf==1.26.6",
#   "tinycss2==1.4.0",
# ]
# ///

from __future__ import annotations

import argparse
import asyncio
import base64
import binascii
import hashlib
import json
import math
import os
import re
import shutil
import stat
import sys
import tempfile
import unicodedata
from bisect import bisect_left, bisect_right
from collections import Counter
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, cast
from urllib.parse import unquote, urlsplit
from urllib.request import url2pathname
from xml.etree.ElementTree import ParseError

import tinycss2
from defusedxml.common import DefusedXmlException
from defusedxml.ElementTree import fromstring
from jsonschema import Draft202012Validator

# Keep this self-contained runtime at the contract's readable 120-column size.
# fmt: off
SCRIPT_VERSION = "0.1.0"
TREE_ALGORITHM = "sha256-tree-json-v1"
TREE_SCOPE = "manifest-declared-regular-files-no-symlinks"
RENDER_DIRECTORY = "independent-renders"
PUBLICATION_PATH_BASE = "publication-root"
EVIDENCE_PATH_BASE = "evidence-root"
RELEASE_PATH_BASE = "release-root"
SVG_NAMESPACE = "http://www.w3.org/2000/svg"
PX_PER_INCH = 96.0
POINTS_PER_INCH = 72.0
PDF_TOLERANCE = 0.75
RASTER_SCALE = 2.0
MAX_RASTER_AREA_FACTOR = 4.0
MAX_PDF_OBJECTS = 100_000
MAX_PDF_PAGES = 500
MAX_DIAGNOSTIC_STRING = 256
MAX_DIAGNOSTIC_ITEMS = 25
NAVIGATION_TIMEOUT_MS = 60_000
RENDER_TIMEOUT_MS = 120_000
PAGE_POINTS = {"letter": (612.0, 792.0), "a4": (595.276, 841.89)}
PAGE_CSS_NAMES = {"letter": "Letter", "a4": "A4"}
SVG_LENGTH = re.compile(r"\s*([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)(?:pt|px)?\s*")
CORE_CHECK_IDS = (
    "manifest.integrity", "html.offline-profile", "render.geometry-overflow",
    "pdf.fonts", "pdf.actions-type3-text", "figures.crop-bindings",
    "render.repeatability", "rasters.complete", "publication.tree-unchanged",
)
HUMAN_REVIEW_SCOPE = (
    "Inspect every full-page raster at readable zoom.",
    "Check crop loss, overflow, page breaks, and continuation order.",
    "Check figure presence, identity, fidelity, and caption correspondence.",
    "Check mixed-script typography and notation fidelity.",
    "Review source labels, language inventories, translations, glosses, and errata.",
)
PROFILE_ID = "scholarly-fragment-and-stylesheet-v1"
PROFILE_SCHEMA_VERSION = "1.0"
DOM_URL_ATTRIBUTES = frozenset({
    "action", "background", "cite", "data", "formaction", "href", "longdesc",
    "manifest", "ping", "poster", "src", "srcset", "xlink:href",
})
ACTIVE_DOM_ELEMENTS = frozenset({
    "applet", "audio", "base", "button", "canvas", "datalist", "details", "dialog",
    "embed", "form", "frame", "frameset", "iframe", "input", "object", "script",
    "select", "source", "textarea", "track", "video",
})
SVG_ACTIVE_ELEMENTS = frozenset({
    "audio", "foreignobject", "iframe", "script", "video",
})
SVG_ANIMATION_ELEMENTS = frozenset({
    "animate", "animatemotion", "animatetransform", "discard", "set",
})
SVG_RESOURCE_ATTRIBUTES = frozenset({"href", "src", "xlink:href"})
SVG_URL_PRESENTATION_ATTRIBUTES = frozenset({
    "clip-path", "cursor", "fill", "filter", "marker-end", "marker-mid",
    "marker-start", "mask", "stroke",
})
OBSERVED_ACTIVE_ELEMENTS = frozenset({
    *ACTIVE_DOM_ELEMENTS,
    *SVG_ACTIVE_ELEMENTS,
    *SVG_ANIMATION_ELEMENTS,
})
CSS_LENGTH_INCHES = {
    "in": 1.0,
    "cm": 1.0 / 2.54,
    "mm": 1.0 / 25.4,
    "q": 1.0 / 101.6,
    "pt": 1.0 / POINTS_PER_INCH,
    "pc": 1.0 / 6.0,
    "px": 1.0 / PX_PER_INCH,
}
MAX_EMBEDDED_IMAGE_BYTES = 64 * 1024 * 1024
REPARSE_POINT_ATTRIBUTE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
ASSET_ROOT = Path(__file__).resolve().parents[1] / "assets"
ASSEMBLY_SCHEMA = ASSET_ROOT / "assembly-manifest.schema.json"
EVIDENCE_SCHEMA = ASSET_ROOT / "qa-evidence.schema.json"
RELEASE_SCHEMA = ASSET_ROOT / "release-manifest.schema.json"
PROFILE_PATH = ASSET_ROOT / "publication-profile.json"
CSS_UNSAFE_STRING_CATEGORIES = frozenset({"Cc", "Cf", "Cs"})
# Characters treated as ambiguous CJK visual-wrap boundaries.
CJK_LINE_JOIN_RANGES = (
    (0xB7, 0xB7),
    (0x305, 0x305),
    (0x323, 0x323),
    (0x1100, 0x11FF),
    (0x2E80, 0x2E99),
    (0x2E9B, 0x2EF3),
    (0x2F00, 0x2FD5),
    (0x2FF0, 0x2FFF),
    (0x3001, 0x3003),
    (0x3005, 0x3011),
    (0x3013, 0x301F),
    (0x3021, 0x3035),
    (0x3037, 0x303F),
    (0x3041, 0x3096),
    (0x3099, 0x30FF),
    (0x3131, 0x318E),
    (0x3190, 0x319F),
    (0x31C0, 0x31E5),
    (0x31EF, 0x321E),
    (0x3220, 0x3247),
    (0x3260, 0x327E),
    (0x3280, 0x32B0),
    (0x32C0, 0x32CB),
    (0x32D0, 0x3370),
    (0x337B, 0x337F),
    (0x33E0, 0x33FE),
    (0x3400, 0x4DBF),
    (0x4E00, 0x9FFF),
    (0xA700, 0xA707),
    (0xA960, 0xA97C),
    (0xAC00, 0xD7A3),
    (0xD7B0, 0xD7C6),
    (0xD7CB, 0xD7FB),
    (0xF900, 0xFA6D),
    (0xFA70, 0xFAD9),
    (0xFE45, 0xFE46),
    (0xFF61, 0xFFBE),
    (0xFFC2, 0xFFC7),
    (0xFFCA, 0xFFCF),
    (0xFFD2, 0xFFD7),
    (0xFFDA, 0xFFDC),
    (0x16FE2, 0x16FE3),
    (0x16FF0, 0x16FF6),
    (0x1AFF0, 0x1AFF3),
    (0x1AFF5, 0x1AFFB),
    (0x1AFFD, 0x1AFFE),
    (0x1B000, 0x1B122),
    (0x1B132, 0x1B132),
    (0x1B150, 0x1B152),
    (0x1B155, 0x1B155),
    (0x1B164, 0x1B167),
    (0x1D360, 0x1D371),
    (0x1F200, 0x1F200),
    (0x1F250, 0x1F251),
    (0x20000, 0x2A6DF),
    (0x2A700, 0x2B81D),
    (0x2B820, 0x2CEAD),
    (0x2CEB0, 0x2EBE0),
    (0x2EBF0, 0x2EE5D),
    (0x2F800, 0x2FA1D),
    (0x30000, 0x3134A),
    (0x31350, 0x33479),
)
PDF_LINE_SEPARATOR = re.compile(r"\r\n|\r|\n")
PDF_TERMINAL_LINE_SEPARATOR = re.compile(r"(?:\r\n|\r|\n)\Z")
MATCH_BOUNDARY_NONE = -1
MATCH_BOUNDARY_SPACE = -2
MATCH_BOUNDARY_AMBIGUOUS = -3
TEXT_MATCH_WORK_FACTOR = 16

class AuditError(RuntimeError):
    pass
class PublicationError(RuntimeError):
    pass
class ResourceUrlError(PublicationError):
    def __init__(self, category: str, value: str) -> None:
        self.diagnostic = resource_url_diagnostic(value, category)
        super().__init__(f"resource URL rejected: {category}")
@dataclass(frozen=True)
class Asset:
    path: str
    file: Path
    sha256: str
    bytes: int
    def json(self) -> dict[str, Any]:
        return {"path": self.path, "sha256": self.sha256, "bytes": self.bytes}


@dataclass(frozen=True)
class TreeSnapshot:
    files: dict[str, Asset]
    symlinks: tuple[str, ...]
    special: tuple[str, ...]
    inventory_sha256: str
    fingerprint_sha256: str
    def json(self) -> dict[str, Any]:
        return {"files": len(self.files), "path_inventory_sha256": self.inventory_sha256,
                "fingerprint_sha256": self.fingerprint_sha256, "symlinks": list(self.symlinks),
                "special_nodes": list(self.special)}

@dataclass
class Context:
    root: Path
    manifest_path: Path
    manifest: dict[str, Any]
    profile: dict[str, Any]
    profile_sha256: str
    manifest_asset: Asset
    assets: dict[str, Asset]
    html: Asset
    css: Asset
    pdf: Asset
    before: TreeSnapshot
    errors: list[Any]

@dataclass
class PassiveAudit:
    errors: list[Any]
    binding_errors: list[Any]
    source_svgs: dict[str, tuple[float, float]]

@dataclass
class Render:
    pdf: Asset
    requests: dict[str, list[str]]
    geometry: dict[str, Any]
    dom: dict[str, Any]

@dataclass
class Pdf:
    evidence: dict[str, Any]
    detail: dict[str, Any]
    rasters: list[dict[str, Any]]

@dataclass(frozen=True)
class ReviewLayout:
    root: Path
    evidence: Path
    release: Path
    rasters: Path
    renders: Path

class CaptionTextParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
        self.hidden_depth = 0

    def handle_starttag(
        self,
        tag: str,
        attrs: list[tuple[str, str | None]],
    ) -> None:
        del attrs
        if tag.casefold() in {"script", "style", "template"}:
            self.hidden_depth += 1

    def handle_endtag(self, tag: str) -> None:
        if tag.casefold() in {"script", "style", "template"} and self.hidden_depth:
            self.hidden_depth -= 1

    def handle_data(self, data: str) -> None:
        if not self.hidden_depth:
            self.parts.append(data)


def eprint(message: str) -> None:
    print(message, file=sys.stderr)


def canonical_json(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode()
def write_json(path: Path, value: Any) -> None:
    content = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True, allow_nan=False)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((content + "\n").encode())
def reject_constant(value: str) -> Any:
    raise ValueError(f"non-finite JSON number {value}")
def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result = dict(pairs)
    if len(result) != len(pairs):
        raise ValueError("duplicate JSON object key")
    return result
def read_json(path: Path, *, publication: bool = False) -> Any:
    try:
        return json.loads(
            path.read_text(encoding="utf-8"),
            parse_constant=reject_constant,
            object_pairs_hook=reject_duplicate_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        exception = PublicationError if publication else AuditError
        raise exception(f"cannot read JSON {path}: {error}") from error
def validate_schema(value: Any, schema_path: Path) -> list[str]:
    validator = Draft202012Validator(read_json(schema_path))
    return [
        f"{'/'.join(map(str, error.absolute_path)) or '$'}: {error.message}"
        for error in sorted(validator.iter_errors(value), key=lambda item: tuple(map(str, item.absolute_path)))
    ]
def hash_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()
def diagnostic_bytes(value: str) -> bytes:
    return value.encode("utf-8", errors="surrogatepass")
def css_unsafe_diagnostic(
    field: str,
    value: str,
    categories: list[str],
) -> str:
    content = diagnostic_bytes(value)
    return (
        f"{field} contains CSS-unsafe Unicode categories "
        f"{categories}; characters={len(value)}; bytes={len(content)}; "
        f"sha256={hash_bytes(content)}"
    )
def scheme_category(value: str) -> str:
    if PureWindowsPath(value).is_absolute() or value.startswith(("/", "\\")):
        return "path"
    try:
        scheme = urlsplit(value).scheme.casefold()
    except (UnicodeError, ValueError):
        return "invalid"
    if not scheme:
        return "none"
    if scheme in {"file", "http", "https"}:
        return scheme
    return "other"
def resource_url_diagnostic(value: str, category: str) -> dict[str, Any]:
    content = diagnostic_bytes(value)
    return {
        "kind": "resource-url",
        "category": category,
        "scheme_category": scheme_category(value),
        "input_characters": len(value),
        "input_bytes": len(content),
        "sha256": hash_bytes(content),
    }
def source_svg_diagnostic(logical: str, category: str) -> dict[str, Any]:
    content = diagnostic_bytes(logical)
    return {
        "kind": "source-svg",
        "category": category,
        "path": logical[:MAX_DIAGNOSTIC_STRING],
        "path_truncated": len(logical) > MAX_DIAGNOSTIC_STRING,
        "path_characters": len(logical),
        "path_bytes": len(content),
        "path_sha256": hash_bytes(content),
    }
def text_read_diagnostic(
    logical: str,
    category: str,
    error: OSError | UnicodeError,
    *,
    fragment_id: str | None = None,
) -> dict[str, Any]:
    record: dict[str, Any] = {
        "kind": "text-read",
        "category": category,
        "path": logical,
        "failure_category": "unicode" if isinstance(error, UnicodeError) else "os",
    }
    if fragment_id is not None:
        record["fragment_id"] = fragment_id
    if isinstance(error, OSError) and isinstance(error.errno, int):
        record["errno"] = error.errno
    return record
def action_target_sample(
    kind: str,
    page: int,
    target: str,
    target_category: str,
) -> dict[str, Any]:
    record: dict[str, Any] = {"kind": kind, "page": page}
    content = diagnostic_bytes(target)
    target_scheme = scheme_category(target)
    if target_category == "file" and target_scheme == "none":
        target_scheme = "path"
    record.update(
        {
            "target_category": target_category,
            "scheme_category": target_scheme,
            "target_characters": len(target),
            "target_bytes": len(content),
            "target_sha256": hash_bytes(content),
        }
    )
    return record
def bounded_diagnostic_string(value: str) -> str:
    if len(value) <= MAX_DIAGNOSTIC_STRING:
        return value
    content = diagnostic_bytes(value)
    suffix = (
        f"...[characters={len(value)},bytes={len(content)},"
        f"sha256={hash_bytes(content)}]"
    )
    return value[:MAX_DIAGNOSTIC_STRING - len(suffix)] + suffix
def bounded_diagnostic(value: Any) -> Any:
    if isinstance(value, str):
        return bounded_diagnostic_string(value)
    if isinstance(value, list):
        samples = [
            bounded_diagnostic(item)
            for item in value[:MAX_DIAGNOSTIC_ITEMS]
        ]
        if len(value) <= MAX_DIAGNOSTIC_ITEMS:
            return samples
        return {
            "samples": samples,
            "total": len(value),
            "omitted": len(value) - len(samples),
            "sha256": hash_bytes(canonical_json(value)),
        }
    if isinstance(value, dict):
        return {key: bounded_diagnostic(item) for key, item in value.items()}
    return value
def asset(path: Path, logical: str) -> Asset:
    content = path.read_bytes()
    return Asset(logical, path, hash_bytes(content), len(content))
def relative_asset(path: Path, root: Path) -> Asset:
    try:
        logical = path.relative_to(root).as_posix()
    except ValueError as error:
        raise AuditError(f"{path} is not beneath {root}") from error
    return asset(path, logical)
def stable_asset(item: Asset, path_base: str) -> dict[str, Any]:
    if path_base not in {PUBLICATION_PATH_BASE, EVIDENCE_PATH_BASE, RELEASE_PATH_BASE}:
        raise AuditError(f"unsupported stable asset path base: {path_base}")
    return {"path_base": path_base, **item.json()}
def manifest_path(value: str) -> str:
    pure = PurePosixPath(value)
    if (
        not value or pure.as_posix() != value or "\\" in value or pure.is_absolute() or PureWindowsPath(value).is_absolute()
        or any(part in {"", ".", ".."} for part in pure.parts) or urlsplit(value).scheme or value.startswith("//")
    ):
        raise PublicationError(f"unconfined manifest path: {value!r}")
    return pure.as_posix()
def confined(root: Path, value: str) -> Path:
    logical = manifest_path(value)
    path = root.joinpath(*PurePosixPath(logical).parts).resolve()
    try:
        path.relative_to(root)
    except ValueError as error:
        raise PublicationError(f"manifest path escapes root: {value!r}") from error
    return path
def snapshot(root: Path) -> TreeSnapshot:
    files: dict[str, Asset] = {}
    links: list[str] = []
    special: list[str] = []
    pending = [root]
    while pending:
        directory = pending.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                path = Path(entry.path)
                logical = path.relative_to(root).as_posix()
                info = entry.stat(follow_symlinks=False)
                reparse = bool(getattr(info, "st_file_attributes", 0) & REPARSE_POINT_ATTRIBUTE)
                if entry.is_symlink() or reparse:
                    links.append(logical)
                elif stat.S_ISDIR(info.st_mode):
                    pending.append(path)
                elif stat.S_ISREG(info.st_mode):
                    files[logical] = asset(path, logical)
                else:
                    special.append(logical)
    paths = sorted(files)
    records = [files[path].json() for path in paths]
    return TreeSnapshot(files, tuple(sorted(links)), tuple(sorted(special)),
                        hash_bytes(canonical_json(paths)), hash_bytes(canonical_json(records)))
def projected_assets(manifest: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
    records = [(f"inputs.{key}", value) for key, value in manifest["inputs"].items()]
    records.extend((f"fragments[{index}].asset", item["asset"]) for index, item in enumerate(manifest["fragments"], 1))
    records.extend(
        (f"figures[{fi}].parts[{pi}].source_svg", part["source_svg"])
        for fi, figure in enumerate(manifest["figures"], 1) for pi, part in enumerate(figure["parts"], 1)
    )
    records.extend((f"fonts[{index}].asset", item["asset"]) for index, item in enumerate(manifest["fonts"], 1))
    records.extend((f"stylesheets[{index}]", item) for index, item in enumerate(manifest["stylesheets"], 1))
    records.extend((f"outputs.{key}", value) for key, value in manifest["outputs"].items() if value is not None)
    return records
def duplicate_values(values: list[str]) -> list[str]:
    return sorted(value for value, count in Counter(values).items() if count > 1)
def load_profile() -> tuple[dict[str, Any], bytes]:
    try:
        profile_bytes = PROFILE_PATH.read_bytes()
    except OSError as error:
        raise AuditError("cannot read the bundled publication profile") from error
    profile = read_json(PROFILE_PATH)
    if (
        not isinstance(profile, dict)
        or profile.get("schema_version") != PROFILE_SCHEMA_VERSION
        or profile.get("profile_id") != PROFILE_ID
        or profile.get("closed") is not True
    ):
        raise AuditError("bundled publication profile identity is not supported")
    return profile, profile_bytes


def css_unsafe_categories(value: str) -> list[str]:
    return sorted(
        {
            category
            for character in value
            if (category := unicodedata.category(character))
            in CSS_UNSAFE_STRING_CATEGORIES
        }
    )


def manifest_bbox(value: list[Any]) -> list[float] | None:
    try:
        bbox = [float(coordinate) for coordinate in value]
    except (OverflowError, TypeError, ValueError):
        return None
    if not all(math.isfinite(coordinate) for coordinate in bbox):
        return None
    return bbox


def bbox_within_source(
    value: list[Any],
    source: tuple[float, float],
) -> bool:
    bbox = manifest_bbox(value)
    width, height = source
    return (
        bbox is not None
        and width > 0
        and height > 0
        and 0 <= bbox[0] < bbox[2] <= width + 0.001
        and 0 <= bbox[1] < bbox[3] <= height + 0.001
    )


def load_context(manifest_file: Path, html_file: Path, page_size: str) -> Context:
    manifest_file = manifest_file.resolve()
    html_file = html_file.resolve()
    if not manifest_file.is_file() or not html_file.is_file():
        raise PublicationError("assembly manifest and publication HTML must exist")
    root = manifest_file.parent.resolve()
    if manifest_file.name != "assembly-manifest.json":
        raise PublicationError("assembly manifest must be named assembly-manifest.json")
    value = read_json(manifest_file, publication=True)
    schema_findings = validate_schema(value, ASSEMBLY_SCHEMA)
    if schema_findings:
        raise PublicationError("assembly manifest schema validation failed: " + "; ".join(schema_findings))
    manifest = cast("dict[str, Any]", value)
    errors: list[Any] = []
    profile, profile_bytes = load_profile()
    profile_hash = hash_bytes(profile_bytes)
    policy = manifest["policies"]["publication_profile"]
    if (
        policy["id"] != profile.get("profile_id")
        or policy["schema_version"] != profile.get("schema_version")
        or policy["sha256"] != profile_hash
    ):
        errors.append("manifest publication-profile identity does not match QA")
    if manifest["print_geometry"]["page_size"] != page_size:
        errors.append(f"--page-size {page_size} conflicts with manifest geometry")
    declared_records: dict[str, dict[str, Any]] = {}
    declaration_labels: dict[str, str] = {}
    for label, record in projected_assets(manifest):
        logical = manifest_path(record["path"])
        if logical in declared_records:
            if declared_records[logical] != record:
                errors.append(
                    "conflicting semantic asset declarations for "
                    f"{logical}: {declaration_labels[logical]} and {label}"
                )
            continue
        declared_records[logical] = record
        declaration_labels[logical] = label
    assets: dict[str, Asset] = {}
    aliases: dict[str, str] = {}
    for logical, record in declared_records.items():
        path = confined(root, logical)
        alias = os.path.normcase(str(path))
        if alias in aliases:
            errors.append(
                "semantic asset paths alias: "
                f"{aliases[alias]!r}, {logical!r}"
            )
            continue
        aliases[alias] = logical
        if not path.is_file():
            errors.append(f"manifest-declared asset is missing: {logical}")
            continue
        observed = asset(path, logical)
        assets[logical] = observed
        if observed.sha256 != record["sha256"] or observed.bytes != record["bytes"]:
            errors.append(f"manifest-declared asset binding mismatch: {logical}")
    html_logical = manifest_path(manifest["outputs"]["html"]["path"])
    css_logical = manifest_path(manifest["outputs"]["css"]["path"])
    pdf_record = manifest["outputs"]["draft_pdf"]
    if pdf_record is None:
        raise PublicationError("assembly manifest has no canonical PDF")
    pdf_logical = manifest_path(pdf_record["path"])
    if confined(root, html_logical) != html_file:
        errors.append("--html does not match manifest outputs.html")
    for label, logical in (("HTML", html_logical), ("CSS", css_logical), ("PDF", pdf_logical)):
        if logical not in assets:
            raise PublicationError(f"manifest {label} output is unavailable")
    id_groups = {
        "fragment": [item["id"] for item in manifest["fragments"]],
        "figure": [item["id"] for item in manifest["figures"]],
        "figure DOM": [item["dom_id"] for item in manifest["figures"]],
        "crop": [part["id"] for figure in manifest["figures"] for part in figure["parts"]],
    }
    for label, values in id_groups.items():
        duplicates = duplicate_values(values)
        if duplicates:
            errors.append(f"duplicate {label} IDs: {duplicates}")
    for fragment in manifest["fragments"]:
        if fragment["dom_selector"] != f'[data-fragment-id="{fragment["id"]}"]':
            errors.append(f"fragment {fragment['id']} DOM selector is inconsistent")
    for figure in manifest["figures"]:
        if [part["order"] for part in figure["parts"]] != list(range(1, len(figure["parts"]) + 1)):
            errors.append(f"figure {figure['id']} part order is not contiguous")
        figure_profile = figure["profile"]
        if (
            figure_profile is not None
            and figure_profile not in manifest["profiles"]
        ):
            errors.append(
                f"figure {figure['id']} profile is not declared by the "
                f"assembly manifest: {figure_profile}"
            )
        if figure["caption_sha256"] != hash_bytes(figure["caption_html"].encode()):
            errors.append(f"figure {figure['id']} caption hash is inconsistent")
        for part in figure["parts"]:
            if part["dom_selector"] != f'[data-crop-id="{part["id"]}"]':
                errors.append(f"crop {part['id']} DOM selector is inconsistent")
            bbox = manifest_bbox(part["bbox"])
            if bbox is None:
                errors.append(
                    f"crop {part['id']} bbox coordinates must be finite and representable"
                )
                continue
            canonical_bbox = [float(f"{value:.3f}") for value in bbox]
            if bbox != canonical_bbox:
                errors.append(
                    f"crop {part['id']} bbox is not canonical to three decimals"
                )
            if bbox[2] <= bbox[0] or bbox[3] <= bbox[1]:
                errors.append(
                    f"crop {part['id']} bbox must have positive width and height"
                )
    families: set[str] = set()
    for index, font in enumerate(manifest["fonts"], start=1):
        unsafe = css_unsafe_categories(font["family"])
        if unsafe:
            errors.append(
                css_unsafe_diagnostic(
                    f"font {index} family",
                    font["family"],
                    unsafe,
                )
            )
        families.add(font["family"].casefold())
    for role, family in manifest["font_roles"].items():
        unsafe = css_unsafe_categories(family)
        if unsafe:
            errors.append(
                css_unsafe_diagnostic(
                    f"font role {role}",
                    family,
                    unsafe,
                )
            )
            continue
        if family.casefold() not in families:
            errors.append(f"font role {role} references undeclared {family!r}")
    before = snapshot(root)
    expected_paths = set(declared_records) | {"assembly-manifest.json"}
    missing = sorted(expected_paths - set(before.files))
    extra = sorted(set(before.files) - expected_paths)
    if missing:
        errors.append(f"retained tree is missing regular files: {missing}")
    if extra:
        errors.append(f"retained tree has undeclared regular files: {extra}")
    if before.symlinks:
        errors.append(f"retained tree has symlinks/reparse points: {before.symlinks}")
    if before.special:
        errors.append(f"retained tree has unsupported nodes: {before.special}")
    if "assembly-manifest.json" in declared_records:
        errors.append("assembly manifest must not track itself")
    return Context(root, manifest_file, manifest, profile, profile_hash, relative_asset(manifest_file, root),
                   assets, assets[html_logical], assets[css_logical], assets[pdf_logical], before, errors)
def normalize_text(value: str) -> str:
    return " ".join(unicodedata.normalize("NFC", value).split())


def caption_text(value: str) -> str:
    parser = CaptionTextParser()
    parser.feed(value)
    parser.close()
    return normalize_text("".join(parser.parts))


def parse_numbers(value: str | None) -> list[float] | None:
    try:
        numbers = (
            [float(part) for part in re.split(r"[\s,]+", value.strip()) if part]
            if value
            else None
        )
    except (OverflowError, ValueError):
        return None
    return (
        numbers
        if numbers and all(math.isfinite(number) for number in numbers)
        else None
    )


def same_numbers(
    actual: list[float] | None,
    expected: list[float],
    tolerance: float = 0.001,
) -> bool:
    return (
        actual is not None
        and len(actual) == len(expected)
        and all(
            abs(left - right) <= tolerance
            for left, right in zip(actual, expected, strict=True)
        )
    )


def same_aspect_ratio(
    rendered_width: float,
    rendered_height: float,
    expected_width: float,
    expected_height: float,
    tolerance: float = 0.01,
) -> bool:
    dimensions = (
        rendered_width,
        rendered_height,
        expected_width,
        expected_height,
    )
    if not all(math.isfinite(value) and value > 0 for value in dimensions):
        return False
    rendered_cross = rendered_width * expected_height
    expected_cross = rendered_height * expected_width
    if (
        not math.isfinite(rendered_cross)
        or not math.isfinite(expected_cross)
        or rendered_cross <= 0
        or expected_cross <= 0
    ):
        return False
    return abs(rendered_cross - expected_cross) <= (
        tolerance * max(rendered_cross, expected_cross)
    )


def resolve_url(root: Path, owner: Path, value: str) -> Path:
    try:
        parsed = urlsplit(value)
    except (UnicodeError, ValueError) as error:
        raise ResourceUrlError("invalid", value) from error
    if (
        parsed.scheme
        or parsed.netloc
        or parsed.query
        or parsed.fragment
        or value.startswith(("//", "/", "\\"))
    ):
        raise ResourceUrlError("nonlocal", value)
    if "\x00" in value or re.search(r"%(?![0-9A-Fa-f]{2})", parsed.path):
        raise ResourceUrlError("invalid", value)
    try:
        decoded = unquote(parsed.path, errors="strict")
        target = (owner.parent / decoded).resolve()
    except (OSError, UnicodeError, ValueError) as error:
        raise ResourceUrlError("invalid", value) from error
    try:
        target.relative_to(root)
    except ValueError as error:
        raise ResourceUrlError("escapes-root", value) from error
    return target


def css_resource_diagnostic(logical: str, category: str) -> dict[str, Any]:
    return {
        "kind": "css-resource",
        "category": category,
        "path": logical,
    }


def css_url_values(
    tokens: list[Any],
    *,
    depth: int = 0,
) -> tuple[list[str], list[str]]:
    if depth > 64:
        return [], ["recursion-limit"]
    urls: list[str] = []
    errors: list[str] = []
    for token in tokens:
        css_type = getattr(token, "type", "")
        if css_type == "error":
            errors.append("parse-error")
            continue
        if css_type == "url":
            urls.append(str(token.value))
            continue
        if css_type == "function" and token.lower_name == "url":
            significant = [
                item
                for item in token.arguments
                if getattr(item, "type", "") not in {"comment", "whitespace"}
            ]
            if len(significant) == 1 and getattr(significant[0], "type", "") in {
                "ident",
                "string",
                "url",
            }:
                urls.append(str(significant[0].value))
            else:
                errors.append("invalid-url")
            continue
        nested = None
        if css_type == "function":
            nested = token.arguments
        elif hasattr(token, "content") and token.content is not None:
            nested = token.content
        if nested is not None:
            nested_urls, nested_errors = css_url_values(
                list(nested),
                depth=depth + 1,
            )
            urls.extend(nested_urls)
            errors.extend(nested_errors)
    return urls, errors


def scan_css_resources(
    content: str,
    logical: str,
    owner: Path,
    context: Context,
    allowed_fonts: set[Path],
) -> list[Any]:
    findings: list[Any] = []
    try:
        rules = tinycss2.parse_stylesheet(
            content,
            skip_comments=True,
            skip_whitespace=True,
        )
    except (RecursionError, TypeError, ValueError):
        return [css_resource_diagnostic(logical, "parser-failure")]
    for rule in rules:
        if rule.type == "error":
            findings.append(css_resource_diagnostic(logical, "parse-error"))
            continue
        if rule.type == "at-rule" and rule.lower_at_keyword == "import":
            findings.append(css_resource_diagnostic(logical, "import"))
        tokens = list(getattr(rule, "prelude", ()) or ())
        tokens.extend(list(getattr(rule, "content", ()) or ()))
        try:
            urls, errors = css_url_values(tokens)
        except (RecursionError, TypeError, ValueError):
            findings.append(css_resource_diagnostic(logical, "parser-failure"))
            continue
        findings.extend(
            css_resource_diagnostic(logical, category) for category in errors
        )
        for value in urls:
            try:
                target = resolve_url(context.root, owner, value)
            except ResourceUrlError as error:
                findings.append(
                    {
                        **error.diagnostic,
                        "context": "css-resource",
                        "path": logical,
                    }
                )
                continue
            if target not in allowed_fonts:
                findings.append(
                    {
                        **resource_url_diagnostic(
                            value,
                            "undeclared-font-resource",
                        ),
                        "context": "css-resource",
                        "path": logical,
                    }
                )
    return findings


def embedded_image_ok(value: str) -> bool:
    matched = re.fullmatch(
        r"data:(image/(?:png|jpeg));base64,([A-Za-z0-9+/]*={0,2})",
        value,
        flags=re.IGNORECASE,
    )
    if matched is None:
        return False
    try:
        content = base64.b64decode(matched.group(2), validate=True)
    except (binascii.Error, ValueError):
        return False
    if not content or len(content) > MAX_EMBEDDED_IMAGE_BYTES:
        return False
    media_type = matched.group(1).casefold()
    return (
        media_type == "image/png" and content.startswith(b"\x89PNG\r\n\x1a\n")
    ) or (
        media_type == "image/jpeg" and content.startswith(b"\xff\xd8")
        and content.endswith(b"\xff\xd9")
    )


def svg_reference_ok(value: str, identifiers: set[str]) -> bool:
    if value.startswith("#"):
        return len(value) > 1 and value[1:] in identifiers
    return embedded_image_ok(value)


def split_xml_name(value: str) -> tuple[str, str]:
    if value.startswith("{"):
        namespace, local = value[1:].split("}", maxsplit=1)
        return namespace, local.casefold()
    return "", value.casefold()


def source_svg_resource_values(value: str) -> tuple[list[str], list[str]]:
    try:
        tokens = tinycss2.parse_component_value_list(value)
        return css_url_values(tokens)
    except (RecursionError, TypeError, ValueError):
        return [], ["parser-failure"]


def inspect_source_svg(
    path: Path,
    logical: str,
) -> tuple[float, float, list[Any]]:
    try:
        content = path.read_bytes()
    except OSError:
        return 0.0, 0.0, [source_svg_diagnostic(logical, "read-error")]
    findings: list[Any] = []
    if re.search(br"<!DOCTYPE|<!ENTITY", content, flags=re.IGNORECASE):
        findings.append(source_svg_diagnostic(logical, "unsafe-declaration"))
    without_declaration = re.sub(
        br"^(?:\xef\xbb\xbf)?\s*<\?xml\b[^?]*\?>",
        b"",
        content,
        count=1,
        flags=re.IGNORECASE,
    )
    if b"<?" in without_declaration:
        findings.append(source_svg_diagnostic(logical, "processing-instruction"))
    try:
        root = fromstring(content)
    except (ParseError, DefusedXmlException):
        return 0.0, 0.0, [
            *findings,
            source_svg_diagnostic(logical, "xml-parse-error"),
        ]
    if root.tag != f"{{{SVG_NAMESPACE}}}svg":
        findings.append(source_svg_diagnostic(logical, "wrong-root"))
    width_match = SVG_LENGTH.fullmatch(root.attrib.get("width", ""))
    height_match = SVG_LENGTH.fullmatch(root.attrib.get("height", ""))
    width = float(width_match.group(1)) if width_match else 0.0
    height = float(height_match.group(1)) if height_match else 0.0
    if (
        not math.isfinite(width)
        or not math.isfinite(height)
        or width <= 0
        or height <= 0
    ):
        findings.append(source_svg_diagnostic(logical, "invalid-geometry"))
        width = height = 0.0
    if not same_numbers(
        parse_numbers(root.attrib.get("viewBox")),
        [0.0, 0.0, width, height],
    ):
        findings.append(source_svg_diagnostic(logical, "invalid-viewbox"))
    identifiers = {
        value
        for element in root.iter()
        if isinstance(element.tag, str)
        and isinstance((value := element.attrib.get("id")), str)
        and value
    }
    for element in root.iter():
        if not isinstance(element.tag, str):
            continue
        _namespace, local = split_xml_name(element.tag)
        if local in SVG_ACTIVE_ELEMENTS:
            findings.append(source_svg_diagnostic(logical, f"active-{local}"))
        if local in SVG_ANIMATION_ELEMENTS:
            findings.append(source_svg_diagnostic(logical, "animation"))
        for raw_name, value in element.attrib.items():
            namespace, name = split_xml_name(raw_name)
            qualified = f"xlink:{name}" if namespace.endswith("/xlink") else name
            if name.startswith("on"):
                findings.append(source_svg_diagnostic(logical, "event-attribute"))
            if qualified in SVG_RESOURCE_ATTRIBUTES:
                if not svg_reference_ok(value, identifiers):
                    findings.append(source_svg_diagnostic(logical, "nonlocal-resource"))
                continue
            if qualified == "style" or qualified in SVG_URL_PRESENTATION_ATTRIBUTES:
                urls, errors = source_svg_resource_values(value)
                if errors:
                    findings.append(source_svg_diagnostic(logical, "css-resource-parse"))
                if any(not svg_reference_ok(url, identifiers) for url in urls):
                    findings.append(source_svg_diagnostic(logical, "nonlocal-resource"))
        if local == "style" and element.text:
            try:
                rules = tinycss2.parse_stylesheet(
                    element.text,
                    skip_comments=True,
                    skip_whitespace=True,
                )
            except (RecursionError, TypeError, ValueError):
                findings.append(source_svg_diagnostic(logical, "css-resource-parse"))
                continue
            for rule in rules:
                if rule.type == "error" or (
                    rule.type == "at-rule" and rule.lower_at_keyword == "import"
                ):
                    findings.append(source_svg_diagnostic(logical, "css-resource-parse"))
                tokens = list(getattr(rule, "prelude", ()) or ())
                tokens.extend(list(getattr(rule, "content", ()) or ()))
                urls, errors = css_url_values(tokens)
                if errors:
                    findings.append(source_svg_diagnostic(logical, "css-resource-parse"))
                if any(not svg_reference_ok(url, identifiers) for url in urls):
                    findings.append(source_svg_diagnostic(logical, "nonlocal-resource"))
    unique = {canonical_json(item): item for item in findings}
    return width, height, [unique[key] for key in sorted(unique)]


def audit_passive(context: Context) -> PassiveAudit:
    errors: list[Any] = []
    binding_errors: list[Any] = []
    allowed_fonts = {
        confined(context.root, font["asset"]["path"])
        for font in context.manifest["fonts"]
    }
    css_records = [context.manifest["outputs"]["css"], *context.manifest["stylesheets"]]
    scanned_css: set[str] = set()
    for record in css_records:
        logical = manifest_path(record["path"])
        if logical in scanned_css:
            continue
        scanned_css.add(logical)
        owner = confined(context.root, logical)
        try:
            content = owner.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            errors.append(
                text_read_diagnostic(
                    logical,
                    "generated-css" if logical == context.css.path else "retained-stylesheet",
                    error,
                )
            )
            continue
        errors.extend(
            scan_css_resources(
                content,
                logical,
                owner,
                context,
                allowed_fonts,
            )
        )
    source_svgs: dict[str, tuple[float, float]] = {}
    for figure in context.manifest["figures"]:
        for part in figure["parts"]:
            logical = manifest_path(part["source_svg"]["path"])
            if logical not in source_svgs:
                width, height, findings = inspect_source_svg(
                    confined(context.root, logical),
                    logical,
                )
                source_svgs[logical] = (width, height)
                errors.extend(findings)
            if not bbox_within_source(part["bbox"], source_svgs[logical]):
                binding_errors.append(
                    f"crop {part['id']} bbox is outside its source SVG viewBox"
                )
    return PassiveAudit(errors, binding_errors, source_svgs)


def browser_candidates() -> list[Path]:
    candidates: list[Path] = []
    configured = os.environ.get("SCHOLARLY_PUBLICATION_BROWSER")
    if configured:
        candidates.append(Path(configured))
    if os.name == "nt":
        for variable in ("PROGRAMFILES(X86)", "PROGRAMFILES", "LOCALAPPDATA"):
            root = os.environ.get(variable)
            if root:
                candidates.extend([
                    Path(root) / "Microsoft" / "Edge" / "Application" / "msedge.exe",
                    Path(root) / "Google" / "Chrome" / "Application" / "chrome.exe",
                ])
    for name in ("msedge", "microsoft-edge", "google-chrome", "chromium"):
        resolved = shutil.which(name)
        if resolved:
            candidates.append(Path(resolved))
    unique: list[Path] = []
    seen: set[Path] = set()
    for candidate in candidates:
        try:
            resolved = candidate.expanduser().resolve(strict=True)
        except OSError:
            continue
        if resolved not in seen and stat.S_ISREG(resolved.stat().st_mode):
            seen.add(resolved)
            unique.append(resolved)
    return unique
def file_url_path(value: str) -> Path | None:
    parsed = urlsplit(value)
    if parsed.scheme.casefold() != "file" or parsed.netloc not in {
        "",
        "localhost",
    } or parsed.query or parsed.fragment:
        return None
    try:
        converted = url2pathname(parsed.path)
    except (UnicodeError, ValueError):
        return None
    if os.name == "nt" and re.match(r"^/[A-Za-z]:", converted):
        converted = converted[1:]
    return Path(converted).resolve()
def dom_expectations(context: Context) -> dict[str, Any]:
    return {
        "stylesheet_url": context.css.file.as_uri(),
        "crop_sources": {
            part["id"]: confined(
                context.root,
                part["source_svg"]["path"],
            ).as_uri()
            for figure in context.manifest["figures"]
            for part in figure["parts"]
        },
        "url_attributes": sorted(DOM_URL_ATTRIBUTES),
        "active_elements": sorted(OBSERVED_ACTIVE_ELEMENTS),
    }


DOM_SCRIPT = r"""
(payload) => {
 const observationLimit = 25;
 const localName = element => (element?.localName || "").toLowerCase();
 const norm = value => (value || "").normalize("NFC").replace(/\s+/gu, " ").trim();
 const contextVisible = element => {
   if (!element || !element.isConnected) return false;
   for (let current = element; current; current = current.parentElement) {
     const style = getComputedStyle(current);
     const opacity = Number.parseFloat(style.opacity);
     if (current.hidden || style.display === "none" ||
         style.visibility === "hidden" || style.visibility === "collapse" ||
         style.contentVisibility === "hidden" ||
         (Number.isFinite(opacity) && opacity <= 0)) return false;
   }
   return true;
 };
 const elementVisible = element => {
   if (!contextVisible(element)) return false;
   const rect = element.getBoundingClientRect();
   return Number.isFinite(rect.width) && Number.isFinite(rect.height) &&
          rect.width > 0 && rect.height > 0;
 };
 const textVisible = node => {
   if (!node.parentElement || !contextVisible(node.parentElement)) return false;
   if (!/\S/u.test(node.data)) return true;
   const range = document.createRange();
   range.selectNodeContents(node);
   return [...range.getClientRects()].some(rect =>
     Number.isFinite(rect.width) && Number.isFinite(rect.height) &&
     rect.width > 0 && rect.height > 0);
 };
 const visibleText = (root, omitFigures = false) => {
   if (!root) return "";
   const parts = [];
   const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
   while (walker.nextNode()) {
     const node = walker.currentNode;
     if (omitFigures && node.parentElement?.closest("figure[data-figure-id]")) continue;
     if (textVisible(node)) parts.push(node.data);
   }
   return norm(parts.join(""));
 };
 const resolved = (value, base) => {
   try { return new URL(value, base).href; } catch { return null; }
 };
 const effectiveLanguage = element => {
   for (let current = element; current; current = current.parentElement) {
     if (current.hasAttribute("lang")) return current.getAttribute("lang");
   }
   return null;
 };
 const root = document.documentElement;
 const body = document.body;
 const mains = [...document.querySelectorAll("main")];
 const main = mains.length === 1 ? mains[0] : null;
 const ids = [...document.querySelectorAll("[id]")].map(element => element.id);
 const duplicates = [...new Set(ids.filter((value, index) => ids.indexOf(value) !== index))];
 const fragmentNodes = [...document.querySelectorAll("[data-fragment-id]")];
 const fragments = fragmentNodes.map(node => ({
   id: node.getAttribute("data-fragment-id"),
   tag: node.localName,
   under_main: !!main && main.contains(node),
   visible_text: visibleText(node, true),
 }));
 const mainFragmentOrder = main
   ? [...main.querySelectorAll("[data-fragment-id]")].map(node => node.getAttribute("data-fragment-id"))
   : [];
 const outsideText = [];
 const textSegments = [];
 if (body) {
   const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT);
   while (walker.nextNode()) {
     const node = walker.currentNode;
     const text = norm(node.data);
     if (!text || !textVisible(node)) continue;
     textSegments.push({
       text,
       leading: /^\s/u.test(node.data),
       trailing: /\s$/u.test(node.data),
     });
     if (!node.parentElement?.closest("[data-fragment-id]")) {
       outsideText.push({tag: node.parentElement?.localName || null, id: node.parentElement?.id || null});
     }
   }
 }
 const mediaSelector = "img,svg,image,video,audio,canvas,object,embed,iframe";
 const outsideMedia = body ? [...body.querySelectorAll(mediaSelector)]
   .filter(element => elementVisible(element) && !element.closest("[data-fragment-id]"))
   .map(element => ({tag: element.localName, id: element.id || null})) : [];
 const figures = [...document.querySelectorAll("figure")].map((figure, index) => ({
   id: figure.getAttribute("data-figure-id"),
   dom_id: figure.id || null,
   aria_label: figure.getAttribute("aria-label"),
   order: index,
   fragment_id: figure.closest("[data-fragment-id]")?.getAttribute("data-fragment-id") || null,
 }));
 const dataFigureNodes = [...document.querySelectorAll("[data-figure-id]")].map(node => ({
   tag: node.localName,
   id: node.getAttribute("data-figure-id"),
 }));
 const captions = [...document.querySelectorAll("figcaption")].map(caption => ({
   owner: caption.closest("figure")?.getAttribute("data-figure-id") || null,
   visible: elementVisible(caption),
   text: visibleText(caption),
 }));
 const crops = [...document.querySelectorAll("[data-crop-id]")].map((crop, index) => {
   const images = [...crop.querySelectorAll("image,img")];
   const image = images.length === 1 ? images[0] : null;
   const rect = crop.getBoundingClientRect();
   const viewBox = crop.viewBox?.baseVal;
   const href = image?.href?.baseVal ?? image?.getAttribute("href") ?? image?.getAttribute("xlink:href");
   return {
     id: crop.getAttribute("data-crop-id"),
     tag: crop.localName,
     order: index,
     owner: crop.closest("figure")?.getAttribute("data-figure-id") || null,
     fragment_id: crop.closest("[data-fragment-id]")?.getAttribute("data-fragment-id") || null,
     role: crop.getAttribute("role"),
     aria_label: crop.getAttribute("aria-label"),
     view_box: viewBox ? [viewBox.x, viewBox.y, viewBox.width, viewBox.height] : null,
     image_count: images.length,
     image_url: href ? resolved(href, image.baseURI) : null,
     visible: elementVisible(crop),
     rect: {width: rect.width, height: rect.height},
   };
 });
 const svgNodes = [...document.querySelectorAll("svg")].map(node => node.getAttribute("data-crop-id"));
 const imageNodes = [...document.querySelectorAll("image,img")].map(node => ({
   tag: localName(node),
   crop_id: node.closest("[data-crop-id]")?.getAttribute("data-crop-id") || null,
 }));
 const active = [...document.querySelectorAll("*")]
   .map(element => localName(element))
   .filter(name => payload.active_elements.includes(name));
 const eventAttributes = [];
 const inlineStyles = [];
 const urlViolations = [];
 const expectedCropSources = new Map(Object.entries(payload.crop_sources));
 for (const element of document.querySelectorAll("*")) {
   const tag = localName(element);
   for (const attribute of element.attributes) {
     const name = attribute.name.toLowerCase();
     if (name.startsWith("on")) eventAttributes.push({tag, attribute: name});
     if (name === "style") inlineStyles.push({tag});
     if (!payload.url_attributes.includes(name)) continue;
     const value = attribute.value;
     let allowed = false;
     if (tag === "a" && name === "href" && value.startsWith("#") && value.length > 1) {
       const target = resolved(value, element.baseURI);
       const current = new URL(document.location.href);
       const candidate = target ? new URL(target) : null;
       let identifier = "";
       try { identifier = decodeURIComponent(candidate?.hash.slice(1) || ""); } catch {}
       allowed = !!candidate && candidate.origin === current.origin &&
         candidate.pathname === current.pathname && candidate.search === current.search &&
         !!identifier && !!document.getElementById(identifier);
     } else if (tag === "link" && name === "href" &&
                element.relList.contains("stylesheet")) {
       allowed = resolved(value, element.baseURI) === payload.stylesheet_url;
     } else if (tag === "image" && (name === "href" || name === "xlink:href")) {
       const cropId = element.closest("[data-crop-id]")?.getAttribute("data-crop-id");
       allowed = !!cropId && resolved(value, element.baseURI) === expectedCropSources.get(cropId);
     }
     if (!allowed) urlViolations.push({tag, attribute: name, value});
   }
 }
 const httpEquivMetadata = [];
 const httpEquivNodes = [...document.querySelectorAll("meta[http-equiv]")];
 for (const meta of httpEquivNodes.slice(0, observationLimit)) {
   httpEquivMetadata.push({
     value: (meta.getAttribute("http-equiv") || "").trim().toLowerCase(),
   });
 }
 const stylesheetLinks = [...document.querySelectorAll("link[rel~='stylesheet']")]
   .map(link => resolved(link.getAttribute("href") || "", link.baseURI));
 const loadedStylesheets = [...document.styleSheets].map(sheet => sheet.href);
 const pageRules = [];
 const stylesheetErrors = [];
 const isPageRule = rule =>
   rule?.constructor?.name === "CSSPageRule" || rule?.type === 6;
 const nestedRules = rule => {
   try { return rule.cssRules || null; } catch { return undefined; }
 };
 const containsPageRules = (rules, depth = 0) => {
   if (!rules || depth > 32) return depth > 32;
   for (const rule of rules) {
     if (isPageRule(rule)) return true;
     const nested = nestedRules(rule);
     if (nested === undefined || containsPageRules(nested, depth + 1)) return true;
   }
   return false;
 };
 const mediaMatches = condition => {
   const query = (condition || "").trim();
   return !query || matchMedia(query).matches;
 };
 const walkRules = rules => {
   for (const rule of rules) {
     if (isPageRule(rule)) {
       const declarations = [];
       for (let index = 0; index < rule.style.length; index++) {
         const name = rule.style.item(index);
         declarations.push({
           name: name.toLowerCase(),
           value: rule.style.getPropertyValue(name).trim(),
           priority: rule.style.getPropertyPriority(name).toLowerCase(),
         });
       }
       pageRules.push({selector: (rule.selectorText || "").trim(), declarations});
       continue;
     }
     const nested = nestedRules(rule);
     if (nested === undefined) {
       stylesheetErrors.push("cssom-group-access");
       continue;
     }
     if (!nested) continue;
     const kind = rule.constructor?.name || "";
     if (kind === "CSSMediaRule") {
       try {
         if (mediaMatches(rule.conditionText || rule.media?.mediaText)) {
           walkRules(nested);
         }
       } catch {
         if (containsPageRules(nested)) stylesheetErrors.push("indeterminate-media-page-group");
       }
       continue;
     }
     if (kind === "CSSSupportsRule") {
       try {
         if (!rule.conditionText || typeof CSS?.supports !== "function") {
           throw new Error("unsupported CSS supports observation");
         }
         if (CSS.supports(rule.conditionText)) walkRules(nested);
       } catch {
         if (containsPageRules(nested)) stylesheetErrors.push("indeterminate-supports-page-group");
       }
       continue;
     }
     if (kind === "CSSLayerBlockRule") {
       walkRules(nested);
       continue;
     }
     if (containsPageRules(nested)) stylesheetErrors.push("unknown-page-group");
   }
 };
 for (const sheet of document.styleSheets) {
   if (sheet.disabled) continue;
   let rules = null;
   try { rules = sheet.cssRules; } catch {
     stylesheetErrors.push("cssom-access");
     continue;
   }
   try {
     if (!mediaMatches(sheet.media?.mediaText)) continue;
   } catch {
     if (containsPageRules(rules)) stylesheetErrors.push("indeterminate-stylesheet-media");
     continue;
   }
   walkRules(rules);
 }
 const overflowElements = [];
 if (body) {
   for (const element of body.querySelectorAll("*")) {
     const crop = element.closest("svg[data-crop-id]");
     if (crop && crop !== element) continue;
     const rect = element.getBoundingClientRect();
     if (rect.left < -0.5 || rect.right > root.clientWidth + 0.5) {
       overflowElements.push({tag: localName(element), id: element.id || null, left: rect.left, right: rect.right});
       if (overflowElements.length >= 25) break;
     }
   }
 }
 return {
   stylesheet: {
     links: stylesheetLinks,
     loaded: loadedStylesheets,
     inline_count: document.querySelectorAll("style").length,
     cssom_errors: stylesheetErrors,
   },
   main: {count: mains.length, id: main?.id || null},
   language: {
     document: effectiveLanguage(root),
     title: effectiveLanguage(document.querySelector("title")),
   },
   title: norm(document.title),
   duplicates,
   fragments,
   main_fragment_order: mainFragmentOrder,
   outside_text: outsideText,
   outside_media: outsideMedia,
   figures,
   data_figure_nodes: dataFigureNodes,
   captions,
   crops,
   svg_nodes: svgNodes,
   image_nodes: imageNodes,
   active,
   event_attributes: eventAttributes,
   inline_styles: inlineStyles,
   character_set: document.characterSet || null,
   http_equiv_metadata: httpEquivMetadata,
   http_equiv_metadata_count: httpEquivNodes.length,
   http_equiv_metadata_truncated: httpEquivNodes.length > httpEquivMetadata.length,
   url_violations: urlViolations,
   page_rules: pageRules,
   text_segments: textSegments,
   geometry: {
     client: root.clientWidth,
     scroll: root.scrollWidth,
     overflow: root.scrollWidth > root.clientWidth + 1 || overflowElements.length > 0,
     elements: overflowElements,
   },
 };
}
"""


async def launch_browser(playwright: Any, browser_path: Path | None) -> Any:
    options: dict[str, Any] = {
        "headless": True,
        "args": ["--allow-file-access-from-files"],
    }
    if browser_path:
        path = browser_path.resolve()
        if not path.is_file():
            raise AuditError(f"browser executable is missing: {path}")
        options["executable_path"] = str(path)
    elif path := next((item for item in browser_candidates() if item.is_file()), None):
        options["executable_path"] = str(path)
    elif Path(playwright.chromium.executable_path).is_file():
        options["executable_path"] = playwright.chromium.executable_path
    elif os.name == "nt":
        options["channel"] = "msedge"
    return await playwright.chromium.launch(**options)


def browser_resource_roles(context: Context) -> dict[Path, set[str]]:
    roles: dict[Path, set[str]] = {}

    def allow(path: Path, resource_type: str) -> None:
        roles.setdefault(path.resolve(), set()).add(resource_type)

    allow(context.html.file, "document")
    allow(context.css.file, "stylesheet")
    for font in context.manifest["fonts"]:
        allow(confined(context.root, font["asset"]["path"]), "font")
    for figure in context.manifest["figures"]:
        for part in figure["parts"]:
            allow(confined(context.root, part["source_svg"]["path"]), "image")
    return roles


async def render_once(
    browser: Any,
    context: Context,
    output: Path,
    evidence_root: Path,
) -> Render:
    blocked: set[str] = set()
    failed: set[str] = set()
    aborted: set[int] = set()
    allowed = browser_resource_roles(context)
    width_points = PAGE_POINTS[context.manifest["print_geometry"]["page_size"]][0]
    margins = context.manifest["print_geometry"]["margin_in"]
    printable = (width_points / POINTS_PER_INCH - margins["left"] - margins["right"]) * PX_PER_INCH
    probe = max(1, round(printable))
    browser_context = None
    output.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=output.parent,
        prefix=f".{output.name}.",
    )
    os.close(descriptor)
    temporary = Path(temporary_name)

    async def route_request(route: Any, request: Any) -> None:
        value = request.url
        path = file_url_path(value)
        if value.strip().casefold() == "about:blank" or (
            path is not None
            and request.resource_type in allowed.get(path, set())
        ):
            await route.continue_()
        else:
            aborted.add(id(request))
            blocked.add(value)
            await route.abort("blockedbyclient")

    try:
        async with asyncio.timeout(RENDER_TIMEOUT_MS / 1000):
            browser_context = await browser.new_context(
                viewport={"width": probe, "height": 960},
                service_workers="block",
                java_script_enabled=False,
            )
            await browser_context.route("**/*", route_request)
            browser_context.on(
                "requestfailed",
                lambda request: failed.add(request.url)
                if id(request) not in aborted
                else None,
            )
            page = await browser_context.new_page()
            await page.goto(
                context.html.file.as_uri(),
                wait_until="load",
                timeout=NAVIGATION_TIMEOUT_MS,
            )
            await page.emulate_media(media="print")
            await page.evaluate("document.fonts.ready")
            dom = cast(
                "dict[str, Any]",
                await page.evaluate(DOM_SCRIPT, dom_expectations(context)),
            )
            pdf_bytes = await page.pdf(
                print_background=True,
                prefer_css_page_size=True,
                display_header_footer=False,
                page_ranges=f"1-{MAX_PDF_PAGES + 1}",
            )
        temporary.write_bytes(pdf_bytes)
        try:
            pdf_page_count(temporary, "generated browser PDF")
        except PublicationError as error:
            raise AuditError(str(error)) from error
        temporary.replace(output)
    except TimeoutError as error:
        raise AuditError(
            "Chromium render exceeded the fixed "
            f"{RENDER_TIMEOUT_MS} ms deadline"
        ) from error
    finally:
        temporary.unlink(missing_ok=True)
        if browser_context is not None:
            await browser_context.close()
    observed = dom["geometry"]
    return Render(
        relative_asset(output, evidence_root),
        {
            "blocked_requests": sorted(blocked),
            "failed_requests": sorted(failed),
        },
        {
            "printable_width_css_px": round(printable, 6),
            "probe_width_css_px": probe,
            "client_width_css_px": int(observed["client"]),
            "scroll_width_css_px": float(observed["scroll"]),
            "horizontal_overflow": bool(observed["overflow"]),
            "overflow_elements": observed["elements"],
        },
        dom,
    )
def render_pair(
    context: Context,
    render_root: Path,
    evidence_root: Path,
    browser_path: Path | None,
) -> tuple[Render, Render]:
    try:
        from playwright.async_api import Error as PlaywrightError
        from playwright.async_api import TimeoutError as PlaywrightTimeoutError
        from playwright.async_api import async_playwright
    except ImportError as error:
        raise AuditError("Playwright is unavailable; run with uv run --script") from error

    async def render() -> tuple[Render, Render]:
        async with async_playwright() as playwright:
            browser = await launch_browser(playwright, browser_path)
            try:
                first = await render_once(
                    browser,
                    context,
                    render_root / "render-1.pdf",
                    evidence_root,
                )
                second = await render_once(
                    browser,
                    context,
                    render_root / "render-2.pdf",
                    evidence_root,
                )
                return first, second
            finally:
                await browser.close()

    try:
        return asyncio.run(render())
    except (PlaywrightError, PlaywrightTimeoutError) as error:
        raise AuditError(f"Chromium rendering failed: {error}") from error
def normalize_font(value: str) -> str:
    return unicodedata.normalize("NFC", re.sub(r"^[A-Z]{6}\+", "", value)).casefold().strip()
def cjk_line_join_character(value: str) -> bool:
    codepoint = ord(value)
    return any(start <= codepoint <= end for start, end in CJK_LINE_JOIN_RANGES)
def prepare_text_search(value: str) -> str:
    value = unicodedata.normalize("NFC", value.replace("\u00ad", ""))
    return re.sub(
        r"(?<=\w)-[ \t]*(?:\r\n|\r|\n)[ \t]*(?=\w)",
        "",
        value,
    )
def text_search_key(value: str) -> str:
    return " ".join(prepare_text_search(value).split())
def pdf_searchable_text(raw_pages: list[str]) -> str:
    value = prepare_text_search(
        "\n".join(
            PDF_TERMINAL_LINE_SEPARATOR.sub("", page, count=1)
            for page in raw_pages
        )
    )

    def line_separator(match: re.Match[str]) -> str:
        left = value[match.start() - 1] if match.start() else ""
        right = value[match.end()] if match.end() < len(value) else ""
        if (
            left
            and right
            and cjk_line_join_character(left)
            and cjk_line_join_character(right)
        ):
            return "\n"
        return " "

    return re.sub(
        r"[^\S\n]+",
        " ",
        PDF_LINE_SEPARATOR.sub(line_separator, value),
    ).strip()
def declared_font_sets(
    manifest: dict[str, Any],
) -> tuple[set[str], list[set[str]]]:
    all_names: set[str] = set()
    by_family: dict[str, set[str]] = {}
    for font in manifest["fonts"]:
        family = font["family"]
        safe_family = not css_unsafe_categories(family)
        names = {
            normalized
            for value in (
                family if safe_family else None,
                font["postscript_name"],
                font["full_name"],
            )
            if isinstance(value, str) and value and (normalized := normalize_font(value))
        }
        all_names.update(names)
        if safe_family:
            by_family.setdefault(family.casefold(), set()).update(names)
    role_sets = [
        by_family[key]
        for family in manifest["font_roles"].values()
        if not css_unsafe_categories(family)
        and (key := family.casefold()) in by_family
    ]
    return all_names, role_sets
def font_observation(
    document: Any,
    raw: tuple[Any, ...],
    declared: set[str],
) -> tuple[dict[str, Any], dict[str, Any]]:
    xref, font_type, base_name = int(raw[0]), str(raw[2]), str(raw[3])
    try:
        embedded = xref > 0 and bool(document.extract_font(xref)[-1])
    except RuntimeError:
        embedded = False
    record = {
        "base_name": base_name,
        "type": font_type,
        "embedded": embedded,
        "subset": re.match(r"^[A-Z]{6}\+", base_name) is not None,
        "type3": "type3" in font_type.casefold(),
        "declared": normalize_font(base_name) in declared,
    }
    return record, {
        **record,
        "extension": str(raw[1]),
        "resource_name": str(raw[4]),
        "encoding": str(raw[5]),
    }
def pdf_actions(document: Any, fitz: Any) -> dict[str, Any]:
    kinds = {
        fitz.LINK_NONE: "none", fitz.LINK_GOTO: "goto", fitz.LINK_URI: "uri",
        fitz.LINK_LAUNCH: "launch", fitz.LINK_GOTOR: "goto-remote",
    }
    safe_kinds = {"none", "goto"}
    unsafe_kinds: set[str] = set()
    witnesses: dict[bytes, dict[str, Any]] = {}
    target_samples: dict[bytes, dict[str, Any]] = {}

    def observe(
        kind: str,
        source_category: str,
        sample: dict[str, Any] | None = None,
        subtype: str | None = None,
    ) -> None:
        if kind in safe_kinds:
            return
        unsafe_kinds.add(kind)
        witness: dict[str, Any] = {
            "kind": kind,
            "source_category": source_category,
        }
        if subtype is not None:
            content = diagnostic_bytes(subtype)
            witness.update(
                {
                    "subtype_characters": len(subtype),
                    "subtype_bytes": len(content),
                    "subtype_sha256": hash_bytes(content),
                }
            )
        witnesses[canonical_json(witness)] = witness
        if sample is not None:
            target_samples[canonical_json(sample)] = sample

    for page_number in range(document.page_count):
        for link in document.load_page(page_number).get_links():
            raw_kind = link.get("kind")
            if raw_kind == fitz.LINK_NAMED:
                # PyMuPDF conflates direct /Dest forms with /Named actions here.
                # The bounded low-level traversal below owns both classifications.
                continue
            kind = kinds.get(raw_kind, "invalid-action")
            target_category, target = next(
                (
                    (category, str(link[key]))
                    for key, category in (
                        ("uri", "uri"),
                        ("file", "file"),
                        ("name", "name"),
                    )
                    if link.get(key) is not None
                ),
                (None, None),
            )
            observe(
                kind,
                "page-link",
                action_target_sample(
                    kind,
                    page_number + 1,
                    target,
                    target_category,
                )
                if target is not None and target_category is not None
                else None,
            )
    action_names = {
        "GoTo": "goto", "GoToR": "goto-remote", "ImportData": "import-data", "JavaScript": "javascript",
        "Launch": "launch", "Named": "named", "SubmitForm": "submit-form", "URI": "uri",
    }
    mupdf = fitz.mupdf
    pdf = mupdf.pdf_document_from_fz_document(document.this)

    def destination_page(raw: Any) -> int | None:
        if raw is None:
            return None
        item = mupdf.pdf_resolve_indirect(raw)
        if item is None or mupdf.pdf_is_null(item):
            return None
        if mupdf.pdf_is_name(item) or mupdf.pdf_is_string(item):
            item = mupdf.pdf_resolve_indirect(
                mupdf.pdf_lookup_dest(pdf, item)
            )
            if item is None or mupdf.pdf_is_null(item):
                return None
            if mupdf.pdf_is_dict(item):
                item = mupdf.pdf_resolve_indirect(
                    mupdf.pdf_dict_gets(item, "D")
                )
        if (
            item is None
            or not mupdf.pdf_is_array(item)
            or mupdf.pdf_array_len(item) < 1
        ):
            return None
        page = mupdf.pdf_array_get(item, 0)
        if not mupdf.pdf_is_indirect(page):
            return None
        page_number = mupdf.pdf_lookup_page_number(pdf, page)
        return (
            page_number
            if type(page_number) is int
            and 0 <= page_number < document.page_count
            else None
        )

    visited = 0
    def enter(item: Any) -> Any | None:
        nonlocal visited
        if item is None or mupdf.pdf_is_null(item):
            return None
        item = mupdf.pdf_resolve_indirect(item)
        if item is None or mupdf.pdf_is_null(item):
            return None
        visited += 1
        if visited > MAX_PDF_OBJECTS:
            raise PublicationError("PDF object graph exceeds the action-inspection limit")
        return item

    def inspect_action(item: Any, source_category: str) -> Any:
        next_action = mupdf.pdf_dict_gets(item, "Next")
        subtype = mupdf.pdf_resolve_indirect(mupdf.pdf_dict_gets(item, "S"))
        if subtype is None or not mupdf.pdf_is_name(subtype):
            observe("invalid-action", source_category)
            return next_action
        name = str(mupdf.pdf_to_name(subtype))
        kind = action_names.get(name)
        if kind is None:
            observe("unknown-action", source_category, subtype=name)
        elif kind == "goto" and destination_page(
            mupdf.pdf_dict_gets(item, "D")
        ) is None:
            observe("invalid-action", source_category)
        else:
            observe(kind, source_category)
        return next_action

    def visit_action(raw: Any, source_category: str, *, arrays: bool = False) -> None:
        pending = [(raw, arrays)]
        while pending:
            candidate, allow_array = pending.pop()
            item = enter(candidate)
            if item is None:
                continue
            if mupdf.pdf_is_array(item):
                if allow_array:
                    pending.extend(
                        (mupdf.pdf_array_get(item, index), False)
                        for index in range(mupdf.pdf_array_len(item))
                    )
                continue
            if not mupdf.pdf_is_dict(item):
                continue
            pending.append((inspect_action(item, source_category), True))

    def visit_open_action(raw: Any) -> None:
        item = enter(raw)
        if item is None:
            return
        if mupdf.pdf_is_dict(item):
            visit_action(inspect_action(item, "catalog"), "catalog", arrays=True)
        elif (
            mupdf.pdf_is_array(item)
            or mupdf.pdf_is_name(item)
            or mupdf.pdf_is_string(item)
        ):
            if destination_page(item) is None:
                observe("invalid-action", "catalog")
        else:
            observe("invalid-action", "catalog")

    def visit_destination(raw: Any, source_category: str) -> None:
        if (
            raw is not None
            and not mupdf.pdf_is_null(raw)
            and destination_page(raw) is None
        ):
            observe("invalid-action", source_category)

    def visit_aa(raw: Any, source_category: str) -> None:
        item = enter(raw)
        if item is not None and mupdf.pdf_is_dict(item):
            for index in range(mupdf.pdf_dict_len(item)):
                visit_action(mupdf.pdf_dict_get_val(item, index), source_category)

    def visit_tree(
        raw: Any,
        child_keys: tuple[str, ...],
        source_category: str,
        *,
        direct_destinations: bool = False,
    ) -> None:
        pending = [raw]
        while pending:
            item = enter(pending.pop())
            if item is None:
                continue
            if mupdf.pdf_is_array(item):
                pending.extend(mupdf.pdf_array_get(item, index) for index in range(mupdf.pdf_array_len(item)))
                continue
            if not mupdf.pdf_is_dict(item):
                continue
            visit_action(mupdf.pdf_dict_gets(item, "A"), source_category)
            visit_aa(mupdf.pdf_dict_gets(item, "AA"), source_category)
            if direct_destinations:
                visit_destination(
                    mupdf.pdf_dict_gets(item, "Dest"),
                    source_category,
                )
            pending.extend(mupdf.pdf_dict_gets(item, key) for key in child_keys)

    def visit_javascript_names(raw: Any) -> None:
        pending = [raw]
        while pending:
            item = enter(pending.pop())
            if item is None or not mupdf.pdf_is_dict(item):
                continue
            names = enter(mupdf.pdf_dict_gets(item, "Names"))
            if names is not None and mupdf.pdf_is_array(names):
                for index in range(1, mupdf.pdf_array_len(names), 2):
                    visit_action(
                        mupdf.pdf_array_get(names, index),
                        "javascript-name-tree",
                    )
            kids = enter(mupdf.pdf_dict_gets(item, "Kids"))
            if kids is not None and mupdf.pdf_is_array(kids):
                pending.extend(mupdf.pdf_array_get(kids, index) for index in range(mupdf.pdf_array_len(kids)))

    catalog = enter(mupdf.pdf_dict_gets(mupdf.pdf_trailer(pdf), "Root"))
    if catalog is not None and mupdf.pdf_is_dict(catalog):
        visit_open_action(mupdf.pdf_dict_gets(catalog, "OpenAction"))
        visit_aa(mupdf.pdf_dict_gets(catalog, "AA"), "catalog")
        outlines = enter(mupdf.pdf_dict_gets(catalog, "Outlines"))
        if outlines is not None and mupdf.pdf_is_dict(outlines):
            visit_tree(
                mupdf.pdf_dict_gets(outlines, "First"),
                ("First", "Next"),
                "outline",
                direct_destinations=True,
            )
        form = enter(mupdf.pdf_dict_gets(catalog, "AcroForm"))
        if form is not None and mupdf.pdf_is_dict(form):
            visit_tree(mupdf.pdf_dict_gets(form, "Fields"), ("Kids",), "form-field")
        names = enter(mupdf.pdf_dict_gets(catalog, "Names"))
        if names is not None and mupdf.pdf_is_dict(names):
            visit_javascript_names(mupdf.pdf_dict_gets(names, "JavaScript"))
    for page_number in range(document.page_count):
        page = mupdf.pdf_load_object(pdf, document.load_page(page_number).xref)
        visit_aa(mupdf.pdf_dict_gets(page, "AA"), "page")
        annots = enter(mupdf.pdf_dict_gets(page, "Annots"))
        if annots is not None and mupdf.pdf_is_array(annots):
            for index in range(mupdf.pdf_array_len(annots)):
                annot = enter(mupdf.pdf_array_get(annots, index))
                if annot is not None and mupdf.pdf_is_dict(annot):
                    visit_action(mupdf.pdf_dict_gets(annot, "A"), "annotation")
                    visit_aa(mupdf.pdf_dict_gets(annot, "AA"), "annotation")
                    visit_destination(
                        mupdf.pdf_dict_gets(annot, "Dest"),
                        "annotation",
                    )

    ordered_kinds = sorted(unsafe_kinds)
    ordered_witnesses = [witnesses[key] for key in sorted(witnesses)]
    ordered_samples = [target_samples[key] for key in sorted(target_samples)]
    return {
        "unsafe_detected": bool(unsafe_kinds),
        "unsafe_kinds": ordered_kinds[:MAX_DIAGNOSTIC_ITEMS],
        "unsafe_kinds_truncated": len(ordered_kinds) > MAX_DIAGNOSTIC_ITEMS,
        "witnesses": ordered_witnesses[:MAX_DIAGNOSTIC_ITEMS],
        "witnesses_truncated": len(ordered_witnesses) > MAX_DIAGNOSTIC_ITEMS,
        "target_samples": ordered_samples[:MAX_DIAGNOSTIC_ITEMS],
        "target_samples_truncated": len(ordered_samples) > MAX_DIAGNOSTIC_ITEMS,
    }

def box_values(box: Any) -> list[float]:
    return [round(float(value), 3) for value in (box.x0, box.y0, box.x1, box.y1)]
def page_box_ok(box: list[float], width: float, height: float, scale: float = 1.0) -> bool:
    return (
        len(box) == 4
        and all(math.isfinite(value) for value in box)
        and box[2] > box[0]
        and box[3] > box[1]
        and abs((box[2] - box[0]) * scale - width) <= PDF_TOLERANCE
        and abs((box[3] - box[1]) * scale - height) <= PDF_TOLERANCE
    )


def admit_pdf_document(document: Any, path: Path, context: str) -> int:
    if (
        not document.is_pdf
        or document.needs_pass
        or document.is_encrypted
        or document.xref_get_key(-1, "Encrypt") != ("null", "null")
        or document.page_count < 1
    ):
        raise PublicationError(
            f"{context} is not an inspectable, unencrypted, nonempty PDF: {path}"
        )
    if document.page_count > MAX_PDF_PAGES:
        raise PublicationError(
            f"{context} exceeds the fixed {MAX_PDF_PAGES}-page ceiling: {path}"
        )
    return int(document.page_count)


def pdf_page_count(path: Path, context: str) -> int:
    try:
        if path.stat().st_size < 1:
            raise PublicationError(f"{context} is empty: {path}")
    except OSError as error:
        raise AuditError(f"cannot inspect {context}: {error}") from error
    try:
        import fitz
    except ImportError as error:
        raise AuditError("PyMuPDF is unavailable; run with uv run --script") from error
    try:
        document = fitz.open(path)
    except fitz.FileDataError as error:
        raise PublicationError(f"cannot inspect {context} {path}: {error}") from error
    except (OSError, RuntimeError, ValueError) as error:
        raise AuditError(f"cannot open {context} {path}: {error}") from error
    try:
        return admit_pdf_document(document, path, context)
    finally:
        document.close()


def inspect_pdf(path: Path, logical: str, page_size: str, raster_dir: Path, raster_source: str,
                evidence_root: Path, manifest: dict[str, Any]) -> Pdf:
    try:
        import fitz
    except ImportError as error:
        raise AuditError("PyMuPDF is unavailable; run with uv run --script") from error
    width, height = PAGE_POINTS[page_size]
    declared, role_sets = declared_font_sets(manifest)
    try:
        document = fitz.open(path)
    except fitz.FileDataError as error:
        raise PublicationError(f"cannot inspect PDF {path}: {error}") from error
    except (OSError, RuntimeError, ValueError) as error:
        raise AuditError(f"cannot open PDF {path}: {error}") from error
    try:
        page_count = admit_pdf_document(document, path, "file")
        pages: list[dict[str, Any]] = []
        details: list[dict[str, Any]] = []
        rasters: list[dict[str, Any]] = []
        fonts: dict[tuple[int, str, str], dict[str, Any]] = {}
        font_details: dict[tuple[int, str, str], dict[str, Any]] = {}
        texts: list[str] = []
        raw_texts: list[str] = []
        replacements = 0
        pdf_document = fitz.mupdf.pdf_document_from_fz_document(document.this)
        raster_dir.mkdir(parents=True, exist_ok=True)
        for index in range(page_count):
            page = document.load_page(index)
            media, crop = box_values(page.mediabox), box_values(page.cropbox)
            effective = box_values(page.rect)
            page_object = fitz.mupdf.pdf_load_object(pdf_document, page.xref)
            unit_object = fitz.mupdf.pdf_resolve_indirect(fitz.mupdf.pdf_dict_gets_inheritable(page_object, "UserUnit"))
            user_unit = float(fitz.mupdf.pdf_to_real(unit_object)) if unit_object is not None and fitz.mupdf.pdf_is_number(unit_object) else 1.0
            media_ok, crop_ok = (
                page_box_ok(media, width, height, user_unit),
                page_box_ok(crop, width, height, user_unit),
            )
            effective_ok = page_box_ok(effective, width, height)
            effective_width, effective_height = float(page.rect.width), float(page.rect.height)
            unsafe_size = (
                not math.isfinite(effective_width) or not math.isfinite(effective_height)
                or effective_width <= 0 or effective_height <= 0
            )
            raster_area = 0 if unsafe_size else math.ceil(effective_width * RASTER_SCALE) * math.ceil(effective_height * RASTER_SCALE)
            if unsafe_size or raster_area > width * height * RASTER_SCALE**2 * MAX_RASTER_AREA_FACTOR:
                raise PublicationError(  # noqa: TRY301
                    f"PDF page {index + 1} geometry is too large to rasterize safely: {path}"
                )
            raw_text = cast("str", page.get_text("text"))
            text = normalize_text(raw_text)
            texts.append(text)
            raw_texts.append(raw_text)
            replacements += text.count("\ufffd")
            for raw in page.get_fonts(full=True):
                record, detail = font_observation(document, raw, declared)
                key = (int(raw[0]), record["base_name"], record["type"])
                fonts[key], font_details[key] = record, detail
            png = page.get_pixmap(matrix=fitz.Matrix(RASTER_SCALE, RASTER_SCALE), alpha=False, annots=True).tobytes("png")
            raster_path = raster_dir / f"page-{index + 1:04d}.png"
            raster_path.write_bytes(png)
            raster = relative_asset(raster_path, evidence_root)
            rasters.append(
                {
                    "source": raster_source,
                    "page": index + 1,
                    "asset": stable_asset(raster, EVIDENCE_PATH_BASE),
                }
            )
            page_record = {
                "page": index + 1,
                "width": round(effective_width, 3),
                "height": round(effective_height, 3),
                "rotation": int(page.rotation),
                "text_sha256": hash_bytes(text.encode()),
                "raster_sha256": hash_bytes(png),
                "size_matches": media_ok and crop_ok and effective_ok and page.rotation == 0,
            }
            pages.append(page_record)
            details.append(
                {
                    **page_record,
                    "media_box": media,
                    "crop_box": crop,
                    "effective_box": effective,
                    "user_unit": user_unit,
                    "media_box_matches": media_ok,
                    "crop_box_matches": crop_ok,
                    "effective_box_matches": effective_ok,
                }
            )
        actions = pdf_actions(document, fitz)
        ordered_keys = sorted(fonts, key=lambda item: (item[1], item[0], item[2]))
        observed_font_names = {normalize_font(fonts[key]["base_name"]) for key in ordered_keys}
        pdf_asset = asset(path, logical)
        evidence = {
            "asset": pdf_asset.json(),
            "page_count": page_count,
            "pages": pages,
            "fonts": [fonts[key] for key in ordered_keys],
            "actions": actions,
            "type3_fonts": [fonts[key]["base_name"] for key in ordered_keys if fonts[key]["type3"]],
            "text_characters": sum(1 for text in texts for character in text if not character.isspace()),
            "replacement_characters": replacements,
        }
        detail = {
            "pages": details,
            "fonts": [font_details[key] for key in ordered_keys],
            "normalized_text": texts,
            "searchable_text": pdf_searchable_text(raw_texts),
            "raw_sha256": pdf_asset.sha256,
            "missing_roles": [sorted(names) for names in role_sets if not names & observed_font_names],
        }
        return Pdf(evidence, detail, rasters)
    except PublicationError:
        raise
    except (RuntimeError, ValueError) as error:
        raise PublicationError(f"cannot inspect PDF {path}: {error}") from error
    finally:
        document.close()

def css_length_inches(value: str) -> float | None:
    matched = re.fullmatch(
        r"\s*([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)\s*([A-Za-z]+)\s*",
        value,
    )
    if matched is None:
        return 0.0 if value.strip() == "0" else None
    try:
        number = float(matched.group(1))
    except (OverflowError, ValueError):
        return None
    factor = CSS_LENGTH_INCHES.get(matched.group(2).casefold())
    if factor is None or not math.isfinite(number):
        return None
    return number * factor


def expand_margin(value: str) -> dict[str, str] | None:
    parts = value.split()
    if not 1 <= len(parts) <= 4:
        return None
    if len(parts) == 1:
        top = right = bottom = left = parts[0]
    elif len(parts) == 2:
        top = bottom = parts[0]
        right = left = parts[1]
    elif len(parts) == 3:
        top, right, bottom = parts
        left = right
    else:
        top, right, bottom, left = parts
    return {
        "margin-top": top,
        "margin-right": right,
        "margin-bottom": bottom,
        "margin-left": left,
    }


def page_binding(
    rules: list[dict[str, Any]],
    context: Context,
) -> tuple[list[str], dict[str, Any]]:
    errors: list[str] = []
    qualified = [rule.get("selector") for rule in rules if rule.get("selector")]
    unqualified = [rule for rule in rules if not rule.get("selector")]
    if len(unqualified) != 1:
        errors.append("CSSOM must have exactly one active unqualified @page rule")
    if qualified:
        errors.append("CSSOM has active competing qualified @page rules")
    effective: dict[str, tuple[str, bool]] = {}

    def assign(name: str, value: str, important: bool) -> None:
        current = effective.get(name)
        if current is None or important or not current[1]:
            effective[name] = (value, important)

    for rule in unqualified if len(unqualified) == 1 else []:
        for declaration in rule.get("declarations", []):
            name = str(declaration.get("name", "")).casefold()
            value = str(declaration.get("value", "")).strip()
            important = declaration.get("priority") == "important"
            if name == "margin":
                expanded = expand_margin(value)
                if expanded is None:
                    errors.append("CSSOM effective @page margin is not a fixed length shorthand")
                    continue
                for side, side_value in expanded.items():
                    assign(side, side_value, important)
            elif name in {
                "size",
                "margin-top",
                "margin-right",
                "margin-bottom",
                "margin-left",
            }:
                assign(name, value, important)
    expected_size = PAGE_CSS_NAMES[context.manifest["print_geometry"]["page_size"]]
    observed_size = effective.get("size", ("", False))[0]
    if len(unqualified) == 1 and observed_size.casefold() != expected_size.casefold():
        errors.append("CSSOM effective @page size does not match the manifest")
    expected_margins = context.manifest["print_geometry"]["margin_in"]
    observed_margins: dict[str, float | None] = {}
    for side in ("top", "right", "bottom", "left"):
        value = effective.get(f"margin-{side}", ("", False))[0]
        observed = css_length_inches(value)
        observed_margins[side] = (
            round(observed, 6) if observed is not None else None
        )
        if len(unqualified) == 1 and (
            observed is None
            or abs(observed - float(expected_margins[side])) > 0.0001
        ):
            errors.append(f"CSSOM effective @page {side} margin does not match the manifest")
    return errors, {
        "active_unqualified_rules": len(unqualified),
        "active_qualified_rules": len(qualified),
        "effective_size": observed_size,
        "effective_margin_in": observed_margins,
    }


def dom_errors(
    dom: dict[str, Any],
    context: Context,
) -> tuple[list[Any], list[Any], dict[str, Any]]:
    errors: list[Any] = []
    bindings: list[Any] = []
    manifest = context.manifest
    expected_stylesheet = context.css.file.as_uri()
    stylesheet = dom.get("stylesheet", {})
    if (
        stylesheet.get("links") != [expected_stylesheet]
        or stylesheet.get("loaded") != [expected_stylesheet]
        or stylesheet.get("inline_count") != 0
        or stylesheet.get("cssom_errors")
    ):
        errors.append("canonical stylesheet was not the one exclusively loaded stylesheet")
    if dom.get("main") != {
        "count": 1,
        "id": manifest["publication_id"],
    }:
        errors.append("browser must expose one main with the manifest publication ID")
    if dom.get("language") != {
        "document": manifest["document"]["language"],
        "title": manifest["document"]["title_language"],
    }:
        errors.append("browser document or title language does not match the manifest")
    if dom.get("title") != normalize_text(manifest["document"]["title"]):
        errors.append("browser title does not match the manifest")
    if dom.get("duplicates"):
        errors.append(f"browser DOM duplicate IDs: {dom['duplicates']}")
    if dom.get("active"):
        errors.append({"kind": "active-elements", "elements": dom["active"]})
    if dom.get("event_attributes"):
        errors.append({"kind": "event-attributes", "items": dom["event_attributes"]})
    if dom.get("inline_styles"):
        errors.append({"kind": "inline-styles", "items": dom["inline_styles"]})
    character_set = str(dom.get("character_set") or "")
    if character_set.casefold() != "utf-8":
        errors.append(
            {
                "kind": "effective-character-set",
                "expected": "UTF-8",
                "observed": character_set or None,
            }
        )
    http_equiv_count = dom.get("http_equiv_metadata_count", 0)
    if http_equiv_count:
        errors.append(
            {
                "kind": "http-equiv-metadata",
                "count": http_equiv_count,
                "samples": dom.get("http_equiv_metadata", []),
                "truncated": bool(
                    dom.get("http_equiv_metadata_truncated")
                ),
            }
        )
    for violation in dom.get("url_violations", []):
        value = str(violation.get("value", ""))
        errors.append(
            {
                **resource_url_diagnostic(value, "dormant-dom-url"),
                "element": violation.get("tag"),
                "attribute": violation.get("attribute"),
            }
        )
    expected_fragment_ids = [item["id"] for item in manifest["fragments"]]
    fragments = dom.get("fragments", [])
    observed_fragment_ids = [item.get("id") for item in fragments]
    if observed_fragment_ids != expected_fragment_ids or dom.get("main_fragment_order") != expected_fragment_ids:
        errors.append("manifest fragments are not exactly once under main in manifest order")
    if any(not item.get("under_main") for item in fragments):
        errors.append("browser has fragment nodes outside main")
    for expected in manifest["fragments"]:
        matches = [item for item in fragments if item.get("id") == expected["id"]]
        if len(matches) != 1:
            continue
        observed_hash = hash_bytes(
            normalize_text(str(matches[0].get("visible_text", ""))).encode()
        )
        if observed_hash != expected["visible_text_sha256"]:
            errors.append(f"browser fragment {expected['id']} visible-text hash mismatch")
    if dom.get("outside_text") or dom.get("outside_media"):
        errors.append(
            {
                "kind": "visible-content-outside-fragments",
                "text_nodes": dom.get("outside_text", []),
                "media_nodes": dom.get("outside_media", []),
            }
        )
    page_errors, page_summary = page_binding(dom.get("page_rules", []), context)
    errors.extend(page_errors)

    expected_figures = manifest["figures"]
    expected_figure_ids = [figure["id"] for figure in expected_figures]
    figures = dom.get("figures", [])
    data_figure_nodes = dom.get("data_figure_nodes", [])
    if [item.get("id") for item in figures] != expected_figure_ids:
        bindings.append("browser figure order or cardinality differs from the manifest")
    if data_figure_nodes != [
        {"tag": "figure", "id": identifier}
        for identifier in expected_figure_ids
    ]:
        bindings.append("browser has unbound or mistyped data-figure-id nodes")
    captions = dom.get("captions", [])
    expected_parts = [
        (figure, part)
        for figure in expected_figures
        for part in figure["parts"]
    ]
    crops = dom.get("crops", [])
    expected_crop_ids = [part["id"] for _figure, part in expected_parts]
    if [item.get("id") for item in crops] != expected_crop_ids:
        bindings.append("browser crop order or cardinality differs from the manifest")
    if dom.get("svg_nodes") != expected_crop_ids:
        bindings.append("browser has unbound SVG nodes")
    image_nodes = dom.get("image_nodes", [])
    if len(image_nodes) != len(expected_crop_ids) or [
        item.get("crop_id") for item in image_nodes
    ] != expected_crop_ids or any(item.get("tag") != "image" for item in image_nodes):
        bindings.append("browser has unbound crop image nodes")

    figure_summaries: list[dict[str, Any]] = []
    crop_summaries: list[dict[str, Any]] = []
    figure_fragments: dict[str, str | None] = {}
    for expected in expected_figures:
        matches = [item for item in figures if item.get("id") == expected["id"]]
        caption_matches = False
        label_matches = False
        if len(matches) == 1:
            observed = matches[0]
            figure_fragments[expected["id"]] = observed.get("fragment_id")
            if observed.get("dom_id") != expected["dom_id"]:
                bindings.append(f"browser figure {expected['id']} DOM ID mismatch")
            if not observed.get("fragment_id"):
                bindings.append(f"browser figure {expected['id']} is outside a bound fragment")
            label_matches = normalize_text(
                str(observed.get("aria_label") or "")
            ) == normalize_text(expected["alt"])
            if not label_matches:
                bindings.append(f"browser figure {expected['id']} aria-label mismatch")
            owned_captions = [
                item for item in captions if item.get("owner") == expected["id"]
            ]
            caption_matches = (
                len(owned_captions) == 1
                and owned_captions[0].get("visible") is True
                and owned_captions[0].get("text") == caption_text(expected["caption_html"])
            )
            if not caption_matches:
                bindings.append(f"browser figure {expected['id']} caption mismatch")
        figure_summaries.append(
            {
                "id": expected["id"],
                "matches": len(matches),
                "accessible_label_matches": label_matches,
                "caption_matches": caption_matches,
            }
        )
    if len(captions) != len(expected_figures):
        bindings.append("browser has unbound figcaption nodes")
    for figure, part in expected_parts:
        matches = [item for item in crops if item.get("id") == part["id"]]
        source_matches = viewbox_matches = geometry_matches = label_matches = False
        if len(matches) == 1:
            observed = matches[0]
            if observed.get("tag") != "svg" or observed.get("owner") != figure["id"]:
                bindings.append(f"browser crop {part['id']} ownership mismatch")
            if observed.get("fragment_id") != figure_fragments.get(figure["id"]):
                bindings.append(f"browser crop {part['id']} fragment ownership mismatch")
            label_matches = (
                observed.get("role") == "img"
                and normalize_text(str(observed.get("aria_label") or ""))
                == normalize_text(f"{figure['alt']} - part {part['order']}")
            )
            if not label_matches:
                bindings.append(f"browser crop {part['id']} role or aria-label mismatch")
            bbox = manifest_bbox(part["bbox"])
            if bbox is not None:
                expected_viewbox = [
                    bbox[0],
                    bbox[1],
                    bbox[2] - bbox[0],
                    bbox[3] - bbox[1],
                ]
                viewbox_matches = same_numbers(
                    observed.get("view_box"),
                    expected_viewbox,
                )
                rect = observed.get("rect", {})
                try:
                    width = float(rect.get("width", 0))
                    height = float(rect.get("height", 0))
                except (TypeError, ValueError):
                    width = height = 0.0
                geometry_matches = (
                    observed.get("visible") is True
                    and same_aspect_ratio(
                        width,
                        height,
                        expected_viewbox[2],
                        expected_viewbox[3],
                    )
                )
            source_matches = (
                observed.get("image_count") == 1
                and file_url_path(str(observed.get("image_url") or ""))
                == confined(context.root, part["source_svg"]["path"])
            )
            if not viewbox_matches:
                bindings.append(f"browser crop {part['id']} viewBox mismatch")
            if not geometry_matches:
                bindings.append(f"browser crop {part['id']} rendered geometry mismatch")
            if not source_matches:
                bindings.append(f"browser crop {part['id']} source mismatch")
        crop_summaries.append(
            {
                "id": part["id"],
                "figure_id": figure["id"],
                "matches": len(matches),
                "accessible_label_matches": label_matches,
                "source_matches": source_matches,
                "viewbox_matches": viewbox_matches,
                "geometry_matches": geometry_matches,
            }
        )
    summary = {
        "document": {
            "main_id": dom.get("main", {}).get("id"),
            "fragment_ids": observed_fragment_ids,
            "duplicate_ids": dom.get("duplicates", []),
            "character_set": character_set or None,
            "http_equiv_metadata_count": http_equiv_count,
            "page_binding": page_summary,
        },
        "figures": figure_summaries,
        "crops": crop_summaries,
    }
    return errors, bindings, summary


def pdf_signature(report: Pdf, field: str) -> list[Any]:
    if field == "geometry":
        return [
            {"effective_box": page["effective_box"], "rotation": page["rotation"]}
            for page in report.detail["pages"]
        ]
    return [page[field] for page in report.evidence["pages"]]
def pdf_findings(
    reports: dict[str, Pdf],
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    geometry: dict[str, Any] = {}
    fonts: dict[str, Any] = {}
    behavior: dict[str, Any] = {}
    counts = {name: report.evidence["page_count"] for name, report in reports.items()}
    if len(set(counts.values())) != 1:
        geometry["page_counts"] = counts
    for name, report in reports.items():
        bad_pages = [page for page in report.detail["pages"] if not page["size_matches"]]
        if bad_pages:
            geometry[name] = bad_pages
        font_items = report.evidence["fonts"]
        font_record = {
            "font_count": len(font_items),
            "unembedded": [item["base_name"] for item in font_items if not item["embedded"] and not item["type3"]],
            "unsubset": [item["base_name"] for item in font_items if not item["subset"] and not item["type3"]],
            "undeclared": [item["base_name"] for item in font_items if not item["declared"]],
            "missing_roles": report.detail["missing_roles"],
        }
        if (
            not font_items
            or font_record["unembedded"]
            or font_record["unsubset"]
            or font_record["undeclared"]
            or font_record["missing_roles"]
        ):
            fonts[name] = font_record
        actions = report.evidence["actions"]
        behavior_record = {
            "actions": {
                "unsafe_detected": actions["unsafe_detected"],
                "unsafe_kinds": actions["unsafe_kinds"],
            },
            "type3_fonts": report.evidence["type3_fonts"],
            "text_characters": report.evidence["text_characters"],
            "replacement_characters": report.evidence["replacement_characters"],
        }
        if (
            actions["unsafe_detected"]
            or behavior_record["type3_fonts"]
            or behavior_record["replacement_characters"] != 0
        ):
            behavior[name] = behavior_record
    return geometry, fonts, behavior

def text_match_projection(
    value: str,
) -> tuple[str, list[int], list[int]]:
    characters: list[str] = []
    boundaries: list[int] = []
    offsets: list[int] = []
    boundary = MATCH_BOUNDARY_NONE
    for offset, character in enumerate(value):
        if character == " ":
            boundary = MATCH_BOUNDARY_SPACE
        elif character == "\n":
            boundary = MATCH_BOUNDARY_AMBIGUOUS
        else:
            if characters:
                boundaries.append(boundary)
            characters.append(character)
            offsets.append(offset)
            boundary = MATCH_BOUNDARY_NONE
    return "".join(characters), boundaries, offsets

def find_text_match(
    content: str,
    content_boundaries: list[int],
    needle: str,
    needle_boundaries: list[int],
    start: int,
    work_limit: int,
) -> tuple[tuple[int, int] | None, int]:
    work = 0
    search_start = start
    while search_start < len(content):
        candidate = content.find(needle, search_start)
        if candidate < 0:
            work += len(content) - search_start
            if work > work_limit:
                raise PublicationError(
                    "PDF text correspondence exceeds its linear work allowance"
                )
            return None, work
        work += candidate - search_start + len(needle)
        matched = True
        for index, expected in enumerate(needle_boundaries):
            work += 1
            observed = content_boundaries[candidate + index]
            if observed not in (MATCH_BOUNDARY_AMBIGUOUS, expected):
                matched = False
                break
        if work > work_limit:
            raise PublicationError(
                "PDF text correspondence exceeds its linear work allowance"
            )
        if matched:
            return (candidate, candidate + len(needle)), work
        search_start = candidate + 1
    return None, work

def missing_text_segments(report: Pdf, segments: list[dict[str, Any]]) -> list[str]:
    content = report.detail["searchable_text"]
    content_characters, content_boundaries, content_offsets = text_match_projection(
        content
    )
    whitespace_offsets: list[int] = [
        index
        for index, character in enumerate(content)
        if character.isspace()
    ]
    prepared: list[tuple[dict[str, Any], str, str, list[int]]] = []
    for segment in segments:
        text = str(segment.get("text", ""))
        needle = text_search_key(text)
        if needle:
            needle_characters, needle_boundaries, _ = text_match_projection(
                needle
            )
            prepared.append(
                (segment, text, needle_characters, needle_boundaries)
            )
    work_remaining = TEXT_MATCH_WORK_FACTOR * (
        len(content)
        + sum(len(text) for _, text, _, _ in prepared)
        + 1
    )
    cursor = 0
    character_cursor = 0
    missing: list[str] = []
    previous_trailing = False
    for segment, text, needle_characters, needle_boundaries in prepared:
        needs_space = cursor > 0 and (previous_trailing or bool(segment.get("leading")))
        search_start = character_cursor
        if needs_space:
            whitespace_index = bisect_left(whitespace_offsets, cursor)
            if whitespace_index == len(whitespace_offsets):
                match = None
                work = 0
            else:
                search_start = bisect_right(
                    content_offsets,
                    whitespace_offsets[whitespace_index],
                )
                match, work = find_text_match(
                    content_characters,
                    content_boundaries,
                    needle_characters,
                    needle_boundaries,
                    search_start,
                    work_remaining,
                )
        else:
            match, work = find_text_match(
                content_characters,
                content_boundaries,
                needle_characters,
                needle_boundaries,
                search_start,
                work_remaining,
            )
        work_remaining -= work
        if match is None:
            missing.append(hash_bytes(normalize_text(text).encode()))
        else:
            character_cursor = match[1]
            cursor = content_offsets[match[1] - 1] + 1
        previous_trailing = bool(segment.get("trailing"))
    return missing
def add_check(
    checks: list[dict[str, Any]],
    identifier: str,
    passed: bool,
    message: str,
    evidence: Any,
) -> None:
    checks.append(
        {
            "id": identifier,
            "severity": "blocking",
            "passed": passed,
            "message": message,
            "evidence": bounded_diagnostic(evidence),
        }
    )

def unchanged(before: TreeSnapshot, after: TreeSnapshot) -> bool:
    return (
        before.inventory_sha256 == after.inventory_sha256
        and before.fingerprint_sha256 == after.fingerprint_sha256
        and before.symlinks == after.symlinks
        and before.special == after.special
    )
def request_failure_summary(
    requests: dict[str, list[str]],
) -> dict[str, dict[str, Any]]:
    summary: dict[str, dict[str, Any]] = {}
    for category in ("blocked_requests", "failed_requests"):
        values = requests[category]
        if values:
            summary[category] = {
                "count": len(values),
                "sha256": hash_bytes(canonical_json(values)),
            }
    return summary
def build_evidence(
    context: Context,
    passive: PassiveAudit,
    renders: tuple[Render, Render],
    reports: dict[str, Pdf],
    after: TreeSnapshot,
) -> dict[str, Any]:
    first, second = renders
    first_dom, first_bindings, first_summary = dom_errors(first.dom, context)
    second_dom, second_bindings, second_summary = dom_errors(second.dom, context)
    request_errors = {
        f"render_{index}": summary
        for index, render in enumerate(renders, 1)
        if (summary := request_failure_summary(render.requests))
    }
    html_errors = [*passive.errors, *first_dom, *second_dom]
    geometry_errors, font_errors, behavior_errors = pdf_findings(reports)
    first_segments = cast(
        "list[dict[str, Any]]",
        first.dom.get("text_segments", []),
    )
    second_segments = cast(
        "list[dict[str, Any]]",
        second.dom.get("text_segments", []),
    )
    if first_segments != second_segments:
        html_errors.append("browser visible-text observations differ between renders")
    for name, report in reports.items():
        missing = missing_text_segments(report, first_segments)
        if missing:
            behavior_errors.setdefault(name, {})[
                "missing_expected_text_sha256"
            ] = missing
    behavior_observations = {
        name: {
            "actions": report.evidence["actions"],
            "type3_fonts": report.evidence["type3_fonts"],
            "text_characters": report.evidence["text_characters"],
            "replacement_characters": report.evidence[
                "replacement_characters"
            ],
        }
        for name, report in reports.items()
    }
    overflow = {
        f"render_{index}": render.geometry
        for index, render in enumerate(renders, 1)
        if render.geometry["horizontal_overflow"]
        or abs(
            render.geometry["client_width_css_px"]
            - render.geometry["probe_width_css_px"]
        )
        > 1
    }
    binding_errors = [
        *passive.binding_errors,
        *first_bindings,
        *second_bindings,
    ]
    first_figures = {
        item["id"]: item for item in first_summary["figures"]
    }
    second_figures = {
        item["id"]: item for item in second_summary["figures"]
    }
    first_crops = {item["id"]: item for item in first_summary["crops"]}
    second_crops = {item["id"]: item for item in second_summary["crops"]}
    figures: list[dict[str, Any]] = []
    crops: list[dict[str, Any]] = []
    for figure in context.manifest["figures"]:
        first_figure = first_figures.get(figure["id"], {})
        second_figure = second_figures.get(figure["id"], {})
        figures.append(
            {
                "id": figure["id"],
                "dom_id": figure["dom_id"],
                "source_label": figure["source_label"],
                "profile": figure["profile"],
                "embedded_language_inventory": figure[
                    "embedded_language_inventory"
                ],
                "render_matches": [
                    first_figure.get("matches", 0),
                    second_figure.get("matches", 0),
                ],
                "accessible_labels_match": all(
                    item.get("accessible_label_matches") is True
                    for item in (first_figure, second_figure)
                ),
                "captions_match": all(
                    item.get("caption_matches") is True
                    for item in (first_figure, second_figure)
                ),
            }
        )
        for part in figure["parts"]:
            first_crop = first_crops.get(part["id"], {})
            second_crop = second_crops.get(part["id"], {})
            width, height = passive.source_svgs.get(
                part["source_svg"]["path"],
                (0.0, 0.0),
            )
            bbox_confined = bbox_within_source(
                part["bbox"],
                (width, height),
            )
            crops.append(
                {
                    "id": part["id"],
                    "figure_id": figure["id"],
                    "pdf_page": part["pdf_page"],
                    "source_svg_sha256": part["source_svg"]["sha256"],
                    "source_bbox_confined": bbox_confined,
                    "render_matches": [
                        first_crop.get("matches", 0),
                        second_crop.get("matches", 0),
                    ],
                    "accessible_labels_match": all(
                        item.get("accessible_label_matches") is True
                        for item in (first_crop, second_crop)
                    ),
                    "source_matches": all(
                        item.get("source_matches") is True
                        for item in (first_crop, second_crop)
                    ),
                    "viewbox_matches": all(
                        item.get("viewbox_matches") is True
                        for item in (first_crop, second_crop)
                    ),
                    "geometry_matches": all(
                        item.get("geometry_matches") is True
                        for item in (first_crop, second_crop)
                    ),
                }
            )
    binding_pass = (
        not binding_errors
        and all(
            item["render_matches"] == [1, 1]
            and item["accessible_labels_match"]
            and item["captions_match"]
            for item in figures
        )
        and all(
            item["source_bbox_confined"]
            and item["render_matches"] == [1, 1]
            and item["accessible_labels_match"]
            and item["source_matches"]
            and item["viewbox_matches"]
            and item["geometry_matches"]
            for item in crops
        )
    )
    canonical, render_1, render_2 = (
        reports["canonical"],
        reports["render_1"],
        reports["render_2"],
    )
    repeatability = {
        "raw_render_pdfs_equal": (
            render_1.detail["raw_sha256"] == render_2.detail["raw_sha256"]
        ),
        "render_geometry_equal": (
            pdf_signature(render_1, "geometry")
            == pdf_signature(render_2, "geometry")
        ),
        "render_text_equal": (
            pdf_signature(render_1, "text_sha256")
            == pdf_signature(render_2, "text_sha256")
        ),
        "render_rasters_equal": (
            pdf_signature(render_1, "raster_sha256")
            == pdf_signature(render_2, "raster_sha256")
        ),
        "canonical_geometry_matches": (
            pdf_signature(canonical, "geometry")
            == pdf_signature(render_1, "geometry")
            == pdf_signature(render_2, "geometry")
        ),
        "canonical_text_matches": (
            pdf_signature(canonical, "text_sha256")
            == pdf_signature(render_1, "text_sha256")
            == pdf_signature(render_2, "text_sha256")
        ),
    }
    repeatability_pass = all(
        repeatability[key]
        for key in (
            "render_geometry_equal",
            "render_text_equal",
            "render_rasters_equal",
            "canonical_geometry_matches",
            "canonical_text_matches",
        )
    )
    repeatability["raw_byte_difference_advisory"] = (
        not repeatability["raw_render_pdfs_equal"] and repeatability_pass
    )
    rasters = [item for report in reports.values() for item in report.rasters]
    expected_rasters = sum(
        report.evidence["page_count"] for report in reports.values()
    )
    raster_keys = {(item["source"], item["page"]) for item in rasters}
    raster_pass = len(rasters) == expected_rasters == len(raster_keys)
    tree_same = unchanged(context.before, after)
    checks: list[dict[str, Any]] = []
    add_check(
        checks,
        "manifest.integrity",
        not context.errors,
        (
            "Assembly manifest schema, bundled profile identity/hash, semantic "
            "asset union, retained regular-file closure, relations, and initial "
            "publication fingerprint match."
        ),
        {"findings": context.errors},
    )
    add_check(
        checks,
        "html.offline-profile",
        not html_errors and not request_errors,
        (
            "Observable offline, passive-resource, stylesheet, document-language, "
            "and semantic-content outcomes match."
        ),
        {
            "findings": html_errors,
            "request_findings": request_errors,
            "render_observations": [
                first_summary["document"],
                second_summary["document"],
            ],
        },
    )
    add_check(
        checks,
        "render.geometry-overflow",
        not geometry_errors and not overflow,
        (
            "Both fresh render DOMs fit the printable width, and canonical plus "
            "rendered PDF page count and physical geometry match the manifest."
        ),
        {
            "overflow": overflow,
            "findings": geometry_errors,
            "pdf_pages": {
                name: report.detail["pages"]
                for name, report in reports.items()
            },
        },
    )
    add_check(
        checks,
        "pdf.fonts",
        not font_errors,
        (
            "Canonical and rendered PDFs use embedded, subset, manifest-declared "
            "fonts and include the required role families."
        ),
        {
            "findings": font_errors,
            "fonts": {
                name: report.detail["fonts"]
                for name, report in reports.items()
            },
        },
    )
    add_check(
        checks,
        "pdf.actions-type3-text",
        not behavior_errors,
        (
            "Canonical and rendered PDFs have no unsafe action witnesses or "
            "Type 3 fonts and retain normalized extractable text without "
            "replacement characters."
        ),
        {
            "findings": behavior_errors,
            "observations": behavior_observations,
        },
    )
    add_check(
        checks,
        "figures.crop-bindings",
        binding_pass,
        (
            "Both renders bind every manifest figure, visible caption, crop, "
            "source SVG, source box, and viewBox exactly once; normalized raw "
            "aria-label values and rendered aspect ratios match within relative "
            "tolerance."
        ),
        {
            "findings": binding_errors,
            "figures": figures,
            "crops": crops,
        },
    )
    add_check(
        checks,
        "render.repeatability",
        repeatability_pass,
        (
            "Fresh renders agree in normalized geometry, text, and rasters; raw "
            "PDF byte inequality is advisory when normalized outcomes agree."
        ),
        repeatability,
    )
    add_check(
        checks,
        "rasters.complete",
        raster_pass,
        "Every page of the canonical PDF and both renders has one bounded full-page raster.",
        {
            "expected": expected_rasters,
            "observed": len(rasters),
            "keys": sorted(map(list, raster_keys)),
        },
    )
    add_check(
        checks,
        "publication.tree-unchanged",
        tree_same,
        "The manifest-declared publication regular-file tree is unchanged.",
        {"unchanged": tree_same},
    )
    check_counts = Counter(check["id"] for check in checks)
    if any(check_counts[identifier] != 1 for identifier in CORE_CHECK_IDS):
        raise AuditError("implementation did not emit every core check exactly once")
    status = (
        "fail"
        if any(
            check["severity"] == "blocking" and not check["passed"]
            for check in checks
        )
        else "pass"
    )
    evidence = {
        "schema_version": "1.0",
        "publication_id": context.manifest["publication_id"],
        "auditor": {
            "name": "audit_publication.py",
            "version": SCRIPT_VERSION,
            "publication_profile": {
                "id": context.profile["profile_id"],
                "schema_version": context.profile["schema_version"],
                "sha256": context.profile_sha256,
            },
        },
        "inputs": {
            "assembly_manifest": stable_asset(
                context.manifest_asset,
                PUBLICATION_PATH_BASE,
            )
        },
        "checks": checks,
        "render_pdfs": {
            "render_1": stable_asset(first.pdf, EVIDENCE_PATH_BASE),
            "render_2": stable_asset(second.pdf, EVIDENCE_PATH_BASE),
        },
        "rasters": rasters,
        "publication_tree": {
            "algorithm": TREE_ALGORITHM,
            "scope": TREE_SCOPE,
            "before": context.before.json(),
            "after": after.json(),
            "unchanged": tree_same,
        },
        "human_review": {
            "status": "required",
            "required_scope": list(HUMAN_REVIEW_SCOPE),
        },
        "mechanical_status": status,
    }
    findings = validate_schema(evidence, EVIDENCE_SCHEMA)
    if findings:
        raise AuditError(
            "generated QA evidence violates schema: " + "; ".join(findings)
        )
    return evidence


def beneath(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return True


def strictly_beneath(path: Path, root: Path) -> bool:
    try:
        relative = path.relative_to(root)
    except ValueError:
        return False
    return bool(relative.parts)


def paths_overlap(left: Path, right: Path) -> bool:
    portable_left = Path(str(left).casefold())
    portable_right = Path(str(right).casefold())
    return beneath(portable_left, portable_right) or beneath(
        portable_right,
        portable_left,
    )


def windows_path_ok(path: Path) -> bool:
    return all(
        not part.endswith((".", " "))
        for part in path.parts
        if part not in {path.anchor, path.drive, "\\", "/"}
    )


def require_fresh_root(root: Path) -> None:
    try:
        root.lstat()
    except FileNotFoundError:
        return
    except OSError as error:
        raise AuditError("cannot inspect the final review root") from error
    raise AuditError("final review root already exists; use a fresh root")


def prepare_outputs(
    evidence: Path,
    release: Path,
    rasters: Path,
    publication: Path,
) -> ReviewLayout:
    if os.name == "nt" and any(
        not windows_path_ok(path) for path in (evidence, release, rasters)
    ):
        raise AuditError(
            "QA output paths must not contain Windows components ending in a dot or space"
        )
    requested = [
        Path(os.path.abspath(path))  # noqa: PTH100 - inspect requested paths without following a stale root.
        for path in (evidence, release, rasters, publication)
    ]
    evidence, release, rasters, publication = requested
    review_root = release.parent
    renders = evidence.parent / RENDER_DIRECTORY
    require_fresh_root(review_root)
    if evidence == release:
        raise AuditError("evidence and release manifests must be distinct")
    if not strictly_beneath(evidence, review_root):
        raise AuditError("evidence must be beneath the final review root")
    if not strictly_beneath(rasters, review_root):
        raise AuditError("rasters must be beneath the final review root")
    if not strictly_beneath(renders, review_root):
        raise AuditError("independent renders must be beneath the final review root")
    if not strictly_beneath(rasters, evidence.parent):
        raise AuditError("rasters must be beneath the evidence directory")
    lexical_outputs = [evidence, release, rasters, renders]
    for index, left in enumerate(lexical_outputs):
        for right in lexical_outputs[index + 1 :]:
            if paths_overlap(left, right):
                raise AuditError("QA output paths overlap lexically")
    try:
        publication_root = publication.resolve(strict=True)
    except OSError as error:
        raise AuditError("cannot resolve the publication root") from error
    canonical_root = review_root.resolve(strict=False)

    def canonical(path: Path) -> Path:
        resolved = path.resolve(strict=False)
        if not strictly_beneath(resolved, canonical_root):
            raise AuditError("QA output resolves outside the final review root")
        return resolved

    layout = ReviewLayout(
        canonical_root,
        canonical(evidence),
        canonical(release),
        canonical(rasters),
        canonical(renders),
    )
    canonical_outputs = [
        layout.evidence,
        layout.release,
        layout.rasters,
        layout.renders,
    ]
    for index, left in enumerate(canonical_outputs):
        for right in canonical_outputs[index + 1 :]:
            if paths_overlap(left, right):
                raise AuditError("QA output paths overlap canonically")
    if paths_overlap(publication_root, layout.root) or any(
        paths_overlap(publication_root, output)
        for output in canonical_outputs
    ):
        raise AuditError("final review output must be disjoint from the publication tree")
    return layout


def make_candidate(layout: ReviewLayout) -> Path:
    layout.root.parent.mkdir(parents=True, exist_ok=True)
    return Path(
        tempfile.mkdtemp(
            prefix=f".{layout.root.name}.candidate-",
            dir=layout.root.parent,
        )
    )


def candidate_layout(layout: ReviewLayout, candidate: Path) -> ReviewLayout:
    def remap(path: Path) -> Path:
        return candidate.joinpath(*path.relative_to(layout.root).parts)

    return ReviewLayout(
        candidate,
        remap(layout.evidence),
        remap(layout.release),
        remap(layout.rasters),
        remap(layout.renders),
    )


def initialize_candidate(layout: ReviewLayout) -> None:
    layout.evidence.parent.mkdir(parents=True, exist_ok=True)
    layout.rasters.mkdir(parents=True, exist_ok=True)
    layout.renders.mkdir(parents=True, exist_ok=True)


def publish_review(candidate: Path, layout: ReviewLayout) -> None:
    try:
        candidate.rename(layout.root)
    except OSError as error:
        raise AuditError("cannot rename the completed review candidate") from error


def release_manifest(context: Context, evidence: Path, release: Path) -> dict[str, Any]:
    value = {
        "schema_version": "1.0",
        "publication_id": context.manifest["publication_id"],
        "assembly_manifest": stable_asset(context.manifest_asset, PUBLICATION_PATH_BASE),
        "qa_evidence": stable_asset(
            relative_asset(evidence, release.parent),
            RELEASE_PATH_BASE,
        ),
        "mechanical_status": "pass",
        "generator": {
            "name": "audit_publication.py",
            "version": SCRIPT_VERSION,
        },
    }
    findings = validate_schema(value, RELEASE_SCHEMA)
    if findings:
        raise AuditError("generated release manifest violates schema: " + "; ".join(findings))
    return value
def audit_candidate(
    args: argparse.Namespace,
    context: Context,
    layout: ReviewLayout,
) -> int:
    initialize_candidate(layout)
    passive = audit_passive(context)
    page_size = context.manifest["print_geometry"]["page_size"]
    canonical = inspect_pdf(
        context.pdf.file, context.pdf.path, page_size, layout.rasters / "canonical",
        "canonical", layout.evidence.parent, context.manifest,
    )
    renders = render_pair(
        context,
        layout.renders,
        layout.evidence.parent,
        args.browser,
    )
    try:
        render_1 = inspect_pdf(
            renders[0].pdf.file,
            renders[0].pdf.path,
            page_size,
            layout.rasters / "render-1",
            "render-1",
            layout.evidence.parent,
            context.manifest,
        )
        render_2 = inspect_pdf(
            renders[1].pdf.file,
            renders[1].pdf.path,
            page_size,
            layout.rasters / "render-2",
            "render-2",
            layout.evidence.parent,
            context.manifest,
        )
    except PublicationError as error:
        raise AuditError(f"cannot inspect generated browser PDF: {error}") from error
    reports = {"canonical": canonical, "render_1": render_1, "render_2": render_2}
    evidence = build_evidence(
        context,
        passive,
        renders,
        reports,
        snapshot(context.root),
    )
    write_json(layout.evidence, evidence)
    if evidence["mechanical_status"] != "pass":
        return 1
    release = release_manifest(context, layout.evidence, layout.release)
    write_json(layout.release, release)
    return 0
def audit(args: argparse.Namespace) -> int:
    if not args.render_twice:
        raise AuditError("--render-twice is required")
    try:
        manifest_file = args.assembly_manifest.resolve(strict=True)
    except OSError as error:
        raise AuditError("cannot resolve the assembly manifest") from error
    publication_root = manifest_file.parent
    final_layout = prepare_outputs(
        args.evidence, args.release_manifest, args.rasters, publication_root
    )
    context = load_context(manifest_file, args.html, args.page_size)
    candidate = make_candidate(final_layout)
    report: str | None = None
    try:
        result = audit_candidate(
            args,
            context,
            candidate_layout(final_layout, candidate),
        )
        if result == 0:
            report = json.dumps(
                {
                    "publication_id": context.manifest["publication_id"],
                    "mechanical_status": "pass",
                    "human_review": "required",
                    "evidence": str(final_layout.evidence),
                    "release_manifest": str(final_layout.release),
                },
                ensure_ascii=True,
            )
        publish_review(candidate, final_layout)
    except BaseException:
        if candidate.exists():
            try:
                shutil.rmtree(candidate)
            except OSError as cleanup_error:
                raise AuditError(
                    "QA operation failed and candidate cleanup failed; "
                    f"orphan candidate: {candidate}"
                ) from cleanup_error
        raise
    if result != 0:
        eprint("QA completed with blocking findings")
        return result
    if report is None:
        raise AuditError("passing audit did not serialize its completion report")
    print(report)
    return 0

def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description="Audit an assembled scholarly publication without modifying it.")
    result.add_argument("--html", type=Path, required=True)
    result.add_argument("--assembly-manifest", type=Path, required=True)
    result.add_argument("--evidence", type=Path, required=True)
    result.add_argument("--release-manifest", type=Path, required=True)
    result.add_argument("--rasters", type=Path, required=True)
    result.add_argument("--page-size", choices=sorted(PAGE_POINTS), required=True)
    result.add_argument("--render-twice", action="store_true")
    result.add_argument("--browser", type=Path)
    return result
def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        return audit(args)
    except (
        AuditError,
        PublicationError,
        KeyError,
        OSError,
        RuntimeError,
        TypeError,
        UnicodeError,
        ValueError,
    ) as error:
        eprint(f"error: {error}")
        return 2
    except KeyboardInterrupt:
        eprint("error: interrupted")
        return 2

# fmt: on
if __name__ == "__main__":
    raise SystemExit(main())
