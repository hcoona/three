"""Streamlit example for the Factorio oil-processing chain."""

from __future__ import annotations

import io
import json
import os
import re
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING

import streamlit as st
from ortools.linear_solver import pywraplp
from PIL import Image

if TYPE_CHECKING:
    from collections.abc import Mapping

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


@dataclass(frozen=True)
class Machine:
    """Describe a crafting machine and its capabilities."""

    key: str
    label: str
    crafting_speed: float
    allow_productivity: bool


@dataclass(frozen=True)
class Recipe:
    """Describe a recipe and its inputs/outputs."""

    key: str
    label: str
    energy_required: float
    ingredients: Mapping[str, float]
    results: Mapping[str, float]
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

    production: dict[str, float]
    consumption: dict[str, float]


@dataclass(frozen=True)
class SolveResult:
    """Capture the solver output for display."""

    status: str
    machine_counts: dict[str, float]
    net_flows_per_s: dict[str, float]
    objective_value: float | None


@dataclass(frozen=True)
class IconTarget:
    """Describe an icon to load for the UI."""

    proto_type: str
    name: str
    fallback: str | None


@dataclass(frozen=True)
class IconSpec:
    """Hold a resolved icon path and its intended size."""

    path: Path
    size: int | None


@dataclass(frozen=True)
class RenderState:
    """Bundle UI state for rendering outputs."""

    recipes: Mapping[str, Recipe]
    config_map: Mapping[str, RecipeConfig]
    row_slots: Mapping[str, tuple]
    count_slots: Mapping[str, object]
    products_slot: object
    byproducts_slot: object
    ingredients_slot: object
    status_slot: object
    unit_multiplier: float
    unit_label: str
    icon_catalog: Mapping[tuple[str, str], IconSpec]


ICON_TARGETS = [
    IconTarget(
        "recipe",
        "advanced-oil-processing",
        "__base__/graphics/icons/fluid/advanced-oil-processing.png",
    ),
    IconTarget(
        "recipe",
        "heavy-oil-cracking",
        "__base__/graphics/icons/fluid/heavy-oil-cracking.png",
    ),
    IconTarget(
        "recipe",
        "light-oil-cracking",
        "__base__/graphics/icons/fluid/light-oil-cracking.png",
    ),
    IconTarget(
        "fluid",
        "crude-oil",
        "__base__/graphics/icons/fluid/crude-oil.png",
    ),
    IconTarget(
        "fluid",
        "heavy-oil",
        "__base__/graphics/icons/fluid/heavy-oil.png",
    ),
    IconTarget(
        "fluid",
        "light-oil",
        "__base__/graphics/icons/fluid/light-oil.png",
    ),
    IconTarget(
        "fluid",
        "water",
        "__base__/graphics/icons/fluid/water.png",
    ),
    IconTarget(
        "fluid",
        "petroleum-gas",
        "__base__/graphics/icons/fluid/petroleum-gas.png",
    ),
    IconTarget(
        "item",
        "oil-refinery",
        "__base__/graphics/icons/oil-refinery.png",
    ),
    IconTarget(
        "item",
        "chemical-plant",
        "__base__/graphics/icons/chemical-plant.png",
    ),
    IconTarget(
        "item",
        "biochamber",
        "__space-age__/graphics/icons/biochamber.png",
    ),
    IconTarget(
        "assembling-machine",
        "oil-refinery",
        "__base__/graphics/icons/oil-refinery.png",
    ),
    IconTarget(
        "assembling-machine",
        "chemical-plant",
        "__base__/graphics/icons/chemical-plant.png",
    ),
    IconTarget(
        "assembling-machine",
        "biochamber",
        "__space-age__/graphics/icons/biochamber.png",
    ),
]

PRIMARY_OUTPUTS = {
    "advanced-oil-processing": "petroleum-gas",
    "heavy-oil-cracking": "light-oil",
    "light-oil-cracking": "petroleum-gas",
}

RECIPE_ORDER = (
    "advanced-oil-processing",
    "heavy-oil-cracking",
    "light-oil-cracking",
)


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


def query_icon_from_data_raw(
    data_raw: Path,
    *,
    proto_type: str,
    name: str,
) -> tuple[str | None, int | None]:
    """Query a prototype icon from data-raw-dump.json using jq."""
    jq = shutil.which("jq")
    if not jq:
        return None, None

    jq_filter = ".[ $t ][ $n ] | {icon, icons}"
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
    result = None
    try:
        result = subprocess.run(  # noqa: S603
            cmd,
            check=True,
            capture_output=True,
            text=True,
        )
    except subprocess.CalledProcessError:
        result = None

    icon_path: str | None = None
    icon_size: int | None = None
    if result:
        output = result.stdout.strip()
        if output and output != "null":
            try:
                payload = json.loads(output)
            except json.JSONDecodeError:
                payload = None
            if isinstance(payload, dict):
                icon_path, icon_size = extract_icon_from_payload(payload)

    return icon_path, icon_size


@st.cache_data(show_spinner=False)
def load_icon_catalog(
    data_raw_path: str,
    data_dir_path: str,
) -> dict[tuple[str, str], IconSpec]:
    """Load a catalog of resolved icon paths."""
    if not data_dir_path:
        return {}

    data_dir = Path(data_dir_path)
    if not data_dir.exists():
        return {}

    data_raw = Path(data_raw_path) if data_raw_path else None
    catalog: dict[tuple[str, str], IconSpec] = {}

    for target in ICON_TARGETS:
        icon_path: str | None = None
        icon_size: int | None = None
        if data_raw and data_raw.exists():
            icon_path, icon_size = query_icon_from_data_raw(
                data_raw,
                proto_type=target.proto_type,
                name=target.name,
            )
        if not icon_path:
            icon_path = target.fallback
        if icon_path:
            resolved = resolve_icon_path(icon_path, data_dir)
            if resolved.exists():
                catalog[(target.proto_type, target.name)] = IconSpec(
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
) -> tuple[dict[str, float], dict[str, float]]:
    """Accumulate total per-second production and consumption."""
    production: dict[str, float] = {}
    consumption: dict[str, float] = {}
    for recipe_key, recipe in recipes.items():
        rates = per_machine_rates(recipe, configs[recipe_key])
        count = counts.get(recipe_key, 0.0)
        for fluid, rate in rates.production.items():
            production[fluid] = production.get(fluid, 0.0) + rate * count
        for fluid, rate in rates.consumption.items():
            consumption[fluid] = consumption.get(fluid, 0.0) + rate * count
    return production, consumption


def build_summary_items(
    production: Mapping[str, float],
    consumption: Mapping[str, float],
    *,
    unit_multiplier: float,
) -> tuple[
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
    list[tuple[str, str, float]],
]:
    """Split net flows into product, byproduct, and ingredient lists."""
    net: dict[str, float] = {}
    keys = set(production) | set(consumption)
    for fluid in keys:
        net[fluid] = production.get(fluid, 0.0) - consumption.get(fluid, 0.0)

    products: list[tuple[str, str, float]] = []
    byproducts: list[tuple[str, str, float]] = []
    ingredients: list[tuple[str, str, float]] = []

    for fluid, value in sorted(net.items()):
        scaled = value * unit_multiplier
        if scaled > FLOW_EPSILON:
            if fluid == "petroleum-gas":
                products.append(("fluid", fluid, scaled))
            else:
                byproducts.append(("fluid", fluid, scaled))
        elif scaled < -FLOW_EPSILON:
            ingredients.append(("fluid", fluid, abs(scaled)))

    return products, byproducts, ingredients


def build_recipe_rows(
    recipe_key: str,
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
        fluid: rate * count * unit_multiplier
        for fluid, rate in rates.production.items()
    }
    consumption = {
        fluid: rate * count * unit_multiplier
        for fluid, rate in rates.consumption.items()
    }

    primary = PRIMARY_OUTPUTS.get(recipe_key)
    products: list[tuple[str, str, float]] = []
    byproducts: list[tuple[str, str, float]] = []

    for fluid, value in production.items():
        if primary and fluid != primary:
            byproducts.append(("fluid", fluid, value))
        else:
            products.append(("fluid", fluid, value))

    ingredients = [
        ("fluid", fluid, value) for fluid, value in consumption.items()
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


def render_summary_placeholders() -> tuple[object, object, object]:
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
) -> tuple[dict[str, RecipeConfig], dict[str, tuple], dict[str, object]]:
    """Render production rows and return configs and row placeholders."""
    config_map: dict[str, RecipeConfig] = {}
    row_slots: dict[str, tuple] = {}
    count_slots: dict[str, object] = {}

    machine_choices = {
        "advanced-oil-processing": ["oil-refinery"],
        "heavy-oil-cracking": ["chemical-plant", "biochamber"],
        "light-oil-cracking": ["chemical-plant", "biochamber"],
    }

    for recipe_key in RECIPE_ORDER:
        recipe = recipes[recipe_key]
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
                machine_choices[recipe_key],
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
    crude_input = consumption.get("crude-oil", 0.0) * state.unit_multiplier
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

    for recipe_key in RECIPE_ORDER:
        recipe = state.recipes[recipe_key]
        count = result.machine_counts.get(recipe_key, 0.0)
        state.count_slots[recipe_key].caption(f"Count: {format_amount(count)}")
        products, byproducts, ingredients = build_recipe_rows(
            recipe_key,
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


def build_machines() -> dict[str, Machine]:
    """Create the machine catalog for the example."""
    return {
        "oil-refinery": Machine(
            key="oil-refinery",
            label="Oil refinery",
            crafting_speed=1.0,
            allow_productivity=True,
        ),
        "chemical-plant": Machine(
            key="chemical-plant",
            label="Chemical plant",
            crafting_speed=1.0,
            allow_productivity=True,
        ),
        "biochamber": Machine(
            key="biochamber",
            label="Biochamber",
            crafting_speed=2.0,
            allow_productivity=True,
        ),
    }


def build_recipes() -> dict[str, Recipe]:
    """Create the oil-processing recipes used in the example."""
    return {
        "advanced-oil-processing": Recipe(
            key="advanced-oil-processing",
            label="Advanced oil processing",
            energy_required=5.0,
            ingredients={"crude-oil": 100.0, "water": 50.0},
            results={
                "heavy-oil": 25.0,
                "light-oil": 45.0,
                "petroleum-gas": 55.0,
            },
            allow_productivity=True,
        ),
        "heavy-oil-cracking": Recipe(
            key="heavy-oil-cracking",
            label="Heavy oil cracking",
            energy_required=2.0,
            ingredients={"heavy-oil": 40.0, "water": 30.0},
            results={"light-oil": 30.0},
            allow_productivity=True,
        ),
        "light-oil-cracking": Recipe(
            key="light-oil-cracking",
            label="Light oil cracking",
            energy_required=2.0,
            ingredients={"light-oil": 30.0, "water": 30.0},
            results={"petroleum-gas": 20.0},
            allow_productivity=True,
        ),
    }


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

    advanced_key = "advanced-oil-processing"
    heavy_key = "heavy-oil-cracking"
    light_key = "light-oil-cracking"

    heavy_prod = rates[advanced_key].production.get("heavy-oil", 0.0)
    heavy_cons = rates[heavy_key].consumption.get("heavy-oil", 0.0)
    solver.Add(
        heavy_prod * variables[advanced_key]  # type: ignore[operator]
        == heavy_cons * variables[heavy_key]  # type: ignore[operator]
    )

    light_prod_advanced = rates[advanced_key].production.get("light-oil", 0.0)
    light_prod_from_heavy = rates[heavy_key].production.get("light-oil", 0.0)
    light_cons = rates[light_key].consumption.get("light-oil", 0.0)
    solver.Add(
        light_prod_advanced * variables[advanced_key]  # type: ignore[operator]
        + light_prod_from_heavy * variables[heavy_key]  # type: ignore[operator]
        == light_cons * variables[light_key]  # type: ignore[operator]
    )

    pg_prod_advanced = rates[advanced_key].production.get("petroleum-gas", 0.0)
    pg_prod_from_light = rates[light_key].production.get("petroleum-gas", 0.0)
    solver.Add(
        pg_prod_advanced * variables[advanced_key]  # type: ignore[operator]
        + pg_prod_from_light * variables[light_key]  # type: ignore[operator]
        >= demand_pg_per_s
    )

    objective_terms = []
    for recipe_key in recipes:
        crude_rate = rates[recipe_key].consumption.get("crude-oil", 0.0)
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
        "heavy-oil": heavy_prod * machine_counts[advanced_key]
        - heavy_cons * machine_counts[heavy_key],
        "light-oil": light_prod_advanced * machine_counts[advanced_key]
        + light_prod_from_heavy * machine_counts[heavy_key]
        - light_cons * machine_counts[light_key],
        "petroleum-gas": pg_prod_advanced * machine_counts[advanced_key]
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

    icon_catalog = load_icon_catalog(data_raw_path, data_dir_path)
    if not icon_catalog:
        st.sidebar.warning(
            "Icons were not resolved. Check your data directory paths."
        )

    machines = build_machines()
    recipes = build_recipes()

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
    )

    demand_pg_per_s = demand_pg_per_min / 60.0
    result = solve_chain(
        demand_pg_per_s,
        recipes,
        config_map,
        force_integer=force_integer,
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
    )
    render_solution(result, state)


main()
