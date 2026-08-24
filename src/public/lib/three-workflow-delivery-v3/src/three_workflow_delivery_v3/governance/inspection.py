"""Optional bounded read-only Governance reviewer inspection."""

# ruff: noqa: EM101, TRY003

from __future__ import annotations

import json
import subprocess
from dataclasses import dataclass
from time import monotonic
from typing import TYPE_CHECKING, Protocol, cast

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

_PRESENT_STATUS = "present"
_REMOVED_STATUS = "removed"
_UNKNOWN_STATUS = "unknown"
_MAX_REVIEW_PAGES = 100


class ReadOnlyGitHubCliRunner(Protocol):
    """Injected seam for bounded read-only GitHub CLI requests."""

    def run(
        self,
        argv: tuple[str, ...],
        *,
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, object]:
        """Run one read-only command and return its parsed response."""


@dataclass(frozen=True, slots=True)
class AcceptanceReviewerRecovery:
    """Single acceptance review recovery coordinates."""

    repository: str
    workflow_run_id: int
    environment: str
    deployment: str
    job: str
    artifact_id: int

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed recovery document."""
        return {
            "repository": self.repository,
            "workflow-run-id": self.workflow_run_id,
            "environment": self.environment,
            "deployment": self.deployment,
            "job": self.job,
            "artifact-id": self.artifact_id,
        }


@dataclass(frozen=True, slots=True)
class AcceptanceReviewerInspection:
    """Diagnostic-only acceptance reviewer inspection result."""

    recovery: AcceptanceReviewerRecovery
    status: str
    reviewer: str | None
    deployment_review_id: int | None
    diagnostics: tuple[str, ...] = ()

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed diagnostic-only inspection document."""
        document: dict[str, JsonValue] = {
            "authority": "diagnostic-only",
            "scope": "single-acceptance-review-recovery",
            "recovery": self.recovery.to_document(),
            "status": self.status,
            "reviewer": self.reviewer,
            "deployment-review-id": self.deployment_review_id,
            "human-required": self.status != _PRESENT_STATUS,
        }
        if self.diagnostics:
            document["diagnostics"] = cast(
                "list[JsonValue]",
                list(self.diagnostics),
            )
        return document


class SubprocessReadOnlyGitHubCliRunner:
    """Bounded process runner for the optional read-only ``gh api GET``."""

    def run(
        self,
        argv: tuple[str, ...],
        *,
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, object]:
        """Run one bounded command and parse a JSON object response."""
        completed = subprocess.run(  # noqa: S603
            argv,
            check=False,
            capture_output=True,
            text=False,
            timeout=timeout_seconds,
        )
        if completed.returncode != 0:
            message = "read-only reviewer lookup failed"
            raise OSError(message)
        if len(completed.stdout) > max_output_bytes:
            message = "read-only reviewer lookup response exceeded the limit"
            raise OSError(message)
        payload = completed.stdout
        parsed = json.loads(payload.decode("utf-8"))
        if not isinstance(parsed, dict):
            message = "read-only reviewer lookup response must be an object"
            raise TypeError(message)
        return cast("dict[str, object]", parsed)


def _positive(value: int, *, field: str) -> int:
    if type(value) is not int or value <= 0:
        message = f"{field} must be a positive integer"
        raise ValueError(message)
    return value


def _string(value: str, *, field: str) -> str:
    if type(value) is not str or not value or value != value.strip():
        message = f"{field} must be a nonempty exact string"
        raise ValueError(message)
    return value


def _workflow_run_node_id(response: dict[str, object]) -> str:
    node_id = response.get("node_id")
    if type(node_id) is not str or not node_id:
        message = "workflow run REST response is missing node_id"
        raise ValueError(message)
    return node_id


def _deployment_reviews_page(  # noqa: C901, PLR0912, PLR0915
    response: dict[str, object],
    *,
    environment: str,
) -> tuple[
    tuple[str, int] | None,
    bool,
    str | None,
    tuple[tuple[str, str, int, str], ...],
]:
    data = response.get("data")
    if type(data) is not dict:
        raise ValueError("deploymentReviews response data is malformed")
    node = data.get("node")
    if type(node) is not dict:
        raise ValueError("deploymentReviews WorkflowRun node is malformed")
    connection = node.get("deploymentReviews")
    if type(connection) is not dict:
        raise ValueError("deploymentReviews connection is malformed")
    edges = connection.get("edges")
    page_info = connection.get("pageInfo")
    if type(edges) is not list or type(page_info) is not dict:
        raise ValueError("deploymentReviews page is malformed")
    pending: list[tuple[str, str, int, str]] = []
    for edge in edges:
        if type(edge) is not dict or type(edge.get("node")) is not dict:
            raise ValueError("deploymentReviews edge is malformed")
        review = edge["node"]
        environments = review.get("environments")
        user = review.get("user")
        if type(user) is not dict:
            raise ValueError("deployment review fields are malformed")
        if type(environments) is list:
            names = [
                item.get("name")
                for item in environments
                if type(item) is dict and type(item.get("name")) is str
            ]
            environment_has_next = False
            environment_cursor = None
        elif type(environments) is dict:
            nodes = environments.get("nodes")
            environment_page_info = environments.get("pageInfo")
            if (
                type(nodes) is not list
                or type(environment_page_info) is not dict
            ):
                raise ValueError("deployment review environments are malformed")
            names = [
                item.get("name")
                for item in nodes
                if type(item) is dict and type(item.get("name")) is str
            ]
            environment_has_next = environment_page_info.get("hasNextPage")
            environment_cursor = environment_page_info.get("endCursor")
            if type(environment_has_next) is not bool:
                raise ValueError(
                    "deployment review environment pageInfo is malformed"
                )
            if environment_has_next and (
                type(environment_cursor) is not str or not environment_cursor
            ):
                raise ValueError(
                    "deployment review environment cursor is malformed"
                )
        else:
            raise ValueError("deployment review environments are malformed")
        if environment not in names:
            if environment_has_next:
                review_node_id = review.get("id")
                reviewer = user.get("login")
                review_id = review.get("databaseId")
                state = review.get("state")
                if (
                    type(review_node_id) is not str
                    or not review_node_id
                    or type(reviewer) is not str
                    or type(review_id) is not int
                    or type(state) is not str
                ):
                    raise ValueError(
                        "deployment review continuation fields are malformed"
                    )
                if state != "APPROVED":
                    continue
                pending.append(
                    (
                        review_node_id,
                        reviewer,
                        review_id,
                        cast("str", environment_cursor),
                    )
                )
            continue
        reviewer = user.get("login")
        review_id = review.get("databaseId")
        state = review.get("state")
        if type(state) is not str:
            raise ValueError("matching deployment review is malformed")
        if state != "APPROVED":
            continue
        if type(reviewer) is not str or type(review_id) is not int:
            raise ValueError("matching deployment review is malformed")
        return (reviewer, review_id), False, None, ()
    has_next_page = page_info.get("hasNextPage")
    end_cursor = page_info.get("endCursor")
    if type(has_next_page) is not bool:
        raise ValueError("deploymentReviews pageInfo is malformed")
    if has_next_page and (type(end_cursor) is not str or not end_cursor):
        raise ValueError("deploymentReviews cursor is malformed")
    return (
        None,
        has_next_page,
        cast("str | None", end_cursor),
        tuple(pending),
    )


def _review_environment_page(
    response: dict[str, object],
    *,
    environment: str,
) -> tuple[bool, bool, str | None]:
    data = response.get("data")
    if type(data) is not dict or type(data.get("node")) is not dict:
        raise ValueError("deployment review node response is malformed")
    environments = data["node"].get("environments")
    if type(environments) is not dict:
        raise ValueError("deployment review environments are malformed")
    nodes = environments.get("nodes")
    page_info = environments.get("pageInfo")
    if type(nodes) is not list or type(page_info) is not dict:
        raise ValueError("deployment review environment page is malformed")
    names = [
        item.get("name")
        for item in nodes
        if type(item) is dict and type(item.get("name")) is str
    ]
    has_next = page_info.get("hasNextPage")
    cursor = page_info.get("endCursor")
    if type(has_next) is not bool:
        raise ValueError("deployment review environment pageInfo is malformed")
    if has_next and (type(cursor) is not str or not cursor):
        raise ValueError("deployment review environment cursor is malformed")
    return environment in names, has_next, cast("str | None", cursor)


def _environment_pagination_limit_error() -> ValueError:
    return ValueError("deployment review environment pagination limit exceeded")


def _remaining(deadline: float) -> float:
    remaining = round(deadline - monotonic(), 3)
    if remaining <= 0:
        raise TimeoutError("reviewer inspection deadline expired")
    return remaining


def inspect_acceptance_reviewer(  # noqa: PLR0913
    *,
    repository: str,
    workflow_run_id: int,
    environment: str,
    deployment: str,
    job: str,
    artifact_id: int,
    runner: ReadOnlyGitHubCliRunner | None = None,
    timeout_seconds: float = 10.0,
    max_output_bytes: int = 8192,
) -> AcceptanceReviewerInspection:
    """Inspect one reviewer through REST identity plus query-only GraphQL."""
    recovery = AcceptanceReviewerRecovery(
        repository=_string(repository, field="repository"),
        workflow_run_id=_positive(workflow_run_id, field="workflow_run_id"),
        environment=_string(environment, field="environment"),
        deployment=_string(deployment, field="deployment"),
        job=_string(job, field="job"),
        artifact_id=_positive(artifact_id, field="artifact_id"),
    )
    read_runner = runner or SubprocessReadOnlyGitHubCliRunner()
    deadline = monotonic() + timeout_seconds
    try:
        run_response = read_runner.run(
            (
                "gh",
                "api",
                "--method",
                "GET",
                f"repos/{recovery.repository}/actions/runs/"
                f"{recovery.workflow_run_id}",
            ),
            timeout_seconds=_remaining(deadline),
            max_output_bytes=max_output_bytes,
        )
        node_id = _workflow_run_node_id(run_response)
        cursor: str | None = None
        for _page in range(_MAX_REVIEW_PAGES):
            command = (
                "gh",
                "api",
                "graphql",
                "--method",
                "POST",
                "-f",
                "query=query($run:ID!,$cursor:String,$environmentCursor:String){node(id:$run){"
                "... on WorkflowRun{deploymentReviews(first:100,after:$cursor){"
                "edges{node{id databaseId state user{login} "
                "environments(first:100,after:$environmentCursor){"
                "nodes{name} pageInfo{hasNextPage endCursor}}}}"
                "pageInfo{hasNextPage endCursor}}}}}",
                "-F",
                f"run={node_id}",
                "-F",
                f"cursor={cursor if cursor is not None else 'null'}",
                "-F",
                "environmentCursor=null",
            )
            response = read_runner.run(
                command,
                timeout_seconds=_remaining(deadline),
                max_output_bytes=max_output_bytes,
            )
            found, has_next_page, cursor, pending_reviews = (
                _deployment_reviews_page(
                    response,
                    environment=recovery.environment,
                )
            )
            if found is not None:
                return AcceptanceReviewerInspection(
                    recovery=recovery,
                    status=_PRESENT_STATUS,
                    reviewer=found[0],
                    deployment_review_id=found[1],
                )
            for (
                review_node_id,
                reviewer,
                review_id,
                environment_cursor,
            ) in pending_reviews:
                nested_cursor = environment_cursor
                for _environment_page in range(_MAX_REVIEW_PAGES):
                    nested_command = (
                        "gh",
                        "api",
                        "graphql",
                        "--method",
                        "POST",
                        "-f",
                        "query=query($review:ID!,$environmentCursor:String){"
                        "node(id:$review){... on DeploymentReview{"
                        "environments(first:100,after:$environmentCursor){"
                        "nodes{name} pageInfo{hasNextPage endCursor}}}}}",
                        "-F",
                        f"review={review_node_id}",
                        "-F",
                        f"environmentCursor={nested_cursor}",
                    )
                    nested_response = read_runner.run(
                        nested_command,
                        timeout_seconds=_remaining(deadline),
                        max_output_bytes=max_output_bytes,
                    )
                    (
                        environment_found,
                        environment_has_next,
                        next_environment_cursor,
                    ) = _review_environment_page(
                        nested_response,
                        environment=recovery.environment,
                    )
                    if environment_found:
                        return AcceptanceReviewerInspection(
                            recovery=recovery,
                            status=_PRESENT_STATUS,
                            reviewer=reviewer,
                            deployment_review_id=review_id,
                        )
                    nested_cursor = next_environment_cursor
                    if not environment_has_next:
                        break
                else:
                    raise _environment_pagination_limit_error()
            if not has_next_page:
                return AcceptanceReviewerInspection(
                    recovery=recovery,
                    status=_REMOVED_STATUS,
                    reviewer=None,
                    deployment_review_id=None,
                    diagnostics=("scoped-review-record-no-longer-available",),
                )
        message = "deploymentReviews pagination limit exceeded"
        raise ValueError(message)  # noqa: TRY301
    except (
        subprocess.TimeoutExpired,
        TimeoutError,
        OSError,
        TypeError,
        ValueError,
    ):
        return AcceptanceReviewerInspection(
            recovery=recovery,
            status=_UNKNOWN_STATUS,
            reviewer=None,
            deployment_review_id=None,
            diagnostics=("reviewer-lookup-failed-human-inspection-required",),
        )
