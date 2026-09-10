"""Selected NuGet authoring and independent npm coexistence contracts."""

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
    FIRST_SLICE_RELEASE_UNIT,
    NUGET_GOVERNANCE_PATH,
    NUGET_PACKAGE,
    NUGET_POLICY_PATH,
    NUGET_RELEASE_QUALITY,
    NUGET_RELEASE_UNIT,
    load_first_slice_authoring,
    load_nuget_authoring,
    load_release_policy,
)

REPOSITORY = Path(__file__).resolve().parents[6]


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
    """Commit both admitted units without invoking their native toolchains."""
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
    _git(tmp_path, "init", "--quiet")
    _git(tmp_path, "config", "user.name", "Workflow Delivery Test")
    _git(tmp_path, "config", "user.email", "workflow-delivery@example.invalid")
    _git(tmp_path, "add", ".")
    _git(tmp_path, "commit", "--quiet", "-m", "Fixture")
    return tmp_path, _git(tmp_path, "rev-parse", "HEAD")


def test_nuget_authoring_and_npm_select_independent_units(
    authored_tree: tuple[Path, str],
) -> None:
    """Each unit selects its own package, policy, and quality obligations."""
    repo, target = authored_tree
    nuget, quality, policy = load_nuget_authoring(repo, target)
    npm, _, npm_policy = load_first_slice_authoring(repo, target)
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
def test_known_slice_does_not_admit_unknown_or_misplaced_descriptor(
    authored_tree: tuple[Path, str],
    loader: object,
) -> None:
    """Known-unit discovery rejects unrelated authoring in the target tree."""
    repo, _ = authored_tree
    source = (
        repo
        / f"src/public/lib/{NUGET_RELEASE_UNIT}"
        / "workflow-delivery.release-unit.yml"
    )
    extra = repo / "src/unknown/workflow-delivery.release-unit.yml"
    extra.parent.mkdir(parents=True)
    extra.write_text(
        source.read_text(encoding="utf-8").replace(
            NUGET_RELEASE_UNIT, "unknown-unit"
        ),
        encoding="utf-8",
    )
    _git(repo, "add", ".")
    _git(repo, "commit", "--quiet", "-m", "Unknown fixture")
    assert callable(loader)
    with pytest.raises(ValueError, match="registered slice path"):
        loader(repo, _git(repo, "rev-parse", "HEAD"))


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
