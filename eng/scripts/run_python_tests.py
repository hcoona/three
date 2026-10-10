"""Run the native Python group in explicit full mode at the committed HEAD."""

from __future__ import annotations

import argparse
import os
import sys
import tempfile
from pathlib import Path

import ci_scope
import run_dotnet_ci_group as dotnet
import run_python_ci_group as group


def main() -> int:
    """Retain exact source, the mixed plan and every required result."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=ci_scope.ROOT)
    parser.add_argument("--directory", type=Path)
    parser.add_argument("--mise-data-directory", type=Path)
    options = parser.parse_args()
    if sys.platform != "linux":
        parser.error("The current native Python group supports Linux execution")
    root = options.repository.resolve()
    if ci_scope.git(root, "diff", "HEAD", "--name-only").strip():
        parser.error("Commit tracked changes before testing the explicit HEAD")
    candidate = ci_scope.git(root, "rev-parse", "HEAD").strip()
    directory = (
        options.directory.resolve()
        if options.directory is not None
        else Path(tempfile.mkdtemp(prefix="three-python-full-")) / "state"
    )
    if directory.is_relative_to(root) or root.is_relative_to(directory):
        parser.error("Full-group state must be outside the source checkout")
    directory.mkdir(parents=True, exist_ok=False)
    print(
        f"Full Python group at commit {candidate}; results: {directory}",
        flush=True,
    )
    control = directory / "control-checkout"
    group.node.run(
        root,
        directory,
        "control-checkout",
        "git",
        "worktree",
        "add",
        "--detach",
        str(control),
        candidate,
    )
    dotnet.bootstrap(control, directory / "control-build")
    scope = directory / "scope.json"
    group.node.write_json(
        scope,
        {
            "base": candidate,
            "candidate": candidate,
            "full": True,
            "changed_paths": [],
            "endpoint_owners": {
                name: {"revision": candidate, "paths": []}
                for name in ("basis", "candidate")
            },
        },
    )
    state = directory / "group"
    group.materialize(root, scope, directory / "control-build/transfer", state)
    group.plan(root, state)
    # The native mise adapter retains the operator's installed tool data.
    data = options.mise_data_directory
    if data is None:
        data = Path(
            os.environ.get(
                "MISE_DATA_DIR",
                str(
                    Path(
                        os.environ.get(
                            "XDG_DATA_HOME", str(Path.home() / ".local/share")
                        )
                    )
                    / "mise"
                ),
            )
        )
    return 0 if group.execute(root, state, data.resolve())["satisfied"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
