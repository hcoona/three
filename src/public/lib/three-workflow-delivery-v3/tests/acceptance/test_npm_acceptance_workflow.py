"""Bounded Actions entry wiring, not approval or native acceptance proof."""

# ruff: noqa: D103, PLR2004, S603

from __future__ import annotations

import json
import os
import shlex
import shutil
import subprocess
import sys
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.canonical import canonicalize

ROOT = Path(__file__).resolve().parents[6]
WORKFLOW = (
    ROOT / ".github/workflows/workflow-delivery-v3-native-npm-acceptance.yml"
)
CHECKOUT = "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1"
UV = "astral-sh/setup-uv@20cfd1bf945f4377ade1205e4dbc17946fc9a30d"
MISE = "jdx/mise-action@3c2e0cf82a5b2e5249f0d3635a4d83d0ae861518"
PNPM = "pnpm/action-setup@0977fd99725f1db4007ccb2928dbb4e90d06cc86"
UPLOAD = "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"
PACKAGE = "three-workflow-delivery-v3"
MODULE = "three_workflow_delivery_v3.acceptance"
AUDIT_FILES = {
    "platform.json",
    "evidence/request.json",
    "evidence/fixture.tgz",
    "evidence/profile-match.json",
    "evidence/command-started",
    "evidence/result.json",
}


def _document():
    document = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))
    assert isinstance(document, dict)
    return document


def _steps():
    return _document()["jobs"]["probe"]["steps"]


def _step(identity):
    matches = [
        step
        for step in _steps()
        if step.get("id") == identity or step.get("uses") == identity
    ]
    assert len(matches) == 1
    return matches[0]


def _shell(script, environment, directory):
    bash = shutil.which("bash")
    assert bash is not None
    assert "${{" not in script
    return subprocess.run(
        [bash, "--noprofile", "--norc", "-euo", "pipefail", "-c", script],
        cwd=directory,
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=15,
    )


def _executable(path, body):
    path.write_text(f"#!{sys.executable}\n{body}", encoding="utf-8")
    path.chmod(0o755)


def test_entry_requires_exact_manual_main_identity_and_confirmation():
    document = _document()
    triggers = document.get("on", document.get(True))
    assert set(triggers) == {"workflow_dispatch"}
    inputs = triggers["workflow_dispatch"]["inputs"]
    assert set(inputs) == {"request_json", "authorized_disposable"}
    assert inputs["request_json"]["type"] == "string"
    assert inputs["request_json"]["required"] is True
    assert "default" not in inputs["request_json"]
    assert inputs["authorized_disposable"]["type"] == "boolean"
    assert inputs["authorized_disposable"]["required"] is True
    assert inputs["authorized_disposable"]["default"] is False
    assert "prior approval" in inputs["authorized_disposable"]["description"]
    assert set(document["jobs"]) == {"probe"}
    job = document["jobs"]["probe"]
    assert "||" not in job["if"]
    assert {term.strip() for term in job["if"].split("&&")} == {
        "github.event_name == 'workflow_dispatch'",
        "github.repository == 'hcoona/three'",
        "github.actor_id == '712433'",
        "github.ref == 'refs/heads/main'",
        "github.run_attempt == 1",
        "inputs.authorized_disposable == true",
    }
    assert job["runs-on"] == "ubuntu-24.04"
    assert job["timeout-minutes"] == 15
    assert document["concurrency"] == {
        "group": "wdv3-native-npm-acceptance",
        "cancel-in-progress": False,
    }
    assert "strategy" not in job
    assert "uses" not in job
    assert "needs" not in job
    assert "concurrency" not in job


def test_probe_token_env_binding_and_prerequisite_order():
    document = _document()
    job = document["jobs"]["probe"]
    assert document["permissions"] == {"contents": "read"}
    assert job["permissions"] == {"contents": "read", "packages": "write"}
    assert "environment" not in job
    assert "continue-on-error" not in job
    assert document.get("env", {}) == {}
    assert job.get("env", {}) == {}
    assert job["defaults"]["run"]["shell"] == "bash"
    steps = _steps()
    assert [step["uses"] for step in steps if "uses" in step] == [
        CHECKOUT,
        UV,
        MISE,
        PNPM,
        UPLOAD,
    ]
    assert _step(CHECKOUT)["with"] == {
        "ref": "${{ github.sha }}",
        "persist-credentials": False,
    }
    assert _step(UV)["with"]["version"] == "0.12.7"
    assert _step(MISE)["with"] == {
        "install": False,
        "working_directory": "${{ runner.temp }}",
    }
    assert _step(PNPM)["with"] == {
        "version": "11.22.0",
        "run_install": False,
    }
    assert _step("toolchain")["working-directory"] == "${{ runner.temp }}"
    dependencies = _step("dependencies")
    assert [shlex.split(line) for line in dependencies["run"].splitlines()] == [
        ["pnpm", "install", "--frozen-lockfile", "--ignore-scripts"],
        ["uv", "sync", "--frozen", "--python", "3.13", "--package", PACKAGE],
    ]
    assert steps.index(_step("toolchain")) < steps.index(_step(PNPM))
    assert steps.index(_step(PNPM)) < steps.index(dependencies)
    assert steps.index(dependencies) < steps.index(_step("probe"))
    assert steps.index(_step("request")) < steps.index(_step("probe"))
    assert _step("request")["env"] == {
        "WDV3_ACCEPTANCE_DIRECTORY": (
            "${{ runner.temp }}/wdv3-native-npm-${{ github.run_id }}"
        ),
        "REQUEST_JSON": "${{ inputs.request_json }}",
    }
    assert _step("probe")["env"] == {
        "WDV3_ACCEPTANCE_DIRECTORY": (
            "${{ runner.temp }}/wdv3-native-npm-${{ github.run_id }}"
        ),
        "GITHUB_TOKEN": "${{ github.token }}",
    }
    for step in steps:
        assert "continue-on-error" not in step
        if step not in (_step("request"), _step("probe")):
            assert not step.get("env")
        if step != _step(UPLOAD):
            assert "if" not in step
    raw = WORKFLOW.read_text(encoding="utf-8")
    assert raw.count("${{ github.token }}") == 1
    assert "secrets." not in raw
    assert raw.count(f"python -m {MODULE} probe") == 1


def test_profile_setup_executes_isolated_exact_node_and_npm(tmp_path):
    tools = tmp_path / "tools"
    tools.mkdir()
    node_root = tmp_path / "node"
    (node_root / "bin").mkdir(parents=True)
    install_log = tmp_path / "install.json"
    _executable(node_root / "bin/node", "print('v24.19.0')\n")
    _executable(
        node_root / "bin/npm",
        f"""import json, os, pathlib, shutil, sys
if sys.argv[1:] == ["--version"]:
    print("11.17.0")
else:
    pathlib.Path({str(install_log)!r}).write_text(json.dumps({{
        "args": sys.argv[1:], "env": dict(os.environ),
    }}))
    prefix = pathlib.Path(sys.argv[sys.argv.index("--prefix") + 1])
    shutil.copy2(__file__, prefix / "bin/npm")
""",
    )
    mise_log = tmp_path / "mise.jsonl"
    _executable(
        tools / "mise",
        f"""import json, pathlib, sys
with pathlib.Path({str(mise_log)!r}).open("a") as log:
    log.write(json.dumps(sys.argv[1:]) + "\\n")
if sys.argv[1] == "where":
    print({str(node_root)!r})
""",
    )
    github_path = tmp_path / "github-path"
    environment = os.environ | {
        "PATH": f"{tools}{os.pathsep}{os.environ['PATH']}",
        "RUNNER_TEMP": str(tmp_path),
        "GITHUB_RUN_ID": "42",
        "GITHUB_PATH": str(github_path),
        "GITHUB_TOKEN": "synthetic-must-not-reach-installer",
        "NPM_CONFIG_REGISTRY": "https://untrusted.invalid",
    }
    result = _shell(_step("toolchain")["run"], environment, tmp_path)
    assert result.returncode == 0, result.stderr
    assert [json.loads(line) for line in mise_log.read_text().splitlines()] == [
        ["install", "--yes", "node@24.19.0"],
        ["where", "node@24.19.0"],
    ]
    toolchain = tmp_path / "wdv3-toolchain-42"
    install = json.loads(install_log.read_text())
    assert install["args"] == [
        "install",
        "--global",
        "--prefix",
        str(toolchain),
        "--ignore-scripts",
        "--registry=https://registry.npmjs.org",
        "npm@11.17.0",
    ]
    install_env = install["env"]
    assert set(install_env) <= {
        "PATH",
        "HOME",
        "NPM_CONFIG_USERCONFIG",
        "NPM_CONFIG_GLOBALCONFIG",
        "LC_CTYPE",
    }
    assert install_env["PATH"] == f"{toolchain}/bin:/usr/bin:/bin"
    assert install_env["HOME"] == str(toolchain / "home")
    for key, name in (
        ("NPM_CONFIG_USERCONFIG", "user.npmrc"),
        ("NPM_CONFIG_GLOBALCONFIG", "global.npmrc"),
    ):
        assert install_env[key] == str(toolchain / name)
        assert Path(install_env[key]).read_bytes() == b""
    assert github_path.read_text() == f"{toolchain}/bin\n"


@pytest.mark.parametrize("exit_status", [0, 1])
def test_shell_preserves_request_and_single_probe_failure(
    tmp_path,
    exit_status,
):
    checkout = tmp_path / "checkout"
    checkout.mkdir()
    tools = tmp_path / "tools"
    tools.mkdir()
    (tools / "python3").symlink_to(sys.executable)
    log = tmp_path / "probe.jsonl"
    _executable(
        tools / "uv",
        """import json, os, pathlib, sys
args = sys.argv[1:]
evidence = pathlib.Path(args[args.index("--evidence-directory") + 1])
assert not evidence.exists()
evidence.mkdir()
(evidence / "command-started").touch()
with pathlib.Path(os.environ["TEST_LOG"]).open("a") as log:
    log.write(json.dumps({
        "args": args, "token": os.environ["GITHUB_TOKEN"],
        "path": os.environ["PATH"],
    }) + "\\n")
raise SystemExit(int(os.environ["TEST_EXIT"]))
""",
    )
    request = canonicalize(
        {
            "opaque": "quotes ' \" ; $(touch unexpected) `touch inert` \n λ",
        }
    )
    context = {
        "run_id": "42",
        "run_attempt": "1",
        "sha": "a" * 40,
        "ref": "refs/heads/main",
        "actor_id": "712433",
        "workflow_ref": (
            f"hcoona/three/.github/workflows/{WORKFLOW.name}@refs/heads/main"
        ),
        "repository": "hcoona/three",
        "event_name": "workflow_dispatch",
    }
    acceptance = tmp_path / "acceptance"
    environment = (
        os.environ
        | {f"GITHUB_{key.upper()}": value for key, value in context.items()}
        | {
            "PATH": f"{tools}{os.pathsep}{os.environ['PATH']}",
            "RUNNER_TEMP": str(tmp_path),
            "GITHUB_WORKSPACE": str(checkout),
            "WDV3_ACCEPTANCE_DIRECTORY": str(acceptance),
            "REQUEST_JSON": request.decode(),
            "TEST_LOG": str(log),
            "TEST_EXIT": str(exit_status),
        }
    )
    environment.pop("GITHUB_TOKEN", None)
    prepared = _shell(_step("request")["run"], environment, checkout)
    assert prepared.returncode == 0, prepared.stderr
    assert (acceptance / "request.json").read_bytes() == request
    assert json.loads((acceptance / "platform.json").read_bytes()) == {
        "schema": "workflow-delivery-v3/native-npm-actions-context/v1",
        **context,
    }
    assert not (acceptance / "evidence").exists()
    assert not (acceptance / "runtime").exists()
    assert not (checkout / "unexpected").exists()
    assert not (checkout / "inert").exists()
    environment["GITHUB_TOKEN"] = "synthetic-actions-token"  # noqa: S105
    completed = _shell(_step("probe")["run"], environment, checkout)
    assert completed.returncode == exit_status, completed.stderr
    calls = [json.loads(line) for line in log.read_text().splitlines()]
    assert len(calls) == 1
    toolchain = tmp_path / "wdv3-toolchain-42/bin"
    assert calls[0]["args"] == [
        "run",
        "--no-sync",
        "--python",
        "3.13",
        "--package",
        PACKAGE,
        "python",
        "-m",
        MODULE,
        "probe",
        "--request",
        str(acceptance / "request.json"),
        "--repository-root",
        str(checkout),
        "--runtime-directory",
        str(acceptance / "runtime"),
        "--toolchain-directory",
        str(toolchain),
        "--evidence-directory",
        str(acceptance / "evidence"),
    ]
    assert calls[0]["token"] == environment["GITHUB_TOKEN"]
    assert calls[0]["path"].split(os.pathsep)[0] == str(toolchain)
    assert (acceptance / "evidence/command-started").is_file()
    assert environment["GITHUB_TOKEN"] not in (
        completed.stdout + completed.stderr
    )


def test_audit_is_always_an_immutable_explicit_bundle():
    upload = _step(UPLOAD)
    assert _steps()[-1] == upload
    assert upload["if"] == "always()"
    settings = upload["with"]
    prefix = "${{ runner.temp }}/wdv3-native-npm-${{ github.run_id }}/"
    assert set(settings["path"].splitlines()) == {
        prefix + name for name in AUDIT_FILES
    }
    assert settings["name"] == "wdv3-native-npm-probe-${{ github.run_id }}"
    assert settings["overwrite"] is False
    assert settings["archive"] is True
    assert settings["include-hidden-files"] is False
    assert settings["if-no-files-found"] == "error"
    assert 45 <= settings["retention-days"] <= 90


def test_built_wheel_exposes_acceptance_probe_module(tmp_path):
    uv = shutil.which("uv")
    assert uv is not None
    built = subprocess.run(
        [
            uv,
            "build",
            "--wheel",
            str(ROOT / "src/public/lib" / PACKAGE),
            "--out-dir",
            str(tmp_path / "dist"),
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert built.returncode == 0, built.stderr
    (wheel,) = (tmp_path / "dist").glob("*.whl")
    invocation = subprocess.run(
        [
            sys.executable,
            "-I",
            "-c",
            (
                "import runpy, sys; sys.path.insert(0, sys.argv[1]); "
                f"import {MODULE}.npm_probe as probe; "
                "assert probe.__file__.startswith(sys.argv[1]); "
                "sys.argv = ['acceptance', 'probe', '--help']; "
                f"runpy.run_module('{MODULE}', run_name='__main__')"
            ),
            str(wheel),
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=15,
    )
    assert invocation.returncode == 0, invocation.stderr
    for option in (
        "--request",
        "--repository-root",
        "--runtime-directory",
        "--toolchain-directory",
        "--evidence-directory",
    ):
        assert option in invocation.stdout
