"""Helpers that expose the dynamically sourced package version."""

from __future__ import annotations

from importlib.metadata import PackageNotFoundError
from importlib.metadata import version as get_version_info

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
    )
    from ._version import (
        PEP440_VERSION as _GENERATED_PEP440,
    )
    from ._version import (
        RAW_VERSION as _GENERATED_RAW,
    )
    from ._version import (
        SEMVER2 as _GENERATED_SEMVER2,
    )
    from ._version import (
        VERSION_TUPLE as _GENERATED_TUPLE,
    )
    from ._version import (
        __version__ as _generated_version,
    )
    from ._version import (
        pep440_version as _generated_pep440,
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
    "GIT_COMMIT",
    "PEP440_VERSION",
    "RAW_VERSION",
    "SEMVER2",
    "VERSION_TUPLE",
    "__version__",
    "get_version",
    "pep440_version",
]


def get_version() -> str:
    """Return the installed package version."""
    return __version__
