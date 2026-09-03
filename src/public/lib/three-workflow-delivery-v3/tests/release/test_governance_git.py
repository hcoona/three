"""Integration contracts for isolated protected Governance Git authority."""

from __future__ import annotations

# ruff: noqa: D103, S603
import os
import shutil
import subprocess
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitReadError,
    IsolatedGovernanceGitReader,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping
    from pathlib import Path

REPOSITORY = "owner/repository"
CONTENT = b'{"schema":"governance-test-v1"}\n'
CHANGED_CONTENT = b'{"schema":"governance-test-v2"}\n'


def _git_executable() -> str:
    executable = shutil.which("git")
    assert executable is not None, "Git is required for Governance authority"
    return executable


def _run(
    repository: Path,
    *arguments: str,
) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        (_git_executable(), *arguments),
        cwd=repository,
        check=True,
        capture_output=True,
    )


def _output(repository: Path, *arguments: str) -> str:
    return _run(repository, *arguments).stdout.decode("utf-8").strip()


def _initialize_repository(
    repository: Path,
    *,
    object_format: str = "sha1",
) -> None:
    repository.mkdir()
    _run(
        repository,
        "init",
        "--quiet",
        "--initial-branch=main",
        f"--object-format={object_format}",
    )
    _run(repository, "config", "user.name", "Governance Git Test")
    _run(
        repository,
        "config",
        "user.email",
        "governance-git@example.invalid",
    )


def _governance_path(repository: Path) -> Path:
    return repository / GOVERNANCE_PATH


def _write_governance(
    repository: Path,
    content: bytes = CONTENT,
) -> None:
    path = _governance_path(repository)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)


def _commit(repository: Path, message: str) -> str:
    _run(repository, "add", "--all")
    _run(repository, "commit", "--quiet", "--message", message)
    return _output(repository, "rev-parse", "HEAD")


def _create_remote_repository(
    tmp_path: Path,
    *,
    governance_kind: str = "blob",
    object_format: str = "sha1",
) -> tuple[Path, Path, str]:
    repository = tmp_path / "work"
    _initialize_repository(repository, object_format=object_format)
    if governance_kind == "blob":
        _write_governance(repository)
    elif governance_kind == "executable":
        _write_governance(repository)
        _governance_path(repository).chmod(0o755)
    elif governance_kind == "tree":
        nested = _governance_path(repository) / "nested.json"
        nested.parent.mkdir(parents=True)
        nested.write_bytes(CONTENT)
    elif governance_kind != "missing":
        message = f"unsupported Governance kind: {governance_kind}"
        raise AssertionError(message)
    (repository / "unrelated.txt").write_text("initial\n", encoding="utf-8")
    eligibility_main_sha = _commit(repository, "initial Governance")

    remote = tmp_path / "remote.git"
    _run(
        tmp_path,
        "init",
        "--bare",
        "--quiet",
        f"--object-format={object_format}",
        str(remote),
    )
    _run(repository, "remote", "add", "origin", remote.as_uri())
    _run(repository, "push", "--quiet", "--set-upstream", "origin", "main")
    return repository, remote, eligibility_main_sha


def _push_main(repository: Path) -> None:
    _run(repository, "push", "--quiet", "origin", "main")


def _reader(remote: Path, parent: Path) -> IsolatedGovernanceGitReader:
    return IsolatedGovernanceGitReader(
        repository=REPOSITORY,
        token=None,
        remote_url=remote.as_uri(),
        parent=parent,
    )


def _read(
    reader: IsolatedGovernanceGitReader,
    *,
    eligibility_main_sha: str | None = None,
):
    return reader.read(
        repository=REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        eligibility_main_sha=eligibility_main_sha,
    )


@pytest.mark.parametrize("object_format", ["sha1", "sha256"])
def test_isolated_read_accepts_unrelated_main_advance(
    tmp_path: Path,
    object_format: str,
) -> None:
    repository, remote, eligibility_main_sha = _create_remote_repository(
        tmp_path,
        object_format=object_format,
    )
    reader = _reader(remote, tmp_path)
    initial = _read(reader)
    (repository / "unrelated.txt").write_text(
        "unrelated main advance\n",
        encoding="utf-8",
    )
    current_main_sha = _commit(repository, "unrelated main advance")
    _push_main(repository)

    current = _read(reader, eligibility_main_sha=eligibility_main_sha)

    assert initial.main_sha == eligibility_main_sha
    assert current.main_sha == current_main_sha
    assert current.main_sha != eligibility_main_sha
    assert current.object_format == object_format
    assert len(current.main_sha) == {"sha1": 40, "sha256": 64}[object_format]
    assert len(current.blob_oid) == {"sha1": 40, "sha256": 64}[object_format]
    assert current.blob_oid == initial.blob_oid
    assert current.content == initial.content == CONTENT


@pytest.mark.parametrize("revert", [False, True], ids=["edit", "edit-revert"])
def test_isolated_read_rejects_protected_path_edit_or_revert(
    tmp_path: Path,
    *,
    revert: bool,
) -> None:
    repository, remote, eligibility_main_sha = _create_remote_repository(
        tmp_path
    )
    _write_governance(repository, CHANGED_CONTENT)
    changed_sha = _commit(repository, "edit protected Governance")
    if revert:
        _write_governance(repository)
        current_main_sha = _commit(repository, "revert protected Governance")
        assert (
            _run(
                repository,
                "diff",
                "--quiet",
                eligibility_main_sha,
                current_main_sha,
                "--",
                GOVERNANCE_PATH,
            ).returncode
            == 0
        )
    else:
        current_main_sha = changed_sha
        assert _governance_path(repository).read_bytes() == CHANGED_CONTENT
    _push_main(repository)

    with pytest.raises(
        GovernanceGitReadError,
        match="protected path changed after eligibility",
    ):
        _read(
            _reader(remote, tmp_path),
            eligibility_main_sha=eligibility_main_sha,
        )

    assert current_main_sha != eligibility_main_sha
    assert _output(repository, "rev-parse", "HEAD") == current_main_sha


def test_isolated_read_rejects_delete_restore_round_trip(
    tmp_path: Path,
) -> None:
    repository, remote, eligibility_main_sha = _create_remote_repository(
        tmp_path
    )
    _governance_path(repository).unlink()
    _commit(repository, "delete protected Governance")
    _write_governance(repository)
    current_main_sha = _commit(repository, "restore protected Governance")
    _push_main(repository)

    with pytest.raises(
        GovernanceGitReadError,
        match="protected path changed after eligibility",
    ):
        _read(
            _reader(remote, tmp_path),
            eligibility_main_sha=eligibility_main_sha,
        )

    assert _governance_path(repository).read_bytes() == CONTENT
    assert (
        _run(
            repository,
            "diff",
            "--quiet",
            eligibility_main_sha,
            current_main_sha,
            "--",
            GOVERNANCE_PATH,
        ).returncode
        == 0
    )


def test_isolated_read_rejects_rename_round_trip(tmp_path: Path) -> None:
    repository, remote, eligibility_main_sha = _create_remote_repository(
        tmp_path
    )
    moved_path = f"{GOVERNANCE_PATH}.moved"
    _run(repository, "mv", GOVERNANCE_PATH, moved_path)
    _commit(repository, "rename protected Governance away")
    _run(repository, "mv", moved_path, GOVERNANCE_PATH)
    current_main_sha = _commit(repository, "rename protected Governance back")
    _push_main(repository)

    with pytest.raises(
        GovernanceGitReadError,
        match="protected path changed after eligibility",
    ):
        _read(
            _reader(remote, tmp_path),
            eligibility_main_sha=eligibility_main_sha,
        )

    assert _governance_path(repository).read_bytes() == CONTENT
    assert (
        _run(
            repository,
            "diff",
            "--quiet",
            eligibility_main_sha,
            current_main_sha,
            "--",
            GOVERNANCE_PATH,
        ).returncode
        == 0
    )


def test_isolated_read_rejects_merged_side_branch_touch(
    tmp_path: Path,
) -> None:
    repository, remote, eligibility_main_sha = _create_remote_repository(
        tmp_path
    )
    _run(repository, "switch", "--quiet", "--create", "side")
    _write_governance(repository, CHANGED_CONTENT)
    _commit(repository, "touch Governance on side branch")
    _write_governance(repository)
    _commit(repository, "restore Governance on side branch")
    _run(repository, "switch", "--quiet", "main")
    (repository / "unrelated.txt").write_text(
        "main diverged\n",
        encoding="utf-8",
    )
    _commit(repository, "advance main independently")
    _run(
        repository,
        "merge",
        "--quiet",
        "--no-ff",
        "--message",
        "merge side branch",
        "side",
    )
    current_main_sha = _output(repository, "rev-parse", "HEAD")
    _push_main(repository)

    with pytest.raises(
        GovernanceGitReadError,
        match="protected path changed after eligibility",
    ):
        _read(
            _reader(remote, tmp_path),
            eligibility_main_sha=eligibility_main_sha,
        )

    touches = _output(
        repository,
        "rev-list",
        "--full-history",
        f"{eligibility_main_sha}..{current_main_sha}",
        "--",
        GOVERNANCE_PATH,
    )
    assert touches
    assert _governance_path(repository).read_bytes() == CONTENT


def test_isolated_read_rejects_non_descendant_force_update(
    tmp_path: Path,
) -> None:
    _, remote, eligibility_main_sha = _create_remote_repository(tmp_path)
    replacement = tmp_path / "replacement"
    _initialize_repository(replacement)
    _write_governance(replacement)
    (replacement / "unrelated.txt").write_text(
        "rewritten history\n",
        encoding="utf-8",
    )
    replacement_sha = _commit(replacement, "non-descendant replacement")
    _run(
        replacement,
        "push",
        "--quiet",
        "--force",
        remote.as_uri(),
        "main:main",
    )

    with pytest.raises(
        GovernanceGitReadError,
        match=(
            r"Eligibility main commit is unavailable|"
            r"Current main is not a descendant"
        ),
    ):
        _read(
            _reader(remote, tmp_path),
            eligibility_main_sha=eligibility_main_sha,
        )

    advertised = _output(
        tmp_path,
        "ls-remote",
        "--refs",
        remote.as_uri(),
        GOVERNANCE_REF,
    )
    assert advertised == f"{replacement_sha}\t{GOVERNANCE_REF}"
    assert replacement_sha != eligibility_main_sha


def test_isolated_read_rejects_shallow_history(tmp_path: Path) -> None:
    repository, _, _ = _create_remote_repository(tmp_path)
    (repository / "unrelated.txt").write_text("second\n", encoding="utf-8")
    _commit(repository, "second commit")
    shallow_remote = tmp_path / "shallow.git"
    _run(
        tmp_path,
        "clone",
        "--quiet",
        "--bare",
        "--depth",
        "1",
        "--branch",
        "main",
        repository.as_uri(),
        str(shallow_remote),
    )

    with pytest.raises(
        GovernanceGitReadError,
        match=(
            r"fetch failed|history is shallow|ancestry is incomplete|"
            r"main ref is unavailable"
        ),
    ):
        _read(_reader(shallow_remote, tmp_path))

    assert (shallow_remote / "shallow").is_file()


def test_isolated_read_rejects_missing_git_object(tmp_path: Path) -> None:
    repository, _, _ = _create_remote_repository(tmp_path)
    blob_oid = _output(
        repository,
        "rev-parse",
        f"HEAD:{GOVERNANCE_PATH}",
    )
    object_path = repository / ".git" / "objects" / blob_oid[:2] / blob_oid[2:]
    assert object_path.is_file()
    object_path.unlink()

    with pytest.raises(
        GovernanceGitReadError,
        match=(r"fetch failed|object closure is incomplete|blob is unreadable"),
    ):
        _read(_reader(repository, tmp_path))

    assert not object_path.exists()


@pytest.mark.parametrize(
    ("governance_kind", "expected_entry_prefix", "message"),
    [
        pytest.param(
            "missing",
            None,
            "did not resolve to exactly one entry",
            id="missing-path",
        ),
        pytest.param(
            "tree",
            "040000 tree ",
            "not the exact regular non-executable blob",
            id="tree-path",
        ),
        pytest.param(
            "executable",
            "100755 blob ",
            "not the exact regular non-executable blob",
            id="executable-blob",
        ),
    ],
)
def test_isolated_read_rejects_missing_or_non_blob_path(
    tmp_path: Path,
    governance_kind: str,
    expected_entry_prefix: str | None,
    message: str,
) -> None:
    repository, remote, _ = _create_remote_repository(
        tmp_path,
        governance_kind=governance_kind,
    )
    tree_entry = _output(
        repository,
        "ls-tree",
        "--full-tree",
        "HEAD",
        "--",
        GOVERNANCE_PATH,
    )
    if expected_entry_prefix is None:
        assert tree_entry == ""
    else:
        metadata, resolved_path = tree_entry.split("\t", maxsplit=1)
        assert metadata.startswith(expected_entry_prefix)
        assert resolved_path == GOVERNANCE_PATH

    with pytest.raises(GovernanceGitReadError, match=message):
        _read(_reader(remote, tmp_path))


class _PushAfterAdvertisementReader(IsolatedGovernanceGitReader):
    def __init__(
        self,
        *,
        remote: Path,
        parent: Path,
        callback: Callable[[], None],
    ) -> None:
        super().__init__(
            repository=REPOSITORY,
            token=None,
            remote_url=remote.as_uri(),
            parent=parent,
        )
        self._callback = callback

    def _remote_ref_identity(
        self,
        root: Path,
        *,
        environment: Mapping[str, str],
    ) -> tuple[str, str]:
        identity = super()._remote_ref_identity(
            root,
            environment=environment,
        )
        self._callback()
        return identity


def test_isolated_read_ignores_hostile_ambient_git_environment(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    expected_content = CONTENT
    repository, remote, _ = _create_remote_repository(tmp_path)
    expected_main_sha = _output(repository, "rev-parse", "HEAD")
    expected_blob_oid = _output(
        repository,
        "rev-parse",
        f"HEAD:{GOVERNANCE_PATH}",
    )
    hostile_object_directory = tmp_path / "hostile-objects"
    hostile_alternate_directory = tmp_path / "hostile-alternates"
    hostile_object_directory.write_bytes(b"not an object directory")
    hostile_alternate_directory.write_bytes(b"not an alternate directory")
    hostile_environment = {
        "GIT_OBJECT_DIRECTORY": os.fspath(hostile_object_directory),
        "GIT_ALTERNATE_OBJECT_DIRECTORIES": os.fspath(
            hostile_alternate_directory
        ),
        "GIT_CONFIG_COUNT": "1",
        "GIT_CONFIG_KEY_0": "protocol.file.allow",
        "GIT_CONFIG_VALUE_0": "never",
    }
    for name, value in hostile_environment.items():
        monkeypatch.setitem(os.environ, name, value)

    result = _read(_reader(remote, tmp_path))

    assert result.main_sha == expected_main_sha
    assert result.object_format == "sha1"
    assert result.blob_oid == expected_blob_oid
    assert result.content == expected_content


def test_isolated_read_rejects_advertisement_fetch_race(
    tmp_path: Path,
) -> None:
    repository, remote, advertised_main_sha = _create_remote_repository(
        tmp_path
    )
    (repository / "unrelated.txt").write_text(
        "prepared main advance\n",
        encoding="utf-8",
    )
    fetched_main_sha = _commit(repository, "prepare main advance")

    def push_prepared_main() -> None:
        _push_main(repository)

    reader = _PushAfterAdvertisementReader(
        remote=remote,
        parent=tmp_path,
        callback=push_prepared_main,
    )

    with pytest.raises(
        GovernanceGitReadError,
        match=r"^Governance main ref changed during isolated read$",
    ):
        _read(reader)

    assert fetched_main_sha != advertised_main_sha
    assert (
        _output(
            tmp_path,
            "ls-remote",
            "--refs",
            remote.as_uri(),
            GOVERNANCE_REF,
        )
        == f"{fetched_main_sha}\t{GOVERNANCE_REF}"
    )
