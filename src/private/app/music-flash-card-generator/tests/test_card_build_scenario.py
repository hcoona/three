"""Scenario tests for planning music flash card builds."""

import re
import sys
from pathlib import Path
from runpy import run_path

import pytest

PROJECT = Path(__file__).parents[1]
MAIN = PROJECT / "main.py"
MERGE = PROJECT / "generate_merged_ly_file.py"


def _run_cli(
    script: Path, monkeypatch: pytest.MonkeyPatch, *arguments: str
) -> None:
    monkeypatch.setattr(sys, "argv", [str(script), *arguments])
    run_path(str(script), run_name="__main__")


def _build_edges(ninja_file: Path) -> dict[str, tuple[str, list[str]]]:
    """Map each build output to its rule and explicit inputs."""
    text = re.sub(r" \$\n +", " ", ninja_file.read_text(encoding="utf-8"))
    edges: dict[str, tuple[str, list[str]]] = {}
    for line in text.splitlines():
        if not line.startswith("build "):
            continue
        outputs, _, rest = line.removeprefix("build ").partition(": ")
        rule, *inputs = rest.split()
        for output in outputs.split():
            edges[output] = (rule, inputs)
    return edges


def test_each_lilypond_file_becomes_a_two_sided_card(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Plan front, back and audio outputs for every LilyPond input."""
    input_dir = tmp_path / "input"
    input_dir.mkdir()
    scale = input_dir / "c-major-scale.ly"
    scale.write_text("scoreToInclude = { c'4 d' e' f' }\n", encoding="utf-8")
    (input_dir / "notes.txt").write_text("not a card\n", encoding="utf-8")
    output_dir = tmp_path / "cards"
    working_dir = tmp_path / "work"

    _run_cli(
        MAIN,
        monkeypatch,
        "--input_dir",
        str(input_dir),
        "--output_dir",
        str(output_dir),
        "--working_dir",
        str(working_dir),
    )

    assert output_dir.is_dir()
    edges = _build_edges(working_dir / "build.ninja")
    assert edges["all"] == ("phony", [str(scale)])
    for card_output in ("front.png", "back.png", "mp3"):
        assert str(output_dir / f"c-major-scale.{card_output}") in edges
    assert not any("notes" in output for output in edges)
    assert edges[str(working_dir / "c-major-scale_front.ly")][1] == [str(scale)]
    assert edges[str(working_dir / "c-major-scale_back.ly")][1] == [str(scale)]


def test_merged_lilypond_file_includes_the_user_score_and_template(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Wrap a user score with the card-side template LilyPond compiles."""
    user_file = tmp_path / "c-major-scale.ly"
    user_file.write_text("scoreToInclude = { c'4 }\n", encoding="utf-8")
    template = PROJECT / "data" / "front.ly"
    merged = tmp_path / "c-major-scale_front.ly"

    _run_cli(
        MERGE,
        monkeypatch,
        "--user_file",
        str(user_file),
        "--template_file",
        str(template),
        "--output_file",
        str(merged),
    )

    assert merged.read_text(encoding="utf-8").splitlines() == [
        '\\version "2.24.3"',
        f'\\include "{user_file}"',
        f'\\include "{template}"',
    ]
