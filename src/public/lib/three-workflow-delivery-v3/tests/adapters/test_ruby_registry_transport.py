"""Cumulative request budgets and fixed HTTPS transport controls."""

import ssl

import pytest
from three_workflow_delivery_v3.adapters import ruby_registry as module
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyHttpsTransport,
    RubyRequestBudget,
)

from ..ruby_registry_fixtures import ScriptedTransport

REQUEST_LIMIT = 32


class ControlledResponse:
    """Expose bounded reads and content metadata without a socket."""

    def __init__(self, body=b"data", status=302):
        """Retain the selected status to verify redirects are not followed."""
        self.body = body
        self.status = status
        self.read_sizes = []

    def read(self, size):
        """Honor the same bounded read surface used by http.client."""
        self.read_sizes.append(size)
        return self.body[:size]

    def getheader(self, name, default):
        """Supply only the supported content metadata."""
        assert name == "Content-Type"
        assert default == ""
        return "application/octet-stream"


class ControlledConnection:
    """Record one request and guarantee the adapter closes the connection."""

    def __init__(self, response):
        """Keep the controlled response or interruption."""
        self.response = response
        self.requests = []
        self.closed = False

    def request(self, method, path, *, body, headers):
        """Record actual origin path, bytes and headers."""
        self.requests.append((method, path, body, headers))

    def getresponse(self):
        """Model response interruption after a request was sent."""
        if isinstance(self.response, Exception):
            raise self.response
        return self.response

    def close(self):
        """Expose the mandatory close, including rejected responses."""
        self.closed = True


@pytest.fixture
def https_factory(monkeypatch):
    """Replace only the TLS socket constructor, preserving real SSL defaults."""
    calls = []
    response = ControlledResponse()
    connection = ControlledConnection(response)

    def connect(host, port, *, timeout, context):
        calls.append((host, port, timeout, context))
        return connection

    monkeypatch.setattr(module.platform, "python_version", lambda: "3.14.3")
    monkeypatch.setattr(
        module.ssl, "OPENSSL_VERSION", "OpenSSL 3.5.5 27 Jan 2026"
    )
    monkeypatch.setattr(module.http.client, "HTTPSConnection", connect)
    return RubyHttpsTransport(), connection, response, calls


def test_ruby_https_transport_uses_verified_tls_and_one_bounded_response(
    https_factory, monkeypatch
):
    """Ambient proxies and redirects cannot alter the admitted destination."""
    transport, connection, response, calls = https_factory
    monkeypatch.setenv("HTTPS_PROXY", "http://untrusted.invalid:8080")
    result = transport.request(
        "POST",
        "https://rubygems.org/api/v1/gems?exact=true",
        {"Authorization": "synthetic"},
        b"original",
        4,
    )
    assert result == RubyHttpResponse(302, b"data", "application/octet-stream")
    assert connection.requests == [
        (
            "POST",
            "/api/v1/gems?exact=true",
            b"original",
            {"Authorization": "synthetic"},
        )
    ]
    assert response.read_sizes == [5]
    assert len(calls) == 1
    host, port, timeout, context = calls[0]
    assert (host, port, timeout) == ("rubygems.org", 443, 30)
    assert context.minimum_version == ssl.TLSVersion.TLSv1_2
    assert context.check_hostname is True
    assert context.verify_mode == ssl.CERT_REQUIRED
    assert connection.closed is True


@pytest.mark.parametrize("failure", ["oversized", "interrupted"])
def test_ruby_https_transport_closes_without_retry_after_failed_response(
    https_factory, failure
):
    """An over-bound body or network exception cannot trigger another send."""
    transport, connection, response, calls = https_factory
    failures = {
        "oversized": (response, ValueError),
        "interrupted": (OSError("controlled disconnect"), OSError),
    }
    connection.response, error = failures[failure]
    with pytest.raises(error):
        transport.request("GET", "https://rubygems.org/index", {}, None, 3)
    assert connection.closed is True
    assert len(connection.requests) == len(calls) == 1


@pytest.mark.parametrize(
    ("method", "url", "limit"),
    [
        ("DELETE", "https://rubygems.org/a", 4),
        ("GET", "http://rubygems.org/a", 4),
        ("GET", "https:///a", 4),
        ("GET", "https://user@rubygems.org/a", 4),
        ("GET", "https://user:password@rubygems.org/a", 4),
        ("GET", "https://rubygems.org:444/a", 4),
        ("GET", "https://rubygems.org/a#fragment", 4),
        ("GET", "https://rubygems.org/a\r\nInjected:yes", 4),
        ("GET", "https://rubygems.org/a\\b", 4),
        ("GET", "https://rubygems.org/a", 0),
        ("GET", "https://rubygems.org/a", True),
        ("GET", "https://rubygems.org/a", 2097153),
    ],
)
def test_ruby_https_rejects_unsafe_requests_before_connection(
    https_factory, method, url, limit
):
    """Invalid requests fail before any TLS connection or credential send."""
    transport, connection, _response, calls = https_factory
    with pytest.raises(ValueError, match="bounded Ruby HTTPS request"):
        transport.request(method, url, {}, None, limit)
    assert calls == []
    assert connection.requests == []


@pytest.mark.parametrize(
    ("python", "openssl"),
    [("3.14.2", "OpenSSL 3.5.5 27 Jan 2026"), ("3.14.3", "OpenSSL 3.5.4")],
)
def test_ruby_https_transport_requires_exact_runtime_profile(
    monkeypatch, python, openssl
):
    """A host runtime drift cannot silently change the accepted transport."""
    monkeypatch.setattr(module.platform, "python_version", lambda: python)
    monkeypatch.setattr(module.ssl, "OPENSSL_VERSION", openssl)
    with pytest.raises(ValueError, match="admitted profile"):
        RubyHttpsTransport()


def test_ruby_request_budget_counts_failed_sends_without_refund():
    """Allow the final request and block later sends before transport."""
    first = RubyHttpResponse(200, b"ok")
    transport = ScriptedTransport(
        first, OSError("controlled failure"), *([first] * 30)
    )
    budget = RubyRequestBudget(transport)
    assert (
        budget.request("GET", "https://rubygems.org/index", {}, None, 4)
        is first
    )
    with pytest.raises(OSError, match="controlled failure"):
        budget.request("GET", "https://rubygems.org/index", {}, None, 4)
    for _ in range(30):
        assert (
            budget.request("GET", "https://rubygems.org/index", {}, None, 4)
            is first
        )
    assert budget.used == REQUEST_LIMIT
    with pytest.raises(ValueError, match="budget exhausted"):
        budget.request(
            "POST", "https://rubygems.org/api/v1/gems", {}, b"original", 4
        )
    assert len(transport.requests) == REQUEST_LIMIT
    assert all(request[0] == "GET" for request in transport.requests)


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(status=True, body=b"x"),
        RubyHttpResponse(99, b"x"),
        RubyHttpResponse(600, b"x"),
        RubyHttpResponse(200, "text"),
        RubyHttpResponse(200, b"12345"),
    ],
)
def test_ruby_request_budget_rejects_invalid_response_without_refund(response):
    """Untrusted transport output is checked even through a supplied seam."""
    transport = ScriptedTransport(response)
    budget = RubyRequestBudget(transport)
    with pytest.raises(ValueError, match="invalid or excessive"):
        budget.request("GET", "https://rubygems.org/index", {}, None, 4)
    assert budget.used == 1
    assert len(transport.requests) == 1
