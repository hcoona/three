"""Python Model source compilation and immutable Provider admission."""

# Compile-only fixtures use real local Git, without executing target code.
# ruff: noqa: S607
import subprocess
from dataclasses import replace
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.records.artifacts import (
    ArtifactReference,
    ArtifactTransportIdentity,
)
from three_workflow_delivery_v3.repository.compiler import (
    CompilationContext,
    provider_binding,
)
from three_workflow_delivery_v3.repository.node_provider import (
    AUTHORITATIVE_REMOTE,
    TAG_REFSPEC,
    CheckoutEvidence,
)
from three_workflow_delivery_v3.repository.python_model import (
    PYTHON_DESCRIPTOR,
    PYTHON_QUALITY_PATH,
    admit_python_provider_facts,
    compile_python_repository_model,
    python_provider_manifest,
    python_repository_model_from_document,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_MANIFEST,
    PYTHON_POLICY,
    PYTHON_RELEASE_UNIT,
    PYTHON_ROOT,
    PYTHON_SOURCE_FILES,
    PythonNbgvFacts,
    PythonProviderResult,
    python_digest,
)

_ROOT = Path(__file__).resolve().parents[6]
_RUN_ID = 601
_INPUTS = (
    ".config/dotnet-tools.json",
    "global.json",
    "mise.lock",
    "mise.toml",
    "pyproject.toml",
    "uv.lock",
    PYTHON_BUILD_CONSTRAINTS,
    PYTHON_POLICY,
    PYTHON_DESCRIPTOR,
    PYTHON_QUALITY_PATH,
    *(f"{PYTHON_ROOT}/{name}" for name in PYTHON_SOURCE_FILES),
)


def _context(target="a" * 40, purpose="slice-validation"):
    return CompilationContext(
        "python-model-test",
        purpose,
        _RUN_ID,
        None if purpose == "live-release" else 1,
        target,
        "compile-python-model",
        "reviewed-control:" + target,
        catalog_digest(),
        "buddy" if purpose == "release-simulation" else None,
        PYTHON_RELEASE_UNIT if purpose == "release-simulation" else None,
    )


def _admitted(context, contents):
    manifest = python_provider_manifest(context)
    facts = PythonNbgvFacts(
        canonicalize(
            {
                "SimpleVersion": "0.1.0",
                "SemVer2": "0.1.0-beta.7",
                "GitCommitId": context.target,
                "VersionHeight": 7,
                "PublicRelease": True,
            }
        ),
        "0.1.0b7",
    )
    result = PythonProviderResult(
        provider_binding(manifest, "python-smoke"),
        CheckoutEvidence(
            context.target,
            context.target,
            shallow=False,
            ancestry_complete=True,
            tags_complete=True,
            credentials_persisted=False,
            authoritative_remote=AUTHORITATIVE_REMOTE,
            authoritative_remote_url="file:///python-model-fixture.git",
            tag_refspec=TAG_REFSPEC,
        ),
        facts,
        tuple(
            sorted(
                (path, python_digest(value)) for path, value in contents.items()
            )
        ),
        contents[PYTHON_BUILD_CONSTRAINTS],
    )
    request = ArtifactReference(
        101,
        "sha256:" + "1" * 64,
        "https://example.invalid/artifacts/101",
        "request.json",
        manifest.manifest_digest,
    )
    reference = ArtifactReference(
        102,
        "sha256:" + "2" * 64,
        "https://example.invalid/artifacts/102",
        "provider.json",
        result.result_digest,
    )
    transport = ArtifactTransportIdentity(
        reference.artifact_id,
        manifest.requests[0].expected_result_identity,
        reference.artifact_url,
        reference.artifact_digest,
        "discover-python",
        context.workflow_run_id,
        context.run_attempt,
    )
    return admit_python_provider_facts(
        canonicalize(result.to_document()),
        manifest=manifest,
        request_reference=request,
        result_reference=reference,
        result_transport=transport,
    )


def _contents():
    return {path: (_ROOT / path).read_bytes() for path in _INPUTS}


@pytest.fixture
def admitted():
    """Admit concrete serialized facts without claiming native evaluation."""
    return _admitted(_context(), _contents())


@pytest.fixture
def committed_inputs(tmp_path):
    """Commit only fixture files and return the true inventory and contents."""

    def create(contents):
        root = tmp_path / "source"
        root.mkdir()
        for path, value in contents.items():
            destination = root / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(value)
        subprocess.run(("git", "init", "--quiet"), cwd=root, check=True)
        subprocess.run(("git", "add", "."), cwd=root, check=True)
        subprocess.run(
            (
                "git",
                "-c",
                "user.name=Python Model Test",
                "-c",
                "user.email=python-model@example.invalid",
                "-c",
                "core.hooksPath=/dev/null",
                "-c",
                "commit.gpgSign=false",
                "commit",
                "--quiet",
                "-m",
                "Create exact Model fixture inputs",
            ),
            cwd=root,
            check=True,
        )
        target = subprocess.check_output(
            ("git", "rev-parse", "HEAD"), cwd=root, text=True
        ).strip()
        return root, target

    return create


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("target", "b" * 40),
        ("control", "different-reviewed-control"),
        ("request_id", "other-request"),
        ("workflow_run_id", _RUN_ID + 1),
        ("run_attempt", 2),
        ("purpose", "ci-pr-slice-shadow"),
    ],
)
def test_python_manifest_binds_current_context(field, value):
    """Current control, purpose, target and run change the frozen request."""
    context = _context()
    original = python_provider_manifest(context)
    changed = python_provider_manifest(replace(context, **{field: value}))
    assert (
        original.requests[0].request_digest
        != changed.requests[0].request_digest
    )
    assert original.manifest_digest != changed.manifest_digest
    assert changed.requests[0].producer == "discover-python"


def test_python_manifest_rejects_foreign_simulation_unit():
    """A valid other-language selection cannot borrow the Python Provider."""
    context = replace(
        _context(purpose="release-simulation"),
        release_unit="hcoona-release-smoke-npm",
    )
    with pytest.raises(ValueError, match="own selected unit"):
        python_provider_manifest(context)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("artifact_id", 103),
        ("transport_digest", "sha256:" + "3" * 64),
        ("artifact_url", "https://example.invalid/artifacts/other"),
        ("producer", "untrusted-target"),
        ("workflow_run_id", _RUN_ID + 1),
        ("run_attempt", 2),
    ],
)
def test_python_provider_admission_rejects_transport_substitution(
    admitted, field, value
):
    """Equal payload bytes cannot borrow another immutable result transport."""
    with pytest.raises(ValueError, match="transport or current-request"):
        replace(
            admitted,
            result_transport=replace(
                admitted.result_transport, **{field: value}
            ),
        )


@pytest.mark.parametrize(
    "change",
    [
        "payload",
        "request-digest",
        "result-digest",
        "result-binding",
        "manifest-producer",
    ],
)
def test_python_provider_admission_rejects_payload_or_request_substitution(
    admitted, change
):
    """Admission checks the actual canonical bytes and the frozen request."""
    if change == "payload":
        payload = canonicalize({"untrusted": "replacement"})
        with pytest.raises(ValueError, match="payload digest"):
            admit_python_provider_facts(
                payload,
                manifest=admitted.manifest,
                request_reference=admitted.request_reference,
                result_reference=admitted.result_reference,
                result_transport=admitted.result_transport,
            )
    elif change == "request-digest":
        with pytest.raises(ValueError, match="current-request"):
            replace(
                admitted,
                request_reference=replace(
                    admitted.request_reference,
                    payload_digest="sha256:" + "9" * 64,
                ),
            )
    elif change == "result-digest":
        with pytest.raises(ValueError, match="current-request"):
            replace(
                admitted,
                result_reference=replace(
                    admitted.result_reference,
                    payload_digest="sha256:" + "9" * 64,
                ),
            )
    elif change == "result-binding":
        changed = replace(
            admitted.result,
            binding=replace(
                admitted.result.binding, request_id="borrowed-request"
            ),
        )
        with pytest.raises(ValueError, match="current-request"):
            replace(
                admitted,
                result=changed,
                result_reference=replace(
                    admitted.result_reference,
                    payload_digest=changed.result_digest,
                ),
            )
    else:
        manifest = replace(
            admitted.manifest,
            requests=(
                replace(
                    admitted.manifest.requests[0], producer="other-producer"
                ),
            ),
        )
        with pytest.raises(ValueError, match="reviewed control"):
            replace(admitted, manifest=manifest)


def test_python_compiler_requires_admitted_facts(admitted, tmp_path):
    """A raw valid Provider Result is insufficient compiler authority."""
    with pytest.raises(TypeError, match="admitted Provider"):
        compile_python_repository_model(tmp_path, admitted.result)


def test_python_compiler_uses_committed_source(committed_inputs):
    """Dirty policy/source files cannot replace the exact target's inputs."""
    contents = _contents()
    root, target = committed_inputs(contents)
    facts = _admitted(_context(target), contents)
    (root / PYTHON_POLICY).write_text("untrusted: dirty\n")
    (root / PYTHON_MANIFEST).write_text("untrusted dirty manifest\n")
    model = compile_python_repository_model(root, facts)
    assert model.context.target == target
    assert model.to_document()["ready"] is True
    assert (
        model.provider.source_input_manifest
        == facts.result.source_input_manifest
    )
    assert (
        model.to_document()["release-unit"]["builds"][0]["outputs"][0]["id"]
        == "wheel"
    )


@pytest.mark.parametrize(
    "change", ["missing-input", "extra-input", "changed-digest"]
)
def test_python_compiler_rejects_missing_or_changed_inputs(
    committed_inputs, change
):
    """Truthful native facts still need the full exact committed closure."""
    contents = _contents()
    optional = f"{PYTHON_ROOT}/additional-source.txt"
    contents[optional] = b"tracked target input"
    root, target = committed_inputs(contents)
    supplied = dict(contents)
    if change == "missing-input":
        supplied.pop(optional)
    elif change == "extra-input":
        supplied[f"{PYTHON_ROOT}/not-in-target.txt"] = b"injected"
    else:
        supplied[PYTHON_MANIFEST] = b"wrong frozen bytes"
    facts = _admitted(_context(target), supplied)
    with pytest.raises(ValueError, match=r"target inputs|exact target"):
        compile_python_repository_model(root, facts)


@pytest.mark.parametrize(
    "path", [PYTHON_DESCRIPTOR, PYTHON_POLICY, PYTHON_QUALITY_PATH]
)
def test_python_compiler_rejects_changed_authoring(committed_inputs, path):
    """Correctly hashed authoring cannot expand the declared Python contract."""
    contents = _contents()
    authoring = yaml.safe_load(contents[path])
    if path == PYTHON_DESCRIPTOR:
        authoring["builds"][0]["outputs"].reverse()
    elif path == PYTHON_POLICY:
        authoring["channels"]["official"]["governance"] = authoring["channels"][
            "buddy"
        ]["governance"]
    else:
        authoring["ecosystems"]["python"]["preset"] = "python/unreviewed-v1"
    contents[path] = yaml.safe_dump(authoring).encode()
    root, target = committed_inputs(contents)
    facts = _admitted(_context(target), contents)
    with pytest.raises(ValueError, match="two-format contract"):
        compile_python_repository_model(root, facts)


def _model(committed_inputs, purpose="slice-validation"):
    contents = _contents()
    root, target = committed_inputs(contents)
    return compile_python_repository_model(
        root, _admitted(_context(target, purpose), contents)
    )


def test_python_model_retains_independent_destination_policy(committed_inputs):
    """Model readers retain separate Buddy and Official registry admissions."""
    model = _model(committed_inputs)
    parsed = python_repository_model_from_document(model.to_document())
    channels = parsed.to_document()["release-policy"]["channels"]
    assert channels["buddy"] == {
        "destination": "python/testpypi-v1",
        "governance": (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-python-testpypi.json"
        ),
    }
    assert channels["official"] == {
        "destination": "python/pypi-v1",
        "governance": (
            ".github/workflow-delivery/governance/"
            "hcoona-release-smoke-python-pypi.json"
        ),
    }
    assert parsed.to_document()["reverse-index"] == {
        PYTHON_RELEASE_UNIT: ["python-distributions"]
    }


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("schema", "workflow-delivery/v3/repository-model-snapshot"),
        ("unexpected", True),
        ("ready", False),
        ("ready", 1),
        ("quality", []),
        ("reverse-index", {}),
        ("release-policy", {}),
    ],
)
def test_python_model_rejects_foreign_or_open_serialized_contract(
    committed_inputs, field, value
):
    """Closed Model fields admit no foreign schemas or primitive aliases."""
    document = _model(committed_inputs).to_document()
    document[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_repository_model_from_document(document)


@pytest.mark.parametrize(
    "purpose",
    [
        "ci-pr-slice-shadow",
        "slice-validation",
        "release-simulation",
        "live-release",
    ],
)
def test_python_model_build_request_keeps_context_purpose(
    committed_inputs, purpose
):
    """Build identity preserves the owning context without promotion."""
    model = _model(committed_inputs, purpose)
    request = model.build_request()
    assert request.witness.purpose == purpose
    assert request.witness.target == model.context.target
    assert request.witness.nbgv.pep440_version == "0.1.0b7"
    assert request.witness.catalog_digest == catalog_digest()
    assert request.witness.control_digest == canonical_sha256(
        {
            "schema": "workflow-delivery/v3/control-identity",
            "identity": model.context.control,
        }
    )
    assert request.source_input_manifest == model.provider.source_input_manifest
    assert "workflow-run-id" not in request.witness.to_document()
