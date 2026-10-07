"""Read pytest's public configuration before its default collect/run hook."""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
import warnings
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path


def inspect_configuration(directory: Path) -> dict[str, object]:
    """Transfer native fields without qualifying arbitrary plugin effects."""
    root = directory.resolve(strict=True)
    if Path.cwd() != root:
        message = "The query must run at its requested endpoint."
        raise ValueError(message)
    os.environ.pop("PYTEST_ADDOPTS", None)
    os.environ.pop("PYTEST_PLUGINS", None)
    os.environ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1"
    import pytest  # noqa: PLC0415

    class ConfigurationQuery:
        response: dict[str, object] | None = None

        @pytest.hookimpl(tryfirst=True)
        def pytest_cmdline_main(self, config: pytest.Config) -> pytest.ExitCode:
            """Return before default configure, collection and execution."""
            self.response = {
                "directory": str(root),
                "root": str(config.rootpath),
                "configuration_file": (
                    str(config.inipath) if config.inipath is not None else None
                ),
                "testpaths": config.getini("testpaths"),
                "addopts": config.getini("addopts"),
                "pythonpath": [
                    str(path) for path in config.getini("pythonpath")
                ],
                "required_plugins": config.getini("required_plugins"),
            }
            return pytest.ExitCode.OK

    query = ConfigurationQuery()
    # Configuration can emit source-valued diagnostics before the early hook.
    with (
        warnings.catch_warnings(record=True),
        redirect_stdout(io.StringIO()),
        redirect_stderr(io.StringIO()),
    ):
        result = pytest.main(
            [
                "--noconftest",
                "--disable-plugin-autoload",
                "-p",
                "no:cacheprovider",
            ],
            plugins=[query],
        )
    if result != pytest.ExitCode.OK or query.response is None:
        message = "Native pytest configuration did not complete."
        raise ValueError(message)
    return query.response


def main() -> int:
    """Return one complete native response or a sanitized terminal failure."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    arguments = parser.parse_args()
    try:
        response = inspect_configuration(arguments.directory)
    except Exception as error:  # noqa: BLE001
        print(  # noqa: T201
            f"Pytest configuration extraction failed ({type(error).__name__}).",
            file=sys.stderr,
        )
        return 1
    print(  # noqa: T201
        json.dumps(response, ensure_ascii=False, allow_nan=False)
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
