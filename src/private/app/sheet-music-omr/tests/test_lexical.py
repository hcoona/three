"""Focused tests for quoted text escapes and rational spellings."""

from __future__ import annotations

from fractions import Fraction

import pytest

from sheet_music_omr.lexical import (
    QuotedTextError,
    format_rational,
    quote_text,
    read_quoted,
    strip_comment,
)


@pytest.mark.parametrize(
    ("source", "decoded"),
    [
        (r'"plain"', "plain"),
        (r'"a\"b\\c\/d"', 'a"b\\c/d'),
        (r'"\t\n\r"', "\t\n\r"),
        (r'"\u00e9\u00E9"', "\u00e9\u00e9"),
        (r'"\ud83c\udfb5"', "\U0001f3b5"),
        ('"\U0001f3b5"', "\U0001f3b5"),
    ],
)
def test_read_quoted_decodes_escapes(source: str, decoded: str) -> None:
    """Quoted text decodes every allowed escape."""
    assert read_quoted(f"{source} rest", 0) == (decoded, len(source))


@pytest.mark.parametrize(
    ("source", "rule_class", "rule"),
    [
        (r'"\b"', "lexical", "text_char"),
        (r'"\u0001"', "lexical", "text_char"),
        (r'"\uFFFE"', "lexical", "text_char"),
        (r'"\udfb5"', "lexical", "surrogate"),
        (r'"\ud83c x"', "lexical", "surrogate"),
        (r'"\x"', "grammar", "text_char"),
        (r'"\u12g4"', "grammar", "text_char"),
        ('"open', "grammar", "quoted"),
    ],
)
def test_read_quoted_rejects(source: str, rule_class: str, rule: str) -> None:
    """Quoted text rejects forbidden escapes and characters."""
    with pytest.raises(QuotedTextError) as caught:
        read_quoted(source, 0)
    assert (caught.value.rule_class, caught.value.rule) == (rule_class, rule)


def test_quote_text_escapes_only_the_canonical_set() -> None:
    """Canonical quoting escapes only the five listed characters."""
    text = 'a"b\\c/d\te\nf\rg\u00e9\U0001f3b5'
    assert quote_text(text) == '"a\\"b\\\\c/d\\te\\nf\\rg\u00e9\U0001f3b5"'
    assert read_quoted(quote_text(text), 0)[0] == text


def test_strip_comment_respects_quotes() -> None:
    """Comments inside quoted text are kept."""
    assert strip_comment('1.1: <"a\\"//b">  // note  ') == '1.1: <"a\\"//b">'
    assert strip_comment("m 1   ") == "m 1"


@pytest.mark.parametrize(
    ("value", "spelling"),
    [(Fraction(0), "0"), (Fraction(3), "3"), (Fraction(6, 8), "3/4")],
)
def test_format_rational(value: Fraction, spelling: str) -> None:
    """Rationals are written reduced."""
    assert format_rational(value) == spelling
