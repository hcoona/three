"""Validate scholarly-publication canonical source and runtime deployment."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import tomllib
from pathlib import Path
from typing import TYPE_CHECKING, Any, Never

from jsonschema import Draft202012Validator
from jsonschema.exceptions import SchemaError

if TYPE_CHECKING:
    from collections.abc import Iterator

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = PACKAGE_ROOT / "skills"
EVALS_ROOT = PACKAGE_ROOT / "evals"
TESTS_ROOT = PACKAGE_ROOT / "tests"

PLUGIN_NAME = "scholarly-publication"
PLUGIN_VERSION = "0.1.0"
APM_NAME = "scholarly-publication"
EXPECTED_SKILLS = (
    "scholarly-pdf-reconstruction",
    "scholarly-print-assembly",
    "scholarly-render-qa",
)
TEST_FILES = {
    "scholarly-pdf-reconstruction": "test_reconstruct_pdf.py",
    "scholarly-print-assembly": "test_assemble_print.py",
    "scholarly-render-qa": "test_audit_publication.py",
}
VALIDATOR_TEST_FILE = "test_validate_package.py"
RUNTIME_DIRECTORIES = frozenset({"assets", "references", "scripts"})
CANONICAL_ONLY_DIRECTORIES = frozenset({"evals", "tests"})
SHARED_FILE_PAIRS = (
    (
        "skills/scholarly-pdf-reconstruction/assets/source-package.schema.json",
        "skills/scholarly-print-assembly/assets/source-package.schema.json",
    ),
    (
        "skills/scholarly-pdf-reconstruction/assets/source-blocks.schema.json",
        "skills/scholarly-print-assembly/assets/source-blocks.schema.json",
    ),
    (
        "skills/scholarly-pdf-reconstruction/assets/figure-map.schema.json",
        "skills/scholarly-print-assembly/assets/figure-map.schema.json",
    ),
    (
        "skills/scholarly-print-assembly/assets/assembly-manifest.schema.json",
        "skills/scholarly-render-qa/assets/assembly-manifest.schema.json",
    ),
    (
        "skills/scholarly-print-assembly/assets/publication-profile.json",
        "skills/scholarly-render-qa/assets/publication-profile.json",
    ),
)

SKILL_NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
MARKDOWN_LINK_PATTERN = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
RESOURCE_PATH_PATTERN = re.compile(
    r"(?<![A-Za-z0-9_.-])"
    r"((?:references|scripts|assets)/[A-Za-z0-9_.\-/]+)"
)
EXACT_PYTHON_PIN_PATTERN = re.compile(r"^==[0-9]+\.[0-9]+\.[0-9]+$")
EXACT_DEPENDENCY_PIN_PATTERN = re.compile(
    r"^[A-Za-z0-9][A-Za-z0-9._-]*==[A-Za-z0-9][A-Za-z0-9._+!-]*$"
)
ALLOWED_SUFFIXES = frozenset({".css", ".json", ".md", ".py", ".yml"})
MAX_FILE_BYTES = 1024 * 1024
MAX_SKILL_LINES = 500
# fmt: off
FIXED_ELEMENT_ATTRIBUTES = {
    "a": frozenset({"href"}),
    "abbr": frozenset(),
    "address": frozenset(),
    "aside": frozenset(),
    "b": frozenset(),
    "bdi": frozenset(),
    "bdo": frozenset(),
    "blockquote": frozenset(),
    "br": frozenset(),
    "caption": frozenset(),
    "cite": frozenset(),
    "code": frozenset(),
    "col": frozenset({"span"}),
    "colgroup": frozenset({"span"}),
    "data": frozenset({"value"}),
    "dd": frozenset(),
    "del": frozenset({"datetime"}),
    "dfn": frozenset(),
    "div": frozenset(),
    "dl": frozenset(),
    "dt": frozenset(),
    "em": frozenset(),
    "h1": frozenset(),
    "h2": frozenset(),
    "h3": frozenset(),
    "h4": frozenset(),
    "h5": frozenset(),
    "h6": frozenset(),
    "hr": frozenset(),
    "i": frozenset(),
    "ins": frozenset({"datetime"}),
    "kbd": frozenset(),
    "li": frozenset({"value"}),
    "mark": frozenset(),
    "ol": frozenset({"reversed", "start", "type"}),
    "p": frozenset(),
    "pre": frozenset(),
    "q": frozenset(),
    "rb": frozenset(),
    "rt": frozenset(),
    "rtc": frozenset(),
    "ruby": frozenset(),
    "s": frozenset(),
    "samp": frozenset(),
    "section": frozenset(),
    "small": frozenset(),
    "span": frozenset(),
    "strong": frozenset(),
    "sub": frozenset(),
    "sup": frozenset(),
    "table": frozenset(),
    "tbody": frozenset(),
    "td": frozenset({"colspan", "headers", "rowspan"}),
    "tfoot": frozenset(),
    "th": frozenset({"abbr", "colspan", "headers", "rowspan", "scope"}),
    "thead": frozenset(),
    "time": frozenset({"datetime"}),
    "tr": frozenset(),
    "u": frozenset(),
    "ul": frozenset(),
    "var": frozenset(),
    "wbr": frozenset(),
}
FIXED_GLOBAL_ATTRIBUTES = frozenset(
    "aria-describedby aria-label aria-labelledby class dir id lang title".split()  # noqa: SIM905
)
FIXED_CSS_PROPERTIES = frozenset(
    "border-bottom-color border-bottom-style border-bottom-width border-collapse border-left-color "  # noqa: SIM905
    "border-left-style border-left-width border-right-color border-right-style border-right-width "
    "border-spacing border-top-color border-top-style border-top-width box-decoration-break break-after "
    "break-before break-inside caption-side color font-family font-kerning font-size font-style "
    "font-variant-caps font-variant-east-asian font-variant-ligatures font-variant-numeric font-weight "
    "hyphens letter-spacing line-break line-height list-style-position list-style-type margin-block-end "
    "margin-block-start margin-bottom margin-inline-end margin-inline-start margin-left margin-right "
    "margin-top orphans overflow-wrap padding-block-end padding-block-start padding-bottom "
    "padding-inline-end padding-inline-start padding-left padding-right padding-top page-break-after "
    "page-break-before page-break-inside ruby-align ruby-position tab-size table-layout text-align "
    "text-align-last text-decoration-color text-decoration-line text-decoration-style "
    "text-decoration-thickness text-indent text-justify text-rendering text-underline-offset "
    "vertical-align white-space widows word-break word-spacing".split()
)
PROFILE_SELECTOR_SURFACE = frozenset(
    "type universal class id lang-attribute lang-pseudo-class child descendant".split()  # noqa: SIM905
)
PROFILE_PROHIBITIONS = frozenset(
    "active-content browser-default-hidden-content css-custom-properties css-functions css-important "  # noqa: SIM905
    "css-pseudo-elements css-url-values event-handler-attributes external-urls inline-style "
    "parser-changing-markup".split()
)
# fmt: on


class ValidationError(ValueError):
    """Report one package contract violation."""


def fail(message: str) -> Never:
    """Raise a package validation error."""
    raise ValidationError(message)


def read_json(path: Path) -> Any:
    """Read one UTF-8 JSON file."""
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        fail(f"cannot parse JSON {path}: {error}")


def parse_apm(path: Path) -> tuple[dict[str, str], dict[str, list[str]]]:
    """Parse the constrained scalar and list syntax used by this APM file."""
    scalars: dict[str, str] = {}
    lists: dict[str, list[str]] = {}
    section: str | None = None
    for line_number, raw_line in enumerate(
        path.read_text(encoding="utf-8").splitlines(),
        start=1,
    ):
        line = raw_line.rstrip()
        if not line or line.lstrip().startswith("#"):
            continue
        section_match = re.fullmatch(r"([A-Za-z][A-Za-z0-9]*):", line)
        if section_match:
            section = section_match.group(1)
            lists.setdefault(section, [])
            continue
        if line.startswith("  - "):
            if section is None:
                fail(f"{path}:{line_number}: list item has no section")
            lists.setdefault(section, []).append(line[4:].strip())
            continue
        if line.startswith("  "):
            continue
        section = None
        scalar_match = re.fullmatch(
            r"([A-Za-z][A-Za-z0-9]*):\s*(.+)",
            line,
        )
        if scalar_match:
            scalars[scalar_match.group(1)] = scalar_match.group(2).strip()
            continue
        fail(f"{path}:{line_number}: unsupported APM YAML")
    return scalars, lists


def parse_frontmatter(path: Path) -> tuple[dict[str, str], list[str]]:
    """Parse scalar Agent Skills frontmatter and return body lines."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0] != "---":
        fail(f"{path} must start with YAML frontmatter")
    try:
        closing = lines.index("---", 1)
    except ValueError:
        fail(f"{path} must close YAML frontmatter")
    fields: dict[str, str] = {}
    for line_number, line in enumerate(lines[1:closing], start=2):
        match = re.fullmatch(r"([a-z][a-z0-9_-]*):\s*(.+)", line)
        if not match:
            fail(f"{path}:{line_number}: unsupported frontmatter syntax")
        key, value = match.groups()
        if key in fields:
            fail(f"{path}:{line_number}: duplicate frontmatter field {key}")
        fields[key] = value.strip().strip('"').strip("'")
    return fields, lines[closing + 1 :]


def validate_relative_path(
    root: Path,
    value: str,
    context: str,
    *,
    must_exist: bool,
) -> Path:
    """Validate and resolve one confined relative path."""
    path = Path(value)
    if not value or path.is_absolute() or "\\" in value:
        fail(f"{context} must be a non-empty forward-slash relative path")
    if ".." in path.parts:
        fail(f"{context} escapes its root: {value}")
    resolved = (root / path).resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError:
        fail(f"{context} escapes its root: {value}")
    if must_exist and not resolved.is_file():
        fail(f"{context} does not name a file: {value}")
    return resolved


def validate_plugin_metadata() -> None:
    """Validate plugin.json and APM metadata shared by both scopes."""
    plugin_path = PACKAGE_ROOT / "plugin.json"
    apm_path = PACKAGE_ROOT / "apm.yml"
    if not plugin_path.is_file() or not apm_path.is_file():
        fail("plugin root must contain plugin.json and apm.yml")

    plugin = read_json(plugin_path)
    expected_fields = {
        "name",
        "description",
        "version",
        "author",
        "license",
        "keywords",
        "category",
        "tags",
        "skills",
    }
    if not isinstance(plugin, dict) or set(plugin) != expected_fields:
        fail("plugin.json does not match the repository metadata shape")
    if plugin.get("name") != PLUGIN_NAME:
        fail(f"plugin.json name must be {PLUGIN_NAME}")
    if plugin.get("version") != PLUGIN_VERSION:
        fail(f"plugin.json version must be {PLUGIN_VERSION}")
    if plugin.get("author") != {"name": "hcoona"}:
        fail("plugin.json author must be hcoona")
    if plugin.get("license") != (
        "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"
    ):
        fail("plugin.json license must match the repository license")
    if plugin.get("skills") != "skills/":
        fail("plugin.json skills must be skills/")
    if plugin.get("category") != "productivity":
        fail("plugin.json category must be productivity")
    if not isinstance(plugin.get("description"), str) or not plugin.get(
        "description"
    ):
        fail("plugin.json description must be non-empty")
    for field in ("keywords", "tags"):
        values = plugin.get(field)
        if (
            not isinstance(values, list)
            or not values
            or not all(isinstance(value, str) and value for value in values)
        ):
            fail(f"plugin.json {field} must be a non-empty string array")


def load_apm_includes() -> tuple[str, ...]:
    """Validate APM metadata and return its runtime include list."""
    apm_path = PACKAGE_ROOT / "apm.yml"
    apm_text = apm_path.read_text(encoding="utf-8")
    scalars, lists = parse_apm(apm_path)
    if scalars.get("name") != APM_NAME:
        fail(f"apm.yml name must be {APM_NAME}")
    if scalars.get("version") != PLUGIN_VERSION:
        fail(f"apm.yml version must be {PLUGIN_VERSION}")
    if not scalars.get("description"):
        fail("apm.yml description must be non-empty")
    if scalars.get("author") != "hcoona":
        fail("apm.yml author must be hcoona")
    if scalars.get("license") != (
        "LGPL-3.0-or-later WITH LGPL-3.0-linking-exception"
    ):
        fail("apm.yml license must match the repository license")
    if lists.get("targets") != ["copilot"]:
        fail("apm.yml must target copilot")
    required_sections = (
        "dependencies:\n  apm: []\n  mcp: []",
        "devDependencies:\n  apm: []",
        "scripts: {}",
    )
    if not all(section in apm_text for section in required_sections):
        fail("apm.yml dependency and scripts sections are not canonical")

    includes = tuple(lists.get("includes", []))
    if not includes:
        fail("apm.yml includes must not be empty")
    if len(includes) != len(set(includes)):
        fail("apm.yml includes contains duplicates")
    if includes != tuple(sorted(includes)):
        fail("apm.yml includes must be sorted")
    for value in includes:
        validate_relative_path(
            PACKAGE_ROOT,
            value,
            "apm.yml include",
            must_exist=True,
        )
        parts = Path(value).parts
        if len(parts) < 3 or parts[0] != "skills":
            fail(f"runtime include must be beneath skills/: {value}")
        if parts[1] not in EXPECTED_SKILLS:
            fail(f"runtime include has an unknown skill: {value}")
        relative_parts = parts[2:]
        if any(
            part.casefold() in CANONICAL_ONLY_DIRECTORIES
            for part in relative_parts
        ):
            fail(f"runtime include cannot contain tests or evals: {value}")
        if relative_parts != ("SKILL.md",) and (
            relative_parts[0] not in RUNTIME_DIRECTORIES
        ):
            fail(f"runtime include has an unsupported category: {value}")
    return includes


def discover_runtime_source_files() -> set[str]:
    """Derive runtime source inventory from canonical skill layout."""
    runtime_files: set[str] = set()
    for skill_name in EXPECTED_SKILLS:
        skill_root = SKILLS_ROOT / skill_name
        skill_file = skill_root / "SKILL.md"
        if not skill_file.is_file():
            fail(f"{skill_root} is missing SKILL.md")
        runtime_files.add(skill_file.relative_to(PACKAGE_ROOT).as_posix())
        for directory_name in sorted(RUNTIME_DIRECTORIES):
            directory = skill_root / directory_name
            if not directory.is_dir():
                fail(
                    f"{skill_root} is missing runtime directory {directory_name}"
                )
            for path in sorted(directory.rglob("*")):
                if path.is_file():
                    runtime_files.add(path.relative_to(PACKAGE_ROOT).as_posix())
    return runtime_files


def validate_canonical_layout(includes: tuple[str, ...]) -> set[str]:
    """Validate canonical source, including source-only tests and evals."""
    if not SKILLS_ROOT.is_dir():
        fail("plugin root is missing skills/")
    actual_skills = tuple(
        sorted(path.name for path in SKILLS_ROOT.iterdir() if path.is_dir())
    )
    if actual_skills != EXPECTED_SKILLS:
        fail("skills/ must contain exactly: " + ", ".join(EXPECTED_SKILLS))

    runtime_files = discover_runtime_source_files()
    include_set = set(includes)
    missing = sorted(include_set - runtime_files)
    unlisted = sorted(runtime_files - include_set)
    if missing:
        fail(
            "apm.yml lists non-runtime or missing files: " + ", ".join(missing)
        )
    if unlisted:
        fail("apm.yml omits runtime files: " + ", ".join(unlisted))

    for skill_name in EXPECTED_SKILLS:
        skill_root = SKILLS_ROOT / skill_name
        allowed_entries = RUNTIME_DIRECTORIES | {"SKILL.md"}
        unexpected_entries = sorted(
            path.name
            for path in skill_root.iterdir()
            if path.name not in allowed_entries
        )
        if unexpected_entries:
            fail(
                f"{skill_root} has unsupported top-level entries: "
                + ", ".join(unexpected_entries)
            )
        eval_path = EVALS_ROOT / f"{skill_name}.json"
        if not eval_path.is_file():
            fail(f"{skill_root} must retain canonical eval metadata")
        test_path = TESTS_ROOT / TEST_FILES[skill_name]
        if not test_path.is_file():
            fail(f"{skill_root} must retain its canonical test module")
    validator_test_path = TESTS_ROOT / VALIDATOR_TEST_FILE
    if not validator_test_path.is_file():
        fail("package must retain its validator test module")

    for path in sorted(PACKAGE_ROOT.rglob("*")):
        relative = path.relative_to(PACKAGE_ROOT).as_posix()
        if path.is_symlink():
            fail(f"canonical package must not contain symlinks: {relative}")
        if path.is_dir():
            if path.name == "__pycache__":
                fail(f"generated Python cache is present: {relative}")
            continue
        if path.suffix == ".pyc":
            fail(f"generated Python bytecode is present: {relative}")
        if path.stat().st_size > MAX_FILE_BYTES:
            fail(f"package file exceeds {MAX_FILE_BYTES} bytes: {relative}")
        if path.suffix.lower() not in ALLOWED_SUFFIXES:
            fail(f"unsupported package file type: {relative}")
    return runtime_files


def validate_skill(skill_root: Path, includes: set[str]) -> None:
    """Validate one canonical Agent Skill and its source-only evals."""
    skill_name = skill_root.name
    skill_path = skill_root / "SKILL.md"
    fields, body_lines = parse_frontmatter(skill_path)
    if set(fields) != {"name", "description"}:
        fail(f"{skill_path} frontmatter must contain only name and description")
    name = fields["name"]
    description = fields["description"]
    if name != skill_name:
        fail(f"{skill_path} name must match its parent directory")
    if len(name) > 64 or not SKILL_NAME_PATTERN.fullmatch(name):
        fail(f"{skill_path} has an invalid Agent Skills name")
    if "--" in name:
        fail(f"{skill_path} name cannot contain consecutive hyphens")
    if not 1 <= len(description) <= 1024:
        fail(f"{skill_path} description must be 1-1024 characters")
    if "Use " not in description:
        fail(f"{skill_path} description must state when to use the skill")
    if "Do not use" not in description and "does not" not in description:
        fail(f"{skill_path} description must state a negative boundary")
    if len(body_lines) > MAX_SKILL_LINES:
        fail(
            f"{skill_path} has {len(body_lines)} body lines; "
            f"maximum is {MAX_SKILL_LINES}"
        )

    content = skill_path.read_text(encoding="utf-8")
    for match in MARKDOWN_LINK_PATTERN.finditer(content):
        target = match.group(1).split("#", 1)[0]
        if not target or re.match(r"^[A-Za-z][A-Za-z0-9+.-]*:", target):
            continue
        candidate = validate_relative_path(
            skill_root,
            target,
            f"{skill_path} link",
            must_exist=True,
        )
        package_relative = candidate.relative_to(PACKAGE_ROOT).as_posix()
        if package_relative not in includes:
            fail(f"{skill_path} links to a non-runtime resource: {target}")

    for match in RESOURCE_PATH_PATTERN.finditer(content):
        target = match.group(1).rstrip(".,;:)")
        candidate = validate_relative_path(
            skill_root,
            target,
            f"{skill_path} resource",
            must_exist=True,
        )
        package_relative = candidate.relative_to(PACKAGE_ROOT).as_posix()
        if package_relative not in includes:
            fail(f"{skill_path} references a non-runtime resource: {target}")

    validate_evals(
        EVALS_ROOT / f"{skill_name}.json",
        skill_name,
        PLUGIN_VERSION,
        PACKAGE_ROOT,
    )
    for script in sorted((skill_root / "scripts").glob("*.py")):
        validate_pep723(script)
    validate_pep723(TESTS_ROOT / TEST_FILES[skill_name])


def validate_pep723(path: Path) -> None:
    """Require inline dependency metadata for executable skill scripts."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0] != "# /// script":
        fail(f"{path} must start with PEP 723 script metadata")
    try:
        end_index = lines.index("# ///", 1, 20)
    except ValueError:
        fail(f"{path} must close PEP 723 metadata near the top")
    metadata_lines: list[str] = []
    for line_number, line in enumerate(lines[1:end_index], start=2):
        if not line.startswith("#"):
            fail(
                f"{path}:{line_number} PEP 723 metadata line must be a comment"
            )
        metadata_lines.append(line[2:] if line.startswith("# ") else line[1:])
    try:
        metadata = tomllib.loads("\n".join(metadata_lines))
    except tomllib.TOMLDecodeError as error:
        fail(f"{path} has invalid PEP 723 TOML: {error}")

    requires_python = metadata.get("requires-python")
    if not isinstance(requires_python, str) or not requires_python:
        fail(f"{path} PEP 723 requires-python must be a non-empty string")
    if not EXACT_PYTHON_PIN_PATTERN.fullmatch(requires_python):
        fail(f"{path} PEP 723 requires-python must use an exact ==X.Y.Z pin")

    dependencies = metadata.get("dependencies")
    if not isinstance(dependencies, list):
        fail(f"{path} PEP 723 dependencies must be a list")
    for index, dependency in enumerate(dependencies):
        if not isinstance(dependency, str) or not dependency:
            fail(
                f"{path} PEP 723 dependencies[{index}] "
                "must be a non-empty string"
            )
        if not EXACT_DEPENDENCY_PIN_PATTERN.fullmatch(dependency):
            fail(
                f"{path} PEP 723 dependency must use an exact "
                f"name==version pin: {dependency!r}"
            )


def validate_eval_path(
    eval_root: Path,
    value: Any,
    context: str,
    *,
    must_exist: bool,
) -> None:
    """Validate one eval-relative source or expected path."""
    if not isinstance(value, str):
        fail(f"{context} must be a non-empty relative path")
    validate_relative_path(
        eval_root,
        value,
        context,
        must_exist=must_exist,
    )


def validate_evals(
    path: Path,
    skill_name: str,
    version: str,
    eval_root: Path,
) -> None:
    """Validate canonical eval metadata without deploying it."""
    data = read_json(path)
    if not isinstance(data, dict):
        fail(f"{path} must contain a JSON object")
    if data.get("skill_name") != skill_name:
        fail(f"{path} skill_name must be {skill_name}")
    if data.get("version") != version:
        fail(f"{path} version must match plugin version {version}")
    evals = data.get("evals")
    if not isinstance(evals, list) or len(evals) < 3:
        fail(f"{path} must contain at least three eval cases")
    identifiers: set[str] = set()
    has_with_skill_case = False
    for index, case in enumerate(evals, start=1):
        context = f"{path} eval {index}"
        if not isinstance(case, dict):
            fail(f"{context} must be an object")
        identifier = case.get("id")
        if not isinstance(identifier, str) or not SKILL_NAME_PATTERN.fullmatch(
            identifier
        ):
            fail(f"{context} has an invalid id")
        if identifier in identifiers:
            fail(f"{context} duplicates eval id {identifier}")
        identifiers.add(identifier)
        prompt = case.get("prompt")
        baseline = case.get("baseline_prompt")
        if not isinstance(prompt, str) or not prompt.strip():
            fail(f"{context} requires a prompt")
        if not isinstance(baseline, str) or not baseline.strip():
            fail(f"{context} requires a baseline_prompt")
        if skill_name in baseline or f"/{skill_name}" in baseline:
            fail(f"{context} leaks the skill name into baseline_prompt")
        if f"/{skill_name}" in prompt:
            has_with_skill_case = True
        assertions = case.get("assertions")
        if (
            not isinstance(assertions, list)
            or not assertions
            or not all(
                isinstance(assertion, str) and assertion.strip()
                for assertion in assertions
            )
        ):
            fail(f"{context} requires observable assertions")
        for value in case.get("files", []):
            validate_eval_path(
                eval_root,
                value,
                f"{context} input file",
                must_exist=True,
            )
        for value in case.get("expected_files", []):
            validate_eval_path(
                eval_root,
                value,
                f"{context} expected file",
                must_exist=False,
            )
    if not has_with_skill_case:
        fail(f"{path} requires at least one explicit with-skill eval case")


def walk_json(value: Any) -> Iterator[Any]:
    """Yield every JSON value in depth-first order."""
    yield value
    if isinstance(value, dict):
        for child in value.values():
            yield from walk_json(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk_json(child)


def resolve_json_pointer(document: Any, pointer: str) -> Any:
    """Resolve one local JSON pointer."""
    current = document
    for raw_part in pointer.removeprefix("#/").split("/"):
        part = raw_part.replace("~1", "/").replace("~0", "~")
        if isinstance(current, dict) and part in current:
            current = current[part]
            continue
        fail(f"unresolved local JSON Schema reference: {pointer}")
    return current


def validate_schema(path: Path, data: Any) -> None:
    """Validate one JSON Schema against Draft 2020-12 and package policy."""
    if not isinstance(data, dict):
        fail(f"{path} schema must be an object")
    if data.get("$schema") != "https://json-schema.org/draft/2020-12/schema":
        fail(f"{path} must declare JSON Schema draft 2020-12")
    try:
        Draft202012Validator.check_schema(data)
    except SchemaError as error:
        fail(f"{path} is not a valid Draft 2020-12 schema: {error.message}")
    if not isinstance(data.get("$id"), str) or not data.get("$id"):
        fail(f"{path} must declare $id")
    if not isinstance(data.get("title"), str) or not data.get("title"):
        fail(f"{path} must declare title")
    schema_version = data.get("properties", {}).get("schema_version")
    if schema_version != {"const": "1.0"}:
        fail(f"{path} must keep schema_version const 1.0")
    for value in walk_json(data):
        if not isinstance(value, dict):
            continue
        reference = value.get("$ref")
        if isinstance(reference, str) and reference.startswith("#/"):
            resolve_json_pointer(data, reference)
        pattern = value.get("pattern")
        if isinstance(pattern, str):
            try:
                re.compile(pattern)
            except re.error as error:
                fail(f"{path} has invalid regex {pattern!r}: {error}")


def validate_json_and_python() -> None:
    """Parse JSON, inspect schemas, and compile every canonical Python file."""
    schemas_by_id: dict[str, tuple[Path, bytes]] = {}
    for path in sorted(PACKAGE_ROOT.rglob("*.json")):
        data = read_json(path)
        if path.name.endswith(".schema.json"):
            validate_schema(path, data)
            schema_id = data["$id"]
            current_bytes = path.read_bytes()
            previous = schemas_by_id.get(schema_id)
            if previous is not None and previous[1] != current_bytes:
                fail(
                    f"schemas sharing $id are not byte-identical: "
                    f"{previous[0]} and {path}"
                )
            schemas_by_id[schema_id] = (path, current_bytes)

    for path in sorted(PACKAGE_ROOT.rglob("*.py")):
        try:
            source = path.read_text(encoding="utf-8")
            compile(source, str(path), "exec")
        except (OSError, UnicodeError, SyntaxError) as error:
            fail(f"Python syntax validation failed for {path}: {error}")


def validate_shared_files() -> None:
    """Require exact canonical copies for intentionally shared contracts."""
    for left_value, right_value in SHARED_FILE_PAIRS:
        left = PACKAGE_ROOT / left_value
        right = PACKAGE_ROOT / right_value
        if not left.is_file() or not right.is_file():
            fail(
                f"required shared pair is missing: {left_value}, {right_value}"
            )
        if left.read_bytes() != right.read_bytes():
            fail(
                f"required shared files are not byte-identical: "
                f"{left_value}, {right_value}"
            )


def validate_publication_profile() -> None:
    """Validate the compact shared untrusted-content profile."""
    profile_path = (
        SKILLS_ROOT
        / "scholarly-print-assembly"
        / "assets"
        / "publication-profile.json"
    )
    profile = read_json(profile_path)
    if not isinstance(profile, dict) or set(profile) != {
        "schema_version",
        "profile_id",
        "closed",
        "fragment_html",
        "untrusted_stylesheet",
        "global_prohibitions",
    }:
        fail("publication profile must have the closed top-level shape")
    if profile.get("schema_version") != "1.0":
        fail("publication profile schema_version must be 1.0")
    if profile.get("profile_id") != ("scholarly-fragment-and-stylesheet-v1"):
        fail("publication profile has an unexpected profile_id")
    if profile.get("closed") is not True:
        fail("publication profile must declare closed: true")

    html = profile.get("fragment_html")
    if not isinstance(html, dict) or set(html) != {
        "elements",
        "global_attributes",
    }:
        fail("publication profile fragment_html shape is not closed")
    elements = html.get("elements")
    global_attributes = html.get("global_attributes")
    if (
        not isinstance(elements, dict)
        or not isinstance(global_attributes, list)
        or not all(isinstance(name, str) for name in global_attributes)
        or not set(global_attributes) <= FIXED_GLOBAL_ATTRIBUTES
        or len(global_attributes) != len(set(global_attributes))
    ):
        fail("publication profile HTML allowlists are malformed")
    for tag, attributes in elements.items():
        ceiling = FIXED_ELEMENT_ATTRIBUTES.get(tag)
        if (
            not isinstance(tag, str)
            or tag != tag.casefold()
            or ceiling is None
            or not isinstance(attributes, list)
            or not all(isinstance(name, str) for name in attributes)
            or not set(attributes) <= ceiling
            or len(attributes) != len(set(attributes))
        ):
            fail("publication profile element allowlist is malformed")

    stylesheet = profile.get("untrusted_stylesheet")
    if not isinstance(stylesheet, dict) or set(stylesheet) != {
        "properties",
        "at_rules",
        "selector_surface",
    }:
        fail("publication profile stylesheet shape is not closed")
    if stylesheet.get("at_rules") != []:
        fail("untrusted stylesheet profile must not allow at-rules")
    properties = stylesheet.get("properties")
    selector_surface = stylesheet.get("selector_surface")
    if (
        not isinstance(properties, list)
        or not all(
            isinstance(property_name, str)
            and property_name == property_name.casefold()
            and not property_name.startswith("--")
            and property_name in FIXED_CSS_PROPERTIES
            for property_name in properties
        )
        or len(properties) != len(set(properties))
    ):
        fail("untrusted stylesheet properties must be unique lowercase names")
    if (
        not isinstance(selector_surface, list)
        or not all(isinstance(name, str) for name in selector_surface)
        or not set(selector_surface) <= PROFILE_SELECTOR_SURFACE
        or len(selector_surface) != len(set(selector_surface))
    ):
        fail("publication profile selector surface is not supported")
    prohibitions = profile.get("global_prohibitions")
    if (
        not isinstance(prohibitions, list)
        or not all(isinstance(name, str) for name in prohibitions)
        or set(prohibitions) != PROFILE_PROHIBITIONS
        or len(prohibitions) != len(set(prohibitions))
    ):
        fail("publication profile global prohibitions are not supported")


def find_repository_root() -> Path:
    """Locate the hosting repository for deployed parity checks."""
    for candidate in PACKAGE_ROOT.parents:
        if (candidate / ".git").exists() and (candidate / "apm.yml").is_file():
            return candidate
    fail("cannot locate repository root for runtime deployment validation")


def deployment_path(repository_root: Path, include: str) -> Path:
    """Map one package include to its generated .agents path."""
    return repository_root / ".agents" / Path(include)


def validate_runtime_deployment(includes: tuple[str, ...]) -> set[str]:
    """Validate generated .agents content and byte parity."""
    repository_root = find_repository_root()
    include_set = set(includes)
    expected_deployed = {
        deployment_path(repository_root, value)
        .relative_to(repository_root)
        .as_posix()
        for value in include_set
    }
    actual_deployed: set[str] = set()

    for skill_name in EXPECTED_SKILLS:
        skill_root = repository_root / ".agents" / "skills" / skill_name
        if not skill_root.is_dir():
            fail(f"runtime deployment is missing {skill_root}")
        for forbidden_name in sorted(CANONICAL_ONLY_DIRECTORIES):
            forbidden = skill_root / forbidden_name
            if forbidden.exists():
                fail(f"runtime deployment must not mirror {forbidden}")
        for path in sorted(skill_root.rglob("*")):
            relative = path.relative_to(repository_root).as_posix()
            if path.is_symlink():
                fail(
                    f"runtime deployment must not contain symlinks: {relative}"
                )
            if path.is_file():
                actual_deployed.add(relative)

    missing = sorted(expected_deployed - actual_deployed)
    extra = sorted(actual_deployed - expected_deployed)
    if missing:
        fail(
            "runtime deployment is missing included files: "
            + ", ".join(missing)
        )
    if extra:
        fail("runtime deployment has non-included files: " + ", ".join(extra))

    for include in includes:
        canonical = PACKAGE_ROOT / include
        deployed = deployment_path(repository_root, include)
        if canonical.read_bytes() != deployed.read_bytes():
            fail(f"runtime deployment differs from canonical source: {include}")

    lock_path = repository_root / "apm.lock.yaml"
    if lock_path.is_file():
        lock_text = lock_path.read_text(encoding="utf-8")
        for skill_name in EXPECTED_SKILLS:
            for forbidden_name in CANONICAL_ONLY_DIRECTORIES:
                forbidden = f".agents/skills/{skill_name}/{forbidden_name}/"
                if forbidden in lock_text:
                    fail(f"apm.lock.yaml retains non-runtime path {forbidden}")
        absent_from_lock = sorted(
            deployed
            for deployed in expected_deployed
            if deployed not in lock_text
        )
        if absent_from_lock:
            fail(
                "apm.lock.yaml omits deployed runtime files: "
                + ", ".join(absent_from_lock)
            )
        for deployed in sorted(expected_deployed):
            deployed_path = repository_root / deployed
            digest = hashlib.sha256(deployed_path.read_bytes()).hexdigest()
            binding = f"    {deployed}: sha256:{digest}"
            if binding not in lock_text:
                fail(
                    "apm.lock.yaml has no exact deployed hash binding for "
                    f"{deployed}"
                )
    return actual_deployed


def parse_args() -> argparse.Namespace:
    """Parse validation scope."""
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--scope",
        choices=("canonical", "runtime", "all"),
        default="all",
        help="validate canonical source, generated runtime deployment, or both",
    )
    return parser.parse_args()


def main() -> int:
    """Run the selected validation scopes."""
    args = parse_args()
    validate_plugin_metadata()
    includes = load_apm_includes()
    runtime_source_files: set[str] = set()
    deployed_files: set[str] = set()

    if args.scope in {"canonical", "all"}:
        runtime_source_files = validate_canonical_layout(includes)
        include_set = set(includes)
        for skill_name in EXPECTED_SKILLS:
            validate_skill(SKILLS_ROOT / skill_name, include_set)
        validate_pep723(TESTS_ROOT / VALIDATOR_TEST_FILE)
        validate_json_and_python()
        validate_shared_files()
        validate_publication_profile()

    if args.scope in {"runtime", "all"}:
        deployed_files = validate_runtime_deployment(includes)

    print(
        json.dumps(
            {
                "canonical_runtime_files": len(runtime_source_files),
                "deployed_runtime_files": len(deployed_files),
                "plugin": PLUGIN_NAME,
                "runtime_includes": len(includes),
                "scope": args.scope,
                "skills": list(EXPECTED_SKILLS),
                "status": "pass",
                "version": PLUGIN_VERSION,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ValidationError as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1) from error
