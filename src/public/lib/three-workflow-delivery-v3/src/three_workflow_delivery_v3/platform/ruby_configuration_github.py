"""Bounded GitHub configuration inventories for independent review."""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from http import HTTPStatus
from typing import TYPE_CHECKING, cast
from urllib.parse import parse_qsl, urlsplit

from three_workflow_delivery_v3.canonical import JsonValue, parse_json_strict
from three_workflow_delivery_v3.platform.ruby_configuration_http import (
    RubyConfigurationRequest,
    RubyConfigurationResponse,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.platform.ruby_configuration_http import (
        RubyConfigurationSender,
    )

_OWNER_ID = 712433
_PAGE_SIZE = 100
_CONTROL_LIMIT = 32
_RULESET_LIMIT = 5
_PACKAGE = "hcoona-release-smoke-ruby"
_MARKER = "WDV3_APPROVAL_ENVIRONMENT_MARKER"


def _require(condition: bool, message: str) -> None:  # noqa: FBT001 - assertion helper
    if not condition:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    _require(isinstance(value, dict), "Ruby configuration object is malformed")
    return cast("dict[str, JsonValue]", value)


def _array(value: JsonValue) -> list[JsonValue]:
    _require(isinstance(value, list), "Ruby configuration array is malformed")
    return cast("list[JsonValue]", value)


def _positive(value: JsonValue) -> int:
    _require(
        type(value) is int and value > 0,
        "Ruby configuration native ID is invalid",
    )
    return cast("int", value)


def _owner(value: JsonValue) -> None:
    owner = _object(value)
    _require(
        owner.get("login") == "hcoona"
        and type(owner.get("id")) is int
        and owner["id"] == _OWNER_ID,
        "Ruby configuration owner differs",
    )


def _json(response: RubyConfigurationResponse) -> JsonValue:
    _require(
        response.failure is None and response.status == HTTPStatus.OK,
        "Ruby configuration read did not succeed",
    )
    media = (
        (response.header("content-type") or "").split(";", 1)[0].strip().lower()
    )
    _require(
        media in {"application/json", "application/vnd.github+json"},
        "Ruby configuration response is not JSON",
    )
    return parse_json_strict(response.body)


@dataclass(frozen=True, slots=True)
class RubyControlInspection:
    """Screened control facts and private complete originals awaiting review."""

    destination: str
    principal: RubyConfigurationResponse = field(repr=False)
    facts: dict[str, JsonValue]
    responses: tuple[RubyConfigurationResponse, ...] = field(repr=False)


@dataclass(frozen=True, slots=True)
class RubyProjectInventory:
    """Current namespace observation, never name reservation or ownership."""

    destination: str
    classification: str
    package_count: int
    responses: tuple[RubyConfigurationResponse, ...] = field(repr=False)

    def screened(self) -> dict[str, JsonValue]:
        """Omit unrelated account package data from workflow-facing facts."""
        return {
            "destination": self.destination,
            "classification": self.classification,
            "principal": {"login": "hcoona", "id": _OWNER_ID},
            "absence-kind": "github-owner-package-inventory",
            "requires-independent-review": True,
        }


@dataclass(frozen=True, slots=True)
class RubySelectedPackageInspection:
    """Separate postcreation package facts, without a false principal record."""

    destination: str
    facts: dict[str, JsonValue]
    response: RubyConfigurationResponse = field(repr=False)


class RubyConfigurationCollector:
    """Collect fixed endpoint families through the admitted durable sender."""

    def __init__(self, sender: RubyConfigurationSender) -> None:
        """Inject transport and persistent accounting without credentials."""
        self._sender = sender

    def _read(
        self,
        request: RubyConfigurationRequest,
        evidence: list[RubyConfigurationResponse],
    ) -> RubyConfigurationResponse:
        response = self._sender.send(request)
        _require(
            response.request == request,
            "Ruby configuration response request differs",
        )
        evidence.append(response)
        return response

    def _pages(  # noqa: PLR0913 - explicit fixed inventory contract
        self,
        destination: str,
        role: str,
        limit: int,
        evidence: list[RubyConfigurationResponse],
        *,
        field_name: str | None = None,
        identity: str = "id",
    ) -> list[dict[str, JsonValue]]:
        items: list[dict[str, JsonValue]] = []
        identities: set[str | int] = set()
        total: int | None = None
        for page in range(1, limit + 1):
            request = RubyConfigurationRequest(destination, role, page)
            response = self._read(request, evidence)
            doc = _json(response)
            if field_name is not None:
                envelope = _object(doc)
                count = envelope.get("total_count")
                _require(
                    type(count) is int and count >= 0,
                    "Ruby configuration inventory lacks total count",
                )
                _require(
                    total is None or count == total,
                    "Ruby configuration inventory total changed",
                )
                total = cast("int", count)
                doc = envelope.get(field_name)
            batch = _array(doc)
            _require(
                len(batch) <= _PAGE_SIZE,
                "Ruby configuration page exceeds page size",
            )
            for raw in batch:
                item = _object(raw)
                key = item.get(identity)
                if identity == "id":
                    key = _positive(key)
                else:
                    _require(
                        isinstance(key, str) and bool(key),
                        "Ruby configuration inventory identity missing",
                    )
                _require(
                    key not in identities,
                    "Ruby configuration inventory repeats an identity",
                )
                identities.add(cast("str | int", key))
                items.append(item)
            next_page = _next_page(response, limit)
            if total is not None:
                _require(
                    len(items) <= total,
                    "Ruby configuration inventory exceeds total",
                )
                if len(items) == total:
                    _require(
                        not next_page,
                        "Ruby configuration total contradicts pagination",
                    )
                    return items
            if next_page:
                _require(
                    bool(batch), "Ruby configuration empty page has a successor"
                )
            elif len(batch) < _PAGE_SIZE:
                _require(
                    total is None or total == len(items),
                    "Ruby configuration inventory is incomplete",
                )
                return items
        message = "Ruby configuration inventory exhausted its page bound"
        raise ValueError(message)

    def collect_project_absence(
        self,
        destination: str,
        principal: RubyConfigurationResponse,
        *,
        admitted_read_packages: bool = False,
    ) -> RubyProjectInventory:
        """Inspect complete owner pages without a visibility filter."""
        _require(
            principal.request
            == RubyConfigurationRequest(destination, "principal"),
            "Ruby package inventory requires the selected principal read",
        )
        _owner(_json(principal))
        scopes = principal.header("x-oauth-scopes")
        _require(
            type(admitted_read_packages) is bool
            and (
                admitted_read_packages
                or (
                    scopes is not None
                    and "read:packages"
                    in {value.strip() for value in scopes.split(",")}
                )
            ),
            "Ruby owner inventory lacks admitted package-read capability",
        )
        evidence: list[RubyConfigurationResponse] = []
        packages = self._pages(destination, "owner-packages", 10, evidence)
        names: set[str] = set()
        present = False
        for package in packages:
            _validate_package(package, selected=False)
            name = cast("str", package["name"])
            _require(
                name not in names, "Ruby owner inventory repeats a package name"
            )
            names.add(name)
            present |= name == _PACKAGE
        return RubyProjectInventory(
            destination,
            "present" if present else "absent",
            len(packages),
            tuple(evidence),
        )

    def collect_selected_package(
        self, destination: str
    ) -> RubySelectedPackageInspection:
        """Read postcreation package facts; 404 never proves absence."""
        evidence: list[RubyConfigurationResponse] = []
        response = self._read(
            RubyConfigurationRequest(destination, "selected-package"), evidence
        )
        package = _object(_json(response))
        _validate_package(package, selected=True)
        repository = _object(package.get("repository"))
        _repository(repository)
        facts: dict[str, JsonValue] = {
            "package-id": package["id"],
            "name": package["name"],
            "visibility": package["visibility"],
            "version-count": package["version_count"],
            "repository-id": repository["id"],
            "repository": "hcoona/three",
            "requires-actions-access-ui-inspection": True,
            "requires-independent-review": True,
        }
        return RubySelectedPackageInspection(destination, facts, response)

    def collect_controls(self, destination: str) -> RubyControlInspection:
        """Read one control unit, retaining incomplete Environment facts."""
        evidence: list[RubyConfigurationResponse] = []
        principal = self._read(
            RubyConfigurationRequest(destination, "principal"), evidence
        )
        _owner(_json(principal))
        repository = _object(
            _json(
                self._read(
                    RubyConfigurationRequest(destination, "repository"),
                    evidence,
                )
            )
        )
        _repository(repository)
        main = _object(
            _json(
                self._read(
                    RubyConfigurationRequest(destination, "main"), evidence
                )
            )
        )
        _require(
            main.get("name") == "main" and main.get("protected") is True,
            "Ruby configuration main is not protected",
        )
        sha = _object(main.get("commit")).get("sha")
        _require(
            isinstance(sha, str)
            and re.fullmatch(r"[0-9a-f]{40}", sha) is not None,
            "Ruby configuration main has no exact commit",
        )
        protection = self._read(
            RubyConfigurationRequest(destination, "protection"), evidence
        )
        _require(
            protection.failure is None
            and protection.status in {HTTPStatus.OK, HTTPStatus.NOT_FOUND},
            "Ruby configuration protection is unavailable",
        )
        classic = (
            _object(_json(protection))
            if protection.status == HTTPStatus.OK
            else None
        )
        effective = self._rule_pages(destination, evidence)
        rulesets = self._pages(destination, "rulesets", 5, evidence)
        _require(
            len(rulesets) <= _RULESET_LIMIT,
            "Ruby configuration needs too many ruleset details",
        )
        details: list[JsonValue] = []
        for summary in rulesets:
            _require(
                summary.get("source_type") == "Repository"
                and summary.get("source") == "hcoona/three",
                "Ruby configuration inherited ruleset is unsupported",
            )
            detail = _object(
                _json(
                    self._read(
                        RubyConfigurationRequest(
                            destination,
                            "ruleset-detail",
                            resource_id=_positive(summary.get("id")),
                        ),
                        evidence,
                    )
                )
            )
            _validate_ruleset(summary, detail)
            details.append(detail)
        _require(
            classic is not None or bool(effective),
            "Ruby configuration enforcing protection is unavailable",
        )
        _validate_effective(effective, details)
        collaborators = self._pages(destination, "collaborators", 5, evidence)
        _writers(collaborators)
        env_response = self._read(
            RubyConfigurationRequest(destination, "environment"), evidence
        )
        facts: dict[str, JsonValue] = {
            "repository-id": repository["id"],
            "main": sha,
            "classic-protection": classic,
            "effective-rules": cast("JsonValue", effective),
            "rulesets": details,
            "accepted-writers": ["hcoona"],
            "requires-independent-review": True,
        }
        if (
            env_response.status == HTTPStatus.NOT_FOUND
            and env_response.failure is None
        ):
            facts["environment-state"] = "missing-or-inaccessible"
        else:
            env = _object(_json(env_response))
            facts.update(self._environment(destination, env, evidence))
        _require(
            len(evidence) <= _CONTROL_LIMIT,
            "Ruby configuration control read unit exceeded bound",
        )
        return RubyControlInspection(
            destination, principal, facts, tuple(evidence)
        )

    def _rule_pages(
        self, destination: str, evidence: list[RubyConfigurationResponse]
    ) -> list[dict[str, JsonValue]]:
        result: list[dict[str, JsonValue]] = []
        seen: set[tuple[int, str]] = set()
        for page in range(1, 6):
            response = self._read(
                RubyConfigurationRequest(destination, "effective-rules", page),
                evidence,
            )
            batch = _array(_json(response))
            _require(
                len(batch) <= _PAGE_SIZE,
                "Ruby effective-rule page exceeds limit",
            )
            for value in batch:
                rule = _object(value)
                key = (
                    _positive(rule.get("ruleset_id")),
                    cast("str", rule.get("type")),
                )
                _require(
                    isinstance(key[1], str) and key not in seen,
                    "Ruby effective-rule identity is ambiguous",
                )
                seen.add(key)
                result.append(rule)
            next_page = _next_page(response, 5)
            if not next_page and len(batch) < _PAGE_SIZE:
                return result
        message = "Ruby effective-rule inventory exhausted its page bound"
        raise ValueError(message)

    def _environment(
        self,
        destination: str,
        env: dict[str, JsonValue],
        evidence: list[RubyConfigurationResponse],
    ) -> dict[str, JsonValue]:
        expected = RubyConfigurationRequest(
            destination, "environment"
        ).environment
        _require(
            env.get("name") == expected,
            "Ruby configuration Environment name differs",
        )
        env_id = _positive(env.get("id"))
        policy = _object(env.get("deployment_branch_policy"))
        _require(
            policy.get("protected_branches") is False
            and policy.get("custom_branch_policies") is True,
            "Ruby configuration Environment branch policy differs",
        )
        rules = [
            _object(value) for value in _array(env.get("protection_rules"))
        ]
        types = [rule.get("type") for rule in rules]
        _require(
            len(types) == len(set(types))
            and set(types)
            <= {"wait_timer", "required_reviewers", "branch_policy"},
            "Ruby configuration Environment has unsupported protection",
        )
        reviewer = next(
            (
                rule
                for rule in rules
                if rule.get("type") == "required_reviewers"
            ),
            None,
        )
        _require(
            reviewer is not None,
            "Ruby configuration Environment lacks reviewer protection",
        )
        reviewer = cast("dict[str, JsonValue]", reviewer)
        _require(
            reviewer.get("prevent_self_review") is False,
            "Ruby configuration self-review policy differs",
        )
        reviewers = _array(reviewer.get("reviewers"))
        _require(
            len(reviewers) == 1,
            "Ruby configuration Environment reviewer count differs",
        )
        entry = _object(reviewers[0])
        _require(
            entry.get("type") == "User",
            "Ruby configuration reviewer is not a user",
        )
        _owner(entry.get("reviewer"))
        wait = next(
            (rule for rule in rules if rule.get("type") == "wait_timer"), None
        )
        _require(
            wait is None
            or (
                type(wait.get("wait_timer")) is int and wait["wait_timer"] == 0
            ),
            "Ruby configuration wait timer is unavailable or nonzero",
        )
        secrets = self._pages(
            destination,
            "secrets",
            2,
            evidence,
            field_name="secrets",
            identity="name",
        )
        _require(not secrets, "Ruby configuration Environment contains secrets")
        variables = self._pages(
            destination,
            "variables",
            2,
            evidence,
            field_name="variables",
            identity="name",
        )
        _require(
            len(variables) == 1
            and variables[0].get("name") == _MARKER
            and variables[0].get("value") == expected + "/v1",
            "Ruby configuration marker or variables differ",
        )
        branches = self._pages(
            destination,
            "branch-policies",
            2,
            evidence,
            field_name="branch_policies",
        )
        _require(
            len(branches) == 1
            and branches[0].get("name") == "main"
            and branches[0].get("type") == "branch",
            "Ruby configuration selected branches differ",
        )
        custom = _object(
            _json(
                self._read(
                    RubyConfigurationRequest(destination, "custom-protections"),
                    evidence,
                )
            )
        )
        _require(
            type(custom.get("total_count")) is int
            and custom["total_count"] == 0
            and custom.get("custom_deployment_protection_rules") == [],
            "Ruby configuration custom protection is unavailable or present",
        )
        bypass = env.get("can_admins_bypass")
        _require(
            "can_admins_bypass" not in env or type(bypass) is bool,
            "Ruby configuration bypass state is malformed",
        )
        return {
            "environment-state": "ui-required"
            if bypass is None
            else "observed",
            "environment-id": env_id,
            "reviewer-rule-id": _positive(reviewer.get("id")),
            "deployment-branch-rule-id": branches[0]["id"],
            "reviewer-id": _OWNER_ID,
            "prevent-self-review": False,
            "wait-timer": 0,
            "wait-timer-source": "absent-optional-rule"
            if wait is None
            else "explicit-rule",
            "protected-main-only": True,
            "sentinel": expected + "/v1",
            "secret-count": 0,
            "can-admins-bypass": bypass,
        }


def _next_page(response: RubyConfigurationResponse, limit: int) -> bool:
    value = response.header("link")
    if value is None:
        return False
    relations: dict[str, int] = {}
    for part in value.split(","):
        match = re.fullmatch(
            r'\s*<([^>]+)>;\s*rel="(next|prev|first|last)"\s*', part
        )
        _require(
            match is not None, "Ruby configuration pagination link is malformed"
        )
        match = cast("re.Match[str]", match)
        url, relation = match.groups()
        parsed = urlsplit(url)
        query = parse_qsl(
            parsed.query, keep_blank_values=True, strict_parsing=True
        )
        pages = [value for key, value in query if key == "page"]
        _require(
            len(pages) == 1 and pages[0].isdigit(),
            "Ruby configuration pagination page is malformed",
        )
        page = int(pages[0])
        _require(
            1 <= page <= limit and relation not in relations,
            "Ruby configuration pagination exceeds fixed bound",
        )
        expected = urlsplit(
            RubyConfigurationRequest(
                response.request.destination, response.request.role, page
            ).path
        )
        _require(
            parsed.scheme == "https"
            and parsed.netloc == "api.github.com"
            and not parsed.fragment
            and parsed.path == expected.path
            and sorted(query) == sorted(parse_qsl(expected.query)),
            "Ruby configuration pagination escaped selected endpoint",
        )
        relations[relation] = page
    current = response.request.page
    _require(
        "next" not in relations or relations["next"] == current + 1,
        "Ruby configuration pagination skips a page",
    )
    _require(
        "prev" not in relations or relations["prev"] == current - 1,
        "Ruby configuration pagination predecessor differs",
    )
    _require(
        "first" not in relations or relations["first"] == 1,
        "Ruby configuration pagination first page differs",
    )
    _require(
        "last" not in relations or relations["last"] >= current,
        "Ruby configuration pagination last page contradicts current",
    )
    _require(
        "last" not in relations
        or (relations["last"] > current) == ("next" in relations),
        "Ruby configuration last page contradicts successor",
    )
    return "next" in relations


def _repository(value: dict[str, JsonValue]) -> None:
    _positive(value.get("id"))
    _require(
        value.get("full_name") == "hcoona/three"
        and value.get("name") == "three",
        "Ruby configuration repository differs",
    )
    _owner(value.get("owner"))


def _validate_package(package: dict[str, JsonValue], *, selected: bool) -> None:
    _positive(package.get("id"))
    _owner(package.get("owner"))
    name = package.get("name")
    _require(
        isinstance(name, str)
        and bool(name)
        and package.get("package_type") == "rubygems",
        "Ruby owner inventory contains malformed package identity",
    )
    _require(not selected or name == _PACKAGE, "Ruby selected package differs")
    count = package.get("version_count")
    _require(
        type(count) is int
        and count >= 0
        and package.get("visibility") in {"public", "private", "internal"},
        "Ruby package visibility or version count is malformed",
    )


def _writers(collaborators: list[dict[str, JsonValue]]) -> None:
    found = False
    for collaborator in collaborators:
        permissions = _object(collaborator.get("permissions"))
        _require(
            all(
                type(permissions.get(key)) is bool
                for key in ("push", "maintain", "admin")
            ),
            "Ruby configuration collaborator permissions are incomplete",
        )
        if any(permissions[key] for key in ("push", "maintain", "admin")):
            _owner(collaborator)
            found = True
    _require(found, "Ruby configuration accepted writer is absent")


def _validate_ruleset(
    summary: dict[str, JsonValue], detail: dict[str, JsonValue]
) -> None:
    for key in ("id", "source", "source_type", "enforcement"):
        _require(
            detail.get(key) == summary.get(key),
            "Ruby ruleset detail differs from inventory",
        )
    _require(
        detail.get("target") in {"branch", "tag"}
        and detail.get("enforcement") in {"active", "disabled", "evaluate"},
        "Ruby ruleset target or enforcement is unsupported",
    )
    conditions = _object(detail.get("conditions"))
    _require(
        set(conditions) == {"ref_name"},
        "Ruby ruleset conditions are unsupported",
    )
    refs = _object(conditions["ref_name"])
    _require(
        set(refs) == {"include", "exclude"},
        "Ruby ruleset ref condition is incomplete",
    )
    for key in refs:
        _require(
            all(
                isinstance(value, str) and bool(value)
                for value in _array(refs[key])
            ),
            "Ruby ruleset ref condition is malformed",
        )
    for actor in _array(detail.get("bypass_actors")):
        bypass = _object(actor)
        _require(
            bypass.get("actor_type")
            in {
                "Integration",
                "OrganizationAdmin",
                "RepositoryRole",
                "Team",
                "DeployKey",
            }
            and bypass.get("bypass_mode")
            in {"always", "pull_request", "exempt"},
            "Ruby ruleset bypass actor is unsupported",
        )
        _positive(bypass.get("actor_id"))
    for raw in _array(detail.get("rules")):
        rule = _object(raw)
        _require(
            isinstance(rule.get("type"), str) and bool(rule["type"]),
            "Ruby ruleset rule type is missing",
        )
    # Full rules/conditions remain review evidence; no automatic policy verdict.


def _validate_effective(
    effective: list[dict[str, JsonValue]], details: list[JsonValue]
) -> None:
    known = {_object(value)["id"]: _object(value) for value in details}
    for rule in effective:
        detail = known.get(rule.get("ruleset_id"))
        _require(
            detail is not None
            and detail.get("enforcement") == "active"
            and rule.get("ruleset_source_type") == "Repository"
            and rule.get("ruleset_source") == "hcoona/three",
            "Ruby effective rule lacks an active supported ruleset",
        )
