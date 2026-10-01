"""Hosted command assembly over real originals and controlled service edges."""

import shutil
from copy import deepcopy
from dataclasses import replace
from functools import partial

import pytest
from three_workflow_delivery_v3 import ruby_operation_cli as cli
from three_workflow_delivery_v3 import ruby_operation_runtime as runtime
from three_workflow_delivery_v3.acceptance import (
    ruby_bootstrap_preparation as preparation,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.repository.ruby_model import RUBY_QUALITY
from three_workflow_delivery_v3.ruby_operation_records import (
    RubyOperationRecords,
)

from .acceptance.ruby_bootstrap_fixtures import (
    CURRENT,
    BootstrapCase,
    bootstrap_approval_proof,
    native_inputs,
)
from .ruby_hosted_fixtures import bootstrap_case_from_run, hosted_case
from .ruby_registry_fixtures import (
    ASSERTION,
    ScriptedTransport,
    json_response,
    native_index,
    token_document,
)


def command(case, monkeypatch, stage, role=None, *extra):
    """Run the public CLI and model only its resulting immutable upload."""
    monkeypatch.setenv(
        "WDV3_REFERENCES", canonicalize(case.references).decode()
    )
    filename = {
        "summary": "approval-summary.txt",
        "bundle": "approval-bundle.json",
        "marker": "mutation-marker.json",
    }.get(role, (role or stage) + ".json")
    path = case.directory / filename
    status = cli.main(
        [
            stage,
            "--root",
            str(case.root),
            "--directory",
            str(case.directory),
            "--output",
            str(path),
            *extra,
        ]
    )
    assert status == 0, stage
    if role:
        case.add(role, path.read_bytes(), filename=path.name)
    return path


def job(case, monkeypatch, name):
    """Each job sees exact downloaded artifacts and fresh local state."""
    directory = case.directory.parent / name
    directory.mkdir()
    for reference in case.references.values():
        filename = reference["payload-path"]
        shutil.copyfile(case.directory / filename, directory / filename)
    case.directory = directory
    case.install(monkeypatch)
    monkeypatch.setenv("GITHUB_JOB", name)
    command(case, monkeypatch, "initialize")


@pytest.fixture(scope="module", params=["rubygems", "github-packages"])
def native_hosted_bootstrap(request, tmp_path_factory):
    """Use actual native build and consumer for each destination."""
    base = tmp_path_factory.mktemp("hosted-native-" + request.param)
    source, run, _materialization = native_inputs(base, request.param)
    case = bootstrap_case_from_run(source, base / "request", run)
    with pytest.MonkeyPatch.context() as patch:
        case.install(patch)
        patch.setattr(
            cli,
            "provide_ruby_bootstrap",
            partial(preparation.provide_ruby_bootstrap, clock=lambda: CURRENT),
        )
        patch.setattr(
            cli,
            "build_ruby_bootstrap",
            partial(preparation.build_ruby_bootstrap, clock=lambda: CURRENT),
        )
        patch.setattr(
            cli,
            "run_ruby_bootstrap_quality",
            partial(
                preparation.run_ruby_bootstrap_quality, clock=lambda: CURRENT
            ),
        )
        command(case, patch, "request", "operation")
        job(case, patch, "provide-ruby-bootstrap")
        command(case, patch, "provider", "provider")
        job(case, patch, "plan-ruby-bootstrap")
        command(case, patch, "plan", "plan")
        job(case, patch, "build-ruby")
        command(case, patch, "build")
        gem = next(case.directory.glob("*.gem"))
        case.add("gem", gem.read_bytes(), filename=gem.name)
        job(case, patch, "qualify-ruby-bootstrap")
        command(case, patch, "artifacts", "artifacts")
        command(case, patch, "quality", "quality")
        job(case, patch, "finalize-ruby-bootstrap-qualification")
        command(case, patch, "qualification", "qualification")
    return case


@pytest.fixture
def qualified_hosted(native_hosted_bootstrap, tmp_path, monkeypatch):
    """Preserve frozen native originals while mutating each local DAG alone."""
    directory = tmp_path / "qualified"
    shutil.copytree(native_hosted_bootstrap.directory, directory)
    case = replace(
        native_hosted_bootstrap,
        directory=directory,
        references=deepcopy(native_hosted_bootstrap.references),
    )
    case.install(monkeypatch)
    return case


def native_case(case):
    """Build scripted service responses using the actual native coordinate."""
    records = RubyOperationRecords(case.inputs())
    publication = records.bootstrap_publication()
    original = records.artifact().inspect(case.inputs().content("gem"))
    indexes = {
        "empty": native_index(case.directory, []),
        "exact": native_index(
            case.directory,
            [
                [
                    "hcoona-release-smoke-ruby",
                    original.witness.nbgv.native_version,
                    "ruby",
                ]
            ],
        ),
    }
    return BootstrapCase(publication, original, indexes)


def test_hosted_native_provider_build_and_clean_qualification_join_target(
    qualified_hosted,
):
    """Actual NBGV and the installed original qualify the DAG."""
    case = qualified_hosted
    records = RubyOperationRecords(case.inputs())
    decision = records.bootstrap_qualification()
    assert decision.result == "passed"
    assert decision.plan.run.target == case.request.document["target"]
    assert (
        decision.plan.provider.nbgv.to_document()
        == case.request.document["nbgv"]
    )
    assert {item.definition for item in decision.evidence} == set(RUBY_QUALITY)
    assert all(
        item.artifact == records.artifact() for item in decision.evidence
    )
    assert case.inputs().transport("gem").producer == "build-ruby"
    assert (
        records.bootstrap_publication().original.content
        == case.inputs().content("gem")
    )


@pytest.mark.parametrize(
    "role", ["plan", "artifacts", "quality", "qualification"]
)
def test_hosted_records_reject_extra_or_changed_replayed_record(
    qualified_hosted, role
):
    """Correct transport hashes cannot bless a modified domain predecessor."""
    case = qualified_hosted
    payload = case.inputs().document(role)
    payload["unreviewed-success"] = True
    case.add(role, payload)
    with pytest.raises(ValueError, match=r"Ruby|ruby|fields|record"):
        RubyOperationRecords(case.inputs()).bootstrap_publication()


def test_hosted_bootstrap_markers_exchange_and_remote_consumer(  # noqa: PLR0915 - complete cross-job journey
    qualified_hosted, monkeypatch
):
    """Execute all assembly transitions with real readers/writers and replay."""
    case = qualified_hosted
    native = native_case(case)
    events = []
    active = {"transport": None}

    def transport(_deadline):
        assert active["transport"] is not None
        return active["transport"]

    def guard(inputs, _github, _initial=None):
        def check():
            inputs.operation().require_current(CURRENT)
            events.append("authority")
            return {"modeled-native-authority": True}

        return check

    def oidc(audience, _environment, _transport):
        assert audience == "rubygems.org"
        records = RubyOperationRecords(case.inputs())
        records.bootstrap_authorization()
        records.bootstrap_marker()
        assert (case.directory / "upload-claim.json").is_file()
        events.append("oidc")
        return ASSERTION

    monkeypatch.setattr(runtime, "RubyOperationHttpsTransport", transport)
    monkeypatch.setattr(cli, "RubyOperationHttpsTransport", transport)
    monkeypatch.setattr(cli, "HostedRubyGitHub", lambda *_: object())
    monkeypatch.setattr(cli, "authority_guard", guard)
    monkeypatch.setattr(cli, "obtain_ruby_oidc_assertion", oidc)
    monkeypatch.setattr(
        cli,
        "collect_ruby_bootstrap_approval",
        lambda run, *_args, **_kwargs: canonicalize(
            bootstrap_approval_proof(run)
        ),
    )

    job(case, monkeypatch, "prepare-ruby-bootstrap-publication")
    command(
        case,
        monkeypatch,
        "claim",
        "phase-eligibility",
        "--phase",
        "eligibility",
    )
    active["transport"] = ScriptedTransport(*native.absent_responses())
    command(case, monkeypatch, "observe", "absence")
    assert len(active["transport"].requests) == len(native.absent_responses())
    command(case, monkeypatch, "summary", "summary")
    command(case, monkeypatch, "bundle", "bundle")

    job(case, monkeypatch, native.publisher)
    monkeypatch.setenv(
        "WDV3_ENVIRONMENT_SENTINEL", native.registry.environment + "/v1"
    )
    command(case, monkeypatch, "authorize", "authorization")
    command(
        case, monkeypatch, "claim", "phase-pre-marker", "--phase", "pre-marker"
    )
    active["transport"] = ScriptedTransport(*native.absent_responses())
    command(case, monkeypatch, "marker", "marker")
    assert events.count("oidc") == 0
    command(case, monkeypatch, "claim", "phase-execute", "--phase", "execute")
    exchange = (
        [json_response(token_document(), status=201)]
        if native.registry.name == "rubygems"
        else []
    )
    active["transport"] = ScriptedTransport(
        *exchange,
        RubyHttpResponse(200, b"ok", "text/plain"),
        *native.exact_responses(),
    )
    command(case, monkeypatch, "execute", "result")
    sent = active["transport"].requests
    assert [request[0] for request in sent] == (
        ["POST"] if exchange else []
    ) + ["POST", "GET", "GET", "GET"]
    assert sum(request[3] == native.original.content for request in sent) == 1
    assert events.count("oidc") == (1 if exchange else 0)
    assert events.count("authority") == 5  # noqa: PLR2004 - prepare and four publisher guards
    result = RubyOperationRecords(case.inputs()).bootstrap_result()
    assert result.result == "published"

    retained = deepcopy(case.references)
    for failed_state in ("cancelled", "missing-result"):
        if failed_state == "missing-result":
            case.references.pop("result")
        monkeypatch.setenv(
            "WDV3_NEEDS",
            canonicalize(
                {native.publisher: {"result": "cancelled", "outputs": {}}}
            ).decode(),
        )
        diagnostic = command(case, monkeypatch, "finalize")
        assert parse_json_strict(diagnostic.read_bytes())["result"] == (
            "unknown" if failed_state == "missing-result" else "failed"
        )
    case.references = retained

    job(case, monkeypatch, "finalize-ruby-bootstrap")
    needs = {native.publisher: {"result": "success", "outputs": {}}}
    monkeypatch.setenv("WDV3_NEEDS", canonicalize(needs).decode())
    command(case, monkeypatch, "finalize", "terminal")
    terminal = case.inputs().document("terminal")
    assert terminal["result"] == "bootstrap-published"
    assert terminal["normal-live-completion"] is False
    assert terminal["independent-audit"] is False

    job(case, monkeypatch, "verify-ruby-bootstrap-remote")
    command(
        case,
        monkeypatch,
        "claim",
        "phase-remote-consumer",
        "--phase",
        "remote-consumer",
    )
    active["transport"] = ScriptedTransport(*native.exact_responses())
    command(case, monkeypatch, "remote", "remote-consumer")
    assert len(active["transport"].requests) == len(native.exact_responses())
    remote = case.inputs().document("remote-consumer")
    assert remote["producer"] == "verify-ruby-bootstrap-remote"
    assert remote["consumer"]


@pytest.mark.parametrize("stage", ["execute", "remote"])
@pytest.mark.parametrize("claim", ["failed", "bootstrap-published"])
def test_hosted_scalar_success_or_missing_marker_never_acquires_credentials(
    qualified_hosted, monkeypatch, stage, claim
):
    """Missing markers and scalar claims cannot trigger native sends."""
    case = qualified_hosted
    calls = []
    native = ScriptedTransport()
    monkeypatch.setattr(
        runtime, "RubyOperationHttpsTransport", lambda _: native
    )
    monkeypatch.setattr(cli, "RubyOperationHttpsTransport", lambda _: native)
    monkeypatch.setattr(cli, "HostedRubyGitHub", lambda *_: object())
    monkeypatch.setattr(
        cli, "obtain_ruby_oidc_assertion", lambda *_: calls.append("oidc")
    )
    phase = "execute" if stage == "execute" else "remote-consumer"
    job_name = (
        "publish-ruby-bootstrap-"
        + case.request.document["binding"]["destination"]
        if stage == "execute"
        else "verify-ruby-bootstrap-remote"
    )
    job(case, monkeypatch, job_name)
    command(case, monkeypatch, "claim", "phase-" + phase, "--phase", phase)
    if stage == "remote":
        case.add("terminal", {"result": claim})
    monkeypatch.setenv(
        "WDV3_REFERENCES", canonicalize(case.references).decode()
    )
    output = case.directory / "rejected.json"
    status = cli.main(
        [
            stage,
            "--root",
            str(case.root),
            "--directory",
            str(case.directory),
            "--output",
            str(output),
        ]
    )
    assert status == 1
    assert calls == []
    assert native.requests == []
    assert not output.exists()


@pytest.mark.parametrize(
    "predecessor", ["failure", "cancelled", "missing", "corrupt"]
)
def test_hosted_terminal_always_retains_failure_without_promoting_records(
    tmp_path, monkeypatch, predecessor
):
    """The finalizer records a terminal even when request or inputs are lost."""
    case = hosted_case(tmp_path)
    case.install(monkeypatch)
    if predecessor == "corrupt":
        case.add("operation", b"not-json")
    monkeypatch.setenv(
        "WDV3_NEEDS",
        canonicalize(
            {"request-ruby-operation": {"result": predecessor, "outputs": {}}}
        ).decode(),
    )
    path = command(case, monkeypatch, "finalize")
    terminal = parse_json_strict(path.read_bytes())
    assert terminal["result"] == "failed"
    assert terminal["normal-live-completion"] is False
    assert terminal["independent-audit"] is False
    assert "outcome" not in terminal


@pytest.mark.parametrize(
    "stage", ["observe", "authorize", "marker", "execute", "remote"]
)
def test_hosted_cli_rejects_absent_admission_before_native_access(
    tmp_path, monkeypatch, stage
):
    """Every service-bearing stage fails before constructing any caller."""
    case = hosted_case(tmp_path)
    case.install(monkeypatch)
    calls = []
    monkeypatch.setattr(
        cli, "HostedRubyGitHub", lambda *_: calls.append("github")
    )
    monkeypatch.setattr(
        cli, "registry_reader", lambda *_: calls.append("registry")
    )
    status = cli.main(
        [
            stage,
            "--root",
            str(case.root),
            "--directory",
            str(case.directory),
            "--output",
            str(case.directory / "output.json"),
        ]
    )
    assert status == 1
    assert calls == []
    assert not (case.directory / "output.json").exists()
