"""Optimization and flow computation services."""

from __future__ import annotations

from collections.abc import Mapping, Sequence

from ortools.linear_solver import pywraplp

from factorio_cycle_calculator.models import (
    ChainConstraintSettings,
    ChainRates,
    EffectSettings,
    FlowRates,
    Machine,
    Recipe,
    RecipeConfig,
    SolveResult,
)

FLOW_EPSILON = 1e-6
FORMAT_MILLION = 1_000_000.0
FORMAT_THOUSAND = 1_000.0
FORMAT_TEN = 10.0


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


def compute_effective_speed(machine: Machine, effects: EffectSettings) -> float:
    """Compute the effective crafting speed after bonuses."""
    return machine.crafting_speed * (1.0 + effects.speed_bonus)


def per_machine_rates(recipe: Recipe, config: RecipeConfig) -> FlowRates:
    """Compute per-second production and consumption for one machine."""
    effective_speed = compute_effective_speed(config.machine, config.effects)
    cycle_seconds = recipe.energy_required / effective_speed
    productivity = 0.0
    if recipe.allow_productivity and config.machine.allow_productivity:
        productivity = (
            config.machine.base_productivity + config.effects.productivity_bonus
        )
    multiplier = 1.0 + productivity

    production: dict[tuple[str, str], float] = {}
    for key, amount in recipe.results.items():
        if key in recipe.ignored_by_productivity:
            production[key] = amount / cycle_seconds
        else:
            production[key] = amount * multiplier / cycle_seconds
    consumption = {
        key: amount / cycle_seconds
        for key, amount in recipe.ingredients.items()
    }
    return FlowRates(production=production, consumption=consumption)


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
            flow_key,
            0.0,
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


def build_solver(*, force_integer: bool) -> pywraplp.Solver | None:
    """Create the OR-Tools solver instance."""
    solver_name = "CBC_MIXED_INTEGER_PROGRAMMING" if force_integer else "GLOP"
    return pywraplp.Solver.CreateSolver(solver_name)


def validate_recipe_configs(
    recipes: Mapping[str, Recipe],
    configs: Mapping[str, RecipeConfig],
) -> str | None:
    """Validate recipe configs before building the solver."""
    for recipe_key, recipe in recipes.items():
        config = configs[recipe_key]
        effective_speed = compute_effective_speed(
            config.machine, config.effects
        )
        if effective_speed <= 0.0:
            return (
                "Effective crafting speed must be positive. "
                "Check module and beacon bonuses."
            )
        if recipe.energy_required <= 0.0:
            return "Recipe energy_required must be positive."
    return None


def build_solver_variables(
    solver: pywraplp.Solver,
    recipes: Mapping[str, Recipe],
    *,
    force_integer: bool,
) -> dict[str, pywraplp.Variable]:
    """Create solver variables for each recipe."""
    variables: dict[str, pywraplp.Variable] = {}
    for recipe_key in recipes:
        if force_integer:
            variables[recipe_key] = solver.IntVar(
                0.0,
                solver.infinity(),
                recipe_key,
            )
        else:
            variables[recipe_key] = solver.NumVar(
                0.0,
                solver.infinity(),
                recipe_key,
            )
    return variables


def extract_chain_rates(
    rates: Mapping[str, FlowRates],
    recipe_order: tuple[str, str, str],
) -> ChainRates:
    """Extract oil-chain flow rates from per-machine rates."""
    advanced_key, heavy_key, light_key = recipe_order
    heavy_prod = rates[advanced_key].production.get(("fluid", "heavy-oil"), 0.0)
    heavy_cons = rates[heavy_key].consumption.get(("fluid", "heavy-oil"), 0.0)
    light_prod_advanced = rates[advanced_key].production.get(
        ("fluid", "light-oil"),
        0.0,
    )
    light_prod_from_heavy = rates[heavy_key].production.get(
        ("fluid", "light-oil"),
        0.0,
    )
    light_cons = rates[light_key].consumption.get(("fluid", "light-oil"), 0.0)
    pg_prod_advanced = rates[advanced_key].production.get(
        ("fluid", "petroleum-gas"),
        0.0,
    )
    pg_prod_from_light = rates[light_key].production.get(
        ("fluid", "petroleum-gas"),
        0.0,
    )
    return ChainRates(
        heavy_prod=heavy_prod,
        heavy_cons=heavy_cons,
        light_prod_advanced=light_prod_advanced,
        light_prod_from_heavy=light_prod_from_heavy,
        light_cons=light_cons,
        pg_prod_advanced=pg_prod_advanced,
        pg_prod_from_light=pg_prod_from_light,
    )


def validate_chain_rates(rates: ChainRates) -> str | None:
    """Validate that chain rates include required flows."""
    if rates.heavy_prod <= 0.0 or rates.heavy_cons <= 0.0:
        return (
            "Selected recipes must produce/consume heavy-oil. "
            "Please choose an oil-processing recipe that outputs heavy-oil "
            "and a cracking recipe that consumes heavy-oil."
        )
    total_light_prod = rates.light_prod_advanced + rates.light_prod_from_heavy
    if total_light_prod <= 0.0 or rates.light_cons <= 0.0:
        return (
            "Selected recipes must produce/consume light-oil. "
            "Please choose recipes that produce light-oil and a cracking "
            "recipe that consumes light-oil."
        )
    if rates.pg_prod_advanced + rates.pg_prod_from_light <= 0.0:
        return (
            "Selected recipes must produce petroleum gas. "
            "Please choose an oil-processing/cracking chain that outputs it."
        )
    return None


def add_balance_constraint(
    solver: pywraplp.Solver,
    *,
    left_terms: Sequence[tuple[float, pywraplp.Variable]],
    right_terms: Sequence[tuple[float, pywraplp.Variable]],
    force_integer: bool,
) -> None:
    """Add a balance constraint using >= for integers or == for floats."""
    left_expr = solver.Sum(
        coeff * variable  # type: ignore[operator]
        for coeff, variable in left_terms
    )
    right_expr = solver.Sum(
        coeff * variable  # type: ignore[operator]
        for coeff, variable in right_terms
    )
    if force_integer:
        solver.Add(left_expr >= right_expr)
    else:
        solver.Add(left_expr == right_expr)


def add_chain_constraints(
    solver: pywraplp.Solver,
    variables: Mapping[str, pywraplp.Variable],
    rates: ChainRates,
    *,
    settings: ChainConstraintSettings,
) -> None:
    """Add oil-chain balance constraints to the solver."""
    advanced_key, heavy_key, light_key = settings.recipe_order
    add_balance_constraint(
        solver,
        left_terms=[(rates.heavy_prod, variables[advanced_key])],
        right_terms=[(rates.heavy_cons, variables[heavy_key])],
        force_integer=settings.force_integer,
    )
    add_balance_constraint(
        solver,
        left_terms=[
            (rates.light_prod_advanced, variables[advanced_key]),
            (rates.light_prod_from_heavy, variables[heavy_key]),
        ],
        right_terms=[(rates.light_cons, variables[light_key])],
        force_integer=settings.force_integer,
    )
    solver.Add(
        rates.pg_prod_advanced * variables[advanced_key]  # type: ignore[operator]
        + rates.pg_prod_from_light * variables[light_key]  # type: ignore[operator]
        >= settings.demand_pg_per_s
    )


def solve_chain(
    demand_pg_per_s: float,
    recipes: Mapping[str, Recipe],
    configs: Mapping[str, RecipeConfig],
    *,
    force_integer: bool,
    recipe_order: tuple[str, str, str],
) -> tuple[SolveResult | None, str | None]:
    """Solve the oil-processing chain to meet petroleum gas demand."""
    solver = build_solver(force_integer=force_integer)
    if solver is None:
        return None, "OR-Tools solver is not available in this environment."

    validation_error = validate_recipe_configs(recipes, configs)
    if validation_error:
        return None, validation_error

    rates = {
        recipe_key: per_machine_rates(recipe, configs[recipe_key])
        for recipe_key, recipe in recipes.items()
    }

    variables = build_solver_variables(
        solver,
        recipes,
        force_integer=force_integer,
    )

    advanced_key, heavy_key, light_key = recipe_order
    chain_rates = extract_chain_rates(rates, recipe_order)
    chain_error = validate_chain_rates(chain_rates)
    if chain_error:
        return None, chain_error

    add_chain_constraints(
        solver,
        variables,
        chain_rates,
        settings=ChainConstraintSettings(
            recipe_order=recipe_order,
            force_integer=force_integer,
            demand_pg_per_s=demand_pg_per_s,
        ),
    )

    objective_terms = []
    for recipe_key in recipes:
        crude_rate = rates[recipe_key].consumption.get(
            ("fluid", "crude-oil"), 0.0
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
        ("fluid", "heavy-oil"): chain_rates.heavy_prod
        * machine_counts[advanced_key]
        - chain_rates.heavy_cons * machine_counts[heavy_key],
        ("fluid", "light-oil"): chain_rates.light_prod_advanced
        * machine_counts[advanced_key]
        + chain_rates.light_prod_from_heavy * machine_counts[heavy_key]
        - chain_rates.light_cons * machine_counts[light_key],
        ("fluid", "petroleum-gas"): chain_rates.pg_prod_advanced
        * machine_counts[advanced_key]
        + chain_rates.pg_prod_from_light * machine_counts[light_key],
    }

    objective_value: float | None
    if status_code in {pywraplp.Solver.OPTIMAL, pywraplp.Solver.FEASIBLE}:
        objective_value = solver.Objective().Value()
    else:
        objective_value = None

    return (
        SolveResult(
            status=status,
            machine_counts=machine_counts,
            net_flows_per_s=net_flows,
            objective_value=objective_value,
        ),
        None,
    )
