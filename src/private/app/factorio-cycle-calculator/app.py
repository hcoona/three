"""Streamlit example for the Factorio oil-processing chain."""

from __future__ import annotations

import io
import json
import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, Protocol, Self

import streamlit as st
from ortools.linear_solver import pywraplp
from PIL import Image

if TYPE_CHECKING:
    from collections.abc import Mapping
    from types import TracebackType

DEFAULT_DATA_DIR = (
    "/mnt/c/Program Files (x86)/Steam/steamapps/common/Factorio/data"
)
DEFAULT_DATA_RAW = (
    "/mnt/c/Users/zhang/AppData/Roaming/Factorio/"
    "script-output/data-raw-dump.json"
)

ICON_TOKEN_RE = re.compile(r"__([^/]+)__/(.+)")

FORMAT_MILLION = 1_000_000.0
FORMAT_THOUSAND = 1_000.0
FORMAT_TEN = 10.0
FLOW_EPSILON = 1e-6
MIN_LIST_ENTRY_LEN = 2


@dataclass(frozen=True)
class Machine:
    """Describe a crafting machine and its capabilities."""

    key: str
    label: str
    crafting_speed: float
    allow_productivity: bool
    crafting_categories: tuple[str, ...]


@dataclass(frozen=True)
class Recipe:
    """Describe a recipe and its inputs/outputs."""

    key: str
    label: str
    category: str
    energy_required: float
    ingredients: Mapping[tuple[str, str], float]
    results: Mapping[tuple[str, str], float]
    allow_productivity: bool


@dataclass(frozen=True)
class EffectSettings:
    """Hold module and beacon bonuses."""

    speed_bonus: float
    productivity_bonus: float
    beacon_speed_bonus: float


@dataclass(frozen=True)
class RecipeConfig:
    """Store a recipe's chosen machine and effects."""

    machine: Machine
    effects: EffectSettings


@dataclass(frozen=True)
class FlowRates:
    """Store per-second production and consumption rates."""

    production: dict[tuple[str, str], float]
    consumption: dict[tuple[str, str], float]


@dataclass(frozen=True)
class SolveResult:
    """Capture the solver output for display."""

    status: str
    machine_counts: dict[str, float]
    net_flows_per_s: dict[tuple[str, str], float]
    objective_value: float | None


@dataclass(frozen=True)
class IconSpec:
    """Hold a resolved icon path and its intended size."""

    path: Path
    size: int | None


class ContainerSlot(Protocol):
    """Define the container slot API used by the UI."""

    def container(self) -> ContainerSlot:
        """Return a context manager for nested rendering."""
        ...

    def __enter__(self) -> Self:
        """Enter the container context."""
        ...

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> bool | None:
        """Exit the container context."""
        ...


class CaptionSlot(Protocol):
    """Define the caption API used by the UI."""

    def caption(self, body: str) -> None:
        """Render a caption string."""
        ...


class MarkdownSlot(Protocol):
    """Define the markdown API used by the UI."""

    def markdown(self, body: str) -> None:
        """Render a markdown string."""
        ...


@dataclass(frozen=True)
class RenderState:
    """Bundle UI state for rendering outputs."""

    recipes: Mapping[str, Recipe]
    config_map: Mapping[str, RecipeConfig]
    row_slots: Mapping[str, tuple[ContainerSlot, ContainerSlot, ContainerSlot]]
    count_slots: Mapping[str, CaptionSlot]
    products_slot: ContainerSlot
    byproducts_slot: ContainerSlot
    ingredients_slot: ContainerSlot
    status_slot: MarkdownSlot
    unit_multiplier: float
    unit_label: str
    icon_catalog: Mapping[tuple[str, str], IconSpec]
    recipe_order: tuple[str, str, str]


def resolve_icon_path(icon_path: str, data_dir: Path) -> Path:
    """Resolve a Factorio icon path against the data directory."""
    match = ICON_TOKEN_RE.match(icon_path)
    if match:
        mod_name = match.group(1)
        rel_path = match.group(2)
        return data_dir / mod_name / rel_path
    return data_dir / icon_path.lstrip("/")


def extract_icon_from_payload(payload: dict) -> tuple[str | None, int | None]:
    """Extract a single icon path and size from a prototype payload."""
    icon_path = payload.get("icon")
    icon_size = payload.get("icon_size")
    if isinstance(icon_size, float):
        icon_size = int(icon_size)
    if not isinstance(icon_size, int):
        icon_size = None
    if isinstance(icon_path, str):
        return icon_path, icon_size
    icons = payload.get("icons")
    if isinstance(icons, list):
        for entry in icons:
            if isinstance(entry, dict) and entry.get("icon"):
                entry_size = entry.get("icon_size")
                if isinstance(entry_size, float):
                    entry_size = int(entry_size)
                if not isinstance(entry_size, int):
                    entry_size = icon_size
                return str(entry["icon"]), entry_size
    return None, icon_size


@st.cache_data(show_spinner=False)
def load_data_raw(data_raw_path: str) -> dict:
    """Load data-raw-dump.json into memory."""
    try:
        with Path(data_raw_path).open("r", encoding="utf-8") as handle:
            return json.load(handle)
    except OSError:
        return {}


def get_prototype(
    data_raw: Mapping[str, Mapping[str, dict]],
    proto_type: str,
    name: str,
) -> dict | None:
    """Fetch a prototype from data-raw by type and name."""
    return data_raw.get(proto_type, {}).get(name)


def parse_amount(entry: dict) -> float:
    """Parse an amount from a recipe ingredient/result entry."""
    if "amount" in entry:
        return float(entry["amount"])
    amount_min = entry.get("amount_min")
    amount_max = entry.get("amount_max")
    if amount_min is not None and amount_max is not None:
        return (float(amount_min) + float(amount_max)) / 2.0
    if amount_min is not None:
        return float(amount_min)
    if amount_max is not None:
        return float(amount_max)
    return 0.0


def parse_ingredient_list(
    entries: list[object],
) -> dict[tuple[str, str], float]:
    """Parse a list of ingredient-like entries into a typed map."""
    parsed: dict[tuple[str, str], float] = {}
    for entry in entries:
        if isinstance(entry, list) and len(entry) >= MIN_LIST_ENTRY_LEN:
            name = entry[0]
            amount = float(entry[1])
            proto_type = "item"
        elif isinstance(entry, dict):
            name = entry.get("name")
            amount = parse_amount(entry)
            proto_type = entry.get("type", "item")
        else:
            continue
        if not isinstance(name, str):
            continue
        key = (proto_type, name)
        parsed[key] = parsed.get(key, 0.0) + amount
    return parsed


def parse_results(proto: dict) -> dict[tuple[str, str], float]:
    """Parse recipe results into a typed map."""
    results: dict[tuple[str, str], float] = {}
    if "results" in proto and isinstance(proto["results"], list):
        for entry in proto["results"]:
            if not isinstance(entry, dict):
                continue
            name = entry.get("name")
            if not isinstance(name, str):
                continue
            amount = parse_amount(entry)
            probability = entry.get("probability", 1.0)
            amount *= float(probability)
            proto_type = entry.get("type", "item")
            key = (proto_type, name)
            results[key] = results.get(key, 0.0) + amount
        return results

    result_name = proto.get("result")
    if isinstance(result_name, str):
        amount = float(proto.get("result_count", 1.0))
        results[("item", result_name)] = amount
    return results


def build_recipe_from_proto(name: str, proto: dict) -> Recipe:
    """Build a Recipe instance from a data-raw recipe prototype."""
    energy_required = proto.get("energy_required")
    if energy_required is None:
        energy_required = 0.5
    category = proto.get("category", "crafting")
    ingredients = parse_ingredient_list(proto.get("ingredients", []))
    results = parse_results(proto)
    allow_productivity = bool(proto.get("allow_productivity", False))
    return Recipe(
        key=name,
        label=name.replace("-", " ").title(),
        category=str(category),
        energy_required=float(energy_required),
        ingredients=ingredients,
        results=results,
        allow_productivity=allow_productivity,
    )


def build_machine_catalog(
    data_raw: Mapping[str, Mapping[str, dict]],
) -> dict[str, Machine]:
    """Build a machine catalog from data-raw assembling machines."""
    catalog: dict[str, Machine] = {}
    for name, proto in data_raw.get("assembling-machine", {}).items():
        crafting_speed = float(proto.get("crafting_speed", 1.0))
        crafting_categories = tuple(proto.get("crafting_categories", []))
        allowed_effects = proto.get("allowed_effects", [])
        allow_productivity = "productivity" in allowed_effects
        if not allow_productivity:
            base_effect = (proto.get("effect_receiver") or {}).get(
                "base_effect", {}
            )
            allow_productivity = bool(base_effect.get("productivity", 0))
        catalog[name] = Machine(
            key=name,
            label=name.replace("-", " ").title(),
            crafting_speed=crafting_speed,
            allow_productivity=allow_productivity,
            crafting_categories=crafting_categories,
        )
    return catalog


def build_recipe_catalog(
    data_raw: Mapping[str, Mapping[str, dict]],
    recipe_keys: tuple[str, str, str],
) -> dict[str, Recipe]:
    """Build recipe definitions for the selected chain."""
    catalog: dict[str, Recipe] = {}
    for recipe_key in recipe_keys:
        proto = get_prototype(data_raw, "recipe", recipe_key)
        if not proto:
            continue
        catalog[recipe_key] = build_recipe_from_proto(recipe_key, proto)
    return catalog


def list_recipe_names_by_category(
    data_raw: Mapping[str, Mapping[str, dict]],
    category: str,
) -> list[str]:
    """List recipe names matching a category."""
    matches = []
    for name, proto in data_raw.get("recipe", {}).items():
        if proto.get("category", "crafting") == category:
            matches.append(name)
    return sorted(matches)


def select_recipe_option(
    label: str,
    options: list[str],
    *,
    default_name: str,
) -> str:
    """Select a recipe name from options with a preferred default."""
    if not options:
        st.sidebar.warning(f"No recipes found for {label}.")
        return ""
    index = options.index(default_name) if default_name in options else 0
    return st.sidebar.selectbox(label, options=options, index=index)


def build_icon_catalog(
    data_raw: Mapping[str, Mapping[str, dict]],
    data_dir_path: str,
    recipes: Mapping[str, Recipe],
    machines: Mapping[str, Machine],
) -> dict[tuple[str, str], IconSpec]:
    """Build the icon catalog for recipes, machines, and flows."""
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
        if not proto:
            continue
        icon_path, icon_size = extract_icon_from_payload(proto)
        if not icon_path:
            continue
        resolved = resolve_icon_path(icon_path, data_dir)
        if resolved.exists():
            catalog[(proto_type, name)] = IconSpec(
                path=resolved,
                size=icon_size,
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


@st.cache_data(show_spinner=False)
def load_icon_image(path: str, size: int | None) -> bytes | None:
    """Load and crop an icon image to a single square."""
    try:
        image = Image.open(path)
    except OSError:
        return None

    image = image.convert("RGBA")
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


def format_amount(value: float) -> str:
    """Format a number using compact k/M suffixes."""
    abs_value = abs(value)
    if abs_value >= FORMAT_MILLION:
        return f"{value / FORMAT_MILLION:.2f}M"
    if abs_value >= FORMAT_THOUSAND:
        return f"{value / FORMAT_THOUSAND:.1f}k"
    if abs_value >= FORMAT_TEN:
        return f"{value:.1f}"
    return f"{value:.2f}"


def render_icon_label(icon_spec: IconSpec | None, label: str) -> None:
    """Render an icon with a label beneath it."""
    if icon_spec:
        image_data = load_icon_image(str(icon_spec.path), icon_spec.size)
        if image_data:
            st.image(image_data, width=32)
    st.caption(label)


def render_icon_amount_list(
    items: list[tuple[str, str, float]],
    icon_catalog: Mapping[tuple[str, str], IconSpec],
    *,
    unit_label: str,
) -> None:
    """Render a horizontal list of icon + amount pairs."""
    if not items:
        st.caption("—")
        return
    columns = st.columns(len(items))
    for col, (proto_type, name, amount) in zip(
        columns,
        items,
        strict=False,
    ):
        with col:
            icon_path = find_icon(
                icon_catalog,
                (proto_type,),
                name=name,
            )
            if icon_path:
                image_data = load_icon_image(
                    str(icon_path.path),
                    icon_path.size,
                )
                if image_data:
                    st.image(image_data, width=28)
            st.caption(f"{format_amount(amount)} {unit_label}")
            st.caption(name.replace("-", " ").title())


def render_summary_panel(
    title: str,
    items: list[tuple[str, str, float]],
    icon_catalog: Mapping[tuple[str, str], IconSpec],
    *,
    unit_label: str,
) -> None:
    """Render one of the summary panels (products/byproducts/ingredients)."""
    st.markdown(f"**{title}**")
    render_icon_amount_list(items, icon_catalog, unit_label=unit_label)


def accumulate_flows(
    recipes: Mapping[str, Recipe],
    configs: Mapping[str, RecipeConfig],
    counts: Mapping[str, float],
) -> tuple[dict[tuple[str, str], float], dict[tuple[str, str], float]]:
    """Accumulate total per-second production and consumption."""
    production: dict[tuple[str, str], float] = {}
    consumption: dict[tuple[str, str], float] = {}
    for recipe_key, recipe in recipes.items():
        rates = per_machine_rates(recipe, configs[recipe_key])
        count = counts.get(recipe_key, 0.0)
        for flow_key, rate in rates.production.items():
            production[flow_key] = production.get(flow_key, 0.0) + rate * count
        for flow_key, rate in rates.consumption.items():
            consumption[flow_key] = (
                consumption.get(flow_key, 0.0) + rate * count
            )
    return production, consumption


def build_summary_items(
    production: Mapping[tuple[str, str], float],
    consumption: Mapping[tuple[str, str], float],
    *,
    unit_multiplier: float,
) -> tuple[
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
]:
    """Split net flows into product, byproduct, and ingredient lists."""
    net: dict[tuple[str, str], float] = {}
    keys = set(production) | set(consumption)
    for flow_key in keys:
        net[flow_key] = production.get(flow_key, 0.0) - consumption.get(
            flow_key, 0.0
        )

    products: list[tuple[str, str, float]] = []
    byproducts: list[tuple[str, str, float]] = []
    ingredients: list[tuple[str, str, float]] = []

    for flow_key, value in sorted(net.items()):
        scaled = value * unit_multiplier
        if scaled > FLOW_EPSILON:
            if flow_key == ("fluid", "petroleum-gas"):
                products.append((flow_key[0], flow_key[1], scaled))
            else:
                byproducts.append((flow_key[0], flow_key[1], scaled))
        elif scaled < -FLOW_EPSILON:
            ingredients.append((flow_key[0], flow_key[1], abs(scaled)))

    return products, byproducts, ingredients


def build_recipe_rows(
    recipe: Recipe,
    config: RecipeConfig,
    *,
    count: float,
    unit_multiplier: float,
) -> tuple[
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
]:
    """Build per-recipe product, byproduct, and ingredient lists."""
    rates = per_machine_rates(recipe, config)
    production = {
        flow_key: rate * count * unit_multiplier
        for flow_key, rate in rates.production.items()
    }
    consumption = {
        flow_key: rate * count * unit_multiplier
        for flow_key, rate in rates.consumption.items()
    }

    primary: tuple[str, str] | None = None
    if ("fluid", "petroleum-gas") in recipe.results:
        primary = ("fluid", "petroleum-gas")
    elif len(recipe.results) == 1:
        primary = next(iter(recipe.results.keys()))
    products: list[tuple[str, str, float]] = []
    byproducts: list[tuple[str, str, float]] = []

    for flow_key, value in production.items():
        if primary and flow_key != primary:
            byproducts.append((flow_key[0], flow_key[1], value))
        else:
            products.append((flow_key[0], flow_key[1], value))

    ingredients = [
        (flow_key[0], flow_key[1], value)
        for flow_key, value in consumption.items()
    ]

    return products, byproducts, ingredients


def render_sidebar_controls() -> tuple[str, str, float, bool, float, str]:
    """Render sidebar controls and return selected values."""
    st.sidebar.header("Assets")
    data_dir_path = st.sidebar.text_input(
        "Factorio data directory",
        value=os.environ.get("FACTORIO_DATA_DIR", DEFAULT_DATA_DIR),
    )
    data_raw_path = st.sidebar.text_input(
        "data-raw-dump.json path",
        value=os.environ.get("FACTORIO_DATA_RAW", DEFAULT_DATA_RAW),
    )

    st.sidebar.header("Demand")
    demand_pg_per_min = st.sidebar.number_input(
        "Petroleum gas target (per minute)",
        min_value=0.0,
        value=900.0,
        step=30.0,
    )
    force_integer = st.sidebar.checkbox(
        "Force integer machine counts",
        value=False,
    )
    rate_unit = st.sidebar.radio(
        "Rate unit",
        options=["per minute", "per second"],
        index=0,
    )
    unit_multiplier = 60.0 if rate_unit == "per minute" else 1.0
    unit_label = "per min" if rate_unit == "per minute" else "per s"

    return (
        data_dir_path,
        data_raw_path,
        demand_pg_per_min,
        force_integer,
        unit_multiplier,
        unit_label,
    )


def render_recipe_selection(
    data_raw: Mapping[str, Mapping[str, dict]],
) -> tuple[str, str, str] | None:
    """Render recipe selectors for the oil chain."""
    st.sidebar.header("Recipes")
    oil_processing = list_recipe_names_by_category(data_raw, "oil-processing")
    chemistry = list_recipe_names_by_category(data_raw, "organic-or-chemistry")
    advanced_key = select_recipe_option(
        "Oil processing recipe",
        oil_processing,
        default_name="advanced-oil-processing",
    )
    heavy_key = select_recipe_option(
        "Heavy oil cracking recipe",
        chemistry,
        default_name="heavy-oil-cracking",
    )
    light_key = select_recipe_option(
        "Light oil cracking recipe",
        chemistry,
        default_name="light-oil-cracking",
    )
    if not advanced_key or not heavy_key or not light_key:
        return None
    return advanced_key, heavy_key, light_key


def render_summary_placeholders() -> tuple[
    ContainerSlot, ContainerSlot, ContainerSlot
]:
    """Render the summary placeholder panels and return their slots."""
    summary_container = st.container()
    summary_cols = summary_container.columns(3)
    return (
        summary_cols[0].empty(),
        summary_cols[1].empty(),
        summary_cols[2].empty(),
    )


def render_production_header() -> None:
    """Render the production table header row."""
    header_cols = st.columns([1.4, 1.8, 1.6, 0.9, 2.4, 2.4, 2.4])
    header_labels = [
        "Recipe",
        "Machine",
        "Modules / Beacons",
        "Power",
        "Products",
        "Byproducts",
        "Ingredients",
    ]
    for col, label in zip(header_cols, header_labels, strict=False):
        col.caption(label)


def render_production_rows(
    recipes: Mapping[str, Recipe],
    machines: Mapping[str, Machine],
    icon_catalog: Mapping[tuple[str, str], IconSpec],
    recipe_order: tuple[str, str, str],
) -> tuple[
    dict[str, RecipeConfig],
    dict[str, tuple[ContainerSlot, ContainerSlot, ContainerSlot]],
    dict[str, CaptionSlot],
]:
    """Render production rows and return configs and row placeholders."""
    config_map: dict[str, RecipeConfig] = {}
    row_slots: dict[
        str,
        tuple[ContainerSlot, ContainerSlot, ContainerSlot],
    ] = {}
    count_slots: dict[str, CaptionSlot] = {}

    for recipe_key in recipe_order:
        recipe = recipes[recipe_key]
        eligible = [
            key
            for key, machine in machines.items()
            if recipe.category in machine.crafting_categories
        ]
        if not eligible:
            eligible = list(machines.keys())
        cols = st.columns([1.4, 1.8, 1.6, 0.9, 2.4, 2.4, 2.4])

        with cols[0]:
            recipe_icon = find_icon(
                icon_catalog,
                ("recipe",),
                name=recipe.key,
            )
            render_icon_label(recipe_icon, recipe.label)

        with cols[1]:
            machine = render_machine_selector(
                recipe,
                machines,
                eligible,
            )
            machine_icon = find_icon(
                icon_catalog,
                ("item", "assembling-machine"),
                name=machine.key,
            )
            render_icon_label(machine_icon, machine.label)
            count_slots[recipe_key] = st.empty()

        with cols[2]:
            effects = render_effect_controls(recipe)

        with cols[3]:
            st.caption("—")

        with cols[4]:
            products_cell = st.empty()

        with cols[5]:
            byproducts_cell = st.empty()

        with cols[6]:
            ingredients_cell = st.empty()

        config_map[recipe_key] = RecipeConfig(machine=machine, effects=effects)
        row_slots[recipe_key] = (
            products_cell,
            byproducts_cell,
            ingredients_cell,
        )

    return config_map, row_slots, count_slots


def render_solution(result: SolveResult, state: RenderState) -> None:
    """Render the solved output and update placeholders."""
    production, consumption = accumulate_flows(
        state.recipes,
        state.config_map,
        result.machine_counts,
    )
    crude_input = (
        consumption.get(("fluid", "crude-oil"), 0.0) * state.unit_multiplier
    )
    status_line = (
        f"Solver status: **{result.status}** • "
        f"Crude input: {format_amount(crude_input)} {state.unit_label}"
    )
    state.status_slot.markdown(status_line)
    products, byproducts, ingredients = build_summary_items(
        production,
        consumption,
        unit_multiplier=state.unit_multiplier,
    )

    with state.products_slot.container():
        render_summary_panel(
            "Products",
            products,
            state.icon_catalog,
            unit_label=state.unit_label,
        )
    with state.byproducts_slot.container():
        render_summary_panel(
            "Byproducts",
            byproducts,
            state.icon_catalog,
            unit_label=state.unit_label,
        )
    with state.ingredients_slot.container():
        render_summary_panel(
            "Ingredients",
            ingredients,
            state.icon_catalog,
            unit_label=state.unit_label,
        )

    for recipe_key in state.recipe_order:
        recipe = state.recipes[recipe_key]
        count = result.machine_counts.get(recipe_key, 0.0)
        state.count_slots[recipe_key].caption(f"Count: {format_amount(count)}")
        products, byproducts, ingredients = build_recipe_rows(
            recipe,
            state.config_map[recipe_key],
            count=count,
            unit_multiplier=state.unit_multiplier,
        )
        (
            products_cell,
            byproducts_cell,
            ingredients_cell,
        ) = state.row_slots[recipe_key]
        with products_cell.container():
            render_icon_amount_list(
                products,
                state.icon_catalog,
                unit_label=state.unit_label,
            )
        with byproducts_cell.container():
            render_icon_amount_list(
                byproducts,
                state.icon_catalog,
                unit_label=state.unit_label,
            )
        with ingredients_cell.container():
            render_icon_amount_list(
                ingredients,
                state.icon_catalog,
                unit_label=state.unit_label,
            )


def machine_label(machine: Machine) -> str:
    """Format machine labels for UI controls."""
    return f"{machine.label} (speed {machine.crafting_speed:g})"


def compute_effective_speed(machine: Machine, effects: EffectSettings) -> float:
    """Compute the effective crafting speed after bonuses."""
    bonus = effects.speed_bonus + effects.beacon_speed_bonus
    return machine.crafting_speed * (1.0 + bonus)


def per_machine_rates(recipe: Recipe, config: RecipeConfig) -> FlowRates:
    """Compute per-second production and consumption for one machine."""
    effective_speed = compute_effective_speed(config.machine, config.effects)
    cycle_seconds = recipe.energy_required / effective_speed
    productivity = 0.0
    if recipe.allow_productivity and config.machine.allow_productivity:
        productivity = config.effects.productivity_bonus
    multiplier = 1.0 + productivity

    production = {
        key: amount * multiplier / cycle_seconds
        for key, amount in recipe.results.items()
    }
    consumption = {
        key: amount / cycle_seconds
        for key, amount in recipe.ingredients.items()
    }
    return FlowRates(production=production, consumption=consumption)


def build_solver(*, force_integer: bool) -> pywraplp.Solver | None:
    """Create the OR-Tools solver instance."""
    solver_name = "CBC_MIXED_INTEGER_PROGRAMMING" if force_integer else "GLOP"
    return pywraplp.Solver.CreateSolver(solver_name)


def solve_chain(
    demand_pg_per_s: float,
    recipes: Mapping[str, Recipe],
    configs: Mapping[str, RecipeConfig],
    *,
    force_integer: bool,
    recipe_order: tuple[str, str, str],
) -> SolveResult | None:
    """Solve the oil-processing chain to meet petroleum gas demand."""
    solver = build_solver(force_integer=force_integer)
    if solver is None:
        st.error("OR-Tools solver is not available in this environment.")
        return None

    for recipe_key, recipe in recipes.items():
        config = configs[recipe_key]
        effective_speed = compute_effective_speed(
            config.machine, config.effects
        )
        if effective_speed <= 0.0:
            st.error(
                "Effective crafting speed must be positive. "
                "Check module and beacon bonuses."
            )
            return None
        if recipe.energy_required <= 0.0:
            st.error("Recipe energy_required must be positive.")
            return None

    rates = {
        recipe_key: per_machine_rates(recipe, configs[recipe_key])
        for recipe_key, recipe in recipes.items()
    }

    variables: dict[str, pywraplp.Variable] = {}
    for recipe_key in recipes:
        if force_integer:
            variables[recipe_key] = solver.IntVar(
                0.0, solver.infinity(), recipe_key
            )
        else:
            variables[recipe_key] = solver.NumVar(
                0.0, solver.infinity(), recipe_key
            )

    advanced_key, heavy_key, light_key = recipe_order

    heavy_prod = rates[advanced_key].production.get(
        ("fluid", "heavy-oil"),
        0.0,
    )
    heavy_cons = rates[heavy_key].consumption.get(
        ("fluid", "heavy-oil"),
        0.0,
    )
    solver.Add(
        heavy_prod * variables[advanced_key]  # type: ignore[operator]
        == heavy_cons * variables[heavy_key]  # type: ignore[operator]
    )

    light_prod_advanced = rates[advanced_key].production.get(
        ("fluid", "light-oil"),
        0.0,
    )
    light_prod_from_heavy = rates[heavy_key].production.get(
        ("fluid", "light-oil"),
        0.0,
    )
    light_cons = rates[light_key].consumption.get(
        ("fluid", "light-oil"),
        0.0,
    )
    solver.Add(
        light_prod_advanced * variables[advanced_key]  # type: ignore[operator]
        + light_prod_from_heavy * variables[heavy_key]  # type: ignore[operator]
        == light_cons * variables[light_key]  # type: ignore[operator]
    )

    pg_prod_advanced = rates[advanced_key].production.get(
        ("fluid", "petroleum-gas"),
        0.0,
    )
    pg_prod_from_light = rates[light_key].production.get(
        ("fluid", "petroleum-gas"),
        0.0,
    )
    solver.Add(
        pg_prod_advanced * variables[advanced_key]  # type: ignore[operator]
        + pg_prod_from_light * variables[light_key]  # type: ignore[operator]
        >= demand_pg_per_s
    )

    objective_terms = []
    for recipe_key in recipes:
        crude_rate = rates[recipe_key].consumption.get(
            ("fluid", "crude-oil"),
            0.0,
        )
        if crude_rate > 0.0:
            objective_terms.append(
                crude_rate * variables[recipe_key]  # type: ignore[operator]
            )
    solver.Minimize(solver.Sum(objective_terms))
    status_code = solver.Solve()

    status_map = {
        pywraplp.Solver.OPTIMAL: "Optimal",
        pywraplp.Solver.FEASIBLE: "Feasible",
        pywraplp.Solver.INFEASIBLE: "Infeasible",
        pywraplp.Solver.UNBOUNDED: "Unbounded",
        pywraplp.Solver.ABNORMAL: "Abnormal",
        pywraplp.Solver.NOT_SOLVED: "Not solved",
    }
    status = status_map.get(status_code, "Unknown")

    machine_counts = {
        recipe_key: variables[recipe_key].solution_value()
        for recipe_key in recipes
    }
    net_flows = {
        ("fluid", "heavy-oil"): heavy_prod * machine_counts[advanced_key]
        - heavy_cons * machine_counts[heavy_key],
        ("fluid", "light-oil"): light_prod_advanced
        * machine_counts[advanced_key]
        + light_prod_from_heavy * machine_counts[heavy_key]
        - light_cons * machine_counts[light_key],
        ("fluid", "petroleum-gas"): pg_prod_advanced
        * machine_counts[advanced_key]
        + pg_prod_from_light * machine_counts[light_key],
    }

    objective_value: float | None
    if status_code in {pywraplp.Solver.OPTIMAL, pywraplp.Solver.FEASIBLE}:
        objective_value = solver.Objective().Value()
    else:
        objective_value = None

    return SolveResult(
        status=status,
        machine_counts=machine_counts,
        net_flows_per_s=net_flows,
        objective_value=objective_value,
    )


def render_effect_controls(recipe: Recipe) -> EffectSettings:
    """Render module and beacon controls for a recipe."""
    use_modules = st.checkbox(
        "Modules",
        key=f"{recipe.key}-modules",
        help="Provide total speed/productivity bonuses from modules.",
    )
    speed_bonus = 0.0
    productivity_bonus = 0.0
    if use_modules:
        module_cols = st.columns(2)
        speed_bonus = module_cols[0].number_input(
            "Speed %",
            min_value=0.0,
            value=0.0,
            step=5.0,
            key=f"{recipe.key}-module-speed",
        )
        productivity_bonus = module_cols[1].number_input(
            "Productivity %",
            min_value=0.0,
            value=0.0,
            step=1.0,
            key=f"{recipe.key}-module-prod",
        )

    use_beacons = st.checkbox(
        "Beacons",
        key=f"{recipe.key}-beacons",
        help="Provide a total speed bonus from beacons.",
    )
    beacon_speed_bonus = 0.0
    if use_beacons:
        beacon_speed_bonus = st.number_input(
            "Beacon speed %",
            min_value=0.0,
            value=0.0,
            step=5.0,
            key=f"{recipe.key}-beacon-speed",
        )

    return EffectSettings(
        speed_bonus=speed_bonus / 100.0,
        productivity_bonus=productivity_bonus / 100.0,
        beacon_speed_bonus=beacon_speed_bonus / 100.0,
    )


def render_machine_selector(
    recipe: Recipe,
    machines: Mapping[str, Machine],
    machine_keys: list[str],
) -> Machine:
    """Render the machine selector for a recipe."""
    options = [machines[key] for key in machine_keys]
    return st.selectbox(
        "Machine",
        options=options,
        format_func=machine_label,
        key=f"{recipe.key}-machine",
        label_visibility="collapsed",
    )


def main() -> None:
    """Run the Streamlit UI for the oil-processing example."""
    st.set_page_config(page_title="Factorio Cycle Calculator", layout="wide")
    st.title("Factorio Cycle Calculator")
    st.markdown(
        "This example models the advanced oil processing chain using "
        "Google OR-Tools. Choose machines and bonuses, then solve for the "
        "machine counts that satisfy a petroleum gas demand while minimizing "
        "crude oil input (water is treated as a free input)."
    )

    (
        data_dir_path,
        data_raw_path,
        demand_pg_per_min,
        force_integer,
        unit_multiplier,
        unit_label,
    ) = render_sidebar_controls()

    data_raw = load_data_raw(data_raw_path)
    if not data_raw:
        st.error("Failed to load data-raw-dump.json.")
        return

    recipe_order = render_recipe_selection(data_raw)
    if recipe_order is None:
        st.error("Recipe selection is incomplete.")
        return

    machines = build_machine_catalog(data_raw)
    if not machines:
        st.error("No assembling machines found in data-raw.")
        return

    recipes = build_recipe_catalog(data_raw, recipe_order)
    if len(recipes) != len(recipe_order):
        st.error("Some selected recipes were not found in data-raw.")
        return

    icon_catalog = build_icon_catalog(
        data_raw,
        data_dir_path,
        recipes,
        machines,
    )
    if not icon_catalog:
        st.sidebar.warning(
            "Icons were not resolved. Check your data directory paths."
        )

    products_slot, byproducts_slot, ingredients_slot = (
        render_summary_placeholders()
    )
    status_slot = st.empty()

    st.subheader("Production")
    render_production_header()
    config_map, row_slots, count_slots = render_production_rows(
        recipes,
        machines,
        icon_catalog,
        recipe_order,
    )

    demand_pg_per_s = demand_pg_per_min / 60.0
    result = solve_chain(
        demand_pg_per_s,
        recipes,
        config_map,
        force_integer=force_integer,
        recipe_order=recipe_order,
    )

    if result is None:
        return

    state = RenderState(
        recipes=recipes,
        config_map=config_map,
        row_slots=row_slots,
        count_slots=count_slots,
        products_slot=products_slot,
        byproducts_slot=byproducts_slot,
        ingredients_slot=ingredients_slot,
        status_slot=status_slot,
        unit_multiplier=unit_multiplier,
        unit_label=unit_label,
        icon_catalog=icon_catalog,
        recipe_order=recipe_order,
    )
    render_solution(result, state)


main()
