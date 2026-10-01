"""Native local indexes and controlled HTTP without networking."""

import gzip
import json
import subprocess
from datetime import timedelta

from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_environment,
    ruby_executable,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import canonicalize

from .ruby_integration_fixtures import NOW

VERSION = "0.1.0.pre.beta.7"
READ_TOKEN = "synthetic-read-credential"  # noqa: S105 - nonsecret test data
WRITE_TOKEN = "synthetic-write-credential"  # noqa: S105 - nonsecret test data
ASSERTION = "synthetic-oidc-assertion"

_INDEX_BUILDER = r"""
require "rubygems"
require "json"
input = JSON.parse(STDIN.read)
rows = input.map do |name, version, platform|
  [name, Gem::Version.new(version), platform]
end
STDOUT.binmode
STDOUT.write(Marshal.dump(rows))
"""


def native_index(root, entries):
    """Serialize actual Gem::Version objects in an isolated pinned Ruby."""
    result = subprocess.run(  # noqa: S603 - fixed local fixture script
        (str(ruby_executable()), "-e", _INDEX_BUILDER),
        input=canonicalize(entries),
        cwd=root,
        env=ruby_environment(root / "index-home"),
        capture_output=True,
        check=True,
        timeout=30,
    )
    return gzip.compress(result.stdout, mtime=0)


def json_response(document, status=200):
    """Return a controlled JSON response with explicit MIME type."""
    return RubyHttpResponse(status, canonicalize(document), "application/json")


def inventory(version=VERSION, platform="ruby"):
    """Model RubyGems inventory, preserving the registry's exact spelling."""
    return json_response([{"number": version, "platform": platform}])


def metadata(distribution):
    """Bind version metadata to actual independently built original bytes."""
    return {
        "name": RUBY_RELEASE_UNIT,
        "version": distribution.witness.nbgv.native_version,
        "platform": "ruby",
        "yanked": False,
        "sha": distribution.digest.removeprefix("sha256:"),
        "gem_uri": "https://rubygems.org/gems/" + distribution.filename,
    }


def token_document():
    """Provide clearly synthetic bounded token-exchange fields."""
    return {
        "name": "synthetic-publisher-key",
        "rubygems_api_key": WRITE_TOKEN,
        "scopes": ["push_rubygem"],
        "expires_at": (NOW + timedelta(minutes=15)).isoformat(),
        "gem": {"name": RUBY_RELEASE_UNIT},
    }


class ScriptedTransport:
    """Record outbound requests and fail if orchestration sends unexpectedly."""

    def __init__(self, *responses):
        """Accept bounded responses or exceptions in intended response order."""
        self.responses = list(responses)
        self.requests = []

    def request(self, method, url, headers, body, maximum_bytes):
        """Observe actual bytes and credentials without opening a socket."""
        self.requests.append((method, url, dict(headers), body, maximum_bytes))
        if not self.responses:
            message = "unexpected request exhausted controlled transport"
            raise AssertionError(message)
        response = self.responses.pop(0)
        if isinstance(response, Exception):
            raise response
        return response

    def json_body(self, index=0):
        """Decode the recorded request only for explicit exchange assertions."""
        return json.loads(self.requests[index][3])
