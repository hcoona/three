"""Environment bootstrap helpers."""

from __future__ import annotations

import os
from pathlib import Path

from dotenv import load_dotenv


def load_app_env() -> None:
    """Load .env values without overriding existing environment variables.

    Resolution order for dotenv files:
    1) Path from FACTORIO_ENV_FILE (if provided)
    2) .env in the current working directory

    Existing process environment variables always win because override=False.
    """
    explicit_env_file = os.environ.get("FACTORIO_ENV_FILE")
    if explicit_env_file:
        explicit_path = Path(explicit_env_file).expanduser()
        if explicit_path.exists():
            load_dotenv(explicit_path, override=False)
            return

    cwd_env_path = Path.cwd() / ".env"
    if cwd_env_path.exists():
        load_dotenv(cwd_env_path, override=False)


def get_env_default(*keys: str) -> str:
    """Return the first non-empty environment value for given keys."""
    for key in keys:
        value = os.environ.get(key)
        if value:
            return value
    return ""
