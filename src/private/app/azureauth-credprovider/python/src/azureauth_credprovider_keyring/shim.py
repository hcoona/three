"""Controlled keyring CLI shim for Azure Artifacts Python feeds."""

from __future__ import annotations

import sys
from dataclasses import dataclass
from typing import TextIO

from azureauth_credprovider_keyring.contracts import (
    EXIT_CONFIGURATION_ERROR,
    EXIT_NO_CREDENTIAL,
    EXIT_SUCCESS,
    MODE_CREDENTIALS,
    MODE_PASSWORD,
    KeyringHelperError,
    NoCredentialError,
)
from azureauth_credprovider_keyring.endpoint import (
    EndpointStatus,
    classify_python_feed_endpoint,
)
from azureauth_credprovider_keyring.helper import invoke_helper

GET_MIN_ARG_COUNT = 2
MODE_ARG_COUNT = 2


@dataclass(frozen=True)
class ShimRequest:
    """Parsed keyring shim request."""

    service: str
    username: str | None
    mode: str


def main(argv: list[str] | None = None) -> int:
    """Run the controlled keyring shim."""
    return run(
        sys.argv[1:] if argv is None else argv,
        stdout=sys.stdout,
        stderr=sys.stderr,
    )


def run(argv: list[str], *, stdout: TextIO, stderr: TextIO) -> int:
    """Run the shim with explicit standard streams."""
    try:
        request = _parse_args(argv)
        endpoint_status = classify_python_feed_endpoint(request.service).status
        if endpoint_status == EndpointStatus.UNSUPPORTED:
            return EXIT_NO_CREDENTIAL
        if endpoint_status == EndpointStatus.INVALID:
            stderr.write(
                "error: keyring service must be an Azure Artifacts "
                "Python feed URL.\n"
            )
            return EXIT_CONFIGURATION_ERROR

        credential = invoke_helper(
            request.service,
            request.username,
            mode=request.mode,
        )
        if request.mode == MODE_CREDENTIALS:
            if credential.username is None:
                stderr.write(
                    "error: keyring helper did not return a username.\n"
                )
                return EXIT_CONFIGURATION_ERROR
            stdout.write(f"{credential.username}\n{credential.password}\n")
        else:
            stdout.write(f"{credential.password}\n")
    except NoCredentialError:
        return EXIT_NO_CREDENTIAL
    except KeyringHelperError as error:
        if error.safe_message:
            stderr.write(f"error: {error.safe_message}\n")
        return error.exit_code
    else:
        return EXIT_SUCCESS


def _parse_args(argv: list[str]) -> ShimRequest:
    if len(argv) < GET_MIN_ARG_COUNT or argv[0] != "get":
        message = "keyring shim supports only the get command."
        raise KeyringHelperError(EXIT_CONFIGURATION_ERROR, message)

    service = argv[1]
    remaining = argv[2:]
    username: str | None = None
    mode = MODE_PASSWORD

    if remaining and remaining[0] != "--mode":
        username = remaining[0]
        remaining = remaining[1:]

    if remaining:
        if len(remaining) != MODE_ARG_COUNT or remaining[0] != "--mode":
            message = "keyring shim get syntax is invalid."
            raise KeyringHelperError(EXIT_CONFIGURATION_ERROR, message)
        mode = remaining[1]

    if mode not in {MODE_PASSWORD, MODE_CREDENTIALS}:
        message = "keyring shim mode must be password or creds."
        raise KeyringHelperError(EXIT_CONFIGURATION_ERROR, message)

    return ShimRequest(service=service, username=username, mode=mode)
