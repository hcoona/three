from __future__ import annotations

import unittest
from pathlib import Path


def main() -> int:
    tests = Path(__file__).resolve().parents[1] / "tests"
    suite = unittest.defaultTestLoader.discover(str(tests), pattern="test_*.py")
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
