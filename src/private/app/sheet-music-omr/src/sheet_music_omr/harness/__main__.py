"""Run the harness's command line: ``python -m sheet_music_omr.harness``."""

from __future__ import annotations

import sys

from sheet_music_omr.harness.cli import main

if __name__ == "__main__":
    sys.exit(main())
