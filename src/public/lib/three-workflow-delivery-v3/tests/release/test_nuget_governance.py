"""Separate NuGet Governance and actual platform readback contracts."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004, SLF001
import hashlib
import json
from copy import deepcopy
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.adapters.nuget_github_packages import (
    NuGetHttpResponse,
    NuGetReadTransport,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release import eligibility as shared
from three_workflow_delivery_v3.release import nuget_governance as nuget
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitRead,
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    NUGET_GOVERNANCE_PATH,
    NUGET_PACKAGE,
    NUGET_RELEASE_UNIT,
    GovernanceSource,
)

ROOT = Path(__file__).resolve().parents[6]
NOW = datetime(2026, 9, 9, 12, tzinfo=UTC)
TARGET = "a" * 40
HEAD = "b" * 40
TREE = "c" * 40
MAIN = "d" * 40
RUN_ID = 7101
WORKFLOW = ".github/workflows/workflow-delivery-v3-buddy-live.yml"
ENV = "workflow-delivery-v3-buddy-approval"
API = "https://api.github.com"
REPO = "/repos/hcoona/three"
ACTOR = {"login": "hcoona", "id": 712433, "type": "User"}
REPOSITORY = {
    "id": 1102295886,
    "full_name": GOVERNANCE_REPOSITORY,
    "owner": ACTOR,
}
TOKEN = "test-only-platform-token"  # noqa: S105


def _attested_document():
    return {
        "schema": shared.NUGET_ATTESTATION_SCHEMA,
        "release_policy": NUGET_RELEASE_UNIT,
        "package": NUGET_PACKAGE,
        "issuer": "hcoona",
        "inspected_at": "2026-09-09T00:00:00Z",
        "expires_at": "2026-12-08T00:00:00Z",
        "accepted_writers": [{"login": "hcoona", "role": "Admin"}],
        "accepted_publisher": "hcoona",
        "access_inventory": {
            "repository": [{"subject": "hcoona", "access": "admin"}],
            "package": [{"subject": "hcoona", "access": "write"}],
            "manage_actions": [{"subject": "hcoona", "access": "allowed"}],
        },
        "package_principal": {
            "repository": GOVERNANCE_REPOSITORY,
            "intended_coordinate": NUGET_PACKAGE,
            "known_wider_reach": [
                "every-package-granting-actions-access-to-hcoona/three"
            ],
        },
        "limitations": [
            (
                "Disabled implementation; native atomic assurance "
                "and current platform evidence are unadmitted."
            )
        ],
        "activation": {"state": "blocked"},
        "live_enabled": False,
        "control_policy": dict(shared.NUGET_CONTROL_POLICY),
    }


def _document():
    return shared.BlockedNuGetGovernanceAttestation().to_document()


def _source(path=NUGET_GOVERNANCE_PATH):
    return GovernanceSource(
        GOVERNANCE_REPOSITORY, GOVERNANCE_REF, path, GOVERNANCE_MAX_AGE_DAYS
    )


def _client(document=None):
    content = canonicalize(_document() if document is None else document)
    client = Mock(spec=shared.GovernanceSourceClient)
    client.is_ref_protected.return_value = True
    client.read_source.return_value = GovernanceGitRead(
        MAIN, "sha1", "e" * 40, content
    )
    return client


def _observation():
    return shared.observe_governance_source(_source(), _client(), now=NOW)


def test_nuget_blocked_attestation_is_distinct_and_canonical():
    content = canonicalize(_document())
    attestation = shared.parse_governance_attestation(content)
    assert attestation.release_policy == NUGET_RELEASE_UNIT
    assert attestation.package == NUGET_PACKAGE
    assert canonicalize(attestation.to_document()) == content
    assert attestation.to_document()["schema"] != shared.ATTESTATION_SCHEMA
    assert attestation.to_document()["activation"] == {"state": "blocked"}
    assert not attestation.live_enabled
    assert not nuget.nuget_destination_primitive_is_admitted(attestation)


def test_existing_npm_governance_bytes_roundtrip_unchanged():
    content = (ROOT / GOVERNANCE_PATH).read_bytes()
    attestation = shared.parse_governance_attestation(content)
    assert canonicalize(attestation.to_document()) == content
    assert attestation.to_document()["schema"] == shared.ATTESTATION_SCHEMA
    assert "control_policy" not in attestation.to_document()
    assert not nuget.nuget_destination_primitive_is_admitted(attestation)


@pytest.mark.parametrize(
    "mutation",
    [
        "schema",
        "package",
        "unit",
        "control",
        "unknown",
        "issuer",
        "writer",
        "reach",
        "enabled-blocked",
    ],
)
def test_nuget_attestation_rejects_unbounded_or_cross_ecosystem_forms(mutation):
    document = _document()
    if mutation == "schema":
        document["schema"] = shared.ATTESTATION_SCHEMA
    elif mutation == "package":
        document["package"] = "@hcoona/hcoona-release-smoke-npm"
    elif mutation == "unit":
        document["release_policy"] = "hcoona-release-smoke-npm"
    elif mutation == "control":
        document["control_policy"]["selected_ref"] = "refs/heads/arbitrary"
    elif mutation == "unknown":
        document["review_override"] = True
    elif mutation == "issuer":
        document["issuer"] = "another"
    elif mutation == "writer":
        document["accepted_writers"] = [{"login": "another", "role": "Admin"}]
    elif mutation == "reach":
        document["package_principal"] = {
            "known_wider_reach": ["package-isolated"]
        }
    else:
        document["live_enabled"] = True
    with pytest.raises((ValueError, TypeError)):
        shared.parse_governance_attestation(canonicalize(document))


def test_nuget_ready_cannot_borrow_npm_acceptance():
    document = _attested_document()
    document["activation"] = json.loads((ROOT / GOVERNANCE_PATH).read_bytes())[
        "activation"
    ]
    document["inspected_at"] = "2026-09-09T06:00:00Z"
    for enabled in (False, True):
        document["live_enabled"] = enabled
        with pytest.raises(ValueError, match="NuGet ready activation requires"):
            shared.parse_governance_attestation(canonicalize(document))
    assert frozenset() == nuget._ADMITTED_NUGET_NATIVE_GENERATIONS
    assert frozenset() == nuget._ADMITTED_NUGET_ATOMIC_CONTRACTS


def test_nuget_blocked_action_has_no_authority():
    with pytest.raises(
        shared.GovernanceFreshnessRejectionError, match="fresh enabled"
    ):
        shared.require_action_governance(
            _observation().attestation,
            now=NOW,
            destination_operation_profile_digest="sha256:" + "0" * 64,
        )


@pytest.mark.parametrize("swap", ["npm-on-nuget", "nuget-on-npm"])
def test_nuget_source_binding_rejects_other_ecosystem(swap):
    document = (
        json.loads((ROOT / GOVERNANCE_PATH).read_bytes())
        if swap == "npm-on-nuget"
        else _document()
    )
    path = NUGET_GOVERNANCE_PATH if swap == "npm-on-nuget" else GOVERNANCE_PATH
    with pytest.raises(shared.GovernanceRejectionError, match="ecosystem"):
        shared.observe_governance_source(
            _source(path), _client(document), now=NOW
        )


def test_nuget_source_reuses_protected_git_observation():
    client = _client()
    observation = shared.observe_governance_source(_source(), client, now=NOW)
    assert observation.source.path == NUGET_GOVERNANCE_PATH
    assert observation.current_main_sha == MAIN
    assert (
        observation.canonical_content_digest
        == "sha256:" + hashlib.sha256(canonicalize(_document())).hexdigest()
    )
    client.read_source.assert_called_once_with(
        GOVERNANCE_REPOSITORY,
        GOVERNANCE_REF,
        NUGET_GOVERNANCE_PATH,
        eligibility_main_sha=None,
    )
    client.is_ref_protected.return_value = False
    with pytest.raises(shared.GovernanceRejectionError, match="not protected"):
        shared.observe_governance_source(_source(), client, now=NOW)
    assert client.read_source.call_count == 1


def test_nuget_fresh_read_reuses_path_touch_rejection():
    observation = _observation()
    client = _client()
    client.read_source.side_effect = GovernanceGitReadError(
        "Governance path touched after eligibility"
    )
    with pytest.raises(shared.GovernanceRejectionError, match="path touched"):
        shared.require_fresh_governance_identity(
            _source(),
            client,
            now=NOW,
            expected_provenance=shared.governance_observation_provenance(
                observation
            ),
            expected_canonical_content_digest=observation.canonical_content_digest,
            expected_expires_at="2026-12-08T00:00:00Z",
            expected_live_enabled=True,
        )
    assert client.read_source.call_args.kwargs["eligibility_main_sha"] == MAIN


def _page(path):
    return path + ("&" if "?" in path else "?") + "per_page=100&page=1"


def _platform_documents():
    environment = REPO + "/environments/" + ENV
    return deepcopy(
        {
            REPO: REPOSITORY,
            REPO + "/branches/main": {
                "name": "main",
                "protected": True,
                "commit": {"sha": MAIN},
            },
            f"{REPO}/compare/{TARGET}...{MAIN}": {
                "status": "ahead",
                "merge_base_commit": {"sha": TARGET},
            },
            f"{REPO}/actions/runs/{RUN_ID}": {
                "id": RUN_ID,
                "run_attempt": 1,
                "event": "workflow_dispatch",
                "head_sha": TARGET,
                "head_branch": "main",
                "path": WORKFLOW,
                "actor": ACTOR,
                "triggering_actor": ACTOR,
                "repository": REPOSITORY,
            },
            _page(f"{REPO}/commits/{TARGET}/pulls"): [
                {
                    "number": 700,
                    "merge_commit_sha": TARGET,
                    "merged_at": "2026-09-09T01:00:00Z",
                }
            ],
            f"{REPO}/pulls/700": {
                "number": 700,
                "body": "Exact-head delegate review completed.",
                "merged": True,
                "merge_commit_sha": TARGET,
                "merged_by": ACTOR,
                "head": {"sha": HEAD, "repo": REPOSITORY},
                "base": {"ref": "main", "repo": REPOSITORY},
            },
            f"{REPO}/git/commits/{TARGET}": {
                "sha": TARGET,
                "tree": {"sha": TREE},
            },
            f"{REPO}/git/commits/{HEAD}": {"sha": HEAD, "tree": {"sha": TREE}},
            _page(f"{REPO}/pulls/700/reviews"): [
                {
                    "id": 5,
                    "commit_id": HEAD,
                    "state": "COMMENTED",
                    "body": "Review carrier",
                    "user": {"login": "copilot-pull-request-reviewer[bot]"},
                }
            ],
            _page(f"{REPO}/pulls/700/comments"): [],
            _page(f"{REPO}/issues/700/comments"): [],
            _page(f"{REPO}/commits/{HEAD}/check-runs"): {
                "total_count": 1,
                "check_runs": [
                    {"name": "CI", "head_sha": HEAD, "conclusion": "success"}
                ],
            },
            _page(REPO + "/collaborators?affiliation=all"): [
                {**ACTOR, "permissions": {"admin": True, "push": True}}
            ],
            _page(REPO + "/teams"): [],
            _page(REPO + "/invitations"): [],
            environment: {
                "id": 20895030723,
                "name": ENV,
                "can_admins_bypass": False,
                "deployment_branch_policy": None,
                "protection_rules": [
                    {
                        "type": "required_reviewers",
                        "prevent_self_review": False,
                        "reviewers": [{"type": "User", "reviewer": ACTOR}],
                    }
                ],
            },
            _page(environment + "/variables"): {
                "total_count": 1,
                "variables": [
                    {
                        "name": "WDV3_APPROVAL_ENVIRONMENT_MARKER",
                        "value": "workflow-delivery-v3-buddy-approval/v1",
                    }
                ],
            },
            _page(f"/repositories/1102295886/environments/{ENV}/secrets"): {
                "total_count": 0,
                "secrets": [],
            },
            _page(REPO + "/actions/variables"): {
                "total_count": 0,
                "variables": [],
            },
            REPO + "/actions/permissions/artifact-and-log-retention": {
                "days": 90
            },
            "/users/hcoona/packages/nuget/" + NUGET_PACKAGE: {
                "id": 12024661,
                "name": NUGET_PACKAGE,
                "package_type": "nuget",
                "visibility": "public",
                "owner": ACTOR,
                "repository": REPOSITORY,
            },
        }
    )


def _transport(documents):
    transport = Mock(spec=NuGetReadTransport)
    transport.get.side_effect = lambda url, **_kwargs: NuGetHttpResponse(
        url, 200, (), json.dumps(documents[url.removeprefix(API)]).encode()
    )
    return transport


def _facts(transport, **changes):
    arguments = {
        "transport": transport,
        "token": TOKEN,
        "selected_ref": GOVERNANCE_REF,
        "target": TARGET,
        "control_sha": TARGET,
        "workflow_sha": TARGET,
        "workflow_run_id": RUN_ID,
        "workflow_path": WORKFLOW,
        "now": NOW,
    }
    arguments.update(changes)
    return nuget.read_nuget_platform_facts(**arguments)


def test_platform_readback_binds_run_main_merge_tree_and_control():
    transport = _transport(_platform_documents())
    facts = _facts(transport)
    assert facts.target == TARGET
    assert facts.control_sha == facts.workflow_sha == TARGET
    assert facts.current_main_sha == MAIN
    assert facts.reviewed_head_sha == HEAD
    assert facts.reviewed_tree_sha == TREE
    assert facts.pull_request_number == 700
    assert facts.writer_inventory == ("hcoona",)
    assert facts.package_control["id"] == 12024661
    assert facts.artifact_retention.days == 90
    assert facts.approval_environment.environment_id == 20895030723
    assert facts.readback_digest.startswith("sha256:")
    assert len(facts.exchanges) == len(_platform_documents())
    assert all(
        dict(call.kwargs["headers"])["Authorization"] == "Bearer " + TOKEN
        for call in transport.get.call_args_list
    )
    nuget.require_nuget_live_platform(
        _observation(), facts, target=TARGET, workflow_run_id=RUN_ID, now=NOW
    )


def test_platform_retains_review_carriers_without_fabricating_approval():
    facts = _facts(_transport(_platform_documents()))
    review = next(
        response
        for response in facts.review_carriers
        if "/reviews?" in response.url
    )
    assert json.loads(review.body)[0]["state"] == "COMMENTED"
    assert any(
        json.loads(response.body).get("body")
        == "Exact-head delegate review completed."
        for response in facts.review_carriers
        if isinstance(json.loads(response.body), dict)
    )
    assert not hasattr(facts, "owner_reviewed")
    assert not hasattr(facts, "review_passed")


@pytest.mark.parametrize(
    "scenario",
    [
        "unprotected",
        "diverged-main",
        "rerun",
        "wrong-actor",
        "wrong-merge-owner",
        "different-tree",
        "extra-writer",
        "team",
        "environment-reviewer",
        "environment-bypass",
        "environment-secret",
        "sentinel-shadow",
        "retention",
        "package-owner",
        "package-repository",
    ],
)
def test_platform_readback_rejects_wrong_authority(scenario):  # noqa: C901, PLR0912
    documents = _platform_documents()
    environment = REPO + "/environments/" + ENV
    if scenario == "unprotected":
        documents[REPO + "/branches/main"]["protected"] = False
    elif scenario == "diverged-main":
        documents[f"{REPO}/compare/{TARGET}...{MAIN}"]["status"] = "diverged"
    elif scenario == "rerun":
        documents[f"{REPO}/actions/runs/{RUN_ID}"]["run_attempt"] = 2
    elif scenario == "wrong-actor":
        documents[f"{REPO}/actions/runs/{RUN_ID}"]["actor"] = {
            "login": "another",
            "id": 1,
        }
    elif scenario == "wrong-merge-owner":
        documents[REPO + "/pulls/700"]["merged_by"] = {
            "login": "another",
            "id": 1,
        }
    elif scenario == "different-tree":
        documents[f"{REPO}/git/commits/{HEAD}"]["tree"]["sha"] = "f" * 40
    elif scenario == "extra-writer":
        documents[_page(REPO + "/collaborators?affiliation=all")].append(
            {"login": "another", "id": 1, "permissions": {"push": True}}
        )
    elif scenario == "team":
        documents[_page(REPO + "/teams")] = [{"name": "writers"}]
    elif scenario == "environment-reviewer":
        documents[environment]["protection_rules"][0]["reviewers"][0][
            "reviewer"
        ] = {"login": "another", "id": 1}
    elif scenario == "environment-bypass":
        documents[environment]["can_admins_bypass"] = True
    elif scenario == "environment-secret":
        documents[
            _page(f"/repositories/1102295886/environments/{ENV}/secrets")
        ] = {"total_count": 1, "secrets": [{"name": "NEW_SECRET"}]}
    elif scenario == "sentinel-shadow":
        documents[_page(REPO + "/actions/variables")] = {
            "total_count": 1,
            "variables": [
                {"name": "WDV3_APPROVAL_ENVIRONMENT_MARKER", "value": "shadow"}
            ],
        }
    elif scenario == "retention":
        documents[REPO + "/actions/permissions/artifact-and-log-retention"][
            "days"
        ] = 44
    elif scenario == "package-owner":
        documents["/users/hcoona/packages/nuget/" + NUGET_PACKAGE]["owner"] = {
            "login": "another",
            "id": 1,
        }
    else:
        documents["/users/hcoona/packages/nuget/" + NUGET_PACKAGE][
            "repository"
        ] = {"id": 1, "full_name": "another/repository"}
    with pytest.raises(
        (shared.GovernanceRejectionError, ValueError, TypeError)
    ):
        _facts(_transport(documents))


@pytest.mark.parametrize(
    "changes",
    [
        {"selected_ref": "refs/heads/arbitrary"},
        {"workflow_sha": HEAD},
        {"control_sha": HEAD},
    ],
)
def test_platform_rejects_arbitrary_ref_or_control_before_read(changes):
    transport = _transport(_platform_documents())
    with pytest.raises(shared.GovernanceRejectionError, match="same-revision"):
        _facts(transport, **changes)
    transport.get.assert_not_called()


@pytest.mark.parametrize(
    "changes",
    [
        {"target": HEAD},
        {"workflow_run_id": RUN_ID + 1},
        {"observed_at": NOW - timedelta(minutes=6)},
    ],
)
def test_platform_fact_binding_rejects_stale_or_cross_run_facts(changes):
    facts = replace(_facts(_transport(_platform_documents())), **changes)
    with pytest.raises(shared.GovernanceRejectionError, match="stale or"):
        nuget.require_nuget_live_platform(
            _observation(),
            facts,
            target=TARGET,
            workflow_run_id=RUN_ID,
            now=NOW,
        )


def test_checked_in_nuget_source_is_state_only_disabled():
    content = (ROOT / NUGET_GOVERNANCE_PATH).read_bytes()
    attestation = shared.parse_governance_attestation(content)
    assert not attestation.live_enabled
    assert attestation.activation.to_document() == {"state": "blocked"}
    assert "destination_primitive" not in attestation.activation.to_document()
    assert (
        attestation.to_document()["control_policy"]
        == shared.NUGET_CONTROL_POLICY
    )
    assert canonicalize(attestation.to_document()) == content


def test_nuget_disabled_source_has_no_unobserved_access_or_time_claims():
    document = _document()
    assert set(document) == {
        "schema",
        "release_policy",
        "package",
        "control_policy",
        "activation",
        "live_enabled",
    }
    with pytest.raises(ValueError, match="only disabled state"):
        shared.parse_governance_attestation(canonicalize(_attested_document()))
    blocked = shared.parse_governance_attestation(canonicalize(document))
    with pytest.raises(
        shared.GovernanceFreshnessRejectionError, match="no inspection"
    ):
        _ = blocked.inspected_at
    with pytest.raises(
        shared.GovernanceFreshnessRejectionError, match="no expiry"
    ):
        _ = blocked.expires_at
    with pytest.raises(shared.GovernanceRejectionError, match="no access"):
        _ = blocked.package_principal
