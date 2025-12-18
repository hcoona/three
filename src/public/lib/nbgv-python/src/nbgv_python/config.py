"""Configuration helpers for the hatchling integration."""

from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .command import parse_command_tokens
from .templating import TemplateFieldsConfig, VersionTupleConfig
from .writer import WriteConfig

_ALLOWED_KEYS = {
    "command",
    "epoch",
    "template-fields",
    "version-field",
    "working-directory",
    "write",
}


@dataclass(frozen=True, slots=True)
class PluginConfig:
    """Strongly typed view over the hatch configuration mapping."""

    command: tuple[str, ...] | None
    version_field: str
    working_directory: Path
    template_fields: TemplateFieldsConfig
    write: WriteConfig | None
    epoch: int | None

    @classmethod
    def from_mapping(
        cls,
        root: Path,
        config: Mapping[str, Any] | None,
    ) -> PluginConfig:
        """Parse user configuration under `[tool.hatch.version.nbgv]`."""
        data = dict(config or {})
        template_fields_mapping = data.pop("template-fields", None)
        write_mapping = data.pop("write", None)
        extra = set(data) - _ALLOWED_KEYS
        if extra:
            keys = ", ".join(sorted(extra))
            msg = f"Unsupported configuration keys: {keys}"
            raise ValueError(msg)

        command_value = data.get("command")
        command_tokens: tuple[str, ...] | None = None
        if command_value is not None:
            if isinstance(command_value, Sequence) and not isinstance(
                command_value, str
            ):
                command_tokens = tuple(
                    parse_command_tokens(tuple(command_value))
                )
            else:
                command_tokens = tuple(parse_command_tokens(str(command_value)))

        raw_field = data.get("version-field", "SemVer2")
        if not isinstance(raw_field, str) or not raw_field.strip():
            msg = "'version-field' must be a non-empty string"
            raise ValueError(msg)
        version_field = raw_field.strip()

        raw_directory = data.get("working-directory")
        project_root = Path(root)
        if raw_directory is None:
            working_directory = project_root
        else:
            directory_path = Path(str(raw_directory))
            if not directory_path.is_absolute():
                working_directory = project_root / directory_path
            else:
                working_directory = directory_path

        epoch_value = data.get("epoch")
        epoch = _parse_epoch(epoch_value) if epoch_value is not None else None

        template_fields = _parse_template_fields(template_fields_mapping)
        write_config = _parse_write_config(project_root, write_mapping)

        return cls(
            command_tokens,
            version_field,
            working_directory,
            template_fields,
            write_config,
            epoch,
        )


def _parse_epoch(value: Any) -> int:  # noqa: ANN401
    if isinstance(value, bool):
        msg = "'epoch' must be an integer >= 0"
        raise TypeError(msg)
    if isinstance(value, int):
        epoch = value
    elif isinstance(value, str):
        stripped = value.strip()
        if not stripped:
            msg = "'epoch' must be an integer >= 0"
            raise ValueError(msg)
        try:
            epoch = int(stripped, 10)
        except ValueError as exc:  # pragma: no cover - invalid config
            msg = "'epoch' must be an integer >= 0"
            raise ValueError(msg) from exc
    else:
        msg = "'epoch' must be an integer >= 0"
        raise TypeError(msg)
    if epoch < 0:
        msg = "'epoch' must be an integer >= 0"
        raise ValueError(msg)
    return epoch


def _parse_template_fields(mapping: Any) -> TemplateFieldsConfig:  # noqa: ANN401
    if mapping is None:
        return TemplateFieldsConfig()
    if not isinstance(mapping, Mapping):
        msg = "'template-fields' must be a mapping"
        raise TypeError(msg)
    data = dict(mapping)
    version_tuple_mapping = data.pop("version-tuple", None)
    if data:
        keys = ", ".join(sorted(data))
        msg = f"Unsupported template-fields keys: {keys}"
        raise ValueError(msg)
    version_tuple = _parse_version_tuple(version_tuple_mapping)
    return TemplateFieldsConfig(version_tuple=version_tuple)


def _parse_version_tuple(  # noqa: C901, PLR0912, PLR0915
    mapping: Any,  # noqa: ANN401
) -> VersionTupleConfig:
    if mapping is None:
        return VersionTupleConfig()
    if not isinstance(mapping, Mapping):
        msg = "'version-tuple' must be a mapping"
        raise TypeError(msg)
    data = dict(mapping)
    defaults = VersionTupleConfig()
    mode = data.pop("mode", defaults.mode)
    fields_value = data.pop("fields", defaults.fields)
    epoch = data.pop("epoch", defaults.epoch)
    double_quote = data.pop("double-quote", defaults.double_quote)
    normalized_prerelease = data.pop(
        "normalized-prerelease", defaults.normalized_prerelease
    )
    if data:
        keys = ", ".join(sorted(data))
        msg = f"Unsupported version-tuple keys: {keys}"
        raise ValueError(msg)
    if not isinstance(mode, str) or not mode.strip():
        msg = "'mode' must be a non-empty string"
        raise ValueError(msg)
    normalized_mode = mode.strip().lower()
    if normalized_mode not in {"nbgv", "pep440"}:
        msg = "'mode' must be either 'nbgv' or 'pep440'"
        raise ValueError(msg)
    nbgv_fields: tuple[str, ...]
    if isinstance(fields_value, str):
        candidate = fields_value.strip()
        if not candidate:
            msg = "'fields' cannot contain empty names"
            raise ValueError(msg)
        nbgv_fields = (candidate,)
    elif isinstance(fields_value, Sequence) and not isinstance(
        fields_value, str
    ):
        collected: list[str] = []
        for element in fields_value:
            if not isinstance(element, str) or not element.strip():
                msg = "'fields' entries must be non-empty strings"
                raise ValueError(msg)
            collected.append(element.strip())
        nbgv_fields = tuple(collected)
    else:
        msg = "'fields' must be a string or sequence of strings"
        raise TypeError(msg)
    if not nbgv_fields:
        msg = "'fields' must contain at least one entry"
        raise ValueError(msg)
    if epoch is not None and not isinstance(epoch, bool):
        msg = "'epoch' must be a boolean when provided"
        raise TypeError(msg)
    if not isinstance(double_quote, bool):
        msg = "'double-quote' must be a boolean"
        raise TypeError(msg)
    if not isinstance(normalized_prerelease, bool):
        msg = "'normalized-prerelease' must be a boolean"
        raise TypeError(msg)
    return VersionTupleConfig(
        mode=normalized_mode,
        fields=nbgv_fields,
        epoch=epoch,
        double_quote=double_quote,
        normalized_prerelease=normalized_prerelease,
    )


def _parse_write_config(
    root: Path,
    mapping: Any,  # noqa: ANN401
) -> WriteConfig | None:
    if mapping is None:
        return None
    if not isinstance(mapping, Mapping):
        msg = "'write' must be a mapping"
        raise TypeError(msg)
    data = dict(mapping)
    file_value = data.pop("file", None)
    if not isinstance(file_value, str) or not file_value.strip():
        msg = "'write.file' must be a non-empty string"
        raise ValueError(msg)
    file_path = Path(file_value)
    if not file_path.is_absolute():
        file_path = root / file_path
    template = data.pop("template", None)
    if template is not None and not isinstance(template, str):
        msg = "'write.template' must be a string when provided"
        raise ValueError(msg)
    encoding = data.pop("encoding", "utf-8")
    if not isinstance(encoding, str) or not encoding:
        msg = "'write.encoding' must be a non-empty string"
        raise ValueError(msg)
    if data:
        keys = ", ".join(sorted(data))
        msg = f"Unsupported write keys: {keys}"
        raise ValueError(msg)
    return WriteConfig(file=file_path, template=template, encoding=encoding)


__all__ = ["PluginConfig"]
