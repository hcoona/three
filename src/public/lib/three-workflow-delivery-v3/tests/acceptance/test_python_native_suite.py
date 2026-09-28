"""Original-revision historical suite readers; no new native execution."""

from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance.python_native_suite import (
    audit_suite,
    duplicate_response,
)
from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpResponse,
)
from three_workflow_delivery_v3.adapters.python import PythonConsumerResult
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

from . import test_python_native_capture as capture_tests
from . import test_python_native_fixture as fixture_tests
from .python_native_history import historical_files

modeled_fixtures = fixture_tests.modeled_fixtures
deny_real_registry = capture_tests.deny_real_registry
_TOKEN = "pypi-suite-synthetic-secret"  # noqa: S105 - synthetic local capability
_CONSUMERS = 4


def _consumer(calls):
    def consume(item):
        calls.append(item)
        return PythonConsumerResult(
            item.variant,
            item.digest,
            canonicalize(
                {
                    "version": item.witness.nbgv.pep440_version,
                    "project-id": "hcoona-release-smoke-python",
                    "witness": item.witness.to_document(),
                    "module": "/synthetic/fresh/consumer/module.py",
                }
            ),
            (
                canonicalize(
                    {
                        "argv": ["synthetic-consumer"],
                        "exit-code": 0,
                        "stdout": "",
                        "stderr": "",
                    }
                ),
            ),
            None,
        )

    return consume


def _archived_suite(fixtures):
    """Load supplied original history without executing native mutations."""
    files = historical_files("hosted")
    return fixture_tests.fixture_request(fixtures), {
        name.removeprefix("suite/"): content
        for name, content in files.items()
        if name.startswith("suite/")
    }


@pytest.mark.parametrize(
    "change",
    [
        "raw-pending",
        "timing",
        "missing-pending",
        "previous-partial",
        "terminal-after-next-file",
    ],
)
def test_native_delayed_audit_rejects_changed_observation_history(
    modeled_fixtures, change
):
    """Clean final files cannot replace unchanged original pending evidence."""
    request = fixture_tests.fixture_request(modeled_fixtures)
    files = historical_files("delayed-suite")
    prefix = "observation/c7/"
    trace = parse_json_strict(files[prefix + "phase.json"])
    if change == "raw-pending":
        files[prefix + "index-0.body"] = b"{}"
    elif change == "timing":
        trace["reads"][1]["start"] = trace["reads"][0]["finish"] + 9
    elif change == "missing-pending":
        trace["reads"].pop(0)
    elif change == "terminal-after-next-file":
        records = parse_json_strict(files["requests.json"])
        next_file = next(
            record
            for record in records
            if record["kind"] == "file"
            and record["start"] >= trace["reads"][-1]["finish"]
        )
        trace["stopped-at"] = next_file["start"] + 0.001
    else:
        before = parse_json_strict(files["capture/c6/index.body"])
        before["files"].pop(0)
        files["capture/c6/index.body"] = canonicalize(before)
    files[prefix + "phase.json"] = canonicalize(trace)
    consumed = []
    with pytest.raises(
        (ValueError, KeyError),
        match="observation termination"
        if change == "terminal-after-next-file"
        else None,
    ):
        audit_suite(
            request, modeled_fixtures, files, consumer=_consumer(consumed)
        )
    assert consumed == []


@pytest.mark.parametrize(
    ("status", "body", "expected"),
    [
        (400, b"File already exists", True),
        (400, b"See https://pypi.org/help/#file-name-reuse", True),
        (400, b"https://test.pypi.org/help/#file-name-reuse", True),
        (200, b"File already exists", False),
        (409, b"File already exists", False),
        (400, b"Invalid metadata", False),
        (403, b"File already exists", False),
        (302, b"File already exists", False),
        (500, b"File already exists", False),
    ],
)
def test_python_native_historical_duplicate_requires_old_rejection(
    status, body, expected
):
    """The retired protocol retains its original rejection interpretation."""
    assert (
        duplicate_response(PythonHttpResponse(status, body, "text/plain"))
        is expected
    )


@pytest.mark.parametrize(
    "change",
    [
        "missing-marker",
        "serialized-race",
        "foreign-digest",
        "unknown-duplicate",
        "unexpected-delta",
        "raw-download",
        "missing-artifact",
        "failed-step",
        "false-count",
    ],
)
def test_python_native_audit_rejects_incomplete_or_contradictory_evidence(
    modeled_fixtures, change
):
    """A passing producer summary cannot overrule replayed raw facts."""
    request, files = _archived_suite(modeled_fixtures)
    if change == "missing-marker":
        files.pop("upload/step-1.marker.json")
    elif change == "missing-artifact":
        files = {"request.json": request.content}
    elif change == "failed-step":
        files["failure.json"] = canonicalize(
            {"result": "spent-possibly-mutated"}
        )
    elif change == "unknown-duplicate":
        files["upload/step-3-0.body"] = b"Invalid package metadata"
    elif change == "raw-download":
        files["capture/c8/file-0.body"] = b"wrong original"
    elif change == "unexpected-delta":
        key = "capture/c4.json"
        metadata = parse_canonical_json(files[key])
        metadata["inventory"][next(iter(metadata["inventory"]))]["yanked"] = (
            True
        )
        files[key] = canonicalize(metadata)
    elif change == "false-count":
        metadata = parse_canonical_json(files["suite.json"])
        metadata["counts"]["upload"] = 9
        files["suite.json"] = canonicalize(metadata)
    else:
        key = "upload/step-7-1.json"
        record = parse_canonical_json(files[key])
        if change == "foreign-digest":
            record["digest"] = "sha256:" + "f" * 64
        else:
            first = parse_canonical_json(files["upload/step-7-0.json"])
            record["start"] = first["finish"] + 1
            record["finish"] = record["start"] + 1
        files[key] = canonicalize(record)
    consumed = []
    with pytest.raises((ValueError, KeyError)):
        audit_suite(
            request, modeled_fixtures, files, consumer=_consumer(consumed)
        )
    assert consumed == []


def test_python_native_failed_fresh_consumer_blocks_audit(modeled_fixtures):
    """Exact service readback is insufficient when a clean consumer fails."""
    request, files = _archived_suite(modeled_fixtures)
    consumed = []

    def failed(item):
        consumed.append(item)
        message = "fresh consumer failed"
        raise ValueError(message)

    with pytest.raises(ValueError, match="fresh consumer failed"):
        audit_suite(request, modeled_fixtures, files, consumer=failed)
    assert len(consumed) == 1


@pytest.mark.parametrize(
    "change", ["failed-command", "version", "project", "module-type", "schema"]
)
def test_python_native_audit_rejects_invalid_fresh_consumer_proof(
    modeled_fixtures, change
):
    """Fresh qualification must return successful and bound consumer facts."""
    request, files = _archived_suite(modeled_fixtures)
    consumed = []
    consume = _consumer(consumed)

    def invalid(item):
        result = consume(item)
        if change == "failed-command":
            command = parse_canonical_json(result.command_evidence[0])
            command["exit-code"] = 1
            return replace(result, command_evidence=(canonicalize(command),))
        installed = parse_canonical_json(result.installed)
        if change == "version":
            installed["version"] = "99.0.0"
        elif change == "project":
            installed["project-id"] = "foreign-project"
        elif change == "module-type":
            installed["module"] = 17
        else:
            installed["unexpected"] = True
        return replace(result, installed=canonicalize(installed))

    with pytest.raises((ValueError, TypeError)):
        audit_suite(request, modeled_fixtures, files, consumer=invalid)
    assert len(consumed) == 1


@pytest.mark.parametrize("change", ["serialized-log", "single-step"])
def test_python_native_audit_binds_upload_intervals_to_request_log(
    modeled_fixtures, change
):
    """Separately valid timelines cannot contradict each other in an audit."""
    request, files = _archived_suite(modeled_fixtures)
    if change == "serialized-log":
        requests = parse_json_strict(files["requests.json"])
        start = parse_canonical_json(files["budget.json"])["start"]
        for number, item in enumerate(requests):
            item["start"] = start + number
            item["finish"] = start + number + 0.5
        files["requests.json"] = canonicalize(requests)
    else:
        name = "upload/step-1-0.json"
        record = parse_canonical_json(files[name])
        record["start"] -= 0.0001
        files[name] = canonicalize(record)
    consumed = []
    with pytest.raises(ValueError, match=r"timing|interval|request"):
        audit_suite(
            request, modeled_fixtures, files, consumer=_consumer(consumed)
        )
    assert consumed == []


@pytest.mark.parametrize("order", [(0, 1), (1, 0)])
def test_python_native_audit_accepts_either_race_completion_order(
    modeled_fixtures, order
):
    """Request completion order need not match fixed contender record order."""
    request, files = _archived_suite(modeled_fixtures)
    requests = parse_json_strict(files["requests.json"])
    uploads = [i for i, item in enumerate(requests) if item["kind"] == "upload"]
    for ordinal, positions in ((7, uploads[6:8]), (8, uploads[8:10])):
        for position, contender in zip(positions, order, strict=True):
            record = parse_canonical_json(
                files[f"upload/step-{ordinal}-{contender}.json"]
            )
            requests[position] = {
                "kind": "upload",
                "start": record["start"],
                "finish": record["finish"],
            }
    files["requests.json"] = canonicalize(requests)
    consumed = []
    result = audit_suite(
        request, modeled_fixtures, files, consumer=_consumer(consumed)
    )
    assert len(consumed) == _CONSUMERS
    assert (
        parse_canonical_json(result["audit.json"])["native-admission"] is False
    )


@pytest.mark.parametrize(
    "change",
    [
        "long-budget",
        "empty-budget",
        "expired-start",
        "late-finish",
        "missing-request",
        "wrong-kind",
        "reordered",
        "read-overlap",
        "extra-file",
    ],
)
def test_python_native_audit_rejects_invalid_effect_budget(
    modeled_fixtures, change
):
    """Raw request history must preserve finite bounds, order and inventory."""
    request, files = _archived_suite(modeled_fixtures)
    budget = parse_canonical_json(files["budget.json"])
    requests = parse_json_strict(files["requests.json"])
    if change == "long-budget":
        budget["deadline"] = budget["start"] + 601
    elif change == "empty-budget":
        budget["deadline"] = budget["start"]
    elif change == "expired-start":
        requests[-1]["start"] = budget["deadline"]
        requests[-1]["finish"] = budget["deadline"]
    elif change == "late-finish":
        requests[-1]["finish"] = budget["deadline"] + 31
    elif change == "missing-request":
        requests.pop()
    elif change == "wrong-kind":
        requests[-1]["kind"] = "upload"
    elif change == "reordered":
        requests[0]["kind"], requests[1]["kind"] = (
            requests[1]["kind"],
            requests[0]["kind"],
        )
    elif change == "read-overlap":
        requests[1]["start"] = requests[0]["start"]
    else:
        files["unexpected.bin"] = b"unbound evidence"
    files["budget.json"] = canonicalize(budget)
    files["requests.json"] = canonicalize(requests)
    consumed = []
    with pytest.raises(
        ValueError,
        match=r"deadline|timing|budget|schedule|concurrency|inventory",
    ):
        audit_suite(
            request, modeled_fixtures, files, consumer=_consumer(consumed)
        )
    assert consumed == []


@pytest.mark.parametrize("group", ["hosted", "delayed-suite"])
def test_python_native_historical_audit_remains_non_admitting(
    modeled_fixtures, monkeypatch, group
):
    """Static old-protocol supplied facts pass only the historical reader."""
    request, files = _archived_suite(modeled_fixtures)
    if group == "delayed-suite":
        files = historical_files(group)

    def forbidden(*_args, **_kwargs):
        pytest.fail("Historical audit attempted a request or sleep")

    monkeypatch.setattr("time.sleep", forbidden)
    calls = []
    result = audit_suite(
        request, modeled_fixtures, files, consumer=_consumer(calls)
    )
    verdict = parse_canonical_json(result["audit.json"])
    assert verdict["result"] == "supplied-facts-pass"
    assert verdict["native-admission"] is False
    assert len(calls) == _CONSUMERS
    assert {item.digest for item in calls} == {
        modeled_fixtures.distributions[f"{label}/original/{variant}"].digest
        for label in ("a", "b")
        for variant in ("wheel", "sdist")
    }
