"""Closed NuGet qualification planning from admitted native facts."""

from three_workflow_delivery_v3.adapters.dotnet import (
    DotnetPackageTargetWitness,
)
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import DESTINATION_DEFINITIONS
from three_workflow_delivery_v3.records.release import (
    ArtifactVariantIdentity,
    DestinationProjection,
    NugetExternalPackageCoordinate,
    NugetPackageIdentity,
    NugetReleaseBuildRequest,
    PotentialActionContract,
    QualificationSnapshot,
    ReleaseAttemptBinding,
    ReleaseBuildIdentity,
    ReleaseIntent,
    ReleaseOutputIdentity,
    publication_capability_requirements,
    publication_mutable_resource_key_basis,
)
from three_workflow_delivery_v3.release.planner import (
    _definition_digest,
    _obligation,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedDotnetProviderFactBundle,
    AdmittedRepositoryModelSnapshot,
    validate_nuget_repository_model_snapshot,
)
from three_workflow_delivery_v3.repository.descriptors import NUGET_RELEASE_UNIT
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DotnetNbgvFacts,
    DotnetProjectNode,
    validate_dotnet_provider_result,
)

NUGET_BUILD_OBLIGATION = "release:build:nuget-package"
NUGET_CONTENTS_OBLIGATION = "release:quality:nuget-artifact-contents"
NUGET_CONSUMER_OBLIGATION = "release:quality:nuget-restore-build-invoke"
NUGET_RUNNER = "windows-latest"
NUGET_DIMENSIONS = (("framework", "net10.0"), ("os", "windows"))


def _validate_inputs(
    intent: ReleaseIntent,
    binding: ReleaseAttemptBinding,
    model: AdmittedRepositoryModelSnapshot,
    provider: AdmittedDotnetProviderFactBundle,
) -> None:
    if (
        type(intent) is not ReleaseIntent
        or type(binding) is not ReleaseAttemptBinding
        or type(model) is not AdmittedRepositoryModelSnapshot
        or type(provider) is not AdmittedDotnetProviderFactBundle
    ):
        message = "NuGet planning requires exact admitted native inputs"
        raise TypeError(message)
    snapshot = model.snapshot
    validate_nuget_repository_model_snapshot(snapshot)
    result = provider.provider_result
    validate_dotnet_provider_result(result)
    if (
        canonicalize(snapshot.to_document()) != model.canonical_bytes
        or snapshot.snapshot_digest != model.canonical_digest
        or snapshot.provider_result_digests != (result.result_digest,)
        or snapshot.manifest_digest != provider.bundle.manifest_digest
        or provider.bundle.provider_result_digest != result.result_digest
        or provider.bundle.bundle_digest != provider.admission.bundle_digest
        or snapshot.nbgv != result.nbgv
        or snapshot.project_nodes != result.project_nodes
    ):
        message = "NuGet planning input integrity is not closed"
        raise ValueError(message)
    context = snapshot.context
    if (
        intent.repository != "hcoona/three"
        or intent.actor != "hcoona"
        or intent.selected_ref != "refs/heads/main"
        or intent.workflow_sha != intent.target
        or intent.channel != "buddy"
        or intent.mode != "live"
        or intent.purpose != "live-release"
        or intent.release_unit != NUGET_RELEASE_UNIT
        or context.purpose != intent.purpose
        or context.target != intent.target
        or context.control != f"workflow-delivery-v3:{intent.target}"
        or context.request_id != intent.request_id
        or context.workflow_run_id != intent.workflow_run_id
        or context.run_attempt is not None
        or result.binding.target != context.target
        or result.binding.purpose != context.purpose
        or result.binding.request_id != context.request_id
        or result.binding.workflow_run_id != context.workflow_run_id
        or binding.intent_digest != intent.intent_digest
        or binding.request_id != intent.request_id
        or binding.repository_model_digest != model.canonical_digest
        or binding.execution.target != intent.target
        or binding.execution.release_unit != intent.release_unit
        or binding.execution.channel != intent.channel
        or binding.attempt.workflow_run_id != intent.workflow_run_id
    ):
        message = "NuGet qualification requires protected-main Buddy context"
        raise ValueError(message)


def nuget_package_target_witness(
    model: AdmittedRepositoryModelSnapshot,
) -> DotnetPackageTargetWitness:
    """Construct the canonical native witness from the admitted live Model."""
    if type(model) is not AdmittedRepositoryModelSnapshot:
        message = "NuGet witness requires an admitted Repository Model"
        raise TypeError(message)
    repository = model.snapshot
    validate_nuget_repository_model_snapshot(repository)
    if (
        canonicalize(repository.to_document()) != model.canonical_bytes
        or repository.snapshot_digest != model.canonical_digest
        or repository.context.purpose != "live-release"
        or type(repository.nbgv) is not DotnetNbgvFacts
    ):
        message = "NuGet witness requires the intact native live Model"
        raise ValueError(message)
    return DotnetPackageTargetWitness(
        target=repository.context.target,
        release_unit=NUGET_RELEASE_UNIT,
        nbgv=repository.nbgv,
        build_definition=repository.release_units[0].builds[0].definition,
        catalog_digest=repository.context.catalog_digest,
        control_digest=canonical_sha256(
            {
                "schema": "workflow-delivery/v3/control-identity",
                "identity": repository.context.control,
            }
        ),
        purpose="live-release",
    )


def plan_nuget_live_qualification(
    intent: ReleaseIntent,
    binding: ReleaseAttemptBinding,
    model: AdmittedRepositoryModelSnapshot,
    provider: AdmittedDotnetProviderFactBundle,
) -> QualificationSnapshot:
    """Freeze one native build and two independent artifact obligations."""
    _validate_inputs(intent, binding, model, provider)
    repository = model.snapshot
    facts = repository.nbgv
    project = repository.project_nodes[0]
    if (
        type(facts) is not DotnetNbgvFacts
        or type(project) is not DotnetProjectNode
    ):
        message = "NuGet qualification requires native .NET facts"
        raise TypeError(message)
    policy = repository.release_policy
    if policy is None:
        message = "NuGet qualification requires a compiled policy"
        raise ValueError(message)
    compiled = repository.release_units[0].builds[0]
    compiled_output = compiled.outputs[0]
    build = ReleaseBuildIdentity(
        NUGET_RELEASE_UNIT,
        compiled.build_id,
        compiled.definition,
        compiled.project_id,
    )
    variant = ArtifactVariantIdentity(
        build, "nuget-package/net10.0-windows", NUGET_DIMENSIONS
    )
    output = ReleaseOutputIdentity(
        variant,
        compiled_output.output_id,
        compiled_output.role,
        compiled_output.kind,
    )
    witness = nuget_package_target_witness(model)
    manifest = provider.provider_result.source_input_manifest
    request = NugetReleaseBuildRequest(
        build=build,
        variant=variant,
        output=output,
        repository_model_digest=model.canonical_digest,
        definition_digest=_definition_digest(build.definition_id),
        nuget_package_version=facts.nuget_package_version,
        witness_digest=canonical_sha256(witness.to_document()),
        declared_inputs=tuple(path for path, _ in manifest),
        source_input_manifest=manifest,
        adapter_id=build.definition_id,
    )
    selected = policy.channel("buddy").projections[0]
    destination = DESTINATION_DEFINITIONS[selected.destination]
    projection = DestinationProjection(
        projection_id="projection:nuget:github-packages",
        destination_id=destination.logical_id,
        registry=destination.registry,
        coordinate=NugetExternalPackageCoordinate(
            "buddy",
            destination.logical_id,
            NugetPackageIdentity(
                selected.package,
                facts.nuget_package_version,
                project.normalized_package_id,
                project.normalized_version,
            ),
        ),
        output=output,
        operation="nuget-publish-create-only",
        observation_contract_id="nuget/github-packages-observation-v1",
        potential_action_id="publish-nuget-github-packages",
    )
    potential = PotentialActionContract(
        contract_id=projection.potential_action_id,
        projection_id=projection.projection_id,
        operation=projection.operation,
        output=output,
        prerequisites=(),
        capability_requirements=publication_capability_requirements(projection),
        mutable_resource_key_basis=publication_mutable_resource_key_basis(
            projection
        ),
    )
    basis = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/qualification-snapshot-basis",
            "release-binding-digest": binding.binding_digest,
            "repository-model-digest": model.canonical_digest,
            "release-policy-digest": policy.policy_digest,
            "build-request-digest": request.request_digest,
            "destination-projection-digest": projection.projection_digest,
            "potential-action": potential.to_document(),
        }
    )
    obligations = tuple(
        _obligation(
            snapshot_basis_digest=basis,
            obligation_id=obligation_id,
            definition_id=definition_id,
            subject_kind="artifact-variant" if is_build else "release-output",
            subject_digest=canonical_sha256(
                variant.to_document() if is_build else output.to_document()
            ),
            target=intent.target,
            prerequisites=() if is_build else (NUGET_BUILD_OBLIGATION,),
            runner=NUGET_RUNNER,
            dimensions=NUGET_DIMENSIONS,
        )
        for obligation_id, definition_id, is_build in (
            (NUGET_BUILD_OBLIGATION, build.definition_id, True),
            (
                NUGET_CONTENTS_OBLIGATION,
                "dotnet/nuget-artifact-contents-v1",
                False,
            ),
            (
                NUGET_CONSUMER_OBLIGATION,
                "dotnet/nuget-restore-build-invoke-v1",
                False,
            ),
        )
    )
    return QualificationSnapshot(
        subject=binding.attempt,
        repository=intent.repository,
        repository_model_digest=model.canonical_digest,
        release_policy_digest=policy.policy_digest,
        target=intent.target,
        channel="buddy",
        release_unit=intent.release_unit,
        nbgv=facts,
        builds=(build,),
        variants=(variant,),
        outputs=(output,),
        build_requests=(request,),
        destination_projections=(projection,),
        potential_actions=(potential,),
        obligations=obligations,
        expected_evidence_ids=tuple(
            item.expected_evidence_id for item in obligations
        ),
        ready=True,
    )
