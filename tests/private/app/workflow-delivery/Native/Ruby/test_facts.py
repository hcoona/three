"""Exercise delegated RubyGems/Bundler facts with real, local native inputs."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[6]
HELPER = ROOT / "src/private/app/workflow-delivery/Native/Ruby/facts.rb"
VERSIONS = ("2.4.20", "2.7.2")


@pytest.fixture(params=VERSIONS)
def native(request, tmp_path):
    """Use prepared locked libraries without personal configuration."""
    ruby = os.environ.get("WORKFLOW_DELIVERY_TEST_RUBY") or shutil.which("ruby")
    assert ruby, "The native Ruby runtime must be prepared before these tests."
    homes = json.loads(os.environ["WORKFLOW_DELIVERY_TEST_RUBY_GEM_HOMES"])
    version = request.param
    state = tmp_path / "native state"
    state.mkdir()
    for name in ("home", "config", "cache", "tmp"):
        (state / name).mkdir()
    environment = {
        "PATH": str(Path(ruby).parent) + os.pathsep + os.defpath,
        "HOME": str(state / "home"),
        "GEM_HOME": homes[version],
        "GEM_PATH": homes[version],
        "BUNDLE_APP_CONFIG": str(state / "config"),
        "BUNDLE_USER_HOME": str(state / "config"),
        "BUNDLE_USER_CACHE": str(state / "cache"),
        "BUNDLE_IGNORE_CONFIG": "1",
        "BUNDLE_FROZEN": "1",
        "TMPDIR": str(state / "tmp"),
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
    }
    return ruby, version, environment


def command(native, directory, arguments, *, environment=None):
    """Keep the native command outcome distinct from serialized answers."""
    ruby, _, defaults = native
    return subprocess.run(  # noqa: S603 - Explicit prepared runtime and owned fixture arguments.
        [ruby, *arguments],
        cwd=directory,
        env=defaults | (environment or {}),
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )


def facts(native, operation, path, *, environment=None):
    """Activate the requested library through RubyGems, then load the helper."""
    _, version, _ = native
    request = path.parent / "facts-request.json"
    request.write_text(json.dumps({"operation": operation, "path": str(path)}))
    return command(
        native,
        path.parent,
        [
            "-e",
            'gem "bundler", ARGV.shift; load ARGV.shift',
            version,
            str(HELPER),
            str(request),
        ],
        environment=environment,
    )


def answer(result):
    """Require successful native collection before parsing facts."""
    assert result.returncode == 0, result.stderr
    return json.loads(result.stdout)


def gem(directory, name, dependencies=()):
    """Create source metadata with native runtime dependency declarations."""
    directory.mkdir(parents=True)
    (directory / "lib").mkdir()
    (directory / "lib" / (name + ".rb")).write_text("VALUE = :native_fixture\n")
    declaration = (
        "Gem::Specification.new do |spec|\n"
        f"  spec.name = {json.dumps(name)}\n"
        '  spec.version = "1.2.3"\n'
        '  spec.summary = "Native facts fixture"\n'
        '  spec.authors = ["Fixture"]\n'
        '  spec.license = "MIT"\n'
        '  spec.files = Dir["lib/**/*.rb"]\n'
    )
    declaration += "".join(
        f"  spec.add_runtime_dependency {json.dumps(dependency)}, '= 1.2.3'\n"
        for dependency in dependencies
    )
    path = directory / (name + ".gemspec")
    path.write_text(declaration + "end\n")
    return path


def lock(native, gemfile, *, environment=None):
    """Let native Bundler generate the fixture lock."""
    _, version, defaults = native
    bundle = Path(defaults["GEM_HOME"]) / "bin/bundle"
    result = command(
        native,
        gemfile.parent,
        [str(bundle), "_" + version + "_", "lock", "--local"],
        environment={"BUNDLE_GEMFILE": str(gemfile), "BUNDLE_FROZEN": "0"}
        | (environment or {}),
    )
    assert result.returncode == 0, result.stderr
    return {"BUNDLE_GEMFILE": str(gemfile)} | (environment or {})


def test_source_metadata_is_native_and_not_a_test_inventory(native, tmp_path):
    """Ruby evaluates interpolation, dependencies and native file globs."""
    source = gem(tmp_path / "source gem", "fixture_a", ("fixture_b",))
    (source.parent / "spec").mkdir()
    (source.parent / "spec/required_spec.rb").write_text(
        "raise 'not package data'\n"
    )

    result = answer(facts(native, "gemspec", source))

    assert result["name"] == "fixture_a"
    assert result["version"] == "1.2.3"
    assert result["gemspec"] == str(source)
    assert result["runtime_dependencies"] == [
        {"name": "fixture_b", "requirement": "= 1.2.3"}
    ]
    assert result["files"] == ["lib/fixture_a.rb"]
    assert result["require_paths"] == ["lib"]


def test_nested_bundle_retains_transitive_original_path_identity(
    native, tmp_path
):
    """Retain A -> B -> C and original source paths in native selection."""
    root = tmp_path / "source tree with spaces"
    paths = {
        name: gem(root / name, name, dependencies)
        for name, dependencies in (
            ("fixture_a", ("fixture_b",)),
            ("fixture_b", ("fixture_c",)),
            ("fixture_c", ()),
        )
    }
    gemfile = root / "Gemfile"
    nested = root / "dependencies.rb"
    gemfile.write_text('eval_gemfile "dependencies.rb"\n')
    nested.write_text(
        "".join(f'gem "{name}", path: "{name}"\n' for name in paths)
    )
    environment = lock(native, gemfile)
    before = gemfile.with_name("Gemfile.lock").read_bytes()

    result = answer(facts(native, "bundle", gemfile, environment=environment))

    assert (
        result["bundler_version"]
        == result["locked_bundler_version"]
        == native[1]
    )
    assert set(result["gemfiles"]) == {str(gemfile), str(nested)}
    selected = {spec["name"]: spec for spec in result["specifications"]}
    for name, source in paths.items():
        assert selected[name]["source"]["kind"] == "path"
        assert selected[name]["source"]["path"] == str(source.parent)
        assert selected[name]["source"]["gemspec"] == str(source)
    assert selected["fixture_a"]["runtime_dependencies"] == [
        {"name": "fixture_b", "requirement": "= 1.2.3"}
    ]
    assert selected["fixture_b"]["runtime_dependencies"] == [
        {"name": "fixture_c", "requirement": "= 1.2.3"}
    ]
    assert selected["bundler"]["source"] == {"kind": "metadata"}
    assert gemfile.with_name("Gemfile.lock").read_bytes() == before


def test_native_group_selection_and_missing_local_source(native, tmp_path):
    """Native group context controls selection; required missing inputs fail."""
    source = gem(tmp_path / "required", "fixture_a")
    gem(tmp_path / "optional", "fixture_optional")
    gemfile = tmp_path / "Gemfile"
    gemfile.write_text(
        'gem "fixture_a", path: "required"\n'
        'group :optional do\n  gem "fixture_optional", path: "optional"\nend\n'
    )
    environment = lock(native, gemfile) | {"BUNDLE_WITHOUT": "optional"}

    result = answer(facts(native, "bundle", gemfile, environment=environment))

    assert result["direct_dependencies"] == ["fixture_a"]
    assert "fixture_optional" not in {
        s["name"] for s in result["specifications"]
    }
    source.unlink()
    failed = facts(native, "bundle", gemfile, environment=environment)
    assert failed.returncode != 0
    assert failed.stdout == ""


def test_active_local_git_override_uses_original_checkout_identity(
    native, tmp_path
):
    """Associate Bundler's local override with the original producer."""
    original = gem(tmp_path / "original git source", "fixture_a")
    _, _, defaults = native

    def git(directory, *arguments):
        executable = shutil.which("git", path=defaults["PATH"])
        assert executable, "The native Git executable must be prepared."
        result = subprocess.run(  # noqa: S603 - Explicit native Git and owned fixture arguments.
            [executable, *arguments],
            cwd=directory,
            env=defaults,
            text=True,
            capture_output=True,
            check=False,
            timeout=30,
        )
        assert result.returncode == 0, result.stderr

    git(original.parent, "init", "--initial-branch=main")
    git(original.parent, "add", ".")
    git(
        original.parent,
        "-c",
        "user.name=Fixture",
        "-c",
        "user.email=fixture@example.invalid",
        "commit",
        "-m",
        "Initialize native local fixture",
    )
    checkout = tmp_path / "active local checkout"
    git(tmp_path, "clone", str(original.parent), str(checkout))
    gemfile = tmp_path / "Gemfile"
    gemfile.write_text(
        'gem "fixture_a", git: '
        + json.dumps(str(original.parent))
        + ', branch: "main"\n'
    )
    environment = lock(
        native, gemfile, environment={"BUNDLE_LOCAL__FIXTURE_A": str(checkout)}
    )
    before = gemfile.with_name("Gemfile.lock").read_bytes()

    result = answer(facts(native, "bundle", gemfile, environment=environment))

    selected = {spec["name"]: spec for spec in result["specifications"]}
    assert selected["fixture_a"]["source"] == {
        "kind": "local_git",
        "path": str(checkout),
        "gemspec": str(checkout / "fixture_a.gemspec"),
        "directory": str(checkout),
    }
    assert gemfile.with_name("Gemfile.lock").read_bytes() == before


@pytest.mark.parametrize(
    "defect", ["unfrozen", "changed", "missing_lock", "wrong_lock"]
)
def test_invalid_bundle_context_cannot_return_facts(native, tmp_path, defect):
    """Refuse invalid context without rewriting the lock."""
    gem(tmp_path / "source", "fixture_a")
    gemfile = tmp_path / "Gemfile"
    gemfile.write_text('gem "fixture_a", path: "source"\n')
    environment = lock(native, gemfile)
    locked = gemfile.with_name("Gemfile.lock")
    if defect == "unfrozen":
        environment["BUNDLE_FROZEN"] = "0"
    elif defect == "changed":
        gemfile.write_text('gem "fixture_a", "= 9.9.9", path: "source"\n')
    elif defect == "missing_lock":
        locked.unlink()
    else:
        other = VERSIONS[1] if native[1] == VERSIONS[0] else VERSIONS[0]
        locked.write_text(locked.read_text().replace(native[1], other))
    before = locked.read_bytes() if locked.exists() else None

    result = facts(native, "bundle", gemfile, environment=environment)

    assert result.returncode != 0
    assert result.stdout == ""
    assert (locked.read_bytes() if locked.exists() else None) == before


def test_failed_gemspec_and_corrupt_archive_fail_collection(native, tmp_path):
    """Failed evaluation or a rejected archive cannot remove an owner."""
    source = tmp_path / "invalid.gemspec"
    source.write_text("raise 'required metadata unavailable'\n")
    archive = tmp_path / "corrupt.gem"
    archive.write_bytes(b"not a native gem archive")

    for operation, path in (("gemspec", source), ("archive", archive)):
        result = facts(native, operation, path)
        assert result.returncode != 0
        assert result.stdout == ""


def test_archive_metadata_and_clean_require_need_no_bundle_or_specs(
    native, tmp_path
):
    """Inspect and install native output without bundle or spec inputs."""
    source = gem(tmp_path / "source", "fixture_a")
    built = command(native, source.parent, ["-S", "gem", "build", str(source)])
    assert built.returncode == 0, built.stderr
    archive = source.parent / "fixture_a-1.2.3.gem"

    result = answer(facts(native, "archive", archive))

    assert result["name"] == "fixture_a"
    assert result["version"] == "1.2.3"
    assert result["contents"] == ["lib/fixture_a.rb"]
    installed = tmp_path / "clean gem home"
    installed.mkdir()
    environment = {"GEM_HOME": str(installed), "GEM_PATH": str(installed)}
    install = command(
        native,
        installed,
        ["-S", "gem", "install", "--local", "--no-document", str(archive)],
        environment=environment,
    )
    assert install.returncode == 0, install.stderr
    consumed = command(
        native,
        installed,
        [
            "-e",
            (
                'require "fixture_a"; puts VALUE; '
                'puts Gem.loaded_specs["fixture_a"].version'
            ),
        ],
        environment=environment,
    )
    assert consumed.returncode == 0, consumed.stderr
    assert consumed.stdout.splitlines() == ["native_fixture", "1.2.3"]


def test_current_smoke_gemspec_separates_native_and_legacy_package_membership(
    native, tmp_path
):
    """Native source/build need no witness; old package closure still does."""
    source = tmp_path / "source"
    shutil.copytree(ROOT / "src/public/lib/hcoona-release-smoke-ruby", source)
    version = source / "lib/hcoona_release_smoke_ruby/version.rb"
    version.parent.mkdir(parents=True, exist_ok=True)
    version.write_text(
        'module HcoonaReleaseSmokeRuby; VERSION = "1.2.3"; end\n'
    )
    witness = (
        source
        / "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json"
    )
    assert not witness.exists()
    gemspec = source / "hcoona-release-smoke-ruby.gemspec"
    normal = [
        "LICENSE",
        "README.md",
        "lib/hcoona_release_smoke_ruby.rb",
        "lib/hcoona_release_smoke_ruby/version.rb",
    ]
    evaluated = answer(facts(native, "gemspec", gemspec))
    assert sorted(evaluated["files"]) == normal
    assert evaluated["name"] == "hcoona-release-smoke-ruby"
    assert evaluated["version"] == "1.2.3"

    legacy_helper = ROOT / (
        "src/public/lib/three-workflow-delivery-v3/"
        "src/three_workflow_delivery_v3/_ruby_helper.rb"
    )
    request = tmp_path / "legacy-request.json"

    def legacy(operation, **fields):
        request.write_text(json.dumps({"operation": operation, **fields}))
        return command(
            native,
            source,
            [
                "-e",
                "STDIN.reopen(ARGV.shift); load ARGV.shift",
                str(request),
                str(legacy_helper),
            ],
        )

    assert (
        answer(legacy("specification", gemspec=str(gemspec)))["files"] == normal
    )
    legacy_archive = tmp_path / "legacy.gem"
    refused = legacy("build", gemspec=str(gemspec), output=str(legacy_archive))
    assert refused.returncode != 0
    assert "unsupported native specification" in refused.stderr
    assert not legacy_archive.exists()

    built = command(native, source, ["-S", "gem", "build", str(gemspec)])
    assert built.returncode == 0, built.stderr
    archive = source / "hcoona-release-smoke-ruby-1.2.3.gem"
    inspected = answer(facts(native, "archive", archive))
    assert sorted(inspected["contents"]) == normal
    assert not witness.exists()
    installed = tmp_path / "clean gem home"
    installed.mkdir()
    environment = {"GEM_HOME": str(installed), "GEM_PATH": str(installed)}
    install = command(
        native,
        installed,
        ["-S", "gem", "install", "--local", "--no-document", str(archive)],
        environment=environment,
    )
    assert install.returncode == 0, install.stderr
    consumed = command(
        native,
        installed,
        [
            "-e",
            (
                'require "hcoona_release_smoke_ruby"; '
                "puts HcoonaReleaseSmokeRuby.project_id; "
                "puts HcoonaReleaseSmokeRuby::VERSION; "
                'puts Gem.loaded_specs["hcoona-release-smoke-ruby"].version'
            ),
        ],
        environment=environment,
    )
    assert consumed.returncode == 0, consumed.stderr
    assert consumed.stdout.splitlines() == [
        "hcoona-release-smoke-ruby",
        "1.2.3",
        "1.2.3",
    ]
    # This fixture checks old file membership, not provenance-record semantics.
    witness.write_text("{}\n")
    historical_files = sorted([*normal, witness.relative_to(source).as_posix()])
    assert (
        answer(legacy("specification", gemspec=str(gemspec)))["files"]
        == historical_files
    )
    assert (
        answer(
            legacy("build", gemspec=str(gemspec), output=str(legacy_archive))
        )["files"]
        == historical_files
    )
    assert (
        sorted(answer(facts(native, "archive", legacy_archive))["contents"])
        == historical_files
    )


@pytest.mark.parametrize("native", ["2.4.20"], indirect=True)
@pytest.mark.parametrize("scenario", ["passed", "failed", "pending", "outside"])
def test_public_rspec_json_preserves_examples_and_outside_errors(
    native, tmp_path, scenario
):
    """Exercise the supported public CLI JSON formatter."""
    gemfile = tmp_path / "Gemfile"
    gemfile.write_text('gem "rspec", "~> 3.13"\n')
    environment = lock(native, gemfile)
    spec = tmp_path / "spec/native_spec.rb"
    spec.parent.mkdir()
    sources = {
        "passed": 'RSpec.describe "fixture" do\n'
        '  it("passes") { expect(3).to eq(3) }\nend\n',
        "failed": 'RSpec.describe "fixture" do\n'
        '  it("fails") { expect(3).to eq(4) }\nend\n',
        "pending": 'RSpec.describe "fixture" do\n'
        '  it("pending", pending: "native pending") do\n'
        "    expect(3).to eq(4)\n  end\nend\n",
        "outside": 'raise "native outside example error"\n',
    }
    spec.write_text(sources[scenario])
    output = tmp_path / "original-rspec-result.json"
    _, version, defaults = native
    bundle = Path(defaults["GEM_HOME"]) / "bin/bundle"

    result = command(
        native,
        tmp_path,
        [
            str(bundle),
            "_" + version + "_",
            "exec",
            "rspec",
            "--format",
            "json",
            "--out",
            str(output),
        ],
        environment=environment,
    )

    assert result.returncode == (
        1 if scenario in {"failed", "outside"} else 0
    ), result.stderr
    original = json.loads(output.read_text())
    summary = original["summary"]
    assert summary["example_count"] == (0 if scenario == "outside" else 1)
    assert summary["failure_count"] == (1 if scenario == "failed" else 0)
    assert summary["pending_count"] == (1 if scenario == "pending" else 0)
    assert summary["errors_outside_of_examples_count"] == (
        1 if scenario == "outside" else 0
    )
    if scenario == "outside":
        assert "native outside example error" in "\n".join(original["messages"])
        assert original["examples"] == []
    else:
        assert original["examples"][0]["status"] == scenario
        assert original["examples"][0]["id"] == "./spec/native_spec.rb[1:1]"
        if scenario == "pending":
            assert (
                original["examples"][0]["pending_message"] == "native pending"
            )
