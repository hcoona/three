"""Run harness steps in worker processes with the protocol's time bound.

A step is a top-level function that takes and returns plain data. It runs in
a persistent worker process, which the pool kills and replaces when the step
exceeds its time bound. Workers write no files: they read their inputs and
return results, which the parent writes. A worker's temporary directory is
the run's temporary directory.

A step that raises a stop condition or a protocol defect stops the run; any
other error is returned as an error outcome for the caller to classify.
"""

from __future__ import annotations

import contextlib
import multiprocessing
import os
import queue
import tempfile
import threading
import time
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Literal, Protocol

from sheet_music_omr.harness.stops import StopConditionError

if TYPE_CHECKING:
    from collections.abc import Callable, Mapping
    from multiprocessing.connection import Connection
    from multiprocessing.process import BaseProcess
    from pathlib import Path

STEP_SECONDS = 300.0
"""The time bound of one conversion or comparison step."""

STOP_TYPES = frozenset(
    {
        "sheet_music_omr.harness.stops.StopConditionError",
        "sheet_music_omr.musicxml_events.ProtocolDefectError",
        "sheet_music_omr.comparison.InstrumentMismatchError",
    },
)
"""The exception types, and their subclasses, that stop the run."""

type Status = Literal["ok", "error", "timeout"]
type Step = Callable[..., object]


class StepStopError(StopConditionError):
    """A step raised a stop condition or a protocol defect."""


class WorkerCrashError(StopConditionError):
    """A worker process ended without returning a step's result."""


@dataclass(frozen=True, slots=True)
class Outcome:
    """The outcome of one step.

    Attributes:
        status: ``ok``, ``error`` for an exception or ``timeout``.
        value: The step's return value when it is ``ok``.
        seconds: The elapsed time.
        error_type: The qualified name of the exception's type.
        message: The exception's message.
        codes: The diagnostic codes that the exception carries.
    """

    status: Status
    value: object = None
    seconds: float = 0.0
    error_type: str = ""
    message: str = ""
    codes: tuple[str, ...] = field(default=())

    @property
    def ok(self) -> bool:
        """Whether the step returned a value."""
        return self.status == "ok"

    def record(self) -> dict[str, object]:
        """Return the outcome without its value, as plain data."""
        record: dict[str, object] = {
            "status": self.status,
            "seconds": round(self.seconds, 3),
        }
        if self.error_type:
            record["error_type"] = self.error_type
            record["message"] = self.message[:2000]
        if self.codes:
            record["codes"] = list(self.codes)
        return record


class Executor(Protocol):
    """Runs steps with a time bound."""

    def call(
        self,
        step: Step,
        arguments: Mapping[str, object],
        *,
        context: str,
        timeout: float = STEP_SECONDS,
    ) -> Outcome:
        """Run one step and return its outcome.

        Raises:
            StepStopError: If the step raised a stop condition.
        """
        ...

    def close(self) -> None:
        """Release the executor's workers."""
        ...


def _type_names(error: BaseException) -> tuple[str, ...]:
    return tuple(
        f"{t.__module__}.{t.__qualname__}" for t in type(error).__mro__
    )


def _codes(error: BaseException) -> tuple[str, ...]:
    diagnostics = getattr(error, "diagnostics", ())
    try:
        return tuple(str(d.code) for d in diagnostics)
    except AttributeError, TypeError:
        return ()


type _Reply = tuple[str, object, float, str, str, tuple[str, ...]]


def _run(step: Step, arguments: Mapping[str, object]) -> _Reply:
    """Run a step and describe its result as plain data."""
    started = time.perf_counter()
    try:
        value = step(**arguments)
    except Exception as error:  # noqa: BLE001 - every error is classified
        names = _type_names(error)
        status = "stop" if STOP_TYPES.intersection(names) else "error"
        return (
            status,
            None,
            time.perf_counter() - started,
            names[0],
            str(error),
            _codes(error),
        )
    return ("ok", value, time.perf_counter() - started, "", "", ())


def _outcome(reply: _Reply, context: str) -> Outcome:
    status, value, seconds, error_type, message, codes = reply
    if status == "stop":
        text = f"{context}: {error_type}: {message}"
        raise StepStopError(text)
    return Outcome(
        "ok" if status == "ok" else "error",
        value,
        seconds,
        error_type,
        message,
        codes,
    )


def _serve(connection: Connection, temp: str) -> None:
    """Serve step requests until the parent sends ``None`` or goes away."""
    for name in ("TEMP", "TMP", "TMPDIR"):
        os.environ[name] = temp
    tempfile.tempdir = temp
    while True:
        try:
            request = connection.recv()
        except EOFError:
            return
        if request is None:
            return
        step, arguments = request
        connection.send(_run(step, arguments))


@dataclass(slots=True)
class InlineExecutor:
    """Run steps in the calling thread, without a time bound.

    It classifies errors as the pool does. Tests use it for speed.
    """

    def call(
        self,
        step: Step,
        arguments: Mapping[str, object],
        *,
        context: str,
        timeout: float = STEP_SECONDS,  # noqa: ARG002 - the pool's interface
    ) -> Outcome:
        """Run one step in this thread.

        Raises:
            StepStopError: If the step raised a stop condition.
        """
        return _outcome(_run(step, arguments), context)

    def close(self) -> None:
        """Release nothing."""


class _Worker:
    """One worker process and the parent's end of its pipe."""

    def __init__(self, temp: Path) -> None:
        context = multiprocessing.get_context("spawn")
        self.connection, child = context.Pipe()
        self.process: BaseProcess = context.Process(
            target=_serve,
            args=(child, str(temp)),
            daemon=True,
        )
        self.process.start()
        child.close()

    def kill(self) -> None:
        self.process.kill()
        self.process.join()
        self.connection.close()

    def stop(self) -> None:
        with contextlib.suppress(OSError):
            self.connection.send(None)
        self.process.join(timeout=5)
        if self.process.is_alive():
            self.process.kill()
            self.process.join()
        self.connection.close()


class WorkerPool:
    """A pool of persistent spawn-context worker processes.

    Each worker serves one step at a time. A step that exceeds its time bound
    has its worker killed and replaced. Up to ``jobs`` steps run at once, one
    per calling thread.
    """

    def __init__(self, temp: Path, jobs: int = 1) -> None:
        """Start ``jobs`` workers whose temporary directory is ``temp``."""
        if jobs < 1:
            message = "the pool needs at least one worker"
            raise ValueError(message)
        self._temp = temp
        self._idle: queue.Queue[_Worker] = queue.Queue()
        self._all: list[_Worker] = []
        self._lock = threading.Lock()
        self._closed = False
        for _ in range(jobs):
            self._add()

    def _add(self) -> None:
        if self._closed:
            return
        worker = _Worker(self._temp)
        with self._lock:
            self._all.append(worker)
        self._idle.put(worker)

    def _discard(self, worker: _Worker) -> None:
        worker.kill()
        with self._lock:
            if worker in self._all:
                self._all.remove(worker)

    def call(
        self,
        step: Step,
        arguments: Mapping[str, object],
        *,
        context: str,
        timeout: float = STEP_SECONDS,
    ) -> Outcome:
        """Run one step in an idle worker.

        Returns:
            The step's outcome; ``timeout`` when it exceeded ``timeout``
            seconds, after which its worker is replaced.

        Raises:
            StepStopError: If the step raised a stop condition.
            WorkerCrashError: If the worker ended without a result.
        """
        if self._closed:
            message = "the worker pool is closed"
            raise RuntimeError(message)
        worker = self._idle.get()
        started = time.perf_counter()
        try:
            worker.connection.send((step, dict(arguments)))
            if not worker.connection.poll(timeout):
                self._discard(worker)
                self._add()
                return Outcome(
                    "timeout",
                    seconds=time.perf_counter() - started,
                    message=f"exceeded {timeout:g} seconds",
                )
            reply: _Reply = worker.connection.recv()
        except (EOFError, OSError) as error:
            self._discard(worker)
            self._add()
            message = f"{context}: the worker process ended: {error!r}"
            raise WorkerCrashError(message) from error
        except BaseException:
            self._discard(worker)
            raise
        self._idle.put(worker)
        return _outcome(reply, context)

    def close(self) -> None:
        """Stop every worker, killing any that is still running a step."""
        self._closed = True
        with self._lock:
            workers = list(self._all)
            self._all.clear()
        for worker in workers:
            worker.stop()
