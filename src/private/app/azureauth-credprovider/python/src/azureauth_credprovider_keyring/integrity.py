"""Installed helper configuration validation."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Any

from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    PLATFORM_LINUX,
    PLATFORM_MACOS,
    PLATFORM_WINDOWS,
    PRODUCT_ID,
    HelperContract,
    HelperIntegrityError,
)

ENV_MANIFEST_PATH = "AZUREAUTH_CREDPROVIDER_KEYRING_MANIFEST"
MANIFEST_RELATIVE_PATH = Path("python-keyring") / "backend-manifest.json"


def default_manifest_path() -> Path:
    """Return the default backend manifest path for the current platform."""
    if sys.platform.startswith("win"):
        local_app_data = os.environ.get("LOCALAPPDATA")
        root = (
            Path(local_app_data)
            if local_app_data
            else Path.home() / "AppData" / "Local"
        )
        return root / "AzureAuth" / "CredProvider" / MANIFEST_RELATIVE_PATH

    if sys.platform == "darwin":
        return (
            Path.home()
            / "Library"
            / "Application Support"
            / "AzureAuth"
            / "CredProvider"
            / MANIFEST_RELATIVE_PATH
        )

    xdg_config_home = os.environ.get("XDG_CONFIG_HOME")
    root = Path(xdg_config_home) if xdg_config_home else Path.home() / ".config"
    return root / PRODUCT_ID / MANIFEST_RELATIVE_PATH


def resolve_manifest_path(path: str | Path | None = None) -> Path:
    """Resolve the configured helper manifest path."""
    if path is not None:
        return Path(path)

    configured_path = os.environ.get(ENV_MANIFEST_PATH)
    if configured_path:
        return Path(configured_path)

    return default_manifest_path()


def load_and_validate_helper(
    manifest_path: str | Path | None = None,
) -> HelperContract:
    """Load and validate the configured helper before execution."""
    resolved_manifest_path = resolve_manifest_path(manifest_path)
    contract = _load_contract(resolved_manifest_path)
    _validate_contract_policy(contract)
    _validate_filesystem_binding(contract)
    return contract


def runtime_platform() -> str:
    """Return the keyring-helper-v2 platform value for this runtime."""
    if sys.platform.startswith("linux"):
        return PLATFORM_LINUX
    if sys.platform.startswith("win"):
        return PLATFORM_WINDOWS
    if sys.platform == "darwin":
        return PLATFORM_MACOS
    return "unsupported"


def _load_contract(manifest_path: Path) -> HelperContract:
    try:
        raw_contract = json.loads(manifest_path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        message = "Keyring helper manifest is missing."
        raise HelperIntegrityError(message) from error
    except (OSError, UnicodeError) as error:
        message = "Keyring helper manifest could not be read."
        raise HelperIntegrityError(message) from error
    except json.JSONDecodeError as error:
        message = "Keyring helper manifest is malformed."
        raise HelperIntegrityError(message) from error

    if not isinstance(raw_contract, dict):
        message = "Keyring helper manifest must be a JSON object."
        raise HelperIntegrityError(message)

    return HelperContract(
        contract_major=_read_int(raw_contract, "contractMajor"),
        product_id=_read_string(raw_contract, "productId"),
        absolute_helper_path=_read_string(raw_contract, "absoluteHelperPath"),
        platform=_read_string(raw_contract, "platform"),
    )


def _validate_contract_policy(contract: HelperContract) -> None:
    if contract.contract_major != CONTRACT_MAJOR:
        message = "Keyring helper contract major must be 2."
        raise HelperIntegrityError(message)

    if contract.product_id != PRODUCT_ID:
        message = "Keyring helper product ID is invalid."
        raise HelperIntegrityError(message)

    if contract.platform != runtime_platform():
        message = "Keyring helper platform does not match this runtime."
        raise HelperIntegrityError(message)

    if contract.platform not in {
        PLATFORM_LINUX,
        PLATFORM_WINDOWS,
        PLATFORM_MACOS,
    }:
        message = "Keyring helper platform is unsupported."
        raise HelperIntegrityError(message)


def _validate_filesystem_binding(contract: HelperContract) -> None:
    helper_path = Path(contract.absolute_helper_path)
    if not helper_path.is_absolute():
        message = "Keyring helper path must be absolute."
        raise HelperIntegrityError(message)

    if not helper_path.is_file():
        message = "Keyring helper executable is missing."
        raise HelperIntegrityError(message)

    if contract.platform != PLATFORM_WINDOWS and not os.access(
        helper_path, os.X_OK
    ):
        message = "Keyring helper executable is not executable by this user."
        raise HelperIntegrityError(message)


def _read_string(raw_contract: dict[str, Any], key: str) -> str:
    value = raw_contract.get(key)
    if not isinstance(value, str) or not value.strip():
        message = f"Keyring helper manifest field '{key}' is required."
        raise HelperIntegrityError(message)
    return value


def _read_int(raw_contract: dict[str, Any], key: str) -> int:
    value = raw_contract.get(key)
    if not isinstance(value, int):
        message = f"Keyring helper manifest field '{key}' is required."
        raise HelperIntegrityError(message)
    return value
