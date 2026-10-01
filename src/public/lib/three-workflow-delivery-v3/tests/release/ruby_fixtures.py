"""Locally qualified Ruby publication scenarios without platform authority."""

from dataclasses import dataclass, replace
from datetime import timedelta

import pytest
from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_digest,
)
from three_workflow_delivery_v3.adapters.ruby_registry import (
    RubyHttpResponse,
    RubyPublishingCredential,
    RubyRegistryReader,
    RubyRegistryWriter,
    RubyRequestBudget,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.release.ruby_execution import (
    execute_ruby_publication,
)
from three_workflow_delivery_v3.release.ruby_governance import RUBY_WORKFLOW
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyApprovalBundle,
    RubyMutationMarker,
    RubyPublicationAuthorization,
    RubyPublicationSnapshot,
    RubyRemoteObservation,
    render_ruby_approval_summary,
)
from three_workflow_delivery_v3.release.ruby_qualification import (
    RubyQualificationDecision,
    RubyQualificationSnapshot,
    qualify_ruby_release,
)

from ..ruby_integration_fixtures import NOW, consumer, governance, reference
from ..ruby_registry_fixtures import (
    READ_TOKEN,
    VERSION,
    WRITE_TOKEN,
    ScriptedTransport,
    inventory,
    json_response,
    metadata,
    native_index,
)


@dataclass
class PublicationCase:
    """Retain one actual qualified gem with modeled destination admission."""

    original: object
    decision: object
    missing: object
    exact: object
    snapshot: object
    bundle: object
    authorization: object
    marker: object
    indexes: dict

    @property
    def registry(self):
        """Return the independently selected destination."""
        return self.decision.snapshot.governance.registry

    def missing_responses(self):
        """Supply complete native inventory absence for the destination."""
        return {
            "rubygems": [json_response([])],
            "github-packages": [RubyHttpResponse(200, self.indexes["empty"])]
            * 2,
        }[self.registry.name]

    def exact_responses(self):
        """Supply exact metadata and the original immutable native gem."""
        return {
            "rubygems": [inventory(), json_response(metadata(self.original))],
            "github-packages": [
                RubyHttpResponse(200, self.indexes["empty"]),
                RubyHttpResponse(200, self.indexes["exact"]),
            ],
        }[self.registry.name] + [RubyHttpResponse(200, self.original.content)]

    def reader(self, budget):
        """Inject an offline transport, never a socket-capable client."""
        return RubyRegistryReader(
            self.registry,
            budget,
            github_read_token=READ_TOKEN
            if self.registry.name == "github-packages"
            else None,
        )


def approval_proof(snapshot):
    """Model exact native approval fields without claiming live evidence."""
    return {
        "repository": "hcoona/three",
        "run-id": snapshot.attempt.workflow_run_id,
        "run-attempt": 1,
        "target": snapshot.attempt.execution.target,
        "environment": snapshot.registry.environment,
        "environment-id": 1901,
        "deployment-id": 8701,
        "reviewer-id": 712433,
        "reviewer": "hcoona",
        "state": "approved",
        "native-response-digest": "sha256:" + "d" * 64,
        "sentinel": snapshot.registry.environment + "/v1",
    }


@pytest.fixture(scope="session")
def ruby_publication_cases(ruby_release_original, tmp_path_factory):
    """Qualify one original with separate modeled destination authority."""
    source, artifact, original = ruby_release_original
    root = tmp_path_factory.mktemp("ruby-publication-index")
    indexes = {
        "empty": native_index(root, []),
        "exact": native_index(root, [[RUBY_RELEASE_UNIT, VERSION, "ruby"]]),
    }
    cases = {}
    for name in ("rubygems", "github-packages"):
        gov = governance(name)
        ctx = source.context
        intent = ReleaseIntent(
            "hcoona/three",
            RUBY_WORKFLOW,
            "refs/heads/main",
            ctx.target,
            ctx.request_id,
            "hcoona",
            ctx.workflow_run_id,
            "workflow_dispatch",
            "refs/heads/main",
            ctx.target,
            gov.registry.channel,
            "live",
            "live-release",
            RUBY_RELEASE_UNIT,
        )
        planned = RubyQualificationSnapshot(
            intent, source, reference(source.to_document()), gov
        )
        decision = RubyQualificationDecision(
            planned,
            qualify_ruby_release(
                planned, artifact, original.content, consumer=consumer
            ),
        )
        case = PublicationCase(
            original, decision, None, None, None, None, None, None, indexes
        )
        case.missing = case.reader(
            RubyRequestBudget(ScriptedTransport(*case.missing_responses()))
        ).observe(original)
        case.exact = case.reader(
            RubyRequestBudget(ScriptedTransport(*case.exact_responses()))
        ).observe(original)
        observation = RubyRemoteObservation(
            decision, reference(decision.to_document(), 901), case.missing, NOW
        )
        case.snapshot = RubyPublicationSnapshot(
            observation, reference(observation.to_document(), 902)
        )
        summary_ref = replace(
            reference({}, 904, "approval.txt"),
            payload_digest=ruby_digest(
                render_ruby_approval_summary(case.snapshot)
            ),
        )
        case.bundle = RubyApprovalBundle(
            case.snapshot,
            reference(case.snapshot.to_document(), 903),
            summary_ref,
        )
        case.authorization = RubyPublicationAuthorization(
            case.bundle,
            reference(case.bundle.to_document(), 905),
            canonicalize(approval_proof(case.snapshot)),
            NOW + timedelta(seconds=1),
        )
        case.marker = RubyMutationMarker(
            case.authorization,
            reference(case.authorization.to_document(), 906),
            replace(gov, observed_at=NOW + timedelta(seconds=2)),
            case.missing,
            NOW + timedelta(seconds=2),
        )
        cases[name] = case
    return cases


class VirtualClock:
    """Move UTC and monotonic time together without wall-clock waits."""

    def __init__(self):
        """Begin after the independently retained marker."""
        self.utc = NOW + timedelta(seconds=3)
        self.tick = 100.0
        self.waits = []

    def clock(self):
        """Return the current aware timestamp."""
        return self.utc

    def monotonic(self):
        """Return the current monotonic observation."""
        return self.tick

    def advance(self, seconds):
        """Model a dependency's elapsed duration."""
        self.utc += timedelta(seconds=seconds)
        self.tick += seconds

    def wait(self, seconds):
        """Record required pacing and advance time immediately."""
        self.waits.append(seconds)
        self.advance(seconds)


class TimedTransport(ScriptedTransport):
    """Consume controlled HTTP responses with optional modeled durations."""

    def __init__(self, timeline, responses, delays=()):
        """Share the same clock as the publication executor."""
        super().__init__(*responses)
        self.timeline = timeline
        self.delays = list(delays)

    def request(self, method, url, headers, body, maximum_bytes):
        """Advance elapsed time for this request before returning or failing."""
        self.timeline.advance(self.delays.pop(0) if self.delays else 0)
        return super().request(method, url, headers, body, maximum_bytes)


def execute(  # noqa: PLR0913 - controlled publication scenario inputs
    case,
    tmp_path,
    responses,
    *,
    delays=(),
    timeline=None,
    deadline_seconds=120,
    payload=None,
    wait=None,
):
    """Execute the real adapter/Release boundary with offline transport only."""
    timeline = VirtualClock() if timeline is None else timeline
    transport = TimedTransport(timeline, responses, delays)
    budget = RubyRequestBudget(transport)
    credential = {
        "rubygems": RubyPublishingCredential(
            WRITE_TOKEN, NOW + timedelta(minutes=15)
        ),
        "github-packages": WRITE_TOKEN,
    }[case.registry.name]
    result = execute_ruby_publication(
        case.marker,
        reference(case.marker.to_document(), 907),
        case.original.content if payload is None else payload,
        credential=credential,
        reader=case.reader(budget),
        writer=RubyRegistryWriter(case.registry, budget),
        claim_path=tmp_path / "claim.json",
        deadline=timeline.utc + timedelta(seconds=deadline_seconds),
        clock=timeline.clock,
        monotonic=timeline.monotonic,
        wait=timeline.wait if wait is None else wait,
    )
    return result, transport, timeline
