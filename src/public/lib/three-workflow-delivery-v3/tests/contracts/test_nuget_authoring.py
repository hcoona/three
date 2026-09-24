"""Independent npm/NuGet authoring beside the accepted Python descriptor."""

from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

import pytest
from three_workflow_delivery_v3.catalogs import (
    BUILD_DEFINITIONS,
    DESTINATION_DEFINITIONS,
    QUALITY_DEFINITIONS,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_RELEASE_UNIT,
    NUGET_GOVERNANCE_PATH,
    NUGET_PACKAGE,
    NUGET_POLICY_PATH,
    NUGET_RELEASE_QUALITY,
    NUGET_RELEASE_UNIT,
    discover_release_units,
    load_first_slice_authoring,
    load_nuget_authoring,
    load_release_policy,
)

REPOSITORY = Path(__file__).resolve().parents[6]
PYTHON_UNIT = "hcoona-release-smoke-python"
DESCRIPTOR_NAME = "workflow-delivery.release-unit.yml"


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(  # noqa: S603
        ("git", *args),  # noqa: S607
        cwd=repo,
        capture_output=True,
        check=True,
        text=True,
    ).stdout.strip()


@pytest.fixture
def authored_tree(tmp_path: Path) -> tuple[Path, str]:
    """Commit all admitted descriptors without invoking native toolchains."""
    for unit in (FIRST_SLICE_RELEASE_UNIT, NUGET_RELEASE_UNIT):
        root = Path("src/public/lib") / unit
        (tmp_path / root).mkdir(parents=True)
        for name in (
            "workflow-delivery.release-unit.yml",
            "workflow-delivery.quality.yml",
        ):
            shutil.copyfile(REPOSITORY / root / name, tmp_path / root / name)
        policy = Path("eng/workflow-delivery/v3/policies") / f"{unit}.yml"
        (tmp_path / policy).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPOSITORY / policy, tmp_path / policy)
    npm_manifest = tmp_path / (
        "src/public/lib/hcoona-release-smoke-npm/package.json"
    )
    npm_manifest.write_text(
        '{"name":"@hcoona/hcoona-release-smoke-npm"}', encoding="utf-8"
    )
    project = (
        tmp_path
        / f"src/public/lib/{NUGET_RELEASE_UNIT}/{NUGET_RELEASE_UNIT}.csproj"
    )
    project.write_text('<Project Sdk="Microsoft.NET.Sdk" />', encoding="utf-8")
    python_descriptor = Path("src/public/lib") / PYTHON_UNIT / DESCRIPTOR_NAME
    (tmp_path / python_descriptor).parent.mkdir(parents=True)
    shutil.copyfile(
        REPOSITORY / python_descriptor, tmp_path / python_descriptor
    )
    _git(tmp_path, "init", "--quiet")
    _git(tmp_path, "config", "user.name", "Workflow Delivery Test")
    _git(tmp_path, "config", "user.email", "workflow-delivery@example.invalid")
    _git(tmp_path, "add", ".")
    _git(tmp_path, "commit", "--quiet", "-m", "Fixture")
    return tmp_path, _git(tmp_path, "rev-parse", "HEAD")


def test_nuget_authoring_and_npm_select_independent_units(
    authored_tree: tuple[Path, str],
) -> None:
    """Python coexistence leaves each existing unit's own contract intact."""
    repo, target = authored_tree
    assert {
        (d.release_unit, d.path) for d in discover_release_units(repo, target)
    } == {
        (unit, f"src/public/lib/{unit}/{DESCRIPTOR_NAME}")
        for unit in (FIRST_SLICE_RELEASE_UNIT, NUGET_RELEASE_UNIT, PYTHON_UNIT)
    }
    nuget, quality, policy = load_nuget_authoring(repo, target)
    npm, npm_quality, npm_policy = load_first_slice_authoring(repo, target)
    assert nuget.release_unit == NUGET_RELEASE_UNIT
    assert nuget.builds[0].definition == "dotnet/nuget-package-v1"
    assert nuget.builds[0].outputs[0].kind == "nuget-package"
    assert quality.ecosystems == (
        ("dotnet", "dotnet/hcoona-release-smoke-github-packages-v1"),
    )
    assert policy.governance.path == NUGET_GOVERNANCE_PATH
    assert tuple(name for name, _ in policy.channels) == ("buddy",)
    assert policy.channel("buddy").quality == NUGET_RELEASE_QUALITY
    assert policy.channel("buddy").projections[0].package == NUGET_PACKAGE
    assert npm.release_unit == FIRST_SLICE_RELEASE_UNIT
    assert npm.builds[0].definition == "node/npm-package-v1"
    assert npm.builds[0].outputs[0].kind == "npm-tarball"
    assert npm_quality.ecosystems == (
        ("node", "node/hcoona-release-smoke-npm-v1"),
    )
    assert (
        npm_policy.channel("buddy").projections[0].package
        == FIRST_SLICE_PACKAGE
    )
    assert npm_policy.governance != policy.governance
    assert tuple(name for name, _ in npm_policy.channels) == (
        "buddy",
        "official",
    )


@pytest.mark.parametrize(
    ("old", "new"),
    [
        (
            NUGET_GOVERNANCE_PATH,
            ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json",
        ),
        (NUGET_PACKAGE, "Hcoona.ReleaseSmoke.Nuget"),
        (
            "nuget/github-packages-hcoona-three-v1",
            "npm/github-packages-hcoona-three-v1",
        ),
        ("artifact: nuget-package", "artifact: npm-tarball"),
        ("dotnet/nuget-restore-build-invoke-v1", "node/npm-install-import-v1"),
        ("  buddy:", "  official:"),
    ],
)
def test_nuget_policy_rejects_cross_slice_or_expanded_contract(
    tmp_path: Path,
    old: str,
    new: str,
) -> None:
    """Authoring cannot borrow npm authority or expand the NuGet slice."""
    path = tmp_path / "policy.yml"
    content = (REPOSITORY / NUGET_POLICY_PATH).read_text(encoding="utf-8")
    assert old in content
    path.write_text(content.replace(old, new), encoding="utf-8")
    with pytest.raises(ValueError, match=r"Governance|channels"):
        load_release_policy(path)


def test_nuget_authoring_reads_committed_target_not_dirty_worktree(
    authored_tree: tuple[Path, str],
) -> None:
    """The exact committed target owns authoring despite a dirty checkout."""
    repo, target = authored_tree
    policy = repo / NUGET_POLICY_PATH
    policy.write_text("invalid: dirty policy", encoding="utf-8")
    _, _, admitted = load_nuget_authoring(repo, target)
    assert admitted.channel("buddy").projections[0].package == NUGET_PACKAGE
    _git(repo, "add", ".")
    _git(repo, "commit", "--quiet", "-m", "Invalid fixture")
    with pytest.raises(ValueError, match="release policy missing required"):
        load_nuget_authoring(repo, _git(repo, "rev-parse", "HEAD"))


@pytest.mark.parametrize(
    "loader", [load_nuget_authoring, load_first_slice_authoring]
)
@pytest.mark.parametrize(
    "mutation", ["unknown", "misplaced", "identity-mismatch", "duplicate"]
)
def test_known_slice_does_not_admit_unknown_or_misplaced_descriptor(
    authored_tree: tuple[Path, str],
    loader: object,
    mutation: str,
) -> None:
    """Known-unit discovery rejects unrelated authoring in the target tree."""
    repo, _ = authored_tree
    source = repo / f"src/public/lib/{PYTHON_UNIT}" / DESCRIPTOR_NAME
    if mutation == "identity-mismatch":
        source.write_text(
            source.read_text(encoding="utf-8").replace(
                PYTHON_UNIT, "unknown-unit"
            ),
            encoding="utf-8",
        )
    else:
        directory = (
            "src/public/lib/unknown-unit"
            if mutation == "unknown"
            else "src/misplaced"
        )
        extra = repo / directory / DESCRIPTOR_NAME
        extra.parent.mkdir(parents=True)
        if mutation == "unknown":
            extra.write_text(
                source.read_text(encoding="utf-8").replace(
                    PYTHON_UNIT, "unknown-unit"
                ),
                encoding="utf-8",
            )
        elif mutation == "duplicate":
            shutil.copyfile(source, extra)
        else:
            source.rename(extra)
    _git(repo, "add", ".")
    _git(repo, "commit", "--quiet", "-m", "Unknown fixture")
    assert callable(loader)
    message = (
        "identity mismatch"
        if mutation == "identity-mismatch"
        else "duplicate Release Unit identity"
        if mutation == "duplicate"
        else "registered slice path"
    )
    with pytest.raises(ValueError, match=message):
        loader(repo, _git(repo, "rev-parse", "HEAD"))


@pytest.mark.parametrize(
    "loader", [load_nuget_authoring, load_first_slice_authoring]
)
def test_known_slice_ignores_dirty_python_descriptor_inventory(
    authored_tree: tuple[Path, str],
    loader: object,
) -> None:
    """The committed three-unit target survives a malformed local descriptor."""
    repo, target = authored_tree
    assert callable(loader)
    expected = loader(repo, target)
    source = repo / "src/public/lib" / PYTHON_UNIT / DESCRIPTOR_NAME
    source.write_text("invalid: dirty descriptor", encoding="utf-8")
    assert loader(repo, target) == expected


@pytest.mark.parametrize(
    ("loader", "selected"),
    [
        (load_nuget_authoring, NUGET_RELEASE_UNIT),
        (load_first_slice_authoring, FIRST_SLICE_RELEASE_UNIT),
    ],
)
def test_known_slice_does_not_require_unrelated_descriptors(
    authored_tree: tuple[Path, str],
    loader: object,
    selected: str,
) -> None:
    """Coexistence does not require unrelated units in historical targets."""
    repo, _ = authored_tree
    for unit in (FIRST_SLICE_RELEASE_UNIT, NUGET_RELEASE_UNIT, PYTHON_UNIT):
        if unit != selected:
            descriptor = repo / "src/public/lib" / unit / DESCRIPTOR_NAME
            descriptor.rename(descriptor.with_suffix(".disabled"))
    _git(repo, "add", ".")
    _git(repo, "commit", "--quiet", "-m", "Historical single-unit fixture")
    assert callable(loader)
    descriptor, _, policy = loader(repo, _git(repo, "rev-parse", "HEAD"))
    assert descriptor.release_unit == selected
    assert policy.release_unit == selected


def test_nuget_catalog_separates_quality_and_blocks_live_admission() -> None:
    """Static catalog admission does not confer live publication authority."""
    build = BUILD_DEFINITIONS["dotnet/nuget-package-v1"]
    destination = DESTINATION_DEFINITIONS[
        "nuget/github-packages-hcoona-three-v1"
    ]
    assert build.output_kinds == ("nuget-package",)
    assert build.required_native_projections == ("NuGetPackageVersion",)
    assert destination.supported_channels == ("buddy",)
    assert (
        destination.live_mutation_status == "requires-nuget-native-acceptance"
    )
    assert (
        QUALITY_DEFINITIONS[NUGET_RELEASE_QUALITY[0]].operation
        == "nuget-artifact-contents"
    )
    assert (
        QUALITY_DEFINITIONS[NUGET_RELEASE_QUALITY[1]].operation
        == "nuget-restore-build-invoke"
    )
