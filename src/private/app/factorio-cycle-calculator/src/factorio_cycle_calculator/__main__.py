"""Run the Streamlit app via the package entry point."""

from __future__ import annotations

import sys
from importlib import import_module
from pathlib import Path

from streamlit.web.cli import main as streamlit_main


def main(argv: list[str] | None = None) -> None:
    """Launch the Streamlit application."""
    module = import_module("factorio_cycle_calculator.app")
    app_path = Path(module.__file__).resolve()
    old_argv = sys.argv
    extra_args = old_argv[1:] if argv is None else list(argv)
    new_argv = ["streamlit", "run", str(app_path), *extra_args]
    try:
        sys.argv = new_argv
        streamlit_main()
    finally:
        sys.argv = old_argv


if __name__ == "__main__":
    main()
