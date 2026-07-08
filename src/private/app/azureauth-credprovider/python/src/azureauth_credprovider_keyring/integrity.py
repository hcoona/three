"""Helper ownership and integrity validation."""

from __future__ import annotations

import hashlib
import json
import os
import sys
from pathlib import Path
from typing import Any

from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    DIGEST_SHA256_REQUIRED,
    DIGEST_SHA256_REQUIRED_WEAK_PATH,
    OWNER_DEFERRED,
    OWNER_REQUIRED,
    PLATFORM_LINUX,
    PLATFORM_MACOS,
    PLATFORM_WINDOWS,
    PRODUCT_ID,
    SYMLINK_BEST_EFFORT_REJECT,
    SYMLINK_REJECT,
    HelperIntegrityError,
    IntegrityContract,
)

ENV_MANIFEST_PATH = "AZUREAUTH_CREDPROVIDER_KEYRING_MANIFEST"
MANIFEST_RELATIVE_PATH = Path("python-keyring") / "backend-manifest.json"
SHA256_HEX_LENGTH = 64


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
    """Resolve the configured helper integrity manifest path."""
    if path is not None:
        return Path(path)

    configured_path = os.environ.get(ENV_MANIFEST_PATH)
    if configured_path:
        return Path(configured_path)

    return default_manifest_path()


def load_and_validate_helper(
    manifest_path: str | Path | None = None,
) -> IntegrityContract:
    """Load and validate the helper integrity manifest before execution."""
    resolved_manifest_path = resolve_manifest_path(manifest_path)
    contract = _load_contract(resolved_manifest_path)
    _validate_contract_policy(contract)
    _validate_filesystem_binding(contract, resolved_manifest_path)
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


def _load_contract(manifest_path: Path) -> IntegrityContract:
    try:
        raw_contract = json.loads(manifest_path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        message = "Keyring helper integrity manifest is missing."
        raise HelperIntegrityError(message) from error
    except json.JSONDecodeError as error:
        message = "Keyring helper integrity manifest is malformed."
        raise HelperIntegrityError(message) from error

    if not isinstance(raw_contract, dict):
        message = "Keyring helper integrity manifest must be a JSON object."
        raise HelperIntegrityError(message)

    return IntegrityContract(
        contract_major=_read_int(raw_contract, "contractMajor"),
        product_id=_read_string(raw_contract, "productId"),
        absolute_helper_path=_read_string(raw_contract, "absoluteHelperPath"),
        sha256=_read_string(raw_contract, "sha256"),
        platform=_read_string(raw_contract, "platform"),
        owner_validation=_read_string(raw_contract, "ownerValidation"),
        symlink_policy=_read_string(raw_contract, "symlinkPolicy"),
        digest_policy=_read_string(raw_contract, "digestPolicy"),
    )


def _validate_contract_policy(contract: IntegrityContract) -> None:
    if contract.contract_major != CONTRACT_MAJOR:
        message = "Keyring helper integrity contract major must be 2."
        raise HelperIntegrityError(message)

    if contract.product_id != PRODUCT_ID:
        message = "Keyring helper integrity product ID is invalid."
        raise HelperIntegrityError(message)

    if contract.platform != runtime_platform():
        message = (
            "Keyring helper integrity platform does not match this runtime."
        )
        raise HelperIntegrityError(message)

    if not _is_sha256_hex(contract.sha256):
        message = "Keyring helper SHA-256 digest is invalid."
        raise HelperIntegrityError(message)

    if contract.platform == PLATFORM_LINUX:
        if (
            contract.owner_validation != OWNER_REQUIRED
            or contract.symlink_policy != SYMLINK_REJECT
            or contract.digest_policy != DIGEST_SHA256_REQUIRED
        ):
            message = "Linux keyring helper integrity policy is not strong."
            raise HelperIntegrityError(message)
        return

    if contract.platform in {PLATFORM_WINDOWS, PLATFORM_MACOS}:
        if (
            contract.owner_validation != OWNER_DEFERRED
            or contract.symlink_policy != SYMLINK_BEST_EFFORT_REJECT
            or contract.digest_policy != DIGEST_SHA256_REQUIRED_WEAK_PATH
        ):
            message = (
                "Windows/macOS keyring helper integrity policy is invalid."
            )
            raise HelperIntegrityError(message)
        return

    message = "Keyring helper integrity platform is unsupported."
    raise HelperIntegrityError(message)


def _validate_filesystem_binding(
    contract: IntegrityContract,
    manifest_path: Path,
) -> None:
    helper_path = Path(contract.absolute_helper_path)
    if not helper_path.is_absolute():
        message = "Keyring helper path must be absolute."
        raise HelperIntegrityError(message)

    try:
        helper_lstat = helper_path.lstat()
    except FileNotFoundError as error:
        message = "Keyring helper executable is missing."
        raise HelperIntegrityError(message) from error

    if helper_path.is_symlink():
        message = "Keyring helper executable must not be a symbolic link."
        raise HelperIntegrityError(message)

    if not helper_path.is_file():
        message = "Keyring helper path must be a regular file."
        raise HelperIntegrityError(message)

    if contract.platform != PLATFORM_WINDOWS and not os.access(
        helper_path, os.X_OK
    ):
        message = "Keyring helper executable is not executable by this user."
        raise HelperIntegrityError(message)

    if contract.owner_validation == OWNER_REQUIRED:
        _validate_owner(manifest_path, helper_lstat.st_uid)

    actual_sha256 = hashlib.sha256(helper_path.read_bytes()).hexdigest()
    if actual_sha256 != contract.sha256.lower():
        message = (
            "Keyring helper executable digest does not match the manifest."
        )
        raise HelperIntegrityError(message)


def _validate_owner(manifest_path: Path, helper_uid: int) -> None:
    if not hasattr(os, "getuid"):
        message = "Keyring helper owner validation is unavailable."
        raise HelperIntegrityError(message)

    process_uid = os.getuid()
    if helper_uid != process_uid:
        message = "Keyring helper executable owner does not match this user."
        raise HelperIntegrityError(message)

    manifest_uid = manifest_path.stat().st_uid
    if manifest_uid != process_uid:
        message = "Keyring helper manifest owner does not match this user."
        raise HelperIntegrityError(message)


def _read_string(raw_contract: dict[str, Any], key: str) -> str:
    value = raw_contract.get(key)
    if not isinstance(value, str) or not value.strip():
        message = f"Keyring helper integrity field '{key}' is required."
        raise HelperIntegrityError(message)
    return value


def _read_int(raw_contract: dict[str, Any], key: str) -> int:
    value = raw_contract.get(key)
    if not isinstance(value, int):
        message = f"Keyring helper integrity field '{key}' is required."
        raise HelperIntegrityError(message)
    return value


def _is_sha256_hex(value: str) -> bool:
    return len(value) == SHA256_HEX_LENGTH and all(
        character in "0123456789abcdefABCDEF" for character in value
    )
