# Phase 1.3 Python Backend-Helper Evidence Gate

Status: **Accepted with packaging constraints**

Date: **2026-06-05**

Decision ID: **`phase-1.3-python-backend-helper-evidence`**

Gate name: **Phase 1.3 Python backend-helper evidence gate**

Owner: **ADAPTER-PY**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                                                                                                       |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Gate status                | Passed for Phase 1.3 evidence gathering.                                                                                                                                                                                                                       |
| Decision                   | Use a thin Python keyring backend plus a `keyring` executable shim. The backend delegates to the helper by an absolute product-configured path and validates ordinary existence and executable requirements before invocation.                                 |
| Evidence scope             | Upstream `artifacts-keyring` source confirms Python keyring backend registration and non-shell helper invocation. Disposable local probes confirm `keyring` package backend discovery, loading, selection, API dispatch, and fixed absolute helper invocation. |
| Implementation may proceed | Yes for Python adapter design and later implementation. Release packaging records normal artifact digest and provenance evidence; runtime helper invocation relies on the installed product layout and standard OS/.NET filesystem guarantees.                 |
| Phase 1R routing           | Not entered. If later platform validation disproves helper discovery or invocation, dependent Python packaging work must stop and enter Phase 1R.                                                                                                              |

## Current Production Contract

The historical probe below explored helper digest, owner, and symlink checks.
The production implementation deliberately supersedes those checks with the
accepted simpler model: a backend manifest records `contractMajor`, `productId`,
`absoluteHelperPath`, and `platform`; the backend performs ordinary platform,
absolute-path, existence, file, and executable checks; and it invokes:

```text
<absolute-product-apphost> python-keyring get ...
```

On POSIX platforms, `configure python` separately creates a PATH-facing
`keyring` shim that delegates to the wheel-provided `azureauth-keyring` console
script. The shim is for uv and pip subprocess discovery and is not
helper-integrity metadata. Windows subprocess mode remains deferred until a real
`keyring.exe` launcher is available. Runtime digest, owner, ACL,
package-ownership, symlink, inode, and TOCTOU proof systems are not part of the
production contract.

## Upstream Snapshot

Reference source inspected from the local mirror of
[microsoft/artifacts-keyring][artifacts-keyring-repo]. The local mirror was clean
and resolved to commit
[`213574f8850ae99073118c1f35a7d02384e41b05`][artifacts-keyring-commit],
described as `213574f`.

Commands used to identify the snapshot:

```bash
git -C /workspace/public/artifacts-keyring --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-keyring --no-pager remote -v
git -C /workspace/public/artifacts-keyring --no-pager describe --tags --always --dirty
git -C /workspace/public/artifacts-keyring --no-pager status --short
```

Results:

```text
HEAD: 213574f8850ae99073118c1f35a7d02384e41b05
origin: https://github.com/microsoft/artifacts-keyring
version description: 213574f
status --short: no output
```

## Evidence Sources

Upstream source and documentation inspected:

- [README package purpose and discovery][readme-purpose]
    - `artifacts-keyring` is a thin wrapper around `artifacts-credprovider`.
    - The package is a Python `keyring` extension that pip and twine can use once
      installed.
- [README requirements and platform notes][readme-requirements]
    - Python support is documented for Python 3.9 or higher.
    - Platform-specific wheels are documented for Windows and macOS; Linux uses
      an sdist with the default non-platform-specific .NET 8 credential provider
      unless an external self-contained provider is configured.
- [README external provider override][readme-provider-path]
    - `ARTIFACTS_KEYRING_CREDENTIALPROVIDER_PATH` can point to an external
      credential-provider executable.
    - The referenced executable must already have executable permissions.
- [setup.cfg entry point][setup-entry-point]
    - Registers `ArtifactsKeyringBackend` under the `keyring.backends` entry-point
      group.
- [setup.py provider release URLs][setup-provider-urls]
    - Defines the `artifacts-credprovider` GitHub release URL base and default
      Net8 archive names used by the build-time provider acquisition path.
- [setup.py provider download and extraction][setup-download]
    - Opens the selected provider archive URL and extracts ZIP or tar.gz content
      into the destination directory passed by the build script.
    - This function does not perform source-inspected SHA-256 or signature
      verification before extraction.
- [setup.py runtime selection][setup-runtime]
    - Selects platform-specific provider payloads based on runtime identifier or
      environment variables.
- [setup.py executable bit][setup-executable]
    - Sets executable permissions on the packaged non-Windows provider binary
      when the file exists.
- [ArtifactsKeyringBackend host filtering][backend-hosts]
    - Supports Azure Artifacts package hostnames and returns no credential for
      unsupported hosts.
- [ArtifactsKeyringBackend keyring methods][backend-methods]
    - Implements `get_credential` and `get_password` and refuses writes and
      deletes by raising `NotImplementedError`, allowing other backends to handle
      unrelated entries.
- [CredentialProvider path selection][provider-path]
    - Selects either the environment-provided provider path or the bundled
      package path.
    - Uses `dotnet exec <dll>` only for DLL helper payloads; otherwise invokes
      the executable directly.
- [CredentialProvider subprocess invocation][provider-popen]
    - Invokes the helper through an argv list with `stdin` closed and stdout and
      stderr captured; it does not use shell command construction.
    - Requests JSON output from the helper.
- [CredentialProvider missing-helper check][provider-missing]
    - Raises during initialization when the selected helper path is not an
      existing regular file.
- [CredentialProvider subprocess and JSON failure handling][provider-failure]
    - After helper execution, raises hard failures for non-zero exit,
      undecodable stdout, or stdout that cannot be parsed as JSON.
- [CredentialProvider public-feed short circuit][provider-public-feed]
    - Returns no credential when a non-upload endpoint appears accessible without
      authentication.

## Local Prototype

A disposable local probe was created under
`.copilot-scratch/phase-1.3-python-probe` and removed after execution. It did not
modify product source. The probe explored a stronger owner/hash/symlink proof
model, but that model was not adopted as a production contract. The accepted
runtime scope is fixed absolute invocation plus ordinary existence and executable
checks. The historical probe exercised four concerns:

1. Python package metadata can expose a backend through the `keyring.backends`
   entry-point group and `EntryPoint.load()` can import the referenced backend
   object when the distribution is importable in the active Python environment.
2. A backend can validate a product-owned helper manifest before invoking the
   helper.
3. A backend can invoke a fixed absolute helper path with a non-shell argv list
   matching the product's `keyring-helper-v2` shape only after validation passes.
4. Local Linux validation fails closed before helper execution for relative helper
   paths, symlink helper paths, missing owner-execute mode, wrong product ID, and
   digest mismatch.

The prototype is intentionally Linux-scoped and records only what that discarded
stronger model did in the disposable probe. Same-user ownership races,
symlink/TOCTOU adversaries, privileged attackers, hostile filesystems, and
runtime digest enforcement are outside the supported product model. The probe
does not define production behavior or prove Windows ACL, Authenticode, macOS
signing/notarization, package-install, or real credential behavior.

Reproduction commands, run from the repository root:

```bash
cd /workspace/three-workspaces/azureauth-credprovider
rm -rf .copilot-scratch/phase-1.3-python-probe
mkdir -p \
  .copilot-scratch/phase-1.3-python-probe/bin \
  .copilot-scratch/phase-1.3-python-probe/fake_backend-1.0.dist-info
cat > .copilot-scratch/phase-1.3-python-probe/bin/keyring-helper-probe <<'PY'
#!/usr/bin/env python3
from pathlib import Path
import os
import sys

EXPECTED = [
    "python-keyring",
    "get",
    "--protocol-version",
    "2",
    "--service",
    "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
    "--username",
    "user",
    "--mode",
    "creds",
]

marker = os.environ.get("AZUREAUTH_PROBE_MARKER")
if marker:
    Path(marker).write_text("invoked\n")

if sys.argv[1:] != EXPECTED:
    print(f"unexpected argv: {sys.argv[1:]!r}", file=sys.stderr)
    raise SystemExit(64)

print("probe-user")
print("probe-password")
PY
chmod 700 .copilot-scratch/phase-1.3-python-probe/bin/keyring-helper-probe
cat > .copilot-scratch/phase-1.3-python-probe/fake_backend.py <<'PY'
class ProbeBackend:
    priority = 9

    def get_credential(self, service, username):
        return None
PY
cat > .copilot-scratch/phase-1.3-python-probe/fake_backend-1.0.dist-info/METADATA <<'EOF'
Metadata-Version: 2.1
Name: fake-backend
Version: 1.0
EOF
cat > .copilot-scratch/phase-1.3-python-probe/fake_backend-1.0.dist-info/entry_points.txt <<'EOF'
[keyring.backends]
azureauth_probe = fake_backend:ProbeBackend
EOF
python - <<'PY'
from pathlib import Path
import hashlib, json, os

root = Path('.copilot-scratch/phase-1.3-python-probe').resolve()
helper = root / 'bin' / 'keyring-helper-probe'
config = {
    'product_id': 'azureauth-credprovider',
    'helper_path': str(helper),
    'helper_sha256': hashlib.sha256(helper.read_bytes()).hexdigest(),
    'expected_uid': os.getuid(),
    'expected_manifest_uid': os.getuid(),
}
(root / 'azureauth-keyring-helper.json').write_text(json.dumps(config, indent=2) + '\n')
PY
cat > .copilot-scratch/phase-1.3-python-probe/probe.py <<'PY'
from __future__ import annotations

import hashlib
import importlib.metadata as metadata
import json
import os
from pathlib import Path
import stat
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
CONFIG_PATH = ROOT / "azureauth-keyring-helper.json"
MARKER_PATH = ROOT / "helper-invoked.marker"
SERVICE = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
PRODUCT_ID = "azureauth-credprovider"
HARD_FAILURE_EXIT = 70


def discover_and_load_entry_point() -> tuple[bool, str | None, bool, str | None]:
    entry_points = [
        entry_point
        for distribution in metadata.distributions(path=[str(ROOT)])
        for entry_point in distribution.entry_points
        if entry_point.group == "keyring.backends" and entry_point.name == "azureauth_probe"
    ]
    if not entry_points:
        return False, None, False, None
    sys.path.insert(0, str(ROOT))
    try:
        loaded = entry_points[0].load()
    finally:
        sys.path.remove(str(ROOT))
    return True, entry_points[0].value, loaded.__name__ == "ProbeBackend", repr(loaded)


def validate(config: dict[str, object]) -> tuple[dict[str, bool], Path, str]:
    helper_path = Path(str(config["helper_path"]))
    manifest_stat = CONFIG_PATH.stat()
    helper_exists = helper_path.exists()
    helper_lstat = helper_path.lstat() if helper_exists or helper_path.is_symlink() else None
    helper_mode = helper_lstat.st_mode if helper_lstat else 0
    helper_is_regular = bool(helper_lstat and stat.S_ISREG(helper_lstat.st_mode))
    actual_sha256 = hashlib.sha256(helper_path.read_bytes()).hexdigest() if helper_is_regular else ""
    checks = {
        "helper_absolute": helper_path.is_absolute(),
        "helper_exists": helper_exists,
        "helper_regular": helper_is_regular,
        "helper_not_symlink": bool(helper_lstat and not stat.S_ISLNK(helper_lstat.st_mode)),
        "helper_executable_by_owner": bool(helper_mode & stat.S_IXUSR),
        "helper_uid_matches_process": bool(helper_lstat and helper_lstat.st_uid == os.getuid()),
        "manifest_uid_matches_process": manifest_stat.st_uid == os.getuid(),
        "manifest_uid_matches_record": manifest_stat.st_uid == config["expected_manifest_uid"],
        "manifest_product_id_matches": config["product_id"] == PRODUCT_ID,
        "manifest_expected_uid_matches_process": config["expected_uid"] == os.getuid(),
        "sha256_matches_manifest": actual_sha256 == config["helper_sha256"],
    }
    return checks, helper_path, actual_sha256


def run_case(case_name: str, config: dict[str, object]) -> dict[str, object]:
    if MARKER_PATH.exists():
        MARKER_PATH.unlink()
    entry_point_discovered, entry_point_value, entry_point_loaded, entry_point_loaded_repr = (
        discover_and_load_entry_point()
    )
    checks, helper_path, actual_sha256 = validate(config)
    validation_passed = entry_point_discovered and entry_point_loaded and all(checks.values())
    result: dict[str, object] = {
        "case": case_name,
        "python_version": os.sys.version.split()[0],
        "entry_point_discovered": entry_point_discovered,
        "entry_point_value": entry_point_value,
        "entry_point_loaded": entry_point_loaded,
        "entry_point_loaded_repr": entry_point_loaded_repr,
        "pre_execution_checks": checks,
        "actual_sha256": actual_sha256,
        "validation_passed_before_invocation": validation_passed,
        "backend_exit": 0 if validation_passed else HARD_FAILURE_EXIT,
        "helper_invoked": False,
        "helper_exit": None,
        "stdout": "",
        "stderr": "",
    }
    if not validation_passed:
        result["failure_reason"] = "pre-execution helper validation failed"
        return result
    argv = [
        str(helper_path),
        "python-keyring",
        "get",
        "--protocol-version",
        "2",
        "--service",
        SERVICE,
        "--username",
        "user",
        "--mode",
        "creds",
    ]
    env = os.environ.copy()
    env["AZUREAUTH_PROBE_MARKER"] = str(MARKER_PATH)
    completed = subprocess.run(
        argv,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        check=False,
        env=env,
    )
    result.update(
        {
            "non_shell_argv": argv,
            "helper_exit": completed.returncode,
            "helper_invoked": MARKER_PATH.exists(),
            "stdout": completed.stdout,
            "stderr": completed.stderr,
        }
    )
    return result


base_config = json.loads(CONFIG_PATH.read_text())
relative_path_config = dict(
    base_config,
    helper_path=".copilot-scratch/phase-1.3-python-probe/bin/keyring-helper-probe",
)
symlink = ROOT / "bin" / "keyring-helper-symlink"
if symlink.exists() or symlink.is_symlink():
    symlink.unlink()
symlink.symlink_to(ROOT / "bin" / "keyring-helper-probe")
symlink_config = dict(base_config, helper_path=str(symlink))
non_executable = ROOT / "bin" / "keyring-helper-non-executable"
non_executable.write_bytes((ROOT / "bin" / "keyring-helper-probe").read_bytes())
non_executable.chmod(0o600)
non_executable_config = dict(
    base_config,
    helper_path=str(non_executable),
    helper_sha256=hashlib.sha256(non_executable.read_bytes()).hexdigest(),
)
wrong_product_config = dict(base_config, product_id="wrong-product")
tampered_config = dict(base_config)
tampered_config["helper_sha256"] = "0" * 64
results = [
    run_case("valid_manifest", base_config),
    run_case("relative_path", relative_path_config),
    run_case("symlink_path", symlink_config),
    run_case("non_executable_mode", non_executable_config),
    run_case("wrong_product_manifest", wrong_product_config),
    run_case("tampered_digest", tampered_config),
]
print(json.dumps(results, indent=4, sort_keys=True))
valid_ok = (
    results[0]["backend_exit"] == 0
    and results[0]["helper_invoked"] is True
    and results[0]["helper_exit"] == 0
    and results[0]["stderr"] == ""
    and results[0]["stdout"] == "probe-user\nprobe-password\n"
)
negative_ok = all(
    result["backend_exit"] == HARD_FAILURE_EXIT
    and result["helper_invoked"] is False
    and result["helper_exit"] is None
    for result in results[1:]
)
load_ok = all(result["entry_point_loaded"] is True for result in results)
raise SystemExit(0 if valid_ok and negative_ok and load_ok else 1)
PY
python --version
python .copilot-scratch/phase-1.3-python-probe/probe.py
probe_status=$?
echo "probe_exit=$probe_status"
rm -rf .copilot-scratch/phase-1.3-python-probe
cleanup_status=0
if [ -e .copilot-scratch/phase-1.3-python-probe ]; then
  echo cleanup_exists=yes
  cleanup_status=1
else
  echo cleanup_exists=no
fi
if [ "$probe_status" -eq 0 ] && [ "$cleanup_status" -eq 0 ]; then
  exit 0
fi
exit 1
```

The helper stub behavior was intentionally narrow: it accepted only the expected
`python-keyring get --protocol-version 2 ... --mode creds` argv suffix, exited 64
with stderr for any mismatch, and otherwise wrote exactly `probe-user\n` followed
by `probe-password\n` to stdout with empty stderr. The positive assertion required
backend exit 0, helper invocation, helper exit 0, empty stderr, and that exact
creds-mode stdout payload. The helper wrote a marker file only when it started;
every negative case checks that the marker is absent.

Generated probe metadata, fake backend module, and manifest contents from the
successful run. The transcript below replaces the repository root path with
`<repo>` for readability.

```text
fake_backend.py:
class ProbeBackend:
    priority = 9

    def get_credential(self, service, username):
        return None

fake_backend-1.0.dist-info/METADATA:
Metadata-Version: 2.1
Name: fake-backend
Version: 1.0

fake_backend-1.0.dist-info/entry_points.txt:
[keyring.backends]
azureauth_probe = fake_backend:ProbeBackend

azureauth-keyring-helper.json:
{
  "product_id": "azureauth-credprovider",
  "helper_path": "<repo>/.copilot-scratch/phase-1.3-python-probe/bin/keyring-helper-probe",
  "helper_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
  "expected_uid": 1002,
  "expected_manifest_uid": 1002
}
```

Observed transcript. The positive case returned backend status 0, invoked the
helper, recorded `helper_exit` 0, left stderr empty, and recorded the exact
creds-mode stdout payload `probe-user\nprobe-password\n`. The negative cases
changed one dimension each where possible: relative path, symlink path, missing
owner-execute mode, wrong product ID, and tampered digest. Each negative case
returned the expected hard-failure backend status 70, left `helper_exit` null,
and left `helper_invoked` false.

```text
Python 3.14.3
[
    {
        "actual_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
        "backend_exit": 0,
        "case": "valid_manifest",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "helper_exit": 0,
        "helper_invoked": true,
        "non_shell_argv": [
            "<repo>/.copilot-scratch/phase-1.3-python-probe/bin/keyring-helper-probe",
            "python-keyring",
            "get",
            "--protocol-version",
            "2",
            "--service",
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
            "--username",
            "user",
            "--mode",
            "creds"
        ],
        "pre_execution_checks": {
            "helper_absolute": true,
            "helper_executable_by_owner": true,
            "helper_exists": true,
            "helper_not_symlink": true,
            "helper_regular": true,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": true,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": true
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "probe-user\nprobe-password\n",
        "validation_passed_before_invocation": true
    },
    {
        "actual_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
        "backend_exit": 70,
        "case": "relative_path",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "failure_reason": "pre-execution helper validation failed",
        "helper_exit": null,
        "helper_invoked": false,
        "pre_execution_checks": {
            "helper_absolute": false,
            "helper_executable_by_owner": true,
            "helper_exists": true,
            "helper_not_symlink": true,
            "helper_regular": true,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": true,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": true
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "",
        "validation_passed_before_invocation": false
    },
    {
        "actual_sha256": "",
        "backend_exit": 70,
        "case": "symlink_path",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "failure_reason": "pre-execution helper validation failed",
        "helper_exit": null,
        "helper_invoked": false,
        "pre_execution_checks": {
            "helper_absolute": true,
            "helper_executable_by_owner": true,
            "helper_exists": true,
            "helper_not_symlink": false,
            "helper_regular": false,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": true,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": false
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "",
        "validation_passed_before_invocation": false
    },
    {
        "actual_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
        "backend_exit": 70,
        "case": "non_executable_mode",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "failure_reason": "pre-execution helper validation failed",
        "helper_exit": null,
        "helper_invoked": false,
        "pre_execution_checks": {
            "helper_absolute": true,
            "helper_executable_by_owner": false,
            "helper_exists": true,
            "helper_not_symlink": true,
            "helper_regular": true,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": true,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": true
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "",
        "validation_passed_before_invocation": false
    },
    {
        "actual_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
        "backend_exit": 70,
        "case": "wrong_product_manifest",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "failure_reason": "pre-execution helper validation failed",
        "helper_exit": null,
        "helper_invoked": false,
        "pre_execution_checks": {
            "helper_absolute": true,
            "helper_executable_by_owner": true,
            "helper_exists": true,
            "helper_not_symlink": true,
            "helper_regular": true,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": false,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": true
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "",
        "validation_passed_before_invocation": false
    },
    {
        "actual_sha256": "d753d2a64641999e718b0983dc630a7ef938e96d341cab902055b1e509442da3",
        "backend_exit": 70,
        "case": "tampered_digest",
        "entry_point_discovered": true,
        "entry_point_loaded": true,
        "entry_point_loaded_repr": "<class 'fake_backend.ProbeBackend'>",
        "entry_point_value": "fake_backend:ProbeBackend",
        "failure_reason": "pre-execution helper validation failed",
        "helper_exit": null,
        "helper_invoked": false,
        "pre_execution_checks": {
            "helper_absolute": true,
            "helper_executable_by_owner": true,
            "helper_exists": true,
            "helper_not_symlink": true,
            "helper_regular": true,
            "helper_uid_matches_process": true,
            "manifest_expected_uid_matches_process": true,
            "manifest_product_id_matches": true,
            "manifest_uid_matches_process": true,
            "manifest_uid_matches_record": true,
            "sha256_matches_manifest": false
        },
        "python_version": "3.14.3",
        "stderr": "",
        "stdout": "",
        "validation_passed_before_invocation": false
    }
]
probe_exit=0
cleanup_exists=no
```

One earlier disposable probe attempt used the wrong `importlib.metadata` API for
path-scoped entry-point inspection on Python 3.14 and failed with
`AttributeError: 'EntryPoint' object has no attribute 'path'`. The final probe
used `importlib.metadata.distributions(path=[...])`, loaded
`fake_backend:ProbeBackend` through `EntryPoint.load()`, and passed.

### Keyring API Discovery Prototype

The first local check showed that `keyring` was not available in the repository's
ambient Python environment:

```bash
cd /workspace/three-workspaces/azureauth-credprovider
python - <<'PY'
import sys
print(sys.version)
try:
    import keyring
    print('keyring_available=True')
    print('keyring_version=' + getattr(keyring, '__version__', 'unknown'))
    import keyring.backend
    print('keyring_backend_module=' + keyring.backend.__file__)
except Exception as exc:
    print('keyring_available=False')
    print(type(exc).__name__ + ': ' + str(exc))
    raise SystemExit(1)
PY
```

Result:

```text
3.14.3 (main, Mar 10 2026, 18:18:50) [Clang 21.1.4 ]
keyring_available=False
ModuleNotFoundError: No module named 'keyring'
exit status: 1
```

To avoid adding project dependencies, a second disposable prototype installed
`keyring` only into a scratch virtual environment under
`.copilot-scratch/phase-1.3-keyring-api-probe`, set `TMPDIR` and the pip cache
inside that scratch tree, and removed the tree after execution. This directly
exercised the installed `keyring` package path used by import-mode tools:

1. `importlib.metadata.entry_points(group="keyring.backends")` could see the
   fake distribution's entry point.
2. `keyring.backend.get_all_keyring()` loaded the fake backend from the
   `keyring.backends` entry-point group.
3. `keyring.get_keyring()` selected the fake backend by priority.
4. `keyring.get_credential()` and `keyring.get_password()` dispatched through
   the selected fake backend.

Reproduction commands, run from the repository root:

```bash
cd /workspace/three-workspaces/azureauth-credprovider
rm -rf .copilot-scratch/phase-1.3-keyring-api-probe
mkdir -p \
  .copilot-scratch/phase-1.3-keyring-api-probe/tmp \
  .copilot-scratch/phase-1.3-keyring-api-probe/cache \
  .copilot-scratch/phase-1.3-keyring-api-probe/plugin/fake_keyring_backend-1.0.dist-info
python -m venv .copilot-scratch/phase-1.3-keyring-api-probe/.venv
TMPDIR="$PWD/.copilot-scratch/phase-1.3-keyring-api-probe/tmp" \
  .copilot-scratch/phase-1.3-keyring-api-probe/.venv/bin/python \
  -m pip install \
  --cache-dir .copilot-scratch/phase-1.3-keyring-api-probe/cache \
  --disable-pip-version-check \
  --quiet \
  'keyring==25.7.0'
install_status=$?
echo "keyring_install_exit=$install_status"
if [ "$install_status" -eq 0 ]; then
  TMPDIR="$PWD/.copilot-scratch/phase-1.3-keyring-api-probe/tmp" \
    .copilot-scratch/phase-1.3-keyring-api-probe/.venv/bin/python \
    - <<'PY'
import importlib.metadata as metadata
print("keyring_install_version=" + metadata.version("keyring"))
PY
fi
if [ "$install_status" -ne 0 ]; then
  rm -rf .copilot-scratch/phase-1.3-keyring-api-probe
  if [ -e .copilot-scratch/phase-1.3-keyring-api-probe ]; then
    echo cleanup_exists=yes
  else
    echo cleanup_exists=no
  fi
  exit "$install_status"
fi
cat > .copilot-scratch/phase-1.3-keyring-api-probe/plugin/fake_keyring_backend.py <<'PY'
from keyring.backend import KeyringBackend
from keyring.credentials import SimpleCredential

class AzureAuthProbeBackend(KeyringBackend):
    priority = 99

    def get_password(self, service, username):
        return f"password-for:{service}:{username}"

    def get_credential(self, service, username):
        return SimpleCredential(
            username or "probe-user",
            self.get_password(service, username or "probe-user"),
        )

    def set_password(self, service, username, password):
        raise NotImplementedError("probe backend is read-only")

    def delete_password(self, service, username):
        raise NotImplementedError("probe backend is read-only")
PY
cat > .copilot-scratch/phase-1.3-keyring-api-probe/plugin/fake_keyring_backend-1.0.dist-info/METADATA <<'EOF'
Metadata-Version: 2.1
Name: fake-keyring-backend
Version: 1.0
EOF
cat > .copilot-scratch/phase-1.3-keyring-api-probe/plugin/fake_keyring_backend-1.0.dist-info/entry_points.txt <<'EOF'
[keyring.backends]
azureauth_probe = fake_keyring_backend:AzureAuthProbeBackend
EOF
cat > .copilot-scratch/phase-1.3-keyring-api-probe/probe.py <<'PY'
from __future__ import annotations

import importlib.metadata as metadata
import json
import os
import sys

import keyring
import keyring.backend

service = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
username = "user"

discovered = sorted(
    f"{entry_point.name}={entry_point.value}"
    for entry_point in metadata.entry_points(group="keyring.backends")
    if entry_point.name == "azureauth_probe"
)
loaded_backends = keyring.backend.get_all_keyring()
loaded_backend_types = [
    f"{backend.__class__.__module__}:{backend.__class__.__name__}"
    for backend in loaded_backends
]
selected = keyring.get_keyring()
credential = keyring.get_credential(service, username)
password = keyring.get_password(service, username)
expected_password = f"password-for:{service}:{username}"
credential_matches_expected = (
    credential is not None
    and credential.username == username
    and credential.password == expected_password
)
result = {
    "python_executable": sys.executable,
    "python_version": sys.version.split()[0],
    "keyring_module": keyring.__file__,
    "keyring_version": metadata.version("keyring"),
    "sys_path_contains_plugin": os.environ["AZUREAUTH_PROBE_PLUGIN"] in sys.path,
    "entry_point_discovered_by_importlib_metadata": discovered,
    "loaded_backend_types_from_keyring": loaded_backend_types,
    "selected_backend_type": (
        f"{selected.__class__.__module__}:{selected.__class__.__name__}"
    ),
    "selected_backend_priority": selected.priority,
    "keyring_get_credential": None
    if credential is None
    else {"username": credential.username, "password": credential.password},
    "keyring_get_credential_matches_expected": credential_matches_expected,
    "keyring_get_password": password,
    "keyring_get_password_matches_expected": password == expected_password,
}
print(json.dumps(result, indent=2, sort_keys=True))
expected_type = "fake_keyring_backend:AzureAuthProbeBackend"
raise SystemExit(
    0
    if expected_type in loaded_backend_types
    and result["selected_backend_type"] == expected_type
    and credential_matches_expected
    and password == expected_password
    else 1
)
PY
AZUREAUTH_PROBE_PLUGIN="$PWD/.copilot-scratch/phase-1.3-keyring-api-probe/plugin" \
PYTHONPATH="$PWD/.copilot-scratch/phase-1.3-keyring-api-probe/plugin" \
TMPDIR="$PWD/.copilot-scratch/phase-1.3-keyring-api-probe/tmp" \
  .copilot-scratch/phase-1.3-keyring-api-probe/.venv/bin/python \
  .copilot-scratch/phase-1.3-keyring-api-probe/probe.py
probe_status=$?
echo "probe_exit=$probe_status"
rm -rf .copilot-scratch/phase-1.3-keyring-api-probe
cleanup_status=0
if [ -e .copilot-scratch/phase-1.3-keyring-api-probe ]; then
  echo cleanup_exists=yes
  cleanup_status=1
else
  echo cleanup_exists=no
fi
if [ "$probe_status" -eq 0 ] && [ "$cleanup_status" -eq 0 ]; then
  exit 0
fi
exit 1
```

Observed transcript. The transcript below keeps the scratch paths exactly as
printed by the run.

```text
keyring_install_exit=0
keyring_install_version=25.7.0
{
  "entry_point_discovered_by_importlib_metadata": [
    "azureauth_probe=fake_keyring_backend:AzureAuthProbeBackend"
  ],
  "keyring_get_credential": {
    "password": "password-for:https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/:user",
    "username": "user"
  },
  "keyring_get_credential_matches_expected": true,
  "keyring_get_password": "password-for:https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/:user",
  "keyring_get_password_matches_expected": true,
  "keyring_module": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.3-keyring-api-probe/.venv/lib/python3.14/site-packages/keyring/__init__.py",
  "keyring_version": "25.7.0",
  "loaded_backend_types_from_keyring": [
    "fake_keyring_backend:AzureAuthProbeBackend",
    "keyring.backends.SecretService:Keyring",
    "keyring.backends.chainer:ChainerBackend",
    "keyring.backends.fail:Keyring"
  ],
  "python_executable": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/phase-1.3-keyring-api-probe/.venv/bin/python",
  "python_version": "3.14.3",
  "selected_backend_priority": 99,
  "selected_backend_type": "fake_keyring_backend:AzureAuthProbeBackend",
  "sys_path_contains_plugin": true
}
probe_exit=0
cleanup_exists=no
```

This keyring API probe does not prove pip or twine's end-to-end behavior. It
does directly prove that an installed `keyring` 25.7.0 package can discover,
load, select, and dispatch to a backend registered through `keyring.backends`
when that backend is importable in the active Python environment. The
disposable install command pinned `keyring==25.7.0`, exited 0, and the probe
recorded the installed package version before exercising the API.

## Backend Discovery Decision

Backend discovery is feasible and evidence-backed for an importable backend. The
upstream package registers `ArtifactsKeyringBackend` in `setup.cfg` under
`keyring.backends`. Local probes confirmed both path-scoped Python distribution
metadata loading through `EntryPoint.load()` and direct `keyring` 25.7.0 backend
discovery, loading, selection, and API dispatch through `keyring.backend` and
the top-level `keyring` API.

Accepted implementation expectation:

- The product must ship a Python backend package that registers through
  `keyring.backends`.
- `configure python` and `doctor` must validate the exact Python environment that
  runs pip or twine, not only the globally installed primary CLI.
- pipx, tox/nox, active virtual environments, and isolated CI environments remain
  explicit bootstrap targets because import-mode tools load backends from their
  own Python environment.

## Fixed External Helper Invocation Decision

Fixed external helper invocation is feasible. Upstream `artifacts-keyring`
already invokes the credential provider through a subprocess argv list and does
not require importing the large credential implementation into the keyring
backend process. The product should keep that isolation pattern but replace the
reference provider's ad hoc command shape with the versioned
`keyring-helper-v2` contract from `mid-level-design.md`:

```text
<helper> python-keyring get
  --protocol-version 2
  --service <service>
  [--username <username>]
  [--mode password|creds]
```

The helper path must come from product-owned configuration written by the
configuration manager. The backend must not discover the helper through ambient
`PATH` for import mode and must not construct shell command strings.

## Helper Validation Decision

The backend uses an absolute helper path written by product configuration. Before
invocation it checks that the path exists, is a regular file, and is executable
where the platform exposes executable mode. It then invokes the helper through
an argument list rather than a shell command.

Release artifacts continue to record SHA-256 and provenance evidence. Those are
build and release controls, not a runtime attempt to re-prove the installed
filesystem, account, or operating system on every helper invocation.

## Environment Coverage Constraints

Evidence supports the architecture but not every release platform execution
scenario. Coverage accepted by this gate:

| Area                      | Evidence                                                                                                                                                                                 |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Python keyring discovery  | Upstream `keyring.backends` registration plus local Python 3.14 probes: metadata `EntryPoint.load()` and direct `keyring` 25.7.0 backend load/selection/API dispatch for a fake backend. |
| pip and twine import mode | Upstream README and backend shape support import-mode keyring usage; exact pip and twine tool-version execution remains later validation.                                                |
| uv and pip subprocess     | Requires the separate `keyring` executable shim from design; upstream `artifacts-keyring` is not itself evidence that a global shim is present.                                          |
| Windows                   | Upstream supports Windows wheels and executable naming, but this Linux session did not execute Windows path or PowerShell install flows.                                                 |
| Linux                     | Source and local probe executed on Linux; release packages still need runtime-dependency and mode-bit validation.                                                                        |
| macOS                     | Upstream documents macOS wheel constraints; this session did not execute macOS signing, notarization, or keyring import validation.                                                      |
| CI                        | Non-interactive helper behavior is design-supported; release validation must prove fail-closed behavior with no persistent secrets by default.                                           |

## No-Credential Versus Hard-Failure Mapping

Accepted mapping for implementation:

| Condition                                            | Backend result                                                                                                                                           |
| ---------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Unsupported host or non-Azure Artifacts service URL  | Return keyring no-credential behavior so other backends or the host tool can continue.                                                                   |
| Public non-upload endpoint needs no credential       | Return no credential only after an explicit, timeout-bounded, redaction-safe policy decision. Avoid unbounded network checks in keyring backend startup. |
| Malformed Azure Artifacts endpoint                   | Hard failure with redacted diagnostics.                                                                                                                  |
| Missing helper path                                  | Hard failure.                                                                                                                                            |
| Helper path is not the configured absolute path      | Hard failure.                                                                                                                                            |
| Helper protocol-version mismatch                     | Hard failure.                                                                                                                                            |
| Helper exits non-zero or emits invalid response      | Hard failure with redacted diagnostics.                                                                                                                  |
| No credential available for a supported private feed | Return keyring-compatible no credential only when the helper explicitly reports no credential; otherwise preserve hard failures.                         |

This mapping differs intentionally from unsupported-host fallback. Installation
and protocol failures remain hard failures so package tools do not silently
change behavior.

## Release-Packaging Follow-ups

Before Python release packaging can lock, later phases must record or implement:

1. The product-configured absolute helper path and package layout.
2. Normal platform packaging/signing decisions and release artifact integrity
   evidence.
3. A wheel and sdist policy that does not download unverified remote helper
   payloads during release install or build.
4. Bootstrap procedures for active virtual environments, pipx-managed twine,
   tox/nox environments, isolated CI, and subprocess-mode `keyring` shim
   placement.
5. `doctor` checks for backend importability, configured helper availability,
   selected tool mode, and PATH order for the subprocess shim.
6. Tests that prove protocol stdout contains only password or username/password
   response data and that diagnostics are redacted and sent away from stdout.

## Validation and Checks

Commands run from repository root:

```bash
git -C /workspace/public/artifacts-keyring --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-keyring --no-pager remote -v
git -C /workspace/public/artifacts-keyring --no-pager describe --tags --always --dirty
git -C /workspace/public/artifacts-keyring --no-pager status --short
```

Results:

```text
HEAD: 213574f8850ae99073118c1f35a7d02384e41b05
origin: https://github.com/microsoft/artifacts-keyring
version description: 213574f
status --short: no output
```

Disposable prototype validation (historical stronger-model evidence, not a
production runtime contract):

```text
Ambient Python keyring import check: ModuleNotFoundError, exit 1
Python metadata backend entry-point discovery: true
Python entry-point load/import: fake_backend:ProbeBackend -> true
Disposable venv keyring package install: keyring==25.7.0, install exit 0, installed version 25.7.0
Keyring API backend entry-point discovery: azureauth_probe=fake_keyring_backend:AzureAuthProbeBackend
keyring.backend.get_all_keyring loaded fake backend: true
keyring.get_keyring selected fake backend by priority 99: true
keyring.get_credential dispatched through fake backend with expected username/password: true
keyring.get_password dispatched through fake backend with expected password: true
keyring API probe exit: 0
valid manifest pre-execution helper validation: true
absolute helper path positive check: true
helper existence positive check: true
regular file positive check: true
non-symlink positive check: true
owner executable bit positive check: true
helper uid matches process uid positive check: true
manifest uid/product-id positive checks: true
SHA-256 manifest comparison before invocation: true
valid manifest helper invocation: true
valid manifest helper process exit: 0
valid manifest helper stderr: empty
valid manifest stdout for creds mode: exactly "probe-user\nprobe-password\n"
relative path negative: helper_absolute=false, backend_exit=70, helper_invoked=false, helper_exit=null
symlink path negative: helper_not_symlink=false, helper_regular=false, backend_exit=70, helper_invoked=false, helper_exit=null
non-executable mode negative: helper_executable_by_owner=false, backend_exit=70, helper_invoked=false, helper_exit=null
wrong product manifest negative: manifest_product_id_matches=false, backend_exit=70, helper_invoked=false, helper_exit=null
tampered digest negative: sha256_matches_manifest=false, backend_exit=70, helper_invoked=false, helper_exit=null
UID owner mismatch negative: unproven in this session because changing file owner requires privileged operations
stderr: empty
cleanup check: .copilot-scratch/phase-1.3-python-probe did not exist after cleanup
cleanup check: .copilot-scratch/phase-1.3-keyring-api-probe did not exist after cleanup
```

Markdown validation:

```bash
pnpm exec prettier --write src/private/app/azureauth-credprovider/docs/phase-1.3-python-backend-helper-evidence.md
pnpm exec prettier --check src/private/app/azureauth-credprovider/docs/phase-1.3-python-backend-helper-evidence.md
pnpm exec markdownlint-cli2 src/private/app/azureauth-credprovider/docs/phase-1.3-python-backend-helper-evidence.md
```

Results: all three commands exited 0.

## Risks and Follow-ups

- Windows and macOS behavior was source-inspected only. Platform-specific helper
  path-with-spaces and virtual-environment behavior require later validation.
- Upstream `artifacts-keyring` permits an environment variable to override the
  provider path. The product may expose an explicit diagnostic override, but the
  default backend uses product configuration rather than an ambient override.
- The upstream build path downloads helper artifacts during setup. This gate does
  not accept unverified downloads for release packaging.
- Public-feed probing can introduce network latency and ambiguous failure modes.
  The product should avoid broad network probes in backend startup and should
  bound any validation requests tightly.
- The local prototype exercised stronger helper-path and digest checks than the
  product requires. Runtime implementation should use the configured absolute path,
  basic file/executable checks, and normal platform process-launch behavior.
- The `keyring` executable shim is required for uv and pip subprocess mode but was
  not implemented by this gate; it remains a later adapter artifact with separate
  PATH-order diagnostics.

## Affected Requirements and Designs

- `requirements.md`: Python integration requirements 1 through 6 remain valid.
- `high-level-design.md`: Python backend and `keyring` shim shapes are
  evidence-supported for the scoped evidence above.
- `mid-level-design.md`: `keyring-helper-v2`, fixed helper invocation, fail-closed
  protocol behavior, and ordinary configured-path validation are accepted.
- `project-breakdown.md`: Phase 1.3 exit criterion is satisfied with a pass
  decision for evidence gathering, while release packaging lock remains dependent
  on the follow-ups in this record.

[artifacts-keyring-commit]: https://github.com/microsoft/artifacts-keyring/commit/213574f8850ae99073118c1f35a7d02384e41b05
[artifacts-keyring-repo]: https://github.com/microsoft/artifacts-keyring
[backend-hosts]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/__init__.py#L19-L58
[backend-methods]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/__init__.py#L61-L80
[provider-failure]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/plugin.py#L125-L147
[provider-missing]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/plugin.py#L59-L60
[provider-path]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/plugin.py#L18-L58
[provider-popen]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/plugin.py#L96-L147
[provider-public-feed]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/src/artifacts_keyring/plugin.py#L62-L80
[readme-provider-path]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/README.md#L82-L117
[readme-purpose]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/README.md#L1-L15
[readme-requirements]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/README.md#L26-L60
[setup-download]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/setup.py#L97-L118
[setup-entry-point]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/setup.cfg#L44-L47
[setup-executable]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/setup.py#L158-L171
[setup-provider-urls]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/setup.py#L21-L24
[setup-runtime]: https://github.com/microsoft/artifacts-keyring/blob/213574f8850ae99073118c1f35a7d02384e41b05/setup.py#L36-L94
