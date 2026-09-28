"""Synthetic archived supplied facts for retired native readers only.

Captured from local substitutes at the revision in provenance.json. No fixture
is real registry evidence or permission to execute the retired protocol.
"""

import gzip
import io
import zipfile
from pathlib import Path

from three_workflow_delivery_v3.canonical import parse_json_strict

_FIXTURES = (
    Path(__file__).resolve().parents[1]
    / "fixtures/acceptance/python-native-history"
)


def historical_files(group):
    """Return fresh original bytes without running retired producers."""
    prefix = group + "/"
    with zipfile.ZipFile(
        io.BytesIO(
            gzip.decompress((_FIXTURES / "synthetic.zip.gz").read_bytes())
        )
    ) as archive:
        return {
            name.removeprefix(prefix): archive.read(name)
            for name in archive.namelist()
            if name.startswith(prefix) and name != prefix + "manifest.json"
        }


def historical_reference():
    """Read the original synthetic artifact binding."""
    with zipfile.ZipFile(
        io.BytesIO(
            gzip.decompress((_FIXTURES / "synthetic.zip.gz").read_bytes())
        )
    ) as archive:
        return parse_json_strict(archive.read("reference.json"))


def historical_catalog_digest():
    """Return the pinned original revision's catalog dependency."""
    return parse_json_strict((_FIXTURES / "provenance.json").read_bytes())[
        "original-catalog-digest"
    ]
