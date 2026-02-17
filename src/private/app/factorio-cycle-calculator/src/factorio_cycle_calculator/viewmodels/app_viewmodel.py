"""Application view model: orchestration between Streamlit views and services."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

from factorio_cycle_calculator.models import (
    BeaconSpec,
    FactorioDataRaw,
    IconSpec,
    Machine,
    ModuleSpec,
    Recipe,
    RecipeConfig,
    SolveResult,
)
from factorio_cycle_calculator.services.catalog_service import (
    build_beacon_catalog,
    build_machine_catalog,
    build_module_catalog,
    build_recipe_catalog,
    list_recipe_names_by_category,
    select_default_beacon,
)
from factorio_cycle_calculator.services.data_raw_service import load_data_raw
from factorio_cycle_calculator.services.env_service import (
    get_env_default,
    load_app_env,
)
from factorio_cycle_calculator.services.icon_service import build_icon_catalog
from factorio_cycle_calculator.services.solver_service import solve_chain


@dataclass(frozen=True)
class SidebarSettings:
    """Sidebar input state from the view."""

    data_dir_path: str
    data_raw_path: str
    demand_pg_per_min: float
    force_integer: bool
    unit_multiplier: float
    unit_label: str


@dataclass(frozen=True)
class LoadedChain:
    """Loaded domain data required by the view."""

    recipes: Mapping[str, Recipe]
    machines: Mapping[str, Machine]
    modules: Mapping[str, ModuleSpec]
    beacon_spec: BeaconSpec | None
    icon_catalog: Mapping[tuple[str, str], IconSpec]
    recipe_order: tuple[str, str, str]


class OilChainViewModel:
    """Coordinates backend services for the Streamlit app."""

    def __init__(self) -> None:
        load_app_env()
        self.default_data_dir = get_env_default(
            "FACTORIO_DATA_DIRECTORY",
            "FACTORIO_DATA_DIR",
        )
        self.default_data_raw = get_env_default(
            "FACTORIO_DATA_RAW_DUMP_JSON_FILE_PATH",
            "FACTORIO_DATA_RAW",
        )

    def load_data(self, data_raw_path: str) -> FactorioDataRaw | None:
        """Load typed data-raw model from JSON file."""
        return load_data_raw(data_raw_path)

    def list_recipe_options(
        self, data_raw: FactorioDataRaw
    ) -> tuple[list[str], list[str]]:
        """List selectable recipes for oil processing and chemistry."""
        oil_processing = list_recipe_names_by_category(
            data_raw, "oil-processing"
        )
        chemistry = list_recipe_names_by_category(
            data_raw, "organic-or-chemistry"
        )
        return oil_processing, chemistry

    def load_chain(
        self,
        data_raw: FactorioDataRaw,
        recipe_order: tuple[str, str, str],
        data_dir_path: str,
    ) -> tuple[LoadedChain | None, list[str], str | None]:
        """Create all catalogs needed to render and solve the chain."""
        warnings: list[str] = []

        machines = build_machine_catalog(data_raw)
        if not machines:
            return None, warnings, "No assembling machines found in data-raw."

        modules = build_module_catalog(data_raw)
        if not modules:
            warnings.append("No modules found in data-raw.")

        beacons = build_beacon_catalog(data_raw)
        beacon_spec = select_default_beacon(beacons)
        if beacon_spec is None:
            warnings.append("No beacons found in data-raw.")

        recipes = build_recipe_catalog(data_raw, recipe_order)
        if len(recipes) != len(recipe_order):
            return (
                None,
                warnings,
                "Some selected recipes were not found in data-raw.",
            )

        icon_catalog = build_icon_catalog(
            data_raw,
            data_dir_path,
            recipes,
            machines,
        )
        if not icon_catalog:
            warnings.append(
                "Icons were not resolved. Check your data directory paths."
            )

        return (
            LoadedChain(
                recipes=recipes,
                machines=machines,
                modules=modules,
                beacon_spec=beacon_spec,
                icon_catalog=icon_catalog,
                recipe_order=recipe_order,
            ),
            warnings,
            None,
        )

    def solve(
        self,
        chain: LoadedChain,
        config_map: Mapping[str, RecipeConfig],
        settings: SidebarSettings,
    ) -> tuple[SolveResult | None, str | None]:
        """Run optimization based on current selections."""
        demand_pg_per_s = settings.demand_pg_per_min / settings.unit_multiplier
        return solve_chain(
            demand_pg_per_s,
            chain.recipes,
            config_map,
            force_integer=settings.force_integer,
            recipe_order=chain.recipe_order,
        )
