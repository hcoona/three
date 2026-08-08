"""Python keyring backend for Azure Artifacts Python feeds."""

from __future__ import annotations

import importlib
from dataclasses import dataclass
from importlib.util import find_spec
from typing import cast

from azureauth_credprovider_keyring.contracts import (
    HelperProtocolError,
    NoCredentialError,
)
from azureauth_credprovider_keyring.endpoint import (
    EndpointStatus,
    classify_python_feed_endpoint,
)
from azureauth_credprovider_keyring.helper import get_credentials, get_password


class _FallbackKeyringBackend:
    """Fallback base class used when keyring is not installed."""

    priority = 0


@dataclass(frozen=True)
class _SimpleCredential:
    """Credential shape matching keyring.credentials.SimpleCredential."""

    username: str
    password: str


def _load_keyring_backend() -> type[_FallbackKeyringBackend]:
    if find_spec("keyring") is None:
        return _FallbackKeyringBackend

    module = importlib.import_module("keyring.backend")
    return cast("type[_FallbackKeyringBackend]", module.KeyringBackend)


def _load_keyring_credential(
    keyring_backend: type[_FallbackKeyringBackend],
) -> type[_SimpleCredential]:
    if keyring_backend is _FallbackKeyringBackend:
        return _SimpleCredential

    module = importlib.import_module("keyring.credentials")
    return cast("type[_SimpleCredential]", module.SimpleCredential)


_KEYRING_BACKEND = _load_keyring_backend()
_KEYRING_CREDENTIAL = _load_keyring_credential(_KEYRING_BACKEND)


class AzureAuthKeyringBackend(_KEYRING_BACKEND):
    """Thin backend that delegates Azure Artifacts credentials to the helper."""

    priority = 9

    def get_password(self, service: str, username: str | None) -> str | None:
        """Return a password for Azure Artifacts Python feed endpoints."""
        endpoint_status = _get_endpoint_status(service)
        if endpoint_status == EndpointStatus.UNSUPPORTED:
            return None

        try:
            return get_password(service, username)
        except NoCredentialError:
            return None

    def get_credential(
        self,
        service: str,
        username: str | None,
    ) -> _SimpleCredential | None:
        """Return username and password for Azure Artifacts feeds."""
        endpoint_status = _get_endpoint_status(service)
        if endpoint_status == EndpointStatus.UNSUPPORTED:
            return None

        try:
            credential = get_credentials(service, username)
        except NoCredentialError:
            return None

        if credential.username is None:
            message = "Keyring helper did not return a username."
            raise HelperProtocolError(message)

        return _KEYRING_CREDENTIAL(
            credential.username,
            credential.password,
        )

    def set_password(self, service: str, username: str, password: str) -> None:
        """Reject credential writes so other backends can own them."""
        raise NotImplementedError

    def delete_password(self, service: str, username: str) -> None:
        """Reject credential deletes so other backends can own them."""
        raise NotImplementedError


def _get_endpoint_status(service: str) -> EndpointStatus:
    endpoint_check = classify_python_feed_endpoint(service)
    if endpoint_check.status == EndpointStatus.INVALID:
        message = "Keyring service must be an Azure Artifacts Python feed URL."
        raise HelperProtocolError(message)
    return endpoint_check.status
