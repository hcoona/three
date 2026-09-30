# Assertions enforce experiment invariants; invoke Python without -O.
# Console output is retained evidence. Subprocess arguments are local recipes.
# ruff: noqa: S101, T201, S603

"""Execute the bounded Windows matrix and export only experiment evidence."""

import argparse
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
args = parser.parse_args()
root = args.root.resolve()
root.mkdir(parents=True, exist_ok=False)
recipe = Path(__file__).resolve().parent
packet = root / "packet"
with zipfile.ZipFile(recipe / "packet.zip") as z:
    z.extractall(packet)
output = root / "results"
export = root / "export"
export.mkdir()
completed = []
try:
    for sdk in ["10.0.401"]:
        subprocess.run(
            [
                sys.executable,
                str(recipe / "install.py"),
                "--version",
                sdk,
                "--rid",
                "win-x64",
                "--root",
                str(root / "toolchains"),
            ],
            check=True,
            timeout=600,
        )
    for sdk, cell in [
        ("10.0.401", "complete"),
        ("10.0.401", "source-control"),
        ("10.0.401", "timestamp-control"),
    ]:
        subprocess.run(
            [
                sys.executable,
                str(recipe / "run.py"),
                "--packet",
                str(packet),
                "--output",
                str(output / sdk),
                "--dotnet",
                str(root / "toolchains" / f"sdk-{sdk}" / "dotnet.exe"),
                "--sdk",
                sdk,
                "--cell",
                cell,
            ],
            check=True,
            timeout=1200,
        )
        completed.append({"sdk": sdk, "cell": cell})
    stable = output / "10.0.401"
    full = json.loads((stable / "complete-comparison.json").read_text())
    assert full["whole_archive_equal"]
    assert full["member_records_equal"]
    assert full["binary_files_equal"]
    assert not any(
        f["contains_physical_root_literal"]
        for r in full["records"]
        for f in r["binary_files"]
    )
    equal_hash = full["records"][0]["sha256"]
    for cell in ["source-control", "timestamp-control"]:
        control = json.loads((stable / f"{cell}-comparison.json").read_text())
        assert control["records"][0]["sha256"] != equal_hash
    print(
        "Complete Windows pair and both negative controls passed.", flush=True
    )
finally:
    for item in output.rglob("*") if output.exists() else []:
        if item.is_file() and (
            "evidence" in item.relative_to(output).parts
            or "packages" in item.relative_to(output).parts
            or item.parent.name in ["10.0.300", "10.0.401"]
        ):
            target = export / "results" / item.relative_to(output)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(item, target)
    for item in (root / "toolchains").glob("*-provenance.json"):
        shutil.copyfile(item, export / item.name)
    shutil.copytree(packet, export / "packet")
    shutil.copyfile(recipe / "packet.zip", export / "packet.zip")
    (export / "campaign.json").write_text(
        json.dumps(
            {
                "completed_cells": completed,
                "planned_builds": 4,
                "prior_run_id": "36769964306",
                "prior_builds": 11,
                "cumulative_builds": 15,
                "run_id": os.environ.get("GITHUB_RUN_ID"),
                "run_attempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                "recipe_commit": os.environ.get("GITHUB_SHA"),
                "image_os": os.environ.get("ImageOS"),  # noqa: SIM112 - GitHub spelling
                "image_version": os.environ.get("ImageVersion"),  # noqa: SIM112 - GitHub spelling
            },
            indent=2,
        )
        + "\n"
    )
