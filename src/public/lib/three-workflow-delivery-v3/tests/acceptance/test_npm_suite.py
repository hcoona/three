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
    RestorabilityEvidence,
    TombstoneState,
    VersionIdentity,
)
from three_workflow_delivery_v3.acceptance.npm_capture import (
    CaptureFile,
    NpmStateCapture,
    OriginalDeletionContext,
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
    deleted_original=_request("4.0.0", "d" * 40),
)
A = VersionIdentity(101, "1.0.0")
W = VersionIdentity(202, "2.0.0")
V = VersionIdentity(303, "3.0.0")
D = VersionIdentity(404, "4.0.0")
UNRELATED = VersionIdentity(17, "0.9.0")
OLD_DELETED = VersionIdentity(88, "0.1.0")
TAG_A = "buddy-sha-" + "a" * 40
TAG_RACE = "buddy-sha-" + "b" * 40
TAG_D = "buddy-sha-" + "d" * 40
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
CONTEXT = OriginalDeletionContext(CONTROL, D, NOW + timedelta(minutes=7))
PROBE_LABELS = (
    "create-a",
    "duplicate-a-identical",
    "duplicate-a-different",
    "create-w",
    "candidate-v",
    "create-d",
    "duplicate-d-identical",
    "duplicate-d-different",
)
CAPTURE_LABELS = (
    "initial",
    "after-create-a",
    "after-duplicate-a-identical",
    "after-duplicate-a-different",
    "after-create-w",
    "after-candidate-v",
    "after-create-d",
    "after-delete-d",
    "after-duplicate-d-identical",
    "after-duplicate-d-different",
    "after-restore-d",
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
    "probe:create-d",
    "capture:after-create-d",
    "delete",
    "capture:after-delete-d",
    "probe:duplicate-d-identical",
    "capture:after-duplicate-d-identical",
    "probe:duplicate-d-different",
    "capture:after-duplicate-d-different",
    "restore",
    "capture:after-restore-d",
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
    context = CONTEXT if CAPTURE_LABELS.index(label) >= 7 else None
    tombstone = None
    if context is not None:
        tombstone = TombstoneState(
            deleted_versions=(
                (OLD_DELETED,)
                if label == "after-restore-d"
                else (OLD_DELETED, D)
            ),
            target=D,
            restorability=(
                None
                if label == "after-restore-d"
                else RestorabilityEvidence(
                    CONTROL, D, CONTEXT.deletion_lower_bound_at, captured_at
                )
            ),
        )
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
            tombstone,
        ),
        captured_at=captured_at,
        original_deletion=context,
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
        self.deleted = []
        self.restored = []
        self.denied = False
        self.failures = {}
        self.context = CONTEXT
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
            ("create-d", PLAN.deleted_original),
            ("duplicate-d-identical", PLAN.deleted_original),
            (
                "duplicate-d-different",
                replace(
                    PLAN.deleted_original,
                    fixture=replace(
                        PLAN.deleted_original.fixture, variant="different"
                    ),
                ),
            ),
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
                (
                    "after-create-d",
                    (UNRELATED, A, W, V, D),
                    {**tags_v, TAG_D: D.name},
                ),
                ("after-delete-d", (UNRELATED, A, W, V), tags_v),
                ("after-duplicate-d-identical", (UNRELATED, A, W, V), tags_v),
                ("after-duplicate-d-different", (UNRELATED, A, W, V), tags_v),
                ("after-restore-d", (UNRELATED, A, W, V, D), tags_v),
            )
        }

    def _record(self, event):
        self.events.append(event)
        if event in self.failures:
            raise self.failures[event]

    def capture(self, label, *, plan, original_deletion=None):
        """Return observed selectors, not reconstructed expected fixtures."""
        self._record("capture:" + label)
        self.capture_arguments.append((plan, original_deletion))
        if self.denied:
            message = "SYNTHETIC backend: no separate disposable approval"
            raise PermissionError(message)
        return self.captures[label]

    def probe(self, label, request):
        """Return a synthetic artifact; no process or workflow is launched."""
        self._record("probe:" + label)
        self.requests.append(request)
        return self.probes[label]

    def delete_exact(self, original_control, original):
        """Record the exact caller operands without any external mutation."""
        self._record("delete")
        self.deleted.append((original_control, original))
        return self.context

    def restore_exact(self, context):
        """Record one requested restoration without any external mutation."""
        self._record("restore")
        self.restored.append(context)


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


def test_complete_fixed_sequence_retains_actual_bytes_ids_and_final_restore(
    ops, fixtures
):
    result = run_npm_suite(PLAN, ops)

    assert ops.events == list(EXPECTED_EVENTS)
    assert result.probes == tuple(ops.probes[label] for label in PROBE_LABELS)
    assert tuple(item.run_id for item in result.probes) == tuple(
        range(1001, 1009)
    )
    assert result.captures == tuple(
        ops.captures[label] for label in CAPTURE_LABELS
    )
    assert all(capture.files for capture in result.captures)
    assert result.original_deletion == CONTEXT
    assert ops.deleted == [(CONTROL, D)]
    assert ops.restored == [CONTEXT]
    assert ops.capture_arguments == [(PLAN, None)] * 7 + [(PLAN, CONTEXT)] * 4
    assert [request.fixture.variant for request in ops.requests] == [
        "original",
        "original",
        "different",
        "original",
        "original",
        "original",
        "original",
        "different",
    ]
    final = result.captures[-1]
    assert final.active_inventory == (UNRELATED, A, W, V, D)
    assert final.state.tombstone == TombstoneState((OLD_DELETED,), D, None)
    assert fixtures[D.name, "original"].content in final.state.contents
    assert dict(final.state.tags) == {
        **BASE_TAGS,
        TAG_A: A.name,
        TAG_RACE: V.name,
    }
    for request in (PLAN.creation, PLAN.deleted_original):
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


def test_candidate_success_without_creation_stops_before_d(ops):
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
        ("after-create-d", D.name),
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
    assert ops.deleted == []


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
    assert ops.deleted == []


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
        ("duplicate-d-identical", "definitive-success"),
        ("duplicate-d-identical", "ambiguous"),
        ("duplicate-d-different", "definitive-success"),
        ("duplicate-d-different", "ambiguous"),
        ("candidate-v", "ambiguous"),
        ("create-a", "not-initiated"),
        ("create-w", "definitive-non-success"),
        ("create-d", "definitive-non-success"),
    ],
)
def test_unacceptable_process_stops_all_later_mutation(
    ops, label, classification
):
    _process(ops, label, classification)
    with pytest.raises(ValueError, match="process classification"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "probe:" + label)
    assert ops.restored == []


@pytest.mark.parametrize(
    "label",
    [
        "after-duplicate-a-identical",
        "after-duplicate-a-different",
        "after-duplicate-d-identical",
        "after-duplicate-d-different",
    ],
)
def test_duplicate_semantic_delta_stops_before_next_probe_or_restore(
    ops, label
):
    tags = dict(ops.captures[label].state.tags)
    tags["latest"] = A.name
    _state(ops, label, tags=tuple(sorted(tags.items())))
    with pytest.raises(ValueError, match="delta changed: tags"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)
    assert ops.restored == []


@pytest.mark.parametrize(
    ("label", "wrong_variant"),
    [
        ("duplicate-a-identical", "different"),
        ("duplicate-a-different", "original"),
        ("duplicate-d-identical", "different"),
        ("duplicate-d-different", "original"),
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
        item for item in (A, W, V, D) if item.name == requested.fixture.version
    )
    ops.captures["initial"] = _capture(
        "initial", fixtures, (UNRELATED, identity), BASE_TAGS
    )
    with pytest.raises(ValueError, match="absent scenario versions and tags"):
        run_npm_suite(PLAN, ops)
    assert ops.events == ["capture:initial"]


@pytest.mark.parametrize("tag", [TAG_A, TAG_RACE, TAG_D])
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
        ("deleted_original", {"target": "a" * 40}),
        ("creation", {"target": "b" * 40}),
        ("deleted_original", {"generation": "another-generation"}),
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
    assert ops.deleted == []
    assert ops.restored == []


@pytest.mark.parametrize("tag", ["latest", "stable", "dangling"])
def test_delete_side_effect_on_unrelated_tag_stops_deleted_probes(ops, tag):
    tags = dict(ops.captures["after-delete-d"].state.tags)
    del tags[tag]
    _state(ops, "after-delete-d", tags=tuple(sorted(tags.items())))
    with pytest.raises(ValueError, match="delta changed: tags"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:after-delete-d")
    assert ops.deleted == [(CONTROL, D)]
    assert ops.restored == []


@pytest.mark.parametrize("damage", ["unrelated-version", "original-a-content"])
def test_delete_must_preserve_other_versions_and_content(ops, fixtures, damage):
    label = "after-delete-d"
    captured = ops.captures[label]
    if damage == "unrelated-version":
        _state(
            ops,
            label,
            active_versions=tuple(
                name
                for name in captured.state.active_versions
                if name != UNRELATED.name
            ),
        )
        ops.captures[label] = replace(
            ops.captures[label],
            active_inventory=(A, W, V),
        )
    else:
        _state(
            ops,
            label,
            contents=tuple(
                fixtures[A.name, "different"].content
                if item.version == A.name
                else item
                for item in captured.state.contents
            ),
        )
    with pytest.raises(ValueError, match="delta changed:"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)
    assert ops.restored == []


@pytest.mark.parametrize("tag_target", [D.name, A.name])
def test_delete_allows_actual_scenario_tag_readback_without_repair(
    ops, tag_target
):
    for label in CAPTURE_LABELS[7:]:
        tags = dict(ops.captures[label].state.tags)
        tags[TAG_D] = tag_target
        _state(ops, label, tags=tuple(sorted(tags.items())))

    result = run_npm_suite(PLAN, ops)

    assert ops.events == list(EXPECTED_EVENTS)
    assert dict(result.captures[-1].state.tags)[TAG_D] == tag_target
    assert ops.restored == [CONTEXT]


def test_delete_success_without_deleted_readback_stops(ops):
    before = ops.captures["after-create-d"]
    tombstone = TombstoneState((OLD_DELETED,), D, None)
    ops.captures["after-delete-d"] = replace(
        ops.captures["after-delete-d"],
        state=replace(before.state, tombstone=tombstone),
        active_inventory=before.active_inventory,
    )
    with pytest.raises(ValueError, match="deleted/restorable"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:after-delete-d")


def test_delete_context_must_bind_exact_captured_original_id(ops):
    ops.context = replace(
        CONTEXT, original_version=VersionIdentity(405, D.name)
    )
    with pytest.raises(ValueError, match="captured original control and ID"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "delete")
    assert ops.deleted == [(CONTROL, D)]


def test_first_deleted_capture_cannot_refresh_original_deletion_time(ops):
    label = "after-duplicate-d-identical"
    observed = ops.captures[label]
    evidence = RestorabilityEvidence(
        CONTROL,
        D,
        CONTEXT.deletion_lower_bound_at + timedelta(seconds=1),
        observed.captured_at,
    )
    _state(
        ops,
        label,
        tombstone=TombstoneState((OLD_DELETED, D), D, evidence),
    )
    with pytest.raises(ValueError, match="original deletion time"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)
    assert ops.restored == []


def test_first_deleted_empty_delta_includes_unrelated_deleted_inventory(ops):
    label = "after-duplicate-d-identical"
    captured = ops.captures[label]
    tombstone = captured.state.tombstone
    assert tombstone is not None
    _state(
        ops,
        label,
        tombstone=replace(tombstone, deleted_versions=(D,)),
    )
    with pytest.raises(ValueError, match="delta changed: tombstone"):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)
    assert "probe:duplicate-d-different" not in ops.events
    assert ops.restored == []


def test_premature_restoration_blocks_second_deleted_probe(ops):
    label = "after-duplicate-d-identical"
    restored = ops.captures["after-restore-d"]
    ops.captures[label] = replace(
        ops.captures[label],
        state=restored.state,
        active_inventory=restored.active_inventory,
    )
    with pytest.raises(
        ValueError, match="documented-restorable deleted target"
    ):
        run_npm_suite(PLAN, ops)
    _stop_at(ops, "capture:" + label)
    assert ops.restored == []


@pytest.mark.parametrize(
    "damage", ["wrong-id", "wrong-content", "still-deleted"]
)
def test_restore_readback_rejects_wrong_original_without_second_restore(
    ops, fixtures, damage
):
    label = "after-restore-d"
    captured = ops.captures[label]
    if damage == "wrong-id":
        wrong_id = VersionIdentity(405, D.name)
        _state(
            ops, label, tombstone=TombstoneState((OLD_DELETED,), wrong_id, None)
        )
        ops.captures[label] = replace(
            ops.captures[label],
            active_inventory=(UNRELATED, A, W, V, wrong_id),
        )
    elif damage == "wrong-content":
        _state(
            ops,
            label,
            contents=tuple(
                fixtures[D.name, "different"].content
                if item.version == D.name
                else item
                for item in captured.state.contents
            ),
        )
    else:
        ops.captures[label] = ops.captures["after-duplicate-d-different"]

    with pytest.raises(ValueError, match=r"identity mismatch|delta changed"):
        run_npm_suite(PLAN, ops)
    assert ops.events == list(EXPECTED_EVENTS)
    assert ops.restored == [CONTEXT]


@pytest.mark.parametrize(
    "event",
    [
        "probe:duplicate-a-identical",
        "delete",
        "capture:after-delete-d",
        "capture:after-duplicate-d-identical",
        "restore",
        "capture:after-restore-d",
    ],
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
    assert ops.events.count("restore") <= 1


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
