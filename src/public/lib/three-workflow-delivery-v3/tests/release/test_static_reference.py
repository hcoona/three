"""Scenario and contract tests for the bounded static-reference foundation."""

from __future__ import annotations

import importlib.util
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.release.static_reference_model import (
    PRODUCER_PACKAGE,
    STATIC_REFERENCE_ERROR_KINDS,
    STATIC_REFERENCE_POLICY_ID,
    STATIC_REFERENCE_SOURCE_KINDS,
    BoundedStaticReferenceResult,
    StaticReferenceFinding,
    native_repository_path,
    normalized_repository_path,
    parse_bounded_static_reference_result,
    utf8_sort_key,
)
from three_workflow_delivery_v3.release.static_reference_policy import (
    STATIC_REFERENCE_POLICY_DIGEST,
    scan_bounded_static_references,
    validate_bounded_static_reference_result,
)
from three_workflow_delivery_v3.release.static_reference_session import (
    StaticReferenceCleanupError,
    StaticReferenceSession,
)
from three_workflow_delivery_v3.release.static_reference_source import (
    InvalidRepositoryRootError,
    SourceAcquisitionError,
    StaticReferenceCandidate,
    StaticReferenceSelection,
    acquire_static_reference_inventory,
    resolve_static_reference_repository_root,
)

if TYPE_CHECKING:
    from types import ModuleType

REPO_ROOT = Path(__file__).resolve().parents[6]
STATIC_REFERENCE_SCRIPT = (
    REPO_ROOT / "eng/scripts/workflow_delivery_v3_static_reference.py"
)
PREPARE_STATIC_REFERENCE_SCRIPT = (
    REPO_ROOT / "eng/scripts/workflow_delivery_v3_prepare_static_reference.py"
)
TARGET = "1" * 40
DIGEST = "sha256:" + ("a" * 64)


def _git(repository: Path, *arguments: str) -> str:
    return subprocess.run(  # noqa: S603
        ("git", *arguments),  # noqa: S607
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()


def _initialize_repository(repository: Path) -> None:
    repository.mkdir()
    _git(repository, "init", "--quiet")
    _git(repository, "config", "user.name", "Static Reference Test")
    _git(
        repository,
        "config",
        "user.email",
        "static-reference@example.invalid",
    )


def _load_static_reference_script() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "workflow_delivery_v3_static_reference_test_entry",
        STATIC_REFERENCE_SCRIPT,
    )
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _load_prepare_static_reference_script() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "workflow_delivery_v3_prepare_static_reference_test_entry",
        PREPARE_STATIC_REFERENCE_SCRIPT,
    )
    assert spec is not None
    assert spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _finding(
    *,
    path: str = "consumer/package.json",
    context: str = "dependencies",
) -> StaticReferenceFinding:
    return StaticReferenceFinding(
        path=path,
        family="npm-manifest",
        context=context,
        prohibited_form="D",
        matched_identity=PRODUCER_PACKAGE,
        location=f"{context}.{PRODUCER_PACKAGE}",
    )


def _result(
    *,
    findings: tuple[StaticReferenceFinding, ...] = (),
    error_kind: str | None = None,
) -> BoundedStaticReferenceResult:
    return BoundedStaticReferenceResult(
        source_kind="index",
        target=None,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=(),
        findings=findings,
        error_kind=error_kind,  # type: ignore[arg-type]
    )


def _candidate(
    path: str = "nested/package.json",
    content: bytes = b'{"name":"example"}\n',
) -> StaticReferenceCandidate:
    return StaticReferenceCandidate(
        path=path,
        selection=StaticReferenceSelection(
            family="npm-manifest",
            graph_id="npm-manifest-v1",
            input_mode="strict-utf8-file",
        ),
        content=content,
        source_object="blob-object",
    )


def test_model_source_kinds_and_all_seven_error_kinds_are_exact_and_distinct() -> (  # noqa: E501
    None
):
    """Keep source and error-kind vocabularies exact and duplicate-free."""
    assert STATIC_REFERENCE_SOURCE_KINDS == (
        "git-target",
        "index",
        "worktree",
    )
    assert STATIC_REFERENCE_ERROR_KINDS == (
        "source-acquisition-failed",
        "encoding-rejected",
        "authority-rejected",
        "authority-execution-failed",
        "unsupported-projection",
        "authority-mismatch",
        "cleanup-failed",
    )
    assert len(STATIC_REFERENCE_ERROR_KINDS) == len(
        set(STATIC_REFERENCE_ERROR_KINDS)
    )


def test_model_applies_exact_path_and_utf8_ordering() -> None:
    """Normalize paths and sort them by their UTF-8 byte representation."""
    values = ("é/package.json", "z/package.json", "a/package.json")

    assert sorted(values, key=utf8_sort_key) == [
        "a/package.json",
        "z/package.json",
        "é/package.json",
    ]
    assert (
        normalized_repository_path(
            r"consumer\literal/package.json",
            field="path",
        )
        == r"consumer\literal/package.json"
    )

    for invalid in (
        "",
        ".",
        "/package.json",
        "../package.json",
        "a/../package.json",
        "a//package.json",
        "package.json\0ignored",
    ):
        with pytest.raises(ValueError, match="normalized"):
            normalized_repository_path(invalid, field="path")


def test_model_finding_and_result_parse_the_canonical_document() -> None:
    """Round-trip canonical findings and bounded scan results."""
    finding = _finding()
    expected_finding = {
        "path": "consumer/package.json",
        "family": "npm-manifest",
        "semantic-context": "dependencies",
        "prohibited-form": "D",
        "matched-identity": PRODUCER_PACKAGE,
        "location": f"dependencies.{PRODUCER_PACKAGE}",
    }
    result = BoundedStaticReferenceResult(
        source_kind="git-target",
        target=TARGET,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=DIGEST,
        implementation_identities=("node@24.19.0", "npm-package-arg@14.0.0"),
        findings=(finding,),
    )

    assert finding.to_document() == expected_finding
    assert result.result == "findings"
    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "findings",
        "source-kind": "git-target",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": DIGEST,
        "implementation-identities": [
            "node@24.19.0",
            "npm-package-arg@14.0.0",
        ],
        "findings": [expected_finding],
        "target": TARGET,
    }
    assert (
        parse_bounded_static_reference_result(
            canonicalize(result.to_document())
        )
        == result
    )


@pytest.mark.parametrize(
    "document",
    [
        {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "clean",
            "source-kind": "index",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": DIGEST,
            "implementation-identities": [],
            "findings": [],
            "unknown": True,
        },
        {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "error",
            "source-kind": "worktree",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": DIGEST,
            "implementation-identities": [],
            "findings": [],
        },
        {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "clean",
            "source-kind": "index",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": DIGEST,
            "implementation-identities": {},
            "findings": [],
        },
        {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "clean",
            "source-kind": "index",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": DIGEST,
            "implementation-identities": ["z@1", "a@1"],
            "findings": [],
        },
        {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "error",
            "source-kind": "index",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": DIGEST,
            "implementation-identities": [],
            "findings": [],
            "error-kind": "other",
        },
    ],
    ids=(
        "unknown-field",
        "missing-error-kind",
        "wrong-type",
        "invalid-order",
        "invalid-error-kind",
    ),
)
def test_model_finding_and_result_reject_invalid_shape(
    document: dict[str, object],
) -> None:
    """Reject malformed canonical result documents."""
    with pytest.raises((TypeError, ValueError)):
        parse_bounded_static_reference_result(canonicalize(document))


def test_source_uses_its_own_git_target_index_and_worktree_bytes(
    tmp_path: Path,
) -> None:
    """Acquire each source kind from its independently owned bytes."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    manifest = repository / "package.json"
    committed = b'{"name":"committed"}\n'
    indexed = b'{"name":"indexed"}\n'
    worktree = b'{"name":"worktree"}\n'
    manifest.write_bytes(committed)
    _git(repository, "add", "package.json")
    _git(repository, "commit", "--quiet", "--message", "target")
    target = _git(repository, "rev-parse", "HEAD")
    manifest.write_bytes(indexed)
    _git(repository, "add", "package.json")
    manifest.write_bytes(worktree)

    target_inventory = acquire_static_reference_inventory(
        repository,
        source_kind="git-target",
        target=target,
    )
    index_inventory = acquire_static_reference_inventory(
        repository,
        source_kind="index",
    )
    worktree_inventory = acquire_static_reference_inventory(
        repository,
        source_kind="worktree",
    )

    assert target_inventory.source_kind == "git-target"
    assert target_inventory.target == target
    assert index_inventory.source_kind == "index"
    assert index_inventory.target is None
    assert worktree_inventory.source_kind == "worktree"
    assert worktree_inventory.target is None
    assert [item.path for item in target_inventory.candidates] == [
        "package.json"
    ]
    assert [item.content for item in target_inventory.candidates] == [committed]
    assert [item.content for item in index_inventory.candidates] == [indexed]
    assert [item.content for item in worktree_inventory.candidates] == [
        worktree
    ]
    assert target_inventory.candidates[0].source_object is not None
    assert index_inventory.candidates[0].source_object is not None
    assert worktree_inventory.candidates[0].source_object is None


def test_source_admitted_candidate_failure_is_typed(
    tmp_path: Path,
) -> None:
    """Report unavailable Git targets as typed acquisition failures."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)

    with pytest.raises(SourceAcquisitionError) as caught:
        acquire_static_reference_inventory(
            repository,
            source_kind="git-target",
            target="0" * 40,
        )

    assert caught.value.diagnostic_code == "git-target-unavailable"
    assert caught.value.path is None
    assert str(caught.value) == "static-reference source acquisition failed"


def test_git_target_duplicate_selected_path_is_typed_source_failure(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject ambiguous Git tree candidates before graph execution."""
    repository = tmp_path / "repository"
    repository.mkdir()
    (repository / ".git").mkdir()
    source = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    target = "a" * 40
    object_id = "b" * 40
    entry = f"100644 blob {object_id}\tpackage.json\0".encode()

    def fake_git(_root: Path, *arguments: str) -> bytes:
        if arguments == ("rev-parse", "--show-toplevel"):
            return os.fsencode(repository.resolve()) + b"\n"
        if arguments[:2] == ("rev-parse", "--verify"):
            return f"{target}\n".encode()
        if arguments[0] == "ls-tree":
            return entry * 2
        raise AssertionError(arguments)

    monkeypatch.setattr(source, "_git", fake_git)

    with pytest.raises(SourceAcquisitionError) as caught:
        acquire_static_reference_inventory(
            repository,
            source_kind="git-target",
            target=target,
        )

    assert caught.value.diagnostic_code == "duplicate-git-path"
    assert caught.value.path == "package.json"

    result = scan_bounded_static_references(
        repository,
        source_kind="git-target",
        target=target,
    )

    assert result.result == "error"
    assert result.error_kind == "source-acquisition-failed"
    assert result.implementation_identities == ()


def test_session_materializes_only_the_candidate_and_records_evidence(
    tmp_path: Path,
) -> None:
    """Materialize only the selected candidate and record exact evidence."""
    candidate = _candidate(content=b"\xef\xbb\xbf{}\r\n")
    session = StaticReferenceSession(parent=tmp_path)
    session_root = session.root

    invocation = session.materialize(
        candidate,
        source_kind="git-target",
        target=TARGET,
    )

    assert invocation.root.parent == session_root
    assert invocation.candidate_path is not None
    assert invocation.candidate_path.read_bytes() == candidate.content
    assert sorted(
        str(path.relative_to(invocation.snapshot_root))
        for path in invocation.snapshot_root.rglob("*")
        if path.is_file()
    ) == ["nested/package.json"]
    assert json.loads(invocation.manifest_path.read_bytes()) == {
        "authority-graph": "npm-manifest-v1",
        "byte-length": 7,
        "content-sha256": candidate.content_sha256,
        "input-mode": "strict-utf8-file",
        "logical-path": "nested/package.json",
        "source-kind": "git-target",
        "source-object": "blob-object",
        "target": TARGET,
        "utf8-bom": True,
    }

    session.release(invocation)
    assert not invocation.root.exists()
    session.close()
    assert not session_root.exists()


@pytest.mark.parametrize(
    ("snapshot_root", "logical_path"),
    [
        pytest.param(
            PureWindowsPath("C:/snapshot"),
            "D:/package.json",
            id="different-drive-escape",
        ),
        pytest.param(
            PureWindowsPath("D:/snapshot"),
            "D:/package.json",
            id="same-drive-component-collapse",
        ),
        pytest.param(
            PureWindowsPath("C:/snapshot"),
            "AUX/package.json",
            id="reserved-component",
        ),
    ],
)
def test_session_rejects_host_path_reinterpretation(
    snapshot_root: PureWindowsPath,
    logical_path: str,
) -> None:
    """Fail closed when a host path cannot preserve POSIX components."""
    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )

    with pytest.raises(OSError, match="not representable"):
        session_module._materialized_candidate_path(  # noqa: SLF001
            snapshot_root,
            logical_path,
        )


@pytest.mark.parametrize(
    ("repository_root", "logical_path"),
    [
        pytest.param(
            PureWindowsPath("C:/repository"),
            "D:/packages.lock.json",
            id="different-drive-escape",
        ),
        pytest.param(
            PureWindowsPath("D:/repository"),
            "D:/packages.lock.json",
            id="same-drive-component-collapse",
        ),
        pytest.param(
            PureWindowsPath("C:/repository"),
            "AUX/packages.lock.json",
            id="reserved-component",
        ),
    ],
)
def test_source_rejects_host_path_reinterpretation(
    repository_root: PureWindowsPath,
    logical_path: str,
) -> None:
    """Reject selected worktree paths before native filesystem access."""
    source_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )

    with pytest.raises(SourceAcquisitionError) as caught:
        source_module._native_worktree_path(  # noqa: SLF001
            repository_root,
            logical_path,
        )

    assert caught.value.diagnostic_code == "git-path-rejected"
    assert caught.value.path == logical_path


@pytest.mark.parametrize(
    "component",
    [
        pytest.param("trailing.", id="trailing-dot"),
        pytest.param("trailing ", id="trailing-space"),
        pytest.param("AUX", id="device-name"),
        pytest.param("COM1", id="numbered-device-name"),
        pytest.param("bad<name", id="invalid-character"),
        pytest.param("name:stream", id="alternate-stream"),
        pytest.param(r"literal\component", id="backslash-component"),
    ],
)
def test_native_path_mapping_uses_only_the_host_reservation_rules(
    component: str,
) -> None:
    """Apply official Windows reservations without restricting POSIX Git."""
    logical_path = f"{component}/package.json"

    with pytest.raises(ValueError, match="cannot be represented"):
        native_repository_path(
            PureWindowsPath("C:/snapshot"),
            logical_path,
            field="test path",
        )

    assert native_repository_path(
        PurePosixPath("/snapshot"),
        logical_path,
        field="test path",
    ) == PurePosixPath("/snapshot", component, "package.json")


@pytest.mark.skipif(
    os.name == "nt",
    reason="A Windows filesystem cannot preserve a backslash component",
)
@pytest.mark.parametrize("source_kind", ["git-target", "index", "worktree"])
def test_source_and_session_preserve_posix_backslash_components(
    tmp_path: Path,
    source_kind: str,
) -> None:
    """Preserve POSIX backslashes through acquisition and materialization."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    logical_path = r"literal\component/package.json"
    source_path = repository / r"literal\component" / "package.json"
    source_path.parent.mkdir()
    source_path.write_bytes(b'{"name":"ordinary"}\n')
    _git(repository, "add", "--", logical_path)
    _git(repository, "commit", "--quiet", "-m", "literal backslash")
    target = _git(repository, "rev-parse", "HEAD")

    inventory = acquire_static_reference_inventory(
        repository,
        source_kind=source_kind,
        target=target if source_kind == "git-target" else None,
    )

    assert tuple(candidate.path for candidate in inventory.candidates) == (
        logical_path,
    )
    with StaticReferenceSession(parent=tmp_path) as session:
        invocation = session.materialize(
            inventory.candidates[0],
            source_kind=source_kind,
            target=target if source_kind == "git-target" else None,
        )
        assert invocation.candidate_path is not None
        assert (
            invocation.candidate_path.read_bytes() == source_path.read_bytes()
        )
        assert invocation.candidate_path.parent.name == r"literal\component"


def test_result_round_trips_a_posix_backslash_component() -> None:
    """Preserve a literal POSIX component in canonical Result paths."""
    finding = _finding(path=r"literal\component/package.json")
    result = _result(findings=(finding,))

    assert (
        parse_bounded_static_reference_result(
            canonicalize(result.to_document())
        )
        == result
    )


def test_session_uses_only_the_controlled_environment(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Expose only the controlled authority execution environment."""
    monkeypatch.setenv("GIT_INDEX_FILE", "/ambient/index")
    monkeypatch.setenv("NPM_TOKEN", "secret")
    monkeypatch.setenv("NPM_CONFIG_REGISTRY", "https://example.invalid")
    session = StaticReferenceSession(parent=tmp_path)
    invocation = session.materialize(
        _candidate(),
        source_kind="worktree",
        target=None,
    )

    environment = dict(session.environment_for(invocation))

    assert environment["HOME"] == str(invocation.home)
    assert environment["USERPROFILE"] == str(invocation.home)
    assert environment["DOTNET_CLI_HOME"] == str(invocation.home)
    assert environment["TEMP"] == str(invocation.scratch)
    assert environment["TMP"] == str(invocation.scratch)
    assert environment["TMPDIR"] == str(invocation.scratch)
    assert environment["NPM_CONFIG_USERCONFIG"] == os.devnull
    assert environment["GIT_CONFIG_GLOBAL"] == os.devnull
    assert environment["GIT_CONFIG_NOSYSTEM"] == "1"
    assert environment["CI"] == "1"
    assert "GIT_INDEX_FILE" not in environment
    assert "NPM_TOKEN" not in environment
    assert "NPM_CONFIG_REGISTRY" not in environment

    session.close()


@pytest.mark.parametrize(
    "raise_inside", [False, True], ids=("success", "error")
)
def test_session_removes_the_exact_root_after_success_or_failure(
    tmp_path: Path,
    raise_inside: bool,  # noqa: FBT001
) -> None:
    """Remove the exact session root on successful and failing exits."""
    session_root: Path | None = None
    sentinel_message = "sentinel"

    with (  # noqa: SIM117
        pytest.raises(RuntimeError, match=sentinel_message)
        if raise_inside
        else (_does_not_raise())
    ):
        with StaticReferenceSession(parent=tmp_path) as session:
            session_root = session.root
            session.materialize(
                _candidate(),
                source_kind="index",
                target=None,
            )
            if raise_inside:
                raise RuntimeError(sentinel_message)

    assert session_root is not None
    assert not session_root.exists()
    assert list(tmp_path.iterdir()) == []


class _does_not_raise:  # noqa: N801
    """Minimal context manager used without adding a test dependency."""

    def __enter__(self) -> None:
        return None

    def __exit__(
        self,
        exc_type: object,
        exc_value: object,
        traceback: object,
    ) -> bool:
        del exc_type, exc_value, traceback
        return False


def test_session_surfaces_exact_path_cleanup_failure(
    tmp_path: Path,
) -> None:
    """Surface the exact path when session cleanup fails."""
    calls: list[Path] = []

    def fail_cleanup(path: Path) -> None:
        calls.append(path)
        message = "injected"
        raise OSError(message)

    session = StaticReferenceSession(parent=tmp_path, cleanup=fail_cleanup)
    session_root = session.root

    with pytest.raises(
        StaticReferenceCleanupError,
        match="Session cleanup failed",
    ):
        session.close()

    assert calls == [session_root]
    assert session_root.exists()
    shutil.rmtree(session_root)


@pytest.mark.parametrize(
    ("arguments", "diagnostic"),
    [
        ((), "the following arguments are required: --source-kind"),
        (("--source-kind", "unknown"), "invalid choice"),
        (
            ("--source-kind", "index", "--target", TARGET),
            "--target is accepted only for git-target",
        ),
    ],
    ids=("source-kind-omitted", "source-kind-unknown", "index-target"),
)
def test_invocation_rejects_invalid_arguments_before_result_or_root(
    arguments: tuple[str, ...],
    diagnostic: str,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject invalid arguments before scanning or creating output."""
    module = _load_static_reference_script()
    calls: list[object] = []

    def unexpected_scan(*args: object, **kwargs: object) -> object:
        calls.append((args, kwargs))
        message = "scan must not run before argument admission"
        raise AssertionError(message)

    monkeypatch.setattr(
        module, "scan_bounded_static_references", unexpected_scan
    )
    monkeypatch.setattr(sys, "argv", [str(STATIC_REFERENCE_SCRIPT), *arguments])

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert diagnostic in captured.err
    assert "bounded-static-reference-result" not in captured.err
    assert calls == []


def test_invocation_rejects_malformed_git_target_before_result_or_root(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject malformed Git targets before scanning or creating output."""
    module = _load_static_reference_script()
    calls: list[object] = []

    def unexpected_scan(*args: object, **kwargs: object) -> object:
        calls.append((args, kwargs))
        message = "scan must not run before target admission"
        raise AssertionError(message)

    monkeypatch.setattr(
        module, "scan_bounded_static_references", unexpected_scan
    )
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--source-kind",
            "git-target",
            "--target",
            "HEAD",
        ],
    )

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert "target must be a full lowercase commit SHA" in captured.err
    assert "bounded-static-reference-result" not in captured.err
    assert calls == []


@pytest.mark.parametrize(
    ("result", "expected_exit"),
    [
        (_result(), 0),
        (_result(findings=(_finding(),)), 1),
        (_result(error_kind="source-acquisition-failed"), 2),
    ],
    ids=("clean", "findings", "error"),
)
def test_invocation_emits_one_canonical_result_with_exact_exit_code(
    result: BoundedStaticReferenceResult,
    expected_exit: int,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Emit one canonical result and map it to the exact exit code."""
    module = _load_static_reference_script()
    calls: list[tuple[Path, str, str | None]] = []

    def scan(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None,
    ) -> BoundedStaticReferenceResult:
        calls.append((repository_root, source_kind, target))
        return result

    monkeypatch.setattr(module, "scan_bounded_static_references", scan)
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(REPO_ROOT),
            "--source-kind",
            "index",
        ],
    )

    exit_code = module.main()

    captured = capsys.readouterr()
    assert exit_code == expected_exit
    assert captured.out.encode() == canonicalize(result.to_document())
    assert captured.err == ""
    assert calls == [(REPO_ROOT, "index", None)]


def test_invocation_does_not_convert_parser_errors_into_results(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Keep parser failures outside bounded result conversion."""
    module = _load_static_reference_script()
    validated: list[BoundedStaticReferenceResult] = []
    monkeypatch.setattr(
        module,
        "validate_bounded_static_reference_result",
        validated.append,
    )
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--source-kind",
            "worktree",
            "--target",
            TARGET,
        ],
    )

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert "--target is accepted only for git-target" in captured.err
    assert validated == []


_PHASE2_NPM_IMPLEMENTATIONS = (
    "@npmcli/package-json@8.0.0",
    "node@24.19.0",
    "npm-package-arg@14.0.0",
)


def test_source_failure_result_rejects_loaded_implementation_identities() -> (
    None
):
    """Keep source failure strictly before authority implementation loading."""
    result = BoundedStaticReferenceResult(
        source_kind="index",
        target=None,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=("node@24.19.0",),
        findings=(),
        error_kind="source-acquisition-failed",
    )

    with pytest.raises(
        ValueError,
        match="cannot contain implementation identities",
    ):
        validate_bounded_static_reference_result(result)


_PHASE2_GRAPH_IMPLEMENTATIONS = {
    "npm-manifest-v1": _PHASE2_NPM_IMPLEMENTATIONS,
    "pnpm-lock-v1": (
        "@pnpm/deps.path@1101.0.1",
        "@pnpm/lockfile.fs@1100.2.5",
        "@pnpm/lockfile.utils@1102.1.0",
        "@pnpm/resolving.npm-resolver@1104.1.0",
        "@pnpm/workspace.spec-parser@1100.0.1",
        "node@24.19.0",
    ),
    "pnpm-workspace-v1": (
        "@pnpm/resolving.npm-resolver@1104.1.0",
        "@pnpm/workspace.spec-parser@1100.0.1",
        "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
        "node@24.19.0",
        "npm-package-arg@14.0.0",
    ),
    "nuget-lock-v1": (
        "NuGet.Packaging@7.9.0",
        "NuGet.ProjectModel@7.9.0",
        "dotnet-runtime@10.0.8",
    ),
}
_PHASE2_LIVE_IMPLEMENTATIONS = tuple(
    sorted(
        {
            *_PHASE2_GRAPH_IMPLEMENTATIONS["npm-manifest-v1"],
            *_PHASE2_GRAPH_IMPLEMENTATIONS["pnpm-lock-v1"],
            *_PHASE2_GRAPH_IMPLEMENTATIONS["pnpm-workspace-v1"],
        },
        key=utf8_sort_key,
    )
)
_PHASE2_PRODUCER_ROOT = "src/public/lib/hcoona-release-smoke-npm"
_PHASE2_EXPECTED_AUTHORITY_MANIFEST = {
    "schema": "workflow-delivery/v3/static-reference-authority-manifest",
    "dependency-closures": [
        {
            "kind": "pnpm-lock",
            "path": "pnpm-lock.yaml",
            "sha256": (
                "sha256:"
                "44ea8ea08134a04f079e89747de2f4b6219ff7dbc23365d66c9656e087a224ba"
            ),
        },
        {
            "kind": "nuget-lock",
            "path": (
                "src/private/app/workflow-delivery-v3-nuget-authority/"
                "packages.lock.json"
            ),
            "sha256": (
                "sha256:"
                "2fcd4e94b3b3be83522776536c4cae3f22aaa4bcbfe747a522627654c020cc5a"
            ),
        },
    ],
    "execution": {
        "preparation-command": [
            "mise",
            "run",
            "prepare:static-reference-authorities",
        ],
        "node-command": [
            "node",
            "eng/scripts/workflow_delivery_v3_static_reference_node.mjs",
        ],
        "nuget-command": [
            "dotnet",
            (
                "artifacts/workflow-delivery-v3/static-reference/"
                "nuget-authority/WorkflowDeliveryV3NuGetAuthority.dll"
            ),
        ],
        "preparation-stamp": (
            "artifacts/workflow-delivery-v3/static-reference/"
            "authority-preparation.json"
        ),
        "timeout-seconds": 30,
    },
    "graphs": [
        {
            "id": "npm-manifest-v1",
            "artifact": "package.json",
            "input-mode": "strict-utf8-file",
            "snapshot-inputs": ["package.json"],
            "implementations": [
                "@npmcli/package-json@8.0.0",
                "node@24.19.0",
                "npm-package-arg@14.0.0",
            ],
            "apis": [
                "PackageJson.load(snapshotDirectory)",
                "npa.resolve(name,spec,snapshotDirectory)",
            ],
            "fact-kinds": ["npm-package-name", "npm-reference"],
        },
        {
            "id": "pnpm-lock-v1",
            "artifact": "pnpm-lock.yaml@9.0",
            "input-mode": "strict-utf8-file",
            "snapshot-inputs": ["pnpm-lock.yaml"],
            "implementations": [
                "@pnpm/deps.path@1101.0.1",
                "@pnpm/lockfile.fs@1100.2.5",
                "@pnpm/lockfile.utils@1102.1.0",
                "@pnpm/resolving.npm-resolver@1104.1.0",
                "@pnpm/workspace.spec-parser@1100.0.1",
                "node@24.19.0",
            ],
            "apis": [
                "extractMainDocument",
                "readWantedLockfileWithMergeInfo",
                "WorkspaceSpec.parse",
                "workspacePrefToNpm",
                "parseBareSpecifier",
                "refToRelative",
                "nameVerFromPkgSnapshot",
                "pkgSnapshotToResolution",
            ],
            "fact-kinds": [
                "pnpm-lock-snapshot",
                "pnpm-lock-importer-reference",
            ],
        },
        {
            "id": "pnpm-workspace-v1",
            "artifact": "pnpm-workspace.yaml",
            "input-mode": "strict-utf8-file",
            "snapshot-inputs": ["pnpm-workspace.yaml"],
            "implementations": [
                "@pnpm/resolving.npm-resolver@1104.1.0",
                "@pnpm/workspace.spec-parser@1100.0.1",
                "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
                "node@24.19.0",
                "npm-package-arg@14.0.0",
            ],
            "apis": [
                "readWorkspaceManifest(snapshotDirectory)",
                "WorkspaceSpec.parse",
                "workspacePrefToNpm",
                "parseBareSpecifier",
                "npa.resolve(name,spec,snapshotDirectory)",
            ],
            "fact-kinds": [
                "pnpm-workspace-pattern",
                "pnpm-workspace-reference",
            ],
        },
        {
            "id": "nuget-lock-v1",
            "artifacts": [
                "packages.lock.json@1-3",
                "packages.config",
            ],
            "input-modes": [
                {
                    "artifact": "packages.lock.json",
                    "mode": "strict-utf8-byte-stream",
                },
                {
                    "artifact": "packages.config",
                    "mode": "xml-byte-stream",
                },
            ],
            "implementations": [
                "NuGet.Packaging@7.9.0",
                "NuGet.ProjectModel@7.9.0",
                "dotnet-runtime@10.0.8",
            ],
            "apis": [
                (
                    "PackagesLockFileFormat.Read("
                    "Stream,NullLogger.Instance,repositoryLogicalPath)"
                ),
                "PackagesConfigReader(Stream,false).GetPackages(false)",
            ],
            "fact-kinds": [
                "nuget-lock-dependency",
                "nuget-packages-config-entry",
            ],
        },
    ],
    "runtimes": [
        {
            "tool": "dotnet",
            "backend": "core:dotnet",
            "sdk-version": "10.0.300",
            "loaded-runtime": "dotnet-runtime@10.0.8",
        },
        {
            "tool": "node",
            "backend": "core:node",
            "version": "24.19.0",
            "loaded-runtime": "node@24.19.0",
            "artifact-checksums": {
                "linux-arm64": (
                    "sha256:"
                    "d28c8a5bf0a808f0ed434a1dce8c54ae98f0371c0bd86ac58abc613f73e6643f"
                ),
                "linux-arm64-musl": (
                    "sha256:"
                    "20824e4d35948fae5b337dccef47813b04d8995312f59df7386f2256d9f9ab7e"
                ),
                "linux-x64": (
                    "sha256:"
                    "f625d97cd707df4ff96254916fbc5ff014f09c09effe5a1e0ca8f6d41a8789d4"
                ),
                "linux-x64-musl": (
                    "sha256:"
                    "c60223786df14a5d23e220ebb8e60318f5322640a62f90e6d9e54d3a18da532e"
                ),
                "macos-arm64": (
                    "sha256:"
                    "8294b7aa9b03997481c06babf1e8b270c859358f27da57a11509afe537ac381d"
                ),
                "macos-x64": (
                    "sha256:"
                    "d1b5e999db158c62fe8f7267a4476b035d8bd93b1a605bac24a3f0dd166e3316"
                ),
                "windows-x64": (
                    "sha256:"
                    "57f71ab3652e797d84acddc79c81cc9ff1c6ddb2a1974cdb83f00fee9bff4c73"
                ),
            },
        },
        {
            "tool": "pnpm",
            "backend": "aqua:pnpm/pnpm",
            "version": "11.22.0",
            "provenance": "github-attestations",
            "artifact-checksums": {
                "linux-arm64": (
                    "sha256:"
                    "f1426231f365bdfd46c15fa3d1211c3936ee2c4e557afd304f6c66dbf1b2a8bf"
                ),
                "linux-arm64-musl": (
                    "sha256:"
                    "6e53557024be48e59ab8760f9117c0e5c0e0a37ab420f71f302d86216970d28f"
                ),
                "linux-x64": (
                    "sha256:"
                    "4c592fa410eb23b69691a9efb9bf21c87c15b3e9d88c6ec8acdd354a0eb8de71"
                ),
                "linux-x64-musl": (
                    "sha256:"
                    "45425b06e747cbcaff4940d7b4a55e694645f15f9339dbf7f2601cfb21400545"
                ),
                "macos-arm64": (
                    "sha256:"
                    "2000dcc8f0718852c2806ba4dca1edaedf18a4a39264474d5a1c8fcee250adfd"
                ),
                "windows-x64": (
                    "sha256:"
                    "1de83ad5100acfd2adb5c8bc6f8a428cee9ff4e365deff57c22bfc0cccaa4ddb"
                ),
            },
        },
    ],
}


def _phase2_candidate(
    path: str,
    *,
    family: str,
    graph_id: str,
    content: bytes = b'{"name":"phase-2"}\n',
) -> StaticReferenceCandidate:
    input_mode = {
        "npm-manifest": "strict-utf8-file",
        "pnpm-lock": "strict-utf8-file",
        "pnpm-workspace": "strict-utf8-file",
        "nuget-lock": "strict-utf8-byte-stream",
        "nuget-packages-config": "xml-byte-stream",
    }[family]
    return StaticReferenceCandidate(
        path=path,
        selection=StaticReferenceSelection(
            family=family,
            graph_id=graph_id,
            input_mode=input_mode,
        ),
        content=content,
        source_object=f"phase-2:{path}",
    )


def _phase2_npa_reference(
    name: str,
    reference_type: str,
    *,
    local_path: str | None = None,
    alias_target: object = None,
) -> dict[str, object]:
    return {
        "aliasTarget": alias_target,
        "fetchSpec": "1.2.3",
        "localPath": local_path,
        "name": name,
        "rawSpec": "1.2.3",
        "saveSpec": "1.2.3",
        "type": reference_type,
    }


def test_projection_maps_normalized_facts_to_d_v_a_w_l_and_dependency_key_findings() -> (  # noqa: E501
    None
):
    """Map normalized facts to every prohibited-reference finding form."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    npm_candidate = _phase2_candidate(
        "z/consumer/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    npm_facts = (
        {
            "kind": "npm-package-name",
            "context": "name",
            "name": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "kind": "npm-reference",
            "dependencyKey": "@hcoona/hcoona-release-smoke-npm",
            "section": "dependencies",
            "sourceSpec": "1.2.3",
            "reference": _phase2_npa_reference(
                "@hcoona/hcoona-release-smoke-npm",
                "version",
            ),
        },
        {
            "kind": "npm-reference",
            "dependencyKey": "alias",
            "section": "optionalDependencies",
            "sourceSpec": "npm:@hcoona/hcoona-release-smoke-npm@1.2.3",
            "reference": _phase2_npa_reference(
                "alias",
                "alias",
                alias_target=_phase2_npa_reference(
                    "@hcoona/hcoona-release-smoke-npm",
                    "version",
                ),
            ),
        },
        {
            "kind": "npm-reference",
            "dependencyKey": "local",
            "section": "devDependencies",
            "sourceSpec": "file:../../hcoona-release-smoke-npm",
            "reference": _phase2_npa_reference(
                "local-package",
                "directory",
                local_path=_PHASE2_PRODUCER_ROOT,
            ),
        },
    )
    workspace_candidate = _phase2_candidate(
        "a/pnpm-workspace.yaml",
        family="pnpm-workspace",
        graph_id="pnpm-workspace-v1",
        content=b"packages: []\n",
    )
    workspace_facts = (
        {
            "catalogKind": "default",
            "catalogName": None,
            "dependencyKey": "workspaceAlias",
            "kind": "pnpm-workspace-reference",
            "reference": {
                "kind": "workspace",
                "workspace": {
                    "fetchSpec": "*",
                    "name": "@hcoona/hcoona-release-smoke-npm",
                    "selector": "*",
                    "type": "range",
                },
            },
            "sourceSpec": "workspace:*",
        },
    )

    npm_findings = projection.project_static_reference_facts(
        npm_candidate,
        npm_facts,
    )
    workspace_findings = projection.project_static_reference_facts(
        workspace_candidate,
        workspace_facts,
    )

    assert [finding.to_document() for finding in npm_findings] == [
        {
            "path": "z/consumer/package.json",
            "family": "npm-manifest",
            "semantic-context": (
                "dependencies.@hcoona/hcoona-release-smoke-npm"
            ),
            "prohibited-form": "V",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "z/consumer/package.json",
            "family": "npm-manifest",
            "semantic-context": (
                "dependencies.@hcoona/hcoona-release-smoke-npm"
            ),
            "prohibited-form": "dependency-key",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "z/consumer/package.json",
            "family": "npm-manifest",
            "semantic-context": "devDependencies.local",
            "prohibited-form": "L",
            "matched-identity": _PHASE2_PRODUCER_ROOT,
        },
        {
            "path": "z/consumer/package.json",
            "family": "npm-manifest",
            "semantic-context": "name",
            "prohibited-form": "D",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "z/consumer/package.json",
            "family": "npm-manifest",
            "semantic-context": "optionalDependencies.alias",
            "prohibited-form": "A",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
    ]
    assert [finding.to_document() for finding in workspace_findings] == [
        {
            "path": "a/pnpm-workspace.yaml",
            "family": "pnpm-workspace",
            "semantic-context": "catalog.workspaceAlias",
            "prohibited-form": "W",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        }
    ]
    assert {
        finding.prohibited_form
        for finding in (*npm_findings, *workspace_findings)
    } == {"A", "D", "L", "V", "W", "dependency-key"}
    assert npm_candidate.content == b'{"name":"phase-2"}\n'
    assert workspace_candidate.content == b"packages: []\n"


def test_projection_accepts_official_empty_specifier_values() -> None:
    """Preserve empty exact values admitted by the selected authorities."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    npm_candidate = _phase2_candidate(
        "consumer/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    empty_npm_reference = {
        "aliasTarget": None,
        "fetchSpec": "",
        "localPath": None,
        "name": "ordinary",
        "rawSpec": "",
        "saveSpec": None,
        "type": "range",
    }
    npm_fact = {
        "dependencyKey": "ordinary",
        "kind": "npm-reference",
        "reference": empty_npm_reference,
        "section": "dependencies",
        "sourceSpec": "",
    }
    workspace_candidate = _phase2_candidate(
        "consumer/pnpm-workspace.yaml",
        family="pnpm-workspace",
        graph_id="pnpm-workspace-v1",
    )
    workspace_facts = (
        {
            "catalogKind": "default",
            "catalogName": None,
            "dependencyKey": "ordinary",
            "kind": "pnpm-workspace-reference",
            "reference": {"kind": "npm", "npm": empty_npm_reference},
            "sourceSpec": "",
        },
        {
            "catalogKind": "named",
            "catalogName": "workspace",
            "dependencyKey": "ordinary",
            "kind": "pnpm-workspace-reference",
            "reference": {
                "kind": "workspace",
                "workspace": {
                    "fetchSpec": "*",
                    "name": "ordinary",
                    "selector": "",
                    "type": "range",
                },
            },
            "sourceSpec": "workspace:",
        },
        {
            "catalogKind": "named",
            "catalogName": "",
            "dependencyKey": "ordinary",
            "kind": "pnpm-workspace-reference",
            "reference": {"kind": "npm", "npm": empty_npm_reference},
            "sourceSpec": "",
        },
    )
    lock_candidate = _phase2_candidate(
        "consumer/pnpm-lock.yaml",
        family="pnpm-lock",
        graph_id="pnpm-lock-v1",
    )
    lock_fact = {
        "dependencyKey": "ordinary",
        "importerId": ".",
        "kind": "pnpm-lock-importer-reference",
        "rawSpecifier": "workspace:",
        "registrySpec": {
            "fetchSpec": "*",
            "name": "ordinary",
            "type": "range",
        },
        "resolvedReference": "link:../ordinary",
        "section": "dependencies",
        "snapshotKey": None,
        "workspaceSelector": "",
    }
    producer_snapshot_fact = {
        "dependencies": [
            {
                "dependencyKey": "",
                "reference": "",
                "section": "dependencies",
            }
        ],
        "dependencyPath": ("@hcoona/hcoona-release-smoke-npm@1.0.0"),
        "kind": "pnpm-lock-snapshot",
        "name": "@hcoona/hcoona-release-smoke-npm",
        "nonSemverVersion": None,
        "registryName": "@hcoona/hcoona-release-smoke-npm",
        "resolution": {"kind": "registry"},
        "version": "1.0.0",
    }
    producer_dependency_key_fact = {
        "dependencies": [
            {
                "dependencyKey": "@hcoona/hcoona-release-smoke-npm",
                "reference": "",
                "section": "optionalDependencies",
            }
        ],
        "dependencyPath": "ordinary@1.0.0",
        "kind": "pnpm-lock-snapshot",
        "name": "ordinary",
        "nonSemverVersion": None,
        "registryName": "ordinary",
        "resolution": {"kind": "registry"},
        "version": "1.0.0",
    }

    assert (
        projection.project_static_reference_facts(
            npm_candidate,
            (npm_fact,),
        )
        == ()
    )
    assert (
        projection.project_static_reference_facts(
            workspace_candidate,
            workspace_facts,
        )
        == ()
    )
    assert (
        projection.project_static_reference_facts(
            lock_candidate,
            (lock_fact,),
        )
        == ()
    )
    assert [
        (
            finding.prohibited_form,
            finding.matched_identity,
            finding.context,
        )
        for finding in projection.project_static_reference_facts(
            lock_candidate,
            (
                producer_snapshot_fact,
                producer_dependency_key_fact,
            ),
        )
    ] == [
        (
            "V",
            "@hcoona/hcoona-release-smoke-npm",
            "packages.@hcoona/hcoona-release-smoke-npm@1.0.0",
        ),
        (
            "dependency-key",
            "@hcoona/hcoona-release-smoke-npm",
            (
                "packages.ordinary@1.0.0.optionalDependencies."
                "@hcoona/hcoona-release-smoke-npm"
            ),
        ),
    ]

    invalid_identity = dict(npm_fact)
    invalid_identity["dependencyKey"] = ""
    with pytest.raises(
        projection.StaticReferenceProjectionError,
        match="nonempty",
    ):
        projection.project_static_reference_facts(
            npm_candidate,
            (invalid_identity,),
        )


def test_projection_orders_candidates_nodes_arrays_mappings_facts_and_findings() -> (  # noqa: E501
    None
):
    """Order candidates, normalized structures, facts, and findings."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    candidate = _phase2_candidate(
        "ordering/pnpm-lock.yaml",
        family="pnpm-lock",
        graph_id="pnpm-lock-v1",
        content=b"lockfileVersion: '9.0'\n",
    )
    snapshot_key = "/@hcoona/hcoona-release-smoke-npm@1.0.0"
    snapshot = {
        "kind": "pnpm-lock-snapshot",
        "dependencyPath": snapshot_key,
        "name": "@hcoona/hcoona-release-smoke-npm",
        "version": "1.0.0",
        "nonSemverVersion": None,
        "registryName": "@hcoona/hcoona-release-smoke-npm",
        "resolution": {"kind": "registry"},
        "dependencies": [
            {
                "dependencyKey": "@hcoona/hcoona-release-smoke-npm",
                "reference": "1.0.0",
                "section": "dependencies",
            },
            {
                "dependencyKey": "@hcoona/hcoona-release-smoke-npm",
                "reference": "1.0.0",
                "section": "optionalDependencies",
            },
        ],
    }
    importer = {
        "kind": "pnpm-lock-importer-reference",
        "dependencyKey": "z-alias",
        "importerId": "apps/z",
        "rawSpecifier": "1.0.0",
        "registrySpec": {
            "fetchSpec": "1.0.0",
            "name": "@hcoona/hcoona-release-smoke-npm",
            "type": "version",
        },
        "resolvedReference": "1.0.0",
        "section": "dependencies",
        "snapshotKey": snapshot_key,
        "workspaceSelector": None,
    }
    scrambled_snapshot = dict(reversed(tuple(snapshot.items())))
    scrambled_snapshot["dependencies"] = list(
        reversed(snapshot["dependencies"])
    )
    scrambled_importer = dict(reversed(tuple(importer.items())))

    ordered_findings = projection.project_static_reference_facts(
        candidate,
        (snapshot, importer),
    )
    scrambled_findings = projection.project_static_reference_facts(
        candidate,
        (scrambled_importer, scrambled_snapshot, scrambled_importer),
    )

    assert scrambled_findings == ordered_findings
    assert [finding.to_document() for finding in scrambled_findings] == [
        {
            "path": "ordering/pnpm-lock.yaml",
            "family": "pnpm-lock",
            "semantic-context": "importers.apps/z.dependencies.z-alias",
            "prohibited-form": "A",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "ordering/pnpm-lock.yaml",
            "family": "pnpm-lock",
            "semantic-context": (
                "packages./@hcoona/hcoona-release-smoke-npm@1.0.0"
            ),
            "prohibited-form": "V",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "ordering/pnpm-lock.yaml",
            "family": "pnpm-lock",
            "semantic-context": (
                "packages./@hcoona/hcoona-release-smoke-npm@1.0.0."
                "dependencies.@hcoona/hcoona-release-smoke-npm"
            ),
            "prohibited-form": "dependency-key",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "path": "ordering/pnpm-lock.yaml",
            "family": "pnpm-lock",
            "semantic-context": (
                "packages./@hcoona/hcoona-release-smoke-npm@1.0.0."
                "optionalDependencies.@hcoona/hcoona-release-smoke-npm"
            ),
            "prohibited-form": "dependency-key",
            "matched-identity": "@hcoona/hcoona-release-smoke-npm",
        },
    ]
    assert [edge["section"] for edge in snapshot["dependencies"]] == [
        "dependencies",
        "optionalDependencies",
    ]
    assert len(scrambled_findings) == 4  # noqa: PLR2004


def test_projection_rejects_invalid_fact_shape_without_partial_findings(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject malformed authority facts without partial findings."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidate = _phase2_candidate(
        "consumer/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    facts = (
        {
            "kind": "npm-package-name",
            "context": "name",
            "name": "@hcoona/hcoona-release-smoke-npm",
        },
        {
            "kind": "npm-reference",
            "dependencyKey": "partial",
        },
    )

    with pytest.raises(
        projection.StaticReferenceProjectionError,
        match="npm reference fact fields are not exact",
    ):
        projection.project_static_reference_facts(candidate, facts)

    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=(candidate,),
    )
    acquisition_calls: list[tuple[Path, str, str | None]] = []

    def acquire(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None = None,
    ) -> object:
        acquisition_calls.append((repository_root, source_kind, target))
        return inventory

    authority_calls: list[str] = []

    def run_authority(
        repository_root: Path,
        selected_candidate: StaticReferenceCandidate,
        invocation: object,
        session: StaticReferenceSession,
    ) -> object:
        del repository_root, invocation, session
        authority_calls.append(selected_candidate.path)
        return authority.AuthorityGraphOutcome(
            graph_id="npm-manifest-v1",
            implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
            facts=facts,
        )

    session_roots: list[Path] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        session_roots.append(session.root)
        return session

    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        acquire,
    )

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        authority_runner=run_authority,
        session_factory=session_factory,
    )

    assert result.error_kind == "unsupported-projection"
    assert result.findings == ()
    assert result.implementation_identities == _PHASE2_NPM_IMPLEMENTATIONS
    assert result.result == "error"
    assert acquisition_calls == [(tmp_path, "index", None)]
    assert authority_calls == ["consumer/package.json"]
    assert len(session_roots) == 1
    assert not session_roots[0].exists()
    assert list(tmp_path.iterdir()) == []


@pytest.mark.parametrize(
    (
        "family",
        "graph_id",
        "logical_path",
        "runtime",
        "request_schema",
        "response_schema",
    ),
    [
        (
            "npm-manifest",
            "npm-manifest-v1",
            "a/package.json",
            "node",
            "workflow-delivery/v3/static-reference-node-authority-request",
            "workflow-delivery/v3/static-reference-node-authority-response",
        ),
        (
            "pnpm-lock",
            "pnpm-lock-v1",
            "b/pnpm-lock.yaml",
            "node",
            "workflow-delivery/v3/static-reference-node-authority-request",
            "workflow-delivery/v3/static-reference-node-authority-response",
        ),
        (
            "pnpm-workspace",
            "pnpm-workspace-v1",
            "c/pnpm-workspace.yaml",
            "node",
            "workflow-delivery/v3/static-reference-node-authority-request",
            "workflow-delivery/v3/static-reference-node-authority-response",
        ),
        (
            "nuget-lock",
            "nuget-lock-v1",
            "d/packages.lock.json",
            "dotnet",
            "workflow-delivery/v3/static-reference-nuget-authority-request",
            "workflow-delivery/v3/static-reference-nuget-authority-response",
        ),
    ],
    ids=("npm-manifest", "pnpm-lock", "pnpm-workspace", "nuget-lock"),
)
def test_authority_dispatches_each_graph_to_its_exact_protocol(  # noqa: PLR0913, PLR0917
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    family: str,
    graph_id: str,
    logical_path: str,
    runtime: str,
    request_schema: str,
    response_schema: str,
) -> None:
    """Dispatch every selected graph through its exact authority protocol."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    repository_root = tmp_path / "repository"
    repository_root.mkdir()
    relative_authority = (
        Path("eng/scripts/workflow_delivery_v3_static_reference_node.mjs")
        if runtime == "node"
        else Path(
            "artifacts/workflow-delivery-v3/static-reference/"
            "nuget-authority/WorkflowDeliveryV3NuGetAuthority.dll"
        )
    )
    prepared_authority = repository_root / relative_authority
    prepared_authority.parent.mkdir(parents=True)
    prepared_authority.write_bytes(b"phase-2 prepared authority")
    candidate = _phase2_candidate(
        logical_path,
        family=family,
        graph_id=graph_id,
        content=b"phase-2-\0",
    )
    session = StaticReferenceSession(parent=tmp_path)
    session_root = session.root
    invocation = session.materialize(
        candidate,
        source_kind="index",
        target=None,
    )
    invocation_root = invocation.root
    expected_path = session.environment_for(invocation).get("PATH")
    which_calls: list[tuple[str, str | None]] = []

    def find_executable(
        command: str,
        *,
        path: str | None = None,
    ) -> str:
        which_calls.append((command, path))
        return f"/controlled/{command}"

    process_calls: list[tuple[object, object, object, object]] = []

    def run_process(
        command: tuple[str, ...],
        *,
        request: object,
        invocation: object,
        session: object,
    ) -> bytes:
        process_calls.append((command, request, invocation, session))
        return canonicalize(
            {
                "schema": response_schema,
                "result": "facts",
                "graph": graph_id,
                "implementationIdentities": [],
                "facts": [],
            }
        )

    monkeypatch.setattr(authority.shutil, "which", find_executable)
    monkeypatch.setattr(authority, "_run_process", run_process)

    try:
        outcome = authority.run_authority_graph(
            repository_root,
            candidate,
            invocation,
            session,
        )
    finally:
        session.release(invocation)
        session.close()

    expected_request = (
        {
            "schema": request_schema,
            "graph": graph_id,
            "snapshotRoot": str(invocation.snapshot_root),
            "candidatePath": str(invocation.candidate_path),
            "logicalPath": logical_path,
        }
        if runtime == "node"
        else {
            "schema": request_schema,
            "family": family,
            "logicalPath": logical_path,
            "contentBase64": "cGhhc2UtMi0A",
        }
    )
    assert outcome == authority.AuthorityGraphOutcome(
        graph_id=graph_id,
        implementation_identities=(),
        facts=(),
    )
    assert which_calls == [(runtime, expected_path)]
    assert len(process_calls) == 1
    command, request, seen_invocation, seen_session = process_calls[0]
    assert command == (
        f"/controlled/{runtime}",
        str(prepared_authority),
    )
    assert request == expected_request
    assert seen_invocation is invocation
    assert seen_session is session
    assert not invocation_root.exists()
    assert not session_root.exists()


@pytest.mark.parametrize(
    ("response_case", "expected_message"),
    [
        ("missing", "authority response fields are not exact"),
        ("extra", "authority response fields are not exact"),
        ("malformed-field", "implementation identities must be an array"),
        ("partial-fact", "authority fact must be an object"),
        ("wrong-schema", "authority response identity is invalid"),
        ("wrong-graph", "authority response identity is invalid"),
        ("duplicate-field", "authority response is not valid UTF-8 JSON"),
        ("invalid-result", "authority response result is invalid"),
        ("invalid-error-kind", "authority response error kind is invalid"),
        ("unsorted-identities", "authority response is not canonical"),
        ("duplicate-identities", "authority response is not canonical"),
    ],
    ids=(
        "missing",
        "extra",
        "malformed-field",
        "partial-fact",
        "wrong-schema",
        "wrong-graph",
        "duplicate-field",
        "invalid-result",
        "invalid-error-kind",
        "unsorted-identities",
        "duplicate-identities",
    ),
)
def test_authority_rejects_missing_extra_or_malformed_response_fields_without_partial_facts(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    response_case: str,
    expected_message: str,
) -> None:
    """Reject malformed authority responses without partial facts."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    repository_root = tmp_path / "repository"
    script = (
        repository_root
        / "eng/scripts/workflow_delivery_v3_static_reference_node.mjs"
    )
    script.parent.mkdir(parents=True)
    script.write_bytes(b"phase-2 prepared authority")
    candidate = _phase2_candidate(
        "response/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    session = StaticReferenceSession(parent=tmp_path)
    session_root = session.root
    invocation = session.materialize(
        candidate,
        source_kind="worktree",
        target=None,
    )
    invocation_root = invocation.root
    base_response = {
        "schema": (
            "workflow-delivery/v3/static-reference-node-authority-response"
        ),
        "result": "facts",
        "graph": "npm-manifest-v1",
        "implementationIdentities": [],
        "facts": [],
    }
    response_documents = {
        "missing": {
            key: value for key, value in base_response.items() if key != "facts"
        },
        "extra": {**base_response, "unexpected": "phase-2-secret-extra"},
        "malformed-field": {
            **base_response,
            "implementationIdentities": {},
        },
        "partial-fact": {
            **base_response,
            "facts": [{"kind": "accepted-prefix"}, []],
        },
        "wrong-schema": {
            **base_response,
            "schema": "workflow-delivery/v3/not-the-node-response",
        },
        "wrong-graph": {
            **base_response,
            "graph": "pnpm-lock-v1",
        },
        "invalid-result": {
            **base_response,
            "result": "partial",
        },
        "invalid-error-kind": {
            "schema": (
                "workflow-delivery/v3/static-reference-node-authority-response"
            ),
            "result": "error",
            "graph": "npm-manifest-v1",
            "implementationIdentities": [],
            "errorKind": "authority-execution-failed",
        },
        "unsorted-identities": {
            **base_response,
            "implementationIdentities": ["z@1", "a@1"],
        },
        "duplicate-identities": {
            **base_response,
            "implementationIdentities": ["a@1", "a@1"],
        },
    }
    process_calls: list[tuple[str, ...]] = []

    def run_process(
        command: tuple[str, ...],
        **kwargs: object,
    ) -> bytes:
        del kwargs
        process_calls.append(command)
        if response_case == "duplicate-field":
            return (
                b'{"schema":"workflow-delivery/v3/'
                b'static-reference-node-authority-response",'
                b'"result":"facts","result":"error",'
                b'"graph":"npm-manifest-v1",'
                b'"implementationIdentities":[],"facts":[]}'
            )
        return canonicalize(response_documents[response_case])

    monkeypatch.setattr(
        authority.shutil,
        "which",
        lambda command, *, path=None: f"/controlled/{command}",  # noqa: ARG005
    )
    monkeypatch.setattr(authority, "_run_process", run_process)
    outcomes: list[object] = []

    try:
        with pytest.raises(
            authority.AuthorityExecutionError,
            match=expected_message,
        ):
            outcomes.append(
                authority.run_authority_graph(
                    repository_root,
                    candidate,
                    invocation,
                    session,
                )
            )
    finally:
        session.release(invocation)
        session.close()

    assert process_calls == [
        ("/controlled/node", str(script)),
    ]
    assert outcomes == []
    assert not invocation_root.exists()
    assert not session_root.exists()


@pytest.mark.parametrize(
    ("failure_kind", "expected_message"),
    [
        ("spawn", "static-reference authority process failed"),
        ("exit-stderr", "static-reference authority process failed"),
        ("signal-stderr", "static-reference authority process failed"),
        ("json", "authority response is not valid UTF-8 JSON"),
    ],
    ids=("spawn", "exit-stderr", "signal-stderr", "json"),
)
def test_authority_sanitizes_spawn_exit_signal_stderr_and_json_failures(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    failure_kind: str,
    expected_message: str,
) -> None:
    """Sanitize process and response failures from authority execution."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    repository_root = tmp_path / "repository"
    script = (
        repository_root
        / "eng/scripts/workflow_delivery_v3_static_reference_node.mjs"
    )
    script.parent.mkdir(parents=True)
    script.write_bytes(b"phase-2 prepared authority")
    candidate = _phase2_candidate(
        "sanitized/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    session = StaticReferenceSession(parent=tmp_path)
    session_root = session.root
    invocation = session.materialize(
        candidate,
        source_kind="git-target",
        target=TARGET,
    )
    invocation_root = invocation.root
    expected_environment = session.environment_for(invocation)
    process_calls: list[tuple[tuple[str, ...], dict[str, object]]] = []

    def run_process(
        command: tuple[str, ...],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        process_calls.append((command, kwargs))
        if failure_kind == "spawn":
            message = "phase-2-secret-spawn-path"
            raise OSError(message)
        if failure_kind == "exit-stderr":
            raise subprocess.CalledProcessError(
                23,
                command,
                output=b"phase-2-secret-partial-output",
                stderr=b"phase-2-secret-stderr-token",
            )
        if failure_kind == "signal-stderr":
            raise subprocess.CalledProcessError(
                -9,
                command,
                output=b"",
                stderr=b"phase-2-secret-signal-detail",
            )
        return subprocess.CompletedProcess(
            command,
            0,
            stdout=b'{"schema":',
            stderr=b"phase-2-secret-json-stderr",
        )

    monkeypatch.setattr(
        authority.shutil,
        "which",
        lambda command, *, path=None: f"/controlled/{command}",  # noqa: ARG005
    )
    monkeypatch.setattr(authority.subprocess, "run", run_process)

    try:
        with pytest.raises(authority.AuthorityExecutionError) as caught:
            authority.run_authority_graph(
                repository_root,
                candidate,
                invocation,
                session,
            )
    finally:
        session.release(invocation)
        session.close()

    assert str(caught.value) == expected_message
    assert caught.value.args == (expected_message,)
    assert "phase-2-secret" not in str(caught.value)
    assert len(process_calls) == 1
    command, kwargs = process_calls[0]
    assert command == ("/controlled/node", str(script))
    assert kwargs["cwd"] == invocation.root
    assert kwargs["check"] is True
    assert kwargs["capture_output"] is True
    assert kwargs["env"] == expected_environment
    assert kwargs["input"] == canonicalize(
        {
            "schema": (
                "workflow-delivery/v3/static-reference-node-authority-request"
            ),
            "graph": "npm-manifest-v1",
            "snapshotRoot": str(invocation.snapshot_root),
            "candidatePath": str(invocation.candidate_path),
            "logicalPath": "sanitized/package.json",
        }
    )
    assert kwargs["timeout"] == 30  # noqa: PLR2004
    assert not invocation_root.exists()
    assert not session_root.exists()


def test_policy_authority_manifest_and_digest_are_exact() -> None:
    """Keep the authority manifest and derived policy digest exact."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    manifest = policy.static_reference_authority_manifest()
    document = policy.static_reference_policy_document()
    expected_policy = {
        "schema": "workflow-delivery/v3/bounded-static-reference-policy",
        "policy-id": (
            "release/hcoona-release-smoke-npm-bounded-static-reference-v1"
        ),
        "producer": {
            "package": "@hcoona/hcoona-release-smoke-npm",
            "root": "src/public/lib/hcoona-release-smoke-npm",
            "manifest": (
                "src/public/lib/hcoona-release-smoke-npm/package.json"
            ),
        },
        "source-kinds": ["git-target", "index", "worktree"],
        "selectors": [
            {
                "basename": "package.json",
                "family": "npm-manifest",
                "graph": "npm-manifest-v1",
            },
            {
                "basename": "pnpm-lock.yaml",
                "exclude-descendant": ".github/workflows",
                "family": "pnpm-lock",
                "graph": "pnpm-lock-v1",
            },
            {
                "basename": "pnpm-workspace.yaml",
                "exclude-descendant": ".github/workflows",
                "family": "pnpm-workspace",
                "graph": "pnpm-workspace-v1",
            },
            {
                "basename": "packages.lock.json",
                "family": "nuget-lock",
                "graph": "nuget-lock-v1",
            },
            {
                "basename": "packages.config",
                "family": "nuget-packages-config",
                "graph": "nuget-lock-v1",
            },
        ],
        "authority-manifest": manifest,
        "prohibited-forms": [
            "A",
            "D",
            "L",
            "V",
            "W",
            "dependency-key",
        ],
        "allowances": [
            "producer-name-in-exact-producer-manifest",
            "producer-root-outside-dependency-position",
        ],
        "traversal": {
            "inventory-order": "normalized-posix-path-utf8-bytes",
            "graph-order": "declared",
            "array-order": "index",
            "mapping-order": "declared-section-then-utf8-key",
        },
        "failure-selection": {
            "source-before-graph": True,
            "first-typed-graph-failure": True,
            "cleanup-overrides": True,
            "partial-findings-on-error": False,
        },
    }

    manifest_core = {
        key: value
        for key, value in manifest.items()
        if key
        not in {
            "graph-contracts",
            "normalized-fact-contracts",
            "runtime-closure",
        }
    }
    assert manifest_core == _PHASE2_EXPECTED_AUTHORITY_MANIFEST
    assert document == expected_policy
    graph_contracts = manifest["graph-contracts"]
    assert graph_contracts["npm-manifest-v1"]["decoding"] == {
        "byte-preflight": "fatal-utf8",
        "snapshot-bytes": "exact",
        "accepted-leading-utf8-bom-counts": [0, 1],
        "next-leading-utf8-bom-outcome": "authority-rejected",
    }
    assert graph_contracts["pnpm-lock-v1"]["decoding"][
        "accepted-leading-utf8-bom-counts"
    ] == [0, 1, 2]
    read_lock_call = next(
        call
        for call in graph_contracts["pnpm-lock-v1"]["calls"]
        if call["api"] == "readWantedLockfileWithMergeInfo"
    )
    assert read_lock_call["options"] == {
        "autofixMergeConflicts": True,
        "ignoreIncompatible": False,
        "mergeGitBranchLockfiles": False,
        "useGitBranchLockfile": False,
        "wantedVersions": ["9.0"],
    }
    nuget_contract = graph_contracts["nuget-lock-v1"]
    assert nuget_contract["projections"] == [
        {
            "api": "PackageDependencyType.ToString",
            "source": "LockFileDependency.Type",
            "field": "dependencyType",
        },
        {
            "api": "VersionRange.ToNormalizedString",
            "source": "LockFileDependency.RequestedVersion",
            "field": "requestedRange",
        },
        {
            "api": "NuGetVersion.ToNormalizedString",
            "source": "LockFileDependency.ResolvedVersion",
            "field": "resolvedVersion",
        },
        {
            "api": "VersionRange.ToNormalizedString",
            "source": "PackageDependency.VersionRange",
            "field": "dependencies[].requestedRange",
        },
        {
            "api": "NuGetVersion.ToNormalizedString",
            "source": "PackageIdentity.Version",
            "field": "version",
        },
    ]
    assert nuget_contract["ordering"] == [
        {
            "collection": "targets",
            "key": "PackagesLockFileTarget.Name",
            "comparers": ["StringComparer.Ordinal"],
        },
        {
            "collection": "target.dependencies",
            "key": "LockFileDependency.Id",
            "comparers": [
                "StringComparer.OrdinalIgnoreCase",
                "StringComparer.Ordinal",
            ],
        },
        {
            "collection": "dependency.dependencies",
            "key": "PackageDependency.Id",
            "comparers": [
                "StringComparer.OrdinalIgnoreCase",
                "StringComparer.Ordinal",
            ],
        },
        {
            "collection": "packages.config",
            "key": "PackageReference.PackageIdentity",
            "comparers": ["PackageIdentity.Comparer"],
        },
    ]
    fact_contracts = manifest["normalized-fact-contracts"]["facts"]
    assert fact_contracts["npm-reference"]["fields"]["sourceSpec"] == (
        "exact-string"
    )
    assert (
        fact_contracts["pnpm-lock-importer-reference"]["fields"][
            "workspaceSelector"
        ]
        == "nullable-exact-string"
    )
    assert fact_contracts["pnpm-workspace-reference"]["fields"][
        "catalogName"
    ] == ("nullable-exact-string")
    snapshot_dependency_contract = manifest["normalized-fact-contracts"][
        "definitions"
    ]["pnpm-snapshot-dependency"]["fields"]
    assert snapshot_dependency_contract["dependencyKey"] == "exact-string"
    assert snapshot_dependency_contract["reference"] == "exact-string"
    runtime_closure = manifest["runtime-closure"]
    assert runtime_closure["mise-config"]["selectors"] == [
        {
            "tool": "dotnet",
            "config-key": "core:dotnet",
            "selector": "10",
            "lock-key": "dotnet",
        },
        {
            "tool": "node",
            "config-key": "node",
            "selector": "24",
            "lock-key": "node",
        },
        {
            "tool": "pnpm",
            "config-key": "pnpm",
            "selector": "11.22.0",
            "lock-key": "pnpm",
        },
    ]
    mutated = json.loads(json.dumps(document))
    mutated["authority-manifest"]["graph-contracts"]["pnpm-lock-v1"][
        "decoding"
    ]["accepted-leading-utf8-bom-counts"] = [0, 1]
    assert policy.canonical_sha256(mutated) != (
        policy.STATIC_REFERENCE_POLICY_DIGEST
    )
    mutated = json.loads(json.dumps(document))
    mutated["authority-manifest"]["graph-contracts"]["nuget-lock-v1"][
        "ordering"
    ][1]["comparers"] = ["StringComparer.Ordinal"]
    assert policy.canonical_sha256(mutated) != (
        policy.STATIC_REFERENCE_POLICY_DIGEST
    )
    assert policy.STATIC_REFERENCE_POLICY_DIGEST == (
        "sha256:"
        "c9647d381e07fd54dcf187cca242064eb00fe702201f0428689aa7a4c50f599d"
    )
    assert policy.canonical_sha256(document) == (
        "sha256:"
        "c9647d381e07fd54dcf187cca242064eb00fe702201f0428689aa7a4c50f599d"
    )


def test_authority_closure_validation_binds_exact_locks_and_preparation_stamp(
    tmp_path: Path,
) -> None:
    """Require current lock bytes and the exact post-preparation stamp."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    repository = tmp_path / "repository"
    repository.mkdir()
    closures = policy.static_reference_authority_manifest()[
        "dependency-closures"
    ]
    assert isinstance(closures, list)
    for closure in closures:
        assert isinstance(closure, dict)
        relative_path = closure["path"]
        assert isinstance(relative_path, str)
        destination = repository / relative_path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPO_ROOT / relative_path, destination)
    for relative_path in ("mise.toml", "mise.lock"):
        shutil.copyfile(REPO_ROOT / relative_path, repository / relative_path)

    policy.validate_static_reference_dependency_closures(repository)
    stamp_path = policy.static_reference_authority_preparation_stamp_path(
        repository
    )
    stamp_path.parent.mkdir(parents=True)
    stamp_path.write_bytes(
        canonicalize(policy.static_reference_authority_preparation_document())
    )
    policy.validate_static_reference_authority_preparation(repository)

    pnpm_lock = repository / "pnpm-lock.yaml"
    original_lock = pnpm_lock.read_bytes()
    pnpm_lock.write_bytes(original_lock + b"\n")
    with pytest.raises(
        policy.StaticReferenceAuthorityMismatchError,
        match="closure does not match",
    ):
        policy.validate_static_reference_authority_preparation(repository)

    pnpm_lock.write_bytes(original_lock)
    mise_config = repository / "mise.toml"
    original_mise_config = mise_config.read_bytes()
    mise_config.write_bytes(
        original_mise_config.replace(
            b'node = "24"',
            b'node = "22"',
            1,
        )
    )
    with pytest.raises(
        policy.StaticReferenceAuthorityMismatchError,
        match="closure does not match",
    ):
        policy.validate_static_reference_authority_preparation(repository)

    mise_config.write_bytes(original_mise_config)
    mise_lock = repository / "mise.lock"
    original_mise_lock = mise_lock.read_bytes()
    mise_lock.write_bytes(
        original_mise_lock.replace(
            (
                b"sha256:"
                b"f625d97cd707df4ff96254916fbc5ff014f09c09effe5a1e0ca8f6d41a8789d4"
            ),
            b"sha256:" + (b"0" * 64),
            1,
        )
    )
    with pytest.raises(
        policy.StaticReferenceAuthorityMismatchError,
        match="closure does not match",
    ):
        policy.validate_static_reference_authority_preparation(repository)

    mise_lock.write_bytes(original_mise_lock)
    stamp_path.write_bytes(b"{}\n")
    with pytest.raises(
        policy.StaticReferenceAuthorityMismatchError,
        match="preparation does not match",
    ):
        policy.validate_static_reference_authority_preparation(repository)


def test_authority_closure_paths_are_materialized_with_lf() -> None:
    """Keep raw-byte authority closures stable across Git checkout policy."""
    output = _git(
        REPO_ROOT,
        "check-attr",
        "text",
        "eol",
        "--",
        "pnpm-lock.yaml",
        (
            "src/private/app/workflow-delivery-v3-nuget-authority/"
            "packages.lock.json"
        ),
    )

    assert output.splitlines() == [
        "pnpm-lock.yaml: text: set",
        "pnpm-lock.yaml: eol: lf",
        (
            "src/private/app/workflow-delivery-v3-nuget-authority/"
            "packages.lock.json: text: set"
        ),
        (
            "src/private/app/workflow-delivery-v3-nuget-authority/"
            "packages.lock.json: eol: lf"
        ),
    ]


def test_preparation_validates_locks_before_work_and_stamps_only_after_publish(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Bind prepared executables to locks validated before and after work."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    module = _load_prepare_static_reference_script()
    repository = tmp_path / "repository"
    repository.mkdir()
    project = repository / "authority.csproj"
    publish_directory = repository / "prepared"
    stamp_path = policy.static_reference_authority_preparation_stamp_path(
        repository
    )
    stamp_path.parent.mkdir(parents=True)
    stamp_path.write_bytes(b"stale")
    events: list[str] = []

    def validate(root: Path) -> None:
        assert root == repository
        assert not stamp_path.exists()
        events.append("validate")

    def run(*arguments: str) -> None:
        events.append(" ".join(arguments[:2]))
        if arguments[:2] == ("dotnet", "publish"):
            for name in (
                "NuGet.Packaging.dll",
                "NuGet.ProjectModel.dll",
                "WorkflowDeliveryV3NuGetAuthority.deps.json",
                "WorkflowDeliveryV3NuGetAuthority.dll",
                "WorkflowDeliveryV3NuGetAuthority.runtimeconfig.json",
            ):
                (publish_directory / name).write_bytes(b"prepared")

    monkeypatch.setattr(module, "_REPOSITORY_ROOT", repository)
    monkeypatch.setattr(module, "_NUGET_PROJECT", project)
    monkeypatch.setattr(module, "_PUBLISH_DIRECTORY", publish_directory)
    monkeypatch.setattr(
        module,
        "validate_static_reference_dependency_closures",
        validate,
    )
    monkeypatch.setattr(module, "_run", run)

    assert module.main() == 0
    assert events == [
        "validate",
        "pnpm install",
        "dotnet restore",
        "dotnet publish",
        "validate",
    ]
    assert stamp_path.read_bytes() == canonicalize(
        policy.static_reference_authority_preparation_document()
    )


def test_policy_traverses_candidates_and_graphs_in_deterministic_order(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Traverse candidates and authority graphs deterministically."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidates = (
        _phase2_candidate(
            "00/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
            content=b'{"name":"00"}\n',
        ),
        _phase2_candidate(
            "01/pnpm-lock.yaml",
            family="pnpm-lock",
            graph_id="pnpm-lock-v1",
            content=b"lockfileVersion: '9.0'\n",
        ),
        _phase2_candidate(
            "02/pnpm-workspace.yaml",
            family="pnpm-workspace",
            graph_id="pnpm-workspace-v1",
            content=b"packages: []\n",
        ),
        _phase2_candidate(
            "03/packages.lock.json",
            family="nuget-lock",
            graph_id="nuget-lock-v1",
            content=b'{"version":3,"dependencies":{}}\n',
        ),
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=candidates,
    )
    acquisition_calls: list[tuple[Path, str, str | None]] = []

    def acquire(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None = None,
    ) -> object:
        acquisition_calls.append((repository_root, source_kind, target))
        return inventory

    authority_calls: list[tuple[Path, str, str, bytes, Path, object]] = []
    materialized_paths: list[Path | None] = []

    def run_authority(
        repository_root: Path,
        candidate: StaticReferenceCandidate,
        invocation: object,
        session: object,
    ) -> object:
        materialized_paths.append(invocation.candidate_path)
        if invocation.candidate_path is not None:
            assert invocation.candidate_path.read_bytes() == candidate.content
        authority_calls.append(
            (
                repository_root,
                candidate.path,
                candidate.selection.graph_id,
                candidate.content,
                invocation.root,
                session,
            )
        )
        return authority.AuthorityGraphOutcome(
            graph_id=candidate.selection.graph_id,
            implementation_identities=_PHASE2_GRAPH_IMPLEMENTATIONS[
                candidate.selection.graph_id
            ],
            facts=(),
        )

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        sessions.append(session)
        return session

    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        acquire,
    )

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        authority_runner=run_authority,
        session_factory=session_factory,
    )

    assert acquisition_calls == [(tmp_path, "index", None)]
    assert [
        (path, graph_id, content)
        for _, path, graph_id, content, _, _ in authority_calls
    ] == [
        ("00/package.json", "npm-manifest-v1", b'{"name":"00"}\n'),
        (
            "01/pnpm-lock.yaml",
            "pnpm-lock-v1",
            b"lockfileVersion: '9.0'\n",
        ),
        (
            "02/pnpm-workspace.yaml",
            "pnpm-workspace-v1",
            b"packages: []\n",
        ),
        (
            "03/packages.lock.json",
            "nuget-lock-v1",
            b'{"version":3,"dependencies":{}}\n',
        ),
    ]
    assert [
        candidate_path is None for candidate_path in materialized_paths
    ] == [False, False, False, True]
    assert all(
        repository_root == tmp_path.resolve()
        for repository_root, *_ in authority_calls
    )
    assert len({root for *_, root, _ in authority_calls}) == 4  # noqa: PLR2004
    assert all(
        seen_session is sessions[0] for *_, seen_session in authority_calls
    )
    assert result.result == "clean"
    assert result.error_kind is None
    assert result.findings == ()
    assert result.implementation_identities == (
        "@npmcli/package-json@8.0.0",
        "@pnpm/deps.path@1101.0.1",
        "@pnpm/lockfile.fs@1100.2.5",
        "@pnpm/lockfile.utils@1102.1.0",
        "@pnpm/resolving.npm-resolver@1104.1.0",
        "@pnpm/workspace.spec-parser@1100.0.1",
        "@pnpm/workspace.workspace-manifest-reader@1100.1.8",
        "NuGet.Packaging@7.9.0",
        "NuGet.ProjectModel@7.9.0",
        "dotnet-runtime@10.0.8",
        "node@24.19.0",
        "npm-package-arg@14.0.0",
    )
    assert len(sessions) == 1
    assert not sessions[0].root.exists()
    assert all(
        not invocation_root.exists()
        for *_, invocation_root, _ in authority_calls
    )


def test_policy_materializes_every_candidate_before_any_graph_execution(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Give later materialization failures precedence over every graph."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidates = (
        _phase2_candidate(
            "00/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
        ),
        _phase2_candidate(
            "01/pnpm-workspace.yaml",
            family="pnpm-workspace",
            graph_id="pnpm-workspace-v1",
            content=b"packages: []\n",
        ),
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=candidates,
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    materialization_calls: list[str] = []
    invocation_roots: list[Path] = []
    sessions: list[StaticReferenceSession] = []

    class LaterFailureSession(StaticReferenceSession):
        def materialize(
            self,
            candidate: StaticReferenceCandidate,
            *,
            source_kind: str,
            target: str | None,
        ) -> object:
            materialization_calls.append(candidate.path)
            if len(materialization_calls) == 2:  # noqa: PLR2004
                message = "later materialization failed"
                raise OSError(message)
            invocation = super().materialize(
                candidate,
                source_kind=source_kind,  # type: ignore[arg-type]
                target=target,
            )
            invocation_roots.append(invocation.root)
            return invocation

    def session_factory() -> StaticReferenceSession:
        session = LaterFailureSession(parent=tmp_path)
        sessions.append(session)
        return session

    graph_calls: list[object] = []
    closure_calls: list[Path] = []

    def forbidden_graph(*args: object, **kwargs: object) -> object:
        graph_calls.append((args, kwargs))
        message = "graph ran before complete materialization"
        raise AssertionError(message)

    def forbidden_closure(root: Path) -> None:
        closure_calls.append(root)
        message = "closure validation ran before complete materialization"
        raise AssertionError(message)

    monkeypatch.setattr(policy, "run_authority_graph", forbidden_graph)
    monkeypatch.setattr(
        policy,
        "validate_static_reference_authority_preparation",
        forbidden_closure,
    )

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        session_factory=session_factory,
    )

    assert result.error_kind == "source-acquisition-failed"
    assert result.findings == ()
    assert result.implementation_identities == ()
    assert materialization_calls == [
        "00/package.json",
        "01/pnpm-workspace.yaml",
    ]
    assert graph_calls == []
    assert closure_calls == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()
    assert all(not root.exists() for root in invocation_roots)


def test_policy_preflights_utf8_before_candidate_materialization(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Reject malformed JSON/YAML bytes before creating a snapshot."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidate = _phase2_candidate(
        "consumer/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
        content=b'{"name":"malformed-\xff"}\n',
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=(candidate,),
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    materialization_calls: list[str] = []
    authority_calls: list[object] = []
    sessions: list[StaticReferenceSession] = []

    class RecordingSession(StaticReferenceSession):
        def materialize(
            self,
            selected_candidate: StaticReferenceCandidate,
            *,
            source_kind: str,
            target: str | None,
        ) -> object:
            materialization_calls.append(selected_candidate.path)
            return super().materialize(
                selected_candidate,
                source_kind=source_kind,  # type: ignore[arg-type]
                target=target,
            )

    def session_factory() -> StaticReferenceSession:
        session = RecordingSession(parent=tmp_path)
        sessions.append(session)
        return session

    def forbidden_authority(*args: object, **kwargs: object) -> object:
        authority_calls.append((args, kwargs))
        message = "authority ran after UTF-8 preflight failed"
        raise AssertionError(message)

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        authority_runner=forbidden_authority,
        session_factory=session_factory,
    )

    assert result.error_kind == "encoding-rejected"
    assert result.findings == ()
    assert result.implementation_identities == ()
    assert materialization_calls == []
    assert authority_calls == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()


def test_policy_reports_authority_mismatch_only_after_materialization(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Validate the prepared closure after source shaping and before graphs."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidate = _phase2_candidate(
        "consumer/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="worktree",
        target=None,
        candidates=(candidate,),
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    invocation_roots: list[Path] = []
    sessions: list[StaticReferenceSession] = []

    class RecordingSession(StaticReferenceSession):
        def materialize(
            self,
            selected_candidate: StaticReferenceCandidate,
            *,
            source_kind: str,
            target: str | None,
        ) -> object:
            invocation = super().materialize(
                selected_candidate,
                source_kind=source_kind,  # type: ignore[arg-type]
                target=target,
            )
            invocation_roots.append(invocation.root)
            return invocation

    def session_factory() -> StaticReferenceSession:
        session = RecordingSession(parent=tmp_path)
        sessions.append(session)
        return session

    closure_calls: list[Path] = []

    def mismatch(root: Path) -> None:
        closure_calls.append(root)
        assert all(path.exists() for path in invocation_roots)
        message = "injected authority mismatch"
        raise policy.StaticReferenceAuthorityMismatchError(message)

    graph_calls: list[object] = []

    def forbidden_graph(*args: object, **kwargs: object) -> object:
        graph_calls.append((args, kwargs))
        message = "graph ran with a mismatched authority closure"
        raise AssertionError(message)

    monkeypatch.setattr(
        policy,
        "validate_static_reference_authority_preparation",
        mismatch,
    )
    monkeypatch.setattr(policy, "run_authority_graph", forbidden_graph)

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="worktree",
        session_factory=session_factory,
    )

    assert result.error_kind == "authority-mismatch"
    assert result.findings == ()
    assert result.implementation_identities == ()
    assert closure_calls == [tmp_path.resolve()]
    assert graph_calls == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()
    assert all(not root.exists() for root in invocation_roots)


def test_policy_stops_at_the_first_source_error_before_authority_execution(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Stop before authority execution when source acquisition fails."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    acquisition_calls: list[tuple[Path, str, str | None]] = []
    seed_repository = tmp_path / "seed-repository"
    _initialize_repository(seed_repository)
    with pytest.raises(SourceAcquisitionError) as seeded:
        acquire_static_reference_inventory(
            seed_repository,
            source_kind="git-target",
            target=TARGET,
        )
    source_error = seeded.value
    shutil.rmtree(seed_repository)

    def fail_acquisition(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None = None,
    ) -> object:
        acquisition_calls.append((repository_root, source_kind, target))
        raise source_error

    authority_calls: list[object] = []
    session_calls: list[object] = []

    def unexpected_authority(*args: object, **kwargs: object) -> object:
        authority_calls.append((args, kwargs))
        message = "authority must not run after a source error"
        raise AssertionError(message)

    def unexpected_session() -> StaticReferenceSession:
        session_calls.append(object())
        message = "session must not open after a source error"
        raise AssertionError(message)

    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        fail_acquisition,
    )

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="git-target",
        target=TARGET,
        authority_runner=unexpected_authority,
        session_factory=unexpected_session,
    )

    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "git-target",
        "policy-id": (
            "release/hcoona-release-smoke-npm-bounded-static-reference-v1"
        ),
        "policy-digest": (
            "sha256:"
            "c9647d381e07fd54dcf187cca242064eb00fe702201f0428689aa7a4c50f599d"
        ),
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
        "target": TARGET,
    }
    assert acquisition_calls == [(tmp_path, "git-target", TARGET)]
    assert authority_calls == []
    assert session_calls == []
    assert list(tmp_path.iterdir()) == []


def test_policy_returns_only_the_first_typed_graph_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Return only the first typed authority graph error."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidates = (
        _phase2_candidate(
            "00/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
        ),
        _phase2_candidate(
            "01/pnpm-workspace.yaml",
            family="pnpm-workspace",
            graph_id="pnpm-workspace-v1",
            content=b"packages: []\n",
        ),
        _phase2_candidate(
            "02/packages.lock.json",
            family="nuget-lock",
            graph_id="nuget-lock-v1",
        ),
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="worktree",
        target=None,
        candidates=candidates,
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    authority_calls: list[tuple[str, str, Path]] = []

    def run_authority(
        repository_root: Path,
        candidate: StaticReferenceCandidate,
        invocation: object,
        session: object,
    ) -> object:
        del repository_root, session
        authority_calls.append(
            (
                candidate.path,
                candidate.selection.graph_id,
                invocation.root,
            )
        )
        if candidate.path == "00/package.json":
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
                facts=(
                    {
                        "kind": "npm-package-name",
                        "context": "name",
                        "name": "@hcoona/hcoona-release-smoke-npm",
                    },
                ),
            )
        if candidate.path == "01/pnpm-workspace.yaml":
            return authority.AuthorityGraphOutcome(
                graph_id="pnpm-workspace-v1",
                implementation_identities=(),
                facts=(),
                error_kind="authority-rejected",
            )
        message = "later graph must not run"
        raise AssertionError(message)

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        sessions.append(session)
        return session

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="worktree",
        authority_runner=run_authority,
        session_factory=session_factory,
    )

    assert [(path, graph_id) for path, graph_id, _ in authority_calls] == [
        ("00/package.json", "npm-manifest-v1"),
        ("01/pnpm-workspace.yaml", "pnpm-workspace-v1"),
    ]
    assert result.result == "error"
    assert result.error_kind == "authority-rejected"
    assert result.findings == ()
    assert result.implementation_identities == _PHASE2_NPM_IMPLEMENTATIONS
    assert result.to_document()["error-kind"] == "authority-rejected"
    assert result.to_document()["findings"] == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()
    assert all(not root.exists() for _, _, root in authority_calls)


@pytest.mark.parametrize(
    ("terminal_path", "expected"),
    [
        ("success", (1, 2, None)),
        ("source-error", (0, 1, "source-acquisition-failed")),
        ("graph-error", (1, 2, "authority-rejected")),
    ],
    ids=("success", "source-error", "graph-error"),
)
def test_policy_cleanup_failed_overrides_success_source_or_graph_result_and_cleans_exact_roots(  # noqa: E501, PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    terminal_path: str,
    expected: tuple[int, int, str | None],
    caplog: pytest.LogCaptureFixture,
) -> None:
    """Let cleanup failure override every terminal scan outcome."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    caplog.set_level("WARNING", logger=policy.__name__)
    (
        expected_authority_calls,
        expected_cleanup_calls,
        expected_prior_error,
    ) = expected
    candidate = _phase2_candidate(
        "cleanup/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=(candidate,),
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    cleanup_calls: list[Path] = []

    def cleanup_then_fail(path: Path) -> None:
        cleanup_calls.append(path)
        if path.exists():
            shutil.rmtree(path)
        message = "phase-2-secret-cleanup-detail"
        raise OSError(message)

    materialize_calls: list[tuple[str, str, str | None]] = []

    class SourceFailureSession(StaticReferenceSession):
        def materialize(
            self,
            selected_candidate: StaticReferenceCandidate,
            *,
            source_kind: str,
            target: str | None,
        ) -> object:
            materialize_calls.append(
                (selected_candidate.path, source_kind, target)
            )
            message = "phase-2-secret-source-detail"
            raise OSError(message)

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session_type = (
            SourceFailureSession
            if terminal_path == "source-error"
            else StaticReferenceSession
        )
        session = session_type(
            parent=tmp_path,
            cleanup=cleanup_then_fail,
        )
        sessions.append(session)
        return session

    authority_calls: list[tuple[str, Path]] = []

    def run_authority(
        repository_root: Path,
        selected_candidate: StaticReferenceCandidate,
        invocation: object,
        session: object,
    ) -> object:
        del repository_root, session
        authority_calls.append((selected_candidate.path, invocation.root))
        if terminal_path == "graph-error":
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=(),
                facts=(),
                error_kind="authority-rejected",
            )
        return authority.AuthorityGraphOutcome(
            graph_id="npm-manifest-v1",
            implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
            facts=(
                {
                    "kind": "npm-package-name",
                    "context": "name",
                    "name": "@hcoona/hcoona-release-smoke-npm",
                },
            ),
        )

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        authority_runner=run_authority,
        session_factory=session_factory,
    )

    assert result.result == "error"
    assert result.error_kind == "cleanup-failed"
    assert result.findings == ()
    assert result.implementation_identities == (
        _PHASE2_NPM_IMPLEMENTATIONS if terminal_path == "success" else ()
    )
    assert "phase-2-secret" not in json.dumps(result.to_document())
    assert len(authority_calls) == expected_authority_calls
    assert len(cleanup_calls) == expected_cleanup_calls
    assert len(sessions) == 1
    if terminal_path == "source-error":
        assert materialize_calls == [("cleanup/package.json", "index", None)]
        assert cleanup_calls == [sessions[0].root]
    else:
        assert materialize_calls == []
        assert cleanup_calls == [
            authority_calls[0][1],
            sessions[0].root,
        ]
        assert authority_calls[0][1].parent == sessions[0].root
    assert all(not path.exists() for path in cleanup_calls)
    assert list(tmp_path.iterdir()) == []
    diagnostics = [
        record.getMessage()
        for record in caplog.records
        if record.name == policy.__name__
    ]
    if expected_prior_error is None:
        assert diagnostics == []
    else:
        assert diagnostics == [
            (
                "static-reference cleanup failure overrode prior "
                f"error-kind={expected_prior_error}"
            )
        ]
    assert "phase-2-secret" not in caplog.text


def test_hexo_file_reference_and_v9_lock_project_a_typed_directory(
    tmp_path: Path,
) -> None:
    """Project the tracked Hexo file reference and keep the whole scan clean."""
    from three_workflow_delivery_v3.release.static_reference_authority import (  # noqa: PLC0415
        run_authority_graph,
    )
    from three_workflow_delivery_v3.release.static_reference_policy import (  # noqa: PLC0415
        static_reference_policy_document,
    )

    hexo_root = (
        REPO_ROOT / "src/public/lib/hexo-renderer-asciidoc/examples/hexo-site"
    )
    manifest_path = hexo_root / "package.json"
    lock_path = hexo_root / "pnpm-lock.yaml"
    manifest_logical_path = manifest_path.relative_to(REPO_ROOT).as_posix()
    lock_logical_path = lock_path.relative_to(REPO_ROOT).as_posix()
    manifest = json.loads(manifest_path.read_bytes())

    assert manifest["dependencies"]["hexo-renderer-asciidoc"] == "file:../.."

    inventory = acquire_static_reference_inventory(
        REPO_ROOT,
        source_kind="worktree",
    )
    candidates = {
        candidate.path: candidate for candidate in inventory.candidates
    }
    manifest_candidate = candidates[manifest_logical_path]
    lock_candidate = candidates[lock_logical_path]

    assert manifest_candidate.selection == StaticReferenceSelection(
        family="npm-manifest",
        graph_id="npm-manifest-v1",
        input_mode="strict-utf8-file",
    )
    assert manifest_candidate.content == manifest_path.read_bytes()
    assert lock_candidate.selection == StaticReferenceSelection(
        family="pnpm-lock",
        graph_id="pnpm-lock-v1",
        input_mode="strict-utf8-file",
    )
    assert lock_candidate.content == lock_path.read_bytes()

    session_root: Path
    with StaticReferenceSession(parent=tmp_path) as session:
        session_root = session.root
        invocation = session.materialize(
            lock_candidate,
            source_kind="worktree",
            target=None,
        )
        assert sorted(
            path.relative_to(invocation.snapshot_root).as_posix()
            for path in invocation.snapshot_root.rglob("*")
            if path.is_file()
        ) == [lock_logical_path]

        outcome = run_authority_graph(
            REPO_ROOT,
            lock_candidate,
            invocation,
            session,
        )
        relevant_facts = tuple(
            fact
            for fact in outcome.facts
            if (
                fact["kind"] == "pnpm-lock-importer-reference"
                and fact["dependencyKey"] == "hexo-renderer-asciidoc"
            )
            or (
                fact["kind"] == "pnpm-lock-snapshot"
                and fact["name"] == "hexo-renderer-asciidoc"
            )
        )

        assert outcome.error_kind is None
        assert (
            outcome.implementation_identities
            == (_PHASE2_GRAPH_IMPLEMENTATIONS["pnpm-lock-v1"])
        )
        assert relevant_facts == (
            {
                "dependencies": [
                    {
                        "dependencyKey": "@asciidoctor/core",
                        "reference": "4.0.11",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": "cheerio",
                        "reference": "1.2.0",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": "entities",
                        "reference": "8.0.0",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": "hexo",
                        "reference": "8.1.2(chokidar@3.6.0)",
                        "section": "dependencies",
                    },
                    {
                        "dependencyKey": "hexo-util",
                        "reference": "4.0.0",
                        "section": "dependencies",
                    },
                ],
                "dependencyPath": (
                    "hexo-renderer-asciidoc@file:../.."
                    "(hexo@8.1.2(chokidar@3.6.0))"
                ),
                "kind": "pnpm-lock-snapshot",
                "name": "hexo-renderer-asciidoc",
                "nonSemverVersion": "file:../..",
                "registryName": None,
                "resolution": {
                    "kind": "directory",
                    "localPath": "src/public/lib/hexo-renderer-asciidoc",
                },
                "version": None,
            },
            {
                "dependencyKey": "hexo-renderer-asciidoc",
                "importerId": ".",
                "kind": "pnpm-lock-importer-reference",
                "rawSpecifier": "file:../..",
                "registrySpec": None,
                "resolvedReference": ("file:../..(hexo@8.1.2(chokidar@3.6.0))"),
                "section": "dependencies",
                "snapshotKey": (
                    "hexo-renderer-asciidoc@file:../.."
                    "(hexo@8.1.2(chokidar@3.6.0))"
                ),
                "workspaceSelector": None,
            },
        )
        session.release(invocation)

    assert not session_root.exists()
    policy_document = static_reference_policy_document()
    assert lock_logical_path not in json.dumps(policy_document)

    assert all(
        fact.get("registryName") != PRODUCER_PACKAGE for fact in relevant_facts
    )


@pytest.mark.parametrize(
    ("path", "content"),
    [
        pytest.param(
            ".github/workflows/release.yml",
            b"jobs:\n  inspect:\n    uses: @hcoona/hcoona-release-smoke-npm\n",
            id="github-workflow",
        ),
        pytest.param(
            ".github/actions/inspect/action.yml",
            (
                b"runs:\n  using: node20\n"
                b"  main: @hcoona/hcoona-release-smoke-npm\n"
            ),
            id="composite-action",
        ),
        pytest.param(
            "src/consumer/import-subpath.mjs",
            b'import "@hcoona/hcoona-release-smoke-npm/runtime";\n',
            id="node-import-subpath",
        ),
        pytest.param(
            "src/consumer/package-lock.json",
            (
                b'{"packages":{"node_modules/x":{"name":'
                b'"@hcoona/hcoona-release-smoke-npm"}}}\n'
            ),
            id="npm-lock",
        ),
        pytest.param(
            "uv.lock",
            b'[[package]]\nname = "@hcoona/hcoona-release-smoke-npm"\n',
            id="uv-lock",
        ),
        pytest.param(
            "src/consumer/yarn.lock",
            b'"@hcoona/hcoona-release-smoke-npm@*":\n  version "1.0.0"\n',
            id="yarn-lock",
        ),
        pytest.param(
            "src/consumer/Consumer.csproj",
            (
                b'<Project><ItemGroup><PackageReference Include="'
                b'@hcoona/hcoona-release-smoke-npm" />'
                b"</ItemGroup></Project>\n"
            ),
            id="msbuild-project",
        ),
        pytest.param(
            "Directory.Packages.props",
            (
                b'<Project><ItemGroup><PackageVersion Include="'
                b'@hcoona/hcoona-release-smoke-npm" />'
                b"</ItemGroup></Project>\n"
            ),
            id="msbuild-central-manifest",
        ),
        pytest.param(
            "src/consumer/pyproject.toml",
            b"dependencies = [\x22@hcoona/hcoona-release-smoke-npm\x22]\n",
            id="standalone-python-manifest",
        ),
        pytest.param(
            "eng/scripts/install.sh",
            b"npm install @hcoona/hcoona-release-smoke-npm\n",
            id="shell-script",
        ),
        pytest.param(
            "eng/scripts/install.ps1",
            b"npm install @hcoona/hcoona-release-smoke-npm\n",
            id="powershell-script",
        ),
        pytest.param(
            ".github/workflows/pnpm-lock.yaml",
            b"lockfileVersion: '9.0'\npackages:\n  producer: {}\n",
            id="reserved-workflow-pnpm-lock",
        ),
        pytest.param(
            ".github/workflows/pnpm-workspace.yaml",
            b"packages:\n  - '@hcoona/hcoona-release-smoke-npm'\n",
            id="reserved-workflow-pnpm-workspace",
        ),
    ],
)
def test_excluded_surface_selects_no_graph_and_has_no_fallback(
    tmp_path: Path,
    path: str,
    content: bytes,
) -> None:
    """Keep every excluded representative outside every authority graph."""
    from three_workflow_delivery_v3.release.static_reference_policy import (  # noqa: PLC0415
        scan_bounded_static_references,
    )
    from three_workflow_delivery_v3.release.static_reference_source import (  # noqa: PLC0415
        select_static_reference_path,
    )

    repository = tmp_path / "repository"
    _initialize_repository(repository)
    source = repository.joinpath(*path.split("/"))
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_bytes(content)
    inventory = acquire_static_reference_inventory(
        repository,
        source_kind="worktree",
    )
    fallback_calls: list[tuple[tuple[object, ...], dict[str, object]]] = []
    sessions: list[StaticReferenceSession] = []

    def forbidden_fallback(
        *args: object,
        **kwargs: object,
    ) -> object:
        fallback_calls.append((args, kwargs))
        message = "excluded surface invoked an authority fallback"
        raise AssertionError(message)

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        sessions.append(session)
        return session

    result = scan_bounded_static_references(
        repository,
        source_kind="worktree",
        authority_runner=forbidden_fallback,  # type: ignore[arg-type]
        session_factory=session_factory,
    )

    assert select_static_reference_path(path) is None
    assert inventory.candidates == ()
    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "clean",
        "source-kind": "worktree",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
    }
    assert fallback_calls == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()


def test_no_forbidden_static_reference_strategy_or_consumer_claim_is_declared() -> (  # noqa: E501
    None
):
    """Reject superseded grammars, exceptions, inventories, and claims."""
    from three_workflow_delivery_v3.release.static_reference_policy import (  # noqa: PLC0415
        static_reference_policy_document,
    )

    obsolete_paths = (
        "eng/scripts/workflow_delivery_v3_consumer_policy.py",
        (
            "src/public/lib/three-workflow-delivery-v3/src/"
            "three_workflow_delivery_v3/release/consumer_policy.py"
        ),
        (
            "src/public/lib/three-workflow-delivery-v3/src/"
            "three_workflow_delivery_v3/release/javascript_consumer.py"
        ),
    )
    assert [
        path for path in obsolete_paths if (REPO_ROOT / path).exists()
    ] == []

    implementation_paths = (
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_static_reference.py",
        (
            REPO_ROOT
            / "eng/scripts/workflow_delivery_v3_static_reference_node.mjs"
        ),
        (
            REPO_ROOT
            / "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs"
        ),
        *sorted(
            (
                REPO_ROOT / "src/public/lib/three-workflow-delivery-v3/src/"
                "three_workflow_delivery_v3/release"
            ).glob("static_reference_*.py")
        ),
    )
    implementation_text = "\n".join(
        path.read_text(encoding="utf-8") for path in implementation_paths
    ).casefold()
    assert {
        marker
        for marker in (
            "approved_consumer_exceptions",
            "consumer_policy_parser_profile",
            "tree_sitter",
            "tree-sitter",
            "dataflow",
            "scanned-surfaces",
            "admitted-exceptions",
            "fixed-inventory",
            "trigger-catalog",
            "consumer-policy-result",
        )
        if marker in implementation_text
    } == set()

    policy_document = static_reference_policy_document()
    policy_text = json.dumps(policy_document, sort_keys=True).casefold()
    assert {
        marker
        for marker in (
            "compatibility-grammar",
            "secondary-authority",
            "whole-file",
            "approved-exception",
            "fixture-exception",
            "fixed-inventory",
            "inventory-authority",
            "trigger-catalog",
            "consumer-claim",
            '"consumers"',
        )
        if marker in policy_text
    } == set()


@pytest.mark.parametrize(
    ("source_kind", "result_target", "expected_outcome"),
    [
        pytest.param(
            "git-target",
            TARGET,
            "accepted",
            id="exact-git-target",
        ),
        pytest.param(
            "git-target",
            "2" * 40,
            "rejected",
            id="different-git-target",
        ),
        pytest.param("index", None, "rejected", id="index-feedback"),
        pytest.param("worktree", None, "rejected", id="worktree-feedback"),
    ],
)
def test_cli_live_evidence_accepts_git_target_only(
    monkeypatch: pytest.MonkeyPatch,
    source_kind: str,
    result_target: str | None,
    expected_outcome: str,
) -> None:
    """Admit only an exact-target Result at the CLI's Live boundary."""
    from datetime import UTC, datetime, timedelta  # noqa: PLC0415
    from types import SimpleNamespace  # noqa: PLC0415

    from three_workflow_delivery_v3 import cli as cli_module  # noqa: PLC0415
    from three_workflow_delivery_v3.release import (  # noqa: PLC0415
        eligibility as live,
    )

    assert cli_module.admit_live_eligibility_decision is (
        live.admit_live_eligibility_decision
    )

    control = "refs/heads/main@" + TARGET
    snapshot_release_policy = object()
    snapshot = SimpleNamespace(
        context=SimpleNamespace(control=control),
        release_policy=snapshot_release_policy,
    )
    repository_model = SimpleNamespace(
        canonical_digest=DIGEST,
        snapshot=snapshot,
    )
    policy = SimpleNamespace(governance=object())
    intent = SimpleNamespace(
        repository=live.GOVERNANCE_REPOSITORY,
        workflow_path=live.BUDDY_LIVE_WORKFLOW_PATH,
        channel="buddy",
        mode="live",
        purpose="live-release",
        release_unit=live.FIRST_SLICE_RELEASE_UNIT,
        request_id="phase-5-live-request",
        workflow_run_id=501,
        run_attempt=1,
        selected_ref="refs/heads/main",
        target=TARGET,
    )
    now = datetime(2026, 9, 1, tzinfo=UTC)
    governance = SimpleNamespace(
        inspected_at=now - timedelta(days=2),
        observed_at=now - timedelta(days=1),
        expires_at=now + timedelta(days=30),
        live_enabled=True,
    )
    validation_calls: list[tuple[object, object, object]] = []
    admitted_calls: list[dict[str, object]] = []

    def validate_context(
        context: object,
        actual_snapshot: object,
        actual_policy: object,
    ) -> None:
        validation_calls.append((context, actual_snapshot, actual_policy))

    def parse_governance(_value: object) -> object:
        return governance

    def policy_digest(_policy: object) -> str:
        return DIGEST

    def compiled_policy(_policy: object) -> object:
        return snapshot_release_policy

    def admitted_decision(**values: object) -> SimpleNamespace:
        admitted_calls.append(values)
        return SimpleNamespace(**values, to_document=lambda: document)

    monkeypatch.setattr(live, "ReleaseIntent", SimpleNamespace)
    monkeypatch.setattr(
        live,
        "AdmittedRepositoryModelSnapshot",
        SimpleNamespace,
    )
    monkeypatch.setattr(live, "ReleasePolicy", SimpleNamespace)
    monkeypatch.setattr(live, "_validate_live_context", validate_context)
    monkeypatch.setattr(live, "_decision_governance", parse_governance)
    monkeypatch.setattr(live, "release_policy_digest", policy_digest)
    monkeypatch.setattr(live, "catalog_digest", lambda: DIGEST)
    monkeypatch.setattr(live, "compile_release_policy", compiled_policy)
    monkeypatch.setattr(
        live,
        "AdmittedLiveEligibilityDecision",
        admitted_decision,
    )

    static_reference = BoundedStaticReferenceResult(
        source_kind=source_kind,  # type: ignore[arg-type]
        target=result_target,
        policy_id=STATIC_REFERENCE_POLICY_ID,
        policy_digest=STATIC_REFERENCE_POLICY_DIGEST,
        implementation_identities=_PHASE2_LIVE_IMPLEMENTATIONS,
        findings=(),
    )
    document = {
        "schema": live.LIVE_ELIGIBILITY_DECISION_SCHEMA,
        "context": {
            "purpose": intent.purpose,
            "request-id": intent.request_id,
            "workflow-run-id": intent.workflow_run_id,
            "run-attempt": intent.run_attempt,
            "selected-ref": intent.selected_ref,
            "target": intent.target,
            "repository-model-digest": repository_model.canonical_digest,
            "producer": live.LIVE_ELIGIBILITY_PRODUCER,
            "control": control,
            "release-policy-digest": DIGEST,
            "catalog-digest": DIGEST,
        },
        "static-reference": static_reference.to_document(),
        "governance": {},
        "result": "pass",
        "diagnostics": [],
    }
    canonical_bytes = canonicalize(document)
    expected_digest = live.canonical_sha256(document)
    arguments = {
        "intent": intent,
        "repository_model": repository_model,
        "policy": policy,
        "expected_digest": expected_digest,
        "admission_mode": live.LiveEligibilityAdmissionMode.CURRENT_FRESHNESS,
        "now": now,
    }

    if expected_outcome == "accepted":
        admitted = live.admit_live_eligibility_decision(
            canonical_bytes,
            **arguments,  # type: ignore[arg-type]
        )

        assert admitted.static_reference == static_reference
        assert admitted.context.target == TARGET
        assert len(admitted_calls) == 1
        assert admitted_calls[0]["static_reference"] == static_reference
    else:
        with pytest.raises(
            ValueError,
            match="static-reference target mismatch",
        ):
            live.admit_live_eligibility_decision(
                canonical_bytes,
                **arguments,  # type: ignore[arg-type]
            )

        assert admitted_calls == []

    assert len(validation_calls) == 1
    assert validation_calls[0][1:] == (snapshot, policy)


@pytest.mark.parametrize(
    "failure_case",
    [
        pytest.param("annotated-tag-object", id="annotated-tag-object"),
        pytest.param("non-commit-object", id="non-commit-object"),
        pytest.param("git-target-symlink", id="git-target-symlink"),
        pytest.param("git-target-gitlink", id="git-target-gitlink"),
        pytest.param("index-symlink", id="index-symlink"),
        pytest.param("index-gitlink", id="index-gitlink"),
        pytest.param("unmerged-index", id="unmerged-index"),
        pytest.param("missing-index-object", id="missing-index-object"),
        pytest.param("worktree-file-symlink", id="worktree-file-symlink"),
        pytest.param("worktree-parent-symlink", id="worktree-parent-symlink"),
    ],
)
def test_source_exact_failures_are_typed_and_stop_policy_before_authority(  # noqa: C901, PLR0915
    tmp_path: Path,
    failure_case: str,
) -> None:
    """Fail closed for every retained exact-source structural hazard."""
    from three_workflow_delivery_v3.release.static_reference_policy import (  # noqa: PLC0415
        scan_bounded_static_references,
    )

    repository = tmp_path / f"repository-{failure_case}"
    _initialize_repository(repository)
    readme = repository / "README.md"
    readme.write_bytes(b"base object\n")
    _git(repository, "add", "README.md")
    _git(repository, "commit", "--quiet", "-m", "base")
    base_target = _git(repository, "rev-parse", "HEAD")

    source_kind = "index"
    target: str | None = None
    expected_diagnostic = "unsupported-git-mode"
    expected_path: str | None = "package.json"

    if failure_case == "annotated-tag-object":
        _git(repository, "tag", "-a", "annotated", "-m", "annotated")
        source_kind = "git-target"
        target = _git(repository, "rev-parse", "refs/tags/annotated")
        expected_diagnostic = "git-target-not-exact"
        expected_path = None
    elif failure_case == "non-commit-object":
        source_kind = "git-target"
        target = _git(repository, "rev-parse", f"{base_target}^{{tree}}")
        expected_diagnostic = "git-target-unavailable"
        expected_path = None
    elif failure_case in {"git-target-symlink", "index-symlink"}:
        (repository / "package.json").symlink_to("README.md")
        _git(repository, "add", "package.json")
        if failure_case == "git-target-symlink":
            _git(repository, "commit", "--quiet", "-m", "symlink candidate")
            source_kind = "git-target"
            target = _git(repository, "rev-parse", "HEAD")
    elif failure_case in {"git-target-gitlink", "index-gitlink"}:
        _git(
            repository,
            "update-index",
            "--add",
            "--cacheinfo",
            f"160000,{base_target},package.json",
        )
        if failure_case == "git-target-gitlink":
            _git(repository, "commit", "--quiet", "-m", "gitlink candidate")
            source_kind = "git-target"
            target = _git(repository, "rev-parse", "HEAD")
    elif failure_case == "unmerged-index":
        object_id = _git(repository, "hash-object", "-w", "README.md")
        unmerged_entries = "".join(
            f"100644 {object_id} {stage}\tpackage.json\n" for stage in (1, 2, 3)
        ).encode()
        subprocess.run(
            ("git", "update-index", "--index-info"),  # noqa: S607
            cwd=repository,
            check=True,
            capture_output=True,
            input=unmerged_entries,
        )
        expected_diagnostic = "unmerged-index-entry"
    elif failure_case == "missing-index-object":
        _git(
            repository,
            "update-index",
            "--add",
            "--info-only",
            "--cacheinfo",
            f"100644,{'f' * 40},package.json",
        )
        expected_diagnostic = "git-blob-unreadable"
    elif failure_case == "worktree-file-symlink":
        package = repository / "package.json"
        package.write_bytes(b'{"name":"tracked-before-symlink"}\n')
        _git(repository, "add", "package.json")
        _git(repository, "commit", "--quiet", "-m", "tracked candidate")
        package.unlink()
        package.symlink_to("README.md")
        source_kind = "worktree"
        expected_diagnostic = "worktree-file-unsupported"
    else:
        nested = repository / "nested"
        nested.mkdir()
        package = nested / "package.json"
        package.write_bytes(b'{"name":"tracked-before-parent-symlink"}\n')
        _git(repository, "add", "nested/package.json")
        _git(repository, "commit", "--quiet", "-m", "tracked nested")
        shutil.rmtree(nested)
        outside = tmp_path / "outside-parent"
        outside.mkdir()
        (outside / "package.json").write_bytes(b'{"name":"outside"}\n')
        nested.symlink_to(outside, target_is_directory=True)
        source_kind = "worktree"
        expected_diagnostic = "worktree-parent-unsupported"
        expected_path = "nested/package.json"

    with pytest.raises(SourceAcquisitionError) as caught:
        acquire_static_reference_inventory(
            repository,
            source_kind=source_kind,  # type: ignore[arg-type]
            target=target,
        )

    assert caught.value.diagnostic_code == expected_diagnostic
    assert caught.value.path == expected_path
    assert str(caught.value) == (
        "static-reference source acquisition failed"
        if expected_path is None
        else f"{expected_path}: static-reference source acquisition failed"
    )

    authority_calls: list[object] = []
    session_calls: list[object] = []

    def forbidden_authority(*args: object, **kwargs: object) -> object:
        authority_calls.append((args, kwargs))
        message = "authority ran after exact-source failure"
        raise AssertionError(message)

    def forbidden_session() -> StaticReferenceSession:
        session_calls.append(object())
        message = "session opened after exact-source failure"
        raise AssertionError(message)

    result = scan_bounded_static_references(
        repository,
        source_kind=source_kind,  # type: ignore[arg-type]
        target=target,
        authority_runner=forbidden_authority,  # type: ignore[arg-type]
        session_factory=forbidden_session,
    )
    expected_document = {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": source_kind,
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
    }
    if target is not None:
        expected_document["target"] = target

    assert result.to_document() == expected_document
    assert authority_calls == []
    assert session_calls == []


@pytest.mark.parametrize(
    "source_kind",
    [
        pytest.param("git-target", id="git-target-100755"),
        pytest.param("index", id="index-100755"),
    ],
)
def test_source_accepts_an_executable_regular_blob(
    tmp_path: Path,
    source_kind: str,
) -> None:
    """Admit both bound regular Git blob modes without rewriting bytes."""
    import hashlib  # noqa: PLC0415

    repository = tmp_path / f"repository-{source_kind}"
    _initialize_repository(repository)
    content = b'{"name":"executable-regular-blob","private":true}\n'
    package = repository / "package.json"
    package.write_bytes(content)
    package.chmod(0o755)
    _git(repository, "add", "package.json")
    _git(repository, "commit", "--quiet", "-m", "executable regular blob")
    target = _git(repository, "rev-parse", "HEAD")
    object_id = _git(repository, "rev-parse", "HEAD:package.json")

    inventory = acquire_static_reference_inventory(
        repository,
        source_kind=source_kind,  # type: ignore[arg-type]
        target=target if source_kind == "git-target" else None,
    )

    assert (
        _git(
            repository,
            "ls-tree",
            "HEAD",
            "package.json",
        ).split()[0]
        == "100755"
    )
    assert (
        _git(
            repository,
            "ls-files",
            "--stage",
            "package.json",
        ).split()[0]
        == "100755"
    )
    assert inventory.source_kind == source_kind
    assert inventory.target == (target if source_kind == "git-target" else None)
    assert len(inventory.candidates) == 1
    assert inventory.candidates[0].path == "package.json"
    assert inventory.candidates[0].selection == StaticReferenceSelection(
        family="npm-manifest",
        graph_id="npm-manifest-v1",
        input_mode="strict-utf8-file",
    )
    assert inventory.candidates[0].content == content
    assert inventory.candidates[0].source_object == object_id
    assert inventory.candidates[0].content_sha256 == (
        f"sha256:{hashlib.sha256(content).hexdigest()}"
    )


@pytest.mark.parametrize(
    "source_kind",
    [
        pytest.param("git-target", id="git-target"),
        pytest.param("index", id="index"),
        pytest.param("worktree", id="worktree"),
    ],
)
def test_source_ignores_non_utf8_paths_outside_the_bounded_selector(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    source_kind: str,
) -> None:
    """Decode only raw paths whose exact basename can enter the inventory."""
    source = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    target = "a" * 40
    object_id = "b" * 40
    selected_path = b"nested/package.json"
    unrelated_path = b"unrelated-\xff.txt"
    excluded_path = b".github/workflows/pnpm-lock.yaml"
    content = b'{"name":"selected"}\n'

    def fake_git(_root: Path, *arguments: str) -> bytes:
        if arguments[0] == "rev-parse":
            return f"{target}\n".encode()
        if arguments[0] == "ls-tree":
            return b"".join(
                (
                    f"100644 blob {object_id}\t".encode(),
                    unrelated_path,
                    b"\0",
                    f"100644 blob {object_id}\t".encode(),
                    excluded_path,
                    b"\0",
                    f"100644 blob {object_id}\t".encode(),
                    selected_path,
                    b"\0",
                )
            )
        if arguments[:2] == ("ls-files", "--stage"):
            return b"".join(
                (
                    f"100644 {object_id} 0\t".encode(),
                    unrelated_path,
                    b"\0",
                    f"100644 {object_id} 0\t".encode(),
                    excluded_path,
                    b"\0",
                    f"100644 {object_id} 0\t".encode(),
                    selected_path,
                    b"\0",
                )
            )
        if arguments[:2] == ("ls-files", "--cached"):
            return b"\0".join(
                (unrelated_path, excluded_path, selected_path, b"")
            )
        if arguments[:2] == ("ls-files", "--others"):
            return b""
        if arguments[:2] == ("cat-file", "blob"):
            return content
        raise AssertionError(arguments)

    monkeypatch.setattr(source, "_git", fake_git)
    monkeypatch.setattr(
        source,
        "_read_worktree_candidate",
        lambda _root, path: content if path == "nested/package.json" else None,
    )

    inventory = {
        "git-target": lambda: source._git_target_inventory(  # noqa: SLF001
            tmp_path,
            target,
        ),
        "index": lambda: source._index_inventory(tmp_path),  # noqa: SLF001
        "worktree": lambda: source._worktree_inventory(  # noqa: SLF001
            tmp_path
        ),
    }[source_kind]()

    assert [candidate.path for candidate in inventory.candidates] == [
        "nested/package.json"
    ]
    assert [candidate.content for candidate in inventory.candidates] == [
        content
    ]


@pytest.mark.parametrize(
    "source_kind",
    [
        pytest.param("git-target", id="git-target"),
        pytest.param("index", id="index"),
        pytest.param("worktree", id="worktree"),
    ],
)
def test_source_rejects_non_utf8_parent_of_selected_basename(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    source_kind: str,
) -> None:
    """Keep strict path admission for every raw path that can be selected."""
    source = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    target = "a" * 40
    object_id = "b" * 40
    selected_path = b"\xff/package.json"

    def fake_git(_root: Path, *arguments: str) -> bytes:
        if arguments[0] == "rev-parse":
            return f"{target}\n".encode()
        if arguments[0] == "ls-tree":
            return f"100644 blob {object_id}\t".encode() + selected_path + b"\0"
        if arguments[:2] == ("ls-files", "--stage"):
            return f"100644 {object_id} 0\t".encode() + selected_path + b"\0"
        if arguments[:2] == ("ls-files", "--cached"):
            return selected_path + b"\0"
        if arguments[:2] == ("ls-files", "--others"):
            return b""
        raise AssertionError(arguments)

    monkeypatch.setattr(source, "_git", fake_git)

    with pytest.raises(SourceAcquisitionError) as caught:
        {
            "git-target": lambda: source._git_target_inventory(  # noqa: SLF001
                tmp_path,
                target,
            ),
            "index": lambda: source._index_inventory(  # noqa: SLF001
                tmp_path
            ),
            "worktree": lambda: source._worktree_inventory(  # noqa: SLF001
                tmp_path
            ),
        }[source_kind]()

    assert caught.value.diagnostic_code == "git-path-rejected"
    assert caught.value.path is None


def test_worktree_selects_present_tracked_and_eligible_untracked_only(
    tmp_path: Path,
) -> None:
    """Include eligible untracked bytes while omitting ignored/deleted paths."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    tracked = repository / "tracked" / "package.json"
    absent = repository / "absent" / "package.json"
    for path, content in (
        (tracked, b'{"name":"present-tracked"}\n'),
        (absent, b'{"name":"later-absent"}\n'),
    ):
        path.parent.mkdir()
        path.write_bytes(content)
    (repository / ".gitignore").write_text("ignored/\n", encoding="utf-8")
    _git(repository, "add", ".gitignore", "tracked", "absent")
    _git(repository, "commit", "--quiet", "-m", "tracked worktree inputs")
    absent.unlink()

    eligible = repository / "eligible" / "package.json"
    eligible.parent.mkdir()
    eligible.write_bytes(b'{"name":"eligible-untracked"}\n')
    ignored = repository / "ignored" / "package.json"
    ignored.parent.mkdir()
    ignored.write_bytes(b'{"name":"ignored-untracked"}\n')

    inventory = acquire_static_reference_inventory(
        repository,
        source_kind="worktree",
    )

    assert [candidate.path for candidate in inventory.candidates] == [
        "eligible/package.json",
        "tracked/package.json",
    ]
    assert [candidate.content for candidate in inventory.candidates] == [
        b'{"name":"eligible-untracked"}\n',
        b'{"name":"present-tracked"}\n',
    ]
    assert all(
        candidate.source_object is None for candidate in inventory.candidates
    )
    assert [
        path
        for path in _git(
            repository,
            "ls-files",
            "--others",
            "--exclude-standard",
            "-z",
        ).split("\0")
        if path
    ] == ["eligible/package.json"]
    assert ignored.read_bytes() == b'{"name":"ignored-untracked"}\n'
    assert not absent.exists()


def test_git_source_ignores_poisoned_index_and_replace_refs(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Isolate exact Git reads from ambient index and replace-ref state."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    package = repository / "package.json"
    original = b'{"name":"original-target"}\n'
    replacement = b'{"name":"replacement-target"}\n'
    package.write_bytes(original)
    _git(repository, "add", "package.json")
    _git(repository, "commit", "--quiet", "-m", "original")
    original_target = _git(repository, "rev-parse", "HEAD")
    original_object = _git(repository, "rev-parse", "HEAD:package.json")

    alternate_index = tmp_path / "poisoned-index"
    alternate_environment = os.environ.copy()
    alternate_environment["GIT_INDEX_FILE"] = str(alternate_index)
    subprocess.run(  # noqa: S603
        ("git", "read-tree", original_target),  # noqa: S607
        cwd=repository,
        env=alternate_environment,
        check=True,
        capture_output=True,
    )

    package.write_bytes(replacement)
    _git(repository, "add", "package.json")
    _git(repository, "commit", "--quiet", "-m", "replacement")
    replacement_target = _git(repository, "rev-parse", "HEAD")
    replacement_object = _git(repository, "rev-parse", "HEAD:package.json")
    _git(repository, "replace", original_target, replacement_target)
    default_replaced_tree = subprocess.run(  # noqa: S603
        ("git", "ls-tree", original_target, "package.json"),  # noqa: S607
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    assert replacement_object in default_replaced_tree
    assert original_object not in default_replaced_tree

    monkeypatch.setenv("GIT_INDEX_FILE", str(alternate_index))
    target_inventory = acquire_static_reference_inventory(
        repository,
        source_kind="git-target",
        target=original_target,
    )
    index_inventory = acquire_static_reference_inventory(
        repository,
        source_kind="index",
    )

    assert [
        (
            candidate.path,
            candidate.content,
            candidate.source_object,
        )
        for candidate in target_inventory.candidates
    ] == [("package.json", original, original_object)]
    assert [
        (
            candidate.path,
            candidate.content,
            candidate.source_object,
        )
        for candidate in index_inventory.candidates
    ] == [("package.json", replacement, replacement_object)]
    assert alternate_index.is_file()
    assert _git(repository, "replace", "-l") == original_target


@pytest.mark.parametrize(
    "source_kind",
    [
        pytest.param("git-target", id="git-target"),
        pytest.param("index", id="index"),
        pytest.param("worktree", id="worktree"),
    ],
)
def test_source_and_npm_candidate_order_uses_utf8_bytes_for_non_ascii_paths(
    tmp_path: Path,
    source_kind: str,
) -> None:
    """Order U+E000 before U+10000 for every source and npm graph run."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    repository = tmp_path / f"repository-{source_kind}"
    _initialize_repository(repository)
    expected_paths = [
        "\ue000/package.json",
        "\U00010000/package.json",
    ]
    for path, package_name in zip(
        reversed(expected_paths),
        ("non-bmp-package", "private-use-package"),
        strict=True,
    ):
        source = repository.joinpath(*path.split("/"))
        source.parent.mkdir()
        source.write_text(
            json.dumps({"name": package_name}) + "\n",
            encoding="utf-8",
        )
    _git(repository, "add", ".")
    _git(repository, "commit", "--quiet", "-m", "non-ascii candidates")
    target = _git(repository, "rev-parse", "HEAD")
    authority_calls: list[str] = []

    def run_authority(
        repository_root: Path,
        candidate: StaticReferenceCandidate,
        invocation: object,
        session: object,
    ) -> object:
        del repository_root, invocation, session
        authority_calls.append(candidate.path)
        return authority.AuthorityGraphOutcome(
            graph_id="npm-manifest-v1",
            implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
            facts=(),
        )

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        sessions.append(session)
        return session

    inventory = acquire_static_reference_inventory(
        repository,
        source_kind=source_kind,  # type: ignore[arg-type]
        target=target if source_kind == "git-target" else None,
    )
    result = policy.scan_bounded_static_references(
        repository,
        source_kind=source_kind,
        target=target if source_kind == "git-target" else None,
        authority_runner=run_authority,
        session_factory=session_factory,
    )

    assert [candidate.path for candidate in inventory.candidates] == (
        expected_paths
    )
    assert authority_calls == expected_paths
    assert result.result == "clean"
    assert result.error_kind is None
    assert result.findings == ()
    assert result.implementation_identities == _PHASE2_NPM_IMPLEMENTATIONS
    assert len(sessions) == 1
    assert not sessions[0].root.exists()


@pytest.mark.parametrize(
    "matrix_case",
    [
        pytest.param("nuget-lock-direct", id="nuget-lock-D-and-key"),
        pytest.param("nuget-lock-versioned", id="nuget-lock-V-and-key"),
        pytest.param("nuget-config-versioned", id="nuget-config-V"),
        pytest.param("pnpm-lock-workspace", id="pnpm-lock-W"),
        pytest.param("pnpm-lock-local", id="pnpm-lock-L"),
        pytest.param("pnpm-lock-key", id="pnpm-lock-dependency-key"),
        pytest.param("pnpm-workspace-alias", id="pnpm-workspace-A"),
        pytest.param("pnpm-workspace-version", id="pnpm-workspace-V"),
        pytest.param("pnpm-workspace-local", id="pnpm-workspace-L"),
        pytest.param(
            "pnpm-workspace-key",
            id="pnpm-workspace-dependency-key",
        ),
    ],
)
def test_selector_rows_project_only_their_assigned_prohibited_forms(
    matrix_case: str,
) -> None:
    """Pin the assigned selector-to-fact forms row by row."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    producer = "@hcoona/hcoona-release-smoke-npm"
    expected: list[tuple[str, str, str]]

    if matrix_case.startswith("nuget-lock"):
        candidate = _phase2_candidate(
            "consumer/packages.lock.json",
            family="nuget-lock",
            graph_id="nuget-lock-v1",
        )
        versioned = matrix_case == "nuget-lock-versioned"
        facts = (
            {
                "dependencies": [],
                "dependencyType": "Direct",
                "id": producer,
                "kind": "nuget-lock-dependency",
                "requestedRange": "[1.0.0, )" if versioned else None,
                "resolvedVersion": "1.2.3" if versioned else None,
                "target": "net10.0",
            },
        )
        context = f"targets.net10.0.{producer}"
        expected = [
            ("V" if versioned else "D", context, producer),
            ("dependency-key", context, producer),
        ]
    elif matrix_case == "nuget-config-versioned":
        candidate = _phase2_candidate(
            "consumer/packages.config",
            family="nuget-packages-config",
            graph_id="nuget-lock-v1",
        )
        facts = (
            {
                "id": producer,
                "kind": "nuget-packages-config-entry",
                "version": "1.2.3",
            },
        )
        expected = [("V", f"packages.{producer}", producer)]
    elif matrix_case.startswith("pnpm-lock"):
        candidate = _phase2_candidate(
            "consumer/pnpm-lock.yaml",
            family="pnpm-lock",
            graph_id="pnpm-lock-v1",
        )
        if matrix_case == "pnpm-lock-workspace":
            facts = (
                {
                    "dependencyKey": "producer-workspace-alias",
                    "importerId": ".",
                    "kind": "pnpm-lock-importer-reference",
                    "rawSpecifier": f"workspace:{producer}@*",
                    "registrySpec": {
                        "fetchSpec": "*",
                        "name": producer,
                        "type": "range",
                    },
                    "resolvedReference": "link:../producer",
                    "section": "dependencies",
                    "snapshotKey": None,
                    "workspaceSelector": "*",
                },
            )
            expected = [
                (
                    "W",
                    "importers...dependencies.producer-workspace-alias",
                    producer,
                )
            ]
        elif matrix_case == "pnpm-lock-local":
            facts = (
                {
                    "dependencies": [],
                    "dependencyPath": "local@file:../producer",
                    "kind": "pnpm-lock-snapshot",
                    "name": "local",
                    "nonSemverVersion": "file:../producer",
                    "registryName": None,
                    "resolution": {
                        "kind": "directory",
                        "localPath": _PHASE2_PRODUCER_ROOT,
                    },
                    "version": None,
                },
            )
            expected = [
                (
                    "L",
                    "packages.local@file:../producer",
                    _PHASE2_PRODUCER_ROOT,
                )
            ]
        else:
            facts = (
                {
                    "dependencies": [
                        {
                            "dependencyKey": producer,
                            "reference": "1.0.0",
                            "section": "optionalDependencies",
                        }
                    ],
                    "dependencyPath": "host@1.0.0",
                    "kind": "pnpm-lock-snapshot",
                    "name": "host",
                    "nonSemverVersion": None,
                    "registryName": "host",
                    "resolution": {"kind": "registry"},
                    "version": "1.0.0",
                },
            )
            expected = [
                (
                    "dependency-key",
                    (f"packages.host@1.0.0.optionalDependencies.{producer}"),
                    producer,
                )
            ]
    else:
        candidate = _phase2_candidate(
            "consumer/pnpm-workspace.yaml",
            family="pnpm-workspace",
            graph_id="pnpm-workspace-v1",
        )
        dependency_key = (
            producer
            if matrix_case == "pnpm-workspace-key"
            else matrix_case.removeprefix("pnpm-workspace-")
        )
        if matrix_case == "pnpm-workspace-alias":
            reference = {
                "kind": "npm",
                "npm": _phase2_npa_reference(
                    "alias",
                    "alias",
                    alias_target=_phase2_npa_reference(
                        producer,
                        "version",
                    ),
                ),
            }
            expected = [("A", "catalog.alias", producer)]
        elif matrix_case == "pnpm-workspace-version":
            reference = {
                "kind": "npm",
                "npm": _phase2_npa_reference(producer, "version"),
            }
            expected = [("V", "catalog.version", producer)]
        elif matrix_case == "pnpm-workspace-local":
            reference = {
                "kind": "npm",
                "npm": _phase2_npa_reference(
                    "local",
                    "directory",
                    local_path=_PHASE2_PRODUCER_ROOT,
                ),
            }
            expected = [("L", "catalog.local", _PHASE2_PRODUCER_ROOT)]
        else:
            reference = {
                "kind": "npm",
                "npm": _phase2_npa_reference("innocent", "version"),
            }
            expected = [("dependency-key", f"catalog.{producer}", producer)]
        facts = (
            {
                "catalogKind": "default",
                "catalogName": None,
                "dependencyKey": dependency_key,
                "kind": "pnpm-workspace-reference",
                "reference": reference,
                "sourceSpec": "1.2.3",
            },
        )

    findings = projection.project_static_reference_facts(candidate, facts)

    assert [finding.to_document() for finding in findings] == [
        {
            "path": candidate.path,
            "family": candidate.selection.family,
            "semantic-context": context,
            "prohibited-form": prohibited_form,
            "matched-identity": matched_identity,
        }
        for prohibited_form, context, matched_identity in expected
    ]
    assert len(findings) == len(expected)
    assert candidate.content


@pytest.mark.parametrize(
    "fact_kind",
    [
        pytest.param("lock-dependency", id="lock-dependency"),
        pytest.param("lock-edge", id="lock-edge"),
        pytest.param("packages-config", id="packages-config"),
    ],
)
@pytest.mark.parametrize(
    ("producer_variant", "expected_outcome"),
    [
        pytest.param(
            "@HCOONA/HCOONA-RELEASE-SMOKE-NPM",
            "match",
            id="ascii-case-variant",
        ),
        pytest.param(
            "@hcoona/hcoona-relea\u017fe-smoke-npm",
            "no-match",
            id="unicode-near-match",
        ),
    ],
)
def test_nuget_projection_uses_ordinal_ignore_case_producer_matching(
    fact_kind: str,
    producer_variant: str,
    expected_outcome: str,
) -> None:
    """Match the producer with NuGet package-ID comparison semantics."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    expected_match = expected_outcome == "match"
    if fact_kind == "packages-config":
        candidate = _phase2_candidate(
            "consumer/packages.config",
            family="nuget-packages-config",
            graph_id="nuget-lock-v1",
        )
        facts = (
            {
                "id": producer_variant,
                "kind": "nuget-packages-config-entry",
                "version": "1.2.3",
            },
        )
        expected_forms = ["V"] if expected_match else []
    else:
        candidate = _phase2_candidate(
            "consumer/packages.lock.json",
            family="nuget-lock",
            graph_id="nuget-lock-v1",
        )
        facts = (
            {
                "dependencies": (
                    [
                        {
                            "id": producer_variant,
                            "requestedRange": "[1.0.0, )",
                        }
                    ]
                    if fact_kind == "lock-edge"
                    else []
                ),
                "dependencyType": "Direct",
                "id": (
                    "ordinary-package"
                    if fact_kind == "lock-edge"
                    else producer_variant
                ),
                "kind": "nuget-lock-dependency",
                "requestedRange": None,
                "resolvedVersion": None,
                "target": "net10.0",
            },
        )
        expected_forms = (
            (
                ["D", "dependency-key"]
                if fact_kind == "lock-dependency"
                else ["V", "dependency-key"]
            )
            if expected_match
            else []
        )

    findings = projection.project_static_reference_facts(candidate, facts)

    assert [finding.prohibited_form for finding in findings] == expected_forms
    assert {finding.matched_identity for finding in findings} == (
        {PRODUCER_PACKAGE} if expected_match else set()
    )


def test_pnpm_projection_accepts_typed_hosted_git_resolution() -> None:
    """Admit the official hosted-Git fact without interpreting its URL."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    candidate = _phase2_candidate(
        "consumer/pnpm-lock.yaml",
        family="pnpm-lock",
        graph_id="pnpm-lock-v1",
    )
    facts = (
        {
            "dependencies": [],
            "dependencyPath": (
                "git-dependency@git+https://github.com/acme/repository.git"
                "#0123456789abcdef0123456789abcdef01234567"
            ),
            "kind": "pnpm-lock-snapshot",
            "name": "git-dependency",
            "nonSemverVersion": (
                "git+https://github.com/acme/repository.git"
                "#0123456789abcdef0123456789abcdef01234567"
            ),
            "registryName": None,
            "resolution": {
                "kind": "hosted-git",
                "path": "packages/library",
                "tarball": (
                    "https://codeload.github.com/acme/repository/tar.gz/"
                    "0123456789abcdef0123456789abcdef01234567"
                ),
            },
            "version": None,
        },
    )

    assert projection.project_static_reference_facts(candidate, facts) == ()


@pytest.mark.parametrize(
    ("candidate_path", "expected_findings"),
    [
        pytest.param(
            "src/public/lib/hcoona-release-smoke-npm/package.json",
            [],
            id="exact-producer-manifest-allowed",
        ),
        pytest.param(
            ("src/public/lib/hcoona-release-smoke-npm/adjacent/package.json"),
            [
                {
                    "path": (
                        "src/public/lib/hcoona-release-smoke-npm/"
                        "adjacent/package.json"
                    ),
                    "family": "npm-manifest",
                    "semantic-context": "name",
                    "prohibited-form": "D",
                    "matched-identity": ("@hcoona/hcoona-release-smoke-npm"),
                }
            ],
            id="adjacent-manifest-not-allowed",
        ),
    ],
)
def test_producer_name_allowance_is_exact_manifest_path_only(
    candidate_path: str,
    expected_findings: list[dict[str, str]],
) -> None:
    """Allow the producer name only in its exact top-level manifest."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    candidate = _phase2_candidate(
        candidate_path,
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    facts = (
        {
            "context": "name",
            "kind": "npm-package-name",
            "name": "@hcoona/hcoona-release-smoke-npm",
        },
    )

    findings = projection.project_static_reference_facts(candidate, facts)

    assert [finding.to_document() for finding in findings] == (
        expected_findings
    )
    assert candidate.path == candidate_path


@pytest.mark.parametrize(
    "allowance_case",
    [
        pytest.param("workspace-pattern", id="workspace-producer-root"),
        pytest.param("importer-id", id="importer-producer-root"),
    ],
)
def test_producer_root_outside_dependency_positions_is_clean(
    allowance_case: str,
) -> None:
    """Keep legitimate workspace/importer producer-root roles clean."""
    projection = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_projection"
    )
    if allowance_case == "workspace-pattern":
        candidate = _phase2_candidate(
            "pnpm-workspace.yaml",
            family="pnpm-workspace",
            graph_id="pnpm-workspace-v1",
        )
        facts = (
            {
                "index": 0,
                "kind": "pnpm-workspace-pattern",
                "pattern": _PHASE2_PRODUCER_ROOT,
            },
        )
    else:
        candidate = _phase2_candidate(
            "pnpm-lock.yaml",
            family="pnpm-lock",
            graph_id="pnpm-lock-v1",
        )
        facts = (
            {
                "dependencies": [],
                "dependencyPath": "innocent@1.0.0",
                "kind": "pnpm-lock-snapshot",
                "name": "innocent",
                "nonSemverVersion": None,
                "registryName": "innocent",
                "resolution": {"kind": "registry"},
                "version": "1.0.0",
            },
            {
                "dependencyKey": "innocent",
                "importerId": _PHASE2_PRODUCER_ROOT,
                "kind": "pnpm-lock-importer-reference",
                "rawSpecifier": "1.0.0",
                "registrySpec": {
                    "fetchSpec": "1.0.0",
                    "name": "innocent",
                    "type": "version",
                },
                "resolvedReference": "1.0.0",
                "section": "dependencies",
                "snapshotKey": "innocent@1.0.0",
                "workspaceSelector": None,
            },
        )

    findings = projection.project_static_reference_facts(candidate, facts)

    assert findings == ()
    assert candidate.selection.family in {"pnpm-lock", "pnpm-workspace"}


@pytest.mark.parametrize(
    "terminal_case",
    [
        pytest.param("source", id="source-acquisition-failed"),
        pytest.param("encoding", id="encoding-rejected"),
        pytest.param("authority-rejected", id="authority-rejected"),
        pytest.param("execution", id="authority-execution-failed"),
        pytest.param("unsupported", id="unsupported-projection"),
        pytest.param("mismatch-missing", id="authority-mismatch-missing"),
        pytest.param("mismatch-extra", id="authority-mismatch-extra"),
        pytest.param("cleanup", id="cleanup-failed"),
    ],
)
def test_policy_routes_every_terminal_error_without_partial_findings(  # noqa: C901, PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    terminal_case: str,
) -> None:
    """Route every terminal kind, stop once, and clean exact roots."""
    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidates = (
        _phase2_candidate(
            "00/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
        ),
        _phase2_candidate(
            "01/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
        ),
        _phase2_candidate(
            "02/package.json",
            family="npm-manifest",
            graph_id="npm-manifest-v1",
        ),
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="worktree",
        target=None,
        candidates=candidates,
    )
    acquisition_calls: list[tuple[Path, str, str | None]] = []
    source_error: SourceAcquisitionError | None = None
    if terminal_case == "source":
        non_repository = tmp_path / "not-a-repository"
        _initialize_repository(non_repository)
        with pytest.raises(SourceAcquisitionError) as caught:
            acquire_static_reference_inventory(
                non_repository,
                source_kind="git-target",
                target=TARGET,
            )
        source_error = caught.value

    def acquire(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None = None,
    ) -> object:
        acquisition_calls.append((repository_root, source_kind, target))
        if source_error is not None:
            raise source_error
        return inventory

    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        acquire,
    )
    authority_calls: list[tuple[str, Path]] = []

    def run_authority(
        repository_root: Path,
        candidate: StaticReferenceCandidate,
        invocation: object,
        session: object,
    ) -> object:
        del repository_root, session
        authority_calls.append((candidate.path, invocation.root))
        if candidate.path == "00/package.json":
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
                facts=(
                    {
                        "context": "name",
                        "kind": "npm-package-name",
                        "name": "@hcoona/hcoona-release-smoke-npm",
                    },
                ),
            )
        if candidate.path != "01/package.json":
            message = "policy continued after terminal result"
            raise AssertionError(message)
        if terminal_case == "execution":
            message = "injected authority timeout"
            raise authority.AuthorityExecutionError(message)
        if terminal_case == "mismatch-missing":
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=("node@24.19.0",),
                facts=(),
            )
        if terminal_case == "mismatch-extra":
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=("foreign-authority@9.9.9",),
                facts=(),
            )
        error_kind = {
            "encoding": "encoding-rejected",
            "authority-rejected": "authority-rejected",
            "unsupported": "unsupported-projection",
        }.get(terminal_case)
        if error_kind is not None:
            return authority.AuthorityGraphOutcome(
                graph_id="npm-manifest-v1",
                implementation_identities=("node@24.19.0",),
                facts=(),
                error_kind=error_kind,
            )
        return authority.AuthorityGraphOutcome(
            graph_id="npm-manifest-v1",
            implementation_identities=_PHASE2_NPM_IMPLEMENTATIONS,
            facts=(
                {
                    "context": "name",
                    "kind": "npm-package-name",
                    "name": "@hcoona/hcoona-release-smoke-npm",
                },
            ),
        )

    cleanup_calls: list[Path] = []

    def cleanup(path: Path) -> None:
        cleanup_calls.append(path)
        if path.exists():
            shutil.rmtree(path)
        if terminal_case == "cleanup" and path.name == "invocation-0001":
            message = "injected exact-root cleanup failure"
            raise OSError(message)

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path, cleanup=cleanup)
        sessions.append(session)
        return session

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="worktree",
        authority_runner=run_authority,
        session_factory=session_factory,
    )
    expected_error = {
        "source": "source-acquisition-failed",
        "encoding": "encoding-rejected",
        "authority-rejected": "authority-rejected",
        "execution": "authority-execution-failed",
        "unsupported": "unsupported-projection",
        "mismatch-missing": "authority-mismatch",
        "mismatch-extra": "authority-mismatch",
        "cleanup": "cleanup-failed",
    }[terminal_case]
    expected_identities = (
        ()
        if terminal_case == "source"
        else tuple(
            sorted(
                {
                    *_PHASE2_NPM_IMPLEMENTATIONS,
                    *(
                        ("foreign-authority@9.9.9",)
                        if terminal_case == "mismatch-extra"
                        else ()
                    ),
                },
                key=lambda identity: identity.encode(),
            )
        )
    )

    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "worktree",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": list(expected_identities),
        "findings": [],
        "error-kind": expected_error,
    }
    assert result.findings == ()
    assert acquisition_calls == [(tmp_path, "worktree", None)]
    assert [path for path, _ in authority_calls] == (
        []
        if terminal_case == "source"
        else ["00/package.json", "01/package.json"]
    )
    assert len(sessions) == (0 if terminal_case == "source" else 1)
    if terminal_case == "source":
        assert cleanup_calls == []
    else:
        assert cleanup_calls == [
            authority_calls[0][1],
            authority_calls[1][1],
            sessions[0].root / "invocation-0002",
            sessions[0].root,
        ]
        assert all(not path.exists() for path in cleanup_calls)


@pytest.mark.parametrize(
    "terminal_case",
    [
        pytest.param("timeout", id="timeout"),
        pytest.param("timeout-cleanup-failure", id="timeout-cleanup-override"),
        pytest.param("keyboard-interrupt", id="keyboard-interrupt"),
        pytest.param("cancelled", id="cancellation"),
    ],
)
def test_policy_timeout_and_cancellation_always_clean_exact_roots(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    terminal_case: str,
) -> None:
    """Normalize timeout, propagate cancellation, and always clean exactly."""
    import asyncio  # noqa: PLC0415

    authority = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_authority"
    )
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    candidate = _phase2_candidate(
        "candidate/package.json",
        family="npm-manifest",
        graph_id="npm-manifest-v1",
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=(candidate,),
    )
    monkeypatch.setattr(
        policy,
        "acquire_static_reference_inventory",
        lambda repository_root, *, source_kind, target=None: inventory,  # noqa: ARG005
    )
    process_calls: list[tuple[tuple[str, ...], dict[str, object]]] = []

    def fail_process(
        command: tuple[str, ...],
        **kwargs: object,
    ) -> object:
        process_calls.append((tuple(command), kwargs))
        if terminal_case.startswith("timeout"):
            raise subprocess.TimeoutExpired(
                command,
                kwargs["timeout"],  # type: ignore[arg-type]
            )
        if terminal_case == "keyboard-interrupt":
            message = "injected keyboard interrupt"
            raise KeyboardInterrupt(message)
        message = "injected cancellation"
        raise asyncio.CancelledError(message)

    monkeypatch.setattr(authority.subprocess, "run", fail_process)
    cleanup_calls: list[Path] = []

    def cleanup(path: Path) -> None:
        cleanup_calls.append(path)
        if path.exists():
            shutil.rmtree(path)
        if (
            terminal_case == "timeout-cleanup-failure"
            and path.name == "invocation-0000"
        ):
            message = "cleanup overrides timeout"
            raise OSError(message)

    invocation_roots: list[Path] = []

    class RecordingSession(StaticReferenceSession):
        def materialize(
            self,
            selected_candidate: StaticReferenceCandidate,
            *,
            source_kind: str,
            target: str | None,
        ) -> object:
            invocation = super().materialize(
                selected_candidate,
                source_kind=source_kind,  # type: ignore[arg-type]
                target=target,
            )
            invocation_roots.append(invocation.root)
            return invocation

    sessions: list[RecordingSession] = []

    def session_factory() -> RecordingSession:
        session = RecordingSession(parent=tmp_path, cleanup=cleanup)
        sessions.append(session)
        return session

    if terminal_case in {"keyboard-interrupt", "cancelled"}:
        exception_type = (
            KeyboardInterrupt
            if terminal_case == "keyboard-interrupt"
            else asyncio.CancelledError
        )
        with pytest.raises(exception_type):
            policy.scan_bounded_static_references(
                REPO_ROOT,
                source_kind="index",
                authority_runner=authority.run_authority_graph,
                session_factory=session_factory,
            )
    else:
        result = policy.scan_bounded_static_references(
            REPO_ROOT,
            source_kind="index",
            authority_runner=authority.run_authority_graph,
            session_factory=session_factory,
        )
        expected_error = (
            "cleanup-failed"
            if terminal_case == "timeout-cleanup-failure"
            else "authority-execution-failed"
        )
        assert result.to_document() == {
            "schema": "workflow-delivery/v3/bounded-static-reference-result",
            "result": "error",
            "source-kind": "index",
            "policy-id": STATIC_REFERENCE_POLICY_ID,
            "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
            "implementation-identities": [],
            "findings": [],
            "error-kind": expected_error,
        }

    assert len(process_calls) == 1
    command, kwargs = process_calls[0]
    assert Path(command[0]).name == "node"
    assert command[1] == str(
        REPO_ROOT / "eng/scripts/workflow_delivery_v3_static_reference_node.mjs"
    )
    assert kwargs["cwd"] == invocation_roots[0]
    assert kwargs["timeout"] == 30  # noqa: PLR2004
    request = json.loads(kwargs["input"])  # type: ignore[arg-type]
    assert request == {
        "schema": (
            "workflow-delivery/v3/static-reference-node-authority-request"
        ),
        "graph": "npm-manifest-v1",
        "snapshotRoot": str(invocation_roots[0] / "snapshot"),
        "candidatePath": str(
            invocation_roots[0] / "snapshot/candidate/package.json"
        ),
        "logicalPath": "candidate/package.json",
    }
    assert len(sessions) == 1
    assert cleanup_calls == [invocation_roots[0], sessions[0].root]
    assert all(not path.exists() for path in cleanup_calls)


@pytest.mark.parametrize(
    "admission_case",
    [
        pytest.param("missing-target", id="missing-target"),
        pytest.param("target-39", id="target-39-characters"),
        pytest.param("target-41", id="target-41-characters"),
        pytest.param("target-uppercase", id="target-uppercase"),
        pytest.param("missing-root", id="nonexistent-repository-root"),
        pytest.param("file-root", id="file-repository-root"),
    ],
)
def test_invocation_boundary_rejects_exact_invalid_target_and_root_forms(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    admission_case: str,
) -> None:
    """Reject malformed requests before Result, scan, or Session allocation."""
    module = _load_static_reference_script()
    repository = tmp_path / "repository"
    repository.mkdir()
    root_argument = repository
    arguments = [
        str(STATIC_REFERENCE_SCRIPT),
        "--repository-root",
        str(root_argument),
        "--source-kind",
        "git-target",
    ]
    expected_diagnostic = "--target is required for git-target"
    if admission_case == "target-39":
        arguments.extend(("--target", "a" * 39))
        expected_diagnostic = "target must be a full lowercase commit SHA"
    elif admission_case == "target-41":
        arguments.extend(("--target", "a" * 41))
        expected_diagnostic = "target must be a full lowercase commit SHA"
    elif admission_case == "target-uppercase":
        arguments.extend(("--target", "A" * 40))
        expected_diagnostic = "target must be a full lowercase commit SHA"
    elif admission_case == "missing-root":
        arguments = [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(tmp_path / "does-not-exist"),
            "--source-kind",
            "worktree",
        ]
        expected_diagnostic = (
            "--repository-root must identify an existing directory"
        )
    elif admission_case == "file-root":
        root_file = tmp_path / "not-a-directory"
        root_file.write_bytes(b"not a repository root\n")
        arguments = [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(root_file),
            "--source-kind",
            "index",
        ]
        expected_diagnostic = (
            "--repository-root must identify an existing directory"
        )
    before = sorted(path.name for path in tmp_path.iterdir())
    scan_calls: list[object] = []

    def forbidden_scan(*args: object, **kwargs: object) -> object:
        scan_calls.append((args, kwargs))
        message = "scan ran before request admission"
        raise AssertionError(message)

    monkeypatch.setattr(
        module,
        "scan_bounded_static_references",
        forbidden_scan,
    )
    monkeypatch.setattr(sys, "argv", arguments)

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert expected_diagnostic in captured.err
    assert "bounded-static-reference-result" not in captured.err
    assert scan_calls == []
    assert sorted(path.name for path in tmp_path.iterdir()) == before
    assert not any(
        path.name.startswith("wdv3-static-reference-")
        for path in tmp_path.iterdir()
    )


def test_nested_repository_root_is_rejected_before_result_or_session(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Do not let Git cwd scoping narrow the repository inventory."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    nested = repository / "nested"
    nested.mkdir()

    with pytest.raises(
        ValueError,
        match="exact Git worktree root",
    ):
        acquire_static_reference_inventory(
            nested,
            source_kind="worktree",
        )

    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )
    allocation_calls: list[object] = []

    def forbidden_allocation(*args: object, **kwargs: object) -> object:
        allocation_calls.append((args, kwargs))
        message = "Session allocated for an invalid repository root"
        raise AssertionError(message)

    monkeypatch.setattr(
        session_module.tempfile,
        "mkdtemp",
        forbidden_allocation,
    )
    module = _load_static_reference_script()
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(nested),
            "--source-kind",
            "worktree",
        ],
    )

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert (
        "--repository-root must identify the exact Git worktree root"
        in captured.err
    )
    assert "bounded-static-reference-result" not in captured.err
    assert allocation_calls == []


def test_non_git_repository_root_is_rejected_before_result_or_session(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Reject a directory without a root Git control marker at admission."""
    repository = tmp_path / "repository"
    repository.mkdir()

    with pytest.raises(
        InvalidRepositoryRootError,
        match="exact Git worktree root",
    ):
        acquire_static_reference_inventory(
            repository,
            source_kind="worktree",
        )

    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )
    allocation_calls: list[object] = []

    def forbidden_allocation(*args: object, **kwargs: object) -> object:
        allocation_calls.append((args, kwargs))
        message = "Session allocated for a non-Git repository root"
        raise AssertionError(message)

    monkeypatch.setattr(
        session_module.tempfile,
        "mkdtemp",
        forbidden_allocation,
    )
    module = _load_static_reference_script()
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(repository),
            "--source-kind",
            "worktree",
        ],
    )

    with pytest.raises(SystemExit) as caught:
        module.main()

    captured = capsys.readouterr()
    assert caught.value.code == 2  # noqa: PLR2004
    assert captured.out == ""
    assert (
        "--repository-root must identify the exact Git worktree root"
        in captured.err
    )
    assert "bounded-static-reference-result" not in captured.err
    assert allocation_calls == []


@pytest.mark.skipif(
    os.name == "nt",
    reason="Windows cannot create these trailing-whitespace directory names",
)
@pytest.mark.parametrize(
    "suffix",
    [
        pytest.param(" ", id="space"),
        pytest.param("\t", id="tab"),
        pytest.param("\n", id="line-feed"),
        pytest.param("\r", id="carriage-return"),
    ],
)
def test_repository_root_preserves_trailing_whitespace(
    tmp_path: Path,
    suffix: str,
) -> None:
    """Remove only Git's output terminator from the exact root path."""
    repository = tmp_path / f"repository{suffix}"
    _initialize_repository(repository)

    assert resolve_static_reference_repository_root(repository) == (
        repository.resolve(strict=True)
    )


@pytest.mark.parametrize(
    "diagnostic_name",
    [
        pytest.param(
            "GIT_EXECUTABLE_UNAVAILABLE",
            id="git-executable-unavailable",
        ),
        pytest.param("GIT_COMMAND_FAILED", id="git-command-failed"),
    ],
)
def test_cli_operational_git_failure_emits_source_failure_result(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    diagnostic_name: str,
) -> None:
    """Keep admitted Git acquisition failures inside the Result contract."""
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    source_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    diagnostic = getattr(
        source_module._SourceDiagnostic,  # noqa: SLF001
        diagnostic_name,
    )

    def fail_git(*args: object, **kwargs: object) -> bytes:
        del args, kwargs
        raise SourceAcquisitionError(diagnostic)

    monkeypatch.setattr(source_module, "_git", fail_git)
    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )
    allocation_calls: list[object] = []

    def forbidden_allocation(*args: object, **kwargs: object) -> object:
        allocation_calls.append((args, kwargs))
        message = "Session allocated after Git acquisition failed"
        raise AssertionError(message)

    monkeypatch.setattr(
        session_module.tempfile,
        "mkdtemp",
        forbidden_allocation,
    )
    module = _load_static_reference_script()
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(repository),
            "--source-kind",
            "worktree",
        ],
    )

    exit_code = module.main()

    captured = capsys.readouterr()
    assert exit_code == 2  # noqa: PLR2004
    assert captured.err == ""
    assert json.loads(captured.out) == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "worktree",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
    }
    assert allocation_calls == []


def test_invocation_admitted_missing_object_emits_source_failure_result(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """Convert an admitted exact missing object into one canonical Result."""
    session_module = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_session"
    )
    repository = tmp_path / "repository"
    _initialize_repository(repository)
    (repository / "README.md").write_bytes(b"base\n")
    _git(repository, "add", "README.md")
    _git(repository, "commit", "--quiet", "-m", "base")
    missing_target = "f" * 40
    allocation_calls: list[object] = []

    def forbidden_allocation(*args: object, **kwargs: object) -> object:
        allocation_calls.append((args, kwargs))
        message = "Session allocated after source failure"
        raise AssertionError(message)

    monkeypatch.setattr(
        session_module.tempfile,
        "mkdtemp",
        forbidden_allocation,
    )
    module = _load_static_reference_script()
    monkeypatch.setattr(
        sys,
        "argv",
        [
            str(STATIC_REFERENCE_SCRIPT),
            "--repository-root",
            str(repository),
            "--source-kind",
            "git-target",
            "--target",
            missing_target,
        ],
    )

    exit_code = module.main()

    captured = capsys.readouterr()
    assert exit_code == 2  # noqa: PLR2004
    assert captured.err == ""
    assert json.loads(captured.out) == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "git-target",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
        "target": missing_target,
    }
    assert captured.out.encode() == canonicalize(json.loads(captured.out))
    assert allocation_calls == []


@pytest.mark.parametrize(
    ("path", "content"),
    [
        pytest.param(
            "docs/static-reference.md",
            b"@hcoona/hcoona-release-smoke-npm\n",
            id="documentation",
        ),
        pytest.param(
            "setup.py",
            b'install_requires=["@hcoona/hcoona-release-smoke-npm"]\n',
            id="setup-py",
        ),
        pytest.param(
            "requirements-release.txt",
            b"@hcoona/hcoona-release-smoke-npm==1.0.0\n",
            id="requirements",
        ),
        pytest.param(
            "poetry.lock",
            b'name = "@hcoona/hcoona-release-smoke-npm"\n',
            id="poetry-lock",
        ),
        pytest.param(
            "bun.lockb",
            b"\x00@hcoona/hcoona-release-smoke-npm\x00",
            id="bun",
        ),
        pytest.param(
            ".npmrc",
            b"producer=@hcoona/hcoona-release-smoke-npm\n",
            id="npmrc",
        ),
        pytest.param(
            ".yarnrc.yml",
            b"producer: @hcoona/hcoona-release-smoke-npm\n",
            id="yarnrc",
        ),
        pytest.param(
            "renovate.json",
            b'{"package":"@hcoona/hcoona-release-smoke-npm"}\n',
            id="renovate",
        ),
        pytest.param(
            ".github/dependabot.yml",
            b"package: @hcoona/hcoona-release-smoke-npm\n",
            id="dependabot",
        ),
        pytest.param(
            ".pnpmfile.cjs",
            b"module.exports='@hcoona/hcoona-release-smoke-npm';\n",
            id="pnpmfile",
        ),
        pytest.param(
            "eng/install.bat",
            b"npm install @hcoona/hcoona-release-smoke-npm\r\n",
            id="batch",
        ),
        pytest.param(
            "eng/install.zsh",
            b"npm install @hcoona/hcoona-release-smoke-npm\n",
            id="zsh",
        ),
        pytest.param(
            "src/ordinary.js",
            b'import "@hcoona/hcoona-release-smoke-npm";\n',
            id="ordinary-javascript",
        ),
        pytest.param(
            "src/ordinary.ts",
            b'import "@hcoona/hcoona-release-smoke-npm";\n',
            id="ordinary-typescript",
        ),
        pytest.param(
            "src/ordinary.py",
            b'PACKAGE = "@hcoona/hcoona-release-smoke-npm"\n',
            id="ordinary-python",
        ),
    ],
)
def test_remaining_excluded_categories_select_no_graph_or_runner(
    tmp_path: Path,
    path: str,
    content: bytes,
) -> None:
    """Keep every remaining excluded category outside the bounded graph."""
    from three_workflow_delivery_v3.release.static_reference_policy import (  # noqa: PLC0415
        scan_bounded_static_references,
    )
    from three_workflow_delivery_v3.release.static_reference_source import (  # noqa: PLC0415
        select_static_reference_path,
    )

    repository = tmp_path / "repository"
    _initialize_repository(repository)
    source = repository.joinpath(*path.split("/"))
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_bytes(content)
    runner_calls: list[object] = []

    def forbidden_runner(*args: object, **kwargs: object) -> object:
        runner_calls.append((args, kwargs))
        message = "excluded category invoked an authority"
        raise AssertionError(message)

    sessions: list[StaticReferenceSession] = []

    def session_factory() -> StaticReferenceSession:
        session = StaticReferenceSession(parent=tmp_path)
        sessions.append(session)
        return session

    inventory = acquire_static_reference_inventory(
        repository,
        source_kind="worktree",
    )
    result = scan_bounded_static_references(
        repository,
        source_kind="worktree",
        authority_runner=forbidden_runner,  # type: ignore[arg-type]
        session_factory=session_factory,
    )

    assert select_static_reference_path(path) is None
    assert inventory.candidates == ()
    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "clean",
        "source-kind": "worktree",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
    }
    assert runner_calls == []
    assert len(sessions) == 1
    assert not sessions[0].root.exists()
    assert source.read_bytes() == content


def test_fixture_is_non_normative_and_production_manifest_is_exact(
    tmp_path: Path,
) -> None:
    """Exclude test-local fixture shape from the production-manifest oracle."""
    from three_workflow_delivery_v3.release.static_reference_model import (  # noqa: PLC0415
        PRODUCER_MANIFEST,
        PRODUCER_PACKAGE,
    )
    from three_workflow_delivery_v3.release.static_reference_source import (  # noqa: PLC0415
        select_static_reference_path,
    )

    tracked_paths = [
        path for path in _git(REPO_ROOT, "ls-files", "-z").split("\0") if path
    ]
    fixture_prefix = (
        "src/public/lib/three-workflow-delivery-v3/tests/fixtures/"
        "acceptance/npm-publish-request/"
    )
    fixture_source = f"{fixture_prefix}package/package-manifest.json"
    fixture_package_json = f"{fixture_prefix}package/package.json"
    fixture_sources: list[str] = []
    selected_producer_manifests: list[str] = []
    for path in tracked_paths:
        if not path.endswith(".json"):
            continue
        try:
            document = json.loads(_git(REPO_ROOT, "show", f":{path}"))
        except json.JSONDecodeError:
            continue
        if not isinstance(document, dict):
            continue
        if (
            path.startswith(fixture_prefix)
            and document.get("name") == PRODUCER_PACKAGE
        ):
            fixture_sources.append(path)
        if (
            PurePosixPath(path).name == "package.json"
            and document.get("name") == PRODUCER_PACKAGE
        ):
            selected_producer_manifests.append(path)

    assert fixture_sources == [fixture_source]
    observed = {
        "fixture-source-is-tracked": fixture_source in tracked_paths,
        "fixture-source-has-candidate-basename": (
            PurePosixPath(fixture_source).name == "package.json"
        ),
        "fixture-source-selects-a-graph": (
            select_static_reference_path(fixture_source) is not None
        ),
        "fixture-package-json-is-tracked": fixture_package_json
        in tracked_paths,
        "fixture-package-json-exists": REPO_ROOT.joinpath(
            *PurePosixPath(fixture_package_json).parts
        ).exists(),
        "normative-producer-manifests": sorted(
            (
                path
                for path in selected_producer_manifests
                if not path.startswith(fixture_prefix)
            ),
            key=lambda path: path.encode(),
        ),
    }
    assert observed == {
        "fixture-source-is-tracked": True,
        "fixture-source-has-candidate-basename": False,
        "fixture-source-selects-a-graph": False,
        "fixture-package-json-is-tracked": False,
        "fixture-package-json-exists": False,
        "normative-producer-manifests": [PRODUCER_MANIFEST],
    }

    fixture_source_path = REPO_ROOT.joinpath(
        *PurePosixPath(fixture_source).parts
    )
    tracked_fixture_bytes = fixture_source_path.read_bytes()
    _git(tmp_path, "init", "--quiet")
    materialized_manifest = tmp_path / "package/package.json"
    materialized_manifest.parent.mkdir()
    materialized_manifest.write_bytes(tracked_fixture_bytes)

    inventory = acquire_static_reference_inventory(
        tmp_path,
        source_kind="worktree",
    )

    assert tuple(candidate.path for candidate in inventory.candidates) == (
        "package/package.json",
    )
    materialized_candidate = inventory.candidates[0]
    assert materialized_manifest.read_bytes() == tracked_fixture_bytes
    assert materialized_candidate.content == tracked_fixture_bytes
    assert materialized_candidate.selection == StaticReferenceSelection(
        family="npm-manifest",
        graph_id="npm-manifest-v1",
        input_mode="strict-utf8-file",
    )
    assert materialized_candidate.source_object is None


def test_git_sources_ignore_all_ambient_repository_object_and_config_redirects(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Strip ambient Git redirects while retaining declared source bytes."""
    source = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    repository = tmp_path / "declared-repository"
    _initialize_repository(repository)
    manifest = repository / "package.json"
    committed = b'{"name":"declared-committed"}\n'
    indexed = b'{"name":"declared-indexed"}\n'
    worktree = b'{"name":"declared-worktree"}\n'
    manifest.write_bytes(committed)
    _git(repository, "add", "package.json")
    _git(repository, "commit", "--quiet", "-m", "declared source")
    target = _git(repository, "rev-parse", "HEAD")
    committed_object = _git(repository, "rev-parse", "HEAD:package.json")
    manifest.write_bytes(indexed)
    _git(repository, "add", "package.json")
    indexed_object = _git(repository, "rev-parse", ":package.json")
    manifest.write_bytes(worktree)

    poisoned_repository = tmp_path / "poisoned-repository"
    _initialize_repository(poisoned_repository)
    poisoned_manifest = poisoned_repository / "package.json"
    poisoned_manifest.write_bytes(b'{"name":"ambient-poison"}\n')
    _git(poisoned_repository, "add", "package.json")
    _git(poisoned_repository, "commit", "--quiet", "-m", "ambient poison")
    poisoned_config = tmp_path / "poisoned.gitconfig"
    poisoned_config.write_text("[core]\n\tbare = true\n", encoding="utf-8")

    redirect_values = {
        "GIT_ALTERNATE_OBJECT_DIRECTORIES": str(
            poisoned_repository / ".git" / "objects"
        ),
        "GIT_COMMON_DIR": str(poisoned_repository / ".git"),
        "GIT_CONFIG_COUNT": "1",
        "GIT_CONFIG_PARAMETERS": "malformed-ambient-parameters",
        "GIT_DIR": str(poisoned_repository / ".git"),
        "GIT_INDEX_FILE": str(poisoned_repository / ".git" / "index"),
        "GIT_NAMESPACE": "ambient-poison",
        "GIT_OBJECT_DIRECTORY": str(poisoned_repository / ".git" / "objects"),
        "GIT_WORK_TREE": str(poisoned_repository),
    }
    for name, value in redirect_values.items():
        monkeypatch.setenv(name, value)
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", str(poisoned_config))
    monkeypatch.setenv("GIT_CONFIG_NOSYSTEM", "0")
    monkeypatch.setenv("GIT_NO_LAZY_FETCH", "0")

    real_run = subprocess.run
    git_calls: list[tuple[tuple[str, ...], Path, dict[str, str]]] = []

    def recording_run(command: object, **kwargs: object) -> object:
        assert isinstance(command, tuple)
        assert all(isinstance(argument, str) for argument in command)
        environment = kwargs["env"]
        working_directory = kwargs["cwd"]
        assert isinstance(environment, dict)
        assert isinstance(working_directory, Path)
        git_calls.append(
            (
                command,
                working_directory,
                dict(environment),
            )
        )
        return real_run(command, **kwargs)  # type: ignore[call-overload]

    monkeypatch.setattr(source.subprocess, "run", recording_run)

    target_inventory = source.acquire_static_reference_inventory(
        repository,
        source_kind="git-target",
        target=target,
    )
    index_inventory = source.acquire_static_reference_inventory(
        repository,
        source_kind="index",
    )
    worktree_inventory = source.acquire_static_reference_inventory(
        repository,
        source_kind="worktree",
    )

    assert [
        (
            target_inventory.source_kind,
            target_inventory.target,
            target_inventory.candidates[0].content,
            target_inventory.candidates[0].source_object,
        ),
        (
            index_inventory.source_kind,
            index_inventory.target,
            index_inventory.candidates[0].content,
            index_inventory.candidates[0].source_object,
        ),
        (
            worktree_inventory.source_kind,
            worktree_inventory.target,
            worktree_inventory.candidates[0].content,
            worktree_inventory.candidates[0].source_object,
        ),
    ] == [
        ("git-target", target, committed, committed_object),
        ("index", None, indexed, indexed_object),
        ("worktree", None, worktree, None),
    ]
    assert len(git_calls) == 10  # noqa: PLR2004
    assert (
        sum(
            call[0][2:] == ("rev-parse", "--show-toplevel")
            for call in git_calls
        )
        == 3  # noqa: PLR2004
    )
    assert all(call[1] == repository.resolve() for call in git_calls)
    assert all(call[0][1] == "--no-replace-objects" for call in git_calls)
    assert all(
        set(environment).isdisjoint(redirect_values)
        for _, _, environment in git_calls
    )
    assert all(
        {
            "GIT_CONFIG_GLOBAL": environment["GIT_CONFIG_GLOBAL"],
            "GIT_CONFIG_NOSYSTEM": environment["GIT_CONFIG_NOSYSTEM"],
            "GIT_NO_LAZY_FETCH": environment["GIT_NO_LAZY_FETCH"],
        }
        == {
            "GIT_CONFIG_GLOBAL": os.devnull,
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_NO_LAZY_FETCH": "1",
        }
        for _, _, environment in git_calls
    )
    assert poisoned_manifest.read_bytes() == b'{"name":"ambient-poison"}\n'
    assert manifest.read_bytes() == worktree


@pytest.mark.parametrize(
    ("fault_site", "expected_diagnostic"),
    [
        pytest.param(
            "parent-lstat",
            "worktree-parent-unreadable",
            id="parent-lstat-permission-error",
        ),
        pytest.param(
            "candidate-lstat",
            "worktree-file-unreadable",
            id="candidate-lstat-permission-error",
        ),
        pytest.param(
            "candidate-open",
            "worktree-file-unreadable",
            id="candidate-open-permission-error",
        ),
    ],
)
def test_worktree_permission_errors_are_typed_and_stop_before_authority(  # noqa: PLR0915
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    fault_site: str,
    expected_diagnostic: str,
) -> None:
    """Map parent/file permission failures before any authority."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    source = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_source"
    )
    repository = tmp_path / f"repository-{fault_site}"
    _initialize_repository(repository)
    parent_path = repository / "nested"
    parent_path.mkdir()
    candidate_path = parent_path / "package.json"
    declared = b'{"name":"permission-owned-source"}\n'
    candidate_path.write_bytes(declared)
    _git(repository, "add", "nested/package.json")
    _git(repository, "commit", "--quiet", "-m", "permission source")

    fault_path = parent_path if fault_site == "parent-lstat" else candidate_path
    fault_calls: list[Path] = []
    if fault_site.endswith("lstat"):
        real_lstat = Path.lstat

        def permission_lstat(path: Path) -> os.stat_result:
            if path == fault_path:
                fault_calls.append(path)
                message = "injected deterministic lstat denial"
                raise PermissionError(message)
            return real_lstat(path)

        monkeypatch.setattr(source.Path, "lstat", permission_lstat)
    else:
        real_open = os.open

        def permission_open(
            path: object,
            flags: int,
            mode: int = 0o600,
            *,
            dir_fd: int | None = None,
        ) -> int:
            if os.fspath(path) == os.fspath(fault_path):  # type: ignore[arg-type]
                fault_calls.append(fault_path)
                message = "injected deterministic open denial"
                raise PermissionError(message)
            if dir_fd is None:
                return real_open(path, flags, mode)  # type: ignore[arg-type]
            return real_open(  # type: ignore[arg-type]
                path,
                flags,
                mode,
                dir_fd=dir_fd,
            )

        monkeypatch.setattr(source.os, "open", permission_open)

    with pytest.raises(SourceAcquisitionError) as caught:
        source.acquire_static_reference_inventory(
            repository,
            source_kind="worktree",
        )

    assert caught.value.diagnostic_code == expected_diagnostic
    assert caught.value.path == "nested/package.json"
    assert str(caught.value) == (
        "nested/package.json: static-reference source acquisition failed"
    )

    authority_calls: list[object] = []
    session_calls: list[object] = []

    def forbidden_authority(*args: object, **kwargs: object) -> object:
        authority_calls.append((args, kwargs))
        message = "authority ran after a worktree permission failure"
        raise AssertionError(message)

    def forbidden_session() -> StaticReferenceSession:
        session_calls.append(object())
        message = "session opened after a worktree permission failure"
        raise AssertionError(message)

    result = policy.scan_bounded_static_references(
        repository,
        source_kind="worktree",
        authority_runner=forbidden_authority,
        session_factory=forbidden_session,
    )

    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "worktree",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
    }
    assert fault_calls == [fault_path, fault_path]
    assert authority_calls == []
    assert session_calls == []


@pytest.mark.parametrize(
    (
        "selected_path",
        "boundary_near_miss",
        "family",
        "graph_id",
        "input_mode",
    ),
    [
        pytest.param(
            "nested/package.json",
            "nested/package.json.bak",
            "npm-manifest",
            "npm-manifest-v1",
            "strict-utf8-file",
            id="package-json",
        ),
        pytest.param(
            "nested/pnpm-lock.yaml",
            "nested/pnpm-lock.yaml.bak",
            "pnpm-lock",
            "pnpm-lock-v1",
            "strict-utf8-file",
            id="pnpm-lock",
        ),
        pytest.param(
            "nested/pnpm-workspace.yaml",
            "nested/pnpm-workspace.yaml.bak",
            "pnpm-workspace",
            "pnpm-workspace-v1",
            "strict-utf8-file",
            id="pnpm-workspace",
        ),
        pytest.param(
            "nested/packages.lock.json",
            "nested/packages.lock.json.bak",
            "nuget-lock",
            "nuget-lock-v1",
            "strict-utf8-byte-stream",
            id="nuget-lock",
        ),
        pytest.param(
            "nested/packages.config",
            "nested/packages.config.bak",
            "nuget-packages-config",
            "nuget-lock-v1",
            "xml-byte-stream",
            id="nuget-packages-config",
        ),
    ],
)
def test_selector_rows_bind_exact_family_graph_mode_and_basename_boundary(
    selected_path: str,
    boundary_near_miss: str,
    family: str,
    graph_id: str,
    input_mode: str,
) -> None:
    """Bind all five positive rows and one basename boundary miss per row."""
    from three_workflow_delivery_v3.release.static_reference_source import (  # noqa: PLC0415
        select_static_reference_path,
    )

    selection = select_static_reference_path(selected_path)

    assert selection == StaticReferenceSelection(
        family=family,  # type: ignore[arg-type]
        graph_id=graph_id,  # type: ignore[arg-type]
        input_mode=input_mode,  # type: ignore[arg-type]
    )
    assert (
        selection.family,
        selection.graph_id,
        selection.input_mode,
    ) == (family, graph_id, input_mode)
    assert select_static_reference_path(boundary_near_miss) is None


@pytest.mark.parametrize(
    ("basename", "family", "graph_id"),
    [
        pytest.param(
            "pnpm-lock.yaml",
            "pnpm-lock",
            "pnpm-lock-v1",
            id="nested-pnpm-lock",
        ),
        pytest.param(
            "pnpm-workspace.yaml",
            "pnpm-workspace",
            "pnpm-workspace-v1",
            id="nested-pnpm-workspace",
        ),
    ],
)
def test_nested_workflow_pnpm_paths_are_reserved_but_adjacent_paths_select(
    basename: str,
    family: str,
    graph_id: str,
) -> None:
    """Exclude nested workflow descendants without widening the boundary."""
    from three_workflow_delivery_v3.release.static_reference_source import (  # noqa: PLC0415
        select_static_reference_path,
    )

    reserved = f".github/workflows/release/nested/{basename}"
    adjacent = f".github/workflows-adjacent/release/nested/{basename}"

    assert select_static_reference_path(reserved) is None
    assert select_static_reference_path(adjacent) == StaticReferenceSelection(
        family=family,  # type: ignore[arg-type]
        graph_id=graph_id,  # type: ignore[arg-type]
        input_mode="strict-utf8-file",
    )


def test_policy_session_factory_oserror_is_canonical_source_failure_without_root(  # noqa: E501
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Normalize Session construction OSError before authority or root use."""
    policy = importlib.import_module(
        "three_workflow_delivery_v3.release.static_reference_policy"
    )
    inventory = policy.StaticReferenceInventory(
        source_kind="index",
        target=None,
        candidates=(_candidate("factory/package.json"),),
    )
    acquisition_calls: list[tuple[Path, str, str | None]] = []

    def acquire(
        repository_root: Path,
        *,
        source_kind: str,
        target: str | None = None,
    ) -> object:
        acquisition_calls.append((repository_root, source_kind, target))
        return inventory

    monkeypatch.setattr(policy, "acquire_static_reference_inventory", acquire)
    authority_calls: list[object] = []
    factory_calls: list[str] = []

    def forbidden_authority(*args: object, **kwargs: object) -> object:
        authority_calls.append((args, kwargs))
        message = "authority ran after Session construction failed"
        raise AssertionError(message)

    def failing_session_factory() -> StaticReferenceSession:
        factory_calls.append("called")
        message = "injected Session allocation failure"
        raise OSError(message)

    result = policy.scan_bounded_static_references(
        tmp_path,
        source_kind="index",
        authority_runner=forbidden_authority,
        session_factory=failing_session_factory,
    )

    assert result.to_document() == {
        "schema": "workflow-delivery/v3/bounded-static-reference-result",
        "result": "error",
        "source-kind": "index",
        "policy-id": STATIC_REFERENCE_POLICY_ID,
        "policy-digest": STATIC_REFERENCE_POLICY_DIGEST,
        "implementation-identities": [],
        "findings": [],
        "error-kind": "source-acquisition-failed",
    }
    assert result.implementation_identities == ()
    assert result.findings == ()
    assert acquisition_calls == [(tmp_path, "index", None)]
    assert factory_calls == ["called"]
    assert authority_calls == []
    assert list(tmp_path.iterdir()) == []
