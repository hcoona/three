"""Regression tests for the distributable AzureAuth keyring package."""
# ruff: noqa: S101

from __future__ import annotations

import json
import os
import shutil
import stat
import subprocess
import sys
import traceback
import venv
from pathlib import Path
from types import ModuleType

import keyring.cli
import keyring.credentials
import pytest

from azureauth_credprovider_keyring import backend as backend_module
from azureauth_credprovider_keyring import helper
from azureauth_credprovider_keyring.contracts import (
    CONTRACT_MAJOR,
    EXIT_CONFIGURATION_ERROR,
    EXIT_FATAL,
    EXIT_NO_CREDENTIAL,
    MODE_CREDENTIALS,
    PLATFORM_LINUX,
    PRODUCT_ID,
    HelperContract,
    HelperCredential,
    HelperExecutionError,
    HelperProtocolError,
    NoCredentialError,
)
from azureauth_credprovider_keyring.integrity import ENV_MANIFEST_PATH

PACKAGE_ROOT = Path(__file__).resolve().parents[1]
WORKSPACE_ROOT = Path(__file__).resolve().parents[6]
BACKEND_SOURCE = PACKAGE_ROOT / "src/azureauth_credprovider_keyring/backend.py"
PYTHON_FEED = "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
TEST_USERNAME = "AzureDevOps"
TEST_PASSWORD = "regression-token"  # noqa: S105
INVALID_SUCCESS_MESSAGE = "Keyring helper stdout is not valid UTF-8."
INVALID_FAILURE_MESSAGE = "Keyring helper stderr is not valid UTF-8."
SECRET_SENTINEL = "must-not-leak-helper-secret"  # noqa: S105
VALID_FAILURE_EXIT = 73


@pytest.fixture(scope="session")
def built_wheel(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """Build the real wheel once for entry-point and binary tests."""
    output_directory = tmp_path_factory.mktemp("azureauth-wheel")
    completed = subprocess.run(  # noqa: S603
        [
            _required_executable("uv"),
            "build",
            "--package",
            "azureauth-credprovider-keyring",
            "--wheel",
            "--out-dir",
            str(output_directory),
        ],
        cwd=WORKSPACE_ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )

    assert completed.returncode == 0, completed.stderr
    wheel_paths = list(output_directory.glob("*.whl"))
    assert len(wheel_paths) == 1
    return wheel_paths[0]


@pytest.fixture(scope="session")
def installed_wheel_bin(
    tmp_path_factory: pytest.TempPathFactory,
    built_wheel: Path,
) -> Path:
    """Install only the built wheel and return its generated script folder."""
    environment = tmp_path_factory.mktemp("installed-wheel") / "venv"
    venv.EnvBuilder(with_pip=False).create(environment)
    python = _venv_executable(environment, "python")
    completed = subprocess.run(  # noqa: S603
        [
            _required_executable("uv"),
            "pip",
            "install",
            "--python",
            str(python),
            str(built_wheel),
        ],
        cwd=WORKSPACE_ROOT,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )

    assert completed.returncode == 0, completed.stderr
    return python.parent


def test_load_keyring_backend_falls_back_only_for_absent_top_level_keyring(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A genuinely absent optional keyring selects the local fallback."""
    monkeypatch.setattr(backend_module, "find_spec", lambda _name: None)
    monkeypatch.setattr(
        backend_module.importlib,
        "import_module",
        lambda _name: pytest.fail("absent keyring must not be imported"),
    )

    assert (
        backend_module._load_keyring_backend()  # noqa: SLF001
        is backend_module._FallbackKeyringBackend  # noqa: SLF001
    )


def test_load_keyring_backend_propagates_nested_import_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A broken installed keyring must not be mistaken for an absent one."""
    nested_error = ImportError(
        "keyring nested dependency failed",
        name="keyring_nested_dependency",
    )

    def import_broken_keyring(name: str) -> object:
        assert name == "keyring.backend"
        raise nested_error

    monkeypatch.setattr(
        backend_module.importlib,
        "import_module",
        import_broken_keyring,
    )

    with pytest.raises(ImportError) as raised:
        backend_module._load_keyring_backend()  # noqa: SLF001

    assert raised.value is nested_error
    assert raised.value.name == "keyring_nested_dependency"


def test_load_keyring_backend_propagates_nested_module_not_found_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A missing keyring dependency is not a missing top-level keyring."""
    nested_error = ModuleNotFoundError(
        "No module named 'keyring.missing_internal'",
        name="keyring.missing_internal",
    )

    def import_broken_keyring(name: str) -> object:
        assert name == "keyring.backend"
        raise nested_error

    monkeypatch.setattr(
        backend_module.importlib,
        "import_module",
        import_broken_keyring,
    )

    with pytest.raises(ModuleNotFoundError) as raised:
        backend_module._load_keyring_backend()  # noqa: SLF001

    assert raised.value is nested_error
    assert raised.value.name == "keyring.missing_internal"


def test_load_keyring_backend_rejects_broken_installed_module(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """An installed module without KeyringBackend must not use the fallback."""
    broken_backend_module = ModuleType("keyring.backend")

    def import_broken_module(name: str) -> ModuleType:
        assert name == "keyring.backend"
        return broken_backend_module

    monkeypatch.setattr(
        backend_module.importlib,
        "import_module",
        import_broken_module,
    )

    with pytest.raises(AttributeError, match="KeyringBackend"):
        backend_module._load_keyring_backend()  # noqa: SLF001


def test_backend_get_credential_returns_public_keyring_simple_credential(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Installed keyring receives its public concrete credential type."""
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        lambda service, username: _credential_for(service, username),
    )

    credential = backend_module.AzureAuthKeyringBackend().get_credential(
        PYTHON_FEED,
        None,
    )

    assert isinstance(credential, keyring.credentials.Credential)
    assert type(credential) is keyring.credentials.SimpleCredential
    assert type(credential).__module__ == "keyring.credentials"
    assert credential.username == TEST_USERNAME
    assert credential.password == TEST_PASSWORD


def test_backend_does_not_implement_private_keyring_vars_protocol() -> None:
    """Compatibility must not clone keyring's private formatter protocol."""
    source = BACKEND_SOURCE.read_text(encoding="utf-8")

    assert "_vars" not in source


@pytest.mark.parametrize(
    ("output_format", "expected_stdout"),
    [
        ("plain", f"{TEST_USERNAME}\n{TEST_PASSWORD}\n"),
        (
            "json",
            json.dumps(
                {
                    "username": TEST_USERNAME,
                    "password": TEST_PASSWORD,
                }
            )
            + "\n",
        ),
    ],
)
def test_keyring_cli_formats_backend_credential_via_public_command_path(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    output_format: str,
    expected_stdout: str,
) -> None:
    """The supported keyring CLI emits backend credentials in both formats."""
    monkeypatch.setattr(
        backend_module,
        "get_credentials",
        lambda service, username: _credential_for(service, username),
    )
    backend = backend_module.AzureAuthKeyringBackend()
    monkeypatch.setattr(
        keyring.cli,
        "get_credential",
        backend.get_credential,
    )

    keyring.cli.CommandLineTool().run(
        [
            "--mode=creds",
            f"--output={output_format}",
            "get",
            PYTHON_FEED,
        ]
    )

    captured = capsys.readouterr()
    assert captured.out == expected_stdout
    assert captured.err == ""


def test_built_wheel_keyring_backend_entry_point_loads(
    built_wheel: Path,
) -> None:
    """The real wheel exposes a loadable backend based on installed keyring."""
    script = """
import importlib.metadata
import json
import sys

wheel, site_packages = sys.argv[1:]
sys.path[:0] = [wheel, site_packages]
import keyring.backend
import keyring.credentials

distribution = importlib.metadata.distribution(
    "azureauth-credprovider-keyring"
)
entry_point = next(
    entry
    for entry in distribution.entry_points
    if entry.group == "keyring.backends" and entry.name == "azureauth"
)
backend_type = entry_point.load()
print(json.dumps({
    "entry_value": entry_point.value,
    "is_keyring_backend": issubclass(
        backend_type,
        keyring.backend.KeyringBackend,
    ),
    "credential_module": keyring.credentials.SimpleCredential.__module__,
}))
"""
    completed = _run_isolated_python(
        built_wheel,
        script,
        _site_packages(),
    )

    assert completed.returncode == 0, completed.stderr
    assert json.loads(completed.stdout) == {
        "entry_value": (
            "azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend"
        ),
        "is_keyring_backend": True,
        "credential_module": "keyring.credentials",
    }
    assert completed.stderr == ""


def test_built_wheel_uses_local_credential_in_isolated_no_keyring_runtime(
    built_wheel: Path,
) -> None:
    """The real wheel falls back only in a runtime with no keyring package."""
    script = """
import importlib.metadata
import importlib.util
import json
import sys

wheel = sys.argv[1]
sys.path.insert(0, wheel)
assert importlib.util.find_spec("keyring") is None
distribution = importlib.metadata.distribution(
    "azureauth-credprovider-keyring"
)
entry_point = next(
    entry
    for entry in distribution.entry_points
    if entry.group == "keyring.backends" and entry.name == "azureauth"
)
backend_type = entry_point.load()
backend_module = sys.modules[backend_type.__module__]
from azureauth_credprovider_keyring.contracts import HelperCredential
backend_module.get_credentials = lambda service, username: (
    HelperCredential("AzureDevOps", "regression-token")
)
credential = backend_type().get_credential(
    "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
    None,
)
print(json.dumps({
    "backend_base": backend_type.__bases__[0].__name__,
    "credential_type": type(credential).__name__,
    "credential_module": type(credential).__module__,
    "username": credential.username,
    "password": credential.password,
}))
"""
    completed = _run_isolated_python(built_wheel, script)

    assert completed.returncode == 0, completed.stderr
    assert json.loads(completed.stdout) == {
        "backend_base": "_FallbackKeyringBackend",
        "credential_type": "_SimpleCredential",
        "credential_module": "azureauth_credprovider_keyring.backend",
        "username": TEST_USERNAME,
        "password": TEST_PASSWORD,
    }
    assert completed.stderr == ""


@pytest.mark.parametrize(
    ("arguments", "expected_stdout"),
    [
        (
            ["get", PYTHON_FEED, "requested-user"],
            f"{TEST_PASSWORD}\n",
        ),
        (
            ["--mode=creds", "--output=json", "get", PYTHON_FEED],
            json.dumps(
                {
                    "username": TEST_USERNAME,
                    "password": TEST_PASSWORD,
                }
            )
            + "\n",
        ),
    ],
)
def test_built_wheel_console_script_invocation(
    tmp_path: Path,
    installed_wheel_bin: Path,
    arguments: list[str],
    expected_stdout: str,
) -> None:
    """The console binary generated from the real wheel supports both modes."""
    helper_path = _write_binary_helper(tmp_path)
    manifest_path = _write_manifest(tmp_path, helper_path)
    environment = os.environ.copy()
    environment[ENV_MANIFEST_PATH] = str(manifest_path)
    executable = _venv_executable(
        installed_wheel_bin.parent,
        "azureauth-keyring",
    )

    completed = subprocess.run(  # noqa: S603
        [str(executable), *arguments],
        stdin=subprocess.DEVNULL,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
        env=environment,
    )

    assert completed.returncode == 0
    assert completed.stdout == expected_stdout
    assert completed.stderr == ""


def test_invoke_helper_captures_bytes_and_strictly_decodes_utf8(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Helper streams stay bytes until explicit strict UTF-8 decoding."""
    subprocess_calls: list[dict[str, object]] = []
    _patch_contract(monkeypatch)

    def run_bytes(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        subprocess_calls.append(kwargs)
        return subprocess.CompletedProcess(
            args,
            returncode=0,
            stdout="Azuré\npässword\n".encode(),
            stderr=b"",
        )

    monkeypatch.setattr(helper.subprocess, "run", run_bytes)

    credential = helper.invoke_helper(
        PYTHON_FEED,
        None,
        mode=MODE_CREDENTIALS,
    )

    assert subprocess_calls == [
        {
            "stdin": subprocess.DEVNULL,
            "capture_output": True,
            "check": False,
        }
    ]
    assert credential == HelperCredential("Azuré", "pässword")


def test_invalid_success_stdout_raises_redacted_helper_protocol_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Invalid success bytes become one fixed, traceback-safe error."""
    invalid_stdout = b"\xff" + SECRET_SENTINEL.encode()
    _patch_completed_process(
        monkeypatch,
        returncode=0,
        stdout=invalid_stdout,
        stderr=b"",
    )

    with pytest.raises(HelperProtocolError) as raised:
        helper.invoke_helper(PYTHON_FEED, None, mode=MODE_CREDENTIALS)

    _assert_redacted_protocol_error(
        raised.value,
        INVALID_SUCCESS_MESSAGE,
        invalid_stdout,
    )


def test_invalid_failure_stderr_raises_redacted_helper_protocol_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Invalid failure bytes and embedded secrets never become diagnostics."""
    invalid_stderr = b"\xff" + SECRET_SENTINEL.encode()
    _patch_completed_process(
        monkeypatch,
        returncode=EXIT_FATAL,
        stdout=b"",
        stderr=invalid_stderr,
    )

    with pytest.raises(HelperProtocolError) as raised:
        helper.invoke_helper(PYTHON_FEED, None, mode=MODE_CREDENTIALS)

    _assert_redacted_protocol_error(
        raised.value,
        INVALID_FAILURE_MESSAGE,
        invalid_stderr,
    )


def test_invoke_helper_preserves_valid_failure_stderr_and_exit_code(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """A valid helper failure keeps its documented exit code and message."""
    _patch_completed_process(
        monkeypatch,
        returncode=VALID_FAILURE_EXIT,
        stdout=b"ignored failure stdout",
        stderr=b"controlled helper failure\n",
    )

    with pytest.raises(HelperExecutionError) as raised:
        helper.invoke_helper(PYTHON_FEED, None, mode=MODE_CREDENTIALS)

    assert raised.value.exit_code == VALID_FAILURE_EXIT
    assert raised.value.safe_message == "controlled helper failure"
    assert str(raised.value) == "controlled helper failure"


def test_invoke_helper_preserves_no_credential_exit_behavior(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """No-credential remains exit 1 and does not inspect process streams."""
    _patch_completed_process(
        monkeypatch,
        returncode=EXIT_NO_CREDENTIAL,
        stdout=b"\xffignored",
        stderr=b"\xffignored",
    )

    with pytest.raises(NoCredentialError) as raised:
        helper.invoke_helper(PYTHON_FEED, None, mode=MODE_CREDENTIALS)

    assert raised.value.exit_code == EXIT_NO_CREDENTIAL
    assert raised.value.safe_message == ""
    assert str(raised.value) == ""


def _credential_for(service: str, username: str | None) -> HelperCredential:
    assert (service, username) == (PYTHON_FEED, None)
    return HelperCredential(TEST_USERNAME, TEST_PASSWORD)


def _required_executable(name: str) -> str:
    executable = shutil.which(name)
    assert executable is not None
    return executable


def _venv_executable(environment: Path, name: str) -> Path:
    suffix = ".exe" if os.name == "nt" else ""
    script_directory = "Scripts" if os.name == "nt" else "bin"
    return environment / script_directory / f"{name}{suffix}"


def _site_packages() -> str:
    candidates = [
        path
        for path in sys.path
        if path.endswith(("site-packages", "dist-packages"))
    ]
    assert len(candidates) == 1
    return candidates[0]


def _run_isolated_python(
    built_wheel: Path,
    script: str,
    *arguments: str,
) -> subprocess.CompletedProcess[str]:
    environment = built_wheel.parent / "isolated-venv"
    if not environment.exists():
        venv.EnvBuilder(with_pip=False).create(environment)
    python = _venv_executable(environment, "python")
    return subprocess.run(  # noqa: S603
        [str(python), "-I", "-c", script, str(built_wheel), *arguments],
        stdin=subprocess.DEVNULL,
        capture_output=True,
        check=False,
        encoding="utf-8",
        text=True,
    )


def _write_binary_helper(tmp_path: Path) -> Path:
    helper_path = tmp_path / "azureauth-credprovider"
    helper_path.write_text(
        "#!/usr/bin/env python3\n"
        "import sys\n"
        f"username = {TEST_USERNAME!r}\n"
        f"password = {TEST_PASSWORD!r}\n"
        "if sys.argv[-1] == 'creds':\n"
        "    sys.stdout.write(f'{username}\\n{password}\\n')\n"
        "elif sys.argv[-1] == 'password':\n"
        "    sys.stdout.write(f'{password}\\n')\n"
        "else:\n"
        "    raise SystemExit(64)\n",
        encoding="utf-8",
    )
    helper_path.chmod(
        stat.S_IRUSR
        | stat.S_IWUSR
        | stat.S_IXUSR
        | stat.S_IRGRP
        | stat.S_IXGRP,
    )
    return helper_path


def _write_manifest(tmp_path: Path, helper_path: Path) -> Path:
    manifest_path = tmp_path / "backend-manifest.json"
    manifest_path.write_text(
        json.dumps(
            {
                "contractMajor": CONTRACT_MAJOR,
                "productId": PRODUCT_ID,
                "absoluteHelperPath": str(helper_path),
                "platform": PLATFORM_LINUX,
            }
        ),
        encoding="utf-8",
    )
    return manifest_path


def _contract() -> HelperContract:
    return HelperContract(
        contract_major=CONTRACT_MAJOR,
        product_id=PRODUCT_ID,
        absolute_helper_path="/opt/azureauth-credprovider",
        platform=PLATFORM_LINUX,
    )


def _patch_contract(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        helper,
        "load_and_validate_helper",
        lambda _manifest_path=None: _contract(),
    )


def _patch_completed_process(
    monkeypatch: pytest.MonkeyPatch,
    *,
    returncode: int,
    stdout: bytes,
    stderr: bytes,
) -> None:
    _patch_contract(monkeypatch)

    def run_bytes(
        args: list[str],
        **kwargs: object,
    ) -> subprocess.CompletedProcess[bytes]:
        del kwargs
        return subprocess.CompletedProcess(
            args,
            returncode=returncode,
            stdout=stdout,
            stderr=stderr,
        )

    monkeypatch.setattr(helper.subprocess, "run", run_bytes)


def _assert_redacted_protocol_error(
    error: HelperProtocolError,
    expected_message: str,
    raw_stream: bytes,
) -> None:
    rendered = "".join(
        traceback.format_exception(type(error), error, error.__traceback__)
    )

    assert error.exit_code == EXIT_CONFIGURATION_ERROR
    assert error.safe_message == expected_message
    assert str(error) == expected_message
    assert error.args == (expected_message,)
    assert SECRET_SENTINEL not in rendered
    assert repr(raw_stream) not in rendered
    assert "UnicodeDecodeError" not in rendered
    assert "\\xff" not in rendered
