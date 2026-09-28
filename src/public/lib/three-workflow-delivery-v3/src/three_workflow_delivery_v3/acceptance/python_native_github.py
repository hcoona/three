"""Closed hosted identity and bounded native Environment approval proof."""

from __future__ import annotations

import subprocess
from typing import TYPE_CHECKING, Protocol, cast
from urllib.parse import urlencode

from three_workflow_delivery_v3.acceptance.python_native_contract import (
    WORKFLOW,
    NativeRequest,
    commit,
    positive,
    require,
)
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize

if TYPE_CHECKING:
    from collections.abc import Mapping
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
    from three_workflow_delivery_v3.platform.python_github import (
        PythonGitHubRuntime,
    )

_OWNER_ID = 712433
_PAGE_SIZE = 100
_MAX_DEPLOYMENTS = 5

_REPO = "/repos/hcoona/three"


class ApprovalRequest(Protocol):
    """Common immutable facts for separately admitted Python operations."""

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return the closed operation request."""

    @property
    def registry(self) -> PythonRegistry:
        """Return the exact destination profile."""

    @property
    def digest(self) -> str:
        """Return the immutable request digest."""


def git_output(root: Path, *arguments: str) -> str:
    """Read local immutable Git facts without fetching or credentials."""
    return subprocess.run(  # noqa: S603 - bounded local Git facts
        ("git", *arguments),  # noqa: S607 - repository Git tool
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()


def validate_hosted(
    root: Path,
    environment: Mapping[str, str],
    tooling_sha: str,
    request: NativeRequest,
) -> int:
    """Reject foreign, moving, unprotected or rerun tooling before effects."""
    return validate_hosted_targets(
        root,
        environment,
        tooling_sha,
        tuple(
            cast("str", request.target(label)["commit"]) for label in ("a", "b")
        ),
        WORKFLOW,
    )


def validate_hosted_targets(
    root: Path,
    environment: Mapping[str, str],
    tooling_sha: str,
    targets: tuple[str, ...],
    workflow: str,
) -> int:
    """Bind the specified admitted workflow to actual local protected Git."""
    commit(tooling_sha)
    expected = {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": tooling_sha,
        "GITHUB_WORKFLOW_SHA": tooling_sha,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{workflow}@refs/heads/main",
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Linux",
    }
    require(
        all(environment.get(k) == v for k, v in expected.items()),
        "foreign Python native execution context",
    )
    require(
        git_output(root, "rev-parse", "HEAD") == tooling_sha
        and git_output(root, "rev-parse", "refs/remotes/origin/main")
        == tooling_sha,
        "Python native protected main moved",
    )
    for target in targets:
        commit(target)
        git_output(root, "merge-base", "--is-ancestor", target, tooling_sha)
    run = environment.get("GITHUB_RUN_ID", "")
    require(
        run.isascii() and run.isdecimal() and not run.startswith("0"),
        "invalid native run ID",
    )
    return positive(int(run))


def prove_native_approval(
    request: NativeRequest,
    run_id: int,
    tooling_sha: str,
    sentinel: str,
    runtime: PythonGitHubRuntime,
) -> bytes:
    """Read at most eight current-run facts; no pages, retry or fallback."""
    return prove_approval(
        request, run_id, tooling_sha, sentinel, runtime, WORKFLOW
    )


def prove_approval(  # noqa: PLR0913, PLR0917 - exact admitted proof tuple
    request: ApprovalRequest,
    run_id: int,
    tooling_sha: str,
    sentinel: str,
    runtime: PythonGitHubRuntime,
    workflow: str,
) -> bytes:
    """Replay or obtain the same bounded owner proof for one exact workflow."""
    config = cast("dict[str, JsonValue]", request.document["environment"])
    require(
        sentinel == config["sentinel"], "native Environment sentinel differs"
    )
    run = runtime.get(f"{_REPO}/actions/runs/{run_id}")
    require(isinstance(run, dict), "native run unavailable")
    run = cast("dict[str, JsonValue]", run)
    require(
        run.get("id") == run_id
        and run.get("head_sha") == tooling_sha
        and type(run.get("run_attempt")) is int
        and run["run_attempt"] == 1
        and run.get("event") == "workflow_dispatch"
        and run.get("path") in {workflow, f"{workflow}@main"}
        and run.get("head_branch") == "main",
        "native run binding differs",
    )
    actor = run.get("actor")
    require(
        isinstance(actor, dict)
        and actor.get("id") == _OWNER_ID
        and actor.get("login") == "hcoona",
        "native actor differs",
    )
    reviews = runtime.get(
        f"{_REPO}/actions/runs/{run_id}/approvals?per_page=100"
    )
    require(
        isinstance(reviews, list) and len(reviews) < _PAGE_SIZE,
        "native approval inventory incomplete",
    )
    matches = []
    for item in cast("list[JsonValue]", reviews):
        require(
            isinstance(item, dict)
            and isinstance(item.get("environments"), list),
            "malformed native approval",
        )
        item = cast("dict[str, JsonValue]", item)
        if any(
            isinstance(e, dict)
            and e.get("id") == config["id"]
            and e.get("name") == request.registry.environment
            for e in cast("list[JsonValue]", item["environments"])
        ):
            matches.append(item)
    require(
        len(matches) == 1 and matches[0].get("state") == "approved",
        "one exact owner approval required",
    )
    approver = matches[0].get("user")
    require(
        isinstance(approver, dict)
        and approver.get("id") == _OWNER_ID
        and approver.get("login") == "hcoona",
        "native approver differs",
    )
    query = urlencode(
        {
            "sha": tooling_sha,
            "environment": request.registry.environment,
            "per_page": 100,
        }
    )
    deployments = runtime.get(f"{_REPO}/deployments?{query}")
    require(
        isinstance(deployments, list)
        and 0 < len(deployments) <= _MAX_DEPLOYMENTS,
        "native deployment budget or inventory unavailable",
    )
    matched = []
    status_evidence: dict[str, JsonValue] = {}
    for item in cast("list[JsonValue]", deployments):
        require(isinstance(item, dict), "malformed native deployment")
        item = cast("dict[str, JsonValue]", item)
        identity = positive(item.get("id"))
        require(
            item.get("sha") == tooling_sha
            and item.get("environment") == request.registry.environment
            and str(identity) not in status_evidence,
            "foreign native deployment",
        )
        statuses = runtime.get(
            f"{_REPO}/deployments/{identity}/statuses?per_page=100"
        )
        require(
            isinstance(statuses, list) and len(statuses) < _PAGE_SIZE,
            "incomplete native deployment statuses",
        )
        status_evidence[str(identity)] = statuses
        if any(
            isinstance(s, dict)
            and s.get("state") == "in_progress"
            and isinstance(s.get("log_url"), str)
            and cast("str", s["log_url"]).startswith(
                f"https://github.com/hcoona/three/actions/runs/{run_id}/job/"
            )
            for s in cast("list[JsonValue]", statuses)
        ):
            matched.append(identity)
    require(
        len(matched) == 1, "one current native Environment deployment required"
    )
    return canonicalize(
        {
            "request-digest": request.digest,
            "tooling-sha": tooling_sha,
            "run-id": run_id,
            "run-attempt": 1,
            "environment": config,
            "run": run,
            "reviews": reviews,
            "deployments": deployments,
            "statuses": status_evidence,
            "deployment-id": matched[0],
        }
    )
