"""Exercise checked-in workflow contracts at bounded test interfaces."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tomllib
from dataclasses import dataclass
from pathlib import Path
from typing import Any

SHELL_ENV = ("PATH", "HOME", "TMPDIR", "LANG", "LC_ALL", "TERM")
CONDITION_TERM = re.compile(
    r"(?P<status>!?\s*(?:always|cancelled)\(\))"
    r"|(?P<reference>(?:inputs|needs)(?:\.[\w-]+)+)\s*"
    r"(?P<operator>==|!=)\s*(?P<literal>true|false|'[^']*')"
)
COMMAND_RECORDER = r"""
import json
import os
import sys
from pathlib import Path

command = [Path(sys.argv[0]).name, *sys.argv[1:]]
with Path(os.environ["COMMAND_LOG"]).open("a", encoding="utf-8") as stream:
    stream.write(json.dumps({
        "command": command,
        "cwd": str(Path.cwd()),
        "profile": os.environ.get("HK_PROFILE"),
        "auto_install": os.environ.get("MISE_TASK_RUN_AUTO_INSTALL"),
        "disabled_tools": os.environ.get("MISE_DISABLE_TOOLS"),
    }) + "\n")
failure = json.loads(os.environ.get("FAIL_COMMAND", "[]"))
if failure and command[:len(failure)] == failure:
    sys.exit(73)
if "--version" in command:
    print("fixture-version")
"""


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
    if "publish-github-anchor" in jobs:
        required["publish-github-anchor"] = publication | {
            ("publish", "publication")
        }
        required["verify-result"] |= {
            ("publish-github-anchor", "anchor-publication")
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
                if producer not in ("publish", "publish-github-anchor"):
                    assert step.get("if") in (None, True, "success()")
                received.add(transfer)
        assert payloads <= received, job_name
    assert ("verify-result", "${{ runner.temp }}/verification") in retained


def executable(path: Path, body: str) -> None:
    """Install a controlled command using the current test interpreter."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"#!{sys.executable}\n{body}\n", encoding="utf-8")
    path.chmod(0o755)


def load_script(path: Path):
    """Load a checked-in standalone engineering script for local tests."""
    spec = importlib.util.spec_from_file_location(path.stem, path)
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def fixture_path(bin_dir: Path, *commands: str) -> str:
    """Expose only the specified host utilities alongside fixture tools."""
    for name in commands:
        command = shutil.which(name)
        assert command is not None, name
        (bin_dir / name).symlink_to(command)
    return str(bin_dir)


def recorded_commands(cwd: Path, *tools: str) -> dict[str, str]:
    """Give child commands an isolated PATH and a shared observation log."""
    bin_dir = cwd / "setup tools"
    for name in tools:
        executable(bin_dir / name, COMMAND_RECORDER)
    return {
        "PATH": fixture_path(bin_dir, "bash"),
        "COMMAND_LOG": str(cwd / "commands.jsonl"),
        "GITHUB_ENV": str(cwd / "github-env"),
        "GITHUB_OUTPUT": str(cwd / "github-output"),
        "GITHUB_EVENT_NAME": "pull_request",
        "RUNNER_TEMP": str(cwd / "runner-state"),
        "GITHUB_WORKSPACE": str(cwd),
    }


def command_log(path: Path) -> list[dict[str, Any]]:
    """Read observations, including an empty log when no tool ran."""
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text().splitlines()]


def output_values(path: Path) -> dict[str, str]:
    """Read the single-line values written to an Actions runner file."""
    outputs = {}
    if path.exists():
        for line in path.read_text().splitlines():
            key, separator, value = line.partition("=")
            assert separator, line
            outputs[key] = value
    return outputs


def resolve(value: str, bindings: dict[str, str]) -> str:
    """Require explicit fixture values for every consumed Actions expression."""
    return re.sub(
        r"\$\{\{\s*(.*?)\s*\}\}",
        lambda match: bindings[match.group(1)],
        value,
    )


def effective_env(workflow, job, step):
    """Apply Actions environment precedence before resolving expressions."""
    return (
        (workflow.get("env") or {})
        | (job.get("env") or {})
        | (step.get("env") or {})
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
    for scope in (workflow or {}, job or {}, step):
        defaults = scope.get("defaults", {}).get("run", {})
        shell = defaults.get("shell", shell)
        directory = defaults.get("working-directory", directory)
    process_env.update(
        {
            key: resolve(str(value), bindings)
            for key, value in effective_env(
                workflow or {}, job or {}, step
            ).items()
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


@dataclass(frozen=True)
class WorkflowShell:
    """Run scenarios with common bindings and actual workflow defaults."""

    workflow: dict[str, Any]
    cwd: Path
    bindings: dict[str, str]

    def run(self, name, step, *, env=None, bindings=None):
        """Run one checked-in step with optional scenario overrides."""
        return run_step(
            step,
            cwd=self.cwd,
            env=env or {},
            bindings=self.bindings | (bindings or {}),
            workflow=self.workflow,
            job=self.workflow["jobs"][name],
        )

    def run_required(self, name, steps, *, env, bindings):
        """Carry GITHUB_ENV across selected bodies and stop at first failure."""
        result = None
        for step in steps:
            result = self.run(name, step, env=env, bindings=bindings)
            if result.returncode:
                break
            updates = output_values(Path(env["GITHUB_ENV"]))
            assert all(key.isidentifier() for key in updates)
            env.update(updates)
        assert result is not None
        return result


def selected_control_call(
    workflow: dict[str, Any],
    job_name: str,
    *,
    root: Path,
    cwd: Path,
    tools: tuple[str, ...],
) -> list[str]:
    """Run runtime setup and the first control command across GITHUB_ENV."""
    lock = tomllib.loads((root / "mise.lock").read_text())["tools"]
    framework_lock = tomllib.loads(
        (root / "eng/dotnet-frameworks/mise.lock").read_text()
    )["tools"]
    committed = {
        name: lock[name][0]["version"] for name in tools if name != "dotnet"
    }
    committed["dotnet"] = json.loads((root / "global.json").read_text())["sdk"][
        "version"
    ]
    committed["dotnet-frameworks"] = framework_lock["dotnet"][0]["version"]
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
    assert installed[:2] == ["--jobs", "1"]
    assert {item for item in installed if "@" in item} == {
        f"dotnet@{committed['dotnet']}",
        f"dotnet@{committed['dotnet-frameworks']}",
        *{
            f"{tool}@{version}"
            for tool, version in committed.items()
            if tool not in {"dotnet", "dotnet-frameworks"}
            and (
                job_name == "prepare"
                or (
                    job_name != "publish-github-anchor"
                    and tool in {"python", "uv"}
                )
            )
        },
    }
    env.update(output_values(cwd / "github-env"))
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
