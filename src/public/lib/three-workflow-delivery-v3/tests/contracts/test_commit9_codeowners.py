"""Commit-9 contracts for CODEOWNERS final-match coverage."""

from __future__ import annotations

import importlib.util
import inspect
import json
import re
import socket
import subprocess
from dataclasses import dataclass
from pathlib import Path

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.cli import main
from three_workflow_delivery_v3.release.identity import (
    normalize_buddy_live_intent,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
CODEOWNERS_PATH = REPO_ROOT / ".github/CODEOWNERS"
REQUIRED_OWNER = "@hcoona"
GOVERNANCE_PATH = (
    ".github/workflow-delivery/governance/hcoona-release-smoke-npm.json"
)
PLATFORM_ORPHAN_GOVERNANCE_PATHS = (
    (
        ".github/workflow-delivery/governance/"
        "platform-orphan-run-32809578776.json"
    ),
    (
        ".github/workflow-delivery/governance/"
        "platform-orphan-run-32809578776-result.json"
    ),
)
PLATFORM_ORPHAN_GOVERNANCE_DESCENDANT_PATHS = tuple(
    f"{path}/narrow-descendant" for path in PLATFORM_ORPHAN_GOVERNANCE_PATHS
)
PLATFORM_ORPHAN_GOVERNANCE_SURFACES = (
    *PLATFORM_ORPHAN_GOVERNANCE_PATHS,
    *PLATFORM_ORPHAN_GOVERNANCE_DESCENDANT_PATHS,
)
PLATFORM_ORPHAN_SIMILAR_PREFIX_PATHS = (
    f"{PLATFORM_ORPHAN_GOVERNANCE_PATHS[0]}-similar/child",
    f"{PLATFORM_ORPHAN_GOVERNANCE_PATHS[1]}.backup/child",
)
ROOT_PYTHON_INPUTS = ("pyproject.toml", "uv.lock")
SYNTHETIC_FUTURE_SURFACES = (
    "src/workflow-delivery.release-unit.yml",
    "src/workflow-delivery.quality.yml",
    "src/public/app/new-product/workflow-delivery.release-unit.yml",
    "src/private/app/new-product/config/workflow-delivery.quality.yml",
    ".github/workflows/workflow-delivery-v3-future.yml",
    ".github/actions/workflow-delivery-v3-future/action.yml",
    ".github/actions/workflow-delivery-v3/future/action.yml",
)
OVERRIDE_EXEMPLARS = (
    *SYNTHETIC_FUTURE_SURFACES,
    *PLATFORM_ORPHAN_GOVERNANCE_SURFACES,
    "eng/scripts/workflow_delivery_v3_consumer_policy.py",
    "eng/scripts/workflow_delivery_v3_hk.py",
)
_BUDDY_CONTRACT_SPEC = importlib.util.spec_from_file_location(
    "_commit9_buddy_workflow_contract",
    Path(__file__).with_name("test_buddy_workflows.py"),
)
assert _BUDDY_CONTRACT_SPEC is not None
assert _BUDDY_CONTRACT_SPEC.loader is not None
_BUDDY_CONTRACT = importlib.util.module_from_spec(_BUDDY_CONTRACT_SPEC)
_BUDDY_CONTRACT_SPEC.loader.exec_module(_BUDDY_CONTRACT)
CALLER = _BUDDY_CONTRACT.CALLER
_document = _BUDDY_CONTRACT._document  # noqa: SLF001
_run = _BUDDY_CONTRACT._run  # noqa: SLF001
_step = _BUDDY_CONTRACT._step  # noqa: SLF001


@dataclass(frozen=True, slots=True)
class CodeOwnersRule:
    """One ordered CODEOWNERS rule."""

    pattern: str
    owners: tuple[str, ...]


def _parse_rules(content: str) -> tuple[CodeOwnersRule, ...]:
    rules: list[CodeOwnersRule] = []
    for raw_line in content.splitlines():
        line = raw_line.split("#", 1)[0].strip()
        if not line:
            continue
        fields = line.split()
        rules.append(CodeOwnersRule(fields[0], tuple(fields[1:])))
    return tuple(rules)


def _pattern_expression(pattern: str) -> re.Pattern[str]:
    normalized = pattern.removeprefix("/")
    if "/" not in normalized:
        normalized = f"**/{normalized}"
    expression: list[str] = []
    index = 0
    while index < len(normalized):
        character = normalized[index]
        if normalized[index : index + 3] == "**/":
            expression.append("(?:.*/)?")
            index += 3
            continue
        if normalized[index : index + 2] == "**":
            expression.append(".*")
            index += 2
            continue
        if character == "*":
            expression.append("[^/]*")
        elif character == "?":
            expression.append("[^/]")
        else:
            expression.append(re.escape(character))
        index += 1
    return re.compile(rf"^{''.join(expression)}$")


def _final_owners(
    rules: tuple[CodeOwnersRule, ...],
    path: str,
) -> tuple[str, ...]:
    owners: tuple[str, ...] = ()
    for rule in rules:
        if _pattern_expression(rule.pattern).fullmatch(path) is not None:
            owners = rule.owners
    return owners


def _coverage_failures(
    rules: tuple[CodeOwnersRule, ...],
    paths: set[str],
) -> dict[str, tuple[str, ...]]:
    return {
        path: owners
        for path in sorted(paths)
        if (owners := _final_owners(rules, path)) != (REQUIRED_OWNER,)
    }


def _workspace_paths() -> set[str]:
    result = subprocess.run(
        (  # noqa: S607
            "git",
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard",
        ),
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
    )
    return set(result.stdout.splitlines())


def _descriptor_paths(paths: set[str]) -> set[str]:
    basenames = {
        "workflow-delivery.release-unit.yml",
        "workflow-delivery.quality.yml",
    }
    return {
        path
        for path in paths
        if path.startswith("src/") and Path(path).name in basenames
    }


def _governed_categories(paths: set[str]) -> dict[str, set[str]]:
    return {
        "v3-package": {
            path
            for path in paths
            if path.startswith("src/public/lib/three-workflow-delivery-v3/")
        },
        "v3-engineering": {
            path
            for path in paths
            if path.startswith("eng/workflow-delivery/v3/")
        },
        "descriptors": _descriptor_paths(paths),
        "hk-surfaces": {
            path
            for path in paths
            if path
            in {
                "hk.pkl",
                "eng/scripts/hk_exec.py",
                "eng/scripts/workflow_delivery_v3_hk.py",
            }
            or path.startswith("src/private/lib/hk/")
        },
        "root-python-inputs": set(ROOT_PYTHON_INPUTS),
        "v3-workflows": {
            path
            for path in paths
            if path.startswith(".github/workflows/workflow-delivery-v3")
        },
        "v3-actions": {
            path
            for path in paths
            if path.startswith(".github/actions/")
            and "workflow-delivery-v3" in path
        },
        "v3-direct-scripts": {
            path
            for path in paths
            if path.startswith("eng/scripts/workflow_delivery_v3")
        },
        "codeowners": {".github/CODEOWNERS"},
        "protected-governance": {
            GOVERNANCE_PATH,
            *PLATFORM_ORPHAN_GOVERNANCE_SURFACES,
        },
    }


def _governed_surface_inventory() -> frozenset[str]:
    """Return the shared current, required-absent, and future v3 inventory."""
    paths = _workspace_paths() | set(SYNTHETIC_FUTURE_SURFACES)
    return frozenset(set().union(*_governed_categories(paths).values()))


ACTUAL_RULES = _parse_rules(CODEOWNERS_PATH.read_text(encoding="utf-8"))


def test_codeowners_parser_ignores_github_inline_comments() -> None:
    """Keep inline comments outside the exact final owner tuple."""
    rules = _parse_rules("/governed/** @hcoona # governance explanation\n")

    assert rules == (CodeOwnersRule("/governed/**", (REQUIRED_OWNER,)),)
    assert _final_owners(rules, "governed/file.txt") == (REQUIRED_OWNER,)


def test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface() -> (  # noqa: E501
    None
):
    """Require the real final rule to name exactly one owner for every path."""
    paths = _workspace_paths() | set(SYNTHETIC_FUTURE_SURFACES)
    categories = _governed_categories(paths)
    for category in categories.values():
        assert category

    governed_paths = set(_governed_surface_inventory())
    assert set(SYNTHETIC_FUTURE_SURFACES) <= governed_paths
    assert GOVERNANCE_PATH in governed_paths
    assert GOVERNANCE_PATH in _workspace_paths()
    assert set(PLATFORM_ORPHAN_GOVERNANCE_PATHS) <= governed_paths
    assert PLATFORM_ORPHAN_GOVERNANCE_PATHS[0] in _workspace_paths()
    assert PLATFORM_ORPHAN_GOVERNANCE_PATHS[1] not in _workspace_paths()
    assert _coverage_failures(ACTUAL_RULES, governed_paths) == {}
    assert {
        path: _final_owners(ACTUAL_RULES, path) for path in governed_paths
    } == dict.fromkeys(governed_paths, (REQUIRED_OWNER,))


def test_platform_orphan_codeowners_are_narrow_and_ordered() -> None:
    """Own descendants without broadening either singleton prefix."""
    patterns = tuple(rule.pattern for rule in ACTUAL_RULES)

    for path, descendant in zip(
        PLATFORM_ORPHAN_GOVERNANCE_PATHS,
        PLATFORM_ORPHAN_GOVERNANCE_DESCENDANT_PATHS,
        strict=True,
    ):
        descendant_pattern = f"/{path}/**"
        exact_pattern = f"/{path}"
        assert patterns.index(descendant_pattern) < patterns.index(
            exact_pattern,
        )
        assert _final_owners(ACTUAL_RULES, descendant) == (REQUIRED_OWNER,)
        assert _final_owners(ACTUAL_RULES, path) == (REQUIRED_OWNER,)

    assert {
        path: _final_owners(ACTUAL_RULES, path)
        for path in PLATFORM_ORPHAN_SIMILAR_PREFIX_PATHS
    } == dict.fromkeys(PLATFORM_ORPHAN_SIMILAR_PREFIX_PATHS, ())


@pytest.mark.parametrize(
    ("pattern", "exemplars"),
    [
        (
            "/.github/workflows/**",
            (".github/workflows/workflow-delivery-v3-future.yml",),
        ),
        (
            "/.github/actions/**",
            (
                ".github/actions/workflow-delivery-v3-future/action.yml",
                ".github/actions/workflow-delivery-v3/future/action.yml",
            ),
        ),
        (
            "/eng/scripts/**",
            (
                "eng/scripts/workflow_delivery_v3_consumer_policy.py",
                "eng/scripts/workflow_delivery_v3_hk.py",
            ),
        ),
        (
            "/src/public/lib/three-workflow-delivery-v3/**",
            (
                (
                    "src/public/lib/three-workflow-delivery-v3/"
                    "src/three_workflow_delivery_v3/cli.py"
                ),
            ),
        ),
        (
            "/eng/workflow-delivery/v3/**",
            ("eng/workflow-delivery/v3/future-policy.yml",),
        ),
        (
            "/src/**/workflow-delivery.release-unit.yml",
            ("src/public/app/future/workflow-delivery.release-unit.yml",),
        ),
        (
            "/src/**/workflow-delivery.quality.yml",
            ("src/private/app/future/workflow-delivery.quality.yml",),
        ),
        (
            f"/{GOVERNANCE_PATH}",
            (GOVERNANCE_PATH,),
        ),
        *(
            (f"/{path}/**", (descendant,))
            for path, descendant in zip(
                PLATFORM_ORPHAN_GOVERNANCE_PATHS,
                PLATFORM_ORPHAN_GOVERNANCE_DESCENDANT_PATHS,
                strict=True,
            )
        ),
        *((f"/{path}", (path,)) for path in PLATFORM_ORPHAN_GOVERNANCE_PATHS),
        ("/hk.pkl", ("hk.pkl",)),
        (
            "/src/private/lib/hk/**",
            ("src/private/lib/hk/Commit9Future.pkl",),
        ),
        ("/pyproject.toml", ("pyproject.toml",)),
        ("/uv.lock", ("uv.lock",)),
        ("/.github/CODEOWNERS", (".github/CODEOWNERS",)),
    ],
)
def test_removing_each_actual_governing_rule_exposes_its_exact_surface(
    pattern: str,
    exemplars: tuple[str, ...],
) -> None:
    """Remove one real rule and require its surface to become uncovered."""
    matching_rules = [rule for rule in ACTUAL_RULES if rule.pattern == pattern]
    assert matching_rules == [CodeOwnersRule(pattern, (REQUIRED_OWNER,))]
    mutated = tuple(rule for rule in ACTUAL_RULES if rule.pattern != pattern)
    expected = dict.fromkeys(exemplars, ())

    assert {
        path: _final_owners(mutated, path) for path in exemplars
    } == expected
    assert _coverage_failures(mutated, set(exemplars)) == expected


@pytest.mark.parametrize("path", OVERRIDE_EXEMPLARS)
def test_later_replacement_owner_override_fails_exact_final_match(
    path: str,
) -> None:
    """Reject a later exact-path replacement owner."""
    mutated = (
        *ACTUAL_RULES,
        CodeOwnersRule(f"/{path}", ("@replacement-owner",)),
    )

    assert _final_owners(mutated, path) == ("@replacement-owner",)
    assert _coverage_failures(mutated, {path}) == {
        path: ("@replacement-owner",)
    }


@pytest.mark.parametrize("path", OVERRIDE_EXEMPLARS)
def test_later_hcoona_coowner_override_fails_exact_final_match(
    path: str,
) -> None:
    """Reject a later exact-path rule that adds a co-owner."""
    mutated = (
        *ACTUAL_RULES,
        CodeOwnersRule(f"/{path}", (REQUIRED_OWNER, "@co-owner")),
    )

    assert _final_owners(mutated, path) == (REQUIRED_OWNER, "@co-owner")
    assert _coverage_failures(mutated, {path}) == {
        path: (REQUIRED_OWNER, "@co-owner")
    }


@pytest.mark.parametrize(
    "selected_ref",
    [
        "refs/heads/contributor/arbitrary-buddy-source",
        "refs/tags/arbitrary-buddy-candidate",
    ],
    ids=["branch", "tag"],
)
def test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    selected_ref: str,
) -> None:
    """Preserve arbitrary refs through the public offline CLI boundary."""

    def unexpected_network(*_args: object, **_kwargs: object) -> None:
        message = "normalization attempted network access"
        raise AssertionError(message)

    monkeypatch.setattr(cli_module, "urlopen", unexpected_network)
    monkeypatch.setattr(socket, "create_connection", unexpected_network)
    output = tmp_path / "intent.json"
    target = "1234567890abcdef1234567890abcdef12345678"

    status = main(
        [
            "release",
            "normalize-live-request",
            "--repository",
            "hcoona/three",
            "--selected-ref",
            selected_ref,
            "--target",
            target,
            "--actor",
            "commit9-test",
            "--workflow-run-id",
            "9009",
            "--run-attempt",
            "2",
            "--output",
            str(output),
        ]
    )
    intent = json.loads(output.read_bytes())

    assert status == 0
    assert {
        field: intent[field]
        for field in (
            "workflow-ref",
            "selected-ref",
            "workflow-sha",
            "target",
            "event-kind",
            "channel",
            "mode",
            "purpose",
        )
    } == {
        "workflow-ref": selected_ref,
        "selected-ref": selected_ref,
        "workflow-sha": target,
        "target": target,
        "event-kind": "workflow_dispatch",
        "channel": "buddy",
        "mode": "live",
        "purpose": "live-release",
    }
    boundary_source = (
        inspect.getsource(
            cli_module._release_normalize_live_request_command  # noqa: SLF001
        )
        + inspect.getsource(normalize_buddy_live_intent)
    ).casefold()
    assert "codeowners" not in boundary_source
    assert selected_ref in output.read_text(encoding="utf-8")


def test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate() -> (  # noqa: E501
    None
):
    """Pin actual selected-ref wiring and offline ownership scope."""
    caller = _document(CALLER)
    request = caller["jobs"]["request"]
    normalization_step = _step(request, "Normalize fixed live request")
    command = _run(normalization_step)
    folded = command.casefold()
    conditions = [
        condition
        for job in caller["jobs"].values()
        for condition in (
            job.get("if"),
            *(
                step.get("if")
                for step in job.get("steps", ())
                if isinstance(step, dict)
            ),
        )
        if isinstance(condition, str)
    ]

    assert "release normalize-live-request" in command
    assert '--selected-ref "${GITHUB_REF}"' in command
    assert 'echo "selected-ref=${GITHUB_REF}" >> "${GITHUB_OUTPUT}"' in command
    assert "if" not in request
    assert "if" not in normalization_step
    assert all(
        "github.ref" not in condition.casefold() for condition in conditions
    )
    assert "refs/heads/" not in command
    assert "codeowners" not in folded
    assert "api.github.com" not in folded
    assert "curl " not in folded
    assert "wget " not in folded
