"""Four original native builds shared by byte and fresh-Attempt regressions."""

from __future__ import annotations

# Native commands use fixed arguments, isolated test directories and no shell.
# ruff: noqa: S603, S607
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass, replace
from pathlib import Path

import pytest
from three_workflow_delivery_v3.adapters import dotnet as native
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.descriptors import NUGET_POLICY_PATH
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_PROJECT_ROOT,
    DotnetProviderResult,
)


def sha256(content: bytes) -> str:
    """Hash original bytes without normalization."""
    return "sha256:" + hashlib.sha256(content).hexdigest()


def write_json(path: Path, value: object) -> None:
    """Retain only explicit, non-secret native evidence."""
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


@dataclass(frozen=True)
class NativeNugetReproducibility:
    """Original production outputs and the native facts that produced them."""

    requests: dict[str, native.DotnetBuildRequest]
    results: dict[str, native.DotnetBuildResult]
    provider: DotnetProviderResult
    evidence: Path
    timestamp: int


def _observed_build(  # noqa: C901 - one bounded native observation lifecycle
    request, scratch, label, ordinal, timestamp
):
    """Observe actual native inputs and outputs without replacing mechanics."""
    evidence = request.evidence_directory
    calls = []
    real_run = native.run_native
    real_temporary = tempfile.TemporaryDirectory
    observed_stage = None
    mtime = timestamp + (ordinal + 1) * 86400
    binaries = evidence / "binaries"

    def observe(command, cwd, environment=None, *, diagnostics=None):
        nonlocal observed_stage
        environment = dict(environment or {})
        # These isolated caches deliberately differ for each real build.
        for name, directory in (
            ("NUGET_PACKAGES", "nuget-cache"),
            ("NUGET_HTTP_CACHE_PATH", "http-cache"),
            ("NUGET_PLUGINS_CACHE_PATH", "plugins"),
            ("DOTNET_CLI_HOME", "cli-home"),
        ):
            environment[name] = str(scratch / directory)
        if cwd.name == "stage" and observed_stage is None:
            observed_stage = cwd
            for relative in request.declared_inputs:
                os.utime(cwd / relative, (mtime, mtime))
        calls.append(
            {
                "argv": list(command),
                "cwd": str(cwd),
                "cache": environment["NUGET_PACKAGES"],
            }
        )
        output = real_run(command, cwd, environment, diagnostics=diagnostics)
        if cwd.name == "stage" and command[1] == "pack":
            packed = Path(command[command.index("--output") + 1])
            for archive in packed.glob("*.nupkg"):
                shutil.copyfile(archive, evidence / archive.name)
        if cwd.name == "stage" and command[1] in {"build", "pack"}:
            binaries.mkdir(exist_ok=True)
            for path in (
                cwd / DOTNET_PROJECT_ROOT / "bin/Release/net10.0"
            ).glob("*"):
                if path.suffix in {".dll", ".pdb"}:
                    shutil.copyfile(path, binaries / path.name)
            write_json(
                evidence / "staged-inputs.json",
                {
                    "root": str(cwd.parent),
                    "source_mtime": mtime,
                    "files": {
                        name: {
                            "sha256": sha256((cwd / name).read_bytes()),
                            "mtime": (cwd / name).stat().st_mtime,
                        }
                        for name in request.declared_inputs
                    },
                },
            )
        return output

    def temporary(**kwargs):
        return real_temporary(dir=scratch, **kwargs)

    scratch.mkdir(parents=True)
    with pytest.MonkeyPatch.context() as patch:
        patch.setattr(native, "run_native", observe)
        patch.setattr(native, "TemporaryDirectory", temporary)
        if label == "timestamp-control":
            patch.setattr(
                native, "_target_timestamp", lambda _request: timestamp + 86400
            )
        result = native.build_dotnet_package(request)
    (evidence / result.manifest.basename).write_bytes(result.package)
    (evidence / "witness.json").write_bytes(result.witness)
    write_json(evidence / "commands.json", calls)
    write_json(
        evidence / "inputs.json",
        {
            "source_inputs": request.source_input_manifest,
            "witness_sha256": sha256(result.witness),
            "toolchain": result.toolchain,
            "target": request.witness.target,
            "target_timestamp": timestamp,
            "control": label if label.endswith("control") else None,
        },
    )
    script = Path(__file__).parent / "fixtures/nuget-repro/inspect-debug.ps1"
    decoded = subprocess.run(
        (
            "pwsh",
            "-NoProfile",
            "-File",
            str(script),
            "-EvidenceRoot",
            str(binaries),
        ),
        check=True,
        capture_output=True,
        text=True,
        timeout=60,
    ).stdout
    write_json(evidence / "debug-paths.json", json.loads(decoded))
    return result


def prepare_reproducibility(frozen_source, tmp_path_factory):
    """Build one pair and two controls; never contact a package destination."""
    original, provider, provider_root = frozen_source
    scratch = tmp_path_factory.mktemp("nuget-repro-work")
    evidence = Path(
        os.environ.get(
            "WDV3_NUGET_REPRO_EVIDENCE",
            str(tmp_path_factory.getbasetemp() / "nuget-repro-evidence"),
        )
    ).resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    target = original.witness.target
    timestamp = int(
        subprocess.check_output(
            ("git", "show", "--no-patch", "--format=%ct", target),
            cwd=original.source_root,
            text=True,
        ).strip()
    )
    witness = replace(
        original.witness,
        catalog_digest=catalog_digest(),
        control_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/control-identity",
                "identity": f"workflow-delivery-v3:{target}",
            }
        ),
        purpose="live-release",
    )
    requests, results = {}, {}
    for ordinal, label in enumerate(
        ("first", "second-longer-root", "source-control", "timestamp-control")
    ):
        source = scratch / f"source-{label}"
        subprocess.run(
            (
                "git",
                "clone",
                "--quiet",
                "--no-checkout",
                "--shared",
                str(original.source_root),
                str(source),
            ),
            check=True,
            env={**os.environ, "GIT_LFS_SKIP_SMUDGE": "1"},
        )
        for name in (*original.declared_inputs, NUGET_POLICY_PATH):
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original.source_root / name, path)
        manifest = original.source_input_manifest
        if label == "source-control":
            path = source / DOTNET_PROJECT_ROOT / "Smoke.cs"
            content = path.read_bytes()
            before = b'"hcoona-release-smoke-github-packages"'
            assert content.count(before) == 1
            path.write_bytes(
                content.replace(before, b'"nuget-repro-negative-control"')
            )
            name = path.relative_to(source).as_posix()
            manifest = tuple(
                (p, sha256(path.read_bytes()) if p == name else digest)
                for p, digest in manifest
            )
        request = replace(
            original,
            source_root=source,
            source_input_manifest=manifest,
            witness=witness,
            evidence_directory=evidence / label,
        )
        requests[label] = request
        results[label] = _observed_build(
            request, scratch / f"build-{label}", label, ordinal, timestamp
        )
    shutil.copytree(provider_root / "provider", evidence / "provider")
    write_json(
        evidence / "campaign.json",
        {
            "build_count": len(results),
            "target": target,
            "target_timestamp": timestamp,
            "original_sha256": {
                label: result.manifest.sha256
                for label, result in results.items()
            },
        },
    )
    return NativeNugetReproducibility(
        requests, results, provider, evidence, timestamp
    )
