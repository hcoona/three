"""Shared contracts for the azureauth keyring integration."""

from __future__ import annotations

from dataclasses import dataclass

PRODUCT_ID = "azureauth-credprovider"
CONTRACT_MAJOR = 2

EXIT_SUCCESS = 0
EXIT_NO_CREDENTIAL = 1
EXIT_CONFIGURATION_ERROR = 64
EXIT_INTEGRITY_FAILURE = 65
EXIT_FATAL = 70

MODE_PASSWORD = "password"  # noqa: S105
MODE_CREDENTIALS = "creds"
SUPPORTED_MODES = frozenset({MODE_PASSWORD, MODE_CREDENTIALS})

PLATFORM_LINUX = "linux"
PLATFORM_WINDOWS = "windows"
PLATFORM_MACOS = "macOs"


class KeyringHelperError(RuntimeError):
    """Base class for keyring helper errors safe to show to users."""

    def __init__(self, exit_code: int, safe_message: str) -> None:
        """Initialize the helper error."""
        super().__init__(safe_message)
        self.exit_code = exit_code
        self.safe_message = safe_message


class NoCredentialError(KeyringHelperError):
    """Raised when the helper reports no credential."""

    def __init__(self) -> None:
        """Initialize the no-credential error."""
        super().__init__(EXIT_NO_CREDENTIAL, "")


class HelperProtocolError(KeyringHelperError):
    """Raised when keyring input or helper output violates the contract."""

    def __init__(self, safe_message: str) -> None:
        """Initialize a helper protocol error."""
        super().__init__(EXIT_CONFIGURATION_ERROR, safe_message)


class HelperIntegrityError(KeyringHelperError):
    """Raised when helper validation fails before execution."""

    def __init__(self, safe_message: str) -> None:
        """Initialize a helper integrity error."""
        super().__init__(EXIT_INTEGRITY_FAILURE, safe_message)


class HelperExecutionError(KeyringHelperError):
    """Raised when helper execution fails."""


@dataclass(frozen=True)
class HelperCredential:
    """Credential material returned by the helper."""

    username: str | None
    password: str


@dataclass(frozen=True)
class HelperContract:
    """Parsed keyring-helper-v2 installation contract."""

    contract_major: int
    product_id: str
    absolute_helper_path: str
    platform: str
