"""Catalog builders for recipes, machines, modules and beacons."""

from __future__ import annotations

from collections.abc import Mapping
from typing import cast

from factorio_cycle_calculator.models import (
    BeaconSpec,
    EffectSettings,
    Machine,
    ModuleSpec,
    Recipe,
)
from factorio_cycle_calculator.models.generated_models import (
    AssemblingMachinePrototype,
    BeaconPrototype,
    FactorioDataRaw,
    ModulePrototype,
    RecipePrototype,
)
from factorio_cycle_calculator.services.data_raw_service import (
    coerce_float,
    coerce_int,
    get_payload_value,
    get_prototype,
    parse_ingredient_list,
    parse_results,
)


def normalize_allowed_effects(raw: object) -> frozenset[str]:
    """Normalize allowed effects lists to a usable set."""
    if isinstance(raw, (list, tuple, set)):
        allowed = {str(value) for value in raw if isinstance(value, str)}
    else:
        allowed = set()
    if not allowed:
        allowed = {"speed", "productivity"}
    return frozenset(allowed)


def parse_effect_bonus(effect: object) -> float:
    """Parse a module effect bonus payload."""
    bonus: object
    if isinstance(effect, Mapping):
        bonus = get_payload_value(effect, "bonus") or 0.0
    elif isinstance(effect, (float, int)):
        bonus = effect
    else:
        bonus = 0.0
    return coerce_float(bonus)


def build_recipe_from_proto(name: str, proto: RecipePrototype) -> Recipe:
    """Build a Recipe instance from a data-raw recipe prototype."""
    energy_required = (
        proto.energy_required if proto.energy_required is not None else 0.5
    )
    category = proto.category or "crafting"
    ingredients_list = (
        proto.ingredients if isinstance(proto.ingredients, list) else []
    )
    ingredients = parse_ingredient_list(ingredients_list)
    results, ignored_by_productivity = parse_results(proto)
    allow_productivity = bool(proto.allow_productivity or False)
    return Recipe(
        key=name,
        label=name.replace("-", " ").title(),
        category=str(category),
        energy_required=coerce_float(energy_required),
        ingredients=ingredients,
        results=results,
        allow_productivity=allow_productivity,
        ignored_by_productivity=ignored_by_productivity,
    )


def build_machine_catalog(data_raw: FactorioDataRaw) -> dict[str, Machine]:
    """Build a machine catalog from data-raw assembling machines."""
    catalog: dict[str, Machine] = {}
    machine_map = cast(
        "Mapping[str, object]", data_raw.assembling_machine or {}
    )
    for name, proto in machine_map.items():
        proto = cast("AssemblingMachinePrototype", proto)
        crafting_speed = coerce_float(proto.crafting_speed, default=1.0)
        if isinstance(proto.crafting_categories, list):
            crafting_categories = tuple(
                str(category)
                for category in proto.crafting_categories
                if isinstance(category, str)
            )
        else:
            crafting_categories = ()
        allowed_effects = normalize_allowed_effects(proto.allowed_effects)
        effect_receiver = proto.effect_receiver
        base_effect = get_payload_value(effect_receiver, "base_effect")
        base_productivity = parse_effect_bonus(
            get_payload_value(base_effect, "productivity") or 0.0
        )
        allow_productivity = (
            "productivity" in allowed_effects or base_productivity > 0.0
        )
        module_slots = coerce_int(proto.module_slots, default=0)
        catalog[name] = Machine(
            key=name,
            label=name.replace("-", " ").title(),
            crafting_speed=crafting_speed,
            allow_productivity=allow_productivity,
            base_productivity=base_productivity,
            crafting_categories=crafting_categories,
            module_slots=module_slots,
            allowed_effects=allowed_effects,
        )
    return catalog


def build_module_catalog(data_raw: FactorioDataRaw) -> dict[str, ModuleSpec]:
    """Build a module catalog from data-raw module items."""
    catalog: dict[str, ModuleSpec] = {}
    module_map = cast("Mapping[str, object]", data_raw.module or {})
    for name, proto in module_map.items():
        proto = cast("ModulePrototype", proto)
        effects = proto.effect
        speed_bonus = parse_effect_bonus(get_payload_value(effects, "speed"))
        productivity_bonus = parse_effect_bonus(
            get_payload_value(effects, "productivity")
        )
        limitation_raw = get_payload_value(proto, "limitation") or []
        if not isinstance(limitation_raw, list):
            limitation_raw = []
        limitation = frozenset(limitation_raw)
        limitation_blacklist_raw = (
            get_payload_value(proto, "limitation_blacklist") or []
        )
        if not isinstance(limitation_blacklist_raw, list):
            limitation_blacklist_raw = []
        limitation_blacklist = frozenset(limitation_blacklist_raw)
        catalog[name] = ModuleSpec(
            key=name,
            label=name.replace("-", " ").title(),
            speed_bonus=speed_bonus,
            productivity_bonus=productivity_bonus,
            limitation=limitation,
            limitation_blacklist=limitation_blacklist,
        )
    return catalog


def build_beacon_catalog(data_raw: FactorioDataRaw) -> dict[str, BeaconSpec]:
    """Build a beacon catalog from data-raw beacon entities."""
    catalog: dict[str, BeaconSpec] = {}
    beacon_map = cast("Mapping[str, object]", data_raw.beacon or {})
    for name, proto in beacon_map.items():
        proto = cast("BeaconPrototype", proto)
        module_slots = coerce_int(proto.module_slots, default=0)
        effectivity = coerce_float(proto.distribution_effectivity, default=1.0)
        allowed_effects = normalize_allowed_effects(proto.allowed_effects)
        catalog[name] = BeaconSpec(
            key=name,
            label=name.replace("-", " ").title(),
            module_slots=module_slots,
            distribution_effectivity=effectivity,
            allowed_effects=allowed_effects,
        )
    return catalog


def select_default_beacon(
    beacons: Mapping[str, BeaconSpec],
) -> BeaconSpec | None:
    """Select the default beacon spec (prefer base beacon)."""
    if not beacons:
        return None
    if "beacon" in beacons:
        return beacons["beacon"]
    return beacons[sorted(beacons.keys())[0]]


def build_recipe_catalog(
    data_raw: FactorioDataRaw,
    recipe_keys: tuple[str, str, str],
) -> dict[str, Recipe]:
    """Build recipe definitions for the selected chain."""
    catalog: dict[str, Recipe] = {}
    for recipe_key in recipe_keys:
        proto = get_prototype(data_raw, "recipe", recipe_key)
        if not isinstance(proto, RecipePrototype):
            continue
        catalog[recipe_key] = build_recipe_from_proto(recipe_key, proto)
    return catalog


def list_recipe_names_by_category(
    data_raw: FactorioDataRaw,
    category: str,
) -> list[str]:
    """List recipe names matching a category."""
    matches = []
    recipe_map = cast("Mapping[str, object]", data_raw.recipe or {})
    for name, proto in recipe_map.items():
        proto_category = get_payload_value(proto, "category") or "crafting"
        if proto_category == category:
            matches.append(name)
    return sorted(matches)


def module_matches_recipe(module: ModuleSpec, recipe: Recipe) -> bool:
    """Check module limitation lists against a recipe."""
    if module.limitation and recipe.key not in module.limitation:
        return False
    return not (
        module.limitation_blacklist
        and recipe.key in module.limitation_blacklist
    )


def filter_modules_for_machine(
    modules: Mapping[str, ModuleSpec],
    *,
    recipe: Recipe,
    machine: Machine,
    allowed_effects: frozenset[str],
) -> list[ModuleSpec]:
    """Filter modules based on recipe, machine, and allowed effects."""
    filtered: list[ModuleSpec] = []
    for module in modules.values():
        if module.speed_bonus == 0.0 and module.productivity_bonus == 0.0:
            continue
        if module.productivity_bonus != 0.0 and (
            not recipe.allow_productivity or not machine.allow_productivity
        ):
            continue
        if module.speed_bonus != 0.0 and "speed" not in allowed_effects:
            continue
        if (
            module.productivity_bonus != 0.0
            and "productivity" not in allowed_effects
        ):
            continue
        if not module_matches_recipe(module, recipe):
            continue
        filtered.append(module)
    return sorted(filtered, key=lambda item: item.label)


def compute_module_effects(
    module: ModuleSpec | None,
    *,
    count: int,
) -> tuple[float, float]:
    """Compute total module effects for a machine."""
    if module is None or count <= 0:
        return 0.0, 0.0
    return module.speed_bonus * count, module.productivity_bonus * count


def compute_beacon_effects(
    module: ModuleSpec | None,
    *,
    module_count: int,
    beacon_count: int,
    effectivity: float,
) -> tuple[float, float]:
    """Compute total beacon effects applied to a machine."""
    if (
        module is None
        or module_count <= 0
        or beacon_count <= 0
        or effectivity <= 0.0
    ):
        return 0.0, 0.0
    factor = module_count * beacon_count * effectivity
    return module.speed_bonus * factor, module.productivity_bonus * factor


def build_effect_settings(
    *,
    module_speed: float,
    module_productivity: float,
    beacon_speed: float,
    beacon_productivity: float,
) -> EffectSettings:
    """Build aggregated effect settings."""
    return EffectSettings(
        speed_bonus=module_speed + beacon_speed,
        productivity_bonus=module_productivity + beacon_productivity,
    )
