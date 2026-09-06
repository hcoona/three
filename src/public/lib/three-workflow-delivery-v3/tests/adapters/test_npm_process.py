"""Local process guarantees, without invoking npm or a registry."""

# ruff: noqa: D103

from __future__ import annotations

import json
import os
import sys

import pytest
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
)

pytestmark = pytest.mark.skipif(os.name != "posix", reason="Ubuntu publisher")


def _run(tmp_path, code, **changes):
    return IsolatedNpmProcessRunner().run(
        (sys.executable, "-c", code),
        cwd=tmp_path,
        environment={"ONLY_EXPECTED": "value"},
        timeout=changes.get("timeout", 5),
        output_limit=changes.get("output_limit", 4096),
    )


def test_process_uses_exact_environment_cwd_and_closed_stdin(
    tmp_path, monkeypatch
):
    monkeypatch.setenv("NPM_CONFIG_USERCONFIG", "untrusted")
    result = _run(
        tmp_path,
        "import json,os,sys; print(json.dumps([os.getcwd(), "
        "dict(os.environ), sys.stdin.read()]))",
    )
    assert result.classification == "definitive-success"
    cwd, environment, stdin = json.loads(result.output)
    assert cwd == str(tmp_path)
    assert environment["ONLY_EXPECTED"] == "value"
    assert "NPM_CONFIG_USERCONFIG" not in environment
    assert "GITHUB_TOKEN" not in environment
    assert stdin == ""


@pytest.mark.parametrize(
    ("code", "classification"),
    [
        ("raise SystemExit(17)", "definitive-non-success"),
        ("import os,signal; os.kill(os.getpid(),signal.SIGKILL)", "ambiguous"),
    ],
)
def test_process_exit_facts_do_not_guess_registry_outcome(
    tmp_path, code, classification
):
    assert _run(tmp_path, code).classification == classification


def test_exec_failure_proves_command_not_initiated(tmp_path):
    result = IsolatedNpmProcessRunner().run(
        (str(tmp_path / "missing-npm"),),
        cwd=tmp_path,
        environment={},
        timeout=1,
        output_limit=256,
    )
    assert result.classification == "not-initiated"
    assert result.returncode is None


def test_timeout_is_ambiguous_and_does_not_restart_process(tmp_path):
    result = _run(
        tmp_path,
        "import pathlib,time\n"
        "with pathlib.Path('invocations').open('ab') as log:\n"
        "    log.write(b'x')\n"
        "time.sleep(30)",
        timeout=0.5,
    )
    assert result.classification == "ambiguous"
    assert (tmp_path / "invocations").read_bytes() == b"x"
    assert result.returncode < 0


def test_process_output_is_bounded_without_deadlock(tmp_path):
    result = _run(
        tmp_path,
        "import sys; sys.stdout.write('x' * 1000000)",
        output_limit=256,
    )
    assert result.classification == "definitive-success"
    assert result.output == b"x" * 256
    assert result.truncated
