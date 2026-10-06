"""Original-parent collection and native preparation for the Node group."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
from pathlib import Path

import pytest

SCRIPTS = Path(__file__).resolve().parents[2] / "eng/scripts"
SPEC = importlib.util.spec_from_file_location(
    "run_node_ci_group", SCRIPTS / "run_node_ci_group.py"
)
assert SPEC is not None
assert SPEC.loader is not None
sys.path.insert(0, str(SCRIPTS))
try:
    group = importlib.util.module_from_spec(SPEC)
    SPEC.loader.exec_module(group)
finally:
    sys.path.remove(str(SCRIPTS))

BEFORE, AFTER = "a" * 40, "b" * 40


def test_artifact_uses_exact_native_run_and_repository():
    """Artifact uses exact native run and repository."""
    group.validate_scope_artifact(
        {
            "id": 17,
            "name": "ci-scope",
            "expired": False,
            "workflow_run": {
                "id": 23,
                "repository_id": 41,
                "head_sha": "native-run-head",
            },
            "unrelatedNativeField": "ignored",
        },
        17,
        23,
        41,
    )


@pytest.mark.parametrize(
    "defect", ["id", "name", "expired", "run", "repository", "absent"]
)
def test_substituted_or_expired_artifact_cannot_enter_planning(defect):
    """Substituted or expired artifact cannot enter planning."""
    metadata = {
        "id": 17,
        "name": "ci-scope",
        "expired": False,
        "workflow_run": {"id": 23, "repository_id": 41},
    }
    if defect == "run":
        metadata["workflow_run"]["id"] = 24
    elif defect == "repository":
        metadata["workflow_run"]["repository_id"] = 42
    elif defect == "absent":
        del metadata["workflow_run"]
    else:
        metadata[defect] = {"id": 18, "name": "other", "expired": True}[defect]

    with pytest.raises(ValueError, match="native producer or lifetime"):
        group.validate_scope_artifact(metadata, 17, 23, 41)


def test_record_join_preserves_roles_and_received_scope(tmp_path, monkeypatch):
    """Record join preserves roles and received scope."""
    original = {
        "base": BEFORE,
        "candidate": AFTER,
        "endpoint_owners": {
            name: {
                "revision": revision,
                "paths": [{"path": "changed.py", "reasons": []}],
            }
            for name, revision in (("basis", BEFORE), ("candidate", AFTER))
        },
    }
    sources = ["docs/governance/controls.yaml", "mise.toml"]
    reasons = [
        {"owner": "record-control", "target": role, "sources": sources}
        for role in ("records:check", "records:test")
    ]
    endpoints = {name: tmp_path / name for name in ("basis", "candidate")}
    queries = []

    def owners(root, revision, checkout):
        queries.append((root, revision, checkout))
        return {"changed.py": reasons}

    monkeypatch.setattr(group.ci_scope, "record_control_owners", owners)
    joined = group.join_record_owners(tmp_path, original, endpoints)

    assert queries == [
        (tmp_path, BEFORE, endpoints["basis"]),
        (tmp_path, AFTER, endpoints["candidate"]),
    ]
    for name in endpoints:
        assert joined["endpoint_owners"][name]["paths"][0]["reasons"] == reasons
        assert original["endpoint_owners"][name]["paths"][0]["reasons"] == []
    assert joined["base"] == BEFORE
    assert joined["candidate"] == AFTER


@pytest.mark.parametrize(
    "defect", ["candidate", "full", "object", "abbreviated"]
)
def test_comparison_requires_exact_native_objects(
    tmp_path, monkeypatch, defect
):
    """Comparison requires exact native objects."""
    payload = {"base": BEFORE, "candidate": AFTER, "full": False}

    def native(_root, *arguments):
        if arguments == ("rev-parse", "HEAD"):
            return BEFORE if defect == "candidate" else AFTER
        return (
            "c" * 40
            if defect == "object"
            else arguments[-1].removesuffix("^{commit}")
        )

    monkeypatch.setattr(group.ci_scope, "git", native)
    if defect == "full":
        payload["full"] = True
    if defect == "abbreviated":
        payload["base"] = BEFORE[:12]

    with pytest.raises(
        ValueError, match=r"candidate|basis|unavailable|identities"
    ):
        group.comparison(tmp_path, payload)


@pytest.fixture
def caller(tmp_path, monkeypatch):
    """Caller."""
    root = tmp_path / "source"
    root.mkdir()
    received = tmp_path / "received.json"
    payload = {"base": BEFORE, "candidate": AFTER, "full": False}
    group.write_json(received, payload)
    directory = tmp_path / "owned"
    output = tmp_path / "github-output"
    monkeypatch.setenv("GITHUB_OUTPUT", str(output))
    monkeypatch.setattr(group, "comparison", lambda *_: (BEFORE, AFTER))
    monkeypatch.setattr(group, "owner_present", lambda *_: True)
    monkeypatch.setattr(
        group, "join_record_owners", lambda _root, scope, _endpoints: scope
    )
    keys = [
        {"target": "adopted", "check": check, "variant": runtime}
        for runtime, checks in (
            ("node22", ["build", "test"]),
            ("node24", ["build", "test", "pack", "contents", "consumer"]),
        )
        for check in checks
    ]
    parent = {
        "comparison": BEFORE,
        "candidate": AFTER,
        "scope": "node",
        "checks": [{"work": {"key": key}, "reasons": []} for key in keys],
    }
    plan = {
        "adopted": parent,
        "retained": [{"directory": "src/retained", "reasons": []}],
        "adoptedDirectory": "tests/fixture",
        "runtime": {
            "variants": [
                {"key": "node22", "selector": "22.x", "exactVersion": None},
                {
                    "key": "node24",
                    "selector": "24.x",
                    "exactVersion": "24.21.0",
                },
            ]
        },
    }
    calls, builds, executions = [], [], []
    state = {"missing": None, "basisOwner": True}

    def build(checkout, _carrier, name):
        builds.append((checkout, name))
        target = checkout / "output/WorkflowDelivery.dll"
        target.parent.mkdir()
        target.write_bytes(b"prepared controlled application")
        log = _carrier / (name + ".binlog")
        log.write_bytes(b"controlled operation")
        return log, target

    def native(checkout, _carrier, label, *arguments, required=True):
        calls.append((checkout, label, arguments, required))
        if label.endswith("-checkout"):
            Path(arguments[-2]).mkdir()
        if label == "planning":
            request = group.read_json(Path(arguments[-1]))
            assert request["candidateBuildLog"] == str(
                directory / "candidate.binlog"
            )
            assert request["basisBuildLog"] == (
                str(directory / "basis.binlog") if state["basisOwner"] else None
            )
            assert group.read_json(Path(request["scopePath"])) == payload
            return json.dumps(plan)
        if label.endswith("-execution"):
            assert required is False
            assert group.read_json(Path(arguments[-2])) == parent
            request = group.read_json(Path(arguments[-1]))
            assert request["checkout"] == str(checkout)
            assert not any(Path(request["scratch"]).iterdir())
            runtime = request["runtime"]
            executions.append((runtime, checkout, request["scratch"]))
            if state["missing"] == runtime:
                return ""
            return json.dumps(
                {
                    "candidate": AFTER,
                    "runtime": runtime,
                    "results": [
                        {"candidate": AFTER, "key": key, "status": "Passed"}
                        for key in keys
                        if key["variant"] == runtime
                    ],
                }
            )
        if label == "collection":
            assert group.read_json(Path(arguments[-2])) == parent
            results = group.read_json(Path(arguments[-1]))
            expected = keys if parent["checks"] else []
            if [result["key"] for result in results] != expected:
                raise subprocess.CalledProcessError(1, arguments)
            return json.dumps({"candidate": AFTER, "satisfied": True})
        return ""

    monkeypatch.setattr(group, "build", build)
    monkeypatch.setattr(group, "run", native)
    return (
        root,
        received,
        directory,
        output,
        plan,
        state,
        calls,
        builds,
        executions,
    )


def test_group_collects_both_isolated_runtimes_against_original_parent(caller):
    """Group collects both isolated runtimes against original parent."""
    root, received, directory, output, plan, _, calls, builds, executions = (
        caller
    )

    assert group.execute(root, received, directory) == plan

    assert builds == [
        (directory / "candidate", "candidate"),
        (directory / "basis", "basis"),
    ]
    assert [entry[0] for entry in executions] == ["node22", "node24"]
    assert len({entry[1] for entry in executions}) == 2
    assert len({entry[2] for entry in executions}) == 2
    installs = [
        (checkout, args)
        for checkout, label, args, _ in calls
        if label.endswith("-dependencies")
    ]
    assert len(installs) == 4
    assert all(
        args[-2:] == ("install", "--frozen-lockfile") for _, args in installs
    )
    assert group.read_json(directory / "outcome.json") == {
        "candidate": AFTER,
        "satisfied": True,
    }
    assert output.read_text() == (
        "retained=true\n"
        'retained_directories=["src/retained"]\n'
        "adopted_directory=tests/fixture\n"
    )


def test_empty_group_still_builds_control_without_product_runtimes(
    caller, monkeypatch
):
    """Empty group still builds control without product runtimes."""
    (
        root,
        received,
        directory,
        output,
        plan,
        state,
        calls,
        builds,
        executions,
    ) = caller
    plan["adopted"]["checks"] = []
    plan["retained"] = []
    state["basisOwner"] = False
    monkeypatch.setattr(
        group, "owner_present", lambda _root, revision: revision == AFTER
    )

    group.execute(root, received, directory)

    assert builds == [(directory / "candidate", "candidate")]
    assert executions == []
    assert all(
        not label.startswith(("node22", "node24")) for _, label, _, _ in calls
    )
    assert group.read_json(directory / "results.json") == []
    assert group.read_json(directory / "parent-plan.json")["candidate"] == AFTER
    assert output.read_text() == (
        "retained=false\nretained_directories=[]\n"
        "adopted_directory=tests/fixture\n"
    )


@pytest.mark.parametrize("missing", ["node22", "node24"])
def test_failed_runtime_cannot_omit_original_required_results(caller, missing):
    """Failed runtime cannot omit original required results."""
    root, received, directory, output, _, state, _, _, executions = caller
    state["missing"] = missing

    with pytest.raises(subprocess.CalledProcessError):
        group.execute(root, received, directory)

    assert [entry[0] for entry in executions] == ["node22", "node24"]
    assert all(
        result["key"]["variant"] != missing
        for result in group.read_json(directory / "results.json")
    )
    assert not output.exists()
    assert not (directory / "outcome.json").exists()


@pytest.mark.parametrize(
    ("exit_code", "timeout"),
    [(0, False), (1, False), (2, False), (3, False), (None, True)],
)
def test_native_command_retains_failure_or_timeout(
    tmp_path, monkeypatch, exit_code, timeout
):
    """Native command retains failure or timeout."""

    def native(*_args, **_kwargs):
        if timeout:
            raise subprocess.TimeoutExpired(
                ["native"], 900, b"partial", b"deadline"
            )
        return subprocess.CompletedProcess(
            ["native"], exit_code, b"partial", b"failure"
        )

    monkeypatch.setattr(group.subprocess, "run", native)
    assert group.run(
        tmp_path, tmp_path, "native", "native", required=False
    ) == ("partial" if exit_code in (0, 1) else "")
    observation = group.read_json(tmp_path / "native.command.json")
    assert observation == {
        "arguments": ["native"],
        "cwd": str(tmp_path),
        "exitCode": exit_code,
        "termination": "timedOut" if timeout else "exited",
    }
    assert (tmp_path / "native.stdout").read_bytes() == b"partial"
    assert (tmp_path / "native.stderr").read_bytes() == (
        b"deadline" if timeout else b"failure"
    )


@pytest.mark.parametrize("timeout", [False, True])
@pytest.mark.parametrize("reporting_fails", [False, True])
def test_required_native_failure_reports_streams_and_preserves_exception(
    tmp_path, monkeypatch, capsys, timeout, reporting_fails
):
    """Retain required failure details even if reporting itself fails."""
    calls = []
    error_output = b"deadline" if timeout else b"failure"

    def native(*args, **kwargs):
        calls.append((args, kwargs))
        if timeout:
            raise subprocess.TimeoutExpired(
                ["native"], 900, b"partial", error_output
            )
        return subprocess.CompletedProcess(
            ["native"], 2, b"partial", error_output
        )

    class BrokenStream:
        def write(self, _text):
            raise OSError

    monkeypatch.setattr(group.subprocess, "run", native)
    exception = (
        subprocess.TimeoutExpired if timeout else subprocess.CalledProcessError
    )
    with monkeypatch.context() as reporting:
        if reporting_fails:
            reporting.setattr(group.sys, "stderr", BrokenStream())
        with pytest.raises(exception) as captured:
            group.run(tmp_path, tmp_path, "required", "native")
    assert len(calls) == 1
    assert captured.value.cmd == ("native",)
    assert captured.value.output == b"partial"
    assert captured.value.stderr == error_output
    if timeout:
        assert captured.value.timeout == 900
    else:
        assert captured.value.returncode == 2
    output = capsys.readouterr()
    assert output.out == ""
    assert output.err == (
        "" if reporting_fails else "partial" + error_output.decode("utf-8")
    )
    assert (tmp_path / "required.stdout").read_bytes() == b"partial"
    assert (tmp_path / "required.stderr").read_bytes() == error_output
    assert group.read_json(tmp_path / "required.command.json") == {
        "arguments": ["native"],
        "cwd": str(tmp_path),
        "exitCode": None if timeout else 2,
        "termination": "timedOut" if timeout else "exited",
    }


def test_successful_native_json_remains_quiet(tmp_path, monkeypatch, capsys):
    """Return the original successful stdout without mixing diagnostics."""
    payload = b'{"value":1}'
    monkeypatch.setattr(
        group.subprocess,
        "run",
        lambda *_args, **_kwargs: subprocess.CompletedProcess(
            ["native"], 0, payload, b"warning"
        ),
    )
    assert group.run(tmp_path, tmp_path, "success", "native") == (
        payload.decode("utf-8")
    )
    output = capsys.readouterr()
    assert output.out == output.err == ""
    assert (tmp_path / "success.stdout").read_bytes() == payload
    assert (tmp_path / "success.stderr").read_bytes() == b"warning"


@pytest.mark.parametrize("probe_selected", [False, True])
def test_retained_filters_exclude_only_adopted_and_keep_selected_probes(
    tmp_path, monkeypatch, probe_selected
):
    """Preserve selected native tests and Hexo probes."""
    probe = "src/hexo"
    monkeypatch.setenv("PACKAGE_DIR", probe)
    selected = ["src/other", probe] if probe_selected else ["src/other"]
    calls = []
    monkeypatch.setattr(
        group,
        "run",
        lambda root, _carrier, label, *arguments: calls.append(
            (root, label, arguments)
        ),
    )

    group.run_retained(
        tmp_path, selected, "tests/adopted", tmp_path / "retained"
    )

    assert [label for _, label, _ in calls] == (
        ["typecheck", "test", "build", "packed"]
        if probe_selected
        else ["test", "build"]
    )
    expected_filters = tuple(
        argument for path in selected for argument in ("--filter", "./" + path)
    )
    for root, label, arguments in calls:
        assert root == tmp_path
        if label in {"test", "build"}:
            assert arguments == (
                "pnpm",
                *expected_filters,
                "--filter",
                "!./tests/adopted",
                "--recursive",
                "--if-present",
                "run",
                label,
            )
        else:
            assert arguments == (
                "pnpm",
                "--dir",
                probe,
                "run",
                "typecheck"
                if label == "typecheck"
                else "validate:packed-artifact",
            )


@pytest.mark.parametrize(
    "selected", [[], ["tests/adopted"], ["src/other", "src/other"], [None]]
)
def test_invalid_retained_selection_cannot_become_native_full_execution(
    tmp_path, selected
):
    """Reject invalid selection before native commands."""
    carrier = tmp_path / "retained"
    with pytest.raises(ValueError, match="distinct selected native members"):
        group.run_retained(tmp_path, selected, "tests/adopted", carrier)
    assert not carrier.exists()
