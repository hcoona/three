# ruff: noqa: ARG001, E501, EM101, EM102, PLR2004, SIM102, SLF001, TRY003
"""Tests for workflow-release planner core."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from dataclasses import replace
from pathlib import Path
from typing import TYPE_CHECKING, Any, Literal, cast

import pytest
from three_workflow_release_authoring import (
    Artifact,
    AuthoringSnapshot,
    ProjectDescriptor,
    TargetUsage,
    Variant,
    validate_authoring,
)
from three_workflow_release_contracts import validate_contract
from three_workflow_release_planner import (
    PlannerError,
    PlannerInputs,
    plan_release,
)
from three_workflow_release_planner.cli import main as cli_main

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

REPO_ROOT = Path(__file__).parents[5]
_GIT_SHOW_ARG_COUNT = 3
SHA = subprocess.run(  # noqa: S603
    [shutil.which("git") or "git", "rev-parse", "HEAD"],
    cwd=REPO_ROOT,
    check=True,
    capture_output=True,
    text=True,
).stdout.strip()
RemoteObservation = Literal[
    "absent",
    "exact-satisfied",
    "partial",
    "partial-authoritative",
    "conflicting",
]
_SPLIT_GITHUB_RELEASE_NODE_COUNT = 2


@pytest.fixture(autouse=True)
def _direct_nbgv_for_planner_tests(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Provide direct fake NBGV responses and reject planner-side builds."""
    real_run = subprocess.run

    def fake_run(
        args: Sequence[str],
        *pargs: Any,
        **kwargs: Any,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if Path(str(args[0])).name == "nbgv" and "get-version" in args:
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps({"SemVer2": "1.2.3"}),
                "",
            )
        return real_run(args, *pargs, **kwargs)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )


def _assert_no_planner_build_command(args: Sequence[str]) -> None:
    """Fail tests when planner code attempts package/build execution."""
    command = Path(str(args[0])).name if args else ""
    tokens = [str(arg) for arg in args]
    if command == "dotnet" and len(tokens) > 1 and tokens[1] == "pack":
        raise AssertionError(f"planner must not run dotnet pack: {tokens}")
    if command == "uv" and len(tokens) > 1 and tokens[1] == "build":
        raise AssertionError(f"planner must not run uv build: {tokens}")
    if command == "gem" and len(tokens) > 1 and tokens[1] == "build":
        raise AssertionError(f"planner must not run gem build: {tokens}")
    if command == "npm" and len(tokens) > 1:
        if tokens[1] == "pack" or tokens[1:3] in (
            ["run", "build"],
            ["run", "prepack"],
        ):
            raise AssertionError(
                f"planner must not run npm build/pack: {tokens}"
            )
    if command == "pnpm":
        if "pack" in tokens or tokens[-2:] in (
            ["run", "build"],
            ["run", "prepack"],
        ):
            raise AssertionError(
                f"planner must not run pnpm build/pack: {tokens}"
            )


def _request(
    project_ids: list[str] | None = None,
    *,
    profile: str = "buddy",
    force: bool = False,
):
    """Return a closed planner request."""
    requested_project_ids = (
        ["nbgv-python"] if project_ids is None else sorted(project_ids)
    )
    return {
        "api-version": "three.release.planner-request/v1alpha1",
        "kind": "planner-request",
        "profile": profile,
        "commit-sha": SHA,
        "requested-project-ids": requested_project_ids,
        "request-flags": {"force": force},
    }


def _plan(
    project_ids: list[str] | None = None,
    *,
    dry_run: bool = False,
    validation_build: bool = False,
):
    """Plan selected projects from checked-in authoring."""
    snapshot = validate_authoring(REPO_ROOT)
    remote_observations = None
    if not dry_run and not validation_build:
        bootstrap = plan_release(
            snapshot,
            PlannerInputs(
                request=_request(project_ids),
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )
        remote_observations = _remote_observations(bootstrap.plan)
    return plan_release(
        snapshot,
        PlannerInputs(
            request=_request(project_ids),
            repo_root=REPO_ROOT,
            dry_run=dry_run,
            validation_build=validation_build,
            remote_observations=remote_observations,
        ),
    )


def _publish_nodes(plan: Mapping[str, object]) -> Mapping[str, object]:
    """Return publish nodes from a plan object."""
    graph = cast("Mapping[str, object]", plan["graph"])
    return cast("Mapping[str, object]", graph["publish-nodes"])


def _execution_ids(
    execution_sets: Mapping[str, object], key: str
) -> Sequence[str]:
    """Return a typed execution-set id list."""
    return cast("Sequence[str]", execution_sets[key])


def _assert_publish_nodes_embed_ids(plan: Mapping[str, object]) -> None:
    """Assert every publish node snapshot carries its containing id."""
    for node_id, node in _publish_nodes(plan).items():
        payload = cast("Mapping[str, object]", node)
        assert payload["publish-node-id"] == node_id


def _remote_observations(
    plan: Mapping[str, object],
    overrides: Mapping[str, RemoteObservation] | None = None,
) -> dict[str, RemoteObservation]:
    """Return explicit absent remote observations, with selected overrides."""
    observations = cast(
        "dict[str, RemoteObservation]",
        dict.fromkeys(_publish_nodes(plan), "absent"),
    )
    observations.update(overrides or {})
    return observations


def _dotnet_metadata(snapshot: AuthoringSnapshot) -> dict[str, object]:
    """Return valid synthetic .NET metadata for planner tests."""
    metadata_input = snapshot.dotnet_metadata_input(SHA)
    projects = cast(
        "Mapping[str, Mapping[str, object]]", metadata_input["projects"]
    )
    metadata_projects: dict[str, object] = {}
    for project_id, project in projects.items():
        entry = {
            "descriptor-path": project["descriptor-path"],
            "primary-manifest-path": project["primary-manifest-path"],
            "resolved-version": "1.2.3",
        }
        if project["requires-package-id"] is True:
            entry["package-id"] = project_id
        metadata_projects[project_id] = entry
    return {
        "api-version": "three.release.dotnet-planner-metadata/v1alpha1",
        "kind": "dotnet-planner-metadata",
        "commit-sha": SHA,
        "projects": metadata_projects,
    }


def _github_release_node_id(plan: Mapping[str, object]) -> str:
    """Return the GitHub Release publish node id from a plan object."""
    return _publish_node_id_for_family(plan, "github-release")


def _snapshot_with_split_github_release_targets(
    snapshot: AuthoringSnapshot,
    project_id: str,
    *,
    profile: str = "buddy",
) -> AuthoringSnapshot:
    """Return a snapshot whose GitHub Release target is split per artifact."""
    project = snapshot.projects[project_id]
    github_release_usage = next(
        usage
        for usage in project.profiles[profile]
        if usage.uses == "github-release/public"
    )
    split_usages = tuple(
        replace(github_release_usage, artifacts=(artifact_id,))
        for artifact_id in github_release_usage.artifacts
    )
    profiles = dict(project.profiles)
    profiles[profile] = (
        tuple(
            usage
            for usage in project.profiles[profile]
            if usage.uses != "github-release/public"
        )
        + split_usages
    )
    projects = dict(snapshot.projects)
    projects[project_id] = replace(project, profiles=profiles)
    return replace(snapshot, projects=projects)


def _publish_node_ids_for_family(
    plan: Mapping[str, object],
    family: str,
) -> list[str]:
    """Return publish node ids for a target family from a plan object."""
    graph = cast("Mapping[str, object]", plan["graph"])
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        graph["target-instance-snapshots"],
    )
    return [
        node_id
        for node_id, node in _publish_nodes(plan).items()
        if snapshots[
            str(
                cast("Mapping[str, object]", node)[
                    "target-instance-snapshot-id"
                ]
            )
        ]["family"]
        == family
    ]


def _publish_node_id_for_family(plan: Mapping[str, object], family: str) -> str:
    """Return the publish node id for a target family from a plan object."""
    graph = cast("Mapping[str, object]", plan["graph"])
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        graph["target-instance-snapshots"],
    )
    for node_id, node in _publish_nodes(plan).items():
        target_id = str(
            cast("Mapping[str, object]", node)["target-instance-snapshot-id"]
        )
        if snapshots[target_id]["family"] == family:
            return node_id
    raise AssertionError


def _fake_git_worktree(
    args: Sequence[str],
) -> subprocess.CompletedProcess[str] | None:
    """Handle planner-created throwaway worktrees in subprocess fakes."""
    if "worktree" not in args:
        return None
    if "add" in args:
        return subprocess.CompletedProcess(args, 0, "", "")
    if "remove" in args or "prune" in args:
        return subprocess.CompletedProcess(args, 0, "", "")
    return None


def _fake_git_show_pyproject(
    args: Sequence[str],
    *,
    name: str,
    version: str | None,
) -> subprocess.CompletedProcess[str] | None:
    """Handle requested-commit pyproject reads in subprocess fakes."""
    if handled := _fake_git_show_epoch(args):
        return handled
    if (
        len(args) < _GIT_SHOW_ARG_COUNT
        or args[1] != "show"
        or ":" not in args[2]
    ):
        return None
    body = f'[project]\nname = "{name}"\n'
    if version is None:
        body += 'dynamic = ["version"]\n'
    else:
        body += f'version = "{version}"\n'
    return subprocess.CompletedProcess(args, 0, body, "")


def _fake_git_show_json(
    args: Sequence[str],
    *,
    path: str,
    payload: Mapping[str, object],
) -> subprocess.CompletedProcess[str] | None:
    """Handle requested-commit JSON reads in subprocess fakes."""
    if handled := _fake_git_show_epoch(args):
        return handled
    if (
        len(args) < _GIT_SHOW_ARG_COUNT
        or args[1] != "show"
        or ":" not in args[2]
    ):
        return None
    if args[2] != f"{SHA}:{path}":
        return None
    return subprocess.CompletedProcess(args, 0, json.dumps(payload), "")


def _fake_git_show_epoch(
    args: Sequence[str], timestamp: str = "1712345678"
) -> subprocess.CompletedProcess[str] | None:
    """Handle requested-commit SOURCE_DATE_EPOCH reads in subprocess fakes."""
    if len(args) >= 5 and args[1:4] == ["show", "-s", "--format=%ct"]:
        return subprocess.CompletedProcess(args, 0, timestamp, "")
    return None


def _fake_nbgv_get_version(
    args: Sequence[str],
    *,
    semver2: str,
) -> subprocess.CompletedProcess[str] | None:
    """Handle requested-commit NBGV version queries in subprocess fakes."""
    if handled := _fake_git_show_epoch(args):
        return handled
    if "get-version" not in args:
        return None
    return subprocess.CompletedProcess(
        args,
        0,
        json.dumps({"SemVer2": semver2}),
        "",
    )


def _remove_flat_scratch(path: Path) -> None:
    """Remove planner test scratch directories containing only files."""
    if not path.exists():
        return
    for child in path.iterdir():
        if child.is_file():
            child.unlink()
    path.rmdir()


def test_build_system_nbgv_uses_trusted_cli_path(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve build-system NBGV versions through the trusted CLI path."""
    snapshot = validate_authoring(REPO_ROOT)
    trusted_nbgv = "/trusted/tools/nbgv"
    observed_args: list[Sequence[str]] = []

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        observed_args.append(tuple(args))
        if args[:2] == ["dotnet", "tool"]:
            raise AssertionError("planner must not invoke dotnet tool run nbgv")
        if args[0] != trusted_nbgv:
            raise AssertionError(args)
        return subprocess.CompletedProcess(
            args,
            0,
            json.dumps({"SemVer2": "4.5.6"}),
            "",
        )

    monkeypatch.setenv("THREE_WORKFLOW_RELEASE_NBGV_PATH", trusted_nbgv)
    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )

    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-wxt"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )

    envelope = cast("Mapping[str, object]", result.plan["envelope"])
    projects = cast("Mapping[str, object]", envelope["projects"])
    project = cast("Mapping[str, object]", projects["hcoona-release-smoke-wxt"])
    assert project["resolved-version"] == "4.5.6"
    assert observed_args
    assert all("dotnet" not in args[:1] for args in observed_args)


def test_nbgv_python_uses_checked_in_pyproject_version() -> None:
    """Resolve nbgv-python from pyproject.toml without NBGV metadata."""
    result = _plan(["nbgv-python"])
    validate_contract(result.plan)
    _assert_publish_nodes_embed_ids(result.plan)
    envelope = cast("Mapping[str, object]", result.plan["envelope"])
    projects = cast("Mapping[str, object]", envelope["projects"])
    project = cast("Mapping[str, object]", projects["nbgv-python"])
    assert project["resolved-version"] == "2.1.0.dev1"
    publish_nodes = _publish_nodes(result.plan)
    nodes = [
        cast("Mapping[str, object]", node) for node in publish_nodes.values()
    ]
    identities = [
        cast("Mapping[str, str]", node["resolved-publish-identity"])
        for node in nodes
    ]
    assert {identity["release-tag"] for identity in identities} == {
        "release/nbgv-python/v2.1.0.dev1"
    }


def test_nbgv_python_pyproject_version_reads_requested_commit(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve nbgv-python version from requested commit, not ambient HEAD."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if args[1] == "show" and len(args) > 2 and ":" in args[2]:
            assert args[2] == f"{SHA}:src/public/lib/nbgv-python/pyproject.toml"
            return subprocess.CompletedProcess(
                args,
                0,
                """
[project]
name = "nbgv-python"
version = "7.8.9.dev1"
""",
                "",
            )
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    envelope = cast("Mapping[str, object]", result.plan["envelope"])
    projects = cast("Mapping[str, object]", envelope["projects"])
    project = cast("Mapping[str, object]", projects["nbgv-python"])
    assert project["resolved-version"] == "7.8.9.dev1"


def test_pypi_build_system_nbgv_identity_uses_pep440_projection_without_build(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve PyPI build-system NBGV identity without planner-side builds."""
    snapshot = validate_authoring(REPO_ROOT)
    captured_commands: list[list[str]] = []

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        captured_commands.append(list(args))
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="7.8.9-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["version"] == "7.8.9.dev1"
    projection = cast("Mapping[str, object]", node["projection"])
    filenames = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert sorted(filenames.values()) == [
        "hcoona_release_smoke_pypi-7.8.9.dev1-py3-none-any.whl",
        "hcoona_release_smoke_pypi-7.8.9.dev1.tar.gz",
    ]
    assert not any("worktree" in command for command in captured_commands)


def test_official_force_request_is_preserved_for_tag_retarget() -> None:
    """Allow official force for tag retarget intent."""
    snapshot = validate_authoring(REPO_ROOT)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-pypi"],
                profile="official",
                force=True,
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    envelope = cast("Mapping[str, object]", result.plan["envelope"])
    assert envelope["request-flags"] == {"force": True}


def test_nbgv_python_requested_commit_pyproject_failure_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fail closed when the requested-commit pyproject.toml cannot be read."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(args, 128, "", "fatal: bad object")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"]),
                repo_root=REPO_ROOT,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "VERSION_AUTHORITY_FAILED"


def test_dry_run_defaults_to_no_build_or_publish() -> None:
    """Ordinary dry-runs suppress build and publish fan-out."""
    execution_sets = _plan(["nbgv-python"], dry_run=True).execution_sets
    assert execution_sets["dry-run"] is True
    assert execution_sets["validation-build"] is False
    assert execution_sets["publish-intent-node-ids"]
    assert execution_sets["active-publish-node-ids"] == []
    assert execution_sets["active-variant-ids"] == []


def test_validation_build_runs_build_selectors_only() -> None:
    """Validation-build dry-runs run build but not publish selectors."""
    execution_sets = _plan(
        ["nbgv-python"], dry_run=True, validation_build=True
    ).execution_sets
    assert execution_sets["active-publish-node-ids"] == []
    assert execution_sets["active-variant-ids"]


def test_validation_build_without_dry_run_fails_closed() -> None:
    """Reject invalid validation-build controls before plan emission."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"]),
                repo_root=REPO_ROOT,
                validation_build=True,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REQ_INVALID_INPUT"


def test_exact_satisfied_routes_to_skip_selector() -> None:
    """Model exact-satisfied as skip-satisfied with no active publish node."""
    first = _plan(["nbgv-python"])
    publish_nodes = _publish_nodes(first.plan)
    node_id = next(iter(publish_nodes))
    snapshot = validate_authoring(REPO_ROOT)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"]),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                first.plan, {node_id: "exact-satisfied"}
            ),
        ),
    )
    skip_nodes = cast(
        "Sequence[str]",
        result.execution_sets["skip-satisfied-publish-node-ids"],
    )
    publish_nodes = cast(
        "Sequence[str]", result.execution_sets["publish-intent-node-ids"]
    )
    assert node_id in skip_nodes
    assert node_id not in publish_nodes


def test_github_release_mixed_siblings_keep_skip_satisfied() -> None:
    """Same-release exact siblings stay skipped while repair stays active."""
    base_snapshot = validate_authoring(REPO_ROOT)
    snapshot = _snapshot_with_split_github_release_targets(
        base_snapshot,
        "nbgv-python",
    )
    bootstrap = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], force=True),
            repo_root=REPO_ROOT,
            dry_run=True,
            official_frozen_versions={"nbgv-python": ()},
        ),
    )
    node_ids = sorted(
        _publish_node_ids_for_family(bootstrap.plan, "github-release")
    )
    assert len(node_ids) == _SPLIT_GITHUB_RELEASE_NODE_COUNT

    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], force=True),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                bootstrap.plan,
                {
                    node_ids[0]: "exact-satisfied",
                    node_ids[1]: "partial",
                },
            ),
            official_frozen_versions={"nbgv-python": ()},
        ),
    )

    satisfied_node_id = node_ids[0]
    repair_node_id = node_ids[1]
    skip_node_ids = set(
        _execution_ids(result.execution_sets, "skip-satisfied-publish-node-ids")
    )
    publish_intent_ids = set(
        _execution_ids(result.execution_sets, "publish-intent-node-ids")
    )
    active_publish_ids = set(
        _execution_ids(result.execution_sets, "active-publish-node-ids")
    )
    active_github_release_ids = set(
        _execution_ids(
            result.execution_sets,
            "active-github-release-publish-node-ids",
        )
    )
    selected_github_release_ids = set(
        _execution_ids(
            result.execution_sets,
            "selected-github-release-publish-node-ids",
        )
    )

    assert satisfied_node_id in skip_node_ids
    assert repair_node_id not in skip_node_ids
    assert satisfied_node_id not in publish_intent_ids
    assert repair_node_id in publish_intent_ids
    assert satisfied_node_id not in active_publish_ids
    assert repair_node_id in active_publish_ids
    assert satisfied_node_id not in active_github_release_ids
    assert repair_node_id in active_github_release_ids
    assert set(node_ids) <= selected_github_release_ids

    publish_nodes = _publish_nodes(result.plan)
    satisfied_node = cast(
        "Mapping[str, object]", publish_nodes[satisfied_node_id]
    )
    repair_node = cast("Mapping[str, object]", publish_nodes[repair_node_id])
    assert satisfied_node["publish-disposition"] == "skip-satisfied"
    assert "publish-mode" not in satisfied_node
    assert "overwrite-mutable-authorization" not in satisfied_node
    assert repair_node["publish-disposition"] == "publish"
    assert repair_node["publish-mode"] == "overwrite-mutable"


def test_pypi_exact_satisfied_routes_to_skip_selector(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Model exact-satisfied PyPI versions as immutable replay skips."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(first.plan, "pypi")
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                first.plan, {node_id: "exact-satisfied"}
            ),
        ),
    )

    assert node_id in _execution_ids(
        result.execution_sets, "skip-satisfied-publish-node-ids"
    )
    assert node_id not in _execution_ids(
        result.execution_sets, "publish-intent-node-ids"
    )


def test_live_pypi_without_remote_observation_remains_gateable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """External OIDC targets without observations remain gate-controlled."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    bootstrap = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    pypi_node_id = _publish_node_id_for_family(bootstrap.plan, "pypi")
    observations = _remote_observations(bootstrap.plan)
    del observations[pypi_node_id]
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            remote_observations=observations,
        ),
    )

    assert pypi_node_id in _execution_ids(
        result.execution_sets, "publish-intent-node-ids"
    )
    assert pypi_node_id in _execution_ids(
        result.execution_sets, "active-publish-node-ids"
    )


def test_official_github_release_conflicting_replay_fails_closed() -> None:
    """Official conflicting GitHub Release replay fails closed."""
    snapshot = validate_authoring(REPO_ROOT)
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(first.plan)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], profile="official"),
                repo_root=REPO_ROOT,
                remote_observations=_remote_observations(
                    first.plan, {node_id: "conflicting"}
                ),
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REMOTE_CONFLICTING"


def test_official_github_release_absent_observation_plans_new_publication() -> (
    None
):
    """Official absent GitHub Release observation plans create-only publish."""
    snapshot = validate_authoring(REPO_ROOT)
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(first.plan)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                first.plan, {node_id: "absent"}
            ),
        ),
    )

    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    assert node["publish-disposition"] == "publish"
    assert node["publish-mode"] == "create-only"
    assert node_id in _execution_ids(
        result.execution_sets,
        "active-github-release-publish-node-ids",
    )


def test_buddy_force_github_release_conflicting_replay_fails_closed() -> None:
    """Buddy force conflicting GitHub Release replay fails closed."""
    first = _plan(["nbgv-python"])
    node_id = _github_release_node_id(first.plan)
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                remote_observations=_remote_observations(
                    first.plan, {node_id: "conflicting"}
                ),
                official_frozen_versions={"nbgv-python": ()},
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REMOTE_CONFLICTING"


def test_official_partial_github_release_fails_closed() -> None:
    """Official partial GitHub Release replay fails closed."""
    snapshot = validate_authoring(REPO_ROOT)
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(first.plan)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], profile="official"),
                repo_root=REPO_ROOT,
                remote_observations=_remote_observations(
                    first.plan, {node_id: "partial"}
                ),
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REMOTE_CONFLICTING"
    assert error.value.diagnostics[0]["details"] == {
        "remote-observation": "partial"
    }


def test_official_github_release_prerelease_promotion_replaces_authoritative() -> (
    None
):
    """Official same-tag prerelease-to-release replay uses replace-authoritative."""
    snapshot = validate_authoring(REPO_ROOT)
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(first.plan)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], profile="official"),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                first.plan, {node_id: "partial-authoritative"}
            ),
        ),
    )

    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    assert node["publish-disposition"] == "publish"
    assert node["publish-mode"] == "replace-authoritative"


def test_circular_list_github_release_absent_plans_successfully() -> None:
    """Circular-list GitHub Release absent observation plans create-only."""
    snapshot = validate_authoring(REPO_ROOT)
    first = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["circular-list"], profile="official"),
            repo_root=REPO_ROOT,
            dotnet_metadata=_dotnet_metadata(snapshot),
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(first.plan)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["circular-list"], profile="official"),
            repo_root=REPO_ROOT,
            dotnet_metadata=_dotnet_metadata(snapshot),
            remote_observations={node_id: "absent"},
        ),
    )

    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast("Mapping[str, str]", projection["asset-names-by-artifact-id"])

    assert node["publish-disposition"] == "publish"
    assert node["publish-mode"] == "create-only"
    assert sorted(names.values()) == [
        "circular-list.1.2.3.nupkg",
        "circular-list.1.2.3.snupkg",
    ]
    assert "asset-sizes-by-artifact-id" not in projection
    assert "asset-sha256-by-artifact-id" not in projection
    assert node_id in _execution_ids(
        result.execution_sets, "active-publish-node-ids"
    )


def test_github_packages_nuget_absent_observation_plans_first_publish() -> None:
    """GitHub Packages NuGet stays absent from active official plans."""
    snapshot = validate_authoring(REPO_ROOT)
    metadata = _dotnet_metadata(snapshot)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-github-packages"], profile="official"
            ),
            repo_root=REPO_ROOT,
            dotnet_metadata=metadata,
            dry_run=True,
        ),
    )
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        cast("Mapping[str, object]", result.plan["graph"])[
            "target-instance-snapshots"
        ],
    )

    assert all(snapshot["family"] != "nuget" for snapshot in snapshots.values())


def test_github_packages_nuget_exact_observation_skips_replay() -> None:
    """GitHub Packages NuGet stays absent from active buddy plans."""
    snapshot = validate_authoring(REPO_ROOT)
    metadata = _dotnet_metadata(snapshot)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-github-packages"], profile="buddy"
            ),
            repo_root=REPO_ROOT,
            dotnet_metadata=metadata,
            dry_run=True,
        ),
    )
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        cast("Mapping[str, object]", result.plan["graph"])[
            "target-instance-snapshots"
        ],
    )

    assert all(snapshot["family"] != "nuget" for snapshot in snapshots.values())


@pytest.mark.parametrize(
    ("project_id", "target_id", "package_name"),
    [
        (
            "hcoona-release-smoke-npm",
            "npm/github-packages",
            "@hcoona/hcoona-release-smoke-npm",
        ),
        (
            "hcoona-release-smoke-npm-dual",
            "npm/github-packages",
            "@hcoona/hcoona-release-smoke-npm-dual",
        ),
        (
            "hcoona-release-smoke-rubygems",
            "rubygems/github-packages",
            "hcoona-release-smoke-rubygems",
        ),
    ],
)
def test_buddy_smoke_projects_plan_github_packages_publish(
    project_id: str, target_id: str, package_name: str
) -> None:
    """Buddy smoke plans include supported GitHub Packages targets."""
    snapshot = validate_authoring(REPO_ROOT)
    dotnet_metadata = _dotnet_metadata(snapshot)
    dotnet_projects = cast(
        "dict[str, dict[str, object]]", dotnet_metadata["projects"]
    )
    dotnet_projects["hcoona-release-smoke-github-packages"]["package-id"] = (
        "Hcoona.ReleaseSmoke.GithubPackages"
    )
    dotnet_projects["hcoona-release-smoke-nuget"]["package-id"] = (
        "Hcoona.ReleaseSmoke.Nuget"
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request([project_id], profile="buddy"),
            repo_root=REPO_ROOT,
            dotnet_metadata=dotnet_metadata,
            dry_run=True,
        ),
    )

    matching_nodes = [
        cast("Mapping[str, object]", node)
        for node in _publish_nodes(result.plan).values()
        if cast("Mapping[str, object]", node)["target-instance-snapshot-id"]
        == target_id
    ]

    assert len(matching_nodes) == 1
    assert matching_nodes[0]["profile"] == "buddy"
    identity = cast(
        "Mapping[str, object]", matching_nodes[0]["resolved-publish-identity"]
    )
    assert identity["package-name"] == package_name


@pytest.mark.parametrize(
    "project_id",
    [
        "hcoona-release-smoke-npm",
        "hcoona-release-smoke-rubygems",
    ],
)
def test_buddy_mixed_github_release_fails_closed_when_deactivated(
    project_id: str,
) -> None:
    """Buddy GitHub Release deactivation fails closed even with registries."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request([project_id], profile="buddy"),
                repo_root=REPO_ROOT,
                dry_run=True,
                deactivate_buddy_github_release=True,
            ),
        )

    assert error.value.diagnostics[0]["code"] == "IMMUTABLE_PROOF_UNAVAILABLE"
    assert error.value.diagnostics[0]["details"] == {
        "target": "github-release/public"
    }


def test_buddy_github_release_target_remains_fail_closed_without_deactivation() -> (
    None
):
    """Unsupported buddy GitHub Release remains guarded unless deactivated."""
    snapshot = validate_authoring(REPO_ROOT)
    bootstrap = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-npm"], profile="buddy"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-npm"], profile="buddy"),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(bootstrap.plan),
        ),
    )

    assert result.execution_sets["active-github-release-publish-node-ids"]
    assert result.execution_sets["active-publish-node-ids"]


def test_buddy_github_release_only_target_fails_closed_when_deactivated() -> (
    None
):
    """Buddy GitHub Release-only plans fail closed when deactivated."""
    snapshot = validate_authoring(REPO_ROOT)

    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(
                    ["hcoona-release-smoke-github-release"],
                    profile="buddy",
                ),
                repo_root=REPO_ROOT,
                dotnet_metadata=_dotnet_metadata(snapshot),
                dry_run=True,
                deactivate_buddy_github_release=True,
            ),
        )

    assert error.value.diagnostics[0]["code"] == ("IMMUTABLE_PROOF_UNAVAILABLE")


@pytest.mark.parametrize("profile", ["buddy", "official"])
def test_nuget_rejects_deferred_package_id_coexistence_conflict(
    profile: str,
) -> None:
    """Deferred NuGet registry targets do not reach coexistence planning."""
    snapshot = validate_authoring(REPO_ROOT)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-nuget"], profile=profile),
            repo_root=REPO_ROOT,
            dotnet_metadata=_dotnet_metadata(snapshot),
            dry_run=True,
        ),
    )
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        cast("Mapping[str, object]", result.plan["graph"])[
            "target-instance-snapshots"
        ],
    )

    assert set(snapshots) == {"github-release/public"}
    assert all(snapshot["family"] != "nuget" for snapshot in snapshots.values())


@pytest.mark.parametrize("profile", ["buddy", "official"])
def test_github_packages_same_name_allowed_profile_coexistence(
    profile: str,
) -> None:
    """Deferred GitHub Packages NuGet targets do not reach active plans."""
    snapshot = validate_authoring(REPO_ROOT)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-github-packages"], profile=profile
            ),
            repo_root=REPO_ROOT,
            dotnet_metadata=_dotnet_metadata(snapshot),
            dry_run=True,
        ),
    )
    snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        cast("Mapping[str, object]", result.plan["graph"])[
            "target-instance-snapshots"
        ],
    )

    assert set(snapshots) == {"github-release/public"}
    assert all(snapshot["family"] != "nuget" for snapshot in snapshots.values())


def test_buddy_force_partial_github_release_overwrites_mutable() -> None:
    """Buddy force partial GitHub Release replay uses overwrite-mutable."""
    first = _plan(["nbgv-python"])
    node_id = _github_release_node_id(first.plan)
    snapshot = validate_authoring(REPO_ROOT)
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["nbgv-python"], force=True),
            repo_root=REPO_ROOT,
            remote_observations=_remote_observations(
                first.plan, {node_id: "partial"}
            ),
            official_frozen_versions={"nbgv-python": ()},
        ),
    )
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    assert node["publish-mode"] == "overwrite-mutable"
    assert node["overwrite-mutable-authorization"] == {
        "kind": "planner-validated-buddy-force",
        "profile": "buddy",
        "force": True,
        "remote-observation": "partial",
        "mutability": "mutable-prerelease",
    }


def test_buddy_force_official_frozen_version_fails_closed() -> None:
    """Reject buddy force when the project version is official-frozen."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                official_frozen_versions={"nbgv-python": ("2.1.0.dev1",)},
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"


def test_missing_remote_observation_fails_closed() -> None:
    """Reject live planning without explicit remote evidence."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"]),
                repo_root=REPO_ROOT,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REMOTE_CLASSIFICATION_FAILED"


def test_buddy_force_requires_official_frozen_evidence() -> None:
    """Reject buddy force when official-frozen evidence is omitted."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"
    details = cast(
        "Mapping[str, object]", error.value.diagnostics[0]["details"]
    )
    assert details["evidence"] == "missing"


def test_buddy_force_rejects_incomplete_official_frozen_evidence() -> None:
    """Reject buddy force when evidence omits the selected project."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                official_frozen_versions={"other-project": ()},
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"
    details = cast(
        "Mapping[str, object]", error.value.diagnostics[0]["details"]
    )
    assert details["evidence"] == "missing"


def test_buddy_force_rejects_malformed_official_frozen_evidence() -> None:
    """Reject buddy force when evidence is not a version sequence."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                official_frozen_versions=cast(
                    "Mapping[str, Sequence[str]]",
                    {"nbgv-python": "2.1.0.dev1"},
                ),
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"
    details = cast(
        "Mapping[str, object]", error.value.diagnostics[0]["details"]
    )
    assert details["evidence"] == "malformed"


def test_buddy_force_rejects_non_string_official_frozen_item() -> None:
    """Reject buddy force when an evidence item is not a string."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                official_frozen_versions=cast(
                    "Mapping[str, Sequence[str]]",
                    {"nbgv-python": [123]},
                ),
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"
    details = cast(
        "Mapping[str, object]", error.value.diagnostics[0]["details"]
    )
    assert details["evidence"] == "malformed"


def test_buddy_force_rejects_unhashable_official_frozen_item() -> None:
    """Reject malformed nested evidence instead of raising TypeError."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["nbgv-python"], force=True),
                repo_root=REPO_ROOT,
                official_frozen_versions=cast(
                    "Mapping[str, Sequence[str]]",
                    {"nbgv-python": [[1]]},
                ),
            ),
        )
    assert error.value.diagnostics[0]["code"] == "OFFICIAL_FROZEN_VERSION"
    details = cast(
        "Mapping[str, object]", error.value.diagnostics[0]["details"]
    )
    assert details["evidence"] == "malformed"


def test_cli_passes_remote_observations() -> None:
    """CLI remote observations participate in fail-closed planning."""
    first = _plan(["nbgv-python"])
    node_id = _github_release_node_id(first.plan)
    scratch = REPO_ROOT / ".planner-cli-remote-observations"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = scratch / "request.json"
        remote = scratch / "remote.json"
        frozen = scratch / "frozen.json"
        plan_out = scratch / "plan.json"
        execution_sets = scratch / "execution-sets.json"
        diagnostics = scratch / "diagnostics.json"
        request.write_text(
            json.dumps(_request(["nbgv-python"], force=True)),
            encoding="utf-8",
        )
        remote.write_text(
            json.dumps(
                _remote_observations(first.plan, {node_id: "conflicting"})
            ),
            encoding="utf-8",
        )
        frozen.write_text(
            json.dumps({"nbgv-python": []}),
            encoding="utf-8",
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--remote-observations",
            str(remote),
            "--official-frozen-versions",
            str(frozen),
            "--plan-out",
            str(plan_out),
            "--execution-sets-out",
            str(execution_sets),
            "--diagnostics-out",
            str(diagnostics),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert document["diagnostics"][0]["code"] == "REMOTE_CONFLICTING"
    finally:
        _remove_flat_scratch(scratch)


def test_cli_without_remote_observations_fails_closed() -> None:
    """CLI live planning fails closed when remote evidence is omitted."""
    scratch = REPO_ROOT / ".planner-cli-missing-remote-observations"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = scratch / "request.json"
        plan_out = scratch / "plan.json"
        execution_sets = scratch / "execution-sets.json"
        diagnostics = scratch / "diagnostics.json"
        request.write_text(
            json.dumps(_request(["nbgv-python"])),
            encoding="utf-8",
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--plan-out",
            str(plan_out),
            "--execution-sets-out",
            str(execution_sets),
            "--diagnostics-out",
            str(diagnostics),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert document["diagnostics"][0]["code"] == (
            "REMOTE_CLASSIFICATION_FAILED"
        )
    finally:
        _remove_flat_scratch(scratch)


def test_cli_passes_official_frozen_versions() -> None:
    """CLI official-frozen evidence participates in fail-closed planning."""
    scratch = REPO_ROOT / ".planner-cli-official-frozen"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = scratch / "request.json"
        frozen = scratch / "frozen.json"
        plan_out = scratch / "plan.json"
        execution_sets = scratch / "execution-sets.json"
        diagnostics = scratch / "diagnostics.json"
        request.write_text(
            json.dumps(_request(["nbgv-python"], force=True)),
            encoding="utf-8",
        )
        frozen.write_text(
            json.dumps({"nbgv-python": ["2.1.0.dev1"]}),
            encoding="utf-8",
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--official-frozen-versions",
            str(frozen),
            "--plan-out",
            str(plan_out),
            "--execution-sets-out",
            str(execution_sets),
            "--diagnostics-out",
            str(diagnostics),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        document = json.loads(diagnostics.read_text(encoding="utf-8"))
        assert document["diagnostics"][0]["code"] == "OFFICIAL_FROZEN_VERSION"
    finally:
        _remove_flat_scratch(scratch)


def test_cli_buddy_force_succeeds_with_selected_project_frozen_evidence() -> (
    None
):
    """CLI buddy force accepts official-frozen evidence for the selected project."""
    first = _plan(["nbgv-python"])
    node_id = _github_release_node_id(first.plan)
    scratch = REPO_ROOT / ".planner-cli-official-frozen-success"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = scratch / "request.json"
        remote = scratch / "remote.json"
        frozen = scratch / "frozen.json"
        plan_out = scratch / "plan.json"
        execution_sets = scratch / "execution-sets.json"
        diagnostics = scratch / "diagnostics.json"
        request.write_text(
            json.dumps(_request(["nbgv-python"], force=True)),
            encoding="utf-8",
        )
        remote.write_text(
            json.dumps(_remote_observations(first.plan, {node_id: "partial"})),
            encoding="utf-8",
        )
        frozen.write_text(
            json.dumps({"nbgv-python": []}),
            encoding="utf-8",
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--remote-observations",
            str(remote),
            "--official-frozen-versions",
            str(frozen),
            "--plan-out",
            str(plan_out),
            "--execution-sets-out",
            str(execution_sets),
            "--diagnostics-out",
            str(diagnostics),
        ]
        try:
            assert cli_main() == 0
        finally:
            sys.argv = old_argv
        plan = json.loads(plan_out.read_text(encoding="utf-8"))
        node = cast("Mapping[str, object]", _publish_nodes(plan)[node_id])
        assert node["publish-mode"] == "overwrite-mutable"
        assert not diagnostics.exists()
    finally:
        _remove_flat_scratch(scratch)


def test_build_system_nbgv_resolves_node_project_version(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve build-system-nbgv projects through the NBGV tool query."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        if handled := _fake_git_show_json(
            args,
            path="src/public/lib/hexo-renderer-asciidoc/package.json",
            payload={"name": "hexo-renderer-asciidoc"},
        ):
            return handled
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "3.1.0-beta.1"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hexo-renderer-asciidoc"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    envelope = cast("Mapping[str, object]", result.plan["envelope"])
    projects = cast("Mapping[str, object]", envelope["projects"])
    project = cast("Mapping[str, object]", projects["hexo-renderer-asciidoc"])
    version = cast("str", project["resolved-version"])
    assert version.startswith("3.1.0-beta.")
    assert "placeholder" not in version


def test_build_system_nbgv_query_uses_request_commit(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Query NBGV at the requested commit, not ambient HEAD."""
    snapshot = validate_authoring(REPO_ROOT)
    captured_commands: list[list[str]] = []

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        if handled := _fake_git_show_json(
            args,
            path="src/public/lib/hexo-renderer-asciidoc/package.json",
            payload={"name": "hexo-renderer-asciidoc"},
        ):
            return handled
        captured_commands.append(args)
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "3.1.0-beta.1"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hexo-renderer-asciidoc"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    nbgv_commands = [
        args for args in captured_commands if "get-version" in args
    ]
    assert len(nbgv_commands) == 1
    assert nbgv_commands[0][nbgv_commands[0].index("get-version") + 1] == SHA


def test_npm_package_name_reads_requested_commit_package_json(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve npm package identity from the requested commit."""
    snapshot = validate_authoring(REPO_ROOT)
    project = snapshot.projects["hcoona-release-smoke-npm"]
    official_targets = tuple(
        replace(target, projection={}, projection_present=False)
        if target.uses == "npm/npmjs"
        else target
        for target in project.profiles["official"]
    )
    snapshot = replace(
        snapshot,
        projects={
            **snapshot.projects,
            "hcoona-release-smoke-npm": replace(
                project,
                profiles={
                    **project.profiles,
                    "official": official_targets,
                },
            ),
        },
    )

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        if handled := _fake_git_show_json(
            args,
            path="src/public/lib/hcoona-release-smoke-npm/package.json",
            payload={"name": "requested-npm-name"},
        ):
            return handled
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "3.1.0-beta.1"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-npm"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "npm")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["package-name"] == "requested-npm-name"
    assert identity["package-name"] != "hcoona-release-smoke-npm"
    projection = cast("Mapping[str, object]", node["projection"])
    filenames = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert list(filenames.values()) == ["requested-npm-name-3.1.0-beta.1.tgz"]


def test_npm_dual_artifact_projection_selects_registry_identity(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Plan distinct npm package identities and filenames for dual npm smoke."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        if len(args) > 2 and args[1] == "show" and ":" in args[2]:
            return subprocess.CompletedProcess(
                args,
                0,
                '{"name": "hcoona-release-smoke-npm-dual"}',
                "",
            )
        return subprocess.CompletedProcess(
            args,
            0,
            '{"SemVer2": "1.2.3-beta.4"}',
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )

    official = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-npm-dual"], profile="official"
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    buddy = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-npm-dual"], profile="buddy"
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )

    official_npm = cast(
        "Mapping[str, object]",
        _publish_nodes(official.plan)[
            _publish_node_id_for_family(official.plan, "npm")
        ],
    )
    buddy_npm = cast(
        "Mapping[str, object]",
        _publish_nodes(buddy.plan)[
            _publish_node_id_for_family(buddy.plan, "npm")
        ],
    )
    assert official_npm["resolved-publish-identity"] == {
        "package-name": "hcoona-release-smoke-npm-dual",
        "version": "1.2.3-beta.4",
    }
    assert buddy_npm["resolved-publish-identity"] == {
        "package-name": "@hcoona/hcoona-release-smoke-npm-dual",
        "version": "1.2.3-beta.4",
    }
    official_projection = cast(
        "Mapping[str, object]", official_npm["projection"]
    )
    buddy_projection = cast("Mapping[str, object]", buddy_npm["projection"])
    assert list(
        cast(
            "Mapping[str, str]",
            official_projection["final-distribution-filenames-by-artifact-id"],
        ).values()
    ) == ["hcoona-release-smoke-npm-dual-1.2.3-beta.4.tgz"]
    assert list(
        cast(
            "Mapping[str, str]",
            buddy_projection["final-distribution-filenames-by-artifact-id"],
        ).values()
    ) == ["hcoona-hcoona-release-smoke-npm-dual-1.2.3-beta.4.tgz"]


def test_npm_distribution_projection_uses_target_specific_pack_contract(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Npm projections freeze names only and leave digests to build receipts."""
    snapshot = validate_authoring(REPO_ROOT)
    source = snapshot.projects["hcoona-release-smoke-npm"]
    artifact_id = "npm-package"
    official_targets = (
        TargetUsage(
            uses="github-release/public",
            artifacts=(artifact_id,),
            projection={},
            projection_present=False,
        ),
        TargetUsage(
            uses="npm/npmjs",
            artifacts=(artifact_id,),
            projection={"package-name": "plain-smoke"},
            projection_present=True,
        ),
        TargetUsage(
            uses="npm/github-packages",
            artifacts=(artifact_id,),
            projection={"package-name": "@hcoona/plain-smoke"},
            projection_present=True,
        ),
    )
    snapshot = replace(
        snapshot,
        projects={
            **snapshot.projects,
            source.project_id: replace(
                source,
                profiles={**source.profiles, "official": official_targets},
            ),
        },
    )
    package_json = {
        "name": "ambient-smoke",
        "version": "3.1.0-beta.1",
        "scripts": {"build": "node build.js", "prepack": "node prepack.js"},
    }
    captured_commands: list[list[str]] = []

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        captured_commands.append(list(args))
        if handled := _fake_git_show_epoch(args):
            return handled
        if handled := _fake_git_show_json(
            args,
            path="src/public/lib/hcoona-release-smoke-npm/package.json",
            payload=package_json,
        ):
            return handled
        command = Path(str(args[0])).name
        if command in {"dotnet", "nbgv"} and "get-version" in args:
            return subprocess.CompletedProcess(
                args, 0, json.dumps({"SemVer2": "3.1.0-beta.1"}), ""
            )
        message = f"unexpected planner subprocess: {args}"
        raise AssertionError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-npm"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    validate_contract(result.plan)
    target_snapshots = cast(
        "Mapping[str, Mapping[str, object]]",
        cast("Mapping[str, object]", result.plan["graph"])[
            "target-instance-snapshots"
        ],
    )
    npm_nodes = [
        cast("Mapping[str, object]", node)
        for node in _publish_nodes(result.plan).values()
        if target_snapshots[
            str(
                cast("Mapping[str, object]", node)[
                    "target-instance-snapshot-id"
                ]
            )
        ]["family"]
        == "npm"
    ]
    projections = [
        cast("Mapping[str, object]", node["projection"]) for node in npm_nodes
    ]
    filename_maps = [
        cast(
            "Mapping[str, str]",
            projection["final-distribution-filenames-by-artifact-id"],
        )
        for projection in projections
    ]
    filenames = [
        next(iter(filename_map.values())) for filename_map in filename_maps
    ]
    assert sorted(filenames) == [
        "hcoona-plain-smoke-3.1.0-beta.1.tgz",
        "plain-smoke-3.1.0-beta.1.tgz",
    ]
    assert all(
        "final-distribution-sha256-by-artifact-id" not in projection
        for projection in projections
    )
    assert all(
        "final-distribution-digests-by-artifact-id" not in projection
        for projection in projections
    )
    assert not any("worktree" in command for command in captured_commands)


def test_npm_distribution_projection_does_not_run_prepack(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Planner does not execute prepack to derive npm projection evidence."""
    snapshot = validate_authoring(REPO_ROOT)
    source = snapshot.projects["hcoona-release-smoke-npm"]
    artifact_id = "npm-package"
    snapshot = replace(
        snapshot,
        projects={
            **snapshot.projects,
            source.project_id: replace(
                source,
                profiles={
                    **source.profiles,
                    "official": (
                        TargetUsage(
                            uses="npm/npmjs",
                            artifacts=(artifact_id,),
                            projection={"package-name": "plain-smoke"},
                            projection_present=True,
                        ),
                    ),
                },
            ),
        },
    )
    original_manifest = {
        "name": "ambient-smoke",
        "version": "3.1.0-beta.1",
        "description": "before-prepack",
        "scripts": {"prepack": "node prepack.js"},
    }

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_epoch(args):
            return handled
        if handled := _fake_git_show_json(
            args,
            path="src/public/lib/hcoona-release-smoke-npm/package.json",
            payload=original_manifest,
        ):
            return handled
        command = Path(str(args[0])).name
        if command in {"dotnet", "nbgv"} and "get-version" in args:
            return subprocess.CompletedProcess(
                args, 0, json.dumps({"SemVer2": "3.1.0-beta.1"}), ""
            )
        message = f"unexpected planner subprocess: {args}"
        raise AssertionError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-npm"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )

    validate_contract(result.plan)
    node_id = _publish_node_id_for_family(result.plan, "npm")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    assert projection["package-name"] == "plain-smoke"
    assert "final-distribution-digests-by-artifact-id" not in projection


def test_rubygems_metadata_uses_requested_commit_backend(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve RubyGems metadata from requested-commit gemspec output."""
    snapshot = validate_authoring(REPO_ROOT)
    raw_version = "2.1.0-alpha.1.3.g7c66dfda05"
    gem_version = "2.1.0.pre.alpha.1.3.g7c66dfda05"
    gem_name = "requested-ruby-name"
    gem_file = f"{gem_name}-{gem_version}.gem"
    saw_requested_checkout = False

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        nonlocal saw_requested_checkout
        if "worktree" in args and "add" in args:
            assert args[-1] == SHA
            saw_requested_checkout = True
        if handled := _fake_git_worktree(args):
            return handled
        if args[0].endswith("ruby"):
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    {
                        "name": gem_name,
                        "version": gem_version,
                        "file_name": gem_file,
                    }
                ),
                "",
            )
        return subprocess.CompletedProcess(
            args,
            0,
            json.dumps({"SemVer2": raw_version}),
            "",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-rubygems"], profile="official"
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "rubygems")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert saw_requested_checkout
    assert identity == {"package-name": gem_name, "version": gem_version}
    assert identity["version"] != raw_version
    projection = cast("Mapping[str, object]", node["projection"])
    filenames = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert list(filenames.values()) == [gem_file]

    github_node_id = _github_release_node_id(result.plan)
    github_node = cast(
        "Mapping[str, object]", _publish_nodes(result.plan)[github_node_id]
    )
    projection = cast("Mapping[str, object]", github_node["projection"])
    assets = cast(
        "Mapping[str, str]",
        projection["asset-names-by-artifact-id"],
    )
    assert gem_file in assets.values()


def test_rubygems_distribution_projection_does_not_build_for_sha(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """RubyGems projections leave content SHA authority to build receipts."""
    snapshot = validate_authoring(REPO_ROOT)
    gem_name = "hcoona-release-smoke-rubygems"
    gem_version = "2.1.0.pre.alpha.1"
    gem_file = f"{gem_name}-{gem_version}.gem"

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_epoch(args):
            return handled
        if "worktree" in args:
            if "add" in args:
                checkout = Path(str(args[-2]))
                release_root = (
                    checkout / "src/public/lib/hcoona-release-smoke-rubygems"
                )
                release_root.mkdir(parents=True, exist_ok=True)
                (release_root / f"{gem_name}.gemspec").write_text(
                    "Gem::Specification.new\n",
                    encoding="utf-8",
                )
            return subprocess.CompletedProcess(args, 0, "", "")
        command = Path(str(args[0])).name
        if command == "ruby":
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    {
                        "name": gem_name,
                        "version": gem_version,
                        "file_name": gem_file,
                    }
                ),
                "",
            )
        if command in {"dotnet", "nbgv"} and "get-version" in args:
            return subprocess.CompletedProcess(
                args, 0, json.dumps({"SemVer2": "2.1.0-alpha.1"}), ""
            )
        message = f"unexpected planner subprocess: {args}"
        raise AssertionError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-rubygems"], profile="official"
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    validate_contract(result.plan)
    node_id = _publish_node_id_for_family(result.plan, "rubygems")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    assert "final-distribution-sha256-by-artifact-id" not in projection


def test_rubygems_distribution_projection_ignores_gem_build_failures(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Planner no longer runs gem build while projecting RubyGems nodes."""
    snapshot = validate_authoring(REPO_ROOT)
    gem_name = "hcoona-release-smoke-rubygems"
    gem_version = "2.1.0.pre.alpha.1"
    gem_file = f"{gem_name}-{gem_version}.gem"

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_epoch(args):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        command = Path(str(args[0])).name
        if command == "ruby":
            return subprocess.CompletedProcess(
                args,
                0,
                json.dumps(
                    {
                        "name": gem_name,
                        "version": gem_version,
                        "file_name": gem_file,
                    }
                ),
                "",
            )
        if command in {"dotnet", "nbgv"} and "get-version" in args:
            return subprocess.CompletedProcess(
                args, 0, json.dumps({"SemVer2": "2.1.0-alpha.1"}), ""
            )
        message = f"unexpected planner subprocess: {args}"
        raise AssertionError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(
                ["hcoona-release-smoke-rubygems"], profile="official"
            ),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "rubygems")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    assert "final-distribution-sha256-by-artifact-id" not in projection


def test_build_system_nbgv_failure_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fail closed when the planner-owned NBGV query cannot run."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        _args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        message = "dotnet"
        raise FileNotFoundError(message)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["hexo-renderer-asciidoc"]),
                repo_root=REPO_ROOT,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "VERSION_AUTHORITY_FAILED"


def test_build_system_nbgv_unavailable_commit_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fail closed when NBGV cannot resolve the requested commit."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        assert args[args.index("get-version") + 1] == SHA
        return subprocess.CompletedProcess(
            args,
            1,
            "",
            "fatal: bad object",
        )

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["hexo-renderer-asciidoc"]),
                repo_root=REPO_ROOT,
            ),
        )
    assert error.value.diagnostics[0]["code"] == "VERSION_AUTHORITY_FAILED"


def test_pypi_filenames_are_taken_from_build_backend(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Freeze PyPI member filenames from backend output, not static formulas."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        out_dir = Path(args[args.index("--out-dir") + 1])
        out_dir.mkdir(parents=True, exist_ok=True)
        (out_dir / "nbgv_python-2.1.0.dev1-py3-none-any.whl").write_text(
            "",
            encoding="utf-8",
        )
        (out_dir / "nbgv_python-2.1.0.dev1.tar.gz").write_text(
            "",
            encoding="utf-8",
        )
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert "asset-names-by-artifact-id" not in projection
    assert {
        "nbgv_python-2.1.0.dev1-py3-none-any.whl",
        "nbgv_python-2.1.0.dev1.tar.gz",
    } <= set(names.values())


def test_pypi_projection_does_not_run_backend_wheel_build(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """PyPI planning projects conventional filenames without building wheels."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert {
        "nbgv_python-2.1.0.dev1-py3-none-any.whl",
        "nbgv_python-2.1.0.dev1.tar.gz",
    } == set(names.values())


def test_pypi_package_name_reads_requested_commit_pyproject(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve PyPI identity name from requested commit, not ambient HEAD."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="requested-name",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["package-name"] == "requested-name"
    assert identity["package-name"] != "nbgv-python"


def test_pypi_dotted_package_name_matches_wheel_normalization(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Accept dotted PyPI names normalized with underscores."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="requested.name",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity == {
        "package-name": "requested-name",
        "version": "2.1.0.dev1",
    }


def test_pypi_build_system_nbgv_identity_uses_pep440_normalized_version(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Use a PEP 440 projection for PyPI identity, not raw SemVer2."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if "get-version" in args:
            return subprocess.CompletedProcess(
                args,
                0,
                '{"SemVer2": "1.0.0-beta.5+gabcdef"}',
                "",
            )
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["version"] == "1.0.0b5+gabcdef"
    assert identity["version"] != "1.0.0-beta.5+gabcdef"
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert {
        "hcoona_release_smoke_pypi-1.0.0b5+gabcdef-py3-none-any.whl",
        "hcoona_release_smoke_pypi-1.0.0b5+gabcdef.tar.gz",
    } <= set(names.values())


def test_pypi_build_system_nbgv_name_reads_requested_commit_pyproject(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Resolve build-system PyPI name from requested commit pyproject.toml."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="requested-smoke",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if "get-version" in args:
            return subprocess.CompletedProcess(
                args,
                0,
                '{"SemVer2": "1.0.0-beta.5+gabcdef"}',
                "",
            )
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["package-name"] == "requested-smoke"
    assert identity["package-name"] != "hcoona-release-smoke-pypi"


def test_pypi_build_system_nbgv_query_uses_requested_commit_without_backend(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Query NBGV at the requested commit without materializing a build checkout."""
    snapshot = validate_authoring(REPO_ROOT)
    captured_commands: list[list[str]] = []

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        captured_commands.append(list(args))
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if "get-version" in args:
            assert args[args.index("get-version") + 1] == SHA
            return subprocess.CompletedProcess(
                args,
                0,
                '{"SemVer2": "1.0.0-beta.5+grequested"}',
                "",
            )
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["version"] == "1.0.0b5+grequested"
    assert not any("worktree" in command for command in captured_commands)


def test_pypi_invalid_semver2_projection_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fail closed when NBGV output cannot be projected to PEP 440."""
    snapshot = validate_authoring(REPO_ROOT)

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="not a version"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(
                    ["hcoona-release-smoke-pypi"], profile="official"
                ),
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )
    assert {diagnostic["code"] for diagnostic in error.value.diagnostics} == {
        "PYPI_FILENAME_COMPUTE_FAILED"
    }


def test_pypi_projection_does_not_materialize_build_checkout(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """PyPI projection reads metadata files directly and does not create worktrees."""
    snapshot = validate_authoring(REPO_ROOT)
    captured_commands: list[list[str]] = []

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        captured_commands.append(list(args))
        if handled := _fake_git_show_pyproject(
            args,
            name="hcoona-release-smoke-pypi",
            version=None,
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if "get-version" in args:
            return subprocess.CompletedProcess(
                args,
                0,
                '{"SemVer2": "1.0.0-beta.5+gabcdef"}',
                "",
            )
        raise AssertionError(args)

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["hcoona-release-smoke-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    validate_contract(result.plan)
    assert not any("worktree" in command for command in captured_commands)


def test_pypi_wheel_only_publish_does_not_require_sdist(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """PyPI publish planning allows the optional sdist to be absent."""
    base = validate_authoring(REPO_ROOT)
    source = base.projects["hcoona-release-smoke-pypi"]
    pypi_usage = next(
        usage
        for usage in source.profiles["official"]
        if base.target_instances[usage.uses].family == "pypi"
    )
    project = ProjectDescriptor(
        project_id="wheel-only-pypi",
        display_name="Wheel Only PyPI",
        ecosystem=source.ecosystem,
        release_kind=source.release_kind,
        descriptor_path=source.descriptor_path,
        release_root=source.release_root,
        primary_manifest_path=source.primary_manifest_path,
        auxiliary_input_paths=source.auxiliary_input_paths,
        version_authority_kind=source.version_authority_kind,
        variants=(
            Variant(
                "package",
                {},
                (
                    Artifact(
                        "wheel",
                        "primary-package",
                        "package",
                        "wheel",
                        (),
                        "package",
                        (),
                    ),
                ),
            ),
        ),
        profiles={
            "buddy": (),
            "official": (
                TargetUsage(
                    pypi_usage.uses,
                    ("wheel",),
                    pypi_usage.projection,
                    pypi_usage.projection_present,
                ),
            ),
        },
    )
    snapshot = AuthoringSnapshot(
        descriptor_api_version=base.descriptor_api_version,
        catalog_path=base.catalog_path,
        projects={"wheel-only-pypi": project},
        target_instances=base.target_instances,
    )

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["wheel-only-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    identity = cast("Mapping[str, str]", node["resolved-publish-identity"])
    assert identity["version"] == "2.1.0.dev1"
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert list(names.values()) == ["nbgv_python-2.1.0.dev1-py3-none-any.whl"]


def test_pypi_wheel_only_publish_ignores_unbuilt_sdist_outputs(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Wheel-only PyPI planning does not inspect backend output directories."""
    base = validate_authoring(REPO_ROOT)
    source = base.projects["hcoona-release-smoke-pypi"]
    pypi_usage = next(
        usage
        for usage in source.profiles["official"]
        if base.target_instances[usage.uses].family == "pypi"
    )
    project = ProjectDescriptor(
        project_id="wheel-only-pypi",
        display_name="Wheel Only PyPI",
        ecosystem=source.ecosystem,
        release_kind=source.release_kind,
        descriptor_path=source.descriptor_path,
        release_root=source.release_root,
        primary_manifest_path=source.primary_manifest_path,
        auxiliary_input_paths=source.auxiliary_input_paths,
        version_authority_kind=source.version_authority_kind,
        variants=(
            Variant(
                "package",
                {},
                (
                    Artifact(
                        "wheel",
                        "primary-package",
                        "package",
                        "wheel",
                        (),
                        "package",
                        (),
                    ),
                ),
            ),
        ),
        profiles={
            "buddy": (),
            "official": (
                TargetUsage(
                    pypi_usage.uses,
                    ("wheel",),
                    pypi_usage.projection,
                    pypi_usage.projection_present,
                ),
            ),
        },
    )
    snapshot = AuthoringSnapshot(
        descriptor_api_version=base.descriptor_api_version,
        catalog_path=base.catalog_path,
        projects={"wheel-only-pypi": project},
        target_instances=base.target_instances,
    )

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        if handled := _fake_nbgv_get_version(args, semver2="2.1.0-dev.1"):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["wheel-only-pypi"], profile="official"),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _publish_node_id_for_family(result.plan, "pypi")
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast(
        "Mapping[str, str]",
        projection["final-distribution-filenames-by-artifact-id"],
    )
    assert list(names.values()) == ["nbgv_python-2.1.0.dev1-py3-none-any.whl"]


def test_github_release_wheel_only_asset_does_not_require_sdist(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """GitHub Release wheel-only targets do not force project sdists."""
    base = validate_authoring(REPO_ROOT)
    source = base.projects["nbgv-python"]
    github_usage = next(
        usage
        for usage in source.profiles["official"]
        if base.target_instances[usage.uses].family == "github-release"
    )
    wheel = source.artifacts_by_id["wheel"]
    sdist = source.artifacts_by_id["sdist"]
    project = ProjectDescriptor(
        project_id="github-wheel-only",
        display_name="GitHub Wheel Only",
        ecosystem=source.ecosystem,
        release_kind=source.release_kind,
        descriptor_path=source.descriptor_path,
        release_root=source.release_root,
        primary_manifest_path=source.primary_manifest_path,
        auxiliary_input_paths=source.auxiliary_input_paths,
        version_authority_kind=source.version_authority_kind,
        variants=(
            Variant(
                "package",
                {},
                (
                    Artifact(
                        wheel.id,
                        wheel.role,
                        wheel.kind_family,
                        wheel.concrete_kind,
                        wheel.produced_from,
                        wheel.variant_id,
                        wheel.companions,
                    ),
                    Artifact(
                        sdist.id,
                        sdist.role,
                        sdist.kind_family,
                        sdist.concrete_kind,
                        sdist.produced_from,
                        sdist.variant_id,
                        sdist.companions,
                    ),
                ),
            ),
        ),
        profiles={
            "buddy": (
                TargetUsage(
                    github_usage.uses,
                    ("wheel",),
                    github_usage.projection,
                    github_usage.projection_present,
                ),
            ),
            "official": (),
        },
    )
    snapshot = AuthoringSnapshot(
        descriptor_api_version=base.descriptor_api_version,
        catalog_path=base.catalog_path,
        projects={"github-wheel-only": project},
        target_instances=base.target_instances,
    )

    def fake_run(
        args: list[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        _assert_no_planner_build_command(args)
        if handled := _fake_git_show_pyproject(
            args,
            name="nbgv-python",
            version="2.1.0.dev1",
        ):
            return handled
        if handled := _fake_git_worktree(args):
            return handled
        return subprocess.CompletedProcess(args, 0, "", "")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["github-wheel-only"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(result.plan)
    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast("Mapping[str, str]", projection["asset-names-by-artifact-id"])
    assert list(names.values()) == ["nbgv_python-2.1.0.dev1-py3-none-any.whl"]
    assert "asset-sizes-by-artifact-id" not in projection
    assert "asset-sha256-by-artifact-id" not in projection


@pytest.mark.parametrize(
    ("project_id", "metadata_kind"),
    [
        ("hcoona-release-smoke-wxt", "none"),
        ("hcoona-release-smoke-dotnet-executable", "dotnet"),
        ("hcoona-release-smoke-inno", "dotnet"),
        ("hcoona-release-smoke-pypi", "none"),
        ("hcoona-release-smoke-npm", "none"),
        ("hcoona-release-smoke-rubygems", "none"),
    ],
)
def test_active_github_release_asset_kinds_plan_names_without_build_evidence(
    monkeypatch: pytest.MonkeyPatch,
    project_id: str,
    metadata_kind: str,
) -> None:
    """Active concrete GitHub Release asset kinds plan names without bytes."""
    snapshot = validate_authoring(REPO_ROOT)

    monkeypatch.delitem(
        sys.modules, "three_workflow_release_build", raising=False
    )

    dotnet_metadata = (
        _dotnet_metadata(snapshot) if metadata_kind == "dotnet" else None
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request([project_id]),
            repo_root=REPO_ROOT,
            dotnet_metadata=dotnet_metadata,
            dry_run=True,
        ),
    )
    node_id = _github_release_node_id(result.plan)

    node = cast("Mapping[str, object]", _publish_nodes(result.plan)[node_id])
    artifact_ids = cast("Sequence[str]", node["artifact-ids"])
    projection = cast("Mapping[str, object]", node["projection"])
    names = cast("Mapping[str, str]", projection["asset-names-by-artifact-id"])
    assert set(names) == set(artifact_ids)
    assert all(name for name in names.values())
    assert "asset-sizes-by-artifact-id" not in projection
    assert "asset-sha256-by-artifact-id" not in projection


def test_dotnet_metadata_boundary_fails_closed_when_missing() -> None:
    """Do not plan .NET projects without Windows metadata evidence."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(request=_request(["hjg-pngcs"]), repo_root=REPO_ROOT),
        )
    assert {diagnostic["code"] for diagnostic in error.value.diagnostics} == {
        "DOTNET_METADATA_FAILED"
    }


def test_zero_target_selection_serializes_empty_execution_sets() -> None:
    """Zero-target selections are successful empty-selector plans."""
    snapshot = AuthoringSnapshot(
        descriptor_api_version="three.release/v1alpha1",
        catalog_path="eng/release/target-instances.yml",
        projects={
            "zero-target": ProjectDescriptor(
                project_id="zero-target",
                display_name="Zero Target",
                ecosystem="python",
                release_kind="lib",
                descriptor_path="src/public/lib/nbgv-python/three.release.yml",
                release_root="src/public/lib/nbgv-python",
                primary_manifest_path="src/public/lib/nbgv-python/pyproject.toml",
                auxiliary_input_paths=(),
                version_authority_kind="nbgv-python-pyproject-version",
                variants=(Variant("package", {}, ()),),
                profiles={"buddy": (), "official": ()},
            )
        },
        target_instances={},
    )
    result = plan_release(
        snapshot,
        PlannerInputs(
            request=_request(["zero-target"]),
            repo_root=REPO_ROOT,
            dry_run=True,
        ),
    )
    assert result.execution_sets["publish-intent-node-ids"] == []
    assert result.execution_sets["active-variant-ids"] == []
    assert result.execution_sets["active-publish-node-ids"] == []


def test_unknown_project_fails_the_whole_request() -> None:
    """Requested unknown projects produce blocking diagnostics."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(["missing-project"]), repo_root=REPO_ROOT
            ),
        )
    assert error.value.diagnostics[0]["code"] == "REQ_PROJECT_NOT_FOUND"


@pytest.mark.parametrize(
    "requested_project_ids",
    [
        [],
        ["nbgv-python", "hcoona-release-smoke-pypi"],
    ],
)
def test_planner_rejects_non_single_project_requests(
    requested_project_ids: list[str],
) -> None:
    """Active planner requests must name exactly one project."""
    snapshot = validate_authoring(REPO_ROOT)
    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=_request(requested_project_ids),
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )
    assert any(
        diagnostic["code"] == "REQ_INVALID_INPUT"
        for diagnostic in error.value.diagnostics
    )
    assert any(
        "exactly one project" in str(diagnostic["message"])
        for diagnostic in error.value.diagnostics
    )


@pytest.mark.parametrize(
    "bad_commit_sha",
    [
        "HEAD",
        "a" * 39,
        "a" * 41,
        "A" * 40,
        "g" * 40,
    ],
)
def test_planner_rejects_mutable_request_commit_sha_before_commands(
    bad_commit_sha: str,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject non-immutable request SHAs before checkout or version commands."""
    snapshot = validate_authoring(REPO_ROOT)
    request = _request(["nbgv-python"])
    request["commit-sha"] = bad_commit_sha

    def fake_run(
        args: Sequence[str],
        **_kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        raise AssertionError(f"planner must reject before subprocess: {args}")

    monkeypatch.setattr(
        "three_workflow_release_planner.planner.subprocess.run",
        fake_run,
    )

    with pytest.raises(PlannerError) as error:
        plan_release(
            snapshot,
            PlannerInputs(
                request=request,
                repo_root=REPO_ROOT,
                dry_run=True,
            ),
        )

    assert any(
        diagnostic["code"] == "REQ_INVALID_INPUT"
        for diagnostic in error.value.diagnostics
    )
    assert any(
        "40-char lowercase hex SHA" in str(details.get("issues", []))
        for diagnostic in error.value.diagnostics
        if isinstance(details := diagnostic.get("details"), dict)
    )


def _ci_request(changed_files: list[str]) -> dict[str, object]:
    """Return a normalized CI validation planner request."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        API_VERSIONS_BY_KIND,
        CiValidationKind,
        canonical_json_digest,
        ci_validation_request_artifact_ref,
        ci_validation_request_projection,
    )

    run_id = "25887422010"
    run_attempt = "1"
    document: dict[str, object] = {
        "api-version": API_VERSIONS_BY_KIND[CiValidationKind.REQUEST.value],
        "kind": CiValidationKind.REQUEST.value,
        "created-at": "2026-05-14T21:09:21Z",
        "repository": {"owner": "hcoona", "name": "three"},
        "run": {
            "workflow": "CI Validation",
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "schema-diagnostics": [],
        "artifact-ref": ci_validation_request_artifact_ref(
            run_id=run_id,
            run_attempt=run_attempt,
        ),
        "request-digest": "0" * 64,
        "mode": "pull_request",
        "validation-tree": {
            "commit-sha": SHA,
            "ref": "refs/pull/42/merge",
        },
        "event": {
            "name": "pull_request",
            "number": "42",
            "actor": "octocat",
            "run-id": run_id,
            "run-attempt": run_attempt,
        },
        "affected-range": {
            "status": "available",
            "base-sha": "a" * 40,
            "base-tip-sha": "c" * 40,
            "head-sha": SHA,
            "changed-files": changed_files,
            "source": "pull_request",
            "diagnostic": None,
            "diagnostic-detail": None,
        },
    }
    document["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(document),
    )
    return document


def _ci_inputs(changed_files: list[str]):
    """Return CI planner inputs for tests."""
    from three_workflow_release_planner import (  # noqa: PLC0415
        CiValidationPlannerInputs,
    )

    return CiValidationPlannerInputs(
        request=_ci_request(changed_files),
        repo_root=REPO_ROOT,
        expected_run_id="25887422010",
        expected_run_attempt="1",
        created_at="2026-05-14T21:09:21Z",
    )


def _scheduled_ci_inputs():
    """Return scheduled full CI planner inputs for tests."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        canonical_json_digest,
        ci_validation_request_projection,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        CiValidationPlannerInputs,
    )

    request = dict(_ci_request([]))
    request["mode"] = "scheduled_full"
    request["event"] = {
        "name": "schedule",
        "number": None,
        "actor": "octocat",
        "run-id": "25887422010",
        "run-attempt": "1",
    }
    request["scheduled-full"] = {"enabled": True}
    del request["affected-range"]
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )
    return CiValidationPlannerInputs(
        request=request,
        repo_root=REPO_ROOT,
        expected_run_id="25887422010",
        expected_run_attempt="1",
        created_at="2026-05-14T21:09:21Z",
    )


def test_cli_emits_ci_validation_plan_snapshots() -> None:
    """CI planner CLI writes the plan and available companion snapshots."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )

    scratch = REPO_ROOT / ".planner-cli-ci-validation"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = scratch / "ci-request.json"
        plan_out = scratch / "validation-plan.json"
        changed_files_out = scratch / "changed-files.json"
        fact_snapshot_out = scratch / "fact-snapshot.json"
        request.write_text(
            json.dumps(_ci_request([])),
            encoding="utf-8",
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "ci-plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request),
            "--expected-run-id",
            "25887422010",
            "--expected-run-attempt",
            "1",
            "--created-at",
            "2026-05-14T21:09:21Z",
            "--plan-out",
            str(plan_out),
            "--changed-files-out",
            str(changed_files_out),
            "--fact-snapshot-out",
            str(fact_snapshot_out),
        ]
        try:
            assert cli_main() == 0
        finally:
            sys.argv = old_argv
        plan = json.loads(plan_out.read_text(encoding="utf-8"))
        changed_files = json.loads(
            changed_files_out.read_text(encoding="utf-8"),
        )
        fact_snapshot = json.loads(
            fact_snapshot_out.read_text(encoding="utf-8"),
        )
        validate_ci_validation_plan(
            plan,
            changed_files_snapshot=changed_files,
            fact_snapshot=fact_snapshot,
        )
        assert plan["kind"] == "ci-validation-plan"
        assert plan["mode"] == "pull_request"
    finally:
        _remove_flat_scratch(scratch)


def test_cli_ci_plan_failure_writes_planner_diagnostics_contract() -> None:
    """CI planner CLI failure diagnostics use the planner diagnostics schema."""
    scratch = REPO_ROOT / ".planner-cli-ci-validation-diagnostics"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir(parents=True)
        request = _ci_request([])
        request["run"] = dict(cast("Mapping[str, object]", request["run"]))
        cast("dict[str, object]", request["run"])["run-id"] = "wrong"
        request_path = scratch / "ci-request.json"
        diagnostics_out = scratch / "planner-diagnostics.json"
        plan_out = scratch / "validation-plan.json"
        request_path.write_text(json.dumps(request), encoding="utf-8")
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-planner",
            "ci-plan",
            "--repo-root",
            str(REPO_ROOT),
            "--request",
            str(request_path),
            "--expected-run-id",
            "25887422010",
            "--expected-run-attempt",
            "1",
            "--plan-out",
            str(plan_out),
            "--diagnostics-out",
            str(diagnostics_out),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        document = json.loads(diagnostics_out.read_text(encoding="utf-8"))
        validate_contract(document)
        assert document["api-version"] == (
            "three.release.planner-diagnostics/v1alpha1"
        )
        assert document["kind"] == "planner-diagnostics"
        assert document["diagnostics"][0]["kind"] == "planner-diagnostic"
    finally:
        _remove_flat_scratch(scratch)


def test_ci_validation_unavailable_affected_range_fails_closed() -> None:
    """Real affected range derivation failures remain fail-closed."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticDetail,
        DiagnosticFamily,
        canonical_json_digest,
        ci_validation_request_projection,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    inputs = _ci_inputs([])
    request = dict(inputs.request)
    request["affected-range"] = {
        "status": "unavailable",
        "base-sha": None,
        "base-tip-sha": None,
        "head-sha": None,
        "changed-files": None,
        "source": "pull_request",
        "diagnostic": DiagnosticFamily.RANGE_UNCONFIRMED.value,
        "diagnostic-detail": DiagnosticDetail.INCOMPLETE.value,
    }
    request["request-digest"] = canonical_json_digest(
        ci_validation_request_projection(request),
    )

    snapshot = plan_ci_validation_from_repo(replace(inputs, request=request))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    assert snapshot.changed_files_snapshot is None
    assert snapshot.fact_snapshot is None
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == DiagnosticFamily.RANGE_UNCONFIRMED.value
    assert diagnostics[0]["detail"] == DiagnosticDetail.INCOMPLETE.value
    work_groups = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["work-groups"],
    )
    assert all(item["kind"] == "evidence-aggregation" for item in work_groups)


def test_ci_validation_plans_zero_file_affected_range() -> None:
    """Confirmed zero-file affected ranges are executable no-work plans."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs([]))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    assert classification["impacts"] == []
    assert classification["lightweight-only"] is True
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    assert all(item["selection-status"] == "not-selected" for item in subjects)
    assert snapshot.plan["descriptor-obligations"] == []
    assert snapshot.plan["validation-obligations"] == []
    assert snapshot.plan["artifact-obligations"] == []
    work_groups = cast(
        "Sequence[Mapping[str, object]]", snapshot.plan["work-groups"]
    )
    assert [item["kind"] for item in work_groups] == ["evidence-aggregation"]


def test_ci_validation_plans_known_non_impacting_scope() -> None:
    """Known non-impacting paths produce executable lightweight-only plans."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs(["README.md"]))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    assert classification["lightweight-only"] is True


def test_ci_validation_plans_descriptor_backed_subject_scope() -> None:
    """Project changes select the owning subject and planned evidence."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(["src/public/lib/nbgv-python/pyproject.toml"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    selected = [
        item for item in subjects if item["selection-status"] == "selected"
    ]
    assert "python.src-public-lib-nbgv-python" in [
        item["subject-id"] for item in selected
    ]
    work_groups = cast(
        "Sequence[Mapping[str, object]]", snapshot.plan["work-groups"]
    )
    assert {item["kind"] for item in work_groups} >= {
        "descriptor-validation",
        "ecosystem-gate",
        "release-shaped-artifact",
    }


def test_ci_validation_plans_rubygems_descriptor_subject_scope() -> None:
    """RubyGems smoke project changes plan Ruby validation obligations."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    subject_id = "ruby.src-public-lib-hcoona-release-smoke-rubygems"
    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(
            [
                "src/public/lib/hcoona-release-smoke-rubygems/"
                "hcoona-release-smoke-rubygems.gemspec"
            ],
        ),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    selected = [
        item for item in subjects if item["selection-status"] == "selected"
    ]
    assert [item["subject-id"] for item in selected] == [subject_id]
    assert selected[0]["ecosystem"] == "ruby"
    validation_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["validation-obligations"],
    )
    assert any(
        item["kind"] == "ecosystem-gate"
        and cast("Mapping[str, object]", item["coverage-target"])["id"]
        == subject_id
        for item in validation_obligations
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["work-groups"],
    )
    ruby_groups = [
        item for item in work_groups if item.get("ecosystem") == "ruby"
    ]
    assert {item["kind"] for item in ruby_groups} >= {
        "ecosystem-gate",
        "release-shaped-artifact",
    }
    assert {item["runner-family"] for item in ruby_groups} == {"ubuntu"}


def test_ci_validation_plans_mixed_changes_keep_canonical_paths() -> None:
    """Mixed valid changes keep flattened matched paths in canonical order."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = [
        "README.md",
        "src/public/lib/nbgv-python/pyproject.toml",
    ]

    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    flattened_paths = [
        path
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    ]
    assert flattened_paths == sorted(changed_files)
    readme_impact = next(
        impact
        for impact in impacts
        if impact["category"] == "known-non-impacting"
    )
    readme_impact_id = str(readme_impact["impact-id"])
    work_groups = cast(
        "Sequence[Mapping[str, object]]", snapshot.plan["work-groups"]
    )
    assert any(item["kind"] != "lightweight-preflight" for item in work_groups)
    assert any(item["kind"] == "lightweight-preflight" for item in work_groups)
    validation_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["validation-obligations"],
    )
    lightweight_obligations = [
        item
        for item in validation_obligations
        if item["kind"] == "lightweight-preflight"
    ]
    assert len(lightweight_obligations) == 1
    assert lightweight_obligations[0]["source-impact-ids"] == [readme_impact_id]


def test_ci_validation_plans_repository_infrastructure_paths() -> None:
    """Repository infrastructure changes avoid fail-closed unknowns."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = sorted(
        [
            ".config/dotnet-tools.json",
            ".github/hooks/telegram-notify.json",
            ".github/workflows/ci.yml",
            ".gitignore",
            ".pre-commit-config.yaml",
            "Directory.Build.targets",
            "tests/fixtures/workflow-release-ci-validation-acceptance-matrix.json",
            "tests/fixtures/workflow-release-acceptance-matrix.json",
            "tests/test_workflow_release_control.py",
        ]
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    assert snapshot.plan["diagnostics"] == []


def test_ci_validation_plans_workflow_governance_markdown_as_tooling() -> None:
    """Active workflow governance markdown is release tooling, not docs-only."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = [".github/workflows/docs/DESIGN.v2.md"]

    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    classification = cast(
        "Mapping[str, object]",
        snapshot.plan["classification"],
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert [impact["matched-paths"] for impact in impacts] == [
        [path] for path in changed_files
    ]
    assert {impact["category"] for impact in impacts} == {
        "workflow-release-infrastructure"
    }
    assert {
        cast("Mapping[str, object]", impact["coverage-target"])["id"]
        for impact in impacts
    } == {"workflow-orchestration"}
    assert {impact["source-rule"] for impact in impacts} == {
        "workflow-release-workflow-orchestration"
    }
    assert classification["lightweight-only"] is False


def test_ci_validation_derives_validation_only_capabilities() -> None:
    """Validation-only subjects expose discovered project-scoped commands."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs([".github/workflows/ci.yml"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    capabilities_by_root = {
        str(subject["root"]): cast(
            "Mapping[str, object]",
            subject["capabilities"],
        )
        for subject in subjects
        if subject["capability-class"] == "validation-only"
    }

    private_python = capabilities_by_root[
        "src/private/app/music-flash-card-generator"
    ]
    assert private_python["build"] is True
    assert private_python["test"] is False
    assert private_python["lint"] is False
    assert private_python["format"] is False
    assert private_python["type-check"] is True

    planner_python = capabilities_by_root[
        "src/public/lib/three-workflow-release-planner"
    ]
    assert planner_python["test"] is True
    assert planner_python["lint"] is False
    assert planner_python["format"] is False

    telegram_bot = capabilities_by_root[
        "src/private/app/im-acp-gateway/poc/telegram-bot-verifier"
    ]
    assert telegram_bot["build"] is True
    assert telegram_bot["test"] is True
    assert telegram_bot["lint"] is True
    assert telegram_bot["format"] is False
    assert telegram_bot["type-check"] is True

    circular_list_tests = capabilities_by_root[
        "tests/public/lib/CircularList.UnitTest"
    ]
    assert circular_list_tests["build"] is True
    assert circular_list_tests["test"] is True
    assert circular_list_tests["format"] is False


def test_ci_validation_plans_python_downstream_dependency_closure() -> None:
    """Project-scoped Python changes include downstream package dependents."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(
            ["src/public/lib/three-workflow-release-contracts/pyproject.toml"]
        ),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    selected_subject_ids = {
        item["subject-id"]
        for item in subjects
        if item["selection-status"] == "selected"
    }
    assert "python.src-public-lib-three-workflow-release-contracts" in (
        selected_subject_ids
    )
    assert "python.src-public-lib-three-workflow-release-authoring" in (
        selected_subject_ids
    )
    assert "python.src-public-lib-three-workflow-release-planner" in (
        selected_subject_ids
    )


def test_ci_validation_plans_transitive_downstream_dependency_basis() -> None:
    """Transitive downstream selections carry the complete dependency path."""
    from three_workflow_release_planner import (  # noqa: PLC0415
        ci_validation_planner as planner_module,
    )

    edge_to_b: dict[str, object] = {
        "from-subject-id": "python.package-b",
        "to-subject-id": "python.package-a",
        "relation": "runtime",
    }
    edge_to_c: dict[str, object] = {
        "from-subject-id": "python.package-c",
        "to-subject-id": "python.package-b",
        "relation": "runtime",
    }
    facts = planner_module._PlanningFacts(
        subjects={},
        providers=(),
        dependency_edges=(edge_to_b, edge_to_c),
        dependency_failures=(),
    )

    downstream = planner_module._downstream_subjects(
        "python.package-a",
        facts,
    )

    assert downstream == [
        ("python.package-b", [edge_to_b]),
        ("python.package-c", [edge_to_b, edge_to_c]),
    ]


def test_ci_validation_plans_python_build_system_dependency_closure() -> None:
    """Build-system requirements also drive Python downstream closure."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(["src/public/lib/nbgv-python/pyproject.toml"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    selected_subject_ids = {
        item["subject-id"]
        for item in subjects
        if item["selection-status"] == "selected"
    }
    assert "python.src-public-lib-nbgv-python" in selected_subject_ids
    assert "python.src-public-lib-hcoona-release-smoke" in selected_subject_ids
    assert "python.src-public-lib-hcoona-release-smoke-pypi" in (
        selected_subject_ids
    )
    artifact_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["artifact-obligations"],
    )
    assert not any(
        item["subject-id"] == "python.src-public-lib-hcoona-release-smoke"
        for item in artifact_obligations
    )


def test_ci_validation_plans_artifact_obligations_keep_shape_granularity() -> (
    None
):
    """Wheel and sdist targets remain distinct catalog-backed obligations."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(["src/public/lib/nbgv-python/pyproject.toml"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    artifact_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["artifact-obligations"],
    )
    nbgv_obligations = [
        obligation
        for obligation in artifact_obligations
        if obligation["subject-id"] == "python.src-public-lib-nbgv-python"
    ]
    obligations_by_ref = {
        ref: cast("Mapping[str, object]", obligation["artifact"])[
            "concrete-kind"
        ]
        for obligation in nbgv_obligations
        for ref in cast(
            "Sequence[str]",
            cast("Mapping[str, object]", obligation["artifact"])[
                "expected-artifact-refs"
            ],
        )
    }
    assert (
        obligations_by_ref[
            "ci-validation/artifacts/nbgv-python/official/wheel.artifact"
        ]
        == "wheel"
    )
    assert (
        obligations_by_ref[
            "ci-validation/artifacts/nbgv-python/official/sdist.artifact"
        ]
        == "sdist"
    )
    assert all(
        not (
            cast("Mapping[str, object]", obligation["artifact"])[
                "concrete-kind"
            ]
            == "wheel"
            and any(
                "sdist.artifact" in ref
                for ref in cast(
                    "Sequence[str]",
                    cast("Mapping[str, object]", obligation["artifact"])[
                        "expected-artifact-refs"
                    ],
                )
            )
        )
        for obligation in nbgv_obligations
    )


def test_ci_validation_plans_order_artifact_work_after_prerequisites() -> None:
    """Release-shaped artifact work waits for executable gate work."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(["src/public/lib/nbgv-python/pyproject.toml"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["work-groups"],
    )
    by_id = {str(item["work-group-id"]): item for item in work_groups}
    artifact_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["artifact-obligations"],
    )
    artifact_obligation = next(
        item
        for item in artifact_obligations
        if item["subject-id"] == "python.src-public-lib-nbgv-python"
    )
    artifact_group = by_id[str(artifact_obligation["work-group-id"])]
    dependencies = {
        by_id[str(dependency)]["kind"]
        for dependency in cast("Sequence[str]", artifact_group["depends-on"])
    }

    assert dependencies == {"ecosystem-gate"}
    assert any(
        group["kind"] == "descriptor-validation" for group in work_groups
    )


def test_ci_validation_dotnet_batches_do_not_depend_on_ubuntu_descriptors() -> (
    None
):
    """Descriptor evidence is aggregated, not a cross-runner prerequisite."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        materialize_ci_validation_execution_batches,
        validate_ci_validation_execution_batch_manifest,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = [
        "src/public/lib/hcoona-release-smoke-github-packages/"
        "hcoona-release-smoke-github-packages.csproj"
    ]
    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["work-groups"],
    )
    descriptor_group_ids = {
        str(group["work-group-id"])
        for group in work_groups
        if group["kind"] == "descriptor-validation"
    }
    dotnet_groups = [
        group for group in work_groups if group.get("ecosystem") == "dotnet"
    ]

    assert descriptor_group_ids
    assert dotnet_groups
    assert not any(
        descriptor_group_ids.intersection(
            cast("Sequence[str]", group["depends-on"]),
        )
        for group in dotnet_groups
    )

    materialization = materialize_ci_validation_execution_batches(
        plan=snapshot.plan,
        request=_ci_request(changed_files),
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        created_at="2026-05-14T21:09:21Z",
        execution_workflow="CI Validation",
        expected_run_id="25887422010",
        expected_run_attempt="1",
    )
    manifest = cast("Mapping[str, object]", materialization.manifest)
    validate_ci_validation_execution_batch_manifest(
        manifest,
        plan=snapshot.plan,
        request=_ci_request(changed_files),
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
        expected_run_id="25887422010",
        expected_run_attempt="1",
    )


def test_ci_validation_dotnet_release_artifacts_use_platform_runners() -> None:
    """NativeAOT release artifacts use a runner matching their OS dimension."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = [
        "src/public/app/PhiFailureDetector.Console/three.release.yml",
    ]
    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["work-groups"],
    )
    work_groups_by_id = {
        str(group["work-group-id"]): group for group in work_groups
    }
    obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["artifact-obligations"],
    )
    runner_by_os: dict[str, str] = {}
    for obligation in obligations:
        if obligation["subject-id"] != (
            "dotnet.src-public-app-phifailuredetector-console"
        ):
            continue
        artifact = cast("Mapping[str, object]", obligation["artifact"])
        dimensions = cast(
            "Mapping[str, object]",
            artifact["variant-dimensions"],
        )
        runner_by_os[str(dimensions["os"])] = str(
            work_groups_by_id[str(obligation["work-group-id"])]["runner-family"]
        )

    assert runner_by_os["windows"] == "windows"
    assert runner_by_os["linux"] == "ubuntu"
    assert runner_by_os["macos"] == "macos"


def test_ci_validation_fact_snapshot_workflow_release_owns_catalog_facts() -> (
    None
):
    """Descriptor and target-catalog facts are workflow-release facts."""
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs([]))

    fact_snapshot = snapshot.fact_snapshot
    assert fact_snapshot is not None
    providers = cast(
        "Sequence[Mapping[str, object]]",
        fact_snapshot["providers"],
    )
    providers_by_id = {
        str(provider["provider"]): provider for provider in providers
    }
    workflow_release = providers_by_id["workflow-release"]
    workflow_catalog = cast(
        "Mapping[str, object]",
        workflow_release["target-catalog"],
    )
    assert workflow_release["descriptors"]
    assert workflow_catalog["entries"]
    for provider_id in ("dotnet", "javascript-typescript", "python"):
        provider = providers_by_id[provider_id]
        catalog = cast("Mapping[str, object]", provider["target-catalog"])
        assert provider["subjects"]
        assert provider["descriptors"] == []
        assert catalog["entries"] == []


def test_ci_validation_plans_pnpm_workspace_globs_are_subjects() -> None:
    """PNPM package globs expand without reading unrelated YAML lists."""
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs([]))

    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    subject_ids = {str(item["subject-id"]) for item in subjects}
    assert {
        "typescript.src-private-app-im-acp-gateway-poc-telegram-bot-verifier",
        "typescript.src-private-app-im-acp-gateway-poc-telegram-topic-session-bridge",
        "typescript.src-private-app-im-acp-gateway-poc-wechat-ilink-verifier",
    } <= subject_ids
    assert not any("hexo-util" in subject_id for subject_id in subject_ids)


def test_ci_validation_plans_dependency_read_failures_fail_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Project-scoped dependency discovery failures fail closed."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation,
    )

    snapshot = validate_authoring(REPO_ROOT)
    broken_path = (
        REPO_ROOT / "src/public/lib/hcoona-release-smoke/pyproject.toml"
    )
    original_read_text = Path.read_text

    def fake_read_text(
        path: Path,
        encoding: str | None = None,
        errors: str | None = None,
        newline: str | None = None,
    ) -> str:
        if path == broken_path:
            message = "simulated dependency metadata read failure"
            raise OSError(message)
        return original_read_text(
            path,
            encoding=encoding,
            errors=errors,
            newline=newline,
        )

    monkeypatch.setattr(Path, "read_text", fake_read_text)

    result = plan_ci_validation(
        snapshot,
        _ci_inputs(["src/public/lib/nbgv-python/pyproject.toml"]),
    )

    validate_ci_validation_plan(
        result.plan,
        changed_files_snapshot=result.changed_files_snapshot,
        fact_snapshot=result.fact_snapshot,
    )
    assert result.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        result.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]", result.plan["work-groups"]
    )
    assert [item["kind"] for item in work_groups] == ["evidence-aggregation"]


@pytest.mark.parametrize(
    ("changed_files", "request_mode"),
    [
        (["src/lab/unowned/uv.lock"], "affected"),
        (["uv.lock"], "affected"),
        (
            ["src/public/lib/three-workflow-release-planner/pyproject.toml"],
            "affected",
        ),
        (
            ["src/public/lib/three-workflow-release-metadata/provider.py"],
            "affected",
        ),
        ([], "scheduled-full"),
    ],
)
def test_ci_validation_plans_broad_dependency_read_failures_fail_closed(
    monkeypatch: pytest.MonkeyPatch,
    changed_files: list[str],
    request_mode: str,
) -> None:
    """Broad-scope dependency discovery failures fail closed."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation,
    )

    snapshot = validate_authoring(REPO_ROOT)
    broken_path = (
        REPO_ROOT / "src/public/lib/hcoona-release-smoke/pyproject.toml"
    )
    original_read_text = Path.read_text

    def fake_read_text(
        path: Path,
        encoding: str | None = None,
        errors: str | None = None,
        newline: str | None = None,
    ) -> str:
        if path == broken_path:
            message = "simulated dependency metadata read failure"
            raise OSError(message)
        return original_read_text(
            path,
            encoding=encoding,
            errors=errors,
            newline=newline,
        )

    monkeypatch.setattr(Path, "read_text", fake_read_text)

    inputs = (
        _scheduled_ci_inputs()
        if request_mode == "scheduled-full"
        else _ci_inputs(changed_files)
    )
    result = plan_ci_validation(snapshot, inputs)

    validate_ci_validation_plan(
        result.plan,
        changed_files_snapshot=result.changed_files_snapshot,
        fact_snapshot=result.fact_snapshot,
    )
    assert result.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        result.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )
    work_groups = cast(
        "Sequence[Mapping[str, object]]", result.plan["work-groups"]
    )
    assert [item["kind"] for item in work_groups] == ["evidence-aggregation"]


@pytest.mark.parametrize(
    ("broken_path", "broken_content", "changed_file"),
    [
        (
            REPO_ROOT / "pyproject.toml",
            "[tool.uv.workspace\n",
            "pyproject.toml",
        ),
        (
            REPO_ROOT / "pnpm-workspace.yaml",
            "packages: [\n",
            "pnpm-workspace.yaml",
        ),
        (
            REPO_ROOT / "src/public/lib/hcoona-release-smoke-npm/package.json",
            "{",
            "src/public/lib/hcoona-release-smoke-npm/package.json",
        ),
    ],
)
def test_ci_validation_plans_fact_discovery_parse_failures_fail_closed(
    monkeypatch: pytest.MonkeyPatch,
    broken_path: Path,
    broken_content: str,
    changed_file: str,
) -> None:
    """Fact discovery parse failures fail closed instead of omitting facts."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation,
    )

    snapshot = validate_authoring(REPO_ROOT)
    original_read_text = Path.read_text

    def fake_read_text(
        path: Path,
        encoding: str | None = None,
        errors: str | None = None,
        newline: str | None = None,
    ) -> str:
        if path == broken_path:
            return broken_content
        return original_read_text(
            path,
            encoding=encoding,
            errors=errors,
            newline=newline,
        )

    monkeypatch.setattr(Path, "read_text", fake_read_text)

    result = plan_ci_validation(snapshot, _ci_inputs([changed_file]))

    validate_ci_validation_plan(
        result.plan,
        changed_files_snapshot=result.changed_files_snapshot,
        fact_snapshot=result.fact_snapshot,
    )
    assert result.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        result.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )
    classification = cast(
        "Mapping[str, object]",
        result.plan["classification"],
    )
    impacts = cast(
        "Sequence[Mapping[str, object]]",
        classification["impacts"],
    )
    requires = cast("Mapping[str, object]", impacts[0]["requires"])
    assert requires["diagnostic"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )


@pytest.mark.parametrize(
    ("broken_path", "changed_file"),
    [
        (
            REPO_ROOT / "src/public/lib/hcoona-release-smoke/pyproject.toml",
            "src/public/lib/nbgv-python/pyproject.toml",
        ),
        (
            REPO_ROOT / "src/public/lib/hcoona-release-smoke-npm/package.json",
            "src/public/lib/hcoona-release-smoke-npm/package.json",
        ),
    ],
)
def test_ci_validation_plans_dependency_utf8_failures_fail_closed(
    monkeypatch: pytest.MonkeyPatch,
    broken_path: Path,
    changed_file: str,
) -> None:
    """Invalid UTF-8 dependency metadata fails closed instead of crashing."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation,
    )

    snapshot = validate_authoring(REPO_ROOT)
    original_read_text = Path.read_text

    def fake_read_text(
        path: Path,
        encoding: str | None = None,
        errors: str | None = None,
        newline: str | None = None,
    ) -> str:
        if path == broken_path:
            encoding_name = "utf-8"
            reason = "invalid start"
            raise UnicodeDecodeError(encoding_name, b"\xff", 0, 1, reason)
        return original_read_text(
            path,
            encoding=encoding,
            errors=errors,
            newline=newline,
        )

    monkeypatch.setattr(Path, "read_text", fake_read_text)

    result = plan_ci_validation(snapshot, _ci_inputs([changed_file]))

    validate_ci_validation_plan(
        result.plan,
        changed_files_snapshot=result.changed_files_snapshot,
        fact_snapshot=result.fact_snapshot,
    )
    assert result.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        result.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )
    classification = cast(
        "Mapping[str, object]",
        result.plan["classification"],
    )
    impacts = cast(
        "Sequence[Mapping[str, object]]",
        classification["impacts"],
    )
    requires = cast("Mapping[str, object]", impacts[0]["requires"])
    assert requires["diagnostic"] == (
        DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value
    )


def test_ci_validation_global_tooling_obligations_have_source_impacts() -> None:
    """Affected global tooling obligations retain their source impact IDs."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(
            [
                "docs/wiki/analyses/"
                "workflow-release-ci-affected-validation-low-level-design.md"
            ]
        )
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    validation_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["validation-obligations"],
    )
    tooling_obligations = [
        item
        for item in validation_obligations
        if item["kind"] == "workflow-release-tooling"
    ]
    assert tooling_obligations
    assert all(item["source-impact-ids"] for item in tooling_obligations)


def test_ci_validation_workflow_release_project_paths_are_tooling() -> None:
    """Workflow-release package paths are classified by tooling surface."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_file = (
        "src/public/lib/three-workflow-release-planner/src/"
        "three_workflow_release_planner/ci_validation_planner.py"
    )
    snapshot = plan_ci_validation_from_repo(_ci_inputs([changed_file]))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert len(impacts) == 1
    assert impacts[0]["category"] == "workflow-release-infrastructure"
    assert impacts[0]["coverage-target"] == {
        "type": "tooling-surface",
        "id": "planner",
    }
    validation_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["validation-obligations"],
    )
    tooling_obligations = [
        item
        for item in validation_obligations
        if item["kind"] == "workflow-release-tooling"
    ]
    assert tooling_obligations


def test_ci_validation_metadata_paths_are_fact_provider_tooling() -> None:
    """Metadata package changes exercise the fact-provider surface."""
    from three_workflow_release_planner import (  # noqa: PLC0415
        ci_validation_planner as ci,
    )

    changed_file = (
        "src/public/lib/three-workflow-release-metadata/src/"
        "three_workflow_release_metadata/dotnet_metadata.py"
    )
    impact = ci._classify_path(
        changed_file,
        ci._PlanningFacts({}, (), (), ()),
    )

    assert impact.category == "workflow-release-infrastructure"
    assert impact.coverage_target == {
        "type": "tooling-surface",
        "id": "fact-provider",
    }


def test_ci_validation_active_release_helper_paths_are_tooling() -> None:
    """Active release helper and config paths classify to closed surfaces."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    release_orchestrate_helpers = {
        path.relative_to(REPO_ROOT).as_posix(): "workflow-orchestration"
        for path in sorted(
            (REPO_ROOT / "eng/scripts").glob("release_orchestrate_*.sh")
        )
    }
    release_publish_helpers = {
        path.relative_to(REPO_ROOT).as_posix(): "publish-execution"
        for path in sorted(
            (REPO_ROOT / "eng/scripts").glob("publish_*_idempotent.sh")
        )
    }
    expected_surfaces = (
        {
            ".github/CODEOWNERS": "workflow-orchestration",
            ".github/actionlint.yaml": "workflow-orchestration",
            "eng/scripts/find_project_path.py": "workflow-orchestration",
            "eng/scripts/validate_pep440_version.py": "workflow-orchestration",
            "eng/scripts/validate_semver2_version.py": "workflow-orchestration",
            "eng/scripts/validate_rubygems_version.py": (
                "workflow-orchestration"
            ),
            "eng/scripts/prepare_npm_publish.py": "publish-execution",
            "eng/scripts/validate_pypi_remote_digests.sh": "publish-execution",
            (
                "eng/scripts/verify_python_distribution_exactness.py"
            ): "build-execution",
            "eng/scripts/verify_python_artifact_version.py": (
                "build-execution"
            ),
            "tests/test_verify_python_distribution_exactness.py": (
                "build-execution"
            ),
        }
        | release_orchestrate_helpers
        | release_publish_helpers
    )
    assert "eng/scripts/release_orchestrate_policy_publish_targets.sh" in (
        expected_surfaces
    )
    assert "eng/scripts/validate_pypi_remote_digests.sh" in expected_surfaces
    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(sorted(expected_surfaces)),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    surface_by_path = {
        path: str(cast("Mapping[str, object]", impact["coverage-target"])["id"])
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    }
    assert surface_by_path == expected_surfaces


def test_ci_validation_build_execution_scope_covers_build_subjects() -> None:
    """Build tooling changes select every active build-capable subject."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_file = (
        "src/public/lib/three-workflow-release-build/src/"
        "three_workflow_release_build/executor.py"
    )
    snapshot = plan_ci_validation_from_repo(_ci_inputs([changed_file]))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "executable"
    subjects = cast("Sequence[Mapping[str, object]]", snapshot.plan["subjects"])
    active_build_subjects = [
        subject
        for subject in subjects
        if subject["activity-status"] == "active"
        and (
            cast("Mapping[str, object]", subject["capabilities"]).get("build")
            is True
            or cast("Mapping[str, object]", subject["capabilities"]).get(
                "release-shaped-artifacts",
            )
            is True
        )
    ]
    assert active_build_subjects
    assert any(
        subject["capability-class"] == "validation-only"
        for subject in active_build_subjects
    )
    assert {
        subject["selection-status"] for subject in active_build_subjects
    } == {"selected"}
    descriptor_obligations = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["descriptor-obligations"],
    )
    assert descriptor_obligations
    assert {
        obligation["descriptor-scope"] for obligation in descriptor_obligations
    } == {"all-discovered"}


def test_ci_validation_freeze_fallback_covers_changed_files(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Executable freeze fallback preserves changed-file coverage."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        ContractValidationError,
        DiagnosticFamily,
        ValidationIssue,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        ci_validation_planner,
    )

    original_freeze = ci_validation_planner.freeze_ci_validation_plan
    calls = 0

    def fail_first_executable_freeze(**kwargs: Any) -> object:
        nonlocal calls
        calls += 1
        if calls == 1 and kwargs["verdict_intent"] == "executable":
            raise ContractValidationError(
                [ValidationIssue("$.classification", "forced failure")],
            )
        return original_freeze(**kwargs)

    expected_freeze_calls = 2
    changed_files = [
        "src/public/lib/three-workflow-release-planner/src/"
        "three_workflow_release_planner/ci_validation_planner.py",
    ]
    monkeypatch.setattr(
        ci_validation_planner,
        "freeze_ci_validation_plan",
        fail_first_executable_freeze,
    )

    snapshot = ci_validation_planner.plan_ci_validation_from_repo(
        _ci_inputs(changed_files),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert calls == expected_freeze_calls
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert {
        path
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    } == set(changed_files)
    assert {impact["category"] for impact in impacts} == {"unknown"}
    assert {
        cast("Mapping[str, object]", impact["requires"])["diagnostic"]
        for impact in impacts
    } == {DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value}


def test_ci_validation_capability_failures_cover_changed_files(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Capability fail-closed plans do not keep unresolved targets."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        ci_validation_planner,
    )

    def force_capability_failure(
        *_args: Any,
        **_kwargs: Any,
    ) -> list[dict[str, object]]:
        return [
            ci_validation_planner._diagnostic(
                code=DiagnosticFamily.FACT_PROVIDER_INSUFFICIENT.value,
                detail=None,
                message="forced capability failure",
                source_type="subject",
                source_id="subject-forced",
                ordinal=1,
            ),
        ]

    changed_files = ["src/public/lib/hcoona-release-smoke/pyproject.toml"]
    monkeypatch.setattr(
        ci_validation_planner,
        "_capability_diagnostics",
        force_capability_failure,
    )

    snapshot = ci_validation_planner.plan_ci_validation_from_repo(
        _ci_inputs(changed_files),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert [
        path
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    ] == changed_files
    assert {impact["category"] for impact in impacts} == {"unknown"}
    coverage_target_types = {
        cast("Mapping[str, object]", impact["coverage-target"])["type"]
        for impact in impacts
    }
    assert coverage_target_types == {"none"}


def test_ci_validation_authoring_validation_failure_covers_changed_files(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Authoring validation failures preserve changed-file coverage."""
    from three_workflow_release_authoring import (  # noqa: PLC0415
        AuthoringIssue,
        AuthoringValidationError,
    )
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        ci_validation_planner,
    )

    def fail_authoring_validation(*_args: Any, **_kwargs: Any) -> object:
        raise AuthoringValidationError(
            [
                AuthoringIssue(
                    code="SIMULATED_AUTHORING_FAILURE",
                    path="workflow-release/projects/example.yaml",
                    message="simulated authoring validation failure",
                ),
            ],
        )

    changed_files = [
        "src/public/lib/three-workflow-release-planner/src/"
        "three_workflow_release_planner/ci_validation_planner.py",
        "src/public/lib/three-workflow-release-planner/tests/test_planner.py",
    ]
    monkeypatch.setattr(
        ci_validation_planner,
        "validate_authoring",
        fail_authoring_validation,
    )

    snapshot = ci_validation_planner.plan_ci_validation_from_repo(
        _ci_inputs(changed_files),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == DiagnosticFamily.DESCRIPTOR_INVALID.value
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert [
        path
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    ] == sorted(changed_files)
    assert {impact["category"] for impact in impacts} == {"unknown"}
    assert {
        cast("Mapping[str, object]", impact["requires"])["diagnostic"]
        for impact in impacts
    } == {DiagnosticFamily.DESCRIPTOR_INVALID.value}


def test_ci_validation_plans_unknown_paths_fail_closed() -> None:
    """Unclassified paths produce an inspectable fail-closed plan."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(_ci_inputs(["unknown.bin"]))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == DiagnosticFamily.UNKNOWN_CHANGE.value


def test_ci_validation_fail_closed_diagnostics_are_contract_ordered() -> None:
    """Multiple fail-closed diagnostics are emitted in contract order."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    snapshot = plan_ci_validation_from_repo(
        _ci_inputs(["unknown-2.bin", "unknown-3.bin"]),
    )

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["diagnostics"],
    )
    diagnostic_ids = [
        str(diagnostic["diagnostic-id"]) for diagnostic in diagnostics
    ]
    assert diagnostic_ids == sorted(diagnostic_ids)
    assert {diagnostic["code"] for diagnostic in diagnostics} == {
        DiagnosticFamily.UNKNOWN_CHANGE.value,
    }


def test_ci_validation_mixed_unknown_paths_cover_all_changes() -> None:
    """Mixed known and unknown impacts fail closed with resolvable coverage."""
    from three_workflow_release_contracts import (  # noqa: PLC0415
        DiagnosticFamily,
        validate_ci_validation_plan,
    )
    from three_workflow_release_planner import (  # noqa: PLC0415
        plan_ci_validation_from_repo,
    )

    changed_files = [
        "src/public/lib/hcoona-release-smoke/pyproject.toml",
        "unknown-2.bin",
    ]

    snapshot = plan_ci_validation_from_repo(_ci_inputs(changed_files))

    validate_ci_validation_plan(
        snapshot.plan,
        changed_files_snapshot=snapshot.changed_files_snapshot,
        fact_snapshot=snapshot.fact_snapshot,
    )
    assert snapshot.plan["verdict-intent"] == "fail-closed"
    diagnostics = cast(
        "Sequence[Mapping[str, object]]",
        snapshot.plan["diagnostics"],
    )
    assert diagnostics[0]["code"] == DiagnosticFamily.UNKNOWN_CHANGE.value
    classification = cast(
        "Mapping[str, object]", snapshot.plan["classification"]
    )
    impacts = cast("Sequence[Mapping[str, object]]", classification["impacts"])
    assert {
        path
        for impact in impacts
        for path in cast("Sequence[str]", impact["matched-paths"])
    } == set(changed_files)
    assert {impact["category"] for impact in impacts} == {"unknown"}
