"""Data structures representing the output emitted by `nbgv`."""

from __future__ import annotations

import re
from collections.abc import Iterator, Mapping
from dataclasses import dataclass
from typing import Any, TypeVar, overload

_T = TypeVar("_T")


_SPECIAL_TOKEN_FIXES = {
    "nu_get": "nuget",
    "sem_ver": "semver",
}


def _to_snake_case(name: str) -> str:
    """Convert a PascalCase identifier to snake_case."""
    first_pass = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", name)
    second_pass = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", first_pass)
    snake = second_pass.replace("-", "_").lower()
    for token, replacement in _SPECIAL_TOKEN_FIXES.items():
        snake = snake.replace(token, replacement)
    return snake


@dataclass(frozen=True, slots=True)
class GitVersion(Mapping[str, Any]):
    """Immutable mapping over version fields reported by `nbgv`."""

    _aliases: dict[str, Any]
    raw: dict[str, Any]

    @classmethod
    def from_payload(cls, payload: Mapping[str, Any]) -> GitVersion:
        """Create an instance from the JSON dictionary returned by `nbgv`."""
        if not isinstance(payload, Mapping):
            msg = "Payload must be a mapping"
            raise TypeError(msg)
        raw = {str(key): value for key, value in payload.items()}
        aliases: dict[str, Any] = {}
        for key, value in raw.items():
            aliases[_to_snake_case(key)] = value
        return cls(aliases, raw)

    def __getitem__(self, key: str) -> Any:  # noqa: ANN401
        """Return the value for *key*."""
        return self.require(key)

    def __iter__(self) -> Iterator[str]:
        """Return an iterator over the keys."""
        yield from self._aliases
        for key in self.raw:
            if key not in self._aliases:
                yield key

    def __len__(self) -> int:
        """Return the number of items."""
        return len(self._aliases) + sum(
            1 for key in self.raw if key not in self._aliases
        )

    @overload
    def get(
        self,
        key: str,
        default: None = ...,
    ) -> Any | None:  # type: ignore[override]  # noqa: ANN401
        ...

    @overload
    def get(
        self,
        key: str,
        default: _T,
    ) -> Any | _T:  # type: ignore[override]  # noqa: ANN401
        ...

    def get(
        self,
        key: str,
        default: _T | None = None,
    ) -> Any | _T | None:
        """Return a value using either snake_case or the original key names."""
        if key in self._aliases:
            return self._aliases[key]
        if key in self.raw:
            return self.raw[key]
        lowered = key.lower().replace("-", "_")
        for alias_key, value in self._aliases.items():
            if alias_key.lower() == lowered:
                return value
        for raw_key, value in self.raw.items():
            if raw_key.lower() == lowered:
                return value
        return default

    def require(self, key: str) -> Any:  # noqa: ANN401
        """Return a value or raise `KeyError` when it is unavailable."""
        sentinel = object()
        value = self.get(key, default=sentinel)
        if value is sentinel:
            raise KeyError(key)
        return value

    def as_dict(self, *, include_raw: bool = False) -> dict[str, Any]:
        """Return a copy of the snake_case mapping or the combined mapping."""
        if include_raw:
            merged = dict(self._aliases)
            merged.update(self.raw)
            return merged
        return dict(self._aliases)


__all__ = [
    "GitVersion",
]
