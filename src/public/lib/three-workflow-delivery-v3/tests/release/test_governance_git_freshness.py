"""Complete Ruby input continuity over actual isolated local Git history."""

import pytest
from three_workflow_delivery_v3.release.governance_git import (
    GovernanceGitReadError,
)
from three_workflow_delivery_v3.repository.descriptors import (
    GOVERNANCE_PATH,
    GOVERNANCE_REF,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    is_ruby_input_path,
)

from .test_governance_git import (
    CONTENT,
    REPOSITORY,
    _commit,
    _create_remote_repository,
    _initialize_repository,
    _output,
    _push_main,
    _read,
    _reader,
    _run,
)

_IMPLEMENTATION = (
    "src/public/lib/three-workflow-delivery-v3/src/"
    "three_workflow_delivery_v3/new_ruby_input.py"
)
_NEW_INPUTS = (
    _IMPLEMENTATION,
    ".github/actions/workflow-delivery-v3-ruby-new/action.yml",
    ".github/workflows/workflow-delivery-v3-ruby-new.yml",
    ".github/workflow-delivery/configuration/hcoona-release-smoke-ruby-rubygems.json",
)


def _write_input(repository, path, content=b"inert tracked fixture bytes\n"):
    destination = repository / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(content)
    return destination


def _read_inputs(reader, target):
    return reader.read(
        repository=REPOSITORY,
        ref=GOVERNANCE_REF,
        path=GOVERNANCE_PATH,
        eligibility_main_sha=target,
        relevant_path=is_ruby_input_path,
    )


@pytest.mark.parametrize("advance", [False, True], ids=["same", "unrelated"])
def test_input_freshness_accepts_same_target_or_unrelated_main(
    tmp_path, *, advance
):
    """Read current protected bytes without forcing main-tip equality."""
    repository, remote, target = _create_remote_repository(tmp_path)
    current = target
    if advance:
        _write_input(repository, "unrelated/new module.py")
        current = _commit(repository, "add unrelated source")
        _push_main(repository)
    observed = _read_inputs(_reader(remote, tmp_path), target)
    assert observed.main_sha == current
    assert observed.content == CONTENT
    assert (observed.main_sha != target) is advance


@pytest.mark.parametrize("path", _NEW_INPUTS)
def test_input_freshness_rejects_previously_untracked_relevant_addition(
    tmp_path, path
):
    """The frozen target's inventory cannot hide a newly introduced input."""
    repository, remote, target = _create_remote_repository(tmp_path)
    assert not (repository / path).exists()
    _write_input(repository, path)
    current = _commit(repository, "add new Ruby input")
    _push_main(repository)
    with pytest.raises(GovernanceGitReadError, match="Protected input changed"):
        _read_inputs(_reader(remote, tmp_path), target)
    assert current != target
    # Existing readers without the new selector retain one-blob semantics.
    legacy = _read(_reader(remote, tmp_path), eligibility_main_sha=target)
    assert (legacy.main_sha, legacy.content) == (current, CONTENT)


def test_input_freshness_rejects_relevant_touch_revert_with_equal_end_tree(
    tmp_path,
):
    """Exact end-tree equality does not erase relevant intervening history."""
    repository, remote, target = _create_remote_repository(tmp_path)
    path = _write_input(repository, _IMPLEMENTATION)
    _commit(repository, "introduce relevant input")
    path.unlink()
    current = _commit(repository, "revert relevant input")
    _push_main(repository)
    assert _output(repository, "rev-parse", f"{target}^{{tree}}") == _output(
        repository, "rev-parse", f"{current}^{{tree}}"
    )
    with pytest.raises(GovernanceGitReadError, match="Protected input changed"):
        _read_inputs(_reader(remote, tmp_path), target)


def test_input_freshness_rejects_merged_side_branch_touch_revert(tmp_path):
    """Merged side history remains relevant after its end tree is restored."""
    repository, remote, target = _create_remote_repository(tmp_path)
    _run(repository, "switch", "--quiet", "--create", "side")
    path = _write_input(repository, _IMPLEMENTATION)
    _commit(repository, "touch relevant input on side")
    path.unlink()
    _commit(repository, "restore side branch tree")
    _run(repository, "switch", "--quiet", "main")
    _write_input(repository, "unrelated.txt", b"main advanced\n")
    _commit(repository, "advance main independently")
    _run(repository, "merge", "--quiet", "--no-ff", "-m", "merge side", "side")
    _push_main(repository)
    assert not (repository / _IMPLEMENTATION).exists()
    with pytest.raises(GovernanceGitReadError, match="Protected input changed"):
        _read_inputs(_reader(remote, tmp_path), target)


def test_input_freshness_rejects_relevant_change_introduced_only_by_merge(
    tmp_path,
):
    """Examining side commits alone cannot miss merge-resolution input."""
    repository, remote, target = _create_remote_repository(tmp_path)
    _run(repository, "switch", "--quiet", "--create", "side")
    _write_input(repository, "unrelated/side.txt")
    side = _commit(repository, "unrelated side change")
    _run(repository, "switch", "--quiet", "main")
    _write_input(repository, "unrelated/main.txt")
    main = _commit(repository, "unrelated main change")
    _run(repository, "merge", "--quiet", "--no-ff", "--no-commit", "side")
    _write_input(repository, _IMPLEMENTATION)
    merge = _commit(repository, "introduce Ruby input in merge resolution")
    _push_main(repository)
    assert _output(repository, "rev-parse", f"{merge}^1") == main
    assert _output(repository, "rev-parse", f"{merge}^2") == side
    with pytest.raises(GovernanceGitReadError, match="Protected input changed"):
        _read_inputs(_reader(remote, tmp_path), target)


def test_input_freshness_keeps_parentless_merge_fail_closed(
    tmp_path,
):
    """The original control-blob gate still rejects separately rooted merges."""
    repository, remote, target = _create_remote_repository(tmp_path)
    other = tmp_path / "other-root"
    _initialize_repository(other)
    _write_input(other, _IMPLEMENTATION)
    _write_input(other, "unrelated/other-root.txt")
    root = _commit(other, "separate root with relevant input")
    _run(repository, "fetch", "--quiet", other.as_uri(), "main:other")
    _run(
        repository,
        "merge",
        "--quiet",
        "--no-ff",
        "--no-commit",
        "--allow-unrelated-histories",
        "other",
    )
    (repository / _IMPLEMENTATION).unlink()
    _commit(repository, "merge unrelated root without its relevant input")
    _push_main(repository)
    assert not (repository / _IMPLEMENTATION).exists()
    assert (
        root in _output(repository, "rev-list", f"{target}..HEAD").splitlines()
    )
    # The existing protected-blob check rejects this history first. Do not
    # weaken it to force execution into the later complete-input scanner.
    with pytest.raises(GovernanceGitReadError, match="protected path changed"):
        _read_inputs(_reader(remote, tmp_path), target)


@pytest.mark.parametrize("outward", [False, True], ids=["into", "out-of"])
def test_input_freshness_rejects_rename_across_relevant_boundary(
    tmp_path, *, outward
):
    """Both rename endpoints matter even when file bytes are unchanged."""
    repository, remote, _initial = _create_remote_repository(tmp_path)
    source, destination = (
        (_IMPLEMENTATION, "unrelated/renamed.py")
        if outward
        else ("unrelated/original.py", _IMPLEMENTATION)
    )
    original = _write_input(repository, source)
    target = _commit(repository, "prepare rename source")
    renamed = repository / destination
    renamed.parent.mkdir(parents=True, exist_ok=True)
    original.rename(renamed)
    _commit(repository, "rename across Ruby input boundary")
    _push_main(repository)
    assert renamed.read_bytes() == b"inert tracked fixture bytes\n"
    assert not original.exists()
    with pytest.raises(GovernanceGitReadError, match="Protected input changed"):
        _read_inputs(_reader(remote, tmp_path), target)


def test_input_freshness_accepts_unrelated_merge_from_before_target(tmp_path):
    """A merge-parent difference cannot invent a new post-target change."""
    repository, remote, initial = _create_remote_repository(tmp_path)
    _run(repository, "branch", "side", initial)
    _write_input(repository, _IMPLEMENTATION)
    target = _commit(repository, "establish selected Ruby input")
    _run(repository, "switch", "--quiet", "side")
    _write_input(repository, "unrelated/side.txt")
    _commit(repository, "unrelated side advancement")
    _run(repository, "switch", "--quiet", "main")
    _run(repository, "merge", "--quiet", "--no-ff", "-m", "merge side", "side")
    current = _output(repository, "rev-parse", "HEAD")
    _push_main(repository)
    assert (
        _run(
            repository,
            "diff",
            "--quiet",
            target,
            current,
            "--",
            _IMPLEMENTATION,
        ).returncode
        == 0
    )
    observed = _read_inputs(_reader(remote, tmp_path), target)
    assert (observed.main_sha, observed.content) == (current, CONTENT)


def test_input_freshness_rejects_target_outside_current_main_history(tmp_path):
    """Matching control bytes cannot supply missing target ancestry."""
    repository, remote, initial = _create_remote_repository(tmp_path)
    _run(repository, "switch", "--quiet", "--create", "foreign")
    _write_input(repository, "unrelated/foreign.txt")
    foreign = _commit(repository, "nonancestor target")
    _run(repository, "switch", "--quiet", "main")
    assert _output(repository, "rev-parse", "HEAD") == initial
    with pytest.raises(
        GovernanceGitReadError,
        match=r"commit is unavailable|not a descendant",
    ):
        _read_inputs(_reader(remote, tmp_path), foreign)


@pytest.mark.parametrize(
    ("target", "selector"), [(None, is_ruby_input_path), ("a" * 40, True)]
)
def test_input_freshness_requires_target_and_callable_before_git(
    tmp_path, target, selector
):
    """Invalid continuity requests cannot initialize a repository or fetch."""
    reader = _reader(tmp_path / "not-a-remote.git", tmp_path)
    with pytest.raises(GovernanceGitReadError, match="target and selector"):
        reader.read(
            repository=REPOSITORY,
            ref=GOVERNANCE_REF,
            path=GOVERNANCE_PATH,
            eligibility_main_sha=target,
            relevant_path=selector,
        )
    assert list(tmp_path.iterdir()) == []
