"""CLI entry point for ``python -m sheet_music_omr.synthetic``."""

from __future__ import annotations

import sys

from sheet_music_omr.synthetic.cli import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
