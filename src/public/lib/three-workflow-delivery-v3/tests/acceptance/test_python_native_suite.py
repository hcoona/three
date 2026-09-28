"""Fixed native scenarios through real upload mapping and offline replay."""

import base64
import threading
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from dataclasses import replace
from email import policy
from email.parser import BytesParser
from http import HTTPStatus
from pathlib import Path

import pytest
from three_workflow_delivery_v3.acceptance import (
    python_native_contract,
    python_native_suite,
)
from three_workflow_delivery_v3.acceptance.python_native_suite import (
    audit_suite,
    duplicate_response,
    run_suite,
)
from three_workflow_delivery_v3.adapters.pypi import (
    PythonHttpResponse,
    PythonRegistry,
)
from three_workflow_delivery_v3.adapters.python import PythonConsumerResult
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

from ..adapters.test_pypi import FakeHttp, _entry, _index
from . import test_python_native_capture as capture_tests
from . import test_python_native_fixture as fixture_tests

modeled_fixtures = fixture_tests.modeled_fixtures
deny_real_registry = capture_tests.deny_real_registry
_TOKEN = "pypi-suite-synthetic-secret"  # noqa: S105 - synthetic local capability
_UPLOADS = 10
_READS = 27
_CONSUMERS = 4
_MAX_INDEX_READS = 29
_FIXED_STEPS = (
    ("a/original/wheel",),
    ("a/original/sdist",),
    ("a/original/wheel",),
    ("a/original/sdist",),
    ("a/comparison/wheel",),
    ("a/comparison/sdist",),
    ("b/original/wheel", "b/comparison/wheel"),
    ("b/original/sdist", "b/comparison/sdist"),
)


class RegistryBoundary:
    """Finite in-process storage boundary, not a native service claim."""

    def __init__(  # noqa: PLR0913 - bounded external outcome controls
        self,
        fixtures,
        *,
        winners=("original", "original"),
        failed_post=None,
        failed_status=503,
        failed_body=b"unavailable",
        marker_check=None,
    ):
        """Choose only external outcomes; production owns the fixed schedule."""
        self.registry = PythonRegistry("testpypi")
        self.fixtures = fixtures
        self.winners = dict(zip(("wheel", "sdist"), winners, strict=True))
        self.failed_post = failed_post
        self.failed_status = failed_status
        self.failed_body = failed_body
        self.marker_check = marker_check
        self.posts = []
        self.reads = []
        self.stored = {}
        self.lock = threading.Lock()
        self.race_barriers = {
            variant: threading.Barrier(2) for variant in ("wheel", "sdist")
        }
        self.created = {
            variant: threading.Event() for variant in ("wheel", "sdist")
        }
        self.unrelated = {
            "filename": "hcoona_release_smoke_python-0.0.1-py3-none-any.whl",
            "url": f"https://{self.registry.file_host}/packages/untouched.whl",
            "hashes": {"sha256": "e" * 64},
            "yanked": False,
        }

    def request(  # noqa: C901 - explicit finite service-boundary outcomes
        self, method, url, headers, body, _maximum_bytes
    ):
        """Parse actual multipart bytes and record external boundary calls."""
        if method == "GET":
            with self.lock:
                self.reads.append(url)
            assert headers.get("Authorization") is None
            if url == self.registry.index_url:
                return _index(
                    [
                        self.unrelated,
                        *[
                            _entry(self.registry, item)
                            for item in self.stored.values()
                        ],
                    ]
                )
            item = self.stored[url.rsplit("/", 1)[1]]
            return PythonHttpResponse(
                200, item.content, "application/octet-stream"
            )
        assert method == "POST"
        assert url == self.registry.upload_url
        message = BytesParser(policy=policy.default).parsebytes(
            (
                f"Content-Type: {headers['Content-Type']}\r\n"
                "MIME-Version: 1.0\r\n\r\n"
            ).encode()
            + body
        )
        content_part = next(
            part
            for part in message.iter_parts()
            if part.get_param("name", header="content-disposition") == "content"
        )
        content = content_part.get_payload(decode=True)
        key, item = next(
            (key, item)
            for key, item in self.fixtures.distributions.items()
            if item.content == content
        )
        assert content_part.get_filename() == item.filename
        if self.marker_check is not None:
            self.marker_check(key)
        with self.lock:
            self.posts.append(key)
            post_number = len(self.posts)
        label, candidate, variant = key.split("/")
        if label == "b":
            self.race_barriers[variant].wait(timeout=5)
            if candidate != self.winners[variant]:
                assert self.created[variant].wait(timeout=5), (
                    "Winner never completed its admitted request"
                )
        if post_number == self.failed_post:
            if label == "b" and candidate == self.winners[variant]:
                self.created[variant].set()
            if self.failed_status is None:
                message = "synthetic lost response"
                raise TimeoutError(message)
            return PythonHttpResponse(
                self.failed_status, self.failed_body, "text/plain"
            )
        with self.lock:
            exists = item.filename in self.stored
            if not exists:
                self.stored[item.filename] = item
        if label == "b" and candidate == self.winners[variant]:
            self.created[variant].set()
        return PythonHttpResponse(
            400 if exists else 200,
            b"File already exists" if exists else b"accepted",
            "text/plain",
        )


class ObservationClock:
    """Advance tiny trusted ticks under real races and virtual policy waits."""

    def __init__(self):
        """Keep the mutable instant shared safely by both upload contenders."""
        self.now = 100.0
        self.lock = threading.Lock()
        self.waits = []

    def __call__(self):
        """Return ordered instants without depending on scheduler speed."""
        with self.lock:
            self.now += 0.0001
            return self.now

    def wait(self, seconds):
        """Advance finite observation spacing without real sleep."""
        with self.lock:
            self.waits.append(seconds)
            self.now += seconds


class DelayedRegistry(RegistryBoundary):
    """Hide only the newest addition during selected finite index reads."""

    def __init__(self, fixtures, *, pending=5, **options):
        """Retain a pre-existing single-wheel version throughout the suite."""
        super().__init__(fixtures, **options)
        self.pending = pending
        self.pending_left = pending
        self.visible_count = 0
        self.index_history = []

    def request(self, method, url, headers, body, maximum_bytes):
        """Delay visibility while actual multipart storage and races remain."""
        if (
            method == "GET"
            and url == self.registry.index_url
            and len(self.stored) > self.visible_count
        ):
            if self.pending_left:
                self.pending_left -= 1
                self.reads.append(url)
                response = _index(
                    [
                        self.unrelated,
                        *[
                            _entry(self.registry, item)
                            for item in list(self.stored.values())[:-1]
                        ],
                    ]
                )
                self.index_history.append(parse_json_strict(response.body))
                return response
            self.visible_count = len(self.stored)
            self.pending_left = self.pending
        response = super().request(method, url, headers, body, maximum_bytes)
        if method == "GET" and url == self.registry.index_url:
            self.index_history.append(parse_json_strict(response.body))
        return response


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


def _run(fixtures, output, **options):
    request = fixture_tests.fixture_request(fixtures)
    http = RegistryBoundary(fixtures, **options)
    files = run_suite(request, fixtures, http, _TOKEN, output)
    return request, http, files


@pytest.mark.parametrize("wheel_winner", ["original", "comparison"])
@pytest.mark.parametrize("sdist_winner", ["original", "comparison"])
def test_python_native_competing_creation_accepts_independent_winners(
    modeled_fixtures, tmp_path, wheel_winner, sdist_winner
):
    """All four winner pairs preserve bytes within the fixed effect budget."""
    request, http, files = _run(
        modeled_fixtures,
        tmp_path / "probe",
        winners=(wheel_winner, sdist_winner),
    )
    assert len(http.posts) == _UPLOADS
    assert len(http.reads) == _READS
    assert http.posts[:6] == [keys[0] for keys in _FIXED_STEPS[:6]]
    assert set(http.posts[6:8]) == set(_FIXED_STEPS[6])
    assert set(http.posts[8:]) == set(_FIXED_STEPS[7])
    expected_sizes = (1, 2, 3, 3, 3, 3, 3, 4, 5)
    for ordinal, size in enumerate(expected_sizes):
        capture = parse_canonical_json(files[f"capture/c{ordinal}.json"])
        assert len(capture["inventory"]) == size
        assert (
            capture["inventory"][http.unrelated["filename"]] == http.unrelated
        )
    for variant, winner in (("wheel", wheel_winner), ("sdist", sdist_winner)):
        expected = modeled_fixtures.distributions[f"b/{winner}/{variant}"]
        assert http.stored[expected.filename].digest == expected.digest
    consumed = []
    audit = audit_suite(
        request, modeled_fixtures, files, consumer=_consumer(consumed)
    )
    assert len(consumed) == _CONSUMERS
    assert {item.digest for item in consumed} == {
        item.digest for item in http.stored.values()
    }
    assert (
        parse_canonical_json(audit["audit.json"])["native-admission"] is False
    )
    assert len(http.posts) == _UPLOADS
    assert len(http.reads) == _READS
    assert all(
        _TOKEN.encode() not in data
        for data in (*files.values(), *audit.values())
    )


@pytest.mark.parametrize("winner", ["original", "comparison"])
def test_native_delayed_observations_preserve_partial_version_and_budget(
    modeled_fixtures, tmp_path, monkeypatch, winner
):
    """Each phase may use six reads while preserving older partial versions."""
    request = fixture_tests.fixture_request(modeled_fixtures)
    clock = ObservationClock()
    http = DelayedRegistry(modeled_fixtures, winners=(winner, winner))
    files = run_suite(
        request,
        modeled_fixtures,
        http,
        _TOKEN,
        tmp_path / "probe",
        clock=clock,
        wait=clock.wait,
    )
    records = parse_json_strict(files["requests.json"])
    assert Counter(record["kind"] for record in records) == {
        "index": 29,
        "upload": 10,
        "file": 18,
    }
    expected = ["index"]
    for ordinal, keys in enumerate(_FIXED_STEPS, 1):
        expected.extend(["upload"] * len(keys))
        expected.extend(["index"] * (6 if ordinal in {1, 2, 7, 8} else 1))
        expected.extend(
            ["file"] * (ordinal - 4 if ordinal in {7, 8} else min(ordinal, 2))
        )
    assert [record["kind"] for record in records] == expected
    assert len(http.index_history) == _MAX_INDEX_READS
    assert all(
        document["files"][0] == http.unrelated
        for document in http.index_history
    )
    assert http.unrelated["filename"] not in http.stored
    assert len(clock.waits) == 20  # noqa: PLR2004 - five waits in four phases
    assert all(9 < delay <= 10 for delay in clock.waits)  # noqa: PLR2004 - controlled tick margin
    for ordinal in (1, 2, 7, 8):
        trace = parse_json_strict(files[f"observation/c{ordinal}/phase.json"])
        assert [read["classification"] for read in trace["reads"]] == [
            *(["pending"] * 5),
            "exact",
        ]
        assert [read["ordinal"] for read in trace["reads"]] == list(range(6))
        assert trace["reads"][-1]["start"] < trace["upload-finished"] + 60
        if ordinal in {7, 8}:
            uploads = [
                parse_json_strict(files[f"upload/step-{ordinal}-{number}.json"])
                for number in (0, 1)
            ]
            successful = next(
                upload
                for upload in uploads
                if upload["status"] == HTTPStatus.OK
            )
            assert trace["upload-finished"] == successful["finish"]
            assert trace["addition"]["digest"] == successful["digest"]

    def denied_wait(_seconds):
        pytest.fail("Native offline audit attempted to sleep")

    monkeypatch.setattr("time.sleep", denied_wait)
    consumed = []
    audited = audit_suite(
        request, modeled_fixtures, files, consumer=_consumer(consumed)
    )
    assert len(consumed) == _CONSUMERS
    assert {item.digest for item in consumed} == {
        item.digest for item in http.stored.values()
    }
    assert parse_json_strict(audited["audit.json"])["native-admission"] is False


def test_native_late_race_join_exhausts_winner_window_before_read(
    modeled_fixtures, tmp_path, monkeypatch
):
    """Both real contenders finish, but delayed join cannot renew the winner."""
    clock = ObservationClock()
    joins = []

    class DelayedJoin(ThreadPoolExecutor):
        def __exit__(self, *args):
            result = super().__exit__(*args)
            joins.append(clock())
            clock.wait(60)
            return result

    monkeypatch.setattr(python_native_suite, "ThreadPoolExecutor", DelayedJoin)
    http = RegistryBoundary(modeled_fixtures)
    output = tmp_path / "probe"
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(
            fixture_tests.fixture_request(modeled_fixtures),
            modeled_fixtures,
            http,
            _TOKEN,
            output,
            clock=clock,
            wait=clock.wait,
        )
    assert len(joins) == 1
    assert set(http.posts[6:]) == set(_FIXED_STEPS[6])
    assert len(http.posts) == 8  # noqa: PLR2004 - both wheel contenders joined
    assert (output / "upload/step-7-0.json").is_file()
    assert (output / "upload/step-7-1.json").is_file()
    assert not (output / "capture/c7.json").exists()
    assert not (output / "upload/step-8.marker.json").exists()
    trace = parse_json_strict(
        (output / "observation/c7/phase.json").read_bytes()
    )
    assert trace["terminal"] == "exhausted"
    assert trace["reads"] == []
    assert trace["stopped-at"] >= trace["upload-finished"] + 60
    assert (
        parse_json_strict((output / "failure.json").read_bytes())["result"]
        == "spent-possibly-mutated"
    )


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
    modeled_fixtures, tmp_path, change
):
    """Clean final files cannot replace unchanged original pending evidence."""
    clock = ObservationClock()
    http = DelayedRegistry(modeled_fixtures, pending=1)
    request = fixture_tests.fixture_request(modeled_fixtures)
    files = run_suite(
        request,
        modeled_fixtures,
        http,
        _TOKEN,
        tmp_path / "probe",
        clock=clock,
        wait=clock.wait,
    )
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
def test_python_native_duplicate_needs_explicit_filename_rejection(
    status, body, expected
):
    """Only explicit filename-reuse evidence establishes duplication."""
    assert (
        duplicate_response(PythonHttpResponse(status, body, "text/plain"))
        is expected
    )


@pytest.mark.parametrize(
    ("post", "status"), [(1, 503), (2, 403), (3, 200), (4, 400), (6, None)]
)
def test_python_native_failure_stops_and_retains_partial_audit(
    modeled_fixtures, tmp_path, post, status
):
    """A failed/ambiguous step never spends the next read or mutation budget."""
    output = tmp_path / "probe"
    request = fixture_tests.fixture_request(modeled_fixtures)
    http = RegistryBoundary(
        modeled_fixtures, failed_post=post, failed_status=status
    )
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(request, modeled_fixtures, http, _TOKEN, output)
    assert len(http.posts) == post
    assert (output / f"upload/step-{post}.marker.json").is_file()
    assert not (output / f"capture/c{post}.json").exists()
    assert not (output / "suite.json").exists()
    failure = parse_canonical_json((output / "failure.json").read_bytes())
    assert failure["result"] == "spent-possibly-mutated"
    assert failure["counts"]["upload"] == post
    assert not (output / f"upload/step-{post + 1}.marker.json").exists()


def test_python_native_race_failure_joins_both_admitted_peers(
    modeled_fixtures, tmp_path
):
    """Both wheel contenders finish collection; a failed peer prevents sdist."""
    output = tmp_path / "probe"
    request = fixture_tests.fixture_request(modeled_fixtures)
    http = RegistryBoundary(modeled_fixtures, failed_post=8, failed_status=None)
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(request, modeled_fixtures, http, _TOKEN, output)
    assert set(http.posts[6:]) == set(_FIXED_STEPS[6])
    assert all("b/" not in key or key.endswith("/wheel") for key in http.posts)
    assert (output / "upload/step-7-0.json").exists()
    assert (output / "upload/step-7-1.json").exists()
    assert not (output / "upload/step-8.marker.json").exists()
    assert not (output / "capture/c7.json").exists()


def test_python_native_suite_fsyncs_complete_marker_before_each_send(
    modeled_fixtures, tmp_path, monkeypatch
):
    """Both competing identities are locally durable before either HTTP call."""
    output = tmp_path / "probe"
    synced = set()
    real_sync = python_native_contract.os.fsync

    def sync(descriptor):
        real_sync(descriptor)
        synced.add(Path(f"/proc/self/fd/{descriptor}").resolve())

    monkeypatch.setattr(python_native_contract.os, "fsync", sync)

    def marker_check(key):
        markers = [
            path for path in synced if path.name.endswith(".marker.json")
        ]
        ordinal = len(markers)
        path = output / f"upload/step-{ordinal}.marker.json"
        assert path in synced
        marker = parse_canonical_json(path.read_bytes())
        assert [entry["key"] for entry in marker["files"]] == list(
            _FIXED_STEPS[ordinal - 1]
        )
        assert key in _FIXED_STEPS[ordinal - 1]

    _run(modeled_fixtures, output, marker_check=marker_check)


def test_python_native_marker_failure_stops_before_send(
    modeled_fixtures, tmp_path, monkeypatch
):
    """Failed local persistence cannot admit even the first upload."""
    output = tmp_path / "probe"
    original = python_native_suite.write_exclusive

    def persist(root, name, content):
        if name.endswith(".marker.json"):
            message = "marker persistence unavailable"
            raise OSError(message)
        original(root, name, content)

    monkeypatch.setattr(python_native_suite, "write_exclusive", persist)
    http = RegistryBoundary(modeled_fixtures)
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(
            fixture_tests.fixture_request(modeled_fixtures),
            modeled_fixtures,
            http,
            _TOKEN,
            output,
        )
    assert http.posts == []
    assert (output / "capture/c0.json").is_file()
    assert (output / "failure.json").is_file()


def test_python_native_secret_response_is_not_retained(
    modeled_fixtures, tmp_path
):
    """An encoded token echo stops the generation without persistent secrets."""
    output = tmp_path / "probe"
    encoded = base64.b64encode(f"__token__:{_TOKEN}".encode())
    http = RegistryBoundary(
        modeled_fixtures,
        failed_post=1,
        failed_status=400,
        failed_body=b"File already exists " + encoded,
    )
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(
            fixture_tests.fixture_request(modeled_fixtures),
            modeled_fixtures,
            http,
            _TOKEN,
            output,
        )
    assert len(http.posts) == 1
    assert not (output / "upload/step-1-0.body").exists()
    for path in output.rglob("*"):
        if path.is_file():
            assert _TOKEN.encode() not in path.read_bytes()
            assert encoded not in path.read_bytes()


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
    modeled_fixtures, tmp_path, change
):
    """A passing producer summary cannot overrule replayed raw facts."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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


def test_python_native_failed_fresh_consumer_blocks_audit(
    modeled_fixtures, tmp_path
):
    """Exact service readback is insufficient when a clean consumer fails."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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
    modeled_fixtures, tmp_path, change
):
    """Fresh qualification must return successful and bound consumer facts."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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
    modeled_fixtures, tmp_path, change
):
    """Separately valid timelines cannot contradict each other in an audit."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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
    modeled_fixtures, tmp_path, order
):
    """Request completion order need not match fixed contender record order."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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


@pytest.mark.parametrize("body", [b"Project not found", _index([]).body])
def test_python_native_initial_404_stops_before_upload(
    modeled_fixtures, tmp_path, body
):
    """Missing project evidence is retained without admitting any upload."""
    request = fixture_tests.fixture_request(modeled_fixtures)
    response = PythonHttpResponse(
        404, body, "application/vnd.pypi.simple.v1+json"
    )
    http = FakeHttp(response)
    output = tmp_path / "probe"
    with pytest.raises(ValueError, match="generation remains spent"):
        run_suite(request, modeled_fixtures, http, _TOKEN, output)
    assert [(call[0], call[1]) for call in http.calls] == [
        ("GET", request.registry.index_url)
    ]
    assert (output / "capture/c0/index.body").read_bytes() == response.body
    retained = parse_canonical_json(
        (output / "capture/c0/index.response.json").read_bytes()
    )
    assert retained["status"] == 404  # noqa: PLR2004 - concrete HTTP evidence
    failure = parse_canonical_json((output / "failure.json").read_bytes())
    assert failure["counts"] == {"index": 1, "upload": 0, "file": 0}
    assert failure["result"] == "spent-possibly-mutated"
    assert not (output / "upload").exists()
    assert not (output / "capture/c0.json").exists()
    assert not (output / "capture/c1").exists()
    assert not (output / "suite.json").exists()


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
    modeled_fixtures, tmp_path, change
):
    """Raw request history must preserve finite bounds, order and inventory."""
    request, _, files = _run(modeled_fixtures, tmp_path / "probe")
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
