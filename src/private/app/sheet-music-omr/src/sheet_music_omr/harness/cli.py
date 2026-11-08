"""The command line of the round-trip measurement harness.

Usage: ``python -m sheet_music_omr.harness <command> --run-dir DIR
--downloads DIR [options]`` with the commands ``preflight``, ``download``,
``select``, ``run``, ``report``, ``view`` and ``pilot``. A stop condition
is recorded in the run record and exits with status 2.
"""

from __future__ import annotations

import argparse
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

from sheet_music_omr.harness import steps as default_steps
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.downloads import (
    LIEDER_ZIP,
    PDMX_CSV,
    PDMX_MXL,
    DownloadError,
    fetch_file,
    urllib_fetch,
)
from sheet_music_omr.harness.gold import load_gold
from sheet_music_omr.harness.musescore import (
    DEFAULT_EXECUTABLE,
    MuseScore,
    registry_version,
    run_command,
)
from sheet_music_omr.harness.preflight import (
    PREFLIGHT_FIXTURE,
    PreflightError,
    open_ledger,
    preflight,
    run_calibration,
    tool_versions,
)
from sheet_music_omr.harness.privacy import GOLD_MAPPING
from sheet_music_omr.harness.records import (
    PROJECT_DIR,
    check_outside_repository,
    load_run_record,
    record_stop,
    run_git,
    source_state,
    write_json,
)
from sheet_music_omr.harness.report import write_report
from sheet_music_omr.harness.run import (
    PublicFile,
    Runner,
    RunPlan,
    begin_run,
    finish_run,
)
from sheet_music_omr.harness.screen import screen
from sheet_music_omr.harness.selection import (
    inputs_dir,
    load_selection,
    select_lieder,
    select_pdmx,
    selection_path,
    write_selection_markdown,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.view import view_file
from sheet_music_omr.harness.worker import WorkerPool

if TYPE_CHECKING:
    from collections.abc import Callable, Iterable, Mapping, Sequence

    from sheet_music_omr.harness.gold import GoldPiece
    from sheet_music_omr.harness.musescore import Launcher
    from sheet_music_omr.harness.preflight import Calibrate
    from sheet_music_omr.harness.records import Git
    from sheet_music_omr.harness.worker import Executor

STOP_STATUS = 2
"""The exit status of a stop condition."""

PUBLIC_CORPORA = ("pdmx", "lieder")
"""The public corpora in arm order."""


@dataclass(frozen=True, slots=True)
class Dependencies:
    """The external effects of the command line, replaceable in tests.

    Attributes:
        launcher: Runs MuseScore.
        version_lookup: Reads MuseScore's installed version.
        calibrate: Runs the calibration tests.
        git: Runs a read-only git command.
        fetch: Streams a download.
        executor: Makes the step executor from the temporary directory and
            the number of jobs.
        steps: The step table.
        executable: The MuseScore executable of the formal commands.
        settings: The MuseScore settings file of the formal commands;
            ``None`` is the protocol's default path.
    """

    launcher: Launcher = run_command
    version_lookup: Callable[[], str | None] = registry_version
    calibrate: Calibrate = run_calibration
    git: Git = run_git
    fetch: Callable[[str], Iterable[bytes]] = urllib_fetch
    executor: Callable[[Path, int], Executor] = WorkerPool
    steps: Mapping[str, Callable[..., object]] = field(
        default_factory=lambda: dict(default_steps.STEPS),
    )
    executable: Path = DEFAULT_EXECUTABLE
    settings: Path | None = None


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="python -m sheet_music_omr.harness",
        description="Run the W1 round-trip measurement protocol.",
    )
    commands = parser.add_subparsers(dest="command", required=True)

    def command(name: str, text: str) -> argparse.ArgumentParser:
        sub = commands.add_parser(name, help=text, description=text)
        sub.add_argument("--run-dir", type=Path, required=True)
        sub.add_argument("--downloads", type=Path, required=True)
        return sub

    def ledger(sub: argparse.ArgumentParser) -> None:
        sub.add_argument("--prior-total", type=int, default=None)
        sub.add_argument("--basis", default=None)

    sub = command("preflight", "Record versions and run calibration.")
    ledger(sub)
    sub = command("download", "Download PDMX and OpenScore Lieder.")
    ledger(sub)
    sub = command("select", "Select the PDMX and Lieder samples.")
    sub.add_argument(
        "--corpus",
        choices=("pdmx", "lieder", "all"),
        default="all",
    )
    sub = command("run", "Run every arm, the baseline and assembly test.")
    sub.add_argument("--gold-status", type=Path, required=True)
    sub.add_argument("--gold-cache", type=Path, required=True)
    sub.add_argument("--jobs", type=int, default=1)
    command("report", "Write the public and local reports.")
    sub = command("view", "Show one file's measure differences.")
    sub.add_argument("--id", required=True)
    sub.add_argument("--arm", default=None)
    sub.add_argument("--measure", type=int, default=None)
    sub = command("pilot", "Run the whole pipeline on repository fixtures.")
    sub.add_argument("--musescore", type=Path, default=None)
    sub.add_argument("--settings", type=Path, default=None)
    sub.add_argument("--fixtures", type=Path, nargs="+", default=None)
    sub.add_argument("--jobs", type=int, default=1)
    sub.add_argument("--skip-calibration", action="store_true")
    return parser


def main(
    argv: Sequence[str] | None = None,
    *,
    dependencies: Dependencies | None = None,
) -> int:
    """Run one command of the harness.

    Returns:
        0 on success, 1 on an error that is not a stop condition, and 2 on
        a stop condition, which is recorded in the run record.
    """
    arguments = _parser().parse_args(argv)
    effects = Dependencies() if dependencies is None else dependencies
    try:
        check_outside_repository(arguments.run_dir, arguments.downloads)
        confinement = Confinement.create(arguments.run_dir, arguments.downloads)
    except StopConditionError as error:
        print(f"stop: {error}", file=sys.stderr)  # noqa: T201
        return STOP_STATUS
    handler = _HANDLERS[arguments.command]
    try:
        handler(arguments, confinement, effects)
    except StopConditionError as error:
        record_stop(confinement, arguments.command, error)
        print(f"stop: {error}", file=sys.stderr)  # noqa: T201
        return STOP_STATUS
    except DownloadError as error:
        print(f"error: {error}", file=sys.stderr)  # noqa: T201
        return 1
    except KeyboardInterrupt:
        record_stop(
            confinement, arguments.command, KeyboardInterrupt("interrupted")
        )
        print("interrupted", file=sys.stderr)  # noqa: T201
        return 130
    return 0


def _musescore(
    confinement: Confinement,
    effects: Dependencies,
    *,
    executable: Path | None = None,
    settings: Path | None = None,
) -> MuseScore:
    return MuseScore.open(
        confinement,
        executable=effects.executable if executable is None else executable,
        settings=effects.settings if settings is None else settings,
        version_lookup=effects.version_lookup,
        launcher=effects.launcher,
    )


def _preflight(
    arguments: argparse.Namespace,
    confinement: Confinement,
    effects: Dependencies,
) -> None:
    preflight(
        confinement,
        _musescore(confinement, effects),
        prior_total=arguments.prior_total,
        basis=arguments.basis,
        calibrate=effects.calibrate,
        git=effects.git,
    )


def _require_preflight(
    confinement: Confinement,
    effects: Dependencies,
) -> dict[str, object]:
    """Check that the run's preflight calibrated the instrument used now.

    Returns:
        The preflight record.

    Raises:
        PreflightError: If the calibration has not passed, the recorded or
            current commit differs or is dirty, or a recorded tool version
            differs from the current one.
    """
    record = load_run_record(confinement).get("preflight")
    preflight = record if isinstance(record, dict) else {}
    calibration = preflight.get("calibration")
    status = (
        calibration.get("status") if isinstance(calibration, dict) else None
    )
    if status != "passed":
        message = "the preflight with calibration has not passed in this run"
        raise PreflightError(message)
    recorded = preflight.get("source")
    current = source_state(effects.git)
    if current["dirty"]:
        message = "the project source has uncommitted changes"
        raise PreflightError(message)
    if not isinstance(recorded, dict) or recorded != current:
        message = (
            "the preflight's commit is not the current clean commit; run a "
            "new preflight in a new run directory"
        )
        raise PreflightError(message)
    if preflight.get("versions") != tool_versions():
        message = "a tool version differs from the preflight's"
        raise PreflightError(message)
    return preflight


def _formal_musescore(
    confinement: Confinement,
    effects: Dependencies,
    preflight: Mapping[str, object],
) -> MuseScore:
    """Open MuseScore and check it against the preflight's record.

    Raises:
        PreflightError: If the version or settings hash differs from the
            preflight's.
    """
    musescore = _musescore(confinement, effects)
    recorded = preflight.get("musescore")
    recorded = recorded if isinstance(recorded, dict) else {}
    if recorded.get("version") != musescore.version:
        message = "the MuseScore version differs from the preflight's"
        raise PreflightError(message)
    if recorded.get("settings_sha256") != musescore.baseline:
        message = "the MuseScore settings hash differs from the preflight's"
        raise PreflightError(message)
    return musescore


def _download(
    arguments: argparse.Namespace,
    confinement: Confinement,
    effects: Dependencies,
) -> None:
    ledger = open_ledger(
        confinement,
        prior_total=arguments.prior_total,
        basis=arguments.basis,
    )
    for file in (PDMX_CSV, PDMX_MXL, LIEDER_ZIP):
        fetch_file(
            file, ledger=ledger, confinement=confinement, fetch=effects.fetch
        )


def _download_path(confinement: Confinement, name: str) -> Path:
    path = confinement.downloads / name
    if not path.is_file():
        message = f"the download {name} is absent; run download first"
        raise StopConditionError(message)
    return path


def _select(
    arguments: argparse.Namespace,
    confinement: Confinement,
    effects: Dependencies,
) -> None:
    musescore = _formal_musescore(
        confinement, effects, _require_preflight(confinement, effects)
    )
    corpora = (
        PUBLIC_CORPORA if arguments.corpus == "all" else (arguments.corpus,)
    )
    executor = effects.executor(confinement.temp, 1)
    try:
        if "pdmx" in corpora:
            select_pdmx(
                _download_path(confinement, PDMX_CSV.name),
                _download_path(confinement, PDMX_MXL.name),
                confinement=confinement,
                executor=executor,
                parse_check=effects.steps["parse_check"],
            )
        if "lieder" in corpora:
            select_lieder(
                _download_path(confinement, LIEDER_ZIP.name),
                confinement=confinement,
                executor=executor,
                parse_check=effects.steps["parse_check"],
                musescore=musescore,
            )
    finally:
        executor.close()
    records = [
        load_selection(confinement, corpus)
        for corpus in PUBLIC_CORPORA
        if selection_path(confinement, corpus).exists()
    ]
    write_selection_markdown(confinement, records)


def _public_files(
    confinement: Confinement,
    corpora: Sequence[str],
) -> tuple[PublicFile, ...]:
    files: list[PublicFile] = []
    for corpus in corpora:
        selected = load_selection(confinement, corpus).get("selected")
        for entry in selected if isinstance(selected, list) else []:
            if not isinstance(entry, dict):
                continue
            name = str(entry.get("id"))
            x = inputs_dir(confinement, corpus) / name / "x.musicxml"
            if not x.is_file():
                message = f"the selected input {corpus} {name} is missing"
                raise StopConditionError(message)
            files.append(PublicFile(name, corpus, str(entry.get("key")), x))
    return tuple(files)


def _write_gold_mapping(
    confinement: Confinement,
    arguments: argparse.Namespace,
    gold: Sequence[GoldPiece],
) -> None:
    write_json(
        confinement,
        confinement.run / GOLD_MAPPING,
        {
            "status": str(arguments.gold_status.resolve()),
            "cache": str(arguments.gold_cache.resolve()),
            "pieces": [
                {
                    "id": piece.id,
                    "workspace": piece.workspace,
                    "build": piece.path.parent.name,
                    "path": str(piece.path),
                    "sha256": piece.sha256,
                }
                for piece in gold
            ],
        },
    )


def _execute(
    confinement: Confinement,
    effects: Dependencies,
    musescore: MuseScore,
    plan: RunPlan,
    jobs: int,
) -> None:
    executor = effects.executor(confinement.temp, jobs)
    try:
        Runner(
            confinement,
            musescore,
            executor,
            effects.steps,
            jobs=jobs,
        ).run(plan)
    finally:
        executor.close()
    finish_run(confinement)


def _run(
    arguments: argparse.Namespace,
    confinement: Confinement,
    effects: Dependencies,
) -> None:
    musescore = _formal_musescore(
        confinement, effects, _require_preflight(confinement, effects)
    )
    source = source_state(effects.git)
    if not (confinement.run / "selection" / "selection.md").exists():
        message = "the selection lists have not been written"
        raise StopConditionError(message)
    public = _public_files(confinement, PUBLIC_CORPORA)
    gold = load_gold(arguments.gold_status, arguments.gold_cache)
    _write_gold_mapping(confinement, arguments, gold)
    plan = RunPlan(
        PUBLIC_CORPORA,
        public,
        gold,
        tuple((piece.id, piece.path) for piece in gold),
    )
    begin_run(confinement, plan, source)
    _execute(
        confinement,
        effects,
        musescore,
        plan,
        arguments.jobs,
    )


def _report(
    _arguments: argparse.Namespace,
    confinement: Confinement,
    _effects: Dependencies,
) -> None:
    write_report(confinement)


def _view(
    arguments: argparse.Namespace,
    confinement: Confinement,
    _effects: Dependencies,
) -> None:
    sys.stdout.write(
        view_file(
            confinement,
            arguments.id,
            arm=arguments.arm,
            measure=arguments.measure,
        ),
    )


def _fixture_key(path: Path) -> str:
    resolved = path.resolve()
    if resolved.is_relative_to(PROJECT_DIR):
        return resolved.relative_to(PROJECT_DIR).as_posix()
    return resolved.name


def _pilot(
    arguments: argparse.Namespace,
    confinement: Confinement,
    effects: Dependencies,
) -> None:
    musescore = _musescore(
        confinement,
        effects,
        executable=arguments.musescore,
        settings=arguments.settings,
    )
    preflight(
        confinement,
        musescore,
        calibrate=None if arguments.skip_calibration else effects.calibrate,
        git=effects.git,
        require_ledger=False,
    )
    fixtures = arguments.fixtures or [PREFLIGHT_FIXTURE]
    public: list[PublicFile] = []
    screened: list[dict[str, object]] = []
    for number, fixture in enumerate(fixtures, start=1):
        name = f"t{number:04d}"
        target = inputs_dir(confinement, "pilot") / name / "x.musicxml"
        data = fixture.read_bytes()
        if not target.exists():
            confinement.write_bytes(target, data)
        key = _fixture_key(fixture)
        result = screen(data)
        screened.append(
            {
                "id": name,
                "key": key,
                "screen": result.reason,
                "detail": result.detail,
            },
        )
        public.append(PublicFile(name, "pilot", key, target))
    write_json(
        confinement,
        confinement.run / "selection" / "pilot-screen.json",
        screened,
    )
    plan = RunPlan(
        ("pilot",),
        tuple(public),
        (),
        tuple((file.id, file.x) for file in public),
    )
    begin_run(confinement, plan, source_state(effects.git))
    _execute(confinement, effects, musescore, plan, arguments.jobs)
    write_report(confinement)
    shutil.rmtree(confinement.temp, ignore_errors=True)
    confinement.temp.mkdir(parents=True, exist_ok=True)


_HANDLERS: Mapping[
    str,
    Callable[[argparse.Namespace, Confinement, Dependencies], None],
] = {
    "preflight": _preflight,
    "download": _download,
    "select": _select,
    "run": _run,
    "report": _report,
    "view": _view,
    "pilot": _pilot,
}
