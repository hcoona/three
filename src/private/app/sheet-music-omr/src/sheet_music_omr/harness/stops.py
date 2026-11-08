"""The stop conditions of the comparison protocol.

A run that meets one stops before its next step and returns to the owner or
to review. Each stop is an exception of :class:`StopConditionError`, which no
step handles as a file failure.
"""

from __future__ import annotations


class StopConditionError(Exception):
    """A stop condition of the comparison protocol holds."""
