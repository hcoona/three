"""Registry observations over native indexes and original gem bytes."""

import base64
import http.client
from dataclasses import replace

import pytest
from three_workflow_delivery_v3._ruby_native import RUBY_RELEASE_UNIT
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryReader,
    RubyRequestBudget,
    RubyScreenedResponse,
    ruby_response_document,
)
from three_workflow_delivery_v3.adapters.rubygems import RubyRegistry
from three_workflow_delivery_v3.canonical import canonicalize

from ..ruby_registry_fixtures import (
    READ_TOKEN,
    VERSION,
    ScriptedTransport,
    inventory,
    json_response,
    metadata,
    native_index,
)

INDEX_LIMIT = 2 * 1024 * 1024
TWO_READS = 2
THREE_READS = 3


def _reader(name, *responses):
    transport = ScriptedTransport(*responses)
    budget = RubyRequestBudget(transport)
    reader = RubyRegistryReader(
        RubyRegistry(name),
        budget,
        github_read_token=READ_TOKEN if name == "github-packages" else None,
    )
    return reader, transport


@pytest.fixture(scope="module")
def indexes(tmp_path_factory):
    """Prepare three tiny actual Marshal indexes once, without a gem rebuild."""
    root = tmp_path_factory.mktemp("ruby-native-indexes")
    return {
        "empty": native_index(root, []),
        "exact": native_index(root, [[RUBY_RELEASE_UNIT, VERSION, "ruby"]]),
        "alternate": native_index(
            root, [[RUBY_RELEASE_UNIT, VERSION + ".0", "ruby"]]
        ),
        "platform": native_index(
            root, [[RUBY_RELEASE_UNIT, VERSION, "x86_64-linux"]]
        ),
    }


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_registry_missing_requires_complete_destination_inventory(
    ruby_release_original, indexes, name
):
    """Only the successful inventory contract establishes absence."""
    original = ruby_release_original[2]
    responses = {
        "github-packages": [RubyHttpResponse(200, indexes["empty"])] * 2,
        "rubygems": [json_response([])],
    }
    reader, transport = _reader(name, *responses[name])
    observed = reader.observe(original)
    assert observed.classification == "missing"
    assert observed.distribution is None
    assert observed.failure_kind is None
    assert tuple(response for _, response in observed.responses) == tuple(
        responses[name]
    )
    assert [request[1] for request in transport.requests] == {
        "github-packages": [
            "https://rubygems.pkg.github.com/hcoona/specs.4.8.gz",
            "https://rubygems.pkg.github.com/hcoona/prerelease_specs.4.8.gz",
        ],
        "rubygems": [
            f"https://rubygems.org/api/v1/versions/{RUBY_RELEASE_UNIT}.json"
        ],
    }[name]
    assert all(
        request[0] == "GET" and request[3] is None
        for request in transport.requests
    )
    assert all(request[4] == INDEX_LIMIT for request in transport.requests)


def test_rubygems_package_not_found_establishes_missing(ruby_release_original):
    """RubyGems package inventory 404 has its own explicit absence meaning."""
    response = RubyHttpResponse(404, b"package not found")
    reader, transport = _reader("rubygems", response)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "missing"
    assert ruby_response_document(
        observed.responses[0][1]
    ) == ruby_response_document(response)
    assert len(transport.requests) == 1


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_registry_exact_requires_downloaded_inspected_original(
    ruby_release_original, indexes, name
):
    """Exact evidence includes all metadata/index replies and original bytes."""
    original = ruby_release_original[2]
    responses = {
        "github-packages": [
            RubyHttpResponse(200, indexes["empty"]),
            RubyHttpResponse(200, indexes["exact"]),
        ],
        "rubygems": [inventory(), json_response(metadata(original))],
    }[name] + [
        RubyHttpResponse(200, original.content, "application/octet-stream")
    ]
    reader, transport = _reader(name, *responses)
    observed = reader.observe(original)
    assert observed.classification == "exact"
    assert observed.distribution == original
    assert observed.failure_kind is None
    assert tuple(
        ruby_response_document(response) for _, response in observed.responses
    ) == tuple(ruby_response_document(response) for response in responses)
    assert (
        transport.requests[-1][1]
        == RubyRegistry(name).origin + "/gems/" + original.filename
    )
    expected_headers = {
        "github-packages": {
            "Authorization": "Basic "
            + base64.b64encode(("hcoona:" + READ_TOKEN).encode()).decode()
        },
        "rubygems": {},
    }[name]
    assert all(request[2] == expected_headers for request in transport.requests)
    assert observed.to_document()["artifact-digest"] == original.digest
    assert READ_TOKEN not in repr(observed)
    assert READ_TOKEN.encode() not in canonicalize(observed.to_document())


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
@pytest.mark.parametrize("candidate", ["alternate", "platform", "duplicate"])
def test_ruby_registry_ambiguous_native_coordinates_block_download(
    ruby_release_original, indexes, name, candidate
):
    """Native-equal alternate spelling/platform and duplicate rows block."""
    github = {
        "alternate": [indexes["empty"], indexes["alternate"]],
        "platform": [indexes["empty"], indexes["platform"]],
        "duplicate": [indexes["exact"], indexes["exact"]],
    }
    rubygems = {
        "alternate": inventory(VERSION + ".0"),
        "platform": inventory(platform="x86_64-linux"),
        "duplicate": json_response(
            [{"number": VERSION, "platform": "ruby"}] * 2
        ),
    }
    responses = {
        "github-packages": [
            RubyHttpResponse(200, body) for body in github[candidate]
        ],
        "rubygems": [rubygems[candidate]],
    }[name]
    reader, transport = _reader(name, *responses)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "conflicting"
    assert observed.distribution is None
    assert observed.failure_kind is None
    assert len(transport.requests) == len(responses)
    assert all("/gems/" not in request[1] for request in transport.requests)


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_registry_other_original_bytes_conflict(
    ruby_release_original, indexes, name
):
    """A coordinate cannot satisfy qualification with different whole bytes."""
    original = ruby_release_original[2]
    other = replace(original, content=original.content + b"different")
    responses = {
        "github-packages": [
            RubyHttpResponse(200, indexes["empty"]),
            RubyHttpResponse(200, indexes["exact"]),
        ],
        "rubygems": [inventory(), json_response(metadata(other))],
    }[name] + [RubyHttpResponse(200, other.content)]
    reader, _transport = _reader(name, *responses)
    observed = reader.observe(original)
    assert observed.classification == "conflicting"
    assert observed.distribution is None
    assert observed.responses[-1][1].body == other.content


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_registry_equal_bytes_with_foreign_expected_witness_are_unknown(
    ruby_release_original, indexes, name
):
    """Byte equality alone cannot bypass native package/witness inspection."""
    original = ruby_release_original[2]
    forged = replace(
        original,
        witness=replace(original.witness, control_digest="sha256:" + "f" * 64),
    )
    responses = {
        "github-packages": [
            RubyHttpResponse(200, indexes["empty"]),
            RubyHttpResponse(200, indexes["exact"]),
        ],
        "rubygems": [inventory(), json_response(metadata(original))],
    }[name] + [RubyHttpResponse(200, original.content)]
    reader, _transport = _reader(name, *responses)
    observed = reader.observe(forged)
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert observed.failure_kind == "ValueError"
    assert observed.responses[-1][1].body == original.content


@pytest.mark.parametrize("status", [301, 302, 401, 403, 404, 429, 500])
@pytest.mark.parametrize("index_number", [0, 1])
def test_github_both_indexes_must_succeed_before_missing(
    ruby_release_original, indexes, status, index_number
):
    """Unavailable or redirected native indexes never prove absence."""
    responses = [RubyHttpResponse(200, indexes["empty"])] * index_number
    responses.append(RubyHttpResponse(status, b"unavailable"))
    reader, transport = _reader("github-packages", *responses)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert observed.failure_kind == "ValueError"
    assert tuple(
        ruby_response_document(response) for _, response in observed.responses
    ) == tuple(ruby_response_document(response) for response in responses)
    if status in {301, 302}:
        assert isinstance(observed.responses[-1][1], RubyScreenedResponse)
        assert "body-base64" not in ruby_response_document(
            observed.responses[-1][1]
        )
    assert len(transport.requests) == len(responses)


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(302, b"redirect"),
        RubyHttpResponse(401, b"unauthorized"),
        RubyHttpResponse(500, b"unavailable"),
        RubyHttpResponse(200, b"[]", "text/html"),
        RubyHttpResponse(200, b"not-json", "application/json"),
        json_response({"versions": []}),
        json_response([{"number": VERSION}]),
        json_response([{"number": VERSION, "platform": "ruby"}] * 4097),
    ],
)
def test_rubygems_unverified_inventory_remains_unknown(
    ruby_release_original, response
):
    """Malformed or unavailable inventories cannot manufacture absence."""
    reader, transport = _reader("rubygems", response)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert ruby_response_document(
        observed.responses[0][1]
    ) == ruby_response_document(response)
    assert observed.distribution is None
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("name", "another-package"),
        ("version", VERSION + ".0"),
        ("platform", "x86_64-linux"),
        ("yanked", True),
        ("yanked", 0),
        ("yanked", None),
        ("sha", "A" * 64),
        ("sha", "short"),
        ("gem_uri", "https://foreign.invalid/original.gem"),
        ("gem_uri", "https://rubygems.org/gems/../original.gem"),
    ],
)
def test_rubygems_exact_metadata_must_bind_identity_state_and_download_path(
    ruby_release_original, field, value
):
    """Unsupported metadata fails before issuing any original download."""
    original = ruby_release_original[2]
    document = metadata(original)
    document[field] = value
    response = json_response(document)
    reader, transport = _reader("rubygems", inventory(), response)
    observed = reader.observe(original)
    assert observed.classification == "unknown"
    assert observed.failure_kind == "ValueError"
    assert observed.responses[-1][1] == response
    assert len(transport.requests) == TWO_READS
    assert transport.requests[-1][1].endswith(f"/{VERSION}.json?platform=ruby")


def test_rubygems_download_digest_must_match_metadata(ruby_release_original):
    """Matching qualified bytes still need consistent registry SHA metadata."""
    original = ruby_release_original[2]
    document = metadata(original)
    document["sha"] = "f" * 64
    reader, transport = _reader(
        "rubygems",
        inventory(),
        json_response(document),
        RubyHttpResponse(200, original.content),
    )
    observed = reader.observe(original)
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert len(observed.responses) == len(transport.requests) == THREE_READS


@pytest.mark.parametrize(
    "failure",
    [
        OSError("controlled timeout"),
        http.client.BadStatusLine("bad status"),
        http.client.IncompleteRead(b"partial", 8),
    ],
)
def test_ruby_reader_network_failure_retains_prior_noncredential_responses(
    ruby_release_original, indexes, failure
):
    """Transport failure stays unknown without dropping prior bounded facts."""
    first = RubyHttpResponse(200, indexes["empty"])
    reader, transport = _reader("github-packages", first, failure)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert observed.failure_kind == (
        "HTTPException"
        if isinstance(failure, http.client.HTTPException)
        else "OSError"
    )
    assert observed.failure_stage == "request"
    assert tuple(response for _, response in observed.responses) == (first,)
    assert len(transport.requests) == TWO_READS
    assert READ_TOKEN.encode() not in canonicalize(observed.to_document())


def test_ruby_reader_parser_failure_retains_original_response(
    ruby_release_original,
):
    """Invalid native bytes remain replayable evidence instead of missing."""
    response = RubyHttpResponse(200, b"not-an-index")
    reader, transport = _reader("github-packages", response)
    observed = reader.observe(ruby_release_original[2])
    assert observed.classification == "unknown"
    assert ruby_response_document(
        observed.responses[0][1]
    ) == ruby_response_document(response)
    assert (
        observed.to_document()["responses"][0]["response"]["body-base64"]
        == base64.b64encode(response.body).decode()
    )
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    ("name", "token"),
    [
        ("github-packages", None),
        ("github-packages", ""),
        ("github-packages", "bad\nvalue"),
        ("rubygems", READ_TOKEN),
    ],
)
def test_ruby_reader_rejects_missing_malformed_or_cross_origin_credentials(
    name, token
):
    """Credential admission rejects before any inventory request."""
    transport = ScriptedTransport()
    with pytest.raises(ValueError, match=r"capability|credential|malformed"):
        RubyRegistryReader(
            RubyRegistry(name),
            RubyRequestBudget(transport),
            github_read_token=token,
        )
    assert transport.requests == []


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
@pytest.mark.parametrize("status", [302, 404, 500])
def test_ruby_registry_download_failure_is_unknown_without_redirect_or_retry(
    ruby_release_original, indexes, name, status
):
    """Candidate existence does not excuse an unavailable original download."""
    original = ruby_release_original[2]
    responses = {
        "github-packages": [
            RubyHttpResponse(200, indexes["empty"]),
            RubyHttpResponse(200, indexes["exact"]),
        ],
        "rubygems": [inventory(), json_response(metadata(original))],
    }[name] + [RubyHttpResponse(status, b"unavailable")]
    reader, transport = _reader(name, *responses)
    observed = reader.observe(original)
    assert observed.classification == "unknown"
    assert observed.distribution is None
    assert observed.responses[-1][1].status == status
    assert len(transport.requests) == len(responses)
    assert transport.requests[-1][1].endswith("/gems/" + original.filename)
