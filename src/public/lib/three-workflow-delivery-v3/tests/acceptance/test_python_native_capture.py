"""Historical capture readers inspect supplied bytes without registry access."""

import pytest
from three_workflow_delivery_v3.acceptance.python_native_capture import (
    replay_capture,
)
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)

from . import test_python_native_fixture as fixture_tests
from .python_native_history import historical_files

modeled_fixtures = fixture_tests.modeled_fixtures


@pytest.fixture(autouse=True)
def deny_real_registry(monkeypatch):
    """A mistaken concrete HTTPS call cannot leave a local reader test."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real registry/OIDC access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


def witnesses(fixtures):
    """Return exact source witnesses from synthetic archived originals."""
    return tuple(
        fixtures.distributions[f"{label}/original/wheel"].witness
        for label in ("a", "b")
    )


def captured_files():
    """Select the final original capture from an archived synthetic suite."""
    return {
        name.removeprefix("suite/"): content
        for name, content in historical_files("hosted").items()
        if name.startswith("suite/capture/c8")
    }


def test_python_native_capture_replay_preserves_original_inventory(
    modeled_fixtures,
):
    """Replay re-inspects four original files and the unrelated inventory."""
    files = captured_files()
    capture = replay_capture(
        files, 8, PythonRegistry("testpypi"), witnesses(modeled_fixtures)
    )
    assert capture.inventory[
        "hcoona_release_smoke_python-0.0.1-py3-none-any.whl"
    ]["hashes"] == {"sha256": "e" * 64}
    assert {item.digest for item in capture.distributions} == {
        modeled_fixtures.distributions[f"{label}/original/{variant}"].digest
        for label in ("a", "b")
        for variant in ("wheel", "sdist")
    }
    assert capture.files(8) == files


def test_python_native_replay_rejects_404_as_complete_empty_index(
    modeled_fixtures,
):
    """A missing-project response cannot prove complete historical inventory."""
    files = captured_files()
    summary = parse_canonical_json(files["capture/c8.json"])
    summary["index-status"] = 404
    files["capture/c8.json"] = canonicalize(summary)
    response = parse_canonical_json(files["capture/c8/index.response.json"])
    response["status"] = 404
    files["capture/c8/index.response.json"] = canonicalize(response)
    with pytest.raises(ValueError, match="complete HTTP 200 index"):
        replay_capture(
            files, 8, PythonRegistry("testpypi"), witnesses(modeled_fixtures)
        )


@pytest.mark.parametrize(
    "change",
    [
        "raw-index",
        "raw-file",
        "missing-file",
        "inventory",
        "download-url",
        "status",
    ],
)
def test_python_native_capture_replay_rejects_substituted_raw_facts(
    modeled_fixtures, change
):
    """Summaries cannot replace changed or missing original response bytes."""
    files = captured_files()
    metadata = parse_canonical_json(files["capture/c8.json"])
    if change == "raw-index":
        files["capture/c8/index.body"] = b"{}"
    elif change == "raw-file":
        files["capture/c8/file-0.body"] = b"substitution"
    elif change == "missing-file":
        files.pop("capture/c8/file-0.body")
    elif change == "inventory":
        metadata["inventory"].pop(next(iter(metadata["inventory"])))
    elif change == "download-url":
        metadata["downloads"][0]["url"] = "https://example.invalid/foreign"
    else:
        metadata["index-status"] = 403
    files["capture/c8.json"] = canonicalize(metadata)
    with pytest.raises((ValueError, KeyError)):
        replay_capture(
            files, 8, PythonRegistry("testpypi"), witnesses(modeled_fixtures)
        )
