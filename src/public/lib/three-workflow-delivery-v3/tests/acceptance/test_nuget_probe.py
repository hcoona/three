"""Probe contracts with controlled native authority and publication seams."""

from __future__ import annotations

# ruff: noqa: D103, PLR2004, SLF001
import base64
import hashlib
import json
import subprocess
from dataclasses import replace
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import nuget_probe as probe
from three_workflow_delivery_v3.acceptance.nuget_fixture import (
    NuGetFixtureInspection,
)
from three_workflow_delivery_v3.acceptance.nuget_preparation import (
    COMPLETION_SCHEMA,
    NuGetPreparationRequest,
    _archive_files,
    write_archive,
)
from three_workflow_delivery_v3.adapters import nuget_github_packages as native
from three_workflow_delivery_v3.adapters.dotnet import (
    BUILD_DEFINITION,
    DotnetArtifactManifest,
    DotnetPackageTargetWitness,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.records.artifacts import ArtifactReference
from three_workflow_delivery_v3.repository import dotnet_provider
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_RELEASE_UNIT,
)

from ..repository.test_dotnet_provider import _facts

TOKEN = "controlled-probe-repository-token"  # noqa: S105
BASE = "https://nuget.pkg.github.com/hcoona/download/"
PUBLISH = "https://nuget.pkg.github.com/hcoona/"
INDEX = b'{"resources":[]}'
A = b"PK\x03\x04original-A\x00\xff\r\n"
B = b"PK\x03\x04original-B\x00\xff\r\n"


def _sha(content):
    return hashlib.sha256(content).hexdigest()


def _read(path):
    return parse_canonical_json(path.read_bytes())


def _reference(path, identity, run=71):
    digest = "sha256:" + _sha(path.read_bytes())
    return ArtifactReference(
        identity,
        digest,
        f"https://github.com/hcoona/three/actions/runs/{run}/artifacts/{identity}",
        path.name,
        digest,
    )


def _platform(request):
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_SHA": request.tooling_sha,
        "GITHUB_WORKFLOW_REF": (
            f"hcoona/three/{probe.WORKFLOW_PATH}@refs/heads/main"
        ),
        "GITHUB_RUN_ID": str(request.workflow_run_id),
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    facts = _facts()
    witness = DotnetPackageTargetWitness(
        facts.git_commit_id,
        DOTNET_RELEASE_UNIT,
        facts,
        BUILD_DEFINITION,
        "sha256:" + "c" * 64,
        "sha256:" + "d" * 64,
        "destination-acceptance",
    )
    version = facts.nuget_package_version

    def identity(package_id, display_version):
        return {
            "displayPackageId": package_id,
            "displayVersion": display_version,
            "normalizedPackageId": native.NUGET_PACKAGE_ID.lower(),
            "normalizedVersion": version,
        }

    official = {}
    manifests = {}
    files = {}
    for role, content in (("original", A), ("comparison", B)):
        package_id = native.NUGET_PACKAGE_ID
        display = version
        if role == "comparison":
            package_id = package_id.lower()
            display += "+wdv3fixture.old-fixtures"
        item = {
            "identity": identity(package_id, display),
            "entries": [
                "lib/net10.0/Smoke.dll",
                "workflow-delivery/provenance.json",
            ],
            "witnessBase64": base64.b64encode(witness.canonical_bytes).decode(),
            "assembly": {"name": "Smoke"},
            "frameworks": ["net10.0"],
            "dependencies": [],
            "repository": {"commit": witness.target},
        }
        official[content] = item
        basename = package_id + "." + version + ".nupkg"
        entries = tuple(item["entries"])
        if role == "comparison":
            entries = tuple(reversed(entries))
        manifests[role] = DotnetArtifactManifest(
            basename,
            entries,
            "sha256:" + _sha(content),
            "sha512:" + hashlib.sha512(content).hexdigest(),
            len(content),
        )
        files[role + "/" + basename] = content
    inspection = NuGetFixtureInspection(
        manifests["original"],
        manifests["comparison"],
        official[A]["identity"],
        official[B]["identity"],
        witness.canonical_bytes,
    ).to_document()
    helper_path = tmp_path / "helper.zip"
    write_archive(
        helper_path,
        {
            "WorkflowDeliveryV3DotnetProvider.dll": b"controlled helper",
            "WorkflowDeliveryV3DotnetProvider.deps.json": b"{}",
            "WorkflowDeliveryV3DotnetProvider.runtimeconfig.json": b"{}",
            "NuGet.Packaging.dll": b"controlled dependency",
        },
    )
    helper_reference = _reference(helper_path, 101)
    preparation = {
        "schema": COMPLETION_SCHEMA,
        "request": NuGetPreparationRequest(
            "b" * 40, witness.target, "old-fixtures", 71
        ).to_document(),
        "helper-artifact": helper_reference.to_document(),
        "inspection": inspection,
        "files": {
            name: "sha256:" + _sha(content) for name, content in files.items()
        },
    }
    files["preparation.json"] = canonicalize(preparation)
    fixture = tmp_path / "fixtures.zip"
    write_archive(fixture, files)
    profile = native._nuget_profile_document(
        PUBLISH,
        {
            "executableSha256": "a" * 64,
            "runtimeBuild": "controlled CPython",
            "sslSourceSha256": "b" * 64,
            "platform": "Windows",
            "tlsLibrary": "controlled TLS",
            "adapterSha256": "c" * 64,
        },
    )
    profile_call = Mock(return_value=profile)
    monkeypatch.setattr(native, "nuget_operation_profile", profile_call)
    request = probe.NuGetProbeRequest(
        "new-probes",
        "create",
        "c" * 40,
        81,
        "b" * 40,
        71,
        witness.target,
        _reference(fixture, 102),
        helper_reference,
        tuple(
            (probe._HELPER_ROOT + name, _sha(b"source"))
            for name in (
                "Program.cs",
                "WorkflowDeliveryV3DotnetProvider.csproj",
                "packages.lock.json",
            )
        ),
        "d" * 64,
        "e" * 64,
        "f" * 64,
        INDEX,
        inspection,
        profile,
    )
    helper = Mock(spec=native.NuGetAuthority)
    helper.inspect_package.side_effect = official.__getitem__
    helper.normalize_identity.side_effect = identity
    helper.service_resources.return_value = {
        "packageBaseAddress": BASE,
        "packagePublish": PUBLISH,
    }
    publisher = Mock(spec=native.publish_nuget_once)
    monkeypatch.setattr(native, "publish_nuget_once", publisher)
    return SimpleNamespace(
        request=request,
        fixture=fixture,
        helper=helper,
        helper_path=helper_path,
        official=official,
        profile=profile_call,
        publisher=publisher,
        root=tmp_path,
    )


def _prepare(inputs, request=None):
    request = request or inputs.request
    path = inputs.root / "prepared.zip"
    probe.prepare_probe_inputs(
        request,
        platform=_platform(request),
        fixture_path=inputs.fixture,
        helper=inputs.helper,
        output=path,
    )
    return path, _reference(path, 201, 81)


def _invoke(inputs, prepared, *, platform=None):
    path, reference = prepared
    return probe.publish_probe(
        prepared_path=path,
        prepared_reference=reference,
        platform=platform or _platform(inputs.request),
        token=TOKEN,
        directory=inputs.root / "publication",
    )


def _invocation(inputs, *, status=201, body=b"created", headers=(), error=None):
    return native.NuGetPublicationInvocation(
        canonical_sha256(inputs.request.profile),
        _sha(A),
        "1" * 64,
        None
        if error
        else native.NuGetHttpResponse(PUBLISH, status, headers, body),
        error,
    )


def test_probe_spec_binds_only_the_actual_current_run(inputs):
    spec = inputs.request.to_document()
    del spec["workflowRunId"]
    spec["schema"] = probe.SPEC_SCHEMA
    actual = probe.bind_probe_spec(
        canonicalize(spec), _platform(inputs.request)
    )
    assert actual == inputs.request
    assert (
        probe.read_probe_request(canonicalize(actual.to_document())) == actual
    )
    spec["workflowRunId"] = 81
    with pytest.raises(ValueError, match="prospective"):
        probe.bind_probe_spec(canonicalize(spec), _platform(inputs.request))
    inputs.publisher.assert_not_called()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/repo"),
        ("GITHUB_REPOSITORY_ID", "1"),
        ("GITHUB_ACTOR_ID", "1"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/feature"),
        ("GITHUB_REF_PROTECTED", "false"),
        ("GITHUB_SHA", "d" * 40),
        ("GITHUB_WORKFLOW_SHA", "d" * 40),
        ("GITHUB_WORKFLOW_REF", "other.yml@refs/heads/main"),
        ("GITHUB_RUN_ID", "82"),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("RUNNER_OS", "Linux"),
    ],
)
def test_probe_rejects_substituted_platform_before_effects(
    inputs, field, value
):
    prepared = _prepare(inputs)
    with pytest.raises(ValueError, match="platform binding"):
        _invoke(
            inputs,
            prepared,
            platform={**_platform(inputs.request), field: value},
        )
    inputs.publisher.assert_not_called()
    assert not (inputs.root / "publication/invocation-started.json").exists()
    assert (
        _read(inputs.root / "publication/failure.json")[
            "invocationMayHaveStarted"
        ]
        is False
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("repositoryId", 1),
        ("actorId", 1),
        ("purpose", "live-release"),
        ("scenario", "delete"),
        ("workflowRunId", True),
        ("runAttempt", 2),
        ("maximumPublicationInvocations", 2),
        ("publicationRetries", 1),
        ("preflightSha256", "not-evidence"),
        ("extra", "open contract"),
    ],
)
def test_probe_rejects_substituted_request(inputs, field, value):
    document = {**inputs.request.to_document(), field: value}
    with pytest.raises(ValueError, match=r"probe|NuGet"):
        probe.read_probe_request(canonicalize(document))
    inputs.publisher.assert_not_called()


@pytest.mark.parametrize(
    ("scenario", "selected"),
    [
        ("create", A),
        ("identical-duplicate", A),
        ("equivalent-duplicate", B),
    ],
)
def test_probe_preparation_preserves_original_selected_fixture(
    inputs, scenario, selected
):
    request = replace(inputs.request, scenario=scenario)
    path, reference = _prepare(inputs, request)
    files = _archive_files(path.read_bytes())
    assert files["package.nupkg"] == selected
    assert files["witness.json"] == canonicalize(request.inspection["witness"])
    assert (
        _read_document(files["request.json"])["fixture"]["producerRunId"] == 71
    )
    admitted, content, resources = probe._prepared_input(
        path, reference, _platform(request)
    )
    assert admitted == request
    assert content == selected
    assert resources == native.NuGetServiceResources(BASE, PUBLISH, _sha(INDEX))
    assert request.inspection["original"]["sha256"] == "sha256:" + _sha(A)
    assert request.inspection["comparison"]["sha512"] == (
        "sha512:" + hashlib.sha512(B).hexdigest()
    )
    assert (
        request.inspection["comparison"]["entries"]
        != inputs.official[B]["entries"]
    )
    inputs.helper.service_resources.assert_called_once_with(INDEX)
    assert [
        call.args[0] for call in inputs.helper.inspect_package.call_args_list
    ] == [A, B]
    inputs.publisher.assert_not_called()


def _read_document(content):
    return parse_canonical_json(content)


@pytest.mark.parametrize(
    "change",
    [
        "bytes",
        "helper",
        "inspection",
        "witness",
        "equivalence",
        "duplicate-entry",
    ],
)
def test_probe_rejects_substituted_fixture_or_helper(inputs, change):
    request = inputs.request
    if change == "bytes":
        inputs.fixture.write_bytes(
            inputs.fixture.read_bytes() + b"substitution"
        )
    elif change == "helper":
        request = replace(
            request,
            helper_reference=replace(
                request.helper_reference,
                payload_digest="sha256:" + "9" * 64,
                artifact_digest="sha256:" + "9" * 64,
            ),
        )
    elif change == "inspection":
        inputs.official[A]["entries"] = ["unrelated.dll"]
    elif change == "witness":
        inputs.official[A]["witnessBase64"] = base64.b64encode(
            b"another witness"
        ).decode()
    elif change == "duplicate-entry":
        inputs.official[B]["entries"] = [
            *inputs.official[B]["entries"],
            inputs.official[B]["entries"][0],
        ]
    else:
        changed = {
            **inputs.official[B]["identity"],
            "normalizedVersion": "9.0.0",
        }
        inputs.official[B]["identity"] = changed
    with pytest.raises(ValueError, match=r"fixture|probe|witness"):
        _prepare(inputs, request)
    assert not (inputs.root / "prepared.zip").exists()
    inputs.publisher.assert_not_called()


def test_probe_rejects_changed_profile_before_marker(inputs):
    prepared = _prepare(inputs)
    inputs.profile.return_value = {
        **inputs.request.profile,
        "tlsLibrary": "changed",
    }
    with pytest.raises(ValueError, match="profile changed"):
        _invoke(inputs, prepared)
    inputs.publisher.assert_not_called()
    assert not (inputs.root / "publication/invocation-started.json").exists()


@pytest.mark.parametrize("role", ["original", "comparison"])
@pytest.mark.parametrize("algorithm", ["sha256", "sha512"])
def test_probe_rejects_bare_original_manifest_digests(inputs, role, algorithm):
    files = _archive_files(inputs.fixture.read_bytes())
    preparation = parse_canonical_json(files["preparation.json"])
    inspection = preparation["inspection"]
    inspection[role][algorithm] = inspection[role][algorithm].split(":", 1)[1]
    files["preparation.json"] = canonicalize(preparation)
    changed = inputs.root / "bare-manifest.zip"
    write_archive(changed, files)
    request = replace(
        inputs.request,
        inspection=inspection,
        fixture_reference=_reference(changed, 102),
    )
    inputs.fixture = changed
    with pytest.raises(ValueError, match="original fixture bytes changed"):
        _prepare(inputs, request)
    assert not (inputs.root / "prepared.zip").exists()
    inputs.publisher.assert_not_called()


def test_probe_persists_marker_before_one_call_and_never_reuses_attempt(
    inputs,
):
    prepared = _prepare(inputs)

    def publish(**kwargs):
        marker = _read(inputs.root / "publication/invocation-started.json")
        assert marker["requestDigest"] == inputs.request.request_digest
        assert marker["preparedReference"] == prepared[1].to_document()
        assert marker["mutationMayHaveStarted"] is True
        assert marker["retryPermitted"] is False
        assert kwargs["package"] == A
        assert kwargs["token"] == TOKEN
        assert kwargs["expected_profile_sha256"] == canonical_sha256(
            inputs.request.profile
        )
        return _invocation(inputs)

    inputs.publisher.side_effect = publish
    result = _read(_invoke(inputs, prepared))
    assert result["httpCreated"] is True
    assert result["invocations"] == 1
    with pytest.raises(FileExistsError):
        _invoke(inputs, prepared)
    assert inputs.publisher.call_count == 1


def test_probe_marker_failure_prevents_invocation(inputs, monkeypatch):
    prepared = _prepare(inputs)
    monkeypatch.setattr(
        probe.os, "fsync", Mock(side_effect=OSError("controlled failure"))
    )
    with pytest.raises(OSError, match="controlled failure"):
        _invoke(inputs, prepared)
    inputs.publisher.assert_not_called()
    assert (
        _read(inputs.root / "publication/failure.json")[
            "invocationMayHaveStarted"
        ]
        is False
    )
    assert (
        _read(inputs.root / "publication/failure.json")["requestSpent"] is True
    )


@pytest.mark.parametrize(
    ("status", "error"),
    [(201, None), (409, None), (500, None), (None, "lost response")],
)
def test_probe_retains_creation_rejection_and_unknown_results(
    inputs, status, error
):
    prepared = _prepare(inputs)
    inputs.publisher.return_value = _invocation(
        inputs, status=status, error=error
    )
    result = _read(_invoke(inputs, prepared))
    assert result["httpCreated"] is (status == 201)
    assert result["possiblyMutated"] is (status != 201)
    assert result["transportError"] is (error is not None)
    assert result["requestSpent"] is True
    assert result["retryPermitted"] is False
    assert result["requestDigest"] == inputs.request.request_digest
    assert "independent native audit remain required" in result["evidenceLevel"]
    assert inputs.publisher.call_count == 1
    if error:
        assert result["response"] is None
        assert (
            error.encode()
            not in (inputs.root / "publication/result.json").read_bytes()
        )
    else:
        assert result["response"]["status"] == status
        assert (
            inputs.root / "publication/response.body"
        ).read_bytes() == b"created"


@pytest.mark.parametrize(
    ("location", "encoding"),
    [
        ("body", "plain"),
        ("body", "basic"),
        ("body", "url"),
        ("header", "plain"),
    ],
)
def test_probe_rejects_reflected_credential_without_unsafe_retention(
    inputs, location, encoding
):
    prepared = _prepare(inputs)
    secret = TOKEN.encode()
    if encoding == "basic":
        secret = base64.b64encode(b"hcoona:" + secret)
    elif encoding == "url":
        secret = "".join(f"%{byte:02X}" for byte in secret).encode()
    body = secret if location == "body" else b"safe body"
    headers = (
        (("x-github-request-id", secret.decode()),)
        if location == "header"
        else ()
    )
    inputs.publisher.return_value = _invocation(
        inputs, body=body, headers=headers
    )
    with pytest.raises(ValueError, match="credential reflected"):
        _invoke(inputs, prepared)
    directory = inputs.root / "publication"
    assert not (directory / "response.body").exists()
    assert not (directory / "response.json").exists()
    assert not (directory / "result.json").exists()
    assert _read(directory / "failure.json")["invocationMayHaveStarted"] is True
    assert all(secret not in path.read_bytes() for path in directory.iterdir())
    assert inputs.publisher.call_count == 1


def test_probe_retains_safe_failure_without_exception_text(inputs):
    prepared = _prepare(inputs)
    inputs.publisher.side_effect = RuntimeError("transport reflected " + TOKEN)
    with pytest.raises(RuntimeError):
        _invoke(inputs, prepared)
    directory = inputs.root / "publication"
    assert _read(directory / "failure.json")["errorType"] == "RuntimeError"
    assert _read(directory / "failure.json")["invocationMayHaveStarted"] is True
    assert all(
        TOKEN.encode() not in path.read_bytes() for path in directory.iterdir()
    )


@pytest.mark.parametrize(
    ("status", "scenario", "exit_code"),
    [
        (201, "create", 0),
        (409, "create", 1),
        (409, "identical-duplicate", 0),
        (201, "identical-duplicate", 1),
        (500, "identical-duplicate", 1),
    ],
)
def test_probe_cli_reports_only_the_expected_http_observation(
    inputs, monkeypatch, status, scenario, exit_code
):
    inputs.request = replace(inputs.request, scenario=scenario)
    path, reference = _prepare(inputs)
    reference_path = inputs.root / "reference.json"
    reference_path.write_bytes(canonicalize(reference.to_document()))
    monkeypatch.setattr(probe, "admit_probe_source", Mock())
    for key, value in {
        **_platform(inputs.request),
        "GITHUB_TOKEN": TOKEN,
    }.items():
        monkeypatch.setenv(key, value)
    inputs.publisher.return_value = _invocation(inputs, status=status)
    code = probe.main(
        [
            "publish",
            "--prepared",
            str(path),
            "--reference",
            str(reference_path),
            "--checkout",
            str(inputs.root),
            "--evidence",
            str(inputs.root / "publication"),
        ]
    )
    assert code == exit_code
    assert inputs.publisher.call_count == 1
    assert _read(inputs.root / "publication/result.json")["httpCreated"] is (
        status == 201
    )


@pytest.mark.parametrize(
    "change", ["wrong-run", "extra-file", "package", "witness", "resource"]
)
def test_probe_rejects_substituted_prepared_transport(inputs, change):
    path, reference = _prepare(inputs)
    if change == "wrong-run":
        reference = replace(
            reference,
            artifact_url=reference.artifact_url.replace("/81/", "/82/"),
        )
    else:
        files = _archive_files(path.read_bytes())
        if change == "extra-file":
            files["unbound.txt"] = b"extra"
        elif change == "package":
            files["package.nupkg"] = B
        elif change == "witness":
            files["witness.json"] = b"{}"
        else:
            files["resources.json"] = canonicalize(
                {
                    "packageBaseAddress": BASE,
                    "packagePublish": PUBLISH,
                    "indexSha256": "9" * 64,
                }
            )
        changed = inputs.root / "changed.zip"
        write_archive(changed, files)
        path, reference = changed, _reference(changed, 202, 81)
    with pytest.raises(ValueError, match="probe"):
        _invoke(inputs, (path, reference))
    inputs.publisher.assert_not_called()
    assert not (inputs.root / "publication/invocation-started.json").exists()


def test_probe_source_admission_compares_actual_bytes_and_original_helper(
    inputs, monkeypatch
):
    root = inputs.root / "checkout"
    root.mkdir()

    def git(*args):
        return (
            subprocess.check_output(  # noqa: S603
                ("git", "-C", str(root), *args),  # noqa: S607
                stderr=subprocess.DEVNULL,
                timeout=30,
            )
            .decode()
            .strip()
        )

    git("init", "--quiet")
    git("config", "user.name", "Controlled Test")
    git("config", "user.email", "test@example.invalid")
    git("config", "core.autocrlf", "false")
    for name, _digest in inputs.request.helper_source_inputs:
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"source")
    code = root / probe._SOURCE / "acceptance/nuget_probe.py"
    code.parent.mkdir(parents=True)
    code.write_bytes(b"# controlled source\n")
    git("add", ".")
    git("commit", "--quiet", "-m", "Controlled source")
    original = git("rev-parse", "HEAD")
    code.write_bytes(b"# current controlled source\n")
    git("add", ".")
    git("commit", "--quiet", "-m", "Current controlled source")
    request = replace(
        inputs.request,
        fixture_tooling_sha=original,
        tooling_sha=git("rev-parse", "HEAD"),
    )
    monkeypatch.setattr(probe, "__file__", str(code))
    probe.admit_probe_source(root, request)
    code.write_bytes(b"# changed runtime bytes\r\n")
    git("update-index", "--assume-unchanged", str(code.relative_to(root)))
    assert not git("status", "--porcelain")
    with pytest.raises(ValueError, match="source bytes"):
        probe.admit_probe_source(root, request)
    code.write_bytes(b"# current controlled source\n")
    bad = list(request.helper_source_inputs)
    bad[0] = (bad[0][0], "9" * 64)
    with pytest.raises(ValueError, match="helper dependency"):
        probe.admit_probe_source(
            root, replace(request, helper_source_inputs=tuple(bad))
        )
    inputs.publisher.assert_not_called()


def test_probe_helper_retains_process_result_without_credentials(
    inputs, monkeypatch
):
    directory = inputs.root / "helper-inspection"
    directory.mkdir()
    dll = directory / "helper.dll"
    dll.write_bytes(b"controlled helper")
    expected = {"displayPackageId": "controlled", "displayVersion": "1.0.0"}
    monkeypatch.setenv("GITHUB_TOKEN", TOKEN)
    monkeypatch.setenv("GH_TOKEN", TOKEN)

    def process(command, **kwargs):
        assert command == (
            "dotnet",
            str(dll),
            "normalize-identity",
            "controlled",
            "1.0.0",
        )
        assert not {"GITHUB_TOKEN", "GH_TOKEN"}.intersection(kwargs["env"])
        assert kwargs["timeout"] == 300
        return SimpleNamespace(
            stdout=json.dumps(expected),
            stderr="controlled warning\n",
            returncode=0,
        )

    monkeypatch.setattr(dotnet_provider.subprocess, "run", process)
    helper = probe._LoggedHelper(dll, directory)
    assert helper.normalize_identity("controlled", "1.0.0") == expected
    evidence = directory / "helper-01"
    assert json.loads((evidence / "stdout.txt").read_text()) == expected
    assert (evidence / "stderr.txt").read_text() == "controlled warning\n"
    assert (evidence / "exit-code.txt").read_text() == "0"
    assert all(TOKEN not in path.read_text() for path in evidence.iterdir())
    inputs.publisher.assert_not_called()
