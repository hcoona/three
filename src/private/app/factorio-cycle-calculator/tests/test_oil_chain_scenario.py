"""Scenario tests for planning the advanced oil processing chain."""

import json
from pathlib import Path

import pytest

from factorio_cycle_calculator.models import EffectSettings, RecipeConfig
from factorio_cycle_calculator.viewmodels.app_viewmodel import (
    LoadedChain,
    OilChainViewModel,
    SidebarSettings,
)

RECIPE_ORDER = (
    "advanced-oil-processing",
    "heavy-oil-cracking",
    "light-oil-cracking",
)
NO_EFFECTS = EffectSettings(speed_bonus=0.0, productivity_bonus=0.0)


def _fluid(name: str, amount: float) -> dict[str, object]:
    return {"type": "fluid", "name": name, "amount": amount}


def _machine(name: str, category: str) -> dict[str, object]:
    return {
        "type": "assembling-machine",
        "name": name,
        "crafting_speed": 1,
        "crafting_categories": [category],
        "energy_source": {"type": "electric"},
        "energy_usage": "420kW",
        "module_slots": 3,
        "allowed_effects": ["speed", "productivity"],
    }


def _recipe(
    name: str,
    category: str,
    energy_required: float,
    ingredients: list[dict[str, object]],
    results: list[dict[str, object]],
) -> dict[str, object]:
    return {
        "type": "recipe",
        "name": name,
        "category": category,
        "energy_required": energy_required,
        "ingredients": ingredients,
        "results": results,
        "allow_productivity": True,
    }


# A data-raw dump excerpt with the vanilla advanced oil processing chain.
DATA_RAW = {
    "assembling-machine": {
        "oil-refinery": _machine("oil-refinery", "oil-processing"),
        "chemical-plant": _machine("chemical-plant", "organic-or-chemistry"),
    },
    "recipe": {
        "advanced-oil-processing": _recipe(
            "advanced-oil-processing",
            "oil-processing",
            5,
            [_fluid("water", 50), _fluid("crude-oil", 100)],
            [
                _fluid("heavy-oil", 25),
                _fluid("light-oil", 45),
                _fluid("petroleum-gas", 55),
            ],
        ),
        "heavy-oil-cracking": _recipe(
            "heavy-oil-cracking",
            "organic-or-chemistry",
            2,
            [_fluid("water", 30), _fluid("heavy-oil", 40)],
            [_fluid("light-oil", 30)],
        ),
        "light-oil-cracking": _recipe(
            "light-oil-cracking",
            "organic-or-chemistry",
            2,
            [_fluid("water", 30), _fluid("light-oil", 30)],
            [_fluid("petroleum-gas", 20)],
        ),
    },
}


def _load_chain(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> tuple[OilChainViewModel, LoadedChain]:
    # Keep developer dotenv files out of the scenario.
    monkeypatch.delenv("FACTORIO_ENV_FILE", raising=False)
    monkeypatch.chdir(tmp_path)
    data_raw_path = tmp_path / "data-raw-dump.json"
    data_raw_path.write_text(json.dumps(DATA_RAW), encoding="utf-8")

    view_model = OilChainViewModel()
    data_raw = view_model.load_data(str(data_raw_path))
    assert data_raw is not None
    assert view_model.list_recipe_options(data_raw) == (
        ["advanced-oil-processing"],
        ["heavy-oil-cracking", "light-oil-cracking"],
    )

    chain, _, error = view_model.load_chain(
        data_raw, RECIPE_ORDER, str(tmp_path / "no-game-data")
    )
    assert error is None
    assert chain is not None
    return view_model, chain


def _configs(chain: LoadedChain) -> dict[str, RecipeConfig]:
    refinery = chain.machines["oil-refinery"]
    chemical_plant = chain.machines["chemical-plant"]
    return {
        "advanced-oil-processing": RecipeConfig(refinery, NO_EFFECTS),
        "heavy-oil-cracking": RecipeConfig(chemical_plant, NO_EFFECTS),
        "light-oil-cracking": RecipeConfig(chemical_plant, NO_EFFECTS),
    }


def _settings(demand: float, *, force_integer: bool) -> SidebarSettings:
    return SidebarSettings(
        data_dir_path="",
        data_raw_path="",
        demand_value=demand,
        force_integer=force_integer,
        unit_multiplier=1.0,
        unit_label="/s",
    )


def test_balanced_chain_matches_the_classic_20_5_17_ratio(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Fully crack all oil into 390 petroleum gas per second."""
    view_model, chain = _load_chain(tmp_path, monkeypatch)

    result, error = view_model.solve(
        chain, _configs(chain), _settings(390.0, force_integer=False)
    )

    assert error is None
    assert result is not None
    assert result.status == "Optimal"
    assert result.machine_counts == {
        "advanced-oil-processing": pytest.approx(20.0),
        "heavy-oil-cracking": pytest.approx(5.0),
        "light-oil-cracking": pytest.approx(17.0),
    }
    # Crude oil per second: 20 refineries * 100 crude / 5 seconds.
    assert result.objective_value == pytest.approx(400.0)
    assert result.net_flows_per_s == {
        ("fluid", "heavy-oil"): pytest.approx(0.0, abs=1e-6),
        ("fluid", "light-oil"): pytest.approx(0.0, abs=1e-6),
        ("fluid", "petroleum-gas"): pytest.approx(390.0),
    }


def test_whole_machine_plan_meets_demand_without_starving_crackers(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Use the fewest whole refineries and report the oil surplus."""
    view_model, chain = _load_chain(tmp_path, monkeypatch)

    result, error = view_model.solve(
        chain, _configs(chain), _settings(100.0, force_integer=True)
    )

    assert error is None
    assert result is not None
    assert result.status == "Optimal"
    assert {
        key: round(value) for key, value in result.machine_counts.items()
    } == {
        "advanced-oil-processing": 6,
        "heavy-oil-cracking": 1,
        "light-oil-cracking": 4,
    }
    assert result.objective_value == pytest.approx(120.0)
    assert result.net_flows_per_s == {
        ("fluid", "heavy-oil"): pytest.approx(10.0),
        ("fluid", "light-oil"): pytest.approx(9.0),
        ("fluid", "petroleum-gas"): pytest.approx(106.0),
    }
