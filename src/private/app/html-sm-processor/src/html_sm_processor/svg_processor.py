"""This module provides functions to convert SVG data to PNG format."""

import base64
import logging
import pathlib
import urllib.parse

import cairosvg
import niquests as requests

from .constants import _DEFAULT_DPI


def convert_svg_base64(data_base64: str) -> bytes | None:
    """Convert base64 encoded SVG data to PNG bytes."""
    try:
        svg_bytes = base64.b64decode(data_base64)
        return cairosvg.svg2png(bytestring=svg_bytes, dpi=_DEFAULT_DPI)
    except Exception as e:
        logging.exception(f"Failed to convert base64 SVG to PNG: {e}")  # noqa: G004, TRY401
        return None


def convert_svg_relative_path(
    root_path: str, relative_path: str
) -> bytes | None:
    """Convert SVG file at a relative path to PNG bytes.

    1. If the root_path is an URL, download it via requests.
    2. If the root_path is a local file, read it directly.
    3. Convert the SVG to PNG using cairosvg.
    4. Return the PNG bytes.
    """
    if root_path.startswith("http://") or root_path.startswith("https://"):  # noqa: PIE810
        if relative_path.startswith("http://") or relative_path.startswith(  # noqa: PIE810
            "https://"
        ):
            response = requests.get(relative_path)
            response.raise_for_status()
        else:
            response = requests.get(
                urllib.parse.urljoin(root_path, relative_path)
            )
            response.raise_for_status()

        svg_bytes = response.content
        if svg_bytes is None:
            return None
    elif relative_path.startswith("/"):
        svg_bytes = pathlib.Path(relative_path).read_bytes()
    else:
        with open(pathlib.Path(root_path) / relative_path, mode="rb") as f:  # noqa: PTH123
            svg_bytes = f.read()

    return cairosvg.svg2png(bytestring=svg_bytes, dpi=_DEFAULT_DPI)
