"""Exact all-project first-publication readback,.

Exact all-project first-publication readback, including hostile inventories.
"""

import pytest
from three_workflow_delivery_v3.acceptance.python_bootstrap_capture import (
    capture_exact,
)
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import parse_json_strict

from ..adapters.test_pypi import FakeHttp, _entry, _index
from . import test_python_bootstrap_fixture as fixture_tests
from .test_python_bootstrap_fixture import bootstrap_request

bootstrap_fixtures = fixture_tests.bootstrap_fixtures


@pytest.mark.parametrize(
    "change",
    [
        "other-version",
        "extra-file",
        "missing-sdist",
        "duplicate",
        "empty",
        "yanked",
        "wrong-hash",
        "foreign-host",
        "bad-type",
        "redirect",
    ],
)
def test_bootstrap_complete_capture_rejects_unexpected_project_inventory(  # noqa: C901
    bootstrap_fixtures, change
):
    """Complete project inventory cannot be filtered into apparent success."""
    originals = tuple(bootstrap_fixtures.distributions.values())
    registry = bootstrap_request(bootstrap_fixtures).registry
    entries = [_entry(registry, item) for item in originals]
    if change == "other-version":
        other = dict(entries[0])
        other["filename"] = "hcoona_release_smoke_python-0.0.1-py3-none-any.whl"
        entries.append(other)
    elif change == "extra-file":
        entries.append({"filename": "foreign.txt"})
    elif change == "missing-sdist":
        entries.pop()
    elif change == "duplicate":
        entries[1] = entries[0]
    elif change == "empty":
        entries = []
    elif change == "yanked":
        entries[0]["yanked"] = True
    elif change == "wrong-hash":
        entries[0]["hashes"] = {"sha256": "f" * 64}
    elif change == "foreign-host":
        entries[0]["url"] = "https://evil.example/packages/file.whl"
    response = _index(entries)
    if change == "bad-type":
        response = PythonHttpResponse(200, response.body, "text/html")
    elif change == "redirect":
        response = PythonHttpResponse(302, b"redirect", "text/html")
    http = FakeHttp(
        response,
        *[
            PythonHttpResponse(200, item.content, "application/octet-stream")
            for item in originals
        ],
    )
    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        capture_exact(http, originals)
    assert len(http.calls) == (2 if change == "wrong-hash" else 1)
    assert all(call[0] == "GET" for call in http.calls)
    if change not in {"redirect", "bad-type"}:
        assert parse_json_strict(response.body)["files"] == entries
