"""Bootstrap protected execution identity and bounded approval proof facts."""

import pytest
from three_workflow_delivery_v3.acceptance import python_native_github
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    SENTINEL,
    WORKFLOW,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    prove_approval,
    validate_hosted_targets,
)
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform.python_github import (
    PythonGitHubRuntime,
)

# Protocol constants are kept visible in boundary assertions.
# ruff: noqa: PLR2004
from ..adapters.test_pypi import FakeHttp
from . import test_python_bootstrap_fixture as fixture_tests
from .test_python_bootstrap_fixture import RUN, TOOLING, bootstrap_request
from .test_python_native_hosted import github_facts, hosted_environment

bootstrap_fixtures = fixture_tests.bootstrap_fixtures


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("GITHUB_ACTOR", "Backspace7980"),
        ("GITHUB_ACTOR_ID", "17"),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("GITHUB_RUN_ID", "01"),
        ("GITHUB_REPOSITORY_ID", "17"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/topic"),
        ("GITHUB_REF_PROTECTED", "false"),
        ("GITHUB_WORKFLOW_SHA", "d" * 40),
        ("RUNNER_OS", "Windows"),
    ],
)
def test_bootstrap_hosted_context_rejects_foreign_or_repeated_execution(
    bootstrap_fixtures, tmp_path, monkeypatch, key, value
):
    """TestPyPI account identity is distinct from the one.

    TestPyPI account identity is distinct from the one accepted GitHub
    owner.
    """
    request = bootstrap_request(bootstrap_fixtures)
    environment = hosted_environment()
    environment["GITHUB_WORKFLOW_REF"] = (
        f"hcoona/three/{WORKFLOW}@refs/heads/main"
    )
    monkeypatch.setattr(
        python_native_github, "git_output", lambda *_args: TOOLING
    )
    assert (
        validate_hosted_targets(
            tmp_path,
            environment,
            TOOLING,
            (request.source["commit"],),
            WORKFLOW,
        )
        == RUN
    )
    environment[key] = value
    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        validate_hosted_targets(
            tmp_path,
            environment,
            TOOLING,
            (request.source["commit"],),
            WORKFLOW,
        )


@pytest.mark.parametrize("suffix", ["", "@main"])
def test_bootstrap_approval_proofs_preserve_original_current_run_facts(
    bootstrap_fixtures, suffix
):
    """Both proof passes use the same exact bounded raw.

    Both proof passes use the same exact bounded raw current-run
    authority.
    """
    request = bootstrap_request(bootstrap_fixtures)
    facts = github_facts(request, deployment_count=5)
    facts[0][1]["path"] = WORKFLOW + suffix
    http = FakeHttp(
        *[
            PythonHttpResponse(200, canonicalize(value), "application/json")
            for _, value in facts
        ]
    )
    proof = parse_canonical_json(
        prove_approval(
            request,
            RUN,
            TOOLING,
            SENTINEL,
            PythonGitHubRuntime("synthetic-proof", http),
            WORKFLOW,
        )
    )
    assert proof["request-digest"] == request.digest
    assert proof["run"] == facts[0][1]
    assert proof["reviews"] == facts[1][1]
    assert proof["deployments"] == facts[2][1]
    assert proof["deployment-id"] == 100
    assert [call[1] for call in http.calls] == [
        "https://api.github.com" + route for route, _ in facts
    ]
    assert len(http.calls) == 8


@pytest.mark.parametrize(
    "change",
    ["actor", "approver", "path", "rerun", "sentinel", "too-many-deployments"],
)
def test_bootstrap_approval_rejects_foreign_or_unbounded_facts(
    bootstrap_fixtures, change
):
    """Neither account labels nor repeated/foreign proofs can admit a send."""
    request = bootstrap_request(bootstrap_fixtures)
    facts = github_facts(
        request, deployment_count=6 if change == "too-many-deployments" else 1
    )
    facts[0][1]["path"] = WORKFLOW
    if change == "actor":
        facts[0][1]["actor"]["login"] = "Backspace7980"
    elif change == "approver":
        facts[1][1][0]["user"]["login"] = "Backspace7980"
    elif change == "path":
        facts[0][1]["path"] = WORKFLOW + "@topic"
    elif change == "rerun":
        facts[0][1]["run_attempt"] = 2
    http = FakeHttp(
        *[
            PythonHttpResponse(200, canonicalize(value), "application/json")
            for _, value in facts
        ]
    )
    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        prove_approval(
            request,
            RUN,
            TOOLING,
            "foreign" if change == "sentinel" else SENTINEL,
            PythonGitHubRuntime("synthetic-proof", http),
            WORKFLOW,
        )
    expected = {
        "actor": 1,
        "approver": 2,
        "path": 1,
        "rerun": 1,
        "sentinel": 0,
        "too-many-deployments": 3,
    }
    assert len(http.calls) == expected[change]
