"""Tests for template field generation."""

from __future__ import annotations

import pytest
from nbgv_python.templating import (
    TemplateFieldsConfig,
    VersionTupleConfig,
    build_template_fields,
)


def test_build_template_fields_default() -> None:
    """Default configuration should expose core fields."""
    info = {
        "SimpleVersion": "1.2.3",
        "VersionMajor": 1,
        "VersionMinor": 2,
        "BuildNumber": 3,
        "PrereleaseVersionNoLeadingHyphen": "",
        "BuildMetadataFragment": "+abc123",
    }
    fields = build_template_fields(
        version="1.2.3",
        normalized_version="1.2.3",
        version_info=info,
    )
    assert fields["version"] == "1.2.3"
    assert fields["normalized_version"] == "1.2.3"
    assert fields["version_tuple"] == "(1, 2, 3)"
    assert fields["SimpleVersion"] == "1.2.3"


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("1.2.3a1", '(0, 1, 2, 3, "a1")'),
        ("1.2.3.post1", '(0, 1, 2, 3, "post", 1)'),
        ("1!1.2.3", "(1, 1, 2, 3)"),
    ],
)
def test_build_template_fields_pep440(value: str, expected: str) -> None:
    """PEP 440 tuples should include release metadata."""
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="pep440", epoch=True)
    )
    fields = build_template_fields(
        version=value,
        normalized_version=value,
        version_info={},
        config=config,
    )
    assert fields["version_tuple"] == expected


def test_build_template_fields_pep440_epoch_default_absent() -> None:
    """PEP 440 tuple omits epoch when not configured and value lacks one."""
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="pep440", epoch=None)
    )
    fields = build_template_fields(
        version="1.2.3",
        normalized_version="1.2.3",
        version_info={},
        config=config,
    )
    assert fields["version_tuple"] == "(1, 2, 3)"


def test_build_template_fields_nbgv_custom_fields() -> None:
    """Custom field lists should project NBGV metadata in order."""
    info = {
        "SemVer2": "1.2.3-beta.1+abc",
        "PrereleaseVersionNoLeadingHyphen": "beta.1",
        "VersionMajor": 1,
        "VersionMinor": 2,
        "BuildNumber": 99,
    }
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(
            mode="nbgv",
            fields=(
                "VersionMajor",
                "VersionMinor",
                "BuildNumber",
                "PrereleaseVersionNoLeadingHyphen",
            ),
        )
    )
    fields = build_template_fields(
        version="1.2.3-beta.1",
        normalized_version="1.2.3b1",
        version_info=info,
        config=config,
    )
    assert fields["version_tuple"] == '(1, 2, 99, "beta.1")'


def test_build_template_fields_nbgv_normalized_prerelease() -> None:
    """Normalized prerelease option should collapse beta.1 -> b1."""
    info = {
        "SemVer2": "1.2.3-beta.1+abc",
        "PrereleaseVersionNoLeadingHyphen": "beta.1",
        "VersionMajor": 1,
        "VersionMinor": 2,
        "BuildNumber": 99,
    }
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(
            mode="nbgv",
            fields=(
                "VersionMajor",
                "VersionMinor",
                "BuildNumber",
                "PrereleaseVersionNoLeadingHyphen",
            ),
            normalized_prerelease=True,
        )
    )
    fields = build_template_fields(
        version="1.2.3-beta.1",
        normalized_version="1.2.3b1",
        version_info=info,
        config=config,
    )
    assert fields["version_tuple"] == '(1, 2, 99, "b1")'


def test_build_template_fields_pep440_epoch_forced() -> None:
    """PEP 440 tuple includes epoch when forced even if zero."""
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="pep440", epoch=True)
    )
    fields = build_template_fields(
        version="1.2.3",
        normalized_version="1.2.3",
        version_info={},
        config=config,
    )
    assert fields["version_tuple"].startswith("(0, 1, 2, 3")


def test_build_template_fields_single_element_tuple() -> None:
    """Single element tuples should include a trailing comma."""
    info = {"VersionMajor": 1}
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="nbgv", fields=("VersionMajor",))
    )
    fields = build_template_fields(
        version="1.0.0",
        normalized_version="1.0.0",
        version_info=info,
        config=config,
    )
    assert fields["version_tuple"] == "(1,)"


def test_build_template_fields_nbgv_boolean_component() -> None:
    """Boolean metadata should render as Python boolean literals."""
    info = {
        "VersionMajor": True,
    }
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="nbgv", fields=("VersionMajor",))
    )
    fields = build_template_fields(
        version="1.0.0",
        normalized_version="1.0.0",
        version_info=info,
        config=config,
    )
    assert fields["version_tuple"] == "(True,)"


def test_build_template_fields_nbgv_boolean_component_false() -> None:
    """False should render as the unquoted literal 'False'."""
    info = {
        "VersionMajor": False,
    }
    config = TemplateFieldsConfig(
        version_tuple=VersionTupleConfig(mode="nbgv", fields=("VersionMajor",))
    )
    fields = build_template_fields(
        version="1.0.0",
        normalized_version="1.0.0",
        version_info=info,
        config=config,
    )
    assert fields["version_tuple"] == "(False,)"
