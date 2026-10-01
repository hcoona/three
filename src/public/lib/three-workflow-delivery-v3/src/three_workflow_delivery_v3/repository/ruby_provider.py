"""Exact-target Ruby metadata and frozen native NBGV version facts."""

from __future__ import annotations

import re
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath
from typing import cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BUILD_DEFINITION,
    RUBY_MANIFEST,
    RUBY_MAX_EPOCH,
    RUBY_ROOT,
    RUBY_SOURCE_FILES,
    materialize_ruby_version,
    ruby_digest,
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
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.dotnet_provider import run_native
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutEvidence,
    CheckoutMaterialization,
    ProviderBinding,
    _isolated_exact_target_repository,
    _provider_binding_document,
    _run_command,
    validate_checkout_evidence,
    validate_provider_binding,
    verify_exact_checkout,
)
from three_workflow_delivery_v3.repository.ruby_controls import (
    RUBY_CONTROL_PATHS,
)

RUBY_GLOBALS = (
    ".config/dotnet-tools.json",
    "global.json",
    "mise.toml",
    "mise.lock",
    "pyproject.toml",
    "uv.lock",
)
_SEMVER = re.compile(
    r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
)


@dataclass(frozen=True, slots=True)
class RubyNbgvFacts:
    """Raw NBGV output and its explicitly selected native projection."""

    raw_bytes: bytes
    native_version: str

    def __post_init__(self) -> None:
        """Reject malformed source facts or an altered projection."""
        raw = parse_canonical_json(self.raw_bytes)
        semver = ruby_text(raw.get("SemVer2"))
        if (
            not _SEMVER.fullmatch(semver)
            or not re.fullmatch(
                r"[0-9a-f]{40}", ruby_text(raw.get("GitCommitId"))
            )
            or type(raw.get("VersionHeight")) is not int
            or cast("int", raw["VersionHeight"]) < 0
            or type(raw.get("PublicRelease")) is not bool
            or self.native_version != semver.replace("-", ".pre.")
        ):
            message = "invalid frozen Ruby NBGV facts or projection"
            raise ValueError(message)

    @property
    def target(self) -> str:
        """Return the immutable version-evaluation target."""
        return ruby_text(parse_canonical_json(self.raw_bytes)["GitCommitId"])

    def to_document(self) -> dict[str, JsonValue]:
        """Keep raw facts, selected field and projection identity explicit."""
        return {
            "schema": "workflow-delivery/v3/ruby-nbgv-facts",
            "raw": parse_canonical_json(self.raw_bytes),
            "selected-field": "SemVer2",
            "projection": "rubygems/Gem::Version-v1",
            "native-version": self.native_version,
        }


def ruby_nbgv_facts_from_document(value: JsonValue) -> RubyNbgvFacts:
    """Import frozen Ruby version data without target evaluation."""
    doc = ruby_object(
        value,
        {"schema", "raw", "selected-field", "projection", "native-version"},
    )
    result = RubyNbgvFacts(
        canonicalize(doc["raw"]), ruby_text(doc["native-version"])
    )
    if result.to_document() != doc:
        message = "unsupported Ruby version authority"
        raise ValueError(message)
    return result


def is_ruby_input_path(path: str) -> bool:
    """Select existing and newly introduced Ruby source/control inputs."""
    roots = (
        RUBY_ROOT + "/",
        "src/public/lib/three-workflow-delivery-v3/src/",
        ".github/actions/workflow-delivery-v3-ruby-",
        ".github/workflows/workflow-delivery-v3-ruby-",
        "eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby",
    )
    return path in (*RUBY_GLOBALS, *RUBY_CONTROL_PATHS) or path.startswith(
        roots
    )


def ruby_input_candidates(tracked: tuple[str, ...]) -> tuple[str, ...]:
    """Close the smoke, version lineage, native tooling and control inputs."""
    paths = tuple(sorted(p for p in tracked if is_ruby_input_path(p)))
    required = {
        *RUBY_GLOBALS,
        f"{RUBY_ROOT}/version.json",
        *(f"{RUBY_ROOT}/{p}" for p in RUBY_SOURCE_FILES),
    }
    if not required.issubset(paths):
        message = "incomplete Ruby source closure"
        raise ValueError(message)
    return paths


def validate_ruby_inputs(inputs: tuple[tuple[str, str], ...]) -> None:
    """Reject aliases, malformed digests and incomplete source manifests."""
    paths = tuple(p for p, _ in inputs)
    if (
        type(inputs) is not tuple
        or paths != tuple(sorted(set(paths)))
        or ruby_input_candidates(paths) != paths
    ):
        message = "invalid Ruby source input manifest"
        raise ValueError(message)
    for path, digest in inputs:
        if (
            not path
            or PurePosixPath(path).is_absolute()
            or "\\" in path
            or any(p in {"", ".", ".."} for p in path.split("/"))
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
        ):
            message = "unsafe Ruby source identity"
            raise ValueError(message)


@dataclass(frozen=True, slots=True)
class RubyProviderResult:
    """Technical discovery bound to one full-history target and request."""

    binding: ProviderBinding
    checkout: CheckoutEvidence
    nbgv: RubyNbgvFacts
    source_input_manifest: tuple[tuple[str, str], ...]
    source_date_epoch: int
    native_profile: bytes
    specification: bytes

    def __post_init__(self) -> None:
        """Require source, native output and request to describe one target."""
        validate_provider_binding(self.binding)
        validate_checkout_evidence(self.checkout)
        validate_ruby_inputs(self.source_input_manifest)
        validate_ruby_profile(parse_canonical_json(self.native_profile))
        validate_ruby_specification(
            parse_canonical_json(self.specification), self.nbgv.native_version
        )
        if (
            self.nbgv.target != self.binding.target
            or self.checkout.target != self.binding.target
            or self.checkout.head != self.binding.target
            or self.checkout.shallow
            or not self.checkout.ancestry_complete
            or not self.checkout.tags_complete
            or self.checkout.credentials_persisted
            or type(self.source_date_epoch) is not int
            or not 0 <= self.source_date_epoch <= RUBY_MAX_EPOCH
        ):
            message = "Ruby Provider target, toolchain or metadata mismatch"
            raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Export immutable data rather than executable native metadata."""
        return {
            "schema": "workflow-delivery/v3/ruby-provider-result",
            "binding": _provider_binding_document(self.binding),
            "checkout": {
                k.replace("_", "-"): v for k, v in asdict(self.checkout).items()
            },
            "provider": "ruby/rubygems-nbgv-v1",
            "execution-class": "target-evaluation/unprivileged-v1",
            "nbgv": self.nbgv.to_document(),
            "inputs": [[p, d] for p, d in self.source_input_manifest],
            "source-date-epoch": self.source_date_epoch,
            "native-profile": parse_canonical_json(self.native_profile),
            "specification": parse_canonical_json(self.specification),
            "build-definition": RUBY_BUILD_DEFINITION,
        }

    @property
    def result_digest(self) -> str:
        """Identify the entire frozen discovery envelope."""
        return canonical_sha256(self.to_document())


def ruby_provider_result_from_document(value: JsonValue) -> RubyProviderResult:
    """Import strict native facts without evaluating target or native code."""
    doc = ruby_object(
        value,
        {
            "schema",
            "binding",
            "checkout",
            "provider",
            "execution-class",
            "nbgv",
            "inputs",
            "source-date-epoch",
            "native-profile",
            "specification",
            "build-definition",
        },
    )
    binding_value = ruby_object(doc["binding"])
    keys = {
        "request-id",
        "purpose",
        "workflow-run-id",
        "target",
        "producer",
        "control",
        "catalog-digest",
        "request-digest",
    }
    if "run-attempt" in binding_value:
        keys.add("run-attempt")
    b = ruby_object(binding_value, keys)
    binding = ProviderBinding(
        ruby_text(b["request-id"]),
        ruby_text(b["purpose"]),
        cast("int", b["workflow-run-id"]),
        cast("int | None", b.get("run-attempt")),
        ruby_text(b["target"]),
        ruby_text(b["producer"]),
        ruby_text(b["control"]),
        ruby_text(b["catalog-digest"]),
        ruby_text(b["request-digest"]),
    )
    c = ruby_object(
        doc["checkout"],
        {
            "target",
            "head",
            "shallow",
            "ancestry-complete",
            "tags-complete",
            "credentials-persisted",
            "authoritative-remote",
            "authoritative-remote-url",
            "tag-refspec",
        },
    )
    checkout = CheckoutEvidence(
        ruby_text(c["target"]),
        ruby_text(c["head"]),
        cast("bool", c["shallow"]),
        cast("bool", c["ancestry-complete"]),
        cast("bool", c["tags-complete"]),
        cast("bool", c["credentials-persisted"]),
        ruby_text(c["authoritative-remote"]),
        ruby_text(c["authoritative-remote-url"]),
        ruby_text(c["tag-refspec"]),
    )
    inputs = doc["inputs"]
    pair_length = 2
    if not isinstance(inputs, list) or any(
        not isinstance(p, list) or len(p) != pair_length for p in inputs
    ):
        message = "Ruby input manifest must contain path/digest pairs"
        raise ValueError(message)
    result = RubyProviderResult(
        binding,
        checkout,
        ruby_nbgv_facts_from_document(doc["nbgv"]),
        tuple(
            (ruby_text(p[0]), ruby_text(p[1]))
            for p in cast("list[list[JsonValue]]", inputs)
        ),
        cast("int", doc["source-date-epoch"]),
        canonicalize(doc["native-profile"]),
        canonicalize(doc["specification"]),
    )
    if result.to_document() != doc:
        message = "unsupported Ruby Provider record variant"
        raise ValueError(message)
    return result


def provide_ruby_repository_facts(
    repo_root: Path,
    binding: ProviderBinding,
    materialization: CheckoutMaterialization,
) -> RubyProviderResult:
    """Evaluate NBGV and a native gemspec once in an isolated target tree."""
    validate_provider_binding(binding)
    source = verify_exact_checkout(repo_root, binding.target, materialization)
    with (
        _isolated_exact_target_repository(
            repo_root,
            binding.target,
            source.authoritative_remote_url,
            runner=_run_command,
        ) as isolated,
        tempfile.TemporaryDirectory(prefix="ruby-provider-") as temporary,
    ):
        home = Path(temporary)
        checkout = verify_exact_checkout(
            isolated, binding.target, materialization
        )
        if binding.purpose in {
            "live-release",
            "destination-acceptance",
            "destination-bootstrap",
        }:
            _run_command(
                ("git", "checkout", "-B", "main", binding.target), isolated
            )
        paths = ruby_input_candidates(
            tuple(_run_command(("git", "ls-files", "-z"), isolated).split("\0"))
        )
        for path in paths:
            if (isolated / path).is_symlink() or not (
                isolated / path
            ).is_file():
                message = "Ruby inputs must be regular tracked files"
                raise ValueError(message)
        inputs = tuple(
            (p, ruby_digest((isolated / p).read_bytes())) for p in paths
        )
        if (
            run_native(("dotnet", "nbgv", "--version"), isolated)
            .strip()
            .split("+")[0]
            != "3.10.94"
        ):
            message = "Ruby Provider NBGV pin mismatch"
            raise ValueError(message)
        raw = ruby_object(
            parse_json_strict(
                run_native(
                    (
                        "dotnet",
                        "nbgv",
                        "get-version",
                        "--format",
                        "json",
                        "--project",
                        RUBY_ROOT,
                    ),
                    isolated,
                )
            )
        )
        projection = run_ruby(
            {"operation": "version", "raw": raw.get("SemVer2")},
            cwd=isolated,
            home=home,
        )
        facts = RubyNbgvFacts(
            canonicalize(raw), ruby_text(projection.get("native"))
        )
        profile = ruby_profile(cwd=isolated, home=home)
        materialize_ruby_version(isolated / RUBY_ROOT, facts.native_version)
        specification = run_ruby(
            {"operation": "specification", "gemspec": RUBY_MANIFEST},
            cwd=isolated / RUBY_ROOT,
            home=home,
        )
        epoch = int(
            _run_command(
                ("git", "show", "-s", "--format=%ct", binding.target), isolated
            ).strip()
        )
        if inputs != tuple(
            (p, ruby_digest((isolated / p).read_bytes())) for p in paths
        ):
            message = "Ruby Provider evaluation changed source inputs"
            raise ValueError(message)
        return RubyProviderResult(
            binding,
            checkout,
            facts,
            inputs,
            epoch,
            canonicalize(profile),
            canonicalize(specification),
        )
