"""Finite observation with controlled clocks and original response bytes."""

import base64
import copy
import json
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.adapters import pypi
from three_workflow_delivery_v3.adapters.pypi import (
    MAX_INDEX_BYTES,
    PythonHttpResponse,
    PythonRegistry,
    PythonUploadResponse,
)
from three_workflow_delivery_v3.adapters.python_observation import (
    IndexPhase,
    ObservationBasis,
    index_inventory,
    replay_index_phase,
)
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..python_fixtures import model, originals
from .test_pypi import _entry, _index


@pytest.fixture(autouse=True)
def deny_real_network(monkeypatch):
    """Reject an accidental escape from the finite HTTP fixture."""

    def denied(*_args, **_kwargs):
        pytest.fail("Real network access is forbidden")

    monkeypatch.setattr(pypi.http.client, "HTTPSConnection", denied)


@pytest.fixture
def basis():
    """Observe the sdist after a previously verified wheel inventory."""
    _, _, pair = originals(model())
    registry = PythonRegistry("testpypi")
    return ObservationBasis(
        registry,
        "normal-1",
        pair[1],
        _index([_entry(registry, pair[0])]),
        PythonUploadResponse(
            "definitive-success", 200, python_digest(b"accepted"), 100.0
        ),
    )


def exact_index(basis):
    """Construct the expected inventory independently of classification."""
    files = (
        []
        if basis.previous is None
        else json.loads(basis.previous.body)["files"]
    )
    return _index([*files, _entry(basis.registry, basis.addition)])


class Boundary:
    """Control only HTTP, elapsed time, waiting and evidence persistence."""

    def __init__(self, *responses, now=100.0, durations=(), oversleep=0.0):
        """Retain a finite reply queue and an ordered external-effect log."""
        self.responses = list(responses)
        self.now = now
        self.durations = list(durations)
        self.oversleep = oversleep
        self.events = []
        self.saved = {}
        self.calls = []

    def clock(self):
        """Return the controlled process monotonic instant."""
        return self.now

    def wait(self, seconds):
        """Advance virtual time, including a selected scheduler overshoot."""
        self.events.append(("wait", seconds))
        self.now += seconds + self.oversleep

    def request(self, method, url, headers, body, maximum_bytes):
        """Timestamp one finite response and advance by its actual duration."""
        self.calls.append((method, url, headers, body, maximum_bytes, self.now))
        self.events.append(("request", self.now))
        assert self.responses, "Unexpected extra request"
        response = self.responses.pop(0)
        start = self.now
        self.now += self.durations.pop(0) if self.durations else 0
        if isinstance(response, BaseException):
            raise response
        return replace(response, started=start, finished=self.now)

    def retain(self, name, content):
        """Record exactly which bytes persisted before a subsequent effect."""
        self.events.append(("retain", name))
        self.saved[name] = content

    def run(self, phase):
        """Exercise the production scheduler through its injected boundaries."""
        return phase.run(
            self, retain=self.retain, clock=self.clock, wait=self.wait
        )


def test_observation_pending_preserves_originals_before_next_read(basis):
    """Pending reads retain originals; spacing starts at response completion."""
    original = replace(
        basis.previous, body=b"\n  " + basis.previous.body + b"\n"
    )
    expected = exact_index(basis)
    boundary = Boundary(original, expected, durations=(3, 2))
    phase = IndexPhase(basis)
    result = boundary.run(phase)
    assert result.body == expected.body
    assert boundary.calls == [
        (
            "GET",
            basis.registry.index_url,
            {"Accept": "application/vnd.pypi.simple.v1+json"},
            None,
            MAX_INDEX_BYTES,
            instant,
        )
        for instant in (100.0, 113.0)
    ]
    assert boundary.events[:5] == [
        ("request", 100.0),
        ("retain", "index-0.body"),
        ("retain", "index-0.response.json"),
        ("wait", 10.0),
        ("request", 113.0),
    ]
    assert boundary.saved["index-0.body"] == original.body
    retained = json.loads(boundary.saved["phase.json"])
    assert [read["classification"] for read in retained["reads"]] == [
        "pending",
        "exact",
    ]
    assert (
        base64.b64decode(retained["reads"][0]["response"]["body"])
        == original.body
    )
    assert retained["reads"][0]["response"]["digest"] == python_digest(
        original.body
    )
    assert retained["terminal"] == "exact"
    assert [read["ordinal"] for read in retained["reads"]] == [0, 1]
    assert [read["url"] for read in retained["reads"]] == [
        basis.registry.index_url,
        basis.registry.index_url,
    ]


@pytest.mark.parametrize(
    "phase_name",
    [
        "bootstrap-p3",
        "native-c1",
        "native-c2",
        "native-c7",
        "native-c8",
        "normal-0",
        "normal-1",
    ],
)
def test_existing_project_pending_requires_unchanged_http200(basis, phase_name):
    """Each existing-project phase accepts only its unchanged prior listing."""
    basis = replace(basis, phase=phase_name)
    assert basis.classify(basis.previous) == "pending"
    assert basis.classify(exact_index(basis)) == "exact"
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        basis.classify(PythonHttpResponse(404, b"absent", "text/plain"))


def test_bootstrap_p2_only_404_is_pending(basis):
    """An initial empty HTTP 200 cannot masquerade as existing-project delay."""
    basis = replace(basis, phase="bootstrap-p2", previous=None)
    assert (
        basis.classify(PythonHttpResponse(404, b"absent", "text/plain"))
        == "pending"
    )
    assert basis.classify(exact_index(basis)) == "exact"
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        basis.classify(_index([]))
    with pytest.raises(ValueError, match="oversized"):
        basis.classify(
            PythonHttpResponse(404, b"x" * (MAX_INDEX_BYTES + 1), "text/plain")
        )


@pytest.mark.parametrize(
    "change", ["url", "hashes", "requires-python", "yanked", "extension"]
)
def test_observation_compares_every_previous_entry_field(basis, change):
    """Equal filenames cannot hide changed prior metadata or hashes."""
    document = json.loads(exact_index(basis).body)
    document["files"][0][change] = {
        "url": f"https://{basis.registry.file_host}/packages/changed.whl",
        "hashes": {"sha256": "f" * 64},
        "requires-python": ">=3.15",
        "yanked": "withdrawn",
        "extension": "changed",
    }[change]
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        basis.classify(_index(document["files"]))


def test_observation_ignores_index_serial_but_preserves_scope(basis):
    """Normal observes its version; acceptance preserves all prior versions."""
    old = copy.deepcopy(json.loads(basis.previous.body)["files"][0])
    old["filename"] = old["filename"].replace(
        basis.addition.witness.nbgv.pep440_version, "9.9.9"
    )
    previous = _index([*json.loads(basis.previous.body)["files"], old])
    scoped = replace(basis, previous=previous)
    response = exact_index(basis)
    document = json.loads(response.body)
    document["meta"]["_last-serial"] = 99
    response = replace(response, body=canonicalize(document))
    assert scoped.classify(response) == "exact"
    assert set(
        index_inventory(basis.registry, previous, basis.version_scope)
    ) == {json.loads(basis.previous.body)["files"][0]["filename"]}
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        replace(scoped, phase="native-c2").classify(response)


@pytest.mark.parametrize(
    "change",
    [
        "status",
        "content-type",
        "malformed",
        "oversized",
        "missing-prior",
        "extra",
        "foreign",
        "duplicate",
        "yanked",
        "digest",
        "file-host",
    ],
)
def test_observation_invalid_inventory_is_terminal_without_later_effects(  # noqa: C901 - independent invalid response cases
    basis, change
):
    """A queued later exact listing cannot repair a terminal response."""
    response = exact_index(basis)
    files = json.loads(response.body)["files"]
    if change == "status":
        response = replace(response, status=503)
    elif change == "content-type":
        response = replace(response, content_type="text/html")
    elif change == "malformed":
        response = replace(response, body=b"{")
    elif change == "oversized":
        response = replace(response, body=b"x" * (MAX_INDEX_BYTES + 1))
    else:
        if change == "missing-prior":
            files.pop(0)
        elif change in {"extra", "foreign", "duplicate"}:
            extra = copy.deepcopy(files[-1])
            if change == "extra":
                extra["filename"] = extra["filename"].replace(".tar.gz", ".zip")
            elif change == "foreign":
                extra["filename"] = "foreign-1.0.tar.gz"
            files.append(extra)
        elif change == "yanked":
            files[-1]["yanked"] = True
        elif change == "digest":
            files[-1]["hashes"]["sha256"] = "f" * 64
        elif change == "file-host":
            files[-1]["url"] = "https://foreign.invalid/packages/file"
        response = _index(files)
    boundary = Boundary(response, exact_index(basis))
    phase = IndexPhase(basis)
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        boundary.run(phase)
    assert len(boundary.calls) == 1
    assert len(boundary.responses) == 1
    assert phase.trace["terminal"] == "failed"
    assert boundary.saved["index-0.body"] == response.body


def test_observation_six_reads_exhaust_without_seventh(basis):
    """Six pending replies consume one phase and cannot donate another read."""
    boundary = Boundary(*([basis.previous] * 6), exact_index(basis))
    phase = IndexPhase(basis)
    with pytest.raises(ValueError, match="exhausted"):
        boundary.run(phase)
    assert [call[-1] for call in boundary.calls] == [
        100,
        110,
        120,
        130,
        140,
        150,
    ]
    assert len(boundary.responses) == 1
    assert phase.trace["terminal"] == "exhausted"
    with pytest.raises(ValueError, match="consumed"):
        boundary.run(phase)
    assert len(boundary.calls) == 6  # noqa: PLR2004 - accepted phase limit


def test_observation_sixth_read_can_complete_and_replay(basis):
    """The final permitted request remains usable without an extra read."""
    boundary = Boundary(*([basis.previous] * 5), exact_index(basis))
    phase = IndexPhase(basis)
    assert boundary.run(phase).body == exact_index(basis).body
    assert [call[-1] for call in boundary.calls] == [
        100,
        110,
        120,
        130,
        140,
        150,
    ]
    assert (
        replay_index_phase(phase.trace, basis).body == exact_index(basis).body
    )
    assert boundary.responses == []


def test_bootstrap_p2_preserves_404_body_through_offline_replay(basis):
    """An absent initial project can become exact within its bounded phase."""
    basis = replace(basis, phase="bootstrap-p2", previous=None)
    absent = PythonHttpResponse(404, b"not yet visible\n", "text/plain")
    boundary = Boundary(absent, exact_index(basis))
    phase = IndexPhase(basis)
    assert boundary.run(phase).body == exact_index(basis).body
    assert boundary.saved["index-0.body"] == b"not yet visible\n"
    assert [call[-1] for call in boundary.calls] == [100, 110]
    assert (
        replay_index_phase(phase.trace, basis).body == exact_index(basis).body
    )


@pytest.mark.parametrize(
    ("now", "admitted"),
    [(100, True), (159.999, True), (160, False), (170, False)],
)
def test_observation_window_uses_upload_completion_not_later_join(
    basis, now, admitted
):
    """A native winner's completion remains the anchor after both calls join."""
    phase = IndexPhase(replace(basis, phase="native-c7"))
    boundary = Boundary(exact_index(basis), now=now)
    if admitted:
        assert boundary.run(phase).body == exact_index(basis).body
        assert boundary.calls[0][-1] == now
    else:
        with pytest.raises(ValueError, match="exhausted"):
            boundary.run(phase)
        assert boundary.calls == []
    assert not any(event[0] == "wait" for event in boundary.events)


@pytest.mark.parametrize(
    ("outer", "oversleep", "durations"),
    [(110, 0, ()), (None, 50, ()), (None, 0, (30, 20))],
)
def test_observation_rechecks_deadlines_after_pending_wait(
    basis, outer, oversleep, durations
):
    """Outer expiry, scheduler delay and slow replies never extend admission."""
    phase = IndexPhase(replace(basis, outer_deadline=outer))
    boundary = Boundary(
        basis.previous,
        basis.previous,
        exact_index(basis),
        durations=durations,
        oversleep=oversleep,
    )
    with pytest.raises(ValueError, match="exhausted"):
        boundary.run(phase)
    assert [call[-1] for call in boundary.calls] == (
        [100, 140] if durations else [100]
    )
    assert phase.trace["terminal"] == "exhausted"


@pytest.mark.parametrize(
    ("duration", "accepted"), [(30, True), (30.001, False)]
)
def test_observation_admission_window_is_distinct_from_socket_bound(
    basis, duration, accepted
):
    """A read starting before the window closes retains its socket bound."""
    boundary = Boundary(exact_index(basis), now=159, durations=(duration,))
    phase = IndexPhase(basis)
    if accepted:
        assert boundary.run(phase).body == exact_index(basis).body
        assert (
            replay_index_phase(phase.trace, basis).body
            == exact_index(basis).body
        )
    else:
        with pytest.raises(ValueError, match="timing"):
            boundary.run(phase)
        assert boundary.saved["index-0.body"] == exact_index(basis).body
        assert phase.trace["terminal"] == "failed"
    assert len(boundary.calls) == 1


@pytest.mark.parametrize("instant", [float("nan"), float("inf"), -1, 99])
def test_observation_invalid_or_reversed_clock_never_reads(basis, instant):
    """Invalid trusted timing cannot obtain an index request."""
    boundary = Boundary(exact_index(basis), now=instant)
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        boundary.run(IndexPhase(basis))
    assert boundary.calls == []


def test_observation_clock_cannot_reverse_after_pending_completion(basis):
    """Waiting cannot hide a clock regression inside the upload window."""
    boundary = Boundary(basis.previous, exact_index(basis), durations=(3, 0))
    phase = IndexPhase(basis)

    def clock():
        if len(boundary.calls) == 1 and boundary.now == 103:  # noqa: PLR2004 - response finish
            boundary.now = 102
        return boundary.now

    with pytest.raises(ValueError, match=r"clock|timing"):
        phase.run(
            boundary,
            retain=boundary.retain,
            clock=clock,
            wait=boundary.wait,
        )
    assert len(boundary.calls) == 1
    assert not any(event[0] == "wait" for event in boundary.events)


@pytest.mark.parametrize("terminal_time", [99, float("nan")])
def test_observation_invalid_terminal_clock_cannot_return_success(
    basis, terminal_time
):
    """An exact index cannot excuse invalid final trusted clock evidence."""
    boundary = Boundary(exact_index(basis))
    phase = IndexPhase(basis)

    def clock():
        return terminal_time if boundary.calls else boundary.now

    with pytest.raises(ValueError, match=r"clock|timing"):
        phase.run(
            boundary,
            retain=boundary.retain,
            clock=clock,
            wait=boundary.wait,
        )
    assert len(boundary.calls) == 1
    assert json.loads(boundary.saved["phase.json"])["terminal"] == "failed"


@pytest.mark.parametrize(
    "change",
    [
        "classification",
        "status",
        "digest",
        "phase",
        "missing-previous",
        "p2-previous",
        "nonfinite-upload",
        "already-present",
    ],
)
def test_observation_requires_closed_definitive_upload_basis(basis, change):
    """Ineligible uploads and missing prior context fail before observation."""
    if change == "classification":
        basis = replace(
            basis, upload=replace(basis.upload, classification="ambiguous")
        )
    elif change == "status":
        basis = replace(basis, upload=replace(basis.upload, status=400))
    elif change == "digest":
        basis = replace(
            basis, upload=replace(basis.upload, response_digest=None)
        )
    elif change == "phase":
        basis = replace(basis, phase="native-c3")
    elif change == "missing-previous":
        basis = replace(basis, previous=None)
    elif change == "p2-previous":
        basis = replace(basis, phase="bootstrap-p2")
    elif change == "already-present":
        basis = replace(basis, previous=exact_index(basis))
    else:
        basis = replace(
            basis, upload=replace(basis.upload, finished=float("nan"))
        )
    with pytest.raises(ValueError, match=r"Python|bootstrap"):
        IndexPhase(basis)


def test_observation_retention_failure_stops_before_next_request(basis):
    """An unsafe persistence boundary preserves survivors without proceeding."""
    boundary = Boundary(basis.previous, exact_index(basis))
    phase = IndexPhase(basis)

    def retain(name, content):
        if name == "index-0.response.json":
            message = "fixture persistence failure"
            raise OSError(message)
        boundary.retain(name, content)

    with pytest.raises(OSError, match="persistence"):
        phase.run(
            boundary, retain=retain, clock=boundary.clock, wait=boundary.wait
        )
    assert boundary.saved["index-0.body"] == basis.previous.body
    assert len(boundary.calls) == 1
    assert json.loads(boundary.saved["phase.json"])["terminal"] == "failed"


def test_observation_transport_failure_is_terminal(basis):
    """A failed request cannot consume the queued recovery response."""
    boundary = Boundary(TimeoutError("fixture timeout"), exact_index(basis))
    phase = IndexPhase(basis)
    with pytest.raises(TimeoutError, match="fixture timeout"):
        boundary.run(phase)
    assert len(boundary.calls) == 1
    assert len(boundary.responses) == 1
    trace = json.loads(boundary.saved["phase.json"])
    assert trace["terminal"] == "failed"
    assert trace["reads"] == [
        {
            "ordinal": 0,
            "url": basis.registry.index_url,
            "admitted-at": 100,
            "start": None,
            "finish": None,
            "response": None,
            "classification": "transport-failed",
        }
    ]


@pytest.mark.parametrize("where", ["request", "wait"])
def test_observation_cancellation_retains_terminal_without_later_read(
    basis, where
):
    """Cancellation preserves reached evidence and never resumes the phase."""
    first = KeyboardInterrupt() if where == "request" else basis.previous
    boundary = Boundary(first, exact_index(basis))
    phase = IndexPhase(basis)

    def cancel_wait(_seconds):
        raise KeyboardInterrupt

    with pytest.raises(KeyboardInterrupt):
        phase.run(
            boundary,
            retain=boundary.retain,
            clock=boundary.clock,
            wait=cancel_wait,
        )
    assert len(boundary.calls) == 1
    assert len(boundary.responses) == 1
    trace = json.loads(boundary.saved["phase.json"])
    assert trace["terminal"] == "cancelled"
    assert trace["reads"][0]["classification"] == (
        "transport-failed" if where == "request" else "pending"
    )
    if where == "wait":
        assert boundary.saved["index-0.body"] == basis.previous.body
    with pytest.raises(ValueError, match="consumed"):
        boundary.run(phase)
    assert len(boundary.calls) == 1


@pytest.mark.parametrize(
    "tamper",
    [
        "flag",
        "raw",
        "digest",
        "missing",
        "missing-pending",
        "reordered",
        "surplus",
        "spacing",
        "window",
        "finish",
        "stopped",
        "clock",
        "upload",
        "profile",
        "previous",
        "addition",
        "unknown",
        "url",
        "ordinal",
        "admitted",
    ],
)
def test_observation_replay_rejects_unbound_or_inconsistent_trace(  # noqa: C901, PLR0912 - one independent tamper per case
    basis, tamper
):
    """Offline replay recomputes raw state and rejects forged success claims."""
    boundary = Boundary(basis.previous, exact_index(basis))
    phase = IndexPhase(basis)
    boundary.run(phase)
    trace = copy.deepcopy(phase.trace)
    if tamper == "flag":
        trace["reads"][0]["classification"] = "exact"
    elif tamper == "raw":
        trace["reads"][0]["response"]["body"] = base64.b64encode(
            exact_index(basis).body
        ).decode()
        trace["reads"][0]["response"]["digest"] = python_digest(
            exact_index(basis).body
        )
    elif tamper == "digest":
        trace["reads"][0]["response"]["digest"] = "sha256:" + "f" * 64
    elif tamper == "missing":
        trace["reads"].pop()
    elif tamper == "missing-pending":
        trace["reads"].pop(0)
    elif tamper == "reordered":
        trace["reads"].reverse()
    elif tamper == "surplus":
        trace["reads"].append(copy.deepcopy(trace["reads"][-1]))
    elif tamper == "spacing":
        trace["reads"][1]["start"] = 109.999
    elif tamper == "window":
        trace["reads"][1]["start"] = 160
        trace["reads"][1]["finish"] = 160
    elif tamper == "finish":
        trace["reads"][1]["finish"] = 109
    elif tamper == "stopped":
        trace["stopped-at"] = 109
    elif tamper == "clock":
        trace["clock"] = "UTC"
    elif tamper == "upload":
        trace["upload-finished"] = 101
    elif tamper == "profile":
        trace["profile-digest"] = "sha256:" + "f" * 64
    elif tamper == "previous":
        trace["previous"] = None
    elif tamper == "addition":
        trace["addition"]["digest"] = "sha256:" + "f" * 64
    elif tamper == "url":
        trace["reads"][0]["url"] = "https://foreign.invalid/simple/"
    elif tamper == "ordinal":
        trace["reads"][0]["ordinal"] = True
    elif tamper == "admitted":
        trace["reads"][1]["admitted-at"] = 109
    else:
        trace["unexpected"] = True
    with pytest.raises(ValueError, match=r"Python|JSON|Expecting|record"):
        replay_index_phase(trace, basis)


def test_observation_replay_never_waits_or_calls_transport(basis, monkeypatch):
    """Original responses replay with all external effects forbidden."""
    boundary = Boundary(basis.previous, exact_index(basis))
    phase = IndexPhase(basis)
    boundary.run(phase)
    trace = json.loads(boundary.saved["phase.json"])

    def denied(*_args, **_kwargs):
        pytest.fail("Offline replay attempted an external effect")

    monkeypatch.setattr("time.sleep", denied)
    monkeypatch.setattr("time.monotonic", denied)
    monkeypatch.setattr(boundary, "request", denied)
    assert replay_index_phase(trace, basis).body == exact_index(basis).body
    assert len(boundary.calls) == 2  # noqa: PLR2004 - pending and exact
