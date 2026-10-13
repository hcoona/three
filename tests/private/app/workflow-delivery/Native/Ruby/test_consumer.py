"""Local native installer/API evidence without registry or live acceptance."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import time
from datetime import UTC, datetime
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parents[6]
CONTRACTS = ROOT / "src/private/app/workflow-delivery/Native/contracts"
HELPER = ROOT / "src/private/app/workflow-delivery/Native/Ruby/consumer.rb"
PRODUCT = (
    ROOT
    / "tests/private/app/workflow-delivery/fixtures/products"
    / "hcoona-release-smoke-ruby"
)
NAME = "hcoona-release-smoke-ruby"
FILES = (
    "LICENSE",
    "README.md",
    "lib/hcoona_release_smoke_ruby.rb",
    "lib/hcoona_release_smoke_ruby/version.rb",
)
UNSAFE = "UNSAFE-native-exception-URI-and-token"


def digest(path):
    """Bind the retained complete native original and cache."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Native:
    """Prepared runtime, fresh closed state and retained terminal receipts."""

    def __init__(self, root):
        """Select the already prepared maintained Ruby runtime."""
        self.root = root
        self.ruby = os.environ.get(
            "WORKFLOW_DELIVERY_TEST_RUBY"
        ) or shutil.which("ruby")
        assert self.ruby, "Prepare the maintained mise Ruby before testing."
        self.ruby = str(Path(self.ruby).resolve())
        self.gems = root / "installed gems"
        self.gems.mkdir()
        self.calls = []

    def run(self, arguments, *, cwd=None):
        """Run a separate child with EOF and a finite deadline."""
        state = self.root / f"command-{len(self.calls):02d}"
        state.mkdir()
        environment = {
            "PATH": str(Path(self.ruby).parent) + os.pathsep + os.defpath,
            "LANG": "C.UTF-8",
            "LC_ALL": "C.UTF-8",
        }
        for key in (
            "HOME",
            "USERPROFILE",
            "XDG_CONFIG_HOME",
            "XDG_CACHE_HOME",
            "XDG_DATA_HOME",
            "APPDATA",
            "LOCALAPPDATA",
            "GEM_SPEC_CACHE",
            "TMPDIR",
            "TMP",
            "TEMP",
        ):
            owned = state / key.lower()
            owned.mkdir()
            environment[key] = str(owned)
        environment["GEM_HOME"] = str(self.gems)
        environment["GEM_PATH"] = str(self.gems)
        argv = [self.ruby, *map(str, arguments)]
        directory = cwd or state
        started = time.monotonic()
        receipt = {
            "argv": argv,
            "cwd": str(directory),
            "environment": environment,
            "stdin": "DEVNULL",
            "timeout_seconds": 30,
            "started_utc": datetime.now(UTC).isoformat(),
        }
        try:
            result = subprocess.run(  # noqa: S603 - Owned local inputs.
                argv,
                cwd=directory,
                env=environment,
                stdin=subprocess.DEVNULL,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            receipt["exit_code"] = result.returncode
            receipt["termination"] = "exited"
            (state / "stdout.txt").write_text(result.stdout)
            (state / "stderr.txt").write_text(result.stderr)
        except subprocess.TimeoutExpired as error:
            receipt["exit_code"] = None
            receipt["termination"] = "timed-out"
            (state / "stdout.txt").write_bytes(error.stdout or b"")
            (state / "stderr.txt").write_bytes(error.stderr or b"")
            raise
        finally:
            receipt["duration_seconds"] = time.monotonic() - started
            receipt["finished_utc"] = datetime.now(UTC).isoformat()
            (state / "receipt.json").write_text(json.dumps(receipt, indent=2))
            self.calls.append(receipt)
        return result

    def build(self, version="1.2.3"):
        """Copy the real product, then use the maintained RubyGems builder."""
        source = self.root / "source product"
        source.mkdir()
        for relative in (*FILES[:3], f"{NAME}.gemspec"):
            target = source / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(PRODUCT / relative, target)
        version_file = source / FILES[3]
        version_file.parent.mkdir(parents=True, exist_ok=True)
        version_file.write_text(
            "module HcoonaReleaseSmokeRuby\n"
            f"  VERSION = {json.dumps(version)}\nend\n"
        )
        original = self.root / "original.gem"
        result = self.run(
            [
                "-S",
                "gem",
                "build",
                "--norc",
                f"{NAME}.gemspec",
                "--output",
                original,
            ],
            cwd=source,
        )
        assert result.returncode == 0, result.stderr
        assert original.is_file()
        assert original.stat().st_size > 0
        request = {
            "original": str(original),
            "version": version,
            "sha256": digest(original),
            "install_dir": str(self.gems),
        }
        # Preserve the builder's original before a negative case changes it.
        shutil.copyfile(original, self.root / "built-original.gem")
        (self.root / "built-original.json").write_text(
            json.dumps(request | {"size": original.stat().st_size}, indent=2)
        )
        return request

    def helper(self, operation, request):
        """Invoke the actual helper with an owned persisted request."""
        path = self.root / f"request-{len(self.calls):02d}.json"
        message = request | {"operation": operation}
        schema = json.loads(
            (CONTRACTS / "ruby-consumer-request.schema.json").read_text()
        )
        Draft202012Validator(schema).validate(message)
        path.write_text(json.dumps(message))
        return self.run([HELPER, path])


@pytest.fixture
def native(tmp_path):
    """Retain this base outside checkout, including passed originals."""
    return Native(tmp_path)


def success(result, version):
    """Portable facts cannot replace actual native process success."""
    assert result.returncode == 0, result.stderr
    assert result.stderr == ""
    schema = json.loads(
        (CONTRACTS / "ruby-consumer-answer.schema.json").read_text()
    )
    Draft202012Validator(schema).validate(json.loads(result.stdout))
    assert json.loads(result.stdout) == {
        "name": NAME,
        "version": version,
        "passed": True,
    }


def failure(result):
    """Native exception messages and stacks must not cross this boundary."""
    assert result.returncode == 1
    assert result.stdout == '{"passed":false}\n'
    assert result.stderr == ""
    assert UNSAFE not in result.stdout + result.stderr


@pytest.mark.parametrize("version", ["1.2.3", "1.2.3.pre.beta.4"])
def test_native_original_install_and_separate_clean_api(native, version):
    """Install the built original, bind cache and inspect API origin."""
    request = native.build(version)
    original = Path(request["original"])
    before = original.read_bytes()

    success(native.helper("install", request), version)
    success(native.helper("consume", request), version)

    cache = native.gems / "cache" / f"{NAME}-{version}.gem"
    assert cache.read_bytes() == before
    assert digest(cache) == request["sha256"]
    assert original.read_bytes() == before
    probe = native.run(
        [
            "-rjson",
            "-rdigest",
            "-e",
            (
                'abort "stdin was not EOF" unless STDIN.read.empty?; '
                'require "hcoona_release_smoke_ruby"; '
                f"spec = Gem.loaded_specs.fetch({json.dumps(NAME)}); "
                "puts JSON.generate({name: spec.name, "
                "version: spec.version.to_s, "
                "platform: spec.platform.to_s, "
                "origin: File.realpath(spec.full_gem_path), "
                "features: $LOADED_FEATURES.select { |p| "
                'p.end_with?("/hcoona_release_smoke_ruby.rb") || '
                'p.end_with?("/hcoona_release_smoke_ruby/version.rb") '
                "}.map { |p| File.realpath(p) }, "
                "id: HcoonaReleaseSmokeRuby.project_id, "
                "api_version: HcoonaReleaseSmokeRuby::VERSION, "
                "cache: Digest::SHA256.file(spec.cache_file).hexdigest})"
            ),
        ]
    )
    assert probe.returncode == 0, probe.stderr
    facts = json.loads(probe.stdout)
    assert facts["name"] == facts["id"] == NAME
    assert facts["version"] == facts["api_version"] == version
    assert facts["platform"] == "ruby"
    origin = Path(facts["origin"])
    assert origin.is_relative_to(native.gems)
    assert {str(origin / path) for path in FILES[2:]} == set(facts["features"])
    assert facts["cache"] == request["sha256"]
    child_calls = native.calls[1:]
    assert len({call["environment"]["HOME"] for call in child_calls}) == 3
    assert len({call["environment"]["TMPDIR"] for call in child_calls}) == 3
    for call in child_calls:
        assert not Path(call["cwd"]).is_relative_to(ROOT)
        assert call["stdin"] == "DEVNULL"
        assert call["timeout_seconds"] == 30
        assert call["environment"]["GEM_HOME"] == str(native.gems)
        assert call["environment"]["GEM_PATH"] == str(native.gems)
        for excluded in (
            "RUBY_SOURCE_USER",
            "RUBY_SOURCE_TOKEN",
            "GEM_HOST_API_KEY",
            "RUBYOPT",
            "RUBYLIB",
            "BUNDLE_GEMFILE",
            "HTTPS_PROXY",
        ):
            assert excluded not in call["environment"]


@pytest.mark.parametrize("change", ["bytes", "reported-digest"])
def test_changed_digest_prevents_install(native, change):
    """Hash is verified before native installation can create product state."""
    request = native.build()
    original = Path(request["original"])
    before = original.read_bytes()
    if change == "bytes":
        original.write_bytes(bytes([before[0] ^ 1]) + before[1:])
        assert original.stat().st_size == len(before)
    else:
        request["sha256"] = "0" * 64

    failure(native.helper("install", request))

    assert not list(native.gems.iterdir())
    assert digest(original) != request["sha256"]


@pytest.mark.parametrize(
    "defect",
    [
        "api-id",
        "api-version",
        "native-version",
        "cache",
        "origin",
        "import-origin",
        "missing-version-file",
    ],
)
def test_wrong_installed_facts_fail_cleanly(native, defect):
    """Refuse wrong installed API, native identity, cache and origins."""
    request = native.build()
    success(native.helper("install", request), request["version"])
    origin = native.gems / "gems" / f"{NAME}-{request['version']}"
    main = origin / FILES[2]
    version = origin / FILES[3]
    if defect == "api-id":
        main.write_text(main.read_text().replace(NAME, "another-product"))
    elif defect in {"api-version", "native-version"}:
        version.write_text(
            'module HcoonaReleaseSmokeRuby; VERSION = "9.8.7"; end\n'
        )
        if defect == "native-version":
            request["version"] = "9.8.7"
    elif defect == "cache":
        (native.gems / "cache" / f"{NAME}-1.2.3.gem").write_bytes(b"changed")
    elif defect == "origin":
        other = native.root / "unrelated install root"
        other.mkdir()
        request["install_dir"] = str(other)
    elif defect == "import-origin":
        outside = native.root / "outside-version.rb"
        shutil.copyfile(version, outside)
        version.unlink()
        version.symlink_to(outside)
    else:
        version.unlink()

    failure(native.helper("consume", request))

    assert digest(Path(request["original"])) == request["sha256"]


def test_wrong_install_identity_retains_failed_effect(native):
    """A native installer effect followed by wrong identity remains failure."""
    request = native.build()
    request["version"] = "9.8.7"

    failure(native.helper("install", request))

    assert (native.gems / "specifications" / f"{NAME}-1.2.3.gemspec").exists()
    assert (
        digest(native.gems / "cache" / f"{NAME}-1.2.3.gem") == request["sha256"]
    )


def test_existing_install_is_not_force_replaced(native):
    """Keep the native no-force/no-alternate-original boundary."""
    request = native.build()
    success(native.helper("install", request), request["version"])
    cache = native.gems / "cache" / f"{NAME}-1.2.3.gem"
    before = cache.read_bytes()

    failure(native.helper("install", request))

    assert cache.read_bytes() == before


@pytest.mark.parametrize(
    "defect",
    [
        "malformed",
        "missing-argument",
        "missing-file",
        "missing-version",
        "missing-root",
        "missing-operation",
        "unsupported",
        "missing-original",
        "corrupt-original",
    ],
)
def test_invalid_request_emits_only_fixed_failure(native, defect):
    """Invalid inputs never expose native exception details."""
    request = {
        "operation": "install",
        "version": "1.2.3",
        "install_dir": str(native.gems),
        "sha256": "0" * 64,
        "original": str(native.root / UNSAFE),
    }
    if defect == "corrupt-original":
        original = Path(request["original"])
        original.write_bytes(b"not a native gem")
        request["sha256"] = digest(original)
    elif defect == "unsupported":
        request["operation"] = UNSAFE
    elif defect in {"missing-version", "missing-root", "missing-operation"}:
        field = {
            "missing-version": "version",
            "missing-root": "install_dir",
            "missing-operation": "operation",
        }[defect]
        del request[field]
    path = native.root / "invalid-request.json"
    if defect != "missing-file":
        path.write_text("{" if defect == "malformed" else json.dumps(request))

    arguments = [HELPER] if defect == "missing-argument" else [HELPER, path]
    failure(native.run(arguments))

    assert not (
        native.gems / "specifications" / f"{NAME}-1.2.3.gemspec"
    ).exists()


def test_native_exception_is_omitted(native):
    """A product-controlled exception is reduced to the fixed failure answer."""
    request = native.build()
    success(native.helper("install", request), request["version"])
    installed = native.gems / "gems" / f"{NAME}-1.2.3" / FILES[2]
    installed.write_text(f"raise {json.dumps(UNSAFE)}\n")

    failure(native.helper("consume", request))
