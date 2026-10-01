"""Finite bootstrap registry spending and actual native response parsing."""

from copy import deepcopy
from datetime import timedelta
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_runtime import (
    BOOTSTRAP_WORKFLOW,
    RubyBootstrapPhaseBudget,
    check_ruby_bootstrap_authority,
    collect_ruby_bootstrap_approval,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    parse_canonical_json,
)
from three_workflow_delivery_v3.platform.ruby_github import RubyGitHubRuntime
from three_workflow_delivery_v3.release.ruby_operation import (
    RUBY_REGISTRY_PARTITIONS,
)

from ..ruby_registry_fixtures import ScriptedTransport, json_response
from .ruby_bootstrap_fixtures import CURRENT, EXPIRED, modeled_plan

USER = {"login": "hcoona", "id": 712433}


@pytest.fixture
def run(tmp_path):
    """Use exact modeled bootstrap admission with a real local reservation."""
    return modeled_plan(tmp_path).run


def budget(run, phase, transport, reserve, clock=lambda: CURRENT):
    """Select one fixed runtime partition with a finite local deadline."""
    return RubyBootstrapPhaseBudget(
        run,
        phase,
        transport,
        reserve,
        deadline=CURRENT + timedelta(minutes=10),
        clock=clock,
    )


def send(selected):
    """Use a fixed public read solely against the scripted transport."""
    return selected.request("GET", "https://rubygems.org/fixture", {}, None, 32)


@pytest.mark.parametrize("destination", ["github-packages", "rubygems"])
@pytest.mark.parametrize("phase", list(RUBY_REGISTRY_PARTITIONS))
def test_partition_caps_include_prior_operator_debit_and_reserve_before_send(
    tmp_path, destination, phase
):
    """Reserve each fixed ordinal without borrowing another partition."""
    run = modeled_plan(tmp_path, destination).run
    debit = int(destination == "rubygems" and phase == "eligibility")
    cap = RUBY_REGISTRY_PARTITIONS[phase]
    observed = []

    class Native:
        def request(self, *_args):
            observed.append(("send", len(observed) // 2 + debit + 1))
            return RubyHttpResponse(200, b"ok")

    selected = budget(
        run, phase, Native(), lambda p, ordinal: observed.append((p, ordinal))
    )
    assert selected.used == debit
    for _ in range(cap - debit):
        assert send(selected).body == b"ok"
    assert observed == [
        entry
        for ordinal in range(debit + 1, cap + 1)
        for entry in ((phase, ordinal), ("send", ordinal))
    ]
    with pytest.raises(ValueError, match="partition exhausted"):
        send(selected)
    assert selected.used == cap
    assert len(observed) == 2 * (cap - debit)


@pytest.mark.parametrize("failure", ["reservation", "transport", "response"])
def test_spending_survives_failure_and_reopened_budget_cannot_refill(
    run, failure
):
    """The durable seam rejects a repeated ordinal after any failed attempt."""
    spent = set()
    calls = []

    def reserve(phase, ordinal):
        key = (phase, ordinal)
        if key in spent:
            message = "already spent"
            raise ValueError(message)
        spent.add(key)
        if failure == "reservation":
            message = "reservation flush failed"
            raise OSError(message)

    class Native:
        def request(self, *_args):
            calls.append("send")
            if failure == "transport":
                message = "transport lost"
                raise OSError(message)
            return RubyHttpResponse(200, b"x" * 33)

    selected = budget(run, "execute", Native(), reserve)
    with pytest.raises((OSError, ValueError), match=r"failed|lost|bounded"):
        send(selected)
    assert selected.used == 1
    assert spent == {("execute", 1)}
    assert calls == ([] if failure == "reservation" else ["send"])
    reopened = budget(run, "execute", Native(), reserve)
    with pytest.raises(ValueError, match="already spent"):
        send(reopened)
    assert calls == ([] if failure == "reservation" else ["send"])


@pytest.mark.parametrize(
    "mode",
    [
        "expired-entry",
        "regressed-entry",
        "expired-after-reserve",
        "regressed-after-reserve",
    ],
)
def test_request_clock_checks_bracket_durable_reservation(run, mode):
    """No native send occurs after expired or regressed authority."""
    clock_values = [CURRENT, CURRENT, CURRENT]
    bad = (
        CURRENT - timedelta(seconds=1)
        if "regressed" in mode
        else CURRENT + timedelta(minutes=10)
    )
    clock_values[1 if mode.endswith("entry") else 2] = bad
    times = iter(clock_values)
    reserved = []
    native = ScriptedTransport()
    selected = budget(
        run,
        "execute",
        native,
        lambda *args: reserved.append(args),
        clock=lambda: next(times),
    )
    with pytest.raises(ValueError, match=r"expired|deadline|predates"):
        send(selected)
    assert reserved == ([] if mode.endswith("entry") else [("execute", 1)])
    assert selected.used == (0 if mode.endswith("entry") else 1)
    assert native.requests == []


@pytest.mark.parametrize(
    "deadline",
    [
        CURRENT,
        CURRENT.replace(tzinfo=None),
        CURRENT + timedelta(minutes=30, microseconds=1),
        EXPIRED,
    ],
)
def test_phase_cannot_extend_its_finite_deadline(run, deadline):
    """Phase admission is bounded by thirty minutes and remaining authority."""
    with pytest.raises(ValueError, match="lifetime"):
        RubyBootstrapPhaseBudget(
            run,
            "execute",
            ScriptedTransport(),
            lambda *_: None,
            deadline=deadline,
            clock=lambda: CURRENT,
        )


@pytest.mark.parametrize(
    "response",
    [
        RubyHttpResponse(status=True, body=b""),
        RubyHttpResponse(99, b""),
        RubyHttpResponse(600, b""),
        RubyHttpResponse(200, "text"),
        RubyHttpResponse(200, b"x" * 33),
    ],
)
def test_bad_response_is_rejected_after_exactly_one_spent_send(run, response):
    """Malformed native responses cannot be admitted or retried implicitly."""
    native = ScriptedTransport(response)
    reserved = []
    selected = budget(
        run, "execute", native, lambda *args: reserved.append(args)
    )
    with pytest.raises(ValueError, match="bounded"):
        send(selected)
    assert reserved == [("execute", 1)]
    assert len(native.requests) == 1
    assert selected.used == 1


def control_documents():
    """Model complete native ownership, protected-main and writer responses."""
    return [
        {"full_name": "hcoona/three", "owner": deepcopy(USER)},
        {"protected": True},
        [
            {
                **USER,
                "permissions": {"push": True, "maintain": True, "admin": True},
            }
        ],
    ]


def runtime(documents):
    """Use the real bounded GitHub parser over controlled response originals."""
    native = ScriptedTransport(*(json_response(doc) for doc in documents))
    return RubyGitHubRuntime(
        "synthetic-test-token", native, clock=lambda: CURRENT
    ), native


def test_authority_rechecks_native_identity_and_protected_inputs(
    run, monkeypatch
):
    """Retain unrelated main advancement and actual control hashes."""
    documents = control_documents()
    github, native = runtime(documents)
    seen = []

    def read(_self, target):
        seen.append(target)
        return SimpleNamespace(
            content=run.request.envelope.content, main_sha="e" * 40
        )

    monkeypatch.setattr(RubyGitHubRuntime, "operation_control", read)
    result = check_ruby_bootstrap_authority(
        run, github, parse_canonical_json(run.native_environment), CURRENT
    )
    assert seen == [run.target]
    assert result == {
        "run-binding-digest": run.binding_digest,
        "main": "e" * 40,
        "envelope-digest": run.request.document["envelope-digest"],
        "checked-at": CURRENT.isoformat(),
        "native-controls-digest": canonical_sha256(
            dict(
                zip(
                    ("repository", "branch", "collaborators"),
                    documents,
                    strict=True,
                )
            )
        ),
    }
    assert [item[1] for item in native.requests] == [
        "https://api.github.com/repos/hcoona/three",
        "https://api.github.com/repos/hcoona/three/branches/main",
        "https://api.github.com/repos/hcoona/three/collaborators?affiliation=all&per_page=100&page=1",
    ]


@pytest.mark.parametrize(
    "mode",
    [
        "native-run",
        "envelope",
        "repository",
        "owner-id",
        "main",
        "writer",
        "permissions",
        "freshness",
    ],
)
def test_authority_rejects_changed_identity_freshness_or_controls(
    run, monkeypatch, mode
):
    """A fresh bad fact blocks authority without granting a registry action."""
    docs = control_documents()
    env = parse_canonical_json(run.native_environment)
    if mode == "native-run":
        env["GITHUB_RUN_ID"] = "992"
    elif mode == "repository":
        docs[0]["full_name"] = "other/three"
    elif mode == "owner-id":
        docs[0]["owner"]["id"] = True
    elif mode == "main":
        docs[1]["protected"] = False
    elif mode == "writer":
        docs[2][0]["login"] = "other"
    elif mode == "permissions":
        docs[2][0]["permissions"]["push"] = "true"
    github, native = runtime(docs)
    seen = []

    def read(_self, target):
        seen.append(target)
        if mode == "freshness":
            message = "protected inputs changed"
            raise ValueError(message)
        return SimpleNamespace(
            content=b"{}"
            if mode == "envelope"
            else run.request.envelope.content,
            main_sha=run.target,
        )

    monkeypatch.setattr(RubyGitHubRuntime, "operation_control", read)
    with pytest.raises(ValueError, match=r"Ruby|protected"):
        check_ruby_bootstrap_authority(run, github, env, CURRENT)
    assert seen == ([] if mode == "native-run" else [run.target])
    if mode in {"native-run", "envelope", "freshness"}:
        assert native.requests == []


def approval_documents(run):
    """Represent one native approved bootstrap deployment and reviewer."""
    environment = run.inputs.configuration.registry.environment
    return [
        {
            "id": run.run_id,
            "head_sha": run.target,
            "head_branch": "main",
            "run_attempt": 1,
            "event": "workflow_dispatch",
            "path": BOOTSTRAP_WORKFLOW,
            "actor": deepcopy(USER),
            "repository": {"full_name": "hcoona/three"},
        },
        [
            {
                "environments": [{"id": 101, "name": environment}],
                "state": "approved",
                "user": deepcopy(USER),
            }
        ],
        [{"id": 2001, "sha": run.target, "environment": environment}],
        [
            {
                "state": "in_progress",
                "log_url": f"https://github.com/hcoona/three/actions/runs/{run.run_id}/job/9001",
            }
        ],
    ]


def test_native_approval_is_bound_to_bootstrap_run_and_environment(run):
    """Native approval is collected from the distinct bootstrap workflow."""
    docs = approval_documents(run)
    github, native = runtime(docs)
    sentinel = run.inputs.configuration.document["github-controls"]["sentinel"]
    proof = parse_canonical_json(
        collect_ruby_bootstrap_approval(
            run, github, sentinel=sentinel, now=CURRENT
        )
    )
    assert proof["workflow"] == BOOTSTRAP_WORKFLOW
    assert (proof["run-id"], proof["run-attempt"], proof["target"]) == (
        991,
        1,
        run.target,
    )
    assert (
        proof["environment-id"],
        proof["deployment-id"],
        proof["reviewer-id"],
    ) == (101, 2001, 712433)
    assert proof["native-response-digest"] == canonical_sha256(
        {
            "run": docs[0],
            "reviews": docs[1],
            "deployments": [{"deployment": docs[2][0], "statuses": docs[3]}],
        }
    )
    assert len(native.requests) == 4  # noqa: PLR2004 - run, reviews, deployments, statuses


@pytest.mark.parametrize(
    "mode",
    [
        "normal-workflow",
        "run-attempt",
        "actor",
        "repository",
        "duplicate-approval",
        "no-approval",
        "rejected",
        "environment",
        "reviewer",
        "deployment-sha",
        "status-run",
        "status-state",
        "sentinel",
    ],
)
def test_native_approval_rejects_foreign_ambiguous_or_unapproved_state(  # noqa: C901, PLR0912 - independent native mismatches
    run, mode
):
    """No ambiguous native approval can authorize the first-project action."""
    docs = approval_documents(run)
    sentinel = run.inputs.configuration.registry.environment + "/v1"
    if mode == "normal-workflow":
        docs[0]["path"] = (
            ".github/workflows/workflow-delivery-v3-ruby-smoke.yml"
        )
    elif mode == "run-attempt":
        docs[0]["run_attempt"] = True
    elif mode == "actor":
        docs[0]["actor"]["login"] = "other"
    elif mode == "repository":
        docs[0]["repository"]["full_name"] = "other/three"
    elif mode == "duplicate-approval":
        docs[1] *= 2
    elif mode == "no-approval":
        docs[1] = []
    elif mode == "rejected":
        docs[1][0]["state"] = "rejected"
    elif mode == "environment":
        docs[1][0]["environments"][0]["name"] = "other"
    elif mode == "reviewer":
        docs[1][0]["user"]["id"] = 1
    elif mode == "deployment-sha":
        docs[2][0]["sha"] = "e" * 40
    elif mode == "status-run":
        docs[3][0]["log_url"] = (
            "https://github.com/hcoona/three/actions/runs/992/job/9001"
        )
    elif mode == "status-state":
        docs[3][0]["state"] = "success"
    else:
        sentinel = "wrong/v1"
    github, native = runtime(docs)
    with pytest.raises(ValueError, match="Ruby"):
        collect_ruby_bootstrap_approval(
            run, github, sentinel=sentinel, now=CURRENT
        )
    assert len(native.requests) <= 4  # noqa: PLR2004 - no retries
