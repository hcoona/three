"""Complete first-slice Release Qualification Snapshot planning."""

from __future__ import annotations

from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.adapters.node import PackageTargetWitness
from three_workflow_delivery_v3.canonical import canonical_sha256
from three_workflow_delivery_v3.catalogs import (
    BUILD_DEFINITIONS,
    DESTINATION_DEFINITIONS,
    QUALITY_DEFINITIONS,
)
from three_workflow_delivery_v3.records.release import (
    ArtifactVariantIdentity,
    DestinationProjection,
    ExternalPackageCoordinate,
    PotentialActionContract,
    QualificationSnapshot,
    ReleaseBuildIdentity,
    ReleaseBuildRequest,
    ReleaseIntent,
    ReleaseObligation,
    ReleaseOutputIdentity,
    SimulationBinding,
)
from three_workflow_delivery_v3.repository.compiler import (
    AdmittedRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.descriptors import (
    FIRST_SLICE_PACKAGE,
    FIRST_SLICE_POLICY_PATH,
    FIRST_SLICE_RELEASE_UNIT,
)

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

_RUNNER = "ubuntu-24.04"
_BUILD_OBLIGATION_ID = "release:build:npm-package"
_PROJECT_TEST_OBLIGATION_ID = "release:quality:project-test"
_CONTENTS_OBLIGATION_ID = "release:quality:npm-artifact-contents"
_INSTALL_OBLIGATION_ID = "release:quality:npm-install-import"
_DECLARED_BUILD_INPUTS = (
    "README.md",
    "package.json",
    "scripts/build.mjs",
    "src/index.js",
)


def _definition_digest(definition_id: str) -> str:
    if definition_id in BUILD_DEFINITIONS:
        definition = BUILD_DEFINITIONS[definition_id]
        document = cast(
            "dict[str, JsonValue]",
            {
                "schema": "workflow-delivery/v3/build-definition",
                "logical-id": definition.logical_id,
                "ecosystem": definition.ecosystem,
                "operation": definition.operation,
                "implementation-id": definition.implementation_id,
                "execution-class": definition.execution_class,
                "capability-requirements": list(
                    definition.capability_requirements
                ),
                "output-kinds": list(definition.output_kinds),
                "required-native-projections": list(
                    definition.required_native_projections
                ),
            },
        )
    else:
        definition = QUALITY_DEFINITIONS[definition_id]
        document = cast(
            "dict[str, JsonValue]",
            {
                "schema": "workflow-delivery/v3/quality-definition",
                "logical-id": definition.logical_id,
                "subject": definition.subject,
                "operation": definition.operation,
                "implementation-id": definition.implementation_id,
                "execution-class": definition.execution_class,
                "capability-requirements": list(
                    definition.capability_requirements
                ),
            },
        )
    return canonical_sha256(document)


def _obligation(  # noqa: PLR0913
    *,
    snapshot_basis_digest: str,
    obligation_id: str,
    definition_id: str,
    subject_kind: str,
    subject_digest: str,
    target: str,
    prerequisites: tuple[str, ...] = (),
) -> ReleaseObligation:
    definition_digest = _definition_digest(definition_id)
    request_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/release-obligation-request",
            "qualification-snapshot-basis-digest": snapshot_basis_digest,
            "obligation-id": obligation_id,
            "definition-id": definition_id,
            "definition-digest": definition_digest,
            "subject-kind": subject_kind,
            "subject-digest": subject_digest,
            "target": target,
            "dimensions": [],
            "runner": _RUNNER,
            "prerequisites": list(prerequisites),
            "required": True,
        }
    )
    return ReleaseObligation(
        obligation_id=obligation_id,
        definition_id=definition_id,
        definition_digest=definition_digest,
        subject_kind=subject_kind,
        subject_digest=subject_digest,
        target=target,
        dimensions=(),
        runner=_RUNNER,
        prerequisites=prerequisites,
        required=True,
        request_digest=request_digest,
        expected_evidence_id=(
            "evidence:"
            f"{obligation_id.removeprefix('release:')}:"
            f"{request_digest.removeprefix('sha256:')}"
        ),
    )


def _validate_inputs(
    intent: ReleaseIntent,
    binding: SimulationBinding,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> None:
    if type(intent) is not ReleaseIntent:
        message = "Release Planner requires an exact ReleaseIntent"
        raise TypeError(message)
    if type(binding) is not SimulationBinding:
        message = "Release Planner requires an exact SimulationBinding"
        raise TypeError(message)
    if type(admitted_repository_model) is not AdmittedRepositoryModelSnapshot:
        message = "Release Planner requires an admitted Repository Model"
        raise TypeError(message)
    snapshot = admitted_repository_model.snapshot
    if (
        intent.channel != "official"
        or intent.mode != "simulation"
        or intent.purpose != "release-simulation"
        or binding.channel != "official"
        or binding.purpose != "release-simulation"
    ):
        message = "Release Planner supports only Official simulation"
        raise ValueError(message)
    checks = (
        ("Intent digest", binding.intent_digest, intent.intent_digest),
        (
            "Repository Model digest",
            binding.repository_model_digest,
            admitted_repository_model.canonical_digest,
        ),
        ("target", binding.target, snapshot.context.target),
        ("channel", binding.channel, snapshot.context.channel),
        ("Release Unit", binding.release_unit, snapshot.context.release_unit),
        ("control", binding.control, snapshot.context.control),
        ("request", binding.simulation.request_id, snapshot.context.request_id),
        (
            "workflow run",
            binding.simulation.workflow_run_id,
            snapshot.context.workflow_run_id,
        ),
        (
            "run attempt",
            binding.simulation.run_attempt,
            snapshot.context.run_attempt,
        ),
    )
    for field, actual, expected in checks:
        if actual != expected:
            message = f"Release Planner {field} binding mismatch"
            raise ValueError(message)
    policy = snapshot.release_policy
    if policy is None:
        message = "Release Planner requires compiled Snapshot policy"
        raise ValueError(message)
    if (
        policy.path != FIRST_SLICE_POLICY_PATH
        or snapshot.release_policy_path != policy.path
        or policy.release_unit != FIRST_SLICE_RELEASE_UNIT
        or intent.release_unit != policy.release_unit
    ):
        message = "Release Planner policy binding mismatch"
        raise ValueError(message)
    channel = policy.channel("official")
    if (
        channel.quality
        != (
            "node/project-test-v1",
            "node/npm-artifact-contents-v1",
            "node/npm-install-import-v1",
        )
        or len(channel.projections) != 1
    ):
        message = "Release Planner Official policy is not the first slice"
        raise ValueError(message)


def plan_official_simulation_qualification(
    intent: ReleaseIntent,
    binding: SimulationBinding,
    admitted_repository_model: AdmittedRepositoryModelSnapshot,
) -> QualificationSnapshot:
    """Plan the exact four-obligation Official simulation closure."""
    _validate_inputs(intent, binding, admitted_repository_model)
    repository_model = admitted_repository_model.snapshot
    policy = repository_model.release_policy
    if policy is None:
        message = "Release Planner requires compiled Snapshot policy"
        raise ValueError(message)
    compiled_unit = repository_model.release_units[0]
    compiled_build = compiled_unit.builds[0]
    compiled_output = compiled_build.outputs[0]
    project = repository_model.project_nodes[0]
    if (
        compiled_unit.release_unit != FIRST_SLICE_RELEASE_UNIT
        or compiled_build.build_id != "npm-package"
        or compiled_build.definition != "node/npm-package-v1"
        or compiled_build.project_id != FIRST_SLICE_PACKAGE
        or (
            compiled_output.output_id,
            compiled_output.role,
            compiled_output.kind,
        )
        != ("npm-tarball", "primary-package", "npm-tarball")
        or project.project_id != FIRST_SLICE_PACKAGE
    ):
        message = "Release Planner Repository Model closure is not exact"
        raise ValueError(message)

    build = ReleaseBuildIdentity(
        release_unit=compiled_unit.release_unit,
        build_id=compiled_build.build_id,
        definition_id=compiled_build.definition,
        project_id=compiled_build.project_id,
    )
    variant = ArtifactVariantIdentity(
        build=build,
        variant_id="npm-package/default",
        dimensions=(),
    )
    output = ReleaseOutputIdentity(
        variant=variant,
        output_id=compiled_output.output_id,
        logical_role=compiled_output.role,
        media_kind=compiled_output.kind,
    )
    control_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": repository_model.context.control,
        }
    )
    witness = PackageTargetWitness(
        target=binding.target,
        release_unit=binding.release_unit,
        nbgv=repository_model.nbgv,
        build_definition=build.definition_id,
        catalog_digest=repository_model.context.catalog_digest,
        control_digest=control_digest,
        purpose="release-simulation",
    )
    build_request = ReleaseBuildRequest(
        build=build,
        variant=variant,
        output=output,
        repository_model_digest=admitted_repository_model.canonical_digest,
        definition_digest=_definition_digest(build.definition_id),
        npm_package_version=repository_model.nbgv.npm_package_version,
        witness_digest=canonical_sha256(witness.to_document()),
        declared_inputs=_DECLARED_BUILD_INPUTS,
        adapter_id="node/npm-package-v1",
    )

    policy_projection = policy.channel("official").projections[0]
    destination = DESTINATION_DEFINITIONS[policy_projection.destination]
    coordinate = ExternalPackageCoordinate(
        channel="official",
        destination_id=destination.logical_id,
        package_name=policy_projection.package,
        native_version=repository_model.nbgv.npm_package_version,
    )
    projection = DestinationProjection(
        projection_id="projection:npm:npmjs-public",
        destination_id=destination.logical_id,
        registry=destination.registry,
        coordinate=coordinate,
        output=output,
        operation="npm-publish-create-only",
        observation_contract_id="npm/npmjs-public-observation-v1",
        potential_action_id="potential-action:npm:npmjs-publish",
    )
    potential_action = PotentialActionContract(
        contract_id=projection.potential_action_id,
        projection_id=projection.projection_id,
        operation=projection.operation,
        output=output,
        prerequisites=(),
        capability_requirements=destination.capability_requirements,
        mutable_resource_key_basis=("external-package-coordinate",),
    )

    snapshot_basis_digest = canonical_sha256(
        {
            "schema": "workflow-delivery/v3/qualification-snapshot-basis",
            "simulation-binding-digest": binding.binding_digest,
            "repository-model-digest": (
                admitted_repository_model.canonical_digest
            ),
            "release-policy-digest": policy.policy_digest,
            "build-request-digest": build_request.request_digest,
            "destination-projection-digest": projection.projection_digest,
            "potential-action": potential_action.to_document(),
        }
    )
    obligations = (
        _obligation(
            snapshot_basis_digest=snapshot_basis_digest,
            obligation_id=_BUILD_OBLIGATION_ID,
            definition_id=build.definition_id,
            subject_kind="artifact-variant",
            subject_digest=canonical_sha256(variant.to_document()),
            target=binding.target,
        ),
        _obligation(
            snapshot_basis_digest=snapshot_basis_digest,
            obligation_id=_PROJECT_TEST_OBLIGATION_ID,
            definition_id="node/project-test-v1",
            subject_kind="project-node",
            subject_digest=canonical_sha256(
                {
                    "schema": "workflow-delivery/v3/project-node-identity",
                    "project-id": project.project_id,
                    "path": project.path,
                }
            ),
            target=binding.target,
        ),
        _obligation(
            snapshot_basis_digest=snapshot_basis_digest,
            obligation_id=_CONTENTS_OBLIGATION_ID,
            definition_id="node/npm-artifact-contents-v1",
            subject_kind="release-output",
            subject_digest=canonical_sha256(output.to_document()),
            target=binding.target,
            prerequisites=(_BUILD_OBLIGATION_ID,),
        ),
        _obligation(
            snapshot_basis_digest=snapshot_basis_digest,
            obligation_id=_INSTALL_OBLIGATION_ID,
            definition_id="node/npm-install-import-v1",
            subject_kind="release-output",
            subject_digest=canonical_sha256(output.to_document()),
            target=binding.target,
            prerequisites=(_BUILD_OBLIGATION_ID,),
        ),
    )
    return QualificationSnapshot(
        subject=binding,
        repository=intent.repository,
        repository_model_digest=admitted_repository_model.canonical_digest,
        release_policy_digest=policy.policy_digest,
        target=binding.target,
        channel="official",
        release_unit=binding.release_unit,
        nbgv=repository_model.nbgv,
        builds=(build,),
        variants=(variant,),
        outputs=(output,),
        build_requests=(build_request,),
        destination_projections=(projection,),
        potential_actions=(potential_action,),
        obligations=obligations,
        expected_evidence_ids=tuple(
            obligation.expected_evidence_id for obligation in obligations
        ),
        ready=True,
    )


__all__ = ["plan_official_simulation_qualification"]
