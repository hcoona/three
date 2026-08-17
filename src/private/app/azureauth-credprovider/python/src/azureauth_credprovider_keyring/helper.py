"""Invocation wrapper for the fixed keyring-helper-v2 command."""

from __future__ import annotations

import subprocess
from typing import TYPE_CHECKING

from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    EXIT_FATAL,
    EXIT_NO_CREDENTIAL,
    MODE_CREDENTIALS,
    MODE_PASSWORD,
    HelperCredential,
    HelperExecutionError,
    HelperIntegrityError,
    HelperProtocolError,
    NoCredentialError,
)
from azureauth_credprovider_keyring.integrity import load_and_validate_helper

if TYPE_CHECKING:
    from collections.abc import Sequence
    from pathlib import Path


def get_password(
    service: str,
    username: str | None = None,
    *,
    manifest_path: str | Path | None = None,
) -> str:
    """Return password material from the helper."""
    return invoke_helper(
        service,
        username,
        mode=MODE_PASSWORD,
        manifest_path=manifest_path,
    ).password


def get_credentials(
    service: str,
    username: str | None = None,
    *,
    manifest_path: str | Path | None = None,
) -> HelperCredential:
    """Return username and password material from the helper."""
    return invoke_helper(
        service,
        username,
        mode=MODE_CREDENTIALS,
        manifest_path=manifest_path,
    )


def invoke_helper(
    service: str,
    username: str | None,
    *,
    mode: str,
    manifest_path: str | Path | None = None,
) -> HelperCredential:
    """Validate and invoke the configured keyring-helper-v2 executable."""
    contract = load_and_validate_helper(manifest_path)
    args = build_helper_args(
        contract.absolute_helper_path, service, username, mode
    )
    try:
        completed = subprocess.run(
            args,
            stdin=subprocess.DEVNULL,
            capture_output=True,
            check=False,
        )
    except PermissionError as error:
        message = "Keyring helper execution was denied."
        raise HelperIntegrityError(message) from error
    except FileNotFoundError as error:
        message = "Keyring helper executable disappeared before execution."
        raise HelperIntegrityError(message) from error
    except OSError as error:
        message = "Keyring helper process could not be started."
        raise HelperExecutionError(EXIT_FATAL, message) from error

    if completed.returncode == 0:
        stdout = _decode_success_stdout(completed.stdout)
        return _parse_success_stdout(stdout, mode)

    if completed.returncode == EXIT_NO_CREDENTIAL:
        raise NoCredentialError()

    stderr = _decode_failure_stderr(completed.stderr)
    safe_message = _safe_failure_message(stderr)
    raise HelperExecutionError(completed.returncode or EXIT_FATAL, safe_message)


def build_helper_args(
    helper_path: str,
    service: str,
    username: str | None,
    mode: str,
) -> list[str]:
    """Build the fixed non-shell keyring-helper-v2 argv."""
    if mode not in {MODE_PASSWORD, MODE_CREDENTIALS}:
        message = "Keyring helper mode must be password or creds."
        raise HelperProtocolError(message)

    args = [
        helper_path,
        "python-keyring",
        "get",
        "--protocol-version",
        str(CONTRACT_MAJOR),
        "--service",
        service,
    ]
    if username:
        args.extend(["--username", username])
    args.extend(["--mode", mode])
    return args


def _parse_success_stdout(stdout: str, mode: str) -> HelperCredential:
    if mode == MODE_PASSWORD:
        fields = _split_exact_stdout(stdout, expected_field_count=1)
        return HelperCredential(username=None, password=fields[0])

    fields = _split_exact_stdout(stdout, expected_field_count=2)
    return HelperCredential(username=fields[0], password=fields[1])


def _decode_success_stdout(stdout: bytes) -> str:
    try:
        return stdout.decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        message = "Keyring helper stdout is not valid UTF-8."
        raise HelperProtocolError(message) from None


def _decode_failure_stderr(stderr: bytes) -> str:
    try:
        return stderr.decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        message = "Keyring helper stderr is not valid UTF-8."
        raise HelperProtocolError(message) from None


def _split_exact_stdout(
    stdout: str, *, expected_field_count: int
) -> Sequence[str]:
    fields = stdout.split("\n")
    if fields and fields[-1] == "":
        fields = fields[:-1]

    if len(fields) != expected_field_count or any(
        not field for field in fields
    ):
        message = "Keyring helper success stdout is malformed."
        raise HelperProtocolError(message)

    if any("\r" in field for field in fields):
        message = "Keyring helper success stdout contains CR characters."
        raise HelperProtocolError(message)

    return fields


def _safe_failure_message(stderr: str) -> str:
    stripped = stderr.strip()
    if not stripped:
        return "Keyring helper execution failed."
    return stripped
