from __future__ import annotations

import importlib.metadata
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest import mock


SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
SCRIPT_PATH = SCRIPTS / "check_runtime.py"
SPEC = importlib.util.spec_from_file_location("check_runtime", SCRIPT_PATH)
assert SPEC and SPEC.loader
check_runtime = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(check_runtime)


class FakeVersion:
    def __init__(self, major: int, minor: int, micro: int) -> None:
        self.major = major
        self.minor = minor
        self.micro = micro

    def __getitem__(self, key: object) -> object:
        return (self.major, self.minor, self.micro)[key]


class FakeDistribution:
    def __init__(self, version: str, origin: Path) -> None:
        self.version = version
        self.origin = origin

    def locate_file(self, unused: str) -> Path:
        return self.origin


class RuntimeReadinessTests(unittest.TestCase):
    def readiness(
        self,
        python: tuple[int, int, int] = (3, 12, 13),
        overrides: dict[str, str] | None = None,
        runtime: Path | None = None,
    ) -> dict[str, object]:
        runtime = runtime or SCRIPTS / ".runtime-test"
        versions = {
            "imagecodecs": "2026.6.26",
            "numpy": "2.2.6",
            "opencv-python-headless": "4.12.0.88",
            "Pillow": "12.3.0",
            "tifffile": "2026.7.31",
            **(overrides or {}),
        }
        imported = {
            "imagecodecs": "2026.6.26",
            "numpy": "2.2.6",
            "cv2": "4.12.0",
            "PIL": "12.3.0",
            "tifffile": "2026.7.31",
        }

        def distribution(name: str) -> FakeDistribution:
            if name not in versions:
                raise importlib.metadata.PackageNotFoundError(name)
            return FakeDistribution(versions[name], runtime / "Lib" / "site-packages")

        def import_module(name: str) -> mock.Mock:
            return mock.Mock(
                __version__=imported[name],
                __file__=str(runtime / "Lib" / "site-packages" / name / "__init__.py"),
            )

        with (
            mock.patch.object(check_runtime.sys, "version_info", FakeVersion(*python)),
            mock.patch.object(check_runtime.sys, "prefix", str(runtime)),
            mock.patch.object(
                check_runtime.sys,
                "executable",
                str(runtime / "Scripts" / "python.exe"),
            ),
            mock.patch.object(
                check_runtime.importlib.metadata,
                "distribution",
                side_effect=distribution,
            ),
            mock.patch.object(
                check_runtime.importlib,
                "import_module",
                side_effect=import_module,
            ),
            mock.patch.object(check_runtime.sys, "flags", mock.Mock(isolated=1)),
            mock.patch.object(check_runtime.site, "ENABLE_USER_SITE", False),
        ):
            return check_runtime.readiness(runtime)

    def test_exact_runtime_and_origins_are_ready(self) -> None:
        result = self.readiness()
        self.assertTrue(result["ready"])
        self.assertTrue(result["runtime_root_ok"])
        self.assertTrue(result["dependencies_ok"])
        self.assertTrue(result["imports_ok"])

    def test_linked_scripts_directory_is_canonicalized(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            scripts_alias = Path(directory) / "scripts-link"
            try:
                scripts_alias.symlink_to(SCRIPTS, target_is_directory=True)
            except OSError as error:
                self.skipTest(f"directory links are unavailable: {error}")
            runtime = scripts_alias / ".runtime-test"

            self.assertNotEqual(runtime.parent, SCRIPTS)
            self.assertEqual(runtime.parent.resolve(), SCRIPTS)
            result = self.readiness(runtime=runtime)

        self.assertTrue(result["ready"])
        self.assertTrue(result["runtime_root_ok"])

    def test_wrong_python_or_dependency_fails_closed(self) -> None:
        self.assertFalse(self.readiness((3, 12, 12))["ready"])
        result = self.readiness(overrides={"Pillow": "12.2.0"})
        self.assertFalse(result["ready"])
        self.assertFalse(result["dependencies_ok"])

    def test_conflicting_opencv_distribution_fails_closed(self) -> None:
        result = self.readiness(overrides={"opencv-python": "4.12.0.88"})
        self.assertFalse(result["ready"])
        self.assertEqual(
            result["conflicting_opencv_distributions"],
            ["opencv-python"],
        )

    def test_runner_matches_practical_security_boundary(self) -> None:
        runner = (SCRIPTS / "run.ps1").read_text(encoding="utf-8")
        requirements = (SCRIPTS / "requirements.lock").read_text(encoding="utf-8")

        self.assertIn('[ValidateSet("analyze_scans.py", "run_tests.py", "check_runtime.py")]', runner)
        self.assertIn('$pythonVersion = "3.12.13"', runner)
        self.assertIn('$pythonSpec = "python@$pythonVersion"', runner)
        self.assertIn("--no-config", runner)
        self.assertIn('$env:MISE_NO_CONFIG = "1"', runner)
        self.assertIn('$env:MISE_CONFIG_DIR =', runner)
        self.assertIn('Save-And-ClearEnvironment { $_.Name -like "MISE_*" }', runner)
        self.assertIn('Save-And-ClearEnvironment { $_.Name -like "PIP_*" }', runner)
        self.assertIn('Save-And-ClearEnvironment { $_.Name -like "AZUREAUTH_*" }', runner)
        for name in (
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "REQUESTS_CA_BUNDLE",
            "SSL_CERT_FILE",
            "PIP_CERT",
            "PIP_CLIENT_CERT",
            "SYSTEM_ACCESSTOKEN",
        ):
            self.assertIn(f'"{name}"', runner)
        self.assertIn('$env:PIP_CONFIG_FILE = "nul"', runner)
        self.assertIn("$env:PIP_INDEX_URL =", runner)
        self.assertIn("Remove-Item Env:PIP_INDEX_URL", runner)
        self.assertIn("$azureAuthExe ado token --output token", runner)
        self.assertNotIn("--index-url", runner)
        self.assertIn("--require-hashes", runner)
        self.assertIn("--no-deps", runner)
        self.assertIn("--only-binary=:all:", runner)
        self.assertIn("Remove-Item -LiteralPath $runtime -Recurse", runner)
        self.assertIn("Remove-Item -LiteralPath $miseSession -Recurse", runner)

        self.assertEqual(requirements.count("--hash=sha256:"), 5)
        for version in (
            "imagecodecs==2026.6.26",
            "numpy==2.2.6",
            "opencv-python-headless==4.12.0.88",
            "Pillow==12.3.0",
            "tifffile==2026.7.31",
        ):
            self.assertIn(version, requirements)

        for removed in (
            "startup_launcher.exe",
            "startup_launcher.c",
            "run.cmd",
            "python-runtime-manifest.json",
            "azureauth-0.9.5.manifest.json",
        ):
            self.assertFalse((SCRIPTS / removed).exists(), removed)
        for stale_model in (
            "Get-AuthenticodeSignature",
            "FileSystemAccessRule",
            "FileShare]::Read",
            "Open-PinnedFile",
            "Assert-OrdinaryPathChain",
            "Assert-PinnedPythonDistribution",
        ):
            self.assertNotIn(stale_model, runner)

    def test_documentation_uses_no_profile_run_ps1_entrypoint(self) -> None:
        documentation = (SCRIPTS.parent / "SKILL.md").read_text(encoding="utf-8")
        self.assertIn("powershell.exe -NoProfile", documentation)
        self.assertIn(r"scripts\run.ps1", documentation)
        self.assertNotIn("startup_launcher", documentation)
        self.assertNotIn("run.cmd", documentation)
        self.assertNotIn("Authenticode", documentation)
        self.assertNotIn("ACL", documentation)


if __name__ == "__main__":
    unittest.main()
