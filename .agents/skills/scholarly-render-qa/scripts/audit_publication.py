# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "html5lib==1.1",
#   "jsonschema==4.25.1",
#   "playwright==1.56.0",
#   "pymupdf==1.26.6",
#   "tinycss2==1.4.0",
# ]
# ///

from __future__ import annotations

import argparse
import functools
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
import uuid
from collections import Counter
from dataclasses import dataclass, field
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, cast
from urllib.parse import unquote, urlsplit
from urllib.request import url2pathname
from xml.etree.ElementTree import ParseError

import html5lib
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
MAX_DIAGNOSTIC_STRING = 256
MAX_DIAGNOSTIC_ITEMS = 25
PAGE_POINTS = {"letter": (612.0, 792.0), "a4": (595.276, 841.89)}
PAGE_CSS_NAMES = {"letter": "Letter", "a4": "A4"}
CSS_LENGTH_UNITS = frozenset({"ch", "cm", "em", "ex", "in", "mm", "pc", "pt", "px", "q", "rem"})
GENERATED_CLASSES = frozenset({"publication-figure", "figure-parts", "figure-part"})
FIGURE_MARKER = re.compile(r"\s*figure\s*:\s*(?P<id>[a-z0-9][a-z0-9._-]*)\s*")
LANGUAGE_PATTERN = re.compile(r"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$")
SVG_LENGTH = re.compile(r"\s*([+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)(?:pt|px)?\s*")
CORE_CHECK_IDS = (
    "manifest.integrity", "html.offline-profile", "render.geometry-overflow",
    "pdf.fonts", "pdf.actions-type3-text", "figures.crop-bindings",
    "render.repeatability", "rasters.complete", "publication.tree-unchanged",
)
HUMAN_REVIEW_SCOPE = (
    "Inspect every full-page raster at readable zoom.",
    "Check crop loss, overflow, page breaks, and continuation order.",
    "Check mixed-script typography, figures, captions, and notation fidelity.",
)
VOID_ELEMENTS = {
    "area", "base", "br", "col", "embed", "hr", "img", "input",
    "link", "meta", "param", "source", "track", "wbr",
}
SELF_CLOSING_ELEMENTS = VOID_ELEMENTS | {"image"}
RESOURCE_ATTRIBUTES = {"background", "data", "href", "poster", "src", "srcset", "xlink:href"}
ACTIVE_ELEMENTS = {
    "applet", "audio", "base", "button", "canvas", "datalist", "details", "dialog", "embed", "form", "frame",
    "frameset", "iframe", "img", "input", "listing", "meter", "noembed", "noframes", "noscript", "object",
    "plaintext", "progress", "rp", "script", "select", "source", "style", "textarea", "video", "xmp",
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
FIXED_CSS_PROPERTIES = frozenset({
    *CSS_BORDER_STYLE_PROPERTIES, *CSS_BORDER_WIDTH_PROPERTIES, *CSS_COLOR_PROPERTIES,
    *CSS_NONNEGATIVE_LENGTH_PERCENTAGE_PROPERTIES, *CSS_SIGNED_SPACING_PROPERTIES,
    *CSS_WIDOW_ORPHAN_PROPERTIES, *CSS_ENUM_VALUES, *CSS_KEYWORD_SET_VALUES,
    "border-spacing", "font-family", "font-size", "font-weight", "line-height", "tab-size",
    "text-decoration-thickness", "text-indent", "vertical-align",
})
REPARSE_POINT_ATTRIBUTE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
ASSET_ROOT = Path(__file__).resolve().parents[1] / "assets"
ASSEMBLY_SCHEMA = ASSET_ROOT / "assembly-manifest.schema.json"
EVIDENCE_SCHEMA = ASSET_ROOT / "qa-evidence.schema.json"
RELEASE_SCHEMA = ASSET_ROOT / "release-manifest.schema.json"
PROFILE_PATH = ASSET_ROOT / "publication-profile.json"
CSS_UNSAFE_STRING_CATEGORIES = frozenset({"Cc", "Cf", "Cs"})

# The generated-output profile is deliberately small and closed. Values are
# normalized with tinycss2 before comparison.
BASE_RULES: dict[str, tuple[tuple[str, str], ...]] = {
    "html": (
        ("color", "var(--ink)"), ("font-family", "var(--body-latin)"), ("font-size", "10.5pt"),
        ("font-style", "normal"), ("font-synthesis", "none"), ("font-weight", "400"),
        ("hyphenate-character", '"-"'), ("line-height", "1.52"),
        ("text-autospace", "normal"), ("text-spacing-trim", "trim-start"),
        ("hanging-punctuation", "first allow-end last"), ("print-color-adjust", "exact"),
        ("-webkit-print-color-adjust", "exact"),
    ),
    "body": (("margin", "0"),),
    "body :where(*)": (("font-family", "inherit"), ("font-style", "inherit"), ("font-weight", "inherit")),
    "[lang]": (("font-family", "var(--body-latin)"),),
    (
        '[lang|="zh" i], [lang|="ja" i], [lang|="ko" i], '
        '[lang$="-Hans" i], [lang*="-Hans-" i], [lang$="-Hant" i], '
        '[lang*="-Hant-" i], [lang$="-Hani" i], [lang*="-Hani-" i], '
        '[lang$="-Jpan" i], [lang*="-Jpan-" i], [lang$="-Kore" i], '
        '[lang*="-Kore-" i]'
    ): (("font-family", "var(--body-cjk)"),),
    "h1, h2, h3, h4, h5, h6": (
        ("break-after", "avoid-page"), ("font-weight", "700"), ("line-height", "1.24"),
        ("margin-block", "1.25em 0.48em"),
    ),
    "b, strong, th": (("font-weight", "700"),),
    "cite, em, i": (("font-style", "italic"),),
    "q": (("quotes", '"\u201c" "\u201d" "\u2018" "\u2019"'),),
    "ul": (("list-style-type", "disc"),),
    "p": (("margin-block", "0 0.58em"), ("orphans", "2"), ("widows", "2")),
    "figure": (("break-inside", "avoid-page"), ("margin", "1em auto")),
    ".figure-parts": (("display", "grid"), ("gap", "0.45em")),
    ".figure-part": (("display", "block"), ("height", "auto"), ("margin-inline", "auto"), ("max-width", "100%")),
    "figcaption": (
        ("color", "var(--muted-ink)"), ("font-size", "0.9em"), ("margin-block-start", "0.42em"),
        ("text-align", "center"),
    ),
    ".keep-with-next": (("break-after", "avoid-page"),),
    ".keep-together, .atomic, .bilingual-term, .music-token, .figure-label": (
        ("break-inside", "avoid"), ("display", "inline-block"), ("white-space", "nowrap"),
    ),
    "a": (("color", "inherit"), ("text-decoration", "none")),
}
SCREEN_RULES = {
    "html": (("background", "#ddd"),),
    "body": (
        ("background", "white"), ("box-shadow", "0 0.08in 0.28in rgb(0 0 0 / 22%)"),
        ("margin", "0.35in auto"), ("max-width", "7.14in"), ("min-height", "9.7in"),
        ("padding", "0.62in 0.68in 0.68in"),
    ),
}
BASE_ROOT = (
    ("--body-cjk", '"Publication CJK", serif'),
    ("--body-latin", '"Publication Latin", serif'),
    ("--ink", "#111"),
    ("--muted-ink", "#555"),
)
BASE_PAGE = (("size", "Letter"), ("margin", "0.62in 0.68in 0.68in"))
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
class TextNode:
    text: str

@dataclass
class CommentNode:
    text: str

@dataclass
class Element:
    tag: str
    attrs: dict[str, str | None]
    attr_names: tuple[str, ...]
    children: list[HtmlNode] = field(default_factory=list)

type HtmlNode = Element | TextNode | CommentNode

@dataclass
class ParsedHtml:
    root: Element
    errors: list[str]
    doctypes: int

@dataclass
class Context:
    root: Path
    manifest_path: Path
    manifest: dict[str, Any]
    profile: dict[str, Any]
    profile_sha256: str
    manifest_asset: Asset
    tracked: dict[str, Asset]
    html: Asset
    css: Asset
    pdf: Asset
    before: TreeSnapshot
    errors: list[str]

@dataclass
class StaticAudit:
    errors: list[Any]
    binding_errors: list[Any]
    document: dict[str, Any]
    figures: list[dict[str, Any]]
    crops: list[dict[str, Any]]

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
class TreeParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.root = Element("#document", {}, ())
        self.stack = [self.root]
        self.errors: list[str] = []
        self.doctypes = 0
        self.seen_element = False
    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self._start(tag, attrs, closing=False)
    def handle_startendtag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        self._start(tag, attrs, closing=True)
    def _start(self, tag: str, attrs: list[tuple[str, str | None]], closing: bool) -> None:
        name = tag.casefold()
        names = tuple(key.casefold() for key, _ in attrs)
        duplicates = sorted(key for key, count in Counter(names).items() if count > 1)
        if duplicates:
            self.errors.append(f"<{name}> has duplicate attributes: {duplicates}")
        node = Element(name, {key.casefold(): value for key, value in attrs}, names)
        self.stack[-1].children.append(node)
        self.seen_element = True
        if closing and name not in SELF_CLOSING_ELEMENTS:
            self.errors.append(f"non-void <{name}/> is not allowed")
        if not closing and name not in VOID_ELEMENTS:
            self.stack.append(node)
    def handle_endtag(self, tag: str) -> None:
        name = tag.casefold()
        if name in VOID_ELEMENTS:
            self.errors.append(f"void element has end tag </{name}>")
        elif len(self.stack) == 1:
            self.errors.append(f"unmatched end tag </{name}>")
        elif self.stack[-1].tag != name:
            self.errors.append(f"misnested end tag </{name}>; expected </{self.stack[-1].tag}>")
        else:
            self.stack.pop()
    def handle_data(self, data: str) -> None:
        self.stack[-1].children.append(TextNode(data))
    def handle_comment(self, data: str) -> None:
        self.stack[-1].children.append(CommentNode(data))
    def handle_decl(self, decl: str) -> None:
        if decl.strip().casefold() == "doctype html" and not self.seen_element:
            self.doctypes += 1
        else:
            self.errors.append(f"unexpected declaration <!{decl}>")
    def unknown_decl(self, data: str) -> None:
        self.errors.append(f"unexpected declaration <![{data}]>")
    def handle_pi(self, data: str) -> None:
        self.errors.append(f"unexpected processing instruction <?{data}>")
    def result(self) -> ParsedHtml:
        self.close()
        if len(self.stack) > 1:
            self.errors.append("unclosed elements: " + ", ".join(node.tag for node in self.stack[1:]))
        return ParsedHtml(self.root, self.errors, self.doctypes)
def eprint(message: str) -> None:
    print(message, file=sys.stderr)
def canonical_json(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n").encode()
def atomic_write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(dir=path.parent, prefix=f".{path.name}.")
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)
def write_json(path: Path, value: Any) -> None:
    content = json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True, allow_nan=False)
    atomic_write(path, (content + "\n").encode())
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
@functools.cache
def load_profile() -> tuple[dict[str, Any], bytes]:
    profile_bytes = PROFILE_PATH.read_bytes()
    profile = read_json(PROFILE_PATH)
    if not isinstance(profile, dict) or set(profile) != {
        "schema_version", "profile_id", "closed", "fragment_html", "untrusted_stylesheet",
        "global_prohibitions",
    }:
        raise AuditError("publication profile has an unexpected top-level shape")
    if (
        profile.get("schema_version") != "1.0"
        or profile.get("profile_id") != "scholarly-fragment-and-stylesheet-v1"
        or profile.get("closed") is not True
    ):
        raise AuditError("publication profile identity is not supported")
    html = profile.get("fragment_html")
    css = profile.get("untrusted_stylesheet")
    if (
        not isinstance(html, dict)
        or set(html) != {"elements", "global_attributes"}
        or not isinstance(html.get("elements"), dict)
        or not isinstance(html.get("global_attributes"), list)
    ):
        raise AuditError("publication profile HTML allowlists are malformed")
    elements = html["elements"]
    global_attributes = html["global_attributes"]
    if any(
        not isinstance(tag, str)
        or tag != tag.casefold()
        or tag not in FIXED_ELEMENT_ATTRIBUTES
        or not isinstance(attributes, list)
        or not all(isinstance(name, str) for name in attributes)
        or not set(attributes) <= FIXED_ELEMENT_ATTRIBUTES[tag]
        or len(attributes) != len(set(attributes))
        for tag, attributes in elements.items()
    ):
        raise AuditError("publication profile element allowlist is unsupported")
    if (
        not all(isinstance(name, str) and name in FIXED_GLOBAL_ATTRIBUTES for name in global_attributes)
        or len(global_attributes) != len(set(global_attributes))
    ):
        raise AuditError("publication profile global attribute allowlist is unsupported")
    if (
        not isinstance(css, dict)
        or set(css) != {"properties", "at_rules", "selector_surface"}
        or not isinstance(css.get("properties"), list)
        or not isinstance(css.get("at_rules"), list)
        or not isinstance(css.get("selector_surface"), list)
    ):
        raise AuditError("publication profile stylesheet allowlists are malformed")
    if (
        not all(isinstance(name, str) and not name.startswith("--") and name in FIXED_CSS_PROPERTIES
                for name in css["properties"])
        or len(css["properties"]) != len(set(css["properties"]))
    ):
        raise AuditError("publication profile CSS property allowlist is unsupported")
    if css["at_rules"] != []:
        raise AuditError("publication profile at-rules are unsupported")
    if (
        not all(isinstance(name, str) for name in css["selector_surface"])
        or not set(css["selector_surface"]) <= PROFILE_SELECTOR_SURFACE
        or len(css["selector_surface"]) != len(set(css["selector_surface"]))
    ):
        raise AuditError("publication profile selector surface is unsupported")
    prohibitions = profile.get("global_prohibitions")
    if (
        not isinstance(prohibitions, list)
        or not all(isinstance(name, str) for name in prohibitions)
        or set(prohibitions) != PROFILE_PROHIBITIONS
        or len(prohibitions) != len(set(prohibitions))
    ):
        raise AuditError("publication profile global prohibitions are unsupported")
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


def manifest_bbox(value: list[Any], context: str) -> list[float]:
    try:
        bbox = [float(coordinate) for coordinate in value]
    except (OverflowError, TypeError, ValueError) as error:
        raise PublicationError(
            f"{context} bbox coordinates are not representable"
        ) from error
    if not all(math.isfinite(coordinate) for coordinate in bbox):
        raise PublicationError(f"{context} bbox coordinates must be finite")
    return bbox


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
    errors: list[str] = []
    profile, profile_bytes = load_profile()
    profile_hash = hash_bytes(profile_bytes)
    policy = manifest["policies"]["publication_profile"]
    expected_identity = {"schema_version": "1.0", "profile_id": "scholarly-fragment-and-stylesheet-v1", "closed": True}
    for key, expected in expected_identity.items():
        if profile.get(key) != expected:
            errors.append(f"publication profile {key} is not {expected!r}")
    if (
        policy["id"] != profile.get("profile_id")
        or policy["schema_version"] != profile.get("schema_version")
        or policy["sha256"] != profile_hash
    ):
        errors.append("manifest publication-profile identity does not match QA")
    if manifest["print_geometry"]["page_size"] != page_size:
        errors.append(f"--page-size {page_size} conflicts with manifest geometry")
    tracked: dict[str, Asset] = {}
    declared_records: dict[str, dict[str, Any]] = {}
    aliases: dict[str, str] = {}
    for record in manifest["tracked_files"]:
        logical = manifest_path(record["path"])
        if logical in declared_records:
            errors.append(f"duplicate tracked path: {logical}")
            continue
        declared_records[logical] = record
        path = confined(root, logical)
        alias = os.path.normcase(str(path))
        if alias in aliases:
            errors.append(f"tracked paths alias: {aliases[alias]!r}, {logical!r}")
            continue
        aliases[alias] = logical
        if not path.is_file():
            errors.append(f"tracked file is missing: {logical}")
            continue
        observed = asset(path, logical)
        tracked[logical] = observed
        if observed.sha256 != record["sha256"] or observed.bytes != record["bytes"]:
            errors.append(f"tracked file binding mismatch: {logical}")
    unavailable = sorted(set(declared_records) - set(tracked))
    if unavailable:
        raise PublicationError(f"manifest-declared regular files are unavailable: {unavailable}")
    projections = projected_assets(manifest)
    for label, record in projections:
        logical = manifest_path(record["path"])
        if declared_records.get(logical) != record:
            errors.append(f"{label} does not exactly match tracked_files")
    unprojected = sorted(set(declared_records) - {manifest_path(record["path"]) for _label, record in projections})
    if unprojected:
        errors.append(f"tracked_files has unprojected assets: {unprojected}")
    html_logical = manifest_path(manifest["outputs"]["html"]["path"])
    css_logical = manifest_path(manifest["outputs"]["css"]["path"])
    pdf_record = manifest["outputs"]["draft_pdf"]
    if pdf_record is None:
        raise PublicationError("assembly manifest has no canonical PDF")
    pdf_logical = manifest_path(pdf_record["path"])
    if confined(root, html_logical) != html_file:
        errors.append("--html does not match manifest outputs.html")
    for label, logical in (("HTML", html_logical), ("CSS", css_logical), ("PDF", pdf_logical)):
        if logical not in tracked:
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
        if figure["caption_sha256"] != hash_bytes(figure["caption_html"].encode()):
            errors.append(f"figure {figure['id']} caption hash is inconsistent")
        for part in figure["parts"]:
            if part["dom_selector"] != f'[data-crop-id="{part["id"]}"]':
                errors.append(f"crop {part['id']} DOM selector is inconsistent")
            bbox = manifest_bbox(part["bbox"], f"crop {part['id']}")
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
                   tracked, tracked[html_logical], tracked[css_logical], tracked[pdf_logical], before, errors)
def parse_html(content: str) -> ParsedHtml:
    parser = TreeParser()
    parser.feed(content)
    return parser.result()
def html5_parser() -> Any:
    return html5lib.HTMLParser(tree=html5lib.getTreeBuilder("etree"), strict=False, namespaceHTMLElements=False)
def html5_findings(content: str, label: str) -> list[str]:
    parser = html5_parser()
    parser.parse(content)
    return [f"{label}: HTML parse error at {position[0]}:{position[1]} ({code})"
            for position, code, _data in parser.errors[:10]]
def parse_html5_fragment(content: str, label: str) -> ParsedHtml:
    parser = html5_parser()
    source = parser.parseFragment(content, container="div")
    root = Element("#document", {}, ())
    def append_children(source_node: Any, target: Element) -> None:
        if source_node.text:
            target.children.append(TextNode(source_node.text))
        for child in source_node:
            if isinstance(child.tag, str):
                node = Element(child.tag.casefold(), dict(child.attrib), tuple(child.attrib))
                append_children(child, node)
                target.children.append(node)
            else:
                target.children.append(CommentNode(child.text or ""))
            if child.tail:
                target.children.append(TextNode(child.tail))

    append_children(source, root)
    errors = [f"{label}: HTML parse error at {position[0]}:{position[1]} ({code})"
              for position, code, _data in parser.errors[:10]]
    return ParsedHtml(root, errors, 0)
def elements(node: Element) -> list[Element]:
    return [child for child in node.children if isinstance(child, Element)]
def walk(node: Element) -> list[Element]:
    result: list[Element] = []
    pending = list(reversed(elements(node)))
    while pending:
        current = pending.pop()
        result.append(current)
        pending.extend(reversed(elements(current)))
    return result
def normalize_text(value: str) -> str:
    return " ".join(unicodedata.normalize("NFC", value).split())
def visible_text(node: Element, *, skip_figures: bool = False, skip_templates: bool = False) -> str:
    parts: list[str] = []
    def visit(item: HtmlNode) -> None:
        if isinstance(item, TextNode):
            parts.append(item.text)
        elif isinstance(item, Element):
            if skip_figures and item.tag == "figure" and item.attrs.get("data-figure-id"):
                return
            if skip_templates and item.tag == "template":
                return
            for child in item.children:
                visit(child)

    for child in node.children:
        visit(child)
    return normalize_text("".join(parts))
def structure_signature(
    items: list[HtmlNode],
    *,
    drop_figures: bool,
) -> tuple[Any, ...]:
    result: list[Any] = []
    for item in items:
        if isinstance(item, TextNode):
            text = normalize_text(item.text)
            if text:
                if result and result[-1][0] == "text":
                    result[-1] = ("text", normalize_text(f"{result[-1][1]} {text}"))
                else:
                    result.append(("text", text))
        elif isinstance(item, CommentNode):
            marker = FIGURE_MARKER.fullmatch(item.text)
            if marker:
                result.append(("figure", marker.group("id")))
            else:
                result.append(("comment", normalize_text(item.text)))
        elif drop_figures and item.tag == "figure" and item.attrs.get("data-figure-id"):
            result.append(("figure", item.attrs["data-figure-id"]))
        else:
            result.append(
                (
                    item.tag,
                    tuple(sorted((key, value or "") for key, value in item.attrs.items())),
                    structure_signature(item.children, drop_figures=drop_figures),
                )
            )
    return tuple(result)
def nonwhitespace_text(node: Element) -> bool:
    return any(isinstance(item, TextNode) and item.text.strip() for item in node.children)
def exact_attrs(node: Element, expected: dict[str, str | None], label: str) -> list[str]:
    if node.attrs == expected and len(node.attr_names) == len(expected):
        return []
    return [f"{label} attributes are not canonical"]
def validate_attribute(  # noqa: PLR0911
    name: str,
    value: str | None,
) -> bool:
    text = "" if value is None else value
    if "\x00" in text:
        return False
    try:
        text.encode()
    except UnicodeEncodeError:
        return False
    if name in ATTRIBUTE_PLAIN_LIMITS:
        return len(text) <= ATTRIBUTE_PLAIN_LIMITS[name]
    if name in ATTRIBUTE_TOKEN_LIMITS:
        tokens = text.split()
        return 0 < len(tokens) <= ATTRIBUTE_TOKEN_LIMITS[name] and all(
            re.fullmatch(r"[A-Za-z][A-Za-z0-9._-]{0,127}", token)
            for token in tokens
        )
    if name == "class":
        tokens = text.split()
        return 0 < len(tokens) <= 16 and all(
            re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]{0,63}", token)
            for token in tokens
        )
    if name in {"colspan", "rowspan", "span"}:
        return re.fullmatch(r"[+-]?[0-9]+", text) is not None and 1 <= int(text) <= 100
    if name == "datetime":
        return re.fullmatch(r"[0-9A-Za-z:+.TZ-]{1,64}", text) is not None
    if name == "dir":
        return text in {"auto", "ltr", "rtl"}
    if name == "href":
        return re.fullmatch(r"#[A-Za-z][A-Za-z0-9._-]{0,127}", text) is not None
    if name == "id":
        return re.fullmatch(r"[A-Za-z][A-Za-z0-9._-]{0,127}", text) is not None
    if name == "lang":
        return LANGUAGE_PATTERN.fullmatch(text) is not None
    if name == "reversed":
        return value in {None, ""}
    if name == "scope":
        return text in {"col", "colgroup", "row", "rowgroup"}
    if name == "start":
        try:
            number = int(text)
        except ValueError:
            return False
        return -1_000_000 <= number <= 1_000_000 and re.fullmatch(r"[+-]?[0-9]+", text) is not None
    if name == "type":
        return text in {"1", "A", "I", "a", "i"}
    return False
def figure_class_ok(value: str | None) -> bool:
    tokens = (value or "").split()
    extras = tokens[1:]
    return (
        bool(tokens)
        and tokens[0] == "publication-figure"
        and value == " ".join(tokens)
        and len(tokens) == len(set(tokens))
        and not GENERATED_CLASSES.intersection(extras)
        and (not extras or validate_attribute("class", " ".join(extras)))
    )
def validate_fragment(
    content: str,
    profile: dict[str, Any],
    label: str,
) -> tuple[list[str], set[str]]:
    parsed = parse_html5_fragment(content, label)
    errors = list(parsed.errors)
    policy = profile["fragment_html"]
    allowed_elements = policy["elements"]
    global_attrs = set(policy["global_attributes"])
    identifiers: set[str] = set()
    for node in walk(parsed.root):
        allowed = allowed_elements.get(node.tag)
        if not isinstance(allowed, list):
            errors.append(f"{label}: element <{node.tag}> is not allowed")
            continue
        for name, value in node.attrs.items():
            if name not in allowed and name not in global_attrs:
                errors.append(f"{label}: <{node.tag}> attribute {name} is not allowed")
            elif not validate_attribute(name, value):
                errors.append(f"{label}: <{node.tag}> attribute {name} is invalid")
            elif name == "class":
                reserved = GENERATED_CLASSES.intersection((value or "").split())
                if reserved:
                    errors.append(
                        f"{label}: <{node.tag}> class uses assembler-owned "
                        f"classes: {', '.join(sorted(reserved))}"
                    )
            if name == "id":
                text = value or ""
                if text in identifiers:
                    errors.append(f"{label}: duplicate ID {text!r}")
                identifiers.add(text)
    pending = list(parsed.root.children)
    while pending:
        item = pending.pop()
        if isinstance(item, CommentNode):
            if FIGURE_MARKER.fullmatch(item.text) is None:
                errors.append(f"{label}: comment is not allowed")
        elif isinstance(item, Element):
            pending.extend(item.children)
    return errors, identifiers
def css_text(tokens: list[Any]) -> str:
    return re.sub(r"\s+", " ", tinycss2.serialize(tokens).strip())
def css_groups(tokens: list[Any]) -> list[list[Any]]:
    groups: list[list[Any]] = [[]]
    for token in tokens:
        if token.type == "literal" and token.value == ",":
            groups.append([])
        else:
            groups[-1].append(token)
    return groups
def css_ident(token: Any) -> str | None:
    return str(token.lower_value) if token.type == "ident" else None

def lang_selector_ok(token: Any) -> bool:
    content = significant(token.content if token.type == "[] block" else token.arguments)
    if token.type == "function":
        return (
            len(content) == 1
            and content[0].type in {"ident", "string"}
            and LANGUAGE_PATTERN.fullmatch(str(content[0].value)) is not None
        )
    if len(content) == 1:
        return css_ident(content[0]) == "lang"
    return (
        len(content) in {3, 4}
        and css_ident(content[0]) == "lang"
        and content[1].type == "literal"
        and content[1].value in {"=", "|="}
        and content[2].type in {"ident", "string"}
        and LANGUAGE_PATTERN.fullmatch(str(content[2].value)) is not None
        and (len(content) == 3 or css_ident(content[3]) in {"i", "s"})
    )

def selector_ok(  # noqa: PLR0911
    tokens: list[Any],
    allowed_elements: set[str],
    surface: set[str],
) -> bool:
    clean = [token for token in tokens if token.type != "comment"]
    while clean and clean[0].type == "whitespace":
        clean.pop(0)
    while clean and clean[-1].type == "whitespace":
        clean.pop()
    if not clean:
        return False
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
            if "descendant" not in surface:
                return False
            has_simple = False
            continue
        if token.type == "literal" and token.value == ">":
            if not has_simple or "child" not in surface:
                return False
            has_simple = False
            index += 1
            continue
        step = 1
        if token.type == "ident":
            if "type" not in surface or has_simple or str(token.lower_value) not in allowed_elements:
                return False
        elif token.type == "literal" and token.value == "*":
            if "universal" not in surface or has_simple:
                return False
        elif token.type == "hash" and token.is_identifier:
            if (
                "id" not in surface
                or re.fullmatch(r"[A-Za-z][A-Za-z0-9._-]{0,127}", token.value) is None
            ):
                return False
        elif token.type == "literal" and token.value == ".":
            if (
                "class" not in surface
                or
                index + 1 >= len(clean)
                or clean[index + 1].type != "ident"
                or re.fullmatch(r"[A-Za-z][A-Za-z0-9_-]{0,63}", str(clean[index + 1].value)) is None
            ):
                return False
            step = 2
        elif token.type == "[] block":
            if "lang-attribute" not in surface or not lang_selector_ok(token):
                return False
        elif token.type == "literal" and token.value == ":":
            if (
                "lang-pseudo-class" not in surface
                or index + 1 >= len(clean)
                or clean[index + 1].type != "function"
            ):
                return False
            pseudo = clean[index + 1]
            if pseudo.lower_name != "lang" or not lang_selector_ok(pseudo):
                return False
            step = 2
        else:
            return False
        has_simple = True
        index += step
    return has_simple
def significant(tokens: list[Any]) -> list[Any]:
    return [token for token in tokens if token.type not in {"comment", "whitespace"}]
def number_ok(
    token: Any,
    units: frozenset[str],
    minimum: float,
    maximum: float,
    maximum_percentage: float | None = None,
    minimum_percentage: float | None = None,
) -> bool:
    if token.type == "dimension" and token.lower_unit in units:
        value = float(token.value)
        lower = minimum
        upper = maximum
    elif token.type == "percentage" and maximum_percentage is not None:
        value = float(token.value)
        lower = (
            minimum if minimum_percentage is None else minimum_percentage
        )
        upper = maximum_percentage
    elif token.type == "number" and float(token.value) == 0:
        value = 0.0
        lower = minimum
        upper = maximum
    else:
        return False
    return math.isfinite(value) and lower <= value <= upper
def color_ok(token: Any) -> bool:
    if token.type == "ident":
        return token.value.casefold() in CSS_NAMED_COLORS
    if token.type != "hash" or re.fullmatch(r"[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}", token.value) is None:
        return False
    value = token.value
    if len(value) == 3:
        rgb = [int(character * 2, 16) for character in value]
    else:
        rgb = [int(value[index : index + 2], 16) for index in range(0, 6, 2)]
    def linear(channel: int) -> float:
        fraction = channel / 255
        return fraction / 12.92 if fraction <= 0.04045 else ((fraction + 0.055) / 1.055) ** 2.4

    luminance = 0.2126 * linear(rgb[0]) + 0.7152 * linear(rgb[1]) + 0.0722 * linear(rgb[2])
    return 1.05 / (luminance + 0.05) + 1e-09 >= 3
def family_list(tokens: list[Any]) -> list[tuple[str, bool]] | None:
    families: list[tuple[str, bool]] = []
    for group in css_groups(tokens):
        values = significant(group)
        if len(values) == 1 and values[0].type == "string":
            families.append((str(values[0].value), False))
        elif values and all(value.type == "ident" for value in values):
            families.append((" ".join(str(value.value) for value in values), len(values) == 1))
        else:
            return None
    return families
def css_value_ok(  # noqa: PLR0911
    tokens: list[Any],
    property_name: str,
    families: set[str],
) -> bool:
    values = significant(tokens)
    if not values or any(
        token.type in {"() block", "[] block", "{} block", "error", "function", "url"} for token in values
    ):
        return False
    text = css_text(values).casefold()
    if len(values) == 1 and values[0].type == "ident" and text in CSS_WIDE_KEYWORDS:
        return True
    if property_name == "font-family":
        names = family_list(tokens)
        declared = {value.casefold() for value in families}
        return (
            names is not None
            and len(names) <= 8
            and all(name.casefold() in declared or (identifier and name.casefold() in CSS_GENERIC_FAMILIES)
                    for name, identifier in names)
        )
    if property_name == "border-spacing":
        return 1 <= len(values) <= 2 and all(
            number_ok(value, CSS_LENGTH_UNITS, 0, 256) for value in values
        )
    if property_name in CSS_ENUM_VALUES:
        return len(values) == 1 and text in CSS_ENUM_VALUES[property_name]
    if property_name in CSS_BORDER_STYLE_PROPERTIES:
        return len(values) == 1 and text in {"dashed", "dotted", "double", "none", "solid"}
    if property_name in CSS_KEYWORD_SET_VALUES:
        words = [str(value.value).casefold() for value in values if value.type == "ident"]
        word_set = set(words)
        return (
            len(words) == len(values) == len(set(words))
            and word_set <= CSS_KEYWORD_SET_VALUES[property_name]
            and (not word_set.intersection({"none", "normal"}) or len(words) == 1)
            and all(
                len(word_set.intersection(group)) <= 1
                for group in CSS_KEYWORD_EXCLUSIVE_GROUPS.get(property_name, ())
            )
        )
    if property_name == "font-weight":
        if len(values) == 1 and text in {"bold", "normal"}:
            return True
        return (
            len(values) == 1
            and values[0].type == "number"
            and math.isfinite(float(values[0].value))
            and float(values[0].value).is_integer()
            and 100 <= float(values[0].value) <= 900
            and int(values[0].value) % 100 == 0
        )
    if property_name in CSS_WIDOW_ORPHAN_PROPERTIES:
        return (
            len(values) == 1
            and values[0].type == "number"
            and math.isfinite(float(values[0].value))
            and float(values[0].value).is_integer()
            and 1 <= float(values[0].value) <= 10
        )
    if property_name == "tab-size":
        return (
            len(values) == 1
            and values[0].type == "number"
            and math.isfinite(float(values[0].value))
            and float(values[0].value).is_integer()
            and 1 <= float(values[0].value) <= 16
        )
    if len(values) != 1:
        return False
    token = values[0]
    if property_name in CSS_COLOR_PROPERTIES:
        return color_ok(token)
    if property_name in CSS_NONNEGATIVE_LENGTH_PERCENTAGE_PROPERTIES:
        return number_ok(token, CSS_LENGTH_UNITS, 0, 256, 100)
    if property_name in CSS_BORDER_WIDTH_PROPERTIES:
        return text in {"medium", "thick", "thin"} or number_ok(token, CSS_LENGTH_UNITS, 0, 12)
    if property_name == "font-size":
        return text in {"large", "larger", "medium", "small", "smaller", "x-large", "x-small"} or number_ok(
            token, CSS_LENGTH_UNITS, 0.5, 200, 200
        )
    if property_name == "text-decoration-thickness":
        return text in {"auto", "from-font"} or number_ok(token, CSS_LENGTH_UNITS, 0, 8)
    if property_name == "vertical-align":
        return text in {
            "baseline", "bottom", "middle", "sub", "super", "text-bottom", "text-top", "top",
        } or number_ok(token, CSS_LENGTH_UNITS, -4, 4, 100)
    if property_name == "line-height":
        if text == "normal":
            return True
        if token.type == "number":
            value = float(token.value)
            return math.isfinite(value) and 0.8 <= value <= 4
        return number_ok(
            token,
            CSS_LENGTH_UNITS,
            0.8,
            4,
            minimum_percentage=80,
            maximum_percentage=400,
        )
    if property_name in CSS_SIGNED_SPACING_PROPERTIES:
        return text == "normal" or number_ok(
            token, frozenset({"ch", "em", "ex", "pt", "px", "rem"}), -4, 16
        )
    if property_name == "text-indent":
        return number_ok(
            token, frozenset({"ch", "em", "ex", "pt", "px", "rem"}), 0, 16, 25
        )
    return False
def declaration_tuple(
    content: list[Any],
    *,
    profile: dict[str, Any] | None = None,
    families: set[str] | None = None,
) -> tuple[tuple[str, str], ...] | None:
    result: list[tuple[str, str]] = []
    seen: set[str] = set()
    policy = profile["untrusted_stylesheet"] if profile else None
    for declaration in tinycss2.parse_declaration_list(content, skip_comments=True, skip_whitespace=True):
        if declaration.type != "declaration" or declaration.important:
            return None
        if policy:
            properties = set(policy["properties"])
            if declaration.lower_name in seen or declaration.lower_name not in properties or not css_value_ok(
                declaration.value,
                declaration.lower_name,
                families or set(),
            ):
                return None
            seen.add(declaration.lower_name)
        result.append((declaration.lower_name, css_text(declaration.value)))
    return tuple(result) if result or not policy else None
def validate_stylesheet(
    content: str,
    profile: dict[str, Any],
    families: set[str],
    label: str,
) -> tuple[list[str], list[tuple[tuple[str, ...], tuple[tuple[str, str], ...]]]]:
    errors: list[str] = []
    records: list[tuple[tuple[str, ...], tuple[tuple[str, str], ...]]] = []
    allowed_elements = set(profile["fragment_html"]["elements"])
    selector_surface = set(profile["untrusted_stylesheet"]["selector_surface"])
    for index, rule in enumerate(
        tinycss2.parse_stylesheet(content, skip_comments=True, skip_whitespace=True),
        1,
    ):
        if rule.type != "qualified-rule":
            errors.append(f"{label} rule {index}: only qualified rules are allowed")
            continue
        declarations = declaration_tuple(rule.content, profile=profile, families=families)
        if declarations is None:
            errors.append(f"{label} rule {index}: declarations violate the profile")
            continue
        selectors: list[str] = []
        groups = css_groups(rule.prelude)
        for group in groups:
            selector = css_text(group)
            if not selector_ok(group, allowed_elements, selector_surface):
                errors.append(f"{label} rule {index}: selector violates the profile")
            else:
                selectors.append(selector)
        if len(selectors) == len(groups):
            records.append((tuple(selectors), declarations))
    return errors, records
def css_string(value: str) -> str:
    escaped: list[str] = []
    for character in value:
        if unicodedata.category(character) in CSS_UNSAFE_STRING_CATEGORIES:
            escaped.append(f"\\{ord(character):x} ")
        elif character in {'"', "\\"}:
            escaped.append("\\" + character)
        else:
            escaped.append(character)
    return '"' + "".join(escaped) + '"'
def relative_url(owner: Path, resource: Path) -> str:
    return manifest_path(os.path.relpath(resource, owner.parent).replace("\\", "/"))
def scoped_selector(selectors: tuple[str, ...]) -> str:
    return ", ".join(f"[data-fragment-id] {selector}" for selector in selectors)
def validate_generated_css(context: Context) -> list[Any]:
    errors: list[Any] = []
    try:
        content = context.css.file.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        return [text_read_diagnostic(context.css.path, "generated-css", error)]
    families = {font["family"] for font in context.manifest["fonts"]}
    expected_additional: list[tuple[tuple[str, ...], tuple[tuple[str, str], ...]]] = []
    for record in context.manifest["stylesheets"]:
        try:
            stylesheet = confined(context.root, record["path"]).read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            errors.append(text_read_diagnostic(record["path"], "retained-stylesheet", error))
            continue
        findings, records = validate_stylesheet(
            stylesheet,
            context.profile,
            families,
            f"stylesheet {record['path']}",
        )
        errors.extend(findings)
        expected_additional.extend(records)
    expected_fonts = []
    for font in context.manifest["fonts"]:
        path = relative_url(context.css.file, confined(context.root, font["asset"]["path"]))
        fmt = "truetype" if path.casefold().endswith(".ttf") else "opentype"
        expected_fonts.append(
            (
                ("font-family", css_string(font["family"])),
                ("src", f"url({css_string(path)}) format({css_string(fmt)})"),
                ("font-style", font["style"]),
                ("font-weight", str(font["weight"])),
                ("font-display", "block"),
            )
        )
    margins = context.manifest["print_geometry"]["margin_in"]
    expected_page = (
        (
            "size",
            PAGE_CSS_NAMES[context.manifest["print_geometry"]["page_size"]],
        ),
        (
            "margin",
            (f"{margins['top']}in {margins['right']}in {margins['bottom']}in {margins['left']}in"),
        ),
    )
    roles = context.manifest["font_roles"]
    role_root = (
        ("--body-cjk", css_string(roles["body-cjk"])),
        ("--body-latin", css_string(roles["body-latin"])),
    )
    expected: list[Any] = [
        *(("font-face", declarations) for declarations in expected_fonts),
        ("rule", ":root", BASE_ROOT),
        ("page", BASE_PAGE),
        *(("rule", selector, declarations) for selector, declarations in BASE_RULES.items()),
        ("media-screen", tuple(SCREEN_RULES.items())),
        ("page", expected_page),
        ("rule", ":root", role_root),
        *(("rule", scoped_selector(selectors), declarations) for selectors, declarations in expected_additional),
    ]
    seen: list[Any] = []
    rules = tinycss2.parse_stylesheet(content, skip_comments=True, skip_whitespace=True)
    for rule in rules:
        if rule.type == "error":
            errors.append(f"generated CSS parse error: {rule.message}")
            continue
        if rule.type == "at-rule":
            declarations = declaration_tuple(rule.content or [])
            if rule.lower_at_keyword == "font-face" and not significant(rule.prelude) and declarations is not None:
                seen.append(("font-face", declarations))
            elif rule.lower_at_keyword == "page" and not significant(rule.prelude) and declarations is not None:
                seen.append(("page", declarations))
            elif rule.lower_at_keyword == "media" and css_text(rule.prelude).casefold() == "screen":
                nested = tinycss2.parse_rule_list(rule.content or [], skip_comments=True, skip_whitespace=True)
                nested_records: list[Any] = []
                for child in nested:
                    if child.type != "qualified-rule":
                        errors.append("generated @media screen contains a non-rule")
                        continue
                    selector = css_text(child.prelude)
                    nested_records.append((selector, declaration_tuple(child.content)))
                seen.append(("media-screen", tuple(nested_records)))
            else:
                errors.append(f"generated CSS has unsupported @{rule.lower_at_keyword}")
            continue
        if rule.type != "qualified-rule":
            errors.append("generated CSS contains an unsupported rule")
            continue
        selector = css_text(rule.prelude)
        declarations = declaration_tuple(rule.content)
        if declarations is None:
            errors.append(f"generated CSS rule {selector!r} is invalid")
        seen.append(("rule", selector, declarations))
    if seen != expected:
        errors.append("generated CSS rule sequence differs from the closed profile")
    return errors
def parse_svg_geometry(path: Path, logical: str) -> tuple[float, float, list[dict[str, Any]]]:
    try:
        content = path.read_bytes()
    except OSError:
        return 0, 0, [source_svg_diagnostic(logical, "read-error")]
    try:
        root = fromstring(content)
    except (ParseError, DefusedXmlException):
        return 0, 0, [source_svg_diagnostic(logical, "xml-parse-error")]
    errors: list[dict[str, Any]] = []
    if root.tag != f"{{{SVG_NAMESPACE}}}svg":
        errors.append(source_svg_diagnostic(logical, "wrong-root"))
    width_match = SVG_LENGTH.fullmatch(root.attrib.get("width", ""))
    height_match = SVG_LENGTH.fullmatch(root.attrib.get("height", ""))
    if width_match is None or height_match is None:
        return 0, 0, [*errors, source_svg_diagnostic(logical, "invalid-geometry")]
    width, height = float(width_match.group(1)), float(height_match.group(1))
    if not math.isfinite(width) or not math.isfinite(height) or width <= 0 or height <= 0:
        errors.append(source_svg_diagnostic(logical, "invalid-geometry"))
    view_box = root.attrib.get("viewBox")
    if not same_numbers(parse_numbers(view_box), [0, 0, width, height]):
        errors.append(source_svg_diagnostic(logical, "invalid-viewbox"))
    return width, height, errors
def parse_numbers(value: str | None) -> list[float] | None:
    try:
        numbers = [float(part) for part in re.split(r"[\s,]+", value.strip()) if part] if value else None
    except ValueError:
        return None
    return numbers if numbers and all(math.isfinite(number) for number in numbers) else None
def same_numbers(
    actual: list[float] | None,
    expected: list[float],
    tolerance: float = 0.001,
) -> bool:
    return (
        actual is not None
        and len(actual) == len(expected)
        and all(abs(left - right) <= tolerance for left, right in zip(actual, expected, strict=True))
    )
def resolve_url(root: Path, owner: Path, value: str) -> Path:
    try:
        parsed = urlsplit(value)
    except (UnicodeError, ValueError) as error:
        raise ResourceUrlError("invalid", value) from error
    if parsed.scheme or parsed.netloc or parsed.query or parsed.fragment or value.startswith(("//", "/", "\\")):
        raise ResourceUrlError("nonlocal", value)
    if "\x00" in value or re.search(r"%(?![0-9A-Fa-f]{2})", parsed.path):
        raise ResourceUrlError("invalid", value)
    try:
        decoded = unquote(parsed.path, errors="strict")
        path = (owner.parent / decoded).resolve()
    except (OSError, UnicodeError, ValueError) as error:
        raise ResourceUrlError("invalid", value) from error
    try:
        path.relative_to(root)
    except ValueError as error:
        raise ResourceUrlError("escapes-root", value) from error
    return path
def caption_matches(node: Element, expected: str) -> bool:
    parsed = parse_html5_fragment(expected, "caption")
    if parsed.errors or parsed.doctypes:
        return False
    def signature(items: list[HtmlNode]) -> tuple[Any, ...]:
        result: list[Any] = []
        for item in items:
            if isinstance(item, TextNode):
                text = normalize_text(item.text)
                if text:
                    result.append(("text", text))
            elif isinstance(item, CommentNode):
                result.append(("comment", normalize_text(item.text)))
            else:
                result.append(
                    (
                        item.tag,
                        tuple(sorted((key, value or "") for key, value in item.attrs.items())),
                        signature(item.children),
                    )
                )
        return tuple(result)

    return signature(node.children) == signature(parsed.root.children)
def validate_crop(
    node: Element,
    figure: dict[str, Any],
    part: dict[str, Any],
    context: Context,
) -> tuple[list[Any], dict[str, Any]]:
    errors: list[Any] = []
    bbox = [float(value) for value in part["bbox"]]
    crop_width, crop_height = bbox[2] - bbox[0], bbox[3] - bbox[1]
    title = f"{figure['alt']} - part {part['order']}"
    expected_attrs = {
        "class": "figure-part",
        "data-crop-id": part["id"],
        "xmlns": SVG_NAMESPACE,
        "viewbox": f"{bbox[0]:.3f} {bbox[1]:.3f} {crop_width:.3f} {crop_height:.3f}",
        "width": f"{crop_width:.3f}",
        "height": f"{crop_height:.3f}",
        "role": "img",
        "aria-label": title,
        "preserveaspectratio": "xMidYMid meet",
    }
    errors.extend(exact_attrs(node, expected_attrs, f"crop {part['id']}"))
    children = elements(node)
    if nonwhitespace_text(node):
        errors.append(f"crop {part['id']} has unexpected direct text")
    source = confined(context.root, part["source_svg"]["path"])
    source_width, source_height, svg_errors = parse_svg_geometry(
        source,
        part["source_svg"]["path"],
    )
    errors.extend(svg_errors)
    structure_ok = [child.tag for child in children] == ["title", "image"]
    if not structure_ok:
        errors.append(f"crop {part['id']} must contain title then image")
    else:
        title_node, image = children
        if title_node.attrs or elements(title_node) or visible_text(title_node) != normalize_text(title):
            errors.append(f"crop {part['id']} title is not canonical")
        expected_image: dict[str, str | None] = {
            "href": relative_url(context.html.file, source),
            "x": "0",
            "y": "0",
            "width": f"{source_width:.3f}",
            "height": f"{source_height:.3f}",
        }
        errors.extend(exact_attrs(image, expected_image, f"crop {part['id']} image"))
        if elements(image) or nonwhitespace_text(image):
            errors.append(f"crop {part['id']} image must be empty")
        href = image.attrs.get("href") or ""
        try:
            resolved = resolve_url(context.root, context.html.file, href)
        except ResourceUrlError as error:
            errors.append(
                {
                    **error.diagnostic,
                    "context": "figure-href",
                    "crop_id": part["id"],
                }
            )
        else:
            category = None
            if resolved != source:
                category = "wrong-binding"
            elif href != expected_image["href"]:
                category = "noncanonical"
            if category is not None:
                errors.append(
                    {
                        **resource_url_diagnostic(href, category),
                        "context": "figure-href",
                        "crop_id": part["id"],
                    }
                )
    bounds_ok = (
        source_width > 0
        and source_height > 0
        and 0 <= bbox[0] < bbox[2] <= source_width + 0.01
        and 0 <= bbox[1] < bbox[3] <= source_height + 0.01
    )
    bbox_ok = bounds_ok and same_numbers(parse_numbers(node.attrs.get("viewbox")), [bbox[0], bbox[1], crop_width, crop_height])
    geometry_ok = (
        same_numbers(parse_numbers(node.attrs.get("width")), [crop_width])
        and same_numbers(parse_numbers(node.attrs.get("height")), [crop_height])
        and structure_ok
        and source_width > 0
        and source_height > 0
    )
    return errors, {
        "id": part["id"],
        "figure_id": figure["id"],
        "pdf_page": part["pdf_page"],
        "dom_matches": 1,
        "source_svg_sha256": part["source_svg"]["sha256"],
        "bbox_matches": bbox_ok,
        "geometry_matches": geometry_ok,
    }
def validate_figure(
    node: Element,
    figure: dict[str, Any],
    context: Context,
) -> tuple[list[Any], dict[str, Any], list[dict[str, Any]]]:
    class_value = node.attrs.get("class")
    errors: list[Any] = exact_attrs(
        node,
        {
            "id": figure["dom_id"],
            "class": class_value,
            "data-figure-id": figure["id"],
            "role": "group",
            "aria-label": figure["alt"],
        },
        f"figure {figure['id']}",
    )
    if not figure_class_ok(class_value):
        errors.append(f"figure {figure['id']} class list is not canonical")
    children = elements(node)
    if nonwhitespace_text(node):
        errors.append(f"figure {figure['id']} has unexpected direct text")
    caption_ok = False
    crops: list[dict[str, Any]] = []
    if [child.tag for child in children] != ["div", "figcaption"]:
        errors.append(f"figure {figure['id']} must contain parts then caption")
    else:
        parts, caption = children
        errors.extend(exact_attrs(parts, {"class": "figure-parts"}, "figure parts"))
        errors.extend(exact_attrs(caption, {}, "figcaption"))
        if nonwhitespace_text(parts):
            errors.append(f"figure {figure['id']} parts have unexpected direct text")
        caption_findings, _ = validate_fragment(
            figure["caption_html"],
            context.profile,
            f"figure {figure['id']} caption",
        )
        errors.extend(caption_findings)
        caption_matches_manifest = caption_matches(
            caption,
            figure["caption_html"],
        )
        caption_ok = not caption_findings and caption_matches_manifest
        if not caption_matches_manifest:
            errors.append(f"figure {figure['id']} caption does not match manifest")
        crop_nodes = elements(parts)
        if len(crop_nodes) != len(figure["parts"]):
            errors.append(f"figure {figure['id']} crop count differs from manifest")
        for part, crop in zip(figure["parts"], crop_nodes, strict=False):
            if crop.tag != "svg":
                errors.append(f"figure {figure['id']} contains a non-SVG crop")
                continue
            findings, record = validate_crop(crop, figure, part, context)
            errors.extend(findings)
            crops.append(record)
    return (
        errors,
        {
            "id": figure["id"],
            "dom_id": figure["dom_id"],
            "matches": 1,
            "caption_matches": caption_ok,
        },
        crops,
    )
def collect_ids(root: Element) -> tuple[set[str], list[str]]:
    values = [node.attrs["id"] for node in walk(root) if node.attrs.get("id")]
    return set(values), duplicate_values(values)
def resource_errors(parsed: ParsedHtml, context: Context) -> list[Any]:
    errors: list[Any] = []
    identifiers, _ = collect_ids(parsed.root)
    for node in walk(parsed.root):
        for name in ("aria-describedby", "aria-labelledby", "headers"):
            for target in (node.attrs.get(name) or "").split():
                if target not in identifiers:
                    errors.append(f"<{node.tag}> {name} target is missing: {target!r}")
        for name in node.attrs:
            if name.startswith(("on", "shadowroot")) or name in {
                "srcdoc",
                "http-equiv",
                "style",
            }:
                errors.append(f"<{node.tag}> has active attribute {name}")
        for name in RESOURCE_ATTRIBUTES:
            value = node.attrs.get(name)
            if value is None:
                continue
            if node.tag == "a" and name == "href" and value.startswith("#"):
                if not value[1:] or value[1:] not in identifiers:
                    errors.append(
                        {
                            **resource_url_diagnostic(
                                value,
                                "missing-internal-target",
                            ),
                            "element": node.tag,
                            "attribute": name,
                        }
                    )
                continue
            if name == "srcset" or not (
                (node.tag == "link" and name == "href") or (node.tag == "image" and name == "href")
            ):
                errors.append(
                    {
                        **resource_url_diagnostic(
                            value,
                            "unsupported-attribute",
                        ),
                        "element": node.tag,
                        "attribute": name,
                    }
                )
                continue
            try:
                resource = resolve_url(context.root, context.html.file, value)
            except ResourceUrlError as error:
                errors.append(
                    {
                        **error.diagnostic,
                        "element": node.tag,
                        "attribute": name,
                    }
                )
                continue
            logical = resource.relative_to(context.root).as_posix()
            if logical not in context.tracked or not resource.is_file():
                errors.append(
                    {
                        **resource_url_diagnostic(
                            value,
                            "untracked-or-missing",
                        ),
                        "element": node.tag,
                        "attribute": name,
                    }
                )
    return errors
def audit_html(context: Context) -> StaticAudit:
    errors: list[Any] = []
    binding_errors: list[Any] = []
    figures: list[dict[str, Any]] = []
    crops: list[dict[str, Any]] = []
    try:
        content = context.html.file.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        return StaticAudit(
            [text_read_diagnostic(context.html.path, "generated-html", error)],
            [],
            {},
            [],
            [],
        )
    errors.extend(html5_findings(content, "generated HTML"))
    parsed = parse_html(content)
    errors.extend(parsed.errors)
    if parsed.doctypes != 1:
        errors.append("HTML must have exactly one leading doctype")
    roots = elements(parsed.root)
    if [node.tag for node in roots] != ["html"]:
        return StaticAudit([*errors, "HTML must have one html root"], [], {}, [], [])
    html = roots[0]
    language = context.manifest["document"]["language"]
    errors.extend(exact_attrs(html, {"lang": language}, "html"))
    if nonwhitespace_text(parsed.root) or nonwhitespace_text(html):
        errors.append("document has text outside head/body content")
    html_children = elements(html)
    if [node.tag for node in html_children] != ["head", "body"]:
        return StaticAudit([*errors, "html must contain head then body"], [], {}, [], [])
    head, body = html_children
    errors.extend(exact_attrs(head, {}, "head"))
    if nonwhitespace_text(head) or nonwhitespace_text(body):
        errors.append("head/body has non-whitespace direct text")
    head_children = elements(head)
    if [node.tag for node in head_children] != ["meta", "title", "link"]:
        errors.append("head must contain meta, title, link")
    else:
        charset, title, link = head_children
        errors.extend(exact_attrs(charset, {"charset": "utf-8"}, "charset meta"))
        errors.extend(
            exact_attrs(
                title,
                {"lang": context.manifest["document"]["title_language"]},
                "title",
            )
        )
        if elements(title) or visible_text(title) != normalize_text(context.manifest["document"]["title"]):
            errors.append("title is not the manifest plain-text title")
        errors.extend(
            exact_attrs(
                link,
                {
                    "rel": "stylesheet",
                    "href": relative_url(context.html.file, context.css.file),
                },
                "stylesheet link",
            )
        )
    errors.extend(exact_attrs(body, {}, "body"))
    body_children = elements(body)
    if [node.tag for node in body_children] != ["main"]:
        return StaticAudit([*errors, "body must contain one main"], [], {}, [], [])
    main = body_children[0]
    errors.extend(exact_attrs(main, {"id": context.manifest["publication_id"]}, "main"))
    if nonwhitespace_text(main):
        errors.append("main has non-whitespace direct text")
    sections = elements(main)
    fragment_ids = [fragment["id"] for fragment in context.manifest["fragments"]]
    observed_fragments = [
        section.attrs.get("data-fragment-id") if section.tag == "section" else None for section in sections
    ]
    if observed_fragments != fragment_ids:
        errors.append("fragment section order does not match manifest")
    fragment_dom_ids: set[str] = set()
    fragment_text: list[dict[str, str]] = []
    for fragment, section in zip(context.manifest["fragments"], sections, strict=False):
        errors.extend(
            exact_attrs(
                section,
                {"data-fragment-id": fragment["id"]},
                f"fragment section {fragment['id']}",
            )
        )
        path = confined(context.root, fragment["asset"]["path"])
        try:
            content = path.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            errors.append(
                text_read_diagnostic(
                    fragment["asset"]["path"],
                    "fragment",
                    error,
                    fragment_id=fragment["id"],
                )
            )
            continue
        findings, identifiers = validate_fragment(content, context.profile, f"fragment {fragment['id']}")
        errors.extend(findings)
        overlap = fragment_dom_ids & identifiers
        if overlap:
            errors.append(f"duplicate IDs across fragments: {sorted(overlap)}")
        fragment_dom_ids.update(identifiers)
        copied = parse_html5_fragment(content, f"fragment {fragment['id']}")
        copied_hash = hash_bytes(visible_text(copied.root, skip_templates=True).encode())
        section_hash = hash_bytes(visible_text(section, skip_figures=True, skip_templates=True).encode())
        if copied_hash != fragment["visible_text_sha256"]:
            errors.append(f"fragment {fragment['id']} visible-text hash mismatch")
        if section_hash != fragment["visible_text_sha256"]:
            errors.append(f"fragment section {fragment['id']} text mismatch")
        copied_structure = structure_signature(copied.root.children, drop_figures=False)
        rendered_structure = structure_signature(section.children, drop_figures=True)
        if copied_structure != rendered_structure:
            errors.append(f"fragment section {fragment['id']} structure mismatch")
        fragment_text.append({"id": fragment["id"], "visible_text_sha256": section_hash})
    observed_figure_nodes = [node for node in walk(main) if node.tag == "figure" and node.attrs.get("data-figure-id")]
    observed_crop_nodes = [node for node in walk(main) if node.tag == "svg" and node.attrs.get("data-crop-id")]
    expected_figure_ids = [figure["id"] for figure in context.manifest["figures"]]
    expected_crop_ids = [part["id"] for figure in context.manifest["figures"] for part in figure["parts"]]
    observed_figure_ids = [node.attrs.get("data-figure-id") for node in observed_figure_nodes]
    observed_crop_ids = [node.attrs.get("data-crop-id") for node in observed_crop_nodes]
    if observed_figure_ids != expected_figure_ids:
        binding_errors.append("figure order does not match manifest")
    if observed_crop_ids != expected_crop_ids:
        binding_errors.append("crop order does not match manifest")
    tag_counts = Counter(node.tag for node in walk(main))
    for tag, expected in {
        "figure": len(expected_figure_ids),
        "figcaption": len(expected_figure_ids),
        "svg": len(expected_crop_ids),
        "image": len(expected_crop_ids),
        "title": len(expected_crop_ids),
    }.items():
        if tag_counts[tag] != expected:
            binding_errors.append(f"generated <{tag}> count is {tag_counts[tag]}, expected {expected}")
    for figure in context.manifest["figures"]:
        matches = [node for node in observed_figure_nodes if node.attrs.get("data-figure-id") == figure["id"]]
        if len(matches) != 1:
            binding_errors.append(f"figure {figure['id']} occurs {len(matches)} times")
            figures.append(
                {
                    "id": figure["id"],
                    "dom_id": figure["dom_id"],
                    "matches": len(matches),
                    "caption_matches": False,
                }
            )
            continue
        findings, record, crop_records = validate_figure(matches[0], figure, context)
        binding_errors.extend(findings)
        figures.append(record)
        crops.extend(crop_records)
    crop_map = {record["id"]: record for record in crops}
    for figure in context.manifest["figures"]:
        for part in figure["parts"]:
            crop_map.setdefault(
                part["id"],
                {
                    "id": part["id"],
                    "figure_id": figure["id"],
                    "pdf_page": part["pdf_page"],
                    "dom_matches": observed_crop_ids.count(part["id"]),
                    "source_svg_sha256": part["source_svg"]["sha256"],
                    "bbox_matches": False,
                    "geometry_matches": False,
                },
            )
    crops = [crop_map[part["id"]] for figure in context.manifest["figures"] for part in figure["parts"]]
    allowed = set(context.profile["fragment_html"]["elements"])
    generated = {"section", "figure", "figcaption", "svg", "title", "image"}
    for node in walk(main):
        if node.tag not in allowed | generated:
            errors.append(f"generated document contains unsupported <{node.tag}>")
        if node.tag in ACTIVE_ELEMENTS and not (node.tag == "svg" and node.attrs.get("data-crop-id")):
            errors.append(f"generated document contains active <{node.tag}>")
    pending = list(parsed.root.children)
    while pending:
        item = pending.pop()
        if isinstance(item, CommentNode):
            errors.append("generated document retains a comment")
        elif isinstance(item, Element):
            pending.extend(item.children)
    _, duplicate_ids = collect_ids(parsed.root)
    if duplicate_ids:
        errors.append(f"document has duplicate IDs: {duplicate_ids}")
    errors.extend(resource_errors(parsed, context))
    errors.extend(validate_generated_css(context))
    document = {
        "language": html.attrs.get("lang"),
        "body_language": body.attrs.get("lang") or html.attrs.get("lang"),
        "main_id": main.attrs.get("id"),
        "fragment_ids": observed_fragments,
        "fragment_text": fragment_text,
        "figure_ids": observed_figure_ids,
        "crop_ids": observed_crop_ids,
        "duplicate_ids": duplicate_ids,
    }
    return StaticAudit(errors, binding_errors, document, figures, crops)
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
    }:
        return None
    try:
        converted = url2pathname(parsed.path)
    except (UnicodeError, ValueError):
        return None
    if os.name == "nt" and re.match(r"^/[A-Za-z]:", converted):
        converted = converted[1:]
    return Path(converted).resolve()
DOM_SCRIPT = r"""
() => {
 const norm=v=>(v||"").normalize("NFC").replace(/\s+/gu," ").trim();
 const attrs=e=>Object.fromEntries(e.getAttributeNames().map(n=>[n.toLowerCase(),e.getAttribute(n)]));
 const children=e=>e?[...e.children].map(c=>c.localName):[];
 const root=document.documentElement, body=document.body, mains=[...document.querySelectorAll("main")], main=mains[0]||null;
 const ids=[...document.querySelectorAll("[id]")].map(e=>e.id);
 const fragments=main?[...main.children].map(s=>{const c=s.cloneNode(true);c.querySelectorAll("figure[data-figure-id],template").forEach(n=>n.remove());return {tag:s.localName,attrs:attrs(s),id:s.getAttribute("data-fragment-id"),text:norm(c.textContent),html:s.innerHTML};}):[];
 const figures=[...document.querySelectorAll("figure")].map(f=>{const c=[...f.children].filter(n=>n.localName==="figcaption");return {id:f.getAttribute("data-figure-id"),dom_id:f.id||null,attrs:attrs(f),children:children(f),caption_html:c.length===1?c[0].innerHTML:null};});
 const crops=[...document.querySelectorAll("[data-crop-id]")].map(c=>{const images=[...c.children].filter(n=>n.localName==="image"),titles=[...c.children].filter(n=>n.localName==="title"),i=images[0]||null,r=c.getBoundingClientRect(),h=i&&i.href&&typeof i.href.baseVal==="string"?i.href.baseVal:null;let resolved=null;try{resolved=h?new URL(h,i.baseURI).href:null;}catch{}return {id:c.getAttribute("data-crop-id"),owner:c.closest("figure[data-figure-id]")?.getAttribute("data-figure-id")||null,attrs:attrs(c),children:children(c),title:titles.length===1?norm(titles[0].textContent):null,image_attrs:i?attrs(i):null,image_url:resolved,rect:{left:r.left,right:r.right,width:r.width,height:r.height}};});
 const text_segments=[];if(body){const w=document.createTreeWalker(body,NodeFilter.SHOW_TEXT);while(w.nextNode()){const n=w.currentNode,t=norm(n.data);if(!t)continue;const range=document.createRange();range.selectNodeContents(n);if([...range.getClientRects()].some(r=>r.width>0&&r.height>0))text_segments.push({text:t,leading:/^\s/u.test(n.data),trailing:/\s$/u.test(n.data)});}}
 const overflow=[];for(const e of body?body.querySelectorAll("*"):[]){const crop=e.closest("svg[data-crop-id]");if(crop&&crop!==e)continue;const r=e.getBoundingClientRect();if(r.left<-.5||r.right>root.clientWidth+.5){overflow.push({tag:e.localName,id:e.id||null,left:r.left,right:r.right});if(overflow.length>=25)break;}}
 return {structure:{html:children(root),head:children(document.head),body:children(body),main_count:mains.length,main_id:main?main.id:null},language:{html:root.lang||null,body:body?.lang||root.lang||null,title:document.querySelector("head>title")?.lang||null},title:norm(document.querySelector("head>title")?.textContent),duplicates:[...new Set(ids.filter((v,i)=>ids.indexOf(v)!==i))],fragments,figures,crops,text_segments,geometry:{client:root.clientWidth,scroll:root.scrollWidth,overflow:root.scrollWidth>root.clientWidth+1||overflow.length>0,elements:overflow}};
}
"""

def launch_browser(playwright: Any, browser_path: Path | None) -> Any:
    options: dict[str, Any] = {"headless": True}
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
    return playwright.chromium.launch(**options)
def render_once(
    browser: Any,
    context: Context,
    output: Path,
    evidence_root: Path,
) -> Render:
    blocked: set[str] = set()
    failed: set[str] = set()
    aborted: set[str] = set()
    allowed = {item.file.resolve() for item in context.tracked.values()}
    width_points = PAGE_POINTS[context.manifest["print_geometry"]["page_size"]][0]
    margins = context.manifest["print_geometry"]["margin_in"]
    printable = (width_points / POINTS_PER_INCH - margins["left"] - margins["right"]) * PX_PER_INCH
    probe = max(1, round(printable))
    browser_context = browser.new_context(
        viewport={"width": probe, "height": 960},
        service_workers="block",
        java_script_enabled=False,
    )
    def route_request(route: Any, request: Any) -> None:
        value = request.url
        path = file_url_path(value)
        if value.strip().casefold() == "about:blank" or path in allowed:
            route.continue_()
        else:
            aborted.add(value)
            blocked.add(value)
            route.abort("blockedbyclient")
    browser_context.route("**/*", route_request)
    browser_context.on(
        "requestfailed",
        lambda request: (
            failed.add(request.url) if request.url not in aborted and file_url_path(request.url) is not None else None
        ),
    )
    page = browser_context.new_page()
    try:
        page.goto(context.html.file.as_uri(), wait_until="load", timeout=60_000)
        page.emulate_media(media="print")
        page.evaluate("document.fonts.ready")
        dom = cast("dict[str, Any]", page.evaluate(DOM_SCRIPT))
        output.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(dir=output.parent, prefix=f".{output.name}.")
        os.close(descriptor)
        temporary = Path(temporary_name)
        try:
            page.pdf(path=str(temporary), print_background=True, prefer_css_page_size=True)
            if temporary.stat().st_size == 0:
                raise AuditError("browser render did not produce a PDF")
            temporary.replace(output)
        finally:
            temporary.unlink(missing_ok=True)
    finally:
        browser_context.close()
    observed = dom["geometry"]
    return Render(
        relative_asset(output, evidence_root),
        {
            "blocked_nonlocal": sorted(blocked),
            "failed_local": sorted(failed),
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
        from playwright.sync_api import Error as PlaywrightError
        from playwright.sync_api import sync_playwright
    except ImportError as error:
        raise AuditError("Playwright is unavailable; run with uv run --script") from error
    with sync_playwright() as playwright:
        browser = None
        try:
            browser = launch_browser(playwright, browser_path)
            return (
                render_once(
                    browser,
                    context,
                    render_root / "render-1.pdf",
                    evidence_root,
                ),
                render_once(
                    browser,
                    context,
                    render_root / "render-2.pdf",
                    evidence_root,
                ),
            )
        except PlaywrightError as error:
            raise AuditError(f"Chromium rendering failed: {error}") from error
        finally:
            if browser is not None:
                browser.close()
def normalize_font(value: str) -> str:
    return unicodedata.normalize("NFC", re.sub(r"^[A-Z]{6}\+", "", value)).casefold().strip()
def text_search_key(value: str) -> str:
    value = re.sub(r"(?<=\w)-[ \t]*(?:\r\n|\r|\n)[ \t]*(?=\w)", "", value)
    return " ".join(unicodedata.normalize("NFC", value.replace("\u00ad", "")).split())
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
        "xref": xref,
        "extension": str(raw[1]),
        "resource_name": str(raw[4]),
        "encoding": str(raw[5]),
    }
def pdf_actions(document: Any, fitz: Any) -> dict[str, Any]:
    kinds = {
        fitz.LINK_NONE: "none", fitz.LINK_GOTO: "goto", fitz.LINK_URI: "uri",
        fitz.LINK_LAUNCH: "launch", fitz.LINK_NAMED: "named", fitz.LINK_GOTOR: "goto-remote",
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
            destination_page = link.get("page")
            if (
                raw_kind == fitz.LINK_NAMED
                and type(destination_page) is int
                and 0 <= destination_page < document.page_count
            ):
                kind = "goto"
            else:
                kind = kinds.get(raw_kind, f"kind-{raw_kind}")
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
            subtype = mupdf.pdf_resolve_indirect(mupdf.pdf_dict_gets(item, "S"))
            if subtype is None or not mupdf.pdf_is_name(subtype):
                observe("invalid-action", source_category)
                continue
            name = str(mupdf.pdf_to_name(subtype))
            kind = action_names.get(name)
            if kind is None:
                observe("unknown-action", source_category, subtype=name)
            else:
                observe(kind, source_category)
            pending.append((mupdf.pdf_dict_gets(item, "Next"), True))

    def visit_aa(raw: Any, source_category: str) -> None:
        item = enter(raw)
        if item is not None and mupdf.pdf_is_dict(item):
            for index in range(mupdf.pdf_dict_len(item)):
                visit_action(mupdf.pdf_dict_get_val(item, index), source_category)

    def visit_tree(raw: Any, child_keys: tuple[str, ...], source_category: str) -> None:
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
        visit_action(mupdf.pdf_dict_gets(catalog, "OpenAction"), "catalog")
        visit_aa(mupdf.pdf_dict_gets(catalog, "AA"), "catalog")
        outlines = enter(mupdf.pdf_dict_gets(catalog, "Outlines"))
        if outlines is not None and mupdf.pdf_is_dict(outlines):
            visit_tree(mupdf.pdf_dict_gets(outlines, "First"), ("First", "Next"), "outline")
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
    if not document.is_pdf or document.needs_pass or document.page_count < 1:
        document.close()
        raise PublicationError(f"file is not an inspectable, unencrypted, nonempty PDF: {path}")
    try:
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
        for index in range(document.page_count):
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
            atomic_write(raster_path, png)
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
            "page_count": document.page_count,
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
            "searchable_text": text_search_key("\n".join(raw_texts)),
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

def dom_errors(dom: dict[str, Any], context: Context) -> tuple[list[Any], list[str]]:
    errors: list[Any] = []
    bindings: list[str] = []
    manifest = context.manifest
    if dom.get("structure") != {
        "html": ["head", "body"],
        "head": ["meta", "title", "link"],
        "body": ["main"],
        "main_count": 1,
        "main_id": manifest["publication_id"],
    }:
        errors.append("browser DOM structure is not canonical")
    if dom.get("language") != {
        "html": manifest["document"]["language"],
        "body": manifest["document"]["language"],
        "title": manifest["document"]["title_language"],
    }:
        errors.append("browser DOM language does not match manifest")
    if dom.get("title") != normalize_text(manifest["document"]["title"]):
        errors.append("browser DOM title does not match manifest")
    if dom.get("duplicates"):
        errors.append(f"browser DOM duplicate IDs: {dom['duplicates']}")
    fragments = dom.get("fragments", [])
    if [item.get("id") for item in fragments] != [item["id"] for item in manifest["fragments"]]:
        errors.append("browser fragment order does not match manifest")
    for expected, observed in zip(manifest["fragments"], fragments, strict=False):
        if observed.get("tag") != "section" or observed.get("attrs") != {"data-fragment-id": expected["id"]}:
            errors.append(f"browser fragment {expected['id']} wrapper is invalid")
        if hash_bytes(normalize_text(str(observed.get("text", ""))).encode()) != expected["visible_text_sha256"]:
            errors.append(f"browser fragment {expected['id']} text mismatch")
        try:
            copied = confined(context.root, expected["asset"]["path"]).read_text(encoding="utf-8")
        except (OSError, UnicodeError) as error:
            errors.append(
                text_read_diagnostic(
                    expected["asset"]["path"],
                    "fragment",
                    error,
                    fragment_id=expected["id"],
                )
            )
            continue
        expected_structure = structure_signature(
            parse_html5_fragment(copied, f"fragment {expected['id']}").root.children,
            drop_figures=False,
        )
        observed_structure = structure_signature(
            parse_html5_fragment(str(observed.get("html", "")), f"browser fragment {expected['id']}").root.children,
            drop_figures=True,
        )
        if observed_structure != expected_structure:
            errors.append(f"browser fragment {expected['id']} structure mismatch")

    figures = dom.get("figures", [])
    if [item.get("id") for item in figures] != [item["id"] for item in manifest["figures"]]:
        bindings.append("browser figure order does not match manifest")
    for expected, observed in zip(manifest["figures"], figures, strict=False):
        attrs = observed.get("attrs")
        class_value = attrs.get("class") if isinstance(attrs, dict) else None
        if attrs != {
            "id": expected["dom_id"],
            "class": class_value,
            "data-figure-id": expected["id"],
            "role": "group",
            "aria-label": expected["alt"],
        } or not figure_class_ok(class_value) or observed.get("children") != ["div", "figcaption"]:
            bindings.append(f"browser figure {expected['id']} is not canonical")
        actual_caption = parse_html(str(observed.get("caption_html") or ""))
        if actual_caption.errors or not caption_matches(actual_caption.root, expected["caption_html"]):
            bindings.append(f"browser figure {expected['id']} caption mismatch")

    crops = dom.get("crops", [])
    expected_parts = [(figure, part) for figure in manifest["figures"] for part in figure["parts"]]
    if [item.get("id") for item in crops] != [part["id"] for _, part in expected_parts]:
        bindings.append("browser crop order does not match manifest")
    for (figure, part), observed in zip(expected_parts, crops, strict=False):
        bbox = [float(value) for value in part["bbox"]]
        crop_width, crop_height = bbox[2] - bbox[0], bbox[3] - bbox[1]
        if observed.get("owner") != figure["id"]:
            bindings.append(f"browser crop {part['id']} owner mismatch")
        if not same_numbers(
            parse_numbers(observed.get("attrs", {}).get("viewbox")),
            [bbox[0], bbox[1], crop_width, crop_height],
        ):
            bindings.append(f"browser crop {part['id']} viewBox mismatch")
        if observed.get("children") != ["title", "image"] or observed.get("title") != normalize_text(
            f"{figure['alt']} - part {part['order']}"
        ):
            bindings.append(f"browser crop {part['id']} structure mismatch")
        source = confined(context.root, part["source_svg"]["path"])
        if file_url_path(str(observed.get("image_url") or "")) != source:
            bindings.append(f"browser crop {part['id']} source href mismatch")
        rect = observed.get("rect", {})
        width, height = (
            float(rect.get("width", 0)),
            float(rect.get("height", 0)),
        )
        if (
            crop_width <= 0
            or crop_height <= 0
            or width <= 0
            or height <= 0
            or abs(width / height - crop_width / crop_height) > 0.01
        ):
            bindings.append(f"browser crop {part['id']} geometry mismatch")
    return errors, bindings
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
            "undeclared": [item["base_name"] for item in font_items if not item["declared"]],
            "missing_roles": report.detail["missing_roles"],
        }
        if (
            not font_items
            or font_record["unembedded"]
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

def missing_text_segments(report: Pdf, segments: list[dict[str, Any]]) -> list[str]:
    content = report.detail["searchable_text"]
    cursor = 0
    missing: list[str] = []
    previous_trailing = False
    for segment in segments:
        text = str(segment.get("text", ""))
        needle = text_search_key(text)
        if not needle:
            continue
        index = content.find(needle, cursor)
        needs_space = cursor > 0 and (previous_trailing or bool(segment.get("leading")))
        while index >= 0 and needs_space and not any(character.isspace() for character in content[cursor:index]):
            index = content.find(needle, index + 1)
        if index < 0:
            missing.append(hash_bytes(normalize_text(text).encode()))
        else:
            cursor = index + len(needle)
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
    for category in ("blocked_nonlocal", "failed_local"):
        values = requests[category]
        if values:
            summary[category] = {
                "count": len(values),
                "sha256": hash_bytes(canonical_json(values)),
            }
    return summary
def build_evidence(
    context: Context,
    static: StaticAudit,
    renders: tuple[Render, Render],
    reports: dict[str, Pdf],
    after: TreeSnapshot,
) -> dict[str, Any]:
    first, second = renders
    first_dom, first_bindings = dom_errors(first.dom, context)
    second_dom, second_bindings = dom_errors(second.dom, context)
    request_errors = {
        f"render_{index}": summary
        for index, render in enumerate(renders, 1)
        if (summary := request_failure_summary(render.requests))
    }
    html_errors = [*static.errors, *first_dom, *second_dom]
    geometry_errors, font_errors, behavior_errors = pdf_findings(reports)
    first_segments = cast("list[dict[str, Any]]", first.dom.get("text_segments", []))
    second_segments = cast("list[dict[str, Any]]", second.dom.get("text_segments", []))
    if first_segments != second_segments:
        html_errors.append("browser visible-text segments differ between renders")
    for name, report in reports.items():
        missing = missing_text_segments(report, first_segments)
        if missing:
            behavior_errors.setdefault(name, {})["missing_expected_text_sha256"] = missing
    behavior_observations = {
        name: {
            "actions": report.evidence["actions"],
            "type3_fonts": report.evidence["type3_fonts"],
            "text_characters": report.evidence["text_characters"],
            "replacement_characters": report.evidence["replacement_characters"],
        }
        for name, report in reports.items()
    }
    overflow = {
        f"render_{index}": render.geometry
        for index, render in enumerate(renders, 1)
        if render.geometry["horizontal_overflow"]
        or abs(render.geometry["client_width_css_px"] - render.geometry["probe_width_css_px"]) > 1
    }
    binding_errors = [*static.binding_errors, *first_bindings, *second_bindings]
    figure_records = {item["id"]: dict(item) for item in static.figures}
    crop_records = {item["id"]: dict(item) for item in static.crops}
    for figure in context.manifest["figures"]:
        figure_records.setdefault(
            figure["id"],
            {
                "id": figure["id"],
                "dom_id": figure["dom_id"],
                "matches": 0,
                "caption_matches": False,
            },
        )
        for part in figure["parts"]:
            crop_records.setdefault(
                part["id"],
                {
                    "id": part["id"],
                    "figure_id": figure["id"],
                    "pdf_page": part["pdf_page"],
                    "dom_matches": 0,
                    "source_svg_sha256": part["source_svg"]["sha256"],
                    "bbox_matches": False,
                    "geometry_matches": False,
                },
            )
    figures = [figure_records[item["id"]] for item in context.manifest["figures"]]
    crops = [crop_records[part["id"]] for figure in context.manifest["figures"] for part in figure["parts"]]
    binding_pass = (
        not binding_errors
        and all(item["matches"] == 1 and item["caption_matches"] for item in figures)
        and all(item["dom_matches"] == 1 and item["bbox_matches"] and item["geometry_matches"] for item in crops)
    )
    canonical, render_1, render_2 = (
        reports["canonical"],
        reports["render_1"],
        reports["render_2"],
    )
    repeatability = {
        "raw_render_pdfs_equal": render_1.detail["raw_sha256"] == render_2.detail["raw_sha256"],
        "render_geometry_equal": pdf_signature(render_1, "geometry") == pdf_signature(render_2, "geometry"),
        "render_text_equal": pdf_signature(render_1, "text_sha256") == pdf_signature(render_2, "text_sha256"),
        "render_rasters_equal": pdf_signature(render_1, "raster_sha256") == pdf_signature(render_2, "raster_sha256"),
        "canonical_geometry_matches": pdf_signature(canonical, "geometry")
        == pdf_signature(render_1, "geometry")
        == pdf_signature(render_2, "geometry"),
        "canonical_text_matches": pdf_signature(canonical, "text_sha256")
        == pdf_signature(render_1, "text_sha256")
        == pdf_signature(render_2, "text_sha256"),
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
    rasters = [item for report in reports.values() for item in report.rasters]
    expected_rasters = sum(report.evidence["page_count"] for report in reports.values())
    raster_keys = {(item["source"], item["page"]) for item in rasters}
    raster_pass = len(rasters) == expected_rasters == len(raster_keys)
    tree_same = unchanged(context.before, after)
    checks: list[dict[str, Any]] = []
    add_check(
        checks,
        "manifest.integrity",
        not context.errors,
        "Assembly manifest, profile identity, declared regular-file tree, hashes, path confinement, and initial fingerprint match.",
        {"findings": context.errors},
    )
    add_check(
        checks,
        "html.offline-profile",
        not html_errors and not request_errors,
        "Copied fragments and retained stylesheets satisfy the shared positive profile; generated HTML/CSS is closed, passive, local, and manifest-bound.",
        {
            "findings": html_errors,
            "request_findings": request_errors,
            "static_document": static.document,
        },
    )
    add_check(
        checks,
        "render.geometry-overflow",
        not geometry_errors and not overflow,
        "Both render DOMs fit the printable width, and all MediaBox/CropBox geometry matches the declared page size.",
        {
            "overflow": overflow,
            "findings": geometry_errors,
            "pdf_pages": {name: report.detail["pages"] for name, report in reports.items()},
        },
    )
    add_check(
        checks,
        "pdf.fonts",
        not font_errors,
        "Canonical and rendered PDFs use embedded manifest-declared fonts and include the required role families.",
        {
            "findings": font_errors,
            "fonts": {name: report.detail["fonts"] for name, report in reports.items()},
        },
    )
    add_check(
        checks,
        "pdf.actions-type3-text",
        not behavior_errors,
        "Canonical and rendered PDFs have no detected unsafe action witnesses or Type 3 fonts and retain extractable text without replacement characters.",
        {"findings": behavior_errors, "observations": behavior_observations},
    )
    add_check(
        checks,
        "figures.crop-bindings",
        binding_pass,
        "Every manifest figure and crop binds exactly once to its caption, copied source SVG, source box, and browser geometry.",
        {"findings": binding_errors, "figures": figures, "crops": crops},
    )
    add_check(
        checks,
        "render.repeatability",
        repeatability_pass,
        "Independent renders agree in geometry, normalized text, and rasters; canonical geometry and text match both.",
        repeatability,
    )
    add_check(
        checks,
        "rasters.complete",
        raster_pass,
        "Every page of the canonical PDF and both renders has one full-page raster.",
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
    status = "pass" if all(check["passed"] for check in checks) else "fail"
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
        raise AuditError("generated QA evidence violates schema: " + "; ".join(findings))
    return evidence

def beneath(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
    except ValueError:
        return False
    return True
def is_reparse(path: Path) -> bool:
    try:
        info = path.lstat()
    except FileNotFoundError:
        return False
    return stat.S_ISLNK(info.st_mode) or bool(
        getattr(info, "st_file_attributes", 0) & REPARSE_POINT_ATTRIBUTE
    )
def strictly_beneath(path: Path, root: Path) -> bool:
    try:
        relative = path.relative_to(root)
    except ValueError:
        return False
    return bool(relative.parts)
def windows_alias_key(path: Path) -> tuple[str, ...]:
    return tuple(part.rstrip(" .").casefold() for part in path.parts)
def same_filesystem_object(left: Path, right: Path) -> bool:
    try:
        return left.samefile(right)
    except OSError as error:
        raise AuditError(f"cannot compare QA output filesystem identity: {error}") from error
def nearest_existing_ancestor(path: Path) -> Path:
    candidate = path
    while True:
        try:
            candidate.lstat()
        except FileNotFoundError:
            parent = candidate.parent
            if parent == candidate:
                raise AuditError(
                    f"QA output path has no existing filesystem ancestor: {path}"
                ) from None
            candidate = parent
        except OSError as error:
            raise AuditError(f"cannot inspect QA output filesystem identity: {error}") from error
        else:
            return candidate
def physically_contains(container: Path, candidate: Path) -> bool:
    try:
        container.lstat()
    except FileNotFoundError:
        return False
    except OSError as error:
        raise AuditError(f"cannot inspect QA output filesystem identity: {error}") from error
    existing = nearest_existing_ancestor(candidate)
    try:
        resolved = existing.resolve(strict=True)
    except OSError as error:
        raise AuditError(f"cannot resolve QA output filesystem identity: {error}") from error
    return any(
        same_filesystem_object(container, ancestor)
        for ancestor in (resolved, *resolved.parents)
    )
def physically_overlaps(left: Path, right: Path) -> bool:
    return physically_contains(left, right) or physically_contains(right, left)
def aliases_existing_ancestor(path: Path, other: Path, root: Path) -> bool:
    for ancestor in other.parents:
        if same_filesystem_object(path, ancestor):
            return True
        if same_filesystem_object(root, ancestor):
            break
    return False
def validate_candidate_layout(layout: ReviewLayout) -> None:
    try:
        root = layout.root.resolve(strict=True)
    except OSError as error:
        raise AuditError(f"cannot resolve QA candidate root: {error}") from error
    outputs = (
        ("QA evidence", layout.evidence),
        ("release manifest", layout.release),
        ("raster directory", layout.rasters),
        ("render directory", layout.renders),
    )
    existing: list[tuple[str, Path, Path]] = []
    for label, path in outputs:
        if not (path.exists() or path.is_symlink()):
            continue
        try:
            resolved = path.resolve(strict=True)
        except OSError as error:
            raise AuditError(f"cannot resolve {label}: {error}") from error
        if not strictly_beneath(resolved, root):
            raise AuditError(f"{label} resolves outside the QA candidate root")
        existing.append((label, path, resolved))
    for index, (left_label, left, left_resolved) in enumerate(existing):
        for right_label, right, right_resolved in existing[index + 1 :]:
            if same_filesystem_object(left, right):
                raise AuditError(
                    f"{left_label} and {right_label} alias the same filesystem object"
                )
            if beneath(left_resolved, right_resolved) or beneath(
                right_resolved,
                left_resolved,
            ) or aliases_existing_ancestor(
                left,
                right,
                root,
            ) or aliases_existing_ancestor(
                right,
                left,
                root,
            ):
                raise AuditError(
                    f"{left_label} and {right_label} overlap after resolving filesystem aliases"
                )
def validate_publication_disjoint(publication: Path, layout: ReviewLayout) -> None:
    try:
        publication = publication.resolve(strict=True)
    except OSError as error:
        raise AuditError(f"cannot resolve publication root for QA output safety: {error}") from error
    outputs = (
        (layout.root, "review root must be disjoint from the publication tree"),
        (layout.evidence, "QA evidence must be disjoint from the publication tree"),
        (layout.release, "release manifest must be disjoint from the publication tree"),
        (layout.rasters, "rasters must be disjoint from the publication tree"),
        (layout.renders, "independent renders must be disjoint from the publication tree"),
    )
    for path, message in outputs:
        if physically_overlaps(publication, path):
            raise AuditError(message)
def validate_review_root(layout: ReviewLayout) -> bool:
    try:
        root_info = layout.root.lstat()
    except FileNotFoundError:
        return False
    root_reparse = (
        stat.S_ISLNK(root_info.st_mode)
        or bool(getattr(root_info, "st_file_attributes", 0) & REPARSE_POINT_ATTRIBUTE)
    )
    if root_reparse or not stat.S_ISDIR(root_info.st_mode):
        raise AuditError(f"review root exists and is not a regular directory: {layout.root}")
    if next(layout.root.iterdir(), None) is None:
        return True
    try:
        marker_info = layout.evidence.lstat()
        marker = layout.evidence.resolve(strict=True)
    except OSError as error:
        raise AuditError(
            f"non-empty review root requires a QA evidence ownership marker at {layout.evidence}: {error}"
        ) from error
    marker_reparse = (
        stat.S_ISLNK(marker_info.st_mode)
        or bool(getattr(marker_info, "st_file_attributes", 0) & REPARSE_POINT_ATTRIBUTE)
    )
    if (
        marker_reparse
        or not stat.S_ISREG(marker_info.st_mode)
        or not strictly_beneath(marker, layout.root)
    ):
        raise AuditError(
            f"non-empty review root requires a regular non-symlink QA evidence ownership marker: "
            f"{layout.evidence}"
        )
    return True
def prepare_outputs(
    evidence: Path,
    release: Path,
    rasters: Path,
    publication: Path,
) -> ReviewLayout:
    evidence, release, rasters, publication = (
        Path(os.path.abspath(path))  # noqa: PTH100 - validate requested layout without following stale outputs.
        for path in (evidence, release, rasters, publication)
    )
    review_root = release.parent
    renders = evidence.parent / RENDER_DIRECTORY
    if evidence == release:
        raise AuditError("evidence and release manifests must be distinct")
    try:
        evidence_relative = evidence.relative_to(review_root)
    except ValueError as error:
        raise AuditError("evidence must be beneath the release-manifest directory") from error
    if not evidence_relative.parts:
        raise AuditError("evidence must be beneath the release-manifest directory")
    try:
        rasters.relative_to(evidence.parent)
    except ValueError as error:
        raise AuditError("rasters must be beneath the evidence directory") from error
    relative_outputs = (
        (evidence, "QA evidence"), (release, "release manifest"),
        (rasters, "rasters"), (renders, "independent renders"),
    )
    if os.name == "nt":
        aliases: dict[tuple[str, ...], str] = {}
        for path, label in relative_outputs:
            key = windows_alias_key(path)
            if key != tuple(part.casefold() for part in path.parts):
                raise AuditError(
                    f"{label} must not contain Windows path components ending in a dot or space"
                )
            if key in aliases:
                raise AuditError(
                    f"{label} aliases {aliases[key]} under Windows path semantics"
                )
            aliases[key] = label
    for path, label in relative_outputs:
        if not strictly_beneath(path, review_root):
            raise AuditError(f"{label} must be beneath the review root")
    if is_reparse(review_root):
        raise AuditError(f"review root is a symlink or reparse point: {review_root}")
    resolved_root = review_root.resolve(strict=False)
    def remap(path: Path) -> Path:
        return resolved_root.joinpath(*path.relative_to(review_root).parts)
    layout = ReviewLayout(
        resolved_root,
        remap(evidence),
        remap(release),
        remap(rasters),
        remap(renders),
    )
    outputs = (
        (layout.evidence, "QA evidence"), (layout.release, "release manifest"),
        (layout.rasters, "rasters"), (layout.renders, "independent renders"),
    )
    validate_publication_disjoint(publication, layout)
    paths = [path for path, _label in outputs]
    for index, left in enumerate(paths):
        for right in paths[index + 1 :]:
            if beneath(left, right) or beneath(right, left):
                raise AuditError("QA output paths must not overlap")
    validate_review_root(layout)
    return layout
def make_stage(layout: ReviewLayout) -> Path:
    layout.root.parent.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix=f".{layout.root.name}.staging-", dir=layout.root.parent))
def stage_layout(layout: ReviewLayout, stage: Path) -> ReviewLayout:
    def remap(path: Path) -> Path:
        return stage.joinpath(*path.relative_to(layout.root).parts)
    return ReviewLayout(
        stage,
        remap(layout.evidence),
        remap(layout.release),
        remap(layout.rasters),
        remap(layout.renders),
    )
def initialize_candidate(layout: ReviewLayout) -> None:
    layout.evidence.parent.mkdir(parents=True, exist_ok=True)
    layout.rasters.mkdir(parents=True, exist_ok=True)
    layout.renders.mkdir(parents=True, exist_ok=True)
    validate_candidate_layout(layout)
def publish_review(stage: Path, layout: ReviewLayout, publication: Path) -> None:
    had_root = validate_review_root(layout)
    validate_publication_disjoint(publication, layout)
    backup: Path | None = None
    try:
        if had_root:
            backup = layout.root.with_name(f".{layout.root.name}.backup-{uuid.uuid4().hex}")
            layout.root.replace(backup)
        stage.replace(layout.root)
    except (OSError, KeyboardInterrupt) as error:
        try:
            if backup is not None and backup.exists():
                if layout.root.exists():
                    shutil.rmtree(layout.root)
                backup.replace(layout.root)
            elif not had_root and layout.root.exists():
                shutil.rmtree(layout.root)
        except OSError as rollback_error:
            raise AuditError(
                f"cannot restore review output after {error}; backup remains at {backup}: {rollback_error}"
            ) from rollback_error
        if isinstance(error, KeyboardInterrupt):
            raise
        raise AuditError(f"cannot publish review directory: {error}") from error
    finally:
        if stage.exists():
            shutil.rmtree(stage)
    if backup is not None:
        try:
            shutil.rmtree(backup)
        except OSError as error:
            eprint(f"warning: review committed but backup remains at {backup}: {error}")

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
    renders = render_pair(context, layout.renders, layout.evidence.parent, args.browser)
    static = audit_html(context)
    page_size = context.manifest["print_geometry"]["page_size"]
    canonical = inspect_pdf(
        context.pdf.file, context.pdf.path, page_size, layout.rasters / "canonical",
        "canonical", layout.evidence.parent, context.manifest,
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
    evidence = build_evidence(context, static, renders, reports, snapshot(context.root))
    write_json(layout.evidence, evidence)
    validate_candidate_layout(layout)
    if evidence["mechanical_status"] != "pass":
        return 1
    write_json(layout.release, release_manifest(context, layout.evidence, layout.release))
    validate_candidate_layout(layout)
    return 0
def audit(args: argparse.Namespace) -> int:
    if not args.render_twice:
        raise AuditError("--render-twice is required")
    publication_root = args.assembly_manifest.resolve().parent
    final_layout = prepare_outputs(
        args.evidence, args.release_manifest, args.rasters, publication_root
    )
    context = load_context(args.assembly_manifest, args.html, args.page_size)
    stage = make_stage(final_layout)
    try:
        result = audit_candidate(args, context, stage_layout(final_layout, stage))
        publish_review(stage, final_layout, context.root)
    finally:
        if stage.exists():
            shutil.rmtree(stage)
    if result != 0:
        eprint("QA completed with blocking findings")
        return result
    print(
        json.dumps(
            {
                "publication_id": context.manifest["publication_id"],
                "mechanical_status": "pass",
                "human_review": "required",
                "evidence": str(final_layout.evidence),
                "release_manifest": str(final_layout.release),
            },
            ensure_ascii=False,
        )
    )
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
