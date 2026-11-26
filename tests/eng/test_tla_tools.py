"""Scenario tests for TLA+ toolchain locking and the SANY/TLC launcher."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import subprocess
import tomllib
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from types import ModuleType

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "eng/scripts/tla.py"


def _load_tla() -> ModuleType:
    spec = importlib.util.spec_from_file_location("tla_script", SCRIPT)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _write_jar(path: Path) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\n")
    path.chmod(0o644)
    return path


def _install_fake_java(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    tla: ModuleType,
    exit_code: int = 0,
) -> dict[str, object]:
    jar_dir = tmp_path / "install"
    jar = _write_jar(jar_dir / "tla2tools.jar")
    fake_java = tmp_path / "fake-java.exe"
    fake_java.write_text("placeholder", encoding="utf-8")
    monkeypatch.setenv("PATH", str(jar_dir))
    monkeypatch.delenv("TLA2TOOLS_JAR", raising=False)
    monkeypatch.setattr(
        tla.shutil,
        "which",
        lambda name: str(fake_java) if name == "java" else None,
    )

    recorded: dict[str, object] = {}

    def fake_run(cmd: tuple[str, ...], *, check: bool = False, **_kwargs):
        assert check is False
        args = list(cmd[1:])
        tmpdir = next(
            a.split("=", 1)[1]
            for a in args
            if a.startswith("-Djava.io.tmpdir=")
        )
        metadir = (
            args[args.index("-metadir") + 1] if "-metadir" in args else None
        )
        if metadir:
            states = Path(metadir)
            states.mkdir(parents=True, exist_ok=True)
            (states / "MC.out").write_text("state", encoding="utf-8")
        recorded.update(
            {
                "cmd": list(cmd),
                "args": args,
                "jar": args[args.index("-cp") + 1],
                "tmpdir": tmpdir,
                "tmpdir_existed": Path(tmpdir).is_dir(),
                "metadir": metadir,
            }
        )
        return subprocess.CompletedProcess(cmd, exit_code)

    monkeypatch.setattr(tla.subprocess, "run", fake_run)
    recorded["expected_jar"] = str(jar)
    return recorded


def test_tla_toolchain_pins_immutable_release_with_platform_checksums():
    """tla2tools.jar stays pinned to a stable release on Linux and Windows."""
    mise_toml = tomllib.loads((ROOT / "mise.toml").read_text(encoding="utf-8"))
    spec = mise_toml["tools"]["github:tlaplus/tlaplus"]
    assert spec["asset_pattern"] == "tla2tools.jar"
    assert re.fullmatch(r"v\d+\.\d+\.\d+", spec["version"])

    lock = tomllib.loads((ROOT / "mise.lock").read_text(encoding="utf-8"))
    (entry,) = lock["tools"]["github:tlaplus/tlaplus"]
    assert re.fullmatch(r"\d+\.\d+\.\d+", entry["version"])
    for platform in ("platforms.linux-x64", "platforms.windows-x64"):
        checksum = entry[platform]["checksum"]
        assert checksum.startswith("sha256:")
        assert entry[platform]["url"].endswith(
            f"/v{entry['version']}/tla2tools.jar"
        )

    renovate = json.loads((ROOT / "renovate.json").read_text(encoding="utf-8"))
    guard = next(
        rule
        for rule in renovate["packageRules"]
        if rule.get("matchDepNames") == ["github:tlaplus/tlaplus"]
    )
    assert guard["allowedVersions"] == r"/^v?\d+\.\d+\.\d+$/"


def test_jar_discovery_skips_non_zip_shims_and_falls_back_to_mise_where(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """Non-ZIP mise shims on PATH are ignored in favor of real JAR archives."""
    tla = _load_tla()
    shims_dir = tmp_path / "shims"
    shims_dir.mkdir()
    (shims_dir / "tla2tools.jar").write_bytes(b"\x7fELF-mise-shim")
    real_dir = tmp_path / "mise-install"
    real_jar = _write_jar(real_dir / "tla2tools.jar")

    monkeypatch.delenv("TLA2TOOLS_JAR", raising=False)
    monkeypatch.setenv("PATH", str(shims_dir))
    monkeypatch.setattr(
        tla.shutil,
        "which",
        lambda name: "/usr/bin/mise" if name == "mise" else None,
    )
    monkeypatch.setattr(
        tla.subprocess,
        "run",
        lambda cmd, **_kw: subprocess.CompletedProcess(
            cmd, 0, stdout=f"{real_dir}\n", stderr=""
        ),
    )
    assert tla._resolve_tla_jar() == real_jar  # noqa: SLF001


@pytest.mark.parametrize("exit_code", [0, 1])
def test_sany_batches_files_with_isolated_tmpdir_and_propagates_status(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, exit_code: int
):
    """SANY passes driver and literal operands and cleans its temporary dir."""
    tla = _load_tla()
    recorded = _install_fake_java(
        tmp_path, monkeypatch, tla, exit_code=exit_code
    )

    status = tla.main(["sany", "--", "specs/A.tla", "-leading-dash.tla"])
    assert status == exit_code
    assert recorded["jar"] == recorded["expected_jar"]
    assert recorded["tmpdir_existed"] is True
    assert not Path(str(recorded["tmpdir"])).exists()
    args = list(recorded["args"])  # type: ignore[arg-type]
    assert args[-4:] == [
        str(ROOT / "eng/linter/TlaSanyCheck.java"),
        "--",
        "specs/A.tla",
        "-leading-dash.tla",
    ]


def test_tlc_uses_isolated_metadir_without_deleting_working_tree_states(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """TLC isolates -metadir without passing -cleanup or touching ./states."""
    tla = _load_tla()
    recorded = _install_fake_java(tmp_path, monkeypatch, tla, exit_code=12)
    cwd_states = tmp_path / "cwd" / "states"
    cwd_states.mkdir(parents=True)
    sentinel = cwd_states / "keep.txt"
    sentinel.write_text("user-data", encoding="utf-8")
    monkeypatch.chdir(cwd_states.parent)

    status = tla.main(["tlc", "--", "-config", "Spec.cfg", "Spec.tla"])
    assert status == 12
    args = list(recorded["args"])  # type: ignore[arg-type]
    assert "-XX:+UseParallelGC" in args
    assert "-cleanup" not in args
    assert recorded["metadir"] is not None
    assert not Path(str(recorded["metadir"])).exists()
    assert not Path(str(recorded["tmpdir"])).exists()
    assert sentinel.read_text(encoding="utf-8") == "user-data"
    assert args[-3:] == ["-config", "Spec.cfg", "Spec.tla"]


def test_tlc_preserves_explicit_caller_metadir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """Caller-supplied -metadir is forwarded without injecting a duplicate."""
    tla = _load_tla()
    recorded = _install_fake_java(tmp_path, monkeypatch, tla, exit_code=0)
    explicit = tmp_path / "custom-states"

    status = tla.main(["tlc", "-metadir", str(explicit), "Spec.tla"])
    assert status == 0
    args = list(recorded["args"])  # type: ignore[arg-type]
    assert args.count("-metadir") == 1
    assert recorded["metadir"] == str(explicit)
    assert (explicit / "MC.out").is_file()


@pytest.mark.parametrize(
    ("args", "hide_tools"),
    [
        ([], False),
        (["unknown"], False),
        (["sany", "--"], False),
        (["tlc", "--"], False),
        (["sany", "--", "Spec.tla"], True),
    ],
)
def test_launcher_fails_closed_on_invalid_usage_or_missing_tools(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    args: list[str],
    hide_tools: bool,
):
    """Missing tools or invalid CLI invocations return deterministic exit 2."""
    tla = _load_tla()
    if hide_tools:
        monkeypatch.setenv("PATH", str(tmp_path))
        monkeypatch.delenv("TLA2TOOLS_JAR", raising=False)
        monkeypatch.setattr(tla.shutil, "which", lambda _name: None)
    assert tla.main(args) == 2


@pytest.mark.skipif(
    _load_tla()._require_runtime() is None,  # noqa: SLF001
    reason="Locked Java and tla2tools.jar are not installed in python-tests.",
)
def test_real_sany_driver_isolates_sibling_modules_and_rejects_semantic_errors(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """Real TlaSanyCheck ignores CWD shadowing and exits 1 on semantic bugs."""
    tla = _load_tla()
    cwd_dir = tmp_path / "cwd"
    cwd_dir.mkdir()
    (cwd_dir / "Helper.tla").write_text(
        "---- MODULE Helper ----\nWrongOp == TRUE\n====\n",
        encoding="utf-8",
    )
    spec_dir = tmp_path / f"dir{os.pathsep}with{os.pathsep}sep"
    spec_dir.mkdir()
    helper = spec_dir / "Helper.tla"
    helper.write_text(
        "---- MODULE Helper ----\nEXTENDS Naturals\nInc(x) == x + 1\n====\n",
        encoding="utf-8",
    )
    valid = spec_dir / "ValidSpec.tla"
    valid.write_text(
        "---- MODULE ValidSpec ----\n"
        "EXTENDS Helper\n"
        "VARIABLE count\n"
        "Init == count = Inc(0)\n"
        "====\n",
        encoding="utf-8",
    )
    semantic_err = spec_dir / "SemanticErr.tla"
    semantic_err.write_text(
        "---- MODULE SemanticErr ----\nInit == UnknownOp = 1\n====\n",
        encoding="utf-8",
    )
    monkeypatch.chdir(cwd_dir)

    assert tla.main(["sany", "--", str(valid), str(helper)]) == 0
    assert tla.main(["sany", "--", str(semantic_err)]) == 1
