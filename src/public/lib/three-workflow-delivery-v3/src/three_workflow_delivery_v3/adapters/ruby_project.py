"""Whole-project RubyGems observation, distinct from version availability."""

from __future__ import annotations

import http.client
from dataclasses import dataclass
from http import HTTPStatus
from typing import TYPE_CHECKING

from three_workflow_delivery_v3._ruby_native import RUBY_RELEASE_UNIT
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    ruby_response_document,
)
from three_workflow_delivery_v3.adapters.rubygems import RUBY_INDEX_LIMIT
from three_workflow_delivery_v3.canonical import JsonValue, parse_json_strict

if TYPE_CHECKING:
    from three_workflow_delivery_v3.adapters.ruby_registry import (
        RubyRequestBudget,
    )

RUBYGEMS_PROJECT_URL = (
    f"https://rubygems.org/api/v1/gems/{RUBY_RELEASE_UNIT}/owners.json"
)
_ABSENT_BODY = b"This rubygem could not be found."


def classify_rubygems_project(response: RubyHttpResponse) -> str:
    """Admit absence only from the supported direct project lookup.

    An empty owner array still means that a project object exists. This does
    not validate ownership or publisher registration for normal admission.
    """
    if (
        type(response.status) is not int
        or type(response.body) is not bytes
        or len(response.body) > RUBY_INDEX_LIMIT
        or type(response.content_type) is not str
    ):
        return "unknown"
    media = response.content_type.split(";", 1)[0].strip().lower()
    if response.status == HTTPStatus.NOT_FOUND:
        return (
            "absent"
            if media == "text/plain" and response.body == _ABSENT_BODY
            else "unknown"
        )
    if response.status != HTTPStatus.OK or media != "application/json":
        return "unknown"
    try:
        owners = parse_json_strict(response.body)
    except (ValueError, TypeError, UnicodeError):
        return "unknown"
    # The endpoint's successful array establishes project existence. Unknown
    # owner fields do not establish account identity, but cannot mean absence.
    return (
        "present"
        if isinstance(owners, list)
        and all(isinstance(owner, dict) for owner in owners)
        else "unknown"
    )


@dataclass(frozen=True, slots=True)
class RubyGemsProjectObservation:
    """One original public response or one failed send; never ownership."""

    response: RubyHttpResponse | None
    failure: str | None = None

    def __post_init__(self) -> None:
        """Reject contradictory response/failure records."""
        if (self.response is None) != (self.failure is not None) or (
            self.failure is not None
            and self.failure not in {"HTTPException", "OSError", "ValueError"}
        ):
            message = "RubyGems project observation has contradictory evidence"
            raise ValueError(message)

    @property
    def classification(self) -> str:
        """Derive a conclusion from retained evidence, never caller input."""
        return (
            "unknown"
            if self.response is None
            else classify_rubygems_project(self.response)
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Retain complete evidence privately; owner data needs screening."""
        return {
            "schema": "workflow-delivery/v3/rubygems-project-observation-v1",
            "url": RUBYGEMS_PROJECT_URL,
            "classification": self.classification,
            "failure": self.failure,
            "response": None
            if self.response is None
            else ruby_response_document(self.response),
        }


def observe_rubygems_project(
    budget: RubyRequestBudget,
) -> RubyGemsProjectObservation:
    """Spend one request from the caller's admitted phase, without retry."""
    try:
        response = budget.request(
            "GET",
            RUBYGEMS_PROJECT_URL,
            {
                "Accept": "application/json",
                "User-Agent": "three-workflow-delivery-v3",
            },
            None,
            RUBY_INDEX_LIMIT,
        )
    except http.client.HTTPException:
        return RubyGemsProjectObservation(None, "HTTPException")
    except OSError:
        return RubyGemsProjectObservation(None, "OSError")
    except ValueError:
        return RubyGemsProjectObservation(None, "ValueError")
    return RubyGemsProjectObservation(response)
