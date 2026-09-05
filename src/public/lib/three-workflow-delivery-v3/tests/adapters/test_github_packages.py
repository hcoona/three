"""Test-first contracts for the commit-8 GitHub Packages Adapter."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import importlib
from pathlib import Path
from typing import Any

import pytest
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


class RecordingPublishRunner:
    """Strict fake that captures private config while it exists."""

    def __init__(
        self,
        *,
        exit_code: int = 0,
        stdout: str = "",
        exception: BaseException | None = None,
    ) -> None:
        """Configure one deterministic command outcome."""
        self.exit_code = exit_code
        self.stdout = stdout
        self.exception = exception
        self.calls: list[tuple[tuple[str, ...], dict[str, str]]] = []
        self.argv: tuple[str, ...] = ()
        self.config_path: Path | None = None
        self.config_mode: int | None = None
        self.config_text: str | None = None

    def run(
        self,
        argv: tuple[str, ...],
        *,
        env: dict[str, str],
    ) -> Any:
        """Capture command/config facts and return the scripted outcome."""
        self.argv = argv
        self.calls.append((argv, dict(env)))
        config_index = argv.index("--userconfig") + 1
        self.config_path = Path(argv[config_index])
        self.config_mode = self.config_path.stat().st_mode & 0o777
        self.config_text = self.config_path.read_text(encoding="utf-8")
        if self.exception is not None:
            raise self.exception
        return {
            "exit_code": self.exit_code,
            "stdout": self.stdout,
            "stderr": "",
        }


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
    assert adapter.npm_exact_metadata_url(
        "@hcoona/hcoona-release-smoke-npm", VERSION
    ) == (
        "https://npm.pkg.github.com/"
        "@hcoona%2Fhcoona-release-smoke-npm/"
        "1.2.3-beta.42.ge123456"
    )
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


def test_github_packages_rejects_wrong_basis_before_transport() -> None:
    adapter = _adapter()
    transport = RecordingTransport({})

    with pytest.raises(ValueError, match="basis"):
        adapter.observe_github_packages_projection(
            snapshot=object(),
            decision=object(),
            artifact=object(),
            expectation=object(),
            token=TOKEN,
            transport=transport,
        )

    assert transport.requests == []


@pytest.mark.parametrize(
    (
        "rest_state",
        "npm_state",
        "remote_sha512",
        "remote_witness",
        "tag_version",
        "classification",
    ),
    [
        ("absent", "absent", None, None, None, "absent"),
        (
            "present",
            "present",
            "sha512:" + ("6" * 128),
            "sha256:" + ("7" * 64),
            VERSION,
            "exact-satisfied",
        ),
        (
            "present",
            "present",
            "sha512:" + ("6" * 128),
            "sha256:" + ("7" * 64),
            None,
            "partial",
        ),
        (
            "present",
            "present",
            "sha512:" + ("8" * 128),
            "sha256:" + ("7" * 64),
            VERSION,
            "conflicting",
        ),
        ("denied", "unknown", None, None, None, "unknown"),
        ("present", "present", None, None, VERSION, "unprovable"),
    ],
)
def test_github_packages_classifies_all_six_closed_states(  # noqa: PLR0913, PLR0917
    rest_state: str,
    npm_state: str,
    remote_sha512: str | None,
    remote_witness: str | None,
    tag_version: str | None,
    classification: str,
) -> None:
    adapter = _adapter()
    observation = adapter.classify_github_packages_probe(
        coordinate=_coordinate(),
        target=TARGET,
        rest_state=rest_state,
        npm_state=npm_state,
        local_sha512="sha512:" + ("6" * 128),
        remote_sha512=remote_sha512,
        local_witness="sha256:" + ("7" * 64),
        remote_witness=remote_witness,
        tag_version=tag_version,
    )

    assert observation.value.classification == classification
    assert TOKEN not in repr(observation.to_document())


def test_github_packages_exact_requires_tar_witness_and_target_tag() -> None:
    adapter = _adapter()
    common = {
        "coordinate": _coordinate(),
        "target": TARGET,
        "rest_state": "present",
        "npm_state": "present",
        "local_sha512": "sha512:" + ("6" * 128),
        "local_witness": "sha256:" + ("7" * 64),
    }

    exact = adapter.classify_github_packages_probe(
        **common,
        remote_sha512=common["local_sha512"],
        remote_witness=common["local_witness"],
        tag_version=VERSION,
    )
    byte_conflict = adapter.classify_github_packages_probe(
        **common,
        remote_sha512="sha512:" + ("8" * 128),
        remote_witness=common["local_witness"],
        tag_version=VERSION,
    )
    witness_conflict = adapter.classify_github_packages_probe(
        **common,
        remote_sha512=common["local_sha512"],
        remote_witness="sha256:" + ("9" * 64),
        tag_version=VERSION,
    )
    tag_conflict = adapter.classify_github_packages_probe(
        **common,
        remote_sha512=common["local_sha512"],
        remote_witness=common["local_witness"],
        tag_version="9.9.9",
    )

    assert exact.value.classification == "exact-satisfied"
    assert exact.value.content_sha512 == common["local_sha512"]
    assert exact.value.witness_digest == common["local_witness"]
    assert exact.value.routing == (("buddy-sha-" + TARGET, VERSION),)
    assert byte_conflict.value.classification == "conflicting"
    assert witness_conflict.value.classification == "conflicting"
    assert tag_conflict.value.classification == "conflicting"


def test_github_packages_rest_npm_inconsistency_is_blocking() -> None:
    adapter = _adapter()

    assert (
        adapter.classify_rest_npm_consistency(
            rest_version=VERSION,
            npm_version="9.9.9",
            tag_version=VERSION,
        )
        == "conflicting"
    )
    assert (
        adapter.classify_rest_npm_consistency(
            rest_version=VERSION,
            npm_version=VERSION,
            tag_version=None,
        )
        == "partial"
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


def test_github_packages_versions_link_next_is_authoritative() -> None:
    adapter = _adapter()
    requested = (
        "https://api.github.com/users/hcoona/packages/npm/"
        "hcoona-release-smoke-npm/versions?per_page=100&page=1"
    )
    next_url = requested.replace("page=1", "page=2")
    short_page_with_next = adapter.GitHubPackagesHttpResponse(
        200,
        requested,
        (("Link", f'<{next_url}>; rel="next"'),),
        b"[]",
    )
    exhausted = adapter.GitHubPackagesHttpResponse(200, requested, (), b"[]")
    off_origin = adapter.GitHubPackagesHttpResponse(
        200,
        requested,
        (("Link", '<https://example.invalid/page>; rel="next"'),),
        b"[]",
    )

    assert (
        adapter._github_link_next_url(  # noqa: SLF001
            short_page_with_next,
            requested_url=requested,
        )
        == next_url
    )
    assert (
        adapter._github_link_next_url(exhausted, requested_url=requested)  # noqa: SLF001
        is None
    )
    with pytest.raises(adapter.GitHubPackagesPolicyError, match="origin"):
        adapter._github_link_next_url(off_origin, requested_url=requested)  # noqa: SLF001


@pytest.mark.parametrize(
    ("version_url", "expected_owner"),
    [
        (
            (
                "https://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42"
            ),
            "hcoona",
        ),
        (
            (
                "https://api.github.com/orgs/example/packages/npm/"
                "hcoona-release-smoke-npm/versions/42"
            ),
            "example",
        ),
    ],
)
def test_package_version_owner_comes_from_the_api_resource_url(
    version_url: str,
    expected_owner: str,
) -> None:
    adapter = _adapter()
    document = {
        "url": version_url,
        "package_html_url": (
            "https://github.com/users/not-authoritative/packages/npm/"
            "package/hcoona-release-smoke-npm"
        ),
    }

    assert adapter._rest_owner(document) == expected_owner  # noqa: SLF001


@pytest.mark.parametrize(
    "document",
    [
        {},
        {
            "package_html_url": (
                "https://github.com/users/hcoona/packages/npm/"
                "package/hcoona-release-smoke-npm"
            )
        },
        {
            "url": (
                "http://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42"
            )
        },
        {
            "url": (
                "https://api.github.com.example/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages/npm/"
                "another-package/versions/42"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/not-an-id"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42?unexpected=true"
            )
        },
        {
            "url": (
                "https://[api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42;ignored"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages//npm/"
                "hcoona-release-smoke-npm/versions/42"
            )
        },
        {
            "url": (
                "https://api.github.com/users/hcoona/packages/npm/"
                "hcoona-release-smoke-npm/versions/42/"
            )
        },
    ],
)
def test_package_version_owner_rejects_noncanonical_api_routes(
    document: dict[str, str],
) -> None:
    assert _adapter()._rest_owner(document) == "ambiguous"  # noqa: SLF001


def test_replacement_adapter_contract_api_is_available() -> None:
    adapter = _adapter()
    expected_api = (
        "GITHUB_PACKAGES_OPERATION",
        "GITHUB_PACKAGES_DESTINATION_OPERATION_PROFILE_ID",
        "GitHubPackagesHttpResponse",
        "GitHubPackagesTransport",
        "UnsupportedPublicationPrimitiveError",
        "classify_github_packages_probe",
        "github_api_headers",
        "github_packages_destination_operation_profile",
        "github_package_versions_url",
        "npm_exact_metadata_url",
        "observe_github_packages_projection",
        "preflight_github_packages_action",
        "publish_github_packages_action",
        "redact_diagnostic",
        "redirect_headers",
        "validate_observation_bounds",
        "validate_github_packages_publication_action",
    )
    missing = tuple(name for name in expected_api if not hasattr(adapter, name))

    assert missing == (), f"missing replacement adapter API: {missing}"
    assert not hasattr(adapter, "AuthorizationRecord")
    assert not hasattr(adapter, "CapabilityAdmissionDecision")


def test_normal_live_and_acceptance_operations_remain_distinct() -> None:
    adapter = _adapter()
    scenario = adapter.FixedCoordinateAcceptanceProbeResult(
        scenario="absent-create-readback",
        package_coordinate=adapter.ACCEPTANCE_COORDINATES[
            "absent-create-readback"
        ],
        tag="wdv3-acceptance-1",
        pre_state="absent",
        post_state="exact",
        result="created",
        mutation_classification="complete",
        action_executed=True,
        mutation_started=True,
        response_identity_digest=RESPONSE_DIGEST,
        content_sha512="sha512:" + ("6" * 128),
        diagnostics=(),
    )
    acceptance_document = adapter.FixedAcceptanceSuiteResult(
        suite="absent-create-readback",
        scenarios=(scenario,),
    ).to_document()

    assert (
        adapter.GITHUB_PACKAGES_OPERATION
        == "conditional-create-npm-version-and-target-tag"
    )
    assert acceptance_document["scenarios"][0]["action"] == {
        "operation": "npm-publish-create-only",
        "executed": True,
        "mutation-started": True,
    }
    assert (
        acceptance_document["scenarios"][0]["action"]["operation"]
        != adapter.GITHUB_PACKAGES_OPERATION
    )


def test_publish_fails_before_every_mutation_capable_seam(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    adapter = _adapter()
    runner = RecordingPublishRunner(exit_code=0, stdout="created")
    transport = RecordingTransport({})
    forbidden_calls: list[str] = []
    unavailable = object()
    tarball = tmp_path / "must-not-be-read.tgz"
    temp_root = tmp_path / "must-not-be-created"

    def reject_forbidden_call(*_args: object, **_kwargs: object) -> None:
        forbidden_calls.append("called")
        message = "mutation-capable publisher seam must not run"
        raise AssertionError(message)

    for helper in (
        "_admit_mutation_marker",
        "_npm_configuration_digest",
        "_runner_result",
        "_validate_local_tarball_preconditions",
        "_validate_publish_preconditions",
        "_write_private_npm_config",
        "observe_github_packages_projection",
        "preflight_github_packages_action",
    ):
        monkeypatch.setattr(adapter, helper, reject_forbidden_call)

    with pytest.raises(
        adapter.UnsupportedPublicationPrimitiveError,
    ) as raised:
        adapter.publish_github_packages_action(
            tarball=tarball,
            target=TARGET,
            token=TOKEN,
            runner=runner,
            temp_root=temp_root,
            transport=transport,
            publication_snapshot=unavailable,
            authorization=unavailable,
            action=unavailable,
            qualification_snapshot=unavailable,
            qualification_decision=unavailable,
            artifact=unavailable,
            expectation=unavailable,
            preflight=unavailable,
            mutation_marker=unavailable,
            governance_source=unavailable,
            governance_client=unavailable,
            governance_observed_at=unavailable,
        )

    assert str(raised.value) == (
        "The conditional GitHub Packages version-and-tag primitive is not "
        "implemented; normal Live remains activation-blocked"
    )
    assert forbidden_calls == []
    assert runner.calls == []
    assert runner.config_path is None
    assert transport.requests == []
    assert not tarball.exists()
    assert not temp_root.exists()


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
