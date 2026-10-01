"""Closed Ruby destination protocol profiles, without network effects."""

import pytest
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonical_sha256


@pytest.mark.parametrize(
    ("name", "origin", "channel", "authentication", "index"),
    [
        (
            "github-packages",
            "https://rubygems.pkg.github.com/hcoona",
            "buddy",
            "github-job-token",
            "rubygems-marshal-4.8",
        ),
        (
            "rubygems",
            "https://rubygems.org",
            "official",
            "ruby-oidc",
            "rubygems-json-v1-v2",
        ),
    ],
)
def test_ruby_registry_freezes_independent_bounded_operation_profile(
    name, origin, channel, authentication, index
):
    """Freeze destination credentials, endpoints and request budgets."""
    registry = RubyRegistry(name)
    profile = registry.profile
    assert registry.origin == origin
    assert registry.channel == channel
    assert registry.environment == f"workflow-delivery-v3-ruby-{name}"
    assert registry.upload_url == origin + "/api/v1/gems"
    assert profile == {
        "schema": "workflow-delivery/v3/ruby-destination-operation-profile",
        "destination": f"ruby/{name}-v1",
        "project": "hcoona-release-smoke-ruby",
        "channel": channel,
        "registry": origin,
        "environment": f"workflow-delivery-v3-ruby-{name}",
        "upload": origin + "/api/v1/gems",
        "method": "POST",
        "content-type": "application/octet-stream",
        "accepted-status": 200,
        "authentication": authentication,
        "oidc-audience": None if name == "github-packages" else "rubygems.org",
        "token-exchange": None
        if name == "github-packages"
        else "https://rubygems.org/api/v1/oidc/trusted_publisher/exchange_token",
        "index-protocol": index,
        "client": "stdlib/http.client.HTTPSConnection",
        "python": "3.14.3",
        "tls": "OpenSSL 3.5.5 27 Jan 2026",
        "tls-minimum": "TLSv1.2",
        "verify-certificates": True,
        "proxy": False,
        "redirects": 0,
        "retries": 0,
        "timeout-seconds": 30,
        "response-byte-budget": 65536,
        "index-byte-budget": 2097152,
        "gem-byte-budget": 2097152,
        "registry-request-budget": 32,
        "upload-posts": 1,
        "post-upload-observation": {
            "maximum-reads": 6,
            "pending-spacing-seconds": 10,
            "admission-window-seconds": 60,
        },
    }
    assert registry.profile_digest == canonical_sha256(profile)
    profile["registry"] = "https://foreign.invalid"
    assert registry.profile["registry"] == origin
    assert registry.profile_digest != canonical_sha256(profile)


@pytest.mark.parametrize(
    "name",
    [
        "",
        "github",
        "RubyGems",
        "rubygems.org",
        "https://rubygems.org",
        "rubygems/",
        "testpypi",
    ],
)
def test_ruby_registry_rejects_aliases_or_unreviewed_hosts(name):
    """Runtime destination data cannot substitute a convenient URL or alias."""
    with pytest.raises(ValueError, match="unsupported Ruby registry"):
        RubyRegistry(name)
