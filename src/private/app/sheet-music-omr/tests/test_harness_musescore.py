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
    unverified = musescore.read_preferences(text, version="4.8.0.1")
    assert (
        unverified["import/musicXml/importLayout"]["source"]
        == "assumed_v4_7_6_default"
    )
    assert musescore.has_source_verified_defaults("4.7.7.261109195")
    assert not musescore.has_source_verified_defaults("4.8.0.1")


def test_sanitize_log_removes_private_paths() -> None:
    """The log keeps its last 20 lines and no private path or name."""
    run = r"Q:\data\runs\one"
    log = "\n".join(
        [
            *(f"line {i}" for i in range(30)),
            rf"wrote {run}\tmp\x.musicxml",
            r"wsl \\wsl.localhost\Ubuntu\home\alice\run\x.musicxml",
            "unc //wsl.localhost/Ubuntu/tmp/spaced path/x.musicxml",
            "kv file=//wsl.localhost/Ubuntu/home/eve/x.musicxml",
            r"share \\fileserver\private\score 1.mxl",
            'quoted "D:\\Gold Scores\\Piece 1.mxl" failed',
            "mnt /mnt/c/Users/bob/AppData/Roaming/MuseScore/MuseScore4.ini",
            "mnt-data /mnt/d/Gold Scores/Piece 2.mxl",
            "custom-root /c/Users/frank/score.mxl",
            r"profile C:\Users\alice\Documents\Secret Piece.mscz",
            "posix /home/grace/private/score.mxl",
            "user alice C:/Users/alice/file",
        ],
    )

    sanitized = musescore.sanitize_log(
        log,
        (run, "alice", r"C:\Users\alice", "/home/grace"),
    )

    for secret in (
        "alice",
        "bob",
        "eve",
        "frank",
        "grace",
        "wsl.localhost",
        "fileserver",
        "Gold Scores",
        "Piece 1",
        "Piece 2",
        "Secret Piece",
        "Documents",
        "runs",
    ):
        assert secret not in sanitized
    assert "line 9" not in sanitized
    assert sanitized.splitlines()[0] == "line 22"


def test_settings_path_fails_closed_and_translates_windows_appdata(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Unresolved, corrupted or missing %APPDATA% stops; valid paths pass."""
    for bad in (
        {},
        {"APPDATA": ""},
        {"APPDATA": "%APPDATA%"},
        {"APPDATA": "C:\\Users\\\ufffd\\AppData\\Roaming"},
    ):
        with pytest.raises(StopConditionError, match="%APPDATA%"):
            musescore.settings_path(bad)

    monkeypatch.setenv("APPDATA", str(tmp_path / "missing-appdata"))
    with pytest.raises(StopConditionError, match="does not exist"):
        musescore.settings_path()

    existing = tmp_path / "Roaming"
    existing.mkdir()
    monkeypatch.setenv("APPDATA", str(existing))
    assert (
        musescore.settings_path() == existing / "MuseScore" / "MuseScore4.ini"
    )

    translated = musescore.settings_path(
        {"APPDATA": r"C:\Users\bob\AppData\Roaming"},
        to_posix=lambda raw: (
            "/mnt/c/Users/bob/AppData/Roaming" if "bob" in raw else None
        ),
    )
    assert translated.parts[-2:] == ("MuseScore", "MuseScore4.ini")


class _FakeLauncher:
    def __init__(
        self,
        *,
        settings: Path | None = None,
        mutation: str = "[import]\nmusicXml\\importBreaks=false\n",
    ) -> None:
        self.commands: list[list[str]] = []
        self.settings = settings
        self.mutation = mutation

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
            self.settings.write_text(self.mutation, encoding="utf-8")
        return 0, f"converted to {output}"


def _open(
    roots: Confinement,
    tmp_path: Path,
    launcher: _FakeLauncher,
    *,
    version: str = "4.7.7",
    with_settings: bool = True,
) -> musescore.MuseScore:
    executable = tmp_path / "MuseScore4.exe"
    executable.parent.mkdir(parents=True, exist_ok=True)
    executable.write_bytes(b"binary")
    settings = tmp_path / "MuseScore4.ini"
    if with_settings and not settings.exists():
        settings.write_text("[ui]\nx=1\n", encoding="utf-8")
    return musescore.MuseScore.open(
        roots,
        executable=executable,
        settings=settings,
        version_lookup=lambda: version,
        launcher=launcher,
    )


def test_open_accepts_musescore_4_and_stops_outside_major_4(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Any MuseScore 4.x release is recorded with binary hash; non-4.x stops."""
    for valid in ("4.7.6", "4.7.7.261051422"):
        app = _open(roots, tmp_path, _FakeLauncher(), version=valid)
        assert app.version == valid
        assert len(app.executable_sha256) == 64  # noqa: PLR2004

    for invalid in ("3.6.2", "5.0.0"):
        with pytest.raises(StopConditionError, match="version"):
            _open(roots, tmp_path, _FakeLauncher(), version=invalid)


def test_open_tolerates_missing_or_ui_settings_and_stops_on_preference_drift(
    roots: Confinement,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fresh default INI absence or UI drift is allowed; bad override stops."""
    fresh_roots = Confinement.create(
        tmp_path / "fresh-run", tmp_path / "fresh-dl"
    )
    fresh_exe = tmp_path / "fresh" / "MuseScore4.exe"
    fresh_exe.parent.mkdir(parents=True, exist_ok=True)
    fresh_exe.write_bytes(b"binary")
    monkeypatch.setattr(
        musescore,
        "settings_path",
        lambda: tmp_path / "fresh-appdata" / "MuseScore" / "MuseScore4.ini",
    )
    fresh = musescore.MuseScore.open(
        fresh_roots,
        executable=fresh_exe,
        version_lookup=lambda: musescore.VERSION,
        launcher=_FakeLauncher(),
    )
    assert fresh.baseline is None
    assert (
        fresh.preferences()["import/musicXml/importBreaks"]["source"]
        == "default"
    )

    with pytest.raises(StopConditionError, match="settings file"):
        _open(
            fresh_roots,
            tmp_path / "missing-override",
            _FakeLauncher(),
            with_settings=False,
        )

    _open(roots, tmp_path, _FakeLauncher())
    (tmp_path / "MuseScore4.ini").write_text("[ui]\nx=2\n", encoding="utf-8")
    tolerated = _open(roots, tmp_path, _FakeLauncher())
    assert tolerated.preferences()["import/musicXml/importBreaks"]["value"] == (
        "true"
    )

    (tmp_path / "MuseScore4.ini").write_text(
        "[import]\nmusicXml\\importBreaks=false\n",
        encoding="utf-8",
    )
    with pytest.raises(StopConditionError, match="MusicXML preferences"):
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


def test_convert_stops_when_musicxml_preferences_change(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """UI state changes are tolerated; MusicXML preference changes stop."""
    ui_launcher = _FakeLauncher(
        settings=tmp_path / "MuseScore4.ini",
        mutation="[ui]\nlastWindow=2\n",
    )
    app = _open(roots, tmp_path, ui_launcher)
    source = tmp_path / "in.musicxml"
    source.write_text("<score/>", encoding="utf-8")
    assert app.convert(source, roots.temp / "ui.mscz").ok

    pref_launcher = _FakeLauncher(
        settings=tmp_path / "MuseScore4.ini",
        mutation="[import]\nmusicXml\\importBreaks=false\n",
    )
    app_pref = _open(roots, tmp_path, pref_launcher)
    with pytest.raises(StopConditionError, match="MusicXML preferences"):
        app_pref.convert(source, roots.temp / "out.mscz")


def test_wsl_translate_command_maps_paths_for_windows_exe() -> None:
    """WSL command translation converts -o and input paths for .exe binaries."""
    translated = musescore.wsl_translate_command(
        [
            "/mnt/c/Program Files/MuseScore 4/bin/MuseScore4.exe",
            "-o",
            "/a/out.xml",
            "/b/in.mscx",
        ],
        in_wsl=True,
        to_windows=lambda path: rf"\\wsl.localhost\Ubuntu{path}".replace(
            "/", "\\"
        ),
    )
    assert translated == [
        "/mnt/c/Program Files/MuseScore 4/bin/MuseScore4.exe",
        "-o",
        r"\\wsl.localhost\Ubuntu\a\out.xml",
        r"\\wsl.localhost\Ubuntu\b\in.mscx",
    ]
    native = ["/usr/bin/mscore4", "-o", "/a/out.xml", "/b/in.mscx"]
    assert (
        musescore.wsl_translate_command(
            native, in_wsl=True, to_windows=lambda p: p
        )
        == native
    )


def test_custom_executable_resolves_cli_version_instead_of_registry(
    roots: Confinement,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """An explicit --musescore override probes the binary, not MSI registry."""
    custom_exe = tmp_path / "custom-mscore4.exe"
    custom_exe.write_bytes(b"portable-4.8")
    monkeypatch.setattr(
        musescore,
        "registry_version",
        lambda *_args: musescore.VERSION,
    )

    def _version_launcher(
        command: Sequence[str],
        timeout: float,
    ) -> tuple[int | None, str]:
        _ = timeout
        if command[1] == "--long-version":
            banner = (
                "OpenGL 4.6 context initialized\n"
                "MuseScore: Music Score Editor; Version 4.8.0.1; Build abc\n"
            )
            return 0, banner
        return 1, ""

    settings_file = tmp_path / "MuseScore4.ini"
    settings_file.write_text("[ui]\nx=1\n", encoding="utf-8")
    opened = musescore.MuseScore.open(
        roots,
        executable=custom_exe,
        settings=settings_file,
        version_lookup=musescore.registry_version,
        launcher=_version_launcher,
    )
    assert opened.version == "4.8.0.1"
    assert (
        opened.preferences()["import/musicXml/importBreaks"]["source"]
        == "assumed_v4_7_6_default"
    )

    for bad_output in (
        "MuseScore 3.4.2\n",
        "MuseScore Studio 5.4.0\n",
        "Using Qt 6.4.3\nMuseScore 3.6.2\n",
    ):
        with pytest.raises(StopConditionError, match=r"4\.x"):
            musescore.MuseScore.open(
                roots,
                executable=custom_exe,
                settings=tmp_path / "MuseScore4.ini",
                version_lookup=musescore.registry_version,
                launcher=lambda cmd, _t, out=bad_output: (
                    (0, out) if cmd[1] == "--version" else (1, "")
                ),
            )
