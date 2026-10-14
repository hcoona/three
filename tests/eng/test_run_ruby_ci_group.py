"""Original Ruby caller transfers and finite result collection."""

from __future__ import annotations

import importlib
import json
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

SCRIPTS = Path(__file__).resolve().parents[2] / "eng/scripts"
sys.path.insert(0, str(SCRIPTS))
try:
    group = importlib.import_module("run_ruby_ci_group")
finally:
    sys.path.remove(str(SCRIPTS))

BEFORE, AFTER = "a" * 40, "b" * 40


@pytest.fixture
def flow(tmp_path, monkeypatch):
    """Provide original endpoint and command transfers."""
    root = tmp_path / "repository"
    root.mkdir()
    directory = tmp_path / "state"
    directory.mkdir()
    context = {
        "basis": {"directory": str(tmp_path / "basis"), "reference": BEFORE},
        "candidate": {
            "directory": str(tmp_path / "candidate"),
            "reference": AFTER,
        },
        "full": False,
        "scopePath": str(tmp_path / "scope.json"),
        "application": str(tmp_path / "control/WorkflowDelivery.dll"),
        "helper": str(tmp_path / "ruby/facts.rb"),
    }
    group.node.write_json(directory / "context.json", context)
    calls = []
    state = {"checks": [], "missing": False, "failed": False}
    monkeypatch.setattr(
        group, "executable", lambda name: str(tmp_path / "tools" / name)
    )

    def run(_root, _directory, label, *args, **options):
        calls.append((label, args, options))
        if label == "preparation":
            request = group.node.read_json(Path(args[-1]))
            assert request["helper"] == context["helper"]
            endpoint = {
                name: {
                    "checkout": context[name],
                    "collection": {
                        "tools": {
                            "ruby": request["tools"]["ruby"],
                            "helper": request["helper"],
                            "scratch": str(directory / name),
                            "deadlineSeconds": 900,
                            "environment": {
                                "GEM_HOME": str(directory / name / "gems")
                            },
                        },
                        "nbgv": str(directory / "native/nbgv"),
                        "bundlerProbeVersion": "2.7.2",
                    },
                }
                for name in ("basis", "candidate")
            }
            return json.dumps({"request": endpoint, "commands": []})
        if label == "planning":
            return json.dumps(
                {
                    "plan": {
                        "comparison": BEFORE,
                        "candidate": AFTER,
                        "scope": group.SCOPE,
                        "checks": state["checks"],
                    },
                    **{
                        name: {
                            "facts": {
                                "revision": {
                                    "commit": context[name]["reference"]
                                },
                                "native": {
                                    "graph": {"projects": [], "bundles": []},
                                    "inputs": [],
                                },
                                "owners": [],
                                "operationInputs": [],
                            }
                        }
                        for name in ("basis", "candidate")
                    },
                }
            )
        if label == "execution":
            request = group.node.read_json(Path(args[-1]))
            assert request["checkout"] == context["candidate"]["directory"]
            assert request["facts"]["environment"]["GEM_HOME"] == str(
                directory / "candidate/gems"
            )
            assert (
                group.node.read_json(Path(args[-2]))["checks"]
                == state["checks"]
            )
            return json.dumps(
                {
                    "results": [] if state["missing"] else [{"native": True}],
                    "outcome": {
                        "comparison": BEFORE,
                        "candidate": AFTER,
                        "scope": group.SCOPE,
                        "satisfied": not state["missing"]
                        and not state["failed"],
                        "checks": [],
                        "errors": [],
                    },
                }
            )
        assert label == "collection"
        assert args[2:4] == ("ci", "result")
        results = group.node.read_json(Path(args[-1]))
        satisfied = not state["checks"] or (
            bool(results) and not state["failed"]
        )
        return json.dumps(
            {
                "comparison": BEFORE,
                "candidate": AFTER,
                "scope": group.SCOPE,
                "satisfied": satisfied,
                "checks": [],
                "errors": [],
            }
        )

    monkeypatch.setattr(group.node, "run", run)
    return SimpleNamespace(
        root=root,
        directory=directory,
        calls=calls,
        state=state,
        context=context,
    )


def test_planning_retains_original_paired_inputs_without_recollection(flow):
    """Both endpoints are retained by the original planning command."""
    group.plan(flow.root, flow.directory)
    contribution = group.node.read_json(flow.directory / "node-inputs.json")
    assert contribution["basis"]["revision"]["commit"] == BEFORE
    assert contribution["candidate"]["revision"]["commit"] == AFTER
    assert [call[0] for call in flow.calls] == ["preparation", "planning"]
    assert "plan" not in group.node.read_json(flow.directory / "readback.json")


@pytest.mark.parametrize(
    ("missing", "failed"), [(False, False), (True, False), (False, True)]
)
def test_execution_owner_retains_required_failure_outcome(
    flow, missing, failed
):
    """The bound execution outcome retains required failure."""
    flow.state.update(
        checks=[{"original": "selected"}], missing=missing, failed=failed
    )
    group.plan(flow.root, flow.directory)
    before = (flow.directory / "plan.json").read_bytes()
    outcome = group.execute(flow.root, flow.directory)
    assert outcome["satisfied"] is (not missing and not failed)
    assert outcome["candidate"] == AFTER
    assert (flow.directory / "plan.json").read_bytes() == before
    assert [call[0] for call in flow.calls] == [
        "preparation",
        "planning",
        "execution",
    ]


def test_empty_selection_collects_without_product_preparation(flow):
    """No selected checks create no product execution scratch."""
    group.plan(flow.root, flow.directory)
    outcome = group.execute(flow.root, flow.directory)
    assert outcome["satisfied"] is True
    assert outcome["comparison"] == BEFORE
    assert outcome["candidate"] == AFTER
    assert [call[0] for call in flow.calls] == [
        "preparation",
        "planning",
        "collection",
    ]
    assert not (flow.directory / "execution").exists()


@pytest.mark.parametrize("defect", ["comparison", "candidate", "scope"])
def test_substituted_plan_stops_before_execution(flow, defect):
    """A changed comparison cannot reach execution."""
    group.plan(flow.root, flow.directory)
    plan = group.node.read_json(flow.directory / "plan.json")
    plan[defect] = "substituted"
    group.node.write_json(flow.directory / "plan.json", plan)
    with pytest.raises(ValueError, match="different binding"):
        group.execute(flow.root, flow.directory)
    assert [call[0] for call in flow.calls] == ["preparation", "planning"]


@pytest.mark.parametrize("full", [False, True])
def test_receiver_materializes_both_exact_checkouts(tmp_path, full):
    """Native Git establishes both endpoint identities before preparation."""
    root = tmp_path / "repository"
    root.mkdir()
    hooks = tmp_path / "hooks"
    hooks.mkdir()

    def git(*args):
        return subprocess.check_output(  # noqa: S603 - Local Git fixture.
            [  # noqa: S607 - Native local Git fixture.
                "git",
                "-c",
                "core.hooksPath=" + str(hooks),
                "-c",
                "user.name=Caller Fixture",
                "-c",
                "user.email=caller@example.invalid",
                *args,
            ],
            cwd=root,
            text=True,
        ).strip()

    git("init", "-q")
    (root / "original.txt").write_text("basis")
    git("add", ".")
    git("commit", "-qm", "Basis")
    before = git("rev-parse", "HEAD")
    (root / "original.txt").write_text("candidate")
    git("add", ".")
    git("commit", "-qm", "Candidate")
    after = git("rev-parse", "HEAD")
    if full:
        before = after
    scope = tmp_path / "scope.json"
    group.node.write_json(
        scope, {"base": before, "candidate": after, "full": full}
    )
    transfer = tmp_path / "transfer"
    (transfer / "control").mkdir(parents=True)
    (transfer / "ruby").mkdir()
    (transfer / "control/WorkflowDelivery.dll").write_bytes(b"control")
    (transfer / "ruby/facts.rb").write_bytes(b"original helper")
    directory = tmp_path / "group"
    context = group.materialize(root, scope, transfer, directory)
    for name, revision in (("basis", before), ("candidate", after)):
        endpoint = Path(context[name]["directory"])
        assert (
            subprocess.check_output(
                ["git", "rev-parse", "HEAD"],  # noqa: S607 - Native identity.
                cwd=endpoint,
                text=True,
            ).strip()
            == revision
        )
        assert context[name]["reference"] == revision
    assert context["basis"]["directory"] != context["candidate"]["directory"]
    assert Path(context["helper"]).read_bytes() == b"original helper"
    assert not (directory / "query-preparation").exists()
