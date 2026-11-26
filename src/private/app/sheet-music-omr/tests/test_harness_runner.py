"""Scenario tests for the round-trip runner, its records and its report.

MuseScore is replaced by a launcher that copies its input, so ``R(X)`` is
``X``; the steps run inline. The scenarios cover the pilot end to end, the
failure accounting, resumption and every stop the runner owns.
"""

from __future__ import annotations

import hashlib
import json
import shutil
import threading
from concurrent.futures import Future, ThreadPoolExecutor
from pathlib import Path
from typing import TYPE_CHECKING, ParamSpec, TypeVar

import pytest
from tokenizers import Tokenizer
from tokenizers.models import BPE
from tokenizers.pre_tokenizers import ByteLevel

from sheet_music_omr.harness import cli, steps
from sheet_music_omr.harness import downloads as dl_mod
from sheet_music_omr.harness import run as runner_module
from sheet_music_omr.harness.cli import Dependencies, main
from sheet_music_omr.harness.confine import Confinement
from sheet_music_omr.harness.downloads import (
    HuggingFaceFile,
    Ledger,
    git_blob_oid_file,
)
from sheet_music_omr.harness.gold import (
    GoldError,
    GoldPiece,
    check_unchanged,
    gold_id,
    load_gold,
)
from sheet_music_omr.harness.musescore import VERSION, MuseScore
from sheet_music_omr.harness.privacy import (
    GOLD_MAPPING,
    PrivacyError,
    private_strings,
    scan,
)
from sheet_music_omr.harness.records import RecordError, read_object, write_json
from sheet_music_omr.harness.report import (
    INCOMPLETE,
    public_markdown,
    summarize,
    write_report,
)
from sheet_music_omr.harness.req012 import (
    Req012ReplayStopError,
    replay_req012,
)
from sheet_music_omr.harness.run import (
    GOLD_ARM,
    PublicFile,
    Runner,
    RunPlan,
    arms,
    begin_run,
    failed_tally,
    finish_run,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.tokenizer_counts import run_tokenizer_counts
from sheet_music_omr.harness.worker import (
    InlineExecutor,
    Outcome,
    StepStopError,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Iterator, Mapping, Sequence

FIXTURE = Path(__file__).parent / "fixtures" / "comparison" / "base.musicxml"
COMMIT = "0" * 40
_P = ParamSpec("_P")
_T = TypeVar("_T")


class CopyLauncher:
    """Copy a conversion's input to its output, or fail chosen targets."""

    def __init__(self, *, fail: str | None = None) -> None:
        """Fail every conversion whose target has the name ``fail``."""
        self.commands: list[list[str]] = []
        self.fail = fail
        self.settings: Path | None = None

    def __call__(
        self,
        command: Sequence[str],
        timeout: float,
    ) -> tuple[int | None, str]:
        """Convert by copying."""
        del timeout
        self.commands.append(list(command))
        target = Path(command[command.index("-o") + 1])
        if target.name == self.fail:
            return 1, "failed"
        shutil.copyfile(command[-1], target)
        if self.settings is not None:
            self.settings.write_text(
                "[import]\nmusicXml\\importBreaks=false\n",
                encoding="utf-8",
            )
        return 0, "converted"


def git(arguments: Sequence[str]) -> str:
    """Answer the read-only git commands of the source state."""
    return COMMIT if arguments[0] == "rev-parse" else ""


def calibrate(tests: Sequence[Path], temp: Path) -> tuple[int, str]:
    """Pass the calibration without running it."""
    del tests, temp
    return 0, "passed"


@pytest.fixture
def roots(tmp_path: Path) -> Confinement:
    """Return fresh run and download roots."""
    return Confinement.create(tmp_path / "run", tmp_path / "downloads")


def _musescore(
    roots: Confinement,
    tmp_path: Path,
    launcher: CopyLauncher,
) -> MuseScore:
    executable = tmp_path / "MuseScore4.exe"
    executable.write_bytes(b"")
    settings = tmp_path / "MuseScore4.ini"
    settings.write_text("[ui]\n", encoding="utf-8")
    return MuseScore.open(
        roots,
        executable=executable,
        settings=settings,
        version_lookup=lambda: VERSION,
        launcher=launcher,
    )


def _no_fetch(url: str) -> Iterator[bytes]:
    message = f"no network in tests: {url}"
    raise AssertionError(message)


def _inline(temp: Path, jobs: int) -> InlineExecutor:
    del temp, jobs
    return InlineExecutor()


def _dependencies(
    launcher: CopyLauncher,
    tmp_path: Path,
    *,
    source: Callable[[Sequence[str]], str] = git,
) -> Dependencies:
    executable = tmp_path / "MuseScore4.exe"
    executable.write_bytes(b"")
    settings = tmp_path / "MuseScore4.ini"
    if not settings.exists():
        settings.write_text("[ui]\n", encoding="utf-8")
    return Dependencies(
        launcher=launcher,
        version_lookup=lambda: VERSION,
        calibrate=calibrate,
        git=source,
        fetch=_no_fetch,
        executor=_inline,
        executable=executable,
        settings=settings,
    )


def _cli_arguments(tmp_path: Path, command: str, *extra: str) -> list[str]:
    executable = tmp_path / "MuseScore4.exe"
    if not executable.exists():
        executable.write_bytes(b"")
    settings = tmp_path / "MuseScore4.ini"
    if not settings.exists():
        settings.write_text("[ui]\n", encoding="utf-8")
    return [
        command,
        "--run-dir",
        str(tmp_path / "run"),
        "--downloads",
        str(tmp_path / "downloads"),
        *(
            ["--musescore", str(executable), "--settings", str(settings)]
            if command == "pilot"
            else []
        ),
        *extra,
    ]


def _result(tmp_path: Path, arm: str, name: str) -> dict[str, object]:
    return read_object(tmp_path / "run" / "files" / arm / name / "result.json")


def test_pilot_runs_every_phase_and_resumes(
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    """A pilot on one fixture fills every arm, baseline and report."""
    launcher = CopyLauncher()
    dependencies = _dependencies(launcher, tmp_path)
    arguments = _cli_arguments(tmp_path, "pilot", "--fixtures", str(FIXTURE))

    assert main(arguments, dependencies=dependencies) == 0

    run = tmp_path / "run"
    x = _result(tmp_path, "pilot-x", "t0001")
    rx = _result(tmp_path, "pilot-rx", "t0001")
    for result in (x, rx):
        assert result["failed"] is False
        assert result["import_check"] == "passed"
        record = result["steps"]
        assert isinstance(record, dict)
        assert record["import_check"]["status"] == "passed"
        assert result["text_space"] == "consistent"
        content = result["content"]
        assert isinstance(content, dict)
        assert content["elements"] == content["identical"]
    assert (
        read_object(run / "inputs" / "pilot" / "t0001" / "rx.json")["status"]
        == "ok"
    )
    assert _result(tmp_path, "baseline", "t0001")["status"] == "ok"
    assert "status" in _result(tmp_path, "assembly", "t0001")
    public = (run / "report" / "public.md").read_text(encoding="utf-8")
    assert "pilot-rx" in public
    assert str(tmp_path) not in public
    statements = [line for line in public.splitlines() if line[:2] == "- "]
    assert statements
    assert all(
        line.endswith(("(runtime observation).", "(source finding)."))
        for line in statements
    )
    assert not list((run / "tmp").iterdir())
    imports = [c for c in launcher.commands if c[2].endswith("t.mscz")]
    assert len(imports) == 2  # noqa: PLR2004 - one per arm

    calls = len(launcher.commands)
    assert main(arguments, dependencies=dependencies) == 0
    assert len(launcher.commands) == calls + 1  # only the preflight again

    capsys.readouterr()
    view = _cli_arguments(tmp_path, "view", "--id", "t0001", "--measure", "0")
    assert main(view, dependencies=dependencies) == 0
    shown = capsys.readouterr().out
    assert "# pilot-x t0001" in shown
    assert "## measure ordinal 0" in shown
    assert "Y:\n  m " in shown


def test_pilot_allows_dirty_source_in_fresh_run_dir(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A fresh pilot allows uncommitted edits while formal runs reject them."""
    plan = _plan(roots, ["p0001"])
    with pytest.raises(RecordError, match="uncommitted changes"):
        begin_run(roots, plan, {"commit": COMMIT, "dirty": True})

    def dirty_git(arguments: Sequence[str]) -> str:
        if arguments[0] == "rev-parse":
            return COMMIT
        if arguments[0] == "status":
            return " M src/sheet_music_omr/parser.py\n"
        return ""

    pilot_root = tmp_path / "dirty_pilot"
    pilot_root.mkdir()
    launcher = CopyLauncher()
    dependencies = _dependencies(launcher, pilot_root, source=dirty_git)
    arguments = _cli_arguments(
        pilot_root,
        "pilot",
        "--fixtures",
        str(FIXTURE),
    )

    assert main(arguments, dependencies=dependencies) == 0
    recorded = read_object(pilot_root / "run" / "run.json")
    run_meta = recorded["run"]
    assert isinstance(run_meta, dict)
    assert run_meta["dirty"] is True
    assert main(arguments, dependencies=dependencies) == 2  # noqa: PLR2004 - stop on dirty resume


def test_pilot_in_worker_processes_matches_the_inline_run(
    tmp_path: Path,
) -> None:
    """Spawned workers with two jobs give the inline run's results."""
    launcher = CopyLauncher()
    dependencies = Dependencies(
        launcher=launcher,
        version_lookup=lambda: VERSION,
        calibrate=calibrate,
        git=git,
        fetch=_no_fetch,
    )
    arguments = _cli_arguments(
        tmp_path,
        "pilot",
        "--jobs",
        "2",
        "--fixtures",
        str(FIXTURE),
        str(FIXTURE),
    )

    assert main(arguments, dependencies=dependencies) == 0

    for name in ("t0001", "t0002"):
        result = _result(tmp_path, "pilot-rx", name)
        assert result["failed"] is False
        content = result["content"]
        assert isinstance(content, dict)
        assert content["elements"] == content["identical"]


def _plan(roots: Confinement, names: Sequence[str]) -> RunPlan:
    files = []
    for name in names:
        x = roots.run / "inputs" / "pdmx" / name / "x.musicxml"
        roots.write_bytes(x, FIXTURE.read_bytes())
        files.append(PublicFile(name, "pdmx", f"mxl/{name}.mxl", x))
    return RunPlan(("pdmx",), tuple(files), (), ())


def _counting(content: int) -> Callable[..., object]:
    def count(y: Path) -> dict[str, object]:
        size = content + (1 if y.name == "rx.musicxml" else 0)
        return {"status": "ok", "content": size, "layout": 3}

    return count


def _unsupported(y: Path) -> dict[str, object]:
    del y
    return {"status": "unsupported", "codes": ["unsupported/staff_count"]}


def test_failed_files_count_every_reference_element(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A failed file counts Y, or X when R(X) is missing, as non-identical."""
    launcher = CopyLauncher(fail="rx.musicxml")
    plan = _plan(roots, ["p0001"])
    table = {
        **steps.STEPS,
        "count": _counting(10),
        "reverse": _unsupported,
    }
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, launcher),
        InlineExecutor(),
        table,
    ).run(plan)

    x = _result(tmp_path, "pdmx-x", "p0001")
    assert (x["failure"], x["count_source"]) == ("unsupported", "y")
    assert x["content"] == {
        "elements": 10,
        "identical": 0,
        "extras": 0,
        "fidelity": 0.0,
    }
    steps_run = x["steps"]
    assert isinstance(steps_run, dict)
    assert steps_run["content"] == {"status": "not-run"}
    rx = _result(tmp_path, "pdmx-rx", "p0001")
    assert (rx["failure"], rx["count_source"]) == ("rx-export-failed", "x")
    assert rx["content"] == x["content"]
    assert _result(tmp_path, "baseline", "p0001")["status"] == "rx-missing"

    summary = summarize(roots)
    arms = summary["arms"]
    assert isinstance(arms, dict)
    pooled = arms["pdmx-x"]["content"]
    assert (pooled["identical"], pooled["denominator"]) == (0, 10)
    assert arms["pdmx-rx"]["failures"] == {"rx-export-failed": 1}
    assert summary["failed_public_files"] == [
        ["pdmx-rx", "mxl/p0001.mxl", "rx-export-failed"],
        ["pdmx-x", "mxl/p0001.mxl", "unsupported"],
    ]


def test_unparsable_reference_and_failed_count_are_resolved(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """An unparsable R(X) is counted on X; an uncountable X stops the run."""
    launcher = CopyLauncher()
    plan = _plan(roots, ["p0001"])

    def count(y: Path) -> dict[str, object]:
        if y.name == "rx.musicxml":
            return {"status": "unparsable", "detail": "bad"}
        return {"status": "ok", "content": 7, "layout": 2}

    def content(t: Path, y: Path) -> dict[str, object]:
        del t
        if y.name == "rx.musicxml":
            return {"status": "unparsable"}
        return steps.content(FIXTURE, FIXTURE)

    table = {**steps.STEPS, "count": count, "content": content}
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    musescore = _musescore(roots, tmp_path, launcher)
    Runner(roots, musescore, InlineExecutor(), table).run(plan)
    rx = _result(tmp_path, "pdmx-rx", "p0001")
    assert rx["failures"] == ["rx-unparsable"]
    assert rx["count_source"] == "x"
    assert _result(tmp_path, "pdmx-x", "p0001")["failed"] is False

    other = _plan(roots, ["p0002"])
    table["count"] = lambda y: {"status": "unparsable", "detail": str(y)}
    table["reverse"] = _unsupported
    with pytest.raises(StopConditionError, match="cannot parse X"):
        Runner(roots, musescore, InlineExecutor(), table).run(other)


def test_import_check_failure_is_reported_and_its_output_deleted(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A failed import is reported only; its temporary output is deleted."""
    launcher = CopyLauncher(fail="t.mscz")
    plan = _plan(roots, ["p0001"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, launcher),
        InlineExecutor(),
    ).run(plan)
    x = _result(tmp_path, "pdmx-x", "p0001")
    assert (x["failed"], x["import_check"]) == (False, "failed")
    record = x["steps"]
    assert isinstance(record, dict)
    assert record["import_check"]["deleted"] is True
    assert not list(roots.temp.iterdir())


def test_stops_end_the_run(roots: Confinement, tmp_path: Path) -> None:
    """A stop in a step, a settings change or a changed plan ends the run."""
    plan = _plan(roots, ["p0001"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    with pytest.raises(RecordError, match="frozen"):
        begin_run(roots, plan, {"commit": "1" * 40, "dirty": False})
    with pytest.raises(RecordError, match="plan"):
        begin_run(roots, _plan(roots, ["p0002"]), {"commit": COMMIT})

    def defect(t: Path, y: Path) -> None:
        del t, y
        message = "defect"
        raise StopConditionError(message)

    launcher = CopyLauncher()
    musescore = _musescore(roots, tmp_path, launcher)
    table = {**steps.STEPS, "content": defect}
    with pytest.raises(StepStopError, match="step content"):
        Runner(roots, musescore, InlineExecutor(), table).run(plan)
    assert not (
        roots.run / "files" / "pdmx-x" / "p0001" / "result.json"
    ).exists()

    launcher.settings = tmp_path / "MuseScore4.ini"
    with pytest.raises(StopConditionError, match="MusicXML preferences"):
        Runner(roots, musescore, InlineExecutor()).run(plan)


def test_parallel_stop_interrupts_a_running_file_between_steps(
    roots: Confinement,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Stops a concurrent peer before further work or result publication."""
    plan = _plan(roots, ["p0001", "p0002"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    launcher = CopyLauncher()
    waiting = threading.Event()
    release = threading.Event()
    origin_waiting = threading.Event()
    release_origin = threading.Event()
    peer_finished = threading.Event()
    forwarded: list[str] = []
    origin_thread: int | None = None

    def reverse(y: Path) -> dict[str, object]:
        if y.parent.name == "p0002":
            waiting.set()
            assert release.wait(timeout=5)
        return {"status": "ok", "text": y.parent.name}

    def forward(text: str) -> dict[str, object]:
        forwarded.append(text)
        return {"status": "ok", "musicxml": FIXTURE.read_bytes()}

    def content(t: Path, y: Path) -> dict[str, object]:
        nonlocal origin_thread
        if y.parent.name == "p0001":
            assert waiting.wait(timeout=5)
            origin_thread = threading.get_ident()
            message = "parallel protocol defect"
            raise StopConditionError(message)
        return steps.content(t, y)

    runner = Runner(
        roots,
        _musescore(roots, tmp_path, launcher),
        InlineExecutor(),
        {
            **steps.STEPS,
            "reverse": reverse,
            "forward": forward,
            "content": content,
        },
        jobs=2,
    )
    original_set = runner._stopping.set  # noqa: SLF001

    def delayed_set() -> None:
        original_set()
        if threading.get_ident() == origin_thread:
            origin_waiting.set()
            assert release_origin.wait(timeout=10)

    class TrackingPool(ThreadPoolExecutor):
        def submit(
            self,
            fn: Callable[_P, _T],
            /,
            *args: _P.args,
            **kwargs: _P.kwargs,
        ) -> Future[_T]:
            future = super().submit(fn, *args, **kwargs)
            future.add_done_callback(lambda _: peer_finished.set())
            return future

    monkeypatch.setattr(runner._stopping, "set", delayed_set)  # noqa: SLF001
    monkeypatch.setattr(runner_module, "ThreadPoolExecutor", TrackingPool)
    try:
        with ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(runner.run, plan)
            assert waiting.wait(timeout=5)
            assert origin_waiting.wait(timeout=5)
            release.set()
            assert peer_finished.wait(timeout=5)
            release_origin.set()
            with pytest.raises(StepStopError, match="parallel protocol defect"):
                future.result(timeout=5)
    finally:
        release.set()
        release_origin.set()

    assert forwarded == ["p0001"]
    assert not (
        roots.run / "files" / "pdmx-x" / "p0002" / "result.json"
    ).exists()


def test_parallel_stop_prevents_baseline_result_publication(
    roots: Confinement,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A baseline cannot publish a result after a peer triggers a stop."""
    plan = _plan(roots, ["p0001", "p0002"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    for file in plan.public:
        write_json(roots, file.x.parent / "rx.json", {"status": "ok"})

    operations_waiting = threading.Event()
    release_operations = threading.Event()
    original_write = runner_module.write_json

    def delayed_write(
        confinement: Confinement,
        path: Path,
        value: dict[str, object],
    ) -> None:
        if (
            path.name == "operations.json"
            and path.parent.parent.name == "baseline"
            and path.parent.name == "p0001"
        ):
            operations_waiting.set()
            assert release_operations.wait(timeout=10)
        original_write(confinement, path, value)

    def baseline_part(t: Path, y: Path) -> dict[str, object]:
        del t
        if y.parent.name == "p0002":
            assert operations_waiting.wait(timeout=5)
            message = "baseline protocol defect"
            raise StopConditionError(message)
        return {"status": "ok"}

    table = {
        **steps.STEPS,
        "baseline_content": baseline_part,
        "baseline_layout": baseline_part,
    }
    runner = Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        InlineExecutor(),
        table,
        jobs=2,
    )
    monkeypatch.setattr(runner_module, "write_json", delayed_write)
    try:
        with ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(runner.baselines, plan.public)
            assert operations_waiting.wait(timeout=5)
            assert runner._stopping.wait(timeout=5)  # noqa: SLF001
            release_operations.set()
            with pytest.raises(
                StopConditionError, match="baseline protocol defect"
            ):
                future.result(timeout=5)
    finally:
        release_operations.set()

    for name in ("p0001", "p0002"):
        assert not (
            roots.run / "files" / "baseline" / name / "result.json"
        ).exists()


def test_cli_records_a_stop_and_exits_with_two(tmp_path: Path) -> None:
    """A run without a passed preflight stops, and the stop is recorded."""
    gold = tmp_path / "status.json"
    gold.write_text("[]", encoding="utf-8")
    arguments = _cli_arguments(
        tmp_path,
        "run",
        "--gold-status",
        str(gold),
        "--gold-cache",
        str(tmp_path),
    )
    dependencies = _dependencies(CopyLauncher(), tmp_path)
    assert main(arguments, dependencies=dependencies) == cli.STOP_STATUS
    record = json.loads((tmp_path / "run" / "run.json").read_text("utf-8"))
    assert record["stops"][0]["phase"] == "run"
    inside = [
        "report",
        "--run-dir",
        str(cli.PROJECT_DIR / "run"),
        "--downloads",
        str(tmp_path / "downloads"),
    ]
    assert main(inside, dependencies=dependencies) == cli.STOP_STATUS
    assert not (cli.PROJECT_DIR / "run").exists()


def test_view_errors_stay_read_only_and_preflight_pins_identity(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """View never mutates run.json on error; preflight pins provenance."""
    launcher = CopyLauncher()
    dependencies = _dependencies(launcher, tmp_path)
    pilot_args = _cli_arguments(tmp_path, "pilot", "--fixtures", str(FIXTURE))
    assert main(pilot_args, dependencies=dependencies) == 0

    run_record_path = tmp_path / "run" / "run.json"
    before = run_record_path.read_text(encoding="utf-8")

    corrupt = tmp_path / "run" / "files" / "pilot-x" / "t0001" / "result.json"
    corrupt.write_text("{", encoding="utf-8")
    view_args = _cli_arguments(
        tmp_path, "view", "--id", "t0001", "--measure", "0"
    )
    assert main(view_args, dependencies=dependencies) == cli.STOP_STATUS
    assert run_record_path.read_text(encoding="utf-8") == before

    def interrupt_view(*_args: object) -> None:
        raise KeyboardInterrupt

    monkeypatch.setitem(cli._HANDLERS, "view", interrupt_view)  # noqa: SLF001
    assert main(view_args, dependencies=dependencies) == 130  # noqa: PLR2004
    assert run_record_path.read_text(encoding="utf-8") == before

    preflight_args = [
        "preflight",
        "--run-dir",
        str(tmp_path / "run"),
        "--downloads",
        str(tmp_path / "downloads"),
        "--prior-total",
        "0",
        "--basis",
        "test",
    ]
    failing_deps = Dependencies(
        launcher=launcher,
        version_lookup=lambda: VERSION,
        calibrate=lambda _tests, _temp: (1, "failed"),
        git=git,
        fetch=_no_fetch,
        executor=_inline,
        executable=dependencies.executable,
        settings=dependencies.settings,
    )
    assert main(preflight_args, dependencies=failing_deps) == cli.STOP_STATUS
    failed_record = json.loads(run_record_path.read_text(encoding="utf-8"))
    assert failed_record["preflight"]["calibration"]["status"] == "failed"
    assert failed_record["preflight"]["musescore"]["executable_sha256"] == (
        hashlib.sha256(b"").hexdigest()
    )

    before_calls = len(launcher.commands)
    assert dependencies.executable is not None
    dependencies.executable.write_bytes(b"upgraded-musescore-binary")
    assert main(preflight_args, dependencies=dependencies) == cli.STOP_STATUS
    assert len(launcher.commands) == before_calls
    after_record = json.loads(run_record_path.read_text(encoding="utf-8"))
    assert after_record["preflight"]["musescore"]["executable_sha256"] == (
        hashlib.sha256(b"").hexdigest()
    )


def _gold_status(tmp_path: Path, records: list[dict[str, object]]) -> Path:
    path = tmp_path / "status.json"
    path.write_text(json.dumps({"batches": records}), encoding="utf-8")
    return path


def test_gold_pieces_are_ordered_hashed_and_frozen(tmp_path: Path) -> None:
    """Accepted pieces load by number; a missing or changed X stops."""
    cache = tmp_path / "cache"
    for workspace in ("song-b", "song-a"):
        score = cache / workspace / "build-1" / "score.musicxml"
        score.parent.mkdir(parents=True)
        score.write_bytes(workspace.encode())
    status = _gold_status(
        tmp_path,
        [
            {
                "number": 7,
                "workspace": "song-b",
                "latest_build": "build-1",
                "accepted": True,
            },
            {
                "number": 2,
                "workspace": "song-a",
                "latest_build": "build-1",
                "accepted": True,
            },
            {"number": 3, "workspace": "draft", "accepted": False},
        ],
    )
    pieces = load_gold(status, cache)
    assert [p.id for p in pieces] == ["gold-02", "gold-07"]
    pieces[0].path.write_bytes(b"edited")
    with pytest.raises(GoldError, match="gold-02 changed"):
        check_unchanged(pieces[0])
    missing = _gold_status(
        tmp_path,
        [
            {
                "number": 1,
                "workspace": "absent",
                "latest_build": "b",
                "accepted": True,
            }
        ],
    )
    with pytest.raises(GoldError, match="gold-01 is missing"):
        load_gold(missing, cache)
    with pytest.raises(GoldError, match="no accepted"):
        load_gold(_gold_status(tmp_path, []), cache)


def test_privacy_scan_refuses_private_text(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A private path, workspace name or absolute path stops a report."""
    scan(
        "pilot-x fidelity 100% </note> from https://zenodo.org/records/14648209",
        ["song-a", r"Q:\runs"],
    )
    for text in (
        "piece song-a failed",
        "see q:/runs/x",
        r"C:\Users\x",
        "unc //wsl.localhost/Ubuntu/tmp/run",
        "backtick `//wsl.localhost/Ubuntu/data/x`",
        "custom `/d/Gold Scores/Piece 1.mxl`",
        "user /c/Users/alice/score.mxl",
        r"share \\server\share\x",
    ):
        with pytest.raises(PrivacyError):
            scan(text, ["song-a", r"Q:\runs"])

    confinement = Confinement.create(tmp_path / "run", tmp_path / "downloads")
    monkeypatch.setenv("USERPROFILE", r"C:\Users\DistinctWinAccount")
    collected = private_strings(confinement)
    assert "DistinctWinAccount" in collected
    with pytest.raises(PrivacyError):
        scan("account DistinctWinAccount leaked", collected)


class TimeoutExecutor(InlineExecutor):
    """Run steps inline, but time out one chosen step."""

    def __init__(self, step: Callable[..., object]) -> None:
        """Time out every call of ``step``."""
        self.step = step

    def call(
        self,
        step: Callable[..., object],
        arguments: Mapping[str, object],
        *,
        context: str,
        timeout: float = 300,
    ) -> Outcome:
        """Return a timeout for the chosen step; run the others."""
        if step is self.step:
            return Outcome("timeout", seconds=timeout)
        return super().call(step, arguments, context=context, timeout=timeout)


def test_a_step_timeout_fails_the_file_in_both_passes(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A timed-out step fails the file; both passes count Y as missed."""
    plan = _plan(roots, ["p0001"])
    table = {**steps.STEPS, "count": _counting(10)}
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        TimeoutExecutor(table["forward"]),
        table,
    ).run(plan)

    for arm in ("pdmx-x", "pdmx-rx"):
        result = _result(tmp_path, arm, "p0001")
        assert result["failures"] == ["forward-timeout"]
        assert result["content"] == failed_tally(10 + (arm == "pdmx-rx"))
        assert result["layout"] == failed_tally(3)


def _gold_piece(tmp_path: Path, number: int) -> GoldPiece:
    path = tmp_path / "cache" / f"song-{number}" / "build-1" / "x.musicxml"
    path.parent.mkdir(parents=True)
    path.write_bytes(FIXTURE.read_bytes())
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    return GoldPiece(gold_id(number), number, f"song-{number}", path, digest)


def test_gold_failures_return_to_the_owner_and_a_change_stops(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Unsupported and unparsable gold X return; a changed X stops."""
    pieces = tuple(_gold_piece(tmp_path, n) for n in (1, 2, 3))
    plan = RunPlan((), (), pieces, ())

    def reverse(y: Path) -> object:
        if "song-1" in y.parts:
            return _unsupported(y)
        return steps.reverse(y)

    def count(y: Path) -> dict[str, object]:
        if "song-2" in y.parts:
            return {"status": "unparsable", "detail": "bad"}
        return {"status": "ok", "content": 10, "layout": 3}

    def content(t: Path, y: Path) -> object:
        if "song-2" in y.parts:
            return {"status": "unparsable"}
        return steps.content(t, y)

    table = {
        **steps.STEPS,
        "reverse": reverse,
        "count": count,
        "content": content,
    }
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})

    class EditingLauncher(CopyLauncher):
        """Edit gold-03's X during gold-02's import check."""

        def __call__(
            self,
            command: Sequence[str],
            timeout: float,
        ) -> tuple[int | None, str]:
            if "gold-02" in command[-1]:
                pieces[2].path.write_bytes(b"edited")
            return super().__call__(command, timeout)

    musescore = _musescore(roots, tmp_path, EditingLauncher())
    with pytest.raises(GoldError, match="gold-03 changed"):
        Runner(roots, musescore, InlineExecutor(), table).run(plan)

    first = _result(tmp_path, GOLD_ARM, "gold-01")
    assert (first["failure"], first["returns_to_owner"]) == (
        "unsupported",
        True,
    )
    assert first["content"] == failed_tally(10)
    second = _result(tmp_path, GOLD_ARM, "gold-02")
    assert (second["failure"], second["returns_to_owner"]) == (
        "x-unparsable",
        True,
    )
    assert second["content"] is None
    gold = json.loads(json.dumps(summarize(roots)))["arms"][GOLD_ARM]
    assert (gold["completed"], gold["planned"]) == (2, 3)
    assert gold["content"]["fidelity"] is None
    assert gold["returns_to_owner"] == 2  # noqa: PLR2004


def test_an_unfinished_arm_has_no_pooled_figure(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A stopped run's arm figures are not computable; others print."""
    plan = _plan(roots, ["p0001", "p0002"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    musescore = _musescore(roots, tmp_path, CopyLauncher())
    runner = Runner(roots, musescore, InlineExecutor())
    runner.make_rx(plan.public)
    for item in arms(plan)[0][1][:1]:
        runner.process(item)
    runner.baselines(plan.public)

    public = public_markdown(summarize(roots))
    row = next(line for line in public.splitlines() if "| pdmx-x |" in line)
    assert row.startswith("| pdmx-x | 1 of 2 | 0 | " + INCOMPLETE)
    assert (
        "Whole-score symbols of files without failure: " + INCOMPLETE in public
    )
    assert "content status ok 2; layout status ok 2" in public


def test_whole_score_figures_and_baseline_status_are_public(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """The report prints symbol counts, the distance and baseline status."""
    plan = _plan(roots, ["p0001"])

    def baseline_unparsable(t: Path, y: Path) -> dict[str, object]:
        del t, y
        return {"status": "unparsable"}

    table = {
        **steps.STEPS,
        "baseline_content": baseline_unparsable,
        "baseline_layout": baseline_unparsable,
    }
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        InlineExecutor(),
        table,
    ).run(plan)

    summary = summarize(roots)
    public = public_markdown(summary)
    arm = json.loads(json.dumps(summary))["arms"]["pdmx-x"]
    figures = arm["figures"]
    assert (
        f"T {figures['whole_score_symbols_t']}, reference "
        f"{figures['whole_score_symbols_y']}; normalized distance per file "
        f"mean {arm['normalized_distance']['mean']}"
    ) in public
    assert (
        "status ok 1; content status unparsable 1; layout status unparsable 1"
        in public
    )


def _both_up() -> bytes:
    """Return the fixture with both voices of measure 3, staff 1, stem up."""
    text = FIXTURE.read_text(encoding="utf-8")
    start = text.index('<note id="m3s1n2">')
    end = text.index("</note>", start)
    note = text[start:end]
    assert note.count("<stem>down</stem>") == 1
    up = note.replace("<stem>down</stem>", "<stem>up</stem>")
    return (text[:start] + up + text[end:]).encode("utf-8")


def test_excluded_reference_voices_are_reported_by_arm_and_reason(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Reference voices the format cannot place are summed and printed."""
    plan = _plan(roots, ["p0001"])
    roots.write_bytes(plan.public[0].x, _both_up())
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        InlineExecutor(),
    ).run(plan)

    reason = "indistinguishable-voices"
    content = _result(tmp_path, "pdmx-x", "p0001")["content_pass"]
    assert isinstance(content, dict)
    assert content["excluded_voices"] == {reason: 2}
    summary = json.loads(json.dumps(summarize(roots)))
    expected = {"total": 2, "by_reason": {reason: 2}}
    for arm in ("pdmx-x", "pdmx-rx"):
        assert summary["arms"][arm]["excluded_voices"] == expected
        voice_order = summary["arms"][arm]["voice_order"]
        assert voice_order["identical"] == voice_order["denominator"]
    assert summary["baseline"]["pdmx"]["excluded_voices"] == expected
    public = public_markdown(summarize(roots))
    assert (
        "excluded reference voices, neither elements nor agreement: 2 "
        f"(by reason: {reason} 2)"
    ) in public
    assert "excluded reference voices 2. Never counted" in public


def test_assembly_reports_passed_pieces_with_excluded_voices(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A passed assembly piece with exclusions is counted apart."""
    excluded = {"with.musicxml": 2, "without.musicxml": 0}
    for name in excluded:
        (tmp_path / name).write_bytes(FIXTURE.read_bytes())
    plan = RunPlan(
        (),
        (),
        (),
        (
            ("gold-01", tmp_path / "with.musicxml"),
            ("gold-02", tmp_path / "without.musicxml"),
        ),
    )

    def assembly(x: Path) -> dict[str, object]:
        return {
            "status": "compared-pass",
            "passed": True,
            "systems": 2,
            "texts_differ": True,
            "differences": 0,
            "category_counts": {},
            "excluded_voices": excluded[x.name],
            "detail": "",
        }

    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        InlineExecutor(),
        {**steps.STEPS, "assembly": assembly},
    ).run(plan)

    summary = summarize(roots)
    assembled = json.loads(json.dumps(summary))["assembly"]
    assert (assembled["passed"], assembled["passed_with_excluded_voices"]) == (
        2,
        1,
    )
    assert (
        "- Excluded reference voices in the compared texts: 2; passed pieces "
        "with any: 1"
    ) in public_markdown(summary)


def test_write_report_refuses_a_gold_identity_in_public_text(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """A gold workspace name in a public key stops the public report."""
    plan = _plan(roots, ["p0001"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher(fail="rx.musicxml")),
        InlineExecutor(),
    ).run(plan)
    write_json(
        roots,
        roots.run / GOLD_MAPPING,
        {"pieces": [{"id": "gold-01", "workspace": "p0001"}]},
    )

    with pytest.raises(PrivacyError):
        write_report(roots)
    assert not (roots.run / "report" / "public.md").exists()


@pytest.fixture(autouse=True)
def _allow_w1_fixture_runner(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(dl_mod, "_W1_CAMPAIGN_RETIRED", False)


def _preflight(tmp_path: Path, dependencies: Dependencies) -> None:
    arguments = _cli_arguments(
        tmp_path,
        "preflight",
        "--prior-total",
        "0",
        "--basis",
        "a test",
        "--musescore",
        str(tmp_path / "MuseScore4.exe"),
        "--settings",
        str(tmp_path / "MuseScore4.ini"),
    )
    assert main(arguments, dependencies=dependencies) == 0


def _stop_after(
    tmp_path: Path,
    dependencies: Dependencies,
    command: str,
    *extra: str,
) -> str:
    arguments = _cli_arguments(tmp_path, command, *extra)
    assert main(arguments, dependencies=dependencies) == cli.STOP_STATUS
    record = json.loads((tmp_path / "run" / "run.json").read_text("utf-8"))
    return str(record["stops"][-1]["message"])


def test_later_commands_require_the_preflight_instrument(
    tmp_path: Path,
) -> None:
    """Unrelated HEAD moves and UI INI edits pass; code/pref drift stops."""
    state = {"head": COMMIT, "tree": "tree-v1", "dirty": ""}

    def fake_git(arguments: Sequence[str]) -> str:
        if arguments[0] == "status":
            return state["dirty"]
        if arguments[0] == "ls-tree":
            return state["tree"]
        return state["head"]

    clean = _dependencies(CopyLauncher(), tmp_path, source=fake_git)
    _preflight(tmp_path, clean)
    assert "run download first" in _stop_after(tmp_path, clean, "select")

    # Unrelated HEAD commit + UI-only MuseScore4.ini change remain valid.
    state["head"] = "1" * 40
    (tmp_path / "MuseScore4.ini").write_text("[ui]\nx=2\n", encoding="utf-8")
    assert "run download first" in _stop_after(tmp_path, clean, "select")

    confinement = Confinement.create(tmp_path / "run", tmp_path / "downloads")
    plan = _plan(confinement, ["p0001"])
    begin_run(
        confinement,
        plan,
        {"commit": COMMIT, "tree": "a" * 64, "dirty": False},
    )
    begin_run(
        confinement,
        plan,
        {"commit": "1" * 40, "tree": "a" * 64, "dirty": False},
    )

    gold = ("--gold-status", str(tmp_path / "s.json"), "--gold-cache", ".")
    state["tree"] = "tree-v2"
    assert "code tree" in _stop_after(tmp_path, clean, "select")

    state["tree"] = "tree-v1"
    state["dirty"] = " M pyproject.toml\n"
    assert "uncommitted" in _stop_after(tmp_path, clean, "run", *gold)
    state["dirty"] = ""

    record_path = tmp_path / "run" / "run.json"
    saved = record_path.read_text(encoding="utf-8")
    record = json.loads(saved)
    record["preflight"]["versions"]["music21"] = "0"
    record_path.write_text(json.dumps(record), encoding="utf-8")
    assert "tool version" in _stop_after(tmp_path, clean, "select")
    record_path.write_text(saved, encoding="utf-8")

    record = json.loads(saved)
    del record["preflight"]["musescore"]["executable_sha256"]
    record_path.write_text(json.dumps(record), encoding="utf-8")
    assert "executable hash" in _stop_after(tmp_path, clean, "run", *gold)
    record_path.write_text(saved, encoding="utf-8")

    (tmp_path / "MuseScore4.exe").write_bytes(b"upgraded-mid-run")
    assert "executable hash" in _stop_after(tmp_path, clean, "run", *gold)
    (tmp_path / "MuseScore4.exe").write_bytes(b"")

    (tmp_path / "MuseScore4.ini").write_text(
        "[import]\nmusicXml\\importBreaks=false\n",
        encoding="utf-8",
    )
    assert "MusicXML preferences" in _stop_after(tmp_path, clean, "run", *gold)

    # A held run lock rejects concurrent mutating CLI commands without
    # modifying run.json.
    before_lock = record_path.read_text(encoding="utf-8")
    with confinement.lock(confinement.run / ".run.lock", blocking=False):
        assert (
            main(_cli_arguments(tmp_path, "report"), dependencies=clean)
            == cli.STOP_STATUS
        )
    assert record_path.read_text(encoding="utf-8") == before_lock


def _corrupted_rx_dashes_xml() -> bytes:
    return (
        b'<?xml version="1.0" encoding="utf-8"?>\n'
        b'<score-partwise version="4.0">\n'
        b'  <part-list><score-part id="P1"><part-name>Piano</part-name>'
        b"</score-part></part-list>\n"
        b'  <part id="P1">\n'
        b'    <measure number="1">\n'
        b"      <attributes><divisions>1</divisions>"
        b"<key><fifths>0</fifths></key>"
        b"<time><beats>4</beats><beat-type>4</beat-type></time>"
        b"<staves>2</staves>"
        b'<clef number="1"><sign>G</sign><line>2</line></clef>'
        b'<clef number="2"><sign>F</sign><line>4</line></clef></attributes>\n'
        b'      <direction placement="above"><direction-type>'
        b"<words>rall.</words></direction-type><direction-type>"
        b'<dashes type="start" number="1"/></direction-type>'
        b"<staff>1</staff></direction>\n"
        b'      <direction placement="below"><direction-type>'
        b"<words>smorz.</words></direction-type><direction-type>"
        b'<dashes type="start" number="1"/></direction-type>'
        b"<staff>1</staff></direction>\n"
        b"      <note><pitch><step>C</step><octave>5</octave></pitch>"
        b"<duration>4</duration><voice>1</voice><type>whole</type>"
        b"<staff>1</staff></note>\n"
        b"      <backup><duration>4</duration></backup>\n"
        b"      <note><pitch><step>C</step><octave>3</octave></pitch>"
        b"<duration>4</duration><voice>5</voice><type>whole</type>"
        b"<staff>2</staff></note>\n"
        b"    </measure>\n"
        b'    <measure number="2">\n'
        b'      <direction placement="above"><direction-type>'
        b'<dashes type="stop" number="1"/></direction-type>'
        b"<staff>1</staff></direction>\n"
        b'      <direction placement="below"><direction-type>'
        b'<dashes type="stop" number="1"/></direction-type>'
        b"<staff>1</staff></direction>\n"
        b"      <note><pitch><step>D</step><octave>5</octave></pitch>"
        b"<duration>4</duration><voice>1</voice><type>whole</type>"
        b"<staff>1</staff></note>\n"
        b"      <backup><duration>4</duration></backup>\n"
        b"      <note><pitch><step>G</step><octave>2</octave></pitch>"
        b"<duration>4</duration><voice>5</voice><type>whole</type>"
        b"<staff>2</staff></note>\n"
        b"    </measure>\n"
        b"  </part>\n"
        b"</score-partwise>\n"
    )


def test_rx_overlapping_dashes_defect_counts_from_x_and_is_attributed(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Overlapping R(X) dashes fail the file using X's count and log #35740."""

    class CorruptingRxLauncher(CopyLauncher):
        def __call__(
            self,
            command: Sequence[str],
            timeout: float,
        ) -> tuple[int | None, str]:
            out = Path(command[-2])
            if out.name == "rx.musicxml" and "p0002" in out.parts:
                out.write_bytes(_corrupted_rx_dashes_xml())
                return 0, ""
            return super().__call__(command, timeout)

    plan = _plan(roots, ["p0001", "p0002"])
    begin_run(roots, plan, {"commit": COMMIT, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CorruptingRxLauncher()),
        InlineExecutor(),
    ).run(plan)

    rx2 = _result(tmp_path, "pdmx-rx", "p0002")
    assert rx2["failed"] is True
    assert rx2["count_source"] == "x"
    assert rx2["failures"] == ["unsupported", "rx-unparsable"]
    raw_summary = summarize(roots)
    summary = json.loads(json.dumps(raw_summary))
    defects = summary["arms"]["pdmx-rx"]["upstream_export_defects"]
    assert isinstance(defects, dict)
    files = defects["files"]
    assert isinstance(files, list)
    assert len(files) == 1
    assert files[0]["id"] == "p0002"
    assert "35740" in str(files[0]["issue"])
    rem = defects["remaining_content"]
    assert isinstance(rem, dict)
    assert summary["arms"]["pdmx-rx"]["content"]["fidelity"] < 0.5  # noqa: PLR2004
    assert rem["fidelity"] == summary["arms"]["pdmx-x"]["content"]["fidelity"]
    md = public_markdown(raw_summary)
    assert "Attributed upstream MuseScore 4 export defects" in md
    assert "issues/35740" in md


def test_replay_req012_and_tokenizer_counts_end_to_end(
    roots: Confinement,
    tmp_path: Path,
) -> None:
    """Verify OMR-REQ-012 replay and tokenizer-counts CLI scenarios."""
    gold = _gold_piece(tmp_path, 1)
    plan_base = _plan(roots, ["p0001"])
    iso_xml = (
        plan_base.public[0]
        .x.read_text(encoding="utf-8")
        .replace('encoding="UTF-8"', 'encoding="ISO-8859-1"')
        .replace('encoding="utf-8"', 'encoding="ISO-8859-1"')
        .replace("Piano", "Pianofort\u00e9")
        .encode("iso-8859-1")
    )
    roots.write_bytes(plan_base.public[0].x, iso_xml)
    plan = RunPlan(
        ("pdmx",),
        plan_base.public,
        (gold,),
        ((gold.id, gold.path),),
    )
    begin_run(roots, plan, {"commit": COMMIT, "tree": "b" * 64, "dirty": False})
    Runner(
        roots,
        _musescore(roots, tmp_path, CopyLauncher()),
        InlineExecutor(),
    ).run(plan)
    finish_run(roots)
    write_json(
        roots,
        roots.run / GOLD_MAPPING,
        {
            "pieces": [
                {
                    "id": gold.id,
                    "workspace": gold.workspace,
                    "path": str(gold.path),
                    "sha256": gold.sha256,
                }
            ]
        },
    )

    manifest_path = tmp_path / "req012-manifest.json"
    manifest_data = {
        "input_provenance": {
            "gold_scores": {"gold-01": {"score_musicxml_sha256": gold.sha256}}
        },
        "union_set_a_all_34_and_set_b": {
            "raw_count": 2,
            "deduplicated_locus_count": 1,
            "loci": [
                {
                    "gold_id": "gold-01",
                    "measure": 1,
                    "w1_category": "beam",
                    "in_set_a": True,
                    "in_post_t01": False,
                    "replay_target": {
                        "gold_score_sha256": gold.sha256,
                        "measure_ordinal": 1,
                        "target_measure_ordinals": [1],
                        "expected_zero_diff_category": "beam",
                    },
                }
            ],
        },
    }
    manifest_path.write_text(json.dumps(manifest_data), encoding="utf-8")
    expected_manifest_sha = hashlib.sha256(
        manifest_path.read_bytes()
    ).hexdigest()
    replay_summary = replay_req012(
        roots, manifest_path, expected_sha256=expected_manifest_sha
    )
    assert replay_summary["all_passed"] is True
    assert replay_summary["passed_loci"] == 1
    assert (roots.run / "report" / "req012-replay.md").is_file()

    with pytest.raises(Req012ReplayStopError, match="SHA-256"):
        replay_req012(roots, manifest_path, expected_sha256="0" * 64)

    vocab = {ch: idx for idx, ch in enumerate(ByteLevel.alphabet())}
    tok = Tokenizer(BPE(vocab=vocab, merges=[]))
    tok.pre_tokenizer = ByteLevel(add_prefix_space=False)
    tok_src = tmp_path / "tiny-tokenizer.json"
    tok.save(str(tok_src))
    tok_bytes = tok_src.read_bytes()
    spec = HuggingFaceFile(
        repo="test/tiny-tok",
        revision="a" * 40,
        filename="tokenizer.json",
        local_name="tiny-tokenizer.json",
        size=len(tok_bytes),
        git_blob_oid=git_blob_oid_file(tok_src),
    )
    ledger = Ledger.create(
        roots.downloads / "download-ledger.json",
        prior_total=0,
        basis="unit-test",
        confinement=roots,
        mirror=roots.run / "download-ledger.json",
    )
    tok_summary = json.loads(
        json.dumps(
            run_tokenizer_counts(
                roots,
                ledger,
                tokenizer_spec=spec,
                fetch=lambda _url: [tok_bytes],
            )
        )
    )
    cohorts = tok_summary["cohorts"]
    assert cohorts["gold"]["evaluated_pieces"] == 1
    assert cohorts["gold"]["v2_per_system_tokens"]["count"] >= 1
    assert cohorts["pdmx"]["evaluated_pieces"] == 1
    assert ledger.total == len(tok_bytes)
    assert (roots.run / "report" / "tokenizer-counts.md").is_file()
