"""Real-Git merge-time contracts for Platform-Orphan consumption."""

# ruff: noqa: C901, D103, PLR0912, PLR0915, S607

from __future__ import annotations

import importlib.util
import subprocess
import sys
from copy import deepcopy
from pathlib import Path

import pytest
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.governance.platform_orphan_history import (
    PlatformOrphanHistoryError,
    validate_platform_orphan_history,
)
from three_workflow_delivery_v3.records import (
    PLATFORM_ORPHAN_AUTHORITY_PATH,
    PLATFORM_ORPHAN_RESULT_PATH,
)

REPO_ROOT = Path(__file__).resolve().parents[6]
ACTIVE_BYTES = (
    Path(__file__).resolve().parents[1]
    / "fixtures/governance/platform-orphan-active-authority.json"
).read_bytes()
_RECORD_TEST_SPEC = importlib.util.spec_from_file_location(
    "_platform_orphan_record_fixtures",
    Path(__file__).with_name("test_platform_orphan_records.py"),
)
assert _RECORD_TEST_SPEC is not None
assert _RECORD_TEST_SPEC.loader is not None
_RECORD_FIXTURES = importlib.util.module_from_spec(_RECORD_TEST_SPEC)
sys.modules[_RECORD_TEST_SPEC.name] = _RECORD_FIXTURES
_RECORD_TEST_SPEC.loader.exec_module(_RECORD_FIXTURES)


def _git(repo: Path, *arguments: str) -> str:
    result = subprocess.run(  # noqa: S603
        ("git", *arguments),
        cwd=repo,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def _write(repo: Path, path: str, content: bytes) -> None:
    destination = repo / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(content)


def _commit(repo: Path, message: str) -> str:
    _git(repo, "add", "--all")
    _git(repo, "commit", "--quiet", "--message", message)
    return _git(repo, "rev-parse", "HEAD")


def _active_repository(tmp_path: Path) -> tuple[Path, str]:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Platform Orphan Test")
    _git(repo, "config", "user.email", "platform-orphan@example.invalid")
    _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, ACTIVE_BYTES)
    return repo, _commit(repo, "active authority")


def _empty_repository(tmp_path: Path) -> tuple[Path, str]:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Platform Orphan Test")
    _git(repo, "config", "user.email", "platform-orphan@example.invalid")
    _write(repo, "baseline", b"baseline\n")
    return repo, _commit(repo, "baseline")


def _candidate_and_audit(repo: Path, base_oid: str) -> tuple[bytes, bytes]:
    candidate = _RECORD_FIXTURES._candidate_document()  # noqa: SLF001
    active_blob_oid = _git(
        repo,
        "rev-parse",
        f"{base_oid}:{PLATFORM_ORPHAN_AUTHORITY_PATH}",
    )
    candidate["producer"]["control_commit"] = base_oid
    for field in ("initial_commit", "final_commit", "parent_main_commit"):
        candidate["authority"][field] = base_oid
    for field in ("initial_blob_oid", "final_blob_oid"):
        candidate["authority"][field] = active_blob_oid
    candidate["result_digest"] = canonical_sha256(
        {
            key: value
            for key, value in candidate.items()
            if key != "result_digest"
        },
    )
    audit = _RECORD_FIXTURES._audit_document(candidate)  # noqa: SLF001
    audit["consumption"]["producer_control_commit"] = base_oid
    audit["consumption"]["result_sha256"] = canonical_sha256(candidate)
    return canonicalize(candidate), canonicalize(audit)


def _consume(repo: Path, base_oid: str) -> tuple[str, bytes, bytes]:
    result_bytes, audit_bytes = _candidate_and_audit(repo, base_oid)
    _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
    _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
    return _commit(repo, "consume authority"), result_bytes, audit_bytes


def test_valid_atomic_consumption_uses_only_explicit_commit_trees(
    tmp_path: Path,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    head_oid, _, _ = _consume(repo, base_oid)
    _write(repo, PLATFORM_ORPHAN_RESULT_PATH, b"dirty working tree\n")

    validate_platform_orphan_history(repo, base_oid, head_oid)


def test_initial_active_authority_introduction_is_admitted(
    tmp_path: Path,
) -> None:
    repo, base_oid = _empty_repository(tmp_path)
    _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, ACTIVE_BYTES)
    head_oid = _commit(repo, "introduce active authority")

    validate_platform_orphan_history(repo, base_oid, head_oid)


@pytest.mark.parametrize(
    "mutation",
    ["invalid-authority", "result", "result-only", "authority-descendant"],
)
def test_invalid_initial_authority_introduction_is_rejected(
    tmp_path: Path,
    mutation: str,
) -> None:
    repo, base_oid = _empty_repository(tmp_path)
    if mutation == "invalid-authority":
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, b"{}\n")
    elif mutation == "result-only":
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, b"{}\n")
    elif mutation == "authority-descendant":
        _write(
            repo,
            f"{PLATFORM_ORPHAN_AUTHORITY_PATH}/descendant",
            b"invalid\n",
        )
    else:
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, ACTIVE_BYTES)
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, b"{}\n")
    head_oid = _commit(repo, f"invalid initialization {mutation}")

    with pytest.raises(PlatformOrphanHistoryError):
        validate_platform_orphan_history(repo, base_oid, head_oid)


def test_valid_consumption_allows_equal_bytes_at_pathspec_magic_path(
    tmp_path: Path,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    _write(repo, ":(glob)unrelated-authority-copy.json", ACTIVE_BYTES)
    head_oid, _, _ = _consume(repo, base_oid)

    validate_platform_orphan_history(repo, base_oid, head_oid)


def test_range_helper_admits_valid_atomic_consumption(tmp_path: Path) -> None:
    repo, base_oid = _active_repository(tmp_path)
    head_oid, _, _ = _consume(repo, base_oid)

    result = subprocess.run(  # noqa: S603
        (
            sys.executable,
            str(REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py"),
            "--repository",
            str(repo),
            "--from-ref",
            base_oid,
            "--to-ref",
            head_oid,
        ),
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0
    assert "Traceback" not in result.stderr
    assert PLATFORM_ORPHAN_AUTHORITY_PATH in result.stdout
    assert PLATFORM_ORPHAN_RESULT_PATH in result.stdout


def test_clean_plain_python_does_not_mask_required_package_import(
    tmp_path: Path,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    descendant = f"{PLATFORM_ORPHAN_RESULT_PATH}/descendant"
    _write(repo, descendant, b"trigger\n")
    head_oid = _commit(repo, "descendant trigger")

    result = subprocess.run(  # noqa: S603
        (
            sys.executable,
            "-I",
            "-S",
            str(REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py"),
            "--repository",
            str(repo),
            "--from-ref",
            base_oid,
            "--to-ref",
            head_oid,
        ),
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "No module named 'three_workflow_delivery_v3'" in result.stderr


def test_descendant_only_change_invokes_history_validator(
    tmp_path: Path,
) -> None:
    helper_spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_hk_descendant",
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py",
    )
    assert helper_spec is not None
    assert helper_spec.loader is not None
    helper = importlib.util.module_from_spec(helper_spec)
    sys.modules[helper_spec.name] = helper
    helper_spec.loader.exec_module(helper)
    repo, base_oid = _active_repository(tmp_path)
    descendant = f"{PLATFORM_ORPHAN_RESULT_PATH}/descendant"
    _write(repo, descendant, b"trigger\n")
    head_oid = _commit(repo, "descendant trigger")
    resolved = helper.changed_range(repo, base_oid, head_oid)
    captured: list[tuple[Path, str, str]] = []

    helper._validate_affected_history(  # noqa: SLF001
        repo,
        resolved,
        validator=lambda repository, base, head: captured.append(
            (repository, base, head),
        ),
    )

    assert resolved.paths == (descendant,)
    assert captured == [(repo, base_oid, head_oid)]


def test_similar_prefix_does_not_invoke_history_validator(
    tmp_path: Path,
) -> None:
    helper_spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_hk_similar_prefix",
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py",
    )
    assert helper_spec is not None
    assert helper_spec.loader is not None
    helper = importlib.util.module_from_spec(helper_spec)
    sys.modules[helper_spec.name] = helper
    helper_spec.loader.exec_module(helper)
    repo, base_oid = _active_repository(tmp_path)
    similar = f"{PLATFORM_ORPHAN_RESULT_PATH}.backup/descendant"
    _write(repo, similar, b"unrelated\n")
    head_oid = _commit(repo, "similar prefix")
    resolved = helper.changed_range(repo, base_oid, head_oid)
    captured: list[tuple[Path, str, str]] = []

    helper._validate_affected_history(  # noqa: SLF001
        repo,
        resolved,
        validator=lambda repository, base, head: captured.append(
            (repository, base, head),
        ),
    )

    assert resolved.paths == (similar,)
    assert captured == []


@pytest.mark.parametrize(
    ("path", "tree_side"),
    [
        (PLATFORM_ORPHAN_AUTHORITY_PATH, "base"),
        (PLATFORM_ORPHAN_RESULT_PATH, "base"),
        (PLATFORM_ORPHAN_AUTHORITY_PATH, "head"),
        (PLATFORM_ORPHAN_RESULT_PATH, "head"),
    ],
)
def test_fixed_path_tree_is_controlled_history_error(
    tmp_path: Path,
    path: str,
    tree_side: str,
) -> None:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "--quiet")
    _git(repo, "config", "user.name", "Platform Orphan Test")
    _git(repo, "config", "user.email", "platform-orphan@example.invalid")
    if tree_side == "base" and path == PLATFORM_ORPHAN_AUTHORITY_PATH:
        _write(repo, f"{path}/child", b"tree\n")
    else:
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, ACTIVE_BYTES)
        if tree_side == "base":
            _write(repo, f"{path}/child", b"tree\n")
    base_oid = _commit(repo, "base")
    if tree_side == "head":
        fixed_path = repo / path
        if fixed_path.is_file():
            fixed_path.unlink()
        _write(repo, f"{path}/child", b"tree\n")
    else:
        _write(repo, "unrelated", b"head\n")
    head_oid = _commit(repo, "head")

    with pytest.raises(PlatformOrphanHistoryError, match="is not a Git blob"):
        validate_platform_orphan_history(repo, base_oid, head_oid)


@pytest.mark.parametrize(
    "path",
    [PLATFORM_ORPHAN_AUTHORITY_PATH, PLATFORM_ORPHAN_RESULT_PATH],
)
def test_descendant_only_addition_is_rejected_by_range_helper(
    tmp_path: Path,
    path: str,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    if path == PLATFORM_ORPHAN_AUTHORITY_PATH:
        (repo / path).unlink()
    _write(repo, f"{path}/child", b"tree\n")
    head_oid = _commit(repo, "descendant only")

    result = subprocess.run(  # noqa: S603
        (
            sys.executable,
            str(REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py"),
            "--repository",
            str(repo),
            "--from-ref",
            base_oid,
            "--to-ref",
            head_oid,
        ),
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode != 0
    assert "PlatformOrphanHistoryError" in result.stderr


@pytest.mark.parametrize(
    "mutation",
    [
        "base-mismatch",
        "stale-candidate",
        "missing-result",
        "missing-authority",
        "result-only",
        "active-modify",
        "modified-candidate",
        "invalid-audit",
        "result-preexists",
        "authority-delete",
        "authority-rename",
        "result-rename-in",
    ],
)
def test_invalid_active_authority_transition_is_rejected(
    tmp_path: Path,
    mutation: str,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    result_bytes, audit_bytes = _candidate_and_audit(repo, base_oid)

    if mutation == "base-mismatch":
        _write(repo, "unrelated", b"advance base\n")
        actual_base = _commit(repo, "advance base")
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)
        base_oid = actual_base
    elif mutation == "stale-candidate":
        candidate = _RECORD_FIXTURES._candidate_document()  # noqa: SLF001
        candidate["producer"]["control_commit"] = base_oid
        for field in ("initial_commit", "final_commit"):
            candidate["authority"][field] = base_oid
        active_blob = _git(
            repo,
            "rev-parse",
            f"{base_oid}:{PLATFORM_ORPHAN_AUTHORITY_PATH}",
        )
        for field in ("initial_blob_oid", "final_blob_oid"):
            candidate["authority"][field] = active_blob
        candidate["result_digest"] = canonical_sha256(
            {
                key: value
                for key, value in candidate.items()
                if key != "result_digest"
            },
        )
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, canonicalize(candidate))
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)
    elif mutation == "missing-result":
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)
    elif mutation == "missing-authority":
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        (repo / PLATFORM_ORPHAN_AUTHORITY_PATH).unlink()
        head_oid = _commit(repo, mutation)
    elif mutation == "result-only":
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        head_oid = _commit(repo, mutation)
    elif mutation == "active-modify":
        _write(
            repo,
            PLATFORM_ORPHAN_AUTHORITY_PATH,
            ACTIVE_BYTES + b"\n",
        )
        head_oid = _commit(repo, mutation)
    elif mutation == "modified-candidate":
        candidate = deepcopy(
            _RECORD_FIXTURES._candidate_document(),  # noqa: SLF001
        )
        candidate["unexpected"] = True
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, canonicalize(candidate))
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)
    elif mutation == "invalid-audit":
        audit = _RECORD_FIXTURES._active_document()  # noqa: SLF001
        audit["exception"]["state"] = "consumed"
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, canonicalize(audit))
        head_oid = _commit(repo, mutation)
    elif mutation == "result-preexists":
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        base_oid = _commit(repo, "invalid base result")
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)
    elif mutation == "authority-delete":
        (repo / PLATFORM_ORPHAN_AUTHORITY_PATH).unlink()
        head_oid = _commit(repo, mutation)
    elif mutation == "authority-rename":
        destination = repo / "retired-authority.json"
        destination.parent.mkdir(parents=True, exist_ok=True)
        _git(repo, "mv", PLATFORM_ORPHAN_AUTHORITY_PATH, str(destination.name))
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes)
        head_oid = _commit(repo, mutation)
    else:
        _write(repo, "candidate.json", result_bytes)
        base_oid = _commit(repo, "candidate source")
        _git(repo, "mv", "candidate.json", PLATFORM_ORPHAN_RESULT_PATH)
        _write(repo, PLATFORM_ORPHAN_AUTHORITY_PATH, audit_bytes)
        head_oid = _commit(repo, mutation)

    with pytest.raises(PlatformOrphanHistoryError):
        validate_platform_orphan_history(repo, base_oid, head_oid)


@pytest.mark.parametrize("mutation", ["delete", "modify", "rename", "re-add"])
def test_consumed_pair_is_append_only(
    tmp_path: Path,
    mutation: str,
) -> None:
    repo, active_oid = _active_repository(tmp_path)
    base_oid, result_bytes, _ = _consume(repo, active_oid)
    if mutation == "delete":
        (repo / PLATFORM_ORPHAN_RESULT_PATH).unlink()
    elif mutation == "modify":
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, result_bytes + b"\n")
    elif mutation == "rename":
        _git(repo, "mv", PLATFORM_ORPHAN_RESULT_PATH, "moved-result.json")
    else:
        (repo / PLATFORM_ORPHAN_RESULT_PATH).unlink()
        _write(repo, PLATFORM_ORPHAN_RESULT_PATH, b"replacement\n")
    head_oid = _commit(repo, f"post-consumption {mutation}")

    with pytest.raises(PlatformOrphanHistoryError):
        validate_platform_orphan_history(repo, base_oid, head_oid)


def test_unrelated_change_does_not_create_an_authority_transition(
    tmp_path: Path,
) -> None:
    repo, base_oid = _active_repository(tmp_path)
    _write(repo, "unrelated.txt", b"unrelated\n")
    head_oid = _commit(repo, "unrelated")

    validate_platform_orphan_history(repo, base_oid, head_oid)


def test_range_helper_forwards_resolved_base_and_head_oids(
    tmp_path: Path,
) -> None:
    helper_spec = importlib.util.spec_from_file_location(
        "_workflow_delivery_v3_hk",
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_hk.py",
    )
    assert helper_spec is not None
    assert helper_spec.loader is not None
    helper = importlib.util.module_from_spec(helper_spec)
    sys.modules[helper_spec.name] = helper
    helper_spec.loader.exec_module(helper)
    repo, base_oid = _active_repository(tmp_path)
    _write(repo, PLATFORM_ORPHAN_RESULT_PATH, b"trigger\n")
    head_oid = _commit(repo, "trigger")
    _git(repo, "branch", "base-ref", base_oid)
    _git(repo, "branch", "head-ref", head_oid)
    resolved = helper.changed_range(repo, "base-ref", "head-ref")
    captured: list[tuple[Path, str, str]] = []

    helper._validate_affected_history(  # noqa: SLF001
        repo,
        resolved,
        validator=lambda repository, base, head: captured.append(
            (repository, base, head),
        ),
    )

    assert captured == [(repo, base_oid, head_oid)]
