"""Native first-project authority checks and durable registry partitions."""

from __future__ import annotations

from datetime import datetime, timedelta
from typing import TYPE_CHECKING, cast
from urllib.parse import urlencode

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    RubyBootstrapRun,
    admit_ruby_bootstrap,
    require_bootstrap,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyRequestBudget
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_REGISTRY_PARTITIONS,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping

    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyHttpResponse,
        RubyHttpTransport,
    )
    from three_workflow_delivery_v3.platform.ruby_github import (
        RubyGitHubRuntime,
    )

_REPO = "/repos/hcoona/three"
_OWNER_ID = 712433
_MIN_STATUS = 100
_MAX_STATUS = 599
BOOTSTRAP_WORKFLOW = ".github/workflows/workflow-delivery-v3-ruby-bootstrap.yml"


class RubyBootstrapPhaseBudget(RubyRequestBudget):
    """One job's fixed partition, joined to the operator's durable ledger.

    The reviewed caller must persist every reserve before returning, rejecting
    an already used ordinal. It must never recreate missing ledger state.
    RubyGems eligibility's first owners send was spent by the local operator.
    """

    def __init__(  # noqa: PLR0913 - exact phase and durable boundary
        self,
        run: RubyBootstrapRun,
        phase: str,
        transport: RubyHttpTransport,
        reserve: Callable[[str, int], None],
        *,
        deadline: datetime,
        clock: Callable[[], datetime],
    ) -> None:
        """Bind a finite partition without granting a registry capability."""
        require_bootstrap(
            phase in RUBY_REGISTRY_PARTITIONS,
            "Unknown Ruby bootstrap registry partition",
        )
        now = clock()
        run.require_current(now)
        expires = datetime.fromisoformat(
            ruby_text(run.request.document["expires-at"])
        )
        require_bootstrap(
            deadline.tzinfo is not None
            and now < deadline <= min(expires, now + timedelta(minutes=30)),
            "Ruby bootstrap registry phase exceeds its lifetime",
        )
        self.deadline = deadline
        self._clock = clock
        self._previous = now
        super().__init__(transport)
        self.run = run
        self.phase = phase
        self._reserve = reserve
        if (
            phase == "eligibility"
            and run.inputs.configuration.registry.name == "rubygems"
        ):
            self.used = 1

    def request(
        self,
        method: str,
        url: str,
        headers: dict[str, str],
        body: bytes | None,
        maximum_bytes: int,
    ) -> RubyHttpResponse:
        """Spend locally and durably before a send; failures never refund."""
        require_bootstrap(
            self.used
            < self.run.request.envelope.registry_partitions[self.phase],
            "Ruby bootstrap registry partition exhausted",
        )
        now = self._clock()
        self.run.require_current(now)
        require_bootstrap(
            self._previous <= now < self.deadline,
            "Ruby bootstrap registry phase expired or clock regressed",
        )
        self._previous = now
        self.used += 1
        self._reserve(self.phase, self.used)
        now = self._clock()
        self.run.require_current(now)
        require_bootstrap(
            self._previous <= now < self.deadline,
            "Ruby bootstrap reservation crossed its phase deadline",
        )
        self._previous = now
        response = self.transport.request(
            method, url, headers, body, maximum_bytes
        )
        require_bootstrap(
            type(response.status) is int
            and _MIN_STATUS <= response.status <= _MAX_STATUS
            and type(response.body) is bytes
            and len(response.body) <= maximum_bytes,
            "Invalid bounded Ruby bootstrap HTTP response",
        )
        return response


def check_ruby_bootstrap_authority(
    run: RubyBootstrapRun,
    github: RubyGitHubRuntime,
    environment: Mapping[str, str],
    now: datetime,
) -> dict[str, JsonValue]:
    """Establish actual native identity and current protected-path continuity.

    The caller supplies actual native environment and fresh GitHub capability,
    not request-controlled substitutes. Git ancestry/history is checked by the
    shared isolated reader; unrelated later main commits remain permitted.
    """
    current = admit_ruby_bootstrap(run.inputs, run.run_join, environment, now)
    require_bootstrap(
        current.binding_digest == run.binding_digest
        and current.native_environment == run.native_environment,
        "Ruby bootstrap current native identity changed",
    )
    read = github.operation_control(run.target)
    require_bootstrap(
        ruby_digest(read.content) == run.request.document["envelope-digest"],
        "Ruby bootstrap protected envelope changed",
    )
    repository = ruby_object(github.get(_REPO))
    owner = ruby_object(repository.get("owner"))
    require_bootstrap(
        repository.get("full_name") == "hcoona/three"
        and owner.get("login") == "hcoona"
        and type(owner.get("id")) is int
        and owner.get("id") == _OWNER_ID,
        "Ruby bootstrap repository owner changed",
    )
    branch = ruby_object(github.get(_REPO + "/branches/main"))
    require_bootstrap(
        branch.get("protected") is True,
        "Ruby bootstrap main is no longer protected",
    )
    collaborators = github.pages(_REPO + "/collaborators?affiliation=all")
    for value in collaborators:
        collaborator = ruby_object(value)
        permissions = ruby_object(collaborator.get("permissions"))
        require_bootstrap(
            all(
                type(permissions.get(p)) is bool
                for p in ("push", "maintain", "admin")
            ),
            "Ruby bootstrap writer permissions are unavailable",
        )
        if any(permissions[p] is True for p in ("push", "maintain", "admin")):
            require_bootstrap(
                collaborator.get("login") == "hcoona"
                and type(collaborator.get("id")) is int
                and collaborator.get("id") == _OWNER_ID,
                "Ruby bootstrap accepted writer changed",
            )
    return {
        "run-binding-digest": run.binding_digest,
        "main": read.main_sha,
        "envelope-digest": ruby_digest(read.content),
        "checked-at": now.isoformat(),
        "native-controls-digest": canonical_sha256(
            {
                "repository": repository,
                "branch": branch,
                "collaborators": collaborators,
            }
        ),
    }


def collect_ruby_bootstrap_approval(
    run: RubyBootstrapRun,
    github: RubyGitHubRuntime,
    *,
    sentinel: str,
    now: datetime,
) -> bytes:
    """Collect the distinct bootstrap workflow's native current-run approval."""
    run.require_current(now)
    registry = run.inputs.configuration.registry
    config = ruby_object(run.inputs.configuration.document["github-controls"])
    native = ruby_object(github.get(_REPO + f"/actions/runs/{run.run_id}"))
    actor = ruby_object(native.get("actor"))
    repository = ruby_object(native.get("repository"))
    require_bootstrap(
        type(native.get("id")) is int
        and native.get("id") == run.run_id
        and native.get("head_sha") == run.target
        and native.get("head_branch") == "main"
        and type(native.get("run_attempt")) is int
        and native.get("run_attempt") == 1
        and native.get("event") == "workflow_dispatch"
        and native.get("path") == BOOTSTRAP_WORKFLOW
        and actor.get("login") == "hcoona"
        and type(actor.get("id")) is int
        and actor.get("id") == _OWNER_ID
        and repository.get("full_name") == "hcoona/three",
        "Ruby bootstrap native run differs from its admitted request",
    )
    reviews = github.get(_REPO + f"/actions/runs/{run.run_id}/approvals")
    require_bootstrap(
        isinstance(reviews, list), "Ruby bootstrap approvals are unavailable"
    )
    matched: list[dict[str, JsonValue]] = []
    for value in cast("list[JsonValue]", reviews):
        review = ruby_object(value)
        environments = review.get("environments")
        require_bootstrap(
            isinstance(environments, list), "Malformed Ruby bootstrap approval"
        )
        for environment in cast("list[JsonValue]", environments):
            selected = ruby_object(environment)
            if selected.get("id") == config["environment-id"]:
                require_bootstrap(
                    selected.get("name") == registry.environment,
                    "Ruby bootstrap approval Environment changed",
                )
                matched.append(review)
    require_bootstrap(
        len(matched) == 1 and matched[0].get("state") == "approved",
        "Ruby bootstrap requires one current Environment approval",
    )
    reviewer = ruby_object(matched[0].get("user"))
    require_bootstrap(
        reviewer.get("login") == "hcoona"
        and type(reviewer.get("id")) is int
        and reviewer.get("id") == _OWNER_ID,
        "Ruby bootstrap native reviewer changed",
    )
    deployments = github.pages(
        _REPO
        + "/deployments?"
        + urlencode({"sha": run.target, "environment": registry.environment})
    )
    matching: list[dict[str, JsonValue]] = []
    retained: list[JsonValue] = []
    for value in deployments:
        deployment = ruby_object(value)
        identity = deployment.get("id")
        require_bootstrap(
            type(identity) is int
            and cast("int", identity) > 0
            and deployment.get("sha") == run.target
            and deployment.get("environment") == registry.environment,
            "Malformed Ruby bootstrap deployment inventory",
        )
        statuses = github.pages(_REPO + f"/deployments/{identity}/statuses")
        retained.append({"deployment": deployment, "statuses": statuses})
        if any(
            isinstance(status, dict)
            and status.get("state") == "in_progress"
            and isinstance(status.get("log_url"), str)
            and cast("str", status["log_url"]).startswith(
                f"https://github.com/hcoona/three/actions/runs/{run.run_id}/job/"
            )
            for status in statuses
        ):
            matching.append(deployment)
    require_bootstrap(
        len(matching) == 1 and sentinel == config["sentinel"],
        "Ruby bootstrap lacks exact native deployment or sentinel",
    )
    return canonicalize(
        {
            "repository": "hcoona/three",
            "run-id": run.run_id,
            "run-attempt": 1,
            "target": run.target,
            "workflow": BOOTSTRAP_WORKFLOW,
            "environment": registry.environment,
            "environment-id": config["environment-id"],
            "deployment-id": matching[0]["id"],
            "reviewer-id": _OWNER_ID,
            "reviewer": "hcoona",
            "state": "approved",
            "sentinel": sentinel,
            "native-response-digest": canonical_sha256(
                {"run": native, "reviews": reviews, "deployments": retained}
            ),
        }
    )
