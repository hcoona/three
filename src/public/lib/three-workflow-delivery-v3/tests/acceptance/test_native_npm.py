"""Synthetic WD-OPS-002A state scenarios, never native acceptance or approval.

No publish/restore API or process outcome is simulated here. These tests pin
the pure comparison boundary; collectors must separately prove actual bytes,
complete inventories, definitive process outcomes and authorized provenance.
"""

from __future__ import annotations

import hashlib
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from typing import TYPE_CHECKING

import pytest
from three_workflow_delivery_v3.acceptance import native_npm as npm
from three_workflow_delivery_v3.canonical import canonicalize

if TYPE_CHECKING:
    from three_workflow_delivery_v3.canonical import JsonValue

# Literal counts are independent scenario expectations, not computed oracles.
# ruff: noqa: PLR2004

PACKAGE = "@hcoona/native-fixture"
TAG = "synthetic-scenario"
V_BYTES = b"Synthetic V package content; not a publishable tarball.\n"
W_BYTES = b"Synthetic W package content; not a publishable tarball.\n"
DELETED_AT = datetime(2026, 8, 1, 12, tzinfo=UTC)


def _control():
    return npm.PackageControl(
        container_id=101,
        full_scoped_name=PACKAGE,
        owner="hcoona",
        visibility="public",
        repository_full_name="hcoona/three",
        exposed_access=("synthetic-actions:write",),
    )


def _sample(version, payload, target):
    """Model observed facts from local sample bytes, not remote evidence."""
    return npm.ObservedContent(
        version=version,
        sha256="sha256:" + hashlib.sha256(payload).hexdigest(),
        sha512="sha512:" + hashlib.sha512(payload).hexdigest(),
        witness=canonicalize(
            {
                "package": PACKAGE,
                "synthetic": True,
                "target": target,
                "version": version,
            }
        ),
        target=target,
    )


def _expected_v():
    """Keep the expected witness literal separate from observed projection."""
    return npm.ObservedContent(
        version="2.0.0",
        sha256="sha256:" + hashlib.sha256(V_BYTES).hexdigest(),
        sha512="sha512:" + hashlib.sha512(V_BYTES).hexdigest(),
        witness=(
            b'{"package":"@hcoona/native-fixture","synthetic":true,'
            b'"target":"synthetic-v","version":"2.0.0"}'
        ),
        target="synthetic-v",
    )


def _old_contents():
    return (
        _sample("1.0.0", b"Unrelated original package.\n", "synthetic-old"),
        _sample("1.5.0", b"Unrelated latest package.\n", "synthetic-latest"),
    )


def _baseline():
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0"),
        tags=(("latest", "1.5.0"), ("stable", "1.0.0")),
        contents=_old_contents(),
    )


def _creation_readback():
    """Construct an observed snapshot without the comparator's add helper."""
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0", "2.0.0"),
        tags=(
            ("latest", "1.5.0"),
            ("stable", "1.0.0"),
            (TAG, "2.0.0"),
        ),
        contents=(*_old_contents(), _sample("2.0.0", V_BYTES, "synthetic-v")),
    )


def test_fresh_creation_has_only_exact_version_and_declared_tag():
    """Admit V while retaining both old versions, controls and routing."""
    before = _baseline()
    after = _creation_readback()
    expected = _expected_v()

    assert npm.require_creation_delta(before, after, expected, TAG) is None
    assert after.active_versions == ("1.0.0", "1.5.0", "2.0.0")
    assert dict(after.tags) == {
        "latest": "1.5.0",
        "stable": "1.0.0",
        TAG: "2.0.0",
    }
    assert after.contents[-1] == expected
    assert after.contents[-1] is not expected
    assert after.contents[:2] == before.contents
    assert after.control == before.control


def test_creation_rejects_substituted_unrelated_version_or_content():
    """Correct V cannot hide changed unrelated inventory or original bytes."""
    after = replace(
        _creation_readback(),
        active_versions=("1.0.0", "1.4.0", "2.0.0"),
        contents=(_old_contents()[0], _expected_v()),
    )

    with pytest.raises(
        ValueError,
        match=r"^acceptance delta changed: active_versions, contents$",
    ):
        npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)

    complete = _creation_readback()
    altered_original = replace(
        complete,
        contents=(
            _sample("1.0.0", b"Changed unrelated bytes", "synthetic-old"),
            *complete.contents[1:],
        ),
    )
    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: contents$"
    ):
        npm.require_creation_delta(
            _baseline(), altered_original, _expected_v(), TAG
        )


def test_creation_rejects_latest_and_unrelated_tag_changes():
    """Neither latest nor the unrelated stable tag belongs to the delta."""
    for changed_tag in ("latest", "stable"):
        tags = dict(_creation_readback().tags)
        tags[changed_tag] = "2.0.0"
        after = replace(_creation_readback(), tags=tuple(sorted(tags.items())))

        with pytest.raises(
            ValueError, match=r"^acceptance delta changed: tags$"
        ):
            npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_changed_observed_package_controls():
    """All exposed identity, ownership and access facts remain authoritative."""
    for control in (
        replace(_control(), container_id=102),
        replace(_control(), full_scoped_name="@hcoona/other-synthetic-fixture"),
        replace(_control(), owner="synthetic-other"),
        replace(_control(), visibility="private"),
        replace(_control(), repository_full_name="hcoona/synthetic-other"),
        replace(_control(), exposed_access=()),
    ):
        after = replace(_creation_readback(), control=control)
        with pytest.raises(
            ValueError, match=r"^acceptance delta changed: control$"
        ):
            npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_sha256_mismatch_even_with_exact_witness_and_sha512():
    """An independently computed changed-byte SHA256 cannot be ignored."""
    wrong = replace(
        _expected_v(),
        sha256="sha256:" + hashlib.sha256(V_BYTES + b"changed").hexdigest(),
    )
    after = replace(_creation_readback(), contents=(*_old_contents(), wrong))

    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: contents$"
    ):
        npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_sha512_mismatch_even_with_exact_witness_and_sha256():
    """SHA512 is an independent content fact, not redundant decoration."""
    wrong = replace(
        _expected_v(),
        sha512="sha512:" + hashlib.sha512(V_BYTES + b"changed").hexdigest(),
    )
    after = replace(_creation_readback(), contents=(*_old_contents(), wrong))

    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: contents$"
    ):
        npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_witness_mismatch_even_when_both_digests_match():
    """A canonical but substituted witness must not be accepted as exact."""
    wrong = replace(
        _expected_v(), witness=b'{"synthetic":true,"target":"different"}'
    )
    after = replace(_creation_readback(), contents=(*_old_contents(), wrong))

    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: contents$"
    ):
        npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_target_mismatch_even_when_witness_matches():
    """The projected target cannot silently diverge from the expected target."""
    wrong = replace(_expected_v(), target="synthetic-other-target")
    after = replace(_creation_readback(), contents=(*_old_contents(), wrong))

    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: contents$"
    ):
        npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_rejects_missing_expected_version_tag_or_content():
    """Partial readback is not a successful fresh-creation delta."""
    complete = _creation_readback()
    for after, fields in (
        (
            replace(
                complete,
                active_versions=("1.0.0", "1.5.0"),
                contents=_old_contents(),
            ),
            "active_versions, active_version_count, contents",
        ),
        (replace(complete, tags=_baseline().tags), "tags"),
        (replace(complete, contents=_old_contents()), "contents"),
    ):
        with pytest.raises(
            ValueError, match=rf"^acceptance delta changed: {fields}$"
        ):
            npm.require_creation_delta(_baseline(), after, _expected_v(), TAG)


def test_creation_requires_fresh_version_and_fresh_non_latest_tag():
    """Creation cannot relabel an active version or reuse existing routing."""
    for tag, expected_message in (
        ("latest", "scenario tag must not be latest"),
        ("stable", "creation tag already present"),
    ):
        with pytest.raises(ValueError, match=expected_message):
            npm.require_creation_delta(
                _baseline(), _creation_readback(), _expected_v(), tag
            )
    with pytest.raises(ValueError, match="candidate already active"):
        npm.require_creation_delta(
            _creation_readback(), _creation_readback(), _expected_v(), "new"
        )


def test_active_duplicate_attempts_leave_an_entire_empty_delta():
    """Identical then differing proposals leave the original ACTIVE snapshot.

    The proposed bytes are not comparison operands. Actual npm rejection
    and sequential invocation are responsibilities of the native runner.
    """
    before = _creation_readback()
    identical_proposal = _sample("2.0.0", V_BYTES, "synthetic-v")
    differing_proposal = _sample("2.0.0", V_BYTES + b"different", "synthetic-v")
    assert identical_proposal == _expected_v()
    assert differing_proposal.sha256 != _expected_v().sha256
    assert differing_proposal.sha512 != _expected_v().sha512

    after_identical = _creation_readback()
    assert (
        npm.require_active_duplicate_delta(
            before, after_identical, _expected_v()
        )
        is None
    )
    after_differing = _creation_readback()
    assert (
        npm.require_active_duplicate_delta(
            after_identical, after_differing, _expected_v()
        )
        is None
    )
    assert after_differing.contents[-1] == _expected_v()
    assert after_differing.digest() == before.digest()

    overwritten = replace(
        after_differing, contents=(*_old_contents(), differing_proposal)
    )
    with pytest.raises(ValueError, match="requires exact original content"):
        npm.require_active_duplicate_delta(before, overwritten, _expected_v())


def test_active_duplicate_requires_original_content_even_without_a_delta():
    """Identical inventories cannot replace actual original content evidence."""
    before = replace(_creation_readback(), contents=_old_contents())
    after = replace(_creation_readback(), contents=_old_contents())
    assert before.digest() == after.digest()
    with pytest.raises(ValueError, match="requires exact original content"):
        npm.require_active_duplicate_delta(before, after, _expected_v())


def test_active_duplicate_rejects_each_changed_state_section():
    """An empty delta means whole-state equality, not just V equality."""
    before = _creation_readback()
    for after, fields in (
        (
            replace(before, active_versions=(*before.active_versions, "9.0.0")),
            "active_versions, active_version_count",
        ),
        (replace(before, tags=before.tags[:-1]), "tags"),
        (
            replace(before, control=replace(_control(), visibility="private")),
            "control",
        ),
        (
            replace(
                before,
                contents=(
                    _sample(
                        "1.0.0", b"Changed unrelated bytes", "synthetic-old"
                    ),
                    *before.contents[1:],
                ),
            ),
            "contents",
        ),
    ):
        with pytest.raises(
            ValueError, match=rf"^acceptance delta changed: {fields}$"
        ):
            npm.require_active_duplicate_delta(before, after, _expected_v())


def _race_baseline():
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0", "3.0.0"),
        tags=(("latest", "1.5.0"), ("stable", "1.0.0"), (TAG, "3.0.0")),
        contents=(*_old_contents(), _sample("3.0.0", W_BYTES, "synthetic-w")),
    )


def _race_readback(tag_target):
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0", "2.0.0", "3.0.0"),
        tags=(("latest", "1.5.0"), ("stable", "1.0.0"), (TAG, tag_target)),
        contents=(
            *_old_contents(),
            _sample("2.0.0", V_BYTES, "synthetic-v"),
            _sample("3.0.0", W_BYTES, "synthetic-w"),
        ),
    )


@pytest.mark.parametrize("tag_target", ["2.0.0", "3.0.0"], ids=["at-v", "at-w"])
def test_tag_race_accepts_either_route_only_with_both_versions_exact(
    tag_target,
):
    """T is routing only; V and the competing W must both remain exact."""
    before = _race_baseline()
    after = _race_readback(tag_target)
    known_w = _sample("3.0.0", W_BYTES, "synthetic-w")

    assert (
        npm.require_tag_race_delta(before, after, _expected_v(), known_w, TAG)
        is None
    )
    assert after.contents[2:] == (_expected_v(), known_w)
    assert after.contents[:2] == before.contents[:2]
    assert dict(after.tags) == {
        "latest": "1.5.0",
        "stable": "1.0.0",
        TAG: tag_target,
    }
    assert after.control == before.control


def test_tag_race_no_mutation_failure_preserves_the_complete_baseline():
    """A no-mutation failure safely leaves V absent and W exact.

    This is not a process-outcome test: the caller must also keep any current
    failure failed even when a later readback happens to contain exact V.
    """
    before = _race_baseline()
    assert (
        npm.require_tag_race_delta(
            before,
            _race_baseline(),
            _expected_v(),
            _sample("3.0.0", W_BYTES, "synthetic-w"),
            TAG,
        )
        is None
    )
    assert _expected_v().version not in before.active_versions
    assert dict(before.tags)[TAG] == "3.0.0"
    changed = replace(
        before, control=replace(before.control, visibility="private")
    )
    with pytest.raises(ValueError, match="acceptance delta changed: control"):
        npm.require_tag_race_delta(
            before,
            changed,
            _expected_v(),
            _sample("3.0.0", W_BYTES, "synthetic-w"),
            TAG,
        )


def test_tag_race_rejects_v_or_w_tampering_and_unrelated_or_missing_routing():
    """Reject damaged V/W, unrelated tags, and a third T target."""
    complete = _race_readback("2.0.0")
    wrong_v = _sample("2.0.0", V_BYTES + b"tampered", "synthetic-v")
    wrong_w = _sample("3.0.0", W_BYTES + b"tampered", "synthetic-w")
    for after, message in (
        (
            replace(
                complete,
                contents=(*_old_contents(), wrong_v, complete.contents[-1]),
            ),
            "^acceptance delta changed: contents$",
        ),
        (
            replace(complete, contents=(*complete.contents[:-1], wrong_w)),
            "^acceptance delta changed: contents$",
        ),
        (
            replace(
                complete,
                tags=(("latest", "1.5.0"), ("stable", "2.0.0"), (TAG, "2.0.0")),
            ),
            "^acceptance delta changed: tags$",
        ),
        (_race_readback("1.0.0"), "race tag must resolve to V or W"),
        (replace(complete, tags=_baseline().tags), "race tag must resolve"),
    ):
        with pytest.raises(ValueError, match=message):
            npm.require_tag_race_delta(
                _race_baseline(),
                after,
                _expected_v(),
                _sample("3.0.0", W_BYTES, "synthetic-w"),
                TAG,
            )


def test_tag_race_requires_exact_known_w_and_its_existing_tag():
    """The race baseline must already contain exact W and T -> W."""
    baseline = _race_baseline()
    for before, known_w in (
        (replace(baseline, tags=_baseline().tags), baseline.contents[-1]),
        (replace(baseline, contents=_old_contents()), baseline.contents[-1]),
        (
            baseline,
            _sample("3.0.0", W_BYTES + b"wrong-expected", "synthetic-w"),
        ),
    ):
        with pytest.raises(ValueError, match="race baseline requires exact W"):
            npm.require_tag_race_delta(
                before, _race_readback("2.0.0"), _expected_v(), known_w, TAG
            )


def _deleted_snapshot(inspected_at=DELETED_AT + timedelta(days=1)):
    target = npm.VersionIdentity(202, "2.0.0")
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0"),
        tags=(("latest", "1.5.0"), ("stable", "1.0.0"), (TAG, "2.0.0")),
        contents=_old_contents(),
        tombstone=npm.TombstoneState(
            deleted_versions=(npm.VersionIdentity(71, "0.8.0"), target),
            target=target,
            restorability=npm.RestorabilityEvidence(
                original_control=_control(),
                original_version=target,
                deletion_observed_at=DELETED_AT,
                inspected_at=inspected_at,
            ),
        ),
    )


def _restored_snapshot(tag_target="2.0.0"):
    tags = [("latest", "1.5.0"), ("stable", "1.0.0")]
    if tag_target is not None:
        tags.append((TAG, tag_target))
    return npm.AcceptanceState(
        control=_control(),
        active_versions=("1.0.0", "1.5.0", "2.0.0"),
        tags=tuple(tags),
        contents=(*_old_contents(), _sample("2.0.0", V_BYTES, "synthetic-v")),
        tombstone=npm.TombstoneState(
            deleted_versions=(npm.VersionIdentity(71, "0.8.0"),),
            target=npm.VersionIdentity(202, "2.0.0"),
            restorability=None,
        ),
    )


def test_deleted_duplicate_sequence_preserves_state_with_fresh_inspections():
    """Both sequential duplicate readbacks preserve the original tombstone.

    Identical/differing invocation and definitive rejection are outside this
    pure module; each returned snapshot must independently pass this gate.
    """
    before = _deleted_snapshot()
    after_identical = _deleted_snapshot(DELETED_AT + timedelta(days=2))
    after_differing = _deleted_snapshot(DELETED_AT + timedelta(days=29))

    assert npm.require_deleted_duplicate_delta(before, after_identical) is None
    assert (
        npm.require_deleted_duplicate_delta(after_identical, after_differing)
        is None
    )
    assert after_differing.digest() == before.digest()
    assert after_differing.to_document() == before.to_document()
    assert after_differing.tombstone is not None
    assert after_differing.tombstone.target == npm.VersionIdentity(202, "2.0.0")
    assert after_differing.tombstone.deleted_versions == (
        npm.VersionIdentity(71, "0.8.0"),
        npm.VersionIdentity(202, "2.0.0"),
    )
    assert after_differing.tombstone.restorability is not None
    assert after_differing.tombstone.restorability.inspected_at == (
        DELETED_AT + timedelta(days=29)
    )


def test_deleted_duplicate_rejects_changed_target_id():
    """The same target name cannot hide replacement of its original ID."""
    before = _deleted_snapshot()
    tombstone = before.tombstone
    assert tombstone is not None
    assert tombstone.restorability is not None
    new_target = npm.VersionIdentity(203, "2.0.0")
    changed = replace(
        tombstone,
        target=new_target,
        deleted_versions=(npm.VersionIdentity(71, "0.8.0"), new_target),
        restorability=replace(
            tombstone.restorability, original_version=new_target
        ),
    )
    after = replace(_deleted_snapshot(), tombstone=changed)
    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: tombstone$"
    ):
        npm.require_deleted_duplicate_delta(before, after)


def test_deleted_duplicate_cannot_reset_the_original_restoration_window():
    """A later deletion timestamp must not renew the same object's window."""
    before = _deleted_snapshot()
    tombstone = before.tombstone
    assert tombstone is not None
    assert tombstone.restorability is not None
    after = replace(
        before,
        tombstone=replace(
            tombstone,
            restorability=replace(
                tombstone.restorability,
                deletion_observed_at=DELETED_AT + timedelta(days=31),
                inspected_at=DELETED_AT + timedelta(days=32),
            ),
        ),
    )
    assert after.digest() == before.digest()
    with pytest.raises(ValueError, match="changed the original deletion time"):
        npm.require_deleted_duplicate_delta(before, after)


def test_deleted_duplicate_rejects_changed_complete_deleted_inventory():
    """An intact target cannot excuse losing or replacing another deletion."""
    before = _deleted_snapshot()
    tombstone = before.tombstone
    assert tombstone is not None
    for changed in (
        replace(tombstone, deleted_versions=(tombstone.target,)),
        replace(
            tombstone,
            deleted_versions=(
                npm.VersionIdentity(72, "0.8.0"),
                tombstone.target,
            ),
        ),
    ):
        after = replace(_deleted_snapshot(), tombstone=changed)
        with pytest.raises(
            ValueError, match=r"^acceptance delta changed: tombstone$"
        ):
            npm.require_deleted_duplicate_delta(before, after)


def test_deleted_duplicate_rejects_active_tag_and_control_changes():
    """Hidden-tombstone rejection must leave the visible package unchanged."""
    before = _deleted_snapshot()
    assert before.tombstone is not None
    assert before.tombstone.restorability is not None
    changed_control = replace(_control(), visibility="private")
    control_changed = replace(
        before,
        control=changed_control,
        tombstone=replace(
            before.tombstone,
            restorability=replace(
                before.tombstone.restorability,
                original_control=changed_control,
            ),
        ),
    )
    for after, fields in (
        (
            replace(before, active_versions=("1.0.0", "1.5.0", "9.0.0")),
            "active_versions, active_version_count",
        ),
        (replace(before, tags=_baseline().tags), "tags"),
        (control_changed, "control"),
        (replace(before, contents=()), "contents"),
    ):
        with pytest.raises(
            ValueError, match=rf"^acceptance delta changed: {fields}$"
        ):
            npm.require_deleted_duplicate_delta(before, after)


def test_deleted_duplicate_rejects_missing_or_lost_restorability():
    """Missing evidence, a restored object, and namespace drift fail closed."""
    deleted = _deleted_snapshot()
    assert deleted.tombstone is not None
    for invalid in (_baseline(), _restored_snapshot()):
        for before, after in ((deleted, invalid), (invalid, deleted)):
            with pytest.raises(
                ValueError, match="requires a documented-restorable deleted"
            ):
                npm.require_deleted_duplicate_delta(before, after)
    with pytest.raises(
        ValueError,
        match="restored target must be absent from deleted inventory",
    ):
        replace(deleted.tombstone, restorability=None)
    with pytest.raises(ValueError, match="original deletion namespace"):
        replace(deleted, control=replace(_control(), container_id=999))


@pytest.mark.parametrize(
    "tag_target", ["2.0.0", "1.0.0", None], ids=["unchanged", "moved", "absent"]
)
def test_restore_original_object_allows_only_scenario_tag_variation(tag_target):
    """Read the original object without inventing an API tag-restore promise."""
    before = _deleted_snapshot()
    after = _restored_snapshot(tag_target)

    assert (
        npm.require_restoration_delta(before, after, _expected_v(), TAG) is None
    )
    assert after.tombstone is not None
    assert after.tombstone.target == npm.VersionIdentity(202, "2.0.0")
    assert after.tombstone.deleted_versions == (
        npm.VersionIdentity(71, "0.8.0"),
    )
    assert after.tombstone.restorability is None
    assert after.contents[-1] == _expected_v()
    assert after.contents[:2] == before.contents
    assert after.active_versions == ("1.0.0", "1.5.0", "2.0.0")
    assert dict(after.tags).get(TAG) == tag_target
    assert dict(after.tags)["latest"] == "1.5.0"
    assert dict(after.tags)["stable"] == "1.0.0"
    assert after.control == before.control


def test_restoration_rejects_newly_allocated_version_id():
    """Republishing identical bytes under the same name is not restoration."""
    after = _restored_snapshot()
    assert after.tombstone is not None
    after = replace(
        after,
        tombstone=replace(
            after.tombstone, target=npm.VersionIdentity(999, "2.0.0")
        ),
    )

    with pytest.raises(
        ValueError, match=r"^acceptance delta changed: tombstone$"
    ):
        npm.require_restoration_delta(
            _deleted_snapshot(), after, _expected_v(), TAG
        )


def test_restoration_rejects_wrong_original_bytes_or_witness():
    """Matching restored ID cannot excuse differing byte or witness readback."""
    changed_bytes = _sample("2.0.0", V_BYTES + b"wrong-restored", "synthetic-v")
    changed_witness = replace(
        _expected_v(), witness=b'{"synthetic":true,"target":"not-original"}'
    )
    for wrong in (changed_bytes, changed_witness):
        after = replace(
            _restored_snapshot(), contents=(*_old_contents(), wrong)
        )
        with pytest.raises(
            ValueError, match=r"^acceptance delta changed: contents$"
        ):
            npm.require_restoration_delta(
                _deleted_snapshot(), after, _expected_v(), TAG
            )


def test_restoration_rejects_unrelated_tag_version_and_control_deltas():
    """Only the target leaves deleted state; latest and other facts stay put."""
    complete = _restored_snapshot()
    assert complete.tombstone is not None
    for after, fields in (
        (
            replace(
                complete,
                tags=(("latest", "2.0.0"), ("stable", "1.0.0"), (TAG, "2.0.0")),
            ),
            "tags",
        ),
        (
            replace(
                complete,
                tags=(("latest", "1.5.0"), ("stable", "2.0.0"), (TAG, "2.0.0")),
            ),
            "tags",
        ),
        (
            replace(
                complete, active_versions=(*complete.active_versions, "9.0.0")
            ),
            "active_versions, active_version_count",
        ),
        (
            replace(
                complete,
                tombstone=replace(complete.tombstone, deleted_versions=()),
            ),
            "tombstone",
        ),
        (
            replace(
                complete, control=replace(_control(), visibility="private")
            ),
            "control",
        ),
    ):
        with pytest.raises(
            ValueError, match=rf"^acceptance delta changed: {fields}$"
        ):
            npm.require_restoration_delta(
                _deleted_snapshot(), after, _expected_v(), TAG
            )


def test_restoration_requires_deleted_target_and_matching_expected_version():
    """An active baseline or unrelated expected fixture is not a restore."""
    with pytest.raises(ValueError, match="documented-restorable deleted"):
        npm.require_restoration_delta(
            _baseline(), _restored_snapshot(), _expected_v(), TAG
        )
    with pytest.raises(ValueError, match="must identify the targeted deleted"):
        npm.require_restoration_delta(
            _deleted_snapshot(),
            _restored_snapshot(),
            _sample("3.0.0", W_BYTES, "synthetic-w"),
            TAG,
        )


def test_canonical_state_has_a_closed_deterministic_semantic_shape():
    """Hash the complete explicit document, not raw times or caller counters."""
    expected: dict[str, JsonValue] = {
        "schema": "workflow-delivery-v3/native-npm-state/v1",
        "control": {
            "container_id": 101,
            "full_scoped_name": "@hcoona/native-fixture",
            "owner": "hcoona",
            "visibility": "public",
            "repository_full_name": "hcoona/three",
            "exposed_access": ["synthetic-actions:write"],
        },
        "active_versions": ["1.0.0", "1.5.0"],
        "active_version_count": 2,
        "tags": {
            "latest": "1.5.0",
            "stable": "1.0.0",
            TAG: "2.0.0",
        },
        "contents": [
            {
                "version": "1.0.0",
                "sha256": "sha256:"
                + hashlib.sha256(b"Unrelated original package.\n").hexdigest(),
                "sha512": "sha512:"
                + hashlib.sha512(b"Unrelated original package.\n").hexdigest(),
                "witness": {
                    "package": "@hcoona/native-fixture",
                    "synthetic": True,
                    "target": "synthetic-old",
                    "version": "1.0.0",
                },
                "target": "synthetic-old",
            },
            {
                "version": "1.5.0",
                "sha256": "sha256:"
                + hashlib.sha256(b"Unrelated latest package.\n").hexdigest(),
                "sha512": "sha512:"
                + hashlib.sha512(b"Unrelated latest package.\n").hexdigest(),
                "witness": {
                    "package": "@hcoona/native-fixture",
                    "synthetic": True,
                    "target": "synthetic-latest",
                    "version": "1.5.0",
                },
                "target": "synthetic-latest",
            },
        ],
        "tombstone": {
            "deleted_versions": [
                {"version_id": 71, "name": "0.8.0"},
                {"version_id": 202, "name": "2.0.0"},
            ],
            "deleted_version_count": 2,
            "target": {"version_id": 202, "name": "2.0.0"},
            "restorability": "documented-30-day-inference",
        },
    }
    before = _deleted_snapshot()
    refreshed = _deleted_snapshot(DELETED_AT + timedelta(days=29))
    expected_digest = (
        "sha256:" + hashlib.sha256(canonicalize(expected)).hexdigest()
    )

    assert before.to_document() == expected
    assert refreshed.to_document() == expected
    assert before.digest() == expected_digest
    assert refreshed.digest() == expected_digest
    assert before.tombstone is not None
    assert refreshed.tombstone is not None
    assert before.tombstone.restorability != refreshed.tombstone.restorability
    assert _baseline().to_document() == {
        **expected,
        "tags": {"latest": "1.5.0", "stable": "1.0.0"},
        "tombstone": None,
    }


def test_active_and_deleted_counts_are_derived_from_their_own_inventories():
    """Creation adds one active; restoration moves one deleted to active."""
    empty = npm.AcceptanceState(_control(), (), (), ())
    assert empty.to_document()["active_version_count"] == 0
    assert _baseline().to_document()["active_version_count"] == 2
    assert _creation_readback().to_document()["active_version_count"] == 3
    deleted = _deleted_snapshot().to_document()
    restored = _restored_snapshot().to_document()
    deleted_tombstone = deleted["tombstone"]
    restored_tombstone = restored["tombstone"]
    assert isinstance(deleted_tombstone, dict)
    assert isinstance(restored_tombstone, dict)
    assert deleted["active_version_count"] == 2
    assert deleted_tombstone["deleted_version_count"] == 2
    assert restored["active_version_count"] == 3
    assert restored_tombstone["deleted_version_count"] == 1
    assert restored["tombstone"] == {
        "deleted_versions": [{"version_id": 71, "name": "0.8.0"}],
        "deleted_version_count": 1,
        "target": {"version_id": 202, "name": "2.0.0"},
        "restorability": None,
    }
    with pytest.raises(TypeError, match="active_version_count"):
        # A caller-supplied count must be rejected at runtime.
        npm.AcceptanceState(
            _control(),
            (),
            (),
            (),
            active_version_count=99,  # type: ignore[unexpected-keyword]
        )


def test_restorability_requires_aware_times_strictly_inside_original_window():
    """Age zero is valid; negative age and the exact 30-day boundary fail."""
    original = _deleted_snapshot(DELETED_AT)
    last_instant = _deleted_snapshot(
        DELETED_AT + timedelta(days=30) - timedelta(microseconds=1)
    )
    assert npm.require_deleted_duplicate_delta(original, last_instant) is None
    assert original.digest() == last_instant.digest()
    assert last_instant.tombstone is not None
    assert last_instant.tombstone.restorability is not None
    assert (
        last_instant.tombstone.restorability.deletion_observed_at == DELETED_AT
    )

    for invalid_time in (
        DELETED_AT - timedelta(microseconds=1),
        DELETED_AT + timedelta(days=30),
        DELETED_AT + timedelta(days=30, microseconds=1),
    ):
        with pytest.raises(
            ValueError, match="outside the documented restore window"
        ):
            _deleted_snapshot(invalid_time)
    assert original.tombstone is not None
    evidence = original.tombstone.restorability
    assert evidence is not None
    with pytest.raises(ValueError, match="times must be timezone-aware"):
        replace(evidence, deletion_observed_at=DELETED_AT.replace(tzinfo=None))
    with pytest.raises(ValueError, match="times must be timezone-aware"):
        replace(evidence, inspected_at=DELETED_AT.replace(tzinfo=None))


def test_core_records_reject_coerced_identity_and_noncanonical_content():
    """Reject core unsafe records rather than primitive permutations."""
    with pytest.raises(ValueError, match="invalid package container ID"):
        replace(_control(), container_id=True)
    with pytest.raises(ValueError, match="invalid version identity"):
        npm.VersionIdentity(version_id=True, name="2.0.0")
    with pytest.raises(ValueError, match="canonical prefixed lowercase hex"):
        replace(_expected_v(), sha256=_expected_v().sha256.upper())
    with pytest.raises(ValueError, match="witness must be immutable bytes"):
        # Mutable bytes deliberately exercise the runtime immutability guard.
        replace(
            _expected_v(),
            witness=bytearray(_expected_v().witness),  # type: ignore[bad-argument-type]
        )
    with pytest.raises(
        ValueError, match="witness must be a canonical JSON object"
    ):
        replace(_expected_v(), witness=b'{ "synthetic":true}')
    with pytest.raises(ValueError, match="missing observed package control"):
        # Serialized control is intentionally not a typed PackageControl.
        replace(
            _baseline(),
            control=_control().to_document(),  # type: ignore[bad-argument-type]
        )
    with pytest.raises(
        ValueError, match="comparison requires typed acceptance"
    ):
        # Serialized state must not bypass the typed comparison boundary.
        npm.empty_delta(
            _baseline().to_document(),  # type: ignore[bad-argument-type]
            _baseline(),
        )
    with pytest.raises(TypeError, match="request_id"):
        # Raw request metadata is deliberately outside the closed state shape.
        npm.AcceptanceState(
            _control(),
            (),
            (),
            (),
            request_id="excluded-raw-id",  # type: ignore[unexpected-keyword]
        )


def test_content_and_tombstone_membership_must_be_bound_to_actual_inventory():
    """Retain dangling tags but require content and target membership."""
    deleted = _deleted_snapshot()
    assert deleted.tombstone is not None
    assert deleted.tombstone.restorability is not None
    assert dict(deleted.tags)[TAG] == "2.0.0"
    assert "2.0.0" not in deleted.active_versions
    with pytest.raises(
        ValueError, match="contents must be sorted unique and observed active"
    ):
        replace(_baseline(), contents=(_expected_v(),))
    with pytest.raises(
        ValueError, match="active and deleted inventories overlap"
    ):
        replace(deleted, active_versions=("1.0.0", "1.5.0", "2.0.0"))
    with pytest.raises(
        ValueError, match="restored original target must be observed active"
    ):
        replace(
            _restored_snapshot(),
            active_versions=("1.0.0", "1.5.0"),
            contents=_old_contents(),
        )
    with pytest.raises(ValueError, match="bound restorability evidence"):
        replace(
            deleted.tombstone,
            deleted_versions=(npm.VersionIdentity(71, "0.8.0"),),
        )
    with pytest.raises(ValueError, match="bound restorability evidence"):
        replace(
            deleted.tombstone,
            restorability=replace(
                deleted.tombstone.restorability,
                original_version=npm.VersionIdentity(999, "2.0.0"),
            ),
        )
    restored = _restored_snapshot()
    assert restored.tombstone is not None
    for alias in (
        npm.VersionIdentity(202, "0.9.0"),
        npm.VersionIdentity(999, "2.0.0"),
    ):
        with pytest.raises(
            ValueError,
            match="restored target must be absent from deleted inventory",
        ):
            replace(restored.tombstone, deleted_versions=(alias,))


def test_inventories_require_immutable_sorted_unique_names_and_deleted_ids():
    """Reject core duplicate and mutable records before any delta comparison."""
    baseline = _baseline()
    for invalid_versions in (
        ["1.0.0", "1.5.0"],
        ("1.0.0", "1.0.0"),
        ("1.5.0", "1.0.0"),
    ):
        with pytest.raises(
            ValueError, match="active names must be sorted unique"
        ):
            # Include a mutable list to verify runtime inventory validation.
            replace(
                baseline,
                active_versions=invalid_versions,  # type: ignore[bad-argument-type]
            )
    with pytest.raises(ValueError, match="tags require sorted unique"):
        replace(baseline, tags=(("stable", "1.0.0"), ("stable", "1.5.0")))
    with pytest.raises(ValueError, match="contents must be sorted unique"):
        replace(baseline, contents=(baseline.contents[0], baseline.contents[0]))
    with pytest.raises(ValueError, match="access facts must be sorted unique"):
        replace(_control(), exposed_access=("duplicate", "duplicate"))
    tombstone = _deleted_snapshot().tombstone
    assert tombstone is not None
    for invalid_deleted in (
        (npm.VersionIdentity(71, "0.8.0"), npm.VersionIdentity(71, "2.0.0")),
        (npm.VersionIdentity(71, "2.0.0"), npm.VersionIdentity(202, "2.0.0")),
        tuple(reversed(tombstone.deleted_versions)),
    ):
        with pytest.raises(
            ValueError, match="sorted unique names and unique IDs"
        ):
            replace(tombstone, deleted_versions=invalid_deleted)
