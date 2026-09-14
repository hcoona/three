"""Original capture closure and state deltas with controlled service facts."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004
import hashlib
import json
from dataclasses import replace
from importlib import import_module

import pytest
from three_workflow_delivery_v3.acceptance.nuget_capture import (
    capture_nuget_state,
)
from three_workflow_delivery_v3.acceptance.nuget_evidence import (
    read_capture_evidence,
    require_creation_delta,
    require_unchanged_delta,
)
from three_workflow_delivery_v3.canonical import (
    canonicalize,
)

# Pytest supplies the runtime namespace for this hyphenated project path.
_fixtures = import_module(".test_nuget_capture", __package__)
ARCHIVE = _fixtures.ARCHIVE
FLAT = _fixtures.FLAT
NOW = _fixtures.NOW
PACKAGE = _fixtures.PACKAGE
PAGE = _fixtures.PAGE
TOKEN = _fixtures.TOKEN
VERSION = _fixtures.VERSION
WITNESS = _fixtures.WITNESS
_response = _fixtures._response  # noqa: SLF001
capture_request = _fixtures.capture_request
scenario = _fixtures.scenario

COORDINATE = PACKAGE.lower() + "@" + VERSION
NEIGHBOR = PACKAGE.lower() + "@1.0.0"


@pytest.fixture
def capture(tmp_path, capture_request, scenario):
    authority, transport, responses = scenario
    identities = {
        "1.0.0": "1.0.0",
        VERSION: VERSION,
        VERSION + "+build": VERSION,
    }
    authority.normalize_identity.side_effect = lambda package_id, version: {
        "displayPackageId": package_id,
        "displayVersion": version,
        "normalizedPackageId": PACKAGE.lower(),
        "normalizedVersion": identities[version],
    }

    def create(label, objects):
        request = replace(capture_request, label=label)
        responses[PAGE + "1"] = _response(
            PAGE + "1", [{"id": key, "name": value} for key, value in objects]
        )
        responses[FLAT] = _response(
            FLAT, {"versions": [identities[value] for _, value in objects]}
        )
        directory = tmp_path / label
        capture_nuget_state(
            request,
            authority=authority,
            transport=transport,
            token=TOKEN,
            audit_directory=directory,
            clock=lambda: NOW,
        )
        return read_capture_evidence(directory, request), directory, request

    return create


def test_creation_admits_only_the_new_native_object_and_original_bytes(capture):
    before, _, _ = capture("before-create", [(40, "1.0.0")])
    after, directory, _ = capture(
        "after-create", [(71, VERSION), (40, "1.0.0")]
    )
    assert (
        require_creation_delta(before, after, original=ARCHIVE, witness=WITNESS)
        == 71
    )
    assert before.objects == ((40, NEIGHBOR),)
    assert before.package is None
    assert after.objects == ((40, NEIGHBOR), (71, COORDINATE))
    assert after.package == ARCHIVE
    assert after.witness == WITNESS
    assert (
        after.capture_sha256
        == hashlib.sha256((directory / "capture.json").read_bytes()).hexdigest()
    )
    assert (directory / "response-005.body").read_bytes() == ARCHIVE


def test_duplicate_accepts_reordered_inventory_with_equivalent_display_name(
    capture,
):
    before, first, _ = capture(
        "before-identical", [(40, "1.0.0"), (71, VERSION)]
    )
    after, second, _ = capture(
        "after-identical", [(71, VERSION + "+build"), (40, "1.0.0")]
    )
    require_unchanged_delta(before, after, original=ARCHIVE, witness=WITNESS)
    assert before.objects == after.objects == ((40, NEIGHBOR), (71, COORDINATE))
    assert before.package == after.package == ARCHIVE
    assert (first / "response-003.body").read_bytes() != (
        second / "response-003.body"
    ).read_bytes()


@pytest.mark.parametrize("operation", ["create", "unchanged"])
@pytest.mark.parametrize(
    "change",
    [
        "neighbor-removed",
        "neighbor-replaced",
        "neighbor-coordinate",
        "extra-coordinate",
        "archive",
        "witness",
        "resource",
        "subject",
    ],
)
def test_delta_rejects_unexpected_changes(capture, operation, change):
    objects = [(40, "1.0.0")]
    if operation == "unchanged":
        objects.append((71, VERSION))
    before, _, _ = capture("before-create", objects)
    after, _, _ = capture("after-create", [(40, "1.0.0"), (71, VERSION)])
    if change == "neighbor-removed":
        after = replace(after, objects=((71, COORDINATE),))
    elif change == "neighbor-replaced":
        after = replace(after, objects=((41, NEIGHBOR), (71, COORDINATE)))
    elif change == "neighbor-coordinate":
        after = replace(
            after, objects=((40, "unexpected@1.0.0"), (71, COORDINATE))
        )
    elif change == "extra-coordinate":
        after = replace(
            after, objects=(*after.objects, (72, "unexpected@2.0.0"))
        )
    elif change == "archive":
        after = replace(after, package=ARCHIVE + b"changed")
    elif change == "witness":
        after = replace(after, witness=WITNESS + b" ")
    elif change == "resource":
        after = replace(
            after, resources={**after.resources, "packagePublish": "changed"}
        )
    else:
        after = replace(after, coordinate="unexpected@1.0.0")
    check = (
        require_creation_delta
        if operation == "create"
        else require_unchanged_delta
    )
    message = (
        "subject or resources"
        if change in {"resource", "subject"}
        else "creation changed"
        if operation == "create"
        else "declared active state"
    )
    with pytest.raises(ValueError, match=message):
        check(before, after, original=ARCHIVE, witness=WITNESS)


def test_duplicate_rejects_scenario_object_replacement(capture):
    before, _, _ = capture("before-identical", [(40, "1.0.0"), (71, VERSION)])
    after, _, _ = capture("after-identical", [(40, "1.0.0"), (72, VERSION)])
    with pytest.raises(ValueError, match="active state"):
        require_unchanged_delta(
            before, after, original=ARCHIVE, witness=WITNESS
        )
    assert before.package == after.package == ARCHIVE


@pytest.mark.parametrize(
    ("change", "message"),
    [
        ("body", "original bytes changed"),
        ("request", "request binding mismatch"),
        ("missing-response", "missing or unsafe"),
        ("partial-journal", "journal is incomplete"),
        ("duplicate-coordinate", "inventories disagree"),
        ("substituted-object", "inventories disagree"),
        ("missing-completion", "missing or unsafe"),
    ],
)
def test_capture_reader_rejects_incomplete_or_substituted_evidence(
    capture, change, message
):
    _, directory, request = capture(
        "after-create", [(40, "1.0.0"), (71, VERSION)]
    )
    document = json.loads((directory / "capture.json").read_bytes())
    if change == "body":
        (directory / "response-005.body").write_bytes(b"changed")
    elif change == "request":
        request = replace(request, label="after-identical")
    elif change == "missing-response":
        (directory / "response-005.body").unlink()
    elif change == "partial-journal":
        path = directory / "requests.jsonl"
        body = b"\n".join(path.read_bytes().splitlines()[:-1]) + b"\n"
        path.write_bytes(body)
        document["files"]["requests.jsonl"] = {
            "bytes": len(body),
            "sha256": hashlib.sha256(body).hexdigest(),
        }
    elif change == "duplicate-coordinate":
        document["githubCoordinates"][0]["coordinate"] = COORDINATE
    elif change == "substituted-object":
        document["githubCoordinates"][0]["id"] = 99
    else:
        (directory / "capture.json").unlink()
    if change != "missing-completion":
        (directory / "capture.json").write_bytes(canonicalize(document))
    with pytest.raises(ValueError, match=message):
        read_capture_evidence(directory, request)
