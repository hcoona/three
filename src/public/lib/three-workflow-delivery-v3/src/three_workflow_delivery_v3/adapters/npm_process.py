"""Bounded POSIX process facts for the isolated, standard npm publisher."""

from __future__ import annotations

import os
import selectors
import signal
import subprocess
import time
from contextlib import suppress
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Literal, Protocol

if TYPE_CHECKING:
    from collections.abc import Mapping
    from pathlib import Path

CommandClassification = Literal[
    "not-initiated",
    "definitive-success",
    "definitive-non-success",
    "ambiguous",
]


@dataclass(frozen=True, slots=True)
class NpmProcessOutcome:
    """Local process facts, never destination or no-mutation evidence."""

    classification: CommandClassification
    output: bytes = field(default=b"", repr=False)
    truncated: bool = False
    returncode: int | None = None

    def __post_init__(self) -> None:
        """Keep controlled exit facts separate from unknown termination."""
        if (
            self.classification
            not in {
                "not-initiated",
                "definitive-success",
                "definitive-non-success",
                "ambiguous",
            }
            or type(self.output) is not bytes
            or type(self.truncated) is not bool
            or (
                self.returncode is not None and type(self.returncode) is not int
            )
        ):
            message = "Malformed npm process outcome"
            raise ValueError(message)
        if (
            (
                self.classification == "definitive-success"
                and self.returncode != 0
            )
            or (
                self.classification == "definitive-non-success"
                and (type(self.returncode) is not int or self.returncode <= 0)
            )
            or (
                self.classification == "not-initiated"
                and self.returncode is not None
            )
        ):
            message = "Inconsistent npm process outcome"
            raise ValueError(message)


class NpmProcessRunner(Protocol):
    """Execute with the exact environment and working directory supplied."""

    def run(
        self,
        argv: tuple[str, ...],
        *,
        cwd: Path,
        environment: Mapping[str, str],
        timeout: float,
        output_limit: int,
    ) -> NpmProcessOutcome:
        """Return controlled process facts, without retrying the command."""
        ...


class IsolatedNpmProcessRunner:
    """Use the first-slice Ubuntu boundary without shell or inheritance."""

    def run(  # noqa: C901, PLR0912
        self,
        argv: tuple[str, ...],
        *,
        cwd: Path,
        environment: Mapping[str, str],
        timeout: float,
        output_limit: int,
    ) -> NpmProcessOutcome:
        """Drain bounded output and kill the exact process group on timeout."""
        if os.name != "posix" or timeout <= 0 or output_limit <= 0:
            message = "Isolated npm requires POSIX and positive process bounds"
            raise ValueError(message)
        try:
            process = subprocess.Popen(  # noqa: S603
                argv,
                cwd=cwd,
                env=dict(environment),
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                start_new_session=True,
            )
        except OSError:
            # Popen reports child setup/exec failure before the program starts.
            return NpmProcessOutcome("not-initiated")
        output = bytearray()
        truncated = False
        deadline = time.monotonic() + timeout
        timed_out = False
        try:
            assert process.stdout is not None  # noqa: S101
            with selectors.DefaultSelector() as selector:
                selector.register(process.stdout, selectors.EVENT_READ)
                while selector.get_map() or process.poll() is None:
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        timed_out = True
                        break
                    for key, _ in selector.select(min(remaining, 0.1)):
                        chunk = os.read(key.fd, 65536)
                        if not chunk:
                            selector.unregister(key.fileobj)
                            continue
                        available = output_limit - len(output)
                        output.extend(chunk[:available])
                        truncated |= len(chunk) > available
                if timed_out:
                    self._terminate(process)
                returncode = process.wait()
        finally:
            if process.poll() is None:
                self._terminate(process)
                process.wait()
            if process.stdout is not None:
                process.stdout.close()
        classification: CommandClassification
        if timed_out or returncode < 0:
            classification = "ambiguous"
        elif returncode == 0:
            classification = "definitive-success"
        else:
            classification = "definitive-non-success"
        return NpmProcessOutcome(
            classification, bytes(output), truncated, returncode
        )

    @staticmethod
    def _terminate(process: subprocess.Popen[bytes]) -> None:
        with suppress(ProcessLookupError):
            os.killpg(process.pid, signal.SIGKILL)
