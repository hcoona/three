"""Closed bootstrap request, immutable artifact identity and UTC boundaries."""

# Protocol constants are kept visible in boundary assertions.
# ruff: noqa: PLR2004

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    BootstrapRequest,
    authority_window,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_transport import (
    JournalTransport,
    ReplayTransport,
)
from three_workflow_delivery_v3.acceptance.python_native import read_artifact
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_INDEX_BYTES,
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict

from ..adapters.test_pypi import FakeHttp
from . import test_python_bootstrap_fixture as fixture_tests
from . import test_python_bootstrap_suite as suite_tests
from .test_python_bootstrap_fixture import RUN, bootstrap_request

bootstrap_fixtures = fixture_tests.bootstrap_fixtures
scenario = suite_tests.scenario
deny_real_registry = suite_tests.deny_real_registry


@pytest.mark.parametrize(
    ("path", "value"),
    [
        (("account",), "hcoona"),
        (("registry",), "pypi"),
        (("project",), "foreign-project"),
        (("workflow",), "foreign.yml"),
        (("schema",), "foreign-schema"),
        (("extra",), True),
        (("source", "version"), "0.1.0b7+foreign"),
        (("source", "version"), "0.1.0"),
        (("source", "commit"), "a" * 39),
        (("generation",), "A" * 32),
        (("environment", "id"), True),
        (("environment", "sentinel"), "foreign"),
        (("fixture-digests", "sdist"), "f" * 64),
        (("configuration", "url"), "https://user:password@example.invalid"),
        (("authorization", "digest"), None),
    ],
)
def test_bootstrap_request_closes_exact_account_and_project(
    bootstrap_fixtures, path, value
):
    """A foreign tuple or extra authority field never becomes.

    A foreign tuple or extra authority field never becomes a valid
    request.
    """
    document = bootstrap_request(bootstrap_fixtures).document
    parent = document
    for name in path[:-1]:
        parent = parent[name]
    parent[path[-1]] = value
    with pytest.raises((ValueError, TypeError)):
        BootstrapRequest(canonicalize(document))


@pytest.mark.parametrize("change", ["id", "service", "payload", "url", "name"])
def test_bootstrap_artifact_rejects_foreign_original_reference(
    scenario, change
):
    """Actual bytes must match both service/logical hashes.

    Actual bytes must match both service/logical hashes and current-run
    URL.
    """
    reference = scenario.context.references["prepared"]
    kwargs = {
        "id": {"artifact_id": 9},
        "service": {"artifact_digest": "sha256:" + "f" * 64},
        "payload": {"payload_digest": "sha256:" + "f" * 64},
        "url": {
            "artifact_url": reference.artifact_url.replace(str(RUN), "912")
        },
        "name": {"payload_path": "foreign.zip"},
    }[change]
    with pytest.raises(ValueError, match="binding"):
        read_artifact(
            scenario.root / reference.payload_path,
            replace(reference, **kwargs),
            RUN,
        )


@pytest.mark.parametrize("now", [999, 1600, 1601])
def test_bootstrap_artifact_wait_consumes_original_authority(scenario, now):
    """Downloaded authority is neither renewable nor valid before its origin."""
    scenario.authorize()
    scenario.now = now
    with pytest.raises(ValueError, match="future or expired"):
        scenario.execute()
    assert scenario.exec_http.calls == []
    assert not (scenario.root / "result/execution").exists()


@pytest.mark.parametrize(
    "value", [True, None, "1000", float("inf"), float("nan"), -1, 0]
)
def test_bootstrap_utc_rejects_nonfinite_or_non_numeric(value):
    """Authority timestamps require finite positive epoch numbers."""
    with pytest.raises(ValueError, match="UTC"):
        authority_window({"created-at": value, "deadline": 1600})


def test_bootstrap_authority_is_half_open_without_renewal():
    """Beginning and final in-window instant are valid; the deadline is not."""
    window = {"created-at": 1000, "deadline": 1600}
    assert authority_window(window, 1000) == (1000.0, 1600.0)
    assert authority_window(window, 1599.999) == (1000.0, 1600.0)
    with pytest.raises(ValueError, match="deadline"):
        authority_window({"created-at": 1000, "deadline": 1601})
    with pytest.raises(ValueError, match="expired"):
        authority_window(window, 1600)


def test_bootstrap_authority_expiry_blocks_next_request():
    """A sent response may finish after expiry; the next attempt is denied."""
    now = [1599.0]
    retained = {}

    class CompletionBoundary:
        def __init__(self):
            self.calls = []

        def request(self, *args):
            self.calls.append(args)
            now[0] = 1601.0
            return PythonHttpResponse(404, b"original absent", "text/html")

    boundary = CompletionBoundary()
    journal = JournalTransport(
        boundary,
        "execute",
        retained.__setitem__,
        window={"created-at": 1000, "deadline": 1600},
        clock=lambda: now[0],
    )
    url = PythonRegistry("testpypi").index_url
    assert journal.request("GET", url, {}, None, MAX_INDEX_BYTES).status == 404
    with pytest.raises(ValueError, match="expired"):
        journal.request("GET", url, {}, None, MAX_INDEX_BYTES)
    journal.close()
    assert len(boundary.calls) == 1
    assert parse_json_strict(retained["requests.json"])[0]["finish"] == 1601
    replay = ReplayTransport(
        retained, "execute", window={"created-at": 1000, "deadline": 1600}
    )
    assert (
        replay.request("GET", url, {}, None, MAX_INDEX_BYTES).body
        == b"original absent"
    )
    replay.finished()


def test_bootstrap_effect_budget_counts_failed_attempt_before_send():
    """A timed-out attempt consumes its finite slot and preserves failure."""
    retained = {}
    http = FakeHttp(TimeoutError("synthetic timeout"))
    journal = JournalTransport(
        http,
        "authorize",
        retained.__setitem__,
        window={"created-at": 1000, "deadline": 1600},
        clock=lambda: 1000,
    )
    url = PythonRegistry("testpypi").index_url
    with pytest.raises(TimeoutError):
        journal.request("GET", url, {}, None, MAX_INDEX_BYTES)
    with pytest.raises(ValueError, match="budget"):
        journal.request("GET", url, {}, None, MAX_INDEX_BYTES)
    journal.close()
    assert len(http.calls) == 1
    assert parse_json_strict(retained["requests.json"])[0]["status"] is None
    with pytest.raises(ValueError, match="unavailable"):
        ReplayTransport(
            retained, "authorize", window={"created-at": 1000, "deadline": 1600}
        )


@pytest.mark.parametrize(
    "secret_form",
    [
        b"sensitive-test-token",
        b"c2Vuc2l0aXZlLXRlc3QtdG9rZW4=",
        b"X19 0b2tlbl9fOnNlbnNpdGl2ZS10ZXN0LXRva2Vu".replace(b" ", b""),
    ],
)
def test_bootstrap_response_never_retains_reflected_credentials(secret_form):
    """Raw response retention fails closed on reflected credential encodings."""
    retained = {}
    http = FakeHttp(PythonHttpResponse(404, secret_form, "text/plain"))
    journal = JournalTransport(
        http,
        "authorize",
        retained.__setitem__,
        window={"created-at": 1000, "deadline": 1600},
        clock=lambda: 1000,
        secrets=("sensitive-test-token",),
    )
    with pytest.raises(ValueError, match="unsafe"):
        journal.request(
            "GET",
            PythonRegistry("testpypi").index_url,
            {},
            None,
            MAX_INDEX_BYTES,
        )
    journal.close()
    assert "http/0.body" not in retained
    assert secret_form not in b"".join(retained.values())
