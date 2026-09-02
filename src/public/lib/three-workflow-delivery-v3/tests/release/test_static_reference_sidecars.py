"""Black-box contracts for the Workflow Delivery v3 Node authority."""

from __future__ import annotations

import importlib
import json
import os
import shutil
import subprocess
import textwrap
from functools import cache
from pathlib import Path
from typing import Any

import pytest

_REPOSITORY_ROOT = Path(__file__).resolve().parents[6]
_NODE_AUTHORITY = (
    _REPOSITORY_ROOT
    / "eng"
    / "scripts"
    / "workflow_delivery_v3_static_reference_node.mjs"
)
_NODE_BINARY = shutil.which("node")
_NODE_RUNTIME_UNAVAILABLE = "the prepared Node.js runtime is unavailable"
if _NODE_BINARY is None:
    raise RuntimeError(_NODE_RUNTIME_UNAVAILABLE)

_REQUEST_SCHEMA = "workflow-delivery/v3/static-reference-node-authority-request"
_RESPONSE_SCHEMA = (
    "workflow-delivery/v3/static-reference-node-authority-response"
)
_NPM_PACKAGES = (
    "@npmcli/package-json@8.0.0",
    "npm-package-arg@14.0.0",
)
_PNPM_WORKSPACE_PACKAGES = (
    "@pnpm/resolving.npm-resolver@1104.1.0",
    "@pnpm/workspace.spec-parser@1100.0.1",
    "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
    "npm-package-arg@14.0.0",
)
_PNPM_LOCK_PACKAGES = (
    "@pnpm/deps.path@1101.0.1",
    "@pnpm/lockfile.fs@1100.2.5",
    "@pnpm/lockfile.utils@1102.1.0",
    "@pnpm/resolving.npm-resolver@1104.1.0",
    "@pnpm/workspace.spec-parser@1100.0.1",
)
_UTF8_BOM = b"\xef\xbb\xbf"


@cache
def _node_identity() -> str:
    completed = subprocess.run(  # noqa: S603
        [_NODE_BINARY, "--version"],
        cwd=_REPOSITORY_ROOT,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    assert completed.stderr == ""
    assert completed.stdout.startswith("v")
    return f"node@{completed.stdout.removeprefix('v').strip()}"


def _implementation_identities(*packages: str) -> list[str]:
    return sorted(
        [_node_identity(), *packages],
        key=lambda identity: identity.encode(),
    )


def _run_node_authority(  # noqa: PLR0913
    *,
    snapshot_root: Path,
    candidate_path: Path,
    graph: str,
    logical_path: str | None = None,
    cwd: Path | None = None,
    home: Path | None = None,
    extra_environment: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    controlled_home = home or snapshot_root.parent / "controlled-home"
    controlled_home.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    environment.update(
        {
            "HOME": str(controlled_home),
            "USERPROFILE": str(controlled_home),
            "XDG_CACHE_HOME": str(controlled_home / ".cache"),
            "XDG_CONFIG_HOME": str(controlled_home / ".config"),
            "npm_config_cache": str(controlled_home / ".npm-cache"),
        }
    )
    if extra_environment is not None:
        environment.update(extra_environment)
    request = {
        "candidatePath": str(candidate_path),
        "graph": graph,
        "logicalPath": (
            logical_path
            if logical_path is not None
            else candidate_path.relative_to(snapshot_root).as_posix()
        ),
        "schema": _REQUEST_SCHEMA,
        "snapshotRoot": str(snapshot_root),
    }
    return subprocess.run(  # noqa: S603
        [_NODE_BINARY, str(_NODE_AUTHORITY)],
        cwd=cwd or _REPOSITORY_ROOT,
        env=environment,
        input=json.dumps(request, separators=(",", ":")),
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )


def _assert_facts_response(
    completed: subprocess.CompletedProcess[str],
    *,
    graph: str,
    facts: list[dict[str, Any]],
    packages: tuple[str, ...],
) -> dict[str, Any]:
    expected = {
        "facts": facts,
        "graph": graph,
        "implementationIdentities": _implementation_identities(*packages),
        "result": "facts",
        "schema": _RESPONSE_SCHEMA,
    }
    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == json.dumps(
        expected,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    actual = json.loads(completed.stdout)
    assert actual == expected
    return actual


def _assert_error_response(
    completed: subprocess.CompletedProcess[str],
    *,
    graph: str,
    error_kind: str,
    packages: tuple[str, ...],
) -> dict[str, Any]:
    expected = {
        "errorKind": error_kind,
        "graph": graph,
        "implementationIdentities": _implementation_identities(*packages),
        "result": "error",
        "schema": _RESPONSE_SCHEMA,
    }
    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == json.dumps(
        expected,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    actual = json.loads(completed.stdout)
    assert actual == expected
    return actual


def _write_package_json(path: Path, document: dict[str, Any]) -> bytes:
    path.parent.mkdir(parents=True, exist_ok=True)
    content = (
        json.dumps(document, ensure_ascii=False, indent=2) + "\n"
    ).encode()
    path.write_bytes(content)
    return content


def _write_lockfile(path: Path, content: str, *, bom_count: int = 0) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes((_UTF8_BOM * bom_count) + content.encode())


def _yaml(content: str) -> str:
    return textwrap.dedent(content).lstrip()


def _npm_reference(  # noqa: PLR0913
    *,
    name: str,
    raw_spec: str,
    reference_type: str = "version",
    fetch_spec: str | None = None,
    save_spec: str | None = None,
    local_path: str | None = None,
    alias_target: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "aliasTarget": alias_target,
        "fetchSpec": raw_spec if fetch_spec is None else fetch_spec,
        "localPath": local_path,
        "name": name,
        "rawSpec": raw_spec,
        "saveSpec": save_spec,
        "type": reference_type,
    }


def _npm_dependency_fact(
    *,
    dependency_key: str,
    reference: dict[str, Any],
    section: str,
    source_spec: str,
) -> dict[str, Any]:
    return {
        "dependencyKey": dependency_key,
        "kind": "npm-reference",
        "reference": reference,
        "section": section,
        "sourceSpec": source_spec,
    }


def _workspace_npm_reference(
    *,
    dependency_key: str,
    source_spec: str,
) -> dict[str, Any]:
    return {
        "kind": "npm",
        "npm": _npm_reference(
            name=dependency_key,
            raw_spec=source_spec,
        ),
    }


def _catalog_fact(
    *,
    catalog_kind: str,
    catalog_name: str | None,
    dependency_key: str,
    reference: dict[str, Any],
    source_spec: str,
) -> dict[str, Any]:
    return {
        "catalogKind": catalog_kind,
        "catalogName": catalog_name,
        "dependencyKey": dependency_key,
        "kind": "pnpm-workspace-reference",
        "reference": reference,
        "sourceSpec": source_spec,
    }


def _snapshot_fact(  # noqa: PLR0913
    *,
    dependency_path: str,
    dependencies: list[dict[str, str]],
    name: str,
    non_semver_version: str | None,
    resolution: dict[str, Any],
    version: str | None,
) -> dict[str, Any]:
    return {
        "dependencyPath": dependency_path,
        "dependencies": dependencies,
        "kind": "pnpm-lock-snapshot",
        "name": name,
        "nonSemverVersion": non_semver_version,
        "registryName": None,
        "resolution": resolution,
        "version": version,
    }


def _importer_fact(  # noqa: PLR0913
    *,
    dependency_key: str,
    importer_id: str,
    raw_specifier: str,
    registry_spec: dict[str, str] | None,
    resolved_reference: str,
    section: str,
    snapshot_key: str | None,
    workspace_selector: str | None,
) -> dict[str, Any]:
    return {
        "dependencyKey": dependency_key,
        "importerId": importer_id,
        "kind": "pnpm-lock-importer-reference",
        "rawSpecifier": raw_specifier,
        "registrySpec": registry_spec,
        "resolvedReference": resolved_reference,
        "section": section,
        "snapshotKey": snapshot_key,
        "workspaceSelector": workspace_selector,
    }


def _registry_spec(
    *,
    fetch_spec: str,
    name: str,
    specifier_type: str = "version",
) -> dict[str, str]:
    return {
        "fetchSpec": fetch_spec,
        "name": name,
        "type": specifier_type,
    }


def _minimal_v9_lockfile() -> str:
    return _yaml(
        """
        lockfileVersion: '9.0'

        importers:
          .:
            dependencies:
              exact:
                specifier: 1.2.3
                version: 1.2.3

        packages:
          exact@1.2.3:
            resolution:
              integrity: sha512-YWJj

        snapshots:
          exact@1.2.3: {}
        """
    )


def _minimal_v9_facts() -> list[dict[str, Any]]:
    return [
        _snapshot_fact(
            dependency_path="exact@1.2.3",
            dependencies=[],
            name="exact",
            non_semver_version=None,
            resolution={"kind": "registry"},
            version="1.2.3",
        ),
        _importer_fact(
            dependency_key="exact",
            importer_id=".",
            raw_specifier="1.2.3",
            registry_spec=_registry_spec(
                fetch_spec="1.2.3",
                name="exact",
            ),
            resolved_reference="1.2.3",
            section="dependencies",
            snapshot_key="exact@1.2.3",
            workspace_selector=None,
        ),
    ]


def test_node_npm_protocol_reads_one_snapshot_content_without_mutation(
    tmp_path: Path,
) -> None:
    """Read loaded package content and leave the selected bytes unchanged."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "application" / "package.json"
    original = _write_package_json(
        candidate_path,
        {
            "name": "snapshot-owned-package",
            "version": "9.8.7",
            "dependencies": {"selected-dependency": "1.2.3"},
            "private": True,
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            {
                "context": "name",
                "kind": "npm-package-name",
                "name": "snapshot-owned-package",
            },
            _npm_dependency_fact(
                dependency_key="selected-dependency",
                reference=_npm_reference(
                    name="selected-dependency",
                    raw_spec="1.2.3",
                ),
                section="dependencies",
                source_spec="1.2.3",
            ),
        ],
    )
    assert candidate_path.read_bytes() == original


def test_node_protocol_binds_logical_path_to_materialized_candidate(
    tmp_path: Path,
) -> None:
    """Reject a candidate path labeled as a different logical artifact."""
    snapshot_root = tmp_path / "snapshot"
    first = snapshot_root / "first" / "package.json"
    second = snapshot_root / "second" / "package.json"
    _write_package_json(first, {"name": "first"})
    _write_package_json(second, {"name": "second"})

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=second,
        logical_path="first/package.json",
        graph="npm-manifest-v1",
    )

    assert completed.returncode == 1
    assert completed.stdout == ""
    expected_stderr = "static-reference node authority execution failed\n"
    assert completed.stderr == expected_stderr


def test_node_npm_protocol_orders_name_and_four_dependency_sections_exactly(
    tmp_path: Path,
) -> None:
    """Emit name, sections, and keys in the protocol's canonical order."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "ordered" / "package.json"
    _write_package_json(
        candidate_path,
        {
            "peerDependencies": {
                "z-peer": "8.0.0",
                "a-peer": "7.0.0",
            },
            "optionalDependencies": {
                "z-optional": "6.0.0",
                "a-optional": "5.0.0",
            },
            "devDependencies": {
                "z-dev": "4.0.0",
                "a-dev": "3.0.0",
            },
            "dependencies": {
                "z-runtime": "2.0.0",
                "a-runtime": "1.0.0",
            },
            "name": "ordered-package",
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    ordered_entries = (
        ("dependencies", "a-runtime", "1.0.0"),
        ("dependencies", "z-runtime", "2.0.0"),
        ("devDependencies", "a-dev", "3.0.0"),
        ("devDependencies", "z-dev", "4.0.0"),
        ("optionalDependencies", "a-optional", "5.0.0"),
        ("optionalDependencies", "z-optional", "6.0.0"),
        ("peerDependencies", "a-peer", "7.0.0"),
        ("peerDependencies", "z-peer", "8.0.0"),
    )
    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            {
                "context": "name",
                "kind": "npm-package-name",
                "name": "ordered-package",
            },
            *[
                _npm_dependency_fact(
                    dependency_key=dependency_key,
                    reference=_npm_reference(
                        name=dependency_key,
                        raw_spec=source_spec,
                    ),
                    section=section,
                    source_spec=source_spec,
                )
                for section, dependency_key, source_spec in ordered_entries
            ],
        ],
    )


@pytest.mark.parametrize(
    ("case", "dependency_key", "source_spec"),
    [
        pytest.param(
            "registry",
            "registry-dependency",
            "1.2.3",
            id="registry-version",
        ),
        pytest.param(
            "alias",
            "aliased-dependency",
            "npm:@scope/actual-package@2.3.4",
            id="one-level-alias",
        ),
        pytest.param(
            "file",
            "archive-dependency",
            "file:../../archives/archive.tgz",
            id="file-relative",
        ),
        pytest.param(
            "directory",
            "directory-dependency",
            "../../vendor/local-package",
            id="directory-relative",
        ),
    ],
)
def test_node_npm_protocol_projects_pinned_npa_alias_and_local_facts(
    tmp_path: Path,
    case: str,
    dependency_key: str,
    source_spec: str,
) -> None:
    """Pin complete npm-package-arg facts for every selected reference kind."""
    snapshot_root = tmp_path / "snapshot"
    package_directory = snapshot_root / "sources" / "application"
    candidate_path = package_directory / "package.json"
    _write_package_json(
        candidate_path,
        {"dependencies": {dependency_key: source_spec}},
    )

    if case == "registry":
        reference = _npm_reference(
            name=dependency_key,
            raw_spec=source_spec,
        )
    elif case == "alias":
        reference = _npm_reference(
            name=dependency_key,
            raw_spec=source_spec,
            reference_type="alias",
            fetch_spec=None,
            alias_target=_npm_reference(
                name="@scope/actual-package",
                raw_spec="2.3.4",
            ),
        )
        reference["fetchSpec"] = None
    elif case == "file":
        absolute_target = snapshot_root / "archives" / "archive.tgz"
        reference = _npm_reference(
            name=dependency_key,
            raw_spec=source_spec,
            reference_type="file",
            fetch_spec=str(absolute_target),
            save_spec=source_spec,
            local_path="archives/archive.tgz",
        )
    else:
        absolute_target = snapshot_root / "vendor" / "local-package"
        reference = _npm_reference(
            name=dependency_key,
            raw_spec=source_spec,
            reference_type="directory",
            fetch_spec=str(absolute_target),
            save_spec=f"file:{source_spec}",
            local_path="vendor/local-package",
        )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            _npm_dependency_fact(
                dependency_key=dependency_key,
                reference=reference,
                section="dependencies",
                source_spec=source_spec,
            )
        ],
    )


def test_node_npm_protocol_preserves_an_empty_official_specifier(
    tmp_path: Path,
) -> None:
    """Preserve the exact empty npm-package-arg values without hardening."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "package.json"
    _write_package_json(
        candidate_path,
        {"dependencies": {"ordinary": ""}},
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            _npm_dependency_fact(
                dependency_key="ordinary",
                reference=_npm_reference(
                    name="ordinary",
                    raw_spec="",
                    reference_type="range",
                ),
                section="dependencies",
                source_spec="",
            )
        ],
    )


def test_node_npm_protocol_uses_the_source_owned_base_for_relative_specs(
    tmp_path: Path,
) -> None:
    """Resolve local specs from the selected source rather than cwd or HOME."""
    snapshot_root = tmp_path / "snapshot"
    package_directory = snapshot_root / "source-owned" / "application"
    candidate_path = package_directory / "package.json"
    execution_directory = tmp_path / "unrelated-execution-directory"
    uncontrolled_home = tmp_path / "uncontrolled-home"
    execution_directory.mkdir()
    (execution_directory / "archives").mkdir()
    uncontrolled_home.mkdir()
    (uncontrolled_home / "vendor").mkdir()
    original = _write_package_json(
        candidate_path,
        {
            "dependencies": {
                "local-directory": "../vendor/local-package",
                "local-tarball": "file:../archives/archive.tgz",
            }
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
        cwd=execution_directory,
        home=uncontrolled_home,
    )

    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            _npm_dependency_fact(
                dependency_key="local-directory",
                reference=_npm_reference(
                    name="local-directory",
                    raw_spec="../vendor/local-package",
                    reference_type="directory",
                    fetch_spec=str(
                        snapshot_root
                        / "source-owned"
                        / "vendor"
                        / "local-package"
                    ),
                    save_spec="file:../vendor/local-package",
                    local_path="source-owned/vendor/local-package",
                ),
                section="dependencies",
                source_spec="../vendor/local-package",
            ),
            _npm_dependency_fact(
                dependency_key="local-tarball",
                reference=_npm_reference(
                    name="local-tarball",
                    raw_spec="file:../archives/archive.tgz",
                    reference_type="file",
                    fetch_spec=str(
                        snapshot_root
                        / "source-owned"
                        / "archives"
                        / "archive.tgz"
                    ),
                    save_spec="file:../archives/archive.tgz",
                    local_path="source-owned/archives/archive.tgz",
                ),
                section="dependencies",
                source_spec="file:../archives/archive.tgz",
            ),
        ],
    )
    assert candidate_path.read_bytes() == original


@pytest.mark.parametrize(
    "invalid_selected_field",
    [
        pytest.param([], id="dependencies-not-an-object"),
        pytest.param(
            {"a-valid": "1.0.0", "z-invalid": 17},
            id="dependency-specifier-not-a-string",
        ),
        pytest.param(None, id="selected-section-null"),
    ],
)
def test_node_npm_protocol_rejects_selected_field_shape_without_partial_facts(
    tmp_path: Path,
    invalid_selected_field: Any,
) -> None:
    """Reject malformed selected sections without returning any facts."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "invalid" / "package.json"
    _write_package_json(
        candidate_path,
        {
            "name": "valid-name-before-invalid-shape",
            "dependencies": {"valid-runtime": "1.0.0"},
            "devDependencies": {"valid-development": "2.0.0"},
            "optionalDependencies": invalid_selected_field,
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    response = _assert_error_response(
        completed,
        graph="npm-manifest-v1",
        error_kind="unsupported-projection",
        packages=_NPM_PACKAGES,
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }


def test_node_npm_protocol_ignores_unselected_fields_and_uncontrolled_home(
    tmp_path: Path,
) -> None:
    """Ignore unselected shapes and package data outside the snapshot."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "application" / "package.json"
    uncontrolled_home = tmp_path / "outside-snapshot-home"
    uncontrolled_home.mkdir()
    home_package = _write_package_json(
        uncontrolled_home / "package.json",
        {
            "name": 42,
            "dependencies": ["must", "not", "be", "selected"],
        },
    )
    (uncontrolled_home / ".npmrc").write_text(
        "registry=https://unreachable.invalid/\n",
        encoding="utf-8",
    )
    original = _write_package_json(
        candidate_path,
        {
            "name": "selected-package",
            "dependencies": {"selected": "3.4.5"},
            "scripts": {"postinstall": {"not": "a command"}},
            "workflowDeliverySentinel": {
                "dependencies": [None, 99],
                "nested": {"peerDependencies": False},
            },
            "workspaces": "intentionally-unselected-shape",
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
        home=uncontrolled_home,
        extra_environment={
            "npm_config_userconfig": str(uncontrolled_home / ".npmrc")
        },
    )

    _assert_facts_response(
        completed,
        graph="npm-manifest-v1",
        packages=_NPM_PACKAGES,
        facts=[
            {
                "context": "name",
                "kind": "npm-package-name",
                "name": "selected-package",
            },
            _npm_dependency_fact(
                dependency_key="selected",
                reference=_npm_reference(
                    name="selected",
                    raw_spec="3.4.5",
                ),
                section="dependencies",
                source_spec="3.4.5",
            ),
        ],
    )
    assert candidate_path.read_bytes() == original
    assert (uncontrolled_home / "package.json").read_bytes() == home_package


def test_node_pnpm_protocol_accepts_exact_v9_and_bound_bom_behavior(
    tmp_path: Path,
) -> None:
    """Pin v9, BOM, generation, and selected-path behavior."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "project" / "pnpm-lock.yaml"
    lockfile = _minimal_v9_lockfile()
    observed_outputs = []

    for bom_count in (0, 1, 2):
        _write_lockfile(
            candidate_path,
            lockfile,
            bom_count=bom_count,
        )
        completed = _run_node_authority(
            snapshot_root=snapshot_root,
            candidate_path=candidate_path,
            graph="pnpm-lock-v1",
        )
        observed_outputs.append(
            _assert_facts_response(
                completed,
                graph="pnpm-lock-v1",
                facts=_minimal_v9_facts(),
                packages=_PNPM_LOCK_PACKAGES,
            )
        )

    assert observed_outputs[0] == observed_outputs[1] == observed_outputs[2]

    _write_lockfile(candidate_path, lockfile, bom_count=3)
    extra_bom = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-lock-v1",
    )
    _assert_error_response(
        extra_bom,
        graph="pnpm-lock-v1",
        error_kind="authority-rejected",
        packages=_PNPM_LOCK_PACKAGES,
    )

    wrong_generation = lockfile.replace("'9.0'", "'8.0'", 1)
    _write_lockfile(candidate_path, wrong_generation)
    generation_result = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-lock-v1",
    )
    _assert_error_response(
        generation_result,
        graph="pnpm-lock-v1",
        error_kind="authority-rejected",
        packages=_PNPM_LOCK_PACKAGES,
    )

    _write_lockfile(candidate_path, lockfile)
    wrong_selected_path = candidate_path.with_name("not-pnpm-lock.yaml")
    _write_lockfile(wrong_selected_path, wrong_generation)
    selected_path_result = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=wrong_selected_path,
        graph="pnpm-lock-v1",
    )
    selected_path_document = _assert_facts_response(
        selected_path_result,
        graph="pnpm-lock-v1",
        facts=_minimal_v9_facts(),
        packages=_PNPM_LOCK_PACKAGES,
    )
    assert selected_path_document["result"] == "facts"
    assert wrong_selected_path.read_text(encoding="utf-8") == wrong_generation


def test_node_pnpm_protocol_orders_catalog_importer_snapshot_and_preserves_equal_keys(  # noqa: E501
    tmp_path: Path,
) -> None:
    """Order all pnpm fact groups while preserving equal keys across groups."""
    snapshot_root = tmp_path / "snapshot"
    workspace_path = snapshot_root / "pnpm-workspace.yaml"
    workspace_path.parent.mkdir(parents=True)
    workspace_path.write_text(
        _yaml(
            """
            packages:
              - packages/zeta
              - packages/alpha

            catalog:
              z-default: 2.0.0
              a-default: 1.0.0

            catalogs:
              z-catalog:
                same-key: 4.0.0
                a-named: 3.0.0
              a-catalog:
                z-named: 2.0.0
                same-key: 1.0.0
            """
        ),
        encoding="utf-8",
    )

    workspace_result = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=workspace_path,
        graph="pnpm-workspace-v1",
    )

    _assert_facts_response(
        workspace_result,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[
            {
                "index": 0,
                "kind": "pnpm-workspace-pattern",
                "pattern": "packages/zeta",
            },
            {
                "index": 1,
                "kind": "pnpm-workspace-pattern",
                "pattern": "packages/alpha",
            },
            _catalog_fact(
                catalog_kind="default",
                catalog_name=None,
                dependency_key="a-default",
                reference=_workspace_npm_reference(
                    dependency_key="a-default",
                    source_spec="1.0.0",
                ),
                source_spec="1.0.0",
            ),
            _catalog_fact(
                catalog_kind="default",
                catalog_name=None,
                dependency_key="z-default",
                reference=_workspace_npm_reference(
                    dependency_key="z-default",
                    source_spec="2.0.0",
                ),
                source_spec="2.0.0",
            ),
            _catalog_fact(
                catalog_kind="named",
                catalog_name="a-catalog",
                dependency_key="same-key",
                reference=_workspace_npm_reference(
                    dependency_key="same-key",
                    source_spec="1.0.0",
                ),
                source_spec="1.0.0",
            ),
            _catalog_fact(
                catalog_kind="named",
                catalog_name="a-catalog",
                dependency_key="z-named",
                reference=_workspace_npm_reference(
                    dependency_key="z-named",
                    source_spec="2.0.0",
                ),
                source_spec="2.0.0",
            ),
            _catalog_fact(
                catalog_kind="named",
                catalog_name="z-catalog",
                dependency_key="a-named",
                reference=_workspace_npm_reference(
                    dependency_key="a-named",
                    source_spec="3.0.0",
                ),
                source_spec="3.0.0",
            ),
            _catalog_fact(
                catalog_kind="named",
                catalog_name="z-catalog",
                dependency_key="same-key",
                reference=_workspace_npm_reference(
                    dependency_key="same-key",
                    source_spec="4.0.0",
                ),
                source_spec="4.0.0",
            ),
        ],
    )

    lock_path = snapshot_root / "pnpm-lock.yaml"
    _write_lockfile(
        lock_path,
        _yaml(
            """
            lockfileVersion: '9.0'

            importers:
              z-app:
                dependencies:
                  zed:
                    specifier: 2.0.0
                    version: 2.0.0
                  shared:
                    specifier: 1.0.0
                    version: 1.0.0
                optionalDependencies:
                  shared:
                    specifier: 1.0.0
                    version: 1.0.0
              a-app:
                optionalDependencies:
                  shared:
                    specifier: 1.0.0
                    version: 1.0.0
                devDependencies:
                  shared:
                    specifier: 1.0.0
                    version: 1.0.0

            packages:
              zed@2.0.0:
                resolution:
                  integrity: sha512-emVk
              shared@1.0.0:
                resolution:
                  integrity: sha512-c2hhcmVk

            snapshots:
              zed@2.0.0: {}
              shared@1.0.0:
                optionalDependencies:
                  equal-edge: 3.0.0
                dependencies:
                  equal-edge: 3.0.0
            """
        ),
    )

    lock_result = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    shared_snapshot_key = "shared@1.0.0"
    _assert_facts_response(
        lock_result,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path=shared_snapshot_key,
                dependencies=[
                    {
                        "dependencyKey": "equal-edge",
                        "reference": "3.0.0",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": "equal-edge",
                        "reference": "3.0.0",
                        "section": "optionalDependencies",
                    },
                ],
                name="shared",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="1.0.0",
            ),
            _snapshot_fact(
                dependency_path="zed@2.0.0",
                dependencies=[],
                name="zed",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="2.0.0",
            ),
            _importer_fact(
                dependency_key="shared",
                importer_id="a-app",
                raw_specifier="1.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="1.0.0",
                    name="shared",
                ),
                resolved_reference="1.0.0",
                section="devDependencies",
                snapshot_key=shared_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="shared",
                importer_id="a-app",
                raw_specifier="1.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="1.0.0",
                    name="shared",
                ),
                resolved_reference="1.0.0",
                section="optionalDependencies",
                snapshot_key=shared_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="shared",
                importer_id="z-app",
                raw_specifier="1.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="1.0.0",
                    name="shared",
                ),
                resolved_reference="1.0.0",
                section="dependencies",
                snapshot_key=shared_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="zed",
                importer_id="z-app",
                raw_specifier="2.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="2.0.0",
                    name="zed",
                ),
                resolved_reference="2.0.0",
                section="dependencies",
                snapshot_key="zed@2.0.0",
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="shared",
                importer_id="z-app",
                raw_specifier="1.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="1.0.0",
                    name="shared",
                ),
                resolved_reference="1.0.0",
                section="optionalDependencies",
                snapshot_key=shared_snapshot_key,
                workspace_selector=None,
            ),
        ],
    )


def test_node_workspace_accepts_null_top_level_catalogs_from_official_reader(
    tmp_path: Path,
) -> None:
    """Treat official null catalog containers as an empty workspace catalog."""
    snapshot_root = tmp_path / "snapshot"
    workspace_path = snapshot_root / "pnpm-workspace.yaml"
    _write_lockfile(
        workspace_path,
        _yaml(
            """
            packages: []
            catalog: null
            catalogs: null
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=workspace_path,
        graph="pnpm-workspace-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[],
    )


def test_node_pnpm_protocol_projects_git_file_and_workspace_resolutions(
    tmp_path: Path,
) -> None:
    """Project typed Git, directory, named-workspace, and range facts."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "configuration" / "pnpm-lock.yaml"
    git_reference = (
        "git+https://example.invalid/acme/repository.git"
        "#0123456789abcdef0123456789abcdef01234567"
    )
    git_snapshot_key = f"git-dependency@{git_reference}"
    local_reference = "file:../vendor/local-dependency"
    local_snapshot_key = f"local-dependency@{local_reference}"
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers:
              .:
                dependencies:
                  ranged-workspace:
                    specifier: workspace:^2.0.0
                    version: link:../ranged-workspace
                  local-dependency:
                    specifier: {local_reference}
                    version: {local_reference}
                  named-workspace:
                    specifier: workspace:shared-package@*
                    version: link:../shared-package
                  git-dependency:
                    specifier: '{git_reference}'
                    version: '{git_reference}'

            packages:
              '{local_snapshot_key}':
                resolution:
                  type: directory
                  directory: ../vendor/local-dependency
              '{git_snapshot_key}':
                resolution:
                  type: git
                  repo: https://example.invalid/acme/repository.git
                  commit: 0123456789abcdef0123456789abcdef01234567
                  path: packages/library

            snapshots:
              '{local_snapshot_key}': {{}}
              '{git_snapshot_key}': {{}}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path=git_snapshot_key,
                dependencies=[],
                name="git-dependency",
                non_semver_version=git_reference,
                resolution={
                    "commit": "0123456789abcdef0123456789abcdef01234567",
                    "kind": "git",
                    "path": "packages/library",
                    "repo": "https://example.invalid/acme/repository.git",
                },
                version=None,
            ),
            _snapshot_fact(
                dependency_path=local_snapshot_key,
                dependencies=[],
                name="local-dependency",
                non_semver_version=local_reference,
                resolution={
                    "kind": "directory",
                    "localPath": "vendor/local-dependency",
                },
                version=None,
            ),
            _importer_fact(
                dependency_key="git-dependency",
                importer_id=".",
                raw_specifier=git_reference,
                registry_spec=None,
                resolved_reference=git_reference,
                section="dependencies",
                snapshot_key=git_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="local-dependency",
                importer_id=".",
                raw_specifier=local_reference,
                registry_spec=None,
                resolved_reference=local_reference,
                section="dependencies",
                snapshot_key=local_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="named-workspace",
                importer_id=".",
                raw_specifier="workspace:shared-package@*",
                registry_spec=_registry_spec(
                    fetch_spec="*",
                    name="shared-package",
                    specifier_type="range",
                ),
                resolved_reference="link:../shared-package",
                section="dependencies",
                snapshot_key=None,
                workspace_selector="*",
            ),
            _importer_fact(
                dependency_key="ranged-workspace",
                importer_id=".",
                raw_specifier="workspace:^2.0.0",
                registry_spec=_registry_spec(
                    fetch_spec=">=2.0.0 <3.0.0-0",
                    name="ranged-workspace",
                    specifier_type="range",
                ),
                resolved_reference="link:../ranged-workspace",
                section="dependencies",
                snapshot_key=None,
                workspace_selector="^2.0.0",
            ),
        ],
    )


@pytest.mark.parametrize(
    ("resolved_reference", "resolution_yaml", "expected_resolution"),
    [
        pytest.param(
            "file:../vendor/dependency",
            """
            type: directory
            directory: ../vendor/dependency
            """,
            {
                "kind": "directory",
                "localPath": "vendor/dependency",
            },
            id="directory",
        ),
        pytest.param(
            "file:../vendor/dependency.tgz",
            """
            tarball: file:../vendor/dependency.tgz
            """,
            {
                "kind": "file-tarball",
                "localPath": "vendor/dependency.tgz",
            },
            id="file-tarball",
        ),
    ],
)
def test_node_pnpm_trusts_typed_snapshot_independently_of_raw_specifier(
    tmp_path: Path,
    resolved_reference: str,
    resolution_yaml: str,
    expected_resolution: dict[str, str],
) -> None:
    """Do not add handwritten consistency checks beside the lock graph."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "configuration" / "pnpm-lock.yaml"
    raw_specifier = (
        "git+https://example.invalid/unrelated/repository.git"
        "#0123456789abcdef0123456789abcdef01234567"
    )
    snapshot_key = f"dependency@{resolved_reference}"
    resolution_block = textwrap.indent(
        textwrap.dedent(resolution_yaml).strip(),
        " " * 18,
    )
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers:
              .:
                dependencies:
                  dependency:
                    specifier: '{raw_specifier}'
                    version: {resolved_reference}

            packages:
              '{snapshot_key}':
                resolution:
{resolution_block}

            snapshots:
              '{snapshot_key}': {{}}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path=snapshot_key,
                dependencies=[],
                name="dependency",
                non_semver_version=resolved_reference,
                resolution=expected_resolution,
                version=None,
            ),
            _importer_fact(
                dependency_key="dependency",
                importer_id=".",
                raw_specifier=raw_specifier,
                registry_spec=None,
                resolved_reference=resolved_reference,
                section="dependencies",
                snapshot_key=snapshot_key,
                workspace_selector=None,
            ),
        ],
    )


def test_node_pnpm_protocol_projects_typed_hosted_git_tarball_resolution(
    tmp_path: Path,
) -> None:
    """Consume the official reader's gitHosted marker without URL parsing."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    commit = "0123456789abcdef0123456789abcdef01234567"
    git_reference = f"git+https://github.com/acme/repository.git#{commit}"
    snapshot_key = f"git-dependency@{git_reference}"
    tarball = f"https://codeload.github.com/acme/repository/tar.gz/{commit}"
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers:
              .:
                dependencies:
                  git-dependency:
                    specifier: '{git_reference}'
                    version: '{snapshot_key}'

            packages:
              '{snapshot_key}':
                resolution:
                  tarball: '{tarball}'
                  path: packages/library

            snapshots:
              '{snapshot_key}': {{}}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path=snapshot_key,
                dependencies=[],
                name="git-dependency",
                non_semver_version=git_reference,
                resolution={
                    "kind": "hosted-git",
                    "path": "packages/library",
                    "tarball": tarball,
                },
                version=None,
            ),
            _importer_fact(
                dependency_key="git-dependency",
                importer_id=".",
                raw_specifier=git_reference,
                registry_spec=None,
                resolved_reference=snapshot_key,
                section="dependencies",
                snapshot_key=snapshot_key,
                workspace_selector=None,
            ),
        ],
    )


@pytest.mark.parametrize(
    "document_kind",
    [
        pytest.param("environment-only", id="environment-only"),
        pytest.param("combined", id="combined-environment-and-main"),
    ],
)
def test_node_pnpm_protocol_rejects_environment_documents_without_partial_facts(
    tmp_path: Path,
    document_kind: str,
) -> None:
    """Reject env-only and combined lockfiles without emitting partial facts."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    environment_document = _yaml(
        """
        lockfileVersion: '9.0'
        importers:
          .:
            configDependencies: {}
        packages: {}
        snapshots: {}
        """
    )
    if document_kind == "environment-only":
        content = f"---\n{environment_document}"
    else:
        content = f"---\n{environment_document}\n---\n{_minimal_v9_lockfile()}"
    _write_lockfile(lock_path, content, bom_count=1)

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    response = _assert_error_response(
        completed,
        graph="pnpm-lock-v1",
        error_kind="unsupported-projection",
        packages=_PNPM_LOCK_PACKAGES,
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }


def test_node_pnpm_protocol_does_not_resolve_registry_git_file_or_tar_references(  # noqa: E501
    tmp_path: Path,
) -> None:
    """Prove selected references are projected without network or fallback."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "project" / "pnpm-lock.yaml"
    git_reference = (
        "git+https://network-must-not-run.invalid/acme/repository.git"
        "#fedcba9876543210fedcba9876543210fedcba98"
    )
    directory_reference = "file:../missing/directory"
    tar_reference = "file:../missing/archive.tgz"
    snapshot_keys = {
        "directory-dependency": (f"directory-dependency@{directory_reference}"),
        "git-dependency": f"git-dependency@{git_reference}",
        "registry-dependency": "registry-dependency@5.6.7",
        "tar-dependency": f"tar-dependency@{tar_reference}",
    }
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers:
              .:
                dependencies:
                  tar-dependency:
                    specifier: {tar_reference}
                    version: {tar_reference}
                  registry-dependency:
                    specifier: 5.6.7
                    version: 5.6.7
                  git-dependency:
                    specifier: '{git_reference}'
                    version: '{git_reference}'
                  directory-dependency:
                    specifier: {directory_reference}
                    version: {directory_reference}

            packages:
              '{snapshot_keys["tar-dependency"]}':
                resolution:
                  tarball: {tar_reference}
              '{snapshot_keys["registry-dependency"]}':
                resolution:
                  integrity: sha512-bmV0d29yaw==
              '{snapshot_keys["git-dependency"]}':
                resolution:
                  type: git
                  repo: https://network-must-not-run.invalid/acme/repository.git
                  commit: fedcba9876543210fedcba9876543210fedcba98
              '{snapshot_keys["directory-dependency"]}':
                resolution:
                  type: directory
                  directory: ../missing/directory

            snapshots:
              '{snapshot_keys["tar-dependency"]}': {{}}
              '{snapshot_keys["registry-dependency"]}': {{}}
              '{snapshot_keys["git-dependency"]}': {{}}
              '{snapshot_keys["directory-dependency"]}': {{}}
            """
        ),
    )

    guard_path = tmp_path / "network_guard.cjs"
    guard_loaded_path = tmp_path / "network-guard-loaded"
    attempts_path = tmp_path / "forbidden-network-attempts"
    guard_path.write_text(
        textwrap.dedent(
            """
            const fs = require('node:fs');
            fs.writeFileSync(
              process.env.WDV3_NETWORK_GUARD_LOADED,
              'loaded\\n',
              { flag: 'wx' },
            );
            const attempts = process.env.WDV3_NETWORK_GUARD_ATTEMPTS;
            function blocked(operation) {
              return function forbiddenOperation() {
                fs.appendFileSync(attempts, `${operation}\\n`);
                throw new Error(`forbidden external operation: ${operation}`);
              };
            }
            const dns = require('node:dns');
            for (const name of [
              'lookup', 'resolve', 'resolve4', 'resolve6', 'resolveAny',
              'resolveCaa', 'resolveCname', 'resolveMx', 'resolveNaptr',
              'resolveNs', 'resolvePtr', 'resolveSoa', 'resolveSrv',
              'resolveTxt', 'reverse',
            ]) {
              dns[name] = blocked(`dns.${name}`);
              if (typeof dns.promises?.[name] === 'function') {
                dns.promises[name] = blocked(`dns.promises.${name}`);
              }
            }
            const http = require('node:http');
            http.get = blocked('http.get');
            http.request = blocked('http.request');
            const https = require('node:https');
            https.get = blocked('https.get');
            https.request = blocked('https.request');
            const net = require('node:net');
            net.connect = blocked('net.connect');
            net.createConnection = blocked('net.createConnection');
            net.Socket.prototype.connect = blocked('net.Socket.connect');
            const tls = require('node:tls');
            tls.connect = blocked('tls.connect');
            const childProcess = require('node:child_process');
            for (const name of [
              'exec', 'execFile', 'fork', 'spawn', 'execSync',
              'execFileSync', 'spawnSync',
            ]) {
              childProcess[name] = blocked(`child_process.${name}`);
            }
            globalThis.fetch = blocked('fetch');
            """
        ).lstrip(),
        encoding="utf-8",
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
        home=tmp_path / "uncontrolled-home",
        extra_environment={
            "HTTP_PROXY": "http://127.0.0.1:1",
            "HTTPS_PROXY": "http://127.0.0.1:1",
            "NODE_OPTIONS": f"--require={guard_path}",
            "WDV3_NETWORK_GUARD_ATTEMPTS": str(attempts_path),
            "WDV3_NETWORK_GUARD_LOADED": str(guard_loaded_path),
            "npm_config_offline": "true",
            "npm_config_registry": ("https://network-must-not-run.invalid/"),
        },
    )

    directory_snapshot_key = snapshot_keys["directory-dependency"]
    git_snapshot_key = snapshot_keys["git-dependency"]
    registry_snapshot_key = snapshot_keys["registry-dependency"]
    tar_snapshot_key = snapshot_keys["tar-dependency"]
    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path=directory_snapshot_key,
                dependencies=[],
                name="directory-dependency",
                non_semver_version=directory_reference,
                resolution={
                    "kind": "directory",
                    "localPath": "missing/directory",
                },
                version=None,
            ),
            _snapshot_fact(
                dependency_path=git_snapshot_key,
                dependencies=[],
                name="git-dependency",
                non_semver_version=git_reference,
                resolution={
                    "commit": ("fedcba9876543210fedcba9876543210fedcba98"),
                    "kind": "git",
                    "path": None,
                    "repo": (
                        "https://network-must-not-run.invalid/"
                        "acme/repository.git"
                    ),
                },
                version=None,
            ),
            _snapshot_fact(
                dependency_path=registry_snapshot_key,
                dependencies=[],
                name="registry-dependency",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="5.6.7",
            ),
            _snapshot_fact(
                dependency_path=tar_snapshot_key,
                dependencies=[],
                name="tar-dependency",
                non_semver_version=tar_reference,
                resolution={
                    "kind": "file-tarball",
                    "localPath": "missing/archive.tgz",
                },
                version=None,
            ),
            _importer_fact(
                dependency_key="directory-dependency",
                importer_id=".",
                raw_specifier=directory_reference,
                registry_spec=None,
                resolved_reference=directory_reference,
                section="dependencies",
                snapshot_key=directory_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="git-dependency",
                importer_id=".",
                raw_specifier=git_reference,
                registry_spec=None,
                resolved_reference=git_reference,
                section="dependencies",
                snapshot_key=git_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="registry-dependency",
                importer_id=".",
                raw_specifier="5.6.7",
                registry_spec=_registry_spec(
                    fetch_spec="5.6.7",
                    name="registry-dependency",
                ),
                resolved_reference="5.6.7",
                section="dependencies",
                snapshot_key=registry_snapshot_key,
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="tar-dependency",
                importer_id=".",
                raw_specifier=tar_reference,
                registry_spec=None,
                resolved_reference=tar_reference,
                section="dependencies",
                snapshot_key=tar_snapshot_key,
                workspace_selector=None,
            ),
        ],
    )
    assert guard_loaded_path.read_bytes() == b"loaded\n"
    assert not attempts_path.exists()
    assert not (snapshot_root / "missing").exists()


from base64 import b64encode  # noqa: E402, I001


_NUGET_AUTHORITY = (
    _REPOSITORY_ROOT
    / "artifacts"
    / "workflow-delivery-v3"
    / "static-reference"
    / "nuget-authority"
    / "WorkflowDeliveryV3NuGetAuthority.dll"
)
_NUGET_REQUEST_SCHEMA = (
    "workflow-delivery/v3/static-reference-nuget-authority-request"
)
_NUGET_RESPONSE_SCHEMA = (
    "workflow-delivery/v3/static-reference-nuget-authority-response"
)
_NUGET_GRAPH = "nuget-lock-v1"
_NUGET_IMPLEMENTATION_IDENTITIES = [
    "NuGet.Packaging@7.9.0",
    "NuGet.ProjectModel@7.9.0",
    "dotnet-runtime@10.0.8",
]
_DOTNET_BINARY = shutil.which("dotnet")
_DOTNET_RUNTIME_UNAVAILABLE = "the prepared .NET runtime is unavailable"
if _DOTNET_BINARY is None:
    raise RuntimeError(_DOTNET_RUNTIME_UNAVAILABLE)
_MISSING_NUGET_MODEL_VERSION = object()


@pytest.fixture(scope="module", autouse=True)
def _require_current_authority_closure() -> None:
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    policy.validate_static_reference_dependency_closures(_REPOSITORY_ROOT)


def _run_nuget_authority(
    *,
    content: bytes,
    family: str,
    logical_path: str,
    cwd: Path | None = None,
) -> subprocess.CompletedProcess[str]:
    request = {
        "contentBase64": b64encode(content).decode("ascii"),
        "family": family,
        "logicalPath": logical_path,
        "schema": _NUGET_REQUEST_SCHEMA,
    }
    return subprocess.run(  # noqa: S603
        [_DOTNET_BINARY, str(_NUGET_AUTHORITY)],
        cwd=cwd or _REPOSITORY_ROOT,
        input=json.dumps(request, separators=(",", ":")),
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )


def _assert_nuget_facts_response(
    completed: subprocess.CompletedProcess[str],
    *,
    facts: list[dict[str, Any]],
) -> dict[str, Any]:
    expected = {
        "schema": _NUGET_RESPONSE_SCHEMA,
        "result": "facts",
        "graph": _NUGET_GRAPH,
        "implementationIdentities": _NUGET_IMPLEMENTATION_IDENTITIES,
        "facts": facts,
    }
    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == json.dumps(
        expected,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    actual = json.loads(completed.stdout)
    assert actual == expected
    return actual


def _assert_nuget_error_response(
    completed: subprocess.CompletedProcess[str],
    *,
    error_kind: str,
) -> dict[str, Any]:
    expected = {
        "schema": _NUGET_RESPONSE_SCHEMA,
        "result": "error",
        "graph": _NUGET_GRAPH,
        "implementationIdentities": _NUGET_IMPLEMENTATION_IDENTITIES,
        "errorKind": error_kind,
    }
    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == json.dumps(
        expected,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    actual = json.loads(completed.stdout)
    assert actual == expected
    return actual


def _nuget_json_bytes(document: dict[str, Any]) -> bytes:
    return json.dumps(
        document,
        ensure_ascii=False,
        separators=(",", ":"),
    ).encode()


def _nuget_lock_dependency_fact(  # noqa: PLR0913
    *,
    target: str,
    package_id: str,
    dependency_type: str,
    requested_range: str | None,
    resolved_version: str | None,
    dependencies: list[dict[str, str | None]],
) -> dict[str, Any]:
    return {
        "kind": "nuget-lock-dependency",
        "target": target,
        "id": package_id,
        "dependencyType": dependency_type,
        "requestedRange": requested_range,
        "resolvedVersion": resolved_version,
        "dependencies": dependencies,
    }


@pytest.mark.parametrize(
    ("model_version", "expected_behavior"),
    [
        pytest.param(1, "facts", id="integer-v1"),
        pytest.param(2, "facts", id="integer-v2"),
        pytest.param(3, "empty", id="integer-v3"),
        pytest.param("2", "facts", id="numeric-string-coerces-to-v2"),
        pytest.param(True, "facts", id="boolean-coerces-to-v1"),
        pytest.param(2.51, "empty", id="fraction-coerces-to-v3"),
        pytest.param(
            _MISSING_NUGET_MODEL_VERSION,
            "rejected",
            id="missing-version",
        ),
        pytest.param(
            "not-a-model-version",
            "rejected",
            id="unconvertible-version",
        ),
        pytest.param(
            -2_147_483_648,
            "rejected",
            id="int-min-value",
        ),
        pytest.param(0, "rejected", id="below-admitted-range"),
        pytest.param(4, "rejected", id="above-admitted-range"),
    ],
)
def test_nuget_lock_protocol_projects_versions_and_documented_coercions(
    model_version: Any,
    expected_behavior: str,
) -> None:
    """Pin NuGet 7.9 model admission, coercion, and normalization."""
    document: dict[str, Any] = {
        "dependencies": {
            "net8.0": {
                "Pinned.Model": {
                    "type": "Direct",
                    "requested": "1.2",
                    "resolved": "1.2.3.0",
                    "contentHash": "ignored-content-hash",
                    "dependencies": {
                        "Child.Package": "[2.0, 3.0)",
                    },
                }
            }
        }
    }
    if model_version is not _MISSING_NUGET_MODEL_VERSION:
        document["version"] = model_version

    completed = _run_nuget_authority(
        content=_nuget_json_bytes(document),
        family="nuget-lock",
        logical_path="repository/virtual/packages.lock.json",
    )

    if expected_behavior == "facts":
        _assert_nuget_facts_response(
            completed,
            facts=[
                _nuget_lock_dependency_fact(
                    target="net8.0",
                    package_id="Pinned.Model",
                    dependency_type="Direct",
                    requested_range="[1.2.0, )",
                    resolved_version="1.2.3",
                    dependencies=[
                        {
                            "id": "Child.Package",
                            "requestedRange": "[2.0.0, 3.0.0)",
                        }
                    ],
                )
            ],
        )
    elif expected_behavior == "empty":
        _assert_nuget_facts_response(completed, facts=[])
    else:
        assert expected_behavior == "rejected"
        response = _assert_nuget_error_response(
            completed,
            error_kind="authority-rejected",
        )
        assert set(response) == {
            "errorKind",
            "graph",
            "implementationIdentities",
            "result",
            "schema",
        }


def test_nuget_lock_protocol_orders_targets_dependencies_edges_and_selected_fields(  # noqa: E501
) -> None:
    """Order selected levels and reject projection without partial facts."""
    content = _nuget_json_bytes(
        {
            "version": 2,
            "workflowDeliverySentinel": ["ignored", 17],
            "dependencies": {
                "net9.0": {
                    "zeta": {
                        "type": "Transitive",
                        "resolved": "4.5",
                        "contentHash": "ignored-content-hash",
                        "workflowDeliverySentinel": {"ignored": True},
                        "dependencies": {
                            "zEdge": "[3.0,4.0)",
                            "alpha": "2.0",
                            "ALPHA": "1.0",
                        },
                    },
                    "Beta": {
                        "type": "Direct",
                        "requested": "(, 7.0]",
                        "resolved": "7.0.0",
                    },
                    "alpha": {
                        "type": "Project",
                        "requested": "[2.0.0]",
                        "resolved": "2.0.0",
                    },
                    "ALPHA": {
                        "type": "CentralTransitive",
                        "requested": "1.*",
                        "resolved": "1.9.3",
                    },
                },
                "net10.0": {
                    "delta": {
                        "type": "Direct",
                        "requested": "[0.1, 0.2)",
                        "resolved": "0.1.5",
                        "dependencies": {
                            "Beta": "3.0",
                            "aardvark": "[1.0, )",
                        },
                    }
                },
            },
        }
    )

    completed = _run_nuget_authority(
        content=content,
        family="nuget-lock",
        logical_path="repository/virtual/packages.lock.json",
    )

    actual = _assert_nuget_facts_response(
        completed,
        facts=[
            _nuget_lock_dependency_fact(
                target="net10.0",
                package_id="delta",
                dependency_type="Direct",
                requested_range="[0.1.0, 0.2.0)",
                resolved_version="0.1.5",
                dependencies=[
                    {
                        "id": "aardvark",
                        "requestedRange": "[1.0.0, )",
                    },
                    {
                        "id": "Beta",
                        "requestedRange": "[3.0.0, )",
                    },
                ],
            ),
            _nuget_lock_dependency_fact(
                target="net9.0",
                package_id="ALPHA",
                dependency_type="CentralTransitive",
                requested_range="[1.*, )",
                resolved_version="1.9.3",
                dependencies=[],
            ),
            _nuget_lock_dependency_fact(
                target="net9.0",
                package_id="alpha",
                dependency_type="Project",
                requested_range="[2.0.0, 2.0.0]",
                resolved_version="2.0.0",
                dependencies=[],
            ),
            _nuget_lock_dependency_fact(
                target="net9.0",
                package_id="Beta",
                dependency_type="Direct",
                requested_range="(, 7.0.0]",
                resolved_version="7.0.0",
                dependencies=[],
            ),
            _nuget_lock_dependency_fact(
                target="net9.0",
                package_id="zeta",
                dependency_type="Transitive",
                requested_range=None,
                resolved_version="4.5.0",
                dependencies=[
                    {
                        "id": "ALPHA",
                        "requestedRange": "[1.0.0, )",
                    },
                    {
                        "id": "alpha",
                        "requestedRange": "[2.0.0, )",
                    },
                    {
                        "id": "zEdge",
                        "requestedRange": "[3.0.0, 4.0.0)",
                    },
                ],
            ),
        ],
    )
    assert [(fact["target"], fact["id"]) for fact in actual["facts"]] == [
        ("net10.0", "delta"),
        ("net9.0", "ALPHA"),
        ("net9.0", "alpha"),
        ("net9.0", "Beta"),
        ("net9.0", "zeta"),
    ]
    assert [edge["id"] for edge in actual["facts"][-1]["dependencies"]] == [
        "ALPHA",
        "alpha",
        "zEdge",
    ]
    assert "contentHash" not in completed.stdout
    assert "workflowDeliverySentinel" not in completed.stdout

    invalid_after_valid = _run_nuget_authority(
        content=_nuget_json_bytes(
            {
                "version": 2,
                "dependencies": {
                    "net8.0": {
                        "Valid.Before.Error": {
                            "type": "Direct",
                            "resolved": "1.0.0",
                        }
                    },
                    "net9.0": {
                        "": {
                            "type": "Transitive",
                            "resolved": "2.0.0",
                        }
                    },
                },
            }
        ),
        family="nuget-lock",
        logical_path="repository/virtual/packages.lock.json",
    )
    response = _assert_nuget_error_response(
        invalid_after_valid,
        error_kind="unsupported-projection",
    )
    assert "facts" not in response


def test_nuget_lock_protocol_uses_stream_logger_and_logical_path_behavior(
    tmp_path: Path,
) -> None:
    """Use request bytes with a non-host logical path and a silent logger."""
    execution_directory = tmp_path / "unrelated-execution-directory"
    execution_directory.mkdir()
    logical_path = "repository/virtual/packages.lock.json"
    logical_host_path = execution_directory / logical_path
    assert not logical_host_path.exists()

    conflicting_content = _nuget_json_bytes(
        {
            "version": 4,
            "dependencies": {
                "net8.0": {
                    "Host.Fallback": {
                        "type": "Direct",
                        "resolved": "9.9.9",
                    }
                }
            },
        }
    )
    conflicting_path = execution_directory / "packages.lock.json"
    conflicting_path.write_bytes(conflicting_content)
    request_content = _nuget_json_bytes(
        {
            "version": 2,
            "dependencies": {
                "net8.0": {
                    "Request.Owned": {
                        "type": "Direct",
                        "requested": "5.6",
                        "resolved": "5.6.7.0",
                    }
                }
            },
        }
    )

    completed = _run_nuget_authority(
        content=request_content,
        family="nuget-lock",
        logical_path=logical_path,
        cwd=execution_directory,
    )

    _assert_nuget_facts_response(
        completed,
        facts=[
            _nuget_lock_dependency_fact(
                target="net8.0",
                package_id="Request.Owned",
                dependency_type="Direct",
                requested_range="[5.6.0, )",
                resolved_version="5.6.7",
                dependencies=[],
            )
        ],
    )
    assert not logical_host_path.exists()
    assert conflicting_path.read_bytes() == conflicting_content

    invalid_utf8 = _run_nuget_authority(
        content=b'{"version":2,"dependencies":{}}\xff',
        family="nuget-lock",
        logical_path=logical_path,
        cwd=execution_directory,
    )
    response = _assert_nuget_error_response(
        invalid_utf8,
        error_kind="encoding-rejected",
    )
    assert "facts" not in response


@pytest.mark.parametrize(
    ("family", "logical_path", "input_mode", "content", "expected_facts"),
    [
        pytest.param(
            "nuget-lock",
            r"literal\component/packages.lock.json",
            "strict-utf8-byte-stream",
            _nuget_json_bytes(
                {
                    "version": 2,
                    "dependencies": {
                        "net8.0": {
                            "Backslash.Lock": {
                                "type": "Direct",
                                "resolved": "4.5.6.0",
                            }
                        }
                    },
                }
            ),
            [
                _nuget_lock_dependency_fact(
                    target="net8.0",
                    package_id="Backslash.Lock",
                    dependency_type="Direct",
                    requested_range=None,
                    resolved_version="4.5.6",
                    dependencies=[],
                )
            ],
            id="nuget-lock",
        ),
        pytest.param(
            "nuget-packages-config",
            r"literal\component/packages.config",
            "xml-byte-stream",
            (
                b"<packages>"
                b'<package id="Backslash.Config" version="1.2.3.0" />'
                b"</packages>"
            ),
            [
                {
                    "kind": "nuget-packages-config-entry",
                    "id": "Backslash.Config",
                    "version": "1.2.3",
                }
            ],
            id="nuget-packages-config",
        ),
    ],
)
def test_nuget_authority_accepts_posix_backslash_logical_components(  # noqa: PLR0913, PLR0917
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    family: str,
    logical_path: str,
    input_mode: str,
    content: bytes,
    expected_facts: list[dict[str, Any]],
) -> None:
    """Preserve POSIX component data through the prepared NuGet authority."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )
    source_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    real_subprocess_run = authority.subprocess.run
    serialized_requests: list[bytes] = []

    def run_process(
        command: tuple[str, ...],
        **kwargs: Any,
    ) -> subprocess.CompletedProcess[bytes]:
        serialized_request = kwargs.get("input")
        assert isinstance(serialized_request, bytes)
        serialized_requests.append(serialized_request)
        return real_subprocess_run(command, **kwargs)

    monkeypatch.setattr(authority.subprocess, "run", run_process)
    candidate = source_module.StaticReferenceCandidate(
        path=logical_path,
        selection=source_module.StaticReferenceSelection(
            family=family,
            graph_id="nuget-lock-v1",
            input_mode=input_mode,
        ),
        content=content,
        source_object=f"test:{family}",
    )

    with session_module.StaticReferenceSession(parent=tmp_path) as session:
        invocation = session.materialize(
            candidate,
            source_kind="index",
            target=None,
        )
        assert invocation.candidate_path is None
        outcome = authority.run_authority_graph(
            _REPOSITORY_ROOT,
            candidate,
            invocation,
            session,
        )

    assert outcome.graph_id == "nuget-lock-v1"
    assert outcome.implementation_identities == tuple(
        _NUGET_IMPLEMENTATION_IDENTITIES
    )
    assert [
        json.loads(request)["logicalPath"] for request in serialized_requests
    ] == [logical_path]
    assert outcome.facts == tuple(expected_facts)
    assert outcome.error_kind is None


def test_nuget_packages_config_protocol_orders_with_package_identity_comparer() -> (  # noqa: E501
    None
):
    """Order package identities and project only normalized ID/version facts."""
    content = textwrap.dedent(
        """\
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          <package
            id="Zulu.Package"
            version="3.0"
            targetFramework="net9.0"
            developmentDependency="true" />
          <package
            id="alpha.package"
            version="1.2.3.0"
            allowedVersions="[1.0,2.0)" />
          <package
            id="Beta.Package"
            version="2.0.0-beta.1+build.5" />
        </packages>
        """
    ).encode()

    completed = _run_nuget_authority(
        content=content,
        family="nuget-packages-config",
        logical_path="repository/virtual/packages.config",
    )

    actual = _assert_nuget_facts_response(
        completed,
        facts=[
            {
                "kind": "nuget-packages-config-entry",
                "id": "alpha.package",
                "version": "1.2.3",
            },
            {
                "kind": "nuget-packages-config-entry",
                "id": "Beta.Package",
                "version": "2.0.0-beta.1",
            },
            {
                "kind": "nuget-packages-config-entry",
                "id": "Zulu.Package",
                "version": "3.0.0",
            },
        ],
    )
    assert [fact["id"] for fact in actual["facts"]] == [
        "alpha.package",
        "Beta.Package",
        "Zulu.Package",
    ]
    assert "targetFramework" not in completed.stdout
    assert "developmentDependency" not in completed.stdout
    assert "allowedVersions" not in completed.stdout


@pytest.mark.parametrize(
    "second_id",
    [
        pytest.param("Duplicate.Package", id="exact-duplicate-id"),
        pytest.param("duplicate.package", id="case-insensitive-duplicate-id"),
    ],
)
def test_nuget_packages_config_protocol_rejects_duplicate_ids_without_partial_facts(  # noqa: E501
    second_id: str,
) -> None:
    """Reject exact and case-only duplicate IDs after a valid entry."""
    content = (
        "<packages>"
        '<package id="Alpha.Before.Duplicate" version="1.0.0" />'
        '<package id="Duplicate.Package" version="2.0.0" />'
        f'<package id="{second_id}" version="3.0.0" />'
        "</packages>"
    ).encode()

    completed = _run_nuget_authority(
        content=content,
        family="nuget-packages-config",
        logical_path="repository/virtual/packages.config",
    )

    response = _assert_nuget_error_response(
        completed,
        error_kind="authority-rejected",
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }


@pytest.mark.parametrize(
    ("content", "expected_behavior"),
    [
        pytest.param(
            (
                '<?xml version="1.0" encoding="utf-16"?>'
                "<packages>"
                '<package id="Reader.Owned" version="1.2.3.0" />'
                "</packages>"
            ).encode("utf-16"),
            "facts",
            id="utf16-reader-owned-parse",
        ),
        pytest.param(
            (
                b"<packages>"
                b'<package id="Valid.Before.Error" version="1.0.0" />'
                b'<package id="Broken" version="2.0.0">'
                b"</packages>"
            ),
            "rejected",
            id="malformed-xml-after-valid-entry",
        ),
        pytest.param(
            (
                b"<packages>"
                b'<package id="Valid.Before.Error" version="1.0.0" />'
                b'<package version="2.0.0" />'
                b"</packages>"
            ),
            "rejected",
            id="missing-id-after-valid-entry",
        ),
        pytest.param(
            (
                b"<packages>"
                b'<package id="Valid.Before.Error" version="1.0.0" />'
                b'<package id="Missing.Version" />'
                b"</packages>"
            ),
            "rejected",
            id="missing-version-after-valid-entry",
        ),
        pytest.param(
            (
                b"<packages>"
                b'<package id="Valid.Before.Error" version="1.0.0" />'
                b'<package id="Invalid.Version" version="not-a-version" />'
                b"</packages>"
            ),
            "rejected",
            id="invalid-version-after-valid-entry",
        ),
    ],
)
def test_nuget_packages_config_protocol_enforces_the_non_tolerant_reader_contract(  # noqa: E501
    content: bytes,
    expected_behavior: str,
) -> None:
    """Pin reader-owned encoding and strict selected-entry failures."""
    completed = _run_nuget_authority(
        content=content,
        family="nuget-packages-config",
        logical_path="repository/virtual/packages.config",
    )

    if expected_behavior == "facts":
        _assert_nuget_facts_response(
            completed,
            facts=[
                {
                    "kind": "nuget-packages-config-entry",
                    "id": "Reader.Owned",
                    "version": "1.2.3",
                }
            ],
        )
    else:
        assert expected_behavior == "rejected"
        response = _assert_nuget_error_response(
            completed,
            error_kind="authority-rejected",
        )
        assert "facts" not in response


@pytest.mark.parametrize(
    ("negative_case", "expected_error"),
    [
        pytest.param(
            "missing-own-specifier",
            "unsupported-projection",
            id="missing-own-specifier",
        ),
        pytest.param(
            "null-importer",
            "authority-rejected",
            id="null-importer",
        ),
        pytest.param(
            "null-importer-section",
            "unsupported-projection",
            id="null-importer-selected-map",
        ),
        pytest.param(
            "non-string-importer-section",
            "unsupported-projection",
            id="non-string-importer-selected-map",
        ),
        pytest.param(
            "null-snapshot",
            "authority-rejected",
            id="null-snapshot",
        ),
        pytest.param(
            "array-snapshot",
            "unsupported-projection",
            id="array-snapshot",
        ),
        pytest.param(
            "null-snapshot-section",
            "unsupported-projection",
            id="null-snapshot-selected-map",
        ),
        pytest.param(
            "array-snapshot-section",
            "unsupported-projection",
            id="array-snapshot-selected-map",
        ),
        pytest.param(
            "non-string-snapshot-section",
            "unsupported-projection",
            id="non-string-snapshot-selected-map",
        ),
        pytest.param(
            "missing-required-snapshot",
            "unsupported-projection",
            id="missing-required-snapshot",
        ),
        pytest.param(
            "non-workspace-link",
            "unsupported-projection",
            id="unexplained-null-link-key",
        ),
        pytest.param(
            "non-workspace-bare-local",
            "unsupported-projection",
            id="non-workspace-bare-local",
        ),
        pytest.param(
            "non-workspace-path-local",
            "unsupported-projection",
            id="non-workspace-path-local",
        ),
        pytest.param(
            "workspace-path",
            "unsupported-projection",
            id="path-form-workspace",
        ),
        pytest.param(
            "conflicted-lock",
            "unsupported-projection",
            id="conflicted-lock",
        ),
    ],
)
def test_node_pnpm_importer_and_snapshot_negatives_fail_closed(  # noqa: C901, PLR0912
    tmp_path: Path,
    negative_case: str,
    expected_error: str,
) -> None:
    """Reject malformed loaded importer/snapshot relations without facts."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    package_sections = _yaml(
        """
        packages:
          alpha@1.0.0:
            resolution:
              integrity: sha512-YWxwaGE=

        snapshots:
          alpha@1.0.0: {}
        """
    )
    importer_entry = _yaml(
        """
        importers:
          .:
            dependencies:
              alpha:
                specifier: 1.0.0
                version: 1.0.0
        """
    )

    if negative_case == "missing-own-specifier":
        importer_entry = _yaml(
            """
            importers:
              .:
                dependencies:
                  alpha:
                    version: 1.0.0
            """
        )
    elif negative_case == "null-importer":
        importer_entry = "importers:\n  .: null\n"
    elif negative_case == "null-importer-section":
        importer_entry = "importers:\n  .:\n    dependencies: null\n"
    elif negative_case == "non-string-importer-section":
        importer_entry = _yaml(
            """
            importers:
              .:
                dependencies:
                  alpha:
                    specifier: 1.0.0
                    version: 17
            """
        )
    elif negative_case == "null-snapshot":
        package_sections = _yaml(
            """
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
            snapshots:
              alpha@1.0.0: null
            """
        )
    elif negative_case == "array-snapshot":
        package_sections = _yaml(
            """
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
            snapshots:
              alpha@1.0.0: []
            """
        )
    elif negative_case == "null-snapshot-section":
        package_sections = _yaml(
            """
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
            snapshots:
              alpha@1.0.0:
                dependencies: null
            """
        )
    elif negative_case == "array-snapshot-section":
        package_sections = _yaml(
            """
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
            snapshots:
              alpha@1.0.0:
                dependencies: []
            """
        )
    elif negative_case == "non-string-snapshot-section":
        package_sections = _yaml(
            """
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
            snapshots:
              alpha@1.0.0:
                dependencies:
                  child: 17
            """
        )
    elif negative_case == "missing-required-snapshot":
        package_sections = "packages: {}\nsnapshots: {}\n"
    elif negative_case in {
        "non-workspace-link",
        "non-workspace-bare-local",
        "non-workspace-path-local",
        "workspace-path",
    }:
        source_spec = {
            "non-workspace-link": "link:../alpha",
            "non-workspace-bare-local": "../alpha",
            "non-workspace-path-local": "./alpha",
            "workspace-path": "workspace:../alpha",
        }[negative_case]
        importer_entry = _yaml(
            f"""
            importers:
              .:
                dependencies:
                  alpha:
                    specifier: {source_spec}
                    version: {source_spec}
            """
        )
        package_sections = "packages: {}\nsnapshots: {}\n"
    elif negative_case == "conflicted-lock":
        content = _yaml(
            """
            lockfileVersion: '9.0'
            <<<<<<< HEAD
            importers:
              .:
                dependencies:
                  alpha:
                    specifier: 1.0.0
                    version: 1.0.0
            =======
            importers:
              .:
                dependencies:
                  beta:
                    specifier: 2.0.0
                    version: 2.0.0
            >>>>>>> topic
            packages:
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=
              beta@2.0.0:
                resolution:
                  integrity: sha512-YmV0YQ==
            snapshots:
              alpha@1.0.0: {}
              beta@2.0.0: {}
            """
        )
        _write_lockfile(lock_path, content)
    if negative_case != "conflicted-lock":
        _write_lockfile(
            lock_path,
            (f"lockfileVersion: '9.0'\n\n{importer_entry}\n{package_sections}"),
        )
    original = lock_path.read_bytes()

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    response = _assert_error_response(
        completed,
        graph="pnpm-lock-v1",
        error_kind=expected_error,
        packages=_PNPM_LOCK_PACKAGES,
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }
    assert "facts" not in response
    assert lock_path.read_bytes() == original


def test_shipped_node_authority_uses_only_exact_pinned_public_calls() -> None:
    """Pin exact public calls/options and forbid fallback authority APIs."""
    import re  # noqa: PLC0415

    source_bytes = _NODE_AUTHORITY.read_bytes()
    source = source_bytes.decode("utf-8", "strict")
    expected_imports = [
        "@npmcli/package-json",
        "npm-package-arg",
        "@pnpm/workspace.workspace-manifest-reader",
        "@pnpm/workspace.spec-parser",
        "@pnpm/resolving.npm-resolver",
        "npm-package-arg",
        "@pnpm/lockfile.fs",
        "@pnpm/lockfile.utils",
        "@pnpm/deps.path",
        "@pnpm/workspace.spec-parser",
        "@pnpm/resolving.npm-resolver",
    ]
    assert source_bytes.startswith(
        b"import { readFile } from 'node:fs/promises';"
    )
    assert re.findall(r"importPackage\('([^']+)'\)", source) == (
        expected_imports
    )
    assert source.count("PackageJson.load(snapshotDirectory)") == 1
    assert source.count("const content = loaded?.content;") == 1
    assert source.count("npa.resolve(name, '*', snapshotDirectory)") == 1
    assert source.count("npa.resolve(name, specifier, snapshotDirectory)") == 1
    assert source.count("readWorkspaceManifest(snapshotDirectory)") == 1
    assert source.count("WorkspaceSpec.parse(rawSpecifier)") == 2  # noqa: PLR2004
    assert source.count("workspacePrefToNpm(rawSpecifier)") == 2  # noqa: PLR2004
    assert source.count("extractMainDocument(comparisonView)") == 1
    assert (
        len(
            re.findall(
                r"\blockfileModule\.readWantedLockfileWithMergeInfo\s*\(",
                source,
            )
        )
        == 1
    )
    assert source.count("nameVerFromPkgSnapshot(dependencyPath, snapshot)") == 1
    assert (
        len(
            re.findall(
                r"\bauthorities\.pkgSnapshotToResolution\s*\(\s*"
                r"dependencyPath\s*,\s*snapshot\s*,\s*"
                r"authorities\.registryContext\s*\)",
                source,
            )
        )
        == 1
    )
    assert source.count("refToRelative(resolvedReference, dependencyKey)") == 1
    assert (
        source.count(
            "parseBareSpecifier(\n        normalizedSpecifier,\n"
            '        dependencyKey,\n        "latest",\n'
            '        "https://registry.npmjs.org/"\n      )'
        )
        == 0
    )
    assert {
        forbidden
        for forbidden in (
            ".normalize(",
            ".prepare(",
            ".fix(",
            "readWorkspaceManifest(snapshotDirectory,",
            "packageIdFromSnapshot",
            "readCurrentLockfile(",
            "readWantedLockfile(",
            "readWantedLockfileAndAutofixConflicts(",
            "readWantedLockfileFile(",
            "@pnpm/lockfile.fs/",
            "@pnpm/lockfile.utils/",
            "@pnpm/deps.path/",
        )
        if forbidden in source
    } == set()


@pytest.mark.parametrize(
    ("dependency_key", "source_spec", "expected_fetch", "expected_selector"),
    [
        pytest.param(
            "producer-alias",
            "workspace:@hcoona/hcoona-release-smoke-npm@*",
            "*",
            "*",
            id="named-workspace-catalog",
        ),
        pytest.param(
            "@hcoona/hcoona-release-smoke-npm",
            "workspace:^2.0.0",
            ">=2.0.0 <3.0.0-0",
            "^2.0.0",
            id="ranged-workspace-catalog",
        ),
    ],
)
def test_node_workspace_catalog_projects_named_and_ranged_workspace_specs(
    tmp_path: Path,
    dependency_key: str,
    source_spec: str,
    expected_fetch: str,
    expected_selector: str,
) -> None:
    """Exercise the exact named/ranged WorkspaceSpec public branch."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "pnpm-workspace.yaml"
    _write_lockfile(
        candidate_path,
        _yaml(
            f"""
            catalog:
              '{dependency_key}': '{source_spec}'
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-workspace-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[
            _catalog_fact(
                catalog_kind="default",
                catalog_name=None,
                dependency_key=dependency_key,
                source_spec=source_spec,
                reference={
                    "kind": "workspace",
                    "workspace": {
                        "fetchSpec": expected_fetch,
                        "name": "@hcoona/hcoona-release-smoke-npm",
                        "selector": expected_selector,
                        "type": "range",
                    },
                },
            )
        ],
    )
    assert candidate_path.read_text(encoding="utf-8") == _yaml(
        f"""
        catalog:
          '{dependency_key}': '{source_spec}'
        """
    )


@pytest.mark.parametrize(
    ("source_spec", "reference"),
    [
        pytest.param(
            "",
            {
                "kind": "npm",
                "npm": _npm_reference(
                    name="ordinary",
                    raw_spec="",
                    reference_type="range",
                ),
            },
            id="empty-npm-specifier",
        ),
        pytest.param(
            "workspace:",
            {
                "kind": "workspace",
                "workspace": {
                    "fetchSpec": "*",
                    "name": "ordinary",
                    "selector": "",
                    "type": "range",
                },
            },
            id="empty-workspace-selector",
        ),
    ],
)
def test_node_workspace_catalog_preserves_official_empty_values(
    tmp_path: Path,
    source_spec: str,
    reference: dict[str, Any],
) -> None:
    """Preserve empty values returned by the selected public APIs."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "pnpm-workspace.yaml"
    _write_lockfile(
        candidate_path,
        _yaml(
            f"""
            catalog:
              ordinary: {json.dumps(source_spec)}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-workspace-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[
            _catalog_fact(
                catalog_kind="default",
                catalog_name=None,
                dependency_key="ordinary",
                reference=reference,
                source_spec=source_spec,
            )
        ],
    )


def test_node_workspace_preserves_empty_named_catalog(
    tmp_path: Path,
) -> None:
    """Preserve every named catalog accepted by the official reader."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "pnpm-workspace.yaml"
    _write_lockfile(
        candidate_path,
        _yaml(
            """
            packages: []
            catalogs:
              "":
                ordinary: 1.2.3
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-workspace-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[
            _catalog_fact(
                catalog_kind="named",
                catalog_name="",
                dependency_key="ordinary",
                reference=_workspace_npm_reference(
                    dependency_key="ordinary",
                    source_spec="1.2.3",
                ),
                source_spec="1.2.3",
            )
        ],
    )


def test_node_pnpm_accepts_canonical_registry_tarball_specifier(
    tmp_path: Path,
) -> None:
    """Use the pinned pnpm registry parser as the importer authority."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    raw_specifier = "https://registry.npmjs.org/is-number/-/is-number-7.0.0.tgz"
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'
            importers:
              .:
                dependencies:
                  is-number:
                    specifier: {raw_specifier}
                    version: 7.0.0
            packages:
              is-number@7.0.0:
                resolution:
                  integrity: sha512-YWJj
            snapshots:
              is-number@7.0.0: {{}}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path="is-number@7.0.0",
                dependencies=[],
                name="is-number",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="7.0.0",
            ),
            _importer_fact(
                dependency_key="is-number",
                importer_id=".",
                raw_specifier=raw_specifier,
                registry_spec=_registry_spec(
                    fetch_spec="7.0.0",
                    name="is-number",
                ),
                resolved_reference="7.0.0",
                section="dependencies",
                snapshot_key="is-number@7.0.0",
                workspace_selector=None,
            ),
        ],
    )


def test_node_pnpm_rejects_null_registry_spec_with_registry_snapshot(
    tmp_path: Path,
) -> None:
    """Do not reinterpret an arbitrary remote URL as a registry spec."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    _write_lockfile(
        lock_path,
        _yaml(
            """
            lockfileVersion: '9.0'
            importers:
              .:
                dependencies:
                  ordinary:
                    specifier: https://example.invalid/ordinary-1.0.0.tgz
                    version: 1.0.0
            packages:
              ordinary@1.0.0:
                resolution:
                  integrity: sha512-YWJj
            snapshots:
              ordinary@1.0.0: {}
            """
        ),
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_error_response(
        completed,
        graph="pnpm-lock-v1",
        error_kind="unsupported-projection",
        packages=_PNPM_LOCK_PACKAGES,
    )


@pytest.mark.parametrize(
    ("graph", "basename"),
    [
        pytest.param("npm-manifest-v1", "package.json", id="npm-manifest"),
        pytest.param("pnpm-lock-v1", "pnpm-lock.yaml", id="pnpm-lock"),
        pytest.param(
            "pnpm-workspace-v1",
            "pnpm-workspace.yaml",
            id="pnpm-workspace",
        ),
    ],
)
def test_node_graphs_reject_malformed_utf8_before_facts(
    tmp_path: Path,
    graph: str,
    basename: str,
) -> None:
    """Reject malformed UTF-8 in all file-oriented Node authority graphs."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / basename
    candidate_path.parent.mkdir(parents=True)
    malformed = b"\xff\xfevalid-looking: true\n"
    candidate_path.write_bytes(malformed)

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph=graph,
    )

    response = _assert_error_response(
        completed,
        graph=graph,
        error_kind="encoding-rejected",
        packages=(),
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }
    assert "facts" not in response
    assert candidate_path.read_bytes() == malformed


@pytest.mark.parametrize(
    ("graph", "bom_count", "expected_behavior"),
    [
        pytest.param("npm-manifest-v1", 0, "facts", id="npm-zero-bom"),
        pytest.param("npm-manifest-v1", 1, "facts", id="npm-one-bom"),
        pytest.param(
            "npm-manifest-v1",
            2,
            "authority-rejected",
            id="npm-two-bom",
        ),
        pytest.param(
            "pnpm-workspace-v1",
            0,
            "facts",
            id="workspace-zero-bom",
        ),
        pytest.param(
            "pnpm-workspace-v1",
            1,
            "facts",
            id="workspace-one-bom",
        ),
        pytest.param(
            "pnpm-workspace-v1",
            2,
            "facts",
            id="workspace-two-bom",
        ),
        pytest.param(
            "pnpm-workspace-v1",
            3,
            "empty-facts",
            id="workspace-three-bom",
        ),
    ],
)
def test_node_npm_and_workspace_bom_outcomes_are_reader_owned_and_exact(
    tmp_path: Path,
    graph: str,
    bom_count: int,
    expected_behavior: str,
) -> None:
    """Pin exact BOM outcomes while preserving each original candidate."""
    snapshot_root = tmp_path / "snapshot"
    if graph == "npm-manifest-v1":
        candidate_path = snapshot_root / "package.json"
        payload = b'{"name":"bom-package"}\n'
        facts = [
            {
                "context": "name",
                "kind": "npm-package-name",
                "name": "bom-package",
            }
        ]
        packages = _NPM_PACKAGES
    else:
        candidate_path = snapshot_root / "pnpm-workspace.yaml"
        payload = b"packages:\n  - packages/bom\n"
        facts = [
            {
                "index": 0,
                "kind": "pnpm-workspace-pattern",
                "pattern": "packages/bom",
            }
        ]
        packages = _PNPM_WORKSPACE_PACKAGES
    candidate_path.parent.mkdir(parents=True)
    original = (_UTF8_BOM * bom_count) + payload
    candidate_path.write_bytes(original)

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph=graph,
    )

    if expected_behavior in {"facts", "empty-facts"}:
        _assert_facts_response(
            completed,
            graph=graph,
            facts=[] if expected_behavior == "empty-facts" else facts,
            packages=packages,
        )
    else:
        response = _assert_error_response(
            completed,
            graph=graph,
            error_kind=expected_behavior,
            packages=packages,
        )
        assert "facts" not in response
    assert candidate_path.read_bytes() == original


@pytest.mark.parametrize(
    "document_case",
    [
        pytest.param("clean-lf", id="clean-lf"),
        pytest.param("clean-crlf", id="clean-crlf"),
        pytest.param("combined-environment-crlf", id="environment-crlf"),
    ],
)
def test_node_pnpm_preserves_original_bytes_digest_and_crlf_admission(
    tmp_path: Path,
    document_case: str,
) -> None:
    """Preserve exact pnpm bytes while comparison-view CRLF rules apply."""
    import hashlib  # noqa: PLC0415

    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "pnpm-lock.yaml"
    canonical = _minimal_v9_lockfile()
    if document_case == "combined-environment-crlf":
        environment_document = _yaml(
            """
            lockfileVersion: '9.0'
            importers:
              .:
                configDependencies: {}
            packages: {}
            snapshots: {}
            """
        )
        text = (f"---\n{environment_document}\n---\n{canonical}").replace(
            "\n", "\r\n"
        )
        bom_count = 1
    elif document_case == "clean-crlf":
        text = canonical.replace("\n", "\r\n")
        bom_count = 2
    else:
        text = canonical
        bom_count = 0
    original = (_UTF8_BOM * bom_count) + text.encode("utf-8")
    expected_digest = hashlib.sha256(original).hexdigest()
    candidate_path.parent.mkdir(parents=True)
    candidate_path.write_bytes(original)
    unrelated_cwd = tmp_path / "unrelated-cwd"
    unrelated_cwd.mkdir()

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="pnpm-lock-v1",
        cwd=unrelated_cwd,
        extra_environment={
            "LANG": "C",
            "LC_ALL": "C",
            "npm_config_registry": "https://must-not-be-used.invalid/",
        },
    )

    if document_case == "combined-environment-crlf":
        response = _assert_error_response(
            completed,
            graph="pnpm-lock-v1",
            error_kind="unsupported-projection",
            packages=_PNPM_LOCK_PACKAGES,
        )
        assert "facts" not in response
    else:
        _assert_facts_response(
            completed,
            graph="pnpm-lock-v1",
            facts=_minimal_v9_facts(),
            packages=_PNPM_LOCK_PACKAGES,
        )
    observed = candidate_path.read_bytes()
    assert observed == original
    assert hashlib.sha256(observed).hexdigest() == expected_digest
    assert not (snapshot_root / "node_modules").exists()


def test_nuget_source_contract_uses_exact_non_writable_public_reader_calls() -> (  # noqa: E501
    None
):
    """Pin selected NuGet APIs without binding source formatting."""
    program_path = (
        _REPOSITORY_ROOT
        / "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs"
    )
    source = program_path.read_text(encoding="utf-8")
    assert (
        source.count("new MemoryStream(request.Content, writable: false)") == 2  # noqa: PLR2004
    )
    assert source.count("ILogger logger = NullLogger.Instance;") == 1
    assert source.count("PackagesLockFileFormat.Read(") == 1
    assert source.count("request.LogicalPath") == 1
    assert source.count("new PackagesConfigReader(") == 1
    assert source.count("leaveStreamOpen: false") == 1
    assert source.count(".GetPackages(allowDuplicatePackageIds: false)") == 1
    assert (
        source.count(
            ".OrderBy(package => package.PackageIdentity, "
            "PackageIdentity.Comparer)"
        )
        == 1
    )
    assert {
        forbidden
        for forbidden in (
            "new FileStream(",
            "new MemoryStream(request.Content, writable: true)",
            "PackagesLockFileFormat.Read(request.",
            "NullLogger.Instance,\n            stream",
            "leaveStreamOpen: true",
            "allowDuplicatePackageIds: true",
            ".GetPackages()",
            "JObject",
            "JsonNode.Parse",
            "XDocument",
        )
        if forbidden in source
    } == set()
    assert program_path.is_file()


def test_node_pnpm_orders_non_ascii_catalogs_and_importers_by_utf8_bytes(
    tmp_path: Path,
) -> None:
    """Order U+E000 before U+10000 in accepted pnpm mapping positions."""
    private_use = "\ue000"
    non_bmp = "\U00010000"
    snapshot_root = tmp_path / "snapshot"
    workspace_path = snapshot_root / "pnpm-workspace.yaml"
    _write_lockfile(
        workspace_path,
        _yaml(
            f"""
            catalogs:
              "{non_bmp}":
                beta: 2.0.0
              "{private_use}":
                alpha: 1.0.0
            """
        ),
    )
    workspace_completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=workspace_path,
        graph="pnpm-workspace-v1",
    )
    workspace_response = _assert_facts_response(
        workspace_completed,
        graph="pnpm-workspace-v1",
        packages=_PNPM_WORKSPACE_PACKAGES,
        facts=[
            _catalog_fact(
                catalog_kind="named",
                catalog_name=private_use,
                dependency_key="alpha",
                reference=_workspace_npm_reference(
                    dependency_key="alpha",
                    source_spec="1.0.0",
                ),
                source_spec="1.0.0",
            ),
            _catalog_fact(
                catalog_kind="named",
                catalog_name=non_bmp,
                dependency_key="beta",
                reference=_workspace_npm_reference(
                    dependency_key="beta",
                    source_spec="2.0.0",
                ),
                source_spec="2.0.0",
            ),
        ],
    )
    assert [fact["catalogName"] for fact in workspace_response["facts"]] == [
        private_use,
        non_bmp,
    ]

    lock_path = snapshot_root / "pnpm-lock.yaml"
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers:
              "{non_bmp}":
                dependencies:
                  beta:
                    specifier: 2.0.0
                    version: 2.0.0
              "{private_use}":
                dependencies:
                  alpha:
                    specifier: 1.0.0
                    version: 1.0.0

            packages:
              beta@2.0.0:
                resolution:
                  integrity: sha512-YmV0YQ==
              alpha@1.0.0:
                resolution:
                  integrity: sha512-YWxwaGE=

            snapshots:
              beta@2.0.0: {{}}
              alpha@1.0.0: {{}}
            """
        ),
    )
    lock_completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )
    lock_response = _assert_facts_response(
        lock_completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path="alpha@1.0.0",
                dependencies=[],
                name="alpha",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="1.0.0",
            ),
            _snapshot_fact(
                dependency_path="beta@2.0.0",
                dependencies=[],
                name="beta",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="2.0.0",
            ),
            _importer_fact(
                dependency_key="alpha",
                importer_id=private_use,
                raw_specifier="1.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="1.0.0",
                    name="alpha",
                ),
                resolved_reference="1.0.0",
                section="dependencies",
                snapshot_key="alpha@1.0.0",
                workspace_selector=None,
            ),
            _importer_fact(
                dependency_key="beta",
                importer_id=non_bmp,
                raw_specifier="2.0.0",
                registry_spec=_registry_spec(
                    fetch_spec="2.0.0",
                    name="beta",
                ),
                resolved_reference="2.0.0",
                section="dependencies",
                snapshot_key="beta@2.0.0",
                workspace_selector=None,
            ),
        ],
    )
    assert [
        fact["importerId"]
        for fact in lock_response["facts"]
        if fact["kind"] == "pnpm-lock-importer-reference"
    ] == [private_use, non_bmp]


def test_nuget_uses_ordinal_and_case_insensitive_ordinal_unicode_ordering() -> (
    None
):
    """Distinguish UTF-16 Ordinal order from UTF-8 order at both levels."""
    private_use = "\ue000"
    non_bmp = "\U00010000"
    dependency_entries = {
        private_use: {
            "type": "Direct",
            "resolved": "4.0.0",
        },
        non_bmp: {
            "type": "Direct",
            "resolved": "3.0.0",
        },
        "alpha": {
            "type": "Direct",
            "resolved": "2.0.0",
        },
        "Alpha": {
            "type": "Direct",
            "resolved": "1.0.0",
            "dependencies": {
                private_use: "4.0.0",
                non_bmp: "3.0.0",
                "alpha": "2.0.0",
                "Alpha": "1.0.0",
            },
        },
    }
    content = _nuget_json_bytes(
        {
            "version": 2,
            "dependencies": {
                "net9.0": {
                    "tail": {
                        "type": "Direct",
                        "resolved": "9.0.0",
                    }
                },
                "net10.0": dependency_entries,
            },
        }
    )

    completed = _run_nuget_authority(
        content=content,
        family="nuget-lock",
        logical_path="unicode/packages.lock.json",
    )

    expected_facts = [
        _nuget_lock_dependency_fact(
            target="net10.0",
            package_id="Alpha",
            dependency_type="Direct",
            requested_range=None,
            resolved_version="1.0.0",
            dependencies=[
                {"id": "Alpha", "requestedRange": "[1.0.0, )"},
                {"id": "alpha", "requestedRange": "[2.0.0, )"},
                {"id": private_use, "requestedRange": "[4.0.0, )"},
                {"id": non_bmp, "requestedRange": "[3.0.0, )"},
            ],
        ),
        _nuget_lock_dependency_fact(
            target="net10.0",
            package_id="alpha",
            dependency_type="Direct",
            requested_range=None,
            resolved_version="2.0.0",
            dependencies=[],
        ),
        _nuget_lock_dependency_fact(
            target="net10.0",
            package_id=private_use,
            dependency_type="Direct",
            requested_range=None,
            resolved_version="4.0.0",
            dependencies=[],
        ),
        _nuget_lock_dependency_fact(
            target="net10.0",
            package_id=non_bmp,
            dependency_type="Direct",
            requested_range=None,
            resolved_version="3.0.0",
            dependencies=[],
        ),
        _nuget_lock_dependency_fact(
            target="net9.0",
            package_id="tail",
            dependency_type="Direct",
            requested_range=None,
            resolved_version="9.0.0",
            dependencies=[],
        ),
    ]
    expected_response = {
        "schema": _NUGET_RESPONSE_SCHEMA,
        "result": "facts",
        "graph": _NUGET_GRAPH,
        "implementationIdentities": _NUGET_IMPLEMENTATION_IDENTITIES,
        "facts": expected_facts,
    }
    expected_raw = (
        json.dumps(
            expected_response,
            ensure_ascii=True,
            separators=(",", ":"),
        )
        .replace("\\ue000", "\\uE000")
        .replace(
            "\\ud800\\udc00",
            "\\uD800\\uDC00",
        )
    )
    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == expected_raw
    response = json.loads(completed.stdout)
    assert response == expected_response
    assert [(fact["target"], fact["id"]) for fact in response["facts"]] == [
        ("net10.0", "Alpha"),
        ("net10.0", "alpha"),
        ("net10.0", private_use),
        ("net10.0", non_bmp),
        ("net9.0", "tail"),
    ]


def test_shipped_node_authority_binds_fixed_parser_arguments_exactly() -> None:
    """Bind fixed parser arguments without binding whitespace or line layout."""
    import re  # noqa: PLC0415

    source = _NODE_AUTHORITY.read_text(encoding="utf-8")

    assert source.count("const REGISTRY = 'https://registry.npmjs.org/';") == 1
    assert source.count("const DEFAULT_TAG = 'latest';") == 1
    assert (
        len(
            re.findall(
                r"\bparseBareSpecifier\s*\(\s*normalizedSpecifier\s*,\s*"
                r"dependencyKey\s*,\s*DEFAULT_TAG\s*,\s*REGISTRY\s*\)",
                source,
            )
        )
        == 2  # noqa: PLR2004
    )
    assert (
        len(
            re.findall(
                r"registriesByScope\s*:\s*\{\s*default\s*:\s*REGISTRY\s*\}",
                source,
            )
        )
        == 1
    )
    assert "process.env.npm_config_registry" not in source
    assert "process.cwd()" not in source


@pytest.mark.parametrize(
    "dependency_key",
    [
        pytest.param("\ue000", id="private-use-U+E000"),
        pytest.param("\U00010000", id="non-bmp-U+10000"),
    ],
)
def test_node_npm_non_ascii_dependency_keys_are_authority_rejected(
    tmp_path: Path,
    dependency_key: str,
) -> None:
    """Record why these code points have no admitted npm key-order case."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "package.json"
    original = _write_package_json(
        candidate_path,
        {
            "name": "unicode-order-host",
            "dependencies": {dependency_key: "1.0.0"},
        },
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    response = _assert_error_response(
        completed,
        graph="npm-manifest-v1",
        error_kind="authority-rejected",
        packages=_NPM_PACKAGES,
    )
    assert "facts" not in response
    assert candidate_path.read_bytes() == original


@pytest.mark.parametrize(
    ("document", "expected_error"),
    [
        pytest.param(
            {
                "name": "workspace-protocol-host",
                "dependencies": {"workspace-reference": "workspace:*"},
            },
            "authority-rejected",
            id="workspace-protocol",
        ),
        pytest.param(
            {
                "name": 17,
                "dependencies": {"must-not-project": "1.0.0"},
            },
            "unsupported-projection",
            id="numeric-selected-name",
        ),
        pytest.param(
            {
                "name": "tilde-local-host",
                "dependencies": {"tilde-local": "~/outside-snapshot"},
            },
            "unsupported-projection",
            id="tilde-local-directory",
        ),
        pytest.param(
            {
                "name": "escaping-directory-host",
                "dependencies": {
                    "escaping-directory": "../../../outside-snapshot"
                },
            },
            "unsupported-projection",
            id="repository-escaping-directory",
        ),
        pytest.param(
            {
                "name": "escaping-tarball-host",
                "dependencies": {
                    "escaping-tarball": "file:../../../outside-snapshot.tgz"
                },
            },
            "unsupported-projection",
            id="repository-escaping-file-tarball",
        ),
    ],
)
def test_node_npm_rejects_unprojectable_selected_values_without_partial_facts(
    tmp_path: Path,
    document: dict[str, Any],
    expected_error: str,
) -> None:
    """Reject selected npm values without facts or candidate byte mutation."""
    snapshot_root = tmp_path / "snapshot"
    candidate_path = snapshot_root / "deep" / "application" / "package.json"
    original = _write_package_json(candidate_path, document)

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=candidate_path,
        graph="npm-manifest-v1",
    )

    response = _assert_error_response(
        completed,
        graph="npm-manifest-v1",
        error_kind=expected_error,
        packages=_NPM_PACKAGES,
    )
    assert set(response) == {
        "errorKind",
        "graph",
        "implementationIdentities",
        "result",
        "schema",
    }
    assert "facts" not in response
    assert candidate_path.read_bytes() == original


def test_pnpm_importer_specifier_reverse_membership_guard_behavior() -> None:
    """Reject a specifier not referenced by any selected importer section."""
    program = f"""
import {{ validateImporterSpecifierMembership }} from {
        json.dumps(_NODE_AUTHORITY.as_uri())
    };
validateImporterSpecifierMembership({{ used: '1.0.0' }}, new Set(['used']));
try {{
  validateImporterSpecifierMembership({{ orphan: '1.0.0' }}, new Set());
  process.exitCode = 2;
}} catch (error) {{
  process.stdout.write(error.kind ?? '');
}}
"""
    completed = subprocess.run(  # noqa: S603
        ("node", "--input-type=module", "--eval", program),  # noqa: S607
        cwd=_REPOSITORY_ROOT,
        check=False,
        capture_output=True,
        text=True,
    )

    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == "unsupported-projection"


def test_node_pnpm_snapshot_dependency_edges_use_utf8_byte_order(
    tmp_path: Path,
) -> None:
    """Order U+E000 before U+10000 for snapshot dependency edge keys."""
    private_use = "\ue000"
    non_bmp = "\U00010000"
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    _write_lockfile(
        lock_path,
        _yaml(
            f"""
            lockfileVersion: '9.0'

            importers: {{}}

            packages:
              host@1.0.0:
                resolution:
                  integrity: sha512-aG9zdA==

            snapshots:
              host@1.0.0:
                dependencies:
                  "{non_bmp}": 2.0.0
                  "{private_use}": 1.0.0
            """
        ),
    )
    original = lock_path.read_bytes()
    assert original.index(non_bmp.encode()) < original.index(
        private_use.encode()
    )

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    response = _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path="host@1.0.0",
                dependencies=[
                    {
                        "dependencyKey": private_use,
                        "reference": "1.0.0",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": non_bmp,
                        "reference": "2.0.0",
                        "section": "dependencies",
                    },
                ],
                name="host",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="1.0.0",
            )
        ],
    )
    edges = response["facts"][0]["dependencies"]
    assert [edge["dependencyKey"] for edge in edges] == [
        private_use,
        non_bmp,
    ]
    assert [edge["dependencyKey"].encode() for edge in edges] == sorted(
        (private_use.encode(), non_bmp.encode())
    )
    assert lock_path.read_bytes() == original


def test_node_pnpm_snapshot_preserves_empty_dependency_edge_strings(
    tmp_path: Path,
) -> None:
    """Preserve exact empty edge strings emitted by the official reader."""
    snapshot_root = tmp_path / "snapshot"
    lock_path = snapshot_root / "pnpm-lock.yaml"
    _write_lockfile(
        lock_path,
        _yaml(
            """
            lockfileVersion: '9.0'

            importers: {}

            packages:
              host@1.0.0:
                resolution:
                  integrity: sha512-aG9zdA==

            snapshots:
              host@1.0.0:
                dependencies:
                  '': ''
                  '@hcoona/hcoona-release-smoke-npm': ''
            """
        ),
    )
    original = lock_path.read_bytes()

    completed = _run_node_authority(
        snapshot_root=snapshot_root,
        candidate_path=lock_path,
        graph="pnpm-lock-v1",
    )

    _assert_facts_response(
        completed,
        graph="pnpm-lock-v1",
        packages=_PNPM_LOCK_PACKAGES,
        facts=[
            _snapshot_fact(
                dependency_path="host@1.0.0",
                dependencies=[
                    {
                        "dependencyKey": "",
                        "reference": "",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": ("@hcoona/hcoona-release-smoke-npm"),
                        "reference": "",
                        "section": "dependencies",
                    },
                ],
                name="host",
                non_semver_version=None,
                resolution={"kind": "registry"},
                version="1.0.0",
            )
        ],
    )
    assert lock_path.read_bytes() == original


def test_nuget_targets_use_ordinal_case_and_unicode_ordering() -> None:
    """Order target case and U+10000 before U+E000 with Ordinal semantics."""
    private_use = "\ue000"
    non_bmp = "\U00010000"
    content = _nuget_json_bytes(
        {
            "version": 2,
            "dependencies": {
                f"net10.0/{private_use}": {
                    "sentinel": {
                        "type": "Direct",
                        "resolved": "4.0.0",
                    }
                },
                "net10.0/a": {
                    "sentinel": {
                        "type": "Direct",
                        "resolved": "2.0.0",
                    }
                },
                f"net10.0/{non_bmp}": {
                    "sentinel": {
                        "type": "Direct",
                        "resolved": "3.0.0",
                    }
                },
                "net10.0/A": {
                    "sentinel": {
                        "type": "Direct",
                        "resolved": "1.0.0",
                    }
                },
            },
        }
    )

    completed = _run_nuget_authority(
        content=content,
        family="nuget-lock",
        logical_path="ordinal-targets/packages.lock.json",
    )

    expected_targets = [
        "net10.0/A",
        "net10.0/a",
        f"net10.0/{non_bmp}",
        f"net10.0/{private_use}",
    ]
    expected_facts = [
        _nuget_lock_dependency_fact(
            target=target,
            package_id="sentinel",
            dependency_type="Direct",
            requested_range=None,
            resolved_version=version,
            dependencies=[],
        )
        for target, version in zip(
            expected_targets,
            ("1.0.0", "2.0.0", "3.0.0", "4.0.0"),
            strict=True,
        )
    ]
    expected_response = {
        "schema": _NUGET_RESPONSE_SCHEMA,
        "result": "facts",
        "graph": _NUGET_GRAPH,
        "implementationIdentities": _NUGET_IMPLEMENTATION_IDENTITIES,
        "facts": expected_facts,
    }
    expected_raw = (
        json.dumps(
            expected_response,
            ensure_ascii=True,
            separators=(",", ":"),
        )
        .replace("\\ue000", "\\uE000")
        .replace("\\ud800\\udc00", "\\uD800\\uDC00")
    )

    assert completed.returncode == 0
    assert completed.stderr == ""
    assert completed.stdout == expected_raw
    response = json.loads(completed.stdout)
    assert response == expected_response
    assert [fact["target"] for fact in response["facts"]] == expected_targets
    assert expected_targets[-2:] == [
        f"net10.0/{non_bmp}",
        f"net10.0/{private_use}",
    ]
    assert sorted(
        expected_targets[-2:],
        key=lambda target: target.encode(),
    ) == [f"net10.0/{private_use}", f"net10.0/{non_bmp}"]
