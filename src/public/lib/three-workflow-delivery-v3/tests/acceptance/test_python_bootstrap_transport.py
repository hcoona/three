"""Finite transport budgets, strict UTC completion and raw journal replay."""

from http import HTTPStatus

import pytest
from three_workflow_delivery_v3.acceptance.python_bootstrap_transport import (
    JournalTransport,
    ReplayTransport,
)
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_FILE_BYTES,
    MAX_INDEX_BYTES,
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import parse_json_strict

from ..adapters.test_pypi import FakeHttp

REGISTRY = PythonRegistry("testpypi")
WINDOW = {"created-at": 1000, "deadline": 1600}


def request_tuple(kind):
    """Independent expected transport routes and exact ceilings."""
    routes = {
        "index": ("GET", REGISTRY.index_url, None, MAX_INDEX_BYTES),
        "file": (
            "GET",
            f"https://{REGISTRY.file_host}/packages/original.whl",
            None,
            MAX_FILE_BYTES,
        ),
        "proof": (
            "GET",
            "https://api.github.com/repos/hcoona/three/actions/runs/911",
            None,
            8 * 1024 * 1024,
        ),
        "upload": (
            "POST",
            REGISTRY.upload_url,
            b"original-multipart",
            MAX_RESPONSE_BYTES,
        ),
        "mint": (
            "POST",
            REGISTRY.origin + "/_/oidc/mint-token",
            b"redacted-assertion",
            MAX_RESPONSE_BYTES,
        ),
        "oidc": (
            "GET",
            "https://run.actions.githubusercontent.com/idtoken?audience=testpypi",
            None,
            MAX_RESPONSE_BYTES,
        ),
    }
    method, url, body, maximum = routes[kind]
    return method, url, {}, body, maximum


@pytest.mark.parametrize(
    ("phase", "kind", "limit"),
    [
        ("authorize", "proof", 8),
        ("authorize", "index", 1),
        ("execute", "proof", 8),
        ("execute", "index", 13),
        ("execute", "file", 3),
        ("execute", "upload", 2),
        ("execute", "oidc", 1),
        ("execute", "mint", 1),
        ("audit", "index", 1),
        ("audit", "file", 2),
    ],
)
def test_bootstrap_every_phase_budget_blocks_before_limit_plus_one(
    phase, kind, limit
):
    """Each actual attempt, including token acquisition, has its own ceiling."""
    retained = {}
    http = FakeHttp(
        *[
            PythonHttpResponse(200, b"original response", "text/plain")
            for _ in range(limit)
        ]
    )
    journal = JournalTransport(
        http,
        phase,
        retained.__setitem__,
        window=None if phase == "audit" else WINDOW,
        clock=lambda: 1000,
    )
    arguments = request_tuple(kind)
    for _ in range(limit):
        assert journal.request(*arguments).status == HTTPStatus.OK
    with pytest.raises(ValueError, match="budget exhausted"):
        journal.request(*arguments)
    journal.close()
    assert len(http.calls) == limit
    replay = ReplayTransport(
        retained, phase, window=None if phase == "audit" else WINDOW
    )
    for _ in range(limit):
        if kind in {"oidc", "mint"}:
            replay.credential(kind)
        else:
            assert replay.request(*arguments).body == b"original response"
    replay.finished()
    if kind in {"oidc", "mint"}:
        assert not any(name.endswith(".body") for name in retained)
        assert b"redacted-assertion" not in b"".join(retained.values())


def test_bootstrap_monotonic_guard_can_only_shorten_authority():
    """UTC remaining time cannot override an expired local monotonic guard."""
    http = FakeHttp()
    journal = JournalTransport(
        http, "authorize", lambda *_: None, window=WINDOW, clock=lambda: 1000
    )
    journal.monotonic = lambda: journal.monotonic_deadline
    with pytest.raises(ValueError, match="monotonic deadline"):
        journal.request(*request_tuple("index"))
    assert http.calls == []


@pytest.mark.parametrize(
    ("finish", "accepted"), [(1030.0, True), (1030.001, False), (999.0, False)]
)
def test_bootstrap_request_completion_bound_is_enforced_live(finish, accepted):
    """The original response survives an overlong request but cannot succeed."""
    now = [1000.0]
    retained = {}

    class TimedBoundary:
        def request(self, *_args):
            now[0] = finish
            return PythonHttpResponse(
                200, b"original late response", "text/plain"
            )

    journal = JournalTransport(
        TimedBoundary(),
        "execute",
        retained.__setitem__,
        window=WINDOW,
        clock=lambda: now[0],
    )
    if accepted:
        assert journal.request(*request_tuple("upload")).status == HTTPStatus.OK
    else:
        with pytest.raises(ValueError, match="completion bound"):
            journal.request(*request_tuple("upload"))
    journal.close()
    assert retained["http/0.body"] == b"original late response"
    assert parse_json_strict(retained["requests.json"])[0]["finish"] == finish
    if not accepted:
        with pytest.raises(ValueError, match="timing"):
            ReplayTransport(retained, "execute", window=WINDOW)


def test_bootstrap_audit_has_separate_read_only_budget_after_expiry():
    """P4 may observe after the publisher deadline without renewing uploads."""
    retained = {}
    http = FakeHttp(
        PythonHttpResponse(200, b"fresh original observation", "text/plain")
    )
    journal = JournalTransport(
        http, "audit", retained.__setitem__, clock=lambda: 5000
    )
    assert (
        journal.request(*request_tuple("index")).body
        == b"fresh original observation"
    )
    with pytest.raises(ValueError, match="budget"):
        journal.request(*request_tuple("upload"))
    journal.close()
    assert len(http.calls) == 1
    assert parse_json_strict(retained["requests.json"])[0]["start"] == 5000  # noqa: PLR2004


def test_bootstrap_replay_rejects_unused_responses_within_budget():
    """Unused allowance does not excuse unexplained original HTTP responses."""
    retained = {}
    http = FakeHttp(PythonHttpResponse(200, b"original index", "text/plain"))
    journal = JournalTransport(
        http, "execute", retained.__setitem__, window=WINDOW, clock=lambda: 1000
    )
    journal.request(*request_tuple("index"))
    journal.close()
    replay = ReplayTransport(retained, "execute", window=WINDOW)
    with pytest.raises(ValueError, match="unconsumed bootstrap responses"):
        replay.finished()
    assert replay.request(*request_tuple("index")).body == b"original index"
    replay.finished()
