"""Controlled GitHub response fixtures, without credentials or HTTP effects."""

from copy import deepcopy

from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.platform.ruby_configuration_http import (
    RubyConfigurationRequest,
    RubyConfigurationResponse,
)

from ..ruby_integration_fixtures import NOW, TARGET

OWNER = {"login": "hcoona", "id": 712433}
DESTINATION = "github-packages"


def response(  # noqa: PLR0913 - original response dimensions
    request, document=None, *, status=200, headers=(), body=None, failure=None
):
    """Retain canonical synthetic original bytes at the sender seam."""
    return RubyConfigurationResponse(
        request,
        "fixture-durable-receipt",
        NOW,
        NOW,
        status,
        () if failure else (("content-type", "application/json"), *headers),
        canonicalize(document)
        if body is None and not failure
        else (body or b""),
        failure,
    )


def principal(destination=DESTINATION, *, scopes="read:packages"):
    """Model an existing successful principal response, not a new send."""
    headers = () if scopes is None else (("x-oauth-scopes", scopes),)
    return response(
        RubyConfigurationRequest(destination, "principal"),
        OWNER,
        headers=headers,
    )


def repository():
    """Provide exact repository native identity."""
    return {
        "id": 999,
        "name": "three",
        "full_name": "hcoona/three",
        "owner": dict(OWNER),
    }


def package(identifier=1, *, target=False):
    """Retain private zero-version package objects as present projects."""
    return {
        "id": identifier,
        "name": "hcoona-release-smoke-ruby"
        if target
        else f"unrelated-private-{identifier}",
        "package_type": "rubygems",
        "owner": dict(OWNER),
        "visibility": "private",
        "version_count": 0,
        "repository": repository(),
    }


def controls(destination=DESTINATION):
    """Minimal complete classic-protection control inspection."""
    environment = "workflow-delivery-v3-ruby-" + destination
    return {
        "principal": dict(OWNER),
        "repository": repository(),
        "main": {"name": "main", "protected": True, "commit": {"sha": TARGET}},
        "protection": {
            "required_status_checks": {"strict": True, "contexts": ["CI"]}
        },
        "effective-rules": [],
        "rulesets": [],
        "collaborators": [
            {
                **OWNER,
                "permissions": {"push": True, "maintain": False, "admin": True},
            }
        ],
        "environment": {
            "id": 101,
            "name": environment,
            "can_admins_bypass": False,
            "deployment_branch_policy": {
                "protected_branches": False,
                "custom_branch_policies": True,
            },
            "protection_rules": [
                {
                    "id": 102,
                    "type": "required_reviewers",
                    "prevent_self_review": False,
                    "reviewers": [{"type": "User", "reviewer": dict(OWNER)}],
                },
                {"id": 103, "type": "wait_timer", "wait_timer": 0},
            ],
        },
        "secrets": {"total_count": 0, "secrets": []},
        "variables": {
            "total_count": 1,
            "variables": [
                {
                    "name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
                    "value": environment + "/v1",
                }
            ],
        },
        "branch-policies": {
            "total_count": 1,
            "branch_policies": [{"id": 104, "name": "main", "type": "branch"}],
        },
        "custom-protections": {
            "total_count": 0,
            "custom_deployment_protection_rules": [],
        },
    }


class Sender:
    """Route scripted originals and fail immediately on unexpected sends."""

    def __init__(self, documents=None, *, scripted=()):
        """Use per-role documents or exact preconstructed response sequences."""
        self.documents = deepcopy(documents or {})
        self.scripted = list(scripted)
        self.requests = []
        self.responses = []

    def send(self, request):
        """Record the exact requested role/page without touching a network."""
        self.requests.append(request)
        if self.scripted:
            result = self.scripted.pop(0)
        else:
            value = self.documents[request.role]
            result = (
                value
                if isinstance(value, RubyConfigurationResponse)
                else response(request, value)
            )
        self.responses.append(result)
        return result


def page_link(role, page, *, relation="next", destination=DESTINATION):
    """Select the official fixed endpoint continuation for a known page."""
    request = RubyConfigurationRequest(destination, role, page)
    return (
        "<https://api.github.com" + request.path + '>; rel="' + relation + '"'
    )
