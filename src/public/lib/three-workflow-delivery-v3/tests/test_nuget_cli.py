"""Native control entry over modeled services and actual Git sources."""

from __future__ import annotations

# ruff: noqa: D103, SLF001
import base64
import hashlib
import json
import sys
from copy import deepcopy
from dataclasses import replace
from datetime import datetime, timedelta
from pathlib import Path
from platform import python_version
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3 import cli
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.canonical import canonical_sha256, canonicalize
from three_workflow_delivery_v3.records.release import (
    NugetDestinationOperationProfile,
)
from three_workflow_delivery_v3.release import nuget_governance as governance
from three_workflow_delivery_v3.release.governance_git import GovernanceGitRead
from three_workflow_delivery_v3.release.identity import (
    derive_buddy_execution_identity,
    normalize_buddy_live_intent,
    normalize_nuget_buddy_live_intent,
)
from three_workflow_delivery_v3.repository import compiler, dotnet_provider
from three_workflow_delivery_v3.repository.descriptors import NUGET_RELEASE_UNIT

from .release.test_nuget_destination import RESOURCES, _modeled_profile_document
from .release.test_nuget_destination_runtime import NOW, _ready_document
from .release.test_nuget_governance import _client, _document
from .repository.test_dotnet_compiler import _admitted, _native_scenario

TOKEN = "modeled-cli-repository-token"  # noqa: S105


@pytest.fixture(scope="module")
def control_source(tmp_path_factory):
    """Reuse a frozen Git source/Model with modeled external facts."""
    with pytest.MonkeyPatch.context() as patch:
        repo, original, _manifest, result = _native_scenario(
            tmp_path_factory.mktemp("nuget-cli-control"), patch
        )
        intent = normalize_nuget_buddy_live_intent(
            repository="hcoona/three",
            selected_ref="refs/heads/main",
            target=original.target,
            actor="hcoona",
            workflow_run_id=original.workflow_run_id,
        )
        context = cli._live_model_context(intent)
        manifest = compiler.nuget_provider_manifest(
            context, provider_producer="discover-dotnet"
        )
        result = replace(
            result,
            binding=compiler.provider_binding(manifest, "dotnet-nuget-slice"),
        )
        snapshot = compiler.compile_dotnet_repository_model(
            repo, context, manifest, [_admitted(context, manifest, result)]
        )
        model = compiler.admit_repository_model_snapshot(
            canonicalize(snapshot.to_document()),
            expected_context=context,
            expected_digest=snapshot.snapshot_digest,
        )
        return SimpleNamespace(
            repo=repo,
            intent=intent,
            context=context,
            manifest=manifest,
            result=result,
            model=model,
        )


def _write(path, document):
    content = canonicalize(document)
    path.write_bytes(content)
    return "sha256:" + hashlib.sha256(content).hexdigest()


def _uploaded(root, name, document, artifact_id):
    path = root / (name + ".json")
    digest = _write(path, document)
    option = name.replace("_", "-")
    return [
        "--" + option,
        str(path),
        "--" + option + "-digest",
        digest,
        "--" + option + "-artifact-id",
        str(artifact_id),
        "--" + option + "-artifact-digest",
        digest,
    ]


def _set_option(arguments, option, value):
    arguments[arguments.index(option) + 1] = str(value)


@pytest.fixture
def control_case(control_source, tmp_path, monkeypatch):
    source = control_source
    intent_args = _uploaded(
        tmp_path, "intent", source.intent.to_document(), 101
    )
    model_args = _uploaded(
        tmp_path, "repository_model", source.model.snapshot.to_document(), 102
    )
    provider_document = source.result.to_document()
    provider_document.update(
        {
            "provider-request-manifest-digest": source.manifest.manifest_digest,
            "result-digest": source.result.result_digest,
        }
    )
    provider_path = tmp_path / "provider.json"
    provider_digest = _write(provider_path, provider_document)
    evaluate = Mock(
        side_effect=AssertionError("control consumer evaluated target code")
    )
    monkeypatch.setattr(dotnet_provider, "run_native", evaluate)
    return SimpleNamespace(
        source=source,
        root=tmp_path,
        intent_args=intent_args,
        model_args=model_args,
        provider_document=provider_document,
        provider_path=provider_path,
        provider_args=[
            "--provider-result",
            str(provider_path),
            "--provider-artifact-id",
            "103",
            "--provider-artifact-digest",
            provider_digest,
        ],
        current=[
            "--workflow-run-id",
            str(source.intent.workflow_run_id),
            "--run-attempt",
            "1",
            "--target",
            source.intent.target,
        ],
        output=tmp_path / "output.json",
        github_output=tmp_path / "github-output",
        evaluate=evaluate,
    )


def _command(case, command, extra=(), *, output=True):
    arguments = [
        "release",
        "nuget",
        command,
        "--repo-root",
        str(case.source.repo),
        *case.current,
        *case.intent_args,
        *extra,
    ]
    if output:
        arguments += [
            "--output",
            str(case.output),
            "--github-output",
            str(case.github_output),
        ]
    return arguments


def _clock(monkeypatch, instant):
    class Clock(datetime):
        @classmethod
        def now(cls, tz=None):
            return instant.astimezone(tz)

    monkeypatch.setattr(cli, "datetime", Clock)


@pytest.fixture
def enabled_case(control_case, monkeypatch):
    case = control_case
    _clock(monkeypatch, NOW)
    profile = NugetDestinationOperationProfile(
        canonicalize(_modeled_profile_document(RESOURCES))
    )
    document = _ready_document(profile, monkeypatch)
    client = _client(document)
    client.read_source.return_value = GovernanceGitRead(
        case.source.intent.target, "sha1", "e" * 40, canonicalize(document)
    )
    client_factory = Mock(return_value=client)
    monkeypatch.setattr(cli, "GitHubGovernanceClient", client_factory)
    response = native.NuGetHttpResponse(
        "https://api.github.com/modeled-control", 200, (), b'{"modeled":true}'
    )
    intent = case.source.intent
    platform = governance.NuGetLiveControlFacts(
        intent.target,
        intent.target,
        intent.target,
        intent.workflow_run_id,
        intent.target,
        "c" * 40,
        "d" * 40,
        123,
        NOW,
        (response,),
        (response,),
    )
    platform_reader = Mock(return_value=platform)
    profile_reader = Mock(return_value=profile)
    monkeypatch.setattr(cli, "read_nuget_live_control_facts", platform_reader)
    monkeypatch.setattr(cli, "_nuget_live_profile", profile_reader)
    case.client, case.document, case.profile = client, document, profile
    case.platform_reader, case.profile_reader = platform_reader, profile_reader
    case.client_factory = client_factory
    return case


def _evaluate(case):
    return cli.main(
        _command(
            case,
            "evaluate-live-eligibility",
            [
                *case.model_args,
                "--github-token",
                TOKEN,
                "--helper-dll",
                str(case.root / "authority.dll"),
            ],
        )
    )


def _eligibility_args(case):
    destination = case.root / "eligibility.json"
    destination.write_bytes(case.output.read_bytes())
    digest = canonical_sha256(json.loads(destination.read_bytes()))
    case.output.unlink()
    return [
        "--live-eligibility-decision",
        str(destination),
        "--live-eligibility-artifact-id",
        "201",
        "--live-eligibility-artifact-digest",
        digest,
        "--live-eligibility-payload-digest",
        digest,
    ]


@pytest.mark.parametrize(
    "fault", [None, "repository", "actor", "selected-ref", "run-attempt"]
)
def test_nuget_cli_normalizes_only_confirmed_buddy_request(control_case, fault):
    case = control_case
    arguments = [
        "release",
        "nuget",
        "normalize-live-request",
        *case.current,
        "--repository",
        "hcoona/three",
        "--actor",
        "hcoona",
        "--selected-ref",
        "refs/heads/main",
        "--output",
        str(case.output),
    ]
    if fault:
        _set_option(
            arguments,
            "--" + fault,
            "2" if fault == "run-attempt" else "unaccepted",
        )
    assert cli.main(arguments) == (1 if fault else 0)
    if fault:
        assert not case.output.exists()
    else:
        assert (
            json.loads(case.output.read_bytes())
            == case.source.intent.to_document()
        )
        assert case.source.intent.release_unit == NUGET_RELEASE_UNIT
        assert case.source.intent.workflow_sha == case.source.intent.target


def test_nuget_cli_keeps_npm_normalization_separate(control_case):
    case = control_case
    args = {
        "repository": "hcoona/three",
        "selected_ref": "refs/heads/main",
        "target": case.source.intent.target,
        "actor": "hcoona",
        "workflow_run_id": case.source.intent.workflow_run_id,
    }
    npm = normalize_buddy_live_intent(**args)
    assert npm.release_unit != case.source.intent.release_unit
    arguments = [
        "release",
        "normalize-live-request",
        *case.current,
        "--repository",
        "hcoona/three",
        "--actor",
        "hcoona",
        "--selected-ref",
        "refs/heads/main",
        "--output",
        str(case.output),
    ]
    assert cli.main(arguments) == 0
    assert json.loads(case.output.read_bytes()) == npm.to_document()


def test_dotnet_provider_cli_routes_native_materialization(
    control_case, monkeypatch
):
    case = control_case
    provider = Mock(return_value=case.source.result)
    monkeypatch.setattr(
        dotnet_provider, "provide_dotnet_repository_facts", provider
    )
    helper = case.root / "provider.dll"
    evidence = case.root / "native-evidence"
    assert (
        cli.main(
            [
                "repository",
                "provide-dotnet",
                *case.current,
                "--repo-root",
                str(case.source.repo),
                "--request-id",
                case.source.intent.request_id,
                "--purpose",
                "live-release",
                "--compiler-producer",
                "compile-live-model",
                "--provider-producer",
                "discover-dotnet",
                "--control",
                case.source.context.control,
                "--fetch-depth",
                "0",
                "--no-persist-credentials",
                "--helper-dll",
                str(helper),
                "--evidence-directory",
                str(evidence),
                "--output",
                str(case.output),
            ]
        )
        == 0
    )
    provider.assert_called_once_with(
        case.source.repo,
        case.source.result.binding,
        cli.CheckoutMaterialization(fetch_depth=0, credentials_persisted=False),
        helper=dotnet_provider.NativeNuGetHelper(helper),
        evidence_directory=evidence,
    )
    assert json.loads(case.output.read_bytes()) == case.provider_document


def test_nuget_cli_compiles_uploaded_provider_without_evaluation(
    control_case, monkeypatch
):
    case = control_case
    provider = Mock(
        side_effect=AssertionError("Provider must not run in control consumer")
    )
    monkeypatch.setattr(
        dotnet_provider, "provide_dotnet_repository_facts", provider
    )
    assert (
        cli.main(_command(case, "compile-live-model", case.provider_args)) == 0
    )
    assert (
        json.loads(case.output.read_bytes())
        == case.source.model.snapshot.to_document()
    )
    execution = canonical_sha256(
        derive_buddy_execution_identity(case.source.intent).to_document()
    ).removeprefix("sha256:")
    assert (
        "execution-concurrency-key=" + execution
        in case.github_output.read_text()
    )
    provider.assert_not_called()
    case.evaluate.assert_not_called()


@pytest.mark.parametrize(
    "fault",
    [
        "manifest",
        "request",
        "run",
        "producer",
        "catalog",
        "result-digest",
        "transport",
        "artifact-id",
        "missing",
        "npm-schema",
        "credentials",
        "source-bytes",
        "npm-intent",
        "rerun",
    ],
)
def test_nuget_cli_rejects_substituted_provider_authority(  # noqa: C901
    control_case, fault
):
    case = control_case
    document = deepcopy(case.provider_document)
    if fault == "manifest":
        document["provider-request-manifest-digest"] = "sha256:" + "f" * 64
    elif fault in ("request", "run", "producer", "catalog"):
        key = {
            "request": "request-id",
            "run": "workflow-run-id",
            "producer": "producer",
            "catalog": "catalog-digest",
        }[fault]
        document["binding"][key] = {
            "request": "other-request",
            "run": 999,
            "producer": "other-provider",
            "catalog": "sha256:" + "f" * 64,
        }[fault]
    elif fault == "missing":
        del document["nbgv"]
    elif fault == "npm-schema":
        document["schema"] = "workflow-delivery/v3/node-provider-result"
    elif fault == "credentials":
        document["checkout"]["credentials-persisted"] = True
    elif fault == "source-bytes":
        path = document["source-input-manifest"][0]["path"]
        document["source-input-manifest"][0]["content-digest"] = (
            "sha256:" + "f" * 64
        )
        next(
            item for item in document["global-inputs"] if item["path"] == path
        )["content-digest"] = "sha256:" + "f" * 64
    core = {
        key: value
        for key, value in document.items()
        if key not in ("result-digest", "provider-request-manifest-digest")
    }
    document["result-digest"] = canonical_sha256(core)
    if fault == "result-digest":
        document["result-digest"] = "sha256:" + "f" * 64
    digest = _write(case.provider_path, document)
    _set_option(case.provider_args, "--provider-artifact-digest", digest)
    if fault == "transport":
        _set_option(
            case.provider_args,
            "--provider-artifact-digest",
            "sha256:" + "e" * 64,
        )
    elif fault == "artifact-id":
        _set_option(case.provider_args, "--provider-artifact-id", 0)
    elif fault == "npm-intent":
        intent = replace(
            case.source.intent, release_unit="hcoona-release-smoke-npm"
        )
        case.intent_args = _uploaded(
            case.root, "intent", intent.to_document(), 101
        )
    elif fault == "rerun":
        _set_option(case.current, "--run-attempt", 2)
    assert (
        cli.main(_command(case, "compile-live-model", case.provider_args)) == 1
    )
    assert not case.output.exists()
    assert not case.github_output.exists()
    case.evaluate.assert_not_called()


@pytest.mark.parametrize("state", ["disabled", "unadmitted"])
def test_nuget_cli_blocks_before_native_collection(
    enabled_case, monkeypatch, state
):
    case = enabled_case
    if state == "disabled":
        document = _document()
        case.client.read_source.return_value = GovernanceGitRead(
            case.source.intent.target, "sha1", "e" * 40, canonicalize(document)
        )
    else:
        monkeypatch.setattr(
            governance, "_ADMITTED_NUGET_NATIVE_GENERATIONS", frozenset()
        )
        monkeypatch.setattr(
            governance, "_ADMITTED_NUGET_ATOMIC_CONTRACTS", frozenset()
        )
    assert _evaluate(case) == 1
    if state == "disabled":
        decision = json.loads(case.output.read_bytes())
        assert decision["result"] == "blocked"
        assert decision["platform"] is decision["profile"] is None
        assert decision["diagnostics"] == ["governance-live-disabled"]
    else:
        # The protected source parser rejects an enabled unadmitted contract.
        assert not case.output.exists()
    case.platform_reader.assert_not_called()
    case.profile_reader.assert_not_called()
    case.evaluate.assert_not_called()


def test_nuget_cli_eligibility_and_attempt_round_trip(enabled_case):
    case = enabled_case
    assert _evaluate(case) == 0
    decision = json.loads(case.output.read_bytes())
    assert decision["result"] == "pass"
    assert decision["diagnostics"] == []
    assert decision["profile"] == case.profile.to_document()
    assert "static-reference" not in decision
    case.platform_reader.assert_called_once()
    read = case.platform_reader.call_args.kwargs
    assert (
        read["target"]
        == read["workflow_sha"]
        == read["control_sha"]
        == case.source.intent.target
    )
    assert read["workflow_run_id"] == case.source.intent.workflow_run_id
    assert read["workflow_path"] == case.source.intent.workflow_path
    assert read["selected_ref"] == "refs/heads/main"
    case.profile_reader.assert_called_once()
    case.client_factory.assert_called_once_with(
        repository="hcoona/three", token=TOKEN
    )
    assert case.client.read_source.call_count == 2  # noqa: PLR2004
    eligibility = _eligibility_args(case)
    assert (
        cli.main(
            _command(
                case, "admit-live-eligibility", [*case.model_args, *eligibility]
            )
        )
        == 0
    )
    assert json.loads(case.output.read_bytes()) == decision
    case.output.unlink()
    assert (
        cli.main(
            _command(
                case, "bind-live-attempt", [*case.model_args, *eligibility]
            )
        )
        == 0
    )
    binding = json.loads(case.output.read_bytes())
    attempt = _uploaded(case.root, "attempt_binding", binding, 202)
    assert (
        cli.main(
            _command(
                case,
                "admit-live-attempt",
                [
                    *case.model_args,
                    *eligibility,
                    *attempt,
                    "--admission-mode",
                    "current-freshness",
                ],
                output=False,
            )
        )
        == 0
    )
    assert "attempt-binding-digest=" in case.github_output.read_text()
    case.evaluate.assert_not_called()


def test_nuget_cli_flag_off_during_collection_stays_blocked(enabled_case):
    case = enabled_case
    case.client.read_source.side_effect = [
        GovernanceGitRead(
            case.source.intent.target,
            "sha1",
            "e" * 40,
            canonicalize(case.document),
        ),
        GovernanceGitRead(
            case.source.intent.target,
            "sha1",
            "f" * 40,
            canonicalize(_document()),
        ),
    ]
    assert _evaluate(case) == 1
    decision = json.loads(case.output.read_bytes())
    assert decision["diagnostics"] == ["governance-live-disabled"]
    assert decision["platform"] is decision["profile"] is None
    case.profile_reader.assert_called_once()


def test_nuget_cli_rechecks_actual_control_context(enabled_case):
    case = enabled_case
    case.platform_reader.return_value = replace(
        case.platform_reader.return_value, workflow_run_id=999
    )
    assert _evaluate(case) == 1
    assert not case.output.exists()


@pytest.mark.parametrize(
    "fault", ["transport", "payload", "run", "source", "npm-schema"]
)
def test_nuget_cli_rejects_substituted_eligibility(enabled_case, fault):
    case = enabled_case
    assert _evaluate(case) == 0
    eligibility = _eligibility_args(case)
    path = Path(eligibility[1])
    document = json.loads(path.read_bytes())
    if fault == "run":
        document["context"]["workflow-run-id"] = 999
    elif fault == "source":
        document["governance"]["path"] = (
            ".github/governance/workflow-delivery-v3-buddy.yml"
        )
    elif fault == "npm-schema":
        document["schema"] = "workflow-delivery/v3/live-eligibility-decision"
    digest = _write(path, document)
    _set_option(eligibility, "--live-eligibility-artifact-digest", digest)
    _set_option(eligibility, "--live-eligibility-payload-digest", digest)
    if fault in ("transport", "payload"):
        key = "artifact" if fault == "transport" else "payload"
        _set_option(
            eligibility,
            "--live-eligibility-" + key + "-digest",
            "sha256:" + "f" * 64,
        )
    assert (
        cli.main(
            _command(
                case, "bind-live-attempt", [*case.model_args, *eligibility]
            )
        )
        == 1
    )
    assert not case.output.exists()


@pytest.mark.parametrize(
    "fault",
    ["eligibility-artifact", "attempt-transport", "authority", "current-run"],
)
def test_nuget_cli_attempt_admission_rechecks_authority(enabled_case, fault):
    case = enabled_case
    assert _evaluate(case) == 0
    eligibility = _eligibility_args(case)
    assert (
        cli.main(
            _command(
                case, "bind-live-attempt", [*case.model_args, *eligibility]
            )
        )
        == 0
    )
    binding = json.loads(case.output.read_bytes())
    if fault == "authority":
        binding["repository-model-digest"] = "sha256:" + "f" * 64
    attempt = _uploaded(case.root, "attempt_binding", binding, 202)
    if fault == "eligibility-artifact":
        _set_option(eligibility, "--live-eligibility-artifact-id", 999)
    elif fault == "attempt-transport":
        _set_option(
            attempt, "--attempt-binding-artifact-digest", "sha256:" + "f" * 64
        )
    elif fault == "current-run":
        _set_option(case.current, "--workflow-run-id", 999)
    assert (
        cli.main(
            _command(
                case,
                "admit-live-attempt",
                [
                    *case.model_args,
                    *eligibility,
                    *attempt,
                    "--admission-mode",
                    "authorization-replay",
                ],
                output=False,
            )
        )
        == 1
    )


def test_nuget_cli_replay_does_not_relax_fresh_attempt_binding(
    enabled_case, monkeypatch
):
    case = enabled_case
    assert _evaluate(case) == 0
    eligibility = _eligibility_args(case)
    assert (
        cli.main(
            _command(
                case, "bind-live-attempt", [*case.model_args, *eligibility]
            )
        )
        == 0
    )
    attempt = _uploaded(
        case.root, "attempt_binding", json.loads(case.output.read_bytes()), 202
    )
    case.output.unlink()
    _clock(monkeypatch, NOW + timedelta(days=100))
    assert (
        cli.main(
            _command(
                case, "bind-live-attempt", [*case.model_args, *eligibility]
            )
        )
        == 1
    )
    assert not case.output.exists()
    for mode, expected in (
        ("current-freshness", 1),
        ("authorization-replay", 0),
    ):
        assert (
            cli.main(
                _command(
                    case,
                    "admit-live-attempt",
                    [
                        *case.model_args,
                        *eligibility,
                        *attempt,
                        "--admission-mode",
                        mode,
                    ],
                    output=False,
                )
            )
            == expected
        )


@pytest.mark.parametrize(
    "fault", [None, "redirect", "http", "encoded", "profile-mismatch"]
)
def test_nuget_profile_collection_uses_exact_native_discovery(
    tmp_path, monkeypatch, fault
):
    response = native.NuGetHttpResponse(
        native.NUGET_SERVICE_INDEX, 200, (), b'{"version":"3.0.0"}'
    )
    if fault == "redirect":
        response = replace(response, url="https://example.invalid/index.json")
    elif fault == "http":
        response = replace(response, status=403)
    elif fault == "encoded":
        response = replace(response, headers=(("Content-Encoding", "gzip"),))
    transport = Mock(spec=native.NuGetReadTransport)
    transport.get.return_value = response
    monkeypatch.setattr(
        native, "NuGetHttpTransport", Mock(return_value=transport)
    )
    helper = Mock(spec=dotnet_provider.NativeNuGetHelper)
    helper.service_resources.return_value = {
        "packageBaseAddress": RESOURCES.package_base_address,
        "packagePublish": RESOURCES.package_publish,
    }
    helper_factory = Mock(return_value=helper)
    monkeypatch.setattr(dotnet_provider, "NativeNuGetHelper", helper_factory)
    if (
        sys.implementation.name != "cpython"
        or python_version() != native.NUGET_PYTHON_VERSION
    ):
        monkeypatch.setattr(
            native, "nuget_operation_profile", _modeled_profile_document
        )
    if fault == "profile-mismatch":
        monkeypatch.setattr(
            native,
            "nuget_operation_profile",
            Mock(side_effect=native.NuGetAdapterError("runtime is not pinned")),
        )
    arguments = SimpleNamespace(
        github_token=TOKEN, helper_dll=str(tmp_path / "authority.dll")
    )
    if fault:
        with pytest.raises(
            (ValueError, native.NuGetTransportError),
            match=r"readback|Encoded|pinned",
        ):
            cli._nuget_live_profile(arguments)
        if fault != "profile-mismatch":
            helper.service_resources.assert_not_called()
    else:
        profile = cli._nuget_live_profile(arguments)
        expected = NugetDestinationOperationProfile(
            canonicalize(native.nuget_operation_profile(RESOURCES))
        )
        assert profile == expected
        helper.service_resources.assert_called_once_with(response.body)
        helper_factory.assert_called_once_with(tmp_path / "authority.dll")
    transport.get.assert_called_once()
    assert transport.get.call_args.args == (native.NUGET_SERVICE_INDEX,)
    headers = dict(transport.get.call_args.kwargs["headers"])
    assert headers["Authorization"] == "Basic " + base64.b64encode(
        ("hcoona:" + TOKEN).encode("ascii")
    ).decode("ascii")
    assert headers["Accept-Encoding"] == "identity"
    assert transport.get.call_args.kwargs["timeout"] == 60  # noqa: PLR2004
    assert transport.get.call_args.kwargs["max_bytes"] == 8 * 1024 * 1024


def test_nuget_cli_changed_initial_main_blocks_native_collection(enabled_case):
    case = enabled_case
    case.client.read_source.return_value = GovernanceGitRead(
        "f" * 40, "sha1", "e" * 40, canonicalize(case.document)
    )
    assert _evaluate(case) == 1
    decision = json.loads(case.output.read_bytes())
    assert decision["result"] == "blocked"
    assert "initial-protected-main-target-changed" in decision["diagnostics"]
    case.platform_reader.assert_not_called()
    case.profile_reader.assert_not_called()


def test_nuget_cli_control_transport_failure_has_no_decision(enabled_case):
    case = enabled_case
    case.platform_reader.side_effect = native.NuGetTransportError(
        "incomplete readback"
    )
    assert _evaluate(case) == 1
    assert not case.output.exists()
    case.profile_reader.assert_not_called()
