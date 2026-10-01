"""Strict native observation replay and predecessor-bound Ruby imports."""

from copy import deepcopy

import pytest
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    ruby_response_document,
)
from three_workflow_delivery_v3.release.ruby_audit import (
    ruby_approval_bundle_from_document,
    ruby_authorization_from_document,
    ruby_marker_from_document,
    ruby_publication_result_from_document,
    ruby_publication_snapshot_from_document,
    ruby_remote_observation_from_document,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
    ruby_response_from_document,
)

from ..ruby_integration_fixtures import reference
from . import ruby_fixtures
from .ruby_fixtures import execute

ruby_publication_cases = ruby_fixtures.ruby_publication_cases


@pytest.mark.parametrize("name", ["rubygems", "github-packages"])
@pytest.mark.parametrize("state", ["missing", "exact"])
def test_ruby_native_observation_replay_uses_original_responses_offline(
    ruby_publication_cases, name, state
):
    """Native protocol evidence independently reproduces the observed state."""
    case = ruby_publication_cases[name]
    native = getattr(case, state)
    replayed = replay_ruby_observation(
        native.to_document(), case.original, case.registry
    )
    assert replayed.to_document() == native.to_document()
    assert replayed.classification == state
    if state == "exact":
        assert replayed.distribution == case.original


@pytest.mark.parametrize(
    "change",
    [
        "classification",
        "artifact-digest",
        "registry",
        "version",
        "response-order",
        "url",
        "body-digest",
        "body",
        "extra-response",
        "missing-response",
        "extra-field",
    ],
)
def test_ruby_native_replay_rejects_tampered_response_or_scalar_claim(
    ruby_publication_cases, change
):
    """Stored success is recomputed from ordered bounded native evidence."""
    case = ruby_publication_cases["rubygems"]
    document = case.exact.to_document()
    mutations = {
        "classification": lambda: document.update(classification="missing"),
        "artifact-digest": lambda: document.update(
            {"artifact-digest": "sha256:" + "f" * 64}
        ),
        "registry": lambda: document.update(registry="github-packages"),
        "version": lambda: document.update(version="9.0.0"),
        "response-order": document["responses"].reverse,
        "url": lambda: document["responses"][0].update(
            url="https://foreign.invalid/index"
        ),
        "body-digest": lambda: document["responses"][-1]["response"].update(
            {"body-digest": "sha256:" + "f" * 64}
        ),
        "body": lambda: document["responses"][-1].update(
            response=ruby_response_document(
                RubyHttpResponse(200, b"foreign bytes")
            )
        ),
        "extra-response": lambda: document["responses"].append(
            deepcopy(document["responses"][-1])
        ),
        "missing-response": document["responses"].pop,
        "extra-field": lambda: document.update(extra=True),
    }
    mutations[change]()
    with pytest.raises(ValueError):  # noqa: PT011 - all strict replay boundaries
        replay_ruby_observation(document, case.original, case.registry)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("status", True),
        ("status", 600),
        ("body-base64", "%%%"),
        ("body-digest", "sha256:" + "f" * 64),
        ("content-type", 1),
        ("extra", True),
    ],
)
def test_ruby_response_import_requires_exact_bounded_envelope(field, value):
    """Coercion, alternate bytes and open envelope fields are rejected."""
    document = ruby_response_document(
        RubyHttpResponse(200, b"native", "application/octet-stream")
    )
    document[field] = value
    with pytest.raises((ValueError, TypeError)):
        ruby_response_from_document(document)


def test_ruby_response_import_accepts_limit_and_rejects_next_byte():
    """Encoded and decoded byte bounds agree at the exact limit."""
    response = RubyHttpResponse(200, b"1234")
    assert (
        ruby_response_from_document(ruby_response_document(response), 4)
        == response
    )
    with pytest.raises(ValueError, match=r"budget|digest or status"):
        ruby_response_from_document(
            ruby_response_document(RubyHttpResponse(200, b"12345")), 4
        )


def _imports(case):
    observation = case.snapshot.observation
    return {
        "observation": (
            observation,
            lambda doc: ruby_remote_observation_from_document(
                doc, case.decision, case.original.content
            ),
        ),
        "snapshot": (
            case.snapshot,
            lambda doc: ruby_publication_snapshot_from_document(
                doc, observation
            ),
        ),
        "bundle": (
            case.bundle,
            lambda doc: ruby_approval_bundle_from_document(doc, case.snapshot),
        ),
        "authorization": (
            case.authorization,
            lambda doc: ruby_authorization_from_document(doc, case.bundle),
        ),
        "marker": (
            case.marker,
            lambda doc: ruby_marker_from_document(
                doc,
                case.authorization,
                case.marker.fresh_governance,
                case.original.content,
            ),
        ),
    }


@pytest.mark.parametrize(
    "kind", ["observation", "snapshot", "bundle", "authorization", "marker"]
)
def test_ruby_publication_imports_reconstruct_exact_predecessors(
    ruby_publication_cases, kind
):
    """Every public record is reconstructed through the current parent chain."""
    original, importer = _imports(ruby_publication_cases["rubygems"])[kind]
    parsed = importer(original.to_document())
    assert parsed == original
    assert parsed.to_document() == original.to_document()


@pytest.mark.parametrize(
    "kind", ["observation", "snapshot", "bundle", "authorization", "marker"]
)
@pytest.mark.parametrize("change", ["producer", "run", "open"])
def test_ruby_publication_imports_reject_foreign_or_open_records(
    ruby_publication_cases, kind, change
):
    """Valid embedded facts cannot excuse foreign provenance or extra fields."""
    original, importer = _imports(ruby_publication_cases["rubygems"])[kind]
    document = original.to_document()
    changes = {
        "producer": lambda: document.update(producer="foreign"),
        "run": lambda: document["attempt"].update({"workflow-run-id": 1000}),
        "open": lambda: document.update(extra=True),
    }
    changes[change]()
    with pytest.raises(ValueError):  # noqa: PT011 - strict canonical equality
        importer(document)


def test_ruby_result_import_replays_original_upload_and_visibility(
    ruby_publication_cases, tmp_path
):
    """Canonical Result import independently verifies the complete success."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    parsed = ruby_publication_result_from_document(
        result.to_document(), case.marker, case.original.content
    )
    assert parsed.result == "published"
    assert parsed.result_digest == result.result_digest
    assert parsed.visibility == result.visibility


@pytest.mark.parametrize(
    "change",
    [
        "result",
        "mutation",
        "producer",
        "run",
        "marker-reference",
        "status",
        "upload-body",
        "exact-coercion",
        "false-exact",
        "ordinal",
        "before-upload",
        "past-window",
        "reversed-completion",
        "authority-deadline",
        "extra",
    ],
)
def test_ruby_result_import_rejects_tampered_claims_responses_and_times(
    ruby_publication_cases, tmp_path, change
):
    """Scalar success requires exact original bytes and timely evidence."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
    )
    doc = result.to_document()
    visibility = doc["visibility"][0]
    changes = {
        "result": lambda: doc.update(result="failed"),
        "mutation": lambda: doc.update(
            {"mutation-classification": "not-mutated"}
        ),
        "producer": lambda: doc.update(producer="foreign"),
        "run": lambda: doc["attempt"].update({"workflow-run-id": 1000}),
        "marker-reference": lambda: doc.update(
            {"mutation-marker-reference": reference({}).to_document()}
        ),
        "status": lambda: doc.update(status="failed"),
        "upload-body": lambda: doc["upload-response"].update(
            {"body-base64": "eA=="}
        ),
        "exact-coercion": lambda: visibility.update(exact=1),
        "false-exact": lambda: visibility["native"]["responses"][-1].update(
            response=ruby_response_document(RubyHttpResponse(200, b"foreign"))
        ),
        "ordinal": lambda: visibility.update(ordinal=True),
        "before-upload": lambda: visibility.update({"started-monotonic": 99}),
        "past-window": lambda: visibility.update(
            {"started-monotonic": 160, "completed-monotonic": 160}
        ),
        "reversed-completion": lambda: visibility.update(
            {"completed-monotonic": 99}
        ),
        "authority-deadline": lambda: doc["authority"].update(
            {"deadline-monotonic": 999}
        ),
        "extra": lambda: doc.update(extra=True),
    }
    changes[change]()
    with pytest.raises((ValueError, TypeError)):
        ruby_publication_result_from_document(
            doc, case.marker, case.original.content
        )


def test_ruby_audit_rejects_forged_exactness_over_late_original_response(
    ruby_publication_cases, tmp_path
):
    """Scalar rebinding cannot override the independent deadline audit."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [RubyHttpResponse(200, b"accepted"), *case.exact_responses()],
        delays=[0, 0, 0, 60],
    )
    document = result.to_document()
    document["visibility"][-1]["exact"] = True
    document["result"] = "published"
    document["error-kind"] = None
    with pytest.raises(ValueError, match="exactness"):
        ruby_publication_result_from_document(
            document, case.marker, case.original.content
        )


@pytest.mark.parametrize("change", ["too-close", "after-terminal", "too-many"])
def test_ruby_audit_rejects_visibility_spacing_and_terminal_extension(
    ruby_publication_cases, tmp_path, change
):
    """Reject early reads and any evidence collected after exactness."""
    case = ruby_publication_cases["rubygems"]
    result, _transport, _timeline = execute(
        case,
        tmp_path,
        [
            RubyHttpResponse(200, b"accepted"),
            *case.missing_responses(),
            *case.exact_responses(),
        ],
    )
    document = result.to_document()
    rows = document["visibility"]
    if change == "too-close":
        rows[1]["started-monotonic"] = rows[0]["started-monotonic"] + 9
    elif change == "after-terminal":
        rows.append(deepcopy(rows[-1]))
        rows[-1]["ordinal"] = 2
    else:
        rows.extend(deepcopy(rows[-1]) for _ in range(5))
    with pytest.raises(
        ValueError, match=r"spacing|upload response|operation profile"
    ):
        ruby_publication_result_from_document(
            document, case.marker, case.original.content
        )
