"""Prepare or run Copilot CLI evals for the enterprise translation plugin."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

SKILL = Path(__file__).resolve().parents[1]
_DEFAULT_PLUGIN_ROOT = SKILL.parents[1]
DEFAULT_PLUGIN_ROOT = (
    _DEFAULT_PLUGIN_ROOT
    if (_DEFAULT_PLUGIN_ROOT / "plugin.json").is_file()
    else None
)
EVALS = SKILL / "evals" / "evals.json"
CHECKER = SKILL / "scripts" / "check_translation_outputs.py"
COPILOT_ENV = {**os.environ, "COPILOT_PLUGIN_DIR_ONLY": "true"}
AGENT_BY_CASE = {
    "structured-markdown-translation": "enterprise-translation-team:translation-linguist",
    "terminology-glossary-conflict": "enterprise-translation-team:translation-terminologist",
    "mqm-review-json": "enterprise-translation-team:translation-reviser",
    "final-qa-contract": "enterprise-translation-team:translation-qa-engineer",
    "negative-no-durable-wiki-write": "enterprise-translation-team:translation-workflow-lead",
}
REVIEW_AGENTS = [
    "enterprise-translation-team:translation-positive-reviewer",
    "enterprise-translation-team:translation-negative-reviewer",
]
REVIEW_MODEL = "gpt-5.5"
PLUGIN_NAME = "enterprise-translation-team"


def project_agent_name(agent: str) -> str:
    return agent.rsplit(":", 1)[-1]


def validate_plugin_root(candidate: Path) -> Path:
    plugin_root = candidate.resolve()
    required_paths = [
        plugin_root / "plugin.json",
        plugin_root / "skills" / PLUGIN_NAME / "SKILL.md",
        *[
            plugin_root / "agents" / f"{agent.split(':', 1)[1]}.agent.md"
            for agent in [*AGENT_BY_CASE.values(), *REVIEW_AGENTS]
        ],
    ]
    missing = [
        path.relative_to(plugin_root).as_posix()
        for path in required_paths
        if not path.is_file()
    ]
    if missing:
        raise ValueError(
            f"Invalid enterprise translation plugin root; missing: {missing}"
        )
    manifest = json.loads(
        (plugin_root / "plugin.json").read_text(encoding="utf-8")
    )
    if manifest.get("name") != PLUGIN_NAME:
        raise ValueError(f"Plugin root manifest name must be {PLUGIN_NAME!r}")
    return plugin_root


def validate_apm_root(candidate: Path) -> Path:
    apm_root = candidate.resolve()
    required_paths = [
        apm_root / ".agents" / "skills" / PLUGIN_NAME / "SKILL.md",
        *[
            apm_root
            / ".github"
            / "agents"
            / f"{project_agent_name(agent)}.agent.md"
            for agent in [*AGENT_BY_CASE.values(), *REVIEW_AGENTS]
        ],
    ]
    missing = [
        path.relative_to(apm_root).as_posix()
        for path in required_paths
        if not path.is_file()
    ]
    if missing:
        raise ValueError(f"Invalid APM deployment root; missing: {missing}")
    return apm_root


def stage_apm_project(apm_root: Path, workspace: Path) -> Path:
    project_root = workspace / "apm-project"
    if project_root.exists():
        shutil.rmtree(project_root)
    skill_source = apm_root / ".agents" / "skills" / PLUGIN_NAME
    shutil.copytree(
        skill_source,
        project_root / ".agents" / "skills" / PLUGIN_NAME,
    )
    agents_target = project_root / ".github" / "agents"
    agents_target.mkdir(parents=True)
    for agent in [*AGENT_BY_CASE.values(), *REVIEW_AGENTS]:
        name = project_agent_name(agent)
        shutil.copy2(
            apm_root / ".github" / "agents" / f"{name}.agent.md",
            agents_target / f"{name}.agent.md",
        )
    return project_root


def load_cases() -> list[dict]:
    return json.loads(EVALS.read_text(encoding="utf-8"))["evals"]


def select_cases(case_id: str | None) -> list[dict]:
    cases = load_cases()
    if case_id is None:
        return cases
    selected = [case for case in cases if case["id"] == case_id]
    if not selected:
        raise ValueError(f"Unknown eval case: {case_id}")
    return selected


def copy_inputs(case: dict, run_dir: Path) -> None:
    for relative in case.get("files", []):
        source = SKILL / relative
        target = run_dir / relative
        if source.is_dir():
            shutil.copytree(source, target, dirs_exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)


def build_workspace(workspace: Path | None) -> Path:
    if workspace is not None:
        workspace.mkdir(parents=True, exist_ok=True)
        return workspace.resolve()
    return Path(
        tempfile.mkdtemp(prefix="enterprise-translation-evals-")
    ).resolve()


def needs_grading(case: dict) -> bool:
    return bool(
        case.get("expected_files")
        or case.get("forbidden_created_paths")
        or case.get("forbid_workspace_changes")
        or case.get("required_response_patterns")
        or case.get("forbidden_response_patterns")
    )


def last_verdict(text: str) -> str:
    verdict_tokens = [
        match.group(0).upper()
        for match in re.finditer(r"\b(?:PASS|BLOCK)\b", text, re.IGNORECASE)
    ]
    nonempty_lines = [
        line.strip() for line in text.splitlines() if line.strip()
    ]
    if "BLOCK" in verdict_tokens:
        return "BLOCK"
    if (
        verdict_tokens == ["PASS"]
        and nonempty_lines
        and nonempty_lines[-1] == "PASS"
    ):
        return "PASS"
    return "UNKNOWN"


def snapshot_files(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): hashlib.sha256(
            path.read_bytes()
        ).hexdigest()
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def compare_snapshots(
    before: dict[str, str], after: dict[str, str]
) -> dict[str, list[str]]:
    before_paths = set(before)
    after_paths = set(after)
    return {
        "added": sorted(after_paths - before_paths),
        "removed": sorted(before_paths - after_paths),
        "modified": sorted(
            path
            for path in before_paths & after_paths
            if before[path] != after[path]
        ),
    }


def run_discovery_command(command: list[str], cwd: Path) -> object:
    completed = subprocess.run(
        command,
        cwd=cwd,
        text=True,
        capture_output=True,
        check=False,
        env=COPILOT_ENV,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            "Could not verify no-plugin baseline isolation: "
            f"{completed.stderr.strip()}"
        )
    try:
        return json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(
            "Copilot discovery returned invalid JSON while verifying "
            "no-plugin baseline isolation"
        ) from exc


def require_component_list(
    payload: object, label: str
) -> list[dict[str, object]]:
    if not isinstance(payload, list):
        raise RuntimeError(
            f"Copilot {label} discovery returned an unexpected payload"
        )
    components: list[dict[str, object]] = []
    for index, item in enumerate(payload):
        if not isinstance(item, dict):
            raise RuntimeError(
                f"Copilot {label} discovery item {index} must be an object"
            )
        required_strings = (
            ["name", "scope", "source"]
            if label == "plugin"
            else ["name", "source", "path"]
        )
        for field in required_strings:
            value = item.get(field)
            if not isinstance(value, str) or not value.strip():
                raise RuntimeError(
                    f"Copilot {label} discovery item {index} has no valid "
                    f"{field}"
                )
        if label == "plugin" and item.get("kind") != "plugin":
            raise RuntimeError(
                f"Copilot plugin discovery item {index} has invalid kind"
            )
        if type(item.get("enabled")) is not bool:
            raise RuntimeError(
                f"Copilot {label} discovery item {index} has invalid enabled"
            )
        components.append(item)
    return components


def assert_no_target_components(case_dir: Path) -> dict[str, object]:
    plugin_payload = run_discovery_command(
        [
            "copilot",
            "-C",
            str(case_dir),
            "plugins",
            "list",
            "--kind",
            "plugin",
            "--json",
        ],
        case_dir,
    )
    skill_payload = run_discovery_command(
        ["copilot", "-C", str(case_dir), "skill", "list", "--json"],
        case_dir,
    )
    if not isinstance(plugin_payload, dict):
        raise RuntimeError(
            "Copilot plugin discovery returned an unexpected payload"
        )
    plugin_errors = plugin_payload.get("errors")
    if not isinstance(plugin_errors, list):
        raise RuntimeError(
            "Copilot plugin discovery payload has no valid errors array"
        )
    if plugin_errors:
        raise RuntimeError(
            "Copilot plugin discovery reported errors while verifying "
            f"no-plugin isolation: {plugin_errors}"
        )
    plugins = require_component_list(plugin_payload.get("plugins"), "plugin")
    skills = require_component_list(skill_payload, "skill")
    discovered = [
        {
            "kind": component_kind,
            "scope": item.get("scope"),
            "source": item.get("source"),
        }
        for component_kind, items in [("plugin", plugins), ("skill", skills)]
        for item in items
        if isinstance(item, dict)
        and item.get("name") == PLUGIN_NAME
        and item.get("enabled", True)
    ]
    if discovered:
        raise RuntimeError(
            "No-plugin baseline is contaminated by discoverable "
            f"{PLUGIN_NAME} components: {discovered}"
        )
    return {
        "plugin_payload": plugin_payload,
        "skill_payload": skill_payload,
        "target_absent": True,
    }


def assert_apm_target_components(
    case_dir: Path, project_root: Path
) -> dict[str, object]:
    plugin_payload = run_discovery_command(
        [
            "copilot",
            "-C",
            str(case_dir),
            "plugins",
            "list",
            "--kind",
            "plugin",
            "--json",
        ],
        case_dir,
    )
    if not isinstance(plugin_payload, dict):
        raise RuntimeError(
            "Copilot plugin discovery returned an unexpected payload"
        )
    plugin_errors = plugin_payload.get("errors")
    if not isinstance(plugin_errors, list) or plugin_errors:
        raise RuntimeError(
            "Copilot plugin discovery did not complete cleanly for the "
            f"APM project: {plugin_errors!r}"
        )
    plugins = require_component_list(plugin_payload.get("plugins"), "plugin")
    conflicting_plugins = [
        item
        for item in plugins
        if item.get("name") == PLUGIN_NAME and item.get("enabled", True)
    ]
    if conflicting_plugins:
        raise RuntimeError(
            "APM project eval is contaminated by a directly loaded target "
            f"plugin: {conflicting_plugins}"
        )
    skill_payload = run_discovery_command(
        ["copilot", "-C", str(case_dir), "skill", "list", "--json"],
        case_dir,
    )
    skills = require_component_list(skill_payload, "skill")
    expected_skill = (
        project_root / ".agents" / "skills" / PLUGIN_NAME
    ).resolve()
    target_skills = [
        item
        for item in skills
        if item.get("name") == PLUGIN_NAME and item.get("enabled", True)
    ]
    if len(target_skills) != 1:
        raise RuntimeError(
            "APM project must expose exactly one enabled target skill: "
            f"{target_skills}"
        )
    target_path = target_skills[0].get("path")
    if (
        not isinstance(target_path, str)
        or Path(target_path).resolve() != expected_skill
        or target_skills[0].get("source") not in {"project", "inherited"}
    ):
        raise RuntimeError(
            "Copilot did not discover the target skill from the staged APM "
            f"project path: {target_skills[0]}"
        )
    return {
        "plugin_payload": plugin_payload,
        "skill_payload": skill_payload,
        "target_skill_path": str(expected_skill),
    }


def run_review_gates(
    case: dict,
    case_dir: Path,
    plugin_root: Path | None,
    apm_project_root: Path | None,
) -> list[dict]:
    if plugin_root is None and apm_project_root is None:
        raise RuntimeError(
            "Review gates require an intact plugin or staged APM project"
        )
    results: list[dict] = []
    prompt = (
        f"Review eval case {case['id']} for the enterprise-translation-team plugin. "
        f"Inspect the run artifacts under {case_dir}, including run-manifest.json, "
        "response.txt, grading output, and outputs if present. "
        "You are one of the review gates currently being executed; gate output "
        "files are withheld until both invocations complete, so do not block "
        "because your own or the peer gate artifact is not present. "
        "End with exactly one standalone verdict line: PASS only if the run satisfies "
        "the eval prompt, expected output, assertions, and safety constraints; "
        "otherwise BLOCK. Do not use either verdict token elsewhere in the response."
    )
    agents = (
        REVIEW_AGENTS
        if plugin_root is not None
        else [project_agent_name(agent) for agent in REVIEW_AGENTS]
    )
    review_cwd = case_dir if plugin_root is not None else apm_project_root
    if review_cwd is None:
        raise RuntimeError("Could not determine review-gate working directory")
    completed_reviews: list[tuple[str, subprocess.CompletedProcess[str]]] = []
    for agent in agents:
        command = [
            "copilot",
            "--agent",
            agent,
            "--model",
            REVIEW_MODEL,
            "-C",
            str(review_cwd),
            "-p",
            prompt,
            "--allow-tool=read",
            "--allow-tool=search",
            "--silent",
        ]
        if plugin_root is not None:
            command[command.index("-p") : command.index("-p")] = [
                "--plugin-dir",
                str(plugin_root),
            ]
        else:
            command[command.index("-p") : command.index("-p")] = [
                "--add-dir",
                str(case_dir),
            ]
        completed = subprocess.run(
            command,
            cwd=review_cwd,
            text=True,
            capture_output=True,
            check=False,
            env=COPILOT_ENV,
        )
        completed_reviews.append((agent, completed))
    for agent, completed in completed_reviews:
        safe_agent = agent.replace(":", "__")
        output_path = case_dir / f"review-gate-{safe_agent}.txt"
        output_path.write_text(completed.stdout, encoding="utf-8")
        (case_dir / f"review-gate-{safe_agent}.stderr.txt").write_text(
            completed.stderr,
            encoding="utf-8",
        )
        verdict = last_verdict(completed.stdout)
        results.append(
            {
                "agent": agent,
                "returncode": completed.returncode,
                "verdict": verdict,
                "output": str(output_path),
            }
        )
    return results


def run_case(
    case: dict,
    workspace: Path,
    mode: str,
    model: str,
    explicit_agent: str | None,
    baseline: str,
    review_gates: bool,
    plugin_root: Path | None,
    apm_project_root: Path | None,
) -> dict:
    run_name = baseline if mode == "copilot" else "dry-run"
    if baseline == "no-plugin" and explicit_agent is not None:
        raise ValueError("--agent cannot be used with --baseline no-plugin")
    if (
        mode == "copilot"
        and (baseline == "with-plugin" or review_gates)
        and plugin_root is None
        and apm_project_root is None
    ):
        raise RuntimeError(
            "Copilot evals require an intact plugin or staged APM project"
        )
    case_dir = workspace / case["id"] / run_name
    if case_dir.exists():
        shutil.rmtree(case_dir)
    outputs = case_dir / "outputs"
    outputs.mkdir(parents=True, exist_ok=True)
    copy_inputs(case, case_dir)
    if (
        mode == "copilot"
        and baseline == "with-plugin"
        and apm_project_root is not None
        and not case_dir.is_relative_to(apm_project_root)
    ):
        raise RuntimeError(
            "APM with-plugin runs must be inside the staged project"
        )

    case_prompt = (
        case["baseline_prompt"] if baseline == "no-plugin" else case["prompt"]
    )
    manifest = {
        "case": case["id"],
        "mode": mode,
        "model": model,
        "review_gate_model": REVIEW_MODEL,
        "baseline": baseline,
        "review_gates": review_gates,
        "plugin_root": str(plugin_root) if plugin_root is not None else None,
        "apm_project_root": (
            str(apm_project_root) if apm_project_root is not None else None
        ),
        "plugin_discovery": (
            "no-plugin-isolation"
            if baseline == "no-plugin"
            else "explicit-plugin"
            if plugin_root is not None
            else "staged-apm-project"
            if apm_project_root is not None
            else "none"
        ),
        "discovery_evidence": (
            "component-discovery.json"
            if mode == "copilot"
            and (baseline == "no-plugin" or apm_project_root is not None)
            else None
        ),
        "run_dir": str(case_dir),
        "expected_files": case.get("expected_files", []),
        "prompt": case_prompt,
    }
    (case_dir / "run-manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    if mode == "dry-run":
        return {
            "case": case["id"],
            "status": "prepared",
            "run_dir": str(case_dir),
        }

    default_agent = (
        AGENT_BY_CASE.get(case["id"]) if baseline == "with-plugin" else None
    )
    if (
        default_agent is not None
        and plugin_root is None
        and apm_project_root is not None
    ):
        default_agent = project_agent_name(default_agent)
    agent = explicit_agent or default_agent
    prompt = (
        f"{case_prompt}\n\n"
        "Work only in the current directory. "
        "Do not modify files outside the current directory. "
        "Do not use network access or shell commands."
    )
    if case.get("expected_files"):
        prompt += " Write requested files under the existing outputs directory."
    command = ["copilot"]
    if agent:
        command.extend(["--agent", agent])
    command.extend(
        [
            "--model",
            model,
            "-C",
            str(case_dir),
            "-p",
            prompt,
            "--allow-tool=read",
            "--allow-tool=write",
            "--allow-tool=edit",
            "--allow-tool=search",
            "--deny-tool=execute",
            "--deny-tool=shell",
            "--silent",
        ]
    )
    if baseline == "with-plugin" and plugin_root is not None:
        command[command.index("-p") : command.index("-p")] = [
            "--plugin-dir",
            str(plugin_root),
        ]
    elif baseline == "with-plugin":
        if apm_project_root is None:
            raise RuntimeError("Missing staged APM project")
        discovery = assert_apm_target_components(case_dir, apm_project_root)
        (case_dir / "component-discovery.json").write_text(
            json.dumps(discovery, indent=2, ensure_ascii=False),
            encoding="utf-8",
        )
    else:
        discovery = assert_no_target_components(case_dir)
        (case_dir / "component-discovery.json").write_text(
            json.dumps(discovery, indent=2, ensure_ascii=False),
            encoding="utf-8",
        )

    workspace_before = snapshot_files(case_dir)
    started = time.perf_counter()
    result = subprocess.run(
        command,
        cwd=case_dir,
        text=True,
        capture_output=True,
        check=False,
        env=COPILOT_ENV,
    )
    duration_ms = int((time.perf_counter() - started) * 1000)
    workspace_diff = compare_snapshots(
        workspace_before, snapshot_files(case_dir)
    )
    workspace_diff_path = case_dir / "workspace-diff.json"
    workspace_diff_path.write_text(
        json.dumps(workspace_diff, indent=2),
        encoding="utf-8",
    )
    response = case_dir / "response.txt"
    response.write_text(result.stdout, encoding="utf-8")
    (case_dir / "stderr.txt").write_text(result.stderr, encoding="utf-8")
    (case_dir / "timing.json").write_text(
        json.dumps({"duration_ms": duration_ms}, indent=2),
        encoding="utf-8",
    )
    if result.returncode != 0:
        return {
            "case": case["id"],
            "status": "copilot-failed",
            "returncode": result.returncode,
            "run_dir": str(case_dir),
        }

    grading_returncode: int | None = None
    if needs_grading(case):
        check = subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--evals",
                str(EVALS),
                "--case",
                case["id"],
                "--outputs",
                str(outputs),
                "--run-dir",
                str(case_dir),
                "--response",
                str(response),
                "--workspace-diff",
                str(workspace_diff_path),
            ],
            text=True,
            capture_output=True,
            check=False,
        )
        (case_dir / "grading-stdout.txt").write_text(
            check.stdout, encoding="utf-8"
        )
        (case_dir / "grading-stderr.txt").write_text(
            check.stderr, encoding="utf-8"
        )
        grading_returncode = check.returncode

    gate_results: list[dict] = []
    review_gate_failed = False
    if review_gates:
        gate_results = run_review_gates(
            case, case_dir, plugin_root, apm_project_root
        )
        review_gate_failed = any(
            gate["returncode"] != 0 or gate["verdict"] != "PASS"
            for gate in gate_results
        )

    result_payload = {
        "case": case["id"],
        "review_gates": gate_results,
        "run_dir": str(case_dir),
    }
    if grading_returncode not in {None, 0}:
        return {
            **result_payload,
            "status": "grading-failed",
            "returncode": grading_returncode,
        }
    if review_gate_failed:
        return {**result_payload, "status": "review-gate-failed"}
    return {**result_payload, "status": "passed"}


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Prepare or run enterprise translation plugin eval cases."
    )
    parser.add_argument(
        "--mode", choices=["dry-run", "copilot"], default="dry-run"
    )
    parser.add_argument(
        "--baseline",
        choices=["with-plugin", "no-plugin"],
        default="with-plugin",
    )
    parser.add_argument("--case", help="Eval case id. Defaults to all cases.")
    parser.add_argument(
        "--workspace", type=Path, help="Disposable eval workspace."
    )
    parser.add_argument(
        "--agent", help="Override the Copilot custom agent identifier."
    )
    plugin_source = parser.add_mutually_exclusive_group()
    plugin_source.add_argument(
        "--plugin-root",
        type=Path,
        help=(
            "Intact source or packed plugin root. Auto-detected when the "
            "runner is inside a plugin."
        ),
    )
    plugin_source.add_argument(
        "--apm-root",
        type=Path,
        help=(
            "Repository root containing the deployed .agents skill and "
            ".github agents. Copilot mode stages and evaluates that layout "
            "without --plugin-dir."
        ),
    )
    parser.add_argument(
        "--model", default="gpt-5.5", help="Copilot model for eval runs."
    )
    parser.add_argument(
        "--skip-review-gates",
        action="store_true",
        help="Skip GPT-5.5 positive/negative review gates for copilot mode.",
    )
    args = parser.parse_args(argv)
    if args.baseline == "no-plugin" and args.agent is not None:
        parser.error("--agent cannot be used with --baseline no-plugin")
    plugin_root_candidate = (
        args.plugin_root
        if args.plugin_root is not None
        else DEFAULT_PLUGIN_ROOT
        if args.apm_root is None
        else None
    )
    try:
        plugin_root = (
            validate_plugin_root(plugin_root_candidate)
            if plugin_root_candidate is not None
            else None
        )
        apm_root = (
            validate_apm_root(args.apm_root)
            if args.apm_root is not None
            else None
        )
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    review_gates = args.mode == "copilot" and not args.skip_review_gates
    if (
        args.mode == "copilot"
        and (args.baseline == "with-plugin" or review_gates)
        and plugin_root is None
        and apm_root is None
    ):
        parser.error(
            "copilot mode requires an intact auto-detected plugin, "
            "--plugin-root, or --apm-root"
        )

    workspace = build_workspace(args.workspace)
    apm_project_root = (
        stage_apm_project(apm_root, workspace)
        if args.mode == "copilot" and apm_root is not None
        else None
    )
    case_workspace = (
        apm_project_root / "runs"
        if apm_project_root is not None and args.baseline == "with-plugin"
        else workspace / "no-plugin-runs"
        if apm_project_root is not None
        else workspace
    )
    results = [
        run_case(
            case,
            case_workspace,
            args.mode,
            args.model,
            args.agent,
            args.baseline,
            review_gates,
            plugin_root,
            apm_project_root,
        )
        for case in select_cases(args.case)
    ]
    summary = {
        "workspace": str(workspace),
        "apm_project_root": (
            str(apm_project_root) if apm_project_root is not None else None
        ),
        "results": results,
    }
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    if any(
        result["status"] not in {"prepared", "passed"} for result in results
    ):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
