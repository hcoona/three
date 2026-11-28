"""Check bounded Workflow Delivery models and their named negative controls."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / "src/private/app/workflow-delivery/docs/design/models"
TLC_INVARIANT_VIOLATED = 12
CHECKS = (
    ("CiComponentComposition", "CiComponentComposition", None),
    ("ReleaseComponentProtocol", "ReleaseComponentProtocol", None),
    ("ReleaseSystemRefinement", "ReleaseRefinementPython", None),
    ("ReleaseSystemRefinement", "ReleaseRefinementAssets", None),
    ("OfficialAnchorComposition", "OfficialAnchorProtocol", None),
    (
        "CiComponentComposition",
        "CiBridgeClosureCounterexample",
        "EndpointLocalRunnableClosureSuffices",
    ),
    (
        "CiComponentComposition",
        "CiFlatJoinCounterexample",
        "CheckOnlyJoinSuffices",
    ),
    (
        "PublicationWindow",
        "OperationCountCounterexample",
        "CountBoundsAuthority",
    ),
)


def _tlc(module: str, config: str) -> subprocess.CompletedProcess[str]:
    print(f"Checking {config} ({module})", flush=True)
    return subprocess.run(
        [
            sys.executable,
            str(ROOT / "eng/scripts/tla.py"),
            "tlc",
            "-workers",
            "1",
            "-config",
            f"{config}.cfg",
            f"{module}.tla",
        ],
        cwd=MODELS,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )


def _check_models() -> bool:
    for module, config, rejected in CHECKS:
        run = _tlc(module, config)
        if rejected is None:
            if run.returncode == 0:
                print("Verified.", flush=True)
                continue
        elif (
            run.returncode == TLC_INVARIANT_VIOLATED
            and f"Invariant {rejected} is violated" in run.stdout
        ):
            print(f"Rejected {rejected} as expected.", flush=True)
            continue
        print(run.stdout, file=sys.stderr)
        print(
            f"Unexpected TLC result for {config}: {run.returncode}",
            file=sys.stderr,
        )
        return False
    return True


def compare_callers(root: Path) -> int:
    """Compare controlled production calls against fresh TLC observations."""
    for module, prefix, minimum_cases in (
        ("PublicationWindowOracle", "WD-WINDOW", 10),
        ("VerifierReadGateOracle", "WD-GATE", 6),
        ("CiJoinOracle", "WD-CI", 16),
        ("CiSelectionOracle", "WD-SELECT", 16),
    ):
        run = _tlc(module, module)
        if run.returncode:
            print(run.stdout, file=sys.stderr)
            return 1
        rows = sorted(
            json.loads(line).removeprefix(prefix + "|")
            for line in run.stdout.splitlines()
            if line.startswith(f'"{prefix}|')
        )
        cases = {row.partition("|")[0] for row in rows}
        if len(cases) < minimum_cases or len(cases) != len(rows):
            print(f"{module}: missing or repeated TLC cases", file=sys.stderr)
            return 1
        (root / f"{module}.txt").write_text(
            "\n".join(rows) + "\n", encoding="utf-8"
        )
        print(f"{module}: {len(rows)} specification rows", flush=True)
    return subprocess.run(
        [
            "dotnet",
            "test",
            "--project",
            "tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj",
            "-p:RestoreLockedMode=true",
            "--filter",
            "TestCategory=DeliveryTlcConformance",
            "--minimum-expected-tests",
            "4",
        ],
        cwd=ROOT,
        env={**os.environ, "WORKFLOW_DELIVERY_TLC_ORACLES": str(root)},
        check=False,
    ).returncode


def main() -> int:
    """Run the locked checker, accepting only the intended counterexamples."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--conformance-only", action="store_true")
    if not parser.parse_args().conformance_only and not _check_models():
        return 1
    with tempfile.TemporaryDirectory(prefix="delivery-oracles-") as directory:
        return compare_callers(Path(directory))


if __name__ == "__main__":
    sys.exit(main())
