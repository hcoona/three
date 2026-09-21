"""Commit-10 optional read-only reviewer recovery scenarios."""

# ruff: noqa: D101, D102, D103, D107, EM101, PLR2004, TRY003

from __future__ import annotations

import subprocess
from typing import Any

import pytest
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
    assert document["scope"] == "single-acceptance-review-recovery"
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
    assert all("mutation" not in part for call in runner.calls for part in call)


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
    assert set(document) <= allowed
    serialized = str(document).lower()
    assert "capability" not in serialized
    assert "live_enabled" not in serialized
    assert "authorization" not in serialized


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
