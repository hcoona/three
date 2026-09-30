# Assertions enforce experiment invariants; invoke Python without -O.
# Console output is retained evidence. Subprocess arguments are local recipes.
# ruff: noqa: S101, T201, S603

"""Run bounded frozen package experiments without modifying production code."""

import argparse
import hashlib
import json
import os
import platform
import shutil
import subprocess
import time
import zipfile
from pathlib import Path
from typing import Any

PROJECT = (
    "src/public/lib/hcoona-release-smoke-github-packages/"
    "hcoona-release-smoke-github-packages.csproj"
)


def digest(data: bytes) -> str:
    """Hash the original bytes without normalization."""
    return hashlib.sha256(data).hexdigest()


def save(path: Path, value: object) -> None:
    """Retain a structured observation."""
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def execute(
    argv: list[str], cwd: Path, env: dict[str, str], evidence: Path, label: str
) -> str:
    """Run a bounded native command and retain its invocation and output."""
    save(
        evidence / f"{label}-command.json",
        {"argv": [str(x) for x in argv], "cwd": str(cwd)},
    )
    start = time.monotonic()
    with (evidence / f"{label}.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(
            argv,
            cwd=cwd,
            env=env,
            stdout=log,
            stderr=subprocess.STDOUT,
            timeout=600,
            check=False,
        )
    save(
        evidence / f"{label}-result.json",
        {"exit_code": result.returncode, "seconds": time.monotonic() - start},
    )
    if result.returncode:
        message = f"{label} failed: {evidence / (label + '.log')}"
        raise RuntimeError(message)
    return (evidence / f"{label}.log").read_text(encoding="utf-8")


def archive_record(path: Path) -> dict[str, Any]:
    """Record original archive bytes and ordered member metadata."""
    content = path.read_bytes()
    with zipfile.ZipFile(path) as z:
        entries = [
            {
                "name": i.filename,
                "sha256": digest(z.read(i.filename)),
                "size": i.file_size,
                "timestamp": i.date_time,
                "compression": i.compress_type,
                "attributes": i.external_attr,
            }
            for i in z.infolist()
        ]
    return {
        "filename": path.name,
        "size": len(content),
        "sha256": digest(content),
        "entries": entries,
    }


# Keep the experimental sequence together for comparison with the adapter.
def build(  # noqa: C901, PLR0913, PLR0917, PLR0915
    packet: Path,
    output: Path,
    dotnet: Path,
    sdk: str,
    label: str,
    paths: bool,  # noqa: FBT001 - recorded matrix factor
    stamp: bool,  # noqa: FBT001 - recorded matrix factor
    ordinal: int,
    control: str | None = None,
) -> dict[str, Any]:
    """Build once in an independent root with recorded experimental inputs."""
    root = (
        output
        / label
        / ("first-root" if ordinal == 0 else "second-distinct-longer-root")
    ).resolve()
    root.mkdir(parents=True, exist_ok=False)
    stage = root / "stage"
    shutil.copytree(packet / "source", stage)
    manifest = json.loads((packet / "manifest.json").read_text())
    for name, expected in manifest["source_inputs"]:
        assert "sha256:" + digest((stage / name).read_bytes()) == expected
    witness_bytes = (packet / "witness.json").read_bytes()
    assert digest(witness_bytes) == manifest["witness_sha256"]
    witness = json.loads(witness_bytes)
    (root / "provenance.json").write_bytes(witness_bytes)
    global_file = stage / "global.json"
    global_json = json.loads(global_file.read_text())
    global_json["sdk"] = {
        "version": sdk,
        "rollForward": "disable",
        "allowPrerelease": "-" in sdk,
    }
    global_file.write_text(json.dumps(global_json, indent=2) + "\n")
    modified = {"global.json": digest(global_file.read_bytes())}
    if control == "source":
        source_file = next((stage / Path(PROJECT).parent).glob("*.cs"))
        original = source_file.read_text()
        # Change the observable marker, preserving the source style.
        assert '"hcoona-release-smoke-github-packages"' in original
        source_file.write_text(
            original.replace(
                '"hcoona-release-smoke-github-packages"',
                '"hcoona-release-smoke-github-packages-negative-control"',
            ),
            newline="",
        )
        modified[source_file.relative_to(stage).as_posix()] = digest(
            source_file.read_bytes()
        )
    source_time = manifest["epoch"] + (ordinal + 1) * 86400
    for file in stage.rglob("*"):
        if file.is_file():
            os.utime(file, (source_time, source_time))
    (root / "temp").mkdir()
    (root / "cli-home").mkdir()
    allowed = {
        "PATH",
        "SYSTEMROOT",
        "WINDIR",
        "COMSPEC",
        "PATHEXT",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "HOME",
        "USERPROFILE",
        "APPDATA",
        "LOCALAPPDATA",
        "SSL_CERT_FILE",
        "SSL_CERT_DIR",
    }
    env = {k: v for k, v in os.environ.items() if k.upper() in allowed}
    env.update(
        {
            "DOTNET_ROOT": str(dotnet.parent),
            "DOTNET_CLI_HOME": str(root / "cli-home"),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_NOLOGO": "1",
            "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
            "IGNORE_GITHUB_REF": "true",
            "NUGET_PACKAGES": str(root / "nuget-packages"),
            "NUGET_HTTP_CACHE_PATH": str(root / "http-cache"),
            "NUGET_PLUGINS_CACHE_PATH": str(root / "plugins"),
            "TMP": str(root / "temp"),
            "TEMP": str(root / "temp"),
            "TMPDIR": str(root / "temp"),
        }
    )
    facts = witness["nbgv"]["native"]
    properties = {
        "WorkflowDeliveryFrozenBuild": "true",
        "WorkflowDeliveryNuGetVersion": facts["nugetPackageVersion"],
        "WorkflowDeliveryAssemblyVersion": facts["assemblyVersion"],
        "WorkflowDeliveryFileVersion": facts["assemblyFileVersion"],
        "WorkflowDeliveryInformationalVersion": facts[
            "assemblyInformationalVersion"
        ],
        "WorkflowDeliveryWitnessPath": str(root / "provenance.json"),
        "BaseIntermediateOutputPath": str(root / "frozen-obj") + "/",
        "MSBuildProjectExtensionsPath": str(root / "frozen-obj") + "/",
        "RepositoryCommit": witness["target"],
        "SourceRevisionId": witness["target"],
        "Configuration": "Release",
        "RestoreLockedMode": "true",
    }
    if paths:
        properties["PathMap"] = str(root) + "=/_/"
    if stamp:
        properties["DeterministicTimestamp"] = str(
            manifest["epoch"] + (86400 if control == "timestamp" else 0)
        )
    evidence = root / "evidence"
    evidence.mkdir()
    save(
        evidence / "inputs.json",
        {
            "sdk": sdk,
            "properties": properties,
            "modified_source_files": modified,
            "source_mtime": source_time,
            "control": control,
            "os": platform.platform(),
            "root": str(root),
            "witness_sha256": digest(witness_bytes),
            "source_manifest_sha256": digest(
                (packet / "manifest.json").read_bytes()
            ),
        },
    )
    actual = execute(
        [str(dotnet), "--version"], stage, env, evidence, "sdk"
    ).strip()
    assert actual == sdk, (actual, sdk)
    execute([str(dotnet), "--info"], stage, env, evidence, "sdk-info")
    execute(
        [str(dotnet), "nuget", "--version"],
        stage,
        env,
        evidence,
        "nuget-version",
    )
    project = stage / PROJECT
    args = [f"-property:{k}={v}" for k, v in properties.items()]
    lock = project.parent / "packages.lock.json"
    before_lock = lock.read_bytes()
    for operation, extra in [
        ("restore", ["--configfile", str(stage / "nuget.config")]),
        ("build", ["--no-restore"]),
        (
            "pack",
            ["--no-restore", "--no-build", "--output", str(root / "packages")],
        ),
    ]:
        execute(
            [
                str(dotnet),
                operation,
                str(project),
                *extra,
                *args,
                "-bl:" + str(evidence / f"{operation}.binlog"),
            ],
            stage,
            env,
            evidence,
            operation,
        )
        assert (evidence / f"{operation}.binlog").is_file()
    assert lock.read_bytes() == before_lock
    for name, expected in manifest["source_inputs"]:
        if name not in modified:
            assert (
                "sha256:" + digest((stage / name).read_bytes()) == expected
            ), name
    packages = list((root / "packages").iterdir())
    assert len(packages) == 1
    assert packages[0].suffix == ".nupkg"
    with zipfile.ZipFile(packages[0]) as z:
        assert z.read("workflow-delivery/provenance.json") == witness_bytes
    record = archive_record(packages[0])
    record.update(
        {
            "root": str(root),
            "sdk": sdk,
            "label": label,
            "control": control,
            "lock_sha256": digest(before_lock),
            "binary_files": [],
        }
    )
    for f in sorted((project.parent / "bin/Release/net10.0").glob("*")):
        if f.suffix in (".dll", ".pdb", ".xml"):
            data = f.read_bytes()
            physical = (
                str(root).encode() in data
                or str(root).encode("utf-16-le") in data
            )
            record["binary_files"].append(
                {
                    "name": f.name,
                    "sha256": digest(data),
                    "size": len(data),
                    "contains_physical_root_literal": physical,
                }
            )
            shutil.copyfile(f, evidence / f.name)
    save(evidence / "artifact.json", record)
    print(
        json.dumps(
            {
                "label": label,
                "ordinal": ordinal,
                "sha256": record["sha256"],
                "size": record["size"],
            }
        ),
        flush=True,
    )
    return record


def main() -> None:
    """Execute one bounded matrix cell and retain the comparison."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--sdk", required=True)
    parser.add_argument(
        "--cell",
        choices=[
            "baseline",
            "paths-only",
            "timestamp-only",
            "complete",
            "source-control",
            "timestamp-control",
        ],
        required=True,
    )
    options = parser.parse_args()
    options.output.mkdir(parents=True, exist_ok=True)
    cell = options.cell
    control = (
        cell.removesuffix("-control") if cell.endswith("-control") else None
    )
    paths = cell in [
        "paths-only",
        "complete",
        "source-control",
        "timestamp-control",
    ]
    stamp = cell in [
        "timestamp-only",
        "complete",
        "source-control",
        "timestamp-control",
    ]
    records = []
    try:
        for ordinal in range(1 if control else 2):
            records.append(
                build(
                    options.packet.resolve(),
                    options.output.resolve(),
                    options.dotnet.resolve(),
                    options.sdk,
                    cell,
                    paths,
                    stamp,
                    ordinal,
                    control,
                )
            )
        result = {"cell": cell, "records": records}
        if not control:
            result["whole_archive_equal"] = (
                Path(records[0]["root"]) / "packages" / records[0]["filename"]
            ).read_bytes() == (
                Path(records[1]["root"]) / "packages" / records[1]["filename"]
            ).read_bytes()
            result["member_records_equal"] = (
                records[0]["entries"] == records[1]["entries"]
            )
            result["binary_files_equal"] = (
                records[0]["binary_files"] == records[1]["binary_files"]
            )
        save(options.output / f"{cell}-comparison.json", result)
    except Exception as exc:
        save(
            options.output / f"{cell}-failure.json",
            {"error": str(exc), "completed_records": records},
        )
        raise


if __name__ == "__main__":
    main()
