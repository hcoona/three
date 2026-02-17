"""Streamlit view implementation for the Factorio cycle calculator."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

import streamlit as st

from factorio_cycle_calculator.models import (
    BeaconSpec,
    EffectSettings,
    FactorioDataRaw,
    IconSpec,
    Machine,
    ModuleSpec,
    Recipe,
    RecipeConfig,
    SolveResult,
)
from factorio_cycle_calculator.services.catalog_service import (
    build_effect_settings,
    compute_beacon_effects,
    compute_module_effects,
    filter_modules_for_machine,
)
from factorio_cycle_calculator.services.icon_service import (
    find_icon,
    load_icon_image,
)
from factorio_cycle_calculator.services.solver_service import (
    accumulate_flows,
    build_recipe_rows,
    build_summary_items,
    format_amount,
)
from factorio_cycle_calculator.viewmodels import (
    OilChainViewModel,
    SidebarSettings,
)
from factorio_cycle_calculator.views.types import (
    CaptionSlot,
    ContainerSlot,
    RenderState,
)


@dataclass(frozen=True)
class ProductionContext:
    """Hold the data needed to render production rows."""

    recipes: Mapping[str, Recipe]
    machines: Mapping[str, Machine]
    modules: Mapping[str, ModuleSpec]
    beacon: BeaconSpec | None
    icon_catalog: Mapping[tuple[str, str], IconSpec]
    recipe_order: tuple[str, str, str]


def module_label(module: ModuleSpec | None) -> str:
    """Format module labels for selection widgets."""
    if module is None:
        return "None"
    return module.label


def machine_label(machine: Machine) -> str:
    """Format machine labels for UI controls."""
    return f"{machine.label} (speed {machine.crafting_speed:g})"


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
    for col, (proto_type, name, amount) in zip(columns, items, strict=False):
        with col:
            icon = find_icon(icon_catalog, (proto_type,), name=name)
            if icon:
                image_data = load_icon_image(str(icon.path), icon.size)
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
    """Render one summary panel for net flows."""
    st.markdown(f"**{title}**")
    render_icon_amount_list(items, icon_catalog, unit_label=unit_label)


def render_sidebar_controls(vm: OilChainViewModel) -> SidebarSettings:
    """Render sidebar controls and return selected values."""
    st.sidebar.header("Assets")
    data_dir_path = st.sidebar.text_input(
        "Factorio data directory",
        value=vm.default_data_dir,
    )
    data_raw_path = st.sidebar.text_input(
        "data-raw-dump.json path",
        value=vm.default_data_raw,
    )

    st.sidebar.header("Demand")
    rate_unit = st.sidebar.radio(
        "Rate unit",
        options=["per minute", "per second"],
        index=0,
    )
    unit_multiplier = 60.0 if rate_unit == "per minute" else 1.0
    unit_label = "per min" if rate_unit == "per minute" else "per s"

    demand_pg_per_min = st.sidebar.number_input(
        f"Petroleum gas target ({rate_unit})",
        min_value=0.0,
        value=900.0,
        step=30.0,
    )
    force_integer = st.sidebar.checkbox(
        "Force integer machine counts", value=False
    )

    return SidebarSettings(
        data_dir_path=data_dir_path,
        data_raw_path=data_raw_path,
        demand_pg_per_min=demand_pg_per_min,
        force_integer=force_integer,
        unit_multiplier=unit_multiplier,
        unit_label=unit_label,
    )


def warn_missing_data_dir(data_dir_path: str) -> None:
    """Warn when no data directory is provided."""
    if not data_dir_path:
        st.sidebar.warning(
            "Set FACTORIO_DATA_DIRECTORY or enter a data directory "
            "to load icons."
        )


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


def render_recipe_selection(
    vm: OilChainViewModel,
    data_raw: FactorioDataRaw,
) -> tuple[str, str, str] | None:
    """Render recipe selectors for the oil chain."""
    st.sidebar.header("Recipes")
    oil_processing, chemistry = vm.list_recipe_options(data_raw)
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
    """Render summary placeholder panels and return their slots."""
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


def render_effect_controls(
    recipe: Recipe,
    *,
    machine: Machine,
    modules: Mapping[str, ModuleSpec],
    beacon: BeaconSpec | None,
) -> EffectSettings:
    """Render module and beacon controls for a recipe."""
    module_column, beacon_column = st.columns(2)

    module_speed = 0.0
    module_productivity = 0.0

    with module_column:
        st.caption("Modules")
        if machine.module_slots <= 0:
            st.caption("No module slots")
        else:
            allowed_modules = filter_modules_for_machine(
                modules,
                recipe=recipe,
                machine=machine,
                allowed_effects=machine.allowed_effects,
            )
            module_options: list[ModuleSpec | None] = [None]
            module_options.extend(allowed_modules)
            selected_module = st.selectbox(
                "Module",
                options=module_options,
                format_func=module_label,
                key=f"{recipe.key}-module",
                label_visibility="collapsed",
            )
            count_options = list(range(machine.module_slots + 1))
            module_count = st.selectbox(
                "Module count",
                options=count_options,
                index=machine.module_slots,
                key=f"{recipe.key}-module-count",
                label_visibility="collapsed",
            )
            module_speed, module_productivity = compute_module_effects(
                selected_module,
                count=module_count,
            )

    beacon_speed = 0.0
    beacon_productivity = 0.0

    with beacon_column:
        st.caption("Beacons")
        if beacon is None:
            st.caption("No beacon data")
        else:
            effectivity = beacon.distribution_effectivity
            st.caption(f"{beacon.label} (effectivity {effectivity:g})")
            beacon_allowed_effects = (
                beacon.allowed_effects & machine.allowed_effects
            )
            beacon_modules = filter_modules_for_machine(
                modules,
                recipe=recipe,
                machine=machine,
                allowed_effects=beacon_allowed_effects,
            )
            beacon_module_options: list[ModuleSpec | None] = [None]
            beacon_module_options.extend(beacon_modules)
            selected_beacon_module = st.selectbox(
                "Beacon module",
                options=beacon_module_options,
                format_func=module_label,
                key=f"{recipe.key}-beacon-module",
                label_visibility="collapsed",
            )
            beacon_module_count = st.selectbox(
                "Beacon module count",
                options=list(range(beacon.module_slots + 1)),
                index=beacon.module_slots,
                key=f"{recipe.key}-beacon-module-count",
                label_visibility="collapsed",
            )
            beacon_count = st.selectbox(
                "Beacon count",
                options=list(range(13)),
                index=0,
                key=f"{recipe.key}-beacon-count",
                label_visibility="collapsed",
            )
            beacon_speed, beacon_productivity = compute_beacon_effects(
                selected_beacon_module,
                module_count=beacon_module_count,
                beacon_count=beacon_count,
                effectivity=beacon.distribution_effectivity,
            )

    return build_effect_settings(
        module_speed=module_speed,
        module_productivity=module_productivity,
        beacon_speed=beacon_speed,
        beacon_productivity=beacon_productivity,
    )


def render_production_rows(
    context: ProductionContext,
) -> tuple[
    dict[str, RecipeConfig],
    dict[str, tuple[ContainerSlot, ContainerSlot, ContainerSlot]],
    dict[str, CaptionSlot],
]:
    """Render production rows and return configs and row placeholders."""
    config_map: dict[str, RecipeConfig] = {}
    row_slots: dict[
        str, tuple[ContainerSlot, ContainerSlot, ContainerSlot]
    ] = {}
    count_slots: dict[str, CaptionSlot] = {}

    for recipe_key in context.recipe_order:
        recipe = context.recipes[recipe_key]
        eligible = [
            key
            for key, machine in context.machines.items()
            if recipe.category in machine.crafting_categories
        ]
        if not eligible:
            eligible = list(context.machines.keys())

        cols = st.columns([1.4, 1.8, 1.6, 0.9, 2.4, 2.4, 2.4])
        with cols[0]:
            recipe_icon = find_icon(
                context.icon_catalog, ("recipe",), name=recipe.key
            )
            render_icon_label(recipe_icon, recipe.label)

        with cols[1]:
            machine = render_machine_selector(
                recipe, context.machines, eligible
            )
            machine_icon = find_icon(
                context.icon_catalog,
                ("item", "assembling-machine"),
                name=machine.key,
            )
            render_icon_label(machine_icon, machine.label)
            count_slots[recipe_key] = st.empty()

        with cols[2]:
            effects = render_effect_controls(
                recipe,
                machine=machine,
                modules=context.modules,
                beacon=context.beacon,
            )

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
    """Render solved output and update placeholders."""
    production, consumption = accumulate_flows(
        state.recipes,
        state.config_map,
        result.machine_counts,
    )
    crude_input = (
        consumption.get(("fluid", "crude-oil"), 0.0) * state.unit_multiplier
    )
    state.status_slot.markdown(
        f"Solver status: **{result.status}** • "
        f"Crude input: {format_amount(crude_input)} {state.unit_label}"
    )

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
        products_cell, byproducts_cell, ingredients_cell = state.row_slots[
            recipe_key
        ]
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


def render_chain(vm: OilChainViewModel, settings: SidebarSettings) -> None:
    """Render full chain page based on current settings."""
    if not settings.data_raw_path:
        st.error(
            "Set FACTORIO_DATA_RAW_DUMP_JSON_FILE_PATH or enter a "
            "data-raw-dump.json path to load."
        )
        return

    warn_missing_data_dir(settings.data_dir_path)

    data_raw = vm.load_data(settings.data_raw_path)
    if data_raw is None:
        st.error("Failed to load data-raw-dump.json.")
        return

    recipe_order = render_recipe_selection(vm, data_raw)
    if recipe_order is None:
        st.error("Recipe selection is incomplete.")
        return

    chain, warnings, chain_error = vm.load_chain(
        data_raw,
        recipe_order,
        settings.data_dir_path,
    )
    for warning in warnings:
        st.warning(warning)
    if chain_error:
        st.error(chain_error)
        return
    if chain is None:
        st.error("Failed to load chain context.")
        return

    products_slot, byproducts_slot, ingredients_slot = (
        render_summary_placeholders()
    )
    status_slot = st.empty()

    st.subheader("Production")
    render_production_header()
    production_context = ProductionContext(
        recipes=chain.recipes,
        machines=chain.machines,
        modules=chain.modules,
        beacon=chain.beacon_spec,
        icon_catalog=chain.icon_catalog,
        recipe_order=chain.recipe_order,
    )
    config_map, row_slots, count_slots = render_production_rows(
        production_context
    )

    result, solve_error = vm.solve(chain, config_map, settings)
    if solve_error:
        st.error(solve_error)
        return
    if result is None:
        st.error("Solver did not produce a result.")
        return

    state = RenderState(
        recipes=chain.recipes,
        config_map=config_map,
        row_slots=row_slots,
        count_slots=count_slots,
        products_slot=products_slot,
        byproducts_slot=byproducts_slot,
        ingredients_slot=ingredients_slot,
        status_slot=status_slot,
        unit_multiplier=settings.unit_multiplier,
        unit_label=settings.unit_label,
        icon_catalog=chain.icon_catalog,
        recipe_order=chain.recipe_order,
    )
    render_solution(result, state)


def run_app() -> None:
    """Run the Streamlit UI."""
    st.set_page_config(page_title="Factorio Cycle Calculator", layout="wide")
    st.title("Factorio Cycle Calculator")
    st.markdown(
        "This example models the advanced oil processing chain using "
        "Google OR-Tools. Choose machines and bonuses, then solve for the "
        "machine counts that satisfy a petroleum gas demand while minimizing "
        "crude oil input (water is treated as a free input)."
    )
    vm = OilChainViewModel()
    settings = render_sidebar_controls(vm)
    render_chain(vm, settings)
