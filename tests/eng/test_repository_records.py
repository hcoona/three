"""Bounded record validation against tiny real Git snapshots."""

from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

import pytest
import yaml

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "repository_record_checker",
    ROOT / "eng/scripts/check_repository_records.py",
)
assert SPEC
assert SPEC.loader
checker = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = checker
SPEC.loader.exec_module(checker)


def run_git(root: Path, *args: str) -> str:
    """Run fixture Git without hooks or inherited repository context."""
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    return subprocess.run(  # noqa: S603 - Fixed Git CLI on isolated fixtures.
        [  # noqa: S607 - Fixture Git uses the installed executable.
            "git",
            "-C",
            str(root),
            "-c",
            "core.hooksPath=/dev/null",
            "-c",
            "user.name=Record Tests",
            "-c",
            "user.email=records@example.invalid",
            *args,
        ],
        check=True,
        capture_output=True,
        text=True,
        env=env,
    ).stdout.strip()


class Repository:
    """A tiny local Git repository with an initially valid record catalog."""

    def __init__(self, root: Path) -> None:
        """Initialize a minimal valid catalog and freeze its base."""
        self.root = root
        run_git(root, "init", "-q")
        self.catalog = {
            "schema_version": 3,
            "families": [
                {
                    "id": "records",
                    "concern": "Fixture reader records",
                    "producer": "Authors",
                    "maintainer": "Owner",
                    "consumers": ["Readers"],
                    "creation_trigger": "A reader needs the record",
                    "failure_mode": "A reader cannot recover the concern",
                    "review_point": "Changed records",
                }
            ],
            "bindings": [],
        }
        for path in checker.SCHEMA_BINDINGS.values():
            self.write(path, (ROOT / path).read_text())
            self.bind(path, "json-schema")
        self.write("docs/README.md", "# Records\n\n")
        self.bind("docs/README.md")
        for path in checker.SCHEMA_BINDINGS:
            self.bind(path, "yaml", schema=checker.SCHEMA_BINDINGS[path])
        self.write(
            checker.CONTROL_CATALOG,
            yaml.safe_dump(
                {
                    "schema_version": 2,
                    "controls": [
                        {
                            "id": "record-review",
                            "state": "current",
                            "class": "procedural-review",
                            "enforcement": "review-required",
                            "producer": "Reviewer",
                            "maintainer": "Owner",
                            "consumers": ["Owner"],
                            "governing_rules": ["docs/README.md"],
                            "invariant": "Records retain readers",
                            "runner": "agent-skill",
                            "implementation": {
                                "kind": "review-procedure",
                                "value": "Read applicable records",
                            },
                            "execution_points": ["local-validation"],
                            "failure_mode": "Records become orphaned",
                        }
                    ],
                }
            ),
        )
        self.save()
        self.route_all()
        self.base = self.commit()

    def write(self, path: str, text: str) -> None:
        """Write one fixture file with parent directories."""
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def bind(
        self,
        path: str,
        format_: str = "markdown",
        namespace: str = "repository",
        **kwargs: str,
    ) -> None:
        """Admit one explicit fixture path."""
        self.catalog["bindings"].append(
            {
                "id": f"binding-{len(self.catalog['bindings'])}",
                "family": "records",
                "namespace": namespace,
                "state": "current",
                "carrier": "repository-file",
                "path": path,
                "format": format_,
                **kwargs,
            }
        )

    def save(self) -> None:
        """Persist the candidate catalog."""
        self.write(checker.FAMILY_CATALOG, yaml.safe_dump(self.catalog))

    def route_all(self) -> None:
        """Route each fixture record through the portal."""
        self.write(
            "docs/README.md",
            "# Records\n\n"
            + "\n".join(
                f"- [Record](../{b['path']})"
                for b in self.catalog["bindings"]
                if b["path"] != "docs/README.md"
            )
            + "\n",
        )

    def commit(self) -> str:
        """Create an isolated fixture commit."""
        run_git(self.root, "add", ".")
        run_git(self.root, "commit", "-qm", "Fixture snapshot")
        return run_git(self.root, "rev-parse", "HEAD")

    def check(self, candidate: str | None = None) -> dict:
        """Run the checker against the fixture base."""
        return checker.check_repository(self.root, self.base, candidate)


@pytest.fixture
def repo(tmp_path: Path) -> Repository:
    """Create a valid isolated repository."""
    return Repository(tmp_path)


def codes(report: dict) -> set[str]:
    """Extract exact public diagnostic categories."""
    return {d["code"] for d in report["diagnostics"]}


def write_legacy_catalog(repo: Repository) -> None:
    """Pair legacy family instances with their version-2 schema."""
    family = repo.catalog["families"][0]
    legacy = {
        "schema_version": 2,
        "families": [
            {**family, **{k: v for k, v in b.items() if k != "family"}}
            for b in repo.catalog["bindings"]
        ],
    }
    schema_path = checker.SCHEMA_BINDINGS[checker.FAMILY_CATALOG]
    schema = json.loads((ROOT / schema_path).read_text())
    schema["required"].remove("bindings")
    schema["properties"].pop("bindings")
    schema["properties"]["schema_version"]["const"] = 2
    binding = schema["$defs"].pop("binding")
    family_schema = schema["$defs"]["family"]
    family_schema["required"] += [
        k for k in binding["required"] if k not in {"id", "family"}
    ]
    family_schema["properties"].update(
        {
            k: v
            for k, v in binding["properties"].items()
            if k not in {"family", "purpose"}
        }
    )
    family_schema["allOf"] = binding["allOf"]
    repo.write(schema_path, json.dumps(schema))
    repo.write(checker.FAMILY_CATALOG, yaml.safe_dump(legacy))


def test_readme_admission_and_deleted_binding(repo: Repository) -> None:
    """Verify readme admission and deleted binding."""
    path = "src/private/app/new-project/README.md"
    repo.write(path, "# New project\n\nUsage.\n")
    assert "record-coverage" in codes(repo.check())
    repo.bind(path, namespace="src/private/app/new-project")
    repo.save()
    repo.route_all()
    passing = repo.check()
    assert passing["diagnostics"] == []
    assert next(e for e in passing["manifest"] if e["path"] == path)[
        "untracked"
    ]
    repo.catalog["bindings"].pop()
    repo.save()
    failed = repo.check()
    assert any(
        d["code"] == "record-coverage" and d["path"] == path
        for d in failed["diagnostics"]
    )
    assert path not in failed["deleted_paths"]


def test_move_and_companion_namespace(repo: Repository) -> None:
    """Verify move and companion namespace."""
    owner = "src/public/lib/product"
    source = owner + "/docs/requirements.md"
    companion = "src/private/app/helper/README.md"
    repo.write(
        source, "# REQ-001: Behavior\n\nThe product preserves behavior.\n"
    )
    repo.write(companion, "# Helper\n\nCompanion implementation.\n")
    repo.bind(source, namespace=owner)
    repo.bind(companion, namespace=owner)
    repo.save()
    repo.route_all()
    repo.base = repo.commit()
    target = owner + "/docs/contract.md"
    (repo.root / source).rename(repo.root / target)
    next(b for b in repo.catalog["bindings"] if b["path"] == source)["path"] = (
        target
    )
    repo.save()
    repo.route_all()
    report = repo.check()
    assert report["diagnostics"] == []
    assert source in report["deleted_paths"]
    route = next(
        x for x in report["binding_comparison"] if x["path"] == companion
    )
    assert route["base_namespaces"] == route["candidate_namespaces"] == [owner]


def test_directory_browsing_and_portal_traversal(repo: Repository) -> None:
    """Verify directory browsing and portal traversal."""
    repo.write("eng/component/source.py", "VALUE = 1\n")
    repo.write(
        "src/public/lib/project/README.md",
        "# Project\n\n[Contract](docs/contract.md#contract)\n",
    )
    repo.write(
        "src/public/lib/project/docs/contract.md",
        "# Contract\n\nStable behavior.\n",
    )
    repo.bind(
        "src/public/lib/project/README.md", namespace="src/public/lib/project"
    )
    repo.bind(
        "src/public/lib/project/docs/contract.md",
        namespace="src/public/lib/project",
    )
    repo.save()
    repo.route_all()
    portal = (repo.root / "docs/README.md").read_text()
    portal = portal.replace(
        "- [Record](../src/public/lib/project/README.md)",
        "- [Project](../src/public/lib/project/)",
    )
    portal = portal.replace(
        "- [Record](../src/public/lib/project/docs/contract.md)\n", ""
    )
    repo.write(
        "docs/README.md", portal + "\n[Source directory](../eng/component/)\n"
    )
    assert repo.check()["diagnostics"] == []
    assert not (repo.root / "eng/component/README.md").exists()


@pytest.mark.parametrize(
    ("mutation", "expected"),
    [
        ("duplicate-id", "duplicate-id"),
        ("unknown-family", "unknown-family"),
        ("unused-family", "unused-family"),
        ("duplicate-yaml-key", "schema-input-invalid"),
        ("duplicate-json-key", "schema-input-invalid"),
        ("malformed-schema", "schema-input-invalid"),
        ("missing-schema-ref", "schema-input-invalid"),
        ("remote-schema-ref", "schema-input-invalid"),
        ("wrong-field", "schema-invalid"),
        ("unsafe-path", "unsafe-binding-path"),
    ],
)
def test_invalid_catalog_diagnostics(
    repo: Repository, mutation: str, expected: str
) -> None:
    """Verify invalid catalog diagnostics."""
    if mutation == "duplicate-id":
        repo.catalog["bindings"][1]["id"] = repo.catalog["bindings"][0]["id"]
    elif mutation == "unknown-family":
        repo.catalog["bindings"][0]["family"] = "missing"
    elif mutation == "unused-family":
        repo.catalog["families"].append(
            {**repo.catalog["families"][0], "id": "unused"}
        )
    elif mutation == "wrong-field":
        repo.catalog["bindings"][0]["state"] = "whatever"
    elif mutation == "unsafe-path":
        repo.catalog["bindings"][0]["path"] = "../escaped.md"
    repo.save()
    if mutation == "duplicate-yaml-key":
        with (repo.root / checker.FAMILY_CATALOG).open("a") as stream:
            stream.write("schema_version: 3\n")
    invalid_schemas = {
        "duplicate-json-key": '{"type":"object","type":"array"}',
        "malformed-schema": '{"type":42}',
        "missing-schema-ref": '{"$ref":"#/$defs/missing"}',
        "remote-schema-ref": '{"$ref":"https://example.invalid/schema"}',
    }
    if mutation in invalid_schemas:
        repo.write(
            checker.SCHEMA_BINDINGS[checker.FAMILY_CATALOG],
            invalid_schemas[mutation],
        )
    assert expected in codes(repo.check())


@pytest.mark.parametrize(
    ("destination", "expected"),
    [
        ("missing.md", "reference-missing"),
        ("README.md#missing", "anchor-missing"),
        ("../../escape.md", "unsafe-reference"),
    ],
)
def test_missing_links_and_anchors(
    repo: Repository, destination: str, expected: str
) -> None:
    """Verify missing links and anchors."""
    with (repo.root / "docs/README.md").open("a") as stream:
        stream.write(f"\n[Wrong]({destination})\n")
    assert expected in codes(repo.check())


def test_markdown_parser_handles_reference_links_code_and_duplicate_headings(
    repo: Repository,
) -> None:
    """Verify parsed links, code exclusion and duplicate heading anchors."""
    path = "docs/reader.md"
    repo.write(
        path,
        '# Repeat\n\n# Repeat\n\n<a id="custom"></a>\n\n'
        "[Second][two]\n\n[two]: #repeat-1\n\n[Explicit](#custom)\n\n"
        "`[not a link](absent.md)`\n\n"
        "```md\n[not a link](missing.md)\n```\n",
    )
    repo.bind(path)
    repo.save()
    repo.route_all()
    assert repo.check()["diagnostics"] == []


def test_unavailable_base_is_not_replaced(repo: Repository) -> None:
    """Verify unavailable base is not replaced."""
    report = checker.check_repository(repo.root, "missing-base", None)
    assert report["base"] is None
    assert report["candidate"] is None
    assert codes(report) == {"snapshot-unavailable"}
    assert report["checks"] == {"snapshot": "failed"}


def test_identifier_retirement_and_removal(repo: Repository) -> None:
    """Verify identifier retirement and removal."""
    path = "docs/requirements.md"
    repo.write(path, "# REQ-001: Initial requirement\n\nPreserve identity.\n")
    repo.bind(path)
    repo.save()
    repo.route_all()
    repo.base = repo.commit()
    repo.write(path, "# New record\n\nRemoved without retirement.\n")
    assert "established-identifier-removed" in codes(repo.check())
    repo.write(
        path,
        "# REQ-001: Retired - current authority: docs/README.md#records\n\n"
        "Nonnormative retirement.\n",
    )
    assert repo.check()["diagnostics"] == []
    repo.write(
        path,
        "# REQ-001: Retired - current authority: docs/absent.md#missing\n"
        "\nRetired.\n",
    )
    assert "reference-missing" in codes(repo.check())


def test_commit_and_worktree_snapshots(repo: Repository) -> None:
    """Verify commit and worktree snapshots."""
    committed = repo.check(repo.base)
    assert committed["diagnostics"] == []
    assert committed["candidate"]["commit"] == repo.base
    assert committed["candidate"]["tree"] == run_git(
        repo.root, "rev-parse", "HEAD^{tree}"
    )
    repo.write("docs/untracked.md", "# Untracked\n")
    worktree = repo.check()
    assert worktree["candidate"]["kind"] == "worktree"
    assert worktree["candidate"]["tree"] is None
    assert (
        worktree["candidate"]["manifest_sha256"]
        != committed["candidate"]["manifest_sha256"]
    )
    assert "record-coverage" in codes(worktree)
    assert repo.check(repo.base)["diagnostics"] == []
    assert worktree["checks"]["hk-plan-execution"].startswith("unavailable:")


def test_hidden_untracked_and_symlink_candidates(repo: Repository) -> None:
    """Verify hidden untracked and symlink candidates."""
    path = "src/private/app/project/.AGENT/docs/hidden.md"
    repo.write(path, "# Hidden record\n")
    report = repo.check()
    assert any(
        d["path"] == path and d["code"] == "record-coverage"
        for d in report["diagnostics"]
    )
    repo.bind(path, namespace="src/private/app/project")
    repo.save()
    repo.route_all()
    assert repo.check()["diagnostics"] == []
    (repo.root / path).unlink()
    (repo.root / path).symlink_to(repo.root / "docs/README.md")
    assert "canonical-symlink" in codes(repo.check())


def test_v2_base_and_v3_candidate_boundary(repo: Repository) -> None:
    """Verify v2 base and v3 candidate boundary."""
    write_legacy_catalog(repo)
    old = (repo.root / checker.FAMILY_CATALOG).read_text()
    repo.base = repo.commit()
    repo.save()
    schema_path = checker.SCHEMA_BINDINGS[checker.FAMILY_CATALOG]
    repo.write(schema_path, (ROOT / schema_path).read_text())
    assert repo.check()["diagnostics"] == []
    repo.write(checker.FAMILY_CATALOG, old)
    assert "schema-invalid" in codes(repo.check())


def test_unknown_structured_candidates_are_unresolved(repo: Repository) -> None:
    """Verify unknown structured candidates are unresolved."""
    repo.write("src/private/app/project/unknown.json", "{}\n")
    report = repo.check()
    assert any(
        d["code"] == "classification-unresolved"
        and d["path"].endswith("unknown.json")
        for d in report["diagnostics"]
    )


def test_cli_writes_recoverable_json_and_returns_failure(
    repo: Repository, tmp_path: Path
) -> None:
    """Verify cli writes recoverable json and returns failure."""
    output = tmp_path / "report-output.txt"
    arguments = [
        "--repository-root",
        str(repo.root),
        "--base",
        repo.base,
        "--candidate",
        repo.base,
        "--output",
        str(output),
    ]
    assert checker.main(arguments) == 0
    report = json.loads(output.read_text())
    assert report["command"] == arguments
    assert report["base"]["commit"] == repo.base
    arguments[arguments.index("--base") + 1] = "absent"
    assert checker.main(arguments) == 1
    assert (
        json.loads(output.read_text())["diagnostics"][0]["code"]
        == "snapshot-unavailable"
    )


@pytest.mark.parametrize(
    ("path", "format_", "content"),
    [
        ("docs/governance/reference-license.txt", "text", "Retained license\n"),
        ("docs/evidence/observations.json", "json", "{}\n"),
    ],
)
def test_retained_nonmarkdown_record_cannot_lose_binding(
    repo: Repository,
    path: str,
    format_: str,
    content: str,
) -> None:
    """Preserve accepted text/legal and structured binding obligations."""
    repo.write(path, content)
    schema = (
        {"schema": checker.SCHEMA_BINDINGS[checker.FAMILY_CATALOG]}
        if format_ == "json"
        else {}
    )
    repo.bind(path, format_, **schema)
    repo.save()
    repo.route_all()
    repo.base = repo.commit()
    repo.catalog["bindings"].pop()
    repo.save()
    report = repo.check()
    assert any(
        d["code"] == "record-coverage" and d["path"] == path
        for d in report["diagnostics"]
    )
    assert path in report["accepted_record_obligations"]
    assert path not in report["deleted_paths"]


@pytest.mark.parametrize(
    ("path", "pattern", "expected"),
    [
        ("docs/README.md", "docs/*.md", True),
        ("docs/project/README.md", "docs/*.md", False),
        ("docs/README.md", "docs/**/*.md", True),
        ("docs/project/deep/README.md", "docs/**/*.md", True),
        ("docs-old/README.md", "docs/**/*.md", False),
    ],
)
def test_component_globs(path: str, pattern: str, expected: bool) -> None:
    """Keep single stars within components and allow zero recursive levels."""
    assert checker.matches(path, pattern) is expected


def test_duplicate_identifiers_and_missing_retirement_id(
    repo: Repository,
) -> None:
    """Reject ambiguous definitions and unresolved same-namespace retirement."""
    path = "docs/requirements.md"
    repo.write(
        path,
        "# REQ-001: Behavior\n\nRequired.\n\n"
        "# REQ-001: Duplicate\n\nRequired.\n",
    )
    repo.bind(path)
    repo.save()
    repo.route_all()
    assert "duplicate-requirement-definition" in codes(repo.check())
    repo.write(
        path, "# REQ-001: Retired - current authority: REQ-002\n\nRetired.\n"
    )
    assert "retirement-target-missing" in codes(repo.check())
    with (repo.root / path).open("a") as stream:
        stream.write("\n# REQ-002: Replacement\n\nCurrent behavior.\n")
    report = repo.check()
    assert report["diagnostics"] == []
    assert report["identifier_comparison"]["candidate_definition_count"] == 2


def test_portal_cycle_does_not_establish_reachability(repo: Repository) -> None:
    """Reject disconnected records even when their own links form a cycle."""
    repo.write("docs/a.md", "# A\n\n[B](b.md)\n")
    repo.write("docs/b.md", "# B\n\n[A](a.md)\n")
    repo.bind("docs/a.md")
    repo.bind("docs/b.md")
    repo.save()
    report = repo.check()
    missing = {
        d["path"]
        for d in report["diagnostics"]
        if d["code"] == "portal-unreachable"
    }
    assert missing == {"docs/a.md", "docs/b.md"}
    assert "reference-missing" not in codes(report)


@pytest.mark.parametrize(
    "image",
    [
        "![Diagram](orphan.md)",
        "![Diagram][record]\n\n[record]: orphan.md",
        "![See [record](orphan.md)](diagram.svg)",
    ],
)
def test_images_do_not_establish_portal_reachability(
    repo: Repository, image: str
) -> None:
    """Image destinations and alt text cannot admit an unlinked record."""
    repo.write("docs/orphan.md", "# Orphan\n")
    repo.write("docs/diagram.svg", "<svg/>\n")
    repo.bind("docs/orphan.md")
    repo.save()
    with (repo.root / "docs/README.md").open("a") as stream:
        stream.write(f"\n{image}\n")
    assert repo.check()["diagnostics"] == [
        {
            "severity": "error",
            "code": "portal-unreachable",
            "path": "docs/orphan.md",
            "message": "No explicit link chain from docs/README.md",
        }
    ]


def test_linked_images_preserve_navigation_and_asset_validation(
    repo: Repository,
) -> None:
    """A linked image retains its outer link and validates its asset."""
    repo.write("docs/reader.md", "# Reader\n")
    repo.write("docs/diagram.svg", "<svg/>\n")
    repo.bind("docs/reader.md")
    repo.save()
    with (repo.root / "docs/README.md").open("a") as stream:
        stream.write("\n[![Diagram](diagram.svg)](reader.md)\n")
    assert repo.check()["diagnostics"] == []
    (repo.root / "docs/diagram.svg").unlink()
    assert repo.check()["diagnostics"] == [
        {
            "severity": "error",
            "code": "reference-missing",
            "path": "docs/README.md",
            "message": "diagram.svg",
        }
    ]


@pytest.mark.parametrize("invalid_snapshot", ["base", "candidate", "both"])
def test_invalid_markdown_preserves_cli_report(
    repo: Repository, invalid_snapshot: str
) -> None:
    """Malformed Markdown keeps JSON recoverable and ID checks unavailable."""
    path = "docs/requirements.md"
    valid = "# REQ-001: Retained behavior\n"
    repo.write(path, valid)
    repo.bind(path)
    repo.save()
    repo.route_all()
    if invalid_snapshot in {"base", "both"}:
        (repo.root / path).write_bytes(b"# REQ-001: Invalid \xff\n")
    repo.base = repo.commit()
    if invalid_snapshot == "base":
        repo.write(path, valid)
    elif invalid_snapshot == "candidate":
        (repo.root / path).write_bytes(b"# REQ-001: Invalid \xff\n")
    candidate = repo.base if invalid_snapshot == "both" else repo.commit()
    output = repo.root / "report-output.txt"
    arguments = [
        "--repository-root",
        str(repo.root),
        "--base",
        repo.base,
        "--candidate",
        candidate,
        "--output",
        str(output),
    ]
    assert checker.main(arguments) == 1
    report = json.loads(output.read_text())
    expected = {
        "base": {"base-markdown-input-invalid"},
        "candidate": {"markdown-input-invalid"},
        "both": {"base-markdown-input-invalid", "markdown-input-invalid"},
    }[invalid_snapshot]
    assert codes(report) == expected
    assert {d["path"] for d in report["diagnostics"]} == {path}
    assert report["checks"]["requirement-heading-identifiers"].startswith(
        "unavailable:"
    )
    assert "identifier_comparison" not in report
    assert report["checks"]["portal-reachability"] == "executed"
    assert report["command"] == arguments
    assert report["base"]["commit"] == repo.base
    assert report["candidate"]["commit"] == candidate


@pytest.mark.parametrize("field", ["governing_rules", "implementation"])
def test_malformed_reference_preserves_cli_report(
    repo: Repository, field: str
) -> None:
    """Malformed structured URLs fail within the recoverable report boundary."""
    with (repo.root / "docs/README.md").open("a") as stream:
        # The pinned Markdown parser normalizes these destinations to http://%5B.
        stream.write("\n[Link](http://[)\n\n![Image](http://[)\n")
    assert repo.check()["diagnostics"] == []
    controls = yaml.safe_load((repo.root / checker.CONTROL_CATALOG).read_text())
    if field == "governing_rules":
        controls["controls"][0][field] = ["http://["]
    else:
        controls["controls"][0][field] = {
            "kind": "repository-path",
            "value": "http://[",
        }
    repo.write(checker.CONTROL_CATALOG, yaml.safe_dump(controls))
    candidate = repo.commit()
    output = repo.root / "report-output.txt"
    arguments = [
        "--repository-root",
        str(repo.root),
        "--base",
        repo.base,
        "--candidate",
        candidate,
        "--output",
        str(output),
    ]
    assert checker.main(arguments) == 1
    report = json.loads(output.read_text())
    assert report["diagnostics"] == [
        {
            "severity": "error",
            "code": "reference-invalid",
            "path": checker.CONTROL_CATALOG,
            "message": "http://[",
        }
    ]
    assert report["checks"]["requirement-heading-identifiers"].startswith(
        "executed:"
    )
    assert report["command"] == arguments
    assert report["candidate"]["commit"] == candidate


def test_human_source_retains_independent_classification_and_coverage(
    repo: Repository,
) -> None:
    """Original source input remains visible without a candidate binding."""
    owner = "src/public/lib/asciidoctor-latexmath"
    path = owner + "/docs/research/processor-selection-input.txt"
    repo.write(path, "Original question and assistant discussion.\n")
    report = repo.check()
    assert (
        next(e for e in report["manifest"] if e["path"] == path)[
            "classification"
        ]
        == "human-source-input"
    )
    assert any(
        d["code"] == "record-coverage" and d["path"] == path
        for d in report["diagnostics"]
    )
    repo.bind(path, "text", namespace=owner)
    repo.save()
    repo.route_all()
    assert repo.check()["diagnostics"] == []
    repo.base = repo.commit()
    repo.catalog["bindings"].pop()
    repo.save()
    assert any(
        d["code"] == "record-coverage" and d["path"] == path
        for d in repo.check()["diagnostics"]
    )
    assert checker.classify(owner + "/docs/research/other-input.txt") == (
        "source-or-other-asset"
    )


@pytest.mark.parametrize("scenario", ["new", "candidate", "base", "removal"])
def test_bound_fixture_markdown_validation(
    repo: Repository, scenario: str
) -> None:
    """Explicit fixture ownership retains parse and identifier obligations."""
    path = "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md"
    valid = "# REQ-001: Example contract\n"
    repo.write(path, valid)
    repo.bind(path, namespace="src/public/lib/hexo-renderer-asciidoc")
    repo.save()
    repo.route_all()
    if scenario == "base":
        (repo.root / path).write_bytes(b"# Invalid \xff\n")
    if scenario != "new":
        repo.base = repo.commit()
    if scenario == "base":
        repo.write(path, valid)
    elif scenario == "removal":
        repo.write(path, "# Example without its established identifier\n")
    else:
        (repo.root / path).write_bytes(b"# Invalid \xff\n")
    report = repo.check()
    expected = {
        "new": "markdown-input-invalid",
        "candidate": "markdown-input-invalid",
        "base": "base-markdown-input-invalid",
        "removal": "established-identifier-removed",
    }[scenario]
    assert [(d["code"], d["path"]) for d in report["diagnostics"]] == [
        (expected, path)
    ]
    assert report["checks"]["requirement-heading-identifiers"].startswith(
        "executed:" if scenario == "removal" else "unavailable:"
    )
    assert (
        next(e for e in report["manifest"] if e["path"] == path)[
            "classification"
        ]
        == "fixture-product-asset"
    )


def test_unbound_fixture_markdown_remains_asset(repo: Repository) -> None:
    """Binary example input without record ownership is not a broken record."""
    path = "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/README.md"
    repo.write(path, "Placeholder\n")
    (repo.root / path).write_bytes(b"# Invalid \xff\n")
    assert repo.check()["diagnostics"] == []


@pytest.mark.parametrize("version", [2, 3])
@pytest.mark.parametrize("mutation", ["missing-list", "missing-field", "root"])
def test_invalid_base_catalog_preserves_cli_report(
    repo: Repository, version: int, mutation: str
) -> None:
    """The accepted schema prevents malformed bases from losing obligations."""
    if version == 2:
        write_legacy_catalog(repo)
    catalog = yaml.safe_load((repo.root / checker.FAMILY_CATALOG).read_text())
    key = "families" if version == 2 else "bindings"
    if mutation == "missing-list":
        catalog.pop(key)
    elif mutation == "missing-field":
        catalog[key][0].pop("state")
    else:
        catalog = None
    repo.write(checker.FAMILY_CATALOG, yaml.safe_dump(catalog))
    repo.base = repo.commit()
    repo.save()
    # Candidate schema changes cannot weaken validation of the accepted base.
    repo.write(checker.SCHEMA_BINDINGS[checker.FAMILY_CATALOG], "{}\n")
    candidate = repo.commit()
    output = repo.root / "report-output.txt"
    assert (
        checker.main(
            [
                "--repository-root",
                str(repo.root),
                "--base",
                repo.base,
                "--candidate",
                candidate,
                "--output",
                str(output),
            ]
        )
        == 1
    )
    report = json.loads(output.read_text())
    assert [(d["code"], d["path"]) for d in report["diagnostics"]] == [
        ("catalog-version-or-base-invalid", checker.FAMILY_CATALOG)
    ]
    assert report["checks"]["accepted-family-catalog"] == "failed"
    assert report["checks"]["dependent-record-checks"].startswith(
        "unavailable:"
    )
    assert "accepted_record_obligations" not in report
    assert "identifier_comparison" not in report
    assert report["base"]["commit"] == repo.base


@pytest.mark.parametrize("version", [None, 2, 3])
@pytest.mark.parametrize(
    "mutation", ["missing-list", "missing-state", "root", "id", "path"]
)
def test_permissive_schema_preserves_input_boundary(
    repo: Repository, version: int | None, mutation: str
) -> None:
    """Editable policy schemas cannot remove consumed catalog field types."""
    if version == 2:
        write_legacy_catalog(repo)
    path = checker.FAMILY_CATALOG
    catalog = yaml.safe_load((repo.root / path).read_text())
    key = "families" if version == 2 else "bindings"
    if mutation == "missing-list":
        catalog.pop(key)
    elif mutation == "missing-state":
        catalog[key][0].pop("state")
    elif mutation == "root":
        catalog = []
    else:
        catalog[key][0][mutation] = [] if mutation == "id" else {}
    repo.write(path, yaml.safe_dump(catalog))
    repo.write(checker.SCHEMA_BINDINGS[path], "{}\n")
    if version is not None:
        repo.base = repo.commit()
        repo.save()
    candidate = repo.commit()
    output = repo.root / "report-output.txt"
    assert (
        checker.main(
            [
                "--repository-root",
                str(repo.root),
                "--base",
                repo.base,
                "--candidate",
                candidate,
                "--output",
                str(output),
            ]
        )
        == 1
    )
    report = json.loads(output.read_text())
    expected = (
        "schema-input-invalid"
        if version is None
        else "catalog-version-or-base-invalid"
    )
    assert [(d["code"], d["path"]) for d in report["diagnostics"]] == [
        (expected, path)
    ]
    assert report["checks"]["dependent-record-checks"].startswith(
        "unavailable:"
    )
    assert "identifier_comparison" not in report
    assert report["candidate"]["commit"] == candidate


@pytest.mark.parametrize(
    "mutation",
    ["rules-missing", "rule-type", "implementation-null", "value-type"],
)
def test_permissive_control_schema_preserves_input_boundary(
    repo: Repository, mutation: str
) -> None:
    """Malformed control fields retain recoverable diagnostics."""
    path = checker.CONTROL_CATALOG
    catalog = yaml.safe_load((repo.root / path).read_text())
    control = catalog["controls"][0]
    if mutation == "rules-missing":
        control.pop("governing_rules")
    elif mutation == "rule-type":
        control["governing_rules"] = [None]
    elif mutation == "implementation-null":
        control["implementation"] = None
    else:
        control["implementation"] = {"kind": "repository-path", "value": 42}
    repo.write(path, yaml.safe_dump(catalog))
    repo.write(checker.SCHEMA_BINDINGS[path], "{}\n")
    candidate = repo.commit()
    output = repo.root / "report-output.txt"
    assert (
        checker.main(
            [
                "--repository-root",
                str(repo.root),
                "--base",
                repo.base,
                "--candidate",
                candidate,
                "--output",
                str(output),
            ]
        )
        == 1
    )
    report = json.loads(output.read_text())
    assert [(d["code"], d["path"]) for d in report["diagnostics"]] == [
        ("schema-input-invalid", path)
    ]
    assert report["checks"]["dependent-record-checks"].startswith(
        "unavailable:"
    )


@pytest.mark.parametrize(
    "target",
    [
        "https://example.invalid/tool",
        "//example.invalid/tool",
        "docs/README.md",
    ],
)
def test_repository_implementation_requires_local_path(
    repo: Repository, target: str
) -> None:
    """Typed implementations require local paths; Markdown permits URLs."""
    path = checker.CONTROL_CATALOG
    catalog = yaml.safe_load((repo.root / path).read_text())
    catalog["controls"][0]["implementation"] = {
        "kind": "repository-path",
        "value": target,
    }
    repo.write(path, yaml.safe_dump(catalog))
    with (repo.root / "docs/README.md").open("a") as stream:
        stream.write("\n[External](https://example.invalid/tool)\n")
    report = repo.check()
    expected = (
        []
        if target == "docs/README.md"
        else [
            {
                "severity": "error",
                "code": "repository-reference-required",
                "path": path,
                "message": target,
            }
        ]
    )
    assert report["diagnostics"] == expected


@pytest.mark.parametrize(
    ("target", "expected"),
    [
        (
            "https://example.invalid/tool#records",
            "repository-reference-required",
        ),
        ("//example.invalid/tool#records", "repository-reference-required"),
        ("docs/README.md", "retirement-target-invalid"),
        ("#records", "retirement-target-invalid"),
        ("docs/README.md#records", None),
        ("REQ-002", None),
        ("http://[", "reference-invalid"),
    ],
)
def test_retirement_requires_local_path_and_anchor(
    repo: Repository, target: str, expected: str | None
) -> None:
    """Retirements require local anchored authority or a defined ID."""
    path = "docs/requirements.md"
    repo.write(
        path,
        f"# REQ-001: Retired - current authority: {target}\n"
        "\n# REQ-002: Current\n",
    )
    repo.bind(path)
    repo.save()
    repo.route_all()
    report = repo.check()
    assert report["diagnostics"] == (
        []
        if expected is None
        else [
            {
                "severity": "error",
                "code": expected,
                "path": path,
                "message": target,
            }
        ]
    )


@pytest.mark.parametrize("source_kind", ["local", "remote"])
@pytest.mark.parametrize(
    "mutation",
    [None, "orphan", "directory", "owner", "duplicate", "malformed", "missing"],
)
def test_generated_interface_requires_exact_source_mapping(
    repo: Repository, source_kind: str, mutation: str | None
) -> None:
    """Only exact deployments with an unambiguous source establish ownership."""
    path = ".agents/skills/example/SKILL.md"
    repo.write(path, "# Generated interface\n")
    owner = "example/skills/plugin"
    dependency = {
        "repo_url": "example/skills",
        "virtual_path": "plugin",
        "resolved_commit": "a" * 40,
        "deployed_files": [path],
    }
    if source_kind == "local":
        dependency["local_path"] = "./src/private/lib/example"
        owner = dependency["local_path"]
        repo.write("src/private/lib/example/skill.txt", "Source input\n")
    deployment = {
        "kind": "project-relative",
        "value": path,
        "owners": [owner],
        "active_owner": owner,
    }
    lock = {"dependencies": [dependency], "deployments": [deployment]}
    if mutation == "orphan":
        dependency["deployed_files"] = []
    elif mutation == "directory":
        deployment["value"] = ".agents/skills/example"
        dependency["deployed_files"] = [deployment["value"]]
    elif mutation == "owner":
        deployment["active_owner"] = "unknown/source"
    elif mutation == "duplicate":
        lock["dependencies"].append(dependency.copy())
    elif mutation == "malformed":
        dependency["deployed_files"] = None
    if mutation != "missing":
        repo.write("apm.lock.yaml", yaml.safe_dump(lock))
    candidate = repo.commit()
    output = repo.root / "report-output.txt"
    assert checker.main(
        [
            "--repository-root",
            str(repo.root),
            "--base",
            repo.base,
            "--candidate",
            candidate,
            "--output",
            str(output),
        ]
    ) == (0 if mutation is None else 1)
    report = json.loads(output.read_text())
    entry = next(e for e in report["manifest"] if e["path"] == path)
    if mutation is None:
        assert report["diagnostics"] == []
        assert entry["classification"] == "generated-interface"
        assert entry["canonical_source"] == (
            {"kind": "local-package", "path": "src/private/lib/example"}
            if source_kind == "local"
            else {
                "kind": "locked-package",
                "repository": "example/skills",
                "path": "plugin",
                "commit": "a" * 40,
            }
        )
    else:
        assert entry["classification"] == "unresolved"
        assert "canonical_source" not in entry
        assert any(
            d["code"] == "classification-unresolved" and d["path"] == path
            for d in report["diagnostics"]
        )
        if mutation in {"missing", "malformed"}:
            assert "generated-source-map-invalid" in codes(report)
