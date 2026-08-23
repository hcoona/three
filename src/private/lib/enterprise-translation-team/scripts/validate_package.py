"""Static validator for the enterprise translation team Copilot plugin."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAME_RE = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")


def fail(message: str) -> None:
    raise AssertionError(message)


def read_frontmatter(path: Path) -> tuple[dict[str, str], str]:
    text = (
        path.read_text(encoding="utf-8")
        .replace("\r\n", "\n")
        .replace("\r", "\n")
    )
    if not text.startswith("---\n"):
        fail(f"{path} must start with YAML frontmatter")
    marker = "\n---\n"
    end = text.find(marker, 4)
    if end < 0:
        fail(f"{path} must close YAML frontmatter")
    raw = text[4:end]
    body = text[end + len(marker) :]
    data: dict[str, str] = {}
    for line in raw.splitlines():
        if not line.strip() or line.startswith("  "):
            continue
        if ":" not in line:
            fail(f"{path} has unsupported frontmatter line: {line}")
        key, value = line.split(":", 1)
        data[key.strip()] = value.strip().strip('"')
    return data, body


def validate_plugin_json() -> None:
    manifest = json.loads((ROOT / "plugin.json").read_text(encoding="utf-8"))
    name = manifest.get("name", "")
    if not NAME_RE.fullmatch(name):
        fail(f"plugin.json name must be kebab-case <=64 chars: {name!r}")
    if manifest.get("agents") != "agents/":
        fail("plugin.json agents path must be agents/")
    if manifest.get("skills") != "skills/":
        fail("plugin.json skills path must be skills/")


def validate_agents() -> None:
    agent_paths = sorted((ROOT / "agents").glob("*.agent.md"))
    if not agent_paths:
        fail("Expected at least one custom agent")
    agents_by_name: dict[str, dict[str, str]] = {}
    bodies_by_name: dict[str, str] = {}
    for path in agent_paths:
        frontmatter, body = read_frontmatter(path)
        name = frontmatter.get("name", "")
        description = frontmatter.get("description", "")
        if not NAME_RE.fullmatch(name):
            fail(f"{path} name must be kebab-case <=64 chars: {name!r}")
        if not description or len(description) > 1024:
            fail(f"{path} description must be 1..1024 chars")
        if len(body) > 30000:
            fail(f"{path} body exceeds custom agent prompt limit")
        agents_by_name[name] = frontmatter
        bodies_by_name[name] = body
    for reviewer in [
        "translation-positive-reviewer",
        "translation-negative-reviewer",
    ]:
        if reviewer not in agents_by_name:
            fail(f"Missing GPT-5.5 review gate agent: {reviewer}")
        if agents_by_name[reviewer].get("model") != "gpt-5.5":
            fail(f"{reviewer} must pin model: gpt-5.5")
        if agents_by_name[reviewer].get("disable-model-invocation") == "true":
            fail(f"{reviewer} must allow workflow-lead model invocation")
        if "compare the source and target" not in bodies_by_name[reviewer]:
            fail(f"{reviewer} must define source-target content review")
    for required in [
        "schema_version",
        "standard_basis",
        "competing_targets",
        "forbidden_term",
        "langSec/termSec/term",
    ]:
        if required not in bodies_by_name["translation-terminologist"]:
            fail(
                "translation-terminologist missing direct schema contract: "
                f"{required}"
            )
    for required in [
        "missing-approved-term",
        "projection-mismatch",
        "Human or subject-matter expert sign-off still required.",
    ]:
        if required not in bodies_by_name["translation-qa-engineer"]:
            fail(f"translation-qa-engineer missing QA contract: {required}")


def validate_skill() -> None:
    skills_root = ROOT / "skills"
    skill_dirs = [p for p in skills_root.iterdir() if p.is_dir()]
    if len(skill_dirs) != 1:
        fail("Expected exactly one skill directory")
    skill_dir = skill_dirs[0]
    skill_path = skill_dir / "SKILL.md"
    frontmatter, body = read_frontmatter(skill_path)
    name = frontmatter.get("name", "")
    description = frontmatter.get("description", "")
    if name != skill_dir.name:
        fail(f"Skill name {name!r} must match directory {skill_dir.name!r}")
    if not NAME_RE.fullmatch(name):
        fail(f"Skill name must be kebab-case <=64 chars: {name!r}")
    if not description or len(description) > 1024:
        fail("Skill description must be 1..1024 chars")
    if len(body.splitlines()) > 500:
        fail(
            "SKILL.md body must stay under 500 lines for progressive disclosure"
        )
    for required in [
        "references/roles.md",
        "references/context-packet.md",
        "references/evaluation.md",
        "references/mqm-taxonomy.md",
        "references/output-contracts.md",
        "references/terminology-schema.md",
        "evals/evals.json",
        "scripts/check_translation_outputs.py",
        "scripts/run_copilot_evals.py",
    ]:
        if not (skill_dir / required).exists():
            fail(f"Missing skill resource: {required}")
    runner = (skill_dir / "scripts" / "run_copilot_evals.py").read_text(
        encoding="utf-8"
    )
    if '"--apm-root"' not in runner:
        fail("Eval runner must support APM project-discovery mode")


def validate_cache_hygiene() -> None:
    cache_paths = sorted(
        path
        for path in ROOT.rglob("*")
        if path.name == "__pycache__" or path.suffix == ".pyc"
    )
    if cache_paths:
        fail(f"Package source must not contain Python bytecode: {cache_paths}")
    repository_lock = ROOT.parents[3] / "apm.lock.yaml"
    if repository_lock.is_file():
        lock_content = repository_lock.read_text(encoding="utf-8")
        if "__pycache__" in lock_content or ".pyc" in lock_content:
            fail("apm.lock.yaml must not deploy Python bytecode")


def validate_evals() -> None:
    evals_path = (
        ROOT / "skills" / "enterprise-translation-team" / "evals" / "evals.json"
    )
    payload = json.loads(evals_path.read_text(encoding="utf-8"))
    if payload.get("skill_name") != "enterprise-translation-team":
        fail("evals.json skill_name must match skill")
    review_gates = payload.get("review_gates", {})
    if review_gates.get("model") != "gpt-5.5":
        fail("evals.json review_gates.model must be gpt-5.5")
    if not review_gates.get("positive_agent") or not review_gates.get(
        "negative_agent"
    ):
        fail("evals.json must define positive and negative review gate agents")
    if len(payload.get("dataset_registry", [])) < 3:
        fail("evals.json must register multiple public dataset candidates")
    evals = payload.get("evals", [])
    if len(evals) < 3:
        fail("evals.json must include at least three eval cases")
    seen: set[str] = set()
    for case in evals:
        case_id = case.get("id", "")
        if not NAME_RE.fullmatch(case_id):
            fail(f"Eval id must be kebab-case: {case_id!r}")
        if case_id in seen:
            fail(f"Duplicate eval id: {case_id}")
        seen.add(case_id)
        for field in ["prompt", "expected_output", "assertions"]:
            if not case.get(field):
                fail(f"Eval {case_id} missing {field}")
        if not case.get("baseline_prompt"):
            fail(f"Eval {case_id} missing baseline_prompt")
        if "/enterprise-translation-team" in case["baseline_prompt"]:
            fail(
                f"Eval {case_id} baseline_prompt must not invoke the plugin skill"
            )
        if not (
            case.get("expected_files")
            or case.get("forbidden_created_paths")
            or case.get("required_response_patterns")
            or case.get("forbidden_response_patterns")
        ):
            fail(f"Eval {case_id} has no objective checks")
        if not isinstance(case.get("assertions"), list):
            fail(f"Eval {case_id} assertions must be a list")
        expected_files = case.get("expected_files", [])
        if len(expected_files) != len(set(expected_files)):
            fail(f"Eval {case_id} has duplicate expected_files")
        for expected in expected_files:
            expected_path = Path(expected)
            if (
                expected_path.is_absolute()
                or ".." in expected_path.parts
                or expected_path.name != expected
            ):
                fail(
                    f"Eval {case_id} expected file must be a simple relative "
                    f"name: {expected!r}"
                )
        if case.get("forbid_workspace_changes") and expected_files:
            fail(f"Eval {case_id} cannot expect files while forbidding changes")
        if case_id == "mqm-review-json" and not case.get("seeded_defects"):
            fail("mqm-review-json must define seeded_defects")
        if case_id == "mqm-review-json" and (
            "issue_id" not in case["prompt"]
            or "resolution_status" not in case["prompt"]
        ):
            fail("mqm-review-json must request stable issue resolution fields")
        if case_id == "terminology-glossary-conflict" and (
            "exactly match canonical JSON" not in case["prompt"]
            or "conflict_id" not in case["prompt"]
        ):
            fail(
                "terminology-glossary-conflict must bind projections and conflicts"
            )
        if case_id == "final-qa-contract":
            for required in [
                "missing-approved-term",
                "projection-mismatch",
                "still required",
            ]:
                if required not in case["prompt"]:
                    fail(f"final-qa-contract prompt missing: {required}")
        if case_id == "negative-no-durable-wiki-write":
            for prompt_field in ["prompt", "baseline_prompt"]:
                if "Do not edit files" in case[prompt_field]:
                    fail(
                        "negative no-write eval must not duplicate the skill "
                        f"safeguard in {prompt_field}"
                    )
        for file_name in case.get("files", []):
            path = evals_path.parent.parent / file_name
            if not path.exists():
                fail(f"Eval {case_id} references missing file: {file_name}")


def main() -> int:
    validate_plugin_json()
    validate_agents()
    validate_skill()
    validate_cache_hygiene()
    validate_evals()
    print(json.dumps({"plugin": "enterprise-translation-team", "status": "ok"}))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # noqa: BLE001
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1) from None
