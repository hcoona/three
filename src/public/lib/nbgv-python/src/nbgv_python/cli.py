"""Command-line interface that proxies invocations to the `nbgv` CLI."""

from __future__ import annotations

import logging
import sys
from typing import TYPE_CHECKING

from .errors import NbgvCommandError, NbgvNotFoundError
from .runner import NbgvRunner

if TYPE_CHECKING:
    from collections.abc import Sequence


EXIT_CODE_NBGV_NOT_FOUND = 127


def main(argv: Sequence[str] | None = None) -> int:
    """Entry point used by the console script."""
    logging.basicConfig(format="%(message)s", level=logging.INFO)
    arguments = list(argv if argv is not None else sys.argv[1:])
    try:
        runner = NbgvRunner()
        return runner.forward(arguments)
    except NbgvNotFoundError as exc:
        logging.error(str(exc))  # noqa: TRY400
        if exc.search_paths:
            logging.debug("Searched PATH entries: %s", exc.search_paths)
        return EXIT_CODE_NBGV_NOT_FOUND
    except NbgvCommandError as exc:
        # Always log the exception so users get the command + exit code context.
        logging.error(str(exc))  # noqa: TRY400
        # stderr can contain additional useful details; keep it available for
        # diagnostics without dropping the higher-level context above.
        if exc.stderr:
            logging.debug("nbgv stderr:\n%s", exc.stderr.rstrip())
        return exc.returncode


if __name__ == "__main__":  # pragma: no cover - convenience entry point
    raise SystemExit(main())
