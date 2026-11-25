"""Module entry point for ``python -m sheet_music_omr.training``."""

from __future__ import annotations

import sys

from sheet_music_omr.training.cli import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
