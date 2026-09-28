"""Exact-target Python metadata and one frozen NBGV projection."""

from __future__ import annotations

import hashlib
import importlib.metadata
import platform
import re
import tomllib
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import cast

from nbgv_python.versioning import normalize_version_field
from packaging.version import Version

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.dotnet_provider import run_native
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
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

PYTHON_RELEASE_UNIT = "hcoona-release-smoke-python"
PYTHON_IMPORT = "hcoona_release_smoke_python"
PYTHON_ROOT = f"src/public/lib/{PYTHON_RELEASE_UNIT}"
PYTHON_MANIFEST = f"{PYTHON_ROOT}/pyproject.toml"
PYTHON_BUILD_DEFINITION = "python/distribution-set-v1"
PYTHON_BUILD_CONSTRAINTS = (
    "eng/workflow-delivery/v3/python-build-constraints.txt"
)
PYTHON_POLICY = (
    "eng/workflow-delivery/v3/policies/hcoona-release-smoke-python.yml"
)
PYTHON_TOOLCHAIN = (
    ("python", "3.14.3"),
    ("uv", "0.10.9"),
    ("hatchling", "1.32.0"),
    ("nbgv", "3.10.94"),
    ("nbgv-python", "2.1.0.dev1"),
    ("packaging", "26.3"),
)
PYTHON_SOURCE_FILES = (
    "LICENSE",
    "README.md",
    "pyproject.toml",
    f"src/{PYTHON_IMPORT}/__init__.py",
)
_INPUT_PAIR_LENGTH = 2
_GLOBALS = (
    ".config/dotnet-tools.json",
    "global.json",
    "mise.lock",
    "mise.toml",
    "pyproject.toml",
    "uv.lock",
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_POLICY,
)


def python_digest(content: bytes) -> str:
    """Return the logical SHA-256 of exact bytes."""
    return "sha256:" + hashlib.sha256(content).hexdigest()


def python_object(value: JsonValue, keys: set[str]) -> dict[str, JsonValue]:
    """Require a closed Python record object without coercion."""
    if not isinstance(value, dict) or set(value) != keys:
        message = "invalid closed Python record fields"
        raise ValueError(message)
    return value


def python_text(value: JsonValue) -> str:
    """Require an exact nonempty string."""
    if not isinstance(value, str) or not value or value != value.strip():
        message = "invalid Python record string"
        raise ValueError(message)
    return value


def validate_python_build_constraints(
    content: bytes, lock_content: bytes
) -> None:
    """Bind the complete producer backend closure to native UV lock facts."""
    names = {
        "hatchling",
        "packaging",
        "pathspec",
        "pluggy",
        "tomlkit",
        "trove-classifiers",
    }
    lock = tomllib.loads(lock_content.decode("utf-8"))
    packages = [p for p in lock.get("package", []) if p.get("name") in names]
    if len(packages) != len(names):
        message = "Python backend lock closure is incomplete or ambiguous"
        raise ValueError(message)
    expected: list[str] = []
    for package in sorted(packages, key=lambda p: p["name"]):
        if package.get("source") != {"registry": "https://pypi.org/simple"}:
            message = "Python backend must resolve from the public PyPI index"
            raise ValueError(message)
        if any(
            d.get("name") not in names for d in package.get("dependencies", [])
        ):
            message = "Python backend has an unclosed transitive dependency"
            raise ValueError(message)
        hashes = sorted({wheel["hash"] for wheel in package.get("wheels", [])})
        if not hashes or any(
            not re.fullmatch(r"sha256:[0-9a-f]{64}", h) for h in hashes
        ):
            message = "Python backend lock lacks exact wheel hashes"
            raise ValueError(message)
        expected.append(
            f"{package['name']}=={package['version']} "
            + " ".join("--hash=" + digest for digest in hashes)
        )
    actual = [
        line
        for line in content.decode("utf-8").splitlines()
        if line and not line.startswith("#")
    ]
    if (
        actual != expected
        or next(p for p in packages if p["name"] == "hatchling")["version"]
        != "1.32.0"
    ):
        message = "Python producer constraints differ from the frozen UV lock"
        raise ValueError(message)


@dataclass(frozen=True, slots=True)
class PythonNbgvFacts:
    """Original native facts plus their sole admitted Python projection."""

    raw_bytes: bytes
    pep440_version: str

    def __post_init__(self) -> None:
        """Reject altered projections, missing fields and malformed targets."""
        raw = parse_canonical_json(self.raw_bytes)
        for name in ("SimpleVersion", "SemVer2", "GitCommitId"):
            python_text(raw.get(name))
        if (
            not re.fullmatch(r"[0-9a-f]{40}", str(raw["GitCommitId"]))
            or type(raw.get("VersionHeight")) is not int
            or cast("int", raw["VersionHeight"]) < 0
            or type(raw.get("PublicRelease")) is not bool
            or self.pep440_version
            != normalize_version_field(raw["SemVer2"], field="SemVer2")
            or str(Version(self.pep440_version)) != self.pep440_version
        ):
            message = "invalid frozen Python NBGV facts"
            raise ValueError(message)

    @property
    def target(self) -> str:
        """Return the evaluated immutable commit."""
        return python_text(parse_canonical_json(self.raw_bytes)["GitCommitId"])

    def to_document(self) -> dict[str, JsonValue]:
        """Retain native output, selected field and normalized value."""
        return {
            "schema": "workflow-delivery/v3/python-nbgv-facts",
            "raw": parse_canonical_json(self.raw_bytes),
            "selected-field": "SemVer2",
            "pep440-version": self.pep440_version,
        }


def python_nbgv_facts_from_document(value: JsonValue) -> PythonNbgvFacts:
    """Read the distinct native Python version variant."""
    document = python_object(
        value, {"schema", "raw", "selected-field", "pep440-version"}
    )
    if (
        document["schema"] != "workflow-delivery/v3/python-nbgv-facts"
        or document["selected-field"] != "SemVer2"
    ):
        message = "unsupported Python version authority"
        raise ValueError(message)
    return PythonNbgvFacts(
        canonicalize(document["raw"]),
        python_text(document["pep440-version"]),
    )


def require_public_python_version(facts: PythonNbgvFacts) -> None:
    """Reject registry-inadmissible projections without rewriting CI facts."""
    if (
        type(facts) is not PythonNbgvFacts
        or Version(facts.pep440_version).local
    ):
        message = "Python Live requires a public version without local metadata"
        raise ValueError(message)


def validate_python_source_manifest(content: bytes) -> dict[str, object]:
    """Admit only the bounded smoke's declared Hatch/NBGV metadata shape."""
    document = tomllib.loads(content.decode("utf-8"))
    project = document.get("project", {})
    tool = document.get("tool", {})
    expected_tool = {
        "hatch": {
            "version": {"source": "nbgv", "nbgv": {"version-field": "SemVer2"}},
            "build": {
                "targets": {
                    "wheel": {"packages": [f"src/{PYTHON_IMPORT}"]},
                    "sdist": {
                        "include": [
                            f"src/{PYTHON_IMPORT}",
                            "pyproject.toml",
                            "README.md",
                            "LICENSE",
                        ]
                    },
                }
            },
        },
        "uv": {"sources": {"nbgv-python": {"workspace": True}}},
    }
    if (
        not isinstance(project, dict)
        or set(document) != {"project", "build-system", "tool"}
        or document["build-system"]
        != {
            "requires": ["hatchling==1.32.0", "nbgv-python"],
            "build-backend": "hatchling.build",
        }
        or set(project)
        != {
            "name",
            "description",
            "readme",
            "requires-python",
            "license",
            "dynamic",
            "dependencies",
        }
        or project["name"] != PYTHON_RELEASE_UNIT
        or project["requires-python"] != ">=3.14"
        or project["dynamic"] != ["version"]
        or project["dependencies"] != []
        or project["readme"] != "README.md"
        or project["license"] != "MIT"
        or not isinstance(project["description"], str)
        or tool != expected_tool
    ):
        message = "unsupported Python smoke source metadata or build hook"
        raise ValueError(message)
    return document


def python_input_candidates(tracked: tuple[str, ...]) -> tuple[str, ...]:
    """Select the Python input closure from an exact Git path inventory."""
    roots = (
        PYTHON_ROOT + "/",
        "src/public/lib/nbgv-python/",
        "src/public/lib/three-workflow-delivery-v3/src/",
        ".github/actions/workflow-delivery-v3-python-",
        ".github/workflows/workflow-delivery-v3-python-",
    )
    result = tuple(
        sorted(p for p in tracked if p in _GLOBALS or p.startswith(roots))
    )
    if not set(_GLOBALS).issubset(result) or not {
        f"{PYTHON_ROOT}/{name}" for name in PYTHON_SOURCE_FILES
    }.issubset(result):
        message = "Python source closure is incomplete"
        raise ValueError(message)
    return result


def python_input_paths(repo_root: Path) -> tuple[str, ...]:
    """Close native, workspace, projection and reviewed control inputs."""
    tracked = tuple(
        _run_command(("git", "ls-files", "-z"), repo_root).split("\0")
    )
    result = python_input_candidates(tracked)
    for path in result:
        if (repo_root / path).is_symlink() or not (repo_root / path).is_file():
            message = "Python source inputs must be regular tracked files"
            raise ValueError(message)
    return result


@dataclass(frozen=True, slots=True)
class PythonProviderResult:
    """Purpose-bound exact-target facts for the closed wheel/sdist pair."""

    binding: ProviderBinding
    checkout: CheckoutEvidence
    nbgv: PythonNbgvFacts
    source_input_manifest: tuple[tuple[str, str], ...]
    build_constraints: bytes

    def __post_init__(self) -> None:
        """Reject cross-target or incomplete fact envelopes."""
        validate_provider_binding(self.binding)
        validate_checkout_evidence(self.checkout)
        paths = tuple(path for path, _ in self.source_input_manifest)
        if (
            self.nbgv.target != self.binding.target
            or self.checkout.target != self.binding.target
            or self.checkout.head != self.binding.target
            or self.checkout.shallow
            or not self.checkout.ancestry_complete
            or not self.checkout.tags_complete
            or self.checkout.credentials_persisted
            or self.checkout.authoritative_remote != AUTHORITATIVE_REMOTE
            or self.checkout.tag_refspec != TAG_REFSPEC
            or paths != tuple(sorted(set(paths)))
            or not set(_GLOBALS).issubset(paths)
            or not {
                f"{PYTHON_ROOT}/{path}" for path in PYTHON_SOURCE_FILES
            }.issubset(paths)
            or dict(self.source_input_manifest).get(PYTHON_BUILD_CONSTRAINTS)
            != python_digest(self.build_constraints)
        ):
            message = "Python Provider input binding mismatch"
            raise ValueError(message)
        for path, digest in self.source_input_manifest:
            if (
                PurePosixPath(path).is_absolute()
                or ".." in PurePosixPath(path).parts
                or "\\" in path
                or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
            ):
                message = "malformed Python Provider source identity"
                raise ValueError(message)

    def to_document(self) -> dict[str, JsonValue]:
        """Return the closed technical fact record."""
        return {
            "schema": "workflow-delivery/v3/python-provider-result",
            "binding": _provider_binding_document(self.binding),
            "checkout": {
                "target": self.checkout.target,
                "head": self.checkout.head,
                "shallow": self.checkout.shallow,
                "ancestry-complete": self.checkout.ancestry_complete,
                "tags-complete": self.checkout.tags_complete,
                "credentials-persisted": self.checkout.credentials_persisted,
                "authoritative-remote": self.checkout.authoritative_remote,
                "authoritative-remote-url": (
                    self.checkout.authoritative_remote_url
                ),
                "tag-refspec": self.checkout.tag_refspec,
            },
            "provider": "python/uv-nbgv-v1",
            "execution-class": "target-evaluation/unprivileged-v1",
            "nbgv": self.nbgv.to_document(),
            "inputs": [[p, d] for p, d in self.source_input_manifest],
            "build-constraints": self.build_constraints.decode("utf-8"),
            "toolchain": [
                [name, version] for name, version in PYTHON_TOOLCHAIN
            ],
            "outputs": ["wheel", "sdist"],
        }

    @property
    def result_digest(self) -> str:
        """Return the immutable Provider result digest."""
        return canonical_sha256(self.to_document())


def python_provider_result_from_document(
    value: JsonValue,
) -> PythonProviderResult:
    """Admit the exact serialized Python Provider variant."""
    doc = python_object(
        value,
        {
            "schema",
            "binding",
            "checkout",
            "provider",
            "execution-class",
            "nbgv",
            "inputs",
            "build-constraints",
            "toolchain",
            "outputs",
        },
    )
    binding_value = doc["binding"]
    if not isinstance(binding_value, dict):
        message = "Python Provider binding must be an object"
        raise TypeError(message)
    binding_keys = {
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
        binding_keys.add("run-attempt")
    b = python_object(binding_value, binding_keys)
    binding = ProviderBinding(
        python_text(b["request-id"]),
        python_text(b["purpose"]),
        cast("int", b["workflow-run-id"]),
        cast("int | None", b.get("run-attempt")),
        python_text(b["target"]),
        python_text(b["producer"]),
        python_text(b["control"]),
        python_text(b["catalog-digest"]),
        python_text(b["request-digest"]),
    )
    c = python_object(
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
        python_text(c["target"]),
        python_text(c["head"]),
        cast("bool", c["shallow"]),
        cast("bool", c["ancestry-complete"]),
        cast("bool", c["tags-complete"]),
        cast("bool", c["credentials-persisted"]),
        python_text(c["authoritative-remote"]),
        python_text(c["authoritative-remote-url"]),
        python_text(c["tag-refspec"]),
    )
    inputs = doc["inputs"]
    if not isinstance(inputs, list) or any(
        not isinstance(p, list) or len(p) != _INPUT_PAIR_LENGTH for p in inputs
    ):
        message = "Python Provider inputs must be pairs"
        raise ValueError(message)
    constraints = doc["build-constraints"]
    if not isinstance(constraints, str):
        message = "Python build constraints must be text"
        raise TypeError(message)
    result = PythonProviderResult(
        binding,
        checkout,
        python_nbgv_facts_from_document(doc["nbgv"]),
        tuple(
            (python_text(p[0]), python_text(p[1]))
            for p in cast("list[list[JsonValue]]", inputs)
        ),
        constraints.encode("utf-8"),
    )
    if result.to_document() != doc:
        message = "Python Provider record is not the canonical closed variant"
        raise ValueError(message)
    return result


def provide_python_repository_facts(
    repo_root: Path,
    binding: ProviderBinding,
    materialization: CheckoutMaterialization,
) -> PythonProviderResult:
    """Evaluate NBGV once at the isolated target without credentials."""
    validate_provider_binding(binding)
    source = verify_exact_checkout(repo_root, binding.target, materialization)
    with _isolated_exact_target_repository(
        repo_root,
        binding.target,
        source.authoritative_remote_url,
        runner=_run_command,
    ) as isolated:
        checkout = verify_exact_checkout(
            isolated, binding.target, materialization
        )
        # Give NBGV the protected-main ref in the disposable clone only. This
        # preserves the project's publicReleaseRefSpec instead of editing a
        # computed version. CI keeps its detached, possibly local projection.
        if binding.purpose in {
            "live-release",
            "destination-acceptance",
            "destination-bootstrap",
        }:
            _run_command(
                ("git", "checkout", "-B", "main", binding.target), isolated
            )
        validate_python_source_manifest(
            (isolated / PYTHON_MANIFEST).read_bytes()
        )
        workspace = tomllib.loads((isolated / "pyproject.toml").read_text())
        if PYTHON_ROOT not in workspace.get("tool", {}).get("uv", {}).get(
            "workspace", {}
        ).get("members", []):
            message = "Python smoke is not an explicit UV workspace member"
            raise ValueError(message)
        validate_python_build_constraints(
            (isolated / PYTHON_BUILD_CONSTRAINTS).read_bytes(),
            (isolated / "uv.lock").read_bytes(),
        )
        paths = python_input_paths(isolated)
        inputs = tuple(
            (p, python_digest((isolated / p).read_bytes())) for p in paths
        )
        if platform.python_version() != dict(PYTHON_TOOLCHAIN)["python"]:
            message = (
                "Python Provider interpreter differs from the pinned toolchain"
            )
            raise ValueError(message)
        for name in ("nbgv-python", "packaging"):
            if importlib.metadata.version(name) != dict(PYTHON_TOOLCHAIN)[name]:
                message = "Python projection implementation version mismatch"
                raise ValueError(message)
        if run_native(("uv", "--version"), isolated).strip() != "uv 0.10.9":
            message = "Python Provider UV version mismatch"
            raise ValueError(message)
        if (
            run_native(("dotnet", "nbgv", "--version"), isolated)
            .strip()
            .split("+")[0]
            != "3.10.94"
        ):
            message = "Python Provider NBGV version mismatch"
            raise ValueError(message)
        raw = parse_json_strict(
            run_native(
                (
                    "dotnet",
                    "nbgv",
                    "get-version",
                    "--format",
                    "json",
                    "--project",
                    PYTHON_ROOT,
                ),
                isolated,
            )
        )
        if not isinstance(raw, dict):
            message = "NBGV did not produce an object"
            raise TypeError(message)
        facts = PythonNbgvFacts(
            canonicalize(raw),
            normalize_version_field(raw.get("SemVer2"), field="SemVer2"),
        )
        if inputs != tuple(
            (p, python_digest((isolated / p).read_bytes())) for p in paths
        ):
            message = "Python Provider evaluation changed a source input"
            raise ValueError(message)
        return PythonProviderResult(
            binding,
            checkout,
            facts,
            inputs,
            (isolated / PYTHON_BUILD_CONSTRAINTS).read_bytes(),
        )
