"""Concrete Python Release inputs and immutable job transfer bindings."""

from __future__ import annotations

import json
import os
import shutil
import sys
from pathlib import Path

UNIT = "hcoona-release-smoke-python"
DECLARATION = (
    "src/public/lib/hcoona-release-smoke-python/"
    "workflow-delivery.release-unit.yml"
)


def read(path: str | Path) -> dict:
    """Read one typed CLI or producer transfer."""
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write(path: str | Path, value: dict) -> None:
    """Write actual concrete job inputs as JSON."""
    Path(path).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def subject() -> dict:
    """Select the concrete request from the native workflow context."""
    destination = os.environ["DESTINATION"]
    if destination not in ("testpypi", "pypi"):
        message = "Unsupported Python destination"
        raise ValueError(message)
    return {
        "revision": os.environ["GITHUB_SHA"],
        "selectedRef": os.environ["GITHUB_REF"],
        "unit": UNIT,
        "declaration": DECLARATION,
        "channel": "buddy" if destination == "testpypi" else "official",
        "destination": destination,
        "dryRun": os.environ["DRY_RUN"] == "true",
    }


def producer() -> dict:
    """Bind an actual job transfer to the current request and native run."""
    return {
        "subject": subject(),
        "run": os.environ["GITHUB_RUN_ID"],
        "attempt": os.environ["GITHUB_RUN_ATTEMPT"],
    }


def _request(root: Path) -> None:
    control = root / "control"
    prepared = root / "prepared"
    if os.environ["GITHUB_REPOSITORY"] != "hcoona/three":
        message = "Unsupported Python repository"
        raise ValueError(message)
    if not subject()["dryRun"] and os.environ["GITHUB_RUN_ATTEMPT"] != "1":
        message = "Live rerun is unavailable"
        raise ValueError(message)
    prepared.mkdir()
    (root / "preparation-scratch").mkdir()
    write(control / "producer.json", producer())
    write(prepared / "producer.json", producer())
    write(
        prepared / "request.json",
        {
            "subject": subject(),
            "checkout": os.environ["GITHUB_WORKSPACE"],
            "scratch": str(root / "preparation-scratch"),
            "miseDataDirectory": os.environ["MISE_DATA_DIR"],
        },
    )


def _originals(root: Path) -> None:
    prepared = root / "prepared"
    preparation = read(prepared / "preparation.json")
    if preparation["prepared"]["subject"] != subject():
        message = "Preparation subject mismatch"
        raise ValueError(message)
    scratch = (root / "preparation-scratch").resolve()
    for original in preparation["prepared"]["files"]:
        relative = Path(original["path"])
        source = (scratch / relative).resolve()
        if relative.is_absolute() or not source.is_relative_to(scratch):
            message = "Original transfer leaves preparation"
            raise ValueError(message)
        target = prepared / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)


def _validate(root: Path, args: list[str]) -> None:
    prepared = root / "prepared"
    for kind in args[1:]:
        if read(root / kind / "producer.json") != producer():
            message = "Transferred producer mismatch"
            raise ValueError(message)
    if read(prepared / "preparation.json")["prepared"]["subject"] != subject():
        message = "Transferred Python subject mismatch"
        raise ValueError(message)


def _phase(root: Path, args: list[str]) -> None:
    phase = root / args[1]
    phase.mkdir()
    write(phase / "producer.json", producer())


def _tool_request(root: Path) -> None:
    control = root / "control"
    (root / "index-tool-scratch").mkdir()
    write(
        root / "index-tool-request.json",
        {
            "uv": os.environ["UV_EXECUTABLE"],
            "interpreter": os.environ["PYTHON_EXECUTABLE"],
            "requirements": str(control / "index-requirements.txt"),
            "scratch": str(root / "index-tool-scratch"),
        },
    )


def _index_config(root: Path, args: list[str]) -> None:
    control = root / "control"
    scratch = root / args[1]
    scratch.mkdir()
    write(
        root / "index-tools.json",
        {
            "interpreter": read(root / "index-tool-result.json")["interpreter"],
            "helper": str(control / "packages.py"),
            "scratch": str(scratch),
        },
    )


def _publisher_config(root: Path) -> None:
    (root / "publisher-scratch").mkdir()
    write(
        root / "publisher-tools.json",
        {
            "uv": os.environ["UV_EXECUTABLE"],
            "scratch": str(root / "publisher-scratch"),
        },
    )


def _consumer_config(root: Path, args: list[str]) -> None:
    control = root / "control"
    kind = args[1]
    if kind not in ("python-wheel", "python-sdist"):
        message = "Unsupported Python format"
        raise ValueError(message)
    scratch = root / (kind + "-scratch")
    scratch.mkdir()
    write(
        root / (kind + "-tools.json"),
        {
            "checkout": os.environ["GITHUB_WORKSPACE"],
            "scratch": str(scratch),
            "uv": os.environ["UV_EXECUTABLE"],
            "interpreter": os.environ["PYTHON_EXECUTABLE"],
            "helper": str(control / "packages.py"),
            "deadlineSeconds": 180,
            "environment": {
                name: os.environ[name]
                for name in ("PATH", "DOTNET_ROOT")
                if name in os.environ
            },
            "inheritEnvironment": False,
        },
    )


def _state(root: Path) -> None:
    observation = read(root / "observation/observation.json")["observation"]
    if observation["subject"] != subject():
        message = "Observation subject mismatch"
        raise ValueError(message)
    needed = any(item["state"] == "absent" for item in observation["files"])
    with Path(os.environ["GITHUB_OUTPUT"]).open(
        "a", encoding="utf-8"
    ) as output:
        output.write("write-needed=" + str(needed).lower() + "\n")


def main(args: list[str]) -> None:
    """Execute a supported concrete caller input/transfer operation."""
    root = Path(os.environ["RUNNER_TEMP"])
    match args:
        case ["request"]:
            _request(root)
        case ["originals"]:
            _originals(root)
        case ["validate", *kinds] if kinds:
            _validate(root, args)
        case ["phase", _]:
            _phase(root, args)
        case ["tool-request"]:
            _tool_request(root)
        case ["index-config", _]:
            _index_config(root, args)
        case ["publisher-config"]:
            _publisher_config(root)
        case ["consumer-config", _]:
            _consumer_config(root, args)
        case ["state"]:
            _state(root)
        case _:
            message = "Unsupported ordinary Python caller operation"
            raise ValueError(message)


if __name__ == "__main__":
    main(sys.argv[1:])
