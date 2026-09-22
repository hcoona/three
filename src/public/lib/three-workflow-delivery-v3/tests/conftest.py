"""Isolate fixture repositories and own native-tool temporary files."""

from __future__ import annotations

# Trusted native fixtures invoke the pinned SDK and Git without a shell.
# ruff: noqa: S603, S607
import os
import shutil
import subprocess
from pathlib import Path
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.adapters.dotnet import (
    BUILD_DEFINITION,
    DotnetBuildRequest,
    DotnetBuildResult,
    DotnetPackageTargetWitness,
    build_dotnet_package,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_PROJECT_ROOT,
    DOTNET_RELEASE_UNIT,
    NativeNuGetHelper,
    provide_dotnet_repository_facts,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
    ProviderBinding,
)

if TYPE_CHECKING:
    from collections.abc import Iterator


@pytest.fixture(scope="session", autouse=True)
def isolate_native_tool_temporary_files(
    tmp_path_factory: pytest.TempPathFactory,
) -> Iterator[None]:
    """Include native subprocess scratch and caches in pytest retention."""
    temporary = tmp_path_factory.mktemp("native-tools")
    with pytest.MonkeyPatch.context() as environment:
        for name in ("TMPDIR", "TMP", "TEMP"):
            environment.setenv(name, str(temporary))
        yield


@pytest.fixture(scope="session", autouse=True)
def isolate_hook_repository_environment() -> Iterator[None]:
    """Clear Git's repository-local variables before fixture Git commands.

    Git exports these variables to hooks. In a linked worktree, inherited
    GIT_DIR can redirect a temporary fixture commit into the real repository
    and recursively invoke its hooks. Ask Git for its documented local set;
    individual tests can still inject ambient variables with monkeypatch.
    """
    names = subprocess.run(
        ("git", "rev-parse", "--local-env-vars"),
        check=True,
        capture_output=True,
        text=True,
    ).stdout.splitlines()
    with pytest.MonkeyPatch.context() as environment:
        for name in names:
            environment.delenv(name, raising=False)
        yield


@pytest.fixture(scope="session")
def native_helper(
    tmp_path_factory: pytest.TempPathFactory,
) -> NativeNuGetHelper:
    """Build trusted test tooling once, outside any publication operation."""
    root = Path(__file__).resolve().parents[5]
    project = (
        root
        / "src/private/app/workflow-delivery-v3-dotnet-provider"
        / "WorkflowDeliveryV3DotnetProvider.csproj"
    )
    evidence = tmp_path_factory.mktemp("dotnet-helper")
    subprocess.run(
        (
            "dotnet",
            "build",
            str(project),
            "--configuration",
            "Release",
            f"-bl:{evidence / 'helper.binlog'}",
        ),
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return NativeNuGetHelper(
        project.parent
        / "bin/Release/net10.0/WorkflowDeliveryV3DotnetProvider.dll"
    )


@pytest.fixture(scope="session")
def frozen_package(
    native_helper: NativeNuGetHelper,
    tmp_path_factory: pytest.TempPathFactory,
) -> tuple[DotnetBuildResult, Path]:
    """Prepare shared native inputs; consumers own all subsequent writes."""
    repo = Path(__file__).resolve().parents[5]
    root = tmp_path_factory.mktemp("dotnet-native")
    source = root / "source"
    subprocess.run(
        ("git", "clone", "--quiet", "--no-local", str(repo), str(source)),
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
    )
    for path in (repo / DOTNET_PROJECT_ROOT).iterdir():
        if path.is_file():
            destination = source / DOTNET_PROJECT_ROOT / path.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, destination)
    subprocess.run(("git", "add", DOTNET_PROJECT_ROOT), cwd=source, check=True)
    subprocess.run(
        (
            "git",
            "-c",
            "user.name=Native Test",
            "-c",
            "user.email=native-test@example.invalid",
            "commit",
            "--quiet",
            "--no-verify",
            "--allow-empty",
            "-m",
            "Exercise the proposed native smoke target",
        ),
        cwd=source,
        check=True,
    )
    subprocess.run(
        ("git", "checkout", "--quiet", "--detach"),
        cwd=source,
        env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        check=True,
    )
    target = subprocess.check_output(
        ("git", "rev-parse", "HEAD"), cwd=source, text=True
    ).strip()
    binding = ProviderBinding(
        "native-test",
        "release-simulation",
        1,
        1,
        target,
        "dotnet-provider",
        target,
        catalog_digest(),
        "sha256:" + "c" * 64,
    )
    provider = provide_dotnet_repository_facts(
        source,
        binding,
        CheckoutMaterialization(0, credentials_persisted=False),
        helper=native_helper,
        evidence_directory=root / "provider",
        dependency_directory=root / "provider-packages",
    )
    assert provider.checkout.head == target
    assert provider.checkout.ancestry_complete is True
    assert provider.checkout.tags_complete is True
    assert provider.checkout.credentials_persisted is False
    facts = provider.nbgv
    inputs = provider.source_input_manifest
    witness = DotnetPackageTargetWitness(
        facts.git_commit_id,
        DOTNET_RELEASE_UNIT,
        facts,
        BUILD_DEFINITION,
        "sha256:" + "a" * 64,
        "sha256:" + "b" * 64,
        "release-simulation",
    )
    result = build_dotnet_package(
        DotnetBuildRequest(
            source,
            tuple(path for path, _ in inputs),
            inputs,
            witness,
            native_helper,
            root / "build",
        )
    )
    return result, root
