"""Complete configuration inventories use controlled native response records."""

import pytest
from three_workflow_delivery_v3.platform.ruby_configuration_github import (
    RubyConfigurationCollector,
)
from three_workflow_delivery_v3.platform.ruby_configuration_http import (
    RubyConfigurationRequest,
)

from .ruby_configuration_fixtures import (
    OWNER,
    Sender,
    controls,
    package,
    page_link,
    principal,
    response,
)


@pytest.mark.parametrize("packages", [[], [package()], [package(target=True)]])
def test_owner_inventory_distinguishes_project_without_version_assumptions(
    packages,
):
    """Private zero-version projects still block bootstrap."""
    sender = Sender({"owner-packages": packages})
    result = RubyConfigurationCollector(sender).collect_project_absence(
        "github-packages", principal()
    )
    assert result.classification == (
        "present"
        if any(item["name"] == "hcoona-release-smoke-ruby" for item in packages)
        else "absent"
    )
    assert result.package_count == len(packages)
    assert sender.requests == [
        RubyConfigurationRequest("github-packages", "owner-packages")
    ]
    assert result.responses == tuple(sender.responses)
    assert result.screened() == {
        "destination": "github-packages",
        "classification": result.classification,
        "principal": OWNER,
        "absence-kind": "github-owner-package-inventory",
        "requires-independent-review": True,
    }
    assert "unrelated-private" not in str(result.screened())
    assert "unrelated-private" not in repr(result)


def test_owner_inventory_follows_exact_successor_and_retains_both_originals():
    """Reuse principal evidence and perform only selected inventory pages."""
    first = response(
        RubyConfigurationRequest("github-packages", "owner-packages"),
        [package()],
        headers=(("link", page_link("owner-packages", 2)),),
    )
    last = response(
        RubyConfigurationRequest("github-packages", "owner-packages", 2),
        [package(2, target=True)],
        headers=(("link", page_link("owner-packages", 1, relation="prev")),),
    )
    sender = Sender(scripted=[first, last])
    result = RubyConfigurationCollector(sender).collect_project_absence(
        "github-packages", principal()
    )
    assert result.classification == "present"
    assert result.package_count == 2  # noqa: PLR2004
    assert result.responses == (first, last)
    assert [request.page for request in sender.requests] == [1, 2]
    assert all(request.role == "owner-packages" for request in sender.requests)


@pytest.mark.parametrize(
    ("scopes", "admitted"),
    [
        (None, False),
        ("write:packages", False),
        ("not-read:packages", False),
        ("read:packages", 1),
    ],
)
def test_owner_inventory_requires_existing_supported_scope_without_upgrade(
    scopes, admitted
):
    """Missing scope stops before any package request."""
    sender = Sender({"owner-packages": []})
    with pytest.raises(ValueError, match="capability"):
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages",
            principal(scopes=scopes),
            admitted_read_packages=admitted,
        )
    assert sender.requests == []


def test_owner_inventory_accepts_admitted_capability_without_scope_header():
    """Model independently admitted capability without a scope header."""
    sender = Sender({"owner-packages": []})
    result = RubyConfigurationCollector(sender).collect_project_absence(
        "github-packages", principal(scopes=None), admitted_read_packages=True
    )
    assert result.classification == "absent"
    assert len(sender.requests) == 1


@pytest.mark.parametrize(
    "change", ["login", "id", "role", "destination", "failure"]
)
def test_owner_inventory_requires_exact_prior_principal_response(change):
    """Foreign or failed principal evidence cannot authorize namespace reads."""
    doc = dict(OWNER)
    request = RubyConfigurationRequest("github-packages", "principal")
    if change == "login":
        doc["login"] = "foreign"
    elif change == "id":
        doc["id"] = True
    elif change == "role":
        request = RubyConfigurationRequest("github-packages", "repository")
    elif change == "destination":
        request = RubyConfigurationRequest("rubygems", "principal")
    proof = response(
        request,
        doc,
        status=403 if change == "failure" else 200,
        headers=(("x-oauth-scopes", "read:packages"),),
    )
    sender = Sender({"owner-packages": []})
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages", proof
        )
    assert sender.requests == []


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("owner", None),
        ("owner", {"login": "foreign", "id": 712433}),
        ("id", True),
        ("package_type", "npm"),
        ("name", ""),
        ("version_count", -1),
        ("version_count", False),
        ("visibility", "unknown"),
    ],
)
def test_package_inventory_rejects_unknown_identity_or_owner(field, value):
    """An ambiguous package object never disappears from absence evidence."""
    item = package()
    item[field] = value
    sender = Sender({"owner-packages": [item]})
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages", principal()
        )
    assert len(sender.requests) == 1


@pytest.mark.parametrize(
    "mode",
    [
        "duplicate-id",
        "duplicate-name",
        "oversized",
        "wrong-request",
        "404",
        "redirect",
        "malformed",
    ],
)
def test_package_inventory_rejects_incomplete_or_conflicting_response(mode):
    """Response shape and provenance matter independently of HTTP success."""
    items = [package(), package(2)]
    if mode == "duplicate-id":
        items[1]["id"] = 1
    elif mode == "duplicate-name":
        items[1]["name"] = items[0]["name"]
    elif mode == "oversized":
        items = [package(i + 1) for i in range(101)]
    request = RubyConfigurationRequest(
        "github-packages",
        "owner-packages" if mode != "wrong-request" else "selected-package",
    )
    result = response(
        request,
        items,
        status={"404": 404, "redirect": 302}.get(mode, 200),
        body=b"{" if mode == "malformed" else None,
    )
    sender = Sender(scripted=[result])
    with pytest.raises(ValueError):  # noqa: PT011 - JSON parse or closed collector guard
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages", principal()
        )
    assert len(sender.requests) == 1


@pytest.mark.parametrize(
    "link",
    [
        (
            "<https://evil.invalid/user/packages?package_type=rubygems"
            '&per_page=100&page=2>; rel="next"'
        ),
        page_link("owner-packages", 3),
        page_link("owner-packages", 2).replace("per_page=100", "per_page=99"),
        page_link("owner-packages", 2) + ", " + page_link("owner-packages", 2),
        page_link("owner-packages", 2, relation="last"),
        page_link("owner-packages", 2).replace("page=2", "page=11"),
        page_link("owner-packages", 2).replace(
            "page=2", "page=2&visibility=public"
        ),
        "malformed",
    ],
)
def test_inventory_pagination_cannot_skip_escape_or_hide_pages(link):
    """Bad continuation never causes a second send to a supplied URL."""
    initial = response(
        RubyConfigurationRequest("github-packages", "owner-packages"),
        [package()],
        headers=(("link", link),),
    )
    sender = Sender(scripted=[initial])
    with pytest.raises(ValueError, match=r"pagination|last page"):
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages", principal()
        )
    assert len(sender.requests) == 1


def test_full_final_owner_page_without_terminal_evidence_stops_at_ten():
    """A complete-looking cap page does not establish inventory completeness."""
    pages = [
        response(
            RubyConfigurationRequest("github-packages", "owner-packages", page),
            [package((page - 1) * 100 + index + 1) for index in range(100)],
        )
        for page in range(1, 11)
    ]
    sender = Sender(scripted=pages)
    with pytest.raises(ValueError, match="page bound"):
        RubyConfigurationCollector(sender).collect_project_absence(
            "github-packages", principal()
        )
    assert [request.page for request in sender.requests] == list(range(1, 11))


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize("bypass", [False, True, None])
def test_complete_controls_retain_native_facts_and_explicit_bypass_uncertainty(
    destination, bypass
):
    """Retain original evidence and require independent review."""
    docs = controls(destination)
    if bypass is None:
        docs["environment"].pop("can_admins_bypass")
    else:
        docs["environment"]["can_admins_bypass"] = bypass
    sender = Sender(docs)
    result = RubyConfigurationCollector(sender).collect_controls(destination)
    assert result.responses == tuple(sender.responses)
    assert result.principal == sender.responses[0]
    assert result.facts["can-admins-bypass"] is bypass
    assert result.facts["environment-state"] == (
        "ui-required" if bypass is None else "observed"
    )
    assert result.facts["classic-protection"] == docs["protection"]
    assert result.facts["accepted-writers"] == ["hcoona"]
    assert result.facts["reviewer-rule-id"] == 102  # noqa: PLR2004
    assert result.facts["deployment-branch-rule-id"] == 104  # noqa: PLR2004
    assert result.facts["requires-independent-review"] is True
    assert [request.role for request in sender.requests] == [
        "principal",
        "repository",
        "main",
        "protection",
        "effective-rules",
        "rulesets",
        "collaborators",
        "environment",
        "secrets",
        "variables",
        "branch-policies",
        "custom-protections",
    ]
    assert len(sender.requests) <= 32  # noqa: PLR2004
    assert all(
        request.method == "GET" and request.body is None
        for request in sender.requests
    )
    assert sender.requests[-1].path.endswith("/deployment_protection_rules")


def test_environment_404_remains_missing_or_inaccessible_without_subreads():
    """A configuration 404 never invents an absent resource or write grant."""
    docs = controls()
    docs["environment"] = response(
        RubyConfigurationRequest("github-packages", "environment"),
        {},
        status=404,
    )
    sender = Sender(docs)
    result = RubyConfigurationCollector(sender).collect_controls(
        "github-packages"
    )
    assert result.facts["environment-state"] == "missing-or-inaccessible"
    assert "environment-id" not in result.facts
    assert "can-admins-bypass" not in result.facts
    assert sender.requests[-1].role == "environment"
    assert len(sender.requests) == 8  # noqa: PLR2004


@pytest.mark.parametrize(
    ("role", "key", "value"),
    [
        ("repository", "full_name", "hcoona/other"),
        ("main", "protected", 1),
        ("main", "commit", {"sha": "main"}),
        ("environment", "name", "other-environment"),
        ("environment", "id", True),
        ("environment", "can_admins_bypass", 0),
        (
            "environment",
            "deployment_branch_policy",
            {"protected_branches": True, "custom_branch_policies": False},
        ),
        ("secrets", "secrets", [{"name": "STATIC_TOKEN"}]),
        ("variables", "variables", []),
        (
            "branch-policies",
            "branch_policies",
            [{"id": 104, "name": "main", "type": "tag"}],
        ),
        ("custom-protections", "total_count", 1),
    ],
)
def test_controls_reject_malformed_or_conflicting_native_state(
    role, key, value
):
    """Mismatch halts at its role without later hidden sends or repair."""
    docs = controls()
    docs[role][key] = value
    sender = Sender(docs)
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert sender.requests[-1].role == (
        "custom-protections" if key == "can_admins_bypass" else role
    )
    assert all(request.method == "GET" for request in sender.requests)


@pytest.mark.parametrize(
    "change",
    [
        "foreign-writer",
        "missing-permission",
        "missing-writer",
        "reviewer",
        "self-review",
        "wait",
        "custom-rule",
    ],
)
def test_controls_reject_ambiguous_writers_or_approval_rules(change):
    """Unknown write access or approval protection cannot pass inspection."""
    docs = controls()
    rules = docs["environment"]["protection_rules"]
    if change == "foreign-writer":
        docs["collaborators"].append(
            {
                "id": 2,
                "login": "foreign",
                "permissions": {
                    "push": True,
                    "maintain": False,
                    "admin": False,
                },
            }
        )
    elif change == "missing-permission":
        docs["collaborators"][0]["permissions"].pop("push")
    elif change == "missing-writer":
        docs["collaborators"] = []
    elif change == "reviewer":
        rules[0]["reviewers"][0]["type"] = "Team"
    elif change == "self-review":
        rules[0]["prevent_self_review"] = True
    elif change == "wait":
        rules[1]["wait_timer"] = 1
    else:
        rules.append({"type": "custom"})
    sender = Sender(docs)
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert sender.requests[-1].role == (
        "collaborators"
        if change in {"foreign-writer", "missing-permission", "missing-writer"}
        else "environment"
    )


def _ruleset_controls():
    docs = controls()
    docs["protection"] = response(
        RubyConfigurationRequest("github-packages", "protection"),
        {},
        status=404,
    )
    summary = {
        "id": 15,
        "source_type": "Repository",
        "source": "hcoona/three",
        "enforcement": "active",
    }
    docs["rulesets"] = [summary]
    docs["ruleset-detail"] = {
        **summary,
        "target": "branch",
        "conditions": {
            "ref_name": {"include": ["refs/heads/main"], "exclude": []}
        },
        "bypass_actors": [],
        "rules": [
            {
                "type": "required_status_checks",
                "parameters": {"strict_required_status_checks_policy": True},
            }
        ],
    }
    docs["effective-rules"] = [
        {
            "ruleset_id": 15,
            "type": "required_status_checks",
            "ruleset_source_type": "Repository",
            "ruleset_source": "hcoona/three",
        }
    ]
    return docs


def test_ruleset_only_controls_retain_enforcing_details_for_review():
    """Classic 404 requires active joined rule details, never a default."""
    docs = _ruleset_controls()
    sender = Sender(docs)
    result = RubyConfigurationCollector(sender).collect_controls(
        "github-packages"
    )
    assert result.facts["classic-protection"] is None
    assert result.facts["effective-rules"] == docs["effective-rules"]
    assert result.facts["rulesets"] == [docs["ruleset-detail"]]
    assert (
        next(
            request
            for request in sender.requests
            if request.role == "ruleset-detail"
        ).resource_id
        == docs["rulesets"][0]["id"]
    )


@pytest.mark.parametrize(
    "change",
    [
        "no-effective",
        "foreign-source",
        "changed-id",
        "unsupported-condition",
        "unknown-bypass",
        "disabled",
        "too-many",
    ],
)
def test_ruleset_evidence_cannot_be_incomplete_or_unjoined(change):
    """Unsupported conditions and mismatched details stop collection."""
    docs = _ruleset_controls()
    if change == "no-effective":
        docs["effective-rules"] = []
    elif change == "foreign-source":
        docs["rulesets"][0]["source_type"] = "Organization"
    elif change == "changed-id":
        docs["ruleset-detail"]["id"] = 16
    elif change == "unsupported-condition":
        docs["ruleset-detail"]["conditions"]["repository_name"] = {}
    elif change == "unknown-bypass":
        docs["ruleset-detail"]["bypass_actors"] = [
            {"actor_id": 1, "actor_type": "Unknown", "bypass_mode": "always"}
        ]
    elif change == "disabled":
        docs["rulesets"][0]["enforcement"] = "disabled"
        docs["ruleset-detail"]["enforcement"] = "disabled"
    else:
        docs["rulesets"] = [
            {**docs["rulesets"][0], "id": index + 1} for index in range(6)
        ]
    sender = Sender(docs)
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert not any(
        request.role == "collaborators" for request in sender.requests
    )
    assert (
        sum(request.role == "ruleset-detail" for request in sender.requests)
        <= 1
    )


def test_counted_environment_inventory_rejects_incomplete_total():
    """Short successful arrays cannot override their declared total count."""
    docs = controls()
    docs["secrets"] = {"total_count": 1, "secrets": []}
    sender = Sender(docs)
    with pytest.raises(ValueError, match="incomplete"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert sender.requests[-1].role == "secrets"


def test_counted_inventory_rejects_changed_total_between_pages():
    """Pagination must preserve one stable complete count."""
    docs = controls()
    sender = Sender(docs)
    original_send = sender.send

    def send(request):
        if request.role == "secrets":
            sender.requests.append(request)
            return response(
                request,
                {
                    "total_count": 2 if request.page == 1 else 3,
                    "secrets": [
                        {"name": "first" if request.page == 1 else "second"}
                    ],
                },
                headers=(("link", page_link("secrets", 2)),)
                if request.page == 1
                else (),
            )
        return original_send(request)

    sender.send = send
    with pytest.raises(ValueError, match="total changed"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert [
        request.page for request in sender.requests if request.role == "secrets"
    ] == [1, 2]


def test_selected_package_keeps_association_separate_from_actions_access():
    """Exact association still requires independent access/UI inspection."""
    sender = Sender({"selected-package": package(target=True)})
    result = RubyConfigurationCollector(sender).collect_selected_package(
        "github-packages"
    )
    assert result.facts["repository"] == "hcoona/three"
    assert result.facts["package-id"] == 1
    assert result.facts["version-count"] == 0
    assert result.facts["requires-actions-access-ui-inspection"] is True
    assert result.facts["requires-independent-review"] is True
    assert result.response == sender.responses[0]
    assert len(sender.requests) == 1


@pytest.mark.parametrize(
    "change",
    ["404", "missing-repository", "foreign-repository", "foreign-name"],
)
def test_selected_package_never_treats_404_or_wrong_association_as_absence(
    change,
):
    """A postcreation read must establish the exact object and association."""
    doc = package(target=True)
    if change == "missing-repository":
        doc["repository"] = None
    elif change == "foreign-repository":
        doc["repository"]["full_name"] = "hcoona/other"
    elif change == "foreign-name":
        doc["name"] = "unrelated"
    reply = response(
        RubyConfigurationRequest("github-packages", "selected-package"),
        doc,
        status=404 if change == "404" else 200,
    )
    sender = Sender(scripted=[reply])
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_selected_package(
            "github-packages"
        )
    assert len(sender.requests) == 1


@pytest.mark.parametrize(
    ("timer", "source"),
    [("absent", "absent-optional-rule"), ("zero", "explicit-rule")],
)
def test_zero_wait_preserves_absent_optional_vs_explicit_rule(timer, source):
    """A complete rule array can prove no delay without a timer entry."""
    docs = controls()
    if timer == "absent":
        docs["environment"]["protection_rules"] = [
            rule
            for rule in docs["environment"]["protection_rules"]
            if rule["type"] != "wait_timer"
        ]
    sender = Sender(docs)
    result = RubyConfigurationCollector(sender).collect_controls(
        "github-packages"
    )
    assert result.facts["wait-timer"] == 0
    assert result.facts["wait-timer-source"] == source
    original = next(
        item for item in result.responses if item.request.role == "environment"
    )
    assert original.body == response(original.request, docs["environment"]).body
    assert result.facts["requires-independent-review"] is True
    assert sender.requests[-1].role == "custom-protections"


@pytest.mark.parametrize("timer", ["missing", None, True, False, "0", 1, -1])
def test_present_wait_rule_requires_actual_integer_zero(timer):
    """Malformed present timers cannot borrow absence-derived no-delay facts."""
    docs = controls()
    rule = next(
        item
        for item in docs["environment"]["protection_rules"]
        if item["type"] == "wait_timer"
    )
    if timer == "missing":
        rule.pop("wait_timer")
    else:
        rule["wait_timer"] = timer
    sender = Sender(docs)
    with pytest.raises(ValueError, match="wait timer"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert sender.requests[-1].role == "environment"


@pytest.mark.parametrize("rules", ["missing", None, {}, [], [None]])
def test_zero_wait_requires_complete_valid_protection_array(rules):
    """Missing whole protection evidence is not an absent optional timer."""
    docs = controls()
    if rules == "missing":
        docs["environment"].pop("protection_rules")
    else:
        docs["environment"]["protection_rules"] = rules
    sender = Sender(docs)
    with pytest.raises(ValueError, match="Ruby"):
        RubyConfigurationCollector(sender).collect_controls("github-packages")
    assert sender.requests[-1].role == "environment"
