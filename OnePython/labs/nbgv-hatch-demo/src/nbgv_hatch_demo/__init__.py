"""Helpers that expose the dynamically sourced package version."""

from __future__ import annotations

from importlib.metadata import PackageNotFoundError, version as get_version_info

RAW_VERSION: str = ""
SEMVER2: str = ""
PEP440_VERSION: str = ""
VERSION_TUPLE: tuple[object, ...] = ()
GIT_COMMIT: str = ""


def pep440_version(value: str) -> str:
    """Normalize arbitrary version strings to a PEP 440 representation."""

    return value


try:  # pragma: no cover - exercised via Hatch build
    from ._version import (  # type: ignore[attr-defined]
        GIT_COMMIT as _GENERATED_COMMIT,
        PEP440_VERSION as _GENERATED_PEP440,
        RAW_VERSION as _GENERATED_RAW,
        SEMVER2 as _GENERATED_SEMVER2,
        VERSION_TUPLE as _GENERATED_TUPLE,
        pep440_version as _generated_pep440,
        __version__ as _generated_version,
    )
except ImportError:
    try:
        __version__ = get_version_info(__name__)
    except PackageNotFoundError:  # pragma: no cover - import-time fallback
        __version__ = "0.0.0"
    RAW_VERSION = __version__
    SEMVER2 = __version__
    PEP440_VERSION = __version__
    VERSION_TUPLE = (__version__,)
    GIT_COMMIT = ""
else:
    __version__ = _generated_version
    RAW_VERSION = _GENERATED_RAW
    SEMVER2 = _GENERATED_SEMVER2
    PEP440_VERSION = _GENERATED_PEP440
    VERSION_TUPLE = tuple(_GENERATED_TUPLE)
    GIT_COMMIT = _GENERATED_COMMIT
    pep440_version = _generated_pep440


__all__ = [
    "RAW_VERSION",
    "SEMVER2",
    "PEP440_VERSION",
    "VERSION_TUPLE",
    "GIT_COMMIT",
    "pep440_version",
    "__version__",
    "get_version",
]


def get_version() -> str:
    """Return the installed package version."""

    return __version__
