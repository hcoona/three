"""Profile observation boundaries with a controlled local collector."""

from __future__ import annotations

# ruff: noqa: D103, SLF001
import hashlib
import json
import subprocess
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from three_workflow_delivery_v3.acceptance import nuget_profile as observer
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)

BASE = "https://nuget.pkg.github.com/hcoona/download/"
PUBLISH = "https://nuget.pkg.github.com/hcoona"
SHA = "a" * 40
TREE = "b" * 40


def _platform(tooling=SHA):
    return {
        "GITHUB_REPOSITORY": "hcoona/three",
        "GITHUB_REPOSITORY_ID": "1102295886",
        "GITHUB_ACTOR_ID": "712433",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_REF_PROTECTED": "true",
        "GITHUB_SHA": tooling,
        "GITHUB_WORKFLOW_SHA": tooling,
        "GITHUB_WORKFLOW_REF": (
            f"hcoona/three/{observer.WORKFLOW_PATH}@refs/heads/main"
        ),
        "GITHUB_RUN_ID": "81",
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_OS": "Windows",
    }


@pytest.fixture
def inputs(tmp_path, monkeypatch):
    resources = {
        "serviceIndexSha256": "c" * 64,
        "packageBaseAddress": BASE,
        "packagePublish": PUBLISH,
    }
    spec = {
        "toolingSha": SHA,
        "preflightSha256": "d" * 64,
        "resources": resources,
        "resourcesDigest": canonical_sha256(resources),
    }
    profile = observer.native._nuget_profile_document(
        PUBLISH,
        {
            "executableSha256": "1" * 64,
            "runtimeBuild": "controlled CPython build",
            "sslSourceSha256": "2" * 64,
            "platform": "Windows",
            "tlsLibrary": "controlled TLS library",
            "adapterSha256": "3" * 64,
        },
    )
    collector = Mock(return_value=profile)
    monkeypatch.setattr(observer.native, "nuget_operation_profile", collector)
    original_source_admission = observer._admit_source
    source_admission = Mock(return_value=TREE)
    monkeypatch.setattr(observer, "_admit_source", source_admission)
    for key, value in _platform().items():
        monkeypatch.setenv(key, value)
    monkeypatch.setenv("CONTROLLED_PRIVATE_VALUE", "do-not-retain-this-value")
    checkout = tmp_path / "checkout with spaces"
    checkout.mkdir()
    return SimpleNamespace(
        spec=spec,
        profile=profile,
        collector=collector,
        source_admission=source_admission,
        original_source_admission=original_source_admission,
        checkout=checkout,
        evidence=tmp_path / "profile evidence",
        spec_path=tmp_path / "input specification.json",
    )


def _invoke(inputs, content=None):
    inputs.spec_path.write_bytes(
        canonicalize(inputs.spec) if content is None else content
    )
    return observer.main(
        [
            "--spec",
            str(inputs.spec_path),
            "--checkout",
            str(inputs.checkout),
            "--evidence",
            str(inputs.evidence),
        ]
    )


def _read(path):
    return parse_canonical_json(path.read_bytes())


def _assert_failed_before_collection(inputs):
    inputs.collector.assert_not_called()
    assert {p.name for p in inputs.evidence.iterdir()} == {"failure.json"}
    assert _read(inputs.evidence / "failure.json") == {
        "errorType": "ValueError",
        "inputSha256": hashlib.sha256(
            inputs.spec_path.read_bytes()
        ).hexdigest(),
        "nativeAdmissionEstablished": False,
        "retryPermitted": False,
    }


@pytest.mark.parametrize("publish", [PUBLISH, PUBLISH + "/"])
def test_profile_observer_retains_complete_profile_and_bindings(
    inputs, publish
):
    inputs.spec["resources"]["packagePublish"] = publish
    inputs.spec["resourcesDigest"] = canonical_sha256(inputs.spec["resources"])
    inputs.profile["packagePublish"] = publish

    assert _invoke(inputs) == 0

    expected_files = {
        "spec.json": canonicalize(inputs.spec),
        "resources.json": canonicalize(inputs.spec["resources"]),
        "operation-profile.json": canonicalize(inputs.profile),
    }
    assert {p.name for p in inputs.evidence.iterdir()} == {
        *expected_files,
        "observation.json",
    }
    for name, content in expected_files.items():
        assert (inputs.evidence / name).read_bytes() == content
    result = _read(inputs.evidence / "observation.json")
    assert result == {
        "specDigest": canonical_sha256(inputs.spec),
        "resourcesDigest": canonical_sha256(inputs.spec["resources"]),
        "preflightSha256": "d" * 64,
        "toolingSha": SHA,
        "toolingTree": TREE,
        "platform": _platform(),
        "operationProfileDigest": canonical_sha256(inputs.profile),
        "files": {
            name: "sha256:" + hashlib.sha256(content).hexdigest()
            for name, content in expected_files.items()
        },
        "evidenceLevel": (
            "local Windows profile observation; original preflight and "
            "actual run/artifact provenance require independent audit"
        ),
        "nativeAdmissionEstablished": False,
    }
    inputs.collector.assert_called_once_with(
        observer.native.NuGetServiceResources(BASE, publish, "c" * 64)
    )
    assert "do-not-retain-this-value" not in json.dumps(result)
    assert "CONTROLLED_PRIVATE_VALUE" not in result["platform"]


@pytest.mark.parametrize(
    "change",
    [
        "noncanonical",
        "duplicate",
        "missing",
        "extra",
        "not-object",
        "tooling-digest",
        "preflight-digest",
        "index-digest",
        "resources-not-object",
        "resources-extra",
        "resource-type",
        "digest-type",
        "digest-mismatch",
        "unnormalized-base",
    ],
)
def test_profile_observer_rejects_invalid_spec_before_collection(  # noqa: C901, PLR0912
    inputs, change
):
    spec = inputs.spec
    if change == "missing":
        del spec["preflightSha256"]
    elif change == "extra":
        spec["admitted"] = True
    elif change == "tooling-digest":
        spec["toolingSha"] = "A" * 40
    elif change == "preflight-digest":
        spec["preflightSha256"] = False
    elif change == "index-digest":
        spec["resources"]["serviceIndexSha256"] = "c" * 63
    elif change == "resources-not-object":
        spec["resources"] = []
    elif change == "resources-extra":
        spec["resources"]["admitted"] = True
    elif change == "resource-type":
        spec["resources"]["packageBaseAddress"] = None
    elif change == "unnormalized-base":
        spec["resources"]["packageBaseAddress"] = BASE.rstrip("/")
    spec["resourcesDigest"] = canonical_sha256(spec["resources"])
    if change == "digest-type":
        spec["resourcesDigest"] = []
    elif change == "digest-mismatch":
        spec["resources"]["packagePublish"] += "/changed"
    content = canonicalize(spec)
    if change == "noncanonical":
        content += b"\n"
    elif change == "duplicate":
        content = b'{"toolingSha":"a","toolingSha":"b"}'
    elif change == "not-object":
        content = b"[]"

    assert _invoke(inputs, content) == 1

    inputs.collector.assert_not_called()
    inputs.source_admission.assert_not_called()
    assert {p.name for p in inputs.evidence.iterdir()} == {"failure.json"}
    failure = _read(inputs.evidence / "failure.json")
    assert failure["inputSha256"] == hashlib.sha256(content).hexdigest()
    assert failure["nativeAdmissionEstablished"] is False
    assert failure["retryPermitted"] is False


@pytest.mark.parametrize(("size", "accepted"), [(65535, True), (65536, False)])
def test_profile_observer_enforces_canonical_spec_size_limit(
    inputs, size, accepted
):
    padding = size - len(canonicalize(inputs.spec))
    base = BASE[:-1] + "x" * padding + "/"
    inputs.spec["resources"]["packageBaseAddress"] = base
    inputs.spec["resourcesDigest"] = canonical_sha256(inputs.spec["resources"])
    content = canonicalize(inputs.spec)
    assert len(content) == size

    assert _invoke(inputs, content) == (0 if accepted else 1)

    if accepted:
        inputs.source_admission.assert_called_once_with(inputs.checkout, SHA)
        inputs.collector.assert_called_once_with(
            observer.native.NuGetServiceResources(base, PUBLISH, "c" * 64)
        )
        assert (inputs.evidence / "spec.json").read_bytes() == content
        assert (inputs.evidence / "operation-profile.json").read_bytes() == (
            canonicalize(inputs.profile)
        )
        result = _read(inputs.evidence / "observation.json")
        assert result["specDigest"] == canonical_sha256(inputs.spec)
        assert result["nativeAdmissionEstablished"] is False
    else:
        _assert_failed_before_collection(inputs)
        inputs.source_admission.assert_not_called()


@pytest.mark.parametrize("field", ["packageBaseAddress", "packagePublish"])
@pytest.mark.parametrize(
    "endpoint",
    [
        "https://other.example/hcoona/",
    ],
)
def test_profile_observer_rejects_unbounded_resources_before_collection(
    inputs, field, endpoint
):
    inputs.spec["resources"][field] = endpoint
    inputs.spec["resourcesDigest"] = canonical_sha256(inputs.spec["resources"])

    assert _invoke(inputs) == 1

    inputs.collector.assert_not_called()
    assert {p.name for p in inputs.evidence.iterdir()} == {"failure.json"}
    assert _read(inputs.evidence / "failure.json")["errorType"] == (
        "NuGetAdapterError"
    )


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("GITHUB_REPOSITORY", "other/three"),
        ("GITHUB_REPOSITORY_ID", "1"),
        ("GITHUB_ACTOR_ID", "1"),
        ("GITHUB_EVENT_NAME", "push"),
        ("GITHUB_REF", "refs/heads/other"),
        ("GITHUB_REF_PROTECTED", "false"),
        ("GITHUB_SHA", "b" * 40),
        ("GITHUB_WORKFLOW_SHA", "b" * 40),
        ("GITHUB_WORKFLOW_REF", "other.yml@refs/heads/main"),
        ("GITHUB_RUN_ID", "0"),
        ("GITHUB_RUN_ID", "01"),
        ("GITHUB_RUN_ID", ""),
        ("GITHUB_RUN_ATTEMPT", "2"),
        ("RUNNER_OS", "Linux"),
    ],
)
def test_profile_observer_rejects_platform_mismatch_before_collection(
    inputs, monkeypatch, field, value
):
    monkeypatch.setenv(field, value)

    assert _invoke(inputs) == 1

    _assert_failed_before_collection(inputs)
    inputs.source_admission.assert_not_called()


@pytest.mark.parametrize(
    "change", ["none", "wrong-head", "dirty", "actual-bytes", "outside-import"]
)
def test_profile_observer_binds_actual_source_before_collection(
    inputs, monkeypatch, tmp_path, change
):
    root = inputs.checkout

    def git(*arguments):
        return subprocess.check_output(  # noqa: S603
            (  # noqa: S607
                "git",
                "--no-pager",
                "-C",
                str(root),
                "-c",
                "core.autocrlf=false",
                "-c",
                f"core.hooksPath={tmp_path / 'no-hooks'}",
                "-c",
                "user.name=Controlled observer test",
                "-c",
                "user.email=observer@example.invalid",
                *arguments,
            ),
            stderr=subprocess.DEVNULL,
            timeout=30,
        )

    git("init", "--quiet")
    (root / ".gitattributes").write_bytes(b"*.py text\n")
    package = root / observer._SOURCE
    for module, relative in (
        (observer, "acceptance/nuget_profile.py"),
        (observer.native, "adapters/nuget_github_packages.py"),
        (observer.canonical, "canonical.py"),
    ):
        path = package / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"# controlled source\n")
        monkeypatch.setattr(module, "__file__", str(path))
    git("add", ".")
    git("commit", "--quiet", "-m", "Controlled observer source")
    tooling = git("rev-parse", "HEAD").decode().strip()
    inputs.spec["toolingSha"] = tooling
    for key, value in _platform(tooling).items():
        monkeypatch.setenv(key, value)
    monkeypatch.setattr(
        observer, "_admit_source", inputs.original_source_admission
    )
    path = package / "canonical.py"
    if change == "wrong-head":
        inputs.spec["toolingSha"] = SHA
        monkeypatch.setenv("GITHUB_SHA", SHA)
        monkeypatch.setenv("GITHUB_WORKFLOW_SHA", SHA)
    elif change == "dirty":
        (root / "untracked-observer-note.txt").write_bytes(
            b"controlled untracked fixture\n"
        )
        assert git("rev-parse", "HEAD").decode().strip() == tooling
        assert git("status", "--porcelain", "--untracked-files=all") == (
            b"?? untracked-observer-note.txt\n"
        )
        for source in package.rglob("*.py"):
            relative = source.relative_to(root).as_posix()
            assert source.read_bytes() == git("show", f"{tooling}:{relative}")
    elif change == "actual-bytes":
        path.write_bytes(b"# controlled source\r\n")
        relative = path.relative_to(root).as_posix()
        git("add", "--", relative)
        assert git("write-tree") == git("rev-parse", "HEAD^{tree}")
        assert path.read_bytes() != git("show", f"{tooling}:{relative}")
        assert git("status", "--porcelain") == b""
    elif change == "outside-import":
        monkeypatch.setattr(observer, "__file__", str(tmp_path / "outside.py"))

    assert _invoke(inputs) == (0 if change == "none" else 1)

    if change == "none":
        result = _read(inputs.evidence / "observation.json")
        assert result["toolingSha"] == tooling
        assert (
            result["toolingTree"]
            == git("rev-parse", "HEAD^{tree}").decode().strip()
        )
        inputs.collector.assert_called_once()
    else:
        _assert_failed_before_collection(inputs)


@pytest.mark.parametrize(
    "change", ["pinned-runtime", "platform", "endpoint", "shape"]
)
def test_profile_observer_rejects_runtime_mismatch_without_profile(
    inputs, change
):
    if change == "pinned-runtime":
        inputs.collector.side_effect = observer.native.NuGetAdapterError(
            "controlled private diagnostic must not be retained"
        )
    elif change == "platform":
        inputs.profile["platform"] = "Linux"
    elif change == "endpoint":
        inputs.profile["packagePublish"] = PUBLISH + "/changed"
    else:
        del inputs.profile["tlsLibrary"]

    assert _invoke(inputs) == 1

    inputs.collector.assert_called_once()
    assert {p.name for p in inputs.evidence.iterdir()} == {"failure.json"}
    failure = _read(inputs.evidence / "failure.json")
    assert failure["nativeAdmissionEstablished"] is False
    assert failure["retryPermitted"] is False
    assert set(failure) == {
        "errorType",
        "inputSha256",
        "nativeAdmissionEstablished",
        "retryPermitted",
    }
    assert "controlled private diagnostic" not in json.dumps(failure)


def test_profile_observer_preserves_an_existing_attempt(inputs):
    assert _invoke(inputs) == 0
    originals = {p.name: p.read_bytes() for p in inputs.evidence.iterdir()}
    inputs.collector.reset_mock()

    assert _invoke(inputs, b"invalid second attempt") == 1

    inputs.collector.assert_not_called()
    assert {
        p.name: p.read_bytes() for p in inputs.evidence.iterdir()
    } == originals


def test_profile_observer_partial_write_has_no_success_manifest(
    inputs, monkeypatch
):
    original_open = Path.open

    def open_file(path, *arguments, **keywords):
        if path == inputs.evidence / "operation-profile.json":
            message = "controlled write failure"
            raise OSError(message)
        return original_open(path, *arguments, **keywords)

    monkeypatch.setattr(Path, "open", open_file)

    assert _invoke(inputs) == 1

    assert {p.name for p in inputs.evidence.iterdir()} == {
        "spec.json",
        "resources.json",
        "failure.json",
    }
    assert _read(inputs.evidence / "failure.json")["errorType"] == "OSError"
    assert (inputs.evidence / "spec.json").read_bytes() == canonicalize(
        inputs.spec
    )
