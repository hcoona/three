"""Native original-gem reproducibility, hostile archives and clean consumers."""

import gzip
import hashlib
import io
import os
import tarfile
from dataclasses import replace
from pathlib import Path

import pytest
import yaml
from three_workflow_delivery_v3._ruby_native import (
    RUBY_ROOT,
    RUBY_VERSION_PATH,
    RUBY_WITNESS_PATH,
    ruby_digest,
)
from three_workflow_delivery_v3.adapters import ruby
from three_workflow_delivery_v3.adapters.ruby import (
    build_ruby_package,
    inspect_ruby_distribution,
    qualify_ruby_consumer,
)
from three_workflow_delivery_v3.canonical import (
    canonical_sha256,
    canonicalize,
    parse_canonical_json,
)
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    RubyNbgvFacts,
    provide_ruby_repository_facts,
)

from ..ruby_fixtures import (
    EPOCH,
    binding,
    build_request,
    commit,
    git,
    repository,
)


@pytest.fixture(scope="module")
def native_build(tmp_path_factory):
    """Native build."""
    root = tmp_path_factory.mktemp("ruby-build-case-r4")
    source, origin, target = repository(root)
    provider = provide_ruby_repository_facts(
        source,
        binding(target),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    request = build_request(provider)
    evidence = Path(
        os.environ.get("WDV3_RUBY_TEST_EVIDENCE", str(root / "evidence"))
    )
    evidence.mkdir(parents=True, exist_ok=False)
    (evidence / "provider.json").write_bytes(
        canonicalize(provider.to_document())
    )
    first_root = root / "first"
    second_root = root / "second"
    first_root.mkdir()
    second_root.mkdir()
    first = build_ruby_package(source, request, scratch_root=first_root)
    (evidence / "first.gem").write_bytes(first.content)
    (evidence / "first-inspection.json").write_bytes(first.inspection)
    readme = source / RUBY_ROOT / "README.md"
    original = readme.read_bytes()
    readme.write_bytes(b"Ambient dirty source is not the target.")
    native = ruby.run_native
    calls = []
    helper = ruby.run_ruby

    def changed_file_times(request, **kwargs):
        if request["operation"] == "build":
            for path in kwargs["cwd"].rglob("*"):
                os.utime(path, (EPOCH + 12345, EPOCH + 12345))
        return helper(request, **kwargs)

    def only_git(command, *args, **kwargs):
        calls.append(command)
        assert command[0] == "git"
        return native(command, *args, **kwargs)

    try:
        with pytest.MonkeyPatch.context() as guard:
            guard.setattr(ruby, "run_native", only_git)
            guard.setattr(ruby, "run_ruby", changed_file_times)
            second = build_ruby_package(
                source, request, scratch_root=second_root
            )
        (evidence / "second.gem").write_bytes(second.content)
        (evidence / "second-inspection.json").write_bytes(second.inspection)
        (evidence / "build-calls.json").write_bytes(
            canonicalize(
                {
                    "calls": [list(call) for call in calls],
                    "second-staged-mtime": EPOCH + 12345,
                }
            )
        )
        assert calls == [("git", "show", "-s", "--format=%ct", target)]
    finally:
        readme.write_bytes(original)
    return source, origin, request, first, second


def test_ruby_independent_native_builds_are_identical_original_bytes(
    native_build,
):
    """Independent native builds are identical original bytes."""
    _, _, request, first, second = native_build
    assert first.content == second.content
    assert first.digest == ruby_digest(second.content)
    version = request.witness.nbgv.native_version
    assert first.filename == f"hcoona-release-smoke-ruby-{version}.gem"
    inspection = parse_canonical_json(first.inspection)
    assert inspection["witness"] == request.witness.canonical_bytes.decode()
    assert inspection["members"][RUBY_WITNESS_PATH] == {
        "sha256": ruby_digest(request.witness.canonical_bytes),
        "size": len(request.witness.canonical_bytes),
    }


def test_ruby_build_rejects_manifest_for_ambient_dirty_source(
    native_build, monkeypatch
):
    """Dirty working bytes cannot satisfy the frozen original source digest."""
    source, _, request, _, _ = native_build
    readme = source / RUBY_ROOT / "README.md"
    original = readme.read_bytes()
    readme.write_bytes(b"ambient dirty source must never be packaged")
    calls = []
    native = ruby.run_native

    def only_git(command, *args, **kwargs):
        calls.append(command)
        assert command[0] == "git"
        return native(command, *args, **kwargs)

    monkeypatch.setattr(ruby, "run_native", only_git)
    try:
        # Reject a manifest mismatch before native construction.
        bad = replace(
            request,
            source_input_manifest=tuple(
                (
                    p,
                    ruby_digest(b"ambient dirty source must never be packaged")
                    if p.endswith("README.md")
                    else d,
                )
                for p, d in request.source_input_manifest
            ),
        )
        with pytest.raises(ValueError, match="frozen source differs"):
            build_ruby_package(source, bad)
        assert calls == []
    finally:
        readme.write_bytes(original)


def test_ruby_build_rejects_wrong_frozen_epoch_before_native_work(
    native_build, monkeypatch
):
    """Build rejects wrong frozen epoch before native work."""
    source, _, request, _, _ = native_build

    def unexpected(*_args, **_kwargs):
        pytest.fail("wrong epoch reached native construction")

    monkeypatch.setattr(ruby, "ruby_profile", unexpected)
    with pytest.raises(ValueError, match="committer timestamp"):
        build_ruby_package(
            source, replace(request, source_date_epoch=EPOCH + 1)
        )


@pytest.mark.parametrize("change", ["source", "epoch"])
def test_ruby_new_target_source_and_epoch_controls_change_original_bytes(
    native_build, tmp_path, change
):
    """New target source and epoch controls change original bytes."""
    _, original, _, baseline, _ = native_build
    source = tmp_path / "control"
    git(tmp_path, "clone", "--quiet", "--no-local", str(original), str(source))
    if change == "source":
        readme = source / RUBY_ROOT / "README.md"
        readme.write_bytes(
            readme.read_bytes() + b"\nControlled source variation.\n"
        )
    target = commit(source, epoch=EPOCH + (86400 if change == "epoch" else 0))
    # Origin is this fixture's authoritative upstream and must contain target.
    git(source, "push", "origin", "HEAD:refs/heads/control-" + change)
    git(source, "checkout", "--detach", target)
    provider = provide_ruby_repository_facts(
        source,
        binding(target),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    result = build_ruby_package(source, build_request(provider))
    evidence = (
        Path(
            os.environ.get(
                "WDV3_RUBY_TEST_EVIDENCE", str(tmp_path / "evidence")
            )
        )
        / change
    )
    evidence.mkdir(parents=True, exist_ok=False)
    (evidence / "original.gem").write_bytes(result.content)
    (evidence / "provider.json").write_bytes(
        canonicalize(provider.to_document())
    )
    (evidence / "inspection.json").write_bytes(result.inspection)
    assert result.content != baseline.content
    assert result.witness.target == target
    assert provider.source_date_epoch == EPOCH + (
        86400 if change == "epoch" else 0
    )
    with tarfile.open(fileobj=io.BytesIO(result.content)) as archive:
        data = archive.extractfile("data.tar.gz").read()
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(data))) as payload:
        readme = payload.extractfile("README.md").read()
        assert (b"Controlled source variation." in readme) is (
            change == "source"
        )
        assert (
            payload.getmember("README.md").mtime == provider.source_date_epoch
        )


def repack_members(content, *, unsafe=None, duplicate=False):
    """Construct hostile inputs solely for negative inspection tests."""
    output = io.BytesIO()
    with (
        tarfile.open(fileobj=io.BytesIO(content)) as source,
        tarfile.open(fileobj=output, mode="w") as target,
    ):
        for entry in source:
            data = source.extractfile(entry).read()
            target.addfile(entry, io.BytesIO(data))
            if duplicate:
                target.addfile(entry, io.BytesIO(data))
                duplicate = False
        if unsafe:
            entry = tarfile.TarInfo(unsafe)
            entry.size = 1
            target.addfile(entry, io.BytesIO(b"x"))
    return output.getvalue()


@pytest.mark.parametrize(
    "name", ["../outside", "/outside", "data//bad", "data\\bad"]
)
def test_ruby_inspection_rejects_unsafe_archive_members(native_build, name):
    """Inspection rejects unsafe archive members."""
    artifact = native_build[3]
    with pytest.raises(ValueError, match="unsafe or duplicate gem member"):
        inspect_ruby_distribution(
            artifact.filename,
            repack_members(artifact.content, unsafe=name),
            artifact.witness,
        )


def test_ruby_inspection_rejects_duplicate_archive_members(native_build):
    """Inspection rejects duplicate archive members."""
    artifact = native_build[3]
    with pytest.raises(ValueError, match="unsafe or duplicate gem member"):
        inspect_ruby_distribution(
            artifact.filename,
            repack_members(artifact.content, duplicate=True),
            artifact.witness,
        )


@pytest.mark.parametrize("change", ["witness", "version"])
def test_ruby_inspection_rejects_conflicting_frozen_identity(
    native_build, change
):
    """Inspection rejects conflicting frozen identity."""
    artifact = native_build[3]
    if change == "witness":
        witness = replace(artifact.witness, control_digest="sha256:" + "d" * 64)
        filename = artifact.filename
        error = "witness mismatch"
    else:
        raw = parse_canonical_json(artifact.witness.nbgv.raw_bytes)
        raw["SemVer2"] = "9.8.7"

        witness = replace(
            artifact.witness, nbgv=RubyNbgvFacts(canonicalize(raw), "9.8.7")
        )
        filename = "hcoona-release-smoke-ruby-9.8.7.gem"
        error = "specification differs"
    with pytest.raises(ValueError, match=error):
        inspect_ruby_distribution(filename, artifact.content, witness)


def test_ruby_native_clean_install_has_no_producer_tools_and_matches_witness(
    native_build, monkeypatch
):
    """Native clean install has no producer tools and matches witness."""
    source, _, _, artifact, _ = native_build
    native = ruby.run_native
    calls = []

    def capture(command, cwd, environment):
        calls.append((command, cwd, environment))
        assert environment["PATH"] == ""
        assert not cwd.is_relative_to(source)
        assert environment["GEM_HOME"] == environment["GEM_PATH"]
        assert "--local" in command
        assert "--no-document" in command
        return native(command, cwd, environment)

    monkeypatch.setattr(ruby, "run_native", capture)
    result = qualify_ruby_consumer(artifact)
    assert len(calls) == 1
    assert result["project-id"] == "hcoona-release-smoke-ruby"
    assert result["version"] == artifact.witness.nbgv.native_version
    assert result["artifact-digest"] == artifact.digest
    assert result["witness-digest"] == canonical_sha256(
        artifact.witness.to_document()
    )
    assert "1 gem installed" in result["install-output"]


def test_ruby_inspection_rejects_static_version_conflict_with_valid_checksums(
    native_build,
):
    """A valid native archive cannot hide differing installed version code."""
    artifact = native_build[3]
    with tarfile.open(fileobj=io.BytesIO(artifact.content)) as archive:
        outer = {
            entry.name: archive.extractfile(entry).read() for entry in archive
        }
    data = io.BytesIO()
    with (
        tarfile.open(
            fileobj=io.BytesIO(gzip.decompress(outer["data.tar.gz"]))
        ) as source,
        tarfile.open(fileobj=data, mode="w") as target,
    ):
        for entry in source:
            content = source.extractfile(entry).read()
            if entry.name == RUBY_VERSION_PATH:
                content = b'HcoonaReleaseSmokeRuby::VERSION = "9.9.9"\n'
                entry.size = len(content)
            target.addfile(entry, io.BytesIO(content))
    outer["data.tar.gz"] = gzip.compress(data.getvalue(), mtime=EPOCH)
    checksums = yaml.safe_load(gzip.decompress(outer["checksums.yaml.gz"]))
    for algorithm in ("SHA256", "SHA512"):
        checksums[algorithm]["data.tar.gz"] = hashlib.new(
            algorithm.lower(), outer["data.tar.gz"]
        ).hexdigest()
    outer["checksums.yaml.gz"] = gzip.compress(
        yaml.safe_dump(checksums).encode(), mtime=EPOCH
    )
    modified = io.BytesIO()
    with tarfile.open(fileobj=modified, mode="w") as archive:
        for name, content in outer.items():
            entry = tarfile.TarInfo(name)
            entry.size = len(content)
            archive.addfile(entry, io.BytesIO(content))
    with pytest.raises(
        ValueError, match="static version or source witness mismatch"
    ):
        inspect_ruby_distribution(
            artifact.filename, modified.getvalue(), artifact.witness
        )


def test_ruby_build_rejects_frozen_compression_profile_drift(native_build):
    """An admitted profile with another zlib identity cannot build here."""
    source, _, request, _, _ = native_build
    profile = parse_canonical_json(request.native_profile)
    profile["zlib-runtime"] = "different-native-runtime"
    with pytest.raises(ValueError, match="compression profile drift"):
        build_ruby_package(
            source, replace(request, native_profile=canonicalize(profile))
        )
