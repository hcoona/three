"""Lexical rules and preprocessing of Text Score 2.

This module implements the "Lexical rules and preprocessing" section of the
format: the document-level character rules, comment and trailing-space
removal, quoted text with JSON escapes and the XML 1.0 character rule, and the
canonical spelling of text and rationals.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING

from sheet_music_omr.diagnostics import (
    Diagnostic,
    Location,
    RuleClass,
    TextScoreError,
)

if TYPE_CHECKING:
    from fractions import Fraction

_BOM = "\ufeff"
_SIMPLE_ESCAPES = {
    '"': '"',
    "\\": "\\",
    "/": "/",
    "n": "\n",
    "r": "\r",
    "t": "\t",
}
_FORBIDDEN_ESCAPES = {"b": "\\b", "f": "\\f"}
_CANONICAL_ESCAPES = {
    '"': '\\"',
    "\\": "\\\\",
    "\t": "\\t",
    "\n": "\\n",
    "\r": "\\r",
}
_HEX_DIGITS = frozenset("0123456789abcdefABCDEF")
_HEX_ESCAPE_DIGITS = 4
_HIGH_SURROGATES = range(0xD800, 0xDC00)
_LOW_SURROGATES = range(0xDC00, 0xE000)
_FIRST_PRINTABLE = 0x20
_ALLOWED_CONTROLS = frozenset({0x09, 0x0A, 0x0D})


@dataclass(frozen=True, slots=True)
class SourceLine:
    """A normalized line and its 1-based line number in the original text."""

    number: int
    text: str


class QuotedTextError(Exception):
    """A quoted-text violation at an index of its line."""

    def __init__(
        self,
        rule_class: RuleClass,
        rule: str,
        message: str,
        index: int,
    ) -> None:
        """Create the error for ``index`` of the scanned line."""
        super().__init__(message)
        self.rule_class = rule_class
        self.rule = rule
        self.message = message
        self.index = index


def _location_of(text: str, index: int) -> Location:
    line = text.count("\n", 0, index) + 1
    line_start = text.rfind("\n", 0, index) + 1
    return Location(line, index - line_start + 1)


def _lexical(
    *,
    rule: str,
    message: str,
    location: Location,
) -> TextScoreError:
    return TextScoreError(
        (Diagnostic(RuleClass.LEXICAL, rule, message, location),),
    )


def decode_source(data: bytes) -> str:
    """Decode UTF-8 document bytes.

    Args:
        data: The raw document bytes.

    Returns:
        The decoded text.

    Raises:
        TextScoreError: When the bytes are not UTF-8.
    """
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError as error:
        prefix = data[: error.start].decode("utf-8")
        raise _lexical(
            rule="encoding",
            message="the document is not valid UTF-8",
            location=_location_of(prefix, len(prefix)),
        ) from error


def check_document_characters(text: str) -> None:
    """Check the document-level lexical rules.

    The rules are: no byte order mark, LF line ends only, no tab characters
    and a final LF.

    Raises:
        TextScoreError: For the first violation in text order.
    """
    if text.startswith(_BOM):
        raise _lexical(
            rule="encoding",
            message="the document starts with a byte order mark",
            location=Location(1, 1),
        )
    for index, char in enumerate(text):
        if char == "\r":
            raise _lexical(
                rule="line_end",
                message="a carriage return is not allowed; lines end with LF",
                location=_location_of(text, index),
            )
        if char == "\t":
            raise _lexical(
                rule="tab",
                message="tab characters are not allowed",
                location=_location_of(text, index),
            )
    if text and not text.endswith("\n"):
        raise _lexical(
            rule="final_lf",
            message="the document does not end with a line feed",
            location=_location_of(text, len(text)),
        )


def strip_comment(line: str) -> str:
    """Remove a ``//`` comment outside quoted text and trailing spaces."""
    in_quote = False
    index = 0
    while index < len(line):
        char = line[index]
        if in_quote:
            if char == "\\":
                index += 1
            elif char == '"':
                in_quote = False
        elif char == '"':
            in_quote = True
        elif line.startswith("//", index):
            line = line[:index]
            break
        index += 1
    return line.rstrip(" ")


def preprocess(text: str) -> tuple[SourceLine, ...]:
    """Normalize a document into its non-empty lines.

    Args:
        text: The document text, which must already satisfy
            ``check_document_characters``.

    Returns:
        The normalized lines with their original line numbers.
    """
    lines = text.split("\n")
    if lines and not lines[-1]:
        lines.pop()
    normalized = (
        SourceLine(number, strip_comment(line))
        for number, line in enumerate(lines, start=1)
    )
    return tuple(line for line in normalized if line.text)


def end_location(text: str) -> Location:
    """Return the location just after the last line of ``text``."""
    return Location(text.count("\n") + 1, 1)


def _is_xml_char(code_point: int) -> bool:
    if code_point < _FIRST_PRINTABLE:
        return code_point in _ALLOWED_CONTROLS
    if code_point in _HIGH_SURROGATES or code_point in _LOW_SURROGATES:
        return False
    return code_point not in {0xFFFE, 0xFFFF}


def _read_hex4(line: str, start: int) -> int:
    digits = line[start : start + _HEX_ESCAPE_DIGITS]
    if len(digits) != _HEX_ESCAPE_DIGITS or any(
        d not in _HEX_DIGITS for d in digits
    ):
        raise QuotedTextError(
            RuleClass.GRAMMAR,
            "text_char",
            "expected four hexadecimal digits after \\u",
            start,
        )
    return int(digits, 16)


def _read_unicode_escape(line: str, start: int) -> tuple[str, int]:
    r"""Decode ``\uXXXX`` at ``start``, joining a surrogate pair."""
    unit = _read_hex4(line, start + 2)
    end = start + 6
    if unit in _LOW_SURROGATES:
        raise QuotedTextError(
            RuleClass.LEXICAL,
            "surrogate",
            "a low surrogate escape must follow a high surrogate escape",
            start,
        )
    if unit in _HIGH_SURROGATES:
        if line.startswith("\\u", end):
            low = _read_hex4(line, end + 2)
            if low in _LOW_SURROGATES:
                code_point = 0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00)
                return chr(code_point), end + 6
        raise QuotedTextError(
            RuleClass.LEXICAL,
            "surrogate",
            "a high surrogate escape must be followed by a low surrogate"
            " escape",
            start,
        )
    if not _is_xml_char(unit):
        raise QuotedTextError(
            RuleClass.LEXICAL,
            "text_char",
            f"the escaped character U+{unit:04X} is not allowed in text",
            start,
        )
    return chr(unit), end


def read_quoted(line: str, start: int) -> tuple[str, int]:
    """Read quoted text that starts at ``line[start]``.

    Args:
        line: The normalized line.
        start: The index of the opening quote.

    Returns:
        The decoded text and the index after the closing quote.

    Raises:
        QuotedTextError: When the text breaks a grammar or lexical rule.
    """
    if not line.startswith('"', start):
        raise QuotedTextError(
            RuleClass.GRAMMAR,
            "quoted",
            "expected quoted text",
            start,
        )
    parts: list[str] = []
    index = start + 1
    while index < len(line):
        char = line[index]
        if char == '"':
            return "".join(parts), index + 1
        if char == "\\":
            escape = line[index + 1 : index + 2]
            if escape in _SIMPLE_ESCAPES:
                parts.append(_SIMPLE_ESCAPES[escape])
                index += 2
            elif escape in _FORBIDDEN_ESCAPES:
                raise QuotedTextError(
                    RuleClass.LEXICAL,
                    "text_char",
                    f"the escape {_FORBIDDEN_ESCAPES[escape]} is not allowed",
                    index,
                )
            elif escape == "u":
                decoded, index = _read_unicode_escape(line, index)
                parts.append(decoded)
            else:
                raise QuotedTextError(
                    RuleClass.GRAMMAR,
                    "text_char",
                    "invalid escape in quoted text",
                    index,
                )
            continue
        code_point = ord(char)
        if code_point < _FIRST_PRINTABLE:
            raise QuotedTextError(
                RuleClass.LEXICAL,
                "text_char",
                f"raw control character U+{code_point:04X} in quoted text",
                index,
            )
        if not _is_xml_char(code_point):
            raise QuotedTextError(
                RuleClass.LEXICAL,
                "text_char",
                f"the character U+{code_point:04X} is not allowed in text",
                index,
            )
        parts.append(char)
        index += 1
    raise QuotedTextError(
        RuleClass.GRAMMAR,
        "quoted",
        "unterminated quoted text",
        len(line),
    )


def quote_text(text: str) -> str:
    """Return the canonical quoted spelling of ``text``."""
    escaped = "".join(_CANONICAL_ESCAPES.get(char, char) for char in text)
    return f'"{escaped}"'


def format_rational(value: Fraction) -> str:
    """Return the reduced spelling of a non-negative rational."""
    if value.denominator == 1:
        return str(value.numerator)
    return f"{value.numerator}/{value.denominator}"
