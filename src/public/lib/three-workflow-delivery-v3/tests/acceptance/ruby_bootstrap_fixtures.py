"""Local bootstrap artifacts with modeled admission, never hosted authority."""

from dataclasses import dataclass, replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_digest,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    RubyBootstrapInputs,
    admit_ruby_bootstrap,
    ruby_bootstrap_source_digest,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_preparation import (
    RubyBootstrapPlan,
    RubyBootstrapQualification,
    build_ruby_bootstrap,
    provide_ruby_bootstrap,
    run_ruby_bootstrap_quality,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_publication import (
    PREPARER,
    RubyBootstrapApprovalBundle,
    RubyBootstrapAuthorization,
    RubyBootstrapMarker,
    RubyBootstrapPublication,
    observe_ruby_bootstrap_absence,
    render_ruby_bootstrap_summary,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_runtime import (
    BOOTSTRAP_WORKFLOW,
    RubyBootstrapPhaseBudget,
)
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryReader,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.records.ruby import RubyArtifact
from three_workflow_delivery_v3.release.ruby_configuration import (
    RubyFirstProjectInspection,
)
from three_workflow_delivery_v3.release.ruby_operation import (
    RubyOperationEnvelope,
    RubyOperationRequest,
)
from three_workflow_delivery_v3.release.ruby_operation_ledger import (
    RubyOperationLedger,
    initialize_ruby_operation_ledger,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_ENVELOPE_PATH,
)
from three_workflow_delivery_v3.repository.ruby_model import RUBY_QUALITY
from three_workflow_delivery_v3.repository.ruby_provider import (
    provide_ruby_repository_facts,
)

from ..release.ruby_configuration_fixtures import (
    configuration,
    inspection_document,
    review_document,
)
from ..release.ruby_operation_fixtures import (
    admission_document,
    current_environment,
    envelope_document,
    instant,
    request_document,
)
from ..ruby_fixtures import binding, commit, git, repository
from ..ruby_integration_fixtures import (
    NOW,
    admitted,
    consumer,
    contents,
    context,
)
from ..ruby_registry_fixtures import (
    READ_TOKEN,
    ScriptedTransport,
    inventory,
    json_response,
    metadata,
    native_index,
)

CURRENT = NOW + timedelta(minutes=2)
EXPIRED = NOW + timedelta(hours=2)


def controls(destination="rubygems"):
    """Select modeled bootstrap controls before sealing a source target."""
    config = configuration(destination)
    slot = destination + "-bootstrap"
    doc = envelope_document(slot)
    doc["slots"][slot]["configuration-digest"] = config.digest
    return config, RubyOperationEnvelope(canonicalize(doc))


def run_for_provider(root, config, envelope, provider, tree="d" * 40):
    """Reserve a real local ledger after sealing modeled reviewer inputs."""
    slot = config.registry.name + "-bootstrap"
    target = provider.binding.target
    request_doc = request_document(envelope, slot)
    request_doc.update(
        {
            "target": target,
            "control": target,
            "tree": tree,
            "nbgv": provider.nbgv.to_document(),
            "source-manifest-digest": ruby_bootstrap_source_digest(provider),
            "expires-at": instant(EXPIRED),
        }
    )
    request = RubyOperationRequest(canonicalize(request_doc), envelope)
    inspection_doc = inspection_document(config)
    inspection_doc["target"] = target
    inspected = RubyFirstProjectInspection(canonicalize(inspection_doc), config)
    review = canonicalize(review_document(request, config, inspected))
    root.mkdir(parents=True, exist_ok=True)
    header = initialize_ruby_operation_ledger(root / "ledger", "e" * 32)
    ledger = RubyOperationLedger(root / "ledger")
    admission = canonicalize(
        admission_document(request, ruby_digest(header), review)
    )
    reservation = ledger.reserve(request, admission, review=review, now=CURRENT)
    inputs = RubyBootstrapInputs(
        request, config, inspected, review, admission, reservation
    )
    env = current_environment(request)
    env.update({"GITHUB_SHA": target, "GITHUB_WORKFLOW_SHA": target})
    return admit_ruby_bootstrap(
        inputs, ledger.join_run(request, reservation, 991), env, CURRENT
    )


def transport(
    run,
    payload,
    *,
    artifact_id=201,
    producer="provide-ruby-bootstrap",
    path="provider.json",
):
    """Model archive:false upload identities, without hosted proof."""
    digest = ruby_digest(payload)
    url = f"https://github.com/hcoona/three/actions/runs/{run.run_id}/artifacts/{artifact_id}"
    ref = ArtifactReference(artifact_id, digest, url, path, digest)
    return ref, ArtifactTransportIdentity(
        artifact_id, "bootstrap-original", url, digest, producer, run.run_id, 1
    )


def plan_for_provider(run, provider):
    """Wrap strict Provider bytes in a modeled current-run artifact."""
    payload = canonicalize(
        {
            "schema": "workflow-delivery/v3/ruby-bootstrap-provider-v1",
            "run-binding-digest": run.binding_digest,
            "target-tree": run.request.document["tree"],
            "provider": provider.to_document(),
            "producer": "provide-ruby-bootstrap",
        }
    )
    return RubyBootstrapPlan(run, payload, *transport(run, payload))


def modeled_plan(root, destination="rubygems"):
    """Use real source bytes and modeled native facts for contract mutations."""
    config, envelope = controls(destination)
    source = contents()
    source[RUBY_ENVELOPE_PATH] = envelope.content
    source[config.document["binding"]["configuration-path"]] = config.content
    provider = admitted(context(), source).result
    run = run_for_provider(root, config, envelope, provider)
    return plan_for_provider(
        run, replace(provider, binding=run.provider_binding())
    )


def native_inputs(root, destination="rubygems"):
    """Seal actual Git, NBGV and Ruby facts before bootstrap execution."""
    config, envelope = controls(destination)
    source, origin, _ = repository(root)
    for path, payload in (
        (RUBY_ENVELOPE_PATH, envelope.content),
        (config.document["binding"]["configuration-path"], config.content),
    ):
        destination = origin / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(payload)
    target = commit(origin)
    git(source, "fetch", "origin")
    git(source, "checkout", "--detach", target)
    materialization = CheckoutMaterialization(0, credentials_persisted=False)
    provider = provide_ruby_repository_facts(
        source, binding(target, "destination-bootstrap"), materialization
    )
    run = run_for_provider(
        root,
        config,
        envelope,
        provider,
        git(source, "rev-parse", "HEAD^{tree}"),
    )
    return source, run, materialization


def artifact_for(plan, distribution, *, artifact_id=301):
    """Join one actual original to a modeled immutable current-run upload."""
    ref, identity = transport(
        plan.run,
        distribution.content,
        artifact_id=artifact_id,
        producer="build-ruby",
        path=distribution.filename,
    )
    return RubyArtifact(
        distribution.filename,
        len(distribution.content),
        distribution.witness,
        ref,
        identity,
    )


@dataclass
class BootstrapCase:
    """Actual original and modeled first-project authority for offline tests."""

    publication: object
    original: object
    indexes: dict
    marker: object = None

    @property
    def registry(self):
        """Select the admitted destination profile."""
        return self.publication.run.inputs.configuration.registry

    @property
    def publisher(self):
        """Use the independently reviewed actual hosted publisher job name."""
        return "publish-ruby-bootstrap-" + self.registry.name

    def missing_responses(self):
        """Provide complete controlled coordinate absence."""
        if self.registry.name == "rubygems":
            return [json_response([])]
        return [RubyHttpResponse(200, self.indexes["empty"])] * 2

    def exact_responses(self):
        """Return native metadata/index and exact original archive bytes."""
        if self.registry.name == "rubygems":
            responses = [
                inventory(self.original.witness.nbgv.native_version),
                json_response(metadata(self.original)),
            ]
        else:
            responses = [
                RubyHttpResponse(200, self.indexes["empty"]),
                RubyHttpResponse(200, self.indexes["exact"]),
            ]
        return [*responses, RubyHttpResponse(200, self.original.content)]

    def absent_responses(self):
        """Spend owners absence before the RubyGems native coordinate lookup."""
        owners = (
            [
                RubyHttpResponse(
                    404, b"This rubygem could not be found.", "text/plain"
                )
            ]
            if self.registry.name == "rubygems"
            else []
        )
        return [*owners, *self.missing_responses()]

    def reader(
        self,
        phase,
        native,
        *,
        clock=lambda: CURRENT,
        reserve=lambda *_: None,
        deadline=None,
    ):
        """Construct a real reader over the exact bootstrap partition."""
        budget = RubyBootstrapPhaseBudget(
            self.publication.run,
            phase,
            native,
            reserve,
            deadline=deadline or CURRENT + timedelta(minutes=10),
            clock=clock,
        )
        return RubyRegistryReader(
            self.registry,
            budget,
            github_read_token=READ_TOKEN
            if self.registry.name == "github-packages"
            else None,
        )


def bootstrap_approval_proof(run):
    """Model native bootstrap proof without claiming hosted provenance."""
    registry = run.inputs.configuration.registry
    return {
        "repository": "hcoona/three",
        "run-id": run.run_id,
        "run-attempt": 1,
        "target": run.target,
        "workflow": BOOTSTRAP_WORKFLOW,
        "environment": registry.environment,
        "environment-id": 101,
        "deployment-id": 2001,
        "reviewer-id": 712433,
        "reviewer": "hcoona",
        "state": "approved",
        "sentinel": registry.environment + "/v1",
        "native-response-digest": "sha256:" + "e" * 64,
    }


def bootstrap_marker(case):
    """Join actual original evidence with modeled approved current artifacts."""
    run = case.publication.run
    observed = observe_ruby_bootstrap_absence(
        case.publication,
        case.reader("eligibility", ScriptedTransport(*case.absent_responses())),
        phase="eligibility",
        clock=lambda: CURRENT,
        check_authority=lambda: {"modeled": True},
    )
    absence_ref, absence_transport = transport(
        run,
        canonicalize(observed.to_document()),
        artifact_id=401,
        producer=PREPARER,
        path="absence.json",
    )
    summary_ref, summary_transport = transport(
        run,
        render_ruby_bootstrap_summary(observed),
        artifact_id=402,
        producer=PREPARER,
        path="approval-summary.txt",
    )
    bundle = RubyBootstrapApprovalBundle(
        observed, absence_ref, absence_transport, summary_ref, summary_transport
    )
    authorization = RubyBootstrapAuthorization(
        bundle,
        *transport(
            run,
            canonicalize(bundle.to_document()),
            artifact_id=403,
            producer=PREPARER,
            path="approval-bundle.json",
        ),
        canonicalize(bootstrap_approval_proof(run)),
        CURRENT,
    )
    premarker = observe_ruby_bootstrap_absence(
        case.publication,
        case.reader("pre-marker", ScriptedTransport(*case.absent_responses())),
        phase="pre-marker",
        clock=lambda: CURRENT,
        check_authority=lambda: {"modeled": True},
        authorization=authorization,
    )
    return RubyBootstrapMarker(
        authorization,
        *transport(
            run,
            canonicalize(authorization.to_document()),
            artifact_id=404,
            producer=case.publisher,
            path="authorization.json",
        ),
        premarker,
    )


@pytest.fixture(scope="session")
def bootstrap_cases(tmp_path_factory):
    """Use fresh destination originals and actual native registry parsing."""
    cases = {}
    for destination in ("rubygems", "github-packages"):
        root = tmp_path_factory.mktemp("bootstrap-publication-" + destination)
        source, run, materialization = native_inputs(root, destination)
        payload = provide_ruby_bootstrap(
            source, run, materialization, clock=lambda: CURRENT
        )
        plan = RubyBootstrapPlan(run, payload, *transport(run, payload))
        original = build_ruby_bootstrap(source, plan, clock=lambda: CURRENT)
        artifact = artifact_for(plan, original)
        qualification = RubyBootstrapQualification(
            plan,
            tuple(
                run_ruby_bootstrap_quality(
                    plan,
                    artifact,
                    original.content,
                    definition,
                    clock=lambda: CURRENT,
                    consumer=consumer,
                )
                for definition in RUBY_QUALITY
            ),
        )
        publication = RubyBootstrapPublication(
            qualification,
            *transport(
                run,
                canonicalize(qualification.to_document()),
                artifact_id=400,
                producer="finalize-ruby-bootstrap-qualification",
                path="qualification.json",
            ),
            original.content,
        )
        indexes = {
            "empty": native_index(root, []),
            "exact": native_index(
                root,
                [
                    [
                        RUBY_RELEASE_UNIT,
                        original.witness.nbgv.native_version,
                        "ruby",
                    ]
                ],
            ),
        }
        case = BootstrapCase(publication, original, indexes)
        case.marker = bootstrap_marker(case)
        cases[destination] = case
    return cases
