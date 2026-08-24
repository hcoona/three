"""Run the Streamlit app via the package entry point."""

from __future__ import annotations

import sys
from pathlib import Path

from streamlit.web.cli import main as streamlit_main

from factorio_cycle_calculator import app as app_module


def main(argv: list[str] | None = None) -> None:
    """Launch the Streamlit application."""
    module_file = app_module.__file__
    if module_file is None:
        raise RuntimeError(  # noqa: TRY003
            "Could not locate factorio_cycle_calculator.app module file."  # noqa: EM101
        )
    app_path = Path(module_file).resolve()
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
