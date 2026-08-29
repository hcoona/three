# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "cssselect2==0.8.0",
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
import uuid
from dataclasses import dataclass
from html import escape
from io import BytesIO
from pathlib import Path, PurePosixPath
from typing import Any, Never
from urllib.parse import unquote, urlsplit
from urllib.request import url2pathname
from xml.etree.ElementTree import Comment, Element, ParseError

import tinycss2
from defusedxml.common import DefusedXmlException
from defusedxml.ElementTree import iterparse
from fontTools.ttLib import TTFont, TTLibError
from jsonschema import Draft202012Validator

html5lib: Any = importlib.import_module("html5lib")
cssselect2: Any = importlib.import_module("cssselect2")

SCRIPT_VERSION = "0.1.0"
ASSETS = Path(__file__).resolve().parents[1] / "assets"
MANIFEST_NAME = "assembly-manifest.json"
PROFILE_NAME = "publication-profile.json"
PROFILE_ID = "scholarly-fragment-and-stylesheet-v1"
GENERATED_DOCUMENT_PROFILE = "scholarly-generated-document-v1"
GENERATED_CSS_PROFILE = "scholarly-generated-css-v1"
FONT_ROLES = ("body-cjk", "body-latin")
PAGE_SIZES = {"letter": "Letter", "a4": "A4"}
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
LIST_MARKER_CHARACTERS = {
    "none": "",
    "disc": "\u2022",
    "circle": "\u25e6",
    "square": "\u25aa",
    "decimal": "0123456789.-",
    "decimal-leading-zero": "0123456789.-",
    "lower-alpha": "abcdefghijklmnopqrstuvwxyz0123456789.-",
    "upper-alpha": "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-",
    "lower-roman": "ivxlcdm0123456789.-",
    "upper-roman": "IVXLCDM0123456789.-",
    "hiragana": "\u3042\u3044\u3046\u3048\u304a\u304b\u304d\u304f\u3051\u3053\u3055\u3057\u3059\u305b\u305d\u305f\u3061\u3064\u3066\u3068\u306a\u306b\u306c\u306d\u306e\u306f\u3072\u3075\u3078\u307b\u307e\u307f\u3080\u3081\u3082\u3084\u3086\u3088\u3089\u308a\u308b\u308c\u308d\u308f\u3090\u3091\u3092\u30930123456789.-\u3001",
    "katakana": "\u30a2\u30a4\u30a6\u30a8\u30aa\u30ab\u30ad\u30af\u30b1\u30b3\u30b5\u30b7\u30b9\u30bb\u30bd\u30bf\u30c1\u30c4\u30c6\u30c8\u30ca\u30cb\u30cc\u30cd\u30ce\u30cf\u30d2\u30d5\u30d8\u30db\u30de\u30df\u30e0\u30e1\u30e2\u30e4\u30e6\u30e8\u30e9\u30ea\u30eb\u30ec\u30ed\u30ef\u30f0\u30f1\u30f2\u30f30123456789.-\u3001",
    "cjk-ideographic": "\u3007\u96f6\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341\u767e\u5343\u4e07\u842c\u4ebf\u5104\u5146\u8d1f\u8ca00123456789.-\u3001",
    "simp-chinese-informal": "\u96f6\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341\u767e\u5343\u4e07\u4ebf\u5146\u8d1f0123456789.-\u3001",
    "trad-chinese-informal": "\u96f6\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341\u767e\u5343\u842c\u5104\u5146\u8ca00123456789.-\u3001",
}
OL_TYPE_STYLES = {"1": "decimal", "a": "lower-alpha", "A": "upper-alpha", "i": "lower-roman", "I": "upper-roman"}
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
    consumed_paths: list[Path]

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
        if key in result:
            fail(f"duplicate JSON object key: {key}")
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
    if stat.S_ISLNK(info.st_mode) or reparse:
        fail(f"{context} is a link or reparse point: {path}")
    return info

def read_regular(path: Path, context: str) -> bytes:
    candidate = path.expanduser().absolute()
    try:
        if not stat.S_ISREG(checked_node(candidate, context).st_mode):
            fail(f"{context} is not a regular file: {candidate}")
        return candidate.read_bytes()
    except OSError as error:
        fail(f"cannot read {context} {path}: {error}")

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
    if not stat.S_ISREG(resolved.stat().st_mode):
        fail(f"{context} is not a regular file: {resolved}")
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
    if record.get("bytes") != len(content):
        fail(f"{context} byte length does not match its manifest record")
    if record.get("sha256") != sha256_bytes(content):
        fail(f"{context} SHA-256 does not match its manifest record")
    return (path, content)

def scan_tree(root: Path) -> dict[str, FileState]:
    candidate = root.expanduser().absolute()
    if not stat.S_ISDIR(checked_node(candidate, "publication root").st_mode):
        fail(f"publication root is not a directory: {candidate}")
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
                content = child.read_bytes()
                inventory[child.relative_to(resolved).as_posix()] = FileState(child, len(content), sha256_bytes(content))
            else:
                fail(f"publication tree contains a special node: {child}")
    return dict(sorted(inventory.items()))

def verify_asset_record(record: Any, inventory: dict[str, FileState], context: str) -> FileState:
    if not isinstance(record, dict) or set(record) != {"path", "sha256", "bytes"}:
        fail(f"{context} must be an exact asset record")
    identity = normalized_relative_path(record["path"], f"{context} path")
    state = inventory.get(identity)
    if state is None:
        fail(f"{context} is missing from the publication tree: {identity}")
    if record["bytes"] != state.size or record["sha256"] != state.sha256:
        fail(f"{context} content does not match {identity}")
    return state

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
    if any(unicodedata.category(character) in {"Cc", "Cf", "Cs"} for character in text):
        fail(f"{context} contains a CSS-unsafe control, format, or surrogate character")
    return text

def validate_identifier(value: Any, context: str) -> str:
    text = ensure_utf8_text(value, context, nonempty=True)
    if not ID_PATTERN.fullmatch(text):
        fail(f"{context} is not a canonical identifier: {text!r}")
    return text

def validate_language(value: Any, context: str) -> str:
    text = ensure_utf8_text(value, context, nonempty=True)
    if not LANGUAGE_PATTERN.fullmatch(text):
        fail(f"{context} is not a supported language tag: {text!r}")
    return text

def finite_number(value: Any, context: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        fail(f"{context} must be a number")
    try:
        number = float(value)
    except OverflowError:
        fail(f"{context} must be representable as a finite number")
    if not math.isfinite(number):
        fail(f"{context} must be finite")
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
                    validate_attribute_value(name, value, f"{context} <{tag}> {name}")
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
    validate_attribute_value("class", text, context)
    tokens = text.split()
    require(len(tokens) == len(set(tokens)), f"{context} contains duplicate class tokens")
    reserved = GENERATED_CLASSES.intersection(tokens)
    require(not reserved, f"{context} uses assembler-owned classes: " + ", ".join(sorted(reserved)))
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
) -> None:
    if token.type == "number" and css_number(token, context) == 0:
        require(minimum <= 0 <= maximum, f"{context} zero is outside the allowed range")
        return
    if token.type == "dimension":
        require(str(token.lower_unit) in units, f"{context} uses a disallowed length unit")
        require(minimum <= css_number(token, context) <= maximum, f"{context} length is outside the allowed range")
        return
    if maximum_percentage is not None and token.type == "percentage":
        require(minimum <= css_number(token, context) <= maximum_percentage,
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
            validate_css_length(token, context, minimum=0.8, maximum=4, maximum_percentage=4)
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

def resolved_font_value(name: str, tokens: list[Any], inherited: Any, roles: dict[str, str], context: str) -> Any:  # noqa: PLR0911
    compact = css_tokens(tokens)
    require(bool(compact), f"{context} has an empty {name}")
    keyword = css_ident(compact[0]) if len(compact) == 1 else None
    if keyword in {"inherit", "unset"}:
        return inherited
    if keyword == "initial":
        return (None,) if name == "font-family" else ("normal" if name == "font-style" else 400)
    if name == "font-family":
        if len(compact) == 1 and compact[0].type == "function" and compact[0].lower_name == "var":
            arguments = css_tokens(compact[0].arguments)
            require(len(arguments) == 1 and arguments[0].type == "ident", f"{context} has an invalid font role")
            role = str(arguments[0].value).casefold()
            require(role in roles, f"{context} references an unknown font role")
            return (roles[role],)
        families: list[str | None] = []
        for group in split_css(tokens, ","):
            family = css_tokens(group)
            if len(family) == 1 and family[0].type == "string":
                families.append(str(family[0].value).casefold())
            else:
                require(bool(family) and all(token.type == "ident" for token in family), f"{context} has an invalid font family")
                value = " ".join(str(token.value) for token in family).casefold()
                families.append(None if len(family) == 1 and value in {"monospace", "sans-serif", "serif"} else value)
        return tuple(families)
    if name == "font-style":
        require(keyword in {"normal", "italic"}, f"{context} has an unsupported font style")
        return keyword
    if keyword == "normal":
        return 400
    if keyword == "bold":
        return 700
    token = one_css_token(compact, context)
    require(token.type == "number" and token.is_integer, f"{context} has an unsupported font weight")
    return int(css_number(token, context))

def resolved_inherited_keyword(tokens: list[Any], inherited: str, initial: str, context: str) -> str:
    token = one_css_token(css_tokens(tokens), context)
    value = css_ident(token)
    if value is None:
        fail(f"{context} requires a keyword")
    if value in {"inherit", "unset"}:
        return inherited
    return initial if value == "initial" else value

def require_font_glyphs(characters: str, selected: tuple[tuple[str | None, ...], str, int],
                        coverage: dict[tuple[str, str, int], frozenset[int]], context: str) -> None:
    for codepoint in sorted({ord(character) for character in characters if not character.isspace()}):
        for family in selected[0]:
            if family is None:
                fail(f"{context} reaches a generic or initial font family")
            face = (family, selected[1], selected[2])
            require(face in coverage, f"{context} selects undeclared font face {family!r} {selected[1]} {selected[2]}")
            if codepoint in coverage[face]:
                break
        else:
            fail(f"{context} has no declared font glyph for U+{codepoint:04X}")

def validate_selected_font_faces(html_content: bytes, css_content: bytes, roles: JsonObject,
                                 coverage: dict[tuple[str, str, int], frozenset[int]]) -> None:
    parser = html5lib.HTMLParser(tree=html5lib.getTreeBuilder("etree"), strict=False, namespaceHTMLElements=False)
    document = parser.parse(html_content.decode())
    require(not parser.errors, "generated HTML cannot be parsed for font selection")
    matcher = cssselect2.Matcher()
    for rule_index, rule in enumerate(tinycss2.parse_stylesheet(css_content.decode(), skip_comments=True, skip_whitespace=True), start=1):
        if rule.type == "error":
            fail(f"generated CSS rule {rule_index} cannot be parsed for font selection")
        if rule.type != "qualified-rule":
            continue
        declarations: dict[str, list[Any]] = {}
        for declaration in tinycss2.parse_declaration_list(rule.content, skip_comments=True, skip_whitespace=True):
            if declaration.type == "declaration" and declaration.lower_name in {"font-family", "font-style", "font-weight", "hyphens", "list-style-type"}:
                declarations[str(declaration.lower_name)] = declaration.value
        if not declarations:
            continue
        try:
            selectors = cssselect2.compile_selector_list(tinycss2.serialize(rule.prelude))
        except cssselect2.SelectorError as error:
            fail(f"generated CSS rule {rule_index} has an invalid selector: {error}")
        for selector in selectors:
            require(selector.pseudo_element is None, f"generated CSS rule {rule_index} has a font-selecting pseudo-element")
            matcher.add_selector(selector, declarations)
    role_values = {"--body-cjk": str(roles["body-cjk"]).casefold(),
                   "--body-latin": str(roles["body-latin"]).casefold()}
    computed: dict[Element, tuple[tuple[str | None, ...], str, int, str, str]] = {}
    in_body: dict[Element, bool] = {}
    wrapped = cssselect2.ElementWrapper.from_html_root(document)
    for node in wrapped.iter_subtree():
        element = node.etree_element
        parent = node.parent.etree_element if node.parent is not None else None
        inherited = computed[parent] if parent is not None else ((role_values["--body-latin"],), "normal", 400, "manual", "disc")
        tag = element.tag if isinstance(element.tag, str) else ""
        local = tag.rsplit("}", maxsplit=1)[-1]
        values: list[Any] = list(inherited)
        if local == "ul":
            values[4] = "disc"
        elif local == "ol":
            values[4] = OL_TYPE_STYLES.get(element.attrib.get("type", "1"), "decimal")
        for _specificity, _order, _pseudo, declarations in matcher.match(node):
            for index, name in enumerate(("font-family", "font-style", "font-weight")):
                if name in declarations:
                    values[index] = resolved_font_value(name, declarations[name], inherited[index], role_values, f"generated CSS {name}")
            if "hyphens" in declarations:
                values[3] = resolved_inherited_keyword(declarations["hyphens"], inherited[3], "manual", "generated CSS hyphens")
            if "list-style-type" in declarations:
                values[4] = resolved_inherited_keyword(declarations["list-style-type"], inherited[4], "disc", "generated CSS list-style-type")
        selected = (tuple(values[0]), str(values[1]), int(values[2]), str(values[3]), str(values[4]))
        computed[element] = selected
        body = local == "body" or (parent is not None and in_body.get(parent, False))
        in_body[element] = body
        text = (element.text or "") + "".join(child.tail or "" for child in list(element))
        face = (selected[0], selected[1], selected[2])
        if body and not tag.startswith("{"):
            if text.strip():
                require_font_glyphs(text, face, coverage, f"rendered <{local}> text")
                if selected[3] == "auto" or (selected[3] == "manual" and "\u00ad" in text):
                    require_font_glyphs("-", face, coverage, f"rendered <{local}> hyphen")
            if local == "q":
                require_font_glyphs("\u201c\u201d\u2018\u2019", face, coverage, "generated quotation marks")
            if local == "li":
                require_font_glyphs(LIST_MARKER_CHARACTERS[selected[4]], face, coverage, f"generated {selected[4]} list marker")

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

def validate_page_svg(content: bytes, page: JsonObject, context: str) -> None:
    try:
        text = content.decode()
    except UnicodeDecodeError as error:
        fail(f"{context} is not UTF-8: {error}")
    if "<!--" in text or "<?" in text or "<![CDATA[" in text:
        fail(f"{context} contains XML comments, instructions, or CDATA")
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
    if namespace != SVG_NAMESPACE or local != "svg":
        fail(f"{context} root must be an SVG namespace <svg>")
    width = svg_length(root.attrib.get("width"), f"{context} width")
    height = svg_length(root.attrib.get("height"), f"{context} height")
    page_width = finite_number(page["width"], f"{context} source page width")
    page_height = finite_number(page["height"], f"{context} source page height")
    if (
        f"{width:.3f}" != f"{page_width:.3f}"
        or f"{height:.3f}" != f"{page_height:.3f}"
    ):
        fail(f"{context} dimensions do not match the source package page")
    view_box = root.attrib.get("viewBox")
    if not isinstance(view_box, str):
        fail(f"{context} requires a viewBox")
    try:
        values = [float(value) for value in re.split("[\\s,]+", view_box.strip())]
    except ValueError as error:
        fail(f"{context} has an invalid viewBox: {error}")
    if (
        len(values) != 4
        or any(not math.isfinite(value) for value in values)
        or any(
            abs(actual - expected) > 0.001
            for actual, expected in zip(
                values,
                (0.0, 0.0, width, height),
                strict=True,
            )
        )
    ):
        fail(f"{context} viewBox does not match the source page")
    identifiers: set[str] = set()
    references: set[str] = set()
    for node in root.iter():
        if not isinstance(node.tag, str):
            fail(f"{context} contains unsupported XML nodes")
        node_namespace, node_name = split_xml_name(node.tag)
        if node_namespace != SVG_NAMESPACE or node_name not in SVG_ELEMENTS:
            fail(f"{context} contains undeclared SVG element <{node_name}>")
        if node.text and node.text.strip():
            fail(f"{context} contains unexpected SVG text nodes")
        if node.tail and node.tail.strip():
            fail(f"{context} contains unexpected SVG tail text")
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
                if (svg_css_is_obfuscated(value) or node_name != "g" or match is None
                        or match.group(1).casefold() not in SVG_STANDARD_BLEND_MODES):
                    fail(f"{context} allows only a standard mix-blend-mode style on <g>")
                continue
            if attribute_name == "href":
                if node_name not in {"image", "linearGradient", "pattern", "radialGradient", "use"}:
                    fail(f"{context} contains href on unsupported SVG element <{node_name}>")
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
                if (media == "png" and not decoded.startswith(b"\x89PNG\r\n\x1a\n")) or (
                    media == "jpeg" and not decoded.startswith(b"\xff\xd8\xff")
                ):
                    fail(f"{context} embedded image signature does not match its media type")
                continue
            if attribute_name not in SVG_ATTRIBUTES:
                fail(f"{context} contains undeclared SVG attribute {attribute_name!r}")
            if attribute_name == "id":
                if SVG_FRAGMENT_PATTERN.fullmatch(f"#{value}") is None:
                    fail(f"{context} contains invalid SVG id {value!r}")
                if value in identifiers:
                    fail(f"{context} contains duplicate SVG id {value!r}")
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
    if missing:
        fail(f"{context} references missing SVG IDs: {', '.join(missing)}")

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
    by_id = index_objects(pages, "id", "source page")
    by_number = index_objects(pages, "pdf_page", "source page")
    return (by_id, by_number)

def validate_source_and_bundle(source: Snapshot, bundle: Snapshot, recipe: Recipe, *,
                               require_original_binding: bool) -> tuple[dict[str, JsonObject], dict[int, JsonObject], dict[str, JsonObject]]:
    validate_schema(source.value, "source-package.schema.json", "source package")
    validate_schema(bundle.value, "translation-bundle.schema.json", "translation bundle")
    if (source.value["status"] != "pass" or source.value["issues"]
            or any(page["status"] != "pass" for page in source.value["pages"])):
        fail("source package effective status must be pass")
    source_record = bundle.value["source_package"]
    if (source_record["sha256"] != sha256_bytes(source.content)
            or source_record["bytes"] != len(source.content)):
        fail("translation bundle source-package hash binding does not match")
    if require_original_binding:
        bound_source = resolve_relative(bundle.path.parent, source_record["path"],
                                        "translation bundle source_package")
        if bound_source != source.path:
            fail("translation bundle source_package path does not resolve to the assembly source package")
    if bundle.value["target_language"] != recipe.value["language"]:
        fail("translation target language does not match assembly language")
    missing_profiles = sorted(set(source.value["profiles"]) - set(recipe.value["profiles"]))
    if missing_profiles:
        fail("assembly profiles omit source profiles: " + ", ".join(missing_profiles))
    pages_by_id, pages_by_number = source_pages(source.value)
    fragments = index_objects(bundle.value["fragments"], "id", "translation fragment")
    order = recipe.value["fragment_order"]
    missing_fragments = [identifier for identifier in order if identifier not in fragments]
    if missing_fragments:
        fail("assembly fragment_order references absent approved fragments: " + ", ".join(missing_fragments))
    for identifier in order:
        for block_id in fragments[identifier]["source_block_ids"]:
            match = BLOCK_ID_PATTERN.fullmatch(block_id)
            if match is None:
                fail(f"fragment {identifier} has an invalid source block ID")
            page = pages_by_id.get(match.group("page"))
            if page is None:
                fail(f"fragment {identifier} references an absent source page")
            if page["status"] != "pass":
                fail(f"fragment {identifier} references a non-passing source page")
    return (pages_by_id, pages_by_number, fragments)

def load_assembly(spec: Snapshot, *, source: Snapshot | None = None, bundle: Snapshot | None = None,
                  require_original_binding: bool) -> Assembly:
    profile, _profile_content = load_profile()
    recipe = validate_recipe(spec.value, profile)
    if source is None:
        source_path = resolve_relative(spec.path.parent, recipe.value["source_package"],
                                       "assembly source package")
        source = read_snapshot(source_path, "source package")
    if bundle is None:
        bundle_path = resolve_relative(spec.path.parent, recipe.value["translation_bundle"],
                                       "assembly translation bundle")
        bundle = read_snapshot(bundle_path, "translation bundle")
    pages_by_id, pages_by_number, fragments = validate_source_and_bundle(
        source, bundle, recipe, require_original_binding=require_original_binding)
    return Assembly(spec, recipe, source, bundle, profile, pages_by_id, pages_by_number, fragments)

def load_fragments(assembly: Assembly) -> tuple[dict[str, Markup], dict[str, bytes], list[Path]]:
    markups: dict[str, Markup] = {}
    contents: dict[str, bytes] = {}
    paths: list[Path] = []
    claimed_paths: set[Path] = set()
    for identifier in assembly.recipe.value["fragment_order"]:
        record = assembly.bundle_fragments[identifier]
        path, content = read_bound_asset(assembly.bundle.path.parent, record, f"approved fragment {identifier}")
        if path in claimed_paths:
            fail("distinct selected fragment IDs cannot share one asset path")
        claimed_paths.add(path)
        try:
            text = content.decode()
        except UnicodeDecodeError as error:
            fail(f"approved fragment {identifier} is not UTF-8: {error}")
        markup = parse_markup(text, assembly.profile, f"approved fragment {identifier}", allow_figure_markers=True)
        if markup.visible_sha256 != record["visible_text_sha256"]:
            fail(f"approved fragment {identifier} visible-text hash does not match")
        markups[identifier] = markup
        contents[identifier] = content
        paths.append(path)
    return (markups, contents, paths)

def validate_consumed_blocks(assembly: Assembly) -> list[Path]:
    requested: dict[str, set[str]] = {}
    for identifier in assembly.recipe.value["fragment_order"]:
        for block_id in assembly.bundle_fragments[identifier]["source_block_ids"]:
            match = BLOCK_ID_PATTERN.fullmatch(block_id)
            if match is None:
                fail(f"fragment {identifier} has an invalid source block ID")
            requested.setdefault(match.group("page"), set()).add(block_id)
    paths: list[Path] = []
    for page_id, block_ids in requested.items():
        page = assembly.pages_by_id[page_id]
        path, content = read_bound_asset(assembly.source.path.parent, page["assets"]["blocks"], f"source blocks for {page_id}")
        blocks = parse_json_bytes(content, f"source blocks for {page_id}")
        validate_schema(blocks, "source-blocks.schema.json", f"source blocks for {page_id}")
        if blocks["page_id"] != page_id:
            fail(f"source block file page_id does not match {page_id}")
        available = set(index_objects(blocks["blocks"], "id", "source block"))
        missing = sorted(block_ids - available)
        if missing:
            fail(f"selected source blocks are absent on {page_id}: " + ", ".join(missing))
        paths.append(path)
    return paths

def validated_bbox(value: Any, page: JsonObject, context: str) -> list[float]:
    if not isinstance(value, list) or len(value) != 4:
        fail(f"{context} bbox must contain four numbers")
    bbox = [finite_number(number, f"{context} bbox coordinate") for number in value]
    x0, y0, x1, y1 = bbox
    width = finite_number(page["width"], f"{context} page width")
    height = finite_number(page["height"], f"{context} page height")
    if x0 < 0 or y0 < 0 or x1 <= x0 or (y1 <= y0) or x1 > width or y1 > height:
        fail(f"{context} bbox is outside the source page")
    canonical = [float(f"{number:.3f}") for number in bbox]
    if canonical[2] <= canonical[0] or canonical[3] <= canonical[1]:
        fail(f"{context} bbox collapses under three-decimal canonicalization")
    return canonical

def load_figures(assembly: Assembly) -> tuple[list[JsonObject], dict[str, tuple[JsonObject, bytes]], list[Path]]:
    declarations = assembly.recipe.value["figures"]
    if not declarations:
        return ([], {}, [])
    figure_map_record = assembly.source.value.get("figure_map")
    if not isinstance(figure_map_record, dict):
        fail("assembly figures require a source-package figure_map")
    map_path, map_content = read_bound_asset(assembly.source.path.parent, figure_map_record, "source figure map")
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
    consumed_paths = [map_path]
    part_ids: set[str] = set()
    for declaration in declarations:
        identifier = declaration["id"]
        source_figure = figures_by_id.get(identifier)
        if source_figure is None:
            fail(f"assembly figure is absent from the figure map: {identifier}")
        source_profile = source_figure.get("profile")
        if isinstance(source_profile, str) and source_profile and (source_profile not in assembly.recipe.value["profiles"]):
            fail(f"figure {identifier} requires omitted profile {source_profile!r}")
        parts = source_figure["parts"]
        if [part["order"] for part in parts] != list(range(1, len(parts) + 1)):
            fail(f"figure {identifier} parts are not in canonical order")
        for part in parts:
            part_id = part["id"]
            if part_id in part_ids:
                fail(f"duplicate source figure part id: {part_id}")
            part_ids.add(part_id)
            page = assembly.pages_by_number.get(part["pdf_page"])
            if page is None:
                fail(f"figure part {part_id} references an absent page")
            if page["status"] != "pass":
                fail(f"figure part {part_id} references a non-passing page")
            validated_bbox(part["bbox"], page, f"figure part {part_id}")
            page_id = page["id"]
            if page_id not in used_pages:
                svg_path, svg_content = read_bound_asset(assembly.source.path.parent, page["assets"]["svg"], f"source page SVG {page_id}")
                validate_page_svg(svg_content, page, f"source page SVG {page_id}")
                used_pages[page_id] = (page, svg_content)
                consumed_paths.append(svg_path)
        selected.append(source_figure)
    return (selected, used_pages, consumed_paths)

def inspect_font_bytes(content: bytes, context: str) -> tuple[JsonObject, frozenset[int]]:
    font: TTFont | None = None
    try:
        font = TTFont(BytesIO(content), lazy=False, fontNumber=0)
        codepoints = frozenset(font.getBestCmap() or ())
        if not codepoints:
            fail(f"{context} has no Unicode cmap")
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
        return ({"postscript_name": font_name(6), "full_name": font_name(4)}, codepoints)
    except ContractError:
        raise
    except (AssertionError, AttributeError, IndexError, KeyError, OSError, OverflowError, TTLibError, TypeError, ValueError, struct.error) as error:
        fail(f"{context} is not a valid font: {error}")
    finally:
        if font is not None:
            font.close()

def validate_document_bindings(recipe: Recipe, fragments: dict[str, Markup]) -> None:
    order = recipe.value["fragment_order"]
    figure_ids = [declaration["id"] for declaration in recipe.value["figures"]]
    markers = [marker for identifier in order for marker in fragments[identifier].markers]
    if markers != figure_ids:
        fail("figure markers must match assembly figure IDs and order exactly")
    known_ids = {recipe.value["publication_id"]}
    references: list[tuple[str, str]] = []
    for identifier in order:
        markup = fragments[identifier]
        duplicates = known_ids.intersection(markup.ids)
        if duplicates:
            fail("duplicate document IDs: " + ", ".join(sorted(duplicates)))
        known_ids.update(markup.ids)
        references.extend(markup.references)
    for figure_id in figure_ids:
        if figure_id in known_ids:
            fail(f"figure ID conflicts with another document ID: {figure_id}")
        known_ids.add(figure_id)
        caption = recipe.captions[figure_id]
        duplicates = known_ids.intersection(caption.ids)
        if duplicates:
            fail("duplicate caption document IDs: " + ", ".join(sorted(duplicates)))
        known_ids.update(caption.ids)
        references.extend(caption.references)
    missing = sorted({target for _context, target in references if target not in known_ids})
    if missing:
        fail("same-document references have no target: " + ", ".join(missing))

def load_build_material(assembly: Assembly) -> BuildMaterial:
    fragments, fragment_contents, fragment_paths = load_fragments(assembly)
    block_paths = validate_consumed_blocks(assembly)
    figures, used_pages, figure_paths = load_figures(assembly)
    validate_document_bindings(assembly.recipe, fragments)
    fonts: list[tuple[JsonObject, bytes, JsonObject]] = []
    font_paths: list[Path] = []
    for index, declaration in enumerate(assembly.recipe.value["fonts"], start=1):
        path = resolve_relative(assembly.spec.path.parent, declaration["path"], f"font {index}")
        content = read_regular(path, f"font {index}")
        metadata, _codepoints = inspect_font_bytes(content, f"font {index}")
        fonts.append((declaration, content, metadata))
        font_paths.append(path)
    families = {font["family"].casefold() for font in assembly.recipe.value["fonts"]}
    stylesheets: list[tuple[bytes, str]] = []
    stylesheet_paths: list[Path] = []
    for index, identity in enumerate(assembly.recipe.value["stylesheets"], start=1):
        path = resolve_relative(assembly.spec.path.parent, identity, f"stylesheet {index}")
        content = read_regular(path, f"stylesheet {index}")
        try:
            text = content.decode()
        except UnicodeDecodeError as error:
            fail(f"stylesheet {index} is not UTF-8: {error}")
        stylesheets.append((content, validate_and_scope_stylesheet(text, assembly.profile, families, f"stylesheet {index}")))
        stylesheet_paths.append(path)
    consumed_paths = [
        assembly.spec.path, assembly.source.path, assembly.bundle.path, *fragment_paths, *block_paths,
        *figure_paths, *font_paths, *stylesheet_paths,
    ]
    return BuildMaterial(assembly, fragments, fragment_contents, figures, used_pages, fonts,
                         stylesheets, consumed_paths)

def crop_markup(part: JsonObject, page: JsonObject, source_svg: AssetRecord, alternative: str) -> str:
    x0, y0, x1, y1 = validated_bbox(part["bbox"], page, f"crop {part['id']}")
    width = x1 - x0
    height = y1 - y0
    page_width = finite_number(page["width"], "source page width")
    page_height = finite_number(page["height"], "source page height")
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
    if len(declarations) != len(manifest_figures):
        fail("manifest figure count does not match assembly specification")
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

def ensure_output_is_separate(output: Path, inputs: list[Path]) -> None:
    destination = output.expanduser().resolve()
    for input_path in inputs:
        try:
            input_path.resolve(strict=True).relative_to(destination)
        except ValueError:
            continue
        fail(f"assembly output would contain and replace input {input_path}")

def make_stage(output: Path) -> Path:
    output.parent.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix=f".{output.name}.staging-", dir=output.parent))

def publish_directory(stage: Path, output: Path, *, force: bool) -> None:
    try:
        output_info = output.lstat()
    except FileNotFoundError:
        output_info = None
    if output_info is not None:
        reparse = getattr(output_info, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        if stat.S_ISLNK(output_info.st_mode) or reparse or not stat.S_ISDIR(output_info.st_mode):
            fail(f"assembly output exists and is not a regular directory: {output}")
    if output_info is not None and (not force):
        fail(f"assembly output already exists; pass --force: {output}")
    if output_info is not None and next(output.iterdir(), None) is not None:
        marker = output / MANIFEST_NAME
        try:
            marker_info = marker.lstat()
        except OSError as error:
            fail(f"--force requires an assembly ownership marker at {marker}: {error}")
        reparse = getattr(marker_info, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        if stat.S_ISLNK(marker_info.st_mode) or reparse or not stat.S_ISREG(marker_info.st_mode):
            fail(f"--force requires a regular non-symlink assembly ownership marker: {marker}")
    had_output = output_info is not None
    backup: Path | None = None
    try:
        if had_output:
            backup = output.with_name(f".{output.name}.backup-{uuid.uuid4().hex}")
            output.replace(backup)
        stage.replace(output)
    except (OSError, KeyboardInterrupt) as error:
        try:
            if backup is not None and backup.exists():
                if output.exists():
                    shutil.rmtree(output)
                backup.replace(output)
            elif not had_output and output.exists():
                shutil.rmtree(output)
        except OSError as rollback_error:
            fail(f"cannot restore assembly output after {error}; backup remains at {backup}: {rollback_error}")
        if isinstance(error, KeyboardInterrupt):
            raise
        fail(f"cannot publish assembly directory: {error}")
    finally:
        if stage.exists():
            shutil.rmtree(stage)
    if backup is not None:
        try:
            shutil.rmtree(backup)
        except OSError as error:
            eprint(f"warning: assembly committed but backup remains at {backup}: {error}")

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
            canonical_bbox = validated_bbox(
                source_part["bbox"],
                page,
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
        "tracked_files": [],
        "status": "assembled",
    }
    manifest["tracked_files"] = [{"path": identity, "sha256": state.sha256, "bytes": state.size}
                                 for identity, state in scan_tree(stage).items()]
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
    requested_output = args.output.expanduser()
    if (
        sys.platform == "win32"
        and requested_output.name.rstrip(" .") != requested_output.name
    ):
        fail("Windows output names must not end in a dot or space")
    spec = read_snapshot(args.spec, "assembly specification")
    material = load_build_material(load_assembly(spec, require_original_binding=True))
    output = requested_output
    output = output.parent.resolve() / output.name
    if sys.platform == "win32":
        try:
            output_info = output.lstat()
        except FileNotFoundError:
            output_info = None
        if output_info is not None:
            reparse = getattr(
                output_info,
                "st_file_attributes",
                0,
            ) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
            if (
                not stat.S_ISLNK(output_info.st_mode)
                and not reparse
                and stat.S_ISDIR(output_info.st_mode)
            ):
                try:
                    output = output.resolve(strict=True)
                except OSError as error:
                    fail(f"cannot resolve existing assembly output: {error}")
    ensure_output_is_separate(output, material.consumed_paths)
    stage = make_stage(output)
    try:
        summary = build_candidate(stage, material)
        publish_directory(stage, output, force=args.force)
    finally:
        if stage.exists():
            shutil.rmtree(stage)
    print_json({"committed": True, "manifest": str(output / MANIFEST_NAME), **summary})
    return 0

def semantic_asset_records(manifest: JsonObject) -> list[AssetRecord]:
    records = list(manifest["inputs"].values())
    records.extend(fragment["asset"] for fragment in manifest["fragments"])
    records.extend(part["source_svg"] for figure in manifest["figures"] for part in figure["parts"])
    records.extend(font["asset"] for font in manifest["fonts"])
    records.extend(manifest["stylesheets"])
    records.extend((manifest["outputs"]["html"], manifest["outputs"]["css"]))
    if isinstance(manifest["outputs"]["draft_pdf"], dict):
        records.append(manifest["outputs"]["draft_pdf"])
    return records

def validate_manifest_inventory(root: Path, manifest: JsonObject) -> dict[str, FileState]:
    inventory = scan_tree(root)
    if MANIFEST_NAME not in inventory:
        fail("publication tree is missing assembly-manifest.json")
    tracked = manifest["tracked_files"]
    paths = [record["path"] for record in tracked]
    if paths != sorted(paths) or len(paths) != len(set(paths)):
        fail("tracked_files must be path-sorted with unique paths")
    expected = set(inventory) - {MANIFEST_NAME}
    if set(paths) != expected:
        fail(f"tracked_files does not exactly match retained regular files; missing={sorted(expected - set(paths))}, extra={sorted(set(paths) - expected)}")
    tracked_by_path = {}
    for index, record in enumerate(tracked, start=1):
        verify_asset_record(record, inventory, f"tracked file {index}")
        tracked_by_path[record["path"]] = record
    semantic = semantic_asset_records(manifest)
    semantic_paths = {record["path"] for record in semantic}
    if semantic_paths != expected:
        fail("manifest semantic assets do not exactly cover the retained tree; "
             f"unreferenced={sorted(expected - semantic_paths)}, "
             f"absent={sorted(semantic_paths - expected)}")
    for index, record in enumerate(semantic, start=1):
        verify_asset_record(record, inventory, f"manifest asset {index}")
        if tracked_by_path.get(record["path"]) != record:
            fail(f"manifest asset {record['path']} does not equal its tracked_files record")
    return inventory

def input_snapshot(manifest: JsonObject, inventory: dict[str, FileState], role: str, expected_path: str) -> Snapshot:
    record = manifest["inputs"][role]
    if record["path"] != expected_path:
        fail(f"manifest input {role} must use {expected_path}")
    state = verify_asset_record(record, inventory, f"input {role}")
    content = state.path.read_bytes()
    return Snapshot(state.path, content, parse_json_bytes(content, f"input {role}"))

def validate_fragment_projection(manifest: JsonObject, assembly: Assembly, inventory: dict[str, FileState]) -> dict[str, Markup]:
    records = manifest["fragments"]
    order = assembly.recipe.value["fragment_order"]
    if [record["id"] for record in records] != order:
        fail("manifest fragment IDs do not match assembly fragment order")
    markups = {}
    for index, (identifier, record) in enumerate(zip(order, records, strict=True), start=1):
        asset = record["asset"]
        if asset["path"] != f"fragments/{identifier}.html":
            fail(f"manifest fragment {identifier} has a noncanonical path")
        state = verify_asset_record(asset, inventory, f"manifest fragment {identifier}")
        approved = assembly.bundle_fragments[identifier]
        if state.size != approved["bytes"] or state.sha256 != approved["sha256"]:
            fail(f"retained fragment {identifier} does not match the translation bundle")
        try:
            content = state.path.read_text(encoding="utf-8")
        except UnicodeDecodeError as error:
            fail(f"retained fragment {identifier} is not UTF-8: {error}")
        markup = parse_markup(content, assembly.profile, f"retained fragment {identifier}", allow_figure_markers=True)
        expected = {
            "id": identifier,
            "asset": asset,
            "dom_selector": f'[data-fragment-id="{identifier}"]',
            "visible_text_sha256": approved["visible_text_sha256"],
            "source_block_ids": approved["source_block_ids"],
        }
        if record != expected:
            fail(f"manifest fragment {index} does not project the approved fragment")
        if markup.visible_sha256 != record["visible_text_sha256"]:
            fail(f"retained fragment {identifier} visible-text hash changed")
        markups[identifier] = markup
    return markups

def validate_figure_projection(manifest: JsonObject, assembly: Assembly, inventory: dict[str, FileState]) -> list[JsonObject]:
    declarations = assembly.recipe.value["figures"]
    figures = manifest["figures"]
    if len(declarations) != len(figures):
        fail("manifest figures do not match assembly figures")
    seen_parts: set[str] = set()
    validated_svgs: set[str] = set()
    for declaration, figure in zip(declarations, figures, strict=True):
        identifier = declaration["id"]
        caption = declaration["caption_html"]
        expected = {
            "id": identifier,
            "dom_id": identifier,
            "caption_html": caption,
            "caption_sha256": sha256_bytes(caption.encode()),
            "alt": declaration["alt"],
        }
        if {key: figure[key] for key in expected} != expected:
            fail(f"manifest figure {identifier} projection is inconsistent")
        parts = figure["parts"]
        if [part["order"] for part in parts] != list(range(1, len(parts) + 1)):
            fail(f"manifest figure {identifier} part order is invalid")
        for part in parts:
            part_id = part["id"]
            if part_id in seen_parts:
                fail(f"duplicate manifest crop id: {part_id}")
            seen_parts.add(part_id)
            if part["dom_selector"] != f'[data-crop-id="{part_id}"]':
                fail(f"crop {part_id} has a noncanonical selector")
            page = assembly.pages_by_number.get(part["pdf_page"])
            if page is None:
                fail(f"crop {part_id} references an absent source page")
            if page["status"] != "pass":
                fail(f"crop {part_id} references a non-passing source page")
            canonical_bbox = validated_bbox(
                part["bbox"],
                page,
                f"crop {part_id}",
            )
            if part["bbox"] != canonical_bbox:
                fail(
                    f"crop {part_id} bbox is not canonical to three decimals"
                )
            source_record = page["assets"]["svg"]
            retained = part["source_svg"]
            page_id = page["id"]
            if retained["path"] != f"assets/pages/{page_id}.svg":
                fail(f"crop {part_id} source SVG path is not canonical")
            if retained["sha256"] != source_record["sha256"] or retained["bytes"] != source_record["bytes"]:
                fail(f"crop {part_id} source SVG does not match the source-package asset")
            if retained["path"] not in validated_svgs:
                state = verify_asset_record(retained, inventory, f"crop {part_id} source SVG")
                validate_page_svg(state.path.read_bytes(), page, f"retained source page SVG {page_id}")
                validated_svgs.add(retained["path"])
    return figures

def validate_font_projection(manifest: JsonObject, assembly: Assembly, inventory: dict[str, FileState]) -> tuple[list[JsonObject], dict[tuple[str, str, int], frozenset[int]]]:
    declarations = assembly.recipe.value["fonts"]
    records = manifest["fonts"]
    coverage: dict[tuple[str, str, int], frozenset[int]] = {}
    if len(declarations) != len(records):
        fail("manifest fonts do not match assembly fonts")
    for index, (declaration, record) in enumerate(zip(declarations, records, strict=True), start=1):
        extension = Path(declaration["path"]).suffix.casefold()
        asset = record["asset"]
        if asset["path"] != f"assets/fonts/font-{index:03d}{extension}":
            fail(f"manifest font {index} has a noncanonical path")
        state = verify_asset_record(asset, inventory, f"manifest font {index}")
        metadata, codepoints = inspect_font_bytes(state.path.read_bytes(), f"manifest font {index}")
        expected = {"family": declaration["family"], "style": declaration["style"], "weight": declaration["weight"], **metadata, "asset": asset}
        if record != expected:
            fail(f"manifest font {index} metadata is inconsistent")
        coverage[(declaration["family"].casefold(), declaration["style"], declaration["weight"])] = codepoints
    if manifest["font_roles"] != assembly.recipe.value["font_roles"]:
        fail("manifest font_roles do not match the assembly specification")
    return (records, coverage)

def validate_stylesheet_projection(manifest: JsonObject, assembly: Assembly, inventory: dict[str, FileState]) -> list[str]:
    records = manifest["stylesheets"]
    if len(assembly.recipe.value["stylesheets"]) != len(records):
        fail("manifest stylesheets do not match assembly stylesheets")
    families = {font["family"].casefold() for font in assembly.recipe.value["fonts"]}
    scoped = []
    for index, record in enumerate(records, start=1):
        if record["path"] != f"assets/stylesheets/stylesheet-{index:03d}.css":
            fail(f"manifest stylesheet {index} has a noncanonical path")
        state = verify_asset_record(record, inventory, f"manifest stylesheet {index}")
        try:
            content = state.path.read_text(encoding="utf-8")
        except UnicodeDecodeError as error:
            fail(f"manifest stylesheet {index} is not UTF-8: {error}")
        scoped.append(validate_and_scope_stylesheet(content, assembly.profile, families, f"manifest stylesheet {index}"))
    return scoped

def validate_pdf(path: Path, context: str) -> None:
    content = read_regular(path, context)
    if not content:
        fail(f"{context} is empty")
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
            if not document.is_pdf or document.page_count < 1:
                fail(f"{context} is not a nonempty PDF")
            for page_index in range(document.page_count):
                document.load_page(page_index)
        except (fitz.mupdf.FzErrorBase, OSError, RuntimeError, ValueError) as error:
            fail(f"{context} cannot load all PDF pages: {error}")
    finally:
        document.close()

def validate_publication(manifest_path: Path) -> ValidationResult:
    entry = manifest_path.expanduser().absolute()
    if entry.name != MANIFEST_NAME:
        fail(f"manifest must be named {MANIFEST_NAME}")
    if not stat.S_ISDIR(checked_node(entry.parent, "publication root").st_mode):
        fail(f"publication root is not a directory: {entry.parent}")
    manifest_bytes = read_regular(entry, "assembly manifest")
    resolved = entry.resolve(strict=True)
    manifest = parse_json_bytes(manifest_bytes, "assembly manifest")
    validate_schema(manifest, "assembly-manifest.schema.json", "assembly manifest")
    if manifest["generator"] != generator_record():
        fail("assembly manifest generator does not match this runtime")
    if manifest["policies"] != publication_policies():
        fail("assembly manifest policies do not match bundled profiles")
    inventory = validate_manifest_inventory(resolved.parent, manifest)
    source = input_snapshot(manifest, inventory, "source_package", "inputs/source-package.json")
    bundle = input_snapshot(manifest, inventory, "translation_bundle", "inputs/translation-bundle.json")
    spec = input_snapshot(manifest, inventory, "assembly_spec", "inputs/assembly-spec.json")
    assembly = load_assembly(spec, source=source, bundle=bundle, require_original_binding=False)
    recipe = assembly.recipe
    expected_document = {"title": recipe.value["title"], "title_language": recipe.value["title_language"], "language": recipe.value["language"]}
    expected_geometry = {"page_size": recipe.value["page"]["size"], "margin_in": recipe.value["page"]["margin_in"]}
    if manifest["publication_id"] != recipe.value["publication_id"]:
        fail("manifest publication_id does not match the assembly specification")
    if manifest["document"] != expected_document:
        fail("manifest document does not match the assembly specification")
    if manifest["profiles"] != recipe.value["profiles"]:
        fail("manifest profiles do not match the assembly specification")
    if manifest["print_geometry"] != expected_geometry:
        fail("manifest print geometry does not match the assembly specification")
    fragments = validate_fragment_projection(manifest, assembly, inventory)
    validate_document_bindings(recipe, fragments)
    figures = validate_figure_projection(manifest, assembly, inventory)
    fonts, font_coverage = validate_font_projection(manifest, assembly, inventory)
    stylesheets = validate_stylesheet_projection(manifest, assembly, inventory)
    outputs = manifest["outputs"]
    if outputs["html"]["path"] != "index.html":
        fail("manifest HTML output must be index.html")
    if outputs["css"]["path"] != "assets/print.css":
        fail("manifest CSS output must be assets/print.css")
    html = verify_asset_record(outputs["html"], inventory, "HTML output")
    css = verify_asset_record(outputs["css"], inventory, "CSS output")
    html_content = html.path.read_bytes()
    css_content = css.path.read_bytes()
    if html_content != compose_html(recipe, fragments, figures, assembly.pages_by_number):
        fail("generated HTML does not match the closed document profile")
    if css_content != compose_css(recipe.value, fonts, stylesheets):
        fail("generated CSS does not match the closed CSS profile")
    validate_selected_font_faces(html_content, css_content, manifest["font_roles"], font_coverage)
    draft_pdf = outputs["draft_pdf"]
    if isinstance(draft_pdf, dict):
        pdf = verify_asset_record(draft_pdf, inventory, "draft PDF")
        if pdf.path.suffix.casefold() != ".pdf":
            fail("draft PDF must use a .pdf extension")
        validate_pdf(pdf.path, "draft PDF")
    summary = {
        "manifest": str(resolved),
        "publication_id": manifest["publication_id"],
        "fragments": len(manifest["fragments"]),
        "figures": len(figures),
        "crops": sum(len(figure["parts"]) for figure in figures),
        "pdf": draft_pdf["path"] if isinstance(draft_pdf, dict) else None,
        "status": "valid",
    }
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
        if not stat.S_ISREG(path.stat().st_mode):
            fail(f"browser is not a regular file: {path}")
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
    return Path(url2pathname(unquote(parsed.path))).resolve()

def render_pdf(html_path: Path, output_path: Path, browser_path: Path, allowed_files: set[Path]) -> None:
    try:
        from playwright.sync_api import Error as PlaywrightError
        from playwright.sync_api import TimeoutError as PlaywrightTimeoutError
        from playwright.sync_api import sync_playwright
    except ImportError:
        fail("Playwright is unavailable; run with uv run --script")
    blocked: list[str] = []

    def route_request(route: Any, request: Any) -> None:
        url = request.url
        local = file_url_path(url)
        if url == "about:blank" or (local is not None and local in allowed_files):
            route.continue_()
        else:
            blocked.append(url)
            route.abort()
    try:
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(
                executable_path=str(browser_path),
                headless=True,
                args=[
                    "--disable-background-networking",
                    "--disable-component-update",
                    "--disable-default-apps",
                    "--disable-extensions",
                    "--disable-sync",
                    "--no-first-run",
                ])
            try:
                context = browser.new_context(java_script_enabled=False, service_workers="block")
                context.route("**/*", route_request)
                page = context.new_page()
                page.goto(html_path.as_uri(), wait_until="networkidle", timeout=120000)
                if blocked:
                    fail("Chromium requested files outside the manifest: " + ", ".join(sorted(set(blocked))))
                page.emulate_media(media="print")
                page.pdf(path=str(output_path), print_background=True, prefer_css_page_size=True, display_header_footer=False)
                context.close()
            finally:
                browser.close()
    except (PlaywrightError, PlaywrightTimeoutError) as error:
        fail(f"Chromium PDF render failed: {error}")

def publish_render(result: ValidationResult, pdf_path: Path, temporary_pdf: Path, browser_path: Path, *, force: bool) -> None:
    root = result.manifest_path.parent
    existing_record = result.manifest["outputs"]["draft_pdf"]
    existing_path = root / Path(*PurePosixPath(existing_record["path"]).parts) if isinstance(existing_record, dict) else None
    if existing_path is not None and (not force):
        fail("assembly already has a draft PDF; pass --force to replace it")
    if pdf_path.exists() and (existing_path is None or pdf_path != existing_path or (not force)):
        fail(f"PDF destination already exists outside the manifest: {pdf_path}")
    pdf_content = read_regular(temporary_pdf, "rendered PDF")
    pdf_record = asset_from_bytes(pdf_path.relative_to(root).as_posix(), pdf_content)
    updated = json.loads(json.dumps(result.manifest))
    updated["outputs"]["draft_pdf"] = pdf_record
    updated["draft_render"] = {
        "browser": browser_path.name,
        "browser_sha256": sha256_bytes(read_regular(browser_path, "Chromium executable")),
        "command_mode": "headless-print-to-pdf-offline",
    }
    retained = [record for record in updated["tracked_files"]
                if not isinstance(existing_record, dict) or record["path"] != existing_record["path"]]
    updated["tracked_files"] = sorted([*retained, pdf_record], key=lambda record: record["path"])
    validate_schema(updated, "assembly-manifest.schema.json", "rendered assembly manifest")
    backup: Path | None = None
    try:
        if existing_path is not None:
            backup = root.parent / f".{root.name}.{existing_path.name}.backup-{uuid.uuid4().hex}"
            existing_path.replace(backup)
        pdf_path.parent.mkdir(parents=True, exist_ok=True)
        temporary_pdf.replace(pdf_path)
        write_atomic(result.manifest_path, json_bytes(updated))
        validate_publication(result.manifest_path)
    except (ContractError, OSError, KeyboardInterrupt) as error:
        try:
            if backup is not None and backup.exists() and existing_path is not None:
                pdf_path.unlink(missing_ok=True)
                existing_path.parent.mkdir(parents=True, exist_ok=True)
                backup.replace(existing_path)
            elif existing_path is None or pdf_path != existing_path:
                pdf_path.unlink(missing_ok=True)
            write_atomic(result.manifest_path, result.manifest_bytes)
        except OSError as rollback_error:
            fail(f"cannot restore render after {error}; backup remains at {backup}: {rollback_error}")
        if isinstance(error, KeyboardInterrupt):
            raise
        raise
    finally:
        temporary_pdf.unlink(missing_ok=True)
    if backup is not None:
        try:
            backup.unlink(missing_ok=True)
        except OSError as error:
            eprint(f"warning: render committed but backup remains at {backup}: {error}")

def render_publication(args: argparse.Namespace) -> int:
    html_entry = args.html.expanduser().absolute()
    if not stat.S_ISDIR(checked_node(html_entry.parent, "publication root").st_mode):
        fail(f"publication root is not a directory: {html_entry.parent}")
    read_regular(html_entry, "HTML input")
    result = validate_publication(html_entry.parent / MANIFEST_NAME)
    root = result.manifest_path.parent
    html_path = html_entry.resolve(strict=True)
    expected_html = result.manifest["outputs"]["html"]["path"]
    if html_path != root / Path(*PurePosixPath(expected_html).parts):
        fail("--html must name the manifest-bound canonical HTML")
    pdf_path = args.pdf.expanduser().resolve()
    try:
        pdf_path.relative_to(root)
    except ValueError:
        fail(f"draft PDF must stay beneath the publication root: {root}")
    if pdf_path.suffix.casefold() != ".pdf":
        fail("draft PDF path must use a .pdf extension")
    if pdf_path == result.manifest_path:
        fail("draft PDF cannot replace the assembly manifest")
    current = result.manifest["outputs"]["draft_pdf"]
    if isinstance(current, dict) and not args.force:
        fail("assembly already has a draft PDF; pass --force to replace it")
    current_path = (root / Path(*PurePosixPath(current["path"]).parts)).resolve() if isinstance(current, dict) else None
    existing_paths = {state.path.resolve() for state in result.inventory.values()}
    if pdf_path in existing_paths and pdf_path != current_path:
        fail("draft PDF destination would replace another tracked file")
    browser_path = select_browser(args.browser)
    allowed_files = {result.inventory[record["path"]].path.resolve() for record in result.manifest["tracked_files"]}
    with tempfile.NamedTemporaryFile(prefix=f".{pdf_path.name}.render-", suffix=".pdf", dir=root.parent, delete=False) as stream:
        temporary_pdf = Path(stream.name)
    temporary_pdf.unlink()
    try:
        render_pdf(html_path, temporary_pdf, browser_path, allowed_files)
        validate_pdf(temporary_pdf, "rendered PDF")
        pdf_sha256 = sha256_bytes(temporary_pdf.read_bytes())
        publish_render(result, pdf_path, temporary_pdf, browser_path, force=args.force)
    finally:
        temporary_pdf.unlink(missing_ok=True)
    print_json({"browser": str(browser_path), "committed": True, "pdf": str(pdf_path), "sha256": pdf_sha256, "status": "rendered"})
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
    build.add_argument(
        "--force",
        action="store_true",
        help="replace a non-link output directory when empty or marked by a regular non-symlink assembly-manifest.json",
    )
    build.set_defaults(handler=build_publication)
    render = subparsers.add_parser("render", help="render the canonical HTML")
    render.add_argument("--html", type=Path, required=True)
    render.add_argument("--pdf", type=Path, required=True)
    render.add_argument("--browser", type=Path)
    render.add_argument("--force", action="store_true")
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
