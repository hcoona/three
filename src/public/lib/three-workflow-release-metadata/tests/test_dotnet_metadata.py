"""Tests for workflow-release .NET metadata helpers."""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, Any

import pytest
from three_workflow_release_contracts import validate_contract
from three_workflow_release_metadata import (
    DotnetMetadataError,
    collect_dotnet_metadata,
)
from three_workflow_release_metadata.cli import main as cli_main

if TYPE_CHECKING:
    from collections.abc import Sequence

REPO_ROOT = Path(__file__).parents[5]
_PACKAGE_ID_MSBUILD_CALL_COUNT = 2
_MAX_NUGET_PACKAGE_ID_LENGTH = 100
_OVER_LENGTH_NUGET_PACKAGE_ID_LENGTH = _MAX_NUGET_PACKAGE_ID_LENGTH + 1
CONTRACT_FIXTURES = (
    REPO_ROOT
    / "src/public/lib/three-workflow-release-contracts/tests/fixtures/valid"
)
_TRUSTED_NBGV = "/trusted/tools/nbgv"


@pytest.fixture(autouse=True)
def _trusted_nbgv_for_metadata_tests(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Provide the isolated NBGV CLI path required by metadata collection."""
    monkeypatch.setenv("THREE_WORKFLOW_RELEASE_NBGV_PATH", _TRUSTED_NBGV)


def _load(path: Path) -> dict[str, Any]:
    """Load one JSON fixture."""
    return json.loads(path.read_text(encoding="utf-8"))


def _metadata_input() -> dict[str, Any]:
    """Return the shared valid .NET metadata input fixture."""
    return _load(CONTRACT_FIXTURES / "dotnet-planner-metadata-input.json")


def _is_nbgv_call(args: Sequence[str]) -> bool:
    """Return whether a subprocess invocation targets the trusted NBGV CLI."""
    return Path(str(args[0])).name == "nbgv"


def _metadata_input_with_manifests(
    scratch: Path,
    *,
    package_id: str | None,
    package_id_condition: str | None = None,
) -> dict[str, Any]:
    """Return metadata input whose project manifests exist under scratch."""
    app_manifest = scratch / "App" / "App.csproj"
    example_manifest = scratch / "Example" / "Example.csproj"
    app_manifest.parent.mkdir(parents=True)
    example_manifest.parent.mkdir(parents=True)
    app_manifest.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup /></Project>',
        encoding="utf-8",
    )
    condition = (
        f' Condition="{package_id_condition}"'
        if package_id_condition is not None
        else ""
    )
    package_id_xml = (
        f"<PackageId{condition}>{package_id}</PackageId>"
        if package_id is not None
        else ""
    )
    example_manifest.write_text(
        (
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            f"{package_id_xml}"
            "</PropertyGroup></Project>"
        ),
        encoding="utf-8",
    )
    metadata_input = _metadata_input()
    projects = metadata_input["projects"]
    assert isinstance(projects, dict)
    app = projects["app"]
    example = projects["example"]
    assert isinstance(app, dict)
    assert isinstance(example, dict)
    app["primary-manifest-path"] = app_manifest.relative_to(
        REPO_ROOT
    ).as_posix()
    example["primary-manifest-path"] = example_manifest.relative_to(
        REPO_ROOT
    ).as_posix()
    return metadata_input


def _assert_input_diagnostic(error: DotnetMetadataError) -> None:
    """Assert an input error is a closed request-scoped diagnostic."""
    diagnostics = error.diagnostics
    assert len(diagnostics) == 1
    assert diagnostics[0]["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostics[0]["scope-kind"] == "request"
    validate_contract(error.document())


def test_collect_dotnet_metadata_emits_closed_observation() -> None:
    """Collect versions and only required PackageId values."""
    scratch = REPO_ROOT / ".metadata-packaged-success-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    calls: list[tuple[str, ...]] = []

    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Explicit.Example",
        )

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == REPO_ROOT.resolve()
            calls.append(tuple(args))
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(
                args, 0, "Explicit.Example\n", ""
            )

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )

        validate_contract(document, metadata_input=metadata_input)
        projects = document["projects"]
        assert isinstance(projects, dict)
        example = projects["example"]
        assert isinstance(example, dict)
        assert example["package-id"] == "Explicit.Example"
        nbgv_projects = {
            call[call.index("--project") + 1]
            for call in calls
            if _is_nbgv_call(call)
        }
        assert nbgv_projects == {
            ".metadata-packageid-success-test/App",
            ".metadata-packageid-success-test/Example",
        }
        msbuild_calls = [call for call in calls if "msbuild" in call]
        assert len(msbuild_calls) == _PACKAGE_ID_MSBUILD_CALL_COUNT
        assert {call[2] for call in msbuild_calls} == {
            ".metadata-packageid-success-test/Example/Example.csproj"
        }
        assert any(
            "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in call
            for call in msbuild_calls
        )
        assert all(
            call[-1] == "-getProperty:PackageId" for call in msbuild_calls
        )
    finally:
        _remove_tree_scratch(scratch)


def test_collect_dotnet_metadata_uses_trusted_nbgv_path(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Metadata collection must not invoke target-controlled tool manifests."""
    scratch = REPO_ROOT / ".metadata-trusted-nbgv-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    calls: list[tuple[str, ...]] = []
    trusted_nbgv = scratch / "trusted-tools" / "nbgv"
    monkeypatch.setenv("THREE_WORKFLOW_RELEASE_NBGV_PATH", str(trusted_nbgv))

    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Trusted.Example",
        )

        def runner(
            args: Sequence[str],
            cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            assert cwd == REPO_ROOT.resolve()
            calls.append(tuple(args))
            if args[0] == str(trusted_nbgv):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "Trusted.Example\n", "")

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )

        validate_contract(document, metadata_input=metadata_input)
        nbgv_calls = [call for call in calls if call[0] == str(trusted_nbgv)]
        assert nbgv_calls
        assert all("tool" not in call for call in nbgv_calls)
        assert all("run" not in call for call in nbgv_calls)
        assert all(call[1] == "get-version" for call in nbgv_calls)
    finally:
        _remove_tree_scratch(scratch)


@pytest.mark.parametrize(
    ("configured_path", "expected_message"),
    [
        (None, "not configured"),
        ("   ", "not configured"),
        ("nbgv", "must be absolute"),
    ],
)
def test_collect_dotnet_metadata_requires_valid_trusted_nbgv_path(
    monkeypatch: pytest.MonkeyPatch,
    configured_path: str | None,
    expected_message: str,
) -> None:
    """Fail closed before metadata collection can fall back to PATH NBGV."""
    if configured_path is None:
        monkeypatch.delenv("THREE_WORKFLOW_RELEASE_NBGV_PATH", raising=False)
    else:
        monkeypatch.setenv("THREE_WORKFLOW_RELEASE_NBGV_PATH", configured_path)

    def runner(
        args: Sequence[str],
        _cwd: Path,
    ) -> subprocess.CompletedProcess[str]:
        message = f"unexpected NBGV invocation: {args}"
        raise AssertionError(message)

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(
            _metadata_input(),
            REPO_ROOT,
            runner=runner,
        )

    _assert_input_diagnostic(error.value)
    diagnostic = error.value.diagnostics[0]
    assert expected_message in str(diagnostic["message"])
    details = diagnostic["details"]
    assert isinstance(details, dict)
    assert details["environment-variable"] == "THREE_WORKFLOW_RELEASE_NBGV_PATH"


def test_collect_dotnet_metadata_fails_closed_on_missing_package_id() -> None:
    """Reject NuGet-shaped projects when evaluated PackageId is empty."""
    scratch = REPO_ROOT / ".metadata-packaged-empty-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()

    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostics = error.value.diagnostics
    assert diagnostics[0]["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostics[0]["project-id"] == "example"
    assert "PackageId" in str(diagnostics[0]["message"])
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_accepts_active_conditional_package_id() -> (
    None
):
    """Accept active pre-fallback PackageId when it matches."""
    scratch = REPO_ROOT / ".metadata-packageid-active-condition-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Active.Example",
            package_id_condition="'true' == 'true'",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "Active.Example\n", "")

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )
    finally:
        _remove_tree_scratch(scratch)

    projects = document["projects"]
    assert isinstance(projects, dict)
    example = projects["example"]
    assert isinstance(example, dict)
    assert example["package-id"] == "Active.Example"


def test_collect_dotnet_metadata_accepts_valid_package_id_edges() -> None:
    """Accept NuGet-valid PackageId dots, hyphens, and underscores."""
    scratch = REPO_ROOT / ".metadata-packaged-valid-edge-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Good.Name-Ok_1",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "Good.Name-Ok_1\n", "")

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )
    finally:
        _remove_tree_scratch(scratch)

    projects = document["projects"]
    assert isinstance(projects, dict)
    example = projects["example"]
    assert isinstance(example, dict)
    assert example["package-id"] == "Good.Name-Ok_1"


def test_collect_dotnet_metadata_accepts_max_length_package_id() -> None:
    """Accept a NuGet PackageId at the 100-character maximum."""
    package_id = "A" * _MAX_NUGET_PACKAGE_ID_LENGTH
    scratch = REPO_ROOT / ".metadata-packaged-max-length-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id=package_id,
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, f"{package_id}\n", "")

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )
    finally:
        _remove_tree_scratch(scratch)

    projects = document["projects"]
    assert isinstance(projects, dict)
    example = projects["example"]
    assert isinstance(example, dict)
    assert example["package-id"] == package_id


def test_collect_dotnet_metadata_rejects_over_length_package_id() -> None:
    """Reject an explicit NuGet PackageId longer than 100 characters."""
    package_id = "A" * _OVER_LENGTH_NUGET_PACKAGE_ID_LENGTH
    scratch = REPO_ROOT / ".metadata-packaged-over-length-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id=package_id,
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, f"{package_id}\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "maximum length" in str(diagnostic["message"])
    details = diagnostic["details"]
    assert isinstance(details, dict)
    assert details["explicit-package-id"] == package_id
    assert details["actual-length"] == _OVER_LENGTH_NUGET_PACKAGE_ID_LENGTH
    assert details["max-length"] == _MAX_NUGET_PACKAGE_ID_LENGTH
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_over_length_evaluated_package_id() -> (
    None
):
    """Reject a final PackageId longer than 100 characters."""
    explicit_package_id = "A" * _MAX_NUGET_PACKAGE_ID_LENGTH
    evaluated_package_id = "A" * _OVER_LENGTH_NUGET_PACKAGE_ID_LENGTH
    scratch = REPO_ROOT / ".metadata-packageid-over-length-evaluated-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id=explicit_package_id,
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(
                    args, 0, f"{explicit_package_id}\n", ""
                )
            return subprocess.CompletedProcess(
                args, 0, f"{evaluated_package_id}\n", ""
            )

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "maximum length" in str(diagnostic["message"])
    details = diagnostic["details"]
    assert isinstance(details, dict)
    assert details["evaluated-package-id"] == evaluated_package_id
    assert details["actual-length"] == _OVER_LENGTH_NUGET_PACKAGE_ID_LENGTH
    assert details["max-length"] == _MAX_NUGET_PACKAGE_ID_LENGTH
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_invalid_package_id() -> None:
    """Reject PackageId values that violate NuGet NU1017 format."""
    scratch = REPO_ROOT / ".metadata-packaged-invalid-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Bad/Name",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            return subprocess.CompletedProcess(args, 0, "Bad/Name\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "NuGet package ID format" in str(diagnostic["message"])
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_invalid_evaluated_package_id() -> None:
    """Reject invalid final PackageId evaluation before accepting metadata."""
    scratch = REPO_ROOT / ".metadata-packageid-invalid-evaluated-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Good.Name",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(args, 0, "Good.Name\n", "")
            return subprocess.CompletedProcess(args, 0, "Bad/Name\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "NuGet package ID format" in str(diagnostic["message"])
    details = diagnostic["details"]
    assert isinstance(details, dict)
    assert details["evaluated-package-id"] == "Bad/Name"
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_inactive_package_id_fallback() -> None:
    """Reject PackageId text whose MSBuild condition is inactive."""
    scratch = REPO_ROOT / ".metadata-packageid-inactive-condition-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Inactive.Example",
            package_id_condition="'false' == 'true'",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(args, 0, "\n", "")
            return subprocess.CompletedProcess(args, 0, "Example\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "SDK fallback" in str(diagnostic["message"])
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_package_id_fallback() -> None:
    """Reject SDK fallback PackageId when no PackageId is authored."""
    scratch = REPO_ROOT / ".metadata-packaged-fallback-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id=None,
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(args, 0, "\n", "")
            return subprocess.CompletedProcess(args, 0, "Example\n", "")

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "SDK fallback" in str(diagnostic["message"])
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_accepts_case_only_package_id_difference() -> (
    None
):
    """Accept case-only differences while preserving evaluated PackageId."""
    scratch = REPO_ROOT / ".metadata-packaged-case-difference-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Explicit.Example",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(
                    args, 0, "Explicit.Example\n", ""
                )
            return subprocess.CompletedProcess(
                args, 0, "explicit.example\n", ""
            )

        document = collect_dotnet_metadata(
            metadata_input,
            REPO_ROOT,
            runner=runner,
        )
    finally:
        _remove_tree_scratch(scratch)

    validate_contract(document, metadata_input=metadata_input)
    projects = document["projects"]
    assert isinstance(projects, dict)
    example = projects["example"]
    assert isinstance(example, dict)
    assert example["package-id"] == "explicit.example"


def test_collect_dotnet_metadata_rejects_mismatched_package_id() -> None:
    """Reject when active pre-fallback PackageId differs from final value."""
    scratch = REPO_ROOT / ".metadata-packaged-mismatch-test"
    _remove_tree_scratch(scratch)
    scratch.mkdir()
    try:
        metadata_input = _metadata_input_with_manifests(
            scratch,
            package_id="Explicit.Example",
        )

        def runner(
            args: Sequence[str],
            _cwd: Path,
        ) -> subprocess.CompletedProcess[str]:
            if _is_nbgv_call(args):
                return subprocess.CompletedProcess(
                    args,
                    0,
                    json.dumps({"SemVer2": "1.2.3"}),
                    "",
                )
            if "-p:ImportNuGetBuildTasksPackTargetsFromSdk=false" in args:
                return subprocess.CompletedProcess(
                    args, 0, "Explicit.Example\n", ""
                )
            return subprocess.CompletedProcess(
                args, 0, "Fallback.Example\n", ""
            )

        with pytest.raises(DotnetMetadataError) as error:
            collect_dotnet_metadata(
                metadata_input,
                REPO_ROOT,
                runner=runner,
            )
    finally:
        _remove_tree_scratch(scratch)

    diagnostic = error.value.diagnostics[0]
    assert diagnostic["code"] == "DOTNET_METADATA_FAILED"
    assert diagnostic["project-id"] == "example"
    assert "does not match" in str(diagnostic["message"])
    validate_contract(error.value.document())


def test_collect_dotnet_metadata_rejects_valid_non_metadata_contract() -> None:
    """Reject other valid contract kinds with closed diagnostics."""
    planner_request = _load(CONTRACT_FIXTURES / "planner-request.json")
    validate_contract(planner_request)

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(planner_request, REPO_ROOT)

    _assert_input_diagnostic(error.value)
    assert "dotnet-planner-metadata-input" in str(
        error.value.diagnostics[0]["message"]
    )


def test_collect_dotnet_metadata_rejects_non_object_input() -> None:
    """Reject top-level non-object metadata input with closed diagnostics."""
    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata([], REPO_ROOT)

    _assert_input_diagnostic(error.value)
    assert "JSON object" in str(error.value.diagnostics[0]["message"])


def test_collect_dotnet_metadata_rejects_wrong_input_api_version() -> None:
    """Reject wrong metadata-input API versions before field access."""
    metadata_input = _metadata_input()
    metadata_input["api-version"] = "three.release.invalid/v1alpha1"

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(metadata_input, REPO_ROOT)

    _assert_input_diagnostic(error.value)


def test_collect_dotnet_metadata_rejects_wrong_input_shape() -> None:
    """Reject malformed metadata-input shape with closed diagnostics."""
    metadata_input = _metadata_input()
    del metadata_input["projects"]

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(metadata_input, REPO_ROOT)

    _assert_input_diagnostic(error.value)
    details = error.value.diagnostics[0]["details"]
    assert isinstance(details, dict)
    assert "projects" in str(details["issues"])


def test_collect_dotnet_metadata_fails_closed_on_invalid_nbgv_json() -> None:
    """Reject NBGV output that cannot provide SemVer2."""

    def runner(
        args: Sequence[str],
        _cwd: Path,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(args, 0, "{}", "")

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(
            _metadata_input(),
            REPO_ROOT,
            runner=runner,
        )

    assert {
        diagnostic["project-id"] for diagnostic in error.value.diagnostics
    } == {"app", "example"}


def test_collect_dotnet_metadata_rejects_unsafe_input_paths() -> None:
    """Do not let the helper use non-normalized paths from input JSON."""
    metadata_input = _metadata_input()
    projects = metadata_input["projects"]
    assert isinstance(projects, dict)
    example = projects["example"]
    assert isinstance(example, dict)
    example["primary-manifest-path"] = "../Example.csproj"

    def runner(
        args: Sequence[str],
        _cwd: Path,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(
            args,
            0,
            json.dumps({"SemVer2": "1.2.3"}),
            "",
        )

    with pytest.raises(DotnetMetadataError) as error:
        collect_dotnet_metadata(metadata_input, REPO_ROOT, runner=runner)

    assert error.value.diagnostics[0]["project-id"] == "example"
    assert "normalized repo-relative path" in str(
        error.value.diagnostics[0]["message"]
    )


def test_cli_writes_diagnostics_on_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """CLI failures are surfaced as closed diagnostics JSON."""
    scratch = REPO_ROOT / ".metadata-cli-test"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir()
        input_path = scratch / "input.json"
        output_path = scratch / "metadata.json"
        diagnostics_path = scratch / "diagnostics.json"
        input_path.write_text(json.dumps(_metadata_input()), encoding="utf-8")

        def fail_collect(
            _metadata_input: dict[str, Any],
            _repo_root: Path,
        ) -> dict[str, object]:
            raise DotnetMetadataError(
                [
                    {
                        "api-version": (
                            "three.release.planner-diagnostic/v1alpha1"
                        ),
                        "kind": "planner-diagnostic",
                        "code": "DOTNET_METADATA_FAILED",
                        "message": "metadata failed",
                        "phase": "normalization",
                        "scope-kind": "project",
                        "blocking": True,
                        "project-id": "example",
                        "details": {},
                    }
                ]
            )

        monkeypatch.setattr(
            "three_workflow_release_metadata.cli.collect_dotnet_metadata",
            fail_collect,
        )
        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-metadata",
            "dotnet",
            "--repo-root",
            str(REPO_ROOT),
            "--input",
            str(input_path),
            "--output",
            str(output_path),
            "--diagnostics-out",
            str(diagnostics_path),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        assert not output_path.exists()
        diagnostics = _load(diagnostics_path)
        validate_contract(diagnostics)
    finally:
        _remove_flat_scratch(scratch)


def test_cli_writes_diagnostics_for_valid_non_metadata_contract() -> None:
    """CLI turns wrong-kind input into diagnostics instead of crashing."""
    scratch = REPO_ROOT / ".metadata-cli-wrong-kind-test"
    try:
        _remove_flat_scratch(scratch)
        scratch.mkdir()
        input_path = scratch / "input.json"
        output_path = scratch / "metadata.json"
        diagnostics_path = scratch / "diagnostics.json"
        input_path.write_text(
            json.dumps(_load(CONTRACT_FIXTURES / "planner-request.json")),
            encoding="utf-8",
        )

        old_argv = sys.argv
        sys.argv = [
            "three-workflow-release-metadata",
            "dotnet",
            "--repo-root",
            str(REPO_ROOT),
            "--input",
            str(input_path),
            "--output",
            str(output_path),
            "--diagnostics-out",
            str(diagnostics_path),
        ]
        try:
            assert cli_main() == 1
        finally:
            sys.argv = old_argv
        assert not output_path.exists()
        diagnostics = _load(diagnostics_path)
        validate_contract(diagnostics)
        assert diagnostics["diagnostics"][0]["code"] == (
            "DOTNET_METADATA_FAILED"
        )
    finally:
        _remove_flat_scratch(scratch)


def test_cli_writes_diagnostics_for_malformed_json() -> None:
    """CLI turns malformed JSON input into closed diagnostics."""
    diagnostics = _run_cli_with_input_text(
        ".metadata-cli-malformed-json-test",
        "{",
    )

    validate_contract(diagnostics)
    assert diagnostics["diagnostics"][0]["code"] == "DOTNET_METADATA_FAILED"
    assert "valid JSON" in str(diagnostics["diagnostics"][0]["message"])


def test_cli_writes_diagnostics_for_non_object_json() -> None:
    """CLI turns top-level array input into closed diagnostics."""
    diagnostics = _run_cli_with_input_text(
        ".metadata-cli-non-object-test",
        "[]",
    )

    validate_contract(diagnostics)
    assert diagnostics["diagnostics"][0]["code"] == "DOTNET_METADATA_FAILED"
    assert "JSON object" in str(diagnostics["diagnostics"][0]["message"])


def test_cli_writes_diagnostics_for_missing_input_file() -> None:
    """CLI turns missing input files into closed diagnostics."""
    diagnostics = _run_cli_with_input_path(
        ".metadata-cli-missing-input-test",
        "missing.json",
    )

    validate_contract(diagnostics)
    assert diagnostics["diagnostics"][0]["code"] == "DOTNET_METADATA_FAILED"
    assert "could not be read" in str(diagnostics["diagnostics"][0]["message"])


def test_cli_writes_diagnostics_for_invalid_utf8_input() -> None:
    """CLI turns text decode failures into closed diagnostics."""
    diagnostics = _run_cli_with_input_bytes(
        ".metadata-cli-invalid-utf8-test",
        b"\xff",
    )

    validate_contract(diagnostics)
    assert diagnostics["diagnostics"][0]["code"] == "DOTNET_METADATA_FAILED"
    assert "valid UTF-8" in str(diagnostics["diagnostics"][0]["message"])


def _run_cli_with_input_text(scratch_name: str, content: str) -> dict[str, Any]:
    """Run the CLI with raw input text and return diagnostics."""
    scratch = REPO_ROOT / scratch_name
    _remove_flat_scratch(scratch)
    scratch.mkdir()
    try:
        input_path = scratch / "input.json"
        input_path.write_text(content, encoding="utf-8")
        diagnostics = _run_cli_for_scratch(scratch, input_path)
    finally:
        _remove_flat_scratch(scratch)
    return diagnostics


def _run_cli_with_input_bytes(
    scratch_name: str,
    content: bytes,
) -> dict[str, Any]:
    """Run the CLI with raw input bytes and return diagnostics."""
    scratch = REPO_ROOT / scratch_name
    _remove_flat_scratch(scratch)
    scratch.mkdir()
    try:
        input_path = scratch / "input.json"
        input_path.write_bytes(content)
        diagnostics = _run_cli_for_scratch(scratch, input_path)
    finally:
        _remove_flat_scratch(scratch)
    return diagnostics


def _run_cli_with_input_path(
    scratch_name: str,
    input_name: str,
) -> dict[str, Any]:
    """Run the CLI with an input path and return diagnostics."""
    scratch = REPO_ROOT / scratch_name
    _remove_flat_scratch(scratch)
    scratch.mkdir()
    try:
        diagnostics = _run_cli_for_scratch(scratch, scratch / input_name)
    finally:
        _remove_flat_scratch(scratch)
    return diagnostics


def _run_cli_for_scratch(scratch: Path, input_path: Path) -> dict[str, Any]:
    """Run the CLI in one scratch directory and return diagnostics."""
    output_path = scratch / "metadata.json"
    diagnostics_path = scratch / "diagnostics.json"
    old_argv = sys.argv
    sys.argv = [
        "three-workflow-release-metadata",
        "dotnet",
        "--repo-root",
        str(REPO_ROOT),
        "--input",
        str(input_path),
        "--output",
        str(output_path),
        "--diagnostics-out",
        str(diagnostics_path),
    ]
    try:
        assert cli_main() == 1
    finally:
        sys.argv = old_argv
    assert not output_path.exists()
    return _load(diagnostics_path)


def _remove_flat_scratch(path: Path) -> None:
    """Remove a flat scratch directory."""
    if not path.exists():
        return
    for child in path.iterdir():
        child.unlink()
    path.rmdir()


def _remove_tree_scratch(path: Path) -> None:
    """Remove a scratch directory tree."""
    if not path.exists():
        return
    for child in sorted(path.rglob("*"), reverse=True):
        if child.is_file():
            child.unlink()
        else:
            child.rmdir()
    path.rmdir()
