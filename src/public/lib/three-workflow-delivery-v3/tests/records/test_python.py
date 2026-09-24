"""Original Python artifact and current-build pair contracts."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.records.python import (
    python_artifact_from_document,
    validate_python_artifact_pair,
)

from ..python_fixtures import model, originals


@pytest.fixture
def built():
    """Supply actual archives with synthetic immutable current-run transport."""
    source = model()
    return source, originals(source)


def test_python_artifact_admits_exact_bytes_and_closed_serialization(built):
    """Both native originals retain independent transports and byte identity."""
    source, (artifacts, payloads, _) = built
    validate_python_artifact_pair(
        artifacts,
        witness=source.build_request().witness,
        context=source.context,
    )
    for artifact, content in zip(artifacts, payloads, strict=True):
        parsed = python_artifact_from_document(artifact.to_document())
        inspected = parsed.inspect(content)
        assert inspected.variant == artifact.variant
        assert inspected.digest == artifact.reference.payload_digest
        assert len(inspected.content) == artifact.byte_size
    assert (
        artifacts[0].reference.artifact_id != artifacts[1].reference.artifact_id
    )


@pytest.mark.parametrize(
    "change", ["size", "filename", "path", "id", "digest", "url", "producer"]
)
def test_python_artifact_rejects_native_or_transport_substitution(
    built, change
):
    """Native and transport components cannot be independently changed."""
    _, (artifacts, _, _) = built
    artifact = artifacts[0]
    changes = {
        "size": {"byte_size": True},
        "filename": {"filename": "other.whl"},
        "path": {
            "reference": replace(artifact.reference, payload_path="other.whl")
        },
        "id": {"transport": replace(artifact.transport, artifact_id=901)},
        "digest": {
            "transport": replace(
                artifact.transport, transport_digest="sha256:" + "0" * 64
            )
        },
        "url": {
            "transport": replace(
                artifact.transport, artifact_url="https://example.invalid/other"
            )
        },
        "producer": {
            "transport": replace(artifact.transport, producer="consumer-python")
        },
    }
    with pytest.raises(ValueError, match="Python artifact"):
        replace(artifact, **changes[change])


@pytest.mark.parametrize("change", ["size", "digest"])
def test_python_artifact_checks_bytes_against_immutable_reference(
    built, change
):
    """Valid archives must still match their frozen size and hash."""
    _, (artifacts, payloads, _) = built
    artifact = artifacts[0]
    changed = (
        replace(artifact, byte_size=artifact.byte_size + 1)
        if change == "size"
        else replace(
            artifact,
            reference=replace(
                artifact.reference, payload_digest="sha256:" + "0" * 64
            ),
        )
    )
    with pytest.raises(ValueError, match="immutable reference"):
        changed.inspect(payloads[0])


@pytest.mark.parametrize(
    "change", ["reverse", "missing", "duplicate", "run", "attempt", "purpose"]
)
def test_python_artifact_pair_rejects_wrong_order_or_build_lineage(
    built, change
):
    """The pair belongs to one exact current-purpose build, in native order."""
    source, (artifacts, _, _) = built
    if change == "reverse":
        artifacts = artifacts[::-1]
    elif change == "missing":
        artifacts = artifacts[:1]
    elif change == "duplicate":
        artifacts = (artifacts[0], artifacts[0])
    elif change in {"run", "attempt"}:
        field = "workflow_run_id" if change == "run" else "run_attempt"
        artifacts = (
            replace(
                artifacts[0],
                transport=replace(artifacts[0].transport, **{field: 99}),
            ),
            artifacts[1],
        )
    else:
        artifacts = (
            replace(
                artifacts[0],
                witness=replace(artifacts[0].witness, purpose="live-release"),
            ),
            artifacts[1],
        )
    with pytest.raises(ValueError, match="Python artifact"):
        validate_python_artifact_pair(
            artifacts,
            witness=source.build_request().witness,
            context=source.context,
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [("schema", "foreign"), ("extra", True), ("byte-size", True)],
)
def test_python_artifact_parser_rejects_open_or_coerced_records(
    built, field, value
):
    """Schema closure retains exact JSON primitives."""
    _, (artifacts, _, _) = built
    doc = artifacts[0].to_document()
    doc[field] = value
    with pytest.raises(ValueError, match="Python"):
        python_artifact_from_document(doc)


@pytest.mark.parametrize("value", [None, True, "1", 1.0, 0])
def test_python_artifact_transport_rejects_explicit_invalid_run_attempt(value):
    """An absent Live attempt cannot be replaced by null or numeric aliases."""
    artifacts, _, _ = originals(model("live-release"))
    document = artifacts[0].to_document()
    assert "run-attempt" not in document["transport"]
    document["transport"]["run-attempt"] = value
    with pytest.raises((ValueError, TypeError), match=r"run.attempt"):
        python_artifact_from_document(document)
