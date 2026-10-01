"""First-project Ruby admission and native-run binding."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyBootstrapConfiguration,
    RubyFirstProjectInspection,
    validate_ruby_bootstrap_review,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationRequest,
    validate_ruby_operation_reservation,
)
from three_workflow_delivery_v3.repository.node_provider import ProviderBinding

if TYPE_CHECKING:
    from collections.abc import Mapping

    from three_workflow_delivery_v3.records.artifacts import (
        ArtifactReference,
        ArtifactTransportIdentity,
    )
    from three_workflow_delivery_v3.repository.ruby_provider import (
        RubyProviderResult,
    )

BOOTSTRAP_PURPOSE = "destination-bootstrap"
_NATIVE_FIELDS = {
    "GITHUB_REPOSITORY",
    "GITHUB_REF",
    "GITHUB_EVENT_NAME",
    "GITHUB_ACTOR",
    "GITHUB_ACTOR_ID",
    "GITHUB_TRIGGERING_ACTOR",
    "GITHUB_RUN_ATTEMPT",
    "GITHUB_SHA",
    "GITHUB_WORKFLOW_SHA",
    "GITHUB_WORKFLOW_REF",
    "GITHUB_RUN_ID",
}


def require_bootstrap(condition: bool, message: str) -> None:  # noqa: FBT001 - contract assertion
    """Reject a contradictory bootstrap record without coercing its values."""
    if not condition:
        raise ValueError(message)


def _same(actual: JsonValue, expected: JsonValue) -> None:
    require_bootstrap(
        canonicalize(actual) == canonicalize(expected),
        "Ruby bootstrap record differs from its exact binding",
    )


@dataclass(frozen=True, slots=True)
class RubyBootstrapInputs:
    """Exact first-project inputs, separate from normal Governance."""

    request: RubyOperationRequest
    configuration: RubyBootstrapConfiguration
    inspection: RubyFirstProjectInspection
    review: bytes = field(repr=False)
    admission: bytes = field(repr=False)
    reservation: bytes = field(repr=False)

    def require_current(self, now: datetime) -> None:
        """Join the whole review chain and preserve its shortest lifetime."""
        binding = ruby_object(self.request.document["binding"])
        require_bootstrap(
            binding["kind"] == "bootstrap"
            and self.request.document["slot"]
            == self.configuration.registry.name + "-bootstrap",
            "Ruby bootstrap cannot admit a normal operation",
        )
        _same(binding, self.configuration.document["binding"])
        validate_ruby_bootstrap_review(
            self.review,
            self.request,
            self.configuration,
            self.inspection,
            self.admission,
            now=now,
        )
        reservation = validate_ruby_operation_reservation(
            self.reservation, self.request, self.admission, review=self.review
        )
        require_bootstrap(
            datetime.fromisoformat(ruby_text(reservation["reserved-at"]))
            <= now,
            "Ruby bootstrap reservation is not yet current",
        )


@dataclass(frozen=True, slots=True)
class RubyBootstrapRun:
    """Joined first-project request and one actual current native run."""

    inputs: RubyBootstrapInputs
    run_join: bytes
    native_environment: bytes
    admitted_at: datetime

    def __post_init__(self) -> None:
        """Reject a changed run, workflow, purpose or admission."""
        self.require_current(self.admitted_at)

    @property
    def request(self) -> RubyOperationRequest:
        """Return the first-project exact operation request."""
        return self.inputs.request

    @property
    def run_id(self) -> int:
        """Return the native run identity after strict admission."""
        return int(
            ruby_text(
                parse_canonical_json(self.native_environment)["GITHUB_RUN_ID"]
            )
        )

    @property
    def target(self) -> str:
        """Return the identical source and control commit."""
        return ruby_text(self.request.document["target"])

    def require_current(self, now: datetime) -> None:
        """Replay native identity and first-project expiry for every phase."""
        require_bootstrap(
            self.admitted_at.tzinfo is not None
            and now.tzinfo is not None
            and self.admitted_at <= now,
            "Ruby bootstrap phase predates its run admission",
        )
        self.inputs.require_current(now)
        environment = ruby_object(
            parse_canonical_json(self.native_environment), _NATIVE_FIELDS
        )
        native = {key: ruby_text(value) for key, value in environment.items()}
        self.request.require_current(native, now)
        expected: dict[str, JsonValue] = {
            "schema": "workflow-delivery/v3/ruby-operation-run-join-v1",
            "request-digest": self.request.digest,
            "reservation-digest": ruby_digest(self.inputs.reservation),
            "run-id": int(native["GITHUB_RUN_ID"]),
            "run-attempt": 1,
        }
        actual = ruby_object(parse_canonical_json(self.run_join), set(expected))
        _same(actual, expected)

    def to_document(self) -> dict[str, JsonValue]:
        """Export a screened binding without creating publication authority."""
        return {
            "schema": "workflow-delivery/v3/ruby-bootstrap-run-v1",
            "purpose": BOOTSTRAP_PURPOSE,
            "request-digest": self.request.digest,
            "configuration-digest": self.inputs.configuration.digest,
            "inspection-digest": ruby_digest(self.inputs.inspection.content),
            "review-digest": ruby_digest(self.inputs.review),
            "admission-digest": ruby_digest(self.inputs.admission),
            "reservation-digest": ruby_digest(self.inputs.reservation),
            "run-join-digest": ruby_digest(self.run_join),
            "target": self.target,
            "control": self.target,
            "workflow-run-id": self.run_id,
            "run-attempt": 1,
        }

    @property
    def binding_digest(self) -> str:
        """Identify admission and actual run separately from package bytes."""
        return canonical_sha256(self.to_document())

    def provider_binding(self) -> ProviderBinding:
        """Request fresh Provider facts with explicit bootstrap purpose."""
        doc = self.request.document
        return ProviderBinding(
            "ruby-bootstrap:"
            + ruby_text(doc["slot"])
            + ":"
            + ruby_text(doc["generation"]),
            BOOTSTRAP_PURPOSE,
            self.run_id,
            1,
            self.target,
            "provide-ruby-bootstrap",
            f"workflow-delivery-v3:{self.target}",
            catalog_digest(),
            self.request.digest,
        )


def admit_ruby_bootstrap(
    inputs: RubyBootstrapInputs,
    run_join: bytes,
    environment: Mapping[str, str],
    now: datetime,
) -> RubyBootstrapRun:
    """Copy only actual nonsecret native identity fields at the hosted boundary.

    The reviewed caller supplies native environment and independently
    established reviewer/ledger/run provenance. Supplied strings and digests
    do not prove those external facts. Every job performs its own admission.
    """
    selected: dict[str, JsonValue] = {
        key: environment.get(key) for key in _NATIVE_FIELDS
    }
    return RubyBootstrapRun(inputs, run_join, canonicalize(selected), now)


def ruby_bootstrap_source_digest(provider: RubyProviderResult) -> str:
    """Hash canonical ordered Provider input pairs for the sealed request."""
    return canonical_sha256(
        cast(
            "JsonValue",
            [[path, digest] for path, digest in provider.source_input_manifest],
        )
    )


def validate_bootstrap_provider(
    run: RubyBootstrapRun, provider: RubyProviderResult
) -> None:
    """Compare newly derived native facts to the exact first-project request."""
    require_bootstrap(
        provider.binding == run.provider_binding(),
        "Ruby bootstrap Provider belongs to another purpose or run",
    )
    _same(provider.nbgv.to_document(), run.request.document["nbgv"])
    _same(
        ruby_bootstrap_source_digest(provider),
        run.request.document["source-manifest-digest"],
    )
    # RubyProviderResult independently checks native/compression profile and
    # full-history target metadata; no normal Model is fabricated here.


def require_bootstrap_transport(  # noqa: PLR0913 - exact current artifact edge
    run: RubyBootstrapRun,
    reference: ArtifactReference,
    transport: ArtifactTransportIdentity,
    payload: bytes,
    *,
    producer: str,
    payload_path: str,
) -> None:
    """Join archive:false originals with current uploaded producer identity."""
    expected_url = (
        f"https://github.com/hcoona/three/actions/runs/{run.run_id}"
        f"/artifacts/{reference.artifact_id}"
    )
    require_bootstrap(
        reference.payload_path == payload_path
        and reference.payload_digest == ruby_digest(payload)
        and reference.artifact_digest == reference.payload_digest
        and reference.artifact_id == transport.artifact_id
        and reference.artifact_digest == transport.transport_digest
        and reference.artifact_url == transport.artifact_url == expected_url
        and transport.producer == producer
        and transport.workflow_run_id == run.run_id
        and transport.run_attempt == 1,
        "Ruby bootstrap immutable transport differs from the current run",
    )
