"""Command-line interface that proxies invocations to the `nbgv` CLI."""

from __future__ import annotations

import logging
import sys
from typing import TYPE_CHECKING

from .errors import NbgvCommandError, NbgvNotFoundError
from .runner import NbgvRunner

if TYPE_CHECKING:
    from collections.abc import Sequence


def main(argv: Sequence[str] | None = None) -> int:
    """Entry point used by the console script."""
    logging.basicConfig(format="%(message)s", level=logging.INFO)
    arguments = list(argv if argv is not None else sys.argv[1:])
    try:
        runner = NbgvRunner()
        return runner.forward(arguments)
    except NbgvNotFoundError:
        logging.exception("nbgv executable not found")
        return 127
    except NbgvCommandError as exc:
        if exc.stderr:
            logging.exception(exc.stderr)
        return exc.returncode


if __name__ == "__main__":  # pragma: no cover - convenience entry point
    raise SystemExit(main())
