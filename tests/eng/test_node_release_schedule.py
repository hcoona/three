"""Contracts for the committed Node.js release schedule and its updates."""

from __future__ import annotations

import importlib.util
import json
import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCHEDULE = "eng/node-release-schedule.json"
COMMIT = "0123456789abcdef0123456789abcdef01234567"


def _load_script():
    path = ROOT / "eng/scripts/update_node_release_schedule.py"
    spec = importlib.util.spec_from_file_location(
        "update_node_release_schedule", path
    )
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _renovate() -> dict:
    return json.loads((ROOT / "renovate.json").read_text(encoding="utf-8"))


def _manager(dep_name: str) -> dict:
    managers = [
        manager
        for manager in _renovate()["customManagers"]
        if SCHEDULE in manager.get("managerFilePatterns", [])
        and manager.get("depNameTemplate") == dep_name
    ]
    assert len(managers) == 1
    return managers[0]


def _rules(dep_name: str, update_type: str | None = None) -> list[dict]:
    """Package rules that apply to one custom regex dependency, in order."""
    return [
        rule
        for rule in _renovate()["packageRules"]
        if "custom.regex" in rule.get("matchManagers", [])
        and dep_name in rule.get("matchDepNames", [])
        and (
            "matchUpdateTypes" not in rule
            or (
                update_type is not None
                and update_type in rule["matchUpdateTypes"]
            )
        )
    ]


def _effective(dep_name: str, update_type: str | None = None) -> dict:
    config = {
        key: value
        for key, value in _renovate().items()
        if key != "packageRules"
    }
    for rule in _rules(dep_name, update_type):
        config.update(rule)
    return config


def _capture(manager: dict) -> dict[str, str]:
    text = (ROOT / SCHEDULE).read_text(encoding="utf-8")
    (pattern,) = manager["matchStrings"]
    matches = list(re.finditer(pattern.replace("(?<", "(?P<"), text))
    assert len(matches) == 1
    return matches[0].groupdict()


def _js_regex(value: str) -> re.Pattern[str]:
    assert value.startswith("/")
    assert value.endswith("/")
    return re.compile(value[1:-1])


def test_committed_schedule_has_the_planned_shape_and_script_format() -> None:
    """The committed file is the reader shape, written as the script writes."""
    text = (ROOT / SCHEDULE).read_text(encoding="utf-8")
    data = json.loads(text)
    assert set(data) == {"newestRelease", "schedule"}
    assert set(data["schedule"]) == {"commit", "releases"}
    assert re.fullmatch(r"\d+\.\d+\.\d+", data["newestRelease"])
    assert re.fullmatch(r"[0-9a-f]{40}", data["schedule"]["commit"])
    newest = "v" + data["newestRelease"].split(".")[0]
    assert newest in data["schedule"]["releases"]
    assert text == json.dumps(data, indent=2, ensure_ascii=False) + "\n"


def test_newest_release_manager_proposes_released_current_majors_only() -> None:
    """Released Current majors are proposed; prereleases never are."""
    manager = _manager("node")
    assert manager["datasourceTemplate"] == "node-version"
    assert manager["versioningTemplate"] == "semver"
    extract = re.compile(
        manager["extractVersionTemplate"].replace("(?<", "(?P<")
    )
    assert extract.fullmatch("v27.0.0")["version"] == "27.0.0"
    data = json.loads((ROOT / SCHEDULE).read_text(encoding="utf-8"))
    assert _capture(manager) == {"currentValue": data["newestRelease"]}

    config = _effective("node", "major")
    # node-version marks every non-LTS release unstable; a released Current
    # major must still qualify.
    assert config["ignoreUnstable"] is False
    allowed = _js_regex(config["allowedVersions"])
    for version in ("v27.0.0", "27.0.0", "v26.13.0"):
        assert allowed.fullmatch(version), version
    for version in ("v27.0.0-rc.1", "27.0.0-nightly20260101", "v27.0"):
        assert not allowed.fullmatch(version), version
    assert config["automerge"] is False
    for update_type in ("minor", "patch"):
        assert _effective("node", update_type)["enabled"] is False


def test_schedule_commit_manager_is_a_monthly_reviewed_digest() -> None:
    """The pinned commit moves monthly and refreshes the releases verbatim."""
    manager = _manager("nodejs/Release")
    assert manager["datasourceTemplate"] == "git-refs"
    assert manager["packageNameTemplate"] == "https://github.com/nodejs/Release"
    assert manager["currentValueTemplate"] == "main"
    data = json.loads((ROOT / SCHEDULE).read_text(encoding="utf-8"))
    assert _capture(manager) == {"currentDigest": data["schedule"]["commit"]}

    config = _effective("nodejs/Release", "digest")
    assert config["automerge"] is False
    assert config["schedule"] == ["* 0-3 1 * *"]
    # git-refs digests carry no release timestamp, which the global age gate
    # would hold forever.
    assert config["minimumReleaseAge"] is None
    tasks = config["postUpgradeTasks"]
    assert tasks["fileFilters"] == [SCHEDULE]
    global_config = (ROOT / ".github/renovate/global.cjs").read_text(
        encoding="utf-8"
    )
    allowed = [
        re.compile(pattern)
        for pattern in re.findall(r"String\.raw`([^`]*)`", global_config)
    ]
    for command in tasks["commands"]:
        assert any(pattern.search(command) for pattern in allowed), command


def test_refresh_rewrites_only_the_releases_at_the_pinned_commit(
    tmp_path: Path,
) -> None:
    """Only schedule.releases changes, fetched at the pinned commit."""
    script = _load_script()
    path = tmp_path / SCHEDULE
    path.parent.mkdir(parents=True)
    path.write_text(
        json.dumps(
            {
                "newestRelease": "26.13.0",
                "schedule": {
                    "commit": COMMIT,
                    "releases": {"v24": {"lts": "x"}},
                },
            }
        ),
        encoding="utf-8",
    )
    requested: list[str] = []

    def fetch(commit: str) -> bytes:
        requested.append(commit)
        return (
            b'{"v26": {"start": "2026-04-22", "lts": "2026-10-28"},\n'
            b' "v27": {}}'
        )

    script.refresh(tmp_path, fetch)

    assert requested == [COMMIT]
    text = path.read_bytes().decode("utf-8")
    assert "\r" not in text
    assert text.endswith("}\n")
    assert json.loads(text) == {
        "newestRelease": "26.13.0",
        "schedule": {
            "commit": COMMIT,
            "releases": {
                "v26": {"start": "2026-04-22", "lts": "2026-10-28"},
                "v27": {},
            },
        },
    }


@pytest.mark.parametrize(
    ("commit", "answer"),
    [
        ("main", b'{"v26": {}}'),
        (COMMIT, b'{"v26": {}, "v26": {"lts": "x"}}'),
        (COMMIT, b"[]"),
        (COMMIT, b"{}"),
    ],
)
def test_refresh_rejects_unpinned_or_ambiguous_data(
    tmp_path: Path, commit: str, answer: bytes
) -> None:
    """Unpinned, ambiguous or empty data leaves the committed file unchanged."""
    script = _load_script()
    path = tmp_path / SCHEDULE
    path.parent.mkdir(parents=True)
    original = json.dumps(
        {
            "newestRelease": "26.13.0",
            "schedule": {"commit": commit, "releases": {}},
        }
    )
    path.write_text(original, encoding="utf-8")

    with pytest.raises(ValueError, match=r"commit|repeats|nonempty"):
        script.refresh(tmp_path, lambda _: answer)

    assert path.read_text(encoding="utf-8") == original
