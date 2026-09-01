# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "fonttools==4.60.1",
#   "html5lib==1.1",
#   "jsonschema==4.25.1",
#   "playwright==1.56.0",
#   "pymupdf==1.26.6",
#   "tinycss2==1.4.0",
# ]
# ///

# fmt: off
from __future__ import annotations

import argparse
import asyncio
import base64
import binascii
import functools
import hashlib
import importlib
import json
import math
import os
import re
import shutil
import stat
import struct
import sys
import tempfile
import unicodedata
from dataclasses import dataclass
from html import escape
from io import BytesIO
from pathlib import Path, PurePosixPath
from typing import Any, Never
from urllib.parse import urlsplit
from urllib.request import url2pathname
from xml.etree.ElementTree import Comment, Element, ParseError

import tinycss2
from defusedxml.common import DefusedXmlException
from defusedxml.ElementTree import iterparse
from fontTools.ttLib import TTFont, TTLibError
from jsonschema import Draft202012Validator

html5lib: Any = importlib.import_module("html5lib")

SCRIPT_VERSION = "0.1.0"
ASSETS = Path(__file__).resolve().parents[1] / "assets"
MANIFEST_NAME = "assembly-manifest.json"
PROFILE_NAME = "publication-profile.json"
PROFILE_ID = "scholarly-fragment-and-stylesheet-v1"
GENERATED_DOCUMENT_PROFILE = "scholarly-generated-document-v1"
GENERATED_CSS_PROFILE = "scholarly-generated-css-v1"
FONT_ROLES = ("body-cjk", "body-latin")
PAGE_SIZES = {"letter": "Letter", "a4": "A4"}
MAX_PDF_PAGES = 500
NAVIGATION_TIMEOUT_MS = 120_000
PDF_TIMEOUT_MS = 120_000
ID_PATTERN = re.compile("^[a-z0-9][a-z0-9._-]*$")
BLOCK_ID_PATTERN = re.compile("^(?P<page>pdf-[0-9]{4,})-block-[0-9]{4,}$")
LANGUAGE_PATTERN = re.compile("^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")
FIGURE_COMMENT_PATTERN = re.compile("^\\s*figure\\s*:\\s*(?P<id>[a-z0-9][a-z0-9._-]*)\\s*$")
SCHEME_PATTERN = re.compile("^[A-Za-z][A-Za-z0-9+.-]*:")
HEX_COLOR_PATTERN = re.compile("^[0-9A-Fa-f]+$")
SVG_LENGTH_PATTERN = re.compile("^\\s*(?P<number>[+-]?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)"
                                "(?:[eE][+-]?[0-9]+)?)\\s*$")
CSS_LENGTH_UNITS = frozenset({"ch", "cm", "em", "ex", "in", "mm", "pc", "pt", "px", "q", "rem"})
VOID_ELEMENTS = frozenset({"br", "col", "hr", "wbr"})
FIGURE_CONTAINERS = frozenset({
    "DOCUMENT_FRAGMENT", "address", "aside", "blockquote", "dd", "div", "li", "section", "td",
})
GENERATED_CLASSES = frozenset({"publication-figure", "figure-parts", "figure-part"})
SVG_NAMESPACE = "http://www.w3.org/2000/svg"
XLINK_NAMESPACE = "http://www.w3.org/1999/xlink"
INKSCAPE_NAMESPACE = "http://www.inkscape.org/namespaces/inkscape"
SVG_ELEMENTS = frozenset({
    "circle", "clipPath", "defs", "ellipse", "g", "image", "line", "linearGradient", "mask", "path",
    "pattern", "polygon", "polyline", "radialGradient", "rect", "stop", "svg", "symbol", "use",
})
SVG_ATTRIBUTES = frozenset({
    "clip-path", "clip-rule", "clipPathUnits", "color", "color-interpolation", "color-rendering", "cx",
    "cy", "d", "data-text", "display", "fill", "fill-opacity", "fill-rule", "fx", "fy",
    "gradientTransform", "gradientUnits", "height", "id", "image-rendering", "mask",
    "maskContentUnits", "maskUnits", "offset", "opacity", "overflow", "pathLength",
    "patternContentUnits", "patternTransform", "patternUnits", "points", "preserveAspectRatio", "r",
    "rx", "ry", "shape-rendering", "spreadMethod", "stop-color", "stop-opacity", "stroke",
    "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin",
    "stroke-miterlimit", "stroke-opacity", "stroke-width", "transform", "vector-effect", "version",
    "viewBox", "visibility", "width", "x", "x1", "x2", "y", "y1", "y2",
})
SVG_LOCAL_URL_ATTRIBUTES = frozenset({"clip-path", "fill", "mask", "stroke"})
SVG_PRESENTATION_ATTRIBUTES = frozenset({
    "clip-path", "clip-rule", "color", "color-interpolation", "color-rendering", "display", "fill",
    "fill-opacity", "fill-rule", "image-rendering", "mask", "opacity", "overflow", "shape-rendering",
    "stop-color", "stop-opacity", "stroke", "stroke-dasharray", "stroke-dashoffset",
    "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-opacity", "stroke-width",
    "vector-effect", "visibility",
})
SVG_STANDARD_BLEND_MODES = frozenset({
    "color", "color-burn", "color-dodge", "darken", "difference", "exclusion", "hard-light", "hue",
    "lighten", "luminosity", "multiply", "normal", "overlay", "saturation", "screen", "soft-light",
})
SVG_FRAGMENT_PATTERN = re.compile("^#[A-Za-z_][A-Za-z0-9_.:-]*$")
SVG_LOCAL_URL_PATTERN = re.compile('^url\\(\\s*([\'"]?)(#[A-Za-z_][A-Za-z0-9_.:-]*)\\1\\s*\\)$', re.IGNORECASE)
SVG_DATA_IMAGE_PATTERN = re.compile("^data:image/(?P<media>png|jpeg);base64,(?P<data>.*)$", re.IGNORECASE | re.DOTALL)
SVG_STYLE_PATTERN = re.compile("^mix-blend-mode\\s*:\\s*([a-z-]+)\\s*;?$", re.IGNORECASE)
SVG_EXTERNAL_SCHEME_PATTERN = re.compile("(?:^|[\\s('\\\"])(?:data|file|ftp|https?|javascript):", re.IGNORECASE)
CSS_KEYWORD_EXCLUSIVE_GROUPS = {
    "font-variant-east-asian": (
        {"jis04", "jis78", "jis83", "jis90", "simplified", "traditional"}, {"full-width", "proportional-width"},
    ),
    "font-variant-ligatures": (
        {"common-ligatures", "no-common-ligatures"}, {"contextual", "no-contextual"},
        {"discretionary-ligatures", "no-discretionary-ligatures"}, {"historical-ligatures", "no-historical-ligatures"},
    ),
    "font-variant-numeric": (
        {"lining-nums", "oldstyle-nums"}, {"proportional-nums", "tabular-nums"},
        {"diagonal-fractions", "stacked-fractions"},
    ),
}
PROFILE_PROHIBITIONS = frozenset({
    "active-content", "browser-default-hidden-content", "css-custom-properties", "css-functions",
    "css-important", "css-pseudo-elements", "css-url-values", "event-handler-attributes",
    "external-urls", "inline-style", "parser-changing-markup",
})
PROFILE_SELECTOR_SURFACE = frozenset({
    "type", "universal", "class", "id", "lang-attribute", "lang-pseudo-class", "child", "descendant",
})
ATTRIBUTE_PLAIN_LIMITS = {"abbr": 128, "aria-label": 512, "title": 512, "value": 512}
ATTRIBUTE_TOKEN_LIMITS = {
    "aria-describedby": 32, "aria-labelledby": 32, "headers": 32,
}
MARKUP_NESTING_LIMIT = 128
FIXED_ELEMENT_ATTRIBUTES = {
    "a": frozenset({"href"}),
    "abbr": frozenset(),
    "address": frozenset(),
    "aside": frozenset(),
    "b": frozenset(),
    "bdi": frozenset(),
    "bdo": frozenset(),
    "blockquote": frozenset(),
    "br": frozenset(),
    "caption": frozenset(),
    "cite": frozenset(),
    "code": frozenset(),
    "col": frozenset({"span"}),
    "colgroup": frozenset({"span"}),
    "data": frozenset({"value"}),
    "dd": frozenset(),
    "del": frozenset({"datetime"}),
    "dfn": frozenset(),
    "div": frozenset(),
    "dl": frozenset(),
    "dt": frozenset(),
    "em": frozenset(),
    "h1": frozenset(),
    "h2": frozenset(),
    "h3": frozenset(),
    "h4": frozenset(),
    "h5": frozenset(),
    "h6": frozenset(),
    "hr": frozenset(),
    "i": frozenset(),
    "ins": frozenset({"datetime"}),
    "kbd": frozenset(),
    "li": frozenset({"value"}),
    "mark": frozenset(),
    "ol": frozenset({"reversed", "start", "type"}),
    "p": frozenset(),
    "pre": frozenset(),
    "q": frozenset(),
    "rb": frozenset(),
    "rt": frozenset(),
    "rtc": frozenset(),
    "ruby": frozenset(),
    "s": frozenset(),
    "samp": frozenset(),
    "section": frozenset(),
    "small": frozenset(),
    "span": frozenset(),
    "strong": frozenset(),
    "sub": frozenset(),
    "sup": frozenset(),
    "table": frozenset(),
    "tbody": frozenset(),
    "td": frozenset({"colspan", "headers", "rowspan"}),
    "tfoot": frozenset(),
    "th": frozenset({"abbr", "colspan", "headers", "rowspan", "scope"}),
    "thead": frozenset(),
    "time": frozenset({"datetime"}),
    "tr": frozenset(),
    "u": frozenset(),
    "ul": frozenset(),
    "var": frozenset(),
    "wbr": frozenset(),
}
FIXED_GLOBAL_ATTRIBUTES = frozenset({
    "aria-describedby", "aria-label", "aria-labelledby", "class", "dir", "id", "lang", "title",
})
FIXED_ATTRIBUTE_NAMES = FIXED_GLOBAL_ATTRIBUTES | frozenset().union(*FIXED_ELEMENT_ATTRIBUTES.values())
CSS_WIDE_KEYWORDS = frozenset({"inherit", "initial", "unset"})
CSS_GENERIC_FAMILIES = frozenset({"monospace", "sans-serif", "serif"})
CSS_NAMED_COLORS = frozenset({
    "black", "currentcolor", "darkblue", "darkgreen", "darkred", "dimgray", "gray", "maroon", "navy",
})
CSS_BORDER_STYLE_PROPERTIES = frozenset({
    "border-bottom-style", "border-left-style", "border-right-style", "border-top-style",
})
CSS_BORDER_WIDTH_PROPERTIES = frozenset({
    "border-bottom-width", "border-left-width", "border-right-width", "border-top-width",
})
CSS_COLOR_PROPERTIES = frozenset({
    "border-bottom-color", "border-left-color", "border-right-color", "border-top-color", "color",
    "text-decoration-color",
})
CSS_NONNEGATIVE_LENGTH_PERCENTAGE_PROPERTIES = frozenset({
    "margin-block-end", "margin-block-start", "margin-bottom", "margin-inline-end", "margin-inline-start",
    "margin-left", "margin-right", "margin-top", "padding-block-end", "padding-block-start", "padding-bottom",
    "padding-inline-end", "padding-inline-start", "padding-left", "padding-right", "padding-top",
})
CSS_SIGNED_SPACING_PROPERTIES = frozenset({"letter-spacing", "text-underline-offset", "word-spacing"})
CSS_WIDOW_ORPHAN_PROPERTIES = frozenset({"orphans", "widows"})
CSS_ENUM_VALUES = {
    "border-collapse": frozenset({"collapse", "separate"}),
    "box-decoration-break": frozenset({"clone", "slice"}),
    "break-after": frozenset({"auto", "avoid", "avoid-page", "left", "page", "recto", "right", "verso"}),
    "break-before": frozenset({"auto", "avoid", "avoid-page", "left", "page", "recto", "right", "verso"}),
    "break-inside": frozenset({"auto", "avoid", "avoid-page"}),
    "caption-side": frozenset({"bottom", "top"}),
    "font-kerning": frozenset({"auto", "none", "normal"}),
    "font-style": frozenset({"italic", "normal"}),
    "font-variant-caps": frozenset({
        "all-small-caps", "normal", "petite-caps", "small-caps", "titling-caps", "unicase",
    }),
    "hyphens": frozenset({"auto", "manual", "none"}),
    "line-break": frozenset({"anywhere", "auto", "loose", "normal", "strict"}),
    "list-style-position": frozenset({"inside", "outside"}),
    "list-style-type": frozenset({
        "circle", "cjk-ideographic", "decimal", "decimal-leading-zero", "disc", "hiragana", "katakana",
        "lower-alpha", "lower-roman", "none", "simp-chinese-informal", "square", "trad-chinese-informal",
        "upper-alpha", "upper-roman",
    }),
    "overflow-wrap": frozenset({"anywhere", "break-word", "normal"}),
    "page-break-after": frozenset({"always", "auto", "avoid", "left", "right"}),
    "page-break-before": frozenset({"always", "auto", "avoid", "left", "right"}),
    "page-break-inside": frozenset({"auto", "avoid"}),
    "ruby-align": frozenset({"center", "space-around", "space-between", "start"}),
    "ruby-position": frozenset({"over", "under"}),
    "table-layout": frozenset({"auto", "fixed"}),
    "text-align": frozenset({"center", "end", "justify", "left", "match-parent", "right", "start"}),
    "text-align-last": frozenset({"auto", "center", "end", "justify", "left", "right", "start"}),
    "text-decoration-style": frozenset({"dashed", "dotted", "double", "solid", "wavy"}),
    "text-justify": frozenset({"auto", "inter-character", "inter-word", "none"}),
    "text-rendering": frozenset({"auto", "geometricprecision", "optimizelegibility", "optimizespeed"}),
    "white-space": frozenset({"break-spaces", "normal", "nowrap", "pre", "pre-line", "pre-wrap"}),
    "word-break": frozenset({"break-all", "keep-all", "normal"}),
}
CSS_KEYWORD_SET_VALUES = {
    "font-variant-east-asian": frozenset({
        "full-width", "jis04", "jis78", "jis83", "jis90", "normal", "proportional-width", "ruby",
        "simplified", "traditional",
    }),
    "font-variant-ligatures": frozenset({
        "common-ligatures", "contextual", "discretionary-ligatures", "historical-ligatures",
        "no-common-ligatures", "no-contextual", "no-discretionary-ligatures", "no-historical-ligatures",
        "none", "normal",
    }),
    "font-variant-numeric": frozenset({
        "diagonal-fractions", "lining-nums", "normal", "oldstyle-nums", "ordinal", "proportional-nums",
        "slashed-zero", "stacked-fractions", "tabular-nums",
    }),
    "text-decoration-line": frozenset({"line-through", "none", "overline", "underline"}),
}
FIXED_CSS_PROPERTIES = frozenset({
    *CSS_BORDER_STYLE_PROPERTIES, *CSS_BORDER_WIDTH_PROPERTIES, *CSS_COLOR_PROPERTIES,
    *CSS_NONNEGATIVE_LENGTH_PERCENTAGE_PROPERTIES, *CSS_SIGNED_SPACING_PROPERTIES,
    *CSS_WIDOW_ORPHAN_PROPERTIES, *CSS_ENUM_VALUES, *CSS_KEYWORD_SET_VALUES,
    "border-spacing", "font-family", "font-size", "font-weight", "line-height", "tab-size",
    "text-decoration-thickness", "text-indent", "vertical-align",
})
type JsonObject = dict[str, Any]
type AssetRecord = dict[str, Any]

class ContractError(ValueError):
    pass

@dataclass(frozen=True)
class Snapshot:
    path: Path
    content: bytes
    value: JsonObject

@dataclass(frozen=True)
class FileState:
    path: Path
    size: int
    sha256: str

@dataclass
class Markup:
    root: Element
    ids: set[str]
    references: list[tuple[str, str]]
    markers: list[str]
    visible_sha256: str

@dataclass
class Recipe:
    value: JsonObject
    captions: dict[str, Markup]
    figure_classes: dict[str, list[str]]

@dataclass
class Assembly:
    spec: Snapshot
    recipe: Recipe
    source: Snapshot
    bundle: Snapshot
    profile: JsonObject
    pages_by_id: dict[str, JsonObject]
    pages_by_number: dict[int, JsonObject]
    bundle_fragments: dict[str, JsonObject]

@dataclass
class BuildMaterial:
    assembly: Assembly
    fragments: dict[str, Markup]
    fragment_contents: dict[str, bytes]
    figures: list[JsonObject]
    used_pages: dict[str, tuple[JsonObject, bytes]]
    fonts: list[tuple[JsonObject, bytes, JsonObject]]
    stylesheets: list[tuple[bytes, str]]

@dataclass
class ValidationResult:
    manifest_path: Path
    manifest_bytes: bytes
    manifest: JsonObject
    inventory: dict[str, FileState]
    summary: JsonObject

def fail(message: str) -> Never:
    raise ContractError(message)

def require(condition: bool, message: str) -> None:
    if not condition:
        fail(message)

def eprint(message: str) -> None:
    print(message, file=sys.stderr)

def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()

def json_bytes(value: Any) -> bytes:
    text = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True)
    return f"{text}\n".encode()

def _reject_json_constant(value: str) -> Never:
    fail(f"non-finite JSON number is forbidden: {value}")

def _unique_json_object(pairs: list[tuple[str, Any]]) -> JsonObject:
    result: JsonObject = {}
    for key, value in pairs:
        require(key not in result, f"duplicate JSON object key: {key}")
        result[key] = value
    return result

def parse_json_bytes(content: bytes, context: str) -> JsonObject:
    try:
        text = content.decode()
    except UnicodeDecodeError as error:
        fail(f"{context} is not UTF-8: {error}")
    try:
        value = json.loads(text, object_pairs_hook=_unique_json_object, parse_constant=_reject_json_constant)
    except ContractError:
        raise
    except (json.JSONDecodeError, RecursionError) as error:
        fail(f"{context} is not valid JSON: {error}")
    except ValueError as error:
        fail(f"{context} contains an invalid JSON number: {error}")
    if not isinstance(value, dict):
        fail(f"{context} must be a JSON object")
    return value

def checked_node(path: Path, context: str) -> os.stat_result:
    try:
        info = path.lstat()
    except OSError as error:
        fail(f"cannot inspect {context} {path}: {error}")
    reparse = getattr(info, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    require(not stat.S_ISLNK(info.st_mode) and not reparse,
            f"{context} is a link or reparse point: {path}")
    return info

def reject_existing(path: Path, message: str) -> None:
    try:
        path.lstat()
    except FileNotFoundError:
        return
    fail(message)

def read_regular(path: Path, context: str) -> bytes:
    candidate = path.expanduser().absolute()
    try:
        require(stat.S_ISREG(checked_node(candidate, context).st_mode),
                f"{context} is not a regular file: {candidate}")
        return candidate.read_bytes()
    except OSError as error:
        fail(f"cannot read {context} {path}: {error}")

def decode_utf8(content: bytes, context: str) -> str:
    try:
        return content.decode()
    except UnicodeDecodeError as error:
        fail(f"{context} is not UTF-8: {error}")

def read_snapshot(path: Path, context: str) -> Snapshot:
    candidate = path.expanduser().absolute()
    content = read_regular(candidate, context)
    return Snapshot(candidate.resolve(strict=True), content, parse_json_bytes(content, context))

def write_bytes(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)

def write_atomic(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)

@functools.cache
def load_schema(name: str) -> JsonObject:
    content = read_regular(ASSETS / name, f"bundled schema {name}")
    return parse_json_bytes(content, f"bundled schema {name}")

def validate_schema_value(value: Any, schema: JsonObject, name: str, context: str) -> None:
    errors = sorted(Draft202012Validator(schema).iter_errors(value),
                    key=lambda error: (tuple(str(part) for part in error.absolute_path), error.message))
    if not errors:
        return
    detail = "; ".join(f"{error.json_path}: {error.message}" for error in errors[:20])
    if len(errors) > 20:
        detail += f"; and {len(errors) - 20} more"
    fail(f"{context} violates {name}: {detail}")

def validate_schema(value: Any, name: str, context: str) -> None:
    validate_schema_value(value, load_schema(name), name, context)

@functools.cache
def load_profile() -> tuple[JsonObject, bytes]:
    content = read_regular(ASSETS / PROFILE_NAME, "publication profile")
    profile = parse_json_bytes(content, "publication profile")
    require(set(profile) == {
                "schema_version", "profile_id", "closed", "fragment_html", "untrusted_stylesheet",
                "global_prohibitions",
            },
            "publication profile has an unexpected top-level shape")
    require(profile["schema_version"] == "1.0" and profile["profile_id"] == PROFILE_ID and profile["closed"] is True,
            "publication profile identity is not supported")
    html = profile["fragment_html"]
    css = profile["untrusted_stylesheet"]
    require(isinstance(html, dict) and isinstance(css, dict),
            "publication profile sections must be objects")
    require(set(html) == {"elements", "global_attributes"}
            and isinstance(html.get("elements"), dict) and isinstance(html.get("global_attributes"), list),
            "publication profile HTML allowlists are malformed")
    elements = html["elements"]
    global_attributes = html["global_attributes"]
    require(all(isinstance(tag, str) and tag == tag.casefold()
                and tag in FIXED_ELEMENT_ATTRIBUTES
                and isinstance(attributes, list)
                and all(isinstance(name, str) for name in attributes)
                and set(attributes) <= FIXED_ELEMENT_ATTRIBUTES[tag]
                and len(attributes) == len(set(attributes))
                for tag, attributes in elements.items()),
            "publication profile element allowlist is malformed")
    require(all(isinstance(name, str) and name in FIXED_GLOBAL_ATTRIBUTES
                for name in global_attributes)
            and len(global_attributes) == len(set(global_attributes)),
            "publication profile global attribute allowlist is malformed")
    require(set(css) == {"properties", "at_rules", "selector_surface"}
            and isinstance(css.get("properties"), list) and isinstance(css.get("at_rules"), list)
            and isinstance(css.get("selector_surface"), list),
            "publication profile stylesheet allowlists are malformed")
    require(all(isinstance(name, str) and not name.startswith("--") and name in FIXED_CSS_PROPERTIES
                for name in css["properties"])
            and len(css["properties"]) == len(set(css["properties"])),
            "publication profile CSS property allowlist is unsupported")
    require(css["at_rules"] == [], "publication profile at-rules are unsupported")
    require(all(isinstance(name, str) for name in css["selector_surface"])
            and len(css["selector_surface"]) == len(set(css["selector_surface"]))
            and set(css["selector_surface"]) <= PROFILE_SELECTOR_SURFACE,
            "publication profile selector surface is unsupported")
    prohibitions = profile["global_prohibitions"]
    require(isinstance(prohibitions, list) and all(isinstance(name, str) for name in prohibitions)
            and len(prohibitions) == len(set(prohibitions))
            and set(prohibitions) == PROFILE_PROHIBITIONS,
            "publication profile global prohibitions are unsupported")
    return (profile, content)

def normalized_relative_path(value: Any, context: str) -> str:
    if not isinstance(value, str) or not value:
        fail(f"{context} must be a non-empty relative path")
    if "\\" in value or value.startswith("/") or re.match("^[A-Za-z]:", value) or SCHEME_PATTERN.match(value) or ("\x00" in value):
        fail(f"{context} is not a confined POSIX relative path: {value!r}")
    path = PurePosixPath(value)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts) or path.as_posix() != value:
        fail(f"{context} is not a normalized relative path: {value!r}")
    return value

def resolve_relative(base: Path, value: Any, context: str) -> Path:
    identity = normalized_relative_path(value, context)
    root = base.resolve(strict=True)
    candidate = root
    try:
        for part in PurePosixPath(identity).parts:
            candidate /= part
            checked_node(candidate, context)
        resolved = candidate.resolve(strict=True)
        resolved.relative_to(root)
    except (OSError, ValueError):
        fail(f"{context} escapes or is missing beneath {root}: {identity!r}")
    require(stat.S_ISREG(resolved.stat().st_mode),
            f"{context} is not a regular file: {resolved}")
    return resolved

def asset_from_bytes(identity: str, content: bytes) -> AssetRecord:
    return {"path": normalized_relative_path(identity, "asset path"), "sha256": sha256_bytes(content), "bytes": len(content)}

def write_asset(root: Path, identity: str, content: bytes) -> AssetRecord:
    path = root / Path(*PurePosixPath(identity).parts)
    write_bytes(path, content)
    return asset_from_bytes(identity, content)

def read_bound_asset(base: Path, record: Any, context: str) -> tuple[Path, bytes]:
    if not isinstance(record, dict):
        fail(f"{context} record must be an object")
    path = resolve_relative(base, record.get("path"), context)
    content = read_regular(path, context)
    require(record.get("bytes") == len(content),
            f"{context} byte length does not match its manifest record")
    require(record.get("sha256") == sha256_bytes(content),
            f"{context} SHA-256 does not match its manifest record")
    return (path, content)

def scan_tree(root: Path, hash_paths: set[str] | None=None) -> dict[str, FileState]:
    candidate = root.expanduser().absolute()
    require(stat.S_ISDIR(checked_node(candidate, "publication root").st_mode),
            f"publication root is not a directory: {candidate}")
    resolved = candidate.resolve(strict=True)
    inventory: dict[str, FileState] = {}
    pending = [resolved]
    while pending:
        directory = pending.pop()
        for child in sorted(directory.iterdir(), key=lambda item: item.name):
            info = checked_node(child, "publication tree node")
            mode = info.st_mode
            if stat.S_ISDIR(mode):
                pending.append(child)
            elif stat.S_ISREG(mode):
                identity = child.relative_to(resolved).as_posix()
                digest = sha256_bytes(child.read_bytes()) if hash_paths is None or identity in hash_paths else ""
                inventory[identity] = FileState(child, info.st_size, digest)
            else:
                fail(f"publication tree contains a special node: {child}")
    return dict(sorted(inventory.items()))

def ensure_utf8_text(value: Any, context: str, *, nonempty: bool=False) -> str:
    if not isinstance(value, str):
        fail(f"{context} must be a string")
    if "\x00" in value:
        fail(f"{context} contains U+0000")
    try:
        value.encode()
    except UnicodeEncodeError as error:
        fail(f"{context} is not valid Unicode: {error}")
    if nonempty and (not value.strip()):
        fail(f"{context} must be non-empty")
    return value

def validate_css_string(value: Any, context: str, *, nonempty: bool=False) -> str:
    text = ensure_utf8_text(value, context, nonempty=nonempty)
    require(not any(unicodedata.category(character) in {"Cc", "Cf", "Cs"} for character in text),
            f"{context} contains a CSS-unsafe control, format, or surrogate character")
    return text

def validate_identifier(value: Any, context: str) -> str:
    text = ensure_utf8_text(value, context, nonempty=True)
    require(ID_PATTERN.fullmatch(text) is not None,
            f"{context} is not a canonical identifier: {text!r}")
    return text

def validate_language(value: Any, context: str) -> str:
    text = ensure_utf8_text(value, context, nonempty=True)
    require(LANGUAGE_PATTERN.fullmatch(text) is not None,
            f"{context} is not a supported language tag: {text!r}")
    return text

def finite_number(value: Any, context: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        fail(f"{context} must be a number")
    try:
        number = float(value)
    except OverflowError:
        fail(f"{context} must be representable as a finite number")
    require(math.isfinite(number), f"{context} must be finite")
    return number

def normalize_visible_text(parts: list[str]) -> str:
    return " ".join(unicodedata.normalize("NFC", "".join(parts)).split())

def visible_text_sha256(parts: list[str]) -> str:
    return sha256_bytes(normalize_visible_text(parts).encode())

def is_comment(node: Element) -> bool:
    return node.tag is Comment or not isinstance(node.tag, str)

def validate_markup_nesting(root: Element, context: str) -> None:
    pending = [(root, 0)]
    while pending:
        parent, depth = pending.pop()
        for child in list(parent):
            if is_comment(child):
                continue
            child_depth = depth + 1
            require(
                child_depth <= MARKUP_NESTING_LIMIT,
                f"{context} exceeds the markup nesting limit of {MARKUP_NESTING_LIMIT}",
            )
            pending.append((child, child_depth))

def profile_html(profile: JsonObject) -> JsonObject:
    return profile["fragment_html"]

def is_integer_in_range(value: str, minimum: int, maximum: int) -> bool:
    if re.fullmatch("[+-]?[0-9]+", value) is None:
        return False
    try:
        number = int(value)
    except ValueError:
        return False
    return minimum <= number <= maximum

def validate_attribute_value(name: str, value: str, context: str) -> None:
    ensure_utf8_text(value, context)
    if name in ATTRIBUTE_PLAIN_LIMITS:
        require(len(value) <= ATTRIBUTE_PLAIN_LIMITS[name], f"{context} exceeds the character limit")
    elif name in ATTRIBUTE_TOKEN_LIMITS:
        items = value.split()
        require(
            0 < len(items) <= ATTRIBUTE_TOKEN_LIMITS[name]
            and all(re.fullmatch("[A-Za-z][A-Za-z0-9._-]{0,127}", item) for item in items),
            f"{context} contains an invalid ID-reference list",
        )
    elif name == "class":
        items = value.split()
        require(
            0 < len(items) <= 16
            and all(re.fullmatch("[A-Za-z][A-Za-z0-9_-]{0,63}", item) for item in items),
            f"{context} contains an invalid class list",
        )
    elif name in {"colspan", "rowspan", "span"}:
        require(is_integer_in_range(value, 1, 100),
                f"{context} must be an integer from 1 through 100")
    elif name == "datetime":
        require(re.fullmatch("[0-9A-Za-z:+.TZ-]{1,64}", value) is not None,
                f"{context} does not match the allowed datetime form")
    elif name == "dir":
        require(value in {"auto", "ltr", "rtl"}, f"{context} has a disallowed direction")
    elif name == "href":
        require(re.fullmatch("#[A-Za-z][A-Za-z0-9._-]{0,127}", value) is not None,
                f"{context} must be a same-document fragment URL")
    elif name == "id":
        require(re.fullmatch("[A-Za-z][A-Za-z0-9._-]{0,127}", value) is not None,
                f"{context} is not a valid HTML identifier")
    elif name == "lang":
        validate_language(value, context)
    elif name == "reversed":
        require(not value, f"{context} must use presence-only syntax")
    elif name == "scope":
        require(value in {"col", "colgroup", "row", "rowgroup"}, f"{context} has a disallowed scope")
    elif name == "start":
        require(is_integer_in_range(value, -1_000_000, 1_000_000),
                f"{context} is outside the allowed range")
    elif name == "type":
        require(value in {"1", "A", "I", "a", "i"}, f"{context} has a disallowed list type")
    else:
        fail(f"{context} has no fixed value policy")

def authored_class_tokens(value: str, context: str) -> list[str]:
    validate_attribute_value("class", value, context)
    tokens = value.split()
    reserved = GENERATED_CLASSES.intersection(tokens)
    require(not reserved, f"{context} uses assembler-owned classes: " + ", ".join(sorted(reserved)))
    return tokens

def parse_markup(content: str, profile: JsonObject, context: str, *, allow_figure_markers: bool) -> Markup:
    ensure_utf8_text(content, context)
    parser = html5lib.HTMLParser(tree=html5lib.getTreeBuilder("etree"), strict=False,
                                 namespaceHTMLElements=False)
    try:
        root = parser.parseFragment(content, container="div")
    except html5lib.html5parser.ParseError as error:
        fail(f"{context} is not valid HTML: {error}")
    except ValueError as error:
        fail(f"{context} is not valid HTML: {error}")
    if parser.errors:
        findings = ", ".join(f"{position[0]}:{position[1]} {code}"
                             for position, code, _data in parser.errors[:10])
        fail(f"{context} has HTML parse errors: {findings}")
    validate_markup_nesting(root, context)
    html_profile = profile_html(profile)
    elements = html_profile["elements"]
    globals_ = set(html_profile["global_attributes"])
    identifiers: set[str] = set()
    references: list[tuple[str, str]] = []
    markers: list[str] = []
    text_parts: list[str] = []

    def inspect_text(value: str | None, text_context: str) -> None:
        if value is not None:
            text_parts.append(ensure_utf8_text(value, text_context))

    def inspect_children(parent: Element, parent_tag: str) -> None:
        inspect_text(parent.text, f"{context} text")
        for child in list(parent):
            if is_comment(child):
                comment = child.text or ""
                match = FIGURE_COMMENT_PATTERN.fullmatch(comment)
                require(match is not None, f"{context} contains a comment outside the profile")
                if match:
                    require(allow_figure_markers, f"{context} cannot contain figure markers")
                    require(parent_tag in FIGURE_CONTAINERS, f"{context} figure marker is not in a flow container")
                    markers.append(match.group("id"))
            else:
                require(isinstance(child.tag, str), f"{context} contains unsupported markup")
                tag = child.tag
                local = elements.get(tag)
                require(isinstance(local, list), f"{context} contains undeclared element <{tag}>")
                allowed = set(local) | set(globals_)
                for name, raw_value in child.attrib.items():
                    require(isinstance(name, str) and name == name.casefold(),
                            f"{context} contains a namespaced attribute")
                    require(name in allowed, f"{context} contains undeclared attribute {name!r} on <{tag}>")
                    value = ensure_utf8_text(raw_value, f"{context} <{tag}> {name}")
                    attribute_context = f"{context} <{tag}> {name}"
                    if name == "class":
                        authored_class_tokens(value, attribute_context)
                    else:
                        validate_attribute_value(name, value, attribute_context)
                    if name == "id":
                        if value in identifiers:
                            fail(f"{context} contains duplicate id {value!r}")
                        identifiers.add(value)
                    elif name == "href":
                        references.append((f"{context} href", value[1:]))
                    elif name in {"aria-describedby", "aria-labelledby", "headers"}:
                        references.extend((f"{context} {name}", target) for target in value.split())
                inspect_children(child, tag)
            inspect_text(child.tail, f"{context} tail text")
    inspect_children(root, "DOCUMENT_FRAGMENT")
    return Markup(root, identifiers, references, markers, visible_text_sha256(text_parts))

def serialize_markup(markup: Markup, figures: dict[str, str] | None=None) -> str:
    replacements = figures or {}

    def serialize_children(parent: Element) -> str:
        output = escape(parent.text or "", quote=False)
        for child in list(parent):
            if is_comment(child):
                comment = child.text or ""
                match = FIGURE_COMMENT_PATTERN.fullmatch(comment)
                if match:
                    identifier = match.group("id")
                    if identifier not in replacements:
                        fail(f"figure marker has no generated figure: {identifier}")
                    output += replacements[identifier]
                else:
                    output += f"<!--{comment}-->"
            else:
                tag = child.tag
                attributes = []
                for name, value in sorted(child.attrib.items()):
                    attributes.append(name if value == "" and name == "reversed" else f'{name}="{escape(value, quote=True)}"')
                start = f"{tag} {' '.join(attributes)}" if attributes else tag
                output += f"<{start}>"
                if tag not in VOID_ELEMENTS:
                    output += serialize_children(child) + f"</{tag}>"
            output += escape(child.tail or "", quote=False)
        return output
    return serialize_children(markup.root)

def class_tokens(value: Any, context: str) -> list[str]:
    if value is None:
        return []
    text = ensure_utf8_text(value, context)
    if not text.strip():
        return []
    tokens = authored_class_tokens(text, context)
    require(len(tokens) == len(set(tokens)), f"{context} contains duplicate class tokens")
    return tokens

def validate_recipe(value: JsonObject, profile: JsonObject) -> Recipe:
    validate_schema(value, "assembly-spec.schema.json", "assembly specification")
    validate_identifier(value["publication_id"], "publication_id")
    ensure_utf8_text(value["title"], "publication title", nonempty=True)
    validate_language(value["title_language"], "title language")
    validate_language(value["language"], "publication language")
    normalized_relative_path(value["source_package"], "source_package")
    normalized_relative_path(value["translation_bundle"], "translation_bundle")
    for index, identifier in enumerate(value["fragment_order"], start=1):
        validate_identifier(identifier, f"fragment_order item {index}")
    captions: dict[str, Markup] = {}
    figure_classes: dict[str, list[str]] = {}
    for declaration in value["figures"]:
        identifier = validate_identifier(declaration["id"], "figure id")
        require(identifier not in captions, f"duplicate figure id: {identifier}")
        ensure_utf8_text(declaration["alt"], f"figure {identifier} alt", nonempty=True)
        caption = ensure_utf8_text(declaration["caption_html"], f"figure {identifier} caption")
        captions[identifier] = parse_markup(caption, profile, f"figure {identifier} caption", allow_figure_markers=False)
        figure_classes[identifier] = class_tokens(declaration.get("class"), f"figure {identifier} class")
    roles = value["font_roles"]
    cjk = validate_css_string(roles["body-cjk"], "font role body-cjk", nonempty=True)
    latin = validate_css_string(roles["body-latin"], "font role body-latin", nonempty=True)
    require(cjk == latin or cjk.casefold() != latin.casefold(), "font family names must not differ only by case")
    faces: set[tuple[str, str, int]] = set()
    families: set[str] = set()
    for index, font in enumerate(value["fonts"], start=1):
        family = validate_css_string(font["family"], f"font {index} family", nonempty=True)
        normalized_relative_path(font["path"], f"font {index} path")
        require(Path(font["path"]).suffix.casefold() in {".otf", ".ttf"}, f"font {index} must be an OTF or TTF file")
        face = (family.casefold(), font["style"], font["weight"])
        require(face not in faces, f"duplicate declared font face: {family} {font['style']} {font['weight']}")
        faces.add(face)
        families.add(family.casefold())
    for role in FONT_ROLES:
        family = roles[role]
        require(family.casefold() in families, f"font role {role} references undeclared family {family!r}")
        require((family.casefold(), "normal", 400) in faces, f"font role {role} has no normal 400 face")
    for index, stylesheet in enumerate(value["stylesheets"], start=1):
        normalized_relative_path(stylesheet, f"stylesheet {index} path")
        require(Path(stylesheet).suffix.casefold() == ".css", f"stylesheet {index} must use a .css extension")
    for profile_name in value["profiles"]:
        validate_identifier(profile_name, "profile")
    return Recipe(value, captions, figure_classes)

def css_tokens(tokens: list[Any]) -> list[Any]:
    return [token for token in tokens if token.type not in {"comment", "whitespace"}]

def css_ident(token: Any) -> str | None:
    return str(token.lower_value) if token.type == "ident" else None

def split_css(tokens: list[Any], delimiter: str) -> list[list[Any]]:
    groups: list[list[Any]] = [[]]
    for token in tokens:
        if token.type == "literal" and token.value == delimiter:
            groups.append([])
        else:
            groups[-1].append(token)
    return groups

def css_number(token: Any, context: str) -> float:
    value = getattr(token, "value", None)
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        fail(f"{context} requires a finite numeric value")
    return float(value)

def one_css_token(tokens: list[Any], context: str) -> Any:
    require(len(tokens) == 1, f"{context} requires one value")
    return tokens[0]

def validate_lang_selector(token: Any, context: str) -> None:
    content = css_tokens(token.content if token.type == "[] block" else token.arguments)
    if token.type == "function":
        require(len(content) == 1 and content[0].type in {"ident", "string"}, f"{context} :lang() must contain one language tag")
        validate_language(str(content[0].value), f"{context} :lang()")
        return
    if len(content) == 1:
        require(css_ident(content[0]) == "lang", f"{context} has an unsupported attribute selector")
        return
    require(len(content) in {3, 4} and css_ident(content[0]) == "lang"
            and content[1].type == "literal" and content[1].value in {"=", "|="}
            and content[2].type in {"ident", "string"}
            and (len(content) == 3 or css_ident(content[3]) in {"i", "s"}),
            f"{context} has an invalid lang attribute selector")
    validate_language(str(content[2].value), f"{context} language")

def validate_selector(
    tokens: list[Any],
    allowed_elements: set[str],
    surface: set[str],
    context: str,
) -> str:
    clean = [token for token in tokens if token.type != "comment"]
    while clean and clean[0].type == "whitespace":
        clean.pop(0)
    while clean and clean[-1].type == "whitespace":
        clean.pop()
    require(bool(clean), f"{context} is empty")
    index = 0
    has_simple = False
    while index < len(clean):
        token = clean[index]
        if token.type == "whitespace":
            while index < len(clean) and clean[index].type == "whitespace":
                index += 1
            if index == len(clean):
                break
            if clean[index].type == "literal" and clean[index].value == ">":
                continue
            if not has_simple:
                continue
            require("descendant" in surface, f"{context} uses a disallowed descendant combinator")
            has_simple = False
            continue
        if token.type == "literal" and token.value == ">":
            require(has_simple, f"{context} has an invalid child combinator")
            require("child" in surface, f"{context} uses a disallowed child combinator")
            has_simple = False
            index += 1
            continue
        step = 1
        if token.type == "ident":
            require("type" in surface, f"{context} uses a disallowed type selector")
            require(not has_simple, f"{context} has adjacent type selectors")
            require(str(token.lower_value) in allowed_elements, f"{context} selects undeclared element {token.value!r}")
        elif token.type == "literal" and token.value == "*":
            require("universal" in surface, f"{context} uses a disallowed universal selector")
            require(not has_simple, f"{context} has an invalid universal selector")
        elif token.type == "hash" and token.is_identifier:
            require("id" in surface, f"{context} uses a disallowed ID selector")
            require(re.fullmatch("[A-Za-z][A-Za-z0-9._-]{0,127}", token.value) is not None, f"{context} has an invalid ID selector")
        elif token.type == "literal" and token.value == ".":
            require("class" in surface, f"{context} uses a disallowed class selector")
            require(index + 1 < len(clean) and clean[index + 1].type == "ident"
                    and re.fullmatch("[A-Za-z][A-Za-z0-9_-]{0,63}", str(clean[index + 1].value)) is not None,
                    f"{context} has an invalid class selector")
            step = 2
        elif token.type == "[] block":
            require("lang-attribute" in surface, f"{context} uses a disallowed attribute selector")
            validate_lang_selector(token, context)
        elif token.type == "literal" and token.value == ":":
            require("lang-pseudo-class" in surface, f"{context} uses a disallowed pseudo-class")
            require(index + 1 < len(clean), f"{context} has an incomplete pseudo-class")
            pseudo = clean[index + 1]
            require(pseudo.type == "function" and pseudo.lower_name == "lang", f"{context} has an unsupported pseudo-class")
            validate_lang_selector(pseudo, context)
            step = 2
        else:
            fail(f"{context} contains unsupported selector syntax")
        has_simple = True
        index += step
    require(has_simple, f"{context} ends with a combinator")
    return tinycss2.serialize(clean).strip()

def validate_css_length(
    token: Any,
    context: str,
    *,
    units: frozenset[str] = CSS_LENGTH_UNITS,
    minimum: float = 0,
    maximum: float = 256,
    maximum_percentage: float | None = None,
    minimum_percentage: float | None = None,
) -> None:
    if token.type == "number" and css_number(token, context) == 0:
        require(minimum <= 0 <= maximum, f"{context} zero is outside the allowed range")
        return
    if token.type == "dimension":
        require(str(token.lower_unit) in units, f"{context} uses a disallowed length unit")
        require(minimum <= css_number(token, context) <= maximum, f"{context} length is outside the allowed range")
        return
    if maximum_percentage is not None and token.type == "percentage":
        lower = minimum if minimum_percentage is None else minimum_percentage
        require(lower <= css_number(token, context) <= maximum_percentage,
                f"{context} percentage is outside the allowed range")
        return
    fail(f"{context} requires an allowed length value")

def validate_opaque_color(token: Any, context: str) -> None:
    if token.type == "ident":
        require(str(token.lower_value) in CSS_NAMED_COLORS, f"{context} uses a disallowed named color")
        return
    value = str(getattr(token, "value", ""))
    require(token.type == "hash" and len(value) in {3, 6} and (HEX_COLOR_PATTERN.fullmatch(value) is not None), f"{context} requires an opaque print color")
    pairs = [character * 2 for character in value] if len(value) == 3 else [value[index:index + 2] for index in (0, 2, 4)]
    channels = []
    for pair in pairs:
        channel = int(pair, 16) / 255
        channels.append(channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4)
    luminance = sum((factor * channel for factor, channel in zip((0.2126, 0.7152, 0.0722), channels, strict=True)))
    require(1.05 / (luminance + 0.05) + 1e-09 >= 3, f"{context} does not meet the minimum print contrast")

def validate_font_families(tokens: list[Any], declared: set[str], context: str) -> None:
    groups = split_css(tokens, ",")
    require(bool(groups) and len(groups) <= 8, f"{context} has an invalid font-family list")
    for group in groups:
        compact = css_tokens(group)
        if len(compact) == 1 and compact[0].type == "string":
            family = ensure_utf8_text(compact[0].value, context).casefold()
            require(family in declared, f"{context} references undeclared font family {family!r}")
        elif compact and all(token.type == "ident" for token in compact):
            family = " ".join(str(token.value) for token in compact).casefold()
            require(family in declared or (len(compact) == 1 and family in CSS_GENERIC_FAMILIES),
                    f"{context} references undeclared font family {family!r}")
        else:
            fail(f"{context} has an invalid font-family item")

def validate_css_integer(
    token: Any,
    context: str,
    *,
    minimum: int,
    maximum: int,
    multiple_of: int = 1,
) -> None:
    require(token.type == "number" and token.is_integer, f"{context} requires an integer")
    number = int(css_number(token, context))
    require(minimum <= number <= maximum and number % multiple_of == 0,
            f"{context} integer is outside the allowed range")

def validate_css_value(
    tokens: list[Any],
    property_name: str,
    families: set[str],
    context: str,
) -> None:
    for token in tokens:
        if token.type == "error":
            fail(f"{context} contains invalid CSS: {token.message}")
        require(token.type not in {"url", "function", "{} block", "[] block", "() block"}, f"{context} contains a disallowed CSS value form")
    compact = css_tokens(tokens)
    require(bool(compact), f"{context} has an empty value")
    if len(compact) == 1 and css_ident(compact[0]) in CSS_WIDE_KEYWORDS:
        return
    if property_name == "font-family":
        validate_font_families(tokens, families, context)
        return
    if property_name == "border-spacing":
        require(len(compact) in {1, 2}, f"{context} requires one or two values")
        for item in compact:
            validate_css_length(item, context)
        return
    if property_name not in CSS_KEYWORD_SET_VALUES:
        one_css_token(compact, context)
    token = compact[0]
    if property_name in CSS_ENUM_VALUES:
        require(css_ident(token) in CSS_ENUM_VALUES[property_name], f"{context} has a disallowed keyword")
    elif property_name in CSS_BORDER_STYLE_PROPERTIES:
        require(css_ident(token) in {"dashed", "dotted", "double", "none", "solid"},
                f"{context} has a disallowed border style")
    elif property_name in CSS_KEYWORD_SET_VALUES:
        values = [css_ident(item) for item in compact]
        value_set = set(values)
        require(all(value in CSS_KEYWORD_SET_VALUES[property_name] for value in values)
                and len(values) == len(set(values))
                and (not value_set.intersection({"none", "normal"}) or len(values) == 1)
                and all(len(value_set.intersection(group)) <= 1
                        for group in CSS_KEYWORD_EXCLUSIVE_GROUPS.get(property_name, ())),
                f"{context} has a disallowed keyword set")
    elif property_name == "font-weight":
        if css_ident(token) in {"bold", "normal"}:
            return
        validate_css_integer(token, context, minimum=100, maximum=900, multiple_of=100)
    elif property_name in CSS_WIDOW_ORPHAN_PROPERTIES:
        validate_css_integer(token, context, minimum=1, maximum=10)
    elif property_name == "tab-size":
        validate_css_integer(token, context, minimum=1, maximum=16)
    elif property_name in CSS_COLOR_PROPERTIES:
        validate_opaque_color(token, context)
    elif property_name in CSS_NONNEGATIVE_LENGTH_PERCENTAGE_PROPERTIES:
        validate_css_length(token, context, maximum_percentage=100)
    elif property_name in CSS_BORDER_WIDTH_PROPERTIES:
        if css_ident(token) not in {"medium", "thick", "thin"}:
            validate_css_length(token, context, maximum=12)
    elif property_name == "font-size":
        if css_ident(token) not in {"large", "larger", "medium", "small", "smaller", "x-large", "x-small"}:
            validate_css_length(token, context, minimum=0.5, maximum=200, maximum_percentage=200)
    elif property_name == "text-decoration-thickness":
        if css_ident(token) not in {"auto", "from-font"}:
            validate_css_length(token, context, maximum=8)
    elif property_name == "vertical-align":
        if css_ident(token) not in {
            "baseline", "bottom", "middle", "sub", "super", "text-bottom", "text-top", "top",
        }:
            validate_css_length(token, context, minimum=-4, maximum=4, maximum_percentage=100)
    elif property_name == "line-height":
        if css_ident(token) == "normal":
            return
        if token.type == "number":
            require(0.8 <= css_number(token, context) <= 4, f"{context} number is outside the allowed range")
        else:
            validate_css_length(
                token,
                context,
                minimum=0.8,
                maximum=4,
                minimum_percentage=80,
                maximum_percentage=400,
            )
    elif property_name in CSS_SIGNED_SPACING_PROPERTIES:
        if css_ident(token) != "normal":
            validate_css_length(
                token,
                context,
                units=frozenset({"ch", "em", "ex", "pt", "px", "rem"}),
                minimum=-4,
                maximum=16,
            )
    elif property_name == "text-indent":
        validate_css_length(
            token,
            context,
            units=frozenset({"ch", "em", "ex", "pt", "px", "rem"}),
            maximum=16,
            maximum_percentage=25,
        )
    else:
        fail(f"{context} has no fixed value policy for {property_name!r}")

def validate_and_scope_stylesheet(content: str, profile: JsonObject, families: set[str], context: str) -> str:
    ensure_utf8_text(content, context)
    css = profile["untrusted_stylesheet"]
    require(css["at_rules"] == [], "publication profile at-rules are unsupported")
    properties = set(css["properties"])
    surface = set(css["selector_surface"])
    elements = set(profile_html(profile)["elements"])
    output: list[str] = []
    try:
        rules = tinycss2.parse_stylesheet(content, skip_comments=True, skip_whitespace=True)
    except ValueError as error:
        fail(f"{context} is not valid CSS: {error}")
    for rule_index, rule in enumerate(rules, start=1):
        rule_context = f"{context} rule {rule_index}"
        if rule.type == "error":
            fail(f"{rule_context} is invalid: {rule.message}")
        require(rule.type == "qualified-rule", f"{rule_context} is not an allowed qualified rule")
        selectors = [validate_selector(group, elements, surface, f"{rule_context} selector")
                     for group in split_css(rule.prelude, ",")]
        rendered: list[str] = []
        seen: set[str] = set()
        declarations = tinycss2.parse_declaration_list(rule.content, skip_comments=True, skip_whitespace=True)
        for declaration_index, declaration in enumerate(declarations, start=1):
            item_context = f"{rule_context} declaration {declaration_index}"
            if declaration.type == "error":
                fail(f"{item_context} is invalid: {declaration.message}")
            require(declaration.type == "declaration", f"{item_context} is not a CSS declaration")
            name = str(declaration.lower_name)
            require(not name.startswith("--"), f"{item_context} uses a custom property")
            require(name in properties, f"{item_context} uses undeclared property {name!r}")
            require(not declaration.important, f"{item_context} uses !important")
            require(name not in seen, f"{rule_context} repeats property {name!r}")
            seen.add(name)
            validate_css_value(declaration.value, name, families, item_context)
            value = tinycss2.serialize(declaration.value).strip()
            rendered.append(f"  {name}: {value};")
        require(bool(rendered), f"{rule_context} has no declarations")
        scoped = ",\n".join(f"[data-fragment-id] {selector}" for selector in selectors)
        output.append(scoped + " {\n" + "\n".join(rendered) + "\n}\n")
    return "\n".join(output)

def css_string(value: str, context: str) -> str:
    value = validate_css_string(value, context)
    escaped: list[str] = []
    for character in value:
        if character in {'"', "\\"}:
            escaped.append("\\" + character)
        else:
            escaped.append(character)
    return '"' + "".join(escaped) + '"'

def font_face_css(record: JsonObject, css_path: Path) -> str:
    font_path = Path(*PurePosixPath(record["asset"]["path"]).parts)
    css_directory = Path(*PurePosixPath(css_path.as_posix()).parts).parent
    relative = Path(os.path.relpath(font_path, css_directory)).as_posix()
    format_name = "truetype" if font_path.suffix.casefold() == ".ttf" else "opentype"
    return (
        "@font-face {\n"
        f"  font-family: {css_string(record['family'], 'font family')};\n"
        f"  src: url({css_string(relative, 'font URL')}) "
        f'format("{format_name}");\n'
        f"  font-style: {record['style']};\n"
        f"  font-weight: {record['weight']};\n"
        "  font-display: block;\n"
        "}\n"
    )

def compose_css(spec: JsonObject, font_records: list[JsonObject], scoped_stylesheets: list[str]) -> bytes:
    base = read_regular(ASSETS / "print-base.css", "bundled print CSS").decode()
    font_css = "\n".join(font_face_css(record, Path("assets/print.css")) for record in font_records)
    margins = spec["page"]["margin_in"]
    page_css = (
        "@page {\n"
        f"  size: {PAGE_SIZES[spec['page']['size']]};\n"
        f"  margin: {margins['top']}in {margins['right']}in "
        f"{margins['bottom']}in {margins['left']}in;\n"
        "}\n"
    )
    roles = spec["font_roles"]
    role_css = (
        ":root {\n"
        f"  --body-cjk: {css_string(roles['body-cjk'], 'body-cjk family')};\n"
        f"  --body-latin: {css_string(roles['body-latin'], 'body-latin family')};\n"
        "}\n"
    )
    additions = "\n".join(f"/* Scoped stylesheet {index}. */\n{content.rstrip()}\n"
                          for index, content in enumerate(scoped_stylesheets, start=1))
    sections = [font_css.rstrip(), base.rstrip(), page_css.rstrip(), role_css.rstrip()]
    if additions:
        sections.append(additions.rstrip())
    return ("\n\n".join(section for section in sections if section) + "\n").encode()

def split_xml_name(value: str) -> tuple[str, str]:
    if value.startswith("{"):
        namespace, local = value[1:].split("}", maxsplit=1)
        return (namespace, local)
    return ("", value)

def svg_length(value: Any, context: str) -> float:
    if not isinstance(value, str):
        fail(f"{context} is missing")
    match = SVG_LENGTH_PATTERN.fullmatch(value)
    if match is None:
        fail(f"{context} is not a finite SVG length")
    number = float(match.group("number"))
    if not math.isfinite(number) or number <= 0:
        fail(f"{context} must be positive")
    return number

def svg_css_is_obfuscated(value: str) -> bool:
    return ("\\" in value or "/*" in value or "*/" in value
            or re.search("@import\\b", value, flags=re.IGNORECASE) is not None
            or re.search("(?:var|env)\\s*\\(", value, flags=re.IGNORECASE) is not None)

def validate_page_svg(
    content: bytes,
    context: str,
    *,
    expected_dimensions: tuple[float, float] | None=None,
) -> tuple[float, float]:
    text = decode_utf8(content, context)
    require(not any(token in text for token in ("<!--", "<?", "<![CDATA[")),
            f"{context} contains XML comments, instructions, or CDATA")
    try:
        parser = iterparse(BytesIO(content), events=("start", "pi"), forbid_dtd=True,
                           forbid_entities=True, forbid_external=True)
        for event, _node in parser:
            if event == "pi":
                fail(f"{context} contains an XML processing instruction")
        root = getattr(parser, "root", None)
    except (DefusedXmlException, ParseError) as error:
        fail(f"{context} is not safe XML: {error}")
    if root is None:
        fail(f"{context} has no SVG root")
    namespace, local = split_xml_name(root.tag)
    require(namespace == SVG_NAMESPACE and local == "svg",
            f"{context} root must be an SVG namespace <svg>")
    width = svg_length(root.attrib.get("width"), f"{context} width")
    height = svg_length(root.attrib.get("height"), f"{context} height")
    if expected_dimensions is not None:
        expected_width, expected_height = expected_dimensions
        require(f"{width:.3f}" == f"{expected_width:.3f}"
                and f"{height:.3f}" == f"{expected_height:.3f}",
                f"{context} dimensions do not match the source package page")
    view_box = root.attrib.get("viewBox")
    require(isinstance(view_box, str), f"{context} requires a viewBox")
    try:
        values = [float(value) for value in re.split("[\\s,]+", view_box.strip())]
    except ValueError as error:
        fail(f"{context} has an invalid viewBox: {error}")
    require(len(values) == 4 and all(math.isfinite(value) for value in values)
            and all(abs(actual - expected) <= 0.001
                    for actual, expected in zip(values, (0.0, 0.0, width, height), strict=True)),
            f"{context} viewBox does not match the source page")
    identifiers: set[str] = set()
    references: set[str] = set()
    for node in root.iter():
        require(isinstance(node.tag, str), f"{context} contains unsupported XML nodes")
        node_namespace, node_name = split_xml_name(node.tag)
        require(node_namespace == SVG_NAMESPACE and node_name in SVG_ELEMENTS,
                f"{context} contains undeclared SVG element <{node_name}>")
        require(not node.text or not node.text.strip(), f"{context} contains unexpected SVG text nodes")
        require(not node.tail or not node.tail.strip(), f"{context} contains unexpected SVG tail text")
        for raw_name, raw_value in node.attrib.items():
            attribute_namespace, attribute_name = split_xml_name(raw_name)
            value = ensure_utf8_text(raw_value, f"{context} {attribute_name}")
            if attribute_namespace == INKSCAPE_NAMESPACE:
                if node_name == "g" and ((attribute_name == "groupmode" and value == "layer")
                                         or attribute_name == "label"):
                    continue
                fail(f"{context} contains undeclared Inkscape attribute {raw_name!r}")
            if attribute_namespace == XLINK_NAMESPACE and attribute_name == "href":
                attribute_name = "href"
            elif attribute_namespace:
                fail(f"{context} contains undeclared namespaced attribute {raw_name!r}")
            if attribute_name == "style":
                match = SVG_STYLE_PATTERN.fullmatch(value.strip())
                require(not svg_css_is_obfuscated(value) and node_name == "g" and match is not None
                        and match.group(1).casefold() in SVG_STANDARD_BLEND_MODES,
                        f"{context} allows only a standard mix-blend-mode style on <g>")
                continue
            if attribute_name == "href":
                require(node_name in {"image", "linearGradient", "pattern", "radialGradient", "use"},
                        f"{context} contains href on unsupported SVG element <{node_name}>")
                reference = value.strip()
                if SVG_FRAGMENT_PATTERN.fullmatch(reference):
                    references.add(reference[1:])
                    continue
                match = SVG_DATA_IMAGE_PATTERN.fullmatch(reference)
                if node_name != "image" or match is None:
                    fail(f"{context} contains a nonlocal SVG resource")
                try:
                    decoded = base64.b64decode(re.sub("\\s+", "", match.group("data")), validate=True)
                except (binascii.Error, ValueError) as error:
                    fail(f"{context} contains invalid embedded image data: {error}")
                media = match.group("media").casefold()
                require((media == "png" and decoded.startswith(b"\x89PNG\r\n\x1a\n"))
                        or (media == "jpeg" and decoded.startswith(b"\xff\xd8\xff")),
                        f"{context} embedded image signature does not match its media type")
                continue
            require(attribute_name in SVG_ATTRIBUTES,
                    f"{context} contains undeclared SVG attribute {attribute_name!r}")
            if attribute_name == "id":
                require(SVG_FRAGMENT_PATTERN.fullmatch(f"#{value}") is not None,
                        f"{context} contains invalid SVG id {value!r}")
                require(value not in identifiers, f"{context} contains duplicate SVG id {value!r}")
                identifiers.add(value)
            if attribute_name in SVG_PRESENTATION_ATTRIBUTES:
                if svg_css_is_obfuscated(value):
                    fail(f"{context} contains obfuscated CSS in SVG {attribute_name}")
                url_match = re.search("url\\s*\\(", value, flags=re.IGNORECASE)
                if url_match is not None:
                    match = SVG_LOCAL_URL_PATTERN.fullmatch(value.strip())
                    if attribute_name not in SVG_LOCAL_URL_ATTRIBUTES or match is None:
                        fail(f"{context} contains a nonlocal SVG paint resource")
                    references.add(match.group(2)[1:])
                elif SVG_EXTERNAL_SCHEME_PATTERN.search(value):
                    fail(f"{context} contains an external scheme in SVG {attribute_name}")
    missing = sorted(references - identifiers)
    require(not missing, f"{context} references missing SVG IDs: {', '.join(missing)}")
    return (width, height)

def index_objects(items: list[Any], key: str, context: str) -> dict[Any, JsonObject]:
    indexed: dict[Any, JsonObject] = {}
    for position, item in enumerate(items, start=1):
        if not isinstance(item, dict):
            fail(f"{context} {position} must be an object")
        identity = item[key]
        if identity in indexed:
            fail(f"duplicate {context} {key}: {identity}")
        indexed[identity] = item
    return indexed

def source_pages(source: JsonObject) -> tuple[dict[str, JsonObject], dict[int, JsonObject]]:
    pages = source["pages"]
    return (index_objects(pages, "id", "source page"),
            index_objects(pages, "pdf_page", "source page"))

def validate_source_and_bundle(source: Snapshot, bundle: Snapshot, recipe: Recipe, *,
                               require_original_binding: bool) -> tuple[dict[str, JsonObject], dict[int, JsonObject], dict[str, JsonObject]]:
    validate_schema(source.value, "source-package.schema.json", "source package")
    validate_schema(bundle.value, "translation-bundle.schema.json", "translation bundle")
    if (source.value["status"] != "pass" or source.value["issues"]
            or any(page["status"] != "pass" for page in source.value["pages"])):
        fail("source package effective status must be pass")
    source_record = bundle.value["source_package"]
    require(source_record["sha256"] == sha256_bytes(source.content)
            and source_record["bytes"] == len(source.content),
            "translation bundle source-package hash binding does not match")
    if require_original_binding:
        bound_source = resolve_relative(bundle.path.parent, source_record["path"],
                                        "translation bundle source_package")
        require(bound_source == source.path,
                "translation bundle source_package path does not resolve to the assembly source package")
    require(bundle.value["target_language"] == recipe.value["language"],
            "translation target language does not match assembly language")
    missing_profiles = sorted(set(source.value["profiles"]) - set(recipe.value["profiles"]))
    require(not missing_profiles, "assembly profiles omit source profiles: " + ", ".join(missing_profiles))
    pages_by_id, pages_by_number = source_pages(source.value)
    fragments = index_objects(bundle.value["fragments"], "id", "translation fragment")
    order = recipe.value["fragment_order"]
    missing_fragments = [identifier for identifier in order if identifier not in fragments]
    require(not missing_fragments,
            "assembly fragment_order references absent approved fragments: " + ", ".join(missing_fragments))
    for identifier in order:
        for block_id in fragments[identifier]["source_block_ids"]:
            match = BLOCK_ID_PATTERN.fullmatch(block_id)
            if match is None:
                fail(f"fragment {identifier} has an invalid source block ID")
            page = pages_by_id.get(match.group("page"))
            if page is None:
                fail(f"fragment {identifier} references an absent source page")
            require(page["status"] == "pass", f"fragment {identifier} references a non-passing source page")
    return (pages_by_id, pages_by_number, fragments)

def load_assembly(spec: Snapshot, *, source: Snapshot | None = None, bundle: Snapshot | None = None,
                  require_original_binding: bool) -> Assembly:
    profile, _profile_content = load_profile()
    recipe = validate_recipe(spec.value, profile)
    if source is None:
        source_path = resolve_relative(
            spec.path.parent, recipe.value["source_package"], "assembly source package")
        source = read_snapshot(source_path, "source package")
    if bundle is None:
        bundle_path = resolve_relative(
            spec.path.parent, recipe.value["translation_bundle"], "assembly translation bundle")
        bundle = read_snapshot(bundle_path, "translation bundle")
    pages_by_id, pages_by_number, fragments = validate_source_and_bundle(
        source, bundle, recipe, require_original_binding=require_original_binding)
    return Assembly(spec, recipe, source, bundle, profile, pages_by_id, pages_by_number, fragments)

def load_fragments(assembly: Assembly) -> tuple[dict[str, Markup], dict[str, bytes]]:
    markups: dict[str, Markup] = {}
    contents: dict[str, bytes] = {}
    claimed_paths: set[Path] = set()
    for identifier in assembly.recipe.value["fragment_order"]:
        record = assembly.bundle_fragments[identifier]
        path, content = read_bound_asset(assembly.bundle.path.parent, record, f"approved fragment {identifier}")
        require(path not in claimed_paths, "distinct selected fragment IDs cannot share one asset path")
        claimed_paths.add(path)
        text = decode_utf8(content, f"approved fragment {identifier}")
        markup = parse_markup(
            text, assembly.profile, f"approved fragment {identifier}", allow_figure_markers=True)
        require(markup.visible_sha256 == record["visible_text_sha256"],
                f"approved fragment {identifier} visible-text hash does not match")
        markups[identifier] = markup
        contents[identifier] = content
    return (markups, contents)

def validate_consumed_blocks(assembly: Assembly) -> None:
    requested: dict[str, set[str]] = {}
    for identifier in assembly.recipe.value["fragment_order"]:
        for block_id in assembly.bundle_fragments[identifier]["source_block_ids"]:
            match = BLOCK_ID_PATTERN.fullmatch(block_id)
            if match is None:
                fail(f"fragment {identifier} has an invalid source block ID")
            requested.setdefault(match.group("page"), set()).add(block_id)
    for page_id, block_ids in requested.items():
        page = assembly.pages_by_id[page_id]
        _path, content = read_bound_asset(
            assembly.source.path.parent, page["assets"]["blocks"], f"source blocks for {page_id}")
        blocks = parse_json_bytes(content, f"source blocks for {page_id}")
        validate_schema(blocks, "source-blocks.schema.json", f"source blocks for {page_id}")
        require(blocks["page_id"] == page_id, f"source block file page_id does not match {page_id}")
        indexed = index_objects(blocks["blocks"], "id", "source block")
        width = finite_number(page["width"], "page width")
        height = finite_number(page["height"], "page height")
        for position, block in enumerate(blocks["blocks"], start=1):
            block_id = block["id"]
            expected_id = f"{page_id}-block-{position:04d}"
            require(block_id == expected_id, f"source block {position} id must be {expected_id}")
            require(block["source_order"] == position,
                    f"source block {block_id} source_order must be {position}")
            require(bool(block["text"].strip()), f"source block {block_id} text is blank")
            bbox = [finite_number(value, f"source block {block_id} bbox coordinate")
                    for value in block["bbox"]]
            x0, y0, x1, y1 = bbox
            require(x0 >= 0 and y0 >= 0 and x1 > x0 and y1 > y0 and x1 <= width and y1 <= height,
                    f"source block {block_id} bbox is outside the source page")
        available = set(indexed)
        missing = sorted(block_ids - available)
        require(not missing, f"selected source blocks are absent on {page_id}: " + ", ".join(missing))

def validated_bbox(value: Any, dimensions: tuple[float, float], context: str) -> list[float]:
    require(isinstance(value, list) and len(value) == 4, f"{context} bbox must contain four numbers")
    bbox = [finite_number(number, f"{context} bbox coordinate") for number in value]
    x0, y0, x1, y1 = bbox
    width, height = dimensions
    require(x0 >= 0 and y0 >= 0 and x1 > x0 and y1 > y0 and x1 <= width and y1 <= height,
            f"{context} bbox is outside the source page")
    canonical = [float(f"{number:.3f}") for number in bbox]
    require(canonical[2] > canonical[0] and canonical[3] > canonical[1],
            f"{context} bbox collapses under three-decimal canonicalization")
    return canonical

def load_figures(assembly: Assembly) -> tuple[list[JsonObject], dict[str, tuple[JsonObject, bytes]]]:
    declarations = assembly.recipe.value["figures"]
    if not declarations:
        return ([], {})
    figure_map_record = assembly.source.value.get("figure_map")
    if not isinstance(figure_map_record, dict):
        fail("assembly figures require a source-package figure_map")
    _map_path, map_content = read_bound_asset(
        assembly.source.path.parent, figure_map_record, "source figure map")
    figure_map = parse_json_bytes(map_content, "source figure map")
    figure_schema = load_schema("figure-map.schema.json")
    require(set(figure_map) == {"schema_version", "coordinate_space", "figures"}
            and figure_map["schema_version"] == "1.0"
            and figure_map["coordinate_space"] == "pdf-points-top-left"
            and isinstance(figure_map["figures"], list),
            "source figure map has an invalid envelope")
    requested = {declaration["id"] for declaration in declarations}
    figures_by_id: dict[str, JsonObject] = {}
    item_schema = figure_schema["properties"]["figures"]["items"]
    for position, item in enumerate(figure_map["figures"], start=1):
        identifier = item.get("id") if isinstance(item, dict) else None
        if not isinstance(identifier, str) or identifier not in requested:
            continue
        require(identifier not in figures_by_id, f"duplicate selected source figure id: {identifier}")
        validate_schema_value(item, item_schema, "figure-map.schema.json figure item", f"selected source figure {position}")
        figures_by_id[identifier] = item
    selected: list[JsonObject] = []
    used_pages: dict[str, tuple[JsonObject, bytes]] = {}
    part_ids: set[str] = set()
    for declaration in declarations:
        identifier = declaration["id"]
        source_figure = figures_by_id.get(identifier)
        if source_figure is None:
            fail(f"assembly figure is absent from the figure map: {identifier}")
        source_profile = source_figure["profile"]
        require(source_profile is None or source_profile in assembly.source.value["profiles"],
                f"figure {identifier} profile is not declared by the source package: {source_profile!r}")
        parts = source_figure["parts"]
        require([part["order"] for part in parts] == list(range(1, len(parts) + 1)),
                f"figure {identifier} parts are not in canonical order")
        for part in parts:
            part_id = part["id"]
            require(part_id not in part_ids, f"duplicate source figure part id: {part_id}")
            part_ids.add(part_id)
            page = assembly.pages_by_number.get(part["pdf_page"])
            if page is None:
                fail(f"figure part {part_id} references an absent page")
            require(page["status"] == "pass", f"figure part {part_id} references a non-passing page")
            dimensions = (finite_number(page["width"], f"figure part {part_id} page width"),
                          finite_number(page["height"], f"figure part {part_id} page height"))
            validated_bbox(part["bbox"], dimensions, f"figure part {part_id}")
            page_id = page["id"]
            if page_id not in used_pages:
                _svg_path, svg_content = read_bound_asset(assembly.source.path.parent, page["assets"]["svg"], f"source page SVG {page_id}")
                validate_page_svg(svg_content, f"source page SVG {page_id}",
                                  expected_dimensions=dimensions)
                used_pages[page_id] = (page, svg_content)
        selected.append(source_figure)
    return (selected, used_pages)

def inspect_font_bytes(content: bytes, context: str) -> JsonObject:
    font: TTFont | None = None
    try:
        font = TTFont(BytesIO(content), lazy=False, fontNumber=0)
        codepoints = frozenset(font.getBestCmap() or ())
        require(bool(codepoints), f"{context} has no Unicode cmap")
        name_table = font.get("name")

        def font_name(name_id: int) -> str | None:
            if name_table is None:
                return None
            for record in name_table.names:
                if record.nameID == name_id:
                    try:
                        return record.toUnicode()
                    except UnicodeError:
                        continue
            return None
        return {"postscript_name": font_name(6), "full_name": font_name(4)}
    except ContractError:
        raise
    except (AssertionError, AttributeError, IndexError, KeyError, OSError, OverflowError, TTLibError, TypeError, ValueError, struct.error) as error:
        fail(f"{context} is not a valid font: {error}")
    finally:
        if font is not None:
            font.close()

def validate_document_bindings(publication_id: str, fragment_order: list[str],
                               fragments: dict[str, Markup],
                               figures: list[tuple[str, str, Markup]]) -> None:
    figure_ids = [identifier for identifier, _dom_id, _caption in figures]
    markers = [marker for identifier in fragment_order for marker in fragments[identifier].markers]
    require(markers == figure_ids, "figure markers must match figure IDs and order exactly")
    known_ids = {publication_id}
    references: list[tuple[str, str]] = []
    for identifier in fragment_order:
        markup = fragments[identifier]
        duplicates = known_ids.intersection(markup.ids)
        require(not duplicates, "duplicate document IDs: " + ", ".join(sorted(duplicates)))
        known_ids.update(markup.ids)
        references.extend(markup.references)
    for identifier, dom_id, caption in figures:
        require(dom_id not in known_ids,
                f"figure {identifier} DOM ID conflicts with another document ID: {dom_id}")
        known_ids.add(dom_id)
        duplicates = known_ids.intersection(caption.ids)
        require(not duplicates, "duplicate caption document IDs: " + ", ".join(sorted(duplicates)))
        known_ids.update(caption.ids)
        references.extend(caption.references)
    missing = sorted({target for _context, target in references if target not in known_ids})
    require(not missing, "same-document references have no target: " + ", ".join(missing))

def load_build_material(assembly: Assembly) -> BuildMaterial:
    fragments, fragment_contents = load_fragments(assembly)
    validate_consumed_blocks(assembly)
    figures, used_pages = load_figures(assembly)
    spec = assembly.recipe.value
    bindings = [(figure["id"], figure["id"], assembly.recipe.captions[figure["id"]])
                for figure in spec["figures"]]
    validate_document_bindings(spec["publication_id"], spec["fragment_order"], fragments, bindings)
    fonts: list[tuple[JsonObject, bytes, JsonObject]] = []
    for index, declaration in enumerate(assembly.recipe.value["fonts"], start=1):
        path = resolve_relative(assembly.spec.path.parent, declaration["path"], f"font {index}")
        content = read_regular(path, f"font {index}")
        metadata = inspect_font_bytes(content, f"font {index}")
        fonts.append((declaration, content, metadata))
    families = {font["family"].casefold() for font in assembly.recipe.value["fonts"]}
    stylesheets: list[tuple[bytes, str]] = []
    for index, identity in enumerate(assembly.recipe.value["stylesheets"], start=1):
        path = resolve_relative(assembly.spec.path.parent, identity, f"stylesheet {index}")
        content = read_regular(path, f"stylesheet {index}")
        text = decode_utf8(content, f"stylesheet {index}")
        stylesheets.append((content, validate_and_scope_stylesheet(text, assembly.profile, families, f"stylesheet {index}")))
    return BuildMaterial(assembly, fragments, fragment_contents, figures, used_pages, fonts, stylesheets)

def crop_markup(part: JsonObject, page: JsonObject, source_svg: AssetRecord, alternative: str) -> str:
    page_width = finite_number(page["width"], "source page width")
    page_height = finite_number(page["height"], "source page height")
    x0, y0, x1, y1 = validated_bbox(
        part["bbox"], (page_width, page_height), f"crop {part['id']}")
    width = x1 - x0
    height = y1 - y0
    accessible = f"{alternative} - part {part['order']}"
    return (
        f'<svg class="figure-part" data-crop-id="{escape(part["id"], quote=True)}" '
        f'xmlns="{SVG_NAMESPACE}" '
        f'viewBox="{x0:.3f} {y0:.3f} {width:.3f} {height:.3f}" '
        f'width="{width:.3f}" height="{height:.3f}" role="img" '
        f'aria-label="{escape(accessible, quote=True)}" '
        'preserveAspectRatio="xMidYMid meet">'
        f"<title>{escape(accessible)}</title>"
        f'<image href="{escape(source_svg["path"], quote=True)}" '
        f'x="0" y="0" width="{page_width:.3f}" '
        f'height="{page_height:.3f}"></image>'
        "</svg>"
    )

def figure_markup(declaration: JsonObject, manifest_figure: JsonObject,
                  pages_by_number: dict[int, JsonObject], caption: Markup, classes: list[str]) -> str:
    identifier = declaration["id"]
    class_value = " ".join(["publication-figure", *classes])
    parts = []
    for part in manifest_figure["parts"]:
        page = pages_by_number.get(part["pdf_page"])
        if page is None:
            fail(f"figure part {part['id']} references an absent page")
        parts.append(crop_markup(part, page, part["source_svg"], declaration["alt"]))
    return (
        f'<figure id="{escape(identifier, quote=True)}" '
        f'class="{escape(class_value, quote=True)}" '
        f'data-figure-id="{escape(identifier, quote=True)}" '
        f'role="group" aria-label="{escape(declaration["alt"], quote=True)}">'
        f'<div class="figure-parts">{"".join(parts)}</div>'
        f"<figcaption>{serialize_markup(caption)}</figcaption>"
        "</figure>"
    )

def compose_html(recipe: Recipe, fragments: dict[str, Markup], manifest_figures: list[JsonObject],
                 pages_by_number: dict[int, JsonObject]) -> bytes:
    declarations = recipe.value["figures"]
    require(len(declarations) == len(manifest_figures),
            "manifest figure count does not match assembly specification")
    figures = {
        declaration["id"]: figure_markup(declaration, manifest_figure, pages_by_number,
                                          recipe.captions[declaration["id"]],
                                          recipe.figure_classes[declaration["id"]])
        for declaration, manifest_figure in zip(declarations, manifest_figures, strict=True)
    }
    sections = [
        f'<section data-fragment-id="{escape(identifier, quote=True)}">'
        f"{serialize_markup(fragments[identifier], figures)}</section>"
        for identifier in recipe.value["fragment_order"]
    ]
    spec = recipe.value
    html = (
        "<!doctype html>\n"
        f'<html lang="{escape(spec["language"], quote=True)}">\n'
        "<head>\n"
        '<meta charset="utf-8">\n'
        f'<title lang="{escape(spec["title_language"], quote=True)}">'
        f"{escape(spec['title'])}</title>\n"
        '<link rel="stylesheet" href="assets/print.css">\n'
        "</head>\n"
        "<body>\n"
        f'<main id="{escape(spec["publication_id"], quote=True)}">\n'
        + "\n".join(sections)
        + "\n</main>\n"
        "</body>\n"
        "</html>\n"
    )
    return html.encode()

def generator_record() -> JsonObject:
    version = sys.version_info
    return {"name": "assemble_print.py", "version": SCRIPT_VERSION, "runtime": f"python-{version.major}.{version.minor}.{version.micro}"}

def publication_policies() -> JsonObject:
    profile, content = load_profile()
    return {
        "publication_profile": {"id": profile["profile_id"], "schema_version": profile["schema_version"],
                                "sha256": sha256_bytes(content)},
        "generated_document_profile": GENERATED_DOCUMENT_PROFILE,
        "generated_css_profile": GENERATED_CSS_PROFILE,
    }

def build_candidate(stage: Path, material: BuildMaterial) -> JsonObject:
    assembly = material.assembly
    recipe = assembly.recipe
    spec = recipe.value
    inputs = {
        "source_package": write_asset(stage, "inputs/source-package.json", assembly.source.content),
        "translation_bundle": write_asset(stage, "inputs/translation-bundle.json", assembly.bundle.content),
        "assembly_spec": write_asset(stage, "inputs/assembly-spec.json", assembly.spec.content),
    }
    fragment_records = []
    for identifier in spec["fragment_order"]:
        source_record = assembly.bundle_fragments[identifier]
        fragment_records.append({
            "id": identifier,
            "asset": write_asset(stage, f"fragments/{identifier}.html", material.fragment_contents[identifier]),
            "dom_selector": f'[data-fragment-id="{identifier}"]',
            "visible_text_sha256": source_record["visible_text_sha256"],
            "source_block_ids": source_record["source_block_ids"],
        })
    page_assets = {
        page_id: write_asset(stage, f"assets/pages/{page_id}.svg", content)
        for page_id, (_page, content) in sorted(material.used_pages.items())
    }
    manifest_figures = []
    for declaration, source_figure in zip(spec["figures"], material.figures, strict=True):
        parts = []
        for source_part in source_figure["parts"]:
            page = assembly.pages_by_number[source_part["pdf_page"]]
            dimensions = (
                finite_number(page["width"], "source page width"),
                finite_number(page["height"], "source page height"),
            )
            canonical_bbox = validated_bbox(
                source_part["bbox"],
                dimensions,
                f"figure part {source_part['id']}",
            )
            parts.append({
                "id": source_part["id"],
                "order": source_part["order"],
                "pdf_page": source_part["pdf_page"],
                "bbox": canonical_bbox,
                "dom_selector": f'[data-crop-id="{source_part["id"]}"]',
                "source_svg": page_assets[page["id"]],
            })
        caption = declaration["caption_html"]
        manifest_figures.append({
            "id": declaration["id"],
            "dom_id": declaration["id"],
            "source_label": source_figure["source_label"],
            "profile": source_figure["profile"],
            "embedded_language_inventory": list(
                source_figure["embedded_language_inventory"]
            ),
            "caption_html": caption,
            "caption_sha256": sha256_bytes(caption.encode()),
            "alt": declaration["alt"],
            "parts": parts,
        })
    font_records = []
    for index, (declaration, content, metadata) in enumerate(material.fonts, start=1):
        extension = Path(declaration["path"]).suffix.casefold()
        font_records.append({
            "family": declaration["family"],
            "style": declaration["style"],
            "weight": declaration["weight"],
            **metadata,
            "asset": write_asset(stage, f"assets/fonts/font-{index:03d}{extension}", content),
        })
    stylesheet_records = [
        write_asset(stage, f"assets/stylesheets/stylesheet-{index:03d}.css", content)
        for index, (content, _scoped) in enumerate(material.stylesheets, start=1)
    ]
    css_record = write_asset(stage, "assets/print.css",
                             compose_css(spec, font_records, [scoped for _content, scoped in material.stylesheets]))
    html_record = write_asset(stage, "index.html",
                              compose_html(recipe, material.fragments, manifest_figures, assembly.pages_by_number))
    manifest: JsonObject = {
        "schema_version": "1.0",
        "publication_id": spec["publication_id"],
        "generator": generator_record(),
        "policies": publication_policies(),
        "document": {
            "title": spec["title"],
            "title_language": spec["title_language"],
            "language": spec["language"],
        },
        "profiles": spec["profiles"],
        "print_geometry": {
            "page_size": spec["page"]["size"],
            "margin_in": spec["page"]["margin_in"],
        },
        "inputs": inputs,
        "fragments": fragment_records,
        "figures": manifest_figures,
        "font_roles": spec["font_roles"],
        "fonts": font_records,
        "stylesheets": stylesheet_records,
        "outputs": {
            "html": html_record,
            "css": css_record,
            "draft_pdf": None,
        },
        "status": "assembled",
    }
    manifest_path = stage / MANIFEST_NAME
    write_bytes(manifest_path, json_bytes(manifest))
    validate_publication(manifest_path)
    return {
        "publication_id": spec["publication_id"],
        "fragments": len(fragment_records),
        "figures": len(manifest_figures),
        "crops": sum(len(figure["parts"]) for figure in manifest_figures),
        "status": "assembled",
    }

def print_json(value: JsonObject) -> None:
    print(json.dumps(value, ensure_ascii=False, sort_keys=True))

def build_publication(args: argparse.Namespace) -> int:
    spec = read_snapshot(args.spec, "assembly specification")
    material = load_build_material(load_assembly(spec, require_original_binding=True))
    requested_output = args.output.expanduser()
    if os.name == "nt" and any(
        part not in {".", ".."} and part.rstrip(" .") != part
        for part in requested_output.parts
    ):
        fail("Windows assembly output path components must not end in a dot or space")
    output = requested_output.parent.resolve() / requested_output.name
    reject_existing(output, f"assembly output already exists: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix=f".{output.name}.staging-", dir=output.parent))
    try:
        summary = build_candidate(stage, material)
        try:
            stage.rename(output)
        except OSError as error:
            fail(f"cannot publish assembly directory: {error}")
    except BaseException as error:
        try:
            if stage.exists():
                shutil.rmtree(stage)
        except OSError as cleanup_error:
            fail(f"{error}; cannot remove assembly candidate {stage}: {cleanup_error}")
        raise
    print_json({"committed": True, "manifest": str(output / MANIFEST_NAME), **summary})
    return 0

def semantic_asset_records(manifest: JsonObject) -> list[tuple[str, AssetRecord]]:
    records = [(f"inputs.{role}", record) for role, record in manifest["inputs"].items()]
    records += [(f"fragments[{index}].asset", item["asset"]) for index, item in enumerate(manifest["fragments"], 1)]
    records += [(f"figures[{fi}].parts[{pi}].source_svg", part["source_svg"])
                for fi, figure in enumerate(manifest["figures"], 1)
                for pi, part in enumerate(figure["parts"], 1)]
    records += [(f"fonts[{index}].asset", item["asset"]) for index, item in enumerate(manifest["fonts"], 1)]
    records += [(f"stylesheets[{index}]", item) for index, item in enumerate(manifest["stylesheets"], 1)]
    records += [(f"outputs.{role}", record) for role, record in manifest["outputs"].items()
                if isinstance(record, dict)]
    return records

def semantic_asset_map(manifest: JsonObject) -> dict[str, AssetRecord]:
    assets: dict[str, AssetRecord] = {}
    labels: dict[str, str] = {}
    for label, record in semantic_asset_records(manifest):
        require(isinstance(record, dict) and set(record) == {"path", "sha256", "bytes"},
                f"{label} must be an exact asset record")
        identity = normalized_relative_path(record["path"], f"{label} path")
        if identity in assets and assets[identity] != record:
            fail(f"conflicting semantic asset declarations for {identity}: {labels[identity]} and {label}")
        assets.setdefault(identity, record)
        labels.setdefault(identity, label)
    require(MANIFEST_NAME not in assets, "assembly manifest must not declare itself as a semantic asset")
    return dict(sorted(assets.items()))

def validate_manifest_inventory(root: Path, manifest: JsonObject) -> dict[str, FileState]:
    assets = semantic_asset_map(manifest)
    expected = set(assets) | {MANIFEST_NAME}
    inventory = scan_tree(root, expected)
    missing, extra = sorted(expected - set(inventory)), sorted(set(inventory) - expected)
    require(not missing and not extra,
            f"manifest semantic assets do not exactly cover the retained tree; missing={missing}, extra={extra}")
    for identity, record in assets.items():
        state = inventory[identity]
        require(record["bytes"] == state.size and record["sha256"] == state.sha256,
                f"manifest asset {identity} content does not match")
    return inventory

def read_utf8_asset(state: FileState, context: str) -> str:
    return decode_utf8(read_regular(state.path, context), context)

def load_manifest_material(manifest: JsonObject, inventory: dict[str, FileState],
                           profile: JsonObject) -> tuple[dict[str, Markup], dict[str, Markup],
                                                         dict[str, tuple[float, float]], list[str]]:
    for role in ("source_package", "translation_bundle", "assembly_spec"):
        path = f"inputs/{role.replace('_', '-')}.json"
        require(manifest["inputs"][role]["path"] == path, f"manifest input {role} must use {path}")
    fragments: dict[str, Markup] = {}
    for index, record in enumerate(manifest["fragments"], start=1):
        identifier = record["id"]
        require(identifier not in fragments, f"duplicate manifest fragment id: {identifier}")
        require(record["asset"]["path"] == f"fragments/{identifier}.html",
                f"manifest fragment {identifier} has a noncanonical path")
        require(record["dom_selector"] == f'[data-fragment-id="{identifier}"]',
                f"manifest fragment {identifier} has a noncanonical selector")
        context = f"manifest fragment {identifier}"
        markup = parse_markup(read_utf8_asset(inventory[record["asset"]["path"]], context), profile, context,
                              allow_figure_markers=True)
        require(markup.visible_sha256 == record["visible_text_sha256"],
                f"manifest fragment {index} visible-text hash is inconsistent")
        fragments[identifier] = markup
    captions: dict[str, Markup] = {}
    part_ids: set[str] = set()
    dimensions: dict[str, tuple[float, float]] = {}
    for figure in manifest["figures"]:
        identifier = figure["id"]
        require(identifier not in captions, f"duplicate manifest figure id: {identifier}")
        figure_profile = figure["profile"]
        require(figure_profile is None or figure_profile in manifest["profiles"],
                f"manifest figure {identifier} profile is not declared by the assembly manifest: {figure_profile!r}")
        caption = figure["caption_html"]
        require(figure["caption_sha256"] == sha256_bytes(caption.encode()),
                f"manifest figure {identifier} caption hash is inconsistent")
        captions[identifier] = parse_markup(
            caption, profile, f"manifest figure {identifier} caption", allow_figure_markers=False)
        parts = figure["parts"]
        require([part["order"] for part in parts] == list(range(1, len(parts) + 1)),
                f"manifest figure {identifier} part order is invalid")
        for part in parts:
            part_id = part["id"]
            require(part_id not in part_ids, f"duplicate manifest crop id: {part_id}")
            part_ids.add(part_id)
            require(part["dom_selector"] == f'[data-crop-id="{part_id}"]',
                    f"crop {part_id} has a noncanonical selector")
            retained = part["source_svg"]
            path = PurePosixPath(retained["path"])
            require(path.parent.as_posix() == "assets/pages" and path.suffix == ".svg"
                    and ID_PATTERN.fullmatch(path.stem) is not None,
                    f"crop {part_id} source SVG path is not canonical")
            if retained["path"] not in dimensions:
                dimensions[retained["path"]] = validate_page_svg(
                    inventory[retained["path"]].path.read_bytes(),
                    f"retained source page SVG {retained['path']}")
            canonical = validated_bbox(part["bbox"], dimensions[retained["path"]], f"crop {part_id}")
            require(part["bbox"] == canonical, f"crop {part_id} bbox is not canonical to three decimals")
    figure_bindings = [(figure["id"], figure["dom_id"], captions[figure["id"]])
                       for figure in manifest["figures"]]
    validate_document_bindings(manifest["publication_id"],
                               [fragment["id"] for fragment in manifest["fragments"]],
                               fragments, figure_bindings)
    faces: set[tuple[str, str, int]] = set()
    families: set[str] = set()
    for index, record in enumerate(manifest["fonts"], start=1):
        family = validate_css_string(record["family"], f"manifest font {index} family", nonempty=True)
        asset = record["asset"]
        extension = PurePosixPath(asset["path"]).suffix.casefold()
        require(asset["path"] == f"assets/fonts/font-{index:03d}{extension}",
                f"manifest font {index} has a noncanonical path")
        require(extension in {".otf", ".ttf"}, f"manifest font {index} must be an OTF or TTF file")
        face = (family.casefold(), record["style"], record["weight"])
        require(face not in faces, f"duplicate manifest font face: {family} {record['style']} {record['weight']}")
        faces.add(face)
        families.add(family.casefold())
        metadata = inspect_font_bytes(inventory[asset["path"]].path.read_bytes(), f"manifest font {index}")
        require({"postscript_name": record["postscript_name"], "full_name": record["full_name"]} == metadata,
                f"manifest font {index} metadata is inconsistent")
    for role in FONT_ROLES:
        family = validate_css_string(manifest["font_roles"][role], f"font role {role}", nonempty=True)
        require(family.casefold() in families, f"font role {role} references undeclared family {family!r}")
        require((family.casefold(), "normal", 400) in faces, f"font role {role} has no normal 400 face")
    scoped: list[str] = []
    for index, record in enumerate(manifest["stylesheets"], start=1):
        require(record["path"] == f"assets/stylesheets/stylesheet-{index:03d}.css",
                f"manifest stylesheet {index} has a noncanonical path")
        context = f"manifest stylesheet {index}"
        scoped.append(validate_and_scope_stylesheet(
            read_utf8_asset(inventory[record["path"]], context), profile, families, context))
    return (fragments, captions, dimensions, scoped)

def generated_css_records(content: str, context: str) -> list[Any]:
    def canonical_tokens(tokens: list[Any]) -> tuple[Any, ...]:
        result: list[Any] = []
        pending_whitespace = False
        for token in tokens:
            if token.type == "comment":
                continue
            if token.type == "whitespace":
                pending_whitespace = bool(result)
                continue
            if pending_whitespace:
                result.append(("whitespace",))
                pending_whitespace = False
            if token.type in {"string", "url"}:
                result.append((token.type, str(token.value)))
            elif token.type == "function":
                result.append(("function", str(token.name), canonical_tokens(token.arguments)))
            elif token.type in {"() block", "[] block", "{} block"}:
                result.append((token.type, canonical_tokens(token.content)))
            else:
                result.append((token.type, tinycss2.serialize([token])))
        return tuple(result)

    def records(rules: list[Any], *, nested: bool) -> list[Any]:
        result: list[Any] = []
        for index, rule in enumerate(rules, start=1):
            item_context = f"{context} rule {index}"
            require(rule.type != "error", f"{item_context} cannot be parsed: {getattr(rule, 'message', '')}")
            if rule.type == "qualified-rule":
                result.append(("rule", canonical_tokens(rule.prelude), canonical_tokens(rule.content)))
                continue
            require(rule.type == "at-rule" and not nested, f"{item_context} is outside the generated CSS profile")
            keyword = str(rule.lower_at_keyword)
            if keyword in {"font-face", "page"}:
                require(not css_tokens(rule.prelude) and rule.content is not None,
                        f"{item_context} has an invalid @{keyword} rule")
                result.append((keyword, canonical_tokens(rule.content)))
            elif keyword == "media":
                prelude = css_tokens(rule.prelude)
                require(len(prelude) == 1 and css_ident(prelude[0]) == "screen" and rule.content is not None,
                        f"{item_context} has an invalid @media rule")
                nested_rules = tinycss2.parse_rule_list(rule.content, skip_comments=True, skip_whitespace=True)
                result.append(("media-screen", tuple(records(nested_rules, nested=True))))
            else:
                fail(f"{item_context} uses unsupported @{keyword}")
        return result

    try:
        rules = tinycss2.parse_stylesheet(content, skip_comments=True, skip_whitespace=True)
        return records(rules, nested=False)
    except ContractError:
        raise
    except (RecursionError, ValueError) as error:
        fail(f"{context} is not valid CSS: {error}")

def validate_generated_css(content: str, manifest: JsonObject,
                           scoped_stylesheets: list[str]) -> None:
    css_path = Path(manifest["outputs"]["css"]["path"])
    margins = manifest["print_geometry"]["margin_in"]
    roles = manifest["font_roles"]
    expected = (
        "".join(font_face_css(font, css_path) for font in manifest["fonts"])
        + decode_utf8(read_regular(ASSETS / "print-base.css", "bundled print CSS"), "bundled print CSS")
        + "@page {\n"
        f"  size: {PAGE_SIZES[manifest['print_geometry']['page_size']]};\n"
        f"  margin: {margins['top']}in {margins['right']}in "
        f"{margins['bottom']}in {margins['left']}in;\n"
        "}\n:root {\n"
        f"  --body-cjk: {css_string(roles['body-cjk'], 'body-cjk family')};\n"
        f"  --body-latin: {css_string(roles['body-latin'], 'body-latin family')};\n"
        "}\n"
        + "".join(scoped_stylesheets)
    )
    require(generated_css_records(content, "generated CSS")
            == generated_css_records(expected, "manifest-bound CSS"),
            "generated CSS does not match the manifest-bound generated profile")

def local_name(node: Element) -> str:
    return node.tag.rsplit("}", maxsplit=1)[-1] if isinstance(node.tag, str) else ""

def checked_children(node: Element, tag: str, attributes: dict[str, str], context: str, *,
                     text: str | None=None, mixed: bool=False) -> list[Element]:
    require(local_name(node) == tag, f"{context} must be <{tag}>")
    require(dict(node.attrib) == attributes, f"{context} attributes do not match the generated profile")
    children = [child for child in list(node) if not is_comment(child)]
    if mixed:
        return children
    if text is not None:
        require(not children and (node.text or "") == text, f"{context} content is inconsistent")
    else:
        require(not (node.text or "").strip() and not any((child.tail or "").strip() for child in list(node)),
                f"{context} contains unexpected direct text")
    return children

def fragment_signature(parent: Element, *, generated: bool) -> tuple[Any, ...]:
    signature: list[Any] = [("text", parent.text or "")]
    for child in list(parent):
        if is_comment(child):
            require(not generated, "generated HTML contains a comment")
            match = FIGURE_COMMENT_PATTERN.fullmatch(child.text or "")
            if match is None:
                fail("retained fragment contains an invalid comment")
            signature.append(("figure", match.group("id")))
        elif generated and local_name(child) == "figure" and "data-figure-id" in child.attrib:
            signature.append(("figure", child.attrib["data-figure-id"]))
        else:
            signature.append(("element", local_name(child), tuple(sorted(child.attrib.items())),
                              fragment_signature(child, generated=generated)))
        signature.append(("tail", child.tail or ""))
    return tuple(signature)

def validate_crop_element(node: Element, figure: JsonObject, part: JsonObject,
                          dimensions: tuple[float, float]) -> None:
    part_id = part["id"]
    x0, y0, x1, y1 = validated_bbox(part["bbox"], dimensions, f"crop {part_id}")
    width, height = x1 - x0, y1 - y0
    accessible = f"{figure['alt']} - part {part['order']}"
    attributes = {
        "class": "figure-part", "data-crop-id": part_id,
        "{http://www.w3.org/2000/xmlns/}xmlns": SVG_NAMESPACE,
        "viewBox": f"{x0:.3f} {y0:.3f} {width:.3f} {height:.3f}",
        "width": f"{width:.3f}", "height": f"{height:.3f}", "role": "img",
        "aria-label": accessible, "preserveAspectRatio": "xMidYMid meet"}
    children = checked_children(node, "svg", attributes, f"crop {part_id}")
    require(len(children) == 2, f"crop {part_id} must contain title and image")
    title, image = children
    checked_children(title, "title", {}, f"crop {part_id} title", text=accessible)
    page_width, page_height = dimensions
    image_attributes = {"href": part["source_svg"]["path"], "x": "0", "y": "0",
                        "width": f"{page_width:.3f}", "height": f"{page_height:.3f}"}
    checked_children(image, "image", image_attributes, f"crop {part_id} image", text="")

def validate_figure_element(node: Element, figure: JsonObject, caption: Markup,
                            svg_dimensions: dict[str, tuple[float, float]]) -> None:
    identifier = figure["id"]
    attributes = dict(node.attrib)
    class_value = attributes.pop("class", "")
    classes = class_value.split()
    require(bool(classes) and len(classes) == len(set(classes))
            and classes[0] == "publication-figure" and not (set(classes[1:]) & GENERATED_CLASSES),
            f"figure {identifier} classes are outside the generated profile")
    require(all(re.fullmatch("[A-Za-z][A-Za-z0-9_-]{0,63}", token) for token in classes),
            f"figure {identifier} has invalid class tokens")
    expected = {"id": figure["dom_id"], "data-figure-id": identifier,
                "role": "group", "aria-label": figure["alt"]}
    require(attributes == expected, f"figure {identifier} attributes do not match the generated profile")
    children = checked_children(node, "figure", {"class": class_value, **expected}, f"figure {identifier}")
    require(len(children) == 2, f"figure {identifier} must contain parts and caption")
    parts_node, caption_node = children
    crop_nodes = checked_children(parts_node, "div", {"class": "figure-parts"},
                                  f"figure {identifier} parts wrapper")
    require(len(crop_nodes) == len(figure["parts"]), f"figure {identifier} crop count is inconsistent")
    for part, crop in zip(figure["parts"], crop_nodes, strict=True):
        validate_crop_element(crop, figure, part, svg_dimensions[part["source_svg"]["path"]])
    checked_children(caption_node, "figcaption", {}, f"figure {identifier} caption", mixed=True)
    require(fragment_signature(caption_node, generated=True) == fragment_signature(caption.root, generated=False),
            f"figure {identifier} caption content is inconsistent")

def validate_generated_html(content: str, manifest: JsonObject, fragments: dict[str, Markup],
                            captions: dict[str, Markup],
                            svg_dimensions: dict[str, tuple[float, float]]) -> None:
    require(content.startswith("<!doctype html>"), "generated HTML requires an HTML doctype")
    parser = html5lib.HTMLParser(tree=html5lib.getTreeBuilder("etree"), strict=False,
                                 namespaceHTMLElements=False)
    try:
        document = parser.parse(content)
    except html5lib.html5parser.ParseError as error:
        fail(f"generated HTML is not valid HTML: {error}")
    except ValueError as error:
        fail(f"generated HTML is not valid HTML: {error}")
    if parser.errors:
        findings = ", ".join(f"{position[0]}:{position[1]} {code}"
                             for position, code, _data in parser.errors[:10])
        fail(f"generated HTML has parse errors: {findings}")
    for node in document.iter():
        require(not is_comment(node), "generated HTML contains a comment")
    root_children = checked_children(
        document, "html", {"lang": manifest["document"]["language"]}, "generated <html>")
    require([local_name(node) for node in root_children] == ["head", "body"],
            "generated HTML must contain one head followed by one body")
    head, body = root_children
    head_children = checked_children(head, "head", {}, "generated <head>")
    require([local_name(node) for node in head_children] == ["meta", "title", "link"],
            "generated head structure is invalid")
    meta, title, link = head_children
    checked_children(meta, "meta", {"charset": "utf-8"}, "generated charset metadata", text="")
    checked_children(title, "title", {"lang": manifest["document"]["title_language"]},
                     "generated title", text=manifest["document"]["title"])
    checked_children(link, "link", {"rel": "stylesheet",
                                    "href": manifest["outputs"]["css"]["path"]},
                     "generated stylesheet link", text="")
    body_children = checked_children(body, "body", {}, "generated <body>")
    require(len(body_children) == 1 and local_name(body_children[0]) == "main",
            "generated body must contain one main")
    main = body_children[0]
    sections = checked_children(main, "main", {"id": manifest["publication_id"]}, "generated <main>")
    expected_fragments = manifest["fragments"]
    require(len(sections) == len(expected_fragments), "generated fragment section count is inconsistent")
    for record, section in zip(expected_fragments, sections, strict=True):
        identifier = record["id"]
        checked_children(section, "section", {"data-fragment-id": identifier},
                         f"fragment section {identifier}", mixed=True)
        require(fragment_signature(section, generated=True)
                == fragment_signature(fragments[identifier].root, generated=False),
                f"fragment section {identifier} content is inconsistent")
    observed_figures = [node for section in sections for node in section.iter()
                        if local_name(node) == "figure"]
    require([node.attrib.get("data-figure-id") for node in observed_figures]
            == [figure["id"] for figure in manifest["figures"]],
            "generated figure order is inconsistent")
    for figure, node in zip(manifest["figures"], observed_figures, strict=True):
        validate_figure_element(node, figure, captions[figure["id"]], svg_dimensions)

def validate_pdf(path: Path, context: str) -> None:
    content = read_regular(path, context)
    require(bool(content), f"{context} is empty")
    try:
        import fitz
    except ImportError:
        fail("PyMuPDF is unavailable; run with uv run --script")
    try:
        document = fitz.open(stream=content, filetype="pdf")
    except (OSError, RuntimeError, ValueError) as error:
        fail(f"{context} is not a parseable PDF: {error}")
    try:
        try:
            require(document.is_pdf and document.page_count >= 1, f"{context} is not a nonempty PDF")
            require(document.page_count <= MAX_PDF_PAGES,
                    f"{context} exceeds the {MAX_PDF_PAGES}-page validation ceiling")
            for page_index in range(document.page_count):
                document.load_page(page_index)
        except (fitz.mupdf.FzErrorBase, OSError, RuntimeError, ValueError) as error:
            fail(f"{context} cannot load all PDF pages: {error}")
    finally:
        document.close()

def validate_publication(manifest_path: Path) -> ValidationResult:
    entry = manifest_path.expanduser().absolute()
    require(entry.name == MANIFEST_NAME, f"manifest must be named {MANIFEST_NAME}")
    require(stat.S_ISDIR(checked_node(entry.parent, "publication root").st_mode),
            f"publication root is not a directory: {entry.parent}")
    manifest_bytes = read_regular(entry, "assembly manifest")
    resolved = entry.resolve(strict=True)
    manifest = parse_json_bytes(manifest_bytes, "assembly manifest")
    validate_schema(manifest, "assembly-manifest.schema.json", "assembly manifest")
    require(manifest["policies"] == publication_policies(),
            "assembly manifest policies do not match bundled profiles")
    inventory = validate_manifest_inventory(resolved.parent, manifest)
    profile, _profile_content = load_profile()
    fragments, captions, svg_dimensions, stylesheets = load_manifest_material(
        manifest,
        inventory,
        profile,
    )
    outputs = manifest["outputs"]
    require(outputs["html"]["path"] == "index.html", "manifest HTML output must be index.html")
    require(outputs["css"]["path"] == "assets/print.css",
            "manifest CSS output must be assets/print.css")
    html = inventory[outputs["html"]["path"]]
    css = inventory[outputs["css"]["path"]]
    try:
        validate_generated_html(read_utf8_asset(html, "HTML output"), manifest, fragments,
                                captions, svg_dimensions)
    except RecursionError as error:
        fail(f"generated HTML exceeds runtime validation limits: {error}")
    validate_generated_css(read_utf8_asset(css, "CSS output"), manifest, stylesheets)
    draft_pdf = outputs["draft_pdf"]
    if isinstance(draft_pdf, dict):
        pdf = inventory[draft_pdf["path"]]
        require(pdf.path.suffix.casefold() == ".pdf", "draft PDF must use a .pdf extension")
        validate_pdf(pdf.path, "draft PDF")
    summary = {
        "manifest": str(resolved), "publication_id": manifest["publication_id"],
        "fragments": len(manifest["fragments"]), "figures": len(manifest["figures"]),
        "crops": sum(len(figure["parts"]) for figure in manifest["figures"]),
        "pdf": draft_pdf["path"] if isinstance(draft_pdf, dict) else None, "status": "valid"}
    return ValidationResult(resolved, manifest_bytes, manifest, inventory, summary)

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

def select_browser(requested: Path | None) -> Path:
    if requested is not None:
        path = requested.expanduser().resolve(strict=True)
        require(stat.S_ISREG(path.stat().st_mode), f"browser is not a regular file: {path}")
        return path
    candidates = browser_candidates()
    if candidates:
        return candidates[0]
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        fail("Playwright is unavailable; run with uv run --script")
    with sync_playwright() as playwright:
        bundled = Path(playwright.chromium.executable_path)
    if bundled.is_file():
        return bundled.resolve()
    fail("no Chromium browser was found; install Playwright Chromium, pass --browser, or set SCHOLARLY_PUBLICATION_BROWSER")

def file_url_path(value: str) -> Path | None:
    parsed = urlsplit(value)
    if parsed.scheme.casefold() != "file" or parsed.query or parsed.netloc not in {"", "localhost"}:
        return None
    return Path(url2pathname(parsed.path)).resolve()

def render_pdf(html_path: Path, output_path: Path, browser_path: Path, allowed_files: set[Path]) -> None:
    try:
        from playwright.async_api import Error as PlaywrightError
        from playwright.async_api import TimeoutError as PlaywrightTimeoutError
        from playwright.async_api import async_playwright
    except ImportError:
        fail("Playwright is unavailable; run with uv run --script")
    blocked: list[str] = []

    async def route_request(route: Any, request: Any) -> None:
        url = request.url
        local = file_url_path(url)
        if url == "about:blank" or (local is not None and local in allowed_files):
            await route.continue_()
        else:
            blocked.append(url)
            await route.abort()

    async def render() -> None:
        try:
            async with async_playwright() as playwright:
                browser = await playwright.chromium.launch(
                    executable_path=str(browser_path),
                    headless=True,
                    args=["--disable-background-networking", "--disable-component-update",
                          "--disable-default-apps", "--disable-extensions", "--disable-sync",
                          "--no-first-run"])
                try:
                    context = await browser.new_context(java_script_enabled=False, service_workers="block")
                    try:
                        await context.route("**/*", route_request)
                        page = await context.new_page()
                        await page.goto(html_path.as_uri(), wait_until="networkidle",
                                        timeout=NAVIGATION_TIMEOUT_MS)
                        require(not blocked,
                                "Chromium requested files outside the manifest: "
                                + ", ".join(sorted(set(blocked))))
                        await page.emulate_media(media="print")
                        try:
                            async with asyncio.timeout(PDF_TIMEOUT_MS / 1000):
                                await page.pdf(path=str(output_path), print_background=True,
                                               prefer_css_page_size=True,
                                               display_header_footer=False)
                        except TimeoutError:
                            fail(f"Chromium PDF render exceeded the fixed {PDF_TIMEOUT_MS} ms deadline")
                    finally:
                        await context.close()
                finally:
                    await browser.close()
        except (PlaywrightError, PlaywrightTimeoutError) as error:
            fail(f"Chromium PDF render failed: {error}")

    asyncio.run(render())

def publish_render(result: ValidationResult, pdf_path: Path, temporary_pdf: Path) -> None:
    pdf_record = asset_from_bytes(
        pdf_path.relative_to(result.manifest_path.parent).as_posix(),
        read_regular(temporary_pdf, "rendered PDF"))
    updated = json.loads(json.dumps(result.manifest))
    updated["outputs"]["draft_pdf"] = pdf_record
    validate_schema(updated, "assembly-manifest.schema.json", "rendered assembly manifest")
    semantic_asset_map(updated)
    moved = False
    try:
        pdf_path.parent.mkdir(parents=True, exist_ok=True)
        temporary_pdf.replace(pdf_path)
        moved = True
        write_atomic(result.manifest_path, json_bytes(updated))
    except (ContractError, OSError, KeyboardInterrupt) as error:
        cleanup_errors: list[str] = []
        try:
            if moved or pdf_path.exists():
                pdf_path.unlink(missing_ok=True)
        except OSError as cleanup_error:
            cleanup_errors.append(f"cannot remove rendered PDF: {cleanup_error}")
        try:
            if read_regular(result.manifest_path, "assembly manifest") != result.manifest_bytes:
                write_atomic(result.manifest_path, result.manifest_bytes)
        except (ContractError, OSError) as cleanup_error:
            cleanup_errors.append(f"cannot restore assembly manifest: {cleanup_error}")
        if cleanup_errors:
            fail(f"cannot restore render after {error}; {'; '.join(cleanup_errors)}")
        if isinstance(error, KeyboardInterrupt):
            raise
        raise

def render_publication(args: argparse.Namespace) -> int:
    html_entry = args.html.expanduser().absolute()
    require(stat.S_ISDIR(checked_node(html_entry.parent, "publication root").st_mode),
            f"publication root is not a directory: {html_entry.parent}")
    read_regular(html_entry, "HTML input")
    root = html_entry.parent.resolve(strict=True)
    requested_pdf = args.pdf.expanduser()
    if os.name == "nt" and any(
        part not in {".", ".."} and part.rstrip(" .") != part
        for part in requested_pdf.parts
    ):
        fail("Windows draft PDF path components must not end in a dot or space")
    requested_pdf = requested_pdf.resolve(strict=False)
    try:
        pdf_relative = requested_pdf.relative_to(root)
    except ValueError:
        fail(f"draft PDF must stay beneath the publication root: {root}")
    pdf_identity = normalized_relative_path(pdf_relative.as_posix(), "draft PDF path")
    pdf_path = root.joinpath(*PurePosixPath(pdf_identity).parts)
    reject_existing(pdf_path, f"draft PDF destination already exists: {pdf_path}")
    result = validate_publication(root / MANIFEST_NAME)
    html_path = html_entry.resolve(strict=True)
    expected_html = result.manifest["outputs"]["html"]["path"]
    require(html_path == root / Path(*PurePosixPath(expected_html).parts),
            "--html must name the manifest-bound canonical HTML")
    require(pdf_path.suffix.casefold() == ".pdf", "draft PDF path must use a .pdf extension")
    require(result.manifest["outputs"]["draft_pdf"] is None, "assembly already has a draft PDF")
    browser_path = select_browser(args.browser)
    allowed_files = {result.inventory[identity].path.resolve()
                     for identity in semantic_asset_map(result.manifest)}
    with tempfile.NamedTemporaryFile(prefix=f".{pdf_path.name}.render-", suffix=".pdf", dir=root.parent, delete=False) as stream:
        temporary_pdf = Path(stream.name)
    temporary_pdf.unlink()
    try:
        render_pdf(html_path, temporary_pdf, browser_path, allowed_files)
        validate_pdf(temporary_pdf, "rendered PDF")
        pdf_sha256 = sha256_bytes(temporary_pdf.read_bytes())
        publish_render(result, pdf_path, temporary_pdf)
    finally:
        temporary_pdf.unlink(missing_ok=True)
    print_json({"committed": True, "pdf": str(pdf_path), "sha256": pdf_sha256, "status": "rendered"})
    return 0

def validate_command(args: argparse.Namespace) -> int:
    print_json(validate_publication(args.manifest).summary)
    return 0

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Build, render, and validate a scholarly print assembly.")
    subparsers = parser.add_subparsers(dest="command", required=True)
    build = subparsers.add_parser("build", help="build a publication tree")
    build.add_argument("--spec", type=Path, required=True)
    build.add_argument("--output", type=Path, required=True)
    build.set_defaults(handler=build_publication)
    render = subparsers.add_parser("render", help="render the canonical HTML")
    render.add_argument("--html", type=Path, required=True)
    render.add_argument("--pdf", type=Path, required=True)
    render.add_argument("--browser", type=Path)
    render.set_defaults(handler=render_publication)
    validate = subparsers.add_parser("validate", help="validate an assembled publication tree")
    validate.add_argument("--manifest", type=Path, required=True)
    validate.set_defaults(handler=validate_command)
    return parser

def main(argv: list[str] | None=None) -> int:
    args = build_parser().parse_args(argv)
    handler = args.handler
    try:
        return handler(args)
    except (ContractError, OSError, UnicodeError) as error:
        eprint(f"error: {error}")
        return 2
if __name__ == "__main__":
    raise SystemExit(main())
# fmt: on
