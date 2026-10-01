"""Native normal originals, publication and exact-satisfied replay."""

import shutil
from copy import deepcopy
from dataclasses import replace
from datetime import timedelta
from types import SimpleNamespace

import pytest
from three_workflow_delivery_v3 import ruby_operation_cli as cli
from three_workflow_delivery_v3 import ruby_operation_host as host
from three_workflow_delivery_v3 import ruby_operation_runtime as runtime
from three_workflow_delivery_v3.adapters.ruby_registry import RubyHttpResponse
from three_workflow_delivery_v3.canonical import canonicalize, parse_json_strict
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    provide_ruby_repository_facts,
)
from three_workflow_delivery_v3.ruby_operation_records import (
    RubyOperationRecords,
)

from .acceptance.ruby_bootstrap_fixtures import CURRENT, BootstrapCase
from .release.ruby_fixtures import approval_proof
from .ruby_fixtures import ROOT, binding, commit, git, repository
from .ruby_hosted_fixtures import hosted_case, reseal_normal
from .ruby_registry_fixtures import (
    ASSERTION,
    ScriptedTransport,
    json_response,
    native_index,
    token_document,
)
from .test_ruby_operation_cli import command, job


@pytest.fixture(scope="module", params=["rubygems", "github-packages"])
def normal_original(request, tmp_path_factory):
    """Seal native normal controls, Provider, Build and real clean consumers."""
    base = tmp_path_factory.mktemp("hosted-normal-" + request.param)
    case = hosted_case(
        base / "admission", destination=request.param, bootstrap=False
    )
    source, origin, _ = repository(base / "repository")
    for original in case.root.rglob("*"):
        if original.is_file():
            target = origin / original.relative_to(case.root)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(original.read_bytes())
    policy = "eng/workflow-delivery/v3/policies/hcoona-release-smoke-ruby.yml"
    (origin / policy).parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(ROOT / policy, origin / policy)
    target = commit(origin)
    git(source, "fetch", "origin")
    git(source, "checkout", "--detach", target)
    provider = provide_ruby_repository_facts(
        source,
        binding(target, "live-release"),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    reseal_normal(
        case,
        source,
        provider,
        git(source, "rev-parse", "HEAD^{tree}"),
        base / "native-ledger",
    )
    with pytest.MonkeyPatch.context() as patch:
        case.install(patch)
        command(case, patch, "request", "operation")
        case.add(
            "provider-request",
            (case.directory / "provider-request.json").read_bytes(),
        )
        job(case, patch, "discover-ruby")
        command(case, patch, "provider", "provider")
        job(case, patch, "plan-ruby-release")
        command(case, patch, "compile", "model")
        command(case, patch, "plan", "plan")
        job(case, patch, "build-ruby")
        command(case, patch, "build")
        gem = next(case.directory.glob("*.gem"))
        case.add("gem", gem.read_bytes(), filename=gem.name)
        job(case, patch, "qualify-ruby-release")
        command(case, patch, "artifacts", "artifacts")
        command(case, patch, "quality", "quality")
        job(case, patch, "finalize-ruby-qualification")
        command(case, patch, "qualification", "qualification")
    return case


@pytest.fixture
def normal_case(normal_original, tmp_path, monkeypatch):
    """Keep one native qualification immutable while selecting action cases."""
    directory = tmp_path / "qualified"
    shutil.copytree(normal_original.directory, directory)
    case = replace(
        normal_original,
        directory=directory,
        references=deepcopy(normal_original.references),
    )
    case.install(monkeypatch)
    return case


def advance_clock(monkeypatch, seconds):
    """Model separate current observations after the original eligibility."""
    for module in (cli, host, runtime):
        monkeypatch.setattr(
            module, "now", lambda: CURRENT + timedelta(seconds=seconds)
        )


def service_case(case):
    """Serve each real native version rather than a fixed test coordinate."""
    records = RubyOperationRecords(case.inputs())
    original = records.artifact().inspect(case.inputs().content("gem"))
    native = SimpleNamespace(
        registry=records.operation.registry,
        original=original,
        indexes={
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
        },
    )
    native.missing_responses = lambda: BootstrapCase.missing_responses(native)
    native.exact_responses = lambda: BootstrapCase.exact_responses(native)
    return native


def reject_foreign_edge(  # noqa: PLR0913 - explicit stage and optional effect trace
    case, monkeypatch, role, field, importer, *, events=None
):
    """R01: unchanged predecessor bytes cannot conceal a foreign ID or run."""
    original_reference = deepcopy(case.references[role])
    path = case.directory / original_reference["payload-path"]
    original_content = path.read_bytes()
    for substitution in ("id", "run"):
        document = case.inputs().document(role)
        edge = document[field]
        if substitution == "id":
            edge["artifact-id"] = 99991
            edge["artifact-url"] = (
                "https://github.com/hcoona/three/actions/runs/991/artifacts/99991"
            )
        else:
            edge["artifact-url"] = edge["artifact-url"].replace(
                "/991/", "/992/"
            )
        case.add(role, document, filename=path.name)
        with pytest.raises(
            ValueError, match=r"reference|predecessor|hosted|summary|differs"
        ):
            getattr(RubyOperationRecords(case.inputs()), importer)()
        if events is not None:
            before = list(events)
            monkeypatch.setenv(
                "WDV3_REFERENCES", canonicalize(case.references).decode()
            )
            output = case.directory / "rejected-execute.json"
            assert (
                cli.main(
                    [
                        "execute",
                        "--root",
                        str(case.root),
                        "--directory",
                        str(case.directory),
                        "--output",
                        str(output),
                    ]
                )
                == 1
            )
            assert events == before
            assert not output.exists()
            assert not (case.directory / "credential-claim.json").exists()
        path.write_bytes(original_content)
        case.references[role] = deepcopy(original_reference)


def reject_cancelled_or_missing_result(
    case, monkeypatch, needs, publisher, events
):
    """Cancellation and lost results cannot become successful publication."""
    retained = deepcopy(case.references)
    before = list(events)
    del case.references["result"]
    for conclusion in ("cancelled", "success"):
        diagnostic_needs = deepcopy(needs)
        diagnostic_needs[publisher] = {
            "result": conclusion,
            "outputs": {
                "publication-step-outcome": conclusion,
                "terminal-reference": canonicalize(
                    case.references["marker"]
                ).decode(),
            },
        }
        monkeypatch.setenv(
            "WDV3_NEEDS", canonicalize(diagnostic_needs).decode()
        )
        diagnostic = command(case, monkeypatch, "finalize")
        assert parse_json_strict(diagnostic.read_bytes())["result"] == "unknown"
        assert events == before
    case.references = retained


@pytest.mark.parametrize("action", [True, False])
def test_normal_hosted_action_or_exact_satisfied(  # noqa: PLR0915 - complete cross-job journey
    normal_case, monkeypatch, action
):
    """Strict records drive distinct action and exact-satisfied outcomes."""
    case = normal_case
    native = service_case(case)
    records = RubyOperationRecords(case.inputs())
    decision = records.normal_qualification()
    assert decision.result == "passed"
    assert records.model().context.run_attempt is None
    assert records.model().context.target == case.request.document["target"]
    reject_foreign_edge(
        case, monkeypatch, "model", "request-reference", "model"
    )
    reject_foreign_edge(
        case, monkeypatch, "model", "provider-reference", "model"
    )
    reject_foreign_edge(
        case, monkeypatch, "plan", "model-reference", "normal_plan"
    )
    events = []
    active = {"native": None}

    def transport(_deadline):
        assert active["native"] is not None
        return active["native"]

    class GitHub:
        def approval(self, _intent, _governance, *, sentinel):
            assert sentinel == native.registry.environment + "/v1"
            events.append("approval")
            snapshot = RubyOperationRecords(case.inputs()).normal_publication()
            return canonicalize(approval_proof(snapshot))

    def guard(inputs, _github, _initial=None):
        def check():
            operation = inputs.operation()
            instant = cli.now()
            gov = operation.governance(instant)
            events.append("authority")
            return {
                "governance": gov.document,
                "source-commit": gov.source_commit,
                "observed-at": instant.isoformat(),
            }

        return check

    def oidc(_audience, _environment, _transport):
        records = RubyOperationRecords(case.inputs())
        records.normal_authorization()
        records.normal_marker()
        assert (case.directory / "credential-claim.json").is_file()
        events.append("oidc")
        return ASSERTION

    monkeypatch.setattr(runtime, "RubyOperationHttpsTransport", transport)
    monkeypatch.setattr(cli, "RubyOperationHttpsTransport", transport)
    monkeypatch.setattr(cli, "HostedRubyGitHub", lambda *_: GitHub())
    monkeypatch.setattr(cli, "authority_guard", guard)
    monkeypatch.setattr(cli, "obtain_ruby_oidc_assertion", oidc)
    job(case, monkeypatch, "prepare-ruby-publication")
    command(
        case,
        monkeypatch,
        "claim",
        "phase-eligibility",
        "--phase",
        "eligibility",
    )
    active["native"] = ScriptedTransport(
        *(native.missing_responses() if action else native.exact_responses())
    )
    command(case, monkeypatch, "observe", "observation")
    reject_foreign_edge(
        case,
        monkeypatch,
        "observation",
        "decision-reference",
        "normal_observation",
    )
    command(case, monkeypatch, "publication", "publication")
    reject_foreign_edge(
        case,
        monkeypatch,
        "publication",
        "observation-reference",
        "normal_publication",
    )
    publication = RubyOperationRecords(case.inputs()).normal_publication()
    assert publication.action_required is action
    publisher = "publish-ruby-" + native.registry.name
    needs = {
        publisher: {"result": "skipped", "outputs": {}},
        "prepare-ruby-publication": {"result": "success", "outputs": {}},
    }
    if action:
        command(case, monkeypatch, "summary", "summary")
        command(case, monkeypatch, "bundle", "bundle")
        for field in ("snapshot-reference", "summary-reference"):
            reject_foreign_edge(
                case, monkeypatch, "bundle", field, "normal_bundle"
            )
        job(case, monkeypatch, publisher)
        monkeypatch.setenv(
            "WDV3_ENVIRONMENT_SENTINEL", native.registry.environment + "/v1"
        )
        command(case, monkeypatch, "authorize", "authorization")
        reject_foreign_edge(
            case,
            monkeypatch,
            "authorization",
            "bundle-reference",
            "normal_authorization",
        )
        command(
            case,
            monkeypatch,
            "claim",
            "phase-pre-marker",
            "--phase",
            "pre-marker",
        )
        active["native"] = ScriptedTransport(*native.missing_responses())
        command(case, monkeypatch, "marker", "marker")
        case.add(
            "fresh-governance",
            (case.directory / "fresh-governance.json").read_bytes(),
        )
        command(
            case, monkeypatch, "claim", "phase-execute", "--phase", "execute"
        )
        reject_foreign_edge(
            case,
            monkeypatch,
            "marker",
            "authorization-reference",
            "normal_marker",
            events=events,
        )
        exchange = (
            [json_response(token_document(), status=201)]
            if native.registry.name == "rubygems"
            else []
        )
        active["native"] = ScriptedTransport(
            *exchange, RubyHttpResponse(200, b"ok"), *native.exact_responses()
        )
        command(case, monkeypatch, "execute", "result")
        requests = active["native"].requests
        assert [item[0] for item in requests] == (
            ["POST"] if exchange else []
        ) + ["POST", "GET", "GET", "GET"]
        assert sum(item[3] == native.original.content for item in requests) == 1
        assert events.count("oidc") == (1 if exchange else 0)
        assert (
            RubyOperationRecords(case.inputs()).normal_result().result
            == "published"
        )
        reject_foreign_edge(
            case,
            monkeypatch,
            "result",
            "mutation-marker-reference",
            "normal_result",
        )
        needs[publisher] = {
            "result": "success",
            "outputs": {
                "publication-step-outcome": "success",
                "terminal-reference": canonicalize(
                    case.references["result"]
                ).decode(),
            },
        }
    else:
        assert not {"authorization", "marker", "bundle", "result"} & set(
            case.references
        )
        assert "oidc" not in events
        assert "approval" not in events

    job(case, monkeypatch, "finalize-attempt")
    if not action:
        advance_clock(monkeypatch, 1)
        command(
            case,
            monkeypatch,
            "claim",
            "phase-zero-action",
            "--phase",
            "zero-action",
        )
        active["native"] = ScriptedTransport(*native.exact_responses())
        command(case, monkeypatch, "zero-proof", "exact-proof")
        case.add(
            "zero-governance",
            (case.directory / "zero-governance.json").read_bytes(),
        )
        assert len(active["native"].requests) == len(native.exact_responses())
    if action:
        reject_cancelled_or_missing_result(
            case, monkeypatch, needs, publisher, events
        )
    monkeypatch.setenv("WDV3_NEEDS", canonicalize(needs).decode())
    command(case, monkeypatch, "finalize", "terminal")
    terminal = case.inputs().document("terminal")
    assert terminal["result"] == ("published" if action else "exact-satisfied")
    assert terminal["independent-audit"] is False
    assert terminal["normal-live-completion"] is False
    case.add("outcome", (case.directory / "outcome.json").read_bytes())
    job(case, monkeypatch, "verify-ruby-operation")
    advance_clock(monkeypatch, 2)
    command(
        case,
        monkeypatch,
        "claim",
        "phase-remote-consumer",
        "--phase",
        "remote-consumer",
    )
    active["native"] = ScriptedTransport(*native.exact_responses())
    command(case, monkeypatch, "remote", "remote-consumer")
    assert len(active["native"].requests) == len(native.exact_responses())
    remote = case.inputs().document("remote-consumer")
    assert remote["consumer"]
    assert remote["independent-audit"] is False
    assert remote["outcome-reference"] == case.references["outcome"]
    assert events.count("authority") == (6 if action else 3)
