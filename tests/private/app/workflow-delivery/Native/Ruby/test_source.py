"""Test actual Ruby helper composition with controlled public API answers."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import time
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[6]
CONTRACTS = ROOT / "src/private/app/workflow-delivery/Native/contracts"
HELPER = ROOT / "src/private/app/workflow-delivery/Native/Ruby/source.rb"
NAME = "hcoona-release-smoke-ruby"
BUDDY = "https://rubygems.pkg.github.com/hcoona"
OFFICIAL = "https://rubygems.org"
ORIGINAL = b"actual controlled native download"
USER = "read user:@/+?%&="
TOKEN = "synthetic token:@/+?%&= punctuation"  # noqa: S105 - Inert test value.
UNSAFE = "unsafe-native-exception-detail"

# Only public dependency methods are substituted, in this test-only preload.
# The helper main, Source constructor/list, identity/requirement/specification,
# owned-file validation, digest and serialization remain actual native code.
PRELOAD = r"""
require "json"
require "uri"
require "rubygems/source"
require "rubygems/source_list"
require "rubygems/name_tuple"
raise "stdin must be EOF" unless STDIN.read.empty?
scenario = ENV.fetch("TEST_SCENARIO")
trace_path = ENV.fetch("TEST_TRACE")
calls = []
at_exit { File.write(trace_path, JSON.generate(calls)) }
Gem::Source.prepend(Module.new do
  define_method(:load_specs) do |type|
    raise "not a real native Source" unless is_a?(Gem::Source)
    unless Gem.sources.is_a?(Gem::SourceList)
      raise "not the selected native SourceList"
    end
    raise "alternate source configured" unless Gem.sources.to_a == [uri.to_s]
    if (Gem.configuration.sources || []).include?("https://alternate.invalid")
      raise "unexpected inherited config"
    end
    expected_host = JSON.parse(File.read(ARGV.fetch(0))).fetch("host")
    selected = URI.parse(expected_host)
    unless uri.host == selected.host && uri.path == selected.path
      raise "wrong native host"
    end
    credential_ok = if uri.host == "rubygems.pkg.github.com"
      URI.decode_www_form_component(uri.user) ==
        ENV.fetch("RUBY_SOURCE_USER") &&
        URI.decode_www_form_component(uri.password) ==
          ENV.fetch("RUBY_SOURCE_TOKEN") &&
        uri.user.include?("%40") && uri.user.include?("%3A") &&
        uri.password.include?("%2B") && uri.password.include?("%25") &&
        !uri.user.include?(" ") && !uri.password.include?(" ")
    else
      uri.user.nil? && uri.password.nil?
    end
    raise "incorrect native credential encoding" unless credential_ok
    calls << { "method" => "load_specs", "type" => type.to_s,
      "source_id" => object_id, "selected_only" => true,
      "credential_ok" => credential_ok }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-#{type}"
    version = Gem::Version.new(ENV.fetch("TEST_VERSION"))
    tuple = Gem::NameTuple.new("hcoona-release-smoke-ruby", version, "ruby")
    # Native NameTuple, Version and Requirement own exact tuple interpretation.
    distractors = [Gem::NameTuple.new("another-package", version, "ruby"),
      Gem::NameTuple.new(tuple.name, Gem::Version.new("9.9.9"), "ruby"),
      Gem::NameTuple.new(tuple.name, version, "x86_64-linux")]
    matching_query = version.prerelease? ? :prerelease : :released
    return distractors if scenario == "absent" || type != matching_query
    return [tuple] * 65 if scenario == "too-many"
    if scenario == "duplicate"
      return [tuple, Gem::NameTuple.new(tuple.name, version, "ruby")]
    end
    [*distractors, tuple]
  end

  define_method(:fetch_spec) do |tuple|
    raise "not a native NameTuple" unless tuple.is_a?(Gem::NameTuple)
    requirement = Gem::Requirement.new("= #{ENV.fetch('TEST_VERSION')}")
    unless tuple.name == "hcoona-release-smoke-ruby" &&
      requirement.satisfied_by?(tuple.version) && tuple.platform == "ruby"
      raise "not exact native tuple"
    end
    calls << { "method" => "fetch_spec", "name" => tuple.name,
      "version" => tuple.version.to_s, "platform" => tuple.platform,
      "source_id" => object_id }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-spec"
    Gem::Specification.new do |spec|
      spec.name = scenario == "spec-name" ? "another-package" : tuple.name
      spec.version = scenario == "spec-version" ? "9.0.0" : tuple.version
      spec.platform = scenario == "spec-platform" ?
        "x86_64-linux" : tuple.platform
      spec.summary = "Controlled native source fixture"
      spec.authors = ["Fixture"]
    end
  end

  define_method(:download) do |spec, directory|
    raise "not a native Specification" unless spec.is_a?(Gem::Specification)
    request = JSON.parse(File.read(ARGV.fetch(0)))
    requested_directory = request.fetch("directory")
    unless directory == File.realpath(requested_directory)
      raise "wrong original destination"
    end
    calls << { "method" => "download", "name" => spec.name,
      "version" => spec.version.to_s, "platform" => spec.platform.to_s,
      "directory" => directory, "source_id" => object_id }
    raise "unsafe-native-exception-detail #{uri}" if scenario == "fail-download"
    path = File.join(directory, "native-original.gem")
    bytes = "actual controlled native download"
    bytes = "" if scenario == "empty-original"
    bytes = "x" * (8 * 1024 * 1024 + 1) if scenario == "oversized-original"
    bytes = "x" * (8 * 1024 * 1024) if scenario == "limit-original"
    if scenario == "outside-original"
      path = File.join(File.dirname(directory), "outside.gem")
    elsif scenario == "prefix-sibling"
      sibling = directory + "-other"
      Dir.mkdir(sibling)
      path = File.join(sibling, "original.gem")
    elsif scenario == "symlink-outside"
      outside = File.join(File.dirname(directory), "outside.gem")
      File.binwrite(outside, bytes)
      File.symlink(outside, path)
      return path
    elsif scenario == "missing-original"
      return path
    elsif scenario == "directory-original"
      Dir.mkdir(path)
      return path
    elsif scenario == "invalid-original"
      return nil
    end
    File.binwrite(path, bytes)
    path
  end
end)
"""


@pytest.fixture
def native(tmp_path):
    """Use prepared mise Ruby with fresh home/config/gem/cache/temp state."""
    ruby = os.environ.get("WORKFLOW_DELIVERY_TEST_RUBY") or shutil.which("ruby")
    assert ruby, "Prepare the maintained Ruby runtime before these tests."
    state = tmp_path / "native state"
    state.mkdir()
    environment = {
        "PATH": str(Path(ruby).parent) + os.pathsep + os.defpath,
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
    }
    for variable in (
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME",
        "XDG_DATA_HOME",
        "GEM_HOME",
        "GEM_PATH",
        "GEM_SPEC_CACHE",
        "TMPDIR",
        "TMP",
        "TEMP",
    ):
        path = state / variable.lower()
        path.mkdir()
        environment[variable] = str(path)
    # --norc ignores these values; selected sources must remain memory-only.
    (Path(environment["HOME"]) / ".gemrc").write_text(
        "---\n:sources:\n- https://alternate.invalid\n", encoding="utf-8"
    )
    preload = state / "public_source_answers.rb"
    preload.write_text(PRELOAD, encoding="utf-8")
    return ruby, state, environment, preload


def invoke(native, scenario, *, host=OFFICIAL, version="1.2.3", defect=None):
    """Run actual helper main with public method substitutions and EOF stdin."""
    ruby, state, environment, preload = native
    directory = state / "download"
    directory.mkdir()
    request = {
        "host": host,
        "name": NAME,
        "version": version,
        "directory": str(directory),
    }
    if defect == "host":
        request["host"] = "https://unselected.invalid"
    elif defect == "name":
        request["name"] = "another-package"
    elif defect == "version":
        request["version"] = "invalid native version!"
    elif defect == "directory":
        request["directory"] = str(state / "missing")
    path = state / "request.json"
    path.write_text(json.dumps(request), encoding="utf-8")
    trace_path = state / "calls.json"
    environment = environment | {
        "TEST_SCENARIO": scenario,
        "TEST_VERSION": version,
        "TEST_TRACE": str(trace_path),
    }
    if host == BUDDY:
        environment |= {"RUBY_SOURCE_USER": USER, "RUBY_SOURCE_TOKEN": TOKEN}
    arguments = [ruby, "-r", str(preload), str(HELPER), str(path)]
    assert USER not in json.dumps(request)
    assert TOKEN not in json.dumps(request)
    assert all(
        USER not in argument and TOKEN not in argument for argument in arguments
    )
    started = time.monotonic()
    result = subprocess.run(  # noqa: S603 - Prepared runtime and owned inputs.
        arguments,
        cwd=state,
        env=environment,
        input="",
        text=True,
        capture_output=True,
        check=False,
        timeout=30,
    )
    (state / "process.stdout.txt").write_text(result.stdout, encoding="utf-8")
    (state / "process.stderr.txt").write_text(result.stderr, encoding="utf-8")
    (state / "process.receipt.json").write_text(
        json.dumps(
            {
                "argv": arguments,
                "cwd": str(state),
                "exit": result.returncode,
                "duration_seconds": time.monotonic() - started,
                "timeout_seconds": 30,
                "stdin": "closed EOF",
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    trace = json.loads(trace_path.read_text(encoding="utf-8"))
    assert result.stderr == ""
    assert len(result.stdout.encode()) <= 256 * 1024
    for output in (result.stdout, result.stderr, json.dumps(trace)):
        assert USER not in output
        assert TOKEN not in output
        assert UNSAFE not in output
    value = json.loads(result.stdout)
    for name, message in (
        ("ruby-source-request", request),
        ("ruby-source-answer", value),
    ):
        schema = json.loads((CONTRACTS / (name + ".schema.json")).read_text())
        Draft202012Validator.check_schema(schema)
        Draft202012Validator(schema).validate(message)
    return result, value, trace, request


def assert_queries(trace):
    """Require both native complete inventories on the same selected Source."""
    assert [call["type"] for call in trace[:2]] == ["released", "prerelease"]
    assert all(call["method"] == "load_specs" for call in trace[:2])
    assert all(
        call["selected_only"] and call["credential_ok"] for call in trace[:2]
    )
    assert len({call["source_id"] for call in trace}) == 1


@pytest.mark.parametrize("host", [OFFICIAL, BUDDY])
@pytest.mark.parametrize("version", ["1.2.3", "1.2.3.pre.beta.7"])
def test_exact_native_tuple_uses_selected_public_source(native, host, version):
    """Public Source binds native identity, owned bytes and selected host."""
    result, answer, trace, request = invoke(
        native, "exact", host=host, version=version
    )

    assert result.returncode == 0
    assert_queries(trace)
    assert [call["method"] for call in trace] == [
        "load_specs",
        "load_specs",
        "fetch_spec",
        "download",
    ]
    original = Path(answer["path"])
    assert original.parent == Path(request["directory"])
    assert original.read_bytes() == ORIGINAL
    assert answer == {
        "state": "present",
        "name": NAME,
        "version": version,
        "platform": "ruby",
        "path": str(original),
        "size": len(ORIGINAL),
        "sha256": hashlib.sha256(ORIGINAL).hexdigest(),
    }
    assert trace[2] | {"source_id": 0} == {
        "method": "fetch_spec",
        "name": NAME,
        "version": version,
        "platform": "ruby",
        "source_id": 0,
    }
    assert trace[3] | {"source_id": 0} == {
        "method": "download",
        "name": NAME,
        "version": version,
        "platform": "ruby",
        "directory": request["directory"],
        "source_id": 0,
    }
    ruby, _, _, _ = native
    assert Path(ruby).is_absolute()
    assert not list(original.parent.glob("*.gemrc"))


@pytest.mark.parametrize("version", ["1.2.3", "1.2.3.pre.beta.7"])
def test_absence_requires_both_successful_inventories(native, version):
    """Wrong name/version/platform tuples cannot count as the requested gem."""
    result, answer, trace, request = invoke(native, "absent", version=version)

    assert result.returncode == 0
    assert answer == {"state": "absent"}
    assert_queries(trace)
    assert len(trace) == 2
    assert list(Path(request["directory"]).iterdir()) == []


@pytest.mark.parametrize(
    ("scenario", "last_method"),
    [
        ("fail-released", "load_specs"),
        ("fail-prerelease", "load_specs"),
        ("fail-spec", "fetch_spec"),
        ("fail-download", "download"),
        ("too-many", "load_specs"),
        ("duplicate", "load_specs"),
        ("spec-name", "fetch_spec"),
        ("spec-version", "fetch_spec"),
        ("spec-platform", "fetch_spec"),
        ("outside-original", "download"),
        ("prefix-sibling", "download"),
        ("symlink-outside", "download"),
        ("missing-original", "download"),
        ("directory-original", "download"),
        ("invalid-original", "download"),
        ("empty-original", "download"),
        ("oversized-original", "download"),
    ],
)
def test_native_failures_are_unknown_without_exception_diagnostics(
    native, scenario, last_method
):
    """Native errors and contradictory originals cannot become absence/exact."""
    result, answer, trace, _ = invoke(
        native, scenario, host=BUDDY, version="1.2.3.pre.beta.7"
    )

    assert result.returncode == 1
    assert answer == {"state": "unknown"}
    assert result.stdout == '{"state":"unknown"}\n'
    assert result.stderr == ""
    assert trace[-1]["method"] == last_method
    if scenario == "fail-released":
        assert [call["type"] for call in trace] == ["released"]
    else:
        assert_queries(trace)
    if scenario in {
        "fail-prerelease",
        "too-many",
        "duplicate",
        "spec-name",
        "spec-version",
        "spec-platform",
    }:
        assert not any(call["method"] == "download" for call in trace)


def test_original_at_size_limit_is_portable_present(native):
    """The original limit is inclusive and digest binds actual file bytes."""
    result, answer, trace, request = invoke(native, "limit-original")

    assert result.returncode == 0
    assert_queries(trace)
    assert answer["state"] == "present"
    assert answer["size"] == 8 * 1024 * 1024
    original = Path(answer["path"])
    assert original.parent == Path(request["directory"])
    assert answer["sha256"] == hashlib.sha256(original.read_bytes()).hexdigest()
    assert original.stat().st_size == answer["size"]


def test_punctuated_buddy_credentials_stay_in_memory(native):
    """Validate encoded in-memory URI without persisting user/token."""
    result, answer, trace, _ = invoke(native, "exact", host=BUDDY)

    assert result.returncode == 0
    assert answer["state"] == "present"
    assert_queries(trace)
    _, state, _, _ = native
    for path in state.rglob("*"):
        if path.is_file():
            persisted = path.read_text(encoding="utf-8")
            assert USER not in persisted
            assert TOKEN not in persisted
    assert len(list(state.rglob(".gemrc"))) == 1
    assert (state / "home" / ".gemrc").read_text(encoding="utf-8") == (
        "---\n:sources:\n- https://alternate.invalid\n"
    )


@pytest.mark.parametrize("defect", ["host", "name", "version", "directory"])
def test_invalid_request_is_unknown_without_native_calls(native, defect):
    """Unsupported request identity/state fails before Source reads."""
    result, answer, trace, _ = invoke(native, "exact", defect=defect)

    assert result.returncode == 1
    assert answer == {"state": "unknown"}
    assert trace == []
    assert result.stderr == ""
