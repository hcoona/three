"""Closed native Provider transport without target reevaluation."""

from __future__ import annotations

# ruff: noqa: D103
from copy import deepcopy
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.repository import dotnet_provider

from .test_dotnet_compiler import _native_scenario


@pytest.fixture(scope="module")
def native_transport_scenario(tmp_path_factory):
    """Reuse immutable modeled facts over an actual isolated Git target."""
    with pytest.MonkeyPatch.context() as patch:
        return _native_scenario(
            tmp_path_factory.mktemp("nuget-provider-transport"), patch
        )


def test_native_provider_result_round_trip_without_evaluation(
    native_transport_scenario, monkeypatch
):
    result = native_transport_scenario[3]
    evaluate = Mock(
        side_effect=AssertionError("target evaluation is forbidden")
    )
    monkeypatch.setattr(dotnet_provider, "run_native", evaluate)
    parsed = dotnet_provider.dotnet_provider_result_from_document(
        result.to_document()
    )
    assert parsed == result
    assert parsed.nbgv.nuget_package_version == "1.2.3-beta.42+Build.Meta"
    assert parsed.source_input_manifest == result.source_input_manifest
    assert parsed.native_evaluation_digest == result.native_evaluation_digest
    assert parsed.result_digest == result.result_digest
    evaluate.assert_not_called()


@pytest.mark.parametrize(
    ("path", "value"),
    [
        (("schema",), "workflow-delivery/v3/node-provider-result"),
        (("extra",), True),
        (("binding", "run-attempt"), None),
        (("binding", "workflow-run-id"), True),
        (("provider", "toolchain", "dotnet"), "11.0.100"),
        (("checkout", "shallow"), 0),
        (("checkout", "ancestry-complete"), 1),
        (("checkout", "tags-complete"), 1),
        (("checkout", "credentials-persisted"), 0),
        (("checkout", "authoritative-remote-url"), True),
        (("nbgv", "native", "npmPackageVersion"), "1.2.3"),
        (("project-nodes",), []),
        (("global-inputs",), []),
        (("source-input-manifest",), []),
        (("unresolved",), ["missing-input"]),
        (("diagnostic-reference",), "not-success"),
        (("native-evaluation-digest",), "not-a-digest"),
    ],
    ids=(
        "npm-schema",
        "extra-field",
        "live-attempt",
        "boolean-run",
        "toolchain",
        "boolean-checkout",
        "numeric-ancestry",
        "numeric-tags",
        "numeric-credentials",
        "boolean-remote-url",
        "npm-native",
        "missing-project",
        "missing-globals",
        "missing-sources",
        "unresolved",
        "diagnostic",
        "evaluation-digest",
    ),
)
def test_native_provider_parser_rejects_malformed_facts(
    native_transport_scenario, path, value
):
    document = deepcopy(native_transport_scenario[3].to_document())
    parent = document
    for key in path[:-1]:
        parent = parent[key]
    parent[path[-1]] = value
    checkout_messages = {
        "shallow": "checkout shallow must be an exact Boolean",
        "ancestry-complete": (
            "checkout ancestry_complete must be an exact Boolean"
        ),
        "tags-complete": "checkout tags_complete must be an exact Boolean",
        "credentials-persisted": (
            "checkout credentials_persisted must be an exact Boolean"
        ),
        "authoritative-remote-url": "native fact must be a nonempty string",
    }
    message = checkout_messages[path[-1]] if path[0] == "checkout" else None
    with pytest.raises((TypeError, ValueError), match=message):
        dotnet_provider.dotnet_provider_result_from_document(document)
