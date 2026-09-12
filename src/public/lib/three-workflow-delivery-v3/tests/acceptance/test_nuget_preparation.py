"""Preparation scenarios with actual Git and controlled native mechanics."""

from __future__ import annotations

import hashlib
import io
import json
import shutil
import zipfile
from dataclasses import replace
from typing import TYPE_CHECKING
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import nuget_preparation as entry
from three_workflow_delivery_v3.canonical import canonicalize
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository import compiler, dotnet_provider

from ..repository.test_dotnet_compiler import _native_scenario

if TYPE_CHECKING:
    from pathlib import Path


def _files(content):
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        return {name: archive.read(name) for name in archive.namelist()}


def _platform(request):
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_REF": (
            f"hcoona/three/{entry.WORKFLOW_PATH}@refs/heads/main"
        ),
        "GITHUB_RUN_ID": str(request.workflow_run_id),
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }


def _reference(path: Path, identity: int) -> ArtifactReference:
    digest = "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()
    return ArtifactReference(
        identity,
        digest,
        f"https://github.com/hcoona/three/actions/runs/71/artifacts/{identity}",
        path.name,
        digest,
    )


def test_preparation_request_preserves_separate_target_and_tooling():
    """Keep target, tooling and acceptance authority distinct."""
    request = entry.NuGetPreparationRequest(
        "a" * 40, "b" * 40, "generation-1", 71
    )
    content = canonicalize(request.to_document())
    assert (
        entry.admit_preparation_request(content, _platform(request)) == request
    )
    context = request.context
    assert context.target == "b" * 40
    assert context.control == "workflow-delivery-v3:" + "a" * 40
    assert (context.purpose, context.run_attempt) == (
        "destination-acceptance",
        1,
    )
    assert context.channel is None
    assert context.release_unit is None
    assert request.provider_manifest.context == context
    with pytest.raises(ValueError, match="first Release Unit"):
        compiler.first_slice_provider_manifest(
            context, provider_producer="node"
        )
    with pytest.raises(ValueError, match="rejects reruns"):
        compiler.nuget_provider_manifest(
            replace(context, run_attempt=2), provider_producer="native"
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_ACTOR_ID", "1"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/feature"),
        ("GITHUB_SHA", "c" * 40),
        ("GITHUB_WORKFLOW_SHA", "c" * 40),
        ("GITHUB_WORKFLOW_REF", "hcoona/three/other.yml@refs/heads/main"),
        ("GITHUB_RUN_ID", "72"),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("RUNNER_OS", "Linux"),
    ],
)
def test_preparation_request_rejects_platform_substitution(field, value):
    """Another actor, revision or run cannot adopt the current request."""
    request = entry.NuGetPreparationRequest(
        "a" * 40, "b" * 40, "generation-1", 71
    )
    with pytest.raises(ValueError, match="platform binding"):
        entry.admit_preparation_request(
            canonicalize(request.to_document()),
            {**_platform(request), field: value},
        )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("purpose", "live-release"),
        ("package", "Hcoona.ReleaseSmoke.Nuget"),
        ("run-attempt", 2),
        ("run-attempt", True),
        ("generation", "../escape"),
        ("target", "main"),
        ("workflow-run-id", True),
        ("extra", "unclosed"),
    ],
)
def test_preparation_request_rejects_unclosed_inputs(field, value):
    """Reject malformed inputs and borrowed Live/product identities."""
    request = entry.NuGetPreparationRequest(
        "a" * 40, "b" * 40, "generation-1", 71
    )
    with pytest.raises(ValueError, match=r"fixture|commit SHAs"):
        entry.admit_preparation_request(
            canonicalize({**request.to_document(), field: value}),
            _platform(request),
        )


@pytest.fixture(scope="module")
def preparation_source(tmp_path_factory):
    """Share only immutable target Git; keep transports and effects per case."""
    with pytest.MonkeyPatch.context() as patch:
        return _native_scenario(
            tmp_path_factory.mktemp("fixture-entry-source"), patch
        )


@pytest.fixture
def preparation(tmp_path, monkeypatch, preparation_source):
    """Retain real target Git and transport around a native setup seam."""
    repo, context, _, original = preparation_source
    monkeypatch.setattr(
        dotnet_provider,
        "run_native",
        Mock(
            side_effect=AssertionError(
                "Model admission must not execute target code"
            )
        ),
    )
    request = entry.NuGetPreparationRequest(
        "a" * 40, context.target, "fixture-1", 71
    )
    result = replace(
        original,
        binding=compiler.provider_binding(
            request.provider_manifest, "dotnet-nuget-slice"
        ),
    )
    helper_path = tmp_path / "helper.zip"
    entry.write_archive(
        helper_path,
        {
            "WorkflowDeliveryV3DotnetProvider.dll": b"modeled trusted helper",
            "WorkflowDeliveryV3DotnetProvider.deps.json": b"{}",
            "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json": b"{}",
            "NuGet.Packaging.dll": b"modeled dependency",
        },
    )
    helper_reference = _reference(helper_path, 101)
    request_path = tmp_path / "request.json"
    request_path.write_bytes(canonicalize(request.to_document()))
    request_reference = _reference(request_path, 102)
    helper = entry.materialize_helper(
        helper_path, helper_reference, tmp_path / "helper"
    )
    dependencies = {
        "first.1.0.0.nupkg": b"original first",
        "second.2.0.0.nupkg": b"original second",
    }

    def provide(  # noqa: PLR0913
        root,
        binding,
        materialization,
        *,
        helper,
        evidence_directory,
        dependency_directory,
    ):
        assert root == repo
        assert binding == result.binding
        assert materialization.fetch_depth == 0
        assert materialization.credentials_persisted is False
        assert helper.helper_dll.read_bytes() == b"modeled trusted helper"
        assert not dependency_directory.exists()
        dependency_directory.mkdir(parents=True)
        for name, content in dependencies.items():
            (dependency_directory / name).write_bytes(content)
        evidence_directory.mkdir()
        for name in (
            "provider.binlog",
            "native-facts.json",
            "project.assets.json",
        ):
            (evidence_directory / name).write_bytes(b"modeled native evidence")
        return result

    monkeypatch.setattr(
        dotnet_provider, "provide_dotnet_repository_facts", provide
    )
    setup = tmp_path / "setup.zip"
    entry.prepare_native_inputs(
        request, repo, helper, helper_reference, tmp_path / "setup", setup
    )
    return (
        request,
        request_reference,
        setup,
        _reference(setup, 103),
        helper,
        helper_reference,
        repo,
    )


def test_fixture_setup_retains_native_facts_and_original_dependencies(
    preparation,
):
    """Seal original dependency bytes and the distinct native facts."""
    request, _, path, reference, _, helper_reference, _ = preparation
    files = _files(entry.read_uploaded(path, reference))
    setup = json.loads(files["setup.json"])
    assert setup["request-digest"] == request.request_digest
    assert setup["helper"] == helper_reference.to_document()
    assert files["dependencies/first.1.0.0.nupkg"] == b"original first"
    assert files["dependencies/second.2.0.0.nupkg"] == b"original second"
    assert {
        "native/provider.binlog",
        "native/native-facts.json",
        "native/project.assets.json",
    } <= files.keys()
    provider = json.loads(files["provider.json"])
    assert provider["binding"]["purpose"] == "destination-acceptance"


def test_preparation_closes_pair_and_preserves_original_transport(
    preparation, tmp_path, monkeypatch
):
    """Complete admission before packing and retain exact logical outputs."""
    observed = []

    def pack(request):
        observed.append(request)
        assert {
            p.name: p.read_bytes() for p in request.dependency_archives
        } == {
            "first.1.0.0.nupkg": b"original first",
            "second.2.0.0.nupkg": b"original second",
        }
        request.build.evidence_directory.mkdir()
        (request.build.evidence_directory / "fixtures.json").write_bytes(
            b"modeled inspection and consumer"
        )
        return Mock(
            original=Mock(
                basename="Original.nupkg", content=b"original A bytes"
            ),
            comparison=Mock(
                basename="comparison.nupkg", content=b"original B bytes"
            ),
            inspection=Mock(to_document=Mock(return_value={"modeled": True})),
        )

    monkeypatch.setattr(entry, "prepare_nuget_fixture_pair", pack)
    output = tmp_path / "complete.zip"
    entry.prepare_from_native_inputs(*preparation, tmp_path / "prepare", output)
    files = _files(output.read_bytes())
    complete = json.loads(files.pop("preparation.json"))
    assert complete["schema"] == entry.COMPLETION_SCHEMA
    assert files["original/Original.nupkg"] == b"original A bytes"
    assert files["comparison/comparison.nupkg"] == b"original B bytes"
    assert files["pair/fixtures.json"] == b"modeled inspection and consumer"
    assert complete["files"] == {
        name: "sha256:" + hashlib.sha256(content).hexdigest()
        for name, content in files.items()
    }
    assert (
        complete["provider-bundle"]["transport"]["artifact-id"]
        == preparation[3].artifact_id
    )
    assert observed[0].build.witness.purpose == "destination-acceptance"
    assert observed[0].build.witness.target == preparation[0].target
    assert observed[0].build.source_input_manifest
    assert complete["request"]["tooling-sha"] != complete["request"]["target"]


def test_preparation_failure_retains_closed_request_without_completion(
    preparation, tmp_path, monkeypatch
):
    """A failed consumer leaves diagnostics and no completed pair."""

    def fail(request):
        request.build.evidence_directory.mkdir()
        (request.build.evidence_directory / "failed.binlog").write_bytes(
            b"partial diagnostics"
        )
        message = "modeled consumer failure"
        raise ValueError(message)

    monkeypatch.setattr(entry, "prepare_nuget_fixture_pair", fail)
    output = tmp_path / "complete.zip"
    directory = tmp_path / "prepare"
    with pytest.raises(ValueError, match="consumer failure"):
        entry.prepare_from_native_inputs(*preparation, directory, output)
    assert not output.exists()
    assert (
        directory / "pair/failed.binlog"
    ).read_bytes() == b"partial diagnostics"
    closed = json.loads((directory / "preparation-request.json").read_bytes())
    assert closed["request"] == preparation[0].to_document()
    assert "inspection" not in closed


@pytest.mark.parametrize(
    "change", ["request", "helper", "setup-bytes", "purpose"]
)
def test_preparation_rejects_substituted_inputs_before_pack(
    preparation, tmp_path, monkeypatch, change
):
    """Reject request, native and byte substitutions before native packing."""
    inputs = list(preparation)
    if change == "request":
        inputs[0] = replace(inputs[0], generation="other-generation")
    elif change == "helper":
        inputs[5] = replace(inputs[5], artifact_id=201)
    elif change == "setup-bytes":
        inputs[2].write_bytes(inputs[2].read_bytes() + b"altered")
    else:
        files = _files(inputs[2].read_bytes())
        provider = json.loads(files["provider.json"])
        provider["binding"]["purpose"] = "slice-validation"
        files["provider.json"] = canonicalize(provider)
        result = dotnet_provider.dotnet_provider_result_from_document(provider)
        setup = json.loads(files.pop("setup.json"))
        setup["provider-result-digest"] = result.result_digest
        setup["files"] = {
            name: "sha256:" + hashlib.sha256(content).hexdigest()
            for name, content in files.items()
        }
        files["setup.json"] = canonicalize(setup)
        changed = tmp_path / "changed.zip"
        entry.write_archive(changed, files)
        inputs[2], inputs[3] = changed, _reference(changed, 204)
    pack = Mock(side_effect=AssertionError("must reject before packing"))
    monkeypatch.setattr(entry, "prepare_nuget_fixture_pair", pack)
    output = tmp_path / "complete.zip"
    with pytest.raises((ValueError, TypeError)):
        entry.prepare_from_native_inputs(*inputs, tmp_path / "prepare", output)
    assert not output.exists()
    pack.assert_not_called()


def test_preparation_rejects_changed_source_before_native_execution(
    preparation, tmp_path
):
    """The real pair boundary verifies captured bytes before native work."""
    inputs = list(preparation)
    inputs[6] = tmp_path / "changed-source"
    shutil.copytree(preparation[6], inputs[6])
    (inputs[6] / dotnet_provider.DOTNET_PROJECT_PATH).write_text(
        "altered", encoding="utf-8"
    )
    output = tmp_path / "complete.zip"
    with pytest.raises(ValueError, match="Build source input digest mismatch"):
        entry.prepare_from_native_inputs(*inputs, tmp_path / "prepare", output)
    assert not output.exists()
    assert not (tmp_path / "prepare/pair").exists()


def test_preparation_cli_emits_only_the_current_canonical_request(
    tmp_path, monkeypatch
):
    """The command emits a bound request and refuses output reuse or reruns."""
    request = entry.NuGetPreparationRequest("a" * 40, "b" * 40, "fixture-1", 71)
    for key, value in _platform(request).items():
        monkeypatch.setenv(key, value)
    output = tmp_path / "request.json"
    arguments = [
        "request",
        "--tooling-sha",
        request.tooling_sha,
        "--target",
        request.target,
        "--generation",
        request.generation,
        "--output",
        str(output),
    ]
    assert entry.main(arguments) == 0
    assert output.read_bytes() == canonicalize(request.to_document())
    with pytest.raises(ValueError, match="output already exists"):
        entry.main(arguments)
    output = tmp_path / "rerun.json"
    arguments[-1] = str(output)
    monkeypatch.setenv("GITHUB_RUN_ATTEMPT", "2")
    with pytest.raises(ValueError, match="platform binding"):
        entry.main(arguments)
    assert not output.exists()


@pytest.mark.parametrize("change", ["bytes", "missing-runtime", "path"])
def test_preparation_rejects_unbound_helper_before_materialization(
    tmp_path, change
):
    """Reject changed raw bytes, incomplete runtimes and escaping ZIP paths."""
    archive = tmp_path / "helper.zip"
    files = {
        "WorkflowDeliveryV3DotnetProvider.dll": b"helper",
        "WorkflowDeliveryV3DotnetProvider.deps.json": b"{}",
        "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json": b"{}",
    }
    if change == "missing-runtime":
        del files["WorkflowDeliveryV3DotnetProvider.runtimeconfig.json"]
    elif change == "path":
        files["../outside.dll"] = b"escaping"
    entry.write_archive(archive, files)
    reference = _reference(archive, 1)
    if change == "bytes":
        archive.write_bytes(archive.read_bytes() + b"changed")
    destination = tmp_path / "helper"
    with pytest.raises(ValueError, match=r"binding|incomplete|unsafe"):
        entry.materialize_helper(archive, reference, destination)
    assert not destination.exists()
    assert not (tmp_path / "outside.dll").exists()


@pytest.mark.parametrize("command", ["setup", "prepare"])
def test_preparation_cli_consumes_exact_uploaded_inputs(
    preparation, tmp_path, monkeypatch, command
):
    """Exercise both CLI paths through real transport and Model admission."""
    request, request_ref, setup, setup_ref, _, helper_ref, repo = preparation
    for key, value in _platform(request).items():
        monkeypatch.setenv(key, value)

    def pack(request):
        request.build.evidence_directory.mkdir()
        (request.build.evidence_directory / "fixtures.json").write_bytes(
            b"modeled successful content and consumer"
        )
        return Mock(
            original=Mock(basename="A.nupkg", content=b"original A"),
            comparison=Mock(basename="B.nupkg", content=b"original B"),
            inspection=Mock(to_document=Mock(return_value={"modeled": True})),
        )

    monkeypatch.setattr(entry, "prepare_nuget_fixture_pair", pack)
    output = tmp_path / "cli-output.zip"
    arguments = [
        command,
        "--source-root",
        str(repo),
        "--directory",
        str(tmp_path / "cli-evidence"),
        "--output",
        str(output),
    ]
    references = [("request", request_ref), ("helper", helper_ref)]
    if command == "prepare":
        references.append(("setup", setup_ref))
    for role, reference in references:
        arguments.extend(
            [
                "--" + role,
                str(tmp_path / reference.payload_path),
                "--" + role + "-id",
                str(reference.artifact_id),
                "--" + role + "-digest",
                reference.artifact_digest,
                "--" + role + "-url",
                reference.artifact_url,
            ]
        )
    assert entry.main(arguments) == 0
    files = _files(output.read_bytes())
    if command == "setup":
        assert files["request.json"] == canonicalize(request.to_document())
        assert files["dependencies/first.1.0.0.nupkg"] == b"original first"
    else:
        complete = json.loads(files["preparation.json"])
        assert complete["setup-artifact"] == setup_ref.to_document()
        assert complete["request-artifact"] == request_ref.to_document()
        assert complete["helper-artifact"] == helper_ref.to_document()
        assert files["original/A.nupkg"] == b"original A"
        assert files["comparison/B.nupkg"] == b"original B"
    assert setup.is_file()


@pytest.mark.parametrize("failure", ["native", "dependencies"])
def test_fixture_setup_failure_retains_diagnostics_without_transport(
    preparation, tmp_path, monkeypatch, failure
):
    """Native failure or missing dependency archives cannot seal setup."""
    request, _, setup, _, helper, helper_ref, repo = preparation
    result = dotnet_provider.dotnet_provider_result_from_document(
        json.loads(_files(setup.read_bytes())["provider.json"])
    )

    def incomplete(*_args, evidence_directory, dependency_directory, **_kwargs):
        evidence_directory.mkdir(parents=True)
        (evidence_directory / "provider.binlog").write_bytes(b"partial native")
        dependency_directory.mkdir()
        if failure == "native":
            message = "modeled native failure"
            raise ValueError(message)
        return result

    monkeypatch.setattr(
        dotnet_provider, "provide_dotnet_repository_facts", incomplete
    )
    directory = tmp_path / "failed-setup"
    output = tmp_path / "failed-setup.zip"
    with pytest.raises(
        ValueError, match=r"native failure|distinct dependencies"
    ):
        entry.prepare_native_inputs(
            request, repo, helper, helper_ref, directory, output
        )
    assert not output.exists()
    assert (
        directory / "native/provider.binlog"
    ).read_bytes() == b"partial native"
