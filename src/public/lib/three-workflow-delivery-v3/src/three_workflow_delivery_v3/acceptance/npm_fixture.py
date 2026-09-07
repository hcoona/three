"""Local native-suite fixture construction and independent byte inspection.

These acceptance-only witnesses are not qualified Release witnesses. Creating
a fixture neither approves its disposable package nor proves native acceptance.
No package access, registry operation, npm lifecycle or target code runs here.
"""

from __future__ import annotations

import gzip
import hashlib
import os
import re
import shutil
import subprocess
import tarfile
from dataclasses import dataclass
from typing import TYPE_CHECKING, Literal

from three_workflow_delivery_v3.acceptance.native_npm import ObservedContent
from three_workflow_delivery_v3.adapters.node import _read_tarball
from three_workflow_delivery_v3.canonical import (
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)

if TYPE_CHECKING:
    from pathlib import Path

    from three_workflow_delivery_v3.canonical import JsonValue

ACCEPTANCE_WITNESS_SCHEMA = "workflow-delivery-v3/native-npm-fixture/v1"
_MANIFEST_PATH = "package/package.json"
_WITNESS_PATH = "package/workflow-delivery/acceptance-witness.json"
_PARSER_BRIDGE = "eng/scripts/workflow_delivery_v3_native_npm.mjs"
_PARSER_TIMEOUT_SECONDS = 15
_MAX_PAYLOAD_BYTES = 64 * 1024
_WITNESS_FIELDS = {
    "schema",
    "package",
    "version",
    "target",
    "generation",
    "variant",
}


def _require(condition: bool, message: str) -> None:  # noqa: FBT001
    if not condition:
        raise ValueError(message)


def _string(value: JsonValue) -> str:
    if type(value) is not str:
        message = "fixture identity fields must be strings"
        raise TypeError(message)
    return value


def _variant(value: object) -> Literal["original", "different"]:
    if type(value) is str and value == "original":
        return "original"
    if type(value) is str and value == "different":
        return "different"
    message = "fixture variant must be original or different"
    raise ValueError(message)


@dataclass(frozen=True)
class NpmFixtureSpec:
    """Explicit local inputs; npm identity is checked officially during build.

    Generation is a 1-64 character ASCII identifier, starting alphanumeric.
    Target is a lowercase full commit SHA, compatible with a buddy-sha tag.
    The package has no default and requires separate approval before native use.
    """

    package: str
    version: str
    target: str
    generation: str
    variant: Literal["original", "different"] = "original"

    def __post_init__(self) -> None:
        """Reject malformed local bindings without inventing npm syntax."""
        for value in (
            self.package,
            self.version,
            self.target,
            self.generation,
        ):
            _string(value)
        _require(
            re.fullmatch(r"[0-9a-f]{40}", self.target) is not None,
            "fixture target must be a lowercase full commit SHA",
        )
        _require(
            re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", self.generation)
            is not None,
            "fixture generation must be a bounded safe identifier",
        )
        _variant(self.variant)


@dataclass(frozen=True)
class NpmFixture:
    """Actual local tarball bytes and their separately inspected content."""

    tarball: bytes
    content: ObservedContent


def _validate_npm_coordinates(
    spec: NpmFixtureSpec,
    repository_root: Path,
) -> None:
    request: dict[str, JsonValue] = {
        "package": spec.package,
        "version": spec.version,
    }
    # Only Node executable discovery and Windows loader essentials survive.
    environment = {
        name: os.environ[name]
        for name in ("PATH", "SystemRoot", "WINDIR")
        if name in os.environ
    }
    node = shutil.which("node", path=environment.get("PATH", os.defpath))
    if node is None:
        message = "official npm fixture parser requires Node"
        raise ValueError(message)
    try:
        completed = subprocess.run(  # noqa: S603
            (node, str(repository_root.resolve() / _PARSER_BRIDGE)),
            input=canonicalize(request),
            cwd=repository_root,
            env=environment,
            shell=False,
            check=True,
            capture_output=True,
            timeout=_PARSER_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.SubprocessError) as error:
        message = "official npm fixture coordinate parsing failed"
        raise ValueError(message) from error
    try:
        response = parse_json_strict(completed.stdout)
    except (TypeError, ValueError) as error:
        message = "official npm fixture coordinate response is invalid JSON"
        raise ValueError(message) from error
    _require(
        response == request,
        "official npm fixture coordinate response mismatch",
    )


def _manifest(spec: NpmFixtureSpec) -> dict[str, JsonValue]:
    return {
        "name": spec.package,
        "version": spec.version,
        "private": False,
        "repository": {
            "type": "git",
            "url": "git+https://github.com/hcoona/three.git",
        },
    }


def _witness(spec: NpmFixtureSpec) -> dict[str, JsonValue]:
    return {
        "schema": ACCEPTANCE_WITNESS_SCHEMA,
        "package": spec.package,
        "version": spec.version,
        "target": spec.target,
        "generation": spec.generation,
        "variant": spec.variant,
    }


def _pack(entries: dict[str, bytes]) -> bytes:
    payload = bytearray()
    for name, content in sorted(entries.items()):
        info = tarfile.TarInfo(name)
        info.size = len(content)
        header = bytearray(info.tobuf(format=tarfile.USTAR_FORMAT))
        # Match the existing strict reader's npm USTAR physical encoding.
        header[100:108] = b"000644 \0"
        header[108:124] = bytes(16)
        header[124:136] = f"{info.size:010o} \0".encode("ascii")
        header[136:148] = b"0000000000 \0"
        header[148:156] = b" " * 8
        header[329:345] = b"000000 \0" * 2
        header[148:156] = f"{sum(header):06o} \0".encode("ascii")
        payload.extend(header)
        payload.extend(content)
        payload.extend(bytes(-len(content) % tarfile.BLOCKSIZE))
    payload.extend(bytes(tarfile.BLOCKSIZE * 2))
    return gzip.compress(bytes(payload), compresslevel=9, mtime=0)


def build_npm_fixture(
    spec: NpmFixtureSpec,
    *,
    repository_root: Path,
) -> NpmFixture:
    """Construct deterministic local bytes, then inspect them independently.

    The checkout supplies the dedicated official-parser bridge and its locked
    Node packages. Missing tools or rejected coordinates fail closed.
    """
    tarball = _pack(
        {
            _MANIFEST_PATH: canonicalize(_manifest(spec)),
            _WITNESS_PATH: canonicalize(_witness(spec)),
        }
    )
    return NpmFixture(
        tarball=tarball,
        content=inspect_npm_fixture(tarball, repository_root=repository_root),
    )


def inspect_npm_fixture(
    tarball: bytes,
    *,
    repository_root: Path,
) -> ObservedContent:
    """Inspect only supplied bytes, with no expected fixture operand.

    Collectors must compare these observed facts against a separate expectation;
    a different but internally consistent fixture is not rejected here.
    """
    if type(tarball) is not bytes:
        message = "fixture tarball must be immutable bytes"
        raise TypeError(message)
    entries = _read_tarball(tarball, max_payload_bytes=_MAX_PAYLOAD_BYTES)
    _require(
        set(entries) == {_MANIFEST_PATH, _WITNESS_PATH},
        "fixture tarball entry closure mismatch",
    )
    witness = parse_canonical_json(entries[_WITNESS_PATH])
    _require(
        set(witness) == _WITNESS_FIELDS
        and witness["schema"] == ACCEPTANCE_WITNESS_SCHEMA,
        "fixture acceptance witness schema mismatch",
    )
    observed = NpmFixtureSpec(
        package=_string(witness["package"]),
        version=_string(witness["version"]),
        target=_string(witness["target"]),
        generation=_string(witness["generation"]),
        variant=_variant(witness["variant"]),
    )
    _require(
        canonicalize(parse_json_strict(entries[_MANIFEST_PATH]))
        == canonicalize(_manifest(observed)),
        "fixture manifest and acceptance witness closure mismatch",
    )
    _validate_npm_coordinates(observed, repository_root)
    return ObservedContent(
        version=observed.version,
        sha256="sha256:" + hashlib.sha256(tarball).hexdigest(),
        sha512="sha512:" + hashlib.sha512(tarball).hexdigest(),
        witness=entries[_WITNESS_PATH],
        target=observed.target,
    )
