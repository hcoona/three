# /// script
# requires-python = ">=3.13,<3.14"
# dependencies = [
#     "PyYAML==6.0.3",
#     "check-jsonschema==0.38.0",
#     "markdown-it-py==4.2.0",
# ]
# ///

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path, PurePosixPath
from typing import Any

import yaml
from markdown_it import MarkdownIt
from markdown_it.token import Token


ROOT = Path(__file__).resolve().parents[1]
ROOT_RESOLVED = ROOT.resolve()
MARKDOWN = MarkdownIt("commonmark", {"html": True})
REQUIREMENT_ID_PATTERN = re.compile(r"^V2-REQ-[0-9]{3}[A-Z]?$")
REQUIREMENT_PREFIX_PATTERN = re.compile(r"^V2-REQ-")
RETIRED_REQUIREMENT_PREFIX = "Retired - current authority: "
DECISION_PATTERN = re.compile(r"^([0-9]{4})-.*\.md$")
GLOB_MARKERS = frozenset("*?[")
ZERO_SHA = "0" * 40
ROOT_RECORD_PATHS = frozenset(
    {
        ".github/pull_request_template.md",
        "AGENTS.md",
        "CONTRIBUTING.md",
        "README.md",
        "SECURITY.md",
        "UPSTREAM.md",
    }
)
RECORD_ROOT_SUFFIXES = {
    "docs": (".md", ".yaml", ".yml", ".json", ".jsonl", ".csv", ".tm7"),
    "schemas": (".schema.json",),
    "contracts": (".schema.json",),
}
BOOTSTRAP_SCHEMA_BINDINGS = (
    (
        "schemas/governance/record-families.schema.json",
        "docs/governance/record-families.yaml",
    ),
    (
        "schemas/governance/controls.schema.json",
        "docs/governance/controls.yaml",
    ),
)
HK_EXECUTION_PLANS = {
    "local-fast": ("run", "pre-commit"),
    "ci": ("check",),
}


def load_yaml_path(path: Path) -> dict[str, Any]:
    with path.open(encoding="utf-8") as stream:
        value = yaml.safe_load(stream)
    if not isinstance(value, dict):
        raise ValueError(f"{path.relative_to(ROOT).as_posix()} must contain a YAML object")
    return value


def load_yaml(relative_path: str) -> dict[str, Any]:
    return load_yaml_path(ROOT / relative_path)


def duplicates(values: list[str]) -> list[str]:
    return sorted(value for value, count in Counter(values).items() if count > 1)


def git_output(*arguments: str) -> str | None:
    result = subprocess.run(
        ["git", *arguments],
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        return None
    return result.stdout.strip()


def token_plain_text(token: Token) -> str:
    if token.type in {"text", "code_inline", "image"}:
        return token.content
    if token.type in {"softbreak", "hardbreak"}:
        return " "
    return "".join(token_plain_text(child) for child in token.children or [])


def markdown_headings(text: str) -> list[str]:
    tokens = MARKDOWN.parse(text)
    headings: list[str] = []
    for index, token in enumerate(tokens):
        if (
            token.type == "heading_open"
            and index + 1 < len(tokens)
            and tokens[index + 1].type == "inline"
        ):
            headings.append(token_plain_text(tokens[index + 1]).strip())
    return headings


def walk_tokens(tokens: list[Token]) -> list[Token]:
    walked: list[Token] = []
    for token in tokens:
        walked.append(token)
        if token.children:
            walked.extend(walk_tokens(token.children))
    return walked


def requirement_headings(
    text: str,
) -> tuple[list[tuple[str, str | None]], list[str]]:
    requirements: list[tuple[str, str | None]] = []
    malformed: list[str] = []
    for heading_text in markdown_headings(text):
        if not REQUIREMENT_PREFIX_PATTERN.match(heading_text):
            continue
        identifier, separator, title = heading_text.partition(":")
        title = title.strip()
        if (
            not separator
            or REQUIREMENT_ID_PATTERN.fullmatch(identifier) is None
            or not title
        ):
            malformed.append(heading_text)
            continue
        if title.startswith("Retired"):
            if not title.startswith(RETIRED_REQUIREMENT_PREFIX):
                malformed.append(heading_text)
                continue
            authority = title.removeprefix(RETIRED_REQUIREMENT_PREFIX).strip()
            if not authority:
                malformed.append(heading_text)
                continue
            requirements.append((identifier, authority))
        else:
            requirements.append((identifier, None))
    return requirements, malformed


def markdown_link_destinations(text: str) -> list[str]:
    return [
        destination
        for token in walk_tokens(MARKDOWN.parse(text))
        if token.type == "link_open"
        and (destination := token.attrGet("href")) is not None
    ]


def resolve_accepted_base(errors: list[str]) -> str | None:
    explicit_base = os.environ.get("HK_BASE_REF")
    if explicit_base and explicit_base != ZERO_SHA:
        resolved_base = git_output(
            "rev-parse", "--verify", f"{explicit_base}^{{commit}}"
        )
        if resolved_base is None:
            errors.append(
                f"cannot resolve HK_BASE_REF for requirement-ID comparison: {explicit_base}"
            )
            return None
        return resolved_base

    for candidate in ("origin/main-v2", "main-v2"):
        resolved_base = git_output("rev-parse", "--verify", f"{candidate}^{{commit}}")
        if resolved_base is not None:
            return resolved_base

    errors.append(
        "cannot resolve the accepted target branch for requirement-ID comparison; "
        "fetch main-v2 or set HK_BASE_REF"
    )
    return None


def requirement_ids_at_commit(commit: str, errors: list[str]) -> set[str]:
    listing = git_output("ls-tree", "-r", "--name-only", commit, "--", "docs")
    if listing is None:
        errors.append(f"cannot list accepted documentation at {commit}")
        return set()

    requirement_ids: set[str] = set()
    for relative_path in listing.splitlines():
        if not relative_path.endswith(".md"):
            continue
        content = git_output("show", f"{commit}:{relative_path}")
        if content is None:
            errors.append(
                f"cannot read accepted requirement definitions from {relative_path}"
            )
            continue
        requirements, malformed = requirement_headings(content)
        requirement_ids.update(identifier for identifier, _authority in requirements)
        if malformed:
            errors.append(
                f"accepted record {relative_path} contains malformed requirement "
                f"headings: {', '.join(malformed)}"
            )
    return requirement_ids


def check_identifiers() -> list[str]:
    errors: list[str] = []
    requirement_occurrences: list[tuple[str, str, str | None]] = []
    for path in sorted((ROOT / "docs").rglob("*.md")):
        relative_path = path.relative_to(ROOT).as_posix()
        requirements, malformed = requirement_headings(path.read_text(encoding="utf-8"))
        for heading in malformed:
            errors.append(
                f"malformed requirement heading in {relative_path}: {heading}"
            )
        requirement_occurrences.extend(
            (identifier, relative_path, authority)
            for identifier, authority in requirements
        )

    requirement_ids = [
        identifier for identifier, _path, _authority in requirement_occurrences
    ]
    requirement_id_set = set(requirement_ids)
    for identifier in duplicates(requirement_ids):
        errors.append(f"duplicate requirement identifier: {identifier}")
    for identifier, relative_path, authority in requirement_occurrences:
        if not relative_path.startswith("docs/product/requirements/"):
            errors.append(
                f"requirement identifier {identifier} is defined outside the canonical "
                f"requirements family: {relative_path}"
            )
        if authority is None:
            continue
        if REQUIREMENT_ID_PATTERN.fullmatch(authority):
            if authority == identifier:
                errors.append(
                    f"retired requirement {identifier} cannot name itself as current "
                    "authority"
                )
            elif authority not in requirement_id_set:
                errors.append(
                    f"retired requirement {identifier} names unknown current authority "
                    f"{authority}"
                )
            continue
        path_text, separator, anchor = authority.partition("#")
        authority_path = resolve_repository_file(
            path_text,
            f"retired requirement {identifier} current authority",
            errors,
        )
        if authority_path is None:
            continue
        if authority_path.suffix.lower() != ".md":
            errors.append(
                f"retired requirement {identifier} current authority is not Markdown: "
                f"{authority}"
            )
        elif separator and anchor not in markdown_heading_slugs(authority_path):
            errors.append(
                f"retired requirement {identifier} current-authority anchor does not "
                f"exist or is not an ASCII GitHub heading anchor: {authority}"
            )

    accepted_base = resolve_accepted_base(errors)
    if accepted_base is not None:
        accepted_ids = requirement_ids_at_commit(accepted_base, errors)
        for identifier in sorted(accepted_ids - set(requirement_ids)):
            errors.append(f"established requirement identifier was removed: {identifier}")

    decision_numbers = [
        match.group(1)
        for path in sorted((ROOT / "docs/decisions").glob("*.md"))
        if (match := DECISION_PATTERN.fullmatch(path.name))
    ]
    for number in duplicates(decision_numbers):
        errors.append(f"duplicate decision number: {number}")

    registry_ids = {
        "record family": [
            item["id"]
            for item in load_yaml("docs/governance/record-families.yaml")["families"]
        ],
        "control": [
            item["id"]
            for item in load_yaml("docs/governance/controls.yaml")["controls"]
        ],
        "operational identity": [
            item["id"]
            for item in load_yaml("docs/governance/operational-identities.yaml")[
                "selected_v2"
            ]
        ],
        "recheck": [
            item["id"]
            for item in load_yaml("docs/research/rechecks.yaml")["rechecks"]
        ],
    }
    for label, values in registry_ids.items():
        for identifier in duplicates(values):
            errors.append(f"duplicate {label} identifier: {identifier}")
    return errors


def contains_glob(path: str) -> bool:
    return any(marker in path for marker in GLOB_MARKERS)


def matches(path: str, pattern: str) -> bool:
    if not contains_glob(pattern):
        return path == pattern
    return PurePosixPath(path).match(pattern)


def repository_path_error(value: str, allow_glob: bool = False) -> str | None:
    if "\\" in value:
        return "must use repository-relative POSIX separators"
    pure_path = PurePosixPath(value)
    if pure_path.is_absolute():
        return "must be repository-relative"
    if ".." in pure_path.parts:
        return "must not contain parent traversal"
    if not allow_glob and contains_glob(value):
        return "must identify one repository path"
    return None


def resolve_repository_file(
    value: str,
    label: str,
    errors: list[str],
) -> Path | None:
    if reason := repository_path_error(value):
        errors.append(f"{label} {reason}: {value}")
        return None
    lexical_path = ROOT / PurePosixPath(value)
    path = lexical_path.resolve()
    try:
        path.relative_to(ROOT_RESOLVED)
    except ValueError:
        errors.append(f"{label} resolves outside the repository: {value}")
        return None
    if not path.is_file():
        errors.append(f"{label} does not exist: {value}")
        return None
    if path != ROOT_RESOLVED / PurePosixPath(value):
        errors.append(f"{label} must not use a symbolic-link path: {value}")
        return None
    return path


def resolve_governed_record(
    candidate: Path,
    label: str,
    errors: list[str],
) -> Path | None:
    try:
        relative_path = candidate.relative_to(ROOT)
    except ValueError:
        errors.append(f"{label} is outside the repository: {candidate}")
        return None
    resolved = candidate.resolve()
    try:
        resolved.relative_to(ROOT_RESOLVED)
    except ValueError:
        errors.append(f"{label} resolves outside the repository: {relative_path}")
        return None
    if resolved != ROOT_RESOLVED / relative_path:
        errors.append(f"{label} must not use a symbolic-link path: {relative_path}")
        return None
    if not resolved.is_file():
        return None
    return resolved


def expand_family_paths(
    family: dict[str, Any],
    errors: list[str],
) -> set[Path]:
    pattern = family["path"]
    if reason := repository_path_error(pattern, allow_glob=True):
        errors.append(f"{family['id']} family path {reason}: {pattern}")
        return set()
    candidates = ROOT.glob(pattern) if contains_glob(pattern) else [ROOT / pattern]
    paths: set[Path] = set()
    for candidate in candidates:
        resolved = resolve_governed_record(
            candidate,
            f"{family['id']} family record",
            errors,
        )
        if resolved is not None:
            paths.add(resolved)
    return paths


def discover_governed_record_paths(errors: list[str]) -> set[Path]:
    candidates = {ROOT / relative_path for relative_path in ROOT_RECORD_PATHS}
    for root_name, suffixes in RECORD_ROOT_SUFFIXES.items():
        root = ROOT / root_name
        if not root.is_dir():
            continue
        candidates.update(
            path
            for path in root.rglob("*")
            if path.is_file() and any(path.name.endswith(suffix) for suffix in suffixes)
        )

    paths: set[Path] = set()
    for candidate in candidates:
        resolved = resolve_governed_record(candidate, "governed record", errors)
        if resolved is not None:
            paths.add(resolved)
    return paths


def github_ascii_slug(heading: str) -> str | None:
    if not heading.isascii():
        return None
    return "".join(
        character
        for character in heading.lower()
        if character.isalnum() or character in {" ", "-", "_"}
    ).replace(" ", "-")


def markdown_heading_slugs_from_text(text: str) -> set[str]:
    slugs: set[str] = set()
    occurrences: dict[str, int] = {}
    for heading in markdown_headings(text):
        base_slug = github_ascii_slug(heading)
        if base_slug is None:
            continue
        slug = base_slug
        count = occurrences.get(base_slug, 0)
        while slug in occurrences:
            count += 1
            slug = f"{base_slug}-{count}"
        occurrences[base_slug] = count
        occurrences[slug] = 0
        slugs.add(slug)
    return slugs


def markdown_heading_slugs(path: Path) -> set[str]:
    return markdown_heading_slugs_from_text(path.read_text(encoding="utf-8"))


def check_markdown_parser_contract() -> list[str]:
    errors: list[str] = []
    markdown = """\
<script>
## V2-REQ-998: Hidden
[Hidden](hidden.md)
</script>

<div>Rendered HTML block</div>

## V2-REQ-060: `Visible` *Heading*

`[Code](code.md)` \\[Escaped](escaped.md) [Real](real.md) [Reference][record]

[record]: reference.md
"""
    requirements, malformed = requirement_headings(markdown)
    if requirements != [("V2-REQ-060", None)] or malformed:
        errors.append("CommonMark requirement-heading parser contract failed")
    requirement_markdown = """\
## V2-REQ-061: Active requirement
## V2-REQ-062: Retired - current authority: V2-REQ-061
## V2-REQ-063:
## V2-REQ-064: Retired
"""
    requirements, malformed = requirement_headings(requirement_markdown)
    if requirements != [
        ("V2-REQ-061", None),
        ("V2-REQ-062", "V2-REQ-061"),
    ] or malformed != ["V2-REQ-063:", "V2-REQ-064: Retired"]:
        errors.append("requirement active-and-retired syntax contract failed")
    if set(markdown_link_destinations(markdown)) != {"real.md", "reference.md"}:
        errors.append("CommonMark documentation-link parser contract failed")

    slug_markdown = """\
## Use `dotnet` build
## A--B
## foo - bar
## foo
## foo-1
## foo
"""
    if markdown_heading_slugs_from_text(slug_markdown) != {
        "use-dotnet-build",
        "a--b",
        "foo---bar",
        "foo",
        "foo-1",
        "foo-2",
    }:
        errors.append("GitHub ASCII heading-anchor parser contract failed")
    return errors


def hk_steps_by_execution_point(errors: list[str]) -> dict[str, set[str]]:
    steps_by_execution_point: dict[str, set[str]] = {}
    for execution_point, arguments in HK_EXECUTION_PLANS.items():
        result = subprocess.run(
            ["hk", *arguments, "--all", "--plan", "--json"],
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            errors.append(
                f"cannot evaluate hk {execution_point} plan: "
                f"{result.stderr.strip() or result.stdout.strip()}"
            )
            continue
        try:
            plan = json.loads(result.stdout)
        except json.JSONDecodeError as error:
            errors.append(f"cannot parse hk {execution_point} plan JSON: {error}")
            continue
        expected_hook = "pre-commit" if execution_point == "local-fast" else "check"
        if plan.get("hook") != expected_hook:
            errors.append(
                f"hk {execution_point} plan reported unexpected hook: "
                f"{plan.get('hook')}"
            )
            continue
        plan_steps = plan.get("steps")
        if not isinstance(plan_steps, list):
            errors.append(f"hk {execution_point} plan does not contain a step list")
            continue
        steps_by_execution_point[execution_point] = {
            step["name"]
            for step in plan_steps
            if isinstance(step, dict)
            and isinstance(step.get("name"), str)
            and step.get("status") == "included"
        }
    return steps_by_execution_point


def check_control_references(catalog: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    controls = load_yaml("docs/governance/controls.yaml")["controls"]
    current_families = [
        family for family in catalog["families"] if family["state"] == "current"
    ]
    hk_execution_steps = hk_steps_by_execution_point(errors)
    local_only_steps: set[str] = set()
    for control in controls:
        if control["state"] != "current":
            continue
        for reference in control["governing_rules"]:
            path_text, separator, anchor = reference.partition("#")
            path = resolve_repository_file(
                path_text,
                f"control {control['id']} governing rule",
                errors,
            )
            if path is None:
                continue
            relative_path = path.relative_to(ROOT_RESOLVED).as_posix()
            if path.suffix.lower() != ".md":
                errors.append(
                    f"control {control['id']} governing rule is not Markdown: {path_text}"
                )
                continue
            if not any(
                family["format"] == "markdown"
                and matches(relative_path, family["path"])
                for family in current_families
            ):
                errors.append(
                    f"control {control['id']} governing rule is not a current governed "
                    f"Markdown record: {path_text}"
                )
            if separator and anchor not in markdown_heading_slugs(path):
                errors.append(
                    f"control {control['id']} governing-rule anchor does not exist or "
                    f"is not an ASCII GitHub heading anchor: {reference}"
                )

        implementation = control["implementation"]
        if control["runner"] == "hk":
            if implementation["kind"] != "hk-steps":
                errors.append(f"control {control['id']} must identify its hk steps")
                continue
            declared_points = set(implementation["steps"])
            execution_points = set(control["execution_points"])
            if declared_points != execution_points:
                errors.append(
                    f"control {control['id']} hk step mappings must match its execution "
                    "points"
                )
            for execution_point, declared_steps in implementation["steps"].items():
                actual_steps = hk_execution_steps.get(execution_point)
                if actual_steps is None:
                    errors.append(
                        f"control {control['id']} uses unsupported hk execution point "
                        f"{execution_point}"
                    )
                    continue
                missing_steps = sorted(set(declared_steps) - actual_steps)
                if missing_steps:
                    errors.append(
                        f"control {control['id']} is not wired at {execution_point}: "
                        f"{', '.join(missing_steps)}"
                    )
            declared_local_only = set(implementation.get("local_only", []))
            misdeclared = sorted(
                (declared_local_only - set(implementation["steps"].get("local-fast", [])))
                | (declared_local_only & set(implementation["steps"].get("ci", [])))
            )
            if misdeclared:
                errors.append(
                    f"control {control['id']} declares local-only steps that are not "
                    f"local-fast-only: {', '.join(misdeclared)}"
                )
            local_only_steps |= declared_local_only
        elif implementation["kind"] == "hk-steps":
            errors.append(f"non-hk control {control['id']} must not declare hk steps")
        elif implementation["kind"] == "repository-path":
            resolve_repository_file(
                implementation["value"],
                f"control {control['id']} implementation",
                errors,
            )
    if "local-fast" in hk_execution_steps and "ci" in hk_execution_steps:
        missing_from_ci = sorted(
            hk_execution_steps["local-fast"]
            - hk_execution_steps["ci"]
            - local_only_steps
        )
        if missing_from_ci:
            errors.append(
                "local-fast hk steps missing from the CI set without a local-only "
                f"declaration: {', '.join(missing_from_ci)}"
            )
    return errors


def check_documentation_portal(
    current_family_paths: dict[str, set[Path]],
) -> list[str]:
    portal = ROOT / "docs/README.md"
    if not portal.is_file():
        return ["documentation portal does not exist: docs/README.md"]
    linked_records: set[str] = set()
    for target in markdown_link_destinations(portal.read_text(encoding="utf-8")):
        path_part = target.split("#", maxsplit=1)[0]
        if not path_part or "://" in path_part:
            continue
        resolved = (portal.parent / path_part).resolve()
        try:
            relative_path = resolved.relative_to(ROOT_RESOLVED)
        except ValueError:
            continue
        if resolved.is_file():
            linked_records.add(relative_path.as_posix())

    retained_records = {
        path.relative_to(ROOT_RESOLVED).as_posix()
        for paths in current_family_paths.values()
        for path in paths
        if path.is_file() and path != portal
    }
    return [
        f"documentation portal does not route to retained record: {relative_path}"
        for relative_path in sorted(retained_records - linked_records)
    ]


def check_catalog_paths() -> list[str]:
    errors = check_markdown_parser_contract()
    catalog = load_yaml("docs/governance/record-families.yaml")
    current = [family for family in catalog["families"] if family["state"] == "current"]
    current_family_paths: dict[str, set[Path]] = {}
    governed_paths = discover_governed_record_paths(errors)

    for family in catalog["families"]:
        pattern = family["path"]
        family_paths = expand_family_paths(family, errors)
        if family["state"] == "current":
            current_family_paths[family["id"]] = family_paths
            if family["carrier"] == "repository-file":
                if contains_glob(pattern):
                    errors.append(
                        f"{family['id']} uses a glob for a singleton path: {pattern}"
                    )
                elif not family_paths:
                    errors.append(f"{family['id']} path does not exist: {pattern}")
            elif not family_paths:
                errors.append(
                    f"{family['id']} path matches no current files: {pattern}"
                )
            for path in sorted(family_paths - governed_paths):
                errors.append(
                    f"current family {family['id']} path is outside governed record "
                    f"roots: {path.relative_to(ROOT_RESOLVED).as_posix()}"
                )
        elif family_paths:
            errors.append(
                f"{family['id']} has a record instance but remains scheduled; "
                "activate the family in the same change"
            )

        if schema := family.get("schema"):
            resolve_repository_file(
                schema,
                f"{family['id']} schema",
                errors,
            )

    for path in sorted(governed_paths):
        relative_path = path.relative_to(ROOT_RESOLVED).as_posix()
        matching_families = [
            family["id"] for family in current if matches(relative_path, family["path"])
        ]
        if not matching_families:
            errors.append(f"governed record has no current family: {relative_path}")
        elif len(matching_families) > 1:
            errors.append(
                f"governed record matches multiple current families: {relative_path} "
                f"({', '.join(matching_families)})"
            )

    errors.extend(check_documentation_portal(current_family_paths))
    errors.extend(check_control_references(catalog))
    return errors


# Run check-jsonschema from this script's locked environment rather than a separately
# installed tool, so its version and dependencies come from the script's uv lock.
CHECK_JSONSCHEMA = (sys.executable, "-m", "check_jsonschema")


def run_schema_validation(schema: str, instances: list[Path]) -> int:
    result = subprocess.run(
        [
            *CHECK_JSONSCHEMA,
            "--schemafile",
            schema,
            *(path.relative_to(ROOT_RESOLVED).as_posix() for path in instances),
        ],
        cwd=ROOT,
        check=False,
    )
    return result.returncode


def run_metaschema_validation(instances: list[Path]) -> int:
    result = subprocess.run(
        [
            *CHECK_JSONSCHEMA,
            "--check-metaschema",
            *(path.relative_to(ROOT_RESOLVED).as_posix() for path in instances),
        ],
        cwd=ROOT,
        check=False,
    )
    return result.returncode


def validate_contract_examples(schemas: list[Path]) -> int:
    """Validate each embedded example of a public contract schema against that schema."""
    with tempfile.TemporaryDirectory(prefix="contract-examples-") as directory:
        for schema in schemas:
            try:
                examples = json.loads(schema.read_text(encoding="utf-8")).get("examples", [])
            except json.JSONDecodeError as error:
                print(f"ERROR: cannot read {schema}: {error}", file=sys.stderr)
                return 1
            if not isinstance(examples, list):
                print(f"ERROR: {schema} examples must be a list", file=sys.stderr)
                return 1
            instances: list[Path] = []
            for index, example in enumerate(examples):
                instance = Path(directory) / f"{schema.stem}-example-{index}.json"
                instance.write_text(json.dumps(example), encoding="utf-8")
                instances.append(instance)
            if not instances:
                continue
            # check-jsonschema enables format checking by default.
            result = subprocess.run(
                [
                    *CHECK_JSONSCHEMA,
                    "--schemafile",
                    schema.relative_to(ROOT_RESOLVED).as_posix(),
                    *(str(instance) for instance in instances),
                ],
                cwd=ROOT,
                check=False,
            )
            if result.returncode != 0:
                print(f"ERROR: an embedded example does not validate: {schema}", file=sys.stderr)
                return 1
    return 0


def validate_structured_records() -> int:
    errors: list[str] = []
    for schema, instance in BOOTSTRAP_SCHEMA_BINDINGS:
        schema_path = ROOT / schema
        instance_path = ROOT / instance
        if not schema_path.is_file():
            errors.append(f"bootstrap schema does not exist: {schema}")
            continue
        if not instance_path.is_file():
            errors.append(f"bootstrap structured record does not exist: {instance}")
            continue
        if run_schema_validation(schema, [instance_path]) != 0:
            return 1

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    catalog = load_yaml("docs/governance/record-families.yaml")
    bootstrap_bindings = set(BOOTSTRAP_SCHEMA_BINDINGS)
    families = [
        family
        for family in catalog["families"]
        if (
            family.get("schema")
            and (family["schema"], family["path"]) not in bootstrap_bindings
        )
        or family["format"] == "json-schema"
    ]
    for family in families:
        instances = sorted(expand_family_paths(family, errors))
        if not instances:
            if family["state"] == "current":
                errors.append(
                    f"{family['id']} has no structured record at {family['path']}"
                )
            continue

        if family["format"] == "json-schema":
            if run_metaschema_validation(instances) != 0:
                return 1
            if family["path"].startswith("contracts/") and validate_contract_examples(instances) != 0:
                return 1
        elif run_schema_validation(family["schema"], instances) != 0:
            return 1

        if family["state"] == "scheduled":
            errors.append(
                f"{family['id']} has a record instance but remains scheduled; "
                "activate the family in the same change"
            )

    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    return 1 if errors else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("identifiers")
    subparsers.add_parser("schemas")
    subparsers.add_parser("catalog-paths")
    args = parser.parse_args()

    if args.command == "schemas":
        return validate_structured_records()

    errors = check_identifiers() if args.command == "identifiers" else check_catalog_paths()
    if not errors:
        return 0
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
