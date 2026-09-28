"""Expose the isolated caller modules to local operator tests."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
# Root pytest uses importlib mode; make shared standalone test fixtures visible.
sys.path.insert(0, str(Path(__file__).resolve().parent))
