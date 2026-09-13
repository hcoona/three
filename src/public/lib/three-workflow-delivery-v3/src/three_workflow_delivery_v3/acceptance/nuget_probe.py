"""One bound NuGet acceptance invocation, distinct from native admission."""

from __future__ import annotations

import argparse
import base64
import hashlib
import os
import re
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path, PurePosixPath
from tempfile import TemporaryDirectory
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.nuget_capture import (
    _RESPONSE_HEADERS,
)
from three_workflow_delivery_v3.acceptance.nuget_operator import _Evidence, _git
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    COMPLETION_SCHEMA,
    NuGetPreparationRequest,
    _archive_files,
    materialize_helper,
    read_uploaded,
    write_archive,
)
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.adapters.dotnet import (
    dotnet_package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.repository.dotnet_provider import run_native

if TYPE_CHECKING:
    from collections.abc import Mapping

WORKFLOW_PATH = (
    ".github/workflows/workflow-delivery-v3-native-nuget-acceptance.yml"
)
SPEC_SCHEMA = "workflow-delivery/v3/nuget-probe-spec"
REQUEST_SCHEMA = "workflow-delivery/v3/nuget-probe-request"
INPUT_SCHEMA = "workflow-delivery/v3/nuget-probe-input"
RESULT_SCHEMA = "workflow-delivery/v3/nuget-probe-result"
SCENARIOS = ("create", "identical-duplicate", "equivalent-duplicate")
_SERVICE_INDEX_LIMIT = 65536
_DISPATCH_INPUT_LIMIT = 65535
_HELPER_ROOT = "src/private/app/workflow-delivery-v3-dotnet-provider/"
_SOURCE = (
    "src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3"
)


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _object(value: JsonValue) -> dict[str, JsonValue]:
    _require(type(value) is dict, "probe requires an object")
    return cast("dict[str, JsonValue]", value)


def _text(value: JsonValue) -> str:
    _require(type(value) is str, "probe requires text")
    return cast("str", value)


def _integer(value: JsonValue) -> int:
    _require(
        type(value) is int and value > 0, "probe requires a positive integer"
    )
    return cast("int", value)


def _digest(value: object, length: int = 64) -> None:
    _require(
        type(value) is str
        and re.fullmatch(rf"[0-9a-f]{{{length}}}", value) is not None,
        "invalid probe identity digest",
    )


@dataclass(frozen=True)
class NuGetProbeRequest:
    """One current run bound to separately admitted original inputs.

    The referenced audits and captures remain external admission prerequisites.
    Their hashes identify evidence; they neither authenticate its provenance
    nor authorize an invocation. The protected operator closes those gates.
    """

    generation: str
    scenario: str
    tooling_sha: str
    workflow_run_id: int
    fixture_tooling_sha: str
    fixture_run_id: int
    target: str
    fixture_reference: ArtifactReference
    helper_reference: ArtifactReference
    helper_source_inputs: tuple[tuple[str, str], ...]
    fixture_audit_sha256: str
    preflight_sha256: str
    before_capture_sha256: str
    service_index: bytes
    inspection: dict[str, JsonValue]
    profile: dict[str, JsonValue]

    def __post_init__(self) -> None:
        """Close the selected subject and original dependency identities."""
        _require(
            type(self.generation) is str
            and re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", self.generation)
            is not None,
            "invalid probe generation",
        )
        _require(self.scenario in SCENARIOS, "unsupported NuGet probe scenario")
        for value in (self.tooling_sha, self.fixture_tooling_sha, self.target):
            _digest(value, 40)
        for value in (
            self.fixture_audit_sha256,
            self.preflight_sha256,
            self.before_capture_sha256,
        ):
            _digest(value)
        _integer(self.workflow_run_id)
        _integer(self.fixture_run_id)
        for reference in (self.fixture_reference, self.helper_reference):
            _require(
                type(reference) is ArtifactReference
                and re.fullmatch(
                    r"[A-Za-z0-9][A-Za-z0-9._-]{0,240}", reference.payload_path
                )
                is not None
                and reference.artifact_digest == reference.payload_digest
                and reference.artifact_url
                == "https://github.com/hcoona/three/actions/runs/"
                + str(self.fixture_run_id)
                + "/artifacts/"
                + str(reference.artifact_id),
                "probe requires exact original raw artifact references",
            )
        _require(
            self.fixture_reference.artifact_id
            != self.helper_reference.artifact_id,
            "fixture and helper transports must be distinct",
        )
        paths: set[str] = set()
        for name, digest in self.helper_source_inputs:
            relative = PurePosixPath(name)
            _require(
                bool(name)
                and not relative.is_absolute()
                and relative.as_posix() == name
                and ".." not in relative.parts
                and "\\" not in name
                and name not in paths,
                "unsafe or repeated probe helper source input",
            )
            _digest(digest)
            paths.add(name)
        _require(
            {
                _HELPER_ROOT + "Program.cs",
                _HELPER_ROOT + "WorkflowDeliveryV3DotnetProvider.csproj",
                _HELPER_ROOT + "packages.lock.json",
            }.issubset(paths),
            "missing probe helper source inputs; independent audit required",
        )
        _require(
            type(self.inspection) is dict
            and set(self.inspection) == {"original", "comparison", "witness"},
            "incomplete original fixture inspection",
        )
        _require(
            type(self.service_index) is bytes
            and 0 < len(self.service_index) <= _SERVICE_INDEX_LIMIT,
            "missing or oversized retained service index",
        )
        native.validate_nuget_operation_profile(self.profile)
        _require(
            self.profile["platform"] == "Windows",
            "probe requires Windows profile",
        )

    def to_document(self) -> dict[str, JsonValue]:
        """Identify the current probe without certifying a native generation."""
        return {
            "schema": REQUEST_SCHEMA,
            "repository": native.NUGET_REPOSITORY,
            "repositoryId": 1102295886,
            "actorId": 712433,
            "workflowPath": WORKFLOW_PATH,
            "workflowRunId": self.workflow_run_id,
            "runAttempt": 1,
            "generation": self.generation,
            "scenario": self.scenario,
            "toolingSha": self.tooling_sha,
            "packageId": native.NUGET_PACKAGE_ID,
            "containerId": 12024661,
            "fixture": {
                "producerToolingSha": self.fixture_tooling_sha,
                "producerRunId": self.fixture_run_id,
                "target": self.target,
                "reference": self.fixture_reference.to_document(),
                "helperReference": self.helper_reference.to_document(),
                "helperSourceInputs": dict(self.helper_source_inputs),
                "independentAuditSha256": self.fixture_audit_sha256,
                "inspection": self.inspection,
            },
            "preflightSha256": self.preflight_sha256,
            "beforeCaptureSha256": self.before_capture_sha256,
            "serviceIndexBase64": base64.b64encode(self.service_index).decode(
                "ascii"
            ),
            "operationProfile": self.profile,
            "maximumPublicationInvocations": 1,
            "publicationRetries": 0,
            "retentionDays": 45,
            "purpose": "destination-acceptance",
        }

    @property
    def request_digest(self) -> str:
        """Bind the complete current run and original-input request."""
        return canonical_sha256(self.to_document())

    @property
    def selected_fixture(self) -> str:
        """Use A for create/identical and B only for equivalent duplicate."""
        return (
            "comparison"
            if self.scenario == "equivalent-duplicate"
            else "original"
        )


def read_probe_request(content: bytes) -> NuGetProbeRequest:
    """Reject open, malformed or noncanonical current-run requests."""
    value = _object(parse_canonical_json(content))
    fixture = _object(value["fixture"])
    request = NuGetProbeRequest(
        _text(value["generation"]),
        _text(value["scenario"]),
        _text(value["toolingSha"]),
        _integer(value["workflowRunId"]),
        _text(fixture["producerToolingSha"]),
        _integer(fixture["producerRunId"]),
        _text(fixture["target"]),
        artifact_reference_from_document(_object(fixture["reference"])),
        artifact_reference_from_document(_object(fixture["helperReference"])),
        tuple(
            (name, _text(digest))
            for name, digest in _object(fixture["helperSourceInputs"]).items()
        ),
        _text(fixture["independentAuditSha256"]),
        _text(value["preflightSha256"]),
        _text(value["beforeCaptureSha256"]),
        base64.b64decode(_text(value["serviceIndexBase64"]), validate=True),
        _object(fixture["inspection"]),
        _object(value["operationProfile"]),
    )
    _require(
        content == canonicalize(request.to_document()),
        "probe request closure mismatch",
    )
    return request


def bind_probe_spec(
    content: bytes, platform: Mapping[str, str]
) -> NuGetProbeRequest:
    """Bind a closed dispatch specification to its observed current run.

    A prospective spec cannot know its future GitHub run ID. This mechanical
    binding adds that identity without changing the admitted inputs or effects.
    """
    _require(
        len(content) <= _DISPATCH_INPUT_LIMIT,
        "probe spec exceeds dispatch input bound",
    )
    document = _object(parse_canonical_json(content))
    _require(
        document.get("schema") == SPEC_SCHEMA
        and "workflowRunId" not in document,
        "probe dispatch requires a prospective specification",
    )
    document["schema"] = REQUEST_SCHEMA
    document["workflowRunId"] = int(platform.get("GITHUB_RUN_ID", "0"))
    request = read_probe_request(canonicalize(document))
    admit_probe_platform(request, platform)
    return request


def admit_probe_platform(
    request: NuGetProbeRequest, platform: Mapping[str, str]
) -> None:
    """Compare supplied Actions facts without claiming API provenance."""
    expected = {
        "GITHUB_REPOSITORY": native.NUGET_REPOSITORY,
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW_PATH}@refs/heads/main",
        "GITHUB_RUN_ID": str(request.workflow_run_id),
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }
    _require(
        all(platform.get(key) == value for key, value in expected.items()),
        "NuGet probe platform binding mismatch",
    )


def _sha(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _entry_names(value: JsonValue) -> tuple[str, ...]:
    # The official helper validates paths and duplicate entries. Preserve
    # multiplicity here while ignoring the original ZIP's entry ordering.
    _require(type(value) is list, "probe requires an entry sequence")
    return tuple(sorted(_text(name) for name in cast("list[JsonValue]", value)))


def prepare_probe_inputs(
    request: NuGetProbeRequest,
    *,
    platform: Mapping[str, str],
    fixture_path: Path,
    helper: native.NuGetAuthority,
    output: Path,
) -> None:
    """Inspect original inputs without evaluating or rebuilding a target.

    The unprivileged protected entry supplies the admitted complete helper.
    Original preparation/audit provenance stays distinct from this inspection.
    """
    admit_probe_platform(request, platform)
    files = _archive_files(
        read_uploaded(fixture_path, request.fixture_reference)
    )
    preparation_bytes = files.pop("preparation.json")
    preparation = _object(parse_canonical_json(preparation_bytes))
    original_request = _object(preparation["request"])
    expected = NuGetPreparationRequest(
        request.fixture_tooling_sha,
        request.target,
        _text(original_request["generation"]),
        request.fixture_run_id,
    )
    _require(
        preparation.get("schema") == COMPLETION_SCHEMA
        and original_request == expected.to_document()
        and preparation.get("helper-artifact")
        == request.helper_reference.to_document()
        and preparation.get("inspection") == request.inspection
        and preparation.get("files")
        == {name: "sha256:" + _sha(content) for name, content in files.items()},
        "probe fixture preparation or original helper lineage mismatch",
    )
    witness = dotnet_package_target_witness_from_document(
        request.inspection["witness"]
    )
    _require(
        witness.target == request.target
        and witness.purpose == "destination-acceptance",
        "probe requires the original acceptance target witness",
    )
    packages: dict[str, native.NuGetPackageInspection] = {}
    for role in ("original", "comparison"):
        declared = _object(request.inspection[role])
        basename = _text(declared["basename"])
        _require(
            PurePosixPath(basename).name == basename and "\\" not in basename,
            "unsafe original fixture basename",
        )
        content = files[role + "/" + basename]
        _require(
            "sha256:" + _sha(content) == declared.get("sha256")
            and "sha512:" + hashlib.sha512(content).hexdigest()
            == declared.get("sha512")
            and len(content) == declared.get("size"),
            "original fixture bytes changed",
        )
        package = native.inspect_nuget_package(helper, content)
        _require(
            package.official_facts.get("identity") == declared.get("identity")
            and _entry_names(package.official_facts.get("entries"))
            == _entry_names(declared.get("entries"))
            and package.witness == witness.canonical_bytes,
            "probe official inspection differs from the admitted fixture",
        )
        packages[role] = package
    original, comparison = packages["original"], packages["comparison"]
    identity = native.normalize_nuget_identity(
        helper, native.NUGET_PACKAGE_ID, witness.nbgv.nuget_package_version
    )
    comparison_version = witness.nbgv.nuget_package_version
    comparison_version += "." if "+" in comparison_version else "+"
    comparison_version += "wdv3fixture." + expected.generation
    _require(
        original.identity == identity
        and comparison.identity.coordinate == identity.coordinate
        and comparison.identity.display_package_id
        == native.NUGET_PACKAGE_ID.lower()
        and comparison.identity.display_version == comparison_version
        and original.sha256 != comparison.sha256
        and all(
            original.official_facts.get(key)
            == comparison.official_facts.get(key)
            for key in ("assembly", "repository", "frameworks", "dependencies")
        ),
        "probe original and equivalent fixture identities disagree",
    )
    resources = native.discover_nuget_resources(helper, request.service_index)
    profile = native.nuget_operation_profile(resources)
    _require(profile == request.profile, "probe preparation profile mismatch")
    selected = packages[request.selected_fixture]
    outputs = {
        "request.json": canonicalize(request.to_document()),
        "package.nupkg": selected.content,
        "witness.json": selected.witness,
        "original-inspection.json": canonicalize(original.official_facts),
        "comparison-inspection.json": canonicalize(comparison.official_facts),
        "operation-profile.json": canonicalize(profile),
        "resources.json": canonicalize(
            {
                "packageBaseAddress": resources.package_base_address,
                "packagePublish": resources.package_publish,
                "indexSha256": resources.index_sha256,
            }
        ),
    }
    outputs["prepared.json"] = canonicalize(
        {
            "schema": INPUT_SCHEMA,
            "requestDigest": request.request_digest,
            "selectedFixture": request.selected_fixture,
            "coordinate": selected.identity.coordinate,
            "packageSha256": selected.sha256,
            "packageSha512": selected.sha512,
            "witnessSha256": selected.witness_sha256,
            "files": {
                name: "sha256:" + _sha(content)
                for name, content in outputs.items()
            },
            "evidenceLevel": (
                "current local inspection; independent native audit required"
            ),
        }
    )
    write_archive(output, outputs)


def _prepared_input(
    path: Path,
    reference: ArtifactReference,
    platform: Mapping[str, str],
) -> tuple[NuGetProbeRequest, bytes, native.NuGetServiceResources]:
    files = _archive_files(read_uploaded(path, reference))
    _require(
        set(files)
        == {
            "prepared.json",
            "request.json",
            "package.nupkg",
            "witness.json",
            "original-inspection.json",
            "comparison-inspection.json",
            "operation-profile.json",
            "resources.json",
        },
        "unexpected probe prepared files",
    )
    document = _object(parse_canonical_json(files.pop("prepared.json")))
    request = read_probe_request(files["request.json"])
    admit_probe_platform(request, platform)
    _require(
        reference.artifact_url
        == "https://github.com/hcoona/three/actions/runs/"
        + str(request.workflow_run_id)
        + "/artifacts/"
        + str(reference.artifact_id),
        "probe preparation is not from the current run",
    )
    selected = _object(request.inspection[request.selected_fixture])
    identity = _object(selected["identity"])
    package = files["package.nupkg"]
    witness = canonicalize(request.inspection["witness"])
    expected = {
        "schema": INPUT_SCHEMA,
        "requestDigest": request.request_digest,
        "selectedFixture": request.selected_fixture,
        "coordinate": _text(identity["normalizedPackageId"])
        + "@"
        + _text(identity["normalizedVersion"]),
        "packageSha256": _sha(package),
        "packageSha512": hashlib.sha512(package).hexdigest(),
        "witnessSha256": _sha(witness),
        "files": {
            name: "sha256:" + _sha(content) for name, content in files.items()
        },
        "evidenceLevel": (
            "current local inspection; independent native audit required"
        ),
    }
    _require(
        document == expected
        and "sha256:" + _sha(package) == selected.get("sha256")
        and "sha512:" + hashlib.sha512(package).hexdigest()
        == selected.get("sha512")
        and len(package) == selected.get("size")
        and files["witness.json"] == witness
        and files["operation-profile.json"] == canonicalize(request.profile),
        "probe prepared input binding mismatch",
    )
    resources_document = _object(parse_canonical_json(files["resources.json"]))
    _require(
        set(resources_document)
        == {"packageBaseAddress", "packagePublish", "indexSha256"}
        and resources_document["packagePublish"]
        == request.profile["packagePublish"]
        and resources_document["indexSha256"] == _sha(request.service_index),
        "probe prepared resource binding mismatch",
    )
    resources = native.NuGetServiceResources(
        _text(resources_document["packageBaseAddress"]),
        _text(resources_document["packagePublish"]),
        _text(resources_document["indexSha256"]),
    )
    _require(
        native.nuget_operation_profile(resources) == request.profile,
        "probe actual publication profile changed",
    )
    return request, package, resources


def publish_probe(
    *,
    prepared_path: Path,
    prepared_reference: ArtifactReference,
    platform: Mapping[str, str],
    token: str,
    directory: Path,
) -> Path:
    """Consume one current prepared input after a durable local attempt marker.

    Only the protected publisher supplies its Actions-issued repository token.
    This function performs no capture, helper, target execution or retry. A
    complete probe record is not a suite verdict or a normal-Live Outcome.
    """
    directory.mkdir(parents=True, exist_ok=False)
    evidence = _Evidence(directory, token)
    invocation_started = False
    started = time.monotonic()
    try:
        request, package, resources = _prepared_input(
            prepared_path, prepared_reference, platform
        )
        evidence.write("request.json", canonicalize(request.to_document()))
        evidence.write(
            "prepared-reference.json",
            canonicalize(prepared_reference.to_document()),
        )
        profile_digest = canonical_sha256(request.profile)
        marker = canonicalize(
            {
                "schema": "workflow-delivery/v3/nuget-probe-invocation-marker",
                "requestDigest": request.request_digest,
                "workflowRunId": request.workflow_run_id,
                "runAttempt": 1,
                "profileDigest": profile_digest,
                "packageSha256": _sha(package),
                "preparedReference": prepared_reference.to_document(),
                "startedAt": datetime.now(UTC).isoformat(),
                "mutationMayHaveStarted": True,
                "retryPermitted": False,
            }
        )
        evidence.check(marker)
        with (directory / "invocation-started.json").open("xb") as stream:
            stream.write(marker)
            stream.flush()
            os.fsync(stream.fileno())
        invocation_started = True
        invocation = native.publish_nuget_once(
            resources=resources,
            package=package,
            token=token,
            expected_profile_sha256=profile_digest,
            expected_package_sha256=_sha(package),
        )
        _digest(invocation.request_body_sha256)
        _require(
            invocation.profile_sha256 == profile_digest
            and invocation.package_sha256 == _sha(package),
            "probe invocation result binding mismatch",
        )
        response_document: JsonValue = None
        response = invocation.response
        if response is not None:
            _require(
                invocation.error_kind is None
                and response.url == resources.package_publish
                and len(response.body)
                <= cast("int", request.profile["maximumResponseBytes"]),
                "probe response is outside the admitted profile",
            )
            response_document = {
                "url": response.url,
                "status": response.status,
                "headers": [
                    [name.lower(), value]
                    for name, value in response.headers
                    if name.lower() in _RESPONSE_HEADERS
                ],
                "body": "response.body",
                "bodySha256": _sha(response.body),
                "bodyBytes": len(response.body),
            }
            # Check all retained response data before writing any of it.
            evidence.check(canonicalize(response_document))
            evidence.check(response.body)
            evidence.write("response.body", response.body)
            evidence.write("response.json", canonicalize(response_document))
        else:
            _require(
                invocation.error_kind is not None,
                "probe invocation has no outcome",
            )
        result = {
            "schema": RESULT_SCHEMA,
            "requestDigest": request.request_digest,
            "workflowRunId": request.workflow_run_id,
            "runAttempt": 1,
            "scenario": request.scenario,
            "profileDigest": profile_digest,
            "packageSha256": invocation.package_sha256,
            "requestBodySha256": invocation.request_body_sha256,
            "response": response_document,
            "transportError": invocation.error_kind is not None,
            "httpCreated": invocation.definitive_success,
            "possiblyMutated": invocation.possibly_mutated,
            "invocations": 1,
            "elapsedSeconds": time.monotonic() - started,
            "completedAt": datetime.now(UTC).isoformat(),
            "requestSpent": True,
            "retryPermitted": False,
            "evidenceLevel": (
                "one invocation only; complete captures, consumer and "
                "independent native audit remain required"
            ),
        }
        evidence.write("result.json", canonicalize(result))
    except BaseException as error:
        evidence.write(
            "failure.json",
            canonicalize(
                {
                    "errorType": type(error).__name__,
                    "invocationMayHaveStarted": invocation_started,
                    "requestSpent": True,
                    "retryPermitted": False,
                    "elapsedSeconds": time.monotonic() - started,
                    "nativeAdmissionEstablished": False,
                }
            ),
        )
        raise
    return directory / "result.json"


def admit_probe_source(root: Path, request: NuGetProbeRequest) -> None:
    """Compare actual imported bytes and original helper dependency Git blobs.

    The Windows entry checks out LF bytes with a process-local Git override.
    Do not silently normalize changed runtime bytes into a different profile.
    This comparison does not replace the original helper's independent audit.
    """
    _require(
        _git(root, "rev-parse", "HEAD").decode().strip() == request.tooling_sha
        and not _git(root, "status", "--porcelain", "--untracked-files=all"),
        "probe requires its exact clean checkout",
    )
    package = root / _SOURCE
    _require(
        Path(__file__).resolve().is_relative_to(package.resolve()),
        "probe imported outside its pinned checkout",
    )
    for path in package.rglob("*.py"):
        relative = path.relative_to(root).as_posix()
        _require(
            not path.is_symlink()
            and path.read_bytes()
            == _git(root, "show", f"{request.tooling_sha}:{relative}"),
            "probe source bytes mismatch",
        )
    for name, digest in request.helper_source_inputs:
        original = _git(root, "show", f"{request.fixture_tooling_sha}:{name}")
        _require(
            _sha(original) == digest
            and _git(root, "show", f"{request.tooling_sha}:{name}") == original,
            "probe helper dependency source compatibility mismatch",
        )


class _LoggedHelper:
    """Retain unprivileged inspection commands through the native runner.

    The runner's 300-second command timeout and the Windows job deadline apply.
    This is local inspection of admitted inputs, not a destination collector
    or a claim of a bounded native-capture request.
    """

    def __init__(self, dll: Path, directory: Path) -> None:
        self.dll = dll
        self.directory = directory
        self.calls = 0

    def _invoke(self, *arguments: str) -> dict[str, JsonValue]:
        self.calls += 1
        return _object(
            parse_json_strict(
                run_native(
                    ("dotnet", str(self.dll), *arguments),
                    self.dll.parent,
                    diagnostics=self.directory / f"helper-{self.calls:02d}",
                )
            )
        )

    def normalize_identity(
        self, package_id: str, version: str
    ) -> dict[str, JsonValue]:
        return self._invoke("normalize-identity", package_id, version)

    def _file(self, operation: str, content: bytes) -> dict[str, JsonValue]:
        with TemporaryDirectory(prefix="wdv3-probe-inspection-") as temporary:
            path = Path(temporary) / "input"
            path.write_bytes(content)
            return self._invoke(operation, str(path))

    def inspect_package(self, content: bytes) -> dict[str, JsonValue]:
        return self._file("inspect-package", content)

    def service_resources(self, index: bytes) -> dict[str, JsonValue]:
        return self._file("service-resources", index)


def main(argv: list[str] | None = None) -> int:
    """Run a protected probe stage; request files supply no authorization."""
    parser = argparse.ArgumentParser(description=__doc__)
    stages = parser.add_subparsers(dest="stage", required=True)
    bind = stages.add_parser("bind-spec")
    bind.add_argument("--spec", type=Path, required=True)
    bind.add_argument("--output", type=Path, required=True)
    prepare = stages.add_parser("prepare")
    prepare.add_argument("--request", type=Path, required=True)
    prepare.add_argument("--fixture", type=Path, required=True)
    prepare.add_argument("--helper", type=Path, required=True)
    prepare.add_argument("--output", type=Path, required=True)
    publish = stages.add_parser("publish")
    publish.add_argument("--prepared", type=Path, required=True)
    publish.add_argument("--reference", type=Path, required=True)
    for stage in (bind, prepare, publish):
        stage.add_argument("--checkout", type=Path, required=True)
        stage.add_argument("--evidence", type=Path, required=True)
    arguments = parser.parse_args(argv)
    platform = dict(os.environ)
    directory: Path = arguments.evidence
    try:
        if arguments.stage == "bind-spec":
            directory.mkdir(parents=True, exist_ok=False)
            request = bind_probe_spec(arguments.spec.read_bytes(), platform)
            admit_probe_source(arguments.checkout.resolve(strict=True), request)
            with arguments.output.open("xb") as stream:
                stream.write(canonicalize(request.to_document()))
        elif arguments.stage == "prepare":
            directory.mkdir(parents=True, exist_ok=False)
            request = read_probe_request(arguments.request.read_bytes())
            admit_probe_platform(request, platform)
            admit_probe_source(arguments.checkout.resolve(strict=True), request)
            helper = materialize_helper(
                arguments.helper,
                request.helper_reference,
                directory / "helper-runtime",
            )
            prepare_probe_inputs(
                request,
                platform=platform,
                fixture_path=arguments.fixture,
                helper=_LoggedHelper(helper.helper_dll, directory),
                output=arguments.output,
            )
        else:
            reference = artifact_reference_from_document(
                _object(parse_json_strict(arguments.reference.read_bytes()))
            )
            request, _, _ = _prepared_input(
                arguments.prepared, reference, platform
            )
            admit_probe_source(arguments.checkout.resolve(strict=True), request)
            result_path = publish_probe(
                prepared_path=arguments.prepared,
                prepared_reference=reference,
                platform=platform,
                token=platform.get("GITHUB_TOKEN", ""),
                directory=directory,
            )
            result = _object(parse_canonical_json(result_path.read_bytes()))
            response = result["response"]
            # The expected HTTP outcome is necessary, never a suite verdict.
            expected_status = 201 if request.scenario == "create" else 409
            return (
                0
                if (
                    type(response) is dict
                    and response.get("status") == expected_status
                    and result["transportError"] is False
                )
                else 1
            )
    except Exception as error:  # noqa: BLE001
        # Never print raw errors or credential-bearing subprocess diagnostics.
        directory.mkdir(parents=True, exist_ok=True)
        failure = directory / "stage-failure.json"
        if not failure.exists():
            failure.write_bytes(
                canonicalize(
                    {
                        "errorType": type(error).__name__,
                        "requestSpent": True,
                        "retryPermitted": False,
                        "nativeAdmissionEstablished": False,
                    }
                )
            )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
