# Controlled native archives reuse the existing V3 adapter fixture factory.
# ruff: noqa: PLR2004, PLC0415

"""Regressed index recovery through the real pinned parser and exact bytes."""

import importlib.util
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest
from campaign import digest, read, save_json
from read_policy import ReadPending, ReadStopped
from registry_read import RegistryAudit
from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.repository.python_provider import (
    PythonNbgvFacts,
)


@pytest.fixture
def native_pair():
    """Reuse actual wheel/sdist fixture construction without native builds."""
    repository = Path(__file__).resolve().parents[5]
    source = (
        repository
        / "src/public/lib/three-workflow-delivery-v3"
        / "tests/adapters/test_pypi.py"
    )
    spec = importlib.util.spec_from_file_location(
        "operator_native_fixtures", source
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    witness = module.witness.__wrapped__()
    target = "86b63fd09fcf3d20ee2615b35e37f4a541d0dc27"
    witness = replace(
        witness,
        target=target,
        nbgv=PythonNbgvFacts(
            canonicalize(
                {
                    "SimpleVersion": "0.1.0",
                    "SemVer2": "0.1.0-beta.31",
                    "GitCommitId": target,
                    "VersionHeight": 31,
                    "PublicRelease": True,
                }
            ),
            "0.1.0b31",
        ),
    )
    return module.pair.__wrapped__(witness)


def index(registry, distributions, serial):
    """Create a complete native simple-index representation."""
    return canonicalize(
        {
            "meta": {"api-version": "1.4", "_last-serial": serial},
            "name": "hcoona-release-smoke-python",
            "files": [
                {
                    "filename": distribution.filename,
                    "url": f"https://{registry.file_host}/packages/{distribution.filename}",
                    "hashes": {
                        "sha256": distribution.digest.removeprefix("sha256:")
                    },
                    "yanked": False,
                }
                for distribution in distributions
            ],
        }
    )


@pytest.fixture
def audit_fixture(tmp_path, native_pair):
    """Keep wire replies finite and evidence persistence real."""
    from types import SimpleNamespace

    registry = PythonRegistry("testpypi")
    clock = [datetime(2026, 9, 28, 23, tzinfo=UTC)]
    path = tmp_path / "ledger.json"
    save_json(path, {})
    calls, replies = [], []

    def wire(url, headers, maximum_bytes):
        calls.append((url, headers, maximum_bytes))
        assert replies, "Unexpected registry request"
        response = replies.pop(0)
        if isinstance(response, Exception):
            raise response
        return response

    def construct(
        *,
        deadline=None,
        originals=None,
        baseline=...,
        purpose="exact-inventory",
    ):
        ledger = read(path)
        return RegistryAudit(
            tmp_path / "audit",
            ledger,
            deadline or (clock[0] + timedelta(hours=4)).isoformat(),
            registry,
            originals or native_pair[:1],
            save=lambda: save_json(path, ledger),
            now=lambda: clock[0],
            wire=wire,
            baseline=index(registry, native_pair[:1], 200)
            if baseline is ...
            else baseline,
            purpose=purpose,
        )

    return SimpleNamespace(
        registry=registry,
        clock=clock,
        path=path,
        calls=calls,
        replies=replies,
        construct=construct,
        pair=native_pair,
        root=tmp_path,
    )


def response(body):
    """Return an ordinary successful native representation."""
    return 200, {"Content-Type": "application/vnd.pypi.simple.v1+json"}, body


def test_regressed_index_then_exact_fresh_bytes_completes_pending_audit(
    audit_fixture,
):
    """An old representation remains evidence; completion needs fresh bytes."""
    fixture = audit_fixture
    wheel = fixture.pair[0]
    failed = fixture.root / "failed-outcome.json"
    failed.write_text('{"outcome":"failed"}')
    outcome_digest = digest(failed)
    stale = index(fixture.registry, (), 100)
    fixture.replies.append(response(stale))
    first = fixture.construct()
    with pytest.raises(ReadPending):
        first.step()
    assert read(fixture.path)["classification"] == "pending"
    assert len(fixture.calls) == 1
    fixture.clock[0] += timedelta(seconds=30)
    current = index(fixture.registry, (wheel,), 201)
    fixture.replies.extend([response(current), response(wheel.content)])
    result = fixture.construct().step()
    ledger = read(fixture.path)
    assert result.files == (wheel,)
    assert ledger["classification"] == "complete"
    assert [entry["classification"] for entry in ledger["observations"]] == [
        "regressed",
        "complete",
    ]
    assert [entry["kind"] for entry in ledger["requests"]] == [
        "index",
        "index",
        "file",
    ]
    assert (
        fixture.root / "audit" / ledger["requests"][0]["body"]
    ).read_bytes() == stale
    assert (
        fixture.root / "audit" / ledger["requests"][2]["body"]
    ).read_bytes() == wheel.content
    assert digest(failed) == outcome_digest
    assert all(set(headers) <= {"Accept"} for _, headers, _ in fixture.calls)


def test_repeated_regression_stops_at_original_deadline_without_mutation(
    audit_fixture,
):
    """Timeout cannot become absence, upload permission or proof success."""
    fixture = audit_fixture
    deadline = fixture.clock[0] + timedelta(seconds=90)
    fixture.replies.extend([response(index(fixture.registry, (), 100))] * 2)
    with pytest.raises(ReadPending):
        fixture.construct(deadline=deadline.isoformat()).step()
    fixture.clock[0] += timedelta(seconds=30)
    with pytest.raises(ReadStopped, match="deadline"):
        fixture.construct(deadline=deadline.isoformat()).step()
    ledger = read(fixture.path)
    assert ledger["classification"] == "stopped"
    assert [entry["kind"] for entry in ledger["requests"]] == ["index", "index"]
    assert all(
        entry["classification"] == "regressed"
        for entry in ledger["observations"]
    )
    assert len(fixture.calls) == 2
    with pytest.raises(ReadStopped):
        fixture.construct(deadline=deadline.isoformat()).step()
    assert len(fixture.calls) == 2


@pytest.mark.parametrize("serial", [200, 201])
def test_equal_or_newer_serial_without_exact_inventory_cannot_complete_proof(
    audit_fixture, serial
):
    """Serial progression cannot explain missing successful wheel bytes."""
    fixture = audit_fixture
    fixture.replies.append(response(index(fixture.registry, (), serial)))
    with pytest.raises(AssertionError, match="non-regressed"):
        fixture.construct().step()
    assert read(fixture.path)["classification"] == "stopped"
    assert len(fixture.calls) == 1


@pytest.mark.parametrize("purpose", ["exact-inventory", "diagnostic"])
def test_conflicting_download_bytes_stop_native_audit(audit_fixture, purpose):
    """Matching index metadata cannot replace distribution byte verification."""
    fixture = audit_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(b"different archive bytes"),
        ]
    )
    with pytest.raises((AssertionError, ValueError)):
        fixture.construct(purpose=purpose).step()
    assert read(fixture.path)["classification"] == "stopped"
    assert len(fixture.calls) == 2
    assert not (fixture.root / "audit/completion.json").exists()


def test_registry_transient_response_is_retained_and_retry_is_paced(
    audit_fixture,
):
    """An error response survives reentry; another GET waits for Retry-After."""
    fixture = audit_fixture
    fixture.replies.append((503, {"Retry-After": "120"}, b"temporary outage"))
    with pytest.raises(ReadPending):
        fixture.construct().step()
    ledger = read(fixture.path)
    assert (
        fixture.root / "audit" / ledger["requests"][0]["body"]
    ).read_bytes() == b"temporary outage"
    fixture.clock[0] += timedelta(seconds=30)
    with pytest.raises(ReadPending):
        fixture.construct().step()
    assert len(fixture.calls) == 1
    fixture.clock[0] += timedelta(seconds=90)
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            response(fixture.pair[0].content),
        ]
    )
    assert fixture.construct().step().files == fixture.pair[:1]
    assert read(fixture.path)["pacing"]["consecutive_errors"] == 0


def test_complete_pair_resume_reuses_verified_files_and_requires_missing_bytes(
    audit_fixture,
):
    """A paused sdist read preserves the current index and wheel capture."""
    fixture = audit_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair, 201)),
            response(fixture.pair[0].content),
            response(fixture.pair[1].content),
        ]
    )
    with pytest.raises(ReadPending):
        fixture.construct(originals=fixture.pair).step()
    assert [entry["kind"] for entry in read(fixture.path)["requests"]] == [
        "index",
        "file",
    ]
    assert read(fixture.path)["classification"] == "pending"
    fixture.clock[0] += timedelta(seconds=30)
    assert (
        fixture.construct(originals=fixture.pair).step().files == fixture.pair
    )
    assert [url for url, _, _ in fixture.calls] == [
        fixture.registry.index_url,
        f"https://{fixture.registry.file_host}/packages/{fixture.pair[0].filename}",
        f"https://{fixture.registry.file_host}/packages/{fixture.pair[1].filename}",
    ]
    assert read(fixture.path)["classification"] == "complete"


def test_transient_file_read_resumes_same_index_identity_and_digest(
    audit_fixture,
):
    """A failed file GET cannot silently replace the admitted index/digest."""
    fixture = audit_fixture
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair[:1], 201)),
            (503, {"Retry-After": "60"}, b"file unavailable"),
        ]
    )
    with pytest.raises(ReadPending):
        fixture.construct().step()
    fixture.clock[0] += timedelta(seconds=60)
    fixture.replies.append(response(fixture.pair[0].content))
    assert fixture.construct().step().files == fixture.pair[:1]
    assert len(fixture.calls) == 3
    assert fixture.calls[1][0] == fixture.calls[2][0]
    assert [entry["status"] for entry in read(fixture.path)["requests"]] == [
        200,
        503,
        200,
    ]


def test_diagnostic_absence_without_result_or_baseline_records_known_state(
    audit_fixture,
):
    """Pre-upload diagnosis resolves absence without publication proof."""
    fixture = audit_fixture
    fixture.replies.append(response(index(fixture.registry, (), 100)))
    observation = fixture.construct(
        originals=fixture.pair, baseline=None, purpose="diagnostic"
    ).step()
    ledger = read(fixture.path)
    assert observation.classification == "absent"
    assert observation.files == ()
    assert ledger["classification"] == "complete"
    assert ledger["purpose"] == "diagnostic"
    assert ledger["destination_state"] == "absent"
    assert "proof_accepted" not in ledger
    assert [entry["kind"] for entry in ledger["requests"]] == ["index"]


def test_diagnostic_pair_preserves_failed_outcome_without_proof_promotion(
    audit_fixture,
):
    """A failed run may leave a pair that can be diagnosed exactly."""
    fixture = audit_fixture
    failed = fixture.root / "failed-outcome.json"
    failed.write_text('{"outcome":"failed"}')
    failed_digest = digest(failed)
    fixture.replies.extend(
        [
            response(index(fixture.registry, fixture.pair, 201)),
            response(fixture.pair[0].content),
            response(fixture.pair[1].content),
        ]
    )
    with pytest.raises(ReadPending):
        fixture.construct(
            originals=fixture.pair, baseline=None, purpose="diagnostic"
        ).step()
    fixture.clock[0] += timedelta(seconds=30)
    observation = fixture.construct(
        originals=fixture.pair, baseline=None, purpose="diagnostic"
    ).step()
    ledger = read(fixture.path)
    assert observation.files == fixture.pair
    assert ledger["classification"] == "complete"
    assert ledger["purpose"] == "diagnostic"
    assert ledger["destination_state"] == "complete"
    assert "proof_accepted" not in ledger
    assert digest(failed) == failed_digest
    assert [entry["kind"] for entry in ledger["requests"]] == [
        "index",
        "file",
        "file",
    ]


def test_diagnostic_regressed_absence_after_successful_wheel_remains_pending(
    audit_fixture,
):
    """Diagnostic purpose cannot turn a stale index into current absence."""
    fixture = audit_fixture
    fixture.replies.append(response(index(fixture.registry, (), 100)))
    with pytest.raises(ReadPending):
        fixture.construct(originals=fixture.pair, purpose="diagnostic").step()
    ledger = read(fixture.path)
    assert ledger["classification"] == "pending"
    assert ledger["purpose"] == "diagnostic"
    assert "destination_state" not in ledger
    assert [entry["classification"] for entry in ledger["observations"]] == [
        "regressed"
    ]
    assert len(fixture.calls) == 1


@pytest.mark.parametrize("purpose", ["seed-proof", "recovery-proof"])
def test_proof_purposes_cannot_accept_diagnostic_absence(
    audit_fixture, purpose
):
    """Neither hosted proof predicate accepts a merely known absent target."""
    fixture = audit_fixture
    fixture.replies.append(response(index(fixture.registry, (), 100)))
    originals = fixture.pair[:1] if purpose == "seed-proof" else fixture.pair
    with pytest.raises(AssertionError, match="baseline"):
        fixture.construct(
            originals=originals, baseline=None, purpose=purpose
        ).step()
    assert read(fixture.path)["classification"] == "stopped"
    assert len(fixture.calls) == 1
