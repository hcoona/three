"""Immutable native fact admission contracts for the NuGet slice."""

import os
from dataclasses import replace

import pytest
from three_workflow_delivery_v3.adapters.dotnet import (
    BUILD_DEFINITION,
    DotnetPackageTargetWitness,
    dotnet_package_target_witness_from_document,
)
from three_workflow_delivery_v3.canonical import JsonValue
from three_workflow_delivery_v3.repository.dotnet_provider import (
    DOTNET_RELEASE_UNIT,
    DotnetNbgvFacts,
    dotnet_nbgv_facts_from_document,
    neutral_dotnet_environment,
    validate_dotnet_nbgv_facts,
)


def _facts() -> DotnetNbgvFacts:
    return DotnetNbgvFacts(
        "1.2.3",
        "1.2.3-beta-0007",
        "1.2.3-beta.7",
        7,
        "a" * 40,
        False,  # noqa: FBT003
        "1.2.3-beta.7",
        "1.2.0.0",
        "1.2.3.0",
        "1.2.3-beta.7+abc",
        "sha256:" + "b" * 64,
    )


def test_dotnet_facts_and_witness_reject_substitution() -> None:
    """Native facts reject target and ecosystem substitutions."""
    facts = _facts()
    assert dotnet_nbgv_facts_from_document(facts.to_document()) == facts
    with pytest.raises(ValueError, match="target binding"):
        validate_dotnet_nbgv_facts(facts, target="c" * 40)
    witness = DotnetPackageTargetWitness(
        facts.git_commit_id,
        DOTNET_RELEASE_UNIT,
        facts,
        BUILD_DEFINITION,
        "sha256:" + "c" * 64,
        "sha256:" + "d" * 64,
        "release-simulation",
    )
    assert (
        dotnet_package_target_witness_from_document(witness.to_document())
        == witness
    )
    substituted = witness.to_document()
    substituted["release-unit"] = "hcoona-release-smoke-npm"
    with pytest.raises(ValueError, match="unsupported NuGet"):
        dotnet_package_target_witness_from_document(substituted)
    with pytest.raises(ValueError, match="unsupported NuGet"):
        _ = replace(witness, purpose="borrowed-approval").canonical_bytes
    native = facts.to_document()
    native["native"] = {"npmPackageVersion": facts.nuget_package_version}
    with pytest.raises(ValueError, match="schema"):
        dotnet_nbgv_facts_from_document(native)


@pytest.mark.parametrize("height", [True, -1, 2.0, "2"])
def test_dotnet_facts_reject_noncanonical_height(height: JsonValue) -> None:
    """Numeric equality cannot substitute a malformed native fact."""
    document = _facts().to_document()
    canonical = document["canonical"]
    assert isinstance(canonical, dict)
    canonical["versionHeight"] = height
    with pytest.raises(ValueError, match="canonical NBGV"):
        dotnet_nbgv_facts_from_document(document)


def test_native_environment_discards_authority_and_ambient_ref(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Target execution discards write credentials and CI ref selection."""
    for key in (
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "GITHUB_REF",
        "GITHUB_SHA",
        "CI",
        "NUGET_AUTH_TOKEN",
        "NuGetPackageVersion",
    ):
        monkeypatch.setenv(key, "must-not-survive")
    monkeypatch.setenv("PATH", "/controlled/tools")
    environment = neutral_dotnet_environment()
    assert environment["PATH"] == "/controlled/tools"
    assert environment["IGNORE_GITHUB_REF"] == "true"
    assert "must-not-survive" not in environment.values()


@pytest.mark.parametrize(
    "roots",
    [
        {
            "ProgramFiles(x86)": r"C:\Program Files (x86)",
            "ProgramFiles": r"C:\Program Files",
        },
        {
            "PROGRAMFILES(X86)": r"C:\Program Files (x86)",
            "PROGRAMFILES": r"C:\Program Files",
        },
        {"programfiles": r"C:\Program Files"},
        {"ProgramFiles(x86)": "", "ProgramFiles": r"C:\Program Files"},
    ],
    ids=["mixed-case", "uppercase", "fallback-only", "empty-x86"],
)
def test_native_environment_preserves_windows_nuget_settings_roots(
    monkeypatch: pytest.MonkeyPatch, roots: dict[str, str]
) -> None:
    """NuGet's Windows settings roots survive the credential-free boundary."""
    directory_keys = {"PROGRAMFILES(X86)", "PROGRAMFILES"}
    for key in tuple(os.environ):
        if key.upper() in directory_keys:
            monkeypatch.delenv(key)
    for key, value in roots.items():
        monkeypatch.setenv(key, value)
    monkeypatch.setenv("GITHUB_TOKEN", "parent-only-token")
    monkeypatch.setenv("GITHUB_REF", "refs/heads/ambient")
    monkeypatch.setenv("IGNORE_GITHUB_REF", "false")
    parent_values = (
        os.environ["GITHUB_TOKEN"],
        os.environ["IGNORE_GITHUB_REF"],
    )

    environment = neutral_dotnet_environment()

    assert {
        key.upper(): value
        for key, value in environment.items()
        if key.upper() in directory_keys
    } == {key.upper(): value for key, value in roots.items()}
    assert "GITHUB_TOKEN" not in environment
    assert "GITHUB_REF" not in environment
    assert environment["IGNORE_GITHUB_REF"] == "true"
    assert (
        os.environ["GITHUB_TOKEN"],
        os.environ["IGNORE_GITHUB_REF"],
    ) == parent_values
