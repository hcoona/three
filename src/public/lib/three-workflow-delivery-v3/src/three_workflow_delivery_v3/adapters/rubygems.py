"""Closed Ruby registry profiles for independent destination admission."""

from __future__ import annotations

from dataclasses import dataclass

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BYTE_LIMIT,
    RUBY_RELEASE_UNIT,
    RUBY_VERSION,
    RUBYGEMS_VERSION,
)
from three_workflow_delivery_v3._ruby_registry import (
    RUBY_INDEX_ENTRY_LIMIT,
    ruby_registry_reader_digest,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonical_sha256

RUBY_HTTP_TIMEOUT = 30
RUBY_HTTP_RESPONSE_LIMIT = 64 * 1024
RUBY_INDEX_LIMIT = 2 * 1024 * 1024
RUBY_GEM_LIMIT = RUBY_BYTE_LIMIT
RUBY_REGISTRY_REQUEST_LIMIT = 32


@dataclass(frozen=True, slots=True)
class RubyRegistry:
    """One exact destination, without runtime host or capability overrides."""

    name: str

    def __post_init__(self) -> None:
        """Reject aliases and unreviewed registry endpoints."""
        if self.name not in {"github-packages", "rubygems"}:
            message = "unsupported Ruby registry"
            raise ValueError(message)

    @property
    def origin(self) -> str:
        """Select the documented registry base, including GitHub namespace."""
        return (
            "https://rubygems.pkg.github.com/hcoona"
            if self.name == "github-packages"
            else "https://rubygems.org"
        )

    @property
    def channel(self) -> str:
        """Keep Buddy and Official as independent destination Attempts."""
        return "buddy" if self.name == "github-packages" else "official"

    @property
    def environment(self) -> str:
        """Select the stable, separately configured approval Environment."""
        return f"workflow-delivery-v3-ruby-{self.name}"

    @property
    def upload_url(self) -> str:
        """Select the one-shot binary upload endpoint."""
        return self.origin + "/api/v1/gems"

    @property
    def profile(self) -> dict[str, JsonValue]:
        """Freeze bounded protocol requirements without claiming activation."""
        github = self.name == "github-packages"
        return {
            "schema": "workflow-delivery/v3/ruby-destination-operation-profile",
            "destination": f"ruby/{self.name}-v1",
            "project": RUBY_RELEASE_UNIT,
            "channel": self.channel,
            "registry": self.origin,
            "environment": self.environment,
            "upload": self.upload_url,
            "method": "POST",
            "content-type": "application/octet-stream",
            "accepted-status": 200,
            "authentication": "github-job-token" if github else "ruby-oidc",
            "oidc-audience": None if github else "rubygems.org",
            "token-exchange": None
            if github
            else self.origin + "/api/v1/oidc/trusted_publisher/exchange_token",
            "index-protocol": "rubygems-marshal-4.8"
            if github
            else "rubygems-json-v1-v2",
            "native-index-reader": {
                "implementation-digest": ruby_registry_reader_digest(),
                "ruby": RUBY_VERSION,
                "rubygems": RUBYGEMS_VERSION,
                "entry-budget": RUBY_INDEX_ENTRY_LIMIT,
                "expanded-byte-budget": RUBY_INDEX_LIMIT,
            },
            "download-base": self.origin + "/gems/",
            "token-exchange-status": None if github else 201,
            "token-scopes": [] if github else ["push_rubygem"],
            "token-lifetime-seconds": None if github else 900,
            "client": "stdlib/http.client.HTTPSConnection",
            "python": "3.14.3",
            "tls": "OpenSSL 3.5.5 27 Jan 2026",
            "tls-minimum": "TLSv1.2",
            "verify-certificates": True,
            "proxy": False,
            "redirects": 0,
            "retries": 0,
            "timeout-seconds": RUBY_HTTP_TIMEOUT,
            "response-byte-budget": RUBY_HTTP_RESPONSE_LIMIT,
            "index-byte-budget": RUBY_INDEX_LIMIT,
            "gem-byte-budget": RUBY_GEM_LIMIT,
            "registry-request-budget": RUBY_REGISTRY_REQUEST_LIMIT,
            "upload-posts": 1,
            "post-upload-observation": {
                "maximum-reads": 6,
                "pending-spacing-seconds": 10,
                "admission-window-seconds": 60,
            },
        }

    @property
    def profile_digest(self) -> str:
        """Bind later Governance and publication to this precise profile."""
        return canonical_sha256(self.profile)
