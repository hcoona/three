"""Focused offline tests for the case-specific Platform-Orphan CLI."""

# ruff: noqa: ARG001, D103, EM102, S105, SLF001, TRY003

from __future__ import annotations

import subprocess
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest
from three_workflow_delivery_v3 import cli as cli_module
from three_workflow_delivery_v3.governance import (
    platform_orphan_coordinator as coordinator,
)

ARGPARSE_ERROR = 2
CONTROL_COMMIT = "c" * 40
ENTRY_POINT = (
    "three-workflow-delivery-v3 governance "
    "reconcile-platform-orphan-32809578776"
)
PROJECT_CLI_PATH = (
    "src/public/lib/three-workflow-delivery-v3/src/"
    "three_workflow_delivery_v3/cli.py"
)


def _arguments() -> list[str]:
    return [
        "governance",
        "reconcile-platform-orphan-32809578776",
        "--review-artifact",
        "review.bin",
        "--probe-artifact",
        "probe.json",
        "--governance-artifact",
        "governance.json",
    ]


def _arguments_with_token(
    placement: str,
    token_argument: list[str],
) -> list[str]:
    arguments = _arguments()
    if placement == "root":
        return [*token_argument, *arguments]
    if placement == "governance":
        return [arguments[0], *token_argument, *arguments[1:]]
    return [*arguments, *token_argument]


@pytest.mark.parametrize(
    "extra",
    [
        "--repository",
        "--ref",
        "--run-id",
        "--workflow-id",
        "--package-coordinate",
        "--tag",
        "--api-origin",
        "--npm-origin",
        "--result-path",
        "--output",
        "--method",
    ],
)
def test_platform_orphan_cli_rejects_caller_coordinates(extra: str) -> None:
    with pytest.raises(SystemExit) as error:
        cli_module._parser().parse_args([*_arguments(), extra, "caller-value"])

    assert error.value.code == ARGPARSE_ERROR


def test_platform_orphan_cli_has_only_three_content_paths() -> None:
    arguments = cli_module._parser().parse_args(_arguments())

    assert arguments.review_artifact == "review.bin"
    assert arguments.probe_artifact == "probe.json"
    assert arguments.governance_artifact == "governance.json"
    assert {
        name
        for name in vars(arguments)
        if name not in {"context", "governance_command", "handler"}
    } == {
        "review_artifact",
        "probe_artifact",
        "governance_artifact",
    }


@pytest.mark.parametrize(
    "abbreviation",
    ["--review-art", "--probe-art", "--governance-art"],
)
def test_platform_orphan_target_subparser_rejects_abbreviations(
    abbreviation: str,
) -> None:
    arguments = _arguments()
    option_index = arguments.index(
        {
            "--review-art": "--review-artifact",
            "--probe-art": "--probe-artifact",
            "--governance-art": "--governance-artifact",
        }[abbreviation]
    )
    arguments[option_index] = abbreviation

    with pytest.raises(SystemExit) as error:
        cli_module._parser().parse_args(arguments)

    assert error.value.code == ARGPARSE_ERROR


@pytest.mark.parametrize(
    "token_argument",
    [
        ["--token", "super-secret-value"],
        ["--token=super-secret-value"],
    ],
)
@pytest.mark.parametrize("placement", ["root", "governance", "leaf"])
def test_platform_orphan_token_poison_pill_rejects_without_retaining_or_echoing(
    placement: str,
    token_argument: list[str],
    capsys: pytest.CaptureFixture[str],
) -> None:
    with pytest.raises(SystemExit) as error:
        cli_module._parser().parse_args(
            _arguments_with_token(placement, token_argument)
        )

    captured = capsys.readouterr()
    assert error.value.code == ARGPARSE_ERROR
    assert captured.out == ""
    assert captured.err.endswith("error: explicit token input is prohibited\n")
    assert "super-secret-value" not in captured.err


@pytest.mark.parametrize(
    "token_argument",
    [
        ["--token", "super-secret-value"],
        ["--token=super-secret-value"],
    ],
)
@pytest.mark.parametrize("placement", ["root", "governance", "leaf"])
def test_platform_orphan_token_poison_pill_never_reaches_handler(
    monkeypatch: pytest.MonkeyPatch,
    placement: str,
    token_argument: list[str],
) -> None:
    reached_handler = False

    def reconcile(**_kwargs: Any) -> None:
        nonlocal reached_handler
        reached_handler = True

    monkeypatch.setattr(
        cli_module,
        "reconcile_platform_orphan_32809578776",
        reconcile,
    )

    with pytest.raises(SystemExit) as error:
        cli_module.main(_arguments_with_token(placement, token_argument))

    assert error.value.code == ARGPARSE_ERROR
    assert reached_handler is False


def test_platform_orphan_cli_sanitizes_missing_credential_error(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    monkeypatch.delenv("GH_TOKEN", raising=False)
    monkeypatch.delenv("GITHUB_TOKEN", raising=False)

    assert cli_module.main(_arguments()) == 1

    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err == "read credential is missing\n"
    assert all(
        path not in captured.err
        for path in ("review.bin", "probe.json", "governance.json")
    )


def _clean_git_output(
    command: tuple[str, ...],
    cwd: Path,
) -> str:
    if command == ("git", "rev-parse", "--show-toplevel"):
        return "/repo\n"
    if command[:3] == ("git", "ls-files", "--error-unmatch"):
        return f"{PROJECT_CLI_PATH}\n"
    if command == ("git", "rev-parse", "HEAD"):
        return f"{CONTROL_COMMIT}\n"
    if command == (
        "git",
        "status",
        "--porcelain=v1",
        "--untracked-files=all",
    ):
        return ""
    raise AssertionError(f"unexpected Git command: {command!r}")


def test_local_control_provenance_accepts_exact_clean_tracked_worktree() -> (
    None
):
    calls: list[tuple[tuple[str, ...], Path]] = []

    def git_output(command: tuple[str, ...], cwd: Path) -> str:
        calls.append((command, cwd))
        return _clean_git_output(command, cwd)

    provenance = coordinator.inspect_local_control_provenance(
        cli_module_path=Path("/repo") / PROJECT_CLI_PATH,
        entry_point_route=ENTRY_POINT,
        git_output=git_output,
    )

    assert provenance.commit == CONTROL_COMMIT
    assert provenance.project_root == Path("/repo")
    assert [command for command, _cwd in calls] == [
        ("git", "rev-parse", "--show-toplevel"),
        ("git", "ls-files", "--error-unmatch", PROJECT_CLI_PATH),
        ("git", "rev-parse", "HEAD"),
        ("git", "status", "--porcelain=v1", "--untracked-files=all"),
    ]


@pytest.mark.parametrize(
    ("failure", "expected_message"),
    [
        ("wrong-head", "HEAD is malformed"),
        ("uppercase-head", "HEAD is malformed"),
        ("nonhex-head", "HEAD is malformed"),
        ("staged", "worktree is dirty"),
        ("unstaged", "worktree is dirty"),
        ("untracked", "worktree is dirty"),
        ("untracked-module", "module is not tracked"),
        ("module-source", "module source is unexpected"),
        ("entry-point", "entry point is unexpected"),
    ],
)
def test_local_control_provenance_failures_are_closed(
    failure: str,
    expected_message: str,
) -> None:
    def git_output(command: tuple[str, ...], cwd: Path) -> str:
        if command == ("git", "rev-parse", "HEAD") and failure == "wrong-head":
            return f"{CONTROL_COMMIT[:-1]}\n"
        if (
            command == ("git", "rev-parse", "HEAD")
            and failure == "uppercase-head"
        ):
            return f"{'C' * 40}\n"
        if command == ("git", "rev-parse", "HEAD") and failure == "nonhex-head":
            return f"{'g' * 40}\n"
        if (
            command[:3]
            == (
                "git",
                "ls-files",
                "--error-unmatch",
            )
            and failure == "untracked-module"
        ):
            return ""
        if command[1:3] == ("status", "--porcelain=v1"):
            return {
                "staged": "M  tracked.py\n",
                "unstaged": " M tracked.py\n",
                "untracked": "?? untracked.py\n",
            }.get(failure, "")
        return _clean_git_output(command, cwd)

    module_path = Path("/repo") / PROJECT_CLI_PATH
    if failure == "module-source":
        module_path = Path("/repo/other/cli.py")
    route = "unexpected route" if failure == "entry-point" else ENTRY_POINT

    with pytest.raises(ValueError, match=expected_message):
        coordinator.inspect_local_control_provenance(
            cli_module_path=module_path,
            entry_point_route=route,
            git_output=git_output,
        )


@pytest.mark.parametrize(
    ("message", "expected_error"),
    [
        ("local control worktree is dirty", "local control worktree is dirty"),
        (
            "loaded CLI module source is unexpected",
            "loaded CLI module source is unexpected",
        ),
    ],
)
def test_cli_local_control_failure_emits_no_candidate(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    message: str,
    expected_error: str,
) -> None:
    reached_coordinator = False

    def fail_inspection(**_kwargs: Any) -> None:
        raise ValueError(message)

    def reconcile(**_kwargs: Any) -> None:
        nonlocal reached_coordinator
        reached_coordinator = True

    monkeypatch.setenv("GH_TOKEN", "environment-token")
    monkeypatch.setattr(
        cli_module,
        "inspect_local_control_provenance",
        fail_inspection,
    )
    monkeypatch.setattr(
        cli_module,
        "reconcile_platform_orphan_32809578776",
        reconcile,
    )

    assert cli_module.main(_arguments()) == 1

    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err == f"{expected_error}\n"
    assert reached_coordinator is False
    assert "environment-token" not in captured.err


def test_cli_injects_fixed_local_control_and_runtime_seams(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    inspection: dict[str, Any] = {}
    reconciliation: dict[str, Any] = {}

    def inspect(**kwargs: Any) -> SimpleNamespace:
        inspection.update(kwargs)
        return SimpleNamespace(commit=CONTROL_COMMIT)

    def reconcile(**kwargs: Any) -> None:
        reconciliation.update(kwargs)
        kwargs["output"](b'{"candidate":"admitted"}\n')

    monkeypatch.setenv("GH_TOKEN", "environment-token")
    monkeypatch.setattr(cli_module, "inspect_local_control_provenance", inspect)
    monkeypatch.setattr(
        cli_module,
        "reconcile_platform_orphan_32809578776",
        reconcile,
    )

    assert cli_module.main(_arguments()) == 0

    captured = capsys.readouterr()
    assert captured.out == '{"candidate":"admitted"}\n'
    assert captured.err == ""
    assert (
        inspection["cli_module_path"].resolve()
        == Path(cli_module.__file__).resolve()
    )
    assert inspection["entry_point_route"] == ENTRY_POINT
    assert inspection["git_output"] is cli_module._platform_orphan_git_output
    assert reconciliation["local_control_commit"] == CONTROL_COMMIT
    assert reconciliation["token"] == "environment-token"
    assert reconciliation["review_artifact"] == Path("review.bin")
    assert reconciliation["probe_artifact"] == Path("probe.json")
    assert reconciliation["governance_artifact"] == Path("governance.json")


@pytest.mark.parametrize(
    "failure",
    [
        OSError("sensitive local path"),
        subprocess.CalledProcessError(1, ("git", "sensitive-argument")),
        UnicodeError("sensitive output"),
    ],
)
def test_local_git_subprocess_failures_are_sanitized(
    monkeypatch: pytest.MonkeyPatch,
    failure: BaseException,
) -> None:
    def fail_run(*_args: object, **_kwargs: object) -> None:
        raise failure

    monkeypatch.setattr(cli_module.subprocess, "run", fail_run)

    with pytest.raises(
        ValueError,
        match=r"^local control Git inspection failed$",
    ):
        cli_module._platform_orphan_git_output(
            ("git", "rev-parse", "HEAD"),
            Path("/sensitive/local/path"),
        )
