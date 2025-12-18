"""Command-line interface that proxies invocations to the `nbgv` CLI."""

from __future__ import annotations

import sys
from typing import Sequence

from .errors import NbgvCommandError, NbgvNotFoundError
from .runner import NbgvRunner


def main(argv: Sequence[str] | None = None) -> int:
    """Entry point used by the console script."""

    arguments = list(argv if argv is not None else sys.argv[1:])
    try:
        runner = NbgvRunner()
        return runner.forward(arguments)
    except NbgvNotFoundError as exc:
        print(exc, file=sys.stderr)
        return 127
    except NbgvCommandError as exc:
        if exc.stderr:
            print(exc.stderr, file=sys.stderr)
        return exc.returncode


if __name__ == "__main__":  # pragma: no cover - convenience entry point
    raise SystemExit(main())
