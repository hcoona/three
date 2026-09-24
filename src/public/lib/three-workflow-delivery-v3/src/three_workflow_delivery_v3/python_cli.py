"""Hosted Python stages over explicit immutable current-DAG artifact outputs."""

from __future__ import annotations

import argparse
import os
import subprocess
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpsTransport,
    PythonRegistry,
    mint_python_token,
    read_python_index,
)
from three_workflow_delivery_v3.adapters.python import (
    build_python_distributions,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.ci.python import (
    PythonCiDecision,
    PythonCiPlan,
    python_ci_evidence_from_document,
    python_ci_plan_from_document,
    run_python_ci_quality,
)
from three_workflow_delivery_v3.platform.python_github import (
    PythonGitHubRuntime,
    obtain_python_oidc_assertion,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.python import (
    PythonArtifact,
    python_artifact_from_document,
)
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
    _release_intent,
)
from three_workflow_delivery_v3.release.attempt_finalizer import (
    finalize_attempt_outcome,
)
from three_workflow_delivery_v3.release.identity import _request_id
from three_workflow_delivery_v3.release.python_finalizer import (
    PythonExactSatisfiedProof,
    PythonFinalizationInputs,
)
from three_workflow_delivery_v3.release.python_governance import (
    PYTHON_WORKFLOW,
    PythonGovernance,
)
from three_workflow_delivery_v3.release.python_publication import (
    PythonApprovalBundle,
    PythonMutationMarker,
    PythonPublicationAuthorization,
    PythonPublicationSnapshot,
    PythonRemoteObservation,
    execute_python_publication,
    python_publication_result_from_document,
    render_python_approval_summary,
)
from three_workflow_delivery_v3.release.python_qualification import (
    PythonQualificationDecision,
    PythonQualificationSnapshot,
    qualify_python_release,
)
from three_workflow_delivery_v3.release.python_transport import (
    admit_python_publication_snapshot,
    admit_python_qualification_decision,
    python_approval_bundle_from_document,
    python_authorization_from_document,
    python_marker_from_document,
    python_native_observation_from_document,
    python_qualification_evidence_from_document,
    python_qualification_snapshot_from_document,
    python_remote_observation_from_document,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    _compilation_context_from_document,
    provider_binding,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.python_model import (
    PythonRepositoryModelSnapshot,
    admit_python_provider_facts,
    compile_python_repository_model,
    python_provider_manifest,
    python_repository_model_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    provide_python_repository_facts,
    python_digest,
    python_text,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.repository.compiler import (
        ProviderRequestManifest,
    )

# Static current-DAG producer ownership, not payload-selected permissions.
_PRODUCERS = {
    "request": "request-python",
    "intent": "request-python",
    "governance": "request-python",
    "provider": "discover-python",
    "model": "compile-python",
    "ci-plan": "plan-python-ci",
    "qualification": "plan-python-release",
    "wheel": "build-python",
    "sdist": "build-python",
    "build-report": "build-python",
    "artifacts": "qualify-python",
    "quality": "qualify-python",
    "decision": "qualify-python",
    "observation": "prepare-python-publication",
    "publication": "prepare-python-publication",
    "summary": "prepare-python-publication",
    "bundle": "prepare-python-publication",
    "exact-proof": "finalize-attempt",
    "authorization": "publish-python",
    "marker": "publish-python",
    "result": "publish-python",
    "outcome": "finalize-attempt",
}


def _outputs(values: dict[str, str]) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with Path(path).open("a", encoding="utf-8") as stream:
            for name, value in values.items():
                if "\n" in value or "\r" in value:
                    message = "Python workflow output must be a single line"
                    raise ValueError(message)
                stream.write(f"{name}={value}\n")


def _write(path: Path, document: JsonValue) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonicalize(document))


def _current(purpose: str) -> tuple[str, int, int | None]:
    target = os.environ["GITHUB_SHA"]
    run = int(os.environ["GITHUB_RUN_ID"])
    attempt = int(os.environ["GITHUB_RUN_ATTEMPT"])
    if os.environ["GITHUB_REPOSITORY"] != "hcoona/three":
        message = "Python hosted stages require the accepted repository"
        raise ValueError(message)
    if purpose == "live-release":
        expected_ref = f"hcoona/three/{PYTHON_WORKFLOW}@refs/heads/main"
        if (
            attempt != 1
            or os.environ["GITHUB_EVENT_NAME"] != "workflow_dispatch"
            or os.environ["GITHUB_REF"] != "refs/heads/main"
            or os.environ["GITHUB_ACTOR"] != "hcoona"
            or os.environ["GITHUB_WORKFLOW_SHA"] != target
            or os.environ["GITHUB_WORKFLOW_REF"] != expected_ref
        ):
            message = "Python Live requires a new protected-main owner dispatch"
            raise ValueError(message)
        return target, run, None
    if purpose not in {"ci-pr-slice-shadow", "slice-validation"}:
        message = "Python hosted purpose is unsupported"
        raise ValueError(message)
    if purpose == "ci-pr-slice-shadow" and (
        os.environ["GITHUB_EVENT_NAME"] != "pull_request"
        or not os.environ["GITHUB_REF"].startswith("refs/pull/")
        or not os.environ["GITHUB_REF"].endswith("/merge")
    ):
        message = "Python PR CI requires the tested merge candidate"
        raise ValueError(message)
    return target, run, attempt


class PythonInputs:
    """Explicit artifact references from trusted current workflow edges."""

    def __init__(self, directory: Path, purpose: str, references: str) -> None:
        """Bind platform identity before reading record payloads."""
        self.directory = directory
        self.purpose = purpose
        self.target, self.run, self.run_attempt = _current(purpose)
        document = parse_json_strict(references)
        if not isinstance(document, dict) or set(document) - _PRODUCERS.keys():
            message = "Python artifact map has unknown roles"
            raise ValueError(message)
        self.references = document

    def reference(self, role: str) -> ArtifactReference:
        """Read only one named current-DAG reference, never list or discover."""
        return artifact_reference_from_document(self.references[role])

    def content(self, role: str) -> bytes:
        """Admit archive:false action output bytes against both digests."""
        reference = self.reference(role)
        content = (self.directory / reference.payload_path).read_bytes()
        if (
            python_digest(content) != reference.payload_digest
            or reference.artifact_digest != reference.payload_digest
        ):
            message = "Python downloaded artifact differs from immutable output"
            raise ValueError(message)
        return content

    def document(self, role: str) -> JsonValue:
        """Read strict canonical business data after transport admission."""
        content = self.content(role)
        document = parse_json_strict(content)
        if canonicalize(document) != content:
            message = "Python artifact JSON is not canonical"
            raise ValueError(message)
        return document

    def transport(self, role: str) -> ArtifactTransportIdentity:
        """Bind actual workflow upload outputs to this producer/run contract."""
        reference = self.reference(role)
        return ArtifactTransportIdentity(
            reference.artifact_id,
            reference.payload_path,
            reference.artifact_url,
            reference.artifact_digest,
            _PRODUCERS[role],
            self.run,
            self.run_attempt,
        )

    def model(self) -> PythonRepositoryModelSnapshot:
        """Admit the Model against current platform identity and request."""
        model = python_repository_model_from_document(self.document("model"))
        if (
            model.context.target,
            model.context.workflow_run_id,
            model.context.run_attempt,
            model.context.purpose,
        ) != (
            self.target,
            self.run,
            self.run_attempt,
            self.purpose,
        ) or model.context.control != f"workflow-delivery-v3:{self.target}":
            message = "Python Model is not the current workflow's snapshot"
            raise ValueError(message)
        return model

    def artifacts(self) -> tuple[PythonArtifact, ...]:
        """Load the independently transported original pair."""
        document = self.document("artifacts")
        if not isinstance(document, list):
            message = "Python artifact records must be an array"
            raise TypeError(message)
        artifacts = tuple(python_artifact_from_document(a) for a in document)
        for artifact, role in zip(artifacts, ("wheel", "sdist"), strict=True):
            if artifact.reference != self.reference(
                role
            ) or artifact.transport != self.transport(role):
                message = "Python original transport differs from current DAG"
                raise ValueError(message)
            artifact.inspect(self.content(role))
        return artifacts

    def qualification(self) -> PythonQualificationSnapshot:
        """Replay the Release first Snapshot against current immutable Model."""
        snapshot = python_qualification_snapshot_from_document(
            self.document("qualification")
        )
        if (
            snapshot.model != self.model()
            or snapshot.model_reference != self.reference("model")
            or snapshot.intent.to_document() != self.document("intent")
        ):
            message = (
                "Python Qualification predecessors differ from current DAG"
            )
            raise ValueError(message)
        return snapshot

    def decision(self) -> PythonQualificationDecision:
        """Replay the Decision from its three explicit Evidence records."""
        evidence = self.document("quality")
        if not isinstance(evidence, list):
            message = "Python Evidence transport must be an array"
            raise TypeError(message)
        decision = admit_python_qualification_decision(
            self.document("decision"),
            self.qualification(),
            tuple(
                python_qualification_evidence_from_document(e) for e in evidence
            ),
        )
        if (
            decision.result == "passed"
            and decision.artifacts != self.artifacts()
        ):
            message = "Python Decision adopted another original artifact set"
            raise ValueError(message)
        return decision

    def observation(self) -> PythonRemoteObservation:
        """Resolve only the immutable observed file identities."""
        decision = self.decision()
        originals = tuple(
            a.inspect(self.content(a.variant)) for a in decision.artifacts
        )
        return python_remote_observation_from_document(
            self.document("observation"),
            decision,
            self.reference("decision"),
            originals,
        )

    def publication(self) -> PythonPublicationSnapshot:
        """Replay one action or zero actions from exact observed predecessor."""
        return admit_python_publication_snapshot(
            self.document("publication"),
            self.observation(),
            self.reference("observation"),
        )

    def bundle(self) -> PythonApprovalBundle:
        """Require actual summary bytes and Bundle linkage."""
        bundle = python_approval_bundle_from_document(
            self.document("bundle"),
            self.publication(),
            self.reference("publication"),
        )
        if bundle.summary_reference != self.reference(
            "summary"
        ) or bundle.summary != self.content("summary"):
            message = "Python Approval summary transport mismatch"
            raise ValueError(message)
        return bundle

    def authorization(self) -> PythonPublicationAuthorization:
        """Admit persisted Authorization of the exact current Bundle."""
        return python_authorization_from_document(
            self.document("authorization"),
            self.bundle(),
            self.reference("bundle"),
        )

    def marker(self) -> PythonMutationMarker:
        """Admit the durable marker through its explicit Authorization."""
        return python_marker_from_document(
            self.document("marker"),
            self.authorization(),
            self.reference("authorization"),
        )


def _github() -> PythonGitHubRuntime:
    return PythonGitHubRuntime(
        os.environ["GITHUB_TOKEN"], PythonHttpsTransport()
    )


def _request(arguments: argparse.Namespace, inputs: PythonInputs) -> None:
    if inputs.purpose == "live-release":
        registry = PythonRegistry(arguments.registry)
        governance = _github().governance(registry)
        intent = ReleaseIntent(
            "hcoona/three",
            PYTHON_WORKFLOW,
            "refs/heads/main",
            inputs.target,
            _request_id("hcoona/three", PYTHON_WORKFLOW, inputs.run),
            "hcoona",
            inputs.run,
            "workflow_dispatch",
            "refs/heads/main",
            inputs.target,
            registry.channel,
            "live",
            "live-release",
            PYTHON_RELEASE_UNIT,
        )
        _write(inputs.directory / "intent.json", intent.to_document())
        _write(
            inputs.directory / "governance.json",
            {
                "registry": registry.name,
                "content": governance.document,
                "source-commit": governance.source_commit,
                "observed-at": governance.observed_at.isoformat(),
            },
        )
        request_id = intent.request_id
    else:
        request_id = f"python-ci:{inputs.run}:{inputs.run_attempt}"
    context = CompilationContext(
        request_id,
        inputs.purpose,
        inputs.run,
        inputs.run_attempt,
        inputs.target,
        "plan",
        f"workflow-delivery-v3:{inputs.target}",
        catalog_digest(),
    )
    _write(arguments.output, python_provider_manifest(context).to_document())


def _manifest(inputs: PythonInputs) -> ProviderRequestManifest:
    document = inputs.document("request")
    if not isinstance(document, dict):
        message = "Python Provider request must be an object"
        raise TypeError(message)
    context = _compilation_context_from_document(document["context"])
    manifest = python_provider_manifest(context)
    if canonicalize(manifest.to_document()) != canonicalize(document) or (
        context.target,
        context.workflow_run_id,
        context.run_attempt,
        context.purpose,
    ) != (inputs.target, inputs.run, inputs.run_attempt, inputs.purpose):
        message = (
            "Python Provider request differs from current platform context"
        )
        raise ValueError(message)
    return manifest


def _build(arguments: argparse.Namespace, inputs: PythonInputs) -> None:
    result = build_python_distributions(
        Path(arguments.repo_root), inputs.model().build_request()
    )
    for distribution in result.distributions:
        path = inputs.directory / distribution.filename
        path.write_bytes(distribution.content)
        _outputs({distribution.variant + "-path": str(path)})
    _write(
        arguments.output,
        {
            "staged-manifest-digest": result.staged_manifest_digest,
            "producer-versions": result.producer_versions.decode(),
            "commands": [
                parse_canonical_json(c) for c in result.command_evidence
            ],
        },
    )


def _artifact_records(
    arguments: argparse.Namespace, inputs: PythonInputs
) -> None:
    witness = inputs.model().build_request().witness
    artifacts: list[JsonValue] = []
    for variant in ("wheel", "sdist"):
        reference = inputs.reference(variant)
        artifact = PythonArtifact(
            variant,
            reference.payload_path,
            len(inputs.content(variant)),
            witness,
            reference,
            inputs.transport(variant),
        )
        artifact.inspect(inputs.content(variant))
        artifacts.append(artifact.to_document())
    _write(arguments.output, artifacts)


def _plan(arguments: argparse.Namespace, inputs: PythonInputs) -> None:
    model = inputs.model()
    if inputs.purpose == "live-release":
        doc = cast("dict[str, JsonValue]", inputs.document("governance"))
        governance = PythonGovernance(
            PythonRegistry(python_text(doc["registry"])),
            canonicalize(doc["content"]),
            python_text(doc["source-commit"]),
            datetime.fromisoformat(python_text(doc["observed-at"])),
        )
        snapshot = PythonQualificationSnapshot(
            _release_intent(inputs.document("intent")),
            model,
            inputs.reference("model"),
            governance,
        )
        _write(arguments.output, snapshot.to_document())
        _outputs({"selected": "true"})
    else:
        # Hosted Python CI deliberately selects full scope at the exact target.
        plan = PythonCiPlan(model, inputs.reference("model"), None)
        _write(arguments.output, plan.to_document())
        _outputs({"selected": str(plan.selected).lower()})


def _quality(arguments: argparse.Namespace, inputs: PythonInputs) -> None:
    artifacts = inputs.artifacts()
    payloads = (inputs.content("wheel"), inputs.content("sdist"))
    if inputs.purpose == "live-release":
        evidence = qualify_python_release(
            inputs.qualification(), artifacts, payloads
        )
    else:
        plan = python_ci_plan_from_document(inputs.document("ci-plan"))
        evidence = run_python_ci_quality(plan, artifacts, payloads)
    _write(arguments.output, [e.to_document() for e in evidence])


def _decision(arguments: argparse.Namespace, inputs: PythonInputs) -> bool:
    evidence = (
        inputs.document("quality") if "quality" in inputs.references else []
    )
    if not isinstance(evidence, list):
        message = "Python qualification Evidence must be an array"
        raise TypeError(message)
    if inputs.purpose == "live-release":
        decision = PythonQualificationDecision(
            inputs.qualification(),
            tuple(
                python_qualification_evidence_from_document(e) for e in evidence
            ),
        )
    else:
        decision = PythonCiDecision(
            python_ci_plan_from_document(inputs.document("ci-plan")),
            tuple(python_ci_evidence_from_document(e) for e in evidence),
        )
    _write(arguments.output, decision.to_document())
    _outputs({"qualification-result": decision.result})
    return decision.result in {"passed", "empty"}


def _bind(arguments: argparse.Namespace, inputs: PythonInputs) -> None:
    if os.environ["GITHUB_JOB"] != _PRODUCERS[arguments.role]:
        message = "Python upload role belongs to another workflow producer"
        raise ValueError(message)
    path = Path(arguments.payload)
    content = path.read_bytes()
    digest = arguments.artifact_digest
    if not digest.startswith("sha256:"):
        digest = "sha256:" + digest
    reference = ArtifactReference(
        arguments.artifact_id,
        digest,
        arguments.artifact_url,
        path.name,
        python_digest(content),
    )
    if (
        reference.artifact_digest != reference.payload_digest
        or arguments.role in inputs.references
    ):
        message = "Python immutable upload digest mismatch or role replacement"
        raise ValueError(message)
    inputs.references[arguments.role] = reference.to_document()
    _outputs(
        {
            "references": canonicalize(inputs.references).decode(),
            "artifact-ids": ",".join(
                str(artifact_reference_from_document(v).artifact_id)
                for v in inputs.references.values()
            ),
        }
    )
    _write(arguments.output, inputs.references)


def _publication(  # noqa: C901, PLR0915 - closed hosted stage dispatch
    arguments: argparse.Namespace, inputs: PythonInputs
) -> None:
    command = arguments.command
    if command == "observe":
        decision = inputs.decision()
        _github().governance(
            decision.snapshot.governance.registry,
            initial=decision.snapshot.governance,
        )
        native = read_python_index(
            decision.snapshot.governance.registry,
            decision.artifacts[0].witness,
            PythonHttpsTransport(),
        )
        record = PythonRemoteObservation(
            decision, inputs.reference("decision"), native, datetime.now(UTC)
        )
    elif command == "prepare":
        record = PythonPublicationSnapshot(
            inputs.observation(), inputs.reference("observation")
        )
        _outputs(
            {
                "action-required": str(record.action_required).lower(),
                "environment": record.registry.environment,
            }
        )
    elif command == "summary":
        # The summary is deterministic before its transport exists.
        summary = render_python_approval_summary(inputs.publication())
        arguments.output.write_bytes(summary)
        if path := os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(path).open("ab") as stream:
                stream.write(summary)
        return
    elif command == "bundle":
        record = PythonApprovalBundle(
            inputs.publication(),
            inputs.reference("publication"),
            inputs.reference("summary"),
        )
    elif command == "authorize":
        bundle = inputs.bundle()
        initial = bundle.snapshot.observation.decision.snapshot.governance
        github = _github()
        fresh = github.governance(initial.registry, initial=initial)
        proof = github.approval(
            bundle.snapshot.observation.decision.snapshot.intent,
            fresh,
            sentinel=os.environ["WDV3_APPROVAL_ENVIRONMENT_MARKER"],
        )
        record = PythonPublicationAuthorization(
            bundle, inputs.reference("bundle"), proof, datetime.now(UTC)
        )
    elif command == "token":
        authorization = inputs.authorization()
        snapshot = authorization.bundle.snapshot.observation.decision.snapshot
        initial = snapshot.governance
        _github().governance(initial.registry, initial=initial)
        transport = PythonHttpsTransport()
        assertion = obtain_python_oidc_assertion(
            initial.registry.name, os.environ, transport
        )
        token = mint_python_token(initial.registry, assertion, transport)
        print("::add-mask::" + token)  # noqa: T201 - GitHub masking command
        with Path(os.environ["GITHUB_ENV"]).open(
            "a", encoding="utf-8"
        ) as stream:
            stream.write("WDV3_PYPI_TOKEN=" + token + "\n")
        return
    elif command == "marker":
        authorization = inputs.authorization()
        decision = authorization.bundle.snapshot.observation.decision
        fresh = _github().governance(
            decision.snapshot.governance.registry,
            initial=decision.snapshot.governance,
        )
        absence = read_python_index(
            fresh.registry,
            decision.artifacts[0].witness,
            PythonHttpsTransport(),
        )
        record = PythonMutationMarker(
            authorization,
            inputs.reference("authorization"),
            fresh,
            absence,
            datetime.now(UTC),
        )
    elif command == "execute":
        record = execute_python_publication(
            inputs.marker(),
            inputs.reference("marker"),
            (inputs.content("wheel"), inputs.content("sdist")),
            token=os.environ["WDV3_PYPI_TOKEN"],
            transport=PythonHttpsTransport(),
            claim_path=inputs.directory / "upload-started",
            clock=lambda: datetime.now(UTC),
        )
        _outputs({"publication-result": record.result})
    elif command == "exact-proof":
        snapshot = inputs.publication()
        if (
            snapshot.action_required
            or os.environ["GITHUB_JOB"] != "finalize-attempt"
            or os.environ["PUBLISHER_CONCLUSION"] != "skipped"
            or os.environ.get("TERMINAL_REFERENCE", "") not in {"", "null"}
            or {"marker", "result"}.intersection(inputs.references)
        ):
            message = (
                "Python exact proof requires skipped zero-action publisher"
            )
            raise ValueError(message)
        decision = snapshot.observation.decision
        fresh = _github().governance(
            snapshot.registry, initial=decision.snapshot.governance
        )
        native = read_python_index(
            snapshot.registry,
            decision.artifacts[0].witness,
            PythonHttpsTransport(),
        )
        record = PythonExactSatisfiedProof(
            snapshot,
            inputs.reference("publication"),
            fresh,
            native,
            datetime.now(UTC),
        )
    else:
        message = "unsupported Python publication stage"
        raise ValueError(message)
    _write(arguments.output, record.to_document())


def _finalize(arguments: argparse.Namespace, inputs: PythonInputs) -> bool:
    decision = inputs.decision()
    values = PythonFinalizationInputs(decision, inputs.reference("decision"))
    if "observation" in inputs.references:
        values = replace(
            values,
            observation=(inputs.observation(), inputs.reference("observation")),
        )
    if "publication" in inputs.references:
        values = replace(
            values,
            publication=(inputs.publication(), inputs.reference("publication")),
        )
    if "bundle" in inputs.references:
        values = replace(
            values, bundle=(inputs.bundle(), inputs.reference("bundle"))
        )
    if "authorization" in inputs.references:
        values = replace(
            values,
            authorization=(
                inputs.authorization(),
                inputs.reference("authorization"),
            ),
        )
    terminal = None
    if "result" in inputs.references:
        terminal = inputs.reference("result")
        values = replace(
            values,
            terminal=(
                python_publication_result_from_document(
                    inputs.document("result")
                ),
                terminal,
            ),
            result_marker=(inputs.marker(), inputs.reference("marker")),
        )
    elif "marker" in inputs.references:
        terminal = inputs.reference("marker")
        values = replace(values, terminal=(inputs.marker(), terminal))
    if "exact-proof" in inputs.references:
        doc = cast("dict[str, JsonValue]", inputs.document("exact-proof"))
        snapshot = inputs.publication()
        originals = tuple(
            a.inspect(inputs.content(a.variant)) for a in decision.artifacts
        )
        observed_at = datetime.fromisoformat(python_text(doc["observed-at"]))
        fresh = PythonGovernance(
            snapshot.registry,
            decision.snapshot.governance.content,
            python_text(doc["governance-source-commit"]),
            datetime.fromisoformat(python_text(doc["governance-observed-at"])),
        )
        proof = PythonExactSatisfiedProof(
            snapshot,
            inputs.reference("publication"),
            fresh,
            python_native_observation_from_document(doc["native"], originals),
            observed_at,
        )
        if canonicalize(proof.to_document()) != canonicalize(doc):
            message = "Python exact proof differs from its current inputs"
            raise ValueError(message)
        values = replace(
            values, exact_proof=(proof, inputs.reference("exact-proof"))
        )
    outcome = finalize_attempt_outcome(
        values,
        current=ReleaseAdmissionBindings(
            "live-release", inputs.run, None, inputs.target
        ),
        run_attempt=int(os.environ["GITHUB_RUN_ATTEMPT"]),
        publisher_conclusion=arguments.publisher_conclusion,
        publication_step_outcome=arguments.publication_step_outcome,
        publication_terminal_reference=arguments.terminal_reference,
        observation_conclusion=arguments.observation_conclusion,
    )
    if outcome is None:
        return False
    _write(arguments.output, outcome.to_document())
    return outcome.disposition in {"published", "exact-satisfied"}


def main(argv: list[str] | None = None) -> int:  # noqa: C901, PLR0912, PLR0915
    """Run one current-DAG Python stage; no dispatch or provisioning command."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=(
            "request",
            "provider",
            "compile",
            "build",
            "artifacts",
            "plan",
            "quality",
            "decision",
            "bind",
            "export",
            "observe",
            "prepare",
            "summary",
            "bundle",
            "authorize",
            "token",
            "marker",
            "execute",
            "exact-proof",
            "terminal",
            "finalize",
        ),
    )
    parser.add_argument(
        "--purpose",
        choices=("live-release", "ci-pr-slice-shadow", "slice-validation"),
        required=True,
    )
    parser.add_argument("--directory", type=Path, default=Path(".wdv3"))
    parser.add_argument(
        "--references", default=os.environ.get("WDV3_REFERENCES", "{}")
    )
    parser.add_argument(
        "--output", type=Path, default=Path(".wdv3/output.json")
    )
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--registry", choices=("testpypi", "pypi"))
    parser.add_argument("--role", choices=tuple(_PRODUCERS))
    parser.add_argument("--payload")
    parser.add_argument("--artifact-id", type=int)
    parser.add_argument("--artifact-digest")
    parser.add_argument("--artifact-url")
    parser.add_argument("--publisher-conclusion")
    parser.add_argument("--publication-step-outcome")
    parser.add_argument("--terminal-reference")
    parser.add_argument("--observation-conclusion")
    arguments = parser.parse_args(argv)
    try:
        inputs = PythonInputs(
            arguments.directory, arguments.purpose, arguments.references
        )
        arguments.directory.mkdir(parents=True, exist_ok=True)
        if arguments.command == "request":
            _request(arguments, inputs)
        elif arguments.command == "provider":
            manifest = _manifest(inputs)
            result = provide_python_repository_facts(
                Path(arguments.repo_root),
                provider_binding(manifest, "python-smoke"),
                CheckoutMaterialization(0, credentials_persisted=False),
            )
            _write(arguments.output, result.to_document())
        elif arguments.command == "compile":
            facts = admit_python_provider_facts(
                inputs.content("provider"),
                manifest=_manifest(inputs),
                request_reference=inputs.reference("request"),
                result_reference=inputs.reference("provider"),
                result_transport=inputs.transport("provider"),
            )
            model = compile_python_repository_model(
                Path(arguments.repo_root), facts
            )
            _write(arguments.output, model.to_document())
        elif arguments.command == "build":
            _build(arguments, inputs)
        elif arguments.command == "artifacts":
            _artifact_records(arguments, inputs)
        elif arguments.command == "plan":
            _plan(arguments, inputs)
        elif arguments.command == "quality":
            _quality(arguments, inputs)
        elif arguments.command == "decision":
            return 0 if _decision(arguments, inputs) else 1
        elif arguments.command == "bind":
            _bind(arguments, inputs)
        elif arguments.command == "export":
            _outputs(
                {
                    "references": canonicalize(inputs.references).decode(),
                    "artifact-ids": ",".join(
                        str(inputs.reference(role).artifact_id)
                        for role in inputs.references
                    ),
                }
            )
        elif arguments.command == "terminal":
            role = (
                "result"
                if "result" in inputs.references
                else "marker"
                if "marker" in inputs.references
                else None
            )
            if role == "result":
                python_publication_result_from_document(inputs.document(role))
            elif role == "marker":
                inputs.marker()
            _outputs(
                {
                    "terminal-reference": canonicalize(
                        None
                        if role is None
                        else inputs.reference(role).to_document()
                    ).decode()
                }
            )
        elif arguments.command == "finalize":
            return 0 if _finalize(arguments, inputs) else 1
        else:
            if inputs.purpose != "live-release":
                message = "CI cannot enter a Python publication stage"
                raise ValueError(message)  # noqa: TRY301
            _publication(arguments, inputs)
    except (
        OSError,
        ValueError,
        TypeError,
        KeyError,
        subprocess.CalledProcessError,
    ) as error:
        # No native token/assertion or response body is emitted in diagnostics.
        print(f"Python stage rejected: {type(error).__name__}")  # noqa: T201
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
