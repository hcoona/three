"""Small immutable Ruby repositories and bindings for native contract tests."""

# Trusted fixture commands use fixed executables and never a shell.
# ruff: noqa: S603, S607
import os
import shutil
import subprocess
from pathlib import Path

from three_workflow_delivery_v3._ruby_native import RUBY_ROOT
from three_workflow_delivery_v3.adapters.ruby import (
    RubyBuildRequest,
    RubyPackageTargetWitness,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.node_provider import ProviderBinding
from three_workflow_delivery_v3.repository.ruby_provider import RUBY_GLOBALS

ROOT = Path(__file__).resolve().parents[5]
EPOCH = 1_700_000_000
RUN_ID = 91


def git(root, *arguments, epoch=EPOCH):
    """Execute a local fixture Git operation with deterministic commit dates."""
    return subprocess.check_output(
        ("git", *arguments),
        cwd=root,
        env={
            **os.environ,
            "GIT_AUTHOR_DATE": f"@{epoch} +0000",
            "GIT_COMMITTER_DATE": f"@{epoch} +0000",
            "GIT_LFS_SKIP_SMUDGE": "1",
        },
        text=True,
        stderr=subprocess.PIPE,
    ).strip()


def commit(root, *, epoch=EPOCH):
    """Commit only the task-owned fixture repository."""
    git(root, "add", ".")
    git(
        root,
        "-c",
        "user.name=Ruby Fixture",
        "-c",
        "user.email=ruby-fixture@example.invalid",
        "commit",
        "--quiet",
        "--allow-empty",
        "-m",
        "Ruby fixture target",
        epoch=epoch,
    )
    return git(root, "rev-parse", "HEAD")


def repository(root):
    """Clone a complete locally controlled origin with current Ruby source."""
    origin = root / "origin"
    origin.mkdir(parents=True)
    for name in (*RUBY_GLOBALS,):
        path = origin / name
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / name, path)
    shutil.copytree(ROOT / RUBY_ROOT, origin / RUBY_ROOT)
    git(origin, "init", "--quiet", "--initial-branch=main")
    target = commit(origin)
    source = root / "source"
    git(root, "clone", "--quiet", "--no-local", str(origin), str(source))
    git(source, "checkout", "--quiet", "--detach")
    return source, origin, target


def binding(target, purpose="release-simulation"):
    """Construct a valid request with independent catalog/control bindings."""
    return ProviderBinding(
        "ruby-foundation",
        purpose,
        RUN_ID,
        None if purpose == "live-release" else 1,
        target,
        "ruby-provider",
        target,
        catalog_digest(),
        "sha256:" + "b" * 64,
    )


def build_request(provider):
    """Freeze qualified provider facts for a native package build."""
    return RubyBuildRequest(
        RubyPackageTargetWitness(
            provider.binding.target,
            provider.nbgv,
            provider.binding.catalog_digest,
            "sha256:" + "c" * 64,
            provider.binding.purpose,
        ),
        provider.source_input_manifest,
        provider.source_date_epoch,
        provider.native_profile,
    )
