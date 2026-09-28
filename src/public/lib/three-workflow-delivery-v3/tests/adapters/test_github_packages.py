"""Test-first contracts for the commit-8 GitHub Packages Adapter."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import importlib
from pathlib import Path

import pytest
from three_workflow_delivery_v3.records.governance import (
    admit_governance_acceptance_evidence,
)
from three_workflow_delivery_v3.records.release import (
    ArtifactVariantIdentity,
    BuddyExecutionIdentity,
    DestinationProjection,
    ExternalPackageCoordinate,
    ReleaseAttemptIdentity,
    ReleaseBuildIdentity,
    ReleaseOutputIdentity,
    publication_mutable_resource_keys,
    publication_serialization_projection,
)

TARGET = "a" * 40
TOKEN = "test-token-must-never-be-recorded"  # noqa: S105
VERSION = "1.2.3-beta.42.ge123456"
RESPONSE_DIGEST = "sha256:" + ("3" * 64)
COMPLETE_KEY_COUNT = 2


class RecordingTransport:
    """Strict in-memory HTTP fake."""

    def __init__(self, responses: dict[str, object]) -> None:
        """Store exact scripted responses."""
        self.responses = responses
        self.requests: list[
            tuple[str, tuple[tuple[str, str], ...], float, int]
        ] = []

    def get(
        self,
        url: str,
        *,
        headers: tuple[tuple[str, str], ...],
        timeout: float,
        max_bytes: int,
    ) -> object:
        """Record and return one exact scripted response."""
        self.requests.append((url, headers, timeout, max_bytes))
        response = self.responses[url]
        if isinstance(response, BaseException):
            raise response
        return response


def _adapter():
    try:
        return importlib.import_module(
            "three_workflow_delivery_v3.adapters.github_packages"
        )
    except ModuleNotFoundError as error:
        pytest.fail(
            "commit-8 phase-2 production blocker: "
            "three_workflow_delivery_v3.adapters.github_packages is missing",
            pytrace=False,
        )
        raise AssertionError from error


def _attempt() -> ReleaseAttemptIdentity:
    return ReleaseAttemptIdentity(
        execution=BuddyExecutionIdentity(
            channel="buddy",
            release_unit="hcoona-release-smoke-npm",
            target=TARGET,
        ),
        workflow_run_id=101,
    )


def _coordinate(
    *,
    version: str = VERSION,
    package: str = "@hcoona/hcoona-release-smoke-npm",
) -> ExternalPackageCoordinate:
    return ExternalPackageCoordinate(
        channel="buddy",
        destination_id="npm/github-packages-hcoona-three-v1",
        package_name=package,
        native_version=version,
    )


def _projection(
    *,
    version: str = VERSION,
    package: str = "@hcoona/hcoona-release-smoke-npm",
) -> DestinationProjection:
    coordinate = _coordinate(version=version, package=package)
    output = ReleaseOutputIdentity(
        variant=ArtifactVariantIdentity(
            build=ReleaseBuildIdentity(
                release_unit="hcoona-release-smoke-npm",
                build_id="npm-package",
                definition_id="node/npm-package-v1",
                project_id="@hcoona/hcoona-release-smoke-npm",
            ),
            variant_id="default",
            dimensions=(),
        ),
        output_id="npm-tarball",
        logical_role="primary-package",
        media_kind="npm-tarball",
    )
    return DestinationProjection(
        projection_id="buddy-github-packages",
        destination_id=coordinate.destination_id,
        registry="https://npm.pkg.github.com",
        coordinate=coordinate,
        output=output,
        operation="conditional-create-npm-version-and-target-tag",
        observation_contract_id="npm/github-packages-observation-v1",
        potential_action_id="publish-github-packages",
    )


def test_github_packages_requests_exact_escaped_endpoints_headers_and_pages() -> (  # noqa: E501
    None
):
    adapter = _adapter()

    assert adapter.github_package_versions_url(
        owner="hcoona",
        package_name="@hcoona/hcoona-release-smoke-npm",
        page=1,
        per_page=100,
    ) == (
        "https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm/versions?per_page=100&page=1"
    )
    assert adapter.github_package_versions_url(
        owner="hcoona",
        package_name="@hcoona/hcoona-release-smoke-npm",
        page=2,
        per_page=100,
    ) == (
        "https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm/versions?per_page=100&page=2"
    )
    assert adapter.npm_package_metadata_url(
        "@hcoona/hcoona-release-smoke-npm"
    ) == ("https://npm.pkg.github.com/@hcoona%2Fhcoona-release-smoke-npm")
    assert adapter.github_api_headers(TOKEN) == (
        ("Accept", "application/vnd.github+json"),
        ("Authorization", f"Bearer {TOKEN}"),
        ("X-GitHub-Api-Version", "2022-11-28"),
    )
    with pytest.raises(ValueError, match="positive"):
        adapter.validate_observation_bounds(
            timeout=0,
            max_bytes=1_000_000,
            max_pages=10,
        )
    with pytest.raises(ValueError, match="positive"):
        adapter.validate_observation_bounds(
            timeout=20,
            max_bytes=0,
            max_pages=10,
        )
    with pytest.raises(ValueError, match="positive"):
        adapter.validate_observation_bounds(
            timeout=20,
            max_bytes=1_000_000,
            max_pages=0,
        )
    with pytest.raises(ValueError, match="at most 100"):
        adapter.validate_observation_bounds(
            timeout=20,
            max_bytes=1_000_000,
            max_pages=101,
        )


def test_github_packages_redacts_token_and_rejects_cross_origin_redirect() -> (
    None
):
    adapter = _adapter()
    same_origin = adapter.redirect_headers(
        source_url="https://npm.pkg.github.com/a",
        target_url="https://npm.pkg.github.com/b",
        headers=(("Authorization", f"Bearer {TOKEN}"),),
    )
    cross_origin = adapter.redirect_headers(
        source_url="https://npm.pkg.github.com/a",
        target_url="https://objects.githubusercontent.com/b",
        headers=(("Authorization", f"Bearer {TOKEN}"),),
    )

    assert same_origin == (("Authorization", f"Bearer {TOKEN}"),)
    assert cross_origin == ()
    assert TOKEN not in adapter.redact_diagnostic(
        f"request failed Authorization: Bearer {TOKEN}"
    )


def test_concrete_transport_never_sends_authorization_cross_origin() -> None:
    adapter = _adapter()
    calls: list[tuple[str, str | None]] = []

    def opener(request, timeout: float, max_bytes: int):
        del timeout, max_bytes
        calls.append((request.full_url, request.get_header("Authorization")))
        if len(calls) == 1:
            return adapter.GitHubPackagesHttpResponse(
                302,
                request.full_url,
                (
                    (
                        "Location",
                        "https://objects.githubusercontent.com/pkg.tgz",
                    ),
                ),
                b"",
            )
        return adapter.GitHubPackagesHttpResponse(
            200,
            request.full_url,
            (),
            b"ok",
        )

    transport = adapter.GitHubPackagesHttpTransport(opener=opener)
    response = transport.get(
        "https://npm.pkg.github.com/pkg",
        headers=(("Authorization", f"Bearer {TOKEN}"),),
        timeout=1.0,
        max_bytes=100,
    )

    assert response.status == 200
    assert calls == [
        ("https://npm.pkg.github.com/pkg", f"Bearer {TOKEN}"),
        ("https://objects.githubusercontent.com/pkg.tgz", None),
    ]


def test_concrete_transport_rejects_bad_redirects_and_off_origin() -> None:
    adapter = _adapter()

    def cycle_opener(request, timeout: float, max_bytes: int):
        del timeout, max_bytes
        return adapter.GitHubPackagesHttpResponse(
            302,
            request.full_url,
            (("Location", request.full_url),),
            b"",
        )

    def missing_location_opener(
        request,
        timeout: float,
        max_bytes: int,
    ):
        del timeout, max_bytes
        return adapter.GitHubPackagesHttpResponse(
            302,
            request.full_url,
            (),
            b"",
        )

    cycle_transport = adapter.GitHubPackagesHttpTransport(
        opener=cycle_opener,
        max_redirects=1,
    )
    with pytest.raises(adapter.GitHubPackagesPolicyError, match="cycle"):
        cycle_transport.get(
            "https://npm.pkg.github.com/pkg",
            headers=(("Authorization", f"Bearer {TOKEN}"),),
            timeout=1.0,
            max_bytes=100,
        )

    malformed_transport = adapter.GitHubPackagesHttpTransport(
        opener=missing_location_opener,
        max_redirects=1,
    )
    with pytest.raises(adapter.GitHubPackagesPolicyError, match="location"):
        malformed_transport.get(
            "https://npm.pkg.github.com/pkg",
            headers=(("Authorization", f"Bearer {TOKEN}"),),
            timeout=1.0,
            max_bytes=100,
        )

    with pytest.raises(adapter.GitHubPackagesPolicyError, match="outside"):
        malformed_transport.get(
            "https://example.invalid/pkg",
            headers=(("Authorization", f"Bearer {TOKEN}"),),
            timeout=1.0,
            max_bytes=100,
        )


def test_historical_acceptance_remains_distinct_from_normal_live() -> None:
    adapter = _adapter()
    fixture = (
        Path(__file__).parents[1]
        / "fixtures/acceptance/retry-4-http-200-no-proof-governance.json"
    )
    historical = admit_governance_acceptance_evidence(fixture.read_bytes())
    scenario = historical.probe_facts[0].scenarios[0]

    assert (
        adapter.GITHUB_PACKAGES_OPERATION
        == "conditional-create-npm-version-and-target-tag"
    )
    assert scenario["action"] == {
        "operation": "npm-publish-create-only",
        "executed": True,
        "mutation-started": True,
    }
    assert scenario["action"]["operation"] != adapter.GITHUB_PACKAGES_OPERATION
    assert historical.to_document()["release-lineage"] == "none"


def test_conditional_action_keys_remain_exact_and_conservatively_grouped() -> (
    None
):
    attempt = _attempt()
    first = _projection()
    second = _projection(
        version="1.2.4-beta.1",
        package="@HCOONA/Hcoona-Release-Smoke-Npm",
    )

    first_keys = publication_mutable_resource_keys(first, attempt)
    second_keys = publication_mutable_resource_keys(second, attempt)

    assert first.operation == "conditional-create-npm-version-and-target-tag"
    assert len(first_keys) == len(second_keys) == COMPLETE_KEY_COUNT
    assert first_keys != second_keys
    assert first_keys[0] != second_keys[0]
    assert first_keys[1] == second_keys[1]
    assert publication_serialization_projection(
        first
    ) == publication_serialization_projection(second)
