"""Application entry point for the Factorio cycle calculator."""

from __future__ import annotations

from factorio_cycle_calculator.views import run_app


def main() -> None:
    """Run the Streamlit app."""
    run_app()


if __name__ == "__main__":
    main()
