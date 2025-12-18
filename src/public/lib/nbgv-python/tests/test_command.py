"""Tests for command discovery and parsing."""

from __future__ import annotations

import os

import pytest
from nbgv_python.command import (
    ENV_COMMAND_OVERRIDE,
    discover_command,
    parse_command_tokens,
)
from nbgv_python.errors import NbgvNotFoundError


def test_parse_command_tokens_from_string() -> None:
    """Ensure string commands split correctly on whitespace."""
    tokens = parse_command_tokens("python -m nbgv")
    assert tokens == ["python", "-m", "nbgv"]


def test_parse_command_tokens_from_sequence() -> None:
    """Ensure sequences of strings are preserved."""
    tokens = parse_command_tokens(["python", "script.py"])
    assert tokens == ["python", "script.py"]


def test_discover_command_prefers_environment_override(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Environment override should take precedence over PATH detection."""
    monkeypatch.setenv(ENV_COMMAND_OVERRIDE, "custom-nbgv --flag")
    monkeypatch.setenv("PATH", "")
    tokens = discover_command(env=os.environ, which=lambda _: None)
    assert tokens == ["custom-nbgv", "--flag"]


def test_discover_command_falls_back_to_dotnet_tool(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Fallback to `dotnet tool run nbgv` when nbgv is absent."""
    monkeypatch.delenv(ENV_COMMAND_OVERRIDE, raising=False)
    fake_path = {"dotnet": "C:/Program Files/dotnet/dotnet"}

    def fake_which(name: str) -> str | None:
        return fake_path.get(name)

    tokens = discover_command(env=os.environ, which=fake_which)
    assert tokens == [fake_path["dotnet"], "tool", "run", "nbgv"]


def test_discover_command_raises_when_not_found(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Raise an explicit error when the command cannot be resolved."""
    monkeypatch.delenv(ENV_COMMAND_OVERRIDE, raising=False)
    monkeypatch.setenv("PATH", os.pathsep.join(["/tmp", "/usr/bin"]))  # noqa: S108
    with pytest.raises(NbgvNotFoundError) as exc:
        discover_command(env=os.environ, which=lambda _: None)
    assert "/usr/bin" in " ".join(exc.value.search_paths)
