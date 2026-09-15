"""Execute a separately admitted NuGet suite once from a pinned clean checkout.

The request and retained admission carrier do not authorize themselves. Before
invocation, the operator must close the native request, access, source, fixture,
preflight, Windows profile and prebuilt consumer provenance under the NuGet LLD.
This entry performs no credential lookup, host build or Live activation.
"""

from __future__ import annotations

import argparse
import os
import re
import time
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance import nuget_operator as reads
from three_workflow_delivery_v3.acceptance.nuget_consumer import (
    NuGetConsumerLimits,
    NuGetConsumerRequest,
    run_nuget_consumer,
)
from three_workflow_delivery_v3.acceptance.nuget_dispatch import (
    NuGetProbeCollector,
)
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    _MANIFEST_LIMIT,
    NuGetStateEvidence,
    _object,
    _read,
    _require,
    _sha,
    read_capture_evidence,
)
from three_workflow_delivery_v3.acceptance.nuget_github import (
    NuGetGitHubClient,
    NuGetGitHubLimits,
)
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    _archive_files,
    read_uploaded,
)
from three_workflow_delivery_v3.acceptance.nuget_suite import (
    NuGetSuitePlan,
    run_nuget_suite,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import (
    artifact_reference_from_document,
)

if TYPE_CHECKING:
    from collections.abc import Sequence

    from three_workflow_delivery_v3.acceptance.nuget_probe_evidence import (
        NuGetProbeEvidence,
    )


@dataclass(frozen=True)
class NuGetSuiteRequest:
    """Closed execution inputs and prior admission evidence, never a grant."""

    plan: NuGetSuitePlan
    github: NuGetGitHubLimits
    preflight: reads.NuGetReadRequest
    admission_sha256: str

    def __post_init__(self) -> None:
        """Keep the earlier preflight and current suite on one exact subject."""
        _require(
            type(self.plan) is NuGetSuitePlan
            and type(self.github) is NuGetGitHubLimits
            and type(self.preflight) is reads.NuGetReadRequest
            and len(self.preflight.captures) == 1
            and self.preflight.captures[0].label == "preflight",
            "suite requires its separate preflight request",
        )
        previous = self.preflight.captures[0].to_document()
        current = self.plan.reads.captures[0].to_document()
        for document in (previous, current):
            document.pop("label")
            document.pop("limits")
        _require(previous == current, "suite preflight subject changed")
        _require(
            self.preflight.helper_reference == self.plan.reads.helper_reference
            and self.preflight.helper_source_inputs
            == self.plan.reads.helper_source_inputs
            and type(self.admission_sha256) is str
            and re.fullmatch(r"[0-9a-f]{64}", self.admission_sha256)
            is not None,
            "missing suite admission or original helper binding",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Bind separate finite budgets and the prior review carrier."""
        return {
            "schema": "workflow-delivery/v3/nuget-suite-request",
            "plan": self.plan.to_document(),
            "github": self.github.to_document(),
            "preflight": self.preflight.to_document(),
            "admissionSha256": self.admission_sha256,
        }


def read_suite_request(content: bytes) -> NuGetSuiteRequest:
    """Parse the exact canonical request; reject unknown or derived changes."""
    document = _object(parse_canonical_json(content))
    plan = _object(document["plan"])
    consumer = _object(plan["consumerRequest"])
    bounds = _object(consumer["limits"])
    consumer_request = NuGetConsumerRequest(
        cast("str", consumer["generation"]),
        cast("str", consumer["callerToolingSha"]),
        cast("str", consumer["version"]),
        cast("str", consumer["packageSha256"]),
        cast("str", consumer["witnessSha256"]),
        cast("str", consumer["serviceIndexSha256"]),
        cast("str", consumer["packageBaseAddress"]),
        tuple(
            (name, cast("str", digest))
            for name, digest in sorted(
                _object(consumer["restoreHostFiles"]).items()
            )
        ),
        cast("str", consumer["dotnetExecutableSha256"]),
        NuGetConsumerLimits(
            cast("int", bounds["requests"]),
            cast("int", bounds["responseBodyBytes"]),
            cast("int", bounds["restoreTimeoutSeconds"]),
            cast("float", bounds["commandTimeoutSeconds"]),
            cast("float", bounds["completionTimeoutSeconds"]),
            cast("int", bounds["outputBytesPerCommand"]),
        ),
    )
    github = _object(document["github"])
    _require(
        "storageOrigin" not in github and "artifactRedirectPolicy" in github,
        "suite request requires the current artifact redirect policy",
    )
    result = NuGetSuiteRequest(
        NuGetSuitePlan(
            canonicalize(plan["probeInputs"]),
            reads.read_request(canonicalize(plan["readRequest"])),
            consumer_request,
        ),
        NuGetGitHubLimits(
            cast("int", github["requests"]),
            cast("int", github["responseBodyBytes"]),
            cast("int", github["metadataBytesPerResponse"]),
            cast("int", github["artifactBytesPerResponse"]),
            cast("float", github["callTimeoutSeconds"]),
            cast("int", github["pollsPerProbe"]),
            cast("float", github["pollIntervalSeconds"]),
            cast("str", github["artifactRedirectPolicy"]),
        ),
        reads.read_request(canonicalize(document["preflight"])),
        cast("str", document["admissionSha256"]),
    )
    _require(
        canonicalize(result.to_document()) == content,
        "suite request closure mismatch",
    )
    return result


@dataclass(frozen=True)
class NuGetSuiteInputs:
    """Local originals already covered by the independently admitted request."""

    checkout: Path
    preflight_evidence: Path
    fixture_archive: Path
    fixture_audit: Path
    helper_archive: Path
    helper_audit: Path
    restore_host: Path
    admission: Path


class NuGetSuiteOperator:
    """One local lifetime; completion remains candidate sequential evidence."""

    def __init__(
        self,
        request: NuGetSuiteRequest,
        inputs: NuGetSuiteInputs,
        audit_directory: Path,
        token: str,
    ) -> None:
        """Bind source and original evidence before any native operation."""
        self.request = read_suite_request(canonicalize(request.to_document()))
        self.inputs = inputs
        self.root = inputs.checkout.resolve(strict=True)
        self.directory = audit_directory.absolute()
        _require(
            self.directory == self.directory.resolve()
            and not self.directory.is_relative_to(self.root)
            and not self.root.is_relative_to(self.directory)
            and os.environ.get("GITHUB_ACTIONS", "").lower() != "true",
            "suite requires private operator-local evidence outside checkout",
        )
        _require(
            bool(token) and not any(c.isspace() for c in token),
            "invalid suite credential",
        )
        reads._admit_source(self.root, request.plan.reads)  # noqa: SLF001
        self.directory.mkdir(mode=0o700, parents=False, exist_ok=False)
        self.evidence = reads._Evidence(self.directory, token)  # noqa: SLF001
        self.token = token
        self.spent = False
        self.running = False
        self.evidence.write("request.json", canonicalize(request.to_document()))
        static = _object(parse_canonical_json(request.plan.probe_inputs))
        fixture = _object(static["fixture"])
        for name, path, expected in (
            ("admission.md", inputs.admission, request.admission_sha256),
            (
                "fixture-audit.md",
                inputs.fixture_audit,
                fixture["independentAuditSha256"],
            ),
            (
                "helper-audit.md",
                inputs.helper_audit,
                request.plan.reads.helper_audit_sha256,
            ),
        ):
            body = _read(path.parent, path.name, _MANIFEST_LIMIT)
            _require(_sha(body) == expected, "suite prior audit bytes changed")
            self.evidence.write(name, body)
        reference = artifact_reference_from_document(fixture["reference"])
        archive = read_uploaded(inputs.fixture_archive, reference)
        self.evidence.write("fixture-original.zip", archive)
        files = _archive_files(archive)
        original = _object(_object(fixture["inspection"])["original"])
        self.original = files["original/" + cast("str", original["basename"])]
        self.witness = canonicalize(_object(fixture["inspection"])["witness"])
        _require(
            _sha(self.original) == request.plan.consumer.package_sha256
            and _sha(self.witness) == request.plan.consumer.witness_sha256,
            "suite original fixture or witness changed",
        )
        helper_reference = request.plan.reads.helper_reference
        _require(
            Path(helper_reference.payload_path).name
            == helper_reference.payload_path
            and "\\" not in helper_reference.payload_path,
            "suite requires a single original helper filename",
        )
        helper_directory = self.directory / "helper-upload"
        helper_directory.mkdir(mode=0o700)
        self.helper_archive = helper_directory / helper_reference.payload_path
        reads._Evidence(helper_directory, token).write(  # noqa: SLF001
            helper_reference.payload_path,
            read_uploaded(inputs.helper_archive, helper_reference),
        )
        # Copy only the bounded manifest's admitted originals, then revalidate
        # the retained copy. The prior independent audit owns actual provenance.
        read_capture_evidence(
            inputs.preflight_evidence, request.preflight.captures[0]
        )
        manifest = _read(
            inputs.preflight_evidence, "capture.json", _MANIFEST_LIMIT
        )
        names = _object(_object(parse_canonical_json(manifest))["files"])
        retained = self.directory / "preflight"
        retained.mkdir(mode=0o700)
        preflight_writer = reads._Evidence(retained, token)  # noqa: SLF001
        preflight_writer.write("capture.json", manifest)
        for name, entry in names.items():
            preflight_writer.write(
                name,
                _read(
                    inputs.preflight_evidence,
                    name,
                    cast("int", _object(entry)["bytes"]),
                ),
            )
        self.preflight = read_capture_evidence(
            retained, request.preflight.captures[0]
        )

    def capture(self, label: str) -> NuGetStateEvidence:
        """Use the existing reader and its complete original evidence."""
        _require(self.running, "suite lifetime is not running")
        index = self.reader.next_index
        output = self.reader.capture(label)
        return read_capture_evidence(
            output.parent, self.request.plan.reads.captures[index]
        )

    def probe(self, spec: bytes) -> NuGetProbeEvidence:
        """Recheck local source before the exact protected-run collector."""
        _require(self.running, "suite lifetime is not running")
        reads._admit_source(self.root, self.request.plan.reads)  # noqa: SLF001
        return self.collector.collect(spec, deadline=self.deadline)

    def consume(self) -> str:
        """Run the existing clean consumer once and retain its original tree."""
        _require(self.running, "suite lifetime is not running")
        _require(
            time.monotonic()
            + self.request.plan.consumer.limits.completion_timeout_seconds
            < self.deadline,
            "insufficient generation time for the bounded consumer",
        )
        reads._admit_source(self.root, self.request.plan.reads)  # noqa: SLF001
        completed = run_nuget_consumer(
            self.request.plan.consumer,
            original_package=self.original,
            witness=self.witness,
            restore_host=self.inputs.restore_host,
            checkout=self.root,
            audit_directory=self.directory / "consumer",
            token=self.token,
        )
        _require(
            completed == self.directory / "consumer/consumer.json",
            "consumer returned an unexpected completion",
        )
        self.evidence.write(
            "consumer-files.json",
            canonicalize(reads._runtime_files(completed.parent)),  # noqa: SLF001
        )
        return _sha(_read(completed.parent, completed.name, _MANIFEST_LIMIT))

    def execute(self) -> Path:
        """Spend the lifetime before effects; retain failure or completion."""
        _require(not self.spent, "suite lifetime already spent")
        self.spent = True
        self.evidence.write(
            "started.json",
            canonicalize(
                {
                    "requestDigest": canonical_sha256(
                        self.request.to_document()
                    ),
                    "generationSpent": True,
                }
            ),
        )
        started = time.monotonic()
        self.deadline = (
            started + self.request.plan.reads.generation_timeout_seconds
        )
        try:
            self.running = True
            self.reader = reads.NuGetReadOperator(
                self.request.plan.reads,
                checkout=self.root,
                helper_archive=self.helper_archive,
                helper_audit=self.directory / "helper-audit.md",
                audit_directory=self.directory / "captures",
                token=self.token,
            )
            # Do not let initialization extend the suite's original deadline.
            self.reader.deadline = min(self.reader.deadline, self.deadline)
            client = NuGetGitHubClient(
                self.directory / "github", self.request.github, self.token
            )
            self.collector = NuGetProbeCollector(
                self.request.plan, client, self.directory / "probes"
            )
            result = run_nuget_suite(
                self.request.plan,
                self,
                preflight=self.preflight,
                original=self.original,
                witness=self.witness,
            )
            _require(time.monotonic() < self.deadline, "suite completed late")
            result.update(
                requestDigest=canonical_sha256(self.request.to_document()),
                admissionSha256=self.request.admission_sha256,
                githubRequestsCharged=client.requests,
                githubResponseBodyBytesCharged=client.response_bytes,
                files=reads._runtime_files(self.directory),  # noqa: SLF001
            )
            _require(
                time.monotonic() < self.deadline,
                "suite evidence completed late",
            )
            self.evidence.write("suite-observation.json", canonicalize(result))
        except BaseException as error:
            self.evidence.write(
                "suite-failed.json",
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "generationSpent": True,
                        "nativeAdmissionEstablished": False,
                        "atomicAssuranceEstablished": False,
                        "elapsedSeconds": time.monotonic() - started,
                    }
                ),
            )
            raise
        finally:
            self.running = False
        return self.directory / "suite-observation.json"


def main(argv: Sequence[str] | None = None) -> int:
    """Execute the pre-admitted request using an existing token variable."""
    parser = argparse.ArgumentParser(description=__doc__)
    for name in (
        "request",
        "checkout",
        "preflight-evidence",
        "fixture-archive",
        "fixture-audit",
        "helper-archive",
        "helper-audit",
        "restore-host",
        "admission",
        "audit-directory",
    ):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--token-env", required=True)
    arguments = parser.parse_args(argv)
    try:
        request = read_suite_request(
            _read(
                arguments.request.parent,
                arguments.request.name,
                _MANIFEST_LIMIT,
            )
        )
        inputs = NuGetSuiteInputs(
            arguments.checkout,
            arguments.preflight_evidence,
            arguments.fixture_archive,
            arguments.fixture_audit,
            arguments.helper_archive,
            arguments.helper_audit,
            arguments.restore_host,
            arguments.admission,
        )
        operator = NuGetSuiteOperator(
            request,
            inputs,
            arguments.audit_directory,
            os.environ.get(arguments.token_env, ""),
        )
        operator.execute()
    except Exception:  # noqa: BLE001 - no sensitive diagnostics at the CLI boundary
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
