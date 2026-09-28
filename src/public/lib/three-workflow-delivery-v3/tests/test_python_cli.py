"""Hosted Python entry points using local immutable bytes and fake services."""

from dataclasses import replace
from datetime import timedelta
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3 import python_cli
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.adapters.python import PythonBuildResult
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.ci.python import (
    PythonCiPlan,
    run_python_ci_quality,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository.compiler import (
    _compilation_context_from_document,
)
from three_workflow_delivery_v3.repository.python_model import (
    PythonRepositoryModelSnapshot,
)
from three_workflow_delivery_v3.repository.python_provider import python_digest

from .adapters.test_pypi import FakeHttp, _distribution, _index
from .python_fixtures import (
    NOW,
    RUN_ID,
    TARGET,
    consumer,
    governance,
    model,
    originals,
    qualification,
    reference,
)
from .release.python_fixtures import prepared_publication
from .release.test_python_finalizer import _exact_inputs, _result
from .release.test_python_publication import _readback
from .repository.test_python_model import _admitted, _contents

_ARTIFACT_ID = 1001


@pytest.fixture
def hosted(monkeypatch, tmp_path):
    """Set the exact current CI platform identity and deny real services."""
    values = {
        "GITHUB_SHA": TARGET,
        "GITHUB_RUN_ID": str(RUN_ID),
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_EVENT_NAME": "pull_request",
        "GITHUB_REF": "refs/pull/843/merge",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_WORKFLOW_SHA": TARGET,
        "GITHUB_WORKFLOW_REF": "hcoona/three/"
        + python_cli.PYTHON_WORKFLOW
        + "@refs/heads/main",
        "GITHUB_OUTPUT": str(tmp_path / "outputs"),
        "GITHUB_JOB": "plan-python-ci",
    }
    for key, value in values.items():
        monkeypatch.setenv(key, value)
    monkeypatch.delenv("WDV3_REFERENCES", raising=False)

    def denied(*_args, **_kwargs):
        pytest.fail("Real GitHub, registry and OIDC access is forbidden")

    monkeypatch.setattr(python_cli, "_github", denied)
    monkeypatch.setattr(python_cli, "PythonHttpsTransport", denied)
    return tmp_path


def _store(directory, document, name="model.json", artifact_id=1001):
    content = canonicalize(document)
    (directory / name).write_bytes(content)
    digest = python_digest(content)
    return ArtifactReference(
        artifact_id,
        digest,
        f"https://example.invalid/artifacts/{artifact_id}",
        name,
        digest,
    )


def _run(
    directory, command, references=None, purpose="ci-pr-slice-shadow", extra=()
):
    return python_cli.main(
        [
            command,
            "--purpose",
            purpose,
            "--directory",
            str(directory),
            "--output",
            str(directory / "result.json"),
            "--references",
            canonicalize(references or {}).decode(),
            *extra,
        ]
    )


def _live(monkeypatch):
    monkeypatch.setenv("GITHUB_EVENT_NAME", "workflow_dispatch")
    monkeypatch.setenv("GITHUB_REF", "refs/heads/main")


@pytest.fixture
def publication_files(hosted, monkeypatch):
    """Materialize a synthetic DAG with actual single-file transport digests."""
    _live(monkeypatch)
    monkeypatch.setenv("GITHUB_JOB", "publish-python")
    monkeypatch.setenv("GITHUB_ENV", str(hosted / "environment"))
    monkeypatch.setenv("WDV3_PYPI_TOKEN", "pypi-test-only-token")
    marker, _, _, distributions = prepared_publication()
    authorization = marker.authorization
    bundle = authorization.bundle
    publication = bundle.snapshot
    observation = publication.observation
    decision = observation.decision
    refs = {}

    def store(role, content, name=None):
        artifact_id = _ARTIFACT_ID + len(refs)
        name = name or role + ".json"
        (hosted / name).write_bytes(content)
        digest = python_digest(content)
        ref = ArtifactReference(
            artifact_id,
            digest,
            f"https://example.invalid/artifacts/{artifact_id}",
            name,
            digest,
        )
        refs[role] = ref.to_document()
        return ref

    model_ref = store(
        "model", canonicalize(decision.snapshot.model.to_document())
    )
    store("intent", canonicalize(decision.snapshot.intent.to_document()))
    snapshot = replace(decision.snapshot, model_reference=model_ref)
    store("qualification", canonicalize(snapshot.to_document()))
    artifacts = []
    for artifact, distribution in zip(
        decision.artifacts, distributions, strict=True
    ):
        ref = store(
            distribution.variant, distribution.content, distribution.filename
        )
        transport = replace(
            artifact.transport,
            artifact_id=ref.artifact_id,
            artifact_name=ref.payload_path,
            artifact_url=ref.artifact_url,
            transport_digest=ref.artifact_digest,
        )
        artifacts.append(replace(artifact, reference=ref, transport=transport))
    evidence = python_cli.qualify_python_release(
        snapshot,
        tuple(artifacts),
        tuple(d.content for d in distributions),
        consumer=consumer,
    )
    decision = replace(decision, snapshot=snapshot, evidence=evidence)
    store("artifacts", canonicalize([a.to_document() for a in artifacts]))
    store("quality", canonicalize([e.to_document() for e in evidence]))
    decision_ref = store("decision", canonicalize(decision.to_document()))
    observation = replace(
        observation, decision=decision, decision_reference=decision_ref
    )
    observation_ref = store(
        "observation", canonicalize(observation.to_document())
    )
    publication = replace(
        publication,
        observation=observation,
        observation_reference=observation_ref,
    )
    publication_ref = store(
        "publication", canonicalize(publication.to_document())
    )
    summary_ref = store("summary", bundle.summary, "summary.txt")
    bundle = replace(
        bundle,
        snapshot=publication,
        snapshot_reference=publication_ref,
        summary_reference=summary_ref,
    )
    bundle_ref = store("bundle", canonicalize(bundle.to_document()))
    authorization = replace(
        authorization, bundle=bundle, bundle_reference=bundle_ref
    )
    authorization_ref = store(
        "authorization", canonicalize(authorization.to_document())
    )
    marker = replace(
        marker,
        authorization=authorization,
        authorization_reference=authorization_ref,
    )
    marker_ref = store("marker", canonicalize(marker.to_document()))
    monkeypatch.setattr(
        python_cli,
        "datetime",
        SimpleNamespace(now=lambda _zone: NOW + timedelta(seconds=3)),
    )
    # Verify fixture lineage through the real loader, not a substitute loader.
    inputs = python_cli.PythonInputs(
        hosted, "live-release", canonicalize(refs).decode()
    )
    assert inputs.marker() == marker
    return refs, marker, marker_ref, distributions


@pytest.mark.parametrize(
    ("command", "role"),
    [
        ("token", "authorization"),
        ("marker", "authorization"),
        ("execute", "marker"),
    ],
)
@pytest.mark.parametrize(
    "failure",
    [
        "missing-reference",
        "missing-readback",
        "corrupt-readback",
        "foreign-digest",
        "foreign-predecessor",
    ],
)
def test_python_cli_persisted_barrier_rejects_before_native_effects(
    hosted, publication_files, command, role, failure
):
    """Unbound or unreadable authority cannot reach credentials or uploads."""
    refs, _, _, _ = publication_files
    ref = refs[role]
    if failure == "missing-reference":
        del refs[role]
    elif failure == "missing-readback":
        (hosted / ref["payload-path"]).rename(hosted / "unavailable.json")
    elif failure == "corrupt-readback":
        (hosted / ref["payload-path"]).write_bytes(b"substituted")
    elif failure == "foreign-digest":
        ref["artifact-digest"] = "sha256:" + "f" * 64
    else:
        document = parse_canonical_json(
            (hosted / ref["payload-path"]).read_bytes()
        )
        predecessor = (
            "bundle-reference"
            if role == "authorization"
            else "authorization-reference"
        )
        document[predecessor]["artifact-id"] += 1
        refs[role] = _store(
            hosted, document, ref["payload-path"], ref["artifact-id"]
        ).to_document()
    assert _run(hosted, command, refs, purpose="live-release") == 1
    assert not (hosted / "environment").exists()
    assert not (hosted / "upload-started").exists()
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize("failure", ["governance", "oidc", "mint"])
def test_python_cli_token_failure_leaves_no_exported_credential(
    hosted, publication_files, monkeypatch, failure, capsys
):
    """Durable Authorization cannot bypass fresh controls or token errors."""
    refs, _, _, _ = publication_files
    calls = []
    transport = FakeHttp(PythonHttpResponse(403, b"denied", "text/plain"))

    def fresh(_registry, *, initial):
        calls.append("governance")
        if failure == "governance":
            message = "changed controls"
            raise ValueError(message)
        return initial

    def oidc(*_args):
        calls.append("oidc")
        if failure == "oidc":
            message = "unavailable OIDC"
            raise ValueError(message)
        return "test-only-assertion"

    monkeypatch.setattr(
        python_cli, "_github", lambda: SimpleNamespace(governance=fresh)
    )
    monkeypatch.setattr(python_cli, "obtain_python_oidc_assertion", oidc)
    monkeypatch.setattr(python_cli, "PythonHttpsTransport", lambda: transport)
    assert _run(hosted, "token", refs, purpose="live-release") == 1
    assert calls == (
        ["governance"] if failure == "governance" else ["governance", "oidc"]
    )
    assert [call[:2] for call in transport.calls] == (
        [("POST", "https://test.pypi.org/_/oidc/mint-token")]
        if failure == "mint"
        else []
    )
    assert not (hosted / "environment").exists()
    assert not (hosted / "result.json").exists()
    assert "test-only-assertion" not in capsys.readouterr().out


@pytest.mark.parametrize("failure", ["governance", "unavailable", "partial"])
def test_python_cli_marker_rejects_fresh_drift_without_upload(
    hosted, publication_files, monkeypatch, failure
):
    """Issued credentials cannot authorize a drifted or unreadable set."""
    refs, marker, _, distributions = publication_files
    del refs["marker"]
    transport = FakeHttp(
        *(
            _readback(marker.absence.registry, distributions[:1])
            if failure == "partial"
            else [PythonHttpResponse(503, b"unavailable", "text/plain")]
        )
    )

    def fresh(_registry, *, initial):
        decision = marker.authorization.bundle.snapshot.observation.decision
        assert initial == decision.snapshot.governance
        if failure == "governance":
            message = "changed Governance or configuration"
            raise ValueError(message)
        return governance(observed_at=NOW + timedelta(seconds=3))

    monkeypatch.setattr(
        python_cli, "_github", lambda: SimpleNamespace(governance=fresh)
    )
    monkeypatch.setattr(python_cli, "PythonHttpsTransport", lambda: transport)
    assert _run(hosted, "marker", refs, purpose="live-release") == 1
    assert not (hosted / "result.json").exists()
    assert not (hosted / "upload-started").exists()
    assert all(call[0] == "GET" for call in transport.calls)
    assert bool(transport.calls) is (failure != "governance")


@pytest.mark.parametrize(
    "lost_result", ["missing", "unbound-file", "failed-bind"]
)
def test_python_cli_lost_result_preserves_durable_marker_scalar(
    hosted, publication_files, lost_result
):
    """Only explicit durable references participate in terminal selection."""
    refs, marker, marker_ref, _ = publication_files
    if lost_result != "missing":
        result = _result(marker, marker_ref)
        path = hosted / "unbound-result.json"
        path.write_bytes(canonicalize(result.to_document()))
        if lost_result == "failed-bind":
            assert (
                _run(
                    hosted,
                    "bind",
                    refs,
                    purpose="live-release",
                    extra=(
                        "--role",
                        "result",
                        "--payload",
                        str(path),
                        "--artifact-id",
                        "2001",
                        "--artifact-digest",
                        "sha256:" + "f" * 64,
                        "--artifact-url",
                        "https://example.invalid/artifacts/2001",
                    ),
                )
                == 1
            )
    assert _run(hosted, "terminal", refs, purpose="live-release") == 0
    assert (
        hosted / "outputs"
    ).read_text() == "terminal-reference=" + canonicalize(
        marker_ref.to_document()
    ).decode() + "\n"


def test_python_cli_durable_result_takes_terminal_precedence(
    hosted, publication_files
):
    """A bound Result supersedes its marker in the actual terminal entry."""
    refs, marker, marker_ref, _ = publication_files
    result_ref = _store(
        hosted,
        _result(marker, marker_ref).to_document(),
        "publication-result.json",
        2001,
    )
    refs["result"] = result_ref.to_document()
    assert _run(hosted, "terminal", refs, purpose="live-release") == 0
    assert (
        hosted / "outputs"
    ).read_text() == "terminal-reference=" + canonicalize(
        result_ref.to_document()
    ).decode() + "\n"


@pytest.mark.parametrize("conclusion", ["success", "failure", "cancelled"])
def test_python_cli_marker_terminal_finalizes_as_uncertain(
    hosted, publication_files, conclusion
):
    """The actual Finalizer entry cannot turn lost Result into success."""
    refs, _, marker_ref, _ = publication_files
    assert (
        _run(
            hosted,
            "finalize",
            refs,
            purpose="live-release",
            extra=(
                "--publisher-conclusion",
                conclusion,
                "--publication-step-outcome",
                conclusion,
                "--terminal-reference",
                canonicalize(marker_ref.to_document()).decode(),
                "--observation-conclusion",
                "success",
            ),
        )
        == 1
    )
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    assert document["disposition"] == "unknown"
    assert document["possibly-mutated"] is True
    assert (
        document["direct-predecessor"]["reference"] == marker_ref.to_document()
    )


def test_python_cli_lost_terminal_output_cannot_issue_an_outcome(
    hosted, publication_files
):
    """A lost scalar fails admission despite durable marker bytes."""
    refs, _, _, _ = publication_files
    assert (
        _run(
            hosted,
            "finalize",
            refs,
            purpose="live-release",
            extra=(
                "--publisher-conclusion",
                "failure",
                "--publication-step-outcome",
                "success",
                "--observation-conclusion",
                "success",
            ),
        )
        == 1
    )
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize("command", ["token", "marker", "execute"])
def test_python_cli_admitted_publication_stage_reaches_only_its_native_boundary(
    hosted, publication_files, monkeypatch, command
):
    """Real stages admit the full DAG before bounded fake native effects."""
    refs, marker, _, distributions = publication_files
    registry = marker.absence.registry
    ok = PythonHttpResponse(200, b"created", "text/plain")
    token = "pypi-test-only-short-lived"  # noqa: S105
    responses = {
        "token": [
            PythonHttpResponse(
                200, canonicalize({"token": token}), "application/json"
            )
        ],
        "marker": [_index([])],
        "execute": [
            ok,
            *_readback(registry, distributions[:1]),
            ok,
            *_readback(registry, distributions),
        ],
    }
    transport = FakeHttp(*responses[command])
    calls = []

    def fresh(_registry, *, initial):
        calls.append("governance")
        assert initial.registry == registry
        return governance(observed_at=NOW + timedelta(seconds=3))

    def oidc(name, _environment, native_transport):
        calls.append("oidc")
        assert name == "testpypi"
        assert native_transport is transport
        return "test-only-assertion"

    monkeypatch.setattr(
        python_cli, "_github", lambda: SimpleNamespace(governance=fresh)
    )
    monkeypatch.setattr(python_cli, "obtain_python_oidc_assertion", oidc)
    monkeypatch.setattr(python_cli, "PythonHttpsTransport", lambda: transport)
    assert _run(hosted, command, refs, purpose="live-release") == 0
    assert not transport.responses
    if command == "token":
        assert calls == ["governance", "oidc"]
        assert (
            hosted / "environment"
        ).read_text() == f"WDV3_PYPI_TOKEN={token}\n"
        assert not (hosted / "result.json").exists()
    elif command == "marker":
        assert calls == ["governance"]
        document = parse_canonical_json((hosted / "result.json").read_bytes())
        assert document["authorization-reference"] == refs["authorization"]
        assert [call[0] for call in transport.calls] == ["GET"]
    else:
        assert calls == []
        document = parse_canonical_json((hosted / "result.json").read_bytes())
        assert document["result"] == "published"
        assert document["mutation-marker-reference"] == refs["marker"]
        assert [call[0] for call in transport.calls] == [
            "POST",
            "GET",
            "GET",
            "POST",
            "GET",
            "GET",
            "GET",
        ]
        assert (hosted / "upload-started").exists()


def test_python_cli_ci_request_binds_merge_target_and_full_manifest(hosted):
    """The hosted request uses tested GITHUB_SHA and current-run CI identity."""
    assert _run(hosted, "request") == 0
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    context = document["context"]
    assert context["target"] == TARGET
    assert context["purpose"] == "ci-pr-slice-shadow"
    assert context["workflow-run-id"] == RUN_ID
    assert context["run-attempt"] == 1
    assert context["control"] == "workflow-delivery-v3:" + TARGET
    assert not (hosted / "intent.json").exists()
    assert not (hosted / "governance.json").exists()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_EVENT_NAME", "pull_request_target"),
        ("GITHUB_REF", "refs/heads/main"),
        ("GITHUB_REF", "refs/pull/843/head"),
    ],
)
def test_python_cli_pr_request_rejects_foreign_execution_identity(
    hosted, monkeypatch, field, value
):
    """A foreign event or non-merge target cannot issue the CI request."""
    monkeypatch.setenv(field, value)
    assert _run(hosted, "request") == 1
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/feature"),
        ("GITHUB_ACTOR", "other"),
        ("GITHUB_WORKFLOW_SHA", "f" * 40),
        ("GITHUB_WORKFLOW_REF", "hcoona/three/foreign.yml@refs/heads/main"),
    ],
)
def test_python_cli_live_identity_rejects_before_governance_access(
    hosted, monkeypatch, field, value
):
    """Only a new owner dispatch on reviewed main can approach Live controls."""
    _live(monkeypatch)
    monkeypatch.setenv(field, value)
    assert (
        _run(
            hosted,
            "request",
            purpose="live-release",
            extra=("--registry", "testpypi"),
        )
        == 1
    )
    assert not (hosted / "intent.json").exists()


def test_python_cli_disabled_governance_exits_before_request_artifacts(
    hosted, monkeypatch, capsys
):
    """A disabled destination creates no Live authority or sensitive output."""
    _live(monkeypatch)
    calls = []

    class GitHub:
        def governance(self, registry):
            calls.append(registry.name)
            message = "disabled configuration contains sensitive-native-body"
            raise ValueError(message)

    monkeypatch.setattr(python_cli, "_github", GitHub)
    assert (
        _run(
            hosted,
            "request",
            purpose="live-release",
            extra=("--registry", "testpypi"),
        )
        == 1
    )
    assert calls == ["testpypi"]
    assert not (hosted / "intent.json").exists()
    assert not (hosted / "governance.json").exists()
    assert not (hosted / "result.json").exists()
    output = capsys.readouterr().out
    assert output == "Python stage rejected: ValueError\n"
    assert "sensitive-native-body" not in output


@pytest.mark.parametrize("document", [[], {"unknown-role": {}}, {"model": {}}])
def test_python_cli_rejects_invalid_reference_map_without_execution(
    hosted, document
):
    """No directory listing or role guessing can replace explicit references."""
    assert (
        python_cli.main(
            [
                "plan",
                "--purpose",
                "ci-pr-slice-shadow",
                "--directory",
                str(hosted),
                "--references",
                canonicalize(document).decode(),
                "--output",
                str(hosted / "result.json"),
            ]
        )
        == 1
    )
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize("change", ["payload", "transport-digest", "path"])
def test_python_cli_input_content_rejects_substitution_or_escape(
    hosted, change
):
    """Both immutable digests and the bounded payload path must agree."""
    ref = _store(hosted, {})
    document = ref.to_document()
    if change == "payload":
        (hosted / ref.payload_path).write_bytes(b"altered")
    elif change == "transport-digest":
        document["artifact-digest"] = "sha256:" + "f" * 64
    else:
        document["payload-path"] = "../outside"
    inputs = python_cli.PythonInputs(
        hosted, "ci-pr-slice-shadow", canonicalize({"model": document}).decode()
    )
    with pytest.raises(ValueError, match=r"immutable output|payload_path"):
        inputs.content("model")


def test_python_cli_plan_selects_full_current_target_and_emits_boolean(hosted):
    """Hosted CI uses full scope and the exact current immutable Model."""
    source = model("ci-pr-slice-shadow")
    ref = _store(hosted, source.to_document())
    assert _run(hosted, "plan", {"model": ref.to_document()}) == 0
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    assert document == PythonCiPlan(source, ref, None).to_document()
    assert (hosted / "outputs").read_text() == "selected=true\n"


@pytest.mark.parametrize("change", ["run", "purpose", "control"])
def test_python_cli_model_rejects_foreign_current_binding(hosted, change):
    """Native record validity alone cannot replace current hosted identity."""
    source = model(
        "slice-validation" if change == "purpose" else "ci-pr-slice-shadow",
        run_id=RUN_ID + 1 if change == "run" else RUN_ID,
        control="foreign" if change == "control" else None,
    )
    ref = _store(hosted, source.to_document())
    assert _run(hosted, "plan", {"model": ref.to_document()}) == 1
    assert not (hosted / "result.json").exists()


@pytest.fixture(params=["ci-pr-slice-shadow", "live-release"])
def decision_plan(hosted, monkeypatch, request):
    """Persist valid Plan authority without bound Build or quality output."""
    purpose = request.param
    if purpose == "live-release":
        _live(monkeypatch)
        original, payloads, _ = qualification()
        source = original.snapshot.model
        model_ref = _store(hosted, source.to_document())
        snapshot = replace(original.snapshot, model_reference=model_ref)
        intent_ref = _store(
            hosted, snapshot.intent.to_document(), "intent.json", 1002
        )
        snapshot_ref = _store(
            hosted, snapshot.to_document(), "qualification.json", 1003
        )
        refs = {
            "model": model_ref.to_document(),
            "intent": intent_ref.to_document(),
            "qualification": snapshot_ref.to_document(),
        }
        evidence = python_cli.qualify_python_release(
            snapshot, original.artifacts, payloads, consumer=consumer
        )
    else:
        source = model(purpose)
        model_ref = _store(hosted, source.to_document())
        plan = PythonCiPlan(source, model_ref, None)
        plan_ref = _store(hosted, plan.to_document(), "plan.json", 1002)
        refs = {"ci-plan": plan_ref.to_document()}
        artifacts, payloads, _ = originals(source)
        evidence = run_python_ci_quality(
            plan, artifacts, payloads, consumer=consumer
        )
    return purpose, refs, evidence


@pytest.mark.parametrize("quality_state", ["absent", "unbound-file"])
def test_python_cli_missing_evidence_writes_incomplete_decision(
    hosted, decision_plan, quality_state
):
    """Build failure or lost quality persistence retains Plan-bound Decision."""
    purpose, refs, evidence = decision_plan
    if quality_state == "unbound-file":
        (hosted / "quality.json").write_bytes(
            canonicalize([e.to_document() for e in evidence])
        )
    assert not {"wheel", "sdist", "artifacts", "quality"}.intersection(refs)
    assert _run(hosted, "decision", refs, purpose=purpose) == 1
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    assert document["result"] == "incomplete"
    assert (
        hosted / "outputs"
    ).read_text() == "qualification-result=incomplete\n"


@pytest.mark.parametrize(
    "quality_state", ["wrong-shape", "invalid-record", "corrupt-readback"]
)
def test_python_cli_present_invalid_evidence_cannot_be_treated_as_missing(
    hosted, decision_plan, quality_state
):
    """Malformed present Evidence cannot manufacture an empty admitted set."""
    purpose, refs, evidence = decision_plan
    document = {} if quality_state == "wrong-shape" else [{}]
    if quality_state == "corrupt-readback":
        document = [e.to_document() for e in evidence]
    ref = _store(hosted, document, "quality.json", 1004)
    refs["quality"] = ref.to_document()
    if quality_state == "corrupt-readback":
        (hosted / "quality.json").write_bytes(b"substituted")
    assert _run(hosted, "decision", refs, purpose=purpose) == 1
    assert not (hosted / "result.json").exists()
    assert not (hosted / "outputs").exists()


@pytest.mark.parametrize("result", ["passed", "failed"])
def test_python_cli_valid_evidence_preserves_authoritative_decision_result(
    hosted, decision_plan, result
):
    """Durable valid quality distinguishes business failure from absence."""
    purpose, refs, evidence = decision_plan
    if result == "failed":
        evidence = (replace(evidence[0], result="failed"), *evidence[1:])
    ref = _store(
        hosted, [e.to_document() for e in evidence], "quality.json", 1004
    )
    refs["quality"] = ref.to_document()
    assert _run(hosted, "decision", refs, purpose=purpose) == (
        0 if result == "passed" else 1
    )
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    assert document["result"] == result
    assert (
        hosted / "outputs"
    ).read_text() == f"qualification-result={result}\n"


@pytest.mark.parametrize(
    "command",
    [
        "observe",
        "prepare",
        "summary",
        "bundle",
        "authorize",
        "token",
        "marker",
        "execute",
        "exact-proof",
    ],
)
def test_python_cli_ci_cannot_enter_publication_stage(hosted, command):
    """CI purpose fails before any credential or native publication boundary."""
    assert _run(hosted, command) == 1
    assert not (hosted / "result.json").exists()


def test_python_cli_bind_outputs_actual_single_file_reference(
    hosted, monkeypatch
):
    """Binding preserves service identity and the exact payload hash."""
    monkeypatch.setenv("GITHUB_JOB", "compile-python")
    path = hosted / "model.json"
    path.write_bytes(canonicalize({"test": "payload"}))
    digest = python_digest(path.read_bytes())
    assert (
        _run(
            hosted,
            "bind",
            extra=(
                "--role",
                "model",
                "--payload",
                str(path),
                "--artifact-id",
                "1001",
                "--artifact-digest",
                digest.removeprefix("sha256:"),
                "--artifact-url",
                "https://example.invalid/artifacts/1001",
            ),
        )
        == 0
    )
    document = parse_canonical_json((hosted / "result.json").read_bytes())
    assert document["model"]["artifact-id"] == _ARTIFACT_ID
    assert document["model"]["artifact-digest"] == digest
    assert document["model"]["payload-digest"] == digest
    assert document["model"]["payload-path"] == "model.json"
    assert "artifact-ids=1001\n" in (hosted / "outputs").read_text()


@pytest.mark.parametrize("change", ["producer", "digest", "replacement"])
def test_python_cli_bind_rejects_foreign_or_replaced_upload(
    hosted, monkeypatch, change
):
    """No stage may claim another producer or overwrite a prior role."""
    monkeypatch.setenv(
        "GITHUB_JOB", "foreign" if change == "producer" else "compile-python"
    )
    ref = _store(hosted, {})
    refs = {"model": ref.to_document()} if change == "replacement" else {}
    digest = "sha256:" + "f" * 64 if change == "digest" else ref.payload_digest
    assert (
        _run(
            hosted,
            "bind",
            refs,
            extra=(
                "--role",
                "model",
                "--payload",
                str(hosted / "model.json"),
                "--artifact-id",
                "1001",
                "--artifact-digest",
                digest,
                "--artifact-url",
                ref.artifact_url,
            ),
        )
        == 1
    )
    assert not (hosted / "outputs").exists()


def test_python_cli_terminal_without_record_is_explicit_null(hosted):
    """Publisher output absence is represented by the canonical scalar null."""
    assert _run(hosted, "terminal") == 0
    assert (hosted / "outputs").read_text() == "terminal-reference=null\n"


def test_python_cli_current_dag_ci_smoke_with_fake_native_stages(  # noqa: PLR0915
    hosted, monkeypatch
):
    """Real CLI and record admission join one current CI DAG."""
    references = {}
    native_calls = []
    next_id = _ARTIFACT_ID

    def bind(role, path, producer):
        nonlocal next_id
        monkeypatch.setenv("GITHUB_JOB", producer)
        digest = python_digest(path.read_bytes())
        assert (
            _run(
                hosted,
                "bind",
                references,
                extra=(
                    "--role",
                    role,
                    "--payload",
                    str(path),
                    "--artifact-id",
                    str(next_id),
                    "--artifact-digest",
                    digest,
                    "--artifact-url",
                    f"https://example.invalid/artifacts/{next_id}",
                ),
            )
            == 0
        )
        references.update(
            parse_canonical_json((hosted / "result.json").read_bytes())
        )
        next_id += 1

    def stage(command, role, producer):
        assert _run(hosted, command, references) == 0
        path = hosted / (role + ".json")
        (hosted / "result.json").rename(path)
        bind(role, path, producer)

    def provider(_root, binding, checkout):
        native_calls.append("provider")
        request = parse_canonical_json((hosted / "request.json").read_bytes())
        facts = _admitted(
            _compilation_context_from_document(request["context"]), _contents()
        )
        assert facts.result.binding == binding
        assert checkout.fetch_depth == 0
        assert not checkout.credentials_persisted
        return facts.result

    def compile_model(_root, facts):
        native_calls.append("compile")
        return PythonRepositoryModelSnapshot(
            facts.manifest.context,
            facts.result,
            facts.request_reference,
            facts.result_reference,
        )

    def build(_root, request):
        native_calls.append("build")
        distributions = tuple(
            _distribution(v, request.witness) for v in ("wheel", "sdist")
        )
        return PythonBuildResult(
            distributions,
            "sha256:" + "f" * 64,
            canonicalize({"producer": "fake-only"}),
            (),
        )

    def quality(plan, artifacts, payloads):
        native_calls.append("quality")
        return run_python_ci_quality(
            plan, artifacts, payloads, consumer=consumer
        )

    monkeypatch.setattr(python_cli, "provide_python_repository_facts", provider)
    monkeypatch.setattr(
        python_cli, "compile_python_repository_model", compile_model
    )
    monkeypatch.setattr(python_cli, "build_python_distributions", build)
    monkeypatch.setattr(python_cli, "run_python_ci_quality", quality)
    stage("request", "request", "request-python")
    stage("provider", "provider", "discover-python")
    stage("compile", "model", "compile-python")
    stage("plan", "ci-plan", "plan-python-ci")
    assert _run(hosted, "build", references) == 0
    (hosted / "result.json").rename(hosted / "build-report.json")
    for role, suffix in (("wheel", ".whl"), ("sdist", ".tar.gz")):
        path = next(p for p in hosted.iterdir() if p.name.endswith(suffix))
        bind(role, path, "build-python")
    bind("build-report", hosted / "build-report.json", "build-python")
    stage("artifacts", "artifacts", "qualify-python")
    stage("quality", "quality", "qualify-python")
    stage("decision", "decision", "qualify-python")
    decision = parse_canonical_json((hosted / "decision.json").read_bytes())
    assert decision["result"] == "passed"
    assert decision["authority"] == "non-authoritative"
    assert native_calls == ["provider", "compile", "build", "quality"]
    assert set(references) == {
        "request",
        "provider",
        "model",
        "ci-plan",
        "wheel",
        "sdist",
        "build-report",
        "artifacts",
        "quality",
        "decision",
    }
    for ref in references.values():
        assert ref["artifact-digest"] == ref["payload-digest"]
    assert _run(hosted, "export", references) == 0
    output = (hosted / "outputs").read_text().splitlines()
    assert output[-2] == "references=" + canonicalize(references).decode()
    exported_ids = output[-1].removeprefix("artifact-ids=").split(",")
    assert len(exported_ids) == len(references)
    assert set(exported_ids) == {
        str(ref["artifact-id"]) for ref in references.values()
    }


@pytest.mark.parametrize(
    "content", [b"[{} ]", b'{"x":1,"x":2}', b'{"x":NaN}', b" {}"]
)
def test_python_cli_generic_document_preserves_strict_canonical_admission(
    hosted, content
):
    """Array support does not admit noncanonical bytes or ambiguous JSON."""
    path = hosted / "quality.json"
    path.write_bytes(content)
    digest = python_digest(content)
    ref = ArtifactReference(
        _ARTIFACT_ID,
        digest,
        "https://example.invalid/artifacts/1001",
        path.name,
        digest,
    )
    inputs = python_cli.PythonInputs(
        hosted,
        "ci-pr-slice-shadow",
        canonicalize({"quality": ref.to_document()}).decode(),
    )
    with pytest.raises(
        ValueError, match=r"canonical|duplicate|invalid JSON constant"
    ):
        inputs.document("quality")


@pytest.mark.parametrize(
    "change", ["job", "publisher", "terminal", "marker", "result", "action"]
)
def test_python_cli_exact_proof_rejects_foreign_branch_before_native_reads(
    hosted, monkeypatch, change
):
    """Fresh proof cannot borrow publisher authority or erase mutation."""
    exact = _exact_inputs()
    snapshot = exact.publication[0]
    monkeypatch.setenv("GITHUB_JOB", "finalize-attempt")
    monkeypatch.setenv("PUBLISHER_CONCLUSION", "skipped")
    monkeypatch.setenv("TERMINAL_REFERENCE", "null")
    refs = {}
    if change == "job":
        monkeypatch.setenv("GITHUB_JOB", "prepare-python-publication")
    elif change == "publisher":
        monkeypatch.setenv("PUBLISHER_CONCLUSION", "success")
    elif change == "terminal":
        monkeypatch.setenv("TERMINAL_REFERENCE", "{}")
    elif change in {"marker", "result"}:
        refs[change] = {}
    else:
        snapshot = prepared_publication()[0].authorization.bundle.snapshot
    inputs = SimpleNamespace(publication=lambda: snapshot, references=refs)
    arguments = SimpleNamespace(
        command="exact-proof", output=hosted / "proof.json"
    )
    with pytest.raises(ValueError, match="skipped zero-action publisher"):
        python_cli._publication(arguments, inputs)  # noqa: SLF001
    assert not arguments.output.exists()


def test_python_cli_exact_proof_is_fresh_and_owned_by_finalizer(
    hosted, monkeypatch
):
    """The Finalizer reads fresh state and preserves distinct instants."""
    exact = _exact_inputs()
    proof = exact.exact_proof[0]
    monkeypatch.setenv("GITHUB_JOB", "finalize-attempt")
    monkeypatch.setenv("PUBLISHER_CONCLUSION", "skipped")
    monkeypatch.setenv("TERMINAL_REFERENCE", "null")
    calls = []

    def fresh(_registry, *, initial):
        calls.append("governance")
        assert initial == exact.decision.snapshot.governance
        return proof.fresh_governance

    def read(registry, witness, _transport):
        calls.append("index")
        assert registry == proof.snapshot.registry
        assert witness == exact.decision.artifacts[0].witness
        return proof.native

    monkeypatch.setattr(
        python_cli, "_github", lambda: SimpleNamespace(governance=fresh)
    )
    monkeypatch.setattr(python_cli, "PythonHttpsTransport", object)
    monkeypatch.setattr(python_cli, "read_python_index", read)
    monkeypatch.setattr(
        python_cli,
        "datetime",
        SimpleNamespace(now=lambda _zone: NOW + timedelta(seconds=3)),
    )
    inputs = SimpleNamespace(
        publication=lambda: proof.snapshot,
        references={},
        reference=lambda _role: proof.snapshot_reference,
    )
    path = hosted / "proof.json"
    python_cli._publication(  # noqa: SLF001
        SimpleNamespace(command="exact-proof", output=path), inputs
    )
    document = parse_canonical_json(path.read_bytes())
    assert document["producer"] == "finalize-attempt"
    assert document["governance-observed-at"] == "2026-09-24T12:00:01Z"
    assert document["observed-at"] == "2026-09-24T12:00:03Z"
    assert calls == ["governance", "index"]
    assert python_cli._PRODUCERS["exact-proof"] == "finalize-attempt"  # noqa: SLF001


@pytest.mark.parametrize(
    "proof_state", ["bound", "generation-failed", "unbound"]
)
def test_python_cli_finalizer_replays_only_durable_exact_proof(
    hosted,
    proof_state,
):
    """Only durable fresh proof grants success; absent proof stays unknown."""
    exact = _exact_inputs()
    proof = replace(
        exact.exact_proof[0], observed_at=NOW + timedelta(seconds=3)
    )
    refs = {
        "decision": exact.decision_reference,
        "observation": exact.observation[1],
        "publication": exact.publication[1],
    }
    if proof_state == "bound":
        refs["exact-proof"] = reference(proof.to_document(), 509)
    elif proof_state == "unbound":
        # A generated file left by failed persistence is not an admitted edge.
        (hosted / "exact-proof.json").write_bytes(
            canonicalize(proof.to_document())
        )
    _, payloads, _ = originals(exact.decision.snapshot.model)
    contents = dict(zip(("wheel", "sdist"), payloads, strict=True))
    documents = {"exact-proof": proof.to_document()}
    inputs = SimpleNamespace(
        references=refs,
        run=RUN_ID,
        target=TARGET,
        decision=lambda: exact.decision,
        observation=lambda: exact.observation[0],
        publication=lambda: exact.publication[0],
        reference=refs.__getitem__,
        content=contents.__getitem__,
        document=documents.__getitem__,
    )
    arguments = SimpleNamespace(
        output=hosted / "outcome.json",
        publisher_conclusion="skipped",
        publication_step_outcome=None,
        terminal_reference=None,
        observation_conclusion="success",
    )
    assert python_cli._finalize(arguments, inputs) is (proof_state == "bound")  # noqa: SLF001
    document = parse_canonical_json(arguments.output.read_bytes())
    assert document["disposition"] == (
        "exact-satisfied" if proof_state == "bound" else "unknown"
    )
    assert document["possibly-mutated"] is False
    predecessor = "exact-proof" if proof_state == "bound" else "publication"
    assert (
        document["direct-predecessor"]["reference"]
        == refs[predecessor].to_document()
    )
