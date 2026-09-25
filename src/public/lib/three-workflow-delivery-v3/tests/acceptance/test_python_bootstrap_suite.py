"""Original artifact DAG, finite first-project sends and adversarial replay."""

# Protocol constants are kept visible in boundary assertions.
# ruff: noqa: PLR2004

from collections import Counter
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.acceptance.python_bootstrap_contract import (
    SENTINEL,
    WORKFLOW,
)
from three_workflow_delivery_v3.acceptance.python_bootstrap_suite import (
    BootstrapContext,
    audit,
    authorize,
    execute,
    marker,
    replay_audit,
)
from three_workflow_delivery_v3.acceptance.python_native import read_artifact
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    pack_bundle,
)
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import FakeHttp, _entry, _index
from . import test_python_bootstrap_fixture as fixtures_tests
from . import test_python_native_capture as capture_tests
from .test_python_bootstrap_fixture import (
    RUN,
    TOOLING,
    bootstrap_request,
    prepared_files,
)
from .test_python_native_hosted import (
    _ASSERTION,
    _GITHUB_TOKEN,
    _OIDC_TOKEN,
    github_facts,
    hosted_environment,
)
from .test_python_native_suite import _TOKEN, _consumer

deny_real_registry = capture_tests.deny_real_registry
bootstrap_fixtures = fixtures_tests.bootstrap_fixtures
actual_bootstrap_fixtures = fixtures_tests.actual_bootstrap_fixtures
native_python_provider_repository = (
    fixtures_tests.native_python_provider_repository
)
_ABSENT = PythonHttpResponse(404, b"original absent project", "text/html")
_OK = PythonHttpResponse(200, b"original accepted upload", "text/plain")


def files_from_directory(directory):
    """Read exact surviving evidence paths from a phase directory."""
    return {
        path.relative_to(directory).as_posix(): path.read_bytes()
        for path in directory.rglob("*")
        if path.is_file()
    }


def proof_responses(request):
    """Exercise the maximum eight raw current-run proof reads."""
    facts = github_facts(request, deployment_count=5)
    facts[0][1]["path"] = WORKFLOW
    return [
        PythonHttpResponse(200, canonicalize(document), "application/json")
        for _, document in facts
    ]


def observation_responses(fixtures, variants=("wheel", "sdist")):
    """Use actual original bytes behind the finite registry HTTP boundary."""
    items = [fixtures.distributions[variant] for variant in variants]
    registry = bootstrap_request(fixtures).registry
    return [
        _index([_entry(registry, item) for item in items]),
        *[
            PythonHttpResponse(200, item.content, "application/octet-stream")
            for item in items
        ],
    ]


class Scenario:
    """Run production phases against original locally.

    Run production phases against original locally archived external
    facts.
    """

    def __init__(self, fixtures, root):
        """Build one finite local artifact DAG with a.

        Build one finite local artifact DAG with a controllable
        runner clock.
        """
        self.fixtures = fixtures
        self.root = root
        self.now = 1000.0
        self.monotonic_now = 2000.0
        self.waits = []
        self.context = BootstrapContext(
            bootstrap_request(fixtures), RUN, TOOLING, {}
        )
        self.environment = hosted_environment()
        self.environment["GITHUB_WORKFLOW_REF"] = (
            f"hcoona/three/{WORKFLOW}@refs/heads/main"
        )
        self.environment["WDV3_APPROVAL_ENVIRONMENT_MARKER"] = SENTINEL
        self.prepared = self.artifact("prepared", prepared_files(fixtures))
        self.consumers = []

    def clock(self):
        """Return a trusted synthetic runner UTC instant."""
        return self.now

    def monotonic(self):
        """Keep process elapsed time separate from the runner UTC authority."""
        return self.monotonic_now

    def wait(self, seconds):
        """Advance both controlled clocks without real sleeping."""
        self.waits.append(seconds)
        self.now += seconds
        self.monotonic_now += seconds

    def artifact(self, role, files):
        """Archive and re-read exact immutable IDs, names and both digests."""
        name = "prepare" if role == "prepared" else role
        path = self.root / f"python-bootstrap-{name}-r{RUN}.zip"
        content = pack_bundle(files)
        path.write_bytes(content)
        identity = len(self.context.references) + 1
        reference = ArtifactReference(
            identity,
            python_digest(content),
            f"https://github.com/hcoona/three/actions/runs/{RUN}/artifacts/{identity}",
            path.name,
            python_digest(content),
        )
        self.context.references[role] = reference
        return read_artifact(path, reference, RUN)

    def authorize(self, responses=None):
        """Persist first proof/P0 and marker through actual original bundles."""
        self.auth_http = FakeHttp(
            *(responses or [*proof_responses(self.context.request), _ABSENT])
        )
        authorize(
            self.context,
            self.prepared,
            self.root / "result/authorization",
            self.auth_http,
            self.environment,
            clock=self.clock,
        )
        self.authorization = self.artifact(
            "authorization",
            files_from_directory(self.root / "result/authorization"),
        )
        self.marked = self.artifact(
            "marker",
            marker(
                self.context,
                self.prepared,
                self.authorization,
                clock=self.clock,
            ),
        )

    def execution_responses(self):
        """Describe only service replies; production chooses.

        Describe only service replies; production chooses order and
        payloads.
        """
        return [
            *proof_responses(self.context.request),
            _ABSENT,
            PythonHttpResponse(
                200, canonicalize({"value": _ASSERTION}), "application/json"
            ),
            PythonHttpResponse(
                200, canonicalize({"token": _TOKEN}), "application/json"
            ),
            _OK,
            *observation_responses(self.fixtures, ("wheel",)),
            _OK,
            *observation_responses(self.fixtures),
        ]

    def execute(self, responses=None):
        """Use the separately read-back durable authority and marker."""
        self.exec_http = FakeHttp(*(responses or self.execution_responses()))
        execute(
            self.context,
            self.prepared,
            self.authorization,
            self.marked,
            self.root / "result/execution",
            self.exec_http,
            self.environment,
            clock=self.clock,
            monotonic=self.monotonic,
            wait=self.wait,
        )
        self.result = self.artifact(
            "result", files_from_directory(self.root / "result")
        )

    def audit(self, responses=None, consumer=None):
        """Fresh public reads and two consumers form the fifth.

        Fresh public reads and two consumers form the fifth original
        artifact.
        """
        self.audit_http = FakeHttp(
            *(responses or observation_responses(self.fixtures))
        )
        audit(
            self.context,
            self.prepared,
            self.authorization,
            self.marked,
            self.result,
            self.root / "audit",
            self.audit_http,
            consumer=consumer or _consumer(self.consumers),
            clock=self.clock,
        )
        self.audited = self.artifact(
            "audit", files_from_directory(self.root / "audit")
        )

    def replay(self):
        """Use retained originals without the live fake HTTP boundaries."""
        return replay_audit(
            self.context,
            self.prepared,
            self.authorization,
            self.marked,
            self.result,
            self.audited,
        )


@pytest.fixture
def scenario(bootstrap_fixtures, tmp_path, deny_real_registry):  # noqa: ARG001
    """Never permit accidental real registry traffic from a modeled scenario."""
    return Scenario(bootstrap_fixtures, tmp_path)


def test_bootstrap_success_preserves_closed_sequence_and_budgets(scenario):
    """All five immutable originals prove exactly the admitted HTTP sequence."""
    scenario.authorize()
    scenario.execute()
    scenario.audit()
    replay = scenario.replay()
    uploads = [
        call
        for call in scenario.exec_http.calls
        if call[0] == "POST"
        and call[1] == scenario.context.request.registry.upload_url
    ]
    for call, variant, other in zip(
        uploads, ("wheel", "sdist"), ("sdist", "wheel"), strict=True
    ):
        original = scenario.fixtures.distributions[variant]
        assert original.content in call[3]
        assert f'filename="{original.filename}"'.encode() in call[3]
        assert scenario.fixtures.distributions[other].content not in call[3]
    records = [
        *parse_json_strict(scenario.authorization["requests.json"]),
        *parse_json_strict(scenario.result["execution/requests.json"]),
        *parse_json_strict(scenario.audited["requests.json"]),
    ]
    assert Counter(record["kind"] for record in records) == {
        "proof": 16,
        "index": 5,
        "file": 5,
        "upload": 2,
        "oidc": 1,
        "mint": 1,
    }
    assert [
        record["kind"]
        for record in parse_json_strict(
            scenario.result["execution/requests.json"]
        )
    ] == [
        *(["proof"] * 8),
        "index",
        "oidc",
        "mint",
        "upload",
        "index",
        "file",
        "upload",
        "index",
        "file",
        "file",
    ]
    assert len(scenario.context.references) == 5
    assert {item.variant for item in scenario.consumers} == {"wheel", "sdist"}
    assert [item.content for item in scenario.consumers] == [
        scenario.fixtures.distributions[v].content for v in ("wheel", "sdist")
    ]
    verdict = parse_canonical_json(replay["result.json"])
    assert verdict == {
        "status": "success",
        "evidence": "supplied-facts-only",
        "account-ownership": False,
        "native-admission": False,
        "live-enabled": False,
    }
    retained = b"".join(scenario.result.values())
    assert all(
        secret.encode() not in retained
        for secret in (_TOKEN, _GITHUB_TOKEN, _OIDC_TOKEN, _ASSERTION)
    )


def delayed_responses(scenario, p2_pending, p3_pending):
    """Insert approved pending replies before the two original readbacks."""
    immediate = scenario.execution_responses()
    wheel_index = observation_responses(scenario.fixtures, ("wheel",))[0]
    return [
        *immediate[:12],
        *([_ABSENT] * p2_pending),
        *immediate[12:15],
        *([wheel_index] * p3_pending),
        *immediate[15:],
    ]


def test_bootstrap_pending_phases_preserve_full_budget_and_offline_replay(
    scenario, monkeypatch
):
    """Both sixth reads may succeed, with no downloads during pending states."""
    scenario.authorize()
    scenario.execute(delayed_responses(scenario, 5, 5))
    scenario.audit()
    records = parse_json_strict(scenario.result["execution/requests.json"])
    assert [record["kind"] for record in records] == [
        *(["proof"] * 8),
        "index",
        "oidc",
        "mint",
        "upload",
        *(["index"] * 6),
        "file",
        "upload",
        *(["index"] * 6),
        "file",
        "file",
    ]
    all_records = [
        *parse_json_strict(scenario.authorization["requests.json"]),
        *records,
        *parse_json_strict(scenario.audited["requests.json"]),
    ]
    assert Counter(record["kind"] for record in all_records) == {
        "proof": 16,
        "index": 15,
        "file": 5,
        "upload": 2,
        "oidc": 1,
        "mint": 1,
    }
    assert scenario.waits == [10] * 10
    assert [record["monotonic-start"] for record in records[12:18]] == [
        2000,
        2010,
        2020,
        2030,
        2040,
        2050,
    ]
    assert [record["monotonic-start"] for record in records[20:26]] == [
        2050,
        2060,
        2070,
        2080,
        2090,
        2100,
    ]
    for phase in ("p2", "p3"):
        trace = parse_json_strict(
            scenario.result[f"execution/observation/{phase}/phase.json"]
        )
        assert [read["classification"] for read in trace["reads"]] == [
            *(["pending"] * 5),
            "exact",
        ]
        assert trace["terminal"] == "exact"
    uploads = [
        call
        for call in scenario.exec_http.calls
        if call[1] == scenario.context.request.registry.upload_url
    ]
    assert [
        scenario.fixtures.distributions[variant].content in call[3]
        for call, variant in zip(uploads, ("wheel", "sdist"), strict=True)
    ] == [True, True]

    def denied_wait(_seconds):
        pytest.fail("Offline bootstrap replay attempted to sleep")

    monkeypatch.setattr("time.sleep", denied_wait)
    assert (
        parse_json_strict(scenario.replay()["result.json"])["status"]
        == "success"
    )
    assert len(scenario.context.references) == 5


@pytest.mark.parametrize("phase", ["p2", "p3"])
def test_bootstrap_pending_exhaustion_never_reaches_later_effects(
    scenario, phase
):
    """A partial bootstrap remains failed after its sixth pending response."""
    scenario.authorize()
    responses = delayed_responses(
        scenario, 6 if phase == "p2" else 0, 6 if phase == "p3" else 0
    )
    with pytest.raises(ValueError, match="exhausted"):
        scenario.execute(responses)
    files = files_from_directory(scenario.root / "result/execution")
    records = parse_json_strict(files["requests.json"])
    assert Counter(record["kind"] for record in records) == {
        "proof": 8,
        "index": 7 if phase == "p2" else 8,
        "oidc": 1,
        "mint": 1,
        "upload": 1 if phase == "p2" else 2,
        **({"file": 1} if phase == "p3" else {}),
    }
    assert len(scenario.exec_http.responses) == (6 if phase == "p2" else 3)
    assert scenario.waits == [10] * 5
    trace = parse_json_strict(files[f"observation/{phase}/phase.json"])
    assert trace["terminal"] == "exhausted"
    assert [read["classification"] for read in trace["reads"]] == [
        "pending"
    ] * 6
    assert parse_json_strict(files["result.json"])["status"] == "failed"
    assert not (scenario.root / f"python-bootstrap-result-r{RUN}.zip").exists()


def test_bootstrap_utc_expiry_during_pending_wait_blocks_next_read(scenario):
    """Monotonic observation allowance cannot renew the UTC publisher grant."""
    scenario.authorize()

    def expiry_wait(seconds):
        scenario.waits.append(seconds)
        scenario.monotonic_now += seconds
        scenario.now = 1600

    scenario.wait = expiry_wait
    with pytest.raises(ValueError, match=r"expired|exhausted|window"):
        scenario.execute(delayed_responses(scenario, 1, 0))
    files = files_from_directory(scenario.root / "result/execution")
    records = parse_json_strict(files["requests.json"])
    assert len(records) == 13
    assert records[-1]["status"] == 404
    assert scenario.waits == [10]
    assert sum(record["kind"] == "upload" for record in records) == 1
    assert parse_json_strict(files["result.json"])["status"] == "failed"


@pytest.mark.parametrize(
    "change",
    [
        "missing-pending",
        "raw-pending",
        "spacing",
        "url",
        "surplus",
        "terminal-after-next-file",
    ],
)
def test_bootstrap_delayed_replay_binds_phase_to_original_journal(
    scenario, change
):
    """Exact files cannot hide altered intermediate observation history."""
    scenario.authorize()
    scenario.execute(delayed_responses(scenario, 1, 1))
    scenario.audit()
    prefix = "execution/observation/p2/"
    trace = parse_json_strict(scenario.result[prefix + "phase.json"])
    if change == "missing-pending":
        trace["reads"].pop(0)
    elif change == "raw-pending":
        scenario.result[prefix + "index-0.body"] = b"changed pending body"
    elif change == "spacing":
        trace["reads"][1]["start"] = 2009
    elif change == "url":
        trace["reads"][0]["url"] = "https://foreign.invalid/simple/"
    elif change == "terminal-after-next-file":
        records = parse_json_strict(scenario.result["execution/requests.json"])
        next_file = next(
            record for record in records if record["kind"] == "file"
        )
        trace["stopped-at"] = next_file["monotonic-start"] + 0.001
    else:
        scenario.result[prefix + "index-2.body"] = _ABSENT.body
    scenario.result[prefix + "phase.json"] = canonicalize(trace)
    with pytest.raises(
        (ValueError, KeyError, TypeError),
        match="observation termination"
        if change == "terminal-after-next-file"
        else None,
    ):
        scenario.replay()


@pytest.mark.parametrize(
    ("position", "response", "expected_uploads"),
    [
        (8, _index([]), 0),
        (11, PythonHttpResponse(400, b"File already exists", "text/plain"), 1),
        (11, TimeoutError("lost response"), 1),
        (12, _index([]), 1),
        (
            13,
            PythonHttpResponse(
                200, b"foreign original", "application/octet-stream"
            ),
            1,
        ),
        (14, PythonHttpResponse(503, b"original unavailable", "text/plain"), 2),
        (15, _ABSENT, 2),
    ],
)
def test_bootstrap_wheel_or_readback_failure_prevents_sdist(
    scenario, position, response, expected_uploads
):
    """First failed boundary stops subsequent sends and remains unsuccessful."""
    scenario.authorize()
    replies = scenario.execution_responses()
    replies[position] = response
    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        scenario.execute(replies)
    files = files_from_directory(scenario.root / "result/execution")
    records = parse_json_strict(files["requests.json"])
    assert len(scenario.exec_http.calls) == position + 1
    assert (
        sum(record["kind"] == "upload" for record in records)
        == expected_uploads
    )
    assert parse_canonical_json(files["result.json"])["status"] == "failed"
    if isinstance(response, PythonHttpResponse):
        assert files[f"http/{position}.body"] == response.body
    else:
        assert records[-1]["status"] is None
        assert f"http/{position}.body" not in files


@pytest.mark.parametrize("phase", ["authorization", "marker", "prepared"])
def test_bootstrap_authorization_and_marker_readback_precede_capability(
    scenario, phase
):
    """Tampered durable evidence admits no proof, credential request or POST."""
    scenario.authorize()
    chosen = getattr(scenario, "marked" if phase == "marker" else phase)
    chosen["binding.json"] = canonicalize({"relabelled": True})
    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        scenario.execute()
    assert scenario.exec_http.calls == []
    assert not (scenario.root / "result/execution").exists()


@pytest.mark.parametrize("phase", ["p0", "p1"])
def test_bootstrap_initial_state_requires_404_twice(scenario, phase):
    """An existing empty project does not satisfy the bootstrap prerequisite."""
    if phase == "p0":
        with pytest.raises(ValueError, match="not absent"):
            scenario.authorize(
                [*proof_responses(scenario.context.request), _index([])]
            )
        assert len(scenario.auth_http.calls) == 9
    else:
        scenario.authorize()
        replies = scenario.execution_responses()
        replies[8] = _index([])
        with pytest.raises(ValueError, match="not absent"):
            scenario.execute(replies)
        assert len(scenario.exec_http.calls) == 9
    assert not (scenario.root / f"python-bootstrap-result-r{RUN}.zip").exists()


@pytest.mark.parametrize(
    "change",
    [
        "raw-body",
        "upload-body",
        "extra-response",
        "late-start",
        "future-start",
        "late-finish",
        "renew-window",
        "platform",
        "lineage",
        "authorization-subtree",
        "consumer",
        "missing-execution",
    ],
)
def test_bootstrap_replay_rejects_forged_lineage_or_timing(  # noqa: C901, PLR0912
    scenario, change
):
    """Matching terminal package state cannot repair altered.

    Matching terminal package state cannot repair altered original
    history.
    """
    scenario.authorize()
    scenario.execute()
    scenario.audit()
    prefix = "execution/"
    if change == "raw-body":
        scenario.result[prefix + "http/12.body"] = b"{}"
    elif change in {
        "upload-body",
        "late-start",
        "future-start",
        "late-finish",
        "extra-response",
    }:
        records = parse_json_strict(scenario.result[prefix + "requests.json"])
        ordinal = 11
        if change == "upload-body":
            records[ordinal]["request-body-digest"] = "sha256:" + "f" * 64
        elif change == "late-start":
            records[ordinal]["start"] = 1600
            records[ordinal]["finish"] = 1600
        elif change == "future-start":
            records[ordinal]["start"] = 999
        elif change == "late-finish":
            records[ordinal]["finish"] = 1031
        else:
            ordinal = len(records)
            records.append(dict(records[-1]))
            scenario.result[f"{prefix}http/{ordinal}.body"] = scenario.result[
                f"{prefix}http/{ordinal - 1}.body"
            ]
        scenario.result[f"{prefix}http/{ordinal}.json"] = canonicalize(
            records[ordinal]
        )
        scenario.result[prefix + "requests.json"] = canonicalize(records)
    elif change == "renew-window":
        scenario.result[prefix + "window.json"] = canonicalize(
            {"created-at": 1001, "deadline": 1601}
        )
    elif change == "platform":
        platform = parse_canonical_json(
            scenario.result[prefix + "platform.json"]
        )
        platform["GITHUB_ACTOR_ID"] = "17"
        scenario.result[prefix + "platform.json"] = canonicalize(platform)
    elif change == "lineage":
        scenario.context.references["marker"] = replace(
            scenario.context.references["marker"], artifact_id=99
        )
    elif change == "authorization-subtree":
        scenario.result["authorization/approval.json"] = b"{}"
    elif change == "consumer":
        scenario.audited["consumer/sdist.json"] = b"{}"
    else:
        scenario.result = {
            key: value
            for key, value in scenario.result.items()
            if not key.startswith(prefix)
        }
    with pytest.raises((ValueError, KeyError, TypeError)):
        scenario.replay()


@pytest.mark.parametrize("change", ["index", "download", "consumer"])
def test_bootstrap_audit_requires_fresh_pair_and_two_consumers(
    scenario, change
):
    """P3 success cannot supply the distinct P4 observation.

    P3 success cannot supply the distinct P4 observation or consumer
    proof.
    """
    scenario.authorize()
    scenario.execute()
    replies = observation_responses(scenario.fixtures)
    if change == "index":
        replies[0] = _ABSENT
    elif change == "download":
        replies[2] = PythonHttpResponse(
            200, b"foreign", "application/octet-stream"
        )

    def failed_consumer(item):
        result = _consumer(scenario.consumers)(item)
        return replace(
            result,
            command_evidence=(
                canonicalize(
                    {
                        "argv": ["failed"],
                        "exit-code": 1,
                        "stdout": "",
                        "stderr": "",
                    }
                ),
            ),
        )

    with pytest.raises(
        ValueError,
        match=r"bootstrap|Python|native|fixture|consumer|source|foreign",
    ):
        scenario.audit(
            replies, failed_consumer if change == "consumer" else None
        )
    assert (
        parse_canonical_json(
            (scenario.root / "audit/result.json").read_bytes()
        )["status"]
        == "failed"
    )
    assert all(
        call[0] == "GET" and "Authorization" not in call[2]
        for call in scenario.audit_http.calls
    )


def test_real_bootstrap_final_audit_freshly_consumes_downloaded_pair(
    actual_bootstrap_fixtures,
    tmp_path,
    deny_real_registry,  # noqa: ARG001
):
    """Original actual builds pass two further isolated P4 clean consumers."""
    from three_workflow_delivery_v3.adapters.python import (  # noqa: PLC0415
        qualify_python_consumer,
    )

    _, _, fixtures = actual_bootstrap_fixtures
    local = Scenario(fixtures, tmp_path)
    local.authorize()
    local.execute()
    local.audit(consumer=qualify_python_consumer)
    for variant in ("wheel", "sdist"):
        proof = parse_canonical_json(local.audited[f"consumer/{variant}.json"])
        assert (
            proof["original-digest"] == fixtures.distributions[variant].digest
        )
        assert all(command["exit-code"] == 0 for command in proof["commands"])
    assert (
        parse_canonical_json(local.replay()["result.json"])["evidence"]
        == "supplied-facts-only"
    )


def test_bootstrap_audit_clock_cannot_predate_original_execution(scenario):
    """A backward audit clock fails before a fresh destination observation."""
    scenario.authorize()
    scenario.execute()
    scenario.now = 999.0
    with pytest.raises(ValueError, match="P4 precedes execution"):
        scenario.audit()
    assert scenario.audit_http.calls == []
    assert not (scenario.root / "audit").exists()


def test_bootstrap_full_p4_remains_valid_after_publisher_expiry(scenario):
    """Fresh credential-free consumers do not need or renew upload authority."""
    scenario.authorize()
    scenario.execute()
    scenario.now = 5000.0
    scenario.audit()
    assert len(scenario.consumers) == 2
    assert (
        parse_canonical_json(scenario.replay()["result.json"])["status"]
        == "success"
    )
    assert parse_canonical_json(scenario.authorization["window.json"]) == {
        "created-at": 1000,
        "deadline": 1600,
    }
