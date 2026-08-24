"""Contract tests for commit-3 authoring and static catalogs."""

from __future__ import annotations

import json
import subprocess
from collections.abc import Callable
from dataclasses import asdict
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.canonical import JsonValue, canonicalize
from three_workflow_delivery_v3.catalogs import (
    BUILD_DEFINITIONS,
    CAPABILITIES,
    DESTINATION_DEFINITIONS,
    EXECUTION_CLASSES,
    QUALITY_DEFINITIONS,
    QUALITY_PRESETS,
    RELEASE_POLICIES,
    catalog_digest,
    catalog_document,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_POLICY_PATH,
    GOVERNANCE_MAX_AGE_DAYS,
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
    GOVERNANCE_REPOSITORY,
    discover_release_units,
    load_first_slice_authoring,
    load_quality_selection,
    load_release_policy,
    load_release_unit,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
PRODUCT_PATH = "src/public/lib/hcoona-release-smoke-npm"
PRODUCT_ROOT = REPO_ROOT / "src/public/lib/hcoona-release-smoke-npm"
POLICY_PATH = REPO_ROOT / FIRST_SLICE_POLICY_PATH

RELEASE_UNIT_YAML = """\
schema: workflow-delivery/v3/release-unit
release-unit: hcoona-release-smoke-npm
builds:
  - id: npm-package
    definition: node/npm-package-v1
    entry-point: package.json
    outputs:
      - id: npm-tarball
        role: primary-package
        kind: npm-tarball
"""

type DocumentMutation = Callable[[dict[str, JsonValue]], None]


def _run(repo: Path, *command: str) -> str:
    return subprocess.run(  # noqa: S603
        command,
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _initialize_repository(repo: Path) -> None:
    _run(repo, "git", "init", "--quiet")
    _run(repo, "git", "config", "user.name", "Workflow Delivery Test")
    _run(
        repo,
        "git",
        "config",
        "user.email",
        "workflow-delivery@example.invalid",
    )


def _commit_all(repo: Path) -> str:
    _run(repo, "git", "add", "--all")
    _run(repo, "git", "commit", "--quiet", "--message", "fixture")
    return _run(repo, "git", "rev-parse", "HEAD")


def _write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def _write_yaml(path: Path, document: JsonValue) -> None:
    _write(path, yaml.safe_dump(document, sort_keys=False))


def _json_value(value: object, *, context: str) -> JsonValue:
    if value is None or isinstance(value, bool | int | float | str):
        return value
    if isinstance(value, list):
        return [
            _json_value(item, context=f"{context} array item") for item in value
        ]
    if isinstance(value, dict):
        document: dict[str, JsonValue] = {}
        for name, item in value.items():
            if not isinstance(name, str):
                message = f"{context} object key is not a string"
                raise TypeError(message)
            document[name] = _json_value(item, context=f"{context}.{name}")
        return document
    message = f"{context} contains a non-JSON value"
    raise TypeError(message)


def _yaml_document(content: str) -> dict[str, JsonValue]:
    loaded: object = yaml.safe_load(content)
    value = _json_value(loaded, context="test YAML")
    if not isinstance(value, dict):
        message = "test YAML must be an object"
        raise TypeError(message)
    return value


def _object(value: JsonValue, *, context: str) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = f"{context} must be an object"
        raise TypeError(message)
    return value


def _array(value: JsonValue, *, context: str) -> list[JsonValue]:
    if not isinstance(value, list):
        message = f"{context} must be an array"
        raise TypeError(message)
    return value


def _release_unit_builds(
    document: dict[str, JsonValue],
) -> list[JsonValue]:
    return _array(document["builds"], context="builds")


def _first_release_unit_build(
    document: dict[str, JsonValue],
) -> dict[str, JsonValue]:
    return _object(_release_unit_builds(document)[0], context="builds[0]")


def _first_release_unit_outputs(
    document: dict[str, JsonValue],
) -> list[JsonValue]:
    build = _first_release_unit_build(document)
    return _array(build["outputs"], context="builds[0].outputs")


def _duplicate_release_unit_build(
    document: dict[str, JsonValue],
) -> None:
    builds = _release_unit_builds(document)
    builds.append(dict(_first_release_unit_build(document)))


def _duplicate_release_unit_output(
    document: dict[str, JsonValue],
) -> None:
    outputs = _first_release_unit_outputs(document)
    output = _object(outputs[0], context="builds[0].outputs[0]")
    outputs.append(dict(output))


def _remove_release_unit_builds(
    document: dict[str, JsonValue],
) -> None:
    document["builds"] = []


def _remove_release_unit_outputs(
    document: dict[str, JsonValue],
) -> None:
    _first_release_unit_build(document)["outputs"] = []


def _quality_ecosystems(
    document: dict[str, JsonValue],
) -> dict[str, JsonValue]:
    return _object(document["ecosystems"], context="ecosystems")


def _node_quality_selection(
    document: dict[str, JsonValue],
) -> dict[str, JsonValue]:
    return _object(
        _quality_ecosystems(document)["node"],
        context="ecosystems.node",
    )


def _set_wrong_schema(document: dict[str, JsonValue]) -> None:
    document["schema"] = "wrong"


def _add_quality_command(document: dict[str, JsonValue]) -> None:
    document["command"] = "npm test"


def _remove_quality_ecosystems(document: dict[str, JsonValue]) -> None:
    document["ecosystems"] = {}


def _set_unknown_quality_preset(document: dict[str, JsonValue]) -> None:
    _node_quality_selection(document)["preset"] = "node/target-supplied-v1"


def _add_quality_module(document: dict[str, JsonValue]) -> None:
    _node_quality_selection(document)["module"] = "target.py"


def _policy_channels(
    document: dict[str, JsonValue],
) -> dict[str, JsonValue]:
    return _object(document["channels"], context="channels")


def _channel_policy(
    document: dict[str, JsonValue],
    channel: str,
) -> dict[str, JsonValue]:
    return _object(
        _policy_channels(document)[channel],
        context=f"channels.{channel}",
    )


def _first_projection(
    document: dict[str, JsonValue],
    channel: str,
) -> dict[str, JsonValue]:
    policy = _channel_policy(document, channel)
    projections = _array(
        policy["projections"],
        context=f"channels.{channel}.projections",
    )
    return _object(
        projections[0],
        context=f"channels.{channel}.projections[0]",
    )


def _add_policy_command(document: dict[str, JsonValue]) -> None:
    document["command"] = "npm publish"


def _remove_official_channel(document: dict[str, JsonValue]) -> None:
    channels = _policy_channels(document)
    document["channels"] = {"buddy": channels["buddy"]}


def _set_unknown_buddy_quality(document: dict[str, JsonValue]) -> None:
    _channel_policy(document, "buddy")["quality"] = ["node/target-supplied-v1"]


def _set_buddy_official_destination(
    document: dict[str, JsonValue],
) -> None:
    _first_projection(document, "buddy")["destination"] = "npm/npmjs-public-v1"


def _set_other_official_package(
    document: dict[str, JsonValue],
) -> None:
    _first_projection(document, "official")["package"] = "@hcoona/other"


def _write_first_slice_authoring(
    repo: Path,
    *,
    descriptor: str = RELEASE_UNIT_YAML,
    quality: str | None = None,
    policy: str | None = None,
    include_entry_point: bool = True,
) -> None:
    product_root = repo / PRODUCT_PATH
    _write(product_root / "workflow-delivery.release-unit.yml", descriptor)
    _write(
        product_root / "workflow-delivery.quality.yml",
        quality
        or (PRODUCT_ROOT / "workflow-delivery.quality.yml").read_text(
            encoding="utf-8"
        ),
    )
    if include_entry_point:
        _write(
            product_root / "package.json",
            (PRODUCT_ROOT / "package.json").read_text(encoding="utf-8"),
        )
    _write(
        repo / FIRST_SLICE_POLICY_PATH,
        policy or POLICY_PATH.read_text(encoding="utf-8"),
    )


def _first_slice_target(tmp_path: Path) -> tuple[Path, str]:
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    _write_first_slice_authoring(repo)
    return repo, _commit_all(repo)


def test_checked_in_first_slice_authoring_matches_approved_lld(
    tmp_path: Path,
) -> None:
    """Load the exact Release Unit, Quality selection, and Release policy."""
    repo, target = _first_slice_target(tmp_path)

    descriptor, quality, policy = load_first_slice_authoring(repo, target)

    assert descriptor.release_unit == "hcoona-release-smoke-npm"
    assert descriptor.builds[0].build_id == "npm-package"
    assert descriptor.builds[0].definition == "node/npm-package-v1"
    assert descriptor.builds[0].entry_point == "package.json"
    assert tuple(asdict(output) for output in descriptor.builds[0].outputs) == (
        {
            "output_id": "npm-tarball",
            "role": "primary-package",
            "kind": "npm-tarball",
        },
    )
    assert quality.preset_for("node") == ("node/hcoona-release-smoke-npm-v1")
    assert policy.governance.repository == GOVERNANCE_REPOSITORY
    assert policy.governance.ref == GOVERNANCE_REF
    assert policy.governance.path == GOVERNANCE_PATH
    assert policy.governance.max_age_days == GOVERNANCE_MAX_AGE_DAYS


def test_first_slice_authoring_accepts_exact_approved_one_output(
    tmp_path: Path,
) -> None:
    """Accept only the approved first-slice one-output declaration."""
    repo, target = _first_slice_target(tmp_path)

    descriptor, _, _ = load_first_slice_authoring(repo, target)
    outputs = tuple(
        (output.output_id, output.role, output.kind)
        for build in descriptor.builds
        for output in build.outputs
    )

    assert outputs == (("npm-tarball", "primary-package", "npm-tarball"),)
    assert len(descriptor.builds) == 1
    assert len(descriptor.builds[0].outputs) == 1


def test_static_catalog_contains_exact_first_slice_contracts() -> None:
    """Register only the approved first-slice logical contract inventory."""
    assert set(BUILD_DEFINITIONS) == {"node/npm-package-v1"}
    assert set(QUALITY_DEFINITIONS) == {
        "node/project-build-v1",
        "node/project-test-v1",
        "repository/source-tree-conformance-v1",
        "node/npm-artifact-v1",
        "node/npm-artifact-contents-v1",
        "node/npm-install-import-v1",
    }
    assert set(QUALITY_PRESETS) == {"node/hcoona-release-smoke-npm-v1"}
    assert set(DESTINATION_DEFINITIONS) == {
        "npm/github-packages-hcoona-three-v1",
        "npm/npmjs-public-v1",
    }
    assert set(EXECUTION_CLASSES) == {
        "control/read-only-v1",
        "target-evaluation/unprivileged-v1",
        "target-execution/unprivileged-v1",
        "side-effect/privileged-v1",
    }
    assert set(CAPABILITIES) == {
        "github/actions-read-v1",
        "github/contents-read-v1",
        "github/packages-read-v1",
        "github/packages-write-v1",
        "npmjs/trusted-publishing-oidc-v1",
    }
    assert set(RELEASE_POLICIES) == {"hcoona-release-smoke-npm"}
    assert RELEASE_POLICIES["hcoona-release-smoke-npm"].path == (
        FIRST_SLICE_POLICY_PATH
    )


def test_quality_preset_expands_only_project_build_and_test() -> None:
    """Keep CI-added root and artifact obligations outside the preset."""
    preset = QUALITY_PRESETS["node/hcoona-release-smoke-npm-v1"]

    assert preset.required == (
        "node/project-build-v1",
        "node/project-test-v1",
    )
    assert preset.advisory == ()
    assert "repository/source-tree-conformance-v1" not in preset.required
    assert "node/npm-artifact-v1" not in preset.required


def test_catalog_definitions_are_data_only_and_canonically_stable() -> None:
    """Expose immutable data without command, module, or package loading."""
    first = catalog_document()
    second = json.loads(canonicalize(first))

    assert second == first
    assert catalog_digest() == (
        "sha256:2bebcd092c6c4ea58797488f76d9db9ffe166df8735d7efa"
        "67995163af24e357"
    )
    definition_sections = (
        "build-definitions",
        "quality-definitions",
        "destination-definitions",
        "execution-classes",
        "capabilities",
    )
    forbidden = {"command", "module", "executable", "plugin", "package-path"}
    for section in definition_sections:
        section_document = first[section]
        assert isinstance(section_document, dict)
        for record in section_document.values():
            assert isinstance(record, dict)
            assert forbidden.isdisjoint(record)
            assert set(record)


def test_release_policy_keeps_channel_policy_separate_from_quality_selection() -> (  # noqa: E501
    None
):
    """Keep project preset authoring separate from Release channel policy."""
    quality = load_quality_selection(
        PRODUCT_ROOT / "workflow-delivery.quality.yml"
    )
    policy = load_release_policy(POLICY_PATH)

    assert quality.preset_for("node") not in policy.channel("buddy").quality
    assert policy.channel("buddy").quality == (
        "node/project-test-v1",
        "node/npm-artifact-contents-v1",
        "node/npm-install-import-v1",
    )
    assert policy.channel("official").quality == (
        "node/project-test-v1",
        "node/npm-artifact-contents-v1",
        "node/npm-install-import-v1",
    )
    assert policy.channel("buddy").projections[0].destination == (
        "npm/github-packages-hcoona-three-v1"
    )
    assert policy.channel("official").projections[0].destination == (
        "npm/npmjs-public-v1"
    )
    assert {
        projection.package
        for _, channel in policy.channels
        for projection in channel.projections
    } == {FIRST_SLICE_PACKAGE}


def test_descriptor_discovery_uses_sorted_target_git_tree(
    tmp_path: Path,
) -> None:
    """Discover fixed basenames in target-tree order, not directory order."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    first = RELEASE_UNIT_YAML.replace(
        "hcoona-release-smoke-npm",
        "alpha-unit",
    )
    second = RELEASE_UNIT_YAML.replace(
        "hcoona-release-smoke-npm",
        "zeta-unit",
    )
    _write(repo / "z/workflow-delivery.release-unit.yml", second)
    _write(repo / "a/workflow-delivery.release-unit.yml", first)
    _write(repo / "a/package.json", "{}\n")
    _write(repo / "z/package.json", "{}\n")
    target = _commit_all(repo)

    descriptors = discover_release_units(repo, target)

    assert tuple(item.release_unit for item in descriptors) == (
        "alpha-unit",
        "zeta-unit",
    )
    assert tuple(Path(item.path).parent.name for item in descriptors) == (
        "a",
        "z",
    )


def test_descriptor_discovery_reads_exact_target_not_changed_worktree(
    tmp_path: Path,
) -> None:
    """Read descriptor bytes from the target tree, not later worktree edits."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    descriptor_path = repo / "unit/workflow-delivery.release-unit.yml"
    _write(descriptor_path, RELEASE_UNIT_YAML)
    _write(repo / "unit/package.json", "{}\n")
    target = _commit_all(repo)
    _write(
        descriptor_path,
        RELEASE_UNIT_YAML.replace(
            "hcoona-release-smoke-npm",
            "changed-worktree-unit",
        ),
    )

    descriptors = discover_release_units(repo, target)

    assert tuple(item.release_unit for item in descriptors) == (
        "hcoona-release-smoke-npm",
    )
    assert (
        descriptor_path.read_text(encoding="utf-8").find(
            "changed-worktree-unit"
        )
        > 0
    )


def test_first_slice_authoring_reads_quality_from_exact_target(
    tmp_path: Path,
) -> None:
    """Ignore dirty Quality selection while validating a target SHA."""
    repo, target = _first_slice_target(tmp_path)
    quality_path = repo / PRODUCT_PATH / "workflow-delivery.quality.yml"
    _write(
        quality_path,
        quality_path.read_text(encoding="utf-8").replace(
            "node/hcoona-release-smoke-npm-v1",
            "node/dirty-worktree-v1",
        ),
    )

    _, quality, _ = load_first_slice_authoring(repo, target)

    assert quality.preset_for("node") == "node/hcoona-release-smoke-npm-v1"
    assert "node/dirty-worktree-v1" in quality_path.read_text(encoding="utf-8")


def test_first_slice_authoring_reads_policy_from_exact_target(
    tmp_path: Path,
) -> None:
    """Ignore dirty Release policy while validating a target SHA."""
    repo, target = _first_slice_target(tmp_path)
    policy_path = repo / FIRST_SLICE_POLICY_PATH
    _write(
        policy_path,
        policy_path.read_text(encoding="utf-8").replace(
            "@hcoona/hcoona-release-smoke-npm",
            "@hcoona/dirty-worktree",
        ),
    )

    _, _, policy = load_first_slice_authoring(repo, target)

    assert {
        projection.package
        for _, channel in policy.channels
        for projection in channel.projections
    } == {FIRST_SLICE_PACKAGE}
    assert "@hcoona/dirty-worktree" in policy_path.read_text(encoding="utf-8")


def test_first_slice_authoring_ignores_worktree_only_duplicate_descriptor(
    tmp_path: Path,
) -> None:
    """Do not reject non-target duplicate descriptors from the worktree."""
    repo, target = _first_slice_target(tmp_path)
    duplicate_path = repo / "dirty/workflow-delivery.release-unit.yml"
    _write(duplicate_path, RELEASE_UNIT_YAML)

    descriptor, _, _ = load_first_slice_authoring(repo, target)

    assert descriptor.release_unit == "hcoona-release-smoke-npm"
    assert duplicate_path.is_file()


def test_first_slice_authoring_rejects_extra_target_release_unit(
    tmp_path: Path,
) -> None:
    """Keep commit 3 closed to the sole approved first-slice Release Unit."""
    repo, _ = _first_slice_target(tmp_path)
    extra_root = repo / "src/public/lib/other-release-unit"
    _write(
        extra_root / "workflow-delivery.release-unit.yml",
        RELEASE_UNIT_YAML.replace(
            "hcoona-release-smoke-npm",
            "other-release-unit",
        ),
    )
    _write(extra_root / "package.json", "{}\n")
    target = _commit_all(repo)

    with pytest.raises(
        ValueError,
        match="exactly one Release Unit descriptor",
    ):
        load_first_slice_authoring(repo, target)


def test_descriptor_discovery_rejects_duplicate_release_unit(
    tmp_path: Path,
) -> None:
    """Reject one stable identity declared at two target-tree paths."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    for directory in ("first", "second"):
        _write(
            repo / directory / "workflow-delivery.release-unit.yml",
            RELEASE_UNIT_YAML,
        )
        _write(repo / directory / "package.json", "{}\n")
    target = _commit_all(repo)

    with pytest.raises(
        ValueError,
        match="duplicate Release Unit identity: hcoona-release-smoke-npm",
    ):
        discover_release_units(repo, target)


@pytest.mark.parametrize(
    ("content", "message"),
    [
        (
            RELEASE_UNIT_YAML.replace(
                "schema: workflow-delivery/v3/release-unit",
                "schema: workflow-delivery/v3/release-unit\nschema: duplicate",
            ),
            "duplicate YAML mapping key: 'schema'",
        ),
        (
            RELEASE_UNIT_YAML + "command: npm pack\n",
            "release-unit descriptor unknown field: command",
        ),
        (
            RELEASE_UNIT_YAML.replace(
                "definition: node/npm-package-v1",
                "definition: node/target-supplied-command-v1",
            ),
            "unknown Build Definition catalog ID",
        ),
        (
            RELEASE_UNIT_YAML.replace(
                "entry-point: package.json",
                "entry-point: ../../outside.json",
            ),
            "normalized relative POSIX path",
        ),
        (
            RELEASE_UNIT_YAML.replace(
                "kind: npm-tarball",
                "kind: npm-tarball\n        module: target.py",
            ),
            "unknown field: module",
        ),
    ],
    ids=[
        "duplicate-key",
        "command-injection",
        "unknown-definition",
        "escaping-entry-point",
        "module-injection",
    ],
)
def test_strict_authoring_rejects_nonclosed_or_executable_selection(
    tmp_path: Path,
    content: str,
    message: str,
) -> None:
    """Reject duplicate, unknown, executable, and escaping authoring."""
    path = tmp_path / "workflow-delivery.release-unit.yml"
    _write(path, content)

    with pytest.raises((TypeError, ValueError), match=message):
        load_release_unit(path)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("repository", "other/repository"),
        ("ref", "main"),
        ("path", ".github/other.json"),
        ("max-age-days", 89),
        ("max-age-days", 91),
    ],
    ids=["repository", "ref", "path", "age-lower", "age-upper"],
)
def test_release_policy_requires_exact_governance_source(
    tmp_path: Path,
    field: str,
    value: JsonValue,
) -> None:
    """Reject any mutable variation in the fixed Governance source."""
    document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    governance = _object(document["governance"], context="governance")
    attestation = _object(
        governance["attestation"],
        context="governance.attestation",
    )
    attestation[field] = value
    path = tmp_path / "policy.yml"
    _write_yaml(path, document)

    with pytest.raises(
        ValueError,
        match="Governance source is not the fixed contract",
    ):
        load_release_policy(path)


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            _duplicate_release_unit_build,
            "duplicate build identity",
        ),
        (
            _duplicate_release_unit_output,
            "duplicate output identity",
        ),
        (
            _remove_release_unit_builds,
            "must declare a build",
        ),
        (
            _remove_release_unit_outputs,
            "build has no outputs",
        ),
    ],
    ids=["duplicate-build", "duplicate-output", "no-build", "no-output"],
)
def test_release_unit_rejects_incomplete_or_ambiguous_scope(
    tmp_path: Path,
    mutate: DocumentMutation,
    message: str,
) -> None:
    """Reject ambiguous identities and incomplete build/artifact scope."""
    document = _yaml_document(RELEASE_UNIT_YAML)
    mutate(document)
    path = tmp_path / "workflow-delivery.release-unit.yml"
    _write_yaml(path, document)

    with pytest.raises(ValueError, match=message):
        load_release_unit(path)


def test_first_slice_authoring_rejects_missing_entry_point(
    tmp_path: Path,
) -> None:
    """Require the descriptor-relative Build entry point to exist."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    _write_first_slice_authoring(repo, include_entry_point=False)
    target = _commit_all(repo)

    with pytest.raises(
        ValueError,
        match=r"Build entry point does not exist: package\.json",
    ):
        load_first_slice_authoring(repo, target)


@pytest.mark.parametrize(
    "outputs",
    [
        [
            {
                "id": "npm-tarball",
                "role": "primary-package",
                "kind": "npm-tarball",
            },
            {
                "id": "extra-npm-tarball",
                "role": "secondary-package",
                "kind": "npm-tarball",
            },
        ],
        [
            {
                "id": "renamed-npm-tarball",
                "role": "primary-package",
                "kind": "npm-tarball",
            },
        ],
    ],
    ids=["extra-same-kind", "different-identity"],
)
def test_first_slice_authoring_rejects_nonapproved_output_closure(
    tmp_path: Path,
    outputs: list[dict[str, str]],
) -> None:
    """Reject same-kind extras and renamed outputs despite matching kinds."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    descriptor = _yaml_document(RELEASE_UNIT_YAML)
    _first_release_unit_build(descriptor)["outputs"] = [
        dict(output) for output in outputs
    ]
    _write_first_slice_authoring(
        repo,
        descriptor=yaml.safe_dump(descriptor, sort_keys=False),
    )
    target = _commit_all(repo)

    with pytest.raises(
        ValueError,
        match="Build outputs do not match approved first-slice output",
    ):
        load_first_slice_authoring(repo, target)


@pytest.mark.parametrize(
    "mutation",
    ["missing", "extra", "renamed", "duplicate", "substituted"],
)
def test_first_slice_authoring_rejects_non_exact_build_selection(
    tmp_path: Path,
    mutation: str,
) -> None:
    """Reject every non-singleton npm-package build authoring closure."""
    document = _yaml_document(RELEASE_UNIT_YAML)
    builds = _release_unit_builds(document)
    build = dict(_first_release_unit_build(document))
    if mutation == "missing":
        document["builds"] = []
    elif mutation == "extra":
        extra = dict(build)
        extra["id"] = "extra-package"
        builds.append(extra)
    elif mutation == "renamed":
        build["id"] = "renamed-package"
        document["builds"] = [build]
    elif mutation == "duplicate":
        builds.append(dict(build))
    else:
        build["entry-point"] = "other-package.json"
        document["builds"] = [build]

    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    _write_first_slice_authoring(
        repo,
        descriptor=yaml.safe_dump(document, sort_keys=False),
    )
    target = _commit_all(repo)

    with pytest.raises((TypeError, ValueError)):
        load_first_slice_authoring(repo, target)


@pytest.mark.parametrize(
    "mutation",
    ["missing", "extra", "renamed", "duplicate", "substituted"],
)
def test_first_slice_authoring_rejects_non_exact_quality_selection(
    tmp_path: Path,
    mutation: str,
) -> None:
    """Reject every non-singleton approved Node preset selection."""
    document = _yaml_document(
        (PRODUCT_ROOT / "workflow-delivery.quality.yml").read_text(
            encoding="utf-8"
        )
    )
    ecosystems = _quality_ecosystems(document)
    if mutation == "missing":
        document["ecosystems"] = {}
    elif mutation == "extra":
        ecosystems["javascript"] = {
            "preset": "node/hcoona-release-smoke-npm-v1"
        }
    elif mutation == "renamed":
        document["ecosystems"] = {
            "javascript": ecosystems["node"],
        }
    elif mutation == "substituted":
        _node_quality_selection(document)["preset"] = "node/substituted-v1"

    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    if mutation == "duplicate":
        quality = (
            "schema: workflow-delivery/v3/quality-selection\n"
            "ecosystems:\n"
            "  node:\n"
            "    preset: node/hcoona-release-smoke-npm-v1\n"
            "  node:\n"
            "    preset: node/hcoona-release-smoke-npm-v1\n"
        )
    else:
        quality = yaml.safe_dump(document, sort_keys=False)
    _write_first_slice_authoring(repo, quality=quality)
    target = _commit_all(repo)

    with pytest.raises((TypeError, ValueError)):
        load_first_slice_authoring(repo, target)


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            _set_wrong_schema,
            "wrong schema",
        ),
        (
            _add_quality_command,
            "unknown field: command",
        ),
        (
            _remove_quality_ecosystems,
            "must select an ecosystem preset",
        ),
        (
            _set_unknown_quality_preset,
            "unknown Quality preset catalog ID",
        ),
        (
            _add_quality_module,
            "unknown field: module",
        ),
    ],
    ids=[
        "schema",
        "top-level-field",
        "empty",
        "unknown-preset",
        "executable-selection",
    ],
)
def test_quality_selection_is_closed_and_catalog_allowlisted(
    tmp_path: Path,
    mutate: DocumentMutation,
    message: str,
) -> None:
    """Reject malformed or target-extensible Quality selection."""
    document = _yaml_document(
        (PRODUCT_ROOT / "workflow-delivery.quality.yml").read_text(
            encoding="utf-8"
        )
    )
    mutate(document)
    path = tmp_path / "workflow-delivery.quality.yml"
    _write_yaml(path, document)

    with pytest.raises(ValueError, match=message):
        load_quality_selection(path)


@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (
            _set_wrong_schema,
            "wrong schema",
        ),
        (
            _add_policy_command,
            "unknown field: command",
        ),
        (
            _remove_official_channel,
            "channels must be exactly buddy and official",
        ),
        (
            _set_unknown_buddy_quality,
            "unknown Quality Definition catalog ID",
        ),
        (
            _set_buddy_official_destination,
            "does not support buddy",
        ),
        (
            _set_other_official_package,
            "package binding is not exact",
        ),
    ],
    ids=[
        "schema",
        "top-level-field",
        "channel-set",
        "quality-id",
        "destination-channel",
        "package-binding",
    ],
)
def test_release_policy_is_closed_and_catalog_allowlisted(
    tmp_path: Path,
    mutate: DocumentMutation,
    message: str,
) -> None:
    """Reject policy schema drift and target-selected behavior."""
    document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    mutate(document)
    path = tmp_path / "policy.yml"
    _write_yaml(path, document)

    with pytest.raises(ValueError, match=message):
        load_release_policy(path)


@pytest.mark.parametrize(
    ("descriptor_update", "message"),
    [
        (
            {"release-unit": "other-unit"},
            "descriptor and policy identity mismatch",
        ),
        (
            {"output-kind": "other-artifact"},
            "Build output kinds do not match",
        ),
    ],
    ids=["release-unit-binding", "output-kind-binding"],
)
def test_first_slice_authoring_correlates_descriptor_policy_and_catalog(
    tmp_path: Path,
    descriptor_update: dict[str, str],
    message: str,
) -> None:
    """Reject valid individual documents that do not correlate."""
    repo = tmp_path / "repo"
    repo.mkdir()
    _initialize_repository(repo)
    descriptor = _yaml_document(RELEASE_UNIT_YAML)
    if "release-unit" in descriptor_update:
        descriptor["release-unit"] = descriptor_update["release-unit"]
    if "output-kind" in descriptor_update:
        output = _object(
            _first_release_unit_outputs(descriptor)[0],
            context="builds[0].outputs[0]",
        )
        output["kind"] = descriptor_update["output-kind"]
    _write_first_slice_authoring(
        repo,
        descriptor=yaml.safe_dump(descriptor, sort_keys=False),
    )
    target = _commit_all(repo)

    with pytest.raises(ValueError, match=message):
        load_first_slice_authoring(repo, target)


def test_authoring_lookup_rejects_unselected_names(tmp_path: Path) -> None:
    """Do not silently substitute absent Quality presets or channels."""
    repo, target = _first_slice_target(tmp_path)
    _, quality, policy = load_first_slice_authoring(repo, target)

    with pytest.raises(ValueError, match="no ecosystem: python"):
        quality.preset_for("python")
    with pytest.raises(ValueError, match="no channel: preview"):
        policy.channel("preview")


type ReleaseChannelCase = tuple[
    str,
    tuple[str, str, str],
    tuple[dict[str, str], ...],
]
type QualityMutationCase = tuple[str, tuple[str, ...]]

_APPROVED_RELEASE_QUALITY = (
    "node/project-test-v1",
    "node/npm-artifact-contents-v1",
    "node/npm-install-import-v1",
)
_APPROVED_RELEASE_PROJECTIONS = {
    "buddy": (
        {
            "destination": "npm/github-packages-hcoona-three-v1",
            "artifact": "npm-tarball",
            "package": "@hcoona/hcoona-release-smoke-npm",
        },
    ),
    "official": (
        {
            "destination": "npm/npmjs-public-v1",
            "artifact": "npm-tarball",
            "package": "@hcoona/hcoona-release-smoke-npm",
        },
    ),
}


@pytest.fixture(
    params=(
        pytest.param(
            (
                "buddy",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
                (
                    {
                        "destination": ("npm/github-packages-hcoona-three-v1"),
                        "artifact": "npm-tarball",
                        "package": "@hcoona/hcoona-release-smoke-npm",
                    },
                ),
            ),
            id="buddy",
        ),
        pytest.param(
            (
                "official",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
                (
                    {
                        "destination": "npm/npmjs-public-v1",
                        "artifact": "npm-tarball",
                        "package": "@hcoona/hcoona-release-smoke-npm",
                    },
                ),
            ),
            id="official",
        ),
    )
)
def accepted_release_channel_cases(
    request: pytest.FixtureRequest,
) -> ReleaseChannelCase:
    """Provide literal accepted Buddy and Official channel contracts."""
    return request.param


@pytest.fixture(
    params=(
        pytest.param(
            (
                "omit-node-project-test-v1",
                (
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="omit-node-project-test-v1",
        ),
        pytest.param(
            (
                "omit-node-npm-artifact-contents-v1",
                (
                    "node/project-test-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="omit-node-npm-artifact-contents-v1",
        ),
        pytest.param(
            (
                "omit-node-npm-install-import-v1",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                ),
            ),
            id="omit-node-npm-install-import-v1",
        ),
        pytest.param(
            (
                "duplicate-node-project-test-v1",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                    "node/project-test-v1",
                ),
            ),
            id="duplicate-node-project-test-v1",
        ),
        pytest.param(
            (
                "duplicate-node-npm-artifact-contents-v1",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                    "node/npm-artifact-contents-v1",
                ),
            ),
            id="duplicate-node-npm-artifact-contents-v1",
        ),
        pytest.param(
            (
                "duplicate-node-npm-install-import-v1",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="duplicate-node-npm-install-import-v1",
        ),
        pytest.param(("empty", ()), id="empty"),
        pytest.param(
            (
                "extra-node-project-build-v1",
                (
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                    "node/project-build-v1",
                ),
            ),
            id="extra-node-project-build-v1",
        ),
        pytest.param(
            (
                "substitute-node-project-build-v1",
                (
                    "node/project-build-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="substitute-node-project-build-v1",
        ),
        pytest.param(
            (
                "substitute-repository-source-tree-conformance-v1",
                (
                    "repository/source-tree-conformance-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="substitute-repository-source-tree-conformance-v1",
        ),
        pytest.param(
            (
                "substitute-node-npm-artifact-v1",
                (
                    "node/npm-artifact-v1",
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="substitute-node-npm-artifact-v1",
        ),
        pytest.param(
            (
                "order-artifact-contents-project-test-install-import",
                (
                    "node/npm-artifact-contents-v1",
                    "node/project-test-v1",
                    "node/npm-install-import-v1",
                ),
            ),
            id="order-artifact-contents-project-test-install-import",
        ),
        pytest.param(
            (
                "order-project-test-install-import-artifact-contents",
                (
                    "node/project-test-v1",
                    "node/npm-install-import-v1",
                    "node/npm-artifact-contents-v1",
                ),
            ),
            id="order-project-test-install-import-artifact-contents",
        ),
        pytest.param(
            (
                "order-artifact-contents-install-import-project-test",
                (
                    "node/npm-artifact-contents-v1",
                    "node/npm-install-import-v1",
                    "node/project-test-v1",
                ),
            ),
            id="order-artifact-contents-install-import-project-test",
        ),
        pytest.param(
            (
                "order-install-import-project-test-artifact-contents",
                (
                    "node/npm-install-import-v1",
                    "node/project-test-v1",
                    "node/npm-artifact-contents-v1",
                ),
            ),
            id="order-install-import-project-test-artifact-contents",
        ),
        pytest.param(
            (
                "order-install-import-artifact-contents-project-test",
                (
                    "node/npm-install-import-v1",
                    "node/npm-artifact-contents-v1",
                    "node/project-test-v1",
                ),
            ),
            id="order-install-import-artifact-contents-project-test",
        ),
    )
)
def quality_mutation_cases(
    request: pytest.FixtureRequest,
) -> QualityMutationCase:
    """Provide the exact quality-list rejection matrix."""
    return request.param


@pytest.fixture(
    params=(
        pytest.param("missing-projections", id="missing-projections"),
        pytest.param("empty", id="empty"),
        pytest.param("omit-destination", id="omit-destination"),
        pytest.param("omit-artifact", id="omit-artifact"),
        pytest.param("omit-package", id="omit-package"),
        pytest.param("duplicate-approved", id="duplicate-approved"),
        pytest.param("extra", id="extra"),
        pytest.param(
            "substitute-destination",
            id="substitute-destination",
        ),
        pytest.param("substitute-artifact", id="substitute-artifact"),
        pytest.param("substitute-package", id="substitute-package"),
    )
)
def projection_mutation_cases(request: pytest.FixtureRequest) -> str:
    """Provide the exact projection-list rejection matrix."""
    return request.param


def write_release_policy_case(
    tmp_path: Path,
    document: dict[str, JsonValue],
    *,
    name: str,
) -> Path:
    """Write only one temporary Release-policy fixture."""
    path = tmp_path / f"{name}.yml"
    _write_yaml(path, document)
    return path


def _assert_accepted_channel_contract(
    policy_path: Path,
    channel: str,
    expected_quality: tuple[str, str, str],
    expected_projections: tuple[dict[str, str], ...],
) -> None:
    policy = load_release_policy(policy_path)
    channel_policy = policy.channel(channel)

    assert channel_policy.quality == expected_quality
    assert (
        tuple(asdict(projection) for projection in channel_policy.projections)
        == expected_projections
    )
    assert len(channel_policy.quality) == len(_APPROVED_RELEASE_QUALITY)
    assert len(channel_policy.projections) == 1


def _assert_opposite_channel_unchanged(
    document: dict[str, JsonValue],
    selected_channel: str,
) -> None:
    opposite_channel = "official" if selected_channel == "buddy" else "buddy"
    opposite = _channel_policy(document, opposite_channel)

    assert opposite["quality"] == list(_APPROVED_RELEASE_QUALITY)
    assert opposite["projections"] == [
        dict(projection)
        for projection in _APPROVED_RELEASE_PROJECTIONS[opposite_channel]
    ]


def _mutate_projection_case(
    document: dict[str, JsonValue],
    channel: str,
    mutation: str,
) -> None:
    channel_document = _channel_policy(document, channel)
    if mutation == "missing-projections":
        del channel_document["projections"]
        return
    if mutation == "empty":
        channel_document["projections"] = []
        return

    projections = _array(
        channel_document["projections"],
        context=f"channels.{channel}.projections",
    )
    approved = _object(
        projections[0],
        context=f"channels.{channel}.projections[0]",
    )
    if mutation.startswith("omit-"):
        del approved[mutation.removeprefix("omit-")]
    elif mutation == "duplicate-approved":
        channel_document["projections"] = [
            dict(approved),
            dict(approved),
        ]
    elif mutation == "extra":
        extra = dict(approved)
        extra["artifact"] = "other-artifact"
        projections.append(extra)
    elif mutation == "substitute-destination":
        approved["destination"] = (
            "npm/npmjs-public-v1"
            if channel == "buddy"
            else "npm/github-packages-hcoona-three-v1"
        )
    elif mutation == "substitute-artifact":
        approved["artifact"] = "other-artifact"
    elif mutation == "substitute-package":
        approved["package"] = "@hcoona/other"
    else:
        message = f"unknown projection mutation: {mutation}"
        raise AssertionError(message)


def test_release_policy_requires_exact_ordered_channel_quality(
    tmp_path: Path,
    accepted_release_channel_cases: ReleaseChannelCase,
    quality_mutation_cases: QualityMutationCase,
) -> None:
    """Require the exact closed, ordered quality tuple for both channels."""
    channel, expected_quality, expected_projections = (
        accepted_release_channel_cases
    )
    mutation, mutated_quality = quality_mutation_cases
    accepted_document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    accepted_path = write_release_policy_case(
        tmp_path,
        accepted_document,
        name=f"accepted-{channel}-{mutation}",
    )
    _assert_accepted_channel_contract(
        accepted_path,
        channel,
        expected_quality,
        expected_projections,
    )

    mutated_document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    mutated_quality_document: list[JsonValue] = list(mutated_quality)
    _channel_policy(mutated_document, channel)["quality"] = (
        mutated_quality_document
    )
    _assert_opposite_channel_unchanged(mutated_document, channel)
    mutated_path = write_release_policy_case(
        tmp_path,
        mutated_document,
        name=f"{channel}-{mutation}",
    )

    with pytest.raises(
        ValueError,
        match=rf"(?=.*{channel})(?=.*quality)",
    ):
        load_release_policy(mutated_path)


def test_release_policy_requires_exact_channel_projection(
    tmp_path: Path,
    accepted_release_channel_cases: ReleaseChannelCase,
    projection_mutation_cases: str,
) -> None:
    """Require each channel's exact one-element projection tuple."""
    channel, expected_quality, expected_projections = (
        accepted_release_channel_cases
    )
    accepted_document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    accepted_path = write_release_policy_case(
        tmp_path,
        accepted_document,
        name=f"accepted-{channel}-{projection_mutation_cases}",
    )
    _assert_accepted_channel_contract(
        accepted_path,
        channel,
        expected_quality,
        expected_projections,
    )

    mutated_document = _yaml_document(POLICY_PATH.read_text(encoding="utf-8"))
    _mutate_projection_case(
        mutated_document,
        channel,
        projection_mutation_cases,
    )
    _assert_opposite_channel_unchanged(mutated_document, channel)
    mutated_path = write_release_policy_case(
        tmp_path,
        mutated_document,
        name=f"{channel}-{projection_mutation_cases}",
    )

    with pytest.raises(
        ValueError,
        match=rf"(?=.*{channel})(?=.*projection)",
    ):
        load_release_policy(mutated_path)


def test_npmjs_destination_uses_hypothetical_trusted_publishing_oidc() -> None:
    """Freeze the simulation-only npmjs trusted-publishing contract."""
    document = catalog_document()
    capabilities_document = _object(
        document["capabilities"],
        context="catalog capabilities",
    )

    assert tuple(document) == (
        "schema",
        "build-definitions",
        "quality-definitions",
        "quality-presets",
        "destination-definitions",
        "execution-classes",
        "capabilities",
        "release-policies",
    )
    assert tuple(capabilities_document) == (
        "github/actions-read-v1",
        "github/contents-read-v1",
        "github/packages-read-v1",
        "github/packages-write-v1",
        "npmjs/trusted-publishing-oidc-v1",
    )
    assert catalog_digest() == (
        "sha256:2bebcd092c6c4ea58797488f76d9db9ffe166df8735d7efa"
        "67995163af24e357"
    )

    npmjs_capability = CAPABILITIES["npmjs/trusted-publishing-oidc-v1"]
    assert asdict(npmjs_capability) == {
        "logical_id": "npmjs/trusted-publishing-oidc-v1",
        "github_permissions": (
            ("contents", "read"),
            ("id-token", "write"),
        ),
        "permits_mutation": True,
    }
    assert npmjs_capability.github_permissions == (
        ("contents", "read"),
        ("id-token", "write"),
    )
    assert npmjs_capability.permits_mutation is True
    assert all(
        not f"{permission}:{access}".startswith("packages:")
        for permission, access in npmjs_capability.github_permissions
    )

    official = DESTINATION_DEFINITIONS["npm/npmjs-public-v1"]
    assert official.capability_requirements == (
        "npmjs/trusted-publishing-oidc-v1",
    )
    assert "github/packages-write-v1" not in (official.capability_requirements)
    assert all(
        marker not in requirement
        for requirement in official.capability_requirements
        for marker in ("pat", "token", "secret")
    )
    assert official.live_mutation_status == ("simulation-only-in-first-slice")

    buddy = DESTINATION_DEFINITIONS["npm/github-packages-hcoona-three-v1"]
    assert buddy.capability_requirements == ("github/packages-write-v1",)
