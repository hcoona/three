"""Diagnostics shared by the parser and later validators.

A diagnostic names the rule class that the format defines (lexical, grammar,
semantic, assembly or unsupported), a stable rule name within that class and a
1-based location in the original text.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum


class RuleClass(StrEnum):
    """The rule classes that Text Score 2 names for explicit errors."""

    LEXICAL = "lexical"
    GRAMMAR = "grammar"
    SEMANTIC = "semantic"
    ASSEMBLY = "assembly"
    UNSUPPORTED = "unsupported"


@dataclass(frozen=True, slots=True, order=True)
class Location:
    """A 1-based line and column in the original document text.

    Columns count Unicode code points.
    """

    line: int
    column: int

    def __str__(self) -> str:
        """Return the location as ``line:column``."""
        return f"{self.line}:{self.column}"


@dataclass(frozen=True, slots=True)
class Diagnostic:
    """One reported rule violation.

    Attributes:
        rule_class: The rule class of the violated rule.
        rule: A stable rule name, such as the EBNF rule for grammar errors.
        message: A human-readable description.
        location: Where the violation is in the original text.
    """

    rule_class: RuleClass
    rule: str
    message: str
    location: Location

    @property
    def code(self) -> str:
        """Return the rule class and rule name as ``class/rule``."""
        return f"{self.rule_class}/{self.rule}"

    def format(self, path: str) -> str:
        """Return the ``path:line:col: class/rule: message`` spelling."""
        return f"{path}:{self.location}: {self.code}: {self.message}"


class TextScoreError(Exception):
    """Raised when a document violates a rule of the format."""

    def __init__(self, diagnostics: tuple[Diagnostic, ...]) -> None:
        """Create an error that carries one or more diagnostics."""
        super().__init__("; ".join(d.format("<text>") for d in diagnostics))
        self.diagnostics = diagnostics
