"""NuGet native admission and authenticated operator collection of facts.

The shared Governance and current-Attempt mechanisms remain authoritative.
These GET-only reads retain existing PR review carriers; they do not invent a
review verdict, independent human security review, or exhaustive grant audit.
"""

from __future__ import annotations

# ruff: noqa: SLF001
from dataclasses import dataclass, field
from datetime import timedelta
from http import HTTPStatus
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.nuget_github_packages import (
    _validate_response_body,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_json_strict,
)
from three_workflow_delivery_v3.release import eligibility as shared
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    NUGET_GOVERNANCE_PATH,
    NUGET_PACKAGE,
    NUGET_RELEASE_UNIT,
)

if TYPE_CHECKING:
    from datetime import datetime

    from three_workflow_delivery_v3.adapters.nuget_github_packages import (
        NuGetHttpResponse,
        NuGetReadTransport,
    )
    from three_workflow_delivery_v3.canonical import JsonValue

# No generation or service-owned atomic assurance has been admitted. Native
# sequential success, HTTP 409 and npm's active-lifetime acceptance are not
# substitutes. Protected delivery may install only independently audited exact
# identities after the separate GitHub-specific atomic assurance gate closes.
_ADMITTED_NUGET_NATIVE_GENERATIONS: frozenset[
    tuple[str, str, str, str, str, str]
] = frozenset()
_ADMITTED_NUGET_ATOMIC_CONTRACTS: frozenset[str] = frozenset()
_NATIVE_SUITE = "workflow-delivery-v3/native-nuget-suite/v1"
_API = "https://api.github.com"
_REPO = "/repos/hcoona/three"
_ENVIRONMENT = "workflow-delivery-v3-buddy-approval"
_ACTOR_ID = 712433
_REPOSITORY_ID = 1102295886
_PACKAGE_ID = 12024661
_PAGE_SIZE = 100
_MAX_PAGES = 20
_FRESHNESS = timedelta(minutes=5)


def nuget_destination_primitive_is_admitted(
    attestation: shared.GovernanceAttestationState,
) -> bool:
    """Require distinct exact NuGet native evidence and atomic assurance."""
    activation = attestation.activation
    if (
        attestation.release_policy != NUGET_RELEASE_UNIT
        or attestation.package != NUGET_PACKAGE
        or not isinstance(activation, shared.EnabledGovernanceActivation)
    ):
        return False
    primitive = activation.destination_primitive
    return (
        primitive.native_acceptance_suite_version == _NATIVE_SUITE
        and primitive.disposable_package_preconditions.package == NUGET_PACKAGE
        and (*primitive.admission_key, primitive.evidence_digest)
        in _ADMITTED_NUGET_NATIVE_GENERATIONS
        and primitive.lower_layer_contract_revision
        in _ADMITTED_NUGET_ATOMIC_CONTRACTS
    )


@dataclass(frozen=True)
class NuGetPlatformFacts:
    """Current native facts and raw existing review carriers, not authority."""

    target: str
    control_sha: str
    workflow_sha: str
    workflow_run_id: int
    current_main_sha: str
    reviewed_head_sha: str
    reviewed_tree_sha: str
    pull_request_number: int
    observed_at: datetime
    approval_environment: shared.ApprovalEnvironmentAttestation
    artifact_retention: shared.ArtifactRetentionAttestation
    package_control: dict[str, JsonValue]
    writer_inventory: tuple[str, ...]
    review_carriers: tuple[NuGetHttpResponse, ...] = field(repr=False)
    exchanges: tuple[NuGetHttpResponse, ...] = field(repr=False)

    @property
    def readback_digest(self) -> str:
        """Bind actual response bytes and URLs without token material."""
        return _readback_digest(self)


@dataclass(frozen=True)
class NuGetLiveControlFacts:
    """Actual Live control reads, with no operator administration projection."""

    target: str
    control_sha: str
    workflow_sha: str
    workflow_run_id: int
    current_main_sha: str
    reviewed_head_sha: str
    reviewed_tree_sha: str
    pull_request_number: int
    observed_at: datetime
    review_carriers: tuple[NuGetHttpResponse, ...] = field(repr=False)
    exchanges: tuple[NuGetHttpResponse, ...] = field(repr=False)

    @property
    def readback_digest(self) -> str:
        """Bind actual response bytes and URLs without token material."""
        return _readback_digest(self)


def _readback_digest(facts: NuGetPlatformFacts | NuGetLiveControlFacts) -> str:
    """Bind actual response bytes and URLs, without token material."""
    import hashlib  # noqa: PLC0415

    return canonical_sha256(
        {
            "target": facts.target,
            "control_sha": facts.control_sha,
            "workflow_sha": facts.workflow_sha,
            "workflow_run_id": facts.workflow_run_id,
            "observed_at": shared._format_instant(facts.observed_at),
            "responses": [
                {
                    "url": response.url,
                    "status": response.status,
                    "body_sha256": hashlib.sha256(response.body).hexdigest(),
                }
                for response in facts.exchanges
            ],
        }
    )


class _Reader:
    """Bounded authenticated GETs, without redirects or hidden pagination."""

    def __init__(
        self, transport: NuGetReadTransport, token: str, now: datetime
    ) -> None:
        if not token or not token.isascii() or not token.isprintable():
            message = "NuGet platform observation requires an opaque token"
            raise shared.GovernanceRejectionError(message)
        self.transport = transport
        self.token = token
        self.now = now
        self.exchanges: list[NuGetHttpResponse] = []

    def get(self, path: str) -> JsonValue:
        url = _API + path
        response = self.transport.get(
            url,
            headers=(
                ("Authorization", "Bearer " + self.token),
                ("Accept", "application/vnd.github+json"),
                ("X-GitHub-Api-Version", "2022-11-28"),
                ("Accept-Encoding", "identity"),
                ("Connection", "close"),
                ("User-Agent", "workflow-delivery-v3-nuget-governance"),
            ),
            timeout=60,
            max_bytes=8 * 1024 * 1024,
        )
        if response.url != url or response.status != HTTPStatus.OK:
            message = "NuGet platform readback was incomplete or redirected"
            raise shared.GovernanceRejectionError(message)
        _validate_response_body(response, max_bytes=8 * 1024 * 1024)
        self.exchanges.append(response)
        return parse_json_strict(response.body)

    def obj(self, path: str) -> dict[str, JsonValue]:
        return shared._object(self.get(path), context="NuGet platform response")

    def pages(
        self, path: str, *, field_name: str | None = None
    ) -> list[JsonValue]:
        items: list[JsonValue] = []
        expected_total: int | None = None
        for page in range(1, _MAX_PAGES + 1):
            separator = "&" if "?" in path else "?"
            value = self.get(
                f"{path}{separator}per_page={_PAGE_SIZE}&page={page}"
            )
            if field_name is not None:
                document = shared._object(value, context="paginated response")
                total = shared._nonnegative_integer(
                    document.get("total_count"), context="total_count"
                )
                if expected_total is not None and total != expected_total:
                    message = "Platform inventory changed during pagination"
                    raise shared.GovernanceRejectionError(message)
                expected_total = total
                value = document.get(field_name)
            page_items = shared._array(value, context="paginated response")
            if len(page_items) > _PAGE_SIZE:
                message = "Platform page exceeds its bound"
                raise shared.GovernanceRejectionError(message)
            items.extend(page_items)
            if len(page_items) < _PAGE_SIZE:
                if expected_total is not None and len(items) != expected_total:
                    message = "Platform inventory is incomplete"
                    raise shared.GovernanceRejectionError(message)
                return items
        message = "Platform inventory exceeds its page bound"
        raise shared.GovernanceRejectionError(message)

    def evidence(self, path: str) -> shared.NativeEvidence:
        import hashlib  # noqa: PLC0415

        responses = [item for item in self.exchanges if item.url == _API + path]
        if len(responses) != 1:
            message = "Expected one actual platform readback"
            raise shared.GovernanceRejectionError(message)
        return shared.NativeEvidence(
            endpoint="GET " + path,
            captured_at=self.now,
            response_digest="sha256:"
            + hashlib.sha256(responses[0].body).hexdigest(),
        )


def _actor(value: JsonValue) -> bool:
    return (
        isinstance(value, dict)
        and value.get("login") == "hcoona"
        and value.get("id") == _ACTOR_ID
        and value.get("type", "User") == "User"
    )


def _object_id(value: JsonValue) -> str:
    return shared._decision_sha(value, field="NuGet platform commit")


def _environment(
    reader: _Reader, repository: dict[str, JsonValue]
) -> shared.ApprovalEnvironmentAttestation:
    environment_path = f"{_REPO}/environments/{_ENVIRONMENT}"
    environment = reader.obj(environment_path)
    rules = shared._array(
        environment.get("protection_rules"), context="Environment rules"
    )
    reviewer_rules = [
        rule
        for rule in rules
        if isinstance(rule, dict) and rule.get("type") == "required_reviewers"
    ]
    if len(reviewer_rules) != 1:
        message = "The Approval Environment lacks its exact reviewer rule"
        raise shared.GovernanceRejectionError(message)
    rule = reviewer_rules[0]
    reviewers = shared._array(
        rule.get("reviewers"), context="Environment reviewers"
    )
    if (
        len(reviewers) != 1
        or not isinstance(reviewers[0], dict)
        or reviewers[0].get("type") != "User"
        or not _actor(reviewers[0].get("reviewer"))
    ):
        message = "The Approval Environment reviewer changed"
        raise shared.GovernanceRejectionError(message)
    if any(
        not isinstance(rule, dict)
        or rule.get("type") not in ("required_reviewers", "wait_timer")
        for rule in rules
    ):
        message = "The Approval Environment contains an unknown protection rule"
        raise shared.GovernanceRejectionError(message)
    waits = [
        rule.get("wait_timer")
        for rule in rules
        if isinstance(rule, dict) and rule.get("type") == "wait_timer"
    ]
    if waits not in ([], [0]):
        message = "The Approval Environment wait timer changed"
        raise shared.GovernanceRejectionError(message)
    if environment.get("deployment_branch_policy") is not None:
        message = "The Approval Environment deployment policy changed"
        raise shared.GovernanceRejectionError(message)
    variable_path = environment_path + "/variables"
    variables = reader.pages(variable_path, field_name="variables")
    normalized_variables: list[JsonValue] = []
    for item in variables:
        variable = shared._object(item, context="Environment variable")
        normalized_variables.append(
            {
                "name": variable.get("name"),
                "value": variable.get("value"),
                "scope": "environment",
            }
        )
    secrets_path = (
        f"/repositories/{_REPOSITORY_ID}/environments/{_ENVIRONMENT}/secrets"
    )
    secrets = reader.pages(secrets_path, field_name="secrets")
    repository_variables_path = _REPO + "/actions/variables"
    repository_variables = reader.pages(
        repository_variables_path, field_name="variables"
    )
    if not _actor(repository.get("owner")):
        message = "Repository owner is not the accepted user"
        raise shared.GovernanceRejectionError(message)
    absent = all(
        isinstance(item, dict)
        and item.get("name") != "WDV3_APPROVAL_ENVIRONMENT_MARKER"
        for item in repository_variables
    )
    related = (
        _REPO,
        environment_path,
        variable_path,
        secrets_path,
        repository_variables_path,
    )
    evidence = tuple(
        shared.NativeEvidence(
            "GET " + response.url.removeprefix(_API),
            reader.now,
            canonical_sha256(parse_json_strict(response.body)),
        )
        for response in reader.exchanges
        if any(
            response.url == _API + path
            or response.url.startswith(_API + path + "?")
            for path in related
        )
    )
    return shared._approval_environment(
        {
            "name": environment.get("name"),
            "environment_id": environment.get("id"),
            "required_reviewers": [{"login": "hcoona", "id": _ACTOR_ID}],
            "prevent_self_review": rule.get("prevent_self_review"),
            "can_admins_bypass": environment.get("can_admins_bypass"),
            "wait_timer_minutes": 0,
            "deployment_policy": "all",
            "secret_count": len(secrets),
            "variables": normalized_variables,
            "same_name_repository_variable_absent": absent,
            "same_name_organization_variable": "not-applicable-user-owner",
            "evidence": [
                item.to_document()
                for item in sorted(evidence, key=lambda item: item.endpoint)
            ],
        }
    )


def _writers(reader: _Reader) -> tuple[str, ...]:
    collaborators = reader.pages(_REPO + "/collaborators?affiliation=all")
    writers: list[str] = []
    for item in collaborators:
        collaborator = shared._object(item, context="repository collaborator")
        permissions = shared._object(
            collaborator.get("permissions"), context="collaborator permissions"
        )
        if any(
            permissions.get(name) is True
            for name in ("push", "maintain", "admin")
        ):
            if not _actor(collaborator) or permissions.get("admin") is not True:
                message = "The accepted repository writer inventory changed"
                raise shared.GovernanceRejectionError(message)
            writers.append("hcoona")
    if (
        writers != ["hcoona"]
        or reader.pages(_REPO + "/teams")
        or reader.pages(_REPO + "/invitations")
    ):
        message = (
            "Repository writer/team/invitation access "
            "differs from the accepted boundary"
        )
        raise shared.GovernanceRejectionError(message)
    return tuple(writers)


def _package(reader: _Reader) -> dict[str, JsonValue]:
    document = reader.obj(f"/users/hcoona/packages/nuget/{NUGET_PACKAGE}")
    repository = document.get("repository")
    owner = document.get("owner")
    if (
        document.get("id") != _PACKAGE_ID
        or document.get("name") != NUGET_PACKAGE
        or document.get("package_type") != "nuget"
        or document.get("visibility") != "public"
        or not isinstance(repository, dict)
        or repository.get("full_name") != GOVERNANCE_REPOSITORY
        or repository.get("id") != _REPOSITORY_ID
        or (owner is not None and not _actor(owner))
    ):
        message = (
            "NuGet package-control readback differs from its accepted container"
        )
        raise shared.GovernanceRejectionError(message)
    return document


def _read_live_control(  # noqa: C901, PLR0913
    reader: _Reader,
    *,
    selected_ref: str,
    target: str,
    control_sha: str,
    workflow_sha: str,
    workflow_run_id: int,
    workflow_path: str,
    now: datetime,
) -> tuple[NuGetLiveControlFacts, dict[str, JsonValue]]:
    shared._validate_instant(now)
    if (
        selected_ref != GOVERNANCE_REF
        or control_sha != target
        or workflow_sha != target
    ):
        message = (
            "NuGet requires protected-main "
            "same-revision workflow/control/target"
        )
        raise shared.GovernanceRejectionError(message)
    _object_id(target)
    shared._positive_integer(workflow_run_id, context="workflow run")
    if not workflow_path.startswith(
        ".github/workflows/"
    ) or not workflow_path.endswith(".yml"):
        message = "NuGet Live workflow path is malformed"
        raise shared.GovernanceRejectionError(message)
    repository = reader.obj(_REPO)
    if (
        repository.get("id") != _REPOSITORY_ID
        or repository.get("full_name") != GOVERNANCE_REPOSITORY
    ):
        message = "NuGet repository identity changed"
        raise shared.GovernanceRejectionError(message)
    branch = reader.obj(_REPO + "/branches/main")
    if branch.get("protected") is not True or branch.get("name") != "main":
        message = "NuGet main is not currently protected"
        raise shared.GovernanceRejectionError(message)
    main = _object_id(
        shared._object(branch.get("commit"), context="main commit").get("sha")
    )
    comparison = reader.obj(f"{_REPO}/compare/{target}...{main}")
    if (
        comparison.get("status") not in ("identical", "ahead")
        or shared._object(
            comparison.get("merge_base_commit"), context="merge base"
        ).get("sha")
        != target
    ):
        message = "NuGet target is not on current protected main"
        raise shared.GovernanceRejectionError(message)
    run = reader.obj(f"{_REPO}/actions/runs/{workflow_run_id}")
    if (
        run.get("id") != workflow_run_id
        or run.get("run_attempt") != 1
        or run.get("event") != "workflow_dispatch"
        or run.get("head_sha") != target
        or run.get("head_branch") != "main"
        or run.get("path") != workflow_path
        or not _actor(run.get("actor"))
        or not _actor(run.get("triggering_actor"))
        or shared._object(run.get("repository"), context="run repository").get(
            "id"
        )
        != _REPOSITORY_ID
    ):
        message = "NuGet actual workflow actor/attempt/revision differs"
        raise shared.GovernanceRejectionError(message)
    associations = reader.pages(f"{_REPO}/commits/{target}/pulls")
    matching = [
        item
        for item in associations
        if isinstance(item, dict)
        and item.get("merge_commit_sha") == target
        and item.get("merged_at") is not None
    ]
    if len(matching) != 1:
        message = "NuGet target lacks one exact merged PR carrier"
        raise shared.GovernanceRejectionError(message)
    number = shared._positive_integer(
        cast("dict[str, JsonValue]", matching[0]).get("number"),
        context="pull request",
    )
    pull = reader.obj(f"{_REPO}/pulls/{number}")
    base = shared._object(pull.get("base"), context="PR base")
    head = shared._object(pull.get("head"), context="PR head")
    if (
        pull.get("merged") is not True
        or pull.get("merge_commit_sha") != target
        or not _actor(pull.get("merged_by"))
        or base.get("ref") != "main"
        or shared._object(base.get("repo"), context="PR base repository").get(
            "id"
        )
        != _REPOSITORY_ID
        or shared._object(head.get("repo"), context="PR head repository").get(
            "id"
        )
        != _REPOSITORY_ID
    ):
        message = "NuGet target PR lacks accepted operator merge provenance"
        raise shared.GovernanceRejectionError(message)
    head_sha = _object_id(head.get("sha"))
    target_commit = reader.obj(f"{_REPO}/git/commits/{target}")
    head_commit = reader.obj(f"{_REPO}/git/commits/{head_sha}")
    target_tree = _object_id(
        shared._object(target_commit.get("tree"), context="target tree").get(
            "sha"
        )
    )
    if (
        head_commit.get("sha") != head_sha
        or target_commit.get("sha") != target
        or shared._object(head_commit.get("tree"), context="reviewed tree").get(
            "sha"
        )
        != target_tree
    ):
        message = "NuGet merged target differs from its reviewed PR tree"
        raise shared.GovernanceRejectionError(message)
    review_paths = (
        f"{_REPO}/pulls/{number}/reviews",
        f"{_REPO}/pulls/{number}/comments",
        f"{_REPO}/issues/{number}/comments",
        f"{_REPO}/commits/{head_sha}/check-runs",
    )
    for path in review_paths:
        reader.pages(
            path,
            field_name="check_runs" if path.endswith("check-runs") else None,
        )
    review_carriers = tuple(
        response
        for response in reader.exchanges
        if response.url == _API + f"{_REPO}/pulls/{number}"
        or any(
            response.url.startswith(_API + path + "?") for path in review_paths
        )
    )
    return NuGetLiveControlFacts(
        target,
        control_sha,
        workflow_sha,
        workflow_run_id,
        main,
        head_sha,
        target_tree,
        number,
        now,
        review_carriers,
        tuple(reader.exchanges),
    ), repository


def read_nuget_live_control_facts(  # noqa: PLR0913
    *,
    transport: NuGetReadTransport,
    token: str,
    selected_ref: str,
    target: str,
    control_sha: str,
    workflow_sha: str,
    workflow_run_id: int,
    workflow_path: str,
    now: datetime,
) -> NuGetLiveControlFacts:
    """Read current control and raw review carriers without admin endpoints.

    Equal reviewed/merged trees and the accepted owner's merge provide control
    provenance. Raw reviews and comments remain evidence, not a review verdict.
    """
    facts, _repository = _read_live_control(
        _Reader(transport, token, now),
        selected_ref=selected_ref,
        target=target,
        control_sha=control_sha,
        workflow_sha=workflow_sha,
        workflow_run_id=workflow_run_id,
        workflow_path=workflow_path,
        now=now,
    )
    return facts


def read_nuget_platform_facts(  # noqa: PLR0913
    *,
    transport: NuGetReadTransport,
    token: str,
    selected_ref: str,
    target: str,
    control_sha: str,
    workflow_sha: str,
    workflow_run_id: int,
    workflow_path: str,
    now: datetime,
) -> NuGetPlatformFacts:
    """Collect operator platform facts using actual authenticated GETs.

    This comprehensive collection includes administrative metadata endpoints
    that normal Actions tokens may not support. It is not installed runtime
    freshness and never authorizes a PAT/admin fallback in a Live job.

    Owner merge with equal reviewed/target trees is operator/delegated
    acceptance. PR body, reviews, comments and checks are retained verbatim for
    the existing review gate; this reader never interprets COMMENTED as formal
    APPROVED or claims it established an independent review verdict.
    """
    reader = _Reader(transport, token, now)
    control, repository = _read_live_control(
        reader,
        selected_ref=selected_ref,
        target=target,
        control_sha=control_sha,
        workflow_sha=workflow_sha,
        workflow_run_id=workflow_run_id,
        workflow_path=workflow_path,
        now=now,
    )
    writers = _writers(reader)
    environment = _environment(reader, repository)
    retention_path = _REPO + "/actions/permissions/artifact-and-log-retention"
    retention = reader.obj(retention_path)
    retention_evidence = reader.evidence(retention_path)
    retention_fact = shared._artifact_retention(
        {
            **retention_evidence.to_document(),
            "days": retention.get("days"),
        }
    )
    package = _package(reader)
    return NuGetPlatformFacts(
        target,
        control_sha,
        workflow_sha,
        workflow_run_id,
        control.current_main_sha,
        control.reviewed_head_sha,
        control.reviewed_tree_sha,
        control.pull_request_number,
        now,
        environment,
        retention_fact,
        package,
        writers,
        control.review_carriers,
        tuple(reader.exchanges),
    )


def require_nuget_live_platform(
    governance: shared.GovernanceObservation,
    facts: NuGetPlatformFacts | NuGetLiveControlFacts,
    *,
    target: str,
    workflow_run_id: int,
    now: datetime,
) -> None:
    """Bind fresh platform readbacks to NuGet Governance and this run.

    This structural gate supplements the existing PR review-carrier admission
    and require_action_governance; it supplies neither review nor native
    publication authority by itself.
    """
    shared._validate_instant(now)
    if (
        type(governance) is not shared.GovernanceObservation
        or governance.source.path != NUGET_GOVERNANCE_PATH
        or governance.attestation.release_policy != NUGET_RELEASE_UNIT
        or type(facts) not in (NuGetPlatformFacts, NuGetLiveControlFacts)
        or facts.target != target
        or facts.control_sha != target
        or facts.workflow_sha != target
        or facts.workflow_run_id != workflow_run_id
        or not timedelta(0) <= now - facts.observed_at <= _FRESHNESS
    ):
        message = (
            "NuGet platform facts are stale "
            "or belong to another authority context"
        )
        raise shared.GovernanceRejectionError(message)
