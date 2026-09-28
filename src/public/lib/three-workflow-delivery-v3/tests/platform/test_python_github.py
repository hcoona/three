"""Fake-only current GitHub configuration, approval and OIDC boundaries."""

from copy import deepcopy
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_RESPONSE_BYTES,
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform import python_github
from three_workflow_delivery_v3.platform.python_github import (
    PythonGitHubRuntime,
    obtain_python_oidc_assertion,
)
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_WORKFLOW,
    blocked_python_governance,
)

from ..adapters.test_pypi import FakeHttp
from ..python_fixtures import NOW, RUN_ID, TARGET, governance, qualification

_REPO = "/repos/hcoona/three"
_TOKEN = "test-only-job-token"  # noqa: S105
_USER = {"id": 712433, "login": "hcoona"}
_PAGE_SIZE = 100
_MAX_PAGES = 5


@pytest.fixture(autouse=True)
def deny_real_services(monkeypatch):
    """Any accidental native HTTP or Git Governance read fails locally."""

    def denied(*_args, **_kwargs):
        pytest.fail("Native HTTP or Governance Git access is forbidden")

    monkeypatch.setattr(
        "three_workflow_delivery_v3.adapters.pypi.http.client.HTTPSConnection",
        denied,
    )
    monkeypatch.setattr(python_github, "IsolatedGovernanceGitReader", denied)


def _response(document):
    return PythonHttpResponse(200, canonicalize(document), "application/json")


def _runtime(*documents):
    transport = FakeHttp(*(_response(d) for d in documents))
    return PythonGitHubRuntime(_TOKEN, transport, clock=lambda: NOW), transport


def _configuration():
    return [
        {"full_name": "hcoona/three", "owner": deepcopy(_USER)},
        {"protected": True},
        [
            {
                **_USER,
                "permissions": {"push": True, "maintain": True, "admin": True},
            }
        ],
    ]


def _approval(name="testpypi"):
    admitted = governance(name)
    env = admitted.registry.environment
    return [
        {
            "head_sha": TARGET,
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": PYTHON_WORKFLOW,
            "actor": deepcopy(_USER),
        },
        [
            {
                "environments": [{"id": 1901, "name": env}],
                "state": "approved",
                "user": deepcopy(_USER),
            }
        ],
        [{"id": 2001, "sha": TARGET, "environment": env}],
        [
            {
                "state": "in_progress",
                "log_url": f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/job/9001",
            }
        ],
    ]


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_github_configuration_reads_exact_current_controls(name):
    """Both destinations independently read exact GitHub protection controls."""
    documents = _configuration()
    runtime, transport = _runtime(*documents)
    runtime.configuration(governance(name))
    assert len(transport.calls) == len(documents)
    assert all(c[0] == "GET" and c[3] is None for c in transport.calls)
    assert transport.calls[0][1] == "https://api.github.com" + _REPO
    assert all(
        c[2]["Authorization"] == "Bearer " + _TOKEN for c in transport.calls
    )
    assert all(
        c[2]["X-GitHub-Api-Version"] == "2022-11-28" for c in transport.calls
    )
    assert all(c[4] == 8 * 1024 * 1024 for c in transport.calls)
    assert [c[1] for c in transport.calls] == [
        "https://api.github.com" + _REPO,
        "https://api.github.com" + _REPO + "/branches/main",
        "https://api.github.com"
        + _REPO
        + "/collaborators?affiliation=all&per_page=100&page=1",
    ]


@pytest.mark.parametrize(
    "change", ["repository", "owner", "unprotected", "writer", "writer-shape"]
)
def test_python_github_configuration_rejects_changed_control_surface(change):
    """Runtime identity and accepted writer drift fail closed."""
    docs = _configuration()
    mutations = {
        "repository": ((0, "full_name"), "other/three"),
        "owner": ((0, "owner", "id"), 1),
        "unprotected": ((1, "protected"), False),
        "writer": (
            (2,),
            [
                {
                    "id": 1,
                    "login": "other",
                    "permissions": {
                        "push": True,
                        "maintain": False,
                        "admin": False,
                    },
                }
            ],
        ),
        "writer-shape": ((2,), [{"login": "other"}]),
    }
    path, value = mutations[change]
    node = docs
    for part in path[:-1]:
        node = node[part]
    node[path[-1]] = value
    runtime, transport = _runtime(*docs)
    with pytest.raises((ValueError, TypeError), match="Python"):
        runtime.configuration(governance())
    assert all(c[0] == "GET" for c in transport.calls)


@pytest.mark.parametrize(
    "path",
    [
        "/repos/other/three",
        "/repos/hcoona/three-fork",
        "https://api.github.com/repos/hcoona/three",
        "/user",
    ],
)
def test_python_github_rejects_other_repository_before_send(path):
    """Job credentials stay confined to the admitted GitHub repository."""
    runtime, transport = _runtime()
    with pytest.raises(ValueError, match="outside its exact repository"):
        runtime.get(path)
    assert transport.calls == []


@pytest.mark.parametrize("status", [301, 403, 404, 429, 500])
def test_python_github_api_failure_is_not_retried(status):
    """Redirects and server errors never become missing configuration."""
    transport = FakeHttp(PythonHttpResponse(status, b"error", "text/plain"))
    runtime = PythonGitHubRuntime(_TOKEN, transport)
    with pytest.raises(ValueError, match="control read failed"):
        runtime.get(_REPO)
    assert len(transport.calls) == 1


def test_python_github_pagination_is_complete_and_preserves_query():
    """A full first page requires another page before absence is decided."""
    first = [{"id": i} for i in range(_PAGE_SIZE)]
    runtime, transport = _runtime(
        {"items": first}, {"items": [{"id": _PAGE_SIZE}]}
    )
    assert runtime.pages(_REPO + "/items?filter=all", "items") == [
        *first,
        {"id": _PAGE_SIZE},
    ]
    assert [c[1] for c in transport.calls] == [
        f"https://api.github.com{_REPO}/items?filter=all&per_page=100&page={page}"
        for page in (1, 2)
    ]


def test_python_github_page_budget_exhaustion_never_returns_partial_inventory():
    """Five full pages fail closed instead of concealing later writers."""
    runtime, transport = _runtime(
        *([{}] * _PAGE_SIZE for _ in range(_MAX_PAGES))
    )
    with pytest.raises(ValueError, match="page budget"):
        runtime.pages(_REPO + "/collaborators")
    assert len(transport.calls) == _MAX_PAGES


@pytest.mark.parametrize("document", [{}, {"items": {}}, None])
def test_python_github_rejects_malformed_inventory(document):
    """Unexpected response shapes cannot be interpreted as an empty list."""
    runtime, transport = _runtime(document)
    with pytest.raises(TypeError, match="inventory is malformed"):
        runtime.pages(_REPO + "/items", "items")
    assert len(transport.calls) == 1


@pytest.mark.parametrize("name", ["testpypi", "pypi"])
def test_python_github_approval_binds_current_run_environment_and_deployment(
    name,
):
    """Native approval binds the current publisher deployment."""
    docs = _approval(name)
    runtime, transport = _runtime(*docs)
    decision, _, _ = qualification(name)
    admitted = decision.snapshot.governance
    proof = parse_canonical_json(
        runtime.approval(
            decision.snapshot.intent,
            admitted,
            sentinel=admitted.registry.environment + "/v1",
        )
    )
    assert proof["run-id"] == RUN_ID
    assert proof["run-attempt"] == 1
    assert proof["target"] == TARGET
    assert proof["environment"] == admitted.registry.environment
    assert proof["deployment-id"] == docs[2][0]["id"]
    assert proof["reviewer-id"] == _USER["id"]
    assert proof["native-response-digest"].startswith("sha256:")
    assert len(transport.calls) == len(docs)


@pytest.mark.parametrize(
    "change",
    [
        "target",
        "rerun",
        "event",
        "workflow",
        "actor",
        "pending",
        "reviewer",
        "environment",
        "duplicate-approval",
        "deployment-target",
        "deployment-bool-id",
        "status",
        "foreign-log",
        "sentinel",
    ],
)
def test_python_github_approval_rejects_foreign_native_authority(change):
    """Workflow conclusions cannot replace exact native approval authority."""
    docs = _approval()
    sentinel = governance().registry.environment + "/v1"
    mutations = {
        "target": ((0, "head_sha"), "f" * 40),
        "rerun": ((0, "run_attempt"), 2),
        "event": ((0, "event"), "push"),
        "workflow": ((0, "path"), "foreign.yml"),
        "actor": ((0, "actor", "login"), "other"),
        "pending": ((1, 0, "state"), "pending"),
        "reviewer": ((1, 0, "user", "id"), 1),
        "environment": ((1, 0, "environments", 0, "name"), "foreign"),
        "duplicate-approval": ((1,), docs[1] * 2),
        "deployment-target": ((2, 0, "sha"), "f" * 40),
        "deployment-bool-id": ((2, 0, "id"), True),
        "status": ((3, 0, "state"), "success"),
        "foreign-log": (
            (3, 0, "log_url"),
            "https://github.com/hcoona/three/actions/runs/999/job/1",
        ),
    }
    if change == "sentinel":
        sentinel = "foreign"
    else:
        path, value = mutations[change]
        node = docs
        for part in path[:-1]:
            node = node[part]
        node[path[-1]] = value
    runtime, _ = _runtime(*docs)
    decision, _, _ = qualification()
    with pytest.raises(ValueError, match="Python"):
        runtime.approval(
            decision.snapshot.intent,
            decision.snapshot.governance,
            sentinel=sentinel,
        )


def test_python_disabled_governance_stops_before_native_configuration(
    monkeypatch,
):
    """Disabled Governance stops GitHub control and credential use."""
    calls = []
    registry = PythonRegistry("testpypi")

    class Reader:
        def __init__(self, **kwargs):
            calls.append(kwargs)

        def read(self, **kwargs):
            calls.append(kwargs)
            return SimpleNamespace(
                content=canonicalize(blocked_python_governance(registry)),
                main_sha=TARGET,
            )

    monkeypatch.setattr(python_github, "IsolatedGovernanceGitReader", Reader)
    runtime, transport = _runtime()
    with pytest.raises(ValueError, match=r"disabled|blocked"):
        runtime.governance(registry)
    assert transport.calls == []
    assert calls[1]["ref"] == "refs/heads/main"
    assert calls[1]["repository"] == "hcoona/three"


@pytest.fixture
def oidc_environment():
    """Synthetic issuer metadata never leaves the fake transport."""
    return {
        "ACTIONS_ID_TOKEN_REQUEST_URL": "https://runner.actions.githubusercontent.com/token?request=one",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN": "test-only-request-token",
    }


@pytest.mark.parametrize("audience", ["testpypi", "pypi"])
def test_python_oidc_requests_one_exact_audience(audience, oidc_environment):
    """The publisher chooses one destination audience per request."""
    transport = FakeHttp(_response({"value": "test-only-assertion"}))
    assert (
        obtain_python_oidc_assertion(audience, oidc_environment, transport)
        == "test-only-assertion"
    )
    assert transport.calls == [
        (
            "GET",
            "https://runner.actions.githubusercontent.com/token?request=one&audience="
            + audience,
            {"Authorization": "Bearer test-only-request-token"},
            None,
            MAX_RESPONSE_BYTES,
        )
    ]


@pytest.mark.parametrize(
    "url",
    [
        "http://runner.actions.githubusercontent.com/token",
        "https://actions.githubusercontent.com/token",
        "https://runner.actions.githubusercontent.com.evil.invalid/token",
        "https://other.invalid/token",
        "https://user@runner.actions.githubusercontent.com/token",
        "https://runner.actions.githubusercontent.com:444/token",
        "https://runner.actions.githubusercontent.com/token#fragment",
        "https://runner.actions.githubusercontent.com/token?audience=pypi",
        "https://runner.actions.githubusercontent.com/token?%61udience=pypi",
    ],
)
def test_python_oidc_rejects_foreign_issuer_or_preselected_audience(
    url, oidc_environment
):
    """Unsafe issuer inputs fail before request credentials reach transport."""
    oidc_environment["ACTIONS_ID_TOKEN_REQUEST_URL"] = url
    transport = FakeHttp()
    with pytest.raises(ValueError, match="Python OIDC"):
        obtain_python_oidc_assertion("testpypi", oidc_environment, transport)
    assert transport.calls == []


@pytest.mark.parametrize(
    "audience", ["https://pypi.org", "npm", "", "TestPyPI"]
)
def test_python_oidc_rejects_unknown_audience_before_send(
    audience, oidc_environment
):
    """Aliases cannot silently mint for a different relying party."""
    transport = FakeHttp()
    with pytest.raises(ValueError, match="audience is not admitted"):
        obtain_python_oidc_assertion(audience, oidc_environment, transport)
    assert transport.calls == []


@pytest.mark.parametrize(
    "document", [{}, [], {"value": ""}, {"value": True}, {"value": "value\n"}]
)
def test_python_oidc_rejects_malformed_assertion_response(
    document, oidc_environment
):
    """A successful HTTP status alone does not supply an assertion."""
    transport = FakeHttp(_response(document))
    with pytest.raises((ValueError, TypeError), match="Python"):
        obtain_python_oidc_assertion("pypi", oidc_environment, transport)
    assert len(transport.calls) == 1


def test_python_github_approval_rejects_boolean_run_attempt():
    """Native JSON true cannot stand in for the first workflow run attempt."""
    docs = _approval()
    docs[0]["run_attempt"] = True
    runtime, _ = _runtime(*docs)
    decision, _, _ = qualification()
    with pytest.raises(ValueError, match="Python native run identity"):
        runtime.approval(
            decision.snapshot.intent,
            decision.snapshot.governance,
            sentinel=decision.snapshot.governance.registry.environment + "/v1",
        )


@pytest.mark.parametrize("permission", ["push", "maintain", "admin"])
@pytest.mark.parametrize("value", [1, "true", None])
def test_python_github_configuration_rejects_nonboolean_writer_permission(
    permission, value
):
    """Native permission primitives must not silently conceal a writer."""
    docs = _configuration()
    permissions = {"push": False, "maintain": False, "admin": False}
    permissions[permission] = value
    docs[2].append({"id": 1, "login": "other", "permissions": permissions})
    runtime, _ = _runtime(*docs)
    with pytest.raises(TypeError, match="Python writer permissions"):
        runtime.configuration(governance())


@pytest.mark.parametrize("status", [301, 403, 429, 500])
def test_python_oidc_failure_has_one_request_and_no_fallback(
    status, oidc_environment
):
    """Issuer redirects and errors cannot mint through a retry or fallback."""
    transport = FakeHttp(PythonHttpResponse(status, b"denied", "text/plain"))
    with pytest.raises(ValueError, match="assertion request failed"):
        obtain_python_oidc_assertion("testpypi", oidc_environment, transport)
    assert len(transport.calls) == 1


@pytest.mark.parametrize(
    "body", [b'{"value":"a","value":"b"}', b"NaN", b"not-json"]
)
def test_python_oidc_rejects_ambiguous_or_invalid_json(body, oidc_environment):
    """An ambiguous response cannot supply a preferred assertion."""
    transport = FakeHttp(PythonHttpResponse(200, body, "application/json"))
    with pytest.raises(ValueError, match=r"duplicate|invalid JSON|Expecting"):
        obtain_python_oidc_assertion("testpypi", oidc_environment, transport)
    assert len(transport.calls) == 1


def test_python_github_governance_reads_fresh_main_and_current_runtime_controls(
    monkeypatch,
):
    """Fresh Governance and current readable controls jointly admit."""
    admitted = governance()
    calls = []

    class Reader:
        def __init__(self, **kwargs):
            calls.append(kwargs)

        def read(self, **kwargs):
            calls.append(kwargs)
            return SimpleNamespace(content=admitted.content, main_sha=TARGET)

    monkeypatch.setattr(python_github, "IsolatedGovernanceGitReader", Reader)
    runtime, transport = _runtime(*_configuration())
    actual = runtime.governance(admitted.registry, initial=admitted)
    assert actual == admitted
    assert calls[1] == {
        "repository": "hcoona/three",
        "ref": "refs/heads/main",
        "path": (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-python-testpypi.json"
        ),
        "eligibility_main_sha": TARGET,
    }
    assert len(transport.calls) == len(_configuration())
