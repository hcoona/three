"""View-specific types and protocols."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Protocol

from factorio_cycle_calculator.models import IconSpec, Recipe, RecipeConfig


class ContainerSlot(Protocol):
    """Define the container slot API used by the UI."""

    def container(self) -> ContainerSlot:
        """Return a context manager for nested rendering."""
        ...

    def __enter__(self) -> object:
        """Enter the container context."""
        ...

    def __exit__(self, typ, exc, tb) -> bool | None:
        """Exit the container context."""
        ...


class CaptionSlot(Protocol):
    """Define the caption API used by the UI."""

    def caption(self, body: str) -> object:
        """Render a caption string."""
        ...


class MarkdownSlot(Protocol):
    """Define the markdown API used by the UI."""

    def markdown(self, body: str) -> object:
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
