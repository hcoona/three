"""Actual pinned Ruby equality and safe bounded index decoding."""

import gzip
import subprocess
from pathlib import Path

import pytest
from three_workflow_delivery_v3 import _ruby_registry as module
from three_workflow_delivery_v3._ruby_native import (
    RUBY_RELEASE_UNIT,
    ruby_environment,
    ruby_executable,
)
from three_workflow_delivery_v3._ruby_registry import ruby_registry_candidates

from ..ruby_registry_fixtures import VERSION, native_index


@pytest.mark.parametrize("encoding", ["json", "marshal"])
def test_ruby_native_index_preserves_all_equivalent_candidates(
    tmp_path, encoding
):
    """Native equality detects alternate spelling, platform and duplicates."""
    rows = [
        [RUBY_RELEASE_UNIT, VERSION, "ruby"],
        [RUBY_RELEASE_UNIT, VERSION + ".0", "ruby"],
        [RUBY_RELEASE_UNIT, VERSION, "x86_64-linux"],
        [RUBY_RELEASE_UNIT, VERSION, "ruby"],
        [RUBY_RELEASE_UNIT, "9.0.0", "ruby"],
        ["another-package", VERSION, "ruby"],
    ]
    arguments = {
        "json": lambda: {"entries": rows},
        "marshal": lambda: {"marshal": native_index(tmp_path, rows)},
    }
    candidates = ruby_registry_candidates(VERSION, **arguments[encoding]())
    assert candidates == tuple(
        {"name": name, "version": version, "platform": platform}
        for name, version, platform in rows[:4]
    )


@pytest.mark.parametrize("encoding", ["json", "marshal"])
def test_ruby_native_index_accepts_exact_entry_budget(tmp_path, encoding):
    """The last allowed entry participates in equality instead of truncation."""
    rows = [["another-package", "1.0", "ruby"]] * 4095
    rows.append([RUBY_RELEASE_UNIT, VERSION, "ruby"])
    arguments = {
        "json": lambda: {"entries": rows},
        "marshal": lambda: {"marshal": native_index(tmp_path, rows)},
    }
    assert ruby_registry_candidates(VERSION, **arguments[encoding]()) == (
        {"name": RUBY_RELEASE_UNIT, "version": VERSION, "platform": "ruby"},
    )


@pytest.mark.parametrize("encoding", ["json", "marshal"])
def test_ruby_native_index_rejects_excess_entries(tmp_path, encoding):
    """Bound the whole inventory even when every entry is irrelevant."""
    rows = [["another-package", "1.0", "ruby"]] * 4097
    arguments = {
        "json": lambda: {"entries": rows},
        "marshal": lambda: {"marshal": native_index(tmp_path, rows)},
    }
    with pytest.raises(ValueError, match="native registry reader rejected"):
        ruby_registry_candidates(VERSION, **arguments[encoding]())


@pytest.mark.parametrize(
    "entries",
    [
        {},
        [[RUBY_RELEASE_UNIT, VERSION]],
        [[RUBY_RELEASE_UNIT, VERSION, "ruby", "extra"]],
        [[RUBY_RELEASE_UNIT, 1, "ruby"]],
        [[RUBY_RELEASE_UNIT, VERSION + "+build", "ruby"]],
        [[RUBY_RELEASE_UNIT, " " + VERSION, "ruby"]],
        [[RUBY_RELEASE_UNIT, "invalid!", "ruby"]],
        [[RUBY_RELEASE_UNIT, VERSION, ""]],
        [[RUBY_RELEASE_UNIT, VERSION, None]],
        [["", VERSION, "ruby"]],
    ],
)
def test_ruby_native_json_index_rejects_malformed_entries(entries):
    """No malformed row can be silently skipped to manufacture absence."""
    with pytest.raises(ValueError, match="native registry reader rejected"):
        ruby_registry_candidates(VERSION, entries=entries)


@pytest.mark.parametrize(
    "payload",
    [
        b"not-gzip",
        gzip.compress(b"not-marshal", mtime=0),
        gzip.compress(b"x" * (2097152 + 1), mtime=0),
        b"x" * (2097152 + 1),
        b"",
    ],
    ids=[
        "not-gzip",
        "not-marshal",
        "expanded-limit",
        "compressed-limit",
        "empty",
    ],
)
def test_ruby_native_index_rejects_malformed_or_oversized_stream(payload):
    """Both compressed and expanded limits fail before returning candidates."""
    with pytest.raises(ValueError, match=r"reader rejected|byte limit"):
        ruby_registry_candidates(VERSION, marshal=payload)


@pytest.mark.parametrize(
    "mutation", ["truncated", "crc", "trailing", "second-stream"]
)
def test_ruby_native_index_requires_complete_single_gzip_stream(
    tmp_path, mutation
):
    """A valid first member cannot hide an incomplete or extra stream."""
    original = native_index(tmp_path, [])
    mutations = {
        "truncated": original[:-4],
        "crc": original[:-8] + b"\xff\xff\xff\xff" + original[-4:],
        "trailing": original + b"untrusted-trailer",
        "second-stream": original + original,
    }
    with pytest.raises(ValueError, match="native registry reader rejected"):
        ruby_registry_candidates(VERSION, marshal=mutations[mutation])


def test_ruby_native_safe_marshal_rejects_serialized_object_without_execution(
    tmp_path,
):
    """An index object with a load hook cannot create its requested file."""
    marker = tmp_path / "must-not-exist"
    script = r"""
class RegistryPayload
  def initialize(path); @path = path; end
  def _dump(_depth); @path; end
  def self._load(path); File.write(path, "executed"); new(path); end
end
STDOUT.binmode
STDOUT.write(Marshal.dump(RegistryPayload.new(ARGV.fetch(0))))
"""
    raw = subprocess.run(  # noqa: S603 - local fixture; not untrusted execution
        (str(ruby_executable()), "-e", script, str(marker)),
        cwd=tmp_path,
        env=ruby_environment(tmp_path / "fixture-home"),
        capture_output=True,
        check=True,
        timeout=30,
    ).stdout
    with pytest.raises(ValueError, match="native registry reader rejected"):
        ruby_registry_candidates(VERSION, marshal=gzip.compress(raw, mtime=0))
    assert not marker.exists()


@pytest.mark.parametrize("arguments", [{}, {"marshal": b"x", "entries": []}])
def test_ruby_native_index_requires_one_selected_protocol(arguments):
    """Ambiguous or absent input cannot choose a convenient reader."""
    with pytest.raises(ValueError, match="exactly one index format"):
        ruby_registry_candidates(VERSION, **arguments)


def test_ruby_native_reader_excludes_ambient_credentials(monkeypatch):
    """Native untrusted index parsing never inherits publisher credentials."""
    original_run = subprocess.run
    calls = []
    monkeypatch.setenv("GITHUB_TOKEN", "synthetic-secret-not-for-native-reader")
    monkeypatch.setenv("GEM_HOST_API_KEY", "synthetic-gem-secret")
    monkeypatch.setenv("RUBYOPT", "-runtrusted_injection")

    def capture(*args, **kwargs):
        calls.append((args, kwargs))
        return original_run(*args, **kwargs)

    monkeypatch.setattr(module.subprocess, "run", capture)
    assert ruby_registry_candidates(VERSION, entries=[]) == ()
    assert len(calls) == 1
    args, kwargs = calls[0]
    assert args[0][0] == str(ruby_executable())
    assert args[0][1] == str(module.RUBY_REGISTRY_HELPER)
    assert kwargs["timeout"] == 30  # noqa: PLR2004 - protocol budget
    assert set(kwargs["env"]) == {
        "HOME",
        "PATH",
        "GEM_HOME",
        "GEM_PATH",
        "LANG",
        "LC_ALL",
        "TZ",
        "TMPDIR",
        "TMP",
        "TEMP",
    }
    assert kwargs["env"]["PATH"] == ""
    assert Path(kwargs["env"]["HOME"]).parent == kwargs["cwd"]
