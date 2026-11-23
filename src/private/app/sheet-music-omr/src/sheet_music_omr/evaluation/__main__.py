"""Entrypoint for ``python -m sheet_music_omr.evaluation``."""

from __future__ import annotations

import sys

from sheet_music_omr.evaluation.cli import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
