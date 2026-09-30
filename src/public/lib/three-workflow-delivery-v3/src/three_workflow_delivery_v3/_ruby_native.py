"""Bounded unprivileged RubyGems mechanisms, without publication capability."""

from __future__ import annotations

import hashlib
import platform
import shutil
import subprocess
from pathlib import Path

from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)

RUBY_RELEASE_UNIT = "hcoona-release-smoke-ruby"
RUBY_IMPORT = "hcoona_release_smoke_ruby"
RUBY_ROOT = f"src/public/lib/{RUBY_RELEASE_UNIT}"
RUBY_MANIFEST = f"{RUBY_RELEASE_UNIT}.gemspec"
RUBY_BUILD_DEFINITION = "ruby/gem-v1"
RUBY_SOURCE_FILES = (
    "LICENSE",
    "README.md",
    RUBY_MANIFEST,
    f"lib/{RUBY_IMPORT}.rb",
)
RUBY_VERSION_PATH = f"lib/{RUBY_IMPORT}/version.rb"
RUBY_WITNESS_PATH = f"lib/{RUBY_IMPORT}/_workflow_delivery_provenance.json"
RUBY_PACKAGE_FILES = tuple(
    sorted(
        (
            "LICENSE",
            "README.md",
            f"lib/{RUBY_IMPORT}.rb",
            RUBY_VERSION_PATH,
            RUBY_WITNESS_PATH,
        )
    )
)
RUBY_VERSION = "4.0.7"
RUBYGEMS_VERSION = "4.0.20"
RUBY_HELPER = Path(__file__).with_name("_ruby_helper.rb")
RUBY_BYTE_LIMIT = 2 * 1024 * 1024
RUBY_MAX_EPOCH = 0xFFFFFFFF


def ruby_digest(content: bytes) -> str:
    """Identify original bytes without archive normalization."""
    return "sha256:" + hashlib.sha256(content).hexdigest()


def ruby_object(
    value: JsonValue, keys: set[str] | None = None
) -> dict[str, JsonValue]:
    """Read a bounded object, optionally closing its exact schema."""
    if not isinstance(value, dict) or (keys is not None and set(value) != keys):
        message = "invalid Ruby record fields"
        raise ValueError(message)
    return value


def ruby_text(value: JsonValue) -> str:
    """Require a nonempty uncoerced record string."""
    if not isinstance(value, str) or not value or value != value.strip():
        message = "invalid Ruby record string"
        raise ValueError(message)
    return value


def ruby_environment(home: Path, *, epoch: int | None = None) -> dict[str, str]:
    """Isolate native writable state and exclude credentials."""
    home.mkdir(parents=True, exist_ok=True)
    gems = home / "gems"
    gems.mkdir(exist_ok=True)
    env = {
        "HOME": str(home),
        "PATH": "",
        "GEM_HOME": str(gems),
        "GEM_PATH": str(gems),
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
        "TZ": "UTC",
    }
    for name in ("TMPDIR", "TMP", "TEMP"):
        env[name] = str(home)
    if epoch is not None:
        env["SOURCE_DATE_EPOCH"] = str(epoch)
    return env


def ruby_executable() -> Path:
    """Resolve the installed pinned Ruby lane."""
    ruby = shutil.which("ruby")
    if not ruby or platform.system() != "Linux":
        message = "Ruby smoke requires the pinned Ruby on Linux"
        raise ValueError(message)
    return Path(ruby).resolve()


def run_ruby(
    request: dict[str, JsonValue],
    *,
    cwd: Path,
    home: Path,
    epoch: int | None = None,
) -> dict[str, JsonValue]:
    """Invoke reviewed native code only in an unprivileged caller zone."""
    result = subprocess.run(  # noqa: S603 - fixed reviewed helper, no shell
        (str(ruby_executable()), str(RUBY_HELPER)),
        input=canonicalize(request),
        cwd=cwd,
        env=ruby_environment(home, epoch=epoch),
        capture_output=True,
        timeout=120,
        check=False,
    )
    if result.returncode or len(result.stdout) > RUBY_BYTE_LIMIT:
        message = "Ruby native operation failed: " + result.stderr[
            :4096
        ].decode("utf-8", errors="replace")
        raise ValueError(message)
    return ruby_object(parse_json_strict(result.stdout))


def ruby_profile(*, cwd: Path, home: Path) -> dict[str, JsonValue]:
    """Bind the actual native and compression identities to this operation."""
    return validate_ruby_profile(
        run_ruby({"operation": "profile"}, cwd=cwd, home=home)
    )


def validate_ruby_profile(value: JsonValue) -> dict[str, JsonValue]:
    """Validate imported native identities without running any target code."""
    profile = ruby_object(
        value,
        {
            "ruby",
            "rubygems",
            "ruby-platform",
            "ruby-revision",
            "zlib",
            "zlib-runtime",
        },
    )
    if (
        profile["ruby"] != RUBY_VERSION
        or profile["rubygems"] != RUBYGEMS_VERSION
    ):
        message = "Ruby native toolchain differs from the accepted pin"
        raise ValueError(message)
    for identity in profile.values():
        ruby_text(identity)
    return profile


def validate_ruby_specification(
    value: JsonValue, native: str
) -> dict[str, JsonValue]:
    """Require the closed exported smoke specification."""
    files: list[JsonValue] = list(RUBY_PACKAGE_FILES)
    expected: dict[str, JsonValue] = {
        "name": RUBY_RELEASE_UNIT,
        "version": native,
        "platform": "ruby",
        "files": files,
        "required-ruby": ">= 4.0",
    }
    if value != expected:
        message = "Ruby specification differs from the bounded smoke contract"
        raise ValueError(message)
    return expected


def materialize_ruby_version(root: Path, native: str) -> bytes:
    """Write the previously admitted static native version."""
    # JSON literals are valid Ruby for the admitted ASCII version grammar.
    literal = canonicalize(native).decode("ascii")
    content = (
        "# frozen_string_literal: true\n\nmodule HcoonaReleaseSmokeRuby\n"
        f"  VERSION = {literal}\nend\n"
    ).encode()
    path = root / RUBY_VERSION_PATH
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    path.chmod(0o644)
    return content
