"""Real process supervision over controlled native HTTP; no external sends."""

import base64
import multiprocessing
import time
from datetime import UTC, datetime, timedelta

import pytest
from three_workflow_delivery_v3.adapters import ruby_registry
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.platform import ruby_operation_http as module

from ..adapters import test_ruby_registry_redirect as redirect_cases
from ..adapters.test_ruby_registry_transport import (
    ControlledConnection,
    ControlledResponse,
)

indexes = redirect_cases.indexes


@pytest.fixture(autouse=True)
def profile(monkeypatch):
    """Pin the admitted profile without replacing the supervised transport."""
    monkeypatch.setattr(
        ruby_registry.platform, "python_version", lambda: "3.14.3"
    )
    monkeypatch.setattr(
        ruby_registry.ssl, "OPENSSL_VERSION", "OpenSSL 3.5.5 27 Jan 2026"
    )


def transport(seconds=5):
    """Use the actual wall clock and an explicit finite phase deadline."""
    return module.RubyOperationHttpsTransport(
        datetime.now(UTC) + timedelta(seconds=seconds)
    )


def test_supervised_request_keeps_one_native_send_and_ignores_proxy_redirect(
    monkeypatch, tmp_path
):
    """A real child returns the raw 302 and cannot follow or retry it."""
    calls = tmp_path / "calls"

    def connect(host, port, *, timeout, context):
        with calls.open("a") as stream:
            stream.write(f"{host}:{port}:{timeout}:{context.check_hostname}\n")
        return ControlledConnection(
            ControlledResponse(body=b"redirect", status=302)
        )

    monkeypatch.setattr(ruby_registry.http.client, "HTTPSConnection", connect)
    monkeypatch.setenv("HTTPS_PROXY", "http://untrusted.invalid:8080")
    before = {child.pid for child in multiprocessing.active_children()}
    result = transport().request(
        "GET", "https://rubygems.org/index", {}, None, 10
    )
    assert result == RubyHttpResponse(
        302, b"redirect", "application/octet-stream"
    )
    assert calls.read_text().splitlines() == ["rubygems.org:443:30:True"]
    assert {child.pid for child in multiprocessing.active_children()} == before


@pytest.mark.parametrize("delay_stage", ["connect", "body"])
@pytest.mark.parametrize("bound", ["phase", "request"])
def test_supervised_timeout_kills_and_reaps_child_without_retry(
    monkeypatch, tmp_path, delay_stage, bound
):
    """The deadline bounds DNS/connect and a stalled body, not just sockets."""
    sends = tmp_path / "sends"

    class StalledResponse(ControlledResponse):
        def read(self, _size):
            time.sleep(10)
            return b"late"

    def connect(*_args, **_kwargs):
        with sends.open("a") as stream:
            stream.write("attempt\n")
        if delay_stage == "connect":
            time.sleep(10)
        return ControlledConnection(StalledResponse())

    monkeypatch.setattr(ruby_registry.http.client, "HTTPSConnection", connect)
    if bound == "request":
        monkeypatch.setattr(module, "RUBY_HTTP_TIMEOUT", 0.1)
    before = {child.pid for child in multiprocessing.active_children()}
    started = time.monotonic()
    with pytest.raises(TimeoutError, match="deadline"):
        transport(0.1 if bound == "phase" else 5).request(
            "GET", "https://rubygems.org/index", {}, None, 10
        )
    assert time.monotonic() - started < 3  # noqa: PLR2004 - generous supervision ceiling
    assert sends.read_text() == "attempt\n"
    assert {child.pid for child in multiprocessing.active_children()} == before


@pytest.mark.parametrize(
    ("authorization", "reflection"),
    [
        ("Bearer synthetic-secret", b"Bearer synthetic-secret"),
        ("Bearer synthetic-secret", b"synthetic-secret"),
        (
            "Basic " + base64.b64encode(b"actor:synthetic-secret").decode(),
            b"actor:synthetic-secret",
        ),
        (
            "Basic " + base64.b64encode(b"actor:synthetic-secret").decode(),
            b"synthetic-secret",
        ),
    ],
)
@pytest.mark.parametrize("location", ["body", "content-type", "location"])
def test_supervised_response_never_returns_reflected_credentials(
    monkeypatch, authorization, reflection, location
):
    """Token reflection in either returned field becomes a generic failure."""
    response = RubyHttpResponse(
        500,
        reflection if location == "body" else b"failure",
        reflection.decode() if location == "content-type" else "text/plain",
        location="https://storage.invalid/?sig=" + reflection.decode()
        if location == "location"
        else None,
    )
    monkeypatch.setattr(
        ruby_registry.RubyHttpsTransport, "request", lambda *_: response
    )
    with pytest.raises(
        OSError, match=r"^Ruby supervised HTTP request failed$"
    ) as error:
        transport().request(
            "GET",
            "https://rubygems.org/index",
            {"Authorization": authorization},
            None,
            100,
        )
    assert "synthetic-secret" not in str(error.value)


def test_supervised_exception_does_not_expose_native_exception_or_retry(
    monkeypatch, tmp_path
):
    """A native disconnect can contain secrets but exposes no secret text."""
    calls = tmp_path / "attempts"

    def fail(*_args):
        with calls.open("a") as stream:
            stream.write("once\n")
        message = "synthetic-secret in native exception"
        raise OSError(message)

    monkeypatch.setattr(ruby_registry.RubyHttpsTransport, "request", fail)
    with pytest.raises(OSError, match=r"^Ruby supervised HTTP request failed$"):
        transport().request(
            "POST", "https://rubygems.org/api/v1/gems", {}, b"gem", 100
        )
    assert calls.read_text() == "once\n"


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(status=True, body=b"ok", content_type="text/plain"),
        RubyHttpResponse(99, b"ok", "text/plain"),
        RubyHttpResponse(600, b"ok", "text/plain"),
        RubyHttpResponse(200, b"too-long", "text/plain"),
    ],
)
def test_supervised_parent_rejects_malformed_or_oversized_child_response(
    monkeypatch, response
):
    """Untrusted child output cannot bypass the parent's response bound."""
    monkeypatch.setattr(
        ruby_registry.RubyHttpsTransport, "request", lambda *_: response
    )
    with pytest.raises(ValueError, match="response is malformed"):
        transport().request("GET", "https://rubygems.org/index", {}, None, 2)


@pytest.mark.parametrize("failure", ["expired", "naive", "zero", "oversize"])
def test_supervised_deadline_and_size_reject_before_process_creation(
    monkeypatch, failure
):
    """Expired authority and invalid bounds cannot start the native process."""
    calls = []
    current = datetime.now(UTC)
    if failure in {"expired", "naive"}:
        deadline = (
            current if failure == "expired" else current.replace(tzinfo=None)
        )
        with pytest.raises(ValueError, match="deadline"):
            module.RubyOperationHttpsTransport(deadline, clock=lambda: current)
    else:
        caller = transport()
        monkeypatch.setattr(
            multiprocessing.get_context("fork"),
            "Process",
            lambda **kwargs: calls.append(kwargs),
        )
        with pytest.raises(ValueError, match="bound is invalid"):
            caller.request(
                "GET",
                "https://rubygems.org/index",
                {},
                None,
                0 if failure == "zero" else 2097153,
            )
    assert calls == []


def test_supervised_empty_native_fields_remain_valid(monkeypatch):
    """An empty body/content-type is data, not malformed native transport."""
    monkeypatch.setattr(
        ruby_registry.RubyHttpsTransport,
        "request",
        lambda *_: RubyHttpResponse(204, b"", ""),
    )
    assert transport().request(
        "GET", "https://rubygems.org/index", {}, None, 10
    ) == RubyHttpResponse(204, b"", "")


def test_supervised_redirect_location_is_private_bounded_ipc(
    monkeypatch, tmp_path
):
    """One real child passes Location in memory without continuation."""
    location = "https://storage.invalid/?sig=synthetic-private-capability"
    response = ControlledResponse(body=b"redirect", status=302)
    response.headers.append(("Location", location))
    calls = tmp_path / "sends"

    def connect(*_args, **_kwargs):
        with calls.open("a") as stream:
            stream.write("send\n")
        return ControlledConnection(response)

    monkeypatch.setattr(ruby_registry.http.client, "HTTPSConnection", connect)
    before = {child.pid for child in multiprocessing.active_children()}
    result = transport().request(
        "GET", "https://rubygems.org/index", {}, None, 100
    )
    assert result.location == location
    assert result.location_invalid is False
    assert location not in repr(result)
    assert calls.read_text() == "send\n"
    assert {child.pid for child in multiprocessing.active_children()} == before


def test_each_redirect_send_is_separately_supervised_and_credential_scoped(
    monkeypatch, tmp_path, ruby_release_original, indexes
):
    """The reader crosses four child pipes without credential reuse."""
    original = ruby_release_original[2]
    calls = tmp_path / "sends"
    replies = [
        ControlledResponse(indexes["empty"], 200),
        ControlledResponse(indexes["exact"], 200),
        ControlledResponse(redirect_cases.REDIRECT_BODY, 302),
        ControlledResponse(original.content, 200),
    ]
    replies[2].headers.append(("Location", redirect_cases.LOCATION))

    class RecordingConnection(ControlledConnection):
        def request(self, method, path, *, body, headers):
            with calls.open("a") as stream:
                stream.write(str(bool(headers.get("Authorization"))) + "\n")
            super().request(method, path, body=body, headers=headers)

    def connect(*_args, **_kwargs):
        count = len(calls.read_text().splitlines()) if calls.exists() else 0
        return RecordingConnection(replies[count])

    monkeypatch.setattr(ruby_registry.http.client, "HTTPSConnection", connect)
    budget = ruby_registry.RubyRequestBudget(transport())
    reader = ruby_registry.RubyRegistryReader(
        redirect_cases.RubyRegistry("github-packages"),
        budget,
        github_read_token="synthetic-job-token",  # noqa: S106 - nonsecret fixture
    )
    before = {child.pid for child in multiprocessing.active_children()}
    observed = reader.observe(original)
    assert observed.classification == "exact"
    assert observed.distribution == original
    assert budget.used == 4  # noqa: PLR2004
    assert calls.read_text().splitlines() == ["True", "True", "True", "False"]
    assert {child.pid for child in multiprocessing.active_children()} == before
    redirect_cases.assert_screened(observed)
