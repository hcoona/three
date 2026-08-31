# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "jsonschema==4.25.1",
# ]
# ///

from __future__ import annotations

import hashlib
import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any
from unittest import mock

PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
VALIDATOR_PATH = PUBLICATION_ROOT / "scripts" / "validate_package.py"
sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location(
    "scholarly_validate_package_under_test",
    VALIDATOR_PATH,
)
assert spec is not None and spec.loader is not None
validate_package: Any = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validate_package)


def schema_fixture() -> dict[str, Any]:
    """Return the smallest schema satisfying package-specific fields."""
    return {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "https://example.invalid/scholarly-test.schema.json",
        "title": "Scholarly validator test schema",
        "type": "object",
        "properties": {"schema_version": {"const": "1.0"}},
    }


def write_pep723_script(root: Path, metadata: str) -> Path:
    """Write one executable-script fixture with the supplied metadata."""
    commented_metadata = "\n".join(
        "#" if not line else f"# {line}" for line in metadata.splitlines()
    )
    path = root / "runtime.py"
    path.write_text(
        f"# /// script\n{commented_metadata}\n# ///\nprint('fixture')\n",
        encoding="utf-8",
    )
    return path


def make_runtime_fixture(
    root: Path,
) -> tuple[Path, Path, tuple[str, ...]]:
    """Create a minimal valid canonical and deployed runtime fixture."""
    repository_root = root / "repository"
    package_root = repository_root / "src/private/lib/scholarly-publication"
    includes = tuple(
        f"skills/{skill_name}/SKILL.md"
        for skill_name in validate_package.EXPECTED_SKILLS
    )
    bindings: list[str] = []
    for include in includes:
        content = f"{include}\n".encode()
        canonical = package_root / include
        deployed = repository_root / ".agents" / include
        canonical.parent.mkdir(parents=True, exist_ok=True)
        deployed.parent.mkdir(parents=True, exist_ok=True)
        canonical.write_bytes(content)
        deployed.write_bytes(content)
        relative = deployed.relative_to(repository_root).as_posix()
        digest = hashlib.sha256(content).hexdigest()
        bindings.append(f"    {relative}: sha256:{digest}")

    (repository_root / "apm.yml").write_text(
        "dependencies:\n"
        "  apm:\n"
        "    - path: ./src/private/lib/scholarly-publication\n",
        encoding="utf-8",
    )
    (repository_root / "apm.lock.yaml").write_text(
        "lockfile_version: '1'\n"
        "dependencies:\n"
        "- repo_url: _local/scholarly-publication\n"
        "  name: scholarly-publication\n"
        "  version: 0.1.0\n"
        "  deployed_file_hashes:\n" + "\n".join(bindings) + "\n"
        "  source: local\n"
        "  local_path: ./src/private/lib/scholarly-publication\n",
        encoding="utf-8",
    )
    return repository_root, package_root, includes


class JsonSchemaValidationTests(unittest.TestCase):
    def test_required_keyword_rejects_scalar_shape(self) -> None:
        schema = schema_fixture()
        schema["required"] = "schema_version"

        with tempfile.TemporaryDirectory() as temporary_directory:
            package_root = Path(temporary_directory)
            path = package_root / "malformed.schema.json"
            path.write_text(json.dumps(schema), encoding="utf-8")
            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_json_and_python()

        self.assertEqual(
            str(raised.exception),
            f"{path} is not a valid Draft 2020-12 schema: "
            "'schema_version' is not of type 'array'",
        )


class Pep723ValidationTests(unittest.TestCase):
    def test_malformed_toml_is_rejected(self) -> None:
        metadata = """\
requires-python = "==3.12.11"
dependencies = [
  "jsonschema==4.25.1",
"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = write_pep723_script(Path(temporary_directory), metadata)

            with self.assertRaises(  # noqa: PT027
                validate_package.ValidationError
            ) as raised:
                validate_package.validate_pep723(path)

        self.assertIn("has invalid PEP 723 TOML:", str(raised.exception))

    def test_requires_python_must_be_present(self) -> None:
        metadata = """\
dependencies = [
  "jsonschema==4.25.1",
]
"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = write_pep723_script(Path(temporary_directory), metadata)

            with self.assertRaises(  # noqa: PT027
                validate_package.ValidationError
            ) as raised:
                validate_package.validate_pep723(path)

        self.assertEqual(
            str(raised.exception),
            f"{path} PEP 723 requires-python must be a non-empty string",
        )

    def test_dependencies_must_be_a_list(self) -> None:
        metadata = """\
requires-python = "==3.12.11"
dependencies = "jsonschema==4.25.1"
"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = write_pep723_script(Path(temporary_directory), metadata)

            with self.assertRaises(  # noqa: PT027
                validate_package.ValidationError
            ) as raised:
                validate_package.validate_pep723(path)

        self.assertEqual(
            str(raised.exception),
            f"{path} PEP 723 dependencies must be a list",
        )

    def test_dependencies_must_use_exact_pins(self) -> None:
        metadata = """\
requires-python = "==3.12.11"
dependencies = [
  "jsonschema>=4.25.1",
]
"""
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = write_pep723_script(Path(temporary_directory), metadata)

            with self.assertRaises(  # noqa: PT027
                validate_package.ValidationError
            ) as raised:
                validate_package.validate_pep723(path)

        self.assertEqual(
            str(raised.exception),
            f"{path} PEP 723 dependency must use an exact name==version pin: "
            "'jsonschema>=4.25.1'",
        )


class RuntimeDeploymentValidationTests(unittest.TestCase):
    def test_yaml_inline_comment_parser_preserves_scalar_hashes(self) -> None:
        cases = {
            "./package#fragment": "./package#fragment",
            "'./package # literal' # comment": "'./package # literal'",
            '"./package # literal" # comment': '"./package # literal"',
        }
        for value, expected in cases.items():
            with self.subTest(value=value):
                self.assertEqual(
                    validate_package.strip_yaml_inline_comment(value),
                    expected,
                )

    def test_root_apm_manifest_must_register_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            (repository_root / "apm.yml").write_text(
                "dependencies:\n  apm: []\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertEqual(
            str(raised.exception),
            "root apm.yml must register the scholarly publication package "
            "exactly once: ./src/private/lib/scholarly-publication",
        )

    def test_root_apm_manifest_rejects_shadowed_dependencies(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            root_apm = repository_root / "apm.yml"
            root_apm.write_text(
                root_apm.read_text(encoding="utf-8")
                + "dependencies:\n  apm: []\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertEqual(
            str(raised.exception),
            f"{repository_root / 'apm.yml'} contains duplicate "
            "dependencies mappings",
        )

    def test_root_apm_manifest_accepts_path_first_remote_dependency(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            (repository_root / "apm.yml").write_text(
                "dependencies:\n"
                "  apm:\n"
                "    - path: plugins/dotnet\n"
                "      git: https://github.com/dotnet/skills.git\n"
                "      ref: main\n"
                "    - path: ./src/private/lib/scholarly-publication\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
            ):
                deployed = validate_package.validate_runtime_deployment(
                    includes
                )

        self.assertEqual(len(deployed), len(includes))

    def test_root_apm_manifest_accepts_inline_comments(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            (repository_root / "apm.yml").write_text(
                "dependencies: # package registries\n"
                "  apm: # agent packages\n"
                "    - path: ./src/private/lib/scholarly-publication "
                "# local package\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
            ):
                deployed = validate_package.validate_runtime_deployment(
                    includes
                )

        self.assertEqual(len(deployed), len(includes))

    def test_root_apm_manifest_rejects_unpaired_path_quote(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            root_apm = repository_root / "apm.yml"
            root_apm.write_text(
                "dependencies:\n"
                "  apm:\n"
                "    - path: "
                "'./src/private/lib/scholarly-publication\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertEqual(
            str(raised.exception),
            f"{root_apm} has unsupported local dependency path syntax",
        )

    def test_runtime_deployment_requires_root_lock(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            (repository_root / "apm.lock.yaml").unlink()

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertEqual(
            str(raised.exception),
            "runtime deployment requires repository apm.lock.yaml",
        )

    def test_runtime_deployment_rejects_commented_lock_bindings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            lock_path = repository_root / "apm.lock.yaml"
            lock_path.write_text(
                "\n".join(
                    f"# {line}" if line.startswith("    .agents/") else line
                    for line in lock_path.read_text(
                        encoding="utf-8"
                    ).splitlines()
                )
                + "\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertIn(
            "apm.lock.yaml omits deployed runtime files:",
            str(raised.exception),
        )

    def test_runtime_deployment_scopes_lock_bindings_to_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            lock_path = repository_root / "apm.lock.yaml"
            bindings = [
                line
                for line in lock_path.read_text(encoding="utf-8").splitlines()
                if line.startswith("    .agents/")
            ]
            lock_path.write_text(
                "lockfile_version: '1'\n"
                "dependencies:\n"
                "- repo_url: _local/other-package\n"
                "  deployed_file_hashes:\n" + "\n".join(bindings) + "\n"
                "  source: local\n"
                "  local_path: ./src/private/lib/other-package\n"
                "- repo_url: _local/scholarly-publication\n"
                "  deployed_file_hashes:\n"
                "  source: local\n"
                "  local_path: "
                "./src/private/lib/scholarly-publication\n",
                encoding="utf-8",
            )

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertIn(
            "apm.lock.yaml omits deployed runtime files:",
            str(raised.exception),
        )

    def test_runtime_deployment_rejects_skill_root_symlink(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            repository_root, package_root, includes = make_runtime_fixture(
                Path(temporary_directory)
            )
            skill_name = validate_package.EXPECTED_SKILLS[0]
            skill_root = repository_root / ".agents" / "skills" / skill_name
            shutil.rmtree(skill_root)
            try:
                skill_root.symlink_to(
                    package_root / "skills" / skill_name,
                    target_is_directory=True,
                )
            except (NotImplementedError, OSError) as error:
                self.skipTest(f"directory symlinks are unavailable: {error}")

            with (
                mock.patch.object(
                    validate_package,
                    "PACKAGE_ROOT",
                    package_root,
                ),
                mock.patch.object(
                    validate_package,
                    "find_repository_root",
                    return_value=repository_root,
                ),
                self.assertRaises(  # noqa: PT027
                    validate_package.ValidationError
                ) as raised,
            ):
                validate_package.validate_runtime_deployment(includes)

        self.assertEqual(
            str(raised.exception),
            "runtime deployment must not contain symlinks: "
            f".agents/skills/{skill_name}",
        )


if __name__ == "__main__":
    unittest.main()
