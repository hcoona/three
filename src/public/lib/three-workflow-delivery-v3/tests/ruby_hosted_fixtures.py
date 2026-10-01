"""Actual local ledgers and bytes with explicitly modeled hosted boundaries."""

from dataclasses import dataclass
from pathlib import Path

from three_workflow_delivery_v3 import (
    ruby_operation_cli,
    ruby_operation_host,
    ruby_operation_runtime,
)
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.release.ruby_governance import (
    ruby_governance_path,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
)
from three_workflow_delivery_v3.release.ruby_operation_ledger import (
    RubyOperationLedger,
    initialize_ruby_operation_ledger,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)
from three_workflow_delivery_v3.ruby_operation_host import HostedRubyArtifacts

from .acceptance.ruby_bootstrap_fixtures import CURRENT, modeled_plan
from .release.ruby_operation_fixtures import (
    admission_document,
    current_environment,
    envelope_document,
    request_document,
)
from .ruby_integration_fixtures import governance


def reseal_normal(case, source, provider, tree, ledger_root):
    """Join actual native facts to a fresh modeled review and local ledger."""
    document = case.request.document
    from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (  # noqa: PLC0415
        ruby_bootstrap_source_digest,
    )

    document.update(
        {
            "target": provider.binding.target,
            "control": provider.binding.target,
            "tree": tree,
            "nbgv": provider.nbgv.to_document(),
            "source-manifest-digest": ruby_bootstrap_source_digest(provider),
        }
    )
    selected = RubyOperationRequest(
        canonicalize(document), case.request.envelope
    )
    review = {**case.raw["review"], "request-digest": selected.digest}
    header = initialize_ruby_operation_ledger(ledger_root, "8" * 32)
    admission = admission_document(
        selected, ruby_digest(header), canonicalize(review)
    )
    reservation = RubyOperationLedger(ledger_root).reserve(
        selected,
        canonicalize(admission),
        review=canonicalize(review),
        now=CURRENT,
    )
    case.root = source
    case.request = selected
    case.raw = {
        "request": selected.document,
        "review": review,
        "admission": admission,
        "reservation": parse_json_strict(reservation),
        "inspection": None,
    }


@dataclass
class HostedCase:
    """Keep source authority separate from downloaded artifacts."""

    root: Path
    directory: Path
    request: object
    raw: dict
    references: dict
    native_run: object = None

    def env(self):
        """Model only native inputs; these are not independent provenance."""
        environment = current_environment(self.request)
        environment.update(
            {
                "GITHUB_SHA": self.request.document["target"],
                "GITHUB_WORKFLOW_SHA": self.request.document["target"],
                "GITHUB_JOB": "request-ruby-operation",
                "GITHUB_OUTPUT": str(self.directory / "outputs"),
                "WDV3_SELECTED_DESTINATION": self.request.document["binding"][
                    "destination"
                ],
                "WDV3_OPERATION_INPUT": canonicalize(self.raw).decode(),
                "WDV3_REFERENCES": canonicalize(self.references).decode(),
                "GITHUB_TOKEN": "synthetic-job-token",
            }
        )
        return environment

    def install(self, monkeypatch):
        """Set one exact run in all actual hosted command boundaries."""
        for key, value in self.env().items():
            monkeypatch.setenv(key, value)
        for module in (
            ruby_operation_cli,
            ruby_operation_host,
            ruby_operation_runtime,
        ):
            monkeypatch.setattr(module, "now", lambda: CURRENT)

    def inputs(self):
        """Use the production immutable-edge reader over real local files."""
        return HostedRubyArtifacts(
            self.root, self.directory, canonicalize(self.references).decode()
        )

    def add(self, role, value, *, filename=None):
        """Model exact raw upload identity without hosted provenance."""
        payload = value if isinstance(value, bytes) else canonicalize(value)
        name = filename or role + ".json"
        (self.directory / name).write_bytes(payload)
        identity = 1001 + len(self.references)
        digest = ruby_digest(payload)
        reference = ArtifactReference(
            identity,
            digest,
            f"https://github.com/hcoona/three/actions/runs/991/artifacts/{identity}",
            name,
            digest,
        )
        self.references[role] = reference.to_document()
        return reference

    def admit(self, monkeypatch):
        """Run canonical native admission and retain its exact bytes."""
        self.install(monkeypatch)
        operation = ruby_operation_host.admit_operation_input(
            self.root, canonicalize(self.raw).decode()
        )
        self.add("operation", operation.content)
        monkeypatch.setenv(
            "WDV3_REFERENCES", canonicalize(self.references).decode()
        )
        return operation


def bootstrap_case_from_run(root, directory, run):
    """Adapt an existing modeled admission with its actual source and ledger."""
    directory.mkdir(parents=True, exist_ok=True)
    inputs = run.inputs
    raw = {
        "request": run.request.document,
        "review": parse_json_strict(inputs.review),
        "admission": parse_json_strict(inputs.admission),
        "reservation": parse_json_strict(inputs.reservation),
        "inspection": parse_json_strict(inputs.inspection.content),
    }
    return HostedCase(root, directory, run.request, raw, {}, run)


def hosted_case(root, *, destination="rubygems", bootstrap=True):
    """Build coherent admission without sockets, accounts or service effects."""
    source = root / "source"
    source.mkdir(parents=True)
    directory = root / "artifacts"
    directory.mkdir()
    if bootstrap:
        plan = modeled_plan(root / "modeled", destination)
        run = plan.run
        config = run.inputs.configuration
        for path, content in (
            (RUBY_ENVELOPE_PATH, run.request.envelope.content),
            (config.document["binding"]["configuration-path"], config.content),
        ):
            target = source / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(content)
        return bootstrap_case_from_run(source, directory, run)
    gov = governance(destination)
    slot = destination + "-normal01"
    envelope_doc = envelope_document(slot)
    envelope_doc["slots"][slot]["configuration-digest"] = gov.digest
    envelope = RubyOperationEnvelope(canonicalize(envelope_doc))
    selected = RubyOperationRequest(
        canonicalize(request_document(envelope, slot)), envelope
    )
    header = initialize_ruby_operation_ledger(root / "ledger", "f" * 32)
    admission = admission_document(selected, ruby_digest(header))
    review = {
        "schema": "workflow-delivery/v3/ruby-operation-admission-review-v1",
        "request-digest": selected.digest,
        "configuration-digest": gov.digest,
        "first-project-inspection-digest": None,
        "caller-digest": selected.document["caller-digest"],
        "protocol-digest": selected.document["protocol-digest"],
        **{
            key: admission[key]
            for key in (
                "reviewer",
                "author",
                "reviewed-at",
                "verdict",
                "carrier",
            )
        },
    }
    admission = admission_document(
        selected, ruby_digest(header), canonicalize(review)
    )
    ledger = RubyOperationLedger(root / "ledger")
    reservation = ledger.reserve(
        selected,
        canonicalize(admission),
        review=canonicalize(review),
        now=CURRENT,
    )
    for path, payload in (
        (RUBY_ENVELOPE_PATH, envelope.content),
        (ruby_governance_path(gov.registry), gov.content),
    ):
        target = source / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(payload)
    raw = {
        "request": selected.document,
        "review": review,
        "admission": admission,
        "reservation": parse_json_strict(reservation),
        "inspection": None,
    }
    return HostedCase(source, directory, selected, raw, {})
