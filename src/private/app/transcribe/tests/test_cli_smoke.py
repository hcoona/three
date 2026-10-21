"""Credential-free smoke tests for the transcription CLI."""

import asyncio
import sys
from pathlib import Path
from runpy import run_path

import pytest

# Loading the script without running it imports the OpenAI, Azure and audio
# dependencies and exposes the CLI entry points.
TRANSCRIBE = run_path(str(Path(__file__).parents[1] / "main.py"))


@pytest.mark.parametrize(
    ("environment", "missing"),
    [
        ({}, "OPENAI_BASE_URL"),
        ({"OPENAI_BASE_URL": "https://example.invalid/v1"}, "OPENAI_API_KEY"),
    ],
)
def test_missing_openai_configuration_fails_before_reading_audio(
    monkeypatch: pytest.MonkeyPatch,
    environment: dict[str, str],
    missing: str,
) -> None:
    """Report missing OpenAI settings before any audio or network access."""
    for name in (
        "OPENAI_BASE_URL",
        "OPENAI_API_KEY",
        "AZURE_OPENAI_API_VERSION",
    ):
        monkeypatch.delenv(name, raising=False)
    for name, value in environment.items():
        monkeypatch.setenv(name, value)
    monkeypatch.setattr(sys, "argv", ["main.py", "missing-audio.mp3"])

    with pytest.raises(ValueError, match=missing):
        asyncio.run(TRANSCRIBE["main"]())
