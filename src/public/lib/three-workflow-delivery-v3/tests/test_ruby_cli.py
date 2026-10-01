"""Hosted Ruby CI stages over explicit original-byte artifact edges."""

import http.client
from dataclasses import replace
from functools import partial
from pathlib import Path
from subprocess import TimeoutExpired

import pytest
from three_workflow_delivery_v3 import ruby_cli
from three_workflow_delivery_v3._ruby_native import ruby_digest
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.catalogs import catalog_digest
from three_workflow_delivery_v3.ci.ruby import RubyCiPlan, run_ruby_ci_quality
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository.compiler import (
    _compilation_context_from_document,
    provider_binding,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    ruby_provider_manifest,
)

from .ruby_integration_fixtures import (
    RUN_ID,
    TARGET,
    admitted,
    consumer,
    context,
    model,
    native_original,
)

_PRODUCERS = {
    "request": "request-ruby",
    "provider": "discover-ruby",
    "model": "compile-ruby",
    "ci-plan": "plan-ruby-ci",
    "gem": "build-ruby",
    "build-report": "build-ruby",
    "artifacts": "qualify-ruby",
    "quality": "qualify-ruby",
    "decision": "qualify-ruby",
}


@pytest.fixture
def hosted(monkeypatch, tmp_path):
    """Select one merge-candidate run and forbid actual platform services."""
    values = {
        "GITHUB_SHA": TARGET,
        "GITHUB_RUN_ID": str(RUN_ID),
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_EVENT_NAME": "pull_request",
        "GITHUB_REF": "refs/pull/954/merge",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_WORKFLOW_SHA": TARGET,
        "GITHUB_WORKFLOW_REF": "hcoona/three/"
        ".github/workflows/workflow-delivery-v3-ruby-smoke.yml"
        "@refs/heads/main",
        "GITHUB_OUTPUT": str(tmp_path / "outputs"),
        "GITHUB_JOB": "request-ruby",
    }
    for key, value in values.items():
        monkeypatch.setenv(key, value)
    monkeypatch.delenv("WDV3_REFERENCES", raising=False)

    def denied(*_args, **_kwargs):
        pytest.fail("Real platform, registry and OIDC access is forbidden")

    monkeypatch.setattr(http.client, "HTTPSConnection", denied)
    return tmp_path


def _store(directory, document, role, artifact_id=1001):
    payload = (
        document if isinstance(document, bytes) else canonicalize(document)
    )
    name = role if role.endswith(".gem") else role + ".json"
    (directory / name).write_bytes(payload)
    digest = ruby_digest(payload)
    return ArtifactReference(
        artifact_id,
        digest,
        f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/{artifact_id}",
        name,
        digest,
    ).to_document()


def _run(  # noqa: PLR0913 - explicit stage inputs
    directory,
    command,
    references=None,
    *,
    purpose="ci-pr-slice-shadow",
    output="result.json",
    extra=(),
):
    return ruby_cli.main(
        [
            command,
            "--purpose",
            purpose,
            "--directory",
            str(directory),
            "--output",
            str(directory / output),
            "--references",
            canonicalize(references or {}).decode(),
            *extra,
        ]
    )


def _outputs(directory):
    return dict(
        line.split("=", 1)
        for line in (directory / "outputs").read_text().splitlines()
    )


def _ci_files(directory):
    source = model("ci-pr-slice-shadow")
    refs = {"model": _store(directory, source.to_document(), "model")}
    plan = RubyCiPlan(
        source, ruby_cli.artifact_reference_from_document(refs["model"]), None
    )
    refs["ci-plan"] = _store(directory, plan.to_document(), "ci-plan", 1002)
    return refs, plan


def test_ruby_cli_request_binds_exact_merge_context_and_catalog(hosted):
    """The request binds platform identity before accepting target facts."""
    assert _run(hosted, "request") == 0
    doc = parse_canonical_json((hosted / "result.json").read_bytes())
    ctx = _compilation_context_from_document(doc["context"])
    assert (ctx.target, ctx.workflow_run_id, ctx.run_attempt, ctx.purpose) == (
        TARGET,
        RUN_ID,
        1,
        "ci-pr-slice-shadow",
    )
    assert ctx.control == f"workflow-delivery-v3:{TARGET}"
    assert ctx.catalog_digest == catalog_digest()
    assert doc == ruby_provider_manifest(ctx).to_document()
    assert not (hosted / "intent.json").exists()
    assert not (hosted / "governance.json").exists()


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("GITHUB_REPOSITORY", "hcoona/foreign"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/main"),
        ("GITHUB_REF", "refs/pull/954/head"),
        ("GITHUB_RUN_ATTEMPT", "not-an-integer"),
    ],
)
def test_ruby_cli_request_rejects_foreign_platform_identity(
    hosted, monkeypatch, key, value
):
    """Invalid execution identity cannot produce a request artifact."""
    monkeypatch.setenv(key, value)
    assert _run(hosted, "request") == 1
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    "change", ["run", "attempt", "target", "purpose", "control", "catalog"]
)
def test_ruby_cli_provider_rejects_foreign_request_before_native_discovery(
    hosted, monkeypatch, change
):
    """An internally coherent foreign request still lacks current authority."""
    ctx = context(purpose="ci-pr-slice-shadow")
    changes = {
        "run": {"workflow_run_id": 1000},
        "attempt": {"run_attempt": 2},
        "target": {"target": "f" * 40},
        "purpose": {"purpose": "slice-validation"},
        "control": {"control": "foreign-control"},
        "catalog": {"catalog_digest": "sha256:" + "f" * 64},
    }
    manifest = ruby_provider_manifest(
        ctx if change == "catalog" else replace(ctx, **changes[change])
    )
    document = manifest.to_document()
    if change == "catalog":
        document["context"]["catalog-digest"] = "sha256:" + "f" * 64
    ref = _store(hosted, document, "request")
    calls = []
    monkeypatch.setattr(
        ruby_cli,
        "provide_ruby_repository_facts",
        lambda *_args: calls.append("unexpected"),
    )
    assert _run(hosted, "provider", {"request": ref}) == 1
    assert calls == []
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    "change", ["run", "attempt", "target", "purpose", "control"]
)
def test_ruby_cli_model_cannot_borrow_other_current_context(hosted, change):
    """A valid Model is admitted only for the current platform facts."""
    source = model("ci-pr-slice-shadow")
    updates = {
        "run": {"workflow_run_id": 1000},
        "attempt": {"run_attempt": 2},
        "target": {"target": "f" * 40},
        "purpose": {"purpose": "slice-validation"},
        "control": {"control": "foreign-control"},
    }
    facts = admitted(replace(source.context, **updates[change]))
    foreign = replace(
        source,
        context=facts.manifest.context,
        provider=facts.result,
        request_reference=facts.request_reference,
        provider_reference=facts.result_reference,
    )
    refs = {"model": _store(hosted, foreign.to_document(), "model")}
    assert _run(hosted, "plan", refs) == 1
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    "failure",
    [
        "missing-file",
        "payload-digest",
        "archive-digest",
        "foreign-url",
        "noncanonical",
        "extra-role",
    ],
)
def test_ruby_cli_explicit_transport_rejects_missing_or_tampered_bytes(
    hosted, failure
):
    """Immutable bytes, current run and canonical encoding precede parsing."""
    refs, _plan = _ci_files(hosted)
    ref = refs["model"]
    if failure == "missing-file":
        (hosted / ref["payload-path"]).rename(hosted / "unavailable")
    elif failure in {"payload-digest", "archive-digest"}:
        ref[
            "payload-digest"
            if failure == "payload-digest"
            else "artifact-digest"
        ] = "sha256:" + "f" * 64
    elif failure == "foreign-url":
        ref["artifact-url"] = (
            "https://github.com/hcoona/three/actions/runs/1000/artifacts/1001"
        )
    elif failure == "extra-role":
        refs["undisclosed"] = ref
    else:
        raw = (hosted / "model.json").read_bytes() + b"\n"
        refs["model"] = _store(hosted, raw, "model")
    assert _run(hosted, "plan", refs) == 1
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    "change", ["missing-model", "model-reference", "changed-plan"]
)
def test_ruby_cli_ci_plan_preserves_exact_model_predecessor(hosted, change):
    """Lost or rebound prior edges cannot produce a current Decision."""
    refs, plan = _ci_files(hosted)
    if change == "missing-model":
        del refs["model"]
    elif change == "model-reference":
        replacement = replace(
            plan.model_reference,
            artifact_id=1099,
            artifact_url=f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/1099",
        )
        refs["model"] = replacement.to_document()
    else:
        plan = replace(plan, changed_paths=())
        refs["ci-plan"] = _store(hosted, plan.to_document(), "ci-plan", 1002)
        # The explicit full-scope plan is valid only with its own identity;
        # this case tests a malformed claimed selection, not path discovery.
        doc = plan.to_document()
        doc["selected"] = True
        refs["ci-plan"] = _store(hosted, doc, "ci-plan", 1002)
    assert _run(hosted, "decision", refs) == 1
    assert not (hosted / "result.json").exists()


def test_ruby_cli_missing_quality_retains_incomplete_decision(hosted):
    """Build/quality loss surfaces a failed CI gate with retained conclusion."""
    refs, _plan = _ci_files(hosted)
    assert _run(hosted, "decision", refs) == 1
    doc = parse_canonical_json((hosted / "result.json").read_bytes())
    assert doc["result"] == "incomplete"
    assert doc["authority"] == "non-authoritative"
    assert _outputs(hosted)["qualification-result"] == "incomplete"


@pytest.mark.parametrize("role", list(_PRODUCERS))
def test_ruby_cli_bind_preserves_prior_edges_and_exports_exact_original(
    hosted, monkeypatch, role
):
    """Each producer binds an original and retains its predecessors."""
    prior = {
        "ci-plan" if role == "request" else "request": _store(
            hosted, {}, "prior", 1000
        )
    }
    payload = hosted / "readback.json"
    payload.write_bytes(b"{}")
    monkeypatch.setenv("GITHUB_JOB", _PRODUCERS[role])
    digest = ruby_digest(payload.read_bytes())
    assert (
        _run(
            hosted,
            "bind",
            prior,
            extra=(
                "--role",
                role,
                "--payload",
                str(payload),
                "--artifact-id",
                "1001",
                "--artifact-digest",
                digest.removeprefix("sha256:"),
                "--artifact-url",
                f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/1001",
            ),
        )
        == 0
    )
    references = parse_canonical_json((hosted / "result.json").read_bytes())
    assert references == {
        **prior,
        role: {
            "artifact-id": 1001,
            "artifact-digest": digest,
            "artifact-url": f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/1001",
            "payload-path": "readback.json",
            "payload-digest": digest,
        },
    }
    assert _outputs(hosted)["artifact-ids"] == "1000,1001"
    assert (
        parse_canonical_json(_outputs(hosted)["references"].encode())
        == references
    )


@pytest.mark.parametrize(
    "failure", ["producer", "digest", "foreign-run", "overwrite"]
)
def test_ruby_cli_bind_cannot_replace_role_or_forge_upload(
    hosted, monkeypatch, failure
):
    """Reject forged upload outputs before emitting new workflow edges."""
    payload = hosted / "readback.json"
    payload.write_bytes(b"{}")
    monkeypatch.setenv(
        "GITHUB_JOB", "build-ruby" if failure == "producer" else "compile-ruby"
    )
    digest = "sha256:" + "f" * 64 if failure == "digest" else ruby_digest(b"{}")
    run_id = 1000 if failure == "foreign-run" else RUN_ID
    url = (
        f"https://github.com/hcoona/three/actions/runs/{run_id}/artifacts/1001"
    )
    refs = (
        {"model": _store(hosted, {}, "model")} if failure == "overwrite" else {}
    )
    assert (
        _run(
            hosted,
            "bind",
            refs,
            extra=(
                "--role",
                "model",
                "--payload",
                str(payload),
                "--artifact-id",
                "1001",
                "--artifact-digest",
                digest,
                "--artifact-url",
                url,
            ),
        )
        == 1
    )
    assert not (hosted / "outputs").exists()
    assert not (hosted / "result.json").exists()


def test_ruby_cli_export_uses_only_explicit_edges_without_discovery(
    hosted, monkeypatch
):
    """Export neither lists services nor reads unrelated directory payloads."""
    ref = _store(hosted, {}, "model")
    (hosted / "foreign-record.json").write_bytes(b"private unrelated bytes")

    def denied(*_args, **_kwargs):
        pytest.fail("Directory discovery is forbidden")

    monkeypatch.setattr(Path, "glob", denied)
    monkeypatch.setattr(Path, "rglob", denied)
    assert _run(hosted, "export", {"model": ref}) == 0
    assert _outputs(hosted)["artifact-ids"] == "1001"
    assert parse_canonical_json(_outputs(hosted)["references"].encode()) == {
        "model": ref
    }


def test_ruby_cli_native_failure_emits_only_exception_class(
    hosted, monkeypatch, capsys
):
    """Native command arguments and captured output cannot leak in rejection."""
    assert _run(hosted, "request", output="request.json") == 0
    ref = _store(hosted, (hosted / "request.json").read_bytes(), "request")

    def failed(*_args):
        raise TimeoutExpired(
            ["private-command"],
            1,
            output=b"private-token",
            stderr=b"private-stderr",
        )

    monkeypatch.setattr(ruby_cli, "provide_ruby_repository_facts", failed)
    assert _run(hosted, "provider", {"request": ref}) == 1
    assert capsys.readouterr().out == "Ruby stage rejected: TimeoutExpired\n"
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize(
    "outcome", ["passed", "consumer-failed", "missing-gem", "corrupt-gem"]
)
def test_ruby_cli_staged_journey_retains_original_and_ci_outcome(  # noqa: C901, PLR0915 - explicit staged evidence journey
    hosted, monkeypatch, cli_original, outcome
):
    """Exercise actual compilation/inspection with one shared native gem."""
    source_root, (original_model, _artifact, original) = cli_original
    target = original_model.context.target
    monkeypatch.setenv("GITHUB_SHA", target)
    monkeypatch.setenv("GITHUB_WORKFLOW_SHA", target)
    provider_calls = []
    build_calls = []

    def provide(root, binding, materialization):
        assert root == source_root
        assert materialization.fetch_depth == 0
        assert materialization.credentials_persisted is False
        provider_calls.append(binding)
        return replace(original_model.provider, binding=binding)

    def build(root, request):
        assert root == source_root
        assert request == original_model.build_request()
        build_calls.append(request)
        return original

    def consume(distribution):
        assert distribution.content == original.content
        if outcome == "consumer-failed":
            message = "controlled failed consumer"
            raise RuntimeError(message)
        return consumer(distribution)

    monkeypatch.setattr(ruby_cli, "provide_ruby_repository_facts", provide)
    monkeypatch.setattr(ruby_cli, "build_ruby_package", build)
    monkeypatch.setattr(
        ruby_cli,
        "run_ruby_ci_quality",
        partial(run_ruby_ci_quality, consumer=consume),
    )
    refs = {}

    def command(name, role):
        return _run(
            hosted,
            name,
            refs,
            purpose="slice-validation",
            output=role + ".json",
            extra=("--repo-root", str(source_root)),
        )

    def bind(role, payload):
        monkeypatch.setenv("GITHUB_JOB", _PRODUCERS[role])
        artifact_id = 1001 + len(refs)
        previous = dict(refs)
        readback = hosted / "readback" / payload.name
        readback.parent.mkdir(exist_ok=True)
        readback.write_bytes(payload.read_bytes())
        assert (
            _run(
                hosted,
                "bind",
                refs,
                purpose="slice-validation",
                output="references.json",
                extra=(
                    "--role",
                    role,
                    "--payload",
                    str(readback),
                    "--artifact-id",
                    str(artifact_id),
                    "--artifact-digest",
                    ruby_digest(readback.read_bytes()),
                    "--artifact-url",
                    f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/{artifact_id}",
                ),
            )
            == 0
        )
        refs.update(
            parse_canonical_json((hosted / "references.json").read_bytes())
        )
        assert {key: refs[key] for key in previous} == previous

    for stage, role in [
        ("request", "request"),
        ("provider", "provider"),
        ("compile", "model"),
        ("plan", "ci-plan"),
    ]:
        assert command(stage, role) == 0
        bind(role, hosted / (role + ".json"))
    assert len(provider_calls) == 1
    request_doc = parse_canonical_json((hosted / "request.json").read_bytes())
    assert provider_calls[0] == provider_binding(
        ruby_provider_manifest(
            _compilation_context_from_document(request_doc["context"])
        ),
        "ruby-smoke",
    )
    assert command("build", "build-report") == 0
    assert len(build_calls) == 1
    gem = Path(_outputs(hosted)["gem-path"])
    assert gem.read_bytes() == original.content
    bind("gem", gem)
    bind("build-report", hosted / "build-report.json")
    if outcome == "missing-gem":
        gem.rename(hosted / "unavailable.gem")
    elif outcome == "corrupt-gem":
        gem.write_bytes(original.content + b"tampered")
    if outcome in {"missing-gem", "corrupt-gem"}:
        assert command("artifacts", "artifacts") == 1
        assert not (hosted / "artifacts.json").exists()
    else:
        assert command("artifacts", "artifacts") == 0
        bind("artifacts", hosted / "artifacts.json")
        assert command("quality", "quality") == 0
        bind("quality", hosted / "quality.json")
        evidence = parse_json_strict((hosted / "quality.json").read_bytes())
        assert [item["result"] for item in evidence] == [
            "passed",
            "failed" if outcome == "consumer-failed" else "passed",
        ]
    assert command("decision", "decision") == (0 if outcome == "passed" else 1)
    decision = parse_canonical_json((hosted / "decision.json").read_bytes())
    assert (
        decision["result"]
        == {
            "passed": "passed",
            "consumer-failed": "failed",
            "missing-gem": "incomplete",
            "corrupt-gem": "incomplete",
        }[outcome]
    )
    assert decision["authority"] == "non-authoritative"
    bind("decision", hosted / "decision.json")
    assert _run(hosted, "export", refs, purpose="slice-validation") == 0
    assert parse_canonical_json(_outputs(hosted)["references"].encode()) == refs
    assert set(refs) == set(_PRODUCERS) - (
        {"artifacts", "quality"}
        if outcome in {"missing-gem", "corrupt-gem"}
        else set()
    )


@pytest.fixture(scope="session")
def cli_original(tmp_path_factory):
    """Reuse the native original builder while retaining its source location."""
    root = tmp_path_factory.mktemp("ruby-cli-original")
    original = native_original(root)
    return root / "source", original


@pytest.mark.parametrize(
    "change", ["producer", "run", "attempt", "reference", "missing-gem"]
)
def test_ruby_cli_quality_rejects_foreign_original_transport(
    hosted, monkeypatch, cli_original, change
):
    """Quality cannot consume another producer/run or a rebound original."""
    _source, (source, _artifact, original) = cli_original
    monkeypatch.setenv("GITHUB_SHA", source.context.target)
    refs = {"model": _store(hosted, source.to_document(), "model")}
    refs["gem"] = _store(hosted, original.content, original.filename, 1002)
    assert (
        _run(
            hosted,
            "plan",
            refs,
            purpose="slice-validation",
            output="ci-plan.json",
        )
        == 0
    )
    refs["ci-plan"] = _store(
        hosted, (hosted / "ci-plan.json").read_bytes(), "ci-plan", 1003
    )
    assert (
        _run(
            hosted,
            "artifacts",
            refs,
            purpose="slice-validation",
            output="artifacts.json",
        )
        == 0
    )
    doc = parse_canonical_json((hosted / "artifacts.json").read_bytes())
    if change == "missing-gem":
        del refs["gem"]
    elif change == "reference":
        refs["gem"]["artifact-id"] = 1099
        refs["gem"]["artifact-url"] = (
            f"https://github.com/hcoona/three/actions/runs/{RUN_ID}/artifacts/1099"
        )
    else:
        field, value = {
            "producer": ("producer", "qualify-ruby"),
            "run": ("workflow-run-id", 1000),
            "attempt": ("run-attempt", 2),
        }[change]
        doc["transport"][field] = value
    refs["artifacts"] = _store(hosted, doc, "artifacts", 1004)
    calls = []
    monkeypatch.setattr(
        ruby_cli,
        "run_ruby_ci_quality",
        lambda *_args: calls.append("unexpected"),
    )
    assert _run(hosted, "quality", refs, purpose="slice-validation") == 1
    assert calls == []
    assert not (hosted / "result.json").exists()


@pytest.mark.parametrize("raw", [b"[]", b"null", b'{"model":{},"model":{}}'])
def test_ruby_cli_reference_map_rejects_nonobject_or_duplicate_roles(
    hosted, raw
):
    """Ambiguous direct-DAG maps cannot silently choose an artifact."""
    assert (
        ruby_cli.main(
            [
                "export",
                "--purpose",
                "ci-pr-slice-shadow",
                "--directory",
                str(hosted),
                "--references",
                raw.decode(),
            ]
        )
        == 1
    )
    assert not (hosted / "outputs").exists()


def test_ruby_cli_has_no_live_publication_entrypoints(hosted):
    """This CI slice cannot activate an unfinished live command or purpose."""
    with pytest.raises(SystemExit) as caught:
        _run(hosted, "request", purpose="live-release")
    assert caught.value.code == 2  # noqa: PLR2004 - argparse invalid-input exit
    with pytest.raises(SystemExit) as caught:
        _run(hosted, "publish")
    assert caught.value.code == 2  # noqa: PLR2004 - argparse invalid-input exit
    assert not (hosted / "result.json").exists()
