"""Closed Python acceptance requests and original-byte evidence bundles."""

from __future__ import annotations

import io
import os
import re
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import cast
from urllib.parse import urlsplit

from packaging.version import Version

from three_workflow_delivery_v3.adapters.pypi import PythonRegistry
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_canonical_json,
    parse_json_strict,
)
from three_workflow_delivery_v3.repository.python_provider import (
    PYTHON_RELEASE_UNIT,
    python_digest,
    python_object,
    python_text,
)

WORKFLOW = ".github/workflows/workflow-delivery-v3-native-python-acceptance.yml"
SLOT_PATH = ".github/workflow-delivery/native/python-requests.json"
FIXTURE_KEYS = tuple(
    f"{target}/{candidate}/{variant}"
    for target in ("a", "b")
    for candidate in ("original", "comparison")
    for variant in ("wheel", "sdist")
)
MAX_BUNDLE_BYTES = 128 * 1024 * 1024
MAX_BUNDLE_FILES = 256


def require(condition: bool, message: str) -> None:  # noqa: FBT001 - assertion
    """Reject an unavailable acceptance condition without fallback."""
    if not condition:
        raise ValueError(message)


def digest(value: JsonValue) -> str:
    """Require an exact logical SHA-256 identity."""
    text = python_text(value)
    require(
        re.fullmatch(r"sha256:[0-9a-f]{64}", text) is not None,
        "invalid acceptance digest",
    )
    return text


def commit(value: JsonValue) -> str:
    """Require a complete immutable Git identity."""
    text = python_text(value)
    require(
        re.fullmatch(r"[0-9a-f]{40}", text) is not None,
        "invalid acceptance commit",
    )
    return text


def positive(value: JsonValue) -> int:
    """Require a positive non-Boolean platform ID."""
    require(
        type(value) is int and cast("int", value) > 0,
        "invalid acceptance platform ID",
    )
    return cast("int", value)


def path_name(name: str) -> str:
    """Admit normalized relative evidence members only."""
    path = PurePosixPath(name)
    require(
        bool(name)
        and not path.is_absolute()
        and path.as_posix() == name
        and not any(p in {".", ".."} for p in path.parts)
        and not any(c in name for c in "\\:\r\n"),
        "unsafe acceptance evidence path",
    )
    return name


@dataclass(frozen=True)
class NativeRequest:
    """One protected prospective generation; parsing grants no execution."""

    content: bytes

    def __post_init__(self) -> None:
        """Close destination, source, fixture and operator evidence bindings."""
        doc = python_object(
            parse_canonical_json(self.content),
            {
                "schema",
                "generation",
                "registry",
                "project",
                "profile-digest",
                "targets",
                "fixture-digests",
                "environment",
                "authorization",
                "configuration",
                "ownership",
            },
        )
        require(
            doc["schema"] == "workflow-delivery/v3/python-native-request"
            and doc["project"] == PYTHON_RELEASE_UNIT,
            "foreign Python acceptance request",
        )
        require(
            re.fullmatch(r"[0-9a-f]{32}", python_text(doc["generation"]))
            is not None,
            "invalid acceptance generation",
        )
        registry = PythonRegistry(python_text(doc["registry"]))
        require(
            doc["profile-digest"] == registry.profile_digest,
            "acceptance profile mismatch",
        )
        targets = python_object(doc["targets"], {"a", "b"})
        for item in targets.values():
            target = python_object(item, {"commit", "version"})
            commit(target["commit"])
            version = python_text(target["version"])
            parsed = Version(version)
            require(
                str(parsed) == version
                and parsed.local is None
                and parsed.is_prerelease,
                "acceptance needs a public smoke prerelease",
            )
        require(
            cast("dict", targets["a"])["commit"]
            != cast("dict", targets["b"])["commit"]
            and cast("dict", targets["a"])["version"]
            != cast("dict", targets["b"])["version"],
            "acceptance targets and versions must differ",
        )
        for value in python_object(
            doc["fixture-digests"], set(FIXTURE_KEYS)
        ).values():
            digest(value)
        environment = python_object(
            doc["environment"], {"id", "name", "sentinel"}
        )
        positive(environment["id"])
        require(
            environment["name"] == registry.environment,
            "acceptance Environment mismatch",
        )
        python_text(environment["sentinel"])
        for name in ("authorization", "configuration", "ownership"):
            evidence = python_object(doc[name], {"url", "digest"})
            url = urlsplit(python_text(evidence["url"]))
            require(
                url.scheme == "https"
                and bool(url.netloc)
                and url.username is None
                and url.password is None,
                "invalid operator evidence reference",
            )
            digest(evidence["digest"])

    @property
    def document(self) -> dict[str, JsonValue]:
        """Return the canonical protected request."""
        return parse_canonical_json(self.content)

    @property
    def registry(self) -> PythonRegistry:
        """Return the closed destination."""
        return PythonRegistry(python_text(self.document["registry"]))

    @property
    def digest(self) -> str:
        """Bind the exact protected request bytes."""
        return python_digest(self.content)

    def target(self, label: str) -> dict[str, JsonValue]:
        """Select one of the two exact source identities."""
        return cast(
            "dict[str, dict[str, JsonValue]]", self.document["targets"]
        )[label]


def load_request(
    root: Path, registry: str, expected_digest: str
) -> NativeRequest:
    """Read only the exact destination's protected non-null slot."""
    slots = python_object(
        parse_json_strict((root / SLOT_PATH).read_bytes()),
        {"testpypi", "pypi"},
    )
    require(
        registry in slots and slots[registry] is not None,
        "Python native request slot is disabled",
    )
    request = NativeRequest(canonicalize(slots[registry]))
    require(
        request.registry.name == registry and request.digest == expected_digest,
        "protected acceptance request mismatch",
    )
    return request


def write_exclusive(root: Path, name: str, content: bytes) -> None:
    """Persist one local fact before effects; runner loss remains ambiguous."""
    target = root / path_name(name)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("xb") as stream:
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())


def pack_bundle(files: dict[str, bytes]) -> bytes:
    """Bind each raw member without canonicalizing its original bytes."""
    require(
        0 < len(files) < MAX_BUNDLE_FILES and "manifest.json" not in files,
        "invalid acceptance bundle inventory",
    )
    require(
        sum(map(len, files.values())) <= MAX_BUNDLE_BYTES,
        "acceptance bundle too large",
    )
    manifest: dict[str, JsonValue] = {}
    buffer = io.BytesIO()
    with zipfile.ZipFile(
        buffer, "w", compression=zipfile.ZIP_STORED
    ) as archive:
        for name, content in sorted(files.items()):
            path_name(name)
            manifest[name] = {
                "bytes": len(content),
                "digest": python_digest(content),
            }
            archive.writestr(zipfile.ZipInfo(name), content)
        archive.writestr(
            zipfile.ZipInfo("manifest.json"), canonicalize(manifest)
        )
    return buffer.getvalue()


def unpack_bundle(content: bytes) -> dict[str, bytes]:
    """Verify complete member inventory, bounded original bytes and hashes."""
    require(len(content) <= MAX_BUNDLE_BYTES, "acceptance bundle too large")
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        infos = archive.infolist()
        names = [path_name(i.filename) for i in infos]
        require(
            len(names) == len(set(names))
            and 1 < len(names) <= MAX_BUNDLE_FILES
            and "manifest.json" in names
            and sum(i.file_size for i in infos) <= MAX_BUNDLE_BYTES,
            "invalid acceptance bundle members",
        )
        manifest = parse_canonical_json(archive.read("manifest.json"))
        require(
            set(manifest) == set(names) - {"manifest.json"},
            "acceptance bundle manifest mismatch",
        )
        result = {}
        for name, entry in manifest.items():
            data = archive.read(name)
            require(
                entry == {"bytes": len(data), "digest": python_digest(data)},
                "acceptance raw member digest mismatch",
            )
            result[name] = data
        return result
