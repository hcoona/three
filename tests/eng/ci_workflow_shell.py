"""Exercise checked-in workflow contracts at bounded test interfaces."""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from pathlib import Path

SHELL_ENV = ("PATH", "HOME", "TMPDIR", "LANG", "LC_ALL", "TERM")
CONDITION_TERM = re.compile(
    r"(?P<status>!?\s*(?:always|cancelled)\(\))"
    r"|(?P<reference>(?:inputs|needs)(?:\.[\w-]+)+)\s*"
    r"(?P<operator>==|!=)\s*(?P<literal>true|false|'[^']*')"
)


def condition(
    expression: str,
    values: dict[str, bool | str],
    *,
    success: bool = True,
) -> bool:
    """Evaluate status checks and literal comparisons joined by ``&&`` only."""
    expression = expression.strip()
    if expression.startswith("${{") and expression.endswith("}}"):
        expression = expression[3:-2].strip()
    results = []
    explicit_status = False
    for part in expression.split("&&"):
        term = part.strip()
        match = CONDITION_TERM.fullmatch(term)
        assert match is not None, f"Unsupported workflow condition: {term!r}"
        if match["status"]:
            explicit_status = True
            negated = term.startswith("!")
            status = (
                term.removeprefix("!").strip() == "always()"
                or values["cancelled"]
            )
            assert isinstance(status, bool), term
            results.append(status != negated)
        else:
            actual = values[match["reference"]]
            literal = match["literal"]
            if literal.startswith("'"):
                assert isinstance(actual, str), term
                equal = actual.casefold() == literal[1:-1].casefold()
            else:
                assert isinstance(actual, bool), term
                equal = actual is (literal == "true")
            results.append(equal == (match["operator"] == "=="))
    return (explicit_status or success) and all(results)


def dependencies(job: dict[str, Any]) -> list[str]:
    """Normalize scalar and list forms of Actions job dependencies."""
    needs = job.get("needs", [])
    return [needs] if isinstance(needs, str) else needs


def action_steps(job: dict[str, Any], action: str) -> list[dict[str, Any]]:
    """Find an action by its repository, not its pin or step title."""
    return [
        step
        for step in job["steps"]
        if step.get("uses", "").startswith(action + "@")
    ]


def artifact_upload(job: dict[str, Any], output: str) -> dict[str, Any]:
    """Resolve a declared artifact output to the producing upload action."""
    identifier = re.fullmatch(
        r"\$\{\{\s*steps\.([\w-]+)\.outputs\.artifact-id\s*\}\}",
        job["outputs"][output],
    )
    assert identifier is not None, output
    return next(
        step
        for step in action_steps(job, "actions/upload-artifact")
        if step.get("id") == identifier[1]
    )


def assert_release_transfers(workflow: dict[str, Any]) -> None:
    """Check same-run IDs and the payloads needed by each release role."""
    jobs = workflow["jobs"]
    preparation = {("prepare", "control"), ("prepare", "prepared")}
    publication = preparation | {("observe", "observation")}
    required = {
        "observe": preparation,
        "publish": publication,
        "verify-result": publication | {("publish", "publication")},
    }
    retained = set()
    artifact_names = set()
    for job_name, job in jobs.items():
        for step in action_steps(job, "actions/upload-artifact"):
            upload = step["with"]
            assert upload["retention-days"] == 45
            assert upload["overwrite"] is False
            assert upload["if-no-files-found"] == "error"
            assert step["if"] == "always()"
            assert not step.get("continue-on-error", False)
            for identity in ("github.run_id", "github.run_attempt"):
                assert identity in upload["name"]
            assert upload["name"] not in artifact_names
            artifact_names.add(upload["name"])
            retained.add((job_name, upload["path"]))
        payloads = {
            (producer, "${{ runner.temp }}/" + payload)
            for producer, payload in required.get(job_name, set())
        }
        received = set()
        for step in action_steps(job, "actions/download-artifact"):
            download = step["with"]
            assert (
                not {"name", "pattern", "run-id", "repository", "github-token"}
                & download.keys()
            )
            assert download["digest-mismatch"] == "error"
            assert not step.get("continue-on-error", False)
            source = re.fullmatch(
                r"\$\{\{\s*needs\.([\w-]+)\.outputs\.([\w-]+)\s*\}\}",
                download["artifact-ids"],
            )
            assert source is not None, download
            producer, output = source.groups()
            assert producer in dependencies(job)
            upload = artifact_upload(jobs[producer], output)
            transfer = (producer, download["path"])
            if transfer in payloads:
                assert upload["with"]["path"] == download["path"]
                if producer != "publish":
                    assert step.get("if") in (None, True, "success()")
                received.add(transfer)
        assert payloads <= received, job_name
    assert ("verify-result", "${{ runner.temp }}/verification") in retained


def executable(path: Path, body: str) -> None:
    """Install a controlled command using the current test interpreter."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"#!{sys.executable}\n{body}\n", encoding="utf-8")
    path.chmod(0o755)


def resolve(value: str, bindings: dict[str, str]) -> str:
    """Require explicit fixture values for every consumed Actions expression."""
    return re.sub(
        r"\$\{\{\s*(.*?)\s*\}\}",
        lambda match: bindings[match.group(1)],
        value,
    )


def run_step(  # noqa: PLR0913
    step: dict[str, Any],
    *,
    cwd: Path,
    env: dict[str, str],
    bindings: dict[str, str],
    workflow: dict[str, Any] | None = None,
    job: dict[str, Any] | None = None,
) -> subprocess.CompletedProcess[str]:
    """Launch a Bash body with effective Actions environment and flags."""
    process_env = {
        key: os.environ[key] for key in SHELL_ENV if key in os.environ
    }
    process_env.update(env)
    shell = None
    directory = "."
    merged_env: dict[str, Any] = {}
    for scope in (workflow or {}, job or {}, step):
        merged_env.update(scope.get("env", {}))
        defaults = scope.get("defaults", {}).get("run", {})
        shell = defaults.get("shell", shell)
        directory = defaults.get("working-directory", directory)
    process_env.update(
        {
            key: resolve(str(value), bindings)
            for key, value in merged_env.items()
        }
    )
    shell = step.get("shell", shell)
    directory = step.get("working-directory", directory)
    assert shell in (None, "bash"), f"Unsupported shell: {shell}"
    flags = (
        ["--noprofile", "--norc", "-e"]
        if shell is None
        else ["--noprofile", "--norc", "-eo", "pipefail"]
    )
    return subprocess.run(  # noqa: S603
        ["bash", *flags, "-c", resolve(step["run"], bindings)],  # noqa: S607
        cwd=cwd / resolve(directory, bindings),
        env=process_env,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )


def selected_control_call(
    workflow: dict[str, Any],
    job_name: str,
    *,
    root: Path,
    cwd: Path,
    committed: dict[str, str],
) -> list[str]:
    """Run runtime setup and the first control command across GITHUB_ENV."""
    jobs = workflow["jobs"]
    job = jobs[job_name]
    defaults = {"defaults": workflow.get("defaults", {})}
    tools = next(s for s in jobs["prepare"]["steps"] if s.get("id") == "tools")
    output = cwd / "tool-versions"
    result = run_step(
        tools,
        cwd=root,
        env={"GITHUB_OUTPUT": str(output)},
        bindings={},
        workflow=defaults,
        job=jobs["prepare"],
    )
    assert result.returncode == 0, result.stderr
    versions = {
        f"steps.tools.outputs.{key}": value
        for key, value in (
            line.split("=", 1) for line in output.read_text().splitlines()
        )
    }
    assert versions == {
        f"steps.tools.outputs.{key}": value for key, value in committed.items()
    }
    bindings = versions | {"runner.temp": str(cwd)}
    for key, value in jobs["prepare"]["outputs"].items():
        version = value.removeprefix("${{").removesuffix("}}").strip()
        if version in versions:
            bindings[f"needs.prepare.outputs.{key}"] = versions[version]
    setup = next(s for s in job["steps"] if "mise install" in s.get("run", ""))
    command = next(
        line.strip()
        for step in job["steps"]
        for line in step.get("run", "").splitlines()
        if line.strip().startswith('"$DOTNET_EXECUTABLE"')
    )
    selected = cwd / "setup tools/selected-dotnet"
    executable(
        selected,
        "import json, os, sys\n"
        "from pathlib import Path\n"
        "Path(os.environ['COMMAND_LOG']).write_text(json.dumps(sys.argv[1:]))",
    )
    executable(
        selected.with_name("mise"),
        "import json, os, sys\n"
        "from pathlib import Path\n"
        "if sys.argv[1] == 'install':\n"
        "    Path(os.environ['INSTALL_LOG']).write_text("
        "json.dumps(sys.argv[2:]))\n"
        "elif sys.argv[1] == 'which':\n"
        "    print(os.environ['SELECTED_DOTNET'])\n"
        "else:\n"
        "    sys.exit(1)",
    )
    env = {
        "PATH": os.pathsep.join((str(selected.parent), os.defpath)),
        "SELECTED_DOTNET": str(selected),
        "GITHUB_ENV": str(cwd / "github-env"),
        "GITHUB_PATH": str(cwd / "github-path"),
        "RUNNER_TEMP": str(cwd),
        "COMMAND_LOG": str(cwd / "command.json"),
        "INSTALL_LOG": str(cwd / "install.json"),
    }
    result = run_step(
        setup, cwd=cwd, env=env, bindings=bindings, workflow=defaults, job=job
    )
    assert result.returncode == 0, result.stderr
    installed = json.loads((cwd / "install.json").read_text())
    assert {item for item in installed if "@" in item} == {
        f"{tool}@{version}"
        for tool, version in committed.items()
        if tool == "dotnet" or job_name == "prepare" or tool in {"python", "uv"}
    }
    env.update(
        line.split("=", 1)
        for line in (cwd / "github-env").read_text().splitlines()
    )
    result = run_step(
        {"run": "set -u\n" + command},
        cwd=cwd,
        env=env,
        bindings=bindings,
        workflow=defaults,
        job=job,
    )
    assert result.returncode == 0, result.stderr
    return json.loads((cwd / "command.json").read_text())
