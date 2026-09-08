"""SYNTHETIC operation facts only; no native provenance or external calls."""

# ruff: noqa: D103, PLR2004

from __future__ import annotations

from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance.native_npm import (
    AcceptanceState,
    PackageControl,
    VersionIdentity,
)
from three_workflow_delivery_v3.acceptance.npm_capture import (
    CaptureFile,
    NpmStateCapture,
)
from three_workflow_delivery_v3.acceptance.npm_evidence import NpmProbeEvidence
from three_workflow_delivery_v3.acceptance.npm_fixture import (
    NpmFixture,
    NpmFixtureSpec,
    build_npm_fixture,
    inspect_npm_fixture,
)
from three_workflow_delivery_v3.acceptance.npm_probe import NpmProbeRequest
from three_workflow_delivery_v3.acceptance.npm_suite import (
    NativeSuiteOperations,
    NpmSuitePlan,
    run_npm_suite,
)
from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.adapters.npm_process import NpmProcessOutcome
from three_workflow_delivery_v3.records.release import ProfileMatchEvidence
from three_workflow_delivery_v3.release.eligibility import (
    DisposablePackagePreconditions,
)

ROOT = Path(__file__).resolve().parents[6]
PACKAGE = "@hcoona/synthetic-fixed-suite"
PRECONDITIONS = DisposablePackagePreconditions(
    PACKAGE,
    preexisting_container=True,
    operator_controlled=True,
    production_dependency=False,
)


def _request(version, target):
    return NpmProbeRequest(
        NpmFixtureSpec(PACKAGE, version, target, "SYNTHETIC"),
        PRECONDITIONS,
    )


PLAN = NpmSuitePlan(
    creation=_request("1.0.0", "a" * 40),
    race_existing=_request("2.0.0", "b" * 40),
    race_candidate=_request("3.0.0", "b" * 40),
)
A = VersionIdentity(101, "1.0.0")
W = VersionIdentity(202, "2.0.0")
V = VersionIdentity(303, "3.0.0")
UNRELATED = VersionIdentity(17, "0.9.0")
TAG_A = "buddy-sha-" + "a" * 40
TAG_RACE = "buddy-sha-" + "b" * 40
CONTROL = PackageControl(
    700,
    PACKAGE,
    "hcoona",
    "public",
    "hcoona/three",
    ("repository:hcoona/three:write",),
)
BASE_TAGS = {"latest": "0.9.0", "stable": "0.9.0", "dangling": "0.0.9"}
NOW = datetime(2026, 9, 7, tzinfo=UTC)
PROBE_LABELS = (
    "create-a",
    "duplicate-a-identical",
    "duplicate-a-different",
    "create-w",
    "candidate-v",
)
CAPTURE_LABELS = (
    "initial",
    "after-create-a",
    "after-duplicate-a-identical",
    "after-duplicate-a-different",
    "after-create-w",
    "after-candidate-v",
)
EXPECTED_EVENTS = (
    "capture:initial",
    "probe:create-a",
    "capture:after-create-a",
    "probe:duplicate-a-identical",
    "capture:after-duplicate-a-identical",
    "probe:duplicate-a-different",
    "capture:after-duplicate-a-different",
    "probe:create-w",
    "capture:after-create-w",
    "probe:candidate-v",
    "capture:after-candidate-v",
)


@pytest.fixture(scope="module")
def fixtures():
    """Build bytes offline with official npm parsing, not comparator mocks."""
    result = {}
    for request in PLAN.requests:
        for variant in ("original", "different"):
            spec = replace(request.fixture, variant=variant)
            fixture = build_npm_fixture(spec, repository_root=ROOT)
            # Model actual artifact encoding, not a local reconstruction.
            # A valid nonzero gzip timestamp changes bytes/digests, not witness.
            body = fixture.tarball[:4] + b"\x01\0\0\0" + fixture.tarball[8:]
            result[spec.version, variant] = NpmFixture(
                body, inspect_npm_fixture(body, repository_root=ROOT)
            )
    return result


def _evidence(label, request, fixture, process):
    run_id = 1001 + PROBE_LABELS.index(label)
    profile = github_packages_destination_operation_profile()
    tag = "buddy-sha-" + request.fixture.target
    match = ProfileMatchEvidence(
        destination_operation_profile_digest=profile.profile_digest,
        node_version=profile.node_version,
        npm_version=profile.npm_version,
        command=tuple(
            {
                "{tarball-path}": (
                    f"/runner/wdv3-native-npm-{run_id}/runtime/fixture.tgz"
                ),
                "{tag}": tag,
            }.get(word, word)
            for word in profile.command_template
        ),
        configuration=tuple(
            sorted(
                {
                    "@hcoona:registry": profile.registry,
                    "registry": profile.registry + "/",
                    "tag": tag,
                    "ignore-scripts": "true",
                    "fetch-retries": "0",
                    "access": "null",
                }.items()
            )
        ),
        matched_at="2026-09-07T00:00:00Z",
    )
    return NpmProbeEvidence(
        run_id=run_id,
        tooling_sha="c" * 40,
        artifact_id=run_id + 2000,
        artifact_digest="sha256:" + "f" * 64,
        artifact_url=(
            "https://api.github.com/repos/hcoona/three/actions/artifacts/"
            f"{run_id + 2000}"
        ),
        request=request,
        fixture=fixture,
        profile_match=match,
        process=process,
        raw_run_metadata=b'{"SYNTHETIC":"run"}',
        raw_artifact_metadata=b'{"SYNTHETIC":"artifact"}',
    )


def _capture(label, fixtures, identities, tags):
    captured_at = NOW + timedelta(minutes=CAPTURE_LABELS.index(label))
    inventory = tuple(sorted(identities, key=lambda item: item.name))
    contents = tuple(
        fixtures[item.name, "original"].content
        for item in inventory
        if item != UNRELATED
    )
    return NpmStateCapture(
        state=AcceptanceState(
            CONTROL,
            tuple(item.name for item in inventory),
            tuple(sorted(tags.items())),
            contents,
        ),
        captured_at=captured_at,
        files=(
            CaptureFile(label + "/SYNTHETIC-state.json", "sha256:" + "e" * 64),
            CaptureFile(label + "/SYNTHETIC-raw.json", "sha256:" + "f" * 64),
        ),
        active_inventory=inventory,
    )


class SyntheticOperations:
    """Scripted complete observations, NOT an authenticated operator backend."""

    def __init__(self, fixtures):
        """Seed independent service snapshots and actual process artifacts."""
        self.events = []
        self.requests = []
        self.capture_arguments = []
        self.denied = False
        self.failures = {}
        self.probes = {}
        for label, request in (
            ("create-a", PLAN.creation),
            ("duplicate-a-identical", PLAN.creation),
            (
                "duplicate-a-different",
                replace(
                    PLAN.creation,
                    fixture=replace(PLAN.creation.fixture, variant="different"),
                ),
            ),
            ("create-w", PLAN.race_existing),
            ("candidate-v", PLAN.race_candidate),
        ):
            process = (
                NpmProcessOutcome("definitive-non-success", returncode=1)
                if label.startswith("duplicate")
                else NpmProcessOutcome("definitive-success", returncode=0)
            )
            self.probes[label] = _evidence(
                label,
                request,
                fixtures[request.fixture.version, request.fixture.variant],
                process,
            )
        tags_a = {**BASE_TAGS, TAG_A: A.name}
        tags_w = {**tags_a, TAG_RACE: W.name}
        tags_v = {**tags_a, TAG_RACE: V.name}
        self.captures = {
            label: _capture(label, fixtures, identities, tags)
            for label, identities, tags in (
                ("initial", (UNRELATED,), BASE_TAGS),
                ("after-create-a", (UNRELATED, A), tags_a),
                ("after-duplicate-a-identical", (UNRELATED, A), tags_a),
                ("after-duplicate-a-different", (UNRELATED, A), tags_a),
                ("after-create-w", (UNRELATED, A, W), tags_w),
                ("after-candidate-v", (UNRELATED, A, W, V), tags_v),
            )
        }

    def _record(self, event):
        self.events.append(event)
        if event in self.failures:
            raise self.failures[event]

    def capture(self, label, *, plan):
        """Return observed selectors, not reconstructed expected fixtures."""
        self._record("capture:" + label)
        self.capture_arguments.append(plan)
        if self.denied:
            message = "SYNTHETIC backend: no separate disposable approval"
            raise PermissionError(message)
        return self.captures[label]

    def probe(self, label, request):
        """Return a synthetic artifact; no process or workflow is launched."""
        self._record("probe:" + label)
        self.requests.append(request)
        return self.probes[label]


@pytest.fixture
def ops(fixtures):
    return SyntheticOperations(fixtures)


def _process(ops, label, classification):
    ops.probes[label] = replace(
        ops.probes[label],
        process=NpmProcessOutcome(
            classification,
            returncode=(
                0
                if classification == "definitive-success"
                else 1
                if classification == "definitive-non-success"
                else None
            ),
        ),
    )


def _state(ops, label, **changes):
    capture = ops.captures[label]
    ops.captures[label] = replace(
        capture, state=replace(capture.state, **changes)
    )


def _stop_at(ops, event):
    assert ops.events == list(
        EXPECTED_EVENTS[: EXPECTED_EVENTS.index(event) + 1]
    )


def test_complete_fixed_sequence_retains_actual_bytes_ids_and_active_readback(
    ops, fixtures
):
    result = run_npm_suite(PLAN, ops)

    assert ops.events == list(EXPECTED_EVENTS)
    assert result.probes == tuple(ops.probes[label] for label in PROBE_LABELS)
    assert tuple(item.run_id for item in result.probes) == tuple(
        range(1001, 1006)
    )
    assert result.captures == tuple(
        ops.captures[label] for label in CAPTURE_LABELS
    )
    assert all(capture.files for capture in result.captures)
    assert len(result.probes) == 5
    assert len(result.captures) == 6
    assert ops.capture_arguments == [PLAN] * 6
    assert PLAN.requests == (
        PLAN.creation,
        PLAN.race_existing,
        PLAN.race_candidate,
    )
    assert {request.fixture.version for request in PLAN.requests} == {
        A.name,
        W.name,
        V.name,
    }
    assert {request.fixture.target for request in PLAN.requests} == {
        "a" * 40,
        "b" * 40,
    }
    assert not hasattr(result, "original_deletion")
    for route in ("delete_exact", "restore_exact"):
        assert not hasattr(NativeSuiteOperations, route)
    assert [request.fixture.variant for request in ops.requests] == [
        "original",
        "original",
        "different",
        "original",
        "original",
    ]
    final = result.captures[-1]
    assert final.active_inventory == (UNRELATED, A, W, V)
    assert final.state.contents == tuple(
        fixtures[identity.name, "original"].content for identity in (A, W, V)
    )
    assert dict(final.state.tags) == {
        **BASE_TAGS,
        TAG_A: A.name,
        TAG_RACE: V.name,
    }
    for request in PLAN.requests:
        reconstructed = build_npm_fixture(request.fixture, repository_root=ROOT)
        assert (
            fixtures[request.fixture.version, "original"].tarball
            != reconstructed.tarball
        )


@pytest.mark.parametrize(
    "candidate_state", ["absent", "exact-at-w", "exact-at-v"]
)
def test_candidate_failure_safety_passes_without_upgrading_process(
    ops, candidate_state
):
    _process(ops, "candidate-v", "definitive-non-success")
    if candidate_state != "exact-at-v":
        for label in CAPTURE_LABELS[5:]:
            capture = ops.captures[label]
            tags = dict(capture.state.tags)
            tags[TAG_RACE] = W.name
            _state(ops, label, tags=tuple(sorted(tags.items())))
            if candidate_state == "absent":
                _state(
                    ops,
                    label,
                    active_versions=tuple(
                        name
                        for name in capture.state.active_versions
                        if name != V.name
                    ),
                    contents=tuple(
                        item
                        for item in capture.state.contents
                        if item.version != V.name
                    ),
                )
                ops.captures[label] = replace(
                    ops.captures[label],
                    active_inventory=tuple(
                        item for item in capture.active_inventory if item != V
                    ),
                )

    result = run_npm_suite(PLAN, ops)

    assert ops.events == list(EXPECTED_EVENTS)
    candidate = result.probes[4]
    assert candidate.process.classification == "definitive-non-success"
    assert candidate.process.returncode == 1
    assert candidate.run_id == 1005
    assert candidate.raw_run_metadata == b'{"SYNTHETIC":"run"}'
    observed = result.captures[5].state
    assert (V.name in observed.active_versions) == (candidate_state != "absent")
    assert ops.probes["create-w"].fixture.content in observed.contents
    if candidate_state != "absent":
        assert candidate.fixture.content in observed.contents


def test_candidate_success_without_creation_cannot_complete_suite(ops):
    before = ops.captures["after-create-w"]
    ops.captures["after-candidate-v"] = replace(
        ops.captures["after-candidate-v"],
        state=before.state,
        active_inventory=before.active_inventory,
    )
    with pytest.raises(ValueError, match="actually present and exact"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:after-candidate-v")


@pytest.mark.parametrize(
    ("label", "version"),
    [
        ("after-create-a", A.name),
        ("after-create-w", W.name),
    ],
)
def test_successful_creation_requires_actual_artifact_content(
    ops, fixtures, label, version
):
    _state(
        ops,
        label,
        contents=tuple(
            fixtures[version, "different"].content
            if item.version == version
            else item
            for item in ops.captures[label].state.contents
        ),
    )
    with pytest.raises(ValueError, match="delta changed: contents"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)


@pytest.mark.parametrize("version", [W.name, V.name])
def test_race_failure_with_creation_requires_both_versions_exact(
    ops, fixtures, version
):
    _process(ops, "candidate-v", "definitive-non-success")
    label = "after-candidate-v"
    _state(
        ops,
        label,
        contents=tuple(
            fixtures[version, "different"].content
            if item.version == version
            else item
            for item in ops.captures[label].state.contents
        ),
    )
    with pytest.raises(ValueError, match="delta changed: contents"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)


def test_race_cannot_change_unrelated_tag(ops):
    label = "after-candidate-v"
    tags = dict(ops.captures[label].state.tags)
    tags["stable"] = V.name
    _state(ops, label, tags=tuple(sorted(tags.items())))
    with pytest.raises(ValueError, match="delta changed: tags"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)


@pytest.mark.parametrize(
    ("label", "classification"),
    [
        ("duplicate-a-identical", "ambiguous"),
        ("duplicate-a-different", "definitive-success"),
        ("candidate-v", "ambiguous"),
        ("create-a", "not-initiated"),
        ("create-w", "definitive-non-success"),
    ],
)
def test_unacceptable_process_stops_all_later_mutation(
    ops, label, classification
):
    _process(ops, label, classification)
    with pytest.raises(ValueError, match="process classification"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "probe:" + label)


@pytest.mark.parametrize(
    "label",
    [
        "after-duplicate-a-identical",
        "after-duplicate-a-different",
    ],
)
@pytest.mark.parametrize(
    ("damage", "message"),
    [
        ("latest", "delta changed: tags"),
        ("control", "delta changed: control"),
        ("unrelated-version", "delta changed: active_versions"),
        ("missing-original", "incomplete selected content"),
        ("different-original", "requires exact original content"),
    ],
)
def test_duplicate_semantic_delta_stops_before_next_probe(
    ops, fixtures, label, damage, message
):
    capture = ops.captures[label]
    if damage == "latest":
        tags = dict(capture.state.tags)
        tags["latest"] = A.name
        _state(ops, label, tags=tuple(sorted(tags.items())))
    elif damage == "control":
        _state(ops, label, control=replace(CONTROL, container_id=701))
    elif damage == "unrelated-version":
        _state(ops, label, active_versions=(A.name,))
        ops.captures[label] = replace(
            ops.captures[label], active_inventory=(A,)
        )
    else:
        _state(
            ops,
            label,
            contents=(
                ()
                if damage == "missing-original"
                else (fixtures[A.name, "different"].content,)
            ),
        )
    with pytest.raises(ValueError, match=message):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)


@pytest.mark.parametrize(
    ("label", "wrong_variant"),
    [
        ("duplicate-a-identical", "different"),
        ("duplicate-a-different", "original"),
    ],
)
def test_duplicate_artifact_bytes_must_match_same_version_variant(
    ops, fixtures, label, wrong_variant
):
    evidence = ops.probes[label]
    ops.probes[label] = replace(
        evidence,
        fixture=fixtures[evidence.request.fixture.version, wrong_variant],
    )
    with pytest.raises(ValueError, match="actual bytes"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "probe:" + label)


@pytest.mark.parametrize("requested", PLAN.requests)
def test_initial_existing_version_stops_before_any_probe(
    ops, fixtures, requested
):
    identity = next(
        item for item in (A, W, V) if item.name == requested.fixture.version
    )
    ops.captures["initial"] = _capture(
        "initial", fixtures, (UNRELATED, identity), BASE_TAGS
    )
    with pytest.raises(ValueError, match="absent scenario versions and tags"):
        run_npm_suite(PLAN, ops)
    assert ops.events == ["capture:initial"]


@pytest.mark.parametrize("tag", [TAG_A, TAG_RACE])
def test_initial_existing_tag_stops_before_any_probe(ops, tag):
    _state(
        ops, "initial", tags=tuple(sorted({**BASE_TAGS, tag: "0.0.9"}.items()))
    )
    with pytest.raises(ValueError, match="absent scenario versions and tags"):
        run_npm_suite(PLAN, ops)
    assert ops.events == ["capture:initial"]


@pytest.mark.parametrize(
    ("field", "change"),
    [
        ("race_candidate", {"version": W.name}),
        ("race_candidate", {"target": "e" * 40}),
        ("creation", {"target": "b" * 40}),
        ("race_existing", {"generation": "another-generation"}),
        ("creation", {"variant": "different"}),
    ],
)
def test_plan_rejects_collisions_mixed_generation_and_nonoriginals(
    field, change
):
    request = getattr(PLAN, field)
    with pytest.raises(ValueError, match=r"suite requires|suite requests"):
        replace(
            PLAN,
            **{
                field: replace(
                    request, fixture=replace(request.fixture, **change)
                )
            },
        )


def test_plan_requires_all_requests_one_package_and_no_implicit_approval(ops):
    with pytest.raises(TypeError):
        NpmSuitePlan()  # pyrefly: ignore[missing-argument]
    with pytest.raises(TypeError, match="deleted_original"):
        NpmSuitePlan(
            *PLAN.requests,
            deleted_original=PLAN.creation,  # pyrefly: ignore[unexpected-keyword]
        )
    with pytest.raises(ValueError, match="explicit typed original"):
        replace(PLAN, creation=None)  # pyrefly: ignore[bad-argument-type]
    other = "@hcoona/another-synthetic-package"
    different_package = replace(
        PLAN.creation,
        fixture=replace(PLAN.creation.fixture, package=other),
        disposable_package_preconditions=replace(PRECONDITIONS, package=other),
    )
    with pytest.raises(ValueError, match="share package"):
        replace(PLAN, creation=different_package)
    with pytest.raises(ValueError, match="preconditions must be passing"):
        replace(PRECONDITIONS, production_dependency=True)

    ops.denied = True
    with pytest.raises(
        PermissionError, match="no separate disposable approval"
    ):
        run_npm_suite(PLAN, ops)
    assert ops.events == ["capture:initial"]


@pytest.mark.parametrize(
    "event",
    EXPECTED_EVENTS,
)
def test_operation_exception_preserves_audit_and_never_mutates_again(
    ops, event
):
    error = RuntimeError("SYNTHETIC incomplete/ambiguous native operation")
    ops.failures[event] = error
    with pytest.raises(RuntimeError) as raised:
        run_npm_suite(PLAN, ops)
    assert raised.value is error
    _stop_at(ops, event)
    assert len(ops.events) == len(set(ops.events))


@pytest.mark.parametrize("damage", ["request", "run", "artifact"])
def test_probe_binding_or_replayed_evidence_blocks_next_mutation(ops, damage):
    label = "duplicate-a-identical"
    evidence = ops.probes[label]
    if damage == "request":
        evidence = replace(evidence, request=PLAN.race_existing)
    elif damage == "run":
        evidence = replace(evidence, run_id=ops.probes["create-a"].run_id)
    else:
        evidence = replace(
            evidence, artifact_id=ops.probes["create-a"].artifact_id
        )
    ops.probes[label] = evidence
    with pytest.raises(ValueError, match="request mismatch or reused"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "probe:" + label)
