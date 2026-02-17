"""Access wrappers for generated data-raw models."""

from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING


class GeneratedModelsImportError(ImportError):
    """Raised when generated data model imports fail."""

    def __init__(self, path: Path, *, reason: str) -> None:
        """Create a formatted import error for generated models."""
        message = f"{reason} at {path}. Regenerate with datamodel-codegen."
        super().__init__(message)


if TYPE_CHECKING:

    class _GeneratedModel:
        """Type-checking stub for generated dataclasses."""

        def __init__(self, **kwargs: object) -> None: ...

        def __getattr__(self, name: str) -> object: ...

    class AssemblingMachinePrototype(_GeneratedModel):
        """Type-checking stub for assembling machines."""

    class BeaconPrototype(_GeneratedModel):
        """Type-checking stub for beacons."""

    class FactorioDataRaw(_GeneratedModel):
        """Type-checking stub for data-raw container."""

    class FluidPrototype(_GeneratedModel):
        """Type-checking stub for fluids."""

    class ItemPrototype(_GeneratedModel):
        """Type-checking stub for items."""

    class ModulePrototype(_GeneratedModel):
        """Type-checking stub for modules."""

    class RecipePrototype(_GeneratedModel):
        """Type-checking stub for recipes."""
else:
    try:
        from factorio_cycle_calculator.generated import (
            data_raw_models as _module,
        )
    except Exception as exc:
        _generated_path = (
            Path(__file__).resolve().parent.parent
            / "generated"
            / "data_raw_models.py"
        )
        raise GeneratedModelsImportError(
            _generated_path,
            reason="Failed to import generated models",
        ) from exc

    AssemblingMachinePrototype = _module.AssemblingMachinePrototype
    BeaconPrototype = _module.BeaconPrototype
    FactorioDataRaw = _module.FactorioDataRaw
    FluidPrototype = _module.FluidPrototype
    ItemPrototype = _module.ItemPrototype
    ModulePrototype = _module.ModulePrototype
    RecipePrototype = _module.RecipePrototype


__all__ = [
    "AssemblingMachinePrototype",
    "BeaconPrototype",
    "FactorioDataRaw",
    "FluidPrototype",
    "GeneratedModelsImportError",
    "ItemPrototype",
    "ModulePrototype",
    "RecipePrototype",
]
