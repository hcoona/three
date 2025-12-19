"""Utilities for building template fields from NBGV metadata."""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Any

from packaging.version import InvalidVersion, Version

from .versioning import normalize_version_field

if TYPE_CHECKING:
    from collections.abc import Mapping

DEFAULT_NBGV_FIELDS: tuple[str, ...] = (
    "VersionMajor",
    "VersionMinor",
    "BuildNumber",
    "PrereleaseVersionNoLeadingHyphen",
)


@dataclass(frozen=True, slots=True)
class VersionTupleConfig:
    """Options controlling how the version tuple is rendered."""

    mode: str = "nbgv"
    fields: tuple[str, ...] = DEFAULT_NBGV_FIELDS
    epoch: bool | None = None
    double_quote: bool = True
    normalized_prerelease: bool = False


@dataclass(frozen=True, slots=True)
class TemplateFieldsConfig:
    """Configuration for template-field construction."""

    version_tuple: VersionTupleConfig = VersionTupleConfig()


def build_template_fields(
    *,
    version: str,
    version_info: dict[str, Any],
    normalized_version: str | None = None,
    config: TemplateFieldsConfig | None = None,
) -> dict[str, Any]:
    """Produce the mapping used to render templates."""
    tmpl_config = config or TemplateFieldsConfig()
    final_version = version
    normalized = normalized_version or normalize_version_field(
        version, field="version"
    )
    tuple_text = _render_version_tuple(
        normalized,
        version_info,
        tmpl_config.version_tuple,
    )
    fields: dict[str, Any] = {
        "version": final_version,
        "normalized_version": normalized,
        "version_tuple": tuple_text,
    }
    fields.update(version_info)
    return fields


def _render_version_tuple(
    normalized_value: str,
    version_info: Mapping[str, Any],
    config: VersionTupleConfig,
) -> str:
    if config.mode.lower() == "pep440":
        return _render_pep440_tuple(normalized_value, config)
    return _render_nbgv_tuple(version_info, normalized_value, config)


def _render_nbgv_tuple(
    version_info: Mapping[str, Any],
    normalized_value: str,
    config: VersionTupleConfig,
) -> str:
    components: list[str] = []
    normalized_pre: str | None = None
    if config.normalized_prerelease:
        normalized_pre = _extract_normalized_prerelease(normalized_value)
    for field in config.fields:
        value = None
        if (
            normalized_pre is not None
            and field.lower() == "prereleaseversionnoleadinghyphen"
        ):
            value = normalized_pre
        else:
            try:
                value = _lookup_metadata_field(version_info, field)
            except KeyError as exc:  # pragma: no cover - configuration error
                message = (
                    f"Version tuple field '{field}' was not emitted by "
                    "'nbgv get-version'."
                )
                raise RuntimeError(message) from exc
        if value is None:
            continue
        if isinstance(value, str) and value == "":
            continue
        components.append(
            _format_component(value, double_quote=config.double_quote)
        )
    return _format_tuple(components)


def _render_pep440_tuple(value: str, config: VersionTupleConfig) -> str:
    try:
        parsed = Version(value)
    except InvalidVersion:
        msg = (
            "PEP 440 normalization is enabled but the version is invalid: "
            f"{value}"
        )
        raise RuntimeError(msg) from None
    components: list[str] = []
    include_epoch = False
    if config.epoch is True:
        include_epoch = True
    elif config.epoch is False:
        include_epoch = False
    elif parsed.epoch != 0:
        include_epoch = True
    if include_epoch:
        components.append(str(parsed.epoch))
    components.extend(str(number) for number in parsed.release)
    if parsed.pre:
        components.append(
            _quote(
                f"{parsed.pre[0]}{parsed.pre[1]}",
                double_quote=config.double_quote,
            )
        )
    if parsed.post is not None:
        components.append(_quote("post", double_quote=config.double_quote))
        components.append(str(parsed.post))
    if parsed.dev is not None:
        components.append(_quote("dev", double_quote=config.double_quote))
        components.append(str(parsed.dev))
    if parsed.local is not None:
        components.append(_quote("+", double_quote=config.double_quote))
        components.append(
            _quote(parsed.local, double_quote=config.double_quote)
        )
    return _format_tuple(components)


def _format_tuple(parts: list[str]) -> str:
    inner = ", ".join(parts)
    if not parts:
        return "()"
    if len(parts) == 1:
        return f"({inner},)"
    return f"({inner})"


def _format_component(
    value: Any,  # noqa: ANN401
    *,
    double_quote: bool,
) -> str:
    if isinstance(value, bool):
        return "True" if value else "False"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return str(value)
    text = str(value)
    if text.isdigit():
        return str(int(text))
    return _quote(text, double_quote=double_quote)


def _quote(text: str, *, double_quote: bool) -> str:
    quote = '"' if double_quote else "'"
    return f"{quote}{text}{quote}"


def _lookup_metadata_field(
    info: Mapping[str, Any],
    name: str,
) -> Any:  # noqa: ANN401
    if name in info:
        return info[name]
    lowered = name.lower()
    for key, value in info.items():
        if key.lower() == lowered:
            return value
    raise KeyError(name)


def _extract_normalized_prerelease(value: str) -> str | None:
    try:
        version = Version(value)
    except InvalidVersion:  # pragma: no cover - normalized inputs expected
        return None
    if version.pre is None:
        return None
    label, number = version.pre
    return f"{label}{number}"


__all__ = [
    "DEFAULT_NBGV_FIELDS",
    "TemplateFieldsConfig",
    "VersionTupleConfig",
    "build_template_fields",
]
