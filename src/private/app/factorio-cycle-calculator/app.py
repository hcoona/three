"""Streamlit example for the Factorio oil-processing chain."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

import streamlit as st
from ortools.linear_solver import pywraplp

if TYPE_CHECKING:
    from collections.abc import Mapping


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

    light_prod = rates[advanced_key].production.get("light-oil", 0.0) + rates[
        heavy_key
    ].production.get("light-oil", 0.0)
    light_cons = rates[light_key].consumption.get("light-oil", 0.0)
    solver.Add(
        light_prod * variables[advanced_key]  # type: ignore[operator]
        == light_cons * variables[light_key]  # type: ignore[operator]
    )

    pg_prod = rates[advanced_key].production.get("petroleum-gas", 0.0) + rates[
        light_key
    ].production.get("petroleum-gas", 0.0)
    solver.Add(pg_prod * variables[advanced_key] >= demand_pg_per_s)  # type: ignore[operator]

    solver.Minimize(sum(variables.values()))  # type: ignore[operator]
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
        "light-oil": light_prod * machine_counts[advanced_key]
        - light_cons * machine_counts[light_key],
        "petroleum-gas": pg_prod * machine_counts[advanced_key],
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
        "Use modules",
        key=f"{recipe.key}-modules",
        help="Provide total speed/productivity bonuses from modules.",
    )
    speed_bonus = 0.0
    productivity_bonus = 0.0
    if use_modules:
        speed_bonus = st.number_input(
            "Module speed bonus (%)",
            min_value=0.0,
            value=0.0,
            step=5.0,
            key=f"{recipe.key}-module-speed",
        )
        productivity_bonus = st.number_input(
            "Module productivity bonus (%)",
            min_value=0.0,
            value=0.0,
            step=1.0,
            key=f"{recipe.key}-module-prod",
        )

    use_beacons = st.checkbox(
        "Use beacons",
        key=f"{recipe.key}-beacons",
        help="Provide a total speed bonus from beacons.",
    )
    beacon_speed_bonus = 0.0
    if use_beacons:
        beacon_speed_bonus = st.number_input(
            "Beacon speed bonus (%)",
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


def render_recipe_config(
    recipe: Recipe,
    machines: Mapping[str, Machine],
    machine_keys: list[str],
) -> RecipeConfig:
    """Render the machine and effect selection for one recipe."""
    options = [machines[key] for key in machine_keys]
    machine = st.selectbox(
        "Machine",
        options=options,
        format_func=machine_label,
        key=f"{recipe.key}-machine",
    )
    effects = render_effect_controls(recipe)
    return RecipeConfig(machine=machine, effects=effects)


def main() -> None:
    """Run the Streamlit UI for the oil-processing example."""
    st.set_page_config(page_title="Factorio Cycle Calculator", layout="wide")
    st.title("Factorio Cycle Calculator")
    st.markdown(
        "This example models the advanced oil processing chain using "
        "Google OR-Tools. Choose machines and bonuses, then solve for the "
        "machine counts that satisfy a petroleum gas demand."
    )

    machines = build_machines()
    recipes = build_recipes()

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

    st.subheader("Recipe configuration")

    config_map: dict[str, RecipeConfig] = {}
    config_map["advanced-oil-processing"] = render_recipe_config(
        recipes["advanced-oil-processing"],
        machines,
        ["oil-refinery"],
    )
    config_map["heavy-oil-cracking"] = render_recipe_config(
        recipes["heavy-oil-cracking"],
        machines,
        ["chemical-plant", "biochamber"],
    )
    config_map["light-oil-cracking"] = render_recipe_config(
        recipes["light-oil-cracking"],
        machines,
        ["chemical-plant", "biochamber"],
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

    st.subheader("Solution")
    st.write(f"Solver status: **{result.status}**")
    if result.objective_value is not None:
        st.write(f"Objective (total machines): {result.objective_value:.3f}")

    table_rows = []
    for recipe_key, recipe in recipes.items():
        count = result.machine_counts.get(recipe_key, 0.0)
        table_rows.append(
            {
                "Recipe": recipe.label,
                "Machine": config_map[recipe_key].machine.label,
                "Count": round(count, 4),
            }
        )
    st.table(table_rows)

    flow_rows = []
    for fluid, rate in result.net_flows_per_s.items():
        flow_rows.append(
            {
                "Fluid": fluid,
                "Net rate (per s)": round(rate, 4),
                "Net rate (per min)": round(rate * 60.0, 2),
            }
        )
    st.table(flow_rows)


main()
