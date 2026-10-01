"""Concrete native operation admission and immutable hosted artifact edges."""

from __future__ import annotations

import os
from dataclasses import dataclass, replace
from datetime import UTC, datetime, timedelta
from pathlib import Path

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    RubyBootstrapInputs,
    RubyBootstrapRun,
    admit_ruby_bootstrap,
    require_bootstrap,
)
from three_workflow_delivery_v3.adapters.rubygems import (
    RUBY_INDEX_LIMIT,
    RubyRegistry,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    RubyFirstProjectInspection,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RubyGovernance,
    ruby_publisher_job,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
    validate_ruby_operation_admission,
    validate_ruby_operation_reservation,
)
from three_workflow_delivery_v3.repository.compiler import CompilationContext
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)

# All ownership is derived from reviewed workflow topology, never a payload.
COMMON_ROLES = {
    "operation": "request-ruby-operation",
    "provider-request": "request-ruby-operation",
    "provider": "discover-ruby",
    "model": "plan-ruby-release",
    "plan": "plan-ruby-release",
    "gem": "build-ruby",
    "artifacts": "qualify-ruby-release",
    "quality": "qualify-ruby-release",
    "qualification": "finalize-ruby-qualification",
    "phase-eligibility": "prepare-ruby-publication",
    "absence": "prepare-ruby-publication",
    "observation": "prepare-ruby-publication",
    "publication": "prepare-ruby-publication",
    "summary": "prepare-ruby-publication",
    "bundle": "prepare-ruby-publication",
    "phase-zero-action": "finalize-attempt",
    "exact-proof": "finalize-attempt",
    "zero-governance": "finalize-attempt",
    "audit-prepare": "prepare-ruby-publication",
    "audit-finalize": "finalize-attempt",
    "audit-remote": "verify-ruby-operation",
    "outcome": "finalize-attempt",
    "terminal": "finalize-attempt",
    "phase-remote-consumer": "verify-ruby-operation",
    "remote-consumer": "verify-ruby-operation",
}
BOOTSTRAP_ROLES = {
    "provider": "provide-ruby-bootstrap",
    "plan": "plan-ruby-bootstrap",
    "artifacts": "qualify-ruby-bootstrap",
    "quality": "qualify-ruby-bootstrap",
    "qualification": "finalize-ruby-bootstrap-qualification",
    "phase-eligibility": "prepare-ruby-bootstrap-publication",
    "absence": "prepare-ruby-bootstrap-publication",
    "summary": "prepare-ruby-bootstrap-publication",
    "bundle": "prepare-ruby-bootstrap-publication",
    "terminal": "finalize-ruby-bootstrap",
    "audit-prepare": "prepare-ruby-bootstrap-publication",
    "audit-finalize": "finalize-ruby-bootstrap",
    "audit-remote": "verify-ruby-bootstrap-remote",
    "phase-remote-consumer": "verify-ruby-bootstrap-remote",
    "remote-consumer": "verify-ruby-bootstrap-remote",
}
PUBLISHER_ROLES = {
    "audit-publisher",
    "authorization",
    "marker",
    "result",
    "fresh-governance",
    "phase-pre-marker",
    "phase-execute",
}


def now() -> datetime:
    """Read aware UTC at every active boundary."""
    return datetime.now(UTC)


def write(path: Path, document: JsonValue, *, exclusive: bool = False) -> None:
    """Retain canonical bounded data before passing it to upload-artifact."""
    content = canonicalize(document)
    require_bootstrap(
        0 < len(content) <= RUBY_INDEX_LIMIT,
        "Ruby operation record exceeds its bound",
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb" if exclusive else "wb") as stream:
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def outputs(values: dict[str, str]) -> None:
    """Expose only exact single-line workflow outputs."""
    with Path(os.environ["GITHUB_OUTPUT"]).open(
        "a", encoding="utf-8"
    ) as stream:
        for key, value in values.items():
            require_bootstrap(
                not any(c in value for c in "\r\n"),
                "Ruby operation output is multiline",
            )
            stream.write(f"{key}={value}\n")


@dataclass(frozen=True, slots=True)
class HostedRubyOperation:
    """Actual run joined to independently reviewed canonical request bytes."""

    content: bytes
    root: Path
    admitted_at: datetime

    def __post_init__(self) -> None:
        """Validate source controls without inferring reviewer provenance."""
        doc = ruby_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "request",
                "review",
                "admission",
                "reservation",
                "inspection",
                "run-join",
                "native-admitted-at",
            },
        )
        require_bootstrap(
            doc["schema"] == "workflow-delivery/v3/ruby-hosted-operation-v1",
            "Unknown Ruby hosted operation",
        )
        self.require_current(self.admitted_at)

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return canonical independently admitted inputs."""
        return parse_canonical_json(self.content)

    @property
    def request(self) -> RubyOperationRequest:
        """Read protected envelope from the exact checked-out target."""
        return RubyOperationRequest(
            canonicalize(self.document["request"]),
            RubyOperationEnvelope(
                (self.root / RUBY_ENVELOPE_PATH).read_bytes()
            ),
        )

    @property
    def binding(self) -> dict[str, JsonValue]:
        """Select a closed destination/workflow/kind tuple."""
        return ruby_object(self.request.document["binding"])

    @property
    def registry(self) -> RubyRegistry:
        """Select one destination from protected admission."""
        return RubyRegistry(ruby_text(self.binding["destination"]))

    @property
    def bootstrap(self) -> bool:
        """Keep first-project and normal authority distinct."""
        return self.binding["kind"] == "bootstrap"

    @property
    def target(self) -> str:
        """One target is both source and workflow/control."""
        return ruby_text(self.request.document["target"])

    @property
    def run_id(self) -> int:
        """Return the current actual native run, never an input selector."""
        return int(os.environ["GITHUB_RUN_ID"])

    @property
    def request_id(self) -> str:
        """Bind normal Release identity to the exact immutable request bytes."""
        if not self.bootstrap:
            return "release-request:" + self.request.digest.removeprefix(
                "sha256:"
            )
        return (
            "ruby-operation:"
            + ruby_text(self.request.document["slot"])
            + ":"
            + ruby_text(self.request.document["generation"])
        )

    def bootstrap_run(self, instant: datetime) -> RubyBootstrapRun:
        """Admit first-project configuration without normal Governance."""
        doc = self.document
        configuration = RubyBootstrapConfiguration(
            self.registry,
            (
                self.root / ruby_text(self.binding["configuration-path"])
            ).read_bytes(),
        )
        admitted = admit_ruby_bootstrap(
            RubyBootstrapInputs(
                self.request,
                configuration,
                RubyFirstProjectInspection(
                    canonicalize(doc["inspection"]), configuration
                ),
                canonicalize(doc["review"]),
                canonicalize(doc["admission"]),
                canonicalize(doc["reservation"]),
            ),
            canonicalize(doc["run-join"]),
            os.environ,
            instant,
        )

        return replace(
            admitted,
            admitted_at=datetime.fromisoformat(
                ruby_text(doc["native-admitted-at"])
            ),
        )

    def governance(self, instant: datetime) -> RubyGovernance:
        """Admit protected normal Governance with the concrete seven-day cap."""
        require_bootstrap(
            not self.bootstrap, "Bootstrap cannot use normal Governance"
        )
        value = RubyGovernance(
            self.registry,
            (
                self.root / ruby_text(self.binding["configuration-path"])
            ).read_bytes(),
            self.target,
            instant,
        )
        value.require_live(instant)
        doc = value.document
        require_bootstrap(
            value.digest == self.request.document["configuration-digest"]
            and datetime.fromisoformat(ruby_text(doc["expires-at"]))
            <= datetime.fromisoformat(ruby_text(doc["inspected-at"]))
            + timedelta(days=7),
            "Ruby normal configuration differs from its bounded request",
        )
        return value

    def require_current(self, instant: datetime) -> None:
        """Close actual native identity, review, reservation and run join."""
        request, doc = self.request, self.document
        request.require_current(os.environ, instant)
        review, admission, reservation = (
            canonicalize(doc[key])
            for key in ("review", "admission", "reservation")
        )
        admitted = validate_ruby_operation_admission(
            admission, request, review=review
        )
        reserved = validate_ruby_operation_reservation(
            reservation, request, admission, review=review
        )
        require_bootstrap(
            datetime.fromisoformat(ruby_text(reserved["reserved-at"]))
            <= instant,
            "Ruby operation reservation is not yet current",
        )
        expected: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
            "request-digest": request.digest,
            "reservation-digest": ruby_digest(reservation),
            "run-id": self.run_id,
            "run-attempt": 1,
        }
        require_bootstrap(
            canonicalize(doc["run-join"]) == canonicalize(expected),
            "Ruby actual run differs from its reservation join",
        )
        if self.bootstrap:
            self.bootstrap_run(instant)
            return
        require_bootstrap(
            doc["inspection"] is None,
            "Normal admission cannot reuse first-project inspection",
        )
        self.governance(instant)
        expected_review: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/ruby-operation-admission-review-v1",
            "request-digest": request.digest,
            "configuration-digest": request.document["configuration-digest"],
            "first-project-inspection-digest": None,
            "caller-digest": request.document["caller-digest"],
            "protocol-digest": request.document["protocol-digest"],
            **{
                key: admitted[key]
                for key in (
                    "reviewer",
                    "author",
                    "reviewed-at",
                    "verdict",
                    "carrier",
                )
            },
        }
        require_bootstrap(
            canonicalize(doc["review"]) == canonicalize(expected_review)
            and datetime.fromisoformat(ruby_text(admitted["reviewed-at"]))
            <= instant,
            "Ruby normal admission review differs from its exact request",
        )

    def context(self) -> CompilationContext:
        """Preserve normal domain run_attempt=None with native attempt one."""
        require_bootstrap(
            not self.bootstrap, "Bootstrap has its distinct frozen plan"
        )
        return CompilationContext(
            self.request_id,
            "live-release",
            self.run_id,
            None,
            self.target,
            "plan",
            f"workflow-delivery-v3:{self.target}",
            catalog_digest(),
        )

    def deadline(self) -> datetime:
        """Cap each active hosted phase by the absolute operation expiry."""
        return min(
            now() + timedelta(minutes=30),
            datetime.fromisoformat(
                ruby_text(self.request.document["expires-at"])
            ),
        )


def admit_operation_input(root: Path, content: str) -> HostedRubyOperation:
    """Form the actual native run join once in the request job."""
    raw = ruby_object(
        parse_json_strict(content),
        {"request", "review", "admission", "reservation", "inspection"},
    )
    require_bootstrap(
        canonicalize(raw).decode() == content,
        "Ruby operation input must be canonical exact bytes",
    )
    request = RubyOperationRequest(
        canonicalize(raw["request"]),
        RubyOperationEnvelope((root / RUBY_ENVELOPE_PATH).read_bytes()),
    )
    request.require_current(os.environ, now())
    return HostedRubyOperation(
        canonicalize(
            {
                "schema": "workflow-delivery/v3/ruby-hosted-operation-v1",
                **raw,
                "native-admitted-at": now().isoformat(),
                "run-join": {
                    "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
                    "request-digest": request.digest,
                    "reservation-digest": canonical_sha256(raw["reservation"]),
                    "run-id": int(os.environ["GITHUB_RUN_ID"]),
                    "run-attempt": 1,
                },
            }
        ),
        root,
        now(),
    )


class HostedRubyArtifacts:
    """Explicit immutable current-DAG artifacts; missing IDs never pass."""

    def __init__(self, root: Path, directory: Path, references: str) -> None:
        """Bind the actual run before admitting any downloaded payload."""
        self.root, self.directory = root, directory
        self.references = ruby_object(parse_json_strict(references))
        require_bootstrap(
            not set(self.references) - (COMMON_ROLES.keys() | PUBLISHER_ROLES),
            "Unknown Ruby operation artifact role",
        )
        ids = [self.reference(role).artifact_id for role in self.references]
        paths = [self.reference(role).payload_path for role in self.references]
        require_bootstrap(
            len(ids) == len(set(ids)) and len(paths) == len(set(paths)),
            "Ruby operation artifact IDs or paths alias different roles",
        )

    def reference(self, role: str) -> ArtifactReference:
        """Require the exact current-run artifact URL and nonempty ID."""
        value = artifact_reference_from_document(self.references[role])
        require_bootstrap(
            value.artifact_url
            == f"https://github.com/hcoona/three/actions/runs/{os.environ['GITHUB_RUN_ID']}/artifacts/{value.artifact_id}",
            "Ruby artifact is not from this native run",
        )
        return value

    def content(self, role: str) -> bytes:
        """Require an actual downloaded raw file with both exact digests."""
        reference = self.reference(role)
        path = self.directory / reference.payload_path
        require_bootstrap(
            path.is_file()
            and not path.is_symlink()
            and 0 < path.stat().st_size <= RUBY_INDEX_LIMIT,
            "Ruby required artifact is absent or excessive",
        )
        content = path.read_bytes()
        require_bootstrap(
            reference.artifact_digest
            == reference.payload_digest
            == ruby_digest(content),
            "Ruby raw artifact digest changed",
        )
        return content

    def document(self, role: str) -> dict[str, JsonValue]:
        """Decode only canonical object records after immutable admission."""
        return parse_canonical_json(self.content(role))

    def operation(self) -> HostedRubyOperation:
        """Admit exact current native request before any registry access."""
        return HostedRubyOperation(self.content("operation"), self.root, now())

    def historical_operation(self) -> HostedRubyOperation:
        """Admit retained authority at its original native admission time."""
        content = self.content("operation")
        instant = datetime.fromisoformat(
            ruby_text(parse_canonical_json(content)["native-admitted-at"])
        )
        return HostedRubyOperation(content, self.root, instant)

    def producer(self, role: str) -> str:
        """Resolve the fixed concrete job for this admitted operation."""
        if role in PUBLISHER_ROLES:
            operation = self.historical_operation()
            return ruby_publisher_job(
                operation.registry, bootstrap=operation.bootstrap
            )
        native_workflow = os.environ["GITHUB_WORKFLOW_REF"]
        prefix = "hcoona/three/.github/workflows/workflow-delivery-v3-ruby-"
        require_bootstrap(
            native_workflow
            in {
                prefix + "bootstrap.yml@refs/heads/main",
                prefix + "smoke.yml@refs/heads/main",
            },
            "Ruby artifact producer is outside the native operation workflow",
        )
        if (
            role in BOOTSTRAP_ROLES
            and native_workflow == prefix + "bootstrap.yml@refs/heads/main"
        ):
            return BOOTSTRAP_ROLES[role]
        return COMMON_ROLES[role]

    def transport(self, role: str) -> ArtifactTransportIdentity:
        """Actual uploaded role ownership, not content-selected producers."""
        reference = self.reference(role)
        operation = self.historical_operation()
        return ArtifactTransportIdentity(
            reference.artifact_id,
            reference.payload_path,
            reference.artifact_url,
            reference.artifact_digest,
            self.producer(role),
            operation.run_id,
            1 if operation.bootstrap else None,
        )

    def validate_all(self) -> None:
        """Reject missing-ID download success before executing a stage."""
        for role in self.references:
            self.content(role)

    def export(self) -> None:
        """Expose current explicit edges without adding discovery authority."""
        outputs(
            {
                "references": canonicalize(self.references).decode(),
                "artifact-ids": ",".join(
                    str(self.reference(role).artifact_id)
                    for role in self.references
                ),
            }
        )

    def bind(
        self, role: str, path: Path, identity: str, digest: str, url: str
    ) -> None:
        """Bind upload readback once under the actual concrete producer job."""
        require_bootstrap(
            role in COMMON_ROLES or role in PUBLISHER_ROLES,
            "Unknown Ruby upload role",
        )
        require_bootstrap(
            role not in self.references,
            "Ruby operation upload role is already spent",
        )
        if not digest.startswith("sha256:"):
            digest = "sha256:" + digest
        content = path.read_bytes()
        require_bootstrap(
            0 < len(content) <= RUBY_INDEX_LIMIT, "Ruby upload size is invalid"
        )
        reference = ArtifactReference(
            int(identity), digest, url, path.name, ruby_digest(content)
        )
        require_bootstrap(
            reference.artifact_digest == reference.payload_digest,
            "Ruby upload readback digest differs",
        )
        self.references[role] = reference.to_document()
        require_bootstrap(
            os.environ["GITHUB_JOB"] == self.producer(role),
            "Ruby upload role has a different actual producer",
        )
        self.reference(role)
        self.export()
        write(self.directory / "references.json", self.references)
