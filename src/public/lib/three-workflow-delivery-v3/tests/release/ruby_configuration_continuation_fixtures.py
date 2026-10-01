"""Synthetic M01 ledger; fixture pins never admit the actual live incident."""

from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.platform.ruby_configuration_http import (
    RubyConfigurationRequest,
    RubyConfigurationResponse,
)
from three_workflow_delivery_v3.release import (
    ruby_configuration_continuation as m01,
)
from three_workflow_delivery_v3.release.ruby_configuration_continuation import (
    RubyConfigurationM01Continuation,
)

from ..ruby_integration_fixtures import NOW, TARGET
from .ruby_configuration_ledger_fixtures import complete_get, ledger, mutation
from .ruby_operation_fixtures import DIGEST, instant


def incident(tmp_path, monkeypatch, **changes):
    """Produce original failed evidence using real public durable operations."""
    selected = ledger(tmp_path / "ledger", **changes)
    for name in (
        "initial-controls",
        "initial-inventory",
        "environment-write",
        "branch-write",
    ):
        phase = selected.begin(name, now=NOW)
        if name.endswith("-write"):
            request = phase.spend(*mutation("github-packages", name), now=NOW)
            phase.complete_send(
                request,
                canonicalize({"fixture": name}),
                successful=True,
                now=NOW,
            )
        else:
            complete_get(
                phase,
                "/user"
                if name == "initial-controls"
                else RubyConfigurationRequest(
                    "github-packages", "owner-packages"
                ).path,
            )
        phase.finish(now=NOW)
    phase = selected.begin("marker-write", now=NOW)
    request = phase.spend(*mutation("github-packages", "marker-write"), now=NOW)
    raw = RubyConfigurationResponse(
        RubyConfigurationRequest("github-packages", "marker-create"),
        ruby_digest(request),
        NOW,
        NOW,
        201,
        (("content-type", "application/json"),),
        b"{}",
    )
    evidence = canonicalize(raw.document())
    phase.complete_send(request, evidence, successful=False, now=NOW)
    originals = selected.plan.directory.with_name("ledger-originals")
    (originals / "marker-write").mkdir(parents=True)
    (originals / "plan.json").write_bytes(selected.plan.content)
    (originals / "marker-write" / "01.response.json").write_bytes(evidence)
    pins = {
        "plan-digest": ruby_digest(selected.plan.content),
        "original-journal-digest": ruby_digest(
            (selected.plan.directory / "phases.jsonl").read_bytes()
        ),
        **{
            field: ruby_digest((phase.directory / name).read_bytes())
            for name, field in (
                ("phase.json", "failed-phase-digest"),
                ("01.request.json", "failed-request-digest"),
                ("01.evidence.json", "failed-response-digest"),
                ("01.receipt.json", "failed-receipt-digest"),
            )
        },
    }
    # Explicit test-only dependency: production accepts one literal incident.
    monkeypatch.setattr(m01, "M01_IDENTITIES", pins)
    content = canonicalize(
        {
            "schema": (
                "workflow-delivery/v3/ruby-configuration-m01-continuation-v1"
            ),
            **pins,
            "source-commit": TARGET,
            "caller-digest": DIGEST,
            "protocol-digest": DIGEST,
            "reviewer": "independent-test-reviewer",
            "author": "test-source-author",
            "reviewed-at": instant(NOW),
            "carrier": "https://github.com/hcoona/three/issues/954#issuecomment-1",
            "verdict": "accepted",
        }
    )
    acknowledgement_path(selected).write_bytes(content)
    return selected, RubyConfigurationM01Continuation(content)


def acknowledgement_path(selected):
    """Locate the fixed external sibling, never inside ledger membership."""
    return selected.plan.directory.with_name("ledger.m01-continuation.json")


def retained_tree(path):
    """Compare observable evidence before and after rejected operations."""
    return {
        str(item.relative_to(path)): item.read_bytes()
        for item in path.rglob("*")
        if item.is_file()
    }


def finish_read(
    selected, acknowledgement, name="post-configuration-controls", *, now=NOW
):
    """Finish one real reserved GET phase without contacting a service."""
    phase = selected.begin(name, now=now, continuation=acknowledgement)
    path = (
        "/repos/hcoona/three/branches/main"
        if name.endswith("-main")
        else "/user"
    )
    complete_get(phase, path, now=now)
    phase.finish(now=now)
    return phase
