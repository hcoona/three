"""Native version comparison and safe registry index decoding."""

from __future__ import annotations

import subprocess
import tempfile
from pathlib import Path
from typing import cast

from three_workflow_delivery_v3._ruby_native import (
    RUBY_BYTE_LIMIT,
    RUBY_RELEASE_UNIT,
    ruby_digest,
    ruby_environment,
    ruby_executable,
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)

RUBY_REGISTRY_HELPER = Path(__file__).with_name("_ruby_registry_helper.rb")
RUBY_INDEX_ENTRY_LIMIT = 4096


def ruby_registry_reader_digest() -> str:
    """Bind the exact maintained native reader in the operation profile."""
    return ruby_digest(RUBY_REGISTRY_HELPER.read_bytes())


def ruby_registry_candidates(
    version: str, *, marshal: bytes | None = None, entries: JsonValue = None
) -> tuple[dict[str, JsonValue], ...]:
    """Compare every equivalent candidate without executing index data."""
    if (marshal is None) == (entries is None):
        message = "Ruby registry reader requires exactly one index format"
        raise ValueError(message)
    with tempfile.TemporaryDirectory(prefix="ruby-registry-read-") as temporary:
        root = Path(temporary)
        request: dict[str, JsonValue] = {"version": version}
        if marshal is not None:
            if not 0 < len(marshal) <= RUBY_BYTE_LIMIT:
                message = "Ruby registry index exceeds its byte limit"
                raise ValueError(message)
            path = root / "index.gz"
            path.write_bytes(marshal)
            request.update({"format": "marshal", "path": str(path)})
        else:
            request.update({"format": "json", "entries": entries})
        payload = canonicalize(request)
        if len(payload) > RUBY_BYTE_LIMIT:
            message = "Ruby registry reader request exceeds its byte limit"
            raise ValueError(message)
        result = subprocess.run(  # noqa: S603 - fixed reader; sanitized environment
            (str(ruby_executable()), str(RUBY_REGISTRY_HELPER)),
            input=payload,
            cwd=root,
            env=ruby_environment(root / "home"),
            capture_output=True,
            timeout=30,
            check=False,
        )
        if result.returncode or len(result.stdout) > RUBY_BYTE_LIMIT:
            message = "Ruby native registry reader rejected the index"
            raise ValueError(message)
    document = ruby_object(parse_json_strict(result.stdout), {"candidates"})
    candidates = document["candidates"]
    if (
        not isinstance(candidates, list)
        or len(candidates) > RUBY_INDEX_ENTRY_LIMIT
    ):
        message = "Ruby native registry reader returned an invalid inventory"
        raise ValueError(message)
    for item in candidates:
        candidate = ruby_object(item, {"name", "version", "platform"})
        if candidate["name"] != RUBY_RELEASE_UNIT:
            message = "Ruby native registry reader returned a foreign package"
            raise ValueError(message)
        ruby_text(candidate["version"])
        ruby_text(candidate["platform"])
    return tuple(cast("dict[str, JsonValue]", item) for item in candidates)
