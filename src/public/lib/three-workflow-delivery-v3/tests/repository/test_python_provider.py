"""Exact native version and bounded Python Provider input contracts."""

from dataclasses import replace
from pathlib import Path

import pytest
import tomli_w
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.repository import python_provider
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
    CheckoutEvidence,
    ProviderBinding,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_MANIFEST,
    PYTHON_POLICY,
    PYTHON_ROOT,
    PYTHON_SOURCE_FILES,
    PythonNbgvFacts,
    PythonProviderResult,
    python_digest,
    python_input_paths,
    python_nbgv_facts_from_document,
    python_provider_result_from_document,
    require_public_python_version,
    validate_python_build_constraints,
    validate_python_source_manifest,
)

_ROOT = Path(__file__).resolve().parents[6]
_WORKFLOW_RUN_ID = 91
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


def _raw(semver="0.1.0-beta.7"):
    return {
        "SimpleVersion": "0.1.0",
        "SemVer2": semver,
        "GitCommitId": "a" * 40,
        "VersionHeight": 7,
        "PublicRelease": False,
    }


def _facts():
    return PythonNbgvFacts(canonicalize(_raw()), "0.1.0b7")


def _provider():
    constraints = b"hatchling==1.32.0\n"
    paths = (*_GLOBALS, *(f"{PYTHON_ROOT}/{p}" for p in PYTHON_SOURCE_FILES))
    return PythonProviderResult(
        ProviderBinding(
            "python-test",
            "release-simulation",
            _WORKFLOW_RUN_ID,
            1,
            "a" * 40,
            "python-provider",
            "a" * 40,
            catalog_digest(),
            "sha256:" + "b" * 64,
        ),
        CheckoutEvidence(
            "a" * 40,
            "a" * 40,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///authoritative.git",
            tag_refspec=TAG_REFSPEC,
        ),
        _facts(),
        tuple(
            sorted(
                (
                    p,
                    python_digest(
                        constraints
                        if p == PYTHON_BUILD_CONSTRAINTS
                        else p.encode()
                    ),
                )
                for p in paths
            )
        ),
        constraints,
    )


@pytest.mark.parametrize(
    ("semver", "expected"),
    [
        ("0.1.0-beta.7", "0.1.0b7"),
        ("1.2.3", "1.2.3"),
        ("0.1.0-rc.2", "0.1.0rc2"),
    ],
)
def test_python_facts_preserve_semver2_projection(semver, expected):
    """Select SemVer2 rather than the incompatible SimpleVersion fallback."""
    facts = PythonNbgvFacts(canonicalize(_raw(semver)), expected)
    document = facts.to_document()
    assert document["selected-field"] == "SemVer2"
    assert document["pep440-version"] == expected
    assert document["raw"]["SemVer2"] == semver
    assert facts.target == "a" * 40
    require_public_python_version(facts)


def test_local_version_is_valid_for_ci_but_rejected_for_live():
    """A valid CI projection is never rewritten to obtain Live eligibility."""
    facts = PythonNbgvFacts(
        canonicalize(_raw("0.1.0-beta.7+abc1234")), "0.1.0b7+abc1234"
    )
    assert (
        python_nbgv_facts_from_document(facts.to_document()).pep440_version
        == "0.1.0b7+abc1234"
    )
    with pytest.raises(ValueError, match="public version without local"):
        require_public_python_version(facts)
    assert facts.to_document()["raw"]["SemVer2"] == "0.1.0-beta.7+abc1234"
    assert facts.pep440_version == "0.1.0b7+abc1234"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("VersionHeight", True),
        ("VersionHeight", -1),
        ("VersionHeight", "7"),
        ("PublicRelease", 1),
        ("GitCommitId", "A" * 40),
        ("GitCommitId", "a" * 39),
        ("SemVer2", ""),
        ("SimpleVersion", " 0.1.0"),
    ],
)
def test_python_facts_reject_malformed_native_facts(field, value):
    """Native primitive and identity fields are admitted without coercion."""
    raw = _raw()
    raw[field] = value
    with pytest.raises(ValueError, match="Python"):
        PythonNbgvFacts(canonicalize(raw), "0.1.0b7")


@pytest.mark.parametrize("projected", ["0.1.0", "0.1.0-beta.7", "0.1.0b8"])
def test_python_facts_reject_changed_or_noncanonical_projection(projected):
    """Neither fallback, spelling aliases nor rewritten values are admitted."""
    with pytest.raises(ValueError, match="frozen Python NBGV"):
        PythonNbgvFacts(canonicalize(_raw()), projected)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/dotnet-nbgv-facts"),
        ("selected-field", "SimpleVersion"),
        ("attempt", 2),
    ],
)
def test_python_facts_reject_foreign_or_open_schema(field, value):
    """A Python fact cannot silently consume a different authority variant."""
    document = _facts().to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_nbgv_facts_from_document(document)


@pytest.mark.parametrize("field", ["target", "head"])
def test_python_provider_rejects_cross_target_checkout(field):
    """Matching version text cannot substitute another checkout identity."""
    provider = _provider()
    with pytest.raises(ValueError, match="binding mismatch"):
        replace(
            provider, checkout=replace(provider.checkout, **{field: "c" * 40})
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("shallow", True),
        ("ancestry_complete", False),
        ("tags_complete", False),
        ("credentials_persisted", True),
    ],
)
def test_python_provider_rejects_incomplete_or_credentialed_checkout(
    field, value
):
    """Frozen Provider evidence must retain full history and no credentials."""
    provider = _provider()
    with pytest.raises(ValueError, match=r"Python|checkout"):
        replace(provider, checkout=replace(provider.checkout, **{field: value}))


def test_python_provider_rejects_missing_smoke_source():
    """Global inputs alone cannot stand in for a complete package source."""
    provider = _provider()
    with pytest.raises(ValueError, match="Python"):
        replace(
            provider,
            source_input_manifest=tuple(
                pair
                for pair in provider.source_input_manifest
                if pair[0] != PYTHON_MANIFEST
            ),
        )


@pytest.mark.parametrize(
    "change",
    [
        "constraints",
        "duplicate",
        "unsorted",
        "missing-global",
        "traversal",
        "digest",
    ],
)
def test_python_provider_rejects_source_substitution(change):
    """The source manifest binds unique paths and exact constraints."""
    provider = _provider()
    inputs = provider.source_input_manifest
    if change == "constraints":
        with pytest.raises(ValueError, match="binding mismatch"):
            replace(provider, build_constraints=b"different")
        return
    if change == "duplicate":
        inputs = tuple(sorted((*inputs, inputs[0])))
    elif change == "unsorted":
        inputs = tuple(reversed(inputs))
    elif change == "missing-global":
        inputs = tuple(p for p in inputs if p[0] != "global.json")
    elif change == "traversal":
        inputs = tuple(sorted((*inputs, ("../outside", "sha256:" + "e" * 64))))
    else:
        inputs = tuple(
            (p, "SHA256:wrong" if p == PYTHON_MANIFEST else d)
            for p, d in inputs
        )
    with pytest.raises(ValueError, match="Python Provider"):
        replace(provider, source_input_manifest=inputs)


@pytest.mark.parametrize(
    "change",
    [
        "runtime",
        "extra-dynamic",
        "hook",
        "backend",
        "sources",
        "static-version",
    ],
)
def test_python_manifest_rejects_unsupported_build_shape(change):
    """Only the approved zero-dependency NBGV/Hatch source shape is accepted."""
    manifest = validate_python_source_manifest(
        (_ROOT / PYTHON_MANIFEST).read_bytes()
    )
    if change == "runtime":
        manifest["project"]["dependencies"] = ["requests"]
    elif change == "extra-dynamic":
        manifest["project"]["dynamic"].append("description")
    elif change == "hook":
        manifest["tool"]["hatch"]["build"]["hooks"] = {"custom": {}}
    elif change == "backend":
        manifest["build-system"]["build-backend"] = "other.backend"
    elif change == "sources":
        manifest["tool"]["uv"]["sources"]["nbgv-python"] = {"path": "../other"}
    else:
        manifest["project"]["version"] = "0.1.0"
    with pytest.raises(ValueError, match="source metadata or build hook"):
        validate_python_source_manifest(tomli_w.dumps(manifest).encode())


def _tracked_sources(tmp_path, monkeypatch):
    paths = tuple(
        sorted(
            (*_GLOBALS, *(f"{PYTHON_ROOT}/{p}" for p in PYTHON_SOURCE_FILES))
        )
    )
    for path in paths:
        full = tmp_path / path
        full.parent.mkdir(parents=True, exist_ok=True)
        full.write_text("input", encoding="utf-8")
    monkeypatch.setattr(
        python_provider,
        "_run_command",
        lambda _command, _root: "\0".join((*paths, "unrelated.txt", "")),
    )
    return paths


def test_python_input_paths_close_regular_tracked_sources(
    tmp_path, monkeypatch
):
    """Exclude unrelated files and retain all declared smoke inputs."""
    paths = _tracked_sources(tmp_path, monkeypatch)
    assert python_input_paths(tmp_path) == paths
    assert "unrelated.txt" not in python_input_paths(tmp_path)


@pytest.mark.parametrize(
    "change", ["missing", "symlink", "directory", "untracked-required"]
)
def test_python_input_paths_reject_incomplete_or_unsafe_sources(
    tmp_path, monkeypatch, change
):
    """A lexical path is insufficient evidence of an original regular input."""
    paths = _tracked_sources(tmp_path, monkeypatch)
    target = tmp_path / PYTHON_MANIFEST
    target.unlink()
    if change == "symlink":
        target.symlink_to(tmp_path / "global.json")
    elif change == "directory":
        target.mkdir()
    elif change == "untracked-required":
        target.write_text("untracked", encoding="utf-8")
        monkeypatch.setattr(
            python_provider,
            "_run_command",
            lambda _command, _root: "\0".join(
                p for p in paths if p != PYTHON_MANIFEST
            ),
        )
    with pytest.raises(ValueError, match="Python source"):
        python_input_paths(tmp_path)


@pytest.mark.parametrize("change", ["hash", "version", "missing", "additional"])
def test_python_build_constraints_reject_tampered_frozen_closure(change):
    """Producer dependencies and hashes must match the reviewed UV lock."""
    constraints = (_ROOT / PYTHON_BUILD_CONSTRAINTS).read_bytes()
    lock = (_ROOT / "uv.lock").read_bytes()
    validate_python_build_constraints(constraints, lock)
    if change == "hash":
        constraints = constraints.replace(
            b"--hash=sha256:", b"--hash=sha256:0", 1
        )
    elif change == "version":
        constraints = constraints.replace(
            b"hatchling==1.32.0", b"hatchling==1.31.0"
        )
    elif change == "missing":
        constraints = b"\n".join(
            line
            for line in constraints.splitlines()
            if not line.startswith(b"packaging==")
        )
    else:
        constraints += b"undeclared==1.0.0 --hash=sha256:" + b"0" * 64 + b"\n"
    with pytest.raises(ValueError, match="frozen UV lock"):
        validate_python_build_constraints(constraints, lock)


def test_python_provider_serialization_retains_native_and_execution_binding():
    """Retain exact serialized facts without transport authority."""
    source = _provider()
    admitted = python_provider_result_from_document(source.to_document())
    assert admitted.binding.request_id == "python-test"
    assert admitted.binding.workflow_run_id == _WORKFLOW_RUN_ID
    assert admitted.binding.run_attempt == 1
    assert admitted.nbgv.pep440_version == "0.1.0b7"
    assert admitted.checkout.authoritative_remote == "origin"
    assert dict(admitted.source_input_manifest)[PYTHON_POLICY] == python_digest(
        PYTHON_POLICY.encode()
    )
    assert admitted.to_document()["outputs"] == ["wheel", "sdist"]
    assert admitted.result_digest == source.result_digest
    assert "transport-id" not in admitted.to_document()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/node-provider-result"),
        ("provider", "python/unknown"),
        ("execution-class", "publisher/credentialed-v1"),
        ("transport-id", 123),
        ("transport-digest", "sha256:" + "a" * 64),
        ("toolchain", [["python", "3.13.0"]]),
        ("outputs", ["sdist", "wheel"]),
        ("outputs", ["wheel", "sdist", "wheel"]),
        ("inputs", [["pyproject.toml"]]),
        ("inputs", {"pyproject.toml": "sha256:" + "a" * 64}),
    ],
)
def test_python_provider_serialization_rejects_open_or_foreign_variant(
    field, value
):
    """Unknown variants and transport fields cannot broaden authority."""
    document = _provider().to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_provider_result_from_document(document)


@pytest.mark.parametrize(
    ("section", "field", "value", "error"),
    [
        ("binding", "workflow-run-id", True, ValueError),
        ("binding", "workflow-run-id", "91", ValueError),
        ("binding", "run-attempt", None, ValueError),
        ("binding", "transport-id", 123, ValueError),
        ("checkout", "shallow", "false", TypeError),
        ("checkout", "credentials-persisted", 0, TypeError),
        ("checkout", "transport-digest", "arbitrary", ValueError),
    ],
)
def test_python_provider_serialization_rejects_malformed_nested_fields(
    section, field, value, error
):
    """Nested binding primitives and objects remain closed without coercion."""
    document = _provider().to_document()
    document[section][field] = value
    with pytest.raises(error):
        python_provider_result_from_document(document)


def test_python_provider_serialization_retains_live_binding_without_attempt():
    """Normal Live has no run-attempt field, unlike qualification facts."""
    source = _provider()
    live = replace(
        source,
        binding=replace(
            source.binding, purpose="live-release", run_attempt=None
        ),
    )
    document = live.to_document()
    assert "run-attempt" not in document["binding"]
    admitted = python_provider_result_from_document(document)
    assert admitted.binding.purpose == "live-release"
    assert admitted.binding.run_attempt is None
    assert admitted.binding.workflow_run_id == _WORKFLOW_RUN_ID
