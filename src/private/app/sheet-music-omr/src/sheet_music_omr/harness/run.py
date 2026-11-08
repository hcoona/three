"""Run the round trips of every arm, the baseline and the assembly test.

The runner makes ``R(X)`` for the selected public files, then runs each arm's
files through reverse, forward, the MuseScore import check, the content and
layout passes and the text-space check, then compares ``X`` with ``R(X)`` for
the baseline and runs the system-assembly test. Python steps run in workers
with the 300-second bound; MuseScore runs in this process, one conversion at
a time. A file whose ``result.json`` exists is finished and is skipped when a
stopped run resumes.
"""

from __future__ import annotations

import shutil
import threading
from concurrent.futures import FIRST_EXCEPTION, ThreadPoolExecutor, wait
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Literal

from sheet_music_omr.harness import steps as default_steps
from sheet_music_omr.harness.gold import GoldPiece, check_unchanged
from sheet_music_omr.harness.records import (
    RecordError,
    load_run_record,
    now,
    read_object,
    update_run_record,
    write_json,
)
from sheet_music_omr.harness.stops import StopConditionError
from sheet_music_omr.harness.worker import STEP_SECONDS, Outcome

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping, Sequence
    from pathlib import Path

    from sheet_music_omr.harness.confine import Confinement
    from sheet_music_omr.harness.musescore import MuseScore
    from sheet_music_omr.harness.worker import Executor

FILE_SCHEMA = "w1-roundtrip-file-v1"
"""The schema name of a per-file ``result.json``."""

GOLD_ARM = "gold-x"
"""The gold arm's name."""

RETURNS_TO_OWNER = frozenset({"unsupported", "x-unparsable"})
"""The gold failures whose count returns to the owner."""

PASS_STEPS = ("content", "layout", "text_space")
"""The comparison steps of a file after its conversions."""

type Kind = Literal["x", "rx", "gold"]
type Data = dict[str, object]


class StepError(StopConditionError):
    """A comparison or counting step failed in a way the protocol omits."""


class _PeerStoppedError(StopConditionError):
    """A running file stopped because another file triggered a run stop."""


@dataclass(frozen=True, slots=True)
class PublicFile:
    """One selected public file.

    Attributes:
        id: The run identifier, such as ``p0001``.
        corpus: ``pdmx``, ``lieder`` or ``pilot``.
        key: The public PDMX or Lieder path, or the fixture name.
        x: ``X`` in the run directory.
    """

    id: str
    corpus: str
    key: str
    x: Path


@dataclass(frozen=True, slots=True)
class RunPlan:
    """The files of one run.

    Attributes:
        corpora: The public corpora in arm order.
        public: The selected public files.
        gold: The accepted gold pieces.
        assembly: The pieces of the system-assembly test, by identifier.
    """

    corpora: tuple[str, ...]
    public: tuple[PublicFile, ...]
    gold: tuple[GoldPiece, ...] = ()
    assembly: tuple[tuple[str, Path], ...] = ()

    def frozen(self) -> Data:
        """Return the plan as the run record freezes it."""
        return {
            "corpora": list(self.corpora),
            "public": [[f.id, f.corpus, f.key] for f in self.public],
            "gold": [[g.id, g.sha256] for g in self.gold],
            "assembly": [name for name, _ in self.assembly],
        }


@dataclass(frozen=True, slots=True)
class Item:
    """One file of one arm.

    Attributes:
        id: The file identifier.
        arm: The arm's name.
        kind: ``x`` or ``rx`` for a public arm, ``gold`` for the gold arm.
        x: The source score ``X``.
        y: The reference ``Y``: ``X``, or ``R(X)`` for an ``rx`` arm.
        gold: The gold piece, for the gold arm.
    """

    id: str
    arm: str
    kind: Kind
    x: Path
    y: Path
    gold: GoldPiece | None = None


def arms(plan: RunPlan) -> list[tuple[str, list[Item]]]:
    """Return each arm of a plan with its items, in run order."""
    result: list[tuple[str, list[Item]]] = []
    for corpus in plan.corpora:
        files = [f for f in plan.public if f.corpus == corpus]
        for kind in ("x", "rx"):
            arm = f"{corpus}-{kind}"
            result.append(
                (
                    arm,
                    [
                        Item(
                            f.id,
                            arm,
                            "x" if kind == "x" else "rx",
                            f.x,
                            f.x if kind == "x" else rx_path(f.x),
                        )
                        for f in files
                    ],
                ),
            )
    if plan.gold:
        result.append(
            (
                GOLD_ARM,
                [
                    Item(g.id, GOLD_ARM, "gold", g.path, g.path, g)
                    for g in plan.gold
                ],
            ),
        )
    return result


def rx_path(x: Path) -> Path:
    """Return the path of ``R(X)`` next to ``X`` in the run directory."""
    return x.with_name("rx.musicxml")


def rx_record_path(x: Path) -> Path:
    """Return the path of the ``R(X)`` export record next to ``X``."""
    return x.with_name("rx.json")


def file_directory(confinement: Confinement, arm: str, name: str) -> Path:
    """Return the directory of one file's results."""
    return confinement.run / "files" / arm / name


def failed_tally(elements: int) -> Data:
    """Return the tally of a failed file: every element non-identical."""
    return {
        "elements": elements,
        "identical": 0,
        "extras": 0,
        "fidelity": 0.0 if elements else None,
    }


def begin_run(
    confinement: Confinement,
    plan: RunPlan,
    source: Mapping[str, object],
) -> None:
    """Freeze the plan and source state in the run record, or check them.

    A resumed run must have the same plan, gold hashes and converter commit.

    Raises:
        RecordError: If a resumed run differs from the frozen one.
    """
    record = load_run_record(confinement)
    frozen = plan.frozen()
    existing = record.get("run")
    if isinstance(existing, dict):
        if existing.get("plan") != frozen:
            message = "the run plan or a gold hash differs from the frozen run"
            raise RecordError(message)
        if existing.get("commit") != source.get("commit"):
            message = "a resumed run must use the frozen converter commit"
            raise RecordError(message)
        return
    update_run_record(
        confinement,
        "run",
        {
            "plan": frozen,
            "commit": source.get("commit"),
            "dirty": source.get("dirty"),
            "started": now(),
        },
    )


@dataclass(slots=True)
class _FileState:
    """The record of one file as its steps run."""

    item: Item
    result: Data
    operations: Data = field(default_factory=dict)
    counts: dict[Path, Data] = field(default_factory=dict)

    @property
    def steps(self) -> Data:
        steps = self.result["steps"]
        assert isinstance(steps, dict)  # noqa: S101 - set by the runner
        return steps

    @property
    def failures(self) -> list[str]:
        failures = self.result["failures"]
        assert isinstance(failures, list)  # noqa: S101 - set by the runner
        return failures

    def fail(self, status: str) -> None:
        if status not in self.failures:
            self.failures.append(status)


def _value(outcome: Outcome) -> Data:
    value = outcome.value
    if not isinstance(value, dict):
        message = "a step returned no record"
        raise StepError(message)
    return value


def _step_record(outcome: Outcome, value: Data | None = None) -> Data:
    record = outcome.record()
    if value is not None:
        record["status"] = value.get("status")
        for name in ("codes", "detail", "markers"):
            if name in value:
                record[name] = value[name]
    return record


@dataclass(slots=True)
class Runner:
    """Run one plan's phases under the protocol's bounds.

    Attributes:
        confinement: The run's write confinement.
        musescore: The pinned MuseScore, run in this process.
        executor: Runs the Python steps with the time bound.
        steps: The step table, by step name.
        jobs: The number of files processed at once.
        timeout: The bound of one conversion or comparison step.
    """

    confinement: Confinement
    musescore: MuseScore
    executor: Executor
    steps: Mapping[str, Callable[..., object]] = field(
        default_factory=lambda: dict(default_steps.STEPS),
    )
    jobs: int = 1
    timeout: float = STEP_SECONDS
    _musescore_lock: threading.Lock = field(default_factory=threading.Lock)
    _stopping: threading.Event = field(default_factory=threading.Event)
    _stop_lock: threading.Lock = field(default_factory=threading.Lock)

    def run(self, plan: RunPlan) -> None:
        """Run phases 3 to 6 of the protocol for a plan."""
        self.make_rx(plan.public)
        for arm, items in arms(plan):
            with self.musescore.batch(f"arm-{arm}"):
                self._each(items, self.process)
        self.baselines(plan.public)
        self.assemblies(plan)

    def _each(
        self, items: Sequence[Item], action: Callable[[Item], None]
    ) -> None:
        if self.jobs <= 1:
            for item in items:
                self._guarded(action, item)
            return
        with ThreadPoolExecutor(max_workers=self.jobs) as pool:
            futures = [
                pool.submit(self._guarded, action, item) for item in items
            ]
            done, pending = wait(futures, return_when=FIRST_EXCEPTION)
            failed = [
                f
                for f in done
                if not f.cancelled() and f.exception() is not None
            ]
            if failed:
                with self._stop_lock:
                    self._stopping.set()
                for future in pending:
                    future.cancel()
                wait(pending)
                errors = []
                for future in futures:
                    if future.cancelled():
                        continue
                    error = future.exception()
                    if error is not None:
                        errors.append(error)
                error = next(
                    (
                        error
                        for error in errors
                        if not isinstance(error, _PeerStoppedError)
                    ),
                    errors[0],
                )
                assert error is not None  # noqa: S101 - checked above
                raise error

    def _guarded(self, action: Callable[[Item], None], item: Item) -> None:
        if self._stopping.is_set():
            return
        try:
            action(item)
        except _PeerStoppedError:
            raise
        except BaseException:
            with self._stop_lock:
                self._stopping.set()
            raise

    def _check_stopping(self) -> None:
        if self._stopping.is_set():
            message = "another file triggered a protocol stop"
            raise _PeerStoppedError(message)

    def _call(self, name: str, item: Item, **arguments: object) -> Outcome:
        self._check_stopping()
        context = f"step {name}, arm {item.arm}, file {item.id}"
        outcome = self.executor.call(
            self.steps[name],
            arguments,
            context=context,
            timeout=self.timeout,
        )
        self._check_stopping()
        return outcome

    def make_rx(self, files: Sequence[PublicFile]) -> None:
        """Make ``R(X)`` for each public file that has no export record."""
        corpora = dict.fromkeys(f.corpus for f in files)
        for corpus in corpora:
            todo = [
                f
                for f in files
                if f.corpus == corpus and not rx_record_path(f.x).exists()
            ]
            if not todo:
                continue
            with self.musescore.batch(f"rx-{corpus}"):
                for file in todo:
                    target = rx_path(file.x)
                    self.confinement.check(target).unlink(missing_ok=True)
                    conversion = self.musescore.convert(
                        file.x,
                        target,
                        timeout=self.timeout,
                    )
                    record = conversion.record()
                    record["status"] = (
                        "ok"
                        if conversion.ok
                        else "timeout"
                        if conversion.timed_out
                        else "export-failed"
                    )
                    write_json(self.confinement, rx_record_path(file.x), record)

    def _import_check(self, t: Path) -> Data:
        with self._musescore_lock:
            self._check_stopping()
            directory = self.confinement.temp_dir("import-")
            try:
                conversion = self.musescore.convert(
                    t,
                    directory / "t.mscz",
                    timeout=self.timeout,
                )
            finally:
                shutil.rmtree(self.confinement.check(directory))
        record = conversion.record()
        record["status"] = (
            "passed"
            if conversion.ok
            else "timeout"
            if conversion.timed_out
            else "failed"
        )
        record["deleted"] = not directory.exists()
        return record

    def process(self, item: Item) -> None:
        """Run one file of one arm and write its result last."""
        self._check_stopping()
        directory = file_directory(self.confinement, item.arm, item.id)
        if (directory / "result.json").exists():
            return
        if item.gold is not None:
            check_unchanged(item.gold)
        state = _FileState(
            item,
            {
                "schema": FILE_SCHEMA,
                "id": item.id,
                "arm": item.arm,
                "kind": item.kind,
                "steps": {},
                "failures": [],
            },
        )
        self._convert(state, directory)
        self._check_stopping()
        self._finish(state)
        write_json(
            self.confinement,
            directory / "operations.json",
            state.operations,
        )
        self._publish_result(directory, state.result)

    def _publish_result(self, directory: Path, result: Data) -> None:
        with self._stop_lock:
            self._check_stopping()
            write_json(
                self.confinement,
                directory / "result.json",
                result,
            )

    def _convert(self, state: _FileState, directory: Path) -> None:
        item = state.item
        if item.kind == "rx":
            record = read_object(rx_record_path(item.x))
            state.steps["rx_export"] = record
            if record.get("status") != "ok":
                state.fail(
                    "rx-export-timeout"
                    if record.get("status") == "timeout"
                    else "rx-export-failed",
                )
                return
        text = self._reverse(state, directory)
        if text is None:
            return
        t = self._forward(state, text, directory)
        if t is None:
            return
        self._check_import(state, t)
        self._compare(state, t)
        self._text_space(state, t, directory)

    def _reverse(self, state: _FileState, directory: Path) -> str | None:
        outcome = self._call("reverse", state.item, y=state.item.y)
        if outcome.status == "timeout":
            state.steps["reverse"] = _step_record(outcome)
            state.fail("reverse-timeout")
            return None
        if not outcome.ok:
            state.steps["reverse"] = _step_record(
                outcome,
                {"status": default_steps.REVERSE_FAILED},
            )
            state.fail(default_steps.REVERSE_FAILED)
            return None
        value = _value(outcome)
        state.steps["reverse"] = _step_record(outcome, value)
        text = value.get("text")
        if isinstance(text, str):
            self.confinement.write_text(directory / "reverse.txt", text)
        if value.get("status") != default_steps.OK or not isinstance(text, str):
            state.fail(str(value.get("status")))
            return None
        return text

    def _forward(
        self,
        state: _FileState,
        text: str,
        directory: Path,
    ) -> Path | None:
        outcome = self._call("forward", state.item, text=text)
        if outcome.status == "timeout":
            state.steps["forward"] = _step_record(outcome)
            state.fail("forward-timeout")
            return None
        if not outcome.ok:
            state.steps["forward"] = _step_record(
                outcome,
                {"status": default_steps.FORWARD_FAILED},
            )
            state.fail(default_steps.FORWARD_FAILED)
            return None
        value = _value(outcome)
        state.steps["forward"] = _step_record(outcome, value)
        data = value.get("musicxml")
        if value.get("status") != default_steps.OK or not isinstance(
            data,
            bytes,
        ):
            state.fail(default_steps.FORWARD_FAILED)
            return None
        return self.confinement.write_bytes(directory / "t.musicxml", data)

    def _check_import(self, state: _FileState, t: Path) -> None:
        record = self._import_check(t)
        state.steps["import_check"] = record
        state.result["import_check"] = record["status"]
        if record["status"] == "timeout":
            state.fail("import-check-timeout")

    def _comparison(self, name: str, state: _FileState, t: Path) -> Data | None:
        """Run a comparison step; return its record unless it timed out."""
        outcome = self._call(name, state.item, t=t, y=state.item.y)
        if outcome.status == "timeout":
            state.steps[name] = _step_record(outcome)
            state.fail(f"{name}-timeout")
            return None
        if not outcome.ok:
            message = (
                f"step {name}, arm {state.item.arm}, file {state.item.id}: "
                f"{outcome.error_type}: {outcome.message}"
            )
            raise StepError(message)
        value = _value(outcome)
        state.steps[name] = _step_record(outcome, value)
        return value

    def _compare(self, state: _FileState, t: Path) -> None:
        value = self._comparison("content", state, t)
        if value is None:
            return
        if value.get("status") == default_steps.UNPARSABLE:
            self._unparsable(state)
            return
        state.operations["content"] = value.pop("operations", None)
        state.result["content_pass"] = value
        layout = self._comparison("layout", state, t)
        if layout is None:
            return
        if layout.get("status") == default_steps.UNPARSABLE:
            self._unparsable(state)
            return
        state.operations["layout"] = layout.pop("operations", None)
        state.result["layout_pass"] = layout

    def _unparsable(self, state: _FileState) -> None:
        """Decide whether ``T(Y)`` or ``Y`` cannot be parsed.

        Raises:
            StepError: If a public ``X`` that passed the screen's parse
                check cannot be parsed.
        """
        item = state.item
        counted = self._count(state, item.y)
        if counted.get("status") == default_steps.OK:
            state.fail("t-unparsable")
        elif item.kind == "rx":
            state.fail("rx-unparsable")
        elif item.kind == "gold":
            state.fail("x-unparsable")
        else:
            message = (
                f"arm {item.arm}, file {item.id}: musicdiff cannot parse X, "
                "which passed the screen"
            )
            raise StepError(message)

    def _text_space(self, state: _FileState, t: Path, directory: Path) -> None:
        outcome = self._call("text_space", state.item, t=t, y=state.item.y)
        if outcome.status == "timeout":
            state.steps["text_space"] = _step_record(outcome)
            state.fail("text-space-timeout")
            return
        if not outcome.ok:
            state.steps["text_space"] = _step_record(
                outcome,
                {"status": "reverse-failed"},
            )
            state.result["text_space"] = "mismatch"
            return
        value = _value(outcome)
        state.steps["text_space"] = _step_record(outcome, value)
        consistent = value.get("consistent") is True
        state.result["text_space"] = "consistent" if consistent else "mismatch"
        t_text = value.get("t_text")
        if isinstance(t_text, str):
            self.confinement.write_text(directory / "t-reverse.txt", t_text)

    def _count(self, state: _FileState, path: Path) -> Data:
        """Count a score's elements alone, once per file and score.

        Raises:
            StepError: If the count times out or fails.
        """
        if path in state.counts:
            return state.counts[path]
        outcome = self._call("count", state.item, y=path)
        if not outcome.ok:
            message = (
                f"step count, arm {state.item.arm}, file {state.item.id}: "
                f"the failure count is not computable ({outcome.status})"
            )
            raise StepError(message)
        value = _value(outcome)
        state.counts[path] = value
        return value

    def _finish(self, state: _FileState) -> None:
        """Set the counted tallies of a file, failed or not."""
        item = state.item
        result = state.result
        failures = state.failures
        result["failed"] = bool(failures)
        result["failure"] = failures[0] if failures else None
        names = ("reverse", "forward", "import_check", *PASS_STEPS)
        if item.kind == "rx":
            names = ("rx_export", *names)
        for name in names:
            state.steps.setdefault(name, {"status": "not-run"})
        result.setdefault("import_check", "not-run")
        result.setdefault("text_space", "not-run")
        result["returns_to_owner"] = item.kind == "gold" and bool(
            RETURNS_TO_OWNER.intersection(failures),
        )
        if not failures:
            content = result["content_pass"]
            layout = result["layout_pass"]
            assert isinstance(content, dict)  # noqa: S101
            assert isinstance(layout, dict)  # noqa: S101
            result["count_source"] = None
            result["content"] = content["tally"]
            result["layout"] = layout["tally"]
            return
        counted = self._failure_count(state)
        if counted is None:
            result["count_source"] = None
            result["content"] = None
            result["layout"] = None
            return
        source, value = counted
        content = value.get("content")
        layout = value.get("layout")
        assert isinstance(content, int)  # noqa: S101
        assert isinstance(layout, int)  # noqa: S101
        result["count_source"] = source
        result["content"] = failed_tally(content)
        result["layout"] = failed_tally(layout)

    def _failure_count(self, state: _FileState) -> tuple[str, Data] | None:
        """Count a failed file on ``Y``, or on ``X`` when ``R(X)`` fails.

        Returns:
            The counted score, ``y`` or ``x``, and its counts; ``None`` for
            a gold piece whose ``X`` musicdiff cannot parse.

        Raises:
            StepError: If a public ``X`` cannot be counted.
        """
        item = state.item
        if item.kind == "rx":
            missing = {"rx-export-failed", "rx-export-timeout"}
            if not missing.intersection(state.failures):
                value = self._count(state, item.y)
                if value.get("status") == default_steps.OK:
                    return "y", value
                state.fail("rx-unparsable")
            value = self._count(state, item.x)
        else:
            value = self._count(state, item.y)
        if value.get("status") == default_steps.OK:
            return ("y" if item.kind != "rx" else "x"), value
        if item.kind == "gold":
            state.fail("x-unparsable")
            state.result["returns_to_owner"] = True
            return None
        message = (
            f"arm {item.arm}, file {item.id}: musicdiff cannot parse X, which "
            "passed the screen; the failure count is not computable"
        )
        raise StepError(message)

    def baselines(self, files: Sequence[PublicFile]) -> None:
        """Compare ``X`` with ``R(X)`` for each public file, reported only."""
        items = [Item(f.id, "baseline", "x", f.x, rx_path(f.x)) for f in files]
        self._each(items, self._baseline)

    def _baseline(self, item: Item) -> None:
        directory = file_directory(self.confinement, "baseline", item.id)
        if (directory / "result.json").exists():
            return
        export = read_object(rx_record_path(item.x))
        result: Data = {"schema": FILE_SCHEMA, "id": item.id, "arm": "baseline"}
        if export.get("status") != "ok":
            result["status"] = "rx-missing"
            self._publish_result(directory, result)
            return
        outcome = self._call("baseline", item, rx=item.y, x=item.x)
        if outcome.status == "timeout":
            result["status"] = "timeout"
            result["step"] = outcome.record()
        elif not outcome.ok:
            message = (
                f"step baseline, file {item.id}: "
                f"{outcome.error_type}: {outcome.message}"
            )
            raise StepError(message)
        else:
            value = _value(outcome)
            operations: Data = {}
            for name in ("content", "layout"):
                part = value.get(name)
                if isinstance(part, dict):
                    operations[name] = part.pop("operations", None)
                    result[name] = part
            result["status"] = "ok"
            result["step"] = outcome.record()
            write_json(
                self.confinement,
                directory / "operations.json",
                operations,
            )
        self._publish_result(directory, result)

    def assemblies(self, plan: RunPlan) -> None:
        """Run the system-assembly test on each assembly piece."""
        pieces = {g.id: g for g in plan.gold}
        for name, path in plan.assembly:
            directory = file_directory(self.confinement, "assembly", name)
            if (directory / "result.json").exists():
                continue
            piece = pieces.get(name)
            if piece is not None:
                check_unchanged(piece)
            item = Item(name, "assembly", "gold", path, path, piece)
            outcome = self._call("assembly", item, x=path)
            result: Data = {
                "schema": FILE_SCHEMA,
                "id": name,
                "arm": "assembly",
            }
            if outcome.status == "timeout":
                result.update(status="timeout", passed=False)
            elif not outcome.ok:
                message = (
                    f"step assembly, file {name}: "
                    f"{outcome.error_type}: {outcome.message}"
                )
                raise StepError(message)
            else:
                result.update(_value(outcome))
            result["step"] = outcome.record()
            write_json(self.confinement, directory / "result.json", result)


def finish_run(confinement: Confinement) -> None:
    """Record that the run finished."""
    record = load_run_record(confinement)
    run = record.get("run")
    if not isinstance(run, dict):
        message = "the run record has no run section"
        raise RecordError(message)
    run["finished"] = now()
    update_run_record(confinement, "run", run)
