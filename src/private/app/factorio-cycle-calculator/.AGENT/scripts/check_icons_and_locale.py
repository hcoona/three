#!/usr/bin/env python3
"""Check icon paths and localization entries for selected prototypes.

This script intentionally avoids loading the full data-raw-dump.json into
memory. It uses jq to extract only the needed prototypes.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Iterable

DEFAULT_DATA_RAW = (
    "/mnt/c/Users/zhang/AppData/Roaming/Factorio/"
    "script-output/data-raw-dump.json"
)


@dataclass(frozen=True)
class Target:
    """Describe a prototype to verify."""

    proto_type: str
    name: str
    locale_sections: tuple[str, ...]


@dataclass(frozen=True)
class CheckContext:
    """Hold shared context for checks."""

    data_raw: Path
    data_dir: Path
    locale_map: dict[str, dict[str, str]]


TARGETS = [
    Target("recipe", "advanced-oil-processing", ("recipe-name",)),
    Target("recipe", "heavy-oil-cracking", ("recipe-name",)),
    Target("recipe", "light-oil-cracking", ("recipe-name",)),
    Target("fluid", "crude-oil", ("fluid-name",)),
    Target("fluid", "heavy-oil", ("fluid-name",)),
    Target("fluid", "light-oil", ("fluid-name",)),
    Target("fluid", "petroleum-gas", ("fluid-name",)),
    Target("item", "oil-refinery", ("item-name",)),
    Target("item", "chemical-plant", ("item-name",)),
    Target("item", "biochamber", ("item-name",)),
    Target("assembling-machine", "oil-refinery", ("entity-name",)),
    Target("assembling-machine", "chemical-plant", ("entity-name",)),
    Target("assembling-machine", "biochamber", ("entity-name",)),
    Target("item-group", "intermediate-products", ("item-group-name",)),
    Target("item-subgroup", "fluid-recipes", ("item-subgroup-name",)),
]

ICON_TOKEN_RE = re.compile(r"__([^/]+)__/(.+)")


def run_jq(data_raw: Path, proto_type: str, name: str) -> dict | None:
    """Run jq to extract a minimal prototype payload."""
    jq = shutil.which("jq")
    if not jq:
        print("ERROR: jq not found in PATH.", file=sys.stderr)
        return None

    jq_filter = ".[ $t ][ $n ] | {name, type, icon, icon_size, icons}"
    cmd = [
        jq,
        "-c",
        "--arg",
        "t",
        proto_type,
        "--arg",
        "n",
        name,
        jq_filter,
        str(data_raw),
    ]
    try:
        result = subprocess.run(  # noqa: S603
            cmd, check=True, capture_output=True, text=True
        )
    except subprocess.CalledProcessError as exc:
        print(
            f"ERROR: jq failed for {proto_type}/{name}: {exc.stderr}",
            file=sys.stderr,
        )
        return None

    output = result.stdout.strip()
    if not output or output == "null":
        return None

    try:
        return json.loads(output)
    except json.JSONDecodeError:
        print(
            f"ERROR: failed to parse jq output for {proto_type}/{name}",
            file=sys.stderr,
        )
        return None


def resolve_icon_path(icon_path: str, data_dir: Path) -> Path:
    """Resolve Factorio icon paths with mod token expansion."""
    match = ICON_TOKEN_RE.match(icon_path)
    if match:
        mod_name = match.group(1)
        rel_path = match.group(2)
        return data_dir / mod_name / rel_path
    return data_dir / icon_path.lstrip("/")


def read_png_size(path: Path) -> tuple[int, int] | None:
    """Read PNG dimensions from the file header."""
    if path.suffix.lower() != ".png":
        return None
    try:
        with path.open("rb") as handle:
            signature = handle.read(8)
            if signature != b"\x89PNG\r\n\x1a\n":
                return None
            _length = int.from_bytes(handle.read(4), "big")
            chunk_type = handle.read(4)
            if chunk_type != b"IHDR":
                return None
            width = int.from_bytes(handle.read(4), "big")
            height = int.from_bytes(handle.read(4), "big")
            return width, height
    except OSError:
        return None


def parse_locale_file(path: Path) -> dict[str, dict[str, str]]:
    """Parse a locale .cfg file into a nested section map."""
    data: dict[str, dict[str, str]] = {}
    section: str | None = None
    try:
        for raw_line in path.read_text(
            encoding="utf-8", errors="ignore"
        ).splitlines():
            line = raw_line.strip()
            if not line or line.startswith((";", "#")):
                continue
            if line.startswith("[") and line.endswith("]"):
                section = line[1:-1].strip()
                data.setdefault(section, {})
                continue
            if section and "=" in line:
                key, value = line.split("=", 1)
                data[section][key.strip()] = value.strip()
    except OSError:
        return {}
    return data


def load_locale(data_dir: Path, language: str) -> dict[str, dict[str, str]]:
    """Load locale entries from all mods for the requested language."""
    merged: dict[str, dict[str, str]] = {}
    if not data_dir.exists():
        return merged

    for mod_dir in sorted(data_dir.iterdir()):
        if not mod_dir.is_dir():
            continue
        locale_dir = mod_dir / "locale" / language
        if not locale_dir.is_dir():
            continue
        for cfg in sorted(locale_dir.glob("*.cfg")):
            parsed = parse_locale_file(cfg)
            for section, entries in parsed.items():
                merged.setdefault(section, {})
                merged[section].update(entries)
    return merged


def extract_icon_paths(payload: dict) -> list[str]:
    """Collect icon paths from icon and icons fields."""
    icons: list[str] = []
    if payload.get("icon"):
        icons.append(payload["icon"])
    if payload.get("icons"):
        for entry in payload["icons"]:
            if isinstance(entry, dict) and entry.get("icon"):
                icons.append(entry["icon"])
    return icons


def find_locale(
    locale_map: dict[str, dict[str, str]],
    sections: Iterable[str],
    key: str,
) -> tuple[str, str] | None:
    """Find a localized name in the first matching section."""
    for section in sections:
        value = locale_map.get(section, {}).get(key)
        if value:
            return section, value
    return None


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(
        description=(
            "Check icon paths and locale entries for selected prototypes."
        )
    )
    parser.add_argument(
        "--data-raw",
        default=DEFAULT_DATA_RAW,
        help="Path to data-raw-dump.json",
    )
    parser.add_argument(
        "--data-dir",
        default=os.environ.get("FACTORIO_DATA_DIR"),
        help="Factorio data directory (contains base/, space-age/, etc.)",
    )
    parser.add_argument(
        "--locale", default="en", help="Locale to load (default: en)"
    )
    return parser.parse_args()


def ensure_data_raw(data_raw: Path) -> Path | None:
    """Validate the data-raw-dump.json path."""
    if not data_raw.exists():
        print(
            f"ERROR: data-raw-dump.json not found: {data_raw}",
            file=sys.stderr,
        )
        return None
    return data_raw


def ensure_data_dir(data_dir: str | None) -> Path | None:
    """Validate the Factorio data directory path."""
    if not data_dir:
        print(
            "ERROR: --data-dir is required (or set FACTORIO_DATA_DIR).",
            file=sys.stderr,
        )
        return None
    resolved = Path(data_dir)
    if not resolved.exists():
        print(f"ERROR: data directory not found: {resolved}", file=sys.stderr)
        return None
    return resolved


def check_target(context: CheckContext, target: Target) -> tuple[int, int]:
    """Check icon and locale entries for a single prototype."""
    payload = run_jq(context.data_raw, target.proto_type, target.name)
    if not payload:
        print(
            f"SKIP: {target.proto_type}/{target.name} "
            "not found in data-raw-dump.json"
        )
        return 0, 0

    icon_failures = 0
    locale_failures = 0

    icon_paths = extract_icon_paths(payload)
    if not icon_paths:
        print(f"ICON: {target.proto_type}/{target.name}: NO ICON")
    else:
        for icon in icon_paths:
            resolved = resolve_icon_path(icon, context.data_dir)
            if resolved.exists():
                size = read_png_size(resolved)
                size_text = f" ({size[0]}x{size[1]})" if size else ""
                print(
                    f"ICON: {target.proto_type}/{target.name}: OK "
                    f"{resolved}{size_text}"
                )
            else:
                icon_failures += 1
                print(
                    f"ICON: {target.proto_type}/{target.name}: "
                    f"MISSING {resolved}"
                )

    locale_hit = find_locale(
        context.locale_map, target.locale_sections, target.name
    )
    if locale_hit:
        section, value = locale_hit
        print(f"LOCALE: {section}/{target.name}: OK {value}")
    else:
        locale_failures += 1
        sections = ",".join(target.locale_sections)
        print(f"LOCALE: {sections}/{target.name}: MISSING")

    return icon_failures, locale_failures


def summarize_failures(icon_failures: int, locale_failures: int) -> int:
    """Print the summary line and return an exit code."""
    if icon_failures or locale_failures:
        print(
            "DONE: icon failures="
            f"{icon_failures}, locale failures={locale_failures}"
        )
        return 1
    print("DONE: all icons and locales resolved")
    return 0


def main() -> int:
    """Run the icon and locale verification workflow."""
    args = parse_args()
    data_raw = ensure_data_raw(Path(args.data_raw))
    if not data_raw:
        return 2

    data_dir = ensure_data_dir(args.data_dir)
    if not data_dir:
        return 2

    locale_map = load_locale(data_dir, args.locale)
    if not locale_map:
        print(f"WARNING: no locale data found for language: {args.locale}")

    context = CheckContext(
        data_raw=data_raw,
        data_dir=data_dir,
        locale_map=locale_map,
    )
    icon_failures = 0
    locale_failures = 0

    for target in TARGETS:
        icon_delta, locale_delta = check_target(context, target)
        icon_failures += icon_delta
        locale_failures += locale_delta

    return summarize_failures(icon_failures, locale_failures)


if __name__ == "__main__":
    raise SystemExit(main())
