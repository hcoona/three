"""Hosted Ruby stages over explicit immutable current-DAG artifact outputs."""

from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path
from typing import TYPE_CHECKING

from three_workflow_delivery_v3._ruby_native import (
    ruby_digest,
)
from three_workflow_delivery_v3.adapters.ruby import build_ruby_package
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.ci.ruby import (
    RubyCiDecision,
    RubyCiPlan,
    ruby_ci_evidence_from_document,
    ruby_ci_plan_from_document,
    run_ruby_ci_quality,
)
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
    artifact_reference_from_document,
)
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    ruby_artifact_from_document,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    _compilation_context_from_document,
    provider_binding,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RubyRepositoryModelSnapshot,
    admit_ruby_provider_facts,
    compile_ruby_repository_model,
    ruby_provider_manifest,
    ruby_repository_model_from_document,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    provide_ruby_repository_facts,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.repository.compiler import (
        ProviderRequestManifest,
    )

# Static current-DAG producer ownership, not payload-selected permissions.
_PRODUCERS = {
    "request": "request-ruby",
    "provider": "discover-ruby",
    "model": "compile-ruby",
    "ci-plan": "plan-ruby-ci",
    "gem": "build-ruby",
    "build-report": "build-ruby",
    "artifacts": "qualify-ruby",
    "quality": "qualify-ruby",
    "decision": "qualify-ruby",
}


def _outputs(values: dict[str, str]) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with Path(path).open("a", encoding="utf-8") as stream:
            for name, value in values.items():
                if "\n" in value or "\r" in value:
                    message = "Ruby workflow output must be a single line"
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
        message = "Ruby hosted stages require the accepted repository"
        raise ValueError(message)
    if purpose not in {"ci-pr-slice-shadow", "slice-validation"}:
        message = "Ruby hosted purpose is unsupported"
        raise ValueError(message)
    if purpose == "ci-pr-slice-shadow" and (
        os.environ["GITHUB_EVENT_NAME"] != "pull_request"
        or not os.environ["GITHUB_REF"].startswith("refs/pull/")
        or not os.environ["GITHUB_REF"].endswith("/merge")
    ):
        message = "Ruby PR CI requires the tested merge candidate"
        raise ValueError(message)
    return target, run, attempt


class RubyInputs:
    """Explicit artifact references from trusted current workflow edges."""

    def __init__(self, directory: Path, purpose: str, references: str) -> None:
        """Bind platform identity before reading record payloads."""
        self.directory = directory
        self.purpose = purpose
        self.target, self.run, self.run_attempt = _current(purpose)
        document = parse_json_strict(references)
        if not isinstance(document, dict) or set(document) - _PRODUCERS.keys():
            message = "Ruby artifact map has unknown roles"
            raise ValueError(message)
        self.references = document

    def reference(self, role: str) -> ArtifactReference:
        """Read only one named current-DAG reference, never list or discover."""
        reference = artifact_reference_from_document(self.references[role])
        expected = (
            f"https://github.com/hcoona/three/actions/runs/{self.run}"
            f"/artifacts/{reference.artifact_id}"
        )
        if reference.artifact_url != expected:
            message = "Ruby artifact URL differs from the current run"
            raise ValueError(message)
        return reference

    def content(self, role: str) -> bytes:
        """Admit archive:false action output bytes against both digests."""
        reference = self.reference(role)
        content = (self.directory / reference.payload_path).read_bytes()
        if (
            ruby_digest(content) != reference.payload_digest
            or reference.artifact_digest != reference.payload_digest
        ):
            message = "Ruby downloaded artifact differs from immutable output"
            raise ValueError(message)
        return content

    def document(self, role: str) -> JsonValue:
        """Read strict canonical business data after transport admission."""
        content = self.content(role)
        document = parse_json_strict(content)
        if canonicalize(document) != content:
            message = "Ruby artifact JSON is not canonical"
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

    def model(self) -> RubyRepositoryModelSnapshot:
        """Admit the Model against current platform identity and request."""
        model = ruby_repository_model_from_document(self.document("model"))
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
            message = "Ruby Model is not the current workflow's snapshot"
            raise ValueError(message)
        return model

    def artifact(self) -> RubyArtifact:
        """Load the singular original transported by this current Build."""
        artifact = ruby_artifact_from_document(self.document("artifacts"))
        if artifact.reference != self.reference(
            "gem"
        ) or artifact.transport != self.transport("gem"):
            message = "Ruby original transport differs from current DAG"
            raise ValueError(message)
        artifact.inspect(self.content("gem"))
        return artifact

    def ci_plan(self) -> RubyCiPlan:
        """Bind the CI Plan to the exact current Model and transport."""
        plan = ruby_ci_plan_from_document(self.document("ci-plan"))
        if plan.model != self.model() or plan.model_reference != self.reference(
            "model"
        ):
            message = "Ruby CI Plan predecessors differ from current DAG"
            raise ValueError(message)
        return plan


def _request(arguments: argparse.Namespace, inputs: RubyInputs) -> None:
    request_id = f"ruby-ci:{inputs.run}:{inputs.run_attempt}"
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
    _write(arguments.output, ruby_provider_manifest(context).to_document())


def _manifest(inputs: RubyInputs) -> ProviderRequestManifest:
    document = inputs.document("request")
    if not isinstance(document, dict):
        message = "Ruby Provider request must be an object"
        raise TypeError(message)
    context = _compilation_context_from_document(document["context"])
    manifest = ruby_provider_manifest(context)
    if (
        canonicalize(manifest.to_document()) != canonicalize(document)
        or (
            context.target,
            context.workflow_run_id,
            context.run_attempt,
            context.purpose,
        )
        != (inputs.target, inputs.run, inputs.run_attempt, inputs.purpose)
        or (
            context.control != f"workflow-delivery-v3:{inputs.target}"
            or context.catalog_digest != catalog_digest()
        )
    ):
        message = "Ruby Provider request differs from current platform context"
        raise ValueError(message)
    return manifest


def _build(arguments: argparse.Namespace, inputs: RubyInputs) -> None:
    result = build_ruby_package(
        Path(arguments.repo_root), inputs.model().build_request()
    )
    path = inputs.directory / result.filename
    path.write_bytes(result.content)
    _outputs({"gem-path": str(path)})
    _write(arguments.output, parse_canonical_json(result.inspection))


def _artifact_records(
    arguments: argparse.Namespace, inputs: RubyInputs
) -> None:
    reference = inputs.reference("gem")
    artifact = RubyArtifact(
        reference.payload_path,
        len(inputs.content("gem")),
        inputs.model().build_request().witness,
        reference,
        inputs.transport("gem"),
    )
    artifact.inspect(inputs.content("gem"))
    _write(arguments.output, artifact.to_document())


def _plan(arguments: argparse.Namespace, inputs: RubyInputs) -> None:
    model = inputs.model()
    # Hosted Ruby CI deliberately selects full scope at the exact target.
    plan = RubyCiPlan(model, inputs.reference("model"), None)
    _write(arguments.output, plan.to_document())
    _outputs({"selected": str(plan.selected).lower()})


def _quality(arguments: argparse.Namespace, inputs: RubyInputs) -> None:
    artifact = inputs.artifact()
    payload = inputs.content("gem")
    evidence = run_ruby_ci_quality(inputs.ci_plan(), artifact, payload)
    _write(arguments.output, [e.to_document() for e in evidence])


def _decision(arguments: argparse.Namespace, inputs: RubyInputs) -> bool:
    evidence = (
        inputs.document("quality") if "quality" in inputs.references else []
    )
    if not isinstance(evidence, list):
        message = "Ruby qualification Evidence must be an array"
        raise TypeError(message)
    decision = RubyCiDecision(
        inputs.ci_plan(),
        tuple(ruby_ci_evidence_from_document(e) for e in evidence),
    )
    _write(arguments.output, decision.to_document())
    _outputs({"qualification-result": decision.result})
    return decision.result in {"passed", "empty"}


def _bind(arguments: argparse.Namespace, inputs: RubyInputs) -> None:
    if os.environ["GITHUB_JOB"] != _PRODUCERS[arguments.role]:
        message = "Ruby upload role belongs to another workflow producer"
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
        ruby_digest(content),
    )
    if (
        reference.artifact_digest != reference.payload_digest
        or arguments.role in inputs.references
    ):
        message = "Ruby immutable upload digest mismatch or role replacement"
        raise ValueError(message)
    inputs.references[arguments.role] = reference.to_document()
    inputs.reference(arguments.role)
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


def main(argv: list[str] | None = None) -> int:  # noqa: C901
    """Run one current-DAG Ruby stage; no dispatch or provisioning command."""
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
        ),
    )
    parser.add_argument(
        "--purpose",
        choices=("ci-pr-slice-shadow", "slice-validation"),
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
    parser.add_argument("--role", choices=tuple(_PRODUCERS))
    parser.add_argument("--payload")
    parser.add_argument("--artifact-id", type=int)
    parser.add_argument("--artifact-digest")
    parser.add_argument("--artifact-url")
    arguments = parser.parse_args(argv)
    try:
        inputs = RubyInputs(
            arguments.directory, arguments.purpose, arguments.references
        )
        arguments.directory.mkdir(parents=True, exist_ok=True)
        if arguments.command == "request":
            _request(arguments, inputs)
        elif arguments.command == "provider":
            manifest = _manifest(inputs)
            result = provide_ruby_repository_facts(
                Path(arguments.repo_root),
                provider_binding(manifest, "ruby-smoke"),
                CheckoutMaterialization(0, credentials_persisted=False),
            )
            _write(arguments.output, result.to_document())
        elif arguments.command == "compile":
            facts = admit_ruby_provider_facts(
                inputs.content("provider"),
                manifest=_manifest(inputs),
                request_reference=inputs.reference("request"),
                result_reference=inputs.reference("provider"),
                result_transport=inputs.transport("provider"),
            )
            model = compile_ruby_repository_model(
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
    except (
        OSError,
        ValueError,
        TypeError,
        KeyError,
        subprocess.SubprocessError,
    ) as error:
        # No native token/assertion or response body is emitted in diagnostics.
        print(f"Ruby stage rejected: {type(error).__name__}")  # noqa: T201
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
