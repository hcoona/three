from __future__ import annotations

import contextlib
import copy
import ctypes
import hashlib
import importlib
import importlib.util
import io
import json
import os
import shutil
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import TYPE_CHECKING, Any
from urllib.parse import urlsplit
from xml.etree.ElementTree import Comment, Element

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Sequence
    from types import ModuleType


@dataclass(frozen=True)
class MainResult:
    exit_code: int
    report: dict[str, Any]
    stdout: str
    stderr: str


@dataclass(frozen=True)
class PdfPage:
    text: str = ""
    hidden_text: str = ""
    raster_image: bool = False
    vector_marks: bool = True
    rotation: int = 0


def import_by_path(name: str, path: Path) -> ModuleType:
    """Import a standalone script and register it before execution."""
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        message = f"cannot load module {name!r} from {path}"
        raise ImportError(message)
    module = importlib.util.module_from_spec(specification)
    previous = sys.modules.get(name)
    sys.modules[name] = module
    try:
        specification.loader.exec_module(module)
    except BaseException:
        if previous is None:
            sys.modules.pop(name, None)
        else:
            sys.modules[name] = previous
        raise
    return module


def invoke_main(
    module: ModuleType,
    arguments: Sequence[str],
    *,
    require_report: bool = True,
) -> MainResult:
    """Invoke a PEP 723 script main and capture its final JSON report."""
    stdout = io.StringIO()
    stderr = io.StringIO()
    with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
        exit_code = module.main(list(arguments))
    records: list[dict[str, Any]] = []
    for line in stdout.getvalue().splitlines():
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict):
            records.append(value)
    if require_report and not records:
        message = f"main emitted no JSON object: stdout={stdout.getvalue()!r}"
        raise AssertionError(message)
    return MainResult(
        exit_code=exit_code,
        report=records[-1] if records else {},
        stdout=stdout.getvalue(),
        stderr=stderr.getvalue(),
    )


def canonical_json(value: Any) -> bytes:
    """Serialize a value to stable UTF-8 JSON bytes."""
    return (
        json.dumps(
            value,
            allow_nan=False,
            ensure_ascii=False,
            indent=2,
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")


def write_json(path: Path, value: Any) -> None:
    """Write canonical JSON bytes, creating parent directories."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json(value))


def read_json(path: Path) -> Any:
    """Read a UTF-8 JSON value."""
    return json.loads(path.read_text(encoding="utf-8"))


def apply_profile_mutation(
    profile: dict[str, Any],
    case: dict[str, Any],
) -> dict[str, Any]:
    """Apply one declarative profile-shape mutation from the shared corpus."""
    mutated = copy.deepcopy(profile)
    path = case["path"]
    target: Any = mutated
    for part in path[:-1]:
        target = target[part]
    final = path[-1]
    operation = case["operation"]
    if operation == "set":
        target[final] = copy.deepcopy(case["value"])
    elif operation == "delete":
        del target[final]
    elif operation == "append":
        target[final].append(case["value"])
    elif operation == "remove":
        target[final].remove(case["value"])
    else:
        message = f"unsupported profile mutation operation: {operation!r}"
        raise ValueError(message)
    return mutated


def fixed_profile_ceiling(module: Any) -> dict[str, Any]:
    """Normalize one runtime's fixed profile ceiling for comparison."""
    return {
        "element_attributes": {
            tag: sorted(attributes)
            for tag, attributes in sorted(
                module.FIXED_ELEMENT_ATTRIBUTES.items()
            )
        },
        "global_attributes": sorted(module.FIXED_GLOBAL_ATTRIBUTES),
        "css_properties": sorted(module.FIXED_CSS_PROPERTIES),
    }


def resolve_stable_asset(
    record: dict[str, Any],
    roots: dict[str, Path],
) -> Path:
    """Resolve and verify one stable asset binding against named roots."""
    if set(record) != {"path_base", "path", "sha256", "bytes"}:
        message = f"unexpected stable asset fields: {sorted(record)}"
        raise ValueError(message)
    path_base = record["path_base"]
    if not isinstance(path_base, str) or path_base not in roots:
        message = f"unknown path_base: {path_base!r}"
        raise ValueError(message)
    value = record["path"]
    if not isinstance(value, str):
        message = "stable asset path must be a string"
        raise TypeError(message)
    logical = PurePosixPath(value)
    if (
        not value
        or logical.as_posix() != value
        or "\\" in value
        or logical.is_absolute()
        or PureWindowsPath(value).is_absolute()
        or any(part in {"", ".", ".."} for part in logical.parts)
        or urlsplit(value).scheme
        or value.startswith("//")
    ):
        message = f"unconfined stable asset path: {value!r}"
        raise ValueError(message)
    root = roots[path_base].resolve()
    target = root.joinpath(*logical.parts).resolve()
    try:
        target.relative_to(root)
    except ValueError as error:
        message = f"stable asset path escapes {path_base}: {value!r}"
        raise ValueError(message) from error
    if not target.is_file():
        message = f"stable asset target is not a file: {target}"
        raise ValueError(message)
    content = target.read_bytes()
    if len(content) != record["bytes"]:
        message = f"stable asset byte length mismatch: {target}"
        raise ValueError(message)
    if sha256_bytes(content) != record["sha256"]:
        message = f"stable asset hash mismatch: {target}"
        raise ValueError(message)
    return target


def sha256_bytes(content: bytes) -> str:
    """Return a lowercase SHA-256 digest."""
    return hashlib.sha256(content).hexdigest()


def visible_text_sha256(fragment_html: str) -> str:
    """Hash normalized visible text independently from a runtime under test."""
    html5lib = importlib.import_module("html5lib")
    parser = html5lib.HTMLParser(
        tree=html5lib.getTreeBuilder("etree"),
        strict=False,
        namespaceHTMLElements=False,
    )
    root = parser.parseFragment(fragment_html, container="div")
    if parser.errors:
        message = f"fixture fragment has HTML parse errors: {parser.errors!r}"
        raise ValueError(message)
    parts: list[str] = []

    def collect(parent: Element) -> None:
        if parent.text is not None:
            parts.append(parent.text)
        for child in list(parent):
            if child.tag is not Comment and isinstance(child.tag, str):
                collect(child)
            if child.tail is not None:
                parts.append(child.tail)

    collect(root)
    normalized = " ".join(unicodedata.normalize("NFC", "".join(parts)).split())
    return sha256_bytes(normalized.encode("utf-8"))


def asset_record(root: Path, path: Path) -> dict[str, Any]:
    """Build a canonical package-relative asset binding."""
    content = path.read_bytes()
    return {
        "path": path.relative_to(root).as_posix(),
        "sha256": sha256_bytes(content),
        "bytes": len(content),
    }


def tree_snapshot(root: Path) -> dict[str, bytes]:
    """Capture every regular file below a tree by relative path."""
    return {
        path.relative_to(root).as_posix(): path.read_bytes()
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def copy_tree(source: Path, destination: Path) -> None:
    """Copy a complete fixture tree to a new location."""
    shutil.copytree(source, destination)


if sys.platform == "win32":

    def windows_short_path(path: Path) -> Path | None:
        """Return the Win32 short path when the volume exposes one."""
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        get_short_path = kernel32.GetShortPathNameW
        get_short_path.argtypes = [
            ctypes.c_wchar_p,
            ctypes.c_wchar_p,
            ctypes.c_uint32,
        ]
        get_short_path.restype = ctypes.c_uint32
        buffer = ctypes.create_unicode_buffer(32768)
        length = get_short_path(str(path), buffer, len(buffer))
        if length == 0 or length >= len(buffer):
            return None
        return Path(buffer.value)

else:

    def windows_short_path(_path: Path) -> Path | None:
        """Return no alias when Win32 short paths are unavailable."""
        return None


def _fitz() -> ModuleType:
    return importlib.import_module("fitz")


def write_pdf(path: Path, pages: Iterable[PdfPage]) -> None:
    """Write a deterministic local PDF from explicit page specifications."""
    fitz = _fitz()
    page_specs = tuple(pages)
    if not page_specs:
        message = "at least one PDF page is required"
        raise ValueError(message)
    document = fitz.open()
    try:
        for number, spec in enumerate(page_specs, start=1):
            page = document.new_page(width=360, height=480)
            if spec.text:
                font_name = (
                    "china-s"
                    if any(ord(character) > 127 for character in spec.text)
                    else "helv"
                )
                page.insert_textbox(
                    fitz.Rect(36, 36, 324, 170),
                    spec.text,
                    fontsize=12,
                    fontname=font_name,
                )
            if spec.hidden_text:
                page.insert_text(
                    fitz.Point(36, 190),
                    spec.hidden_text,
                    fontsize=10,
                    fontname="helv",
                    render_mode=3,
                )
            if spec.vector_marks:
                page.draw_rect(
                    fitz.Rect(54, 220, 306, 340),
                    color=(0, 0, 0),
                    width=1,
                )
                page.draw_line(
                    fitz.Point(72, 260 + number),
                    fitz.Point(288, 300 + number),
                    color=(0, 0, 0),
                    width=0.8,
                )
            if spec.raster_image:
                pixmap = fitz.Pixmap(fitz.csRGB, fitz.IRect(0, 0, 2, 2))
                pixmap.clear_with(0x336699)
                page.insert_image(
                    fitz.Rect(240, 360, 320, 440),
                    pixmap=pixmap,
                )
            if spec.rotation:
                page.set_rotation(spec.rotation)
        document.set_metadata(
            {
                "producer": "scholarly-publication test fixture",
                "creationDate": "D:20000101000000Z",
                "modDate": "D:20000101000000Z",
            }
        )
        path.parent.mkdir(parents=True, exist_ok=True)
        document.save(
            path,
            garbage=4,
            deflate=True,
            no_new_id=True,
        )
    finally:
        document.close()


def encrypt_pdf(path: Path) -> None:
    """Replace a fixture PDF with an empty-user-password encrypted variant."""
    fitz = _fitz()
    encrypted = path.with_name(f"{path.stem}.encrypted.pdf")
    with fitz.open(path) as document:
        document.save(
            encrypted,
            encryption=5,
            owner_pw="fixture-owner-password",
            user_pw="",
            garbage=4,
            deflate=True,
            no_new_id=True,
        )
    encrypted.replace(path)


def add_pdf_attachment(path: Path) -> None:
    """Add a deterministic embedded-file attachment."""
    fitz = _fitz()
    with fitz.open(path) as document:
        document.embfile_add(
            "fixture.txt",
            b"Deterministic scholarly publication attachment fixture.\n",
            filename="fixture.txt",
        )
        document.saveIncr()


def add_pdf_javascript(path: Path, form: str) -> None:
    """Add one representative JavaScript action structure."""
    fitz = _fitz()
    with fitz.open(path) as document:
        catalog = document.pdf_catalog()
        if form == "open-action":
            action_xref = document.get_new_xref()
            document.update_object(
                action_xref,
                "<< /Type /Action /S /JavaScript /JS (app.alert\\(1\\)) >>",
            )
            document.xref_set_key(catalog, "OpenAction", f"{action_xref} 0 R")
        elif form == "name-tree":
            tree_xref = document.get_new_xref()
            document.update_object(
                tree_xref,
                (
                    "<< /Names [(fixture) "
                    "<< /Type /Action /S /JavaScript "
                    "/JS (app.alert\\(2\\)) >>] >>"
                ),
            )
            names_xref = document.get_new_xref()
            document.update_object(
                names_xref,
                f"<< /JavaScript {tree_xref} 0 R >>",
            )
            document.xref_set_key(catalog, "Names", f"{names_xref} 0 R")
        elif form == "additional-action":
            document.xref_set_key(
                catalog,
                "AA",
                (
                    "<< /WC << /Type /Action /S /JavaScript "
                    "/JS (app.alert\\(3\\)) >> >>"
                ),
            )
        else:
            message = f"unsupported JavaScript fixture form: {form}"
            raise ValueError(message)
        document.saveIncr()


def remove_pdf_font_programs(path: Path) -> None:
    """Remove embedded font streams while preserving the PDF font resources."""
    fitz = _fitz()
    removed = False
    with fitz.open(path) as document:
        for xref in range(1, document.xref_length()):
            for key in ("FontFile", "FontFile2", "FontFile3"):
                kind, _value = document.xref_get_key(xref, key)
                if kind != "null":
                    document.xref_set_key(xref, key, "null")
                    removed = True
        if not removed:
            message = "fixture PDF has no embedded font program"
            raise AssertionError(message)
        document.saveIncr()


def add_pdf_type3_font(path: Path) -> None:
    """Attach one valid unused Type 3 font resource to the first PDF page."""
    fitz = _fitz()
    with fitz.open(path) as document:
        page = document.load_page(0)
        character = document.get_new_xref()
        document.update_object(character, "<< /Length 0 >>")
        document.update_stream(
            character,
            b"0 0 1000 1000 d1\n100 100 800 800 re f\n",
        )
        font = document.get_new_xref()
        document.update_object(
            font,
            (
                "<< /Type /Font /Subtype /Type3 /Name /FType3 "
                "/FontBBox [0 0 1000 1000] "
                "/FontMatrix [0.001 0 0 0.001 0 0] "
                f"/CharProcs << /A {character} 0 R >> "
                "/Encoding << /Type /Encoding /Differences [65 /A] >> "
                "/FirstChar 65 /LastChar 65 /Widths [1000] "
                "/Resources << >> >>"
            ),
        )
        document.xref_set_key(
            page.xref,
            "Resources/Font/FType3",
            f"{font} 0 R",
        )
        document.saveIncr()


def clear_pdf_page_contents(path: Path) -> None:
    """Remove painted content while retaining PDF resources and geometry."""
    fitz = _fitz()
    with fitz.open(path) as document:
        for page in document:
            for xref in page.get_contents():
                document.update_stream(xref, b"q\nQ\n")
        document.saveIncr()


def scale_pdf_user_unit(path: Path, scale: float = 1.1) -> None:
    """Change effective page geometry without rewriting page content."""
    fitz = _fitz()
    with fitz.open(path) as document:
        document.xref_set_key(
            document.load_page(0).xref,
            "UserUnit",
            str(scale),
        )
        document.saveIncr()


def add_pdf_vector_mark(path: Path) -> None:
    """Add a visible vector mark without changing page text or geometry."""
    fitz = _fitz()
    with fitz.open(path) as document:
        page = document.load_page(0)
        page.draw_rect(
            fitz.Rect(8, 8, 20, 20),
            color=(1, 0, 0),
            fill=(1, 0, 0),
        )
        document.saveIncr()


def write_test_font(
    path: Path,
    *,
    characters: str = " A",
    family: str = "ScholarlyFixture",
    style: str = "Regular",
) -> None:
    """Build a deterministic TrueType fixture covering selected characters."""
    font_builder = importlib.import_module("fontTools.fontBuilder")
    glyph_pen = importlib.import_module("fontTools.pens.ttGlyphPen")
    builder = font_builder.FontBuilder(1000, isTTF=True)
    codepoints = sorted(
        {
            ord(character)
            for character in characters
            if character == " " or not character.isspace()
        }
    )
    if 32 not in codepoints:
        codepoints.insert(0, 32)
    glyph_names = {
        codepoint: (
            "space"
            if codepoint == 32
            else (
                f"uni{codepoint:04X}"
                if codepoint <= 0xFFFF
                else f"u{codepoint:05X}"
            )
        )
        for codepoint in codepoints
    }
    glyph_order = [".notdef", *glyph_names.values()]
    builder.setupGlyphOrder(glyph_order)
    glyphs: dict[str, Any] = {}
    for name in glyph_order:
        pen = glyph_pen.TTGlyphPen(None)
        if name not in {".notdef", "space"}:
            pen.moveTo((100, 0))
            pen.lineTo((500, 800))
            pen.lineTo((900, 0))
            pen.closePath()
        glyphs[name] = pen.glyph()
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(
        {name: (1000 if name != "space" else 500, 0) for name in glyph_order}
    )
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupCharacterMap(glyph_names)
    compact_family = "".join(
        character for character in family if character.isalnum()
    )
    compact_style = "".join(
        character for character in style if character.isalnum()
    )
    postscript_name = (
        f"{compact_family or 'Fixture'}-{compact_style or 'Regular'}"
    )
    builder.setupNameTable(
        {
            "familyName": family,
            "styleName": style,
            "uniqueFontIdentifier": f"{family} {style}",
            "fullName": f"{family} {style}",
            "psName": postscript_name,
        }
    )
    builder.setupOS2(
        sTypoAscender=800,
        sTypoDescender=-200,
        usWinAscent=800,
        usWinDescent=200,
    )
    builder.setupPost()
    builder.setupMaxp()
    path.parent.mkdir(parents=True, exist_ok=True)
    builder.save(path)


def build_rendered_publication(  # noqa: PLR0913, PLR0915
    root: Path,
    reconstruct_module: ModuleType,
    assemble_module: ModuleType,
    *,
    browser: Path | None,
    fragment_html: str | None = None,
    figure_bbox: tuple[float, float, float, float] | None = None,
    figure_part_id: str = "integration-figure-part-1",
    render_override: Callable[[Path, Path, Path, set[Path]], None]
    | None = None,
) -> Path:
    """Build and render an externally authored publication pipeline."""
    root.mkdir(parents=True, exist_ok=True)
    source_pdf = root / "source.pdf"
    source_text = (
        "This deterministic source page contains enough extractable text for "
        "a complete reconstruction, assembly, render, and QA integration."
    )
    write_pdf(source_pdf, [PdfPage(text=source_text)])

    figure_map = root / "figure-map.json"
    write_json(
        figure_map,
        {
            "schema_version": "1.0",
            "coordinate_space": "pdf-points-top-left",
            "figures": [
                {
                    "id": "integration-figure",
                    "source_label": "Figure 1",
                    "profile": "music-notation",
                    "embedded_language_inventory": ["en", "zh-Hans"],
                    "parts": [
                        {
                            "id": figure_part_id,
                            "order": 1,
                            "pdf_page": 1,
                            "bbox": list(
                                figure_bbox
                                if figure_bbox is not None
                                else (54, 220, 306, 340)
                            ),
                        }
                    ],
                }
            ],
        },
    )
    source_root = root / "source"
    reconstruction = invoke_main(
        reconstruct_module,
        [
            "extract",
            "--pdf",
            str(source_pdf),
            "--output",
            str(source_root),
            "--pages",
            "1",
            "--figure-map",
            str(figure_map),
            "--profile",
            "music-notation",
            "--rights-note",
            "Authorized deterministic integration fixture.",
        ],
    )
    if reconstruction.exit_code != 0:
        message = f"reconstruction failed: {reconstruction}"
        raise AssertionError(message)

    source_package = source_root / "source-package.json"
    source_manifest = read_json(source_package)
    blocks_path = (
        source_root / source_manifest["pages"][0]["assets"]["blocks"]["path"]
    )
    source_block_id = read_json(blocks_path)["blocks"][0]["id"]

    fragment = fragment_html or (
        '<h1 id="opening">Integrated publication</h1>\n'
        "<p>Deterministic scholarly text for rendering and audit.</p>\n"
        "<!-- figure: integration-figure -->\n"
    )
    fragment_path = root / "fragments" / "section-one.html"
    fragment_path.parent.mkdir(parents=True, exist_ok=True)
    fragment_path.write_text(fragment, encoding="utf-8")
    translation_bundle = root / "translation-bundle.json"
    write_json(
        translation_bundle,
        {
            "schema_version": "1.0",
            "source_package": asset_record(root, source_package),
            "target_language": "en",
            "fragment_text_normalization": (
                "approved-fragment-visible-text-v1"
            ),
            "approval": {
                "status": "approved",
                "reference": "external-review-integration",
            },
            "fragments": [
                {
                    "id": "section-one",
                    **asset_record(root, fragment_path),
                    "visible_text_sha256": visible_text_sha256(fragment),
                    "source_block_ids": [source_block_id],
                }
            ],
        },
    )

    font_path = root / "fonts" / "fixture.ttf"
    write_test_font(
        font_path,
        characters=(
            " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
            "0123456789.,;:!?-()/'"
        ),
        family="Fixture Serif",
    )
    stylesheet = root / "styles" / "publication.css"
    stylesheet.parent.mkdir(parents=True, exist_ok=True)
    stylesheet.write_text(
        "h1 { font-weight: 400; }\np { color: #111; line-height: 1.4; }\n",
        encoding="utf-8",
    )
    assembly_spec = root / "assembly-spec.json"
    write_json(
        assembly_spec,
        {
            "schema_version": "1.0",
            "publication_id": "integration-publication",
            "title": "Integrated Publication",
            "title_language": "en",
            "language": "en",
            "source_package": "source/source-package.json",
            "translation_bundle": "translation-bundle.json",
            "fragment_order": ["section-one"],
            "figures": [
                {
                    "id": "integration-figure",
                    "caption_html": (
                        '<span class="figure-label">Figure 1.</span> '
                        "Deterministic vector figure."
                    ),
                    "alt": "Deterministic vector figure",
                    "class": "keep-together",
                }
            ],
            "font_roles": {
                "body-cjk": "Fixture Serif",
                "body-latin": "Fixture Serif",
            },
            "fonts": [
                {
                    "family": "Fixture Serif",
                    "path": "fonts/fixture.ttf",
                    "style": "normal",
                    "weight": 400,
                }
            ],
            "stylesheets": ["styles/publication.css"],
            "page": {
                "size": "letter",
                "margin_in": {
                    "top": 0.75,
                    "right": 0.75,
                    "bottom": 0.75,
                    "left": 0.75,
                },
            },
            "profiles": ["music-notation"],
        },
    )

    publication = root / "publication"
    assembly = invoke_main(
        assemble_module,
        [
            "build",
            "--spec",
            str(assembly_spec),
            "--output",
            str(publication),
        ],
    )
    if assembly.exit_code != 0:
        message = f"assembly failed: {assembly}"
        raise AssertionError(message)

    browser_path = browser
    if browser_path is None:
        if render_override is None:
            message = "browser or render_override is required"
            raise ValueError(message)
        browser_path = root / "fixture-browser.exe"
        browser_path.write_bytes(b"fixture browser")
    dynamic_assemble: Any = assemble_module
    original_render = dynamic_assemble.render_pdf
    if render_override is not None:
        dynamic_assemble.render_pdf = render_override
    try:
        render = invoke_main(
            assemble_module,
            [
                "render",
                "--html",
                str(publication / "index.html"),
                "--pdf",
                str(publication / "publication.pdf"),
                "--browser",
                str(browser_path),
            ],
        )
    finally:
        dynamic_assemble.render_pdf = original_render
    if render.exit_code != 0:
        message = f"render failed: {render}"
        raise AssertionError(message)

    return publication


def detect_browser() -> Path | None:
    """Return an existing local Chromium-family executable, if available."""
    configured = os.environ.get("SCHOLARLY_PUBLICATION_BROWSER")
    candidates = [Path(configured) if configured else None]
    if os.name == "nt":
        for base_name in ("PROGRAMFILES", "PROGRAMFILES(X86)", "LOCALAPPDATA"):
            base = os.environ.get(base_name)
            if base:
                candidates.extend(
                    [
                        Path(base)
                        / "Microsoft"
                        / "Edge"
                        / "Application"
                        / "msedge.exe",
                        Path(base)
                        / "Google"
                        / "Chrome"
                        / "Application"
                        / "chrome.exe",
                    ]
                )
    candidates.extend(
        Path(found)
        for executable in (
            "msedge",
            "chromium",
            "chromium-browser",
            "google-chrome",
            "chrome",
        )
        if (found := shutil.which(executable))
    )
    return next(
        (
            candidate.resolve()
            for candidate in candidates
            if candidate is not None and candidate.is_file()
        ),
        None,
    )
