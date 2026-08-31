# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "defusedxml==0.7.1",
#   "jsonschema==4.25.1",
#   "pymupdf==1.26.6",
# ]
# ///

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import json
import math
import re
import shutil
import stat
import sys
import tempfile
import uuid
from collections import Counter
from functools import cache
from itertools import pairwise
from pathlib import Path, PurePosixPath
from typing import TYPE_CHECKING, Any, NamedTuple, NoReturn
from xml.etree.ElementTree import ParseError

from defusedxml.common import DefusedXmlException
from defusedxml.ElementTree import fromstring
from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError

if TYPE_CHECKING:
    from collections.abc import Iterable

SCRIPT_VERSION = "1.0.0"
PINNED_PYMUPDF_VERSION = "1.26.6"
PARSER_NAME = f"PyMuPDF {PINNED_PYMUPDF_VERSION}"
MAX_SELECTED_PAGES = 500
GEOMETRY_TOLERANCE = 0.01
SVG_VIEWBOX_TOLERANCE = 0.001
POSSIBLE_SCAN_CHARACTER_THRESHOLD = 32
PACKAGE_CHARACTER_MINIMUM = 32
PACKAGE_CHARACTERS_PER_PAGE = 8
REPARSE_POINT_ATTRIBUTE = getattr(
    stat,
    "FILE_ATTRIBUTE_REPARSE_POINT",
    0x400,
)

PROFILE_PATTERN = re.compile(r"^[a-z0-9][a-z0-9-]*$")
SVG_ID_PATTERN = re.compile(r"^[A-Za-z_][A-Za-z0-9_.:-]*$")
SVG_NUMBER_PATTERN = re.compile(
    r"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$"
)
SVG_FRAGMENT_PATTERN = re.compile(r"^#[A-Za-z_][A-Za-z0-9_.:-]*$")
SVG_LOCAL_URL_PATTERN = re.compile(
    r"""^url\(\s*(['"]?)(#[A-Za-z_][A-Za-z0-9_.:-]*)\1\s*\)$""",
    re.IGNORECASE,
)
SVG_DATA_IMAGE_PATTERN = re.compile(
    r"^data:image/(png|jpeg);base64,(.*)$",
    re.IGNORECASE | re.DOTALL,
)
SVG_STYLE_PATTERN = re.compile(
    r"^mix-blend-mode\s*:\s*([a-z-]+)\s*;?$",
    re.IGNORECASE,
)
SVG_EXTERNAL_SCHEME_PATTERN = re.compile(
    r"(?:^|[\s('\"])(?:data|file|ftp|https?|javascript):",
    re.IGNORECASE,
)
PDF_NAME_OBJECT_PATTERN = re.compile(
    r"^/((?:#[0-9A-Fa-f]{2}|[^\x00\t\n\f\r ()<>\[\]{}/%])+)$"
)
PDF_REFERENCE_OBJECT_PATTERN = re.compile(r"^([0-9]+)\s+[0-9]+\s+R$")
PDF_REFERENCE_PATTERN = re.compile(r"([0-9]+)\s+[0-9]+\s+R")
PDF_ADDITIONAL_ACTION_KEYS = (
    "Bl",
    "C",
    "D",
    "DP",
    "DS",
    "E",
    "F",
    "Fo",
    "K",
    "O",
    "PC",
    "PI",
    "PO",
    "PV",
    "U",
    "V",
    "WC",
    "WP",
    "WS",
    "X",
)
PYMUPDF_ERRORS = (
    AttributeError,
    OSError,
    OverflowError,
    RuntimeError,
    TypeError,
    ValueError,
)
TRACE_ERRORS = (*PYMUPDF_ERRORS, IndexError, KeyError)


def word_set(value: str) -> frozenset[str]:
    return frozenset(value.split())


SOURCE_PACKAGE_SCHEMA = (
    Path(__file__).resolve().parents[1]
    / "assets"
    / "source-package.schema.json"
)
SOURCE_BLOCKS_SCHEMA = (
    Path(__file__).resolve().parents[1] / "assets" / "source-blocks.schema.json"
)
SECTION_MAP_SCHEMA = (
    Path(__file__).resolve().parents[1] / "assets" / "section-map.schema.json"
)
FIGURE_MAP_SCHEMA = (
    Path(__file__).resolve().parents[1] / "assets" / "figure-map.schema.json"
)

PAGE_ASSET_PATHS = {
    "blocks": "pages/{page_id}/blocks.json",
    "svg": "pages/{page_id}/page.svg",
}
MAP_ASSET_PATHS = {
    "sections": "maps/sections.json",
    "figure_map": "maps/figures.json",
}

SVG_NAMESPACE = "http://www.w3.org/2000/svg"
XLINK_NAMESPACE = "http://www.w3.org/1999/xlink"
INKSCAPE_NAMESPACE = "http://www.inkscape.org/namespaces/inkscape"
SVG_ALLOWED_ELEMENTS = word_set(
    """
    circle clipPath defs ellipse g image line linearGradient mask path pattern
    polygon polyline radialGradient rect stop svg symbol use
    """
)
SVG_ALLOWED_ATTRIBUTES = word_set(
    """
    clip-path clip-rule clipPathUnits color color-interpolation color-rendering
    cx cy d data-text display fill fill-opacity fill-rule fx fy
    gradientTransform gradientUnits height id image-rendering mask
    maskContentUnits maskUnits offset opacity overflow pathLength
    patternContentUnits patternTransform patternUnits points preserveAspectRatio
    r rx ry shape-rendering spreadMethod stop-color stop-opacity stroke
    stroke-dasharray stroke-dashoffset stroke-linecap stroke-linejoin
    stroke-miterlimit stroke-opacity stroke-width transform vector-effect
    version viewBox visibility width x x1 x2 y y1 y2
    """
)
SVG_LOCAL_URL_ATTRIBUTES = frozenset({"clip-path", "fill", "mask", "stroke"})
SVG_PRESENTATION_ATTRIBUTES = word_set(
    """
    clip-path clip-rule color color-interpolation color-rendering display fill
    fill-opacity fill-rule image-rendering mask opacity overflow shape-rendering
    stop-color stop-opacity stroke stroke-dasharray stroke-dashoffset
    stroke-linecap stroke-linejoin stroke-miterlimit stroke-opacity
    stroke-width vector-effect visibility
    """
)
SVG_STANDARD_BLEND_MODES = word_set(
    """
    color color-burn color-dodge darken difference exclusion hard-light hue
    lighten luminosity multiply normal overlay saturation screen soft-light
    """
)

REPLACEMENT_MESSAGE = (
    "Extracted block text contains Unicode replacement characters."
)
POSSIBLE_SCAN_MESSAGE = (
    "The page has fewer than 32 text characters and displayed images; "
    "confirm that scan restoration or OCR is not required."
)
HIDDEN_TEXT_MESSAGE = (
    "PyMuPDF text tracing suggests hidden or nonpainting extracted text; "
    "inspect the page before downstream use."
)
MUSIC_FIGURE_MESSAGE = (
    "The music-notation profile requires at least one explicitly mapped figure."
)
TRACE_FAILURE_MESSAGE = (
    "PyMuPDF text tracing could not inspect hidden or nonpainting text; "
    "inspect the page before downstream use."
)
NO_TEXT_MESSAGE = (
    "The selected pages contain no non-whitespace block text and are outside "
    "the born-digital reconstruction boundary."
)


class ContractError(ValueError):
    pass


class JsonArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> NoReturn:
        raise ContractError(f"invalid command line: {message}")


class BlockSummary(NamedTuple):
    block_count: int
    text_characters: int
    replacement_characters: int


class PageEvidence(NamedTuple):
    blocks: dict[str, Any]
    blocks_content: bytes
    svg_content: bytes
    summary: BlockSummary
    image_count: int
    drawing_count: int
    link_count: int
    hidden_text: bool | None


class PackageState(NamedTuple):
    page_records: list[dict[str, Any]]
    page_assets: dict[tuple[int, str], bytes]


def emit_json(value: dict[str, Any]) -> None:
    print(json.dumps(value, ensure_ascii=False, sort_keys=True))


def expect(errors: list[str], condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def json_bytes(value: Any) -> bytes:
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


def reject_json_constant(value: str) -> Any:
    raise ValueError(f"non-finite JSON number: {value}")


def unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON object key: {key}")
        result[key] = value
    return result


def parse_json_bytes(content: bytes, role: str) -> Any:
    try:
        text = content.decode("utf-8")
    except UnicodeError as error:
        raise ContractError(f"{role} is not UTF-8: {error}") from error
    try:
        return json.loads(
            text,
            object_pairs_hook=unique_json_object,
            parse_constant=reject_json_constant,
        )
    except (json.JSONDecodeError, ValueError) as error:
        raise ContractError(f"cannot parse {role}: {error}") from error


def read_bytes(path: Path, role: str) -> bytes:
    try:
        return path.read_bytes()
    except OSError as error:
        raise ContractError(f"cannot read {role} {path}: {error}") from error


def write_bytes(path: Path, content: bytes) -> None:
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    except OSError as error:
        raise ContractError(f"cannot write {path}: {error}") from error


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def hash_file(path: Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    length = 0
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
                length += len(chunk)
    except OSError as error:
        raise ContractError(f"cannot hash {path}: {error}") from error
    return digest.hexdigest(), length


def asset_record(path_value: str, content: bytes) -> dict[str, Any]:
    return {
        "path": path_value,
        "sha256": sha256_bytes(content),
        "bytes": len(content),
    }


@cache
def load_schema(path: Path) -> dict[str, Any]:
    value = parse_json_bytes(read_bytes(path, "schema"), "schema")
    if not isinstance(value, dict):
        raise ContractError(f"schema must be a JSON object: {path}")
    try:
        Draft202012Validator.check_schema(value)
    except SchemaError as error:
        raise ContractError(f"invalid JSON schema {path}: {error}") from error
    return value


def schema_validation_errors(
    value: Any,
    schema_path: Path,
    role: str,
) -> list[str]:
    validator = Draft202012Validator(load_schema(schema_path))
    errors: list[str] = []
    for error in sorted(
        validator.iter_errors(value),
        key=lambda item: tuple(str(part) for part in item.absolute_path),
    ):
        location = "/".join(str(part) for part in error.absolute_path)
        errors.append(
            f"{role} schema error at {location or '<root>'}: {error.message}"
        )
    return errors


def path_exists(path: Path) -> bool:
    return path.exists() or path.is_symlink()


def is_link_or_reparse(info: Any) -> bool:
    return stat.S_ISLNK(info.st_mode) or bool(
        getattr(info, "st_file_attributes", 0) & REPARSE_POINT_ATTRIBUTE
    )


def remove_path(path: Path) -> None:
    if not path_exists(path):
        return
    if path.is_symlink() or path.is_file():
        path.unlink()
    else:
        shutil.rmtree(path)


def source_is_within_output(source: Path, output: Path) -> bool:
    try:
        source.relative_to(output)
    except ValueError:
        pass
    else:
        return True
    if not path_exists(output):
        return False
    for ancestor in source.parents:
        try:
            if output.samefile(ancestor):
                return True
        except OSError as error:
            raise ContractError(
                f"cannot compare source and output filesystem identity: {error}"
            ) from error
    return False


def resolve_asset_path(root: Path, value: str) -> Path:
    if not value or "\\" in value:
        raise ContractError(f"invalid package-relative path: {value!r}")
    relative = PurePosixPath(value)
    if relative.is_absolute() or any(
        part in {"", ".", ".."} for part in relative.parts
    ):
        raise ContractError(f"invalid package-relative path: {value!r}")
    candidate = root.joinpath(*relative.parts)
    if candidate.is_symlink():
        raise ContractError(f"asset path must not be a symlink: {value}")
    try:
        resolved_root = root.resolve(strict=True)
        resolved = candidate.resolve(strict=True)
        resolved.relative_to(resolved_root)
    except (OSError, ValueError) as error:
        raise ContractError(
            f"asset path is missing or escapes the package: {value}"
        ) from error
    if not resolved.is_file():
        raise ContractError(f"asset path is not a regular file: {value}")
    return resolved


def parse_pages(expression: str, page_count: int) -> list[int]:
    if not expression.strip():
        raise ContractError("--pages must be non-empty")

    pages: list[int] = []
    seen: set[int] = set()
    for raw_token in expression.split(","):
        token = raw_token.strip()
        match = re.fullmatch(r"([0-9]+)(?:-([0-9]+))?", token)
        if match is None:
            raise ContractError(f"invalid page item: {token or '<empty>'}")
        start = int(match.group(1))
        end = int(match.group(2) or match.group(1))
        if start < 1 or end < start or end > page_count:
            raise ContractError(
                f"page selection must use ascending values within "
                f"1-{page_count}: {token}"
            )
        span = end - start + 1
        if span > MAX_SELECTED_PAGES - len(pages):
            raise ContractError(
                f"selected page count exceeds {MAX_SELECTED_PAGES}"
            )
        for page_number in range(start, end + 1):
            if page_number in seen:
                raise ContractError(
                    f"page selection contains duplicate page {page_number}"
                )
            seen.add(page_number)
            pages.append(page_number)

    if not pages:
        raise ContractError("at least one PDF page is required")
    pages.sort()
    return pages


def validated_profiles(values: list[str]) -> list[str]:
    profiles: set[str] = set()
    for value in values:
        profile = value.strip()
        if not PROFILE_PATTERN.fullmatch(profile):
            raise ContractError(f"invalid profile name: {value!r}")
        profiles.add(profile)
    return sorted(profiles)


def finite_number(value: Any, context: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ContractError(f"{context} must be a number")
    number = float(value)
    if not math.isfinite(number):
        raise ContractError(f"{context} must be finite")
    return number


def bbox_numbers(value: Any, context: str) -> list[float]:
    if not isinstance(value, list) or len(value) != 4:
        raise ContractError(f"{context} must be a four-number bbox")
    numbers = [
        finite_number(coordinate, f"{context} coordinate")
        for coordinate in value
    ]
    x0, y0, x1, y1 = numbers
    if x0 >= x1 or y0 >= y1:
        raise ContractError(f"{context} must have positive width and height")
    return numbers


def bbox_within_page(
    bbox: list[float],
    width: float,
    height: float,
) -> bool:
    x0, y0, x1, y1 = bbox
    return 0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height


def media_box_contains_crop(
    media: list[float],
    width: float,
    height: float,
) -> bool:
    return (
        media[0] <= GEOMETRY_TOLERANCE
        and media[1] <= GEOMETRY_TOLERANCE
        and media[2] >= width - GEOMETRY_TOLERANCE
        and media[3] >= height - GEOMETRY_TOLERANCE
    )


def canonical_block_bbox(
    value: Any,
    width: float,
    height: float,
    context: str,
) -> list[float]:
    x0, y0, x1, y1 = bbox_numbers(value, context)
    clipped = [
        max(0.0, min(width, x0)),
        max(0.0, min(height, y0)),
        max(0.0, min(width, x1)),
        max(0.0, min(height, y1)),
    ]
    result = [round(number, 3) for number in clipped]
    if result[0] >= result[2] or result[1] >= result[3]:
        raise ContractError(
            f"{context} does not intersect the canonical page geometry"
        )
    return result


def page_geometry(
    page: Any,
) -> tuple[float, float, list[float], list[float], int]:
    crop_box = page.cropbox
    rotation = int(page.rotation)
    crop_width = float(crop_box.width)
    crop_height = float(crop_box.height)
    if crop_width <= 0 or crop_height <= 0:
        raise ContractError("page crop box must have positive geometry")
    if rotation:
        page.set_rotation(0)
    try:
        effective_rect = page.rect
        transformed_media = page.mediabox * page.transformation_matrix
        width = round(float(effective_rect.width), 3)
        height = round(float(effective_rect.height), 3)
        media = [
            round(float(transformed_media.x0), 3),
            round(float(transformed_media.y0), 3),
            round(float(transformed_media.x1), 3),
            round(float(transformed_media.y1), 3),
        ]
    finally:
        if rotation:
            page.set_rotation(rotation)
    if (
        not math.isfinite(width)
        or not math.isfinite(height)
        or width <= 0
        or height <= 0
    ):
        raise ContractError("page crop box must have finite positive geometry")
    bbox_numbers(media, "page media box")
    if not media_box_contains_crop(media, width, height):
        raise ContractError("page media box does not contain the crop box")
    if rotation not in {0, 90, 180, 270}:
        raise ContractError(f"unsupported page rotation: {rotation}")
    return (
        width,
        height,
        [0.0, 0.0, width, height],
        media,
        rotation,
    )


def section_map_errors(
    data: Any,
    selected_pages: set[int],
) -> list[str]:
    errors = schema_validation_errors(
        data,
        SECTION_MAP_SCHEMA,
        "section map",
    )
    if errors:
        return errors

    sections = data["sections"]
    seen_ids: set[str] = set()
    covered_pages: set[int] = set()
    previous_end = 0
    for index, section in enumerate(sections, start=1):
        context = f"section {index}"
        section_id = section["id"]
        start = section["start_pdf_page"]
        end = section["end_pdf_page"]
        if section_id in seen_ids:
            errors.append(f"duplicate section id: {section_id}")
        seen_ids.add(section_id)
        if not section["title"].strip():
            errors.append(f"{context} title must contain visible text")
        if start > end:
            errors.append(f"{context} has a descending page range")
            continue
        if start <= previous_end:
            errors.append(
                f"{context} overlaps or is out of order with the prior section"
            )
        previous_end = end
        section_pages = {
            page_number
            for page_number in selected_pages
            if start <= page_number <= end
        }
        if len(section_pages) != end - start + 1:
            errors.append(
                f"{context} references pages outside the selected package"
            )
        covered_pages.update(section_pages)
    missing_pages = sorted(selected_pages - covered_pages)
    if missing_pages:
        errors.append(
            "section map does not cover selected pages: "
            + ", ".join(str(page) for page in missing_pages)
        )
    return errors


def figure_map_errors(
    data: Any,
    page_geometry_by_number: dict[int, tuple[float, float]],
    declared_profiles: set[str],
) -> list[str]:
    errors = schema_validation_errors(
        data,
        FIGURE_MAP_SCHEMA,
        "figure map",
    )
    if errors:
        return errors

    figure_ids: set[str] = set()
    part_ids: set[str] = set()
    for figure_index, figure in enumerate(data["figures"], start=1):
        context = f"figure {figure_index}"
        figure_id = figure["id"]
        if figure_id in figure_ids:
            errors.append(f"duplicate figure id: {figure_id}")
        figure_ids.add(figure_id)
        profile = figure["profile"]
        if profile is not None and profile not in declared_profiles:
            errors.append(
                f"{context} profile is not declared by the source package: "
                f"{profile}"
            )
        expected_orders = list(range(1, len(figure["parts"]) + 1))
        actual_orders = [part["order"] for part in figure["parts"]]
        if actual_orders != expected_orders:
            errors.append(f"{context} part order must match array order [1..n]")
        for part_index, part in enumerate(figure["parts"], start=1):
            part_context = f"{context} part {part_index}"
            part_id = part["id"]
            if part_id in part_ids:
                errors.append(f"duplicate figure part id: {part_id}")
            part_ids.add(part_id)
            page_number = part["pdf_page"]
            geometry = page_geometry_by_number.get(page_number)
            if geometry is None:
                errors.append(
                    f"{part_context} references unavailable page {page_number}"
                )
                continue
            try:
                bbox = bbox_numbers(part["bbox"], f"{part_context} bbox")
            except ContractError as error:
                errors.append(str(error))
                continue
            if not bbox_within_page(bbox, *geometry):
                errors.append(
                    f"{part_context} bbox exceeds page {page_number} bounds"
                )
    return errors


def load_map_input(
    path: Path,
    role: str,
    selected_pages: set[int],
    page_geometry_by_number: dict[int, tuple[float, float]],
    declared_profiles: set[str],
) -> bytes:
    content = read_bytes(path, role)
    data = parse_json_bytes(content, role)
    errors = (
        section_map_errors(data, selected_pages)
        if role == "section map"
        else figure_map_errors(
            data,
            page_geometry_by_number,
            declared_profiles,
        )
    )
    if errors:
        raise ContractError("; ".join(errors))
    return content


def write_map_asset(
    candidate: Path,
    role: str,
    content: bytes | None,
) -> dict[str, Any] | None:
    if content is None:
        return None
    path_value = MAP_ASSET_PATHS[role]
    write_bytes(
        candidate.joinpath(*PurePosixPath(path_value).parts),
        content,
    )
    return asset_record(path_value, content)


def require_pymupdf() -> tuple[Any, str]:
    try:
        import fitz
    except ImportError as error:
        raise ContractError(
            "PyMuPDF is unavailable; run this file with uv run --script"
        ) from error
    version_info = getattr(fitz, "version", None)
    if (
        not isinstance(version_info, (list, tuple))
        or not version_info
        or not isinstance(version_info[0], str)
    ):
        raise ContractError("cannot inspect the installed PyMuPDF version")
    version = version_info[0]
    if version != PINNED_PYMUPDF_VERSION:
        raise ContractError(
            f"PyMuPDF {version} does not match pinned {PINNED_PYMUPDF_VERSION}"
        )
    return fitz, version


def source_pdf_is_encrypted(document: Any) -> bool:
    trailer = document.xref_get_key(-1, "Encrypt")
    return (
        bool(document.needs_pass)
        or bool(document.is_encrypted)
        or trailer != ("null", "null")
    )


def source_attachments(document: Any) -> list[str]:
    embedded_names = document.embfile_names()
    page_count = int(document.page_count)
    if not isinstance(embedded_names, list) or any(
        not isinstance(name, str) for name in embedded_names
    ):
        raise ContractError("cannot inspect PDF attachments: invalid inventory")

    attachments = list(embedded_names)
    for page_index in range(page_count):
        page = document.load_page(page_index)
        for annotation_index, _ in enumerate(
            page.annots(types=[17]) or (),
            start=1,
        ):
            attachments.append(
                f"page-{page_index + 1:04d}:"
                f"file-attachment-{annotation_index:04d}"
            )
    for xref in range(1, int(document.xref_length())):
        associated_kind, associated_value = document.xref_get_key(xref, "AF")
        if pdf_array_has_entries(
            document,
            associated_kind,
            associated_value,
        ):
            attachments.append(f"xref-{xref}:associated-files")
        object_kind, object_value = document.xref_get_key(xref, "Type")
        is_embedded_file = (
            object_kind == "name" and object_value.lstrip("/") == "EmbeddedFile"
        ) or (
            object_kind == "xref"
            and referenced_pdf_name(
                document,
                int(object_value.split(maxsplit=1)[0]),
            )
            == "EmbeddedFile"
        )
        if is_embedded_file:
            attachments.append(f"xref-{xref}:embedded-file")
    return sorted(attachments)


def decoded_pdf_name(value: str) -> str:
    return re.sub(
        r"#([0-9A-Fa-f]{2})",
        lambda match: chr(int(match.group(1), 16)),
        value,
    )


def referenced_pdf_name(document: Any, xref: int) -> str | None:
    visited: set[int] = set()
    for _ in range(16):
        if xref in visited:
            raise ContractError("cyclic PDF name reference")
        visited.add(xref)
        value = document.xref_object(xref, compressed=True).strip()
        name_match = PDF_NAME_OBJECT_PATTERN.fullmatch(value)
        if name_match is not None:
            return decoded_pdf_name(name_match.group(1))
        reference_match = PDF_REFERENCE_OBJECT_PATTERN.fullmatch(value)
        if reference_match is None:
            return None
        xref = int(reference_match.group(1))
    raise ContractError("PDF name reference chain exceeds 16 objects")


def resolved_pdf_array(
    document: Any,
    kind: str,
    value: str,
) -> str | None:
    if kind == "null":
        return None
    visited: set[int] = set()
    for _ in range(16):
        if kind == "array":
            stripped = value.strip()
            if not stripped.startswith("[") or not stripped.endswith("]"):
                raise ContractError("malformed PDF associated-file array")
            return stripped
        if kind != "xref":
            raise ContractError("PDF value must be an array")
        xref = int(value.split(maxsplit=1)[0])
        if xref in visited:
            raise ContractError("cyclic PDF array reference")
        visited.add(xref)
        value = document.xref_object(xref, compressed=True).strip()
        if value.startswith("["):
            kind = "array"
        elif PDF_REFERENCE_OBJECT_PATTERN.fullmatch(value):
            kind = "xref"
        else:
            raise ContractError("malformed PDF array")
    raise ContractError("PDF array reference exceeds 16 objects")


def pdf_array_has_entries(
    document: Any,
    kind: str,
    value: str,
) -> bool:
    array = resolved_pdf_array(document, kind, value)
    return array is not None and bool(array[1:-1].strip())


def javascript_name_tree_has_entries(document: Any) -> bool:
    catalog_xref = int(document.pdf_catalog())
    tree_kind, tree_value = document.xref_get_key(
        catalog_xref,
        "Names/JavaScript",
    )
    if tree_kind == "null":
        return False
    if tree_kind == "xref":
        root_xref = int(tree_value.split(maxsplit=1)[0])
        return javascript_name_tree_node_has_entries(
            document,
            root_xref,
            "",
            set(),
            0,
        )
    if tree_kind == "dict":
        return javascript_name_tree_node_has_entries(
            document,
            catalog_xref,
            "Names/JavaScript/",
            set(),
            0,
        )
    raise ContractError("PDF JavaScript name tree is malformed")


def javascript_name_tree_node_has_entries(
    document: Any,
    xref: int,
    prefix: str,
    visited: set[int],
    depth: int,
) -> bool:
    if depth > 32 or xref in visited:
        raise ContractError("PDF JavaScript name tree is cyclic or too deep")
    visited.add(xref)
    names_kind, names_value = document.xref_get_key(
        xref,
        f"{prefix}Names",
    )
    if pdf_array_has_entries(document, names_kind, names_value):
        return True
    kids_kind, kids_value = document.xref_get_key(
        xref,
        f"{prefix}Kids",
    )
    kids = resolved_pdf_array(document, kids_kind, kids_value)
    if kids is None or not kids[1:-1].strip():
        return False
    interior = kids[1:-1]
    references = [
        int(match.group(1))
        for match in PDF_REFERENCE_PATTERN.finditer(interior)
    ]
    if not references or PDF_REFERENCE_PATTERN.sub("", interior).strip():
        raise ContractError("PDF JavaScript name tree Kids array is malformed")
    return any(
        javascript_name_tree_node_has_entries(
            document,
            child_xref,
            "",
            visited,
            depth + 1,
        )
        for child_xref in references
    )


def pdf_action_contains_javascript(
    mupdf: Any,
    value: Any,
    visited: set[int],
    depth: int,
    *,
    allow_array: bool,
) -> bool:
    if depth > 32:
        raise ContractError("PDF action chain exceeds 32 levels")
    if mupdf.pdf_is_indirect(value):
        xref = int(mupdf.pdf_to_num(value))
        if xref in visited:
            return False
        visited.add(xref)
        value = mupdf.pdf_resolve_indirect(value)
    if mupdf.pdf_is_null(value) or (
        mupdf.pdf_is_array(value) and not allow_array
    ):
        return False
    if mupdf.pdf_is_array(value):
        return any(
            pdf_action_contains_javascript(
                mupdf,
                mupdf.pdf_array_get(value, index),
                visited,
                depth + 1,
                allow_array=False,
            )
            for index in range(int(mupdf.pdf_array_len(value)))
        )
    if not mupdf.pdf_is_dict(value):
        return False

    action_type = mupdf.pdf_dict_gets(value, "S")
    if mupdf.pdf_is_indirect(action_type):
        action_type = mupdf.pdf_resolve_indirect(action_type)
    script = mupdf.pdf_dict_gets(value, "JS")
    if (
        mupdf.pdf_is_name(action_type)
        and mupdf.pdf_to_name(action_type) in {"JavaScript", "Rendition"}
        and not mupdf.pdf_is_null(script)
    ):
        return True
    return pdf_action_contains_javascript(
        mupdf,
        mupdf.pdf_dict_gets(value, "Next"),
        visited,
        depth + 1,
        allow_array=True,
    )


def pdf_3d_object_contains_javascript(mupdf: Any, value: Any) -> bool:
    if mupdf.pdf_is_indirect(value):
        value = mupdf.pdf_resolve_indirect(value)
    if mupdf.pdf_is_null(value) or not mupdf.pdf_is_dict(value):
        return False

    object_type = mupdf.pdf_dict_gets(value, "Type")
    if mupdf.pdf_is_indirect(object_type):
        object_type = mupdf.pdf_resolve_indirect(object_type)
    on_instantiate = mupdf.pdf_dict_gets(value, "OnInstantiate")
    if mupdf.pdf_is_indirect(on_instantiate):
        on_instantiate = mupdf.pdf_resolve_indirect(on_instantiate)
    return (
        mupdf.pdf_is_name(object_type)
        and mupdf.pdf_to_name(object_type) == "3D"
        and not mupdf.pdf_is_null(on_instantiate)
    )


def source_has_javascript(document: Any) -> bool:
    if javascript_name_tree_has_entries(document):
        return True
    import pymupdf

    mupdf = pymupdf.mupdf
    pdf_document = mupdf.pdf_document_from_fz_document(document.this)
    for xref in range(1, int(document.xref_length())):
        value = mupdf.pdf_load_object(pdf_document, xref)
        if pdf_3d_object_contains_javascript(mupdf, value):
            return True
        if not mupdf.pdf_is_dict(value):
            continue
        for action in (
            value,
            mupdf.pdf_dict_gets(value, "A"),
            mupdf.pdf_dict_gets(value, "OpenAction"),
        ):
            if pdf_action_contains_javascript(
                mupdf,
                action,
                set(),
                0,
                allow_array=False,
            ):
                return True
        if pdf_action_contains_javascript(
            mupdf,
            mupdf.pdf_dict_gets(value, "Next"),
            set(),
            0,
            allow_array=True,
        ):
            return True
        additional_actions = mupdf.pdf_dict_gets(value, "AA")
        if mupdf.pdf_is_indirect(additional_actions):
            additional_actions = mupdf.pdf_resolve_indirect(additional_actions)
        if not mupdf.pdf_is_dict(additional_actions):
            continue
        for key in PDF_ADDITIONAL_ACTION_KEYS:
            if pdf_action_contains_javascript(
                mupdf,
                mupdf.pdf_dict_gets(additional_actions, key),
                set(),
                0,
                allow_array=False,
            ):
                return True
    return False


def reject_unsafe_pdf_features(document: Any) -> None:
    if source_pdf_is_encrypted(document):
        raise ContractError("encrypted PDFs are not accepted")
    attachments = source_attachments(document)
    if attachments:
        raise ContractError(
            "PDF attachments are not accepted: " + ", ".join(attachments)
        )
    catalog_xref = int(document.pdf_catalog())
    xfa_kind, _ = document.xref_get_key(catalog_xref, "AcroForm/XFA")
    if xfa_kind != "null":
        raise ContractError("XFA PDFs are not accepted")
    if source_has_javascript(document):
        raise ContractError("embedded PDF JavaScript is not accepted")


def stable_blocks(
    page: Any,
    page_id: str,
    width: float,
    height: float,
) -> dict[str, Any]:
    source_blocks = page.get_text("blocks", sort=True)
    if not isinstance(source_blocks, (list, tuple)):
        raise ContractError("PyMuPDF text block extraction returned no array")

    blocks: list[dict[str, Any]] = []
    for source_index, source_block in enumerate(source_blocks, start=1):
        if not isinstance(source_block, (list, tuple)) or len(source_block) < 7:
            raise ContractError(
                f"PyMuPDF text block {source_index} is malformed"
            )
        x0, y0, x1, y1, text, source_number, block_type = source_block[:7]
        if block_type != 0:
            continue
        if not isinstance(text, str):
            raise ContractError(
                f"PyMuPDF text block {source_index} has non-string text"
            )
        if not text.strip():
            continue
        if isinstance(source_number, bool) or not isinstance(
            source_number,
            int,
        ):
            raise ContractError(
                f"PyMuPDF text block {source_index} has an invalid number"
            )
        order = len(blocks) + 1
        blocks.append(
            {
                "id": f"{page_id}-block-{order:04d}",
                "source_order": order,
                "source_block_number": source_number,
                "bbox": canonical_block_bbox(
                    [x0, y0, x1, y1],
                    width,
                    height,
                    f"{page_id} source block {source_index}",
                ),
                "text": text,
            }
        )
    return {
        "schema_version": "1.0",
        "coordinate_space": "pdf-points-top-left",
        "page_id": page_id,
        "blocks": blocks,
    }


def canonical_page_svg(page: Any) -> bytes:
    rotation = int(page.rotation)
    if rotation:
        page.set_rotation(0)
    try:
        value = page.get_svg_image(text_as_path=True)
    finally:
        if rotation:
            page.set_rotation(rotation)
    if not isinstance(value, str):
        raise ContractError("PyMuPDF SVG generation returned a non-string")
    return value.encode("utf-8")


def block_summary(data: dict[str, Any]) -> BlockSummary:
    text = "".join(block["text"] for block in data["blocks"])
    return BlockSummary(
        block_count=len(data["blocks"]),
        text_characters=sum(not character.isspace() for character in text),
        replacement_characters=text.count("\ufffd"),
    )


def suspected_hidden_or_nonpainting_text(
    page: Any,
    extracted_text: str,
) -> bool | None:
    expected = "".join(
        character for character in extracted_text if not character.isspace()
    )
    if not expected:
        return False
    try:
        visible_glyphs: set[
            tuple[int, tuple[float, ...], tuple[float, ...]]
        ] = set()
        nonpainting_glyphs: set[
            tuple[int, tuple[float, ...], tuple[float, ...]]
        ] = set()
        for span in page.get_texttrace():
            characters = [
                character
                for character in span["chars"]
                if not chr(character[0]).isspace()
            ]
            if not characters:
                continue
            glyphs = {
                (
                    int(character[0]),
                    tuple(float(value) for value in character[2]),
                    tuple(float(value) for value in character[3]),
                )
                for character in characters
            }
            target = (
                visible_glyphs
                if span["type"] in {0, 1} and float(span["opacity"]) > 0
                else nonpainting_glyphs
            )
            target.update(glyphs)
        if nonpainting_glyphs - visible_glyphs:
            return True
        painted = Counter(chr(character[0]) for character in visible_glyphs)
        return bool(Counter(expected) - painted)
    except TRACE_ERRORS:
        return None


def xml_namespace(value: Any) -> str:
    if not isinstance(value, str) or not value.startswith("{"):
        return ""
    return value[1:].split("}", 1)[0]


def xml_local_name(value: Any) -> str:
    if not isinstance(value, str):
        return ""
    return value.rsplit("}", 1)[-1]


def svg_number(value: str | None) -> float:
    if value is None or SVG_NUMBER_PATTERN.fullmatch(value.strip()) is None:
        return math.nan
    return float(value)


def svg_contains_image(content: bytes) -> bool:
    try:
        root = fromstring(content)
    except (DefusedXmlException, ParseError, ValueError) as error:
        raise ContractError(f"cannot parse validated SVG: {error}") from error
    return any(
        xml_namespace(element.tag) == SVG_NAMESPACE
        and xml_local_name(element.tag) == "image"
        for element in root.iter()
    )


def svg_css_is_obfuscated(value: str) -> bool:
    return (
        "\\" in value
        or "/*" in value
        or "*/" in value
        or re.search(r"@import\b", value, flags=re.IGNORECASE) is not None
        or re.search(r"(?:var|env)\s*\(", value, flags=re.IGNORECASE)
        is not None
    )


def svg_data_image_error(value: str) -> str | None:
    match = SVG_DATA_IMAGE_PATTERN.fullmatch(value.strip())
    if match is None:
        return "data image must be an embedded PNG or JPEG"
    payload = re.sub(r"\s+", "", match.group(2))
    try:
        decoded = base64.b64decode(payload, validate=True)
    except (binascii.Error, ValueError):
        return "data image has invalid base64"
    media_type = match.group(1).casefold()
    if media_type == "png" and not decoded.startswith(b"\x89PNG\r\n\x1a\n"):
        return "embedded PNG data has no PNG signature"
    if media_type == "jpeg" and not decoded.startswith(b"\xff\xd8\xff"):
        return "embedded JPEG data has no JPEG signature"
    return None


def svg_href_error(
    element_name: str,
    value: str,
    references: list[str],
) -> str | None:
    reference = value.strip()
    if SVG_FRAGMENT_PATTERN.fullmatch(reference):
        references.append(reference)
        return None
    if element_name == "image" and reference.casefold().startswith("data:"):
        return svg_data_image_error(reference)
    return f"external or escaping SVG reference: {reference}"


def svg_attribute_errors(  # noqa: PLR0911
    element_name: str,
    attribute: str,
    value: str,
    references: list[str],
) -> list[str]:
    errors: list[str] = []
    namespace = xml_namespace(attribute)
    name = xml_local_name(attribute)

    if namespace == INKSCAPE_NAMESPACE:
        if element_name == "g" and name == "groupmode" and value == "layer":
            return errors
        if element_name == "g" and name == "label":
            return errors
        return [f"unsupported Inkscape attribute {attribute}"]

    is_href = name == "href"
    if namespace not in {"", XLINK_NAMESPACE}:
        return [f"unsupported namespaced SVG attribute {attribute}"]
    if namespace == XLINK_NAMESPACE and not is_href:
        return [f"unsupported XLink attribute {attribute}"]
    if name.casefold().startswith("on"):
        return [f"active SVG event attribute {name} is forbidden"]

    if name == "style":
        if svg_css_is_obfuscated(value):
            return [
                "CSS escapes, variables, comments, or imports are forbidden"
            ]
        match = SVG_STYLE_PATTERN.fullmatch(value.strip())
        if (
            element_name != "g"
            or match is None
            or match.group(1).casefold() not in SVG_STANDARD_BLEND_MODES
        ):
            return ["only a standard mix-blend-mode style is allowed on g"]
        return errors

    if is_href:
        if element_name not in {
            "image",
            "linearGradient",
            "pattern",
            "radialGradient",
            "use",
        }:
            return [f"href is unsupported on SVG element {element_name}"]
        href_error = svg_href_error(element_name, value, references)
        return [href_error] if href_error is not None else errors

    if name not in SVG_ALLOWED_ATTRIBUTES:
        return [f"unsupported SVG attribute {name}"]

    if name in SVG_PRESENTATION_ATTRIBUTES:
        if svg_css_is_obfuscated(value):
            errors.append(
                f"CSS escapes, variables, comments, or imports in {name} "
                "are forbidden"
            )
        url_match = re.search(r"url\s*\(", value, flags=re.IGNORECASE)
        if url_match is not None:
            local_match = SVG_LOCAL_URL_PATTERN.fullmatch(value.strip())
            if name not in SVG_LOCAL_URL_ATTRIBUTES or local_match is None:
                errors.append(f"external or invalid URL in SVG {name}")
            else:
                references.append(local_match.group(2))
        elif SVG_EXTERNAL_SCHEME_PATTERN.search(value):
            errors.append(f"external scheme in SVG {name} is forbidden")
    return errors


def svg_validation_errors(
    content: bytes,
    role: str,
    width: float,
    height: float,
) -> list[str]:
    errors: list[str] = []
    try:
        text = content.decode("utf-8")
    except UnicodeError as error:
        return [f"{role} is not UTF-8: {error}"]
    if "<!--" in text or "<?" in text or "<![CDATA[" in text:
        errors.append(
            f"{role} contains unsupported XML comments, instructions, or CDATA"
        )
    try:
        root = fromstring(
            text,
            forbid_dtd=True,
            forbid_entities=True,
            forbid_external=True,
        )
    except (DefusedXmlException, ParseError) as error:
        return [f"cannot parse {role}: {error}"]

    if root.tag != f"{{{SVG_NAMESPACE}}}svg":
        errors.append(f"{role} root must be a namespaced svg element")

    identifiers: set[str] = set()
    references: list[str] = []
    for element in root.iter():
        namespace = xml_namespace(element.tag)
        name = xml_local_name(element.tag)
        if namespace != SVG_NAMESPACE:
            errors.append(f"{role} contains non-SVG element {element.tag}")
        if name not in SVG_ALLOWED_ELEMENTS:
            errors.append(f"{role} contains unsupported SVG element {name}")
        if element.text and element.text.strip():
            errors.append(f"{role} contains unsupported text in {name}")
        if element.tail and element.tail.strip():
            errors.append(f"{role} contains unsupported SVG tail text")

        identifier = element.get("id")
        if identifier is not None:
            if SVG_ID_PATTERN.fullmatch(identifier) is None:
                errors.append(f"{role} contains invalid SVG id {identifier!r}")
            elif identifier in identifiers:
                errors.append(f"{role} contains duplicate SVG id {identifier}")
            else:
                identifiers.add(identifier)

        for attribute, value in element.attrib.items():
            for error in svg_attribute_errors(
                name,
                attribute,
                value,
                references,
            ):
                errors.append(f"{role} {error}")

    for reference in references:
        if reference[1:] not in identifiers:
            errors.append(f"{role} references missing local id {reference}")

    svg_width = svg_number(root.get("width"))
    svg_height = svg_number(root.get("height"))
    for attribute, actual, expected in (
        ("width", svg_width, width),
        ("height", svg_height, height),
    ):
        if not math.isfinite(actual) or f"{actual:.3f}" != f"{expected:.3f}":
            errors.append(f"{role} {attribute} does not match page geometry")

    view_box = root.get("viewBox")
    try:
        values = (
            [
                float(item)
                for item in re.split(r"[\s,]+", view_box.strip())
                if item
            ]
            if view_box is not None
            else []
        )
    except ValueError:
        values = []
    expected_view_box = [0.0, 0.0, svg_width, svg_height]
    if (
        len(values) != 4
        or not all(math.isfinite(value) for value in values)
        or any(
            abs(actual - expected) > SVG_VIEWBOX_TOLERANCE
            for actual, expected in zip(
                values,
                expected_view_box,
                strict=True,
            )
        )
    ):
        errors.append(f"{role} viewBox does not match page geometry")
    return errors


def block_validation_errors(
    data: Any,
    role: str,
    page_id: str,
    width: float,
    height: float,
    seen_block_ids: set[str],
) -> tuple[list[str], BlockSummary | None]:
    errors = schema_validation_errors(
        data,
        SOURCE_BLOCKS_SCHEMA,
        role,
    )
    if errors:
        return errors, None

    if data["page_id"] != page_id:
        errors.append(f"{role} page_id does not match {page_id}")
    for index, block in enumerate(data["blocks"], start=1):
        block_id = block["id"]
        expected_id = f"{page_id}-block-{index:04d}"
        if block_id != expected_id:
            errors.append(f"{role} block {index} id must be {expected_id}")
        if block_id in seen_block_ids:
            errors.append(f"duplicate source block id: {block_id}")
        seen_block_ids.add(block_id)
        if block["source_order"] != index:
            errors.append(
                f"{role} block {block_id} source_order must be {index}"
            )
        if not block["text"].strip():
            errors.append(f"{role} block {block_id} text is blank")
        try:
            bbox = bbox_numbers(
                block["bbox"],
                f"{role} block {block_id} bbox",
            )
        except ContractError as error:
            errors.append(str(error))
        else:
            if not bbox_within_page(bbox, width, height):
                errors.append(
                    f"{role} block {block_id} bbox exceeds page geometry"
                )
    return errors, block_summary(data)


def issue_record(
    identifier: str,
    severity: str,
    message: str,
    page: int | None = None,
) -> dict[str, Any]:
    return {
        "id": identifier,
        "severity": severity,
        "page": page,
        "message": message,
    }


def derive_issues(
    pages: list[dict[str, Any]],
    hidden_pages: set[int],
    trace_failure_pages: set[int],
    profiles: list[str],
    has_figures: bool,
) -> list[dict[str, Any]]:
    issues: list[dict[str, Any]] = []
    for page in pages:
        page_number = page["pdf_page"]
        page_id = page["id"]
        replacement_count = page["replacement_characters"]
        if replacement_count:
            issues.append(
                issue_record(
                    f"{page_id}.replacement-characters",
                    "review_required",
                    REPLACEMENT_MESSAGE,
                    page_number,
                )
            )
        if (
            page["text_characters"] < POSSIBLE_SCAN_CHARACTER_THRESHOLD
            and page["image_count"] > 0
        ):
            issues.append(
                issue_record(
                    f"{page_id}.possible-scan",
                    "review_required",
                    POSSIBLE_SCAN_MESSAGE,
                    page_number,
                )
            )
        if page_number in hidden_pages:
            issues.append(
                issue_record(
                    f"{page_id}.hidden-or-nonpainting-text",
                    "review_required",
                    HIDDEN_TEXT_MESSAGE,
                    page_number,
                )
            )
        if page_number in trace_failure_pages:
            issues.append(
                issue_record(
                    f"{page_id}.trace-inspection-failed",
                    "review_required",
                    TRACE_FAILURE_MESSAGE,
                    page_number,
                )
            )

    if "music-notation" in profiles and not has_figures:
        issues.append(
            issue_record(
                "source.music-figure-map-missing",
                "review_required",
                MUSIC_FIGURE_MESSAGE,
            )
        )

    total_text = sum(page["text_characters"] for page in pages)
    if total_text == 0:
        issues.append(
            issue_record(
                "source.no-text",
                "fail",
                NO_TEXT_MESSAGE,
            )
        )
    else:
        threshold = max(
            PACKAGE_CHARACTER_MINIMUM,
            len(pages) * PACKAGE_CHARACTERS_PER_PAGE,
        )
        if total_text < threshold:
            issues.append(
                issue_record(
                    "source.sparse-text",
                    "review_required",
                    f"The selected pages contain {total_text} text "
                    f"characters, below the {threshold}-character review "
                    "threshold.",
                )
            )
    return issues


def status_from_issues(issues: Iterable[dict[str, Any]]) -> str:
    severities = {issue["severity"] for issue in issues}
    if "fail" in severities:
        return "fail"
    if "review_required" in severities:
        return "review_required"
    return "pass"


def page_status(
    issues: list[dict[str, Any]],
    page_number: int,
) -> str:
    return status_from_issues(
        issue for issue in issues if issue.get("page") == page_number
    )


def page_observation_counts(page: Any) -> tuple[int, int, int]:
    images = page.get_image_info()
    drawings = page.get_drawings()
    links = page.get_links()
    if not all(isinstance(value, list) for value in (images, drawings, links)):
        raise ContractError("PyMuPDF page inventory returned invalid data")
    return len(images), len(drawings), len(links)


def generate_page_evidence(
    page: Any,
    page_id: str,
    width: float,
    height: float,
) -> PageEvidence:
    blocks = stable_blocks(page, page_id, width, height)
    blocks_content = json_bytes(blocks)
    svg_content = canonical_page_svg(page)
    errors = svg_validation_errors(
        svg_content,
        f"generated SVG for {page_id}",
        width,
        height,
    )
    if errors:
        raise ContractError("; ".join(errors))
    summary = block_summary(blocks)
    image_count, drawing_count, link_count = page_observation_counts(page)
    extracted_text = "".join(block["text"] for block in blocks["blocks"])
    return PageEvidence(
        blocks=blocks,
        blocks_content=blocks_content,
        svg_content=svg_content,
        summary=summary,
        image_count=image_count,
        drawing_count=drawing_count,
        link_count=link_count,
        hidden_text=suspected_hidden_or_nonpainting_text(
            page,
            extracted_text,
        ),
    )


def open_pdf(fitz_module: Any, path: Path) -> Any:
    try:
        document = fitz_module.open(str(path))
    except PYMUPDF_ERRORS as error:
        raise ContractError(
            f"cannot open source PDF {path}: {error}"
        ) from error
    if not bool(document.is_pdf):
        document.close()
        raise ContractError(f"source is not a PDF: {path}")
    return document


def build_candidate(
    args: argparse.Namespace,
    candidate: Path,
) -> tuple[dict[str, Any], int]:
    fitz, installed_version = require_pymupdf()
    source = args.pdf.resolve()
    if not source.is_file():
        raise ContractError(f"source PDF is missing: {source}")
    if source.suffix.casefold() != ".pdf":
        raise ContractError(f"source must have a .pdf extension: {source}")
    source_hash, source_length = hash_file(source)
    if source_length < 1:
        raise ContractError("source PDF is empty")

    rights_note = args.rights_note.strip()
    if not rights_note:
        raise ContractError("--rights-note must contain visible text")
    profiles = validated_profiles(args.profile)

    document = open_pdf(fitz, source)
    try:
        reject_unsafe_pdf_features(document)
        page_count = int(document.page_count)
        if page_count < 1:
            raise ContractError("source PDF has no pages")
        selected_pages = parse_pages(args.pages, page_count)
        selected_set = set(selected_pages)

        geometry_by_page: dict[int, tuple[float, float]] = {}
        for page_number in selected_pages:
            geometry = page_geometry(document.load_page(page_number - 1))
            geometry_by_page[page_number] = geometry[:2]

        sections_input = (
            load_map_input(
                args.sections.resolve(),
                "section map",
                selected_set,
                geometry_by_page,
                set(profiles),
            )
            if args.sections is not None
            else None
        )
        figure_input = (
            load_map_input(
                args.figure_map.resolve(),
                "figure map",
                selected_set,
                geometry_by_page,
                set(profiles),
            )
            if args.figure_map is not None
            else None
        )

        page_records: list[dict[str, Any]] = []
        hidden_pages: set[int] = set()
        trace_failure_pages: set[int] = set()
        for page_number in selected_pages:
            page = document.load_page(page_number - 1)
            page_id = f"pdf-{page_number:04d}"
            width, height, crop_box, media_box, rotation = page_geometry(page)
            evidence = generate_page_evidence(
                page,
                page_id,
                width,
                height,
            )

            block_path_value = PAGE_ASSET_PATHS["blocks"].format(
                page_id=page_id
            )
            svg_path_value = PAGE_ASSET_PATHS["svg"].format(page_id=page_id)
            write_bytes(
                candidate.joinpath(*PurePosixPath(block_path_value).parts),
                evidence.blocks_content,
            )
            write_bytes(
                candidate.joinpath(*PurePosixPath(svg_path_value).parts),
                evidence.svg_content,
            )
            if evidence.hidden_text is True:
                hidden_pages.add(page_number)
            elif evidence.hidden_text is None:
                trace_failure_pages.add(page_number)

            page_records.append(
                {
                    "id": page_id,
                    "pdf_page": page_number,
                    "printed_folio": None,
                    "width": width,
                    "height": height,
                    "rotation": rotation,
                    "crop_box": crop_box,
                    "media_box": media_box,
                    "assets": {
                        "blocks": asset_record(
                            block_path_value,
                            evidence.blocks_content,
                        ),
                        "svg": asset_record(
                            svg_path_value,
                            evidence.svg_content,
                        ),
                    },
                    "block_count": evidence.summary.block_count,
                    "text_characters": evidence.summary.text_characters,
                    "replacement_characters": (
                        evidence.summary.replacement_characters
                    ),
                    "image_count": evidence.image_count,
                    "vector_drawing_count": evidence.drawing_count,
                    "link_count": evidence.link_count,
                    "status": "pass",
                }
            )

        issues = derive_issues(
            page_records,
            hidden_pages,
            trace_failure_pages,
            profiles,
            figure_input is not None
            and bool(parse_json_bytes(figure_input, "figure map")["figures"]),
        )
        for page_record in page_records:
            page_record["status"] = page_status(
                issues,
                page_record["pdf_page"],
            )

        sections_record = write_map_asset(
            candidate,
            "sections",
            sections_input,
        )
        figure_record = write_map_asset(
            candidate,
            "figure_map",
            figure_input,
        )

        manifest = {
            "schema_version": "1.0",
            "package_id": (
                f"source-{source_hash[:16]}-"
                f"{selected_pages[0]}-{selected_pages[-1]}"
            ),
            "generator": {
                "name": "reconstruct_pdf.py",
                "version": SCRIPT_VERSION,
                "runtime": (
                    f"python-{sys.version_info.major}."
                    f"{sys.version_info.minor}.{sys.version_info.micro}"
                ),
                "parser": f"PyMuPDF {installed_version}",
            },
            "source": {
                "file_name": source.name,
                "sha256": source_hash,
                "bytes": source_length,
                "rights_note": rights_note,
                "page_count": page_count,
                "encrypted": False,
                "attachments": [],
                "embedded_javascript": False,
            },
            "selection": {
                "pdf_pages": selected_pages,
                "first_pdf_page": selected_pages[0],
                "last_pdf_page": selected_pages[-1],
            },
            "coordinate_system": {
                "units": "pdf-points",
                "origin": "top-left",
                "bbox_order": "x0-y0-x1-y1",
            },
            "profiles": profiles,
            "pages": page_records,
            "sections": sections_record,
            "figure_map": figure_record,
            "issues": issues,
            "status": status_from_issues(issues),
        }
        write_bytes(
            candidate / "source-package.json",
            json_bytes(manifest),
        )
        return manifest, len(selected_pages)
    finally:
        document.close()


def validate_asset_binding(
    package_root: Path,
    record: dict[str, Any],
    expected_path: str,
    role: str,
    errors: list[str],
) -> bytes | None:
    path_value = record["path"]
    if path_value != expected_path:
        errors.append(f"{role} path must be {expected_path}, not {path_value}")
    try:
        path = resolve_asset_path(package_root, path_value)
        content = read_bytes(path, f"{role} asset")
    except ContractError as error:
        errors.append(str(error))
        return None
    if len(content) != record["bytes"]:
        errors.append(f"{role} byte length does not match {path_value}")
    actual_hash = sha256_bytes(content)
    if actual_hash != record["sha256"]:
        errors.append(f"{role} SHA-256 does not match {path_value}")
    return content


def numeric_values_equal(
    actual: Iterable[Any],
    expected: Iterable[Any],
) -> bool:
    actual_values = list(actual)
    expected_values = list(expected)
    return len(actual_values) == len(expected_values) and all(
        abs(float(left) - float(right)) <= GEOMETRY_TOLERANCE
        for left, right in zip(actual_values, expected_values, strict=True)
    )


def declared_trace_pages(
    issues: list[dict[str, Any]],
) -> tuple[set[int], set[int]]:
    hidden_pages: set[int] = set()
    failure_pages: set[int] = set()
    for item in issues:
        page_number = item.get("page")
        if not isinstance(page_number, int) or isinstance(page_number, bool):
            continue
        hidden_issue = issue_record(
            f"pdf-{page_number:04d}.hidden-or-nonpainting-text",
            "review_required",
            HIDDEN_TEXT_MESSAGE,
            page_number,
        )
        failure_issue = issue_record(
            f"pdf-{page_number:04d}.trace-inspection-failed",
            "review_required",
            TRACE_FAILURE_MESSAGE,
            page_number,
        )
        if item == hidden_issue:
            hidden_pages.add(page_number)
        elif item == failure_issue:
            failure_pages.add(page_number)
    return hidden_pages, failure_pages


def validate_manifest_semantics(
    manifest: dict[str, Any],
    package_root: Path,
) -> tuple[list[str], PackageState]:
    errors: list[str] = []
    source = manifest["source"]
    selection = manifest["selection"]
    generator = manifest["generator"]
    pages = manifest["pages"]

    selected_pages = selection["pdf_pages"]
    expected_package_id = (
        f"source-{source['sha256'][:16]}-"
        f"{selected_pages[0]}-{selected_pages[-1]}"
    )
    checks = (
        (
            generator["parser"] == PARSER_NAME,
            f"generator.parser must be {PARSER_NAME}",
        ),
        (
            bool(source["rights_note"].strip()),
            "source.rights_note must contain visible text",
        ),
        (
            all(
                current < following
                for current, following in pairwise(selected_pages)
            ),
            "selection.pdf_pages must be strictly increasing",
        ),
        (
            selection["first_pdf_page"] == selected_pages[0],
            "selection.first_pdf_page must equal the first selected page",
        ),
        (
            selection["last_pdf_page"] == selected_pages[-1],
            "selection.last_pdf_page must equal the last selected page",
        ),
        (
            selected_pages[-1] <= source["page_count"],
            "selected pages exceed source.page_count",
        ),
        (
            manifest["package_id"] == expected_package_id,
            f"package_id must be {expected_package_id}",
        ),
        (
            len(pages) == len(selected_pages),
            "pages must contain one record per selected PDF page",
        ),
    )
    errors.extend(message for valid, message in checks if not valid)

    seen_block_ids: set[str] = set()
    geometry_by_page: dict[int, tuple[float, float]] = {}
    page_assets: dict[tuple[int, str], bytes] = {}
    effective_pages: list[dict[str, Any]] = []

    for index, page in enumerate(pages):
        page_number = page["pdf_page"]
        page_id = page["id"]
        if index < len(selected_pages) and page_number != selected_pages[index]:
            errors.append(
                "page records must exactly follow selection.pdf_pages"
            )
        expected_page_id = f"pdf-{page_number:04d}"
        if page_id != expected_page_id:
            errors.append(f"page {page_number} id must be {expected_page_id}")
        width = float(page["width"])
        height = float(page["height"])
        geometry_by_page[page_number] = (width, height)
        expected_crop_box = [0.0, 0.0, width, height]
        if not numeric_values_equal(page["crop_box"], expected_crop_box):
            errors.append(
                f"page {page_number} crop_box must be {expected_crop_box}"
            )
        try:
            media = bbox_numbers(
                page["media_box"],
                f"page {page_number} media_box",
            )
        except ContractError as error:
            errors.append(str(error))
        else:
            if not media_box_contains_crop(media, width, height):
                errors.append(
                    f"page {page_number} media_box does not contain crop_box"
                )
        if page["rotation"] not in {0, 90, 180, 270}:
            errors.append(
                f"page {page_number} rotation must be 0, 90, 180, or 270"
            )

        blocks_path = PAGE_ASSET_PATHS["blocks"].format(page_id=page_id)
        blocks_content = validate_asset_binding(
            package_root,
            page["assets"]["blocks"],
            blocks_path,
            f"page {page_number} blocks",
            errors,
        )
        summary: BlockSummary | None = None
        if blocks_content is not None:
            try:
                blocks_data = parse_json_bytes(
                    blocks_content,
                    f"page {page_number} blocks",
                )
            except ContractError as error:
                errors.append(str(error))
            else:
                block_errors, summary = block_validation_errors(
                    blocks_data,
                    f"page {page_number} blocks",
                    page_id,
                    width,
                    height,
                    seen_block_ids,
                )
                errors.extend(block_errors)
                page_assets[(page_number, "blocks")] = blocks_content

        svg_path = PAGE_ASSET_PATHS["svg"].format(page_id=page_id)
        svg_content = validate_asset_binding(
            package_root,
            page["assets"]["svg"],
            svg_path,
            f"page {page_number} SVG",
            errors,
        )
        svg_has_image: bool | None = None
        if svg_content is not None:
            svg_errors = svg_validation_errors(
                svg_content,
                f"page {page_number} SVG",
                width,
                height,
            )
            errors.extend(svg_errors)
            if not svg_errors:
                svg_has_image = svg_contains_image(svg_content)
            page_assets[(page_number, "svg")] = svg_content

        effective_page = dict(page)
        if summary is not None:
            if page["block_count"] != summary.block_count:
                errors.append(
                    f"page {page_number} block_count does not match blocks"
                )
            if page["text_characters"] != summary.text_characters:
                errors.append(
                    f"page {page_number} text_characters does not match blocks"
                )
            if page["replacement_characters"] != summary.replacement_characters:
                errors.append(
                    f"page {page_number} replacement_characters does not "
                    "match blocks"
                )
            effective_page["block_count"] = summary.block_count
            effective_page["text_characters"] = summary.text_characters
            effective_page["replacement_characters"] = (
                summary.replacement_characters
            )
        if svg_has_image is not None:
            if bool(page["image_count"]) != svg_has_image:
                errors.append(
                    f"page {page_number} image_count presence does not "
                    "match SVG"
                )
            effective_page["image_count"] = int(svg_has_image)
        effective_pages.append(effective_page)

    has_figures = False
    for role in ("sections", "figure_map"):
        record = manifest[role]
        if record is None:
            continue
        content = validate_asset_binding(
            package_root,
            record,
            MAP_ASSET_PATHS[role],
            role.replace("_", " "),
            errors,
        )
        if content is None:
            continue
        try:
            data = parse_json_bytes(content, role.replace("_", " "))
        except ContractError as error:
            errors.append(str(error))
            continue
        map_errors = (
            section_map_errors(data, set(selected_pages))
            if role == "sections"
            else figure_map_errors(
                data,
                geometry_by_page,
                set(manifest["profiles"]),
            )
        )
        errors.extend(map_errors)
        if role == "figure_map" and isinstance(data, dict):
            figures = data.get("figures")
            has_figures = isinstance(figures, list) and bool(figures)

    hidden_pages, trace_failure_pages = declared_trace_pages(manifest["issues"])
    conflicting_trace_pages = sorted(hidden_pages & trace_failure_pages)
    if conflicting_trace_pages:
        errors.append(
            "pages cannot declare both hidden text and trace inspection "
            "failure: "
            + ", ".join(str(page) for page in conflicting_trace_pages)
        )
    expected_issues = derive_issues(
        effective_pages,
        hidden_pages,
        trace_failure_pages,
        manifest["profiles"],
        has_figures,
    )
    if manifest["issues"] != expected_issues:
        errors.append("manifest issues do not match derived package issues")
    expected_status = status_from_issues(expected_issues)
    if manifest["status"] != expected_status:
        errors.append(f"manifest status must be derived as {expected_status}")
    for page in pages:
        expected_page_status = page_status(
            expected_issues,
            page["pdf_page"],
        )
        if page["status"] != expected_page_status:
            errors.append(
                f"page {page['pdf_page']} status must be {expected_page_status}"
            )

    return errors, PackageState(
        page_records=pages,
        page_assets=page_assets,
    )


def source_replay_errors(
    manifest: dict[str, Any],
    state: PackageState,
    source: Path,
) -> list[str]:
    errors: list[str] = []
    fitz, _ = require_pymupdf()
    if not source.is_file():
        return [f"source PDF is missing: {source}"]

    source_hash, source_length = hash_file(source)
    source_record = manifest["source"]
    for matches, label in (
        (source_hash == source_record["sha256"], "SHA-256"),
        (source_length == source_record["bytes"], "byte length"),
    ):
        expect(errors, matches, f"source PDF {label} does not match manifest")

    document = open_pdf(fitz, source)
    try:
        reject_unsafe_pdf_features(document)
        expect(
            errors,
            document.page_count == source_record["page_count"],
            "source PDF page count does not match manifest",
        )

        for page_record in state.page_records:
            page_number = page_record["pdf_page"]
            page = document.load_page(page_number - 1)
            page_id = page_record["id"]
            width, height, crop_box, media_box, rotation = page_geometry(page)
            for actual, expected, label in (
                (
                    [page_record["width"], page_record["height"]],
                    [width, height],
                    "dimensions",
                ),
                (page_record["crop_box"], crop_box, "crop_box"),
                (page_record["media_box"], media_box, "media_box"),
            ):
                expect(
                    errors,
                    numeric_values_equal(actual, expected),
                    f"source page {page_number} {label} does not match",
                )
            expect(
                errors,
                page_record["rotation"] == rotation,
                f"source page {page_number} rotation does not match",
            )

            generated = {
                "blocks": json_bytes(
                    stable_blocks(page, page_id, width, height)
                ),
                "svg": canonical_page_svg(page),
            }
            for role, content in generated.items():
                expect(
                    errors,
                    state.page_assets.get((page_number, role)) == content,
                    f"source page {page_number} regenerated {role} differ",
                )
    finally:
        document.close()
    return errors


def package_validation_report(
    manifest_path: Path,
    source: Path | None,
) -> dict[str, Any]:
    errors: list[str] = []
    status = "fail"
    source_checked = False
    try:
        manifest_content = read_bytes(manifest_path, "source package manifest")
        manifest = parse_json_bytes(
            manifest_content,
            "source package manifest",
        )
    except ContractError as error:
        return {
            "valid": False,
            "status": status,
            "errors": [str(error)],
            "package": str(manifest_path),
            "source_checked": source_checked,
        }

    if isinstance(manifest, dict) and manifest.get("status") in {
        "pass",
        "review_required",
        "fail",
    }:
        status = manifest["status"]
    errors.extend(
        schema_validation_errors(
            manifest,
            SOURCE_PACKAGE_SCHEMA,
            "source package",
        )
    )
    if not errors:
        semantic_errors, state = validate_manifest_semantics(
            manifest,
            manifest_path.parent,
        )
        errors.extend(semantic_errors)
        if source is not None and not errors:
            resolved_source = source.resolve()
            if not resolved_source.is_file():
                errors.append(f"source PDF is missing: {resolved_source}")
            else:
                source_checked = True
                try:
                    errors.extend(
                        source_replay_errors(
                            manifest,
                            state,
                            resolved_source,
                        )
                    )
                except ContractError as error:
                    errors.append(str(error))
                except PYMUPDF_ERRORS as error:
                    errors.append(f"cannot replay source PDF: {error}")
    return {
        "valid": not errors,
        "status": "fail" if errors else status,
        "errors": errors,
        "package": str(manifest_path),
        "source_checked": source_checked,
    }


def create_candidate(output: Path) -> Path:
    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        return Path(
            tempfile.mkdtemp(
                prefix=f".{output.name}.build-",
                dir=output.parent,
            )
        )
    except OSError as error:
        raise ContractError(
            f"cannot create sibling candidate directory: {error}"
        ) from error


def restore_backup(output: Path, backup: Path) -> None:
    if path_exists(output):
        remove_path(output)
    backup.replace(output)


def inspect_replacement_target(output: Path, force: bool) -> bool:
    try:
        output_info = output.lstat()
    except FileNotFoundError:
        return False
    except OSError as error:
        raise ContractError(
            f"cannot inspect output path {output}: {error}"
        ) from error

    if is_link_or_reparse(output_info) or not stat.S_ISDIR(output_info.st_mode):
        raise ContractError(
            f"output exists and is not an ordinary directory: {output}"
        )
    if not force:
        raise ContractError(
            f"output already exists; use --force to replace it: {output}"
        )

    try:
        empty = next(output.iterdir(), None) is None
    except OSError as error:
        raise ContractError(
            f"cannot inspect output directory {output}: {error}"
        ) from error
    if empty:
        return True

    marker = output / "source-package.json"
    try:
        marker_info = marker.lstat()
    except OSError as error:
        raise ContractError(
            "--force requires a direct source-package.json ownership marker "
            f"in a non-empty output directory: {marker}: {error}"
        ) from error
    if is_link_or_reparse(marker_info) or not stat.S_ISREG(marker_info.st_mode):
        raise ContractError(
            "--force requires a direct regular non-symlink/non-reparse "
            f"source-package.json ownership marker: {marker}"
        )
    return True


def publish_candidate(
    candidate: Path,
    output: Path,
    force: bool,
) -> None:
    output_exists = inspect_replacement_target(output, force)

    backup: Path | None = None
    try:
        if output_exists:
            backup = output.with_name(
                f".{output.name}.backup-{uuid.uuid4().hex}"
            )
            output.replace(backup)
        candidate.replace(output)
    except KeyboardInterrupt:
        if backup is not None and path_exists(backup):
            try:
                restore_backup(output, backup)
            except OSError as restore_error:
                raise ContractError(
                    "interrupted publication could not restore the previous "
                    f"output; backup retained at {backup}: {restore_error}"
                ) from restore_error
        raise
    except OSError as error:
        if backup is not None and path_exists(backup):
            try:
                restore_backup(output, backup)
            except OSError as restore_error:
                raise ContractError(
                    "cannot publish candidate and cannot restore the previous "
                    f"output: publish={error}; restore={restore_error}"
                ) from restore_error
        raise ContractError(f"cannot publish candidate: {error}") from error

    if backup is not None:
        try:
            remove_path(backup)
        except OSError as error:
            print(
                "warning: reconstruction committed but backup remains at "
                f"{backup}: {error}",
                file=sys.stderr,
            )


def extract_command(args: argparse.Namespace) -> dict[str, Any]:
    source = args.pdf.resolve()
    if not args.output.name:
        raise ContractError("--output must name a package directory")
    if (
        sys.platform == "win32"
        and args.output.name.rstrip(" .") != args.output.name
    ):
        raise ContractError(
            "--output directory name must not end in a dot or space on Windows"
        )
    output = args.output.parent.resolve() / args.output.name
    if path_exists(output) and (output.is_symlink() or output.is_junction()):
        raise ContractError("output must not be a symlink or junction")
    if source_is_within_output(source, output):
        raise ContractError("source PDF must be outside the output directory")
    if sys.platform == "win32" and path_exists(output):
        try:
            output = output.resolve(strict=True)
        except OSError as error:
            raise ContractError(
                f"cannot resolve existing output directory: {output}: {error}"
            ) from error
    if path_exists(output) and not args.force:
        raise ContractError(
            f"output already exists; use --force to replace it: {output}"
        )

    candidate = create_candidate(output)
    try:
        manifest, selected_count = build_candidate(args, candidate)
        candidate_manifest = candidate / "source-package.json"
        validation = package_validation_report(candidate_manifest, source=None)
        if not validation["valid"]:
            raise ContractError(
                "candidate package validation failed: "
                + "; ".join(validation["errors"])
            )
        publish_candidate(candidate, output, args.force)
        return {
            "command": "extract",
            "valid": True,
            "status": manifest["status"],
            "errors": [],
            "manifest": str(output / "source-package.json"),
            "package_id": manifest["package_id"],
            "selected_pages": selected_count,
            "published": True,
        }
    finally:
        if path_exists(candidate):
            try:
                remove_path(candidate)
            except OSError as error:
                raise ContractError(
                    f"cannot clean candidate directory: {error}"
                ) from error


def validate_command(args: argparse.Namespace) -> tuple[dict[str, Any], int]:
    report = package_validation_report(
        args.package.resolve(),
        args.source,
    )
    report["command"] = "validate"
    return report, 0 if report["valid"] else 1


def build_parser() -> argparse.ArgumentParser:
    parser = JsonArgumentParser(
        description=(
            "Extract and validate a contracted scholarly PDF source package."
        )
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    extract = subparsers.add_parser(
        "extract",
        help="Extract selected PDF pages into a source package.",
    )
    extract.add_argument("--pdf", type=Path, required=True)
    extract.add_argument("--output", type=Path, required=True)
    extract.add_argument(
        "--pages",
        required=True,
        help="One-based pages and ascending ranges, for example 1-12,15.",
    )
    extract.add_argument("--rights-note", required=True)
    extract.add_argument("--sections", type=Path)
    extract.add_argument("--figure-map", type=Path)
    extract.add_argument(
        "--profile",
        action="append",
        default=[],
        help="Declare a profile; repeat the option for multiple profiles.",
    )
    extract.add_argument(
        "--force",
        action="store_true",
        help=(
            "Replace an empty output directory, or a non-empty directory with "
            "a direct regular non-symlink/non-reparse source-package.json "
            "ownership marker."
        ),
    )

    validate = subparsers.add_parser(
        "validate",
        help="Validate a source package and optionally replay its source.",
    )
    validate.add_argument("--package", type=Path, required=True)
    validate.add_argument("--source", type=Path)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    try:
        args = parser.parse_args(argv)
    except ContractError as error:
        arguments = argv if argv is not None else sys.argv[1:]
        command = (
            arguments[0]
            if arguments and arguments[0] in {"extract", "validate"}
            else "cli"
        )
        emit_json(
            {
                "command": command,
                "valid": False,
                "status": "fail",
                "errors": [str(error)],
            }
        )
        return 2
    try:
        if args.command == "extract":
            emit_json(extract_command(args))
            return 0
        report, exit_code = validate_command(args)
        emit_json(report)
    except ContractError as error:
        emit_json(
            {
                "command": args.command,
                "valid": False,
                "status": "fail",
                "errors": [str(error)],
            }
        )
        return 2
    except PYMUPDF_ERRORS as error:
        emit_json(
            {
                "command": args.command,
                "valid": False,
                "status": "fail",
                "errors": [f"malformed PDF content: {error}"],
            }
        )
        return 2
    except KeyboardInterrupt:
        emit_json(
            {
                "command": args.command,
                "valid": False,
                "status": "fail",
                "errors": ["interrupted"],
            }
        )
        return 2
    else:
        return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
