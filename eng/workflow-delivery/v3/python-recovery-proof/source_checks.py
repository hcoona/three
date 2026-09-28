# Runtime assertions are mandatory; campaign import rejects optimized Python.
# Native records and operator JSON retain their dynamic external schema.
# Literal IDs, URLs and limits mirror the accepted fixed protocol.
# Only fixed Git/gh argv invoke already admitted local tools.
# ruff: noqa: ANN401, E501, S101, S603, S607

"""Pin the local reader and checkout to the admitted hosted source."""

import importlib.util
import subprocess
from pathlib import Path
from typing import Any

CHECKOUT = Path("/workspace/three-workspaces/design-workflows")


def verify(target: Any, tree: Any) -> None:
    """Reject a changed checkout or a different installed reader."""

    def git(*args: Any) -> Any:
        """Validate the git caller boundary."""
        return subprocess.check_output(
            ["git", *args], cwd=CHECKOUT, text=True, timeout=30
        ).strip()

    assert git("rev-parse", "HEAD") == target
    assert git("rev-parse", "HEAD^{tree}") == tree
    assert not git("status", "--porcelain"), "local reader checkout changed"
    spec = importlib.util.find_spec("three_workflow_delivery_v3")
    expected = (
        CHECKOUT
        / "src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/__init__.py"
    )
    assert spec is not None
    assert spec.origin is not None
    assert Path(spec.origin).resolve() == expected.resolve()
