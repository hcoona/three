"""One fixed LLD 18.6 sequence over supplied native-operation facts.

No concrete IO, approval, native certification or Governance installation lives
here. Synthetic operations can pass these gates without proving provenance.
Any exception stops the sequence, including restoration: retain the backend's
audit for read-only operator investigation, never retry or repair automatically.
"""

from __future__ import annotations

from dataclasses import dataclass, replace
from typing import TYPE_CHECKING, Protocol

from three_workflow_delivery_v3.acceptance.native_npm import (
    AcceptanceState,
    empty_delta,
    require_active_duplicate_delta,
    require_creation_delta,
    require_deleted_duplicate_delta,
    require_restoration_delta,
    require_tag_race_delta,
)
from three_workflow_delivery_v3.acceptance.npm_capture import (
    NpmStateCapture,
    OriginalDeletionContext,
)
from three_workflow_delivery_v3.acceptance.npm_evidence import NpmProbeEvidence
from three_workflow_delivery_v3.acceptance.npm_probe import NpmProbeRequest

if TYPE_CHECKING:
    from three_workflow_delivery_v3.acceptance.native_npm import (
        PackageControl,
        VersionIdentity,
    )
    from three_workflow_delivery_v3.adapters.npm_process import (
        CommandClassification,
    )


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


@dataclass(frozen=True)
class NpmSuitePlan:
    """Four explicit originals, not package approval or a configurable suite.

    Prior disposable approval belongs to the operator. These typed requests
    only assert common bindings; official npm parsing belongs to capture/probe.
    """

    creation: NpmProbeRequest
    race_existing: NpmProbeRequest
    race_candidate: NpmProbeRequest
    deleted_original: NpmProbeRequest

    def __post_init__(self) -> None:
        """Bind A, W, V, D to one generation and three disjoint target tags."""
        requests = self.requests
        _require(
            all(type(request) is NpmProbeRequest for request in requests),
            "suite requires four explicit typed original requests",
        )
        first = self.creation
        _require(
            all(
                request.fixture.package == first.fixture.package
                and request.disposable_package_preconditions
                == first.disposable_package_preconditions
                and request.fixture.generation == first.fixture.generation
                and request.fixture.variant == "original"
                for request in requests
            ),
            "suite requests must share package, preconditions and generation",
        )
        _require(
            len({request.fixture.version for request in requests})
            == len(requests),
            "suite requires four distinct versions",
        )
        _require(
            self.race_existing.fixture.target
            == self.race_candidate.fixture.target
            and len(
                {
                    first.fixture.target,
                    self.race_existing.fixture.target,
                    self.deleted_original.fixture.target,
                }
            )
            == 3,  # noqa: PLR2004
            "suite requires shared race target and distinct A/race/D targets",
        )

    @property
    def requests(self) -> tuple[NpmProbeRequest, ...]:
        """Return the fixed complete capture selectors in A/W/V/D order."""
        return (
            self.creation,
            self.race_existing,
            self.race_candidate,
            self.deleted_original,
        )


class NativeSuiteOperations(Protocol):
    """Actual authenticated acceptance-only operations, with no hidden retry.

    The backend must establish prior exact disposable-package authorization and
    its administrative boundary, independently of plan assertions. Capture uses
    complete native reads for all four plan selectors and retains raw files.
    Probe dispatches once through the distinct protected-main Actions entry,
    using the actual repository GITHUB_TOKEN, and reads evidence bound to the
    exact returned run ID, tooling revision, request and immutable artifact.
    Local admin credentials must never enter probe/runtime inputs.

    Preserve successful and partial audit files even if any call raises. A
    synthetic implementation must identify itself as such: neither this
    protocol nor a completed result certifies real operations or admits Live.
    """

    def capture(
        self,
        label: str,
        *,
        plan: NpmSuitePlan,
        original_deletion: OriginalDeletionContext | None = None,
    ) -> NpmStateCapture:
        """Collect complete actual state, never expected content."""
        ...

    def probe(self, label: str, request: NpmProbeRequest) -> NpmProbeEvidence:
        """Invoke once; retain actual classification, fixture and run facts."""
        ...

    def delete_exact(
        self, original_control: PackageControl, original: VersionIdentity
    ) -> OriginalDeletionContext:
        """Record a conservative time BEFORE one authorized exact-ID delete.

        Bind the captured container/namespace and original version ID; never
        discover by name or refresh the time after deletion. Return context,
        not an assertion that deletion succeeded; native readback must follow.
        """
        ...

    def restore_exact(self, context: OriginalDeletionContext) -> None:
        """Restore the original ID once; errors/ambiguity raise, never retry."""
        ...


@dataclass(frozen=True)
class NpmSuiteResult:
    """Completed supplied-fact gates, not an installed native generation.

    Ordered probe facts retain actual failures and exact run/artifact IDs.
    Captures retain raw-file references; the last is verified restored readback.
    The caller/backend owns audit retention, including every failed sequence.
    """

    probes: tuple[NpmProbeEvidence, ...]
    captures: tuple[NpmStateCapture, ...]
    original_deletion: OriginalDeletionContext


class _Audit:
    def __init__(
        self, plan: NpmSuitePlan, operations: NativeSuiteOperations
    ) -> None:
        self.plan = plan
        self.operations = operations
        self.probes: list[NpmProbeEvidence] = []
        self.captures: list[NpmStateCapture] = []

    def capture(
        self, label: str, context: OriginalDeletionContext | None = None
    ) -> AcceptanceState:
        capture = self.operations.capture(
            label, plan=self.plan, original_deletion=context
        )
        _require(
            type(capture) is NpmStateCapture,
            f"{label}: missing typed complete capture",
        )
        state = capture.state
        _require(
            state.control.full_scoped_name == self.plan.creation.fixture.package
            and capture.original_deletion == context,
            f"{label}: capture package or deletion context mismatch",
        )
        _require(
            tuple(item.name for item in capture.active_inventory)
            == state.active_versions
            and len({item.version_id for item in capture.active_inventory})
            == len(capture.active_inventory)
            and {item.version for item in state.contents}
            == {
                request.fixture.version
                for request in self.plan.requests
                if request.fixture.version in state.active_versions
            },
            f"{label}: incomplete selected content or active identities",
        )
        tombstone = state.tombstone
        if context is None:
            _require(tombstone is None, f"{label}: unexpected deleted facts")
        else:
            _require(
                tombstone is not None
                and tombstone.target == context.original_version,
                f"{label}: original tombstone identity mismatch",
            )
            if tombstone is not None and tombstone.restorability is not None:
                _require(
                    tombstone.restorability.deletion_observed_at
                    == context.deletion_lower_bound_at
                    and tombstone.restorability.inspected_at
                    == capture.captured_at,
                    f"{label}: original deletion time or inspection mismatch",
                )
        self.captures.append(capture)
        return state

    def probe(
        self,
        label: str,
        request: NpmProbeRequest,
        permitted: tuple[CommandClassification, ...],
    ) -> NpmProbeEvidence:
        evidence = self.operations.probe(label, request)
        _require(
            type(evidence) is NpmProbeEvidence
            and evidence.request == request
            and evidence.run_id not in {item.run_id for item in self.probes}
            and evidence.artifact_id
            not in {item.artifact_id for item in self.probes},
            f"{label}: probe request mismatch or reused run/artifact",
        )
        self.probes.append(evidence)
        _require(
            evidence.process.classification in permitted,
            f"{label}: unacceptable process classification",
        )
        return evidence


def _tag(request: NpmProbeRequest) -> str:
    return "buddy-sha-" + request.fixture.target


def _different(request: NpmProbeRequest) -> NpmProbeRequest:
    return replace(
        request, fixture=replace(request.fixture, variant="different")
    )


def _absent(
    state: AcceptanceState, requests: tuple[NpmProbeRequest, ...]
) -> None:
    _require(
        all(
            request.fixture.version not in state.active_versions
            and _tag(request) not in dict(state.tags)
            for request in requests
        ),
        "suite requires absent scenario versions and tags",
    )


def _duplicate_bytes(
    evidence: NpmProbeEvidence, original: NpmProbeEvidence
) -> None:
    _require(
        evidence.fixture.content.version == original.fixture.content.version
        and (evidence.fixture.tarball == original.fixture.tarball)
        == (evidence.request.fixture.variant == "original"),
        "duplicate probe actual bytes do not match the requested variant",
    )


def _deletion_delta(
    before: AcceptanceState,
    after: AcceptanceState,
    request: NpmProbeRequest,
) -> None:
    _require(
        after.tombstone is not None
        and after.tombstone.restorability is not None
        and request.fixture.version not in after.active_versions,
        "delete readback requires the original deleted/restorable target",
    )
    # No deleted baseline was queried. Compare only the known projection,
    # allowing removal of D and an actual change to its scenario-owned tag.
    tag = _tag(request)
    projected_before = replace(
        before,
        active_versions=tuple(
            name
            for name in before.active_versions
            if name != request.fixture.version
        ),
        contents=tuple(
            item
            for item in before.contents
            if item.version != request.fixture.version
        ),
        tags=tuple(pair for pair in before.tags if pair[0] != tag),
    )
    projected_after = replace(
        after,
        tags=tuple(pair for pair in after.tags if pair[0] != tag),
        tombstone=None,
    )
    empty_delta(projected_before, projected_after)


def run_npm_suite(
    plan: NpmSuitePlan, operations: NativeSuiteOperations
) -> NpmSuiteResult:
    """Run the fixed eight probes and two admin mutations; stop on any failure.

    This function has no recovery/finally mutation. Only the valid normal path
    reaches restoration. Success means supplied operation facts passed gates,
    not that those operations were real or separately authorized.
    """
    _require(
        type(plan) is NpmSuitePlan, "suite requires an explicit typed plan"
    )
    audit = _Audit(plan, operations)
    current = audit.capture("initial")
    _absent(current, plan.requests)

    created_a = audit.probe("create-a", plan.creation, ("definitive-success",))
    after = audit.capture("after-create-a")
    require_creation_delta(
        current, after, created_a.fixture.content, _tag(plan.creation)
    )
    current = after
    for label, request in (
        ("duplicate-a-identical", plan.creation),
        ("duplicate-a-different", _different(plan.creation)),
    ):
        duplicate = audit.probe(label, request, ("definitive-non-success",))
        _duplicate_bytes(duplicate, created_a)
        after = audit.capture("after-" + label)
        require_active_duplicate_delta(
            current, after, created_a.fixture.content
        )
        current = after

    _absent(current, (plan.race_existing, plan.race_candidate))
    created_w = audit.probe(
        "create-w", plan.race_existing, ("definitive-success",)
    )
    after = audit.capture("after-create-w")
    require_creation_delta(
        current, after, created_w.fixture.content, _tag(plan.race_existing)
    )
    candidate = audit.probe(
        "candidate-v",
        plan.race_candidate,
        ("definitive-success", "definitive-non-success"),
    )
    current = audit.capture("after-candidate-v")
    require_tag_race_delta(
        after,
        current,
        candidate.fixture.content,
        created_w.fixture.content,
        _tag(plan.race_candidate),
    )
    _require(
        candidate.process.classification != "definitive-success"
        or candidate.fixture.content in current.contents,
        "successful race candidate must be actually present and exact",
    )

    created_d = audit.probe(
        "create-d", plan.deleted_original, ("definitive-success",)
    )
    after = audit.capture("after-create-d")
    require_creation_delta(
        current, after, created_d.fixture.content, _tag(plan.deleted_original)
    )
    original_id = next(
        item
        for item in audit.captures[-1].active_inventory
        if item.name == plan.deleted_original.fixture.version
    )
    context = operations.delete_exact(after.control, original_id)
    _require(
        type(context) is OriginalDeletionContext
        and context.original_control == after.control
        and context.original_version == original_id,
        "delete context must bind the captured original control and ID",
    )
    current = audit.capture("after-delete-d", context)
    _deletion_delta(after, current, plan.deleted_original)
    for label, request in (
        ("duplicate-d-identical", plan.deleted_original),
        ("duplicate-d-different", _different(plan.deleted_original)),
    ):
        duplicate = audit.probe(label, request, ("definitive-non-success",))
        _duplicate_bytes(duplicate, created_d)
        after = audit.capture("after-" + label, context)
        require_deleted_duplicate_delta(current, after)
        current = after

    operations.restore_exact(context)
    restored = audit.capture("after-restore-d", context)
    require_restoration_delta(
        current,
        restored,
        created_d.fixture.content,
        _tag(plan.deleted_original),
    )
    return NpmSuiteResult(tuple(audit.probes), tuple(audit.captures), context)
