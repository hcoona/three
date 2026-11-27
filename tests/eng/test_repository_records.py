# /// script
# requires-python = ">=3.13"
# dependencies = [
#   "PyYAML==6.0.4", "jsonschema==4.26.0", "markdown-it-py==4.0.0",
#   "pytest==8.3.4",
# ]
# ///
"""Scenario and invariant tests for check_repository_records.py."""

from __future__ import annotations

import importlib.util
import os
import shutil
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
sys.path.insert(0, str(ROOT / "eng/scripts"))
try:
    SPEC.loader.exec_module(checker)
finally:
    sys.path.remove(str(ROOT / "eng/scripts"))


def run_git(root: Path, *args: str) -> str:
    """Run Git in an isolated temporary repository."""
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    return subprocess.run(  # noqa: S603 - Trusted Git CLI on isolated fixtures.
        [  # noqa: S607 - Uses installed git executable.
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


class FixtureRepo:
    """Minimal Git repository initialized with wildcard v4 bindings."""

    def __init__(self, root: Path) -> None:
        """Create and commit an initial valid repository snapshot."""
        self.root = root
        root.mkdir(parents=True, exist_ok=True)
        run_git(root, "init", "-q", "-b", "main")
        schema_text = (ROOT / checker.FAMILY_SCHEMA).read_text(encoding="utf-8")
        self.write(checker.FAMILY_SCHEMA, schema_text)
        self.write("docs/README.md", "# Docs\n")
        self.write(
            "src/public/lib/alpha/README.md",
            "# Alpha\n\n## REQ-001 Initial requirement\n\nActive.\n",
        )
        self.write("src/public/lib/alpha/docs/overview.md", "# Overview\n")
        self.write(
            "src/sample/demo/README.md",
            "# Demo\n\n## REQ-001 Sample requirement\n",
        )
        self.catalog = {
            "schema_version": 4,
            "bindings": [
                {
                    "id": "portal",
                    "namespace": "repository",
                    "state": "current",
                    "path": "docs/README.md",
                },
                {
                    "id": "catalog",
                    "namespace": "repository",
                    "state": "current",
                    "path": checker.FAMILY_CATALOG,
                },
                {
                    "id": "schemas",
                    "namespace": "repository",
                    "state": "current",
                    "path": "schemas/governance/*.schema.json",
                },
                {
                    "id": "public-interfaces",
                    "namespace": "src/public/*/*",
                    "state": "current",
                    "path": "src/public/*/*/*.md",
                },
                {
                    "id": "public-records",
                    "namespace": "src/public/*/*",
                    "state": "current",
                    "path": "src/public/*/*/docs/**/*.md",
                },
                {
                    "id": "sample-interfaces",
                    "namespace": "src/sample/*",
                    "state": "current",
                    "path": "src/sample/*/*.md",
                },
                {
                    "id": "sample-records",
                    "namespace": "src/sample/*",
                    "state": "current",
                    "path": "src/sample/*/docs/**/*.md",
                },
            ],
        }
        self.save_catalog()
        self.base = self.commit("Initial valid state")

    def write(self, rel: str, content: str) -> None:
        """Write UTF-8 text to a repository-relative path."""
        target = self.root / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def save_catalog(self) -> None:
        """Serialize in-memory catalog dictionary to record-families.yaml."""
        self.write(
            checker.FAMILY_CATALOG,
            yaml.safe_dump(self.catalog, sort_keys=False),
        )

    def commit(self, message: str) -> str:
        """Stage all changes, create a commit, and return its SHA."""
        run_git(self.root, "add", "-A")
        run_git(self.root, "commit", "-q", "-m", message)
        return run_git(self.root, "rev-parse", "HEAD")


def codes(report: dict) -> list[str]:
    """Extract diagnostic error codes from a checker report."""
    return [d["code"] for d in report["diagnostics"]]


def test_real_repository_passes_against_head() -> None:
    """Confirm the repository worktree passes all record and ID checks."""
    report = checker.check_repository(ROOT, "HEAD", None)
    assert report["diagnostics"] == []


def test_wildcard_binding_covers_new_project_without_catalog_edit(
    tmp_path: Path,
) -> None:
    """Verify standard project paths match wildcard bindings automatically."""
    repo = FixtureRepo(tmp_path)
    repo.write(
        "src/public/lib/beta/README.md",
        "# Beta\n\n## REQ-001 Independent project namespace\n",
    )
    repo.write(
        "src/public/lib/beta/docs/design.md",
        "# Design\n\n## REQ-002 Scoped requirement\n",
    )
    # Moving a sample requirement into nested docs keeps its namespace.
    repo.write("src/sample/demo/README.md", "# Demo\n")
    repo.write(
        "src/sample/demo/docs/nested/requirements.md",
        "# Sample Requirements\n\n## REQ-001 Sample requirement\n",
    )
    report = checker.check_repository(repo.root, repo.base, None)
    assert report["diagnostics"] == []


def test_omitting_scheduled_state_admits_record_and_protects_its_ids(
    tmp_path: Path,
) -> None:
    """A scheduled path gains ownership and ID protection when made current."""
    repo = FixtureRepo(tmp_path)
    path = "docs/next.md"
    binding = {
        "id": "next",
        "namespace": "repository",
        "state": "scheduled",
        "path": path,
    }
    repo.catalog["bindings"].append(binding)
    repo.save_catalog()
    repo.write(path, "# Next\n\n## REQ-042 New commitment\n")
    assert "scheduled-has-instances" in codes(
        checker.check_repository(repo.root, repo.base, None)
    )

    del binding["state"]
    repo.save_catalog()
    assert codes(checker.check_repository(repo.root, repo.base, None)) == []
    accepted = repo.commit("Admit the new record")
    repo.write(path, "# Next\n")
    assert codes(checker.check_repository(repo.root, accepted, None)) == [
        "established-identifier-removed"
    ]


def test_uncovered_and_symlinked_markdown_records_fail_closed(
    tmp_path: Path,
) -> None:
    """Reject uncovered paths and symlinked files or parent directories."""
    repo = FixtureRepo(tmp_path / "repo")
    repo.write("notes/stray.md", "# Stray\n")
    assert codes(checker.check_repository(repo.root, repo.base, None)) == [
        "record-coverage"
    ]
    (repo.root / "notes/stray.md").unlink()

    ext = tmp_path / "ext"
    ext.mkdir()
    (ext / "overview.md").write_text("# External\n", encoding="utf-8")
    shutil.rmtree(repo.root / "src/public/lib/alpha/docs")
    (repo.root / "src/public/lib/alpha/docs").symlink_to(
        ext, target_is_directory=True
    )
    assert codes(checker.check_repository(repo.root, repo.base, None)) == [
        "canonical-symlink"
    ]


def test_invalid_schema_and_unsafe_glob_fail_closed(tmp_path: Path) -> None:
    """Reject schema violations and malformed double-star globs."""
    repo = FixtureRepo(tmp_path)
    repo.write(checker.FAMILY_CATALOG, "schema_version: 4\nbindings: []\n")
    report = checker.check_repository(repo.root, repo.base, None)
    assert codes(report) == ["schema-invalid"]

    repo.catalog["bindings"].append(
        {
            "id": "bad-glob",
            "namespace": "repository",
            "state": "current",
            "path": "docs/a**b.md",
        }
    )
    repo.save_catalog()
    report = checker.check_repository(repo.root, repo.base, None)
    assert "unsafe-binding-path" in codes(report)
    repo.catalog["bindings"].pop()

    # Bound structured YAML records validate against their declared JSON Schema.
    repo.write(
        "src/public/lib/alpha/schemas/reg.schema.json",
        '{"$schema":"https://json-schema.org/draft/2020-12/schema",'
        '"type":"object","required":["v"],"properties":{"v":{"const":1}}}\n',
    )
    repo.write("src/public/lib/alpha/docs/reg.yaml", "v: 1\n")
    repo.catalog["bindings"].append(
        {
            "id": "alpha-reg",
            "namespace": "src/public/lib/alpha",
            "state": "current",
            "path": "src/public/lib/alpha/docs/reg.yaml",
            "schema": "src/public/lib/alpha/schemas/reg.schema.json",
        }
    )
    repo.save_catalog()
    assert (
        checker.check_repository(repo.root, repo.base, None)["diagnostics"]
        == []
    )
    repo.write("src/public/lib/alpha/docs/reg.yaml", "v: 2\n")
    assert codes(checker.check_repository(repo.root, repo.base, None)) == [
        "record-schema-invalid"
    ]


def test_requirement_id_removal_retirement_and_duplicates(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Enforce ID preservation against main while allowing branch iteration."""
    repo = FixtureRepo(tmp_path)
    # Removing an established requirement ID without retirement fails.
    repo.write("src/public/lib/alpha/README.md", "# Alpha\n")
    report = checker.check_repository(repo.root, repo.base, None)
    assert codes(report) == ["established-identifier-removed"]

    # Retiring with a missing target fails; a valid replacement passes.
    repo.write(
        "src/public/lib/alpha/README.md",
        "# Alpha\n\n## REQ-001: Retired - current authority: REQ-002\n",
    )
    report = checker.check_repository(repo.root, repo.base, None)
    assert codes(report) == ["retirement-target-missing"]

    repo.write(
        "src/public/lib/alpha/docs/requirements.md",
        "# Requirements\n\n## REQ-002 Current behavior\n",
    )
    report = checker.check_repository(repo.root, repo.base, None)
    assert report["diagnostics"] == []

    # Duplicate definition in the same project namespace fails.
    repo.write(
        "src/public/lib/alpha/docs/other.md",
        "# Other\n\n## REQ-002 Conflicting definition\n",
    )
    report = checker.check_repository(repo.root, repo.base, None)
    assert codes(report) == ["duplicate-requirement-definition"]
    (repo.root / "src/public/lib/alpha/docs/other.md").unlink()

    # Unmerged feature-branch IDs can be removed locally, unlike main's IDs.
    for var in (
        "HK_BASE",
        "CI_BASE",
        "CI_FULL",
        "HK_CANDIDATE",
        "CI_CANDIDATE",
    ):
        monkeypatch.delenv(var, raising=False)
    run_git(repo.root, "checkout", "-q", "-b", "feat")
    repo.write(
        "src/public/lib/alpha/docs/draft.md",
        "# Draft\n\n## REQ-099 Temporary branch requirement\n",
    )
    repo.commit("Add draft requirement on feature branch")
    (repo.root / "src/public/lib/alpha/docs/draft.md").unlink()
    repo.commit("Remove unmerged draft requirement")
    assert checker.main(["--repository-root", str(repo.root), "--quiet"]) == 0
    repo.write("src/sample/demo/README.md", "# Demo\n")
    assert checker.main(["--repository-root", str(repo.root), "--quiet"]) == 1


def test_commonmark_headings_linked_retirements_and_staged_index(
    tmp_path: Path,
) -> None:
    """Parse Setext/bold headings, ignore code fences, and read :index."""
    repo = FixtureRepo(tmp_path)
    repo.write(
        "src/public/lib/alpha/README.md",
        "# Alpha\n\n"
        "**REQ-001**\nRetired - current authority: "
        "[Replacement spec](docs/overview.md#current)\n"
        "===\n\n"
        "```markdown\n## REQ-001 Ignored inside code fence\n```\n",
    )
    assert (
        checker.check_repository(repo.root, repo.base, None)["diagnostics"]
        == []
    )

    # Unrelated fragment link earlier in heading cannot mask invalid target.
    repo.write(
        "src/public/lib/alpha/README.md",
        "# Alpha\n\n## [REQ-001](#self): Retired - current authority: "
        "docs/overview.md#x\n",
    )
    report = checker.check_repository(repo.root, repo.base, None)
    assert codes(report) == ["retirement-target-invalid"]

    # Empty '#', text-directive, or external URL retirement links fail closed.
    for bad_link in (
        "[Empty](#)",
        "[REQ-002](docs/overview.md#:~:text=missing)",
        "[External](https://example.com/a#b)",
    ):
        repo.write(
            "src/public/lib/alpha/README.md",
            f"# Alpha\n\n## REQ-001: Retired - current authority: {bad_link}\n",
        )
        assert codes(checker.check_repository(repo.root, repo.base, None)) == [
            "retirement-target-invalid"
        ]

    # Staged :index snapshot ignores unstaged worktree removals.
    repo.write(
        "src/public/lib/alpha/README.md",
        "# Alpha\n\n## REQ-001 Kept in index\n",
    )
    run_git(repo.root, "add", "src/public/lib/alpha/README.md")
    repo.write("src/public/lib/alpha/README.md", "# Alpha\n")
    assert (
        checker.check_repository(repo.root, repo.base, ":index")["diagnostics"]
        == []
    )


if __name__ == "__main__":
    raise SystemExit(pytest.main([__file__, *sys.argv[1:]]))
