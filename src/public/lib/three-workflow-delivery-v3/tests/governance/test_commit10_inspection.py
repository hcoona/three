"""Commit-10 optional read-only reviewer recovery scenarios."""

# ruff: noqa: D101, D102, D103, D107, EM101, FBT001, PLR2004, S106, SLF001, TRY003

from __future__ import annotations

import json
import subprocess
import urllib.parse
from typing import Any, cast

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.adapters.github_packages import (
    ACCEPTANCE_COORDINATES,
    ACCEPTANCE_SCENARIO_SPECS,
    GitHubPackagesHttpResponse,
)
from three_workflow_delivery_v3.governance import (
    inspection as inspection_module,
)
from three_workflow_delivery_v3.governance.inspection import (
    inspect_acceptance_reviewer,
)

ENVIRONMENT = "workflow-delivery-v3-buddy-smoke-acceptance"
RECOVERY = {
    "repository": "hcoona/three",
    "workflow_run_id": 101,
    "environment": ENVIRONMENT,
    "deployment": "run:101/environment:acceptance",
    "job": "acceptance-review",
    "artifact_id": 701,
}


def _page(
    edges: list[dict[str, Any]],
    *,
    has_next: bool = False,
    cursor: str | None = None,
) -> dict[str, Any]:
    return {
        "data": {
            "node": {
                "deploymentReviews": {
                    "edges": edges,
                    "pageInfo": {
                        "hasNextPage": has_next,
                        "endCursor": cursor,
                    },
                }
            }
        }
    }


def _page_with_environment_nodes(
    edges: list[dict[str, Any]],
    *,
    has_next: bool = False,
    cursor: str | None = None,
) -> dict[str, Any]:
    return {
        "data": {
            "node": {
                "deploymentReviews": {
                    "edges": edges,
                    "pageInfo": {
                        "hasNextPage": has_next,
                        "endCursor": cursor,
                    },
                }
            }
        }
    }


class RecordingRunner:
    def __init__(
        self,
        responses: list[dict[str, Any]],
        *,
        error_at: int | None = None,
    ) -> None:
        self.responses = responses
        self.error_at = error_at
        self.calls: list[tuple[str, ...]] = []

    def run(
        self,
        argv: tuple[str, ...],
        *,
        timeout_seconds: float,
        max_output_bytes: int,
    ) -> dict[str, Any]:
        del timeout_seconds, max_output_bytes
        self.calls.append(argv)
        if self.error_at == len(self.calls):
            raise OSError("transport failed")
        return self.responses[len(self.calls) - 1]


def _inspect(runner: RecordingRunner) -> dict[str, Any]:
    return inspect_acceptance_reviewer(
        **RECOVERY,
        runner=runner,
        timeout_seconds=7.0,
        max_output_bytes=4096,
    ).to_document()


def test_present_reviewer_uses_rest_node_then_query_only_graphql() -> None:
    runner = RecordingRunner(
        [
            {"node_id": "WFR_kwDOexample"},
            _page(
                [
                    {
                        "node": {
                            "databaseId": 9001,
                            "state": "APPROVED",
                            "user": {"login": "actual-reviewer"},
                            "environments": [{"name": ENVIRONMENT}],
                        }
                    }
                ]
            ),
        ]
    )

    document = _inspect(runner)

    assert document["status"] == "present"
    assert document["reviewer"] == "actual-reviewer"
    assert document["deployment-review-id"] == 9001
    assert document["authority"] == "diagnostic-only"
    assert runner.calls[0] == (
        "gh",
        "api",
        "--method",
        "GET",
        "repos/hcoona/three/actions/runs/101",
    )
    graphql = runner.calls[1]
    assert graphql[:5] == ("gh", "api", "graphql", "--method", "POST")
    assert "deploymentReviews(first:100,after:$cursor)" in " ".join(graphql)
    assert "run=WFR_kwDOexample" in graphql
    assert all(method not in graphql for method in ("PUT", "PATCH", "DELETE"))


def test_graphql_paginates_and_matches_exact_environment() -> None:
    runner = RecordingRunner(
        [
            {"node_id": "WFR_node"},
            _page(
                [
                    {
                        "node": {
                            "databaseId": 1,
                            "state": "APPROVED",
                            "user": {"login": "other"},
                            "environments": [{"name": "other-environment"}],
                        }
                    }
                ],
                has_next=True,
                cursor="cursor-1",
            ),
            _page(
                [
                    {
                        "node": {
                            "id": "DR_review",
                            "databaseId": 2,
                            "state": "APPROVED",
                            "user": {"login": "reviewer"},
                            "environments": [{"name": ENVIRONMENT}],
                        }
                    }
                ]
            ),
        ]
    )

    document = _inspect(runner)

    assert document["status"] == "present"
    assert document["reviewer"] == "reviewer"
    assert "cursor=cursor-1" in runner.calls[2]


def test_graphql_paginates_nested_environment_connection_to_later_node() -> (
    None
):
    runner = RecordingRunner(
        [
            {"node_id": "WFR_node"},
            _page_with_environment_nodes(
                [
                    {
                        "node": {
                            "id": "DR_review",
                            "databaseId": 2,
                            "state": "APPROVED",
                            "user": {"login": "reviewer"},
                            "environments": {
                                "nodes": [{"name": "other-environment"}],
                                "pageInfo": {
                                    "hasNextPage": True,
                                    "endCursor": "environment-cursor-1",
                                },
                            },
                        }
                    }
                ],
            ),
            {
                "data": {
                    "node": {
                        "environments": {
                            "nodes": [{"name": ENVIRONMENT}],
                            "pageInfo": {
                                "hasNextPage": False,
                                "endCursor": None,
                            },
                        }
                    }
                }
            },
        ]
    )

    document = _inspect(runner)
    first_graphql = " ".join(runner.calls[1])
    second_graphql = " ".join(runner.calls[2])

    assert document["status"] == "present"
    assert document["reviewer"] == "reviewer"
    assert document["deployment-review-id"] == 2
    assert "environments(first:100,after:$environmentCursor)" in first_graphql
    assert "environments{name}" not in first_graphql
    assert "nodes{name}" in first_graphql
    assert "pageInfo{hasNextPage endCursor}" in first_graphql
    assert "environmentCursor=null" in runner.calls[1]
    assert "cursor=null" in runner.calls[1]
    assert "environmentCursor=environment-cursor-1" in runner.calls[2]
    assert "cursor=environment-cursor-1" not in runner.calls[2]
    assert "review=DR_review" in runner.calls[2]
    assert "node(id:$review)" in second_graphql
    assert "deploymentReviews(first:100,after:$cursor)" not in second_graphql


def test_exhausted_connection_is_removed_not_universal_negative() -> None:
    runner = RecordingRunner([{"node_id": "WFR_node"}, _page([])])

    document = _inspect(runner)

    assert document["status"] == "removed"
    assert document["human-required"] is True
    assert document["diagnostics"] == [
        "scoped-review-record-no-longer-available"
    ]
    assert "universal-negative-proof" not in document


@pytest.mark.parametrize(
    "responses",
    [
        [{}],
        [{"node_id": "WFR_node"}, {"data": {}}],
        [
            {"node_id": "WFR_node"},
            _page([], has_next=True, cursor=None),
        ],
    ],
)
def test_malformed_api_shapes_are_unknown(
    responses: list[dict[str, Any]],
) -> None:
    document = _inspect(RecordingRunner(responses))

    assert document["status"] == "unknown"
    assert document["reviewer"] is None
    assert document["human-required"] is True
    assert document["authority"] == "diagnostic-only"


def test_transport_failure_is_unknown_and_never_grants_authority() -> None:
    document = _inspect(RecordingRunner([{"node_id": "WFR_node"}], error_at=2))

    assert document["status"] == "unknown"
    serialized = str(document).lower()
    assert "capability" not in serialized
    assert "live_enabled" not in serialized


def test_subprocess_timeout_is_unknown_and_diagnostic_only() -> None:
    class TimeoutRunner(RecordingRunner):
        def run(
            self,
            argv: tuple[str, ...],
            *,
            timeout_seconds: float,
            max_output_bytes: int,
        ) -> dict[str, Any]:
            del timeout_seconds, max_output_bytes
            self.calls.append(argv)
            raise subprocess.TimeoutExpired(argv, 7.0)

    document = _inspect(TimeoutRunner([]))

    assert document["status"] == "unknown"
    assert document["reviewer"] is None
    assert document["deployment-review-id"] is None
    assert document["human-required"] is True
    assert document["authority"] == "diagnostic-only"


@pytest.mark.parametrize(
    "required",
    [
        "status",
        "reviewer",
        "deployment-review-id",
        "authority",
        "scope",
    ],
)
def test_reviewer_inspection_present_is_read_only_and_scoped(
    required: str,
) -> None:
    runner = RecordingRunner(
        [
            {"node_id": "WFR_kwDOexample"},
            _page(
                [
                    {
                        "node": {
                            "databaseId": 9001,
                            "state": "APPROVED",
                            "user": {"login": "actual-reviewer"},
                            "environments": [{"name": ENVIRONMENT}],
                        }
                    }
                ]
            ),
        ]
    )

    document = _inspect(runner)

    assert required in document
    assert document["authority"] == "diagnostic-only"
    assert all("mutation" not in part for call in runner.calls for part in call)


@pytest.mark.parametrize("field", ["human-required", "diagnostics"])
def test_reviewer_inspection_removed_is_not_universal_negative_proof(
    field: str,
) -> None:
    document = _inspect(RecordingRunner([{"node_id": "WFR_node"}, _page([])]))

    assert field in document
    assert document["status"] == "removed"
    assert "universal-negative-proof" not in document


@pytest.mark.parametrize(
    "responses",
    [
        [{}],
        [{"node_id": "WFR_node"}, {"data": {}}],
    ],
)
def test_reviewer_inspection_errors_are_unknown_and_human_required(
    responses: list[dict[str, Any]],
) -> None:
    document = _inspect(RecordingRunner(responses))

    assert document["status"] == "unknown"
    assert document["human-required"] is True
    assert document["authority"] == "diagnostic-only"


def test_reviewer_inspection_cannot_grant_capability_or_enable_live() -> None:
    document = _inspect(RecordingRunner([{"node_id": "WFR_node"}, _page([])]))
    serialized = str(document).lower()

    assert "capability" not in serialized
    assert "live_enabled" not in serialized
    assert "authorization" not in serialized


@pytest.mark.parametrize(
    "extra",
    [
        "capability",
        "live_enabled",
        "mutation-started",
        "authorization",
        "receipt",
        "attempt",
        "universal-negative-proof",
        "release-lineage",
    ],
)
def test_reviewer_inspection_contract_rejects_every_unrecognized_extra_key(
    extra: str,
) -> None:
    document = _inspect(RecordingRunner([{"node_id": "WFR_node"}, _page([])]))
    allowed = {
        "schema",
        "status",
        "reviewer",
        "deployment-review-id",
        "human-required",
        "diagnostics",
        "authority",
        "scope",
        "recovery",
    }

    assert extra not in allowed
    assert set(document) <= allowed


def test_nested_environment_pagination_does_not_skip_later_review_edges() -> (
    None
):
    runner = RecordingRunner(
        [
            {"node_id": "WFR_node"},
            _page_with_environment_nodes(
                [
                    {
                        "node": {
                            "id": "DR_first",
                            "databaseId": 1,
                            "state": "APPROVED",
                            "user": {"login": "first-reviewer"},
                            "environments": {
                                "nodes": [{"name": "other-environment"}],
                                "pageInfo": {
                                    "hasNextPage": True,
                                    "endCursor": "first-environment-next",
                                },
                            },
                        }
                    },
                    {
                        "node": {
                            "id": "DR_second",
                            "databaseId": 2,
                            "state": "APPROVED",
                            "user": {"login": "second-reviewer"},
                            "environments": {
                                "nodes": [{"name": ENVIRONMENT}],
                                "pageInfo": {
                                    "hasNextPage": False,
                                    "endCursor": None,
                                },
                            },
                        }
                    },
                ]
            ),
        ]
    )

    document = _inspect(runner)

    assert document["status"] == "present"
    assert document["reviewer"] == "second-reviewer"
    assert document["deployment-review-id"] == 2
    assert len(runner.calls) == 2


@pytest.mark.parametrize("non_approved_state", ["REJECTED", "PENDING"])
def test_matching_non_approved_review_is_skipped_for_later_approved_match(
    non_approved_state: str,
) -> None:
    runner = RecordingRunner(
        [
            {"node_id": "WFR_node"},
            _page_with_environment_nodes(
                [
                    {
                        "node": {
                            "id": "DR_rejected",
                            "databaseId": 1,
                            "state": non_approved_state,
                            "user": {"login": "rejected-reviewer"},
                            "environments": {
                                "nodes": [{"name": ENVIRONMENT}],
                                "pageInfo": {
                                    "hasNextPage": False,
                                    "endCursor": None,
                                },
                            },
                        }
                    },
                    {
                        "node": {
                            "id": "DR_approved",
                            "databaseId": 2,
                            "state": "APPROVED",
                            "user": {"login": "approved-reviewer"},
                            "environments": {
                                "nodes": [{"name": ENVIRONMENT}],
                                "pageInfo": {
                                    "hasNextPage": False,
                                    "endCursor": None,
                                },
                            },
                        }
                    },
                ]
            ),
        ]
    )

    document = _inspect(runner)

    assert document["status"] == "present"
    assert document["reviewer"] == "approved-reviewer"
    assert document["deployment-review-id"] == 2


def test_nested_environment_pagination_is_scoped_to_one_specific_review() -> (
    None
):
    targeted_page = {
        "data": {
            "node": {
                "environments": {
                    "nodes": [{"name": ENVIRONMENT}],
                    "pageInfo": {
                        "hasNextPage": False,
                        "endCursor": None,
                    },
                },
                "deploymentReviews": {
                    "edges": [
                        {
                            "node": {
                                "id": "DR_first",
                                "databaseId": 1,
                                "state": "APPROVED",
                                "user": {"login": "first-reviewer"},
                                "environments": {
                                    "nodes": [{"name": ENVIRONMENT}],
                                    "pageInfo": {
                                        "hasNextPage": False,
                                        "endCursor": None,
                                    },
                                },
                            }
                        }
                    ],
                    "pageInfo": {
                        "hasNextPage": False,
                        "endCursor": None,
                    },
                },
            }
        }
    }
    runner = RecordingRunner(
        [
            {"node_id": "WFR_node"},
            _page_with_environment_nodes(
                [
                    {
                        "node": {
                            "id": "DR_first",
                            "databaseId": 1,
                            "state": "APPROVED",
                            "user": {"login": "first-reviewer"},
                            "environments": {
                                "nodes": [{"name": "other-environment"}],
                                "pageInfo": {
                                    "hasNextPage": True,
                                    "endCursor": "first-environment-next",
                                },
                            },
                        }
                    },
                    {
                        "node": {
                            "id": "DR_second",
                            "databaseId": 2,
                            "state": "APPROVED",
                            "user": {"login": "second-reviewer"},
                            "environments": {
                                "nodes": [{"name": "other-environment"}],
                                "pageInfo": {
                                    "hasNextPage": False,
                                    "endCursor": None,
                                },
                            },
                        }
                    },
                ]
            ),
            targeted_page,
        ]
    )

    document = _inspect(runner)
    nested_call = runner.calls[2]
    query = " ".join(nested_call)

    assert document["status"] == "present"
    assert document["reviewer"] == "first-reviewer"
    assert "review=DR_first" in nested_call
    assert "environmentCursor=first-environment-next" in nested_call
    assert "node(id:$review)" in query
    assert "deploymentReviews(first:100" not in query
    assert "cursor=first-environment-next" not in nested_call


def test_adversarial_reviewer_pages_and_nested_waits_share_one_deadline(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeClock:
        now = 100.0

        def monotonic(self) -> float:
            return self.now

    clock = FakeClock()
    monkeypatch.setattr(
        inspection_module,
        "monotonic",
        clock.monotonic,
        raising=False,
    )

    class DeadlineRunner(RecordingRunner):
        def __init__(self) -> None:
            super().__init__(
                [
                    {"node_id": "WFR_node"},
                    _page_with_environment_nodes(
                        [
                            {
                                "node": {
                                    "id": "DR_review",
                                    "databaseId": 2,
                                    "state": "APPROVED",
                                    "user": {"login": "reviewer"},
                                    "environments": {
                                        "nodes": [
                                            {"name": "other-environment"}
                                        ],
                                        "pageInfo": {
                                            "hasNextPage": True,
                                            "endCursor": "environment-page-2",
                                        },
                                    },
                                }
                            }
                        ],
                        has_next=True,
                        cursor="review-page-2",
                    ),
                    {
                        "data": {
                            "node": {
                                "environments": {
                                    "nodes": [{"name": "still-other"}],
                                    "pageInfo": {
                                        "hasNextPage": True,
                                        "endCursor": "environment-page-3",
                                    },
                                }
                            }
                        }
                    },
                ]
            )
            self.timeouts: list[float] = []

        def run(
            self,
            argv: tuple[str, ...],
            *,
            timeout_seconds: float,
            max_output_bytes: int,
        ) -> dict[str, Any]:
            self.timeouts.append(timeout_seconds)
            response = super().run(
                argv,
                timeout_seconds=timeout_seconds,
                max_output_bytes=max_output_bytes,
            )
            clock.now += 3.0
            return response

    runner = DeadlineRunner()
    document = _inspect(runner)

    assert document["status"] == "unknown"
    assert document["reviewer"] is None
    assert runner.timeouts == pytest.approx([7.0, 4.0, 1.0])
    assert len(runner.calls) == 3
    assert clock.now == 109.0


def test_adversarial_package_and_version_pages_share_one_deadline(
    tmp_path: Any,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class FakeClock:
        now = 20.0

        def monotonic(self) -> float:
            return self.now

    clock = FakeClock()
    monkeypatch.setattr(cli_module, "monotonic", clock.monotonic, raising=False)
    timeouts: list[float] = []
    calls: list[str] = []

    class DeadlineTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, max_bytes
            calls.append(url)
            timeouts.append(timeout)
            if len(calls) > 3:
                pytest.fail("transport called after operation deadline")
            clock.now += 3.0
            body: object
            if "/versions" not in url:
                body = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
            else:
                body = [{"name": f"other-{index}"} for index in range(100)]
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
                truncated=False,
                complete=True,
            )

    tags = {
        scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS
    }
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha="c" * 40,
    )
    transport._transport = DeadlineTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        tags["exact"],
        timeout_seconds=7.0,
        max_response_bytes=8192,
    )

    assert observation["state"] == "unknown"
    assert timeouts == pytest.approx([7.0, 4.0, 1.0])
    assert len(calls) == 3
    assert clock.now == 29.0


@pytest.mark.parametrize(
    ("truncated", "complete", "expected_state"),
    [
        (False, True, "absent"),
        (True, True, "unknown"),
        (False, False, "unknown"),
        (True, False, "unknown"),
    ],
)
def test_adversarial_package_404_requires_complete_non_truncated_response(
    tmp_path: Any,
    truncated: bool,
    complete: bool,
    expected_state: str,
) -> None:
    class Package404Transport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            return GitHubPackagesHttpResponse(
                status=404,
                url=url,
                headers=(),
                body=b'{"message":"Not Found"}',
                truncated=truncated,
                complete=complete,
            )

    tags = {
        scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS
    }
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha="c" * 40,
    )
    transport._transport = Package404Transport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        tags["exact"],
        timeout_seconds=7.0,
        max_response_bytes=8192,
    )

    assert observation["state"] == expected_state
    assert cast("str", observation["response-identity-digest"]).startswith(
        "sha256:"
    )


@pytest.mark.parametrize(
    ("terminal_truncated", "terminal_complete", "expected_state"),
    [
        (False, True, "absent"),
        (True, True, "unknown"),
        (False, False, "unknown"),
        (True, False, "unknown"),
    ],
)
def test_adversarial_version_absence_requires_terminal_complete_page(
    tmp_path: Any,
    terminal_truncated: bool,
    terminal_complete: bool,
    expected_state: str,
) -> None:
    calls: list[str] = []

    class VersionPagesTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            calls.append(url)
            page = urllib.parse.parse_qs(urllib.parse.urlsplit(url).query).get(
                "page"
            )
            if page is None:
                body: object = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
                truncated = False
                complete = True
            elif page == ["1"]:
                body = [{"name": f"other-{index}"} for index in range(100)]
                truncated = False
                complete = True
            else:
                body = [{"name": "terminal-other-version"}]
                truncated = terminal_truncated
                complete = terminal_complete
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
                truncated=truncated,
                complete=complete,
            )

    tags = {
        scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS
    }
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha="c" * 40,
    )
    transport._transport = VersionPagesTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        tags["exact"],
        timeout_seconds=7.0,
        max_response_bytes=8192,
    )

    assert observation["state"] == expected_state
    assert len(calls) == 3
    assert calls[-1].endswith("per_page=100&page=2")


def test_adversarial_full_version_pages_without_terminal_proof_are_unknown(
    tmp_path: Any,
) -> None:
    calls: list[str] = []

    class AlwaysFullTransport:
        def get(
            self,
            url: str,
            *,
            headers: tuple[tuple[str, str], ...],
            timeout: float,
            max_bytes: int,
        ) -> GitHubPackagesHttpResponse:
            del headers, timeout, max_bytes
            calls.append(url)
            body: object
            if "/versions" not in url:
                body = {
                    "package_type": "npm",
                    "name": "hcoona-release-smoke-npm",
                    "owner": {"login": "hcoona"},
                    "repository": {"full_name": "hcoona/three"},
                }
            else:
                body = [{"name": f"other-{index}"} for index in range(100)]
            return GitHubPackagesHttpResponse(
                status=200,
                url=url,
                headers=(),
                body=json.dumps(body).encode(),
                truncated=False,
                complete=True,
            )

    tags = {
        scenario: tag for scenario, _version, tag in ACCEPTANCE_SCENARIO_SPECS
    }
    transport = cli_module._AcceptanceNpmTransport(
        tmp_path / ".npmrc",
        token="dedicated-token",
        target_sha="c" * 40,
    )
    transport._transport = AlwaysFullTransport()

    observation = transport.observe(
        ACCEPTANCE_COORDINATES["exact"],
        tags["exact"],
        timeout_seconds=7.0,
        max_response_bytes=8192,
    )

    assert observation["state"] == "unknown"
    assert len(calls) == 101


@pytest.mark.parametrize(
    "page_info",
    [
        {},
        {"hasNextPage": True, "endCursor": None},
    ],
)
def test_adversarial_reviewer_absence_requires_terminal_pagination_proof(
    page_info: dict[str, Any],
) -> None:
    full_nonmatching_page = _page(
        [
            {
                "node": {
                    "databaseId": index,
                    "state": "APPROVED",
                    "user": {"login": f"reviewer-{index}"},
                    "environments": [{"name": "other-environment"}],
                }
            }
            for index in range(100)
        ]
    )
    full_nonmatching_page["data"]["node"]["deploymentReviews"]["pageInfo"] = (
        page_info
    )

    document = _inspect(
        RecordingRunner([{"node_id": "WFR_node"}, full_nonmatching_page])
    )

    assert document["status"] == "unknown"
    assert document["reviewer"] is None
    assert document["deployment-review-id"] is None
    assert document["diagnostics"] == [
        "reviewer-lookup-failed-human-inspection-required"
    ]
