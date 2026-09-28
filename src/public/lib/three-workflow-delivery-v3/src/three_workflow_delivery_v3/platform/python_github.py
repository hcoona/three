"""Read-only current GitHub control checks and ordered OIDC acquisition."""

from __future__ import annotations

from datetime import UTC, datetime
from http import HTTPStatus
from typing import TYPE_CHECKING, cast
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

from three_workflow_delivery_v3.adapters.pypi import MAX_RESPONSE_BYTES
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.release.governance_git import (
    IsolatedGovernanceGitReader,
)
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_WORKFLOW,
    PythonGovernance,
    python_governance_path,
)
from three_workflow_delivery_v3.repository.python_provider import python_text

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping

    from three_workflow_delivery_v3.adapters.pypi import (
        PythonHttpTransport,
        PythonRegistry,
    )
    from three_workflow_delivery_v3.records.release import ReleaseIntent

_REPO = "/repos/hcoona/three"
_API = "https://api.github.com"
_OWNER_ID = 712433
_PAGE_SIZE = 100
_MAX_PAGES = 5
_MAX_API_BYTES = 8 * 1024 * 1024


class PythonGitHubRuntime:
    """Fixed-repository control reads; never changes credentials or settings."""

    def __init__(
        self,
        token: str,
        transport: PythonHttpTransport,
        *,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
    ) -> None:
        """Retain the current job token in memory for GitHub API reads only."""
        self._token = python_text(token)
        self._transport = transport
        self._clock = clock

    def get(self, path: str) -> JsonValue:
        """Read one supported API response without redirects or retries."""
        if not path.startswith(_REPO + "/") and path != _REPO:
            message = "Python control read is outside its exact repository"
            raise ValueError(message)
        response = self._transport.request(
            "GET",
            _API + path,
            {
                "Accept": "application/vnd.github+json",
                "Authorization": f"Bearer {self._token}",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "three-workflow-delivery-v3",
            },
            None,
            _MAX_API_BYTES,
        )
        if response.status != HTTPStatus.OK:
            message = "Python GitHub control read failed"
            raise ValueError(message)
        return parse_json_strict(response.body)

    def pages(self, path: str, field: str | None = None) -> list[JsonValue]:
        """Bound complete inventories; pagination exhaustion is not absence."""
        result: list[JsonValue] = []
        for page in range(1, _MAX_PAGES + 1):
            document = self.get(
                path
                + ("&" if "?" in path else "?")
                + f"per_page={_PAGE_SIZE}&page={page}"
            )
            items = (
                document.get(field)
                if field is not None and isinstance(document, dict)
                else document
            )
            if not isinstance(items, list):
                message = "Python GitHub control inventory is malformed"
                raise TypeError(message)
            result.extend(items)
            if len(items) < _PAGE_SIZE:
                return result
        message = "Python GitHub control inventory exceeds its page budget"
        raise ValueError(message)

    def governance(
        self,
        registry: PythonRegistry,
        *,
        initial: PythonGovernance | None = None,
    ) -> PythonGovernance:
        """Fetch protected main and reject disabled state first."""
        read = IsolatedGovernanceGitReader(
            repository="hcoona/three", token=self._token
        ).read(
            repository="hcoona/three",
            ref="refs/heads/main",
            path=python_governance_path(registry),
            eligibility_main_sha=None
            if initial is None
            else initial.source_commit,
        )
        governance = PythonGovernance(
            registry, read.content, read.main_sha, self._clock()
        )
        governance.require_live(self._clock())
        self.configuration(governance)
        if initial is not None:
            initial.require_same_live(governance, self._clock())
        return governance

    def configuration(self, governance: PythonGovernance) -> None:
        """Verify GitHub controls against attested service registration."""
        governance.require_live(self._clock())
        # Administrative inventories and service registration are covered by
        # the protected, expiring configuration attestation. Normal job tokens
        # cannot read those APIs; never substitute empty lists or wider tokens.
        repository = self.get(_REPO)
        if (
            not isinstance(repository, dict)
            or repository.get("full_name") != "hcoona/three"
            or repository.get("owner") is None
        ):
            message = "Python repository identity is unavailable"
            raise ValueError(message)
        owner = repository["owner"]
        if (
            not isinstance(owner, dict)
            or owner.get("login") != "hcoona"
            or owner.get("id") != _OWNER_ID
        ):
            message = "Python repository owner changed"
            raise ValueError(message)
        branch = self.get(_REPO + "/branches/main")
        if not isinstance(branch, dict) or branch.get("protected") is not True:
            message = "Python main branch is not protected"
            raise ValueError(message)
        collaborators = self.pages(_REPO + "/collaborators?affiliation=all")
        for collaborator in collaborators:
            if not isinstance(collaborator, dict) or not isinstance(
                collaborator.get("permissions"), dict
            ):
                message = "Python writer inventory is unavailable"
                raise TypeError(message)
            permissions = cast(
                "dict[str, JsonValue]", collaborator["permissions"]
            )
            if any(
                type(permissions.get(p)) is not bool
                for p in ("push", "maintain", "admin")
            ):
                message = "Python writer permissions are malformed"
                raise TypeError(message)
            if any(
                permissions.get(p) is True
                for p in ("push", "maintain", "admin")
            ) and (
                collaborator.get("login") != "hcoona"
                or collaborator.get("id") != _OWNER_ID
            ):
                message = "Python accepted-writer trust changed"
                raise ValueError(message)

    def approval(  # noqa: C901
        self,
        intent: ReleaseIntent,
        governance: PythonGovernance,
        *,
        sentinel: str,
    ) -> bytes:
        """Bind native approval and deployment before token minting."""
        run = self.get(_REPO + f"/actions/runs/{intent.workflow_run_id}")
        if (
            not isinstance(run, dict)
            or run.get("head_sha") != intent.target
            or type(run.get("run_attempt")) is not int
            or run.get("run_attempt") != 1
            or run.get("event") != "workflow_dispatch"
            or run.get("path") != PYTHON_WORKFLOW
        ):
            message = (
                "Python native run identity differs from the approved request"
            )
            raise ValueError(message)
        actor = run.get("actor")
        if (
            not isinstance(actor, dict)
            or actor.get("id") != _OWNER_ID
            or actor.get("login") != "hcoona"
        ):
            message = "Python native run actor is not accepted"
            raise ValueError(message)
        config = cast(
            "dict[str, JsonValue]", governance.document["configuration"]
        )
        reviews = self.get(
            _REPO + f"/actions/runs/{intent.workflow_run_id}/approvals"
        )
        if not isinstance(reviews, list):
            message = "Python native approval history is unavailable"
            raise TypeError(message)
        matched = []
        for review in reviews:
            if not isinstance(review, dict) or not isinstance(
                review.get("environments"), list
            ):
                message = "Python native approval history is malformed"
                raise TypeError(message)
            if any(
                isinstance(e, dict)
                and e.get("id") == config["environment-id"]
                and e.get("name") == governance.registry.environment
                for e in cast("list[JsonValue]", review["environments"])
            ):
                matched.append(review)
        if len(matched) != 1 or matched[0].get("state") != "approved":
            message = "Python publisher lacks one current native approval"
            raise ValueError(message)
        reviewer = matched[0].get("user")
        if (
            not isinstance(reviewer, dict)
            or reviewer.get("id") != _OWNER_ID
            or reviewer.get("login") != "hcoona"
        ):
            message = "Python native approver differs from Governance"
            raise ValueError(message)
        deployments = self.pages(
            _REPO
            + "/deployments?"
            + urlencode(
                {
                    "sha": intent.target,
                    "environment": governance.registry.environment,
                }
            )
        )
        matching = []
        for deployment in deployments:
            if (
                not isinstance(deployment, dict)
                or type(deployment.get("id")) is not int
                or deployment.get("sha") != intent.target
                or deployment.get("environment")
                != governance.registry.environment
            ):
                message = "Python deployment inventory is malformed"
                raise ValueError(message)
            statuses = self.pages(
                _REPO + f"/deployments/{deployment['id']}/statuses"
            )
            if any(
                isinstance(s, dict)
                and s.get("state") == "in_progress"
                and isinstance(s.get("log_url"), str)
                and cast("str", s["log_url"]).startswith(
                    f"https://github.com/hcoona/three/actions/runs/{intent.workflow_run_id}/job/"
                )
                for s in statuses
            ):
                matching.append(deployment)
        if len(matching) != 1 or sentinel != config["sentinel"]:
            message = (
                "Python publisher lacks exact native deployment or sentinel"
            )
            raise ValueError(message)
        return canonicalize(
            {
                "repository": "hcoona/three",
                "run-id": intent.workflow_run_id,
                "run-attempt": 1,
                "target": intent.target,
                "environment": governance.registry.environment,
                "environment-id": config["environment-id"],
                "deployment-id": matching[0]["id"],
                "reviewer-id": _OWNER_ID,
                "reviewer": "hcoona",
                "state": "approved",
                "native-response-digest": canonical_sha256(
                    {"run": run, "reviews": reviews, "deployment": matching[0]}
                ),
                "sentinel": sentinel,
            }
        )


def obtain_python_oidc_assertion(
    audience: str,
    environment: Mapping[str, str],
    transport: PythonHttpTransport,
) -> str:
    """Request one assertion after the publisher persists authority."""
    if audience not in {"testpypi", "pypi"}:
        message = "Python OIDC audience is not admitted"
        raise ValueError(message)
    url = urlsplit(environment["ACTIONS_ID_TOKEN_REQUEST_URL"])
    if (
        url.scheme != "https"
        or url.hostname is None
        or not url.hostname.endswith(".actions.githubusercontent.com")
        or url.port not in {None, 443}
        or url.username
        or url.password
        or url.fragment
    ):
        message = "Python OIDC request URL is outside GitHub's issuer origin"
        raise ValueError(message)
    query = parse_qsl(url.query, keep_blank_values=True)
    if any(name == "audience" for name, _value in query):
        message = "Python OIDC request already selected an audience"
        raise ValueError(message)
    request_url = urlunsplit(
        (
            url.scheme,
            url.netloc,
            url.path,
            urlencode([*query, ("audience", audience)]),
            "",
        )
    )
    response = transport.request(
        "GET",
        request_url,
        {
            "Authorization": "Bearer "
            + environment["ACTIONS_ID_TOKEN_REQUEST_TOKEN"]
        },
        None,
        MAX_RESPONSE_BYTES,
    )
    if response.status != HTTPStatus.OK:
        message = "Python OIDC assertion request failed"
        raise ValueError(message)
    document = parse_json_strict(response.body)
    if not isinstance(document, dict):
        message = "Python OIDC assertion response is malformed"
        raise TypeError(message)
    return python_text(document.get("value"))
