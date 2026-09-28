"""One source-bound original pair and complete credential-free provenance."""

from __future__ import annotations

from dataclasses import dataclass, replace
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    BootstrapRequest,
    phase_binding,
)
from three_workflow_delivery_v3.acceptance.python_native_contract import require
from three_workflow_delivery_v3.acceptance.python_native_fixture import (
    archive_members,
    consumer_evidence,
    fixture_witness,
    validate_commands,
    validate_consumer_evidence,
)
from three_workflow_delivery_v3.adapters.python import (
    PythonBuildRequest,
    PythonDistribution,
    PythonPackageTargetWitness,
    build_python_distributions,
    inspect_python_distribution,
    python_package_target_witness_from_document,
    qualify_python_consumer,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    CheckoutMaterialization,
    ProviderBinding,
    _isolated_exact_target_repository,
    _run_command,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PythonProviderResult,
    provide_python_repository_facts,
    python_digest,
    python_object,
    python_provider_result_from_document,
    python_text,
)

if TYPE_CHECKING:
    from pathlib import Path

PURPOSE = "destination-bootstrap"
VARIANTS = ("wheel", "sdist")
EVIDENCE_NAMES = {
    "provider.json",
    "build.json",
    "consumer/wheel.json",
    "consumer/sdist.json",
}
FIXTURE_NAMES = EVIDENCE_NAMES | {
    "fixtures.json",
    "fixtures/wheel.bin",
    "fixtures/sdist.bin",
}


def bootstrap_binding(target: str, run_id: int) -> ProviderBinding:
    """Bind actual preparation execution independently from package bytes."""
    return ProviderBinding(
        f"python-bootstrap-fixture:{target}",
        PURPOSE,
        run_id,
        1,
        target,
        "prepare-python-bootstrap",
        f"workflow-delivery-v3:{target}",
        catalog_digest(),
        canonical_sha256({"target": target, "purpose": PURPOSE}),
    )


def bootstrap_witness(
    provider: PythonProviderResult,
) -> PythonPackageTargetWitness:
    """Preserve the public version with distinct bootstrap purpose."""
    return replace(fixture_witness(provider), purpose=PURPOSE)


@dataclass(frozen=True)
class BootstrapFixtures:
    """Original wheel/sdist with actual typed preparation evidence."""

    distributions: dict[str, PythonDistribution]
    evidence: dict[str, bytes]

    def validate(self, run_id: int | None = None) -> None:
        """Reinspect originals, successful consumers and complete provenance."""
        require(
            set(self.distributions) == set(VARIANTS)
            and set(self.evidence) == EVIDENCE_NAMES,
            "bootstrap fixture inventory differs",
        )
        provider = python_provider_result_from_document(
            parse_canonical_json(self.evidence["provider.json"])
        )
        witness = bootstrap_witness(provider)
        require(
            provider.binding
            == bootstrap_binding(
                witness.target,
                provider.binding.workflow_run_id if run_id is None else run_id,
            ),
            "bootstrap Provider binding differs",
        )
        for variant, item in self.distributions.items():
            require(
                item.variant == variant and item.witness == witness,
                "bootstrap fixture source differs",
            )
            inspect_python_distribution(
                item.filename, item.content, variant, witness
            )
            validate_consumer_evidence(
                self.evidence[f"consumer/{variant}.json"], item
            )
        build = python_object(
            parse_canonical_json(self.evidence["build.json"]),
            {
                "source-manifest",
                "staged-manifest-digest",
                "versions",
                "commands",
            },
        )
        manifests = [
            content
            for name, content in archive_members(
                self.distributions["sdist"].content, "sdist"
            ).items()
            if name.endswith("/pyproject.toml")
        ]
        require(
            build["source-manifest"]
            == [list(pair) for pair in provider.source_input_manifest]
            and len(manifests) == 1
            and build["staged-manifest-digest"] == python_digest(manifests[0]),
            "bootstrap Build source provenance differs",
        )
        validate_commands(build["commands"])
        validate_commands([build["versions"]])
        require(
            bool(cast("dict", build["versions"])["stdout"]),
            "bootstrap producer versions missing",
        )

    def match(
        self, request: BootstrapRequest, run_id: int | None = None
    ) -> None:
        """Bind exact source and original digests to the protected request."""
        self.validate(run_id)
        witness = self.distributions["wheel"].witness
        require(
            request.source
            == {
                "commit": witness.target,
                "version": witness.nbgv.pep440_version,
            }
            and request.document["fixture-digests"]
            == {key: item.digest for key, item in self.distributions.items()},
            "bootstrap protected original identity differs",
        )

    def files(self) -> dict[str, bytes]:
        """Preserve original native bytes separately from parsed metadata."""
        result = dict(self.evidence)
        records: dict[str, JsonValue] = {}
        for variant, item in self.distributions.items():
            result[f"fixtures/{variant}.bin"] = item.content
            records[variant] = {
                "filename": item.filename,
                "variant": variant,
                "digest": item.digest,
                "witness": item.witness.to_document(),
            }
        result["fixtures.json"] = canonicalize(records)
        return result


def fixtures_from_files(files: dict[str, bytes]) -> BootstrapFixtures:
    """Read the original pair and complete typed provenance."""
    require(set(files) >= FIXTURE_NAMES, "bootstrap preparation incomplete")
    records = python_object(
        parse_canonical_json(files["fixtures.json"]), set(VARIANTS)
    )
    distributions = {}
    for variant in VARIANTS:
        record = python_object(
            records[variant], {"filename", "variant", "digest", "witness"}
        )
        item = inspect_python_distribution(
            python_text(record["filename"]),
            files[f"fixtures/{variant}.bin"],
            variant,
            python_package_target_witness_from_document(record["witness"]),
        )
        require(
            record["variant"] == variant and record["digest"] == item.digest,
            "bootstrap original bytes differ",
        )
        distributions[variant] = item
    return BootstrapFixtures(
        distributions, {key: files[key] for key in EVIDENCE_NAMES}
    )


def build_bootstrap_fixture(
    root: Path, target: str, run_id: int
) -> BootstrapFixtures:
    """Run actual Provider, frozen Build and two isolated consumers."""
    with _isolated_exact_target_repository(
        root,
        target,
        _run_command(
            ("git", "remote", "get-url", AUTHORITATIVE_REMOTE), root
        ).strip(),
        runner=_run_command,
    ) as source:
        provider = provide_python_repository_facts(
            source,
            bootstrap_binding(target, run_id),
            CheckoutMaterialization(0, credentials_persisted=False),
        )
    witness = bootstrap_witness(provider)
    build = build_python_distributions(
        root,
        PythonBuildRequest(
            witness, provider.source_input_manifest, provider.build_constraints
        ),
    )
    evidence = {
        "provider.json": canonicalize(provider.to_document()),
        "build.json": canonicalize(
            {
                "source-manifest": [
                    list(pair) for pair in provider.source_input_manifest
                ],
                "staged-manifest-digest": build.staged_manifest_digest,
                "versions": parse_canonical_json(build.producer_versions),
                "commands": [
                    parse_canonical_json(c) for c in build.command_evidence
                ],
            }
        ),
    }
    distributions = {item.variant: item for item in build.distributions}
    for variant, item in distributions.items():
        evidence[f"consumer/{variant}.json"] = consumer_evidence(
            qualify_python_consumer(item)
        )
    result = BootstrapFixtures(distributions, evidence)
    result.validate(run_id)
    return result


def prepare_bootstrap(
    root: Path, request: BootstrapRequest, run_id: int, tooling_sha: str
) -> dict[str, bytes]:
    """Reproduce both prospective originals before the Environment wait."""
    fixtures = build_bootstrap_fixture(
        root, python_text(request.source["commit"]), run_id
    )
    fixtures.match(request, run_id)
    return {
        **fixtures.files(),
        "request.json": request.content,
        "binding.json": canonicalize(
            phase_binding(request, run_id, tooling_sha, "prepare")
        ),
    }


def validate_prepared(
    files: dict[str, bytes],
    request: BootstrapRequest,
    run_id: int,
    tooling_sha: str,
) -> BootstrapFixtures:
    """Reject incomplete, foreign or relabeled original preparation bundles."""
    require(
        set(files) == FIXTURE_NAMES | {"request.json", "binding.json"}
        and files["request.json"] == request.content
        and parse_canonical_json(files["binding.json"])
        == phase_binding(request, run_id, tooling_sha, "prepare"),
        "bootstrap preparation binding differs",
    )
    fixtures = fixtures_from_files(files)
    fixtures.match(request, run_id)
    return fixtures
