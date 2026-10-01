"""First-project absence, approval and postapproval marker contracts."""

from dataclasses import replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_publication import (
    RubyBootstrapMarker,
    observe_ruby_bootstrap_absence,
    render_ruby_bootstrap_summary,
)
from three_workflow_delivery_v3.adapters.ruby_project import (
    RubyGemsProjectObservation,
)
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyRegistryReader,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

from ..ruby_registry_fixtures import ScriptedTransport, json_response
from . import ruby_bootstrap_fixtures
from .ruby_bootstrap_fixtures import CURRENT, EXPIRED, transport

bootstrap_cases = ruby_bootstrap_fixtures.bootstrap_cases


@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_publication_summary_and_authority_keep_exact_current_original(
    bootstrap_cases, destination
):
    """Keep distinct bootstrap effect records for both destinations."""
    case = bootstrap_cases[destination]
    publication = case.publication
    marker = case.marker
    authorization = marker.authorization
    bundle = authorization.bundle
    summary = render_ruby_bootstrap_summary(bundle.absence).decode()
    assert publication.original.digest == case.original.digest
    assert (
        publication.to_document()["artifact"]
        == publication.artifact.to_document()
    )
    for value in (
        publication.run.target,
        publication.run.request.digest,
        publication.run.request.document["slot"],
        case.registry.origin,
        case.registry.environment,
        case.original.filename,
        case.original.digest,
        canonical_sha256(bundle.absence.to_document()),
    ):
        assert value in summary
    assert "Create the selected gem once" in summary
    assert "Only HTTP 200" in summary
    assert "No retry, rerun" in summary
    assert authorization.to_document()["producer"] == case.publisher
    assert marker.to_document()["producer"] == case.publisher
    assert marker.absence.to_document()["producer"] == case.publisher
    assert (
        bundle.to_document()["producer"] == "prepare-ruby-bootstrap-publication"
    )
    assert (
        marker.to_document()["authorization-reference"]
        == marker.authorization_reference.to_document()
    )
    assert marker.to_document()["pre-state"]["phase"] == "pre-marker"


@pytest.mark.parametrize(
    "mode", ["incomplete", "payload", "old-run", "archive", "producer"]
)
def test_publication_rejects_unqualified_or_substituted_original(
    bootstrap_cases, mode
):
    """Reject old evidence or hashes that replace current original bytes."""
    pub = bootstrap_cases["rubygems"].publication
    changes = {}
    if mode == "incomplete":
        changes["qualification"] = replace(
            pub.qualification, evidence=pub.qualification.evidence[:1]
        )
    elif mode == "payload":
        changes["payload"] = pub.payload + b"tamper"
    elif mode == "old-run":
        changes["qualification_transport"] = replace(
            pub.qualification_transport, workflow_run_id=992
        )
    elif mode == "producer":
        changes["qualification_transport"] = replace(
            pub.qualification_transport, producer="finalize-ruby-ci"
        )
    else:
        changes["qualification_reference"] = replace(
            pub.qualification_reference, artifact_digest="sha256:" + "e" * 64
        )
    with pytest.raises(ValueError, match=r"Ruby|Qualification"):
        replace(pub, **changes)


@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
def test_absence_spends_exact_native_partition_after_authority(
    bootstrap_cases, destination
):
    """Read owners before coordinates and preserve the operator debit."""
    case = bootstrap_cases[destination]
    native = ScriptedTransport(*case.absent_responses())
    events = []
    reader = case.reader(
        "eligibility",
        native,
        reserve=lambda phase, ordinal: events.append((phase, ordinal)),
    )

    def authority():
        events.append("authority")
        return {"actual-test-check": True}

    result = observe_ruby_bootstrap_absence(
        case.publication,
        reader,
        phase="eligibility",
        clock=lambda: CURRENT,
        check_authority=authority,
    )
    first_ordinal = 2 if destination == "rubygems" else 1
    assert events == [
        "authority",
        ("eligibility", first_ordinal),
        ("eligibility", first_ordinal + 1),
    ]
    assert result.native.classification == "missing"
    assert result.started_at == result.observed_at == CURRENT
    if destination == "rubygems":
        assert result.project.classification == "absent"
        assert native.requests[0][1].endswith("/owners.json")
        assert "Authorization" not in native.requests[0][2]
    else:
        assert result.project is None
    assert len(native.requests) == 2  # noqa: PLR2004 - whole-project/native partition


@pytest.mark.parametrize(
    "response",
    [
        json_response([]),
        RubyHttpResponse(404, b"wrong", "text/plain"),
        RubyHttpResponse(500, b"error"),
        OSError("offline"),
    ],
)
def test_existing_or_unknown_project_blocks_before_coordinate_read(
    bootstrap_cases, response
):
    """An empty owners array is still present; missing/failed evidence stops."""
    case = bootstrap_cases["rubygems"]
    native = ScriptedTransport(response)
    reader = case.reader("eligibility", native)
    with pytest.raises(ValueError, match="project is not absent"):
        observe_ruby_bootstrap_absence(
            case.publication,
            reader,
            phase="eligibility",
            clock=lambda: CURRENT,
            check_authority=dict,
        )
    assert reader.budget.used == 2  # noqa: PLR2004 - operator plus one runtime lookup
    assert len(native.requests) == 1


@pytest.mark.parametrize(
    "mode",
    [
        "wrong-phase",
        "free-budget",
        "missing-authorization",
        "early-authorization",
        "eligibility-with-authorization",
        "authority-failure",
        "expired-after-authority",
    ],
)
def test_absence_cannot_bypass_phase_or_fresh_authority(bootstrap_cases, mode):
    """Invalid predecessors stop before any owners or native registry send."""
    case = bootstrap_cases["rubygems"]
    native = ScriptedTransport()
    phase = (
        "eligibility"
        if mode == "eligibility-with-authorization"
        else "pre-marker"
    )
    reader = case.reader("execute" if mode == "wrong-phase" else phase, native)
    if mode == "free-budget":
        reader = RubyRegistryReader(case.registry, RubyRequestBudget(native))
    authorization = (
        None if mode == "missing-authorization" else case.marker.authorization
    )
    now = (
        CURRENT - timedelta(microseconds=1)
        if mode == "early-authorization"
        else CURRENT
    )
    times = iter(
        [now, EXPIRED] if mode == "expired-after-authority" else [now, now]
    )

    def authority():
        if mode == "authority-failure":
            message = "fresh authority failure"
            raise ValueError(message)
        return {}

    with pytest.raises(ValueError, match=r"Ruby|authority"):
        observe_ruby_bootstrap_absence(
            case.publication,
            reader,
            phase=phase,
            clock=lambda: next(times),
            check_authority=authority,
            authorization=authorization,
        )
    assert native.requests == []


@pytest.mark.parametrize(
    "mode",
    [
        "native-exact",
        "missing-owners",
        "present-owners",
        "clock-order",
        "expired",
    ],
)
def test_absence_record_replays_native_state_and_whole_project_proof(
    bootstrap_cases, mode
):
    """Reject missing claims that contradict native or whole-project state."""
    case = bootstrap_cases["rubygems"]
    old = case.marker.authorization.bundle.absence
    changes = {}
    if mode == "native-exact":
        changes["native"] = case.reader(
            "remote-consumer", ScriptedTransport(*case.exact_responses())
        ).observe(case.original)
    elif mode == "missing-owners":
        changes["project"] = None
    elif mode == "present-owners":
        changes["project"] = RubyGemsProjectObservation(json_response([]))
    elif mode == "clock-order":
        changes.update(
            started_at=CURRENT + timedelta(seconds=1), observed_at=CURRENT
        )
    else:
        changes["observed_at"] = EXPIRED
    with pytest.raises(ValueError, match="Ruby"):
        replace(old, **changes)


@pytest.mark.parametrize(
    "edge", ["absence", "summary", "bundle", "authorization"]
)
def test_approval_artifact_edges_require_current_raw_producer_transport(
    bootstrap_cases, edge
):
    """Each predecessor must come from its actual current-run producer."""
    marker = bootstrap_cases["rubygems"].marker
    auth = marker.authorization
    bundle = auth.bundle
    obj, field = {
        "absence": (bundle, "absence_transport"),
        "summary": (bundle, "summary_transport"),
        "bundle": (auth, "bundle_transport"),
        "authorization": (marker, "authorization_transport"),
    }[edge]
    with pytest.raises(ValueError, match="immutable transport"):
        replace(
            obj, **{field: replace(getattr(obj, field), workflow_run_id=992)}
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("workflow", ".github/workflows/workflow-delivery-v3-ruby-smoke.yml"),
        ("run-id", 992),
        ("run-attempt", True),
        ("target", "e" * 40),
        ("environment", "other"),
        ("environment-id", 102),
        ("reviewer-id", 1),
        ("reviewer", "other"),
        ("state", "rejected"),
        ("sentinel", "wrong/v1"),
        ("deployment-id", True),
        ("native-response-digest", "bad"),
        ("unreviewed", True),
    ],
)
def test_authorization_requires_closed_native_approval(
    bootstrap_cases, field, value
):
    """Preserve canonical types and the exact bootstrap workflow."""
    auth = bootstrap_cases["rubygems"].marker.authorization
    proof = parse_canonical_json(auth.approval_evidence)
    proof[field] = value
    with pytest.raises(ValueError, match=r"Ruby|SHA-256"):
        replace(auth, approval_evidence=canonicalize(proof))


def test_marker_rejects_absence_started_before_approval_even_if_finished_later(
    bootstrap_cases,
):
    """Completion time cannot relabel a preapproval read as postapproval."""
    case = bootstrap_cases["rubygems"]
    auth = replace(
        case.marker.authorization, completed_at=CURRENT + timedelta(seconds=1)
    )
    observed = replace(
        case.marker.absence, observed_at=CURRENT + timedelta(seconds=2)
    )
    ref, identity = transport(
        case.publication.run,
        canonicalize(auth.to_document()),
        artifact_id=404,
        producer=case.publisher,
        path="authorization.json",
    )
    with pytest.raises(ValueError, match="pre-marker"):
        RubyBootstrapMarker(auth, ref, identity, observed)


@pytest.mark.parametrize("destination", ["rubygems", "github-packages"])
@pytest.mark.parametrize("producer", ["publish-ruby-bootstrap", "swapped"])
def test_bootstrap_publication_rejects_legacy_or_other_destination_producer(
    bootstrap_cases, destination, producer
):
    """The closed mapping never accepts legacy aliases or swapped jobs."""
    case = bootstrap_cases[destination]
    if producer == "swapped":
        producer = "publish-ruby-bootstrap-" + (
            "github-packages" if destination == "rubygems" else "rubygems"
        )
    marker = case.marker
    with pytest.raises(ValueError, match="immutable transport"):
        replace(
            marker,
            authorization_transport=replace(
                marker.authorization_transport, producer=producer
            ),
        )
