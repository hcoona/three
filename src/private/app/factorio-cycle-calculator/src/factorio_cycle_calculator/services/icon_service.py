"""Icon loading and resolution helpers."""

from __future__ import annotations

import io
import re
from collections.abc import Mapping
from functools import lru_cache
from pathlib import Path

from PIL import Image

from factorio_cycle_calculator.models import (
    FactorioDataRaw,
    IconSpec,
    Machine,
    Recipe,
)
from factorio_cycle_calculator.services.data_raw_service import (
    get_payload_value,
    get_prototype,
)

ICON_TOKEN_RE = re.compile(r"__([^/]+)__/(.+)")


def resolve_icon_path(icon_path: str, data_dir: Path) -> Path:
    """Resolve a Factorio icon path against the data directory."""
    match = ICON_TOKEN_RE.match(icon_path)
    if match:
        mod_name = match.group(1)
        rel_path = match.group(2)
        return data_dir / mod_name / rel_path
    return data_dir / icon_path.lstrip("/")


def extract_icon_from_payload(payload: object) -> tuple[str | None, int | None]:
    """Extract a single icon path and size from a prototype payload."""
    icon_path = get_payload_value(payload, "icon")
    icon_size = get_payload_value(payload, "icon_size")
    if isinstance(icon_size, float):
        icon_size = int(icon_size)
    if not isinstance(icon_size, int):
        icon_size = None
    if isinstance(icon_path, str):
        return icon_path, icon_size
    icons = get_payload_value(payload, "icons")
    if isinstance(icons, list):
        for entry in icons:
            entry_icon = get_payload_value(entry, "icon")
            if not entry_icon:
                continue
            entry_size = get_payload_value(entry, "icon_size")
            if isinstance(entry_size, float):
                entry_size = int(entry_size)
            if not isinstance(entry_size, int):
                entry_size = icon_size
            return str(entry_icon), entry_size
    return None, icon_size


def build_icon_catalog(
    data_raw: FactorioDataRaw,
    data_dir_path: str,
    recipes: Mapping[str, Recipe],
    machines: Mapping[str, Machine],
) -> dict[tuple[str, str], IconSpec]:
    """Build icon catalog for recipes, machines and flow items."""
    if not data_dir_path:
        return {}
    data_dir = Path(data_dir_path)
    if not data_dir.exists():
        return {}

    icon_keys: set[tuple[str, str]] = set()
    for recipe in recipes.values():
        icon_keys.add(("recipe", recipe.key))
        icon_keys.update(recipe.ingredients.keys())
        icon_keys.update(recipe.results.keys())

    for machine in machines.values():
        icon_keys.add(("assembling-machine", machine.key))
        icon_keys.add(("item", machine.key))

    catalog: dict[tuple[str, str], IconSpec] = {}
    for proto_type, name in icon_keys:
        proto = get_prototype(data_raw, proto_type, name)
        if proto is None:
            continue
        icon_path, icon_size = extract_icon_from_payload(proto)
        if not icon_path:
            continue
        resolved = resolve_icon_path(icon_path, data_dir)
        if resolved.exists():
            catalog[(proto_type, name)] = IconSpec(
                path=resolved, size=icon_size
            )
    return catalog


def find_icon(
    catalog: Mapping[tuple[str, str], IconSpec],
    proto_types: tuple[str, ...],
    *,
    name: str,
) -> IconSpec | None:
    """Find the first matching icon in the catalog."""
    for proto_type in proto_types:
        icon = catalog.get((proto_type, name))
        if icon:
            return icon
    return None


@lru_cache(maxsize=256)
def load_icon_image(path: str, size: int | None) -> bytes | None:
    """Load and crop an icon image to a single square."""
    try:
        with Image.open(path) as image_file:
            image = image_file.convert("RGBA")
            width, height = image.size
            target_size = size
            if target_size is None and width != height:
                target_size = min(width, height)
            if target_size:
                target_size = min(target_size, width, height)
                image = image.crop((0, 0, target_size, target_size))

            with io.BytesIO() as buffer:
                image.save(buffer, format="PNG")
                return buffer.getvalue()
    except OSError:
        return None
