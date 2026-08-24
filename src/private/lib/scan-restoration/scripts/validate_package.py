"""Validate the scan restoration Copilot CLI plugin package."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = PACKAGE_ROOT / "skills"

PACKAGE_NAME = "scan-restoration"
PACKAGE_VERSION = "0.1.0"
PACKAGE_DESCRIPTION = (
    "Copilot CLI skills for scanned-book diagnostics, restoration, layout, "
    "and quality control."
)
PACKAGE_AUTHOR = "hcoona"
PACKAGE_LICENSE = "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"

EXPECTED_SKILLS = (
    "scan-batch-diagnostics",
    "scan-book-layout",
    "scan-book-quality-control",
    "scan-page-rectification",
    "scan-tone-restoration",
)
REQUIRED_FILES = {
    "scan-batch-diagnostics": (
        "scripts/analyze_scans.py",
        "scripts/check_runtime.py",
        "scripts/requirements.lock",
        "scripts/run.ps1",
        "scripts/run_tests.py",
        "tests/test_analyze_scans.py",
        "tests/test_runtime_readiness.py",
    ),
    "scan-book-layout": (
        "scripts/normalize_book.py",
        "scripts/requirements.lock",
        "scripts/run.ps1",
        "tests/run.ps1",
        "tests/test_normalize_book.py",
    ),
    "scan-book-quality-control": (
        "scripts/requirements.lock",
        "scripts/run.ps1",
        "scripts/validate_book.py",
        "tests/run.ps1",
        "tests/test_validate_book.py",
    ),
    "scan-page-rectification": (
        "scripts/rectify_pages.py",
        "scripts/requirements.lock",
        "scripts/run.ps1",
        "tests/run.ps1",
        "tests/test_rectify_pages.py",
        "tests/test_runner.ps1",
    ),
    "scan-tone-restoration": (
        "scripts/requirements.lock",
        "scripts/restore_tone.py",
        "scripts/run.ps1",
        "tests/run.ps1",
        "tests/test_restore_tone.py",
        "tests/test_runner.ps1",
    ),
}
EXPECTED_SKILL_FILES = frozenset(
    f"{skill_name}/{relative_path}"
    for skill_name in EXPECTED_SKILLS
    for relative_path in ("SKILL.md", *REQUIRED_FILES[skill_name])
)
EXPECTED_INCLUDES = tuple(
    f"skills/{skill_name}/{relative_path}"
    for skill_name in EXPECTED_SKILLS
    for relative_path in ("SKILL.md", *REQUIRED_FILES[skill_name])
)
REQUIRED_REFERENCES = {
    "scan-book-layout": {"scan-book-quality-control"},
    "scan-page-rectification": {
        "scan-batch-diagnostics",
        "scan-book-quality-control",
    },
    "scan-tone-restoration": {"scan-batch-diagnostics"},
}
NAME_PATTERN = re.compile(r"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")
REFERENCE_PATTERN = re.compile(r"/(scan-[a-z0-9-]+)\b")
APM_TOP_LEVEL_KEY_PATTERN = re.compile(r"^([A-Za-z][A-Za-z0-9]*):(.*)$")
APM_NESTED_KEY_PATTERN = re.compile(r"^  ([A-Za-z][A-Za-z0-9]*):(.*)$")
APM_TOP_LEVEL_KEYS = frozenset(
    {
        "name",
        "version",
        "description",
        "author",
        "license",
        "targets",
        "dependencies",
        "includes",
        "devDependencies",
        "scripts",
    }
)
MIN_QUOTED_LENGTH = 2
MIN_DESCRIPTION_LENGTH = 1
MAX_DESCRIPTION_LENGTH = 1024
MAX_SKILL_BODY_LINES = 500


def unquote(value: str) -> str:
    """Remove matching single or double quotes from a scalar."""
    if (
        len(value) >= MIN_QUOTED_LENGTH
        and value[0] == value[-1]
        and value[0] in {"'", '"'}
    ):
        return value[1:-1]
    return value


def add_apm_top_level_section(
    line: str,
    line_number: int,
    sections: dict[str, list[str]],
    errors: list[str],
) -> str | None:
    """Add one canonical top-level APM section and return its key."""
    match = APM_TOP_LEVEL_KEY_PATTERN.fullmatch(line)
    if match is None:
        errors.append(
            f"apm.yml line {line_number} has unsupported top-level syntax"
        )
        return None

    key, value = match.groups()
    if key not in APM_TOP_LEVEL_KEYS:
        errors.append(f"apm.yml has unknown top-level key {key!r}")
        return None
    if key in sections:
        errors.append(f"apm.yml has duplicate top-level key {key!r}")
        return None

    sections[key] = [value]
    return key


def parse_apm_sections(
    text: str,
    errors: list[str],
) -> dict[str, tuple[str, ...]] | None:
    """Split the canonical APM manifest into constrained top-level sections."""
    sections: dict[str, list[str]] = {}
    current_key: str | None = None
    invalid = False

    for line_number, line in enumerate(text.splitlines(), start=1):
        if not line.strip():
            continue
        if "\t" in line:
            errors.append(f"apm.yml line {line_number} must not contain tabs")
            invalid = True
            current_key = None
            continue
        if line.startswith(" "):
            if current_key is None:
                errors.append(
                    f"apm.yml line {line_number} has unsupported indentation"
                )
                invalid = True
            else:
                sections[current_key].append(line)
            continue

        current_key = add_apm_top_level_section(
            line,
            line_number,
            sections,
            errors,
        )
        if current_key is None:
            invalid = True

    missing_keys = APM_TOP_LEVEL_KEYS.difference(sections)
    if missing_keys:
        errors.append(
            "apm.yml is missing top-level keys: "
            f"{', '.join(sorted(missing_keys))}"
        )
        invalid = True
    if invalid:
        return None
    return {key: tuple(value) for key, value in sections.items()}


def parse_apm_string(value: str) -> str | None:
    """Parse one plain or simply quoted string without YAML extensions."""
    if not value or value != value.strip():
        return None
    if value[0] not in {"'", '"'}:
        if value[-1] in {"'", '"'}:
            return None
        return value
    if len(value) < MIN_QUOTED_LENGTH or value[-1] != value[0]:
        return None
    inner = value[1:-1]
    if value[0] in inner or "\\" in inner:
        return None
    return inner


def parse_apm_scalar(section: tuple[str, ...]) -> str | None:
    """Parse a section containing one inline string scalar."""
    if len(section) != 1 or not section[0].startswith(" "):
        return None
    return parse_apm_string(section[0][1:])


def parse_apm_list(section: tuple[str, ...]) -> tuple[str, ...] | None:
    """Parse a section containing a canonical block string list."""
    if not section or section[0]:
        return None

    values: list[str] = []
    for line in section[1:]:
        if not line.startswith("  - "):
            return None
        value = parse_apm_string(line.removeprefix("  - "))
        if value is None:
            return None
        values.append(value)
    return tuple(values)


def validate_apm_empty_list_mapping(
    section_name: str,
    section: tuple[str, ...],
    expected_keys: tuple[str, ...],
    errors: list[str],
) -> None:
    """Validate a canonical mapping whose values are empty block lists."""
    if not section or section[0]:
        errors.append(
            f"apm.yml {section_name} must be a canonical block mapping"
        )
        return

    expected = set(expected_keys)
    actual: set[str] = set()
    for line in section[1:]:
        match = APM_NESTED_KEY_PATTERN.fullmatch(line)
        if match is None:
            errors.append(
                f"apm.yml {section_name} has unsupported nested syntax"
            )
            continue
        key, value = match.groups()
        if key in actual:
            errors.append(
                f"apm.yml {section_name} has duplicate nested key {key!r}"
            )
        actual.add(key)
        if key not in expected:
            errors.append(
                f"apm.yml {section_name} has unknown nested key {key!r}"
            )
        if value != " []":
            errors.append(f"apm.yml {section_name}.{key} must be exactly []")

    missing_keys = expected.difference(actual)
    if missing_keys:
        errors.append(
            f"apm.yml {section_name} is missing nested keys: "
            f"{', '.join(sorted(missing_keys))}"
        )


def parse_frontmatter(path: Path) -> tuple[dict[str, str], int]:
    """Parse the scalar frontmatter fields used by the scan skills."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0] != "---":
        return {}, 0
    try:
        end = lines.index("---", 1)
    except ValueError:
        return {}, 0

    fields: dict[str, str] = {}
    index = 1
    while index < end:
        line = lines[index]
        if not line or line.startswith((" ", "\t")) or ":" not in line:
            index += 1
            continue
        key, raw_value = line.split(":", 1)
        value = raw_value.strip()
        if value in {">", ">-", "|", "|-"}:
            parts: list[str] = []
            index += 1
            while index < end and (
                not lines[index] or lines[index].startswith((" ", "\t"))
            ):
                parts.append(lines[index].strip())
                index += 1
            value = " ".join(part for part in parts if part)
        else:
            index += 1
        fields[key] = unquote(value)
    return fields, len(lines) - end - 1


def validate_plugin_manifest(errors: list[str]) -> None:
    """Validate plugin metadata and its cross-manifest contract."""
    path = PACKAGE_ROOT / "plugin.json"
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        errors.append(f"plugin.json is invalid JSON: {error}")
        return

    expected = {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "description": PACKAGE_DESCRIPTION,
        "license": PACKAGE_LICENSE,
        "skills": "skills/",
    }
    for key, value in expected.items():
        if manifest.get(key) != value:
            errors.append(f"plugin.json {key!r} must be {value!r}")
    if manifest.get("author") != {"name": PACKAGE_AUTHOR}:
        errors.append("plugin.json author must name hcoona")
    if "agents" in manifest:
        errors.append(
            "plugin.json must not declare agents for a skills-only package"
        )


def validate_apm_manifest(errors: list[str]) -> None:
    """Validate the package APM manifest and deployable-content list."""
    text = (PACKAGE_ROOT / "apm.yml").read_text(encoding="utf-8")
    sections = parse_apm_sections(text, errors)
    if sections is None:
        return

    expected_scalars = {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "description": PACKAGE_DESCRIPTION,
        "author": PACKAGE_AUTHOR,
        "license": PACKAGE_LICENSE,
    }
    for key, value in expected_scalars.items():
        if parse_apm_scalar(sections[key]) != value:
            errors.append(f"apm.yml {key!r} must be {value!r}")
    if parse_apm_list(sections["targets"]) != ("copilot",):
        errors.append("apm.yml must target only GitHub Copilot")
    if parse_apm_list(sections["includes"]) != EXPECTED_INCLUDES:
        errors.append(
            "apm.yml includes must enumerate the five complete skill trees"
        )
    validate_apm_empty_list_mapping(
        "dependencies",
        sections["dependencies"],
        ("apm", "mcp"),
        errors,
    )
    validate_apm_empty_list_mapping(
        "devDependencies",
        sections["devDependencies"],
        ("apm",),
        errors,
    )
    if sections["scripts"] != (" {}",):
        errors.append("apm.yml scripts must be exactly {}")


def validate_skill(skill_name: str, errors: list[str]) -> None:
    """Validate one skill's metadata, resources, references, and source."""
    skill_root = SKILLS_ROOT / skill_name
    skill_path = skill_root / "SKILL.md"
    validate_skill_metadata(skill_name, skill_path, errors)
    validate_skill_resources(skill_name, skill_root, errors)
    validate_skill_references(skill_name, skill_path, errors)
    validate_python_sources(skill_name, skill_root, errors)


def validate_skill_metadata(
    skill_name: str,
    skill_path: Path,
    errors: list[str],
) -> None:
    """Validate one skill's frontmatter and body size."""
    fields, body_lines = parse_frontmatter(skill_path)
    if fields.get("name") != skill_name:
        errors.append(
            f"{skill_name}: frontmatter name must match its directory"
        )
    description = fields.get("description", "")
    if not MIN_DESCRIPTION_LENGTH <= len(description) <= MAX_DESCRIPTION_LENGTH:
        errors.append(
            f"{skill_name}: description must contain 1-1024 characters"
        )
    compatibility = fields.get("compatibility", "")
    if (
        "GitHub Copilot CLI" not in compatibility
        or "Windows" not in compatibility
    ):
        errors.append(
            f"{skill_name}: compatibility must identify Copilot CLI on Windows"
        )
    if body_lines > MAX_SKILL_BODY_LINES:
        errors.append(f"{skill_name}: SKILL.md body exceeds 500 lines")


def validate_skill_resources(
    skill_name: str,
    skill_root: Path,
    errors: list[str],
) -> None:
    """Validate one skill's required runtime and test resources."""
    for relative_path in REQUIRED_FILES[skill_name]:
        if not (skill_root / relative_path).is_file():
            errors.append(f"{skill_name}: missing {relative_path}")
    lock = skill_root / "scripts" / "requirements.lock"
    if lock.is_file():
        lock_text = lock.read_text(encoding="utf-8")
        if "--hash=sha256:" not in lock_text:
            errors.append(
                f"{skill_name}: requirements.lock must contain pinned hashes"
            )


def validate_skill_references(
    skill_name: str,
    skill_path: Path,
    errors: list[str],
) -> None:
    """Validate one skill's companion-skill references."""
    documentation = skill_path.read_text(encoding="utf-8")
    references = set(REFERENCE_PATTERN.findall(documentation))
    unknown_references = references.difference(EXPECTED_SKILLS)
    if unknown_references:
        errors.append(
            f"{skill_name}: unknown companion skills "
            f"{', '.join(sorted(unknown_references))}"
        )
    missing_references = REQUIRED_REFERENCES.get(skill_name, set()).difference(
        references
    )
    if missing_references:
        errors.append(
            f"{skill_name}: missing companion references "
            f"{', '.join(sorted(missing_references))}"
        )


def validate_python_sources(
    skill_name: str,
    skill_root: Path,
    errors: list[str],
) -> None:
    """Compile one skill's Python sources without importing dependencies."""
    for source_path in sorted(skill_root.rglob("*.py")):
        try:
            compile(
                source_path.read_text(encoding="utf-8"),
                str(source_path),
                "exec",
            )
        except SyntaxError as error:
            errors.append(
                f"{skill_name}: invalid Python in {source_path.name}: {error}"
            )


def find_repository_root() -> Path | None:
    """Find the hosting monorepo when validation runs from a checkout."""
    for candidate in PACKAGE_ROOT.parents:
        if (candidate / "mise.toml").is_file() and (
            candidate / "apm.yml"
        ).is_file():
            return candidate
    return None


def validate_skill_file_allowlist(errors: list[str]) -> None:
    """Reject symlinks and files outside the package's explicit allowlist."""
    actual_skill_files: set[str] = set()
    for path in sorted(SKILLS_ROOT.rglob("*")):
        relative_path = path.relative_to(SKILLS_ROOT).as_posix()
        if path.is_symlink():
            errors.append(
                f"skill content must not use symlinks: {relative_path}"
            )
        elif path.is_file():
            actual_skill_files.add(relative_path)
    for relative_path in sorted(actual_skill_files - EXPECTED_SKILL_FILES):
        errors.append(f"unexpected skill file is not allowed: {relative_path}")


def validate_layout(errors: list[str]) -> None:
    """Validate package structure, skill inventory, and cache hygiene."""
    if not NAME_PATTERN.fullmatch(PACKAGE_NAME):
        errors.append("package name is not valid kebab-case")
    if SKILLS_ROOT.is_symlink():
        errors.append("skill content must not use symlinks: skills/")
    else:
        actual_skills = tuple(
            sorted(path.name for path in SKILLS_ROOT.iterdir() if path.is_dir())
        )
        if actual_skills != EXPECTED_SKILLS:
            errors.append(
                "skills/ must contain exactly the five declared scan skills"
            )
        for skill_name in EXPECTED_SKILLS:
            validate_skill(skill_name, errors)
        validate_skill_file_allowlist(errors)
    if (PACKAGE_ROOT / "apm.lock.yaml").exists():
        errors.append("the package must not contain a local apm.lock.yaml")

    repository_root = find_repository_root()
    if repository_root is not None:
        stale_root = repository_root / ".apm" / "skills"
        stale_skills = [
            skill_name
            for skill_name in EXPECTED_SKILLS
            if (stale_root / skill_name).exists()
        ]
        if stale_skills:
            errors.append("legacy .apm/skills scan sources must not remain")


def validate_readme(errors: list[str]) -> None:
    """Validate the package's ownership and supported-runtime documentation."""
    text = (PACKAGE_ROOT / "README.md").read_text(encoding="utf-8")
    required_phrases = (
        "GitHub Copilot CLI on Windows only",
        "public Python Package Index (PyPI)",
        ".agents/skills/scan-*",
        "SCAN_RESTORATION_FIXTURE_ROOT",
        PACKAGE_LICENSE,
    )
    for phrase in required_phrases:
        if phrase not in text:
            errors.append(f"README.md must document {phrase!r}")
    test_entrypoint = (
        r"powershell\.exe -NoProfile -ExecutionPolicy Bypass -File "
        r"\.\\tests\\run\.ps1"
    )
    for skill_name in ("scan-page-rectification", "scan-tone-restoration"):
        if (
            re.search(
                rf"# {re.escape(skill_name)}\s+{test_entrypoint}",
                text,
            )
            is None
        ):
            errors.append(
                "README.md must use the locked test entrypoint "
                f"for {skill_name}"
            )


def main() -> int:
    """Run all package validations."""
    errors: list[str] = []
    validate_plugin_manifest(errors)
    validate_apm_manifest(errors)
    validate_layout(errors)
    validate_readme(errors)
    if errors:
        print("scan-restoration package validation failed:", file=sys.stderr)
        for error in sorted(set(errors)):
            print(f"- {error}", file=sys.stderr)
        return 1
    print("scan-restoration package validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
