"""Immutable one-gem artifacts retain original bytes and current purpose."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.ruby import (
    ruby_artifact_from_document,
    validate_ruby_artifact,
)


def test_ruby_artifact_roundtrip_inspects_original_payload(
    ruby_integration_original,
):
    """Import closed transport data and inspect the original native gem."""
    snapshot, artifact, distribution = ruby_integration_original
    imported = ruby_artifact_from_document(
        parse_canonical_json(canonicalize(artifact.to_document()))
    )
    validate_ruby_artifact(
        imported,
        witness=snapshot.build_request().witness,
        context=snapshot.context,
    )
    inspected = imported.inspect(distribution.content)
    assert imported.artifact_digest == artifact.artifact_digest
    assert inspected.content == distribution.content
    assert inspected.digest == artifact.reference.payload_digest
    assert inspected.witness == snapshot.build_request().witness
    assert imported.transport.producer == "build-ruby"
    assert imported.byte_size == len(distribution.content)


@pytest.mark.parametrize(
    "change",
    [
        "filename",
        "payload-path",
        "transport-id",
        "transport-digest",
        "transport-url",
        "producer",
        "size-zero",
        "size-bool",
    ],
)
def test_ruby_artifact_rejects_mismatched_native_or_transport_identity(
    ruby_integration_original, change
):
    """A valid archive cannot legitimize an alias or malformed transport."""
    _, artifact, _ = ruby_integration_original
    if change == "filename":
        updates = {"filename": "foreign.gem"}
    elif change == "payload-path":
        updates = {
            "reference": replace(artifact.reference, payload_path="foreign.gem")
        }
    elif change.startswith("transport-"):
        field, value = {
            "transport-id": ("artifact_id", 802),
            "transport-digest": ("transport_digest", "sha256:" + "9" * 64),
            "transport-url": (
                "artifact_url",
                "https://example.invalid/foreign",
            ),
        }[change]
        updates = {"transport": replace(artifact.transport, **{field: value})}
    elif change == "producer":
        updates = {
            "transport": replace(artifact.transport, producer="borrowed-build")
        }
    else:
        updates = {"byte_size": True if change == "size-bool" else 0}
    with pytest.raises(ValueError, match="Ruby"):
        replace(artifact, **updates)


@pytest.mark.parametrize("change", ["run", "attempt", "purpose", "witness"])
def test_ruby_artifact_rejects_cross_context_reuse(
    ruby_integration_original, change
):
    """Keep run, purpose and witness authority independent."""
    snapshot, artifact, _ = ruby_integration_original
    if change == "run":
        artifact = replace(
            artifact,
            transport=replace(artifact.transport, workflow_run_id=1000),
        )
    elif change == "attempt":
        artifact = replace(
            artifact, transport=replace(artifact.transport, run_attempt=2)
        )
    else:
        witness = replace(
            artifact.witness,
            **(
                {"purpose": "live-release"}
                if change == "purpose"
                else {"control_digest": "sha256:" + "9" * 64}
            ),
        )
        artifact = replace(artifact, witness=witness)
    with pytest.raises(ValueError, match="purpose or current-build"):
        validate_ruby_artifact(
            artifact,
            witness=snapshot.build_request().witness,
            context=snapshot.context,
        )


@pytest.mark.parametrize("change", ["bytes", "digest", "size", "witness"])
def test_ruby_artifact_inspection_rejects_payload_substitution(
    ruby_integration_original, change
):
    """Require original-byte and embedded witness binding."""
    _, artifact, distribution = ruby_integration_original
    payload = distribution.content
    if change == "bytes":
        payload = b"not a gem"
    elif change == "digest":
        artifact = replace(
            artifact,
            reference=replace(
                artifact.reference, payload_digest="sha256:" + "9" * 64
            ),
        )
    elif change == "size":
        artifact = replace(artifact, byte_size=artifact.byte_size + 1)
    else:
        artifact = replace(
            artifact,
            witness=replace(
                artifact.witness, control_digest="sha256:" + "9" * 64
            ),
        )
    with pytest.raises(ValueError, match=r"Ruby|ruby"):
        artifact.inspect(payload)


@pytest.mark.parametrize(
    "change",
    [
        "schema",
        "extra",
        "transport-extra",
        "attempt-null",
        "attempt-bool",
        "run-bool",
    ],
)
def test_ruby_artifact_import_rejects_open_or_coerced_shapes(
    ruby_integration_original, change
):
    """Reject foreign domains and coerced artifact primitives."""
    _, artifact, _ = ruby_integration_original
    doc = artifact.to_document()
    if change == "schema":
        doc["schema"] = "workflow-delivery/v3/python-artifact"
    elif change == "extra":
        doc["authority"] = "live-release"
    elif change == "transport-extra":
        doc["transport"]["extra"] = True
    else:
        key = "workflow-run-id" if change == "run-bool" else "run-attempt"
        doc["transport"][key] = None if change == "attempt-null" else True
    with pytest.raises((ValueError, TypeError)):
        ruby_artifact_from_document(doc)
