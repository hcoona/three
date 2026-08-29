# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "jsonschema==4.25.1",
# ]
# ///

from __future__ import annotations

import importlib.util
import json
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


if __name__ == "__main__":
    unittest.main()
