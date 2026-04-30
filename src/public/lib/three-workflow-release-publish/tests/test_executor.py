"""Tests for workflow-release publish executors."""

from __future__ import annotations

import copy
import hashlib
import io
import json
import subprocess
import tarfile
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING, Any, Self

import pytest
import three_workflow_release_publish.executor as executor_module
from three_workflow_release_contracts import contracts, validate_contract
from three_workflow_release_publish import PublishExecutorError, execute_publish

REPO_ROOT = Path(__file__).parents[5]
SHA = "a" * 40

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence


def test_pypi_executor_verifies_identity_and_uses_uv_trusted_publish() -> None:
    """Publish Python distributions with uv trusted publishing."""
    scratch = REPO_ROOT / ".publish-executor-pypi-test"
    _reset_scratch(scratch)
    try:
        wheel = scratch / "input" / "example-1.2.3-py3-none-any.whl"
        _write_wheel(wheel, "Example", "1.2.3")
        request = _request(
            family="pypi",
            host="pypi.org",
            artifact_path=wheel,
            concrete_kind="wheel",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={
                "final-distribution-filenames-by-artifact-id": {
                    "artifact/package": wheel.name,
                },
            },
        )
        calls = _Calls()

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
            work_dir=scratch / "work",
        )

        validate_contract(result)
        assert calls.commands[0][:4] == [
            "uv",
            "publish",
            "--trusted-publishing",
            "always",
        ]
        assert Path(calls.commands[0][4]).name == wheel.name
        assert result["publish-node-id"] == "publish-node/pypi"
        evidence = result["evidence"]
        assert isinstance(evidence, dict)
        assert evidence["project-url"] == "https://pypi.org/project/example/"
        assert (
            evidence["release-url"] == "https://pypi.org/project/example/1.2.3/"
        )
    finally:
        _reset_scratch(scratch)


def test_npm_executor_verifies_tarball_and_uses_provenance() -> None:
    """Publish npm tarballs with provenance enabled."""
    scratch = REPO_ROOT / ".publish-executor-npm-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "example-1.2.3.tgz"
        _write_npm_tarball(package, "example", "1.2.3")
        request = _request(
            family="npm",
            host="registry.npmjs.org",
            artifact_path=package,
            concrete_kind="npm-package",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls()

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls.commands == [
            ["npm", "publish", str(package), "--provenance"]
        ]
    finally:
        _reset_scratch(scratch)


def test_nuget_executor_pushes_github_packages_source(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Publish NuGet package to GitHub Packages."""
    scratch = REPO_ROOT / ".publish-executor-nuget-test"
    _reset_scratch(scratch)
    try:
        monkeypatch.setenv("GITHUB_TOKEN", "github-token")
        package = scratch / "input" / "Example.1.2.3.nupkg"
        _write_nuget_package(package, "Example", "1.2.3")
        request = _request(
            family="nuget",
            host="nuget.pkg.github.com",
            owner="hcoona",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"package-name": "example", "version": "1.2.3.0"},
            projection={
                "final-distribution-filenames-by-artifact-id": {
                    "artifact/package": package.name,
                },
            },
        )
        calls = _Calls()

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
            work_dir=scratch / "work",
        )

        validate_contract(result)
        assert calls.commands[0][:3] == ["dotnet", "nuget", "push"]
        assert Path(calls.commands[0][3]).name == package.name
        assert calls.commands[0][4:] == [
            "--source",
            "https://nuget.pkg.github.com/hcoona/index.json",
            "--api-key",
            "github-token",
        ]
    finally:
        _reset_scratch(scratch)


def test_nuget_github_packages_requires_github_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Refuse GitHub Packages NuGet publish without GITHUB_TOKEN."""
    scratch = REPO_ROOT / ".publish-executor-nuget-token-test"
    _reset_scratch(scratch)
    try:
        monkeypatch.delenv("GITHUB_TOKEN", raising=False)
        package = scratch / "input" / "Example.1.2.3.nupkg"
        _write_nuget_package(package, "Example", "1.2.3")
        request = _request(
            family="nuget",
            host="nuget.pkg.github.com",
            owner="hcoona",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"package-name": "example", "version": "1.2.3.0"},
            projection={
                "final-distribution-filenames-by-artifact-id": {
                    "artifact/package": package.name,
                },
            },
        )
        calls = _Calls()

        with pytest.raises(PublishExecutorError, match="GITHUB_TOKEN"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert calls.commands == []
    finally:
        _reset_scratch(scratch)


def test_nuget_executor_rejects_unpublished_snupkg() -> None:
    """Fail closed when the planned NuGet artifact set includes symbols."""
    scratch = REPO_ROOT / ".publish-executor-nuget-symbols-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "Example.1.2.3.nupkg"
        symbols = scratch / "input" / "Example.1.2.3.snupkg"
        _write_nuget_package(package, "Example", "1.2.3")
        _write_nuget_package(symbols, "Example", "1.2.3")
        request = _request(
            family="nuget",
            host="nuget.pkg.github.com",
            owner="hcoona",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"package-name": "example", "version": "1.2.3.0"},
            projection={
                "final-distribution-filenames-by-artifact-id": {
                    "artifact/package": package.name,
                },
            },
        )
        _add_artifact(
            request,
            artifact_id="artifact/symbols",
            artifact_path=symbols,
            concrete_kind="snupkg",
            role="symbols-package",
        )
        request["publish-node"]["projection"][
            "final-distribution-filenames-by-artifact-id"
        ]["artifact/symbols"] = symbols.name
        validate_contract(request)
        calls = _Calls()

        with pytest.raises(PublishExecutorError, match=r"\.snupkg"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert calls.commands == []
    finally:
        _reset_scratch(scratch)


def test_rubygems_executor_pushes_github_packages_host(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Publish RubyGems package to GitHub Packages."""
    scratch = REPO_ROOT / ".publish-executor-rubygems-test"
    _reset_scratch(scratch)
    try:
        monkeypatch.setenv("GITHUB_TOKEN", "github-token")
        package = scratch / "input" / "example-1.2.3.gem"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"gem")
        request = _request(
            family="rubygems",
            host="rubygems.pkg.github.com",
            owner="hcoona",
            artifact_path=package,
            concrete_kind="rubygem",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls(
            responses={
                (
                    "gem",
                    "specification",
                    str(package),
                    "name",
                    "--yaml",
                ): "--- example\n",
                (
                    "gem",
                    "specification",
                    str(package),
                    "version",
                    "--yaml",
                ): "--- 1.2.3\n",
                ("ruby",): "true\n",
            },
        )

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls.commands[-1] == [
            "gem",
            "push",
            "--host",
            "https://rubygems.pkg.github.com/hcoona",
            str(package),
        ]
        assert calls.envs[-1] == {"GEM_HOST_API_KEY": "Bearer github-token"}
    finally:
        _reset_scratch(scratch)


def test_rubygems_github_packages_requires_github_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Refuse GitHub Packages RubyGems publish without GITHUB_TOKEN."""
    scratch = REPO_ROOT / ".publish-executor-rubygems-token-test"
    _reset_scratch(scratch)
    try:
        monkeypatch.delenv("GITHUB_TOKEN", raising=False)
        package = scratch / "input" / "example-1.2.3.gem"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"gem")
        request = _request(
            family="rubygems",
            host="rubygems.pkg.github.com",
            owner="hcoona",
            artifact_path=package,
            concrete_kind="rubygem",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls(
            responses={
                (
                    "gem",
                    "specification",
                    str(package),
                    "name",
                    "--yaml",
                ): "--- example\n",
                (
                    "gem",
                    "specification",
                    str(package),
                    "version",
                    "--yaml",
                ): "--- 1.2.3\n",
                ("ruby",): "true\n",
            },
        )

        with pytest.raises(PublishExecutorError, match="GITHUB_TOKEN"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
            )
        assert not any(
            command[:2] == ["gem", "push"] for command in calls.commands
        )
    finally:
        _reset_scratch(scratch)


def test_rubygems_org_executor_uses_oidc_token_exchange(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Publish to RubyGems.org with a short-lived trusted-publisher token."""
    scratch = REPO_ROOT / ".publish-executor-rubygems-org-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "example-1.2.3.gem"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"gem")
        request = _request(
            family="rubygems",
            host="rubygems.org",
            artifact_path=package,
            concrete_kind="rubygem",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls(
            responses={
                (
                    "gem",
                    "specification",
                    str(package),
                    "name",
                    "--yaml",
                ): "--- example\n",
                (
                    "gem",
                    "specification",
                    str(package),
                    "version",
                    "--yaml",
                ): "--- 1.2.3\n",
                ("ruby",): "true\n",
            },
        )
        monkeypatch.setattr(
            executor_module,
            "_exchange_rubygems_trusted_publisher_token",
            lambda: "short-lived-token",
        )

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
        )

        validate_contract(result)
        assert calls.commands[-1] == [
            "gem",
            "push",
            "--host",
            "https://rubygems.org",
            str(package),
        ]
        assert calls.envs[-1] == {"GEM_HOST_API_KEY": "short-lived-token"}
        evidence = result["evidence"]
        assert isinstance(evidence, dict)
        assert evidence["gem-url"] == "https://rubygems.org/gems/example"
        assert evidence["version-url"] == (
            "https://rubygems.org/gems/example/versions/1.2.3"
        )
    finally:
        _reset_scratch(scratch)


def test_rubygems_oidc_exchange_uses_github_actions_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Exchange the GitHub Actions OIDC token at RubyGems.org."""
    monkeypatch.setenv(
        "ACTIONS_ID_TOKEN_REQUEST_URL",
        "https://actions.example.invalid/oidc/token",
    )
    monkeypatch.setenv("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "request-token")
    calls: list[tuple[str, dict[str, str], bytes | None]] = []
    responses = [
        b'{"value":"github-oidc-token"}',
        b'{"rubygems_api_key":"short-lived-token"}',
    ]

    def fake_urlopen(req: Any, timeout: int) -> _FakeHttpResponse:
        assert timeout > 0
        calls.append((req.full_url, dict(req.header_items()), req.data))
        return _FakeHttpResponse(responses.pop(0))

    monkeypatch.setattr(executor_module.urlrequest, "urlopen", fake_urlopen)

    token = executor_module._exchange_rubygems_trusted_publisher_token()  # noqa: SLF001

    assert token == "short-lived-token"  # noqa: S105
    assert calls[0][0].endswith("audience=rubygems.org")
    assert calls[0][1]["Authorization"] == "bearer request-token"
    assert calls[1][0] == (
        "https://rubygems.org/api/v1/oidc/trusted_publisher/exchange_token"
    )
    assert calls[1][2] == b'{"jwt": "github-oidc-token"}'


def test_github_release_executor_uses_planned_asset_names_and_labels() -> None:
    """Publish GitHub Release assets through gh using planner-frozen names."""
    scratch = REPO_ROOT / ".publish-executor-github-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "wrong-name.nupkg"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"asset")
        bundle = REPO_ROOT / "attestations" / "Example.1.2.3.nupkg.json"
        request = _request(
            family="github-release",
            host="github",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"release-tag": "release/example/v1.2.3"},
            projection={
                "asset-names-by-artifact-id": {
                    "artifact/package": "Example.1.2.3.nupkg",
                },
                "asset-labels-by-artifact-id": {
                    "artifact/package": "NuGet package",
                },
            },
            desired_state={"release-state": "prerelease"},
        )
        request["github-release-asset-attestations"]["artifact/package"][
            "bundle-path"
        ] = bundle.as_posix()
        _write_attestation_bundles(request)
        calls = _Calls(
            responses={
                ("gh", "release", "view"): json.dumps(
                    {"url": "https://example.invalid", "assets": []},
                ),
            },
        )

        result = execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
            work_dir=scratch / "work",
        )

        validate_contract(result)
        assert calls.commands[0][:3] == ["gh", "attestation", "verify"]
        assert "--signer-workflow" in calls.commands[0]
        assert calls.commands[0][4:6] == ["--bundle", bundle.as_posix()]
        assert calls.commands[1][:4] == [
            "gh",
            "release",
            "create",
            "release/example/v1.2.3",
        ]
        assert any(
            argument.endswith("Example.1.2.3.nupkg#NuGet package")
            for argument in calls.commands[1]
        )
        evidence = result["evidence"]
        assert isinstance(evidence, dict)
        attestations = evidence["asset-attestations"]
        assert isinstance(attestations, dict)
        assert attestations["artifact/package"]["asset-name"] == (
            "Example.1.2.3.nupkg"
        )
    finally:
        _remove_attestation_bundles()
        _reset_scratch(scratch)


def test_github_release_replacement_promotes_after_asset_upload() -> None:
    """Authoritative replacement promotes after verification/upload."""
    scratch = REPO_ROOT / ".publish-executor-github-promote-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "wrong-name.nupkg"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"asset")
        request = _request(
            family="github-release",
            host="github",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"release-tag": "release/example/v1.2.3"},
            projection={
                "asset-names-by-artifact-id": {
                    "artifact/package": "Example.1.2.3.nupkg",
                },
                "asset-labels-by-artifact-id": {},
            },
            desired_state={"release-state": "release"},
        )
        request["publish-node"]["publish-mode"] = "replace-authoritative"
        _write_attestation_bundles(request)
        calls = _Calls(
            responses={
                ("gh", "release", "view"): json.dumps(
                    {"url": "https://example.invalid", "assets": []},
                ),
            },
        )

        execute_publish(
            request,
            REPO_ROOT,
            runner=calls.runner,
            check_commit=False,
            work_dir=scratch / "work",
        )

        assert calls.commands[0][:3] == ["gh", "attestation", "verify"]
        assert calls.commands[1][:3] == ["gh", "release", "view"]
        assert calls.commands[2][:3] == ["gh", "release", "upload"]
        assert calls.commands[3] == [
            "gh",
            "release",
            "edit",
            "release/example/v1.2.3",
            "--repo",
            "hcoona/three",
            "--verify-tag",
            "--prerelease=false",
        ]
    finally:
        _remove_attestation_bundles()
        _reset_scratch(scratch)


def test_github_release_requires_attestation_bundle_file() -> None:
    """Refuse a positive result when the supplied bundle path is missing."""
    scratch = REPO_ROOT / ".publish-executor-github-attestation-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "wrong-name.nupkg"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"asset")
        request = _request(
            family="github-release",
            host="github",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"release-tag": "release/example/v1.2.3"},
            projection={
                "asset-names-by-artifact-id": {
                    "artifact/package": "Example.1.2.3.nupkg",
                },
                "asset-labels-by-artifact-id": {},
            },
            desired_state={"release-state": "prerelease"},
        )
        _remove_attestation_bundles()
        calls = _Calls()

        with pytest.raises(PublishExecutorError, match="bundle is missing"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert not any(
            command[:3] == ["gh", "attestation", "verify"]
            for command in calls.commands
        )
        assert not any(
            command[:2] == ["gh", "release"] for command in calls.commands
        )
    finally:
        _reset_scratch(scratch)


def test_github_release_rejects_invalid_attestation_bundle_path() -> None:
    """Refuse unsafe repo-relative attestation bundle paths."""
    scratch = REPO_ROOT / ".publish-executor-github-attestation-invalid-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "wrong-name.nupkg"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"asset")
        request = _request(
            family="github-release",
            host="github",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"release-tag": "release/example/v1.2.3"},
            projection={
                "asset-names-by-artifact-id": {
                    "artifact/package": "Example.1.2.3.nupkg",
                },
                "asset-labels-by-artifact-id": {},
            },
            desired_state={"release-state": "prerelease"},
        )
        _remove_attestation_bundles()
        request["github-release-asset-attestations"]["artifact/package"][
            "bundle-path"
        ] = "../attestation.json"
        calls = _Calls()

        with pytest.raises(PublishExecutorError, match="repo-relative"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert not any(
            command[:3] == ["gh", "attestation", "verify"]
            for command in calls.commands
        )
        assert not any(
            command[:2] == ["gh", "release"] for command in calls.commands
        )
    finally:
        _remove_attestation_bundles()
        _reset_scratch(scratch)


def test_github_release_requires_verified_asset_attestation() -> None:
    """Refuse a positive result when any bundle does not verify the asset."""
    scratch = REPO_ROOT / ".publish-executor-github-attestation-bad-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "wrong-name.nupkg"
        package.parent.mkdir(parents=True, exist_ok=True)
        package.write_bytes(b"asset")
        request = _request(
            family="github-release",
            host="github",
            artifact_path=package,
            concrete_kind="nuget",
            identity={"release-tag": "release/example/v1.2.3"},
            projection={
                "asset-names-by-artifact-id": {
                    "artifact/package": "Example.1.2.3.nupkg",
                },
                "asset-labels-by-artifact-id": {},
            },
            desired_state={"release-state": "prerelease"},
        )
        _write_attestation_bundles(request)
        calls = _Calls(responses={("gh", "attestation", "verify"): "[]"})

        with pytest.raises(PublishExecutorError, match="attestation"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert "--bundle" in calls.commands[0]
        assert not any(
            command[:2] == ["gh", "release"] for command in calls.commands
        )
    finally:
        _remove_attestation_bundles()
        _reset_scratch(scratch)


def test_pypi_requires_one_wheel_before_upload() -> None:
    """Reject PyPI artifact sets without the required wheel."""
    scratch = REPO_ROOT / ".publish-executor-pypi-set-test"
    _reset_scratch(scratch)
    try:
        sdist = scratch / "input" / "example-1.2.3.tar.gz"
        _write_sdist(sdist, "Example", "1.2.3")
        request = _request(
            family="pypi",
            host="pypi.org",
            artifact_path=sdist,
            concrete_kind="sdist",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={
                "final-distribution-filenames-by-artifact-id": {
                    "artifact/package": sdist.name,
                },
            },
        )
        calls = _Calls()

        with pytest.raises(PublishExecutorError, match="exactly one wheel"):
            execute_publish(
                request,
                REPO_ROOT,
                runner=calls.runner,
                check_commit=False,
                work_dir=scratch / "work",
            )
        assert calls.commands == []
    finally:
        _reset_scratch(scratch)


def test_digest_mismatch_fails_before_publish() -> None:
    """Refuse to publish when artifact receipt does not match bytes."""
    scratch = REPO_ROOT / ".publish-executor-digest-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "example-1.2.3.tgz"
        _write_npm_tarball(package, "example", "1.2.3")
        request = _request(
            family="npm",
            host="registry.npmjs.org",
            artifact_path=package,
            concrete_kind="npm-package",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        request["artifacts"]["artifact/package"]["sha256"] = "0" * 64

        with pytest.raises(PublishExecutorError, match="receipt mismatch"):
            execute_publish(
                request, REPO_ROOT, runner=_Calls().runner, check_commit=False
            )
    finally:
        _reset_scratch(scratch)


def test_identity_mismatch_fails_before_publish() -> None:
    """Refuse to publish when package metadata differs from frozen identity."""
    scratch = REPO_ROOT / ".publish-executor-identity-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "example-1.2.3.tgz"
        _write_npm_tarball(package, "wrong", "1.2.3")
        request = _request(
            family="npm",
            host="registry.npmjs.org",
            artifact_path=package,
            concrete_kind="npm-package",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls()

        with pytest.raises(
            PublishExecutorError, match="does not match frozen name"
        ):
            execute_publish(
                request, REPO_ROOT, runner=calls.runner, check_commit=False
            )
        assert calls.commands == []
    finally:
        _reset_scratch(scratch)


def test_commit_mismatch_fails_before_publish() -> None:
    """Refuse to publish from the wrong checkout commit."""
    scratch = REPO_ROOT / ".publish-executor-commit-test"
    _reset_scratch(scratch)
    try:
        package = scratch / "input" / "example-1.2.3.tgz"
        _write_npm_tarball(package, "example", "1.2.3")
        request = _request(
            family="npm",
            host="registry.npmjs.org",
            artifact_path=package,
            concrete_kind="npm-package",
            identity={"package-name": "example", "version": "1.2.3"},
            projection={},
        )
        calls = _Calls(responses={("git", "rev-parse", "HEAD"): "b" * 40})

        with pytest.raises(
            PublishExecutorError, match="does not match publish commit"
        ):
            execute_publish(request, REPO_ROOT, runner=calls.runner)
        assert calls.commands == [["git", "rev-parse", "HEAD"]]
    finally:
        _reset_scratch(scratch)


class _Calls:
    """Record subprocess calls and return configured output."""

    def __init__(
        self, responses: dict[tuple[str, ...], str] | None = None
    ) -> None:
        self.commands: list[list[str]] = []
        self.envs: list[dict[str, str] | None] = []
        self.responses = responses or {}

    def runner(
        self,
        args: Sequence[str],
        _cwd: Path,
        env: Mapping[str, str] | None,
    ) -> subprocess.CompletedProcess[str]:
        command = list(args)
        command[0] = Path(command[0]).name
        self.commands.append(command)
        self.envs.append(dict(env) if env is not None else None)
        stdout = ""
        for prefix, response in self.responses.items():
            if tuple(command[: len(prefix)]) == prefix:
                stdout = response
                break
        else:
            if command[:3] == ["gh", "attestation", "verify"]:
                path = Path(command[3])
                stdout = _attestation_verify_json(path.name, path.read_bytes())
        return subprocess.CompletedProcess(command, 0, stdout, "")


class _FakeHttpResponse:
    """Minimal context manager for urllib response fakes."""

    def __init__(self, payload: bytes) -> None:
        self._payload = payload

    def __enter__(self) -> Self:
        return self

    def __exit__(self, *_args: object) -> None:
        return None

    def read(self) -> bytes:
        return self._payload


def _request(  # noqa: PLR0913
    *,
    family: str,
    host: str,
    artifact_path: Path,
    concrete_kind: str,
    identity: dict[str, str],
    projection: dict[str, Any],
    owner: str | None = None,
    desired_state: dict[str, str] | None = None,
) -> dict[str, Any]:
    artifact_id = "artifact/package"
    target_id = f"{family}/public"
    destination: dict[str, str] = {"host": host}
    if family == "github-release":
        destination.update({"owner": "hcoona", "repo": "three"})
    elif owner is not None:
        destination["owner"] = owner
    data = artifact_path.read_bytes()
    request: dict[str, Any] = {
        "api-version": "three.release.publish-request/v1alpha1",
        "kind": "publish-request",
        "plan-id": "plan/example",
        "profile": "buddy",
        "commit-sha": SHA,
        "publish-node-id": f"publish-node/{family}",
        "project": {
            "display-name": "Example",
            "ecosystem": "python",
            "release-kind": "lib",
            "descriptor-path": "src/public/lib/Example/three.release.yml",
            "release-root": "src/public/lib/Example",
            "resolved-version": "1.2.3",
            "source": {
                "primary-manifest-path": (
                    "src/public/lib/Example/pyproject.toml"
                ),
                "auxiliary-input-paths": [],
                "version-authority-kind": "build-system-nbgv",
            },
            "variant-ids": ["variant/package"],
            "publish-node-ids": [f"publish-node/{family}"],
        },
        "publish-node": {
            "publish-node-id": f"publish-node/{family}",
            "project-id": "example",
            "profile": "buddy",
            "descriptor-target-index": 0,
            "target-instance-snapshot-id": target_id,
            "artifact-ids": [artifact_id],
            "publish-disposition": "publish",
            "publish-mode": "create-only",
            "resolved-publish-identity": identity,
            "projection": projection,
        },
        "target-instance-snapshot": {
            "family": family,
            "instance-id": "public",
            "catalog-ref": target_id,
            "contract": _contract(family, concrete_kind),
            "destination": destination,
            "capabilities": _capabilities(family, host),
        },
        "artifacts": {
            artifact_id: {
                "artifact": {
                    "project-id": "example",
                    "variant-id": "variant/package",
                    "descriptor-handle": concrete_kind,
                    "role": "primary-package",
                    "kind-family": "package",
                    "concrete-kind": concrete_kind,
                    "produced-from-artifact-ids": [],
                },
                "input-path": artifact_path.relative_to(REPO_ROOT).as_posix(),
                "bundle-relative-path": f"dist/{artifact_path.name}",
                "sha256": hashlib.sha256(data).hexdigest(),
                "byte-size": len(data),
            },
        },
    }
    if desired_state is not None:
        request["publish-node"]["desired-publish-state"] = desired_state
    if family == "github-release":
        request["publish-node"]["attestation"] = {
            "signer-workflow": (
                "hcoona/three/.github/workflows/release-publish-node.yml"
            ),
        }
        names = projection["asset-names-by-artifact-id"]
        request["github-release-asset-attestations"] = {
            artifact_id: {
                "attestation-id": "att-1",
                "attestation-url": "https://github.com/hcoona/three/attestations/1",
                "bundle-path": f"attestations/{names[artifact_id]}.json",
            },
        }
    validate_contract(request)
    return request


def _add_artifact(
    request: dict[str, Any],
    *,
    artifact_id: str,
    artifact_path: Path,
    concrete_kind: str,
    role: str,
) -> None:
    """Add a planned artifact to a publish-request fixture."""
    data = artifact_path.read_bytes()
    request["publish-node"]["artifact-ids"].append(artifact_id)
    request["artifacts"][artifact_id] = {
        "artifact": {
            "project-id": "example",
            "variant-id": "variant/package",
            "descriptor-handle": concrete_kind,
            "role": role,
            "kind-family": "package",
            "concrete-kind": concrete_kind,
            "produced-from-artifact-ids": [],
        },
        "input-path": artifact_path.relative_to(REPO_ROOT).as_posix(),
        "bundle-relative-path": f"dist/{artifact_path.name}",
        "sha256": hashlib.sha256(data).hexdigest(),
        "byte-size": len(data),
    }


def _contract(family: str, concrete_kind: str) -> dict[str, Any]:
    contract_id = (
        f"{family}-publish"
        if family != "github-release"
        else "github-release-assets"
    )
    expected_contracts = contracts.__dict__["_EXPECTED_CONTRACTS"]
    value = copy.deepcopy(expected_contracts[contract_id])
    value["id"] = contract_id
    assert concrete_kind
    return value


def _capabilities(family: str, host: str) -> dict[str, str]:
    if family == "github-release":
        return {
            "mutability": "mutable-prerelease",
            "name-uniqueness-scope": "release-tag",
            "version-uniqueness-rule": "tag",
            "profile-coexistence-rule": "not-applicable",
            "credential-posture": "github-token",
            "publish-topology": "github-token",
        }
    if host.endswith("pkg.github.com"):
        return {
            "mutability": "immutable",
            "name-uniqueness-scope": "package-name-with-owner",
            "version-uniqueness-rule": "package-name-plus-version",
            "profile-coexistence-rule": "requires-distinct-name",
            "credential-posture": "github-token",
            "publish-topology": "github-token",
        }
    return {
        "mutability": "immutable",
        "name-uniqueness-scope": "package-name",
        "version-uniqueness-rule": "package-name-plus-version",
        "profile-coexistence-rule": "requires-distinct-name",
        "credential-posture": "oidc",
        "publish-topology": "external-oidc-reusable-workflow"
        if family == "rubygems"
        else "external-oidc-entry-workflow",
    }


def _write_wheel(path: Path, name: str, version: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(
            f"{name}-{version}.dist-info/METADATA",
            f"Name: {name}\nVersion: {version}\n",
        )


def _write_npm_tarball(path: Path, name: str, version: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps({"name": name, "version": version}).encode()
    info = tarfile.TarInfo("package/package.json")
    info.size = len(payload)
    with tarfile.open(path, "w:gz") as archive:
        archive.addfile(info, io.BytesIO(payload))


def _write_sdist(path: Path, name: str, version: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = f"Name: {name}\nVersion: {version}\n".encode()
    info = tarfile.TarInfo(f"{name}-{version}/PKG-INFO")
    info.size = len(payload)
    with tarfile.open(path, "w:gz") as archive:
        archive.addfile(info, io.BytesIO(payload))


def _write_nuget_package(path: Path, name: str, version: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(
            "Example.nuspec",
            f"<package><metadata><id>{name}</id><version>{version}</version></metadata></package>",
        )


def _attestation_verify_json(subject_name: str, data: bytes) -> str:
    return json.dumps(
        [
            {
                "verificationResult": {
                    "statement": {
                        "predicateType": "https://slsa.dev/provenance/v1",
                        "subject": [
                            {
                                "name": subject_name,
                                "digest": {
                                    "sha256": hashlib.sha256(data).hexdigest()
                                },
                            }
                        ],
                    }
                }
            }
        ]
    )


def _write_attestation_bundles(request: dict[str, Any]) -> None:
    outputs = request.get("github-release-asset-attestations")
    if not isinstance(outputs, dict):
        return
    for output in outputs.values():
        assert isinstance(output, dict)
        path = REPO_ROOT / str(output["bundle-path"])
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("{}", encoding="utf-8")


def _remove_attestation_bundles() -> None:
    root = REPO_ROOT / "attestations"
    if root.exists():
        _remove_tree(root)


def _reset_scratch(path: Path) -> None:
    if path.exists():
        _remove_tree(path)


def _remove_tree(path: Path) -> None:
    if path.is_symlink() or not path.is_dir():
        path.unlink()
        return
    for child in path.iterdir():
        _remove_tree(child)
    path.rmdir()
