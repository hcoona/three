# /// script
# requires-python = "==3.12.11"
# dependencies = [
#   "jsonschema==4.25.1",
#   "PyYAML==6.0.2",
# ]
# ///
# ruff: noqa: PT027, SIM117

from __future__ import annotations

import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import yaml

PUBLICATION_ROOT = Path(__file__).resolve().parents[1]
VALIDATOR_PATH = PUBLICATION_ROOT / "scripts" / "validate_package.py"
sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location(
    "scholarly_validate_package_under_test",
    VALIDATOR_PATH,
)
assert spec is not None and spec.loader is not None
validate_package = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = validate_package
spec.loader.exec_module(validate_package)


def _copy_package(root: Path) -> Path:
    package_root = root / "src" / "private" / "lib" / "scholarly-publication"
    shutil.copytree(
        PUBLICATION_ROOT,
        package_root,
        ignore=shutil.ignore_patterns("__pycache__", "*.pyc"),
    )
    return package_root


def _load_yaml(path: Path) -> dict[str, object]:
    value = yaml.safe_load(path.read_text(encoding="utf-8"))
    assert isinstance(value, dict)
    return value


def _write_yaml(path: Path, value: dict[str, object]) -> None:
    path.write_text(
        yaml.safe_dump(value, sort_keys=False),
        encoding="utf-8",
    )


def _validate_canonical(package_root: Path) -> set[str]:
    with mock.patch.object(validate_package, "PACKAGE_ROOT", package_root):
        package = validate_package.load_package()
        return validate_package.validate_canonical(package)


def _validate_runtime(repository_root: Path, package_root: Path) -> set[str]:
    with (
        mock.patch.object(validate_package, "PACKAGE_ROOT", package_root),
        mock.patch.object(
            validate_package,
            "find_repository_root",
            return_value=repository_root,
        ),
    ):
        package = validate_package.load_package()
        return validate_package.validate_runtime(package)


def _deployment_record(
    value: str,
    local_path: str,
    content_hash: str | None,
) -> dict[str, object]:
    return {
        "kind": "project-relative",
        "target": "agents",
        "value": value,
        "runtime": None,
        "scope": "project",
        "owners": [local_path],
        "active_owner": local_path,
        "content_hash": content_hash,
    }


def _make_runtime_fixture(root: Path) -> tuple[Path, Path]:
    repository_root = root / "repository"
    package_root = _copy_package(repository_root)
    local_path = "./src/private/lib/scholarly-publication"
    with mock.patch.object(validate_package, "PACKAGE_ROOT", package_root):
        package = validate_package.load_package()
        validate_package.validate_canonical(package)

    deployed_files = [
        f".agents/skills/{skill}" for skill in validate_package.EXPECTED_SKILLS
    ]
    hashes: dict[str, str] = {}
    deployments = [
        _deployment_record(value, local_path, None) for value in deployed_files
    ]
    for include in package.includes:
        canonical = package_root.joinpath(*Path(include).parts)
        deployed_relative = f".agents/{include}"
        deployed = repository_root.joinpath(*Path(deployed_relative).parts)
        deployed.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(canonical, deployed)
        digest = f"sha256:{hashlib.sha256(deployed.read_bytes()).hexdigest()}"
        deployed_files.append(deployed_relative)
        hashes[deployed_relative] = digest
        deployments.append(
            _deployment_record(deployed_relative, local_path, digest)
        )

    _write_yaml(
        repository_root / "apm.yml",
        {"dependencies": {"apm": [{"path": local_path}]}},
    )
    _write_yaml(
        repository_root / "apm.lock.yaml",
        {
            "lockfile_version": "1",
            "dependencies": [
                {
                    "repo_url": "_local/scholarly-publication",
                    "name": package.name,
                    "version": package.version,
                    "package_type": "marketplace_plugin",
                    "deployed_files": deployed_files,
                    "deployed_file_hashes": hashes,
                    "source": "local",
                    "local_path": local_path,
                    "declared_license": package.license,
                }
            ],
            "deployments": deployments,
        },
    )
    return repository_root, package_root


def _dependency(lock: dict[str, object]) -> dict[str, object]:
    dependencies = lock["dependencies"]
    assert isinstance(dependencies, list)
    for value in dependencies:
        if isinstance(value, dict) and value.get("name") == (
            "scholarly-publication"
        ):
            return value
    message = "fixture scholarly dependency is missing"
    raise AssertionError(message)


class CanonicalValidationTests(unittest.TestCase):
    def test_actual_canonical_package_passes_with_stable_report(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = validate_package.main(["--scope", "canonical"])

        self.assertEqual(result, 0)
        self.assertEqual(
            json.loads(output.getvalue()),
            {
                "canonical_runtime_files": 29,
                "deployed_runtime_files": 0,
                "plugin": "scholarly-publication",
                "runtime_includes": 29,
                "scope": "canonical",
                "skills": list(validate_package.EXPECTED_SKILLS),
                "status": "pass",
                "version": "0.1.0",
            },
        )

    def test_descriptor_disagreement_and_duplicate_yaml_are_rejected(
        self,
    ) -> None:
        with self.subTest("description disagreement"):
            with tempfile.TemporaryDirectory() as temporary_directory:
                package_root = _copy_package(Path(temporary_directory))
                apm_path = package_root / "apm.yml"
                apm = _load_yaml(apm_path)
                apm["description"] = "A different description."
                _write_yaml(apm_path, apm)
                with self.assertRaisesRegex(
                    validate_package.ValidationError,
                    "disagree on description",
                ):
                    _validate_canonical(package_root)

        with self.subTest("duplicate YAML key"):
            with tempfile.TemporaryDirectory() as temporary_directory:
                package_root = _copy_package(Path(temporary_directory))
                apm_path = package_root / "apm.yml"
                text = apm_path.read_text(encoding="utf-8")
                apm_path.write_text(
                    text.replace(
                        "version: 0.1.0",
                        "name: duplicate\nversion: 0.1.0",
                        1,
                    ),
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(
                    validate_package.ValidationError,
                    "duplicate YAML mapping key",
                ):
                    _validate_canonical(package_root)

    def test_skill_and_include_closure_is_exact(self) -> None:
        for scenario in (
            "missing include",
            "unlisted asset",
            "extra skill",
            "included tests",
        ):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    package_root = _copy_package(Path(temporary_directory))
                    apm_path = package_root / "apm.yml"
                    apm = _load_yaml(apm_path)
                    includes = apm["includes"]
                    assert isinstance(includes, list)
                    skill = validate_package.EXPECTED_SKILLS[0]
                    if scenario == "missing include":
                        includes.pop()
                    elif scenario == "unlisted asset":
                        asset = (
                            package_root
                            / "skills"
                            / skill
                            / "assets"
                            / "new.txt"
                        )
                        asset.write_text("new", encoding="utf-8")
                    elif scenario == "extra skill":
                        extra = package_root / "skills" / "extra-skill"
                        extra.mkdir()
                        (extra / "SKILL.md").write_text(
                            "extra",
                            encoding="utf-8",
                        )
                    else:
                        test_path = (
                            package_root
                            / "skills"
                            / skill
                            / "tests"
                            / "fixture.json"
                        )
                        test_path.parent.mkdir()
                        test_path.write_text("{}", encoding="utf-8")
                        includes.append(f"skills/{skill}/tests/fixture.json")
                    _write_yaml(apm_path, apm)
                    with self.assertRaises(validate_package.ValidationError):
                        _validate_canonical(package_root)

    def test_duplicate_and_non_finite_shipped_json_are_rejected(self) -> None:
        for scenario in (
            "duplicate plugin key",
            "duplicate included key",
            "NaN",
            "Infinity",
            "-Infinity",
            "1e999",
        ):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    package_root = _copy_package(Path(temporary_directory))
                    if scenario == "duplicate plugin key":
                        path = package_root / "plugin.json"
                        text = path.read_text(encoding="utf-8")
                        path.write_text(
                            text.replace(
                                '"name": "scholarly-publication",',
                                '"name": "scholarly-publication",\n'
                                '  "name": "duplicate",',
                                1,
                            ),
                            encoding="utf-8",
                        )
                        expected = "duplicate JSON object key"
                    else:
                        path = (
                            package_root
                            / "skills"
                            / "scholarly-print-assembly"
                            / "assets"
                            / "publication-profile.json"
                        )
                        if scenario == "duplicate included key":
                            path.write_text(
                                '{"value": 1, "value": 2}',
                                encoding="utf-8",
                            )
                            expected = "duplicate JSON object key"
                        else:
                            path.write_text(
                                f'{{"value": {scenario}}}',
                                encoding="utf-8",
                            )
                            expected = "non-finite JSON number"
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        expected,
                    ):
                        _validate_canonical(package_root)

    def test_draft_2020_12_schema_meta_validation(self) -> None:
        for value in (True, False):
            with (
                self.subTest(value=value),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                package_root = _copy_package(Path(temporary_directory))
                schema = (
                    package_root
                    / "skills"
                    / "scholarly-pdf-reconstruction"
                    / "assets"
                    / "section-map.schema.json"
                )
                schema.write_text(json.dumps(value), encoding="utf-8")
                _validate_canonical(package_root)

        with tempfile.TemporaryDirectory() as temporary_directory:
            package_root = _copy_package(Path(temporary_directory))
            schema = (
                package_root
                / "skills"
                / "scholarly-pdf-reconstruction"
                / "assets"
                / "section-map.schema.json"
            )
            schema.write_text(
                '{"$schema": "https://json-schema.org/draft/2020-12/schema", '
                '"type": 7}',
                encoding="utf-8",
            )
            with self.assertRaisesRegex(
                validate_package.ValidationError,
                "not a valid Draft 2020-12 schema",
            ):
                _validate_canonical(package_root)

    def test_shared_file_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            package_root = _copy_package(Path(temporary_directory))
            profile = (
                package_root
                / "skills"
                / "scholarly-render-qa"
                / "assets"
                / "publication-profile.json"
            )
            document = json.loads(profile.read_text(encoding="utf-8"))
            document["test-marker"] = True
            profile.write_text(
                json.dumps(document),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(
                validate_package.ValidationError,
                "shared package files differ",
            ):
                _validate_canonical(package_root)


class RuntimeValidationTests(unittest.TestCase):
    def test_missing_extra_different_and_symlinked_deployment_is_rejected(
        self,
    ) -> None:
        for scenario in ("missing", "extra", "different", "symlinked"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    if scenario == "extra":
                        extra = (
                            repository_root
                            / ".agents"
                            / "skills"
                            / validate_package.EXPECTED_SKILLS[0]
                            / "assets"
                            / "extra.json"
                        )
                        extra.write_text("{}", encoding="utf-8")
                        with self.assertRaisesRegex(
                            validate_package.ValidationError,
                            "runtime deployment file set differs",
                        ):
                            _validate_runtime(repository_root, package_root)
                        continue
                    with mock.patch.object(
                        validate_package,
                        "PACKAGE_ROOT",
                        package_root,
                    ):
                        include = validate_package.load_package().includes[0]
                    deployed = repository_root / ".agents" / Path(include)
                    canonical = package_root / Path(include)
                    deployed.unlink()
                    if scenario == "different":
                        deployed.write_text("different", encoding="utf-8")
                    elif scenario == "symlinked":
                        try:
                            deployed.symlink_to(canonical)
                        except (NotImplementedError, OSError) as error:
                            message = f"file symlinks are unavailable: {error}"
                            raise unittest.SkipTest(message) from error
                    with self.assertRaises(validate_package.ValidationError):
                        _validate_runtime(repository_root, package_root)

    def test_root_registration_must_be_present_once(self) -> None:
        for scenario in ("missing", "duplicate"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    apm_path = repository_root / "apm.yml"
                    apm = _load_yaml(apm_path)
                    dependencies = apm["dependencies"]
                    assert isinstance(dependencies, dict)
                    registrations = dependencies["apm"]
                    assert isinstance(registrations, list)
                    if scenario == "missing":
                        registrations.clear()
                    else:
                        registrations.append(copy.deepcopy(registrations[0]))
                    _write_yaml(apm_path, apm)
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        "register the package exactly once",
                    ):
                        _validate_runtime(repository_root, package_root)

    def test_lock_dependency_identity_is_exact(self) -> None:
        for scenario in ("wrong", "missing", "duplicate"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    lock_path = repository_root / "apm.lock.yaml"
                    lock = _load_yaml(lock_path)
                    dependencies = lock["dependencies"]
                    assert isinstance(dependencies, list)
                    dependency = _dependency(lock)
                    if scenario == "wrong":
                        dependency["version"] = "9.9.9"
                    elif scenario == "missing":
                        dependencies.remove(dependency)
                    else:
                        dependencies.append(copy.deepcopy(dependency))
                    _write_yaml(lock_path, lock)
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        "scholarly local dependency|dependency identity",
                    ):
                        _validate_runtime(repository_root, package_root)

    def test_dependency_deployed_files_set_is_exact(self) -> None:
        for scenario in ("missing", "extra", "duplicate"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    lock_path = repository_root / "apm.lock.yaml"
                    lock = _load_yaml(lock_path)
                    deployed_files = _dependency(lock)["deployed_files"]
                    assert isinstance(deployed_files, list)
                    if scenario == "missing":
                        deployed_files.pop()
                    elif scenario == "extra":
                        deployed_files.append(".agents/skills/scholarly-extra")
                    else:
                        deployed_files.append(deployed_files[0])
                    _write_yaml(lock_path, lock)
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        "deployed_files set is not exact",
                    ):
                        _validate_runtime(repository_root, package_root)

    def test_dependency_hash_bindings_are_exact(self) -> None:
        for scenario in ("wrong", "missing", "extra"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    lock_path = repository_root / "apm.lock.yaml"
                    lock = _load_yaml(lock_path)
                    hashes = _dependency(lock)["deployed_file_hashes"]
                    assert isinstance(hashes, dict)
                    path = next(iter(hashes))
                    if scenario == "wrong":
                        hashes[path] = f"sha256:{'0' * 64}"
                    elif scenario == "missing":
                        hashes.pop(path)
                    else:
                        hashes[".agents/skills/extra/SKILL.md"] = (
                            f"sha256:{'0' * 64}"
                        )
                    _write_yaml(lock_path, lock)
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        "deployed_file_hashes",
                    ):
                        _validate_runtime(repository_root, package_root)

    def test_owned_deployment_bindings_are_exact(self) -> None:
        for scenario in ("wrong", "missing", "extra"):
            with self.subTest(scenario):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    repository_root, package_root = _make_runtime_fixture(
                        Path(temporary_directory)
                    )
                    lock_path = repository_root / "apm.lock.yaml"
                    lock = _load_yaml(lock_path)
                    deployments = lock["deployments"]
                    assert isinstance(deployments, list)
                    first = deployments[0]
                    assert isinstance(first, dict)
                    if scenario == "wrong":
                        first["content_hash"] = f"sha256:{'0' * 64}"
                    elif scenario == "missing":
                        deployments.pop(0)
                    else:
                        extra = copy.deepcopy(first)
                        extra["value"] = ".agents/skills/scholarly-extra"
                        deployments.append(extra)
                    _write_yaml(lock_path, lock)
                    with self.assertRaisesRegex(
                        validate_package.ValidationError,
                        "deployment",
                    ):
                        _validate_runtime(repository_root, package_root)


if __name__ == "__main__":
    unittest.main()
