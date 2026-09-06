"""Shared runtime mechanics for the pinned standard npm publish profile."""

# ruff: noqa: EM101, TRY003

from __future__ import annotations

import os
import stat
from typing import TYPE_CHECKING

from three_workflow_delivery_v3.adapters.github_packages import (
    github_packages_destination_operation_profile,
)
from three_workflow_delivery_v3.records.release import ProfileMatchEvidence

if TYPE_CHECKING:
    from collections.abc import Callable
    from datetime import datetime
    from pathlib import Path

    from three_workflow_delivery_v3.adapters.npm_process import NpmProcessRunner

NPM_QUERY_TIMEOUT = 20.0
NPM_PUBLISH_TIMEOUT = 120.0
NPM_OUTPUT_LIMIT = 4096
_PRIVATE_DIRECTORY_MODE = 0o700
_PRIVATE_FILE_MODE = 0o600
_USER_CONFIG = (
    "@hcoona:registry=https://npm.pkg.github.com\n"
    "//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}\n"
)
_LOCAL_MANIFEST = b'{"private":true}\n'


class NpmProfileRejectionError(ValueError):
    """Controlled local artifact or nonmutating profile-query rejection."""


def npm_runtime_paths(
    directory: Path, toolchain: Path, checkout: Path
) -> tuple[Path, Path]:
    """Resolve the isolated runtime and toolchain outside the checkout."""
    directory = directory.absolute()
    toolchain = toolchain.resolve(strict=True)
    if (
        directory != directory.resolve()
        or directory.is_relative_to(checkout.resolve())
        or toolchain.is_relative_to(checkout.resolve())
    ):
        raise ValueError(
            "Publisher runtime and toolchain must be outside checkout"
        )
    return directory, toolchain


def npm_environment(
    directory: Path, toolchain: Path, token: str
) -> dict[str, str]:
    """Supply only the fixed runtime paths and injected repository token."""
    if type(token) is not str or not token or any(c in token for c in "\r\n\0"):
        raise ValueError("Publisher requires a current repository GITHUB_TOKEN")
    # Nothing from ambient configuration, including NODE_OPTIONS, is inherited.
    return {
        "PATH": os.pathsep.join((str(toolchain), os.defpath)),
        "HOME": str(directory / "home"),
        "TMPDIR": str(directory / "scratch"),
        "GITHUB_TOKEN": token,
        "NPM_CONFIG_USERCONFIG": str(directory / "user.npmrc"),
        "NPM_CONFIG_GLOBALCONFIG": str(directory / "global.npmrc"),
        "NPM_CONFIG_CACHE": str(directory / "cache"),
        "NPM_CONFIG_LOGS_MAX": "0",
    }


def write_private_file(path: Path, content: bytes) -> None:
    """Exclusively create a runner-private file without replacing a claim."""
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(content)


def initialize_npm_configuration(directory: Path, tarball: Path) -> None:
    """Populate a caller-owned fresh directory without owning its cleanup."""
    for name in ("home", "scratch", "cache"):
        (directory / name).mkdir(mode=0o700)
    write_private_file(directory / "package.json", _LOCAL_MANIFEST)
    write_private_file(directory / "user.npmrc", _USER_CONFIG.encode())
    write_private_file(directory / "global.npmrc", b"")
    write_private_file(directory / tarball.name, tarball.read_bytes())


def validate_npm_runtime(directory: Path) -> None:
    """Require the unchanged private configuration prepared for npm."""
    if (
        not stat.S_ISDIR(directory.lstat().st_mode)
        or stat.S_IMODE(directory.stat().st_mode) != _PRIVATE_DIRECTORY_MODE
    ):
        raise ValueError("Publisher runtime is not runner-private")
    for name, expected in (
        ("package.json", _LOCAL_MANIFEST),
        ("user.npmrc", _USER_CONFIG.encode()),
        ("global.npmrc", b""),
    ):
        path = directory / name
        if (
            not stat.S_ISREG(path.lstat().st_mode)
            or stat.S_IMODE(path.stat().st_mode) != _PRIVATE_FILE_MODE
            or path.read_bytes() != expected
        ):
            raise ValueError("Publisher prepared configuration changed")
    if (directory / ".npmrc").exists():
        raise ValueError("Publisher project configuration is forbidden")


def match_npm_profile(  # noqa: PLR0913
    *,
    tarball: Path,
    tag: str,
    directory: Path,
    toolchain: Path,
    token: str,
    runner: NpmProcessRunner,
    clock: Callable[[], datetime],
) -> ProfileMatchEvidence:
    """Query the pinned profile with caller-validated runtime and operands."""
    profile = github_packages_destination_operation_profile()
    command = tuple(
        {"{tarball-path}": str(tarball), "{tag}": tag}.get(word, word)
        for word in profile.command_template
    )
    environment = npm_environment(directory, toolchain, token)

    def query(argv: tuple[str, ...]) -> str:
        outcome = runner.run(
            argv,
            cwd=directory,
            environment=environment,
            timeout=NPM_QUERY_TIMEOUT,
            output_limit=NPM_OUTPUT_LIMIT,
        )
        if outcome.classification != "definitive-success" or outcome.truncated:
            raise NpmProfileRejectionError(
                "Publisher toolchain/configuration query failed"
            )
        try:
            return outcome.output.decode("utf-8").strip()
        except UnicodeError:
            raise NpmProfileRejectionError(
                "Publisher toolchain/configuration is not UTF-8"
            ) from None

    node = query(("node", "--version"))
    npm = query(("npm", "--version"))
    if node != "v" + profile.node_version or npm != profile.npm_version:
        raise NpmProfileRejectionError("Publisher pinned toolchain mismatch")
    # The trusted manifest anchors npm's documented local-prefix discovery.
    # User/global config paths are fixed by the exact environment and checked
    # files, not npm's intentionally redacted textual rendering of paths.
    expected = {
        "@hcoona:registry": profile.registry,
        # npm's URL-typed registry config serializes the root slash.
        "registry": profile.registry + "/",
        "tag": tag,
        "ignore-scripts": "true",
        "fetch-retries": "0",
        "access": "null",
    }
    actual = {}
    for key, value in expected.items():
        observed = query(("npm", "config", "get", key, *command[3:]))
        if observed != value:
            raise NpmProfileRejectionError(
                "Publisher effective npm configuration mismatch"
            )
        actual[key] = observed
    now = clock()
    if now.tzinfo is None or now.utcoffset() is None:
        raise ValueError("Publisher clock must be timezone-aware")
    return ProfileMatchEvidence(
        destination_operation_profile_digest=profile.profile_digest,
        node_version=node.removeprefix("v"),
        npm_version=npm,
        command=command,
        configuration=tuple(sorted(actual.items())),
        matched_at=now.isoformat().replace("+00:00", "Z"),
    )
