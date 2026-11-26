"""Contracts for Renovate's bundled svgedit update."""

import json
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = (
    "src/public/app/ImageOcclusionEditor/ImageOcclusionEditorWinUI3/"
    "Update-LocalSvgEditor.ps1"
)


def _renovate() -> dict[str, Any]:
    return json.loads((ROOT / "renovate.json").read_text(encoding="utf-8"))


def test_manager_captures_only_the_pinned_version() -> None:
    """Track exactly one pinned npm version in the updater."""
    (manager,) = [
        manager
        for manager in _renovate()["customManagers"]
        if manager.get("depNameTemplate") == "svgedit"
    ]
    assert manager["customType"] == "regex"
    assert manager["managerFilePatterns"] == [SCRIPT]
    assert manager["datasourceTemplate"] == "npm"
    assert manager["versioningTemplate"] == "npm"
    (pattern,) = manager["matchStrings"]
    text = (ROOT / SCRIPT).read_text(encoding="utf-8")
    (match,) = re.finditer(pattern.replace("(?<", "(?P<"), text)
    assert match[0] == f'$PinnedVersion = "{match["currentValue"]}"'


def test_integrity_task_is_scoped_and_allowlisted() -> None:
    """Permit only the updater's integrity-refresh command."""
    (rule,) = [
        rule
        for rule in _renovate()["packageRules"]
        if "svgedit" in rule.get("matchDepNames", [])
    ]
    assert rule["matchManagers"] == ["custom.regex"]
    assert rule["matchFileNames"] == [SCRIPT]
    tasks = rule["postUpgradeTasks"]
    assert tasks["fileFilters"] == [SCRIPT]
    assert tasks["executionMode"] == "branch"
    (command,) = tasks["commands"]
    assert command == (
        f"pwsh -NoLogo -NoProfile -NonInteractive -File {SCRIPT} "
        "-RefreshPinnedIntegrity"
    )
    config = (ROOT / ".github/renovate/global.cjs").read_text(encoding="utf-8")
    allowlist = config.partition("allowedCommands: [")[2].partition("],")[0]
    allowed = re.findall(r"String\.raw`([^`]*)`", allowlist)
    assert any(re.search(pattern, command) for pattern in allowed)
    assert not any(
        re.search(pattern, f"{command} -Force") for pattern in allowed
    )


def test_override_requires_explicit_integrity() -> None:
    """Reject an unpinned version before entering the download path."""
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is not installed")
    result = subprocess.run(  # noqa: S603 - Fixed local script and arguments.
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(ROOT / SCRIPT),
            "-Version",
            "0.0.0",
            "-WhatIf",
        ],
        capture_output=True,
        check=False,
        text=True,
        timeout=30,
    )
    assert result.returncode != 0
    assert "No integrity pinned for svgedit 0.0.0" in result.stderr
