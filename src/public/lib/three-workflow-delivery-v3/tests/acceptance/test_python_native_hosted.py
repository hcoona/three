"""Current-run native authority, original artifact binding and hosted replay."""

import subprocess
from dataclasses import replace
from pathlib import Path
from urllib.parse import urlencode

import pytest
import yaml
from three_workflow_delivery_v3.acceptance import python_native
from three_workflow_delivery_v3.acceptance.python_native import (
    audit_native,
    probe,
    read_artifact,
)
from three_workflow_delivery_v3.acceptance.python_native_contract import (
    WORKFLOW,
    NativeRequest,
    pack_bundle,
    unpack_bundle,
)
from three_workflow_delivery_v3.acceptance.python_native_github import (
    prove_native_approval,
    validate_hosted,
)
from three_workflow_delivery_v3.acceptance.python_native_suite import (
    audit_suite,
)
from three_workflow_delivery_v3.adapters.pypi import PythonHttpResponse
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.platform.python_github import (
    PythonGitHubRuntime,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository.python_provider import python_digest

from ..adapters.test_pypi import FakeHttp
from ..repository import test_python_provider as provider_tests
from . import test_python_native_fixture as fixture_tests
from . import test_python_native_suite as suite_tests
from .test_python_native_contract import request_document
from .test_python_native_suite import _TOKEN, _consumer

modeled_fixtures = fixture_tests.modeled_fixtures
native_python_provider_repository = (
    provider_tests.native_python_provider_repository
)
deny_real_registry = suite_tests.deny_real_registry
_ROOT = Path(__file__).resolve().parents[6]
_RUN = 911
_TOOLING = "c" * 40
_GITHUB_TOKEN = "synthetic-github-token"  # noqa: S105
_ASSERTION = "synthetic-oidc-assertion"
_OIDC_TOKEN = "synthetic-oidc-request-token"  # noqa: S105
_OIDC_URL = (
    "https://run.actions.githubusercontent.com/idtoken"
    "?api-version=2.0&audience=testpypi"
)
_PROOF_MAXIMUM = 8


def hosted_environment(tooling=_TOOLING):
    """Supply exact local execution facts without granting hosted authority."""
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR": "hcoona",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": tooling,
        "GITHUB_WORKFLOW_SHA": tooling,
        "GITHUB_WORKFLOW_REF": f"hcoona/three/{WORKFLOW}@refs/heads/main",
        "GITHUB_RUN_ATTEMPT": "1",
        "GITHUB_RUN_ID": str(_RUN),
        "RUNNER_OS": "Linux",
        "GITHUB_TOKEN": _GITHUB_TOKEN,
        "WDV3_APPROVAL_ENVIRONMENT_MARKER": "synthetic-environment-sentinel",
        "ACTIONS_ID_TOKEN_REQUEST_URL": "https://run.actions.githubusercontent.com/idtoken?api-version=2.0",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN": _OIDC_TOKEN,
    }


def github_facts(request, *, deployment_count=1):
    """Model only bounded external API responses and their expected routes."""
    run = {
        "id": _RUN,
        "head_sha": _TOOLING,
        "run_attempt": 1,
        "event": "workflow_dispatch",
        "path": WORKFLOW,
        "head_branch": "main",
        "actor": {"id": 712433, "login": "hcoona"},
    }
    review = {
        "environments": [{"id": 17, "name": request.registry.environment}],
        "state": "approved",
        "user": {"id": 712433, "login": "hcoona"},
    }
    deployments = [
        {
            "id": 100 + number,
            "sha": _TOOLING,
            "environment": request.registry.environment,
        }
        for number in range(deployment_count)
    ]
    query = urlencode(
        {
            "sha": _TOOLING,
            "environment": request.registry.environment,
            "per_page": 100,
        }
    )
    facts = [
        (f"/repos/hcoona/three/actions/runs/{_RUN}", run),
        (
            f"/repos/hcoona/three/actions/runs/{_RUN}/approvals?per_page=100",
            [review],
        ),
        (f"/repos/hcoona/three/deployments?{query}", deployments),
    ]
    for number, deployment in enumerate(deployments):
        facts.append(
            (
                f"/repos/hcoona/three/deployments/{deployment['id']}/statuses?per_page=100",
                [
                    {
                        "state": "in_progress" if number == 0 else "inactive",
                        "log_url": f"https://github.com/hcoona/three/actions/runs/{_RUN}/job/22",
                    }
                ],
            )
        )
    return facts


@pytest.mark.parametrize("deployment_count", [1, 5])
@pytest.mark.parametrize("workflow_path", [WORKFLOW, WORKFLOW + "@main"])
def test_python_native_approval_reads_only_bounded_current_run_facts(
    deployment_count,
    workflow_path,
):
    """One owner-approved deployment binds raw facts without paged discovery."""
    request = NativeRequest(canonicalize(request_document()))
    facts = github_facts(request, deployment_count=deployment_count)
    facts[0][1]["path"] = workflow_path
    http = FakeHttp(
        *(
            PythonHttpResponse(200, canonicalize(document), "application/json")
            for _, document in facts
        )
    )
    proof = parse_canonical_json(
        prove_native_approval(
            request,
            _RUN,
            _TOOLING,
            "synthetic-environment-sentinel",
            PythonGitHubRuntime(_GITHUB_TOKEN, http),
        )
    )
    assert proof["request-digest"] == request.digest
    assert proof["run-id"] == _RUN
    assert proof["deployment-id"] == facts[2][1][0]["id"]
    assert proof["statuses"] == {
        str(item["id"]): facts[index + 3][1]
        for index, item in enumerate(facts[2][1])
    }
    assert [call[1] for call in http.calls] == [
        "https://api.github.com" + route for route, _ in facts
    ]
    assert len(http.calls) <= _PROOF_MAXIMUM
    assert _GITHUB_TOKEN.encode() not in canonicalize(proof)


@pytest.mark.parametrize(
    "workflow_path",
    [
        ".github/workflows/foreign.yml@main",
        WORKFLOW + "@topic",
        WORKFLOW + "@refs/heads/main",
        WORKFLOW + "@main@other",
    ],
)
def test_python_native_approval_rejects_foreign_workflow_path(workflow_path):
    """The documented main suffix does not admit other workflow/ref strings."""
    request = NativeRequest(canonicalize(request_document()))
    route, run = github_facts(request)[0]
    run["path"] = workflow_path
    http = FakeHttp(
        PythonHttpResponse(200, canonicalize(run), "application/json")
    )
    with pytest.raises(ValueError, match="native"):
        prove_native_approval(
            request,
            _RUN,
            _TOOLING,
            "synthetic-environment-sentinel",
            PythonGitHubRuntime(_GITHUB_TOKEN, http),
        )
    assert [(call[0], call[1]) for call in http.calls] == [
        ("GET", "https://api.github.com" + route)
    ]


@pytest.mark.parametrize(
    "change",
    [
        "sentinel",
        "attempt",
        "actor",
        "approval-owner",
        "approval-environment",
        "full-approvals",
        "six-deployments",
        "full-statuses",
        "foreign-run-link",
        "duplicate-deployment",
    ],
)
def test_python_native_approval_rejects_incomplete_or_foreign_authority(  # noqa: C901 - authority variants
    change,
):
    """Wrong authority or an incomplete inventory stops bounded API reads."""
    request = NativeRequest(canonicalize(request_document()))
    facts = github_facts(
        request, deployment_count=6 if change == "six-deployments" else 1
    )
    sentinel = "synthetic-environment-sentinel"
    if change == "sentinel":
        sentinel = "different"
    elif change == "attempt":
        facts[0][1]["run_attempt"] = 2
    elif change == "actor":
        facts[0][1]["actor"]["id"] = 1
    elif change == "approval-owner":
        facts[1][1][0]["user"]["login"] = "other"
    elif change == "approval-environment":
        facts[1][1][0]["environments"][0]["id"] = 99
    elif change == "full-approvals":
        facts[1] = (facts[1][0], facts[1][1] * 100)
    elif change == "full-statuses":
        facts[3] = (facts[3][0], facts[3][1] * 100)
    elif change == "foreign-run-link":
        facts[3][1][0]["log_url"] = (
            "https://github.com/hcoona/three/actions/runs/999/job/22"
        )
    elif change == "duplicate-deployment":
        facts[2][1].append(dict(facts[2][1][0]))
    http = FakeHttp(
        *(
            PythonHttpResponse(200, canonicalize(document), "application/json")
            for _, document in facts
        )
    )
    with pytest.raises(ValueError, match=r"native|owner approval"):
        prove_native_approval(
            request,
            _RUN,
            _TOOLING,
            sentinel,
            PythonGitHubRuntime(_GITHUB_TOKEN, http),
        )
    assert len(http.calls) <= min(len(facts), _PROOF_MAXIMUM)
    if change == "sentinel":
        assert http.calls == []


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_ACTOR_ID", "1"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/topic"),
        ("GITHUB_REF_PROTECTED", "false"),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("GITHUB_WORKFLOW_SHA", "d" * 40),
        ("RUNNER_OS", "Windows"),
    ],
)
def test_python_native_hosted_context_rejects_before_git_or_effects(
    tmp_path, field, value
):
    """Foreign execution fails before even reading a nonexistent checkout."""
    environment = hosted_environment()
    environment[field] = value
    with pytest.raises(ValueError, match="execution context"):
        validate_hosted(
            tmp_path,
            environment,
            _TOOLING,
            NativeRequest(canonicalize(request_document())),
        )


def _artifact(files, tmp_path):
    content = pack_bundle(files)
    path = tmp_path / "prepared.zip"
    path.write_bytes(content)
    reference = ArtifactReference(
        1001,
        python_digest(content),
        f"https://github.com/hcoona/three/actions/runs/{_RUN}/artifacts/1001",
        path.name,
        python_digest(content),
    )
    return path, reference


@pytest.mark.parametrize(
    "change",
    [
        None,
        "service-digest",
        "payload-digest",
        "run",
        "payload-name",
        "original-bytes",
    ],
)
def test_python_native_artifact_checks_both_digest_bindings(tmp_path, change):
    """Either digest or immutable run/path mismatch blocks archive admission."""
    files = {"raw.bin": b"exact original bytes"}
    path, reference = _artifact(files, tmp_path)
    if change == "service-digest":
        reference = replace(reference, artifact_digest="sha256:" + "f" * 64)
    elif change == "payload-digest":
        reference = replace(reference, payload_digest="sha256:" + "f" * 64)
    elif change == "run":
        reference = replace(
            reference,
            artifact_url=reference.artifact_url.replace(str(_RUN), "999"),
        )
    elif change == "payload-name":
        reference = replace(reference, payload_path="foreign.zip")
    elif change == "original-bytes":
        path.write_bytes(pack_bundle({"raw.bin": b"different bytes"}))
    if change is None:
        assert read_artifact(path, reference, _RUN) == files
    else:
        with pytest.raises(ValueError, match="transport or payload"):
            read_artifact(path, reference, _RUN)


class HostedBoundary:
    """Finite GitHub/OIDC outcomes plus the actual-adapter registry fake."""

    def __init__(self, request, fixtures, output):
        """Retain explicit calls and inspect durable proof before capability."""
        self.request_spec = request
        self.facts = github_facts(request)
        self.registry = suite_tests.RegistryBoundary(fixtures)
        self.output = output
        self.calls = []

    def request(self, method, url, headers, body, maximum_bytes):
        """Forward scenario requests after the actual credential sequence."""
        self.calls.append((method, url))
        if url.startswith("https://api.github.com/"):
            route, data = self.facts.pop(0)
            assert url == "https://api.github.com" + route
            return PythonHttpResponse(
                200, canonicalize(data), "application/json"
            )
        if url == _OIDC_URL:
            assert (self.output / "approval.json").is_file()
            assert (self.output / "binding.json").is_file()
            assert (self.output / "platform.json").is_file()
            return PythonHttpResponse(
                200, canonicalize({"value": _ASSERTION}), "application/json"
            )
        if url.endswith("/_/oidc/mint-token"):
            assert parse_canonical_json(body) == {"token": _ASSERTION}
            return PythonHttpResponse(
                200,
                canonicalize({"token": _TOKEN}),
                "application/json",
            )
        return self.registry.request(method, url, headers, body, maximum_bytes)


@pytest.fixture
def hosted_probe(modeled_fixtures, tmp_path, monkeypatch):
    """Execute actual probe orchestration with replaced external transport."""
    request = fixture_tests.fixture_request(modeled_fixtures)
    prepared = modeled_fixtures.files()
    prepared["request.json"] = request.content
    prepared["binding.json"] = canonicalize(
        {
            "request-digest": request.digest,
            "tooling-sha": _TOOLING,
            "run-id": _RUN,
            "run-attempt": 1,
            "producer": "prepare-python-native",
        }
    )
    _, reference = _artifact(prepared, tmp_path)
    output = tmp_path / "probe"
    http = HostedBoundary(request, modeled_fixtures, output)
    monkeypatch.setattr(python_native, "PythonHttpsTransport", lambda: http)

    def denied(*_args, **_kwargs):
        pytest.fail("Probe must not execute target or build subprocesses")

    monkeypatch.setattr(subprocess, "run", denied)
    probe(
        request,
        prepared,
        reference,
        _RUN,
        _TOOLING,
        output,
        hosted_environment(),
    )
    files = {
        path.relative_to(output).as_posix(): path.read_bytes()
        for path in output.rglob("*")
        if path.is_file()
    }
    return request, prepared, reference, files, http


def test_python_native_probe_orders_authority_and_never_builds(
    hosted_probe,
):
    """Actual hosted proof and token stages precede finite original uploads."""
    _, _, _, files, http = hosted_probe
    assert http.calls[:4] == [
        ("GET", "https://api.github.com" + route)
        for route, _ in github_facts(http.request_spec)
    ]
    assert http.calls[4] == ("GET", _OIDC_URL)
    assert http.calls[5] == ("POST", "https://test.pypi.org/_/oidc/mint-token")
    assert "suite/suite.json" in files
    for content in files.values():
        assert all(
            secret.encode() not in content
            for secret in (
                _GITHUB_TOKEN,
                _OIDC_TOKEN,
                _ASSERTION,
                _TOKEN,
            )
        )


@pytest.mark.parametrize(
    "change",
    [
        None,
        "raw-approval",
        "raw-route",
        "extra-proof",
        "binding",
        "missing-suite",
        "missing-credentials",
        "extra-token-exchange",
    ],
)
def test_python_native_audit_replays_original_hosted_proof(
    hosted_probe, monkeypatch, change
):
    """No stored approval summary substitutes for original current-run reads."""
    request, prepared, reference, files, http = hosted_probe
    if change == "raw-approval":
        reviews = parse_json_strict(files["github/1.body"])
        reviews[0]["state"] = "rejected"
        files["github/1.body"] = canonicalize(reviews)
    elif change == "raw-route":
        meta = parse_canonical_json(files["github/0.json"])
        meta["url"] += "/foreign"
        files["github/0.json"] = canonicalize(meta)
    elif change == "extra-proof":
        files["github/8.body"] = b"unbound extra source"
    elif change == "binding":
        binding = parse_canonical_json(files["binding.json"])
        binding["run-id"] = 999
        files["binding.json"] = canonicalize(binding)
    elif change == "missing-suite":
        files.pop("suite/suite.json")
    elif change == "missing-credentials":
        files.pop("credentials.json")
    elif change == "extra-token-exchange":
        files["credentials.json"] = canonicalize(
            {"oidc-requests": 1, "token-exchanges": 2}
        )
    calls = []

    def audited(request, fixtures, retained):
        return audit_suite(
            request, fixtures, retained, consumer=_consumer(calls)
        )

    monkeypatch.setattr(python_native, "audit_suite", audited)
    original_calls = tuple(http.calls)
    if change is None:
        result = audit_native(
            request, prepared, files, reference, _RUN, _TOOLING
        )
        assert result["approval.json"] == files["approval.json"]
        assert (
            parse_canonical_json(result["audit.json"])["native-admission"]
            is False
        )
        assert {item.digest for item in calls} == {
            item.digest for item in http.registry.stored.values()
        }
    else:
        with pytest.raises((ValueError, KeyError)):
            audit_native(request, prepared, files, reference, _RUN, _TOOLING)
        assert calls == []
    assert tuple(http.calls) == original_calls


def test_python_native_workflow_scopes_authority_and_retains_evidence():
    """Actual YAML admits manual protected execution and isolated OIDC only."""
    workflow = yaml.safe_load((_ROOT / WORKFLOW).read_text())
    assert set(workflow["on"]) == {"workflow_dispatch"}
    assert workflow["permissions"] == {}
    jobs = workflow["jobs"]
    assert {
        name
        for name, job in jobs.items()
        if job["permissions"].get("id-token") == "write"
    } == {"probe"}
    assert {name for name, job in jobs.items() if "environment" in job} == {
        "probe"
    }
    assert jobs["probe"]["timeout-minutes"] == 15  # noqa: PLR2004 - protocol ceiling
    assert workflow["concurrency"]["cancel-in-progress"] is False
    assert (
        jobs["probe"]["environment"]
        == "workflow-delivery-v3-python-${{ inputs.registry }}"
    )
    for job in jobs.values():
        guard = job["if"]
        for check in (
            "github.run_attempt == 1",
            "github.ref_protected",
            "github.sha == inputs.tooling_sha",
            "github.actor_id == '712433'",
        ):
            assert check in guard
        for step in job["steps"]:
            if step.get("uses", "").startswith("actions/checkout@"):
                assert step["with"]["ref"] == "${{ github.sha }}"
                assert step["with"]["fetch-depth"] == 0
                assert step["with"]["persist-credentials"] is False
            elif step.get("uses", "").startswith("actions/upload-artifact@"):
                assert step["with"]["archive"] is False
                assert step["with"]["overwrite"] is False
                assert step["with"]["retention-days"] == 45  # noqa: PLR2004 - protocol retention
            elif step.get("uses", "").startswith("actions/download-artifact@"):
                assert "artifact-ids" in step["with"]
                assert "name" not in step["with"]
                assert step["with"]["digest-mismatch"] == "error"
                assert step["with"]["skip-decompress"] is True
    probe_steps = jobs["probe"]["steps"]
    execute = next(
        i
        for i, step in enumerate(probe_steps)
        if "python_native probe " in step.get("run", "")
    )
    assert all(step["if"] == "always()" for step in probe_steps[execute + 1 :])
    assert all(
        "build-fixtures" not in step.get("run", "")
        and "prepare " not in step.get("run", "")
        and "dotnet " not in step.get("run", "")
        for step in probe_steps
    )


def test_python_native_hosted_binds_actual_main_and_ancestors(
    native_python_provider_repository,
):
    """Real Git binds ancestors and rejects changed protected main."""
    source, target_a = native_python_provider_repository
    subprocess.run(
        (  # noqa: S607 - fixed commands in a disposable fixture only
            "git",
            "-c",
            "user.name=Native Hosted Test",
            "-c",
            "user.email=native-hosted@example.invalid",
            "-c",
            "core.hooksPath=/dev/null",
            "commit",
            "--allow-empty",
            "--quiet",
            "-m",
            "Advance disposable hosted tooling",
        ),
        cwd=source,
        check=True,
    )
    tooling = subprocess.check_output(
        ("git", "rev-parse", "HEAD"),  # noqa: S607
        cwd=source,
        text=True,
    ).strip()
    subprocess.run(  # noqa: S603
        ("git", "update-ref", "refs/remotes/origin/main", tooling),  # noqa: S607
        cwd=source,
        check=True,
    )
    document = request_document()
    document["targets"]["a"]["commit"] = target_a
    document["targets"]["b"]["commit"] = tooling
    request = NativeRequest(canonicalize(document))
    assert (
        validate_hosted(source, hosted_environment(tooling), tooling, request)
        == _RUN
    )
    subprocess.run(  # noqa: S603
        ("git", "update-ref", "refs/remotes/origin/main", target_a),  # noqa: S607
        cwd=source,
        check=True,
    )
    with pytest.raises(ValueError, match="protected main moved"):
        validate_hosted(source, hosted_environment(tooling), tooling, request)


@pytest.mark.parametrize("command", ["prepare", "probe", "audit"])
def test_python_native_cli_disabled_slot_prevents_execution(
    command, tmp_path, capsys
):
    """A valid-looking CLI request cannot cross either null protected slot."""
    output = tmp_path / "never-produced.zip"
    result = python_native.main(
        [
            command,
            "--root",
            str(_ROOT),
            "--registry",
            "testpypi",
            "--request-digest",
            "sha256:" + "f" * 64,
            "--tooling-sha",
            _TOOLING,
            "--output",
            str(output),
        ]
    )
    assert result == 1
    assert not output.exists()
    assert "grants no retry or native admission" in capsys.readouterr().err


def test_python_native_local_replay_uses_original_lineage_without_authority(
    hosted_probe, tmp_path, monkeypatch
):
    """Local replay needs no slots, Git, OIDC or registry access."""
    _, prepared, reference, files, http = hosted_probe
    prepared_path = tmp_path / reference.payload_path
    prepared_path.write_bytes(pack_bundle(prepared))
    probe_path = tmp_path / "probe.zip"
    probe_path.write_bytes(pack_bundle(files))
    probe_digest = python_digest(probe_path.read_bytes())
    probe_reference = ArtifactReference(
        1002,
        probe_digest,
        f"https://github.com/hcoona/three/actions/runs/{_RUN}/artifacts/1002",
        probe_path.name,
        probe_digest,
    )
    lineage = canonicalize(
        {
            "prepared": reference.to_document(),
            "probe": probe_reference.to_document(),
            "run-id": _RUN,
            "tooling-sha": _TOOLING,
        }
    )
    references = tmp_path / "references.json"
    references.write_bytes(lineage)
    consumed = []

    def audited(request, fixtures, retained):
        return audit_suite(
            request, fixtures, retained, consumer=_consumer(consumed)
        )

    def forbidden(*_args, **_kwargs):
        pytest.fail("Local replay attempted hosted authority or network access")

    monkeypatch.setattr(python_native, "audit_suite", audited)
    for name in ("PythonHttpsTransport", "load_request", "validate_hosted"):
        monkeypatch.setattr(python_native, name, forbidden)
    for name in hosted_environment():
        monkeypatch.delenv(name, raising=False)
    original_calls = tuple(http.calls)
    output = tmp_path / "audit.zip"
    assert (
        python_native.main(
            [
                "replay",
                "--prepared",
                str(prepared_path),
                "--probe",
                str(probe_path),
                "--references",
                str(references),
                "--output",
                str(output),
            ]
        )
        == 0
    )
    result = unpack_bundle(output.read_bytes())
    assert result["lineage.json"] == lineage
    verdict = parse_canonical_json(result["audit.json"])
    assert verdict["result"] == "supplied-facts-pass"
    assert verdict["native-admission"] is False
    assert {item.digest for item in consumed} == {
        item.digest for item in http.registry.stored.values()
    }
    assert tuple(http.calls) == original_calls
