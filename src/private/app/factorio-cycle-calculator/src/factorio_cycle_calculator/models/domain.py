"""Core domain models for production calculation."""

from __future__ import annotations

from collections.abc import Mapping  # noqa: TC003
from dataclasses import dataclass
from pathlib import Path  # noqa: TC003


@dataclass(frozen=True)
class Machine:
    """Describe a crafting machine and its capabilities."""

    key: str
    label: str
    crafting_speed: float
    allow_productivity: bool
    base_productivity: float
    crafting_categories: tuple[str, ...]
    module_slots: int
    allowed_effects: frozenset[str]


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
    ignored_by_productivity: frozenset[tuple[str, str]]


@dataclass(frozen=True)
class EffectSettings:
    """Hold module and beacon bonuses."""

    speed_bonus: float
    productivity_bonus: float


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
class ChainRates:
    """Store key flow rates for the oil-processing chain."""

    heavy_prod: float
    heavy_cons: float
    light_prod_advanced: float
    light_prod_from_heavy: float
    light_cons: float
    pg_prod_advanced: float
    pg_prod_from_light: float


@dataclass(frozen=True)
class ChainConstraintSettings:
    """Settings needed to add solver constraints."""

    recipe_order: tuple[str, str, str]
    force_integer: bool
    demand_pg_per_s: float


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


@dataclass(frozen=True)
class ModuleSpec:
    """Describe a module item and its effects."""

    key: str
    label: str
    speed_bonus: float
    productivity_bonus: float
    limitation: frozenset[str]
    limitation_blacklist: frozenset[str]


@dataclass(frozen=True)
class BeaconSpec:
    """Describe a beacon entity and its effects."""

    key: str
    label: str
    module_slots: int
    distribution_effectivity: float
    allowed_effects: frozenset[str]
