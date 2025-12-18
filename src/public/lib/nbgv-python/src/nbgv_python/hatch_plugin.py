"""Hatchling integration that surfaces `nbgv` as a version source."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from hatchling.plugin import hookimpl
from hatchling.version.source.plugin.interface import VersionSourceInterface

from .config import PluginConfig
from .runner import NbgvRunner
from .templating import build_template_fields
from .versioning import normalize_version_field
from .writer import write_version_file


class NbgvVersionSource(VersionSourceInterface):
    """Hatch plugin that resolves package versions via `nbgv`."""

    PLUGIN_NAME = "nbgv"

    def __init__(
        self,
        *args: Any,  # noqa: ANN401
        **kwargs: Any,  # noqa: ANN401
    ) -> None:  # pragma: no cover
        """Initialize the plugin."""
        super().__init__(*args, **kwargs)
        self._config: PluginConfig | None = None
        self._version_data: dict[str, Any] | None = None
        self._template_fields: dict[str, Any] | None = None

    def get_version_data(self) -> dict[str, Any]:
        """Return the version mapping consumed by hatchling."""
        mapping = {}
        if self.config is not None:
            mapping = dict(self.config.get("nbgv", {}))
        config = PluginConfig.from_mapping(Path(self.root), mapping)
        self._config = config
        runner = NbgvRunner(command=config.command)
        version = runner.get_version(config.working_directory)
        selected = version.get(config.version_field)
        if selected is None:
            message = (
                f"Field '{config.version_field}' was not produced by "
                "'nbgv get-version'"
            )
            raise RuntimeError(message)
        normalized = normalize_version_field(
            selected, field=config.version_field
        )
        if config.epoch is not None:
            normalized = f"{config.epoch}!{normalized}"
        metadata = version.as_dict(include_raw=True)
        fields = build_template_fields(
            version=selected,
            normalized_version=normalized,
            version_info=metadata,
            config=config.template_fields,
        )
        if config.write is not None:
            write_version_file(config.write, fields)
        data = {
            "version": normalized,
            "metadata": metadata,
            "template_fields": fields,
        }
        self._version_data = data
        self._template_fields = fields
        return data

    def set_version(  # pragma: no cover - hatch isolates CLI so we cannot test
        self, version: str, version_data: dict[str, Any]
    ) -> None:
        """Raise an error as version setting is not supported."""
        message = (
            "The nbgv hatch plugin does not support mutating the project "
            "version. Create a Git tag instead."
        )
        raise NotImplementedError(message)

    def get_template_fields(self) -> dict[str, Any] | None:
        """Expose template fields for potential build hooks."""
        return self._template_fields


@hookimpl
def hatch_register_version_source() -> type[VersionSourceInterface]:
    """Register the nbgv version source with hatch."""
    return NbgvVersionSource


__all__ = [
    "NbgvVersionSource",
    "hatch_register_version_source",
]
