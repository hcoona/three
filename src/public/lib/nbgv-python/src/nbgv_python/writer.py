"""Helpers for writing templated version files."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from collections.abc import Mapping
    from pathlib import Path


@dataclass(frozen=True, slots=True)
class WriteConfig:
    """Configuration for emitting a version artifact."""

    file: Path
    template: str | None = None
    encoding: str = "utf-8"


def write_version_file(
    config: WriteConfig, fields: Mapping[str, object]
) -> None:
    """Render *fields* into the configured file using the template."""
    template = config.template or _default_template(config.file, fields)
    try:
        rendered = template.format(**fields)
    except KeyError as exc:  # pragma: no cover - easier to diagnose via error
        missing = exc.args[0]
        msg = f"Template references unknown field: {missing}"
        raise RuntimeError(msg) from exc
    path = config.file
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"{rendered}\n", encoding=config.encoding)


def _default_template(path: Path, fields: Mapping[str, object]) -> str:
    suffix = path.suffix.lower()
    if suffix == ".py":
        if "normalized_version" in fields:
            return '__version__ = "{normalized_version}"'
        return '__version__ = "{version}"'
    if suffix in {".txt", ""}:
        return "{version}"
    msg = (
        "No default template is defined for files with suffix "
        f"'{path.suffix}'. Please specify 'template'."
    )
    raise RuntimeError(msg)


__all__ = [
    "WriteConfig",
    "write_version_file",
]
