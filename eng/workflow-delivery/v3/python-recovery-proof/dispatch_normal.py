# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Only fixed Git/gh argv invoke already admitted local tools.
# Load versioning only after execution-source admission.
# ruff: noqa: E501, PLC0415, PLR2004, S101, S607

"""One reviewed dispatch for an explicitly selected, reserved campaign slot."""

import json
import os
import re
import ssl
import subprocess
from datetime import UTC, datetime
from pathlib import Path

from campaign import Campaign, digest, gate
from normal_operator import Operator

CODE = Path(__file__).parent
SCENARIO = os.environ["WDV3_SCENARIO"]
ATTEMPT = os.environ["WDV3_ATTEMPT"]
assert re.fullmatch(r"0[1-5]", SCENARIO)
assert re.fullmatch(r"0[1-4]", ATTEMPT)
ROOT = CODE / "scenarios" / SCENARIO / "attempts" / ATTEMPT
BINDING = json.loads((ROOT / "execution-binding.json").read_bytes())
TARGET = BINDING["target"]
CHECKOUT = Path("/workspace/three-workspaces/design-workflows")


def main() -> None:
    """Execute the bound caller stage once."""
    assert __debug__
    assert not os.environ.get("PYTHONOPTIMIZE")
    binding = BINDING
    assert binding["scenario"] == SCENARIO
    assert binding["attempt"] == ATTEMPT
    assert re.fullmatch("[0-9a-f]{40}", TARGET)
    assert binding["target"] == binding["tooling"]
    assert digest(CODE / "protocol.md") == binding["protocol_sha256"]
    for name, expected in binding["files_sha256"].items():
        assert Path(name).name == name
        assert digest(CODE / name) == expected
    assert (
        subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=CHECKOUT, text=True
        ).strip()
        == TARGET
    )
    assert (
        subprocess.check_output(
            ["git", "rev-parse", "HEAD^{tree}"], cwd=CHECKOUT, text=True
        ).strip()
        == binding["tree"]
    )
    assert not subprocess.check_output(
        ["git", "status", "--porcelain"], cwd=CHECKOUT, text=True
    ).strip()
    ci = json.loads((ROOT / "postmerge-ci.json").read_bytes())
    assert ci["headSha"] == TARGET
    assert ci["conclusion"] == "success"
    facts = json.loads((ROOT / "nbgv.json").read_bytes())
    assert facts["GitCommitId"] == TARGET
    assert facts["PublicRelease"] is True
    assert facts["SemVer2"] == binding["semver2"]
    from nbgv_python.versioning import normalize_version_field

    assert (
        normalize_version_field(facts["SemVer2"], field="SemVer2")
        == binding["version"]
    )
    version = binding["version"]
    assert binding["filenames"] == [
        f"hcoona_release_smoke_python-{version}-py3-none-any.whl",
        f"hcoona_release_smoke_python-{version}.tar.gz",
    ]
    governance_path = (
        CHECKOUT
        / ".github/workflow-delivery/governance/hcoona-release-smoke-python-testpypi.json"
    )
    assert (
        digest(governance_path)
        == binding["governance_sha256"]
        == "1831282b13059eb101b9ef095c6917f6f82a0aa6972e1d130dab8d19c49d0a86"
    )
    governance = json.loads(governance_path.read_bytes())
    assert governance["state"] == "ready"
    assert governance["live_enabled"] is True
    assert governance["operation-profile-digest"] == binding["profile"]
    assert (
        datetime.fromisoformat(governance["inspected-at"])
        <= datetime.now(UTC)
        < datetime.fromisoformat(governance["expires-at"])
    )
    assert (
        os.environ.get("SSL_CERT_FILE") == "/etc/ssl/certs/ca-certificates.crt"
    )
    trust = json.loads((ROOT / "local-ca-preparation.json").read_bytes())
    assert digest(os.environ["SSL_CERT_FILE"]) == trust["bundle_sha256"]
    context = ssl.create_default_context()
    assert context.verify_mode == ssl.CERT_REQUIRED
    assert context.check_hostname
    assert context.cert_store_stats()["x509_ca"] > 0
    gate(ROOT, "independent-execution-gate.json", binding, "execution")
    Campaign(CODE).reserve(SCENARIO, ATTEMPT)
    operator = Operator(ROOT / "operation", TARGET)
    with operator.deadline():
        current = operator.get(
            "repos/hcoona/three/git/ref/heads/main", "initial-main"
        )
        assert current["object"]["sha"] == TARGET, (
            "accepted main moved; no dispatch"
        )
        status, _, _ = operator.dispatch()
        assert status == 204, "uncertain dispatch remains spent"
        operator.ledger["dispatch_response"] = status
        operator.save()


if __name__ == "__main__":
    main()
