"""Streamlit view implementation for the Factorio cycle calculator."""

from __future__ import annotations

import base64
import io
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

import streamlit as st
from PIL import Image

from factorio_cycle_calculator.models import (
    BeaconSpec,
    EffectSettings,
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

DEFAULT_RECIPE_ORDER: tuple[str, str, str] = (
    "advanced-oil-processing",
    "heavy-oil-cracking",
    "light-oil-cracking",
)

BLOCK_FLOW_COLUMN_RATIOS = [1.2, 2.4, 2.4, 2.4]
BADGE_ICON_SIZE_PX = 20


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


def format_module_summary(module: ModuleSpec | None, count: int) -> str:
    """Format a short summary for module configuration."""
    if module is None or count <= 0:
        return "None"
    return f"{module.label} x {count}"


def ensure_selectbox_value(
    key: str,
    options: Sequence[object],
    default: object,
) -> None:
    """Ensure selectbox state remains valid when option lists change."""
    if key not in st.session_state:
        st.session_state[key] = default
        return
    current = st.session_state.get(key, default)
    if current not in options:
        st.session_state[key] = default


def build_icon_label(icon: IconSpec | None, *, fallback: str) -> str:
    """Build markdown label for a popover trigger icon."""
    return build_icon_badge_label(icon, fallback=fallback)


def build_icon_badge_label(
    icon: IconSpec | None,
    *,
    fallback: str,
    count_label: str | None = None,
) -> str:
    """Build markdown label with icon and optional compact count badge."""
    if icon is None:
        return fallback
    icon_data = load_icon_image(str(icon.path), icon.size)
    if icon_data is None:
        return fallback
    icon_data = downscale_icon_bytes(icon_data, max_size=BADGE_ICON_SIZE_PX)
    encoded = base64.b64encode(icon_data).decode("ascii")
    label = f"![{fallback}](data:image/png;base64,{encoded})"
    if count_label:
        label = f"{label} `{count_label}`"
    return label


def downscale_icon_bytes(icon_data: bytes, *, max_size: int) -> bytes:
    """Downscale icon bytes to fit a compact square for markdown badges."""
    if max_size <= 0:
        return icon_data
    try:
        with Image.open(io.BytesIO(icon_data)) as image_file:
            image = image_file.convert("RGBA")
            if image.width <= max_size and image.height <= max_size:
                return icon_data
            image.thumbnail((max_size, max_size), Image.Resampling.LANCZOS)
            with io.BytesIO() as output:
                image.save(output, format="PNG")
                return output.getvalue()
    except OSError:
        return icon_data


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


def render_sidebar_asset_controls(vm: OilChainViewModel) -> tuple[str, str]:
    """Render sidebar controls for data paths."""
    st.sidebar.header("Assets")
    data_dir_path = st.sidebar.text_input(
        "Factorio data directory",
        value=vm.default_data_dir,
    )
    data_raw_path = st.sidebar.text_input(
        "data-raw-dump.json path",
        value=vm.default_data_raw,
    )

    return data_dir_path, data_raw_path


def render_objective_bar(
    *,
    default_pg_per_min: float = 900.0,
) -> tuple[float, bool, float, str]:
    """Render compact top objective controls."""
    objective_container = st.container()
    with objective_container:
        st.markdown("### Objective")
        cols = st.columns([2.4, 1.4, 1.6, 3.0])

        with cols[0]:
            st.caption("Target")
            demand_pg_per_min = st.number_input(
                "Petroleum gas target",
                min_value=0.0,
                value=default_pg_per_min,
                step=30.0,
                key="objective-demand",
                label_visibility="collapsed",
            )

        with cols[1]:
            st.caption("Rate")
            rate_unit = st.radio(
                "Rate unit",
                options=["per minute", "per second"],
                index=0,
                horizontal=True,
                key="objective-rate-unit",
                label_visibility="collapsed",
            )

        with cols[2]:
            st.caption("Mode")
            force_integer = st.checkbox(
                "Integer machine counts",
                value=False,
                key="objective-force-integer",
                label_visibility="collapsed",
            )

        with cols[3]:
            unit_text = (
                "per minute" if rate_unit == "per minute" else "per second"
            )
            mode_text = "integer" if force_integer else "continuous"
            st.caption("Current")
            st.markdown(
                "**Petroleum gas** "
                f"{format_amount(demand_pg_per_min)} {unit_text}"
                f" • {mode_text}"
            )

    unit_multiplier = 60.0 if rate_unit == "per minute" else 1.0
    unit_label = "per min" if rate_unit == "per minute" else "per s"
    return demand_pg_per_min, force_integer, unit_multiplier, unit_label


def build_sidebar_settings(vm: OilChainViewModel) -> SidebarSettings:
    """Build settings from sidebar assets and top objective controls."""
    data_dir_path, data_raw_path = render_sidebar_asset_controls(vm)
    demand_pg_per_min, force_integer, unit_multiplier, unit_label = (
        render_objective_bar()
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


def render_machine_selector(
    recipe: Recipe,
    machines: Mapping[str, Machine],
    machine_keys: list[str],
    icon_catalog: Mapping[tuple[str, str], IconSpec],
) -> Machine:
    """Render machine selector with icon-rich option list."""
    options = [machines[key] for key in machine_keys]
    machine_key = f"{recipe.key}-machine"
    ensure_selectbox_value(machine_key, options, options[0])

    selected_machine = st.session_state[machine_key]
    selected_icon = find_icon(
        icon_catalog,
        ("item", "assembling-machine"),
        name=selected_machine.key,
    )

    popover_label = build_icon_label(
        selected_icon,
        fallback=machine_label(selected_machine),
    )
    machine_changed = False
    with st.popover(popover_label):
        st.caption("Machine options")
        for machine in options:
            option_cols = st.columns([0.6, 2.4])
            with option_cols[0]:
                machine_icon = find_icon(
                    icon_catalog,
                    ("item", "assembling-machine"),
                    name=machine.key,
                )
                if machine_icon:
                    option_image = load_icon_image(
                        str(machine_icon.path),
                        machine_icon.size,
                    )
                    if option_image:
                        st.image(option_image, width=20)
            with option_cols[1]:
                if st.button(
                    machine_label(machine),
                    key=f"{recipe.key}-machine-option-{machine.key}",
                    use_container_width=True,
                ):
                    st.session_state[machine_key] = machine
                    machine_changed = True

    if machine_changed:
        st.rerun()

    return st.session_state[machine_key]


def render_module_controls(
    recipe: Recipe,
    *,
    machine: Machine,
    modules: Mapping[str, ModuleSpec],
    icon_catalog: Mapping[tuple[str, str], IconSpec],
) -> tuple[float, float]:
    """Render module controls and return speed/productivity bonuses."""
    if machine.module_slots <= 0:
        st.caption("No slots")
        return 0.0, 0.0

    allowed_modules = filter_modules_for_machine(
        modules,
        recipe=recipe,
        machine=machine,
        allowed_effects=machine.allowed_effects,
    )
    module_options: list[ModuleSpec | None] = [None]
    module_options.extend(allowed_modules)

    module_key = f"{recipe.key}-module"
    ensure_selectbox_value(module_key, module_options, None)

    count_options = list(range(machine.module_slots + 1))
    module_count_key = f"{recipe.key}-module-count"
    ensure_selectbox_value(module_count_key, count_options, 0)

    selected_module = st.session_state[module_key]
    module_count = st.session_state[module_count_key]
    module_summary = format_module_summary(selected_module, module_count)
    module_icon = None
    if selected_module is not None:
        module_icon = find_icon(
            icon_catalog,
            ("item", "module"),
            name=selected_module.key,
        )

    count_label = (
        f"x{module_count}" if selected_module and module_count > 0 else None
    )
    popover_label = build_icon_badge_label(
        module_icon,
        fallback=module_summary,
        count_label=count_label,
    )

    with st.popover(popover_label):
        st.selectbox(
            "Module",
            options=module_options,
            format_func=module_label,
            key=module_key,
        )
        st.selectbox(
            "Module count",
            options=count_options,
            key=module_count_key,
        )

    selected_module = st.session_state[module_key]
    module_count = st.session_state[module_count_key]
    return compute_module_effects(selected_module, count=module_count)


def render_beacon_controls(
    recipe: Recipe,
    *,
    machine: Machine,
    modules: Mapping[str, ModuleSpec],
    beacon: BeaconSpec | None,
    icon_catalog: Mapping[tuple[str, str], IconSpec],
) -> tuple[float, float]:
    """Render beacon controls and return speed/productivity bonuses."""
    if beacon is None:
        st.caption("No beacon data")
        return 0.0, 0.0

    effectivity = beacon.distribution_effectivity
    beacon_allowed_effects = beacon.allowed_effects & machine.allowed_effects
    beacon_modules = filter_modules_for_machine(
        modules,
        recipe=recipe,
        machine=machine,
        allowed_effects=beacon_allowed_effects,
    )
    beacon_module_options: list[ModuleSpec | None] = [None]
    beacon_module_options.extend(beacon_modules)

    beacon_module_key = f"{recipe.key}-beacon-module"
    ensure_selectbox_value(beacon_module_key, beacon_module_options, None)

    beacon_module_count_options = list(range(beacon.module_slots + 1))
    beacon_module_count_key = f"{recipe.key}-beacon-module-count"
    ensure_selectbox_value(
        beacon_module_count_key,
        beacon_module_count_options,
        0,
    )

    beacon_count_options = list(range(13))
    beacon_count_key = f"{recipe.key}-beacon-count"
    ensure_selectbox_value(beacon_count_key, beacon_count_options, 0)

    selected_beacon_module = st.session_state[beacon_module_key]
    beacon_module_count = st.session_state[beacon_module_count_key]
    beacon_count = st.session_state[beacon_count_key]

    module_summary = format_module_summary(
        selected_beacon_module,
        beacon_module_count,
    )
    beacon_summary = f"{module_summary} • Towers: {beacon_count}"

    beacon_icon = find_icon(
        icon_catalog,
        ("item", "beacon"),
        name=beacon.key,
    )

    tower_label = build_icon_badge_label(
        beacon_icon,
        fallback=beacon.label,
        count_label=f"x{beacon_count}",
    )
    st.markdown(tower_label)

    module_icon = None
    if selected_beacon_module is not None:
        module_icon = find_icon(
            icon_catalog,
            ("item", "module"),
            name=selected_beacon_module.key,
        )

    module_count_label = (
        f"x{beacon_module_count}"
        if selected_beacon_module and beacon_module_count > 0
        else None
    )
    popover_label = build_icon_badge_label(
        module_icon,
        fallback=beacon_summary,
        count_label=module_count_label,
    )

    with st.popover(popover_label):
        st.caption(f"{beacon.label} • effectivity {effectivity:g}")
        st.selectbox(
            "Beacon module",
            options=beacon_module_options,
            format_func=module_label,
            key=beacon_module_key,
        )
        st.selectbox(
            "Beacon module count",
            options=beacon_module_count_options,
            key=beacon_module_count_key,
        )
        st.selectbox(
            "Beacon count",
            options=beacon_count_options,
            key=beacon_count_key,
        )

    selected_beacon_module = st.session_state[beacon_module_key]
    beacon_module_count = st.session_state[beacon_module_count_key]
    beacon_count = st.session_state[beacon_count_key]
    return compute_beacon_effects(
        selected_beacon_module,
        module_count=beacon_module_count,
        beacon_count=beacon_count,
        effectivity=beacon.distribution_effectivity,
    )


def render_effect_controls(
    recipe: Recipe,
    *,
    machine: Machine,
    modules: Mapping[str, ModuleSpec],
    beacon: BeaconSpec | None,
    icon_catalog: Mapping[tuple[str, str], IconSpec],
) -> EffectSettings:
    """Render module and beacon controls for a recipe."""
    label_cols = st.columns(2)
    label_cols[0].caption("Module")
    label_cols[1].caption("Beacon")

    module_column, beacon_column = st.columns(2)

    with module_column:
        module_speed, module_productivity = render_module_controls(
            recipe,
            machine=machine,
            modules=modules,
            icon_catalog=icon_catalog,
        )

    with beacon_column:
        beacon_speed, beacon_productivity = render_beacon_controls(
            recipe,
            machine=machine,
            modules=modules,
            beacon=beacon,
            icon_catalog=icon_catalog,
        )

    return build_effect_settings(
        module_speed=module_speed,
        module_productivity=module_productivity,
        beacon_speed=beacon_speed,
        beacon_productivity=beacon_productivity,
    )


def render_recipe_block(
    context: ProductionContext,
    recipe_key: str,
) -> tuple[
    RecipeConfig,
    tuple[ContainerSlot, ContainerSlot, ContainerSlot],
    CaptionSlot,
]:
    """Render one compact recipe block and return its UI bindings."""
    recipe = context.recipes[recipe_key]
    eligible = [
        key
        for key, machine in context.machines.items()
        if recipe.category in machine.crafting_categories
    ]
    if not eligible:
        eligible = list(context.machines.keys())

    with st.container(border=True):
        header_cols = st.columns([2.8, 1.6])
        with header_cols[0]:
            recipe_icon = find_icon(
                context.icon_catalog,
                ("recipe",),
                name=recipe.key,
            )
            render_icon_label(recipe_icon, recipe.label)
        with header_cols[1]:
            st.caption("Category")
            st.markdown(f"`{recipe.category}`")

        build_cols = st.columns([2.2, 2.8, 1.0])
        with build_cols[0]:
            st.caption("Machine")
            machine = render_machine_selector(
                recipe,
                context.machines,
                eligible,
                context.icon_catalog,
            )
            count_slot = st.empty()

        with build_cols[1]:
            st.caption("Modules / Beacons")
            effects = render_effect_controls(
                recipe,
                machine=machine,
                modules=context.modules,
                beacon=context.beacon,
                icon_catalog=context.icon_catalog,
            )

        with build_cols[2]:
            st.caption("Power")
            st.caption("—")

        flow_cols = st.columns(BLOCK_FLOW_COLUMN_RATIOS)
        with flow_cols[0]:
            st.caption("Count")
        with flow_cols[1]:
            st.caption("Products")
            products_cell = st.empty()
        with flow_cols[2]:
            st.caption("Byproducts")
            byproducts_cell = st.empty()
        with flow_cols[3]:
            st.caption("Ingredients")
            ingredients_cell = st.empty()

    return (
        RecipeConfig(machine=machine, effects=effects),
        (products_cell, byproducts_cell, ingredients_cell),
        count_slot,
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
        config, cells, count_slot = render_recipe_block(context, recipe_key)
        config_map[recipe_key] = config
        row_slots[recipe_key] = cells
        count_slots[recipe_key] = count_slot

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

    recipe_order = DEFAULT_RECIPE_ORDER

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

    st.subheader("Production Blocks")
    st.caption(
        "Fixed demo chain: Advanced Oil Processing → Heavy Oil Cracking → "
        "Light Oil Cracking"
    )
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
        "This demo models a fixed advanced oil-processing chain using "
        "Google OR-Tools. Configure machines/modules/beacons, adjust petroleum "
        "gas demand, and solve for machine counts that satisfy the "
        "target while "
        "minimizing crude oil input (water is treated as a free input)."
    )
    vm = OilChainViewModel()
    settings = build_sidebar_settings(vm)
    render_chain(vm, settings)
