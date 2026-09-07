"""One local, profile-bound standard npm probe, not native acceptance proof.

The caller must separately authorize the exact disposable package and supply
an actual Actions-issued repository token. Issuer-attested request booleans
are neither that authorization nor native evidence; token provenance cannot
be established from its string. There is no package default or registry IO
other than the single standard publish process.

Evidence is a local audit, not a durable Release marker or a resume protocol.
A command-started file without a complete result is ambiguous to the operator.
The caller owns artifact retention, registry readback, process-class gates and
canonical state comparisons. Process success alone does not pass acceptance.
"""

from __future__ import annotations

import argparse
import os
import shutil
import stat
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixtureSpec,
    _variant,
    build_npm_fixture,
)
from three_workflow_delivery_v3.adapters.npm_process import (
    IsolatedNpmProcessRunner,
)
from three_workflow_delivery_v3.adapters.npm_runtime import (
    NPM_OUTPUT_LIMIT,
    NPM_PUBLISH_TIMEOUT,
    initialize_npm_configuration,
    match_npm_profile,
    npm_environment,
    npm_runtime_paths,
    validate_npm_runtime,
    write_private_file,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

if TYPE_CHECKING:
    from collections.abc import Callable, Sequence

    from three_workflow_delivery_v3.acceptance.native_npm import ObservedContent
    from three_workflow_delivery_v3.adapters.npm_process import (
        CommandClassification,
        NpmProcessRunner,
    )
    from three_workflow_delivery_v3.canonical import JsonValue

NPM_PROBE_REQUEST_SCHEMA = "workflow-delivery-v3/native-npm-probe-request/v1"
NPM_PROBE_RESULT_SCHEMA = "workflow-delivery-v3/native-npm-probe-result/v1"
_FORBIDDEN_PACKAGES = {
    "@hcoona/hcoona-release-smoke-npm",
    "@hcoona/hexo-renderer-asciidoc",
}


@dataclass(frozen=True, slots=True)
class NpmProbeRequest:
    """Explicit fixture and issuer-attested, exactly bound preconditions."""

    fixture: NpmFixtureSpec
    disposable_package_preconditions: DisposablePackagePreconditions

    def __post_init__(self) -> None:
        """Reject misbinding and known non-disposable coordinates locally."""
        if (
            type(self.fixture) is not NpmFixtureSpec
            or type(self.disposable_package_preconditions)
            is not DisposablePackagePreconditions
        ):
            message = (
                "probe requires typed fixture and disposable preconditions"
            )
            raise TypeError(message)
        if (
            self.fixture.package
            != self.disposable_package_preconditions.package
            or self.fixture.package in _FORBIDDEN_PACKAGES
        ):
            message = "probe package must match an allowed disposable package"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the complete closed request without inferred approvals."""
        return {
            "schema": NPM_PROBE_REQUEST_SCHEMA,
            "fixture": {
                "package": self.fixture.package,
                "version": self.fixture.version,
                "target": self.fixture.target,
                "generation": self.fixture.generation,
                "variant": self.fixture.variant,
            },
            "disposable_package_preconditions": (
                self.disposable_package_preconditions.to_document()
            ),
        }


def _object(value: JsonValue, fields: set[str]) -> dict[str, JsonValue]:
    if not isinstance(value, dict):
        message = "probe request fields must be objects"
        raise TypeError(message)
    if set(value) != fields:
        message = "probe request field closure mismatch"
        raise ValueError(message)
    return value


def _string(value: JsonValue) -> str:
    if type(value) is not str:
        message = "probe request identity fields must be strings"
        raise TypeError(message)
    return value


def _boolean(value: JsonValue) -> bool:
    if type(value) is not bool:
        message = "probe request preconditions must be booleans"
        raise TypeError(message)
    return value


def parse_request(document: bytes) -> NpmProbeRequest:
    """Parse canonical JSON with closed fields and no coercion or defaults."""
    request = _object(
        parse_canonical_json(document),
        {"schema", "fixture", "disposable_package_preconditions"},
    )
    if request["schema"] != NPM_PROBE_REQUEST_SCHEMA:
        message = "unsupported npm probe request schema"
        raise ValueError(message)
    fixture = _object(
        request["fixture"],
        {"package", "version", "target", "generation", "variant"},
    )
    preconditions = _object(
        request["disposable_package_preconditions"],
        {
            "package",
            "preexisting_container",
            "operator_controlled",
            "production_dependency",
        },
    )
    return NpmProbeRequest(
        fixture=NpmFixtureSpec(
            package=_string(fixture["package"]),
            version=_string(fixture["version"]),
            target=_string(fixture["target"]),
            generation=_string(fixture["generation"]),
            variant=_variant(fixture["variant"]),
        ),
        disposable_package_preconditions=DisposablePackagePreconditions(
            package=_string(preconditions["package"]),
            preexisting_container=_boolean(
                preconditions["preexisting_container"]
            ),
            operator_controlled=_boolean(preconditions["operator_controlled"]),
            production_dependency=_boolean(
                preconditions["production_dependency"]
            ),
        ),
    )


def read_request(path: Path) -> NpmProbeRequest:
    """Read only the explicitly supplied canonical local request."""
    return parse_request(path.read_bytes())


@dataclass(frozen=True, slots=True)
class NpmProbeResult:
    """Local command facts and fixture/profile bindings, never remote state."""

    request_digest: str
    profile_match_digest: str
    fixture_content: ObservedContent
    command_classification: CommandClassification
    returncode: int | None
    truncated: bool

    def to_document(self) -> dict[str, JsonValue]:
        """Omit all npm output rather than retain possibly partial secrets."""
        return {
            "schema": NPM_PROBE_RESULT_SCHEMA,
            "request_digest": self.request_digest,
            "profile_match_digest": self.profile_match_digest,
            "fixture_content": self.fixture_content.to_document(),
            "command_classification": self.command_classification,
            "returncode": self.returncode,
            "truncated": self.truncated,
        }


def _evidence_path(evidence: Path, runtime: Path) -> Path:
    evidence = evidence.absolute()
    if (
        evidence != evidence.resolve()
        or evidence.is_relative_to(runtime)
        or runtime.is_relative_to(evidence)
    ):
        message = "probe evidence must be separate from private runtime"
        raise ValueError(message)
    return evidence


def run_npm_probe(  # noqa: PLR0913
    request: NpmProbeRequest,
    *,
    repository_root: Path,
    runtime_directory: Path,
    toolchain_directory: Path,
    evidence_directory: Path,
    token: str,
    runner: NpmProcessRunner,
    clock: Callable[[], datetime],
) -> NpmProbeResult:
    """Validate locally, invoke once, retain evidence and clean owned config.

    Fresh runtime creation is the short-lived exclusion claim; fresh evidence
    also prevents reusing the same audit after private cleanup. Unknown process
    or IO exceptions propagate, without retry or synthetic result. Controlled
    non-success and ambiguous outcomes remain inspectable process facts.
    """
    if type(request) is not NpmProbeRequest:
        message = "probe requires an explicit parsed request"
        raise TypeError(message)
    repository_root = repository_root.resolve(strict=True)
    directory, toolchain = npm_runtime_paths(
        runtime_directory, toolchain_directory, repository_root
    )
    if not repository_root.is_dir() or not toolchain.is_dir():
        message = "probe checkout and toolchain must be directories"
        raise ValueError(message)
    evidence = _evidence_path(evidence_directory, directory)
    environment = npm_environment(directory, toolchain, token)
    fixture = build_npm_fixture(
        request.fixture, repository_root=repository_root
    )
    request_document = request.to_document()
    # Do not enter the cleanup scope unless this invocation owns the claim.
    directory.mkdir(mode=0o700)
    try:
        evidence.mkdir(mode=0o700)
        write_private_file(
            evidence / "request.json", canonicalize(request_document)
        )
        write_private_file(evidence / "fixture.tgz", fixture.tarball)
        initialize_npm_configuration(directory, evidence / "fixture.tgz")
        validate_npm_runtime(directory)
        tarball = directory / "fixture.tgz"
        match = match_npm_profile(
            tarball=tarball,
            tag="buddy-sha-" + request.fixture.target,
            directory=directory,
            toolchain=toolchain,
            token=token,
            runner=runner,
            clock=clock,
        )
        validate_npm_runtime(directory)
        if (
            not stat.S_ISREG(tarball.lstat().st_mode)
            or tarball.read_bytes() != fixture.tarball
        ):
            message = "probe prepared fixture bytes changed"
            raise ValueError(message)
        write_private_file(
            evidence / "profile-match.json",
            canonicalize(match.to_document()),
        )
        write_private_file(evidence / "command-started", b"")
        outcome = runner.run(
            match.command,
            cwd=directory,
            environment=environment,
            timeout=NPM_PUBLISH_TIMEOUT,
            output_limit=NPM_OUTPUT_LIMIT,
        )
        result = NpmProbeResult(
            request_digest=canonical_sha256(request_document),
            profile_match_digest=match.match_digest,
            fixture_content=fixture.content,
            command_classification=outcome.classification,
            returncode=outcome.returncode,
            truncated=outcome.truncated,
        )
        write_private_file(
            evidence / "result.json", canonicalize(result.to_document())
        )
        return result
    finally:
        shutil.rmtree(directory)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the CLI with only the caller's environment GITHUB_TOKEN.

    The workflow caller must enforce actual Actions issuance, authorization
    and run_attempt == 1. No alternate credential lookup is performed here.
    """
    parser = argparse.ArgumentParser(
        description="Acceptance-only one-shot standard npm probe"
    )
    probe = parser.add_subparsers(dest="command", required=True).add_parser(
        "probe"
    )
    for name in (
        "request",
        "repository-root",
        "runtime-directory",
        "toolchain-directory",
        "evidence-directory",
    ):
        probe.add_argument("--" + name, type=Path, required=True)
    arguments = parser.parse_args(argv)
    result = run_npm_probe(
        read_request(arguments.request),
        repository_root=arguments.repository_root,
        runtime_directory=arguments.runtime_directory,
        toolchain_directory=arguments.toolchain_directory,
        evidence_directory=arguments.evidence_directory,
        token=os.environ.get("GITHUB_TOKEN", ""),
        runner=IsolatedNpmProcessRunner(),
        clock=lambda: datetime.now(UTC),
    )
    return 0 if result.command_classification == "definitive-success" else 1
