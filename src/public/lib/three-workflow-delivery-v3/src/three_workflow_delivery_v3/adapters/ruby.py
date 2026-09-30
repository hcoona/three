"""Original Ruby gem construction, native inspection and clean consumption."""

from __future__ import annotations

import re
import tempfile
from dataclasses import dataclass
from pathlib import Path

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BUILD_DEFINITION,
    RUBY_BYTE_LIMIT,
    RUBY_MANIFEST,
    RUBY_MAX_EPOCH,
    RUBY_PACKAGE_FILES,
    RUBY_RELEASE_UNIT,
    RUBY_ROOT,
    RUBY_SOURCE_FILES,
    RUBY_VERSION_PATH,
    RUBY_WITNESS_PATH,
    materialize_ruby_version,
    ruby_digest,
    ruby_environment,
    ruby_executable,
    ruby_object,
    ruby_profile,
    ruby_text,
    run_ruby,
    validate_ruby_profile,
    validate_ruby_specification,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.compiler import (
    _git_target_file_bytes,
)
from three_workflow_delivery_v3.repository.dotnet_provider import run_native
from three_workflow_delivery_v3.repository.ruby_provider import (
    RubyNbgvFacts,
    ruby_nbgv_facts_from_document,
    validate_ruby_inputs,
)


@dataclass(frozen=True, slots=True)
class RubyPackageTargetWitness:
    """Stable provenance without Attempt, destination or wall clock."""

    target: str
    nbgv: RubyNbgvFacts
    catalog_digest: str
    control_digest: str
    purpose: str

    def __post_init__(self) -> None:
        """Require provenance for one qualified build purpose."""
        if (
            self.target != self.nbgv.target
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", self.catalog_digest)
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", self.control_digest)
            or self.purpose
            not in {
                "ci-pr-slice-shadow",
                "slice-validation",
                "release-simulation",
                "live-release",
                "destination-acceptance",
                "destination-bootstrap",
            }
        ):
            message = "invalid Ruby package target witness"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Export source identity without adopting a prior run's authority."""
        return {
            "schema": "workflow-delivery/v3/ruby-package-target-witness",
            "target": self.target,
            "release-unit": RUBY_RELEASE_UNIT,
            "nbgv": self.nbgv.to_document(),
            "build-definition": RUBY_BUILD_DEFINITION,
            "catalog-digest": self.catalog_digest,
            "control-digest": self.control_digest,
            "purpose": self.purpose,
        }

    @property
    def canonical_bytes(self) -> bytes:
        """Return the exact embedded canonical witness."""
        return canonicalize(self.to_document())


def ruby_package_target_witness_from_document(
    value: JsonValue,
) -> RubyPackageTargetWitness:
    """Read the closed witness without executing the product or gemspec."""
    doc = ruby_object(
        value,
        {
            "schema",
            "target",
            "release-unit",
            "nbgv",
            "build-definition",
            "catalog-digest",
            "control-digest",
            "purpose",
        },
    )
    result = RubyPackageTargetWitness(
        ruby_text(doc["target"]),
        ruby_nbgv_facts_from_document(doc["nbgv"]),
        ruby_text(doc["catalog-digest"]),
        ruby_text(doc["control-digest"]),
        ruby_text(doc["purpose"]),
    )
    if result.to_document() != doc:
        message = "unsupported Ruby witness variant"
        raise ValueError(message)
    return result


@dataclass(frozen=True, slots=True)
class RubyBuildRequest:
    """Frozen source, version, epoch and native compression inputs."""

    witness: RubyPackageTargetWitness
    source_input_manifest: tuple[tuple[str, str], ...]
    source_date_epoch: int
    native_profile: bytes

    def __post_init__(self) -> None:
        """Close inputs before execution and reject ambient time/tool drift."""
        validate_ruby_inputs(self.source_input_manifest)
        validate_ruby_profile(parse_canonical_json(self.native_profile))
        if (
            type(self.source_date_epoch) is not int
            or not 0 <= self.source_date_epoch <= RUBY_MAX_EPOCH
        ):
            message = "invalid Ruby source epoch"
            raise ValueError(message)


@dataclass(frozen=True, slots=True)
class RubyDistribution:
    """One original unsigned gem and its native inspection evidence."""

    filename: str
    content: bytes
    witness: RubyPackageTargetWitness
    inspection: bytes

    @property
    def digest(self) -> str:
        """Identify the entire unmodified native archive."""
        return ruby_digest(self.content)


def inspect_ruby_distribution(
    filename: str,
    content: bytes,
    witness: RubyPackageTargetWitness,
) -> RubyDistribution:
    """Inspect natively without publication authority or a target checkout."""
    expected = f"{RUBY_RELEASE_UNIT}-{witness.nbgv.native_version}.gem"
    if filename != expected or not 0 < len(content) <= RUBY_BYTE_LIMIT:
        message = "invalid Ruby artifact identity or size"
        raise ValueError(message)
    with tempfile.TemporaryDirectory(prefix="ruby-inspect-") as temporary:
        root = Path(temporary)
        path = root / filename
        path.write_bytes(content)
        ruby_profile(cwd=root, home=root / "home")
        result = run_ruby(
            {"operation": "inspect", "archive": str(path)},
            cwd=root,
            home=root / "home",
        )
        extra = {"members", "witness", "version-source"}
        validate_ruby_specification(
            {k: v for k, v in result.items() if k not in extra},
            witness.nbgv.native_version,
        )
        members = ruby_object(result.get("members"), set(RUBY_PACKAGE_FILES))
        version_bytes = materialize_ruby_version(
            root / "expected", witness.nbgv.native_version
        )
        if (
            result.get("witness") != witness.canonical_bytes.decode()
            or result.get("version-source") != version_bytes.decode()
            or members[RUBY_WITNESS_PATH]
            != {
                "sha256": ruby_digest(witness.canonical_bytes),
                "size": len(witness.canonical_bytes),
            }
            or members[RUBY_VERSION_PATH]
            != {
                "sha256": ruby_digest(version_bytes),
                "size": len(version_bytes),
            }
        ):
            message = "Ruby artifact static version or source witness mismatch"
            raise ValueError(message)
        return RubyDistribution(
            filename, content, witness, canonicalize(result)
        )


def build_ruby_package(  # noqa: C901 - closed source/build/inspection sequence
    repo_root: Path,
    request: RubyBuildRequest,
    *,
    scratch_root: Path | None = None,
) -> RubyDistribution:
    """Build immutable Git bytes without NBGV reevaluation or repacking."""
    sources: dict[str, bytes] = {}
    for path, digest in request.source_input_manifest:
        content = _git_target_file_bytes(
            repo_root, request.witness.target, path
        )
        if ruby_digest(content) != digest:
            message = "Ruby frozen source differs from the target Git tree"
            raise ValueError(message)
        sources[path] = content
    epoch = int(
        run_native(
            ("git", "show", "-s", "--format=%ct", request.witness.target),
            repo_root,
        ).strip()
    )
    if epoch != request.source_date_epoch:
        message = (
            "Ruby source epoch differs from the target committer timestamp"
        )
        raise ValueError(message)
    with tempfile.TemporaryDirectory(
        prefix="ruby-build-", dir=scratch_root
    ) as temporary:
        root = Path(temporary)
        stage = root / "stage"
        stage.mkdir()
        for name in RUBY_SOURCE_FILES:
            path = stage / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(sources[f"{RUBY_ROOT}/{name}"])
            path.chmod(0o644)
        materialize_ruby_version(stage, request.witness.nbgv.native_version)
        (stage / RUBY_WITNESS_PATH).write_bytes(request.witness.canonical_bytes)
        (stage / RUBY_WITNESS_PATH).chmod(0o644)
        for path in (stage, *stage.rglob("*")):
            if path.is_dir():
                path.chmod(0o755)
        profile = ruby_profile(cwd=stage, home=root / "home")
        if canonicalize(profile) != request.native_profile:
            message = "Ruby build native/compression profile drift"
            raise ValueError(message)
        filename = (
            f"{RUBY_RELEASE_UNIT}-{request.witness.nbgv.native_version}.gem"
        )
        archive = root / filename
        result = run_ruby(
            {
                "operation": "build",
                "gemspec": RUBY_MANIFEST,
                "output": str(archive),
            },
            cwd=stage,
            home=root / "home",
            epoch=request.source_date_epoch,
        )
        validate_ruby_specification(result, request.witness.nbgv.native_version)
        distribution = inspect_ruby_distribution(
            filename, archive.read_bytes(), request.witness
        )
        members = ruby_object(
            parse_canonical_json(distribution.inspection)["members"]
        )
        for name in RUBY_SOURCE_FILES:
            if name != RUBY_MANIFEST:
                content = sources[f"{RUBY_ROOT}/{name}"]
                if members.get(name) != {
                    "sha256": ruby_digest(content),
                    "size": len(content),
                }:
                    message = "Ruby build changed a packaged source input"
                    raise ValueError(message)
        return distribution


def qualify_ruby_consumer(
    distribution: RubyDistribution,
) -> dict[str, JsonValue]:
    """Install the admitted original without producer tools."""
    verified = inspect_ruby_distribution(
        distribution.filename, distribution.content, distribution.witness
    )
    with tempfile.TemporaryDirectory(prefix="ruby-consumer-") as temporary:
        root = Path(temporary)
        archive = root / verified.filename
        archive.write_bytes(verified.content)
        home = root / "home"
        environment = ruby_environment(home)
        ruby = ruby_executable()
        gem = ruby.with_name("gem")
        if not gem.is_file():
            message = "pinned Ruby gem entry is missing"
            raise ValueError(message)
        output = run_native(
            (
                str(ruby),
                str(gem),
                "install",
                "--norc",
                "--local",
                "--no-document",
                "--no-user-install",
                "--install-dir",
                str(home / "gems"),
                str(archive),
            ),
            root,
            environment,
        )
        result = run_ruby({"operation": "consumer"}, cwd=root, home=home)
        native = verified.witness.nbgv.native_version
        expected_root = home / "gems" / "gems" / f"{RUBY_RELEASE_UNIT}-{native}"
        if (
            result.get("project-id") != RUBY_RELEASE_UNIT
            or result.get("installed-version") != native
            or result.get("version") != native
            or result.get("witness")
            != verified.witness.canonical_bytes.decode()
            or ruby_text(result.get("installed-root")) != str(expected_root)
        ):
            message = "Ruby clean consumer identity or witness mismatch"
            raise ValueError(message)
        return {
            "schema": "workflow-delivery/v3/ruby-consumer-evidence",
            "artifact-digest": verified.digest,
            "witness-digest": canonical_sha256(verified.witness.to_document()),
            "project-id": RUBY_RELEASE_UNIT,
            "version": native,
            "install-output": output,
        }
