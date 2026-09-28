"""Repository spelling-check exclusions with current consumers."""

import tomllib
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def test_typos_pngcs_exclusions_remain_bounded() -> None:
    """Keep imported-file exclusions bounded and test artifacts visible."""
    typos_config = tomllib.loads(
        (REPO_ROOT / ".typos.toml").read_text(encoding="utf-8")
    )
    exclusions = typos_config["files"]["extend-exclude"]
    pngcs_exclusions = tuple(
        path for path in exclusions if "src/public/lib/Hjg.Pngcs/" in path
    )

    assert all("*" not in path and "?" not in path for path in pngcs_exclusions)
    assert ".testagent/**" not in exclusions
