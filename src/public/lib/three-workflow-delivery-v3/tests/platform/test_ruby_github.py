"""Bounded Ruby GitHub controls and isolated OIDC without external effects."""

import http.client
from copy import deepcopy
from datetime import timedelta
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_HTTP_RESPONSE_LIMIT,
    RUBY_INDEX_LIMIT,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform import ruby_github
from three_workflow_delivery_v3.platform.ruby_github import (
    RubyGitHubRuntime,
    obtain_ruby_oidc_assertion,
)
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RUBY_WORKFLOW,
    blocked_ruby_governance,
    ruby_governance_path,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    is_ruby_input_path,
)

from ..ruby_integration_fixtures import NOW, RUN_ID, TARGET, governance
from ..ruby_registry_fixtures import ScriptedTransport, json_response

_REPO = "/repos/hcoona/three"
_TOKEN = "synthetic-github-runtime-token"  # noqa: S105 - nonsecret test input
_USER = {"id": 712433, "login": "hcoona"}
_PAGE_SIZE = 100
_MAX_PAGES = 5
_REQUEST_LIMIT = 128


@pytest.fixture(autouse=True)
def deny_native_services(monkeypatch):
    """Fail locally if any test bypasses controlled transport or Git seams."""

    def denied(*_args, **_kwargs):
        pytest.fail(
            "Native HTTP and Governance Git are outside this test scope"
        )

    monkeypatch.setattr(http.client, "HTTPSConnection", denied)
    monkeypatch.setattr(ruby_github, "IsolatedGovernanceGitReader", denied)


def _runtime(*documents, now=NOW):
    transport = ScriptedTransport(*(json_response(doc) for doc in documents))
    return RubyGitHubRuntime(_TOKEN, transport, clock=lambda: now), transport


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


def _intent(name="rubygems"):
    admitted = governance(name)
    return ReleaseIntent(
        "hcoona/three",
        RUBY_WORKFLOW,
        "refs/heads/main",
        TARGET,
        "release-request:" + "b" * 64,
        "hcoona",
        RUN_ID,
        "workflow_dispatch",
        "refs/heads/main",
        TARGET,
        admitted.registry.channel,
        "live",
        "live-release",
        "hcoona-release-smoke-ruby",
    )


def _approval(name="rubygems"):
    environment = governance(name).registry.environment
    return [
        {
            "head_sha": TARGET,
            "head_branch": "main",
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": RUBY_WORKFLOW,
            "actor": deepcopy(_USER),
        },
        [
            {
                "environments": [{"id": 1901, "name": environment}],
                "state": "approved",
                "user": deepcopy(_USER),
            }
        ],
        [{"id": 2001, "sha": TARGET, "environment": environment}],
        [
            {
                "state": "in_progress",
                "log_url": f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/job/9001",
            }
        ],
    ]


def _reader(monkeypatch, content, *, main_sha=TARGET, error=None):
    calls = []

    class Reader:
        def __init__(self, **kwargs):
            calls.append(kwargs)

        def read(self, **kwargs):
            calls.append(kwargs)
            if error is not None:
                raise error
            return SimpleNamespace(content=content, main_sha=main_sha)

    monkeypatch.setattr(ruby_github, "IsolatedGovernanceGitReader", Reader)
    return calls


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_github_reads_exact_current_repository_controls(name):
    """Admitted destinations both require current readable GitHub controls."""
    docs = _configuration()
    runtime, transport = _runtime(*docs)
    runtime.configuration(governance(name))
    paths = [
        _REPO,
        _REPO + "/branches/main",
        _REPO + "/collaborators?affiliation=all&per_page=100&page=1",
    ]
    assert [call[1] for call in transport.requests] == [
        "https://api.github.com" + path for path in paths
    ]
    assert all(
        call[0] == "GET" and call[3] is None for call in transport.requests
    )
    assert all(
        call[2]
        == {
            "Accept": "application/vnd.github+json",
            "Authorization": "Bearer " + _TOKEN,
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "three-workflow-delivery-v3",
        }
        for call in transport.requests
    )
    assert all(call[4] == RUBY_INDEX_LIMIT for call in transport.requests)
    assert runtime.observations == [
        {"path": path, "body": doc}
        for path, doc in zip(paths, docs, strict=True)
    ]
    assert _TOKEN not in canonicalize(runtime.observations).decode()
    assert _TOKEN not in repr(runtime)


@pytest.mark.parametrize(
    "change",
    [
        "repository",
        "owner-name",
        "owner-id",
        "owner-shape",
        "branch-false",
        "branch-coerced",
        "foreign-writer",
        "writer-shape",
    ],
)
def test_ruby_github_rejects_changed_repository_or_writer_control(change):
    """A live attestation cannot excuse changed current controls."""
    docs = _configuration()
    changes = {
        "repository": ((0, "full_name"), "hcoona/foreign"),
        "owner-name": ((0, "owner", "login"), "foreign"),
        "owner-id": ((0, "owner", "id"), 1),
        "owner-shape": ((0, "owner"), None),
        "branch-false": ((1, "protected"), False),
        "branch-coerced": ((1, "protected"), 1),
        "foreign-writer": ((2, 0, "login"), "foreign"),
        "writer-shape": ((2, 0, "permissions"), None),
    }
    path, value = changes[change]
    node = docs
    for part in path[:-1]:
        node = node[part]
    node[path[-1]] = value
    runtime, transport = _runtime(*docs)
    with pytest.raises((ValueError, TypeError), match="Ruby"):
        runtime.configuration(governance())
    assert all(call[0] == "GET" for call in transport.requests)


@pytest.mark.parametrize("permission", ["push", "maintain", "admin"])
@pytest.mark.parametrize("value", [True, 1, "true", None])
def test_ruby_github_rejects_each_foreign_or_coerced_writer_permission(
    permission, value
):
    """Each write permission is independently authoritative and Boolean."""
    docs = _configuration()
    permissions = {"push": False, "maintain": False, "admin": False}
    permissions[permission] = value
    docs[2].append({"id": 1, "login": "foreign", "permissions": permissions})
    runtime, _transport = _runtime(*docs)
    with pytest.raises((ValueError, TypeError), match=r"writer|permissions"):
        runtime.configuration(governance())


def test_ruby_github_accepts_foreign_read_only_collaborator():
    """Read-only access does not change the accepted writer set."""
    docs = _configuration()
    docs[2].append(
        {
            "id": 1,
            "login": "reader",
            "permissions": {
                "push": False,
                "maintain": False,
                "admin": False,
            },
        }
    )
    runtime, _transport = _runtime(*docs)
    runtime.configuration(governance())
    assert runtime.observations[-1]["body"] == docs[2]


@pytest.mark.parametrize(
    "path",
    [
        "/repos/other/three",
        "/repos/hcoona/three-fork",
        "/user",
        "https://api.github.com/repos/hcoona/three",
    ],
)
def test_ruby_github_foreign_repository_is_rejected_before_send(path):
    """The current job token stays confined to the exact repository."""
    runtime, transport = _runtime()
    with pytest.raises(ValueError, match="outside its exact repository"):
        runtime.get(path)
    assert transport.requests == []
    assert runtime.requests_used == 0


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(302, b"redirect"),
        RubyHttpResponse(403, b"denied"),
        RubyHttpResponse(404, b"missing"),
        RubyHttpResponse(429, b"limited"),
        RubyHttpResponse(500, b"error"),
        RubyHttpResponse(200, b"not-json"),
        OSError("controlled network failure"),
        http.client.BadStatusLine("bad status"),
    ],
)
def test_ruby_github_failed_read_spends_budget_without_retry(response):
    """Failures retain earlier observations and consume a send."""
    transport = ScriptedTransport(json_response({"prior": True}), response)
    runtime = RubyGitHubRuntime(_TOKEN, transport)
    assert runtime.get(_REPO) == {"prior": True}
    with pytest.raises((ValueError, OSError, http.client.HTTPException)):
        runtime.get(_REPO + "/branches/main")
    assert runtime.requests_used == len(transport.requests) == 2  # noqa: PLR2004 - success then failure
    assert runtime.observations == [{"path": _REPO, "body": {"prior": True}}]


def test_ruby_github_cumulative_request_limit_includes_failures():
    """A failed send has no refund and request 129 is never sent."""
    transport = ScriptedTransport(
        *[json_response({})] * (_REQUEST_LIMIT - 1), OSError("last send lost")
    )
    runtime = RubyGitHubRuntime(_TOKEN, transport)
    for _ in range(_REQUEST_LIMIT - 1):
        runtime.get(_REPO)
    with pytest.raises(OSError, match="last send lost"):
        runtime.get(_REPO)
    with pytest.raises(ValueError, match="budget exhausted"):
        runtime.get(_REPO)
    assert runtime.requests_used == len(transport.requests) == _REQUEST_LIMIT
    assert len(runtime.observations) == _REQUEST_LIMIT - 1


def test_ruby_github_complete_pagination_preserves_existing_query():
    """A full page is never treated as complete absence."""
    first = [{"id": i} for i in range(_PAGE_SIZE)]
    runtime, transport = _runtime({"items": first}, {"items": [{"id": 100}]})
    assert runtime.pages(_REPO + "/items?filter=all", "items") == [
        *first,
        {"id": 100},
    ]
    assert [call[1] for call in transport.requests] == [
        f"https://api.github.com{_REPO}/items?filter=all&per_page=100&page={page}"
        for page in (1, 2)
    ]


@pytest.mark.parametrize("final_page", [[], [{}] * 99])
def test_ruby_github_accepts_complete_fifth_page(final_page):
    """Completion on the last admitted page is retained without a sixth."""
    full = [{"id": 1}] * _PAGE_SIZE
    runtime, transport = _runtime(*([full] * 4), final_page)
    assert runtime.pages(_REPO + "/items") == full * 4 + final_page
    assert len(transport.requests) == _MAX_PAGES


def test_ruby_github_full_fifth_page_cannot_return_partial_inventory():
    """Exhausting all full pages fails instead of concealing a later writer."""
    runtime, transport = _runtime(*([[{}] * _PAGE_SIZE] * _MAX_PAGES))
    with pytest.raises(ValueError, match="page budget"):
        runtime.pages(_REPO + "/collaborators")
    assert len(transport.requests) == len(runtime.observations) == _MAX_PAGES


@pytest.mark.parametrize("doc", [{}, {"items": {}}, None, "items"])
def test_ruby_github_malformed_inventory_is_not_absence(doc):
    """Missing or non-list inventories fail closed."""
    runtime, transport = _runtime(doc)
    with pytest.raises(TypeError, match="inventory is malformed"):
        runtime.pages(_REPO + "/items", "items")
    assert len(transport.requests) == 1


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
@pytest.mark.parametrize("fresh", [False, True])
def test_ruby_github_governance_fetches_protected_main_and_actual_controls(
    monkeypatch, name, fresh
):
    """Current protected content and runtime controls jointly admit."""
    admitted = governance(name)
    main_sha = "f" * 40 if fresh else TARGET
    calls = _reader(monkeypatch, admitted.content, main_sha=main_sha)
    runtime, transport = _runtime(*_configuration())
    actual = runtime.governance(
        admitted.registry, initial=admitted if fresh else None
    )
    assert actual.content == admitted.content
    assert actual.source_commit == main_sha
    assert actual.observed_at == NOW
    assert calls == [
        {"repository": "hcoona/three", "token": _TOKEN},
        {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": ruby_governance_path(admitted.registry),
            "eligibility_main_sha": TARGET if fresh else None,
        },
    ]
    assert len(transport.requests) == len(_configuration())
    assert runtime.observations[-1]["body"] == _configuration()[-1]


@pytest.mark.parametrize("state", ["disabled", "expired"])
def test_ruby_github_unavailable_governance_blocks_before_runtime_reads(
    monkeypatch, state
):
    """Disabled or expired protected sources cannot acquire live controls."""
    admitted = governance("rubygems")
    doc = (
        blocked_ruby_governance(admitted.registry)
        if state == "disabled"
        else admitted.document
    )
    calls = _reader(monkeypatch, canonicalize(doc))
    runtime, transport = _runtime(
        now=NOW + timedelta(days=2) if state == "expired" else NOW
    )
    with pytest.raises(ValueError, match=r"disabled|blocked|stale"):
        runtime.governance(admitted.registry)
    assert transport.requests == []
    assert runtime.observations == []
    assert calls[-1]["ref"] == "refs/heads/main"


def test_ruby_github_governance_retains_delegated_antirollback_failure(
    monkeypatch,
):
    """The isolated Git reader owns ancestry; its failure cannot be ignored."""
    initial = governance("rubygems")
    calls = _reader(
        monkeypatch,
        initial.content,
        error=ValueError("controlled antirollback rejection"),
    )
    runtime, transport = _runtime()
    with pytest.raises(ValueError, match="antirollback rejection"):
        runtime.governance(initial.registry, initial=initial)
    assert calls[-1]["eligibility_main_sha"] == initial.source_commit
    assert transport.requests == []


def test_ruby_github_fresh_governance_rejects_changed_admitted_configuration(
    monkeypatch,
):
    """A valid but changed attestation cannot renew frozen authority."""
    initial = governance("rubygems")
    doc = initial.document
    doc["configuration"]["attestation-digest"] = "sha256:" + "e" * 64
    _reader(monkeypatch, canonicalize(doc), main_sha="f" * 40)
    runtime, transport = _runtime(*_configuration())
    with pytest.raises(ValueError, match=r"drift|changed"):
        runtime.governance(initial.registry, initial=initial)
    assert len(transport.requests) == len(_configuration())


def test_ruby_operation_control_binds_exact_envelope_target_and_selector(
    monkeypatch,
):
    """Fresh operation bytes do not assert package ownership or ready state."""
    content = b'{"opaque-disabled-envelope-fixture":true}'
    fresh_main = "c" * 40
    calls = _reader(monkeypatch, content, main_sha=fresh_main)
    runtime, transport = _runtime()
    observed = runtime.operation_control(TARGET)
    assert (observed.main_sha, observed.content) == (fresh_main, content)
    assert calls == [
        {"repository": "hcoona/three", "token": _TOKEN},
        {
            "repository": "hcoona/three",
            "ref": "refs/heads/main",
            "path": RUBY_ENVELOPE_PATH,
            "eligibility_main_sha": TARGET,
            "relevant_path": is_ruby_input_path,
        },
    ]
    assert transport.requests == []
    assert runtime.observations == []
    assert runtime.requests_used == 0


def test_ruby_operation_control_preserves_input_continuity_failure(
    monkeypatch,
):
    """Failed protected history cannot yield an operation envelope."""
    failure = GovernanceGitReadError("controlled complete-input rejection")
    _reader(monkeypatch, b"unused", error=failure)
    runtime, transport = _runtime()
    with pytest.raises(GovernanceGitReadError) as caught:
        runtime.operation_control(TARGET)
    assert caught.value is failure
    assert transport.requests == []
    assert runtime.observations == []


def test_ruby_github_protected_content_does_not_bypass_runtime_configuration(
    monkeypatch,
):
    """Ready content still fails if current main protection disappeared."""
    admitted = governance("rubygems")
    _reader(monkeypatch, admitted.content)
    docs = _configuration()
    docs[1]["protected"] = False
    runtime, transport = _runtime(*docs)
    with pytest.raises(ValueError, match="not protected"):
        runtime.governance(admitted.registry)
    assert len(transport.requests) == 2  # noqa: PLR2004 - run/review or repo/branch


@pytest.mark.parametrize("name", ["github-packages", "rubygems"])
def test_ruby_github_approval_binds_exact_current_run_and_native_proof(name):
    """One native approval and current deployment produce the sealed proof."""
    docs = _approval(name)
    runtime, transport = _runtime(*docs)
    admitted = governance(name)
    sentinel = admitted.registry.environment + "/v1"
    proof = parse_canonical_json(
        runtime.approval(_intent(name), admitted, sentinel=sentinel)
    )
    assert proof == {
        "repository": "hcoona/three",
        "run-id": RUN_ID,
        "run-attempt": 1,
        "target": TARGET,
        "environment": admitted.registry.environment,
        "environment-id": 1901,
        "deployment-id": 2001,
        "reviewer-id": 712433,
        "reviewer": "hcoona",
        "state": "approved",
        "native-response-digest": canonical_sha256(
            {
                "run": docs[0],
                "reviews": docs[1],
                "deployment": docs[2][0],
            }
        ),
        "sentinel": sentinel,
    }
    assert [row["body"] for row in runtime.observations] == docs
    assert len(transport.requests) == len(docs)
    assert _TOKEN not in canonicalize(runtime.observations).decode()


@pytest.mark.parametrize("branch", ["missing", None, "feature/ruby"])
def test_ruby_github_approval_rejects_missing_or_foreign_native_branch(branch):
    """A matching commit cannot substitute for the selected main branch."""
    docs = _approval()
    if branch == "missing":
        del docs[0]["head_branch"]
    else:
        docs[0]["head_branch"] = branch
    admitted = governance("rubygems")
    runtime, transport = _runtime(*docs)
    with pytest.raises(ValueError, match="native run identity"):
        runtime.approval(
            _intent(),
            admitted,
            sentinel=admitted.registry.environment + "/v1",
        )
    run_path = _REPO + f"/actions/runs/{RUN_ID}"
    assert [call[1] for call in transport.requests] == [
        "https://api.github.com" + run_path
    ]
    assert runtime.observations == [{"path": run_path, "body": docs[0]}]


@pytest.mark.parametrize(
    "change",
    [
        "target",
        "rerun",
        "bool-attempt",
        "event",
        "workflow",
        "actor-name",
        "actor-id",
        "pending",
        "reviewer-name",
        "reviewer-id",
        "environment-name",
        "environment-id",
        "duplicate-approval",
        "deployment-target",
        "deployment-environment",
        "deployment-bool-id",
        "status",
        "foreign-log",
        "sentinel",
        "duplicate-deployment",
    ],
)
def test_ruby_github_approval_rejects_each_foreign_native_authority(change):
    """Job success cannot substitute for exact approval and deployment."""
    docs = _approval()
    admitted = governance("rubygems")
    sentinel = admitted.registry.environment + "/v1"
    mutations = {
        "target": ((0, "head_sha"), "f" * 40),
        "rerun": ((0, "run_attempt"), 2),
        "bool-attempt": ((0, "run_attempt"), True),
        "event": ((0, "event"), "push"),
        "workflow": ((0, "path"), "foreign.yml"),
        "actor-name": ((0, "actor", "login"), "foreign"),
        "actor-id": ((0, "actor", "id"), 1),
        "pending": ((1, 0, "state"), "pending"),
        "reviewer-name": ((1, 0, "user", "login"), "foreign"),
        "reviewer-id": ((1, 0, "user", "id"), 1),
        "environment-name": ((1, 0, "environments", 0, "name"), "foreign"),
        "environment-id": ((1, 0, "environments", 0, "id"), 1),
        "duplicate-approval": ((1,), deepcopy(docs[1]) * 2),
        "deployment-target": ((2, 0, "sha"), "f" * 40),
        "deployment-environment": ((2, 0, "environment"), "foreign"),
        "deployment-bool-id": ((2, 0, "id"), True),
        "status": ((3, 0, "state"), "success"),
        "foreign-log": (
            (3, 0, "log_url"),
            "https://github.com/hcoona/three/actions/runs/999/job/1",
        ),
    }
    if change == "sentinel":
        sentinel = "foreign"
    elif change == "duplicate-deployment":
        docs[2].append(dict(docs[2][0], id=2002))
        docs.append(deepcopy(docs[3]))
    else:
        path, value = mutations[change]
        node = docs
        for part in path[:-1]:
            node = node[part]
        node[path[-1]] = value
    runtime, transport = _runtime(*docs)
    with pytest.raises(ValueError, match="Ruby"):
        runtime.approval(_intent(), admitted, sentinel=sentinel)
    assert all(
        call[0] == "GET" and call[3] is None for call in transport.requests
    )


@pytest.mark.parametrize("reviews", [{}, [None], [{"environments": None}]])
def test_ruby_github_malformed_review_history_is_not_approval(reviews):
    """Malformed history cannot supply an implicit alternate approval."""
    docs = _approval()
    docs[1] = reviews
    runtime, transport = _runtime(*docs)
    with pytest.raises(TypeError, match="approval history"):
        runtime.approval(_intent(), governance("rubygems"), sentinel="unused")
    assert len(transport.requests) == 2  # noqa: PLR2004 - run/review or repo/branch


@pytest.fixture
def oidc_environment():
    """Synthetic runner metadata without an actual authentication capability."""
    return {
        "ACTIONS_ID_TOKEN_REQUEST_URL": "https://runner.actions.githubusercontent.com/token?request=one&blank=",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN": "synthetic-request-token",
    }


def test_ruby_oidc_requests_exact_audience_and_keeps_assertion_out_of_output(
    oidc_environment, capsys
):
    """Preserve issuer query and return the assertion only to its caller."""
    transport = ScriptedTransport(
        json_response({"value": "synthetic-assertion"})
    )
    assert (
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
        == "synthetic-assertion"
    )
    assert transport.requests == [
        (
            "GET",
            "https://runner.actions.githubusercontent.com/token?request=one&blank=&audience=rubygems.org",
            {"Authorization": "Bearer synthetic-request-token"},
            None,
            RUBY_HTTP_RESPONSE_LIMIT,
        )
    ]
    assert capsys.readouterr() == ("", "")


@pytest.mark.parametrize(
    "audience",
    ["github-packages", "https://rubygems.org", "rubygems", "", "pypi"],
)
def test_ruby_oidc_rejects_other_destinations_before_credentials(
    audience, oidc_environment
):
    """GitHub Packages cannot obtain a RubyGems assertion through this API."""
    transport = ScriptedTransport()
    with pytest.raises(ValueError, match="audience is not admitted"):
        obtain_ruby_oidc_assertion(audience, oidc_environment, transport)
    assert transport.requests == []


@pytest.mark.parametrize(
    "url",
    [
        "http://runner.actions.githubusercontent.com/token",
        "https://actions.githubusercontent.com/token",
        "https://runner.actions.githubusercontent.com.evil.invalid/token",
        "https://other.invalid/token",
        "https://user@runner.actions.githubusercontent.com/token",
        "https://user:pass@runner.actions.githubusercontent.com/token",
        "https://runner.actions.githubusercontent.com:444/token",
        "https://runner.actions.githubusercontent.com/token#fragment",
        "https://runner.actions.githubusercontent.com/token?audience=rubygems.org",
        "https://runner.actions.githubusercontent.com/token?%61udience=other",
    ],
)
def test_ruby_oidc_rejects_foreign_issuer_or_preselected_audience(
    url, oidc_environment
):
    """Unsafe issuer inputs fail before request credentials reach transport."""
    oidc_environment["ACTIONS_ID_TOKEN_REQUEST_URL"] = url
    transport = ScriptedTransport()
    with pytest.raises(ValueError, match="Ruby OIDC"):
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
    assert transport.requests == []


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(302, b"synthetic-private-assertion"),
        RubyHttpResponse(403, b"synthetic-private-assertion"),
        RubyHttpResponse(429, b"synthetic-private-assertion"),
        RubyHttpResponse(500, b"synthetic-private-assertion"),
    ],
)
def test_ruby_oidc_http_failure_has_no_retry_or_response_body_leak(
    response, oidc_environment, capsys
):
    """Issuer failures retain neither response body nor a replacement token."""
    transport = ScriptedTransport(response)
    with pytest.raises(ValueError, match="assertion request failed") as caught:
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
    assert "synthetic-private-assertion" not in str(caught.value)
    assert len(transport.requests) == 1
    assert capsys.readouterr() == ("", "")


@pytest.mark.parametrize(
    "document",
    [
        {},
        [],
        {"value": ""},
        {"value": True},
        {"value": None},
        {"value": "value\n"},
    ],
)
def test_ruby_oidc_success_status_requires_well_formed_assertion(
    document, oidc_environment
):
    """HTTP 200 alone cannot supply an assertion."""
    transport = ScriptedTransport(json_response(document))
    with pytest.raises((ValueError, TypeError), match="Ruby"):
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    "body", [b'{"value":"a","value":"b"}', b"NaN", b"not-json"]
)
def test_ruby_oidc_ambiguous_json_cannot_select_assertion(
    body, oidc_environment
):
    """Duplicate keys and non-JSON bodies cannot choose a preferred token."""
    transport = ScriptedTransport(RubyHttpResponse(200, body))
    with pytest.raises(ValueError):  # noqa: PT011 - strict parser errors vary
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
    assert len(transport.requests) == 1


@pytest.mark.parametrize(
    "error",
    [
        OSError("controlled OIDC connection failure"),
        http.client.IncompleteRead(b"synthetic-partial-assertion", 100),
    ],
)
def test_ruby_oidc_transport_failure_has_no_second_request(
    error, oidc_environment, capsys
):
    """A lost issuer response cannot trigger automatic re-minting."""
    transport = ScriptedTransport(error)
    with pytest.raises(type(error)):
        obtain_ruby_oidc_assertion("rubygems.org", oidc_environment, transport)
    assert len(transport.requests) == 1
    assert capsys.readouterr() == ("", "")


@pytest.mark.parametrize(
    "status_url",
    [
        "https://github.com/foreign/three/actions/runs/991/job/9001",
        "https://github.com/hcoona/three/actions/runs/9910/job/9001",
        "https://github.com/hcoona/three/actions/runs/991",
    ],
)
def test_ruby_github_similar_deployment_log_is_not_current_run(status_url):
    """Partial repository or run identifiers cannot match the exact log path."""
    docs = _approval()
    docs[3][0]["log_url"] = status_url
    runtime, _transport = _runtime(*docs)
    admitted = governance("rubygems")
    with pytest.raises(ValueError, match="exact native deployment"):
        runtime.approval(
            _intent(), admitted, sentinel=admitted.registry.environment + "/v1"
        )
