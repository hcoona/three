"""Sanitized runtime failures retain unknown state during offline replay."""

import http.client
import ssl
import subprocess
from copy import deepcopy

import pytest
from three_workflow_delivery_v3.adapters import ruby_registry
from three_workflow_delivery_v3.adapters.ruby_registry import RubyRequestBudget
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)

from ..ruby_registry_fixtures import ScriptedTransport
from . import ruby_fixtures

ruby_publication_cases = ruby_fixtures.ruby_publication_cases


@pytest.mark.parametrize(
    ("failure", "family"),
    [
        (http.client.RemoteDisconnected("private-http"), "HTTPException"),
        (http.client.BadStatusLine("private-status"), "HTTPException"),
        (http.client.IncompleteRead(b"private-partial", 1), "HTTPException"),
        (ConnectionResetError("private-reset"), "OSError"),
        (ssl.SSLError("private-tls"), "OSError"),
        (FileNotFoundError("private-path"), "OSError"),
        (TimeoutError("private-timeout"), "OSError"),
        (
            subprocess.TimeoutExpired(
                ["private-command"], 1, output=b"private-output"
            ),
            "SubprocessError",
        ),
        (
            subprocess.CalledProcessError(1, ["private-command"]),
            "SubprocessError",
        ),
        (UnicodeError("private-unicode"), "ValueError"),
        (TypeError("private-type"), "TypeError"),
    ],
)
@pytest.mark.parametrize("prefix_count", [0, 1])
def test_ruby_request_failure_families_replay_exact_complete_prefix(
    ruby_publication_cases, failure, family, prefix_count
):
    """Subclass names and private messages cannot alter replay vocabulary."""
    case = ruby_publication_cases["rubygems"]
    prefix = case.exact_responses()[:prefix_count]
    transport = ScriptedTransport(*prefix, failure)
    budget = RubyRequestBudget(transport)
    observed = case.reader(budget).observe(case.original)
    assert observed.classification == "unknown"
    assert observed.failure_kind == family
    assert observed.failure_stage == "request"
    assert observed.distribution is None
    assert [response for _, response in observed.responses] == prefix
    assert budget.used == len(transport.requests) == prefix_count + 1
    assert b"private-" not in canonicalize(observed.to_document())
    replayed = replay_ruby_observation(
        observed.to_document(), case.original, case.registry
    )
    assert replayed.to_document() == observed.to_document()
    assert replayed.classification == "unknown"
    assert replayed.distribution is None


@pytest.mark.parametrize("boundary", ["inventory", "original"])
def test_ruby_interpretation_timeout_stays_unknown_when_offline_helper_succeeds(
    ruby_publication_cases, monkeypatch, boundary
):
    """A transient native failure cannot be promoted by later local success."""
    case = ruby_publication_cases["rubygems"]
    responses = (
        case.missing_responses()
        if boundary == "inventory"
        else case.exact_responses()
    )
    transport = ScriptedTransport(*responses)

    def timeout(*_args, **_kwargs):
        raise subprocess.TimeoutExpired(
            ["private-command"],
            1,
            output=b"private-output",
            stderr=b"private-stderr",
        )

    with monkeypatch.context() as patch:
        patch.setattr(
            ruby_registry,
            "ruby_registry_candidates"
            if boundary == "inventory"
            else "inspect_ruby_distribution",
            timeout,
        )
        observed = case.reader(RubyRequestBudget(transport)).observe(
            case.original
        )
    assert observed.classification == "unknown"
    assert observed.failure_kind == "SubprocessError"
    assert observed.failure_stage == "interpretation"
    assert observed.distribution is None
    assert [response for _, response in observed.responses] == responses
    assert len(transport.requests) == len(responses)
    assert b"private-" not in canonicalize(observed.to_document())
    fresh = case.reader(
        RubyRequestBudget(ScriptedTransport(*responses))
    ).observe(case.original)
    assert fresh.classification == (
        "missing" if boundary == "inventory" else "exact"
    )
    assert fresh.failure_kind is fresh.failure_stage is None
    replayed = replay_ruby_observation(
        observed.to_document(), case.original, case.registry
    )
    assert replayed.to_document() == observed.to_document()
    assert replayed.classification == "unknown"
    assert replayed.distribution is None


def _failed(case, stage):
    """Model a terminal failure after two complete native responses."""
    prefix = case.exact_responses()[:2]
    observed = case.reader(
        RubyRequestBudget(
            ScriptedTransport(*prefix, OSError("private-failure"))
        )
    ).observe(case.original)
    document = observed.to_document()
    if stage == "interpretation":
        document["failure-stage"] = "interpretation"
        document["failure-kind"] = "SubprocessError"
    return document


@pytest.mark.parametrize("stage", ["request", "interpretation"])
@pytest.mark.parametrize(
    "change",
    [
        "url",
        "order",
        "digest",
        "status",
        "base64",
        "envelope-field",
        "entry-field",
        "missing-prefix",
        "duplicate-prefix",
        "count-limit",
    ],
)
def test_ruby_failed_replay_still_validates_ordered_response_envelopes(
    ruby_publication_cases, stage, change
):
    """Terminal failure cannot hide malformed or unconsumed prefix evidence."""
    case = ruby_publication_cases["rubygems"]
    document = _failed(case, stage)
    assert (
        replay_ruby_observation(
            document, case.original, case.registry
        ).to_document()
        == document
    )
    entries = document["responses"]
    changes = {
        "url": lambda: entries[-1].update(
            url="https://foreign.invalid/metadata"
        ),
        "order": entries.reverse,
        "digest": lambda: entries[-1]["response"].update(
            {"body-digest": "sha256:" + "f" * 64}
        ),
        "status": lambda: entries[-1]["response"].update(status=True),
        "base64": lambda: entries[-1]["response"].update(
            {"body-base64": "%%%"}
        ),
        "envelope-field": lambda: entries[-1]["response"].update(extra=True),
        "entry-field": lambda: entries[-1].update(extra=True),
        "missing-prefix": lambda: entries.pop(0),
        "duplicate-prefix": lambda: entries.append(deepcopy(entries[-1])),
        "count-limit": lambda: entries.extend(
            deepcopy(entries[-1]) for _ in range(31)
        ),
    }
    changes[change]()
    with pytest.raises((ValueError, TypeError)):
        replay_ruby_observation(document, case.original, case.registry)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("failure-kind", "RemoteDisconnected"),
        ("failure-kind", "ArbitraryException"),
        ("failure-kind", ""),
        ("failure-kind", None),
        ("failure-kind", True),
        ("failure-kind", []),
        ("failure-stage", "after-response"),
        ("failure-stage", None),
        ("failure-stage", 1),
        ("classification", "exact"),
        ("classification", "missing"),
        ("artifact-digest", "sha256:" + "f" * 64),
        ("responses", {}),
    ],
)
def test_ruby_failed_replay_rejects_open_or_inconsistent_failure_metadata(
    ruby_publication_cases, field, value
):
    """Failure metadata is closed and cannot carry success authority."""
    case = ruby_publication_cases["rubygems"]
    document = _failed(case, "request")
    document[field] = value
    with pytest.raises((ValueError, TypeError)):
        replay_ruby_observation(document, case.original, case.registry)


def test_ruby_interpretation_failure_requires_complete_response(
    ruby_publication_cases,
):
    """Interpretation cannot attest a boundary preceding every HTTP response."""
    case = ruby_publication_cases["rubygems"]
    document = _failed(case, "interpretation")
    document["responses"] = []
    with pytest.raises(ValueError, match="failure family or stage"):
        replay_ruby_observation(document, case.original, case.registry)


@pytest.mark.parametrize("state", ["missing", "exact", "conflicting"])
def test_ruby_success_replay_keeps_null_failure_metadata(
    ruby_publication_cases, state
):
    """Valid native results retain closed null failure fields unchanged."""
    from ..ruby_registry_fixtures import inventory  # noqa: PLC0415

    case = ruby_publication_cases["rubygems"]
    observed = (
        case.reader(
            RubyRequestBudget(
                ScriptedTransport(inventory(platform="x86_64-linux"))
            )
        ).observe(case.original)
        if state == "conflicting"
        else getattr(case, state)
    )
    document = observed.to_document()
    assert document["failure-kind"] is document["failure-stage"] is None
    replayed = replay_ruby_observation(document, case.original, case.registry)
    assert replayed.classification == state
    assert replayed.to_document() == document


@pytest.mark.parametrize(
    "change",
    ["stage-without-kind", "kind-without-stage", "success-with-failure"],
)
def test_ruby_success_cannot_acquire_failure_metadata_to_skip_native_checks(
    ruby_publication_cases, change
):
    """A success claim must be reconstructed without injected failures."""
    case = ruby_publication_cases["rubygems"]
    document = case.exact.to_document()
    if change != "kind-without-stage":
        document["failure-stage"] = "interpretation"
    if change != "stage-without-kind":
        document["failure-kind"] = "SubprocessError"
    with pytest.raises(ValueError, match="failure family or stage"):
        replay_ruby_observation(document, case.original, case.registry)
