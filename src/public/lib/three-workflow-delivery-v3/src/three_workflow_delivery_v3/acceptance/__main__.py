"""Module entry point for acceptance-only probes and the local operator."""

import sys

from three_workflow_delivery_v3.acceptance.npm_operator import (
    main as suite_main,
)
from three_workflow_delivery_v3.acceptance.npm_probe import main as probe_main


def main() -> int:
    """Keep the existing probe CLI unchanged; route only the suite command."""
    if sys.argv[1:2] == ["suite"]:
        return suite_main(sys.argv[1:])
    return probe_main(sys.argv[1:])


if __name__ == "__main__":
    raise SystemExit(main())
