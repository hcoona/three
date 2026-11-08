"""Tests for the harness's write confinement and MuseScore wrapper."""

from __future__ import annotations

import json
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

from sheet_music_omr.harness import musescore
from sheet_music_omr.harness.confine import Confinement, ConfinementError
from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Sequence


@pytest.fixture
def roots(tmp_path: Path) -> Confinement:
    """Return fresh run and download roots."""
    return Confinement.create(tmp_path / "run", tmp_path / "downloads")


def test_confinement_rejects_nested_roots(tmp_path: Path) -> None:
    """Overlapping roots would let a run write into the downloads."""
    with pytest.raises(ConfinementError):
        Confinement.create(tmp_path / "run", tmp_path / "run" / "downloads")


def test_confinement_refuses_writes_outside_its_roots(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A path outside the roots, even through ``..``, is never written."""
    outside = tmp_path / "elsewhere.txt"
    escape = roots.run / ".." / "escape.txt"

    for target in (outside, escape):
        with pytest.raises(StopConditionError):
            roots.write_text(target, "x")
        assert not target.exists()


def test_confinement_writes_atomically_inside_its_roots(
    roots: Confinement,
) -> None:
    """A rewrite replaces the file and leaves no temporary file behind."""
    target = roots.run / "a" / "b.json"

    roots.write_text(target, "first")
    roots.write_text(target, "second")

    assert target.read_text(encoding="utf-8") == "second"
    assert [p.name for p in target.parent.iterdir()] == ["b.json"]
    assert roots.temp_dir("x").parent == roots.temp


def test_read_preferences_reads_qt_sections_and_defaults() -> None:
    """Qt section keys map to settings keys; unset keys take defaults."""
    text = (
        "[General]\nfoo=1\n"
        "[import]\nmusicXml\\importBreaks=false\n"
        '[export]\nmusicXml\\exportBreaks="Manual"\n'
    )

    preferences = musescore.read_preferences(text)

    assert preferences["import/musicXml/importBreaks"] == {
        "label": "import breaks",
        "value": "false",
        "source": "file",
    }
    assert preferences["export/musicXml/exportBreaks"]["value"] == "Manual"
    assert preferences["import/musicXml/importLayout"] == {
        "label": "import layout",
        "value": "true",
        "source": "default",
    }


def test_sanitize_log_removes_private_paths() -> None:
    """The log keeps its last 20 lines and no private path or name."""
    run = r"Q:\data\runs\one"
    log = "\n".join(
        [
            *(f"line {i}" for i in range(30)),
            rf"wrote {run}\tmp\x.musicxml",
            "user alice C:/Users/alice/file",
        ],
    )

    sanitized = musescore.sanitize_log(log, (run, "alice"))

    assert "alice" not in sanitized
    assert "runs" not in sanitized
    assert "line 9" not in sanitized
    assert sanitized.splitlines()[0] == "line 12"


class _FakeLauncher:
    def __init__(self, *, settings: Path | None = None) -> None:
        self.commands: list[list[str]] = []
        self.settings = settings

    def __call__(
        self,
        command: Sequence[str],
        timeout: float,
    ) -> tuple[int | None, str]:
        del timeout
        self.commands.append(list(command))
        output = command[command.index("-o") + 1]
        Path(output).write_text("<score/>", encoding="utf-8")
        if self.settings is not None:
            self.settings.write_text("changed", encoding="utf-8")
        return 0, f"converted to {output}"


def _open(
    roots: Confinement,
    tmp_path: Path,
    launcher: _FakeLauncher,
    *,
    version: str = musescore.VERSION,
) -> musescore.MuseScore:
    executable = tmp_path / "MuseScore4.exe"
    executable.write_bytes(b"")
    settings = tmp_path / "MuseScore4.ini"
    if not settings.exists():
        settings.write_text("[ui]\nx=1\n", encoding="utf-8")
    return musescore.MuseScore.open(
        roots,
        executable=executable,
        settings=settings,
        version_lookup=lambda: version,
        launcher=launcher,
    )


def test_open_stops_on_another_version(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Another MuseScore version is a stop condition."""
    with pytest.raises(StopConditionError, match="version"):
        _open(roots, tmp_path, _FakeLauncher(), version="4.6.0")


def test_open_copies_settings_once_and_stops_on_a_later_change(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """The first copy is kept; a later invocation must match its hash."""
    _open(roots, tmp_path, _FakeLauncher())
    (tmp_path / "MuseScore4.ini").write_text("[ui]\nx=2\n", encoding="utf-8")

    with pytest.raises(StopConditionError, match="recorded settings"):
        _open(roots, tmp_path, _FakeLauncher())

    copy = roots.run / musescore.SETTINGS_COPY
    assert copy.read_text(encoding="utf-8") == "[ui]\nx=1\n"


def test_convert_uses_only_the_output_option(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A conversion passes only ``-o`` and logs the hash around a batch."""
    launcher = _FakeLauncher()
    app = _open(roots, tmp_path, launcher)
    source = tmp_path / "in.musicxml"
    source.write_text("<score/>", encoding="utf-8")
    output = roots.temp / "a" / "out.mscz"

    with app.batch("pilot"):
        result = app.convert(source, output)

    assert result.ok
    assert str(tmp_path) not in result.log
    (command,) = launcher.commands
    assert command[1:] == ["-o", str(output), str(source)]
    log = (roots.run / musescore.HASH_LOG).read_text(encoding="utf-8")
    entries = [json.loads(line) for line in log.splitlines()]
    assert [e["when"] for e in entries] == ["before", "after"]
    assert {e["sha256"] for e in entries} == {app.baseline}


def test_convert_refuses_output_outside_roots(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """MuseScore is not launched for an output outside the roots."""
    launcher = _FakeLauncher()
    app = _open(roots, tmp_path, launcher)

    with pytest.raises(ConfinementError):
        app.convert(tmp_path / "in.musicxml", tmp_path / "out.mscz")
    assert launcher.commands == []


def test_convert_stops_when_settings_change(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A settings change during a conversion is a stop condition."""
    launcher = _FakeLauncher(settings=tmp_path / "MuseScore4.ini")
    app = _open(roots, tmp_path, launcher)

    with pytest.raises(StopConditionError, match="hash changed"):
        app.convert(tmp_path / "in.musicxml", roots.temp / "out.mscz")
