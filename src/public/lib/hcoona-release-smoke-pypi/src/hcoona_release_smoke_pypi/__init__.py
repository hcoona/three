"""Smoke package for validating the PyPI release workflow."""

from __future__ import annotations

from ._version import __version__

__all__ = ["__version__", "smoke_message"]


def smoke_message() -> str:
    """Return a stable marker used by smoke-test scenarios."""
    return "hcoona-release-smoke-pypi"
