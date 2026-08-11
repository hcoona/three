"""Tests for the AzureAuth keyring backend and shim."""
# ruff: noqa: S101

from __future__ import annotations

import json
import os
import shutil
import stat
import subprocess
from io import StringIO
from typing import TYPE_CHECKING

import pytest

from azureauth_credprovider_keyring import backend as backend_module
from azureauth_credprovider_keyring import helper
from azureauth_credprovider_keyring import shim as shim_module
from azureauth_credprovider_keyring.backend import AzureAuthKeyringBackend
from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    EXIT_CONFIGURATION_ERROR,
    EXIT_FATAL,
    EXIT_NO_CREDENTIAL,
    EXIT_SUCCESS,
    PLATFORM_LINUX,
    PLATFORM_MACOS,
    PLATFORM_WINDOWS,
    PRODUCT_ID,
    HelperContract,
    HelperCredential,
    HelperIntegrityError,
    HelperProtocolError,
)
from azureauth_credprovider_keyring.endpoint import (
    EndpointStatus,
    classify_python_feed_endpoint,
)
from azureauth_credprovider_keyring.helper import build_helper_args
from azureauth_credprovider_keyring.integrity import ENV_MANIFEST_PATH
from azureauth_credprovider_keyring.shim import run

if TYPE_CHECKING:
    from pathlib import Path

PYTHON_FEED = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
TEST_TOKEN = "phase11-token"  # noqa: S105


def test_helper_uses_shared_cli_keyring_entrypoint() -> None:
    """The backend invokes the product apphost through its fixed entrypoint."""
    assert build_helper_args(
        "/opt/azureauth-credprovider/azureauth-credprovider",
        PYTHON_FEED,
        "user",
        "creds",
    ) == [
        "/opt/azureauth-credprovider/azureauth-credprovider",
        "python-keyring",
        "get",
        "--protocol-version",
        "2",
        "--service",
        PYTHON_FEED,
        "--username",
        "user",
        "--mode",
        "creds",
    ]


def test_endpoint_classifies_modern_and_legacy_python_feeds() -> None:
    """Supported Azure Artifacts Python feed URL shapes are accepted."""
    supported = [
        PYTHON_FEED,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/",
        "https://dev.azure.com/org/project/_packaging/feed/pypi/simple/",
        "https://dev.azure.com/org/project/_packaging/feed/pypi/upload/",
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/",
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/upload/",
        "https://org.pkgs.visualstudio.com/DefaultCollection/_packaging/feed/pypi/simple/",
        "https://org.pkgs.visualstudio.com/DefaultCollection/_packaging/feed/pypi/upload/",
    ]

    for service in supported:
        check = classify_python_feed_endpoint(service)
        assert check.status == EndpointStatus.SUPPORTED
        assert check.organization == "org"
        assert check.feed == "feed"


def test_endpoint_accepts_explicit_https_default_port() -> None:
    """Explicit HTTPS port 443 is equivalent to the implicit default."""
    check = classify_python_feed_endpoint(
        "https://pkgs.dev.azure.com:443/org/_packaging/feed/pypi/simple/",
    )

    assert check.status == EndpointStatus.SUPPORTED
    assert check.organization == "org"
    assert check.feed == "feed"


@pytest.mark.parametrize("port", ["444", "not-a-port", "99999"])
def test_endpoint_rejects_non_default_and_malformed_ports(port: str) -> None:
    """Only the default HTTPS port is valid for Azure Artifacts feeds."""
    check = classify_python_feed_endpoint(
        f"https://pkgs.dev.azure.com:{port}/org/_packaging/feed/pypi/simple/",
    )

    assert check.status == EndpointStatus.INVALID


@pytest.mark.parametrize(
    "service",
    [
        "https:///pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
        "https:///dev.azure.com/org/project/_packaging/feed/pypi/upload/",
    ],
)
def test_endpoint_invalidates_leading_slash_azure_host_regression(
    service: str,
) -> None:
    """Keep malformed Azure URLs invalid when the host moves into the path."""
    check = classify_python_feed_endpoint(service)

    assert check.status == EndpointStatus.INVALID


def test_backend_unsupported_host_returns_none_without_manifest(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Unsupported non-Azure hosts are keyring no-credential results."""
    monkeypatch.setenv(ENV_MANIFEST_PATH, str(tmp_path / "missing.json"))

    backend = AzureAuthKeyringBackend()

    assert backend.get_password("https://example.com/simple/", "user") is None
    assert backend.get_credential("https://example.com/simple/", "user") is None


@pytest.mark.parametrize(
    "service",
    [
        "PyPI",
        "system credential",
        "git:https://example.com",
        "https://example.com/simple/",
        "notpkgs.dev.azure.com",
        "visualstudio.com.example.org",
    ],
)
def test_backend_unrelated_services_chain_without_invoking_helper(
    monkeypatch: pytest.MonkeyPatch,
    service: str,
) -> None:
    """A global backend leaves unrelated URL and non-URL services alone."""
    helper_calls: list[tuple[object, ...]] = []

    def unexpected_helper_call(*args: object, **kwargs: object) -> None:
        helper_calls.append((*args, kwargs))
        pytest.fail("unrelated keyring service invoked the AzureAuth helper")

    monkeypatch.setattr(
        backend_module,
        "get_password",
        unexpected_helper_call,
    )
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        unexpected_helper_call,
    )
    backend = AzureAuthKeyringBackend()

    assert backend.get_password(service, "user") is None
    assert backend.get_credential(service, "user") is None
    assert helper_calls == []


@pytest.mark.parametrize(
    "service",
    [
        "pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
        "dev.azure.com/org/project/_packaging/feed/pypi/simple/",
        "org.visualstudio.com/DefaultCollection/"
        "project/_packaging/feed/pypi/simple/",
        "https://[pkgs.dev.azure.com",
    ],
)
def test_backend_malformed_recognized_azure_services_fail_explicitly(
    monkeypatch: pytest.MonkeyPatch,
    service: str,
) -> None:
    """Malformed recognized Azure targets fail instead of chaining."""
    helper_calls: list[tuple[object, ...]] = []

    def unexpected_helper_call(*args: object, **kwargs: object) -> None:
        helper_calls.append((*args, kwargs))
        pytest.fail("malformed keyring service invoked the AzureAuth helper")

    monkeypatch.setattr(
        backend_module,
        "get_password",
        unexpected_helper_call,
    )
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        unexpected_helper_call,
    )
    backend = AzureAuthKeyringBackend()

    with pytest.raises(
        HelperProtocolError,
        match="Azure Artifacts Python feed URL",
    ):
        backend.get_password(service, "user")
    with pytest.raises(
        HelperProtocolError,
        match="Azure Artifacts Python feed URL",
    ):
        backend.get_credential(service, "user")
    assert helper_calls == []


@pytest.mark.parametrize("service", ["PyPI", "system credential"])
def test_backend_non_url_service_names_still_chain_regression(
    monkeypatch: pytest.MonkeyPatch,
    service: str,
) -> None:
    """Ordinary non-URL service names still chain to other backends."""
    helper_calls: list[tuple[object, ...]] = []

    def unexpected_helper_call(*args: object, **kwargs: object) -> None:
        helper_calls.append((*args, kwargs))
        pytest.fail("ordinary non-URL service invoked the AzureAuth helper")

    monkeypatch.setattr(
        backend_module,
        "get_password",
        unexpected_helper_call,
    )
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        unexpected_helper_call,
    )
    backend = AzureAuthKeyringBackend()

    assert backend.get_password(service, "user") is None
    assert backend.get_credential(service, "user") is None
    assert helper_calls == []


@pytest.mark.parametrize(
    "service",
    [
        "https:///pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
        "https:///dev.azure.com/org/project/_packaging/feed/pypi/upload/",
    ],
)
def test_backend_leading_slash_azure_hosts_fail_explicitly_regression(
    monkeypatch: pytest.MonkeyPatch,
    service: str,
) -> None:
    """Malformed Azure feed URLs fail explicitly instead of chaining."""
    helper_calls: list[tuple[object, ...]] = []

    def unexpected_helper_call(*args: object, **kwargs: object) -> None:
        helper_calls.append((*args, kwargs))
        pytest.fail("malformed Azure URL invoked the AzureAuth helper")

    monkeypatch.setattr(
        backend_module,
        "get_password",
        unexpected_helper_call,
    )
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        unexpected_helper_call,
    )
    backend = AzureAuthKeyringBackend()

    with pytest.raises(
        HelperProtocolError,
        match="Azure Artifacts Python feed URL",
    ):
        backend.get_password(service, "user")
    with pytest.raises(
        HelperProtocolError,
        match="Azure Artifacts Python feed URL",
    ):
        backend.get_credential(service, "user")
    assert helper_calls == []


@pytest.mark.parametrize(
    "service",
    [
        "https:///example.com/simple/",
        "https://[example.com",
        "/pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
    ],
)
def test_backend_unrelated_malformed_services_still_chain_regression(
    monkeypatch: pytest.MonkeyPatch,
    service: str,
) -> None:
    """Malformed non-Azure strings still chain to other backends."""
    helper_calls: list[tuple[object, ...]] = []

    def unexpected_helper_call(*args: object, **kwargs: object) -> None:
        helper_calls.append((*args, kwargs))
        pytest.fail("unrelated malformed service invoked the AzureAuth helper")

    monkeypatch.setattr(
        backend_module,
        "get_password",
        unexpected_helper_call,
    )
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        unexpected_helper_call,
    )
    backend = AzureAuthKeyringBackend()

    assert backend.get_password(service, "user") is None
    assert backend.get_credential(service, "user") is None
    assert helper_calls == []


@pytest.mark.skipif(os.name == "nt", reason="POSIX executable-bit test helper")
def test_backend_get_credential_invokes_validated_helper(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """The backend validates and invokes the configured helper."""
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
def test_backend_rejects_relative_helper_before_invocation(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    """Relative helper paths fail before helper execution."""
    marker = tmp_path / "invoked.marker"
    manifest = _write_manifest(tmp_path, "relative/keyring-helper")
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


@pytest.mark.parametrize(
    ("argv", "expected_username"),
    [
        (["get", PYTHON_FEED], None),
        (["get", PYTHON_FEED, "requested-user"], "requested-user"),
    ],
)
def test_shim_legacy_plaintext_get_invocation(
    monkeypatch: pytest.MonkeyPatch,
    argv: list[str],
    expected_username: str | None,
) -> None:
    """Uv and pre-pip-26.2 receive the legacy plaintext password."""
    password = 'legacy-token"\\suffix'  # noqa: S105
    helper_calls: list[tuple[str, str | None, str]] = []

    def fake_invoke_helper(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        helper_calls.append((service, username, mode))
        return HelperCredential(username="AzureDevOps", password=password)

    monkeypatch.setattr(shim_module, "invoke_helper", fake_invoke_helper)
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(argv, stdout=stdout, stderr=stderr)

    assert exit_code == EXIT_SUCCESS
    assert stdout.getvalue() == f"{password}\n"
    assert stderr.getvalue() == ""
    assert helper_calls == [(PYTHON_FEED, expected_username, "password")]


@pytest.mark.parametrize(
    ("username_args", "expected_username"),
    [
        ([], None),
        (["requested-user"], "requested-user"),
    ],
)
def test_shim_pip_26_2_creds_json_response_escapes_secrets(
    monkeypatch: pytest.MonkeyPatch,
    username_args: list[str],
    expected_username: str | None,
) -> None:
    """Pip 26.2 receives escaped JSON without credential leakage to stderr."""
    response_username = 'Azure"DevOps\\user'
    password = 'json-token"\\suffix'  # noqa: S105
    helper_calls: list[tuple[str, str | None, str]] = []

    def fake_invoke_helper(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        helper_calls.append((service, username, mode))
        return HelperCredential(
            username=response_username,
            password=password,
        )

    monkeypatch.setattr(shim_module, "invoke_helper", fake_invoke_helper)
    stdout = StringIO()
    stderr = StringIO()
    argv = [
        "--mode=creds",
        "--output=json",
        "get",
        PYTHON_FEED,
        *username_args,
    ]

    exit_code = run(argv, stdout=stdout, stderr=stderr)

    expected_payload = {
        "username": response_username,
        "password": password,
    }
    assert exit_code == EXIT_SUCCESS
    assert json.loads(stdout.getvalue()) == expected_payload
    assert stdout.getvalue() == json.dumps(expected_payload) + "\n"
    assert '\\"' in stdout.getvalue()
    assert "\\\\" in stdout.getvalue()
    assert stderr.getvalue() == ""
    assert password not in stderr.getvalue()
    assert helper_calls == [(PYTHON_FEED, expected_username, "creds")]


def test_shim_pip_26_2_json_unrelated_service_has_no_output(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """JSON mode preserves keyring chaining for unrelated services."""
    helper_called = False

    def unexpected_helper_call(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        del service, username, mode
        nonlocal helper_called
        helper_called = True
        pytest.fail("unrelated service invoked the AzureAuth helper")

    monkeypatch.setattr(
        shim_module,
        "invoke_helper",
        unexpected_helper_call,
    )
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "--mode=creds",
            "--output=json",
            "get",
            "https://example.com/simple/",
            "secret-username",
        ],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_NO_CREDENTIAL
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == ""
    assert "secret-username" not in stdout.getvalue()
    assert "secret-username" not in stderr.getvalue()
    assert helper_called is False


def test_shim_pip_26_2_json_missing_username_fails_without_secret_leak(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A malformed credential pair fails without emitting its password."""
    password = "must-not-leak"  # noqa: S105

    def fake_invoke_helper(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        assert (service, username, mode) == (PYTHON_FEED, None, "creds")
        return HelperCredential(username=None, password=password)

    monkeypatch.setattr(
        shim_module,
        "invoke_helper",
        fake_invoke_helper,
    )
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "--mode=creds",
            "--output=json",
            "get",
            PYTHON_FEED,
        ],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == (
        "error: keyring helper did not return a username.\n"
    )
    assert password not in stdout.getvalue()
    assert password not in stderr.getvalue()


def test_shim_documented_plain_output_option_retains_legacy_wire_format(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """The documented explicit plain format matches the default output."""
    helper_calls: list[tuple[str, str | None, str]] = []

    def fake_invoke_helper(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        helper_calls.append((service, username, mode))
        return HelperCredential(username="AzureDevOps", password=TEST_TOKEN)

    monkeypatch.setattr(shim_module, "invoke_helper", fake_invoke_helper)
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "--mode=creds",
            "--output=plain",
            "get",
            PYTHON_FEED,
        ],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_SUCCESS
    assert stdout.getvalue() == f"AzureDevOps\n{TEST_TOKEN}\n"
    assert stderr.getvalue() == ""
    assert helper_calls == [(PYTHON_FEED, None, "creds")]


def test_shim_rejects_undocumented_output_option_without_leaking_args() -> None:
    """Only keyring's documented plain and JSON output names are accepted."""
    secret_username = "must-not-leak"  # noqa: S105
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "--mode=creds",
            "--output=plaintext",
            "get",
            PYTHON_FEED,
            secret_username,
        ],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == (
        "error: keyring shim output must be plain or json.\n"
    )
    assert secret_username not in stderr.getvalue()


def test_shim_rejects_global_options_after_get_command() -> None:
    """Pip/keyring global options are accepted only before the operation."""
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        ["get", PYTHON_FEED, "--output=json"],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_CONFIGURATION_ERROR
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == "error: keyring shim get syntax is invalid.\n"


def test_shim_documented_password_json_output_omits_username(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """JSON password mode mirrors keyring's anonymous credential payload."""
    password = 'json-password"\\suffix'  # noqa: S105

    def fake_invoke_helper(
        service: str,
        username: str | None,
        *,
        mode: str,
    ) -> HelperCredential:
        assert (service, username, mode) == (
            PYTHON_FEED,
            "requested-user",
            "password",
        )
        return HelperCredential(username=None, password=password)

    monkeypatch.setattr(shim_module, "invoke_helper", fake_invoke_helper)
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        [
            "--output=json",
            "get",
            PYTHON_FEED,
            "requested-user",
        ],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_SUCCESS
    assert stdout.getvalue() == json.dumps({"password": password}) + "\n"
    assert stderr.getvalue() == ""


@pytest.mark.skipif(os.name == "nt", reason="POSIX executable-bit test helper")
@pytest.mark.parametrize(
    ("args", "expected_stdout"),
    [
        (["get", PYTHON_FEED, "requested-user"], f"{TEST_TOKEN}\n"),
        (
            ["--mode=creds", "--output=json", "get", PYTHON_FEED],
            json.dumps(
                {
                    "username": "AzureDevOps",
                    "password": TEST_TOKEN,
                }
            )
            + "\n",
        ),
    ],
)
def test_installed_shim_supports_legacy_and_pip_26_2_protocols(
    tmp_path: Path,
    args: list[str],
    expected_stdout: str,
) -> None:
    """The installed console script supports both subprocess protocols."""
    shim_executable = shutil.which("azureauth-keyring")
    assert shim_executable is not None
    helper = _write_helper(tmp_path)
    manifest = _write_manifest(tmp_path, helper)
    environment = os.environ.copy()
    environment[ENV_MANIFEST_PATH] = str(manifest)
    environment.pop("AZUREAUTH_TEST_MARKER", None)

    completed = subprocess.run(  # noqa: S603
        [shim_executable, *args],
        stdin=subprocess.DEVNULL,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
        env=environment,
    )

    assert completed.returncode == EXIT_SUCCESS
    assert completed.stdout == expected_stdout
    assert completed.stderr == ""


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
    helper: Path | str,
) -> Path:
    manifest = tmp_path / "backend-manifest.json"
    manifest.write_text(
        json.dumps(_manifest_data(helper), sort_keys=True),
        encoding="utf-8",
    )
    return manifest


def _manifest_data(helper: Path | str) -> dict[str, object]:
    return {
        "contractMajor": CONTRACT_MAJOR,
        "productId": PRODUCT_ID,
        "absoluteHelperPath": str(helper),
        "platform": _runtime_platform(),
    }


def _runtime_platform() -> str:
    if os.name == "nt":
        return PLATFORM_WINDOWS
    if os.uname().sysname == "Darwin":
        return PLATFORM_MACOS
    return PLATFORM_LINUX


def test_invoke_helper_executes_shared_cli_argv_with_inherited_environment(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Invoke the apphost without replacing its inherited environment."""
    contract = HelperContract(
        contract_major=CONTRACT_MAJOR,
        product_id=PRODUCT_ID,
        absolute_helper_path="/opt/azureauth-credprovider/azureauth-credprovider",
        platform=PLATFORM_LINUX,
    )
    validated_paths: list[str | Path | None] = []
    subprocess_calls: list[tuple[list[str], dict[str, object]]] = []

    def fake_load_and_validate_helper(
        manifest_path: str | Path | None = None,
    ) -> HelperContract:
        validated_paths.append(manifest_path)
        return contract

    def fake_run(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        subprocess_calls.append((args, kwargs))
        return subprocess.CompletedProcess(
            args,
            returncode=0,
            stdout=f"AzureDevOps\n{TEST_TOKEN}\n".encode(),
            stderr=b"",
        )

    monkeypatch.setattr(
        helper,
        "load_and_validate_helper",
        fake_load_and_validate_helper,
    )
    monkeypatch.setattr(helper.subprocess, "run", fake_run)

    credential = helper.invoke_helper(
        PYTHON_FEED,
        "user",
        mode="creds",
        manifest_path="/ignored/backend-manifest.json",
    )

    expected_argv = [
        "/opt/azureauth-credprovider/azureauth-credprovider",
        "python-keyring",
        "get",
        "--protocol-version",
        "2",
        "--service",
        PYTHON_FEED,
        "--username",
        "user",
        "--mode",
        "creds",
    ]
    assert validated_paths == ["/ignored/backend-manifest.json"]
    assert subprocess_calls == [
        (
            expected_argv,
            {
                "stdin": subprocess.DEVNULL,
                "capture_output": True,
                "check": False,
            },
        )
    ]
    assert "keyring-helper-v2" not in subprocess_calls[0][0]
    assert "shell" not in subprocess_calls[0][1]
    assert credential.username == "AzureDevOps"
    assert credential.password == TEST_TOKEN


def test_shim_redacts_generic_helper_launch_oserror(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Generic process launch failures become controlled redacted errors."""
    raw_error_detail = "sensitive-path-and-token"
    contract = HelperContract(
        contract_major=CONTRACT_MAJOR,
        product_id=PRODUCT_ID,
        absolute_helper_path="/opt/azureauth-credprovider/azureauth-credprovider",
        platform=PLATFORM_LINUX,
    )

    monkeypatch.setattr(
        helper,
        "load_and_validate_helper",
        lambda _manifest_path=None: contract,
    )

    def fail_launch(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[str]:
        del args, kwargs
        raise OSError(raw_error_detail)

    monkeypatch.setattr(helper.subprocess, "run", fail_launch)
    stdout = StringIO()
    stderr = StringIO()

    exit_code = run(
        ["get", PYTHON_FEED],
        stdout=stdout,
        stderr=stderr,
    )

    assert exit_code == EXIT_FATAL
    assert stdout.getvalue() == ""
    assert stderr.getvalue() == (
        "error: Keyring helper process could not be started.\n"
    )
    assert raw_error_detail not in stderr.getvalue()
