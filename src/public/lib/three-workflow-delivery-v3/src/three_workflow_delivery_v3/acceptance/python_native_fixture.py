"""Native fixtures through the credential-free Python Provider and Build."""

from __future__ import annotations

import copy
import gzip
import io
import zipfile
from dataclasses import dataclass
from typing import TYPE_CHECKING, cast

from three_workflow_delivery_v3.acceptance.python_native_contract import (
    FIXTURE_KEYS,
    NativeRequest,
    require,
)
from three_workflow_delivery_v3.adapters.python import (
    PythonBuildRequest,
    PythonConsumerResult,
    PythonDistribution,
    PythonPackageTargetWitness,
    build_python_distributions,
    inspect_python_distribution,
    python_package_target_witness_from_document,
    qualify_python_consumer,
)
from three_workflow_delivery_v3.adapters.python import (
    _archive_members as archive_members,
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
    PYTHON_RELEASE_UNIT,
    PythonProviderResult,
    provide_python_repository_facts,
    python_digest,
    python_object,
    python_provider_result_from_document,
    python_text,
    require_public_python_version,
)

if TYPE_CHECKING:
    from pathlib import Path


def fixture_witness(
    provider: PythonProviderResult,
) -> PythonPackageTargetWitness:
    """Bind source identity without execution, destination or tooling SHA."""
    require_public_python_version(provider.nbgv)
    return PythonPackageTargetWitness(
        provider.binding.target,
        provider.nbgv,
        provider.binding.catalog_digest,
        canonical_sha256(
            {
                "schema": "workflow-delivery/v3/control-identity",
                "identity": f"workflow-delivery-v3:{provider.binding.target}",
            }
        ),
        "destination-acceptance",
    )


def comparison_distribution(original: PythonDistribution) -> PythonDistribution:
    """Change representation while preserving every extracted member byte."""
    if original.variant == "wheel":
        output = io.BytesIO()
        with (
            zipfile.ZipFile(io.BytesIO(original.content)) as source,
            zipfile.ZipFile(output, "w") as destination,
        ):
            for info in source.infolist():
                selected = copy.copy(info)
                selected.compress_type = (
                    zipfile.ZIP_STORED
                    if info.compress_type != zipfile.ZIP_STORED
                    else zipfile.ZIP_DEFLATED
                )
                destination.writestr(selected, source.read(info.filename))
        content = output.getvalue()
    else:
        # Change only gzip MTIME; leave the compressed tar untouched.
        content = (
            original.content[:4]
            + (int.from_bytes(original.content[4:8], "little") ^ 1).to_bytes(
                4, "little"
            )
            + original.content[8:]
        )
        require(
            gzip.decompress(content) == gzip.decompress(original.content),
            "comparison changed sdist payload",
        )
    require(
        content != original.content
        and archive_members(content, original.variant)
        == archive_members(original.content, original.variant),
        "comparison must change only archive representation",
    )
    return inspect_python_distribution(
        original.filename, content, original.variant, original.witness
    )


def consumer_evidence(result: PythonConsumerResult) -> bytes:
    """Retain actual installed metadata, original identity and commands."""
    return canonicalize(
        {
            "variant": result.variant,
            "original-digest": result.original_digest,
            "installed": parse_canonical_json(result.installed),
            "commands": [
                parse_canonical_json(c) for c in result.command_evidence
            ],
        }
    )


def validate_consumer_evidence(
    content: bytes, item: PythonDistribution
) -> None:
    """Require successful commands and the exact closed installed identity."""
    proof = python_object(
        parse_canonical_json(content),
        {"variant", "original-digest", "installed", "commands"},
    )
    installed = python_object(
        proof["installed"], {"version", "project-id", "witness", "module"}
    )
    require(
        proof["original-digest"] == item.digest
        and proof["variant"] == item.variant
        and installed["version"] == item.witness.nbgv.pep440_version
        and installed["project-id"] == PYTHON_RELEASE_UNIT
        and installed["witness"] == item.witness.to_document()
        and isinstance(installed["module"], str)
        and bool(installed["module"]),
        "fixture clean consumer proof differs",
    )
    validate_commands(proof["commands"])


def validate_commands(commands: JsonValue) -> None:
    """Check the existing adapter's closed successful command evidence."""
    require(
        isinstance(commands, list) and bool(commands),
        "consumer command evidence missing",
    )
    for value in cast("list[JsonValue]", commands):
        command = python_object(
            value, {"argv", "exit-code", "stdout", "stderr"}
        )
        argv = command["argv"]
        require(
            isinstance(argv, list)
            and bool(argv)
            and all(isinstance(arg, str) and bool(arg) for arg in argv)
            and type(command["exit-code"]) is int
            and command["exit-code"] == 0
            and isinstance(command["stdout"], str)
            and isinstance(command["stderr"], str),
            "consumer command did not establish successful qualification",
        )


@dataclass(frozen=True)
class NativeFixtures:
    """Eight inspected originals/comparisons and credential-free evidence."""

    distributions: dict[str, PythonDistribution]
    evidence: dict[str, bytes]

    def __post_init__(self) -> None:
        """Close candidate identity and preserve exact member equivalence."""
        require(
            set(self.distributions) == set(FIXTURE_KEYS),
            "acceptance needs exactly eight fixtures",
        )
        for label in ("a", "b"):
            for variant in ("wheel", "sdist"):
                original = self.distributions[f"{label}/original/{variant}"]
                other = self.distributions[f"{label}/comparison/{variant}"]
                for item in (original, other):
                    inspect_python_distribution(
                        item.filename, item.content, item.variant, item.witness
                    )
                    require(
                        item.variant == variant
                        and item.witness.purpose == "destination-acceptance",
                        "foreign acceptance fixture",
                    )
                    require_public_python_version(item.witness.nbgv)
                require(
                    original.witness == other.witness
                    and original.filename == other.filename
                    and original.digest != other.digest
                    and archive_members(original.content, variant)
                    == archive_members(other.content, variant),
                    "invalid comparison fixture",
                )
            require(
                self.distributions[f"{label}/original/wheel"].witness
                == self.distributions[f"{label}/original/sdist"].witness,
                "fixture pair witness differs",
            )
        a = self.distributions["a/original/wheel"].witness
        b = self.distributions["b/original/wheel"].witness
        require(
            a.target != b.target
            and a.nbgv.pep440_version != b.nbgv.pep440_version,
            "fixture source identities must differ",
        )

    def match(
        self, request: NativeRequest, *, run_id: int | None = None
    ) -> None:
        """Verify protected digests and source projection before capability."""
        require(
            request.document["fixture-digests"]
            == {k: d.digest for k, d in self.distributions.items()},
            "protected fixture digests differ",
        )
        require(
            set(self.evidence)
            == {
                *(f"consumer/{key}.json" for key in FIXTURE_KEYS),
                "provider/a.json",
                "provider/b.json",
                "build/a.json",
                "build/b.json",
            },
            "prepared provenance inventory differs",
        )
        for label in ("a", "b"):
            witness = self.distributions[f"{label}/original/wheel"].witness
            self._validate_provenance(label, witness, run_id)
            require(
                request.target(label)
                == {
                    "commit": witness.target,
                    "version": witness.nbgv.pep440_version,
                },
                "protected fixture source differs",
            )
        for key, item in self.distributions.items():
            validate_consumer_evidence(
                self.evidence[f"consumer/{key}.json"], item
            )

    def _validate_provenance(
        self,
        label: str,
        witness: PythonPackageTargetWitness,
        run_id: int | None,
    ) -> None:
        provider = python_provider_result_from_document(
            parse_canonical_json(self.evidence[f"provider/{label}.json"])
        )
        target = witness.target
        require(
            fixture_witness(provider) == witness
            and provider.binding
            == ProviderBinding(
                f"python-native-fixture:{target}",
                "destination-acceptance",
                run_id
                if run_id is not None
                else provider.binding.workflow_run_id,
                1,
                target,
                "prepare-python-native",
                f"workflow-delivery-v3:{target}",
                witness.catalog_digest,
                canonical_sha256(
                    {"target": target, "purpose": "destination-acceptance"}
                ),
            ),
            "prepared Provider provenance differs",
        )
        build = python_object(
            parse_canonical_json(self.evidence[f"build/{label}.json"]),
            {
                "source-manifest",
                "staged-manifest-digest",
                "versions",
                "commands",
            },
        )
        manifests = [
            content
            for path, content in archive_members(
                self.distributions[f"{label}/original/sdist"].content, "sdist"
            ).items()
            if path.endswith("/pyproject.toml")
        ]
        require(
            build["source-manifest"]
            == [list(pair) for pair in provider.source_input_manifest]
            and len(manifests) == 1
            and build["staged-manifest-digest"] == python_digest(manifests[0]),
            "prepared Build source provenance differs",
        )
        validate_commands(build["commands"])
        validate_commands([build["versions"]])
        require(
            bool(cast("dict[str, JsonValue]", build["versions"])["stdout"]),
            "prepared producer versions missing",
        )

    def files(self) -> dict[str, bytes]:
        """Serialize original bytes separately from their parsed bindings."""
        result = dict(self.evidence)
        metadata: dict[str, JsonValue] = {}
        for key, item in self.distributions.items():
            result[f"fixtures/{key}.bin"] = item.content
            metadata[key] = {
                "filename": item.filename,
                "variant": item.variant,
                "witness": item.witness.to_document(),
                "digest": item.digest,
            }
        result["fixtures.json"] = canonicalize(metadata)
        return result


def fixtures_from_files(files: dict[str, bytes]) -> NativeFixtures:
    """Inspect each retained original instead of trusting an asserted digest."""
    metadata = parse_canonical_json(files["fixtures.json"])
    require(
        set(metadata) == set(FIXTURE_KEYS),
        "incomplete prepared fixture inventory",
    )
    distributions = {}
    for key, value in metadata.items():
        item = cast("dict[str, JsonValue]", value)
        distribution = inspect_python_distribution(
            python_text(item["filename"]),
            files[f"fixtures/{key}.bin"],
            python_text(item["variant"]),
            python_package_target_witness_from_document(item["witness"]),
        )
        require(
            distribution.digest == item["digest"],
            "prepared fixture bytes differ",
        )
        distributions[key] = distribution
    return NativeFixtures(
        distributions,
        {
            k: v
            for k, v in files.items()
            if k.startswith(("provider/", "build/", "consumer/"))
        },
    )


def build_fixture_set(
    repo_root: Path, targets: dict[str, str], run_id: int
) -> NativeFixtures:
    """Build and clean-consume both representations at two exact ancestors."""
    require(
        set(targets) == {"a", "b"} and targets["a"] != targets["b"],
        "two distinct fixture targets required",
    )
    distributions: dict[str, PythonDistribution] = {}
    evidence: dict[str, bytes] = {}
    for label, target in targets.items():
        binding = ProviderBinding(
            f"python-native-fixture:{target}",
            "destination-acceptance",
            run_id,
            1,
            target,
            "prepare-python-native",
            f"workflow-delivery-v3:{target}",
            catalog_digest(),
            canonical_sha256(
                {"target": target, "purpose": "destination-acceptance"}
            ),
        )
        with _isolated_exact_target_repository(
            repo_root,
            target,
            _run_command(
                ("git", "remote", "get-url", AUTHORITATIVE_REMOTE), repo_root
            ).strip(),
            runner=_run_command,
        ) as source:
            provider = provide_python_repository_facts(
                source,
                binding,
                CheckoutMaterialization(0, credentials_persisted=False),
            )
        witness = fixture_witness(provider)
        build = build_python_distributions(
            repo_root,
            PythonBuildRequest(
                witness,
                provider.source_input_manifest,
                provider.build_constraints,
            ),
        )
        evidence[f"provider/{label}.json"] = canonicalize(
            provider.to_document()
        )
        evidence[f"build/{label}.json"] = canonicalize(
            {
                "source-manifest": [
                    list(p) for p in provider.source_input_manifest
                ],
                "staged-manifest-digest": build.staged_manifest_digest,
                "versions": parse_canonical_json(build.producer_versions),
                "commands": [
                    parse_canonical_json(c) for c in build.command_evidence
                ],
            }
        )
        for original in build.distributions:
            for candidate, item in (
                ("original", original),
                ("comparison", comparison_distribution(original)),
            ):
                key = f"{label}/{candidate}/{item.variant}"
                distributions[key] = item
                evidence[f"consumer/{key}.json"] = consumer_evidence(
                    qualify_python_consumer(item)
                )
    return NativeFixtures(distributions, evidence)


def prepare_fixtures(
    repo_root: Path, request: NativeRequest, run_id: int, tooling_sha: str
) -> dict[str, bytes]:
    """Bind qualified deterministic fixtures to the current hosted envelope."""
    fixtures = build_fixture_set(
        repo_root,
        {
            label: python_text(request.target(label)["commit"])
            for label in ("a", "b")
        },
        run_id,
    )
    fixtures.match(request, run_id=run_id)
    files = fixtures.files()
    files["request.json"] = request.content
    files["binding.json"] = canonicalize(
        {
            "request-digest": request.digest,
            "tooling-sha": tooling_sha,
            "run-id": run_id,
            "run-attempt": 1,
            "producer": "prepare-python-native",
        }
    )
    return files
