"""Parsing and loading services for Factorio data-raw payloads."""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import MISSING, fields, is_dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any, TypeVar, cast

from factorio_cycle_calculator.models.generated_models import (
    AssemblingMachinePrototype,
    BeaconPrototype,
    FactorioDataRaw,
    FluidPrototype,
    ItemPrototype,
    ModulePrototype,
    RecipePrototype,
)

T = TypeVar("T")

MIN_LIST_ENTRY_LEN = 2

_DATACLASS_FIELD_CACHE: dict[
    type[Any],
    tuple[frozenset[str], frozenset[str]],
] = {}


def normalize_proto_type(proto_type: str) -> str:
    """Normalize data-raw prototype type keys for dataclass access."""
    return proto_type.replace("-", "_")


def get_payload_value(payload: object, key: str) -> object:
    """Retrieve a value from a dict or dataclass payload."""
    if isinstance(payload, Mapping):
        return payload.get(key)
    if is_dataclass(payload):
        if hasattr(payload, key):
            return getattr(payload, key)
        extra = getattr(payload, "extra", None)
        if isinstance(extra, Mapping):
            return extra.get(key)
    return None


def coerce_float(value: object, default: float = 0.0) -> float:
    """Coerce a value into a float, returning a default on failure."""
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        try:
            return float(value)
        except ValueError:
            return default
    return default


def parse_int_string(value: str) -> int | None:
    """Parse an int from a string, handling float-like values."""
    try:
        return int(value)
    except ValueError:
        try:
            return int(float(value))
        except ValueError:
            return None


def coerce_int(value: object, default: int = 0) -> int:
    """Coerce a value into an int, returning a default on failure."""
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return int(value)
    if isinstance(value, str):
        parsed = parse_int_string(value)
        if parsed is not None:
            return parsed
    return default


def get_dataclass_field_info(
    cls: type[Any],
) -> tuple[frozenset[str], frozenset[str]]:
    """Cache dataclass field names and required fields per class."""
    cached = _DATACLASS_FIELD_CACHE.get(cls)
    if cached is not None:
        return cached
    dataclass_fields = fields(cast("Any", cls))
    field_names = frozenset(field.name for field in dataclass_fields)
    required_fields = frozenset(
        field.name
        for field in dataclass_fields
        if field.default is MISSING and field.default_factory is MISSING
    )
    _DATACLASS_FIELD_CACHE[cls] = (field_names, required_fields)
    return field_names, required_fields


def coerce_dataclass(  # noqa: UP047
    cls: type[T],
    payload: Mapping[str, object],
) -> T | None:
    """Coerce a payload mapping into a generated dataclass."""
    field_names, required_fields = get_dataclass_field_info(cls)
    values = {name: payload[name] for name in field_names if name in payload}
    if any(name not in payload for name in required_fields):
        return None
    try:
        instance = cls(**values)
    except TypeError:
        return None
    extras = {
        key: value for key, value in payload.items() if key not in field_names
    }
    if extras:
        cast("Any", instance).extra = extras
    return instance


def parse_prototype_map(  # noqa: UP047
    section: Mapping[str, object] | None,
    cls: type[T],
) -> dict[str, T]:
    """Parse a data-raw section into a mapping of dataclass prototypes."""
    if not isinstance(section, Mapping):
        return {}
    parsed: dict[str, T] = {}
    for name, payload in section.items():
        if not isinstance(payload, Mapping):
            continue
        proto = coerce_dataclass(cls, payload)
        if proto is None:
            continue
        parsed[name] = proto
    return parsed


def build_data_raw(raw: Mapping[str, Mapping[str, object]]) -> FactorioDataRaw:
    """Build a typed data-raw container from the raw JSON payload."""
    assembling_machine = parse_prototype_map(
        raw.get("assembling-machine"),
        AssemblingMachinePrototype,
    )
    module = parse_prototype_map(raw.get("module"), ModulePrototype)
    beacon = parse_prototype_map(raw.get("beacon"), BeaconPrototype)
    recipe = parse_prototype_map(raw.get("recipe"), RecipePrototype)
    item = parse_prototype_map(raw.get("item"), ItemPrototype)
    fluid = parse_prototype_map(raw.get("fluid"), FluidPrototype)
    return FactorioDataRaw(
        assembling_machine=assembling_machine or None,
        module=module or None,
        beacon=beacon or None,
        recipe=recipe or None,
        item=item or None,
        fluid=fluid or None,
    )


@lru_cache(maxsize=8)
def load_data_raw(data_raw_path: str) -> FactorioDataRaw | None:
    """Load data-raw-dump.json into a typed container."""
    try:
        with Path(data_raw_path).open("r", encoding="utf-8") as handle:
            raw = json.load(handle)
    except (OSError, json.JSONDecodeError, ValueError):
        return None
    if not isinstance(raw, Mapping):
        return None
    return build_data_raw(raw)


def get_prototype(
    data_raw: FactorioDataRaw | Mapping[str, Mapping[str, object]],
    proto_type: str,
    name: str,
) -> object | None:
    """Fetch a prototype from data-raw by type and name."""
    if is_dataclass(data_raw):
        section = getattr(data_raw, normalize_proto_type(proto_type), None)
        if isinstance(section, Mapping):
            return section.get(name)
        return None
    if isinstance(data_raw, Mapping):
        return data_raw.get(proto_type, {}).get(name)
    return None


def parse_amount(entry: object) -> float:
    """Parse an amount from a recipe ingredient/result entry."""
    amount = get_payload_value(entry, "amount")
    if amount is not None:
        return coerce_float(amount)
    amount_min = get_payload_value(entry, "amount_min")
    amount_max = get_payload_value(entry, "amount_max")
    if amount_min is not None and amount_max is not None:
        return (coerce_float(amount_min) + coerce_float(amount_max)) / 2.0
    if amount_min is not None:
        return coerce_float(amount_min)
    if amount_max is not None:
        return coerce_float(amount_max)
    return 0.0


def parse_ingredient_list(
    entries: list[object],
) -> dict[tuple[str, str], float]:
    """Parse a list of ingredient-like entries into a typed map."""
    parsed: dict[tuple[str, str], float] = {}
    for entry in entries:
        if isinstance(entry, list) and len(entry) >= MIN_LIST_ENTRY_LEN:
            name = entry[0]
            amount = coerce_float(entry[1])
            proto_type = "item"
        elif isinstance(entry, Mapping) or is_dataclass(entry):
            name = get_payload_value(entry, "name")
            amount = parse_amount(entry)
            proto_type = get_payload_value(entry, "type") or "item"
            if not isinstance(proto_type, str):
                proto_type = "item"
        else:
            continue
        if not isinstance(name, str):
            continue
        key = (proto_type, name)
        parsed[key] = parsed.get(key, 0.0) + amount
    return parsed


def parse_results(
    proto: object,
) -> tuple[dict[tuple[str, str], float], frozenset[tuple[str, str]]]:
    """Parse recipe results into a typed map and ignored-by-productivity set."""
    results: dict[tuple[str, str], float] = {}
    ignored: set[tuple[str, str]] = set()
    results_entries = get_payload_value(proto, "results")
    if isinstance(results_entries, list):
        for entry in results_entries:
            name = get_payload_value(entry, "name")
            if not isinstance(name, str):
                continue
            amount = parse_amount(entry)
            probability = get_payload_value(entry, "probability")
            if probability is None:
                probability = 1.0
            amount *= coerce_float(probability, default=1.0)
            proto_type = get_payload_value(entry, "type") or "item"
            if not isinstance(proto_type, str):
                proto_type = "item"
            key = (proto_type, name)
            results[key] = results.get(key, 0.0) + amount
            if get_payload_value(entry, "ignored_by_productivity"):
                ignored.add(key)
        return results, frozenset(ignored)

    result_name = get_payload_value(proto, "result")
    if isinstance(result_name, str):
        amount = coerce_float(
            get_payload_value(proto, "result_count") or 1.0,
            default=1.0,
        )
        results[("item", result_name)] = amount
    return results, frozenset(ignored)
