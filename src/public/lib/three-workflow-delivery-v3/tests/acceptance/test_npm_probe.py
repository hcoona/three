"""Local scenarios, not package approval, actual publish or native proof."""

# ruff: noqa: D103, PLR2004

from __future__ import annotations

import json
import os
import runpy
import shutil
import stat
import sys
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import npm_probe as probe
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    inspect_npm_fixture,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
    NpmProcessOutcome,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

ROOT = Path(__file__).resolve().parents[6]
NOW = datetime(2026, 9, 7, tzinfo=UTC)
TOKEN = "probe-test-token-not-an-actions-credential"  # noqa: S105
REQUEST = probe.NpmProbeRequest(
    fixture=NpmFixtureSpec(
        package="@hcoona/synthetic-native-probe",
        version="0.0.1-acceptance.1",
        target="b" * 40,
        generation="synthetic-probe",
    ),
    disposable_package_preconditions=DisposablePackagePreconditions(
        package="@hcoona/synthetic-native-probe",
        preexisting_container=True,
        operator_controlled=True,
        production_dependency=False,
    ),
)
PRECOMMAND_FILES = {
    "request.json",
    "fixture.tgz",
    "profile-match.json",
    "command-started",
}


class ControlledNpm:
    """Use the Publication tests' scripted process style, not registry mocks."""

    def __init__(self):
        """Record each query and inject only the final process outcome."""
        self.calls = []
        self.overrides = {}
        self.outcome = NpmProcessOutcome("definitive-success", returncode=0)
        self.after_publish = None

    @property
    def publications(self):
        """Select the mutating process boundary from all recorded calls."""
        return [
            call for call in self.calls if call[0][:2] == ("npm", "publish")
        ]

    def run(self, argv, *, cwd, environment, timeout, output_limit):
        """Return scripted version/config facts without executing npm."""
        self.calls.append((argv, cwd, dict(environment), timeout, output_limit))
        if argv[:2] == ("npm", "publish"):
            if self.after_publish is not None:
                self.after_publish()
            if isinstance(self.outcome, BaseException):
                raise self.outcome
            return self.outcome
        if argv == ("node", "--version"):
            value = self.overrides.get("node", "v24.19.0")
        elif argv == ("npm", "--version"):
            value = self.overrides.get("npm", "11.17.0")
        else:
            assert argv[:3] == ("npm", "config", "get")
            expected = {
                "@hcoona:registry": "https://npm.pkg.github.com",
                "registry": "https://npm.pkg.github.com/",
                "tag": argv[argv.index("--tag") + 1],
                "ignore-scripts": "true",
                "fetch-retries": "0",
                "access": "null",
            }
            value = self.overrides.get(argv[3], expected[argv[3]])
        return NpmProcessOutcome(
            "definitive-success", (value + "\n").encode(), returncode=0
        )


@pytest.fixture
def probe_case(tmp_path):
    """Use the real parser bridge with a separate untrusted checkout."""
    checkout = tmp_path / "checkout"
    bridge = checkout / "eng/scripts/workflow_delivery_v3_native_npm.mjs"
    bridge.parent.mkdir(parents=True)
    bridge.symlink_to(ROOT / bridge.relative_to(checkout))
    (checkout / ".npmrc").write_text(
        "registry=https://untrusted.invalid\n"
        "@hcoona:registry=https://untrusted.invalid\nignore-scripts=false\n"
    )
    (checkout / "package.json").write_text(
        '{"scripts":{"prepublishOnly":"unwanted-target-code"}}'
    )
    toolchain = tmp_path / "toolchain"
    toolchain.mkdir()
    return {
        "repository_root": checkout,
        "runtime_directory": tmp_path / "runtime",
        "toolchain_directory": toolchain,
        "evidence_directory": tmp_path / "evidence",
        "token": TOKEN,
        "runner": ControlledNpm(),
        "clock": lambda: NOW,
    }


def _names(directory):
    return {path.name for path in directory.iterdir()}


def _load(directory, name):
    return parse_canonical_json((directory / name).read_bytes())


def _cli_arguments(case):
    request = case["repository_root"].parent / "request.json"
    request.write_bytes(canonicalize(REQUEST.to_document()))
    return [
        "probe",
        "--request",
        str(request),
        "--repository-root",
        str(case["repository_root"]),
        "--runtime-directory",
        str(case["runtime_directory"]),
        "--toolchain-directory",
        str(case["toolchain_directory"]),
        "--evidence-directory",
        str(case["evidence_directory"]),
    ]


def test_canonical_request_roundtrip_keeps_explicit_preconditions(tmp_path):
    document = REQUEST.to_document()
    path = tmp_path / "request.json"
    path.write_bytes(canonicalize(document))

    assert probe.parse_request(path.read_bytes()) == REQUEST
    assert probe.read_request(path) == REQUEST
    assert document == {
        "schema": "workflow-delivery-v3/native-npm-probe-request/v1",
        "fixture": {
            "package": "@hcoona/synthetic-native-probe",
            "version": "0.0.1-acceptance.1",
            "target": "b" * 40,
            "generation": "synthetic-probe",
            "variant": "original",
        },
        "disposable_package_preconditions": {
            "package": "@hcoona/synthetic-native-probe",
            "preexisting_container": True,
            "operator_controlled": True,
            "production_dependency": False,
        },
    }
    assert (
        type(probe.read_request(path).disposable_package_preconditions)
        is DisposablePackagePreconditions
    )


@pytest.mark.parametrize(
    "document",
    [
        json.dumps(REQUEST.to_document()).encode(),
        canonicalize(REQUEST.to_document()) + b"\n",
        b'{"schema":"duplicate","schema":"duplicate"}',
        b"[]",
    ],
)
def test_request_rejects_noncanonical_or_duplicate_json(document):
    with pytest.raises((ValueError, TypeError)):
        probe.parse_request(document)


@pytest.mark.parametrize(
    ("section", "field", "value"),
    [
        (None, "schema", "workflow-delivery-v3/native-npm-probe-request/v2"),
        (None, "admin_token", "forbidden-request-input"),
        (None, "fixture", []),
        ("fixture", "version", 1),
        ("fixture", "variant", "other"),
        ("fixture", "tag", "latest"),
        ("disposable_package_preconditions", "operator_controlled", "true"),
        ("disposable_package_preconditions", "preexisting_container", 1),
        ("disposable_package_preconditions", "production_dependency", None),
        ("disposable_package_preconditions", "operator_controlled", False),
        ("disposable_package_preconditions", "preexisting_container", False),
        ("disposable_package_preconditions", "production_dependency", True),
        ("disposable_package_preconditions", "package", "@hcoona/other"),
        ("disposable_package_preconditions", "deleted_version_id", 123),
    ],
)
def test_request_rejection_cannot_publish(probe_case, section, field, value):
    document = json.loads(canonicalize(REQUEST.to_document()))
    target = document if section is None else document[section]
    target[field] = value

    with pytest.raises((ValueError, TypeError)):
        probe.run_npm_probe(
            probe.parse_request(canonicalize(document)), **probe_case
        )

    assert not probe_case["runner"].calls
    assert not probe_case["runtime_directory"].exists()
    assert not probe_case["evidence_directory"].exists()


@pytest.mark.parametrize(
    ("section", "field"),
    [
        (None, "disposable_package_preconditions"),
        ("fixture", "variant"),
        ("fixture", "package"),
        ("disposable_package_preconditions", "production_dependency"),
    ],
)
def test_request_has_no_missing_field_defaults(section, field):
    document = json.loads(canonicalize(REQUEST.to_document()))
    target = document if section is None else document[section]
    del target[field]

    with pytest.raises(ValueError, match="closure"):
        probe.parse_request(canonicalize(document))


@pytest.mark.parametrize(
    "package",
    [
        "@hcoona/hcoona-release-smoke-npm",
        "@hcoona/hexo-renderer-asciidoc",
        "@another/synthetic-native-probe",
        "@hcoona/Uppercase",
    ],
)
def test_package_exclusions_and_official_scope_reject_before_runner(
    probe_case, package
):
    document = json.loads(canonicalize(REQUEST.to_document()))
    document["fixture"]["package"] = package
    document["disposable_package_preconditions"]["package"] = package

    with pytest.raises(ValueError, match=r"disposable|official npm fixture"):
        probe.run_npm_probe(
            probe.parse_request(canonicalize(document)), **probe_case
        )

    assert not probe_case["runner"].calls
    assert not probe_case["runtime_directory"].exists()


def test_one_publish_uses_exact_profile_environment_and_precommand_evidence(
    probe_case, monkeypatch
):
    for key in ("NODE_OPTIONS", "NODE_AUTH_TOKEN", "NPM_CONFIG_REGISTRY"):
        monkeypatch.setenv(key, "synthetic-untrusted-input")
    directory = probe_case["runtime_directory"]
    evidence = probe_case["evidence_directory"]
    runner = probe_case["runner"]
    command = (
        "npm",
        "publish",
        str(directory / "fixture.tgz"),
        "--registry",
        "https://npm.pkg.github.com",
        "--tag",
        "buddy-sha-" + REQUEST.fixture.target,
        "--ignore-scripts",
        "--fetch-retries=0",
    )

    def before_publish():
        assert _names(evidence) == PRECOMMAND_FILES
        assert (evidence / "command-started").read_bytes() == b""
        assert _load(evidence, "request.json") == REQUEST.to_document()
        assert _load(evidence, "profile-match.json")["command"] == list(command)
        assert (directory / "fixture.tgz").read_bytes() == (
            evidence / "fixture.tgz"
        ).read_bytes()
        assert stat.S_IMODE(directory.stat().st_mode) == 0o700
        assert stat.S_IMODE((directory / "user.npmrc").stat().st_mode) == 0o600
        assert "${GITHUB_TOKEN}" in (directory / "user.npmrc").read_text()
        assert TOKEN not in (directory / "user.npmrc").read_text()

    runner.after_publish = before_publish
    result = probe.run_npm_probe(REQUEST, **probe_case)

    expected_environment = {
        "PATH": os.pathsep.join(
            (str(probe_case["toolchain_directory"]), os.defpath)
        ),
        "HOME": str(directory / "home"),
        "TMPDIR": str(directory / "scratch"),
        "GITHUB_TOKEN": TOKEN,
        "NPM_CONFIG_USERCONFIG": str(directory / "user.npmrc"),
        "NPM_CONFIG_GLOBALCONFIG": str(directory / "global.npmrc"),
        "NPM_CONFIG_CACHE": str(directory / "cache"),
        "NPM_CONFIG_LOGS_MAX": "0",
    }
    assert runner.publications == [
        (command, directory, expected_environment, 120.0, 4096)
    ]
    assert result.command_classification == "definitive-success"
    assert result.returncode == 0
    assert result.truncated is False
    assert len(runner.calls) == 9
    assert all(call[1] == directory for call in runner.calls)
    assert all(call[2] == expected_environment for call in runner.calls)
    for argv, _, _, timeout, limit in runner.calls[:-1]:
        assert timeout == 20.0
        assert limit == 4096
        if argv[:3] == ("npm", "config", "get"):
            assert argv[4:] == command[3:]
    assert _load(evidence, "result.json") == result.to_document()
    assert _names(evidence) == PRECOMMAND_FILES | {"result.json"}
    profile = _load(evidence, "profile-match.json")
    assert profile["destination-operation-profile-digest"] == (
        github_packages_destination_operation_profile().profile_digest
    )
    assert profile["matched-at"] == "2026-09-07T00:00:00Z"
    assert result.profile_match_digest == canonical_sha256(profile)
    assert result.request_digest == canonical_sha256(REQUEST.to_document())
    assert result.fixture_content == inspect_npm_fixture(
        (evidence / "fixture.tgz").read_bytes(), repository_root=ROOT
    )
    assert not directory.exists()


def test_different_probe_changes_actual_bytes_not_version(probe_case):
    first = probe.run_npm_probe(REQUEST, **probe_case)
    first_bytes = (
        probe_case["evidence_directory"] / "fixture.tgz"
    ).read_bytes()
    probe_case["evidence_directory"] = (
        probe_case["evidence_directory"].parent / "different-evidence"
    )
    different_document = REQUEST.to_document()
    fixture = different_document["fixture"]
    assert isinstance(fixture, dict)
    fixture["variant"] = "different"
    different_request = probe.parse_request(canonicalize(different_document))

    second = probe.run_npm_probe(different_request, **probe_case)
    second_bytes = (
        probe_case["evidence_directory"] / "fixture.tgz"
    ).read_bytes()

    assert first_bytes != second_bytes
    assert first.fixture_content.version == second.fixture_content.version
    assert first.fixture_content.sha256 != second.fixture_content.sha256
    assert first.fixture_content.sha512 != second.fixture_content.sha512
    assert first.fixture_content.witness != second.fixture_content.witness
    assert parse_canonical_json(second.fixture_content.witness)["variant"] == (
        "different"
    )
    assert first.profile_match_digest == second.profile_match_digest
    assert len(probe_case["runner"].publications) == 2


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("node", "v22.0.0"),
        ("npm", "10.0.0"),
        ("registry", "https://untrusted.invalid/"),
        ("fetch-retries", "2"),
    ],
)
def test_wrong_toolchain_or_effective_config_never_publishes(
    probe_case, key, value
):
    probe_case["runner"].overrides[key] = value

    with pytest.raises(ValueError, match=r"toolchain|configuration mismatch"):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert not probe_case["runner"].publications
    assert _names(probe_case["evidence_directory"]) == {
        "request.json",
        "fixture.tgz",
    }
    assert not probe_case["runtime_directory"].exists()


@pytest.mark.parametrize("filename", ["user.npmrc", "fixture.tgz"])
def test_changed_prepared_config_or_content_blocks_before_publish(
    probe_case, monkeypatch, filename
):
    real_match = probe.match_npm_profile

    def changed_after_query(**kwargs):
        result = real_match(**kwargs)
        (kwargs["directory"] / filename).write_bytes(b"changed")
        return result

    monkeypatch.setattr(probe, "match_npm_profile", changed_after_query)

    with pytest.raises(ValueError, match=r"configuration|fixture bytes"):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert not probe_case["runner"].publications
    assert not (probe_case["evidence_directory"] / "command-started").exists()
    assert not probe_case["runtime_directory"].exists()


@pytest.mark.parametrize(
    "operand",
    [
        "runtime-in-checkout",
        "toolchain-in-checkout",
        "toolchain-file",
        "evidence-in-runtime",
        "runtime-in-evidence",
        "symlink-runtime",
        "symlink-evidence",
    ],
)
def test_unsafe_paths_fail_before_process_or_claim(probe_case, operand):
    checkout = probe_case["repository_root"]
    runtime = probe_case["runtime_directory"]
    evidence = probe_case["evidence_directory"]
    if operand == "runtime-in-checkout":
        probe_case["runtime_directory"] = checkout / "runtime"
    elif operand == "toolchain-in-checkout":
        probe_case["toolchain_directory"] = checkout
    elif operand == "toolchain-file":
        probe_case["toolchain_directory"] = checkout.parent / "toolchain-file"
        probe_case["toolchain_directory"].touch()
    elif operand == "evidence-in-runtime":
        probe_case["evidence_directory"] = runtime / "evidence"
    elif operand == "runtime-in-evidence":
        probe_case["runtime_directory"] = evidence / "runtime"
    else:
        path = runtime if operand == "symlink-runtime" else evidence
        path.symlink_to(checkout, target_is_directory=True)

    with pytest.raises(ValueError, match=r"checkout|directories|separate"):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert not probe_case["runner"].calls
    assert not (checkout / "command-started").exists()
    assert (checkout / ".npmrc").is_file()


@pytest.mark.parametrize(
    "occupied", ["runtime_directory", "evidence_directory"]
)
def test_existing_claim_is_never_overwritten_or_cleaned(probe_case, occupied):
    directory = probe_case[occupied]
    directory.mkdir()
    (directory / "command-started").write_bytes(b"another invocation")

    with pytest.raises(FileExistsError):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert (directory / "command-started").read_bytes() == b"another invocation"
    assert _names(directory) == {"command-started"}
    assert not probe_case["runner"].calls
    if occupied == "evidence_directory":
        assert not probe_case["runtime_directory"].exists()


def test_competing_probe_and_completed_audit_cannot_reinvoke(probe_case):
    runner = probe_case["runner"]
    runtime = probe_case["runtime_directory"]
    evidence = probe_case["evidence_directory"]

    def competing_probe():
        with pytest.raises(FileExistsError):
            probe.run_npm_probe(REQUEST, **probe_case)
        assert (runtime / "fixture.tgz").is_file()
        assert _names(evidence) == PRECOMMAND_FILES

    runner.after_publish = competing_probe
    first = probe.run_npm_probe(REQUEST, **probe_case)
    with pytest.raises(FileExistsError):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert _load(evidence, "result.json") == first.to_document()
    assert len(runner.publications) == 1
    assert not runtime.exists()


def test_partial_configuration_failure_cleans_only_owned_runtime(
    probe_case, monkeypatch
):
    initialize = probe.initialize_npm_configuration

    def partial_initialization(directory, tarball):
        initialize(directory, tarball)
        message = "synthetic configuration IO failure"
        raise OSError(message)

    monkeypatch.setattr(
        probe, "initialize_npm_configuration", partial_initialization
    )
    with pytest.raises(OSError, match="configuration IO"):
        probe.run_npm_probe(REQUEST, **probe_case)

    assert not probe_case["runner"].calls
    assert not probe_case["runtime_directory"].exists()
    assert _names(probe_case["evidence_directory"]) == {
        "request.json",
        "fixture.tgz",
    }


@pytest.mark.parametrize(
    "outcome",
    [
        NpmProcessOutcome("definitive-success", returncode=0),
        NpmProcessOutcome("definitive-non-success", returncode=1),
        NpmProcessOutcome("ambiguous", returncode=-9),
        NpmProcessOutcome("not-initiated"),
    ],
)
def test_process_facts_never_claim_mutation_or_acceptance(probe_case, outcome):
    probe_case["runner"].outcome = replace(
        outcome,
        output=(f"npm private output {TOKEN}\n" + TOKEN[:12]).encode(),
        truncated=True,
    )

    result = probe.run_npm_probe(REQUEST, **probe_case)
    evidence = probe_case["evidence_directory"]

    assert result.command_classification == outcome.classification
    assert result.returncode == outcome.returncode
    assert result.truncated is True
    assert set(result.to_document()) == {
        "schema",
        "request_digest",
        "profile_match_digest",
        "fixture_content",
        "command_classification",
        "returncode",
        "truncated",
    }
    assert result.to_document()["schema"] == (
        "workflow-delivery-v3/native-npm-probe-result/v1"
    )
    assert _load(evidence, "result.json") == result.to_document()
    assert len(probe_case["runner"].publications) == 1
    assert _names(evidence) == PRECOMMAND_FILES | {"result.json"}
    for file in evidence.iterdir():
        content = file.read_bytes()
        assert TOKEN.encode() not in content
        assert TOKEN[:12].encode() not in content
        assert b"npm private output" not in content
        assert b"_authToken" not in content
    assert not probe_case["runtime_directory"].exists()


@pytest.mark.parametrize(
    "error",
    [RuntimeError("uncontrolled process"), OSError("uncontrolled IO")],
)
def test_uncontrolled_exception_retains_precommand_evidence_not_result(
    probe_case, error
):
    probe_case["runner"].outcome = error

    with pytest.raises(type(error), match="uncontrolled") as caught:
        probe.run_npm_probe(REQUEST, **probe_case)

    assert caught.value is error
    assert len(probe_case["runner"].publications) == 1
    assert _names(probe_case["evidence_directory"]) == PRECOMMAND_FILES
    assert not probe_case["runtime_directory"].exists()


@pytest.mark.parametrize("failed_file", ["profile-match.json", "result.json"])
def test_audit_io_failure_is_closed_without_reinvocation(
    probe_case, monkeypatch, failed_file
):
    real_write = probe.write_private_file

    def failed_write(path, content):
        if path.name == failed_file:
            message = "synthetic evidence IO failure"
            raise OSError(message)
        real_write(path, content)

    monkeypatch.setattr(probe, "write_private_file", failed_write)
    with pytest.raises(OSError, match="evidence IO"):
        probe.run_npm_probe(REQUEST, **probe_case)

    publications = probe_case["runner"].publications
    assert len(publications) == (1 if failed_file == "result.json" else 0)
    assert not (probe_case["evidence_directory"] / "result.json").exists()
    assert not probe_case["runtime_directory"].exists()
    assert _names(probe_case["evidence_directory"]) == (
        PRECOMMAND_FILES
        if failed_file == "result.json"
        else {"request.json", "fixture.tgz"}
    )


@pytest.mark.parametrize(
    ("outcome", "exit_code"),
    [
        (NpmProcessOutcome("definitive-success", returncode=0), 0),
        (NpmProcessOutcome("definitive-non-success", returncode=1), 1),
        (NpmProcessOutcome("ambiguous", returncode=-9), 1),
        (NpmProcessOutcome("not-initiated"), 1),
    ],
)
def test_module_cli_uses_environment_token_and_process_exit(
    probe_case, monkeypatch, outcome, exit_code
):
    runner = probe_case["runner"]
    runner.outcome = outcome
    monkeypatch.setattr(probe, "IsolatedNpmProcessRunner", lambda: runner)
    monkeypatch.setenv("GITHUB_TOKEN", TOKEN)
    monkeypatch.setenv("GH_TOKEN", "do-not-use-local-pat")
    monkeypatch.setattr(
        sys, "argv", ["acceptance", *_cli_arguments(probe_case)]
    )

    with pytest.raises(SystemExit) as caught:
        runpy.run_module(
            "three_workflow_delivery_v3.acceptance", run_name="__main__"
        )

    assert caught.value.code == exit_code
    assert len(runner.publications) == 1
    assert runner.publications[0][2]["GITHUB_TOKEN"] == TOKEN
    assert (
        _load(probe_case["evidence_directory"], "result.json")[
            "command_classification"
        ]
        == outcome.classification
    )
    assert not probe_case["runtime_directory"].exists()


def test_cli_never_substitutes_pat_for_missing_github_token(
    probe_case, monkeypatch
):
    monkeypatch.setattr(
        probe, "IsolatedNpmProcessRunner", lambda: probe_case["runner"]
    )
    monkeypatch.delenv("GITHUB_TOKEN", raising=False)
    monkeypatch.setenv("GH_TOKEN", "synthetic-local-pat")
    monkeypatch.setenv("NPM_TOKEN", "synthetic-local-pat")
    monkeypatch.setattr(
        sys, "argv", ["acceptance", *_cli_arguments(probe_case)]
    )

    with pytest.raises(ValueError, match="GITHUB_TOKEN"):
        runpy.run_module(
            "three_workflow_delivery_v3.acceptance", run_name="__main__"
        )

    assert not probe_case["runner"].calls
    assert not probe_case["evidence_directory"].exists()
    assert not probe_case["runtime_directory"].exists()


def test_actual_pinned_nonnetwork_queries_ignore_ambient_and_target_config(
    probe_case, monkeypatch
):
    node = shutil.which("node")
    npm = shutil.which("npm")
    if node is None or npm is None or Path(node).parent != Path(npm).parent:
        pytest.skip("Nonmutating integration requires one installed toolchain")
    probe_case["toolchain_directory"] = Path(node).parent
    real_runner = IsolatedNpmProcessRunner()

    class RealQueryNpm(ControlledNpm):
        """Delegate only version/config queries to real npm, never publish."""

        def run(self, argv, *, cwd, environment, timeout, output_limit):
            if argv[:2] == ("npm", "publish"):
                return super().run(
                    argv,
                    cwd=cwd,
                    environment=environment,
                    timeout=timeout,
                    output_limit=output_limit,
                )
            assert argv in {("node", "--version"), ("npm", "--version")} or (
                argv[:3] == ("npm", "config", "get")
            )
            self.calls.append(
                (argv, cwd, dict(environment), timeout, output_limit)
            )
            return real_runner.run(
                argv,
                cwd=cwd,
                environment=environment,
                timeout=timeout,
                output_limit=output_limit,
            )

    runner = RealQueryNpm()
    probe_case["runner"] = runner
    monkeypatch.setenv("NODE_OPTIONS", "--require=/nonexistent/target-code")
    monkeypatch.setenv("npm_config_registry", "https://untrusted.invalid")
    monkeypatch.setenv("npm_config_userconfig", "/nonexistent/untrusted-config")

    result = probe.run_npm_probe(REQUEST, **probe_case)

    profile = _load(probe_case["evidence_directory"], "profile-match.json")
    assert profile["node-version"] == "24.19.0"
    assert profile["npm-version"] == "11.17.0"
    assert result.command_classification == "definitive-success"
    assert len(runner.calls) == 9
    assert len(runner.publications) == 1
    assert not probe_case["runtime_directory"].exists()
