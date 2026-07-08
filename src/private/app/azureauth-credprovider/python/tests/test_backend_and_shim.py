"""Tests for the AzureAuth keyring backend and shim."""
# ruff: noqa: S101

from __future__ import annotations

import hashlib
import json
import os
import stat
from io import StringIO
from typing import TYPE_CHECKING

import pytest

from azureauth_credprovider_keyring.backend import AzureAuthKeyringBackend
from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    DIGEST_SHA256_REQUIRED,
    DIGEST_SHA256_REQUIRED_WEAK_PATH,
    EXIT_CONFIGURATION_ERROR,
    EXIT_NO_CREDENTIAL,
    EXIT_SUCCESS,
    OWNER_DEFERRED,
    OWNER_REQUIRED,
    PLATFORM_LINUX,
    PLATFORM_MACOS,
    PLATFORM_WINDOWS,
    PRODUCT_ID,
    SYMLINK_BEST_EFFORT_REJECT,
    SYMLINK_REJECT,
    HelperIntegrityError,
)
from azureauth_credprovider_keyring.endpoint import (
    EndpointStatus,
    classify_python_feed_endpoint,
)
from azureauth_credprovider_keyring.integrity import ENV_MANIFEST_PATH
from azureauth_credprovider_keyring.shim import run

if TYPE_CHECKING:
    from pathlib import Path

PYTHON_FEED = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
TEST_TOKEN = "phase11-token"  # noqa: S105


def test_endpoint_classifies_modern_and_legacy_python_feeds() -> None:
    """Supported Azure Artifacts Python feed URL shapes are accepted."""
    supported = [
        PYTHON_FEED,
        "https://dev.azure.com/org/project/_packaging/feed/pypi/simple/",
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/",
        "https://org.pkgs.visualstudio.com/DefaultCollection/_packaging/feed/pypi/simple/",
    ]

    for service in supported:
        check = classify_python_feed_endpoint(service)
        assert check.status == EndpointStatus.SUPPORTED
        assert check.organization == "org"
        assert check.feed == "feed"


def test_backend_unsupported_host_returns_none_without_manifest(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Unsupported non-Azure hosts are keyring no-credential results."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))

    backend = AzureAuthKeyringBackend()

    assert backend.get_password("https://example.com/simple/", "user") is None
    assert backend.get_credential("https://example.com/simple/", "user") is None


@pytest.mark.skipif(os.name == "nt", reason="POSIX executable-bit test helper")
def test_backend_get_credential_invokes_validated_helper(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """The backend validates integrity before invoking the helper."""
    marker = tmp_path / "invoked.marker"
    helper = _write_helper(tmp_path)
    manifest = _write_manifest(tmp_path, helper)
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(manifest))
    monkeypatch.setenv("AZUREAUTH_TEST_MARKER", str(marker))

    credential = AzureAuthKeyringBackend().get_credential(PYTHON_FEED, None)

    assert credential is not None
    assert credential.username == "AzureDevOps"
    assert credential.password == TEST_TOKEN
    assert marker.read_text(encoding="utf-8") == "invoked\n"


@pytest.mark.skipif(os.name == "nt", reason="POSIX executable-bit test helper")
def test_backend_rejects_digest_mismatch_before_invocation(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Digest mismatches fail closed before helper execution."""
    marker = tmp_path / "invoked.marker"
    helper = _write_helper(tmp_path)
    manifest = _write_manifest(tmp_path, helper, sha256="0" * 64)
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(manifest))
    monkeypatch.setenv("AZUREAUTH_TEST_MARKER", str(marker))

    with pytest.raises(HelperIntegrityError):
        AzureAuthKeyringBackend().get_password(PYTHON_FEED, "user")

    assert not marker.exists()


@pytest.mark.skipif(os.name == "nt", reason="POSIX executable-bit test helper")
def test_shim_creds_mode_outputs_keyring_credential_pair(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """The controlled shim translates keyring get --mode creds."""
    marker = tmp_path / "invoked.marker"
    helper = _write_helper(tmp_path)
    manifest = _write_manifest(tmp_path, helper)
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(manifest))
    monkeypatch.setenv("AZUREAUTH_TEST_MARKER", str(marker))
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        ["get", PYTHON_FEED, "--mode", "creds"],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_SUCCESS
    assert stdout.getvalue() == f"AzureDevOps\n{TEST_TOKEN}\n"
    assert stderr.getvalue() == ""
    assert marker.exists()


def test_shim_unsupported_host_returns_no_credential_without_manifest(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """The shim does not fail unrelated keyring requests."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        ["get", "https://example.com/simple/", "--mode", "creds"],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_NO_CREDENTIAL
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == ""


def test_shim_bad_azure_path_hard_fails_without_helper_invocation(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Malformed Azure Artifacts service URLs are hard failures."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        ["get", "https://pkgs.dev.azure.com/org/_packaging/feed/npm"],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert "Azure Artifacts Python feed URL" in stderr.getvalue()


def test_shim_bad_azure_port_hard_fails_without_uncaught_exception(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Malformed Azure Artifacts ports are controlled hard failures."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "get",
            "https://pkgs.dev.azure.com:99999/org/_packaging/feed/pypi/simple/",
        ],
        stdout=stdout,
        stderr=stderr,
    )

    check = classify_python_feed_endpoint(
        "https://pkgs.dev.azure.com:99999/org/_packaging/feed/pypi/simple/"
    )
    assert check.status == EndpointStatus.INVALID
    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert "Azure Artifacts Python feed URL" in stderr.getvalue()


def test_shim_raw_control_character_hard_fails_before_manifest(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Raw URL control characters are invalid before URL parsing."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))
    service = "https://pkgs.dev.azure.com\n/org/_packaging/feed/pypi/simple/"
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(["get", service], stdout=stdout, stderr=stderr)

    check = classify_python_feed_endpoint(service)
    assert check.status == EndpointStatus.INVALID
    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert "Azure Artifacts Python feed URL" in stderr.getvalue()
    assert "manifest" not in stderr.getvalue()


def _write_helper(tmp_path: Path) -> Path:
    helper = tmp_path / "keyring-helper"
    helper.write_text(
        "#!/usr/bin/env python3\n"
        "from pathlib import Path\n"
        "import os\n"
        "import sys\n"
        "\n"
        "marker = os.environ.get('AZUREAUTH_TEST_MARKER')\n"
        "if marker:\n"
        "    Path(marker).write_text('invoked\\n', encoding='utf-8')\n"
        "if sys.argv[-1] == 'creds':\n"
        f"    sys.stdout.write('AzureDevOps\\n{TEST_TOKEN}\\n')\n"
        "elif sys.argv[-1] == 'password':\n"
        f"    sys.stdout.write('{TEST_TOKEN}\\n')\n"
        "else:\n"
        "    sys.stderr.write('bad mode\\n')\n"
        "    raise SystemExit(64)\n",
        encoding="utf-8",
    )
    helper.chmod(
        stat.S_IRUSR
        | stat.S_IWUSR
        | stat.S_IXUSR
        | stat.S_IRGRP
        | stat.S_IXGRP,
    )
    return helper


def _write_manifest(
    tmp_path: Path,
    helper: Path,
    *,
    sha256: str | None = None,
) -> Path:
    manifest = tmp_path / "backend-manifest.json"
    manifest.write_text(
        json.dumps(_manifest_data(helper, sha256=sha256), sort_keys=True),
        encoding="utf-8",
    )
    return manifest


def _manifest_data(helper: Path, *, sha256: str | None) -> dict[str, object]:
    platform = _runtime_platform()
    weak_policy = platform in {PLATFORM_WINDOWS, PLATFORM_MACOS}
    return {
        "contractMajor": CONTRACT_MAJOR,
        "productId": PRODUCT_ID,
        "absoluteHelperPath": str(helper),
        "sha256": sha256 or hashlib.sha256(helper.read_bytes()).hexdigest(),
        "platform": platform,
        "ownerValidation": OWNER_DEFERRED if weak_policy else OWNER_REQUIRED,
        "symlinkPolicy": (
            SYMLINK_BEST_EFFORT_REJECT if weak_policy else SYMLINK_REJECT
        ),
        "digestPolicy": (
            DIGEST_SHA256_REQUIRED_WEAK_PATH
            if weak_policy
            else DIGEST_SHA256_REQUIRED
        ),
    }


def _runtime_platform() -> str:
    if os.name == "nt":
        return PLATFORM_WINDOWS
    if os.uname().sysname == "Darwin":
        return PLATFORM_MACOS
    return PLATFORM_LINUX
